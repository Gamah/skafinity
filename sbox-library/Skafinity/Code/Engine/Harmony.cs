using System;
using System.Collections.Generic;

namespace Skafinity;

// Harmony: the per-genre scale / progression / bass-pattern tables and the degree→pitch
// maths that reads them. ScaleMidi wraps octaves rather than clamping, so a degree may run
// off either end of its scale and still land on a sane pitch.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Harmony ──
	// Major-leaning scales (Sublime / reggae sit in major & mixolydian mostly).
	static readonly int[][] Scales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted — was dorian; ska/reggae stays bright)
	};

	static readonly int[][] Progressions =
	{
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV
		new[] { 0, 3, 4, 4 }, // I–IV–V–V
		new[] { 0, 5, 3, 4 }, // I–vi–IV–V
		new[] { 0, 6, 3, 0 }, // I–bVII–IV–I (mixolydian)
		new[] { 5, 3, 0, 4 }, // vi–IV–I–V
		new[] { 0, 3, 0, 4 }, // I–IV–I–V
	};

	// Bass patterns: semitone offsets from the chord root per eighth; -99 = rest
	// (note sustains to the next onset). Slot 7 is the "approach" → walks to the
	// next chord. Mix of melodic / one-drop / rocking / busy ska.
	const int Rest = -99, Approach = 99;
	static readonly int[][] BassPatterns =
	{
		new[] { 0, Rest, 0, 12, Rest, 7, 5, Approach },   // sublime melodic
		new[] { Rest, Rest, 0, Rest, 0, Rest, 7, Approach }, // one-drop spacey
		new[] { 0, Rest, 7, Rest, 12, Rest, 7, Approach },   // rocking
		new[] { 0, 12, 0, 7, 0, 12, 7, Approach },           // busy ska
		new[] { 0, Rest, 0, Rest, 5, Rest, 7, Approach },    // root–fifth
	};

	// ── Rock harmony (Genre 1) ──
	// Darker, power-chord-friendly modes (minor / dorian / mixolydian) so rock doesn't
	// share ska's bright major themes. Picked with the SAME RNG draw as the ska tables, so
	// ska songs are byte-identical — only genre 1 reads these.
	// All minor-3rd modes: the RockProgressions are written/labelled as MINOR (i–♭VII–♭VI …), so
	// pairing them with a major-3rd mode (e.g. mixolydian) flipped the tonic major and the dark
	// rock vamp evaporated. Aeolian/dorian/phrygian keep a minor tonic — the progression reads as
	// intended — while still giving modal variety (dorian's ♮6, phrygian's ♭II colour). 4 entries
	// kept so the genre Pick stays at the same draw and other genres are byte-identical.
	static readonly int[][] RockScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 2, 3, 5, 7, 9, 10 }, // dorian (minor, brighter ♮6 — classic rock)
		new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian (dark)
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (weighted)
	};

	// Degrees are read against the (often minor) scale, so 5 = ♭VI, 6 = ♭VII, 3 = iv, 4 = v.
	static readonly int[][] RockProgressions =
	{
		new[] { 0, 6, 5, 6 }, // i–♭VII–♭VI–♭VII (driving rock vamp)
		new[] { 0, 5, 6, 0 }, // i–♭VI–♭VII–i
		new[] { 0, 6, 3, 0 }, // i–♭VII–IV–i (mixolydian rock)
		new[] { 0, 3, 6, 0 }, // i–iv–♭VII–i
		new[] { 0, 0, 6, 6 }, // i / ♭VII riff vamp
		new[] { 0, 3, 4, 0 }, // i–iv–v–i
	};

	// Driving root/octave eighths that lock to the kick — the rock engine room, vs ska's
	// syncopated off-beat one-drop bass.
	static readonly int[][] RockBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight eighth chug
		new[] { 0, Rest, 0, Rest, 0, Rest, 0, Approach },// quarter-note pulse
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 7, 0, 0, 0, 7, Approach },         // root–fifth gallop
		new[] { 0, Rest, 0, 0, Rest, 0, 12, Approach },  // syncopated driver
	};

	// ── Country harmony (Genre 2) ──
	// Bright and major — country lives in major / mixolydian. Same RNG draws as the other
	// tables, so songs in other genres are untouched.
	static readonly int[][] CountryScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	static readonly int[][] CountryProgressions =
	{
		new[] { 0, 3, 4, 0 }, // I–IV–V–I (the country backbone)
		new[] { 0, 0, 4, 4 }, // I–V vamp
		new[] { 0, 3, 0, 4 }, // I–IV–I–V
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV
		new[] { 0, 4, 0, 4 }, // I–V two-chord
	};

	// "Boom-chick" alternating root/fifth on the beats (the guitar/snare take the off "chick"),
	// walking up to the next chord on the approach.
	static readonly int[][] CountryBassPatterns =
	{
		new[] { 0, Rest, 7, Rest, 0, Rest, 7, Approach },  // alternating root–fifth
		new[] { 0, Rest, 7, Rest, 12, Rest, 7, Approach }, // root–fifth with the octave
		new[] { 0, Rest, 7, Rest, 0, Rest, 5, Approach },  // root–fifth, lean on the 4th
		new[] { 0, Rest, 4, Rest, 7, Rest, 5, Approach },  // walking-ish
	};

	// ── Metal harmony (Genre 3) ──
	// Dark and tight — natural minor / phrygian / harmonic minor for the menacing power-chord
	// riffs. Same RNG draws as the other tables.
	static readonly int[][] MetalScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian (the metal mode)
		new[] { 0, 2, 3, 5, 7, 8, 11 }, // harmonic minor
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (weighted)
	};

	// Degrees read against the (minor) scale: 5 = ♭VI, 6 = ♭VII, 1 = ♭II, 3 = iv.
	static readonly int[][] MetalProgressions =
	{
		new[] { 0, 5, 6, 0 }, // i–♭VI–♭VII–i
		new[] { 0, 6, 5, 6 }, // i–♭VII–♭VI–♭VII (driving)
		new[] { 0, 1, 0, 6 }, // i–♭II–i–♭VII (phrygian menace)
		new[] { 0, 0, 5, 6 }, // i pedal → ♭VI–♭VII
		new[] { 0, 6, 3, 0 }, // i–♭VII–iv–i
	};

	// Driving roots locked to the double-kick; octave pushes for the gallop.
	static readonly int[][] MetalBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight chug
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 0, 12, 0, 0, 0, Approach },        // syncopated octave
		new[] { 0, Rest, 0, 0, Rest, 0, 0, Approach },   // syncopated driver
	};

	// ── Punk harmony (Genre 4) ──
	// "Lean punk" / power-pop: bright and major (the opposite pole to rock's dark minor), riding
	// the anthemic four-chord turnarounds at speed. Same RNG draws as the other tables.
	static readonly int[][] PunkScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (the pop-punk default)
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian (a little grit on the ♭7)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	// Major degrees: 3 = IV, 4 = V, 5 = vi — the anthem turnarounds.
	static readonly int[][] PunkProgressions =
	{
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV (the pop-punk anthem)
		new[] { 5, 3, 0, 4 }, // vi–IV–I–V
		new[] { 0, 5, 3, 4 }, // I–vi–IV–V (50s changes, sped up)
		new[] { 0, 3, 4, 4 }, // I–IV–V–V
		new[] { 0, 4, 3, 4 }, // I–V–IV–V (three-chord drive)
	};

	// Driving root/octave eighths locked to the kick — same engine room as rock, pushed harder.
	static readonly int[][] PunkBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight eighth chug
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 7, 0, 0, 0, 7, Approach },         // root–fifth gallop
		new[] { 0, Rest, 0, Rest, 0, Rest, 0, Approach },// quarter-note pulse
	};

	// ── Pop harmony (Genre 5) ──
	// Modern synth/dance-pop: bright major / lydian over the ubiquitous four-chord loops, built
	// to sit on a four-on-the-floor kick. Same RNG draws as the other tables.
	static readonly int[][] PopScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 6, 7, 9, 11 }, // lydian (the sparkly ♯4 — synth-pop shimmer)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	static readonly int[][] PopProgressions =
	{
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV
		new[] { 5, 3, 0, 4 }, // vi–IV–I–V (the "Axis" loop)
		new[] { 0, 5, 3, 4 }, // I–vi–IV–V
		new[] { 0, 3, 0, 4 }, // I–IV–I–V (two-chord pulse)
	};

	// Four-on-the-floor synth bass: a steady root pulse with octave pops for bounce.
	static readonly int[][] PopBassPatterns =
	{
		new[] { 0, Rest, 0, Rest, 0, Rest, 0, Approach },  // root on every beat (locks the floor)
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },         // root with octave pops
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },           // straight eighth pulse
		new[] { 0, Rest, 0, 0, Rest, 0, 12, Approach },    // syncopated synth bass
	};

	// The genre's harmony tables (one RNG Pick each, so other genres stay byte-identical).
	static int[][] ScalesFor( int g ) => g switch
	{ 1 => RockScales, 2 => CountryScales, 3 => MetalScales, 4 => PunkScales, 5 => PopScales, _ => Scales };
	static int[][] ProgressionsFor( int g ) => g switch
	{ 1 => RockProgressions, 2 => CountryProgressions, 3 => MetalProgressions, 4 => PunkProgressions, 5 => PopProgressions, _ => Progressions };
	static int[][] BassPatternsFor( int g ) => g switch
	{ 1 => RockBassPatterns, 2 => CountryBassPatterns, 3 => MetalBassPatterns, 4 => PunkBassPatterns, 5 => PopBassPatterns, _ => BassPatterns };

	int ScaleMidi( int baseMidi, int degree )
	{
		int len = _scale.Length;
		int oct = (int)Math.Floor( degree / (double)len );
		return baseMidi + _scale[degree - oct * len] + 12 * oct;
	}
	int ChordRoot( int c ) => ScaleMidi( _rootMidi, _prog[c] );
}
