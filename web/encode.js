// skafinity — the export encoder: PCM in, a compressed FILE out. Headless, no DOM.
//
// A song is ~75 s of 44.1 kHz stereo, which as raw PCM is ~13 MB — a download nobody asked for
// at that size. The browser already has an encoder (WebCodecs `AudioEncoder`), but it hands back
// encoded CHUNKS and no container, so the muxing is ours. That is what most of this file is.
//
// WEB-ONLY BY CHOICE. `Engine/` is untouched and s&box keeps writing WAV through `Wav.cs`; the
// two targets deliberately save different things, because only one of them has a browser's codecs.
//
// Two formats, picked at runtime by asking rather than by sniffing a user agent:
//   * FLAC — lossless, the file a listener would want, and the container is a concatenation
//     (see `flacFile`). Registered in the WebCodecs codec registry; whether any given browser
//     ENCODES it is a separate question, hence the probe.
//   * Opus at 256 kb/s — near-transparent for music and ~2.5 MB a song. Needs a real Ogg muxer.
// If neither is offered the caller falls back to the engine's WAV, so the button always does
// something.
//
// [SPEC] Read off the live specs on 2026-08-13 — these are documented contracts, not observed
// behaviour, but re-check them if a browser update breaks the output:
//   * WebCodecs (w3.org/TR/webcodecs/) — AudioEncoder/AudioData/EncodedAudioChunk;
//   * WebCodecs FLAC registration — codec string `flac`; `decoderConfig.description` is the
//     `fLaC` marker + STREAMINFO (+ optional metadata blocks); each chunk is one FLAC FRAME,
//     carrying neither the marker nor STREAMINFO. `AudioEncoderConfig.flac` = {blockSize,
//     compressLevel 0-8};
//   * WebCodecs Opus registration — codec string `opus`; chunks are raw Opus packets (RFC 6716);
//     `decoderConfig.description`, when present, is an RFC 7845 identification header;
//   * RFC 7845 (Ogg Opus) — OpusHead/OpusTags layout, granule positions at 48 kHz, pre-skip;
//   * Ogg framing (xiph.org/ogg/doc/framing.html) — page header, lacing, CRC.

// Opus at 256 kb/s stereo is where "smaller" stops buying anything audible. The bar for this
// export is "much less than 13 MB", not "smallest".
const OPUS_BITRATE = 256000;
// 20 ms is the Opus default and the size every decoder is happiest with; the muxer reads the real
// duration back off each chunk, so this number is not repeated anywhere else.
const OPUS_FRAME_US = 20000;
// Opus is defined at 48 kHz and everything about an Ogg Opus stream — granule positions, pre-skip
// — is counted there whatever the encoder was fed.
const OPUS_RATE = 48000;
// One AudioData per second of song: small enough that the encoder's queue never holds the whole
// song, big enough that a 75 s export is ~75 objects rather than thousands.
const BLOCK_SECONDS = 1;

const VENDOR = 'skafinity';

// ── Which format can this browser actually write? ─────────────────────────────
// Asked once per realm and cached, because the answer cannot change and the UI wants it early
// enough to LABEL the button with the extension it is going to produce.
let _pick = null;

const CANDIDATES = [
  { codec: 'flac', ext: 'flac', mime: 'audio/flac', label: 'FLAC',
    extra: { flac: { compressLevel: 8 } } },
  { codec: 'opus', ext: 'opus', mime: 'audio/ogg', label: 'Opus',
    extra: { bitrate: OPUS_BITRATE, opus: { application: 'audio', signal: 'music', complexity: 10, frameDuration: OPUS_FRAME_US } } },
];

/** The format this browser will encode, or null if it will not encode any of them (in which case
 *  the caller should fall back to WAV). Resolves to { codec, ext, mime, label, config(pcm) }. */
export function pickAudioFormat(sampleRate = 44100, channels = 2) {
  if (!_pick) _pick = probe(sampleRate, channels).catch(() => null);
  return _pick;
}

async function probe(sampleRate, channels) {
  if (typeof AudioEncoder === 'undefined' || !AudioEncoder.isConfigSupported) return null;
  for (const c of CANDIDATES) {
    // The engine renders at 44.1 kHz and Opus is a 48 kHz codec, so an encoder is within its
    // rights to refuse the engine's rate. Ask for it first and fall back to 48 kHz, which then
    // costs a resample on the way in (see `resample`).
    for (const rate of [sampleRate, OPUS_RATE]) {
      const config = { codec: c.codec, sampleRate: rate, numberOfChannels: channels, ...c.extra };
      let ok = false;
      try { ok = (await AudioEncoder.isConfigSupported(config)).supported === true; } catch { ok = false; }
      if (ok) return { codec: c.codec, ext: c.ext, mime: c.mime, label: c.label, sampleRate: rate, config };
      if (rate === OPUS_RATE) break;   // sampleRate === 48000 already: do not ask twice
    }
  }
  return null;
}

// ── PCM → encoded chunks ──────────────────────────────────────────────────────

/** Encode a rendered song and return the finished FILE bytes.
 *  `pcm` is { left, right, sampleRate } — the two Float32Arrays the engine renders.
 *  `fmt` comes from `pickAudioFormat`. `title` rides along as a tag where the container has
 *  somewhere to put one, so a saved file still says which seed it came from. */
export async function encodeSong(pcm, fmt, { title = '' } = {}) {
  const src = pcm.sampleRate === fmt.sampleRate ? pcm : await resample(pcm, fmt.sampleRate);
  const { chunks, decoderConfig } = await runEncoder(src, fmt.config);
  if (!chunks.length) throw new Error('the encoder produced nothing');
  return fmt.codec === 'flac'
    ? flacFile(decoderConfig && decoderConfig.description, chunks.map((c) => c.data))
    : oggOpusFile(chunks, { channels: 2, inputRate: pcm.sampleRate, head: decoderConfig && decoderConfig.description, title });
}

async function runEncoder(pcm, config) {
  const chunks = [];
  let decoderConfig = null;
  let failure = null;
  const enc = new AudioEncoder({
    output: (chunk, meta) => {
      if (!decoderConfig && meta && meta.decoderConfig) decoderConfig = meta.decoderConfig;
      const data = new Uint8Array(chunk.byteLength);
      chunk.copyTo(data);
      chunks.push({ data, timestamp: chunk.timestamp, duration: chunk.duration });
    },
    error: (e) => { failure = e; },
  });
  enc.configure(config);

  const frames = Math.min(pcm.left.length, pcm.right.length);
  const block = Math.max(1, Math.round(BLOCK_SECONDS * pcm.sampleRate));
  // f32-planar: the whole left channel then the whole right one, per AudioData.
  const scratch = new Float32Array(block * 2);
  for (let off = 0; off < frames && !failure; off += block) {
    const n = Math.min(block, frames - off);
    const buf = n === block ? scratch : new Float32Array(n * 2);
    buf.set(pcm.left.subarray(off, off + n), 0);
    buf.set(pcm.right.subarray(off, off + n), n);
    const data = new AudioData({
      format: 'f32-planar',
      sampleRate: pcm.sampleRate,
      numberOfFrames: n,
      numberOfChannels: 2,
      timestamp: Math.round((off / pcm.sampleRate) * 1e6),
      data: buf,
    });
    enc.encode(data);
    // AudioData holds its own copy of the samples until it is closed; 75 of them left open is 75
    // seconds of PCM the encoder cannot reclaim.
    data.close();
    // Backpressure. Without it the whole song lands in the encoder's queue at once, which is the
    // memory the chunked feed above was meant to avoid.
    while (enc.encodeQueueSize > 8 && !failure) await dequeued(enc);
  }
  if (!failure) await enc.flush();
  try { enc.close(); } catch { /* already closed by the error path */ }
  if (failure) throw failure;
  return { chunks, decoderConfig };
}

// `ondequeue` is the spec's signal that the queue drained a little. The timeout is a floor, not a
// poll: an implementation that never fires the event still makes progress, just in 4 ms steps.
function dequeued(enc) {
  return new Promise((resolve) => {
    let done = false;
    const fire = () => { if (!done) { done = true; enc.removeEventListener('dequeue', fire); resolve(); } };
    enc.addEventListener('dequeue', fire);
    setTimeout(fire, 4);
  });
}

// The engine renders at 44.1 kHz; if the encoder will only take 48 kHz, let the browser's own
// resampler do it rather than hand-rolling one — OfflineAudioContext is exactly this, at a
// quality no twenty lines here would match.
async function resample(pcm, rate) {
  if (typeof OfflineAudioContext === 'undefined')
    throw new Error(`this browser cannot resample ${pcm.sampleRate} Hz to ${rate} Hz`);
  const frames = Math.min(pcm.left.length, pcm.right.length);
  const out = Math.ceil((frames * rate) / pcm.sampleRate);
  const ctx = new OfflineAudioContext(2, out, rate);
  const buf = ctx.createBuffer(2, frames, pcm.sampleRate);
  buf.copyToChannel(pcm.left.subarray(0, frames), 0);
  buf.copyToChannel(pcm.right.subarray(0, frames), 1);
  const src = ctx.createBufferSource();
  src.buffer = buf;
  src.connect(ctx.destination);
  src.start();
  const done = await ctx.startRendering();
  return { left: done.getChannelData(0), right: done.getChannelData(1), sampleRate: rate };
}

// ── FLAC: the container is a concatenation ────────────────────────────────────

/** `fLaC` + STREAMINFO (which is what the encoder hands over as `description`) followed by the
 *  frames, in order. There is nothing else to a FLAC file. A description the encoder did not
 *  provide is not something we can invent — STREAMINFO carries the block size and bit depth the
 *  frames were written with — so that is an error rather than a guess. */
export function flacFile(description, frames) {
  const head = asBytes(description);
  if (!head || head.length < 4 || head[0] !== 0x66 || head[1] !== 0x4c || head[2] !== 0x61 || head[3] !== 0x43)
    throw new Error('the FLAC encoder gave no stream header');
  return concat([head, ...frames]);
}

// ── Ogg Opus: a real muxer ────────────────────────────────────────────────────

/** Wrap Opus packets in an Ogg stream. `chunks` are the EncodedAudioChunks' bytes plus their
 *  durations; `head` is the encoder's identification header if it gave one (it carries the real
 *  pre-skip, which nothing else here can know). */
export function oggOpusFile(chunks, { channels = 2, inputRate = 48000, head = null, title = '', serial = randomSerial() } = {}) {
  // Use the encoder's own OpusHead when there is one. Failing that, a pre-skip of 0 is the safe
  // direction to be wrong in: the encoder's few ms of priming silence get PLAYED rather than a
  // few ms of real audio getting trimmed off the front.
  if (!chunks.length) throw new Error('ogg: no audio packets');
  const id = asBytes(head) || opusHead(channels, 0, inputRate);
  const preSkip = id.length >= 12 ? id[10] | (id[11] << 8) : 0;

  const pages = [];
  const stream = { serial, seq: 0 };
  // The ID header is alone on the first page (BOS) and the comment header finishes its own page;
  // both carry granule 0. RFC 7845 requires exactly this.
  pages.push(oggPage(stream, [id], 0, 0x02));
  pages.push(oggPage(stream, [opusTags(title)], 0, 0x00));

  // A granule position is the total number of 48 kHz samples decodable at the end of the page,
  // pre-skip included — so the accumulator STARTS at the pre-skip rather than at zero, or the
  // stream would claim to be that many samples shorter than it is and the last packet would be
  // clipped.
  let granule = preSkip;
  let batch = [];
  let segments = 0;
  for (let i = 0; i < chunks.length; i++) {
    const c = chunks[i];
    const need = Math.floor(c.data.length / 255) + 1;
    // A page's segment table is one byte, so 255 lacing values is the hard ceiling.
    if (segments + need > 255) {
      pages.push(oggPage(stream, batch, granule, 0x00));
      batch = []; segments = 0;
    }
    batch.push(c.data);
    segments += need;
    granule += samplesAt48k(c.duration);
    const last = i === chunks.length - 1;
    if (last) pages.push(oggPage(stream, batch, granule, 0x04));   // end of stream
  }
  return concat(pages);
}

// Durations are microseconds on the input timeline; granule positions are 48 kHz samples. A 20 ms
// packet is 960 of them whatever rate the encoder was fed at.
function samplesAt48k(durationUs) {
  const us = Number.isFinite(durationUs) ? durationUs : OPUS_FRAME_US;
  return Math.round((us * OPUS_RATE) / 1e6);
}

function opusHead(channels, preSkip, inputRate) {
  const b = new Uint8Array(19);
  writeAscii(b, 0, 'OpusHead');
  b[8] = 1;                       // version
  b[9] = channels;
  le16(b, 10, preSkip);
  le32(b, 12, inputRate);         // informational: the rate the samples arrived at
  le16(b, 16, 0);                 // output gain, Q7.8 dB
  b[18] = 0;                      // channel mapping family 0 — mono/stereo, no mapping table
  return b;
}

function opusTags(title) {
  const enc = new TextEncoder();
  const vendor = enc.encode(VENDOR);
  const comments = [enc.encode(`ENCODER=${VENDOR}`)];
  if (title) comments.unshift(enc.encode(`TITLE=${title}`));
  let n = 8 + 4 + vendor.length + 4;
  for (const c of comments) n += 4 + c.length;
  const b = new Uint8Array(n);
  writeAscii(b, 0, 'OpusTags');
  let p = 8;
  le32(b, p, vendor.length); p += 4;
  b.set(vendor, p); p += vendor.length;
  le32(b, p, comments.length); p += 4;
  for (const c of comments) { le32(b, p, c.length); p += 4; b.set(c, p); p += c.length; }
  return b;
}

/** One Ogg page. Every packet handed in must fit in this page — the caller batches to keep the
 *  segment table under 255 entries, and an Opus packet is never near 255*255 bytes. */
function oggPage(stream, packets, granule, flags) {
  const lacing = [];
  let bodyLen = 0;
  for (const p of packets) {
    let n = p.length;
    while (n >= 255) { lacing.push(255); n -= 255; }
    // A packet whose length is a multiple of 255 ends on a 0, which is what tells the demuxer the
    // packet finished rather than continuing onto the next page.
    lacing.push(n);
    bodyLen += p.length;
  }
  if (lacing.length > 255) throw new Error('ogg: too many segments for one page');

  const page = new Uint8Array(27 + lacing.length + bodyLen);
  writeAscii(page, 0, 'OggS');
  page[4] = 0;                    // stream structure version
  page[5] = flags;
  le64(page, 6, granule);
  le32(page, 14, stream.serial);
  le32(page, 18, stream.seq++);
  // 22..25 is the checksum, computed over the whole page with those four bytes zero.
  page[26] = lacing.length;
  page.set(lacing, 27);
  let p = 27 + lacing.length;
  for (const pk of packets) { page.set(pk, p); p += pk.length; }
  le32(page, 22, oggCrc(page));
  return page;
}

// Ogg's CRC is the plain MSB-first CRC-32 over poly 0x04c11db7 with no input or output reflection
// and no final xor — NOT the reflected CRC-32 that zip and PNG use, so a stock crc32 is wrong here.
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let r = i << 24;
    for (let k = 0; k < 8; k++) r = (r & 0x80000000) ? ((r << 1) ^ 0x04c11db7) : (r << 1);
    t[i] = r >>> 0;
  }
  return t;
})();

export function oggCrc(bytes) {
  let crc = 0;
  for (let i = 0; i < bytes.length; i++) crc = ((crc << 8) ^ CRC_TABLE[((crc >>> 24) ^ bytes[i]) & 0xff]) >>> 0;
  return crc;
}

// ── Small bytes helpers ───────────────────────────────────────────────────────

function randomSerial() {
  const b = new Uint8Array(4);
  if (typeof crypto !== 'undefined' && crypto.getRandomValues) crypto.getRandomValues(b);
  else for (let i = 0; i < 4; i++) b[i] = (Math.random() * 256) | 0;
  return (b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24)) >>> 0;
}

function writeAscii(b, at, s) { for (let i = 0; i < s.length; i++) b[at + i] = s.charCodeAt(i); }
function le16(b, at, v) { b[at] = v & 0xff; b[at + 1] = (v >>> 8) & 0xff; }
function le32(b, at, v) { b[at] = v & 0xff; b[at + 1] = (v >>> 8) & 0xff; b[at + 2] = (v >>> 16) & 0xff; b[at + 3] = (v >>> 24) & 0xff; }
function le64(b, at, v) { le32(b, at, v >>> 0); le32(b, at + 4, Math.floor(v / 4294967296) >>> 0); }

function asBytes(x) {
  if (!x) return null;
  if (x instanceof Uint8Array) return x;
  if (ArrayBuffer.isView(x)) return new Uint8Array(x.buffer, x.byteOffset, x.byteLength);
  if (x instanceof ArrayBuffer) return new Uint8Array(x);
  return null;
}

function concat(parts) {
  let n = 0;
  for (const p of parts) n += p.length;
  const out = new Uint8Array(n);
  let at = 0;
  for (const p of parts) { out.set(p, at); at += p.length; }
  return out;
}
