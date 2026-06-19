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
		// Per-part kit trims. The kit is balanced for EQUAL PEAK LEVEL in the file internally
		// (see *Balance consts below), so these knobs all share a 1.0 default — every piece
		// peaks at the same level out of the box and these are pure user trims around that.
		public float KickVol = 1.00f;
		public float SnareVol = 1.00f;
		public float TomVol = 1.00f;
		public float HatVol = 1.00f;
		public float CrashVol = 1.00f;
		public float DrumVol = 1.00f;         // master gain over the whole kit
		// Drum "tone" — toms↔cymbals bias for the PART. 0 = boom (fills/decoration lean to
		// toms, cymbals pulled back), 0.5 = neutral, 1 = bright (fills/decoration lean to
		// cymbals, toms pulled back). Drives both what's played and a gentle per-voice gain lean.
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
		public float LeadGtrDrive = 5.0f;     // distortion amount (tanh drive) — new floor of the DISTORTION knob
		public float LeadGtrBend = 0.30f;     // rock lead "bendiness" 0..1 — propensity to bend up into notes and scoop

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
		public float MasterReverb = 0.5f;
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

		// STEREO WIDTH (vibe slider): master 0..1 stereo amount. Scales the lead/horn placement
		// AND the drum pan + double-tracking spread/decorrelation (see _widthScale). 1 = full
		// design width (the "100%" the rest of the mix is tuned for); 0 = mono.
		public float PanAmount = 1.0f;

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

		// ── Advanced peak-balance / level tuning (NOT vibe knobs) ──
		// These were hardcoded consts; they're Config fields so the house mix can be retuned at
		// runtime (web: config.json) without a rebuild. They're deliberately kept OUT of the vibe
		// seed and the per-genre slider grid (see VibeCodec.AdvancedFields) — they shape the
		// baseline mix, not a song's shareable identity. Edit these to re-balance, not the *Vol
		// defaults (which stay a flat 1.0 so the knobs read as pure user trims around this).
		//
		// KitPresence: drums are short transients fighting a sustained melodic bed; this baseline
		// boost lets the kit sit in the mix at DRUMS = 1.0 (parity with the other voice sliders).
		public float KitPresence = 2.0f;
		// Kit per-voice level balance. The pieces synthesise at very different raw levels (a kick
		// is huge, a hat is thin noise). Starting point was EQUAL PRE-MASTER PEAK (kick 0.40 /
		// snare 0.476 / tom 0.496 / hat 0.582 / crash 0.515), then hand-tuned by ear: equal peak
		// buried the sparse kick/snare/toms under the constant hats, so drums were pushed back up
		// and the hats backed off.
		public float KickBalance = 0.800f;
		public float SnareBalance = 0.900f;
		public float TomBalance = 0.780f;     // tuned by ear
		public float HatBalance = 0.407f;     // equal-peak baseline (0.582) backed off 30% by ear
		public float CrashBalance = 0.515f;
		// Melodic-voice peak balance — the instrument analog of the kit balances above (same
		// equal-peak-then-tune workflow). Bass measured the same peak across genres, so one value.
		public float BassBalance = 0.733f;
		public float SkankBalance = 1.223f;
		public float OrganBalance = 1.237f;
		public float MelodyBalance = 0.896f;  // ska horn lead
		public float HornBalance = 1.142f;    // backing horn section
		public float KeysBalance = 1.065f;    // rock offbeat keys
		public float RhythmGtrBalance = 1.053f;
		public float LeadGtrBalance = 0.896f;

		// ── Stereo double-tracking (width) ── (config-only; see VibeCodec.AdvancedFields)
		// Widen the non-drum voices the way a band double-tracks guitars (bass excepted — it
		// stays centred for a tight, mono-safe low end): each widened note is rendered
		// as TWO decorrelated "takes" panned apart — NOT one mono signal copied to both channels
		// (that just collapses to centre). The width comes entirely from the micro-differences
		// between the takes: a few cents of detune, a small constant timing offset + per-note
		// jitter, independent oscillator start phase, and per-note amplitude/cutoff variation.
		// Tunable at runtime (no rebuild). Drums are panned separately (see DrumPan).
		public float DoubleTrack = 1f;        // master enable: <0.5 = off (single centred take, as before)
		public float WidthBacking = 0.5f;     // pan spread (±) for backing voices — 50% L/R
		public float WidthLead = 1.0f;        // pan spread (±) for the lead — full wide (100%)
		public float WidthDetune = 6f;        // cents BETWEEN the two takes (split ±half each)
		public float WidthDelayMs = 9f;       // constant timing offset of the 2nd take (ms)
		public float WidthJitterMs = 4f;      // extra random per-note timing jitter (ms, ± per take)
		public float WidthAmpVar = 0.08f;     // per-note amplitude variation between takes (0..1)
		public float WidthCutoffVar = 0.10f;  // per-note filter-cutoff variation between takes (0..1)
	}

	readonly Config _c;
	readonly int _sr;
	readonly float _drumGain;   // master kit gain — straight 0..1.5 slider × Config.KitPresence baseline
	float[] _bufL, _bufR;

	MusicGen( Config c ) { _c = c ?? new Config(); _sr = _c.SampleRate; _drumGain = Math.Clamp( _c.DrumVol, 0f, 1.5f ) * _c.KitPresence; }

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
	//   short[] pcm = g.FinishStereo();                    // master + interleave
	public static MusicGen BeginPlan( string tag, Config cfg )
	{
		var g = new MusicGen( cfg );
		g.ComposePlan( tag );
		return g;
	}

	public int TotalSamples => _bufL?.Length ?? 0;
	public int SampleRate => _sr;

	/// <summary>Master-normalize and interleave to stereo 16-bit PCM. Call after every
	/// <see cref="RenderPitchedRange"/> window has finished.</summary>
	public short[] FinishStereo() => ToShorts( Master() );

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

	// ── Country harmony (Genre 2) ──
	// Bright and major — country lives in major / mixolydian. Same RNG draws as the other
	// tables, so songs in other genres are untouched.
	static readonly int[][] CountryScales =
	{
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 10 }, // mixolydian
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major
		new[] { 0, 2, 4, 5, 7, 9, 11 }, // major (weighted)
	};

	static readonly int[][] CountryProgressions =
	{
		new[] { 0, 3, 4, 0 }, // I–IV–V–I (the country backbone)
		new[] { 0, 0, 4, 4 }, // I–V vamp
		new[] { 0, 3, 0, 4 }, // I–IV–I–V
		new[] { 0, 4, 5, 3 }, // I–V–vi–IV
		new[] { 0, 4, 0, 4 }, // I–V two-chord
	};

	// "Boom-chick" alternating root/fifth on the beats (the guitar/snare take the off "chick"),
	// walking up to the next chord on the approach.
	static readonly int[][] CountryBassPatterns =
	{
		new[] { 0, Rest, 7, Rest, 0, Rest, 7, Approach },  // alternating root–fifth
		new[] { 0, Rest, 7, Rest, 12, Rest, 7, Approach }, // root–fifth with the octave
		new[] { 0, Rest, 7, Rest, 0, Rest, 5, Approach },  // root–fifth, lean on the 4th
		new[] { 0, Rest, 4, Rest, 7, Rest, 5, Approach },  // walking-ish
	};

	// ── Metal harmony (Genre 3) ──
	// Dark and tight — natural minor / phrygian / harmonic minor for the menacing power-chord
	// riffs. Same RNG draws as the other tables.
	static readonly int[][] MetalScales =
	{
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (aeolian)
		new[] { 0, 1, 3, 5, 7, 8, 10 }, // phrygian (the metal mode)
		new[] { 0, 2, 3, 5, 7, 8, 11 }, // harmonic minor
		new[] { 0, 2, 3, 5, 7, 8, 10 }, // natural minor (weighted)
	};

	// Degrees read against the (minor) scale: 5 = ♭VI, 6 = ♭VII, 1 = ♭II, 3 = iv.
	static readonly int[][] MetalProgressions =
	{
		new[] { 0, 5, 6, 0 }, // i–♭VI–♭VII–i
		new[] { 0, 6, 5, 6 }, // i–♭VII–♭VI–♭VII (driving)
		new[] { 0, 1, 0, 6 }, // i–♭II–i–♭VII (phrygian menace)
		new[] { 0, 0, 5, 6 }, // i pedal → ♭VI–♭VII
		new[] { 0, 6, 3, 0 }, // i–♭VII–iv–i
	};

	// Driving roots locked to the double-kick; octave pushes for the gallop.
	static readonly int[][] MetalBassPatterns =
	{
		new[] { 0, 0, 0, 0, 0, 0, 0, Approach },         // straight chug
		new[] { 0, 0, 12, 0, 0, 0, 12, Approach },       // root with octave pushes
		new[] { 0, 0, 0, 12, 0, 0, 0, Approach },        // syncopated octave
		new[] { 0, Rest, 0, 0, Rest, 0, 0, Approach },   // syncopated driver
	};

	// The genre's harmony tables (one RNG Pick each, so other genres stay byte-identical).
	static int[][] ScalesFor( int g ) => g switch
	{ 1 => RockScales, 2 => CountryScales, 3 => MetalScales, _ => Scales };
	static int[][] ProgressionsFor( int g ) => g switch
	{ 1 => RockProgressions, 2 => CountryProgressions, 3 => MetalProgressions, _ => Progressions };
	static int[][] BassPatternsFor( int g ) => g switch
	{ 1 => RockBassPatterns, 2 => CountryBassPatterns, 3 => MetalBassPatterns, _ => BassPatterns };

	int[] _scale, _prog;
	int _rootMidi;
	Instrument _lead;
	float _leadPan;
	float _widthScale = 1f;  // STEREO WIDTH slider (PanAmount) as a 0..1 master: scales the drum
	                         // pan AND the double-tracking spread/decorrelation. 1 = full (design)
	                         // width; 0 = everything collapses to centre (mono).
	float _drumPan = DrumPan;// per-song effective drum spread = DrumPan * _widthScale
	bool _hasHorns;
	bool[] _hornMask;
	int[] _bassPat;
	int _drumStyle;          // 0 one-drop, 1 steppers, 2 straight backbeat
	int[] _kickAccents = Array.Empty<int>(); // per-song backbeat kick accents (see BackbeatKickAccents)
	bool _ride;              // per-SECTION: ride cymbal drives the eighth pulse instead of closed hats (set in RenderSection from _ridePref)
	float _ridePref;         // per-song lean toward riding the ride vs the hats; each section rolls its own _ride against this
	bool _crashBrightLeft;   // per-song: which side the kit's two crashes sit on (bright crash left ⇄ dark crash right, or flipped)
	bool _organBubble;
	bool _fast;
	int _genre;              // 0 ska, 1 rock
	string _tag;             // the per-song seed string, reused to seed per-section streams
	int _drumPush;           // per-song-constant kit timing bias in samples (− ahead / + back)
	float _swing;            // per-song shuffle: offbeat eighths pushed late by this fraction of an eighth (whole band, via Swung)
	float _drumTone = 0.5f;  // DrumTone 0..1 → toms↔cymbals CONTENT bias in fills/groove decoration
	float _drumLowMul = 1f;  // DrumTone → kick/tom gain lean (gentle, on top of the content bias)
	float _drumHighMul = 1f; // DrumTone → hat/cymbal gain lean (gentle, on top of the content bias)

	// ── Song structure ──
	// A song is an ordered list of sections. Hardcoded for now (will be RNG-generated once
	// there are more part types); the fixed run is intro → chorus → verse(0) → chorus →
	// verse(1) → chorus → ending. Non-lead voices are seeded by section TYPE so every chorus
	// (and both verses) play identical backing; the lead is seeded by type + verse index so
	// it evolves across the Nth verse; the section-end fill is seeded by absolute index so
	// every section closes with a different fill.
	enum Section { Intro, Chorus, Verse, Ending }

	// Extra seconds appended after the last bar so the ending's final tonic chord (and the
	// master reverb) can ring out naturally instead of being clipped at the buffer edge.
	const float RingOutTail = 2.4f;

	readonly struct Part
	{
		public readonly Section Type; public readonly int Bars; public readonly int VerseIndex;
		public Part( Section t, int bars, int verse ) { Type = t; Bars = bars; VerseIndex = verse; }
	}

	static List<Part> BuildStructure() => new()
	{
		new Part( Section.Intro,  2, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 0 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Verse,  8, 1 ),
		new Part( Section.Chorus, 8, 0 ),
		new Part( Section.Ending, 2, 0 ),
	};

	static string SectionKey( Section s ) => s switch
	{
		Section.Intro => "intro",
		Section.Chorus => "chorus",
		Section.Verse => "verse",
		_ => "ending",
	};

	// Single-threaded generation (used by Generate / GenerateSamples). The controller
	// uses the chunked path instead (BeginPlan → parallel RenderPitchedRange → FinishStereo).
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
		_genre = Math.Clamp( _c.Genre, 0, 3 );
		var rng = new Rng( Xmur3( _tag.ToLowerInvariant() ) );

		_fast = rng.Chance( _c.FastChance );              // TEMPO BIAS
		int bpm = _fast
			? _c.FastBpmMin + rng.Int( Math.Max( 1, _c.FastBpmMax - _c.FastBpmMin + 1 ) )
			: _c.BpmMin + rng.Int( Math.Max( 1, _c.BpmMax - _c.BpmMin + 1 ) );
		_scale = rng.Pick( ScalesFor( _genre ) );
		_prog = rng.Pick( ProgressionsFor( _genre ) );
		_rootMidi = 28 + rng.Int( 8 );                    // E1..B1 bass root
		_lead = Instrument.Trumpet;                       // ska lead is fixed; other genres use guitar
		_leadPan = (rng.Next() * 2f - 1f) * _c.PanAmount;
		_widthScale = Math.Clamp( _c.PanAmount, 0f, 1f );
		_drumPan = DrumPan * _widthScale;
		_bassPat = rng.Pick( BassPatternsFor( _genre ) );
		_drumStyle = _genre switch                        // 0 ska rolls a style; the rest are fixed
		{
			1 => 2,                                       // rock: straight backbeat
			2 => 2,                                       // country: train-beat backbeat
			3 => 3,                                       // metal: double-kick
			_ => _fast ? 2 : rng.Int( 2 ),
		};
		_organBubble = true;
		_hasHorns = true;
		_hornMask = new bool[EighthsPerBar];
		_hornMask[0] = true;
		for ( int e = 1; e < EighthsPerBar; e++ )
			_hornMask[e] = rng.Chance( _c.HornDensity * (e % 2 == 1 ? 1.3f : 0.5f) );
		// How much this song leans on the ride cymbal vs the closed hats for the main pulse.
		// Every song can do both — each SECTION rolls its own choice against this preference
		// (see RenderSection), so a song can hat the verse and ride the chorus. The lean is
		// genre-biased (rock/metal ride more) and spread per song so some strongly prefer one.
		// One rng.Next() (same draw count as the old single Chance), so later draws stay aligned.
		float rideBase = _genre switch { 1 => 0.55f, 3 => 0.65f, 2 => 0.30f, _ => 0.40f };
		_ridePref = Math.Clamp( rideBase - 0.25f + 0.5f * rng.Next(), 0f, 1f );
		// Which side the two crashes sit on (±25%); flips per song so the stereo image varies.
		_crashBrightLeft = rng.Chance( 0.5f );
		// This song's backbeat kick personality — which off-beat eighths the kick leans into
		// beyond the fixed beat-1 & 3 anchors. Only the straight backbeat (rock/country/fast
		// ska) reads it; drawn last so it shifts no earlier choice.
		_kickAccents = rng.Pick( BackbeatKickAccents );

		_swing = _fast ? _c.FastSwing : _c.Swing;
		double secPerEighth = 60.0 / bpm / 2.0;
		int spe = (int)Math.Round( _sr * secPerEighth );

		// Drum tone (toms↔cymbals) → per-voice gain split, and drive (pull↔push) → a constant
		// kit timing bias (− = ahead/push, + = behind/lay back; 0.5 = dead on).
		float dt = Math.Clamp( _c.DrumTone, 0f, 1f );
		_drumTone = dt;
		// Gentle gain lean (neutral at 0.5 so the balanced kit is untouched there); the
		// bulk of the toms↔cymbals bias now comes from what the part actually plays.
		_drumLowMul = 1.2f - 0.4f * dt;
		_drumHighMul = 0.7f + 0.6f * dt;
		_drumPush = (int)Math.Round( (0.5f - Math.Clamp( _c.DrumDrive, 0f, 1f )) * 2f * 0.13f * spe );

		// Lay out the structure and size the buffers to its total length.
		var structure = BuildStructure();
		int totalBars = 0;
		foreach ( var p in structure ) totalBars += p.Bars;
		// Size to the structure plus a ring-out tail, so the ending's final chord and the
		// reverb decay into real silence past the last bar rather than being cut off.
		int structured = spe * EighthsPerBar * totalBars;
		int total = structured + (int)(_sr * RingOutTail);
		_bufL = new float[total];
		_bufR = new float[total];

		int barCursor = 0;
		for ( int si = 0; si < structure.Count; si++ )
		{
			var part = structure[si];
			RenderSection( part, si, barCursor * EighthsPerBar * spe, spe, secPerEighth );
			barCursor += part.Bars;
		}
	}

	// Render one section. Each voice gets its own per-section RNG stream keyed so that repeats
	// of a section type reproduce identical backing, while the lead key folds in the verse
	// index (so the Nth verse's lead differs) and the fill key folds in the absolute section
	// index (so every section closes with a unique fill).
	void RenderSection( Part part, int absIndex, int sectionStart, int spe, double secPerEighth )
	{
		string bk = SectionKey( part.Type );
		string lk = part.Type == Section.Verse ? $"verse:{part.VerseIndex}" : bk;
		var bassRng = new Rng( Xmur3( $"{_tag}:bass:{bk}" ) );
		var bassOrn = new Rng( Xmur3( $"{_tag}:bassorn:{bk}" ) );
		var rhythmRng = new Rng( Xmur3( $"{_tag}:rhythm:{bk}" ) );
		var keysRng = new Rng( Xmur3( $"{_tag}:keys:{bk}" ) );
		var hornRng = new Rng( Xmur3( $"{_tag}:horn:{bk}" ) );
		var leadRng = new Rng( Xmur3( $"{_tag}:lead:{lk}" ) );
		// Expression (vibrato/bend/glide/scoop) rolls off their own stream so adding them
		// leaves every voice's existing note CHOICES untouched — only pitch-shaping is layered on.
		var exprRng = new Rng( Xmur3( $"{_tag}:expr:{lk}" ) );
		var noise = new Rng( Xmur3( $"{_tag}:drums:{bk}" ) );
		// Hats vs ride is decided per section off its own stream (keyed by section TYPE, so every
		// chorus rides-or-hats the same, but a verse can differ). Rolled against the song's
		// _ridePref — independent stream, so it disturbs no other voice's draws.
		_ride = new Rng( Xmur3( $"{_tag}:ride:{bk}" ) ).Chance( _ridePref );
		var fillRng = new Rng( Xmur3( $"{_tag}:fill:{absIndex}" ) );
		var fillNoise = new Rng( Xmur3( $"{_tag}:fillnoise:{absIndex}" ) );

		bool isIntro = part.Type == Section.Intro;
		bool isEnding = part.Type == Section.Ending;

		for ( int bar = 0; bar < part.Bars; bar++ )
		{
			int chord = (bar / 2) % _prog.Length;
			int nextChord = ((bar / 2) + 1) % _prog.Length;
			int barStart = sectionStart + bar * EighthsPerBar * spe;

			// The ending lands on a held tonic chord that rings out — the band stops on the
			// "one", it doesn't roll forward as if looping. The bar before it fills to lead in.
			if ( isEnding && bar == part.Bars - 1 )
			{
				RenderEnding( barStart, spe, noise );
				continue;
			}
			// Every section's last bar fills; for the ending that fill moves one bar earlier so
			// it sets up the final hit instead of pushing past the end into nothing.
			bool lastBar = bar == part.Bars - 1 || (isEnding && bar == part.Bars - 2);

			// Intro build-in: rather than slamming in at full band (which reads as looping back
			// into the middle of the song), the voices enter a layer at a time — bass + drums
			// lay down the groove first, then the chordal voice, then the horns/lead on top.
			bool playChord = !isIntro || bar >= 1;
			bool playTop = !isIntro || bar >= 2;

			RenderBassBar( barStart, spe, secPerEighth, chord, nextChord, bassRng, bassOrn, exprRng );
			if ( playChord )
				switch ( _genre )
				{
					case 1: // rock: keys comp + power-chord guitar
					case 2: // country: honky-tonk piano comp + strummed twang guitar
						RenderKeysBar( barStart, spe, secPerEighth, chord, keysRng, exprRng );
						RenderRhythmGuitarBar( barStart, spe, secPerEighth, chord, rhythmRng, exprRng );
						break;
					case 3: // metal: palm-muted gallop riff carries the bar
						RenderMetalRiffBar( barStart, spe, secPerEighth, chord, rhythmRng, exprRng );
						break;
					default: // ska: skank chop + horn stabs
						RenderRhythmBar( barStart, spe, secPerEighth, chord, rhythmRng, exprRng );
						if ( _hasHorns && playTop )
							RenderHornStabs( barStart, spe, secPerEighth, chord, hornRng, exprRng );
						break;
				}
			RenderDrumBar( barStart, spe, lastBar, noise, fillRng, fillNoise );

			// No lead in the ending: a lead phrase starts every two bars and runs ~two bars, so in
			// the short outro it spilled melody notes across the held final chord — the band has
			// already resolved and stopped, so the lead must too (it read as "random notes after
			// the hold"). The ending is just the fill bar → the ringing tonic.
			if ( playTop && bar % 2 == 0 && !isEnding )
				RenderLeadPhrase( barStart, spe, secPerEighth, chord, leadRng, exprRng );
		}
	}

	// The song's final downbeat. The whole band resolves home to the tonic on the "one" and
	// lets the chord ring out into the tail RingOutTail reserved — a landing, not a turnaround.
	// The previous bar's fill (see RenderSection) leads in and its terminal crash lands right
	// here, so the ending itself only adds the kick + the sustained, decaying chord.
	// ── Swing: warp the eighth-note grid so the whole band shuffles together ──
	// On-beat (even) eighths are anchors that stay put; off-beat (odd) eighths are pushed late
	// by _swing of an eighth, and positions between anchors interpolate. So a sixteenth (e+0.5)
	// or a triplet subdivision lands on the SAME warped grid as the eighths — every voice swings
	// in lockstep instead of the skank chop alone. `eighths` is a within-bar position measured in
	// eighth-notes (0.5 = a sixteenth); returns the absolute sample index.
	int Swung( int barStart, int spe, double eighths )
	{
		double baseE = Math.Floor( eighths );
		double frac = eighths - baseE;
		long slot = (long)baseE;
		double startShift = (slot & 1) == 1 ? _swing : 0.0;          // this eighth's onset shift
		double endShift   = ((slot + 1) & 1) == 1 ? _swing : 0.0;    // the next eighth's onset shift
		double pos = baseE + startShift + frac * (1.0 + endShift - startShift);
		return barStart + (int)Math.Round( pos * spe );
	}

	void RenderEnding( int barStart, int spe, Rng noise )
	{
		int at = Math.Max( 0, barStart );
		RenderKick( at, noise );

		// Resolve to the progression's tonic (slot 0) wherever the chord cycle happened to
		// land, and ring it with a natural exponential tail (Sustained = false) long enough to
		// bloom into the reserved tail room. A held triad up top + the root an octave below.
		int dur = (int)(_sr * RingOutTail * 0.92f);
		int baseMidi = _rootMidi + 19;
		int[] degs = { _prog[0], _prog[0] + 2, _prog[0] + 4, _prog[0] + 7 };
		foreach ( var d in degs )
		{
			var pad = new Patch
			{
				Osc = 1, Voices = 3, Detune = _c.Detune,
				Amp = 0.7f / degs.Length, Attack = 0.006f, Decay = 1.2,
				Sustain = 0f, Sustained = false,
				Cutoff = _c.SkankCutoff, CutEnv = 700f, Reso = 0.8f,
				Drive = _genre == 3 ? 3.5f : 1.3f, Pan = 0f,
			};
			RenderPatch( at, dur, Midi( ScaleMidi( baseMidi, d ) ), pad );
		}
		var low = new Patch
		{
			Osc = 3, Voices = 2, Detune = _c.Detune * 0.4f,
			Amp = _c.BassVol * _c.BassBalance, Attack = 0.004f, Decay = 1.4,
			Sustain = 0f, Sustained = false,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		};
		RenderPatch( at, dur, Midi( ChordRoot( 0 ) ), low, mono: true );
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

	// ── Instrument expression ──
	// Four expressive PROPERTIES every pitched voice can lean on (drums are excluded). Each
	// instrument gets a genre-specific PROPENSITY for each, "based on what it is" — a brass
	// lead sings and scoops, a bass slides, a power-chord guitar stays dead straight. The
	// realization is the per-note pitch shaping in RenderEvent (vibrato depth + bend envelope).
	//   Vib    — #1 vibrato depth (a constant lean, no per-note roll)
	//   BendIn — #2 bend up INTO the note from a step below (per-note chance)
	//   Glide  — #3 portamento from the previous note's pitch (per-note chance)
	//   Scoop  — #4 bend up-and-back within the note (per-note chance)
	readonly struct Expression
	{
		public readonly float Vib, BendIn, Glide, Scoop;
		public Expression( float vib, float bendIn, float glide, float scoop )
		{ Vib = vib; BendIn = bendIn; Glide = glide; Scoop = scoop; }
	}

	const int NoPrev = int.MinValue; // "no previous note" sentinel for glide

	// The per-instrument propensity table — genre-aware. Leads route here by genre
	// ("LEAD" = ska brass, "LEAD GTR" = rock guitar). Rock lead's BENDINESS knob drives its
	// bend-in + scoop directly (that's what "bendiness" is). Tune these by ear.
	Expression Expr( string voice )
	{
		switch ( voice )
		{
			case "BASS":       return _genre switch
			{
				1 => new Expression( 0f, 0f, 0.10f, 0f ),     // rock: locked
				2 => new Expression( 0f, 0f, 0.12f, 0.03f ),  // country: a subtle slide
				3 => default,                                 // metal: dead straight, fast
				_ => new Expression( 0f, 0f, 0.25f, 0.05f ),  // reggae bass slides
			};
			case "SKANK":      return default;                          // staccato chops — dead straight
			case "ORGAN":      return new Expression( 0.15f, 0f, 0f, 0f ); // gentle bubble vibrato (only blooms on held notes)
			case "LEAD":       return new Expression( 0.35f, 0.15f, 0.10f, 0.25f ); // brass sings + scoops
			case "HORNS":      return new Expression( 0.20f, 0f, 0f, 0.20f ); // section stabs fall/scoop
			case "KEYS":       return default;                          // organ comp — locked, no wobble
			case "RHYTHM GTR": return default;                          // power chords — straight
			case "LEAD GTR":
			{
				// Country leans hard into bends (the telecaster twang); rock/metal ride the knob.
				float bend = _genre == 2 ? MathF.Max( _c.LeadGtrBend, 0.5f ) : _c.LeadGtrBend;
				return new Expression( 0.30f, bend, 0.10f, bend );
			}
			default:           return default;
		}
	}

	// A rolled-per-note voicing: the concrete pitch-shaping a note will get. Vibrato is a
	// constant depth (no draw); bend-in/glide/scoop are rolled against their propensities, so
	// only voices that lean on them ever pull from the expression stream.
	struct Voicing { public float VibDepth, BendSemis, BendTime, ScoopSemis; }

	Voicing Roll( in Expression ex, int midi, int prevMidi, Rng rng )
	{
		var v = new Voicing();
		// Vibrato depth is a SMALL pitch fraction (lean 0.5 ≈ ±10 cents) and it's delayed in
		// the synth, so notes read locked-on, not seasick. BendTime is in SECONDS — a quick
		// slide that resolves and locks, never a fraction of a long held note.
		if ( ex.Vib > 0f ) v.VibDepth = 0.003f + 0.006f * ex.Vib;
		if ( ex.Glide > 0f && prevMidi != NoPrev && rng.Chance( ex.Glide ) )
		{
			v.BendSemis = Math.Clamp( (prevMidi - midi) * 0.3f, -2f, 2f ); // lean toward the prev pitch, not all the way
			v.BendTime = 0.13f;                                      // ~130 ms portamento
		}
		else if ( ex.BendIn > 0f && rng.Chance( ex.BendIn ) )
		{
			v.BendSemis = rng.Chance( 0.5f ) ? -0.3f : -0.55f;       // a subtle lean up into pitch
			v.BendTime = 0.09f;                                      // ~90 ms bend up into pitch
		}
		if ( ex.Scoop > 0f && rng.Chance( ex.Scoop ) )
			v.ScoopSemis = rng.Chance( 0.5f ) ? 0.15f : 0.3f;        // a slight attack hump
		return v;
	}

	// Bake a rolled voicing onto a patch. VibDepth is harmless unless the patch carries a
	// vibrato RATE (p.Vibrato) — so a voice the user muted to 0 Hz stays dry — which means a
	// voice that wants expression-vibrato must set its own rate in its patch literal.
	static void ApplyVoicing( ref Patch p, in Voicing v )
	{
		if ( v.VibDepth > 0f ) p.VibDepth = v.VibDepth;
		p.BendSemis = v.BendSemis; p.BendTime = v.BendTime; p.ScoopSemis = v.ScoopSemis;
	}

	// ── Bass ──
	void RenderBassBar( int barStart, int spe, double secPerEighth, int chord, int nextChord, Rng rng, Rng bassOrn, Rng exprRng )
	{
		int root = ChordRoot( chord );
		var ex = Expr( "BASS" );
		int prevMidi = NoPrev;
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
			var vc = Roll( ex, midi, prevMidi, exprRng );
			prevMidi = midi;

			if ( off != Approach && len == 1 && bassOrn.Chance( ornChance ) )
			{
				int n = bassOrn.Chance( 0.65f ) ? 2 : 3;        // 16th pair / 16th triplet
				int step = spe / n;
				int[] moves = { 0, 7, 12 };                     // root / fifth / octave
				for ( int k = 0; k < n; k++ )
				{
					int bm = midi + (k == 0 ? 0 : moves[bassOrn.Int( moves.Length )]);
					EmitBass( Swung( barStart, spe, e + (double)k / n ), (int)(step * 0.9f), bm, secPerEighth / n * 0.8, vc );
				}
				continue;
			}

			EmitBass( Swung( barStart, spe, e ), (int)(spe * len * 0.95f), midi, secPerEighth * len * 0.8, vc );
		}
	}

	void EmitBass( int at, int dur, int midi, double decaySec, in Voicing vc )
	{
		// Triangle body for a round, deep reggae/dub bass (saw alone read as too
		// buzzy) — but triangle alone was too subtle, so layer a quieter square
		// underneath for presence/definition. The square's odd harmonics give the
		// bass its bite; both share the bass low-pass so the tone stays warm.
		var body = new Patch
		{
			Osc = 3, Voices = 2, Detune = _c.Detune * 0.4f,
			Amp = _c.BassVol * _c.BassBalance, Attack = 0.004f, Decay = decaySec,
			Sustain = 0.55f, Sustained = true,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		};
		var sub = new Patch
		{
			Osc = 2, Voices = 1, Detune = 0f,
			Amp = _c.BassVol * 0.4f * _c.BassBalance, Attack = 0.004f, Decay = decaySec,
			Sustain = 0.55f, Sustained = true,
			Cutoff = _c.BassCutoff, CutEnv = 350f, Reso = 0.9f,
			Drive = _c.BassDrive, Pan = 0f,
		};
		ApplyVoicing( ref body, vc ); ApplyVoicing( ref sub, vc );
		RenderPatch( at, dur, Midi( midi ), body, mono: true );
		RenderPatch( at, dur, Midi( midi ), sub, mono: true );
	}

	// ── Skank guitar (the signature) + reggae organ bubble — offbeats, centered ──
	void RenderRhythmBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		// +24: skank/organ sit an octave above the old register — at +12 (E2..B2) the
		// chop was too low/muddy to cut through and read as missing. Organ stays a
		// further octave down via the -12 below.
		int gBase = _rootMidi + 24;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 7 };
		// Skank is dead straight (default); the organ bubble gets a gentle vibrato depth.
		var organVc = Roll( Expr( "ORGAN" ), 0, NoPrev, exprRng );

		for ( int e = 1; e < EighthsPerBar; e += 2 ) // offbeats
		{
			int at = Swung( barStart, spe, e );   // odd e = offbeat → Swung pushes it late by _swing

			// bright, thin, short guitar chop
			foreach ( var d in degs )
				RenderPatch( at, (int)(spe * Math.Clamp( _c.SkankChop, 0.15f, 1f )), Midi( ScaleMidi( gBase, d ) ), new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune,
					Amp = _c.SkankVol * _c.SkankBalance / degs.Length, Attack = 0.002f, Decay = 0.10,
					Sustain = 0f, Sustained = false,
					Cutoff = _c.SkankCutoff, CutEnv = 1500f, Reso = 0.8f,
					Highpass = _c.SkankHighpass, Drive = _c.SkankDrive, Pan = 0f,
				} );

			// reggae organ "bubble": a softer, rounder offbeat under the guitar
			if ( _organBubble )
				foreach ( var d in degs )
				{
					var organ = new Patch
					{
						Osc = 0, Voices = 2, Detune = _c.Detune * 0.5f,
						Amp = _c.OrganVol * _c.OrganBalance / degs.Length, Attack = 0.004f, Decay = 0.16,
						Sustain = 0.3f, Sustained = false,
						Cutoff = _c.OrganCutoff, CutEnv = 0f, Reso = 1.0f, Drive = 1.1f, Pan = 0f,
						Vibrato = _c.OrganVibrato,
					};
					ApplyVoicing( ref organ, organVc );
					RenderPatch( at, (int)(spe * 0.55f), Midi( ScaleMidi( gBase, d ) - 12 ), organ );
				}
		}
	}

	// ── Lead melody (chord-tone locked → consonant) ──
	void RenderLeadPhrase( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		int slots = EighthsPerBar * 2;
		int melBase = _rootMidi + 24;
		int[] tones = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 6 }; // chord tones
		int degree = tones[rng.Int( 3 )];
		bool guitarLead = _genre != 0;                    // ska is the only horn lead
		float amp = guitarLead ? _c.LeadGtrVol * _c.LeadGtrBalance : _c.MelodyVol * _c.MelodyBalance;
		float drive = guitarLead ? _c.LeadGtrDrive : _c.MelodyDrive;
		// Rock lead trades fast RUNS for BENDINESS (handled via expression), so its run rate is
		// forced to 0; metal shreds (a high floor of runs); ska/country keep the TRIPLETS knob.
		float tripChance = _genre switch
		{
			1 => 0f,
			3 => MathF.Max( _c.TripletChance, 0.4f ),
			_ => _c.TripletChance,
		};
		var ex = guitarLead ? Expr( "LEAD GTR" ) : Expr( "LEAD" );
		int prevMidi = NoPrev;

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
				int firstMidi = ScaleMidi( melBase, Math.Clamp( degree - n / 2, _prog[chord] - 3, _prog[chord] + 10 ) );
				var runVc = Roll( ex, firstMidi, prevMidi, exprRng );
				for ( int k = 0; k < n; k++ )
				{
					int d2 = Math.Clamp( degree + (k - n / 2), _prog[chord] - 3, _prog[chord] + 10 );
					int m2 = ScaleMidi( melBase, d2 );
					RenderLeadNote( Swung( barStart, spe, e + (double)k * spanE / n ), (int)(step * 0.9f),
						m2, amp, secPerEighth * spanE / (double)n * 0.85, drive, runVc );
					prevMidi = m2;
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

			int midi = ScaleMidi( melBase, degree );
			var vc = Roll( ex, midi, prevMidi, exprRng );
			RenderLeadNote( Swung( barStart, spe, e ), (int)(spe * len * 0.9f), midi,
				amp, secPerEighth * len * 0.7f, drive, vc );
			prevMidi = midi;
			e += len;
		}
	}

	// Dispatch a lead note to the genre's lead voice: a distorted single-note guitar for rock,
	// otherwise the ska horn (RenderLead → trumpet).
	void RenderLeadNote( int at, int dur, int midi, float amp, double decaySec, float drive, in Voicing vc )
	{
		if ( _genre != 0 )
		{
			// Twang = a bright cutoff-envelope snap on each pick (high CutEnv, decays fast) through
			// a resonant SVF, plus a BASE distortion under the slider so it reads as an electric
			// guitar even at the slider minimum. The base is genre-set: rock = 3 (overdriven),
			// metal = 4 hot (heavy), country = clean (the bite comes from the twang snap + bends,
			// not gain). The bends (BENDINESS knob → bend-in + scoop) come in via the voicing.
			float driveAmt = _genre switch
			{
				3 => 4f + MathF.Max( 1f, _c.LeadGtrDrive ),         // metal: heavy
				2 => 0.8f + 0.3f * MathF.Max( 1f, _c.LeadGtrDrive ),// country: clean twang
				_ => 3f + MathF.Max( 1f, _c.LeadGtrDrive ),         // rock
			};
			float cutEnv = _genre == 2 ? 3000f : 2200f;             // country: extra twang snap
			var gtr = new Patch
			{
				Osc = 1, Voices = 1, Detune = 0f, Amp = amp,
				Attack = 0.002f, Decay = decaySec, Sustain = 0.55f, Sustained = true,
				Cutoff = _c.LeadGtrCutoff, CutEnv = cutEnv, Reso = 0.65f,
				Drive = driveAmt, Pan = _leadPan, Vibrato = _c.MelodyVibrato,
			};
			ApplyVoicing( ref gtr, vc );
			RenderPatch( at, dur, Midi( midi ), gtr, lead: true );
			return;
		}
		RenderLead( at, dur, midi, amp, decaySec, drive, vc );
	}

	// ── Rock KEYS — their OWN part, not a double of the guitar. A syncopated organ comp (the
	// "1, &-of-2, 3, &-of-4" Charleston push) playing diatonic TRIADS (root/3rd/5th) in a high
	// keyboard register. Different notes (a real triad vs the guitar's bare power chord) AND a
	// different rhythm (a 4-hit syncopation vs the guitar's every-eighth chug), so the two
	// interlock instead of playing in lockstep. KeysChug rings the chords (0) or tightens them
	// toward short stabs (1).
	static readonly int[] KeysOnsets = { 0, 3, 4, 7 };    // eighth positions of the comp's hits
	void RenderKeysBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		int kBase = _rootMidi + 24;                        // keyboard register, an octave over the rhythm guitar
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4 };  // diatonic triad
		float chug = Math.Clamp( _c.KeysChug, 0f, 1f );
		// Country reads this comp as a honky-tonk piano: keep it clean (rock drives it dirty).
		float keysDrive = _genre == 2 ? 1f + 0.2f * MathF.Max( 1f, _c.KeysDrive )
		                              : MathF.Max( 1f, _c.KeysDrive );
		var keysVc = Roll( Expr( "KEYS" ), 0, NoPrev, exprRng ); // gentle vibrato only
		for ( int oi = 0; oi < KeysOnsets.Length; oi++ )
		{
			int e = KeysOnsets[oi];
			int nextE = oi + 1 < KeysOnsets.Length ? KeysOnsets[oi + 1] : EighthsPerBar; // ring up to the next hit
			int gap = nextE - e;
			bool ring = chug < 0.5f;
			int dur = (int)(gap * spe * Math.Max( 0.25f, 1f - 0.7f * chug ));
			double dec = secPerEighth * gap * (ring ? 0.9 : 0.4);
			foreach ( var d in degs )
			{
				var keys = new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.KeysVol * _c.KeysBalance / degs.Length,
					Attack = 0.004f, Decay = dec, Sustain = ring ? 0.6f : 0.2f, Sustained = ring,
					Cutoff = _c.KeysCutoff, CutEnv = 250f, Reso = 1.0f,
					Drive = keysDrive, Pan = 0f,
				};
				ApplyVoicing( ref keys, keysVc );
				RenderPatch( Swung( barStart, spe, e ), dur, Midi( ScaleMidi( kBase, d ) ), keys );
			}
		}
	}

	// ── Rock rhythm guitar — twangy distorted power chords. Shares the lead guitar's voice (the
	// bright cutoff-envelope "twang" through a resonant SVF) but strums root+fifth+octave and
	// runs a LOWER base distortion than the lead so the two layer instead of mush. Downbeats
	// ring; offbeats tighten toward a palm-muted chug as RhythmGtrChug rises.
	// exprRng is unused: power chords stay dead straight (Expr("RHYTHM GTR") is default), but the
	// param keeps every instrument's call site uniform.
	void RenderRhythmGuitarBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		bool country = _genre == 2;
		int root = ChordRoot( chord ) + 12;               // chunky power-chord register
		// Country strums a full open triad (root/3rd/5th/octave) clean and bright; rock chunks a
		// bare power chord (root/5th/octave) with more base distortion.
		int[] chordOffs = country ? new[] { 0, 4, 7, 12 } : new[] { 0, 7, 12 };
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		float cutEnv = country ? 2600f : 1400f;            // brighter twang for the clean strum
		float driveAmt = country ? 0.8f + 0.3f * MathF.Max( 1f, _c.RhythmGtrDrive )
		                         : 1.5f + MathF.Max( 1f, _c.RhythmGtrDrive ); // less base than lead
		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			bool accent = (e % 2) == 0;                    // downbeats ring, offbeats chug
			float lenFrac = accent ? (1f - 0.5f * chug) : (0.35f - 0.2f * chug);
			int dur = (int)(spe * Math.Max( 0.12f, lenFrac ));
			double dec = secPerEighth * (accent ? 0.8 : 0.3);
			foreach ( var o in chordOffs )
				RenderPatch( Swung( barStart, spe, e ), dur, Midi( root + o ), new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance / chordOffs.Length * (accent ? 1f : 0.7f),
					Attack = 0.002f, Decay = dec, Sustain = accent ? 0.45f : 0f, Sustained = accent,
					Cutoff = _c.RhythmGtrCutoff, CutEnv = cutEnv, Reso = 0.8f,   // twang
					Drive = driveAmt, Pan = 0f,
				} );
		}
	}

	// ── Metal rhythm guitar — palm-muted 16th-note gallop on the low root with power-chord
	// accents. The relentless 16th chug (under the double-kick) is the "fast riff" engine; the
	// downbeats and a few syncopated stabs ring a full power chord. Heavy base distortion, dark
	// and tight. rng (the rhythm stream) breaks up the accent placement so riffs vary by section.
	void RenderMetalRiffBar( int barStart, int spe, double secPerEighth, int chord, Rng rng, Rng exprRng )
	{
		int root = ChordRoot( chord );                    // low, chunky — no octave bump
		int[] power = { 0, 7, 12 };
		int six = spe / 2;
		if ( six <= 0 ) return;
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		float driveAmt = 4f + MathF.Max( 1f, _c.RhythmGtrDrive ); // heavy
		for ( int s = 0; s < EighthsPerBar * 2; s++ )     // 16 sixteenths
		{
			int at = Swung( barStart, spe, s * 0.5 );   // 16 sixteenths on the swung grid
			bool beat = s % 4 == 0;                        // quarter-note downbeats → ring a chord
			bool ring = beat || (s % 2 == 0 && rng.Chance( 0.3f )); // some offbeat eighths ring too
			int[] offs = ring ? power : new[] { 0 };       // accents = power chord, chugs = root only
			float gain = ring ? 1f : 0.6f;
			// Palm mute = short, tight; accents ring longer. Chug tightens the muted notes further.
			int dur = (int)(six * (ring ? 0.9f : Math.Max( 0.25f, 0.55f - 0.3f * chug )));
			double dec = secPerEighth * (ring ? 0.4 : 0.12);
			foreach ( var o in offs )
				RenderPatch( at, dur, Midi( root + o ), new Patch
				{
					Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
					Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance / offs.Length * gain,
					Attack = 0.002f, Decay = dec, Sustain = ring ? 0.35f : 0f, Sustained = ring,
					Cutoff = _c.RhythmGtrCutoff, CutEnv = 1100f, Reso = 0.7f,
					Drive = driveAmt, Pan = 0f,
				} );
		}
	}

	// ── Backing horns (panned spread) ──
	// Block stabs on the mask read "samey" (only eighth-note chords). A dedicated
	// stream (horn:tag, so the main composition order is unchanged) breaks them up
	// with rolling arpeggios, 16th pairs, grace pickups and varied length. Kept
	// modest — the bass got over-busy when its ornament rate ran high.
	void RenderHornStabs( int barStart, int spe, double secPerEighth, int chord, Rng orn, Rng exprRng )
	{
		int baseMidi = _rootMidi + 19;
		int[] degs = { _prog[chord], _prog[chord] + 2, _prog[chord] + 4 };
		float spread = _c.PanAmount * 0.7f;
		int six = spe / 2;
		float ornChance = 0.18f + _c.TripletChance; // ~0.24 default; rides the same knob

		// Section expression — vibrato + a chance of a scoop/fall, rolled once per onset (below)
		// and applied to every tone of that stab so the whole section bends together.
		var ex = Expr( "HORNS" );
		Voicing hornVc = default;

		// one chord-tone voice
		void Note( int at, int dur, int k, double dec, float gain )
		{
			var horn = new Patch
			{
				Osc = 1, Voices = 3, Detune = _c.Detune,
				Amp = _c.HornVol * _c.HornBalance / degs.Length * gain, Attack = 0.008f, Decay = dec,
				Sustain = 0.2f, Sustained = false,
				Cutoff = _c.HornCutoff, CutEnv = 1200f, Reso = 1.0f,
				Drive = _c.HornDrive, Pan = spread * (k / (float)(degs.Length - 1) * 2f - 1f),
				Vibrato = _c.MelodyVibrato,
			};
			ApplyVoicing( ref horn, hornVc );
			RenderPatch( at, dur, Midi( ScaleMidi( baseMidi, degs[k] ) ), horn );
		}

		// full block chord stab
		void Stab( int at, int dur, double dec, float gain )
		{
			for ( int k = 0; k < degs.Length; k++ ) Note( at, dur, k, dec, gain );
		}

		for ( int e = 0; e < EighthsPerBar; e++ )
		{
			if ( !_hornMask[e] ) continue;
			int at = Swung( barStart, spe, e );
			hornVc = Roll( ex, baseMidi, NoPrev, exprRng );

			if ( six > 0 && orn.Chance( ornChance ) )
			{
				float r = orn.Next();
				if ( r < 0.4f )
				{
					// rolling arpeggio: chord tones climb across a 16th-triplet
					int step = spe / 3;
					for ( int k = 0; k < degs.Length; k++ )
						Note( Swung( barStart, spe, e + (double)k / 3 ), (int)(step * 0.9f), k, secPerEighth / 3 * 0.8, 1f );
					continue;
				}
				if ( r < 0.75f )
				{
					// 16th pair: stab on the beat, softer echo on the "e"
					Stab( at, (int)(six * 0.85f), secPerEighth * 0.5 * 0.8, 1f );
					Stab( Swung( barStart, spe, e + 0.5 ), (int)(six * 0.85f), secPerEighth * 0.5 * 0.7, 0.6f );
					continue;
				}
				// grace pickup: a soft single tone just before the block stab
				Note( Swung( barStart, spe, e - 0.5 ), (int)(six * 0.8f), 0, secPerEighth * 0.5 * 0.6, 0.5f );
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

	void RenderLead( int at, int dur, int midi, float amp, double decaySec, float drive, in Voicing vc )
	{
		Patch p; int m = midi;
		switch ( _lead )
		{
			case Instrument.Trumpet:
				p = new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune * 0.7f, Amp = amp,
					Attack = 0.01f, Decay = decaySec, Sustain = 0.7f, Sustained = true,
					Cutoff = _c.LeadCutoff, CutEnv = 1800f, Reso = 1.0f, Drive = drive,
					Pan = _leadPan, Vibrato = _c.MelodyVibrato,
				};
				break;
			case Instrument.Trombone:
				m = midi - 12;
				p = new Patch
				{
					Osc = 1, Voices = 3, Detune = _c.Detune * 0.7f, Amp = amp * 1.1f,
					Attack = 0.02f, Decay = decaySec, Sustain = 0.7f, Sustained = true,
					Cutoff = _c.LeadCutoff * 0.7f, CutEnv = 900f, Reso = 1.0f, Drive = MathF.Max( 1f, drive * 0.8f ),
					Pan = _leadPan, Vibrato = _c.MelodyVibrato * 0.7f,
				};
				break;
			case Instrument.Sax:
				p = new Patch
				{
					Osc = 3, Voices = 2, Detune = _c.Detune * 0.5f, Amp = amp * 1.15f,
					Attack = 0.014f, Decay = decaySec, Sustain = 0.75f, Sustained = true,
					Cutoff = _c.LeadCutoff, CutEnv = 1400f, Reso = 0.7f, Drive = MathF.Max( 1.2f, drive ),
					Pan = _leadPan, Vibrato = _c.MelodyVibrato, Breath = 0.03f,
				};
				break;
			default: // Organ
				p = new Patch
				{
					Osc = 0, Voices = 3, Detune = _c.Detune * 0.6f, Amp = amp,
					Attack = 0.006f, Decay = decaySec * 1.5, Sustain = 0.9f, Sustained = true,
					Cutoff = 2600f, CutEnv = 0f, Reso = 1.0f, Drive = 1.15f,
					Pan = _leadPan, Vibrato = _c.MelodyVibrato * 0.9f,
				};
				break;
		}
		ApplyVoicing( ref p, vc );
		RenderPatch( at, dur, Midi( m ), p, lead: true );
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
		public float Vibrato;  // Hz (rate of the pitch wobble)
		public float Breath;   // 0..1 noise mix (reeds)
		// ── Expression (per-note pitch shaping; see Expression/Voicing) ──
		public float VibDepth;   // vibrato depth as a pitch fraction (0 → legacy 0.005 when Vibrato>0)
		public float BendSemis;  // pitch offset in semitones at note START, glides to 0 (bend-in / glide); −ve starts below
		public float BendTime;   // 0..1 fraction of the note over which BendSemis glides to 0
		public float ScoopSemis; // height (semitones) of a mid-note bend-up-and-back hump (0 = none)
		public float PhaseSeed;  // oscillator start phase (0..1); 0 = legacy in-phase start. Used to
		                         // decorrelate the two double-tracking takes (see RenderPatch).
	}

	// Pitched note events collected during ComposePlan, then synthesized by
	// RenderPitchedRange. Synthesis pulls no RNG, so windows parallelize across threads.
	struct NoteEvent { public int Start, Dur; public float Freq; public Patch P; }
	readonly List<NoteEvent> _events = new();

	// During ComposePlan this only enqueues; the synthesis happens in RenderPitchedRange.
	// When DoubleTrack is on, every (non-drum) note is widened into two decorrelated takes
	// panned apart — see the Config "width" block. `lead` selects the wider lead spread.
	void RenderPatch( int start, int dur, float freq, Patch p, bool lead = false, bool mono = false )
	{
		if ( start < 0 || dur <= 0 || p.Voices < 1 ) return;
		// Bass stays centred (mono): doubling/detuning low frequencies smears the low end and
		// cancels in mono, so the bass is the one voice kept dead-centre regardless of width.
		if ( mono || _c.DoubleTrack < 0.5f )
		{
			_events.Add( new NoteEvent { Start = start, Dur = dur, Freq = freq, P = p } );
			return;
		}
		// The STEREO WIDTH slider (_widthScale) scales the whole effect: pan AND the
		// decorrelation (detune / delay / jitter / phase / per-note variation, all scaled inside
		// AddTake). At 100% it's the full design width; at 0% both takes collapse onto each other
		// at centre → effectively mono.
		float width = Math.Clamp( lead ? _c.WidthLead : _c.WidthBacking, 0f, 1f ) * _widthScale;
		float halfCents = _c.WidthDetune * 0.5f * _widthScale;
		int delay = (int)(_c.WidthDelayMs * 0.001f * _sr * _widthScale);
		int jit = (int)(_c.WidthJitterMs * 0.001f * _sr * _widthScale);
		// Take A sits left and slightly flat; take B sits right, slightly sharp, and lags by the
		// constant offset. Both get independent phase + per-note amp/cutoff/timing variation, so
		// the two channels are genuinely different performances (the source of the width) rather
		// than a phantom-centre mono copy.
		AddTake( start, dur, freq, p, 0, -width, -halfCents, 0, jit );
		AddTake( start, dur, freq, p, 1, +width, +halfCents, delay, jit );
	}

	// Enqueue one double-tracking take. All per-note variation is derived from a local hash of
	// (start, freq, take) so it stays deterministic and pulls no RNG (synthesis must remain a
	// pure function for the parallel RenderPitchedRange windows).
	void AddTake( int start, int dur, float freq, Patch p, int take, float pan, float cents, int delayBase, int jit )
	{
		uint s = unchecked( (uint)start * 2654435761u + (uint)(int)freq * 40503u )
			^ (take == 0 ? 0x85EBCA6Bu : 0xC2B2AE35u);
		float r1 = Hash01( ref s );   // start phase
		float r2 = Hash01( ref s );   // amplitude
		float r3 = Hash01( ref s );   // cutoff
		float r4 = Hash01( ref s );   // timing jitter
		int st = start + delayBase + (int)((r4 * 2f - 1f) * jit);
		if ( st < 0 ) st = 0;
		var q = p;
		q.Pan = pan;
		// Phase + per-note variation also scale with the width slider, so at 0% the two takes
		// become identical and centred (clean mono) instead of a flangy centred double.
		q.PhaseSeed = r1 * _widthScale;
		q.Amp = p.Amp * (1f + (r2 * 2f - 1f) * _c.WidthAmpVar * _widthScale);
		q.Cutoff = p.Cutoff * (1f + (r3 * 2f - 1f) * _c.WidthCutoffVar * _widthScale);
		float f = freq * (float)Math.Pow( 2.0, cents / 1200.0 );
		_events.Add( new NoteEvent { Start = st, Dur = dur, Freq = f, P = q } );
	}

	// Fast deterministic [0,1) hash step (xorshift32).
	static float Hash01( ref uint s )
	{
		s ^= s << 13; s ^= s >> 17; s ^= s << 5;
		return (s & 0xFFFFFFu) / 16777216f;
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
			ph[v] = p.PhaseSeed;   // 0 for un-doubled notes → identical to the old in-phase start
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
		// Expression windows (samples): vibrato holds off then ramps in; the scoop is a quick
		// attack gesture. Kept fixed/absolute so a long held note locks on pitch after them.
		int vibDelay = (int)(0.18f * _sr);
		int vibRamp = Math.Max( 1, (int)(0.16f * _sr) );
		int scoopWin = Math.Max( 1, (int)(0.16f * _sr) );
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
			// Vibrato: subtle, and DELAYED so the note locks on pitch first and only blooms a
			// wobble if it's held — short notes stay dead-on. Depth is a small pitch fraction.
			float vib = 1f;
			if ( p.Vibrato > 0f && p.VibDepth > 0f )
			{
				float ramp = MathF.Max( 0f, (i - vibDelay) / (float)vibRamp );
				if ( ramp > 1f ) ramp = 1f;
				if ( ramp > 0f )
					vib = (float)(1.0 + p.VibDepth * ramp * Math.Sin( i / (double)_sr * p.Vibrato * 2 * Math.PI ));
			}
			// Pitch-bend envelope (semitones) on top of vibrato. Both are QUICK gestures over a
			// short fixed window so the note then sits locked on its target pitch (bendMul == 1):
			// BendSemis snaps to 0 over BendTime seconds (bend-in / glide); ScoopSemis is a fast
			// up-and-back hump confined to the attack (bend-and-release).
			float bendSemis = 0f;
			if ( p.BendSemis != 0f && p.BendTime > 0f )
			{
				int bt = Math.Min( dur, Math.Max( 1, (int)(p.BendTime * _sr) ) );
				if ( i < bt ) { float u = i / (float)bt; bendSemis += p.BendSemis * (1f - u * u * (3f - 2f * u)); }
			}
			if ( p.ScoopSemis != 0f && i < scoopWin )
				bendSemis += p.ScoopSemis * MathF.Sin( (float)(i / (float)Math.Min( dur, scoopWin ) * Math.PI) );
			float bendMul = bendSemis != 0f ? (float)Math.Pow( 2.0, bendSemis / 12.0 ) : 1f;
			for ( int v = 0; v < voices; v++ )
			{
					double dt = inc[v] * vib * bendMul;
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
	// Fixed (not randomized) stereo spread for the kit's off-centre voices: 25% each way.
	const float DrumPan = 0.25f;
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
			int at = Swung( barStart, spe, e );
			bool open = e == 7;
			float amp = e % 2 == 1 ? _c.HatVol : _c.HatVol * 0.6f;
			if ( _ride && !open )
				RenderRide( at, e % 2 == 0, amp, noise );    // bell accent on the downbeats
			else
				RenderHat( at, open, amp, noise );
			if ( !open && six > 0 && noise.Chance( busy ) )
			{
				int sixAt = Swung( barStart, spe, e + 0.5 );
				if ( _ride ) RenderRide( sixAt, false, _c.HatVol * 0.4f, noise );
				else RenderHat( sixAt, false, _c.HatVol * 0.4f, noise );
			}
		}

		if ( fillEnd )
		{
			RenderKickSnareGroove( barStart, spe, 0, 6, busy, noise );   // first 3 beats normal
			RenderFill( barStart, spe, 6, fillNoise, fillRng );
			return;
		}
		RenderKickSnareGroove( barStart, spe, 0, EighthsPerBar, busy, noise );
	}

	// Per-song kick accents for the straight backbeat: eighths (beyond the beat-1 & 3 anchors)
	// the kick leans into. e1 = "and of 1", e3 = "and of 2", e5 = "and of 3", e6 = beat 4,
	// e7 = "and of 4". One set is picked per song, then each accent is rolled per bar so the
	// groove breathes instead of stamping the same kick pattern every bar — the main nuance
	// lever that keeps rock/country from all sharing one mechanical backbeat.
	static readonly int[][] BackbeatKickAccents =
	{
		new int[0],          // bone-dry: just 1 & 3
		new[] { 3 },         // push into the snare ("and of 2")
		new[] { 7 },         // pickup into the next bar ("and of 4")
		new[] { 3, 7 },      // push + pickup
		new[] { 6 },         // driving beat-4 kick
		new[] { 5, 7 },      // syncopated "and of 3" + pickup
		new[] { 3, 6 },      // push into 3 + beat-4 drive
	};

	void RenderKickSnareGroove( int barStart, int spe, int from, int to, float busy, Rng noise )
	{
		int six = spe / 2;
		for ( int e = from; e < to; e++ )
		{
			int at = Swung( barStart, spe, e );
			int sixAt = six > 0 ? Swung( barStart, spe, e + 0.5 ) : at;
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
				case 3: // metal double-kick: 16th-note kick gallop + crashing, snare backbeat
					if ( e == 0 && noise.Chance( 0.55f ) ) RenderCrash( at, noise, noise.Chance( 0.35f ) );
					RenderKick( at, noise );
					if ( six > 0 ) RenderKick( sixAt, noise ); // the second pedal → the 16th gallop
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					break;
				default: // straight backbeat — anchors on beats 1 & 3, plus this song's kick
					     // accents, each humanised per bar so the groove breathes
					bool kick = e == 0 || e == 4;
					if ( !kick && Array.IndexOf( _kickAccents, e ) >= 0 )
						kick = noise.Chance( 0.82f );                 // mostly play the accent, occasionally lay out
					else if ( !kick && e == 3 )
						kick = noise.Chance( _c.KickSyncChance * (0.4f + busy) ); // stray push into beat 3
					if ( kick ) RenderKick( at, noise );
					if ( e == 2 || e == 6 ) RenderSnare( at, noise, false );
					else if ( noise.Chance( _c.GhostSnareChance * busy ) ) RenderSnare( at, noise, true );
					break;
			}
			// Busy fills the "e/a" sixteenths between hits: more snare as busy rises, and —
			// when the tone leans low — toms too. (Busy → snare + toms; tone → tom vs cymbal.)
			// Metal already fills every 16th with the double-kick, so it skips the ghost layer.
			if ( _drumStyle != 3 && six > 0 && e != 4 && noise.Chance( _c.GhostSnareChance * busy ) )
			{
				if ( noise.Chance( (1f - _drumTone) * 0.5f ) )
					RenderTom( sixAt, 150f + 40f * (e & 1), noise );
				else
					RenderSnare( sixAt, noise, true );
			}
		}
	}

	// Tom/snare roll across the last beat (two eighths). Straight = four 16ths;
	// triplet (TripletChance) = six even subdivisions for a rolling shuffle feel.
	// The fill rolls across the last beat — the two eighths starting at baseE (6) up to the bar
	// line — on the same swung grid as the rest of the band.
	void RenderFill( int barStart, int spe, int baseE, Rng noise, Rng rng )
	{
		// straight = four 16ths; triplet = either an eighth-note triplet (3) or a
		// faster 16th-note triplet (6) across the beat.
		int n = rng.Chance( _c.TripletChance ) ? (rng.Chance( 0.5f ) ? 3 : 6) : 4;
		int step = (spe * 2) / n;
		float[] toms = { 260f, 215f, 175f, 145f, 120f, 100f };
		// Half the hits stay snare so it still reads as a drum fill; the rest are biased by
		// DrumTone — toms when the tone leans low, cymbals (ride hits) when it leans high.
		for ( int i = 0; i < n; i++ )
		{
			int t = Swung( barStart, spe, baseE + i * 2.0 / n );
			if ( rng.Chance( 0.5f ) ) RenderSnare( t, noise, false );
			else if ( rng.Chance( _drumTone ) ) RenderRide( t, false, _c.HatVol, noise );
			else RenderTom( t, toms[i], noise );  // pan derived from pitch inside RenderTom
		}
		// crash into the downbeat (lands on the bar line, an on-beat anchor → dead straight) — a
		// bright crash or a darker, washier crash, picked off the fill stream so the cymbal colour
		// varies section to section.
		RenderCrash( Swung( barStart, spe, baseE + 2.0 ), noise, rng.Chance( 0.4f ) );
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
			float v = (body + sub + click) * _c.KickVol * _c.KickBalance * _drumGain * _drumLowMul;
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
		float amp = _c.SnareVol * _c.SnareBalance * (ghost ? 0.3f : 1f) * _drumGain;
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
		// Pan by PITCH so a given tom always sits in the same spot (rack/high left → floor/low
		// right). Mapped across the commonly-played fill range (~145 Hz floor .. 260 Hz rack) so a
		// normal fill sweeps the full field and the low toms read clearly to the right; anything
		// below the range clamps hard right. Scaled by the STEREO WIDTH slider via _drumPan.
		float u = Math.Clamp( (baseFreq - 145f) / (260f - 145f), 0f, 1f ); // 0 = low/floor, 1 = high/rack
		StereoGains( -_drumPan * (u * 2f - 1f), out float gL, out float gR );
		int dur = (int)(_sr * 0.18f);
		double decay = dur * 0.3;
		double attackDecay = dur * 0.06;        // fast-decaying upper partial → beater "snap"
		double phase = 0, phase2 = 0;
		// deterministic per-hit stick attack — a local LFSR so the shared drum RNG stream
		// (and therefore every other pattern) is left byte-identical.
		uint ns = (uint)start * 2654435761u | 1u;
		int clickLen = (int)(_sr * 0.006f);
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			float pf = baseFreq * (1f - 0.22f * t);   // less pitch sag → holds its note, more bite
			phase += pf / _sr;
			phase2 += pf * 2.5f / _sr;                 // inharmonic upper partial for attack snap
			float env = (float)Math.Exp( -i / decay );
			float aenv = (float)Math.Exp( -i / attackDecay );
			float body = MathF.Sin( (float)(phase * 2 * Math.PI) ) * env;
			float snap = MathF.Sin( (float)(phase2 * 2 * Math.PI) ) * aenv * 0.5f;
			float click = 0f;
			if ( i < clickLen )
			{
				ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
				click = ((ns & 0xffff) / 32768f - 1f) * 0.45f * (1f - i / (float)clickLen);
			}
			float v = (body + snap + click) * _c.TomVol * _c.TomBalance * _drumGain * _drumLowMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	void RenderHat( int start, bool open, float amp, Rng noise )
	{
		start = Math.Max( 0, start + _drumPush );
		// Closed/open hats live on the left of the kit (the hi-hat stand); ride sits opposite.
		StereoGains( -_drumPan, out float gL, out float gR );
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
			float v = hp * env * amp * _c.HatBalance * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	// Two crash colours off one voice: the bright crash (short-ish, high-passed high) and a
	// dark crash — lower cutoff, longer wash, a touch quieter — for a bigger china/ride-crash.
	// The kit has two crashes panned apart (±25%); the bright/dark colour picks which physical
	// cymbal, and _crashBrightLeft (per song) decides which side that is.
	void RenderCrash( int start, Rng noise, bool dark = false )
	{
		start = Math.Max( 0, start + _drumPush );
		StereoGains( (dark == _crashBrightLeft ? _drumPan : -_drumPan), out float gL, out float gR );
		int dur = (int)(_sr * (dark ? 0.9f : 0.6f));
		double decay = dur * (dark ? 0.5 : 0.45);
		float a = HpCoeff( dark ? 2600f : 4000f );
		float amp = _c.CrashVol * _c.CrashBalance * (dark ? 0.85f : 1f);
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env * amp * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	// Ride cymbal — a sustained, high-passed noise wash, nothing more. A cymbal is mostly
	// filtered noise; we deliberately render *only* that body and no pitched component. Earlier
	// versions layered inharmonic sine partials for a metallic "ping"/bell, but any tonal layer
	// (however quiet or detuned) read as a pitched ring/ding, so it's gone entirely. The bell
	// hit (on the beat) just rings a touch longer than the bow hit (on the "and"). Tracks off
	// HatVol via the caller's amp so the existing DRUMS knobs still balance it.
	void RenderRide( int start, bool bell, float amp, Rng noise )
	{
		start = Math.Max( 0, start + _drumPush );
		// Ride sits opposite the hats, on the right of the kit.
		StereoGains( _drumPan, out float gL, out float gR );
		int dur = (int)(_sr * (bell ? 0.34f : 0.22f));
		double decay = dur * 0.42;
		float a = HpCoeff( 7000f );
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * 0.7f * env * amp * _c.HatBalance * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
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
