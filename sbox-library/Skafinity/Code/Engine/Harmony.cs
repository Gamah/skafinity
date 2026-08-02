using System;
using System.Collections.Generic;

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
	/// <summary>Degree offsets inside a voicing. The THIRD decides major or minor and is the note a
	/// driven guitar leaves out; the FOURTH and the FIFTH are the ones that must stay PERFECT (see
	/// MusicGen.VoicedTone), because they are what "sus4" and "power chord" mean.</summary>
	public const int Third = 2, Fourth = 3, Fifth = 4;

	/// <summary>The SECOND — the other degree a suspension puts where the third belongs.</summary>
	public const int Second = 1;

	/// <summary>Index of the voice a suspension occupies in <paramref name="voicing"/>, or -1 if it
	/// is not suspended.
	///
	/// A SUSPENSION IS A DELAYED THIRD, NOT A CHORD QUALITY. sus4 and sus2 put the fourth or the
	/// second in the third's place, so a chord voiced that way states no quality — and the song's
	/// voicing is drawn once, for every chordal voice and every chord. Held that way for a whole
	/// song nothing is out of key and every voice agrees; the song simply has no major and no
	/// minor, and an ear with nothing to resolve to hears the ambiguity as dissonance. The
	/// suspended note has to arrive somewhere, so <see cref="MusicGen.VoicingAt"/> hands the
	/// chordal voices the resolved spelling over the back half of every chord's span: the chord
	/// hangs, then it lands.
	///
	/// A voicing that already contains the third is not suspended — the sixth's added 6th and the
	/// add9's 9th are colour over a stated triad, not a substitution. Neither is the power chord:
	/// it OMITS the third rather than replacing it, which is a sound in its own right (it is what
	/// a driven guitar plays) and there is nothing owed.</summary>
	public static int SuspendedVoice( int[] voicing )
	{
		int sus = -1;
		for ( int i = 0; i < voicing.Length; i++ )
		{
			if ( voicing[i] == Third ) return -1;
			if ( voicing[i] == Second || voicing[i] == Fourth ) sus = i;
		}
		return sus;
	}

	public static readonly int[] Triad = { 0, 2, 4 };
	public static readonly int[] Seventh = { 0, 2, 4, 6 };
	public static readonly int[] Ninth = { 0, 2, 4, 6, 8 };
	public static readonly int[] Sixth = { 0, 2, 4, 5 };
	public static readonly int[] Sus4 = { 0, 3, 4 };
	public static readonly int[] Sus2 = { 0, 1, 4 };
	public static readonly int[] Add9 = { 0, 2, 4, 8 };
	public static readonly int[] Power = { 0, 4 };
	public static readonly int[] PowerFlat7 = { 0, 4, 6 };

	// ── Ska harmony (Genre 0 — third wave) ──
	// Bright and major: third-wave ska is upbeat major-key music, and mixolydian is what keeps its
	// ♭VII moves available. The wave shift moved the WEIGHT toward plain major rather than the
	// table itself — the modes were already right, which is the part of the genre that did not
	// need retuning.
	public static readonly int[][] SkaScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
	};
	public static readonly int[] SkaScaleWeights = { 4, 2 };

	// The 7ths and 9ths stay: they are what make the CLEAN SKANK read as ska rather than as a
	// bright rock stab, and the skank is still what the verses play. They cost nothing in the loud
	// sections — the driven guitar drops its third there anyway (see DrivenVoicing), so the same
	// voicing spells a rocksteady chop in the verse and a power chord in the chorus.
	public static readonly int[][] SkaVoicings = { Seventh, Ninth, Triad, Sixth };
	public static readonly int[] SkaVoicingWeights = { 3, 2, 3, 1 };

	// The bright turnarounds ska keeps. The mixolydian ♭VII moves are the ones doing the work
	// here: punk and pop hold the plain-major anthem loops (I–V–vi–IV, vi–IV–I–V, I–IV–V–V,
	// I–V–IV–V), and ska-punk sits close enough to punk that ♭VII is most of what is left to tell
	// them apart harmonically — so nothing here may drift toward those tables. The 50s/rocksteady
	// ii–V turnaround went with the wave: it is the sound of the era this genre no longer is.
	public static readonly int[][] SkaProgressions =
	{
		new[] { 0, 3, 4, 3 }, // I–IV–V–IV
		new[] { 0, 6, 3, 0 }, // I–♭VII–IV–I (mixolydian)
		new[] { 0, 6, 4, 0 }, // I–♭VII–V–I (the mixolydian cadence)
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

	// Ska (third wave): a DRIVING bass. The spacious one-drop and the long legato 1↔5 lines that
	// used to live here are rocksteady/reggae playing — the right part for a genre this engine does
	// not have yet (see PLAN.md), and recoverable from git history when it does. A ska-punk bassist
	// runs eighths under the skank and pops the octave, closer to punk than to reggae; what keeps
	// these apart from PunkBass is that they still MOVE — walking lines and octave answers rather
	// than the undifferentiated chug that is punk's whole idea.
	public static readonly Pattern[] SkaBass =
	{
		P( 0, 0, 0, 0, 7, 0, 0, 0,
		   0, 0, 0, 0, 12, 0, 7, Approach ),                              // driving eighths, octave on the way out (2 bars)
		P( 0, 12, 0, 12, 0, 12, 7, Approach ),                            // the octave pump (1 bar)
		P( 0, 0, 2, 0, 4, 0, 5, 0,
		   7, 0, 5, 0, 4, 0, 2, Approach ),                               // walking eighths up and back (2 bars)
		P( 0, Rest, 0, 7, Rest, 0, 12, Rest,
		   0, Rest, 0, 7, Rest, 12, 7, Approach ),                        // the verse line that breathes under the skank (2 bars)
		P( 0, 0, 0, 0, 0, 0, 12, 0,
		   0, 0, 0, 0, 7, 0, 12, 0,
		   0, 0, 0, 0, 0, 0, 12, 0,
		   0, 7, 5, 4, 2, 0, 0, Approach ),                               // four bars that walk out of the phrase
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

	/// <summary>
	/// One voice of a chord, as a MIDI pitch — the ONLY correct way to turn a voicing into notes.
	///
	/// A voicing is a list of degree offsets, so <c>ScaleMidi(base, root + offset)</c> spells it
	/// DIATONICALLY: every interval comes out whatever the scale makes it at that degree. For the
	/// third, the sixth, the seventh and the ninth that is exactly right — major-or-minor by
	/// position is what diatonic harmony IS. For the FOURTH and the FIFTH it is wrong, because
	/// every seven-note scale has one degree whose diatonic fifth is DIMINISHED and one whose
	/// fourth is AUGMENTED. Spelled diatonically, a power chord on that degree is a bare tritone
	/// and a sus4 is a root with a flat five — with no third present to explain either, which is
	/// what "way off key" sounds like, and it is at its worst through distortion.
	///
	/// A guitarist frets the same power-chord shape on every degree; the shape does not go
	/// diminished because of the key. So the fourth and the fifth are forced perfect and
	/// everything else keeps its diatonic spelling. Both offsets sit inside one octave of the
	/// root, so the perfect interval is simply the root plus 5 or 7.
	/// </summary>
	public static int VoicedTone( int baseMidi, int[] scale, int rootDegree, int offset )
	{
		int root = ScaleMidi( baseMidi, scale, rootDegree );
		if ( offset == Fourth ) return root + 5;
		if ( offset == Fifth ) return root + 7;
		return ScaleMidi( baseMidi, scale, rootDegree + offset );
	}

	/// <summary>How far one voice may be octave-shifted to stay near the chord before it. An
	/// octave is what an INVERSION is; more than that moves the part into another register
	/// rather than re-voicing the chord.</summary>
	public const int MaxVoiceLead = 12;

	/// <summary>
	/// VOICE LEADING: per chord of <paramref name="prog"/>, the octave offset each voice of
	/// <paramref name="voicing"/> takes so the chord sits near the one before it.
	///
	/// Built upward from its root degree, a chord's register is wherever that degree happens to
	/// fall and the same shape simply slides — so a progression that steps a third moves every
	/// voice a tenth, with no common tone, every time the change comes round. That is what reads
	/// as "it jumped" (and it is loudest when a chord change lands on a section boundary). A
	/// player inverts instead: keep the register, keep the common tones, move the voices that
	/// have to move.
	///
	/// Each voice is octave-shifted to whichever octave sits nearest its OWN previous pitch (ties
	/// going to the octave nearer root position, so the comp cannot walk itself out of register
	/// over a few laps of the progression), so
	/// the chord's identity is untouched — same degrees, same spelling, different inversion. The
	/// result is a table because it is a property of the SONG (progression × voicing × scale),
	/// not of a voice: every chordal voice reads the same shifts and therefore agrees on the
	/// inversion, whatever register it plays in. The bass is deliberately NOT in here — it plays
	/// roots, and a root that inverts is a different chord.
	///
	/// A PROGRESSION IS A CYCLE, and the choice is made as one: the last chord going back round to
	/// the first is a change like any other, so a greedy walk anchored at the first chord parks
	/// every leap the other three avoided on that one seam. Relaxing the walk round and round does
	/// not fix it either — the cycle is what makes it oscillate rather than settle, and an
	/// unsettled seam is a leap of a seventh sitting in the middle of a fixed table. So each voice
	/// is solved EXACTLY, by a walk over the three octaves it may take at each chord that closes
	/// the loop (voices are independent, three options each, four chords: it is a handful of
	/// additions, done once per song). Ties go to the octave nearer root position, so the comp
	/// cannot walk itself out of register.
	///
	/// Pitches are measured over a base of 0 — the shift is base-independent — and a voice may dip
	/// a little under that base but no further (<see cref="VoiceLeadFloor"/>).
	/// </summary>
	public static int[][] PlanVoiceLeading( int[] scale, int[] prog, int[] voicing )
	{
		int n = voicing.Length, np = prog.Length;
		var shift = new int[np][];
		for ( int c = 0; c < np; c++ ) shift[c] = new int[n];

		int k = 2 * (MaxVoiceLead / 12) + 1;              // the octaves on offer: −1, 0, +1
		var raw = new int[np];
		var cost = new int[np, k];
		var from = new int[np, k];
		var chain = new int[np];

		for ( int v = 0; v < n; v++ )
		{
			for ( int c = 0; c < np; c++ ) raw[c] = VoicedTone( 0, scale, prog[c], voicing[v] );

			int bestTotal = int.MaxValue;
			for ( int start = 0; start < k; start++ )     // the loop has to close on what it opened
			{
				if ( Pitch( raw[0], start ) < VoiceLeadFloor ) continue;
				for ( int c = 0; c < np; c++ )
					for ( int o = 0; o < k; o++ ) { cost[c, o] = Unreachable; from[c, o] = 0; }
				cost[0, start] = Home( start );

				for ( int c = 1; c < np; c++ )
					for ( int o = 0; o < k; o++ )
					{
						if ( Pitch( raw[c], o ) < VoiceLeadFloor ) continue;
						for ( int p = 0; p < k; p++ )
						{
							if ( cost[c - 1, p] >= Unreachable ) continue;
							int t = cost[c - 1, p] + Move( raw[c - 1], p, raw[c], o ) + Home( o );
							if ( t >= cost[c, o] ) continue;
							cost[c, o] = t;
							from[c, o] = p;
						}
					}

				for ( int last = 0; last < k; last++ )
				{
					if ( cost[np - 1, last] >= Unreachable ) continue;
					int total = cost[np - 1, last]
						+ (np > 1 ? Move( raw[np - 1], last, raw[0], start ) : 0);
					if ( total >= bestTotal ) continue;
					bestTotal = total;
					for ( int c = np - 1, o = last; c >= 0; c-- ) { chain[c] = o; o = from[c, o]; }
				}
			}
			for ( int c = 0; c < np; c++ ) shift[c][v] = 12 * (chain[c] - MaxVoiceLead / 12);
		}
		return shift;

		int Pitch( int r, int o ) => r + 12 * (o - MaxVoiceLead / 12);
		// Motion is weighted so it always outranks the pull toward root position: an octave of
		// register is worth having, but never at the price of a semitone of extra movement.
		int Move( int ra, int a, int rb, int b ) => 2 * Math.Abs( Pitch( rb, b ) - Pitch( ra, a ) );
		int Home( int o ) => Math.Abs( o - MaxVoiceLead / 12 );
	}

	/// <summary>How far under its own base a voice may be led, relative to the base the chord is
	/// voiced up from. A chord whose root is the scale's seventh degree sits eleven semitones up in
	/// root position and one semitone DOWN inverted, and the inverted one is the whole point — a
	/// floor at the base exactly forbids the move that helps most. Half an octave under is where
	/// "inverted" turns into "an octave lower", and metal's comp is based at the bass's own
	/// register, so there is no room below that.</summary>
	public const int VoiceLeadFloor = -6;

	/// <summary>Cost of a path that does not exist — larger than any real one, and small enough to
	/// add to another without overflowing.</summary>
	const int Unreachable = 1 << 20;
}

public sealed partial class MusicGen
{
	// The song's own scale/progression, supplied to the stateless Harmony maths. Every voice
	// calls these rather than reaching for _scale directly. The section's KeyShift rides on the
	// root here, so a modulation moves the whole band at once (see Part.KeyShift).
	int ScaleMidi( int baseMidi, int degree ) => Harmony.ScaleMidi( baseMidi, _scale, degree );

	/// <summary>A voice's register: the pitch it spells its scale over, <paramref name="octaves"/>
	/// octaves above the song's root.
	///
	/// A REGISTER IS A NUMBER OF OCTAVES, AND ONLY EVER THAT. <see cref="Harmony.ScaleMidi"/> and
	/// <see cref="Harmony.VoicedTone"/> treat their base as THE TONIC and add the scale offset on
	/// top, so a base of root + 31 does not raise a part by a fifth — it spells that part in the
	/// key a fifth up. The part then disagrees with the band about one note of the scale (about two
	/// of them, a whole tone up), and a melody in a different key from its backing is exactly what
	/// it sounds like. Every voice takes its register through here so a base that is not a whole
	/// octave cannot be written in the first place, which is worth more than a test for it: the
	/// wrong version stays in tune roughly six notes in seven, so it does not announce itself.
	///
	/// The cost is that register is QUANTISED — a part sits an octave up or it doesn't, and there
	/// is no landing between. If a part ends up too high, narrow what it plays (a melody's degree
	/// range) rather than reaching for a base between two octaves.
	///
	/// Transposing an actual PITCH by an octave (<c>ChordRoot(c) + 12</c>) is a different thing and
	/// is fine — the scale has already been spelled by then.</summary>
	int Register( int octaves ) => _rootMidi + _keyShift + 12 * octaves;
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

	/// <summary>Every note of the chord, as scale degrees. This is the MELODIC view — what tones
	/// a line may land on. A voice that SOUNDS the chord wants <see cref="ChordMidis"/> instead,
	/// which keeps the perfect intervals perfect.</summary>
	int[] ChordDegrees( int chord )
	{
		var d = new int[_voicing.Length];
		for ( int i = 0; i < d.Length; i++ ) d[i] = _prog[chord] + _voicing[i];
		return d;
	}

	/// <summary>Every note of the chord as MIDI pitches over <paramref name="baseMidi"/> — what a
	/// chordal voice actually plays. Use this rather than <c>ScaleMidi</c> over
	/// <see cref="ChordDegrees"/>: see <see cref="Harmony.VoicedTone"/> for why the difference
	/// matters on one degree of every scale.
	///
	/// This is also where the song's VOICE LEADING lands (<see cref="Harmony.PlanVoiceLeading"/>),
	/// so the chord arrives in the inversion nearest the one before it instead of sliding a tenth.
	/// Voice i keeps its index — the array stays aligned with <c>_voicing</c>, which is what lets
	/// the driven guitar drop the third by position.</summary>
	int[] ChordMidis( int baseMidi, int chord, int tick )
	{
		var m = VoicedMidis( baseMidi, _prog[chord], VoicingAt( tick ) );
		var s = _vlShift[chord];
		for ( int i = 0; i < m.Length; i++ ) m[i] += s[i];
		return m;
	}

	/// <summary>The spelling the chordal voices sound at <paramref name="tick"/>: the song's
	/// voicing, or — over the back half of the current chord's span — the one its suspension
	/// resolves to (<see cref="Harmony.SuspendedVoice"/>). The two arrays are the same object
	/// unless the voicing is suspended, so this costs nothing for the other five voicings.
	///
	/// EVERY chordal voice reads it, at the tick of the note it is about to sound, so the band
	/// resolves together the way it agrees on the chord and the inversion. The voice-leading table
	/// is deliberately NOT recomputed: a suspension resolving moves one voice a step inside the
	/// inversion the song already chose, which is what a player's finger does — re-inverting the
	/// chord underneath it would make the landing a jump.</summary>
	int[] VoicingAt( int tick ) => tick >= _susResolveTick ? _voicingRes : _voicing;

	/// <summary>A chordal note of <paramref name="durTicks"/> from <paramref name="tick"/>, split
	/// into the part before its suspension resolves and the part after — one segment for every
	/// note that does not straddle the resolution, which is all of them for a voicing that is not
	/// suspended.
	///
	/// The chord is RE-ARTICULATED where the third lands rather than changing under a ringing
	/// note, because a suspension that resolves silently is not heard to resolve: the landing is
	/// the gesture, and a player re-picks or hammers the note that moves. It matters most where a
	/// voice holds a whole chord at a time — pop's pad sounds one chord per bar, so without this
	/// its suspension has nowhere to land at all.
	///
	/// The chordal voices whose genres can draw a suspension (the guitar and the keys) read this;
	/// ska's skank and horns do not, because every ska voicing states its third.</summary>
	IEnumerable<(int Tick, int Ticks)> ChordSegments( int tick, int durTicks )
	{
		if ( _susVoice >= 0 && tick < _susResolveTick && tick + durTicks > _susResolveTick )
		{
			yield return (tick, _susResolveTick - tick);
			yield return (_susResolveTick, tick + durTicks - _susResolveTick);
			yield break;
		}
		yield return (tick, durTicks);
	}

	/// <summary>As <see cref="ChordMidis"/> but in ROOT POSITION, for a chord whose root degree is
	/// given directly — the ending's cadence builds its V that way, and a final chord lands where
	/// the genre voices it rather than where the last change left the register.</summary>
	int[] VoicedMidis( int baseMidi, int rootDegree, int[] voicing )
	{
		var m = new int[voicing.Length];
		for ( int i = 0; i < m.Length; i++ )
			m[i] = Harmony.VoicedTone( baseMidi, _scale, rootDegree, voicing[i] );
		return m;
	}

	/// <summary>Pitch of the <paramref name="i"/>th voice of the chord (wrapping up an octave past
	/// the top, so an arpeggio can keep counting) — the sounding counterpart of
	/// <see cref="ChordDegree"/>.</summary>
	int ChordToneMidi( int baseMidi, int chord, int i, int tick )
	{
		var voicing = VoicingAt( tick );
		int n = voicing.Length;
		int oct = (int)Math.Floor( i / (double)n );
		int v = i - oct * n;
		return Harmony.VoicedTone( baseMidi, _scale, _prog[chord], voicing[v] )
			+ _vlShift[chord][v] + 12 * oct;
	}
}
