using System;

namespace Skafinity;

/// <summary>
/// The song's time base: how a musical position becomes a sample index.
///
/// POSITIONS ARE INTEGER TICKS, absolute from the song's first downbeat. <see cref="TicksPerBeat"/>
/// is 48 — 24 already renders every subdivision the engine currently uses exactly (8ths, 16ths,
/// 8th and 16th triplets), and 48 is free headroom for finer ornaments. Deliberately NOT a
/// 16th-note grid: that would lock out triplets, and the triplet gallop, shuffle fills and
/// 8th-triplet horn arps all depend on them.
///
/// TEMPO LIVES IN AN ACCUMULATOR. <see cref="_tickSample"/> holds the sample position of every
/// tick in the song, built by accumulating a per-tick sample delta rather than multiplying a
/// constant. With one tempo the two agree exactly; the difference is that a tempo *curve* —
/// a per-section tempo, or an ending ritard — is a matter of varying the delta as the loop
/// runs, instead of a rewrite. That is nearly free to build now and expensive to retrofit.
///
/// SWING IS NOT ON THE GRID. Ticks are metrical positions; the shuffle is a warp applied on
/// top when a tick is converted to a sample, so it never quantises anything. On-beat (even)
/// eighths are anchors that stay put; off-beat (odd) eighths are pushed late by
/// <see cref="Swing"/> of an eighth, and positions between anchors interpolate — which is what
/// makes a 16th or a triplet subdivision land on the same warped grid as the eighths, so the
/// whole band shuffles in lockstep rather than the skank chop alone.
/// </summary>
sealed class Timing
{
	/// <summary>Ticks per quarter note. Every musical position in the engine is an integer
	/// multiple of a fraction of this.</summary>
	public const int TicksPerBeat = 48;

	/// <summary>Ticks per eighth note — the subdivision most patterns are still authored on.</summary>
	public const int TicksPerEighth = TicksPerBeat / 2;

	/// <summary>Beats in a bar. 4 today; the machinery below does not assume it.</summary>
	public readonly int BeatsPerBar;

	/// <summary>Ticks in a bar.</summary>
	public readonly int BarTicks;

	/// <summary>How the bar's beats group for accent purposes — <c>{1,1,1,1}</c> in 4/4,
	/// <c>{3,2,2}</c> for a 7/8. Nothing reads this yet; it is here so that when a voice needs
	/// to know where the strong beats are, the answer is already carried by the time base
	/// rather than assumed from <see cref="BeatsPerBar"/>.</summary>
	public readonly int[] BeatGrouping;

	/// <summary>Per-song shuffle: offbeat eighths are pushed late by this fraction of an
	/// eighth. 0 = straight.</summary>
	public readonly float Swing;

	/// <summary>Per-song-constant kit timing bias in samples (− ahead / + laid back). Applied
	/// in continuous sample space, off the grid — it is a feel, not a position. The kit alone
	/// reads it, which is what makes the drums push or drag against the rest of the band.</summary>
	public readonly int DrumPush;

	public readonly int SampleRate;

	/// <summary>Sample position of each tick boundary, accumulated. Index 0 is the song's first
	/// downbeat; the array runs one past the last tick so interpolation always has a right
	/// neighbour.</summary>
	readonly double[] _tickSample;

	readonly double _secPerTick;

	/// <param name="totalTicks">Length of the song in ticks. The accumulator is built once over
	/// this span.</param>
	/// <param name="samplesPerTick">Sample delta per tick. A constant today; the accumulator
	/// exists so this can become a per-tick value.</param>
	public Timing( int beatsPerBar, int totalTicks, double samplesPerTick, float swing,
		int drumPush, int sampleRate, int[] beatGrouping = null )
	{
		BeatsPerBar = beatsPerBar;
		BarTicks = beatsPerBar * TicksPerBeat;
		Swing = swing;
		DrumPush = drumPush;
		SampleRate = sampleRate;
		_secPerTick = samplesPerTick / sampleRate;

		BeatGrouping = beatGrouping ?? Uniform( beatsPerBar );

		// The accumulator. One extra entry so TickToSample can always read tick+1.
		_tickSample = new double[Math.Max( 2, totalTicks + 2 )];
		double at = 0;
		for ( int i = 0; i < _tickSample.Length; i++ )
		{
			_tickSample[i] = at;
			at += samplesPerTick;   // ← the per-tick delta a tempo curve would vary
		}
	}

	static int[] Uniform( int beats )
	{
		var g = new int[beats];
		for ( int i = 0; i < beats; i++ ) g[i] = 1;
		return g;
	}

	/// <summary>Absolute sample index of an absolute tick, with the shuffle applied.</summary>
	public int TickToSample( int tick ) => SampleAt( Warp( tick ) );

	/// <summary>Absolute sample index of a fractional tick position, with the shuffle applied.
	/// Used where an ornament subdivides finer than the tick grid.</summary>
	public int TickToSample( double tick ) => SampleAt( Warp( tick ) );

	/// <summary>Apply the shuffle: tick position in, warped tick position out. Even eighths are
	/// anchors; odd eighths are pushed late; everything between interpolates across the pair.
	/// </summary>
	double Warp( double tick )
	{
		if ( Swing <= 0f ) return tick;
		double e = tick / TicksPerEighth;               // position in eighths
		double baseE = Math.Floor( e );
		double frac = e - baseE;
		long slot = (long)baseE;
		double startShift = (slot & 1) == 1 ? Swing : 0.0;        // this eighth's onset shift
		double endShift = ((slot + 1) & 1) == 1 ? Swing : 0.0;    // the next eighth's onset shift
		double warpedE = baseE + startShift + frac * (1.0 + endShift - startShift);
		return warpedE * TicksPerEighth;
	}

	/// <summary>Accumulator lookup, linearly interpolated between tick boundaries.</summary>
	int SampleAt( double tick )
	{
		if ( tick <= 0 ) return 0;
		int i = (int)tick;
		if ( i >= _tickSample.Length - 1 ) return (int)Math.Round( _tickSample[^1] );
		double frac = tick - i;
		return (int)Math.Round( _tickSample[i] + frac * (_tickSample[i + 1] - _tickSample[i]) );
	}

	/// <summary>Length in samples of a span of <paramref name="ticks"/> ticks. A duration, not a
	/// position — it carries no swing.</summary>
	public int SamplesForTicks( double ticks ) => (int)Math.Round( ticks * SamplesPerTick );

	/// <summary>Length in seconds of a span of <paramref name="ticks"/> ticks.</summary>
	public double SecondsForTicks( double ticks ) => ticks * _secPerTick;

	/// <summary>The nominal per-tick sample delta. Durations use this; positions go through the
	/// accumulator so they stay correct if tempo ever varies.</summary>
	public double SamplesPerTick => _tickSample[1] - _tickSample[0];

	/// <summary>Eighth-note cells in a bar. Pattern tables are still authored one cell per
	/// eighth; this is what maps such a table onto the bar.</summary>
	public int EighthsPerBar => BarTicks / TicksPerEighth;

	/// <summary>Samples in an eighth note — the unit most durations are still expressed in.</summary>
	public int Spe => SamplesForTicks( TicksPerEighth );

	/// <summary>Seconds in an eighth note.</summary>
	public double SecPerEighth => SecondsForTicks( TicksPerEighth );
}
