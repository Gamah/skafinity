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

	// ── Ska ──
	public static readonly DrumGroove[] Ska =
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
			// machine. Bar 2 pushes the kick into the "and of 3".
			Kick = E( 0, R, R, R, 0, R, R, R,
			          0, R, R, 0, 0, R, R, R ),
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
			Cymbal = E( 0, R, 0, R, 0, R, 0, R ),
			GhostRate = 0.6f, CrashOnOne = 0.2f,
		},
		new()
		{
			Name = "two beat",
			// The other country feel: a two-beat "boom-chick" where the kit gets out of the way
			// of the bass and the guitar entirely.
			Kick = E( 0, R, R, R, 0, R, R, R ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, R, 0, R, 0, R, 0, Open ),
			GhostRate = 0.5f, CrashOnOne = 0.15f,
		},
	};

	// ── Metal ──
	public static readonly DrumGroove[] Metal =
	{
		new()
		{
			Name = "double kick",
			Kick = S( 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
			Cymbal = E( 0, 0, 0, 0, 0, 0, 0, 0 ),
			GhostRate = 0f, CrashOnOne = 0.55f,
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
			Kick = E( 0, R, 0, R, 0, R, 0, R ),
			Snare = E( R, R, 0, R, R, R, 0, R ),
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
		// only says where the hits land and which are "open". A thin section drops the offbeat
		// cymbal entirely rather than playing quieter — that is what makes a verse read as a
		// verse and a breakdown as a breakdown.
		bool sparse = _energy < 0.4f;
		foreach ( var h in _groove.Cymbal.Slice( barTick, to, _sectionTick, _feel ) )
		{
			bool onBeat = (h.Tick - barTick) % Timing.TicksPerBeat == 0;
			if ( sparse && !onBeat ) continue;
			int at = _time.TickToSample( h.Tick );
			bool open = h.Value == DrumGroove.Open;
			float amp = _c.HatVol * h.Vel * (onBeat ? 1f : 0.75f) * EnergyGain( 0.55f );
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

		// Extra ghosts / toms between the groove's own hits: the "busy" layer. Metal's double
		// kick already fills every sixteenth, so its groove asks for none (GhostRate 0).
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
		int beats = Math.Max( 1, span / Timing.TicksPerBeat );
		int per = rng.Chance( _c.TripletChance ) ? (rng.Chance( 0.5f ) ? 3 : 6) : 4;
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
