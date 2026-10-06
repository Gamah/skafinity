using System;

namespace Skafinity.EngineTests;

/// <summary>
/// The physical models' two load-bearing invariants — the ones that are silent when they break.
///
/// A delay-line string is TUNED BY ITS OWN LENGTH, so every term inside the loop is part of the
/// pitch: the fractional read, the loop filter's phase delay, and the size of the ring buffer the
/// read is clamped to. Get any of them wrong and the instrument is not broken, it is merely a few
/// cents off across its whole range, under a drive stage and inside a chord — which no listening
/// test reliably catches and no render digest can describe. Both of these were real: the buffer
/// was sized to the nominal period, so the clamp truncated the fractional part of it and the
/// guitar played 10 cents sharp; and the loop low-pass's phase delay was not subtracted, so the
/// damping knob also detuned the string.
///
/// The second check is the model's whole reason for existing. A plucked string must lose its high
/// partials FASTER than its fundamental, because the loop loss is applied once per period and a
/// high partial has more of them. That is the property a saw through one envelope cannot have at
/// any setting, so if it ever measures flat, the model has stopped earning its cost.
/// </summary>
static class SynthModelTests
{
	const int Rate = 44100;

	/// <summary>Render one note of one patch, alone, into a mono buffer.</summary>
	static float[] One( Patch p, int midi, double seconds )
	{
		var g = MusicGen.ForAudition( new MusicGen.Config { SampleRate = Rate, DoubleTrack = 0f },
			seconds, 120 );
		g.RenderPatch( 0, (int)(Rate * seconds), Osc.Midi( midi ), p, mono: true );
		var (l, _) = g.AuditionBuffers();
		g.RenderPitchedRange( 0, l.Length );
		return l;
	}

	/// <summary>A bare string: no body filter, no drive, so what is measured is the loop and
	/// nothing after it.</summary>
	static Patch Bare( float t60, float damp ) => new Patch
	{
		Model = Model.String, Voices = 1, Amp = 0.5f, Attack = 0.001f,
		StringDecay = t60, StringDamp = damp, Pluck = 0.25f, PickWidth = 0.03f,
		Cutoff = 0f, Reso = 1f, Drive = 0f,
	};

	/// <summary>Fundamental frequency, measured in a way that a DECAYING tone cannot bias.
	///
	/// The obvious estimators both lie here. A peak-picked DFT has a resolution of one bin, which
	/// at the bottom of the bass is tens of cents. Autocorrelation is worse and more insidious: the
	/// envelope of a decaying note makes later lags systematically smaller, which drags the
	/// correlation peak toward SHORTER lags, so every note reads sharp by an amount proportional to
	/// how fast it decays — which looks exactly like a tuning bug that gets worse with damping.
	///
	/// So: correlate against the EXPECTED frequency in two windows a known distance apart, and read
	/// the frequency off how fast the residual phase drifts between them. If the real frequency is
	/// expect + d, that phase advances at 2.pi.d per second regardless of amplitude, so the method
	/// does not care what the envelope is doing. The gap is chosen so a half-semitone error is
	/// still under half a turn of phase, which is what keeps the unwrap unambiguous.</summary>
	static double Freq( float[] x, double expect )
	{
		int gap = (int)(Rate / (4 * expect * 0.03));   // < half a turn for a 50-cent error
		int len = Math.Min( (int)(Rate * 8 / expect), (x.Length - gap) / 2 );
		if ( len < 16 ) return expect;
		double Phase( int t0 )
		{
			double re = 0, im = 0;
			for ( int i = 0; i < len; i++ )
			{
				// GLOBAL time origin. Restarting the reference at each window's own t = 0 makes
				// the difference below measure the note's whole accumulated phase instead of its
				// drift against the reference — which wraps many times over and reads as a large
				// constant error at every pitch.
				double a = 2 * Math.PI * expect * (t0 + i) / Rate;
				re += x[t0 + i] * Math.Cos( a );
				im -= x[t0 + i] * Math.Sin( a );
			}
			return Math.Atan2( im, re );
		}
		double drift = Phase( gap ) - Phase( 0 );
		while ( drift > Math.PI ) drift -= 2 * Math.PI;
		while ( drift < -Math.PI ) drift += 2 * Math.PI;
		return expect + drift * Rate / (2 * Math.PI * gap);
	}

	/// <summary>Amplitude of the kth harmonic over a window, by direct correlation.</summary>
	static double Harmonic( float[] x, int from, int len, double f, int k )
	{
		double s = 0, c = 0;
		for ( int i = 0; i < len && from + i < x.Length; i++ )
		{
			double a = 2 * Math.PI * f * k * i / Rate;
			s += x[from + i] * Math.Sin( a );
			c += x[from + i] * Math.Cos( a );
		}
		return Math.Sqrt( s * s + c * c ) / len;
	}

	public static void Run( Action<string, bool, string> check )
	{
		// Across the whole playable register and across the damping range, because both of the
		// real bugs were register- or damping-dependent and a single note would have missed them.
		bool tuned = true;
		var off = new System.Text.StringBuilder();
		foreach ( int midi in new[] { 24, 28, 40, 52, 64, 76, 88 } )
			foreach ( float damp in new[] { 0.0f, 0.22f, 0.5f, 0.8f } )
			{
				double want = Osc.Midi( midi );
				double wq = 2 * Math.PI * want / Rate;
				double loopMag = (1 - damp) / Math.Sqrt( 1 - 2 * damp * Math.Cos( wq ) + damp * damp );
				// A loop that loses more than a tenth of its energy per round trip has a Q in the
				// single digits — a few periods and it is gone. That is a thud, not a note, and
				// asking what pitch it is has no answer to be right or wrong about. It is also a
				// physically correct thing for the model to produce: it is what a hard palm mute on
				// a high string sounds like.
				if ( loopMag < 0.9 ) continue;
				var buf = One( Bare( 1.5f, damp ), midi, 0.45 );
				double got = Freq( buf, want );
				double cents = 1200 * Math.Log2( got / want );
				if ( Math.Abs( cents ) > 2 )
					off.Append( $"midi {midi} damp {damp:0.00}: {cents:+0.0;-0.0} cents, "
						+ $"loop gain {loopMag:0.000}; " );
				// 5 cents is under the ~6 cent threshold at which a sustained tone against another
				// instrument starts to read as out of tune, and well under the detune the width
				// stage deliberately applies.
				if ( Math.Abs( cents ) > 5 ) tuned = false;
			}
		check( "a plucked string tunes to its pitch across the register and the damping range",
			tuned, off.Length > 0 ? off.ToString() : null );

		// The loop filter has to be what makes this happen — with it wide open the partials should
		// decay TOGETHER, and closing it should separate them. Both halves are asserted, because
		// "high partials decay faster" is also true of a patch that simply has no high partials.
		var open = One( Bare( 2.0f, 0.02f ), 52, 1.2 );
		var damped = One( Bare( 2.0f, 0.55f ), 52, 1.2 );
		double f0 = Osc.Midi( 52 );
		int win = (int)(0.08 * Rate), late = (int)(0.45 * Rate);
		double Lost( float[] x, int k )
			=> 20 * Math.Log10( Harmonic( x, 0, win, f0, k ) / (Harmonic( x, late, win, f0, k ) + 1e-12) );

		double o1 = Lost( open, 1 ), o8 = Lost( open, 8 );
		double d1 = Lost( damped, 1 ), d8 = Lost( damped, 8 );
		check( "a lossless loop decays every partial at nearly the same rate",
			Math.Abs( o8 - o1 ) < 6, $"1st lost {o1:0.0} dB, 8th lost {o8:0.0} dB" );
		check( "an absorbent termination makes the 8th partial decay far faster than the 1st",
			d8 - d1 > 12, $"1st lost {d1:0.0} dB, 8th lost {d8:0.0} dB" );

		// Modal: the free-free bar's partials must sit on the solved roots and NOT on whole-number
		// ratios, because that distinction is the entire difference between a tine and an organ.
		var tine = One( new Patch
		{
			Model = Model.Modal, Voices = 1, Amp = 0.5f, Attack = 0.001f,
			ModalSet = ModalBank.Bar, ModalCount = 4, ModalDecay = 2f,
			Cutoff = 0f, Reso = 1f, Drive = 0f,
		}, 52, 0.6 );
		double fund = Harmonic( tine, 0, (int)(0.25 * Rate), f0, 1 );
		double bar2 = Harmonic( tine, 0, (int)(0.25 * Rate), f0 * Osc.BarModes[1], 1 );
		double harm2 = Harmonic( tine, 0, (int)(0.25 * Rate), f0, 2 );
		check( "a struck bar sounds its second mode at 2.76x, not at 2x",
			bar2 > fund * 0.1 && harm2 < bar2 * 0.1,
			$"fundamental {fund:0.0000}, 2.76x {bar2:0.0000}, 2x {harm2:0.0000}" );
	}
}
