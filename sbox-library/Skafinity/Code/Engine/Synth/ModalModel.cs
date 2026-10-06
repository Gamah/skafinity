using System;

namespace Skafinity;

// MODAL SYNTHESIS — a body stated as its own eigenfrequencies.
//
// WHY THIS IS NOT "ADDITIVE SYNTHESIS WITH EXTRA STEPS". Any linear vibrating body has normal
// modes: shapes that oscillate at a single frequency and do not exchange energy with each other.
// Strike it and you excite a set of them, each then decaying at its own rate, and the sound is
// their sum — not approximately, exactly, for as long as the body stays linear. So a sum of
// decaying sinusoids is not a model OF a struck object, it IS one, and the only question is where
// the frequencies and the decay rates come from. Getting those from a measurement or from the
// boundary-value problem is what separates this from drawing a spectrum by taste. This engine
// already does it for the cymbals (see CymbalBands) — this is the same method for the voices that
// are struck metal or a tone wheel rather than a plucked string.
//
// Three families, three different physics, no tables:
//
//   HARMONIC — f_n = n.f0. A tone wheel is a magnetised gear spinning past a pickup: one wheel is
//     one sine, and a Hammond's sound is a handful of wheels at whole-number ratios mixed by the
//     drawbars. There is literally nothing else in it, so an organ is the one instrument where
//     additive synthesis is not a model at all but a schematic.
//
//   STIFF STRING — f_n = n.f0.sqrt(1 + B.n^2). A real string resists bending as well as
//     stretching, which adds a fourth-order term to the wave equation and pulls the upper
//     partials SHARP. B is the inharmonicity coefficient and it is why a piano's top octave is
//     tuned the way it is, and why a sampled piano played back a fifth down stops sounding like
//     one. B ~ 1e-4 is a long string, ~1e-3 a short thick one.
//
//   FREE-FREE BAR — f_n proportional to the squares of the roots of cos(b).cosh(b) = 1: 4.7300,
//     7.8532, 10.9956, 14.1372, then asymptotically (n - 1/2).pi. Squared and divided by the
//     first, that is 1, 2.76, 5.40, 8.93 ... — a tine, a glockenspiel, a struck metal tongue.
//     Nothing in it is a whole-number multiple of anything else, so the ear finds no harmonic
//     series to fuse and hears the upper modes as a separate bright ping over a pitch. See
//     Osc.BarModes, which carries the first four exactly.
//
// DECAY RATES ARE NOT FREE EITHER. Damping — radiation into air, and internal friction — grows
// with frequency, so T_n falls as the mode number rises. The law used here is T_n = T0 / r_n^Power
// with Power near 1: one number for how long the body rings, and the spread across the modes
// follows. A flat set of decays is the giveaway of a drawn spectrum; it makes a struck bar sound
// like a chord of sine waves, which is what it then is.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>Point a patch at the modal bank. A SUSTAINED bank ignores the decay, because a
	/// tone wheel does not decay — the note ends when the key is released and not before.
	/// </summary>
	static void AsModal( ref Patch p, int set, int count, float decay, float inharm = 0f,
		bool sustain = false )
	{
		p.Model = Model.Modal;
		p.ModalSet = set; p.ModalCount = count; p.ModalDecay = decay;
		p.ModalInharm = inharm; p.ModalSustain = sustain;
		p.CutEnv = 0f;
	}
}

/// <summary>A bank of decaying modes. Each is advanced as a complex rotation whose magnitude is
/// the per-sample decay — four multiplies a mode, no transcendental in the inner loop, and
/// unconditionally stable because the rotation contracts.</summary>
struct ModalBank
{
	public const int MaxPartials = 24;

	/// <summary>Mode families. Which physics the frequencies come from.</summary>
	public const int Harmonic = 0;   // tone wheels / drawbars
	public const int StiffString = 1;
	public const int Bar = 2;        // free-free, the tine

	/// <summary>How fast the higher modes die relative to the fundamental: T_n = T0 / r_n^Power.
	/// 1.0 is radiation-dominated damping growing linearly with frequency, which is what a thin
	/// metal bar in air measures closest to.</summary>
	const float DampPower = 1.0f;

	/// <summary>Samples between retunes while a pitch gesture is moving — 32 at 44.1 kHz is 0.7 ms,
	/// comfortably inside the shortest gesture the expression layer can ask for. A power of two so
	/// the counter is a mask.</summary>
	const int BlockLen = 32;

	readonly float[] _re, _im, _cos, _sin;
	readonly float[] _w;     // each mode's nominal angular frequency, for retuning
	readonly float[] _dec;   // each mode's per-sample decay, kept apart from the rotation
	readonly int _n;
	float _pitch;
	int _block;

	public ModalBank( float freq, in Patch p, int sr, SynthScratch sc )
	{
		_re = sc.ModRe; _im = sc.ModIm; _cos = sc.ModCos; _sin = sc.ModSin;
		_w = sc.ModW; _dec = sc.ModDec;
		_pitch = 1f; _block = 0;
		_n = Math.Clamp( p.ModalCount > 0 ? p.ModalCount : 8, 1, MaxPartials );
		float t0 = Math.Max( 0.01f, p.ModalDecay );
		float b = Math.Max( 0f, p.ModalInharm );
		float nyq = sr * 0.45f;
		int kept = 0;
		// NORMALISED TO UNIT PEAK. The modes all start in phase, so the bank's first sample is the
		// SUM of their amplitudes — about 1.7 for eight modes at 1/r, where an oscillator would
		// have handed on 1.0. That is not a level problem, it is a TIMBRE problem: the drive stage
		// after this is a tanh, so 1.7 into it clips hard, squares off the fundamental and
		// generates harmonics of its own — which is to say it deletes the inharmonicity that was
		// the entire reason to use modes. Normalising here is what makes the drive knob mean the
		// same thing on every synthesis method.
		float norm = 0f;
		for ( int k = 0; k < _n; k++ ) norm += 1f / Ratio( p.ModalSet, k, b );
		norm = norm > 0f ? 1f / norm : 1f;

		for ( int k = 0; k < _n; k++ )
		{
			float r = Ratio( p.ModalSet, k, b );
			float f = freq * r;
			// A mode above Nyquist does not exist; writing one in aliases it down to an audible
			// frequency that has nothing to do with the instrument. Stop at the first one, because
			// the ratios are monotone.
			if ( f >= nyq ) break;

			// Amplitude: 1/r. The strike delivers a fixed impulse, and a stiffer (higher) mode
			// takes up less of it — the same reason the 1/n^2 of a pluck falls out of a triangle.
			float amp = norm / r;
			float dec = (float)Math.Exp( -1.0 / (t0 / MathF.Pow( r, DampPower ) * sr) );
			double w = 2 * Math.PI * f / sr;
			_re[kept] = amp; _im[kept] = 0f;
			_w[kept] = (float)w; _dec[kept] = dec;
			_cos[kept] = (float)(Math.Cos( w ) * dec);
			_sin[kept] = (float)(Math.Sin( w ) * dec);
			kept++;
		}
		_n = Math.Max( 1, kept );
		if ( kept == 0 ) { _re[0] = 0f; _im[0] = 0f; _cos[0] = 1f; _sin[0] = 0f; }
	}

	/// <summary>Re-derive every mode's rotation at a new pitch ratio, leaving the decay alone — the
	/// body's losses do not change because a note was bent.</summary>
	void Retune( float pitch )
	{
		_pitch = pitch;
		for ( int k = 0; k < _n; k++ )
		{
			double w = _w[k] * pitch;
			_cos[k] = (float)(Math.Cos( w ) * _dec[k]);
			_sin[k] = (float)(Math.Sin( w ) * _dec[k]);
		}
	}

	/// <summary>Frequency of mode k as a ratio to the first, per family.</summary>
	static float Ratio( int set, int k, float b )
	{
		int n = k + 1;
		switch ( set )
		{
			case StiffString:
				return n * MathF.Sqrt( 1f + b * n * n );
			case Bar:
			{
				// The exact roots for the first four modes, then the asymptote. The roots are
				// spaced by pi and the nth is (n + 1/2).pi — 1.5pi = 4.712 against the exact
				// 4.7300, and already within 0.01% by the fourth, so the two agree where they meet
				// instead of repeating a mode.
				float[] modes = Osc.BarModes;
				if ( k < modes.Length ) return modes[k];
				float beta = (float)((n + 0.5) * Math.PI);
				const float First = 4.7300408f;
				return beta * beta / (First * First);
			}
			default:
				return n;
		}
	}

	/// <summary>One sample: the sum of every mode's current displacement. The rotation carries the
	/// decay in its magnitude, so there is no envelope multiply and each mode genuinely has its
	/// own decay rather than sharing one.
	///
	/// A pitch gesture retunes every rotation, which costs a sin/cos pair per mode — so it is done
	/// at BLOCK rate and only while the gesture is actually moving. The modes are being asked to
	/// shift by a few cents over tens of milliseconds; resolving that to the sample is spending a
	/// transcendental per mode per sample to be inaudibly more correct than resolving it to the
	/// third of a millisecond.</summary>
	public float Next( float pitch = 1f )
	{
		if ( pitch != _pitch && (_block = (_block + 1) & (BlockLen - 1)) == 0 ) Retune( pitch );
		float s = 0f;
		for ( int k = 0; k < _n; k++ )
		{
			float re = _re[k], im = _im[k], c = _cos[k], sn = _sin[k];
			_re[k] = re * c - im * sn;
			_im[k] = re * sn + im * c;
			s += _re[k];
		}
		return s;
	}

	/// <summary>A SUSTAINED bank — the tone-wheel case. An organ does not decay: the wheels keep
	/// turning, so every mode's magnitude stays 1 and the "modal decay" is meaningless. Built
	/// separately rather than as a decay of infinity so the distinction is visible in the voice
	/// that asks for it.</summary>
	public static ModalBank Sustained( float freq, in Patch p, int sr, SynthScratch sc )
	{
		var m = new ModalBank( freq, p, sr, sc );
		for ( int k = 0; k < m._n; k++ )
		{
			m._dec[k] = 1f;
			m._cos[k] = (float)Math.Cos( m._w[k] );
			m._sin[k] = (float)Math.Sin( m._w[k] );
		}
		return m;
	}
}
