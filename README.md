# skafinity

*ska + infinity* — an endless, deterministic procedural ska / reggae-rock track that's
generated entirely in your browser from a short shareable seed. No server, no audio
assets: the music is synthesised from scratch in C# (compiled to WebAssembly) and
scheduled through the Web Audio API. The whole song is a URL — `…/web/#vibe:tag:n`.

The engine is the **same** `MusicGen.cs` + `VibeCodec.cs` the Rotaliate s&box music
library ships (`sbox-library/`). The web toy compiles that shared source to WebAssembly
with the .NET `wasm-tools` workload — no port, so the game and the web run identical
composition code. `reference/` keeps the original C# for context (see `CLAUDE.md`).

## Build & run

One-time toolchain (machine, not vendored):

```sh
sudo apt-get install -y dotnet-sdk-10.0
dotnet workload install wasm-tools
```

Then:

```sh
make          # publish the engine → build/_framework  (AOT; ~2 min)
make dev      # same but skip AOT — much faster to build, identical composition
make test     # node smoke test of the JS↔wasm boundary (needs build/)
make serve    # static server; open http://localhost:8000/web/
```

> The page must be **served** (the .NET runtime is a bundle fetched over http) — opening
> `web/index.html` via `file://` won't work. `build/_framework` is committed so a fresh
> clone is testable with just `make serve`; rebuild it with `make`. A single
> self-contained `.html` (inlining the runtime) is a deferred follow-up — `make dist`.

## What's here

| Path | Role |
|---|---|
| `sbox-library/Skafinity/Code/MusicGen.cs` | Composition (fixed RNG draw order) + subtractive synth — the algorithmic source of truth, shared with the s&box library. |
| `sbox-library/Skafinity/Code/VibeCodec.cs` | Base-36 vibe encode/apply + field metadata (append-only wire format). |
| `wasm/Exports.cs` | The `[JSExport]` boundary (generate, vibe codec, WAV, config) — the only web-specific code. |
| `wasm/Skafinity.Wasm.csproj` | `browser-wasm` project that `<Compile Include>`s the shared `.cs` and builds the runtime. |
| `web/engine.js` | Boots the .NET runtime and adapts the exports to the small `mod` API the app uses. |
| `web/index.html` · `app.js` · `worker.js` · `style.css` | The page: Web Audio crossfade scheduler, rolling playlist, vibe editor, WAV export. |
| `test/smoke.mjs` | Node smoke test that boots the published runtime and exercises every export. |

## Features

- **Rolling playlist** — `n` auto-advances on every crossfade and is persisted; a full
  playlist panel shows played / now-playing / up-next, with click-to-jump and per-song
  export.
- **Export to disk** — generate the loop as a WAV in-browser and download it (no server).
  Stereo or mono (the game's export is mono).
- **Share via URL** — the seed lives in `location.hash`, so a reload or a shared link
  reproduces the exact same song.

## Parity

Same seed ⇒ same song — and because the web toy compiles the *same* `MusicGen.cs` as the
s&box library, a seed shared from one plays identically in the other (no port to drift).
`make test` boots the published runtime under node and checks generation, vibe round-trip,
determinism, and WAV output.
