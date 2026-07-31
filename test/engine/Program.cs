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

		Banner( "prng + determinism" );
		DeterminismTests();

		Banner( "harmony" );
		HarmonyTests();

		Banner( "time base" );
		TimingTests();

		Banner( "genre feel" );
		GenreProfileTests();

		Banner( "wired knobs" );
		WiredKnobTests();

		Banner( "structure" );
		StructureTests();

		Banner( "vibe codec" );
		VibeTests();

		Banner( "wav container" );
		WavTests();

		Banner( bless ? "render digest (blessing)" : "render digest" );
		RenderDigestTests( bless );

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

		// Same tag+cfg ⇒ same song, twice in a row, in the same process.
		foreach ( var (vibe, tag, n) in Matrix )
		{
			var s1 = Render( vibe, tag, n );
			var s2 = Render( vibe, tag, n );
			Check( $"song {Seed( vibe, tag, n )} renders identically twice", SameSamples( s1, s2 ) );
		}

		// Different n ⇒ a different song (the infinite sequence must actually advance).
		var x = Render( "0", "rotaliate", 0 );
		var y = Render( "0", "rotaliate", 1 );
		Check( "stepping n produces a different song", !SameSamples( x, y ) );
	}

	static void HarmonyTests()
	{
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

		// Bass patterns index a fixed 8-cell bar; a stray cell value would index off a table.
		bool bassSane = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var p in GenreProfile.For( g ).BassPatterns )
				bassSane &= p.Length == 8;
		Check( "every bass pattern is 8 cells", bassSane );

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
		// Swing is genre character rather than a knob, so every genre must declare a usable band
		// and a draw must stay inside it.
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
		{
			var p = GenreProfile.For( g );
			Check( $"genre {g} swing band is ordered", p.SwingMin <= p.SwingMax );
			Check( $"genre {g} swing band is in range", p.SwingMin >= 0f && p.SwingMax <= 0.4f );

			bool inBand = true, fastTighter = true;
			for ( int i = 0; i < 300; i++ )
			{
				var rng = new Rng( $"swing:{g}:{i}" );
				float s = p.DrawSwing( rng, false );
				inBand &= s >= p.SwingMin - 0.0001f && s <= p.SwingMax + 0.0001f;

				float fast = GenreProfile.For( g ).DrawSwing( new Rng( $"swing:{g}:{i}" ), true );
				fastTighter &= fast <= s + 0.0001f;
			}
			Check( $"genre {g} draws inside its band", inBand );
			Check( $"genre {g} tightens toward straight when fast", fastTighter );
		}

		// The point of the row: metal and punk are machine-straight, ska is pushed. Without this
		// the bands could all quietly collapse to the same values and nothing would notice.
		var metal = GenreProfile.For( 3 );
		var punk = GenreProfile.For( 4 );
		var ska = GenreProfile.For( 0 );
		Check( "metal is effectively straight", metal.SwingMax <= 0.05f );
		Check( "punk is effectively straight", punk.SwingMax <= 0.05f );
		Check( "ska always has a pushed offbeat", ska.SwingMin >= 0.08f );
		Check( "ska swings harder than metal", ska.SwingMin > metal.SwingMax );

		// Swing varies song to song rather than being one constant per genre.
		var seen = new HashSet<int>();
		for ( int i = 0; i < 100; i++ )
			seen.Add( (int)Math.Round( ska.DrawSwing( new Rng( $"ska:{i}" ), false ) * 1000 ) );
		Check( "swing varies between songs of the same genre", seen.Count > 10, $"{seen.Count} distinct values" );

		// A song's swing must follow only from its seed, or the shuffle line would not be
		// reproducible.
		Check( "a song's swing is reproducible from its seed",
			ska.DrawSwing( new Rng( "ska:7" ), false ) == ska.DrawSwing( new Rng( "ska:7" ), false ) );

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

			Check( $"genre {g} has a harmonic rhythm of at least a bar", p.ChordBars >= 1 );
		}

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

		// A genre with a fixed drum style must not consume a draw for it — that is what lets the
		// style be a table entry rather than a roll.
		Check( "a fixed drum style is the same whatever the stream says",
			GenreProfile.For( 3 ).DrawDrumStyle( new Rng( "a" ), false )
				== GenreProfile.For( 3 ).DrawDrumStyle( new Rng( "b" ), true ) );
		bool styleSane = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			for ( int i = 0; i < 50; i++ )
			{
				int st = GenreProfile.For( g ).DrawDrumStyle( new Rng( $"st:{g}:{i}" ), i % 2 == 0 );
				styleSane &= st >= 0 && st <= 4;
			}
		Check( "every drawn drum style is a real style", styleSane );

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
		Check( "the HORN SECTION knob changes the song",
			!SameSamples( Knob( 0, c => c.HornSectionChance = 0f ),
				Knob( 0, c => c.HornSectionChance = 1f ) ) );
		Check( "the ORGAN BUBBLE knob changes the song",
			!SameSamples( Knob( 0, c => c.OrganBubbleChance = 0f ),
				Knob( 0, c => c.OrganBubbleChance = 1f ) ) );

		// ForceInstrument and the four *Weight knobs were inert while the lead was hardcoded to
		// the trumpet, which also meant the finished Sax/Organ/Trombone voices never played.
		Check( "forcing a lead instrument changes the song",
			!SameSamples( Knob( 0, c => c.ForceInstrument = 0 ),
				Knob( 0, c => c.ForceInstrument = 3 ) ) );
		bool everyLeadPlays = true;
		for ( int inst = 0; inst < 4; inst++ )
		{
			var s = Knob( 0, c => c.ForceInstrument = inst );
			everyLeadPlays &= s != null && s.Length > 0;
		}
		Check( "every lead instrument renders", everyLeadPlays );

		// The lead weights must be reachable too — zeroing every one but the trombone has to
		// give the same song as forcing the trombone outright.
		Check( "the lead weights pick the same instrument as forcing it",
			SameSamples( Knob( 0, c => c.ForceInstrument = 3 ),
				Knob( 0, c =>
				{
					c.ForceInstrument = -1;
					c.TrumpetWeight = c.SaxWeight = c.OrganWeight = 0f;
					c.TromboneWeight = 1f;
				} ) ) );

		// The TEMPO knob is the replacement for the retired absolute band: slower must mean a
		// longer song, and it must be the same song.
		var slow = Knob( 1, c => c.TempoScale = 0.70f );
		var quick = Knob( 1, c => c.TempoScale = 1.45f );
		Check( "the TEMPO knob changes how long a song runs", slow.Length > quick.Length );
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
		var parts = MusicGen.BuildStructure();
		Check( "structure is non-empty", parts.Count > 0 );

		int bars = 0;
		bool positive = true;
		foreach ( var p in parts ) { bars += p.Bars; positive &= p.Bars > 0; }
		Check( "every part has a positive bar count", positive );
		Check( "structure is long enough to be a song", bars >= 32 );

		// The verse index is what selects a verse's variation; it must be dense from 0 so a
		// lookup keyed on it can't miss.
		int expected = 0;
		bool verseOrder = true;
		foreach ( var p in parts )
			if ( p.Type == Section.Verse ) verseOrder &= p.VerseIndex == expected++;
		Check( "verse indices run 0,1,2… in order", verseOrder );

		Check( "structure opens on an intro", parts[0].Type == Section.Intro );
		Check( "structure closes on an ending", parts[^1].Type == Section.Ending );

		bool named = true;
		foreach ( var p in parts ) named &= !string.IsNullOrEmpty( MusicGen.SectionKey( p.Type ) );
		Check( "every section has a key string", named );
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

	static void WavTests()
	{
		var wav = MusicGen.Generate( "rotaliate" );
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
		foreach ( var (vibe, tag, n) in Matrix )
		{
			var seed = Seed( vibe, tag, n );
			var digest = Digest( Render( vibe, tag, n ) );
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
		sb.AppendLine( "# Render digests — SHA-256 over the interleaved 16-bit PCM of each seed." );
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
	/// PRNG seeded on <c>"{tag}:{n}"</c>.</summary>
	static short[] Render( string vibe, string tag, int n )
	{
		var cfg = new MusicGen.Config();
		VibeCodec.Apply( vibe, cfg );
		return MusicGen.GenerateSamples( $"{tag}:{n}", cfg, out _ );
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
