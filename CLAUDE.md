# CLAUDE.md — skafinity

**skafinity** = *ska* + *infinity*. A web toy that streams an **endless, deterministic
procedural ska / reggae-rock track** generated entirely in the browser from a short
shareable seed. No server, no audio assets — the music is synthesised from scratch in
WebAssembly and scheduled through the Web Audio API.

Read this file fully before making changes.

---

## Origin & the single source of truth

The generator comes from the **Rotaliate s&box client** procedural-music engine. It now
lives here as a standalone s&box library under `sbox-library/Skafinity/` — and **that C# is
the single source of truth for both the game and this web toy**. The web build compiles the
*same* files to WebAssembly; there is no separate port to keep in sync.

| File | Role |
|---|---|
| `sbox-library/Skafinity/Code/MusicGen.cs` | The composer + subtractive synthesiser. Portable PRNG → every musical choice → interleaved stereo PCM. **The spec.** |
| `sbox-library/Skafinity/Code/VibeCodec.cs` | Base-36 encoding of the "vibe" knobs → the shareable seed fragment. Also holds the `AdvancedFields` registry — the baseline-mix knobs that are config-only (NOT in the seed or the sliders). |
| `sbox-library/Skafinity/skafinity.config.json` | The single shared **house-mix config** (peak balances / kit presence). Canonical here; the s&box plugin reads it at runtime and `make` copies it to `web/config.json`. Edit it to retune the baseline mix without a rebuild. |
| `sbox-library/Skafinity/Code/SkafinityPlayer.cs` | The s&box playback driver (`SoundStream`, infinite `tag:n`, look-ahead, crossfade). Web equivalent is `web/app.js`; the s&box-only bits are not used on the web. |
| `sbox-library/Skafinity/Code/UI/SkafinityMusicPanel.razor` (`.scss`) | Optional drop-in Razor `PanelComponent` — finds a `SkafinityPlayer` and exposes its knobs as in-game UI (seed/prev-next, genre, per-instrument vibe mixer, mute/volume, reroll, save). s&box-only; not in the web build. Re-themeable via the `.scss` variable block. |
| `reference/*.cs` | The original Rotaliate-client copies, kept for context. **Read-only.** The `sbox-library` copies are what actually compile. |

These two `.cs` are framework-free (only `System` / `System.Collections.Generic` /
`System.Text`) — that's *why* they compile to wasm unchanged. Keep them that way: **no
s&box (`Sandbox.*`) types and no web/Emscripten-isms in `MusicGen.cs` / `VibeCodec.cs`.**
Anything web-specific belongs in `wasm/Exports.cs`.

If you change the engine, edit the `sbox-library` copy (both targets pick it up). When in
doubt the C# is right.

---

## Why this is a good web toy

- The synthesis is pure integer/float math with a portable PRNG, AOT-compiled to native
  wasm — runs far faster than real time, so we pre-render whole ~75 s loops on demand.
- A whole song is its seed (`vibe:tag:n`), so **the entire experience is a URL**. Share
  `…/web/#vibe:bd44ac2a:23` and the other person hears the exact same song.
- The web has real `<input type=range>` sliders (s&box did not), so the vibe editor is
  nicer here than in the game.

---

## Stack

| Layer | Tech |
|---|---|
| DSP / composition | **C#** (`net10.0`), compiled to **WebAssembly** via the .NET `wasm-tools` workload (`Microsoft.NET.Sdk.WebAssembly`, `RunAOTCompilation`) |
| JS boundary | `[JSExport]` in `wasm/Exports.cs`; `web/engine.js` boots the runtime and adapts the exports |
| Glue / UI | Vanilla **JS + HTML + CSS** (no framework, no bundler) |
| Audio | **Web Audio API** — `AudioBufferSourceNode`s scheduled with gain-ramp crossfades |
| Distribution | A **served** bundle — the self-contained `web/` (which includes `web/_framework`). The runtime is multi-file and needs http; a single-file inline is a deferred follow-up. |

C# is the choice because it *is* the source — same code, two targets, zero port.

---

## Layout

```
skafinity/
  CLAUDE.md
  reference/              # read-only original C# (context only)
  sbox-library/Skafinity/ # the s&box library — Code/MusicGen.cs + VibeCodec.cs are THE source
    skafinity.config.json # canonical shared house-mix config (make copies it to web/)
  wasm/
    Skafinity.Wasm.csproj # browser-wasm project; <Compile Include>s the shared .cs
    Exports.cs            # [JSExport] boundary: generate, vibe codec, WAV, config <-> double[]
    runtimeconfig.template.json
  web/
    index.html            # the page + vibe UI
    engine.js             # boots .NET, exposes the `mod` API app.js/worker.js expect
    app.js                # Web Audio sequencer (port of the controller's scheduling)
    worker.js             # generation worker (its own runtime instance)
    style.css
    config.json           # house-mix overlay fetched at startup (make-copied from sbox-library)
    _framework/           # published runtime bundle (committed; rebuilt by `make`)
  test/smoke.mjs          # node smoke test of the JS↔wasm boundary
  Makefile
```

---

## Parity — now structural, not a reimplementation

Same seed ⇒ same song. Because the web compiles the *same* `MusicGen.cs`/`VibeCodec.cs` as
the game, composition parity is **automatic** — there is no second implementation to drift.
The old "mirror the PRNG / draw order / Config defaults in C++" rules are obsolete; don't
reintroduce a hand-port. The only places parity can break:

- **Don't fork the engine.** Edit `sbox-library/.../MusicGen.cs` once; never copy it into
  `wasm/`. The csproj references it by relative path.
- **The config round-trip.** The live `Config` crosses into JS as an opaque flat `double[]`
  (see `Cfg.To`/`Cfg.From` in `Exports.cs`). If you add a `Config` field that the vibe or a
  song depends on, add it to *both* `Cfg.To` and `Cfg.From` (and bump `Cfg.Size`), or edits
  to it won't survive the boundary.
- **`float` vs `double`.** Keep `MusicGen`'s `float`/`double` exactly as-is; the wasm runtime
  matches .NET semantics, so leave them be.

`make test` boots the published runtime under node and asserts generation, vibe round-trip,
determinism, and WAV output — run it after engine or boundary changes.

---

## The seed format

`vibe:tag:n` (same as the game's `SkafinityPlayer.CurrentSeed`):

- `tag` — any string (a name, a word). It seeds the PRNG together with `n`: the per-song PRNG
  seed string is **`"{tag}:{n}"`** (empty tag ⇒ `"rotaliate"`).
- `n` — song index in the infinite sequence (0, 1, 2 …). Prev/Next step `n`.
- `vibe` — a base-36 string at **16 levels/knob** (`VibeCodec.Levels`), encoding the genre + knob
  overrides. The **first char is the genre** (0 = Ska, 1 = Rock, 2 = Country, 3 = Metal); the rest
  follow the fixed wire grid below. Empty/absent ⇒ default knobs (genre 0).

Parsing (in `web/engine.js`, `parseSeed`) mirrors the controller: accept `vibe:tag:n`,
`tag:n`, or `tag`. The page keeps the current seed in `location.hash` so it's shareable and
reload-stable.

### VibeCodec wire format (genre-aware, append-only)

The wire layout is **genre-independent**: `[genre char][global block][instrument grid]`,
where the grid reserves up to `MaxInstruments` (8) blocks of 4 columns
(volume / tone / character / extra). Column `c` of instrument `i` always lives at
`1 + globals + i*4 + c`, so adding a genre, an instrument, or a 5th column never shifts an
existing position. **Append-only means**: append global knobs, append instrument slots
(≤ 8), and only ever append columns past the 4th — never reorder/remove. `Apply` ignores
trailing positions a shorter string lacks (older/other-genre seeds degrade gracefully). Each
genre defines its own instrument grid (Ska 6 instruments, Rock 4). The JS UI reads the field
list — including each field's `voice`/`column` — straight from the wasm exports
(`VibeFieldName/Min/Max/IsInt/Voice/Column/Choices`, all genre-parameterized) and lays out
the matrix generically, so there's no second field table to keep in lockstep — just edit
`VibeCodec.cs`.

---

## House-mix config (runtime, NOT in the seed)

The peak-balance / kit-presence values that shape the *baseline mix* (`KickBalance`, `TomBalance`,
… `KitPresence`) are `MusicGen.Config` fields, but they are deliberately **not** vibe knobs:
they don't ride in the shareable seed and don't appear as sliders. They live in
`VibeCodec.AdvancedFields` — a separate registry, kept out of `Fields()` and out of the wire
format. Membership in `AdvancedFields` *is* the "config value, not a vibe slider" marker.

One JSON file tunes them for **both** targets without a rebuild:

- **Canonical:** `sbox-library/Skafinity/skafinity.config.json`, an `{ "advanced": { Name: value } }`
  map whose keys match the `Config` field names 1:1.
- **s&box:** `SkafinityPlayer` reads it (`FileSystem.Mounted`) in `OnStart` and overlays it in
  `BuildConfig` via `VibeCodec.ApplyAdvanced`.
- **Web:** `make` copies it to `web/config.json`; `web/app.js` fetches it at startup and overlays
  it onto the base cfg (the JS mirror of `ApplyAdvanced`, over the same field list).

To add a baseline-mix knob: add the `Config` field, add a row to `VibeCodec.AdvancedFields`, add
it to `Cfg.To`/`From` (+ bump `Cfg.Size`), and add a key to the JSON. To make something a *vibe*
knob instead, put it in a genre grid / `GlobalFields` (see above), not here.

---

## Audio scheduling (replaces s&box SoundStream)

`web/app.js` keeps the controller's model over Web Audio:

- `engine.js`'s `generateSong(seed, cfg)` renders **one full structured song** (stereo) —
  intro → chorus → verse(0) → chorus → verse(1) → chorus → ending (see `BuildStructure` in
  `MusicGen.cs`). PCM stays in wasm memory and comes back as a MemoryView the worker copies
  into two `Float32Array`s (valid only synchronously — copy immediately).
- JS wraps each song in an `AudioBuffer`. Because the song has an intro/ending it **plays
  once** (`LoopsPerSong` = 1, `src.loop = false`), then **equal-power crossfades** into the
  pre-rendered next song (seed `tag:(n+1)`).
- **Look-ahead:** keep `AheadCount` songs pre-rendered in a **Web Worker** (its own runtime
  instance) so a render never janks the UI.
- Persist `n` in `localStorage` so playback resumes.

Browsers require a user gesture before audio — `AudioContext.resume()` is gated on the play
button.

---

## Conventions

- No build framework beyond `make`. `make` → publish + stage `web/_framework`; `make dev`
  skips AOT for speed; `make serve` → `python3 -m http.server` rooted at `web/` (the same
  docroot you'd give nginx). `make test` → node smoke test. `make dist` is a deferred
  single-file follow-up.
- **The page must be served** (http), not opened via `file://` — the runtime is a fetched
  bundle. `web/` is self-contained (it includes `web/_framework`), so any static server can
  serve it with the docroot pointed straight at `web/`. `web/_framework` is committed so a
  clone is testable without the SDK.
- Keep `MusicGen.cs` / `VibeCodec.cs` framework-free; web-specific code goes in `Exports.cs`.
- The house-mix config has ONE canonical copy (`sbox-library/Skafinity/skafinity.config.json`);
  `make`'s `stage` step copies it to `web/config.json`. Edit the canonical and re-`make`, or edit
  `web/config.json` directly for quick web-only iteration (the next `make` overwrites it).
- Commit messages end with the Co-Authored-By trailer (see global instructions).
- Feature work goes on branches.
