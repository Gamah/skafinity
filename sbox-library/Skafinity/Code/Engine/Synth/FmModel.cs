using System;

namespace Skafinity;

// FM — one sine's phase modulated by another.
//
// WHY THIS IS THE RIGHT MODEL FOR A BLOWN INSTRUMENT, and not just a cheap way to get harmonics.
// A brass instrument is a standing wave in a tube driven through the player's lips, which are a
// non-linear valve. The harmonics are not filtered out of something that already had them — they
// are MADE, by that non-linearity, and how many of them exist depends on how hard it is being
// blown. Any model that starts from a spectrum and removes part of it has the causality backwards,
// which is why a lowpassed saw gets louder but not brighter and no cutoff setting fixes it.
//
// sin(2pi.fc.t + I.sin(2pi.fm.t)) expands into sidebands at fc +- k.fm with amplitudes J_k(I),
// the Bessel functions. Two properties make that a usable instrument model rather than a lucky
// noise:
//
//   * BANDWIDTH IS A PARAMETER. Significant sidebands run to about k = I + 1, so the spectrum
//     reaches roughly fc + (I+1).fm and nothing above it exists at all. So the index is not a
//     mystery dial: to sound up to the Nth harmonic, set I ~ N/ratio - 1. That is how every
//     index in this engine is chosen, and it is why they are not round numbers.
//   * fm / fc DECIDES HARMONIC OR NOT. An integer ratio puts every sideband on a harmonic of a
//     common fundamental, so the ear fuses it into one pitch. A non-integer ratio does not, and
//     the result reads as metal or glass. One number spans a trumpet and a bell.
//
// The index gets its OWN envelope, faster than the amplitude's. That is the whole gesture: a note
// blown harder is brighter, and a note dying goes dull before it goes quiet. Chowning's 1973
// brass patch is exactly this — index tracking the envelope — and it is a statement about
// non-linear valves, not a preset.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>Point a patch at the FM model, stating the BANDWIDTH rather than the index.
	///
	/// Sidebands sit at fc +- k.fm, so with the modulator at <paramref name="ratio"/> times the
	/// carrier the kth lands on harmonic 1 + k.ratio, and there are about I + 1 of them (Carson).
	/// The top harmonic is therefore 1 + ratio.(I+1), which inverts to the index below. Stating it
	/// this way is what keeps the model legible: "this instrument sounds up to its 10th harmonic"
	/// is a claim about an instrument, where an index of 8 is a claim about nothing.
	///
	/// The cutoff envelope goes, as with the string — the index envelope IS the brightness
	/// envelope here, and a filter sweeping on top of it is the thing being replaced.</summary>
	static void AsFm( ref Patch p, float ratio, float topHarmonic, float sustain, float idxSec,
		float feedback = 0f )
	{
		p.Model = Model.Fm;
		p.FmRatio = ratio;
		p.FmIndex = MathF.Max( 0.2f, (topHarmonic - 1f) / ratio - 1f );
		p.FmIndexSus = sustain; p.FmIndexSec = idxSec; p.FmFeedback = feedback;
		p.CutEnv = 0f;
	}
}

/// <summary>A two-operator FM voice: carrier, modulator, and an index envelope. Optional carrier
/// feedback, which drives the spectrum toward a saw and is how a reed's harder, buzzier tone is
/// reached without a second modulator.</summary>
struct FmOp
{
	readonly double _incC;      // carrier phase increment at nominal pitch
	readonly double _incM;
	readonly float _index;      // peak modulation index
	readonly float _sustain;    // index held after the attack transient
	readonly float _fb;
	readonly double _decStep;   // per-sample index decay
	double _pc, _pm;
	double _idx;                // running index envelope, 1 -> 0
	float _prev;                // carrier feedback state

	/// <param name="voice">Index within a unison. Each player of a section starts its modulator
	/// at a different phase: three operators started together are one operator three times as
	/// loud until the detune pulls them apart, which is an audible swell on every attack.</param>
	public FmOp( float freq, in Patch p, int sr, int voice = 0 )
	{
		_incC = freq / sr;
		_incM = freq * Math.Max( 0.01f, p.FmRatio ) / sr;
		_index = p.FmIndex;
		_sustain = Math.Clamp( p.FmIndexSus, 0f, 1f );
		_fb = Math.Clamp( p.FmFeedback, 0f, 0.9f );
		_decStep = Math.Exp( -1.0 / Math.Max( 1.0, p.FmIndexSec * sr ) );
		_pc = p.PhaseSeed;
		_pm = voice * 0.37;   // irrational-ish spacing, so two voices never start in phase
		_idx = 1.0;
		_prev = 0f;
	}

	/// <summary>One sample. <paramref name="pitch"/> is the gesture layer's pitch ratio — both
	/// operators scale together, which is what keeps the ratio (and therefore the timbre) fixed
	/// while the note bends.</summary>
	public float Next( float pitch = 1f )
	{
		// The index relaxes from its peak to its sustain: the attack transient of a blown note.
		float I = _index * (_sustain + (1f - _sustain) * (float)_idx);
		_idx *= _decStep;

		float m = MathF.Sin( (float)(_pm * 2 * Math.PI) );
		float y = MathF.Sin( (float)(_pc * 2 * Math.PI + I * m + _fb * _prev) );
		_prev = y;

		_pc += _incC * pitch; if ( _pc >= 1.0 ) _pc -= Math.Floor( _pc );
		_pm += _incM * pitch; if ( _pm >= 1.0 ) _pm -= Math.Floor( _pm );
		return y;
	}
}
