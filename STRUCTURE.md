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

### Landed, and it needed one thing nobody listed

The sweep counts onsets, and every onset it has is one flat list per voice with no boundaries in
it — so *"per section"* was not a question it could ask at all. `PlanTrace` gains the section
spans, **recorded by the composer** rather than re-walked from the form, for the reason the onsets
themselves are: a second bar ruler is wrong exactly when the form varies per song, which it now
does. The span carries the kit's PLAN with it (the groove's name and its three patterns as
handed to the bar loop).

**Two kit counts, and only the first is the one this branch is about.** *Planned* is those
patterns; *played* is the onsets that came out, which carry the per-bar ghost roll and the fill
handover on top. Played was already 3.8–4.6 per song before a line changed, and it says nothing
about whether the kit varies — a stochastic ghost is not a state. Reading it as one would have
declared this branch finished before it started.

### The baseline — `--stats 500`, at `f97dad0`

```
── distinct kits (kick x snare x cymbal, hashed on content) ──
  Ska-Punk  planned     2 over the sweep,  1.00 per song   played   122 / 4.43   (of 8.7 sections)
  Rock      planned     2 over the sweep,  1.00 per song   played    34 / 3.94   (of 9.1 sections)
  Country   planned     2 over the sweep,  1.00 per song   played  1369 / 4.63   (of 8.3 sections)
  Metal     planned     3 over the sweep,  1.00 per song   played    35 / 3.82   (of 8.6 sections)
  Punk      planned     3 over the sweep,  1.00 per song   played   480 / 4.01   (of 7.8 sections)
  Pop       planned     2 over the sweep,  1.00 per song   played    29 / 4.14   (of 8.9 sections)

── kit identity tells (the three the corpus pass corrected) ──
  Ska-Punk  cym beat   90%   cym &   78%   kick &1&3    0%   snare/bar  1.7 (1.2 struck)
  Rock      cym beat   94%   cym &   88%   kick &1&3   29%   snare/bar  2.0 (1.9 struck)
  Country   cym beat   39%   cym &   76%   kick &1&3    0%   snare/bar  8.7 (1.9 struck)
  Metal     cym beat   90%   cym &   79%   kick &1&3   35%   snare/bar  2.6 (2.6 struck)
  Punk      cym beat   94%   cym &   89%   kick &1&3   14%   snare/bar  4.5 (2.4 struck)
  Pop       cym beat   92%   cym &   87%   kick &1&3    0%   snare/bar  1.5 (1.5 struck)

── per-position occupancy: fraction of bars carrying that drum there ──
               1  e  &  a  2  e  &  a  3  e  &  a  4  e  &  a
  Ska-Punk   kick     41  0  0  0 34  0  0  0 88  0  0  0 31  0  0  0
             snare     3  0  0  0 79  0  0  0 57  0  0  0 31  0  0  0
             cym      97  0 81  0 87  0 81  0 94  0 78  0 80  0 74  0
  Rock       kick     97  0 30  0  0  0 67  0 94  0 27  0 16  0  0  0
             snare     0  0  0  0 97  0  0  0  0  0  0  0 89  0 13  0
             cym      97  0 91  0 97  0 91  0 94  0 88  0 89  0 83  0
  Country    kick     97  0  0  0  0  0  0  0 94  0  0  0  0  0  0  0
             snare    50 50 50 50 97 50 50 50 49 49 49 48 89 46 46 46
             cym      68  0 92  0 27  0 59  0 62  0 89  0  0  0 65  0
  Metal      kick     97  8 34  8 91 35 34 36 94  7 36  7 85 40 59 15
             snare     0  0 27  0 64  0 27  0  7  0 26  0 60  0 48  0
             cym      97  0 81  0 88  0 81  0 94  0 79  0 81  0 75  0
  Punk       kick     96  0  0  0 66 28 26  0 67  0 27  0 61 26  0  0
             snare    36  0 62  0 67  0 62  0 37  0 60  0 61  0 67  0
             cym      97  0 92  0 97  0 92  0 94  0 88  0 89  0 84  0
  Pop        kick     97  0  0  0 58  0 37  0 57  0  0  0 87  0  0  0
             snare     0  0  0  0 58  0  0  0 37  0  0  0 53  0  0  0
             cym      97  0 88 37 92  0 92 35 93  0 85 36 85  0 83 32

── cohesion (% of A's onsets that land on a B) ──
  Ska-Punk  bass->kick 26%  kick->bass  87%  comp->snare 15%  snare->comp 34%  bass->comp 53%
  Rock      bass->kick 46%  kick->bass  75%  comp->snare 26%  snare->comp 61%  bass->comp 64%
  Country   bass->kick 41%  kick->bass 100%  comp->snare 50%  snare->comp 18%  bass->comp 17%
  Metal     bass->kick 55%  kick->bass  83%  comp->snare 17%  snare->comp 94%  bass->comp 98%
  Punk      bass->kick 45%  kick->bass  89%  comp->snare 57%  snare->comp 96%  bass->comp 96%
  Pop       bass->kick 52%  kick->bass  75%  comp->snare 15%  snare->comp 12%  bass->comp 25%

── distinct rhythm sections (the song's own) ──
  Ska-Punk 227   Rock 481   Country 351   Metal 308   Punk 133   Pop 329   (of 500)
```

Two rows of that are the tripwire in its resting state and are worth reading before touching
anything: **country's cymbal is 39% on the beat against 76% on the "&"** (the corpus says 36/84,
so the table is doing its job), and **rock's kick carries &1/&3 in 29% of bars** where ska,
country and pop carry them in none at all. Punk's snare is 4.5 a bar with only 2.4 struck — the
ghosts are the density and the strikes are the backbeat, which is why the two are counted apart.

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
