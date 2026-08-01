using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>
/// The TUNE — the part of a song a listener could hum back.
///
/// Everything the engine generated before this was accompaniment plus an improvisation: the
/// chordal voices played a rhythm figure, and the lead invented a fresh phrase every two bars.
/// That is a backing track, not a song. Real rock, punk, ska and pop songs are built on a
/// MELODY that recurs — the chorus states the same tune every time it comes round, and that
/// repetition is what makes it a chorus rather than another eight bars.
///
/// A tune is a <see cref="Pattern"/> whose cell values are SCALE DEGREES relative to the key's
/// tonic (not to the current chord), so the line keeps its shape while the harmony moves under
/// it — which is what a melody is. <see cref="MusicGen.RenderTune"/> resolves a degree against
/// the bar's chord on the strong beats, so the tune stays consonant without being re-written
/// chord by chord.
///
/// Because it is a Pattern it inherits everything patterns get: it anchors to the section (so a
/// four-bar tune restarts with the chorus), it stretches under a half-time feel, and a section
/// can displace it.
/// </summary>
static class Melody
{
	/// <summary>Cell value for a rest — no onset, the previous note holds.</summary>
	public const int Rest = Harmony.Rest;

	/// <summary>
	/// Draw a tune: <paramref name="bars"/> bars of CALL AND ANSWER. The first phrase states a
	/// shape and leaves it open; the second repeats that rhythm and resolves it home. That
	/// call/answer symmetry is most of what makes a line sound composed rather than generated —
	/// a fresh random phrase every two bars never sounds like a tune however good the notes are.
	/// </summary>
	/// <param name="density">Notes per bar, roughly — punk and pop sing in long notes, metal
	/// and ska fill more of the bar.</param>
	/// <param name="leap">How often the line jumps rather than steps.</param>
	public static Pattern Draw( Rng rng, int bars, int barTicks, float density, float leap )
	{
		int phraseTicks = barTicks * Math.Max( 1, bars / 2 );
		var ticks = new List<int>();
		var degrees = new List<int>();

		// ── the call ──
		// Rhythm first, on the eighth grid: a melody's rhythm is what gets remembered, and
		// drawing it separately is what lets the answer repeat it exactly.
		var rhythm = new List<int>();
		for ( int t = 0; t < phraseTicks; )
		{
			rhythm.Add( t );
			// Long notes on the strong beats, shorter ones between them — but weighted by
			// density, so a punk tune is mostly quarters and a ska horn line moves.
			int len = rng.Next() < density
				? Timing.TicksPerEighth * (1 + rng.Int( 2 ))
				: Timing.TicksPerBeat * (1 + rng.Int( 2 ));
			t += len;
		}

		// The contour. Steps most of the time, a leap now and then, held inside a singable
		// range — a melody that wanders more than an octave and a bit stops being singable.
		int degree = rng.Int( 3 ) * 2;                 // start on a chord tone: root, 3rd or 5th
		foreach ( var t in rhythm )
		{
			ticks.Add( t );
			degrees.Add( degree );
			int step = rng.Next() < leap ? (rng.Chance( 0.5f ) ? 2 : -2) : (rng.Chance( 0.5f ) ? 1 : -1);
			degree = Math.Clamp( degree + step, -2, 9 );
		}

		// ── the answer ──
		// The same rhythm, the same shape, resolved: it tracks the call a step lower and lands
		// on the tonic. Repeating the rhythm exactly is the point — vary the rhythm too and the
		// two phrases stop being heard as a question and an answer.
		int n = ticks.Count;
		for ( int i = 0; i < n; i++ )
		{
			ticks.Add( phraseTicks + ticks[i] );
			bool last = i == n - 1;
			degrees.Add( last ? 0 : Math.Clamp( degrees[i] - 1, -2, 9 ) );
		}

		// A held final note, so the tune breathes before it comes round again.
		return new Pattern( bars * barTicks, ticks.ToArray(), degrees.ToArray() );
	}
}

// The tune, and how a bar of it is played. Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// The song's tunes, drawn once per song off their own streams (so having them shifts nothing
	// else in the composition) and keyed by SECTION TYPE. The chorus tune is the hook: identical
	// every chorus, which is the whole reason a chorus reads as one. The verse tune is a second,
	// sparser line — same song, different words.
	Pattern _chorusTune, _verseTune;

	/// <summary>The tune this section sings, or null where the section is not a place for one:
	/// a solo is where the genre's lead grammar improvises, an intro is a build-in, and the
	/// ending has already resolved.</summary>
	Pattern TuneFor( Section s ) => s switch
	{
		Section.Chorus => _chorusTune,
		Section.Verse => _verseTune,
		Section.PreChorus => _verseTune,
		Section.Bridge => _verseTune,
		_ => null,
	};

	/// <summary>Draw the song's tunes. Metal is the one genre that stays riff-led — its chorus
	/// gets a line, its verses are left to the riff and the shred grammar.</summary>
	void DrawTunes( int barTicks )
	{
		float density = _prof.Lead switch
		{
			LeadStyle.Shred => 0.75f,        // metal moves
			LeadStyle.HornLine => 0.6f,      // a horn line phrases in eighths
			LeadStyle.DoubleStop => 0.45f,
			LeadStyle.Unison => 0.3f,        // punk sings in long notes
			_ => 0.4f,
		};
		float leap = _prof.Lead == LeadStyle.Shred ? 0.35f : 0.2f;
		// The tune is as long as the HARMONIC CYCLE — the bars it takes the progression to come
		// round (ChordBars x the progression's length), capped at eight. A four-bar tune over an
		// eight-bar cycle states itself twice, and the second statement lands over different
		// chords than it was written against: same notes, different harmony, which is exactly the
		// "the lead clashes with the backing" it sounds like. Matching the cycle means every
		// repetition sits over the changes it was drawn for.
		int cycle = Math.Clamp( _chordBars * _prog.Length, 2, 8 );
		_chorusTune = Melody.Draw( new Rng( $"{_tag}:tune:chorus" ), cycle, barTicks, density, leap );
		_verseTune = _genre == 3 ? null
			: Melody.Draw( new Rng( $"{_tag}:tune:verse" ), cycle, barTicks, density * 0.8f, leap );
	}

	/// <summary>Play one bar of the section's tune.
	///
	/// Degrees are relative to the KEY, so the tune keeps its shape as the chords move. What
	/// keeps it consonant is resolution on the strong beats only: a note landing on a beat is
	/// pulled to the nearest tone of the bar's chord, while the notes between beats are free to
	/// pass through. Snapping everything would rewrite the tune chord by chord — which is
	/// exactly the "no tune, just an improvisation over the changes" this replaces.</summary>
	void RenderTune( Pattern tune, int barTick, int barTicks, int chord, Rng rng, Rng exprRng )
	{
		int melBase = LeadBase();
		var tones = ChordDegrees( chord );
		bool guitarLead = !_hornLead;
		float amp = (guitarLead ? _c.LeadGtrVol * _c.LeadGtrBalance : _c.MelodyVol * _c.MelodyBalance)
			* _midMul;
		float drive = guitarLead ? _c.LeadGtrDrive : _c.MelodyDrive;
		var ex = guitarLead ? Expr( "LEAD GTR" ) : Expr( "LEAD" );
		int prevMidi = NoPrev;

		foreach ( var h in tune.Slice( barTick, barTick + barTicks, _sectionTick, _feel, _displace ) )
		{
			int degree = h.Value;
			int len = Math.Min( h.SpanTicks, Timing.TicksPerBeat * 2 );
			bool onBeat = (h.Tick - _barTick) % Timing.TicksPerBeat == 0;

			// What resolves is the note the ear has TIME to hear against the chord: anything on a
			// beat, and anything held for a beat or more. A quick note between beats is a passing
			// tone and is left alone — that is the difference between a melody and an arpeggio.
			// (Snapping only the on-beat notes left long off-beat non-chord tones ringing over the
			// backing for up to two beats, which is what a clash sounds like.)
			if ( onBeat || len >= Timing.TicksPerBeat ) degree = NearestChordTone( tones, degree );
			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			prevMidi = midi;
			RenderLeadNote( _time.TickToSample( h.Tick ), _time.SpanSamples( h.Tick, len * 0.92 ),
				midi, amp * NoteGain( h.Tick, h.Vel ), _time.SpanSeconds( h.Tick, len ) * 0.8,
				drive, vc );

			// The genre's own hand on the same tune: country harmonises it in double-stops, metal
			// runs between its notes. The line is the same either way — this is ornament, not a
			// different melody, which is the difference between a genre playing a song and a
			// genre having its own song.
			if ( _prof.Lead == LeadStyle.DoubleStop && len >= Timing.TicksPerEighth * 2 )
				RenderLeadNote( _time.TickToSample( h.Tick ), _time.SpanSamples( h.Tick, len * 0.92 ),
					ScaleMidi( melBase, degree + 2 ), amp * 0.7f * NoteGain( h.Tick, h.Vel ),
					_time.SpanSeconds( h.Tick, len ) * 0.8, drive, vc );
			else if ( _prof.Lead == LeadStyle.Shred && len >= Timing.TicksPerBeat && rng.Chance( 0.18f ) )
				for ( int k = 1; k <= 3; k++ )
				{
					int m2 = ScaleMidi( melBase, degree + k );
					RenderLeadNote( _time.EvenSpan( h.Tick + len / 2, len / 2, (k - 1) / 3.0 ),
						_time.SpanSamples( h.Tick, len / 8.0 ), m2, amp * 0.8f * NoteGain( h.Tick, h.Vel ),
						_time.SpanSeconds( h.Tick, len / 8.0 ) * 0.8, drive, vc );
				}
		}
	}
}
