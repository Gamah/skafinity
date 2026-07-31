using System;
using System.Collections.Generic;

namespace Skafinity;

// Rhythm guitar: the rock/country/punk power-chord comp and the metal palm-muted gallop.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Rock rhythm guitar — twangy distorted power chords. Shares the lead guitar's voice (the
	// bright cutoff-envelope "twang" through a resonant SVF) but strums root+fifth+octave and
	// runs a LOWER base distortion than the lead so the two layer instead of mush. Downbeats
	// ring; offbeats tighten toward a palm-muted chug as RhythmGtrChug rises.
	// exprRng is unused: power chords stay dead straight (Expr("RHYTHM GTR") is default), but the
	// param keeps every instrument's call site uniform.
	void RenderRhythmGuitarBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		bool country = _genre == 2;
		int root = ChordRoot( chord ) + 12;               // chunky register, an octave up
		// Country strums a full DIATONIC triad (root/3rd/5th + octave) clean and bright — built in
		// degree space (ScaleMidi) so the 3rd follows the mode, matching the keys/lead. A hardcoded
		// major 3rd clashed on the minor chords of a progression (e.g. the vi in {0,4,5,3}). Rock
		// chunks a bare, mode-neutral power chord (root/5th/octave) with more base distortion.
		int triBase = _rootMidi + 12;                     // same register as `root`, in degree space
		int[] notes = country
			? new[] { ScaleMidi( triBase, _prog[chord] ),     ScaleMidi( triBase, _prog[chord] + 2 ),
			          ScaleMidi( triBase, _prog[chord] + 4 ), ScaleMidi( triBase, _prog[chord] + 7 ) }
			: new[] { root, root + 7, root + 12 };
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		float cutEnv = country ? 2600f : 1400f;            // brighter twang for the clean strum
		float driveAmt = country ? 0.8f + 0.3f * MathF.Max( 1f, _c.RhythmGtrDrive )
		                         : 1.5f + MathF.Max( 1f, _c.RhythmGtrDrive ); // less base than lead
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			bool accent = (e % 2) == 0;                    // downbeats ring, offbeats chug
			float lenFrac = accent ? (1f - 0.5f * chug) : (0.35f - 0.2f * chug);
			int dur = (int)(spe * Math.Max( 0.12f, lenFrac ));
			double dec = secPerEighth * (accent ? 0.8 : 0.3);
			foreach ( var m in notes )
				RenderPatch( Swung( barStart, spe, e ), dur, Midi( m ), new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance / notes.Length * (accent ? 1f : 0.7f),
					Attack = 0.002f, Decay = dec, Sustain = accent ? 0.45f : 0f, Sustained = accent,
					Cutoff = _c.RhythmGtrCutoff, CutEnv = cutEnv, Reso = 0.8f,   // twang
					Drive = driveAmt, Pan = 0f,
				} );
		}
	}

	// ── Metal rhythm guitar — palm-muted 16th-note gallop on the low root with power-chord
	// accents. The relentless 16th chug (under the double-kick) is the "fast riff" engine; the
	// downbeats and a few syncopated stabs ring a full power chord. Heavy base distortion, dark
	// and tight. rng (the rhythm stream) breaks up the accent placement so riffs vary by section.
	void RenderMetalRiffBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		int root = ChordRoot( chord );                    // low, chunky — no octave bump
		int[] power = { 0, 7, 12 };
		int six = spe / 2;
		if ( six <= 0 ) return;
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		float driveAmt = 4f + MathF.Max( 1f, _c.RhythmGtrDrive ); // heavy
		for ( int s = 0; s < EighthsPerBar * 2; s++ )     // 16 sixteenths
		{
			int at = Swung( barStart, spe, s * 0.5 );   // 16 sixteenths on the swung grid
			bool beat = s % 4 == 0;                        // quarter-note downbeats → ring a chord
			bool ring = beat || (s % 2 == 0 && rng.Chance( 0.3f )); // some offbeat eighths ring too
			int[] offs = ring ? power : new[] { 0 };       // accents = power chord, chugs = root only
			float gain = ring ? 1f : 0.6f;
			// Palm mute = short, tight; accents ring longer. Chug tightens the muted notes further.
			int dur = (int)(six * (ring ? 0.9f : Math.Max( 0.25f, 0.55f - 0.3f * chug )));
			double dec = secPerEighth * (ring ? 0.4 : 0.12);
			foreach ( var o in offs )
				RenderPatch( at, dur, Midi( root + o ), new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance / offs.Length * gain,
					Attack = 0.002f, Decay = dec, Sustain = ring ? 0.35f : 0f, Sustained = ring,
					Cutoff = _c.RhythmGtrCutoff, CutEnv = 1100f, Reso = 0.7f,
					Drive = driveAmt, Pan = 0f,
				} );
		}
	}
}
