// Does the web element's palette still agree with the s&box panel's?
//
// SkafinityTheme.cs is the original derivation and web/palette.js is a port of it, which is exactly
// the situation that drifts: two files, one rule, nobody looking. So this test does not hardcode
// the factors — it READS them out of the C# and checks the JS reproduces the same colours. Change
// Scale(0.09) on either side and this fails rather than the two quietly forking.
//
// The light half has nothing to compare against (the C# only describes a dark board), so what is
// asserted there is the LAW instead: light is the same factor reflected, `1 - f`, with Scale and
// Mix swapped. Plus the properties that actually matter for an embed — a light page never yields a
// dark widget, and every token parses as a colour.
//
//   node test/palette.mjs        (part of `make test`; needs no wasm bundle)
import { readFileSync } from 'node:fs';
import {
  FACTORS, EDGES, NEUTRAL_ACCENT, derivePalette, chooseMode, parseColor, pickAccent,
  scale, mix, rgb, rgba, luminance,
} from '../web/palette.js';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}

const cs = readFileSync(new URL('../sbox-library/Skafinity/Code/UI/SkafinityTheme.cs', import.meta.url), 'utf8');

// ── The factors, lifted out of the C# ──
// e.g.  public static string Bg => Rgb( Scale( Hue, 0.09f ) );
const csFactor = (prop) => {
  const m = new RegExp(`${prop}\\s*=>\\s*Rgba?\\(\\s*(Scale|Mix)\\(\\s*Hue,\\s*([0-9.]+)f`).exec(cs);
  return m ? { op: m[1], f: parseFloat(m[2]) } : null;
};
// The alpha is the LAST argument, so the match has to be greedy up to it — a lazy one stops at the
// factor inside the nested Scale()/Mix() call and silently reports that instead.
const csAlpha = (prop) => {
  const m = new RegExp(`${prop}\\s*=>\\s*Rgba\\(.*,\\s*([0-9.]+)f\\s*\\);`).exec(cs);
  return m ? parseFloat(m[1]) : null;
};

const fromCs = {
  bg: csFactor('Bg'), cell: csFactor('Cell'), fill: csFactor('CellFill'),
  text: csFactor('Text'), dim: csFactor('TextDim'),
};
check('every factor was found in SkafinityTheme.cs', Object.values(fromCs).every(Boolean),
  JSON.stringify(fromCs));

check('bg factor matches the C#', fromCs.bg && fromCs.bg.op === 'Scale' && fromCs.bg.f === FACTORS.bg,
  `C# ${fromCs.bg && fromCs.bg.f} vs JS ${FACTORS.bg}`);
check('cell factor matches the C#', fromCs.cell && fromCs.cell.op === 'Scale' && fromCs.cell.f === FACTORS.cell,
  `C# ${fromCs.cell && fromCs.cell.f} vs JS ${FACTORS.cell}`);
check('fill factor matches the C#', fromCs.fill && fromCs.fill.op === 'Scale' && fromCs.fill.f === FACTORS.fill,
  `C# ${fromCs.fill && fromCs.fill.f} vs JS ${FACTORS.fill}`);
check('text factor matches the C# (and is a Mix)', fromCs.text && fromCs.text.op === 'Mix' && fromCs.text.f === FACTORS.text,
  `C# ${fromCs.text && fromCs.text.f} vs JS ${FACTORS.text}`);
check('dim factor matches the C# (and is a Mix)', fromCs.dim && fromCs.dim.op === 'Mix' && fromCs.dim.f === FACTORS.dim,
  `C# ${fromCs.dim && fromCs.dim.f} vs JS ${FACTORS.dim}`);

const csNeutral = /NeutralAccent\s*=\s*Color\.Parse\(\s*"(#[0-9a-fA-F]{6})"/.exec(cs);
check('the neutral accent matches the C#', csNeutral && csNeutral[1].toLowerCase() === NEUTRAL_ACCENT,
  `C# ${csNeutral && csNeutral[1]} vs JS ${NEUTRAL_ACCENT}`);

check('the soft-fill alpha matches the C#', csAlpha('CellFillSoft') === 0.25, String(csAlpha('CellFillSoft')));
check('the accent-bg alpha matches the C#', csAlpha('AccentBg') === 0.2, String(csAlpha('AccentBg')));
check('the text alpha matches the C#', csAlpha('Text') === 0.9, String(csAlpha('Text')));
check('the dim alpha matches the C#', csAlpha('TextDim') === 0.7, String(csAlpha('TextDim')));

// ── The dark palette reproduces the C#'s own strings ──
// #2f9450 is the value SkafinityTheme's comment calls out as reproducing the panel's original
// hardcoded green, so it is the accent worth checking against.
{
  const a = parseColor('#2f9450');
  const dark = derivePalette('#2f9450', 'dark');
  check('dark bg == Rgb(Scale(hue, 0.09))', dark['--_ska-bg'] === rgb(scale(a, fromCs.bg.f)), dark['--_ska-bg']);
  check('dark cell == Rgb(Scale(hue, 0.04))', dark['--_ska-cell'] === rgb(scale(a, fromCs.cell.f)), dark['--_ska-cell']);
  check('dark fill == Rgb(Scale(hue, 0.6))', dark['--_ska-fill'] === rgb(scale(a, fromCs.fill.f)), dark['--_ska-fill']);
  check('dark fill-soft == Rgba(Scale(hue, 0.6), 0.25)', dark['--_ska-fill-soft'] === rgba(scale(a, fromCs.fill.f), 0.25));
  check('dark accent-bg == Rgba(hue, 0.2)', dark['--_ska-accent-bg'] === rgba(a, 0.2));
  check('dark text == Rgba(Mix(hue, 0.8), 0.9)', dark['--_ska-text'] === rgba(mix(a, fromCs.text.f), 0.9), dark['--_ska-text']);
  check('dark text-dim == Rgba(Mix(hue, 0.72), 0.7)', dark['--_ska-text-dim'] === rgba(mix(a, fromCs.dim.f), 0.7));
}

// ── The edge tokens come from the panel's scss, reflected onto the mode's ink ──
{
  const scss = readFileSync(new URL('../sbox-library/Skafinity/Code/UI/SkafinityMusicPanel.razor.scss', import.meta.url), 'utf8');
  const scssAlpha = (name) => {
    const m = new RegExp(`\\$${name}:\\s*rgba\\(255,255,255,([0-9.]+)\\)`).exec(scss);
    return m ? parseFloat(m[1]) : null;
  };
  for (const name of Object.keys(EDGES)) {
    const a = scssAlpha(name);
    check(`edge alpha $${name} matches the panel scss`, a === EDGES[name], `scss ${a} vs JS ${EDGES[name]}`);
  }
  const dark = derivePalette('#2f9450', 'dark');
  const light = derivePalette('#2f9450', 'light');
  check('edges are white-over on dark', dark['--_ska-edge'] === `rgba(255,255,255,${EDGES.edge})`, dark['--_ska-edge']);
  check('edges are black-over on light', light['--_ska-edge'] === `rgba(0,0,0,${EDGES.edge})`, light['--_ska-edge']);
}

// ── The light half is the same factors reflected ──
{
  const a = parseColor('#2f9450');
  const light = derivePalette('#2f9450', 'light');
  check('light bg == Rgb(Mix(hue, 1 - 0.09))', light['--_ska-bg'] === rgb(mix(a, 1 - FACTORS.bg)), light['--_ska-bg']);
  check('light cell == Rgb(Mix(hue, 1 - 0.04))', light['--_ska-cell'] === rgb(mix(a, 1 - FACTORS.cell)));
  check('light text == Rgba(Scale(hue, 1 - 0.8), 0.9)', light['--_ska-text'] === rgba(scale(a, 1 - FACTORS.text), 0.9));
  check('the fill is Scale(0.6) in BOTH modes', light['--_ska-fill'] === derivePalette('#2f9450', 'dark')['--_ska-fill']);
  check('the accent itself is untouched by mode', light['--_ska-accent'] === rgb(a));
}

// ── The properties that matter for an embed ──
{
  // Whatever the accent, a light board must be lighter than its own ink and a dark board darker.
  const accents = ['#2f9450', '#0d6efd', '#ff5c2a', '#7a7a7a', '#ffffff', '#101010', '#f2e6c4'];
  let lightOk = true, darkOk = true, parsed = true;
  for (const hex of accents) {
    const L = derivePalette(hex, 'light'), D = derivePalette(hex, 'dark');
    for (const tok of Object.values(L).concat(Object.values(D))) if (!parseColor(tok)) parsed = false;
    if (luminance(parseColor(L['--_ska-bg'])) <= luminance(parseColor(L['--_ska-text']))) lightOk = false;
    if (luminance(parseColor(D['--_ska-bg'])) >= luminance(parseColor(D['--_ska-text']))) darkOk = false;
  }
  check('every derived token is a parseable colour', parsed);
  check('a light board is always lighter than its ink', lightOk);
  check('a dark board is always darker than its ink', darkOk);
  // A near-black accent gives invisible fills — the reason SkafinityTheme starts from a MID gray.
  check('the neutral accent is mid, not dark', luminance(parseColor(NEUTRAL_ACCENT)) > 0.1);
}

// ── Mode choice: the failure mode is a dark widget on a light page ──
check('white page -> light', chooseMode('#ffffff', true) === 'light');
check('near-white page -> light', chooseMode('rgb(248, 249, 250)', true) === 'light');
check('mid gray page -> light', chooseMode('#808080', true) === 'light', 'luminance 0.216 reads as a light surface');
check('the toy page -> dark', chooseMode('#010402', false) === 'dark');
check('a dark hand-rolled page -> dark', chooseMode('rgb(20,17,14)', false) === 'dark');
check('no background found -> the OS preference (dark)', chooseMode(null, true) === 'dark');
check('no background found -> the OS preference (light)', chooseMode(null, false) === 'light');
check('a transparent background is not evidence', chooseMode('rgba(0,0,0,0)', false) === 'light');

// ── parseColor, over what getComputedStyle actually returns ──
{
  const eq = (c, r, g, b, a = 1) => c && Math.abs(c.r - r) < 0.004 && Math.abs(c.g - g) < 0.004 &&
    Math.abs(c.b - b) < 0.004 && Math.abs(c.a - a) < 0.004;
  check('rgb()', eq(parseColor('rgb(13, 110, 253)'), 13 / 255, 110 / 255, 253 / 255));
  check('rgba() with alpha', eq(parseColor('rgba(0, 0, 0, 0.5)'), 0, 0, 0, 0.5));
  check('the space/slash form', eq(parseColor('rgb(13 110 253 / 50%)'), 13 / 255, 110 / 255, 253 / 255, 0.5));
  check('#rrggbb', eq(parseColor('#0d6efd'), 13 / 255, 110 / 255, 253 / 255));
  check('#rgb', eq(parseColor('#fff'), 1, 1, 1));
  check('a custom property keeps its leading space', eq(parseColor('  #ff5c2a  '), 1, 92 / 255, 42 / 255));
  check('transparent is null', parseColor('transparent') === null);
  check('an empty custom property is null', parseColor('') === null);
  check('a value we cannot evaluate is null, not a throw',
    parseColor('color-mix(in srgb, red, blue)') === null && parseColor('linear-gradient(red, blue)') === null);
}

// ── Accent priority ──
{
  const page = { '--accent': '#ff5c2a', '--bs-primary': '#0d6efd' };
  const lookup = (n) => page[n] || '';
  check('the more specific var wins', pickAccent(lookup).source === '--accent');
  page['--ska-accent'] = '#2f9450';
  check('--ska-accent beats everything', pickAccent(lookup).source === '--ska-accent');
  check('nothing found is null, not a throw', pickAccent(() => '') === null);
  check('an unparseable var is skipped, not fatal',
    pickAccent((n) => (n === '--ska-accent' ? 'var(--nope)' : n === '--accent' ? '#0d6efd' : '')).source === '--accent');
  check('a near-transparent accent is not an accent', pickAccent((n) => (n === '--accent' ? 'rgba(255,0,0,0.1)' : '')) === null);
}

console.log(failures ? `\n${failures} failure(s)` : '\nall palette checks passed');
process.exit(failures ? 1 : 0);
