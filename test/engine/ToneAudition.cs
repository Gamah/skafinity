using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skafinity.EngineTests;

/// <summary>
/// THE TONE AUDITION — the PITCHED voices, one instrument per block, each playing the same short
/// figure once per candidate timbre, so a voice is chosen by listening rather than by argument.
/// The kit has had this for eleven rounds (see Audition / DRUMS.md); this is the same doctrine
/// pointed at the half of the band that was never auditioned at all.
///
/// WHY THERE IS A ROUND AT ALL. Every pitched voice in this engine is a detuned saw through one
/// resonant low-pass. That is a complete description of nine instruments: bass, both guitars, the
/// keys, the skank, the horn section, the trumpet, the trombone and (triangle instead of saw) the
/// sax. A low-pass can only take harmonics off the TOP of a spectrum, in one fixed slope, at a
/// brightness that does not depend on how hard the note was played — so the differences between
/// those nine are a cutoff and a drive, and they read as nine settings of one synth, which is
/// what they are.
///
/// The candidates are not nine new patches. They are FOUR mechanisms (Patch.Duty, Patch.Pluck,
/// Patch.CutEnvSec, Patch.DriveEnv — read their comments, each is a physical property of a real
/// sounding body with the maths for why a filter cannot stand in for it), applied to the voices
/// whose instrument actually has that property. A candidate is therefore stated as an EDIT to the
/// real patch, through MusicGen.AuditionPatch, and the real voice renders it — so an approved line
/// lands as that same edit inside the voice, and nothing here is a copy of a patch that can drift.
///
/// DRY, like the kit's: no master bus, so no reverb, no soft-clip, above all no peak normalize,
/// and double-tracking is off so each line is one centred take. ONE gain over the whole file at
/// the end — a candidate that is brighter but quieter has to be AUDIBLY brighter but quieter.
/// </summary>
static class ToneAudition
{
	const int Rate = 44100;
	const double GapSec = 0.25;
	const double TailSec = 0.55;
	const float FilePeak = 0.89f;

	/// <summary>One candidate timbre: a name, and the edit it makes to the voice's real patch.
	/// A null edit is the voice as it stands today — present as a candidate in its own right,
	/// not as a baseline.</summary>
	sealed class Cand
	{
		public string Name;
		public string Why;
		public Func<Patch, Patch> Edit;
	}

	sealed class Block
	{
		public string Voice;
		public string Figure;        // what the line plays, in words
		public int Genre;
		public int Bpm;
		public double Beats;
		public int Instrument = -1;  // ska lead instrument, where the block is one
		public Action<Take> Play;
		public List<Cand> Cands = new();
	}

	/// <summary>What a figure is handed: the generator, and where a beat is.</summary>
	sealed class Take
	{
		public readonly MusicGen G;
		public Take( MusicGen g ) { G = g; }
		public int At( double beats ) => G.AuditionTiming.TickToSample( (int)(beats * Timing.TicksPerBeat) );
		public int Tick( double beats ) => (int)(beats * Timing.TicksPerBeat);
		public int Beat => At( 1 ) - At( 0 );
	}

	// ── Entry point ──

	public static void Run( string only, string wavPath, string txtPath )
	{
		var blocks = new List<Block>();
		Bass( blocks ); RhythmGtr( blocks ); LeadGtr( blocks ); Keys( blocks );
		Skank( blocks ); Organ( blocks ); Horns( blocks ); Sax( blocks ); Trumpet( blocks );

		if ( !string.IsNullOrEmpty( only ) )
			blocks = blocks.FindAll( b => b.Voice.Equals( only, StringComparison.OrdinalIgnoreCase ) );
		if ( blocks.Count == 0 )
		{
			Console.WriteLine( "no tone-audition block for '" + only + "' — try "
				+ "bass|rhythmgtr|leadgtr|keys|skank|organ|horns|sax|trumpet" );
			return;
		}

		var L = new List<float>();
		var R = new List<float>();
		var script = new StringBuilder();
		script.AppendLine( "TONE AUDITION — skafinity pitched voices, round 3: THE SHIPPED VOICES" );
		script.AppendLine();
		script.AppendLine( "The physical models are now what the band plays, so \"as it stands\" below is" );
		script.AppendLine( "the voice in the engine and the lines under it are what is still on the table" );
		script.AppendLine( "for that instrument. Three methods, chosen by physical class:" );
		script.AppendLine();
		script.AppendLine( "  STRING  bass, both guitars, the skank. A delay line and a loss filter are" );
		script.AppendLine( "          the travelling-wave solution of the string equation — not an" );
		script.AppendLine( "          imitation of it. Each partial decays at the loss filter\'s own" );
		script.AppendLine( "          response, so the 12th is gone long before the 1st with nothing" );
		script.AppendLine( "          written down; the pluck is an INITIAL CONDITION (a triangle with" );
		script.AppendLine( "          its apex where the pick was, whose coefficients are sin(n.pi.b)/n^2" );
		script.AppendLine( "          — pluck position and a real pluck\'s rolloff being one shape, not" );
		script.AppendLine( "          two settings); and a palm mute is one damping number." );
		script.AppendLine( "  FM      horns, trumpet, trombone, sax. A blown instrument\'s harmonics are" );
		script.AppendLine( "          MADE by a non-linear valve, so they are generated rather than" );
		script.AppendLine( "          filtered away. Sidebands at fc +- k.fm with amplitude J_k(I) reach" );
		script.AppendLine( "          about (I+1) of them, so every index here is derived from a wanted" );
		script.AppendLine( "          bandwidth. The index gets its own faster envelope: blown harder is" );
		script.AppendLine( "          brighter, and a dying note goes dull before it goes quiet." );
		script.AppendLine( "  MODAL   keys, organ. A body IS the sum of its modes, each decaying at its" );
		script.AppendLine( "          own rate. Tone wheels are whole-number ratios and do not decay at" );
		script.AppendLine( "          all; a tine is the free-free bar roots (1, 2.76, 5.40, 8.93 — the" );
		script.AppendLine( "          squares of the cos.cosh = 1 solutions), which fuse into no pitch;" );
		script.AppendLine( "          a piano string is sharp by sqrt(1 + B.n^2)." );
		script.AppendLine();
		script.AppendLine( "THE KNOBS ARE UNCHANGED. Amp, pan, the tone/cutoff knobs, the drive and the" );
		script.AppendLine( "whole BENDINESS layer (bend-in, scoop, bend-up, vibrato) all still apply and" );
		script.AppendLine( "still mean one thing across the band — a bend shortens a delay line, scales an" );
		script.AppendLine( "FM pair, and retunes a modal bank. CHUG is now the palm: a mute is an" );
		script.AppendLine( "absorbent termination, so it goes short AND dull rather than merely short." );
		script.AppendLine( "Every per-voice level was re-measured with --levels so the mix did not move." );
		script.AppendLine();
		script.AppendLine( "Dry: no master bus, no reverb, no normalize per line, double-tracking off." );
		script.AppendLine( "One gain over the whole file, so levels between lines mean something." );
		script.AppendLine( "--tone bass|rhythmgtr|leadgtr|keys|skank|organ|horns|sax|trumpet for one." );

		int gap = (int)(Rate * GapSec);
		int n = 0;
		// A machine-readable index alongside the script: a listener wants the file, but anything
		// that A/Bs two candidates — a player page, a DFT pointed at one onset, ffmpeg cutting the
		// lines apart — needs the boundaries in SAMPLES, and re-deriving them from a printed m:ss.s
		// is a rounding error per line.
		var tsv = new StringBuilder( "line\tstart\tlength\tvoice\tfigure\tcandidate\twhy\n" );
		foreach ( var b in blocks )
		{
			script.AppendLine();
			script.AppendLine( "── " + b.Voice.ToUpperInvariant() + " ── " + b.Figure );
			foreach ( var c in b.Cands )
			{
				double seconds = b.Beats * 60.0 / b.Bpm + TailSec;
				var g = MusicGen.ForAudition( Cfg( b.Genre ), seconds, b.Bpm );
				g.AuditionBand( b.Genre, b.Instrument );
				g.AuditionPatch = c.Edit;
				b.Play( new Take( g ) );
				var (bl, br) = g.AuditionBuffers();
				g.RenderPitchedRange( 0, bl.Length );
				script.AppendLine( $"{++n,3}. [{Stamp( L.Count )}] {c.Name,-18} {c.Why}" );
				tsv.Append( n ).Append( '\t' ).Append( L.Count ).Append( '\t' ).Append( bl.Length )
					.Append( '\t' ).Append( b.Voice ).Append( '\t' ).Append( b.Figure )
					.Append( '\t' ).Append( c.Name ).Append( '\t' ).Append( c.Why ).Append( '\n' );
				L.AddRange( bl ); R.AddRange( br );
				for ( int s = 0; s < gap; s++ ) { L.Add( 0f ); R.Add( 0f ); }
			}
		}

		script.AppendLine();
		script.AppendLine( $"{n} lines, {Stamp( L.Count )} total." );
		Write( wavPath, txtPath, L, R, script.ToString() );
		string tsvPath = Path.ChangeExtension( wavPath, ".tsv" );
		File.WriteAllText( tsvPath, tsv.ToString() );
		Console.WriteLine( $"tsv  {tsvPath}" );
	}

	// Double-tracking OFF: a widened note is two takes a few cents and a few milliseconds apart,
	// which is a real part of the mix and the wrong thing to hear a WAVEFORM through.
	static MusicGen.Config Cfg( int genre ) => new() { SampleRate = Rate, Genre = genre, DoubleTrack = 0f };

	// ── Candidate helpers ──
	// Each returns the edit, so a block reads as a list of claims about an instrument.

	static Cand Today( string why ) => new() { Name = "as it stands", Why = why };

	static Cand Edit( string name, string why, Func<Patch, Patch> f )
		=> new() { Name = name, Why = why, Edit = f };

	static Func<Patch, Patch> Pluck( float beta ) => p => { p.Pluck = beta; return p; };

	static Func<Patch, Patch> Pulse( float duty, float sweep = 0f )
		=> p => { p.Osc = 4; p.Duty = duty; p.DutyEnv = sweep; return p; };

	static Func<Patch, Patch> Bloom( float amount, int osc = -1 )
		=> p => { p.DriveEnv = amount; if ( osc >= 0 ) p.Osc = osc; return p; };

	static Func<Patch, Patch> Then( Func<Patch, Patch> a, Func<Patch, Patch> b ) => p => b( a( p ) );

	static Func<Patch, Patch> CutSec( float sec ) => p => { p.CutEnvSec = sec; return p; };

	/// <summary>Swap the source for a plucked string, leaving everything else on the patch —
	/// level, pan, the tone knobs, the drive, the bends — exactly as the voice set it.
	/// <paramref name="t60"/> is the one damping number: seconds for the fundamental to fall
	/// 60 dB, which is what a palm mute and an open ring are two values of.</summary>
	static Func<Patch, Patch> Str( float t60, float damp, float beta, float width, float noise = 0.02f )
		=> p =>
		{
			p.Model = Model.String;
			p.StringDecay = t60; p.StringDamp = damp;
			p.Pluck = beta; p.PickWidth = width; p.PickNoise = noise;
			// The cutoff envelope was the old voice's stand-in for high partials dying first. The
			// loop filter does that for real now, so leaving it on would be describing it twice.
			p.CutEnv = 0f;
			return p;
		};

	/// <summary>Swap the source for an FM pair. <paramref name="topHarmonic"/> is the point of the
	/// interface: sidebands reach about (I+1).ratio harmonics, so a wanted bandwidth is the thing
	/// stated and the index is derived from it rather than dialled.</summary>
	static Func<Patch, Patch> Fm( float ratio, float topHarmonic, float sustain, float idxSec,
		float feedback = 0f )
		=> p =>
		{
			p.Model = Model.Fm;
			p.FmRatio = ratio;
			// Sidebands sit at fc +- k.fm, so with the modulator at `ratio` times the carrier the
			// kth lands on harmonic 1 + k.ratio, and there are about I + 1 of them (Carson). So
			// the top harmonic is 1 + ratio.(I+1) and the index that reaches it is:
			p.FmIndex = MathF.Max( 0.2f, (topHarmonic - 1f) / ratio - 1f );
			p.FmIndexSus = sustain; p.FmIndexSec = idxSec; p.FmFeedback = feedback;
			p.CutEnv = 0f;
			return p;
		};

	/// <summary>Read the DISTORTION knob the way the engine's own clean keyboards read it (see
	/// KeysDriveFor: country's piano and pop's synth take 1 + 0.2x where rock's organ takes it
	/// whole). A candidate that changes the instrument has to change this too — a tine is not an
	/// overdriven organ, and at drive 3.2 the tanh squares off the fundamental and generates
	/// harmonics of its own, which deletes precisely the inharmonicity that was the point.</summary>
	static Func<Patch, Patch> Clean( Func<Patch, Patch> f )
		=> p => { var q = f( p ); q.Drive = 1f + 0.2f * MathF.Max( 1f, q.Drive ); return q; };

	/// <summary>Swap the source for a modal bank. <paramref name="decay"/> is the FUNDAMENTAL's,
	/// and the upper modes scale off it; a sustained bank ignores it, because a tone wheel does
	/// not decay.</summary>
	static Func<Patch, Patch> Modal( int set, int count, float decay, float inharm = 0f,
		bool sustain = false )
		=> p =>
		{
			p.Model = Model.Modal;
			p.ModalSet = set; p.ModalCount = count; p.ModalDecay = decay;
			p.ModalInharm = inharm; p.ModalSustain = sustain;
			p.CutEnv = 0f;
			return p;
		};

	// ── BASS ──
	// Eighths on the root with a fifth and an octave in them — the line a bass actually plays, so
	// the question is what the NOTE does rather than what one hit does.

	static void Bass( List<Block> into )
	{
		var b = new Block
		{
			Voice = "bass", Genre = 1, Bpm = 148, Beats = 8.5,
			Figure = "two bars of eighths, E1 root with its fifth and octave in them",
		};
		int[] line = { 0, 0, 7, 0, 12, 0, 7, 5, 0, 0, 7, 12, 0, 7, 0, 0 };
		b.Play = t =>
		{
			for ( int i = 0; i < line.Length; i++ )
			{
				double beat = i * 0.5;
				t.G.EmitBass( t.At( beat ), (int)(t.Beat * 0.46), 28 + line[i], 0.30, 1f, default );
			}
			t.G.EmitBass( t.At( 8 ), t.Beat, 28, 0.55, 1f, default );
		};
		// An electric bass is plucked a FIFTH of the way up from the bridge by the thumb-anchored
		// hand — which is why 0.2 rather than a round number: it nulls the 5th harmonic and leaves
		// the 2nd and 3rd intact, and those two ARE the growl. 0.5 is the other interesting place:
		// a centre pluck has no even harmonics at all, which is round and dub-like.
		b.Cands.Add( Today( "SHIPPED: fingered string — fingertip 12% wide, plucked at 0.20, rings 1.1 s" ) );
		// A fingered electric bass: plucked a fifth up from the bridge by the thumb-anchored hand,
		// a fingertip's worth of contact width, a wound string's absorbent termination.
		b.Cands.Add( Edit( "string, fingered", "delay line + loss filter. Fingertip 12% of the string, plucked at 0.20, rings 1.2 s", Str( 1.2f, 0.52f, 0.20f, 0.12f ) ) );
		b.Cands.Add( Edit( "string, picked", "a plectrum instead: 3% contact, nearer the bridge — clank and growl", Str( 1.2f, 0.34f, 0.13f, 0.03f, 0.06f ) ) );
		b.Cands.Add( Edit( "string, flatwound", "dead strings: absorbent termination, short ring. The Motown bass", Str( 0.55f, 0.70f, 0.24f, 0.18f, 0.01f ) ) );
		into.Add( b );
	}

	// ── RHYTHM GUITAR ──
	// Rock's placed riff: ringing chords with a muted chug between them, which is the contrast the
	// voice has to survive.

	static void RhythmGtr( List<Block> into )
	{
		var b = new Block
		{
			Voice = "rhythmgtr", Genre = 1, Bpm = 148, Beats = 8.5,
			Figure = "two bars of rock riff: ringing power chords with muted chugs between",
		};
		int[] triad = { 52, 59, 64 };
		b.Play = t =>
		{
			void Chord( double beat, double len, float vel )
			{
				foreach ( int m in triad )
					t.G.EmitGuitar( t.Tick( beat ), t.Tick( len ), m, vel, true, triad.Length );
			}
			void Chug( double beat, float vel )
				=> t.G.EmitGuitar( t.Tick( beat ), t.Tick( 0.2 ), 40, vel, false, 1 );
			Chord( 0, 0.9, 1f ); Chug( 1, 0.7f ); Chug( 1.5, 0.6f );
			Chord( 2, 0.7, 0.95f ); Chug( 3, 0.7f ); Chord( 3.5, 0.9, 1f );
			Chord( 4, 0.9, 1f ); Chug( 5, 0.7f ); Chug( 5.5, 0.6f );
			Chord( 6, 1.4, 0.95f ); Chord( 8, 1.0, 1f );
		};
		b.Cands.Add( Today( "SHIPPED: string, CHUG 0.5 — so T60 1.1 s and a half-absorbent palm, into drive" ) );
		// THE CHUG KNOB IS WHAT THIS BLOCK IS ABOUT. RhythmGtrChug is today a note-length multiplier
		// — a palm mute faked by playing shorter. A muted string is a DAMPED string: the palm is an
		// absorbent termination, so the note is short AND dull, and the ringing chords either side
		// of it are the same string with the hand lifted. One number, two sounds.
		b.Cands.Add( Edit( "string, ringing", "hand off the bridge: rings 1.4 s, bright termination", Str( 1.4f, 0.22f, 0.25f, 0.03f, 0.04f ) ) );
		b.Cands.Add( Edit( "string, palm muted", "the same string, palm ON: 90 ms and dull. This is what CHUG should drive", Str( 0.09f, 0.62f, 0.25f, 0.05f, 0.04f ) ) );
		b.Cands.Add( Edit( "string, half muted", "hand resting: 400 ms, the figure's own contrast between hits", Str( 0.4f, 0.4f, 0.25f, 0.04f, 0.04f ) ) );
		into.Add( b );
	}

	// ── LEAD GUITAR ──
	// A lick with a bend in it, because the lead's whole expression layer is pitch gestures and a
	// candidate has to hold up under one.

	static void LeadGtr( List<Block> into )
	{
		var b = new Block
		{
			Voice = "leadgtr", Genre = 1, Bpm = 148, Beats = 8.5,
			Figure = "a six-note rock lick, the last note bent up a tone and held",
		};
		int[] lick = { 64, 67, 69, 67, 64, 62, 64 };
		double[] at = { 0, 0.5, 1, 1.5, 2, 2.5, 3 };
		b.Play = t =>
		{
			var (drive, cutEnv) = t.G.LeadGtrTone();
			for ( int i = 0; i < lick.Length; i++ )
			{
				var p = t.G.LeadGtrPatch( 0.22f, 0.45, drive, cutEnv );
				t.G.RenderPatch( t.At( at[i] ), (int)(t.Beat * 0.45), Osc.Midi( lick[i] ), p, mono: true );
			}
			var held = t.G.LeadGtrPatch( 0.22f, 2.4, drive, cutEnv );
			held.BendUpSemis = 2f; held.BendUpStart = 0.18f; held.BendUpTime = 0.14f;
			t.G.RenderPatch( t.At( 4 ), t.Beat * 4, Osc.Midi( 69 ), held, mono: true );
		};
		b.Cands.Add( Today( "SHIPPED: string, barely damped, picked at 0.14, then the amp's drive 8" ) );
		// The bend is the point: on a string model it shortens the delay line, which is what a bend
		// physically is, so BENDINESS drives the real gesture rather than a pitch offset.
		b.Cands.Add( Edit( "string, into the amp", "picked at 0.15, rings 2.5 s, then the patch's own drive 8 — string first, amp second", Str( 2.5f, 0.16f, 0.15f, 0.02f, 0.05f ) ) );
		b.Cands.Add( Edit( "string, bridge pickup", "picked at 0.08: thin, nasal, cuts through — the bridge-pickup lead", Str( 2.5f, 0.12f, 0.08f, 0.015f, 0.05f ) ) );
		b.Cands.Add( Edit( "string, neck + sustain", "picked at 0.33 and barely damped: fat, long, feedbacking into the drive", Str( 4f, 0.09f, 0.33f, 0.03f, 0.03f ) ) );
		into.Add( b );
	}

	// ── KEYS ──
	// Rock's offbeat stab, full triad, which is where the voice's problem shows: three detuned
	// saws per tone through one low-pass is a wall, not a keyboard.

	static void Keys( List<Block> into )
	{
		var b = new Block
		{
			Voice = "keys", Genre = 1, Bpm = 148, Beats = 8.5,
			Figure = "two bars of offbeat triad stabs",
		};
		int[] triad = { 64, 67, 71 };
		b.Play = t =>
		{
			for ( double beat = 0.5; beat < 8; beat += 1 )
				foreach ( int m in triad )
					t.G.EmitKeys( t.Tick( beat ), t.Tick( 0.4 ), m, 1f, false, triad.Length, default );
			foreach ( int m in triad )
				t.G.EmitKeys( t.Tick( 8 ), t.Tick( 1 ), m, 1f, true, triad.Length, default );
		};
		b.Cands.Add( Today( "SHIPPED: 9 tone wheels, sustained, into drive 3.2 — a dirty organ" ) );
		// Rock's keys are a dirty organ, so: tone wheels, which do not decay, through the patch's
		// own drive. Then the two other things this one voice is asked to be in other genres.
		b.Cands.Add( Edit( "modal, tone wheels", "9 sines at whole-number ratios, no decay, into drive 3.2 — the schematic of an organ", Modal( ModalBank.Harmonic, 9, 1f, sustain: true ) ) );
		b.Cands.Add( Edit( "modal, tine", "the free-free bar roots 1/2.76/5.40/8.93: fuses into no pitch, pings like metal. Clean, as a tine is", Clean( Modal( ModalBank.Bar, 6, 1.6f ) ) ) );
		b.Cands.Add( Edit( "modal, stiff string", "16 partials sharp by sqrt(1+B.n^2), B = 4e-4 — a piano's inharmonicity", Clean( Modal( ModalBank.StiffString, 16, 1.8f, 4e-4f ) ) ) );
		into.Add( b );
	}

	// ── SKANK ──
	// The chop. 100 ms long, so this is almost entirely a question about the attack.

	static void Skank( List<Block> into )
	{
		var b = new Block
		{
			Voice = "skank", Genre = 0, Bpm = 160, Beats = 8.5,
			Figure = "two bars of offbeat chops on a rocksteady 7th voicing",
		};
		int[] tones = { 64, 68, 71, 74 };
		b.Play = t =>
		{
			for ( double beat = 0.5; beat < 8.5; beat += 1 )
				foreach ( int m in tones )
					t.G.RenderPatch( t.At( beat ), (int)(t.Beat * 0.22), Osc.Midi( m ),
						t.G.SkankPatch( 1f, tones.Length ) );
		};
		b.Cands.Add( Today( "SHIPPED: string ringing 0.42 s, cut at the SKANK CHOP window — the hand ends it" ) );
		// A SKANK IS A DAMPED CHORD — the hand mutes the strings immediately, which is the whole
		// gesture. On a string model that is one damping number; on an oscillator it was a 100 ms
		// amplitude window over a sound that was not decaying for any reason.
		b.Cands.Add( Edit( "string, chopped", "hand down at once: 70 ms, absorbent, picked at 0.3", Str( 0.07f, 0.5f, 0.3f, 0.03f, 0.05f ) ) );
		b.Cands.Add( Edit( "ring 0.9 s", "a longer one: the chop as a chord being cut rather than a click", Str( 0.9f, 0.32f, 0.3f, 0.030f, 0.05f ) ) );
		b.Cands.Add( Edit( "ring 0.42 s, bridge", "the shipped ring but a hard narrow pick near the bridge: all bite", Str( 0.42f, 0.30f, 0.16f, 0.012f, 0.08f ) ) );
		into.Add( b );
	}

	// ── ORGAN BUBBLE ──
	// A sine, today — so it is the one voice with no harmonics to shape at all, and the candidates
	// are about giving it some.

	static void Organ( List<Block> into )
	{
		var b = new Block
		{
			Voice = "organ", Genre = 0, Bpm = 160, Beats = 8.5,
			Figure = "two bars of the reggae bubble, an octave under the chop",
		};
		int[] tones = { 52, 56, 59 };
		b.Play = t =>
		{
			for ( double beat = 0.5; beat < 8.5; beat += 1 )
				foreach ( int m in tones )
					t.G.RenderPatch( t.At( beat ), (int)(t.Beat * 0.26), Osc.Midi( m ),
						t.G.OrganBubblePatch( 1f, tones.Length ) );
		};
		b.Cands.Add( Today( "SHIPPED: 4 tone wheels, sustained — a dark registration, felt under the chop" ) );
		b.Cands.Add( Edit( "modal, 4 wheels", "the bubble is a dark registration: four whole-number wheels, no decay", Modal( ModalBank.Harmonic, 4, 1f, sustain: true ) ) );
		b.Cands.Add( Edit( "modal, 8 wheels", "the full drawbar stack — brighter, more of the chord audible under the chop", Modal( ModalBank.Harmonic, 8, 1f, sustain: true ) ) );
		into.Add( b );
	}

	// ── HORN SECTION ──
	// Block stabs, panned across the section, which is how the voice is always heard.

	static void Horns( List<Block> into )
	{
		var b = new Block
		{
			Voice = "horns", Genre = 0, Bpm = 160, Beats = 8.5,
			Figure = "two bars of section stabs, spread across the three players",
		};
		int[] tones = { 64, 68, 71 };
		double[] hits = { 0, 1.5, 2.5, 4, 5.5, 7 };
		b.Play = t =>
		{
			foreach ( double beat in hits )
				for ( int k = 0; k < tones.Length; k++ )
					t.G.RenderPatch( t.At( beat ), (int)(t.Beat * 0.5), Osc.Midi( tones[k] ),
						t.G.HornPatch( 0.22, 0.5f * (k / (float)(tones.Length - 1) * 2f - 1f),
							tones.Length, 1f, 1f, 1f ) );
			for ( int k = 0; k < tones.Length; k++ )
				t.G.RenderPatch( t.At( 8 ), t.Beat, Osc.Midi( tones[k] ),
					t.G.HornPatch( 0.6, 0.5f * (k / (float)(tones.Length - 1) * 2f - 1f),
						tones.Length, 1f, 1f, 1f ) );
		};
		b.Cands.Add( Today( "SHIPPED: FM brass to the 8th, index to 45% in 80 ms, still three players" ) );
		// Chowning's brass, and the three detuned players kept (see the unison note in Models.cs).
		// The top harmonic is the thing stated; the index follows from it.
		b.Cands.Add( Edit( "fm brass, 8 harmonics", "ratio 1, sidebands to the 8th, index falling to 45% in 80 ms — the stab's blat", Fm( 1f, 8f, 0.45f, 0.08f ) ) );
		b.Cands.Add( Edit( "fm brass, 12 harmonics", "pushed harder: brighter attack, same section", Fm( 1f, 12f, 0.40f, 0.09f ) ) );
		b.Cands.Add( Edit( "fm brass, soft", "to the 5th and holding 70%: a section playing under a vocal", Fm( 1f, 5f, 0.70f, 0.10f ) ) );
		into.Add( b );
	}

	// ── SKA LEAD: SAX ──

	static void Sax( List<Block> into )
	{
		var b = new Block
		{
			Voice = "sax", Genre = 0, Bpm = 160, Beats = 8.5, Instrument = (int)1,
			Figure = "an eight-note ska lead line, last note held",
		};
		b.Play = t => LeadLine( t, 1 );
		b.Cands.Add( Today( "SHIPPED: FM reed to the 7th with 0.35 feedback, breath noise kept" ) );
		// A reed is a pressure-controlled valve and buzzes rather than blats, so the carrier gets
		// feedback — which pushes an FM spectrum toward a saw and is the cheapest honest stand-in
		// for the reed's own hard non-linearity. The patch keeps its breath noise either way.
		b.Cands.Add( Edit( "fm reed", "ratio 1 to the 7th with 0.35 feedback: the buzz, not the blat", Fm( 1f, 7f, 0.6f, 0.12f, 0.35f ) ) );
		b.Cands.Add( Edit( "fm reed, hard", "to the 11th and more feedback — pushed, edge-of-squawk", Fm( 1f, 11f, 0.55f, 0.10f, 0.55f ) ) );
		b.Cands.Add( Edit( "fm reed, subtone", "to the 4th, holding 80%: breathy and soft, the ballad tone", Fm( 1f, 4f, 0.8f, 0.14f, 0.2f ) ) );
		into.Add( b );
	}

	// ── SKA LEAD: TRUMPET ──

	static void Trumpet( List<Block> into )
	{
		var b = new Block
		{
			Voice = "trumpet", Genre = 0, Bpm = 160, Beats = 8.5, Instrument = 0,
			Figure = "the same eight-note line, on the trumpet voice",
		};
		b.Play = t => LeadLine( t, 0 );
		b.Cands.Add( Today( "SHIPPED: FM brass to the 10th, index to 40% in 60 ms" ) );
		b.Cands.Add( Edit( "fm brass, lead", "ratio 1 to the 10th, index falling to 40% in 60 ms — a solo trumpet's attack", Fm( 1f, 10f, 0.40f, 0.06f ) ) );
		b.Cands.Add( Edit( "fm brass, 2:1", "the modulator an octave up: only odd-ish sidebands reinforced, more muted", Fm( 2f, 10f, 0.45f, 0.07f ) ) );
		b.Cands.Add( Edit( "fm brass, open", "to the 16th: the top of a lead line, blown wide open", Fm( 1f, 16f, 0.35f, 0.07f ) ) );
		into.Add( b );
	}

	/// <summary>The one melodic line both ska leads play, so the two blocks differ only by voice.
	/// </summary>
	static void LeadLine( Take t, int instrument )
	{
		int[] line = { 64, 67, 71, 72, 71, 67, 64, 62 };
		for ( int i = 0; i < line.Length; i++ )
		{
			var p = t.G.LeadHornPatch( (Instrument)instrument, 0.20f, 0.45, 1.4f, out int shift );
			t.G.RenderPatch( t.At( i * 0.5 ), (int)(t.Beat * 0.46), Osc.Midi( line[i] + shift ), p, mono: true );
		}
		var held = t.G.LeadHornPatch( (Instrument)instrument, 0.20f, 2.4, 1.4f, out int hs );
		held.Vibrato = 5f; held.VibDepth = 0.004f;
		t.G.RenderPatch( t.At( 4.5 ), t.Beat * 4, Osc.Midi( 64 + hs ), held, mono: true );
	}

	// ── Output ──

	/// <summary>m:ss.s — to the tenth, because a line is four seconds long and the thing being
	/// listened for is often one note inside it. It is also what lets tools/spectool or a DFT be
	/// pointed at an exact onset: a floored second lands in the gap as often as in the note.
	/// </summary>
	static string Stamp( int samples )
	{
		double total = samples / (double)Rate;
		return $"{(int)total / 60}:{total % 60:00.0}";
	}

	static void Write( string wavPath, string txtPath, List<float> L, List<float> R, string script )
	{
		// ONE gain over the whole file — see the class note.
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
		Console.WriteLine( $"wav  {wavPath}  ({Stamp( L.Count )}, one gain of {k:0.000} over the file)" );
		Console.WriteLine( $"txt  {txtPath}" );
	}
}
