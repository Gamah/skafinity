// Boots the .NET-wasm engine and adapts its [JSExport] surface to the small `mod` API the
// rest of the app expects — the same shape the old emcc module exposed, so app.js / worker.js
// only had to swap their import. The shared MusicGen.cs / VibeCodec.cs (compiled in wasm/)
// are the single source of truth; this file is glue, the analog of the old bindings.cpp's
// JS-facing half.
//
// Both the main thread and the generation worker import this and boot their own runtime
// instance (the runtime is single-realm). Heavy PCM/WAV stay in wasm memory and come back as
// a MemoryView we copy off-heap immediately (it's only valid synchronously).
import { dotnet } from '../build/_framework/dotnet.js';

let _mod = null;
let _booting = null;

function copyChannel(E, channel) {
  const u8 = E.ChannelBytes(channel).slice();           // off-heap copy of the float bytes
  return new Float32Array(u8.buffer, u8.byteOffset, u8.byteLength / 4);
}

// cfg crosses into wasm as a plain number array; normalize (it may arrive as a Float64Array
// from a previous export or a structured-clone across the worker boundary).
const asArray = (cfg) => (Array.isArray(cfg) ? cfg : Array.from(cfg));

function makeMod(E) {
  return {
    defaultConfig: () => E.DefaultConfig(),
    configSize: () => E.ConfigSize(),

    generateSong(seed, cfg) {
      const frames = E.GenerateSong(seed, asArray(cfg));
      return { sampleRate: E.SampleRate(), frames, left: copyChannel(E, 0), right: copyChannel(E, 1) };
    },

    songToWav(seed, cfg, stereo) {
      E.GenerateWav(seed, asArray(cfg), !!stereo);
      return E.WavBytes().slice();                       // Uint8Array, off-heap
    },

    encodeVibe: (cfg) => E.EncodeVibe(asArray(cfg)),
    decodeVibe: (vibe, cfg) => E.DecodeVibe(vibe || '', asArray(cfg)),
    looksLikeVibe: (s) => E.LooksLikeVibe(s || ''),
    vibeFieldCount: () => E.VibeFieldCount(),
    setVibeField: (cfg, i, norm) => E.SetVibeField(asArray(cfg), i, norm),
    getVibeNorm: (cfg, i) => E.GetVibeNorm(asArray(cfg), i),
    vibeDisplay: (cfg, i) => E.VibeDisplay(asArray(cfg), i),

    vibeFieldInfo: (i) => ({
      name: E.VibeFieldName(i),
      min: E.VibeFieldMin(i),
      max: E.VibeFieldMax(i),
      isInt: E.VibeFieldIsInt(i),
      choices: E.VibeFieldChoices(i),
    }),

    // Mirrors MusicController.PlaySeed parsing: vibe:tag:n | tag:n | tag.
    parseSeed(seedIn) {
      const seed = (seedIn || '').trim();
      const p = seed.length ? seed.split(':') : [''];
      const tryInt = (s) => (/^-?\d+$/.test(s) ? parseInt(s, 10) : null);
      let vibe = '', tag = '', n = 0, hasN = false;
      if (seed.length) {
        if (p.length >= 3) {
          vibe = p[0]; tag = p[1];
          const v = tryInt(p[2]); if (v !== null) { n = v; hasN = true; }
        } else if (p.length === 2) {
          const v = tryInt(p[1]);
          if (v !== null) { tag = p[0]; n = v; hasN = true; }
          else if (E.LooksLikeVibe(p[0])) { vibe = p[0]; tag = p[1]; }
          else tag = p[0];
        } else {
          tag = p[0];
        }
      }
      return { vibe, tag, n, hasN };
    },
  };
}

async function boot() {
  const { getAssemblyExports, getConfig } = await dotnet.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  return makeMod(exports.Engine);
}

// Default export keeps the old call site (`mod = await Skafinity()`) working unchanged.
export default function Skafinity() {
  if (_mod) return Promise.resolve(_mod);
  if (!_booting) _booting = boot().then((m) => (_mod = m));
  return _booting;
}
