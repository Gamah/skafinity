using System;
using System.Collections.Generic;

namespace Skafinity;

// Rock keys — their own syncopated comp, not a double of the guitar.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Rock KEYS — their OWN part, not a double of the guitar. A syncopated organ comp (the
	// "1, &-of-2, 3, &-of-4" Charleston push) playing diatonic TRIADS (root/3rd/5th) in a high
	// keyboard register. Different notes (a real triad vs the guitar's bare power chord) AND a
	// different rhythm (a 4-hit syncopation vs the guitar's every-eighth chug), so the two
	// interlock instead of playing in lockstep. KeysChug rings the chords (0) or tightens them
	// toward short stabs (1).
	static readonly int[] KeysOnsets = { 0, 3, 4, 7 };    // eighth positions of the comp's hits
	void RenderKeysBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		int kBase = _rootMidi + 24;                        // keyboard register, an octave over the rhythm guitar
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4 };  // diatonic triad
		float chug = Math.Clamp( _c.KeysChug, 0f, 1f );
		// Country reads this comp as a honky-tonk piano and pop as a clean bright synth: keep both
		// clean (rock drives it dirty).
		float keysDrive = _genre == 2 || _genre == 5 ? 1f + 0.2f * MathF.Max( 1f, _c.KeysDrive )
		                              : MathF.Max( 1f, _c.KeysDrive );
		var keysVc = Roll( Expr( "KEYS" ), 0, NoPrev, exprRng ); // gentle vibrato only
		for ( int oi = 0; oi < KeysOnsets.Length; oi++ )
		{
			int e = KeysOnsets[oi];
			int nextE = oi + 1 < KeysOnsets.Length ? KeysOnsets[oi + 1] : EighthsPerBar; // ring up to the next hit
			int gap = nextE - e;
			bool ring = chug < 0.5f;
			int dur = (int)(gap * spe * Math.Max( 0.25f, 1f - 0.7f * chug ));
			double dec = secPerEighth * gap * (ring ? 0.9 : 0.4);
			foreach ( var d in degs )
			{
				var keys = new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.KeysVol * _c.KeysBalance / degs.Length,
					Attack = 0.004f, Decay = dec, Sustain = ring ? 0.6f : 0.2f, Sustained = ring,
					Cutoff = _c.KeysCutoff, CutEnv = 250f, Reso = 1.0f,
					Drive = keysDrive, Pan = 0f,
				};
				ApplyVoicing( ref keys, keysVc );
				RenderPatch( Swung( barStart, spe, e ), dur, Midi( ScaleMidi( kBase, d ) ), keys );
			}
		}
	}
}
