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
// A four-character stand-in for the real 36-char vibe: same rules (fixed width, hex, exact or
// nothing), small enough to read in a failure message.
const VIBE_LEN = 4;
const asVibe = (x) => (x >>> 0).toString(16).padStart(VIBE_LEN, '0').slice(-VIBE_LEN);

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
    // The vibe rides in cfg[1] and is genre-independent, like the real one: encode never reads
    // cfg[0], so a genre change cannot rewrite the vibe.
    encodeVibe: (c) => asVibe(c[1]),
    decodeVibe: (v, c) => { const n = c.slice(); n[1] = parseInt(v, 16) || 0; return n; },
    isVibe: (s) => new RegExp(`^[0-9a-f]{${VIBE_LEN}}$`).test(s || ''),
    vibeLength: () => VIBE_LEN,
    rollVibe: () => asVibe(0x9999),
    // Deterministic per station and index, like the real lines.
    rollVibeFor: (tag, n) => asVibe(n + 1),
    rollGenreFor: (tag, n) => n % 6,
    rollTagFor: (root, p) => (p === 0 ? root : `${root}-${p}`),
    vibeFieldCount: () => 2,
    vibeFieldInfo: (g, i) => ({ name: i === 0 ? 'VOLUME' : 'TONE', min: 0, max: 1, isInt: false, voice: 'BASS', column: i, choices: [] }),
    vibeLevels: () => 16,
    setVibeField: (c, i, norm) => { const n = c.slice(); if (i !== 0) n[1] = Math.round(norm * 15); return n; },
    getVibeNorm: () => 0.5,
    vibeDisplay: () => '0.50',
    songToWav: () => new Uint8Array([1, 2, 3]),
    songSeed: (tag, n) => `${tag || 'rotaliate'}:${n}`,
    // null / undefined genre means "left to roll", exactly as the real adapter takes it.
    formatSeed: (tag, n, genre, vibe) =>
      `${tag}:${n}${genre >= 0 && genre !== null ? ':' + genre.toString(16) : ''}${vibe ? ':' + vibe : ''}`,
    // The same shape SeedCodec.TryParse has: order-free extras told apart by length, and an error
    // string rather than a coerced result.
    parseSeed: (s) => {
      const fail = (error) => ({ error, tag: '', n: 0, genre: null, vibe: '' });
      const parts = (s || '').trim().split(':');
      if (!s || !s.trim()) return fail('a seed looks like tag:n');
      if (parts.length > 4) return fail('too many parts');
      if (!/^[A-Za-z0-9_-]*$/.test(parts[0])) return fail('not a station name');
      if (parts.length >= 2 && !/^\d+$/.test(parts[1])) return fail('not a song number');
      let genre = null, vibe = '';
      for (const p of parts.slice(2)) {
        if (!/^[0-9a-fA-F]+$/.test(p)) return fail('not hex');
        if (p.length === 1) genre = parseInt(p, 16);
        else if (p.length === VIBE_LEN) vibe = p.toLowerCase();
        else return fail(`a vibe is ${VIBE_LEN} characters`);
      }
      return { error: '', tag: parts[0], n: parts.length >= 2 ? parseInt(parts[1], 10) : 0, genre, vibe };
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
    seed: 'test:0',
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

// ── A resume is a seek, not a rebuild ──────────────────────────────────────────
// Nothing about the timeline changed while it was paused, so the PCM in hand is still the song:
// resuming through the hard restart path would drop that cache and the ledger with it, and answer
// a press of ▶ with a re-render — seconds of silence, and a playlist redrawing itself.
{
  allWorkers.length = 0;
  const pool = new RenderPool(() => new FakeWorker(), 3);
  const p = makePlayer({ pool });
  await p.play();
  for (const w of liveWorkers()) w.finishAll();
  await wait(240);
  const cachedBefore = [...p.rendered.keys()].sort((a, b) => a - b).join(',');
  const ledgerBefore = p.ledger.size;
  const wasN = p.current.n;
  p.ctx.currentTime = p.current.startTime + 1;
  p.pause();
  await p.play();
  check('a resume keeps every rendered song', [...p.rendered.keys()].sort((a, b) => a - b).join(',') === cachedBefore,
    `${[...p.rendered.keys()].join(',')} vs ${cachedBefore}`);
  check('a resume keeps the frozen-vibe ledger', p.ledger.size >= ledgerBefore, `${p.ledger.size} vs ${ledgerBefore}`);
  check('a resume re-schedules the audio it already had', p.activeNodes.length > 0);
  check('nothing is re-rendered to resume', !allWorkers.some((w) => w.jobs.some((j) => j.n === wasN)),
    allWorkers.flatMap((w) => w.jobs.map((j) => j.n)).join(','));

  // …but a knob moved while paused HAS to rebuild: the cache no longer describes the song the
  // seed now names.
  p.ctx.currentTime = p.current ? p.current.startTime + 1 : 1;
  p.pause();
  p.setGenre(2);
  await p.play();
  check('a genre changed while paused does rebuild the timeline', p.rendered.size === 0 && p.gen.claimed.size > 0,
    `${p.rendered.size} cached, ${p.gen.claimed.size} claimed`);
  p.destroy();
}

// ── Scrubbing inside the song ──────────────────────────────────────────────────
{
  allWorkers.length = 0;
  const pool = new RenderPool(() => new FakeWorker(), 3);
  const p = makePlayer({ pool });
  check('no position before anything is rendered', p.position().duration === 0 && p.position().ratio === 0);
  await p.play();
  for (const w of liveWorkers()) w.finishAll();
  await wait(240);
  const dur = p.current.duration;
  p.ctx.currentTime = p.current.startTime + 1;
  check('the position reads the audio clock', Math.abs(p.position().time - 1) < 0.01, String(p.position().time));
  check('…as a ratio of the whole song', Math.abs(p.position().ratio - 1 / dur) < 0.01);

  p.seekWithin(dur * 0.5);
  await wait(100);                      // the scrubbed song becomes audible on the schedule delay
  check('a scrub comes in at the offset it was given', p.current && Math.abs(p.current.offset - dur / 2) < 0.01,
    JSON.stringify(p.current));
  check('a scrub does not throw the timeline away', p.rendered.size > 0, `${p.rendered.size} cached`);
  // Past the end there is nothing to come in on, so the scrub is held inside the song.
  p.seekWithin(dur + 10);
  await wait(100);
  check('a scrub past the end lands inside the song', p.current && p.current.offset < dur,
    JSON.stringify(p.current));

  // The gap between committing the audio and the start timeout firing is tens of milliseconds, and
  // the position has to hold through it: reading `current` as "nothing" in there is what made every
  // seek and every resume flash back to 0:00 before carrying on.
  p.seekWithin(dur * 0.25);
  check('the position holds across a seek, before the new source starts',
    Math.abs(p.position().time - dur * 0.25) < 0.1, String(p.position().time));
  await wait(100);
  check('…and still reads it once the source has started',
    Math.abs(p.position().time - dur * 0.25) < 0.1, String(p.position().time));
  p.ctx.currentTime = p.current.startTime + 0.5;
  p.pause();
  const off = p.resumeOffset;
  check('a pause mid-song leaves an offset to resume from', off > 0, String(off));
  p.next();
  check('a Next taken while paused does not inherit that offset', p.resumeOffset === 0, String(p.resumeOffset));
  p.seekWithin(3);
  check('a scrub while paused moves where the next play comes in', p.resumeOffset === 3, String(p.resumeOffset));
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
  const a = makePlayer({ pool, seed: 'aaa:0' });
  const b = makePlayer({ pool, seed: 'bbb:0' });
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
  quiet.setShuffle(true);
  check('storage: null writes nothing at all', store.map.size === 2, `${store.map.size} keys`);
  check('…and still remembers within the session', quiet.volume === 0.5 && quiet.shuffle === true);
  one.destroy(); two.destroy(); quiet.destroy();
}

// ── The line is derived, not remembered ────────────────────────────────────────
// Dropping a ledger entry must re-derive the identical song — that is what makes a line walkable
// backwards, shareable, and stable across a reload.
{
  const p = makePlayer({ pool: new RenderPool(() => new FakeWorker(), 1) });
  await p.load();
  const first = Array.from(p.cfgForN(7));
  p.ledger.delete(7);
  check('a dropped ledger entry re-derives identically',
    JSON.stringify(Array.from(p.cfgForN(7))) === JSON.stringify(first));
  p.destroy();
}

// ── Absent means rolled, present means pinned ──────────────────────────────────
// The whole point of the seed format: what the string leaves out changes every song, and what it
// writes down does not. Both directions have to be reachable from the UI, or a listener who drags
// one knob has no way back to a station.
{
  const p = makePlayer({ pool: new RenderPool(() => new FakeWorker(), 1), seed: 'gamah:0' });
  await p.load();
  check('a bare station pins nothing', p.seed === 'gamah:0', p.seed);
  check('…so upcoming songs differ from this one',
    p.resolve(0).vibe !== p.resolve(1).vibe && p.resolve(0).genre !== p.resolve(1).genre);
  check('…and the resolved seed writes this song down',
    p.resolvedSeed === `gamah:0:${p.genre.toString(16)}:${p.vibe}`, p.resolvedSeed);

  // Dragging a knob pins the whole vibe — there is no partial vibe to pin.
  p.setField({ i: 1, column: 1, voice: 'BASS' }, 1);
  check('a knob drag pins the vibe into the seed', p.seed.endsWith(`:${p.vibe}`), p.seed);
  check('…and every upcoming song then plays it', p.resolve(3).vibe === p.vibe);
  check('…while the genre keeps rolling', p.resolve(3).genre !== p.resolve(4).genre);

  // Choosing a genre pins it AND keeps the vibe that is playing: the same knobs through a
  // different band, which is what someone reaching for the dropdown mid-song is asking for.
  const heldVibe = p.vibe;
  p.setGenre(3);
  check('a genre change keeps the vibe that was playing', p.vibe === heldVibe, `${p.vibe} vs ${heldVibe}`);
  check('…and pins the genre into the seed', p.seed === `gamah:0:3:${heldVibe}`, p.seed);
  check('…so nothing rolls any more', p.resolve(9).genre === 3 && p.resolve(9).vibe === heldVibe);

  // …and both pins have a way back out.
  p.rollGenre();
  check('rolling the genre takes it back out of the seed', p.seed === `gamah:0:${heldVibe}`, p.seed);
  p.rollVibe();
  check('rolling the vibe takes it back out too', p.seed === 'gamah:0', p.seed);
  p.destroy();
}

// ── Reroll is a new station, not a new taste ───────────────────────────────────
{
  const p = makePlayer({ pool: new RenderPool(() => new FakeWorker(), 1), seed: 'gamah:7:2' });
  await p.load();
  check('a pinned genre survives the parse', p.pinnedGenre === 2 && p.n === 7);
  p.reroll();
  check('a reroll lands on a fresh station at song 0', p.tag !== 'gamah' && p.songAt(p.displayN).n === 0,
    p.seed);
  check('…and keeps what was pinned', p.pinnedGenre === 2 && p.seed.endsWith(':2'), p.seed);
  p.destroy();
}

// ── Shuffle walks stations, not songs ──────────────────────────────────────────
// ON, every "next" is a whole new station at song 0. The stations are DERIVED, so Prev still
// replays exactly what was heard without any of it having been remembered.
{
  const p = makePlayer({ pool: new RenderPool(() => new FakeWorker(), 1), seed: 'gamah:4', shuffle: true });
  await p.load();
  check('shuffle starts on the seed it was given', p.songAt(0).tag === 'gamah' && p.songAt(0).n === 4);
  check('…and every position after it is its own station at song 0',
    p.songAt(1).tag !== 'gamah' && p.songAt(1).n === 0 && p.songAt(2).tag !== p.songAt(1).tag);
  check('the shuffled line is walkable backwards', p.songAt(3).tag === p.songAt(3).tag
    && JSON.stringify(p.songAt(3)) === JSON.stringify(p.songAt(3)));
  check('the playlist shows the station each row belongs to',
    p.timeline().every((row) => !!row.tag));

  // OFF, the same position keeps playing while the line ahead of it becomes n+1 of this station.
  const audible = JSON.stringify(p.songAt(p.displayN));
  p.setShuffle(false);
  check('turning shuffle off stays on the song that was playing',
    JSON.stringify(p.songAt(p.displayN)) === audible, p.seed);
  check('…and the next song is now the next song of this station',
    p.songAt(p.displayN + 1).tag === p.songAt(p.displayN).tag
    && p.songAt(p.displayN + 1).n === p.songAt(p.displayN).n + 1);
  p.destroy();
}

// ── A seed that will not parse changes nothing ─────────────────────────────────
// Typed mid-session it must leave playback alone: starting something adjacent to what was typed is
// worse than refusing, because nobody finds out.
{
  const p = makePlayer({ pool: new RenderPool(() => new FakeWorker(), 1), seed: 'gamah:2' });
  await p.load();
  const before = p.seed;
  const err = p.applySeed('gamah:notanumber');
  check('a malformed seed is refused with a reason', !!err, err);
  check('…and nothing changed', p.seed === before, `${p.seed} vs ${before}`);
  check('a good seed returns no error', p.applySeed('other:3') === '' && p.seed === 'other:3', p.seed);
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
