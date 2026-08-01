using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The lead line. One phrase generator served every genre — same rest chance, same leap logic,
// same register, same "a phrase every two bars" — which made the melody the most interchangeable
// thing in the song. There is a grammar per genre now (see LeadStyle); the VOICES below are
// unchanged, because a trumpet is a trumpet whatever it plays.
//
// Part of the MusicGen engine — see MusicGen.cs.

// ── Lead instrument voices ──
enum Instrument { Trumpet, Sax, Organ, Trombone }

public sealed partial class MusicGen
{
	/// <summary>One lead phrase, in the genre's own grammar.</summary>
	void RenderLeadPhrase( int barTick, int barTicks, int chord, Rng rng, Rng exprRng )
	{
		if ( rng.Chance( _prof.LeadSilence ) ) return;              // the phrase rests
		int span = Math.Max( 1, _prof.LeadPhraseBars ) * barTicks;
		switch ( _prof.Lead )
		{
			case LeadStyle.Shred: RenderShredPhrase( barTick, span, chord, rng, exprRng ); break;
			case LeadStyle.DoubleStop: RenderDoubleStopPhrase( barTick, span, chord, rng, exprRng ); break;
			case LeadStyle.Unison: RenderUnisonPhrase( chord, exprRng ); break;
			// Pop's Hook style is the tune everywhere (see Melody) — where a pop section has no
			// tune to sing it lays out rather than improvising, which is what a drop is.
			case LeadStyle.Hook:
			case LeadStyle.Bluesy: RenderSungPhrase( barTick, span, chord, rng, exprRng, sparse: true ); break;
			default: RenderSungPhrase( barTick, span, chord, rng, exprRng, sparse: false ); break;
		}
	}

	/// <summary>The lead's register, per grammar.
	///
	/// THE MELODY NEEDS AN OCTAVE OF ITS OWN. The comp registers are fixed and known — the rhythm
	/// guitar voices its chord over <c>_rootMidi + 12</c>, the skank and the keys over
	/// <c>+24</c> — so a lead based at +21 or +24 sang its whole line inside the chordal bed and
	/// simply disappeared into it, which no amount of gain fixes. +31 clears the top of that bed
	/// (a full voicing reaches about +31) and the tune's own degrees carry it up from there.
	///
	/// The two exceptions are relational rather than absolute: metal's shred already sat clear at
	/// +26 and ranges far wider than a tune does, and punk's unison IS the riff an octave up, so
	/// its base has to stay one octave over the guitar's <c>_rootMidi + 12</c> or it stops being
	/// a double.</summary>
	int LeadBase() => _rootMidi + _keyShift + (_prof.Lead switch
	{
		LeadStyle.Shred => 26,
		LeadStyle.Unison => 24,
		_ => 31,
	});

	// ── Ska (and rock, sparser): a sung line ──
	// Chord-tone locked on the strong beats so it stays consonant, stepping or leaping between
	// them. This is the old generator, kept for the two genres whose lead really is a melody —
	// with ska phrasing its second half as an ANSWER to its first (the horn convention), and rock
	// leaving far more space than it plays.
	void RenderSungPhrase( int barTick, int span, int chord, Rng rng, Rng exprRng, bool sparse )
	{
		int melBase = LeadBase();
		var tones = ChordDegrees( chord );
		int degree = tones[rng.Int( Math.Min( 3, tones.Length ) )];
		bool guitarLead = !_hornLead;
		float amp = guitarLead ? _c.LeadGtrVol * _c.LeadGtrBalance : _c.MelodyVol * _c.MelodyBalance;
		amp *= _midMul;
		float drive = guitarLead ? _c.LeadGtrDrive : _c.MelodyDrive;
		float rest = _c.MelodyRestChance * (sparse ? 1.6f : 1f);
		float tripChance = sparse ? 0f : _c.TripletChance;
		var ex = guitarLead ? Expr( "LEAD GTR" ) : Expr( "LEAD" );
		int prevMidi = NoPrev;

		int t = barTick;
		int half = barTick + span / 2;
		while ( t < barTick + span )
		{
			if ( rng.Chance( rest ) ) { t += Timing.TicksPerEighth; continue; }

			if ( rng.Chance( tripChance ) )
			{
				t = RenderLeadRun( t, chord, degree, melBase, amp, drive, ex, rng, exprRng, ref prevMidi );
				continue;
			}

			int len = (1 + rng.Int( 3 )) * Timing.TicksPerEighth;
			if ( t + len > barTick + span ) len = barTick + span - t;
			bool strong = ((t - barTick) / Timing.TicksPerEighth) % 2 == 0;

			// The answer half of a horn line lands a step lower than the call did — same shape,
			// resolved — rather than re-rolling as if it were a new phrase.
			if ( strong ) degree = NearestChordTone( tones, degree ) - (t >= half && !sparse ? 1 : 0);
			else
			{
				int step = rng.Chance( _c.MelodyLeapChance ) ? (rng.Chance( 0.5f ) ? 3 : -3)
					: (rng.Chance( 0.5f ) ? 1 : -1);
				degree = Math.Clamp( degree + step, _prog[chord] - 3, _prog[chord] + 10 );
			}

			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			RenderLeadNote( _time.TickToSample( t ), _time.SpanSamples( t, len * 0.9 ), midi,
				amp * NoteGain( t, 1f ), _time.SpanSeconds( t, len ) * 0.7, drive, vc );
			prevMidi = midi;
			t += Math.Max( Timing.TicksPerEighth, len );
		}
	}

	/// <summary>Metal: scalar shred. Runs straight up and down the mode across the whole register —
	/// not the sung line's chord-tone hops with a triplet ornament on top.
	///
	/// The run is sixteenths, with BURSTS of thirty-seconds inside it — which is what a run
	/// actually is on a fretboard. A player does not pick every note of a fast line: they pick one
	/// and hammer-on or pull-off the next, or sweep across strings, so a burst costs the hand far
	/// less than its note count suggests and is over before it runs out. That is why the burst is
	/// short and the surrounding line is not, and it is why nothing here gates on the tempo: the
	/// gesture is a handful of notes wherever it lands.</summary>
	void RenderShredPhrase( int barTick, int span, int chord, Rng rng, Rng exprRng )
	{
		int melBase = LeadBase();
		float amp = _c.LeadGtrVol * _c.LeadGtrBalance * _midMul;
		var ex = Expr( "LEAD GTR" );
		int prevMidi = NoPrev;
		int sixteenth = Timing.TicksPerEighth / 2, thirtySecond = Timing.TicksPerEighth / 4;
		int degree = _prog[chord];
		int dir = rng.Chance( 0.5f ) ? 1 : -1;
		int burst = 0;                                    // notes left in the current 32nd flurry

		for ( int t = barTick; t < barTick + span; )
		{
			// This note's value: inside a flurry it is a 32nd, otherwise a 16th — and a flurry
			// always STARTS on a 16th, four to eight notes long, so it reads as an acceleration
			// out of the line rather than as a second tempo.
			int step = burst > 0 ? thirtySecond : sixteenth;
			if ( burst > 0 ) burst--;
			else if ( rng.Chance( 0.18f ) ) burst = 4 + 2 * rng.Int( 3 );

			// A run breathes at the phrase's seams, and turns around when it runs out of register.
			if ( rng.Chance( 0.12f ) ) { dir = -dir; t += step; continue; }
			degree += dir;
			if ( degree > _prog[chord] + 14 || degree < _prog[chord] - 7 ) { dir = -dir; degree += 2 * dir; }
			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			RenderLeadNote( _time.TickToSample( t ), _time.SpanSamples( t, step * 0.95 ), midi,
				amp * NoteGain( t, 1f ), _time.SpanSeconds( t, step ) * 0.8,
				_c.LeadGtrDrive, vc );
			prevMidi = midi;
			t += step;
		}
	}

	/// <summary>Country: a single-note line with double-stops as punctuation — the Telecaster move,
	/// and the reason country's lead reads as country even over the same changes.
	///
	/// PUNCTUATION, not the voice. Harmonising every note in parallel thirds is not a guitar part:
	/// two pitches, held the same length, bent and vibratoed together, is a two-tone horn, and it
	/// leaves no single line for the ear to follow. <see cref="DoubleStopChance"/> is what makes it
	/// a lick.</summary>
	void RenderDoubleStopPhrase( int barTick, int span, int chord, Rng rng, Rng exprRng )
	{
		int melBase = LeadBase();
		float amp = _c.LeadGtrVol * _c.LeadGtrBalance * _midMul;
		var ex = Expr( "LEAD GTR" );
		var tones = ChordDegrees( chord );
		int prevMidi = NoPrev;
		int degree = tones[rng.Int( tones.Length )];

		for ( int t = barTick; t < barTick + span; )
		{
			int len = (1 + rng.Int( 3 )) * Timing.TicksPerEighth;
			if ( t + len > barTick + span ) len = barTick + span - t;
			if ( rng.Chance( 0.25f ) ) { t += len; continue; }       // country leaves space

			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			float gain = amp * NoteGain( t, 1f );
			RenderLeadNote( _time.TickToSample( t ), _time.SpanSamples( t, len * 0.9 ), midi, gain,
				_time.SpanSeconds( t, len ) * 0.75, _c.LeadGtrDrive, vc );
			if ( rng.Chance( DoubleStopChance ) ) EmitDoubleStop( t, len, degree, gain );
			prevMidi = midi;

			degree = rng.Chance( 0.5f ) ? NearestChordTone( tones, degree + (rng.Chance( 0.5f ) ? 2 : -2) )
				: degree + (rng.Chance( 0.5f ) ? 1 : -1);
			// Held inside the phrase's own register. The walk had no bound at all, so a long solo
			// wandered off the top or the bottom of the line it started on and never came back —
			// which is the same "where is the melody" the parallel thirds caused.
			degree = Math.Clamp( degree, _prog[chord] - 3, _prog[chord] + 10 );
			t += Math.Max( Timing.TicksPerEighth, len );
		}
	}

	/// <summary>How often a country lead note is picked as a double-stop rather than played alone.
	/// It is an ornament: on every note it stops being one, and the line stops being a line.</summary>
	const float DoubleStopChance = 0.35f;

	/// <summary>The harmony note of a country double-stop: the diatonic third UNDER the melody
	/// note, picked with it and left to fall away.
	///
	/// UNDER, because the melody has to stay the top line — harmonising above makes the ornament
	/// the highest voice, and the ear follows the top, so the line a listener hears is the harmony
	/// rather than the tune. DRY, because the melody note's bend/scoop/vibrato is a fretting hand
	/// working ONE string: sliding both notes of a dyad in parallel is the horn. And SHORTER,
	/// because it is picked with the melody note, not held alongside it.</summary>
	void EmitDoubleStop( int tick, int lenTicks, int degree, float gain )
		=> RenderLeadNote( _time.TickToSample( tick ), _time.SpanSamples( tick, lenTicks * 0.6 ),
			ScaleMidi( LeadBase(), degree - 2 ), gain * 0.55f,
			_time.SpanSeconds( tick, lenTicks ) * 0.45, _c.LeadGtrDrive, default );

	/// <summary>Punk: unison. When the lead plays at all it doubles the riff an octave up, which
	/// is what a second guitarist in a three-piece actually does. Falls silent when there is no
	/// riff to double (its LeadSilence is high, so that is most phrases anyway).</summary>
	void RenderUnisonPhrase( int chord, Rng exprRng )
	{
		if ( _riffOnsets.Count == 0 ) return;
		int melBase = LeadBase();
		float amp = _c.LeadGtrVol * _c.LeadGtrBalance * _midMul * 0.8f;
		var ex = Expr( "LEAD GTR" );
		int prev = NoPrev;
		foreach ( var h in _riffOnsets )
		{
			if ( h.Value == CompFigure.Mute ) continue;              // the muted chug is not a note
			int midi = ScaleMidi( melBase, _prog[chord] );
			var vc = Roll( ex, midi, prev, exprRng );
			prev = midi;
			RenderLeadNote( _time.TickToSample( h.Tick ), _time.SpanSamples( h.Tick, h.SpanTicks * 0.9 ),
				midi, amp * NoteGain( h.Tick, h.Vel ), _time.SpanSeconds( h.Tick, h.SpanTicks ) * 0.7,
				_c.LeadGtrDrive, vc );
		}
	}

	/// <summary>A run of evenly-spaced notes around the current degree — the sung line's ornament.
	/// Returns the tick the phrase continues from.</summary>
	int RenderLeadRun( int t, int chord, int degree, int melBase, float amp, float drive,
		in Expression ex, Rng rng, Rng exprRng, ref int prevMidi )
	{
		float r = rng.Next();
		int n, spanTicks;
		if ( r < 0.25f ) { n = 2; spanTicks = Timing.TicksPerEighth; }
		else if ( r < 0.50f ) { n = 3; spanTicks = Timing.TicksPerEighth; }
		else if ( r < 0.80f ) { n = 3; spanTicks = Timing.TicksPerBeat; }
		else { n = 3; spanTicks = Timing.TicksPerBeat * 2; }

		int first = ScaleMidi( melBase, Math.Clamp( degree - n / 2, _prog[chord] - 3, _prog[chord] + 10 ) );
		var runVc = Roll( ex, first, prevMidi, exprRng );
		for ( int k = 0; k < n; k++ )
		{
			int d2 = Math.Clamp( degree + (k - n / 2), _prog[chord] - 3, _prog[chord] + 10 );
			int m2 = ScaleMidi( melBase, d2 );
			// A tuplet divides its own span evenly — it is not warped onto the shuffle grid a
			// second time (see Timing.EvenSpan).
			RenderLeadNote( _time.EvenSpan( t, spanTicks, k / (double)n ),
				_time.SpanSamples( t, spanTicks / (double)n * 0.9 ), m2, amp * NoteGain( t, 1f ),
				_time.SpanSeconds( t, spanTicks ) / n * 0.85, drive, runVc );
			prevMidi = m2;
		}
		return t + spanTicks;
	}

	/// <summary>The chord tone nearest <paramref name="degree"/>, in degree space.</summary>
	int NearestChordTone( int[] tones, int degree )
	{
		int best = tones[0], bestD = int.MaxValue;
		foreach ( var t in tones )
			for ( int oc = -_scale.Length; oc <= 2 * _scale.Length; oc += _scale.Length )
			{
				int cand = t + oc;
				int dist = Math.Abs( cand - degree );
				if ( dist < bestD ) { bestD = dist; best = cand; }
			}
		return best;
	}

	/// <summary>Per-genre lead level, measured with `--levels` and re-measured after any change to
	/// what the lead plays.
	///
	/// The target is +2 dB over that genre's drums, not level with them. The mix rebalance set
	/// these against the kit while the lead was still filling the odd phrase; it carries the tune
	/// now, and a melody that sits AT kit level is a melody a listener has to go looking for.
	/// Every genre measured within a dB of 0 before this, so each is a straight trim to +2 —
	/// which is also comfortably inside the suite's "the lead does not dominate" ceiling.</summary>
	float LeadLevel() => _genre switch
	{
		0 => 0.83f,   // ska horn
		1 => 0.86f,   // rock
		2 => 0.77f,   // country: one note, with the odd double-stop under it
		3 => 1.24f,   // metal: it is supposed to be on top
		4 => 0.64f,   // punk: it doubles the guitar, and two of everything is loud
		_ => 1.21f,   // pop: the hook IS the song
	};

	// Dispatch a lead note to the genre's lead voice: a distorted single-note guitar for rock,
	// otherwise the ska horn (RenderLead → trumpet).
	void RenderLeadNote( int at, int dur, int midi, float amp, double decaySec, float drive, in Voicing vc )
	{
		amp *= LeadLevel();
		if ( !_hornLead )
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

	// Which voice takes the ska lead — a weighted draw, or the config's override. The draw is
	// taken either way: a knob that decides WHAT plays must not also decide how many values the
	// composer pulls, or overriding the instrument would quietly rewrite the rest of the song.
	Instrument PickInstrument( Rng rng )
	{
		float tw = MathF.Max( 0f, _c.TrumpetWeight ), sw = MathF.Max( 0f, _c.SaxWeight );
		float ow = MathF.Max( 0f, _c.OrganWeight ), bw = MathF.Max( 0f, _c.TromboneWeight );
		float sum = tw + sw + ow + bw;
		float r = rng.Next() * sum;

		if ( _c.ForceInstrument >= 0 && _c.ForceInstrument <= 3 ) return (Instrument)_c.ForceInstrument;
		if ( sum <= 0f ) return Instrument.Trumpet;
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
