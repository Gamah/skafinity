// Does the export muxer write a file the rest of the world can read?
//
// web/encode.js has two halves. The encoder half is the browser's (`AudioEncoder`) and there is
// nothing here that can run it — no node build of WebCodecs, no browser. The MUXER half is ours,
// it is pure bytes, and it is exactly the half where a wrong offset produces a file that opens in
// nothing and says nothing about why. So that is what this checks: an Ogg stream is demuxed back
// out, page by page, against the framing spec rather than against the code that wrote it.
//
// What it does NOT prove: that any browser encodes FLAC or Opus at all (`pickAudioFormat` answers
// that at runtime), that the Opus packets inside these pages are valid Opus, or that the resample
// path works — OfflineAudioContext is a browser API.
//
//   node test/encode.mjs        (part of `make test`)
import { oggOpusFile, flacFile, oggCrc } from '../web/encode.js';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}
const ascii = (b, at, n) => String.fromCharCode(...b.subarray(at, at + n));
const le32 = (b, at) => (b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24)) >>> 0;
const le16 = (b, at) => b[at] | (b[at + 1] << 8);

// ── An independent CRC, done the slow way ─────────────────────────────────────
// Bit at a time, straight off the framing spec (poly 0x04c11db7, init 0, no reflection, no final
// xor). Its whole job is to be written differently from the table in encode.js.
function slowCrc(bytes) {
  let crc = 0;
  for (const b of bytes) {
    crc = (crc ^ (b << 24)) >>> 0;
    for (let k = 0; k < 8; k++) crc = ((crc & 0x80000000) ? ((crc << 1) ^ 0x04c11db7) : (crc << 1)) >>> 0;
  }
  return crc >>> 0;
}
{
  const sample = Uint8Array.from([...'123456789'].map((c) => c.charCodeAt(0)));
  check('the table CRC matches a bit-at-a-time one', oggCrc(sample) === slowCrc(sample),
    `0x${oggCrc(sample).toString(16)} vs 0x${slowCrc(sample).toString(16)}`);
  check('CRC of nothing is 0', oggCrc(new Uint8Array(0)) === 0);
}

// ── Demuxer: pages out of a byte stream, packets out of the lacing ────────────
function demux(file) {
  const pages = [];
  let at = 0;
  while (at < file.length) {
    if (ascii(file, at, 4) !== 'OggS') throw new Error(`no capture pattern at ${at}`);
    const nseg = file[at + 26];
    const lacing = file.subarray(at + 27, at + 27 + nseg);
    let bodyLen = 0;
    for (const l of lacing) bodyLen += l;
    const total = 27 + nseg + bodyLen;
    const raw = file.subarray(at, at + total);
    // The checksum covers the whole page with its own four bytes zeroed.
    const zeroed = raw.slice();
    zeroed[22] = zeroed[23] = zeroed[24] = zeroed[25] = 0;
    const body = raw.subarray(27 + nseg);
    const packets = [];
    let p = 0, run = 0;
    for (const l of lacing) {
      run += l;
      if (l < 255) { packets.push(body.subarray(p, p + run)); p += run; run = 0; }
    }
    pages.push({
      version: raw[4],
      flags: raw[5],
      granule: le32(raw, 6) + le32(raw, 10) * 4294967296,
      serial: le32(raw, 14),
      seq: le32(raw, 18),
      crcOk: le32(raw, 22) === slowCrc(zeroed),
      lacing: Array.from(lacing),
      packets,
      continued: run > 0,
    });
    at += total;
  }
  return pages;
}

// ── A stand-in for the encoder's output ───────────────────────────────────────
// 20 ms Opus packets are a few hundred bytes; the CONTENT is opaque to the muxer, so these are
// just numbered bytes that a mis-copied offset would scramble visibly.
const packet = (len, seed) => Uint8Array.from({ length: len }, (_, i) => (i + seed) & 0xff);
const chunkList = (count, len) =>
  Array.from({ length: count }, (_, i) => ({ data: packet(len, i), duration: 20000, timestamp: i * 20000 }));

const SERIAL = 0x5ca11017;

// ── The headers ───────────────────────────────────────────────────────────────
{
  const pages = demux(oggOpusFile(chunkList(3, 300), { channels: 2, inputRate: 44100, title: 'ska:7:3', serial: SERIAL }));
  const id = pages[0].packets[0];
  check('the first page is BOS and carries the ID header alone',
    (pages[0].flags & 0x02) !== 0 && pages[0].packets.length === 1);
  check('the ID header is an OpusHead', ascii(id, 0, 8) === 'OpusHead' && id.length === 19);
  check('OpusHead says version 1, 2 channels, mapping family 0', id[8] === 1 && id[9] === 2 && id[18] === 0);
  check('OpusHead records the rate the samples arrived at', le32(id, 12) === 44100);
  check('the header pages carry granule 0', pages[0].granule === 0 && pages[1].granule === 0);

  const tags = pages[1].packets[0];
  check('the second page is the comment header', ascii(tags, 0, 8) === 'OpusTags');
  const vendorLen = le32(tags, 8);
  check('the vendor string is skafinity', ascii(tags, 12, vendorLen) === 'skafinity');
  const rest = String.fromCharCode(...tags.subarray(12 + vendorLen + 4));
  check('the seed rides along as a TITLE tag', rest.includes('TITLE=ska:7:3'), rest);

  check('every page checksums', pages.every((p) => p.crcOk));
  check('the serial number is one stream', pages.every((p) => p.serial === SERIAL));
  check('page sequence numbers count from 0', pages.every((p, i) => p.seq === i));
  check('the version byte is 0', pages.every((p) => p.version === 0));
  check('the last page is EOS and nothing follows it',
    (pages[pages.length - 1].flags & 0x04) !== 0 && pages.slice(0, -1).every((p) => (p.flags & 0x04) === 0));
}

// ── The packets survive the framing ───────────────────────────────────────────
{
  const chunks = chunkList(40, 317);
  const pages = demux(oggOpusFile(chunks, { serial: SERIAL }));
  const out = pages.slice(2).flatMap((p) => p.packets);
  check('every packet comes back out', out.length === chunks.length);
  check('every packet comes back byte-for-byte',
    out.every((p, i) => p.length === chunks[i].data.length && p.every((b, j) => b === chunks[i].data[j])));
  check('no packet is left continuing past the end of a page', pages.every((p) => !p.continued));
}

// ── Lacing: the 255 rule ──────────────────────────────────────────────────────
{
  // A packet whose length is a multiple of 255 has to end on an explicit 0, or the demuxer reads
  // it as continuing into the next packet.
  const pages = demux(oggOpusFile([{ data: packet(510, 1), duration: 20000 }], { serial: SERIAL }));
  check('a 510-byte packet laces as 255,255,0', String(pages[2].lacing) === '255,255,0');
  const back = pages[2].packets[0];
  check('and still reads back as one 510-byte packet', pages[2].packets.length === 1 && back.length === 510);
}

// ── Pages stay inside the 255-segment table ───────────────────────────────────
{
  // 700-byte packets need 3 lacing values each, so 85 fit in a page (255) and the 86th must start
  // a new one. 200 packets is enough to make that happen more than once.
  const chunks = chunkList(200, 700);
  const pages = demux(oggOpusFile(chunks, { serial: SERIAL }));
  check('no page exceeds 255 segments', pages.every((p) => p.lacing.length <= 255),
    String(pages.map((p) => p.lacing.length)));
  check('audio spans several pages', pages.length > 4, `${pages.length} pages`);
  check('every packet still comes back', pages.slice(2).flatMap((p) => p.packets).length === chunks.length);
}

// ── Granule positions ─────────────────────────────────────────────────────────
{
  const chunks = chunkList(50, 700);
  const pages = demux(oggOpusFile(chunks, { serial: SERIAL }));
  const audio = pages.slice(2);
  check('granule positions only ever go up',
    audio.every((p, i) => i === 0 || p.granule > audio[i - 1].granule));
  // 20 ms at 48 kHz is 960 samples per packet, whatever rate the encoder was fed at, and pre-skip
  // is 0 when the encoder gave no header of its own.
  check('the final granule is the whole stream in 48 kHz samples',
    audio[audio.length - 1].granule === 50 * 960, String(audio[audio.length - 1].granule));

  // With a real encoder's OpusHead the pre-skip comes from it, and the granule accumulator has to
  // START there — otherwise the stream claims to be shorter than it is and the tail is clipped.
  const head = new Uint8Array(19);
  head.set([...'OpusHead'].map((c) => c.charCodeAt(0)), 0);
  head[8] = 1; head[9] = 2; head[10] = 312 & 0xff; head[11] = 312 >> 8;
  const withSkip = demux(oggOpusFile(chunks, { head, serial: SERIAL }));
  check('the encoder\'s own OpusHead is used verbatim', le16(withSkip[0].packets[0], 10) === 312);
  check('pre-skip is added to the granule positions',
    withSkip[withSkip.length - 1].granule === 312 + 50 * 960);
}

// ── FLAC: the container is a concatenation, and a missing header is fatal ─────
{
  const head = Uint8Array.from([0x66, 0x4c, 0x61, 0x43, 0, 0, 0, 34, ...new Array(34).fill(7)]);
  const frames = [packet(100, 1), packet(120, 2)];
  const file = flacFile(head, frames);
  check('a FLAC file is its header then its frames', file.length === head.length + 220 &&
    ascii(file, 0, 4) === 'fLaC' && file[head.length] === 1);

  let threw = '';
  try { flacFile(null, frames); } catch (e) { threw = e.message; }
  check('no STREAMINFO is an error, not a guess', /stream header/.test(threw), threw);
  threw = '';
  try { flacFile(Uint8Array.from([1, 2, 3, 4, 5]), frames); } catch (e) { threw = e.message; }
  check('a description that is not FLAC is refused', /stream header/.test(threw), threw);
}

console.log(failures ? `\n${failures} failure(s)` : '\nall encode checks passed');
process.exit(failures ? 1 : 0);
