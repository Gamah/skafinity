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
	// The pop hook: a two-bar motif drawn ONCE per song and then repeated. No other genre repeats
	// a motif, and a pop song that never does reads as an improvisation over a pop backing. Built
	// off its own stream, so having one shifts nothing else in the song.
	List<(int Tick, int Degree, int Len)> _hook;

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
			case LeadStyle.Hook: RenderHookPhrase( barTick, span, chord, exprRng ); break;
			case LeadStyle.Bluesy: RenderSungPhrase( barTick, span, chord, rng, exprRng, sparse: true ); break;
			default: RenderSungPhrase( barTick, span, chord, rng, exprRng, sparse: false ); break;
		}
	}

	/// <summary>The lead's register, per grammar. A shred line lives high and wide; a horn line
	/// sits where a horn sits.</summary>
	int LeadBase() => _rootMidi + _keyShift + (_prof.Lead switch
	{
		LeadStyle.Shred => 26,
		LeadStyle.Bluesy => 21,
		LeadStyle.DoubleStop => 24,
		_ => 24,
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

	/// <summary>Metal: scalar shred. Runs of sixteenths straight up and down the mode across the
	/// whole register — not the sung line's chord-tone hops with a triplet ornament on top.</summary>
	void RenderShredPhrase( int barTick, int span, int chord, Rng rng, Rng exprRng )
	{
		int melBase = LeadBase();
		float amp = _c.LeadGtrVol * _c.LeadGtrBalance * _midMul;
		var ex = Expr( "LEAD GTR" );
		int prevMidi = NoPrev;
		int step = Timing.TicksPerEighth / 2;                       // sixteenths
		int degree = _prog[chord];
		int dir = rng.Chance( 0.5f ) ? 1 : -1;

		for ( int t = barTick; t < barTick + span; t += step )
		{
			// A run breathes at the phrase's seams, and turns around when it runs out of register.
			if ( rng.Chance( 0.12f ) ) { dir = -dir; continue; }
			degree += dir;
			if ( degree > _prog[chord] + 14 || degree < _prog[chord] - 7 ) { dir = -dir; degree += 2 * dir; }
			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			RenderLeadNote( _time.TickToSample( t ), _time.SpanSamples( t, step * 0.95 ), midi,
				amp * NoteGain( t, 1f ), _time.SpanSeconds( t, step ) * 0.8,
				_c.LeadGtrDrive, vc );
			prevMidi = midi;
		}
	}

	/// <summary>Country: pentatonic double-stops. Two notes a third apart, bent into — the
	/// Telecaster move, and the reason country's lead reads as country even over the same changes.
	/// </summary>
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

			int lower = ScaleMidi( melBase, degree );
			int upper = ScaleMidi( melBase, degree + 2 );            // the third above, in the mode
			var vc = Roll( ex, lower, prevMidi, exprRng );
			int dur = _time.SpanSamples( t, len * 0.9 );
			double dec = _time.SpanSeconds( t, len ) * 0.75;
			RenderLeadNote( _time.TickToSample( t ), dur, lower, amp * 0.75f * NoteGain( t, 1f ),
				dec, _c.LeadGtrDrive, vc );
			RenderLeadNote( _time.TickToSample( t ), dur, upper, amp * 0.75f * NoteGain( t, 1f ),
				dec, _c.LeadGtrDrive, vc );
			prevMidi = lower;

			degree = rng.Chance( 0.5f ) ? NearestChordTone( tones, degree + (rng.Chance( 0.5f ) ? 2 : -2) )
				: degree + (rng.Chance( 0.5f ) ? 1 : -1);
			t += Math.Max( Timing.TicksPerEighth, len );
		}
	}

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

	/// <summary>Pop: the hook. The SAME two-bar motif every phrase, transposed onto the current
	/// chord — that repetition is the whole difference between a pop lead and a solo.</summary>
	void RenderHookPhrase( int barTick, int span, int chord, Rng exprRng )
	{
		_hook ??= BuildHook( new Rng( $"{_tag}:hook" ), span );
		int melBase = LeadBase();
		float amp = _c.LeadGtrVol * _c.LeadGtrBalance * _midMul;
		var ex = Expr( "LEAD GTR" );
		int prev = NoPrev;
		foreach ( var (offset, degree, len) in _hook )
		{
			int t = barTick + offset;
			if ( offset >= span ) break;
			int midi = ScaleMidi( melBase, _prog[chord] + degree );
			var vc = Roll( ex, midi, prev, exprRng );
			prev = midi;
			RenderLeadNote( _time.TickToSample( t ), _time.SpanSamples( t, len * 0.9 ), midi,
				amp * NoteGain( t, 1f ), _time.SpanSeconds( t, len ) * 0.7, _c.LeadGtrDrive, vc );
		}
	}

	/// <summary>Draw the song's hook: a handful of notes over the phrase, aligned to the eighth
	/// grid and ending on a chord tone so every repetition lands.</summary>
	static List<(int, int, int)> BuildHook( Rng rng, int span )
	{
		var hook = new List<(int, int, int)>();
		int degree = 0;
		for ( int t = 0; t < span; )
		{
			int len = (1 + rng.Int( 3 )) * Timing.TicksPerEighth;
			if ( !rng.Chance( 0.25f ) ) hook.Add( (t, degree, len) );
			degree += rng.Chance( 0.35f ) ? (rng.Chance( 0.5f ) ? 2 : -2) : (rng.Chance( 0.5f ) ? 1 : -1);
			degree = Math.Clamp( degree, -3, 9 );
			t += len;
		}
		if ( hook.Count > 0 )
		{
			var last = hook[^1];
			hook[^1] = (last.Item1, 0, last.Item3);                 // resolve home
		}
		return hook;
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

	// Dispatch a lead note to the genre's lead voice: a distorted single-note guitar for rock,
	// otherwise the ska horn (RenderLead → trumpet).
	void RenderLeadNote( int at, int dur, int midi, float amp, double decaySec, float drive, in Voicing vc )
	{
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
