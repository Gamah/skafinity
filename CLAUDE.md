# CLAUDE.md — skafinity

**skafinity** = *ska* + *infinity*. A static-HTML web toy that streams an **endless,
deterministic procedural ska / reggae-rock track** generated entirely in the browser from a
short shareable seed. No server, no audio assets — the music is synthesised from scratch in
WebAssembly and scheduled through the Web Audio API.

Read this file fully before making changes.

---

## Origin

The generator is a straight port of the procedural-music engine from the **Rotaliate s&box
client** (`../rotaliate-client`, issue: procedural music). The original C# lives in
`reference/` and is the **algorithmic source of truth**:

| File | Role |
|---|---|
| `reference/MusicGen.cs` | The composer + subtractive synthesiser. Portable PRNG → every musical choice → interleaved stereo PCM. |
| `reference/VibeCodec.cs` | Base-36 encoding of the "vibe" knobs → the shareable seed fragment. |
| `reference/MusicController.cs` | The s&box playback driver: infinite `tag:n` sequence, look-ahead generation, crossfade. We reimplement its *scheduling logic* in JS over Web Audio (the s&box `SoundStream` bits are not portable). |

**Do not edit `reference/`.** It documents what the C++ must reproduce. When the C# and the
C++ disagree, the C# is right unless this file says otherwise.

---

## Why this is a good web toy

- The synthesis is pure integer/float math with a portable PRNG — it ports to C++ verbatim
  and runs far faster than real time, so we can pre-render whole ~75 s loops on demand.
- A whole song is ~3 KB of seed-defining state (`vibe:tag:n`), so **the entire experience
  is a URL**. Share `skafinity.html#vibe:bd44ac2a:23` and the other person hears the exact
  same song.
- The web has real `<input type=range>` sliders (s&box did not), so the vibe editor is
  nicer here than in the game.

---

## Stack

| Layer | Tech |
|---|---|
| DSP / composition | **C++17**, compiled to WASM with **Emscripten** (`emcc`) |
| Glue / UI | Vanilla **JS + HTML + CSS** (no framework, no bundler) |
| Audio | **Web Audio API** — `AudioBufferSourceNode`s scheduled with gain-ramp crossfades |
| Distribution | One self-contained `index.html` (WASM + JS can be inlined as base64 for a true single file, or shipped as 3 sibling files for dev) |

Rust/Zig would also work; **C++ is the choice** because the source is imperative numeric C#
that maps 1:1 to C++ and Emscripten's tooling is the most turnkey for "ship a wasm blob."

---

## Layout

```
skafinity/
  CLAUDE.md
  PLAN.md
  reference/            # read-only C# originals (the spec)
  src/
    prng.h              # xmur3 + mulberry32 — the parity-critical core
    music_gen.h/.cpp    # port of MusicGen.cs (compose + synth)
    vibe_codec.h/.cpp   # port of VibeCodec.cs
    bindings.cpp        # emscripten exports: generate(), vibe encode/decode, field metadata
  web/
    index.html          # the page + vibe UI
    app.js              # WebAudio sequencer (port of MusicController scheduling)
    style.css
  build/                # emcc output (gitignored): skafinity.wasm, skafinity.js
  Makefile              # emcc build + a `serve` target
```

---

## The cardinal rule: PRNG parity

Same seed **must** produce the same song, on every machine and ideally identical to the s&box
client. That holds only if three things match the C# exactly:

1. **The PRNG.** `xmur3(string) -> uint32` seed, then a `mulberry32`-style stream (the `Rng`
   class in `MusicGen.cs`). All arithmetic is **`unchecked` 32-bit unsigned** — in C++ use
   `uint32_t` and let it wrap; never widen to 64-bit mid-hash. `Next()` returns
   `(t ^ (t >> 14)) / 4294967296f` as a `float` in `[0,1)`.
2. **The call order.** `Compose()` pulls from the RNG in a fixed sequence (fast? → bpm →
   scale → progression → root → instrument → pan → bass pattern → drum style → organ bubble →
   horns…, then per-bar bass/rhythm/lead/horn/drum draws). Drums use a **separate** RNG seeded
   `"drums:" + tag`. Reproduce the order draw-for-draw; an extra or reordered `rng.Next()`
   desyncs everything after it.
3. **The `Config` values.** The composition reads knob values (tempo bounds, chances, mix).
   The *vibe* string overrides the subset of knobs `VibeCodec` covers; the rest come from
   defaults baked into `Config`. Keep the defaults identical to `MusicGen.Config`.

Synthesis (oscillators, SVF, drums) only needs to match for **bit-identical audio**. For a
web toy, matching *composition* (so shared seeds play the same arrangement) is the must-have;
sample-exact synthesis is nice-to-have. Note `float` determinism: use `float` (not `double`)
where the C# uses `float`, and prefer the same `sinf`/`expf`/`tanhf` call sites.

---

## The seed format

`vibe:tag:n` (same as the game's `MusicController.CurrentSeed`):

- `tag` — any string. In the game it's an 8-hex `player_tag`; here it's freeform (a name, a
  word). It seeds the PRNG together with `n`: the actual per-song PRNG seed string is
  **`"{tag}:{n}"`** (empty tag ⇒ `"rotaliate"`).
- `n` — song index in the infinite sequence (0, 1, 2 …). Prev/Next step `n`.
- `vibe` — a base-36 string, **one char per `VibeCodec.Field`**, encoding the knob overrides.
  Empty/absent ⇒ use default knobs.

Parsing mirrors `MusicController.PlaySeed`: accept `vibe:tag:n`, `tag:n`, or `tag`. The page
keeps the current seed in `location.hash` so it's always shareable and reload-stable.

### VibeCodec is append-only

The field list order **is the wire format**. Only ever append new fields; never reorder or
remove. `Apply` ignores trailing chars a shorter string lacks (older seeds degrade
gracefully), and the decoder accepts a few chars under the current length. The list currently
has **22 fields** (last two: `DRUM BUSY`, `TRIPLETS`). Keep `src/vibe_codec` and the JS field
metadata in lockstep with `reference/VibeCodec.cs`.

---

## Audio scheduling (replaces s&box SoundStream)

We keep `MusicController`'s model but realise it with Web Audio:

- WASM `generate(seedStr, config) -> Float32 PCM` renders **one full loop** (stereo or mono;
  the C# downmixes to mono for the game — we can keep stereo on the web).
- JS wraps each loop in an `AudioBuffer`. A song plays `LoopsPerSong` (default 2) passes, then
  **crossfades** into the pre-generated next song (seed `tag:(n+1)`).
- Crossfade = schedule the outgoing buffer's tail and the incoming buffer's head on two
  `AudioBufferSourceNode`s through two `GainNode`s with equal-power ramps, reusing the
  `Crossfade` / `CrossfadeOverlap` math from `MusicController.PushTransition`.
- **Look-ahead:** keep `AheadCount` songs pre-rendered. WASM generation is sync and fast, but
  call it off the main thread in a **Web Worker** (or `setTimeout(0)` chunking) so a render
  never janks the UI. Mirrors `FillAhead`.
- Persist `n` in `localStorage` (the game used `PlayerData.MusicN`) so playback resumes.

Browsers require a user gesture before audio starts — gate `AudioContext.resume()` on the
play button.

---

## Conventions

- No build framework beyond `make`. `make` → wasm in `build/`; `make serve` → `python3 -m
  http.server` rooted so `web/` can fetch `build/`. `make dist` → inline everything into one
  `skafinity.html`.
- Keep `src/` free of Emscripten-isms except `bindings.cpp`; `music_gen`/`vibe_codec`/`prng`
  must compile as plain C++ so they're unit-testable on the host (a native `make test` that
  diffs PRNG output against vectors captured from the C#).
- Commit messages end with the Co-Authored-By trailer (see global instructions).
- This is a fresh repo on `master`; feature work goes on branches.

---

## Parity test (do this early)

Before trusting the port, capture golden vectors from the C#: the first ~32 `Rng.Next()`
floats for a couple of seeds, and the chosen bpm/scale/progression/instrument for a few
`tag:n`. Assert the C++ reproduces them bit-for-bit. PRNG drift is the one bug that silently
ruins everything downstream, so pin it with a test, not by ear.
