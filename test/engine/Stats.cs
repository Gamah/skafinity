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
		public string Played;               // what the rhythm section ACTUALLY played, as ticks
		public int[] TuneTicks, TuneDegrees, TuneSpans;   // the CHORUS tune, as authored
		public int TuneLength;

		// ── the kit ──
		public string[] SectionKits;        // one PLANNED content signature per section
		public string[] PlayedKits;         // and one for what the section actually played
		public int[] KickOcc, SnareOcc, CymOcc;   // hits per sixteenth position of the bar
		public int KitBars;                 // bars the occupancy was folded over
		public int StruckSnares, PlayedSnares;
		public bool KitLeads;               // did the band write to the kit, or the kit to the band
	}

	/// <summary>How many bars a kit signature folds over. Long enough for every groove in the
	/// tables (metal's double-kick figure is four bars) — a shorter window would read a four-bar
	/// pattern as four different bars and a longer one would call two sections different because
	/// they are different lengths.</summary>
	const int KitFoldBars = 4;

	/// <summary>Sixteenth positions in a 4/4 bar. The occupancy grid is the shape the corpus was
	/// read in (see <see cref="DrumGroove"/>'s header), so these columns are directly comparable to
	/// the numbers quoted there.</summary>
	const int BarCells = 16;

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
		KitStates( byGenre );
		KitIdentity( byGenre );
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

		// What the rhythm section actually PLAYED, over the whole song, as onset ticks. The figure
		// triple below it is the table ceiling; this is the realised part, and the two stopped being
		// the same thing the moment a section could work on its figure rather than quote it.
		var played = new StringBuilder();
		foreach ( var v in new[] { TraceVoice.Bass, TraceVoice.Comp, TraceVoice.Keys } )
		{
			foreach ( int t in trace.Of( v ) ) played.Append( t ).Append( ',' );
			played.Append( '|' );
		}

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
			Form = form.ToString(), Bars = bars, Onsets = onsets, Played = played.ToString(),
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
		s.KitLeads = g.KitLeads;
		MeasureKit( trace, s );
		return s;
	}

	/// <summary>The kit, per section and per metric position.
	///
	/// The section boundaries come off the trace rather than off a second walk of the form (see
	/// <see cref="PlanTrace.SectionSpan"/>), so what is folded here is the bars the composer
	/// actually laid out — including a drawn verse length and a truncated last verse.</summary>
	static void MeasureKit( PlanTrace trace, Song s )
	{
		var kick = trace.Of( TraceVoice.Kick );
		var snare = trace.Of( TraceVoice.Snare );
		var cym = trace.Of( TraceVoice.Cymbal );

		s.KickOcc = new int[BarCells];
		s.SnareOcc = new int[BarCells];
		s.CymOcc = new int[BarCells];
		s.SectionKits = new string[trace.Sections.Count];
		s.PlayedKits = new string[trace.Sections.Count];

		// One pass per drum per section is O(sections x onsets); the lists are a few hundred long
		// and the sweep is render-bound, so a sorted walk would buy nothing legible.
		for ( int i = 0; i < trace.Sections.Count; i++ )
		{
			var sec = trace.Sections[i];
			int fold = sec.BarTicks * KitFoldBars;
			var sb = new StringBuilder();
			foreach ( var (v, occ) in new[] { (kick, s.KickOcc), (snare, s.SnareOcc), (cym, s.CymOcc) } )
			{
				var cells = new SortedSet<int>();
				foreach ( int t in v )
				{
					if ( t < sec.Tick || t >= sec.Tick + sec.Ticks ) continue;
					cells.Add( (t - sec.Tick) % fold );
					int c = ((t - sec.Tick) % sec.BarTicks) * BarCells / sec.BarTicks;
					if ( c >= 0 && c < BarCells ) occ[c]++;
				}
				foreach ( int c in cells ) sb.Append( c ).Append( ',' );
				sb.Append( '|' );
			}
			s.PlayedKits[i] = sb.ToString();
			// The kit's PLAN, as the composer handed it to the bar loop. Separate from the line
			// above because the two answer different questions and only the first is what this
			// branch changes: the ghost roll and the crash roll are per-bar dice on top of a
			// pattern, so "as played" varies from song to song however fixed the pattern is.
			s.SectionKits[i] = Sig( sec.Kick ) + "|" + Sig( sec.Snare ) + "|" + Sig( sec.Cymbal );
			s.KitBars += Math.Max( 1, sec.Ticks / sec.BarTicks );
		}

		// A struck snare is the backbeat; a ghost is the density around it. Counting them apart is
		// what makes punk's "very nearly every eighth" legible as a tell rather than as a genre
		// that simply plays a lot of snare.
		s.PlayedSnares = snare.Count;
		s.StruckSnares = trace.Struck( TraceVoice.Snare );
	}

	// ── how many different rhythm sections a genre can produce at all ────────────────────────

	// The three draws are the song's identity and they come out of tables, so this is a TABLE-SIZE
	// ceiling: no amount of seed randomness reaches past |CompFigures| x |BassPatterns| x |Grooves|.
	// One punk song in nine repeating the previous one's whole rhythm section is that ceiling, not
	// a draw that happened to collide.
	static void RhythmSectionStates( Song[][] byGenre )
	{
		// THE SONG'S OWN RHYTHM SECTION — what its choruses play, which is most of what a listener
		// hears as the song. Hashed on CONTENT rather than on object identity: identity answered
		// this exactly while a figure could only ever be an entry in a table, and stopped meaning
		// anything the moment a song could arrange one, since every arranged figure is a fresh
		// object whether or not it differs from the last.
		//
		// The table ceiling below is the number this is measured against: three comp figures times
		// five bass patterns times two grooves is thirty songs before punk repeats itself, and no
		// amount of seed randomness reaches past that. It is still printed because it is still the
		// ceiling — the gap between the two lines is what the arranger bought.
		Console.WriteLine( "── distinct rhythm sections (the song's own — chorus comp x keys x bass x groove) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var seen = new HashSet<string>();
			foreach ( var s in byGenre[g] )
				seen.Add( $"{Sig( s.Comp )}|{Sig( s.Keys )}|{Sig( s.Bass )}|{s.Groove.Name}" );
			Console.WriteLine( $"  {Name( g ),-9} {seen.Count,4} of {byGenre[g].Length} songs" );
		}
		Console.WriteLine();

		Console.WriteLine( "── the authored table ceiling (comp x keys x bass x groove) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var prof = GenreProfile.For( g );
			int keys = Math.Max( 1, prof.KeysFigures?.Length ?? 1 );
			int ceiling = prof.CompFigures.Length * keys * prof.BassPatterns.Length * prof.Grooves.Length;
			Console.WriteLine( $"  {Name( g ),-9} {ceiling,4}   ("
				+ $"{prof.CompFigures.Length} comp x {keys} keys x {prof.BassPatterns.Length} bass"
				+ $" x {prof.Grooves.Length} groove)" );
		}
		Console.WriteLine();
	}

	// ── the kit ──────────────────────────────────────────────────────────────────────────────

	/// <summary>How many kits a genre has, and how many a SONG has. The second is the one that was
	/// 1 for every genre in the engine: the groove was drawn once per song and never re-drawn, so
	/// every bar of every section played the identical kick, snare and cymbal.
	///
	/// Hashed on CONTENT, per section, for the reason the rhythm-section count is: object identity
	/// answers "which table entry is this" and nothing else, and stops meaning anything the moment
	/// a section can work on the pattern it drew.</summary>
	static void KitStates( Song[][] byGenre )
	{
		// TWO LINES, AND THE FIRST IS THE ONE THAT MATTERS. "Planned" is the kick/snare/cymbal
		// patterns the composer handed the bar loop; "played" is the onsets that came out, which
		// carry the per-bar ghost roll and the fill handover on top. The played line was already
		// well above 1 per song before any of this and says nothing about whether the kit varies —
		// a stochastic ghost is not a state.
		Console.WriteLine( "── distinct kits (kick x snare x cymbal, hashed on content) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var planned = new HashSet<string>(); var played = new HashSet<string>();
			double perSong = 0, perSongPlayed = 0, sections = 0;
			foreach ( var s in byGenre[g] )
			{
				var inSong = new HashSet<string>(); var inSongPlayed = new HashSet<string>();
				foreach ( var k in s.SectionKits ) { planned.Add( k ); inSong.Add( k ); }
				foreach ( var k in s.PlayedKits ) { played.Add( k ); inSongPlayed.Add( k ); }
				perSong += inSong.Count; perSongPlayed += inSongPlayed.Count;
				sections += s.SectionKits.Length;
			}
			int n = byGenre[g].Length;
			Console.WriteLine( $"  {Name( g ),-9} planned {planned.Count,5} over the sweep,"
				+ $" {perSong / n,5:0.00} per song"
				+ $"   played {played.Count,5} / {perSongPlayed / n:0.00}"
				+ $"   (of {sections / n:0.0} sections)" );
		}
		Console.WriteLine();
	}

	/// <summary>
	/// THE TRIPWIRE. A drum figure carries a genre's identity in specific measured positions, and
	/// the pass that fitted the tables named the three that had been wrong (see
	/// <see cref="DrumGroove"/>'s header). If arranging the kit erodes them, six genres converge on
	/// one drummer — and that will not read as a regression on any single listen, it will read as
	/// "the drums got more interesting".
	///
	/// Printed for every genre rather than only for the genre each tell was measured in, because a
	/// tell is only legible against the genres that do NOT have it: country's hat on the "and" says
	/// nothing until you can see that rock's is on the beat.
	/// </summary>
	static void KitIdentity( Song[][] byGenre )
	{
		Console.WriteLine( "── kit identity tells (the three the corpus pass corrected) ──" );
		Console.WriteLine( "    cym beat / cym &  : country's hi-hat is on the OFFBEAT eighth (~84% vs ~36%)" );
		Console.WriteLine( "    kick &1&3         : rock's kick spends far more of its bar there than a backbeat" );
		Console.WriteLine( "    snare/bar (struck): punk strikes 2 and 4 and ghosts very nearly every eighth" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			double bars = 0, cymBeat = 0, cymAnd = 0, kickPush = 0, snare = 0, struck = 0;
			foreach ( var s in byGenre[g] )
			{
				bars += s.KitBars;
				for ( int c = 0; c < BarCells; c++ )
				{
					if ( c % 4 == 0 ) cymBeat += s.CymOcc[c];
					else if ( c % 4 == 2 ) cymAnd += s.CymOcc[c];
				}
				kickPush += s.KickOcc[2] + s.KickOcc[10];
				snare += s.PlayedSnares;
				struck += s.StruckSnares;
			}
			// Per BEAT for the cymbal (four beats and four "&"s to a bar), per BAR for the rest.
			Console.WriteLine( $"  {Name( g ),-9} cym beat {cymBeat / (bars * 4) * 100,4:0}%"
				+ $"   cym & {cymAnd / (bars * 4) * 100,4:0}%"
				+ $"   kick &1&3 {kickPush / (bars * 2) * 100,4:0}%"
				+ $"   snare/bar {snare / bars,4:0.0} ({struck / bars:0.0} struck)" );
		}
		Console.WriteLine();

		// The shape the corpus was read in, so these rows sit next to the numbers DrumGroove's
		// header quotes and "did the genre survive" is read rather than argued.
		Console.WriteLine( "── per-position occupancy: fraction of bars carrying that drum there ──" );
		Console.WriteLine( "               1  e  &  a  2  e  &  a  3  e  &  a  4  e  &  a" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			double bars = 0;
			foreach ( var s in byGenre[g] ) bars += s.KitBars;
			Console.WriteLine( $"  {Name( g )}" );
			foreach ( var (label, pick) in new (string, Func<Song, int[]>)[]
				{ ("kick", s => s.KickOcc), ("snare", s => s.SnareOcc), ("cym", s => s.CymOcc) } )
			{
				var row = new int[BarCells];
				foreach ( var s in byGenre[g] )
				{
					var occ = pick( s );
					for ( int c = 0; c < BarCells; c++ ) row[c] += occ[c];
				}
				Console.Write( $"    {label,-8}" );
				for ( int c = 0; c < BarCells; c++ ) Console.Write( $"{row[c] / bars * 100,3:0}" );
				Console.WriteLine();
			}
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
		// BOTH DIRECTIONS, because the kit can now be arranged either before the band or after it.
		// "the bass plays where the kick plays" is a band writing to a beat; "the kick plays where
		// the bass plays" is a drummer playing to the riff. One number cannot say which happened,
		// and a sweep that averages the two describes neither.
		Console.WriteLine( "── cohesion: bass on kick, comp on snare (% of A's onsets that land on a B) ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
			Console.WriteLine( $"  {Name( g ),-9} bass->kick {Agree( byGenre[g], TraceVoice.Bass, TraceVoice.Kick ),5}"
				+ $"   kick->bass {Agree( byGenre[g], TraceVoice.Kick, TraceVoice.Bass ),5}"
				+ $"   comp->snare {Agree( byGenre[g], TraceVoice.Comp, TraceVoice.Snare ),5}"
				+ $"   snare->comp {Agree( byGenre[g], TraceVoice.Snare, TraceVoice.Comp ),5}"
				+ $"   bass->comp {Agree( byGenre[g], TraceVoice.Bass, TraceVoice.Comp ),5}" );
		Console.WriteLine();

		// SPLIT BY WHICH MODE THE SONG DREW, because the two are not one number measured twice.
		// Under "kit leads" cohesion is the band writing to a beat; under "kit follows" it is the
		// drummer writing to the riff. Averaging them describes neither, and a shift in the mix of
		// the two would read as a change in cohesion that never happened.
		Console.WriteLine( "── the same, split by who wrote first ──" );
		for ( int g = 0; g < byGenre.Length; g++ )
		{
			var leads = new List<Song>(); var follows = new List<Song>();
			foreach ( var s in byGenre[g] ) (s.KitLeads ? leads : follows).Add( s );
			foreach ( var (label, set) in new[] { ("kit leads ", leads), ("kit follows", follows) } )
			{
				if ( set.Count == 0 ) { Console.WriteLine( $"  {Name( g ),-9} {label}  —" ); continue; }
				var songs = set.ToArray();
				Console.WriteLine( $"  {Name( g ),-9} {label} {Pct( set.Count, byGenre[g].Length ),4}"
					+ $"   bass->kick {Agree( songs, TraceVoice.Bass, TraceVoice.Kick ),5}"
					+ $"   kick->bass {Agree( songs, TraceVoice.Kick, TraceVoice.Bass ),5}"
					+ $"   comp->snare {Agree( songs, TraceVoice.Comp, TraceVoice.Snare ),5}"
					+ $"   snare->comp {Agree( songs, TraceVoice.Snare, TraceVoice.Comp ),5}" );
			}
		}
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

	/// <summary>A figure's CONTENT — its onsets and what each one plays. Two songs whose comps are
	/// the same rhythm hash the same however they got there, which is the only reading of "how many
	/// distinct rhythm sections" that survives figures being arranged rather than drawn.</summary>
	static string Sig( Pattern p )
	{
		if ( p == null ) return "-";
		var sb = new StringBuilder().Append( p.LengthTicks ).Append( ':' );
		for ( int i = 0; i < p.Count; i++ ) sb.Append( p.TickAt( i ) ).Append( '/' ).Append( p.ValueAt( i ) ).Append( ',' );
		return sb.ToString();
	}

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
