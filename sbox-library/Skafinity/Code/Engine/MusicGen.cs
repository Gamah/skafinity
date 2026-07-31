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

	const int EighthsPerBar = 8;

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
	int _genre;              // 0 ska, 1 rock, 2 country, 3 metal, 4 punk, 5 pop
	string _tag;             // the per-song seed string, reused to seed per-section streams
	Timing _time;            // the song's time base: eighth length, swing, kit push (see Timing.cs)
	float _drumTone = 0.5f;  // DrumTone 0..1 → toms↔cymbals CONTENT bias in fills/groove decoration
	float _drumLowMul = 1f;  // DrumTone → kick/tom gain lean (gentle, on top of the content bias)
	float _drumHighMul = 1f; // DrumTone → hat/cymbal gain lean (gentle, on top of the content bias)
}
