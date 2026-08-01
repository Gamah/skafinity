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
/// NO TWO GENRES SHARE MORE THAN ONE PROGRESSION, OR MORE THAN ONE SCALE. Sharing them is how
/// six genres came to draw byte-identical changes (I–V–vi–IV was in four of them; major was in
/// four scale tables), so both sets of tables are pruned to keep the genres apart and the engine
/// test asserts the cap on each. Adding an entry means checking it against the other five.
///
/// WEIGHTS ARE REAL WEIGHTS. The tables used to bias a draw by listing an entry twice, which
/// silently made "how likely" and "how many entries" the same knob — a genre could not lean on
/// its home mode without also diluting the overlap cap. Each table now carries a parallel weight
/// array (see <see cref="GenreProfile"/>), drawn with one <c>rng.Next()</c> exactly like the old
/// <c>Pick</c>, so a genre's draw count never depends on how it is weighted.
/// </summary>
static class Harmony
{
	/// <summary>Bass-pattern cell: no onset (the previous note sustains).</summary>
	public const int Rest = -99;

	/// <summary>Bass-pattern cell: walk into the next chord instead of playing a fixed
	/// offset.</summary>
	public const int Approach = 99;

	// ── Chord voicings ──
	// Offsets in SCALE-DEGREE space from the chord's own degree, so a voicing follows the mode
	// the way the rest of the engine does: {0,2,4} is a diatonic triad, {0,2,4,6} adds the 7th,
	// {0,4} is the bare power chord (root + 5th). Nothing in the engine played anything but a
	// triad or a power chord, which is why every genre's harmony read as the same primary-colour
	// chord set even under different roots. The chordal voices draw from their genre's table.
	/// <summary>The degree offset of a voicing's THIRD — the note that decides major or minor, and
	/// the one a driven guitar leaves out (see MusicGen.GuitarDegrees).</summary>
	public const int Third = 2;

	public static readonly int[] Triad = { 0, 2, 4 };
	public static readonly int[] Seventh = { 0, 2, 4, 6 };
	public static readonly int[] Ninth = { 0, 2, 4, 6, 8 };
	public static readonly int[] Sixth = { 0, 2, 4, 5 };
	public static readonly int[] Sus4 = { 0, 3, 4 };
	public static readonly int[] Sus2 = { 0, 1, 4 };
	public static readonly int[] Add9 = { 0, 2, 4, 8 };
	public static readonly int[] Power = { 0, 4 };
	public static readonly int[] PowerFlat7 = { 0, 4, 6 };

	// ── Ska harmony (Genre 0) ──
	// Bright and offbeat: ska/rocksteady sits in major, and leans mixolydian for the ♭VII moves
	// its progressions take.
	public static readonly int[][] SkaScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
	};
	public static readonly int[] SkaScaleWeights = { 3, 2 };

	// Rocksteady/ska voice their chords with the 7th and the 9th — the single biggest thing
	// separating a ska chord from a rock one, and the reason a plain triad read as "not ska".
	public static readonly int[][] SkaVoicings = { Seventh, Ninth, Triad, Sixth };
	public static readonly int[] SkaVoicingWeights = { 4, 2, 2, 1 };

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

	// ── Bass pattern libraries ──
	// Cells are semitone offsets from the chord root, one per eighth; Rest carries no onset (the
	// previous note sustains through it) and Approach walks into the next chord.
	//
	// These are Patterns, not int[8]: a pattern owns its LENGTH, so a genre's line can be a
	// two-bar phrase that answers itself or a four-bar one that varies its last bar, instead of
	// one bar repeated until the section ends. That is also what keeps the libraries apart — the
	// old tables shared literal rows ({0,0,0,0,0,0,0,App} was in four of the five).
	static Pattern P( params int[] cells ) => Pattern.Eighths( cells );

	// Ska: the melodic reggae bass — long legato chord tones, 1↔5 motion, and real walking lines
	// between the changes. It is the lead instrument of the rhythm section here, so its phrases
	// are two and four bars long.
	public static readonly Pattern[] SkaBass =
	{
		P( 0, Rest, 0, 12, Rest, 7, 5, Approach ),                        // sublime melodic (1 bar)
		P( Rest, Rest, 0, Rest, 0, Rest, 7, Rest,
		   Rest, Rest, 0, Rest, 0, Rest, 7, Approach ),                   // one-drop, spacious (2 bars)
		P( 0, Rest, 2, Rest, 4, Rest, 5, Rest,
		   7, Rest, 5, Rest, 4, Rest, 2, Approach ),                      // walking up and back (2 bars)
		P( 0, Rest, Rest, Rest, 7, Rest, Rest, Rest,
		   0, Rest, Rest, 12, 7, Rest, 5, Approach ),                     // 1↔5 legato, answered (2 bars)
		P( 0, 12, 0, 7, Rest, 12, 7, Rest,
		   0, 12, 0, 7, Rest, 12, 7, Rest,
		   0, 12, 0, 7, Rest, 12, 7, Rest,
		   0, 7, 5, 4, 2, Rest, 0, Approach ),                            // busy ska, walks out (4 bars)
	};

	// ── Rock harmony (Genre 1) ──
	// Minor-3rd modes throughout: the RockProgressions are written as MINOR (i–♭VII–♭VI …), so a
	// major-3rd mode would flip the tonic major and the dark rock vamp would evaporate. Phrygian
	// went to metal — rock and metal sharing three modes was how two genres in the same tonality
	// could draw the identical mode under their now-different changes.
	public static readonly int[][] RockScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 2, 3, 5, 7, 9, 10 }, // dorian (minor, brighter ♮6 — classic rock)
	};
	public static readonly int[] RockScaleWeights = { 3, 2 };

	// Rock voices the triad plainly and reaches for a suspension rather than an extension — the
	// sus4 that hangs and resolves is the rock chord move ska's 7ths and 9ths are not.
	public static readonly int[][] RockVoicings = { Triad, Sus4, Power };
	public static readonly int[] RockVoicingWeights = { 3, 2, 2 };

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

	// Rock: the engine room. Root-driven and locked to the kick, but phrased over two bars so it
	// pushes and releases rather than chugging identically forever.
	public static readonly Pattern[] RockBass =
	{
		P( 0, Rest, 0, 0, Rest, 0, 0, Rest,
		   0, Rest, 0, 0, Rest, 0, 12, Approach ),                        // syncopated driver (2 bars)
		P( 0, Rest, Rest, 0, Rest, Rest, 0, Rest,
		   0, Rest, Rest, 0, Rest, 12, 7, Approach ),                     // dotted push (2 bars)
		P( 0, 0, 12, 0, 0, 0, 12, Rest,
		   0, 0, 12, 0, 7, 5, 3, Approach ),                              // octave pushes → walkdown (2 bars)
		P( 0, Rest, 0, Rest, 0, Rest, 0, Approach ),                      // quarter pulse (1 bar)
	};

	// ── Country harmony (Genre 2) ──
	// Country is the plainest major genre and stays that way: one mode, and the variety comes from
	// the changes and the boom-chick underneath. Mixolydian is ska's — country and ska sharing
	// both bright modes is exactly the near-duplication the cap exists to stop, and a second mode
	// bought country nothing its progressions do not already give it.
	public static readonly int[][] CountryScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
	};
	public static readonly int[] CountryScaleWeights = { 1 };

	// Country's colour chords are the 6th and the sus4 — the open, ringing shapes a Telecaster
	// plays. No 7ths: that is ska's sound, and a dominant 7th everywhere reads as blues.
	public static readonly int[][] CountryVoicings = { Triad, Sixth, Sus4 };
	public static readonly int[] CountryVoicingWeights = { 3, 2, 1 };

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

	// Country: "boom-chick" — the bass alternates root and fifth on the beats while the guitar and
	// snare take the off "chick", and it walks in step to the next chord rather than pushing.
	// Two-bar phrases so the walkdown has somewhere to happen.
	public static readonly Pattern[] CountryBass =
	{
		P( 0, Rest, 7, Rest, 0, Rest, 7, Rest,
		   0, Rest, 7, Rest, 0, Rest, 5, Approach ),                      // alternating root–fifth (2 bars)
		P( 0, Rest, 7, Rest, 12, Rest, 7, Rest,
		   0, Rest, 7, Rest, 9, 7, 5, Approach ),                         // with the octave, walks out (2 bars)
		P( 0, Rest, 7, Rest, 0, Rest, 7, Rest,
		   0, Rest, 7, Rest, 4, 5, 7, Approach ),                         // scalar walkup (2 bars)
		P( 0, Rest, 7, Rest, 0, Rest, 7, Approach ),                      // the plain boom-chick (1 bar)
	};

	// ── Metal harmony (Genre 3) ──
	// Phrygian is the metal mode and carries the weight here; harmonic minor is the neoclassical
	// colour. Aeolian stays as the common ground with rock — one shared mode is honest, three was
	// two genres playing the same thing in a different tempo band.
	public static readonly int[][] MetalScales =
	{
		new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian (the metal mode)
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 2, 3, 5, 7, 8, 11 }, // harmonic minor
	};
	public static readonly int[] MetalScaleWeights = { 4, 2, 1 };

	// Metal is correctly 3rd-less: the power chord, and the ♭7 on top of it for the wider riff
	// voicing. A major or minor triad through that much gain is mud, and the 3rd is what makes it
	// sound like rock rather than metal.
	public static readonly int[][] MetalVoicings = { Power, PowerFlat7 };
	public static readonly int[] MetalVoicingWeights = { 4, 1 };

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

	// Metal: a low pedal point under the riff, or the riff's own rhythm doubled. These are the
	// fallback tables — when the song draws the "follows the riff" mode the bass reads the
	// guitar's onsets instead of any of these (see Bass.cs), because both real metal bass modes
	// are RELATIONAL and a table can only ever approximate them.
	public static readonly Pattern[] MetalBass =
	{
		P( 0, 0, 0, 0, 0, 0, 0, 0,
		   0, 0, 0, 0, 0, 0, 0, Approach ),                               // pedal chug (2 bars)
		P( 0, Rest, Rest, Rest, Rest, Rest, Rest, Rest,
		   0, Rest, Rest, Rest, Rest, Rest, Rest, Approach ),             // whole-bar pedal point (2 bars)
		P( 0, 0, 12, 0, 0, 0, 12, 0,
		   0, 0, 12, 0, 0, 12, 0, Approach ),                             // octave gallop (2 bars)
	};

	// ── Punk harmony (Genre 4) ──
	// "Lean punk" / power-pop: overwhelmingly major, with the minor-key hardcore option as the
	// rare draw. Mixolydian went to ska — punk's grit comes from the tempo and the downstrokes,
	// not from a ♭7.
	public static readonly int[][] PunkScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (the pop-punk default)
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (the darker hardcore draw)
	};
	public static readonly int[] PunkScaleWeights = { 5, 1 };

	// Punk is a power chord and, when it wants the anthem to open up, a plain triad. Nothing
	// added, nothing suspended — the voicing is the least interesting thing about a punk song.
	public static readonly int[][] PunkVoicings = { Power, Triad };
	public static readonly int[] PunkVoicingWeights = { 3, 2 };

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

	// Punk: relentless straight eighths — the one genre where the undifferentiated chug IS the
	// idiom. The variation is in the last bar of the phrase, not in the bar-to-bar.
	public static readonly Pattern[] PunkBass =
	{
		P( 0, 0, 0, 0, 0, 0, 0, 0,
		   0, 0, 0, 0, 0, 0, 0, 0,
		   0, 0, 0, 0, 0, 0, 0, 0,
		   0, 0, 0, 0, 0, 0, 0, Approach ),                               // eighth chug, 4-bar phrase
		P( 0, 0, 0, 0, 0, 0, 0, 0,
		   0, 0, 0, 0, 12, 12, 7, Approach ),                             // chug that pops the octave (2 bars)
		P( 0, 0, 7, 0, 0, 0, 7, Approach ),                               // root–fifth gallop (1 bar)
	};

	// ── Pop harmony (Genre 5) ──
	// Modern synth/dance-pop: bright major, with lydian's ♯4 as the shimmer. Lydian is pop's
	// alone — it is the one mode that reads as "produced" rather than played.
	public static readonly int[][] PopScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 6, 7, 9, 11 }, // lydian (the sparkly ♯4 — synth-pop shimmer)
	};
	public static readonly int[] PopScaleWeights = { 3, 1 };

	// Pop's chords are open and unresolved: add9 and sus2 leave the 3rd ambiguous, which is what
	// makes a four-chord loop sound like it never lands. The plain triad is the fallback.
	public static readonly int[][] PopVoicings = { Add9, Sus2, Triad };
	public static readonly int[] PopVoicingWeights = { 3, 2, 3 };

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

	// Pop: a synth bass locked to the four-on-the-floor kick, with the octave pops that give a
	// dance track its bounce. One chord per bar here (ChordBars = 1), so these stay short and the
	// approach fires into every change.
	public static readonly Pattern[] PopBass =
	{
		P( 0, Rest, 0, Rest, 0, Rest, 0, Approach ),                      // root on every beat (1 bar)
		P( 0, Rest, 12, 0, Rest, 12, 0, Rest,
		   0, Rest, 12, 0, Rest, 12, 7, Approach ),                       // octave pops (2 bars)
		P( 0, Rest, Rest, 0, Rest, 0, Rest, Approach ),                   // sidechained syncopation (1 bar)
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
	// calls these rather than reaching for _scale directly. The section's KeyShift rides on the
	// root here, so a modulation moves the whole band at once (see Part.KeyShift).
	int ScaleMidi( int baseMidi, int degree ) => Harmony.ScaleMidi( baseMidi, _scale, degree );
	int ChordRoot( int c ) => Harmony.ChordRoot( _rootMidi + _keyShift, _scale, _prog[c] );

	/// <summary>Degree of the <paramref name="i"/>th voice of the chord, in the song's own
	/// voicing. Indices past the top wrap up an octave, so an arpeggio can just keep counting.
	/// </summary>
	int ChordDegree( int chord, int i )
	{
		int n = _voicing.Length;
		int oct = (int)Math.Floor( i / (double)n );
		return _prog[chord] + _voicing[i - oct * n] + oct * _scale.Length;
	}

	/// <summary>Every note of the chord, as scale degrees.</summary>
	int[] ChordDegrees( int chord )
	{
		var d = new int[_voicing.Length];
		for ( int i = 0; i < d.Length; i++ ) d[i] = _prog[chord] + _voicing[i];
		return d;
	}
}
