using System;
using System.Collections.Generic;

namespace Skafinity;

// The backing horn section, panned across the stereo field.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Backing horns (panned spread) ──
	// Block stabs on the mask read "samey" (only eighth-note chords). A dedicated
	// stream (horn:tag, so the main composition order is unchanged) breaks them up
	// with rolling arpeggios, 16th pairs, grace pickups and varied length. Kept
	// modest — the bass got over-busy when its ornament rate ran high.
	void RenderHornStabs( int barStart, int spe, double secPerEighth, int chord, Rng orn, Rng exprRng )
	{
		int baseMidi = _rootMidi + 19;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4 };
		float spread = _c.PanAmount * 0.7f;
		int six = spe / 2;
		float ornChance = 0.18f + _c.TripletChance; // ~0.24 default; rides the same knob

		// Section expression — vibrato + a chance of a scoop/fall, rolled once per onset (below)
		// and applied to every tone of that stab so the whole section bends together.
		var ex = Expr( "HORNS" );
		Voicing hornVc = default;

		// one chord-tone voice
		void Note( int at, int dur, int k, double dec, float gain )
		{
			var horn = new Patch
			{
				Osc = 1, Voices = 3, Detune = _c.Detune,
				Amp = _c.HornVol * _c.HornBalance / degs.Length * gain, Attack = 0.008f, Decay = dec,
				Sustain = 0.2f, Sustained = false,
				Cutoff = _c.HornCutoff, CutEnv = 1200f, Reso = 1.0f,
				Drive = _c.HornDrive, Pan = spread * (k / (float)(degs.Length - 1) * 2f - 1f),
				Vibrato = _c.MelodyVibrato,
			};
			ApplyVoicing( ref horn, hornVc );
			RenderPatch( at, dur, Midi( ScaleMidi( baseMidi, degs[k] ) ), horn );
		}

		// full block chord stab
		void Stab( int at, int dur, double dec, float gain )
		{
			for ( int k = 0; k < degs.Length; k++ ) Note( at, dur, k, dec, gain );
		}

		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			if ( !_hornMask[e] ) continue;
			int at = Swung( barStart, spe, e );
			hornVc = Roll( ex, baseMidi, NoPrev, exprRng );

			if ( six > 0 && orn.Chance( ornChance ) )
			{
				float r = orn.Next();
				if ( r < 0.4f )
				{
					// rolling arpeggio: chord tones climb across a 16th-triplet
					int step = spe / 3;
					for ( int k = 0; k < degs.Length; k++ )
						Note( Swung( barStart, spe, e + (double)k / 3 ), (int)(step * 0.9f), k, secPerEighth / 3 * 0.8, 1f );
					continue;
				}
				if ( r < 0.75f )
				{
					// 16th pair: stab on the beat, softer echo on the "e"
					Stab( at, (int)(six * 0.85f), secPerEighth * 0.5 * 0.8, 1f );
					Stab( Swung( barStart, spe, e + 0.5 ), (int)(six * 0.85f), secPerEighth * 0.5 * 0.7, 0.6f );
					continue;
				}
				// grace pickup: a soft single tone just before the block stab
				Note( Swung( barStart, spe, e - 0.5 ), (int)(six * 0.8f), 0, secPerEighth * 0.5 * 0.6, 0.5f );
				Stab( at, (int)(spe * 0.6f), 0.22, 1f );
				continue;
			}

			// plain stab — length varies a touch so even straight bars aren't identical
			float lenMul = 0.45f + orn.Next() * 0.35f;
			Stab( at, (int)(spe * lenMul), 0.22, 1f );
		}
	}
}
