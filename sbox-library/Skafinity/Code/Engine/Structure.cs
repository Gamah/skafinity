using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>A section of the arrangement.</summary>
enum Section { Intro, Chorus, Verse, Ending }

/// <summary>One section instance in a song's form: its type, its length in bars, and — for a
/// verse — which verse it is.</summary>
readonly struct Part
{
	public readonly Section Type;
	public readonly int Bars;
	public readonly int VerseIndex;

	public Part( Section t, int bars, int verse ) { Type = t; Bars = bars; VerseIndex = verse; }
}

// Song form. Part of the MusicGen engine — see MusicGen.cs.
public sealed partial class MusicGen
{
	// ── Song structure ──
	// A song is an ordered list of sections. Hardcoded for now (will be RNG-generated once
	// there are more part types); the fixed run is intro → chorus → verse(0) → chorus →
	// verse(1) → chorus → ending. Non-lead voices are seeded by section TYPE so every chorus
	// (and both verses) play identical backing; the lead is seeded by type + verse index so
	// it evolves across the Nth verse; the section-end fill is seeded by absolute index so
	// every section closes with a different fill.
	//
	// This one list serves every genre — a metal song and a pop song currently have identical
	// form, which is the next thing to fix here. Buffer sizing already sums per-part, so a
	// per-genre section map and per-section lengths are both tractable. See PLAN.md.

	// Extra seconds appended after the last bar so the ending's final tonic chord (and the
	// master reverb) can ring out naturally instead of being clipped at the buffer edge.
	const float RingOutTail = 2.4f;

	internal static List<Part> BuildStructure() => new()
	{
		new Part( Section.Intro,  4, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 1 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Ending, 2, 0 ),
	};

	/// <summary>The section's RNG key — what makes every chorus play the same backing.</summary>
	internal static string SectionKey( Section s ) => s switch
	{
		Section.Intro => "intro",
		Section.Chorus => "chorus",
		Section.Verse => "verse",
		_ => "ending",
	};
}
