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

- `-- --seed tag:n[:genre][:vibe]` prints what the composer decided for that seed: the decoded knobs, the
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
- `-- --score tag:n[:genre][:vibe] [fromBar] [toBar]` prints the SCORE: every voice's onsets over a range of
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
JS↔wasm boundary (generation, vibe round-trip, WAV output). That part needs `web/_framework`, so
it only runs where the bundle has been built. **Four of the six files touch no wasm at all and
run on a bare checkout**, because what they check is a state machine rather than a sound:

- `test/queue.mjs` — the generation queue's claims (see the scheduling section).
- `test/player.mjs` — the headless transport, with the engine, the AudioContext and the workers all
  injected. It checks the properties that make the thing embeddable and that a browser would not
  have told us about anyway: the queue invariant across a seek, N widgets sharing ONE worker pool,
  storage namespaced per instance, and `destroy()` actually letting go.
- `test/element.mjs` — `<skafinity-player>` against a hand-rolled stub DOM. **It has no CSS engine**,
  so it proves the element builds its tree, wires its events, derives a palette and tears down —
  and proves nothing whatever about layout, cascade or how any of it looks. The demo pages are the
  only way to see that, and a person has to look at them.
- `test/palette.mjs` — the palette derivation, with the factors READ OUT of
  `Code/UI/SkafinityTheme.cs` rather than hardcoded. Two files implementing one colour rule is the
  classic silent fork; change either side and this fails.

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
BeginPlan, Explain, WavFromSamples}` plus all of `VibeCodec` and `SeedCodec`. Keep that surface
stable and the
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
