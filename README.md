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
make          # publish the engine → web/_framework  (AOT; ~2 min)
make dev      # same but skip AOT — much faster to build, identical composition
make test     # node smoke test of the JS↔wasm boundary (needs web/_framework/)
make serve    # static server rooted at web/; open http://localhost:8000/
```

> The page must be **served** (the .NET runtime is a bundle fetched over http) — opening
> `web/index.html` via `file://` won't work. `web/` is self-contained (it includes
> `web/_framework`), so deploy it by pointing any static server's docroot straight at `web/`.
> `web/_framework` is committed so a fresh clone is testable with just `make serve`; rebuild
> it with `make`. A single
> self-contained `.html` (inlining the runtime) is a deferred follow-up — `make dist`.

## What's here

| Path | Role |
|---|---|
| `sbox-library/Skafinity/Code/MusicGen.cs` | Composition (fixed RNG draw order) + subtractive synth — the algorithmic source of truth, shared with the s&box library. |
| `sbox-library/Skafinity/Code/VibeCodec.cs` | Base-36 vibe encode/apply + field metadata (append-only wire format). |
| `wasm/Exports.cs` | The `[JSExport]` boundary (generate, vibe codec, WAV, config) — the only web-specific code. |
| `wasm/Skafinity.Wasm.csproj` | `browser-wasm` project that `<Compile Include>`s the shared `.cs` and builds the runtime. |
| `web/engine.js` | Boots the .NET runtime and adapts the exports to the small `mod` API the app uses. |
| `web/index.html` · `app.js` · `worker.js` · `style.css` | The page: Web Audio crossfade scheduler, rolling playlist, vibe editor, WAV export, shuffle. |
| `sbox-library/Skafinity/skafinity.config.json` · `web/config.json` | The shared house-mix config (peak balances / kit presence). Canonical in the library; `make` copies it to `web/`. Overlaid at runtime — retune the baseline mix without a rebuild. |
| `test/smoke.mjs` | Node smoke test that boots the published runtime and exercises every export. |

## Features

- **Rolling playlist** — `n` auto-advances on every crossfade and is persisted; a full
  playlist panel shows played / now-playing / up-next, with click-to-jump and per-song
  export.
- **Export to disk** — generate the loop as an interleaved-stereo WAV in-browser and download
  it (no server).
- **Share via URL** — the seed lives in `location.hash`, so a reload or a shared link
  reproduces the exact same song.
- **Random every song** — a 🎲 toggle that re-rolls the vibe for each new song (keeping your
  per-voice volumes), so the stream keeps reinventing itself. Mirrors the s&box panel.
- **House-mix config, no rebuild** — the baseline peak balances live in `web/config.json`
  (copied from the shared `sbox-library/Skafinity/skafinity.config.json`); edit + reload to
  retune the mix without recompiling the wasm. These shape the baseline level mix and are
  *not* vibe knobs, so they never travel in the seed.

## Instruments & their inputs

The vibe editor is a matrix: a block of **global** knobs that shape the whole track, then one
**row per instrument**, four columns each (`volume / tone / character / extra`). Every knob is
quantised to one base-36 digit (16 levels) in the seed. The instrument roster — and the two
genre-specific knobs in each row — changes per genre; the field list is read straight from
`VibeCodec.cs`, so this table is just a readable mirror of it.

**Global** (all genres): `TEMPO MIN` / `TEMPO MAX` (BPM band, 60–200) · `TEMPO BIAS` (how
often a song lands fast) · `SWING` (off-beat delay, 0–0.4) · `RESONANCE` (filter Q, 0.2–2) ·
`STEREO WIDTH` (pan spread).

Every instrument row shares the first two columns: **VOLUME** (0–150%) and **TONE** (low-pass
cutoff). **DRUMS** carries the same four knobs in every genre — its `TONE` sweeps toms ↔
cymbals, and its character/extra are `BUSY` (fill/hit density) and `DRIVE` (timing feel,
pull ↔ push) — but the underlying beat changes per genre, so it's listed in each table below.
The other rows' character/extra columns differ by genre:

**Ska** — BASS · SKANK · ORGAN · LEAD · HORNS · DRUMS

| Instrument | Character | Extra |
|---|---|---|
| BASS | `OCTAVE POP` (octave jumps) | `TRIPLETS` |
| SKANK | `BITE` (high-pass) | `CHOP` (note length) |
| ORGAN | `BUBBLE` (chance) | `VIBRATO` (depth) |
| LEAD | `JUMPINESS` (melodic leaps) | `TRIPLETS` |
| HORNS | `SECTION` (full-section chance) | `DENSITY` |
| DRUMS | `BUSY` | `DRIVE` — one-drop or stepper groove (backbeat when fast) |

**Rock** — DRUMS · BASS · KEYS · LEAD GTR · RHYTHM GTR

| Instrument | Character | Extra |
|---|---|---|
| DRUMS | `BUSY` | `DRIVE` — straight backbeat, per-song kick accents |
| BASS | `DRIVE` (overdrive) | `OCTAVE POP` |
| KEYS | `DISTORTION` | `CHUG` |
| LEAD GTR | `DISTORTION` | `BENDINESS` |
| RHYTHM GTR | `DISTORTION` | `CHUG` |

**Country** — DRUMS · BASS · RHYTHM GTR · KEYS · LEAD GTR

| Instrument | Character | Extra |
|---|---|---|
| DRUMS | `BUSY` | `DRIVE` — train-beat backbeat, per-song kick accents |
| BASS | `DRIVE` (overdrive) | `OCTAVE POP` |
| RHYTHM GTR | `DISTORTION` | `CHUG` |
| KEYS | `DISTORTION` | `CHUG` |
| LEAD GTR | `DISTORTION` | `BENDINESS` |

Same per-instrument columns as Rock, but over a much cleaner base distortion (honky-tonk
piano, strummed open chords, twangy telecaster leads with heavy `BENDINESS`).

**Metal** — DRUMS · BASS · RHYTHM GTR · LEAD GTR

| Instrument | Character | Extra |
|---|---|---|
| DRUMS | `BUSY` | `DRIVE` — double-kick gallop |
| BASS | `DRIVE` (overdrive) | `OCTAVE POP` |
| RHYTHM GTR | `DISTORTION` | `CHUG` |
| LEAD GTR | `DISTORTION` | `BENDINESS` |

Same columns again, but a heavy base distortion: palm-muted gallop rhythm (`CHUG`) and fast
shredding leads.

> The straight backbeat (Rock, Country, and fast Ska) picks a per-song **kick accent**
> personality — which off-beats the kick leans into beyond beats 1 & 3 — and rolls each accent
> per bar, so the groove breathes instead of stamping one mechanical pattern every bar.

## Parity

Same seed ⇒ same song — and because the web toy compiles the *same* `MusicGen.cs` as the
s&box library, a seed shared from one plays identically in the other (no port to drift).
`make test` boots the published runtime under node and checks generation, vibe round-trip,
determinism, and WAV output.
