using System;
using System.Collections.Generic;

namespace Skafinity;

// Bass.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Bass ──
	void RenderBassBar( int barStart, int spe, double secPerEighth, int chord, int nextChord, Rng rng, Rng bassOrn, Rng exprRng )
	{
		int root = ChordRoot( chord );
		var ex = Expr( "BASS" );
		int prevMidi = NoPrev;
		// Bass has its own ornament knob (BASS TRIPLETS), nudged up a touch by overall
		// kit busyness so a busy vibe gets a busier bass.
		float ornChance = _c.BassTriplets * 0.5f + _c.DrumBusy * 0.05f;
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			int off = _bassPat[e];
			if ( off == Rest ) continue;

			int midi;
			if ( off == Approach )
			{
				int target = ChordRoot( nextChord );
				midi = target - (rng.Chance( 0.5f ) ? 1 : 2); // chromatic/step lead-in
			}
			else
			{
				midi = root + off;
				if ( off == 0 && e > 0 && rng.Chance( _c.OctavePopChance ) ) midi += 12;
			}

			// note runs until the next onset (legato reggae feel)
			int len = 1;
			while ( e + len < EighthsPerBar && _bassPat[e + len] == Rest ) len++;

			// Chop a standalone (non-sustaining) note into a 16th pair or 16th-note
			// triplet so the line reads "long long short short" / "long short long long"
			// instead of even eighths. Driven by a dedicated stream, so the main
			// composition RNG order — and every existing song — is left unchanged.
			var vc = Roll( ex, midi, prevMidi, exprRng );
			prevMidi = midi;

			if ( off != Approach && len == 1 && bassOrn.Chance( ornChance ) )
			{
				int n = bassOrn.Chance( 0.65f ) ? 2 : 3;        // 16th pair / 16th triplet
				int step = spe / n;
				int[] moves = { 0, 7, 12 };                     // root / fifth / octave
				for ( int k = 0; k < n; k++ )
				{
					int bm = midi + (k == 0 ? 0 : moves[bassOrn.Int( moves.Length )]);
					EmitBass( Swung( barStart, spe, e + (double)k / n ), (int)(step * 0.9f), bm, secPerEighth / n * 0.8, vc );
				}
				continue;
			}

			EmitBass( Swung( barStart, spe, e ), (int)(spe * len * 0.95f), midi, secPerEighth * len * 0.8, vc );
		}
	}

	void EmitBass( int at, int dur, int midi, double decaySec, in Voicing vc )
	{
		// Triangle body for a round, deep reggae/dub bass (saw alone read as too
		// buzzy) — but triangle alone was too subtle, so layer a quieter square
		// underneath for presence/definition. The square's odd harmonics give the
		// bass its bite; both share the bass low-pass so the tone stays warm.
		var body = new Patch
		{
			Osc = 3, Voices = 2, Detune = _c.Detune * 0.4f,
			Amp = _c.BassVol * _c.BassBalance, Attack = 0.004f, Decay = decaySec,
			Sustain = 0.55f, Sustained = true,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		};
		var sub = new Patch
		{
			Osc = 2, Voices = 1, Detune = 0f,
			Amp = _c.BassVol * 0.4f * _c.BassBalance, Attack = 0.004f, Decay = decaySec,
			Sustain = 0.55f, Sustained = true,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		};
		ApplyVoicing( ref body, vc ); ApplyVoicing( ref sub, vc );
		RenderPatch( at, dur, Midi( midi ), body, mono: true );
		RenderPatch( at, dur, Midi( midi ), sub, mono: true );
	}
}
