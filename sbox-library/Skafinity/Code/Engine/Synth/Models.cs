using System;

using static Skafinity.Osc;

namespace Skafinity;

// THE PHYSICAL MODELS — the render paths that are not "a waveform through a low-pass".
//
// The subtractive voice in Render.cs describes a sound by SUBTRACTION: start from a spectrum that
// is already everything (a saw) and take the top off. Three things follow from that and no
// parameter fixes them, which is why the whole band read as settings of one synth:
//
//   * ONE ENVELOPE FOR THE WHOLE SPECTRUM. Every harmonic of a saw decays at the same rate,
//     because there is one amplitude envelope multiplying all of them. No real body does this —
//     losses grow with frequency, so the 12th partial of anything is gone long before the 1st.
//   * THE SPECTRUM IS FIXED BY CONSTRUCTION. A saw is 1/n for ever. The only motion available is
//     a filter sweeping across it, which the ear hears as a filter, because it is one.
//   * EVERY NOTE IS THE SAME NOTE. The oscillator starts at a known phase with known content, so
//     two notes an octave apart are the same sound transposed — which is exactly the property
//     that makes a chip synth identifiable.
//
// Each model below is a different ANSWER to "what is producing the sound", and gets the above
// for free rather than by parameterisation:
//
//   Model.String — a delay line and a loss filter, which together are the travelling-wave
//                  solution of the wave equation on a string. Per-partial decay is the loss
//                  filter's response; pluck position and pick width are the initial condition.
//   Model.Fm     — one sine's phase modulated by another. The spectrum is Bessel-weighted
//                  sidebands whose COUNT is a parameter, so brightness is generated rather than
//                  filtered away, and it can move while the pitch does not.
//   Model.Modal  — a sum of decaying sinusoids at a body's own eigenfrequencies, each with its
//                  own decay. The most direct statement of "what a struck object does", and the
//                  method this engine already uses for the cymbals (see CymbalBands).
//
// The three share the chain AFTER the source — amp, the body/amp filter, drive, pan — because
// that part was never the problem. See RenderModelEvent.
//
// Part of the MusicGen engine — see MusicGen.cs.

/// <summary>Which synthesis method renders a <see cref="Patch"/>. 0 keeps the subtractive voice,
/// so a patch that never mentions this is byte-for-byte what it always was.</summary>
static class Model
{
	public const int Subtractive = 0;
	public const int String = 1;
	public const int Fm = 2;
	public const int Modal = 3;
}

/// <summary>Per-WINDOW scratch for the pitched render. One instance is owned by each
/// <c>RenderPitchedRange</c> window — the windows run on different threads and synthesis must stay
/// a pure function of the event, so nothing here may be shared or carried between notes.
/// Allocated once per window rather than per note: a string's delay line is 16 KB and there are
/// thousands of notes in a song.</summary>
sealed class SynthScratch
{
	/// <summary>Unison oscillator phase / increment (subtractive voice).</summary>
	public readonly double[] Ph = new double[8];
	public readonly double[] Inc = new double[8];

	/// <summary>The string's delay line, and the pluck comb's. 4096 samples is a full period at
	/// 10.8 Hz / 44.1 kHz — an octave below anything playable.</summary>
	public readonly float[] Delay = new float[4096];

	/// <summary>Working copy for the excitation's moving average — the pick-width convolution is
	/// circular, so it cannot be done in place.</summary>
	public readonly float[] Work = new float[4096];

	/// <summary>Modal bank state: one decaying complex rotation per partial.</summary>
	public readonly float[] ModRe = new float[ModalBank.MaxPartials];
	public readonly float[] ModIm = new float[ModalBank.MaxPartials];
	public readonly float[] ModCos = new float[ModalBank.MaxPartials];
	public readonly float[] ModSin = new float[ModalBank.MaxPartials];
	public readonly float[] ModW = new float[ModalBank.MaxPartials];
	public readonly float[] ModDec = new float[ModalBank.MaxPartials];
}

public sealed partial class MusicGen
{
	/// <summary>Render one note through a physical model. Mirrors <c>RenderEvent</c>'s contract
	/// exactly — computes from the note's own start because none of this state can be resumed
	/// mid-stream, writes only inside <c>[clipFrom, clipTo)</c>, and stops past it.
	///
	/// THE ENVELOPE IS NOT AN ENVELOPE HERE. A plucked string and a struck bar decay because they
	/// are losing energy, which the model already computes; multiplying that by an exponential
	/// would be describing the same decay twice and is how a physical model ends up sounding
	/// synthetic again. So the amp stage applies the ATTACK (which is the excitation's rise, and
	/// is genuinely separate) and the RELEASE taper (which is the note being damped by the hand,
	/// also genuinely separate) and nothing in between. FM is the exception: its carrier does not
	/// decay on its own, so it keeps the subtractive envelope.</summary>
	void RenderModelEvent( in NoteEvent ev, int clipFrom, int clipTo, SynthScratch sc )
	{
		int start = ev.Start, dur = ev.Dur;
		var p = ev.P;
		StereoGains( p.Pan, out float gL, out float gR );
		int atk = Math.Max( 1, (int)(p.Attack * _sr) );
		int rel = Math.Max( 1, (int)(0.006f * _sr) );
		int relStart = dur - rel;
		int end = Math.Min( Math.Min( _bufL.Length, start + dur ), clipTo );

		var str = default( PluckedString );
		var mod = default( ModalBank );
		// UNISON SURVIVES ON THE FM PATH and nowhere else, because of what it means in each case.
		// A horn SECTION is three players on the same line, each a few cents off — three sound
		// sources, which is what Voices/Detune has always described, and dropping it would thin
		// the section into one player. A guitar's "unison" is not that: it is double-tracking, two
		// takes of one player, and the engine already models it one level up by emitting two
		// events (see RenderPatch). Three detuned delay lines would be three strings sounding the
		// same note on one guitar, which is not a thing.
		int uni = p.Model == Model.Fm ? Math.Clamp( p.Voices, 1, 4 ) : 1;
		Span<FmOp> fm = stackalloc FmOp[4];
		switch ( p.Model )
		{
			case Model.String: str = new PluckedString( ev.Freq, p, _sr, sc ); break;
			case Model.Fm:
				for ( int v = 0; v < uni; v++ )
				{
					float cents = uni == 1 ? 0f : (v - (uni - 1) * 0.5f) * p.Detune;
					fm[v] = new FmOp( ev.Freq * (float)Math.Pow( 2, cents / 1200.0 ), p, _sr, v );
				}
				break;
			default:
				mod = p.ModalSustain ? ModalBank.Sustained( ev.Freq, p, _sr, sc )
					: new ModalBank( ev.Freq, p, _sr, sc );
				break;
		}

		// The amplifier the instrument is played through — cascaded asymmetric stages, a coupling
		// capacitor, and the speaker cabinet LAST. See Amp.cs for why that order is the whole
		// difference between a driven amp and a waveshaper, and why one symmetric tanh could only
		// ever dirty the attack of a note.
		//
		// The model voices set CutEnv to 0 (see AsString/AsFm/AsModal), so the cabinet does not
		// sweep: a speaker's corner is a property of the cone, not of how long ago the note was
		// struck. The brightness that used to come from a cutoff envelope now comes from the
		// string losing its upper partials, which is where it comes from on a guitar.
		var amp = new AmpChain( p, _sr );
		// FM alone keeps the amplitude envelope — see the summary above.
		bool ownDecay = p.Model != Model.Fm;
		double decStep = Math.Exp( -1.0 / Math.Max( 1.0, p.Decay * _sr ) );
		double ampDecay = 1.0;

		for ( int i = 0; start + i < end; i++ )
		{
			float env = i < atk ? (float)i / atk : 1f;
			if ( !ownDecay && i >= atk )
			{
				float d = (float)ampDecay;
				env = p.Sustained ? p.Sustain + (1f - p.Sustain) * d : d;
				ampDecay *= decStep;
			}
			if ( i >= relStart ) env *= Math.Max( 0f, (float)(dur - i) / rel );

			// The same gesture layer the subtractive voice uses, so BENDINESS and VIBRATO are one
			// mechanism across the band. A bend on a string shortens the delay line; on an FM pair
			// it scales both operators, keeping the ratio and therefore the timbre; the modal bank
			// retunes its rotations. Three different physical readings of one knob.
			float ratio = Pitch( p, i, dur ).Ratio;
			float s;
			if ( p.Model == Model.Fm )
			{
				s = 0f;
				for ( int v = 0; v < uni; v++ ) s += fm[v].Next( ratio );
				s /= uni;
			}
			else s = p.Model == Model.String ? str.Next( ratio ) : mod.Next( ratio );

			float val = amp.Next( s ) * env * p.Amp;
			int idx = start + i;
			if ( idx >= clipFrom )
			{
				_bufL[idx] += val * gL;
				_bufR[idx] += val * gR;
			}
		}
	}
}
