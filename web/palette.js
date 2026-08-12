// skafinity — the widget palette, derived from ONE accent colour.
//
// This is a port of sbox-library/Skafinity/Code/UI/SkafinityTheme.cs, and the port is the point:
// the s&box panel and the web element must not end up with two colour schemes that drift. The
// factors below are that file's factors, and test/palette.mjs reads them back out of the C# so a
// change on either side fails the suite rather than quietly forking.
//
// SkafinityTheme only describes a DARK board — every surface is the accent scaled toward black and
// every ink is the accent mixed toward white. An embed cannot assume that: a dark widget dropped
// into a light page is the failure mode the whole sniff exists to avoid. So light mode is the same
// factors REFLECTED rather than a second scheme:
//
//     surfaces   dark: Scale(f)        light: Mix(1 - f)      (toward black / toward white)
//     inks       dark: Mix(f)          light: Scale(1 - f)    (toward white / toward black)
//     fills      Scale(f) in BOTH — a fill has to read against its own surface either way, and
//                the accent at 60% is the one value that does that on a near-black board and on a
//                near-white one.
//
// One law, two directions: `1 - f` and swap the operator. There is no second table to keep in step.
//
// DOM-free on purpose — the sniffing that FINDS an accent needs a document, but deciding what a
// palette is does not, so this half runs under node.

// The five factors, exactly as SkafinityTheme.cs declares them.
export const FACTORS = {
  bg: 0.09,     // board background        (Scale)
  cell: 0.04,   // button / cell fill      (Scale)
  fill: 0.6,    // filled ticks, selection (Scale, both modes)
  text: 0.8,    // primary ink             (Mix)
  dim: 0.72,    // labels, secondary ink   (Mix)
};

// Alphas of the neutral edge tokens, lifted from SkafinityMusicPanel.razor.scss. There they are
// white-over-black constants; here they ride on the mode's ink (white on a dark board, black on a
// light one) so a border stays a border rather than vanishing into the page.
export const EDGES = { edge: 0.12, rule: 0.10, hover: 0.35, pick: 0.85, busy: 0.55, surface: 0.07 };

// Unset = neutral, and a MID gray rather than a dark one — the palette scales down for the fills
// and up toward white for the ink, so the hue it starts from has to sit between them for both ends
// to land. (SkafinityTheme.NeutralAccent, same value.)
export const NEUTRAL_ACCENT = '#7a7a7a';

// ── Colour maths ───────────────────────────────────────────────────────────────
// Components are 0..1 floats, matching the C#; only the final CSS string is 0..255.
export const scale = (c, f) => ({ r: c.r * f, g: c.g * f, b: c.b * f });
export const mix = (c, towardWhite) => ({
  r: c.r + (1 - c.r) * towardWhite,
  g: c.g + (1 - c.g) * towardWhite,
  b: c.b + (1 - c.b) * towardWhite,
});
const to255 = (v) => Math.round(Math.min(1, Math.max(0, v)) * 255);
export const rgb = (c) => `rgb(${to255(c.r)},${to255(c.g)},${to255(c.b)})`;
export const rgba = (c, a) => `rgba(${to255(c.r)},${to255(c.g)},${to255(c.b)},${a})`;

const HEX3 = /^#([0-9a-f])([0-9a-f])([0-9a-f])([0-9a-f])?$/i;
const HEX6 = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})?$/i;

// Parse the colour syntaxes getComputedStyle actually hands back (rgb/rgba, and the hex an author
// wrote into a custom property — a custom property is NOT resolved to rgb() for us). Returns
// { r, g, b, a } in 0..1, or null for anything unparseable, `transparent`, or a colour we cannot
// evaluate without a browser (color-mix, lab, a bare keyword other than the two below).
export function parseColor(input) {
  const s = String(input || '').trim().toLowerCase();
  if (!s || s === 'transparent' || s === 'none') return null;
  if (s === 'white') return { r: 1, g: 1, b: 1, a: 1 };
  if (s === 'black') return { r: 0, g: 0, b: 0, a: 1 };
  let m = HEX6.exec(s) || HEX3.exec(s);
  if (m) {
    const wide = s.length > 5;
    const v = (h) => parseInt(wide ? h : h + h, 16) / 255;
    return { r: v(m[1]), g: v(m[2]), b: v(m[3]), a: m[4] === undefined ? 1 : v(m[4]) };
  }
  // rgb(1 2 3 / 40%) and rgb(1, 2, 3, 0.4) both reach us depending on the browser.
  m = /^rgba?\(([^)]+)\)$/.exec(s);
  if (m) {
    const parts = m[1].split(/[,/\s]+/).filter(Boolean).map((p) => p.trim());
    if (parts.length < 3) return null;
    const num = (p, scaleTo) => {
      const f = parseFloat(p);
      if (!Number.isFinite(f)) return null;
      return p.endsWith('%') ? f / 100 : f / scaleTo;
    };
    const r = num(parts[0], 255), g = num(parts[1], 255), b = num(parts[2], 255);
    if (r === null || g === null || b === null) return null;
    const a = parts.length > 3 ? num(parts[3], 1) : 1;
    return { r, g, b, a: a === null ? 1 : a };
  }
  return null;
}

// WCAG relative luminance — the same maths a contrast checker uses, so "is this page light?" is
// answered by perceived brightness rather than by a channel average (a saturated blue and a
// saturated yellow of the same average are nowhere near the same brightness).
export function luminance(c) {
  const lin = (v) => (v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));
  return 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b);
}

// Light or dark, from a background that was actually measured on the host page. `prefersDark` is
// the LAST resort and only used when nothing opaque was found: a page that paints no background
// at all is the only case where the OS preference is better evidence than the page itself.
//
// The threshold is 0.18 rather than 0.5 because luminance is not linear in perceived lightness —
// mid-gray #808080 has a luminance of 0.216, and it reads as a light surface to sit on.
export function chooseMode(measuredBg, prefersDark = false) {
  const c = typeof measuredBg === 'string' ? parseColor(measuredBg) : measuredBg;
  if (!c || !c.a) return prefersDark ? 'dark' : 'light';
  return luminance(c) < 0.18 ? 'dark' : 'light';
}

// The custom properties an accent may be hiding in, most specific first. A page that has bothered
// to name --ska-accent means it for this widget; --bs-primary is Bootstrap's and is the broadest
// guess, so it goes last.
export const ACCENT_VARS = [
  '--ska-accent', '--accent', '--primary', '--color-primary', '--color-accent', '--bs-primary',
];

// First var in the priority list that parses to a usable colour. `lookup(name)` returns the raw
// custom-property value (the caller decides which element it is resolved against); anything
// unparseable — a gradient, a color-mix(), an empty string — is skipped rather than fatal.
export function pickAccent(lookup, vars = ACCENT_VARS) {
  for (const name of vars) {
    const c = parseColor(lookup(name));
    if (c && c.a > 0.5) return { color: c, source: name };
  }
  return null;
}

// ── The palette ────────────────────────────────────────────────────────────────
// Returns { '--_ska-bg': 'rgb(...)', ... } — the DERIVED half of the token set. They are written
// under a `--_ska-` prefix and every rule reads `var(--ska-x, var(--_ska-x))`, so a host that sets
// the public `--ska-x` beats the derivation for that one token without disturbing the rest.
export function derivePalette(accent, mode = 'dark') {
  const a = (typeof accent === 'string' ? parseColor(accent) : accent) || parseColor(NEUTRAL_ACCENT);
  const dark = mode !== 'light';
  // The reflection, in one place: a surface goes toward the mode's ground, an ink toward its ink.
  const surface = (f) => (dark ? scale(a, f) : mix(a, 1 - f));
  const ink = (f) => (dark ? mix(a, f) : scale(a, 1 - f));
  // Neutral edges ride on the mode's ink colour (white on dark, black on light).
  const edgeInk = dark ? { r: 1, g: 1, b: 1 } : { r: 0, g: 0, b: 0 };
  const F = FACTORS;

  return {
    '--_ska-bg': rgb(surface(F.bg)),
    '--_ska-cell': rgb(surface(F.cell)),
    '--_ska-fill': rgb(scale(a, F.fill)),
    '--_ska-fill-soft': rgba(scale(a, F.fill), 0.25),
    '--_ska-accent': rgb(a),
    '--_ska-accent-bg': rgba(a, 0.2),
    '--_ska-text': rgba(ink(F.text), 0.9),
    '--_ska-text-dim': rgba(ink(F.dim), 0.7),
    '--_ska-edge': rgba(edgeInk, EDGES.edge),
    '--_ska-rule': rgba(edgeInk, EDGES.rule),
    '--_ska-hover': rgba(edgeInk, EDGES.hover),
    '--_ska-pick': rgba(edgeInk, EDGES.pick),
    '--_ska-busy': rgba(edgeInk, EDGES.busy),
    '--_ska-surface': rgba(edgeInk, EDGES.surface),
  };
}
