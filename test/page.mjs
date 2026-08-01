// Does the PAGE's contract with the engine hold?
//
// smoke.mjs checks the raw [JSExport] boundary. This checks the layer the page actually talks
// to: the `mod` object web/engine.js hands back, and the calls web/app.js + web/worker.js make
// against it. A missing name here is a page that dies at boot — the browser's only symptom is
// "mod.foo is not a function", which no amount of engine testing catches.
//
// Separate file rather than part of smoke.mjs because booting engine.js starts its own .NET
// runtime; two in one process is asking for trouble.
//
//   node test/page.mjs        (or `make test`, which runs both)
import { readFileSync } from 'node:fs';
import Skafinity from '../web/engine.js';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}

const mod = await Skafinity();

// ── the surface the page relies on ──
// Derived from the page source, not hand-listed: adding a mod.* call to app.js is then
// automatically covered, which is exactly the gap that let a missing export ship.
const pageSrc = ['app.js', 'worker.js']
  .map((f) => readFileSync(new URL('../web/' + f, import.meta.url), 'utf8'))
  .join('\n');
const used = [...new Set([...pageSrc.matchAll(/\bmod\.([A-Za-z_]\w*)\s*\(/g)].map((m) => m[1]))].sort();
check('the page calls into the engine at all', used.length > 0, `${used.length} names`);
const missing = used.filter((n) => typeof mod[n] !== 'function');
check('every engine call the page makes resolves', missing.length === 0,
  missing.length ? `MISSING: ${missing.join(', ')} — engine.js and the bundle are out of step` : `${used.length} checked`);

// ── seed parsing (the URL hash is the whole product) ──
for (const [seed, wantTag, wantN] of [['vibe:gamah:7', 'gamah', 7], ['gamah:3', 'gamah', 3], ['gamah', 'gamah', 0]]) {
  const p = mod.parseSeed(seed);
  check(`parseSeed("${seed}") reads tag/n`, p.tag === wantTag && p.n === wantN, JSON.stringify(p));
}

// ── reroll: the 🎲 button ──
let cfg = mod.defaultConfig();
const base = mod.encodeVibe(cfg);
const rolled = mod.rollVibe(cfg, true);
check('rollVibe changes the vibe', mod.encodeVibe(rolled) !== base);
const genresSeen = new Set();
for (let i = 0; i < 60; i++) genresSeen.add(mod.getGenre(mod.rollVibe(cfg, true)));
check('rollVibe reaches more than one genre', genresSeen.size > 1, `${genresSeen.size} genres`);
check('rollVibe(false) keeps the genre',
  mod.getGenre(mod.rollVibe(mod.setGenre(cfg, 3), false)) === 3);

// ── shuffle: the line every listener walks ──
const a = mod.encodeVibe(mod.rollVibeFor(cfg, 'gamah', 5));
const b = mod.encodeVibe(mod.rollVibeFor(cfg, 'gamah', 5));
check('rollVibeFor is reproducible for the same tag+n', a === b, a);
check('rollVibeFor moves with n', a !== mod.encodeVibe(mod.rollVibeFor(cfg, 'gamah', 6)));
check('rollVibeFor moves with the tag', a !== mod.encodeVibe(mod.rollVibeFor(cfg, 'skafinity', 5)));
check('rollVibeFor ignores tag case',
  a === mod.encodeVibe(mod.rollVibeFor(cfg, 'GAMAH', 5)));
const lineGenres = new Set();
for (let i = 0; i < 60; i++) lineGenres.add(mod.getGenre(mod.rollVibeFor(cfg, 'gamah', i)));
check('the shuffle line covers every genre', lineGenres.size === mod.genreCount(),
  `${lineGenres.size} of ${mod.genreCount()}`);

// ── retired knobs are gone from what the UI renders ──
// A retired global leaves a RESERVED (null) slot on the wire so every later position holds; what
// must not survive is the slider. SWING became per-genre character; RESONANCE set a Config field
// no voice ever read, so it was inert.
const retired = ['SWING', 'RESONANCE'];
const seen = new Set();
for (let g = 0; g < mod.genreCount(); g++)
  for (let i = 0; i < mod.vibeFieldCount(g); i++)
    seen.add(mod.vibeFieldInfo(g, i).name);
for (const name of retired)
  check(`${name} is not a knob the UI would render`, !seen.has(name));

// ── the displacement toggle the page puts next to the genre selector ──
// It is an ADVANCED field (config-only, never in the seed) and it is discrete, so the page builds
// its dropdown from the engine's own labels rather than a second table in JS.
let displaceIdx = -1;
for (let i = 0, n = mod.advancedFieldCount(); i < n; i++)
  if (mod.advancedFieldName(i) === 'DisplaceMode') displaceIdx = i;
check('DisplaceMode is an advanced (config-only) field', displaceIdx >= 0);
const displaceChoices = displaceIdx >= 0 ? mod.advancedFieldChoices(displaceIdx) : [];
check('DisplaceMode offers the page a set of labels to render',
  displaceChoices.length === 3, displaceChoices.join(' | '));
check('DisplaceMode round-trips through cfg',
  mod.getAdvancedField(mod.setAdvancedField(cfg, displaceIdx, 2), displaceIdx) === 2);
check('a continuous advanced field offers no choices',
  mod.advancedFieldChoices(0).length === 0);

// ── a rolled song actually renders ──
// The end of the whole chain: shuffle picks a vibe, the worker renders it, the page plays it.
const song = mod.generateSong('gamah:5', mod.rollVibeFor(cfg, 'gamah', 5));
check('a shuffled song renders stereo audio',
  song && song.left?.length > 0 && song.left.length === song.right?.length, `${song?.left?.length} frames`);
let loud = 0;
for (let i = 0; i < song.left.length; i++) if (Math.abs(song.left[i]) > 1e-4) loud++;
check('a shuffled song is not silence', loud > song.left.length / 10, `${loud} loud frames`);
check('a shuffled song reports its sample rate', song.sampleRate === 44100, `${song.sampleRate}`);

console.log(failures ? `\n${failures} FAILURE(S)` : '\nall good');
process.exit(failures ? 1 : 0);
