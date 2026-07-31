using System;
using System.Collections.Generic;

namespace Skafinity;

// Oscillator and pitch primitives: the band-limited (PolyBLEP) saw/square, MIDI→Hz, and
// the constant-power stereo panner.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// Band-limited oscillator. Naive saw/square step instantaneously at the phase wrap,
	// and those discontinuities alias into harsh inharmonic tones — the core of the
	// "8-/16-bit" buzz. PolyBLEP rounds each discontinuity over one sample so the harmonics
	// fold back cleanly, for a warm analog edge instead. Sine is already band-limited;
	// triangle's corners roll off as 1/n² so its aliasing is inaudible.
	// p = phase in [0,1), dt = phase increment per sample (cycles/sample).
	static float BlepOsc( int t, double p, double dt )
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

	// PolyBLEP residual: the correction applied around a step discontinuity.
	static float PolyBlep( double t, double dt )
	{
		if ( dt <= 0 ) return 0f;
		if ( t < dt ) { t /= dt; return (float)(t + t - t * t - 1.0); }
		if ( t > 1.0 - dt ) { t = (t - 1.0) / dt; return (float)(t * t + t + t + 1.0); }
		return 0f;
	}

	static float Midi( int m ) => 440f * MathF.Pow( 2f, (m - 69) / 12f );

	const float Sqrt2 = 1.41421356f;
	// Fixed (not randomized) stereo spread for the kit's off-centre voices: 25% each way.
	const float DrumPan = 0.25f;
	static void StereoGains( float pan, out float gL, out float gR )
	{
		pan = Math.Clamp( pan, -1f, 1f );
		double ang = (pan + 1) * 0.5 * (Math.PI / 2);
		gL = (float)Math.Cos( ang ) * Sqrt2;
		gR = (float)Math.Sin( ang ) * Sqrt2;
	}
}
