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
/// WHERE THE HITS FALL IS MEASURED, the same way the accent weights in
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
	public const int Open = 1;

	public string Name { get; init; }
	public Pattern Kick { get; init; }
	public Pattern Snare { get; init; }
	public Pattern Cymbal { get; init; }

	/// <summary>Extra ghost-note propensity on top of what the pattern names — the "busy" layer's
	/// scaling factor for this groove.</summary>
	public float GhostRate { get; init; } = 1f;

	/// <summary>Chance of a crash on the section's first downbeat.</summary>
	public float CrashOnOne { get; init; } = 0.35f;

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
			Kick = E( 0, R, R, 0, R, 0, R, R,
			          0, R, R, 0, R, 0, R, R ),
			Snare = E( R, R, 0, R, R, R, 0, R,
			           R, R, 0, R, R, R, 0, Ghost ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0.5f, CrashOnOne = 0.5f,
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
		bool sparse = _energy < 0.4f;
		int cymIdx = 0;
		foreach ( var h in _groove.Cymbal.Slice( barTick, to, _sectionTick, _feel ) )
		{
			bool onBeat = (h.Tick - barTick) % Timing.TicksPerBeat == 0;
			if ( sparse && (cymIdx++ & 1) == 1 ) continue;
			int at = _time.TickToSample( h.Tick );
			bool open = h.Value == DrumGroove.Open;
			// The genre's own accent weight decides how a hat off the beat sits against one on it.
			// A flat 0.75 was a house rule where the measurement is per genre and disagrees in both
			// directions — country and ska lean ON the offbeat, pop's programmed kit buries it.
			float amp = _c.HatVol * h.Vel * MetricGain( h.Tick ) * EnergyGain( 0.55f );
			if ( _ride && !open ) RenderRide( at, onBeat, amp, noise );
			else RenderHat( at, open, amp, noise );

			// Busy fills the gaps with quieter sixteenth chatter.
			if ( !open && !sparse && noise.Chance( busy ) )
			{
				int sixAt = _time.TickToSample( h.Tick + Timing.TicksPerEighth / 2 );
				if ( _ride ) RenderRide( sixAt, false, amp * 0.4f, noise );
				else RenderHat( sixAt, false, amp * 0.4f, noise );
			}
		}

		foreach ( var h in _groove.Kick.Slice( barTick, to, _sectionTick, _feel ) )
			RenderKick( _time.TickToSample( h.Tick ), noise );

		// The kick's own humanising. A groove pattern is exact, and a drummer is not: KICK SYNC is
		// the chance of a stray extra kick pushing into the following beat, rolled per bar so the
		// groove breathes instead of stamping the identical bar out for eight bars running.
		if ( _c.KickSyncChance > 0f )
			for ( int t = barTick + Timing.TicksPerEighth; t < to; t += Timing.TicksPerBeat )
				if ( noise.Chance( _c.KickSyncChance * (0.4f + busy) * _energy ) )
					RenderKick( _time.TickToSample( t ), noise );

		foreach ( var h in _groove.Snare.Slice( barTick, to, _sectionTick, _feel ) )
		{
			bool ghost = h.Value == DrumGroove.Ghost;
			// The groove's own ghost notes thin out with the section rather than hammering a
			// verse as hard as a chorus.
			if ( ghost && !noise.Chance( 0.35f + 0.65f * _energy ) ) continue;
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
				if ( noise.Chance( (1f - _drumTone) * 0.5f ) ) RenderTom( at, 150f + 40f * ((t / 24) & 1), noise );
				else RenderSnare( at, noise, true );
			}
	}

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

	// Roll across a span: tom/snare/cymbal hits, subdivided straight (16ths) or as triplets. The
	// span is whatever FillStart drew, so the same code plays a one-beat pickup and a two-bar
	// blow-out. The terminal crash lands on the downbeat the fill is leading into.
	void RenderFill( int fromTick, int toTick, Rng noise, Rng rng )
	{
		int span = toTick - fromTick;
		if ( span <= 0 ) return;
		// One subdivision per beat, so a longer fill is more notes rather than slower ones.
		//
		// A fill is where the THIRTY-SECOND lives, in every genre. A roll is ornament vocabulary
		// everyone owns — a buzz roll, a drag into the downbeat, the flurry that hands over to the
		// chorus — and it is not a metal thing or a fast thing: a drummer plays a fill finer than
		// the groove precisely because a fill is a moment rather than a bar. The short fills are
		// the ones that take it: two beats of unbroken 32nds is a different gesture (and a longer
		// fill is already more notes, by the line above).
		int beats = Math.Max( 1, span / Timing.TicksPerBeat );
		// Both branches take two draws, and the roll comes before the length test — a fill's
		// subdivision must not change how many values the fill stream pulls.
		int per = rng.Chance( _c.TripletChance ) ? (rng.Chance( 0.5f ) ? 3 : 6)
			: rng.Chance( 0.3f ) && beats <= 2 ? 8 : 4;
		float[] toms = { 260f, 215f, 175f, 145f, 120f, 100f };

		for ( int b = 0; b < beats; b++ )
		{
			int beatTick = fromTick + b * Timing.TicksPerBeat;
			for ( int i = 0; i < per; i++ )
			{
				int t = _time.EvenSpan( beatTick, Timing.TicksPerBeat, i / (double)per );
				// Half the hits stay snare so it still reads as a drum fill; the rest are biased
				// by DrumTone — toms when it leans low, cymbals when it leans high.
				if ( rng.Chance( 0.5f ) ) RenderSnare( t, noise, false );
				else if ( rng.Chance( _drumTone ) ) RenderRide( t, false, _c.HatVol, noise );
				else RenderTom( t, toms[(i + b) % toms.Length], noise );
			}
		}
		RenderCrash( _time.TickToSample( toTick ), noise, rng.Chance( 0.4f ) );
	}
}
