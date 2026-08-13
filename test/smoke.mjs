// Node smoke test for the .NET-wasm engine boundary. Boots the published runtime under node
// (the same dotnet.js that runs in the browser) and exercises every JSExport the web layer
// calls — so a boundary regression is caught by `make test`, without needing a browser.
//
//   make            # publish the engine into web/_framework
//   node test/smoke.mjs
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';
import { dotnet } from '../web/_framework/dotnet.js';

let failures = 0;
function check(name, cond, detail = '') {
  console.log(`${cond ? 'ok  ' : 'FAIL'}  ${name}${detail ? '  — ' + detail : ''}`);
  if (!cond) failures++;
}

const { getAssemblyExports, getConfig } = await dotnet.create();
const E = (await getAssemblyExports(getConfig().mainAssemblyName)).Engine;

function floatChannel(channel) {
  const u8 = E.ChannelBytes(channel).slice();      // off-heap copy
  return new Float32Array(u8.buffer, u8.byteOffset, u8.byteLength / 4);
}

// ── glue vs bundle ──
// web/_framework is COMMITTED, so a checkout is runnable without the SDK — which means every
// commit has to keep the glue and the bundle in step. Adding a [JSExport] and forgetting to
// re-publish leaves engine.js calling a function the bundle doesn't have, and the page dies at
// boot with "E.Foo is not a function". Check every export the glue actually calls, so that
// mismatch fails here instead of in a browser.
const glue = readFileSync(new URL('../web/engine.js', import.meta.url), 'utf8');
const called = [...new Set([...glue.matchAll(/\bE\.([A-Za-z_]\w*)\s*\(/g)].map((m) => m[1]))].sort();
check('engine.js calls at least one export', called.length > 0, `${called.length} found`);
const missing = called.filter((nm) => typeof E[nm] !== 'function');
check('every export engine.js calls exists in the bundle', missing.length === 0,
  missing.length ? `MISSING: ${missing.join(', ')} — re-publish (make) and re-stage web/_framework` : `${called.length} checked`);

// ── config / vibe round-trip ──
const cfg = E.DefaultConfig();
check('ConfigSize matches DefaultConfig length', cfg.length === E.ConfigSize(), `${cfg.length}`);

// ── genre ──
// Derived from the engine rather than hardcoded: adding a genre is an append-only change to
// VibeCodec, and this boundary test should follow it instead of having to be edited in step.
const GENRES = ['Ska-Punk', 'Rock', 'Country', 'Metal', 'Punk', 'Pop'];
check('GenreCount matches the known genre list', E.GenreCount() === GENRES.length,
  `${E.GenreCount()} vs ${GENRES.length}`);
for (let g = 0; g < E.GenreCount(); g++) {
  const want = GENRES[g];
  check(`genre ${g} is ${want ?? '(unnamed — add it to GENRES)'}`,
    E.GenreName(g) === want, E.GenreName(g));
}
check('DefaultConfig genre is 0', E.GetGenre(cfg) === 0, `${E.GetGenre(cfg)}`);

// Field counts follow the genre grids: each genre shows a volume plus its exposed cells, per
// instrument row. Asserted as a SHAPE — adding a knob should not mean editing a literal here.
const rowsOf = (g) => {
  let rows = 0;
  for (let i = 0; i < E.VibeFieldCount(g); i++) if (E.VibeFieldVoice(g, i) && E.VibeFieldColumn(g, i) === 0) rows++;
  return rows;
};
check('every genre shows at least a kit and a bass', (() => {
  for (let g = 0; g < E.GenreCount(); g++) if (rowsOf(g) < 2) return false;
  return true;
})());
check('every knob belongs to an instrument row', (() => {
  for (let g = 0; g < E.GenreCount(); g++)
    for (let i = 0; i < E.VibeFieldCount(g); i++) if (!E.VibeFieldVoice(g, i)) return false;
  return true;
})());

// ── The wire is ONE GLOBAL GRID ──
// The whole seed refactor rests on this: a vibe is the same width in every genre, and means the
// same thing in every genre. Without it, pinning a vibe and letting the genre roll is nonsense.
const vibe = E.EncodeVibe(cfg);
check('a vibe is the length the engine says it is', vibe.length === E.VibeLength(), `${vibe.length}`);
check('a vibe is hex', /^[0-9a-f]+$/.test(vibe), vibe);
for (let g = 1; g < E.GenreCount(); g++)
  check(`genre ${g} encodes the same width`,
    E.EncodeVibe(E.SetGenre(cfg, g)).length === vibe.length, `${E.EncodeVibe(E.SetGenre(cfg, g)).length}`);
check('Encode(Decode(vibe)) is stable', E.EncodeVibe(E.DecodeVibe(vibe, cfg)) === vibe);
check('IsVibe accepts the encoding', E.IsVibe(vibe) === true);
check('IsVibe rejects a station name', E.IsVibe('gamah') === false);
check('IsVibe rejects a near-miss width', E.IsVibe(vibe.slice(1)) === false);
// A rolled vibe is an ordinary vibe — that is what lets the UI pin one into a seed.
const rolled = E.RollVibeFor('gamah', 3);
check('a rolled vibe is a vibe', E.IsVibe(rolled) === true, rolled);
check('a rolled vibe survives every genre', (() => {
  for (let g = 0; g < E.GenreCount(); g++)
    if (E.EncodeVibe(E.DecodeVibe(rolled, E.SetGenre(cfg, g))) !== rolled) return false;
  return true;
})(), rolled);
check('a vibe carries no genre of its own',
  E.GetGenre(E.DecodeVibe(rolled, E.SetGenre(cfg, 4))) === 4);

// A listener's levels are a local mix preference: moving one must leave the shared string alone.
{
  let loud = cfg;
  for (let i = 0; i < E.VibeFieldCount(0); i++)
    if (E.VibeFieldVoice(0, i) && E.VibeFieldColumn(0, i) === 0) loud = E.SetVibeField(loud, i, 0.1);
  check('volumes never travel in the seed', E.EncodeVibe(loud) === vibe, E.EncodeVibe(loud));
}

// ── The seed string ──
// Parsed by the engine and nowhere else, so this is the contract the whole web layer sits on.
{
  const [err, tag, n, genre, v] = E.ParseSeed(`gamah:23:3:${rolled}`);
  check('a full seed parses', !err && tag === 'gamah' && n === '23' && genre === '3' && v === rolled,
    `${err} ${tag} ${n} ${genre} ${v}`);
  const [e2, , , g2, v2] = E.ParseSeed('gamah:23');
  check('a bare station leaves both parts rolling', !e2 && g2 === '-1' && v2 === '', `${g2} ${v2}`);
  const [e3] = E.ParseSeed('gamah:oops');
  check('a malformed seed comes back as a sentence', !!e3, e3);
  check('…and says something about the number', /number/.test(e3), e3);
  check('a formatted seed round-trips', E.FormatSeed('gamah', 23, 3, rolled) === `gamah:23:3:${rolled}`,
    E.FormatSeed('gamah', 23, 3, rolled));
  check('rolled parts are simply not written down', E.FormatSeed('gamah', 23, -1, '') === 'gamah:23',
    E.FormatSeed('gamah', 23, -1, ''));
  check('the composer stream is the station and the index',
    E.SongSeed('Gamah', 7) === 'gamah:7', E.SongSeed('Gamah', 7));
  check('genre and vibe roll off separate streams', (() => {
    // Pinning cannot move the other line, because neither is derived from the other.
    for (let k = 0; k < 8; k++) if (E.RollVibeFor('x', k) === E.RollVibeFor('y', k)) return false;
    return true;
  })());
  check('a shuffled line starts on its root', E.RollTagFor('gamah', 0) === 'gamah');
  check('…and derives every station after it',
    E.RollTagFor('gamah', 1) !== 'gamah' && E.RollTagFor('gamah', 1) === E.RollTagFor('gamah', 1));
}

// fields carry voice/column metadata for the UI matrix
check('a field reports a voice', (() => { for (let i = 0; i < E.VibeFieldCount(0); i++) if (E.VibeFieldVoice(0, i) === 'DRUMS') return true; })() === true);

// Move a knob and confirm it round-trips through the vibe string. Picked BY SHAPE rather than by
// name — the first continuous knob, global or not — because naming one couples this test to that
// knob surviving. It was `TEMPO`, which is now a reserved slot; and it was then "the first
// continuous GLOBAL", which stopped existing when the last global was retired.
const knob = (() => {
  for (let i = 0; i < E.VibeFieldCount(0); i++)
    if (!E.VibeFieldIsInt(0, i) && E.VibeFieldChoices(0, i).length === 0) return i;
})();
check('there is a continuous knob to test', knob !== undefined);
const cfg2 = E.SetVibeField(cfg, knob, 0.25);
const norm = E.GetVibeNorm(cfg2, knob);
check('SetVibeField/GetVibeNorm round-trip', Math.abs(norm - 0.25) < 0.04, `${norm}`);
check('VibeDisplay renders a value', E.VibeDisplay(cfg2, knob).length > 0, E.VibeDisplay(cfg2, knob));

// ── generation ──
// A song's LENGTH is drawn now (GenreProfile.Forms + the details over them), so this is a
// runaway guard rather than a description: a form is 32-112 bars and the slowest genre's band
// bottoms out around 95 bpm, which is ~4.5 minutes at 44.1 kHz. What it still catches is a form
// that lost its bounds or a time base that lost its tempo — not a particular song length.
const frames = E.GenerateSong('gamah:0', cfg);
check('GenerateSong returns a sane frame count', frames > 1_000_000 && frames < 13_000_000, `${frames}`);
check('SampleRate is 44100', E.SampleRate() === 44100, `${E.SampleRate()}`);

const L = floatChannel(0), R = floatChannel(1);
check('channel lengths match frame count', L.length === frames && R.length === frames);
let peak = 0, nonzero = 0;
for (let i = 0; i < L.length; i++) { const a = Math.abs(L[i]); if (a > peak) peak = a; if (a > 1e-4) nonzero++; }
check('audio is non-silent', nonzero > frames / 10, `${nonzero} loud frames`);
check('audio peak is normalized (<=1)', peak > 0.5 && peak <= 1.001, `${peak.toFixed(3)}`);

// determinism: same seed → identical first samples
E.GenerateSong('gamah:0', cfg);
const L2 = floatChannel(0);
let same = true;
for (let i = 0; i < 2000; i++) if (L2[i] !== L[i]) { same = false; break; }
check('same seed is deterministic', same);

// different seed → different audio
E.GenerateSong('gamah:1', cfg);
const L3 = floatChannel(0);
let diff = false;
for (let i = 0; i < 200000; i++) if (Math.abs(L3[i] - L[i]) > 1e-6) { diff = true; break; }
check('different seed differs', diff);

// rock genre renders non-silent audio that differs from ska (same seed, different genre)
E.GenerateSong('gamah:0', E.SetGenre(cfg, 1));
const Rk = floatChannel(0);
let rockNonzero = 0, rockDiff = false;
for (let i = 0; i < Rk.length; i++) { if (Math.abs(Rk[i]) > 1e-4) rockNonzero++; }
for (let i = 0; i < 200000; i++) if (Math.abs(Rk[i] - L[i]) > 1e-6) { rockDiff = true; break; }
check('rock audio is non-silent', rockNonzero > Rk.length / 10, `${rockNonzero} loud frames`);
check('rock differs from ska at same seed', rockDiff);

// country + metal genres render non-silent audio that differs from ska (same seed)
for (const [g, name] of [[2, 'country'], [3, 'metal']]) {
  E.GenerateSong('gamah:0', E.SetGenre(cfg, g));
  const ch = floatChannel(0);
  let nonzero = 0, differs = false;
  for (let i = 0; i < ch.length; i++) if (Math.abs(ch[i]) > 1e-4) nonzero++;
  for (let i = 0; i < 200000; i++) if (Math.abs(ch[i] - L[i]) > 1e-6) { differs = true; break; }
  check(`${name} audio is non-silent`, nonzero > ch.length / 10, `${nonzero} loud frames`);
  check(`${name} differs from ska at same seed`, differs);
}

// ── WAV ──
const wavLen = E.GenerateWav('gamah:0', cfg);
const wav = E.WavBytes().slice();
check('WAV length matches', wav.length === wavLen && wavLen > 44);
check('WAV has RIFF/WAVE header',
  String.fromCharCode(wav[0], wav[1], wav[2], wav[3]) === 'RIFF' &&
  String.fromCharCode(wav[8], wav[9], wav[10], wav[11]) === 'WAVE');
check('WAV is stereo', wav[22] === 2);    // fmt chunk numChannels (LE) at byte 22

console.log(failures ? `\n${failures} FAILURE(S)` : '\nall good');
process.exit(failures ? 1 : 0);
