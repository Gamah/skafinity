using System;

namespace Skafinity;

/// <summary>
/// Oscillator and pitch primitives — the stateless maths every voice is built out of.
/// Files that use these pull them in with <c>using static Skafinity.Osc;</c>.
/// </summary>
static class Osc
{
	/// <summary>
	/// Band-limited oscillator. Naive saw/square step instantaneously at the phase wrap, and
	/// those discontinuities alias into harsh inharmonic tones — the core of the "8-/16-bit"
	/// buzz. PolyBLEP rounds each discontinuity over one sample so the harmonics fold back
	/// cleanly, for a warm analog edge instead. Sine is already band-limited; triangle's
	/// corners roll off as 1/n² so its aliasing is inaudible.
	/// </summary>
	/// <param name="t">Waveform: 0 sine, 1 saw, 2 square, 3 triangle.</param>
	/// <param name="p">Phase in [0,1).</param>
	/// <param name="dt">Phase increment per sample (cycles/sample).</param>
	public static float BlepOsc( int t, double p, double dt )
	{
		switch ( t )
		{
			case 0:
				return MathF.Sin( (float)(p * 2 * Math.PI) );
			case 1: // saw
				return (float)(2 * p - 1) - PolyBlep( p, dt );
			case 2: // square (50% duty = two opposed discontinuities)
			{
				float v = p < 0.5 ? 1f : -1f;
				v += PolyBlep( p, dt );
				double p2 = p + 0.5; if ( p2 >= 1.0 ) p2 -= 1.0;
				return v - PolyBlep( p2, dt );
			}
			default: // triangle
				return 4f * MathF.Abs( (float)p - 0.5f ) - 1f;
		}
	}

	/// <summary>PolyBLEP residual: the correction applied around a step discontinuity.</summary>
	public static float PolyBlep( double t, double dt )
	{
		if ( dt <= 0 ) return 0f;
		if ( t < dt ) { t /= dt; return (float)(t + t - t * t - 1.0); }
		if ( t > 1.0 - dt ) { t = (t - 1.0) / dt; return (float)(t * t + t + t + 1.0); }
		return 0f;
	}

	/// <summary>MIDI note number → frequency in Hz (69 = A440).</summary>
	public static float Midi( int m ) => 440f * MathF.Pow( 2f, (m - 69) / 12f );

	const float Sqrt2 = 1.41421356f;

	/// <summary>Fixed (not randomized) stereo spread for the kit's off-centre voices:
	/// 25% each way.</summary>
	public const float DrumPan = 0.25f;

	/// <summary>Constant-power pan: −1 hard left, 0 centre, +1 hard right. Gains are scaled by
	/// √2 so a centred source keeps unity gain per channel.</summary>
	public static void StereoGains( float pan, out float gL, out float gR )
	{
		pan = Math.Clamp( pan, -1f, 1f );
		double ang = (pan + 1) * 0.5 * (Math.PI / 2);
		gL = (float)Math.Cos( ang ) * Sqrt2;
		gR = (float)Math.Sin( ang ) * Sqrt2;
	}
}
