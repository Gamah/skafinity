# Structure — the four things that vary less than they look

The working document for the `structure/arrange` branch. It is deliberately **not** a `PLAN.md`
row: it is the four rank-99 rows taken together, on one branch, because they are four views of
the same defect and three of them touch the same section state. When the work lands, those four
rows are deleted from `PLAN.md`, this file is deleted with them, and whatever a future session
would get wrong without it goes into `CLAUDE.md`. Everything else lives in git.

---

## Context

The engine composes a fresh song per seed and every layer of it *is* fresh. That is not the
problem. The problem is that the layers have wildly different numbers of **states**, and the ones
a listener uses to tell two songs apart are the small ones. Measured over 500 songs per genre —
plan only, no audio:

| layer | distinct states in 500 songs |
|---|---|
| the tune | 500 — unique every song |
| the whole rhythm section (comp + bass + groove) | punk **12**, metal **18**, pop **24**, ska **30**, country **32**, rock **48** |
| the song form | **1** |

So one punk song in nine repeats the previous one's comp *and* bass *and* groove with only the key
and the tune moved, and **every** ska-punk song is the same 9 sections and 60 bars with the
hemiola on the second verse and the half-time bridge in the same place, forever. Randomness cannot
touch either number, because both are table-size limits rather than draws.

And the one layer with 500 states has 500 states of the same *kind*. Over 500 chorus tunes per
genre: **0%** of onsets fall off the eighth grid, note lengths are only 24/48/96 ticks, the first
degree is only ever 0, 2 or 4 (33/33/33), the last is the tonic **100%** of the time, a leap is
always exactly a third, 10–18% of notes sit pinned at the `Clamp(-2, 9)` ends, and the answer is
literally the call transposed down one degree 51–80% of the time. Every tune is unique and every
tune is the same tune. That is what "samey with different notes" is.

On top of which the genre does not reach the melody at all except through two floats, so the same
`n` in two genres returns a **byte-identical** tune: Rock↔Country 50% of the time, Punk↔Pop 53%,
Ska↔Country 10%. Different key, different kit, same melody — which is most of why the roster reads
as one band.

**Intended outcome:** an arranger that writes every part at once against a shared rhythmic
skeleton, a tune with a real vocabulary, a per-genre tune seed, and a family of forms per genre
with per-song variation in the details.

---

## The rule that governs all four phases

**The failure mode is the exact opposite of today's, and it has to be measured rather than hoped
for.** Today's defect is parts that ignore each other and tunes that are all one shape. Every fix
below pushes toward cohesion and toward a wider vocabulary, and both of those, overdone, converge:
a shared skeleton makes every part play the same rhythm, and a wider tune vocabulary makes every
tune reach the same average. **Parts that all play the same rhythm is worse than parts that ignore
each other**, and it will not read as a regression on any single listen.

So the acceptance rule, for every phase: **the distinctness numbers go UP and the agreement
numbers do not move much.** Both are measured by the same tool, before and after, against the
baseline recorded below.

---

## Phase 0 — `--stats`, and it lands before anything changes

`test/engine/Stats.cs`, dispatched from `Main` exactly the way `--levels` is (`Program.cs:65`):
parse, run, `return 0` before the section table. **It is a diagnostic, not a suite section** — it
is not in CI, not in the build, and not in blessing. The suite's ~25 s budget is untouched, and
`--stats` is free to take a minute.

**It is plan-only. No audio.** That is the whole reason 500 × 6 songs is affordable: the tunes are
`Pattern`s, the groove is `Pattern`s, the figures are drawn at plan time, and `SectionTicks` is
arithmetic. `MusicGen.BeginPlan` already exists and is already the entry point `--seed` uses; what
`--stats` needs on top is a plan-only read of each voice's onsets over the song, which is a walk
of the structure slicing patterns and no synthesis at all.

**One implementation point that is design, not detail.** The sweep must not re-implement
`RenderComp`'s figure precedence —

```csharp
var fig = hemiola ? CompFigure.Hemiola
    : loud ? _songLoud
    : _compOrn ? _prof.CompOrnament : _compFig;              // Compose.cs:503-505
```

— because two implementations of "what does the comp play in this bar" drift, and when they drift
the tool starts lying with a straight face.

**What landed goes further than the `FigureFor` this planned, and the reason is worth keeping.**
Factoring the precedence into a pure function still leaves the sweep re-walking the structure and
re-slicing the patterns, which is a second implementation of *everything else* — the bass's riff
branch, the keys' energy gate, the tune's section test, the cymbal's sparse-section thinning, the
snare's ghost roll. And `_compOrn` cannot be re-derived at all outside a render: it is rolled off
`rhythmRng` interleaved with that voice's own note draws, so a walk that only takes the ornament
roll gets a different answer than the song did.

So the onsets are recorded BY THE VOICES, at the moment they play them — `Engine/PlanTrace.cs`,
attached with `MusicGen.BeginPlan( tag, cfg, trace )` and null in every ordinary render. There is
then exactly one answer to what a bar played, structurally rather than by discipline. The cost is
that a song has to genuinely compose (the drums synthesise during the plan pass), so the sweep
composes at 8 kHz — every number it reports is a tick or a count, so the rate is a straight
multiplier on cost and reaches nothing else. 3000 songs in ~65 s.

### What it prints, and today's numbers

Per genre, over N songs (default 500):

- **distinct rhythm-section states** — distinct `(comp figure, bass pattern, groove)` triples.
  Today, by genre index 0–5 (ska, rock, country, metal, punk, pop): **30 / 48 / 32 / 18 / 12 / 24**.
- **distinct forms** — today **1** for every genre. Plus a total-bars histogram, which today has
  one entry per genre modulo tempo.
- **cohesion** — bass-on-kick agreement, today **27 / 47 / 41 / 89 / 45 / 53 %** across genres
  0–5; comp-on-snare, today **1 / 31 / 60 / 16 / 70 / 10 %**. Plus the full pairwise voice-onset
  agreement matrix, which is the number Phase 3 is judged on.
- **tune shape** — off-eighth-grid % (**0**), note-length histogram (**24/48/96 only**, a quarter
  ≈ 50% of all notes in every genre), first-degree histogram (**0/2/4 at 33/33/33**),
  last-degree-tonic % (**100**), leap-size histogram (**always exactly a third**), range-pinned %
  (**10–18**), repeated-adjacent % (**7–12**), answer ≡ call−1 % (**51–80**), distinct rhythms
  (punk **258**, pop **320** — both structurally capped by ~13-note tunes), distinct contours.
- **cross-genre tune collisions** — % of seeds where two genres return the identical tune.
  Today Rock↔Country **50**, Punk↔Pop **53**, Ska↔Country **10**.

### The baseline, recorded before Phase 1

`dotnet run --project test/engine -c Release -- --stats 500`, on the branch point. This is what
every later phase diffs against; re-deriving it later means checking out a pre-branch build.

Two places it reads differently from the numbers quoted above, and both are the tool being more
precise rather than disagreeing. **The state count includes the KEYS figure**, so it is the whole
rhythm section rather than three of its four draws — which is what makes rock 48, country 32 and
pop 24 (exactly 2x the triple, since those three genres are precisely the ones with a keys voice).
And **"answer = call-1" reads 100%**, not 51-80%: the answer IS always the call transposed down a
degree, by construction, and the only thing that ever differs is the last note, which is forced to
the tonic and is the tune landing rather than a derivation. Counting that forced note as a failed
derivation is what produced a number under 100, and it measured the ending rather than the answer.

```
── sweep: 500 songs x 6 genres, tag "rotaliate", composed at 8000 Hz ──


  composed genre 0…  
  composed genre 1…  
  composed genre 2…  
  composed genre 3…  
  composed genre 4…  
  composed genre 5…  
  3000 songs in 63.5 s

── distinct rhythm-section states (comp x keys x bass x groove figures) ──
  Ska-Punk    30   (table ceiling 30: 3 comp x 1 keys x 5 bass x 2 groove)
  Rock        48   (table ceiling 48: 3 comp x 2 keys x 4 bass x 2 groove)
  Country     32   (table ceiling 32: 2 comp x 2 keys x 4 bass x 2 groove)
  Metal       18   (table ceiling 18: 3 comp x 1 keys x 3 bass x 2 groove)
  Punk        12   (table ceiling 12: 2 comp x 1 keys x 3 bass x 2 groove)
  Pop         24   (table ceiling 24: 2 comp x 2 keys x 3 bass x 2 groove)

── distinct forms, and total bars ──
  Ska-Punk     1 forms   bars 60:100%
  Rock         1 forms   bars 64:100%
  Country      1 forms   bars 56:100%
  Metal        1 forms   bars 64:100%
  Punk         1 forms   bars 52:100%
  Pop          1 forms   bars 60:100%

── cohesion: bass on kick, comp on snare (% of A's onsets that land on a B) ──
  Ska-Punk  bass->kick   25%   comp->snare   14%   bass->comp   54%
  Rock      bass->kick   44%   comp->snare   26%   bass->comp   65%
  Country   bass->kick   40%   comp->snare   50%   bass->comp   18%
  Metal     bass->kick   60%   comp->snare   14%   bass->comp   98%
  Punk      bass->kick   43%   comp->snare   63%   bass->comp   96%
  Pop       bass->kick   50%   comp->snare   10%   bass->comp   24%

── the full pairwise agreement matrix (rows = A, cols = B) ──
  Ska-Punk
                Kick   Snare  Cymbal    Bass    Comp    Keys    Tune
    Kick           —     80%    100%     86%     42%       —     46%
    Snare        72%       —    100%     93%     32%       —     44%
    Cymbal       29%     26%       —     94%     52%       —     43%
    Bass         25%     23%     89%       —     54%       —     42%
    Comp         19%     14%     84%     93%       —       —     41%
    Keys           —       —       —       —       —       —       —
    Tune         27%     23%     87%     88%     51%       —       —
  Rock
                Kick   Snare  Cymbal    Bass    Comp    Keys    Tune
    Kick           —      5%     98%     72%     67%     50%     35%
    Snare         7%       —    100%     87%     61%     14%     35%
    Cymbal       44%     27%       —     69%     58%     43%     33%
    Bass         44%     32%     93%       —     65%     42%     33%
    Comp         47%     26%     90%     74%       —     42%     33%
    Keys         51%      9%     97%     70%     62%       —     35%
    Tune         46%     28%     97%     72%     61%     46%       —
  Country
                Kick   Snare  Cymbal    Bass    Comp    Keys    Tune
    Kick           —     52%     68%    100%      0%     11%     40%
    Snare         8%       —     19%     58%     18%     46%     23%
    Cymbal       28%     53%       —     45%     64%     15%     32%
    Bass         40%     68%     43%       —     18%     44%     35%
    Comp          0%     50%     73%     21%       —     10%     26%
    Keys          9%     87%     30%     93%     18%       —     35%
    Tune         31%     64%     60%     68%     42%     32%       —
  Metal
                Kick   Snare  Cymbal    Bass    Comp    Keys    Tune
    Kick           —     28%     75%     83%     93%       —     29%
    Snare       100%       —     98%     87%     95%       —     36%
    Cymbal       83%     31%       —     88%     96%       —     38%
    Bass         60%     18%     59%       —     98%       —     23%
    Comp         51%     14%     47%     80%       —       —     19%
    Keys           —       —       —       —       —       —       —
    Tune         82%     29%     97%     89%     97%       —       —
  Punk
                Kick   Snare  Cymbal    Bass    Comp    Keys    Tune
    Kick           —     58%    100%     99%     96%       —     45%
    Snare        31%       —    100%     99%     96%       —     39%
    Cymbal       45%     68%       —     99%     96%       —     36%
    Bass         43%     65%     95%       —     96%       —     35%
    Comp         42%     63%     93%     98%       —       —     35%
    Keys           —       —       —       —       —       —       —
    Tune         57%     69%     97%     99%     97%       —       —
  Pop
                Kick   Snare  Cymbal    Bass    Comp    Keys    Tune
    Kick           —     30%     99%     72%     35%     90%     46%
    Snare        61%       —    100%     55%      7%     90%     43%
    Cymbal       42%     19%       —     53%     14%     95%     33%
    Bass         50%     19%     91%       —     24%     89%     39%
    Comp         96%     10%     95%     94%       —     91%     59%
    Keys         23%     10%     60%     32%      8%       —     21%
    Tune         56%     22%     97%     66%     26%    100%       —

── chorus tune shape ──
  Ska-Punk  (500 tunes, 31.2 notes each)
    off the 8th grid  0%   pinned at the range ends 17%   repeated adjacent 11%
    last note tonic   100%   answer = call-1 100%
    note lengths      24:33%  48:48%  72:1%  96:18%
    first degree      0:38%  2:29%  4:34%
    step sizes        0:7%  1:75%  2:18%
    distinct          500 rhythms, 500 contours, 500 tunes
  Rock  (500 tunes, 27.4 notes each)
    off the 8th grid  0%   pinned at the range ends 16%   repeated adjacent 10%
    last note tonic   100%   answer = call-1 100%
    note lengths      24:23%  48:49%  72:1%  96:27%
    first degree      0:38%  2:30%  4:32%
    step sizes        0:6%  1:76%  2:18%
    distinct          499 rhythms, 500 contours, 500 tunes
  Country  (500 tunes, 28.2 notes each)
    off the 8th grid  0%   pinned at the range ends 17%   repeated adjacent 11%
    last note tonic   100%   answer = call-1 100%
    note lengths      24:25%  48:48%  72:1%  96:26%
    first degree      0:37%  2:32%  4:31%
    step sizes        0:7%  1:75%  2:18%
    distinct          499 rhythms, 500 contours, 500 tunes
  Metal  (500 tunes, 34.9 notes each)
    off the 8th grid  0%   pinned at the range ends 18%   repeated adjacent 12%
    last note tonic   100%   answer = call-1 100%
    note lengths      24:40%  48:48%  72:0%  96:11%
    first degree      0:36%  2:33%  4:31%
    step sizes        0:8%  1:61%  2:31%
    distinct          500 rhythms, 500 contours, 500 tunes
  Punk  (500 tunes, 13.4 notes each)
    off the 8th grid  0%   pinned at the range ends 13%   repeated adjacent 8%
    last note tonic   100%   answer = call-1 100%
    note lengths      24:19%  48:51%  72:2%  96:28%
    first degree      0:35%  2:33%  4:32%
    step sizes        0:4%  1:78%  2:18%
    distinct          266 rhythms, 410 contours, 498 tunes
  Pop  (500 tunes, 14.1 notes each)
    off the 8th grid  0%   pinned at the range ends 12%   repeated adjacent 8%
    last note tonic   100%   answer = call-1 100%
    note lengths      24:25%  48:49%  72:2%  96:25%
    first degree      0:34%  2:36%  4:30%
    step sizes        0:4%  1:78%  2:18%
    distinct          331 rhythms, 441 contours, 499 tunes

── cross-genre tune collisions (same n, two genres, identical chorus tune) ──
  Ska-Punk  <-> Rock      identical    5%   onset overlap 57%
  Ska-Punk  <-> Country   identical   11%   onset overlap 63%
  Ska-Punk  <-> Metal     identical    1%   onset overlap 70%
  Ska-Punk  <-> Punk      identical    0%   onset overlap 25%
  Ska-Punk  <-> Pop       identical    0%   onset overlap 28%
  Rock      <-> Country   identical   53%   onset overlap 83%
  Rock      <-> Metal     identical    0%   onset overlap 62%
  Rock      <-> Punk      identical    0%   onset overlap 31%
  Rock      <-> Pop       identical    0%   onset overlap 37%
  Country   <-> Metal     identical    0%   onset overlap 63%
  Country   <-> Punk      identical    0%   onset overlap 29%
  Country   <-> Pop       identical    0%   onset overlap 34%
  Metal     <-> Punk      identical    0%   onset overlap 23%
  Metal     <-> Pop       identical    0%   onset overlap 25%
  Punk      <-> Pop       identical   52%   onset overlap 83%
```

---

## Phase 1 — the genre in the tune's seed

`Melody.cs:130-131`:

```csharp
_chorusTune = Melody.Draw( new Rng( $"{_tag}:tune:chorus" ), cycle, barTicks, density, leap );
_verseTune  = Melody.Draw( new Rng( $"{_tag}:tune:verse"  ), cycle, barTicks, density * 0.8f, leap );
```

`_tag` is `"{tag}:{n}"`. The genre is not in it, and reaches `Draw` only through `density` and
`leap` — so where two genres' densities are close the draws mostly agree and the tunes come back
byte-identical. Put the genre in the string: `$"{_tag}:tune:{_genre}:chorus"`, same for `:verse`.
Two lines.

**It buys a different DRAW, not a guaranteed different tune, and the difference is the whole
point.** Two genres landing on a similar melody at one seed is the toy doing what it is for — the
song stream carries no genre precisely so that "the same song, in another genre" exists. What was
wrong was that they landed there RELIABLY, off a stream that could not tell them apart. So the
collision figure is a number `--stats` reports and watches; it is not a target, there is no suite
check that two genres differ, and nothing on this branch may grow into machinery that FORCES them
to. A preference turned into a prohibition is how the next session ends up writing code to invent
divergence nobody asked for.

**`Compose.cs:61` is deliberately NOT touched, and this is the paragraph that stops a future
session from "fixing" it.** The song stream is `new Rng( _tag.ToLowerInvariant() )` with no genre
in it either, so genre 0 and genre 3 at the same `tag:n` share the root note, the pan, the ride
preference, the whole cymbal draw and the kit nuance. **That is a feature and it is kept on
purpose**: the same song in two genres is a thing the toy can do, and it is worth more than the
extra variation putting the genre in that stream would buy. The tune is the one place the sharing
read as a defect rather than as a trick, because a melody is the thing a listener identifies a
song by. So: the genre goes in the *tune* streams and nowhere else.

Re-bless.

---

## Phase 2 — the tune's vocabulary

All of it is `Melody.Draw` (`Melody.cs:38-84`) plus per-genre fields. Three separable levers.

### Rhythm

Today the length of every note is one of two branches:

```csharp
int len = rng.Next() < density
    ? Timing.TicksPerEighth * (1 + rng.Int( 2 ))     // 24 or 48
    : Timing.TicksPerBeat * (1 + rng.Int( 2 ));      // 48 or 96
```

Replace it with a weighted menu over 12 (16th), 24 (8th), 36 (dotted 8th), 48 (quarter), 72
(dotted quarter) and 96 (half), weighted per genre. `TicksPerBeat = 48` renders every one of those
exactly, so `Timing` needs nothing — the 32nd-note row already proved that.

**Rests are an omitted onset, not a `Rest` cell.** Leave the tick out of the array: the preceding
note's `SpanTicks` grows to cover the gap (`Pattern.cs:73-74`), and `RenderTune`'s
`Math.Min( h.SpanTicks, Timing.TicksPerBeat * 2 )` cap (`Melody.cs:170`) turns the remainder into
real silence with no change to `RenderTune` at all. A `Melody.Rest` cell would be read as a
*degree* — `RenderTune` has no Rest arm and would sing it. Two knock-ons to expect: longer spans
mean `resolve = onBeat || len >= TicksPerBeat` (`Melody.cs:178`) fires on more notes, and the
`LeadStyle.DoubleStop` / `Shred` ornament gates at `Melody.cs:200-203` are also length-gated, so
ornament density moves with the rhythm menu.

Also fix the off-by-one while in here: the call loop tests `t < phraseTicks` *before* adding, so
the call can overrun the phrase by up to one note length, and the answer then starts on top of it.

### The answer

Today the answer is not drawn at all — it is the call's rhythm verbatim, the call's degrees minus
one, and a forced tonic on the last note (`Melody.cs:74-80`). Give it a weighted operator set:

- `Transpose(-1)` — today's, and it stays the heaviest weight in most genres.
- `SameWithNewTail` — identical degrees, the last one or two re-drawn to descend home.
- `SequenceUp(+1)` / `SequenceUp(+2)` — the call restated higher, which is a question answered
  with a bigger question, and resolves on the tonic anyway.
- `Invert` — mirror the call's intervals about its first degree.

**The rhythm repeat stays.** `Melody.cs:71-73` is right that varying the rhythm too stops the two
phrases being heard as a question and an answer; the only rhythmic freedom the answer gets is its
final one or two onsets. **The 100%-tonic ending stays too** — it is not a defect, it is what makes
the thing a tune.

Take the operator's draw unconditionally so the chorus and verse tunes stay comparable draws.

### The register ends

```csharp
degree = Math.Clamp( degree + step, -2, 9 );        // Melody.cs:67, and again at :79
```

is sticky: a line that reaches a boundary and keeps stepping outward parks there. That is where the
10–18% pinned notes live, and the 7–12% repeated adjacent notes are the same thing seen from the
other side. **Reflect instead** — negate the step on overshoot. The range stays an octave and a
third, which is what makes a tune singable; it just stops being a wall.

### Where the numbers live

`TuneDensity` and `TuneLeap` move out of the `_prof.Lead` switch in `DrawTunes`
(`Melody.cs:114-122`) and into `GenreProfile`, joined by the length weights, the rest chance and
the answer weights. That switch is the `if ( _genre == … )` smell one level removed — two genres
sharing a `LeadStyle` get the same tune vocabulary today, which is half of why Phase 1's collisions
happen at all.

**Nothing here is a `Config` field**, so `Cfg.To` / `Cfg.From` / `Cfg.Size` are untouched. That is
deliberate and it holds for the whole branch: everything being added is per-genre *character*,
which is `GenreProfile`'s job, not a listener-facing knob.

### Judging it

`--stats`: distinct rhythms and distinct contours go **up** (punk's 258 and pop's 320 are the two
to watch — their tunes are ~13 notes and that is a structural ceiling, so if those two do not move
the rhythm menu is not reaching them); off-grid % becomes non-zero; the length histogram spreads;
the first-degree histogram stops being 33/33/33; pinned % goes to ~0; answer ≡ call−1 % drops to
roughly its new weight. And **pairwise onset overlap between two genres' tunes (31–40% today) does
not rise** — if the vocabulary widened and the tunes got *more* alike, the weights are averaging
rather than distinguishing.

Re-bless.

### What landed, and what it moved

Every target above was met. Baseline → after, across the six genres:

| | before | after |
|---|---|---|
| off the eighth grid | 0% | 28–48% |
| pinned at the range ends | 12–18% | 2–4% |
| repeated adjacent notes | 8–12% | 1–2% |
| answer ≡ call−1 | 100% | 18–44% |
| leap sizes | always a third | third / fourth / fifth |
| distinct rhythms (punk, pop) | 266, 331 | 491, 500 |
| cross-genre onset overlap (worst pairs) | 48%, 54% | 26%, 35% |

The convergence guard holds: the vocabulary widened and the tunes got **less** alike, not more.

**Three things went in that this document did not ask for, and the reason is the same for all
three: the contour was a plain random walk, and none of the vocabulary work reaches that.**

- **Post-skip reversal.** A melody that leaps comes back — one of the most robust findings there
  is about how tunes are actually written, and what makes a leap read as a gesture rather than as
  the line relocating. Without it, widening leaps from "always a third" to thirds/fourths/fifths
  would have made the wandering worse rather than better.
- **The melodic arch.** Phrases rise and then fall on average. The engine had nothing of the kind:
  it wandered, and the only thing that ever brought it home was the forced tonic on the last note,
  which is a landing with no approach to it.
- **Tessitura — a pull toward the middle of the range.** This is the one that actually keeps a tune
  off the range ends, and finding that out is the useful part. Reflection alone was not enough:
  reflecting stops a line *parking* at a boundary, but a walk with no centre still spends its time
  out there, and the arch makes it worse by leaning uphill in the first half of every phrase
  whatever the register already is. Reflection took pinning 12–18% → 5.5%; the centre pull took it
  to 2–4%. **They are different jobs** — one decides where the line lives, the other decides what
  happens when it arrives at an edge anyway — and a session that reads only the pinning number
  will conclude reflection did it.

**The register bound itself is unchanged and is still a judgement.** `-2..9` is twelve scale
degrees, ~19 semitones in a major scale — which is a WHOLE-SONG figure being used as a PHRASE
figure, since the tune here is 2–8 bars. (The large-scale pop-melody work measures range on a
rolling two-bar window for exactly that reason.) The right shape is probably two bounds: a tight
span a single phrase may cover, inside a looser one the whole tune may reach. It is not done here
because the arch plus the centre pull already narrow the phrase in practice, and because no source
found gives a phrase-range figure for this roster's genres — so it would be a second authored
number dressed as a fix. Left as a judgement, labelled as one, in `Melody.DegreeMin/DegreeMax`.

**One thing this document asserts that is not there.** The claimed off-by-one — "the call loop
tests `t < phraseTicks` *before* adding, so the call can overrun the phrase and the answer starts
on top of it" — is not a bug. Every onset the loop adds has `t < phraseTicks` by construction, and
what overruns is the last note's *duration*, which `Pattern` computes from the next onset rather
than from the drawn length. Nothing was changed for it.

---

## Phase 3 — the arranger

The one that matters. Every voice today picks its figure from its own small authored table and no
voice knows what any other is playing. The single exception is `_riffOnsets` — the chordal voice
renders first and the bass reads it (`Comp.cs:26` → `Bass.cs:43,122`) — and it is the only lockup
in the engine that is not a coincidence. Everything else is two tables that happen to agree: pop's
pad lands on the kick 100% of the time and ska's skank 1%, and neither number was decided.

### Shape

A new `Engine/Arrange.cs` — a `MusicGen` partial plus a `Skeleton` type, framework-free like
everything else under `Engine/`.

**A planning pass**, in `ComposePlan` between `BuildStructure` and the render loop
(`Compose.cs:178-215`). It can be: the tunes are `Pattern`s by then, the groove is `Pattern`s, the
figures are drawn (`Compose.cs:97-105`), and `SectionTicks` is arithmetic. Nothing it needs
requires a sample.

One `Skeleton` per section, published in `RenderSection` next to the section's other state
(`Compose.cs:331-336`, under the `── the section's own state ──` comment) — the same mechanism
`_energy` / `_feel` / `_keyShift` already use, so voices read it the way they read those.

`Skeleton` carries, on the section's own sixteenth grid:

- **`Accent[]`** — where the section leans, derived from the groove's kick and snare and the
  genre's measured accent weights.
- **`Seams`** — the phrase ends: every four bars, and the section boundary.
- **the tune's occupancy** — per cell, whether the tune has an onset, is holding, or is silent.

### Roles say *how*, never *what*

The role parameters go in `GenreProfile`, which is exactly the line `CLAUDE.md` already draws:
`BassKickLock` (how hard the bass agrees with the kick), `CompComplement` (how hard the comp avoids
cells the tune and bass occupy), an allowed-cell class for the comp, `SeamConverge`, `MutateRate`.

**The allowed-cell class is what keeps ska's skank offbeat by RULE rather than by table** — offbeat
only for ska, downbeats for punk, sixteenths for metal. That is the property that has to survive
the whole phase, and it is the reason the arranger cannot simply write onsets wherever the accent
grid is loud.

### The tables are seed material

The authored figures are not deleted. They are each genre's characteristic gestures, and the
arranger works on them:

- **`Drop`** an onset that collides with a tune landing.
- **`Add`** one on an accented cell nothing occupies.
- **`Displace`** one by a cell, within the allowed class.
- **`Recombine`** — take one bar of the phrase from another figure in the same genre's table.
- **`Quote`** — the authored figure verbatim.

That is where the state count comes from: figure × mutation × skeleton is combinatorial, where
today the whole rhythm section is a product of three table sizes.

**Choruses quote verbatim.** Today a chorus restores `_songComp` / `_songKeys` / `_songBass`
(`Compose.cs:315-326`) and that is the song's identity guarantee — every chorus must agree, which
is what makes a chorus a chorus. It survives intact; the mutation lives in the non-chorus sections,
which is also where today's per-section-type re-draw already lives.

### Drums stay table-driven, and that is not an inconsistency

They are not melodic and the grooves are fitted to the Groove MIDI corpus, so **the kit is the
measured reference the arranger writes against** — which is precisely why the skeleton's accent
grid is derived from it rather than drawn.

### Streams

`$"{_tag}:arr:{bk}"`, forked per section, so `ComposePlan`'s song stream is untouched and its
stated invariant holds unchanged: *every genre pulls the same number of values out of this stream*
(`Compose.cs:47-50`). Joint generation makes draw order a contract, so that rule gets **stricter**,
not looser — a role that reads another voice's planned line must not also change how many values
that voice drew.

### `_riffOnsets` should retire

Both lines are planned before either renders, so the render-order dance at `Compose.cs:437-451`
stops being necessary and the bass reads the *planned* comp line instead of the rendered one.
Metal's and punk's lockups become a role parameter at 1.0 rather than the engine's one deliberate
coincidence, and `RenderBassFromRiff`'s "skip anything shorter than a sixteenth that isn't `Ring`"
rule (`Bass.cs:124`) becomes a role rule too.

`Lead.cs:231-250` (punk's unison) reads `_riffOnsets` as well. **Fallback if the refactor gets
expensive: keep `_riffOnsets` and let the arranger populate it at plan time.** It is a
simplification, not a requirement of the phase.

### Judging it

This is where the convergence rule bites. From `--stats`:

- **distinct rhythm-section states go up** — that is the point of the phase, and punk's 12 is the
  number to beat.
- **the agreement figures stay within roughly ±10 points of baseline** — bass-on-kick
  27/47/41/89/45/53, comp-on-snare 1/31/60/16/70/10. Ska's comp-on-snare staying near **1%** is
  the single sharpest tell: if the skank starts landing on the backbeat, the skeleton has
  overridden the genre and the whole design is wrong.
- **the pairwise voice-onset agreement matrix does not collapse toward the diagonal.**

Then `--grid` (every voice must still sit on the song's grid — the suite asserts it), `--score`
on a couple of seeds to read what actually changed at a moment, and `--levels`, because **a comp
that plays more notes is louder as well as busier** and the balances are measured numbers.

Re-bless.

---

## Phase 4 — the form family

### The structural blocker, first

```csharp
internal static List<Part> BuildStructure( int genre ) =>
    new( GenreProfile.For( genre ).Form );                  // Structure.cs:236-237
```

Static, no `Rng`, no tag, no instance state — and called from **five** sites that each re-derive
it: `Compose.cs:178`, and `MusicGen.cs:122 / 171 / 209 / 221` (`Explain`, `Onsets`,
`BarTickLines`, `GridSamples`). Per-song variation therefore means **building the form once in
`ComposePlan` and caching it** as `List<Part> _form`, with all five sites reading the cache.
Anything else and the diagnostics' bar rulers disagree with the rendered song, which would make
`--score` and `--grid` quietly wrong exactly when they are most needed.

Draw it off a forked `$"{_tag}:form"` stream, for the same draw-count reason as Phase 3.

### Randomising the form is the wrong fix, and the row is not asking for it

A form is genre identity — which is why it lives in `GenreProfile` — and punk with a 16-bar solo
is not punk. Two mechanisms instead:

1. **A family.** `GenreProfile.Form` (`Part[]`) becomes `Forms` (`Part[][]`) with `FormWeights`,
   2–4 **authored** variants per genre. Authored is what keeps them all punk.
2. **Details drawn per song** over the chosen variant: section length from a per-genre allowed set
   (8 vs 12 vs 16), whether the optional section appears (PreChorus / Bridge / Breakdown / Solo —
   which is today the *only* thing distinguishing the six forms from each other), a doubled last
   chorus, a truncated final verse, whether the key lift happens at all. Country and pop modulate
   up a tone for the final chorus **every single song** today.

### Invariants — and these DO belong in the suite

Unlike the sweep, these are cheap and structural, and they are exactly the shape of assertion
`StructureTests` already carries:

- every section's `Bars % 4 == 0`. The hypermeasure is why the ending is 4 bars and not 2.
- every `Chorus` identical to every other in `(Bars, Energy, Feel, TempoMul)`. `KeyShift` is
  exempt — the final-chorus lift is the point.
- the song ends on `Ending`, at 4 bars, resolved.
- at least 2 choruses and at least 1 verse; total bars inside a band, so the stream never gets a
  30-bar or a 200-bar song.
- the existing `SectionTicks` / `BarBeats` mechanism check stays as it is (`Part.BarBeats` is
  still unused on purpose — see `Structure.cs:76-81`).

### One interaction to respect, not to fix

The tune is as long as the harmonic cycle, `Math.Clamp( _chordBars * _prog.Length, 2, 8 )`
(`Melody.cs:129`). So a 12-bar verse over an 8-bar tune restates it mid-phrase. Prefer lengths that
are the cycle or twice it where a section sings a tune; shorter-than-cycle is already handled —
`RenderTune` pulls the anchor back so the section's last bar lands on the tune's resolution
(`Melody.cs:157-159`), which is the machinery this phase lands on.

**This misalignment already exists** — an 8-bar section over a 6-bar tune (progression length 3,
`ChordBars` 2) does it today. It is a constraint to hold while choosing the allowed length sets,
not a blocker and not a new bug.

Re-bless.

---

## Cross-cutting rules this must not break

- **`Engine/` stays framework-free.** `System`, `System.Collections.Generic`, `System.Text` only;
  no `Sandbox.*`, no Emscripten-isms. Both targets glob the folder, so a new file under `Engine/`
  ships to both with no list to update.
- **`MusicGen.CopyOf`, never `Array.Clone`.** The s&box whitelist blacklists that one member and
  it compiles perfectly here and in the wasm build. Be sparing with `using static` for the same
  class of reason.
- **No `Config` field is added on this branch**, so `Cfg.To` / `Cfg.From` / `Cfg.Size` are
  untouched. If that stops being true, it is three places and the JS mirror.
- **The s&box surface stays** `MusicGen.{ Config, Channels, GenerateSamples, BeginPlan, Explain,
  WavFromSamples }` plus all of `VibeCodec`. `Explain` grows a *drawn* form read-out rather than a
  fixed one — and since it is the only way the s&box target gets inspected at all
  (`skafinity_explain`), it is worth printing the form variant and the drawn details there.
- **The `SkafinityPlayer` `[Property]` drift diff.** No `Config` default moves on this branch, so
  there is nothing to find — which is stated here precisely so the check does not get skipped out
  of habit on the next branch that *does* move one.
- **Re-bless once per phase, not once at the end.** Every phase here is a deliberate audible
  change, and a digest diff is only information if it is attributable to something.
- **The landing commit re-publishes and re-stages `web/_framework`.** `Engine/**` changed, so the
  committed bundle is stale until it does, and `tools/bundle-stamp.sh check` is what says so.
- **No `make` on this host** — run the Makefile's underlying `dotnet` / `node` commands.

---

## Files

| file | what happens to it |
|---|---|
| `test/engine/Stats.cs` | **new** — the sweep, Phase 0. Diagnostic only. |
| `test/engine/Program.cs` | `--stats` dispatch beside `--levels`; the Phase 4 form invariants in `StructureTests`. |
| `Engine/Melody.cs` | `Draw` rewritten (Phase 2); the tune seed strings (Phase 1); `DrawTunes`' `_prof.Lead` switch removed. |
| `Engine/Arrange.cs` | **new** — `Skeleton` + the arranger pass, Phase 3. |
| `Engine/Compose.cs` | the arranger pass in `ComposePlan`; `_skeleton` published in `RenderSection`; `FigureFor` factored out; the `_riffOnsets` render-order branch retires; `_form` cached. |
| `Engine/Structure.cs` | `BuildStructure` becomes per-song and instance-cached. |
| `Engine/GenreProfile.cs` | `Forms` + `FormWeights`; the tune vocabulary fields; the arranger's role parameters. |
| `Engine/Voices/*.cs` | each voice reads the skeleton and its role instead of looping its figure. |
| `Engine/MusicGen.cs` | the four `BuildStructure` call sites read `_form`; plan-only accessors for the sweep. |
| `Engine/Drums/*` | **unchanged.** The kit is the reference, not a client. |
| `PLAN.md` | the four rank-99 rows deleted when the branch lands. |
| `web/_framework` | re-published and re-staged in the landing commit. |

---

## Verification

```sh
# the engine suite — must stay green, and must stay ~25 s
dotnet run --project test/engine -c Release

# the sweep: run it before Phase 1 and after every phase, and diff
dotnet run --project test/engine -c Release -- --stats 500

# what the composer decided for one seed, and what happens at one moment
dotnet run --project test/engine -c Release -- --seed 0:rotaliate:0
dotnet run --project test/engine -c Release -- --score 0:rotaliate:0 12 20
dotnet run --project test/engine -c Release -- --grid

# after Phase 3: a busier comp is a louder comp
dotnet run --project test/engine -c Release -- --levels

# and the only check that is actually about the music
dotnet run --project test/engine -c Release -- --render 0:rotaliate:0 ~/song.wav
```

Then, before the landing commit: the full AOT publish, the node tests, and the bundle stamp.

**What none of that proves** is the thing the whole branch is for. The numbers say the states went
up and the parts did not converge; they cannot say the songs got better. Render one per genre and
listen, and listen to *two consecutive songs of the same genre* in particular — the defect this
branch exists to fix is only audible across songs, which is why it survived this long.
