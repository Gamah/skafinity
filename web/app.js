// skafinity — the toy page's host script.
//
// There is almost nothing here, and that is the point: the page is a HOST for <skafinity-player>,
// exactly like anyone else's page would be. Everything that used to live in this file is now
// web/player.js (transport) and web/skafinity-element.js (UI), because a scheduler that reaches for
// getElementById and a UI that owns module-scope song state cannot be embedded anywhere.
//
// What stays a PAGE concern, and is the reason the element does not do it:
//
//   THE URL IS THE SONG. The element never writes location.hash — an embed has no claim on the
//   address bar of the page that took it in, and two widgets on one page would fight over it. So
//   the toy wires it up itself, from the element's public `song` event, and that wiring is also the
//   worked example a host copies out of docs/embedding.md.
const el = document.querySelector('skafinity-player');

// Seed the element BEFORE the definition loads, so it is constructed with the shared song rather
// than rolling a random one and being corrected a frame later. A static import would be hoisted
// above this line, hence the dynamic one.
const hash = location.hash.slice(1);
if (hash) el.setAttribute('seed', hash);

await import('./skafinity-element.js');

el.addEventListener('song', (e) => {
  if (location.hash.slice(1) !== e.detail.seed) history.replaceState(null, '', '#' + e.detail.seed);
});

window.addEventListener('hashchange', () => {
  const h = location.hash.slice(1);
  if (h && h !== el.seed) el.seed = h;
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

document.getElementById('copyBtn').onclick = async (ev) => {
  const btn = ev.currentTarget;
  try {
    await navigator.clipboard.writeText(location.href);
    btn.textContent = 'copied!';
    setTimeout(() => (btn.textContent = 'copy link'), 1200);
  } catch (_) { /* clipboard denied — the URL is in the address bar either way */ }
};
