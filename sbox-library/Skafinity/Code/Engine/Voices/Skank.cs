using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The ska skank guitar (the signature offbeat chop) and the reggae organ bubble.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>The chop's voice. Named rather than inline so the tone audition plays THIS
	/// definition and not a copy of it.</summary>
	/// <summary>How long the chopped string would ring IF THE HAND LET IT.
	///
	/// This is deliberately LONGER than the chop, and that is the whole subtlety of modelling a
	/// damped instrument. The skank's length is a performance decision — it is the SKANK CHOP knob,
	/// a fraction of an eighth, and the note is cut there. So the string's own decay must outlast
	/// that cut, or the string dies first and the knob stops doing anything: at 160 bpm the chop is
	/// about 95 ms, so a 70 ms T60 is already silent before the hand moves, and SKANK CHOP would
	/// control nothing but a trailing silence. Set past the longest chop, the model supplies the
	/// TIMBRE and the knob supplies the LENGTH, which is the correct division of labour between an
	/// instrument and the person playing it.</summary>
	const float SkankRing = 0.42f;

	internal Patch SkankPatch( float vel, int tones )
	{
		var p = new Patch
		{
			Osc = 1, Voices = 3, Detune = _c.Detune,
			Amp = _c.SkankVol * _c.SkankBalance * _midMul / tones
				* NoteGain( vel ) * _compTrim,
			Attack = 0.002f, Decay = 0.10,
			Sustain = 0f, Sustained = false,
			Cutoff = _c.SkankCutoff, CutEnv = 1500f, Reso = 0.8f,
			Highpass = _c.SkankHighpass, Drive = _c.SkankDrive, Pan = 0f,
		};
		// Struck a third of the way up with a plectrum, lightly damped — a chord being chopped
		// rather than a note being sustained.
		AsString( ref p, SkankRing, 0.38f, 0.30f, 0.030f, 0.05f );
		return p;
	}

	/// <summary>The bubble's voice, under the chop. Named for the same reason as
	/// <see cref="SkankPatch"/>.</summary>
	internal Patch OrganBubblePatch( float vel, int tones )
	{
		var p = new Patch
		{
			Osc = 0, Voices = 2, Detune = _c.Detune * 0.5f,
			Amp = _c.OrganVol * _c.OrganBalance * _midMul / tones
				* NoteGain( vel ) * _compTrim,
			Attack = 0.004f, Decay = 0.16,
			Sustain = 0.3f, Sustained = false,
			Cutoff = _c.OrganCutoff, CutEnv = 0f, Reso = 1.0f, Drive = 1.1f, Pan = 0f,
			Vibrato = _c.OrganVibrato,
		};
		// Four tone wheels, which is a dark registration — the bubble sits UNDER the chop and is
		// felt more than heard, so the upper drawbars would only fight the guitar.
		AsModal( ref p, ModalBank.Harmonic, 4, 1f, sustain: true );
		return p;
	}

	// ── Skank guitar (the signature) + reggae organ bubble — offbeats, centered ──
	// The chop lands where the figure says (CompFigure.SkaPunk), which is normally every offbeat but
	// may be a two-bar figure that pushes into the next bar. The rocksteady 7th/9th voicings the
	// song drew are what make the chop read as ska rather than as a bright rock stab.
	void RenderSkankBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		// +24: skank/organ sit an octave above the bass register — at +12 the chop was too
		// low/muddy to cut through. The organ stays a further octave down via the -12 below.
		int gBase = Register( 2 );
		var tones = ChordMidis( gBase, chord, _barTick );
		// Skank is dead straight (default); the organ bubble gets a gentle vibrato depth.
		var organVc = Roll( Expr( "ORGAN" ), 0, NoPrev, exprRng );

		foreach ( var h in hits )
		{
			int at = _time.TickToSample( h.Tick );
			int chop = _time.SpanSamples( h.Tick,
				Timing.TicksPerEighth * Math.Clamp( _c.SkankChop, 0.15f, 1f ) );

			// bright, thin, short guitar chop
			foreach ( var m in tones )
				RenderPatch( at, chop, Midi( m ), SkankPatch( h.Vel, tones.Length ) );

			// reggae organ "bubble": a softer, rounder offbeat under the guitar
			if ( !_organBubble ) continue;
			foreach ( var m in tones )
			{
				var organ = OrganBubblePatch( h.Vel, tones.Length );
				ApplyVoicing( ref organ, organVc );
				RenderPatch( at, (int)(chop * 1.1f), Midi( m - 12 ), organ );
			}
		}
	}
}
