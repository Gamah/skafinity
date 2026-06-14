// skafinity — Web Audio sequencer + vibe editor + rolling playlist + WAV export.
// Port of MusicController's scheduling (not its s&box plumbing). The heavy synthesis runs
// in worker.js; this file owns the AudioContext, crossfade scheduling, and the UI.
import Skafinity from '../build/skafinity.js';

// ── Tunables (mirror MusicController defaults) ──
const LOOPS_PER_SONG = 2;
const CROSSFADE = 3.75;       // seconds; also the first song's fade-in
const AHEAD_COUNT = 4;        // songs kept pre-rendered
const SCHEDULE_HORIZON = 12;  // seconds: schedule the next song once it's within this

let mod = null;               // main-thread WASM (vibe/codec/export — light calls)
let worker = null;

// ── State ──
let cfg = null;               // Float32Array — the live Config (vibe applied)
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

// ── Worker plumbing ──
function requestSong(nn) {
  if (rendered.has(nn) || requested.has(nn)) return;
  requested.add(nn);
  const id = ++reqId;
  reqMap.set(id, nn);
  worker.postMessage({ type: 'gen', id, n: nn, mySeq: seq, seed: seedFor(nn), cfg: cfg.slice() });
}

function onWorkerMessage(e) {
  const m = e.data;
  if (m.type === 'error') { console.error('gen error', m.n, m.error); requested.delete(m.n); return; }
  if (m.type !== 'song') return;
  requested.delete(m.n);
  reqMap.delete(m.id);
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
  src.loop = true;
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
  rendered.clear();
  requested.clear();
  reqMap.clear();
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

// ── Vibe editor ──
function buildVibeEditor() {
  const host = $('vibe');
  host.innerHTML = '';
  const count = mod.vibeFieldCount();
  for (let i = 0; i < count; i++) {
    const f = mod.vibeFieldInfo(i);
    const choices = f.choices; // JS array (may be empty)
    const row = document.createElement('div');
    row.className = 'vrow';

    const name = document.createElement('label');
    name.className = 'vname';
    name.textContent = f.name;

    const val = document.createElement('span');
    val.className = 'vval';
    val.textContent = mod.vibeDisplay(cfg, i);

    let input;
    if (choices.length > 0) {
      input = document.createElement('select');
      for (let c = 0; c < choices.length; c++) {
        const o = document.createElement('option');
        o.value = String(c); o.textContent = choices[c];
        input.append(o);
      }
      input.selectedIndex = Math.round(mod.getVibeNorm(cfg, i) * (choices.length - 1));
      input.onchange = () => {
        const norm = input.selectedIndex / (choices.length - 1);
        onVibeChange(i, norm, val);
      };
    } else {
      input = document.createElement('input');
      input.type = 'range'; input.min = '0'; input.max = '1000'; input.step = '1';
      input.value = String(Math.round(mod.getVibeNorm(cfg, i) * 1000));
      input.oninput = () => onVibeChange(i, parseInt(input.value, 10) / 1000, val);
    }
    input.className = 'vinput';

    row.append(name, input, val);
    host.append(row);
  }
}

function onVibeChange(i, norm, valEl) {
  cfg = mod.setVibeField(cfg, i, norm);
  vibe = mod.encodeVibe(cfg);
  valEl.textContent = mod.vibeDisplay(cfg, i);
  setHash();
  // debounce-restart so a slider drag isn't a generation storm (≈0.35s like the game)
  clearTimeout(restartTimer);
  restartTimer = setTimeout(() => { if (playing) startSequence(); }, 350);
}

// ── Wire up ──
async function init() {
  mod = await Skafinity();
  worker = new Worker(new URL('./worker.js', import.meta.url), { type: 'module' });
  worker.onmessage = onWorkerMessage;

  cfg = mod.defaultConfig();

  // initial seed: location.hash, else resume from localStorage, else defaults
  const hash = location.hash.slice(1);
  if (hash) {
    const p = mod.parseSeed(hash);
    if (p.tag) tag = p.tag;
    if (p.vibe) cfg = mod.decodeVibe(p.vibe, cfg);
    if (p.hasN) n = Math.max(0, p.n);
  } else {
    const savedN = parseInt(localStorage.getItem('skafinity.n') || '0', 10);
    if (!Number.isNaN(savedN)) n = Math.max(0, savedN);
  }
  vibe = mod.encodeVibe(cfg);
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
  $('dlBtn').onclick = () => exportWav(displayN, $('stereo').checked);
  $('vol').oninput = () => { if (masterGain) masterGain.gain.value = parseFloat($('vol').value); };
  window.addEventListener('hashchange', () => {
    const h = location.hash.slice(1);
    if (h && h !== currentSeedString()) applySeedString(h);
  });
}

init().catch((e) => {
  document.getElementById('status').textContent =
    'Failed to load the WASM engine — run `make` (needs emscripten) so build/skafinity.js exists. ' + e;
});
