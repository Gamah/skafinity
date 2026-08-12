// skafinity — the toy page's host script.
//
// There is almost nothing here, and that is the point: the page is a HOST for <skafinity-player>,
// exactly like anyone else's page would be. Everything that used to live in this file is now
// web/player.js (transport) and web/skafinity-element.js (UI), because a scheduler that reaches for
// getElementById and a UI that owns module-scope song state cannot be embedded anywhere.
//
// What stays a PAGE concern, and is the reason the element does not do it:
//
//   A LINK IS THE SONG, BUT THE ADDRESS BAR IS NOT. The element never writes location.hash — an
//   embed has no claim on the address bar of the page that took it in, and two widgets on one page
//   would fight over it. The toy does the URL wiring itself, and this is the worked example a host
//   copies out of docs/embedding.md — but it only wires the INBOUND half: a link that carries a
//   seed is honoured, and then the seed comes straight back out of the bar. A shuffled station
//   re-writing the URL every ~75 seconds fills the back button with songs nobody chose to go back
//   to, and leaves an address that is stale the moment it is read. `share-base` below is the other
//   half: the widget's copy button builds the same link on demand, from this page's address.
const el = document.querySelector('skafinity-player');

// Seed the element BEFORE the definition loads, so it is constructed with the shared song rather
// than rolling a random one and being corrected a frame later. A static import would be hoisted
// above this line, hence the dynamic one.
const hash = location.hash.slice(1);
if (hash) el.setAttribute('seed', hash);

const pageUrl = () => location.href.split('#')[0];
// What the widget's "copy link" hands out: this page, plus whatever is playing when it is pressed.
el.setAttribute('share-base', pageUrl());
// Consume the link and tidy up after it. replaceState rather than assigning location.hash, which
// would add a history entry (and fire hashchange straight back at us).
const clearHash = () => { if (location.hash) history.replaceState(null, '', pageUrl()); };

await import('./skafinity-element.js');
clearHash();

// Someone can still paste a skafinity link into the bar of a page that is already open.
window.addEventListener('hashchange', () => {
  const h = location.hash.slice(1);
  if (h && h !== el.seed) el.seed = h;
  clearHash();
});

el.addEventListener('error', (e) => {
  const detail = e.detail && e.detail.error;
  document.getElementById('status').textContent = detail
    ? 'skafinity: ' + (detail.message || detail)
    : '';
});

// ── Light / dark, the way a HOST does it ──────────────────────────────────────
// The page swaps its own palette and then asks the widget to look again. It deliberately does NOT
// set theme="light" on the element: driving it through the sniff is what proves the sniff works,
// and it is what a host page with its own theme switcher would get for free if it did nothing at
// all. `auto` (no data-theme) means the OS decides, which is why the media-query listener stays
// live in that state only.
const THEME_KEY = 'skafinity.page-theme';
const MODES = ['auto', 'light', 'dark'];
const themeBtn = document.getElementById('themeBtn');
let mode = localStorage.getItem(THEME_KEY);
if (!MODES.includes(mode)) mode = 'auto';

function applyTheme() {
  if (mode === 'auto') document.documentElement.removeAttribute('data-theme');
  else document.documentElement.setAttribute('data-theme', mode);
  themeBtn.textContent = `theme: ${mode}`;
  // The page's colours have changed under the widget; nothing tells it that but us.
  el.refreshTheme();
}
themeBtn.onclick = () => {
  mode = MODES[(MODES.indexOf(mode) + 1) % MODES.length];
  localStorage.setItem(THEME_KEY, mode);
  applyTheme();
};
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (mode === 'auto') applyTheme(); });
applyTheme();
