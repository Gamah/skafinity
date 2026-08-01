using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>One onset from a <see cref="Pattern"/>, mapped onto the song's own tick line.</summary>
readonly struct Hit
{
	/// <summary>Absolute song tick of the onset.</summary>
	public readonly int Tick;

	/// <summary>The pattern's payload — what the cell means is the voice's business (a bass
	/// interval, a drum accent flag, a comp figure's chord slot).</summary>
	public readonly int Value;

	/// <summary>Ticks until the pattern's next onset — the note's legato length, already scaled
	/// out of pattern time into song time.</summary>
	public readonly int SpanTicks;

	/// <summary>Velocity multiplier authored into the cell (1 = the voice's nominal level).</summary>
	public readonly float Vel;

	public Hit( int tick, int value, int spanTicks, float vel )
	{ Tick = tick; Value = value; SpanTicks = spanTicks; Vel = vel; }
}

/// <summary>
/// A rhythmic figure with its OWN length, free-running against the bar line.
///
/// This is the fix for sameyness inside a song. A table indexed by bar position can only ever
/// be one bar long, so bar 2 is bar 1 and every bar after it; a pattern that carries
/// <see cref="LengthTicks"/> is looped against absolute song time instead, and one mechanism
/// covers every case the engine wants:
///
///   • 1 bar    — the old behaviour, unchanged.
///   • 2 bars   — call and response: bar 2 answers bar 1 (the ska horn convention).
///   • 4 bars   — a four-bar phrase for free, with the variation authored into its last bar.
///   • 3 eighths— a hemiola: the figure and the bar line drift apart and re-converge (see
///                <see cref="MusicGen"/>'s metric-dissonance handling).
///
/// Positions are ticks (see <see cref="Timing"/>), so a pattern is meter-agnostic: nothing here
/// assumes a bar, a beat count, or an eighth grid. <see cref="Eighths"/> is a convenience for
/// the tables that are still authored one cell per eighth.
///
/// Patterns are immutable and shared between songs — everything per-song (which pattern, where
/// it is anchored) is the caller's.
/// </summary>
sealed class Pattern
{
	/// <summary>Cell value meaning "no onset here" in an eighth-authored table.</summary>
	public const int Rest = Harmony.Rest;

	/// <summary>The figure's own length. The bar line is not involved.</summary>
	public readonly int LengthTicks;

	readonly int[] _tick;
	readonly int[] _value;
	readonly int[] _span;    // ticks to the next onset, wrapping around the loop
	readonly float[] _vel;

	public int Count => _tick.Length;

	/// <summary>Onsets, in ticks from the pattern's own start. Must be sorted and inside
	/// <paramref name="lengthTicks"/>.</summary>
	public Pattern( int lengthTicks, int[] ticks, int[] values, float[] vels = null )
	{
		LengthTicks = Math.Max( 1, lengthTicks );
		_tick = ticks; _value = values;
		_vel = vels ?? Filled( ticks.Length, 1f );
		_span = new int[ticks.Length];
		for ( int i = 0; i < ticks.Length; i++ )
		{
			int next = i + 1 < ticks.Length ? _tick[i + 1] : _tick[0] + LengthTicks;
			_span[i] = Math.Max( 1, next - _tick[i] );
		}
	}

	static float[] Filled( int n, float v )
	{
		var a = new float[n];
		for ( int i = 0; i < n; i++ ) a[i] = v;
		return a;
	}

	/// <summary>Build from a table authored one cell per eighth — the shape most of the engine's
	/// tables are still written in. <see cref="Rest"/> cells carry no onset (the previous note
	/// sustains through them), which is what gives a cell its span.</summary>
	public static Pattern Eighths( params int[] cells ) => Eighths( null, cells );

	/// <summary>As <see cref="Eighths(int[])"/>, with a velocity per cell (null = all 1).</summary>
	public static Pattern Eighths( float[] vels, params int[] cells )
	{
		var t = new List<int>(); var v = new List<int>(); var g = new List<float>();
		for ( int e = 0; e < cells.Length; e++ )
		{
			if ( cells[e] == Rest ) continue;
			t.Add( e * Timing.TicksPerEighth );
			v.Add( cells[e] );
			g.Add( vels != null && e < vels.Length ? vels[e] : 1f );
		}
		return new Pattern( cells.Length * Timing.TicksPerEighth, t.ToArray(), v.ToArray(), g.ToArray() );
	}

	/// <summary>Build from sixteenth cells — the resolution the metal gallop and the country train
	/// beat are actually authored at.</summary>
	public static Pattern Sixteenths( params int[] cells )
	{
		var t = new List<int>(); var v = new List<int>();
		int step = Timing.TicksPerEighth / 2;
		for ( int s = 0; s < cells.Length; s++ )
		{
			if ( cells[s] == Rest ) continue;
			t.Add( s * step ); v.Add( cells[s] );
		}
		return new Pattern( cells.Length * step, t.ToArray(), v.ToArray() );
	}

	/// <summary>
	/// The onsets that land in <c>[fromTick, toTick)</c> of SONG time.
	///
	/// The pattern loops from <paramref name="anchorTick"/> (a section's first downbeat, so a
	/// multi-bar figure restarts with the section rather than wherever the song happens to be).
	/// <paramref name="feel"/> is the section's half/double-time multiplier — the pattern's own
	/// rate, not the tempo: 0.5 stretches every figure to twice its length, 2 halves it.
	///
	/// There is deliberately NO "shift every onset late" argument. A figure that pushes into the
	/// next bar says so in its CELLS — the ska skank's stab on the "and of 4", the Charleston, the
	/// horn answer — which is one gesture at a phrase seam, and that is where a push belongs. A
	/// section-wide constant offset expressed the same idea the other way and it did not survive a
	/// listen; see the note in <see cref="SongForm"/>.
	/// </summary>
	public List<Hit> Slice( int fromTick, int toTick, int anchorTick = 0, float feel = 1f )
	{
		var hits = new List<Hit>();
		if ( toTick <= fromTick || _tick.Length == 0 ) return hits;
		if ( feel <= 0f ) feel = 1f;

		// Song tick t ↔ pattern tick p:  p = (t − anchor) · feel.  The window is converted once,
		// then walked repetition by repetition; every hit maps back the same way.
		double pFrom = (fromTick - anchorTick) * (double)feel;
		double pTo = (toTick - anchorTick) * (double)feel;
		long rep = (long)Math.Floor( pFrom / LengthTicks );

		for ( ; ; rep++ )
		{
			double bas = (double)rep * LengthTicks;
			if ( bas >= pTo ) break;
			for ( int i = 0; i < _tick.Length; i++ )
			{
				double p = bas + _tick[i];
				if ( p < pFrom || p >= pTo ) continue;
				int t = anchorTick + (int)Math.Round( p / feel );
				int span = Math.Max( 1, (int)Math.Round( _span[i] / feel ) );
				hits.Add( new Hit( t, _value[i], span, _vel[i] ) );
			}
		}
		return hits;
	}
}
