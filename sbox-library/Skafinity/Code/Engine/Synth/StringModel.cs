using System;

namespace Skafinity;

// THE PLUCKED STRING — a digital waveguide.
//
// WHY A DELAY LINE IS NOT A TRICK. The wave equation on an ideal string has the d'Alembert
// solution y(x,t) = f(x - ct) + g(x + ct): two shapes travelling in opposite directions at speed
// c, unchanged, reflecting at the ends. Sample that at one point and a travelling shape becomes a
// sequence arriving L/c seconds later — which is a DELAY LINE, exactly, with no approximation in
// it. The two directions can be folded into one loop because the reflections are the same, so a
// string is: a buffer of one period, read, attenuated by whatever the terminations lose, written
// back. That is the whole model. Everything an ear calls "a plucked string" then follows from
// three facts rather than from three parameters:
//
//   * PER-PARTIAL DECAY IS THE LOOP FILTER'S RESPONSE. Each round trip multiplies partial n by
//     |H(w_n)|, so its decay time is -N / (sr.ln|g.H(w_n)|). Put a low-pass in the loop and the
//     12th partial dies an order of magnitude faster than the 1st, because it goes through the
//     loss twelve times as often per unit of its own period. No envelope was written down. This
//     is the single biggest difference from a saw through a low-pass, where every partial decays
//     together and the ear reads the result as a filter closing.
//   * THE PLUCK IS AN INITIAL CONDITION, NOT AN ATTACK. The buffer's contents at t=0 ARE the
//     string's starting shape, and its Fourier coefficients ARE the mode amplitudes. A string
//     pulled aside at a fraction b of its length starts as a TRIANGLE with its apex at b, whose
//     nth coefficient is sin(n.pi.b)/n^2 — so pluck position and the 1/n^2 rolloff of a real
//     pluck are not two settings, they are one shape. See Excite.
//   * SPECTRUM EVOLVES WITHOUT ANYTHING SWEEPING. The sound at 200 ms is the initial condition
//     after 200 ms of frequency-dependent loss. Nothing modulates; the brightness falls because
//     the energy up there is gone.
//
// READ POINTER, NOT AN INTEGER DELAY. The loop length is read at a FRACTIONAL position with
// linear interpolation, so the pitch is continuous and the knobs that bend it (BENDINESS, VIBRATO
// — see PitchMod) work on a string the way they work on everything else: a bend is the delay line
// getting shorter, which is what a bend physically is. The usual alternative, an integer delay
// plus a tuning allpass, cannot be retuned mid-note without a click. The cost is that linear
// interpolation is itself a mild low-pass, so it adds a little loss at the top of the spectrum
// and stretches the upper partials slightly sharp. Real strings are stretched sharp too, by
// stiffness, but that is a coincidence of direction and not a model of it — the honest statement
// is that the interpolator's loss is folded into the damping budget below.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>Point a patch at the string model. The cutoff ENVELOPE goes with it: it was the
	/// subtractive voice's stand-in for high partials dying first, and the loop filter now does
	/// that for real, so leaving it on would describe the same thing twice. The static cutoff
	/// stays — that one is the instrument's body and the amp, which are downstream of the string.
	/// </summary>
	static void AsString( ref Patch p, float t60, float damp, float beta, float width,
		float noise = 0.03f )
	{
		p.Model = Model.String;
		p.StringDecay = t60; p.StringDamp = damp;
		p.Pluck = beta; p.PickWidth = width; p.PickNoise = noise;
		p.CutEnv = 0f;
	}
}

/// <summary>One plucked string. Owns a slice of the window's delay line; holds no reference to
/// anything shared, so it is safe inside a parallel render window.</summary>
struct PluckedString
{
	readonly float[] _buf;
	readonly int _len;          // ring length actually used
	readonly float _period;     // loop delay in samples at nominal pitch
	readonly float _g;          // per-round-trip gain
	readonly float _damp;       // loop low-pass coefficient (0 = none, ->1 = very dark)
	float _read;                // fractional read position
	float _lp;                  // loop low-pass state

	/// <summary>A hand damping the string — the shortest decay a plucked note is allowed, so a
	/// palm mute is still a note and not a click. 25 ms is about what a palm on a bridge leaves.
	/// </summary>
	const float MinDecaySec = 0.025f;

	/// <summary>Headroom in the delay line for a DOWNWARD bend — a tone and a bit, which is past
	/// anything the expression layer asks for (see Patch.BendSemis). A bend down lengthens the
	/// loop, so the ring buffer has to be longer than the nominal period or the read clamps and
	/// the pitch stops following the gesture.</summary>
	const float BendRoom = 1.2f;

	public PluckedString( float freq, in Patch p, int sr, SynthScratch sc )
	{
		_buf = sc.Delay;
		// THE LOOP FILTER IS PART OF THE LOOP LENGTH. The one-pole low-pass y = (1-d).x + d.y sits
		// inside the feedback path, so its own delay lengthens the loop and flattens the note — the
		// damping knob would otherwise detune the instrument.
		//
		// AND IT MUST BE EVALUATED AT THE NOTE'S FREQUENCY, not at DC. The convenient d/(1-d) is
		// the delay as w -> 0; the real one is arg H(w)/w, which falls as the frequency rises. At
		// the top of the register with a damped string the two differ by most of a sample out of
		// thirty, which is 60 cents — and that is the whole trap in a tuned-by-length model: the
		// error is proportional to the correction, so it is invisible on a low note and wild on a
		// high one. Evaluating at `freq` is not circular: the loop is being asked to resonate
		// there, so that is where its delay has to come out right.
		float damp = Math.Clamp( p.StringDamp, 0f, 0.95f );
		float f = Math.Max( 20f, freq );
		double w = 2 * Math.PI * f / sr;
		double filterDelay = damp > 0f
			? Math.Atan2( damp * Math.Sin( w ), 1.0 - damp * Math.Cos( w ) ) / w : 0.0;
		_period = (float)(sr / f - filterDelay);
		// Sized for the LOWEST pitch the gesture layer can bend to, not for the nominal one. The
		// read is clamped to the ring, so a buffer that is merely long enough at nominal pitch
		// silently truncates the fractional part of the period — which is a note up to a tenth of
		// a semitone sharp, across the whole instrument, with nothing to see in the code.
		_len = Math.Min( _buf.Length, (int)(_period * BendRoom) + 4 );

		// DAMPING IS SPECIFIED AS THE FUNDAMENTAL'S DECAY TIME, because that is the thing a player
		// and a mix engineer both mean by "how long does it ring", and the model then derives the
		// per-round-trip gain from it instead of the other way round:
		//     g = exp( -period / (sr . T60/6.91) )   — 6.91 = ln(1000), i.e. -60 dB.
		// The upper partials need no number at all; they come out of the same g through the loop
		// low-pass. A patch therefore says "ring for 1.8 s" or "ring for 80 ms" and gets a
		// physically consistent spectrum for free, which is also why the chug/palm-mute knobs can
		// drive this directly.
		float t60 = Math.Max( MinDecaySec, p.StringDecay );
		_g = (float)Math.Exp( -_period * 6.907755 / (t60 * sr) );
		_damp = damp;

		Excite( _buf, sc.Work, _len, p );
		_read = 0f;
		_lp = 0f;
	}

	/// <summary>The initial condition: the shape the string is in at the moment it is let go.
	///
	/// A plucked string is pulled aside at one point, so its shape is two straight segments — a
	/// triangle with its apex at the pluck position b. Written around the loop, that triangle's
	/// Fourier series IS the mode spectrum: amplitude sin(n.pi.b)/n^2, which carries BOTH the
	/// nulls at every 1/b-th partial AND the 1/n^2 rolloff every real pluck has. Neither is a
	/// parameter here; they are consequences of a triangle.
	///
	/// PICK WIDTH IS A CONVOLUTION. A pick is not a point: it holds the string over a finite
	/// contact length, which rounds the apex. Rounding a corner is a moving average over that
	/// width, and a moving average of width w nulls everything above about sr/w — so a soft wide
	/// fingertip is dull and a hard narrow pick is bright, for the reason it actually is, and the
	/// knob is a LENGTH rather than a cutoff.
	///
	/// The mean is removed last. A string is clamped at both ends and cannot hold a DC offset; one
	/// left in the loop would circulate and thump as the note decays.</summary>
	static void Excite( float[] buf, float[] work, int len, in Patch p )
	{
		float beta = Math.Clamp( p.Pluck > 0f ? p.Pluck : 0.25f, 0.02f, 0.98f );
		int apex = Math.Max( 1, Math.Min( len - 2, (int)(beta * len) ) );
		for ( int i = 0; i < len; i++ )
			buf[i] = i <= apex ? i / (float)apex : (len - 1 - i) / (float)Math.Max( 1, len - 1 - apex );

		// Pick width as a fraction of the string's length. 0.02 is a hard narrow plectrum, 0.25 a
		// thumb. Below two samples there is nothing to average and the corner stays sharp.
		int w = (int)(Math.Clamp( p.PickWidth, 0.004f, 0.4f ) * len);
		if ( w >= 2 )
		{
			// Circular moving average — the string is a loop, so the window wraps like everything
			// else on it. Running sum, so the cost does not depend on the width.
			float sum = 0f;
			for ( int i = 0; i < w; i++ ) sum += buf[i];
			for ( int i = 0; i < len; i++ )
			{
				work[i] = sum / w;
				sum += buf[(i + w) % len] - buf[i];
			}
			Array.Copy( work, buf, len );
		}

		// Pick noise: the scrape of the plectrum crossing the winding, and the string's own
		// transverse slap against the fret. Broadband, so it excites every mode at once and the
		// loop filter then strips it in a few milliseconds — which is why a little of it reads as
		// "attack" rather than as hiss. Deterministic from the pluck so synthesis stays a pure
		// function of the event (see RenderPitchedRange).
		if ( p.PickNoise > 0f )
		{
			uint s = unchecked( (uint)(beta * 1e6f) * 2654435761u ^ 0x9E3779B9u );
			for ( int i = 0; i < len; i++ )
			{
				s ^= s << 13; s ^= s >> 17; s ^= s << 5;
				buf[i] += ((s & 0xFFFFFFu) / 8388608f - 1f) * p.PickNoise;
			}
		}

		float mean = 0f;
		for ( int i = 0; i < len; i++ ) mean += buf[i];
		mean /= len;
		for ( int i = 0; i < len; i++ ) buf[i] -= mean;
	}

	/// <summary>One sample at the bridge. <paramref name="pitch"/> is the pitch ratio the gesture
	/// layer asks for (1 = nominal) — the loop simply gets shorter, because that is what fretting
	/// or bending a string does to it.</summary>
	public float Next( float pitch = 1f )
	{
		float delay = Math.Clamp( _period / Math.Max( 0.25f, pitch ), 2f, _len - 2f );
		// Read the travelling wave `delay` samples back, interpolated.
		float rp = _read - delay;
		while ( rp < 0f ) rp += _len;
		int r0 = (int)rp;
		float fr = rp - r0;
		int r1 = r0 + 1 >= _len ? 0 : r0 + 1;
		float y = _buf[r0] + (_buf[r1] - _buf[r0]) * fr;

		// The terminations: a one-pole low-pass is the frequency-dependent reflection loss at the
		// bridge and the nut. It is the entire reason the partials decay at different rates.
		_lp = y * (1f - _damp) + _lp * _damp;
		_buf[(int)_read] = _lp * _g;
		if ( ++_read >= _len ) _read -= _len;
		return y;
	}
}
