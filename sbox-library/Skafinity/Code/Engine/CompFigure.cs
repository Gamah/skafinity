using System;

namespace Skafinity;

/// <summary>
/// The comp figures — what rhythm each genre's chordal voices actually play.
///
/// This was the loudest remaining duplication in the engine: one <c>KeysOnsets {0,3,4,7}</c>
/// served rock, country and pop, and one rhythm-guitar loop served rock, country and punk off a
/// single <c>country</c> bool. Three genres at a time played the SAME comping rhythm, whatever
/// harmony sat underneath — and the comp is most of what a listener hears as "the band".
///
/// A figure is a <see cref="Pattern"/>, so it owns its length: the ska horn answer is two bars
/// because it IS a call and response, the rock riff is two bars because a riff is a motif rather
/// than a bar, and punk is one bar because that is the whole idea of punk.
///
/// CELL VALUES say how the hit is played; <see cref="CompStyle"/> says what the voice does with
/// that. <see cref="Tone"/> cells name a single chord tone by index (for arpeggios and
/// alternating figures) instead of the whole voicing.
/// </summary>
static class CompFigure
{
	/// <summary>Full voicing, allowed to ring into the next hit.</summary>
	public const int Ring = 0;
	/// <summary>Full voicing, short — a stab or a chop.</summary>
	public const int Stab = 1;
	/// <summary>Root only, muted — the palm-muted chug between accents.</summary>
	public const int Mute = 2;
	/// <summary>A single chord tone: <c>Tone(0)</c> is the root, <c>Tone(1)</c> the next voice up.
	/// Wraps over the voicing, so an arpeggio can just count upward.</summary>
	public static int Tone( int i ) => 10 + i;
	public static bool IsTone( int v ) => v >= 10;
	public static int ToneIndex( int v ) => v - 10;

	const int R = Harmony.Rest;

	static Pattern E( params int[] cells ) => Pattern.Eighths( cells );
	static Pattern S( params int[] cells ) => Pattern.Sixteenths( cells );
	static Pattern T( params int[] cells ) => Pattern.ThirtySeconds( cells );

	// ── Ska-punk: the skank chop on the offbeats. The second figure answers itself across two bars
	// (the push on the "and of 4" pulls into bar 2), which one-bar tables could not express.
	public static readonly Pattern[] SkaPunk =
	{
		E( R, Stab, R, Stab, R, Stab, R, Stab ),
		E( R, Stab, R, Stab, R, Stab, R, Stab,
		   R, Stab, R, Stab, R, Stab, Stab, Stab ),
		E( R, Stab, R, Stab, R, Stab, R, R,
		   R, Stab, R, Stab, R, Stab, R, Stab ),
		// The flick: the last chop of the two-bar phrase doubles at the THIRTY-SECOND, so the
		// skank pushes into the next bar instead of just stopping. Two notes off a wrist that is
		// already moving — the same hand that plays the chop plays the flick.
		T( R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,R,R,R,
		   R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,R,R,R,
		   R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,R,R,R,
		   R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,Stab,R,R ),
	};

	// ── Ska-punk (loud): what the same voice plays once the section is loud — see GenreProfile.LoudComp.
	// The skank stops. These are the guitar part of a third-wave chorus: on the beat, ringing, and
	// through a driven amp (so RhythmGtrTone's drive takes the third out and they land as power
	// chords). The offbeat does not vanish from the song — the horns and the kit still carry it,
	// which is what keeps a loud ska chorus from simply being a punk chorus.
	public static readonly Pattern[] SkaPunkLoud =
	{
		E( Ring, R, Stab, R, Ring, R, Stab, R ),
		// Two bars, the second pushing back into the first: the chorus figure that answers itself.
		E( Ring, R, R, Stab, Ring, R, Stab, R,
		   Ring, R, R, Stab, Ring, Stab, Stab, R ),
		// The one that keeps the offbeat inside the loud part — downbeat power chord, offbeat
		// answer. This is the figure that still sounds like ska with the gain on.
		E( Ring, Stab, R, Stab, Ring, Stab, R, Stab ),
	};

	// ── Rock: a real two-bar riff motif. NOT an every-eighth chug — the hits are placed, they
	// ring, and the second bar answers the first.
	public static readonly Pattern[] Rock =
	{
		E( Ring, R, R, Stab, Ring, R, Stab, R,
		   Ring, R, R, Stab, Ring, R, Stab, Stab ),
		E( Ring, R, Stab, R, R, Ring, R, Stab,
		   Ring, R, Stab, R, R, Ring, Stab, R ),
		E( Ring, Mute, Mute, Ring, R, Mute, Ring, R,
		   Ring, Mute, Mute, Ring, R, Ring, R, Stab ),
		// The first figure with a THIRTY-SECOND pickup pushing the phrase back round to bar 1 — a
		// riff's pickup is the oldest ornament in rock guitar. Note it REPLACES the last stab
		// rather than joining it: an ornament that adds notes makes the comp louder as well as
		// busier, and the comp is the bed (see the mix balance in the engine suite).
		T( Ring,R,R,R, R,R,R,R, R,R,R,R, Stab,R,R,R,
		   Ring,R,R,R, R,R,R,R, Stab,R,R,R, R,R,R,R,
		   Ring,R,R,R, R,R,R,R, R,R,R,R, Stab,R,R,R,
		   Ring,R,R,R, R,R,R,R, Stab,R,R,R, R,R,Stab,Stab ),
	};

	// ── Rock keys: the syncopated Charleston push. Kept as its own voice with its own figure so
	// the two interlock rather than doubling.
	public static readonly Pattern[] RockKeys =
	{
		E( Ring, R, R, Stab, Ring, R, R, Stab ),
		E( Ring, R, R, Stab, R, Ring, R, R,
		   R, Stab, Ring, R, R, Stab, R, R ),
	};

	// ── Country: the "chick" — a clean strum on every offbeat, over the bass's "boom". This is
	// the half of boom-chick the guitar owns; the bass tables own the other half.
	public static readonly Pattern[] Country =
	{
		E( R, Stab, R, Stab, R, Stab, R, Stab ),
		E( R, Stab, R, Stab, R, Stab, R, Stab,
		   R, Stab, R, Stab, R, Stab, Stab, R ),
		// Chicken-pickin': a chick snaps a THIRTY-SECOND pull-off after it — one plucked note and
		// one that costs the picking hand nothing, which is why the gesture survives at country's
		// fastest and its slowest alike. TWICE in two bars, not on every chick: a second note on
		// every one of them is a different instrument rather than an ornament, which is the same
		// lesson the lead's double-stops learned.
		T( R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,R,R,R,
		   R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,R,R,R,
		   R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,Stab,R,R,
		   R,R,R,R, Stab,R,R,R, R,R,R,R, Stab,Stab,R,R ),
	};

	// ── Country keys: honky-tonk piano stabs on 2 and 4 — it answers the backbeat rather than
	// keeping time, which is what stops it from doubling the guitar.
	public static readonly Pattern[] CountryKeys =
	{
		E( R, R, Stab, R, R, R, Stab, R ),
		E( R, R, Stab, R, R, R, Stab, Stab,
		   R, R, Stab, R, Stab, R, Stab, R ),
	};

	// ── Punk: downstroke eighths, one chord per bar, nothing else. The variation is that the
	// four-bar phrase drops a hit at the end to breathe before the turnaround.
	public static readonly Pattern[] Punk =
	{
		E( Ring, Ring, Ring, Ring, Ring, Ring, Ring, Ring ),
		E( Ring, Ring, Ring, Ring, Ring, Ring, Ring, Ring,
		   Ring, Ring, Ring, Ring, Ring, Ring, Ring, Ring,
		   Ring, Ring, Ring, Ring, Ring, Ring, Ring, Ring,
		   Ring, Ring, Ring, Ring, Ring, Ring, R, Ring ),
		// The turnaround flurry: two bars of downstrokes, and the last eighth breaks into
		// THIRTY-SECONDS on the way back round. Punk's whole idea is that nothing lets up, so the
		// ornament is where it hands over, not scattered through the bar.
		T( Ring,R,R,R, Ring,R,R,R, Ring,R,R,R, Ring,R,R,R,
		   Ring,R,R,R, Ring,R,R,R, Ring,R,R,R, Ring,R,R,R,
		   Ring,R,R,R, Ring,R,R,R, Ring,R,R,R, Ring,R,R,R,
		   Ring,R,R,R, Ring,R,R,R, Ring,R,R,R, Ring,Ring,Ring,Ring ),
	};

	// ── Metal: the palm-muted gallop, authored at the sixteenth it actually lives on. Ring hits
	// are the power-chord accents; everything between them is the muted root.
	public static readonly Pattern[] Metal =
	{
		S( Ring, Mute, Mute, Mute, Ring, Mute, Mute, Mute,
		   Ring, Mute, Mute, Mute, Ring, Mute, Mute, Mute ),
		S( Ring, Mute, Mute, Ring, Mute, Mute, Ring, Mute,
		   Mute, Ring, Mute, Mute, Ring, Mute, Mute, Mute,
		   Ring, Mute, Mute, Ring, Mute, Mute, Ring, Mute,
		   Mute, Ring, Mute, Mute, Ring, Ring, Mute, Mute ),   // the classic "gallop" (2 bars)
		S( Ring, R, Mute, Mute, Ring, R, Mute, Mute,
		   Ring, R, Mute, Mute, Ring, Ring, R, R ),
		// Authored at the THIRTY-SECOND so the chug can burst. Most of the bar is the same
		// sixteenth chug as the figures above (a cell, then a rest cell); the last beat runs
		// 32nds into the bar line. That is the tremolo gesture as a player uses it — a flurry
		// pulling into the next downbeat — rather than a bar of it, and being a handful of notes
		// it stays a gesture at the top of the genre's band as much as the bottom.
		T( Ring, R, Mute, R, Mute, R, Mute, R,
		   Ring, R, Mute, R, Mute, R, Mute, R,
		   Ring, R, Mute, R, Mute, R, Mute, R,
		   Ring, R, Mute, R, Mute, Mute, Mute, Mute ),
	};

	// ── Pop: a held pad. One hit a bar, ringing the whole way — the harmony is a bed here, not a
	// rhythm part, which is exactly what the arp on top needs.
	public static readonly Pattern[] Pop =
	{
		E( Ring, R, R, R, R, R, R, R ),
		E( Ring, R, R, R, R, R, R, R,
		   Ring, R, R, R, R, R, Ring, R ),
	};

	// ── Pop arp: sixteenths climbing the voicing. The tone indices wrap over whatever voicing
	// the song drew, so an add9 arp reaches the 9th without the figure knowing what a 9th is.
	public static readonly Pattern[] PopArp =
	{
		S( Tone( 0 ), Tone( 1 ), Tone( 2 ), Tone( 3 ), Tone( 2 ), Tone( 1 ), Tone( 2 ), Tone( 3 ),
		   Tone( 0 ), Tone( 1 ), Tone( 2 ), Tone( 3 ), Tone( 2 ), Tone( 1 ), Tone( 2 ), Tone( 1 ) ),
		S( Tone( 0 ), R, Tone( 2 ), Tone( 1 ), Tone( 0 ), R, Tone( 2 ), Tone( 3 ),
		   Tone( 0 ), R, Tone( 2 ), Tone( 1 ), Tone( 3 ), Tone( 2 ), Tone( 1 ), Tone( 0 ) ),
		// The sixteenth arp with a THIRTY-SECOND run home over the last beat — the synth-pop
		// flourish that resolves the two-bar phrase. A sequencer plays it and so does a keyboard
		// player; either way it is one beat of the figure, not the figure's rate.
		T( Tone(0),R, Tone(1),R, Tone(2),R, Tone(3),R,
		   Tone(2),R, Tone(1),R, Tone(2),R, Tone(3),R,
		   Tone(0),R, Tone(1),R, Tone(2),R, Tone(3),R,
		   Tone(2),R, Tone(1),R, Tone(3),Tone(2), Tone(1),Tone(0) ),
	};

	// ── Hemiola: the cadential regrouping. Three eighths long, so it does NOT divide the bar —
	// the figure and the bar line pull apart and re-converge, which is Biamonte's grouping
	// dissonance and the reason Pattern carries its own length at all. Any chordal voice can
	// swap to this for the last bars of a section (see MusicGen.RenderSection).
	public static readonly Pattern Hemiola = E( Stab, R, Stab );
}
