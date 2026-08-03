// skafinity — Web Audio sequencer + vibe editor + rolling playlist + WAV export.
// Port of MusicController's scheduling (not its s&box plumbing). The heavy synthesis runs
// in worker.js; this file owns the AudioContext, crossfade scheduling, and the UI.
import Skafinity from './engine.js';
import { GenQueue } from './queue.js';

// ── Tunables (mirror MusicController defaults) ──
// Songs now have an intro→…→ending structure, so each plays once start-to-end (no internal
// loop) and crossfades into the next.
const LOOPS_PER_SONG = 1;
// Crossfade length, in seconds. This is a CEILING, not the value used: the real fade is capped
// to the song's ring-out tail (mod.ringOutSeconds()), because a fade longer than the tail starts
// the next song before the current one's final chord has landed — two songs, two tempos and two
// downbeats playing at once. It is also the first song's fade-in.
const CROSSFADE = 3.75;
const AHEAD_COUNT = 4;        // songs kept pre-rendered
const SCHEDULE_HORIZON = 12;  // seconds: schedule the next song once it's within this
// Pool of generation workers. Each boots its own .NET runtime (memory cost — hence the cap),
// and the pool lets look-ahead songs render in parallel instead of serializing through one
// worker. A seed change terminates whichever workers are mid-render (true abort) and lets an
// already-booted idle worker pick up the new seed with no reboot.
const POOL_SIZE = 3;

let mod = null;               // main-thread WASM (vibe/codec/export — light calls)
// Seconds of ring-out a song reserves past its last bar; read from the engine at boot (see
// CROSSFADE). The fallback only covers the window before the runtime is up.
let tailSeconds = 2.5;
let pool = [];                // [{ worker, busy, n }]
// Which songs are queued or rendering. Every drop of queued work goes through this object so the
// claims go with it — see web/queue.js for why that is the whole ballgame.
const gen = new GenQueue();

// ── State ──
let cfg = null;               // Float32Array — the live Config (vibe applied)
let genre = 0;                // current genre index (mirrors cfg's genre)
let tag = 'rotaliate';
let n = 0;                    // next song index to schedule
let displayN = 0;            // song currently audible (for UI)
let vibe = '';
// Per-instrument volumes, keyed by voice NAME (BASS, DRUMS, …) so a level follows the
// instrument across genres. Pulled out of the (shareable) vibe seed; a local preference
// persisted to localStorage and overlaid onto cfg after every seed/genre change.
const VOLS_KEY = 'skafinity.vol';
let vols = loadVols();
function loadVols() { try { return JSON.parse(localStorage.getItem(VOLS_KEY)) || {}; } catch (_) { return {}; } }
function saveVols() { try { localStorage.setItem(VOLS_KEY, JSON.stringify(vols)); } catch (_) {} }
// Overlay an optional web/config.json onto the base cfg: the baseline-mix tuning (peak
// balances, kit presence) that used to be hardcoded consts. Edit the file + reload to retune
// the house mix — no rebuild. Shape: { "advanced": { "TomBalance": 0.78, ... } } (or a flat
// { name: value } map). Missing file / bad JSON / unknown keys are ignored silently.
async function applyHouseConfig() {
  try {
    const res = await fetch('./config.json', { cache: 'no-store' });
    if (!res.ok) return;
    const data = await res.json();
    const advanced = (data && typeof data.advanced === 'object') ? data.advanced : data;
    cfg = mod.applyAdvancedConfig(cfg, advanced);
  } catch (_) { /* no config.json (or invalid) — keep engine defaults */ }
}
// Overlay the stored per-voice volumes onto cfg for the current genre (voices without a saved
// level keep the song default). Call after building cfg from a vibe/genre.
function applyStoredVolumes() {
  for (const f of genreFields()) {
    if (f.column === 0 && f.voice && vols[f.voice] !== undefined)
      cfg = mod.setVibeField(cfg, f.i, vols[f.voice]);
  }
}
// 🎲 Shuffle ("random every song"): each new song renders with a freshly re-rolled vibe — a fresh
// genre AND every non-volume knob (per-voice volumes are a local mix preference and never ride in
// the seed). Each upcoming index caches its own re-rolled cfg so the look-ahead renders and the
// audible song stay consistent.
//
// ON by default, matching SkafinityPlayer.RandomEverySong — endless variety out of the box, which
// is the point of an infinite station. An explicit OFF is remembered; an absent setting means the
// visitor has never touched it and gets the default.
const SHUFFLE_KEY = 'skafinity.shuffle';
let randomEverySong = (() => { try { return localStorage.getItem(SHUFFLE_KEY) !== '0'; } catch (_) { return true; } })();
function saveShuffle() { try { localStorage.setItem(SHUFFLE_KEY, randomEverySong ? '1' : '0'); } catch (_) {} }
// ── Navigable timeline (mirrors SkafinityPlayer's seed ledger + PCM cache, issue #14) ──
// `ledger`: n -> the cfg song n was/will be rendered with. Under shuffle that cfg is DERIVED from
// the seed (mod.rollVibeFor over "{tag}:vibe:{n}"), so the ledger is a cache and nothing more —
// dropping an entry re-derives the identical vibe. That is what makes the shuffled line walkable
// in both directions, shareable, and stable across a reload: the sequence genuinely is the seed.
// Outside shuffle, songs aren't pinned (they track the live cfg).
const ledger = new Map();        // n -> frozen cfg (shuffle line)
// Radius of the rendered-PCM cache kept around the audible song so Prev/Next within the window is
// instant; anything further is dropped and regenerated from the ledger on demand.
const PCM_RADIUS = 5;
let bufferingN = -1;             // song a manual seek is waiting on (playback stalled), or -1
// The cfg to render song `nn` with: the shared live cfg normally; a per-index frozen roll under
// shuffle (pinned in the ledger so the look-ahead AND a later revisit stay identical).
function cfgForN(nn) {
  if (ledger.has(nn)) return ledger.get(nn);
  if (!randomEverySong) return cfg;
  const c = mod.rollVibeFor(cfg, tag, nn);
  ledger.set(nn, c);
  return c;
}
// The genre song nn resolves to, for the queue view. Under shuffle every upcoming genre is
// knowable up front (the line is derived, not rolled on arrival), so the queue shows what is
// actually coming rather than the live genre as a placeholder.
function genreForN(nn) {
  const c = ledger.get(nn) || (mod && randomEverySong ? cfgForN(nn) : null);
  return c ? mod.getGenre(c) : genre;
}
// Drop rendered PCM outside the ±PCM_RADIUS window around `center` (the audible song by default;
// the seek target while a jump is still buffering). The ledger of frozen vibes is kept regardless.
function pruneCache(center = displayN) {
  for (const k of rendered.keys()) if (Math.abs(k - center) > PCM_RADIUS) rendered.delete(k);
}
// Two counters, because a seek and a restart invalidate different things. `seq` is the AUDIO
// SCHEDULE: bumped whenever committed nodes are torn down, so stale setTimeouts bail out.
// `renderSeq` is what a rendered song was rendered FOR: bumped only when the cfg behind an index
// changes (a new seed/vibe/genre, or shuffle re-rolling the line), because that is the only thing
// that can make an in-flight render wrong. A seek moves where we are in a timeline whose vibes are
// pinned per index — the render in flight is still the right song — so tying them together threw
// away up to POOL_SIZE songs of work on every Prev/Next and re-rendered them immediately after.
let seq = 0;
let renderSeq = 0;
let playing = false;

let ctx = null, masterGain = null;
const rendered = new Map();   // n -> { buffer, info }
let nextN = 0;               // next index to schedule into the timeline
let nextTime = 0;             // ctx time the next song starts
let firstScheduled = false;
let activeNodes = [];         // live source/gain nodes for the current sequence
let reqId = 0;                // request id echoed back by the worker (correlates the reply)
let restartTimer = null;

// ── Helpers ──
const $ = (id) => document.getElementById(id);
const lower = (s) => (s || '').trim().toLowerCase();
// The JS mirror of VibeCodec.SongSeed — trim + lower-case, with 'rotaliate' for an empty tag.
// The fallback word is load-bearing rather than cosmetic: it decides what song an untagged seed
// (`vibe::23`) resolves to, so a host that spells it differently plays a different song from the
// same seed. Keep this in step with the C#; the engine test asserts that side.
function seedFor(nn) { return `${tag ? lower(tag) : 'rotaliate'}:${nn}`; }
function currentSeedString() { return `${vibe}:${tag}:${displayN}`; }

function setHash() {
  const s = currentSeedString();
  if (location.hash.slice(1) !== s) history.replaceState(null, '', '#' + s);
}

// ── Worker pool plumbing ──
// Construct + wire one pool slot (also used to replace a terminated worker).
function makeWorker(idx) {
  const w = new Worker(new URL('./worker.js', import.meta.url), { type: 'module' });
  w.onmessage = (e) => onPoolMessage(idx, e);
  pool[idx] = { worker: w, busy: false, n: -1 };
}

// Queue a song for generation, then hand queued work to any free worker.
function requestSong(nn) {
  if (rendered.has(nn)) return;
  if (gen.want(nn)) dispatch();
}

// Assign queued songs to idle workers (fans look-ahead renders across the pool).
function dispatch() {
  for (const slot of pool) {
    if (slot.busy) continue;
    const nn = gen.take();
    if (nn === undefined) break;
    const id = ++reqId;
    slot.busy = true;
    slot.n = nn;
    slot.worker.postMessage({ type: 'gen', id, n: nn, mySeq: renderSeq, seed: seedFor(nn), cfg: cfgForN(nn).slice() });
  }
}

// Abort everything in flight: terminate workers mid-render (true cancellation) and replace them
// so they're ready for the new seed; idle/booted workers stay and pick up the new seed at once.
function abortAll() {
  gen.clear();
  rendered.clear();
  for (let i = 0; i < pool.length; i++) {
    if (pool[i].busy) { try { pool[i].worker.terminate(); } catch (_) {} makeWorker(i); }
  }
}

function onPoolMessage(idx, e) {
  const m = e.data;
  // free the slot regardless of outcome, then pull the next queued job
  if (pool[idx]) { pool[idx].busy = false; pool[idx].n = -1; }
  if (m.type === 'error') { console.error('gen error', m.n, m.error); gen.release(m.n); dispatch(); return; }
  if (m.type !== 'song') { dispatch(); return; }
  gen.release(m.n);
  dispatch();
  if (m.mySeq !== undefined && m.mySeq !== renderSeq) return;
  // Build a 2-channel AudioBuffer from the worker's float channels.
  const frames = m.left.length;
  const buf = ctx.createBuffer(2, frames, m.sampleRate);
  buf.copyToChannel(m.left, 0);
  buf.copyToChannel(m.right, 1);
  rendered.set(m.n, { buffer: buf, info: m.info });
  pump();
  renderPlaylist();
}

// ── Equal-power crossfade curve (cos out / sin in) ──
function powerCurve(kind) {
  const N = 64;
  const a = new Float32Array(N);
  for (let i = 0; i < N; i++) {
    const t = (i / (N - 1)) * (Math.PI / 2);
    a[i] = kind === 'in' ? Math.sin(t) : Math.cos(t);
  }
  return a;
}
const CURVE_IN = powerCurve('in');
const CURVE_OUT = powerCurve('out');

// ── Scheduling ──
// Schedule one song starting at startTime; returns the time the NEXT song should start
// (overlapping this one's fade-out for an equal-power crossfade).
function scheduleOneSong(buffer, songN, startTime) {
  const src = ctx.createBufferSource();
  src.buffer = buffer;
  src.loop = false;            // structured song: play through once, then crossfade to next
  const g = ctx.createGain();
  src.connect(g).connect(masterGain);

  const songPlay = buffer.duration * LOOPS_PER_SONG;
  // Fade length. Three bounds, and the tail is the one that matters musically:
  //   * the song's ring-out tail — fade over the decay, never over the ending itself;
  //   * CROSSFADE, the ceiling;
  //   * just under half the play time, so the fade-in and fade-out curves cannot overlap
  //     (Web Audio forbids overlapping automation curves).
  const cf = Math.max(0, Math.min(tailSeconds, CROSSFADE, songPlay / 2 - 0.05));

  if (cf > 0.001) {
    // equal-power fade in; the curve's final value (1) persists as the "hold"; then fade
    // out. No setValueAtTime between them — placing one at a curve edge is an overlap error.
    g.gain.setValueCurveAtTime(CURVE_IN, startTime, cf);
    g.gain.setValueCurveAtTime(CURVE_OUT, startTime + songPlay - cf, cf);
  } else {
    g.gain.setValueAtTime(1, startTime);
  }

  src.start(startTime);
  src.stop(startTime + songPlay + 0.05);
  activeNodes.push(src, g);
  // `activeNodes` exists to tear down what is still PLAYING (a seek/restart stops them), so a
  // finished song has to leave it: a spent source still references its AudioBuffer, i.e. a whole
  // song's PCM, and the list was only ever cleared by a seek. An hour of uninterrupted playback
  // otherwise retains every song it played.
  src.onended = () => {
    try { src.disconnect(); g.disconnect(); } catch (_) {}
    activeNodes = activeNodes.filter((x) => x !== src && x !== g);
  };

  // UI: mark this song as the audible one when it begins, and persist n.
  const mySeq = seq;
  const delay = Math.max(0, (startTime - ctx.currentTime) * 1000);
  setTimeout(() => {
    if (mySeq !== seq) return;
    displayN = songN;
    if (bufferingN === songN) bufferingN = -1;   // the song we were waiting on is now audible
    // Adopt the (frozen) vibe this song was rendered with, so the editor / seed / hash reflect
    // what's audible and the link reproduces it. Pinned songs come from the ledger; an unpinned
    // (non-shuffle) song keeps the live cfg.
    const c = ledger.get(songN);
    if (c) {
      cfg = c;
      genre = mod.getGenre(cfg);
      if ($('genre')) $('genre').value = String(genre);
      vibe = mod.encodeVibe(cfg);
      buildVibeEditor();
    }
    pruneCache();
    localStorage.setItem('skafinity.n', String(songN));
    setHash();
    updateTransport();
    renderPlaylist();
  }, delay);

  return startTime + songPlay - cf; // next song overlaps the fade-out
}

function pump() {
  if (!playing || !ctx) return;
  // Schedule any rendered songs whose start falls within the horizon. Cap iterations so a
  // pathological state can never spin into a scheduling storm.
  let guard = 0;
  while (rendered.has(nextN) && nextTime < ctx.currentTime + SCHEDULE_HORIZON && guard++ < 6) {
    const { buffer } = rendered.get(nextN);
    let nextStart;
    try {
      nextStart = scheduleOneSong(buffer, nextN, nextTime);
    } catch (e) {
      console.error('skafinity: schedule failed', e);
      nextStart = nextTime + buffer.duration * LOOPS_PER_SONG; // still advance, don't wedge
    }
    nextTime = nextStart;
    // Keep a ±PCM_RADIUS window of rendered songs around the audible one so Prev/Next stays
    // instant within it (the ledger of frozen vibes is kept regardless — it's cheap).
    pruneCache();
    firstScheduled = true;
    nextN++;
  }
  // Keep the look-ahead buffer topped up.
  for (let k = nextN; k <= nextN + AHEAD_COUNT; k++) requestSong(k);
}

// HARD restart — for base changes (new seed/tag/genre/vibe edit) that invalidate the whole
// timeline: aborts in-flight renders, drops the PCM cache AND the frozen-vibe ledger, rebuilds
// from n. For a navigation that should PRESERVE the timeline (Prev/Next), use seekTo() instead.
function startSequence() {
  seq++;
  renderSeq++;           // the cfg behind every index may have changed; nothing in flight survives
  // tear down current audio
  for (const node of activeNodes) { try { node.stop && node.stop(); } catch (_) {} try { node.disconnect(); } catch (_) {} }
  activeNodes = [];
  // abort in-flight renders (terminate busy workers) and clear the generation state
  abortAll();
  ledger.clear();        // re-roll fresh vibes for the new run (shuffle mode)
  bufferingN = -1;
  nextN = n;
  firstScheduled = false;
  if (!ctx) return;
  nextTime = ctx.currentTime + 0.18;
  // prime the look-ahead, then pump as renders arrive
  for (let k = n; k <= n + AHEAD_COUNT; k++) requestSong(k);
  pump();
  updateBuffer();
}

// SOFT seek — navigate to song nn while PRESERVING the timeline (ledger + PCM cache). Plays a
// cached song instantly; otherwise stalls in a "Generating…" buffering state while it regenerates
// from nn's ledger seed. This is what Prev/Next/jump call: Prev replays the exact earlier songs,
// Next rolls a fresh genre on demand under shuffle. Restarts the audio schedule (a manual jump
// can't rewind committed Web Audio nodes) but does NOT discard the timeline.
function seekTo(nn) {
  nn = Math.max(0, nn | 0);
  n = nn;
  if (!playing || !ctx) { displayN = nn; setHash(); updateTransport(); renderPlaylist(); return; }
  seq++;                 // invalidate the old audio schedule (stale setTimeouts/onended bail out)
  for (const node of activeNodes) { try { node.stop && node.stop(); } catch (_) {} try { node.disconnect(); } catch (_) {} }
  activeNodes = [];
  // Drop queued work — the window around nn is re-requested below. This RELEASES the dropped
  // indices' claims; holding them would bar those songs from ever being queued again (queue.js).
  gen.dropQueued();
  // A worker mid-render on a song the seek left behind holds a pool slot for the whole render, so
  // a distant jump can hand the entire pool to work nobody will hear while the target waits.
  // Only those are terminated: a Prev/Next lands inside the cache window, where what is in flight
  // is still a song the timeline wants — and a terminate costs a runtime reboot.
  for (let i = 0; i < pool.length; i++) {
    const slot = pool[i];
    if (!slot.busy || Math.abs(slot.n - nn) <= PCM_RADIUS) continue;
    gen.release(slot.n);
    try { slot.worker.terminate(); } catch (_) {}
    makeWorker(i);       // replaces the slot (idle, ready for the target)
  }
  nextN = nn;
  firstScheduled = false;
  bufferingN = rendered.has(nn) ? -1 : nn;   // stall indicator until the target is audible
  nextTime = ctx.currentTime + 0.06;
  pruneCache(nn);        // prune around the target (displayN still lags until nn starts)
  for (let k = nn; k <= nn + AHEAD_COUNT; k++) requestSong(k);
  pump();
  updateTransport();
  updateBuffer();
  renderPlaylist();
}

// ── Transport ──
async function ensureContext() {
  if (!ctx) {
    ctx = new (window.AudioContext || window.webkitAudioContext)();
    masterGain = ctx.createGain();
    masterGain.gain.value = parseFloat($('vol').value);
    masterGain.connect(ctx.destination);
  }
  if (ctx.state === 'suspended') await ctx.resume();
}

async function play() {
  await ensureContext();
  playing = true;
  startSequence();
  updateTransport();
}
function pause() {
  playing = false;
  if (ctx) ctx.suspend();
  updateTransport();
}
function stepN(d) { seekTo(displayN + d); }
function jumpTo(nn) { seekTo(nn); }

function updateTransport() {
  $('playBtn').textContent = playing ? '⏸' : '▶';
  $('seed').value = currentSeedString();
  $('nNow').textContent = displayN;
}

// Surface the "Generating…" / buffering state — playback has stalled waiting on the song you
// skipped to (vs. silent background look-ahead fill). Mirrors SkafinityPlayer.IsBuffering.
function updateBuffer() {
  const el = $('bufState');
  if (!el) return;
  const on = playing && bufferingN >= 0 && !rendered.has(bufferingN);
  el.classList.toggle('show', on);
  el.textContent = on ? `generating #${bufferingN}…` : '';
}

// ── Seed paste ──
function applySeedString(s) {
  const p = mod.parseSeed(s);
  if (p.tag) tag = p.tag;
  if (p.vibe) {
    cfg = mod.decodeVibe(p.vibe, cfg);
    genre = mod.getGenre(cfg);
    if ($('genre')) $('genre').value = String(genre);
    applyStoredVolumes();
    vibe = mod.encodeVibe(cfg);
    buildVibeEditor();
  }
  if (p.hasN) n = Math.max(0, p.n);
  displayN = n;
  setHash();
  if (playing) startSequence();
  updateTransport();
}

// ── Export ──
function exportWav(songN) {
  // Export the song's frozen vibe (its ledger cfg under shuffle), so a downloaded #k matches what
  // the timeline plays — not just whatever the live editor currently shows.
  const bytes = mod.songToWav(seedFor(songN), cfgForN(songN).slice());
  const blob = new Blob([bytes], { type: 'audio/wav' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  const safeTag = (tag ? lower(tag) : 'unknown').replace(/[^a-z0-9_-]/g, '') || 'unknown';
  a.href = url;
  a.download = `${safeTag}_${songN}.wav`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
}

// ── Playlist / navigable-timeline panel ──
// Shows the window around the audible song — history (cached, replayable), the current song, and
// the look-ahead — each with its index, genre, and cached/generating/buffering state, click to jump.
function renderPlaylist() {
  const list = $('playlist');
  list.innerHTML = '';
  const from = Math.max(0, displayN - PCM_RADIUS);
  const to = displayN + AHEAD_COUNT;
  for (let k = from; k <= to; k++) {
    const isNow = k === displayN;
    const cached = rendered.has(k);
    const generating = gen.has(k) || (bufferingN === k && !cached);
    const row = document.createElement('div');
    row.className = 'plrow'
      + (isNow ? ' now' : '')
      + (cached ? ' cached' : '')
      + (generating ? ' gen' : '');

    const label = document.createElement('span');
    label.className = 'pllabel';
    label.textContent = `${isNow ? '▶ ' : ''}#${k}`;
    label.onclick = () => jumpTo(k);

    const tagEl = document.createElement('span');
    tagEl.className = 'plgenre';
    tagEl.textContent = mod ? mod.genreName(genreForN(k)) : '';

    const status = document.createElement('span');
    status.className = 'plstatus';
    if (generating) {
      // No sub-song progress from the one-shot worker render — show an honest indeterminate bar.
      const bar = document.createElement('span');
      bar.className = 'plbar';
      bar.append(document.createElement('span'));
      status.append(bar);
    } else {
      status.textContent = isNow ? 'now' : cached ? 'ready' : k < displayN ? 'gone' : '—';
    }

    const dl = document.createElement('button');
    dl.className = 'pldl';
    dl.textContent = '⬇';
    dl.title = `Export #${k} to WAV`;
    dl.onclick = (ev) => { ev.stopPropagation(); exportWav(k); };
    row.append(label, tagEl, status, dl);
    list.append(row);
  }
  updateBuffer();
}

// ── Vibe editor — per-instrument mixer matrix + GLOBAL strip ──
// Layout is driven entirely from the wasm field metadata for the current genre: each field
// reports its voice (matrix row, or null for a GLOBAL knob) and column (0 volume / 1 tone /
// 2 character / 3 extra). So a new genre — or a new knob — is a pure-C# change; there is no
// JS-side field table to keep in sync.
const COL_HEADERS = ['VOLUME', 'TONE', 'CHARACTER', 'EXTRA'];

// One field's index + cached info for the current genre.
function genreFields() {
  const out = [];
  const count = mod.vibeFieldCount(genre);
  for (let i = 0; i < count; i++) out.push({ i, ...mod.vibeFieldInfo(genre, i) });
  return out;
}

// Build one editable cell (slider, or a <select> for enum/choice knobs) for field `f`.
function buildKnob(f, labelText) {
  const cell = document.createElement('div');
  cell.className = 'knob';
  const choices = f.choices;

  const head = document.createElement('div');
  head.className = 'knob-head';
  const name = document.createElement('span');
  name.className = 'knob-name';
  name.textContent = labelText;
  const val = document.createElement('span');
  val.className = 'knob-val';
  val.textContent = mod.vibeDisplay(cfg, f.i);
  head.append(name, val);

  let input;
  if (choices.length > 0) {
    input = document.createElement('select');
    for (let c = 0; c < choices.length; c++) {
      const o = document.createElement('option');
      o.value = String(c); o.textContent = choices[c];
      input.append(o);
    }
    input.selectedIndex = Math.round(mod.getVibeNorm(cfg, f.i) * (choices.length - 1));
    input.onchange = () => onVibeChange(f, input.selectedIndex / (choices.length - 1), val);
  } else {
    // Snap to the same discrete grid the seed encodes (one level per base-36 char), so the
    // slider can only land on values the vibe can actually represent.
    const steps = mod.vibeLevels() - 1;
    input = document.createElement('input');
    input.type = 'range'; input.min = '0'; input.max = String(steps); input.step = '1';
    input.value = String(Math.round(mod.getVibeNorm(cfg, f.i) * steps));
    input.oninput = () => onVibeChange(f, parseInt(input.value, 10) / steps, val);
  }
  input.className = 'knob-input';
  cell.append(head, input);
  return cell;
}

function buildVibeEditor() {
  const host = $('vibe');
  host.innerHTML = '';

  const fields = genreFields();
  const globals = fields.filter((f) => !f.voice);
  // group instrument fields by voice, preserving first-seen order
  const voices = [];
  const byVoice = new Map();
  for (const f of fields) {
    if (!f.voice) continue;
    if (!byVoice.has(f.voice)) { byVoice.set(f.voice, [null, null, null, null]); voices.push(f.voice); }
    byVoice.get(f.voice)[f.column] = f;
  }

  // ── mixer matrix ──
  const matrix = document.createElement('div');
  matrix.className = 'matrix';
  const head = document.createElement('div');
  head.className = 'mrow mhead';
  for (const c of ['', ...COL_HEADERS]) {
    const h = document.createElement('div');
    h.className = c ? 'mcell mhlabel' : 'mvoice';
    h.textContent = c;
    head.append(h);
  }
  matrix.append(head);

  for (const voice of voices) {
    const row = document.createElement('div');
    row.className = 'mrow';
    const v = document.createElement('div');
    v.className = 'mvoice';
    v.textContent = voice;
    row.append(v);
    const cells = byVoice.get(voice);
    for (let col = 0; col < COL_HEADERS.length; col++) {
      const cell = document.createElement('div');
      cell.className = 'mcell';
      const f = cells[col];
      // the column header already names volume/tone; only label the descriptive knobs
      if (f) cell.append(buildKnob(f, f.name === COL_HEADERS[col] ? '' : f.name));
      row.append(cell);
    }
    matrix.append(row);
  }
  host.append(matrix);

  // ── GLOBAL strip ──
  const gl = document.createElement('div');
  gl.className = 'glabel';
  gl.textContent = 'GLOBAL';
  host.append(gl);

  const grid = document.createElement('div');
  grid.className = 'global-grid';
  for (const f of globals) grid.append(buildKnob(f, f.name));
  host.append(grid);
}

function onVibeChange(f, norm, valEl) {
  cfg = mod.setVibeField(cfg, f.i, norm);
  valEl.textContent = mod.vibeDisplay(cfg, f.i);
  if (f.column === 0 && f.voice) {
    // Per-instrument volume: a local mix preference, not part of the shareable seed/hash.
    vols[f.voice] = norm;
    saveVols();
  } else {
    vibe = mod.encodeVibe(cfg);
    setHash();          // rewrite the URL hash
    updateTransport();  // rewrite the visible seed field
  }
  // debounce-restart so a slider drag isn't a generation storm (≈0.35s like the game)
  clearTimeout(restartTimer);
  restartTimer = setTimeout(() => { if (playing) startSequence(); }, 350);
}

// Change the genre: rewrite cfg, rebuild the (genre-specific) editor, restart playback.
function setGenre(g) {
  genre = g;
  cfg = mod.setGenre(cfg, g);
  applyStoredVolumes();
  vibe = mod.encodeVibe(cfg);
  buildVibeEditor();
  setHash();
  updateTransport();
  if (playing) startSequence();
}

// A throwaway roll (the manual 🎲): fresh genre + every non-volume knob. The rules — which
// knobs are rollable, that per-instrument volumes stay out, that the tempo range gets put back
// in order — live in the engine (VibeCodec.Roll), so this side never restates them.
function randomizedCfg(base, randomizeGenre = true) { return mod.rollVibe(base, randomizeGenre); }
// Randomize the live cfg in place (manual 🎲 reroll). Callers handle UI/hash/restart.
function randomizeVibeCfg() { cfg = randomizedCfg(cfg); vibe = mod.encodeVibe(cfg); }

// Populate the genre <select> from the wasm genre list (once).
function populateGenres() {
  const sel = $('genre');
  sel.innerHTML = '';
  const count = mod.genreCount();
  for (let i = 0; i < count; i++) {
    const o = document.createElement('option');
    o.value = String(i); o.textContent = mod.genreName(i);
    sel.append(o);
  }
}

// A short base-36 tag, e.g. "bd44ac2a" — the random song name used on a fresh visit.
function randomTag() { return Math.random().toString(36).slice(2, 10); }

// 🎲 Reroll: randomize the vibe knobs and restart playback (the seed's tag/n are unchanged).
function rerollVibe() {
  randomizeVibeCfg();
  buildVibeEditor();
  setHash();
  updateTransport();
  if (playing) startSequence();
}

// 🎲 Shuffle toggle ("random every song"): flip the mode and re-resolve the timeline from the
// current song forward under the new mode — ON freezes a fresh rolled vibe+genre per upcoming n,
// OFF reverts upcoming songs to the live vibe. History (n < current) keeps its frozen line so Prev
// still replays what you heard. Soft-reseeks the current song so the change takes immediately.
function toggleShuffle() {
  randomEverySong = !randomEverySong;
  saveShuffle();
  for (const k of ledger.keys()) if (k >= displayN) ledger.delete(k);
  for (const k of rendered.keys()) if (k >= displayN) rendered.delete(k);
  renderSeq++;           // the upcoming vibes just changed — discard whatever is mid-render
  updateShuffleBtn();
  if (playing) seekTo(displayN); else renderPlaylist();
}
function updateShuffleBtn() {
  const b = $('shuffleBtn');
  if (!b) return;
  b.classList.toggle('on', randomEverySong);
  b.textContent = randomEverySong ? '🎲 every song: ON' : '🎲 every song: OFF';
}

// ── Wire up ──
async function init() {
  mod = await Skafinity();
  if (mod.ringOutSeconds) tailSeconds = mod.ringOutSeconds();
  for (let i = 0; i < POOL_SIZE; i++) makeWorker(i);

  cfg = mod.defaultConfig();
  await applyHouseConfig();   // overlay web/config.json baseline-mix tuning (no rebuild needed)
  populateGenres();

  // initial seed: a shared URL (location.hash) wins; otherwise a fresh random song —
  // random tag, random vibe, n=0 — so every plain visit lands somewhere new.
  const hash = location.hash.slice(1);
  if (hash) {
    const p = mod.parseSeed(hash);
    if (p.tag) tag = p.tag;
    if (p.vibe) cfg = mod.decodeVibe(p.vibe, cfg);
    if (p.hasN) n = Math.max(0, p.n);
    vibe = mod.encodeVibe(cfg);
  } else {
    tag = randomTag();
    n = 0;
    // start on a random genre too, then randomize that genre's knobs
    cfg = mod.setGenre(cfg, Math.floor(Math.random() * mod.genreCount()));
    genre = mod.getGenre(cfg);   // sync before randomize (it indexes the genre's field list)
    randomizeVibeCfg();   // sets `vibe`
  }
  genre = mod.getGenre(cfg);
  $('genre').value = String(genre);
  applyStoredVolumes();   // overlay the saved per-voice mix on top of the seed's voicing
  displayN = n;

  buildVibeEditor();
  renderPlaylist();
  updateTransport();
  setHash();

  // Drive scheduling: songs are ~80s, so the next must be queued as its start approaches
  // even when no worker render just landed (mirrors MusicController's per-tick top-up).
  setInterval(() => { if (playing && ctx) pump(); }, 250);

  $('playBtn').onclick = () => (playing ? pause() : play());
  $('prevBtn').onclick = () => stepN(-1);
  $('nextBtn').onclick = () => stepN(1);
  $('jumpBtn').onclick = () => { const v = parseInt($('jumpN').value, 10); if (!Number.isNaN(v)) jumpTo(v); };
  $('seedGo').onclick = () => applySeedString($('seed').value);
  $('seed').addEventListener('keydown', (e) => { if (e.key === 'Enter') applySeedString($('seed').value); });
  $('copyBtn').onclick = async () => {
    try { await navigator.clipboard.writeText(location.href); $('copyBtn').textContent = 'copied!'; setTimeout(() => ($('copyBtn').textContent = 'copy link'), 1200); } catch (_) {}
  };
  $('rerollBtn').onclick = () => rerollVibe();
  if ($('shuffleBtn')) $('shuffleBtn').onclick = () => toggleShuffle();
  updateShuffleBtn();
  $('genre').onchange = () => setGenre(parseInt($('genre').value, 10));
  $('dlBtn').onclick = () => exportWav(displayN);
  $('vol').oninput = () => { if (masterGain) masterGain.gain.value = parseFloat($('vol').value); };
  window.addEventListener('hashchange', () => {
    const h = location.hash.slice(1);
    if (h && h !== currentSeedString()) applySeedString(h);
  });
}

init().catch((e) => {
  document.getElementById('status').textContent =
    'Failed to load the WASM engine — run `make` (needs the .NET wasm-tools workload) so web/_framework exists, and serve over http (make serve). ' + e;
});
