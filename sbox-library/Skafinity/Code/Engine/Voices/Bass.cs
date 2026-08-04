using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// Bass — the genre's own line, from the genre's own library, through the genre's own voice.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// The bass VOICE per genre. Oscillator, sustain and filter are the sound of the instrument, so
	// they live next to it rather than in GenreProfile. The line's REGISTER is not here: it comes
	// out of the pattern's own octave cells, which is where a bass player's register decisions
	// actually live.
	// Level is part of the tone here for a real reason: a short, tight bass note carries far less
	// energy than a held legato one, so the genres whose bass is picked and staccato (metal, punk)
	// measured 10 dB under the kit at the same balance, while pop's sustained synth sub measured
	// 4 dB over it. The one global BassBalance cannot express that — it is the same instrument at
	// the same level playing a different way. Measured with `--levels`; re-measure after changing
	// a bass pattern or its sustain.
	(int Osc, float Sustain, float Cutoff, float Drive, float Level) BassTone() => _genre switch
	{
		0 => (3, 0.60f, 1.0f, 1.0f, 1.00f),   // ska: round, deep, legato
		1 => (3, 0.50f, 1.15f, 1.15f, 1.05f), // rock: fingered, a touch brighter
		2 => (3, 0.35f, 0.95f, 0.9f, 1.00f),  // country: short, plummy, out of the way
		3 => (1, 0.45f, 1.45f, 1.6f, 2.20f),  // metal: a saw with bite, so it cuts under the riff
		4 => (1, 0.40f, 1.35f, 1.4f, 2.10f),  // punk: picked and clanky
		_ => (2, 0.30f, 1.25f, 1.05f, 0.70f), // pop: a tight square synth sub
	};

	// ── Bass ──
	void RenderBassBar( int barTick, int barTicks, int chord, int nextChord, Rng rng, Rng bassOrn,
		Rng exprRng )
	{
		int to = barTick + barTicks;

		// Metal's bass is either a low pedal point or a double of the riff, and punk takes the
		// same unison when it draws it. Both are RELATIONAL — no table can express "whatever the
		// guitar just played" — so this reads the riff's onsets instead of a pattern.
		if ( _riffBass && _riffOnsets.Count > 0 )
		{
			RenderBassFromRiff( chord, exprRng );
			return;
		}

		int root = ChordRoot( chord );
		var ex = Expr( "BASS" );
		int prevMidi = NoPrev;
		// Bass has its own ornament knob (BASS TRIPLETS), nudged up a touch by overall
		// kit busyness so a busy vibe gets a busier bass.
		float ornChance = _c.BassTriplets * 0.5f + _c.DrumBusy * 0.05f;

		var line = _bassPat.Slice( barTick, to, _sectionTick, _feel );
		Trace?.Add( TraceVoice.Bass, line );
		foreach ( var h in line )
		{
			int midi;
			if ( h.Value == Harmony.Approach )
			{
				// Walk only where the harmony actually moves. The approach cell fires at the end
				// of the pattern, but with 2 bars to a chord half of those ends land on the same
				// chord they began on — leading into a chord that isn't arriving just reads as a
				// wrong note, so the bar sits on its root instead. The draw is consumed either
				// way so the ornament stream downstream stays aligned.
				bool chordMoves = nextChord != chord;
				int target = ChordRoot( nextChord );
				int lead = target - (rng.Chance( 0.5f ) ? 1 : 2); // chromatic/step lead-in
				midi = chordMoves ? lead : root;
			}
			else
			{
				midi = root + h.Value;
				if ( h.Value == 0 && h.Tick > barTick && rng.Chance( _c.OctavePopChance ) ) midi += 12;
			}

			var vc = Roll( ex, midi, prevMidi, exprRng );
			prevMidi = midi;

			// Chop a standalone note into a 16th pair or triplet so the line reads "long long
			// short short" instead of even eighths. Its own stream, so the composition order is
			// untouched.
			if ( h.Value != Harmony.Approach && h.SpanTicks <= Timing.TicksPerEighth
				&& bassOrn.Chance( ornChance ) )
			{
				int n = bassOrn.Chance( 0.65f ) ? 2 : 3;        // 16th pair / 16th triplet
				int[] moves = { 0, 7, 12 };                     // root / fifth / octave
				for ( int k = 0; k < n; k++ )
				{
					int bm = midi + (k == 0 ? 0 : moves[bassOrn.Int( moves.Length )]);
					EmitBass( _time.EvenSpan( h.Tick, h.SpanTicks, k / (double)n ),
						_time.SpanSamples( h.Tick, h.SpanTicks / (double)n * 0.9 ), bm,
						_time.SpanSeconds( h.Tick, h.SpanTicks ) / n * 0.8,
						NoteGain( h.Vel ), vc );
				}
				continue;
			}

			EmitBass( _time.TickToSample( h.Tick ), _time.SpanSamples( h.Tick, h.SpanTicks * 0.95 ),
				midi, _time.SpanSeconds( h.Tick, h.SpanTicks ) * 0.8, NoteGain( h.Vel ), vc );
		}
	}

	// The riff-following mode. Wikipedia's Heavy metal bass puts it plainly: the bass is either
	// "a low pedal point as a foundation" or "doubling complex riffs and licks along with the
	// lead guitar". This is the second of those, and the pedal is what the fallback tables do.
	//
	// The bass plays the riff's onsets on the chord root — an octave below the guitar, ignoring
	// the guitar's chord tones, which is what makes it read as a doubling rather than a second
	// guitar. Muted chug cells are kept: they ARE the riff's rhythm.
	//
	// A doubling stops at the SIXTEENTH, whatever the guitar is doing. Where the riff runs at the
	// thirty-second the bass plays its accents — the chord statements — and leaves the tremolo to
	// the guitar, which is what a player does under one: four low notes a beat is mud, not a part.
	// The cut is metrical rather than physical, so it is a tick length: a sixteenth gallop's cells
	// already span a sixteenth and are untouched.
	void RenderBassFromRiff( int chord, Rng exprRng )
	{
		int root = ChordRoot( chord );
		var ex = Expr( "BASS" );
		int prev = NoPrev;
		foreach ( var h in _riffOnsets )
		{
			if ( h.SpanTicks < Timing.TicksPerEighth / 2 && h.Value != CompFigure.Ring ) continue;
			Trace?.Add( TraceVoice.Bass, h.Tick );
			var vc = Roll( ex, root, prev, exprRng );
			prev = root;
			EmitBass( _time.TickToSample( h.Tick ), _time.SpanSamples( h.Tick, h.SpanTicks * 0.95 ),
				root, _time.SpanSeconds( h.Tick, h.SpanTicks ) * 0.8,
				NoteGain( h.Vel ) * (h.Value == CompFigure.Mute ? 0.85f : 1f), vc );
		}
	}

	void EmitBass( int at, int dur, int midi, double decaySec, float gain, in Voicing vc )
	{
		var (osc, sustain, cutoff, drive, level) = BassTone();
		// Triangle body for a round, deep reggae/dub bass (saw alone read as too buzzy) — but
		// triangle alone was too subtle, so layer a quieter square underneath for presence. The
		// genre's own oscillator replaces the body where the genre wants bite; both layers share
		// the bass low-pass so the tone stays warm.
		float amp = _c.BassVol * _c.BassBalance * level * MixTrim( _prof.Mix.Low ) * gain;
		var body = new Patch
		{
			Osc = osc, Voices = 2, Detune = _c.Detune * 0.4f,
			Amp = amp, Attack = 0.004f, Decay = decaySec,
			Sustain = sustain, Sustained = true,
			Cutoff = _c.BassCutoff * cutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive * drive, Pan = 0f,
		};
		var sub = new Patch
		{
			Osc = 2, Voices = 1, Detune = 0f,
			Amp = amp * 0.4f, Attack = 0.004f, Decay = decaySec,
			Sustain = sustain, Sustained = true,
			Cutoff = _c.BassCutoff * cutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive * drive, Pan = 0f,
		};
		ApplyVoicing( ref body, vc ); ApplyVoicing( ref sub, vc );
		RenderPatch( at, dur, Midi( midi ), body, mono: true );
		RenderPatch( at, dur, Midi( midi ), sub, mono: true );
	}
}
