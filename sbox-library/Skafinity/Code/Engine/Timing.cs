using System;

namespace Skafinity;

/// <summary>
/// The song's time base: how a musical position becomes a sample index.
///
/// Everything the whole band shares about timing lives here — the eighth-note length, the
/// shuffle, and the kit's constant push/lay-back — so a voice never has to reconstruct it.
///
/// SWING. On-beat (even) eighths are anchors that stay put; off-beat (odd) eighths are pushed
/// late by <see cref="Swing"/> of an eighth, and positions between anchors interpolate. That
/// interpolation is the point: a sixteenth (e + 0.5) or a triplet subdivision lands on the
/// SAME warped grid as the eighths, so every voice shuffles in lockstep rather than the skank
/// chop alone.
///
/// It is also the known wart — interpolating across a warped pair spaces a triplet unevenly
/// (a 3-note triplet over a swung beat lands at ~0/0.38/0.76, not 0/0.33/0.67). Even-vs-
/// shuffled wants to be an explicit choice here rather than a side effect of the warp. This
/// type is where that fix goes, and where the tick-based time base replaces
/// <see cref="Swung"/> with a running sample accumulator. See PLAN.md.
/// </summary>
readonly struct Timing
{
	/// <summary>Samples per eighth note.</summary>
	public readonly int Spe;

	/// <summary>Per-song shuffle: offbeat eighths are pushed late by this fraction of an
	/// eighth. 0 = straight.</summary>
	public readonly float Swing;

	/// <summary>Per-song-constant kit timing bias in samples (− ahead / + laid back). The kit
	/// alone reads this — it is what makes the drums push or drag against the rest of the
	/// band.</summary>
	public readonly int DrumPush;

	public Timing( int spe, float swing, int drumPush )
	{
		Spe = spe;
		Swing = swing;
		DrumPush = drumPush;
	}

	/// <summary>Absolute sample index of a within-bar position measured in eighth notes
	/// (0.5 = a sixteenth), warped by the swing.</summary>
	public int Swung( int barStart, double eighths )
	{
		double baseE = Math.Floor( eighths );
		double frac = eighths - baseE;
		long slot = (long)baseE;
		double startShift = (slot & 1) == 1 ? Swing : 0.0;          // this eighth's onset shift
		double endShift   = ((slot + 1) & 1) == 1 ? Swing : 0.0;    // the next eighth's onset shift
		double pos = baseE + startShift + frac * (1.0 + endShift - startShift);
		return barStart + (int)Math.Round( pos * Spe );
	}
}
