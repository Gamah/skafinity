using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>A section of the arrangement. Appended to, never reordered — the section TYPE is an
/// RNG key (see <see cref="MusicGen.SectionKey"/>), so its name is part of what a song is.</summary>
enum Section { Intro, Chorus, Verse, Ending, PreChorus, Bridge, Solo, Breakdown }

/// <summary>
/// One section instance in a song's form.
///
/// A section is not just a span of bars: it carries the state that makes it FEEL like a
/// different part of the song. Voices read <see cref="Energy"/> (thin the comp and drop the
/// hats in a verse, pile on in a chorus), the time base reads <see cref="TempoMul"/>, the
/// pattern layer reads <see cref="Feel"/> (half/double time), and the harmony reads
/// <see cref="KeyShift"/> (the final-chorus lift).
///
/// <see cref="BarBeats"/> is the anomalous-measure hook: a section is normally a run of bars in
/// the song's meter, but it may name a beat count per bar, which is how a 2/4 link bar lands
/// inside a 4/4 song.
/// </summary>
readonly struct Part
{
	public readonly Section Type;
	public readonly int Bars;
	public readonly int VerseIndex;

	/// <summary>0 = as sparse as the section ever gets, 1 = full band. Every voice scales its
	/// density and its level off this rather than inventing its own verse/chorus rule.</summary>
	public readonly float Energy;

	/// <summary>The RHYTHM SECTION's pattern-rate multiplier: 0.5 = half time, 2 = double time.
	/// NOT a tempo change — the grid is untouched, the figures stretch or compress against it, and
	/// the song's length does not move.
	///
	/// It is the rhythm section's and not the song's: <see cref="MusicGen.RenderTune"/> plays the
	/// tune at the nominal rate whatever this says. Half and double time are a contrast BETWEEN
	/// the band and the melody — the vocal holds its ground while the kit and the comp change gear
	/// — so a multiplier applied to both at once expresses nothing at all.</summary>
	public readonly float Feel;

	/// <summary>Tempo multiplier for the section, applied to the time base's per-tick delta.</summary>
	public readonly float TempoMul;

	/// <summary>Semitones the section's key is shifted by — the final-chorus lift.</summary>
	public readonly int KeyShift;

	/// <summary>True if the section's last two bars regroup into a hemiola (a figure whose length
	/// does not divide the bar), which is the cadential accelerando into the next section.</summary>
	public readonly bool Hemiola;

	/// <summary>Beats per bar, per bar — null when every bar is the song's meter. A short bar here
	/// is the "anomalous measure": a 2/4 inside a 4/4 context.</summary>
	public readonly int[] BarBeats;

	public Part( Section t, int bars, int verse = 0, float energy = 0.7f, float feel = 1f,
		float tempoMul = 1f, int keyShift = 0, bool hemiola = false, int[] barBeats = null )
	{
		Type = t; Bars = bars; VerseIndex = verse; Energy = energy; Feel = feel;
		TempoMul = tempoMul; KeyShift = keyShift; Hemiola = hemiola;
		BarBeats = barBeats;
	}

	/// <summary>The same section with a detail changed — what the per-song form draw works with.
	/// Everything a form variant AUTHORS (its energy contour, its feel changes, where the hemiola
	/// falls) travels through unchanged; only length and the key lift are drawn.</summary>
	public Part With( int bars, int keyShift ) =>
		new( Type, bars, VerseIndex, Energy, Feel, TempoMul, keyShift, Hemiola, BarBeats );
}

/// <summary>
/// The per-genre section maps.
///
/// One hardcoded list used to serve every genre, so a metal song and a pop song had
/// byte-identical FORM — sameyness no amount of note variation reaches. Each genre now names
/// its own run of sections, its own energy contour, and the places it does something a genre
/// two rows down would never do: metal drops into a half-time breakdown, pop lifts the last
/// chorus a tone, punk cuts a bar short on the way into a chorus, country and rock take a solo.
///
/// Every section here is a multiple of 4 bars in the song's own meter. The 2/4 link bar
/// <see cref="Part.BarBeats"/> exists for is deliberately UNUSED at the moment: dropping a beat
/// out of a bar under a melody reads as the song jumping to a downbeat early, because the tune is
/// a phrase and the missing beat is taken out of the middle of it. The MECHANISM is sound and it
/// is the hook the non-4/4 work needs — what is missing is the melodic half of it (a tune that
/// knows the bar it is being sung over is short, rather than one that is simply truncated). Wire
/// a short bar back in when that lands, not before.
///
/// A <c>Displace</c> field used to sit alongside <see cref="Part.Hemiola"/> — a constant tick
/// offset that pushed the chordal voices late for a whole section. It is gone, and it should not
/// come back in that shape. Three things were wrong with it and all three are structural:
/// it shifted LATE where real syncopation anticipates; it moved the guitar and keys but not the
/// bass, so every chord arrived as a flam with its own root; and being constant across a section
/// it never re-converged, so there was no dissonance to resolve — just an offset bounded by two
/// hard cuts. A push into a phrase seam is ONE GESTURE and the engine already writes it where it
/// belongs, in a figure's cells (the ska skank's stab on the "and of 4", the Charleston, the horn
/// answer). <see cref="Part.Hemiola"/> is the metric device that survives, because a figure whose
/// length does not divide the bar genuinely drifts and comes back.
/// </summary>
static class SongForm
{
	// Energies read as a contour rather than absolutes: what matters is that a verse sits under
	// its chorus and a breakdown falls out of the bottom.
	const float Low = 0.30f, Mid = 0.55f, Lift = 0.75f, Full = 1.00f;

	// Ska-punk — the third-wave shape, and it CLIMBS. The old form opened on the chorus and rotated
	// around it because the genre was tuned laid-back; ska-punk is dynamic music built on the drop
	// between a clean skank verse and a distorted chorus (GenreProfile.LoudComp reads the energy
	// column below to make that happen), so the form has to set that drop up rather than sit level.
	// The bridge is the breakdown, and it is where this genre spends its FEEL: the band drops to
	// half time behind a vocal that does not (Part.Feel), which is the third-wave breakdown and
	// the one place the style genuinely changes gear rather than changing technique. The
	//
	// THE CHORUS TAKES NO FEEL, AND THAT IS SETTLED RATHER THAN UNEXAMINED. The verse→chorus pivot
	// is already carried by GenreProfile.LoudComp swapping the skank for driven power chords, and
	// Feel = 2 on top of it is not a second helping of the same idea — it is a different and worse
	// one, because genre 0's band is counted DOUBLE already (the skank fires once per beat; see the
	// tempo comment in GenreProfile). Doubling the rhythm section's pattern rate over that band puts
	// the loud figures on the sixteenth: the densest of them (CompFigure.SkaPunkLoud's offbeat
	// answer, six onsets to the bar) lands twelve onsets a bar, which at the band's own 172 is 8.6
	// attacks a second and at the genre's tempo ceiling of 210 is over ten — downpicked, sustained,
	// for eight bars. That is past what a guitarist does and past what the style asks for: a
	// third-wave chorus is a HEAVIER part, not a faster one.
	//
	// The escape hatch would be bringing the band down until the doubling fits, and it is closed —
	// the band is anchored on a record ("Sell Out", converted into this engine's units), so moving
	// it to make a feel work would trade a measurement for a preference. The technique change IS the
	// gear change, and the feel stays where it is unambiguous: the half-time bridge.
	public static readonly Part[] SkaPunk =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.98f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid, hemiola: true ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Bridge,    8, energy: Low + 0.05f, feel: 0.5f ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Ending,    4, energy: Lift ),
	};

	// The alternate: no pre-chorus at all, so the verse drops straight into the chorus, and the
	// half-time bridge moves later. Same genre, different arc — a third-wave record does both.
	public static readonly Part[] SkaPunkB =
	{
		new( Section.Intro,   4, energy: Low, tempoMul: 0.98f ),
		new( Section.Verse,   8, 0, energy: Mid ),
		new( Section.Chorus,  8, energy: Full ),
		new( Section.Verse,   8, 1, energy: Mid, hemiola: true ),
		new( Section.Chorus,  8, energy: Full ),
		new( Section.Verse,   8, 2, energy: Mid ),
		new( Section.Bridge,  8, energy: Low + 0.05f, feel: 0.5f ),
		new( Section.Chorus,  8, energy: Full ),
		new( Section.Ending,  4, energy: Lift ),
	};

	// Rock — verse / pre-chorus / chorus, with the solo where a rock song puts it: after the
	// second chorus, before the last one. The pre-chorus is the transitional section, so it is the
	// one that regroups into a hemiola on the way into the chorus.
	public static readonly Part[] Rock =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.97f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift, hemiola: true ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Solo,      8, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Ending,    4, energy: Lift ),
	};

	// The alternate: no solo, and the bridge does the lifting instead — the other half of the
	// alt-rock playbook, where the third section is a drop rather than a guitar break.
	public static readonly Part[] RockB =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.97f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Bridge,    8, energy: Low + 0.1f, hemiola: true ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Ending,    4, energy: Lift ),
	};

	// Country — the plainest form of the six, and deliberately so: verse, chorus, verse, chorus,
	// a solo on the changes, then the last chorus a tone up (the Nashville lift).
	public static readonly Part[] Country =
	{
		new( Section.Intro,  4, energy: Low, tempoMul: 0.96f ),
		new( Section.Verse,  8, 0, energy: Mid ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Verse,  8, 1, energy: Mid ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Solo,   8, energy: Lift ),
		new( Section.Chorus, 8, energy: Full, keyShift: 2 ),
		new( Section.Ending, 4, energy: Lift ),
	};

	// The alternate: three verses and no solo — the storytelling shape, which is as country as the
	// solo one and is what the genre does when the words are the point.
	public static readonly Part[] CountryB =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.96f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 2, energy: Mid, hemiola: true ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full, keyShift: 2 ),
		new( Section.Ending,    4, energy: Lift ),
	};

	// Metal — the breakdown is the form's whole argument: everything drops to half time and the
	// energy falls away, then the solo climbs back out of it into the final chorus.
	public static readonly Part[] Metal =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.94f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Breakdown, 8, energy: Low, feel: 0.5f, tempoMul: 0.98f ),
		new( Section.Solo,      8, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Ending,    4, energy: Full ),
	};

	// The alternate: the breakdown moves to the last possible moment, straight into the final
	// chorus with no solo to climb back out through. The other way a metal record is built.
	public static readonly Part[] MetalB =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.94f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Solo,      8, energy: Lift ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Breakdown, 8, energy: Low, feel: 0.5f, tempoMul: 0.98f ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Ending,    4, energy: Full ),
	};

	// Punk — the shortest form, no pre-chorus, no solo, and a four-bar bridge that regroups on the
	// run into the last chorus. Everything is at the top of its energy already.
	public static readonly Part[] Punk =
	{
		new( Section.Intro,  4, energy: Mid, tempoMul: 1f ),
		new( Section.Verse,  8, 0, energy: Lift ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Verse,  8, 1, energy: Lift ),
		new( Section.Chorus, 8, energy: Full ),
		// HALF TIME. A great deal of punk does this — the band drops to half the backbeat while
		// the tempo does not move, which is what a mosh part IS, and it is the genre's only gear
		// change. Without it a punk song can only run flat out for its whole length, and the
		// tempo band takes the blame for what is really a form with no dynamic in it.
		new( Section.Bridge, 4, energy: Mid, feel: 0.5f, hemiola: true ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Ending, 4, energy: Full ),
	};

	// The alternate: no bridge at all — verse, chorus, verse, chorus, chorus, out. The shortest
	// form on the roster, which is the point of it.
	public static readonly Part[] PunkB =
	{
		new( Section.Intro,  4, energy: Mid, tempoMul: 1f ),
		new( Section.Verse,  8, 0, energy: Lift ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Verse,  8, 1, energy: Lift, hemiola: true ),
		new( Section.Chorus, 8, energy: Full ),
		// The same gear change, taken before the last chorus rather than in a bridge. There is no
		// key lift here for it to undercut — which is exactly why it had to move out of Pop.
		new( Section.Breakdown, 4, energy: Mid, feel: 0.5f ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Ending, 4, energy: Full ),
	};

	// Pop — the modern shape: pre-chorus into every chorus, a half-time "drop" bridge, and the
	// last chorus lifted a tone. The four-chord loop already IS the hypermeasure here
	// (GenreProfile.ChordBars = 1), so the form is what varies rather than the harmony.
	// THE MODULATION IS DIRECT — chorus straight into chorus a tone up, with nothing in between.
	// That is the device pop actually uses (the "truck driver's gear change"), and it works BECAUSE
	// it is abrupt: the lift is the whole gesture.
	//
	// A half-time breakdown used to sit in that gap, at energy 0.30, and it is the one thing in
	// this form that had to go. A modulation lifts a song relative to what came before it, so
	// dropping the floor out immediately beforehand leaves it nothing to lift FROM — the band goes
	// quiet and half-time, and then the key change arrives at a section the listener has just
	// stopped tracking. Two good devices, one after the other, cancelling.
	//
	// The drop is not lost: PopB carries it, mid-song, where a pop record puts it.
	public static readonly Part[] Pop =
	{
		new( Section.Intro,     4, energy: Low ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Chorus,    8, energy: Full, keyShift: 2 ),
		new( Section.Ending,    4, energy: Lift ),
	};
	// The alternate: this is where pop's half-time DROP lives, and it lives MID-SONG — after the
	// first chorus, which is where a record puts it and where it has a chorus to fall out of and a
	// verse to build back through. The modulation at the end is direct here too, for the reason
	// spelled out on Pop.
	public static readonly Part[] PopB =
	{
		new( Section.Intro,     4, energy: Low ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Breakdown, 4, energy: Low, feel: 0.5f ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift, hemiola: true ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Chorus,    8, energy: Full, keyShift: 2 ),
		new( Section.Ending,    4, energy: Lift ),
	};
}

// Song form. Part of the MusicGen engine — see MusicGen.cs.
public sealed partial class MusicGen
{
	// Extra seconds appended after the last bar so the ending's final tonic chord (and the
	// master reverb) can ring out naturally instead of being clipped at the buffer edge. The
	// ending ritard stretches the last bars, so the tail is scaled by the ritard alongside it
	// (see ComposePlan) rather than being a constant that the slowdown outruns.
	const float RingOutTail = 2.4f;

	/// <summary>How many seconds of ring-out every song reserves past its last bar — the tail the
	/// final chord and the reverb decay into.
	///
	/// PUBLISHED because a player has to know it. A crossfade is meant to overlap this tail; a
	/// player that guesses a longer fade starts the next song BEFORE the current one's final
	/// chord has even landed, and two songs at two tempos with two downbeats play at once. The
	/// ritard stretches the last bars, so the tail is scaled to match it (see ComposePlan).</summary>
	public static float RingOutSeconds => RingOutTail * (1f + (float)RitardAmount);

	/// <summary>
	/// THIS SONG's form: one of the genre's authored variants, with its details drawn.
	///
	/// It has to be built ONCE and cached (<see cref="_form"/>), because five places used to
	/// re-derive it — the composer, and four diagnostics. That was harmless while the answer was a
	/// constant; the moment it varies per song, a diagnostic that re-derives it is a bar ruler
	/// disagreeing with the song it is measuring, and `--score` and `--grid` go quietly wrong
	/// exactly when they are most needed.
	///
	/// Randomising a form outright would be the wrong fix and is not what happens here. A form is
	/// genre identity, which is why it lives in <see cref="GenreProfile"/>, and punk with a
	/// sixteen-bar solo is not punk. Two mechanisms instead: a FAMILY of authored variants, and
	/// details drawn over whichever one was chosen.
	///
	/// Non-lead voices are seeded by section TYPE so every chorus plays identical backing; the lead
	/// folds in the verse index so it evolves; the section-end fill is seeded by absolute index so
	/// no two fills repeat.
	/// </summary>
	/// <remarks>STATIC, and takes its profile: drawing a form needs no song, and the suite's
	/// invariants over it are arithmetic. Left as an instance method they cost a composed song
	/// each, which put 35 seconds on a 25-second harness for checks that render nothing.</remarks>
	internal static List<Part> DrawForm( GenreProfile prof, Rng rng )
	{
		var variant = rng.PickWeighted( prof.Forms, prof.FormWeights );
		bool lift = rng.Chance( prof.KeyLiftChance );
		int verseBars = prof.VerseBars[rng.WeightedIndex( prof.VerseBarWeights )];
		bool twice = rng.Chance( prof.DoubleFinalChorusChance );
		bool cut = rng.Chance( prof.TruncateFinalVerseChance );

		int lastVerse = -1, lastChorus = -1;
		for ( int i = 0; i < variant.Length; i++ )
		{
			if ( variant[i].Type == Section.Verse ) lastVerse = i;
			if ( variant[i].Type == Section.Chorus ) lastChorus = i;
		}

		var parts = new List<Part>();
		for ( int i = 0; i < variant.Length; i++ )
		{
			var p = variant[i];
			// The roll is taken for EVERY section, optional or not, so how many values a form costs
			// is the form's length rather than how many optional sections it happens to carry.
			bool drop = rng.Chance( prof.OptionalDropChance ) && Optional( p.Type );
			if ( drop ) continue;

			int bars = p.Type == Section.Verse ? verseBars : p.Bars;
			// A truncated final verse still has to be a multiple of four: the hypermeasure is why
			// the ending is four bars and not two, and it does not stop applying here.
			if ( cut && i == lastVerse ) bars = Math.Max( 4, bars - 4 );
			int shift = lift ? p.KeyShift : 0;
			parts.Add( p.With( bars, shift ) );
			// The final chorus comes round twice as a REPEATED SECTION, not a longer one — every
			// chorus must still agree about its length.
			if ( twice && i == lastChorus ) parts.Add( p.With( p.Bars, shift ) );
		}
		return parts;
	}

	/// <summary>The sections a song may do without. Which one a genre has is most of what
	/// distinguishes the six forms from each other, so dropping one is a song that does without
	/// rather than a genre that does.</summary>
	static bool Optional( Section s ) =>
		s is Section.PreChorus or Section.Bridge or Section.Solo or Section.Breakdown;

	/// <summary>The section's RNG key — what makes every chorus play the same backing.</summary>
	internal static string SectionKey( Section s ) => s switch
	{
		Section.Intro => "intro",
		Section.Chorus => "chorus",
		Section.Verse => "verse",
		Section.PreChorus => "prechorus",
		Section.Bridge => "bridge",
		Section.Solo => "solo",
		Section.Breakdown => "breakdown",
		_ => "ending",
	};
}
