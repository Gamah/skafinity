using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Skafinity.EngineTests;

/// <summary>
/// Engine-only test harness — see the csproj header. Runs on a plain dev host (no s&amp;box,
/// no wasm workload): it compiles <c>Code/Engine/**</c> into the SAME assembly as these
/// tests, so <c>internal</c> engine types are directly reachable and composition can be
/// asserted without rendering audio.
///
/// Two kinds of check live here:
///  * <b>Composition</b> — determinism, harmony maths, structure, the vibe codec. These are
///    the real contract and they are asserted on values, not on a hash.
///  * <b>Render digest</b> — a SHA-256 over the rendered PCM of a fixed seed matrix. This is
///    NOT a promise that audio never changes between commits (it will, deliberately). It is
///    a within-build tripwire for refactors that are supposed to be pure: record the digests
///    with <c>--bless</c> before a mechanical move, re-run after, and any drift means the
///    move was not pure. Re-bless whenever a change is meant to be audible.
/// </summary>
static class Program
{
	static int _failed, _passed;

	static int Main( string[] args )
	{
		bool bless = Array.IndexOf( args, "--bless" ) >= 0;
		// --audition [kick|snare|toms|hats|crash|ride] [wavPath]. The voice filter is what makes a
		// second round cheap: re-render the one voice the notes are about. See DRUMS.md.
		int ai = Array.IndexOf( args, "--audition" );
		if ( ai >= 0 )
		{
			string only = ai + 1 < args.Length && !args[ai + 1].StartsWith( "-" ) ? args[ai + 1] : null;
			string wav = ai + 2 < args.Length && !args[ai + 2].StartsWith( "-" ) ? args[ai + 2]
				: Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
					"audition.wav" );
			Audition.Run( only, wav, Path.ChangeExtension( wav, ".txt" ) );
			return 0;
		}
		// --cymbal [dir]: one dry hit of each cymbal, for tools/spectool to re-measure. See
		// Audition.Cymbals — a fitted spectrum is not fitted until the RESULT is measured too.
		int cy = Array.IndexOf( args, "--cymbal" );
		if ( cy >= 0 )
		{
			Audition.Cymbals( cy + 1 < args.Length && !args[cy + 1].StartsWith( "-" ) ? args[cy + 1]
				: Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ) );
			return 0;
		}
		// --render vibe:tag:n [path]: the song, as a WAV. The diagnostics either side of this one
		// answer "what did the composer decide" and "how loud is this voice"; sometimes the
		// question is just "what does it sound like", and this host has no browser to answer it in.
		int rn = Array.IndexOf( args, "--render" );
		if ( rn >= 0 && rn + 1 < args.Length )
		{
			Render( args[rn + 1], rn + 2 < args.Length && !args[rn + 2].StartsWith( "-" )
				? args[rn + 2]
				: Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
					"song.wav" ) );
			return 0;
		}
		if ( Array.IndexOf( args, "--levels" ) >= 0 ) { Levels(); return 0; }
		// --hand: the cymbal hand measured against the hi-hat it stands in for. See CymbalHand.
		if ( Array.IndexOf( args, "--hand" ) >= 0 ) { CymbalHand(); return 0; }
		int gi = Array.IndexOf( args, "--grid" );
		if ( gi >= 0 ) { Grid( gi + 1 < args.Length && int.TryParse( args[gi + 1], out var gg ) ? gg : -1 ); return 0; }
		int si = Array.IndexOf( args, "--seed" );
		if ( si >= 0 && si + 1 < args.Length ) { Explain( args[si + 1] ); return 0; }
		int ci = Array.IndexOf( args, "--score" );
		if ( ci >= 0 && ci + 1 < args.Length )
		{
			int from = ci + 2 < args.Length && int.TryParse( args[ci + 2], out var f0 ) ? f0 : 1;
			int to = ci + 3 < args.Length && int.TryParse( args[ci + 3], out var t0 ) ? t0 : from + 8;
			Score( args[ci + 1], from, to );
			return 0;
		}

		// Sections run in a list so each one can be timed. The per-section wall clock is printed
		// at the end: this harness is dominated by rendering, so "which section costs what" is the
		// first question anyone asks before optimising it, and guessing has been wrong before.
		var sections = new (string Name, Action Run)[]
		{
			( "prng + determinism", DeterminismTests ),
			( "harmony",            HarmonyTests ),
			( "time base",          TimingTests ),
			( "patterns",           PatternTests ),
			( "melody",             MelodyTests ),
			( "bend placement",     BendBiasTests ),
			( "genre feel",         GenreProfileTests ),
			( "wired knobs",        WiredKnobTests ),
			( "structure",          StructureTests ),
			( "arrangement",        ArrangementTests ),
			( "vibe codec",         VibeTests ),
			( "wav container",      WavTests ),
			( bless ? "render digest (blessing)" : "render digest", () => RenderDigestTests( bless ) ),
		};

		var timings = new (string Name, double Ms)[sections.Length];
		var total = System.Diagnostics.Stopwatch.StartNew();
		for ( int i = 0; i < sections.Length; i++ )
		{
			Banner( sections[i].Name );
			var sw = System.Diagnostics.Stopwatch.StartNew();
			sections[i].Run();
			timings[i] = (sections[i].Name, sw.Elapsed.TotalMilliseconds);
		}

		Console.WriteLine();
		Console.WriteLine( "── time ──" );
		foreach ( var (name, ms) in timings )
			Console.WriteLine( $"  {ms,8:0} ms  {name}" );
		Console.WriteLine( $"  {total.Elapsed.TotalMilliseconds,8:0} ms  TOTAL" );

		Console.WriteLine();
		Console.WriteLine( _failed == 0
			? $"OK — {_passed} checks passed"
			: $"FAILED — {_failed} of {_passed + _failed} checks failed" );
		return _failed == 0 ? 0 : 1;
	}

	// ── the seed matrix every render-digest check runs over ──────────────────────────────

	/// <summary>Genre, tag and index chosen to cover every genre and both the default and a
	/// non-trivial vibe. Keep this list append-only so old digests stay comparable.</summary>
	static readonly (string Vibe, string Tag, int N)[] Matrix =
	{
		( "0", "rotaliate", 0 ),
		( "0", "skafinity", 7 ),
		( "1", "rotaliate", 0 ),
		( "1", "gamah", 3 ),
		( "2", "rotaliate", 0 ),
		( "3", "rotaliate", 0 ),
		( "3", "doom", 11 ),
		( "4", "rotaliate", 0 ),
		( "5", "rotaliate", 0 ),
		( "5", "bubblegum", 23 ),
	};

	// ── checks ───────────────────────────────────────────────────────────────────────────

	static void DeterminismTests()
	{
		// The PRNG is the root of every musical choice: two Rngs off one seed must agree
		// forever, and two different seeds must not agree immediately.
		var a = new Rng( 12345u );
		var b = new Rng( 12345u );
		bool same = true;
		for ( int i = 0; i < 4096; i++ ) same &= a.Next() == b.Next();
		Check( "Rng is reproducible from its seed", same );

		var c = new Rng( 12346u );
		var d = new Rng( 12345u );
		bool diverges = false;
		for ( int i = 0; i < 64; i++ ) diverges |= c.Next() != d.Next();
		Check( "Rng diverges for a different seed", diverges );

		Check( "Rng.Next stays in [0,1)", AllIn01( new Rng( 999u ), 100000 ) );

		// Int(n) must never return n (an out-of-range table index is a crash, not a wrong note).
		var r = new Rng( 4242u );
		bool inRange = true;
		for ( int i = 0; i < 100000; i++ ) { int v = r.Int( 7 ); inRange &= v >= 0 && v < 7; }
		Check( "Rng.Int(n) stays in [0,n)", inRange );
		Check( "Rng.Int(0) is 0", new Rng( 1u ).Int( 0 ) == 0 );

		// Same tag+cfg ⇒ same song, twice in a row, in the same process. Both renders of each
		// seed come from the shared matrix pass, which the digest section reads too.
		foreach ( var (seed, first, second) in MatrixDigests() )
			if ( second != null )
				Check( $"song {seed} renders identically twice", first == second );

		// Different n ⇒ a different song (the infinite sequence must actually advance). 0:rotaliate:0
		// is in the matrix, so only its neighbour needs rendering.
		var stepped = Digest( Render( "0", "rotaliate", 1 ) );
		Check( "stepping n produces a different song",
			MatrixDigest( "0:rotaliate:0" ) != stepped );
	}

	static void HarmonyTests()
	{
		// ── a voicing's PERFECT intervals must be perfect, on every degree of every scale ──
		// Spelled diatonically (root degree + offset), a "power chord" or a "sus4" is only really
		// one on the degrees where the scale happens to hand it a perfect fifth. Every seven-note
		// scale has one degree whose fifth is DIMINISHED and one whose fourth is AUGMENTED, and on
		// those a power chord comes out a bare tritone — with no third present to explain it, which
		// is what "way off key" sounds like, at its worst through distortion. Harmony.VoicedTone
		// forces those two intervals; this asserts it across the whole cross product, which is
		// cheap because none of it renders.
		bool perfect = true;
		string worst = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var prof = GenreProfile.For( g );
			foreach ( var scale in prof.Scales )
				foreach ( var voicing in prof.Voicings )
					foreach ( var prog in prof.Progressions )
						foreach ( var deg in prog )
							foreach ( var offset in voicing )
							{
								if ( offset != Harmony.Fourth && offset != Harmony.Fifth ) continue;
								int root = Harmony.ScaleMidi( 48, scale, deg );
								int tone = Harmony.VoicedTone( 48, scale, deg, offset );
								int want = offset == Harmony.Fourth ? 5 : 7;
								if ( tone - root == want ) continue;
								perfect = false;
								worst = $"genre {g} degree {deg} offset {offset} = {tone - root} semitones";
							}
		}
		Check( "every voicing's fourth and fifth are perfect on every degree", perfect, worst );

		// …and the diatonic spelling really does go wrong somewhere, or the rule above is vacuous
		// and would keep passing after someone deleted it.
		bool diatonicBreaks = false;
		foreach ( var scale in GenreProfile.For( 1 ).Scales )
			for ( int deg = 0; deg < scale.Length; deg++ )
				diatonicBreaks |= Harmony.ScaleMidi( 48, scale, deg + Harmony.Fifth )
					- Harmony.ScaleMidi( 48, scale, deg ) != 7;
		Check( "a diatonic fifth really is diminished on some degree", diatonicBreaks );

		// ── a bend lands ON a note of the key ──
		// A player bends TO a note, not BY an interval: the string arrives at the next tone of the
		// scale, a whole step in some places and a semitone in others. Bent by a fixed depth it
		// lands off the key wherever the step is the other size, and a HELD bend then leaves the
		// note outside the key for its whole tail — which is what "out of tune" means when nothing
		// has been mistuned. Harmony.BendSemis chooses the note; depth is only how far it reaches.
		bool bendInKey = true, bendMoves = true;
		string bendAt = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var scale in GenreProfile.For( g ).Scales )
				for ( int pc = 0; pc < 12; pc++ )
					for ( float depth = 1f; depth <= 2f; depth++ )
					{
						int s = Harmony.BendSemis( scale, pc, depth );
						if ( s == 0 ) { bendMoves = false; bendAt = $"g{g} pc{pc} d{depth}"; continue; }
						bool inKey = false;
						foreach ( int t in scale )
							if ( (((t % 12) + 12) % 12) == (pc + s) % 12 ) inKey = true;
						if ( inKey && s <= Harmony.BendReach ) continue;
						bendInKey = false; bendAt = $"g{g} pc{pc} d{depth} -> +{s}";
					}
		Check( "a bend always lands on a tone of the song's scale", bendInKey, bendAt );
		Check( "a bend always has a note in reach", bendMoves, bendAt );

		// …and a fixed depth really would miss, or the rule above is vacuous: somewhere a whole-step
		// bend has to come back as a semitone because that is the step the scale has there.
		bool bendAdapts = false;
		foreach ( var scale in GenreProfile.For( 1 ).Scales )
			for ( int pc = 0; pc < 12; pc++ )
				bendAdapts |= Harmony.BendSemis( scale, pc, 2f ) != 2;
		Check( "a whole-step bend really is a semitone somewhere", bendAdapts );

		// ── a suspension is a delayed third, so it must have a third to land on ──
		// sus4 and sus2 put the fourth or the second in the third's place. Held for a whole song
		// that states no quality at all — every voice agrees and nothing is out of key, but there
		// is no major and no minor, and the ambiguity reads as dissonance. Harmony.SuspendedVoice
		// names the voice that owes a third and MusicGen.VoicingAt hands the chordal voices the
		// resolved spelling over the back half of each chord. What is asserted is the
		// CLASSIFICATION: a voicing is suspended exactly when it replaces the third rather than
		// omitting it (the power chord) or colouring it (the sixth, the add9).
		bool classified = true, resolves = true, anySus = false;
		string susAt = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var voicing in GenreProfile.For( g ).Voicings )
			{
				bool hasThird = Array.IndexOf( voicing, Harmony.Third ) >= 0;
				bool replaces = !hasThird && (Array.IndexOf( voicing, Harmony.Second ) >= 0
					|| Array.IndexOf( voicing, Harmony.Fourth ) >= 0);
				int sus = Harmony.SuspendedVoice( voicing );
				if ( (sus >= 0) != replaces )
				{
					classified = false;
					susAt = $"genre {g} voicing [{string.Join( " ", voicing )}] -> {sus}";
				}
				if ( sus < 0 ) continue;
				anySus = true;
				// The resolved spelling is the same chord with the suspension landed — same voice
				// count, so every voice keeps its index and the voice-leading table still applies.
				var res = (int[])voicing.Clone();
				res[sus] = Harmony.Third;
				resolves &= res.Length == voicing.Length
					&& Array.IndexOf( res, Harmony.Third ) >= 0;
			}
		Check( "a voicing is suspended exactly when it replaces the third", classified, susAt );
		Check( "a suspension resolves to a spelling that states the third", resolves );
		// …and some genre actually draws one, or neither check above is testing anything.
		Check( "some genre's voicings include a suspension", anySus );

		// ── the melodic view of a chord and the sounding one really do disagree ──
		// ChordDegrees is diatonic; the sounding chord is not, because VoicedTone forces the fourth
		// and the fifth perfect. Where they differ, a melody snapped to a "chord tone" in degree
		// space lands a semitone off the chord that is playing — which is why RenderTune follows the
		// degree snap with NearestSoundingTone. If this ever stops finding a case, that second snap
		// has become dead code and should go, rather than being kept because it looks careful.
		bool viewsDiffer = false;
		string differAt = "";
		for ( int g = 0; g < VibeCodec.GenreCount && !viewsDiffer; g++ )
		{
			var prof = GenreProfile.For( g );
			foreach ( var scale in prof.Scales )
				foreach ( var voicing in prof.Voicings )
					foreach ( var prog in prof.Progressions )
						foreach ( var deg in prog )
							foreach ( var offset in voicing )
							{
								int sounding = Harmony.VoicedTone( 48, scale, deg, offset ) % 12;
								int melodic = Harmony.ScaleMidi( 48, scale, deg + offset ) % 12;
								if ( sounding == melodic ) continue;
								viewsDiffer = true;
								differAt = $"genre {g} degree {deg} offset {offset}: "
									+ $"sounds pc {sounding}, degree view says pc {melodic}";
							}
		}
		Check( "the diatonic chord-tone view differs from the sounding chord somewhere",
			viewsDiffer, differAt );

		// ── voice leading: a chord change must not move the whole comp in parallel ──
		// Built upward from its root degree, a chord's register is wherever that degree falls, so
		// a progression that steps a third slides every voice a tenth with no common tone — the
		// "it jumped" that reads loudest when a change lands on a section boundary.
		// Harmony.PlanVoiceLeading octave-shifts each voice to the inversion nearest the chord
		// before it. The progression is a CYCLE, so the wrap from the last chord back to the first
		// is checked like any other change: an anchored chain would just park every leap there.
		bool led = true, spelled = true, floored = true, anchored = true;
		int worstLed = 0, worstRoot = 0;
		long movedLed = 0, movedRoot = 0, bigLed = 0, bigRoot = 0;
		string leapAt = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var prof = GenreProfile.For( g );
			foreach ( var scale in prof.Scales )
				foreach ( var voicing in prof.Voicings )
					foreach ( var prog in prof.Progressions )
					{
						var plan = Harmony.PlanVoiceLeading( scale, prog, voicing );
						var shift = plan.Shift; var rot = plan.Rot;
						int nv = voicing.Length;
						for ( int c = 0; c < prog.Length; c++ )
						{
							int p = (c + prog.Length - 1) % prog.Length;
							for ( int i = 0; i < voicing.Length; i++ )
							{
								// The plan ROTATES, so voice i is not on voicing slot i — it plays
								// (i + rot[c]) mod n, and that is the whole point: it is what lets a
								// voice hold a common tone across a change instead of sliding with
								// the shape. Reading voicing[i] here would measure a part nobody
								// plays.
								int raw = Harmony.VoicedTone( 0, scale, prog[c], voicing[(i + rot[c]) % nv] );
								int rawPrev = Harmony.VoicedTone( 0, scale, prog[p], voicing[(i + rot[p]) % nv] );
								// The CONTROL is the unplanned spelling — no rotation and no octave
								// shift — because that is what the plan is being measured against.
								// Rotating it too would compare the plan with half of itself.
								int flat = Harmony.VoicedTone( 0, scale, prog[c], voicing[i] );
								int flatPrev = Harmony.VoicedTone( 0, scale, prog[p], voicing[i] );
								int now = raw + shift[c][i], before = rawPrev + shift[p][i];
								// The chord is re-voiced, never re-spelled: same note, other octave.
								spelled &= (now - raw) % 12 == 0;
								floored &= now >= Harmony.VoiceLeadFloor;
								anchored &= Math.Abs( shift[c][i] ) <= Harmony.MaxVoiceLead;
								int move = Math.Abs( now - before ), rootMove = Math.Abs( flat - flatPrev );
								movedLed += move;
								movedRoot += rootMove;
								if ( move > 6 ) bigLed++;
								if ( rootMove > 6 ) bigRoot++;
								worstRoot = Math.Max( worstRoot, rootMove );
								if ( move > worstLed )
								{
									worstLed = move;
									leapAt = $"genre {g} prog [{string.Join( " ", prog )}] voicing "
										+ $"[{string.Join( " ", voicing )}] chord {c} voice {i}: "
										+ $"{before} → {now} (root position {rawPrev} → {raw})";
								}
								if ( move >= 12 ) led = false;
							}
						}
					}
		}
		Check( "no voice leaps an octave or more between chords", led,
			$"worst {worstLed} semitones at {leapAt}" );
		Check( "voice leading re-voices the chord, never re-spells it", spelled );
		Check( "no voice is led below the register the chord is voiced up from", floored );
		Check( "a voice is shifted at most one octave", anchored );
		// …and the unled spelling really does leap, or the checks above are vacuous. A root degree
		// resolves inside one octave, so the worst PARALLEL leap the old spelling could make is a
		// major seventh — which is what "no common tone, the whole comp moved" was.
		Check( "root-position spelling really does leap a seventh somewhere", worstRoot >= 10,
			$"worst unled leap {worstRoot} semitones" );
		// The claim is TOTAL motion, not the single worst leap: a voice already led under its floor
		// may still have to jump back up, and one voice jumping while the others hold is a
		// re-voicing rather than the whole comp sliding. The total can only fall so far — a root
		// move of a fourth is under a tritone already and octave-shifting leaves it alone — so the
		// sharper measure is how many changes still throw a voice more than a tritone.
		Check( "voice leading moves the comp less than root position", movedLed * 5 < movedRoot * 4,
			$"led {movedLed} vs root-position {movedRoot} semitones over all changes" );
		// The ones that survive are the price of closing the cycle: a single wider move that buys
		// smaller ones on the other three changes is the solution, not a failure of it. There are
		// few enough of them now to be worth an absolute cap and not only a ratio — octave-shifting
		// alone left 211 of these, and letting the chord ROTATE (a voice takes whichever note of the
		// voicing keeps it still, rather than being stuck on its own slot) is what took them into
		// single figures. A cap catches a table edit that quietly puts them back; the ratio alone
		// would not, because it scales with the tables.
		Check( "voice leading removes all but a handful of the big leaps",
			bigLed * 3 < bigRoot && bigLed <= 16,
			$"{bigLed} led vs {bigRoot} root-position moves over a tritone; worst {leapAt}" );

		// ScaleMidi wraps octaves rather than clamping, so a progression may run off either
		// end of its scale table and still yield a sane pitch.
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var scales = GenreProfile.For( g ).Scales;
			Check( $"genre {g} has at least one scale", scales.Length > 0 );

			bool monotonic = true, sane = true;
			foreach ( var scale in scales )
			{
				int prev = int.MinValue;
				for ( int deg = -14; deg <= 21; deg++ )
				{
					int m = Harmony.ScaleMidi( 48, scale, deg );
					monotonic &= m >= prev;
					sane &= m > 0 && m < 128;
					prev = m;
				}
			}
			Check( $"genre {g} ScaleMidi rises monotonically across octave wrap", monotonic );
			Check( $"genre {g} ScaleMidi stays in MIDI range", sane );
		}

		// A progression entry is a scale degree; ChordRoot must map every entry of every
		// genre's every progression to a real pitch.
		bool chordsSane = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var prog in GenreProfile.For( g ).Progressions )
			{
				chordsSane &= prog.Length > 0;
				foreach ( var deg in prog )
					foreach ( var scale in GenreProfile.For( g ).Scales )
					{
						int m = Harmony.ChordRoot( 48, scale, deg );
						chordsSane &= m > 0 && m < 128;
					}
			}
		Check( "every progression degree resolves to a MIDI pitch", chordsSane );

		// Genres drawing the SAME changes is how six genres came to sound like one. A little
		// overlap is honest — I–IV–V belongs to everyone — but two genres sharing several
		// progressions can produce byte-identical harmony, so cap it at one.
		int worstPair = 0; string worstWhich = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			for ( int h = g + 1; h < VibeCodec.GenreCount; h++ )
			{
				int shared = 0;
				foreach ( var a in GenreProfile.For( g ).Progressions )
					foreach ( var b in GenreProfile.For( h ).Progressions )
						if ( SameDegrees( a, b ) ) shared++;
				if ( shared > worstPair ) { worstPair = shared; worstWhich = $"{g}/{h}"; }
			}
		Check( "no two genres share more than one progression", worstPair <= 1,
			$"genres {worstWhich} share {worstPair}" );

		// Genres drawing the same MODE under different changes is the same failure one level down,
		// so the scale tables carry the same cap as the progressions.
		int worstScales = 0; string worstScalePair = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			for ( int h = g + 1; h < VibeCodec.GenreCount; h++ )
			{
				int shared = 0;
				foreach ( var a in GenreProfile.For( g ).Scales )
					foreach ( var b in GenreProfile.For( h ).Scales )
						if ( SameDegrees( a, b ) ) shared++;
				if ( shared > worstScales ) { worstScales = shared; worstScalePair = $"{g}/{h}"; }
			}
		Check( "no two genres share more than one scale", worstScales <= 1,
			$"genres {worstScalePair} share {worstScales}" );

		// Weights are real weights now, not repeated table entries: every table must carry one
		// weight per entry, or the draw silently falls back to an unweighted pick.
		bool weighted = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			weighted &= p.ScaleWeights != null && p.ScaleWeights.Length == p.Scales.Length;
			weighted &= p.VoicingWeights != null && p.VoicingWeights.Length == p.Voicings.Length;
			weighted &= p.GrooveWeights != null && p.GrooveWeights.Length == p.Grooves.Length;
		}
		Check( "every weighted table has one weight per entry", weighted );

		// A weighted draw must actually lean — and must still reach the light entries, or the
		// table's rare colours would be dead code.
		var wSeen = new Dictionary<int, int>();
		var wTable = new[] { 10, 20, 30 };
		for ( int i = 0; i < 3000; i++ )
		{
			int v = new Rng( $"w:{i}" ).PickWeighted( wTable, new[] { 6, 3, 1 } );
			wSeen[v] = wSeen.GetValueOrDefault( v ) + 1;
		}
		Check( "a weighted draw leans on its heavy entry", wSeen[10] > wSeen[20] && wSeen[20] > wSeen[30] );
		Check( "a weighted draw still reaches its light entry", wSeen[30] > 0 );

		// Bass lines are Patterns now, so what matters is that a genre HAS a library, that the
		// patterns are whole bars of eighths, and that no cell would index off a table.
		bool bassSane = true, multiBar = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var p in GenreProfile.For( g ).BassPatterns )
			{
				bassSane &= p.LengthTicks > 0 && p.LengthTicks % Timing.TicksPerEighth == 0;
				bassSane &= p.Count > 0;
				multiBar |= p.LengthTicks > 4 * Timing.TicksPerBeat;
			}
		Check( "every bass pattern is a whole number of eighths", bassSane );
		Check( "some bass lines are longer than one bar", multiBar );

		// The libraries used to share literal rows ({0,0,0,0,0,0,0,App} was in four of five), which
		// meant four genres could play the identical bass line.
		int sharedBass = 0;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			for ( int h = g + 1; h < VibeCodec.GenreCount; h++ )
				foreach ( var a in GenreProfile.For( g ).BassPatterns )
					foreach ( var b in GenreProfile.For( h ).BassPatterns )
						if ( ReferenceEquals( a, b ) ) sharedBass++;
		Check( "no two genres share a bass pattern", sharedBass == 0 );

		Check( "Midi(69) is A440", Math.Abs( Osc.Midi( 69 ) - 440f ) < 0.001f );
		Check( "Midi(81) is an octave above Midi(69)",
			Math.Abs( Osc.Midi( 81 ) - 2f * Osc.Midi( 69 ) ) < 0.01f );
	}

	static void TimingTests()
	{
		// A bar of 4/4 at 48 ticks/beat, one second long, so the arithmetic is checkable by eye.
		const int spb = 4, ticks = 4 * Timing.TicksPerBeat;
		var straight = new Timing( spb, ticks * 8, 44100.0 / Timing.TicksPerBeat, 0f, 0, 44100 );

		Check( "an eighth is a whole number of ticks", Timing.TicksPerEighth * 2 == Timing.TicksPerBeat );
		Check( "a bar is BeatsPerBar beats", straight.BarTicks == spb * Timing.TicksPerBeat );
		Check( "the eighth grid divides the bar", straight.BarTicks % Timing.TicksPerEighth == 0 );
		Check( "beat grouping covers the bar", straight.BeatGrouping.Length == spb );

		// The tick grid must render every subdivision the engine actually uses — 8ths, 16ths,
		// and both 8th- and 16th-note triplets — as exact integers. This is the whole reason
		// TicksPerBeat is 48 and not a 16th-note grid.
		foreach ( var div in new[] { 2, 3, 4, 6, 8, 12, 16 } )
			Check( $"a 1/{div} of a beat is an exact tick count", Timing.TicksPerBeat % div == 0 );

		// Straight time: the accumulator must be linear, monotonic and anchored at 0.
		Check( "tick 0 is sample 0", straight.TickToSample( 0 ) == 0 );
		bool monotonic = true, linear = true;
		int prev = -1;
		for ( int t = 0; t <= ticks * 4; t++ )
		{
			int s = straight.TickToSample( t );
			monotonic &= s >= prev; prev = s;
			linear &= Math.Abs( s - t * (44100.0 / Timing.TicksPerBeat) ) <= 1.0;
		}
		Check( "sample positions never go backwards", monotonic );
		Check( "straight time is linear in ticks", linear );
		Check( "one beat is one second at this tempo",
			Math.Abs( straight.TickToSample( Timing.TicksPerBeat ) - 44100 ) <= 1 );

		// Swing warps the grid WITHOUT moving the anchors: on-beat eighths stay put, offbeats
		// are pushed late. A shuffle that moved the downbeat would drag the whole band.
		var swung = new Timing( spb, ticks * 8, 44100.0 / Timing.TicksPerBeat, 0.2f, 0, 44100 );
		bool anchored = true, pushed = true;
		for ( int e = 0; e < 8; e++ )
		{
			int t = e * Timing.TicksPerEighth;
			if ( e % 2 == 0 ) anchored &= swung.TickToSample( t ) == straight.TickToSample( t );
			else pushed &= swung.TickToSample( t ) > straight.TickToSample( t );
		}
		Check( "swing leaves on-beat eighths anchored", anchored );
		Check( "swing pushes offbeat eighths late", pushed );
		Check( "swing keeps positions monotonic", Monotonic( swung, ticks * 4 ) );

		// Durations are spans, not positions — they must not pick up the swing.
		Check( "a span of ticks carries no swing",
			swung.SamplesForTicks( Timing.TicksPerEighth ) == straight.SamplesForTicks( Timing.TicksPerEighth ) );
		Check( "SecondsForTicks agrees with SamplesForTicks",
			Math.Abs( straight.SecondsForTicks( ticks ) * 44100 - straight.SamplesForTicks( ticks ) ) <= 1 );

		// TUPLETS ARE EVEN. A tuplet divides its own span into equal parts, in a straight song
		// and a swung one alike — a shuffle is itself a triplet feel, so a triplet sits evenly
		// against the beat rather than being shuffled a second time.
		foreach ( var t in new[] { straight, swung } )
			foreach ( var n in new[] { 3, 4, 6 } )
			{
				int start = Timing.TicksPerEighth * 6;              // the last beat of a 4/4 bar
				double span = 2.0 * Timing.TicksPerEighth;
				var at = new int[n + 1];
				for ( int i = 0; i <= n; i++ ) at[i] = t.EvenSpan( start, span, i / (double)n );

				int first = at[1] - at[0];
				bool even = true;
				for ( int i = 1; i < n; i++ ) even &= Math.Abs( (at[i + 1] - at[i]) - first ) <= 1;
				Check( $"a {n}-tuplet is evenly spaced ({(t == swung ? "swung" : "straight")} song)", even );
			}

		// …but the tuplet's endpoints still land where the groove puts them, so it starts and
		// finishes with the band rather than drifting off the beat.
		int beat = Timing.TicksPerEighth * 6;
		Check( "a tuplet starts on the groove's own position",
			swung.EvenSpan( beat, 2.0 * Timing.TicksPerEighth, 0 ) == swung.TickToSample( beat ) );
		Check( "a tuplet ends on the groove's own position",
			swung.EvenSpan( beat, 2.0 * Timing.TicksPerEighth, 1 )
				== swung.TickToSample( beat + 2 * Timing.TicksPerEighth ) );

		// Even spacing is a real property of EvenSpan, not something swing would give anyway:
		// the same three points taken off the warped grid are audibly unequal. Without this,
		// the checks above would pass whether or not tuplets were handled specially.
		int a0 = swung.TickToSample( beat );
		int a1 = swung.TickToSample( beat + (int)(2.0 * Timing.TicksPerEighth / 3) );
		int a2 = swung.TickToSample( beat + (int)(4.0 * Timing.TicksPerEighth / 3) );
		Check( "the grid itself is uneven across a beat, so even spacing is a real constraint",
			Math.Abs( (a2 - a1) - (a1 - a0) ) > 1 );

		// ── the tempo curve ──
		// Tempo is an accumulator, so a per-section tempo and an ending ritard are just a delta
		// that varies as the loop runs. A song that slows down must take LONGER, stay monotonic,
		// and report its real length (a constant-tempo estimate would clip the ending).
		const double d0 = 44100.0 / Timing.TicksPerBeat;
		int total = ticks * 8;
		var ritard = new Timing( spb, total, t => d0 * (t < total / 2 ? 1.0 : 1.25), 0f, 0, 44100 );
		Check( "a tempo curve keeps positions monotonic", Monotonic( ritard, total ) );
		Check( "the first half of a ritard matches straight time",
			Math.Abs( ritard.TickToSample( total / 2 ) - straight.TickToSample( total / 2 ) ) <= 1 );
		Check( "a slowing song runs longer than a straight one",
			ritard.TotalSamples > straight.TickToSample( total ) );
		Check( "TotalSamples reads the finished accumulator",
			Math.Abs( ritard.TotalSamples - ritard.TickToSample( total + 1 ) ) <= 2 );

		// Note LENGTHS follow the curve too, or a note in the slow section would be cut short.
		Check( "a span in the slow section is longer than the same span in the fast one",
			ritard.SpanSamples( total * 3 / 4, Timing.TicksPerBeat )
				> ritard.SpanSamples( total / 4, Timing.TicksPerBeat ) );
		Check( "SpanSeconds agrees with SpanSamples",
			Math.Abs( straight.SpanSeconds( 0, ticks ) * 44100 - straight.SpanSamples( 0, ticks ) ) <= 1 );

		// Reading past the end must clamp rather than throw — the ending renders into the tail.
		bool threw = false;
		try { straight.TickToSample( ticks * 100 ); } catch { threw = true; }
		Check( "reading past the song's last tick clamps", !threw );
	}

	static bool Monotonic( Timing t, int ticks )
	{
		int prev = -1;
		for ( int i = 0; i <= ticks; i++ ) { int s = t.TickToSample( i ); if ( s < prev ) return false; prev = s; }
		return true;
	}

	static void GenreProfileTests()
	{
		// ── the comp figure's density spread ──
		// A section draws its comp figure from the genre's table and the figures are not equally
		// dense, so how loud the backing sits was decided by a draw — rock measured 4 dB apart
		// between the seed the suite balances on and the genre's own average. Neither tool could
		// see it: --levels averages the spread away and the balance check below is a single seed.
		// This is the one that watches it, and it is free because it is a property of the TABLES —
		// no render is needed to know how far apart two figures are.
		//
		// The measure is the incoherent-sum level a figure implies (√onsets per tick) times its
		// trim, in dB, worst pair per genre. Untrimmed it has to FAIL the same bound, or the check
		// passes on a genre whose figures were already even and proves nothing.
		float worstTrimmed = 0f, worstRaw = 0f;
		string spreadAt = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var prof = GenreProfile.For( g );
			foreach ( var table in new[] { prof.CompFigures, prof.LoudCompFigures } )
			{
				if ( table == null || table.Length < 2 ) continue;
				float loT = float.MaxValue, hiT = 0f, loR = float.MaxValue, hiR = 0f;
				// The flourish is measured WITH the table it drops into, not as part of it. It is
				// the densest thing the voice plays and it is trimmed against the table's mean, so
				// leaving it out is how this check would go vacuous — pulling the ornaments out of
				// the tables took the untrimmed spread from 4 dB to 1.8 and the check said so.
				var played = new List<Pattern>( table );
				if ( ReferenceEquals( table, prof.CompFigures ) && prof.CompOrnament != null )
					played.Add( prof.CompOrnament );
				foreach ( var fig in played )
				{
					float d = MathF.Sqrt( fig.Count / (float)fig.LengthTicks );
					float t = d * MusicGen.DensityTrim( fig, table );
					loT = MathF.Min( loT, t ); hiT = MathF.Max( hiT, t );
					loR = MathF.Min( loR, d ); hiR = MathF.Max( hiR, d );
				}
				float dbT = 20f * MathF.Log10( hiT / loT ), dbR = 20f * MathF.Log10( hiR / loR );
				if ( dbT > worstTrimmed ) { worstTrimmed = dbT; spreadAt = $"genre {g}"; }
				worstRaw = MathF.Max( worstRaw, dbR );
			}
		}
		Check( "the comp figures a genre draws from sit within 2 dB of each other",
			worstTrimmed <= 2f, $"worst {worstTrimmed:0.0} dB at {spreadAt}" );
		Check( "…and untrimmed they do not, so the trim is not measuring nothing",
			worstRaw > 2f, $"worst untrimmed {worstRaw:0.0} dB" );

		// ── fill density ──
		// A genre's FillHits is a TARGET the grid has to be able to reach: the per-cell chances
		// saturate on the beats first, so a big enough number quietly buys nothing. Rock's is the
		// one measured figure (13.2/bar) and it is the one that must land exactly; the rest are
		// design calls and are only asked to be reachable — which they are because the model water-
		// fills its ornament cells rather than scaling flat, and metal is the genre that proved it
		// had to (a flat scale played 13.4 of the 14 it asks for).
		float worstMiss = 0f; string missAt = "";
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			float want = GenreProfile.For( g ).FillHits;
			float got = MusicGen.FillDensityOnGrid( want );
			if ( want - got > worstMiss ) { worstMiss = want - got; missAt = $"genre {g} wants {want:0.0}, plays {got:0.0}"; }
		}
		Check( "every genre's fill density is one the fill grid can actually reach",
			worstMiss <= 0.05f, missAt );
		Check( "rock's fill plays the density that was measured off the dataset",
			MathF.Abs( MusicGen.FillDensityOnGrid( GenreProfile.For( 1 ).FillHits ) - 13.2f ) < 0.3f,
			$"{MusicGen.FillDensityOnGrid( GenreProfile.For( 1 ).FillHits ):0.00}/bar" );
		// The old floor was 16 hits a bar with no branch anywhere that played fewer, so every
		// genre must now sit under it — otherwise the whole row bought a rename.
		bool underOldFloor = true, densitiesDiffer = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			underOldFloor &= MusicGen.FillDensityOnGrid( GenreProfile.For( g ).FillHits ) < 16f;
			densitiesDiffer |= GenreProfile.For( g ).FillHits != GenreProfile.For( 0 ).FillHits;
			Check( $"genre {g} weights all four fill shapes",
				GenreProfile.For( g ).FillShapes.Length == 4, null );
		}
		Check( "every genre's fill is sparser than the old unconditional sixteenth floor",
			underOldFloor, null );
		Check( "…and fill density is per genre rather than one number in six coats",
			densitiesDiffer, null );

		// Swing is genre character rather than a knob, and it is a YES/NO before it is a depth: a
		// genre that never swings declares SwingChance 0 and needs no band at all. So a draw is one
		// of exactly three things — straight, somewhere in the genre's swing band, or somewhere in
		// its shuffle band — and nothing may land between them. A value just above zero is the
		// specific thing this shape exists to make unrepresentable: it is inaudible, so it is a
		// straight song claiming a feel.
		int swingingGenres = 0, straightOnlyGenres = 0;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			Check( $"genre {g} swing chance is a probability", p.SwingChance >= 0f && p.SwingChance <= 1f );
			Check( $"genre {g} swing band is ordered", p.SwingMin <= p.SwingMax );
			Check( $"genre {g} swing band is in range", p.SwingMin >= 0f && p.SwingMax <= 0.4f );
			// A genre that swings must swing AUDIBLY when it does. 0.08 of an eighth is around
			// 18 ms at these tempos; below that the warp is a micro-timing texture, not a groove.
			if ( p.SwingChance > 0f )
				Check( $"genre {g} swings audibly when it swings", p.SwingMin >= 0.08f );
			if ( p.SwingChance > 0f ) swingingGenres++; else straightOnlyGenres++;

			bool inBand = true, fastNeverDeeper = true, straightWhenNever = true;
			for ( int i = 0; i < 300; i++ )
			{
				var rng = new Rng( $"swing:{g}:{i}" );
				float s = p.DrawSwing( rng, false );
				bool straight = s == 0f;
				bool swung = s >= p.SwingMin - 0.0001f && s <= p.SwingMax + 0.0001f;
				bool shuffled = p.ShuffleChance > 0f
					&& s >= p.ShuffleMin - 0.0001f && s <= p.ShuffleMax + 0.0001f;
				inBand &= straight || swung || shuffled;
				straightWhenNever &= p.SwingChance > 0f || straight;

				// THE REGRESSION THIS SHAPE FIXES. An uptempo song never shuffles and swings LESS
				// OFTEN — but when it does swing it swings at the genre's own depth. The old code
				// halved the depth instead, which put the shallow end of the band under the
				// audibility threshold, and did it worst where the eighth was already shortest: a
				// fast ska song came out at ~9 ms of push, i.e. straight but not saying so.
				float fast = p.DrawSwing( new Rng( $"swing:{g}:{i}" ), true );
				fastNeverDeeper &= fast == 0f
					|| (fast >= p.SwingMin - 0.0001f && fast <= p.SwingMax + 0.0001f);
			}
			Check( $"genre {g} draws straight, its band, or its shuffle band", inBand );
			Check( $"genre {g} is straight when its swing chance is zero", straightWhenNever );
			Check( $"genre {g} never swings shallower than its band when fast", fastNeverDeeper );
		}
		// Both checks above are vacuous unless the roster actually contains each kind.
		Check( "some genres swing", swingingGenres > 0, $"{swingingGenres} of {VibeCodec.GenreCount}" );
		Check( "some genres never swing", straightOnlyGenres > 0, $"{straightOnlyGenres} of {VibeCodec.GenreCount}" );

		// ── the shuffle feel ──
		// A 2:1 triplet shuffle is a different FEEL, not a wider swing — widening a genre's band to
		// reach ~0.33 would just make its ordinary songs sloppy on the way there. Country is the
		// genre that kept it: ska is third wave now, and the third wave is straight.
		var country = GenreProfile.For( 2 );
		bool straightGenres = true;
		foreach ( var g in new[] { 0, 1, 3, 4, 5 } ) straightGenres &= GenreProfile.For( g ).ShuffleChance == 0f;
		Check( "country can draw a shuffle", country.ShuffleChance > 0f );
		Check( "every other genre never shuffles", straightGenres );
		Check( "the shuffle band sits at a real 2:1 triplet",
			country.ShuffleMin >= 0.28f && country.ShuffleMax <= 0.4f );
		Check( "the shuffle band is clear of the swing band", country.ShuffleMin > country.SwingMax );

		int shuffles = 0;
		for ( int i = 0; i < 400; i++ )
			if ( country.DrawSwing( new Rng( $"sh:{i}" ), false ) >= country.ShuffleMin )
				shuffles++;
		Check( "some country songs shuffle and most do not", shuffles > 20 && shuffles < 200,
			$"{shuffles} of 400" );

		// The point of the row: the genres differ in FEEL, not just in tempo. Without this the
		// chances could all quietly collapse to the same value and nothing would notice.
		var ska = GenreProfile.For( 0 );
		var rock = GenreProfile.For( 1 );
		Check( "metal never swings", GenreProfile.For( 3 ).SwingChance == 0f );
		Check( "punk never swings", GenreProfile.For( 4 ).SwingChance == 0f );
		Check( "pop never swings", GenreProfile.For( 5 ).SwingChance == 0f );
		// Third-wave ska is straight — the shuffle belongs to the first wave, which this genre is
		// no longer tuned as. If a first-wave or two-tone genre is ever added (see PLAN.md), that
		// is where a swung offbeat comes back; it does not come back here.
		Check( "third-wave ska never swings", ska.SwingChance == 0f );
		Check( "country swings more often than rock", country.SwingChance > rock.SwingChance );

		// Swing varies song to song rather than being one constant per genre — and the straight
		// songs are part of that variation, so the count includes zero.
		var seen = new HashSet<int>();
		for ( int i = 0; i < 100; i++ )
			seen.Add( (int)Math.Round( country.DrawSwing( new Rng( $"cy:{i}" ), false ) * 1000 ) );
		Check( "swing varies between songs of the same genre", seen.Count > 10, $"{seen.Count} distinct values" );
		Check( "a genre that swings still writes straight songs", seen.Contains( 0 ) );

		// A song's swing must follow only from its seed, or the shuffle line would not be
		// reproducible.
		Check( "a song's swing is reproducible from its seed",
			country.DrawSwing( new Rng( "cy:7" ), false ) == country.DrawSwing( new Rng( "cy:7" ), false ) );

		// ── the loud comp (a genre's dynamic) ──
		// Where a genre changes technique for its loud sections, the pieces must all be present:
		// a figure table to play and a style to play it in. A half-wired one would silently comp
		// the loud sections with the quiet style and nothing would fail.
		int loudGenres = 0;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			if ( p.LoudCompFigures == null ) continue;
			loudGenres++;
			Check( $"genre {g} loud comp has figures", p.LoudCompFigures.Length > 0 );
			Check( $"genre {g} loud comp differs from its quiet comp", p.LoudComp != p.Comp );
			// Reachable: the threshold must sit at or below the loudest energy any of its own
			// sections actually reach, or the loud comp is dead code.
			float peak = 0f;
			foreach ( var part in p.Form ) peak = Math.Max( peak, part.Energy );
			Check( $"genre {g} actually reaches its loud threshold", peak >= p.LoudFrom,
				$"peak energy {peak:0.00} vs LoudFrom {p.LoudFrom:0.00}" );
			// And it must NOT be reached by every section, or the quiet comp is the dead one.
			float trough = 1f;
			foreach ( var part in p.Form ) trough = Math.Min( trough, part.Energy );
			Check( $"genre {g} still has quiet sections", trough < p.LoudFrom );
		}
		Check( "some genre has a loud comp", loudGenres > 0, $"{loudGenres} of {VibeCodec.GenreCount}" );

		// SWING must be gone from the seed grid — that is what "not a knob" means on the wire.
		bool noSwingKnob = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var f in VibeCodec.Fields( g ) )
				noSwingKnob &= !f.Name.Equals( "SWING", StringComparison.OrdinalIgnoreCase );
		Check( "SWING is not a vibe knob in any genre", noSwingKnob );

		// ── tempo bands ──
		// The profile covers every genre the codec offers, or a genre would draw someone else's
		// character.
		Check( "there is a profile for every genre", GenreProfile.Count == VibeCodec.GenreCount );

		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			Check( $"genre {g} tempo band is ordered and playable",
				p.BpmMin <= p.BpmMax && p.BpmMin >= 40 && p.BpmMax <= 300 );
			Check( $"genre {g} uptempo band is ordered and playable",
				p.FastBpmMin <= p.FastBpmMax && p.FastBpmMin >= 40 && p.FastBpmMax <= 300 );
			Check( $"genre {g} uptempo band is not slower than its main band",
				p.FastBpmMin >= p.BpmMin && p.FastBpmMax >= p.BpmMax );

			bool inBand = true;
			for ( int i = 0; i < 200; i++ )
			{
				int bpm = p.DrawBpm( new Rng( $"bpm:{g}:{i}" ), false, 1f );
				inBand &= bpm >= p.BpmMin && bpm <= p.BpmMax;
				int fast = p.DrawBpm( new Rng( $"bpm:{g}:{i}" ), true, 1f );
				inBand &= fast >= p.FastBpmMin && fast <= p.FastBpmMax;
			}
			Check( $"genre {g} draws inside its tempo band", inBand );

			// The knob's saturation points. A single symmetric 0.70–1.45 multiplier put the ends of
			// the slider outside every genre at once (ska 268, metal 290, country 67), so each genre
			// says where it stops being itself and the knob keeps its full travel in the UI.
			Check( $"genre {g} saturation contains its own bands",
				p.TempoFloor <= p.BpmMin && p.TempoCeil >= p.FastBpmMax,
				$"{p.TempoFloor}–{p.TempoCeil} vs {p.BpmMin}–{p.FastBpmMax}" );

			bool playable = true;
			for ( int i = 0; i < 200; i++ )
				foreach ( var scale in new[] { 0.70f, 0.85f, 1f, 1.2f, 1.45f } )
				{
					int b = p.DrawBpm( new Rng( $"knob:{g}:{i}" ), i % 2 == 0, scale );
					playable &= b >= p.TempoFloor && b <= p.TempoCeil;
				}
			Check( $"genre {g} stays playable across the whole TEMPO knob", playable );

			Check( $"genre {g} has a harmonic rhythm of at least a bar", p.ChordBars >= 1 );
		}

		// …and the saturation is not vacuous: the knob's ends must actually be clamped somewhere,
		// or these are six numbers that do nothing.
		bool everSaturates = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			for ( int i = 0; i < 50; i++ )
				everSaturates |= p.DrawBpm( new Rng( $"sat:{g}:{i}" ), true, 1.45f ) == p.TempoCeil
					|| p.DrawBpm( new Rng( $"sat:{g}:{i}" ), false, 0.70f ) == p.TempoFloor;
		}
		Check( "the TEMPO knob saturates at the genre's bound", everSaturates );

		// The whole point of the row: the bands must actually separate the genres. Punk and
		// country are the two extremes, and nothing in between may collapse them all into one
		// shared range again.
		Check( "punk is faster than country can reach",
			GenreProfile.For( 4 ).BpmMin > GenreProfile.For( 2 ).BpmMax );
		var bands = new HashSet<(int, int)>();
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			bands.Add( (GenreProfile.For( g ).BpmMin, GenreProfile.For( g ).BpmMax) );
		Check( "genres do not share one tempo band", bands.Count >= 4, $"{bands.Count} distinct bands" );

		// The TEMPO knob rides on top of the band rather than replacing it.
		Check( "the tempo knob pushes the drawn tempo",
			ska.DrawBpm( new Rng( "t" ), false, 1.4f ) > ska.DrawBpm( new Rng( "t" ), false, 0.7f ) );

		// ── grooves ──
		// Rock, country AND punk used to fall through to one shared `default` backbeat. Each genre
		// draws from its own table now, and no two tables may hold the same groove object.
		bool grooveSane = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var gr in GenreProfile.For( g ).Grooves )
			{
				grooveSane &= gr.Kick != null && gr.Snare != null && gr.Cymbal != null;
				grooveSane &= gr.Kick.Count > 0 && gr.Snare.Count > 0 && gr.Cymbal.Count > 0;
			}
		Check( "every groove names a kick, a snare and a cymbal part", grooveSane );

		int sharedGrooves = 0;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			for ( int h = g + 1; h < VibeCodec.GenreCount; h++ )
				foreach ( var a in GenreProfile.For( g ).Grooves )
					foreach ( var b in GenreProfile.For( h ).Grooves )
						if ( ReferenceEquals( a, b ) ) sharedGrooves++;
		Check( "no two genres share a groove", sharedGrooves == 0 );

		// Country's train beat is the one that did not exist at all: a snare on every sixteenth,
		// ghosted except on the backbeat. If it collapses back to a plain backbeat, country's
		// drums are rock's drums again.
		var train = GenreProfile.For( 2 ).Grooves[0];
		Check( "country has a running train-beat snare",
			train.Snare.Count >= 12 && train.Snare.LengthTicks <= 4 * Timing.TicksPerBeat );
		// Punk's cymbal hand never stops — that one IS deliberate.
		Check( "punk drives the cymbal on every eighth",
			GenreProfile.For( 4 ).Grooves[0].Cymbal.Count >= 8 );

		// Metal's double kick is a BURST, and the two halves of that are separately assertable:
		// it must still reach the sixteenth somewhere (or it is not a double kick), and it must
		// NOT be the sixteenth everywhere (or it is the unbroken wall this replaced, which read as
		// a blast beat at every tempo because nothing in it ever changed).
		var dk = GenreProfile.For( 3 ).Grooves[0].Kick;
		int bars = Math.Max( 1, dk.LengthTicks / (4 * Timing.TicksPerBeat) );
		int sixteenthsPerBar = 4 * Timing.TicksPerBeat / (Timing.TicksPerEighth / 2);
		int longestRun = 0, run = 0, prev = int.MinValue;
		foreach ( var h in dk.Slice( 0, dk.LengthTicks ) )
		{
			run = h.Tick - prev == Timing.TicksPerEighth / 2 ? run + 1 : 1;
			longestRun = Math.Max( longestRun, run );
			prev = h.Tick;
		}
		Check( "metal's double kick still bursts at the sixteenth", longestRun >= sixteenthsPerBar,
			$"longest run {longestRun}" );
		Check( "metal's double kick is a burst, not an unbroken wall",
			bars > 1 && dk.Count < bars * sixteenthsPerBar,
			$"{dk.Count} hits over {bars} bars" );

		bool grooveDrawn = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			for ( int i = 0; i < 50; i++ )
				grooveDrawn &= GenreProfile.For( g ).DrawGroove( new Rng( $"gr:{g}:{i}" ) ) != null;
		Check( "every genre draws a real groove", grooveDrawn );

		// ── the thirty-second ──
		// TicksPerBeat = 48, so a 32nd is exactly 6 ticks — the same clean division that gives a
		// sixteenth 12 — and it lands on the 1-tick grid the voices are measured against.
		var t32 = Pattern.ThirtySeconds( 0, 0, 0, 0, 0, 0, 0, 0 );
		Check( "a thirty-second is 6 ticks and divides the beat",
			Timing.TicksPerBeat % (Timing.TicksPerEighth / 4) == 0
			&& t32.LengthTicks == Timing.TicksPerBeat && t32.Count == 8 );

		// The subdivision is an ALLOWANCE, not a genre's property. There is deliberately no rule
		// here that the kit stays coarser than the guitar, or that a genre must be slow to reach a
		// 32nd: a roll, a drag, a grace note and a flurry are ordinary vocabulary in every genre,
		// and what makes them playable is that they are a handful of notes rather than a bar of
		// them. So what is asserted is that the engine CAN express it and that something does —
		// where each table spends it is an authoring call.
		int thirtySecond = Timing.TicksPerEighth / 4;
		bool anyFine = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			foreach ( var f in p.CompFigures ) anyFine |= MinSpan( f ) <= thirtySecond;
			foreach ( var f in p.BassPatterns ) anyFine |= MinSpan( f ) <= thirtySecond;
			// The flourishes are where most of the 32nds live, and they are deliberately NOT in
			// the figure tables (see GenreProfile.CompOrnament) — so a check that only reads the
			// tables would have gone quietly vacuous the moment they moved out. It did.
			if ( p.CompOrnament != null ) anyFine |= MinSpan( p.CompOrnament ) <= thirtySecond;
			if ( p.KeysOrnament != null ) anyFine |= MinSpan( p.KeysOrnament ) <= thirtySecond;
			if ( p.KeysFigures == null ) continue;
			foreach ( var f in p.KeysFigures ) anyFine |= MinSpan( f ) <= thirtySecond;
		}
		Check( "some authored figure reaches the thirty-second", anyFine );

		// ── the flourish is a flourish ──
		// Held out of the table it could quietly become just another figure, so: every genre that
		// has one is DENSER than the bed it drops into (or it is not an ornament), and no genre
		// smuggles its ornament back into the table it substitutes for (or the per-occurrence roll
		// is competing with a per-section draw of the same thing).
		bool ornDenser = true, ornOutOfTable = true, anyOrn = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			foreach ( var (orn, table) in new[] { (p.CompOrnament, p.CompFigures), (p.KeysOrnament, p.KeysFigures) } )
			{
				if ( orn == null || table == null ) continue;
				anyOrn = true;
				float mean = 0f;
				foreach ( var f in table ) mean += f.Count / (float)f.LengthTicks;
				mean /= table.Length;
				ornDenser &= orn.Count / (float)orn.LengthTicks > mean;
				foreach ( var f in table ) ornOutOfTable &= !ReferenceEquals( f, orn );
			}
		}
		Check( "a genre's flourish is denser than the bed it drops into", ornDenser );
		Check( "…and is not also an entry in the table it substitutes for", ornOutOfTable );
		Check( "…and some genre actually has one", anyOrn );

		// A figure that fine still has to land on the grid the voices are measured against — the
		// per-voice grid checks below cover the rendered result, this covers the table.
		bool fineOnGrid = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var f in GenreProfile.For( g ).CompFigures )
				foreach ( var h in f.Slice( 0, f.LengthTicks ) )
					fineOnGrid &= h.Tick % thirtySecond == 0 || h.Tick % (Timing.TicksPerBeat / 6) == 0;
		Check( "every comp onset sits on a 32nd or a triplet division", fineOnGrid );

		// Punk is the fast genre by definition, and punk/pop take a chord per bar so their
		// four-chord loop is the four-bar hypermeasure.
		Check( "punk always runs hot", GenreProfile.For( 4 ).AlwaysFast );
		Check( "punk changes chord every bar", GenreProfile.For( 4 ).ChordBars == 1 );
		Check( "pop changes chord every bar", GenreProfile.For( 5 ).ChordBars == 1 );
		Check( "ska holds a chord for two bars", GenreProfile.For( 0 ).ChordBars == 2 );
		Check( "ska is the horn-lead genre", GenreProfile.For( 0 ).HornLead );
		bool oneHornLead = true;
		for ( int g = 1; g < VibeCodec.GenreCount; g++ ) oneHornLead &= !GenreProfile.For( g ).HornLead;
		Check( "no other genre takes the horn lead", oneHornLead );
	}

	/// <summary>Knobs that exist must do something. Each of these was a slider that rode in the
	/// seed and changed nothing, so the check is the crude one that would have caught it: move
	/// the knob end to end and the song must come out different.</summary>
	static void WiredKnobTests()
	{
		// Every setting below is rendered once, all of them at the same time, and the comparisons
		// are made afterwards. Rendered one at a time this was the most expensive section in the
		// harness — and it rendered three of these settings twice over, because the pair checks
		// and the per-instrument sweep each asked for their own copy.
		int hornOff = 0, hornOn = 1, bubbleOff = 2, bubbleOn = 3;
		int trumpet = 4, trombone = 7, byWeight = 8, slow = 9, quick = 10;
		var settings = new Action<MusicGen.Config>[]
		{
			c => c.HornSectionChance = 0f,
			c => c.HornSectionChance = 1f,
			c => c.OrganBubbleChance = 0f,
			c => c.OrganBubbleChance = 1f,
			c => c.ForceInstrument = 0,
			c => c.ForceInstrument = 1,
			c => c.ForceInstrument = 2,
			c => c.ForceInstrument = 3,
			c =>
			{
				c.ForceInstrument = -1;
				c.TrumpetWeight = c.SaxWeight = c.OrganWeight = 0f;
				c.TromboneWeight = 1f;
			},
			c => c.TempoScale = 0.70f,
			c => c.TempoScale = 1.45f,
		};
		var song = new short[settings.Length][];
		System.Threading.Tasks.Parallel.For( 0, settings.Length,
			i => song[i] = Knob( i >= slow ? 1 : 0, settings[i] ) );

		Check( "the HORN SECTION knob changes the song", !SameSamples( song[hornOff], song[hornOn] ) );
		Check( "the ORGAN BUBBLE knob changes the song", !SameSamples( song[bubbleOff], song[bubbleOn] ) );

		// ForceInstrument and the four *Weight knobs were inert while the lead was hardcoded to
		// the trumpet, which also meant the finished Sax/Organ/Trombone voices never played.
		Check( "forcing a lead instrument changes the song",
			!SameSamples( song[trumpet], song[trombone] ) );
		bool everyLeadPlays = true;
		for ( int inst = trumpet; inst <= trombone; inst++ )
			everyLeadPlays &= song[inst] != null && song[inst].Length > 0;
		Check( "every lead instrument renders", everyLeadPlays );

		// The lead weights must be reachable too — zeroing every one but the trombone has to
		// give the same song as forcing the trombone outright.
		Check( "the lead weights pick the same instrument as forcing it",
			SameSamples( song[trombone], song[byWeight] ) );

		// The TEMPO knob is the replacement for the retired absolute band: slower must mean a
		// longer song, and it must be the same song.
		Check( "the TEMPO knob changes how long a song runs", song[slow].Length > song[quick].Length );
	}

	/// <summary>One short song, rendered at a low sample rate (these checks look at whether the
	/// output differs, not at what it sounds like).</summary>
	static short[] Knob( int genre, Action<MusicGen.Config> tune )
	{
		var cfg = new MusicGen.Config { Genre = genre, SampleRate = 16000 };
		tune( cfg );
		return MusicGen.GenerateSamples( "knob:0", cfg, out _ );
	}

	static void StructureTests()
	{
		var forms = new List<string>();
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var parts = MusicGen.BuildStructure( g );
			Check( $"genre {g} structure is non-empty", parts.Count > 0 );

			int bars = 0;
			bool positive = true, energyOk = true, feelOk = true;
			foreach ( var p in parts )
			{
				bars += p.Bars; positive &= p.Bars > 0;
				energyOk &= p.Energy > 0f && p.Energy <= 1f;
				feelOk &= p.Feel > 0f && p.Feel <= 4f;
				// Bar lengths are the anomalous-measure hook; a zero-beat bar would be a hang.
				if ( p.BarBeats != null ) foreach ( var b in p.BarBeats ) positive &= b > 0;
			}
			Check( $"genre {g} parts have a positive bar count", positive );
			Check( $"genre {g} is long enough to be a song", bars >= 32 );
			Check( $"genre {g} energies are in range", energyOk );
			Check( $"genre {g} feels are in range", feelOk );

			// The verse index is what selects a verse's variation; it must be dense from 0 so a
			// lookup keyed on it can't miss.
			int expected = 0;
			bool verseOrder = true;
			foreach ( var p in parts )
				if ( p.Type == Section.Verse ) verseOrder &= p.VerseIndex == expected++;
			Check( $"genre {g} verse indices run 0,1,2… in order", verseOrder );

			Check( $"genre {g} opens on an intro", parts[0].Type == Section.Intro );
			Check( $"genre {g} closes on an ending", parts[^1].Type == Section.Ending );
			// The old two-bar ending broke the four-bar norm exactly where a clean landing was
			// wanted; the irregularity belongs in the transitions instead.
			Check( $"genre {g} ends on a four-bar section", parts[^1].Bars == 4 );

			// A chorus must be the loudest thing in the song, or the energy contour is inverted.
			float chorus = 0f, other = 1f;
			foreach ( var p in parts )
			{
				if ( p.Type == Section.Chorus ) chorus = Math.Max( chorus, p.Energy );
				else if ( p.Type == Section.Verse ) other = Math.Min( other, p.Energy );
			}
			Check( $"genre {g} choruses sit above its verses", chorus > other );

			var sig = new StringBuilder();
			foreach ( var p in parts ) sig.Append( $"{p.Type}{p.Bars}." );
			forms.Add( sig.ToString() );
		}

		// The row's whole point: a metal song and a pop song had byte-identical FORM. Every genre
		// must now name its own.
		Check( "every genre has its own form", new HashSet<string>( forms ).Count == forms.Count,
			$"{new HashSet<string>( forms ).Count} distinct of {forms.Count}" );

		// The new section types have to be reachable, or they are decoration in an enum.
		var types = new HashSet<Section>();
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var p in MusicGen.BuildStructure( g ) ) types.Add( p.Type );
		Check( "the new section types are used by some genre",
			types.Contains( Section.PreChorus ) && types.Contains( Section.Bridge )
			&& types.Contains( Section.Solo ) && types.Contains( Section.Breakdown ) );

		bool named = true;
		foreach ( Section s in Enum.GetValues( typeof( Section ) ) )
			named &= !string.IsNullOrEmpty( MusicGen.SectionKey( s ) );
		Check( "every section type has a key string", named );

		// Distinct keys, or two section types would share one RNG stream and play identically.
		var keys = new HashSet<string>();
		foreach ( Section s in Enum.GetValues( typeof( Section ) ) ) keys.Add( MusicGen.SectionKey( s ) );
		Check( "every section type has its OWN key string",
			keys.Count == Enum.GetValues( typeof( Section ) ).Length );

		// Half-time is a PATTERN rate, not a tempo: a section that halves the feel must not change
		// how long the song runs. (Metal's breakdown, pop's drop, ska-punk's bridge.)
		bool halfTime = false, feelUnderTune = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var p in MusicGen.BuildStructure( g ) )
			{
				halfTime |= p.Feel < 1f;
				// Feel is the RHYTHM SECTION's rate and the tune is exempt, so half time is a
				// contrast between the band and the melody. A form where every feel change lands
				// on a section with no tune expresses none of that — the exemption would be
				// unreachable and the rule untested by anything that renders.
				feelUnderTune |= p.Feel != 1f && MusicGen.SectionSingsTune( p.Type );
			}
		Check( "some genre drops into half time", halfTime );
		Check( "some genre changes feel UNDER a tune", feelUnderTune );

		// The final-chorus lift, and the hemiola the transitional sections regroup into. A constant
		// per-section displacement used to live here too; it is gone deliberately (see SongForm) and
		// the hemiola is the metric device that survives, because it re-converges.
		bool lift = false, hemiola = false;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var p in MusicGen.BuildStructure( g ) )
			{
				lift |= p.KeyShift != 0;
				hemiola |= p.Hemiola;
			}
		Check( "some genre lifts a section into a new key", lift );
		Check( "some section regroups into a hemiola", hemiola );

		// The anomalous (short) bar. No FORM uses one today — dropping a beat under a melody reads
		// as the song jumping to a downbeat early — so what is asserted is the MECHANISM, which the
		// non-4/4 work builds on: a section's length has to follow its per-bar beat counts, not
		// bars x the song's meter.
		var evenBars = new Part( Section.Verse, 4 );
		var shortBar = new Part( Section.Verse, 4, barBeats: new[] { 4, 4, 4, 2 } );
		Check( "a plain section is bars x the meter",
			MusicGen.SectionTicks( evenBars, 4 ) == 4 * 4 * Timing.TicksPerBeat );
		Check( "a short bar shortens its section by exactly the beats it drops",
			MusicGen.SectionTicks( shortBar, 4 ) == MusicGen.SectionTicks( evenBars, 4 ) - 2 * Timing.TicksPerBeat );
	}

	/// <summary>The tune — the thing a listener hums back. What matters is that it is a real
	/// melodic line with call-and-answer structure, and that it REPEATS: a chorus that sings a
	/// different line every time is not a chorus.</summary>
	static void MelodyTests()
	{
		int bar = 4 * Timing.TicksPerBeat;
		var tune = Melody.Draw( new Rng( "tune:a" ), 4, bar, 0.4f, 0.2f );

		Check( "a tune is as long as it was asked to be", tune.LengthTicks == 4 * bar );
		Check( "a tune has notes in every bar", tune.Count >= 4 );
		Check( "a tune is reproducible from its seed",
			SameTune( tune, Melody.Draw( new Rng( "tune:a" ), 4, bar, 0.4f, 0.2f ) ) );
		Check( "a different seed writes a different tune",
			!SameTune( tune, Melody.Draw( new Rng( "tune:b" ), 4, bar, 0.4f, 0.2f ) ) );

		// Call and answer: the second phrase repeats the FIRST phrase's rhythm exactly (that
		// symmetry is what makes a line sound composed), and resolves home on the last note.
		var all = tune.Slice( 0, 4 * bar );
		var call = all.FindAll( h => h.Tick < 2 * bar );
		var answer = all.FindAll( h => h.Tick >= 2 * bar );
		Check( "the answer repeats the call's rhythm", call.Count == answer.Count );
		bool sameRhythm = call.Count == answer.Count;
		for ( int i = 0; sameRhythm && i < call.Count; i++ )
			sameRhythm &= answer[i].Tick - 2 * bar == call[i].Tick;
		Check( "the answer lands on the call's own beats", sameRhythm );
		Check( "the answer resolves home", answer.Count > 0 && answer[^1].Value == 0 );

		// Singable: degrees stay inside a range a voice could actually reach.
		bool inRange = true;
		foreach ( var h in all ) inRange &= h.Value >= -3 && h.Value <= 10;
		Check( "a tune stays in a singable range", inRange );

		// Density is a real control — a punk tune must be sparser than a ska horn line.
		int sparse = Melody.Draw( new Rng( "tune:c" ), 4, bar, 0.2f, 0.2f ).Count;
		int busy = Melody.Draw( new Rng( "tune:c" ), 4, bar, 0.9f, 0.2f ).Count;
		Check( "density controls how much a tune moves", busy > sparse, $"{busy} vs {sparse} notes" );
	}

	// ── where a player bends ──
	// The bend rate stopped being a floor and became a weighting, so what is assertable is the
	// SHAPE of that weighting: a bender leans on the long note and on the note a phrase lands on,
	// and passes through the run. None of these is a number chosen to make a check pass — each is
	// the sentence the row was written to express, turned round.
	static void BendBiasTests()
	{
		int longNote = Timing.TicksPerBeat * 2, shortNote = Timing.TicksPerEighth;
		float runNote = MusicGen.BendBias( shortNote, 0.2f );
		float landing = MusicGen.BendBias( longNote, 0.98f );
		Check( "a long note landing a phrase outweighs a short one mid-run, several times over",
			landing > runNote * 4f, $"{landing:0.00} vs {runNote:0.00}" );
		Check( "…and the ordinary note is weighted DOWN, or the rate is still a floor",
			runNote < 0.6f, $"{runNote:0.00}" );
		Check( "length alone moves it", MusicGen.BendBias( longNote, 0.5f ) > MusicGen.BendBias( shortNote, 0.5f ), null );
		Check( "phrase position alone moves it",
			MusicGen.BendBias( longNote, 0.95f ) > MusicGen.BendBias( longNote, 0.55f ), null );
		// Call and answer lands TWICE. Reading the position over the whole phrase would make the
		// end of the call — the most bent note in a country lick — the flattest point of the curve.
		Check( "the end of the CALL is a landing too, not the middle of one long phrase",
			MusicGen.BendBias( longNote, 0.48f ) > MusicGen.BendBias( longNote, 0.55f ), null );
	}

	static bool SameTune( Pattern a, Pattern b )
	{
		var x = a.Slice( 0, a.LengthTicks );
		var y = b.Slice( 0, b.LengthTicks );
		if ( x.Count != y.Count ) return false;
		for ( int i = 0; i < x.Count; i++ )
			if ( x[i].Tick != y[i].Tick || x[i].Value != y[i].Value ) return false;
		return true;
	}

	/// <summary>A pattern owns its LENGTH and free-runs against the bar line — the fix for "bar 2
	/// is bar 1". These check the mechanism itself, since every voice now depends on it.</summary>
	static void PatternTests()
	{
		int bar = 4 * Timing.TicksPerBeat;
		var oneBar = Pattern.Eighths( 0, Harmony.Rest, 0, Harmony.Rest,
			0, Harmony.Rest, 0, Harmony.Rest );
		Check( "an eighth-authored bar is a bar long", oneBar.LengthTicks == bar );
		Check( "rest cells carry no onset", oneBar.Count == 4 );

		// A one-bar figure repeats; a two-bar figure does NOT — that is the whole point.
		var twoBar = Pattern.Eighths( 0, Harmony.Rest, Harmony.Rest, Harmony.Rest,
			Harmony.Rest, Harmony.Rest, Harmony.Rest, Harmony.Rest,
			Harmony.Rest, Harmony.Rest, Harmony.Rest, Harmony.Rest,
			1, Harmony.Rest, Harmony.Rest, Harmony.Rest );
		Check( "a one-bar figure plays the same in bar 2",
			oneBar.Slice( 0, bar ).Count == oneBar.Slice( bar, bar * 2 ).Count );
		Check( "a two-bar figure plays something different in bar 2",
			twoBar.Slice( 0, bar )[0].Value != twoBar.Slice( bar, bar * 2 )[0].Value );

		// Spans run to the NEXT onset, wrapping the loop, which is what makes a note legato.
		Check( "a cell's span reaches the next onset",
			oneBar.Slice( 0, bar )[0].SpanTicks == Timing.TicksPerBeat );

		// A figure whose length does not divide the bar drifts against it and comes back — the
		// grouping dissonance the hemiola uses.
		var hemi = Pattern.Eighths( 0, Harmony.Rest, 0 );
		Check( "a 3-eighth figure does not divide the bar", bar % hemi.LengthTicks != 0 );
		var b1 = hemi.Slice( 0, bar );
		var b2 = hemi.Slice( bar, bar * 2 );
		bool drifts = b1.Count != b2.Count || b1[0].Tick % bar != b2[0].Tick % bar;
		Check( "a hemiola lands differently in the next bar", drifts );

		// Half time stretches the figure without touching the grid.
		var half = oneBar.Slice( 0, bar * 2, 0, 0.5f );
		Check( "half time stretches a figure over two bars", half.Count == 4 );

		// The anchor is the section, so a multi-bar figure restarts with the section rather than
		// wherever the song happens to be.
		Check( "a figure loops from its anchor",
			twoBar.Slice( bar * 8, bar * 9, bar * 8 )[0].Value == twoBar.Slice( 0, bar )[0].Value );

		// Sliced windows must partition: no onset played twice, none dropped.
		int whole = oneBar.Slice( 0, bar * 4 ).Count;
		int pieces = 0;
		for ( int i = 0; i < 4; i++ ) pieces += oneBar.Slice( i * bar, (i + 1) * bar ).Count;
		Check( "adjacent slices neither drop nor duplicate onsets", whole == pieces );
	}

	static void VibeTests()
	{
		Check( "there are genres", VibeCodec.GenreCount > 0 );
		Check( "genre names match genre count", VibeCodec.Genres.Count == VibeCodec.GenreCount );

		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var cfg = new MusicGen.Config { Genre = g };
			var enc = VibeCodec.Encode( cfg );

			Check( $"genre {g} encodes to its own first char",
				enc.Length > 0 && VibeCodec.Alphabet.IndexOf( enc[0] ) == g );

			// Round-trip: decode onto a fresh Config, re-encode, expect the same string. This
			// is the property that actually matters — a shared URL must reproduce the knobs.
			var back = new MusicGen.Config();
			VibeCodec.Apply( enc, back );
			Check( $"genre {g} vibe round-trips", VibeCodec.Encode( back ) == enc );
			Check( $"genre {g} survives the round-trip", back.Genre == g );

			// Every knob at full and at zero must also round-trip (the quantiser's endpoints
			// are where an off-by-one in Levels shows up).
			foreach ( var extreme in new[] { 0f, 1f } )
			{
				var c2 = new MusicGen.Config { Genre = g };
				foreach ( var f in VibeCodec.Fields( g ) ) f.SetNorm( c2, extreme );
				var e2 = VibeCodec.Encode( c2 );
				var r2 = new MusicGen.Config();
				VibeCodec.Apply( e2, r2 );
				Check( $"genre {g} round-trips with every knob at {extreme}",
					VibeCodec.Encode( r2 ) == e2 );
			}
		}

		// ── reroll ──
		// One definition of "reroll" shared by every player. A seeded roll is the shuffle line,
		// so the same tag and index must give the same vibe anywhere.
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var a = new MusicGen.Config { Genre = g };
			var b = new MusicGen.Config { Genre = g };
			VibeCodec.RollFrom( a, VibeCodec.VibeSeed( "gamah", 4 ) );
			VibeCodec.RollFrom( b, VibeCodec.VibeSeed( "gamah", 4 ) );
			Check( $"a seeded roll is reproducible (from genre {g})", VibeCodec.Encode( a ) == VibeCodec.Encode( b ) );

			// …and must not depend on where it started, or the shuffle line would differ between
			// two listeners whose live knobs differ.
			Check( $"a seeded roll ignores the starting genre {g}",
				VibeCodec.Encode( a ) == VibeCodec.Encode( Rolled( "gamah", 4, 0 ) ) );
		}

		Check( "stepping n gives a different vibe",
			VibeCodec.Encode( Rolled( "gamah", 4, 0 ) ) != VibeCodec.Encode( Rolled( "gamah", 5, 0 ) ) );
		Check( "a different tag gives a different line",
			VibeCodec.Encode( Rolled( "gamah", 4, 0 ) ) != VibeCodec.Encode( Rolled( "skafinity", 4, 0 ) ) );
		Check( "tag case does not fork the line",
			VibeCodec.VibeSeed( "Gamah", 3 ) == VibeCodec.VibeSeed( "gamah", 3 ) );
		Check( "an empty tag falls back rather than producing a bare line",
			VibeCodec.VibeSeed( "", 0 ) == VibeCodec.VibeSeed( "rotaliate", 0 ) );

		// A roll reaches every genre — a station stuck on one genre would not be a shuffle.
		var seen = new HashSet<int>();
		for ( int i = 0; i < 400; i++ ) seen.Add( Rolled( "gamah", i, 0 ).Genre );
		Check( "the shuffle line reaches every genre", seen.Count == VibeCodec.GenreCount,
			$"reached {seen.Count} of {VibeCodec.GenreCount}" );

		// Volumes are a local mix preference: a reroll must leave them alone by default, or a
		// listener's levels would be trampled on every song.
		var volCfg = new MusicGen.Config { Genre = 0 };
		foreach ( var f in VibeCodec.Fields( 0 ) ) if ( VibeCodec.IsVolume( f ) ) f.SetNorm( volCfg, 0.25f );
		VibeCodec.RollFrom( volCfg, VibeCodec.VibeSeed( "gamah", 1 ), includeGenre: false );
		bool volsKept = true;
		foreach ( var f in VibeCodec.Fields( 0 ) )
			if ( VibeCodec.IsVolume( f ) ) volsKept &= Math.Abs( f.GetNorm( volCfg ) - 0.25f ) < 0.001f;
		Check( "a reroll leaves per-instrument volumes alone", volsKept );

		// The tempo knob scales whatever the genre drew, so a reroll must never land it at or
		// near zero (a song at 0 bpm is an infinite bar) or somewhere the genre's band would be
		// unrecognisable at the other end.
		bool tempoSane = true;
		for ( int i = 0; i < 200; i++ )
		{
			var c = Rolled( "tempo", i, 0 );
			tempoSane &= c.TempoScale >= 0.5f && c.TempoScale <= 2f;
		}
		Check( "a rolled tempo scale stays musical", tempoSane );

		// The caller owns the randomness; a degenerate generator must not index off the genre table.
		var edge = new MusicGen.Config();
		bool edgeThrew = false;
		try { VibeCodec.Roll( edge, () => 1f ); } catch { edgeThrew = true; }
		Check( "a generator returning 1.0 stays in range", !edgeThrew && edge.Genre < VibeCodec.GenreCount );

		// Malformed input must degrade, never throw — these strings arrive from a URL bar.
		foreach ( var junk in new[] { "", "   ", "!!!!", "zzzzzzzzzzzzzzzzzzzz", "0!!0!!0" } )
		{
			bool threw = false;
			try { VibeCodec.Apply( junk, new MusicGen.Config() ); } catch { threw = true; }
			Check( $"Apply(\"{junk}\") does not throw", !threw );
		}

		// A short vibe must apply its prefix and leave the rest at defaults, which is what
		// makes the wire format append-only.
		var trunc = new MusicGen.Config();
		VibeCodec.Apply( "1", trunc );
		Check( "a one-char vibe still selects the genre", trunc.Genre == 1 );

		Check( "LooksLikeVibe rejects a player tag", !VibeCodec.LooksLikeVibe( "rotaliate" ) );
		Check( "LooksLikeVibe accepts a real vibe",
			VibeCodec.LooksLikeVibe( VibeCodec.Encode( new MusicGen.Config { Genre = 0 } ) ) );

		// AdvancedFields is the "config value, not a vibe slider" marker — the two registries
		// must stay disjoint or a house-mix knob would leak into the shareable seed.
		bool disjoint = true;
		foreach ( var adv in VibeCodec.AdvancedFields )
			for ( int g = 0; g < VibeCodec.GenreCount; g++ )
				foreach ( var f in VibeCodec.Fields( g ) )
					disjoint &= !ReferenceEquals( adv, f ) && adv.Name != f.Name;
		Check( "AdvancedFields never appear as vibe knobs", disjoint );

		// ApplyAdvanced is how skafinity.config.json reaches the engine; an unknown key in
		// that file must be ignored rather than fatal.
		var advCfg = new MusicGen.Config();
		bool advThrew = false;
		try
		{
			VibeCodec.ApplyAdvanced(
				new Dictionary<string, float> { ["NoSuchField"] = 1f }, advCfg );
		}
		catch { advThrew = true; }
		Check( "ApplyAdvanced ignores an unknown key", !advThrew );
	}

	/// <summary>The arrangement has to survive contact with the renderer. Velocity, section energy
	/// and the tempo curve all scale levels and lengths, so these are the crude checks that catch
	/// a song that came out silent, clipped, or the wrong length entirely.</summary>
	static void ArrangementTests()
	{
		// This is the expensive section: every measurement below is a rendered song, and there are
		// four of them per genre. The genres are independent and every Rng is per-instance, so the
		// RENDERING fans out across the machine and the ASSERTIONS run after it, in genre order —
		// Check() writes shared counters and one interleaved line of output per call, so it must
		// not run on a worker.
		// The work is listed one render at a time rather than one genre at a time: a genre's four
		// measurements are 10 renders of very different lengths, so fanning out per genre leaves
		// the machine waiting on whichever genre drew the slowest tempo.
		int genres = VibeCodec.GenreCount;
		var arr = new Arrangement[genres];
		var drift = new List<(string Name, double Mean, double BadPct)>[genres];
		var solo = new double[genres][];
		double mutedRms = 0;

		var work = new List<Action>();
		for ( int i = 0; i < genres; i++ )
		{
			int g = i;
			solo[g] = new double[BalanceSolos.Length];
			work.Add( () => arr[g] = MeasureArrangement( g ) );
			work.Add( () => drift[g] = MeasureGridDrift( g ) );
			for ( int j = 0; j < BalanceSolos.Length; j++ )
			{
				int v = j;
				work.Add( () => solo[g][v] = SoloRms( g, BalanceSolos[v] ) );
			}
		}
		work.Add( () => mutedRms = SoloRms( 0, _ => { } ) );
		System.Threading.Tasks.Parallel.ForEach( work, w => w() );

		var mix = new Balance[genres];
		for ( int g = 0; g < genres; g++ ) mix[g] = Balance.From( solo[g] );

		for ( int g = 0; g < genres; g++ )
		{
			var a = arr[g];
			Check( $"genre {g} renders a song of plausible length", a.Seconds > 30 && a.Seconds < 300,
				$"{a.Seconds:0.0}s" );
			Check( $"genre {g} is not silent", a.Rms > 0.01, $"rms {a.Rms:0.0000}" );
			Check( $"genre {g} plays for most of its length", a.Loud > a.Samples / 4 );
			Check( $"genre {g} is not clipped to a square wave", a.Clipped < a.Samples / 100 );
		}

		// ── are the parts playing together? ──
		// Every onset must land on the song's own TICK grid (swing and tempo curve included) — the
		// grid that makes 8ths, 16ths and both triplet rates exact, so an ornament is not drift.
		// This catches the class of bug that reads as "the band isn't sharing a downbeat": a
		// gesture written in TICKS rather than milliseconds scales with tempo, and country's strum
		// spread — two ticks per string — smeared a chord across 75 ms at country's tempo while
		// looking fine at metal's. Double-tracking offsets the second take ~9 ms by design, so the
		// bar is set above that and well under a 32nd note.
		// Diagnose failures with `dotnet run --project test/engine -- --grid [genre]`.
		for ( int g = 0; g < genres; g++ )
			foreach ( var (name, mean, badPct) in drift[g] )
				Check( $"genre {g} {name} plays on the grid", mean < 15 && badPct < 5,
					$"mean {mean:0.0} ms, {badPct:0.0}% over 25 ms" );

		// ── the balance of the mix ──
		// The comp is the BED. It measured 5 dB over the kit after the figures were rewritten —
		// the balances had been peak-tuned for parts the engine no longer plays — and a loud
		// backing is what makes a repeated figure sound like the whole song. Measured pre-master
		// (see MusicGen.RawLevels), because the master bus normalizes every solo to one peak.
		// Retune with `dotnet run --project test/engine -- --levels`.
		for ( int g = 0; g < genres; g++ )
		{
			double drums = mix[g].Drums, comp = mix[g].Comp, lead = mix[g].Lead, bass = mix[g].Bass;

			// The detail names all three chordal voices, not just the loudest: "the comp is hot"
			// is not actionable until you know whether it is the guitar, the keys or the skank.
			//
			// THIS IS ONE SEED, and a genre's comp/kit ratio varies several dB across seeds — rock
			// averages about −4.5 dB (`--levels`, which measures the genre rather than a song) and
			// this seed sits near 0. So the bar is "not louder than the kit", which still catches
			// the regression this check exists for (the comp measured +5 dB OVER the kit once the
			// figures were rewritten and the peak-tuned balances went stale), without failing every
			// time a figure table grows: adding an entry re-rolls which figure each SECTION draws,
			// so an unrelated table edit moves this number by dB. If it is the average you want to
			// hold, `--levels` is the tool — this is a tripwire, not a mix measurement.
			Check( $"genre {g} comp sits under the kit", comp < drums,
				$"comp {Db( comp, drums ):0.0} dB — gtr {Db( solo[g][1], drums ):0.0}, "
				+ $"keys {Db( solo[g][2], drums ):0.0}, skank {Db( solo[g][3], drums ):0.0}" );
			// The lead is the MELODY and its target is +2 dB over the kit (see LeadLevel), not
			// level with it. This ceiling is that target plus the ~4 dB a single seed varies
			// around it — it catches a lead that has become the whole record, not one that is
			// simply on top, which is where it belongs.
			Check( $"genre {g} lead does not dominate", lead < drums * 2.0,
				$"lead {Db( lead, drums ):0.0} dB" );
			Check( $"genre {g} bass is present", bass > drums * 0.4, $"bass {Db( bass, drums ):0.0} dB" );
			// Nothing may be inaudible either — a voice a genre plays must actually arrive.
			Check( $"genre {g} comp is audible", comp > drums * 0.15, $"comp {Db( comp, drums ):0.0} dB" );
		}

		// Muting everything must give silence. The ending chord used to carry a hardcoded level,
		// so a listener with every instrument at zero still heard a chord at the end of the song.
		Check( "muting every voice renders silence", mutedRms < 0.0005, $"rms {mutedRms:0.00000}" );

		// ── dynamics ──
		// Loudness used to be a per-patch constant with two ad-hoc exceptions, so a song was as
		// flat at the end as at the start. Velocity + the section energy contour must reach the
		// OUTPUT: measure per-second RMS and expect a real spread between the song's quietest
		// playing second and its loudest.
		for ( int g = 0; g < genres; g++ )
			Check( $"genre {g} has real dynamics across the song",
				arr[g].Loudest > arr[g].Quietest * 1.35,
				$"quiet {arr[g].Quietest:0.000} loud {arr[g].Loudest:0.000}" );

		// The ending ritard: the last bars run slower, so the same structure has to render LONGER
		// than a naive constant-tempo estimate — and the tail has to outlast the final chord.
		var end = MusicGen.GenerateSamples( "ritard:0", new MusicGen.Config { SampleRate = 22050 }, out int esr );
		int silentTail = 0;
		for ( int i = end.Length - 2; i >= 0 && Math.Abs( end[i] / 32768.0 ) < 0.001; i -= 2 ) silentTail++;
		Check( "the song decays into its reserved tail rather than being cut off",
			silentTail > 0 && silentTail < esr * 4, $"{silentTail} silent frames" );
	}

	// ── the measurements behind the arrangement checks ─────────────────────────────────────
	// Each one renders and reduces to numbers, so it can run on a worker thread. None of them
	// calls Check(): the assertions are made by the caller, in genre order.

	readonly record struct Arrangement( double Seconds, double Rms, int Loud, int Clipped, int Samples,
		double Quietest, double Loudest );
	/// <summary>The pre-master levels the balance checks compare. The comp bed and the lead are
	/// each whichever of their candidate voices this genre actually plays loudest.</summary>
	readonly record struct Balance( double Drums, double Comp, double Lead, double Bass )
	{
		public static Balance From( double[] s ) => new( s[0],
			Math.Max( Math.Max( s[1], s[2] ), s[3] ), Math.Max( s[4], s[5] ), s[6] );
	}

	/// <summary>The voices <see cref="Balance"/> is built from, in the order it reads them.</summary>
	static readonly Action<MusicGen.Config>[] BalanceSolos =
	{
		c => c.DrumVol = 1f,
		c => c.RhythmGtrVol = 1f,
		c => c.KeysVol = 1f,
		c => c.SkankVol = 1f,
		c => c.LeadGtrVol = 1f,
		c => c.MelodyVol = 1f,
		c => c.BassVol = 1f,
	};

	/// <summary>One whole song for a genre, reduced to length, level, clipping and its dynamic
	/// range. Length/level and dynamics used to render a song each, off two different tags; they
	/// are questions about the same thing, so they ask them of the same song.</summary>
	static Arrangement MeasureArrangement( int g )
	{
		var cfg = new MusicGen.Config { Genre = g, SampleRate = 22050 };
		var pcm = MusicGen.GenerateSamples( $"arrange:{g}", cfg, out int sr );

		double sum = 0; int loud = 0, clipped = 0;
		foreach ( var s in pcm )
		{
			double v = s / 32768.0;
			sum += v * v;
			if ( Math.Abs( v ) > 0.05 ) loud++;
			if ( Math.Abs( v ) > 0.999 ) clipped++;
		}

		int win = sr * MusicGen.Channels;                 // one second of interleaved frames
		double quietest = double.MaxValue, loudest = 0;
		// Skip the last three seconds: the ring-out tail is silence by design.
		for ( int i = 0; i + win < pcm.Length - 3 * win; i += win )
		{
			double w = 0;
			for ( int k = 0; k < win; k++ ) { double v = pcm[i + k] / 32768.0; w += v * v; }
			double rms = Math.Sqrt( w / win );
			if ( rms < 0.005 ) continue;                  // an empty bar is not a dynamic
			quietest = Math.Min( quietest, rms ); loudest = Math.Max( loudest, rms );
		}

		return new Arrangement( pcm.Length / (double)(sr * MusicGen.Channels),
			Math.Sqrt( sum / Math.Max( 1, pcm.Length ) ), loud, clipped, pcm.Length,
			quietest, loudest );
	}

	/// <summary>Each voice's onsets measured against the song's own tick grid, in ms. A voice this
	/// genre barely plays is left out of the list rather than reported. Composition only — no audio
	/// is synthesised, because onsets are decided by the composer.</summary>
	static List<(string Name, double Mean, double BadPct)> MeasureGridDrift( int g )
	{
		var rows = new List<(string, double, double)>();
		foreach ( var (name, solo) in Voices )
		{
			if ( name == "DRUMS" ) continue;             // written into the buffer, not events
			var cfg = new MusicGen.Config { Genre = g, SampleRate = 22050 };
			cfg.DrumVol = cfg.BassVol = cfg.SkankVol = cfg.OrganVol = cfg.MelodyVol =
				cfg.HornVol = cfg.KeysVol = cfg.RhythmGtrVol = cfg.LeadGtrVol = 0f;
			solo( cfg );
			var mg = MusicGen.BeginPlan( $"grid:{g}", cfg );
			var (starts, _) = mg.Onsets();
			if ( starts.Length < 20 ) continue;
			var grid = mg.GridSamples();

			double sum = 0; int n = 0, bad = 0;
			foreach ( var st in starts )
			{
				int i = NearestBar( grid, st );
				double d = Math.Abs( st - grid[i] ) / 22.05;
				if ( i + 1 < grid.Length ) d = Math.Min( d, Math.Abs( st - grid[i + 1] ) / 22.05 );
				sum += d; n++;
				if ( d > 25 ) bad++;
			}
			rows.Add( (name, sum / n, bad * 100.0 / n) );
		}
		return rows;
	}

	// ── the mix balancing tool (`--levels`) ────────────────────────────────────────────────
	// Every voice muted but one, measured BEFORE the master bus (the master peak-normalizes, so
	// a soloed voice measured at the output tells you nothing). Prints dB relative to the drums,
	// which is the reference the kit balances were set against. Use it when a part changes shape
	// — the *Balance values are peak-tuned numbers and they go stale when the part they were
	// tuned for is replaced.
	static readonly (string Name, Action<MusicGen.Config> Solo)[] Voices =
	{
		("DRUMS",    c => c.DrumVol = 1f),
		("BASS",     c => c.BassVol = 1f),
		("SKANK",    c => c.SkankVol = 1f),
		("ORGAN",    c => c.OrganVol = 1f),
		("HORN LEAD",c => c.MelodyVol = 1f),
		("HORNS",    c => c.HornVol = 1f),
		("KEYS",     c => c.KeysVol = 1f),
		("RHY GTR",  c => c.RhythmGtrVol = 1f),
		("LEAD GTR", c => c.LeadGtrVol = 1f),
	};

	/// <summary>Pre-master RMS of one genre with every voice muted but the ones
	/// <paramref name="solo"/> turns back on.</summary>
	static double SoloRms( int genre, Action<MusicGen.Config> solo )
	{
		var cfg = new MusicGen.Config { Genre = genre, SampleRate = 16000 };
		cfg.DrumVol = cfg.BassVol = cfg.SkankVol = cfg.OrganVol = cfg.MelodyVol =
			cfg.HornVol = cfg.KeysVol = cfg.RhythmGtrVol = cfg.LeadGtrVol = 0f;
		solo( cfg );
		var g = MusicGen.BeginPlan( $"mix:{genre}", cfg );
		g.RenderPitchedRange( 0, g.TotalSamples );
		return g.RawLevels().Rms;
	}

	static double Db( double a, double b ) => 20 * Math.Log10( Math.Max( 1e-9, a ) / Math.Max( 1e-9, b ) );

	/// <summary>Explain one seed: what the vibe decodes to and what the composer did with it.
	/// The tool for "this seed sounds wrong" — it is far easier to read the decisions than to
	/// infer them from the audio. Usage: <c>-- --seed vibe:tag:n</c>.</summary>
	/// <summary>The song, rendered to a WAV at full rate — the mix as it actually ships, master
	/// bus and all, which is the one thing the audition deliberately is not.</summary>
	static void Render( string seed, string path )
	{
		var bits = seed.Split( ':' );
		string vibe = bits.Length >= 3 ? bits[0] : "";
		string tag = bits.Length >= 3 ? bits[1] : bits.Length == 2 ? bits[0] : seed;
		int n = int.TryParse( bits[^1], out var parsed ) ? parsed : 0;
		var cfg = new MusicGen.Config { SampleRate = 44100 };
		VibeCodec.Apply( vibe, cfg );
		var wav = MusicGen.Generate( $"{tag}:{n}", cfg );
		File.WriteAllBytes( path, wav );
		Console.WriteLine( $"{seed}  genre {cfg.Genre} ({VibeCodec.Genres[cfg.Genre]})  "
			+ $"-> {path}  ({wav.Length / (1024 * 1024)} MiB)" );
	}

	static void Explain( string seed )
	{
		var bits = seed.Split( ':' );
		string vibe = bits.Length >= 3 ? bits[0] : "";
		string tag = bits.Length >= 3 ? bits[1] : bits.Length == 2 ? bits[0] : seed;
		int n = int.TryParse( bits[^1], out var parsed ) ? parsed : 0;

		var cfg = new MusicGen.Config { SampleRate = 22050 };
		VibeCodec.Apply( vibe, cfg );
		Console.WriteLine( $"seed      {seed}" );
		Console.WriteLine( $"genre     {cfg.Genre} ({VibeCodec.Genres[cfg.Genre]})" );
		Console.WriteLine( $"prng seed \"{tag}:{n}\"" );
		Console.WriteLine( $"re-encode {VibeCodec.Encode( cfg )}" );

		Console.WriteLine();
		Console.WriteLine( "knobs off the wire:" );
		foreach ( var f in VibeCodec.Fields( cfg.Genre ) )
			Console.WriteLine( $"  {f.Voice ?? "GLOBAL",-10} {f.Name,-12} {f.Display( cfg )}" );

		var g = MusicGen.BeginPlan( $"{tag}:{n}", cfg );
		Console.WriteLine();
		Console.WriteLine( g.Explain() );
	}

	/// <summary>What every voice plays over a range of BARS, as a score.
	///
	/// <c>--seed</c> says what the composer decided for the whole song and <c>--grid</c> says
	/// whether the band agrees about the beat. Neither answers "what happens at bar 13" — and a
	/// listening note is always about a MOMENT. This solos each voice, reads its audible notes
	/// back off the plan, and prints them at bar.beat with their pitch, next to the section and
	/// the chord each bar is on. Usage: <c>-- --score vibe:tag:n [fromBar] [toBar]</c> (bars are
	/// 1-based, counted from the song's first downbeat).</summary>
	static void Score( string seed, int fromBar, int toBar )
	{
		var bits = seed.Split( ':' );
		string vibe = bits.Length >= 3 ? bits[0] : "";
		string tag = bits.Length >= 3 ? bits[1] : bits.Length == 2 ? bits[0] : seed;
		int n = int.TryParse( bits[^1], out var parsed ) ? parsed : 0;

		MusicGen.Config Base()
		{
			var c = new MusicGen.Config { SampleRate = 22050 };
			VibeCodec.Apply( vibe, c );
			return c;
		}

		// The ruler: bar lines in ticks, and which section each bar belongs to.
		var ruler = MusicGen.BeginPlan( $"{tag}:{n}", Base() );
		var barTicks = ruler.BarTickLines();
		var structure = MusicGen.BuildStructure( ruler.Genre );
		var barSection = new (string Name, int Chord)[barTicks.Length];
		int bi = 0;
		foreach ( var part in structure )
			for ( int bar = 0; bar < part.Bars && bi < barSection.Length; bar++, bi++ )
				barSection[bi] = ($"{part.Type}", ruler.ChordIndexAt( part, bar ));

		fromBar = Math.Clamp( fromBar, 1, barTicks.Length );
		toBar = Math.Clamp( toBar, fromBar, barTicks.Length );
		Console.WriteLine( $"score for {seed} — bars {fromBar}..{toBar} (1-based)" );
		Console.WriteLine();

		foreach ( var (name, solo) in Voices )
		{
			if ( name == "DRUMS" ) continue;      // synthesised into the buffer, not events
			var cfg = Base();
			cfg.DrumVol = cfg.BassVol = cfg.SkankVol = cfg.OrganVol = cfg.MelodyVol =
				cfg.HornVol = cfg.KeysVol = cfg.RhythmGtrVol = cfg.LeadGtrVol = 0f;
			solo( cfg );
			var g = MusicGen.BeginPlan( $"{tag}:{n}", cfg );
			var notes = g.AudibleNotes();
			if ( notes.Length == 0 ) continue;

			// Sample -> tick, off the same 1-tick grid --grid measures against. Double-tracking
			// emits two takes per note a few ms apart, so identical (tick, midi) pairs collapse.
			var grid = g.GridSamples();
			var rows = new SortedDictionary<int, SortedSet<int>>();
			foreach ( var (start, freq) in notes )
			{
				int tick = NearestBar( grid, start );
				if ( tick < barTicks[fromBar - 1] ) continue;
				if ( toBar < barTicks.Length && tick >= barTicks[toBar] ) continue;
				int midi = (int)Math.Round( 69 + 12 * Math.Log2( Math.Max( 1e-3, freq ) / 440.0 ) );
				if ( !rows.TryGetValue( tick, out var set ) ) rows[tick] = set = new SortedSet<int>();
				set.Add( midi );
			}
			if ( rows.Count == 0 ) continue;

			Console.WriteLine( $"── {name} ──" );
			int lastBar = -1;
			foreach ( var (tick, midis) in rows )
			{
				int bar = NearestBar( barTicks, tick );
				if ( bar != lastBar )
				{
					var (sect, chord) = barSection[bar];
					Console.WriteLine( $"  bar {bar + 1,3}  [{sect}, chord {chord}]" );
					lastBar = bar;
				}
				double beat = (tick - barTicks[bar]) / (double)Timing.TicksPerBeat + 1;
				Console.WriteLine( $"      {beat,5:0.00}   {string.Join( " ", midis )}" );
			}
			Console.WriteLine();
		}
	}

	/// <summary>Are the parts playing together?
	///
	/// For each voice: how often it puts a note ON a bar line, and how far off it sits when it
	/// does. Measured against the bar lines themselves, so a tempo curve, a short bar and the
	/// swing warp (which leaves downbeats anchored) cannot skew it. A voice that has drifted
	/// against the rest of the band shows up as a nonzero mean offset; one that never lands on a
	/// downbeat at all shows up as a low hit rate. Usage: <c>-- --grid [genre]</c>.</summary>
	static void Grid( int only = -1 )
	{
		Console.WriteLine( "downbeat agreement, and distance from the song's own tick grid" );
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			if ( only >= 0 && g != only ) continue;
			Console.WriteLine();
			Console.WriteLine( $"── genre {g} ({VibeCodec.Genres[g]}) ──" );
			foreach ( var (name, solo) in Voices )
			{
				if ( name == "DRUMS" ) continue;      // synthesised into the buffer, not events
				var cfg = new MusicGen.Config { Genre = g, SampleRate = 22050 };
				cfg.DrumVol = cfg.BassVol = cfg.SkankVol = cfg.OrganVol = cfg.MelodyVol =
					cfg.HornVol = cfg.KeysVol = cfg.RhythmGtrVol = cfg.LeadGtrVol = 0f;
				solo( cfg );
				var mg = MusicGen.BeginPlan( $"grid:{g}", cfg );
				var (starts, bars) = mg.Onsets();
				if ( starts.Length == 0 ) continue;

				// How far each onset sits from the nearest legal grid position (a tick, warped by the
				// swing and the tempo curve). Double-tracking offsets the second take by ~9 ms by
				// design, so a few ms of mean is the width, not a drift.
				var grid = mg.GridSamples();
				double gsum = 0, gworst = 0; int gn = 0, gbad = 0;
				foreach ( var st in starts )
				{
					int i = NearestBar( grid, st );
					double d = Math.Abs( st - grid[i] ) / 22.05;
					if ( i + 1 < grid.Length ) d = Math.Min( d, Math.Abs( st - grid[i + 1] ) / 22.05 );
					gsum += d; gn++; gworst = Math.Max( gworst, d );
					if ( d > 25 ) gbad++;
				}

				int landed = 0; double sum = 0, worst = 0;
				for ( int b = 0; b + 1 < bars.Length; b++ )
				{
					double window = (bars[b + 1] - bars[b]) / 8.0;   // half an eighth either side
					int best = int.MaxValue;
					foreach ( var st in starts )
					{
						int d = st - bars[b];
						if ( Math.Abs( d ) < Math.Abs( best ) ) best = d;
						if ( d > window ) break;
					}
					if ( Math.Abs( best ) > window ) continue;
					landed++; sum += best / 22.05; worst = Math.Max( worst, Math.Abs( best / 22.05 ) );
				}
				Console.WriteLine( $"  {name,-9} {starts.Length,5} onsets   downbeat {landed * 100.0 / (bars.Length - 1),3:0}%"
					+ $" ({(landed > 0 ? sum / landed : 0),5:0.0} ms)   off-grid: mean {gsum / Math.Max( 1, gn ),4:0.0} ms,"
					+ $" worst {gworst,5:0.0} ms, {gbad * 100.0 / Math.Max( 1, gn ),4:0.0}% over 25 ms" );
			}
		}
	}

	static int NearestBar( int[] bars, int sample )
	{
		int lo = 0, hi = bars.Length - 1;
		while ( lo < hi )
		{
			int mid = (lo + hi + 1) / 2;
			if ( bars[mid] <= sample ) lo = mid; else hi = mid - 1;
		}
		return lo;
	}

	/// <summary>
	/// THE CYMBAL HAND, measured against itself. A ride does not replace a crash or a tom — it
	/// replaces the HI-HAT, because both are the same hand playing the same pulse, and that makes
	/// the hats the one honest reference for how loud a ride should be. Nothing else in --levels
	/// can see this: the kit is a single row there, so a ride that has quietly fallen 6 dB behind
	/// the hats it stands in for moves the whole kit by a fraction of a dB and reads as noise.
	///
	/// Four bars of the genre's own groove, three ways, off the same stream: hats, ride, and the
	/// crash-ride. Everything but the cymbal hand is identical between them, so the difference IS
	/// the hand.
	/// </summary>
	static void CymbalHand()
	{
		Console.WriteLine( "the cymbal hand ALONE, dB relative to the hats it replaces (pre-master)" );
		Console.WriteLine();
		Console.WriteLine( "  genre                  hats      ride   crash-ride" );
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			// Energy of four bars of the genre's groove, with the cymbal hand on or off. The hand's
			// own energy is the DIFFERENCE: everything else about the take is identical, so
			// subtracting the take with no hand at all leaves exactly the voice being measured.
			// Without that subtraction the kick and snare drown it — doubling the ride's level
			// moved the whole-kit number by 0.4 dB, which reads as "the level does nothing" when
			// what it really means is "you are measuring the wrong thing".
			double Energy( bool ride, bool crashRide, bool hand )
			{
				var cfg = new MusicGen.Config { SampleRate = 44100, Genre = g };
				if ( !hand ) { cfg.HatVol = 0f; cfg.CrashVol = 0f; }
				var m = MusicGen.ForAudition( cfg, 9.0, 116 );
				m.AuditionKit( g );
				m.AuditionCymbalHand( ride, crashRide );
				var noise = new Rng( "levels:hand" );
				for ( int b = 0; b < 4; b++ ) m.AuditionBar( b * 4 * Timing.TicksPerBeat, noise );
				var (l, r) = m.AuditionBuffers();
				double sum = 0;
				for ( int i = 0; i < l.Length; i++ ) sum += (double)l[i] * l[i] + (double)r[i] * r[i];
				return sum;
			}
			// Each case is paired with its OWN silent take, not one shared baseline: the hats
			// consume the drum noise stream per sample and the cymbals do not, so a take with the
			// ride on has different ghost notes and kick pushes from a take with the hats on. The
			// subtraction is only valid inside a pair where everything but the hand's volume is
			// identical.
			double Hand( bool ride, bool crashRide )
				=> Energy( ride, crashRide, true ) - Energy( ride, crashRide, false );
			double hats = Hand( false, false );
			double ride = Hand( true, false );
			double cr = Hand( false, true );
			string D( double x ) => hats <= 0 || x <= 0 ? "    —"
				: $"{10 * Math.Log10( x / hats ),6:0.0}";
			Console.WriteLine( $"  {g} {VibeCodec.Genres[g],-12}  {Math.Sqrt( hats ),8:0.000}  "
				+ $"{D( ride )}  {D( cr )}" );
		}
	}

	static void Levels()
	{
		Console.WriteLine( "voice levels, dB relative to that genre's drums (pre-master)" );
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			Console.WriteLine();
			Console.WriteLine( $"── genre {g} ({VibeCodec.Genres[g]}) ──" );
			double reference = 0;
			foreach ( var (name, solo) in Voices )
			{
				var cfg = new MusicGen.Config { Genre = g, SampleRate = 22050 };
				cfg.DrumVol = cfg.BassVol = cfg.SkankVol = cfg.OrganVol = cfg.MelodyVol =
					cfg.HornVol = cfg.KeysVol = cfg.RhythmGtrVol = cfg.LeadGtrVol = 0f;
				solo( cfg );
				var g2 = MusicGen.BeginPlan( $"levels:{g}", cfg );
				g2.RenderPitchedRange( 0, g2.TotalSamples );
				var (peak, rms) = g2.RawLevels();
				if ( name == "DRUMS" ) reference = rms;
				if ( rms <= 0 ) { Console.WriteLine( $"  {name,-9}  —" ); continue; }
				double db = 20 * Math.Log10( rms / Math.Max( 1e-9, reference ) );
				Console.WriteLine( $"  {name,-9}  rms {rms:0.0000}  peak {peak:0.00}  {db,6:0.0} dB" );
			}
		}
	}

	static void WavTests()
	{
		// These are container checks — they read the 44-byte header, not the audio — so the song
		// behind them is rendered at the lowest rate that still produces one.
		var wav = MusicGen.Generate( "rotaliate", new MusicGen.Config { SampleRate = 8000 } );
		Check( "WAV is produced", wav != null && wav.Length > 44 );
		Check( "WAV starts RIFF", Ascii( wav, 0, 4 ) == "RIFF" );
		Check( "WAV is a WAVE", Ascii( wav, 8, 4 ) == "WAVE" );
		Check( "WAV has a fmt chunk", Ascii( wav, 12, 4 ) == "fmt " );
		Check( "WAV declares its own length",
			BitConverter.ToInt32( wav, 4 ) == wav.Length - 8 );
		Check( "WAV is 16-bit", BitConverter.ToInt16( wav, 34 ) == 16 );
		Check( "WAV is stereo", BitConverter.ToInt16( wav, 22 ) == MusicGen.Channels );
	}

	static void RenderDigestTests( bool bless )
	{
		var path = Path.Combine( AppContext.BaseDirectory, "..", "..", "..", "digests.txt" );
		path = Path.GetFullPath( path );

		var golden = new Dictionary<string, string>();
		if ( File.Exists( path ) )
			foreach ( var line in File.ReadAllLines( path ) )
			{
				var t = line.Trim();
				if ( t.Length == 0 || t[0] == '#' ) continue;
				int sp = t.IndexOf( ' ' );
				if ( sp > 0 ) golden[t[..sp]] = t[(sp + 1)..].Trim();
			}

		var fresh = new List<string>();
		foreach ( var (seed, digest, _) in MatrixDigests() )
		{
			fresh.Add( $"{seed} {digest}" );

			if ( bless ) { Console.WriteLine( $"  bless {seed} → {digest}" ); continue; }

			if ( !golden.TryGetValue( seed, out var want ) )
				Check( $"{seed} has a recorded digest", false, "run with --bless to record it" );
			else
				Check( $"{seed} renders to its recorded digest", want == digest,
					$"want {want}, got {digest}" );
		}

		if ( !bless ) return;

		var sb = new StringBuilder();
		sb.AppendLine( "# Render digests — SHA-256 over the interleaved 16-bit PCM of each seed," );
		sb.AppendLine( $"# rendered at {MatrixRate} Hz. Changing that rate invalidates every hash below." );
		sb.AppendLine( "#" );
		sb.AppendLine( "# NOT a cross-commit contract: audio is expected to change when the engine" );
		sb.AppendLine( "# changes. This file is a tripwire for refactors that are supposed to be PURE" );
		sb.AppendLine( "# (a file split, a type extraction) — bless before, run after, expect silence." );
		sb.AppendLine( "# Re-bless in the same commit as any deliberate audible change:" );
		sb.AppendLine( "#   make test-engine-bless" );
		foreach ( var line in fresh ) sb.AppendLine( line );
		File.WriteAllText( path, sb.ToString() );
		Console.WriteLine( $"  wrote {fresh.Count} digests to {path}" );
	}

	// ── plumbing ─────────────────────────────────────────────────────────────────────────

	/// <summary>Render one song exactly the way the players do: default Config, vibe overlaid,
	/// PRNG seeded on <c>"{tag}:{n}"</c> — but at <see cref="MatrixRate"/> rather than the
	/// player's rate, which is the single biggest cost in this harness and buys nothing here.
	/// Every check over these renders (determinism, the digest tripwire) is about whether two
	/// runs of the same code agree, and that is as true at 22.05 kHz as at 44.1.</summary>
	static short[] Render( string vibe, string tag, int n )
	{
		var cfg = new MusicGen.Config { SampleRate = MatrixRate };
		VibeCodec.Apply( vibe, cfg );
		return MusicGen.GenerateSamples( $"{tag}:{n}", cfg, out _ );
	}

	const int MatrixRate = 22050;

	/// <summary>Every Nth matrix seed is rendered twice for the determinism check — a stride rather
	/// than a prefix so the sample spreads across genres as the matrix grows. What that check is
	/// really asking is whether the engine carries hidden state between songs in one process: a
	/// property of the engine, not of a seed, so a few songs settle it. The recorded digests still
	/// cover all ten, across runs.</summary>
	const int DoubleRendered = 4;

	static (string Seed, string First, string Second)[] _matrix;

	/// <summary>Every matrix seed rendered TWICE, in parallel, hashed. Both the determinism check
	/// (the two renders agree) and the digest tripwire (the first matches the recorded hash) read
	/// this one pass — the harness used to render the matrix three times over, serially, at full
	/// rate. Renders are independent and every Rng is per-instance, so the only ordering that
	/// matters is the result array's, which is by index.</summary>
	static (string Seed, string First, string Second)[] MatrixDigests()
	{
		if ( _matrix != null ) return _matrix;
		// One task per RENDER, not one per seed: the seeds that get rendered twice are twice the
		// work, and a per-seed loop would leave half the machine idle waiting for them.
		var jobs = new List<(int Row, bool Second)>();
		for ( int i = 0; i < Matrix.Length; i++ )
		{
			jobs.Add( (i, false) );
			if ( i % DoubleRendered == 0 ) jobs.Add( (i, true) );
		}
		var first = new string[Matrix.Length];
		var second = new string[Matrix.Length];
		System.Threading.Tasks.Parallel.ForEach( jobs, j =>
		{
			var (vibe, tag, n) = Matrix[j.Row];
			var digest = Digest( Render( vibe, tag, n ) );
			if ( j.Second ) second[j.Row] = digest; else first[j.Row] = digest;
		} );

		var rows = new (string, string, string)[Matrix.Length];
		for ( int i = 0; i < Matrix.Length; i++ )
		{
			var (vibe, tag, n) = Matrix[i];
			rows[i] = (Seed( vibe, tag, n ), first[i], second[i]);
		}
		return _matrix = rows;
	}

	static string MatrixDigest( string seed )
	{
		foreach ( var row in MatrixDigests() ) if ( row.Seed == seed ) return row.First;
		throw new InvalidOperationException( $"{seed} is not in the matrix" );
	}

	static string Seed( string vibe, string tag, int n ) => $"{vibe}:{tag}:{n}";

	/// <summary>Song <paramref name="n"/>'s shuffle vibe, rolled from a config starting at
	/// <paramref name="fromGenre"/> — used to show the result doesn't depend on that start.</summary>
	static MusicGen.Config Rolled( string tag, int n, int fromGenre )
	{
		var c = new MusicGen.Config { Genre = fromGenre };
		VibeCodec.RollFrom( c, VibeCodec.VibeSeed( tag, n ) );
		return c;
	}

	/// <summary>The finest gap in a figure, in ticks — how subdivided it actually is. A pattern's
	/// span already wraps around its own loop, so this reads the figure as it repeats.</summary>
	static int MinSpan( Pattern p )
	{
		int min = p.LengthTicks;
		foreach ( var h in p.Slice( 0, p.LengthTicks ) ) min = Math.Min( min, h.SpanTicks );
		return min;
	}

	static bool SameDegrees( int[] a, int[] b )
	{
		if ( a.Length != b.Length ) return false;
		for ( int i = 0; i < a.Length; i++ ) if ( a[i] != b[i] ) return false;
		return true;
	}

	static bool SameSamples( short[] a, short[] b )
	{
		if ( a == null || b == null || a.Length != b.Length ) return false;
		for ( int i = 0; i < a.Length; i++ ) if ( a[i] != b[i] ) return false;
		return true;
	}

	static bool AllIn01( Rng r, int n )
	{
		for ( int i = 0; i < n; i++ ) { var v = r.Next(); if ( v < 0f || v >= 1f ) return false; }
		return true;
	}

	static string Digest( short[] s )
	{
		var bytes = new byte[s.Length * 2];
		Buffer.BlockCopy( s, 0, bytes, 0, bytes.Length );
		return Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();
	}

	static string Ascii( byte[] b, int off, int len ) => Encoding.ASCII.GetString( b, off, len );

	static void Banner( string name )
	{
		Console.WriteLine();
		Console.WriteLine( $"── {name} ──" );
	}

	static void Check( string what, bool ok, string detail = null )
	{
		if ( ok ) { _passed++; Console.WriteLine( $"  ok   {what}" ); return; }
		_failed++;
		Console.WriteLine( detail == null ? $"  FAIL {what}" : $"  FAIL {what} — {detail}" );
	}
}
