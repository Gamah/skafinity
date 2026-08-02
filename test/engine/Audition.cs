using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skafinity.EngineTests;

/// <summary>
/// THE AUDITION — the kit's voices played, one part per line, so a tuning is chosen by listening
/// rather than by argument. See DRUMS.md.
///
/// Each line is a short musical figure — about a bar plus the beat it lands on — played by the
/// voice under audition and NOTHING ELSE. No supporting kit, no click. A voice is judged in
/// motion: how a fill moves across the toms, how an open hat sits against the closed ones around
/// it, how a double-kick run holds up at sixteenths, how a ghost note reads under a backbeat.
/// Isolated hits only where the point genuinely is the single hit, and even then it repeats.
///
/// DRY: MusicGen.ForAudition neutralises the tone lean and the genre mix, there is no swing and
/// no kit push, and the master bus is not run at all — so no reverb, no soft-clip, and above all
/// no peak normalize. ONE gain is applied across the whole file at the end, which is what keeps
/// the velocity and balance lines meaning anything: normalizing per line would return every
/// candidate at the same level.
///
/// NO BASELINE. Nothing here replays "today's kit" for comparison as such — where a default is
/// heard it is because it is a candidate in its own right. This is about approving the kit going
/// forward.
/// </summary>
static class Audition
{
	const int Rate = 44100;
	const double GapSec = 0.20;      // silence between lines, so they stay countable
	const double TailSec = 0.30;     // ring-out after the figure's last hit
	const double CymbalTail = 1.30;  // a crash is mostly tail, so those lines get their own
	const float FilePeak = 0.89f;    // the one gain the whole file gets

	/// <summary>One line of the script: a figure, and the text that names it.</summary>
	sealed class Line
	{
		public string Voice;
		public string Text;
		public int Bpm;
		public double Beats;          // length of the figure, landing beat included
		public double Tail = TailSec; // ring-out; a cymbal line needs most of a bar of it
		public Action<Take> Play;
	}

	/// <summary>The instance one line renders into, plus the two things a figure needs: where a
	/// beat is, and how long a millisecond is. A gesture measured in MILLISECONDS is physical and
	/// must not scale with tempo; a position in BEATS is musical and must.</summary>
	sealed class Take
	{
		public readonly MusicGen G;
		public readonly Rng N;
		public readonly int Bpm;

		public Take( MusicGen g, int bpm ) { G = g; N = new Rng( "audition" ); Bpm = bpm; }

		/// <summary>Sample position of a beat offset from the figure's start.</summary>
		public int At( double beats ) => G.AuditionTiming.TickToSample( beats * Timing.TicksPerBeat );

		public int Ms( double ms ) => (int)(Rate * ms * 0.001);

		/// <summary>Samples in a beat — for the gestures that span one (a buzz roll, a swell).</summary>
		public int Beat => At( 1 ) - At( 0 );
	}

	// ── Entry point ──

	public static void Run( string only, string wavPath, string txtPath )
	{
		var lines = new List<Line>();
		// The default run is what is still OPEN. Everything but the ride has passed, so the
		// default is the ride — the rest stay reachable by name, because "approved" is not
		// "frozen".
		Ride( lines );
		if ( !string.IsNullOrEmpty( only ) ) { Kick( lines ); Snare( lines ); Toms( lines ); Crash( lines ); Hats( lines ); }

		if ( !string.IsNullOrEmpty( only ) )
			lines = lines.FindAll( l => l.Voice.Equals( only, StringComparison.OrdinalIgnoreCase ) );
		if ( lines.Count == 0 )
		{
			Console.WriteLine( $"no audition lines for '{only}' — try kick|snare|toms|hats|crash|ride" );
			return;
		}

		var L = new List<float>();
		var R = new List<float>();
		var script = new StringBuilder();
		script.AppendLine( "AUDITION — skafinity drum kit, round 7: THE RIDE AS PURE WAVEFORMS" );
		script.AppendLine( "One kit part per line. Each line is a figure played by that voice alone." );
		script.AppendLine( "Dry: no tone lean, no genre mix, no reverb, no master. Centred except where noted." );
		script.AppendLine( "Round 7 abandons filtered noise entirely: the cymbal is a bank of ~80 inharmonic" );
		script.AppendLine( "sine partials, each with its own ring time, many twinned a few Hz apart so they" );
		script.AppendLine( "beat. Every hit draws fresh phases and level jitter, and soft strokes darken." );
		script.AppendLine( "The only noise left is the 4 ms stick contact. Bows first, then bells alone," );
		script.AppendLine( "then bell-and-bow together, then the patterns." );
		script.AppendLine( "--audition kick|snare|toms|hats|crash still plays the settled voices." );
		script.AppendLine();

		int gap = (int)(Rate * GapSec);
		string voice = null;
		for ( int i = 0; i < lines.Count; i++ )
		{
			var line = lines[i];
			double seconds = line.Beats * 60.0 / line.Bpm + line.Tail;
			var g = MusicGen.ForAudition( Cfg(), seconds, line.Bpm );
			var take = new Take( g, line.Bpm );
			line.Play( take );

			var (bl, br) = g.AuditionBuffers();
			if ( voice != line.Voice )
			{
				voice = line.Voice;
				script.AppendLine();
				script.AppendLine( $"── {voice.ToUpperInvariant()} ──" );
			}
			script.AppendLine( $"{i + 1,3}. [{Stamp( L.Count )}] {line.Text}" );
			L.AddRange( bl ); R.AddRange( br );
			for ( int s = 0; s < gap; s++ ) { L.Add( 0f ); R.Add( 0f ); }
		}

		script.AppendLine();
		script.AppendLine( $"{lines.Count} lines, {Stamp( L.Count )} total." );

		Write( wavPath, txtPath, L, R, script.ToString() );
	}

	static MusicGen.Config Cfg() => new() { SampleRate = Rate, Genre = 1 };

	static string Stamp( int samples )
	{
		int total = samples / Rate;
		return $"{total / 60}:{total % 60:00}";
	}

	static void Write( string wavPath, string txtPath, List<float> L, List<float> R, string script )
	{
		// ONE gain over the whole file. Every line's level relative to every other line's is a
		// thing being auditioned, so this is the only place a gain may be applied.
		float peak = 0f;
		for ( int i = 0; i < L.Count; i++ )
			peak = Math.Max( peak, Math.Max( Math.Abs( L[i] ), Math.Abs( R[i] ) ) );
		float k = peak > 0f ? FilePeak / peak : 1f;

		var pcm = new short[L.Count * 2];
		for ( int i = 0; i < L.Count; i++ )
		{
			pcm[i * 2] = (short)Math.Clamp( (int)MathF.Round( L[i] * k * 32767f ), -32768, 32767 );
			pcm[i * 2 + 1] = (short)Math.Clamp( (int)MathF.Round( R[i] * k * 32767f ), -32768, 32767 );
		}
		File.WriteAllBytes( wavPath, MusicGen.WavFromSamples( pcm, 2, Rate ) );
		File.WriteAllText( txtPath, script );

		Console.Write( script );
		Console.WriteLine();
		Console.WriteLine( $"wav  {wavPath}  ({new FileInfo( wavPath ).Length / (1024 * 1024)} MiB, "
			+ $"{Stamp( L.Count )}, {Rate} Hz stereo, one gain of {k:0.000} over the file)" );
		Console.WriteLine( $"txt  {txtPath}" );
	}

	static void Add( List<Line> into, string voice, string text, int bpm, double beats,
		Action<Take> play, double tail = TailSec )
		=> into.Add( new Line { Voice = voice, Text = text, Bpm = bpm, Beats = beats, Play = play,
			Tail = tail } );

	// ── KICK ──
	// Bodies over a four-on-the-floor, then the things a four-on-the-floor cannot answer: the
	// machine-gun run, the double pedal, and velocity.

	static void Kick( List<Line> into )
	{
		// THE CLICK IS 3 ms OF FULL-BAND WHITE NOISE. That is why every body carried the same high
		// tick: it is not part of any of them, it is the same broadband transient laid over all of
		// them. A beater is a soft mass on a skin and cannot radiate 15 kHz; a low-pass is the fix,
		// and where the corner goes is the only real question.
		void Four( Take t, KickTone k )
		{
			for ( int b = 0; b < 4; b++ ) t.G.RenderKick( t.At( b ), t.N, 1f, k, 0f );
			t.G.RenderKick( t.At( 4 ), t.N, 1f, k, 0f );
		}
		void Run16( Take t, KickTone k )
		{
			for ( int i = 0; i < 16; i++ ) t.G.RenderKick( t.At( i * 0.25 ), t.N, 1f, k, 0f );
			t.G.RenderKick( t.At( 4 ), t.N, 1f, k, 0f );
		}
		var b = KickTone.Default;

		Add( into, "kick", "CLICK — none at all, four on the floor", 124, 5,
			t => Four( t, b.With( clickLevel: 0f ) ) );
		Add( into, "kick", "CLICK — low-passed at 1.8 kHz", 124, 5,
			t => Four( t, b.With( clickCut: 1800f ) ) );
		Add( into, "kick", "CLICK — low-passed at 3.5 kHz", 124, 5,
			t => Four( t, b.With( clickCut: 3500f ) ) );
		Add( into, "kick", "CLICK — low-passed at 6 kHz", 124, 5,
			t => Four( t, b.With( clickCut: 6000f ) ) );
		Add( into, "kick", "CLICK — 3.5 kHz and longer (7 ms), a thump rather than a tick", 124, 5,
			t => Four( t, b.With( clickCut: 3500f, clickSec: 0.007f, clickLevel: 0.75f ) ) );
		Add( into, "kick", "CLICK — 3.5 kHz, sixteenth run (does the attack still cut through)",
			132, 5, t => Run16( t, b.With( clickCut: 3500f, jitter: 0.35f ) ) );
	}

	// ── SNARE ──
	// Everything over a backbeat with ghost notes, because the ghost and the hit are only worth
	// anything against each other.

	static void Snare( List<Line> into )
	{
		void Backbeat( Take t, SnareTone hit )
		{
			for ( int i = 0; i < 8; i++ )
			{
				double at = i * 0.5;
				if ( i == 2 || i == 6 ) t.G.RenderSnare( t.At( at ), t.N, 1f, hit );
				else if ( i != 0 ) t.G.RenderSnare( t.At( at ), t.N, 1f, SnareTone.Ghost );
			}
			t.G.RenderSnare( t.At( 4 ), t.N, 1f, hit );
		}
		// A cross-stick is a verse figure, so it is heard as one.
		void HalfTime( Take t, SnareTone k )
		{
			t.G.RenderSnare( t.At( 1.5 ), t.N, 1f, SnareTone.Ghost );
			t.G.RenderSnare( t.At( 2 ), t.N, 1f, k );
			t.G.RenderSnare( t.At( 3.5 ), t.N, 1f, SnareTone.Ghost );
			t.G.RenderSnare( t.At( 4 ), t.N, 1f, k );
		}

		// The ring was the shell partials: two loud sines with a slow decay ARE a tom, whatever
		// the label says. The crack carries the articulation now and the shell is nearly gone.
		Add( into, "snare", "RIMSHOT — crack-led, shell pulled right back", 108, 5,
			t => Backbeat( t, SnareTone.Rimshot ) );
		Add( into, "snare", "RIMSHOT — brighter crack (4.2 kHz)", 108, 5,
			t => Backbeat( t, SnareTone.Rimshot.With( crackHz: 4200f ) ) );
		Add( into, "snare", "RIMSHOT — darker crack (2.4 kHz), a touch more shell", 108, 5,
			t => Backbeat( t, SnareTone.Rimshot.With( crackHz: 2400f, bodyLevel: 0.20f ) ) );
		Add( into, "snare", "RIMSHOT — crack only, no shell at all", 108, 5,
			t => Backbeat( t, SnareTone.Rimshot.With( bodyLevel: 0f ) ) );

		// The clave was the RING, not the pitch: bright, tonal and undamped. A hand holds this
		// stick against the head, so it knocks and stops.
		Add( into, "snare", "CROSS-STICK — woody knock, damped", 96, 5,
			t => HalfTime( t, SnareTone.CrossStick ) );
		Add( into, "snare", "CROSS-STICK — lower and thicker (more thud, less crack)", 96, 5,
			t => HalfTime( t, SnareTone.CrossStick.With( crackHz: 1350f, thudLevel: 0.5f,
				crackLevel: 0.5f ) ) );
		Add( into, "snare", "CROSS-STICK — drier and shorter still", 96, 5,
			t => HalfTime( t, SnareTone.CrossStick.With( dur: 0.038f, decayFrac: 0.07,
				bodyLevel: 0.16f ) ) );
	}

	// ── TOMS ──
	// The biggest change, so it is auditioned hardest. Every line is a fill or a groove: a tom is
	// a kit piece and what is being judged is how the kit MOVES.

	static void Toms( List<Line> into )
	{
		const int RootC = 48, RootF = 53, RootA = 45;

		// High to low across the three drums, landing on the floor tom's downbeat.
		void Down( Take t, TomKit kit, TomTone tone )
		{
			int[] idx = { 0, 0, 1, 1, 2, 1, 2, 2 };
			for ( int i = 0; i < 8; i++ )
				t.G.RenderTom( t.At( i * 0.5 ), kit, idx[i], t.N, 0.78f + 0.22f * (i / 7f), tone );
			t.G.RenderTom( t.At( 4 ), kit, 2, t.N, 1f, tone );
		}
		void Up( Take t, TomKit kit, TomTone tone )
		{
			int[] idx = { 2, 2, 1, 1, 0, 1, 0, 0 };
			for ( int i = 0; i < 8; i++ )
				t.G.RenderTom( t.At( i * 0.5 ), kit, idx[i], t.N, 0.78f + 0.22f * (i / 7f), tone );
			t.G.RenderTom( t.At( 4 ), kit, 0, t.N, 1f, tone );
		}
		// A tom GROOVE: the floor tom keeps the pulse and the rack answers it.
		void Groove( Take t, TomKit kit, TomTone tone )
		{
			for ( int b = 0; b < 4; b++ )
			{
				t.G.RenderTom( t.At( b ), kit, 2, t.N, 1f, tone );
				t.G.RenderTom( t.At( b + 0.5 ), kit, b % 2 == 0 ? 0 : 1, t.N, 0.55f, tone );
			}
			t.G.RenderTom( t.At( 4 ), kit, 2, t.N, 1f, tone );
		}

		var fourths = TomKit.Tuned( TomTune.Fourths, RootC );
		var wide = TomKit.Tuned( TomTune.Wide, RootC );
		var thirds = TomKit.Tuned( TomTune.Thirds, RootC );
		var fixedKit = TomKit.Tuned( TomTune.Fixed, RootC );
		var tone = TomTone.Default;

		Add( into, "toms", "TUNING — stacked fourths, descending fill", 112, 5,
			t => Down( t, fourths, tone ) );
		Add( into, "toms", "TUNING — wide (fifths), descending fill", 112, 5,
			t => Down( t, wide, tone ) );
		Add( into, "toms", "TUNING — stacked thirds, descending fill", 112, 5,
			t => Down( t, thirds, tone ) );
		Add( into, "toms", "TUNING — fixed physical set, ignores the key, descending fill", 112, 5,
			t => Down( t, fixedKit, tone ) );

		Add( into, "toms", "TUNING — stacked fourths, ascending fill", 112, 5,
			t => Up( t, fourths, tone ) );

		Add( into, "toms", "TUNING — stacked fourths, tom groove (floor pulse, rack answers)", 100, 5,
			t => Groove( t, fourths, tone ) );

		void Alone( Take t, TomKit kit, int i, TomTone k )
		{
			double[] hits = { 0, 0.75, 1.5, 2, 3 };
			foreach ( var h in hits ) t.G.RenderTom( t.At( h ), kit, i, t.N, 1f, k );
			t.G.RenderTom( t.At( 4 ), kit, i, t.N, 1f, k );
		}
		Add( into, "toms", "EACH PIECE — rack tom alone (fourths), repeated figure", 112, 5,
			t => Alone( t, fourths, 0, tone ) );

		Add( into, "toms", "KEY — fourths from F, same fill", 112, 5,
			t => Down( t, TomKit.Tuned( TomTune.Fourths, RootF ), tone ) );

		Add( into, "toms", "PAN — 40% spread, rack LEFT, same fill", 112, 5, t =>
		{ t.G.AuditionPan = 0.40f; Down( t, fourths, tone ); } );
		Add( into, "toms", "PAN — 40% spread, rack RIGHT (the flip), same fill", 112, 5, t =>
		{ t.G.AuditionPan = 0.40f; Down( t, TomKit.Tuned( TomTune.Fourths, RootC, rackLeft: false ), tone ); } );

		Add( into, "toms", "PITCH SAG — heavy (falls away), same fill", 112, 5,
			t => Down( t, fourths, tone.With( sag: 0.45f ) ) );
	}

	// ── HATS ──
	// All in pattern — a hat on its own says nothing. The choke lines are the ones that matter:
	// today an open hat rings through whatever comes next.

	static void Hats( List<Line> into )
	{
		var d = HatTone.Default;
		HatTone Open( float u ) => d.With(
			openDur: KitNuance.At( KitNuance.OpenHatDurMin, KitNuance.OpenHatDurMax, u ),
			decayFrac: 0.45 + 0.05 * u, openCut: KitNuance.OpenHatCut );

		// The lift is the test that matters — a steady foot should sound steady — and it is now
		// a geometric map, so a curve change moves far more than it did.
		void Lift( Take t, float curve, int landing )
		{
			for ( int i = 0; i < 8; i++ )
			{
				float o = i / 7f;
				t.G.RenderHat( t.At( i * 0.5 ), o, 0.72f + 0.28f * o, t.N,
					Open( o ).With( openCurve: curve ), t.At( 4 ) );
			}
			// LANDING. The foot comes down ON the downbeat: whatever was ringing is choked. What
			// happens at the same instant is the question — every one of these read as wrong when
			// the ring was left to run under a fresh struck hat.
			if ( landing == 0 ) t.G.RenderHat( t.At( 4 ), HatHit.Foot, 1f, t.N );      // chick
			else if ( landing == 1 ) t.G.RenderHat( t.At( 4 ), 0f, 0.95f, t.N, d );    // struck
			// landing 2: nothing at all — the choke IS the event.
		}

		Add( into, "hats", "LIFT — geometric, curve 1.00, foot-chick landing", 120, 5,
			t => Lift( t, 1f, 0 ) );
		Add( into, "hats", "LIFT — geometric, curve 0.70, foot-chick landing", 120, 5,
			t => Lift( t, 0.70f, 0 ) );
		Add( into, "hats", "LIFT — geometric, curve 0.50, foot-chick landing", 120, 5,
			t => Lift( t, 0.50f, 0 ) );
		Add( into, "hats", "LIFT — geometric, curve 0.35, foot-chick landing", 120, 5,
			t => Lift( t, 0.35f, 0 ) );

		Add( into, "hats", "LANDING — curve 0.70, struck closed hat instead of the chick", 120, 5,
			t => Lift( t, 0.70f, 1 ) );
		Add( into, "hats", "LANDING — curve 0.70, the choke alone, nothing struck", 120, 5,
			t => Lift( t, 0.70f, 2 ) );

		// And the static half-open, which is what the two indistinguishable lines were testing.
		void Eighths( Take t, float curve )
		{
			var h = Open( 0.5f ).With( openCurve: curve );
			for ( int i = 0; i < 8; i++ ) t.G.RenderHat( t.At( i * 0.5 ), 0.5f, 1f, t.N, h );
			t.G.RenderHat( t.At( 4 ), 0f, 0.9f, t.N, d );
		}
		Add( into, "hats", "HALF OPEN — geometric, curve 1.00 (0.15 s)", 120, 5,
			t => Eighths( t, 1f ) );
		Add( into, "hats", "HALF OPEN — geometric, curve 0.70 (0.20 s)", 120, 5,
			t => Eighths( t, 0.70f ) );
		Add( into, "hats", "HALF OPEN — geometric, curve 0.50 (0.26 s)", 120, 5,
			t => Eighths( t, 0.50f ) );
		Add( into, "hats", "HALF OPEN — geometric, curve 0.35 (0.33 s)", 120, 5,
			t => Eighths( t, 0.35f ) );

		Add( into, "hats", "IN USE — curve 0.70 half-open on 2 and 4, closed elsewhere", 120, 5,
			t =>
			{
				var half = Open( 0.5f ).With( openCurve: 0.70f );
				for ( int i = 0; i < 8; i++ )
				{
					bool h = i == 3 || i == 7;
					t.G.RenderHat( t.At( i * 0.5 ), h ? 0.5f : 0f, h ? 1f : 0.75f, t.N,
						h ? half : d, h ? t.At( i * 0.5 + 0.5 ) : int.MaxValue );
				}
				t.G.RenderHat( t.At( 4 ), 0f, 0.9f, t.N, d );
			} );
	}

	// ── CRASH ──

	static void Crash( List<Line> into )
	{
		Add( into, "crash", "BRIGHT — today's decay (0.6 s), accent on each downbeat", 116, 5, t =>
		{
			t.G.AuditionPan = 0.25f;
			t.G.RenderCrash( t.At( 0 ), t.N, false, 1f, CrashTone.Bright );
			t.G.RenderCrash( t.At( 4 ), t.N, false, 1f, CrashTone.Bright );
					}, CymbalTail );
		Add( into, "crash", "DARK — today's (0.9 s), same placement, opposite side", 116, 5, t =>
		{
			t.G.AuditionPan = 0.25f;
			t.G.RenderCrash( t.At( 0 ), t.N, true, 1f, CrashTone.Dark );
			t.G.RenderCrash( t.At( 4 ), t.N, true, 1f, CrashTone.Dark );
					}, CymbalTail );
		Add( into, "crash", "GAIN — accent level, then ridden level (0.45)", 116, 5,
			t =>
			{
				t.G.AuditionPan = 0.25f;
				t.G.RenderCrash( t.At( 0 ), t.N, false, 1f, CrashTone.Bright );
				t.G.RenderCrash( t.At( 4 ), t.N, false, 0.45f, CrashTone.Bright );
							}, CymbalTail );
		Add( into, "crash", "CRASH-RIDE — eighths on the dark crash, bright accenting bar one", 116, 5,
			t =>
			{
				t.G.AuditionPan = 0.25f;
				t.G.RenderCrash( t.At( 0 ), t.N, false, 1f, CrashTone.Bright );
				var ride = CrashTone.Dark.With( dur: 0.34f, decayFrac: 0.40f );
				for ( int i = 0; i < 8; i++ )
					t.G.RenderCrash( t.At( i * 0.5 ), t.N, true, (i & 1) == 0 ? 0.42f : 0.28f, ride );
				t.G.RenderCrash( t.At( 4 ), t.N, false, 1f, CrashTone.Bright );
			}, CymbalTail );
		Add( into, "crash", "CHOKE — a stab: crashed and grabbed on the offbeat", 116, 5, t =>
		{
			t.G.AuditionPan = 0.25f;
			t.G.RenderCrash( t.At( 0 ), t.N, false, 1f, CrashTone.Bright, t.At( 0.5 ) );
			t.G.RenderCrash( t.At( 2 ), t.N, false, 1f, CrashTone.Bright, t.At( 2.5 ) );
			t.G.RenderCrash( t.At( 4 ), t.N, false, 1f, CrashTone.Bright );
		}, CymbalTail );
	}

	// ── RIDE ──
	// Generation 5: pure waveforms (see RideModal in Kit.cs). Every noise-based generation —
	// four of them — came back "hats in weird states", and the listener's instruction was
	// explicit: a novel tone, synthesised as waveforms, not existing sounds with tweaked knobs.
	// So the cymbal is now a dense bank of inharmonic sine partials with per-partial ring times,
	// beating twins and per-hit phase/level variation; the only noise is the stick contact.

	static void Ride( List<Line> into )
	{
		void Straight( Take t, in RideModal m )
		{
			t.G.AuditionPan = 0.25f;
			for ( int i = 0; i < 8; i++ )
				t.G.RenderRideModal( t.At( i * 0.5 ), (i & 1) == 0 ? 1f : 0.62f, m );
			t.G.RenderRideModal( t.At( 4 ), 1f, m );
		}
		// A bell alone first — the attack and the ring are the whole question — then judged the
		// way it is played: bell on beats 1 and 3, bow eighths between, landing on the bell.
		void Quarters( Take t, in RideModal m )
		{
			t.G.AuditionPan = 0.25f;
			for ( int b = 0; b < 4; b++ ) t.G.RenderRideModal( t.At( b ), 1f, m );
			t.G.RenderRideModal( t.At( 4 ), 1f, m );
		}
		void BellBow( Take t, in RideModal bell, in RideModal bow )
		{
			t.G.AuditionPan = 0.25f;
			for ( int i = 0; i < 8; i++ )
			{
				if ( i == 0 || i == 4 ) t.G.RenderRideModal( t.At( i * 0.5 ), 1f, bell );
				else t.G.RenderRideModal( t.At( i * 0.5 ), 0.62f, bow );
			}
			t.G.RenderRideModal( t.At( 4 ), 1f, bell );
		}

		var bow = RideModal.Bow();
		var bell = RideModal.Bell();

		// The bow, and its three axes: shimmer density, shimmer level, ring length.
		Add( into, "ride", "BOW — the cymbal (~80 partials), straight eighths", 128, 5,
			t => Straight( t, bow ), 1.6 );
		Add( into, "ride", "BOW — sparser shimmer (half the high partials)", 128, 5,
			t => Straight( t, RideModal.Bow( density: 0.5f ) ), 1.6 );
		Add( into, "ride", "BOW — denser shimmer (1.6×)", 128, 5,
			t => Straight( t, RideModal.Bow( density: 1.6f ) ), 1.6 );
		Add( into, "ride", "BOW — shimmer forward (highs 1.6× louder)", 128, 5,
			t => Straight( t, RideModal.Bow( shimmer: 1.6f ) ), 1.6 );
		Add( into, "ride", "BOW — dark and dry (ring 0.6×, shimmer 0.7×)", 128, 5,
			t => Straight( t, RideModal.Bow( shimmer: 0.7f, ring: 0.6f ) ), 1.2 );
		Add( into, "ride", "BOW — washy (ring 1.7×)", 128, 5,
			t => Straight( t, RideModal.Bow( ring: 1.7f ) ), 2.2 );

		// The bell alone: f0 and stretch are its two characters.
		Add( into, "ride", "BELL — f0 840 Hz, quarter notes", 116, 5,
			t => Quarters( t, bell ), 2.8 );
		Add( into, "ride", "BELL — f0 700, stretched clangier (1.08)", 116, 5,
			t => Quarters( t, RideModal.Bell( f0: 700f, stretch: 1.08f ) ), 2.8 );
		Add( into, "ride", "BELL — f0 1000, squeezed purer (0.94)", 116, 5,
			t => Quarters( t, RideModal.Bell( f0: 1000f, stretch: 0.94f ) ), 2.8 );
		Add( into, "ride", "BELL — f0 840, short (ring 0.5×) — a stab, not a toll", 116, 5,
			t => Quarters( t, RideModal.Bell( ring: 0.5f ) ), 1.6 );

		// Together, as played.
		Add( into, "ride", "BELL+BOW — 840 bell over the bow", 116, 5,
			t => BellBow( t, bell, bow ), 2.8 );
		Add( into, "ride", "BELL+BOW — 700 clangy bell over the shimmer-forward bow", 116, 5,
			t => BellBow( t, RideModal.Bell( f0: 700f, stretch: 1.08f ),
				RideModal.Bow( shimmer: 1.6f ) ), 2.8 );
		Add( into, "ride", "BELL+BOW — 1000 purer bell over the dark bow", 116, 5,
			t => BellBow( t, RideModal.Bell( f0: 1000f, stretch: 0.94f ),
				RideModal.Bow( shimmer: 0.7f, ring: 0.6f ) ), 2.8 );

		// And in motion.
		Add( into, "ride", "PATTERN — swung ride on the bow", 112, 5, t =>
		{
			t.G.AuditionPan = 0.25f;
			double[] at = { 0, 1, 5.0 / 3.0, 2, 3, 11.0 / 3.0 };
			foreach ( var a in at )
				t.G.RenderRideModal( t.At( a ), a == Math.Floor( a ) ? 1f : 0.62f, bow );
			t.G.RenderRideModal( t.At( 4 ), 1f, bow );
		}, 1.6 );
		Add( into, "ride", "PATTERN — riding eighths, bell replacing the accent on each downbeat",
			120, 5, t =>
		{
			t.G.AuditionPan = 0.25f;
			for ( int i = 0; i < 8; i++ )
			{
				if ( i % 4 == 0 ) t.G.RenderRideModal( t.At( i * 0.5 ), 1f, bell );
				else t.G.RenderRideModal( t.At( i * 0.5 ), 0.62f, bow );
			}
			t.G.RenderRideModal( t.At( 4 ), 1f, bell );
		}, 2.8 );
	}
}
