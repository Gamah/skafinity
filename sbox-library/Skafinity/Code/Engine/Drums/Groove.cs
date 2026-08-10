using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

/// <summary>
/// One drum groove: what the kick, the snare and the cymbal play.
///
/// Grooves used to be five cases in a <c>switch</c>, and rock, country AND punk all resolved to
/// the same <c>default</c> straight backbeat — three of six genres playing identical drums under
/// different guitars. A groove is a set of patterns now, the same way harmony is a set of
/// tables, and each genre draws from its own.
///
/// Cell values: the kick has none (an onset is a kick). A snare cell is 0 for a hit and
/// <see cref="Ghost"/> for a ghost note. A cymbal cell is 0 for the closed/bow articulation and
/// <see cref="Open"/> for the open hat / ride bell — which of the two instruments plays is the
/// section's hats-or-ride roll, not the groove's business.
///
/// WHAT IS MEASURED IS THE TABLE, AND WHAT SHIPS IS ARRANGED FROM IT. Read this before quoting
/// any number below. The placements were fitted to a played corpus and they are real; the engine
/// then draws a groove per SECTION and works on its kick and snare against the section's skeleton
/// (see <see cref="MusicGen.ArrangeKit"/>), so a bar that reaches a listener is a mutation of a
/// measured pattern rather than the pattern itself. Three things follow and they are separable:
///
///   * <b>the tables are measured SEED material.</b> The placements are real, the three
///     corrections below are still why these tables look the way they do, and the arranger never
///     invents a gesture the genre does not have.
///   * <b>what the engine plays is a design call</b>, bounded by the SPINE (see
///     <see cref="SpineOf"/>) — the struck backbeat and the downbeat kick, which mutation may not
///     reach, so a genre's identity survives being arranged.
///   * <b>the accent weights are untouched and remain measured.</b> Velocity was a separate
///     question off the same pass and nothing about arranging placements reaches it.
///
/// RESTORING THE MEASURED OCCUPANCY WAS CONSIDERED AND LOST, and this is recorded because the
/// paragraph below is what will make a future session rediscover it. The pass read a DISTRIBUTION
/// — what fraction of bars carry each drum at each position — and then thresholded it to binary
/// cells, so the variance was measured and thrown away at authoring time; drawing the cells from
/// those probabilities instead would give bar-to-bar variation that is the corpus's own rather
/// than anyone's invention, and the near-certain positions would be a genre guard for free.
/// (<see cref="MusicGen.FootOccupancy"/> is the one place it survives.) It lost on three counts:
/// it caps variety at whatever the dataset's variance happens to be, it needs a fresh pass over
/// Groove MIDI that neither this repo nor its tooling contains, and metal is not in the dataset at
/// all. It is the fallback if free mutation ever turns out to wreck the genres, and it is scoped.
///
/// This distinction is <see cref="GenreProfile.FillHits"/>'s, and it is here for the reason that
/// block gives in its own words: a sentence in this register is READ as a measurement, so leaving
/// the header saying "where the hits fall is measured" would launder an arrangement into a
/// citation.
///
/// WHERE THE TABLES' HITS FALL IS MEASURED, the same way the accent weights in
/// <see cref="GenreProfile"/> are, off the same source: Google Magenta's Groove MIDI Dataset
/// (CC BY 4.0; verified 2026-08-02, https://magenta.tensorflow.org/datasets/groove). Method, since
/// neither the dataset nor the reader is in this repo: fold every note-on of every 4/4 performance
/// of a style onto one bar at the nearest sixteenth and read each drum's OCCUPANCY per metric
/// position — what fraction of bars carry that drum there. Occupancy answers placement; velocity
/// answered the accents. The two are separate questions off one pass.
///
/// The three placements that disagreed with the tables, and what each one moved:
///   * country hi-hat — ~84% on the OFFBEAT eighth against ~36% on the beat, while both country
///     grooves put the cymbal on the pulse and nowhere else. The "chick" is the & and the tables
///     had it on the beat, which is the single largest mismatch the pass turned up.
///   * rock kick — far more &-of-1 and &-of-3 than the two-bar backbeat spent, so each bar of the
///     pair gains one pushed kick.
///   * punk snare — measures on very nearly every eighth rather than on 2 and 4 alone. It is the
///     train beat's vocabulary at punk's tempo: the backbeat is struck and everything between it
///     is ghosted.
///
/// MIND THE SAMPLE SIZES, which are wildly uneven and travel with any figure taken from here: rock
/// is 6521 bars and settles rock, punk 278, country 120 from two performances. Country's is thin
/// enough that the 84/36 split is an INDICATION — it is acted on because the direction is
/// unambiguous and the table said the opposite, not because 120 bars settle a number.
/// </summary>
sealed class DrumGroove
{
	public const int Ghost = 1;

	// ── The cymbal hand's vocabulary ──
	// Open KEEPS THE VALUE 1, so every table above means exactly what it meant when 0-or-open was
	// the whole language. A hi-hat is a pedal and a pedal is a distance, so what a cell says is
	// how far open — except for the two articulations that are not a distance at all: the foot
	// closing the cymbals with no stick on them, and the foot opening and shutting them again.
	public const int Open = 1;
	/// <summary>Half open: the "sloshy" hat. It is not the midpoint of a switch — see RenderHat's
	/// geometric map, which is what makes this a position a foot can actually hold.</summary>
	public const int Half = 2;
	/// <summary>The foot chick: the hat speaking on its own, with no stick involved.</summary>
	public const int Foot = 3;
	/// <summary>Foot splash: opened and shut in one motion.</summary>
	public const int Splash = 4;

	public string Name { get; init; }
	public Pattern Kick { get; init; }
	public Pattern Snare { get; init; }
	public Pattern Cymbal { get; init; }

	/// <summary>Extra ghost-note propensity on top of what the pattern names — the "busy" layer's
	/// scaling factor for this groove.</summary>
	public float GhostRate { get; init; } = 1f;

	/// <summary>Chance of a crash on the section's first downbeat.</summary>
	public float CrashOnOne { get; init; } = 0.35f;

	/// <summary>
	/// THE SPINE: which of a groove's onsets ARE the genre, and so may not be dropped or moved.
	///
	/// This is the drums' answer to <see cref="CellClass"/>, and it is what stops arranging the kit
	/// from eroding the three measured tells the corpus pass corrected. It is a LAW rather than a
	/// per-groove list of ticks, because a list is a table that has to be re-authored every time a
	/// groove is added and gets it wrong silently when nobody does:
	///
	///   * <b>every STRUCK snare</b>. The backbeat is where a genre puts it — 2 and 4 in most of
	///     them, 3 alone in pop's half-time — and a rule phrased in beat numbers would be wrong for
	///     whichever genre disagrees. The ghosts around it are the density and stay arrangeable,
	///     which is the whole of what punk's snare has to say: strike two, ghost the rest.
	///   * <b>every kick ON A BEAT.</b> A KICK ON THE BEAT IS THE PULSE; A KICK OFF IT IS THE PUSH,
	///     and the push is the thing a drummer varies. Protecting the bar's first beat alone was
	///     not enough and the failure was specific rather than general: beat 1 held at 96–97% in
	///     every genre while every OTHER anchor eroded — country's beat 3, half of boom-chick, went
	///     missing in 23% of bars, pop's beat 4 in 24%, rock's beat 3 in 15%. The kick count per
	///     bar barely moved, so nothing about its level or its density said so; what a listener
	///     gets is a kick that flickers where the pulse should be.
	///
	/// AND A GROOVE'S IDENTITY IS PARTLY WHERE IT DOES NOT PLAY, which a rule about onsets cannot
	/// say on its own. The one drop IS the hole on beat 1, so <see cref="MusicGen.Add"/> may not
	/// put a kick on a beat either — same law read the other way round, and without it ska's beat-1
	/// occupancy climbed 41% → 45% as one-drop bars quietly acquired the downbeat they are defined
	/// by not having. On the beat is the groove; off the beat is the arrangement.
	///
	/// The cymbal has no spine here because the cymbal is not arranged at all: it is the pulse, and
	/// country's hat on the "and" — the largest mismatch the corpus pass found — is preserved by
	/// construction rather than by a rule that could be got wrong.
	/// </summary>
	public static bool[] SpineOf( Pattern p, bool kick, int barTicks )
	{
		if ( p == null ) return null;
		var spine = new bool[p.Count];
		for ( int i = 0; i < p.Count; i++ )
			spine[i] = kick ? IsPulse( p.TickAt( i ) ) : p.ValueAt( i ) != Ghost;
		return spine;
	}

	/// <summary>A tick the kick's spine lives on — see <see cref="SpineOf"/>. One law, read two
	/// ways: an onset here may not be dropped or moved, and an onset may not be ADDED here.
	/// </summary>
	public static bool IsPulse( int tick ) => tick % Timing.TicksPerBeat == 0;

	const int R = Harmony.Rest;
	static Pattern E( params int[] c ) => Pattern.Eighths( c );
	static Pattern S( params int[] c ) => Pattern.Sixteenths( c );

	// ── Ska-punk ──
	public static readonly DrumGroove[] SkaPunk =
	{
		new()
		{
			Name = "one drop",
			// The one drop: nothing on beat 1 at all. Kick and snare land together on beat 3, and
			// the space where the downbeat should be is the whole point of the feel.
			Kick = E( R, R, R, R, 0, R, R, R ),
			Snare = E( R, R, Ghost, R, 0, R, R, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, Open ),
			GhostRate = 0.8f, CrashOnOne = 0.25f,
		},
		new()
		{
			Name = "steppers",
			// Steppers: a kick on every beat — the four-to-the-floor of reggae, and what a ska
			// song reaches for when it wants to drive rather than lope.
			Kick = E( 0, R, 0, R, 0, R, 0, R ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, Open ),
		},
	};

	// ── Rock ──
	public static readonly DrumGroove[] Rock =
	{
		new()
		{
			Name = "backbeat",
			// Two bars, because a rock backbeat that is byte-identical every bar is a drum
			// machine. Each bar carries one pushed kick and it is a different one: bar 1 pushes the
			// "and of 1", bar 2 the "and of 2" into the "and of 3". Those pushes are the measured
			// shape — a rock kick spends far more of its bar on &1 and &3 than two anchor hits.
			Kick = E( 0, 0, R, R, 0, R, R, R,
			          0, R, R, 0, 0, 0, R, R ),
			Snare = E( R, R, 0, R, R, R, 0, R,
			           R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, Open ),
		},
		new()
		{
			Name = "driving eights",
			Kick = E( 0, R, R, 0, 0, R, R, R,
			          0, R, R, 0, 0, R, 0, R ),
			Snare = E( R, R, 0, R, R, R, 0, R,
			           R, R, 0, R, R, R, 0, Ghost ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 1.2f,
		},
	};

	// ── Country ──
	public static readonly DrumGroove[] Country =
	{
		new()
		{
			Name = "train beat",
			// The train beat: a constant running snare, ghosted everywhere except the backbeat,
			// which is the sound of country drumming and did not exist in this engine at all.
			Kick = E( 0, R, R, R, 0, R, R, R ),
			Snare = S( Ghost, Ghost, Ghost, Ghost, 0, Ghost, Ghost, Ghost,
			           Ghost, Ghost, Ghost, Ghost, 0, Ghost, Ghost, Ghost ),
			// The hat is on the "and". Two bars to hold the measured split without a per-hit roll:
			// 3 of 8 beats carry the hat against 7 of 8 offbeats, which is the 36/84 the dataset
			// reads. On the pulse it was the one thing in the kit contradicting the genre's own
			// accent weight — country leans on the offbeat and had nothing there to lean on.
			Cymbal = E( 0, 0, R, 0, R, 0, R, 0,
			            R, 0, 0, 0, 0, 0, R, R ),
			GhostRate = 0.6f, CrashOnOne = 0.2f,
		},
		new()
		{
			Name = "two beat",
			// The other country feel: a two-beat "boom-chick" where the kit gets out of the way
			// of the bass and the guitar entirely.
			Kick = E( 0, R, R, R, 0, R, R, R ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
			// Lighter than the train beat's hat and still offbeat-led: the "chick" of boom-chick is
			// the & whichever country feel is playing, and this one just plays fewer of them.
			Cymbal = E( 0, 0, R, R, 0, 0, R, Open ),
			GhostRate = 0.5f, CrashOnOne = 0.15f,
		},
	};

	// ── Metal ──
	public static readonly DrumGroove[] Metal =
	{
		new()
		{
			Name = "double kick",
			// DOUBLE KICK IS A BURST, NOT A SETTING. One bar of unbroken sixteenths looped for
			// three minutes is ~13 hits a second with nothing ever changing, which is why it read
			// as a blast beat at every tempo and every subdivision: the tell is not the rate, it
			// is that the rate never moves. A drummer rides an ordinary kick pattern and stands on
			// the double pedal under a riff — for a beat into a bar line, for a bar at the top of
			// a phrase — and the contrast is the whole effect.
			//
			// Four bars, because Pattern carries its own length: bars 1 and 3 are a played metal
			// kick, bar 2 bursts over its last beat, and bar 4 is the full double-kick bar the
			// phrase turns around on. Same one object, no new mechanism.
			Kick = S( 0, R, R, R, 0, R, R, 0, 0, R, R, R, 0, R, 0, R,
			          0, R, R, R, 0, R, R, 0, 0, R, R, R, 0, 0, 0, 0,
			          0, R, R, R, 0, R, R, 0, 0, R, R, R, 0, R, 0, R,
			          0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			// The kick no longer fills every sixteenth, so the busy layer has somewhere to sit
			// again — but only just: metal's kit is a wall by design and the ghosts are the mortar.
			GhostRate = 0.2f, CrashOnOne = 0.55f,
		},
		new()
		{
			Name = "thrash",
			// Kick on every eighth under a snare that answers it — faster to read than the
			// double-kick wall, and it leaves the snare somewhere to go.
			Kick = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			Snare = E( R, R, 0, R, R, R, 0, 0 ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0.3f, CrashOnOne = 0.5f,
		},
		new()
		{
			Name = "two-step",
			// The same beat punk gets, and deliberately the same: bum-tis-bumbum-tis is played in
			// both genres and inventing a heavier variant for metal would be answering a question
			// nobody asked. What separates the two here is everything around it — tempo, kit, the
			// ghost rate below, and the riff on top.
			//
			//         1   e   &   a   2   e   &   a
			//   kick  K   .   .   .   K   K   .   .
			//   snare .   .   S   .   .   .   S   .
			//
			// A separate OBJECT because no two genres may share a groove (the suite asserts it),
			// which is a rule about tables accidentally converging rather than about two genres
			// never playing the same rhythm.
			//
			// WEIGHTED BEHIND EACH GENRE'S OWN GROOVE, on purpose. This one was added because a
			// listener said it was missing, and the check on that kind of change is not whether the
			// reason was good — it was, the beat really was absent from both tables — but whether
			// the WEIGHT came from the same evidence. It did not: joint-top billing was a choice,
			// and it pushed "eighth drive", which this file calls the punk engine, from 60% of punk
			// songs to 37%. A genre's signature stays its most common groove and a new arrival
			// earns its share; ~29% is present without displacing anything.
			Kick = S( 0, R, R, R, 0, 0, R, R,
			          0, R, R, R, 0, 0, R, R ),
			Snare = S( R, R, 0, R, R, R, 0, R,
			           R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0.25f, CrashOnOne = 0.5f,
		},
	};

	// ── Punk ──
	public static readonly DrumGroove[] Punk =
	{
		new()
		{
			Name = "eighth drive",
			// The punk engine: eighth-note ride/snare drive at speed. It is not a backbeat with
			// the tempo turned up — the cymbal hand never stops and the kick pushes every beat.
			//
			// The snare hand does not stop either, which is what the measurement says and what the
			// two-hits-a-bar backbeat could not be: 2 and 4 are struck and every eighth between
			// them is ghosted. The energy gate on ghost cells thins it back toward the bare
			// backbeat in a quiet section, so the density is the section's rather than the table's.
			Kick = E( 0, R, 0, R, 0, R, 0, R ),
			Snare = E( Ghost, Ghost, 0, Ghost, Ghost, Ghost, 0, Ghost ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0.4f, CrashOnOne = 0.45f,
		},
		new()
		{
			Name = "d-beat",
			// Early punk, and it stays as it is. Notations of the d-beat vary in where the kick's
			// offbeats sit and this is a legitimate one; it is also NOT the sixteenth gallop the
			// "two-step" entry below carries, which is the beat this table was actually missing.
			Kick = E( 0, R, R, 0, R, 0, R, R,
			          0, R, R, 0, R, 0, R, R ),
			Snare = E( R, R, 0, R, R, R, 0, R,
			           R, R, 0, R, R, R, 0, Ghost ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0.5f, CrashOnOne = 0.5f,
		},
		new()
		{
			Name = "two-step",
			// THE PUNK ENGINE'S OTHER GEAR: kick on the beat, snare on the "&", and the kick
			// doubled at the SIXTEENTH going into every other beat —
			//
			//         1   e   &   a   2   e   &   a
			//   kick  K   .   .   .   K   K   .   .
			//   snare .   .   S   .   .   .   S   .
			//
			// — repeating twice a bar. Drummers call it the two-step or the skank beat; it is not
			// the d-beat (that keeps its snare on 2 and 4, and has its own entry above).
			//
			// IT IS A 2-BEAT CELL AND THAT IS THE WHOLE POINT. The same figure written over four
			// beats — kick, snare, doubled kick on 3, snare — is the identical pattern counted at
			// half the rate, and at this genre's tempo that puts the double every 1.4 s instead of
			// every 0.7 s. It reads as an ordinary rock beat rather than as punk. This engine has
			// been caught by exactly that ambiguity before (see the ska tempo block in
			// GenreProfile): a rhythm means nothing until you say which pulse it is counted against.
			Kick = S( 0, R, R, R, 0, 0, R, R,
			          0, R, R, R, 0, 0, R, R ),
			Snare = S( R, R, 0, R, R, R, 0, R,
			           R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0.35f, CrashOnOne = 0.4f,
		},
	};

	// ── Pop ──
	public static readonly DrumGroove[] Pop =
	{
		new()
		{
			Name = "four on the floor",
			Kick = E( 0, R, 0, R, 0, R, 0, R ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, Open, 0, Open, 0, Open, 0, Open ),
			GhostRate = 0.5f, CrashOnOne = 0.3f,
		},
		new()
		{
			Name = "half-time backbeat",
			// The other modern pop feel: the backbeat falls on 3 alone, which halves the pulse
			// without touching the tempo.
			Kick = E( 0, R, R, 0, R, R, 0, R ),
			Snare = E( R, R, R, R, 0, R, R, R ),
			Cymbal = S( 0, R, 0, 0, 0, R, 0, 0, 0, R, 0, 0, 0, R, 0, Open ),
			GhostRate = 0.7f, CrashOnOne = 0.35f,
		},
	};
}

// ── What a fill is made of ──
// Every fill this engine ever played was the same object: `per` evenly-spaced, equally-loud
// hits in EVERY beat of the span, with a floor of four. A two-bar fill was 32 hits that never
// stopped and never moved, which is the blast-beat read — nothing about it was fast, it simply
// occupied every subdivision it could reach. Three things were missing and they are separable.
//
// DENSITY, and it is measured. A real rock fill averages 13.2 hits per bar across the whole
// kit (204 bars of fill performance in the Groove MIDI Dataset — see DrumGroove's header for
// the source and the method), against a FLOOR here of 16 and a 32nd branch of 32. The quietest
// fill this engine could play was already busier than the average real one. The same histogram
// says what the shape of a bar is: the "e" and the "a" carry about 0.62 of the occupancy the
// beats and the "&"s do, so a fill is EIGHTHS WITH SIXTEENTH ORNAMENT, not a sixteenth grid.
// Density is per genre now, like everything else about the kit (GenreProfile.FillHits).
//
// SHAPE, which is what `per` structurally could not express, being one number applied to every
// beat. See FillShape.
//
// DYNAMICS. RenderFill called the kit voices directly and so was the one part of the engine
// with no accent pattern and no energy scaling — a straight exception to the rule that every
// voice routes its level through NoteGain. An even stream of equally-loud hits reads as a wall
// however few of them there are, which is why this is not just a density fix.

/// <summary>The SHAPE of a fill — where its hits sit inside the span. This is the half of a
/// fill that a density number cannot say, and the reason there was only ever one fill.</summary>
enum FillShape
{
	/// <summary>Sparse at the start and filling up into the downbeat it lands on — a bar that
	/// empties itself and then accelerates out of the hole. The commonest fill there is.</summary>
	Ramp,
	/// <summary>Even across the whole span: the roll. What every fill used to be, kept because
	/// it is a real shape and metal is mostly made of it.</summary>
	Rolling,
	/// <summary>Space, then a flurry over the last beat. THIS IS WHERE THE THIRTY-SECOND LIVES
	/// — a pickup is short enough to be played and short enough to stay a gesture, which is
	/// exactly what a bar of unbroken 32nds is not.</summary>
	Pickup,
	/// <summary>Two or three hits with air around them: the tom figure, the single flam, the
	/// bar that does almost nothing. A fill is allowed to be a gesture.</summary>
	Gesture,
}


// The kit's patterns: which drum lands where. The per-song groove, the section's energy, and
// the phrase-end fill.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Drums ──
	// Render one bar of kit off the song's groove. `fillTick` is where a fill takes over (the
	// bar's end tick if there is none), so the groove simply stops there and the fill owns the
	// rest — which is what lets a fill be anything from one beat to two bars.
	void RenderDrumBar( int barTick, int barTicks, int fillTick, Rng noise )
	{
		// Knob ceiling was too frantic: scale so DRUM BUSY 100% reads as the old 75%.
		float busy = Math.Clamp( _c.DrumBusy, 0f, 1f ) * 0.75f * _groove.GhostRate;
		int to = Math.Min( barTick + barTicks, fillTick );
		if ( to <= barTick ) return;

		// The cymbal hand. Which instrument it is was decided per section (_ride); the groove
		// only says where the hits land and which are "open". A thin section HALVES the cymbal
		// pattern rather than playing it quieter — that is what makes a verse read as a verse and
		// a breakdown as a breakdown.
		//
		// Half the ONSETS, not "everything off the beat". Those were the same rule while every
		// groove's cymbal sat on the pulse, and they stop being the same the moment one does not:
		// country's hat is on the "and", so dropping the offbeats there does not thin the kit, it
		// deletes the hi-hat from every verse in the genre. Alternate onsets thin any pattern by
		// half wherever it sits, and for a plain eighth-note cymbal it is exactly what the old rule
		// did.
		// A CRASH ON THE SECTION'S FIRST DOWNBEAT. Every groove has carried a CrashOnOne since the
		// tables were written and nothing ever read it, so no crash landed on any downbeat in the
		// engine — only at the end of a fill and the end of a song. It is one draw, on the one bar
		// it can apply to, so it costs the same from every section's stream.
		// NOT ON THE SONG'S OWN FIRST BAR. A crash PUNCTUATES a transition — it is the drummer
		// marking the seam between one section and the next, and every other place this fires has
		// one. Bar 1 of the intro has nothing behind it to mark, so what lands there is three
		// seconds of cymbal wash over the sparsest section in the song, belonging to no groove and
		// answering nothing that was played. It reads as a cymbal that was already ringing when the
		// song started, which is exactly what it is.
		//
		// The roll still happens, so a genre's draw count does not depend on which bar it is on.
		if ( barTick == _sectionTick && noise.Chance( _groove.CrashOnOne ) && barTick > 0 )
			RenderCrashCym( _time.TickToSample( barTick ),
				_c.CrashVol * KitGain( barTick, 1f, 0.5f ), _crashBright, dark: false );

		bool sparse = _energy < 0.4f;
		// THINNED FIRST, THEN PLAYED, and the slice runs ONE BEAT PAST the bar. An open hat is
		// choked by the next hit the drummer actually plays: half the onsets means half the
		// chokes too, so the thinning cannot happen inside the playing loop — and the hit that
		// chokes an "and of 4" is in the next bar, which is what the lookahead is for. Only the
		// hits inside the bar are played; the rest are read.
		var cym = _groove.Cymbal.Slice( barTick, to + Timing.TicksPerBeat, _sectionTick, _feel );
		var play = new List<Hit>();
		for ( int i = 0, kept = 0; i < cym.Count; i++ )
			if ( !(sparse && (kept++ & 1) == 1) ) play.Add( cym[i] );

		for ( int i = 0; i < play.Count; i++ )
		{
			var h = play[i];
			if ( h.Tick >= to ) break;
			Trace?.Add( TraceVoice.Cymbal, h.Tick );
			int at = _time.TickToSample( h.Tick );
			int next = i + 1 < play.Count ? _time.TickToSample( play[i + 1].Tick ) : int.MaxValue;
			int cell = h.Value;
			// The genre's own accent weight decides how a hat off the beat sits against one on it.
			// A flat 0.75 was a house rule where the measurement is per genre and disagrees in both
			// directions — country and ska lean ON the offbeat, pop's programmed kit buries it.
			float kit = KitGain( h.Tick, h.Vel, 0.55f );
			float amp = _c.HatVol * kit;
			if ( _crashRide )
				// The technique rather than a second cymbal: the hand moves onto a crash and rides
				// it, so the open cell is the accent it leans on rather than an open hat.
				// Crash-riding is a LIFT — the hand moves onto the loudest thing in the kit and the
				// whole section rises. Measured, it was landing within 0.2 dB of an ordinary ride,
				// which is the technique costing a cymbal and buying nothing.
				RenderCrashCym( at, _c.CrashVol * kit * (cell == DrumGroove.Open ? 0.67f : 0.44f),
					_crashDark, dark: true, next, CymbalBands.RestrikeTau );
			else if ( _ride )
				// The bell is a CELL now. It used to be positional — every quarter note was a
				// bell, whatever the groove said — while the open cell a riding section was handed
				// fell through to a hi-hat, so the one thing the pattern said about the cymbal
				// hand was the one thing the ride ignored.
				// EVERY STROKE DAMPS THE ONE BEFORE IT (see RenderCymbal's chokeFloor). A cymbal
				// struck eight times a bar is not eight cymbals summed: the stick is on the metal.
				RenderRideCym( at, amp * RideStroke( h.Tick - barTick ),
					cell == DrumGroove.Open ? _rideBell : _rideBow, next, CymbalBands.RestrikeTau );
			else
				RenderHat( at, HatOpenness( cell ), amp, noise, HatFor( cell ),
					Rings( cell ) ? next : int.MaxValue );

			// Busy fills the gaps with quieter sixteenth chatter.
			if ( cell == 0 && !sparse && noise.Chance( busy ) )
			{
				int sixAt = _time.TickToSample( h.Tick + Timing.TicksPerEighth / 2 );
				if ( _crashRide ) RenderCrashCym( sixAt, _c.CrashVol * kit * 0.22f, _crashDark, true );
				else if ( _ride ) RenderRideCym( sixAt, amp * 0.4f * RideStroke( h.Tick + Timing.TicksPerEighth / 2 - barTick ), _rideBow );
				else RenderHat( sixAt, 0f, amp * 0.4f, noise, _hatTone );
			}
		}

		// The pedal's own part, under a section whose hands are on the ride (see FootOccupancy).
		// The pattern was drawn once for the section; a foot keeps a figure the way a hand does.
		if ( (_ride || _crashRide) && _footCells != 0 )
			for ( int i = 0; i < 8; i++ )
			{
				if ( (_footCells & (1 << i)) == 0 ) continue;
				int t = barTick + i * Timing.TicksPerEighth;
				if ( t >= to ) break;
				RenderHat( _time.TickToSample( t ), 0f, _c.HatVol * KitGain( t, 0.7f, 0.45f ),
					noise, _footTone );
			}

		// THE KICK READS ITS VELOCITY. It used to discard the cell's Vel and take no metric accent
		// and no energy scaling at all, so metal's sixteenth double-kick was the identical
		// waveform at the identical level sixteen times a bar — which is what machine-gunning is.
		// Its depth is under the cymbal hand's and near the fill's: the kick is the floor of the
		// groove and should breathe least.
		var kicks = _kickFig.Slice( barTick, to, _sectionTick, _feel );
		Trace?.Add( TraceVoice.Kick, kicks );
		foreach ( var h in kicks )
			RenderKick( _time.TickToSample( h.Tick ), noise, KitGain( h.Tick, h.Vel, 0.30f ),
				_kickTone, 0f );

		// The kick's own humanising. A groove pattern is exact, and a drummer is not: KICK SYNC is
		// the chance of a stray extra kick pushing into the following beat, rolled per bar so the
		// groove breathes instead of stamping the identical bar out for eight bars running.
		if ( _c.KickSyncChance > 0f )
			for ( int t = barTick + Timing.TicksPerEighth; t < to; t += Timing.TicksPerBeat )
				if ( noise.Chance( _c.KickSyncChance * (0.4f + busy) * _energy ) )
					RenderKick( _time.TickToSample( t ), noise, KitGain( t, 0.85f, 0.30f ),
						_kickTone, 0f );

		foreach ( var h in _snareFig.Slice( barTick, to, _sectionTick, _feel ) )
		{
			bool ghost = h.Value == DrumGroove.Ghost;
			// The groove's own ghost notes thin out with the section rather than hammering a
			// verse as hard as a chorus.
			if ( ghost && !noise.Chance( 0.35f + 0.65f * _energy ) ) continue;
			Trace?.Add( TraceVoice.Snare, h.Tick, !ghost );
			RenderSnare( _time.TickToSample( h.Tick ), noise, ghost );
		}

		// Extra ghosts / toms between the groove's own hits: the "busy" layer. A groove that
		// already fills its own gaps scales this down through GhostRate rather than being
		// special-cased here.
		if ( busy > 0f && !sparse )
			for ( int t = barTick; t < to; t += Timing.TicksPerEighth / 2 )
			{
				if ( !noise.Chance( _c.GhostSnareChance * busy * 0.5f ) ) continue;
				int at = _time.TickToSample( t );
				// The busy layer's toms are the two RACK toms answering each other — the drums a
				// hand can reach without leaving the groove. Which two they are is the kit's, not
				// a pair of frequencies picked here (see TomKit).
				if ( noise.Chance( (1f - _drumTone) * 0.5f ) )
					RenderTom( at, _tomKit, (t / (Timing.TicksPerEighth / 2)) & 1, noise,
						KitGain( t, 0.7f, 0.4f ), TomTone.Default );
				else RenderSnare( at, noise, true );
			}
	}

	/// <summary>
	/// THE FOOT'S OWN PART: how often the pedal closes the hi-hat, per eighth of the bar, while
	/// the hands are on the ride. A riding section used to silence the hi-hat completely, and the
	/// hat is the one voice in the kit that does not need a hand.
	///
	/// MEASURED, off the same source as the grooves and the accent weights (Google Magenta's
	/// Groove MIDI Dataset — see DrumGroove's header). Method: over the 472 4/4 performances,
	/// split by whether the ride carries the pulse (a ride hit at least every four beats), count
	/// note-ons of the PEDAL hi-hat (GM 44) and fold their positions onto one bar at the eighth.
	/// Two things came out of it and only one was expected:
	///
	///   * the foot IS busier when the hands are away — 2.74 pedal hits per bar over 12220 riding
	///     bars, against 1.92 over 8519 bars where the hands are on the hat;
	///   * but it is NOT "2 and 4". Those are the two peaks (16.2% and 17.9% of hits) and the
	///     downbeat is a third (13.1%), while every remaining eighth still carries 9.6–11.6%.
	///     Two chicks a bar on the backbeat is a third of what a drummer's foot actually does,
	///     and it is the part that is easiest to assume you already know.
	///
	/// These are the per-eighth probabilities that reproduce both numbers: each share of the hits
	/// times the 2.74 they are shared out of. The pattern is drawn ONCE PER SECTION from them
	/// rather than rolled per bar, because a drummer's foot keeps a figure the same way a hand
	/// does — the marginals are what a performance averages to, not what it decides every bar.
	/// </summary>
	static readonly float[] FootOccupancy =
		{ 0.36f, 0.26f, 0.44f, 0.32f, 0.31f, 0.28f, 0.49f, 0.28f };

	/// <summary>How hard a ride stroke is, by where it lands. A drummer's "and" is a much lighter
	/// stroke than the beat; the genre's accent weight alone (rock's offbeat is 1.7 dB down) leaves
	/// eight near-equal strokes a bar, and eight near-equal strokes is a WALL however good each one
	/// sounds. Pulling the offbeats back was the single most effective change in the whole cymbal
	/// exercise — more than any edit to the voice itself. Suspect the pattern before the timbre.
	/// </summary>
	static float RideStroke( int tickInBar )
		=> tickInBar % Timing.TicksPerBeat == 0 ? 1f : 0.5f;

	// ── The cymbal hand's cells ──
	// What a cymbal cell means, once it can mean more than "open or not". Openness is a distance
	// and the two foot articulations are not distances at all, which is why the tone and the
	// position are two lookups rather than one.

	static float HatOpenness( int cell ) => cell switch
	{
		DrumGroove.Open => 1f,
		DrumGroove.Half => 0.5f,
		DrumGroove.Splash => 1f,
		_ => 0f,
	};

	HatTone HatFor( int cell ) => cell switch
	{
		DrumGroove.Foot => _footTone,
		DrumGroove.Splash => HatTone.Splash,
		_ => _hatTone,
	};

	/// <summary>Whether this cell leaves the hat RINGING — i.e. whether there is anything for the
	/// next hit's foot to choke. A chick and a splash have already closed the cymbals.</summary>
	static bool Rings( int cell ) => cell == DrumGroove.Open || cell == DrumGroove.Half;

	// ── Fills ──
	// A fill is a span, not "the last beat of the bar". Length is a weighted draw — a beat most
	// of the time, occasionally a whole bar or two — and the long ones are GATED to the
	// boundaries that earn them (into a final chorus, out of a breakdown), because a two-bar
	// fill at every phrase end is not a fill, it is the arrangement.
	//
	// Returns the tick the fill starts at, so the groove above knows where to stop.
	int FillStart( int barTick, int barTicks, bool bigBoundary, Rng rng )
	{
		float r = rng.Next();
		int span;
		if ( r < 0.55f ) span = Timing.TicksPerBeat;                       // one beat
		else if ( r < 0.80f || !bigBoundary ) span = Timing.TicksPerBeat * 2; // two beats (from 3)
		else if ( r < 0.95f ) span = barTicks;                             // a whole bar
		else span = barTicks * 2;                                          // two bars

		// The fill ends on the bar line it is leading into, so a longer one simply starts
		// earlier — two beats start on beat 3, two bars start in the bar before.
		return Math.Max( barTick - barTicks, barTick + barTicks - span );
	}

	static readonly FillShape[] FillShapeTable =
		{ FillShape.Ramp, FillShape.Rolling, FillShape.Pickup, FillShape.Gesture };

	// Occupancy per grid cell within one beat, relative to the beat itself — the measured shape a
	// bar of fill has. The flurry's grid is the same idea at 32nds, with the eighths inside it
	// still carrying the weight, so an acceleration still lands on the beats it passes.
	static readonly float[] FillStraight = { 1f, 0.62f, 1f, 0.62f };
	static readonly float[] FillTriplet = { 1f, 0.62f, 0.62f };
	static readonly float[] FillFlurry = { 1f, 0.5f, 0.62f, 0.5f, 1f, 0.5f, 0.62f, 0.5f };

	/// <summary>How many cells a fill rolls for per beat, whatever grid it is actually on — the
	/// triplet roll, the flurry and the straight sixteenths all pull the same number of values.
	/// A knob (TRIPLET here) must never decide how much of the stream a fill spends.</summary>
	const int FillCells = 8;

	/// <summary>The scale factor that turns a grid's position weights into per-cell probabilities
	/// summing to <paramref name="hitsPerBar"/>.</summary>
	static float FillCellK( float hitsPerBar, float[] grid )
	{
		float perBar = 0f;
		foreach ( var w in grid ) perBar += w;
		return hitsPerBar / (perBar * 4f);
	}

	/// <summary>The per-cell probabilities a density target turns into on a grid.
	///
	/// A WATER-FILL RATHER THAN ONE SCALE FACTOR, and that is the difference between a fill getting
	/// denser and a fill getting louder. The beats reach certainty long before a high target does,
	/// so a flat scale silently drops everything past that point — metal asked for 14 hits a bar
	/// and would have played 13.4 whatever number it wrote down. What a busier drummer actually
	/// adds is ORNAMENT, the "e" and the "a", so the excess goes there. The grid's real ceiling is
	/// four hits a beat, and no genre is near it.</summary>
	static float[] FillChances( float hitsPerBar, float[] grid )
	{
		var p = new float[grid.Length];
		float want = hitsPerBar / 4f;                          // per beat
		float k = FillCellK( hitsPerBar, grid );
		for ( int pass = 0; pass < 6; pass++ )
		{
			float got = 0f, room = 0f;
			for ( int i = 0; i < p.Length; i++ ) { p[i] = Math.Clamp( k * grid[i], 0f, 1f ); got += p[i]; }
			for ( int i = 0; i < p.Length; i++ ) if ( p[i] < 1f ) room += grid[i];
			if ( want - got < 1e-4f || room <= 0f ) break;
			k += (want - got) / room;
		}
		return p;
	}

	/// <summary>What a density target of <paramref name="hitsPerBar"/> actually plays on the
	/// straight grid. The engine suite asserts each genre's target against this: a target the model
	/// cannot reach is a number that quietly buys nothing.</summary>
	internal static float FillDensityOnGrid( float hitsPerBar )
	{
		float hits = 0f;
		foreach ( var p in FillChances( hitsPerBar, FillStraight ) ) hits += p;
		return hits * 4f;
	}

	/// <summary>How dense the fill is at this point in its span, as a multiplier on the genre's
	/// target. <paramref name="u"/> is 0 on the fill's first beat and 1 on its last.</summary>
	static float ShapeDensity( FillShape shape, float u, bool last ) => shape switch
	{
		FillShape.Ramp => 0.45f + 1.1f * u,
		FillShape.Pickup => last ? 1f : 0.22f,
		FillShape.Gesture => 0.15f + 0.35f * u,
		_ => 1f,
	};

	/// <summary>
	/// What the rest of the section is doing where this fill cell lands — the one thing a fill did
	/// not read, on a grid that was already built for it.
	///
	/// A fill is the drummer's bar, but it is not played over silence: the melodic voices play
	/// THROUGH a fill (only the kit hands over), so a fill that puts a hit on the note the tune is
	/// landing on is two things arriving on the same beat. And the seam is what the fill is FOR —
	/// it is crossing one, and leaning on it is the gesture.
	///
	/// A multiplier on a probability, so it changes nothing about how much of the stream a fill
	/// spends: <see cref="FillCells"/>'s rule holds, and the density target still means what it
	/// said. Deliberately gentle in both directions — a fill that dodged the tune outright would
	/// be a fill written by the melody.
	/// </summary>
	float FillAgainst( int tick )
	{
		var sk = _skeleton;
		if ( sk == null ) return 1f;
		int c = sk.CellAt( tick );
		if ( c < 0 ) return 1f;
		return (sk.TuneOn[c] ? 0.6f : 1f) * (sk.Seam[c] ? 1.25f : 1f);
	}

	// One fill across a span. The span is whatever FillStart drew, so the same code plays a
	// one-beat pickup and a two-bar blow-out; the terminal crash lands on the downbeat it is
	// leading into.
	void RenderFill( int fromTick, int toTick, Rng noise, Rng rng )
	{
		int span = toTick - fromTick;
		if ( span <= 0 ) return;
		int beats = Math.Max( 1, span / Timing.TicksPerBeat );

		var shape = rng.PickWeighted( FillShapeTable, _prof.FillShapes );
		// A SHUFFLE IS ALREADY A TRIPLET FEEL, so a fill on the straight grid under one is not
		// straight — it is neither. Ticks are metrical and the shuffle is a warp applied on the way
		// to samples (see Timing), which interpolates between eighth ANCHORS: the four sixteenths of
		// a beat come out 2:2:1:1, so the back half of every beat runs at double the speed of the
		// front. At 115 bpm with swing 0.33 they land at 0/173/347/434 ms. That is right for a comp
		// landing an occasional sixteenth between two eighths the band shares, and wrong for the one
		// voice that runs continuous sixteenths — a drummer shuffling fills in triplets.
		//
		// The Chance draw still happens either way, so the genre's stream position is untouched: the
		// feel decides the GRID, never how much of the stream a fill spends (see FillCells).
		bool triplet = rng.Chance( _c.TripletChance ) || _time.Swing >= GenreProfile.ShuffleGrid;
		bool tomLed = rng.Chance( 0.45f );

		// A fill longer than a bar still has to keep the time while it happens. A gesture or a
		// pickup stretched over two bars is not a sparser fill, it is a hole in the arrangement —
		// the kit has already handed over to it, so there is nothing else playing the beat.
		if ( beats > 4 && shape != FillShape.Rolling ) shape = FillShape.Ramp;

		// And the same rule from the other end: a PICKUP is a wait and then a flurry, so it needs a
		// span to wait in. Over one beat there is nothing to wait through and the shape degenerates
		// into the flurry alone — five or six 32nds in the beat the kit has just handed over to,
		// with no groove either side of them. That is the most common fill length there is (a beat
		// is 55% of the draw), so a genre with any weight on Pickup plays it constantly, and it
		// reads as a drummer arriving late and cramming the whole fill in anyway. A fill that short
		// accelerates into the bar line instead.
		if ( beats == 1 && shape == FillShape.Pickup ) shape = FillShape.Ramp;

		float[] grid = triplet ? FillTriplet : FillStraight;
		// The genre's hits-per-bar turned into per-cell probabilities: the position weights are the
		// SHAPE of a bar's occupancy and this fills them until they sum to the target. The flurry
		// keeps the flat scale instead, because being denser than an ordinary beat is what a flurry
		// IS — water-filling it to the same target would take the acceleration back out of it.
		float[] cells = FillChances( _prof.FillHits, grid );
		float flurryK = FillCellK( _prof.FillHits, grid );

		for ( int b = 0; b < beats; b++ )
		{
			int beatTick = fromTick + b * Timing.TicksPerBeat;
			bool last = b == beats - 1;
			float dens = ShapeDensity( shape, beats == 1 ? 1f : b / (beats - 1f), last );
			// The flurry is a straight-grid gesture: a triplet fill is already a different feel
			// and does not need a second one laid over its last beat.
			bool flurry = shape == FillShape.Pickup && last && !triplet;
			int n = flurry ? FillFlurry.Length : cells.Length;

			for ( int i = 0; i < FillCells; i++ )
			{
				// Both draws happen for every cell of every grid — see FillCells.
				float r = rng.Next(), d = rng.Next();
				if ( i >= n ) continue;
				float p = flurry ? flurryK * FillFlurry[i] : cells[i];
				int cellTick = beatTick + i * Timing.TicksPerBeat / n;
				if ( r >= Math.Clamp( p * dens * FillAgainst( cellTick ), 0f, 1f ) ) continue;

				// A tuplet divides its own span evenly; a straight cell is a grid position and
				// shuffles with everything else the band lands on (see Timing).
				int t = triplet
					? _time.EvenSpan( beatTick, Timing.TicksPerBeat, i / (double)n )
					: _time.TickToSample( cellTick );

				// A fill is a phrase: it leans into the bar line it is landing on, and it reads
				// the genre's accent weights like every other voice in the song.
				float u = (b + i / (float)n) / beats;
				float gain = KitGain( cellTick, 0.72f + 0.33f * u, 0.35f );

				// A fill goes round the kit high to low, which is what a three-piece tom set is
				// laid out for — and it goes there BY INDEX, so a fill cannot reach past the
				// bottom of the kit. It used to sweep six frequencies through a pan map that
				// bottomed out at 145 Hz, so the two lowest drums of every fill shared a position.
				// Snare-led unless the fill is a tom figure; the ride comes in where DrumTone leans
				// high, and the kick is under it either way — "13.2 hits per bar" is the whole kit,
				// and a drummer's foot is part of the kit.
				float tomShare = shape == FillShape.Gesture || tomLed ? 0.55f : 0.26f;
				float rideShare = 0.14f * _drumTone;
				if ( d < tomShare )
					RenderTom( t, _tomKit, Math.Min( TomKit.Count - 1, (int)(u * TomKit.Count) ),
						noise, gain, TomTone.Default );
				else if ( d < tomShare + rideShare )
					RenderRideCym( t, _c.HatVol * gain, _rideBow );
				else if ( d < tomShare + rideShare + 0.08f )
					RenderKick( t, noise, gain, _kickTone, 0f );
				else RenderSnare( t, noise, false, gain );
			}
		}
		// The fill lands on the downbeat it was leading into, and it is a crash at a LEVEL now:
		// the voice had no gain parameter at all, so the loudest thing in a song arrived at full
		// scale whatever the section's energy, the velocity or the genre's accent said.
		bool darkCrash = rng.Chance( 0.4f );
		RenderCrashCym( _time.TickToSample( toTick ), _c.CrashVol * KitGain( toTick, 1f, 0.35f ),
			darkCrash ? _crashDark : _crashBright, darkCrash );
	}
}
