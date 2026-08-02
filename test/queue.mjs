// Does the generation queue ever strand a song?
//
// The toy going permanently silent after a few skips was one line: seekTo dropped the pending
// queue without releasing the claims on it, so every index that was queued-but-not-dispatched at
// the moment of a seek stayed "already requested" forever and could never be queued again. The
// first stranded index the timeline walked onto stopped playback for the rest of the session.
//
// The scheduler around it needs AudioContext + Worker + DOM and cannot run here, but the state
// machine that went wrong needs none of them — which is why it lives in web/queue.js. This file
// asserts its invariant directly, and then drives a model of app.js's use of it (pump / worker
// lands / seek) hard enough that a stranded index shows up as a stalled timeline, the same way it
// does in a browser.
//
//   node test/queue.mjs        (part of `make test`; needs no wasm bundle)
import { GenQueue } from '../web/queue.js';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}

// ── The invariant: claimed === queued ∪ in-flight ──
{
  const q = new GenQueue();
  q.want(1); q.want(2); q.want(3);
  const held = q.take();                       // 1 is in flight now
  q.dropQueued();                              // 2 and 3 are dropped
  check('dropQueued keeps the in-flight claim', q.has(held) && q.inFlight().length === 1);
  check('dropQueued releases the queued claims', !q.has(2) && !q.has(3));
  check('a dropped index can be wanted again', q.want(2) === true);
  check('an in-flight index cannot be double-claimed', q.want(held) === false);
  q.release(held);
  check('release frees the claim', !q.has(held));
  q.clear();
  check('clear empties both', q.inFlight().length === 0 && q.take() === undefined && !q.has(2));
}

// ── A model of app.js's scheduler: look-ahead top-up, a worker pool, and seeks ──
// Deliberately a model rather than an import: app.js is DOM/audio-bound. What it mirrors is the
// only thing that decides whether a song is ever produced — request/dispatch/land/seek over the
// queue — so a claim leak here is a claim leak there.
const AHEAD = 4, POOL = 3, RADIUS = 5;
class Sim {
  constructor() { this.q = new GenQueue(); this.rendered = new Set(); this.busy = []; this.nextN = 0; }
  invariant() {
    const inFlight = this.q.inFlight().sort((a, b) => a - b);
    const busy = [...this.busy].sort((a, b) => a - b);
    return inFlight.length === busy.length && inFlight.every((v, i) => v === busy[i]);
  }
  request(nn) { if (!this.rendered.has(nn) && this.q.want(nn)) this.dispatch(); }
  dispatch() {
    while (this.busy.length < POOL) {
      const nn = this.q.take();
      if (nn === undefined) break;
      this.busy.push(nn);
    }
  }
  pump() {                       // schedule what's ready, then top the look-ahead back up
    while (this.rendered.has(this.nextN)) this.nextN++;
    for (let k = this.nextN; k <= this.nextN + AHEAD; k++) this.request(k);
  }
  land() {                       // one worker finishes (any of them — order is not ours to pick)
    if (!this.busy.length) return false;
    const nn = this.busy.shift();
    this.q.release(nn);
    this.rendered.add(nn);
    this.dispatch();
    this.pump();
    return true;
  }
  seek(nn) {                     // web/app.js seekTo(): drop the queue, abandon far renders, re-request
    this.q.dropQueued();
    for (const b of [...this.busy]) if (Math.abs(b - nn) > RADIUS) {
      this.q.release(b);
      this.busy.splice(this.busy.indexOf(b), 1);
    }
    this.nextN = nn;
    for (let k = nn; k <= nn + AHEAD; k++) this.request(k);
    this.dispatch();
  }
}

// A deterministic pseudo-random walk of skips — Prev/Next mostly, the odd jump — with renders
// landing in between, which is exactly the interleaving the bug needed.
let s = 12345;
const rnd = () => (s = (s * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff;
const sim = new Sim();
sim.pump();
let broke = -1;
for (let step = 0; step < 4000; step++) {
  const r = rnd();
  if (r < 0.55) sim.land();
  else if (r < 0.9) sim.seek(Math.max(0, sim.nextN + (rnd() < 0.5 ? -1 : 1)));
  else sim.seek(Math.floor(rnd() * 400));
  sim.pump();                  // app.js pumps on a 250 ms interval, not only when a render lands
  if (!sim.invariant() && broke < 0) broke = step;
}
check('claims and workers never disagree over a long session', broke < 0, broke < 0 ? '4000 steps' : `broke at step ${broke}`);

// The symptom itself: from wherever the walk left off, nothing but renders landing. If any index
// in the window ahead is stranded, the timeline stops advancing while workers sit idle — which is
// the silent toy, reproduced without a browser.
const from = sim.nextN;
for (let i = 0; i < 200; i++) { sim.pump(); sim.land(); }
check('the timeline still advances after all that skipping', sim.nextN > from + 20,
  `#${from} → #${sim.nextN}`);
check('nothing is claimed that no worker is rendering',
  sim.q.inFlight().length === sim.busy.length, `${sim.q.inFlight().length} claimed / ${sim.busy.length} busy`);

// The one-line regression, stated on its own: a seek that drops the queue must not bar the songs
// it dropped. Pre-fix, the second requestSong() was a no-op forever and #0 never rendered.
{
  const g = new GenQueue();
  g.want(0); g.want(1);          // queued, none dispatched (every worker busy elsewhere)
  g.dropQueued();                // seek
  const requeued = [0, 1].filter((k) => g.want(k));
  check('a seek does not bar the songs it dropped', requeued.length === 2, `re-queued ${requeued}`);
}

console.log(failures ? `\n${failures} FAILURE(S)` : '\nall good');
process.exit(failures ? 1 : 0);
