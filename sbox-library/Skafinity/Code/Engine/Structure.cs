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

	/// <summary>Pattern-rate multiplier: 0.5 = half time, 2 = double time. NOT a tempo change —
	/// the grid is untouched, the figures stretch or compress against it.</summary>
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

	// Ska — the third-wave shape, and it CLIMBS. The old form opened on the chorus and rotated
	// around it because the genre was tuned laid-back; ska-punk is dynamic music built on the drop
	// between a clean skank verse and a distorted chorus (GenreProfile.LoudComp reads the energy
	// column below to make that happen), so the form has to set that drop up rather than sit level.
	// The bridge is the breakdown — back to skank and bass before the last chorus.
	public static readonly Part[] Ska =
	{
		new( Section.Intro,     4, energy: Low, tempoMul: 0.98f ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid, hemiola: true ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Bridge,    8, energy: Low + 0.05f ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Ending,    4, energy: Lift ),
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

	// Punk — the shortest form, no pre-chorus, no solo, and a four-bar bridge that regroups on the
	// run into the last chorus. Everything is at the top of its energy already.
	public static readonly Part[] Punk =
	{
		new( Section.Intro,  4, energy: Mid, tempoMul: 1f ),
		new( Section.Verse,  8, 0, energy: Lift ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Verse,  8, 1, energy: Lift ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Bridge, 4, energy: Mid, hemiola: true ),
		new( Section.Chorus, 8, energy: Full ),
		new( Section.Ending, 4, energy: Full ),
	};

	// Pop — the modern shape: pre-chorus into every chorus, a half-time "drop" bridge, and the
	// last chorus lifted a tone. The four-chord loop already IS the hypermeasure here
	// (GenreProfile.ChordBars = 1), so the form is what varies rather than the harmony.
	public static readonly Part[] Pop =
	{
		new( Section.Intro,     4, energy: Low ),
		new( Section.Verse,     8, 0, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Verse,     8, 1, energy: Mid ),
		new( Section.PreChorus, 4, energy: Lift ),
		new( Section.Chorus,    8, energy: Full ),
		new( Section.Breakdown, 4, energy: Low, feel: 0.5f ),
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

	/// <summary>The genre's section map — its form, not a shared list. Non-lead voices are seeded
	/// by section TYPE so every chorus plays identical backing; the lead folds in the verse index
	/// so it evolves; the section-end fill is seeded by absolute index so no two fills repeat.
	/// </summary>
	internal static List<Part> BuildStructure( int genre ) =>
		new( GenreProfile.For( genre ).Form );

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
