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
// The transport and the element are where the mod.* calls live now that app.js is only a host for
// <skafinity-player>; app.js and worker.js stay in the list because either may grow one back.
const pageSrc = ['app.js', 'worker.js', 'player.js', 'skafinity-element.js']
  .map((f) => readFileSync(new URL('../web/' + f, import.meta.url), 'utf8'))
  .join('\n');
const used = [...new Set([...pageSrc.matchAll(/\bmod\.([A-Za-z_]\w*)\s*\(/g)].map((m) => m[1]))].sort();
check('the page calls into the engine at all', used.length > 0, `${used.length} names`);
const missing = used.filter((n) => typeof mod[n] !== 'function');
check('every engine call the page makes resolves', missing.length === 0,
  missing.length ? `MISSING: ${missing.join(', ')} — engine.js and the bundle are out of step` : `${used.length} checked`);

// ── seed parsing (the URL hash is the whole product) ──
// The adapter's job is to hand the page the engine's answer unchanged — including the refusal,
// which is the half a page has to be able to show.
let cfg = mod.defaultConfig();
const aVibe = mod.rollVibeFor('gamah', 5);
for (const [seed, wantTag, wantN] of [[`gamah:7:3:${aVibe}`, 'gamah', 7], ['gamah:3', 'gamah', 3], ['gamah', 'gamah', 0]]) {
  const p = mod.parseSeed(seed);
  check(`parseSeed("${seed}") reads tag/n`, !p.error && p.tag === wantTag && p.n === wantN, JSON.stringify(p));
}
check('parseSeed pins what the seed wrote down',
  mod.parseSeed(`gamah:7:3:${aVibe}`).genre === 3 && mod.parseSeed(`gamah:7:3:${aVibe}`).vibe === aVibe);
check('…and leaves out what it did not',
  mod.parseSeed('gamah:7').genre === null && mod.parseSeed('gamah:7').vibe === '');
check('a malformed seed comes back as a sentence, not a song',
  !!mod.parseSeed('gamah:nope').error, mod.parseSeed('gamah:nope').error);
check('formatSeed is parseSeed backwards',
  mod.formatSeed('gamah', 7, 3, aVibe) === `gamah:7:3:${aVibe}`, mod.formatSeed('gamah', 7, 3, aVibe));
check('a null genre is left to roll rather than written as -1',
  mod.formatSeed('gamah', 7, null, '') === 'gamah:7', mod.formatSeed('gamah', 7, null, ''));

// ── reroll: the 🎲 button ──
check('rollVibe hands back a vibe', mod.isVibe(mod.rollVibe()) === true, mod.rollVibe());
check('rollVibe actually rolls', mod.rollVibe() !== mod.rollVibe());

// ── the lines every listener walks ──
check('rollVibeFor is reproducible for the same tag+n', mod.rollVibeFor('gamah', 5) === aVibe, aVibe);
check('rollVibeFor moves with n', aVibe !== mod.rollVibeFor('gamah', 6));
check('rollVibeFor moves with the tag', aVibe !== mod.rollVibeFor('skafinity', 5));
check('rollVibeFor ignores tag case', aVibe === mod.rollVibeFor('GAMAH', 5));
const lineGenres = new Set();
for (let i = 0; i < 60; i++) lineGenres.add(mod.rollGenreFor('gamah', i));
check('the genre line covers every genre', lineGenres.size === mod.genreCount(),
  `${lineGenres.size} of ${mod.genreCount()}`);
check('the station line starts on its root and derives the rest',
  mod.rollTagFor('gamah', 0) === 'gamah' && mod.rollTagFor('gamah', 1) !== 'gamah');

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

// ── double-tracking width stays a width, not a tuning error ──
// WidthDetune is house config with no listener-facing undo, so its RANGE is the safety rail:
// a few cents between two takes reads as two performances, tens of cents reads as out of tune.
let wdIdx = -1;
for (let i = 0, n = mod.advancedFieldCount(); i < n; i++)
  if (mod.advancedFieldName(i) === 'WidthDetune') wdIdx = i;
check('WidthDetune is an advanced (config-only) field', wdIdx >= 0);
check('WidthDetune cannot be pushed past a musical width',
  mod.advancedFieldMax(wdIdx) <= 20, `max ${mod.advancedFieldMax(wdIdx)} cents`);
check('WidthDetune round-trips through cfg',
  mod.getAdvancedField(mod.setAdvancedField(cfg, wdIdx, 8), wdIdx) === 8);

// ── a rolled song actually renders ──
// The end of the whole chain: shuffle picks a vibe, the worker renders it, the page plays it.
const song = mod.generateSong(mod.songSeed('gamah', 5), mod.decodeVibe(aVibe, cfg));
check('a shuffled song renders stereo audio',
  song && song.left?.length > 0 && song.left.length === song.right?.length, `${song?.left?.length} frames`);
let loud = 0;
for (let i = 0; i < song.left.length; i++) if (Math.abs(song.left[i]) > 1e-4) loud++;
check('a shuffled song is not silence', loud > song.left.length / 10, `${loud} loud frames`);
check('a shuffled song reports its sample rate', song.sampleRate === 44100, `${song.sampleRate}`);

console.log(failures ? `\n${failures} FAILURE(S)` : '\nall good');
process.exit(failures ? 1 : 0);
