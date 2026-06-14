using System;

namespace Rotaliate.Audio;

/// <summary>
/// Deterministic procedural ska / reggae-rock track generator (issue: procedural music).
///
/// A player's 8-hex <c>player_tag</c> seeds a portable PRNG (xmur3 → mulberry32);
/// the PRNG drives every musical choice (tempo, key, progression, bass / skank /
/// organ / lead / drum patterns) so the SAME tag always produces the SAME song.
/// Output is interleaved stereo 16-bit PCM (for SoundStream) or a WAV (debug).
///
/// PORTABILITY (web parity): the *composition* is whatever the PRNG emits — a web
/// client reproduces the same arrangement by mirroring xmur3 + mulberry32 and this
/// file's note-selection order, using the same <see cref="Config"/> values. Synthesis
/// (oscillators/filters below) only needs matching for bit-identical audio.
///
/// Synthesis: subtractive — unison-detuned oscillators through a resonant low-pass
/// state-variable filter with a cutoff envelope (warm, not "8-bit"); full synth drum
/// kit (kick/snare/toms/hats/crash + fills). Default voicing aims for a Sublime vibe:
/// laid-back reggae-rock tempo, bass-forward, prominent clean skank + organ bubble.
/// </summary>
public sealed class MusicGen
{
	public sealed class Config
	{
		// Output
		public int SampleRate = 32000;
		public float TargetSeconds = 80f; // bar count adapts to tempo to hit this
		public int Bars = 64;             // fallback if TargetSeconds <= 0

		// Tempo — main is laid-back reggae-rock; Fast is an uptempo ska feel.
		public int BpmMin = 86, BpmMax = 104;
		public float FastChance = 0.30f;
		public int FastBpmMin = 150, FastBpmMax = 168;
		public float Swing = 0.14f, FastSwing = 0.05f;

		// Mix (per-voice gain pre-master)
		public float BassVol = 0.68f;
		public float SkankVol = 0.95f;
		public float OrganVol = 0.42f;
		public float MelodyVol = 0.34f;
		public float HornVol = 0.22f;
		public float KickVol = 1.00f;
		public float SnareVol = 0.70f;
		public float TomVol = 0.60f;
		public float HatVol = 0.22f;
		public float CrashVol = 0.35f;

		// Tone — low drives + filtering for warmth; detune for width.
		public float Detune = 14f;        // cents, unison spread
		public float BassCutoff = 380f;   // Hz low-pass on bass
		public float SkankCutoff = 3000f; // Hz low-pass on skank
		public float SkankHighpass = 500f;// Hz high-pass to thin the skank
		public float LeadCutoff = 3200f;  // Hz low-pass on lead/horns
		public float Resonance = 1.0f;    // SVF damping (lower = more resonant)
		public float BassDrive = 1.5f;
		public float SkankDrive = 1.3f;
		public float MelodyDrive = 1.3f;
		public float HornDrive = 1.4f;
		public float MasterDrive = 1.1f;
		public float MasterPeak = 0.95f;

		// Feel
		public float OctavePopChance = 0.30f;
		public float OrganBubbleChance = 0.55f;
		public float KickSyncChance = 0.25f;
		public float GhostSnareChance = 0.35f;
		public float FillChance = 0.6f;       // drum fill at phrase ends
		public float DrumBusy = 0.6f;         // 0..1 overall kit activity: 16th hats, ghosts, kick syncopation
		public float TripletChance = 0.06f;   // 0..0.2 chance of triplet/16th ornament in fills / lead runs (potent)
		public float MelodyRestChance = 0.30f;
		public float MelodyLeapChance = 0.18f;
		public float MelodyVibrato = 5.0f;

		// Stereo — how far off-center the lead sits; horns spread around it.
		public float PanAmount = 0.4f;

		// Lead instrument weights (RNG picks one; ForceInstrument overrides:
		// -1 = RNG, 0=Trumpet 1=Sax 2=Organ 3=Trombone).
		public float TrumpetWeight = 1.0f;
		public float SaxWeight = 1.0f;
		public float OrganWeight = 0.8f;
		public float TromboneWeight = 0.4f;
		public int ForceInstrument = -1;

		// Backing horn section
		public float HornSectionChance = 0.5f;
		public float HornDensity = 0.35f;
	}

	readonly Config _c;
	readonly int _sr;
	float[] _bufL, _bufR;

	MusicGen( Config c ) { _c = c ?? new Config(); _sr = _c.SampleRate; }

	public const int Channels = 2;

	public static byte[] Generate( string tag, Config cfg = null )
	{
		var g = new MusicGen( cfg );
		return g.EncodeWav( g.Compose( tag ) );
	}

	public static short[] GenerateSamples( string tag, Config cfg, out int sampleRate )
	{
		var g = new MusicGen( cfg );
		float gain = g.Compose( tag );
		sampleRate = g._sr;
		return g.ToShorts( gain );
	}

	const int EighthsPerBar = 8;

	// ── Portable PRNG (mirror exactly in the web client) ──
	static uint Xmur3( string str )
	{
		uint h = 1779033703u ^ (uint)str.Length;
		for ( int i = 0; i < str.Length; i++ )
		{
			h = unchecked( (h ^ str[i]) * 3432918353u );
			h = (h << 13) | (h >> 19);
		}
		h = unchecked( (h ^ (h >> 16)) * 2246822507u );
		h = unchecked( (h ^ (h >> 13)) * 3266489909u );
		return h ^ (h >> 16);
	}

	sealed class Rng
	{
		uint _a;
		public Rng( uint seed ) { _a = seed; }
		public float Next()
		{
			_a = unchecked( _a + 0x6D2B79F5u );
			uint t = _a;
			t = unchecked( (t ^ (t >> 15)) * (t | 1u) );
			t ^= unchecked( t + (t ^ (t >> 7)) * (t | 61u) );
			return (t ^ (t >> 14)) / 4294967296f;
		}
		public int Int( int n ) => n <= 0 ? 0 : Math.Min( n - 1, (int)(Next() * n) );
		public bool Chance( float p ) => Next() < p;
		public T Pick<T>( T[] arr ) => arr[Int( arr.Length )];
	}

	// ── Harmony ──
	// Major-leaning scales (Sublime / reggae sit in major & mixolydian mostly).
	static readonly int[][] Scales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted twice)
		new[] { 0, 2, 3, 5, 7, 9, 10 }, // dorian
	};

	static readonly int[][] Progressions =
	{
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV
		new[] { 0, 3, 4, 4 }, // I–IV–V–V
		new[] { 0, 5, 3, 4 }, // I–vi–IV–V
		new[] { 0, 6, 3, 0 }, // I–bVII–IV–I (mixolydian)
		new[] { 5, 3, 0, 4 }, // vi–IV–I–V
		new[] { 0, 3, 0, 4 }, // I–IV–I–V
	};

	// Bass patterns: semitone offsets from the chord root per eighth; -99 = rest
	// (note sustains to the next onset). Slot 7 is the "approach" → walks to the
	// next chord. Mix of melodic / one-drop / rocking / busy ska.
	const int Rest = -99, Approach = 99;
	static readonly int[][] BassPatterns =
	{
		new[] { 0, Rest, 0, 12, Rest, 7, 5, Approach },   // sublime melodic
		new[] { Rest, Rest, 0, Rest, 0, Rest, 7, Approach }, // one-drop spacey
		new[] { 0, Rest, 7, Rest, 12, Rest, 7, Approach },   // rocking
		new[] { 0, 12, 0, 7, 0, 12, 7, Approach },           // busy ska
		new[] { 0, Rest, 0, Rest, 5, Rest, 7, Approach },    // root–fifth
	};

	int[] _scale, _prog;
	int _rootMidi;
	Instrument _lead;
	float _leadPan;
	bool _hasHorns;
	bool[] _hornMask;
	int[] _bassPat;
	int _drumStyle;          // 0 one-drop, 1 steppers, 2 straight backbeat
	bool _organBubble;
	bool _fast;

	float Compose( string tag )
	{
		var rng = new Rng( Xmur3( string.IsNullOrEmpty( tag ) ? "rotaliate" : tag.ToLowerInvariant() ) );

		_fast = rng.Chance( _c.FastChance );
		int bpm = _fast
			? _c.FastBpmMin + rng.Int( Math.Max( 1, _c.FastBpmMax - _c.FastBpmMin + 1 ) )
			: _c.BpmMin + rng.Int( Math.Max( 1, _c.BpmMax - _c.BpmMin + 1 ) );
		_scale = rng.Pick( Scales );
		_prog = rng.Pick( Progressions );
		_rootMidi = 28 + rng.Int( 8 );                    // E1..B1 bass root
		_lead = PickInstrument( rng );
		_leadPan = (rng.Next() * 2f - 1f) * _c.PanAmount;
		_bassPat = rng.Pick( BassPatterns );
		_drumStyle = _fast ? 2 : rng.Int( 2 );            // laid-back → one-drop/steppers
		_organBubble = rng.Chance( _c.OrganBubbleChance );

		_hasHorns = rng.Chance( _c.HornSectionChance );
		_hornMask = new bool[EighthsPerBar];
		if ( _hasHorns )
		{
			_hornMask[0] = true;
			for ( int e = 1; e < EighthsPerBar; e++ )
				_hornMask[e] = rng.Chance( _c.HornDensity * (e % 2 == 1 ? 1.3f : 0.5f) );
		}

		float swing = _fast ? _c.FastSwing : _c.Swing;
		double secPerEighth = 60.0 / bpm / 2.0;
		int spe = (int)Math.Round( _sr * secPerEighth );

		// Adapt bar count to the tempo so length stays ~TargetSeconds (in spec, and
		// bounds file size). Round to a multiple of 8 so it lands on a progression
		// boundary and loops cleanly.
		int barCount = Math.Max( 1, _c.Bars );
		if ( _c.TargetSeconds > 1f )
		{
			double barSec = EighthsPerBar * secPerEighth;
			barCount = Math.Clamp( (int)Math.Round( _c.TargetSeconds / barSec / 8.0 ) * 8, 16, 128 );
		}

		int total = spe * EighthsPerBar * barCount;
		_bufL = new float[total];
		_bufR = new float[total];
		var noise = new Rng( Xmur3( "drums:" + tag ) );

		for ( int bar = 0; bar < barCount; bar++ )
		{
			int chord = (bar / 2) % _prog.Length;
			int nextChord = ((bar / 2) + 1) % _prog.Length;
			int barStart = bar * EighthsPerBar * spe;
			bool phraseEnd = (bar % 4) == 3;              // fill the 4th bar

			RenderBassBar( barStart, spe, secPerEighth, chord, nextChord, rng );
			RenderRhythmBar( barStart, spe, secPerEighth, chord, swing, rng );
			RenderDrumBar( barStart, spe, chord, phraseEnd, rng, noise );

			if ( bar % 2 == 0 )
				RenderLeadPhrase( barStart, spe, secPerEighth, chord, rng );
			if ( _hasHorns )
				RenderHornStabs( barStart, spe, secPerEighth, chord );
		}

		// Master: gentle soft-clip + normalize
		float peak = 0f;
		for ( int i = 0; i < total; i++ )
		{
			float l = (float)Math.Tanh( _bufL[i] * _c.MasterDrive );
			float r = (float)Math.Tanh( _bufR[i] * _c.MasterDrive );
			_bufL[i] = l; _bufR[i] = r;
			float a = Math.Max( MathF.Abs( l ), MathF.Abs( r ) );
			if ( a > peak ) peak = a;
		}
		return peak > 0.0001f ? _c.MasterPeak / peak : 1f;
	}

	int ScaleMidi( int baseMidi, int degree )
	{
		int len = _scale.Length;
		int oct = (int)Math.Floor( degree / (double)len );
		return baseMidi + _scale[degree - oct * len] + 12 * oct;
	}
	int ChordRoot( int c ) => ScaleMidi( _rootMidi, _prog[c] );

	// ── Bass ──
	void RenderBassBar( int barStart, int spe, double secPerEighth, int chord, int nextChord, Rng rng )
	{
		int root = ChordRoot( chord );
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			int off = _bassPat[e];
			if ( off == Rest ) continue;

			int midi;
			if ( off == Approach )
			{
				int target = ChordRoot( nextChord );
				midi = target - (rng.Chance( 0.5f ) ? 1 : 2); // chromatic/step lead-in
			}
			else
			{
				midi = root + off;
				if ( off == 0 && e > 0 && rng.Chance( _c.OctavePopChance ) ) midi += 12;
			}

			// note runs until the next onset (legato reggae feel)
			int len = 1;
			while ( e + len < EighthsPerBar && _bassPat[e + len] == Rest ) len++;
			int dur = (int)(spe * len * 0.95f);

			RenderPatch( barStart + e * spe, dur, Midi( midi ), new Patch
			{
				Osc = 1, Voices = 2, Detune = _c.Detune * 0.4f,
				Amp = _c.BassVol, Attack = 0.004f, Decay = secPerEighth * len * 0.8,
				Sustain = 0.55f, Sustained = true,
				Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
				Drive = _c.BassDrive, Pan = 0f,
			} );
		}
	}

	// ── Skank guitar (the signature) + reggae organ bubble — offbeats, centered ──
	void RenderRhythmBar( int barStart, int spe, double secPerEighth, int chord, float swing, Rng rng )
	{
		int gBase = _rootMidi + 12;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 7 };

		for ( int e = 1; e < EighthsPerBar; e += 2 ) // offbeats
		{
			int at = barStart + e * spe + (int)(swing * spe);

			// bright, thin, short guitar chop
			foreach ( var d in degs )
				RenderPatch( at, (int)(spe * 0.5f), Midi( ScaleMidi( gBase, d ) ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune,
					Amp = _c.SkankVol / degs.Length, Attack = 0.002f, Decay = 0.10,
					Sustain = 0f, Sustained = false,
					Cutoff = _c.SkankCutoff, CutEnv = 1500f, Reso = 0.8f,
					Highpass = _c.SkankHighpass, Drive = _c.SkankDrive, Pan = 0f,
				} );

			// reggae organ "bubble": a softer, rounder offbeat under the guitar
			if ( _organBubble )
				foreach ( var d in degs )
					RenderPatch( at, (int)(spe * 0.55f), Midi( ScaleMidi( gBase, d ) - 12 ), new Patch
					{
						Osc = 0, Voices = 2, Detune = _c.Detune * 0.5f,
						Amp = _c.OrganVol / degs.Length, Attack = 0.004f, Decay = 0.16,
						Sustain = 0.3f, Sustained = false,
						Cutoff = 1400f, CutEnv = 0f, Reso = 1.0f, Drive = 1.1f, Pan = 0f,
						Vibrato = 5.5f,
					} );
		}
	}

	// ── Lead melody (chord-tone locked → consonant) ──
	void RenderLeadPhrase( int barStart, int spe, double secPerEighth, int chord, Rng rng )
	{
		int slots = EighthsPerBar * 2;
		int melBase = _rootMidi + 24;
		int[] tones = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 6 }; // chord tones
		int degree = tones[rng.Int( 3 )];
		float amp = _c.MelodyVol;
		float drive = _c.MelodyDrive;

		int e = 0;
		while ( e < slots )
		{
			if ( rng.Chance( _c.MelodyRestChance ) ) { e++; continue; }

			// ornament: a sixteenth pair, or a triplet at one of three rates — a tight
			// 16th-triplet (3 in an eighth), an eighth-note triplet (3 in a beat), or a
			// wide quarter-note triplet (3 over two beats). Wider spans give the lazy,
			// over-the-barline triplet feel, not just the fast run.
			if ( rng.Chance( _c.TripletChance ) )
			{
				float r = rng.Next();
				int n, spanE; // n notes evenly across spanE eighths
				if ( r < 0.25f ) { n = 2; spanE = 1; }       // sixteenth pair
				else if ( r < 0.50f ) { n = 3; spanE = 1; }  // 16th-note triplet
				else if ( r < 0.80f ) { n = 3; spanE = 2; }  // eighth-note triplet (1 beat)
				else { n = 3; spanE = 4; }                   // quarter-note triplet (2 beats)
				if ( e + spanE > slots ) spanE = 1;

				int span = spanE * spe;
				int step = span / n;
				for ( int k = 0; k < n; k++ )
				{
					int d2 = Math.Clamp( degree + (k - n / 2), _prog[chord] - 3, _prog[chord] + 10 );
					RenderLead( barStart + e * spe + k * step, (int)(step * 0.9f),
						ScaleMidi( melBase, d2 ), amp, secPerEighth * spanE / (double)n * 0.85, drive );
				}
				e += spanE;
				continue;
			}

			int len = 1 + rng.Int( 3 );
			if ( e + len > slots ) len = slots - e;
			bool strong = (e % 2) == 0;

			if ( strong )
			{
				// land on a chord tone near the current degree
				int best = tones[0], bestD = 999;
				foreach ( var t in tones )
				{
					for ( int oc = -7; oc <= 14; oc += 7 )
					{
						int cand = t + (oc / 7) * 7; // keep in degree space
						int dist = Math.Abs( cand - degree );
						if ( dist < bestD ) { bestD = dist; best = cand; }
					}
				}
				degree = best;
			}
			else
			{
				int step = rng.Chance( _c.MelodyLeapChance ) ? (rng.Chance( 0.5f ) ? 3 : -3) : (rng.Chance( 0.5f ) ? 1 : -1);
				degree = Math.Clamp( degree + step, _prog[chord] - 3, _prog[chord] + 10 );
			}

			RenderLead( barStart + e * spe, (int)(spe * len * 0.9f), ScaleMidi( melBase, degree ),
				amp, secPerEighth * len * 0.7f, drive );
			e += len;
		}
	}

	// ── Backing horns (panned spread) ──
	void RenderHornStabs( int barStart, int spe, double secPerEighth, int chord )
	{
		int baseMidi = _rootMidi + 19;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4 };
		float spread = _c.PanAmount * 0.7f;
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			if ( !_hornMask[e] ) continue;
			int at = barStart + e * spe;
			for ( int k = 0; k < degs.Length; k++ )
				RenderPatch( at, (int)(spe * 0.6f), Midi( ScaleMidi( baseMidi, degs[k] ) ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune,
					Amp = _c.HornVol / degs.Length, Attack = 0.008f, Decay = 0.22,
					Sustain = 0.2f, Sustained = false,
					Cutoff = _c.LeadCutoff, CutEnv = 1200f, Reso = 1.0f,
					Drive = _c.HornDrive, Pan = spread * (k / (float)(degs.Length - 1) * 2f - 1f),
				} );
		}
	}

	// ── Lead instrument voices ──
	enum Instrument { Trumpet, Sax, Organ, Trombone }

	Instrument PickInstrument( Rng rng )
	{
		if ( _c.ForceInstrument >= 0 && _c.ForceInstrument <= 3 ) return (Instrument)_c.ForceInstrument;
		float tw = MathF.Max( 0f, _c.TrumpetWeight ), sw = MathF.Max( 0f, _c.SaxWeight );
		float ow = MathF.Max( 0f, _c.OrganWeight ), bw = MathF.Max( 0f, _c.TromboneWeight );
		float sum = tw + sw + ow + bw;
		if ( sum <= 0f ) return Instrument.Trumpet;
		float r = rng.Next() * sum;
		if ( (r -= tw) < 0f ) return Instrument.Trumpet;
		if ( (r -= sw) < 0f ) return Instrument.Sax;
		if ( (r -= ow) < 0f ) return Instrument.Organ;
		return Instrument.Trombone;
	}

	void RenderLead( int at, int dur, int midi, float amp, double decaySec, float drive )
	{
		switch ( _lead )
		{
			case Instrument.Trumpet:
				RenderPatch( at, dur, Midi( midi ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune * 0.7f, Amp = amp,
					Attack = 0.01f, Decay = decaySec, Sustain = 0.7f, Sustained = true,
					Cutoff = _c.LeadCutoff, CutEnv = 1800f, Reso = 1.0f, Drive = drive,
					Pan = _leadPan, Vibrato = _c.MelodyVibrato,
				} );
				break;
			case Instrument.Trombone:
				RenderPatch( at, dur, Midi( midi - 12 ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune * 0.7f, Amp = amp * 1.1f,
					Attack = 0.02f, Decay = decaySec, Sustain = 0.7f, Sustained = true,
					Cutoff = _c.LeadCutoff * 0.7f, CutEnv = 900f, Reso = 1.0f, Drive = MathF.Max( 1f, drive * 0.8f ),
					Pan = _leadPan, Vibrato = _c.MelodyVibrato * 0.7f,
				} );
				break;
			case Instrument.Sax:
				RenderPatch( at, dur, Midi( midi ), new Patch
				{
					Osc = 3, Voices = 2, Detune = _c.Detune * 0.5f, Amp = amp * 1.15f,
					Attack = 0.014f, Decay = decaySec, Sustain = 0.75f, Sustained = true,
					Cutoff = _c.LeadCutoff, CutEnv = 1400f, Reso = 0.7f, Drive = MathF.Max( 1.2f, drive ),
					Pan = _leadPan, Vibrato = _c.MelodyVibrato, Breath = 0.03f,
				} );
				break;
			case Instrument.Organ:
				RenderPatch( at, dur, Midi( midi ), new Patch
				{
					Osc = 0, Voices = 3, Detune = _c.Detune * 0.6f, Amp = amp,
					Attack = 0.006f, Decay = decaySec * 1.5, Sustain = 0.9f, Sustained = true,
					Cutoff = 2600f, CutEnv = 0f, Reso = 1.0f, Drive = 1.15f,
					Pan = _leadPan, Vibrato = _c.MelodyVibrato * 0.9f,
				} );
				break;
		}
	}

	// ── Synth core: unison osc → optional high-pass → resonant low-pass (cutoff
	//    envelope) → soft drive → AD/sustain amp env. ──
	struct Patch
	{
		public int Osc;        // 0 sine 1 saw 2 square 3 triangle
		public int Voices;
		public float Detune;   // cents
		public float Amp;
		public float Attack;   // sec
		public double Decay;   // sec (exp time constant)
		public float Sustain;  // 0..1 (only if Sustained)
		public bool Sustained;
		public float Cutoff;   // Hz low-pass
		public float CutEnv;   // Hz added at attack, decays with Decay
		public float Reso;     // SVF damping (lower = more resonance)
		public float Highpass; // Hz one-pole high-pass (0 = off)
		public float Drive;    // tanh
		public float Pan;      // -1..1
		public float Vibrato;  // Hz
		public float Breath;   // 0..1 noise mix (reeds)
	}

	// Reusable per-note oscillator state (single-threaded render → no reentrancy).
	readonly double[] _ph = new double[8];
	readonly double[] _inc = new double[8];

	void RenderPatch( int start, int dur, float freq, Patch p )
	{
		if ( start < 0 || dur <= 0 || p.Voices < 1 ) return;
		StereoGains( p.Pan, out float gL, out float gR );
		int atk = Math.Max( 1, (int)(p.Attack * _sr) );
		double decSamp = Math.Max( 1.0, p.Decay * _sr );
		int rel = Math.Max( 1, (int)(0.006f * _sr) );
		int voices = Math.Min( 8, p.Voices );

		var ph = _ph; var inc = _inc;
		for ( int v = 0; v < voices; v++ )
		{
			ph[v] = 0;
			float cents = voices == 1 ? 0f : (v - (voices - 1) * 0.5f) * p.Detune;
			inc[v] = freq * Math.Pow( 2, cents / 1200.0 ) / _sr;
		}

		float low = 0, band = 0;
		float reso = Math.Clamp( p.Reso, 0.2f, 2f );
		float dnorm = p.Drive > 1f ? 1f / (float)Math.Tanh( p.Drive ) : 1f;
		float hpA = p.Highpass > 0f ? (float)(1.0 / (1.0 + 2 * Math.PI * p.Highpass / _sr)) : 0f;
		float hpInPrev = 0f, hpOutPrev = 0f;
		uint bn = 0x9E3779B9u;

		int end = Math.Min( _bufL.Length, start + dur );
		int relStart = dur - rel;
		for ( int i = 0; start + i < end; i++ )
		{
			float env;
			if ( i < atk ) env = (float)i / atk;
			else if ( p.Sustained )
			{
				float d = (float)Math.Exp( -(i - atk) / decSamp );
				env = p.Sustain + (1f - p.Sustain) * d;
			}
			else env = (float)Math.Exp( -(i - atk) / decSamp );
			if ( i >= relStart ) env *= Math.Max( 0f, (float)(dur - i) / rel );
			if ( env < 0.0006f && i > atk && !p.Sustained ) break;

			float s = 0f;
			float vib = p.Vibrato > 0f ? (float)(1.0 + 0.005 * Math.Sin( i / (double)_sr * p.Vibrato * 2 * Math.PI )) : 1f;
			for ( int v = 0; v < voices; v++ )
			{
				s += Osc( p.Osc, ph[v] - Math.Floor( ph[v] ) );
				ph[v] += inc[v] * vib;
			}
			s /= voices;
			if ( p.Breath > 0f )
			{
				bn = unchecked( bn * 1664525u + 1013904223u );
				s += (bn / 4294967296f * 2f - 1f) * p.Breath;
			}
			if ( hpA > 0f )
			{
				float hp = hpA * (hpOutPrev + s - hpInPrev);
				hpInPrev = s; hpOutPrev = hp; s = hp;
			}

			// resonant low-pass (Chamberlin SVF) with cutoff envelope.
			// Clamp to ~sr/6 to keep the SVF stable.
			float cut = p.Cutoff + (p.CutEnv > 0f ? p.CutEnv * (float)Math.Exp( -i / decSamp ) : 0f);
			float f = (float)(2 * Math.Sin( Math.PI * Math.Min( cut, _sr * 0.16f ) / _sr ));
			float high = s - low - reso * band;
			band += f * high;
			low += f * band;
			float outp = low;

			if ( p.Drive > 1f ) outp = (float)Math.Tanh( outp * p.Drive ) * dnorm;
			float val = outp * env * p.Amp;
			_bufL[start + i] += val * gL;
			_bufR[start + i] += val * gR;
		}
	}

	static float Osc( int t, double p ) => t switch
	{
		0 => MathF.Sin( (float)(p * 2 * Math.PI) ),
		1 => (float)(2 * p - 1),
		2 => p < 0.5 ? 1f : -1f,
		_ => 4f * MathF.Abs( (float)p - 0.5f ) - 1f,
	};

	static float Midi( int m ) => 440f * MathF.Pow( 2f, (m - 69) / 12f );

	const float Sqrt2 = 1.41421356f;
	static void StereoGains( float pan, out float gL, out float gR )
	{
		pan = Math.Clamp( pan, -1f, 1f );
		double ang = (pan + 1) * 0.5 * (Math.PI / 2);
		gL = (float)Math.Cos( ang ) * Sqrt2;
		gR = (float)Math.Sin( ang ) * Sqrt2;
	}

	// ── Drums ──
	void RenderDrumBar( int barStart, int spe, int chord, bool phraseEnd, Rng rng, Rng noise )
	{
		float busy = Math.Clamp( _c.DrumBusy, 0f, 1f );
		int six = spe / 2;

		// closed hats on eighths (open on the "and of 4"); busy fills the gaps with
		// quieter sixteenth-note hats (constant 16th chatter at the top of the range).
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			int at = barStart + e * spe;
			bool open = e == 7;
			RenderHat( at, open, (e % 2 == 1 ? _c.HatVol : _c.HatVol * 0.6f), noise );
			if ( !open && six > 0 && noise.Chance( busy ) )
				RenderHat( at + six, false, _c.HatVol * 0.4f, noise );
		}

		// fill the last beat of a phrase-ending bar instead of the usual hits
		if ( phraseEnd && rng.Chance( _c.FillChance ) )
		{
			RenderKickSnareGroove( barStart, spe, 0, 6, busy, noise );   // first 3 beats normal
			RenderFill( barStart + 6 * spe, spe, noise, rng );
			return;
		}
		RenderKickSnareGroove( barStart, spe, 0, EighthsPerBar, busy, noise );
	}

	void RenderKickSnareGroove( int barStart, int spe, int from, int to, float busy, Rng noise )
	{
		int six = spe / 2;
		for ( int e = from; e < to; e++ )
		{
			int at = barStart + e * spe;
			switch ( _drumStyle )
			{
				case 0: // one-drop: kick + snare together on beat 3
					if ( e == 4 ) { RenderKick( at, noise ); RenderSnare( at, noise, false ); }
					else if ( e == 2 && noise.Chance( _c.GhostSnareChance * (0.4f + busy) ) ) RenderSnare( at, noise, true );
					break;
				case 1: // steppers: kick every beat, snare on 2 & 4
					if ( e % 2 == 0 ) RenderKick( at, noise );
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					break;
				default: // straight backbeat
					if ( e == 0 || e == 4 || (e == 3 && noise.Chance( _c.KickSyncChance * (0.4f + busy) )) ) RenderKick( at, noise );
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					else if ( noise.Chance( _c.GhostSnareChance * busy ) ) RenderSnare( at, noise, true );
					break;
			}
			// busy syncopated ghost on the "e/a" sixteenth between hits
			if ( six > 0 && e != 4 && noise.Chance( _c.GhostSnareChance * busy * 0.5f ) )
				RenderSnare( at + six, noise, true );
		}
	}

	// Tom/snare roll across the last beat (two eighths). Straight = four 16ths;
	// triplet (TripletChance) = six even subdivisions for a rolling shuffle feel.
	void RenderFill( int at, int spe, Rng noise, Rng rng )
	{
		// straight = four 16ths; triplet = either an eighth-note triplet (3) or a
		// faster 16th-note triplet (6) across the beat.
		int n = rng.Chance( _c.TripletChance ) ? (rng.Chance( 0.5f ) ? 3 : 6) : 4;
		int step = (spe * 2) / n;
		float[] toms = { 200f, 165f, 135f, 110f, 90f, 72f };
		for ( int i = 0; i < n; i++ )
		{
			int t = at + i * step;
			if ( rng.Chance( 0.5f ) ) RenderSnare( t, noise, false );
			else RenderTom( t, toms[i], noise );
		}
		RenderCrash( at + n * step, noise ); // crash into the downbeat (may land at bar end)
	}

	void RenderKick( int start, Rng noise )
	{
		if ( start < 0 ) return;
		int dur = (int)(_sr * 0.13f);
		double decay = dur * 0.28;
		double phase = 0;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			phase += (115f - 67f * MathF.Min( 1f, t * 3f )) / _sr; // fast pitch drop 115→48
			float env = (float)Math.Exp( -i / decay );
			float body = MathF.Sin( (float)(phase * 2 * Math.PI) );
			float click = i < _sr * 0.003f ? (noise.Next() * 2f - 1f) * 0.5f * (1f - i / (_sr * 0.003f)) : 0f;
			float v = ((float)Math.Tanh( body * 1.4f ) + click) * env * _c.KickVol;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	// One-pole high-pass coefficient (unconditionally stable).
	float HpCoeff( float fc ) => (float)(1.0 / (1.0 + 2 * Math.PI * fc / _sr));

	void RenderSnare( int start, Rng noise, bool ghost )
	{
		if ( start < 0 ) return;
		int dur = (int)(_sr * (ghost ? 0.06f : 0.15f));
		double decay = dur * 0.3;
		double phase = 0;
		float amp = _c.SnareVol * (ghost ? 0.3f : 1f);
		float a = HpCoeff( 1200f );
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			phase += 190f / _sr;
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float body = MathF.Sin( (float)(phase * 2 * Math.PI) ) * 0.4f;
			float v = ((float)Math.Tanh( hp * 1.2f ) * 0.7f + body) * env * amp;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	void RenderTom( int start, float baseFreq, Rng noise )
	{
		if ( start < 0 ) return;
		int dur = (int)(_sr * 0.18f);
		double decay = dur * 0.3;
		double phase = 0;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			phase += (baseFreq * (1f - 0.35f * t)) / _sr;
			float env = (float)Math.Exp( -i / decay );
			float v = MathF.Sin( (float)(phase * 2 * Math.PI) ) * env * _c.TomVol;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	void RenderHat( int start, bool open, float amp, Rng noise )
	{
		if ( start < 0 ) return;
		int dur = (int)(_sr * (open ? 0.16f : 0.035f));
		double decay = dur * 0.4;
		float a = HpCoeff( 7000f );
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env * amp;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	void RenderCrash( int start, Rng noise )
	{
		if ( start < 0 ) return;
		int dur = (int)(_sr * 0.6f);
		double decay = dur * 0.45;
		float a = HpCoeff( 4000f );
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env * _c.CrashVol;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	// ── Output ──
	short[] ToShorts( float gain )
	{
		int n = _bufL.Length;
		var s = new short[n * Channels];
		for ( int i = 0; i < n; i++ )
		{
			s[i * 2] = ToS16( _bufL[i] * gain );
			s[i * 2 + 1] = ToS16( _bufR[i] * gain );
		}
		return s;
	}

	static short ToS16( float v ) => (short)(Math.Clamp( v, -1f, 1f ) * 32767f);

	/// <summary>Wrap already-rendered 16-bit samples in a WAV (for export). Mono or
	/// interleaved stereo per <paramref name="channels"/>.</summary>
	public static byte[] WavFromSamples( short[] samples, int channels, int sampleRate )
	{
		int dataSize = samples.Length * 2;
		int blockAlign = channels * 2;
		var bytes = new System.Collections.Generic.List<byte>( 44 + dataSize );
		void Str( string s ) { foreach ( var ch in s ) bytes.Add( (byte)ch ); }
		void U32( uint v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); bytes.Add( (byte)(v >> 16) ); bytes.Add( (byte)(v >> 24) ); }
		void U16( ushort v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); }
		Str( "RIFF" ); U32( (uint)(36 + dataSize) ); Str( "WAVE" );
		Str( "fmt " ); U32( 16 ); U16( 1 ); U16( (ushort)channels );
		U32( (uint)sampleRate ); U32( (uint)(sampleRate * blockAlign) ); U16( (ushort)blockAlign ); U16( 16 );
		Str( "data" ); U32( (uint)dataSize );
		foreach ( var s in samples ) { ushort u = (ushort)s; bytes.Add( (byte)u ); bytes.Add( (byte)(u >> 8) ); }
		return bytes.ToArray();
	}

	byte[] EncodeWav( float gain )
	{
		int n = _bufL.Length;
		int dataSize = n * 4;
		var bytes = new System.Collections.Generic.List<byte>( 44 + dataSize );
		void Str( string s ) { foreach ( var ch in s ) bytes.Add( (byte)ch ); }
		void U32( uint v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); bytes.Add( (byte)(v >> 16) ); bytes.Add( (byte)(v >> 24) ); }
		void U16( ushort v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); }
		Str( "RIFF" ); U32( (uint)(36 + dataSize) ); Str( "WAVE" );
		Str( "fmt " ); U32( 16 ); U16( 1 ); U16( 2 );
		U32( (uint)_sr ); U32( (uint)(_sr * 4) ); U16( 4 ); U16( 16 );
		Str( "data" ); U32( (uint)dataSize );
		for ( int i = 0; i < n; i++ )
		{
			ushort l = (ushort)ToS16( _bufL[i] * gain );
			ushort r = (ushort)ToS16( _bufR[i] * gain );
			bytes.Add( (byte)l ); bytes.Add( (byte)(l >> 8) );
			bytes.Add( (byte)r ); bytes.Add( (byte)(r >> 8) );
		}
		return bytes.ToArray();
	}
}
