# Structure — the drums are the last layer that cannot vary

The working document for the `structure/drums` branch. It does for the kit what `structure/arrange`
did for everything else, and it exists because that branch deliberately left the kit alone. When
the work lands this file is deleted, and whatever a future session would get wrong without it goes
into `CLAUDE.md`. Everything else lives in git.

---

## Context

The arranger branch fixed the melodic layers by measuring first. Every one of them now varies
within a song: comp, keys and bass draw a figure per section *and* have it arranged against a
skeleton; the tune switches by section type; the form is drawn per song out of a family. The kit
was left out on purpose, and the reasoning was good — the grooves are fitted to a played corpus, so
the drums were the measured reference the arranger wrote *against* rather than another client of
it.

The cost of that decision is now the largest single gap in the engine:

```csharp
_groove = prof.DrawGroove( rng );      // Compose.cs, ComposePlan — ONCE, per song
```

It is never re-drawn. Every bar of every section plays the identical kick, snare and cymbal
pattern. What varies is only what is layered on top: a gain from `KitGain`, the cymbal halving
below energy 0.4, ghost rolls against `0.35 + 0.65 × energy`, `KickSyncChance`'s stray kick, the
per-section fill, the hats-or-ride roll, and the foot figure.

| layer | states per genre | states WITHIN one song |
|---|---|---|
| the form | 31–117 | 1 (it is the song) |
| the rhythm section as played | 106–479 | one per section |
| the tune | 500 of 500 | 2 (chorus, verse) |
| **the kit's pattern** | **2 / 2 / 2 / 3 / 3 / 2** | **1** |

Two to three states per genre, and exactly one inside a song. That is where every other layer
started, and it is now the only place left where a listener hearing two songs of one genre is
hearing the same thing twice.

**Intended outcome:** the kit becomes a client of the arranger like every other voice — it writes a
part per section, against the same skeleton, reading the same section state.

---

## The decision this branch is built on, and the one it turned down

There were two ways to make a kit vary, and they are not equivalent.

**What this branch does — the kit is arranged.** `MusicGen.Arrange` already takes a `Pattern` and
returns a mutated one (drop, add, displace, recombine, quote) against a `Skeleton`. Pointing it at
`_groove.Kick` and `_groove.Snare` is a small change to a mechanism that exists and is tested. It
is also the only option that reaches real variety: onsets move, and a genre's kit can play
something it has never played before.

**What it turned down — restoring the measured occupancy.** `DrumGroove`'s header records that the
tables were fitted by reading *"each drum's OCCUPANCY per metric position — what fraction of bars
carry that drum there"*, and then thresholded to binary cells. The distribution was measured and
discarded at authoring time; `FootOccupancy` is the one place it survives, as probabilities drawn
into a figure once per section. Restoring that for the hands would have given bar-to-bar variance
that is the corpus's own rather than anyone's invention, and the near-certain positions would have
been a genre-identity guard for free.

It was turned down deliberately: it caps variety at whatever the dataset's variance happens to be,
it needs a fresh pass over Groove MIDI that this branch would then be blocked on, and metal has no
data at all. **This is recorded because a future session WILL rediscover the occupancy idea reading
the same header, and should know it was considered and why it lost rather than re-deriving the
argument from scratch.** If free mutation turns out to wreck the genres, that is the fallback and
it is already scoped.

**The consequence has to be paid, not argued with.** An arranged kick pattern is not a measured
kick pattern. Phase 4 is not optional tidying — it is the price of Phase 1.

---

## The rule that governs every phase

Same shape as the arranger branch, because the failure mode is the same one: **the distinctness
numbers go UP and the agreement numbers do not move much.** Both measured by the same tool, before
and after.

The drums add a third number the melodic work did not need. A guitar figure is authored, so the
worst a mutation can do is make it worse. **A drum figure carries a genre's identity in specific
measured positions**, and the pass that fitted them named the three that had been wrong:

- country's hi-hat on the OFFBEAT eighth (~84%) against the beat (~36%);
- rock's kick on the &-of-1 and &-of-3, far more than a two-bar backbeat spends;
- punk's snare on very nearly every eighth, backbeat struck and everything between it ghosted.

**Those three are the tripwire.** If arranging the kit erodes them, six genres converge on one
drummer, and it will not read as a regression on any single listen — it will read as "the drums got
more interesting". `--stats` has to report them per genre, before and after.

---

## Phase 0 — the drum side of `--stats`, and it lands before anything changes

`test/engine/Stats.cs` already sweeps N songs per genre and already traces `Kick`, `Snare` and
`Cymbal` through `PlanTrace`. What it does not do is ask drum questions of them. It gains:

- **distinct kit states** — distinct (kick, snare, cymbal) onset signatures, hashed on CONTENT, per
  section as well as per song. Today this is 2–3 per genre and 1 within a song; those are the
  numbers to beat. Content rather than object identity, for the reason the arranger branch learned
  the hard way: identity answers the right question only while a figure cannot be arranged.
- **the three identity measures above**, per genre, as percentages.
- **per-position occupancy** across the sweep — what fraction of bars carry each drum at each
  sixteenth. This is the shape the corpus was read in, so it is directly comparable to the numbers
  in `DrumGroove`'s header, and it is how "did the genre survive" gets answered rather than argued.
- **kit-vs-band agreement** in both directions. The existing matrix already carries bass→kick and
  comp→snare; what is new is that Phase 2 makes the direction meaningful.

**It is a diagnostic, not a suite section** — not in CI, not in blessing. Record the full output in
this file before Phase 1, exactly as last time. Re-deriving a baseline later means checking out a
pre-branch build.

---

## Phase 1 — the kit is arranged, per section

`_groove` stops being a per-song constant. Each section draws its own groove from the genre's table
(the way `_compFig` / `_bassPat` already do, keyed by section type so every chorus agrees), and
then the kick and snare go through `Arrange`.

What that needs:

- **A drum `ArrangeRole`.** The melodic roles carry a `CellClass`, a kick pull, a complement push
  and a seam pull. The kit needs its own reading of those: a kick that leans toward the tune's
  landings or away from them, a snare that holds the backbeat, a cymbal that is mostly left alone
  because it is the pulse.
- **A spine that mutation may not touch.** This is the drums' answer to `CellClass`, and without it
  the identity measures above erode. A genre's groove declares which of its onsets are the genre —
  country's hat on the "and", punk's struck backbeat, the snare on 2 and 4 wherever a genre puts it
  — and `Drop` and `Displace` skip them. Everything else is fair game.
- **Chorus quoting, exactly as the melodic voices do it.** The song's kit part is arranged once and
  cached; every later chorus replays it. That is the identity guarantee and the arranger branch
  already learned that "quotes verbatim" has to mean the choruses quote *each other*, not that the
  chorus quotes the table.

Re-bless.

---

## Phase 2 — who goes first, drawn per song

The skeleton the band writes against is the kit's accents, plus the genre's metric weights, plus
the phrase seams, plus the tune's occupancy. **Three of those four do not need the kit**, which is
what makes both orderings implementable off one mechanism.

- **Kit leads.** The kit arranges first against seams, metre and tune; the skeleton then gains its
  accents; bass, comp and keys follow. This is the current design with the kit's decision moved
  from once-per-song to once-per-section.
- **Kit follows.** The skeleton is built without the kit, the band writes against it, and the kit
  arranges last against what the band actually took — a drummer playing to the riff.

One draw per song, like `_riffBass`. It is not a coin flip dressed up: a leading kit is most punk
and most rock, a following kit is riff-led metal and a great deal of programmed pop where the kit
tracks the topline.

**The acceptance rule needs a wrinkle here.** Cohesion is currently achieved by the band writing
against the kit; under "kit follows" it is achieved by the kit writing against the band. The
numbers should hold either way — but they are now measuring two different mechanisms, so
`--stats` has to report them split by which mode the song drew, or the two average into something
that describes neither.

Re-bless.

---

## Phase 3 — energy and vibe reach the decision

Today `_energy` reaches the kit only as gain and as two thresholds (`sparse`, the ghost roll).
Everything a drummer actually does with dynamics — playing fewer notes, moving to the ride, opening
the hat, dropping the ghosts, hitting harder — is either absent or a level.

The section's energy should be an input to what the kit *plays*: how far the arranger is allowed to
thin, whether the spine alone survives a breakdown, how much the busy layer contributes. The vibe's
`DrumBusy` and `DrumTone` already exist and should feed the same decision rather than sitting on
top of it as multipliers.

Watch the interaction with `Feel`. Half time is a pattern rate and it already stretches the groove;
an arranger that also thins for low energy can take a half-time breakdown down to nothing.

Re-bless.

---

## Phase 4 — the citation is moved to what it still covers

**Not optional, and not tidying.** After Phase 1 the engine does not play the fitted patterns; it
plays mutations of them. `DrumGroove`'s header currently reads as though what ships was measured,
and `CLAUDE.md` repeats it. Left alone, the repo would carry a citation over invented content —
which is precisely the failure the accent-weight block warns about in its own words: *"a sentence
in that register is read as one, and writing an assumption there launders it into a citation."*

What is still true after this branch, and should be stated as such:

- **the accent weights are untouched** and remain measured — velocity was a separate question off
  the same pass, and nothing here changes it;
- **the tables are measured SEED material** — the placements they were fitted to are real, and the
  three corrections the pass made are still the reason those tables look the way they do;
- **what the engine plays is arranged from that seed** and is a design call.

`FillHits` already models this exact distinction — measured for rock, "a design call ANCHORED on
that one number" for everything else, each row saying which it is. Follow it.

---

## Fills — in scope only where it is cheap

Fills are the one part of the kit that already varies: planned per section, in ticks, with a shape
drawn from per-genre weights and a density target. They are not the problem and this branch does
not redesign them.

One alignment is worth taking because the machinery exists: `RenderFill` reads neither the phrase
seams nor the tune, both of which the `Skeleton` already carries. A fill that avoids stepping on
the tune's landing, and that leans on the seam it is actually crossing, is a small change against
something already built. If it turns out not to be small, it is a `PLAN.md` row rather than a
reason to hold the branch.

---

## Cross-cutting rules this must not break

- **`Engine/` stays framework-free.** `System`, `System.Collections.Generic`, `System.Text` only.
  Both targets glob the folder, so a new file under `Engine/` ships to both with no list to update.
- **`MusicGen.CopyOf`, never `Array.Clone`.** The s&box whitelist blacklists that one member and it
  compiles perfectly here and in the wasm build.
- **An arranged figure still has to ASCEND.** `Pattern` derives each span from the next onset, so an
  out-of-order tick becomes a negative span clamped to 1 and every note in that figure turns into a
  one-tick blip with nothing reporting it. The suite checks it for the melodic voices; extend that
  to the kit rather than assuming `Arrange` is safe because it is already used.
- **The kit synthesises during the plan pass**, so anything added here costs `--stats` directly. The
  sweep runs at 8 kHz for that reason and the rate is a straight multiplier on its cost.
- **Re-bless once per phase, not once at the end.** A digest diff is only information if it is
  attributable to something.
- **The landing commit re-publishes and re-stages `web/_framework`.**
- **No `make` on this host** — run the Makefile's underlying `dotnet` / `node` commands.

---

## Explicitly out of scope

**This branch is about what the drummer PLAYS, not how the kit SOUNDS.** `PLAN.md` ranks 45 (some
songs open with a cymbal wash nobody put there) and 40 (the cymbals are not tuned, hats included)
are kit and mix questions and they stay where they are. They will look tempting from inside this
work — a cymbal is a drum and the files are adjacent — and taking them would mean judging an
arrangement change by ear through a timbre change made in the same commit. Keep them separate.

---

## Files

| file | what happens to it |
|---|---|
| `test/engine/Stats.cs` | the drum-side sweep, Phase 0. Diagnostic only. |
| `Engine/Arrange.cs` | a drum `ArrangeRole`, and the spine mutation may not touch. |
| `Engine/Drums/Groove.cs` | grooves gain a spine; `RenderDrumBar` reads the arranged patterns. |
| `Engine/Compose.cs` | `_groove` drawn per section; the lead/follow ordering. |
| `Engine/GenreProfile.cs` | the kit's role parameters per genre. |
| `web/_framework` | re-published and re-staged in the landing commit. |

---

## Verification

```sh
dotnet run --project test/engine -c Release                      # must stay green, and ~25 s
dotnet run --project test/engine -c Release -- --stats 500       # before Phase 1, and after each
dotnet run --project test/engine -c Release -- --grid            # every voice still on the grid
dotnet run --project test/engine -c Release -- --levels          # a busier kit is a louder kit
dotnet run --project test/engine -c Release -- --render 4:rotaliate:8 ~/song.wav
```

**What none of that proves** is the thing the branch is for. Render two consecutive songs of one
genre and listen to them back to back — a kit that varies is only audible across songs, which is
why this gap outlived every other one.
