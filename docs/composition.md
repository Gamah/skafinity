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
  They are TWO tables, though, and the split is on phrase LENGTH rather than on genre: at
  `MinPhraseBars` only `Contrasting` puts a second rhythm into a tune, so the other two shapes hand
  the most exposed voice one two-bar rhythm to sing four times — the shape the period exists to be
  bigger than, arriving one level up. `Melody.ShortShapeWeights` leans a floor-length tune toward
  `Contrasting` without forbidding a parallel period, which is a real shape at two bars too.
- **A parallel period varies only how it answers, so the answer has to actually vary.** The
  consequent's `AnswerOp` is drawn WITHOUT REPLACEMENT against the antecedent's (by zeroing a
  weight, so it stays one draw and the stream does not shift). The same operator over the same call
  is the same phrase but for its last note, and a period whose second half is its first half with a
  different landing is a two-phrase tune wearing four — which is what it was, on any song where
  those two draws agreed.
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
2.00 by construction before the period, 1.25–1.42 and 3.35–3.49 with it, and 1.46–1.57 and
3.64–3.73 once the answer was drawn without replacement and a floor-length tune leaned toward
`Contrasting` (100 songs × 6 genres). A period has at most two rhythms in it, so the rhythm figure
is reading the `Contrasting` rate almost directly: half of tunes now, a quarter before.
**A verse is already sparser than its chorus** and does not need a mechanism of its own — `move`
(1 for the chorus, 0.8 for the verse) leans the length draw toward the long end, which is what the
`LongFrom` split in `Melody.Lengths` is for. One song where the verse has more notes than the
chorus is the draw, not the rule. Its across-song `distinct` counts are
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

