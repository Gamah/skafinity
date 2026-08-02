// skafinity — the generation queue's bookkeeping, split out of app.js so it can be tested.
//
// The scheduler around it is browser-only (AudioContext, Worker, DOM), but the part that decides
// whether a song ever gets rendered is none of those: it is two collections that must agree.
//
//   claimed — every index someone is waiting on: queued OR already handed to a worker. This is
//             what stops the same song being rendered twice.
//   queue   — the subset not yet dispatched.
//
// The invariant is one line, and it is what this file exists to keep:
//
//     claimed === queue ∪ in-flight
//
// so every way of dropping work has to release the claims it drops. A claim is only ever released
// when a render LANDS, so a claim with neither a queue entry nor a worker behind it is stranded
// forever — and `want()` will then refuse to queue that index again for the rest of the session.
// That is fatal rather than untidy: the timeline is walked in order, so the first stranded index
// it reaches stops playback permanently.
export class GenQueue {
  constructor() {
    this.claimed = new Set();
    this.queue = [];
  }

  // Is this index already spoken for (queued, or rendering right now)?
  has(nn) { return this.claimed.has(nn); }

  // Claim nn and queue it. Returns false if it was already claimed — i.e. nothing to dispatch.
  want(nn) {
    if (this.claimed.has(nn)) return false;
    this.claimed.add(nn);
    this.queue.push(nn);
    return true;
  }

  // Pop the next index to hand to a worker, or undefined. It stays CLAIMED: it is in flight now,
  // not free.
  take() { return this.queue.length ? this.queue.shift() : undefined; }

  // A render landed, failed, or was cancelled: the claim is over.
  release(nn) { this.claimed.delete(nn); }

  // Drop everything still queued, releasing each claim with it. In-flight renders are untouched —
  // they still hold their claims and release them when they land.
  dropQueued() {
    for (const nn of this.queue) this.claimed.delete(nn);
    this.queue.length = 0;
  }

  // Hard reset: nothing is queued and nothing in flight is waited on any more. Callers that use
  // this must abandon the in-flight renders too (terminate the workers), or a late arrival lands
  // against a claim that no longer exists.
  clear() {
    this.queue.length = 0;
    this.claimed.clear();
  }

  // Claimed minus queued — the indices a worker is holding. Only the tests and the invariant
  // check need this; the page tracks its own busy slots.
  inFlight() {
    const q = new Set(this.queue);
    return [...this.claimed].filter((nn) => !q.has(nn));
  }
}
