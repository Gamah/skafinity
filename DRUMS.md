# Drum kit rework — audition first, then wire it in

The working document for the `drums/kit-rework` branch. It is deliberately **not** a `PLAN.md` row —
this branch bypasses `PLAN.md` entirely, and nothing here is copied forward. When the work lands,
this file is deleted: whatever a future session would get wrong without it goes into `CLAUDE.md`,
and everything else lives in git.

## Context

The kit is six synthesised voices in `Engine/Drums/Kit.cs`, fired by `Engine/Drums/Groove.cs`.
Auditing it turned up problems that are less "tuning" than "not finished":

- **The tom is one voice with two unrelated pitch sets.** The busy layer alternates 150/190 Hz
  (`Groove.cs:365`); fills sweep `{260,215,175,145,120,100}` (`Groove.cs:485`). `RenderTom` pans
  *by pitch* over a hardcoded 145–260 Hz map (`Kit.cs:91`), so the fill's bottom two pitches fall
  below the floor and clamp hard right — the two lowest toms of every fill share a position.
  Nothing in the engine describes a kit; it describes a pitch.
- **Hats are binary and their tails collide.** Pop's four-on-the-floor puts `Open` on every offbeat
  (`Groove.cs:226`) with a 160 ms tail into a ~240 ms gap. A real hat is choked when the foot
  closes it, and the open→closed pair is the gesture. A riding section silences the hats entirely.
- **The crash is barely wired.** `DrumGroove.CrashOnOne` is set on all twelve grooves and **read
  nowhere** — no crash lands on any downbeat in the engine, only at fill-ends and song-ends. And
  `RenderCrash` is the one kit voice with **no gain parameter** (`Kit.cs:148`), so every crash is
  full-level regardless of energy, velocity or genre accent.
- **The groove kick and snare never call `KitGain`.** They discard `h.Vel` and get no metric accent
  and no energy scaling (`Groove.cs:338`, `:354`). On metal's sixteenth double-kick that is audible
  as machine-gunning: identical waveform, identical level, sixteen times a bar.
- **The ride's `bell` is positional, not musical** — `(tick - barTick) % TicksPerBeat == 0`, so every
  quarter-note is a bell, and the only difference is duration (340 ms vs 220 ms).

Intended outcome: a kit that is a *kit*. But the sound comes first — **every voice is auditioned and
approved before any of it is wired into the grooves.**

**This branch bypasses `PLAN.md`.** No rows are added or removed.

---

## Phase 1 — The audition (gates everything else)

One WAV, one script, iterated until approved. Nothing in Phase 2 starts until then.

- **`~/audition.wav`** — always that name and that location, outside the repo so it is
  straightforwardly pullable off this host.
- **`~/audition.txt`**, also printed to stdout — the numbered script. **One kit part per line**,
  entirely on its own. Long file and long script are fine and wanted.
- **Dry.** Raw voice only: no `_drumLowMul`/`_drumHighMul` tone lean, no reverb, no master
  soft-clip or normalize, centre-panned except where the line is explicitly about position. The
  genre mix is a separate axis and a separate argument.
- **No baseline.** Nothing replays today's kit for comparison — this is about approving the kit
  going forward.
- **Exhaustive.** Every variant worth defending, so a round is a choice between candidates rather
  than a yes/no. Expect several minutes of audio and 60–90 script lines.
- **Each line PLAYS the thing** — a short musical figure of 2–4 bars at a sensible tempo, not a row
  of isolated hits. A voice is judged in motion: how a tom fill moves across the kit, how an open hat
  sits against the closed ones around it, how a double-kick run holds up at sixteenths, how a ghost
  note reads under a backbeat. Isolated hits only where the point genuinely *is* the single hit (a
  crash decay, a bell attack), and even then the figure repeats. Roughly 2 s of silence between
  lines so they stay countable against the script.
- **Still one part alone per line.** The figure is played by the voice under audition and nothing
  else — a tom fill is toms only, a hat pattern is hats only. No supporting kit, no click.

### What gets auditioned

Each bullet is one or more script lines, each playing a figure.

**KICK** — including the stereo/double-pedal work specifically.
- 3–4 body tunings (tight and punchy / deep and booming / short and clicky / sub-heavy), each played
  as a four-on-the-floor and again as a syncopated rock pattern
- pitch-drop rate and depth, sub-layer weight, click level — swept over the same figure each time
- round-robin jitter none / subtle / obvious, over a **straight sixteenth run** where the machine-gun
  effect either shows or doesn't
- beater return off / mid / full, over a **double-kick run at metal tempo** and a triplet gallop
- **stereo alternation ±0% / ±2% / ±4% / ±8%**, over the same sixteenth run and a triplet run
- velocity response: a pattern with accented and unaccented kicks across the `KitGain` range

**SNARE** — in scope at your request, and it has real missing articulations.
- shell tuning and wire-amount variants, each played as a **backbeat with ghost notes** so the ghost
  and the hit are heard against each other
- ghost depth swept over the same backbeat
- **rimshot** (as an accented backbeat), **cross-stick** (as a half-time verse figure), **flam**
  (into a downbeat), **buzz/press roll** (a bar-long crescendo), **snares-off** (a tom-like figure)
- velocity response over a dynamic phrase rather than a flat row

**TOMS** — the biggest change; auditioned hardest, all as fills and grooves.
- **tuning candidates** — two stacked perfect fourths / wider spread / stacked thirds / a fixed
  physical set that ignores the key — each played as a **descending fill**, an **ascending fill**,
  and a **tom groove** (floor-tom pulse with rack answers)
- each of the three pieces alone, played as a repeated figure, so its own decay and sag are audible
- the winning shape rendered **in three different keys**, since pitches derive from `_rootMidi`
- pan spread 0% / 25% / 40%, and rack-left-floor-right vs the reverse, over the same fill so the
  sweep across the field is the thing being compared
- decay length, pitch sag, beater snap and stick click — swept over one fixed fill

**HATS** — all in pattern, since a hat alone tells you nothing.
- openness 0.0 / 0.25 / 0.5 / 0.75 / 1.0, each as a **straight eighth-note pattern**
- choke off / fast / slow, over pop's **open-on-every-offbeat** figure — the one that smears today
- **foot chick** on 2 and 4 under silence; **splash**; **open→closed pairs** in a disco figure
- brightness/cutoff swept at each openness over one fixed eighth pattern
- a sixteenth chatter pattern with alternating accents, for the busy layer

**CRASH**
- bright and dark at several decay lengths, each as an accent landing on a downbeat every 2 bars
- accent-level vs ridden-level gain, same placement
- **crash-ride**: an eighth-note pattern ridden on the dark crash with the bright one accenting
  bar one — the technique itself, played
- choke, as a stab

**RIDE** — the articulations you asked to hear, each as a ride pattern.
- tip on bow (ping), shoulder on bow (wash), edge / crash-ride — each as straight eighths and as a
  swung ride pattern
- **bell**: shoulder and tip, with 3 timbre candidates (narrow band-passed noise cluster, wide
  cluster with longer wash, duration-only), each played as a bell-and-bow pattern so the two
  articulations are heard alternating. `Kit.cs:169` records that every *sine-partial* bell read as a
  pitched "ding" and was deleted; these are noise-based attempts, not a re-run of that.
- choke; crescendo swell into a downbeat

### How it is built

- **`MusicGen.ForAudition( Config, double seconds )`** — a small `internal static` factory in
  `Engine/MusicGen.cs` that allocates `_bufL`/`_bufR` and a constant-tempo `Timing`, skipping
  composition entirely. Precedent for harness-only internals: `RawLevels()`, `AudibleNotes()`,
  `GridSamples()`, `Explain()`.
- **`--audition [path]`** in `test/engine/Program.cs`, alongside `--seed`/`--grid`/`--score`/
  `--levels` (dispatch at `Program.cs:29-44`, all of which `return 0` before the checks run).
  A permanent committed diagnostic, not a throwaway — it is the tool for every future kit argument.
- The voices are **rewritten to their new signatures in this phase** (below), with the variant
  parameters as real arguments; the audition sweeps them. Approving a line fixes that parameter as
  the default or the `GenreProfile` table value. So Phase 1 is the DSP work; Phase 2 is the wiring.

---

## Phase 2 — Wiring (only after the audition is approved)

**No new `Config` fields and no new vibe knobs.** Everything lands in `GenreProfile` and engine
internals, keeping `wasm/Exports.cs` (`Cfg.To`/`From`/`Size`), `VibeCodec`'s grid, `SkafinityPlayer.cs`,
`skafinity.config.json` and `test/smoke.mjs`'s `GLOBALS + N × 4` arithmetic untouched. The five
per-piece `*Balance` advanced fields already exist and are the retuning lever if the mix moves.

**Toms.** A `TomKit` of three pitches drawn once per song from `_rootMidi` (`MusicGen.cs:248`) and a
per-genre `TomTune` character, stored beside `_drumTone`/`_crashBrightLeft` and set in `ComposePlan`
(`Compose.cs:~103`). **Drawn from `_rootMidi`, never `_keyShift`** — nothing re-reads it per section,
so toms cannot drift with a mid-song key change, at zero cost. `RenderTom` takes a tom **index**, not
a frequency, so position comes from the index rather than from a frequency compared against a
hardcoded range — the pan-clamp bug dies by construction, the same trick as `Register(octaves)` in
`CLAUDE.md`. Busy layer (`Groove.cs:365`) alternates the two rack toms; the fill (`:523`) maps
position across all three. Replace the bare literal `24` in `(t / 24) & 1` with `Timing.TicksPerEighth / 2`.

**Hats.** Cell vocabulary on `DrumGroove` (`Groove.cs:47`), with `Open = 1` keeping its value so
every existing table means exactly what it means today: `0` closed, `Open = 1`, `Half = 2`,
`Foot = 3`, `Splash = 4`. Choke wiring in `RenderDrumBar` (`Groove.cs:315-335`) materialises the
sliced cymbal hits so each knows its successor, slicing **one beat past `to`** for lookahead only, so
an open hat on the "and of 4" is choked by the next bar's downbeat; iterating the same list preserves
the existing interleaving of `noise.Chance( busy )` draws. Foot chick on 2 and 4 when
`_ride || _crashRide` — deterministic, no RNG draw, so stream-safe wherever it sits.

**Crashes.** `RenderCrash` gains an amp parameter; three call sites (`Groove.cs:529`,
`Compose.cs:465`, `:479`) route through `KitGain`. Side is already derived from
`dark == _crashBrightLeft` (`Kit.cs:152`), so the ridden and accent crashes land on opposite sides
for free. `CrashRideFrom` on `GenreProfile` follows `LoudComp`/`LoudFrom`'s shape — metal 0.80,
punk 0.85, rock 0.90, ska/country/pop 1.01 (never) — and `_crashRide = _energy >= prof.CrashRideFrom`
is set per section beside the `_ride` roll (`Compose.cs:258`). The dead `CrashOnOne` finally fires the
bright crash on a section's first downbeat.

**Kick.** `_lastKickSample` tracked on the instance so groove, fill (`Groove.cs:525`) and ending
(`Compose.cs:464/476/478/492/499`) share one timeline. The groove kick reads `h.Vel` through
`KitGain( h.Tick, h.Vel, 0.30f )` — under the cymbal hand's 0.55 and near the fill's 0.35, since the
kick is the floor of the groove and should breathe least. Round-robin jitter comes from a **local
LFSR seeded on `start`**, the pattern `RenderTom` already uses at `Kit.cs:99`, so it costs nothing
from the shared drum stream.

**Ride.** Groove-level wiring decided by the audition — in particular whether `bell` stops being
positional and becomes a cell value (reusing `Open`, which a riding section currently ignores
entirely, falling through to `RenderHat`).

## Cross-cutting rules this must not break

- **The `_drumGain <= 0` guard stays *inside* every voice**, before `_time.DrumPush` and before any
  `noise.Next()` (`Kit.cs:10-15`). Every new voice repeats it. Muting the kit must not move the stream.
- **`KitGain` is the only door out of `MetricGain`** (`Compose.cs:626`); no pitched voice may reach it.
- **Per-genre values go in `GenreProfile`, not in a voice.** No `if ( _genre == … )` in `Kit.cs`
  or `Groove.cs`. Timbre stays next to the voice; only what the composer reads goes in the profile.
- **`FillCells = 8` draws per beat regardless of grid** (`Groove.cs:401`) — a knob must never decide
  how much of the stream a fill spends.

## Files

| File | Phase | Change |
|---|---|---|
| `Engine/Drums/Kit.cs` | 1 | every voice rewritten: 3-piece toms by index, hat openness/choke/foot/splash, snare articulations, crash gain, kick jitter/beater/pan, ride articulations |
| `Engine/MusicGen.cs` | 1 | `ForAudition` factory; new drum state fields |
| `test/engine/Program.cs` | 1 | `--audition` dispatch, renderer, script writer |
| `Engine/Drums/Groove.cs` | 2 | cell vocabulary, choke lookahead, foot chick, crash-ride, `CrashOnOne`, `KitGain` on kick/snare, tom indices |
| `Engine/GenreProfile.cs` | 2 | `CrashRideFrom`, `TomTune` |
| `Engine/Compose.cs` | 2 | `_tomKit`/`_crashRide` per song and per section; crash call sites take gain |
| `test/engine/digests.txt` | 2 | re-blessed |

Not touched: `wasm/Exports.cs`, `VibeCodec.cs`, `SkafinityPlayer.cs`, `skafinity.config.json`,
`test/smoke.mjs`, `test/page.mjs`.

## Verification

`make` is unavailable on this host — run the underlying commands.

1. **Phase 1 ends with** `dotnet run --project test/engine -c Release -- --audition`, producing
   `~/audition.wav` and `~/audition.txt`. Hand both over and wait. Iterate on notes; do not start
   Phase 2 until approved.
2. **Engine suite** (~25 s): `dotnet run --project test/engine -c Release`. Phase 1 should leave the
   ten render digests untouched, since nothing is wired in yet — if they move, a voice changed
   behind the grooves' back and that is a bug. Phase 2 is expected to move all ten.
3. **Green throughout:** every groove still names a non-empty kick/snare/cymbal (`Program.cs:900`),
   country's train beat and punk's cymbal (`:917`, `:921`), metal's double kick still bursting at the
   sixteenth (`:927`), `FillShapes.Length == 4` (`:690` — unchanged, no fill shapes added).
4. **Mix balance is the real risk in Phase 2.** `ArrangementTests` asserts comp/lead/bass *relative
   to the kit* (`:1527-1554`), and a velocity-scaled kick, choking hats and crashes with gain all
   move the kit's RMS the same way. Re-measure with `-- --levels` and retune the five `*Balance`
   advanced fields if it moved — they are measured numbers and go stale when the part changes.
5. `--grid` does not measure drums at all (drums are buffer-written, not events, so `Onsets()` cannot
   see them). So the choke lookahead and foot chick are verified by listening and `--score`, not by
   an assertion.
6. **Re-bless in the same commit as the audible change:** `-- --bless`.
7. **Rebuild and re-stage the bundle in the same commit.** `Engine/**/*.cs` is hashed by
   `tools/bundle-stamp.sh`, so every file here invalidates `web/.bundle-stamp`. Full AOT publish
   (a few minutes) + stage, then `node test/queue.mjs && node test/smoke.mjs && node test/page.mjs`
   and `sh tools/bundle-stamp.sh check`. A stale bundle fails CI and silently serves the old engine
   on Pages.
8. Push the branch for listening on a real machine; open a PR. No merge without approval.
