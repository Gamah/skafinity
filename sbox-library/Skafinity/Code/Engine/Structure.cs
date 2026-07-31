using System;
using System.Collections.Generic;

namespace Skafinity;

// Song form: the section enum, a Part, and the fixed arrangement every genre currently
// shares. Buffer sizing sums per-part, so a per-section length is already tractable.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Song structure ──
	// A song is an ordered list of sections. Hardcoded for now (will be RNG-generated once
	// there are more part types); the fixed run is intro → chorus → verse(0) → chorus →
	// verse(1) → chorus → ending. Non-lead voices are seeded by section TYPE so every chorus
	// (and both verses) play identical backing; the lead is seeded by type + verse index so
	// it evolves across the Nth verse; the section-end fill is seeded by absolute index so
	// every section closes with a different fill.
	enum Section { Intro, Chorus, Verse, Ending }

	// Extra seconds appended after the last bar so the ending's final tonic chord (and the
	// master reverb) can ring out naturally instead of being clipped at the buffer edge.
	const float RingOutTail = 2.4f;

	readonly struct Part
	{
		public readonly Section Type; public readonly int Bars; public readonly int VerseIndex;
		public Part( Section t, int bars, int verse ) { Type = t; Bars = bars; VerseIndex = verse; }
	}

	static List<Part> BuildStructure() => new()
	{
		new Part( Section.Intro,  4, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 1 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Ending, 2, 0 ),
	};

	static string SectionKey( Section s ) => s switch
	{
		Section.Intro => "intro",
		Section.Chorus => "chorus",
		Section.Verse => "verse",
		_ => "ending",
	};
}
