using System;

namespace Skafinity;

/// <summary>How the genre's main chordal voice comps. The figure itself is a
/// <see cref="Pattern"/>; this says what a hit MEANS when that voice plays it.</summary>
enum CompStyle
{
	/// <summary>Ska-punk: the offbeat chop, short and bright.</summary>
	Skank,
	/// <summary>Rock: a two-bar power-chord riff motif — hits that ring, not an eighth-note wall.</summary>
	Riff,
	/// <summary>Country: the "chick" — a clean strum on the offbeats, over the bass's boom.</summary>
	BoomChick,
	/// <summary>Punk: relentless downstrokes, one chord per bar.</summary>
	Downstroke,
	/// <summary>Metal: the palm-muted gallop, chord accents on the ring hits.</summary>
	Gallop,
	/// <summary>Pop: a held pad under everything.</summary>
	Pad,
}

/// <summary>How the genre's SECOND chordal voice (keys / piano / synth) plays, when it has one.
/// </summary>
enum KeysStyle
{
	None,
	/// <summary>Rock: the syncopated Charleston organ comp.</summary>
	Stabs,
	/// <summary>Country: honky-tonk piano answering on 2 and 4.</summary>
	HonkyTonk,
	/// <summary>Pop: a sixteenth-note arpeggio over the pad.</summary>
	Arp,
}

/// <summary>What kind of line the lead plays. One <c>RenderLeadPhrase</c> served every genre with
/// the same rest chance, the same leap logic and the same register, which made the melody the
/// most interchangeable thing in the song.</summary>
enum LeadStyle
{
	/// <summary>Ska-punk: the horn line — call in the first phrase, answer in the second.</summary>
	HornLine,
	/// <summary>Rock: mid-register, bendy, spare.</summary>
	Bluesy,
	/// <summary>Country: pentatonic double-stops and heavy bends.</summary>
	DoubleStop,
	/// <summary>Metal: fast scalar runs across the whole register.</summary>
	Shred,
	/// <summary>Punk: mostly absent; doubles the guitar's riff when it does play.</summary>
	Unison,
	/// <summary>Pop: a two-bar hook, REPEATED — no other genre repeats a motif.</summary>
	Hook,
}

/// <summary>How a song lands. Every song used to end on the identical fixed pad — same
/// oscillator, same envelope, same length, whatever the genre and whatever the song — which is
/// why the last four seconds of every track sounded like the same track.</summary>
enum EndingStyle
{
	/// <summary>The band hits the tonic together and lets it ring out.</summary>
	Ring,
	/// <summary>A short, hard unison stab and then nothing. Punk and metal stop; they do not fade.
	/// </summary>
	StopHit,
	/// <summary>A real cadence: the V chord, then the tonic landing on beat 3.</summary>
	Cadence,
	/// <summary>The figure keeps going and falls away — the pop fade, without the fade-out.</summary>
	Fall,
}

/// <summary>
/// THE GENRE'S TUNE VOCABULARY — what kind of melody it writes, as opposed to what its lead
/// GUITAR does with one.
///
/// It is here rather than in a <c>_prof.Lead switch</c> inside <c>DrawTunes</c> because that
/// switch was the <c>if ( _genre == … )</c> smell one level removed: two genres sharing a
/// <see cref="LeadStyle"/> got the same tune vocabulary, which is half of why two genres' tunes
/// used to come back byte-identical. A vocabulary belongs to the genre, not to the lead's grammar.
///
/// EVERY NUMBER BELOW IS AUTHORED, not measured. There is no melodic corpus in this repo the way
/// there is a drum one, so these say what a genre's tunes should be made of and nothing more —
/// unlike the accent weights and the groove placements, which are fitted numbers and say so. Do
/// not write them up as if they were measured.
/// </summary>
readonly struct TuneVocab
{
	/// <summary>Weights over <see cref="Melody.Lengths"/> — the note lengths this genre sings in.
	/// This IS the genre's density: a table that leans on the quarter and the half writes a punk
	/// tune, one that leans on the eighth and the sixteenth writes a metal one.</summary>
	public readonly int[] LengthWeights;

	/// <summary>How often a cell is silence rather than a note. A rest is an OMITTED ONSET — the
	/// note before it simply holds longer — so this thins the line without needing a rest cell the
	/// renderer would have to know about.</summary>
	public readonly float Rest;

	/// <summary>How often the line jumps rather than steps.</summary>
	public readonly float Leap;

	/// <summary>Weights over <see cref="Melody.Answers"/> — how this genre answers its own call.
	/// </summary>
	public readonly int[] AnswerWeights;

	public TuneVocab( int[] lengths, float rest, float leap, int[] answers )
	{ LengthWeights = lengths; Rest = rest; Leap = leap; AnswerWeights = answers; }
}

/// <summary>Per-genre mix trim. Not a knob and not a per-song draw: a genre's records simply
/// sound like this. Scaled globally by <c>Config.GenreMix</c> so the house can dial the whole
/// effect back at runtime without a rebuild.</summary>
readonly struct MixProfile
{
	/// <summary>Multipliers on the master reverb wet, the stereo width, and the three broad
	/// bands (bass/low, body/mid, cymbals+top).</summary>
	public readonly float Reverb, Width, Low, Mid, High;

	public MixProfile( float reverb, float width, float low, float mid, float high )
	{ Reverb = reverb; Width = width; Low = low; Mid = mid; High = high; }
}

/// <summary>
/// Per-genre character that is NOT a user knob — the things a genre simply *is*, which a
/// listener should never have to dial in and a shuffle should never randomise across.
///
/// The line this table draws: it holds what the COMPOSER reads — harmony tables, tempo band,
/// swing band, harmonic rhythm, the FORM, the comp figures, the grooves, the lead grammar, the
/// accent weights. Per-voice TIMBRE (a genre's lead distortion, its bass register and filter,
/// its comp expression) stays next to the voice that renders it, because that is the sound of
/// one instrument rather than the identity of the genre.
///
/// Everything drawn here is drawn per song from the seed, exactly the way tempo is, so a genre
/// has a consistent character with room to vary between songs. EVERY GENRE PULLS THE SAME
/// NUMBER OF VALUES out of the song stream — a weighted draw is one <c>Next()</c> whatever the
/// table holds, and a genre with a fixed value consumes no draw at all in either direction.
/// </summary>
sealed class GenreProfile
{
	// ── Feel ──
	/// <summary>Chance this song swings AT ALL. Swing is a decision before it is a depth: a band
	/// alone cannot say "straight", because its floor is a swing so shallow the ear reads it as
	/// straight anyway — which made "how swung" and "swung or not" the same number, and neither
	/// one legible. A genre that never swings declares 0 and needs no band.</summary>
	public float SwingChance { get; init; }

	/// <summary>Swing band, as a fraction of an eighth note — how deep, GIVEN that the song swings.
	/// Because a straight song is now reachable directly, the floor here is what the genre's swing
	/// sounds like when it is present, not the point where it fades out.</summary>
	public float SwingMin { get; init; }
	public float SwingMax { get; init; }

	/// <summary>Chance this song takes a genuine 2:1 TRIPLET SHUFFLE instead of drawing from the
	/// swing band. A shuffle is a different feel, not a wider swing: widening the band to reach
	/// ~0.33 would just make ordinary songs sloppy on the way there. Ska-punk and country only.</summary>
	public float ShuffleChance { get; init; }
	public float ShuffleMin { get; init; } = 0.30f;
	public float ShuffleMax { get; init; } = 0.36f;

	/// <summary>The genre's own tempo band, and its uptempo band (drawn when the song rolls
	/// FAST). One band per genre is the point: metal at country's tempo is not metal.</summary>
	public int BpmMin { get; init; }
	public int BpmMax { get; init; }
	public int FastBpmMin { get; init; }
	public int FastBpmMax { get; init; }

	/// <summary>Always draw from the fast band — the genre has no laid-back mode.</summary>
	public bool AlwaysFast { get; init; }

	/// <summary>Where the listener's TEMPO knob SATURATES for this genre — the slowest and fastest
	/// this music is still itself.
	///
	/// The knob is one global 0.70–1.45 multiplier over whatever the genre drew, and a symmetric
	/// range chosen against no band in particular put its ends outside every genre at once: ska
	/// 268, metal 290 and punk 290 at the top, metal 63 and country 67 at the bottom. None of
	/// those are tempos these genres are, so the usable part of the slider was its middle.
	///
	/// Narrowing the knob would have been one number, but it takes headroom off punk and ska that
	/// they can genuinely use. The band is the genre's and the knob is the preference riding on
	/// top — so the knob keeps its full travel in the UI and each genre stops where it stops
	/// being itself. These are ceilings for the KNOB: the drawn band must sit inside them, so at
	/// neutral nothing is ever clamped.</summary>
	public int TempoFloor { get; init; }
	public int TempoCeil { get; init; }

	/// <summary>Bars per chord — the harmonic rhythm. 2 is the reggae/rock norm; 1 makes the
	/// four-chord loop itself the four-bar hypermeasure, which is what punk and pop do.</summary>
	public int ChordBars { get; init; } = 2;

	/// <summary>Base lean toward riding the ride cymbal rather than the closed hats. The song
	/// spreads around this and each section rolls against the result.</summary>
	public float RideLean { get; init; }

	/// <summary>True if the lead line is the ska horn section rather than a lead guitar.</summary>
	public bool HornLead { get; init; }

	// ── Form ──
	/// <summary>The genre's section map (see <see cref="SongForm"/>).</summary>
	public Part[] Form { get; init; }

	// ── Harmony ──
	/// <summary>The harmony tables this genre draws from — one weighted draw each, so a genre's
	/// draw count never depends on how many entries its tables have.</summary>
	public int[][] Scales { get; init; }
	public int[] ScaleWeights { get; init; }
	public int[][] Progressions { get; init; }
	/// <summary>Chord voicings in scale-degree space (see <see cref="Harmony"/>).</summary>
	public int[][] Voicings { get; init; }
	public int[] VoicingWeights { get; init; }

	// ── Parts ──
	/// <summary>The genre's bass library. Patterns carry their own length, so these are the
	/// two- and four-bar phrases the genre actually plays, not one bar repeated.</summary>
	public Pattern[] BassPatterns { get; init; }

	/// <summary>The comp figures of the genre's main chordal voice, and how to play them.</summary>
	public Pattern[] CompFigures { get; init; }
	public CompStyle Comp { get; init; }

	/// <summary>What the main chordal voice becomes once the section is loud enough, and the
	/// figures it plays there. Null (the default) means the genre comps one way all song.
	///
	/// This is a genre's DYNAMIC, not a second genre: the same voice, the same chord, a different
	/// instrument technique because the section asked for one. Third-wave ska is the case that
	/// needs it — a clean offbeat skank through the verse dropping into distorted power chords for
	/// the chorus is the single most recognisable thing about the style, and no amount of tuning
	/// the skank reaches it, because the chorus is not a louder skank but a different part.
	///
	/// The threshold is on the section's ENERGY rather than on its type, so it stays the same kind
	/// of value as everything else voices read — a genre that wanted its bridge loud would get it
	/// by giving the bridge energy, not by naming the bridge here.</summary>
	public Pattern[] LoudCompFigures { get; init; }
	public CompStyle LoudComp { get; init; }
	public float LoudFrom { get; init; } = 0.9f;


	/// <summary>The genre's FLOURISH: a two-bar variant of its comp figure whose tail breaks into
	/// thirty-seconds — the ska flick, the rock riff's pickup, country's chicken-pickin' pull-off,
	/// metal's tremolo into the bar line, punk's turnaround.
	///
	/// IT IS NOT AN ENTRY IN <see cref="CompFigures"/>, and that is the whole of this field. It was
	/// one, and figures are drawn per SECTION — so a song that drew it played the flourish every
	/// two bars for the length of a chorus, on a schedule, and a song that did not never heard the
	/// genre's signature gesture at all. A thing that arrives every two bars is not heard as an
	/// ornament, it is heard as the part; that is the same mistake as harmonising every long note
	/// (the country double-stop) and as running the double kick as a setting rather than a burst.
	/// Held out here it becomes a per-occurrence roll instead, which is also how the lead's
	/// ornaments have always worked.
	///
	/// Null where the genre has no such gesture — pop's lives on its arp, not on its pad.</summary>
	public Pattern CompOrnament { get; init; }
	public Pattern KeysOrnament { get; init; }

	/// <summary>The second chordal voice — the keys/piano/synth layer, where the genre has one.
	/// </summary>
	public Pattern[] KeysFigures { get; init; }
	public KeysStyle Keys { get; init; } = KeysStyle.None;

	/// <summary>The genre's drum grooves (see <see cref="DrumGroove"/>), drawn per song.</summary>
	public DrumGroove[] Grooves { get; init; }
	public int[] GrooveWeights { get; init; }

	/// <summary>How busy this genre's fills are, in HITS PER BAR across the whole kit — the target
	/// occupancy <see cref="MusicGen.RenderFill"/> scales its grid to.
	///
	/// MEASURED FOR ROCK AND ONLY FOR ROCK: 13.2 hits per bar over 204 bars of fill performance
	/// (130 files) in the Groove MIDI Dataset — see the placement block in
	/// <see cref="DrumGroove"/> for the source and the method. Everything else here is a design
	/// call ANCHORED on that one number rather than a second measurement wearing the same
	/// citation, and each says which way it leans and why.</summary>
	public float FillHits { get; init; } = 13f;

	/// <summary>Weights over <see cref="FillShape"/>, in declaration order — how this genre's fills
	/// are shaped, which is the half of a fill that density cannot say. Country's vocabulary is a
	/// lick out of the train beat, pop's is a programmed pickup, metal's is the roll.</summary>
	public int[] FillShapes { get; init; } = { 4, 3, 2, 1 };

	/// <summary>How this genre tunes its toms — the INTERVALS between the three drums. Which
	/// pitch the set starts from is the song's key; this is the character (see
	/// <see cref="TomTune"/>), and it is a genre's rather than a song's because it is how a
	/// drummer in that genre sets a kit up.</summary>
	public TomTune Toms { get; init; } = TomTune.Fourths;

	/// <summary>The energy at which the cymbal hand moves off the ride and onto a crash — the
	/// technique, not a second cymbal. Follows <see cref="LoudFrom"/>'s shape exactly: an energy
	/// threshold rather than a section type, so a genre that wants a crash-ridden bridge gives
	/// the bridge the energy. Over 1 means never, which is most genres: a crash-ride is a rock
	/// and metal gesture and it makes a ska verse sound like a mistake.</summary>
	public float CrashRideFrom { get; init; } = 1.01f;

	// ── Arrangement ──
	// How each part behaves against the section's skeleton (see Arrange.cs). These say HOW a voice
	// arranges itself and never WHAT it plays — the figure is still the genre's own authored
	// gesture, and the allowed CELL CLASS is what keeps ska's skank offbeat by rule rather than by
	// table. Exactly the line this file already draws everywhere else.

	/// <summary>The bass's role. Its <see cref="ArrangeRole.Kick"/> is how hard this genre's bass
	/// locks to the kick — the number that was previously an accident of two tables agreeing.
	/// </summary>
	public ArrangeRole BassRole { get; init; } = new( CellClass.Eighths, 0.6f, 0.1f, 0.3f );

	/// <summary>The main chordal voice's role. Its <see cref="ArrangeRole.Complement"/> is how hard
	/// it avoids cells the tune and the bass have already taken — a comp is a BED, and a bed that
	/// lands on every vocal syllable is a second vocal.</summary>
	public ArrangeRole CompRole { get; init; } = new( CellClass.Eighths, 0.2f, 0.5f, 0.3f );

	/// <summary>The role the main chordal voice takes where the section is LOUD enough that the
	/// genre changes technique (see <see cref="LoudComp"/>). Null falls back to
	/// <see cref="CompRole"/>; a genre whose loud technique lives on different cells than its quiet
	/// one has to say so, or its choruses get arranged against the wrong class — third-wave ska's
	/// chorus is punk downstrokes on the beat, not a skank, and the chorus is the most recognisable
	/// thing in the genre.</summary>
	public ArrangeRole? LoudCompRole { get; init; }

	/// <summary>The second chordal voice's role, where the genre has one. It is arranged last and
	/// so sees everything, which is what lets it answer rather than double.</summary>
	public ArrangeRole KeysRole { get; init; } = new( CellClass.Eighths, 0.1f, 0.7f, 0.2f );

	/// <summary>How often a NON-CHORUS section works on its figure rather than quoting it. A
	/// chorus always quotes: every chorus must agree, which is what makes a chorus a chorus.
	/// </summary>
	public float MutateRate { get; init; } = 0.6f;

	/// <summary>What kind of TUNE this genre writes (see <see cref="TuneVocab"/>). Distinct from
	/// <see cref="Lead"/>, which is what the lead instrument does when it improvises instead.
	/// </summary>
	public TuneVocab Tune { get; init; }

	/// <summary>How the lead phrases.</summary>
	public LeadStyle Lead { get; init; }
	/// <summary>Phrase length in bars, and the chance a phrase is a rest instead. A genre whose
	/// lead is mostly absent (punk) simply rests most phrases rather than being special-cased.
	/// </summary>
	public int LeadPhraseBars { get; init; } = 2;
	public float LeadSilence { get; init; }

	/// <summary>True if the bass follows the riff — reading the rhythm guitar's onsets rather
	/// than playing an independent pattern. Metal's two real modes (a pedal point, or doubling
	/// the riff) are both relational; punk gets the unison option from the same switch.</summary>
	public float RiffBassChance { get; init; }

	// ── Dynamics ──
	/// <summary>Velocity weights by metric position: the downbeat, the backbeat (2 and 4), and
	/// the offbeats. This is what a genre's accent pattern IS — country leans on the offbeat
	/// "chick", metal flattens everything, ska pushes the offbeat hardest.
	///
	/// FITTED TO A DATASET, not chosen — see the block below.</summary>
	public float AccentDown { get; init; } = 1f;
	public float AccentBack { get; init; } = 1f;
	public float AccentOff { get; init; } = 0.85f;

	// THE ACCENT WEIGHTS ARE A MEASUREMENT, the way the *Balance values are a measurement off
	// --levels. Source: Google Magenta's Groove MIDI Dataset (CC BY 4.0; 1150 files, 13.6 h, 10
	// drummers with 80%+ professionals, every performance played to a metronome at a stated tempo
	// so it is bar-aligned with no beat-tracking step, and carrying per-hit VELOCITY off a Roland
	// TD-11). Verified 2026-08-02: https://magenta.tensorflow.org/datasets/groove.
	// VELOCITY IS THE ACCENT WEIGHT, which is the whole reason this stopped being a taste call.
	//
	// NEITHER THE DATASET NOR THE TOOL THAT READ IT IS IN THIS REPO, and that is deliberate: no
	// code here may depend on the dataset existing. It was input that drove a decision, not an
	// asset — what is committed is these derived numbers and this citation, which is also what
	// CC BY asks for. The measurement is reproducible from the method rather than from a checked-in
	// script: read every note-on of every 4/4 performance of a style, fold it onto one bar at the
	// nearest sixteenth, bin by metric position, and normalise each drum by its OWN mean before
	// combining — otherwise position and instrument are confounded, since hats sit on every eighth
	// and would decide the offbeat bin by themselves. Scale the set so beat 3 = 1.00, which is the
	// literal 1f MetricGain returns there. The bar alignment checks itself: rock, pop and country
	// all put their measured snare peaks exactly on beats 2 and 4.
	//
	// Measured (beat_type=beat, 4/4), with the bars behind each row:
	//     style      down  back   off     bars
	//     rock       0.99  1.16  0.82     6521
	//     pop        0.92  1.02  0.57      319
	//     punk       1.09  1.05  1.10      278
	//     country    1.04  1.33  1.22      120
	//     reggae     0.81  0.92  0.84      286
	//
	// THESE ARE DRUM VELOCITIES AND THEY REACH NOTHING BUT THE DRUMS. A dataset can say what a
	// drummer does and it cannot say what the band does. MetricGain used to feed NoteGain, so these
	// weights set the level of every pitched note by where in the bar it fell — and a melody drawn
	// on the eighth grid alternates on and off the beat continuously, so the lead stepped 3 dB a
	// note in rock and 5 dB in pop out of metric position alone. Pop's 0.57 was flagged here as the
	// figure to revisit first if a genre read thin off the beat; what it actually needed was for the
	// numbers to stay where they were measured. KitGain is the only door out of MetricGain now.
	//
	// The row counts are wildly uneven: rock's 6521 bars settle rock, while country's 120 (two
	// performances) are an INDICATION that its backbeat and its offbeat "chick" both carry more
	// than the engine gave them — corroborated by its 27 fill performances landing off at 1.12 —
	// rather than a settled figure. Widen country before trusting it further.
	//
	// GENRE 0 IS DELIBERATELY UNCHANGED, AND GENRE 3 HAS NO DATA AT ALL. The dataset's closest
	// style to genre 0 is reggae, and genre 0 is no longer reggae — it is the third wave. Its
	// measured profile is a downbeat QUIETER than everything around it, which is the one drop
	// stated in velocity, so it describes the music genre 0 was retuned AWAY from; it is recorded
	// above because the reggae row of the roster plan will want it, not because it corrects
	// anything here. Metal is simply not in the dataset (E-GMD may answer it later), so metal's
	// flat weights remain a design decision and say so rather than borrowing rock's.

	/// <summary>The genre's mix trim.</summary>
	public MixProfile Mix { get; init; }

	/// <summary>How this genre's songs land, drawn per song. One weighted draw, like everything
	/// else here, so the ending costs the same single value in every genre.</summary>
	public EndingStyle[] Endings { get; init; }
	public int[] EndingWeights { get; init; }

	// Swing is a genre's identity, not a preference — and it is a yes/no before it is a depth.
	// Country is the genre that kept the shuffle; rock takes one a quarter of the time and is
	// otherwise straight; ska (third wave), metal, punk and pop never swing at all. Tempo and
	// swing are not independent in the real music either: the slow end of a lineage shuffles and
	// the fast end goes straight, which is why the uptempo roll halves the CHANCE of a swing.
	//
	// Tempo is the same kind of value. One shared 130–185 band made a metal song and a country
	// song run at the same speed, which is most of what "they all sound alike" was — so each
	// genre carries its own band and its own uptempo band.
	//
	// EACH BAND IS ANCHORED ON RECORDS, AND EVERY ANCHOR NAMES ITS COUNT. The trap is not finding
	// tempos, it is that a reported bpm does not say which pulse it counted: Slayer's "Raining
	// Blood" comes back as 89 from the same databases that give its thrash section as 216, and ska
	// is worse still (see genre 0). A number copied out of an aggregator without deciding where
	// the backbeat falls is not a measurement, it is a coin flip that LOOKS measured — which is
	// the failure mode these bands already had once. So each genre below records its anchors, and
	// a genre whose anchors are ambiguous keeps the band it has until somebody counts them by ear.
	// Ranges quoted as "the genre tables" are the published per-genre bpm guides, used only to
	// corroborate an anchor and never on their own.
	//
	// WHERE THIS ENGINE'S BEAT IS, so an anchor can be CONVERTED rather than guessed: read
	// DrumGroove. Every groove in genres 1–5 puts the snare on cells 2 and 6 of an eight-eighth bar
	// — beats 2 and 4 — so for those five the engine's bpm IS the ordinary backbeat-on-2-and-4
	// count, and an anchor converts by identity. Genre 0 is the exception and says so in its own
	// block: its skank fires once per beat, so ska counts the double-time reading. Pop's "half-time
	// backbeat" groove moves the snare to beat 3 alone, which halves the PULSE without touching the
	// tempo — a groove within the band, not a second counting convention, so a pop anchor is still
	// counted at the four-on-the-floor pulse. That much is settled from the source, and settling it
	// is what leaves only the per-record half-or-double question below instead of a per-genre
	// convention question stacked on top of it.
	static readonly GenreProfile[] Profiles =
	{
		// ── ska-punk — THE THIRD WAVE: straight and fast, clean skank verses into loud choruses ──
		// Era: 90s US ska-punk. The genre is NAMED for it (VibeCodec's "Ska-Punk") rather than
		// holding "Ska", which is the umbrella and is left free for two-tone and first wave.
		// Genre 0 was tuned as first-wave/rocksteady — shuffled, 130–175, a melodic reggae bass under
		// a roomy bass-forward mix and a form that deliberately did not climb. That is a real music
		// and it is not this one. The waves run ska (late 50s–60s, shuffled, out of American R&B) →
		// rocksteady → reggae, and then 2 Tone (1979, straight, punk-sharpened) → the third wave
		// (90s US ska-punk); the tempo band alone already put this genre in the last of those, so
		// every other value was describing a different era than the tempo was.
		new()
		{
			SwingChance = 0f,   // 2 Tone dropped the shuffle for punk's straight eighths; the third wave never took it back
			// Anchored on a record rather than on an adjective. "ska is fast" first put this band at
			// 150–190, which made a REFERENCE-FAST ska song the median and left the tempo knob no
			// usable travel upward. Reel Big Fish's "Sell Out" is ~103 bpm as counted, i.e. ~206 in
			// THIS engine's units — the skank fires once per beat here (the "and" of each beat), so
			// the engine counts the double-time reading and every ska tempo has to be converted
			// before it means anything. 206 is a fast ska song, so it belongs at the CEILING; the
			// ordinary band sits well under it and the knob is what reaches up there.
			BpmMin = 138, BpmMax = 172, FastBpmMin = 165, FastBpmMax = 190,
			TempoFloor = 118, TempoCeil = 210,
			ChordBars = 2, RideLean = 0.20f, HornLead = true,
			Endings = new[] { EndingStyle.StopHit, EndingStyle.Ring, EndingStyle.Cadence },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.SkaPunk,
			Scales = Harmony.SkaPunkScales, ScaleWeights = Harmony.SkaPunkScaleWeights,
			Progressions = Harmony.SkaPunkProgressions,
			Voicings = Harmony.SkaPunkVoicings, VoicingWeights = Harmony.SkaPunkVoicingWeights,
			BassPatterns = Harmony.SkaPunkBass,
			// The skank is OFFBEAT by rule: the arranger may move a chop, never onto a downbeat.
				// Ska's bass walks its own line rather than following the kick, which is why its
				// bass->kick agreement is the lowest on the roster and is meant to be.
				BassRole = new( CellClass.Eighths, kick: 0.35f, complement: 0.25f, seam: 0.35f ),
				CompRole = new( CellClass.Offbeats, kick: 0.05f, complement: 0.45f, seam: 0.30f ),
				// The chorus is punk's downstroke over ska's harmony, and it is on the BEAT.
				LoudCompRole = new( CellClass.Downbeats, kick: 0.40f, complement: 0.20f, seam: 0.40f ),
				MutateRate = 0.55f,
				CompFigures = CompFigure.SkaPunk, Comp = CompStyle.Skank,
			CompOrnament = CompFigure.SkaPunkFlick,
			// The dynamic that IS third-wave ska: the skank stops for the chorus and the same voice
			// plays power chords through a driven amp. LoudComp reuses punk's downstroke because the
			// technique genuinely is punk's — what keeps the genre distinct is that it only does this
			// half the time, over ska's harmony, with the horns still on top.
			LoudCompFigures = CompFigure.SkaPunkLoud, LoudComp = CompStyle.Downstroke,
			Grooves = DrumGroove.SkaPunk, GrooveWeights = new[] { 3, 2 },
			// A shade under rock: the horns answer the fill, so it does not have to fill the space
			// on its own. Ramp-led, with the flick off the last chop that its comp figures carry.
			FillHits = 12f, FillShapes = new[] { 4, 2, 2, 2 },
			Toms = TomTune.Fourths,
			// The horn line: eighth-led and busy, phrased in short answers. Ska's tune is the section
				// singing over an offbeat bed, so it leans on the eighth and does not sit on long notes.
				Tune = new TuneVocab( new[] { 2, 6, 2, 4, 1, 1 }, rest: 0.12f, leap: 0.20f,
					answers: new[] { 4, 3, 2, 1, 1 } ),
				Lead = LeadStyle.HornLine, LeadPhraseBars = 2, LeadSilence = 0.15f,
			AccentDown = 0.95f, AccentBack = 1.05f, AccentOff = 1.1f, // the offbeat is still the loud one
			// Dry and bright — a 90s record, not a 60s room. The high trim is a TIMBRE call and was
			// checked not to be a level one: moving it 0.95→1.15 shifts the kit's RMS by 0.02%,
			// because RMS is kick and snare energy and this rides the hats and cymbals on top.
			Mix = new MixProfile( 0.65f, 1f, 0.95f, 1.05f, 1.15f ),
		},
		// ── rock — 90s/00s ALTERNATIVE: mid-tempo minor vamps behind a straight backbeat ──
		// Era, written down the way the era lean asks: this is grunge and post-grunge alt-rock,
		// not 1971. It matters most here of all the genres, because "rock" is the widest umbrella
		// on the roster and the band was the one the tempo study actually moved.
		//
		// Anchored on records (2026-08-02, aggregator tempos cross-checked between songbpm,
		// tunebat and getsongbpm, all counting the backbeat on 2 and 4 so there is no half/double
		// ambiguity to resolve): RHCP "Californication" 96, Nirvana "Smells Like Teen Spirit" 117,
		// Foo Fighters "Everlong" 158. The old 110–160 band excluded the slow anchor outright and
		// treated the fast one as ORDINARY, which is why every rock song came out driving — so the
		// ordinary band drops to 95–140 (which is also where the genre tables put alt-rock, 115–130)
		// and Everlong's 158 becomes what it is, an uptempo rock song.
		new()
		{
			// A quarter of rock songs are shuffle-rock and the rest are dead straight. The old
			// 0–0.08 band was neither: it swung every song by an amount described as "a touch of
			// human push", which a swing warp cannot deliver — the warp moves EVERY offbeat eighth
			// late by the SAME amount, which is a groove. Human feel is DrumPush and Expression.
			SwingChance = 0.25f, SwingMin = 0.10f, SwingMax = 0.16f,
			BpmMin = 95, BpmMax = 140, FastBpmMin = 145, FastBpmMax = 172,
			TempoFloor = 80, TempoCeil = 185,
			ChordBars = 2, RideLean = 0.55f,
			Endings = new[] { EndingStyle.Ring, EndingStyle.StopHit, EndingStyle.Cadence },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Rock,
			Scales = Harmony.RockScales, ScaleWeights = Harmony.RockScaleWeights,
			Progressions = Harmony.RockProgressions,
			Voicings = Harmony.RockVoicings, VoicingWeights = Harmony.RockVoicingWeights,
			BassPatterns = Harmony.RockBass,
			// A riff and a bass that mostly move together, with the organ answering the gaps both
				// of them leave — which is what the Charleston comp is for.
				BassRole = new( CellClass.Eighths, kick: 0.65f, complement: 0.15f, seam: 0.40f ),
				CompRole = new( CellClass.Eighths, kick: 0.30f, complement: 0.40f, seam: 0.40f ),
				KeysRole = new( CellClass.Eighths, kick: 0.05f, complement: 0.75f, seam: 0.20f ),
				MutateRate = 0.65f,
				CompFigures = CompFigure.Rock, Comp = CompStyle.Riff,
			CompOrnament = CompFigure.RockPickup,
			KeysFigures = CompFigure.RockKeys, Keys = KeysStyle.Stabs,
			Grooves = DrumGroove.Rock, GrooveWeights = new[] { 3, 2 },
			FillHits = 13.2f, FillShapes = new[] { 4, 3, 2, 1 },   // measured, 204 bars of fill
			Toms = TomTune.Fourths, CrashRideFrom = 0.90f,
			// Alt-rock: quarters and eighths with room between them, and enough of an inverted
				// answer to keep a vocal from being one shape restated a step down.
				Tune = new TuneVocab( new[] { 1, 4, 2, 5, 2, 2 }, rest: 0.18f, leap: 0.20f,
					answers: new[] { 4, 3, 2, 1, 2 } ),
				Lead = LeadStyle.Bluesy, LeadPhraseBars = 2, LeadSilence = 0.20f,
			AccentDown = 0.99f, AccentBack = 1.16f, AccentOff = 0.82f,   // measured, 6521 bars
			Mix = new MixProfile( 1f, 1f, 1f, 1.05f, 1f ),
		},
		// ── country — 90s/00s NASHVILLE: the slowest band; a light shuffle under the train beat ──
		// Era: the Garth Brooks / Shania Twain / Brooks & Dunn radio decade, not the Bakersfield
		// 60s and not modern bro-country.
		//
		// Anchors (2026-08-02): "Friends in Low Places" 108, "Man! I Feel Like a Woman!" 125,
		// "Boot Scootin' Boogie" 131. Band UNCHANGED — 108 and 125 sit inside the ordinary band
		// and the 131 two-step is exactly what the uptempo band is for, so the anchors corroborate
		// the numbers rather than moving them. The published country range (80–120) still sits
		// under this one and is still ignored: it averages in the ballads and the old-time material
		// this genre is not.
		//
		// None of the three has a pulse to resolve. The half readings would be 54/62/65 and the
		// double 216/250/262, and country is neither of those at any point in the decade — which is
		// what makes these usable anchors where a thrash record is not.
		new()
		{
			// The genre that kept the shuffle. Straight country is real country, so this is not 1.
			SwingChance = 0.45f, SwingMin = 0.10f, SwingMax = 0.18f, ShuffleChance = 0.18f,
			BpmMin = 95, BpmMax = 130, FastBpmMin = 130, FastBpmMax = 150,
			TempoFloor = 78, TempoCeil = 162,
			ChordBars = 2, RideLean = 0.30f,
			Endings = new[] { EndingStyle.Cadence, EndingStyle.Ring, EndingStyle.Fall },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Country,
			Scales = Harmony.CountryScales, ScaleWeights = Harmony.CountryScaleWeights,
			Progressions = Harmony.CountryProgressions,
			Voicings = Harmony.CountryVoicings, VoicingWeights = Harmony.CountryVoicingWeights,
			BassPatterns = Harmony.CountryBass,
			// BOOM AND CHICK ARE THE SAME RULE STATED TWICE: the bass takes the beats and the
				// guitar takes the "and", so the two are locked by being each other's complement
				// rather than by playing together. The cell classes carry it.
				BassRole = new( CellClass.Downbeats, kick: 0.75f, complement: 0.20f, seam: 0.35f ),
				CompRole = new( CellClass.Offbeats, kick: 0.05f, complement: 0.40f, seam: 0.30f ),
				KeysRole = new( CellClass.Eighths, kick: 0.05f, complement: 0.70f, seam: 0.25f ),
				MutateRate = 0.60f,
				CompFigures = CompFigure.Country, Comp = CompStyle.BoomChick,
			CompOrnament = CompFigure.CountryPickOff,
			KeysFigures = CompFigure.CountryKeys, Keys = KeysStyle.HonkyTonk,
			Grooves = DrumGroove.Country, GrooveWeights = new[] { 3, 2 },
			// The sparsest kit on the roster and the most gestural: country's fill vocabulary is a
			// lick out of the train beat's ghosted snare, not a roll across the toms.
			FillHits = 10f, FillShapes = new[] { 3, 2, 2, 4 },
			Toms = TomTune.Thirds,
			// Nashville: long held notes with a sixteenth lick between them, and the most
				// conservative answer set on the roster — a country hook restates and resolves.
				Tune = new TuneVocab( new[] { 2, 4, 1, 5, 2, 2 }, rest: 0.20f, leap: 0.25f,
					answers: new[] { 5, 3, 1, 1, 1 } ),
				Lead = LeadStyle.DoubleStop, LeadPhraseBars = 2, LeadSilence = 0.25f,
			// Boom AND chick carry weight — and the measurement says both carry MORE than this
			// genre was giving them. Thin (120 bars), so it is an indication rather than settled.
			AccentDown = 1.04f, AccentBack = 1.33f, AccentOff = 1.22f,
			Mix = new MixProfile( 0.75f, 0.85f, 1f, 1.05f, 0.95f ), // dry and centred
		},
		// ── metal — 90s/00s GROOVE AND THRASH: the widest band, doom-slow through thrash ──
		// Era: Pantera, late Metallica, the turn-of-the-century groove/nu lineage.
		//
		// Ordinary-band anchors (2026-08-02): Pantera "Walk" 118, "Cowboys from Hell" 112,
		// Metallica "Enter Sandman" 123 — all squarely inside 90–160, which is the corroboration,
		// and none of them ambiguous (a groove-metal half-time riff at 118 is not 59 and not 236).
		//
		// THE UPTEMPO BAND MOVED, AND THE COUNT IS WHY. Aggregators return Slayer's "Angel of Death"
		// at 106–108, which is the HALF reading: the transcribed backing tracks for it are published
		// at 50% = 104, 90% = 187, 100% = 208 and 105% = 218, four figures whose arithmetic only
		// closes at a base of ~208. Those are a player's count — a backing track has to be at the
		// pulse the player counts — and 208 is where the snare lands on 2 and 4, which is this
		// engine's beat. Metallica's "Master of Puppets" holds ~212 the same way. So thrash's real
		// pulse sits ABOVE the old 160–200 ceiling, and a genre whose fast band stopped at 200 could
		// not reach the records it is named for. 170–210, saturating at 225.
		//
		// This is the row-40 obstacle actually being cleared rather than restated: the ambiguity was
		// never resolvable from a bpm field, and it did not need an ear either — it needed a source
		// that states its own pulse. "Raining Blood" (89 vs 216) still has no such source and is
		// still not used as an anchor.
		new()
		{
			SwingChance = 0f,   // machine-straight; the old 0–0.02 band was ~3 ms and claimed a feel it never had
			BpmMin = 90, BpmMax = 160, FastBpmMin = 170, FastBpmMax = 210,
			TempoFloor = 70, TempoCeil = 225,
			ChordBars = 2, RideLean = 0.65f,
			Endings = new[] { EndingStyle.StopHit, EndingStyle.Ring },
			EndingWeights = new[] { 4, 2 },
			Form = SongForm.Metal,
			Scales = Harmony.MetalScales, ScaleWeights = Harmony.MetalScaleWeights,
			Progressions = Harmony.MetalProgressions,
			Voicings = Harmony.MetalVoicings, VoicingWeights = Harmony.MetalVoicingWeights,
			BassPatterns = Harmony.MetalBass,
			// The tightest lockup on the roster, and the only one that was ever deliberate: metal's
				// bass doubles the riff. It stays a RELATION rather than a role parameter (see
				// RiffBassChance), so what this sets is how the gallop itself is arranged.
				BassRole = new( CellClass.Sixteenths, kick: 0.85f, complement: 0.05f, seam: 0.35f ),
				CompRole = new( CellClass.Sixteenths, kick: 0.45f, complement: 0.25f, seam: 0.45f ),
				MutateRate = 0.55f,
				CompFigures = CompFigure.Metal, Comp = CompStyle.Gallop,
			CompOrnament = CompFigure.MetalTremolo,
			Grooves = DrumGroove.Metal, GrooveWeights = new[] { 3, 2 },
			// The one genre whose fill really is the wall. It is also the genre that showed the
			// density model up: past ~13.4/bar a flat scale had nothing left to give, so what metal
			// asks for above rock arrives as sixteenth ornament (see FillChances).
			FillHits = 14f, FillShapes = new[] { 2, 5, 2, 1 },
			Toms = TomTune.Wide, CrashRideFrom = 0.80f,
			// The only genre whose tune is written at the sixteenth, and the one that rests least:
				// metal's vocal sits ON the riff rather than in the gaps a riff leaves.
				Tune = new TuneVocab( new[] { 5, 6, 1, 3, 1, 1 }, rest: 0.08f, leap: 0.35f,
					answers: new[] { 3, 2, 2, 2, 3 } ),
				Lead = LeadStyle.Shred, LeadPhraseBars = 2, LeadSilence = 0.12f,
			RiffBassChance = 0.75f,
			AccentDown = 1f, AccentBack = 1f, AccentOff = 0.95f,    // deliberately flat: it's a wall
			Mix = new MixProfile( 0.45f, 1f, 1.05f, 0.85f, 1.05f ), // dry, mid-scooped
		},
		// ── punk — 90s SKATE PUNK: always hot, a chord per bar so the loop IS the hypermeasure ──
		// Era: NOFX / Bad Religion / Rancid, the melodic-hardcore end of the decade rather than
		// 1977 or pop-punk radio.
		//
		// Anchors (2026-08-02): NOFX "Linoleum" 198, Bad Religion "American Jesus" 181, Rancid
		// "Roots Radicals" 162 — counted where the snare falls on 2 and 4, which at these tempos is
		// also the only reading that is punk (the halves, 81–99, are the tempos these songs get
		// reported at when a detector locks onto the backbeat instead of the pulse). The top of the
		// band was already right; the FLOOR was not, and 165 excluded the slowest of the three
		// outright. 160–200. The published punk range (150–180) remains the umbrella and skate punk
		// still lives at the top of it and past it.
		new()
		{
			SwingChance = 0f,   // machine-straight
			BpmMin = 160, BpmMax = 200, FastBpmMin = 160, FastBpmMax = 200, AlwaysFast = true,
			TempoFloor = 130, TempoCeil = 225,
			ChordBars = 1, RideLean = 0.20f,
			Endings = new[] { EndingStyle.StopHit, EndingStyle.Ring },
			EndingWeights = new[] { 5, 1 },
			Form = SongForm.Punk,
			Scales = Harmony.PunkScales, ScaleWeights = Harmony.PunkScaleWeights,
			Progressions = Harmony.PunkProgressions,
			Voicings = Harmony.PunkVoicings, VoicingWeights = Harmony.PunkVoicingWeights,
			BassPatterns = Harmony.PunkBass,
			// Downstrokes are on the beat and nowhere else — that is the technique, and the cell
				// class is what stops the arranger syncopating a genre whose whole idea is that it
				// does not. The least mutated of the six for the same reason.
				BassRole = new( CellClass.Eighths, kick: 0.55f, complement: 0.10f, seam: 0.40f ),
				CompRole = new( CellClass.Downbeats, kick: 0.35f, complement: 0.20f, seam: 0.40f ),
				MutateRate = 0.40f,
				CompFigures = CompFigure.Punk, Comp = CompStyle.Downstroke,
			CompOrnament = CompFigure.PunkTurnaround,
			Grooves = DrumGroove.Punk, GrooveWeights = new[] { 3, 2 },
			// Busy, but at punk's tempo a bar of fill is over in a second: the pickup is the shape
			// that reads there, because there is no room for anything longer to develop.
			FillHits = 13f, FillShapes = new[] { 3, 4, 3, 1 },
			Toms = TomTune.Fixed, CrashRideFrom = 0.85f,
			// Shouted: quarters and halves, the fewest notes on the roster, and the most rests —
				// a skate-punk hook is four words held over a wall of eighths, not a melisma.
				Tune = new TuneVocab( new[] { 1, 4, 1, 6, 1, 3 }, rest: 0.22f, leap: 0.20f,
					answers: new[] { 5, 3, 1, 1, 1 } ),
				Lead = LeadStyle.Unison, LeadPhraseBars = 2, LeadSilence = 0.65f,
			RiffBassChance = 0.35f,
			// Punk's offbeat is as loud as its downbeat — the relentless eighth has no dynamic in
			// it, which is the measurement disagreeing with the 0.9 this genre used to assume.
			AccentDown = 1.09f, AccentBack = 1.05f, AccentOff = 1.1f,
			Mix = new MixProfile( 0.7f, 0.95f, 0.95f, 1.1f, 1f ),
		},
		// ── pop — 90s/00s RADIO POP: four-on-the-floor, or the half-time backbeat under it ──
		// Era: the teen-pop and post-teen-pop singles decade.
		//
		// Anchors (2026-08-02), and they come in the genre's two grooves rather than one. The
		// four-on-the-floor side: Madonna "Hung Up" 125, Kylie Minogue "Can't Get You Out of My
		// Head" 126 (its session began on a 125 bpm drum loop, which is as unambiguous as an anchor
		// gets), Cher "Believe" 133 — the first two inside the ordinary band and the third exactly
		// what the uptempo band is for. The HALF-TIME BACKBEAT side, which this genre's second
		// groove is and which nothing had ever been anchored against: Britney Spears "...Baby One
		// More Time" 93 and Backstreet Boys "I Want It That Way" 99, both UNDER the 100 floor.
		// THE FLOOR STAYS AT 100 ANYWAY, and the half-time pair is why the reasoning is written out
		// rather than the number changed. Tempo is drawn BEFORE the groove is (ComposePlan), so a
		// floor low enough for a half-time record is a floor the four-on-the-floor groove draws
		// from too — and 92 under a four-on-the-floor kick is not a pop single, it is a pop single
		// running slow. Two anchors that share a groove do not move a band that both grooves read;
		// they describe how far under the band that ONE groove goes, and the genre already reaches
		// there through TempoScale (TempoFloor 84, comfortably past both). Coupling the band to the
		// groove would fix it exactly, and is not worth a mechanism that only pop would ever use.
		// The published pop range (100–130) and the dance anchors agree with the floor as it is.
		//
		// *NSYNC "Bye Bye Bye" is deliberately NOT an anchor: it is reported at 173 and at 86–87 and
		// nothing here decides which, so it is exactly the coin flip the header warns about.
		new()
		{
			SwingChance = 0f,   // quantised by construction — the grid is the genre
			BpmMin = 100, BpmMax = 128, FastBpmMin = 124, FastBpmMax = 140,
			TempoFloor = 84, TempoCeil = 152,
			ChordBars = 1, RideLean = 0.30f,
			Endings = new[] { EndingStyle.Fall, EndingStyle.Ring, EndingStyle.Cadence },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Pop,
			Scales = Harmony.PopScales, ScaleWeights = Harmony.PopScaleWeights,
			Progressions = Harmony.PopProgressions,
			Voicings = Harmony.PopVoicings, VoicingWeights = Harmony.PopVoicingWeights,
			BassPatterns = Harmony.PopBass,
			// The pad is a held bed and barely arranges at all; the ARP is where pop's movement is,
				// and it is the voice that has to stay out of the vocal's way.
				BassRole = new( CellClass.Eighths, kick: 0.80f, complement: 0.10f, seam: 0.30f ),
				CompRole = new( CellClass.Eighths, kick: 0.20f, complement: 0.35f, seam: 0.25f ),
				KeysRole = new( CellClass.Sixteenths, kick: 0.05f, complement: 0.80f, seam: 0.20f ),
				MutateRate = 0.50f,
				CompFigures = CompFigure.Pop, Comp = CompStyle.Pad,
			KeysFigures = CompFigure.PopArp, Keys = KeysStyle.Arp,
			KeysOrnament = CompFigure.PopArpRun,
			Grooves = DrumGroove.Pop, GrooveWeights = new[] { 3, 2 },
			// Programmed and sparse — a pop fill is a single reversed crash or a two-hit pickup far
			// more often than it is a drummer going round the kit.
			FillHits = 8.5f, FillShapes = new[] { 3, 1, 4, 3 },
			Toms = TomTune.Fixed,
			// The DOTTED EIGHTH is the pop signature — a hook that pulls against the four-on-the-floor
				// instead of sitting on it — and the new-tail answer is what a chorus does: same line,
				// different landing.
				Tune = new TuneVocab( new[] { 2, 5, 3, 4, 2, 2 }, rest: 0.20f, leap: 0.22f,
					answers: new[] { 3, 4, 2, 1, 1 } ),
				Lead = LeadStyle.Hook, LeadPhraseBars = 2, LeadSilence = 0.20f,
			// The widest gap between what was assumed and what was measured, and the one to
			// revisit first: a programmed pop kit puts its off-beat hats far under the pulse.
			AccentDown = 0.92f, AccentBack = 1.02f, AccentOff = 0.57f,
			Mix = new MixProfile( 1.1f, 1.2f, 1.1f, 0.95f, 1.15f ),  // wide, bright, loud sub
		},
	};

	public static int Count => Profiles.Length;

	public static GenreProfile For( int genre ) => Profiles[Math.Clamp( genre, 0, Profiles.Length - 1 )];

	/// <summary>This song's swing. Either a draw from the genre's band, or — for the genres that
	/// have one — a genuine 2:1 triplet shuffle, which is a FEEL rather than a wider band.
	/// Both paths consume the same two draws so the choice shifts nothing downstream.</summary>
	/// <param name="fast">Uptempo songs tighten toward the straight end — at speed a wide
	/// shuffle stops reading as a groove and starts reading as a drag. It halves the CHANCE, never
	/// the depth: scaling the depth pushed the shallow end of the band under the threshold where
	/// swing is audible at all, and it did it worst exactly where the eighth was already shortest,
	/// so a fast song's "reduced swing" was a straight song wearing a number. A song either swings
	/// at a depth its genre means or it does not swing.</param>
	public float DrawSwing( Rng rng, bool fast )
	{
		bool shuffle = rng.Chance( ShuffleChance ) && !fast;
		bool swung = rng.Chance( fast ? SwingChance * 0.5f : SwingChance );
		float t = rng.Next();
		if ( shuffle ) return ShuffleMin + t * (ShuffleMax - ShuffleMin);
		return swung ? SwingMin + t * (SwingMax - SwingMin) : 0f;
	}

	/// <summary>This song's tempo, drawn from the genre's band (its uptempo band when
	/// <paramref name="fast"/>) and then scaled by the listener's TEMPO knob. The band is the
	/// genre's; the knob is the preference riding on top, so a song can be pushed or dragged
	/// without a country song ever ending up at metal's tempo.
	///
	/// The knob SATURATES at the genre's own <see cref="TempoFloor"/>/<see cref="TempoCeil"/>
	/// rather than at one shared 40–300. A single symmetric 0.70–1.45 multiplier cannot be right
	/// for six bands at once: its ends were ska 268, metal 290 and country 67, so the ends of the
	/// slider were unplayable everywhere and only its middle was usable.</summary>
	public int DrawBpm( Rng rng, bool fast, float tempoScale )
	{
		int lo = fast ? FastBpmMin : BpmMin, hi = fast ? FastBpmMax : BpmMax;
		int bpm = lo + rng.Int( Math.Max( 1, hi - lo + 1 ) );
		return Math.Clamp( (int)MathF.Round( bpm * tempoScale ), TempoFloor, TempoCeil );
	}

	/// <summary>This song's groove, drawn from the genre's own table — one weighted draw, never
	/// a shared <c>switch</c> default. Rock, country and punk used to fall through to the same
	/// straight backbeat, which is three of six genres playing identical drums.</summary>
	public DrumGroove DrawGroove( Rng rng ) => rng.PickWeighted( Grooves, GrooveWeights );

	/// <summary>This song's lean toward the ride cymbal, spread ±0.25 around the genre's base so
	/// some songs strongly prefer one. Each SECTION then rolls its own hats-or-ride against it.</summary>
	public float DrawRidePref( Rng rng ) => Math.Clamp( RideLean - 0.25f + 0.5f * rng.Next(), 0f, 1f );
}
