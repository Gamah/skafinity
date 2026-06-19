// Node smoke test for the .NET-wasm engine boundary. Boots the published runtime under node
// (the same dotnet.js that runs in the browser) and exercises every JSExport the web layer
// calls — so a boundary regression is caught by `make test`, without needing a browser.
//
//   make            # publish the engine into web/_framework
//   node test/smoke.mjs
import { fileURLToPath } from 'node:url';
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

// ── config / vibe round-trip ──
const cfg = E.DefaultConfig();
check('ConfigSize matches DefaultConfig length', cfg.length === E.ConfigSize(), `${cfg.length}`);

// ── genre ──
check('GenreCount is 4', E.GenreCount() === 4, `${E.GenreCount()}`);
check('genre 0 is Ska', E.GenreName(0) === 'Ska', E.GenreName(0));
check('genre 1 is Rock', E.GenreName(1) === 'Rock', E.GenreName(1));
check('genre 2 is Country', E.GenreName(2) === 'Country', E.GenreName(2));
check('genre 3 is Metal', E.GenreName(3) === 'Metal', E.GenreName(3));
check('DefaultConfig genre is 0', E.GetGenre(cfg) === 0, `${E.GetGenre(cfg)}`);

// ska (genre 0): 7 globals + 6 instruments × 4 columns
const skaCount = E.VibeFieldCount(0);
check('ska VibeFieldCount is 31', skaCount === 31, `${skaCount}`);
// rock (genre 1): 7 globals + 5 instruments × 4 columns (drums/bass/keys/lead gtr/rhythm gtr)
const rockCount = E.VibeFieldCount(1);
check('rock VibeFieldCount is 27', rockCount === 27, `${rockCount}`);
// country (genre 2): 7 globals + 5 instruments × 4 columns (drums/bass/rhythm gtr/keys/lead gtr)
check('country VibeFieldCount is 27', E.VibeFieldCount(2) === 27, `${E.VibeFieldCount(2)}`);
// metal (genre 3): 7 globals + 4 instruments × 4 columns (drums/bass/rhythm gtr/lead gtr)
check('metal VibeFieldCount is 23', E.VibeFieldCount(3) === 23, `${E.VibeFieldCount(3)}`);

const vibe = E.EncodeVibe(cfg);
check('ska vibe length == fields + genre char', vibe.length === skaCount + 1, `${vibe.length}`);
check('Encode(Decode(vibe)) is stable', E.EncodeVibe(E.DecodeVibe(vibe, cfg)) === vibe);
check('LooksLikeVibe accepts the encoding', E.LooksLikeVibe(vibe) === true);
check('LooksLikeVibe rejects a short tag', E.LooksLikeVibe('gamah') === false);
check('vibe starts with genre 0 char', vibe[0] === '0', vibe);

// rock vibe is genre-tagged + shorter, and round-trips its own genre
const rockCfg = E.SetGenre(cfg, 1);
const rockVibe = E.EncodeVibe(rockCfg);
check('rock vibe length == fields + genre char', rockVibe.length === rockCount + 1, `${rockVibe.length}`);
check('rock vibe is shorter than ska', rockVibe.length < vibe.length);
check('rock vibe starts with genre 1 char', rockVibe[0] === '1', rockVibe);
check('decoding a rock vibe restores genre 1', E.GetGenre(E.DecodeVibe(rockVibe, cfg)) === 1);
check('rock Encode(Decode) is stable', E.EncodeVibe(E.DecodeVibe(rockVibe, cfg)) === rockVibe);

// fields carry voice/column metadata for the UI matrix
check('a field reports a voice', (() => { for (let i = 0; i < skaCount; i++) if (E.VibeFieldVoice(0, i) === 'DRUMS') return true; })() === true);

// move a knob, confirm it round-trips through the vibe string
const tempoMin = (() => { for (let i = 0; i < skaCount; i++) if (E.VibeFieldName(0, i) === 'TEMPO MIN') return i; })();
const cfg2 = E.SetVibeField(cfg, tempoMin, 0.25);
const norm = E.GetVibeNorm(cfg2, tempoMin);
check('SetVibeField/GetVibeNorm round-trip', Math.abs(norm - 0.25) < 0.04, `${norm}`);
check('VibeDisplay renders the value', /^\d+$/.test(E.VibeDisplay(cfg2, tempoMin)), E.VibeDisplay(cfg2, tempoMin));

// ── generation ──
const frames = E.GenerateSong('gamah:0', cfg);
check('GenerateSong returns a sane frame count', frames > 1_000_000 && frames < 5_000_000, `${frames}`);
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
E.GenerateSong('gamah:0', rockCfg);
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
