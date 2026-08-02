# CLAUDE.md — skafinity

**skafinity** = *ska* + *infinity*. A web toy that streams an **endless, deterministic
song** — ska, rock, country, metal, punk or pop — generated entirely in the browser from a short
shareable seed. No server, no audio assets — the music is synthesised from scratch in
WebAssembly and scheduled through the Web Audio API.

---

## Origin & the single source of truth

The generator comes from the **Rotaliate s&box client** procedural-music engine. It now
lives here as a standalone s&box library under `sbox-library/Skafinity/` — and **that C# is
the single source of truth for both the game and this web toy**. The web build compiles the
*same* files to WebAssembly; there is no separate port to keep in sync.

| File | Role |
|---|---|
| `sbox-library/Skafinity/Code/Engine/` | **The engine — the spec.** The composer + subtractive synthesiser, split one file per concern (see the tree below). `MusicGen` is a `partial class` across the folder. **This folder is the unit both targets compile**: `wasm/Skafinity.Wasm.csproj` globs `Engine/**/*.cs` and s&box globs it implicitly, so the folder boundary is the thing that keeps the s&box-only driver and UI out of the web build. |
| `sbox-library/Skafinity/Code/Engine/MusicGen.cs` | Engine core — per-song state, constructor, public entry points (whole-song + chunked). Start here. |
| `sbox-library/Skafinity/Code/Engine/VibeCodec.cs` | Base-36 encoding of the "vibe" knobs → the shareable seed fragment. Also holds the `AdvancedFields` registry — the baseline-mix knobs that are config-only (NOT in the seed or the sliders). |
| `test/engine/` | Engine-only test harness (`make test-engine`) — compiles `Engine/**` alone into the same assembly as the tests, so it runs on a plain dev host where s&box cannot. The safety net for engine work. |
| `sbox-library/Skafinity/skafinity.config.json` | The single shared **house-mix config** (peak balances / kit presence). Canonical here; the s&box plugin reads it at runtime and `make` copies it to `web/config.json`. Edit it to retune the baseline mix without a rebuild. |
| `sbox-library/Skafinity/Code/SkafinityPlayer.cs` | The s&box playback driver (`SoundStream`, infinite `tag:n`, look-ahead, crossfade). Web equivalent is `web/app.js`; the s&box-only bits are not used on the web. |
| `sbox-library/Skafinity/Code/UI/SkafinityMusicPanel.razor` (`.scss`) | Optional drop-in Razor `PanelComponent` — finds a `SkafinityPlayer` and exposes its knobs as in-game UI (seed/prev-next, genre, per-instrument vibe mixer, mute/volume, reroll, save). s&box-only; not in the web build. Re-themeable via the `.scss` variable block. |
| `reference/*.cs` | The original Rotaliate-client copies, kept for context. **Read-only.** The `sbox-library` copies are what actually compile. |

Everything under `Code/Engine/` is framework-free (only `System` / `System.Collections.Generic`
/ `System.Text`) — that's *why* it compiles to wasm unchanged. Keep it that way: **no s&box
(`Sandbox.*`) types and no web/Emscripten-isms anywhere in `Engine/`.** Anything web-specific
belongs in `wasm/Exports.cs`; anything s&box-specific belongs in `Code/SkafinityPlayer.cs` or
`Code/UI/`, which are outside the glob. Adding a file to `Engine/` is all it takes to ship it
to both targets — there is no file list to update.

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
| Distribution | A **served** bundle — the self-contained `web/` (which includes `web/_framework`). `make dist` repackages it two ways: a GitHub-Pages-ready `dist/`, and `dist/skafinity.html` with the whole runtime inlined. Both need http; neither works from `file://`. |
| Deploy | Docker Compose (`make up`) — SDK build stage → nginx runtime stage; host reverse proxy (Caddy) fronts it with TLS |

C# is the choice because it *is* the source — same code, two targets, zero port.

---

## Layout

```
skafinity/
  CLAUDE.md
  reference/              # read-only original C# (context only)
  sbox-library/Skafinity/ # the s&box library — Code/Engine/ is THE source
    skafinity.config.json # canonical shared house-mix config (make copies it to web/)
    Code/
      Engine/             # ← the framework-free engine; BOTH targets compile exactly this
        MusicGen.cs       #   core: per-song state, ctor, public entry points
        MusicGen.Config.cs#   every knob the composer + synth read
        Rng.cs            #   xmur3 → mulberry32, the root of every musical choice
        Harmony.cs        #   per-genre scale/progression/voicing/bass tables + degree→pitch
        Pattern.cs        #   THE RHYTHMIC UNIT: a figure with its own LengthTicks
        Melody.cs         #   THE TUNE: call-and-answer motifs per section type
        CompFigure.cs     #   the comp figures — what rhythm each genre's chordal voices play
        Structure.cs      #   Section, Part (energy/feel/key/tempo), the per-genre song forms
        GenreProfile.cs   #   per-genre character that is NOT a knob (form, grooves, accents…)
        Timing.cs         #   THE TIME BASE: ticks -> samples, tempo accumulator, swing
        Compose.cs        #   the composition pass (plan song → render sections)
        Expression.cs     #   per-note pitch shaping (vibrato/bend/glide/scoop)
        Master.cs         #   reverb, soft-clip, normalize
        Wav.cs            #   float mix → PCM, WAV container
        VibeCodec.cs      #   seed encoding + the AdvancedFields registry
        Voices/           #   Comp (dispatch), Bass, Skank, Lead, Keys, Guitar, Horns
        Drums/            #   Groove (DrumGroove tables + the kit/fill pass) + Kit (voices)
        Synth/            #   Patch, Notes (queue), Render, Osc
      SkafinityPlayer.cs  # s&box-only playback driver — outside the glob
      UI/                 # s&box-only Razor panel — outside the glob
  docker/                 # Dockerfile + compose (nginx on loopback 6970; external Caddy fronts it)
  wasm/
    Skafinity.Wasm.csproj # browser-wasm project; <Compile Include>s the shared .cs
    Exports.cs            # [JSExport] boundary: generate, vibe codec, WAV, config <-> double[]
    runtimeconfig.template.json
  web/
    index.html            # the page + vibe UI
    engine.js             # boots .NET, exposes the `mod` API app.js/worker.js expect
    app.js                # Web Audio sequencer (port of the controller's scheduling)
    queue.js              # the sequencer's generation queue — DOM-free so a node test can drive it
    worker.js             # generation worker (its own runtime instance)
    style.css
    config.json           # house-mix overlay fetched at startup (make-copied from sbox-library)
    _framework/           # published runtime bundle (committed; rebuilt by `make`)
  tools/
    bundle-single.mjs     # builds dist/skafinity.html (the whole runtime inlined into one file)
  test/
    smoke.mjs             # node smoke test of the JS↔wasm boundary
    page.mjs              # the mod.* surface app.js/worker.js actually call
    queue.mjs             # the scheduler's generation queue (no wasm — runs on a bare checkout)
    dist-single.mjs       # boots dist/skafinity.html's inlined runtime under node
    engine/               # engine-only C# harness (make test-engine) — runs without s&box
  Makefile
```

---

## Parity — one build, two targets

**The scope of "same seed ⇒ same song" is a single build.** Within one build the web and the
game must agree, because they compile the *same* `MusicGen.cs`/`VibeCodec.cs` — there is no
second implementation to drift. That is the whole parity guarantee, and it is structural: the
old "mirror the PRNG / draw order / Config defaults in C++" rules are obsolete; don't
reintroduce a hand-port.

**Audio is NOT stable across commits, and nothing should be written as if it were.** Engine
work is expected to change what a seed sounds like — that is the point of most of `PLAN.md`.
There is no golden-audio contract, no back-compat shim for old seeds, and a changed render is
not a regression. Don't add reproducibility machinery beyond what a refactor needs to prove
itself, and don't let it creep into the docs as a promise.

The places parity *within a build* can break:

- **Don't fork the engine.** Edit `sbox-library/Skafinity/Code/Engine/` once; never copy a
  file into `wasm/`. Both csprojs glob that folder.
- **The config round-trip.** The live `Config` crosses into JS as an opaque flat `double[]`
  (see `Cfg.To`/`Cfg.From` in `Exports.cs`). If you add a `Config` field that the vibe or a
  song depends on, add it to *both* `Cfg.To` and `Cfg.From` (and bump `Cfg.Size`), or edits
  to it won't survive the boundary.
- **`float` vs `double`.** Keep `MusicGen`'s `float`/`double` exactly as-is; the wasm runtime
  matches .NET semantics, so leave them be.

## Testing

**`make test-engine` is the one that runs here.** It compiles `Code/Engine/` alone into a
plain `net10.0` console app — no s&box, no `wasm-tools` workload, no browser — and asserts on
composition: PRNG determinism, `ScaleMidi` across the octave wrap, every progression degree
resolving, song structure, the vibe codec round-trip (including junk and truncated input), and
the WAV container. Because the tests compile into the SAME assembly as the engine, `internal`
types are directly reachable; that is why the engine's types are namespace-level `internal`
rather than nested private, and why a new one should stay that way.

It also carries a **render digest** over a 10-seed matrix (`test/engine/digests.txt`). That is
a tripwire for refactors that are *meant* to be pure, not a golden-audio contract: run
`make test-engine-bless` before a mechanical change, `make test-engine` after, and expect
silence. **Any deliberate audible change re-blesses in the same commit** — a digest diff is
information, never a failure to be argued with. The matrix renders at 22.05 kHz
(`Program.MatrixRate`), so **changing that constant invalidates every recorded hash** — the
file's header says so too.

**The harness is render-bound, so keep it that way deliberately.** It runs in ~25 s; it took
8m18s before the work that got it here, and every second of that was synthesis. Four rules keep
a new check from undoing it:

- **Render at the lowest rate the check can answer at.** Nothing here listens. Determinism, the
  digest tripwire, "does this knob change the song", the WAV header — none of them get a truer
  answer at 44.1 kHz, and the rate is a straight multiplier on the cost.
- **Render once and ask several questions.** One song per genre carries the length, level,
  clipping and dynamic-range checks. Two checks wanting "a song for genre g" is not a reason to
  render two.
- **Fan out over renders, assert on the main thread.** Renders are independent and every `Rng` is
  per-instance, so the expensive sections build a work list of individual renders and hand it to
  `Parallel.ForEach`. `Check()` writes shared counters and prints a line, so it must NOT run on a
  worker — collect numbers in parallel, assert in order afterwards. Fan out per RENDER, not per
  genre: a genre's measurements are wildly uneven and a per-genre loop idles the machine on the
  slowest one.
- **Soloing a voice costs one voice.** The mix mutes by amplitude, so a soloed render still
  *carries* every other voice's events; `RenderPitchedRange` skips the silent ones and the kit
  voices return early when `_drumGain` is 0. That is what makes the 40-odd solo renders behind
  the mix-balance checks affordable — don't reintroduce a path that renders them at zero gain.

The per-section wall clock prints at the end of every run. When the harness gets slow again, that
table says where, and it has been wrong to guess before.

**Three diagnostics answer "why does this song sound wrong", and all of them beat arguing by ear:**

- `-- --seed vibe:tag:n` prints what the composer decided for that seed: the decoded knobs, the
  tempo and swing (flagging a shuffle), key, changes, voicing, groove, figure lengths, tune
  lengths, ending style, and the form with each section's energy/feel/key.
- `-- --grid [genre]` prints, per voice, how often it lands on a bar line and how far its onsets
  sit from the song's own sixteenth grid (swing and tempo curve included). **A gesture written in
  TICKS scales with tempo** — country's strum spread was "a couple of ticks per string", which is
  5 ms at metal tempo and 75 ms at country's, i.e. a guitar audibly out of time. Anything physical
  rather than musical (a strum spread, the kit's push/lay-back) belongs in milliseconds/samples;
  the suite asserts every voice stays on the grid.
- `-- --score vibe:tag:n [fromBar] [toBar]` prints the SCORE: every voice's onsets over a range of
  bars, at `bar.beat`, with their MIDI pitches and the section + chord each bar is on. **A
  listening note is always about a moment** ("it goes wrong on the 48th beat"), and the other two
  answer whole-song questions — this is the one that reads that moment. It found the pre-chorus
  section-wide displacement (since removed) by putting the guitar at 1.25/1.75/2.25 next to a
  bass and a lead at 1.00/2.00.
  Double-tracking emits two takes ~9 ms apart, so adjacent near-identical rows are one note.

**Balancing the mix is a measurement, not a guess.** `dotnet run --project test/engine -c Release
-- --levels` renders every genre with one voice soloed and prints its level in dB relative to that
genre's drums. It reads `MusicGen.RawLevels()` — **pre-master**, because the master bus
peak-normalizes, so a soloed voice measured at the OUTPUT tells you nothing (every solo comes back
at the same peak). Re-measure after changing what a part plays: the `*Balance` values and the
per-genre `Level` entries in `BassTone`/`RhythmGtrTone`/`KeysLevel`/`LeadLevel` are measured
numbers and they go stale when the part they were tuned for is replaced. The suite asserts the
outcome — comp under the kit, lead not dominating, bass present, and silence when every voice is
muted.

`make test` is the other half: it boots the *published wasm* runtime under node and checks the
JS↔wasm boundary (generation, vibe round-trip, WAV output). It needs `web/_framework`, so it
only runs where the bundle has been built — except `test/queue.mjs`, which touches no wasm and
runs on a bare checkout (see the scheduling section).

**A commit that changes `wasm/Exports.cs` or `web/engine.js` MUST re-publish and re-stage
`web/_framework` in that same commit.** `web/_framework` is committed so a checkout is runnable
without the SDK — which makes every commit a claim that the glue and the bundle agree. Add a
`[JSExport]`, commit the glue that calls it, and defer the rebuild "until later", and anyone who
pulls in between gets a page that dies at boot with `E.Foo is not a function`. `make test` now
cross-checks every export `engine.js` calls against the bundle, so this fails there rather than
in a browser — but the discipline is what keeps intermediate commits runnable at all.

Note that `make test` passing is NOT evidence that a NEW export shipped unless something
actually calls it; the cross-check above exists precisely because the hand-written assertions
only cover exports someone remembered to test.

**Where the AOT publish's two minutes go — measured, don't re-guess.** On an 8-core dev host a
full `make` is ~113 s after a real source change (131 s from an empty `wasm/obj`), and MSBuild's
`PerformanceSummary` attributes it: the emcc `-O2` compile of `aot-instances.dll.bc` ~32 s, the
emcc link + `wasm-opt` ~25 s, the Mono AOT pass ~14 s, the trimmer (ILLink, the log's "Optimizing
assemblies for size") ~10 s, `Csc` ~1 s. Three things that were suspected and are NOT the cost:
`rm -rf $(PUBROOT)` in `all` (an unchanged re-publish is 9 s with or without it — the AOT cache is
in `obj/`, and everything the rm deletes is re-copied from there); the AOT of the framework
assemblies (`[n/5] skipped unchanged assemblies` — only the app assembly and `aot-instances`
re-AOT); and `WasmNativeDebugSymbols=false`, which measured *slower*, not faster. What was
recovered is the ~22 s of brotli/gzip pre-compression (`CompressionEnabled=false` in the csproj —
see the comment there). The rest is native codegen of a program whose point is a hot per-sample
loop, so **`make dev` is the inner loop** — ~22 s, identical composition — and a full `make` is
what you owe `web/_framework` before committing it. Note the render digests do NOT cover build
flags: they run on the engine-only harness, so a flag change is proved by `make test` and a
listen, never by a hash.

**What can and cannot be built on a dev host.** The web side is fully buildable: `make` does a
full AOT publish and stages `web/_framework`, then `make test` exercises the real JS↔wasm
boundary. It needs the `wasm-tools` workload (`dotnet workload install wasm-tools`) and a modern
node — the node bundled in the emscripten pack is v18 and too old for the ESM `dotnet.js`, and
fails identically against a known-good bundle, so don't read that failure as a broken build.

The Makefile resolves both toolchains itself, falling back to the shared
`~/.local/share/toolchains/` copies (`dotnet10/`, `node22/`) when the host has neither on PATH —
so the targets work on a box with no system-wide .NET or node. Override with
`make test NODE=/path/to/node`.

**The s&box side cannot.** There is no engine install here, so `Code/SkafinityPlayer.cs` and
`Code/UI/` are verified by review and grep only. When changing engine internals, check what
they actually reference — today that is just `MusicGen.{Config, Channels, GenerateSamples,
BeginPlan, WavFromSamples}` plus all of `VibeCodec`. Keep that surface stable and the
uncompilable target stays safe.

**Be sparing with `using static` in `Engine/`.** The s&box build adds a project-wide
`GlobalGameNamespace` static using, so importing a collision-prone bare name (`Rest`,
`Approach`, …) risks an ambiguity that only shows up in a build we cannot run here. `Osc` is
imported that way because `Midi`/`StereoGains` are distinctive; `Harmony` is qualified
explicitly because its members are not.

---

## The seed format

`vibe:tag:n` (same as the game's `SkafinityPlayer.CurrentSeed`):

- `tag` — any string (a name, a word). It seeds the PRNG together with `n`: the per-song PRNG
  seed string is **`"{tag}:{n}"`** (empty tag ⇒ `"rotaliate"`).
- `n` — song index in the infinite sequence (0, 1, 2 …). Prev/Next step `n`.
- `vibe` — a base-36 string at **16 levels/knob** (`VibeCodec.Levels`), encoding the genre + knob
  overrides. The **first char is the genre** (0 = Ska-Punk, 1 = Rock, 2 = Country, 3 = Metal, 4 = Punk, 5 = Pop); the rest
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
genre defines its own instrument grid (Ska-Punk 6 instruments, Rock 4). The JS UI reads the field
list — including each field's `voice`/`column` — straight from the wasm exports
(`VibeFieldName/Min/Max/IsInt/Voice/Column/Choices`, all genre-parameterized) and lays out
the matrix generically, so there's no second field table to keep in lockstep — just edit
`VibeCodec.cs`.

---

## Genre character vs. knobs (`Engine/GenreProfile.cs`)

**The era lean is the 90s and 00s.** A genre is not one music, it is one music *at a moment*, and
every genre here is a broad umbrella that gets tuned to some era whether or not anyone chooses one
— genre 0 spent its whole life tuned as 1960s rocksteady because nobody had said out loud which ska
it was. So it is said: **when a genre or a split has a real choice of era, take the 90s/00s one.**
A roster whose ska is 1995 and whose rock is 1971 is a compilation across three decades rather than
one radio station, which is the "they all sound alike" problem from the other direction. Two
qualifications: it is a **lean, not a law** (some genres ARE their era — two-tone is 1979, roots
reggae is the 70s — and those are worth having anyway; the rule only decides the cases where a
genuine choice exists), and it applies to **retunes as much as to new genres**. Every genre's
profile comment block names its era; a genre added or retuned without one is not finished.

**Genre 0 is `Ska-Punk`, and "Ska" is deliberately not taken.** Genre 0 is the third wave —
straight, fast, clean-skank verses into distorted choruses — and calling it the umbrella is what
would block two-tone or a first-wave/rocksteady genre from ever being added under an honest name.
The seed's genre is an INDEX (the first base-36 char of the vibe), so the display name is
display-only and renaming one breaks no seed; the C# identifiers match it (`VibeCodec.SkaPunk()`,
`Harmony.SkaPunk*`, `CompFigure.SkaPunk`/`SkaPunkLoud`, `DrumGroove.SkaPunk`, `SongForm.SkaPunk`).
The repo stays *skafinity*: it is ska + infinity, and the toy is still a ska toy.

Not everything per-genre is a *preference*. Some things a genre simply **is**, and exposing them
as knobs makes a reroll able to produce nonsense — swing was the example: a global 0–0.4 slider
meant shuffle could hand metal a 40% shuffle. `GenreProfile` holds that kind of value and draws
it per song from the seed.

It carries the **swing chance** and swing band, the **shuffle chance** (a 2:1 triplet shuffle is a
different feel, not a wider band — widening country's band to reach it would only make its ordinary
songs sloppy), the **tempo band and uptempo band**, `ChordBars` (2 bars/chord, or 1 for punk/pop so
the four-chord loop *is* the hypermeasure), the ride-vs-hats lean, whether the lead is the ska horn
or a guitar, **what the chordal voice becomes when a section is loud** (`LoudComp`), and **which
tables the genre draws from for everything else**: harmony (scales + weights, progressions,
voicings), the `Form` (its section map), the `CompFigures`/`KeysFigures` its chordal voices play,
its `Grooves`, its `BassPatterns`, its `LeadStyle`, its accent weights and its `Mix`.
`Harmony` is just the tables — read `GenreProfile.For(g).Progressions`.

**Swing is a decision before it is a depth.** `SwingChance` says whether a song swings at all and
the band says how deep it swings *given that it does*; a genre that never swings declares 0 and
needs no band. One band alone could not express "straight", because its floor was a swing too
shallow to hear — so "how swung" and "swung or not" were the same number and neither was legible.
Today only country (0.45, plus the shuffle) and rock (0.25) swing; ska, metal, punk and pop never
do. **The uptempo roll halves the CHANCE, never the depth.** Scaling the depth was the bug: it
pushed the shallow end of the band under the audibility threshold, and did it worst exactly where
the eighth was already shortest, so a fast ska song came out at ~9 ms of push — a straight song
wearing a number. A song either swings at a depth its genre means or it does not swing.

**Tempo and swing are not independent, because they are not independent in the music.** The slow
end of a lineage shuffles and the fast end goes straight — first-wave ska (110–135, out of the
American R&B shuffle) against 2 Tone and the third wave (fast, straight, punk-sharpened); slow
reggae swings its hats and fast reggae does not. That correlation is why the uptempo roll suppresses
swing at all, and it is also the trap: **a genre's tempo band and its swing chance together pick an
ERA**, so changing one without the other describes two different musics at once. Genre 0's tempo
band had already put it in the third wave while every other value still said rocksteady, and *that*
was the actual defect behind "ska sometimes doesn't swing".

**A genre may change technique when a section gets loud** (`LoudComp` + `LoudCompFigures`, gated on
`LoudFrom` against the section's energy). This is a genre's DYNAMIC, not a second genre: same voice,
same chord, different instrument gesture. Third-wave ska is the case that needs it — a clean offbeat
skank through the verses dropping into distorted power chords for the chorus is the most recognisable
thing about the style, and no amount of tuning the skank reaches it, because a chorus is not a louder
skank but a different part. Ska routes to punk's `Downstroke` and picks up `RhythmGtrTone`'s genre-0
entry, whose drive sits past `DirtyChord`, so `DrivenVoicing` drops the third and the chorus lands as
power chords while the skank keeps the song's 7ths and 9ths. **The threshold is on energy, not on
section type** — a genre wanting a loud bridge gives the bridge energy rather than naming it here —
and the loud figure is drawn ONCE PER SONG, because the loud sections are the choruses and every
chorus must agree. The hemiola still outranks it: that is a metric device, and metre beats timbre.

Everything that used to be a shared `switch` is now a table lookup, and that is the pattern to
follow: the old drum `switch` resolved rock, country AND punk to one `default` backbeat, one
`KeysOnsets` served three genres, and one `RenderLeadPhrase` served all six. **If you find
yourself writing `if (_genre == …)` in a voice, the answer almost certainly belongs in the
profile's table instead** — the exception is timbre (below).

**The line the table draws:** it holds what the *composer* reads — harmony, tempo, feel, form,
figures, grooves, accents. Per-voice **timbre** stays next to the voice that renders it, because
that is the sound of one instrument rather than the identity of a genre: `BassTone()` in
`Bass.cs` (register/osc/sustain/filter), `RhythmGtrTone()` in `Guitar.cs`, `KeysDriveFor()` in
`Keys.cs`, the lead's per-genre distortion in `Lead.cs`, the expression propensities in
`Expression.cs`. Don't drag those in, and don't push a rhythm out.

**A voicing is a list of degree offsets, so turning it into NOTES is not `ScaleMidi(base, root +
offset)`.** That spelling is diatonic — every interval comes out whatever the scale makes it at
that degree — which is exactly right for the third, sixth, seventh and ninth (major-or-minor by
position IS diatonic harmony) and wrong for the fourth and the fifth. Every seven-note scale has
one degree whose diatonic fifth is **diminished** and one whose fourth is **augmented**, so on
those degrees a `Power` chord spells as a bare tritone and a `Sus4` as a root with a flat five —
with no third present to explain either. That is what "way off key" sounds like, and distortion
makes it far worse. A guitarist frets the same power-chord shape on every degree; the shape does
not go diminished because of the key. `Harmony.VoicedTone` forces the fourth and the fifth perfect
and leaves everything else diatonic, and **every voice that SOUNDS a chord goes through it** —
`ChordMidis(baseMidi, chord, tick)` / `ChordToneMidi` / `VoicedMidis(baseMidi, rootDegree, voicing)`.
`ChordDegrees` survives as the MELODIC view (what tones a line may land on); if you find a chordal
voice calling `ScaleMidi` over it, that voice is spelling its chords wrong. The engine test asserts
the invariant over every genre × scale × voicing × progression degree, and separately asserts that
the diatonic spelling really does break somewhere — otherwise the first check is vacuous.

**A driven guitar does not play thirds.** Distortion is a non-linearity, so it generates sum and
difference tones between everything fed into it; a root and a fifth are a simple enough ratio to
survive that and a third is not. That is why guitarists play power chords through a driven amp and
full triads through a clean one — and `RockVoicings` includes `Triad`, so a rock song at
`DISTORTION 4` was a full triad through gain 5.5 and read exactly the way it reads on a real amp:
"something is out of tune". `DrivenVoicing()` in `Guitar.cs` drops the voicing's third
(`Harmony.Third`) once the effective drive passes `DirtyChord`; country's clean strum never
reaches it, punk and metal always do. **The song's `_voicing` is untouched** — every chordal voice
must still agree what the chord IS — this drops a note on the way out of ONE voice, so the keys
keep the third and the band still states the quality. Guitar on the fifths, keyboard on the
colour, which is how the parts divide in a real band.

**A suspension is a delayed third, not a chord quality — so it has to land.** `Sus4` and `Sus2` put
the fourth or the second where the third belongs, and the song's `_voicing` is drawn ONCE, so a
suspended voicing means no chordal voice states a third on any chord for the whole song. Nothing is
out of key and every voice agrees; the song simply has no major and no minor, and an ear with
nothing to resolve to hears that ambiguity as dissonance — "spooky", "off", "all wrong". Rock draws
`Sus4` 2 times in 7 and pop `Sus2` 2 in 8, so it is not rare. `Harmony.SuspendedVoice(voicing)`
names the voice that owes a third — **a voicing is suspended exactly when it REPLACES the third,
never when it omits it (`Power`) or colours it (`Sixth`, `Add9`)** — and `MusicGen.VoicingAt(tick)`
hands the chordal voices the resolved spelling over the back half of each chord's span. Four things
this rests on:

- **The resolve point is a TICK, not a bar.** `_susResolveTick` is half way through the current
  chord, whatever `ChordBars` is: the second bar at 2 bars/chord, the second half of the bar at
  pop's 1. A sus that only resolved on a bar line could never resolve at all in a genre where the
  chord *is* a bar.
- **The chord is re-articulated, not switched under a ringing note.** `ChordSegments(tick, ticks)`
  splits a held note at the resolution, because a suspension that resolves silently is not *heard*
  to resolve — the landing is the gesture. It matters most for pop's pad, which sounds one chord a
  bar and would otherwise never move. The guitar and the keys read it; ska's skank and horns don't,
  because every ska voicing states its third.
- **The voice-leading table is not recomputed.** Resolving moves one voice a step inside the
  inversion the song already chose — a finger, not a re-voicing. Rebuilding `_vlShift` for the
  resolved spelling would make the landing a jump.
- **The guitar and the keys divide the same way as ever.** Driven, the guitar plays the suspension
  whole and then drops the note it resolves to, so a rock sus4 lands as a power chord while the keys
  state the quality. The ending is always the resolved spelling — a song must land on a chord that
  says what it is.

The engine test asserts the classification (and that some genre actually draws a suspension, or the
check is vacuous); the digests confirm the scope — the eight non-suspended matrix seeds are
byte-identical across this change and only the two suspended ones move.

**A chord change must not move the whole comp in parallel.** Built upward from its degree, a
chord's register is wherever that degree falls, so the same shape slides: a progression that steps
a third moved every voice most of an octave, with no common tone, every time the change came round
— which is the "it jumped" that reads loudest when a chord change lands on a section boundary.
`Harmony.PlanVoiceLeading` decides it once per song and `ChordMidis`/`ChordToneMidi` apply it — so
**every chordal voice inverts the same way** and the guitar and the keys agree on the inversion as
much as on the chord.

**It is two decisions and they compose: a ROTATION and an octave.** Octave-shifting alone kills the
octave-sized leap but leaves voice *i* permanently on the *i*th offset of the voicing, so a root
move of a fourth still moves every voice by that fourth — a smaller parallel slide with no common
tone in it, which is the other half of "the comp sounds like a chord calculator". Letting the chord
rotate is what produces a common tone: voice *i* plays offset `(i + Rot[c]) mod n`, so the voice
that was on the fifth can take the next chord's root and simply not move. The two are solved in
sequence rather than jointly — jointly, the state is a rotation plus an octave per voice and it
stops being cheap; split, the rotation pass costs a voice's move as the interval folded into a
tritone (i.e. "could this voice hold, if it may invert?"), and the octave pass then answers that
concretely and is per-voice independent again. It took the changes that still throw a voice more
than a tritone from 211 to **8**, over every genre × scale × voicing × progression.

**The array index is therefore a VOICE, not a voicing slot.** Anything that needs to know what a
pitch *is* — the driven guitar dropping the third — asks `ChordOffsets` for the offsets in the same
order rather than indexing `_voicing` alongside the pitches, or it drops whatever landed in the
third's position. For the same reason the country strum spreads its pick **by pitch, not by array
order**: the order the pick crosses the strings is low to high, and the plan reorders and
octave-shifts freely.

Two things it deliberately does not do: the **bass is not voice-led** (it plays roots, and an
inverted root is a different chord), and no voice is reassigned *within* a rotation, so a stepwise
root move still moves the shape a step — that is a guitarist barring the same shape, not the defect.
A progression is a **cycle**, so the last chord back to the first is a change like any other, and
both passes close the loop: walking it greedily either parks every leap on that seam or oscillates
and never settles. Ties go toward root position — the octave nearer it, and rotation 0 — so a plan
that gains nothing from moving simply doesn't, and the comp cannot walk itself out of register over
a few laps.

**The ending is led too, and used not to be.** `EndingChord` built its chord in root position on the
argument that a song should land where its genre voices the chord rather than where the last change
left the register. That made the ending the one unled change in the song and put it on the most
exposed moment there is, so the final chord could leap a seventh out of the one before it.
`Harmony.LeadToward` picks its inversion against what was actually sounding (`_endingPrev`, read at
the last tick of the previous bar so it is past any suspension's landing), inside the same one
octave every other change gets. The genre's own voicing and register still choose the chord; a
cadence is a change, and the ritard is not cover for a jump.

When adding per-genre behaviour, ask which side it falls on: a listener's taste (a knob, in a
genre grid) or the genre's identity (`GenreProfile`). Getting it wrong is only obvious once
shuffle is on.

**Tempo follows the same rule.** The band is the genre's; the vibe knob is `TempoScale`, a
0.70–1.45 multiplier over whatever the genre drew (`TEMPO`, 15 steps of 0.05 so the neutral 1.0
lands exactly on a level). The old absolute `TEMPO MIN`/`TEMPO MAX` knobs — and the `BpmMin`/
`BpmMax`/`FastBpm*` `Config` fields behind them — are gone; their two wire positions are reserved
nulls. `FastChance` survives as `TEMPO BIAS`: how often a song takes the genre's uptempo band.

**The knob saturates per genre, and the ends of a genre's band are a hits-per-second budget.** One
symmetric 0.70–1.45 multiplier cannot be right for six bands at once — its ends were ska 268, metal
290 and country 67, so only the middle of the slider was usable. `GenreProfile.TempoFloor`/
`TempoCeil` are where each genre stops being itself, `DrawBpm` clamps there instead of at a shared
40–300, and the knob keeps its full travel in the UI. **These twelve numbers are a listening call
and nothing else** — the suite asserts the mechanism (a genre's own bands sit inside its
saturation, the knob's ends never leave it, and the clamp is not vacuous), never that a particular
bpm is correct. Sweep the knob at a fixed seed per genre; `--seed` prints the drawn tempo, the
genre's range and whether the knob saturated.

**A tempo band is anchored on records, and an anchor is worthless until its COUNT is decided.**
Every genre's profile records the tracks its band was set against; the trap is that a reported bpm
never says which pulse it counted. Slayer's "Raining Blood" comes back from the same databases at
89 and at 216, and ska is worse — the engine's skank fires once per beat, so genre 0 counts the
double-time reading and every ska tempo has to be converted before it means anything (see the
comment on genre 0). **A number copied out of an aggregator without deciding where the backbeat
falls is not a measurement, it is a coin flip that looks like one** — which is exactly how these
bands were wrong the first time. Published per-genre bpm tables corroborate an anchor; they never
set a band on their own, because they average in every era and every subgenre under the umbrella.
A genre whose anchors are ambiguous **keeps the band it has** and says so, rather than trading a
guess for a guess with a citation on it.

**All six bands are anchored now, and the method that got there is the part worth keeping.** It has
two halves. First, *where this engine's beat is* is settled from the source rather than assumed:
read `DrumGroove`, and every groove in genres 1–5 puts the snare on beats 2 and 4, so for those five
the engine's bpm is the ordinary backbeat count and an anchor converts by identity — genre 0 is the
documented exception, and pop's half-time groove halves the pulse without touching the tempo, which
is a groove within the band rather than a second convention. Second, **prefer a source that states
its own count over one that reports a number.** Aggregators return "Angel of Death" at 106; its
published backing tracks are labelled 50% = 104, 90% = 187, 100% = 208 and 105% = 218, four figures
whose arithmetic only closes at ~208 — and a backing track has to be at the pulse a player counts.
That is what moved metal's uptempo band to 170–210 after years of the ambiguity being treated as
unresolvable. **The obstacle was never that tempo data is scarce; it was that a bpm field does not
say what it counted, and some sources do.** Where no such source exists the band still does not
move: "Raining Blood" is still not an anchor, and *NSYNC's "Bye Bye Bye" (173 or 86, nothing
deciding) is deliberately not one either.

**What the method does not reach is the grooves and the accent weights.** There is no equivalent of
a bpm field for where the kick falls in a train beat or how much louder country's offbeat "chick"
is than its downbeat, so those are still unchecked against anything — see `PLAN.md`.

**When something reads as too dense, suspect the tempo before you thin a part.** The knob is the
usual culprit, and a genre's own band is the next one. Adding a gate somewhere is almost always the
wrong repair — it makes a genre's identity out of a number that belongs to the song.

**No two genres may share more than one progression — or more than one scale.** Six genres drawing
from overlapping tables is how they came to sound alike (I–V–vi–IV was in four progression tables;
major was in four scale tables), so both are pruned and the engine test asserts the cap on each.
Adding an entry means checking it against the other five. The same rule is asserted structurally
for bass patterns and grooves: no two genres may share the object at all.

**Weights are real weights.** The tables used to bias a draw by listing an entry twice, which tied
"how likely" to "how many entries" — and to the overlap cap. `rng.PickWeighted(table, weights)`
separates them and still costs exactly one value out of the stream.

**Every genre pulls the same number of values out of the song stream.** The lead-instrument pick,
the organ-bubble roll and the horn-section roll happen for *all* genres even though only ska
reads them, and `ForceInstrument` takes its draw before overriding the result. A knob decides
*what* plays, never *how many* values the composer pulls — otherwise setting one quietly rewrites
the rest of the song. Two helpers exist to keep that true: `PickWeighted` is one draw whatever the
weights say, and `PickOrNull` takes its draw even for a genre whose table is `null` (pop has no
second chordal voice; it still pays for the slot).

Retiring a knob leaves a **reserved slot** in the wire rather than shifting positions — see the
`VibeCodec` header. That is why the encoded vibe can be longer than the live knob count, so never
assert "vibe length == field count + 1".

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

**`GenreMix` is the one that is not a level.** Each genre carries its own mix profile
(`GenreProfile.Mix`: reverb, width, and low/mid/high trims — metal dry and mid-scooped, pop wide
and bright, country dry and centred, ska roomy), and `GenreMix` says how far that profile is
taken: 1 = as designed, 0 = every genre through one neutral mix, 2 = exaggerated. The SHAPE lives
in the profile because it is character; only the amount is house config, which is why this is one
value rather than six genres × five trims of JSON. Voices apply it through `MixTrim(trim)` —
`_drumLowMul`/`_drumHighMul`/`_midMul` already carry it for the kit and the body of the mix.

**Double-tracking width has a ceiling, and it is not every voice's friend.** `WidthDetune` is
bounded at 20 cents rather than 50: a few cents between two takes reads as two performances, tens
of cents reads as out of tune, and this is house config with no listener-facing undo. `Detune` (the
unison spread INSIDE one patch) is a half-spread applied symmetrically, so a 3-voice patch spans
double it — at 14 the skank, the horns and the trumpet/trombone/organ leads sang across 28 cents,
which is a chorus rather than a unison, and double-tracking stacked on top of it. **The keys are
not double-tracked** (`mono: true` in `EmitKeys`): they already sound a whole voicing of held,
detuned unisons at once, and doubling that put four detuned oscillators on every chord tone. A part
worth doubling is one line or a strum, not a stack of held thirds. Dropping a take costs ~2.5 dB,
so `KeysBalance` was re-measured with `--levels` in the same change — a timbre fix must not smuggle
a mix change in with it.

---

## The time base (`Engine/Timing.cs`)

Musical positions are **integer ticks**, absolute from the song's first downbeat, at
`TicksPerBeat = 48`. Voices take a `barTick` and ask `Timing` for samples; they never compute
sample offsets themselves. Three rules keep it honest:

- **Ticks are metrical, samples are physical.** `TickToSample(tick)` is the only bridge. 48
  renders every subdivision in use exactly (8ths, 16ths, 8th- and 16th-note triplets) — do not
  "simplify" to a 16th grid, that silently deletes every triplet.
- **Grid positions shuffle; tuplets are even.** `TickToSample` applies the swing warp, for
  notes the band lands on together. A tuplet divides its *own span* into equal parts and uses
  `EvenSpan(startTick, spanTicks, frac)`, which warps only the endpoints. A shuffle is itself
  a triplet feel, so a triplet must not be warped a second time on top of it.
- **Tempo is an accumulator, not a multiply.** `Timing` walks a per-tick sample delta across
  the song. With one tempo that equals a multiply; the point is that a per-section tempo or an
  ending ritard is a matter of varying the delta, not a rewrite. Keep it that way.

Durations are spans, not positions: `SamplesForTicks`/`SecondsForTicks` carry no swing.
`DrumPush` (the kit's push/lay-back) stays in continuous sample space — it is a feel, not a
grid position.

**The tempo accumulator is now actually curved, so note LENGTHS have to read it.** The song's
per-tick delta varies with the section's `TempoMul` and ramps over the final bars (the ending
ritard). A duration therefore has a position: use `SpanSamples(fromTick, ticks)` /
`SpanSeconds(fromTick, ticks)`, which measure through the accumulator. `SamplesForTicks(ticks)`
is the *nominal*-tempo span and will cut a note short in a slowing section — it is kept for
spans with no position. Size buffers off `Timing.TotalSamples` (the finished accumulator), never
off `ticks × nominal`, and remember the ring-out tail is a number of seconds the ritard outruns
(it is scaled by `RitardAmount` in `ComposePlan` for exactly that reason).

---

## Patterns — the rhythmic unit (`Engine/Pattern.cs`)

**A rhythmic figure owns its own length.** `Pattern { LengthTicks, cells }` free-runs against the
bar line; nothing indexes "cell for eighth *e* of the bar" any more. That one mechanism covers a
1-bar loop (the old behaviour), a 2-bar call-and-response (the ska horns, the rock riff), a 4-bar
phrase whose last bar varies (the punk and busy-ska bass), and a 3-eighth hemiola that does *not*
divide the bar (the cadential regrouping). It is also why the engine is meter-agnostic enough for
the non-4/4 row to be tractable.

`Slice(from, to, anchor, feel)` is the only way to read one, and every argument is a feature the
composer needs:

- **anchor** — the section's first tick, so a multi-bar figure restarts with the section rather
  than with wherever the song happens to be.
- **feel** — the section's half/double-time multiplier. Half time is a PATTERN RATE, not a tempo:
  the grid is untouched and the song's length does not change.

There is deliberately **no displace argument**. A figure that pushes into the next bar says so in
its CELLS — the ska skank's stab on the "and of 4", the Charleston, the horn answer — which is one
gesture at a phrase seam. A constant per-section offset expressed the same idea the other way and
did not survive a listen; see `SongForm`.

Cell values are the *voice's* vocabulary, not the pattern's: bass cells are semitone offsets plus
`Harmony.Rest`/`Approach`, comp cells are `CompFigure.Ring/Stab/Mute/Tone(i)`, drum cells are hit
vs `Ghost`/`Open`. A `Hit` carries `SpanTicks` (ticks to the pattern's next onset — the legato
length) and `Vel`.

**The thirty-second is available to every genre and every voice, and nothing gates it.**
`Pattern.ThirtySeconds` sits beside `Sixteenths` — `TicksPerBeat = 48` makes a 32nd exactly 6
ticks, the same clean division that gives a sixteenth 12 and a sixteenth-triplet 8, so `Timing`
needed nothing and `MusicGen.GridSamples()` (one entry per TICK) already measures against it.

Two things this deliberately is NOT. It is **not a metal feature**: a buzz or press roll, a drag
into a downbeat, a grace note, a trill, a hi-hat flurry, a chicken-pickin' lick, a gospel run —
32nds are ordinary ornament vocabulary in every genre, and metal's continuous tremolo is the
outlier rather than the model. And it is **not gated on tempo**, which was tried and was wrong:
notes-per-second arithmetic (32nds/s = bpm × 2/15) assumes every note is picked, and a player
hammers on, pulls off, sweeps and buzzes precisely so a fast passage costs the hand less than its
note count. A gate would also have made "how fast a genre is" decide "how fine it may play", which
is two different things.

**What keeps it playable is the GESTURE, not a limit.** 32nds are authored as *bursts* — a handful
of notes accelerating out of a coarser line and resolving into the next beat — and a burst is as
playable at the top of a genre's band as at the bottom. A bar of unbroken 32nds is not; that is the
thing to avoid, and it is an authoring judgement rather than something the engine forbids. **An
ornament REPLACES a note rather than joining it** — a figure that adds four notes to a bar is
louder as well as busier, and the comp is the bed: the engine suite's single-seed balance check
catches exactly that, and it caught it here. Where
they live today: `RenderFill` may run a short fill at 8 per beat, `RenderShredPhrase` breaks its
sixteenth run into 4–8-note flurries, and **every genre's chordal table has one figure carrying the
gesture in its own idiom** — the ska flick off the last chop, the rock riff's pickup into the
downbeat, country's chicken-pickin' pull-off after the chick, metal's tremolo into the bar line,
punk's turnaround flurry, the pop arp's run home. Note also `Expression.cs` is a *different* axis —
a bend or vibrato is continuous pitch across one note, not a rhythmic position — so the two are
additive, and a 32nd cannot be "folded into" a bend.

One consequence worth keeping: **a doubling bass stops at the sixteenth.** Under a riff finer than
that, `RenderBassFromRiff` plays the `Ring` accents only — four low notes to a beat is mud rather
than a part, and a bassist under a tremolo riff plays the chord statements.

---

## The tune (`Engine/Melody.cs`)

**A song is a melody, not a backing track.** Before this the chordal voices played a rhythm figure
and the lead improvised a fresh phrase every two bars — nothing recurred, so there was nothing to
hum. Every genre now draws two tunes per song, off their own streams (`{tag}:tune:chorus` /
`:verse`), and a section SINGS its tune rather than inventing a line:

- A tune is a `Pattern` whose cell values are **scale degrees relative to the key's tonic**, not to
  the current chord — that is what lets the line keep its shape while the harmony moves under it.
  `RenderTune` resolves a degree to the nearest chord tone **on the strong beats only**; snapping
  every note would rewrite the tune chord by chord, which is the "improvisation over the changes"
  this replaces.
- `Melody.Draw` writes **call and answer**: the second phrase repeats the first's rhythm exactly
  and resolves home. The rhythm is drawn before the pitches for that reason — a fresh random phrase
  never sounds composed however good its notes are.
- The chorus tune is the hook: **identical every chorus**, which is what makes a chorus a chorus.
  Verses get their own, sparser tune. `TuneFor(section)` returns null where a section is not a
  place for one — a solo is where the genre's `LeadStyle` grammar improvises, an intro is a
  build-in, the ending has resolved. Metal is the one riff-led genre: chorus tune only.
- The genre's hand shows in ORNAMENT, not in a different melody: country punctuates the tune with
  double-stops, metal runs between its notes. `LeadStyle` still owns the improvised sections.
  **Ornament means occasional, and it means underneath.** Country harmonised EVERY long note a
  third above, both notes the same length off the same rolled expression — so the pair bent,
  scooped and vibratoed in parallel, which is a two-tone horn rather than a guitar, and the top
  line the ear followed was the harmony rather than the tune. `EmitDoubleStop` puts the third
  BELOW the melody, dry (a fretting hand bends ONE string) and shorter, at `DoubleStopChance`.
  A second voice that fires on every note is not an ornament, it is the instrument.

The ska skank is one comp style among six, not the model for the others — the chordal voice is the
bed under the tune in every genre, including ska. Ska is also the one genre that plays *two* of them
(`GenreProfile.LoudComp`): the skank through its verses, punk's downstroke through its choruses.

**The tune is as long as the harmonic cycle** (`ChordBars × progression length`, capped at 8 bars).
A four-bar tune over an eight-bar cycle states itself twice and the second statement lands over
different chords than it was drawn against — same notes, different harmony, which is what "the lead
clashes with the backing" actually is.

**A section shorter than the tune sings the tune's END, not its beginning.** `RenderTune` pulls the
anchor back by the difference, so the section's last bar lands on the tune's resolution. A four-bar
pre-chorus over an eight-bar tune otherwise stated the call and was cut off by the chorus before
the answer arrived — a phrase interrupted by the next phrase, which is what "two ideas at once"
sounds like from the outside.

**A REGISTER IS A NUMBER OF OCTAVES, AND ONLY EVER THAT.** `ScaleMidi` and `VoicedTone` treat
their base as **the tonic** and add the scale offset on top, so a base of `_rootMidi + 31` does not
raise a part by a fifth — it spells that part **in the key a fifth up**. The part then disagrees
with the band about one note of the scale (two of them, a whole tone up), which is a melody in the
wrong key, because it is one. Every voice takes its base through **`Register(octaves)`** so a
non-octave base cannot be written at all; that is worth more than a test, because the wrong version
stays in tune roughly six notes in seven and never announces itself. Transposing an actual *pitch*
by an octave (`ChordRoot(c) + 12`) is a different thing and is fine — the scale is already spelled
by then.

The registers: rhythm guitar `Register(1)` (metal `Register(0)`, low and chunky), skank and keys
`Register(2)`, the sung lead `Register(3)`. Three octaves for the lead because the comp is a known
bed — a full voicing over `Register(2)` reaches about `+31`, so a melody at two octaves sings
inside it and no amount of gain brings it out. Two exceptions are relational: metal's shred ranges
far wider than a tune and its own comp sits at the root, so `Register(2)` clears it; punk's unison
IS the riff an octave over the guitar, so it is `Register(2)` by definition. **The cost is that
register is quantised** — a part sits an octave up or it doesn't. If one ends up too high, narrow
what it plays (a melody's degree range) rather than reaching for a base between two octaves.

Level is the second half of the same problem: the lead's target is **+2 dB over the genre's
drums**, not level with them (see `LeadLevel`, and the ceiling in the suite is that target plus a
seed's worth of variance).

**A melody resolves to what the chord SOUNDS, not to what its degrees say.** `ChordDegrees` is
diatonic; the sounding chord is not, because `VoicedTone` forces the fourth and the fifth perfect.
On the one degree of every scale whose diatonic fourth is augmented the two disagree by a semitone,
so a note the composer deliberately snapped to a chord tone arrives a semitone off the chord — the
one place a "consonant on the strong beats" melody can still grind. `NearestChordTone` chooses
*which* chord tone in degree space; `NearestSoundingTone(midi, chord, tick)` then puts the note on
the pitch that is actually playing, preserving the octave. Because `ChordMidis` is tick-aware, the
melody follows a suspension to its resolution too. The one strong beat it must NOT touch is the
horn answer's deliberate step off the chord tone (`RenderSungPhrase`) — that dissonance is the
gesture.

---

## Sections carry state (`Engine/Structure.cs`, `SongForm`)

`BuildStructure(genre)` returns the genre's OWN form — a metal song and a pop song no longer share
one hardcoded list. A `Part` carries `Energy`, `Feel`, `TempoMul`, `KeyShift`, `Hemiola` and
`BarBeats`, and `RenderSection` publishes them as `_energy`/`_feel`/`_keyShift` before rendering
a bar.

**A `Displace` field is not coming back.** It held a constant tick offset that pushed the chordal
voices late for a whole section, and all three things wrong with it were structural: it shifted
LATE where real syncopation anticipates; it moved the guitar and keys but not the bass, so every
chord arrived as a flam with its own root; and being constant it never re-converged, so there was
no dissonance to resolve. `Hemiola` is the metric device that survives — a figure whose length does
not divide the bar genuinely drifts and comes back.

- **`Feel` is the RHYTHM SECTION's pattern rate, and the tune is exempt from it.** Half and double
  time are a contrast *between* the band and the melody — the kit and the comp change gear while
  the vocal holds its ground, which is what makes a double-time chorus lift instead of sounding
  like the tape sped up. `RenderTune` therefore slices at the nominal rate whatever the section
  says; every other voice (comp, keys, bass, horns, kit) reads `_feel`. A multiplier applied to
  both at once expresses nothing, so a form that only ever changes feel on a section with no tune
  (`MusicGen.SectionSingsTune`) is not using the mechanism — the suite checks that some genre does.
- **Voices read the state; they never ask "am I in a verse?"** Density and level come from
  `NoteGain(tick, vel)` = the cell's velocity × the genre's accent weight for that metric position
  × `EnergyGain(depth)`. Scaling a patch's `Amp` by hand is how the mix got flat and mechanical in
  the first place — route it through `NoteGain`.
- `BarBeats` is the anomalous-measure hook (a 2/4 bar inside a 4/4 section). Bars are laid out per
  section before anything renders, so a short bar is a length, not a special case. **No form uses
  one today**: dropping a beat out of a bar under a melody reads as the song jumping to a downbeat
  early, because the tune is a phrase and the beat comes out of the middle of it. The mechanism is
  sound and it is what the non-4/4 row builds on — what is missing is the melodic half (a tune that
  knows the bar it is sung over is short, rather than one that is simply truncated), so wire a short
  bar back in when that lands, not before. The engine test asserts the MECHANISM (`SectionTicks`
  honours the beat counts), not that some form uses it. Sections are multiples of 4 bars —
  including the ending, which is 4: the old 2-bar ending broke the hypermeasure exactly where a
  clean landing was wanted.
- **Fills are planned per section, in ticks, not "the last beat of the bar".** Length is a weighted
  draw (a beat ≈ 55% … two bars ≈ 5%) and the ≥1-bar options are gated to boundaries that earn them
  (into a chorus, out of a breakdown). The KIT stops where the fill starts; the melodic voices play
  through it, the way a band does.
- **Figures are drawn per SECTION, not per song.** A chorus plays the song's own figure (`_songComp`
  / `_songKeys` / `_songBass` — that is the song's identity, and every chorus must agree); other
  sections draw their own off a stream keyed by section type, so a verse contrasts while both
  verses still match. Drawing once per song meant a two-bar figure really was everything a listener
  ever heard.
- **Comp notes do not ring to the next onset.** `CompLen(span, ring)` caps a chord at two beats and
  a stab at an eighth. Without it, a figure with uneven gaps produced an uneven note *every bar*,
  which is the "short, longggg" shape that read as one repeated cell.
- **Render order is a feature.** Where the bass follows the riff (`RiffBassChance` — metal's pedal
  vs doubling, punk's unison), the chordal voice renders FIRST and the bass reads `_riffOnsets`.
  Both real metal-bass modes are relational, so no table can express them.

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

**A song is CLAIMED before it is rendered, and dropping the claim is what dropping the work means.**
`web/queue.js` holds the one rule the scheduler cannot get wrong: `claimed === queued ∪ in-flight`.
A claim is released only when a render lands, so any code path that discards queued work has to
release its claims with it (`dropQueued`) — a claim with no queue entry and no worker behind it is
permanent, and `want()` will then refuse that index forever. The timeline is walked in order, so one
stranded index stops playback for the rest of the session rather than skipping a song. That is why
the queue is a DOM-free object with its own node test (`test/queue.mjs`, in `make test`) instead of
two collections inline in `app.js`: everything else in the scheduler needs a browser, and this does
not. The same asymmetry applies to workers — `seekTo` terminates only renders that fall outside the
cache window (a Prev/Next is still rendering songs the timeline wants, and a terminate costs a
runtime reboot), while `startSequence` abandons everything. And `activeNodes` is what is still
*playing*: a finished source removes itself in `onended`, because it holds its `AudioBuffer` — a
whole song's PCM — for as long as the list does.

Browsers require a user gesture before audio — `AudioContext.resume()` is gated on the play
button.

---

## Deploy (`make up`) — loopback only, Caddy fronts it

Mirrors the rotaliate/gambit/splitclicker convention: `docker/docker-compose.yml` pins the
compose project (`name: skafinity`, container `skafinity-1`) so it can't collide with the
other repos whose compose file also lives under `docker/`, and publishes

```
127.0.0.1:6970:80
```

**Never bind `0.0.0.0` and never `ufw allow 6970`.** Docker writes its own iptables chains
which are evaluated *before* ufw, so a bare `6970:80` publish is internet-reachable even
with ufw denying the port. Loopback binding + a host-side reverse proxy is the entire
mechanism; the host Caddyfile (unversioned, not in this repo) does TLS and the http→https
redirect. 6970 is skafinity's allocation on that host — `1337`, `5432`–`5436`, `6969`,
`8080`, `8081` belong to sibling services; check the host's Caddyfile before taking a new one.

**Two ways in, same container.** `web/_framework` is committed, so the bundle usually
already exists on disk and there is nothing to compile:

- **`make fast`** (`docker-compose.fast.yml`) — stock `nginx:1.27-alpine` bind-mounting
  `web/` read-only. No build stage, up in a second, and host-side edits to the page/glue are
  live on reload. **The everyday target.**
- **`make up`** (`docker-compose.yml`) — builds the wasm bundle from source in the image
  first (~2 min). For when `MusicGen.cs` / `VibeCodec.cs` / `Exports.cs` changed, or to prove
  the build still works.

They share the compose project, container name, port and `nginx.conf`, so they are
alternatives rather than a pair — starting one replaces the other, and `down`/`logs`/`ps`
act on whichever is running. `fast` guards on `web/_framework/dotnet.js` and fails with an
instruction rather than serving a 404 page if the bundle was never built.

The built image is two-stage: `mcr.microsoft.com/dotnet/sdk:10.0` installs `wasm-tools` and
publishes the bundle, then `nginx:1.27-alpine` serves `web/` plus the freshly-built
`_framework` — **no .NET at runtime**. `.dockerignore` excludes `web/_framework` so a stale
committed/local bundle can never leak into the image, and the Dockerfile re-copies the
canonical `skafinity.config.json` over `web/config.json` for the same reason (it is the
image's equivalent of `make stage`). Because everything is baked in, there are no volumes,
no `.env`, and no secrets.

## Packaging (`make dist`) — two artifacts, both served over http

`make dist` repackages the already-built `web/`; it never compiles anything. It guards on
`web/_framework/dotnet.js` the way `fast` does. **Both outputs are gitignored — commit the
target, never the artifacts.**

**`dist/` — the GitHub Pages payload.** It is deliberately not `cp -r web dist`, for three
reasons and no others:

- **`.nojekyll` — the trap.** GitHub Pages runs the published tree through Jekyll, which
  **excludes directories whose name starts with an underscore**. That is exactly `_framework/`.
  Without the zero-byte `.nojekyll` at the root, the entire runtime is silently missing from the
  deployed site and the page dies at boot on a 404 for `dotnet.js` — with nothing in the build
  log to say so. This is documented Jekyll behaviour, not something observed on a live deploy.
- It drops the `*.br`/`*.gz` duplicates, which a plain static host never serves.
- It re-copies `config.json` from the canonical `sbox-library/Skafinity/skafinity.config.json`,
  so a hand-edited `web/config.json` can never ship. It is the deploy-path `stage`.

**Every path in `web/` is relative** (`href="style.css"`, `fetch('./config.json')`,
`new URL('./worker.js', import.meta.url)`), so a project page's `/<repo>/` subpath needs no
rewriting. Keep it that way — an absolute path is what would break the Pages deploy.

**`dist/skafinity.html` — one file, ~9.5 MiB.** Built by `tools/bundle-single.mjs`. The runtime's
boot config is already embedded in `dotnet.js` (it ends in `withConfig({…resources…})`), so the
only question is where the bytes come from, and `dotnet.withResourceLoader(fn)` answers it. Three
facts that whole design rests on — all `[SOURCE]`, read out of the published `dotnet.js` on
2026-08-01, i.e. implementation detail rather than documented contract, so **re-read them if a
runtime bump breaks the build**:

- For the `dotnetjs` type the loader **must** return a URL *string* (it asserts, then `import()`s
  it) → the two runtime js modules become `data:text/javascript` URLs.
- For every other behaviour it may return a `Promise<Response>`, returned as-is → the five wasm
  assets come from a synthesized `Response` over the inlined bytes. That path skips `fetch`
  entirely, and SRI is only ever applied to *fetch options*, so **no hash check runs** and
  `disableIntegrityCheck` never has to be touched.
- The loader is minified to one- and two-letter top-level names and declares a top-level `var`,
  and `app.js` has top-level names of its own (`n` among them). Concatenating them into one
  module scope is a redeclaration `SyntaxError`, so the loader goes in an **IIFE** (a bare block
  would not contain the `var`) and returns the builder instead of exporting it.

**The workers are why this is not just "base64 everything into app.js".** Each `Worker` boots its
own runtime instance; re-parsing ~10 MB of base64 three times is not acceptable. The worker is a
**blob-URL module** carrying loader + engine + worker glue and *no* assets, and the main thread
posts it the decoded bytes **without a transfer list** — a structured-clone copy, so this realm
keeps its own runtime alive. The worker's boot awaits that init message.

**Every rewrite in the bundler is anchored on an exact source pattern and hard-fails if it stops
matching.** Renaming something in `app.js`/`engine.js`/`worker.js` should break `make dist` loudly
— a silently mis-rewritten bundle is a page that dies at boot for whoever was handed it, and there
is no server log to find it in.

**What `make test-dist` proves and what it can't.** It boots the artifact's own worker bundle
under node, on the real `loadBootResource` path, and renders a song — so the resource wiring, the
synthesized Responses, the concatenation and the asset handoff are genuinely exercised. Two
node-only substitutions are made and are stated in the test: the worker bundle and the two runtime
modules are hosted from `file:` rather than `data:`/`blob:` URLs, because emscripten's and the
loader's `ENVIRONMENT_IS_NODE` branches call `createRequire(import.meta.url)`, which node rejects
for a data: URL. A browser evaluates neither branch. **Untested without a browser:** the real
blob-URL `Worker`, `AudioContext` playback, the DOM wiring in `app.js` (the bundle is only
parse-checked), and any actual GitHub Pages deploy.

## Pages (`.github/workflows/pages.yml`) — the live site

`https://gamah.github.io/skafinity/`, deployed by Actions on every push to `master`. The workflow
runs `make dist` and `test/dist-single.mjs` on a stock runner and publishes `dist/`, so the live
tree cannot drift from what `make dist` builds on a dev box, and a single-file bundle that fails to
boot fails the job instead of being served.

**The job does not compile the engine, and that is the thing to remember.** A Pages runner has no
.NET and no `wasm-tools` workload; installing them would add ~2 min per deploy and would put audio
on the site that came from a build nobody listened to. It consumes the **committed**
`web/_framework`. So a change to `Code/Engine/**`, `wasm/Exports.cs` or `web/engine.js` is live only
once you have run a full local publish and committed the re-staged bundle — the same
bundle-matches-glue rule as ever, except Pages makes forgetting it *invisible* rather than loud:
the site keeps serving the old engine while `master` claims the new one. Page-only edits
(`index.html`, `app.js`, `style.css`, `config.json`) need no publish; push and the site follows.

**The stale-bundle gate.** Because the deploy packages rather than compiles, an engine commit
that forgets to re-stage `web/_framework` breaks nothing visibly — no 404, no failing test, just
the old engine playing under a `master` that claims otherwise. `make stage` therefore writes
`web/.bundle-stamp` (`<kind> <sha256>` over every `.cs` the wasm build compiles, plus the csproj
and `runtimeconfig.template.json`), and `make check-bundle` recomputes it. Both the CI workflow
and the Pages deploy run that check first, so a stale bundle fails the merge and never reaches
the site. `kind` exists because `make dev` stages an interpreted runtime — fresh but slow in a
browser — so the check demands `aot` and a `dev` stamp fails too. **Both `stage` and
`check-bundle` call the same `tools/bundle-stamp.sh`**, which is the point: two implementations
of "what counts as a source" would drift and the gate would quietly stop gating. Add a compiled
input (a new `Engine/` subfolder, another glob in the csproj) and it goes in `compute()` there.
What the stamp proves is that the bundle was staged from these sources; it is a guard against
forgetting, not against tampering, since it sits beside the bundle rather than being derived
from its bytes.

Two properties are load-bearing and easy to undo by accident. The runner must stay
`ubuntu-latest`: standard runners are free on a public repo, **larger runners are billed even
there**. And Pages must stay on the *Actions* source rather than branch-deploy — with branch-deploy
GitHub only serves a branch's root or `/docs`, never `web/`, and it would run Jekyll over the tree
(see the `.nojekyll` trap above). `dist/` is generated and gitignored; nothing built is committed.

## Conventions

- No build framework beyond `make`. `make` → publish + stage `web/_framework`; `make dev`
  skips AOT for speed; `make serve` → `python3 -m http.server` rooted at `web/` (a quick
  no-Docker preview — `make up` is the real nginx-parity host). `make test-engine` → the
  engine-only C# tests (the check that runs on a bare dev host). `make test` → the node
  tests (wasm boundary, page surface, scheduler queue). `make fast`/`up`/`rebuild`/`down`/
  `logs`/`ps` drive the container. `make dist` → the two handout artifacts (above);
  `make test-dist` boots the single-file one.
- **The page must be served** (http), not opened via `file://` — the runtime is a fetched
  bundle, and inlining it does not change that: `dist/skafinity.html` needs http too (module
  scripts and `data:`/`blob:` imports off a `file://` origin). `web/` is self-contained (it
  includes `web/_framework`), so any static server can serve it with the docroot pointed
  straight at `web/`. `web/_framework` is committed so a clone is testable without the SDK.
- Keep `MusicGen.cs` / `VibeCodec.cs` framework-free; web-specific code goes in `Exports.cs`.
- The house-mix config has ONE canonical copy (`sbox-library/Skafinity/skafinity.config.json`);
  `make`'s `stage` step copies it to `web/config.json`. Edit the canonical and re-`make`, or edit
  `web/config.json` directly for quick web-only iteration (the next `make` overwrites it).
