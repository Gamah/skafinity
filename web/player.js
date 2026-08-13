// skafinity — the transport: scheduling, look-ahead, the navigable timeline. Headless.
//
// This is app.js's engine half with every embed-hostile thing taken out of it: no module-scope
// song state, no getElementById, no location.hash, no un-namespaced localStorage key. What is
// left is a class you can have two of on one page, which is the whole reason the file exists —
// the UI (web/skafinity-element.js) and the page (web/app.js) are consumers of this, not owners
// of it.
//
// What it still owns, and what a host must not duplicate:
//   * the generation queue's claims (web/queue.js — claimed === queued ∪ in-flight);
//   * the crossfade and the fade-up-from-silence, which are different lengths for a reason;
//   * the frozen-vibe ledger that makes the shuffled line walkable in both directions.
//
// What it deliberately does NOT do: touch the document, read or write the URL, or pick a storage
// key for you. A host that wants the URL to be the song wires that up from the `song` event.
import Skafinity from './engine.js';
import { GenQueue } from './queue.js';
import { pickAudioFormat, encodeSong } from './encode.js';

// ── Tunables (mirror MusicController defaults) ──
// Songs have an intro→…→ending structure, so each plays once start-to-end (no internal loop) and
// crossfades into the next.
const LOOPS_PER_SONG = 1;
// Crossfade length, in seconds. This is a CEILING, not the value used: the real fade is capped to
// the song's ring-out tail (mod.ringOutSeconds()), because a fade longer than the tail starts the
// next song before the current one's final chord has landed — two songs, two tempos and two
// downbeats playing at once.
const CROSSFADE = 3.75;
// COMING UP FROM SILENCE IS NOT A CROSSFADE, and it must not borrow the crossfade's length. A
// crossfade is long because two songs have to trade places without either being heard to stop; at
// the start of a sequence there is nothing to trade with. What a multi-second ramp does to a drum
// kit is specific rather than merely quiet: it crushes the STRIKE and lets the RING arrive at full
// level a second later, so every cymbal in the opening bars sounds like it was hit before the song
// started. Long enough not to click, and nothing more.
//
// MEASURED, on four real renders: dry, the strike sits +2.5 to +5.6 dB above the ring half a second
// later. Through the 2.9 s crossfade it comes back at -28 to -31 dB — a 34 dB inversion. Through
// 0.15 s it is still 14-17 dB out. At 0.01 s the balance is the dry balance to within a tenth of a
// dB on every seed, and 441 samples at 44.1 kHz is an ample de-click. So this is a measurement,
// not a taste call.
const START_FADE = 0.012;
const AHEAD_COUNT = 4;        // songs kept pre-rendered
const SCHEDULE_HORIZON = 12;  // seconds: schedule the next song once it's within this
// Radius of the rendered-PCM cache kept around the audible song, so Prev/Next within the window is
// instant; anything further is dropped and regenerated from the ledger on demand.
const PCM_RADIUS = 5;
// Pool of generation workers. Each boots its own .NET runtime (memory cost — hence the cap), and
// the pool lets look-ahead songs render in parallel instead of serializing through one worker.
//
// THE POOL IS SHARED ACROSS PLAYERS ON THE PAGE, and that is not an optimisation. Three runtimes
// per embed is what a per-instance pool would cost, so a page with three widgets would boot nine —
// which is not a widget, it is a denial of service against the page that embedded it.
const POOL_SIZE = 3;

// ── Shared, page-wide singletons ───────────────────────────────────────────────
// Browsers cap AudioContexts per document (and each one is a hardware stream), so instances share
// one unless a host injects its own — a host that already runs Web Audio wants its own graph, and
// passing it in is how it keeps one.
let _sharedCtx = null;
export function sharedAudioContext() {
  if (!_sharedCtx) {
    const Ctor = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!Ctor) throw new Error('skafinity: this browser has no Web Audio API');
    _sharedCtx = new Ctor();
  }
  return _sharedCtx;
}

// The default worker factory. `new Worker(url, {type:'module'})` refuses a cross-origin script, so
// an element served from a CDN cannot construct its worker directly from the module URL — the same
// wall tools/bundle-single.mjs hits, and the same way out: a same-origin blob module whose only
// statement is an absolute import of the real worker. The import itself is cross-origin, which is
// allowed for module scripts as long as the server sends CORS headers (docs/embedding.md says so
// out loud, because a host that misses it gets a widget that boots and then never renders).
export function defaultCreateWorker() {
  const url = new URL('./worker.js', import.meta.url);
  if (typeof location !== 'undefined' && url.origin === location.origin)
    return new Worker(url, { type: 'module' });
  const shim = URL.createObjectURL(new Blob(
    [`import ${JSON.stringify(url.href)};`], { type: 'text/javascript' }));
  const w = new Worker(shim, { type: 'module' });
  URL.revokeObjectURL(shim);   // the worker has it; the URL entry is not needed past construction
  return w;
}

// The worker pool. Players register with it and are asked for work; a slot remembers which player
// it is rendering for so a reply can be routed and so a seek can terminate only its OWN renders.
export class RenderPool {
  constructor(createWorker, size = POOL_SIZE) {
    this.createWorker = createWorker;
    this.size = size;
    this.slots = [];
    this.players = [];
    this.rr = 0;               // round-robin cursor, so one busy player cannot starve another
  }

  register(p) { if (!this.players.includes(p)) this.players.push(p); this.spawn(); }
  unregister(p) {
    this.players = this.players.filter((x) => x !== p);
    for (const slot of this.slots) if (slot.owner === p) this.recycle(slot);
    if (!this.players.length) { for (const s of this.slots) { try { s.worker.terminate(); } catch (_) {} } this.slots = []; }
  }

  spawn() {
    while (this.slots.length < this.size) {
      const slot = { worker: null, busy: false, owner: null, n: -1, i: this.slots.length };
      this.slots.push(slot);
      this.boot(slot);
    }
  }
  boot(slot) {
    slot.worker = this.createWorker();
    slot.worker.onmessage = (e) => this.onMessage(slot, e.data);
    slot.busy = false; slot.owner = null; slot.n = -1;
  }
  // Terminate whatever a slot is doing and replace it — the only true cancellation available, and
  // it costs a runtime reboot, which is why callers are picky about when they use it.
  recycle(slot) {
    try { slot.worker.terminate(); } catch (_) {}
    this.boot(slot);
  }

  // How many slots one player may hold while others are waiting. Look-ahead is SPECULATIVE — it is
  // for a song 80 seconds away — so one widget's fourth look-ahead render must never be the reason
  // another widget's first song has not started.
  fairShare() {
    const needy = this.players.filter((p) => p.gen.queue.length || this.held(p)).length;
    return Math.max(1, Math.floor(this.size / Math.max(1, needy)));
  }
  held(p) { return this.slots.filter((s) => s.busy && s.owner === p).length; }

  // Hand queued work to idle workers, round-robin across players and never past a player's share
  // while someone else is queued behind it.
  dispatch() {
    for (const slot of this.slots) {
      if (slot.busy) continue;
      const share = this.fairShare();
      const others = this.players.filter((p) => p.gen.queue.length);
      let job = null, owner = null;
      for (let k = 0; k < this.players.length && !job; k++) {
        const p = this.players[(this.rr + k) % this.players.length];
        // Over its share, and somebody else is actually waiting? Let them go first.
        if (this.held(p) >= share && others.some((o) => o !== p && this.held(o) < share)) continue;
        job = p._takeJob();
        if (job) { owner = p; this.rr = (this.rr + k + 1) % this.players.length; }
      }
      if (!job) break;
      slot.busy = true; slot.owner = owner; slot.n = job.n;
      slot.worker.postMessage(job.msg);
    }
  }

  // A player has nothing to play RIGHT NOW (a fresh play, or a seek onto an uncached song) and the
  // pool is full. If another player is over its fair share, one of its renders is look-ahead and
  // this one is not, so the look-ahead loses: terminate the slot furthest from that player's
  // playhead and give it here. A terminate costs a runtime reboot, so it happens only in this
  // case — a starving player against a speculative render — and never for look-ahead vs look-ahead.
  preemptFor(player) {
    if (this.slots.some((s) => !s.busy)) { this.dispatch(); return; }
    const share = this.fairShare();
    if (this.held(player) >= share) return;
    let victim = null;
    for (const s of this.slots) {
      if (s.owner === player || !s.owner || this.held(s.owner) <= share) continue;
      if (!victim || Math.abs(s.n - s.owner.displayN) > Math.abs(victim.n - victim.owner.displayN)) victim = s;
    }
    if (!victim) return;
    victim.owner.gen.release(victim.n);
    this.recycle(victim);
    this.dispatch();
  }

  onMessage(slot, m) {
    const owner = slot.owner;
    slot.busy = false; slot.owner = null; slot.n = -1;
    if (owner && m && (m.type === 'song' || m.type === 'error')) owner._onRender(m);
    this.dispatch();
  }

  // Terminate this player's in-flight renders that `pred(n)` rejects, releasing their claims.
  abandon(player, pred) {
    for (const slot of this.slots) {
      if (!slot.busy || slot.owner !== player) continue;
      if (pred(slot.n)) continue;
      player.gen.release(slot.n);
      this.recycle(slot);
    }
  }
}

let _sharedPool = null;
function sharedPool() {
  if (!_sharedPool) _sharedPool = new RenderPool(defaultCreateWorker);
  return _sharedPool;
}

// ── Storage — namespaced per instance, or none ─────────────────────────────────
// An embed writing bare `skafinity.vol` into someone else's origin is a collision waiting to
// happen, both with the host page and with a second widget on it. Keys are
// `skafinity:<key>:<name>`; `storage: null` opts out entirely and keeps everything in memory.
function makeStore(backing, key) {
  const prefix = `skafinity:${key}:`;
  const mem = new Map();
  const ok = (() => {
    if (!backing) return false;
    try { backing.setItem(prefix + '_', '1'); backing.removeItem(prefix + '_'); return true; }
    catch (_) { return false; }   // Safari private mode, a blocked third-party origin, quota
  })();
  return {
    get(name) { try { return ok ? backing.getItem(prefix + name) : (mem.has(name) ? mem.get(name) : null); } catch (_) { return null; } },
    set(name, value) { try { ok ? backing.setItem(prefix + name, value) : mem.set(name, value); } catch (_) {} },
  };
}

const lower = (s) => (s || '').trim().toLowerCase();
// A short base-36 tag, e.g. "bd44ac2a" — the random song name used on a fresh visit.
export const randomTag = () => Math.random().toString(36).slice(2, 10);

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

/**
 * A headless skafinity transport. Emits:
 *   progress {loaded,total,ratio}  runtime download, before `ready`
 *   ready    {}                    the engine is up; vibe fields can be read
 *   song     {n,seed,vibe,tag,genre,genreName}  a new song became audible
 *   vibe     {}                    the live cfg changed (rebuild any editor)
 *   state    {playing}
 *   position {n,time,duration,ratio,playing}  a jump in the playhead (pause/resume/scrub/new song);
 *                                             for a moving bar, poll position() instead
 *   buffer   {generating,n}        playback stalled on a song being rendered
 *   timeline {}                    the playlist window changed
 *   error    {error}
 */
export class SkafinityPlayer extends EventTarget {
  constructor(opts = {}) {
    super();
    this.opts = opts;
    this.injectedCtx = opts.audioContext || null;
    // An injected factory gets its OWN pool: the shared one exists so N widgets cost 3 runtimes,
    // and a host that supplies a different worker means a different runtime, which cannot be shared.
    this.pool = opts.pool || (opts.createWorker ? new RenderPool(opts.createWorker, opts.poolSize || POOL_SIZE) : sharedPool());
    this.store = makeStore(
      opts.storage === null ? null : (opts.storage || (typeof localStorage !== 'undefined' ? localStorage : null)),
      opts.storageKey || 'default');
    this.configUrl = opts.configUrl === undefined ? './config.json' : opts.configUrl;

    this.mod = null;
    this.booting = null;
    // Seconds of ring-out a song reserves past its last bar; read from the engine at boot. The
    // fallback only covers the window before the runtime is up.
    this.tailSeconds = 2.5;

    this.cfg = null;
    // ── The seed, taken apart ──
    // `tag` is the station and `baseN` the index it was joined at. `pinnedGenre` / `pinnedVibe` are
    // what the seed WROTE DOWN; null/'' means the seed left that part to roll, and it then changes
    // with every song. `genre`/`vibe` are what the audible song actually resolved to, pinned or not.
    this.tag = opts.tag || randomTag();
    this.baseN = 0;
    this.pinnedGenre = null;
    this.pinnedVibe = '';
    this.genre = 0;
    this.vibe = '';
    // POSITIONS along the timeline, not song indices — the two are the same thing when shuffle is
    // off, and are not when it is on (every position is then its own station at song 0). Everything
    // keyed by n in this file — the queue, the ledger, the PCM cache, Prev/Next — is keyed by
    // position; `songAt` is the one place that turns one into a station and an index.
    this.n = 0;                 // next position to schedule
    this.displayN = 0;          // position currently audible
    this.pendingSeed = opts.seed || '';
    // Per-instrument volumes, keyed by voice NAME (BASS, DRUMS, …) so a level follows the
    // instrument across genres. Pulled out of the (shareable) vibe seed; a local preference
    // overlaid onto cfg after every seed/genre change.
    this.vols = (() => { try { return JSON.parse(this.store.get('vol')) || {}; } catch (_) { return {}; } })();
    // 🎲 Shuffle. NOT "random every song" any more — that is what a seed with nothing pinned does
    // by itself, and it needs no switch. Shuffle is about the LINE: on, every "next" is a whole new
    // station at song 0 rather than the next song of this one. OFF by default, because walking one
    // station is what makes `n` mean something and what a shared link describes. An explicit ON is
    // remembered.
    this.shuffle = opts.shuffle !== undefined ? !!opts.shuffle : this.store.get('shuffle') === '1';

    this.ledger = new Map();    // n -> frozen cfg (the shuffle line)
    this.rendered = new Map();  // n -> { buffer, info }
    this.gen = new GenQueue();
    this.bufferingN = -1;
    // Two counters, because a seek and a restart invalidate different things. `seq` is the AUDIO
    // SCHEDULE: bumped whenever committed nodes are torn down, so stale setTimeouts bail out.
    // `renderSeq` is what a rendered song was rendered FOR: bumped only when the cfg behind an
    // index changes, because that is the only thing that can make an in-flight render wrong. A seek
    // moves where we are in a timeline whose vibes are pinned per index — the render in flight is
    // still the right song.
    this.seq = 0;
    this.renderSeq = 0;
    this.playing = false;
    this.reqId = 0;

    this.ctx = null;
    this.masterGain = null;
    this.volume = opts.volume !== undefined ? opts.volume : parseFloat(this.store.get('master') ?? '0.8');
    this.activeNodes = [];
    this.nextN = 0;
    this.nextTime = 0;
    this.firstScheduled = false;
    this.current = null;        // {n, startTime, offset, duration} of the audible song
    this.resumeOffset = 0;      // seconds into it that a pause was taken at
    this.pendingOffset = 0;     // …handed to the next first-song schedule, once
    // Does the timeline behind the cached PCM still describe what a play would produce? A vibe,
    // genre or seed change made while PAUSED cannot restart anything, so it records the fact here
    // and the next play throws the timeline away. Without it a resume either replays a song the
    // knobs no longer describe, or — if every play rebuilds to be safe — re-renders a song it is
    // already holding, which is a multi-second silence and a playlist full of progress bars for a
    // press of ⏸ then ▶.
    this.dirty = true;
    this.restartTimer = null;
    this.tick = null;
    this.destroyed = false;
  }

  emit(type, detail = {}) { this.dispatchEvent(new CustomEvent(type, { detail })); }

  // ── Boot ─────────────────────────────────────────────────────────────────────
  // NOTHING is fetched until this is called. An embed that boots on page view costs every visitor
  // ~7 MB of runtime for a widget they may never press play on, so the caller decides when.
  load() {
    if (this.booting) return this.booting;
    this.booting = (async () => {
      // `engine` lets a host hand in an already-booted engine instead of having this instance boot
      // one — a second widget on a page that already has the runtime, or a test that wants the
      // transport without 7.5 MB of wasm behind it.
      this.mod = await (this.opts.engine || Skafinity({ onProgress: (p) => this.emit('progress', p) }));
      if (this.destroyed) return this.mod;
      if (this.mod.ringOutSeconds) this.tailSeconds = this.mod.ringOutSeconds();
      this.cfg = this.mod.defaultConfig();
      await this.applyHouseConfig();
      this.applyInitialSeed();
      this.pool.register(this);
      // Songs are ~80 s, so the next must be queued as its start approaches even when no worker
      // render just landed (mirrors MusicController's per-tick top-up).
      this.tick = setInterval(() => { if (this.playing && this.ctx) this.pump(); }, 250);
      this.emit('ready', {});
      this.emitSong();
      return this.mod;
    })().catch((e) => { this.emit('error', { error: e }); throw e; });
    return this.booting;
  }
  get ready() { return !!this.mod; }

  // Overlay an optional config.json onto the base cfg: the baseline-mix tuning (peak balances, kit
  // presence). Missing file / bad JSON / unknown keys are ignored silently — an embed on someone
  // else's origin will usually have no config.json to find, and that is not an error.
  async applyHouseConfig() {
    if (!this.configUrl) return;
    try {
      const url = new URL(this.configUrl, import.meta.url);
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) return;
      const data = await res.json();
      const advanced = (data && typeof data.advanced === 'object') ? data.advanced : data;
      this.cfg = this.mod.applyAdvancedConfig(this.cfg, advanced);
    } catch (_) { /* no config.json (or invalid) — keep engine defaults */ }
  }

  applyInitialSeed() {
    const p = this.pendingSeed ? this.mod.parseSeed(this.pendingSeed) : null;
    if (p && !p.error) {
      this.tag = p.tag;
      this.baseN = Math.max(0, p.n);
      this.pinnedGenre = p.genre;
      this.pinnedVibe = p.vibe;
      this.n = this.shuffle ? 0 : this.baseN;
    } else {
      // A fresh instance lands somewhere new: a random station at song 0, with the genre and the
      // knobs both left to roll — which is a shuffled station out of the box without any switch
      // being set. A seed that would not parse lands here too, and the caller has been told why.
      if (p && p.error) this.emit('error', { error: new Error(`skafinity: ${p.error}`) });
      const stored = parseInt(this.store.get('n') ?? '', 10);
      if (Number.isFinite(stored) && stored >= 0) this.baseN = stored;
      this.n = this.shuffle ? 0 : this.baseN;
    }
    this.displayN = this.n;
    this.adoptSong(this.displayN);
  }

  // ── The timeline, and what a position resolves to ────────────────────────────────────────
  // Shuffle OFF: one station, walked by song index — position IS the index. Shuffle ON: every
  // position is its own station at song 0, and those stations are DERIVED from the root
  // (SeedCodec.RollTagFor) rather than drawn fresh. That is what keeps a shuffled line a line:
  // Prev replays exactly what was heard without anything having been remembered, and the whole
  // thing is still reproducible from one string. Position 0 is always the seed as given, so a
  // pasted link plays the song it names before the shuffle takes over.
  songAt(p) {
    p = Math.max(0, p | 0);
    // One station: the position IS the song index, which is what makes Prev/Next arithmetic and
    // lets you walk back to a song fifty ago that nothing remembers. `baseN` is only where the
    // seed joined the line.
    if (!this.shuffle) return { tag: this.tag, n: p };
    return p === 0 ? { tag: this.tag, n: this.baseN } : { tag: this.mod.rollTagFor(this.tag, p), n: 0 };
  }

  // What position `p` plays: the pinned genre/vibe where the seed wrote one down, else the ones
  // rolled off that position's own station and index.
  resolve(p) {
    const { tag, n } = this.songAt(p);
    return {
      tag,
      n,
      genre: this.pinnedGenre !== null ? this.pinnedGenre : this.mod.rollGenreFor(tag, n),
      vibe: this.pinnedVibe || this.mod.rollVibeFor(tag, n),
    };
  }

  // Adopt position `p`'s song as the live one: what the editor shows, what the seed box says, and
  // what a copied link reproduces.
  adoptSong(p) {
    const r = this.resolve(p);
    this.genre = r.genre;
    this.vibe = r.vibe;
    this.cfg = this.mod.setGenre(this.mod.decodeVibe(r.vibe, this.cfg), r.genre);
    this.applyStoredVolumes();
  }

  // ── Seeds ────────────────────────────────────────────────────────────────────
  // The PRNG stream a position is composed from — the engine's spelling (SeedCodec.SongSeed), not
  // this host's. It decides what song an untagged seed resolves to, so a host that spells the
  // fallback station differently plays a different song from the same seed.
  seedFor(p) { const s = this.songAt(p); return this.mod.songSeed(s.tag, s.n); }

  // THE SEED AS IT STANDS — what a rolling station is. Rolled parts are left out, so this string
  // keeps rolling for whoever is handed it. Before the runtime is up it is the seed we were HANDED:
  // the only string that reproduces anything, and the one a link carried, so a widget nobody has
  // pressed play on yet still shows what it was sent rather than a placeholder it is about to
  // throw away.
  get seed() {
    if (!this.mod) return this.pendingSeed;
    const s = this.songAt(this.displayN);
    return this.mod.formatSeed(s.tag, s.n, this.pinnedGenre, this.pinnedVibe);
  }
  set seed(s) { this.applySeed(s); }

  // THE SEED FULLY RESOLVED — this song, written down. Everything the station left to chance is
  // spelled out, so the recipient hears exactly what is playing here rather than whatever their
  // own roll produces at that index.
  get resolvedSeed() {
    if (!this.mod) return this.pendingSeed;
    const s = this.songAt(this.displayN);
    return this.mod.formatSeed(s.tag, s.n, this.genre, this.vibe);
  }

  /** Take a seed string. Returns '' on success, or the reason it was refused — in which case
   *  NOTHING changed: a seed typed mid-session leaves playback alone, and one that arrived from a
   *  link leaves the widget silent rather than playing something adjacent to what was sent. */
  applySeed(s) {
    if (!this.mod) { this.pendingSeed = s; return ''; }
    const p = this.mod.parseSeed(s);
    if (p.error) return p.error;
    this.tag = p.tag;
    this.baseN = Math.max(0, p.n);
    this.pinnedGenre = p.genre;
    this.pinnedVibe = p.vibe;
    this.n = this.shuffle ? 0 : this.baseN;
    this.displayN = this.n;
    this.adoptSong(this.displayN);
    this.dirty = true;
    this.resumeOffset = 0;      // a different song: there is nothing to resume into
    this.emit('vibe', {});
    this.emitSong();
    if (this.playing) this.startSequence();
    return '';
  }

  emitSong() {
    if (!this.mod) return;
    const s = this.songAt(this.displayN);
    this.emit('song', {
      n: s.n, position: this.displayN, seed: this.seed, resolvedSeed: this.resolvedSeed,
      vibe: this.vibe, tag: s.tag, genre: this.genre, genreName: this.mod.genreName(this.genre),
      genrePinned: this.pinnedGenre !== null, vibePinned: !!this.pinnedVibe,
    });
  }

  // ── The vibe ─────────────────────────────────────────────────────────────────
  // One field's index + cached info for the current genre. The layout is driven entirely from the
  // wasm field metadata, so a new genre — or a new knob — is a pure-C# change.
  fields() { return this.fieldsFor(this.genre); }
  fieldsFor(genre) {
    const out = [];
    const count = this.mod.vibeFieldCount(genre);
    for (let i = 0; i < count; i++) out.push({ i, ...this.mod.vibeFieldInfo(genre, i) });
    return out;
  }
  fieldDisplay(i) { return this.mod.vibeDisplay(this.cfg, i); }
  fieldNorm(i) { return this.mod.getVibeNorm(this.cfg, i); }
  levels() { return this.mod.vibeLevels(); }

  // Overlay the stored per-voice volumes onto cfg for the current genre (voices without a saved
  // level keep the song default).
  applyStoredVolumes() {
    for (const f of this.fields())
      if (f.column === 0 && f.voice && this.vols[f.voice] !== undefined)
        this.cfg = this.mod.setVibeField(this.cfg, f.i, this.vols[f.voice]);
  }

  // Moving a knob PINS the vibe: from here on the string carries these 36 values and every song
  // plays them, instead of rolling its own. There is no half-way — a vibe is the whole grid — so
  // the way back out is the explicit "roll the vibe again" below, not a knob returned to where it
  // started.
  setField(f, norm) {
    this.cfg = this.mod.setVibeField(this.cfg, f.i, norm);
    if (f.column === 0 && f.voice) {
      // Per-instrument volume: a local mix preference, not part of the shareable seed.
      this.vols[f.voice] = norm;
      this.store.set('vol', JSON.stringify(this.vols));
    } else {
      this.vibe = this.mod.encodeVibe(this.cfg);
      this.pinnedVibe = this.vibe;
      this.emitSong();
    }
    this.dirty = true;
    // Debounce-restart so a slider drag isn't a generation storm (≈0.35 s, like the game).
    clearTimeout(this.restartTimer);
    this.restartTimer = setTimeout(() => { if (this.playing) this.startSequence(); }, 350);
  }

  // Choosing a genre PINS it — and pins the vibe that is playing with it. The vibe is
  // genre-independent, so this is deliberately the same 36 knobs heard through a different band
  // rather than a new song: someone who reaches for the dropdown mid-song is asking what THIS
  // would sound like as metal. Leaving the vibe unpinned would roll a fresh one on the restart and
  // the change would land as a reroll instead.
  setGenre(g) {
    this.pinnedGenre = g;
    this.pinnedVibe = this.vibe;
    this.genre = g;
    this.cfg = this.mod.setGenre(this.cfg, g);
    this.applyStoredVolumes();
    this.dirty = true;
    this.emit('vibe', {});
    this.emitSong();
    if (this.playing) this.startSequence();
  }

  /** Hand the genre back to the station: every song rolls its own again. The way out of a chosen
   *  genre, which a dropdown with no "Random" entry does not have. */
  rollGenre() {
    this.pinnedGenre = null;
    this.restartFromSeed();
  }

  /** Throw the knobs somewhere new and PIN them there — the 🎲 over the sliders. Distinct from
   *  `rollVibe` below, and the difference is the whole reason both exist: this one always moves
   *  every slider, because it draws a fresh vibe and writes it into the seed. */
  rerollVibe() {
    this.pinnedVibe = this.mod.rollVibe();
    this.restartFromSeed();
  }

  /** Hand the vibe back to the station: every song rolls its own again. The way out of a dragged
   *  knob — and it may well move nothing on screen, because the song already playing keeps the
   *  vibe it resolved to; what changes is what comes NEXT. */
  rollVibe() {
    this.pinnedVibe = '';
    this.restartFromSeed();
  }

  /** Reroll the SEED: a fresh random station at song 0. Anything pinned stays pinned, because a
   *  pin is a choice and this is a request for a different song, not a different taste. */
  reroll() {
    this.tag = randomTag();
    this.baseN = 0;
    this.n = 0;
    this.restartFromSeed();
  }

  // Re-resolve the audible song from the seed and rebuild the timeline under it. The shared tail of
  // every control that changes what the seed MEANS.
  restartFromSeed() {
    this.displayN = this.n;
    this.adoptSong(this.displayN);
    this.dirty = true;
    this.emit('vibe', {});
    this.emitSong();
    if (this.playing) this.startSequence(); else this.emit('timeline', {});
  }

  // Flip shuffle: on, every "next" is a new station rather than the next song of this one. The
  // timeline behind every position changes, so this rebuilds from the current position rather than
  // trying to keep a cache that now describes different songs.
  setShuffle(on) {
    if (this.shuffle === !!on) return;
    // Read the audible song BEFORE the flag moves — songAt answers under whichever mode is set,
    // and the point of this whole block is to carry that song across the change.
    const here = this.songAt(this.displayN);
    this.shuffle = !!on;
    this.store.set('shuffle', on ? '1' : '0');
    // Stay on the song that is playing, so the switch is heard as a change of what comes NEXT
    // rather than as a jump. Shuffled, that song becomes position 0 of the new line (the root, in
    // both modes); unshuffled, the position is its song index again.
    this.tag = here.tag;
    this.baseN = here.n;
    this.n = this.shuffle ? 0 : here.n;
    this.restartFromSeed();
  }

  // The cfg to render position `p` with. Everything it needs is DERIVED from the seed, so the
  // ledger is a cache and nothing more — dropping an entry re-derives the identical song, which is
  // what makes the line walkable in both directions, shareable, and stable across a reload.
  cfgForN(p) {
    if (this.ledger.has(p)) return this.ledger.get(p);
    const r = this.resolve(p);
    let c = this.mod.setGenre(this.mod.decodeVibe(r.vibe, this.cfg), r.genre);
    // Volumes are a local mix preference and never ride in the seed, so they are overlaid per song.
    for (const f of this.fieldsFor(r.genre))
      if (f.column === 0 && f.voice && this.vols[f.voice] !== undefined)
        c = this.mod.setVibeField(c, f.i, this.vols[f.voice]);
    this.ledger.set(p, c);
    return c;
  }
  // The genre position p resolves to, for the queue view. Every upcoming genre is knowable up
  // front, so the queue shows what is actually coming rather than a placeholder.
  genreForN(p) { return this.resolve(p).genre; }

  // ── Generation ───────────────────────────────────────────────────────────────
  requestSong(nn) {
    if (this.rendered.has(nn)) return;
    if (this.gen.want(nn)) this.pool.dispatch();
  }

  // Called by the pool when a worker is free. Returns the next job or null.
  _takeJob() {
    if (this.destroyed || !this.mod) return null;
    const nn = this.gen.take();
    if (nn === undefined) return null;
    return {
      n: nn,
      msg: { type: 'gen', id: ++this.reqId, n: nn, mySeq: this.renderSeq,
             seed: this.seedFor(nn), cfg: this.cfgForN(nn).slice() },
    };
  }

  _onRender(m) {
    if (this.destroyed) { this.gen.release(m.n); return; }
    this.gen.release(m.n);
    if (m.type === 'error') { this.emit('error', { error: m.error, n: m.n }); this.emit('timeline', {}); return; }
    if (m.mySeq !== undefined && m.mySeq !== this.renderSeq) return;
    if (!this.ctx) return;
    const buf = this.ctx.createBuffer(2, m.left.length, m.sampleRate);
    buf.copyToChannel(m.left, 0);
    buf.copyToChannel(m.right, 1);
    this.rendered.set(m.n, { buffer: buf, info: m.info });
    this.pump();
    this.emit('timeline', {});
    this.emitBuffer();
  }

  // Drop rendered PCM outside the ±PCM_RADIUS window around `center` (the audible song by default;
  // the seek target while a jump is still buffering). The ledger of frozen vibes is kept regardless.
  pruneCache(center = this.displayN) {
    for (const k of [...this.rendered.keys()]) if (Math.abs(k - center) > PCM_RADIUS) this.rendered.delete(k);
  }

  // ── Transport ────────────────────────────────────────────────────────────────
  async ensureContext() {
    if (!this.ctx) {
      this.ctx = this.injectedCtx || sharedAudioContext();
      this.masterGain = this.ctx.createGain();
      this.masterGain.gain.value = this.volume;
      this.masterGain.connect(this.ctx.destination);
    }
    // Browsers require a user gesture before audio; a shared context may already be running for
    // another instance, in which case this is a no-op.
    if (this.ctx.state === 'suspended') await this.ctx.resume();
  }

  async play() {
    await this.load();
    await this.ensureContext();
    this.playing = true;
    // Only a resume consumes the offset: every other route into a restart (a new seed, a genre, a
    // vibe edit) is a different song and starts at its downbeat.
    const offset = this.resumeOffset;
    this.resumeOffset = 0;
    // A RESUME IS A SEEK, NOT A RESTART. The song is already rendered and the timeline behind it is
    // still the timeline, so the soft path re-schedules the PCM already in hand and audio is back
    // within a frame. `startSequence` would drop that cache, the ledger and every look-ahead render
    // in flight — several seconds of silence, and the playlist redrawing itself as it re-earns what
    // it had. That is only the right answer when the knobs moved while we were paused.
    if (this.dirty || !this.ctx) this.startSequence(offset);
    else this.seekTo(this.n, offset);
    this.emit('state', { playing: true });
    this.emit('position', this.position());
  }

  // Pause SUSPENDS nothing: with one AudioContext across several widgets, suspending it would stop
  // the other widget too, and that is not this one's call. So pausing tears this instance's nodes
  // down — and to still come back where it left off rather than at the top of the song, it keeps
  // how far into the audible song it got and hands that to the next `start()` as an offset.
  pause() {
    this.playing = false;
    const c = this.current;
    if (c && this.ctx) {
      // A song scheduled but not started yet is at its offset, not before it — the clamp is what
      // makes a pause during those few milliseconds keep the place a seek just chose.
      const into = Math.max(0, this.ctx.currentTime - c.startTime) + c.offset;
      // Right at the end there is nothing left to resume INTO; start the next song cleanly instead.
      this.resumeOffset = into < c.duration - 0.25 ? into : 0;
      this.n = c.n + (this.resumeOffset ? 0 : 1);
    }
    this.stopNodes();
    this.emit('state', { playing: false });
    this.emit('position', this.position());
  }
  toggle() { return this.playing ? (this.pause(), Promise.resolve()) : this.play(); }

  // ── Where we are in the song ─────────────────────────────────────────────────
  // Whole seconds of PCM are in hand, so the position is arithmetic on the audio clock rather than
  // anything the engine has to be asked for. A host polls this (rAF) for a progress bar; the
  // `position` event only marks the discontinuities — a pause, a resume, a scrub.
  //
  // `duration` is 0 when the song is not rendered (nothing playing yet, or a seek still buffering).
  // That is the honest answer, not a guess: songs differ in length, so a bar drawn against an
  // assumed one would be wrong for the whole first song.
  position() {
    const c = this.current;
    const duration = c ? c.duration : this.durationOf(this.displayN);
    let time;
    if (c && this.ctx) time = Math.min(Math.max(0, this.ctx.currentTime - c.startTime + c.offset), duration);
    else time = duration > 0 ? Math.min(this.resumeOffset, duration) : this.resumeOffset;
    return { n: this.displayN, time, duration, ratio: duration > 0 ? time / duration : 0, playing: this.playing };
  }
  durationOf(nn) {
    const r = this.rendered.get(nn);
    return r ? r.buffer.duration * LOOPS_PER_SONG : 0;
  }

  // Scrub inside the audible song. Web Audio nodes cannot be rewound, so this re-schedules the same
  // buffer from an offset — the SOFT path, because the timeline is not what changed. Paused, it just
  // moves where the next play will come in.
  seekWithin(seconds) {
    const duration = this.position().duration;
    // Landing on the last instant would schedule a song with nothing left to play; leave the tail.
    const t = Math.max(0, duration > 0 ? Math.min(seconds, Math.max(0, duration - 0.25)) : seconds);
    if (!this.playing || !this.ctx) {
      this.resumeOffset = t;
      this.emit('position', this.position());
      return;
    }
    this.seekTo(this.displayN, t);
    this.emit('position', this.position());
  }

  stopNodes() {
    this.seq++;
    for (const node of this.activeNodes) {
      try { node.stop && node.stop(); } catch (_) {}
      try { node.disconnect(); } catch (_) {}
    }
    this.activeNodes = [];
    this.firstScheduled = false;
    // Nothing is audible any more, so there is no position to resume from; a pause has already
    // taken its offset by the time it gets here.
    this.current = null;
  }

  setVolume(v) {
    this.volume = v;
    this.store.set('master', String(v));
    if (this.masterGain) this.masterGain.gain.value = v;
  }

  next() { this.seekTo(this.displayN + 1); }
  prev() { this.seekTo(this.displayN - 1); }

  // Schedule one song starting at startTime; returns the time the NEXT song should start
  // (overlapping this one's fade-out for an equal-power crossfade). `fromSilence` is true for the
  // first song of a sequence — every other song's fade-in overlaps the previous song's fade-out and
  // the two sum to constant power, which is what a crossfade is.
  // `offset` is seconds into the buffer to start at — non-zero only for the song a pause was taken
  // out of (see pause()).
  scheduleOneSong(buffer, songN, startTime, fromSilence = false, offset = 0) {
    const ctx = this.ctx;
    const src = ctx.createBufferSource();
    src.buffer = buffer;
    src.loop = false;            // structured song: play through once, then crossfade to next
    const g = ctx.createGain();
    src.connect(g).connect(this.masterGain);

    const songPlay = buffer.duration * LOOPS_PER_SONG - offset;
    // Fade length. Three bounds, and the tail is the one that matters musically:
    //   * the song's ring-out tail — fade over the decay, never over the ending itself;
    //   * CROSSFADE, the ceiling;
    //   * just under half the play time, so the fade-in and fade-out curves cannot overlap
    //     (Web Audio forbids overlapping automation curves).
    const cf = Math.max(0, Math.min(this.tailSeconds, CROSSFADE, songPlay / 2 - 0.05));
    // The fade OUT is always the crossfade — the next song is coming in over it. The fade IN is
    // only a crossfade when there is something to cross from.
    const fi = fromSilence ? Math.min(START_FADE, cf) : cf;

    if (cf > 0.001) {
      // Equal-power fade in; the curve's final value (1) persists as the "hold"; then fade out. No
      // setValueAtTime between them — placing one at a curve edge is an overlap error.
      g.gain.setValueCurveAtTime(CURVE_IN, startTime, fi);
      g.gain.setValueCurveAtTime(CURVE_OUT, startTime + songPlay - cf, cf);
    } else {
      g.gain.setValueAtTime(1, startTime);
    }

    src.start(startTime, offset);
    src.stop(startTime + songPlay + 0.05);
    this.activeNodes.push(src, g);
    // `activeNodes` exists to tear down what is still PLAYING, so a finished song has to leave it:
    // a spent source still references its AudioBuffer, i.e. a whole song's PCM.
    src.onended = () => {
      try { src.disconnect(); g.disconnect(); } catch (_) {}
      this.activeNodes = this.activeNodes.filter((x) => x !== src && x !== g);
    };

    // What is audible, and where it started — pause() and position() both read this.
    const desc = { n: songN, startTime, offset, duration: buffer.duration * LOOPS_PER_SONG };
    // Claim it NOW when nothing is audible, rather than only when the start timeout fires. The two
    // are tens of milliseconds apart and the audio is genuinely committed for that whole gap, so a
    // progress bar reading `current` in between would otherwise find nothing and draw a zero — which
    // is what every seek and every resume looked like: a flash back to the start of the song. A song
    // scheduled BEHIND one that is already playing does not claim it; that one is still the audible
    // one until its own timeout says otherwise.
    if (!this.current) this.current = desc;

    const mySeq = this.seq;
    const delay = Math.max(0, (startTime - ctx.currentTime) * 1000);
    setTimeout(() => {
      if (mySeq !== this.seq || this.destroyed) return;
      this.displayN = songN;
      this.current = desc;
      if (this.bufferingN === songN) this.bufferingN = -1;
      // Adopt the song this position resolved to, so the editor and the seed box reflect what is
      // audible and a copied link reproduces it.
      this.adoptSong(songN);
      this.emit('vibe', {});
      this.pruneCache();
      this.store.set('n', String(this.songAt(songN).n));
      this.emitSong();
      this.emit('timeline', {});
      this.emit('position', this.position());
      this.emitBuffer();
    }, delay);

    return startTime + songPlay - cf;  // next song overlaps the fade-out
  }

  pump() {
    if (!this.playing || !this.ctx) return;
    // Schedule any rendered songs whose start falls within the horizon. Cap iterations so a
    // pathological state can never spin into a scheduling storm.
    let guard = 0;
    while (this.rendered.has(this.nextN) && this.nextTime < this.ctx.currentTime + SCHEDULE_HORIZON && guard++ < 6) {
      const { buffer } = this.rendered.get(this.nextN);
      let nextStart;
      // The resume offset belongs to the first song of the sequence and to nothing after it.
      const offset = this.firstScheduled ? 0 : this.pendingOffset;
      this.pendingOffset = 0;
      try {
        nextStart = this.scheduleOneSong(buffer, this.nextN, this.nextTime, !this.firstScheduled, offset);
      } catch (e) {
        this.emit('error', { error: e });
        nextStart = this.nextTime + buffer.duration * LOOPS_PER_SONG;  // still advance, don't wedge
      }
      this.nextTime = nextStart;
      this.pruneCache();
      this.firstScheduled = true;
      this.nextN++;
    }
    for (let k = this.nextN; k <= this.nextN + AHEAD_COUNT; k++) this.requestSong(k);
  }

  // HARD restart — for base changes (new seed/tag/genre/vibe edit) that invalidate the whole
  // timeline: aborts in-flight renders, drops the PCM cache AND the frozen-vibe ledger, rebuilds
  // from n. For a navigation that should PRESERVE the timeline (Prev/Next), use seekTo().
  startSequence(offset = 0) {
    this.stopNodes();
    this.pendingOffset = offset;
    this.dirty = false;
    this.renderSeq++;      // the cfg behind every index may have changed; nothing in flight survives
    this.gen.clear();      // paired with abandoning every in-flight render below — see queue.js
    this.rendered.clear();
    this.pool.abandon(this, () => false);
    this.ledger.clear();
    this.bufferingN = -1;
    this.nextN = this.n;
    if (!this.ctx) return;
    this.nextTime = this.ctx.currentTime + 0.18;
    for (let k = this.n; k <= this.n + AHEAD_COUNT; k++) this.requestSong(k);
    this.pump();
    // Nothing is playing and nothing is cached: this player is starving, so it may take a slot off
    // another player's look-ahead if that player is over its share.
    if (!this.rendered.has(this.n)) this.pool.preemptFor(this);
    this.emitBuffer();
    this.emit('timeline', {});
  }

  // SOFT seek — navigate to song nn while PRESERVING the timeline (ledger + PCM cache). Plays a
  // cached song instantly; otherwise stalls in a buffering state while it regenerates from nn's
  // ledger seed. Restarts the audio schedule (a manual jump can't rewind committed Web Audio
  // nodes) but does NOT discard the timeline.
  // `offset` is seconds into song nn to come in at — a resume or a scrub (see seekWithin); every
  // other caller lands on the downbeat.
  seekTo(nn, offset = 0) {
    nn = Math.max(0, nn | 0);
    this.n = nn;
    if (!this.playing || !this.ctx) {
      this.displayN = nn;
      // Paused: this is where the next play comes in. Setting it unconditionally is what stops a
      // Prev/Next taken while paused from inheriting the offset of the song we paused out of.
      this.resumeOffset = offset;
      this.emitSong();
      this.emit('timeline', {});
      this.emit('position', this.position());
      return;
    }
    this.stopNodes();
    this.pendingOffset = offset;
    // Drop queued work — the window around nn is re-requested below. This RELEASES the dropped
    // indices' claims; holding them would bar those songs from ever being queued again (queue.js).
    this.gen.dropQueued();
    // A worker mid-render on a song the seek left behind holds a pool slot for the whole render, so
    // a distant jump can hand the pool to work nobody will hear while the target waits. Only those
    // are terminated: a Prev/Next lands inside the cache window, where what is in flight is still a
    // song the timeline wants — and a terminate costs a runtime reboot.
    this.pool.abandon(this, (k) => Math.abs(k - nn) <= PCM_RADIUS);
    this.nextN = nn;
    this.bufferingN = this.rendered.has(nn) ? -1 : nn;
    this.nextTime = this.ctx.currentTime + 0.06;
    this.pruneCache(nn);   // prune around the target (displayN still lags until nn starts)
    for (let k = nn; k <= nn + AHEAD_COUNT; k++) this.requestSong(k);
    this.pump();
    if (this.bufferingN >= 0) this.pool.preemptFor(this);   // stalled — see startSequence
    this.emitBuffer();
    this.emit('timeline', {});
  }

  // Playback has stalled waiting on the song you skipped to (vs. silent background look-ahead
  // fill). Mirrors SkafinityPlayer.IsBuffering.
  emitBuffer() {
    const on = this.playing && this.bufferingN >= 0 && !this.rendered.has(this.bufferingN);
    this.emit('buffer', { generating: on, n: on ? this.bufferingN : -1 });
  }

  // ── The playlist window ──────────────────────────────────────────────────────
  timeline() {
    const out = [];
    const from = Math.max(0, this.displayN - PCM_RADIUS);
    for (let k = from; k <= this.displayN + AHEAD_COUNT; k++) {
      const cached = this.rendered.has(k);
      const s = this.mod ? this.songAt(k) : { tag: this.tag, n: k };
      out.push({
        // `n` is the song's index in ITS station — which under shuffle is 0 for every row, so the
        // row also carries the station it belongs to. `position` is the timeline slot.
        n: s.n,
        position: k,
        tag: s.tag,
        now: k === this.displayN,
        cached,
        generating: this.gen.has(k) || (this.bufferingN === k && !cached),
        past: k < this.displayN,
        genre: this.mod ? this.mod.genreName(this.genreForN(k)) : '',
      });
    }
    return out;
  }

  // ── Export ───────────────────────────────────────────────────────────────────
  // Export the song's frozen vibe (its ledger cfg under shuffle), so a downloaded #k matches what
  // the timeline plays — not just whatever the live editor currently shows.
  //
  // The song is COMPRESSED in the browser (web/encode.js): raw stereo PCM is ~13 MB a song, which
  // is not a file to hand somebody over a link. A browser with no usable AudioEncoder still gets
  // the engine's WAV rather than nothing, which is why `songToWav` is still here.
  async exportSong(songN) {
    const s = this.songAt(songN);
    const safeTag = (s.tag ? lower(s.tag) : 'unknown').replace(/[^a-z0-9_-]/g, '') || 'unknown';
    const name = `${safeTag}_${s.n}`;
    const fmt = await pickAudioFormat();
    if (!fmt) {
      const bytes = this.mod.songToWav(this.seedFor(songN), this.cfgForN(songN).slice());
      return { blob: new Blob([bytes], { type: 'audio/wav' }), filename: `${name}.wav` };
    }
    // The fully-resolved seed, not the station's: a saved file names the song it actually is.
    const r = this.resolve(songN);
    const title = this.mod.formatSeed(s.tag, s.n, r.genre, r.vibe);
    const bytes = await encodeSong(this.pcmFor(songN), fmt, { title });
    return { blob: new Blob([bytes], { type: fmt.mime }), filename: `${name}.${fmt.ext}` };
  }

  // The samples to export. A song still in the PCM cache is already rendered — asking the engine
  // for it again would freeze the tab for seconds to reproduce bytes we are holding.
  pcmFor(songN) {
    const cached = this.rendered.get(songN);
    if (cached) {
      const b = cached.buffer;
      return { left: b.getChannelData(0), right: b.getChannelData(1), sampleRate: b.sampleRate };
    }
    const m = this.mod.generateSong(this.seedFor(songN), this.cfgForN(songN).slice());
    return { left: m.left, right: m.right, sampleRate: m.sampleRate };
  }

  // ── Teardown ─────────────────────────────────────────────────────────────────
  // A custom element can be removed from the document at any time; leaving a pool slot rendering
  // for a dead player, or an interval firing, is how an embed becomes a leak in someone else's page.
  destroy() {
    this.destroyed = true;
    this.playing = false;
    clearInterval(this.tick);
    clearTimeout(this.restartTimer);
    this.stopNodes();
    this.pool.abandon(this, () => false);
    this.gen.clear();
    this.rendered.clear();
    this.ledger.clear();
    this.pool.unregister(this);
    if (this.masterGain) { try { this.masterGain.disconnect(); } catch (_) {} }
    // The shared AudioContext is deliberately NOT closed: another widget may be using it, and a
    // closed context cannot be reopened.
  }
}

export default SkafinityPlayer;
