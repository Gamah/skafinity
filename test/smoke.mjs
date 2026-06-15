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

const fieldCount = E.VibeFieldCount();
check('VibeFieldCount is 30', fieldCount === 30, `${fieldCount}`);

const vibe = E.EncodeVibe(cfg);
check('encode length == field count', vibe.length === fieldCount, vibe);
check('Encode(Decode(vibe)) is stable', E.EncodeVibe(E.DecodeVibe(vibe, cfg)) === vibe);
check('LooksLikeVibe accepts the encoding', E.LooksLikeVibe(vibe) === true);
check('LooksLikeVibe rejects a short tag', E.LooksLikeVibe('gamah') === false);

// move a knob, confirm it round-trips through the vibe string
const tempoMin = (() => { for (let i = 0; i < fieldCount; i++) if (E.VibeFieldName(i) === 'TEMPO MIN') return i; })();
const cfg2 = E.SetVibeField(cfg, tempoMin, 0.25);
const norm = E.GetVibeNorm(cfg2, tempoMin);
check('SetVibeField/GetVibeNorm round-trip', Math.abs(norm - 0.25) < 0.04, `${norm}`);
check('VibeDisplay renders the value', /^\d+$/.test(E.VibeDisplay(cfg2, tempoMin)), E.VibeDisplay(cfg2, tempoMin));
check('LEAD INSTR exposes choices', E.VibeFieldChoices((() => { for (let i = 0; i < fieldCount; i++) if (E.VibeFieldName(i) === 'LEAD INSTR') return i; })()).length === 5);

// ── generation ──
const frames = E.GenerateSong('gamah:0', cfg);
check('GenerateSong returns a sane frame count', frames > 1_000_000 && frames < 5_000_000, `${frames}`);
check('SampleRate is 32000', E.SampleRate() === 32000, `${E.SampleRate()}`);

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

// ── WAV ──
const wavLen = E.GenerateWav('gamah:0', cfg, true);
const wav = E.WavBytes().slice();
check('WAV length matches', wav.length === wavLen && wavLen > 44);
check('WAV has RIFF/WAVE header',
  String.fromCharCode(wav[0], wav[1], wav[2], wav[3]) === 'RIFF' &&
  String.fromCharCode(wav[8], wav[9], wav[10], wav[11]) === 'WAVE');

console.log(failures ? `\n${failures} FAILURE(S)` : '\nall good');
process.exit(failures ? 1 : 0);
