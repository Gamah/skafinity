using System;
using System.Collections.Generic;

namespace Skafinity;

// The ska skank guitar (the signature offbeat chop) and the reggae organ bubble.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Skank guitar (the signature) + reggae organ bubble — offbeats, centered ──
	void RenderRhythmBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		// +24: skank/organ sit an octave above the old register — at +12 (E2..B2) the
		// chop was too low/muddy to cut through and read as missing. Organ stays a
		// further octave down via the -12 below.
		int gBase = _rootMidi + 24;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 7 };
		// Skank is dead straight (default); the organ bubble gets a gentle vibrato depth.
		var organVc = Roll( Expr( "ORGAN" ), 0, NoPrev, exprRng );

		for ( int e = 1; e < EighthsPerBar; e += 2 ) // offbeats
		{
			int at = Swung( barStart, spe, e );   // odd e = offbeat → Swung pushes it late by _swing

			// bright, thin, short guitar chop
			foreach ( var d in degs )
				RenderPatch( at, (int)(spe * Math.Clamp( _c.SkankChop, 0.15f, 1f )), Midi( ScaleMidi( gBase, d ) ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune,
					Amp = _c.SkankVol * _c.SkankBalance / degs.Length, Attack = 0.002f, Decay = 0.10,
					Sustain = 0f, Sustained = false,
					Cutoff = _c.SkankCutoff, CutEnv = 1500f, Reso = 0.8f,
					Highpass = _c.SkankHighpass, Drive = _c.SkankDrive, Pan = 0f,
				} );

			// reggae organ "bubble": a softer, rounder offbeat under the guitar
			if ( _organBubble )
				foreach ( var d in degs )
				{
					var organ = new Patch
					{
						Osc = 0, Voices = 2, Detune = _c.Detune * 0.5f,
						Amp = _c.OrganVol * _c.OrganBalance / degs.Length, Attack = 0.004f, Decay = 0.16,
						Sustain = 0.3f, Sustained = false,
						Cutoff = _c.OrganCutoff, CutEnv = 0f, Reso = 1.0f, Drive = 1.1f, Pan = 0f,
						Vibrato = _c.OrganVibrato,
					};
					ApplyVoicing( ref organ, organVc );
					RenderPatch( at, (int)(spe * 0.55f), Midi( ScaleMidi( gBase, d ) - 12 ), organ );
				}
		}
	}
}
