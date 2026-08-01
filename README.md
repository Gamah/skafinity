# skafinity

*ska + infinity* — endless, deterministic procedural songs across six genres (ska, rock,
country, metal, punk and pop), generated entirely in your browser from a short shareable seed. No server, no audio
assets: the music is synthesised from scratch in C# (compiled to WebAssembly) and
scheduled through the Web Audio API. The whole song is a URL — `…/web/#vibe:tag:n`.

The engine is the **same** `MusicGen.cs` + `VibeCodec.cs` the Rotaliate s&box music
library ships (`sbox-library/`). The web toy compiles that shared source to WebAssembly
with the .NET `wasm-tools` workload — no port, so the game and the web run identical
composition code. `reference/` keeps the original C# for context (see `CLAUDE.md`).


Try it out here: https://skafinity.notadomain.lol
## Build & run

### Docker (the deploy path — no local .NET needed)

```sh
make fast     # serve the committed web/ (incl. web/_framework) with stock nginx — no build
make up       # build the wasm bundle from source in Docker first, then serve it (~2 min)
make logs     # follow the container logs
make rebuild  # from-scratch image rebuild (no cache) + restart
make down     # stop and remove (either flavour)
```

`web/_framework` is committed, so **`make fast` is the everyday target** — there is nothing
to compile and it's up in a second. It bind-mounts `web/` read-only, so host-side edits to
the page or the glue are live on reload. Reach for `make up` when you've changed
`MusicGen.cs` / `VibeCodec.cs` / `Exports.cs` and need the bundle rebuilt, or to prove the
build still works. Both produce the same container (`skafinity-1`) on the same port with the
same `nginx.conf`, so they're alternatives — bringing one up replaces the other.

The container publishes on **`127.0.0.1:6970`** only. That loopback bind is the whole
firewall story: Docker's iptables chains are evaluated *before* ufw, so a bare `6970:80`
would be internet-reachable even with `ufw deny 6970`. Front it with the host's reverse
proxy (Caddy terminates TLS and redirects) — **never open the port**.

### Local (.NET SDK on the host)

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
make dist     # package it: dist/ (a GitHub-Pages-ready site) + dist/skafinity.html (one file)
```

> The page must be **served** (over http) — opening it via `file://` won't work, and that is
> true of the single-file build too. `web/` is self-contained (it includes `web/_framework`),
> so deploy it by pointing any static server's docroot straight at `web/`. `web/_framework` is
> committed so a fresh clone is testable with just `make serve`; rebuild it with `make`.

### Handing it out — `make dist`

`make dist` turns the built `web/` into two artifacts:

- **`dist/`** — a static site ready to publish as a GitHub Pages branch/folder root (~7 MB).
  Every path in the page is relative, so a project page's `/<repo>/` subpath needs no
  rewriting. Three things make it more than `cp -r web dist`: it drops the `*.br`/`*.gz`
  duplicates a plain static host never serves, it re-copies `config.json` from the canonical
  `sbox-library/Skafinity/skafinity.config.json` (so a hand-edited `web/config.json` can't
  ship), and it writes a zero-byte **`.nojekyll`**.
  > **The `.nojekyll` is not optional.** GitHub Pages runs Jekyll over the published tree, and
  > Jekyll excludes directories whose name begins with an underscore — which is exactly
  > `_framework/`. Without that file the runtime is silently dropped from the deployed site and
  > the page dies at boot on a 404 for `dotnet.js`.
- **`dist/skafinity.html`** — ONE file (~9.5 MiB) with the page, the glue and the whole .NET
  runtime inlined: base64 for the wasm, `data:` URLs for the two runtime js modules, and a
  blob-URL module for the generation workers. Hand it to someone and it needs nothing else —
  but it still has to be **served over http**, not opened from disk. It sits inside `dist/`, so
  a deployed site also offers the standalone as a download.

`make test-dist` builds both and boots the single file's inlined runtime under node, rendering
a song through the same `loadBootResource` path a browser takes.

## What's here

| Path | Role |
|---|---|
| `sbox-library/Skafinity/Code/Engine/` | The engine — composer + subtractive synth, one file per concern. The algorithmic source of truth, compiled by BOTH targets. `GenreProfile.cs` is what makes a genre a genre; `Pattern.cs` and `Melody.cs` are the rhythmic and melodic units. |
| `sbox-library/Skafinity/Code/Engine/VibeCodec.cs` | Base-36 vibe encode/apply + field metadata (append-only wire format). |
| `wasm/Exports.cs` | The `[JSExport]` boundary (generate, vibe codec, WAV, config) — the only web-specific code. |
| `wasm/Skafinity.Wasm.csproj` | `browser-wasm` project that `<Compile Include>`s the shared `.cs` and builds the runtime. |
| `web/engine.js` | Boots the .NET runtime and adapts the exports to the small `mod` API the app uses. |
| `web/index.html` · `app.js` · `worker.js` · `style.css` | The page: Web Audio crossfade scheduler, rolling playlist, vibe editor, WAV export, shuffle. |
| `sbox-library/Skafinity/skafinity.config.json` · `web/config.json` | The shared house-mix config (peak balances / kit presence / stereo-width knobs). Canonical in the library; `make` copies it to `web/`. Overlaid at runtime — retune the baseline mix or the width without a rebuild. |
| `tools/bundle-single.mjs` | Builds `dist/skafinity.html` — inlines the runtime behind `dotnet.withResourceLoader`. Every rewrite is anchored on an exact source pattern and hard-fails if it stops matching. |
| `test/smoke.mjs` · `test/page.mjs` · `test/dist-single.mjs` | Node tests: the raw `[JSExport]` boundary, the surface the page actually calls, and the single-file artifact booting its inlined runtime. |
| `test/engine/` | The engine-only C# harness (`make test-engine`) — composition, harmony, patterns, melody, form, mix balance, render digests. The check that runs without a browser. |
| `docker/` | `Dockerfile` (SDK build stage → nginx runtime stage), `docker-compose.yml` (`make up`: project `skafinity`, container `skafinity-1`, loopback 6970), `docker-compose.fast.yml` (`make fast`: stock nginx over the committed bundle), `nginx.conf` (docroot + cache headers). |

## Features

- **Rolling playlist** — `n` auto-advances on every crossfade and is persisted; a full
  playlist panel shows played / now-playing / up-next, with click-to-jump and per-song
  export.
- **Export to disk** — generate the loop as an interleaved-stereo WAV in-browser and download
  it (no server).
- **Share via URL** — the seed lives in `location.hash`, so a reload or a shared link
  reproduces the exact same song.
- **Random every song** — a 🎲 toggle that re-rolls the vibe *and the genre* for each new song
  (keeping your per-voice volumes), so the stream keeps reinventing itself.
- **Stereo image** — the mix is laid out across the field, not summed to the centre: hats
  left / ride right, toms spread by pitch (rack → left, floor → right), the kit's two crashes
  split L/R, and every non-drum voice is **double-tracked** — two slightly-detuned,
  independently-phased takes panned apart for genuine width (not a mono copy on both
  channels). Bass stays centred for a tight low end. The `STEREO WIDTH` knob scales the whole
  image from full down to mono, and the double-tracking parameters are config-tunable below.
- **House-mix config, no rebuild** — the baseline peak balances *and the stereo-width knobs*
  live in `web/config.json` (copied from the shared `sbox-library/Skafinity/skafinity.config.json`);
  edit + reload to retune the mix or the width without recompiling the wasm. These shape the
  baseline mix and are *not* vibe knobs, so they never travel in the seed.

## Instruments & their inputs

The vibe editor is a matrix: a block of **global** knobs that shape the whole track, then one
**row per instrument**, four columns each (`volume / tone / character / extra`). Every knob is
quantised to one base-36 digit (16 levels) in the seed. The instrument roster — and the two
genre-specific knobs in each row — changes per genre; the field list is read straight from
`VibeCodec.cs`, so this table is just a readable mirror of it.

**Global** (all genres): `TEMPO` (0.70–1.45× the genre's own band) · `TEMPO BIAS` (how often a
song takes the genre's uptempo band) · `STEREO WIDTH` (master stereo amount — see *Stereo image*
below; 100% = full, 0% = mono) · `REVERB` (room blend). Tempo bands and swing are per-genre
character rather than knobs (see `GenreProfile`), so neither is here.

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
| DRUMS | `BUSY` | `DRIVE` — backbeat or driving eighths |
| BASS | `DRIVE` (overdrive) | `OCTAVE POP` |
| KEYS | `DISTORTION` | `CHUG` |
| LEAD GTR | `DISTORTION` | `BENDINESS` |
| RHYTHM GTR | `DISTORTION` | `CHUG` |

**Country** — DRUMS · BASS · RHYTHM GTR · KEYS · LEAD GTR

| Instrument | Character | Extra |
|---|---|---|
| DRUMS | `BUSY` | `DRIVE` — the train beat, or a two-beat feel |
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

> **Every genre draws its groove from its own table** — there is no shared "straight backbeat"
> default any more. `KICK SYNC` still humanises whichever groove is drawn: a stray extra kick
> pushing into the following beat, rolled per bar, so the pattern breathes rather than stamping.

> **Songs have a tune.** Each one draws a chorus melody (repeated identically every chorus —
> that is what makes a chorus) and a sparser verse melody, as long as the harmonic cycle they
> sit over. Solos and intros are where the genre's own lead grammar improvises instead.

## Parity

Same seed ⇒ same song **within a build** — and because the web toy compiles the *same*
`Code/Engine/` as the s&box library, a seed shared from one plays identically in the other (no
port to drift). Across commits the audio is expected to change whenever the engine does; there
is no golden-audio contract and old seeds are not preserved.
`make test` boots the published runtime under node and checks generation, vibe round-trip,
determinism, and WAV output.
