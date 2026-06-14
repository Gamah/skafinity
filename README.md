# skafinity

*ska + infinity* — an endless, deterministic procedural ska / reggae-rock track that's
generated entirely in your browser from a short shareable seed. No server, no audio
assets: the music is synthesised from scratch in C++ (compiled to WebAssembly) and
scheduled through the Web Audio API. The whole song is a URL —
`skafinity.html#vibe:tag:n`.

This is a C++/WASM port of the procedural-music engine from the Rotaliate s&box client.
The original C# in `reference/` is the algorithmic source of truth (see `CLAUDE.md`).

## Build & run

The DSP core is plain C++17. The browser build needs **Emscripten** (`emcc`); the native
parity test does not.

```sh
make test     # native g++ build of the parity + smoke test (writes build/smoke_*.wav)
make          # build the WASM module → build/skafinity.js  (requires emcc)
make serve    # static server; open http://localhost:8000/web/
make dist     # one self-contained dist/skafinity.html (WASM inlined)
```

> **Note:** `make` (the WASM step) requires emscripten on PATH. Without it you can still
> run `make test` to verify the algorithm and render WAVs natively, but the web page needs
> `build/skafinity.js` to exist.

## What's here

| Path | Role |
|---|---|
| `src/prng.h` | xmur3 + mulberry32 — the parity-critical PRNG. |
| `src/music_gen.{h,cpp}` | Port of `MusicGen.cs`: composition (fixed RNG draw order) + subtractive synth. |
| `src/vibe_codec.{h,cpp}` | Port of `VibeCodec.cs`: base-36 vibe encode/apply + field metadata (22 fields, append-only). |
| `src/bindings.cpp` | Emscripten/embind boundary (`generateSong`, vibe codec, `songToWav`, `parseSeed`, field info). |
| `test/main.cpp` | Native parity + smoke test. |
| `web/index.html` · `app.js` · `worker.js` · `style.css` | The page: Web Audio crossfade scheduler, rolling playlist, vibe editor, WAV export. |

## Features

- **Rolling playlist** — `n` auto-advances on every crossfade and is persisted; a full
  playlist panel shows played / now-playing / up-next, with click-to-jump and per-song
  export.
- **Export to disk** — generate the loop as a WAV in-browser and download it (no server).
  Mono by default (matches the game's export), stereo optional.
- **Share via URL** — the seed lives in `location.hash`, so a reload or a shared link
  reproduces the exact same song.

## Parity

Same seed ⇒ same song. The PRNG, the RNG draw order in `Compose`, and the `Config`
defaults all mirror the C# exactly (drums use a separate `"drums:"+seed` RNG). `make test`
prints golden PRNG vectors and the composed choices so drift is caught early.
