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

	// ── Ska: the skank chop on the offbeats. The second figure answers itself across two bars
	// (the push on the "and of 4" pulls into bar 2), which one-bar tables could not express.
	public static readonly Pattern[] Ska =
	{
		E( R, Stab, R, Stab, R, Stab, R, Stab ),
		E( R, Stab, R, Stab, R, Stab, R, Stab,
		   R, Stab, R, Stab, R, Stab, Stab, Stab ),
		E( R, Stab, R, Stab, R, Stab, R, R,
		   R, Stab, R, Stab, R, Stab, R, Stab ),
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
	};

	// ── Hemiola: the cadential regrouping. Three eighths long, so it does NOT divide the bar —
	// the figure and the bar line pull apart and re-converge, which is Biamonte's grouping
	// dissonance and the reason Pattern carries its own length at all. Any chordal voice can
	// swap to this for the last bars of a section (see MusicGen.RenderSection).
	public static readonly Pattern Hemiola = E( Stab, R, Stab );
}
