using System;

namespace Skafinity;

// THE AMPLIFIER — a cascade, not a clipper.
//
// What was here before was one symmetric tanh with the tone low-pass in FRONT of it, and three
// things follow from that which no amount of drive fixes:
//
//   * THE FILTER WAS IN THE WRONG PLACE. A low-pass before a non-linearity removes the harmonics
//     that would have intermodulated inside it, and then nothing tames the ones it makes. A real
//     chain is the opposite way round: the distortion happens first and a SPEAKER sits after it.
//     That ordering is most of why a driven amp sounds like a guitar and a waveshaper sounds like
//     a fuzz pedal into a wire.
//   * ONE SYMMETRIC STAGE MAKES ODD HARMONICS ONLY. tanh is an odd function, so it generates the
//     3rd, 5th, 7th and nothing between them — a hollow, synthetic kind of dirt. A triode clips
//     asymmetrically (one half of the swing meets grid conduction well before the other), so a
//     real stage makes EVEN harmonics too, and the 2nd is the one that reads as warmth and
//     thickness rather than as fizz.
//   * ONE STAGE DOES NOT COMPRESS, SO THE DISTORTION IS A TRANSIENT. The string arrives at full
//     amplitude and decays 60 dB; with a single tanh the attack is squashed flat and the sustain
//     passes through almost linearly, so a note is dirty for 30 ms and clean for the rest. That
//     is exactly backwards from a high-gain amp, where the second stage is driven into
//     saturation by the already-compressed output of the first and the note therefore stays
//     saturated — which is both where the sustained rasp comes from AND why a real amp's output
//     has a low crest factor. The missing compression is why the backing parts were sitting
//     under the mix: their peaks matched what was measured, their bodies did not.
//
// So: two gain stages with a coupling capacitor between them, then the cabinet.
//
// THE COUPLING CAPACITOR IS NOT DECORATION. Asymmetric clipping produces a DC offset. Left in,
// it walks the next stage's bias until that stage clips only one side and the sound collapses
// into a buzz. Every real cascade has a capacitor between stages for precisely this reason, and
// it is a high-pass — so this is a one-pole high-pass placed where the physics puts one.
//
// Part of the MusicGen engine — see MusicGen.cs.

/// <summary>One note's path through the amplifier. Per-note state, re-derived from the note's own
/// start like every other filter here, so a render window boundary cannot change the result (see
/// RenderPitchedRange).</summary>
struct AmpChain
{
	/// <summary>Where the DC made by asymmetric clipping is removed, in Hz. Low enough to leave
	/// the lowest note's fundamental alone — a 41 Hz bass string has to survive this — and high
	/// enough to settle within a note.</summary>
	const float CouplingHz = 18f;

	/// <summary>The tone stack between the stages. Its job is to stop the second stage being fed
	/// the first one's top octave: harmonics of harmonics are what "fizz" is, and a real amp has
	/// a filter in exactly this position to keep them out.</summary>
	const float InterstageHz = 2600f;

	/// <summary>Asymmetry of a stage, as a fraction of full swing. Small on purpose: this is a
	/// triode's operating point being pushed off centre, not a rectifier. It is what puts the 2nd
	/// harmonic in.</summary>
	const float Bias = 0.17f;

	float _g1, _g2, _makeup;
	float _cpIn, _cpOut, _cpA;   // coupling capacitor (high-pass)
	float _isLp, _isA;           // interstage tone stack (low-pass)
	float _low, _band, _f, _reso; // cabinet (Chamberlin SVF)
	float _hpIn, _hpOut, _hpA;   // the patch's own high-pass, ahead of everything
	bool _clean;

	public AmpChain( in Patch p, int sr )
	{
		// The drive knob is SPLIT ACROSS THE STAGES rather than spent on one, which is what makes
		// the cascade a cascade: sqrt each, so the product is still the number on the knob and
		// the knob's meaning does not move. At Drive 1 both stages are unity and the chain is
		// very nearly clean, so this is continuous — there is no gain setting at which a second
		// stage switches on and the tone jumps.
		float drive = MathF.Max( 1f, p.Drive );
		_g1 = _g2 = MathF.Sqrt( drive );
		_clean = drive < 1.05f;
		// Makeup. Two saturating stages bring almost any input up to the same output, so without
		// this a clean patch and a filthy one differ by 20 dB for no musical reason. Normalising
		// by what the chain does to a full-scale input leaves the LEVEL to Amp and the TONE here.
		float probe = Stage( Stage( 1f, _g1 ), _g2 );
		_makeup = probe > 1e-4f ? 1f / probe : 1f;

		_cpA = (float)(1.0 / (1.0 + 2 * Math.PI * CouplingHz / sr));
		_isA = (float)Math.Exp( -2 * Math.PI * InterstageHz / sr );
		_cpIn = _cpOut = _isLp = 0f;

		// The cabinet. A 12-inch cone in a box cannot follow the top of a distorted spectrum, so
		// it is a low-pass — and it is the LAST thing in the chain, which is the whole point of
		// this file. The patch's cutoff knob is that corner.
		_low = _band = 0f;
		_f = (float)(2 * Math.Sin( Math.PI * Math.Min( p.Cutoff <= 0f ? sr * 0.16f : p.Cutoff,
			sr * 0.16f ) / sr ));
		_reso = Math.Clamp( p.Reso, 0.2f, 2f );

		_hpA = p.Highpass > 0f ? (float)(1.0 / (1.0 + 2 * Math.PI * p.Highpass / sr)) : 0f;
		_hpIn = _hpOut = 0f;
	}

	/// <summary>One asymmetric soft-clipping stage. The bias is added before the non-linearity and
	/// taken off after, so the stage distorts off-centre without handing on a step of DC bigger
	/// than the coupling capacitor can absorb.</summary>
	static float Stage( float x, float g )
	{
		float b = MathF.Tanh( Bias );
		return (float)Math.Tanh( x * g + Bias ) - b;
	}

	public float Next( float x )
	{
		if ( _hpA > 0f )
		{
			float hp = _hpA * (_hpOut + x - _hpIn);
			_hpIn = x; _hpOut = hp; x = hp;
		}

		if ( !_clean )
		{
			x = Stage( x, _g1 );
			// Coupling capacitor: drop the DC the asymmetry just made, before it biases stage two.
			float cp = _cpA * (_cpOut + x - _cpIn);
			_cpIn = x; _cpOut = cp;
			// Tone stack, so stage two is not fed the first one's top octave.
			_isLp = cp * (1f - _isA) + _isLp * _isA;
			x = Stage( _isLp, _g2 ) * _makeup;
		}

		// Cabinet, last.
		float high = x - _low - _reso * _band;
		_band += _f * high;
		_low += _f * _band;
		return _low;
	}
}
