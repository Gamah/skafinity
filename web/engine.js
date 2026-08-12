// Boots the .NET-wasm engine and adapts its [JSExport] surface to the small `mod` API the
// rest of the app expects — the same shape the old emcc module exposed, so app.js / worker.js
// only had to swap their import. The shared MusicGen.cs / VibeCodec.cs (compiled in wasm/)
// are the single source of truth; this file is glue, the analog of the old bindings.cpp's
// JS-facing half.
//
// Both the main thread and the generation worker import this and boot their own runtime
// instance (the runtime is single-realm). Heavy PCM/WAV stay in wasm memory and come back as
// a MemoryView we copy off-heap immediately (it's only valid synchronously).
import { dotnet } from './_framework/dotnet.js';

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
    // Seconds of ring-out a song reserves past its last bar — the window a crossfade may use.
    ringOutSeconds: () => E.RingOutSeconds(),

    generateSong(seed, cfg) {
      const frames = E.GenerateSong(seed, asArray(cfg));
      return { sampleRate: E.SampleRate(), frames, left: copyChannel(E, 0), right: copyChannel(E, 1) };
    },

    songToWav(seed, cfg) {
      E.GenerateWav(seed, asArray(cfg));                 // interleaved stereo
      return E.WavBytes().slice();                       // Uint8Array, off-heap
    },

    encodeVibe: (cfg) => E.EncodeVibe(asArray(cfg)),
    decodeVibe: (vibe, cfg) => E.DecodeVibe(vibe || '', asArray(cfg)),
    looksLikeVibe: (s) => E.LooksLikeVibe(s || ''),

    // Genre lives inside the vibe (first char); the field list depends on it.
    genreCount: () => E.GenreCount(),
    genreName: (i) => E.GenreName(i),
    getGenre: (cfg) => E.GetGenre(asArray(cfg)),
    setGenre: (cfg, i) => E.SetGenre(asArray(cfg), i),
    // Reroll lives in the engine (VibeCodec.Roll) so the web and s&box can't drift on what
    // it means. rollVibe = throwaway 🎲; rollVibeFor = the deterministic shuffle line.
    rollVibe: (cfg, includeGenre = true) => E.RollVibe(asArray(cfg), includeGenre),
    rollVibeFor: (cfg, tag, n) => E.RollVibeFor(asArray(cfg), tag, n),

    vibeFieldCount: (genre) => E.VibeFieldCount(genre),
    vibeLevels: () => E.VibeLevels(),
    setVibeField: (cfg, i, norm) => E.SetVibeField(asArray(cfg), i, norm),
    getVibeNorm: (cfg, i) => E.GetVibeNorm(asArray(cfg), i),
    vibeDisplay: (cfg, i) => E.VibeDisplay(asArray(cfg), i),

    vibeFieldInfo: (genre, i) => ({
      name: E.VibeFieldName(genre, i),
      min: E.VibeFieldMin(genre, i),
      max: E.VibeFieldMax(genre, i),
      isInt: E.VibeFieldIsInt(genre, i),
      voice: E.VibeFieldVoice(genre, i),
      column: E.VibeFieldColumn(genre, i),
      choices: E.VibeFieldChoices(genre, i),
    }),

    // Advanced / tuning-only fields (baseline mix; NOT vibe sliders). Addressed by name so a
    // host config.json can overlay them onto a cfg without knowing the double[] layout.
    advancedFieldCount: () => E.AdvancedFieldCount(),
    advancedFieldName: (i) => E.AdvancedFieldName(i),
    advancedFieldMin: (i) => E.AdvancedFieldMin(i),
    advancedFieldMax: (i) => E.AdvancedFieldMax(i),
    getAdvancedField: (cfg, i) => E.GetAdvancedField(asArray(cfg), i),
    setAdvancedField: (cfg, i, raw) => E.SetAdvancedField(asArray(cfg), i, raw),
    // Overlay { Name: rawValue } onto cfg (raw, clamped per field). Unknown keys are ignored.
    // Returns the new cfg.
    applyAdvancedConfig(cfg, obj) {
      if (!obj || typeof obj !== 'object') return cfg;
      const idx = {};
      for (let i = 0, n = E.AdvancedFieldCount(); i < n; i++) idx[E.AdvancedFieldName(i)] = i;
      let out = cfg;
      for (const [k, v] of Object.entries(obj)) {
        if (k in idx && typeof v === 'number' && Number.isFinite(v)) out = E.SetAdvancedField(asArray(out), idx[k], v);
      }
      return out;
    },

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

// Download progress. The runtime is ~7.5 MB and, since the element only fetches it on the first
// press of play, somebody is watching an empty box while it arrives — so the boot reports bytes.
//
// [SOURCE] read out of web/_framework/dotnet.js on 2026-08-12 — implementation detail of the
// loader, not a documented contract, so re-check it if a runtime bump breaks this:
//   * `withResourceLoader(fn)` installs `fn` as `loadBootResource`, called as
//     `fn(type, name, defaultUri, integrityHash, behavior)`;
//   * for `type === 'dotnetjs'` it MUST return a URL string (it asserts, then `import()`s it);
//   * anything else may return a `Promise<Response>`, which is used as-is.
// The two js modules therefore report no bytes (they are ~400 KB of the 7.5 MB); the five wasm
// assets are streamed here and counted.
//
// The TOTAL is not knowable up front — the boot config carries names and hashes but no sizes — so
// it is the sum of the Content-Lengths seen so far. The runtime kicks all the asset downloads off
// together, so in practice the total settles within one round trip; until it does, `total` is 0 and
// a caller should show something indeterminate rather than a bar at 100%.
//
// The integrity hash is passed straight through to fetch, so taking over the request does NOT drop
// the subresource-integrity check the default path would have done.
function progressLoader(onProgress) {
  let loaded = 0, total = 0, pending = 0;
  const report = (done = false) => onProgress({ loaded, total, ratio: total > 0 ? Math.min(1, loaded / total) : 0, done });
  return (type, name, defaultUri, integrity) => {
    if (type === 'dotnetjs') return defaultUri;
    // Only http(s) is ours to stream. Returning undefined hands the asset back to the runtime's own
    // default path, which is what has to happen for the boots that are not a browser over a server:
    // node (test/page.mjs, test/smoke.mjs) resolves assets off the filesystem, and `fetch` there
    // rejects a relative URL outright.
    if (!/^https?:/i.test(String(defaultUri))) return undefined;
    pending++;
    return (async () => {
      const res = await fetch(defaultUri, integrity ? { integrity, cache: 'force-cache' } : { cache: 'force-cache' });
      const len = parseInt(res.headers.get('content-length') || '', 10);
      if (Number.isFinite(len)) { total += len; report(); }
      if (!res.body || !res.ok) return res;   // no stream to read (or an error the runtime handles)
      const reader = res.body.getReader();
      const chunks = [];
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
        loaded += value.byteLength;
        // A compressed transfer (nginx serving the .br twin) reports the COMPRESSED length while
        // the reader yields decompressed bytes, so loaded can outrun total. Grow the total rather
        // than showing 140%.
        if (loaded > total) total = loaded;
        report();
      }
      if (--pending === 0) report(true);
      return new Response(new Blob(chunks), { status: res.status, headers: res.headers });
    })();
  };
}

async function boot(onProgress) {
  const builder = onProgress ? dotnet.withResourceLoader(progressLoader(onProgress)) : dotnet;
  const { getAssemblyExports, getConfig } = await builder.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  if (onProgress) onProgress({ loaded: 1, total: 1, ratio: 1, done: true });
  return makeMod(exports.Engine);
}

// Default export keeps the old call site (`mod = await Skafinity()`) working unchanged; the options
// bag is optional and only the FIRST caller's is honoured, since the runtime boots once per realm.
export default function Skafinity(opts) {
  if (_mod) return Promise.resolve(_mod);
  // `withResourceLoader` is a builder method the single-file bundle's shim does not have — there,
  // every asset is already in memory and there is nothing to report progress on.
  const onProgress = opts && opts.onProgress && dotnet.withResourceLoader ? opts.onProgress : null;
  if (!_booting) _booting = boot(onProgress).then((m) => (_mod = m));
  return _booting;
}
