using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The backing horn section, panned across the stereo field.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Backing horns (panned spread) ──
	// The figure is a TWO-BAR call and response (see MusicGen.HornFigure): the section states a
	// line and then answers it, which is what a horn section does and what a single reused
	// eight-slot mask could never express. On top of that a dedicated stream breaks the stabs up
	// with rolling arpeggios, 16th pairs and grace pickups so even a repeated call is played
	// slightly differently.
	void RenderHornStabs( int barTick, int barTicks, int chord, Rng orn, Rng exprRng )
	{
		int spe = _time.Spe;
		int baseMidi = _rootMidi + _keyShift + 19;
		var degs = ChordDegrees( chord );
		float spread = _c.PanAmount * 0.7f;
		float sectionGain = TuneFor( _sectionType ) != null ? 0.7f : 1f;
		int six = spe / 2;
		float ornChance = 0.18f + _c.TripletChance; // ~0.24 default; rides the same knob

		// Section expression — vibrato + a chance of a scoop/fall, rolled once per onset and
		// applied to every tone of that stab so the whole section bends together.
		var ex = Expr( "HORNS" );
		Voicing hornVc = default;
		float gainNow = 1f;
		int tickNow = barTick;

		// one chord-tone voice
		void Note( int at, int dur, int k, double dec, float gain )
		{
			var horn = new Patch
			{
				Osc = 1, Voices = 3, Detune = _c.Detune,
				Amp = _c.HornVol * _c.HornBalance * _midMul / degs.Length * gain * sectionGain
					* NoteGain( tickNow, gainNow ),
				Attack = 0.008f, Decay = dec,
				Sustain = 0.2f, Sustained = false,
				Cutoff = _c.HornCutoff, CutEnv = 1200f, Reso = 1.0f,
				Drive = _c.HornDrive,
				Pan = spread * (k / (float)Math.Max( 1, degs.Length - 1 ) * 2f - 1f),
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

		// When the section is singing a tune, the horns ANSWER it — they do not double it. The
		// lead and the section are the same instrument in nearly the same register, so a full
		// two-bar stab figure under a horn melody reads as two lead lines fighting. The call bar
		// belongs to the melody; the horns take the answer bar, quieter.
		bool answerOnly = TuneFor( _sectionType ) != null;

		// Un-displaced, like the melody they answer (see RenderTune).
		foreach ( var h in _hornFig.Slice( barTick, barTick + barTicks, _sectionTick, _feel ) )
		{
			if ( answerOnly && ((h.Tick - _sectionTick) / barTicks) % 2 == 0 ) continue;
			int at = _time.TickToSample( h.Tick );
			double secPerEighth = _time.SpanSeconds( h.Tick, Timing.TicksPerEighth );
			tickNow = h.Tick; gainNow = h.Vel;
			hornVc = Roll( ex, baseMidi, NoPrev, exprRng );

			if ( six > 0 && orn.Chance( ornChance ) )
			{
				float r = orn.Next();
				if ( r < 0.4f )
				{
					// rolling arpeggio: chord tones climb across a 16th-triplet
					int step = spe / 3;
					for ( int k = 0; k < degs.Length; k++ )
						Note( _time.EvenSpan( h.Tick, Timing.TicksPerEighth, k / 3.0 ),
							(int)(step * 0.9f), k, secPerEighth / 3 * 0.8, 1f );
					continue;
				}
				if ( r < 0.75f )
				{
					// 16th pair: stab on the beat, softer echo on the "e"
					Stab( at, (int)(six * 0.85f), secPerEighth * 0.5 * 0.8, 1f );
					Stab( _time.TickToSample( h.Tick + Timing.TicksPerEighth / 2 ),
						(int)(six * 0.85f), secPerEighth * 0.5 * 0.7, 0.6f );
					continue;
				}
				// grace pickup: a soft single tone just before the block stab
				Note( _time.TickToSample( h.Tick - Timing.TicksPerEighth / 2 ), (int)(six * 0.8f), 0,
					secPerEighth * 0.5 * 0.6, 0.5f );
				Stab( at, (int)(spe * 0.6f), 0.22, 1f );
				continue;
			}

			// plain stab — length varies a touch so even straight bars aren't identical
			float lenMul = 0.45f + orn.Next() * 0.35f;
			Stab( at, (int)(spe * lenMul), 0.22, 1f );
		}
	}
}
