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
		// The default run is what is still OPEN. The ride is solved, so the default is the
		// CRASHES — plus the ride, because the table stamp is a new way of playing an approved
		// sound and it has to be heard again for that to mean anything.
		Crash( lines );
		Ride( lines );
		if ( !string.IsNullOrEmpty( only ) ) { Kick( lines ); Snare( lines ); Toms( lines ); Hats( lines ); }

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
		script.AppendLine( "AUDITION — skafinity drum kit, round 9: THE CRASHES, MEASURED" );
		script.AppendLine( "One kit part per line. Each line is a figure played by that voice alone." );
		script.AppendLine( "Dry: no tone lean, no genre mix, no reverb, no master. Centred except where noted." );
		script.AppendLine();
		script.AppendLine( "The crashes get round 8's treatment: measured off the same CC0 kit and reduced to" );
		script.AppendLine( "laws, never a table. The finding is that NEITHER crash obeys the ride's ring law." );
		script.AppendLine( "The bright one is roar first — a third of a second in, the measurement resolves no" );
		script.AppendLine( "partials at all — and its lows die fast where the ride's ring on. The dark one is" );
		script.AppendLine( "the inverse: a low body gone in half a second under a top that rings for two." );
		script.AppendLine( "So the ring law gained two terms, one per cymbal, and both were fitted." );
		script.AppendLine();
		script.AppendLine( "The RIDE lines are here for a different reason: a modal hit costs ~250 ms of CPU," );
		script.AppendLine( "and a riding section is a thousand hits, so the cymbal is now rendered ONCE and" );
		script.AppendLine( "stamped — a sampler's trick, over a spectrum that was measured off a real cymbal." );
		script.AppendLine( "That is what makes the approved ride wirable into a groove at all. What it costs" );
		script.AppendLine( "is that hits repeat exactly, so a stick transient is still played live per hit." );
		script.AppendLine( "Lines 2 and 3 of the RIDE block are two round robins and ONE; round 9 heard no" );
		script.AppendLine( "difference, so ONE is what ships and the pair stays here as the record of it." );
		script.AppendLine( "--audition kick|snare|toms|hats still plays the settled voices." );
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

	/// <summary>
	/// One dry, centred, un-normalised hit of each cymbal, written as a WAV.
	///
	/// This is step 5 of the measured-cymbal method (DRUMS.md): a spectrum that was fitted to a
	/// measurement is only fitted once someone measures the RESULT the same way. Run tools/spectool
	/// over these and compare its band decays and sustain spectrum against the numbers recorded in
	/// CymbalModal — that is what caught the ride being 15 dB light in sizzle above 4.7 kHz, and it
	/// is the only check on this voice that does not require ears.
	/// </summary>
	public static void Cymbals( string dir )
	{
		Directory.CreateDirectory( dir );
		void One( string name, Func<MusicGen, CymbalTable> build )
		{
			var g = MusicGen.ForAudition( Cfg(), 4.5, 120 );
			g.AuditionPan = 0f;
			g.RenderCymbal( 0, 1f, build( g ) );
			var (l, r) = g.AuditionBuffers();
			float peak = 1e-9f;
			for ( int i = 0; i < l.Length; i++ )
				peak = Math.Max( peak, Math.Max( Math.Abs( l[i] ), Math.Abs( r[i] ) ) );
			var pcm = new short[l.Length * 2];
			for ( int i = 0; i < l.Length; i++ )
			{
				pcm[i * 2] = (short)Math.Clamp( (int)MathF.Round( l[i] / peak * 32000f ), -32768, 32767 );
				pcm[i * 2 + 1] = (short)Math.Clamp( (int)MathF.Round( r[i] / peak * 32000f ), -32768, 32767 );
			}
			string path = Path.Combine( dir, name + ".wav" );
			File.WriteAllBytes( path, MusicGen.WavFromSamples( pcm, 2, Rate ) );
			Console.WriteLine( $"  {path}" );
		}
		Console.WriteLine( "one hit per cymbal, dry and centred, peak-normalised per file:" );
		One( "ride-bow", g => g.BuildRide( CymbalModal.Bow() ) );
		One( "ride-bell", g => g.BuildRide( CymbalModal.Bell() ) );
		One( "crash-bright", g => g.BuildCrash( CymbalModal.CrashBright(), dark: false ) );
		One( "crash-dark", g => g.BuildCrash( CymbalModal.CrashDark(), dark: true ) );
	}

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
	// Round 9. The crashes passed round 1 as filtered noise, and round 8 obsoleted that pass: a
	// crash is the same physics as the ride at a different strike, and the ride only stopped
	// sounding like a hat in a weird state when its spectrum became a measured mode forest. So
	// both crashes were measured off the same CC0 kit (see CymbalModal) — and neither of them
	// obeys the ride's ring law, which is the finding. The bright one's whole first half-second
	// is roar with no partials resolvable in it; the dark one's low body dies while its top rings
	// on for two seconds. Those are two different extra terms, not one shared fudge.

	static void Crash( List<Line> into )
	{
		CymbalTable Bright( Take t, in CymbalModal m )
		{ t.G.AuditionPan = 0.25f; return t.G.BuildCrash( m, dark: false ); }
		CymbalTable Dark( Take t, in CymbalModal m )
		{ t.G.AuditionPan = 0.25f; return t.G.BuildCrash( m, dark: true ); }

		// A crash lands on a downbeat and is left alone: the point IS the decay, and it repeats
		// so the tail can be heard twice.
		void Accent( Take t, CymbalTable c )
		{
			t.G.RenderCymbal( t.At( 0 ), 1f, c );
			t.G.RenderCymbal( t.At( 4 ), 1f, c );
		}

		Add( into, "crash", "BRIGHT — measured (roar first, forest under it), accent every 2 bars",
			116, 5, t => Accent( t, Bright( t, CymbalModal.CrashBright() ) ), CymbalTail );
		Add( into, "crash", "BRIGHT — roar halved (the forest earlier and barer)", 116, 5,
			t => Accent( t, Bright( t, CymbalModal.CrashBright( splash: 0.5f ) ) ), CymbalTail );
		Add( into, "crash", "BRIGHT — roar 1.6×", 116, 5,
			t => Accent( t, Bright( t, CymbalModal.CrashBright( splash: 1.6f ) ) ), CymbalTail );
		Add( into, "crash", "BRIGHT — ring 1.4× (a bigger cymbal)", 116, 5,
			t => Accent( t, Bright( t, CymbalModal.CrashBright( ring: 1.4f ) ) ), CymbalTail );
		Add( into, "crash", "BRIGHT — ring 0.7× (a thinner, faster one)", 116, 5,
			t => Accent( t, Bright( t, CymbalModal.CrashBright( ring: 0.7f ) ) ), CymbalTail );

		Add( into, "crash", "DARK — measured: low body, top that outlives it, opposite side",
			116, 5, t => Accent( t, Dark( t, CymbalModal.CrashDark() ) ), CymbalTail );
		Add( into, "crash", "DARK — roar halved", 116, 5,
			t => Accent( t, Dark( t, CymbalModal.CrashDark( splash: 0.5f ) ) ), CymbalTail );
		Add( into, "crash", "DARK — wash 1.6× (more air under the ring)", 116, 5,
			t => Accent( t, Dark( t, CymbalModal.CrashDark( wash: 1.6f ) ) ), CymbalTail );
		Add( into, "crash", "DARK — ring 1.3×", 116, 5,
			t => Accent( t, Dark( t, CymbalModal.CrashDark( ring: 1.3f ) ) ), CymbalTail );

		Add( into, "crash", "BOTH — bright then dark, a bar apart: are these two cymbals?", 116, 5,
			t =>
			{
				var b = Bright( t, CymbalModal.CrashBright() );
				var d = Dark( t, CymbalModal.CrashDark() );
				t.G.RenderCymbal( t.At( 0 ), 1f, b );
				t.G.RenderCymbal( t.At( 2 ), 1f, d );
				t.G.RenderCymbal( t.At( 4 ), 1f, b );
			}, CymbalTail );

		// The gain the whole voice never had: every crash in every song landed at full level
		// whatever the energy, the velocity or the genre's accent said.
		Add( into, "crash", "GAIN — accent (1.0), then ridden (0.45), then a ghosted 0.25", 116, 5,
			t =>
			{
				var b = Bright( t, CymbalModal.CrashBright() );
				t.G.RenderCymbal( t.At( 0 ), 1f, b );
				t.G.RenderCymbal( t.At( 2 ), 0.45f, b );
				t.G.RenderCymbal( t.At( 4 ), 0.25f, b );
			}, CymbalTail );

		// Crash-riding: a technique, not a cymbal. The dark crash carries the pulse and the
		// bright one accents the bar — which is what the two sides of the kit are for.
		Add( into, "crash", "CRASH-RIDE — eighths on the dark crash, bright accenting bar one",
			116, 5, t =>
			{
				var b = Bright( t, CymbalModal.CrashBright() );
				var d = Dark( t, CymbalModal.CrashDark() );
				t.G.RenderCymbal( t.At( 0 ), 1f, b );
				for ( int i = 0; i < 8; i++ )
					t.G.RenderCymbal( t.At( i * 0.5 ), (i & 1) == 0 ? 0.42f : 0.28f, d );
				t.G.RenderCymbal( t.At( 4 ), 1f, b );
			}, CymbalTail );

		Add( into, "crash", "CHOKE — a stab: crashed and grabbed on the offbeat", 116, 5, t =>
		{
			var b = Bright( t, CymbalModal.CrashBright() );
			t.G.RenderCymbal( t.At( 0 ), 1f, b, t.At( 0.5 ) );
			t.G.RenderCymbal( t.At( 2 ), 1f, b, t.At( 2.5 ) );
			t.G.RenderCymbal( t.At( 4 ), 1f, b );
		}, CymbalTail );
	}

	// ── RIDE ──
	// Generation 6: the measured cymbal (see RideModal in Kit.cs). Generation 5's pure sine
	// banks read as church bells — an authored spectrum, however dense, put the energy where a
	// bell has it rather than where a ride does. So a real ride was measured (Virtuosity Drums,
	// CC0) and the measurement reduced to three laws: τ·√f constant, constant modal density,
	// strike position as a spectral bump. The splash is back too — the measured attack is a
	// broadband burst, and the listener asked for it in as many words.

	static void Ride( List<Line> into )
	{
		// A cymbal is built once and stamped (CymbalTable) — a modal hit is ~250 ms of CPU and a
		// riding section is a thousand of them, so the alternative to this was a ride that could
		// not be wired into a groove at all. The lines below are the same measurement; what a
		// round robin buys is that two consecutive hits are not the identical waveform.
		CymbalTable Ride1( Take t, in CymbalModal m, int v = 0 )
		{ t.G.AuditionPan = 0.25f; return t.G.BuildRide( m, v ); }

		void Straight( Take t, in CymbalModal m )
		{
			var a = Ride1( t, m, 0 ); var b = Ride1( t, m, 1 );
			for ( int i = 0; i < 8; i++ )
				t.G.RenderCymbal( t.At( i * 0.5 ), (i & 1) == 0 ? 1f : 0.62f, (i & 1) == 0 ? a : b );
			t.G.RenderCymbal( t.At( 4 ), 1f, a );
		}
		// The single hit, left to ring: the one figure where the point IS the hit, because the
		// ring is the instrument and it has to be heard uncovered at least once. It repeats.
		void Alone( Take t, in CymbalModal m )
		{
			var a = Ride1( t, m );
			t.G.RenderCymbal( t.At( 0 ), 1f, a );
			t.G.RenderCymbal( t.At( 4 ), 1f, a );
		}
		void Quarters( Take t, in CymbalModal m )
		{
			var a = Ride1( t, m, 0 ); var b = Ride1( t, m, 1 );
			for ( int i = 0; i < 4; i++ ) t.G.RenderCymbal( t.At( i ), 1f, (i & 1) == 0 ? a : b );
			t.G.RenderCymbal( t.At( 4 ), 1f, a );
		}
		void BellBow( Take t, in CymbalModal bell, in CymbalModal bow )
		{
			var bl = Ride1( t, bell ); var a = Ride1( t, bow, 0 ); var b = Ride1( t, bow, 1 );
			for ( int i = 0; i < 8; i++ )
			{
				if ( i == 0 || i == 4 ) t.G.RenderCymbal( t.At( i * 0.5 ), 1f, bl );
				else t.G.RenderCymbal( t.At( i * 0.5 ), 0.62f, (i & 1) == 0 ? a : b );
			}
			t.G.RenderCymbal( t.At( 4 ), 1f, bl );
		}

		var bow = CymbalModal.Bow();
		var bell = CymbalModal.Bell();

		Add( into, "ride", "BOW — one hit, left to ring (twice)", 60, 5,
			t => Alone( t, bow ), 2.6 );
		Add( into, "ride", "BOW — straight eighths, as measured, stamped from the table", 128, 5,
			t => Straight( t, bow ), 1.8 );
		Add( into, "ride", "BOW — the same eighths on ONE round robin (is the repeat audible?)",
			128, 5, t =>
			{
				var a = Ride1( t, bow );
				for ( int i = 0; i < 8; i++ )
					t.G.RenderCymbal( t.At( i * 0.5 ), (i & 1) == 0 ? 1f : 0.62f, a );
				t.G.RenderCymbal( t.At( 4 ), 1f, a );
			}, 1.8 );
		Add( into, "ride", "BOW — splash halved", 128, 5,
			t => Straight( t, CymbalModal.Bow( splash: 0.5f ) ), 1.8 );
		Add( into, "ride", "BOW — splash 1.8×", 128, 5,
			t => Straight( t, CymbalModal.Bow( splash: 1.8f ) ), 1.8 );
		Add( into, "ride", "BOW — wash 1.8× (more air between the partials)", 128, 5,
			t => Straight( t, CymbalModal.Bow( wash: 1.8f ) ), 1.8 );
		Add( into, "ride", "BOW — ring 1.4× (longer than measured)", 128, 5,
			t => Straight( t, CymbalModal.Bow( ring: 1.4f ) ), 2.4 );

		Add( into, "ride", "BELL — one hit, left to ring (twice)", 60, 5,
			t => Alone( t, bell ), 2.6 );
		Add( into, "ride", "BELL — quarter notes, as measured (clang 2.3 kHz)", 116, 5,
			t => Quarters( t, bell ), 2.4 );
		Add( into, "ride", "BELL — darker clang (2.0 kHz)", 116, 5,
			t => Quarters( t, CymbalModal.Bell( clang: 2000f ) ), 2.4 );
		Add( into, "ride", "BELL — brighter clang (2.6 kHz)", 116, 5,
			t => Quarters( t, CymbalModal.Bell( clang: 2600f ) ), 2.4 );

		Add( into, "ride", "BELL+BOW — alternating, one cymbal", 116, 5,
			t => BellBow( t, bell, bow ), 2.4 );
		Add( into, "ride", "PATTERN — swung ride on the bow", 112, 5, t =>
		{
			var a = Ride1( t, bow, 0 ); var b = Ride1( t, bow, 1 );
			double[] at = { 0, 1, 5.0 / 3.0, 2, 3, 11.0 / 3.0 };
			for ( int i = 0; i < at.Length; i++ )
				t.G.RenderCymbal( t.At( at[i] ), at[i] == Math.Floor( at[i] ) ? 1f : 0.62f,
					(i & 1) == 0 ? a : b );
			t.G.RenderCymbal( t.At( 4 ), 1f, a );
		}, 1.8 );
		Add( into, "ride", "PATTERN — riding eighths, bell on the downbeats", 120, 5, t =>
		{
			var bl = Ride1( t, bell ); var a = Ride1( t, bow, 0 ); var b = Ride1( t, bow, 1 );
			for ( int i = 0; i < 8; i++ )
			{
				if ( i % 4 == 0 ) t.G.RenderCymbal( t.At( i * 0.5 ), 1f, bl );
				else t.G.RenderCymbal( t.At( i * 0.5 ), 0.62f, (i & 1) == 0 ? a : b );
			}
			t.G.RenderCymbal( t.At( 4 ), 1f, bl );
		}, 2.4 );
	}
}
