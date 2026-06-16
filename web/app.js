// skafinity — Web Audio sequencer + vibe editor + rolling playlist + WAV export.
// Port of MusicController's scheduling (not its s&box plumbing). The heavy synthesis runs
// in worker.js; this file owns the AudioContext, crossfade scheduling, and the UI.
import Skafinity from './engine.js';

// ── Tunables (mirror MusicController defaults) ──
// Songs now have an intro→…→ending structure, so each plays once start-to-end (no internal
// loop) and crossfades into the next.
const LOOPS_PER_SONG = 1;
const CROSSFADE = 3.75;       // seconds; also the first song's fade-in
const AHEAD_COUNT = 4;        // songs kept pre-rendered
const SCHEDULE_HORIZON = 12;  // seconds: schedule the next song once it's within this
// Pool of generation workers. Each boots its own .NET runtime (memory cost — hence the cap),
// and the pool lets look-ahead songs render in parallel instead of serializing through one
// worker. A seed change terminates whichever workers are mid-render (true abort) and lets an
// already-booted idle worker pick up the new seed with no reboot.
const POOL_SIZE = 3;

let mod = null;               // main-thread WASM (vibe/codec/export — light calls)
let pool = [];                // [{ worker, busy, n }]
const pending = [];           // queue of song indices waiting for a free worker

// ── State ──
let cfg = null;               // Float32Array — the live Config (vibe applied)
let genre = 0;                // current genre index (mirrors cfg's genre)
let tag = 'rotaliate';
let n = 0;                    // next song index to schedule
let displayN = 0;            // song currently audible (for UI)
let vibe = '';
let seq = 0;                  // bumped on every restart; stale renders are dropped
let playing = false;

let ctx = null, masterGain = null;
const rendered = new Map();   // n -> { buffer, info }
const requested = new Set();  // n currently being generated
let nextN = 0;                // next index to schedule into the timeline
let nextTime = 0;             // ctx time the next song starts
let firstScheduled = false;
let activeNodes = [];         // live source/gain nodes for the current sequence
let reqId = 0;
const reqMap = new Map();     // worker request id -> n
let restartTimer = null;

// ── Helpers ──
const $ = (id) => document.getElementById(id);
const lower = (s) => (s || '').trim().toLowerCase();
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
  if (rendered.has(nn) || requested.has(nn)) return;
  requested.add(nn);
  pending.push(nn);
  dispatch();
}

// Assign pending songs to idle workers (fans look-ahead renders across the pool).
function dispatch() {
  for (const slot of pool) {
    if (slot.busy || pending.length === 0) continue;
    const nn = pending.shift();
    const id = ++reqId;
    reqMap.set(id, nn);
    slot.busy = true;
    slot.n = nn;
    slot.worker.postMessage({ type: 'gen', id, n: nn, mySeq: seq, seed: seedFor(nn), cfg: cfg.slice() });
  }
}

// Abort everything in flight: terminate workers mid-render (true cancellation) and replace them
// so they're ready for the new seed; idle/booted workers stay and pick up the new seed at once.
function abortAll() {
  pending.length = 0;
  rendered.clear();
  requested.clear();
  reqMap.clear();
  for (let i = 0; i < pool.length; i++) {
    if (pool[i].busy) { try { pool[i].worker.terminate(); } catch (_) {} makeWorker(i); }
  }
}

function onPoolMessage(idx, e) {
  const m = e.data;
  // free the slot regardless of outcome, then pull the next queued job
  if (pool[idx]) { pool[idx].busy = false; pool[idx].n = -1; }
  if (m.type === 'error') { console.error('gen error', m.n, m.error); requested.delete(m.n); reqMap.delete(m.id); dispatch(); return; }
  if (m.type !== 'song') { dispatch(); return; }
  requested.delete(m.n);
  reqMap.delete(m.id);
  dispatch();
  if (m.mySeq !== undefined && m.mySeq !== seq) return;
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
  // Fade length: never more than (just under) half the play time, so the fade-in and
  // fade-out curves can't overlap (Web Audio forbids overlapping automation curves).
  const cf = Math.max(0, Math.min(CROSSFADE, songPlay / 2 - 0.05));

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
  src.onended = () => { try { src.disconnect(); g.disconnect(); } catch (_) {} };

  // UI: mark this song as the audible one when it begins, and persist n.
  const mySeq = seq;
  const delay = Math.max(0, (startTime - ctx.currentTime) * 1000);
  setTimeout(() => {
    if (mySeq !== seq) return;
    displayN = songN;
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
    // drop the buffer we just consumed (keep a small trailing cache for the playlist)
    for (const k of rendered.keys()) if (k < nextN - 2) rendered.delete(k);
    firstScheduled = true;
    nextN++;
  }
  // Keep the look-ahead buffer topped up.
  for (let k = nextN; k <= nextN + AHEAD_COUNT; k++) requestSong(k);
}

function startSequence() {
  seq++;
  // tear down current audio
  for (const node of activeNodes) { try { node.stop && node.stop(); } catch (_) {} try { node.disconnect(); } catch (_) {} }
  activeNodes = [];
  // abort in-flight renders (terminate busy workers) and clear the generation state
  abortAll();
  nextN = n;
  firstScheduled = false;
  if (!ctx) return;
  nextTime = ctx.currentTime + 0.18;
  // prime the look-ahead, then pump as renders arrive
  for (let k = n; k <= n + AHEAD_COUNT; k++) requestSong(k);
  pump();
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
function stepN(d) {
  n = Math.max(0, displayN + d);
  if (playing) startSequence(); else { displayN = n; setHash(); updateTransport(); }
}
function jumpTo(nn) {
  n = Math.max(0, nn | 0);
  if (playing) startSequence(); else { displayN = n; setHash(); updateTransport(); }
}

function updateTransport() {
  $('playBtn').textContent = playing ? '⏸' : '▶';
  $('seed').value = currentSeedString();
  $('nNow').textContent = displayN;
}

// ── Seed paste ──
function applySeedString(s) {
  const p = mod.parseSeed(s);
  if (p.tag) tag = p.tag;
  if (p.vibe) {
    cfg = mod.decodeVibe(p.vibe, cfg);
    genre = mod.getGenre(cfg);
    if ($('genre')) $('genre').value = String(genre);
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
function exportWav(songN, stereo) {
  const bytes = mod.songToWav(seedFor(songN), cfg, stereo);
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

// ── Playlist panel ──
function renderPlaylist() {
  const list = $('playlist');
  list.innerHTML = '';
  const from = Math.max(0, displayN - 2);
  const to = displayN + AHEAD_COUNT;
  for (let k = from; k <= to; k++) {
    const row = document.createElement('div');
    row.className = 'plrow' + (k === displayN ? ' now' : '');
    const state = rendered.has(k) ? 'ready' : (requested.has(k) ? 'gen…' : '');
    const label = document.createElement('span');
    label.className = 'pllabel';
    label.textContent = `${k === displayN ? '▶ ' : ''}#${k}  ${seedFor(k)}`;
    label.onclick = () => jumpTo(k);
    const status = document.createElement('span');
    status.className = 'plstatus';
    status.textContent = k < displayN ? 'played' : state;
    const dl = document.createElement('button');
    dl.className = 'pldl';
    dl.textContent = '⬇';
    dl.title = `Export #${k} to WAV`;
    dl.onclick = (ev) => { ev.stopPropagation(); exportWav(k, $('stereo').checked); };
    row.append(label, status, dl);
    list.append(row);
  }
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

function findFieldIndex(name) {
  const count = mod.vibeFieldCount(genre);
  for (let i = 0; i < count; i++) if (mod.vibeFieldInfo(genre, i).name === name) return i;
  return -1;
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
    input.onchange = () => onVibeChange(f.i, input.selectedIndex / (choices.length - 1), val);
  } else {
    input = document.createElement('input');
    input.type = 'range'; input.min = '0'; input.max = '1000'; input.step = '1';
    input.value = String(Math.round(mod.getVibeNorm(cfg, f.i) * 1000));
    input.oninput = () => onVibeChange(f.i, parseInt(input.value, 10) / 1000, val);
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

function onVibeChange(i, norm, valEl) {
  cfg = mod.setVibeField(cfg, i, norm);
  vibe = mod.encodeVibe(cfg);
  valEl.textContent = mod.vibeDisplay(cfg, i);
  setHash();          // rewrite the URL hash
  updateTransport();  // rewrite the visible seed field
  // debounce-restart so a slider drag isn't a generation storm (≈0.35s like the game)
  clearTimeout(restartTimer);
  restartTimer = setTimeout(() => { if (playing) startSequence(); }, 350);
}

// Change the genre: rewrite cfg, rebuild the (genre-specific) editor, restart playback.
function setGenre(g) {
  genre = g;
  cfg = mod.setGenre(cfg, g);
  vibe = mod.encodeVibe(cfg);
  buildVibeEditor();
  setHash();
  updateTransport();
  if (playing) startSequence();
}

// Randomize cfg's knobs in place — every knob of the current genre except the per-instrument
// volumes — then keep TEMPO MIN ≤ MAX (ranges are identical so swapping the normalized values
// swaps the tempos). Pure on cfg; callers handle UI/hash/restart.
function randomizeVibeCfg() {
  for (const f of genreFields()) {
    if (f.column === 0 && f.voice) continue; // skip per-instrument volumes
    cfg = mod.setVibeField(cfg, f.i, Math.random());
  }
  const lo = findFieldIndex('TEMPO MIN'), hi = findFieldIndex('TEMPO MAX');
  if (lo >= 0 && hi >= 0) {
    const a = mod.getVibeNorm(cfg, lo), b = mod.getVibeNorm(cfg, hi);
    if (a > b) { cfg = mod.setVibeField(cfg, lo, b); cfg = mod.setVibeField(cfg, hi, a); }
  }
  vibe = mod.encodeVibe(cfg);
}

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

// ── Wire up ──
async function init() {
  mod = await Skafinity();
  for (let i = 0; i < POOL_SIZE; i++) makeWorker(i);

  cfg = mod.defaultConfig();
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
  $('genre').onchange = () => setGenre(parseInt($('genre').value, 10));
  $('dlBtn').onclick = () => exportWav(displayN, $('stereo').checked);
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
