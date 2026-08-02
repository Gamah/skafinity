using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Skafinity.EngineTests;

/// <summary>
/// The groove/accent measuring tool: folds an annotated MIDI drum dataset onto a bar and prints
/// what it finds next to what <see cref="DrumGroove"/> and the genre's accent weights claim.
///
/// Usage: <c>-- --groove &lt;dataset-dir&gt; [beat|fill]</c>.
///
/// WHY A DATASET AND NOT THE ENGINE OR A RECORD. Measuring the engine's own render is circular —
/// the groove table IS the specification, and onset detection on a WAV of it can only re-derive
/// what the table already says exactly. Onset-detecting commercial recordings is the right
/// comparison and the wrong material: the drums are not isolated, a loudness-war master compresses
/// away the very downbeat-to-offbeat dynamic being measured, and the audio is somebody else's.
/// A performed, bar-aligned, velocity-carrying dataset has the measurement already made.
///
/// The dataset this was written against is Google Magenta's Groove MIDI Dataset (CC BY 4.0;
/// 1150 files, 13.6 h, 10 drummers, each performance played to a metronome at a stated tempo, so
/// it is bar-aligned with no beat-tracking step). Verified 2026-08-02:
/// https://magenta.tensorflow.org/datasets/groove — that page carries the Roland TD-11 pitch map
/// reproduced in <see cref="Bucket"/> and is where these constants come from. [SOURCE]
///
/// NOTHING FROM THE DATASET GOES IN THE REPO. It is input that drives a decision, not an asset:
/// it lives outside the working tree, and what gets committed is the derived numbers with the
/// dataset cited beside them — the same shape the tempo anchors in GenreProfile already have,
/// and what satisfies the CC BY attribution. That is also why this is a diagnostic mode and not
/// a suite check: `make test-engine` must keep passing on a machine that has never seen it.
/// </summary>
static class GrooveData
{
	// ── the Roland TD-11 map, folded onto the four things a DrumGroove names ──
	// The engine's Cymbal pattern is deliberately hats-OR-ride (which of the two plays is the
	// section's roll, not the groove's business), so both land in one bucket. A crash is not part
	// of a groove pattern at all — it is CrashOnOne — so it gets its own and is excluded from the
	// placement histograms.
	enum Drum { None, Kick, Snare, Cymbal, Crash, Tom }

	static Drum Bucket( int pitch ) => pitch switch
	{
		36 => Drum.Kick,
		37 or 38 or 40 => Drum.Snare,                        // x-stick, head, rim
		42 or 22 or 44 or 46 or 26 => Drum.Cymbal,           // hat: closed bow/edge, pedal, open bow/edge
		51 or 53 or 59 => Drum.Cymbal,                       // ride: bow, bell, edge
		49 or 52 or 55 or 57 => Drum.Crash,
		43 or 45 or 47 or 48 or 50 or 58 => Drum.Tom,
		_ => Drum.None,
	};

	// The dataset's style tags that map onto a genre in this engine, and the genre they map to.
	// METAL IS NOT IN THE DATASET and is deliberately absent rather than borrowing rock's numbers.
	// Reggae maps to genre 0 because genre 0's two grooves ARE reggae grooves ("one drop",
	// "steppers") sitting in a genre that has since been retuned to the third wave — which makes
	// this the one row where a mismatch is the expected result rather than a defect in the tool.
	static readonly (string Style, int Genre)[] Styles =
	{
		("reggae", 0), ("rock", 1), ("country", 2), ("punk", 4), ("pop", 5),
	};

	const int Slots = 16;   // one bar of sixteenths — the finest grid every groove in the table uses

	sealed class Acc
	{
		public int Files, Bars;
		public readonly int[,] Onsets = new int[6, Slots];      // [drum, slot] hit count
		public readonly double[,] VelSum = new double[6, Slots];
		public readonly int[,] VelN = new int[6, Slots];
	}

	public static void Run( string dir, string beatType )
	{
		string csv = Path.Combine( dir, "info.csv" );
		if ( !File.Exists( csv ) )
		{
			Console.WriteLine( $"no info.csv under {dir} — point --groove at the unpacked dataset root" );
			return;
		}

		var rows = ReadCsv( csv );
		var acc = new Dictionary<string, Acc>();
		foreach ( var (style, _) in Styles ) acc[style] = new Acc();

		int used = 0, skipped = 0;
		foreach ( var r in rows )
		{
			if ( r["time_signature"] != "4-4" ) continue;
			if ( r["beat_type"] != beatType ) continue;
			string style = r["style"].Split( '/' )[0];
			if ( !acc.TryGetValue( style, out var a ) ) continue;

			string path = Path.Combine( dir, r["midi_filename"] );
			if ( !File.Exists( path ) ) { skipped++; continue; }
			try { Fold( path, a ); used++; }
			catch ( Exception e ) { Console.WriteLine( $"  ! {r["midi_filename"]}: {e.Message}" ); skipped++; }
		}

		Console.WriteLine( $"── groove data ({beatType}, 4/4) — {used} files read, {skipped} skipped ──" );
		Console.WriteLine();

		foreach ( var (style, genre) in Styles )
		{
			var a = acc[style];
			Console.WriteLine( $"══ {style} → genre {genre} ({VibeCodec.Genres[genre]}) — {a.Files} files, {a.Bars} bars" );
			if ( a.Bars == 0 ) { Console.WriteLine( "   (nothing)" ); Console.WriteLine(); continue; }
			Placement( a );
			Accents( a );
			Console.WriteLine();
		}

		Console.WriteLine( "Placement rows are the % of bars carrying that drum at that sixteenth." );
		Console.WriteLine( "Accent rows are mean velocity per metric position, each drum first" );
		Console.WriteLine( "normalised by its OWN mean (so a loud kick cannot outvote a quiet hat)" );
		Console.WriteLine( "and the set then scaled so beat 3 = 1.00 — the engine's convention," );
		Console.WriteLine( "where MetricGain returns a literal 1f there. Compare against the" );
		Console.WriteLine( "genre's AccentDown / AccentBack / AccentOff in GenreProfile.cs." );
	}

	// ── the report ──

	static void Placement( Acc a )
	{
		// The hit counts behind the percentages — a row backed by 30 hits and a row backed by
		// 30000 print the same way otherwise, and the genres here differ by two orders of that.
		var tot = new System.Text.StringBuilder( "   hits    " );
		for ( int d = 1; d <= 5; d++ )
		{
			int t = 0; for ( int s = 0; s < Slots; s++ ) t += a.Onsets[d, s];
			tot.Append( $" {(Drum)d} {t}" );
		}
		Console.WriteLine( tot );
		Console.WriteLine( "   slot        1  e  &  a  2  e  &  a  3  e  &  a  4  e  &  a" );
		foreach ( var d in new[] { Drum.Kick, Drum.Snare, Drum.Cymbal, Drum.Tom } )
		{
			var sb = new System.Text.StringBuilder( $"   {d,-8}" );
			for ( int s = 0; s < Slots; s++ )
			{
				int pct = (int)Math.Round( 100.0 * a.Onsets[(int)d, s] / a.Bars );
				sb.Append( pct >= 100 ? " **" : pct == 0 ? "  ." : $"{pct,3}" );
			}
			Console.WriteLine( sb );
		}
	}

	// The four bins MetricGain actually distinguishes: the downbeat, the backbeat (beats 2 and 4),
	// beat 3 (its fixed 1f reference), and everything off the beat.
	static void Accents( Acc a )
	{
		double[] sum = new double[4];
		int[] n = new int[4];

		// Normalise per drum before binning. Position and instrument are confounded otherwise —
		// hats sit on every eighth and so dominate the off-beat bin, kick and snare dominate the
		// on-beat ones, and the ratio would then be measuring which drum plays where rather than
		// how hard it is hit. Dividing each drum by its own mean removes that.
		for ( int d = 1; d <= 5; d++ )
		{
			if ( d == (int)Drum.Crash ) continue;
			double tot = 0; int totN = 0;
			for ( int s = 0; s < Slots; s++ ) { tot += a.VelSum[d, s]; totN += a.VelN[d, s]; }
			if ( totN == 0 ) continue;
			double mean = tot / totN;

			for ( int s = 0; s < Slots; s++ )
			{
				if ( a.VelN[d, s] == 0 ) continue;
				int bin = Bin( s );
				sum[bin] += a.VelSum[d, s] / mean;
				n[bin] += a.VelN[d, s];
			}
		}

		double Val( int b ) => n[b] == 0 ? double.NaN : sum[b] / n[b];
		double refv = Val( 2 );
		string F( int b ) => n[b] == 0 ? "  —  " : $"{Val( b ) / refv,5:0.00}";

		Console.WriteLine( $"   accent    down {F( 0 )} ({n[0]} hits)   back {F( 1 )} ({n[1]})   " +
			$"beat3 {F( 2 )} ({n[2]})   off {F( 3 )} ({n[3]})" );
	}

	static int Bin( int slot ) => slot % 4 != 0 ? 3       // not on a beat at all
		: slot == 0 ? 0                                   // the downbeat
		: slot == 8 ? 2                                   // beat 3 — the engine's 1f reference
		: 1;                                              // beats 2 and 4

	// ── folding one performance onto a bar ──

	static void Fold( string path, Acc a )
	{
		var hits = Midi.Read( path, out int tpq );
		if ( hits.Count == 0 ) return;

		int barTicks = tpq * 4;                            // 4/4 only — the caller filtered
		double slotTicks = barTicks / (double)Slots;

		int last = 0;
		foreach ( var h in hits ) if ( h.Tick > last ) last = h.Tick;
		int bars = last / barTicks + 1;

		a.Files++;
		a.Bars += bars;
		foreach ( var h in hits )
		{
			var d = Bucket( h.Pitch );
			if ( d == Drum.None ) continue;
			// Nearest sixteenth, wrapping the last one back onto the next bar's downbeat.
			int slot = (int)Math.Round( h.Tick % barTicks / slotTicks ) % Slots;
			a.Onsets[(int)d, slot]++;
			a.VelSum[(int)d, slot] += h.Vel;
			a.VelN[(int)d, slot]++;
		}
	}

	// ── info.csv ──
	// The dataset's own metadata: no quoted fields, no embedded commas, so a split is honest here.
	static List<Dictionary<string, string>> ReadCsv( string path )
	{
		var lines = File.ReadAllLines( path );
		var head = lines[0].Split( ',' );
		var rows = new List<Dictionary<string, string>>();
		for ( int i = 1; i < lines.Length; i++ )
		{
			if ( lines[i].Length == 0 ) continue;
			var f = lines[i].Split( ',' );
			if ( f.Length != head.Length ) continue;
			var d = new Dictionary<string, string>();
			for ( int c = 0; c < head.Length; c++ ) d[head[c]] = f[c];
			rows.Add( d );
		}
		return rows;
	}
}

/// <summary>
/// The smallest Standard MIDI File reader that answers this question: every note-on, at its
/// absolute tick, with its velocity. Tempo is ignored deliberately — placement is measured in
/// ticks against the bar, and the dataset's stated bpm is metadata rather than something to
/// re-derive.
///
/// SMF 1.0: an MThd chunk (format, ntrks, division) then ntrks MTrk chunks of delta-time +
/// event. Running status is in the format and this handles it; a file that omits it reads the
/// same either way.
/// </summary>
static class Midi
{
	public readonly struct Hit
	{
		public readonly int Tick, Pitch, Vel;
		public Hit( int tick, int pitch, int vel ) { Tick = tick; Pitch = pitch; Vel = vel; }
	}

	public static List<Hit> Read( string path, out int tpq )
	{
		var b = File.ReadAllBytes( path );
		int p = 0;
		if ( Str( b, 0, 4 ) != "MThd" ) throw new InvalidDataException( "not a MIDI file" );
		int hlen = Be32( b, 4 );
		int division = Be16( b, 12 );
		if ( (division & 0x8000) != 0 ) throw new InvalidDataException( "SMPTE division unsupported" );
		tpq = division;
		p = 8 + hlen;

		var hits = new List<Hit>();
		while ( p + 8 <= b.Length )
		{
			int len = Be32( b, p + 4 );
			if ( Str( b, p, 4 ) != "MTrk" ) { p += 8 + len; continue; }
			ReadTrack( b, p + 8, Math.Min( p + 8 + len, b.Length ), hits );
			p += 8 + len;
		}
		hits.Sort( ( x, y ) => x.Tick.CompareTo( y.Tick ) );
		return hits;
	}

	static void ReadTrack( byte[] b, int p, int end, List<Hit> hits )
	{
		int tick = 0, status = 0;
		while ( p < end )
		{
			tick += Vlq( b, ref p );
			if ( p >= end ) break;
			if ( (b[p] & 0x80) != 0 ) status = b[p++];      // else running status: reuse the last

			int hi = status & 0xF0;
			if ( status == 0xFF )                            // meta: type, length, data
			{
				p++;
				int n = Vlq( b, ref p );
				p += n;
			}
			else if ( status == 0xF0 || status == 0xF7 )     // sysex: length, data
			{
				int n = Vlq( b, ref p );
				p += n;
			}
			else if ( hi == 0xC0 || hi == 0xD0 ) p += 1;     // program change / channel pressure
			else if ( hi == 0x90 )
			{
				int pitch = b[p], vel = b[p + 1];
				p += 2;
				if ( vel > 0 ) hits.Add( new Hit( tick, pitch, vel ) );   // vel 0 IS a note-off
			}
			else p += 2;                                     // note-off, aftertouch, CC, pitch bend
		}
	}

	static int Vlq( byte[] b, ref int p )
	{
		int v = 0;
		while ( p < b.Length )
		{
			int c = b[p++];
			v = (v << 7) | (c & 0x7F);
			if ( (c & 0x80) == 0 ) break;
		}
		return v;
	}

	static string Str( byte[] b, int p, int n ) => System.Text.Encoding.ASCII.GetString( b, p, n );
	static int Be16( byte[] b, int p ) => (b[p] << 8) | b[p + 1];
	static int Be32( byte[] b, int p ) => (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
}
