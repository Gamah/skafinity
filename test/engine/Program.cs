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
			var scales = Harmony.ScalesFor( g );
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
			foreach ( var prog in Harmony.ProgressionsFor( g ) )
			{
				chordsSane &= prog.Length > 0;
				foreach ( var deg in prog )
					foreach ( var scale in Harmony.ScalesFor( g ) )
					{
						int m = Harmony.ChordRoot( 48, scale, deg );
						chordsSane &= m > 0 && m < 128;
					}
			}
		Check( "every progression degree resolves to a MIDI pitch", chordsSane );

		// Bass patterns index a fixed 8-cell bar; a stray cell value would index off a table.
		bool bassSane = true;
		for ( int g = 0; g < VibeCodec.GenreCount; g++ )
			foreach ( var p in Harmony.BassPatternsFor( g ) )
				bassSane &= p.Length == 8;
		Check( "every bass pattern is 8 cells", bassSane );

		Check( "Midi(69) is A440", Math.Abs( Osc.Midi( 69 ) - 440f ) < 0.001f );
		Check( "Midi(81) is an octave above Midi(69)",
			Math.Abs( Osc.Midi( 81 ) - 2f * Osc.Midi( 69 ) ) < 0.01f );
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
			Check( $"genre {g} vibe fits the wire budget", enc.Length <= VibeCodec.MaxLength );

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
