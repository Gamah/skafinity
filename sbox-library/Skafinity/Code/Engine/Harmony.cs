using System;

namespace Skafinity;

/// <summary>
/// Harmony: the per-genre scale / progression / bass-pattern tables, and the degree→pitch
/// maths that reads them.
///
/// A progression entry is a SCALE DEGREE, not a semitone — so the same progression table
/// reads as major or minor depending on the scale drawn alongside it, and a degree of 5
/// against a minor scale is a ♭VI. Degrees are unbounded: <see cref="ScaleMidi"/> wraps
/// octaves rather than clamping, so running off either end of a scale still lands on a sane
/// pitch. That is what lets a progression be any length — nothing here assumes four.
///
/// Stateless by design: every entry point takes the scale it should read. MusicGen keeps thin
/// instance wrappers that supply the song's own scale. Which table a genre draws from is
/// <see cref="GenreProfile"/>'s business, not this file's — these are just the tables.
///
/// NO TWO GENRES SHARE MORE THAN ONE PROGRESSION. Sharing them is how six genres came to draw
/// byte-identical changes (I–V–vi–IV was in four of them), so the tables are pruned to keep the
/// genres apart and the engine test asserts it. Adding an entry means checking it against the
/// other five.
/// </summary>
static class Harmony
{
	/// <summary>Bass-pattern cell: no onset (the previous note sustains).</summary>
	public const int Rest = -99;

	/// <summary>Bass-pattern cell: walk into the next chord instead of playing a fixed
	/// offset.</summary>
	public const int Approach = 99;

	// ── Ska harmony (Genre 0) ──
	// Major-leaning scales (Sublime / reggae sit in major & mixolydian mostly).
	public static readonly int[][] SkaScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted — was dorian; ska/reggae stays bright)
	};

	// The bright turnarounds ska keeps: the mixolydian ♭VII move and the ii–V that punk and pop
	// never touch. The anthem loops (I–V–vi–IV, vi–IV–I–V) moved out to punk and pop, which is
	// where they actually belong.
	public static readonly int[][] SkaProgressions =
	{
		new[] { 0, 3, 4, 3 }, // I–IV–V–IV
		new[] { 0, 6, 3, 0 }, // I–♭VII–IV–I (mixolydian)
		new[] { 0, 5, 1, 4 }, // I–vi–ii–V (the 50s/rocksteady turnaround)
		new[] { 0, 3, 0, 4 }, // I–IV–I–V
		new[] { 5, 4, 3, 4 }, // vi–V–IV–V (the minor-tinged vamp)
		new[] { 0, 0, 3, 4 }, // I pedal → IV–V
	};

	// Bass patterns: semitone offsets from the chord root per eighth; -99 = rest
	// (note sustains to the next onset). Slot 7 is the "approach" → walks to the
	// next chord. Mix of melodic / one-drop / rocking / busy ska.
	public static readonly int[][] SkaBassPatterns =
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
	public static readonly int[][] RockScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 2, 3, 5, 7, 9, 10 }, // dorian (minor, brighter ♮6 — classic rock)
		new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian (dark)
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (weighted)
	};

	// Degrees are read against the (often minor) scale, so 5 = ♭VI, 6 = ♭VII, 3 = iv, 4 = v,
	// 2 = ♭III. Rock and metal both live in minor, so they had three progressions in common and
	// could draw the identical vamp; the ♭VI/♭II-leaning ones are metal's now and rock keeps the
	// ♭VII-driven ones.
	public static readonly int[][] RockProgressions =
	{
		new[] { 0, 5, 6, 0 }, // i–♭VI–♭VII–i
		new[] { 0, 3, 6, 0 }, // i–iv–♭VII–i
		new[] { 0, 0, 6, 6 }, // i / ♭VII riff vamp
		new[] { 0, 6, 0, 3 }, // i–♭VII–i–iv
		new[] { 0, 6, 3, 4 }, // i–♭VII–iv–v
		new[] { 0, 2, 3, 6 }, // i–♭III–iv–♭VII
	};

	// Driving root/octave eighths that lock to the kick — the rock engine room, vs ska's
	// syncopated off-beat one-drop bass.
	public static readonly int[][] RockBassPatterns =
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
	public static readonly int[][] CountryScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	// Country is the plainest of the major genres — I, IV and V and not much else — so it keeps
	// the backbone and the two-chord vamps, and the anthem loops go to punk/pop.
	public static readonly int[][] CountryProgressions =
	{
		new[] { 0, 3, 4, 0 }, // I–IV–V–I (the country backbone)
		new[] { 0, 0, 4, 4 }, // I–V vamp
		new[] { 0, 4, 3, 0 }, // I–V–IV–I
		new[] { 0, 3, 0, 3 }, // I–IV two-chord
		new[] { 0, 4, 0, 4 }, // I–V two-chord
	};

	// "Boom-chick" alternating root/fifth on the beats (the guitar/snare take the off "chick"),
	// walking up to the next chord on the approach.
	public static readonly int[][] CountryBassPatterns =
	{
		new[] { 0, Rest, 7, Rest, 0, Rest, 7, Approach },  // alternating root–fifth
		new[] { 0, Rest, 7, Rest, 12, Rest, 7, Approach }, // root–fifth with the octave
		new[] { 0, Rest, 7, Rest, 0, Rest, 5, Approach },  // root–fifth, lean on the 4th
		new[] { 0, Rest, 4, Rest, 7, Rest, 5, Approach },  // walking-ish
	};

	// ── Metal harmony (Genre 3) ──
	// Dark and tight — natural minor / phrygian / harmonic minor for the menacing power-chord
	// riffs. Same RNG draws as the other tables.
	public static readonly int[][] MetalScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian (the metal mode)
		new[] { 0, 2, 3, 5, 7, 8, 11 }, // harmonic minor
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (weighted)
	};

	// Degrees read against the (minor) scale: 5 = ♭VI, 6 = ♭VII, 1 = ♭II, 3 = iv. Metal takes the
	// ♭VI and phrygian ♭II moves — the darkest of the minor turnarounds, and the ones rock does
	// not reach for.
	public static readonly int[][] MetalProgressions =
	{
		new[] { 0, 6, 5, 6 }, // i–♭VII–♭VI–♭VII (driving)
		new[] { 0, 1, 0, 6 }, // i–♭II–i–♭VII (phrygian menace)
		new[] { 0, 0, 5, 6 }, // i pedal → ♭VI–♭VII
		new[] { 0, 5, 1, 0 }, // i–♭VI–♭II–i
		new[] { 0, 0, 1, 1 }, // i / ♭II pedal riff
		new[] { 0, 3, 5, 6 }, // i–iv–♭VI–♭VII
	};

	// Driving roots locked to the double-kick; octave pushes for the gallop.
	public static readonly int[][] MetalBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight chug
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 0, 12, 0, 0, 0, Approach },        // syncopated octave
		new[] { 0, Rest, 0, 0, Rest, 0, 0, Approach },   // syncopated driver
	};

	// ── Punk harmony (Genre 4) ──
	// "Lean punk" / power-pop: bright and major (the opposite pole to rock's dark minor), riding
	// the anthemic four-chord turnarounds at speed. Same RNG draws as the other tables.
	public static readonly int[][] PunkScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (the pop-punk default)
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian (a little grit on the ♭7)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	// Major degrees: 3 = IV, 4 = V, 5 = vi — the anthem turnarounds. Punk keeps the ones that
	// start on the tonic and drive; pop keeps the ones that start away from it and loop.
	public static readonly int[][] PunkProgressions =
	{
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV (the pop-punk anthem)
		new[] { 0, 3, 4, 4 }, // I–IV–V–V
		new[] { 0, 4, 3, 4 }, // I–V–IV–V (three-chord drive)
		new[] { 0, 5, 4, 3 }, // I–vi–V–IV
		new[] { 3, 4, 0, 0 }, // IV–V–I–I (the run-up)
	};

	// Driving root/octave eighths locked to the kick — same engine room as rock, pushed harder.
	public static readonly int[][] PunkBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight eighth chug
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 7, 0, 0, 0, 7, Approach },         // root–fifth gallop
		new[] { 0, Rest, 0, Rest, 0, Rest, 0, Approach },// quarter-note pulse
	};

	// ── Pop harmony (Genre 5) ──
	// Modern synth/dance-pop: bright major / lydian over the ubiquitous four-chord loops, built
	// to sit on a four-on-the-floor kick. Same RNG draws as the other tables.
	public static readonly int[][] PopScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 6, 7, 9, 11 }, // lydian (the sparkly ♯4 — synth-pop shimmer)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	// Pop owns the loops that do not begin on the tonic — the "Axis" rotations, which is exactly
	// what makes a four-chord pop loop sound endless rather than resolved.
	public static readonly int[][] PopProgressions =
	{
		new[] { 5, 3, 0, 4 }, // vi–IV–I–V (the "Axis" loop)
		new[] { 0, 5, 3, 4 }, // I–vi–IV–V
		new[] { 3, 0, 4, 5 }, // IV–I–V–vi
		new[] { 0, 3, 5, 4 }, // I–IV–vi–V
		new[] { 5, 3, 4, 0 }, // vi–IV–V–I
	};

	// Four-on-the-floor synth bass: a steady root pulse with octave pops for bounce.
	public static readonly int[][] PopBassPatterns =
	{
		new[] { 0, Rest, 0, Rest, 0, Rest, 0, Approach },  // root on every beat (locks the floor)
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },         // root with octave pops
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },           // straight eighth pulse
		new[] { 0, Rest, 0, 0, Rest, 0, 12, Approach },    // syncopated synth bass
	};

	/// <summary>Degree → MIDI pitch against <paramref name="scale"/>, wrapping octaves in both
	/// directions so any degree resolves.</summary>
	public static int ScaleMidi( int baseMidi, int[] scale, int degree )
	{
		int len = scale.Length;
		int oct = (int)Math.Floor( degree / (double)len );
		return baseMidi + scale[degree - oct * len] + 12 * oct;
	}

	/// <summary>Root pitch of a progression degree.</summary>
	public static int ChordRoot( int rootMidi, int[] scale, int degree )
		=> ScaleMidi( rootMidi, scale, degree );
}

public sealed partial class MusicGen
{
	// The song's own scale/progression, supplied to the stateless Harmony maths. Every voice
	// calls these rather than reaching for _scale directly.
	int ScaleMidi( int baseMidi, int degree ) => Harmony.ScaleMidi( baseMidi, _scale, degree );
	int ChordRoot( int c ) => Harmony.ChordRoot( _rootMidi, _scale, _prog[c] );
}
