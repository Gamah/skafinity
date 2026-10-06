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
		script.AppendLine( "TONE AUDITION — skafinity pitched voices, round 1: FOUR MECHANISMS" );
		script.AppendLine();
		script.AppendLine( "Every voice below is, today, a detuned saw through one low-pass. Each block" );
		script.AppendLine( "plays ONE figure once per candidate. The candidates are the same four" );
		script.AppendLine( "mechanisms throughout, applied where the real instrument has the property:" );
		script.AppendLine();
		script.AppendLine( "  PLUCK   where the string was struck. A string displaced at a fraction b of" );
		script.AppendLine( "          its length cannot excite a mode with a node there, so harmonic n" );
		script.AppendLine( "          arrives scaled by sin(pi.n.b) — a comb of NULLS inside the" );
		script.AppendLine( "          spectrum. No cutoff is a position: a low-pass cannot remove the" );
		script.AppendLine( "          4th harmonic and keep the 5th." );
		script.AppendLine( "  DUTY    pulse width. Harmonics go as |sin(pi.n.d)|/n, so d = 1/k deletes" );
		script.AppendLine( "          every kth. Swept (PWM) the nulls move while the pitch does not." );
		script.AppendLine( "  CUTSEC  brightness decaying on its OWN clock. Damping grows with mode" );
		script.AppendLine( "          number, so a real body goes dull faster than it goes quiet; today" );
		script.AppendLine( "          the two share one time constant, which reads as a filter sweep." );
		script.AppendLine( "  BLOOM   drive riding the envelope. A non-linearity's harmonic ladder" );
		script.AppendLine( "          steepens with level, so LOUD is BRIGHT. A fixed drive cannot do" );
		script.AppendLine( "          it, and it is the defining behaviour of a blown horn." );
		script.AppendLine();
		script.AppendLine( "Dry: no master bus, no reverb, no normalize per line, double-tracking off." );
		script.AppendLine( "One gain over the whole file, so levels between lines mean something." );
		script.AppendLine( "--tone bass|rhythmgtr|leadgtr|keys|skank|organ|horns|sax|trumpet for one." );

		int gap = (int)(Rate * GapSec);
		int n = 0;
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
				L.AddRange( bl ); R.AddRange( br );
				for ( int s = 0; s < gap; s++ ) { L.Add( 0f ); R.Add( 0f ); }
			}
		}

		script.AppendLine();
		script.AppendLine( $"{n} lines, {Stamp( L.Count )} total." );
		Write( wavPath, txtPath, L, R, script.ToString() );
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
		b.Cands.Add( Today( "triangle body + square sub, low-pass at 380 Hz" ) );
		b.Cands.Add( Edit( "pluck 0.20", "plucked a fifth up from the bridge: 5th harmonic gone, 2nd/3rd kept — growl", Pluck( 0.20f ) ) );
		b.Cands.Add( Edit( "pluck 0.50", "centre pluck: NO even harmonics at all — round, dub", Pluck( 0.50f ) ) );
		b.Cands.Add( Edit( "pluck 0.20 + cutsec 50ms", "and the brightness dies in 50 ms while the note holds — the pick, not a sweep",
			Then( Pluck( 0.20f ), CutSec( 0.05f ) ) ) );
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
		b.Cands.Add( Today( "saw, low-pass 2600 Hz, fixed tanh drive" ) );
		// A pick crosses a guitar string about a quarter of the way up from the bridge; 0.25 nulls
		// the 4th, 8th, 12th, which is the hollow in a strummed electric. And a driven amp is a
		// non-linearity, so the chord's brightness should rise and fall with the strum's envelope.
		b.Cands.Add( Edit( "pluck 0.25", "picked a quarter up: 4th/8th/12th gone — the hollow in a strummed electric", Pluck( 0.25f ) ) );
		b.Cands.Add( Edit( "bloom 0.8", "the amp brightens with the strum and dulls as it decays", Bloom( 0.8f ) ) );
		b.Cands.Add( Edit( "pluck 0.25 + bloom 0.8", "both: a picked string into a valve amp", Then( Pluck( 0.25f ), Bloom( 0.8f ) ) ) );
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
		b.Cands.Add( Today( "saw, one voice, drive 8, cutoff snap 2200 Hz" ) );
		b.Cands.Add( Edit( "bloom 1.2", "the one thing a fixed drive cannot do: the bend gets brighter as it is pushed", Bloom( 1.2f ) ) );
		b.Cands.Add( Edit( "pluck 0.12 + bloom 1.2", "picked hard by the bridge — thin and cutting — into an amp that blooms",
			Then( Pluck( 0.12f ), Bloom( 1.2f ) ) ) );
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
		b.Cands.Add( Today( "saw, low-pass 1700 Hz, drive 3.2" ) );
		// A drawbar organ's tone wheels are sines at whole-number ratios and a reed's bore is a
		// pulse; both are duties, and neither is a saw. 1/3 deletes every third harmonic, which is
		// the hollow an organ has and a saw cannot be filtered into.
		b.Cands.Add( Edit( "duty 1/3", "every 3rd harmonic deleted — hollow, drawbar-ish; no cutoff can do this", Pulse( 1f / 3f ) ) );
		b.Cands.Add( Edit( "duty 1/3 + PWM", "and the nulls sweep up as the stab decays, at constant pitch", Pulse( 1f / 3f, 0.14f ) ) );
		b.Cands.Add( Edit( "duty 1/5 + PWM", "narrower: nulls at every 5th, brighter and reedier", Pulse( 0.2f, 0.12f ) ) );
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
		b.Cands.Add( Today( "saw, high-pass 500, low-pass 3000, cutoff snap 1500" ) );
		b.Cands.Add( Edit( "pluck 0.25", "the pick position, which is most of what a chop's bite IS", Pluck( 0.25f ) ) );
		b.Cands.Add( Edit( "pluck 0.25 + cutsec 25ms", "and the bite gone in 25 ms, inside a 100 ms chop",
			Then( Pluck( 0.25f ), CutSec( 0.025f ) ) ) );
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
		b.Cands.Add( Today( "sine, low-pass 1400 Hz, vibrato" ) );
		b.Cands.Add( Edit( "duty 1/2", "a square: odd harmonics only, which is what a stopped pipe radiates", Pulse( 0.5f ) ) );
		b.Cands.Add( Edit( "duty 1/4", "nulls at every 4th — the 2nd and 3rd drawbars without the 4th", Pulse( 0.25f ) ) );
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
		b.Cands.Add( Today( "saw x3 detuned, low-pass 3200, cutoff snap 1200, fixed drive" ) );
		// THE REASONED CANDIDATE OF THE WHOLE ROUND. A brass instrument is a sine-ish standing wave
		// driven through the player's lips, which are a non-linear valve: the harmonics are MADE by
		// that non-linearity, and its ladder steepens with blowing pressure. So brass = sine into a
		// waveshaper whose drive tracks the envelope, and the saw is standing in for the result of
		// a process the engine can simply run instead. A saw already has every harmonic, so driving
		// one harder only compresses it — which is why a loud stab today is louder and not brighter.
		b.Cands.Add( Edit( "bloom 1.2", "saw, but the drive rides the envelope: loud becomes bright", Bloom( 1.2f ) ) );
		b.Cands.Add( Edit( "sine + bloom 2.5", "the actual model: the harmonics MADE by the lips, and more of them when blown harder", Bloom( 2.5f, osc: 0 ) ) );
		b.Cands.Add( Edit( "sine + bloom 4", "the same, blown hard", Bloom( 4f, osc: 0 ) ) );
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
		b.Cands.Add( Today( "triangle x2, breath noise, low-pass 3200, cutoff snap 1400" ) );
		// A reed instrument's bore is a pressure-controlled valve and its waveform is close to a
		// pulse whose width is the reed's open fraction. A saxophone's bore is CONICAL, so it
		// sounds every harmonic and wants a duty off 1/2; a clarinet's is cylindrical and sounds
		// only the odd ones, which is a duty of exactly 1/2. That distinction is the duty knob and
		// nothing else in this engine can express it.
		b.Cands.Add( Edit( "duty 0.4", "the reed's open fraction: conical bore, every harmonic, weak 5th", Pulse( 0.4f ) ) );
		b.Cands.Add( Edit( "duty 0.4 + PWM", "and the reed opening further as the note is pushed", Pulse( 0.4f, 0.1f ) ) );
		b.Cands.Add( Edit( "duty 0.4 + bloom 1.5", "blown harder is brighter, as on the horns", Then( Pulse( 0.4f ), Bloom( 1.5f ) ) ) );
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
		b.Cands.Add( Today( "saw x3 detuned, low-pass 3200, cutoff snap 1800" ) );
		b.Cands.Add( Edit( "bloom 1.5", "saw, drive riding the envelope", Bloom( 1.5f ) ) );
		b.Cands.Add( Edit( "sine + bloom 3", "the lips model, as on the section", Bloom( 3f, osc: 0 ) ) );
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
