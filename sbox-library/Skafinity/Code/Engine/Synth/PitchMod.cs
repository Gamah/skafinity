using System;

namespace Skafinity;

// The per-note PITCH GESTURES — vibrato, the bend-in, the scoop, and the bend — as one thing both
// the subtractive voice and the physical models ask for.
//
// It is factored out because it is KNOB-DRIVEN and the knobs do not change when the synthesis
// does. BENDINESS, VIBRATO and the expression plan (see Expression.cs) describe a player's hand,
// not an oscillator: a string bent up a tone is a string whose length changed, and a modulated
// FM carrier is the same gesture on a different sound generator. Two copies of this would mean
// BENDINESS meaning two slightly different things depending on which voice happened to be
// playing, which is the sort of drift nobody ever finds by listening.
//
// Part of the MusicGen engine — see MusicGen.cs.

/// <summary>Vibrato and the bend envelopes, evaluated at one sample of one note. Kept as two
/// separate factors rather than one product because the subtractive voice multiplies them into a
/// double phase increment in a fixed order, and folding them first would move the render digest
/// without changing the sound.</summary>
struct PitchMod
{
	public float Vib;      // 1 = on pitch
	public float BendMul;  // 1 = on pitch

	/// <summary>Combined pitch ratio, for the models that take one number.</summary>
	public float Ratio => Vib * BendMul;
}

public sealed partial class MusicGen
{
	/// <summary>Evaluate the note's pitch gestures at sample <paramref name="i"/>.
	///
	/// Every window here is in ABSOLUTE time (samples from the note's start) and every gesture
	/// RESOLVES, so a held note locks on pitch once they are done. Vibrato is delayed and ramped
	/// so short notes stay dead-on; the bend-in and the scoop start off-pitch and arrive; the
	/// bend starts on pitch and leaves. See Patch for which field is which gesture and why the
	/// bend's times are seconds rather than fractions of a note.</summary>
	PitchMod Pitch( in Patch p, int i, int dur )
	{
		var m = new PitchMod { Vib = 1f, BendMul = 1f };
		int vibDelay = (int)(0.18f * _sr);
		int vibRamp = Math.Max( 1, (int)(0.16f * _sr) );
		int scoopWin = Math.Max( 1, (int)(0.16f * _sr) );

		if ( p.Vibrato > 0f && p.VibDepth > 0f )
		{
			float ramp = MathF.Max( 0f, (i - vibDelay) / (float)vibRamp );
			if ( ramp > 1f ) ramp = 1f;
			if ( ramp > 0f )
				m.Vib = (float)(1.0 + p.VibDepth * ramp * Math.Sin( i / (double)_sr * p.Vibrato * 2 * Math.PI ));
		}

		float bendSemis = 0f;
		if ( p.BendSemis != 0f && p.BendTime > 0f )
		{
			int bt = Math.Min( dur, Math.Max( 1, (int)(p.BendTime * _sr) ) );
			if ( i < bt ) { float u = i / (float)bt; bendSemis += p.BendSemis * (1f - u * u * (3f - 2f * u)); }
		}
		if ( p.ScoopSemis != 0f && i < scoopWin )
			bendSemis += p.ScoopSemis * MathF.Sin( (float)(i / (float)Math.Min( dur, scoopWin ) * Math.PI) );
		if ( p.BendUpSemis != 0f && p.BendUpTime > 0f )
		{
			int b0 = (int)(p.BendUpStart * _sr);
			int rise = Math.Max( 1, (int)(p.BendUpTime * _sr) );
			if ( i >= b0 )
			{
				float u = Math.Min( 1f, (i - b0) / (float)rise );
				float amt = u * u * (3f - 2f * u);
				if ( p.BendUpHold > 0f )
				{
					int r0 = b0 + rise + (int)(p.BendUpHold * _sr);
					if ( i >= r0 )
					{
						float w = Math.Min( 1f, (i - r0) / (float)rise );
						amt = 1f - w * w * (3f - 2f * w);
					}
				}
				bendSemis += p.BendUpSemis * amt;
			}
		}
		if ( bendSemis != 0f ) m.BendMul = (float)Math.Pow( 2.0, bendSemis / 12.0 );
		return m;
	}
}
