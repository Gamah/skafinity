using System;
using System.Collections.Generic;

namespace Skafinity;

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
		// Genre — selects the instrument set + arrangement. 0 = Ska (bass/skank/organ/lead/
		// horns/drums), 1 = Rock (drums/bass/rhythm-gtr/lead-gtr). New genres append here.
		public int Genre = 0;

		// Output
		public int SampleRate = 44100;
		public float TargetSeconds = 80f; // (legacy) song length now follows the structure
		public int Bars = 64;             // fallback if TargetSeconds <= 0

		// Tempo — main is laid-back reggae-rock; Fast is an uptempo ska feel.
		public int BpmMin = 130, BpmMax = 185;
		public float FastChance = 0.30f;
		public int FastBpmMin = 150, FastBpmMax = 168;
		public float Swing = 0.14f, FastSwing = 0.05f;

		// Mix (per-voice gain pre-master) — the six "volume" sliders are normalized:
		// same 0..1.5 range and the same 1.0 default (flat mix), tune from there.
		public float BassVol = 1.00f;
		public float SkankVol = 1.00f;
		public float OrganVol = 1.00f;
		public float MelodyVol = 1.00f;
		public float HornVol = 1.00f;
		public float KickVol = 1.00f;
		public float SnareVol = 0.70f;
		public float TomVol = 0.60f;
		public float HatVol = 0.22f;
		public float CrashVol = 0.35f;
		public float DrumVol = 1.00f;         // master gain over the whole kit
		// Drum "tone" — toms↔cymbals balance. 0 = boom (kick/toms forward, hats/cymbals
		// pulled back), 0.5 = neutral, 1 = bright (hats/cymbals forward). Applied per-voice.
		public float DrumTone = 0.5f;
		// Drum "drive" — pull↔push timing feel (replaces the old DrumPush magnitude roll).
		// 0 = lay back behind the beat, 0.5 = dead-on, 1 = push ahead of the beat.
		public float DrumDrive = 0.5f;

		// Rock instruments (Genre 1). Bass + drums reuse the shared knobs above.
		// KEYS — the offbeat-chord comp (was labelled "rhythm guitar", but it always read as a
		// keyboard, so it's named for what it sounds like). Power-chord comping on every eighth.
		public float KeysVol = 1.00f;
		public float KeysCutoff = 1700f;      // Hz low-pass (darker wall)
		public float KeysDrive = 3.2f;        // distortion amount (tanh drive)
		public float KeysChug = 0.5f;         // 0 = ringing chords, 1 = tight palm-mute chug
		// RHYTHM GTR — twangy distorted power chords. Shares the lead guitar's voice but strums
		// chords at a lower base distortion than the lead.
		public float RhythmGtrVol = 1.00f;
		public float RhythmGtrCutoff = 2600f; // Hz low-pass on the rhythm guitar
		public float RhythmGtrDrive = 2.8f;   // distortion amount (tanh drive) — under the lead
		public float RhythmGtrChug = 0.5f;    // 0 = ringing chords, 1 = tight palm-mute chug
		// LEAD GTR — twangy, heavily distorted single-note lead.
		public float LeadGtrVol = 1.00f;
		public float LeadGtrCutoff = 2600f;   // Hz low-pass on the lead guitar
		public float LeadGtrDrive = 3.6f;     // distortion amount (tanh drive)
		public float LeadGtrTriplets = 0.06f; // lead-guitar run/triplet ornament rate

		// Tone — low drives + filtering for warmth; detune for width.
		public float Detune = 14f;        // cents, unison spread
		public float BassCutoff = 380f;   // Hz low-pass on bass
		public float SkankCutoff = 3000f; // Hz low-pass on skank
		public float SkankHighpass = 500f;// Hz high-pass to thin the skank ("skank bite")
		public float SkankChop = 0.5f;    // skank chop length as a fraction of an eighth
		public float LeadCutoff = 3200f;  // Hz low-pass on lead
		public float OrganCutoff = 1400f; // Hz low-pass on the organ bubble
		public float OrganVibrato = 5.5f; // organ bubble vibrato depth
		public float HornCutoff = 3200f;  // Hz low-pass on the backing horns
		public float Resonance = 1.0f;    // SVF damping (lower = more resonant)
		public float BassDrive = 1.5f;
		public float SkankDrive = 1.3f;
		public float MelodyDrive = 1.3f;
		public float HornDrive = 1.4f;
		public float MasterDrive = 1.1f;
		public float MasterPeak = 0.95f;

		// Master room reverb — a touch of stereo space so the mix reads with depth
		// instead of dry/"16-bit". Wet = blend, Decay = tail length (0..1).
		public float MasterReverb = 0.16f;
		public float ReverbDecay = 0.5f;

		// Feel
		public float OctavePopChance = 0.30f;
		public float OrganBubbleChance = 0.55f;
		public float KickSyncChance = 0.25f;
		public float GhostSnareChance = 0.35f;
		public float FillChance = 0.6f;       // drum fill at phrase ends
		public float DrumBusy = 0.6f;         // 0..1 overall kit activity: 16th hats, ghosts, kick syncopation
		public float TripletChance = 0.06f;   // 0..0.2 chance of triplet/16th ornament in fills / lead runs (potent)
		public float BassTriplets = 0.06f;    // 0..0.1 bass-only 16th/triplet ornament rate (own knob)
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
	// Drums are short transients fighting a sustained melodic bed; this baseline boost
	// lets the kit sit in the mix at DRUMS = 1.0 (parity with the other voice sliders).
	const float KitPresence = 2.0f;
	readonly float _drumGain;   // master kit gain — straight 0..1.5 slider × KitPresence baseline
	float[] _bufL, _bufR;

	MusicGen( Config c ) { _c = c ?? new Config(); _sr = _c.SampleRate; _drumGain = Math.Clamp( _c.DrumVol, 0f, 1.5f ) * KitPresence; }

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

	// ── Chunked generation (parallel synthesis) ──
	// Composition + drum synthesis are sequential (RNG-bound); pitched-voice synthesis
	// pulls no RNG, so the caller can split it across worker threads. Flow:
	//   var g = MusicGen.BeginPlan( tag, cfg );            // sequential plan + drums
	//   parallel-for window in 0..g.TotalSamples: g.RenderPitchedRange( from, to );
	//   short[] mono = g.FinishMono();                     // master + downmix
	public static MusicGen BeginPlan( string tag, Config cfg )
	{
		var g = new MusicGen( cfg );
		g.ComposePlan( tag );
		return g;
	}

	public int TotalSamples => _bufL?.Length ?? 0;
	public int SampleRate => _sr;

	/// <summary>Master-normalize and downmix to mono. Call after every
	/// <see cref="RenderPitchedRange"/> window has finished.</summary>
	public short[] FinishMono()
	{
		float gain = Master();
		int n = _bufL.Length;
		var mono = new short[n];
		for ( int i = 0; i < n; i++ )
			mono[i] = ToS16( (_bufL[i] + _bufR[i]) * 0.5f * gain );
		return mono;
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

	// ── Rock harmony (Genre 1) ──
	// Darker, power-chord-friendly modes (minor / dorian / mixolydian) so rock doesn't
	// share ska's bright major themes. Picked with the SAME RNG draw as the ska tables, so
	// ska songs are byte-identical — only genre 1 reads these.
	static readonly int[][] RockScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian (classic-rock major-ish)
		new[] { 0, 2, 3, 5, 7, 9, 10 }, // dorian
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (weighted twice)
	};

	// Degrees are read against the (often minor) scale, so 5 = ♭VI, 6 = ♭VII, 3 = iv, 4 = v.
	static readonly int[][] RockProgressions =
	{
		new[] { 0, 6, 5, 6 }, // i–♭VII–♭VI–♭VII (driving rock vamp)
		new[] { 0, 5, 6, 0 }, // i–♭VI–♭VII–i
		new[] { 0, 6, 3, 0 }, // i–♭VII–IV–i (mixolydian rock)
		new[] { 0, 3, 6, 0 }, // i–iv–♭VII–i
		new[] { 0, 0, 6, 6 }, // i / ♭VII riff vamp
		new[] { 0, 3, 4, 0 }, // i–iv–v–i
	};

	// Driving root/octave eighths that lock to the kick — the rock engine room, vs ska's
	// syncopated off-beat one-drop bass.
	static readonly int[][] RockBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight eighth chug
		new[] { 0, Rest, 0, Rest, 0, Rest, 0, Approach },// quarter-note pulse
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 7, 0, 0, 0, 7, Approach },         // root–fifth gallop
		new[] { 0, Rest, 0, 0, Rest, 0, 12, Approach },  // syncopated driver
	};

	int[] _scale, _prog;
	int _rootMidi;
	Instrument _lead;
	float _leadPan;
	bool _hasHorns;
	bool[] _hornMask;
	int[] _bassPat;
	int _drumStyle;          // 0 one-drop, 1 steppers, 2 straight backbeat
	bool _ride;              // per-song: ride cymbal drives the eighth pulse instead of closed hats
	bool _organBubble;
	bool _fast;
	int _genre;              // 0 ska, 1 rock
	string _tag;             // the per-song seed string, reused to seed per-section streams
	int _drumPush;           // per-song-constant kit timing bias in samples (− ahead / + back)
	float _drumLowMul = 1f;  // DrumTone → kick/tom gain multiplier
	float _drumHighMul = 1f; // DrumTone → hat/cymbal gain multiplier

	// ── Song structure ──
	// A song is an ordered list of sections. Hardcoded for now (will be RNG-generated once
	// there are more part types); the fixed run is intro → chorus → verse(0) → chorus →
	// verse(1) → chorus → ending. Non-lead voices are seeded by section TYPE so every chorus
	// (and both verses) play identical backing; the lead is seeded by type + verse index so
	// it evolves across the Nth verse; the section-end fill is seeded by absolute index so
	// every section closes with a different fill.
	enum Section { Intro, Chorus, Verse, Ending }
	readonly struct Part
	{
		public readonly Section Type; public readonly int Bars; public readonly int VerseIndex;
		public Part( Section t, int bars, int verse ) { Type = t; Bars = bars; VerseIndex = verse; }
	}

	static List<Part> BuildStructure() => new()
	{
		new Part( Section.Intro,  4, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 1 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Ending, 4, 0 ),
	};

	static string SectionKey( Section s ) => s switch
	{
		Section.Intro => "intro",
		Section.Chorus => "chorus",
		Section.Verse => "verse",
		_ => "ending",
	};

	// Single-threaded generation (used by Generate / GenerateSamples). The controller
	// uses the chunked path instead (BeginPlan → parallel RenderPitchedRange → FinishMono).
	float Compose( string tag )
	{
		ComposePlan( tag );
		RenderPitchedRange( 0, _bufL.Length );
		return Master();
	}

	// Sequential planning pass: RNG composition + drum synthesis written straight into
	// the buffer, while every pitched note is collected as an event (rendered later,
	// possibly in parallel). RNG draw order is identical to the old inline render —
	// RenderPatch now only enqueues, and it never pulled RNG anyway.
	void ComposePlan( string tag )
	{
		_events.Clear();
		_tag = string.IsNullOrEmpty( tag ) ? "rotaliate" : tag;
		_genre = Math.Clamp( _c.Genre, 0, 1 );
		var rng = new Rng( Xmur3( _tag.ToLowerInvariant() ) );

		_fast = rng.Chance( _c.FastChance );              // TEMPO BIAS
		int bpm = _fast
			? _c.FastBpmMin + rng.Int( Math.Max( 1, _c.FastBpmMax - _c.FastBpmMin + 1 ) )
			: _c.BpmMin + rng.Int( Math.Max( 1, _c.BpmMax - _c.BpmMin + 1 ) );
		bool rock = _genre == 1;
		_scale = rng.Pick( rock ? RockScales : Scales );
		_prog = rng.Pick( rock ? RockProgressions : Progressions );
		_rootMidi = 28 + rng.Int( 8 );                    // E1..B1 bass root
		_lead = Instrument.Trumpet;                       // ska lead is fixed; rock uses guitar
		_leadPan = (rng.Next() * 2f - 1f) * _c.PanAmount;
		_bassPat = rng.Pick( rock ? RockBassPatterns : BassPatterns );
		_drumStyle = _genre == 1 ? 2 : (_fast ? 2 : rng.Int( 2 )); // rock = straight backbeat
		_organBubble = true;
		_hasHorns = true;
		_hornMask = new bool[EighthsPerBar];
		_hornMask[0] = true;
		for ( int e = 1; e < EighthsPerBar; e++ )
			_hornMask[e] = rng.Chance( _c.HornDensity * (e % 2 == 1 ? 1.3f : 0.5f) );
		// Some songs ride a ride cymbal instead of closed hats for the main pulse (more common
		// in rock). Drawn last so it can't shift any earlier musical choice.
		_ride = rng.Chance( rock ? 0.5f : 0.3f );

		float swing = _fast ? _c.FastSwing : _c.Swing;
		double secPerEighth = 60.0 / bpm / 2.0;
		int spe = (int)Math.Round( _sr * secPerEighth );

		// Drum tone (toms↔cymbals) → per-voice gain split, and drive (pull↔push) → a constant
		// kit timing bias (− = ahead/push, + = behind/lay back; 0.5 = dead on).
		float dt = Math.Clamp( _c.DrumTone, 0f, 1f );
		_drumLowMul = 1.4f - 0.8f * dt;
		_drumHighMul = 0.4f + 1.2f * dt;
		_drumPush = (int)Math.Round( (0.5f - Math.Clamp( _c.DrumDrive, 0f, 1f )) * 2f * 0.13f * spe );

		// Lay out the structure and size the buffers to its total length.
		var structure = BuildStructure();
		int totalBars = 0;
		foreach ( var p in structure ) totalBars += p.Bars;
		int total = spe * EighthsPerBar * totalBars;
		_bufL = new float[total];
		_bufR = new float[total];

		int barCursor = 0;
		for ( int si = 0; si < structure.Count; si++ )
		{
			var part = structure[si];
			RenderSection( part, si, barCursor * EighthsPerBar * spe, spe, secPerEighth, swing );
			barCursor += part.Bars;
		}
	}

	// Render one section. Each voice gets its own per-section RNG stream keyed so that repeats
	// of a section type reproduce identical backing, while the lead key folds in the verse
	// index (so the Nth verse's lead differs) and the fill key folds in the absolute section
	// index (so every section closes with a unique fill).
	void RenderSection( Part part, int absIndex, int sectionStart, int spe, double secPerEighth, float swing )
	{
		string bk = SectionKey( part.Type );
		string lk = part.Type == Section.Verse ? $"verse:{part.VerseIndex}" : bk;
		var bassRng = new Rng( Xmur3( $"{_tag}:bass:{bk}" ) );
		var bassOrn = new Rng( Xmur3( $"{_tag}:bassorn:{bk}" ) );
		var rhythmRng = new Rng( Xmur3( $"{_tag}:rhythm:{bk}" ) );
		var hornRng = new Rng( Xmur3( $"{_tag}:horn:{bk}" ) );
		var leadRng = new Rng( Xmur3( $"{_tag}:lead:{lk}" ) );
		var noise = new Rng( Xmur3( $"{_tag}:drums:{bk}" ) );
		var fillRng = new Rng( Xmur3( $"{_tag}:fill:{absIndex}" ) );
		var fillNoise = new Rng( Xmur3( $"{_tag}:fillnoise:{absIndex}" ) );

		for ( int bar = 0; bar < part.Bars; bar++ )
		{
			int chord = (bar / 2) % _prog.Length;
			int nextChord = ((bar / 2) + 1) % _prog.Length;
			int barStart = sectionStart + bar * EighthsPerBar * spe;
			bool lastBar = bar == part.Bars - 1;          // every section ends with a fill

			RenderBassBar( barStart, spe, secPerEighth, chord, nextChord, bassRng, bassOrn );
			if ( _genre == 1 )
			{
				RenderKeysBar( barStart, spe, secPerEighth, chord, rhythmRng );
				RenderRhythmGuitarBar( barStart, spe, secPerEighth, chord, rhythmRng );
			}
			else
			{
				RenderRhythmBar( barStart, spe, secPerEighth, chord, swing, rhythmRng );
				if ( _hasHorns )
					RenderHornStabs( barStart, spe, secPerEighth, chord, hornRng );
			}
			RenderDrumBar( barStart, spe, lastBar, noise, fillRng, fillNoise );

			if ( bar % 2 == 0 )
				RenderLeadPhrase( barStart, spe, secPerEighth, chord, leadRng );
		}
	}

	// Master: gentle soft-clip + normalize. The mix peak is first normalized to 1.0
	// BEFORE the soft-clipper so it always has headroom — otherwise a hot sustained
	// bed (all voices flat at 1.0) saturated the tanh and swallowed the drum
	// transients (kick/snare washed out). MasterDrive now sets how hard a
	// peak-normalized signal hits the clipper, so the dynamics stay intact.
	// Call only after every RenderPitchedRange window has completed.
	float Master()
	{
		int total = _bufL.Length;
		float rawPeak = 0f;
		for ( int i = 0; i < total; i++ )
			rawPeak = Math.Max( rawPeak, Math.Max( MathF.Abs( _bufL[i] ), MathF.Abs( _bufR[i] ) ) );
		float pre = rawPeak > 0.0001f ? _c.MasterDrive / rawPeak : _c.MasterDrive;

		for ( int i = 0; i < total; i++ )
		{
			_bufL[i] = (float)Math.Tanh( _bufL[i] * pre );
			_bufR[i] = (float)Math.Tanh( _bufR[i] * pre );
		}

		// A touch of stereo room reverb — the dry mix alone read flat/"16-bit".
		ApplyReverb();

		float peak = 0f;
		for ( int i = 0; i < total; i++ )
		{
			float a = Math.Max( MathF.Abs( _bufL[i] ), MathF.Abs( _bufR[i] ) );
			if ( a > peak ) peak = a;
		}
		return peak > 0.0001f ? _c.MasterPeak / peak : 1f;
	}

	// ── Master reverb ──
	// A Schroeder/Freeverb-style bank: several parallel damped comb filters (the dense
	// tail) feeding a chain of allpasses (diffusion). The two channels use slightly
	// different delay lengths so the room is decorrelated → real stereo width and depth.
	static readonly int[] CombBase = { 1116, 1188, 1277, 1356, 1422, 1491 }; // samples @ 44.1k
	static readonly int[] ApBase = { 556, 441, 341 };
	const int ReverbStereoSpread = 23; // R-channel delay offset for decorrelation

	void ApplyReverb()
	{
		float wet = Math.Clamp( _c.MasterReverb, 0f, 1f );
		if ( wet <= 0.0001f ) return;
		float feedback = 0.70f + 0.28f * Math.Clamp( _c.ReverbDecay, 0f, 1f ); // tail length
		const float damp = 0.25f, damp1 = 1f - damp;                            // HF damping in the tail
		const float apg = 0.5f;                                                 // allpass coefficient
		const float inGain = 0.25f;                                             // drive into the reverb
		double srk = _sr / 44100.0;                                             // scale delays to the rate

		for ( int ch = 0; ch < 2; ch++ )
		{
			var buf = ch == 0 ? _bufL : _bufR;
			int off = ch == 0 ? 0 : ReverbStereoSpread;
			int nc = CombBase.Length, na = ApBase.Length;
			var combBuf = new float[nc][];
			var combIdx = new int[nc];
			var combStore = new float[nc];
			for ( int j = 0; j < nc; j++ )
				combBuf[j] = new float[Math.Max( 1, (int)Math.Round( (CombBase[j] + off) * srk ) )];
			var apBuf = new float[na][];
			var apIdx = new int[na];
			for ( int j = 0; j < na; j++ )
				apBuf[j] = new float[Math.Max( 1, (int)Math.Round( (ApBase[j] + off) * srk ) )];

			int n = buf.Length;
			for ( int i = 0; i < n; i++ )
			{
				float input = buf[i] * inGain;
				float acc = 0f;
				for ( int j = 0; j < nc; j++ )
				{
					var cb = combBuf[j];
					int idx = combIdx[j];
					float r = cb[idx];
					combStore[j] = r * damp1 + combStore[j] * damp;
					cb[idx] = input + combStore[j] * feedback;
					if ( ++idx >= cb.Length ) idx = 0;
					combIdx[j] = idx;
					acc += r;
				}
				acc /= nc;
				for ( int j = 0; j < na; j++ )
				{
					var ab = apBuf[j];
					int idx = apIdx[j];
					float r = ab[idx];
					float o = r - acc;
					ab[idx] = acc + r * apg;
					if ( ++idx >= ab.Length ) idx = 0;
					apIdx[j] = idx;
					acc = o;
				}
				buf[i] += wet * acc;
			}
		}
	}

	int ScaleMidi( int baseMidi, int degree )
	{
		int len = _scale.Length;
		int oct = (int)Math.Floor( degree / (double)len );
		return baseMidi + _scale[degree - oct * len] + 12 * oct;
	}
	int ChordRoot( int c ) => ScaleMidi( _rootMidi, _prog[c] );

	// ── Bass ──
	void RenderBassBar( int barStart, int spe, double secPerEighth, int chord, int nextChord, Rng rng, Rng bassOrn )
	{
		int root = ChordRoot( chord );
		// Bass has its own ornament knob (BASS TRIPLETS), nudged up a touch by overall
		// kit busyness so a busy vibe gets a busier bass.
		float ornChance = _c.BassTriplets * 0.5f + _c.DrumBusy * 0.05f;
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

			// Chop a standalone (non-sustaining) note into a 16th pair or 16th-note
			// triplet so the line reads "long long short short" / "long short long long"
			// instead of even eighths. Driven by a dedicated stream, so the main
			// composition RNG order — and every existing song — is left unchanged.
			if ( off != Approach && len == 1 && bassOrn.Chance( ornChance ) )
			{
				int n = bassOrn.Chance( 0.65f ) ? 2 : 3;        // 16th pair / 16th triplet
				int step = spe / n;
				int[] moves = { 0, 7, 12 };                     // root / fifth / octave
				for ( int k = 0; k < n; k++ )
				{
					int bm = midi + (k == 0 ? 0 : moves[bassOrn.Int( moves.Length )]);
					EmitBass( barStart + e * spe + k * step, (int)(step * 0.9f), bm, secPerEighth / n * 0.8 );
				}
				continue;
			}

			EmitBass( barStart + e * spe, (int)(spe * len * 0.95f), midi, secPerEighth * len * 0.8 );
		}
	}

	void EmitBass( int at, int dur, int midi, double decaySec )
	{
		// Triangle body for a round, deep reggae/dub bass (saw alone read as too
		// buzzy) — but triangle alone was too subtle, so layer a quieter square
		// underneath for presence/definition. The square's odd harmonics give the
		// bass its bite; both share the bass low-pass so the tone stays warm.
		RenderPatch( at, dur, Midi( midi ), new Patch
		{
			Osc = 3, Voices = 2, Detune = _c.Detune * 0.4f,
			Amp = _c.BassVol, Attack = 0.004f, Decay = decaySec,
			Sustain = 0.55f, Sustained = true,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		} );
		RenderPatch( at, dur, Midi( midi ), new Patch
		{
			Osc = 2, Voices = 1, Detune = 0f,
			Amp = _c.BassVol * 0.4f, Attack = 0.004f, Decay = decaySec,
			Sustain = 0.55f, Sustained = true,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		} );
	}

	// ── Skank guitar (the signature) + reggae organ bubble — offbeats, centered ──
	void RenderRhythmBar( int barStart, int spe, double secPerEighth, int chord, float swing, Rng rng )
	{
		// +24: skank/organ sit an octave above the old register — at +12 (E2..B2) the
		// chop was too low/muddy to cut through and read as missing. Organ stays a
		// further octave down via the -12 below.
		int gBase = _rootMidi + 24;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 7 };

		for ( int e = 1; e < EighthsPerBar; e += 2 ) // offbeats
		{
			int at = barStart + e * spe + (int)(swing * spe);

			// bright, thin, short guitar chop
			foreach ( var d in degs )
				RenderPatch( at, (int)(spe * Math.Clamp( _c.SkankChop, 0.15f, 1f )), Midi( ScaleMidi( gBase, d ) ), new Patch
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
						Cutoff = _c.OrganCutoff, CutEnv = 0f, Reso = 1.0f, Drive = 1.1f, Pan = 0f,
						Vibrato = _c.OrganVibrato,
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
		float amp = _genre == 1 ? _c.LeadGtrVol : _c.MelodyVol;
		float drive = _genre == 1 ? _c.LeadGtrDrive : _c.MelodyDrive;
		float tripChance = _genre == 1 ? _c.LeadGtrTriplets : _c.TripletChance;

		int e = 0;
		while ( e < slots )
		{
			if ( rng.Chance( _c.MelodyRestChance ) ) { e++; continue; }

			// ornament: a sixteenth pair, or a triplet at one of three rates — a tight
			// 16th-triplet (3 in an eighth), an eighth-note triplet (3 in a beat), or a
			// wide quarter-note triplet (3 over two beats). Wider spans give the lazy,
			// over-the-barline triplet feel, not just the fast run.
			if ( rng.Chance( tripChance ) )
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
					RenderLeadNote( barStart + e * spe + k * step, (int)(step * 0.9f),
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

			RenderLeadNote( barStart + e * spe, (int)(spe * len * 0.9f), ScaleMidi( melBase, degree ),
				amp, secPerEighth * len * 0.7f, drive );
			e += len;
		}
	}

	// Dispatch a lead note to the genre's lead voice: a distorted single-note guitar for rock,
	// otherwise the ska horn (RenderLead → trumpet).
	void RenderLeadNote( int at, int dur, int midi, float amp, double decaySec, float drive )
	{
		if ( _genre == 1 )
		{
			// Twang = a bright cutoff-envelope snap on each pick (high CutEnv, decays fast) through
			// a resonant SVF, then a high BASE distortion (3 + the slider) so it reads as an
			// overdriven electric guitar — twangy and dirty even at the slider minimum.
			RenderPatch( at, dur, Midi( midi ), new Patch
			{
				Osc = 1, Voices = 1, Detune = 0f, Amp = amp,
				Attack = 0.002f, Decay = decaySec, Sustain = 0.55f, Sustained = true,
				Cutoff = _c.LeadGtrCutoff, CutEnv = 2200f, Reso = 0.65f,
				Drive = 3f + MathF.Max( 1f, _c.LeadGtrDrive ), Pan = _leadPan, Vibrato = _c.MelodyVibrato,
			} );
			return;
		}
		RenderLead( at, dur, midi, amp, decaySec, drive );
	}

	// ── Rock KEYS — driven power chords (root + fifth + octave) on every eighth (was labelled
	// "rhythm guitar"; it reads as a keyboard comp). Downbeats ring; offbeats are tightened
	// toward a palm-muted chug as KeysChug rises.
	void RenderKeysBar( int barStart, int spe, double secPerEighth, int chord, Rng rng )
	{
		int root = ChordRoot( chord ) + 12;               // chunky power-chord register (an octave below the ska skank)
		int[] chordOffs = { 0, 7, 12 };                   // power chord
		float chug = Math.Clamp( _c.KeysChug, 0f, 1f );
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			bool accent = (e % 2) == 0;                    // downbeats ring, offbeats chug
			float lenFrac = accent ? (1f - 0.5f * chug) : (0.35f - 0.2f * chug);
			int dur = (int)(spe * Math.Max( 0.12f, lenFrac ));
			double dec = secPerEighth * (accent ? 0.8 : 0.3);
			foreach ( var o in chordOffs )
				RenderPatch( barStart + e * spe, dur, Midi( root + o ), new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.KeysVol / chordOffs.Length * (accent ? 1f : 0.7f),
					Attack = 0.003f, Decay = dec, Sustain = accent ? 0.5f : 0f, Sustained = accent,
					Cutoff = _c.KeysCutoff, CutEnv = 250f, Reso = 1.0f,
					Drive = MathF.Max( 1f, _c.KeysDrive ), Pan = 0f,
				} );
		}
	}

	// ── Rock rhythm guitar — twangy distorted power chords. Shares the lead guitar's voice (the
	// bright cutoff-envelope "twang" through a resonant SVF) but strums root+fifth+octave and
	// runs a LOWER base distortion than the lead so the two layer instead of mush. Downbeats
	// ring; offbeats tighten toward a palm-muted chug as RhythmGtrChug rises.
	void RenderRhythmGuitarBar( int barStart, int spe, double secPerEighth, int chord, Rng rng )
	{
		int root = ChordRoot( chord ) + 12;               // chunky power-chord register
		int[] chordOffs = { 0, 7, 12 };                   // power chord
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			bool accent = (e % 2) == 0;                    // downbeats ring, offbeats chug
			float lenFrac = accent ? (1f - 0.5f * chug) : (0.35f - 0.2f * chug);
			int dur = (int)(spe * Math.Max( 0.12f, lenFrac ));
			double dec = secPerEighth * (accent ? 0.8 : 0.3);
			foreach ( var o in chordOffs )
				RenderPatch( barStart + e * spe, dur, Midi( root + o ), new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.RhythmGtrVol / chordOffs.Length * (accent ? 1f : 0.7f),
					Attack = 0.002f, Decay = dec, Sustain = accent ? 0.45f : 0f, Sustained = accent,
					Cutoff = _c.RhythmGtrCutoff, CutEnv = 1400f, Reso = 0.8f,   // twang
					Drive = 1.5f + MathF.Max( 1f, _c.RhythmGtrDrive ), Pan = 0f, // less base than lead
				} );
		}
	}

	// ── Backing horns (panned spread) ──
	// Block stabs on the mask read "samey" (only eighth-note chords). A dedicated
	// stream (horn:tag, so the main composition order is unchanged) breaks them up
	// with rolling arpeggios, 16th pairs, grace pickups and varied length. Kept
	// modest — the bass got over-busy when its ornament rate ran high.
	void RenderHornStabs( int barStart, int spe, double secPerEighth, int chord, Rng orn )
	{
		int baseMidi = _rootMidi + 19;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4 };
		float spread = _c.PanAmount * 0.7f;
		int six = spe / 2;
		float ornChance = 0.18f + _c.TripletChance; // ~0.24 default; rides the same knob

		// one chord-tone voice
		void Note( int at, int dur, int k, double dec, float gain )
			=> RenderPatch( at, dur, Midi( ScaleMidi( baseMidi, degs[k] ) ), new Patch
			{
				Osc = 1, Voices = 3, Detune = _c.Detune,
				Amp = _c.HornVol / degs.Length * gain, Attack = 0.008f, Decay = dec,
				Sustain = 0.2f, Sustained = false,
				Cutoff = _c.HornCutoff, CutEnv = 1200f, Reso = 1.0f,
				Drive = _c.HornDrive, Pan = spread * (k / (float)(degs.Length - 1) * 2f - 1f),
			} );

		// full block chord stab
		void Stab( int at, int dur, double dec, float gain )
		{
			for ( int k = 0; k < degs.Length; k++ ) Note( at, dur, k, dec, gain );
		}

		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			if ( !_hornMask[e] ) continue;
			int at = barStart + e * spe;

			if ( six > 0 && orn.Chance( ornChance ) )
			{
				float r = orn.Next();
				if ( r < 0.4f )
				{
					// rolling arpeggio: chord tones climb across a 16th-triplet
					int step = spe / 3;
					for ( int k = 0; k < degs.Length; k++ )
						Note( at + k * step, (int)(step * 0.9f), k, secPerEighth / 3 * 0.8, 1f );
					continue;
				}
				if ( r < 0.75f )
				{
					// 16th pair: stab on the beat, softer echo on the "e"
					Stab( at, (int)(six * 0.85f), secPerEighth * 0.5 * 0.8, 1f );
					Stab( at + six, (int)(six * 0.85f), secPerEighth * 0.5 * 0.7, 0.6f );
					continue;
				}
				// grace pickup: a soft single tone just before the block stab
				Note( at - six, (int)(six * 0.8f), 0, secPerEighth * 0.5 * 0.6, 0.5f );
				Stab( at, (int)(spe * 0.6f), 0.22, 1f );
				continue;
			}

			// plain stab — length varies a touch so even straight bars aren't identical
			float lenMul = 0.45f + orn.Next() * 0.35f;
			Stab( at, (int)(spe * lenMul), 0.22, 1f );
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

	// Pitched note events collected during ComposePlan, then synthesized by
	// RenderPitchedRange. Synthesis pulls no RNG, so windows parallelize across threads.
	struct NoteEvent { public int Start, Dur; public float Freq; public Patch P; }
	readonly List<NoteEvent> _events = new();

	// During ComposePlan this only enqueues; the synthesis happens in RenderPitchedRange.
	void RenderPatch( int start, int dur, float freq, Patch p )
	{
		if ( start < 0 || dur <= 0 || p.Voices < 1 ) return;
		_events.Add( new NoteEvent { Start = start, Dur = dur, Freq = freq, P = p } );
	}

	/// <summary>Synthesize every pitched event whose span overlaps <c>[from, to)</c>,
	/// writing ONLY samples inside that window. Safe to call concurrently for disjoint
	/// windows: each output index is owned by exactly one window, a boundary-spanning
	/// note is re-rendered from its own start by each window (the SVF / high-pass state
	/// can't be resumed mid-stream), and each window walks <c>_events</c> in order, so
	/// writes never collide and the per-index sum order is deterministic.</summary>
	public void RenderPitchedRange( int from, int to )
	{
		from = Math.Max( 0, from );
		to = Math.Min( _bufL.Length, to );
		if ( to <= from ) return;
		var ph = new double[8];
		var inc = new double[8];
		var events = _events;
		for ( int k = 0; k < events.Count; k++ )
		{
			var ev = events[k];
			int end = Math.Min( _bufL.Length, ev.Start + ev.Dur );
			if ( end <= from || ev.Start >= to ) continue; // no overlap with this window
			RenderEvent( ev, from, to, ph, inc );
		}
	}

	// One pitched note. Computes from the note's own start (the running filter / breath
	// state can't be resumed mid-note) but writes only within [clipFrom, clipTo), and
	// stops once past clipTo since later windows own those samples. ph/inc are caller-
	// owned scratch (per-thread → no shared state).
	void RenderEvent( in NoteEvent ev, int clipFrom, int clipTo, double[] ph, double[] inc )
	{
		int start = ev.Start, dur = ev.Dur;
		float freq = ev.Freq;
		var p = ev.P;
		StereoGains( p.Pan, out float gL, out float gR );
		int atk = Math.Max( 1, (int)(p.Attack * _sr) );
		double decSamp = Math.Max( 1.0, p.Decay * _sr );
		int rel = Math.Max( 1, (int)(0.006f * _sr) );
		int voices = Math.Min( 8, p.Voices );

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

		int end = Math.Min( Math.Min( _bufL.Length, start + dur ), clipTo );
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
					double dt = inc[v] * vib;
					s += BlepOsc( p.Osc, ph[v] - Math.Floor( ph[v] ), dt );
					ph[v] += dt;
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
			int idx = start + i;
			if ( idx >= clipFrom )
			{
				_bufL[idx] += val * gL;
				_bufR[idx] += val * gR;
			}
		}
	}

	// Band-limited oscillator. Naive saw/square step instantaneously at the phase wrap,
	// and those discontinuities alias into harsh inharmonic tones — the core of the
	// "8-/16-bit" buzz. PolyBLEP rounds each discontinuity over one sample so the harmonics
	// fold back cleanly, for a warm analog edge instead. Sine is already band-limited;
	// triangle's corners roll off as 1/n² so its aliasing is inaudible.
	// p = phase in [0,1), dt = phase increment per sample (cycles/sample).
	static float BlepOsc( int t, double p, double dt )
	{
		switch ( t )
		{
			case 0:
				return MathF.Sin( (float)(p * 2 * Math.PI) );
			case 1: // saw
				return (float)(2 * p - 1) - PolyBlep( p, dt );
			case 2: // square (50% duty = two opposed discontinuities)
			{
				float v = p < 0.5 ? 1f : -1f;
				v += PolyBlep( p, dt );
				double p2 = p + 0.5; if ( p2 >= 1.0 ) p2 -= 1.0;
				return v - PolyBlep( p2, dt );
			}
			default: // triangle
				return 4f * MathF.Abs( (float)p - 0.5f ) - 1f;
		}
	}

	// PolyBLEP residual: the correction applied around a step discontinuity.
	static float PolyBlep( double t, double dt )
	{
		if ( dt <= 0 ) return 0f;
		if ( t < dt ) { t /= dt; return (float)(t + t - t * t - 1.0); }
		if ( t > 1.0 - dt ) { t = (t - 1.0) / dt; return (float)(t * t + t + t + 1.0); }
		return 0f;
	}

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
	// Render a bar of kit. On a section's last bar (fillEnd) the closing beat is replaced by a
	// fill — driven by its own RNG streams so every section's fill is different even when the
	// groove before it is identical.
	void RenderDrumBar( int barStart, int spe, bool fillEnd, Rng noise, Rng fillRng, Rng fillNoise )
	{
		// Knob ceiling was too frantic: scale so DRUM BUSY 100% reads as the old 75%.
		float busy = Math.Clamp( _c.DrumBusy, 0f, 1f ) * 0.75f;
		int six = spe / 2;
		int hatEnd = fillEnd ? 6 : EighthsPerBar;         // hats stop where the fill begins

		// closed hats on eighths (open on the "and of 4"); busy fills the gaps with
		// quieter sixteenth-note hats (constant 16th chatter at the top of the range). On
		// ride songs the ride cymbal carries the eighth pulse instead (bell on the beats), with
		// the open hat still punctuating the "and of 4".
		for ( int e = 0; e < hatEnd; e++ )
		{
			int at = barStart + e * spe;
			bool open = e == 7;
			float amp = e % 2 == 1 ? _c.HatVol : _c.HatVol * 0.6f;
			if ( _ride && !open )
				RenderRide( at, e % 2 == 0, amp, noise );    // bell accent on the downbeats
			else
				RenderHat( at, open, amp, noise );
			if ( !open && six > 0 && noise.Chance( busy ) )
			{
				if ( _ride ) RenderRide( at + six, false, _c.HatVol * 0.4f, noise );
				else RenderHat( at + six, false, _c.HatVol * 0.4f, noise );
			}
		}

		if ( fillEnd )
		{
			RenderKickSnareGroove( barStart, spe, 0, 6, busy, noise );   // first 3 beats normal
			RenderFill( barStart + 6 * spe, spe, fillNoise, fillRng );
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
		// crash into the downbeat (may land at bar end) — a bright crash or a darker, washier
		// crash, picked off the fill stream so the cymbal colour varies section to section.
		RenderCrash( at + n * step, noise, rng.Chance( 0.4f ) );
	}

	void RenderKick( int start, Rng noise )
	{
		start = Math.Max( 0, start + _drumPush );
		int dur = (int)(_sr * 0.17f);          // a little longer tail for thump (was 0.13)
		double decay = dur * 0.31;             // slightly slower decay = a touch more boom
		double subDecay = dur * 0.55;          // sub layer rings longer for weight
		double phase = 0, subPhase = 0;
		// noise.Next() only fires in the fixed 3ms click below, so changing dur/decay
		// here does NOT shift the drum RNG stream (patterns are preserved).
		int clickLen = (int)(_sr * 0.003f);
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			phase += (127f - 80f * MathF.Min( 1f, t * 2.6f )) / _sr;   // pitch drop 127→47
			subPhase += 44f / _sr;                                     // steady sub fundamental
			float env = (float)Math.Exp( -i / decay );
			float subEnv = (float)Math.Exp( -i / subDecay );
			float body = (float)Math.Tanh( MathF.Sin( (float)(phase * 2 * Math.PI) ) * 1.6f ) * env;
			float sub = MathF.Sin( (float)(subPhase * 2 * Math.PI) ) * 0.3f * subEnv;
			float click = i < clickLen ? (noise.Next() * 2f - 1f) * 0.55f * (1f - i / (float)clickLen) : 0f;
			float v = (body + sub + click) * _c.KickVol * _drumGain * _drumLowMul;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	// One-pole high-pass coefficient (unconditionally stable).
	float HpCoeff( float fc ) => (float)(1.0 / (1.0 + 2 * Math.PI * fc / _sr));

	void RenderSnare( int start, Rng noise, bool ghost )
	{
		start = Math.Max( 0, start + _drumPush );
		// dur and the single noise.Next()/sample are kept exactly so the drum RNG stream
		// is unchanged — only the timbre (more shell body) is rerolled.
		int dur = (int)(_sr * (ghost ? 0.06f : 0.15f));
		double decay = dur * (ghost ? 0.3 : 0.32);
		double phase = 0, phase2 = 0;
		float amp = _c.SnareVol * (ghost ? 0.3f : 1f) * _drumGain;
		float a = HpCoeff( 1350f );           // slightly crisper wire crack (was 1200)
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			float env = (float)Math.Exp( -i / decay );
			float drop = 1f - 0.14f * t;       // shell pitch sags a touch → "dow"
			phase += 185f * drop / _sr;
			phase2 += 268f * drop / _sr;
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			// two-tone shell body, a bit fuller than before, vs the wire layer
			float body = (MathF.Sin( (float)(phase * 2 * Math.PI) ) + MathF.Sin( (float)(phase2 * 2 * Math.PI) ) * 0.6f) * 0.375f;
			float v = ((float)Math.Tanh( hp * 1.2f ) * 0.6f + body) * env * amp;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	void RenderTom( int start, float baseFreq, Rng noise )
	{
		start = Math.Max( 0, start + _drumPush );
		int dur = (int)(_sr * 0.18f);
		double decay = dur * 0.3;
		double phase = 0;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			phase += (baseFreq * (1f - 0.35f * t)) / _sr;
			float env = (float)Math.Exp( -i / decay );
			float v = MathF.Sin( (float)(phase * 2 * Math.PI) ) * env * _c.TomVol * _drumGain * _drumLowMul;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	void RenderHat( int start, bool open, float amp, Rng noise )
	{
		start = Math.Max( 0, start + _drumPush );
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
			float v = hp * env * amp * _drumGain * _drumHighMul;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	// Two crash colours off one voice: the bright crash (short-ish, high-passed high) and a
	// dark crash — lower cutoff, longer wash, a touch quieter — for a bigger china/ride-crash.
	void RenderCrash( int start, Rng noise, bool dark = false )
	{
		start = Math.Max( 0, start + _drumPush );
		int dur = (int)(_sr * (dark ? 0.9f : 0.6f));
		double decay = dur * (dark ? 0.5 : 0.45);
		float a = HpCoeff( dark ? 2600f : 4000f );
		float amp = _c.CrashVol * (dark ? 0.85f : 1f);
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env * amp * _drumGain * _drumHighMul;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	// Ride cymbal — a sustained, articulate "ping": metallic inharmonic partials for the stick
	// strike over a high-passed shimmer. The bell hit (on the beat) is brighter and rings a
	// touch longer than the bow hit (on the "and"). Tracks CrashVol-free off HatVol via the
	// caller's amp so the existing DRUMS knobs still balance it.
	void RenderRide( int start, bool bell, float amp, Rng noise )
	{
		start = Math.Max( 0, start + _drumPush );
		int dur = (int)(_sr * (bell ? 0.34f : 0.22f));
		double decay = dur * 0.42;
		float a = HpCoeff( 7000f );
		float inPrev = 0f, outPrev = 0f;
		// A high-passed noise wash carries the body (a cymbal is mostly filtered noise); the
		// metallic "ping" is a *quiet* cluster of six inharmonic partials whose ratios share no
		// common fundamental, so it reads as metal rather than a pitched triangle/bell. (The old
		// version used three near-harmonic sines louder than the noise, which rang as a clear
		// ~540 Hz "ding".) The bell accent leans a touch more tonal than the bow hit.
		double p1 = 0, p2 = 0, p3 = 0, p4 = 0, p5 = 0, p6 = 0;
		float f1 = bell ? 320f : 440f;
		float ping = bell ? 0.14f : 0.09f;      // partials stay well under the noise wash
		double pingDecay = decay * 0.3;         // the metal is an attack tick; the noise sustains
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float penv = (float)Math.Exp( -i / pingDecay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			p1 += f1 / _sr; p2 += f1 * 1.41 / _sr; p3 += f1 * 1.93 / _sr;
			p4 += f1 * 2.51 / _sr; p5 += f1 * 3.07 / _sr; p6 += f1 * 3.66 / _sr;
			float metal = (MathF.Sin( (float)(p1 * 2 * Math.PI) )
				+ MathF.Sin( (float)(p2 * 2 * Math.PI) )
				+ MathF.Sin( (float)(p3 * 2 * Math.PI) )
				+ MathF.Sin( (float)(p4 * 2 * Math.PI) )
				+ MathF.Sin( (float)(p5 * 2 * Math.PI) )
				+ MathF.Sin( (float)(p6 * 2 * Math.PI) )) * ping * penv;
			float v = (hp * 0.7f * env + metal) * amp * _drumGain * _drumHighMul;
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
