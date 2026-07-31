using System;
using System.Collections.Generic;

namespace Skafinity;

// The kit's patterns: which drum lands where. The per-song groove, the backbeat kick
// accents, and the phrase-end fill.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Drums ──
	// Render a bar of kit. On a section's last bar (fillEnd) the closing beat is replaced by a
	// fill — driven by its own RNG streams so every section's fill is different even when the
	// groove before it is identical.
	void RenderDrumBar( int barStart, int spe, bool fillEnd, Rng noise, Rng fillRng, Rng fillNoise )
	{
		// Knob ceiling was too frantic: scale so DRUM BUSY 100% reads as the old 75%.
		float busy = Math.Clamp( _c.DrumBusy, 0f, 1f ) * 0.75f;
		int six = spe / 2;
		int hatEnd = fillEnd ? 6 : EighthsPerBar;         // hats stop where the fill begins

		// closed hats on eighths (open on the "and of 4"); busy fills the gaps with
		// quieter sixteenth-note hats (constant 16th chatter at the top of the range). On
		// ride songs the ride cymbal carries the eighth pulse instead (bell on the beats), with
		// the open hat still punctuating the "and of 4".
		for ( int e = 0; e < hatEnd; e++ )
		{
			int at = Swung( barStart, spe, e );
			// Pop pumps an open hat on every offbeat (the classic four-on-the-floor "ts-ts-ts");
			// every other style opens only on the "and of 4".
			bool open = e == 7 || (_drumStyle == 4 && e % 2 == 1);
			float amp = e % 2 == 1 ? _c.HatVol : _c.HatVol * 0.6f;
			if ( _ride && !open )
				RenderRide( at, e % 2 == 0, amp, noise );    // bell accent on the downbeats
			else
				RenderHat( at, open, amp, noise );
			if ( !open && six > 0 && noise.Chance( busy ) )
			{
				int sixAt = Swung( barStart, spe, e + 0.5 );
				if ( _ride ) RenderRide( sixAt, false, _c.HatVol * 0.4f, noise );
				else RenderHat( sixAt, false, _c.HatVol * 0.4f, noise );
			}
		}

		if ( fillEnd )
		{
			RenderKickSnareGroove( barStart, spe, 0, 6, busy, noise );   // first 3 beats normal
			RenderFill( barStart, spe, 6, fillNoise, fillRng );
			return;
		}
		RenderKickSnareGroove( barStart, spe, 0, EighthsPerBar, busy, noise );
	}

	// Per-song kick accents for the straight backbeat: eighths (beyond the beat-1 & 3 anchors)
	// the kick leans into. e1 = "and of 1", e3 = "and of 2", e5 = "and of 3", e6 = beat 4,
	// e7 = "and of 4". One set is picked per song, then each accent is rolled per bar so the
	// groove breathes instead of stamping the same kick pattern every bar — the main nuance
	// lever that keeps rock/country from all sharing one mechanical backbeat.
	static readonly int[][] BackbeatKickAccents =
	{
		new int[0],          // bone-dry: just 1 & 3
		new[] { 3 },         // push into the snare ("and of 2")
		new[] { 7 },         // pickup into the next bar ("and of 4")
		new[] { 3, 7 },      // push + pickup
		new[] { 6 },         // driving beat-4 kick
		new[] { 5, 7 },      // syncopated "and of 3" + pickup
		new[] { 3, 6 },      // push into 3 + beat-4 drive
	};

	void RenderKickSnareGroove( int barStart, int spe, int from, int to, float busy, Rng noise )
	{
		int six = spe / 2;
		for ( int e = from; e < to; e++ )
		{
			int at = Swung( barStart, spe, e );
			int sixAt = six > 0 ? Swung( barStart, spe, e + 0.5 ) : at;
			switch ( _drumStyle )
			{
				case 0: // one-drop: kick + snare together on beat 3
					if ( e == 4 ) { RenderKick( at, noise ); RenderSnare( at, noise, false ); }
					else if ( e == 2 && noise.Chance( _c.GhostSnareChance * (0.4f + busy) ) ) RenderSnare( at, noise, true );
					break;
				case 1: // steppers: kick every beat, snare on 2 & 4
					if ( e % 2 == 0 ) RenderKick( at, noise );
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					break;
				case 3: // metal double-kick: 16th-note kick gallop + crashing, snare backbeat
					if ( e == 0 && noise.Chance( 0.55f ) ) RenderCrash( at, noise, noise.Chance( 0.35f ) );
					RenderKick( at, noise );
					if ( six > 0 ) RenderKick( sixAt, noise ); // the second pedal → the 16th gallop
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					break;
				case 4: // pop four-on-the-floor: kick on every beat, backbeat snare/clap on 2 & 4
					if ( e % 2 == 0 ) RenderKick( at, noise );
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					else if ( noise.Chance( _c.GhostSnareChance * busy * 0.5f ) ) RenderSnare( at, noise, true );
					break;
				default: // straight backbeat — anchors on beats 1 & 3, plus this song's kick
					     // accents, each humanised per bar so the groove breathes
					bool kick = e == 0 || e == 4;
					if ( !kick && Array.IndexOf( _kickAccents, e ) >= 0 )
						kick = noise.Chance( 0.82f );                 // mostly play the accent, occasionally lay out
					else if ( !kick && e == 3 )
						kick = noise.Chance( _c.KickSyncChance * (0.4f + busy) ); // stray push into beat 3
					if ( kick ) RenderKick( at, noise );
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					else if ( noise.Chance( _c.GhostSnareChance * busy ) ) RenderSnare( at, noise, true );
					break;
			}
			// Busy fills the "e/a" sixteenths between hits: more snare as busy rises, and —
			// when the tone leans low — toms too. (Busy → snare + toms; tone → tom vs cymbal.)
			// Metal already fills every 16th with the double-kick, so it skips the ghost layer.
			if ( _drumStyle != 3 && six > 0 && e != 4 && noise.Chance( _c.GhostSnareChance * busy ) )
			{
				if ( noise.Chance( (1f - _drumTone) * 0.5f ) )
					RenderTom( sixAt, 150f + 40f * (e & 1), noise );
				else
					RenderSnare( sixAt, noise, true );
			}
		}
	}

	// Tom/snare roll across the last beat (two eighths). Straight = four 16ths;
	// triplet (TripletChance) = six even subdivisions for a rolling shuffle feel.
	// The fill rolls across the last beat — the two eighths starting at baseE (6) up to the bar
	// line — on the same swung grid as the rest of the band.
	void RenderFill( int barStart, int spe, int baseE, Rng noise, Rng rng )
	{
		// straight = four 16ths; triplet = either an eighth-note triplet (3) or a
		// faster 16th-note triplet (6) across the beat.
		int n = rng.Chance( _c.TripletChance ) ? (rng.Chance( 0.5f ) ? 3 : 6) : 4;
		int step = (spe * 2) / n;
		float[] toms = { 260f, 215f, 175f, 145f, 120f, 100f };
		// Half the hits stay snare so it still reads as a drum fill; the rest are biased by
		// DrumTone — toms when the tone leans low, cymbals (ride hits) when it leans high.
		for ( int i = 0; i < n; i++ )
		{
			int t = Swung( barStart, spe, baseE + i * 2.0 / n );
			if ( rng.Chance( 0.5f ) ) RenderSnare( t, noise, false );
			else if ( rng.Chance( _drumTone ) ) RenderRide( t, false, _c.HatVol, noise );
			else RenderTom( t, toms[i], noise );  // pan derived from pitch inside RenderTom
		}
		// crash into the downbeat (lands on the bar line, an on-beat anchor → dead straight) — a
		// bright crash or a darker, washier crash, picked off the fill stream so the cymbal colour
		// varies section to section.
		RenderCrash( Swung( barStart, spe, baseE + 2.0 ), noise, rng.Chance( 0.4f ) );
	}
}
