// skafinity — <skafinity-player>, the embeddable widget.
//
// The UI half. Everything that makes noise is web/player.js (headless, instanceable); everything
// that decides what the widget LOOKS like is here, and the two rules it lives by are:
//
//   * a shadow root, so the host page's CSS cannot reach in and wreck the layout — and so the
//     widget's own selectors cannot leak out and restyle the page that took it in;
//   * the palette is SNIFFED from that page rather than shipped. A widget that arrives in its own
//     colours is a widget that looks pasted on; see docs/embedding.md for how far the sniff goes
//     and where it gives up.
//
// It writes nothing global: no location.hash (the page decides whether the URL is the song — see
// web/app.js for how the toy does it), and every localStorage key is namespaced per instance.
import { SkafinityPlayer } from './player.js';
import { derivePalette, chooseMode, pickAccent, parseColor, NEUTRAL_ACCENT } from './palette.js';

// One header per grid column. The wire carries columns 1..4 (column 0, VOLUME, is a local mix
// preference and never travels), and the grid is rectangular — a voice with nothing in its last
// column simply leaves that cell empty.
const COL_HEADERS = ['VOLUME', 'TONE', 'CHARACTER', 'EXTRA', 'MORE'];
// Shown where a length would be if there were one — nothing is rendered yet, so there is no
// duration to state. An honest dash beats 0:00, which reads as a song of no length.
const NO_TIME = '–:––';
const fmtTime = (s) => {
  const t = Math.max(0, Math.round(s));
  return `${Math.floor(t / 60)}:${String(t % 60).padStart(2, '0')}`;
};
// Every section the widget can show. `controls` names the ones it does.
const SECTIONS = ['transport', 'seed', 'vibe', 'playlist'];
// What the message line says when it has nothing else to say and the engine is not up yet. Nothing
// is fetched until the first play, so this is the one moment the widget owes an explanation.
const IDLE_MSG = 'press play — the ~7.5 MB engine downloads on first play, not on page view';

// The shadow stylesheet. Every colour is `var(--ska-x, var(--_ska-x))`: the inner name carries what
// the derivation worked out, the outer is the public override, and the cascade settles which wins —
// so a host overriding one token does not have to restate the other thirteen.
const CSS = `
:host {
  display: block;
  --_ska-font: ui-monospace, 'SF Mono', Menlo, Consolas, monospace;
  --_ska-radius: 2px;
  color: var(--ska-text, var(--_ska-text));
  font-family: var(--ska-font, var(--_ska-font));
  font-size: 13px;
  line-height: 1.4;
  contain: layout style;
}
:host([hidden]) { display: none; }
* { box-sizing: border-box; margin: 0; padding: 0; }

.board {
  background: var(--ska-bg, var(--_ska-bg));
  border: 1px solid var(--ska-edge, var(--_ska-edge));
  border-radius: var(--ska-radius, var(--_ska-radius));
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

button {
  background: var(--ska-cell, var(--_ska-cell));
  border: 1px solid var(--ska-edge, var(--_ska-edge));
  color: var(--ska-text, var(--_ska-text));
  border-radius: var(--ska-radius, var(--_ska-radius));
  padding: 6px 12px;
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  transition: border-color 0.12s, background 0.12s, color 0.12s;
}
button:hover { border-color: var(--ska-hover, var(--_ska-hover)); }
button:focus-visible { outline: 2px solid var(--ska-accent, var(--_ska-accent)); outline-offset: 1px; }
button[disabled] { opacity: 0.45; cursor: default; }
button.primary {
  background: var(--ska-accent-bg, var(--_ska-accent-bg));
  border-color: var(--ska-accent, var(--_ska-accent));
  color: var(--ska-accent, var(--_ska-accent));
  font-weight: 600;
}
button.primary:hover { border-color: var(--ska-pick, var(--_ska-pick)); }
button.big { font-size: 16px; width: 44px; height: 34px; line-height: 1; padding: 0; }
button.on { background: var(--ska-fill, var(--_ska-fill)); border-color: var(--ska-pick, var(--_ska-pick)); }

input, select {
  background: var(--ska-surface, var(--_ska-surface));
  border: 1px solid var(--ska-edge, var(--_ska-edge));
  color: var(--ska-text, var(--_ska-text));
  border-radius: var(--ska-radius, var(--_ska-radius));
  padding: 6px 8px;
  font: inherit;
  font-size: 12px;
  min-width: 0;
}
input:focus, select:focus { outline: none; border-color: var(--ska-accent, var(--_ska-accent)); }
input::placeholder { color: var(--ska-text-dim, var(--_ska-text-dim)); }
input[type=range] { accent-color: var(--ska-accent, var(--_ska-accent)); padding: 0; border: 0; background: none; }

.row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.dim { color: var(--ska-text-dim, var(--_ska-text-dim)); font-size: 12px; }
.grow { flex: 1; }
.right { margin-left: auto; }

/* ── Transport ── */
.transport { display: flex; flex-direction: column; gap: 8px; }
.now b { font-weight: 600; color: var(--ska-text, var(--_ska-text)); }
.vol { display: flex; align-items: center; gap: 6px; }
.vol input { width: 90px; }
/* The seek bar is a real range input, so dragging, arrow keys, Home/End and a screen reader all
   work without any of it being written here. */
.seek { display: flex; align-items: center; gap: 8px; }
.seek input { flex: 1; }
.seek input[disabled] { opacity: 0.4; }
.time {
  font-size: 11px; color: var(--ska-text-dim, var(--_ska-text-dim));
  font-variant-numeric: tabular-nums; min-width: 34px;   /* a steady width, so digits don't jitter */
}
.time.total { text-align: right; }
.bufstate { color: var(--ska-accent, var(--_ska-accent)); font-size: 12px; display: none; }
.bufstate.show { display: inline; animation: pulse 1s ease-in-out infinite; }
@keyframes pulse { 0%,100% { opacity: 0.45; } 50% { opacity: 1; } }

/* ── Boot progress ── */
/* Nothing is downloaded until the first play, so this is the one moment the widget has to explain
   itself: ~7.5 MB of runtime with no visible reason for the wait. */
.boot { display: none; flex-direction: column; gap: 6px; }
.boot.show { display: flex; }
.bar { height: 6px; background: var(--ska-surface, var(--_ska-surface)); border-radius: 3px; overflow: hidden; }
.bar > i { display: block; height: 100%; width: 0%; background: var(--ska-accent, var(--_ska-accent)); transition: width 0.2s linear; }
/* No Content-Length yet — an honest indeterminate sweep rather than a bar parked at zero. */
.bar.indet > i { width: 35%; animation: slide 1.1s ease-in-out infinite; }
@keyframes slide { 0% { transform: translateX(-110%); } 100% { transform: translateX(320%); } }

/* ── Panels ── */
.panel {
  background: var(--ska-cell, var(--_ska-cell));
  border: 1px solid var(--ska-rule, var(--_ska-rule));
  border-radius: var(--ska-radius, var(--_ska-radius));
  padding: 12px;
}
h2 {
  font-size: 10px; font-weight: 600; letter-spacing: 1px; text-transform: uppercase;
  color: var(--ska-text-dim, var(--_ska-text-dim));
  margin-bottom: 10px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
}
h2 select, h2 button { text-transform: none; letter-spacing: 0; font-size: 11px; padding: 3px 8px; }

/* ── Playlist ── */
.playlist { max-height: 260px; overflow-y: auto; display: flex; flex-direction: column; gap: 2px; }
.plrow {
  display: flex; align-items: center; gap: 8px; padding: 5px 6px;
  border-radius: var(--ska-radius, var(--_ska-radius)); border-left: 2px solid transparent;
}
.plrow.cached { border-left-color: var(--ska-fill-soft, var(--_ska-fill-soft)); }
.plrow.gen { border-left-color: var(--ska-busy, var(--_ska-busy)); }
.plrow.now { background: var(--ska-accent-bg, var(--_ska-accent-bg)); outline: 1px solid var(--ska-fill, var(--_ska-fill)); }
.pllabel { cursor: pointer; white-space: nowrap; font-size: 12px; min-width: 40px; background: none; border: 0; color: inherit; padding: 0; text-align: left; }
.plgenre { flex: 1; color: var(--ska-text-dim, var(--_ska-text-dim)); font-size: 11px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.plstatus { color: var(--ska-text-dim, var(--_ska-text-dim)); font-size: 11px; min-width: 44px; text-align: right; }
.plbar { display: inline-block; width: 42px; height: 5px; background: var(--ska-surface, var(--_ska-surface)); border-radius: 2px; overflow: hidden; }
.plbar > i { display: block; width: 40%; height: 100%; background: var(--ska-accent, var(--_ska-accent)); border-radius: 2px; animation: slide 1.1s ease-in-out infinite; }
.pldl { background: transparent; padding: 2px 7px; font-size: 11px; color: var(--ska-text-dim, var(--_ska-text-dim)); }
.jump { margin-top: 10px; }
.jump input[type=number] { width: 62px; }

/* ── Vibe editor ── */
.matrix { display: flex; flex-direction: column; gap: 8px; }
.mrow { display: grid; grid-template-columns: 54px repeat(5, 1fr); align-items: start; gap: 8px; }
.mvoice { font-weight: 700; font-size: 11px; letter-spacing: 1px; padding-top: 2px; }
.mhead { align-items: center; }
.mhlabel { font-size: 9px; letter-spacing: 1.5px; color: var(--ska-text-dim, var(--_ska-text-dim)); }
.mcell { min-width: 0; }
.glabel { font-size: 10px; letter-spacing: 2px; color: var(--ska-text-dim, var(--_ska-text-dim)); border-top: 1px solid var(--ska-rule, var(--_ska-rule)); padding-top: 10px; margin-top: 10px; }
.global-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px 14px; margin-top: 8px; }
.knob { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
.knob-head { display: flex; justify-content: space-between; align-items: baseline; gap: 6px; }
.knob-name { font-size: 10px; color: var(--ska-text-dim, var(--_ska-text-dim)); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.knob-val { font-size: 10px; color: var(--ska-accent, var(--_ska-accent)); white-space: nowrap; }
.knob-input { width: 100%; }

.msg { font-size: 11px; color: var(--ska-text-dim, var(--_ska-text-dim)); }
.msg.err { color: var(--ska-accent, var(--_ska-accent)); }

@media (max-width: 520px) {
  .mrow { grid-template-columns: 40px repeat(5, 1fr); gap: 5px; }
  .global-grid { grid-template-columns: repeat(2, 1fr); }
}
`;

// ── Host-style sniffing ────────────────────────────────────────────────────────
// A throwaway <button> and <a>, inserted where the widget actually sits and measured once. A real
// widget's computed style carries more signal than <body>'s: a page that styles its buttons has
// told us its radius, its font and its accent, none of which body knows.
//
// It is best-effort and it is allowed to be wrong — every value it produces is overridable by a
// `--ska-*` custom property or the `accent`/`theme` attributes, which is the escape hatch a host
// reaches for when the guess misses. See docs/embedding.md.
function probeHost(el) {
  const parent = el.parentElement || el.getRootNode().body || document.body;
  const holder = document.createElement('div');
  // Off-screen but LAID OUT — display:none computes nothing, and visibility:hidden still inherits
  // and still resolves the page's own button styling, which is the whole point of the probe.
  holder.setAttribute('style', 'position:absolute!important;left:-9999px!important;top:0!important;' +
    'width:0!important;height:0!important;overflow:hidden!important;pointer-events:none!important;' +
    'contain:strict!important;');
  holder.setAttribute('aria-hidden', 'true');
  const btn = document.createElement('button');
  btn.textContent = 'skafinity';
  const link = document.createElement('a');
  link.href = '#';
  link.textContent = 'skafinity';
  holder.append(btn, link);
  parent.append(holder);

  let out = {};
  try {
    const cb = getComputedStyle(btn), ca = getComputedStyle(link), ch = getComputedStyle(el);
    out = {
      font: ch.fontFamily || cb.fontFamily || '',
      radius: cb.borderRadius && cb.borderRadius !== '0px' ? cb.borderRadius : '',
      buttonBg: cb.backgroundColor || '',
      buttonBorder: cb.borderTopColor || '',
      linkColor: ca.color || '',
      // A custom property inherits, so resolving it against the element itself already answers
      // "did any ancestor define this?" — no ancestor walk needed.
      varOf: (name) => ch.getPropertyValue(name),
      pageBg: findBackground(el),
    };
  } finally {
    holder.remove();
  }
  return out;
}

// First non-transparent background-color at or above the element — what the widget will actually
// be sitting on. Stops at the document element; a page that paints nothing anywhere returns null
// and the OS preference decides instead.
function findBackground(el) {
  let node = el;
  while (node && node.nodeType === 1) {
    const c = parseColor(getComputedStyle(node).backgroundColor);
    if (c && c.a > 0.5) return c;
    // Climb out of a shadow root too — an embed inside someone else's component still sits on
    // whatever that component's host is painted over.
    const root = node.parentElement ? null : node.getRootNode();
    node = node.parentElement ||
      (typeof ShadowRoot !== 'undefined' && root instanceof ShadowRoot ? root.host : null);
  }
  const c = parseColor(getComputedStyle(document.documentElement).backgroundColor);
  return c && c.a > 0.5 ? c : null;
}

const prefersDark = () =>
  typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;

export class SkafinityPlayerElement extends HTMLElement {
  static get observedAttributes() { return ['seed', 'theme', 'accent', 'controls', 'volume', 'share-base']; }

  /** Transport options every element on the page is constructed with. A host that already runs Web
   *  Audio sets `audioContext` here (before the first widget upgrades) so the widgets join its
   *  graph rather than opening another; `engine` hands in an already-booted runtime. Per-element
   *  attributes win over anything set here. */
  static playerDefaults = {};

  constructor() {
    super();
    this.root = this.attachShadow({ mode: 'open' });
    this.player = null;
    this.els = {};
    this.booted = false;
    this._mq = null;
    this._clock = null;         // rAF handle (or interval id) driving the seek bar
    this._clockRaf = false;
    this._scrubbing = false;    // a drag owns the thumb until it is released
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────
  connectedCallback() {
    if (!this.booted) { this.build(); this.booted = true; }
    this.refreshTheme();
    // The OS flipping to dark is only ever the LAST resort for mode, but when it is what we used,
    // it has to keep working.
    if (typeof matchMedia === 'function' && !this._mq) {
      this._mq = matchMedia('(prefers-color-scheme: dark)');
      this._onScheme = () => this.refreshTheme();
      this._mq.addEventListener('change', this._onScheme);
    }
    if (this.hasAttribute('preload') && this.getAttribute('preload') !== 'none') this.player.load().catch(() => {});
    if (this.hasAttribute('autoplay')) this.play();
  }

  disconnectedCallback() {
    if (this._mq) { this._mq.removeEventListener('change', this._onScheme); this._mq = null; }
    this.stopClock();
    // Removal from the document is the only teardown signal a custom element gets, and a widget
    // that keeps a worker rendering (and an interval firing) after it is gone is a leak in someone
    // else's page.
    if (this.player) { this.player.destroy(); this.player = null; this.booted = false; }
  }

  attributeChangedCallback(name, oldV, newV) {
    if (oldV === newV) return;
    if (name === 'theme' || name === 'accent') { this.refreshTheme(); return; }
    if (name === 'share-base') { this.syncShare(); return; }
    if (!this.player) return;
    if (name === 'seed' && newV && newV !== this.player.seed) {
      // A seed set from outside gets the same refusal as one typed in: it says why, inline, and
      // leaves the widget playing whatever it was.
      this.showMessage(this.player.applySeed(newV));
      this.syncSeedInput();
    }
    if (name === 'controls') this.applyControls();
    if (name === 'volume') { const v = parseFloat(newV); if (Number.isFinite(v)) this.setVolume(v); }
  }

  // ── Public surface (properties mirror the attributes) ──────────────────────
  get seed() { return this.player ? this.player.seed : this.getAttribute('seed') || ''; }
  set seed(v) {
    if (!this.player) { this.setAttribute('seed', v); return; }
    this.showMessage(this.player.applySeed(v));
    this.syncSeedInput();
  }
  get playing() { return !!(this.player && this.player.playing); }
  play() { return this.player.play().catch((e) => this.showError(e)); }
  pause() { this.player.pause(); }
  /** Where the audible song is: `{n, time, duration, ratio, playing}`; `duration` 0 = not rendered. */
  get position() { return this.player ? this.player.position() : { n: 0, time: 0, duration: 0, ratio: 0, playing: false }; }
  /** Scrub to `seconds` into the current song (paused: where the next play comes in). */
  seek(seconds) { this.player.seekWithin(seconds); }
  /** Boot the engine without playing (what `preload` does). */
  load() { return this.player.load(); }

  // ── Theming ────────────────────────────────────────────────────────────────
  /** Re-run the host-style probe and re-derive the palette. Attribute changes call this; a host
   *  whose own theme switched at runtime can call it too. */
  refreshTheme() {
    if (!this.isConnected) return;
    const host = probeHost(this);
    const attrTheme = (this.getAttribute('theme') || 'auto').toLowerCase();

    // Accent, in priority order: the attribute (an explicit instruction), then the host's own
    // custom properties, then the page's link colour, then neutral gray.
    const attrAccent = parseColor(this.getAttribute('accent'));
    const found = attrAccent ? { color: attrAccent, source: 'attribute' } : pickAccent(host.varOf);
    const link = found ? null : parseColor(host.linkColor);
    const accent = (found && found.color) || link || parseColor(NEUTRAL_ACCENT);
    this._accentSource = found ? found.source : link ? 'link colour' : 'neutral';

    const mode = attrTheme === 'light' || attrTheme === 'dark'
      ? attrTheme
      : chooseMode(host.pageBg, prefersDark());
    this._mode = mode;

    const tokens = derivePalette(accent, mode);
    if (host.font) tokens['--_ska-font'] = host.font;
    if (host.radius) tokens['--_ska-radius'] = host.radius;
    for (const [k, v] of Object.entries(tokens)) this.style.setProperty(k, v);
    // A colour-scheme declaration is what makes the browser's own widgets — the range thumb, a
    // scrollbar, a focus ring — agree with the palette we just derived.
    this.style.setProperty('color-scheme', mode);
    this.dispatchEvent(new CustomEvent('theme', { detail: { mode, accent, source: this._accentSource } }));
  }

  /** What the sniff decided, for a host that wants to check it (and for the demo pages). */
  get themeInfo() { return { mode: this._mode, accentSource: this._accentSource }; }

  // ── Build ──────────────────────────────────────────────────────────────────
  build() {
    // A custom element can be removed and re-inserted; disconnect drops the transport, so connect
    // rebuilds — and the shadow root has to be emptied first or the second tree lands under the first.
    this.root.innerHTML = '';
    this.els = {};
    const style = document.createElement('style');
    style.textContent = CSS;
    const board = document.createElement('div');
    board.className = 'board';
    board.setAttribute('part', 'board');
    this.root.append(style, board);
    this.els.board = board;

    this.player = new SkafinityPlayer({
      ...SkafinityPlayerElement.playerDefaults,
      seed: this.getAttribute('seed') || '',
      storage: this.getAttribute('storage') === 'none' ? null : undefined,
      storageKey: this.getAttribute('storage-key') || this.id || 'default',
      volume: this.hasAttribute('volume') ? parseFloat(this.getAttribute('volume')) : undefined,
      shuffle: this.hasAttribute('shuffle') ? this.getAttribute('shuffle') !== 'off' : undefined,
    });

    this.buildTransport(board);
    this.buildBoot(board);
    this.buildSeedBar(board);
    this.buildVibe(board);
    this.buildPlaylist(board);
    const msg = document.createElement('p');
    msg.className = 'msg';
    msg.setAttribute('part', 'message');
    msg.textContent = IDLE_MSG;
    board.append(msg);
    this.els.msg = msg;

    this.applyControls();
    this.wirePlayer();
  }

  section(parent, name, title) {
    const sec = document.createElement('section');
    sec.className = 'panel';
    sec.setAttribute('part', `panel ${name}`);
    sec.dataset.section = name;
    if (title) {
      const h = document.createElement('h2');
      h.append(document.createTextNode(title));
      sec.append(h);
      sec._head = h;
    }
    parent.append(sec);
    return sec;
  }

  buildTransport(board) {
    // Two rows under one section, because `controls="transport"` means the whole transport: the
    // buttons and the bar that says where in the song they are acting.
    const wrap = document.createElement('div');
    wrap.className = 'transport';
    wrap.dataset.section = 'transport';
    wrap.setAttribute('part', 'transport');
    const bar = document.createElement('div');
    bar.className = 'row';

    const mk = (label, title, cls, fn) => {
      const b = document.createElement('button');
      b.textContent = label;
      b.title = title;
      if (cls) b.className = cls;
      b.setAttribute('part', `button ${cls === 'primary big' ? 'play-button' : 'transport-button'}`);
      b.onclick = fn;
      return b;
    };
    const prev = mk('⏮', 'Previous song', '', () => this.player.prev());
    const playBtn = mk('▶', 'Play / Pause', 'primary big', () => (this.player.playing ? this.pause() : this.play()));
    const next = mk('⏭', 'Next song', '', () => this.player.next());

    const now = document.createElement('span');
    now.className = 'dim now';
    now.setAttribute('part', 'now-playing');
    const nowN = document.createElement('b');
    nowN.textContent = '#0';
    now.append('now playing ', nowN);

    const buf = document.createElement('span');
    buf.className = 'bufstate';
    buf.setAttribute('part', 'buffer-state');

    const vol = document.createElement('label');
    vol.className = 'vol dim right';
    vol.setAttribute('part', 'volume');
    const volIn = document.createElement('input');
    volIn.type = 'range'; volIn.min = '0'; volIn.max = '1.5'; volIn.step = '0.01';
    volIn.value = String(this.player.volume);
    volIn.setAttribute('part', 'slider volume-slider');
    volIn.oninput = () => this.setVolume(parseFloat(volIn.value));
    vol.append('vol ', volIn);

    bar.append(prev, playBtn, next, now, buf, vol);

    // ── The seek bar ──
    // The whole song's PCM is already in memory, so a scrub is a re-schedule of a buffer we hold,
    // not a fetch — which is why this is a plain slider and not a "loading" affordance.
    const seek = document.createElement('div');
    seek.className = 'seek';
    seek.setAttribute('part', 'seek');
    const at = document.createElement('span');
    at.className = 'time';
    at.setAttribute('part', 'time-elapsed');
    at.textContent = '0:00';
    const total = document.createElement('span');
    total.className = 'time total';
    total.setAttribute('part', 'time-total');
    total.textContent = NO_TIME;
    const seekIn = document.createElement('input');
    // Per-mille of the song rather than seconds: the song's length is not known until it is
    // rendered, and a range whose max changes under a drag jumps the thumb.
    seekIn.type = 'range'; seekIn.min = '0'; seekIn.max = '1000'; seekIn.step = '1'; seekIn.value = '0';
    seekIn.disabled = true;
    seekIn.title = 'Seek within this song';
    seekIn.setAttribute('aria-label', 'Seek within this song');
    seekIn.setAttribute('part', 'slider seek-slider');
    // `input` fires all through a drag, `change` on release (and on each arrow key). Seeking on
    // `input` would restart the audio schedule at every pixel; the label follows the thumb instead
    // and the transport is told once, when the thumb is let go.
    seekIn.oninput = () => { this._scrubbing = true; this.syncPosition(); };
    seekIn.onchange = () => {
      this._scrubbing = false;
      const { duration } = this.player.position();
      if (duration > 0) this.player.seekWithin((parseInt(seekIn.value, 10) / 1000) * duration);
      this.syncPosition();
    };
    seek.append(at, seekIn, total);

    wrap.append(bar, seek);
    board.append(wrap);
    Object.assign(this.els, { transport: wrap, playBtn, now: nowN, buf, volIn, seekIn, timeAt: at, timeTotal: total });
  }

  buildBoot(board) {
    const wrap = document.createElement('div');
    wrap.className = 'boot';
    wrap.setAttribute('part', 'progress');
    const label = document.createElement('span');
    label.className = 'msg';
    const bar = document.createElement('div');
    bar.className = 'bar indet';
    bar.setAttribute('part', 'progress-bar');
    const fill = document.createElement('i');
    bar.append(fill);
    wrap.append(label, bar);
    board.append(wrap);
    Object.assign(this.els, { boot: wrap, bootLabel: label, bootBar: bar, bootFill: fill });
  }

  buildSeedBar(board) {
    const bar = document.createElement('div');
    bar.className = 'row';
    bar.dataset.section = 'seed';
    bar.setAttribute('part', 'seed-bar');
    const input = document.createElement('input');
    input.type = 'text';
    input.spellcheck = false;
    input.className = 'grow';
    input.placeholder = 'tag:n[:genre][:vibe]';
    input.setAttribute('part', 'seed-input');
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') this.playSeed(input.value); });
    const go = document.createElement('button');
    // Typing a seed and being handed a stopped transport is a dead end — the button says play
    // because that is what it does.
    go.textContent = 'play';
    go.className = 'primary';
    go.setAttribute('part', 'button seed-go');
    go.onclick = () => this.playSeed(input.value);

    // TWO copy buttons, because there are two things somebody might mean by "share this".
    // The default hands over THIS SONG, fully resolved — the recipient hears what is playing here.
    // The second hands over the string as it stands, so a station that is still rolling keeps
    // rolling for them too.
    const copy = this.copyButton('seed-copy', () => this.shareText(false));
    const copyStation = this.copyButton('seed-copy-station', () => this.shareText(true));
    copyStation.title = 'Copy the seed as it stands — whatever it leaves rolling keeps rolling';

    bar.append(input, go, copy, copyStation);
    board.append(bar);
    Object.assign(this.els, { seedBar: bar, seedInput: input, copyBtn: copy, copyStationBtn: copyStation });
    this.syncSeedInput();
    this.syncShare();
  }

  /** A button that copies whatever `text()` returns and says so for a moment. */
  copyButton(part, text) {
    const b = document.createElement('button');
    b.setAttribute('part', `button ${part}`);
    b.onclick = async () => {
      try {
        await navigator.clipboard.writeText(text());
        b.textContent = 'copied!';
        setTimeout(() => this.syncShare(), 1200);
      } catch (_) { /* clipboard denied in this context — the text is in the box either way */ }
    };
    return b;
  }

  /** Show the seed the transport is holding. The `song` event does this once the engine is up; this
   *  is the same job BEFORE it is, because a link that carries a seed has to be visible on a widget
   *  that has not fetched 7.5 MB of runtime yet — the seed is what the visitor was sent, and waiting
   *  for a press of play to display it makes the link look like it did nothing. */
  syncSeedInput() {
    if (this.els.seedInput && !this.player.ready) this.els.seedInput.value = this.player.seed;
  }

  /** Take the seed in the box and START — what the seed bar's button means. A seed that will not
   *  parse says so INLINE, under the box, and changes nothing: typed mid-session it leaves the
   *  music playing, and arriving from a link it leaves the widget silent rather than playing
   *  something adjacent to what was sent. */
  playSeed(v) {
    const error = this.player.applySeed(v);
    if (error) { this.showMessage(error); return; }
    this.showMessage('');
    // Already playing? applySeed restarted the sequence itself; a second start would only churn.
    if (!this.player.playing) this.play();
  }

  // What the copy button hands over. `share-base` is a URL the HOST says reproduces the song on its
  // site — the element cannot work that out for itself, because reading `location` inside an embed
  // yields the host page's address, which reproduces nothing unless that page wired the seed up.
  // Without one it copies the seed, which does reproduce the song anywhere, and says so on the button.
  shareBase() { return (this.getAttribute('share-base') || '').trim(); }
  /** `station` false = this song, fully written down; true = the seed as it stands, still rolling. */
  shareText(station) {
    const base = this.shareBase();
    // No seed yet (nothing has been played and no link brought one) — a bare `page#` reproduces
    // nothing and looks like a broken link, so hand over the page.
    const seed = station ? this.seed : (this.player ? this.player.resolvedSeed : this.seed);
    if (!base) return seed;
    return seed ? `${base.split('#')[0]}#${seed}` : base.split('#')[0];
  }
  syncShare() {
    const link = !!this.shareBase();
    if (this.els.copyBtn) this.els.copyBtn.textContent = link ? 'copy link' : 'copy seed';
    if (this.els.copyStationBtn) this.els.copyStationBtn.textContent = link ? 'copy station link' : 'copy station';
  }

  buildVibe(board) {
    const sec = this.section(board, 'vibe', 'vibe');
    // The genre dropdown IS the seed's genre part: "Random" takes it out of the string (every song
    // rolls its own again), any other option writes it in.
    const genreLabel = document.createElement('label');
    genreLabel.className = 'dim';
    const genre = document.createElement('select');
    genre.setAttribute('part', 'genre-select');
    genre.onchange = () => {
      if (genre.value === '') this.player.rollGenre();
      else this.player.setGenre(parseInt(genre.value, 10));
    };
    genreLabel.append('genre ', genre);

    const reroll = document.createElement('button');
    reroll.textContent = '🎲 reroll';
    reroll.title = 'A fresh station at song 0 — anything you have pinned stays pinned';
    reroll.setAttribute('part', 'button reroll-button');
    reroll.onclick = () => this.player.reroll();

    const shuffle = document.createElement('button');
    shuffle.title = 'Every next song is a whole new station rather than the next song of this one';
    shuffle.setAttribute('part', 'button shuffle-button');
    shuffle.onclick = () => { this.player.setShuffle(!this.player.shuffle); this.syncShuffle(); };

    // The knobs live behind this. They are the deep end of the toy, and a wall of sliders is the
    // first thing a visitor sees otherwise.
    const tinker = document.createElement('button');
    tinker.setAttribute('part', 'button tinker-button');
    tinker.onclick = () => { this.setTinkering(!this._tinkering); };

    sec._head.append(genreLabel, reroll, shuffle, tinker);

    const body = document.createElement('div');
    body.setAttribute('part', 'vibe-body');
    body.hidden = true;

    // Dragging a knob pins the whole vibe, so the vibe needs its own way back to rolling — without
    // one, one accidental drag turns an endless station into one song forever.
    const clear = document.createElement('button');
    clear.textContent = '🎲 random vibe';
    clear.title = 'Stop pinning these knobs — let every song roll its own again';
    clear.setAttribute('part', 'button vibe-random');
    clear.onclick = () => this.player.rollVibe();
    const clearRow = document.createElement('div');
    clearRow.className = 'row';
    clearRow.append(clear);
    // The matrix is rebuilt on every vibe change, so it gets its own host — the button above it
    // must survive that.
    const matrixHost = document.createElement('div');
    body.append(clearRow, matrixHost);

    sec.append(body);
    this._tinkering = false;
    Object.assign(this.els, { vibeSec: sec, vibeBody: body, vibeMatrix: matrixHost, genre, shuffle, tinker, vibeRandom: clear });
    this.syncTinker();
  }

  setTinkering(on) {
    this._tinkering = !!on;
    if (this.els.vibeBody) this.els.vibeBody.hidden = !this._tinkering;
    this.syncTinker();
    if (this._tinkering) this.buildVibeEditor();
  }
  syncTinker() {
    if (this.els.tinker) this.els.tinker.textContent = this._tinkering ? 'hide knobs' : '🎛 tinker';
  }

  buildPlaylist(board) {
    const sec = this.section(board, 'playlist', 'playlist');
    const list = document.createElement('div');
    list.className = 'playlist';
    list.setAttribute('part', 'playlist');
    const jump = document.createElement('div');
    jump.className = 'row jump dim';
    jump.setAttribute('part', 'jump');
    const num = document.createElement('input');
    num.type = 'number'; num.min = '0'; num.value = '0';
    num.setAttribute('part', 'jump-input');
    const go = document.createElement('button');
    go.textContent = 'go';
    go.setAttribute('part', 'button jump-go');
    go.onclick = () => { const v = parseInt(num.value, 10); if (Number.isFinite(v)) this.player.seekTo(v); };
    const dl = document.createElement('button');
    dl.textContent = '⬇ .wav';
    dl.className = 'right';
    dl.setAttribute('part', 'button export-button');
    dl.onclick = () => this.download(this.player.displayN);
    jump.append('jump to ', num, go, dl);
    sec.append(list, jump);
    Object.assign(this.els, { playlistSec: sec, playlist: list });
  }

  // `controls` names the sections that show, e.g. controls="transport playlist". Absent = all of
  // them. An unknown name is ignored rather than fatal — a host should not get a blank widget for a
  // typo it cannot see the effect of.
  applyControls() {
    const raw = (this.getAttribute('controls') || '').trim().toLowerCase();
    const wanted = !raw || raw === 'all' ? SECTIONS : raw.split(/[\s,]+/).filter(Boolean);
    for (const name of SECTIONS) {
      const el = this.root.querySelector(`[data-section="${name}"]`);
      if (el) el.hidden = !wanted.includes(name);
    }
  }

  // ── Player events → UI ─────────────────────────────────────────────────────
  wirePlayer() {
    const p = this.player;
    const relay = (type, detail) => this.dispatchEvent(new CustomEvent(type, { detail, bubbles: true, composed: true }));

    p.addEventListener('progress', (e) => {
      const { loaded, total, ratio, done } = e.detail;
      this.els.boot.classList.toggle('show', !done);
      this.els.bootBar.classList.toggle('indet', !total);
      if (total) this.els.bootFill.style.width = `${Math.round(ratio * 100)}%`;
      this.els.bootLabel.textContent = total
        ? `loading engine — ${(loaded / 1048576).toFixed(1)} / ${(total / 1048576).toFixed(1)} MB`
        : 'loading engine…';
      relay('progress', e.detail);
    });
    p.addEventListener('ready', () => {
      this.els.boot.classList.remove('show');
      this.els.msg.textContent = '';
      this.populateGenres();
      this.buildVibeEditor();
      this.renderPlaylist();
      this.syncShuffle();
      relay('ready', {});
    });
    p.addEventListener('song', (e) => {
      this.els.now.textContent = `#${e.detail.n}`;
      // The box shows the seed AS IT STANDS — a station that is still rolling reads as one.
      this.els.seedInput.value = e.detail.seed;
      // '' is the Random entry: the genre is not in the string, so every song rolls its own.
      if (this.els.genre.options.length)
        this.els.genre.value = e.detail.genrePinned ? String(e.detail.genre) : '';
      this.syncVibePin(e.detail);
      relay('song', e.detail);
    });
    p.addEventListener('vibe', () => { this.buildVibeEditor(); this.syncShuffle(); });
    p.addEventListener('state', (e) => {
      this.els.playBtn.textContent = e.detail.playing ? '⏸' : '▶';
      e.detail.playing ? this.startClock() : this.stopClock();
      relay(e.detail.playing ? 'play' : 'pause', e.detail);
    });
    p.addEventListener('position', (e) => { this.syncPosition(); relay('position', e.detail); });
    p.addEventListener('buffer', (e) => {
      this.els.buf.classList.toggle('show', e.detail.generating);
      this.els.buf.textContent = e.detail.generating ? `generating #${e.detail.n}…` : '';
    });
    // A render landing is also how the current song's LENGTH becomes known, so the bar follows the
    // timeline as well as the playhead.
    p.addEventListener('timeline', () => { this.renderPlaylist(); this.syncPosition(); });
    p.addEventListener('error', (e) => { this.showError(e.detail.error); relay('error', e.detail); });
  }

  setVolume(v) {
    this.player.setVolume(v);
    if (this.els.volIn.value !== String(v)) this.els.volIn.value = String(v);
  }

  // ── The playhead ───────────────────────────────────────────────────────────
  /** Draw the seek bar from the transport's position — or, mid-drag, from the thumb the user is
   *  holding, which must win over the clock until they let go. */
  syncPosition() {
    const { seekIn, timeAt, timeTotal } = this.els;
    if (!seekIn || !this.player) return;
    const { duration, ratio } = this.player.position();
    const known = duration > 0;
    seekIn.disabled = !known;
    const shown = this._scrubbing && known ? parseInt(seekIn.value, 10) / 1000 : ratio;
    if (!this._scrubbing) {
      const v = String(Math.round(shown * 1000));
      if (seekIn.value !== v) seekIn.value = v;
    }
    timeAt.textContent = known ? fmtTime(shown * duration) : '0:00';
    timeTotal.textContent = known ? fmtTime(duration) : NO_TIME;
  }

  // The bar moves continuously while the transport only speaks on events, so the widget drives it
  // off the animation frame — which also means it stops costing anything in a hidden tab. The
  // interval is the fallback for an environment with no rAF (the node test's stub DOM is one).
  startClock() {
    if (this._clock !== null) return;
    const raf = typeof requestAnimationFrame === 'function';
    const step = () => {
      if (!this.player || !this.player.playing) { this.stopClock(); return; }
      this.syncPosition();
      if (raf) this._clock = requestAnimationFrame(step);
    };
    this._clockRaf = raf;
    this._clock = raf ? requestAnimationFrame(step) : setInterval(step, 200);
  }
  stopClock() {
    if (this._clock === null) return;
    if (this._clockRaf) cancelAnimationFrame(this._clock); else clearInterval(this._clock);
    this._clock = null;
    this.syncPosition();
  }

  syncShuffle() {
    if (!this.els.shuffle) return;
    const on = this.player.shuffle;
    this.els.shuffle.textContent = on ? '🔀 shuffle: ON' : '🔀 shuffle: OFF';
    this.els.shuffle.classList.toggle('on', on);
  }

  /** The "random vibe" button only means anything while a vibe is pinned. */
  syncVibePin(detail) {
    if (this.els.vibeRandom) this.els.vibeRandom.disabled = !detail.vibePinned;
  }

  /** Show a sentence under the seed box (a refused seed), or clear it. Inline, deliberately: a
   *  toast that has faded is no help to somebody looking at a widget that did nothing. */
  showMessage(text) {
    if (!this.els.msg) return;
    this.els.msg.className = text ? 'msg err' : 'msg';
    // Clearing an error does not mean clearing the LINE: an unbooted widget still owes the visitor
    // the reason it is sitting there doing nothing.
    this.els.msg.textContent = text || (this.player && this.player.ready ? '' : IDLE_MSG);
  }

  showError(e) {
    this.els.boot.classList.remove('show');
    this.els.msg.className = 'msg err';
    this.els.msg.textContent = String(e && e.message ? e.message : e);
  }

  download(songN) {
    const { blob, filename } = this.player.exportWav(songN);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    // The anchor has to be in a document to be clickable, and it must not be the host page's — the
    // widget's own shadow root is a document fragment that works for this and leaves no trace.
    this.root.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 4000);
  }

  populateGenres() {
    const sel = this.els.genre;
    sel.innerHTML = '';
    // "Random" FIRST, and it is what a seed with no genre in it reads as — the way back out of a
    // chosen genre, which a dropdown without it does not have.
    const any = document.createElement('option');
    any.value = '';
    any.textContent = 'Random';
    sel.append(any);
    for (let i = 0; i < this.player.mod.genreCount(); i++) {
      const o = document.createElement('option');
      o.value = String(i);
      o.textContent = this.player.mod.genreName(i);
      sel.append(o);
    }
    sel.value = this.player.pinnedGenre === null ? '' : String(this.player.genre);
  }

  renderPlaylist() {
    if (!this.player.ready) return;
    const list = this.els.playlist;
    list.innerHTML = '';
    for (const row of this.player.timeline()) {
      const el = document.createElement('div');
      el.className = 'plrow' + (row.now ? ' now' : '') + (row.cached ? ' cached' : '') + (row.generating ? ' gen' : '');
      el.setAttribute('part', 'playlist-row' + (row.now ? ' playlist-row-now' : ''));

      const label = document.createElement('button');
      label.className = 'pllabel';
      label.textContent = `${row.now ? '▶ ' : ''}#${row.n}`;
      label.onclick = () => this.player.seekTo(row.n);

      const genre = document.createElement('span');
      genre.className = 'plgenre';
      genre.textContent = row.genre;

      const status = document.createElement('span');
      status.className = 'plstatus';
      if (row.generating) {
        // No sub-song progress from the one-shot worker render — an honest indeterminate bar.
        const bar = document.createElement('span');
        bar.className = 'plbar';
        bar.append(document.createElement('i'));
        status.append(bar);
      } else {
        status.textContent = row.now ? 'now' : row.cached ? 'ready' : row.past ? 'gone' : '—';
      }

      const dl = document.createElement('button');
      dl.className = 'pldl';
      dl.textContent = '⬇';
      dl.title = `Export #${row.n} to WAV`;
      dl.onclick = (ev) => { ev.stopPropagation(); this.download(row.n); };

      el.append(label, genre, status, dl);
      list.append(el);
    }
  }

  // ── Vibe editor — per-instrument mixer matrix + GLOBAL strip ───────────────
  // Layout is driven entirely from the wasm field metadata for the current genre: each field
  // reports its voice (matrix row, or null for a GLOBAL knob) and column (0 volume / 1 tone /
  // 2 character / 3 extra). A new genre — or a new knob — is a pure-C# change; there is no JS-side
  // field table to keep in sync.
  buildVibeEditor() {
    if (!this.player.ready || !this._tinkering) return;
    const host = this.els.vibeMatrix;
    host.innerHTML = '';
    const fields = this.player.fields();
    const globals = fields.filter((f) => !f.voice);
    const voices = [];
    const byVoice = new Map();
    for (const f of fields) {
      if (!f.voice) continue;
      if (!byVoice.has(f.voice)) { byVoice.set(f.voice, COL_HEADERS.map(() => null)); voices.push(f.voice); }
      byVoice.get(f.voice)[f.column] = f;
    }

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
        // The column header already names volume/tone; only label the descriptive knobs.
        if (f) cell.append(this.buildKnob(f, f.name === COL_HEADERS[col] ? '' : f.name));
        row.append(cell);
      }
      matrix.append(row);
    }
    host.append(matrix);

    // Only when there is one: an unconditional heading over an empty grid is a section that says
    // the UI is missing something.
    if (globals.length) {
      const gl = document.createElement('div');
      gl.className = 'glabel';
      gl.textContent = 'GLOBAL';
      const grid = document.createElement('div');
      grid.className = 'global-grid';
      for (const f of globals) grid.append(this.buildKnob(f, f.name));
      host.append(gl, grid);
    }
  }

  buildKnob(f, labelText) {
    const cell = document.createElement('div');
    cell.className = 'knob';
    const head = document.createElement('div');
    head.className = 'knob-head';
    const name = document.createElement('span');
    name.className = 'knob-name';
    name.textContent = labelText;
    const val = document.createElement('span');
    val.className = 'knob-val';
    val.textContent = this.player.fieldDisplay(f.i);
    head.append(name, val);

    let input;
    if (f.choices.length > 0) {
      input = document.createElement('select');
      for (let c = 0; c < f.choices.length; c++) {
        const o = document.createElement('option');
        o.value = String(c);
        o.textContent = f.choices[c];
        input.append(o);
      }
      input.selectedIndex = Math.round(this.player.fieldNorm(f.i) * (f.choices.length - 1));
      input.onchange = () => {
        this.player.setField(f, input.selectedIndex / (f.choices.length - 1));
        val.textContent = this.player.fieldDisplay(f.i);
      };
      input.setAttribute('part', 'select knob-select');
    } else {
      // Snap to the same discrete grid the seed encodes (one level per base-36 char), so the slider
      // can only land on values the vibe can actually represent.
      const steps = this.player.levels() - 1;
      input = document.createElement('input');
      input.type = 'range'; input.min = '0'; input.max = String(steps); input.step = '1';
      input.value = String(Math.round(this.player.fieldNorm(f.i) * steps));
      input.oninput = () => {
        this.player.setField(f, parseInt(input.value, 10) / steps);
        val.textContent = this.player.fieldDisplay(f.i);
      };
      input.setAttribute('part', 'slider knob-slider');
    }
    input.className = 'knob-input';
    cell.append(head, input);
    return cell;
  }
}

if (!customElements.get('skafinity-player')) customElements.define('skafinity-player', SkafinityPlayerElement);

export default SkafinityPlayerElement;
