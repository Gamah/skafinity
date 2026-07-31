using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The lead line: phrase generation (chord-tone locked, so it stays consonant), the lead
// instrument voices, and the genre dispatch that picks between them.
//
// Part of the MusicGen engine — see MusicGen.cs.

// ── Lead instrument voices ──
enum Instrument { Trumpet, Sax, Organ, Trombone }

public sealed partial class MusicGen
{
	// ── Lead melody (chord-tone locked → consonant) ──
	void RenderLeadPhrase( int barTick, int chord, Rng rng, Rng exprRng )
	{
		int spe = _time.Spe;
		double secPerEighth = _time.SecPerEighth;
		int slots = EighthsPerBar * 2;
		int melBase = _rootMidi + 24;
		int[] tones = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 6 }; // chord tones
		int degree = tones[rng.Int( 3 )];
		bool guitarLead = _genre != 0;                    // ska is the only horn lead
		float amp = guitarLead ? _c.LeadGtrVol * _c.LeadGtrBalance : _c.MelodyVol * _c.MelodyBalance;
		float drive = guitarLead ? _c.LeadGtrDrive : _c.MelodyDrive;
		// Rock lead trades fast RUNS for BENDINESS (handled via expression), so its run rate is
		// forced to 0; metal shreds (a high floor of runs); ska/country keep the TRIPLETS knob.
		float tripChance = _genre switch
		{
			1 => 0f,
			3 => MathF.Max( _c.TripletChance, 0.4f ),
			_ => _c.TripletChance,
		};
		var ex = guitarLead ? Expr( "LEAD GTR" ) : Expr( "LEAD" );
		int prevMidi = NoPrev;

		int e = 0;
		while ( e < slots )
		{
			if ( rng.Chance( _c.MelodyRestChance ) ) { e++; continue; }

			// ornament: a sixteenth pair, or a triplet at one of three rates — a tight
			// 16th-triplet (3 in an eighth), an eighth-note triplet (3 in a beat), or a
			// wide quarter-note triplet (3 over two beats). Wider spans give the lazy,
			// over-the-barline triplet feel, not just the fast run.
			if ( rng.Chance( tripChance ) )
			{
				float r = rng.Next();
				int n, spanE; // n notes evenly across spanE eighths
				if ( r < 0.25f ) { n = 2; spanE = 1; }       // sixteenth pair
				else if ( r < 0.50f ) { n = 3; spanE = 1; }  // 16th-note triplet
				else if ( r < 0.80f ) { n = 3; spanE = 2; }  // eighth-note triplet (1 beat)
				else { n = 3; spanE = 4; }                   // quarter-note triplet (2 beats)
				if ( e + spanE > slots ) spanE = 1;

				int span = spanE * spe;
				int step = span / n;
				int firstMidi = ScaleMidi( melBase, Math.Clamp( degree - n / 2, _prog[chord] - 3, _prog[chord] + 10 ) );
				var runVc = Roll( ex, firstMidi, prevMidi, exprRng );
				for ( int k = 0; k < n; k++ )
				{
					int d2 = Math.Clamp( degree + (k - n / 2), _prog[chord] - 3, _prog[chord] + 10 );
					int m2 = ScaleMidi( melBase, d2 );
					RenderLeadNote( _time.EvenSpan( barTick + e * Timing.TicksPerEighth,
						spanE * Timing.TicksPerEighth, k / (double)n ), (int)(step * 0.9f),
						m2, amp, secPerEighth * spanE / (double)n * 0.85, drive, runVc );
					prevMidi = m2;
				}
				e += spanE;
				continue;
			}

			int len = 1 + rng.Int( 3 );
			if ( e + len > slots ) len = slots - e;
			bool strong = (e % 2) == 0;

			if ( strong )
			{
				// land on a chord tone near the current degree
				int best = tones[0], bestD = 999;
				foreach ( var t in tones )
				{
					for ( int oc = -7; oc <= 14; oc += 7 )
					{
						int cand = t + (oc / 7) * 7; // keep in degree space
						int dist = Math.Abs( cand - degree );
						if ( dist < bestD ) { bestD = dist; best = cand; }
					}
				}
				degree = best;
			}
			else
			{
				int step = rng.Chance( _c.MelodyLeapChance ) ? (rng.Chance( 0.5f ) ? 3 : -3) : (rng.Chance( 0.5f ) ? 1 : -1);
				degree = Math.Clamp( degree + step, _prog[chord] - 3, _prog[chord] + 10 );
			}

			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			RenderLeadNote( _time.TickToSample( barTick + (e) * Timing.TicksPerEighth ), (int)(spe * len * 0.9f), midi,
				amp, secPerEighth * len * 0.7f, drive, vc );
			prevMidi = midi;
			e += len;
		}
	}

	// Dispatch a lead note to the genre's lead voice: a distorted single-note guitar for rock,
	// otherwise the ska horn (RenderLead → trumpet).
	void RenderLeadNote( int at, int dur, int midi, float amp, double decaySec, float drive, in Voicing vc )
	{
		if ( _genre != 0 )
		{
			// Twang = a bright cutoff-envelope snap on each pick (high CutEnv, decays fast) through
			// a resonant SVF, plus a BASE distortion under the slider so it reads as an electric
			// guitar even at the slider minimum. The base is genre-set: rock = 3 (overdriven),
			// metal = 4 hot (heavy), country = clean (the bite comes from the twang snap + bends,
			// not gain). The bends (BENDINESS knob → bend-in + scoop) come in via the voicing.
			float driveAmt = _genre switch
			{
				3 => 4f + MathF.Max( 1f, _c.LeadGtrDrive ),         // metal: heavy
				2 => 0.8f + 0.3f * MathF.Max( 1f, _c.LeadGtrDrive ),// country: clean twang
				4 => 2f + MathF.Max( 1f, _c.LeadGtrDrive ),         // punk: bright, lightly driven
				5 => 0.6f + 0.2f * MathF.Max( 1f, _c.LeadGtrDrive ),// pop: clean synth pluck/lead
				_ => 3f + MathF.Max( 1f, _c.LeadGtrDrive ),         // rock
			};
			// Country/pop get a brighter cutoff snap — country for telecaster twang, pop for a
			// plucky synth attack.
			float cutEnv = _genre == 2 ? 3000f : _genre == 5 ? 3500f : 2200f;
			var gtr = new Patch
			{
				Osc = 1, Voices = 1, Detune = 0f, Amp = amp,
				Attack = 0.002f, Decay = decaySec, Sustain = 0.55f, Sustained = true,
				Cutoff = _c.LeadGtrCutoff, CutEnv = cutEnv, Reso = 0.65f,
				Drive = driveAmt, Pan = _leadPan, Vibrato = _c.MelodyVibrato,
			};
			ApplyVoicing( ref gtr, vc );
			// The lead is monophonic — a single clean take at its per-song pan (_leadPan). It is NOT
			// double-tracked: splitting a solo line into two detuned, hard-panned, time-offset takes
			// beats on sustained notes and overlaps adjacent pitches on runs, reading as "out of key".
			// Double-tracking width is for the chordal/strummed voices, not the melody.
			RenderPatch( at, dur, Midi( midi ), gtr, mono: true );
			return;
		}
		RenderLead( at, dur, midi, amp, decaySec, drive, vc );
	}

	Instrument PickInstrument( Rng rng )
	{
		if ( _c.ForceInstrument >= 0 && _c.ForceInstrument <= 3 ) return (Instrument)_c.ForceInstrument;
		float tw = MathF.Max( 0f, _c.TrumpetWeight ), sw = MathF.Max( 0f, _c.SaxWeight );
		float ow = MathF.Max( 0f, _c.OrganWeight ), bw = MathF.Max( 0f, _c.TromboneWeight );
		float sum = tw + sw + ow + bw;
		if ( sum <= 0f ) return Instrument.Trumpet;
		float r = rng.Next() * sum;
		if ( (r -= tw) < 0f ) return Instrument.Trumpet;
		if ( (r -= sw) < 0f ) return Instrument.Sax;
		if ( (r -= ow) < 0f ) return Instrument.Organ;
		return Instrument.Trombone;
	}

	void RenderLead( int at, int dur, int midi, float amp, double decaySec, float drive, in Voicing vc )
	{
		Patch p; int m = midi;
		switch ( _lead )
		{
			case Instrument.Trumpet:
				p = new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune * 0.7f, Amp = amp,
					Attack = 0.01f, Decay = decaySec, Sustain = 0.7f, Sustained = true,
					Cutoff = _c.LeadCutoff, CutEnv = 1800f, Reso = 1.0f, Drive = drive,
					Pan = _leadPan, Vibrato = _c.MelodyVibrato,
				};
				break;
			case Instrument.Trombone:
				m = midi - 12;
				p = new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune * 0.7f, Amp = amp * 1.1f,
					Attack = 0.02f, Decay = decaySec, Sustain = 0.7f, Sustained = true,
					Cutoff = _c.LeadCutoff * 0.7f, CutEnv = 900f, Reso = 1.0f, Drive = MathF.Max( 1f, drive * 0.8f ),
					Pan = _leadPan, Vibrato = _c.MelodyVibrato * 0.7f,
				};
				break;
			case Instrument.Sax:
				p = new Patch
				{
					Osc = 3, Voices = 2, Detune = _c.Detune * 0.5f, Amp = amp * 1.15f,
					Attack = 0.014f, Decay = decaySec, Sustain = 0.75f, Sustained = true,
					Cutoff = _c.LeadCutoff, CutEnv = 1400f, Reso = 0.7f, Drive = MathF.Max( 1.2f, drive ),
					Pan = _leadPan, Vibrato = _c.MelodyVibrato, Breath = 0.03f,
				};
				break;
			default: // Organ
				p = new Patch
				{
					Osc = 0, Voices = 3, Detune = _c.Detune * 0.6f, Amp = amp,
					Attack = 0.006f, Decay = decaySec * 1.5, Sustain = 0.9f, Sustained = true,
					Cutoff = 2600f, CutEnv = 0f, Reso = 1.0f, Drive = 1.15f,
					Pan = _leadPan, Vibrato = _c.MelodyVibrato * 0.9f,
				};
				break;
		}
		ApplyVoicing( ref p, vc );
		// Single clean take at _leadPan — the ska horn/organ lead is monophonic, so it is not
		// double-tracked (see the guitar-lead note in RenderLeadNote: doubling a solo line smears
		// pitch). Only chordal/strummed voices get the width.
		RenderPatch( at, dur, Midi( m ), p, mono: true );
	}
}
