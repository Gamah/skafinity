using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The ska skank guitar (the signature offbeat chop) and the reggae organ bubble.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Skank guitar (the signature) + reggae organ bubble — offbeats, centered ──
	// The chop lands where the figure says (CompFigure.Ska), which is normally every offbeat but
	// may be a two-bar figure that pushes into the next bar. The rocksteady 7th/9th voicings the
	// song drew are what make the chop read as ska rather than as a bright rock stab.
	void RenderSkankBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		// +24: skank/organ sit an octave above the bass register — at +12 the chop was too
		// low/muddy to cut through. The organ stays a further octave down via the -12 below.
		int gBase = _rootMidi + _keyShift + 24;
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
				RenderPatch( at, chop, Midi( m ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune,
					Amp = _c.SkankVol * _c.SkankBalance * _midMul / tones.Length * NoteGain( h.Tick, h.Vel ),
					Attack = 0.002f, Decay = 0.10,
					Sustain = 0f, Sustained = false,
					Cutoff = _c.SkankCutoff, CutEnv = 1500f, Reso = 0.8f,
					Highpass = _c.SkankHighpass, Drive = _c.SkankDrive, Pan = 0f,
				} );

			// reggae organ "bubble": a softer, rounder offbeat under the guitar
			if ( !_organBubble ) continue;
			foreach ( var m in tones )
			{
				var organ = new Patch
				{
					Osc = 0, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.OrganVol * _c.OrganBalance * _midMul / tones.Length * NoteGain( h.Tick, h.Vel ),
					Attack = 0.004f, Decay = 0.16,
					Sustain = 0.3f, Sustained = false,
					Cutoff = _c.OrganCutoff, CutEnv = 0f, Reso = 1.0f, Drive = 1.1f, Pan = 0f,
					Vibrato = _c.OrganVibrato,
				};
				ApplyVoicing( ref organ, organVc );
				RenderPatch( at, (int)(chop * 1.1f), Midi( m - 12 ), organ );
			}
		}
	}
}
