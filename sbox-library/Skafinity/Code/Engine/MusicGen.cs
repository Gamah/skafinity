using System;
using System.Collections.Generic;
using static Skafinity.Osc;

namespace Skafinity;

/// <summary>
/// Procedural ska / reggae-rock track generator.
///
/// A seed string ("{tag}:{n}") seeds a portable PRNG (xmur3 → mulberry32); the PRNG drives
/// every musical choice — tempo, key, progression, bass / skank / organ / lead / drum
/// patterns — so within one build the same seed always yields the same song. Output is
/// interleaved stereo 16-bit PCM (for SoundStream / Web Audio) or a WAV (debug/export).
///
/// SCOPE OF THAT GUARANTEE: one build. The s&amp;box library and the web wasm bundle compile
/// this same source, so they agree with each other — that is the parity that matters, and it
/// is structural rather than something to verify. Across commits, audio is EXPECTED to change
/// whenever the engine does; there is no golden-audio contract and no back-compat for old
/// seeds. See PLAN.md.
///
/// Synthesis: subtractive — unison-detuned oscillators through a resonant low-pass
/// state-variable filter with a cutoff envelope (warm, not "8-bit"); full synth drum kit
/// (kick/snare/toms/hats/crash + fills). Default voicing aims for a Sublime vibe: laid-back
/// reggae-rock tempo, bass-forward, prominent clean skank + organ bubble.
///
/// This class is split across Code/Engine/ — one partial per concern. This file holds the
/// per-song state every other partial reads, the constructor, and the public entry points
/// (whole-song and chunked). The engine stays framework-free (System, System.Collections
/// .Generic, System.Text only): no Sandbox.* and no web/Emscripten-isms, which is what lets
/// the one source compile to both targets.
/// </summary>
public sealed partial class MusicGen
{
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

	/// <summary>
	/// The rendered mix's level BEFORE the master bus — peak, and RMS over everything above
	/// silence.
	///
	/// This is the instrument the per-voice <c>*Balance</c> values are tuned with, and the reason
	/// it exists: the master bus peak-normalizes, so rendering one voice on its own and measuring
	/// the OUTPUT tells you nothing about how loud that voice sits in a mix — every solo comes
	/// back normalized to the same peak. Measure here, between the render and the master.
	/// Call after <see cref="RenderPitchedRange"/>, instead of <see cref="FinishStereo"/>.
	/// </summary>
	internal (float Peak, double Rms) RawLevels()
	{
		float peak = 0; double sum = 0; int n = 0;
		for ( int i = 0; i < _bufL.Length; i++ )
		{
			float a = Math.Max( MathF.Abs( _bufL[i] ), MathF.Abs( _bufR[i] ) );
			peak = Math.Max( peak, a );
			if ( a > 0.0005f ) { sum += (double)_bufL[i] * _bufL[i] + (double)_bufR[i] * _bufR[i]; n += 2; }
		}
		return (peak, n > 0 ? Math.Sqrt( sum / n ) : 0);
	}

	GenreProfile _prof;      // the genre's character table — every per-genre decision reads this
	int[] _scale, _prog;
	int[] _voicing;          // the song's chord voicing, in scale-degree offsets (Harmony)
	int _rootMidi;
	Instrument _lead;
	float _leadPan;
	float _widthScale = 1f;  // STEREO WIDTH slider (PanAmount) as a 0..1 master: scales the drum
	                         // pan AND the double-tracking spread/decorrelation. 1 = full (design)
	                         // width; 0 = everything collapses to centre (mono).
	float _drumPan = DrumPan;// per-song effective drum spread = DrumPan * _widthScale
	bool _hasHorns;
	Pattern _hornFig;        // the horn section's 2-bar call-and-response figure
	Pattern _bassPat;        // the song's bass line — a Pattern, so it can be a 2- or 4-bar phrase
	Pattern _compFig;        // the main chordal voice's comp figure (the CURRENT section's)
	Pattern _keysFig;        // the second chordal voice's figure (null where the genre has none)
	// The song's own figures — what its choruses play. Other sections draw their own against a
	// stream keyed by section type, so the backing contrasts instead of looping one cell all song.
	Pattern _songComp, _songKeys, _songBass;
	DrumGroove _groove;      // the song's groove — per-genre tables, not a shared switch default
	bool _riffBass;          // the bass reads the riff's onsets instead of playing its own pattern
	readonly List<Hit> _riffOnsets = new(); // this bar's riff, for the bass to double
	bool _ride;              // per-SECTION: ride cymbal drives the eighth pulse instead of closed hats (set in RenderSection from _ridePref)
	float _ridePref;         // per-song lean toward riding the ride vs the hats; each section rolls its own _ride against this
	bool _crashBrightLeft;   // per-song: which side the kit's two crashes sit on (bright crash left ⇄ dark crash right, or flipped)
	bool _organBubble;
	bool _fast;
	int _genre;              // 0 ska, 1 rock, 2 country, 3 metal, 4 punk, 5 pop
	int _chordBars = 2;      // bars per chord — the genre's harmonic rhythm (GenreProfile.ChordBars)
	bool _hornLead;          // the lead line is the ska horn section rather than a lead guitar
	string _tag;             // the per-song seed string, reused to seed per-section streams
	Timing _time;            // the song's time base: eighth length, swing, kit push (see Timing.cs)
	float _drumTone = 0.5f;  // DrumTone 0..1 → toms↔cymbals CONTENT bias in fills/groove decoration
	float _drumLowMul = 1f;  // DrumTone + the genre mix trim → kick/tom/bass gain lean
	float _drumHighMul = 1f; // DrumTone + the genre mix trim → hat/cymbal gain lean
	float _midMul = 1f;      // the genre mix trim on the body of the mix (guitars, keys, horns)

	// ── per-SECTION state ──
	// Set once per section in RenderSection; every voice reads these instead of asking "am I in
	// a verse?" (see Part). This is what makes a chorus a chorus rather than a repeat.
	int[] _sectionStart = Array.Empty<int>(); // first tick of each section
	int _sectionTick;        // the current section's first tick — patterns loop from here
	int _barTick;            // the current bar's first tick — the accent grid is relative to it
	float _energy = 1f;      // 0 = as thin as the arrangement gets, 1 = full band
	float _feel = 1f;        // pattern-rate multiplier: 0.5 half time, 2 double time
	int _displace;           // metric displacement of the comp, in ticks
	int _keyShift;           // semitones this section is transposed by (the final-chorus lift)
	Section _sectionType;    // which kind of section is playing — voices that must not double the
	                         // tune (the ska horn section) ask TuneFor() about it
}
