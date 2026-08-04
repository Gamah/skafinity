using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Skafinity.EngineTests;

/// <summary>
/// THE SWEEP — how much a genre actually varies from song to song, measured over hundreds of
/// songs rather than argued about after listening to two.
///
/// It exists because the layers of a song have wildly different numbers of STATES and the ones a
/// listener uses to tell two songs apart are the small ones. A tune is unique every song; the
/// whole rhythm section comes out of three tables and so has a table-size ceiling that randomness
/// cannot reach; the form is one fixed list. None of that is visible from a render, and all of it
/// is arithmetic over the plan.
///
/// IT IS A DIAGNOSTIC, NOT A SUITE SECTION. It is not in <c>Main</c>'s section list, not in CI and
/// not in blessing, so the suite's ~25 s budget is untouched and this is free to take a minute.
/// Run it before a change and after it, and diff.
///
///     dotnet run --project test/engine -c Release -- --stats [N]
///
/// WHAT IT DOES NOT DO IS RE-DERIVE THE PLAN. Every onset it counts was recorded by the voice that
/// played it (see <see cref="PlanTrace"/>), so there is no second implementation of which figure a
/// bar plays to drift out of step with the composer's. The cost of that is that a song has to
/// actually compose — the drums synthesise during the plan pass — so it renders at
/// <see cref="Rate"/>, the lowest rate the questions here can be answered at. Nothing listens to
/// this; the rate is a straight multiplier on the cost and nothing else.
/// </summary>
static class Stats
{
	/// <summary>The sweep composes at the lowest rate its questions can be answered at. Every
	/// number here is in TICKS or is a count, so the sample rate reaches none of them — it only
	/// decides how much audio the plan pass's drum synthesis has to write and throw away.</summary>
	const int Rate = 8000;

	const string Tag = "rotaliate";

	static readonly TraceVoice[] AllVoices =
	{
		TraceVoice.Kick, TraceVoice.Snare, TraceVoice.Cymbal,
		TraceVoice.Bass, TraceVoice.Comp, TraceVoice.Keys, TraceVoice.Tune,
	};

	/// <summary>One song, reduced to the numbers the sweep asks about. Everything expensive is
	/// dropped here so a 3000-song sweep holds nothing but this.</summary>
	sealed class Song
	{
		public Pattern Comp, Keys, Bass;    // the song's chorus figures — reference identity is the state
		public DrumGroove Groove;
		public string Form;                 // the form's signature
		public int Bars;
		public HashSet<int>[] Onsets;       // per TraceVoice, the distinct ticks it played
		public int[] TuneTicks, TuneDegrees, TuneSpans;   // the CHORUS tune, as authored
		public int TuneLength;
	}

	public static void Run( int n )
	{
		Console.WriteLine( $"── sweep: {n} songs x {GenreProfile.Count} genres, tag \"{Tag}\", "
			+ $"composed at {Rate} Hz ──" );
		Console.WriteLine();

		var byGenre = new Song[GenreProfile.Count][];
		var sw = System.Diagnostics.Stopwatch.StartNew();
		for ( int g = 0; g < GenreProfile.Count; g++ )
		{
			var songs = new Song[n];
			int genre = g;
			Parallel.For( 0, n, i => songs[i] = Measure( genre, i ) );
			byGenre[g] = songs;
			Console.Write( $"\r  composed genre {g}…  " );
		}
		Console.WriteLine( $"\r  {n * GenreProfile.Count} songs in {sw.Elapsed.TotalSeconds:0.0} s" );
		Console.WriteLine();

		RhythmSectionStates( byGenre );
		Forms( byGenre );
		Cohesion( byGenre );
		TuneShape( byGenre );
		CrossGenre( byGenre );
	}

	static Song Measure( int genre, int n )
	{
		var cfg = new MusicGen.Config { Genre = genre, SampleRate = Rate };
		var trace = new PlanTrace();
		var g = MusicGen.BeginPlan( $"{Tag}:{n}", cfg, trace );

		var onsets = new HashSet<int>[AllVoices.Length];
		for ( int v = 0; v < AllVoices.Length; v++ ) onsets[v] = new HashSet<int>( trace.Of( AllVoices[v] ) );

		var (comp, keys, bass, groove) = g.SongParts;
		var (chorus, _) = g.Tunes;

		var form = new StringBuilder();
		int bars = 0;
		foreach ( var p in g.Form )
		{
			form.Append( $"{p.Type}{p.Bars}e{p.Energy:0.00}f{p.Feel:0.0}k{p.KeyShift}{(p.Hemiola ? "h" : "")} " );
			bars += p.Bars;
		}

		var s = new Song
		{
			Comp = comp, Keys = keys, Bass = bass, Groove = groove,
			Form = form.ToString(), Bars = bars, Onsets = onsets,
			TuneLength = chorus?.LengthTicks ?? 0,
		};
		if ( chorus != null )
		{
			s.TuneTicks = new int[chorus.Count];
			s.TuneDegrees = new int[chorus.Count];
			s.TuneSpans = new int[chorus.Count];
			for ( int i = 0; i < chorus.Count; i++ )
			{
				s.TuneTicks[i] = chorus.TickAt( i );
				s.TuneDegrees[i] = chorus.ValueAt( i );
				s.TuneSpans[i] = chorus.SpanAt( i );
			}
		}
		return s;
	}

	// ── how many different rhythm sections a genre can produce at all ────────────────────────

	// The three draws are the song's identity and they come out of tables, so this is a TABLE-SIZE
	// ceiling: no amount of seed randomness reaches past |CompFigures| x |BassPatterns| x |Grooves|.
	// One punk song in nine repeating the previous one's whole rhythm section is that ceiling, not
	// a draw that happened to collide.
	static void RhythmSectionStates( Song[][] byGenre )
	{
		Console.WriteLine( "── distinct rhythm-section states (comp x keys x bass x groove figures) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var seen = new HashSet<string>();
			foreach ( var s in byGenre[g] )
				seen.Add( $"{Id( s.Comp )}|{Id( s.Keys )}|{Id( s.Bass )}|{s.Groove.Name}" );
			var prof = GenreProfile.For( g );
			int keys = Math.Max( 1, prof.KeysFigures?.Length ?? 1 );
			int ceiling = prof.CompFigures.Length * keys * prof.BassPatterns.Length * prof.Grooves.Length;
			Console.WriteLine( $"  {Name( g ),-9} {seen.Count,4}   (table ceiling {ceiling}: "
				+ $"{prof.CompFigures.Length} comp x {keys} keys x {prof.BassPatterns.Length} bass"
				+ $" x {prof.Grooves.Length} groove)" );
		}
		Console.WriteLine();
	}

	static void Forms( Song[][] byGenre )
	{
		Console.WriteLine( "── distinct forms, and total bars ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var forms = new HashSet<string>();
			var bars = new SortedDictionary<int, int>();
			foreach ( var s in byGenre[g] )
			{
				forms.Add( s.Form );
				bars.TryGetValue( s.Bars, out int c );
				bars[s.Bars] = c + 1;
			}
			var hist = new List<string>();
			foreach ( var kv in bars ) hist.Add( $"{kv.Key}:{Pct( kv.Value, byGenre[g].Length )}" );
			Console.WriteLine( $"  {Name( g ),-9} {forms.Count,4} forms   bars {string.Join( " ", hist )}" );
		}
		Console.WriteLine();
	}

	// ── cohesion: do the parts know about each other ─────────────────────────────────────────

	// A→B is the fraction of A's onsets that land on one of B's. It is deliberately not symmetric:
	// "the bass plays where the kick plays" and "the kick plays where the bass plays" are different
	// claims, and the first is the one that says whether the two are locked.
	//
	// THIS IS THE NUMBER THE ARRANGER IS JUDGED ON FROM BOTH SIDES. Parts that ignore each other is
	// today's defect; parts that all play the same rhythm is worse, and it will not read as a
	// regression on any single listen. So the matrix must not collapse toward the diagonal.
	static void Cohesion( Song[][] byGenre )
	{
		Console.WriteLine( "── cohesion: bass on kick, comp on snare (% of A's onsets that land on a B) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
			Console.WriteLine( $"  {Name( g ),-9} bass->kick {Agree( byGenre[g], TraceVoice.Bass, TraceVoice.Kick ),5}"
				+ $"   comp->snare {Agree( byGenre[g], TraceVoice.Comp, TraceVoice.Snare ),5}"
				+ $"   bass->comp {Agree( byGenre[g], TraceVoice.Bass, TraceVoice.Comp ),5}" );
		Console.WriteLine();

		Console.WriteLine( "── the full pairwise agreement matrix (rows = A, cols = B) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			Console.WriteLine( $"  {Name( g )}" );
			Console.Write( "            " );
			foreach ( var b in AllVoices ) Console.Write( $"{b,8}" );
			Console.WriteLine();
			foreach ( var a in AllVoices )
			{
				Console.Write( $"    {a,-8}" );
				foreach ( var b in AllVoices )
					Console.Write( a == b ? $"{"—",8}" : $"{Agree( byGenre[g], a, b ),8}" );
				Console.WriteLine();
			}
		}
		Console.WriteLine();
	}

	/// <summary>Fraction of A's onsets that coincide with one of B's, averaged over the songs that
	/// played both. A song where either voice is silent is not counted — a genre with no keys would
	/// otherwise read as 0% agreement rather than as "no such voice".</summary>
	static string Agree( Song[] songs, TraceVoice a, TraceVoice b )
	{
		int ia = Array.IndexOf( AllVoices, a ), ib = Array.IndexOf( AllVoices, b );
		double sum = 0; int songsCounted = 0;
		foreach ( var s in songs )
		{
			var A = s.Onsets[ia]; var B = s.Onsets[ib];
			if ( A.Count == 0 || B.Count == 0 ) continue;
			int hit = 0;
			foreach ( int t in A ) if ( B.Contains( t ) ) hit++;
			sum += hit / (double)A.Count;
			songsCounted++;
		}
		return songsCounted == 0 ? "—" : $"{sum / songsCounted * 100:0}%";
	}

	// ── the tune: 500 states, all of them the same KIND of state ─────────────────────────────

	static void TuneShape( Song[][] byGenre )
	{
		Console.WriteLine( "── chorus tune shape ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var songs = byGenre[g];
			int notes = 0, offGrid = 0, pinned = 0, repeated = 0, callAnswer = 0, tunes = 0, tonic = 0;
			var lens = new SortedDictionary<int, int>();
			var firsts = new SortedDictionary<int, int>();
			var leaps = new SortedDictionary<int, int>();
			var rhythms = new HashSet<string>();
			var contours = new HashSet<string>();
			var whole = new HashSet<string>();

			foreach ( var s in songs )
			{
				if ( s.TuneTicks == null || s.TuneTicks.Length == 0 ) continue;
				tunes++;
				int half = s.TuneTicks.Length / 2;
				var rhythm = new StringBuilder();
				var contour = new StringBuilder();

				for ( int i = 0; i < s.TuneTicks.Length; i++ )
				{
					notes++;
					if ( s.TuneTicks[i] % Timing.TicksPerEighth != 0 ) offGrid++;
					Bump( lens, s.TuneSpans[i] );
					int d = s.TuneDegrees[i];
					if ( d <= Melody.DegreeMin || d >= Melody.DegreeMax ) pinned++;
					if ( i > 0 && d == s.TuneDegrees[i - 1] ) repeated++;
					if ( i > 0 && i < half ) Bump( leaps, Math.Abs( d - s.TuneDegrees[i - 1] ) );
					if ( i < half ) { rhythm.Append( s.TuneTicks[i] ).Append( ',' ); contour.Append( d - s.TuneDegrees[0] ).Append( ',' ); }
				}
				Bump( firsts, s.TuneDegrees[0] );
				if ( s.TuneDegrees[^1] == 0 ) tonic++;
				rhythms.Add( rhythm.ToString() );
				contours.Add( contour.ToString() );
				whole.Add( rhythm + "/" + contour );

				// Is the answer literally the call transposed down one degree? The last note is
				// exempt — it is forced to the tonic, and that IS the tune landing rather than a
				// derivation.
				bool derived = half > 1;
				for ( int i = 0; i < half - 1 && derived; i++ )
					derived = s.TuneDegrees[half + i] == Math.Clamp( s.TuneDegrees[i] - 1, Melody.DegreeMin, Melody.DegreeMax );
				if ( derived ) callAnswer++;
			}

			if ( tunes == 0 ) { Console.WriteLine( $"  {Name( g ),-9} no tune" ); continue; }
			Console.WriteLine( $"  {Name( g )}  ({tunes} tunes, {notes / (double)tunes:0.0} notes each)" );
			Console.WriteLine( $"    off the 8th grid  {Pct( offGrid, notes )}"
				+ $"   pinned at the range ends {Pct( pinned, notes )}"
				+ $"   repeated adjacent {Pct( repeated, notes )}" );
			Console.WriteLine( $"    last note tonic   {Pct( tonic, tunes )}"
				+ $"   answer = call-1 {Pct( callAnswer, tunes )}" );
			Console.WriteLine( $"    note lengths      {Hist( lens, notes )}" );
			Console.WriteLine( $"    first degree      {Hist( firsts, tunes )}" );
			Console.WriteLine( $"    step sizes        {Hist( leaps, Sum( leaps ) )}" );
			Console.WriteLine( $"    distinct          {rhythms.Count} rhythms, {contours.Count} contours, {whole.Count} tunes" );
		}
		Console.WriteLine();
	}

	// ── two genres, one melody ───────────────────────────────────────────────────────────────

	// The song stream has no genre in it either, and that is DELIBERATE and stays: the same song in
	// two genres is a thing the toy can do. The tune is the one place the sharing reads as a defect
	// rather than as a trick, because a melody is what a listener identifies a song by.
	static void CrossGenre( Song[][] byGenre )
	{
		Console.WriteLine( "── cross-genre tune collisions (same n, two genres, identical chorus tune) ──" );
		for ( int a = 0; a < byGenre.Length; a++ )
			for ( int b = a + 1; b < byGenre.Length; b++ )
			{
				int same = 0, pairs = 0; double overlap = 0;
				for ( int i = 0; i < byGenre[a].Length && i < byGenre[b].Length; i++ )
				{
					var x = byGenre[a][i]; var y = byGenre[b][i];
					if ( x.TuneTicks == null || y.TuneTicks == null ) continue;
					pairs++;
					if ( Same( x.TuneTicks, y.TuneTicks ) && Same( x.TuneDegrees, y.TuneDegrees ) ) same++;
					// Onset overlap, which is the OTHER half of the check: two genres whose tunes
					// stopped being identical but still land in all the same places have not
					// diverged, they have been renamed.
					var set = new HashSet<int>( y.TuneTicks );
					int hit = 0;
					foreach ( int t in x.TuneTicks ) if ( set.Contains( t ) ) hit++;
					overlap += hit / (double)x.TuneTicks.Length;
				}
				if ( pairs == 0 ) continue;
				Console.WriteLine( $"  {Name( a ),-9} <-> {Name( b ),-9} identical {Pct( same, pairs ),5}"
					+ $"   onset overlap {overlap / pairs * 100:0}%" );
			}
		Console.WriteLine();
	}

	// ── plumbing ─────────────────────────────────────────────────────────────────────────────

	static string Name( int g ) => VibeCodec.Genres[g];

	// Reference identity: the figures are entries in a static table, so two songs that drew the
	// same figure hold the same object. Comparing cells instead would merge two authored figures
	// that happen to be equal, which is not what "how many states" is asking.
	static int Id( Pattern p ) => p == null ? -1 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( p );

	static void Bump( SortedDictionary<int, int> d, int k ) { d.TryGetValue( k, out int c ); d[k] = c + 1; }

	static int Sum( SortedDictionary<int, int> d ) { int t = 0; foreach ( var kv in d ) t += kv.Value; return t; }

	static string Pct( int a, int b ) => b == 0 ? "—" : $"{a * 100.0 / b:0}%";

	static string Hist( SortedDictionary<int, int> d, int total )
	{
		var parts = new List<string>();
		foreach ( var kv in d ) parts.Add( $"{kv.Key}:{Pct( kv.Value, total )}" );
		return string.Join( "  ", parts );
	}

	static bool Same( int[] a, int[] b )
	{
		if ( a.Length != b.Length ) return false;
		for ( int i = 0; i < a.Length; i++ ) if ( a[i] != b[i] ) return false;
		return true;
	}
}
