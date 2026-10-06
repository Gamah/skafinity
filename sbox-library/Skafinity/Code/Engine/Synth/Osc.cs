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

	/// <summary>
	/// Variable-duty pulse — the strict generalisation of the square, and the only oscillator
	/// here whose spectrum is CHOSEN rather than inherited.
	///
	/// A pulse of duty <paramref name="duty"/> has harmonic amplitudes proportional to
	/// |sin(pi n d)| / n. The 1/n is the saw's slope, so the duty is pure spectral SHAPE: it
	/// nulls every harmonic whose index is a multiple of 1/d. d = 1/2 deletes the even harmonics
	/// (that is all a square is), d = 1/3 deletes every third, d = 1/4 every fourth. That is the
	/// whole reason to have it: a filter can only take harmonics off the TOP, and no amount of
	/// low-pass will remove the 2nd harmonic while keeping the 3rd. A reed bore does exactly
	/// that, and a lowpassed saw cannot imitate it at any cutoff.
	///
	/// And because d is a free parameter, sweeping it (PWM) moves the whole comb of nulls
	/// through the spectrum while every harmonic's frequency stays put — a moving timbre with
	/// no moving filter and no pitch change.
	///
	/// Built as two opposed steps so each gets its own BLEP. The levels are chosen so the mean
	/// is zero at EVERY duty (d.(2-2d) + (1-d).(-2d) = 0): an un-centred pulse would otherwise
	/// inject a duty-dependent DC step at every note, which the sweep turns into a thump. At
	/// d = 1/2 this is bit-identical to <see cref="BlepOsc"/>'s square.
	/// </summary>
	public static float PulseOsc( double p, double dt, float duty )
	{
		duty = Math.Clamp( duty, 0.03f, 0.97f );
		float v = p < duty ? 2f * (1f - duty) : -2f * duty;
		v += PolyBlep( p, dt );
		double p2 = p + 1.0 - duty; if ( p2 >= 1.0 ) p2 -= 1.0;
		return v - PolyBlep( p2, dt );
	}

	/// <summary>
	/// The transverse modes of a FREE-FREE BAR, as ratios to its first mode — the tine of an
	/// electric piano, a glockenspiel, a struck metal tongue.
	///
	/// A bar is not a string. A string's restoring force is tension, giving a wave equation whose
	/// modes are 1, 2, 3, 4...; a bar's is STIFFNESS, giving a fourth-order equation whose
	/// frequencies go as the square of the mode's wavenumber, and whose free ends admit only the
	/// roots of cos(b)cosh(b) = 1: b = 4.7300, 7.8532, 10.9956, 14.1372. The ratios below are
	/// those roots squared over the first — 2.76, 5.40, 8.93 — so they are solved, not tuned, and
	/// they are the reason a tine sounds like metal rather than like a quiet trumpet: nothing in
	/// it is a whole-number multiple of anything else, so the ear hears no harmonic series to
	/// fuse into one pitch, and the upper partials read as a separate bright ping.
	///
	/// They are deliberately NOT a Patch field: a partial is just another note an octave-ish up
	/// with its own shorter decay, so a voice renders them as extra patches and the inner loop
	/// pays nothing. Stiffness makes high modes lossier (loss grows with wavenumber), so each
	/// successive partial must be given a shorter decay than the one below it.
	/// </summary>
	public static readonly float[] BarModes = { 1f, 2.7565f, 5.4039f, 8.9328f };

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
