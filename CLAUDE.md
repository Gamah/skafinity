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
| `sbox-library/Skafinity/Code/UI/SkafinityMusicPanel.razor` (`.scss`) | Optional drop-in Razor `PanelComponent` — finds a `SkafinityPlayer` and exposes its knobs as in-game UI (seed/prev-next, genre, per-instrument vibe mixer, mute/volume, reroll, save). s&box-only; not in the web build. |
| `sbox-library/Skafinity/Code/UI/SkafinityTheme.cs` | The panel's palette, derived at RUNTIME from one `Accent` colour so a consuming game can retint a *vendored* copy without editing it. Unset = neutral gray/black. |
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
      SkafinityCommands.cs# s&box-only console commands — the only way to try this target
      UI/                 # s&box-only Razor panel + its runtime palette — outside the glob
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
  lengths, ending style, and the form with each section's energy/feel/key **and which cymbal its
  hand is on**. That last column exists because a listening note about a cymbal is ambiguous three
  ways and the repairs differ: the ride's level, the crash's level, and a section on the hats where
  neither applies. It is drawn per SECTION against the song's ride preference, so nothing about the
  genre or the knobs predicts it — a country song with ride pref 0.11 came back hats on all eight
  sections, which said the ring being complained about was the section-boundary CRASH before a
  single constant was touched.
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

- `-- --stats [N]` is the fourth, and it is the only one that asks about a GENRE rather than a
  song: it sweeps N songs per genre (default 500, ~65 s) and prints how many distinct rhythm
  sections the genre can produce, how many forms, how hard each pair of voices agrees about where
  the beat is, and what shape every tune has. It is a DIAGNOSTIC — not in the section list, not in
  CI, not in blessing — because the questions it answers are only visible across hundreds of songs,
  which is exactly why they survived so long. **Run it before a change and after it, and diff.**

  **The kit has TWO counts and only one of them is a state.** `--stats` prints *planned* kits (the
  kick/snare/cymbal patterns the composer handed the bar loop) and *played* kits (the onsets that
  came out). Played was already 3.8–4.6 per song when the groove was a per-song constant, because
  the per-bar ghost roll and the fill handover sit between the two — a stochastic ghost is not a
  state, and reading that line as one would have declared the whole drums branch finished before it
  started. Planned is the number that went 1.00 → 3.8–5.0. The same trap applies to anything else
  measured "as played": ask whether the variation is a decision or a die.

  **It records rather than re-derives, and that is load-bearing.** Every onset it counts was
  written down by the voice that played it (`Engine/PlanTrace.cs`, attached with
  `MusicGen.BeginPlan( tag, cfg, trace )` and null in every ordinary render). A sweep that walked
  the structure and re-sliced the patterns would be a second implementation of every precedence
  rule in the composer — which figure a bar plays, whether the bass reads the riff, whether a
  section sings a tune, how a sparse section thins its cymbal — and two implementations of that
  drift, at which point the tool lies with a straight face. `_compOrn` cannot be re-derived outside
  a render at all: it is rolled off the rhythm stream interleaved with that voice's own note draws.
  The cost is that a song genuinely composes, so the sweep runs at 8 kHz — every number is a tick
  or a count, so the rate is a straight multiplier on cost and reaches nothing else.

**THE CONVERGENCE RULE, and it governs any work on how the parts fit together.** The failure mode
of every fix in this area is the exact opposite of the defect: parts that ignore each other is
today's problem, and **parts that all play the same rhythm is worse**, and it will not read as a
regression on any single listen. Same for the tune — a wider vocabulary, overdone, makes every tune
reach the same average. So the acceptance rule is always **the distinctness numbers go UP and the
agreement numbers do not move much**, both off `--stats`, before and after. Ska's comp-on-snare
sitting at 14% is the single sharpest tell there is: if the skank starts landing on the backbeat,
something has overridden the genre.

**A suite check that costs a composed song is a budget decision, not a free assertion.** The
harness is render-bound and the structural checks are not — plan-only checks over 240 songs put 35
seconds on a 25-second harness for assertions that render nothing. `MusicGen.DrawForm` is static
and takes its profile for exactly this reason. Prefer arithmetic over the tables; where a check
genuinely needs a composed song, one plan per song and never two (the second is re-testing
determinism, which its own section owns).

**Balancing the mix is a measurement, not a guess.** `dotnet run --project test/engine -c Release
-- --levels` renders every genre with one voice soloed and prints its level in dB relative to that
genre's drums. It reads `MusicGen.RawLevels()` — **pre-master**, because the master bus
peak-normalizes, so a soloed voice measured at the OUTPUT tells you nothing (every solo comes back
at the same peak). Re-measure after changing what a part plays: the `*Balance` values and the
per-genre `Level` entries in `BassTone`/`RhythmGtrTone`/`KeysLevel`/`LeadLevel` are measured
numbers and they go stale when the part they were tuned for is replaced. The suite asserts the
outcome — comp under the kit, lead not dominating, bass present, and silence when every voice is
muted.

**A per-song NUANCE BAND outranks the preset it varies, and editing the preset is a silent no-op.**
The kit draws `_hatTone`/`_footTone`/`_kickTone`/the cymbals per song out of `KitNuance` bands
(`Compose.cs`), overriding the `HatTone.Default`-style presets field by field. So "the open hat
rings too long" is `KitNuance.OpenHatDurMin/Max`, not `HatTone.Default.openDur` — a change to the
latter compiles, reads correctly, and moves nothing a listener hears. **The digests are what catch
it**: a deliberate audible change that leaves every hash untouched has not happened. Treat an
unmoved digest after an intended timbre edit as a failed edit, not as a lucky no-op.

**But `--levels` is ONE SEED PER GENRE, and a single seed varies about 4 dB around its target** (the
suite's own ceiling is written to allow for exactly that). So the tool answers "did this change move
the mix", not "is this voice at 2.0 dB" — a sub-dB gap between two runs is a different song, not a
level. Chasing one is fitting noise and calling it a measurement, and it reads as a measurement
afterwards, which is worse than not having done it. Act on a *mechanism* — a voice that now rings
four times longer, a part that plays twice as often — and size the fix by that mechanism.

**Re-measuring is also not licence to edit the voice that came out wrong.** These are levels
RELATIVE TO THE DRUMS, so rebuilding the kit restates every one of them without a melodic voice
being touched. If the kit moved, fix the kit; a diff that reaches into `LeadLevel` on a drums branch
has stopped being a drums branch. The whole-kit lever for that is `KitPresence`, not the per-voice
balances — and a kit that gets quieter because it started reading velocity and choking its hats has
not regressed, it has started breathing.

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
BeginPlan, Explain, WavFromSamples}` plus all of `VibeCodec`. Keep that surface stable and the
uncompilable target stays safe.

**`Code/SkafinityCommands.cs` is how the s&box side gets tried at all.** It cannot be built here,
so the editor is the only place it runs, and the two things most worth trying there were the two
that needed code written first: the board **ships no launcher** (`skafinity_panel`), and the
accent is a static a game sets at startup (`skafinity_theme #hex`, `clear` to go neutral). The
**`skafinity_spawn` is what makes any of that testable at all**: it builds a player + its own
`ScreenPanel` + the board on a `NotSaved` / `NetworkMode.Never` GameObject, so the library can be
tried in a scene that has nothing wired up — and `skafinity_panel` calls it when there is no board,
so one command works from cold. It never makes a SECOND of anything: an existing board (a previous
rig or the game's own UI) is what it hands back, and an existing player is what the board drives.
That is the rule to keep — a debug command that quietly duplicates the game's own UI over the top
of it is worse than one that does nothing. Shape copied from rotaliate-client's `LocalMusicSystem`,
which builds the same three components for real. The
rest drive the seed (`_seed _next _prev _genre _reroll _save`) and read it back — `skafinity_status`
for the player's state, including whether `skafinity.config.json` actually mounted, which nothing
else would ever say; `skafinity_explain` for the composer's decisions, i.e. the harness's `--seed`
read-out from inside the game. Adding a public entry point for a diagnostic is worth it: the
alternative on this target is guessing by ear, and there is no other way in.

**What the s&box side CAN be checked for mechanically is drift against the engine's own
defaults, and that is the failure this target actually has.** `SkafinityPlayer`'s `[Property]`
knobs are a second copy of a `MusicGen.Config` default — the inspector value wins for every song
that has no vibe — so a knob retuned in `Config` leaves the game playing the old number while the
web plays the new one, silently and with nothing to notice it. It had happened to the whole kit
(hats at 0.22 against a rebuilt, velocity-reading kit whose balance is measured in
`skafinity.config.json`), to `LeadGtrDrive` (3.6 under a knob whose floor is 5), and to
`PanAmount`. A `[Range]` drifts the same way and costs the inspector its reach — `Genre` was
capped at 1 with six genres shipping. So: **a player property that isn't equal to its `Config`
default is this host disagreeing with the shipped song, and it has to say why** — `SampleRate`
is the one that legitimately does. Diff the two lists after any `Config` retune; it is a grep,
and it is the only check this target gets.

**The panel's palette is runtime, not SCSS.** A consuming game vendors this library and must not
patch the vendored copy, so a compile-time `$accent` could never be its colour. `SkafinityTheme`
derives the whole palette from one `Accent` (unset = neutral gray/black) and the `.razor` binds
it as inline `style=` values — the pattern rotaliate/gambit's `WallTheme` already uses, and the
same factors, so a game passing its wall accent in gets the board it has elsewhere. The split
that keeps it working: **the razor sets fills and themed text, the stylesheet owns every border**,
because a `:hover` rule in a stylesheet cannot be relied on to beat an inline `background-color`.
That is why hover feedback is a border rather than a fill, and why the accent must be folded into
`BuildHash` — inline styles only re-render when the panel rebuilds.

**s&box compiles `Engine/` against an API WHITELIST, and that is a second way the shared source
can fail on a build we cannot run.** `(int[])a.Clone()` — the obvious way to copy an array — is
`SB1000: … is not allowed when whitelist is enabled` in the game and compiles perfectly here and
in the wasm build. `MusicGen.CopyOf` is the spelled-out replacement.

**The list is a public source file, and it is per-MEMBER** [SOURCE, read 2026-08-03]:
`sbox-public/engine/Sandbox.Access/Rules/Types.cs` allows `"System.Array*"` and then denies
`"!System.Private.CoreLib/System.Array.Clone*"` on the very next line — a leading `!` is a
blacklist entry that `AccessRules.IsInWhitelist` checks *before* the whitelist, so it wins. There
are only seven such entries in the whole ruleset. So `Array.Sort`, `Array.Empty` and `Array.Copy`
are all allowed and all fine to use; the rule is not "avoid `System.Array`", and guessing which
member is blocked from the shape of the error is how you'd get that wrong. The list changes
without notice — re-read the file, don't trust this paragraph, after an engine update.

Two things generalise past the one member. **"It compiles here" is not evidence about the s&box
target at all** — not for the whitelist, not for the `GlobalGameNamespace` ambiguity below. And
when a convenience method on a BCL type is the only reason to reach for it, prefer the loop; the
engine is small, hot, and already written that way.

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
its `Grooves`, its `BassPatterns`, its `LeadStyle`, its accent weights, how busy and how shaped its
drum fills are (`FillHits`/`FillShapes`), and its `Mix`.
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

**The kit is arranged, so the groove tables are SEED MATERIAL and not what ships.** A section draws
its own groove and its kick and snare go through the same `Arrange` every other voice does. So the
measured placements are real and are still why the tables look the way they do, and the bar a
listener hears is a mutation of one — the same split `FillHits` already draws row by row. What
bounds it is the **spine** (`DrumGroove.SpineOf`), and it is a LAW rather than a per-groove list of
protected ticks: every struck snare, and **every kick on a beat**. A list would be a table
that has to be re-authored whenever a groove is added and is silently wrong when nobody does. **The
cymbal is not arranged at all** — it is the pulse, and country's hat on the "and" is the largest
mismatch the corpus pass found, so it is preserved by construction rather than by a rule that can
be got wrong. The accent weights are untouched and remain measured: velocity was a separate
question off the same pass.

**A KICK ON THE BEAT IS THE PULSE; A KICK OFF IT IS THE PUSH, and only the push arranges.** The
first version of that law protected the bar's *first* beat alone, and the way it failed is the part
worth keeping: beat 1 held at 96–97% in every genre while every other anchor eroded — country's
beat 3, half of boom-chick, went missing in **23%** of bars, pop's beat 4 in 24%, rock's beat 3 in
15% — while the kick COUNT per bar moved by under 3%, so neither a density reading nor a level
reading said anything at all. It reaches a listener as a kick that flickers where the pulse should
be, and the two obvious diagnoses are both wrong and both measurably so: nothing touched
`RenderKick`'s gain, and its agreement with the bass went *down*, not up. **Suspect PLACEMENT before
level when a part feels weaker and the mix did not change.**

The law is read **both ways, and it has to be**: an onset on a beat may not be dropped or moved,
*and* one may not be added or displaced onto a beat. A groove's identity is partly where it does
NOT play — the one drop IS the hole on beat 1 — and a rule phrased only over existing onsets cannot
say that; stopping `Add` alone left ska's beat 1 three points over baseline by way of `Displace`.
The one route deliberately left open is `Recombine`, which substitutes a whole bar of another
groove from the *same genre's table*: a one-drop section taking a bar of steppers is vocabulary,
not erosion. The suite asserts the invariant — "an arranged kick never loses a pulse" — against the
groove the SECTION drew, which now varies per section.

**The grooves and the accent weights are measured too, off a different kind of source.** There is no
equivalent of a bpm field for where the kick falls in a train beat, so the drum tables are fitted to
a corpus of played performances instead: Google Magenta's Groove MIDI Dataset, whose every file is
played to a metronome at a stated tempo and carries per-hit velocity. **One pass answers two
questions and they must not be confused** — VELOCITY is the accent weight (`GenreProfile`'s accent
block) and OCCUPANCY is the placement (`DrumGroove`'s header, and `GenreProfile.FillHits` for how
busy a fill is). Both blocks record the method precisely enough to redo it, because **neither the
dataset nor the tool that read it is in this repo and neither may become a dependency**: what lands
is derived numbers and a citation.

Two standing caveats travel with any figure taken from there. **The sample sizes are wildly uneven**
— rock is 6521 bars and settles rock, country is 120 from two performances and is an indication —
so a thin row says so rather than trading a guess for a guess with a citation on it. And **metal is
simply not in the dataset**; its flat accents and its fill density are design calls and are labelled
as such, not rock's numbers wearing rock's citation. E-GMD is the candidate that would answer it.

**The dataset also answers questions nobody thought were questions, and that is what it is for.**
The hi-hat pedal is the case: a riding section had no hi-hat at all, and "the foot goes down on 2
and 4" is standard enough received wisdom that it went in as a comment written in the same voice as
the measured tables around it. It is a third of the truth. Over the 472 4/4 performances, split by
whether the ride carries the pulse, the pedal (GM 44) plays **2.74 hits per bar while the hands are
on the ride** against 1.92 while they are on the hat — the direction is right — but 2 and 4 are only
the two PEAKS (16.2% and 17.9% of hits), the downbeat takes 13.1%, and every other eighth still
carries 9.6–11.6%. `FootOccupancy` in `Groove.cs` is that distribution. **The lesson is about the
prose, not the pedal:** this repo's comments state measurements, so a sentence in that register is
read as one, and writing an assumption there launders it into a citation. If it was not measured,
say which it is — the way metal's `FillHits` does.

**A MEASUREMENT IS NOT A SPELLING, and the cymbals are the case that proves it.** Real cymbals were
analysed and the analysis reduced to three laws (see `CymbalBands`). The first attempt spent those
laws on a mode forest — ~390 partials, each with its own ring time — and it was accurate and
unusable: ~250 ms of CPU a hit, and it **out-detailed every other voice in the engine by two orders
of magnitude**. The rest of the kit is two or three sines and some filtered noise, and a voice built
to a different standard does not sit in that mix at any level. That is the part worth carrying: a
fidelity mismatch is not a level problem, and it will not respond to one — a thing that is too loud
gets quieter, a thing that is too *real* just becomes a quiet hyperreal object in a synthetic mix.
Level, stereo width and pattern density were all tried against it first. The same laws now cost
thirteen components: seven filtered-noise bands whose ring times fall as 1/√f, one low beating pair,
splash and wash. **Both failure modes are worth keeping in mind because they bracket the target —
uniform-decay noise reads as a hi-hat, resolvable partials read as a church bell**, and per-band
decay is the property that sits between them.

**"A beat is not a pitch" is wrong, and it is the exact form the church bell comes back in.** The
measurement resolves the cymbal's lowest partials as near-pairs a few Hz apart, and keeping one such
pair as two real sines looks like a faithful reading of it — the argument being that what is heard is
the BEAT rather than a note. It is a note. A 232 Hz sine ringing two and a half seconds under noise
bands that decay far faster is the most exposed thing in the voice, and it reads as a tone sitting
inside every cymbal. **No tonal component survives in a cymbal at any level**; where the bottom is
wanted, the band set reaches down and carries it as noise like everything else. `--cymbal` +
`tools/spectool` is how to check rather than argue: a healthy cymbal here resolves NO sustained
partials, which is also what the reference recordings measure.

**Two things about a ride are the PATTERN, not the voice**, and both survived every timbre change:
the engine played eight even strokes a bar separated only by the genre's accent weight, which is a
wall however good each stroke is (a drummer's "and" is a much lighter stroke — `RideStroke`); and a
stroke train ACCUMULATES unevenly, because ring time falls with frequency. At riding eighths the
250 Hz band stacks +7.6 dB over a single stroke against +2.4 dB at 5 kHz, so the low ring runs away
and reads as a drone. A flat level cut cannot fix that — it takes the attack down with the drone.
The fix is physical: a stroke landing on a ringing cymbal excites it AND damps it, because the stick
is on the metal, so it is a shorter decay for as long as the cymbal is being played
(`CymbalBands.RestrikeTau`) and it compounds over a train the way the physics does. **Suspect the
pattern before the timbre.**

**A long-ringing voice is synthesised once and stamped, not rendered per hit.** A 2.5-second ring
struck eight times a bar overlaps itself twenty deep, so per-hit rendering pays for all of it — that
is a property of the pattern and no amount of making the voice cheaper removes it. Synthesise the
object once per song into a lo/hi pair (split at 2.5 kHz, so a soft stroke can be DARKER and not
merely quieter) and add it per hit. Round robins cost about three milliseconds each at this
complexity, so the repeat-tell is cheap to break.

**A measured ring length is a cymbal in a ROOM, not a cymbal in this mix.** A crash lands at every
phrase end and every section start here, and at that density the measured three-to-four-second ring
never clears before the next one — the arrangement swims. `CrashRingScale` keeps that departure as
one explicit number rather than a quietly re-fitted constant, so the law stays legible and the mix
decision stays a mix decision. The ride has no equivalent and did not need one.

**A CYMBAL BUILT ON τ=k/√f ALWAYS DARKENS AS IT RINGS; A HI-HAT NEVER DOES.** The hat is one
high-passed noise with one decay, so it measures 12.5 kHz at the attack and still 12.4 kHz half a
second later. The ride's low bands outlive its high ones by construction, so it measured 10.4 kHz
falling to 8.8 kHz — the voice carrying the pulse was the DARK one against a hat that never moves,
and it got darker the longer it rang. Two levers, and they do different jobs: `RingTau`'s `knee`
stops the low tail dominating (it is what keeps the tail from darkening), and the strike bump is
where the energy sits in the first place. **The bow's bump is a mix decision, not the
measurement** — the reference puts a real ride at ~1.2 kHz, which is both darker than the hat and
exactly where the guitars are, so it sits at 4200 Hz instead. Measured on a song rather than a dry
hit, that took the ride from +4.74 dB to +1.98 dB over the rest of the mix in the 1–3 kHz band while
keeping it present at 6–14 kHz: **moving a voice OUT of a crowded band beats turning it down**, and
the two are easy to confuse because both make it less annoying.

**A RIDE AND A CRASH DIFFER BY THEIR TAIL, NOT BY THEIR SPECTRUM — and that is a trap, because
the band weights are nearly identical and look correct.** Measured per band as the time to fall
20 dB, the ride against the bright crash was low 2.80 s vs 1.00, mid 2.53 vs 0.89, upper-mid 2.03 vs
0.87, top 1.53 vs 0.81, while the two spectra's weights sit within a dB of each other (mid −8.4 dB
vs −8.5). A voice whose balance is a crash's and whose tail is 2.8× longer IS a crash that will not
stop, and it reads as one wherever the arrangement gets sparse enough to expose it. `RingTau`'s
`knee` is the in-model lever — it shortens the tail below a corner without touching the stick attack
— and the crash had always carried one while the ride carried none, so the ride's mids rang at the
full τ=k/√f. The listening report that found this was precise in a way the measurements were not:
"the MIDDLE of the noise of the ride feels like a crash when the mix is sparse". **Compare a voice
against its own family before reaching for its level**; the ride had already been through a +10 dB
boost and a ring-scale attempt, and neither was the thing that made it the wrong instrument.

**Measure a voice's dominance as LEVEL-IN-ITS-BAND plus DUTY CYCLE, and never as whole-mix RMS.**
"A cymbal is overpowering everything" was chased through the bass and the reverb because muting the
ride moved whole-mix RMS by 0.77 dB — RMS is owned by the low end and says nothing about a
broadband voice sitting on top. Band-limited to >2.5 kHz, where nothing else in a ska arrangement
lives, the same ride was **+6.9 dB over the entire rest of the mix** and held that band within 12 dB
of its own peak **43% of the time**. Two readings, and they answer different questions: the first is
"is it too loud", the second is "does it ever stop". Take both before touching a constant.

**A level set by ear against one balance cannot see the other one.** `StrokeLevelRide` went 0.30 →
0.95 in a single +10 dB step to stop the ride being buried, and its own commit message flagged the
result as worth watching. It was: 0.30 measured +1.4 dB over the rest of the mix (the buried it was
aimed at) and 0.95 measured +6.9 dB. "Can I hear it?" and "is it now the loudest thing in its band?"
are both real questions and ear-tuning one answers neither of the other. **It sits at 0.45**; the
+3.65 dB / 21% figures were measured at 0.55, before the bow's strike bump moved to 4200 Hz and the
level came down with it, and they have not been re-measured since. What that move is worth is a
band reading rather than a level: on the song, over the rest of the mix, the ride went +4.74 →
+1.98 dB at 1–3 kHz while only +7.03 → +5.54 dB at 6–14 kHz. **Shortening the ring was tried first and is the wrong lever** — it bought 1 dB
against the level's 3.3 and left the ride still on top, which is what "suspect the pattern before
the timbre" looks like when the answer is actually neither.

**`--cymbal [dir]` writes one dry hit per cymbal for `tools/spectool` to re-measure**, and a
spectrum fitted to a measurement is not fitted until the RESULT has been measured the same way. It
is the only check on these voices that does not need ears. **`--render vibe:tag:n [path]`** writes a
whole song as a WAV — the mix as it ships, master bus and all, which is the one thing the audition
deliberately is not, and the only way to answer "is this voice too strong" on a host with no
browser.

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

**The ride used to run through `HatBalance`** — it was built as the cymbal HAND replacing the hat
rather than as an instrument with its own bus, so they shared one number. They are not one
instrument to mix: a hat is short, high and continuous, a ride rings, and "the hats are too loud"
is then unanswerable without moving the ride too. `RideBalance` is now its own advanced field,
seeded from `HatBalance`'s value so the split changed nothing on its own. **The general shape is
worth keeping**: when a voice was built as a variant of another one, it inherits that one's bus by
accident rather than by decision, and the first listening note that separates them is when you
find out.

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
they live today: a fill's `Pickup` shape flurries its last beat at 32nds, `RenderShredPhrase` breaks its
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
- `Melody.Draw` writes **call and answer**: the answering phrase repeats the call's rhythm exactly
  and lands the line. The rhythm is drawn before the pitches for that reason — a fresh random phrase
  never sounds composed however good its notes are. Two of those pairs make a PERIOD (below).
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

**A TUNE IS A PERIOD, AND THE CALL/ANSWER PAIR IS WHAT IT IS MADE OF — not what it replaces.**
`Melody.Draw` writes four phrases: an **antecedent** (call, answer) that leaves the line open, and a
**consequent** (call, answer) that closes it. Two phrases was the whole of a tune's structure before
this, and the period was binary all the way down — punk and pop landed on a 2-bar rhythmic cell,
stated twice to make the tune, looped to fill an 8-bar section and repeated identically at every
chorus, i.e. one 2-bar rhythm heard eight times in the most exposed voice in the mix. Four things
hold it up:

- **The half cadence is what makes the consequent necessary.** The antecedent lands on a chord tone
  that is *not* the tonic (`Melody.HalfCadence` — the fifth mostly, the third otherwise). Resolve it
  home and the period stops being one thought; it becomes two short tunes end to end, which is the
  old shape wearing more bars.
- **A new rhythm enters a tune at the consequent's call or nowhere.** An answer repeats its own
  call's rhythm — that is the machinery that stopped tunes sounding improvised and it is untouched —
  so `Melody.PeriodShape` is the entire variation budget: `Parallel` restates the call and varies
  only how it is answered, `Varied` keeps its rhythm and sings a fresh contour, `Contrasting` brings
  a phrase of its own. **The weights are authored, not measured** (there is no melodic corpus here),
  and they are one table rather than six because nothing found says a genre has an opinion about it.
- **The tune is a WHOLE NUMBER OF HARMONIC CYCLES, capped at 8 bars.** The cap was never about
  length — it is that a tune's statements must land over the chords they were drawn against, and two
  cycles do, bar for bar. So punk and pop (`ChordBars` 1 × a four-chord progression) double a 4-bar
  cycle into an 8-bar period instead of being stuck with two phrases; a tune of 1.5 cycles would
  still be the defect the clamp exists for. Eight is the ceiling because a section is eight bars, and
  a tune longer than the section it is sung in never finishes.
- **A phrase is at least two bars** (`Melody.MinPhraseBars`), so a tune shorter than four of them is
  a plain call and answer and says so. Four one-bar "phrases" is a tune restating itself every bar,
  which is this same defect arriving from the other direction.

`--seed` prints whether a song's tune is a period and how long a phrase of it is; `--stats` prints
**distinct phrase rhythms and contours WITHIN one tune**, which is the number this moved — 1.00 and
2.00 by construction before, 1.28–1.34 and 3.36–3.47 after. Its across-song `distinct` counts are
measured over the CALL, so they shrank for the four genres whose call went from four bars to two
(a shorter string collides more often); punk and pop, whose call was already two bars, are
byte-identical on that line. The band's own cohesion numbers moved by at most a point, which is
what the convergence rule asks of a change confined to the melody. Cross-genre onset overlap rose
for the punk/pop pairs (21% → 45%) because their tunes are twice as long on the same grid; the
identical-tune rate stayed 0%.

**A section shorter than the tune sings the tune's END, not its beginning.** `RenderTune` pulls the
anchor back by the difference, so the section's last bar lands on the tune's resolution. A four-bar
pre-chorus over an eight-bar tune otherwise stated the call and was cut off by the chorus before
the answer arrived — a phrase interrupted by the next phrase, which is what "two ideas at once"
sounds like from the outside.

**A TUNE'S RHYTHM IS BUILT BEAT BY BEAT, AND THE ACCUMULATOR MUST RE-ANCHOR.** The note lengths
are a weighted menu (16th, 8th, dotted 8th, quarter, dotted quarter, half — all exact at
`TicksPerBeat = 48`), and running that menu as a free `t += len` accumulator is the single worst
thing that has been done to this engine. A dotted eighth or a sixteenth shifts **every remaining
note of the phrase** by a non-beat amount, permanently: the line rotates against the bar and never
comes back, which is a 3-against-4 running for eight bars rather than a melody. It reads exactly
as it is — the lead out of time with the band, and disjointed. The rule is the one a player reads
off a stave: **inside a beat you may only play what fits the rest of it**, so a dotted eighth is
followed by a sixteenth and the next beat starts on the beat. Notes still land on the "and" and on
sixteenths; what they cannot do is drift.

**And this is the case that shows what a proxy metric costs.** `--stats` reports "off the eighth
grid %", the plan said it should "become non-zero", and it went 0% → 45%, which was recorded as a
success. 45% is not "non-zero", it is the tune no longer relating to the beat — the number went
the right direction for entirely the wrong reason and no other check could see it, because
`--grid` only asks whether a voice is on the song's TICK grid and a drifting line is. It sits at
14–32% now (metal highest, punk lowest, which is what the vocabularies say it should be). **A
sweep number is a proxy; when one moves a long way, ask which mechanism moved it before recording
it as the fix landing.**

**THE CONTOUR IS NOT A RANDOM WALK, AND THREE SEPARATE THINGS KEEP IT FROM BEING ONE.** The line
used to step or leap at random inside a `Clamp`, so 12–18% of every tune's notes sat pinned against
the range ends and the only thing that ever brought a phrase home was the forced tonic on the last
note — a landing with no approach to it. What is there now: **post-skip reversal** (a melody that
leaps comes back, which is also what makes a leap read as a gesture rather than as the line
relocating), the **melodic arch** (phrases rise then fall on average), and **tessitura**, a pull
toward the middle of the range.

**The last of those is the one that actually keeps a tune off the ends, and that is the part worth
knowing.** Reflection instead of clamping was the obvious repair and it is only a BACKSTOP: it
stops a line PARKING at a boundary, but a walk with no centre still spends its time out there, and
the arch makes it worse by leaning uphill in the first half of every phrase whatever the register
already is. Reflection alone took pinning to 5.5%; the centre pull took it to 2–4%. They are
different jobs — one decides where the line lives, the other decides what happens when it arrives
at an edge anyway — and a session that reads only the pinning number will conclude reflection did
it.

**The genre's tune vocabulary is `GenreProfile.Tune`, and every number in it is AUTHORED.** Note
lengths (a weighted menu, 16th through half), how often it rests, how often it leaps, how it
answers its own call. There is no melodic corpus in this repo the way there is a drum one, so
unlike the accent weights and the groove placements these are design calls — **do not write them
up as if they were measured.** A rest is an OMITTED ONSET rather than a cell: the previous note's
span grows and `RenderTune`'s two-beat cap turns the remainder into silence, so the renderer needs
nothing. A `Melody.Rest` CELL would be read as a degree and sung.

**A RANGE AND AN AMBITUS ARE TWO BOUNDS AND ONE NUMBER CANNOT BE BOTH.** `Melody.DegreeMin/DegreeMax`
(`-2..9`, twelve scale degrees) is the AMBITUS — how far a whole tune reaches — and it used to bound
a single phrase as well, which is a whole-song figure doing a phrase's job (a tune here is 2–8 bars;
the large-scale pop-melody work measures range on a rolling two-bar window for exactly that reason).
`Melody.PhraseSpan` is the second bound: eight degrees, an octave, drawn per phrase as a WINDOW
around the note that phrase opens on. Inside a phrase the walk reflects off the window and the
tessitura pull centres on it; the ambitus is the outer wall. A tune's wider reach then comes from
its phrases sitting in DIFFERENT registers rather than from any one of them wandering.

**The window bounds where a line WANDERS, not where it may be PUT** — `Answer` transposes a call
bodily (`SequenceUp2` takes it up two degrees) and reflects off the ambitus, because folding a
sequence back into its call's window would flatten the one gesture in a tune whose point is that it
goes somewhere else. The window is drawn around the opening note rather than the opening note being
folded into a window drawn first, so `Opens`' weighting toward the tonic and the fifth survives.

**Both numbers are authored and the measurement is of the ENGINE, not of the music.** No source found
gives a phrase-range figure for this roster's genres, so the published pop figures are cited as the
reason for the SHAPE and never borrowed as a value. What `--stats` reports is what the engine does
with it: the phrase/tune split went 5.3–6.4 / 8.1–9.3 degrees to **5.1–5.9 / 7.8–8.7** across the six
genres. Note what that says — the arch and the centre pull already held the average well inside the
ambitus, so **the window is a ceiling on the tail rather than a re-centring**, and a session expecting
the mean to move a long way has misread which mechanism does which job. Distinctness is unmoved
(within-tune contours 3.41–3.47, distinct rhythms per genre identical), which is what the convergence
rule asks of a change confined to the melody.

**Two genres landing on a similar tune at one seed is a FEATURE and must never be engineered away.**
The genre is in the tune's streams so that they are not RELIABLY identical (rock and country used
to return byte-identical melodies 53% of the time), and it is in no other stream: the song stream
carries none, so "the same song, in another genre" — same key, same kit, same pan — still works.
There is deliberately no suite check that two genres differ; the collision rate is something
`--stats` reports. A preference turned into a prohibition is how a session ends up writing code to
invent divergence nobody asked for.

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

**A player bends TO a note, not BY an interval.** This is the same fact about seven-note scales
that `VoicedTone` exists for, arriving through the melody instead of through a chord: the string
lands on the next tone of the scale, which is a whole step in some places and a semitone in
others. A bend of a fixed depth therefore lands *off the key* on every degree whose step is the
other size — a whole step off the third or the seventh of a major scale, a semitone off almost
anywhere — and it is worst on a bend that is HELD, because the note then spends its whole tail
outside the key rather than passing through. That is what "out of tune" sounds like when nothing
has been mistuned. `Harmony.BendSemis(scale, pc, depth)` picks the note; `Expression.BendDepth`
stays the instrument's *preference* — how far the hand reaches, which is the thing a genre has an
opinion about — rather than the distance the pitch travels. For the same reason a bend is **mostly
released**: a held one replaces the composed note, and the tune's pitch was chosen against its
chord, so a hook that bends away and stays there is a different hook every statement.

**How OFTEN is a weighting, never a floor.** Country ran at `max(knob, 0.45)` — at least 45% of
every note long enough to carry a bend, with no position on the slider at which the genre bent
rarely — and it read as exactly that: the right gesture, too often. A flat per-note chance cannot
say *where* a player bends, so a smaller constant would only have been a quieter version of the same
mistake. `MusicGen.BendBias(spanTicks, phraseU)` multiplies note LENGTH by PHRASE POSITION, and the
position is read over each **half** of the phrase: call and answer land twice, so the end of the
call is as much a landing as the end of the answer, and reading the whole phrase would put the
flattest point of the curve on the most bent note in a country lick. Every voice that can bend
passes it. What a genre's floor is actually for survives — its low end must still read as country
rather than as a clean guitar — it is just no longer the only thing the number says.

**Not every lead that routes to `"LEAD GTR"` is a guitar.** `Expr` picks that case for everything
that is not the ska horn section, so pop's plucky synth lands there by elimination — and a synth
does not bend a string. Pop's vibe knob is even labelled `GLIDE` rather than `BENDINESS`, so
letting it buy a bend would make it the one knob in the toy whose label names a different gesture
from the one it performs. Check the genre's own row in `VibeCodec` before adding a gesture to that
case's `_ =>` default.

---

## One authority arranges the section (`Engine/Arrange.cs`)

Every voice used to pick its figure from its own small authored table and no voice knew what any
other was playing — pop's pad landed on the kick 100% of the time and ska's skank 1%, and neither
number was decided by anyone. The tables were also a hard ceiling: the whole rhythm section was the
product of three table sizes, so punk had **12** distinct states over 500 songs and randomness
cannot reach past a table size.

A `Skeleton` per section, on the sixteenth grid, **derived and never drawn**: the accent grid comes
off the groove's kick and snare and the genre's measured accent weights, so **the kit stays the
reference the arranger writes against rather than another client of it**. Plus the phrase seams,
the tune's occupancy, and an occupancy layer that fills in as each part is placed — bass first (its
job is to agree with the kick), then the comp (which can now see the bass and the tune), then the
keys. That order IS the design; arranging them independently against a fixed grid gives every voice
the same answer, which is the failure the convergence rule watches for.

**The authored figures are seed material, not a ceiling.** Drop an onset that fights what is there,
add one on an accented free cell, displace one by a cell, recombine a bar from another figure in
the same genre's table, or quote. **Every mutation stays inside the genre's allowed `CellClass`**,
and that is the property the whole design rests on — it is what keeps ska's skank offbeat by RULE
rather than by table and punk's downstrokes on the beat, and it is the reason the arranger cannot
simply write onsets wherever the accent grid is loud. A genre's identity is WHERE it plays; its
arrangement is which of those places it uses this time.

**"Every chorus agrees" and "the chorus quotes the table" are different claims, and only the first
is true.** The chorus is arranged ONCE and cached as the song's part; every later chorus replays
that line. Making the chorus quote the table instead would leave the song's own rhythm section as
one entry out of three, which is the ceiling this exists to break — and the choruses are most of
what a listener hears as the song. **The LOUD figure is arranged too** (`LoudCompRole`), or genre
0 — whose chorus is punk downstrokes rather than a skank — keeps its most recognisable bars coming
out of a table of two.

**The voices are not clients of the skeleton.** The arranger REPLACES the section's figures with
arranged versions of themselves and every voice goes on slicing the figure it was handed. Putting
the arranger's rules in six renderers would let all six disagree about what the section is — the
same argument `PlanTrace` makes about re-deriving the plan. For the same reason `_riffOnsets`
stays: retiring it means the bass reading the *planned* comp line, and the planned line is not the
played one until the per-bar precedence (hemiola, then loud, then the flourish) has run.

**An arranged figure still has to ASCEND.** `Pattern` computes each onset's span from the next
onset, so one out-of-order tick gives a negative span clamped to 1 — every note in that figure
becomes a one-tick blip, and nothing anywhere reports it. `Recombine` did precisely that by mapping
a two-bar source figure's second bar back onto its first with `tick % barTicks`. The suite checks it
now, on songs it was already planning — over the kit's patterns as well as the band's.

**WHO WRITES FIRST IS A PER-SONG DRAW, AND THE SIGN OF THE KICK'S `Complement` IS THE WHOLE
DIFFERENCE.** Three of the four things the skeleton carries — seams, metre, the tune — do not need
the kit, which is what makes both orderings one mechanism: a **leading** kit arranges against those
three and the band then answers its accents; a **following** kit arranges last, against what the
band actually took. `Score` reads `Complement` as a push AWAY, which is right for every melodic
voice and for a leading kick. A following kick is the opposite gesture — the riff is already on the
grid and a drummer playing to it lands *with* it — so `MusicGen.KickRole()` negates it. Built with
the same sign on both sides, the two modes came out agreeing to within a point, which is a
mechanism that costs a draw and buys nothing; `--stats` reports cohesion **split by which mode the
song drew**, because averaging two mechanisms describes neither. Note the honest limit: with the
sign right they still differ by under two points on whole-song agreement, because one mutation a
section cannot move a bulk percentage. The number is not where this shows.

**Energy reaches what the kit PLAYS through `KitBias`, which shifts weight between DROP and ADD
without changing what the draw costs** — so a quiet section is likelier to lose an onset and a loud
one to gain one, and `DrumBusy`/`DrumTone` feed that decision rather than multiplying its output. A
quiet section **in normal time** thins to the spine alone, and the `_feel >= 1` gate is the
mechanism rather than a caveat: half time is already a pattern rate, so a breakdown that thinned
twice would be a hole in the arrangement. Every `Breakdown` in every form is half time, which is
why this fires on the INTRO — and which makes it one threshold away from being unreachable, so the
suite asserts that it fires at all.

**Counting states on figure object IDENTITY stopped working here.** It answered exactly the right
question while a figure could only ever be an entry in a static table, and answers nothing once a
song can arrange one, since every arranged figure is a fresh object whether or not it differs.
`--stats` hashes CONTENT. Counting the whole song's onsets instead is worse than useless — it reads
~500 of 500 both before and after, because the non-chorus figures already varied per song, so it
measures how many songs there are.

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
  `NoteGain(vel)` = the cell's velocity × `EnergyGain(depth)`. Scaling a patch's `Amp` by hand is
  how the mix got flat and mechanical in the first place — route it through `NoteGain`.
- **The metric accent is a DRUMS thing, and `NoteGain` takes no tick so that it stays one.** The
  genre's `AccentDown`/`AccentBack`/`AccentOff` were measured off drum-hit velocities, and they
  used to multiply into every voice — so a pitched note's level was decided by where in the bar it
  landed. A melody is drawn on the eighth grid and therefore alternates on-beat and off-beat
  continuously, so the lead stepped 3 dB a note in rock and 5 dB in pop out of metric position
  alone: a drummer's dynamic worn by a singer. `KitGain(tick, vel, depth)` is the only door out of
  `MetricGain`, and a pitched voice cannot open it — the same trick as `Register(octaves)`, where
  the wrong version is made unwriteable rather than merely documented. **A phrase-shaped dynamic
  for the melody is a different and real thing**; it comes off the tune rather than off the grid,
  so it belongs in `Melody` and it is a `PLAN.md` row.
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
- **A fill is a SHAPE, a DENSITY and a set of DYNAMICS, and one number could only ever be the
  middle one.** `RenderFill` used to play *n* evenly-spaced, equally-loud hits in every beat of its
  span with a floor of four, so a two-bar fill was 32 hits that never stopped — not fast, just never
  stopping. The three parts now: **shape** (`FillShape` — a ramp that empties and accelerates, a
  roll, a pickup that waits and then flurries, a gesture that is three hits and air; weighted per
  genre, and a fill longer than a bar takes a ramp or a roll whatever it drew, because the kit has
  handed over and nothing else is keeping the time). **Density** (`GenreProfile.FillHits`, in hits
  per bar across the whole kit, against a measured rock fill's 13.2; the per-cell occupancy grid is
  eighths at 1.0 with sixteenth ornament at 0.62, which is what the dataset's histogram is, and a
  target becomes chances by a **water-fill rather than a scale** — the beats reach certainty first
  and what a busier drummer adds is ornament). And **dynamics**: it goes through `NoteGain` like
  every other voice, which it uniquely did not before. `FillCells` fixes the per-beat draw count
  across all three grids so the TRIPLET knob cannot change how much of the stream a fill spends.
- **SHORT SONGS ARE THE POINT, and this is where a future session will go wrong.** This is a game,
  not a record: the toy streams endlessly while somebody plays, so the song boundary is WHERE THE
  VARIETY ARRIVES — twice as many songs is twice as many keys, tempos, grooves and tunes, and it is
  also less decoded PCM held by the web player. Published genre averages (punk ~2:46, pop 3:00–3:30,
  country 3:13–4:00, thrash ~5:00) put every genre here well under its own figure **deliberately**;
  that research is real and it answers a different question. So the drawn details SHORTEN — a
  dropped optional section, a cut final verse — and the verse is one fixed length. The only detail
  that lengthens is the repeated outro chorus, kept because it is idiomatic and made rare for
  exactly this reason. If length ever does want to grow, `{ 8, 16 }` is the musically correct verse
  set (over 90% of sections in this music are 8 or 16 bars; 12 is the blues form and belongs to
  country and blues-rock; a 4-bar verse is not a verse).
- **A genre's form is a FAMILY, and the song's form is drawn once into `_form`.**
  `GenreProfile.Forms` holds 2 authored variants per genre with weights; `MusicGen.DrawForm` picks
  one and draws the details over it (verse length, whether the optional section appears, whether
  the key lift happens, a repeated final chorus, a truncated last verse). Authored variants are
  what keeps them all punk — **randomising a form is the wrong fix**: a form is genre identity,
  which is why it lives in `GenreProfile` at all, and punk with a 16-bar solo is not punk.
  **Caching is not an optimisation.** Five places used to derive the form from the genre alone —
  the composer plus four diagnostics — which was harmless while the answer was a constant and is a
  bar ruler for a DIFFERENT SONG the moment it varies. `--score` and `--grid` would go quietly
  wrong exactly when they are most needed. Everything reads `_form`.
  **A doubled final chorus is a repeated SECTION, never a longer one**, because every chorus must
  still agree about its length; and a truncated verse still lands on a multiple of four, because
  the hypermeasure is why the ending is 4 bars and not 2 and it does not stop applying to a drawn
  length. The suite asserts all of that over drawn forms.
- **Figures are drawn per SECTION, not per song — the groove included.** A chorus plays the song's own figure (`_songComp`
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

**A fade UP FROM SILENCE is not a crossfade, and must not borrow its length.** A crossfade is long
because two songs have to trade places without either being heard to stop; nothing is being traded
at the start of a session. `SkafinityPlayer` used one number for both, and what a multi-second linear
ramp does to a drum kit is specific rather than merely quiet: it crushes the STRIKE and lets the RING
arrive at full level a second later, so every cymbal in the opening bars sounds like it was hit
before the song started. That is the diagnosis for "the song starts with a cymbal already ringing" —
and the tell that it is a HOST bug rather than an engine one is that the engine's own render of the
same seed starts on a clean attack (check it with `--render` and look at the first few milliseconds,
not at a block envelope, which cannot tell an attack from a mid-decay start).

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
and `runtimeconfig.template.json`), and `make check-bundle` recomputes it. The CI workflow and
the Pages deploy both run that check first, so a stale bundle fails the merge and never reaches
the site.

**CI runs on PULL REQUESTS and `preview/*`, deliberately not on every branch push.** Re-staging
the bundle is a ~2-minute AOT publish that belongs at the end of a piece of work, so a feature
branch's intermediate commits are EXPECTED to carry a stale one — running the gate on them mails
the repo owner about a state that is not a defect, and a gate that cries wolf stops being read.
Nothing reaches master except through a PR and nothing reaches the live site except master or a
`preview/*` branch, so the gate still covers everything it was written to cover. If you want the
answer earlier on a WIP branch, run `sh tools/bundle-stamp.sh check` locally — it is the same
script CI calls. `kind` exists because `make dev` stages an interpreted runtime — fresh but slow in a
browser — so the check demands `aot` and a `dev` stamp fails too. **Both `stage` and
`check-bundle` call the same `tools/bundle-stamp.sh`**, which is the point: two implementations
of "what counts as a source" would drift and the gate would quietly stop gating. Add a compiled
input (a new `Engine/` subfolder, another glob in the csproj) and it goes in `compute()` there.
What the stamp proves is that the bundle was staged from these sources; it is a guard against
forgetting, not against tampering, since it sits beside the bundle rather than being derived
from its bytes.

**A branch can be previewed live, and while it is, the site is not `master`.** `pages.yml` carries
`workflow_dispatch`, so the Actions UI offers a ref picker; what decides whether the `deploy` job
may then run is the `github-pages` environment's deployment branch policy, and it admits `master`
and `preview/*` and nothing else. Push a branch named `preview/<something>`, dispatch the workflow
against it, and that build is the live site. **The prefix is the whole safety** — an unrestricted
policy would let any branch that ever acquires a Pages trigger replace the site, so a preview has
to be named as one.

**There is ONE Pages site per repo, so a preview REPLACES the live site rather than sitting beside
it**, and it keeps serving until `master` next deploys. `concurrency: group: pages` with
`cancel-in-progress: false` queues rather than cancels, so the next push to `master` restores the
site instead of racing the preview; nothing else has to be done to put it back. The consequence to
carry: **the live site can legitimately be a branch, so "the site does not match `master`" is not
by itself a stale-bundle bug.** Read the deployment log for the ref that last deployed before
reaching for `web/_framework` — a live preview and a forgotten re-stage look identical from the
outside.

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
