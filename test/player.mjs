// Does the extracted transport still hold its side of the bargain?
//
// web/player.js is what app.js's scheduler became when the DOM, the module-scope song state and the
// un-namespaced storage keys came out of it. Almost all of it needs a browser — but the parts that
// make it EMBEDDABLE do not, and those are exactly the parts a browser would not have told us
// about anyway:
//
//   * the queue invariant survives a seek (claimed === queued ∪ in-flight — see web/queue.js);
//   * two players on one page cost one pool of workers, not two;
//   * storage is namespaced per instance, and `storage: null` really writes nothing;
//   * destroy() lets go of everything — a widget removed from a host page must not keep a worker
//     rendering and an interval firing in someone else's tab.
//
// The engine, the AudioContext and the Worker are all injected, so this runs on a bare checkout
// with no wasm and no browser.
//
//   node test/player.mjs        (part of `make test`)
import { readFileSync } from 'node:fs';
import { SkafinityPlayer, RenderPool } from '../web/player.js';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

// ── Fakes ──────────────────────────────────────────────────────────────────────
// A stand-in for the wasm engine: the same surface web/engine.js hands back, with the composition
// replaced by arithmetic. Only the SHAPE matters here — test/page.mjs is what checks the real one.
function stubEngine() {
  const cfg0 = () => Float64Array.from({ length: 8 }, () => 0);
  return {
    defaultConfig: cfg0,
    ringOutSeconds: () => 2.5,
    applyAdvancedConfig: (c) => c,
    genreCount: () => 6,
    genreName: (i) => `genre${i}`,
    getGenre: (c) => c[0] | 0,
    setGenre: (c, i) => { const n = c.slice(); n[0] = i; return n; },
    encodeVibe: (c) => `v${c[0] | 0}${c[1] | 0}`,
    decodeVibe: (v, c) => { const n = c.slice(); n[0] = parseInt(v.slice(1, 2), 10) || 0; return n; },
    looksLikeVibe: (s) => /^v\d+$/.test(s),
    rollVibe: (c) => { const n = c.slice(); n[1] = (n[1] + 1) % 9; return n; },
    // Deterministic per index, like the real shuffle line: index n always yields the same cfg.
    rollVibeFor: (c, tag, n) => { const x = c.slice(); x[0] = n % 6; x[1] = n; return x; },
    vibeFieldCount: () => 2,
    vibeFieldInfo: (g, i) => ({ name: i === 0 ? 'VOLUME' : 'TONE', min: 0, max: 1, isInt: false, voice: 'BASS', column: i, choices: [] }),
    vibeLevels: () => 36,
    setVibeField: (c) => c.slice(),
    getVibeNorm: () => 0.5,
    vibeDisplay: () => '0.50',
    songToWav: () => new Uint8Array([1, 2, 3]),
    parseSeed: (s) => {
      const p = (s || '').trim().split(':');
      const int = (x) => (/^-?\d+$/.test(x) ? parseInt(x, 10) : null);
      if (p.length >= 3) return { vibe: p[0], tag: p[1], n: int(p[2]) ?? 0, hasN: int(p[2]) !== null };
      if (p.length === 2 && int(p[1]) !== null) return { vibe: '', tag: p[0], n: int(p[1]), hasN: true };
      return { vibe: '', tag: p[0] || '', n: 0, hasN: false };
    },
  };
}

const SR = 44100, FRAMES = SR * 4;   // 4-second "songs"
class FakeCtx {
  constructor() { this.currentTime = 0; this.state = 'running'; this.destination = {}; }
  resume() { this.state = 'running'; return Promise.resolve(); }
  createGain() {
    return { gain: { value: 1, setValueCurveAtTime() {}, setValueAtTime() {} }, connect: (x) => x, disconnect() {} };
  }
  createBuffer(ch, frames, rate) { return { duration: frames / rate, numberOfChannels: ch, copyToChannel() {} }; }
  createBufferSource() {
    return { buffer: null, loop: false, onended: null, connect: (x) => x, disconnect() {}, start() {}, stop() {} };
  }
}

// A worker that renders only when the test says so, so a half-finished pool can be inspected.
const allWorkers = [];
class FakeWorker {
  constructor() { this.jobs = []; this.killed = false; this.onmessage = null; allWorkers.push(this); }
  postMessage(m) { this.jobs.push(m); }
  terminate() { this.killed = true; }
  finishAll() { while (this.jobs.length) this.finishOne(); }
  finishOne() {
    const m = this.jobs.shift();
    if (!m || this.killed) return;
    this.onmessage({ data: {
      type: 'song', id: m.id, n: m.n, mySeq: m.mySeq, sampleRate: SR,
      left: new Float32Array(FRAMES), right: new Float32Array(FRAMES), info: null } });
  }
}
const liveWorkers = () => allWorkers.filter((w) => !w.killed);

function fakeStorage() {
  const map = new Map();
  return {
    map,
    getItem: (k) => (map.has(k) ? map.get(k) : null),
    setItem: (k, v) => map.set(k, String(v)),
    removeItem: (k) => map.delete(k),
  };
}

function makePlayer(extra = {}) {
  return new SkafinityPlayer({
    engine: stubEngine(),
    audioContext: new FakeCtx(),
    configUrl: null,            // no config.json to fetch off a node process
    seed: 'v0:test:0',
    storage: null,
    ...extra,
  });
}

// Claimed minus queued should be exactly what the pool is holding for this player — the invariant,
// checked against the OTHER side of it (the workers) rather than against the queue's own bookkeeping.
function invariantHolds(p, pool) {
  const heldByPool = pool.slots.filter((s) => s.busy && s.owner === p).map((s) => s.n).sort();
  const inFlight = p.gen.inFlight().sort();
  return JSON.stringify(heldByPool) === JSON.stringify(inFlight);
}

// ── It plays ───────────────────────────────────────────────────────────────────
{
  const pool = new RenderPool(() => new FakeWorker(), 3);
  const p = makePlayer({ pool });
  await p.play();
  check('a play boots the engine and starts the sequence', p.ready && p.playing);
  check('the look-ahead is claimed up front', p.gen.claimed.size >= 4, `claimed ${[...p.gen.claimed].join(',')}`);
  check('the pool got the work', pool.slots.filter((s) => s.busy).length === 3);
  check('the queue invariant holds while rendering', invariantHolds(p, pool));

  for (const w of liveWorkers()) w.finishAll();
  await wait(30);
  check('rendered songs land in the cache', p.rendered.size > 0, `${p.rendered.size} cached`);
  check('scheduling committed audio nodes', p.activeNodes.length > 0);
  check('every claim was released as its render landed', p.gen.claimed.size <= 5);

  await wait(260);
  check('the audible song is reported', p.displayN >= 0);
  p.destroy();
}

// ── Pause resumes where it stopped ─────────────────────────────────────────────
// It cannot suspend the AudioContext (shared with the other widgets on the page), so it has to
// carry the position itself — otherwise every pause would restart the song from its downbeat.
{
  allWorkers.length = 0;
  const pool = new RenderPool(() => new FakeWorker(), 3);
  const p = makePlayer({ pool });
  await p.play();
  for (const w of liveWorkers()) w.finishAll();
  await wait(240);                       // let the first song become audible
  check('a song is audible before the pause', !!p.current, JSON.stringify(p.current));
  p.ctx.currentTime = p.current.startTime + 1.5;
  const wasN = p.displayN;
  p.pause();
  check('the pause remembers how far in it got', Math.abs(p.resumeOffset - 1.5) < 0.01, String(p.resumeOffset));
  check('…on the song that was playing', p.n === wasN, `n=${p.n} displayN=${wasN}`);
  await p.play();
  check('the resume consumes the offset exactly once', p.resumeOffset === 0);

  // Paused inside the last quarter-second there is nothing to resume into: take the next song.
  for (let i = 0; i < 20 && !p.current; i++) { for (const w of liveWorkers()) w.finishAll(); await wait(30); }
  check('the resumed song became audible', !!p.current);
  p.ctx.currentTime = p.current.startTime + p.current.duration - p.current.offset;
  const at = p.current.n;
  p.pause();
  check('a pause at the very end starts the NEXT song instead', p.resumeOffset === 0 && p.n === at + 1,
    `offset ${p.resumeOffset}, n ${p.n} vs ${at}`);
  p.destroy();
}

// ── A seek never strands an index ──────────────────────────────────────────────
// This is the bug queue.js exists for: dropping queued work without releasing its claims made every
// affected index permanently un-queueable, and the timeline is walked in order, so the first one it
// reached stopped playback for good.
{
  allWorkers.length = 0;
  const pool = new RenderPool(() => new FakeWorker(), 3);
  const p = makePlayer({ pool });
  await p.play();
  const before = [...p.gen.claimed];
  p.seekTo(400);                       // far outside the cache window
  check('a distant seek keeps the invariant', invariantHolds(p, pool));
  check('a distant seek terminates the renders it abandoned', allWorkers.some((w) => w.killed));
  const stranded = before.filter((n) => Math.abs(n - 400) > 5 && p.gen.has(n));
  check('nothing left behind stays claimed', stranded.length === 0, `stranded ${stranded.join(',')}`);
  check('an abandoned index can be wanted again', p.gen.want(before[0]) === true);

  // …and a near seek does NOT throw away work the timeline still wants (a terminate costs a
  // runtime reboot, and Prev/Next lands inside the window).
  allWorkers.length = 0;
  for (const s of pool.slots) if (!s.busy) { /* keep the pool busy */ }
  const p2 = makePlayer({ pool });
  await p2.play();
  const killedBefore = allWorkers.filter((w) => w.killed).length;
  p2.seekTo(p2.displayN + 1);
  check('a Prev/Next does not terminate in-window renders',
    allWorkers.filter((w) => w.killed).length === killedBefore);
  check('the invariant survives a near seek', invariantHolds(p2, pool));
  p.destroy(); p2.destroy();
}

// ── Two widgets, one pool ──────────────────────────────────────────────────────
// The whole reason the pool is a page-wide singleton: three runtimes per embed would mean nine
// runtimes on a page with three widgets.
{
  allWorkers.length = 0;
  const pool = new RenderPool(() => new FakeWorker(), 3);
  const a = makePlayer({ pool, seed: 'v0:aaa:0' });
  const b = makePlayer({ pool, seed: 'v0:bbb:0' });
  await a.play();
  await b.play();
  check('two players cost one pool of workers', liveWorkers().length === 3, `${liveWorkers().length} workers`);
  // `a` pressed play first and filled every slot with look-ahead. `b` then pressed play and has
  // nothing to play at all, so it takes one slot off `a`'s speculative work rather than waiting a
  // whole render out.
  const owners = new Set(pool.slots.filter((s) => s.busy).map((s) => s.owner));
  check('a starting player preempts another\'s look-ahead', owners.size === 2, `${owners.size} owner(s) served`);
  check('…and takes no more than its fair share', pool.held(b) <= pool.fairShare(), `${pool.held(b)} slots`);
  check('each player keeps its own invariant', invariantHolds(a, pool) && invariantHolds(b, pool));

  // A destroyed widget must not keep a slot rendering for it.
  a.destroy();
  check('destroy releases the dead player from the pool', !pool.players.includes(a));
  check('destroy leaves no slot owned by the dead player', !pool.slots.some((s) => s.owner === a));
  check('the surviving widget still has its pool', pool.players.includes(b));
  b.destroy();
}

// ── Storage is namespaced, or absent ───────────────────────────────────────────
{
  const store = fakeStorage();
  const one = makePlayer({ storage: store, storageKey: 'one', pool: new RenderPool(() => new FakeWorker(), 1) });
  const two = makePlayer({ storage: store, storageKey: 'two', pool: new RenderPool(() => new FakeWorker(), 1) });
  await one.load(); await two.load();
  one.setVolume(0.3);
  two.setVolume(0.9);
  check('keys are namespaced per instance',
    store.getItem('skafinity:one:master') === '0.3' && store.getItem('skafinity:two:master') === '0.9');
  check('no un-namespaced key is written',
    ![...store.map.keys()].some((k) => !k.startsWith('skafinity:')), [...store.map.keys()].join(','));

  const quiet = makePlayer({ storage: null, pool: new RenderPool(() => new FakeWorker(), 1) });
  await quiet.load();
  quiet.setVolume(0.5);
  quiet.setShuffle(false);
  check('storage: null writes nothing at all', store.map.size === 2, `${store.map.size} keys`);
  check('…and still remembers within the session', quiet.volume === 0.5 && quiet.randomEverySong === false);
  one.destroy(); two.destroy(); quiet.destroy();
}

// ── The shuffle line is derived, not rolled ────────────────────────────────────
// Dropping a ledger entry must re-derive the identical vibe — that is what makes a shuffled line
// walkable backwards, shareable, and stable across a reload.
{
  const p = makePlayer({ pool: new RenderPool(() => new FakeWorker(), 1), shuffle: true });
  await p.load();
  const first = Array.from(p.cfgForN(7));
  p.ledger.delete(7);
  check('a dropped ledger entry re-derives identically', JSON.stringify(Array.from(p.cfgForN(7))) === JSON.stringify(first));
  p.setShuffle(false);
  check('shuffle off pins upcoming songs to the live cfg', p.cfgForN(p.displayN + 3) === p.cfg);
  p.destroy();
}

// ── It is genuinely headless ───────────────────────────────────────────────────
// The extraction is only worth anything if the transport stayed out of the document and out of the
// address bar; a single `document.` creeping back in is what would make a second widget impossible.
{
  const src = readFileSync(new URL('../web/player.js', import.meta.url), 'utf8');
  const code = src.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');
  check('the transport never touches the document', !/\bdocument\./.test(code));
  check('the transport never writes the URL', !/location\.(hash|href|search)\s*=|history\./.test(code));
  check('the transport never reaches for an id', !/getElementById|querySelector/.test(code));
  // localStorage may only be reached as the store's DEFAULT BACKING (which the namespacing and the
  // opt-out both sit in front of), never read or written inline.
  check('storage goes through the namespaced store', !/localStorage\s*\.\s*\w+Item/.test(code));
}

console.log(failures ? `\n${failures} failure(s)` : '\nall player checks passed');
process.exit(failures ? 1 : 0);
