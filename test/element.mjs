// Does <skafinity-player> actually build, theme itself, and react to the transport?
//
// There is no browser on a dev host and this repo takes no npm dependencies, so the DOM below is a
// STUB — about a hundred lines of it, enough to run the element's own code and nothing more.
//
// BE CLEAR ABOUT WHAT THAT PROVES. It proves the element constructs its tree, wires its events,
// picks a palette, applies `controls`, and tears down — i.e. that every function it calls exists
// and does what it says on values it will really see. It proves NOTHING about layout, cascade,
// shadow-DOM encapsulation, ::part() actually being addressable, or how any of it looks: the stub
// has no CSS engine, so `getComputedStyle` here returns what this file decided it returns. The
// sniff against a REAL page is what web/embed-light.html and web/embed-dark.html are for, and that
// still has to be looked at by a person.
//
//   node test/element.mjs        (part of `make test`)
import { readFileSync } from 'node:fs';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

// ── The stub DOM ───────────────────────────────────────────────────────────────
class Style {
  constructor() { this.props = new Map(); }
  setProperty(k, v) { this.props.set(k, v); }
  getPropertyValue(k) { return this.props.get(k) || ''; }
}
class ClassList {
  constructor() { this.set = new Set(); }
  add(...c) { c.forEach((x) => this.set.add(x)); }
  remove(...c) { c.forEach((x) => this.set.delete(x)); }
  contains(c) { return this.set.has(c); }
  toggle(c, on) { const want = on === undefined ? !this.set.has(c) : !!on; want ? this.set.add(c) : this.set.delete(c); return want; }
}
class El extends EventTarget {
  constructor(tag) {
    super();
    this.tagName = String(tag).toUpperCase();
    this.childNodes = [];
    this.attrs = new Map();
    this.dataset = {};
    this.style = new Style();
    this.classList = new ClassList();
    this.parentElement = null;
    this.text = '';
  }
  get className() { return [...this.classList.set].join(' '); }
  set className(v) { this.classList.set = new Set(String(v).split(/\s+/).filter(Boolean)); }
  get textContent() { return this.text || this.childNodes.map((c) => (c.textContent ?? String(c))).join(''); }
  set textContent(v) { this.childNodes = []; this.text = String(v); }
  set innerHTML(v) { this.childNodes = []; this.text = String(v).replace(/<[^>]*>/g, ''); }
  get children() { return this.childNodes.filter((c) => c instanceof El); }
  get options() { return this.children; }
  append(...kids) {
    for (const k of kids) {
      if (k instanceof El) { k.parentElement = this; this.text = ''; }
      this.childNodes.push(k);
    }
  }
  remove() {
    if (this.parentElement) this.parentElement.childNodes = this.parentElement.childNodes.filter((c) => c !== this);
    this.parentElement = null;
  }
  setAttribute(k, v) { this.attrs.set(k, String(v)); if (k.startsWith('data-')) this.dataset[k.slice(5)] = String(v); }
  getAttribute(k) { return this.attrs.has(k) ? this.attrs.get(k) : null; }
  hasAttribute(k) { return this.attrs.has(k); }
  removeAttribute(k) { this.attrs.delete(k); }
  get hidden() { return !!this._hidden; }
  set hidden(v) { this._hidden = !!v; }
  getRootNode() { return globalThis.document; }
  // Only the one selector shape the element uses.
  querySelector(sel) {
    const m = /^\[data-section="(.+)"\]$/.exec(sel);
    const walk = (node) => {
      for (const c of node.children) {
        if (m && c.dataset.section === m[1]) return c;
        const hit = walk(c);
        if (hit) return hit;
      }
      return null;
    };
    return walk(this);
  }
  // The element only ever queries its own descendants, so a flat scan is enough.
  all(pred, out = []) {
    for (const c of this.children) { if (pred(c)) out.push(c); c.all(pred, out); }
    return out;
  }
}
class ShadowRoot extends El {
  constructor(host) { super('#shadow'); this.host = host; }
}
globalThis.HTMLElement = class extends El {
  constructor() { super('skafinity-player'); this.isConnected = false; }
  attachShadow() { this.shadowRoot = new ShadowRoot(this); return this.shadowRoot; }
};
globalThis.document = new El('document');
globalThis.document.createElement = (tag) => new El(tag);
globalThis.document.createTextNode = (t) => String(t);
globalThis.document.documentElement = new El('html');
globalThis.document.body = new El('body');
globalThis.document.append(globalThis.document.documentElement);

// The page this widget is pretending to sit on. `pageBg` is what findBackground will read off the
// element's ancestors, so flipping it is how the test poses as a light or a dark host.
const page = { pageBg: 'rgb(255, 255, 255)', accentVar: '', linkColor: 'rgb(13, 110, 253)', radius: '6px', font: 'Georgia, serif' };
globalThis.getComputedStyle = (el) => ({
  fontFamily: page.font,
  borderRadius: el.tagName === 'BUTTON' ? page.radius : '0px',
  backgroundColor: el.tagName === 'BUTTON' ? 'rgb(13, 110, 253)' : (el === host || el === globalThis.document.documentElement ? page.pageBg : 'rgba(0, 0, 0, 0)'),
  borderTopColor: 'rgb(13, 110, 253)',
  color: page.linkColor,
  getPropertyValue: (name) => (name === '--accent' ? page.accentVar : ''),
});
const mqListeners = [];
globalThis.matchMedia = (q) => ({
  matches: q.includes('dark') && page.prefersDark === true,
  addEventListener: (_, fn) => mqListeners.push(fn),
  removeEventListener: () => {},
});
const defined = new Map();
globalThis.customElements = { define: (n, c) => defined.set(n, c), get: (n) => defined.get(n) };

// A parent for the widget, so the probe has somewhere real to go.
const host = new El('div');
globalThis.document.body.append(host);

// ── The engine + audio stubs (same shape as test/player.mjs) ──────────────────
const stubEngine = () => ({
  defaultConfig: () => Float64Array.from({ length: 8 }, () => 0),
  ringOutSeconds: () => 2.5,
  applyAdvancedConfig: (c) => c,
  genreCount: () => 3,
  genreName: (i) => `genre${i}`,
  getGenre: (c) => c[0] | 0,
  setGenre: (c, i) => { const n = c.slice(); n[0] = i; return n; },
  encodeVibe: (c) => `v${c[0] | 0}`,
  decodeVibe: (v, c) => c.slice(),
  looksLikeVibe: () => true,
  rollVibe: (c) => c.slice(),
  rollVibeFor: (c, t, n) => { const x = c.slice(); x[0] = n % 3; return x; },
  vibeFieldCount: () => 3,
  vibeFieldInfo: (g, i) => ({ name: ['VOLUME', 'TONE', 'DRIVE'][i], min: 0, max: 1, isInt: false,
    voice: i < 2 ? 'BASS' : null, column: i % 4, choices: [] }),
  vibeLevels: () => 36,
  setVibeField: (c) => c.slice(),
  getVibeNorm: () => 0.5,
  vibeDisplay: () => '0.50',
  songToWav: () => new Uint8Array([1]),
  parseSeed: (s) => { const p = String(s).split(':'); return { vibe: p[0] || '', tag: p[1] || '', n: parseInt(p[2], 10) || 0, hasN: p.length > 2 }; },
});
class FakeCtx {
  constructor() { this.currentTime = 0; this.state = 'running'; this.destination = {}; }
  resume() { return Promise.resolve(); }
  createGain() { return { gain: { value: 1, setValueCurveAtTime() {}, setValueAtTime() {} }, connect: (x) => x, disconnect() {} }; }
  createBuffer(ch, f, r) { return { duration: f / r, copyToChannel() {} }; }
  createBufferSource() { return { buffer: null, loop: false, onended: null, connect: (x) => x, disconnect() {}, start() {}, stop() {} }; }
}
class FakeWorker {
  constructor() { this.jobs = []; this.killed = false; }
  postMessage(m) { this.jobs.push(m); }
  terminate() { this.killed = true; }
}

const { SkafinityPlayerElement } = await import('../web/skafinity-element.js');
SkafinityPlayerElement.playerDefaults = {
  engine: stubEngine(), audioContext: new FakeCtx(), createWorker: () => new FakeWorker(),
  configUrl: null, storage: null,
};

check('the element registered itself as <skafinity-player>', defined.get('skafinity-player') === SkafinityPlayerElement);

// ── It builds ──────────────────────────────────────────────────────────────────
const el = new SkafinityPlayerElement();
el.setAttribute('seed', 'v0:demo:7');
host.append(el);
el.isConnected = true;
el.connectedCallback();

const parts = new Set(el.shadowRoot.all(() => true).flatMap((n) => (n.getAttribute('part') || '').split(/\s+/)).filter(Boolean));
check('it built a shadow tree', el.shadowRoot.children.length >= 2);
check('nothing was built into the host page itself', host.children.filter((c) => c !== el).length === 0);
for (const p of ['transport', 'seed-bar', 'playlist', 'slider', 'button', 'progress', 'play-button',
                 'seek', 'seek-slider', 'time-elapsed', 'time-total'])
  check(`::part(${p}) is exposed`, parts.has(p), [...parts].join(' '));
check('the probe left nothing behind in the page', globalThis.document.body.all((n) => n.getAttribute('aria-hidden') === 'true').length === 0);

// ── It themed itself off the page ──────────────────────────────────────────────
{
  const bg = el.style.getPropertyValue('--_ska-bg');
  check('a palette was applied to the host element', !!bg, bg);
  check('a white page yields a LIGHT widget', el.themeInfo.mode === 'light', JSON.stringify(el.themeInfo));
  check('the accent came from the page link colour when no var was set', el.themeInfo.accentSource === 'link colour');
  check("the host page's font was adopted", el.style.getPropertyValue('--_ska-font') === page.font);
  check("the host page's button radius was adopted", el.style.getPropertyValue('--_ska-radius') === page.radius);

  page.accentVar = '#ff5c2a';
  el.refreshTheme();
  check('--accent on the page wins over the link colour', el.themeInfo.accentSource === '--accent');
  el.setAttribute('accent', '#2f9450');
  el.attributeChangedCallback('accent', null, '#2f9450');
  check('the accent attribute wins over everything', el.themeInfo.accentSource === 'attribute');

  el.setAttribute('theme', 'dark');
  el.attributeChangedCallback('theme', null, 'dark');
  check('theme="dark" overrides the measured page', el.themeInfo.mode === 'dark');
  const darkBg = el.style.getPropertyValue('--_ska-bg');
  el.setAttribute('theme', 'auto');
  el.attributeChangedCallback('theme', 'dark', 'auto');
  check('theme="auto" hands the decision back to the page', el.themeInfo.mode === 'light');
  check('the two modes really are different palettes', darkBg !== el.style.getPropertyValue('--_ska-bg'));

  page.pageBg = 'rgb(20, 17, 14)';
  el.refreshTheme();
  check('a dark page yields a DARK widget', el.themeInfo.mode === 'dark');
  page.pageBg = 'rgb(255, 255, 255)';
  el.refreshTheme();
}

// ── Nothing is fetched until play ──────────────────────────────────────────────
check('the engine is not booted on connect', !el.player.ready);
check('the widget says why it is idle', /press play/.test(el.els.msg.textContent), el.els.msg.textContent);

// ── Boot progress ──────────────────────────────────────────────────────────────
{
  el.player.emit('progress', { loaded: 1048576, total: 7340032, ratio: 1048576 / 7340032, done: false });
  check('the progress bar shows during the download', el.els.boot.classList.contains('show'));
  check('the progress bar is determinate once a total is known', !el.els.bootBar.classList.contains('indet'));
  check('the progress fill tracks the ratio', el.els.bootFill.style.width === '14%', el.els.bootFill.style.width);
  check('the label reports real megabytes', /1\.0 \/ 7\.0 MB/.test(el.els.bootLabel.textContent), el.els.bootLabel.textContent);
  el.player.emit('progress', { loaded: 0, total: 0, ratio: 0, done: false });
  check('no Content-Length yet means an indeterminate sweep', el.els.bootBar.classList.contains('indet'));
}

// ── It plays, and the UI follows the transport ─────────────────────────────────
await el.play();
await wait(10);
check('play boots the engine', el.player.ready);
check('the progress bar is gone once ready', !el.els.boot.classList.contains('show'));
check('the genre select was populated from the engine', el.els.genre.options.length === 3);
check('the vibe matrix was built from the field metadata', el.els.vibeBody.children.length >= 1);
check('the playlist rendered a window of songs', el.els.playlist.children.length > 1, `${el.els.playlist.children.length} rows`);
check('the seed box shows the seed', el.els.seedInput.value === el.player.seed, el.els.seedInput.value);
check('the seed the attribute asked for is the one playing', el.player.seed.endsWith(':demo:7'), el.player.seed);
check('the play button flipped to pause', el.els.playBtn.textContent === '⏸');
el.pause();
check('…and back', el.els.playBtn.textContent === '▶');

// ── The seek bar ───────────────────────────────────────────────────────────────
// Nothing renders in this stub (the fake worker never answers), so the bar's other job is on show
// here: a song whose length is not known yet gets an inert bar and a dash, not a guess.
{
  check('the seek bar is inert until a song is rendered', el.els.seekIn.disabled === true);
  check('…and says so rather than showing a length', el.els.timeTotal.textContent === '–:––',
    el.els.timeTotal.textContent);

  // Stand a rendered song up by hand — the transport's own cache is what position() measures.
  el.player.rendered.set(el.player.displayN, { buffer: { duration: 80 }, info: null });
  el.syncPosition();
  check('a rendered song enables the bar', el.els.seekIn.disabled === false);
  check('…and reports its length', el.els.timeTotal.textContent === '1:20', el.els.timeTotal.textContent);

  // A drag: `input` moves the label only, `change` (the release) is what seeks. Paused, that lands
  // as the offset the next play comes in at.
  el.els.seekIn.value = '500';
  el.els.seekIn.oninput();
  check('a drag moves the elapsed time with the thumb', el.els.timeAt.textContent === '0:40', el.els.timeAt.textContent);
  check('…and does not seek yet', el.player.resumeOffset === 0, String(el.player.resumeOffset));
  el.els.seekIn.onchange();
  check('releasing the thumb seeks', Math.abs(el.player.resumeOffset - 40) < 0.01, String(el.player.resumeOffset));
  el.player.rendered.clear();
  el.player.resumeOffset = 0;
  el.syncPosition();
}

{
  const seen = [];
  el.addEventListener('song', (e) => seen.push(e.detail.n));
  el.player.seekTo(9);
  check('a song event reaches the host page', seen.includes(9), seen.join(','));
  el.player.emit('buffer', { generating: true, n: 9 });
  check('a stalled seek says so', el.els.buf.classList.contains('show') && /generating #9/.test(el.els.buf.textContent));
}

// ── controls= ──────────────────────────────────────────────────────────────────
{
  el.setAttribute('controls', 'transport playlist');
  el.attributeChangedCallback('controls', null, 'transport playlist');
  const hidden = (name) => el.shadowRoot.querySelector(`[data-section="${name}"]`).hidden;
  check('controls= hides what it does not name', hidden('vibe') && hidden('seed'));
  check('…and keeps what it does', !hidden('transport') && !hidden('playlist'));
  el.setAttribute('controls', 'transport, nonsense');
  el.attributeChangedCallback('controls', null, 'transport, nonsense');
  check('an unknown control name is ignored, not fatal', !hidden('transport') && hidden('playlist'));
  el.removeAttribute('controls');
  el.attributeChangedCallback('controls', 'x', null);
  check('no controls attribute means all of them', !hidden('vibe') && !hidden('playlist'));
}

// ── Teardown ───────────────────────────────────────────────────────────────────
{
  const p = el.player;
  el.disconnectedCallback();
  check('removal destroys the transport', p.destroyed && el.player === null);
}

// ── Two on one page ────────────────────────────────────────────────────────────
{
  const a = new SkafinityPlayerElement(), b = new SkafinityPlayerElement();
  host.append(a, b);
  a.isConnected = b.isConnected = true;
  a.connectedCallback(); b.connectedCallback();
  await a.play(); await b.play();
  check('two widgets on one page both boot', a.player.ready && b.player.ready);
  check('…with independent seeds', a.player.seed !== b.player.seed || a.player.tag !== b.player.tag);
  check('…and separate transports', a.player !== b.player);
  a.disconnectedCallback(); b.disconnectedCallback();
}

// ── The element is the only file allowed to touch the document ─────────────────
{
  const src = readFileSync(new URL('../web/skafinity-element.js', import.meta.url), 'utf8');
  const code = src.replace(/\/\/[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');
  check('the element never writes the URL', !/location\.(hash|href)\s*=|history\./.test(code));
  check('the element never writes un-namespaced storage', !/localStorage/.test(code));
}

console.log(failures ? `\n${failures} failure(s)` : '\nall element checks passed');
process.exit(failures ? 1 : 0);
