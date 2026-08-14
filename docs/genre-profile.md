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
The seed's genre is an INDEX (one hex char, its own part of the seed), so the display name is
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

**AND THAT IS WHY NOTHING MAY ROOT A CHORD ON THE ONE DEGREE WHOSE FIFTH IS A TRITONE.** Forcing the
fifth perfect is right for the SHAPE and it buys that shape, on that one degree, by importing a
pitch from **outside the scale** — so the chord arrives consonant, confident and in another key,
which is worse than the tritone it replaced rather than better, and `NearestSoundingTone` then snaps
the tune onto it. **The progression tables are roman numerals wearing degree numbers**: a row
commented `I–IV–vi–V` is that in the genre's home mode and something else in the mode beside it —
lydian's degree 3 is the ♯4, not the 4th; major's degree 6 is the leading tone, not the ♭VII; aeolian's
degree 1 is the ♮2, not the ♭II. So the changes go through `Harmony.PlayableProgressions(scale, progs)`
as they are drawn, which moves each root back two degrees until its fifth is perfect.

**The law is that the substitute is the chord CONTAINING the degree it replaces**, and it holds for
every scale this engine draws: lydian's ♯4 is II's third, dorian's ♮6 is the major IV's, phrygian's
5th is ♭III's, major's leading tone is V's. That is not a coincidence to be tuned — a mode's
characteristic note lives in a chord, and rooting a chord on it is the mistake. The consequence
worth carrying: **a mode is a TRANSFORMATION of the progression table, not a filter over it.** One
table reads as a different playable loop under each of a genre's scales, so per-mode tables would
be both more work and less variety. A row is dropped only where the move makes two adjacent chords
the same where the written row changed chord (a four-chord loop that has become three is not the
row that was authored), or where it lands on an earlier row's substitution. Adding a scale or a
progression means re-reading the note above `Harmony.Rootable`; the engine test asserts that no
drawn chord sits on an unrootable degree, that the substitute really does contain the degree it
replaces, and that no substitution turns a changing loop into a pedal.

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
is the only check on these voices that does not need ears. **`--render tag:n[:genre][:vibe] [path]`** writes a
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

The wire is ONE GLOBAL GRID: every voice in every genre, whether or not this genre plays it — see
the `VibeCodec` header and `docs/seed-format.md`. A vibe is therefore the same length in every
genre and is always longer than the genre's own knob count, so never assert a relationship between
the two. A genre chooses which cells it SHOWS; it never chooses what a cell means.

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

