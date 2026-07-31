using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The composition pass. Plans the whole song (RNG draws + drum synthesis written straight
// into the buffers), then renders each section, each voice on its own per-section RNG
// stream keyed so a repeated section repeats rather than re-rolls.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
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
		int beatsPerBar = 4;   // 4/4 today; Timing carries it so voices never assume
		_tag = string.IsNullOrEmpty( tag ) ? "rotaliate" : tag;
		_genre = Math.Clamp( _c.Genre, 0, 5 );
		var rng = new Rng( _tag.ToLowerInvariant() );

		// TEMPO BIAS — punk always runs hot (it's the fast genre); the roll still consumes one
		// draw so every other genre's later picks stay byte-identical.
		_fast = rng.Chance( _c.FastChance ) || _genre == 4;
		int bpm = _fast
			? _c.FastBpmMin + rng.Int( Math.Max( 1, _c.FastBpmMax - _c.FastBpmMin + 1 ) )
			: _c.BpmMin + rng.Int( Math.Max( 1, _c.BpmMax - _c.BpmMin + 1 ) );
		_scale = rng.Pick( Harmony.ScalesFor( _genre ) );
		_prog = rng.Pick( Harmony.ProgressionsFor( _genre ) );
		_rootMidi = 28 + rng.Int( 8 );                    // E1..B1 bass root
		_lead = Instrument.Trumpet;                       // ska lead is fixed; other genres use guitar
		_leadPan = (rng.Next() * 2f - 1f) * _c.PanAmount;
		_widthScale = Math.Clamp( _c.PanAmount, 0f, 1f );
		_drumPan = DrumPan * _widthScale;
		_bassPat = rng.Pick( Harmony.BassPatternsFor( _genre ) );
		_drumStyle = _genre switch                        // 0 ska rolls a style; the rest are fixed
		{
			1 => 2,                                       // rock: straight backbeat
			2 => 2,                                       // country: train-beat backbeat
			3 => 3,                                       // metal: double-kick
			4 => 2,                                       // punk: straight backbeat (fast)
			5 => 4,                                       // pop: four-on-the-floor
			_ => _fast ? 2 : rng.Int( 2 ),
		};
		_organBubble = true;
		_hasHorns = true;
		// Meter is fixed up front — the time base is built further down (it needs the song's
		// length), so anything sized in eighths before then counts them from the meter.
		int eighthsPerBar = beatsPerBar * Timing.TicksPerBeat / Timing.TicksPerEighth;
		_hornMask = new bool[eighthsPerBar];
		_hornMask[0] = true;
		for ( int e = 1; e < eighthsPerBar; e++ )
			_hornMask[e] = rng.Chance( _c.HornDensity * (e % 2 == 1 ? 1.3f : 0.5f) );
		// How much this song leans on the ride cymbal vs the closed hats for the main pulse.
		// Every song can do both — each SECTION rolls its own choice against this preference
		// (see RenderSection), so a song can hat the verse and ride the chorus. The lean is
		// genre-biased (rock/metal ride more) and spread per song so some strongly prefer one.
		// One rng.Next() (same draw count as the old single Chance), so later draws stay aligned.
		float rideBase = _genre switch { 1 => 0.55f, 3 => 0.65f, 2 => 0.30f, 4 => 0.20f, 5 => 0.30f, _ => 0.40f };
		_ridePref = Math.Clamp( rideBase - 0.25f + 0.5f * rng.Next(), 0f, 1f );
		// Which side the two crashes sit on (±25%); flips per song so the stereo image varies.
		_crashBrightLeft = rng.Chance( 0.5f );
		// This song's backbeat kick personality — which off-beat eighths the kick leans into
		// beyond the fixed beat-1 & 3 anchors. Only the straight backbeat (rock/country/fast
		// ska) reads it; drawn last so it shifts no earlier choice.
		_kickAccents = rng.Pick( BackbeatKickAccents );

		float swing = _fast ? _c.FastSwing : _c.Swing;
		if ( _genre == 5 ) swing *= 0.25f;                // pop sits on a tight, straight dance grid
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
		int drumPush = (int)Math.Round( (0.5f - Math.Clamp( _c.DrumDrive, 0f, 1f )) * 2f * 0.13f * spe );

		// Lay out the structure first — the time base is built over the song's full tick span,
		// so it has to know how long the song is.
		var structure = BuildStructure();
		int totalBars = 0;
		foreach ( var p in structure ) totalBars += p.Bars;

		// The time base the whole band shares from here on. Ticks are the grid; the swing is a
		// warp applied on the way out to samples, not a quantisation.
		int totalTicks = totalBars * beatsPerBar * Timing.TicksPerBeat;
		_time = new Timing( beatsPerBar, totalTicks, spe / (double)Timing.TicksPerEighth,
			swing, drumPush, _sr );

		// Size to the structure plus a ring-out tail, so the ending's final chord and the
		// reverb decay into real silence past the last bar rather than being cut off.
		int structured = _time.SamplesForTicks( totalTicks );
		int total = structured + (int)(_sr * RingOutTail);
		_bufL = new float[total];
		_bufR = new float[total];

		int barCursor = 0;
		for ( int si = 0; si < structure.Count; si++ )
		{
			var part = structure[si];
			RenderSection( part, si, barCursor * _time.BarTicks );
			barCursor += part.Bars;
		}
	}

	// Render one section. Each voice gets its own per-section RNG stream keyed so that repeats
	// of a section type reproduce identical backing, while the lead key folds in the verse
	// index (so the Nth verse's lead differs) and the fill key folds in the absolute section
	// index (so every section closes with a unique fill).
	void RenderSection( Part part, int absIndex, int sectionTick )
	{
		string bk = SectionKey( part.Type );
		string lk = part.Type == Section.Verse ? $"verse:{part.VerseIndex}" : bk;
		var bassRng = new Rng( $"{_tag}:bass:{bk}" );
		var bassOrn = new Rng( $"{_tag}:bassorn:{bk}" );
		var rhythmRng = new Rng( $"{_tag}:rhythm:{bk}" );
		var keysRng = new Rng( $"{_tag}:keys:{bk}" );
		var hornRng = new Rng( $"{_tag}:horn:{bk}" );
		var leadRng = new Rng( $"{_tag}:lead:{lk}" );
		// Expression (vibrato/bend/glide/scoop) rolls off their own stream so adding them
		// leaves every voice's existing note CHOICES untouched — only pitch-shaping is layered on.
		var exprRng = new Rng( $"{_tag}:expr:{lk}" );
		var noise = new Rng( $"{_tag}:drums:{bk}" );
		// Hats vs ride is decided per section off its own stream (keyed by section TYPE, so every
		// chorus rides-or-hats the same, but a verse can differ). Rolled against the song's
		// _ridePref — independent stream, so it disturbs no other voice's draws.
		_ride = new Rng( $"{_tag}:ride:{bk}" ).Chance( _ridePref );
		var fillRng = new Rng( $"{_tag}:fill:{absIndex}" );
		var fillNoise = new Rng( $"{_tag}:fillnoise:{absIndex}" );

		bool isIntro = part.Type == Section.Intro;
		bool isEnding = part.Type == Section.Ending;

		for ( int bar = 0; bar < part.Bars; bar++ )
		{
			int chord = (bar / 2) % _prog.Length;
			int nextChord = ((bar / 2) + 1) % _prog.Length;
			int barTick = sectionTick + bar * _time.BarTicks;

			// The ending lands on a held tonic chord that rings out — the band stops on the
			// "one", it doesn't roll forward as if looping. The bar before it fills to lead in.
			if ( isEnding && bar == part.Bars - 1 )
			{
				RenderEnding( barTick, noise );
				continue;
			}
			// Every section's last bar fills; for the ending that fill moves one bar earlier so
			// it sets up the final hit instead of pushing past the end into nothing.
			bool lastBar = bar == part.Bars - 1 || (isEnding && bar == part.Bars - 2);

			// Intro build-in: rather than slamming in at full band (which reads as looping back
			// into the middle of the song), the voices enter a layer at a time — bass + drums
			// lay down the groove first, then the chordal voice, then the horns/lead on top. The
			// thresholds are derived from the intro length (not hardcoded bar numbers) so the build
			// always spans the whole intro and can't silently collapse if part.Bars changes: the
			// chord enters a quarter of the way in, the top half-way. (4-bar intro ⇒ bars 1 and 2.)
			bool playChord = !isIntro || bar >= part.Bars / 4;
			bool playTop = !isIntro || bar >= part.Bars / 2;

			RenderBassBar( barTick, chord, nextChord, bassRng, bassOrn, exprRng );
			if ( playChord )
				switch ( _genre )
				{
					case 1: // rock: keys comp + power-chord guitar
					case 2: // country: honky-tonk piano comp + strummed twang guitar
						RenderKeysBar( barTick, chord, keysRng, exprRng );
						RenderRhythmGuitarBar( barTick, chord, rhythmRng, exprRng );
						break;
					case 3: // metal: palm-muted gallop riff carries the bar
						RenderMetalRiffBar( barTick, chord, rhythmRng, exprRng );
						break;
					case 4: // punk: lean — power-chord guitar carries it, no keys
						RenderRhythmGuitarBar( barTick, chord, rhythmRng, exprRng );
						break;
					case 5: // pop: synth comp (the keys voice, run clean + bright)
						RenderKeysBar( barTick, chord, keysRng, exprRng );
						break;
					default: // ska: skank chop + horn stabs
						RenderRhythmBar( barTick, chord, rhythmRng, exprRng );
						if ( _hasHorns && playTop )
							RenderHornStabs( barTick, chord, hornRng, exprRng );
						break;
				}
			RenderDrumBar( barTick, lastBar, noise, fillRng, fillNoise );

			// No lead in the ending: a lead phrase starts every two bars and runs ~two bars, so in
			// the short outro it spilled melody notes across the held final chord — the band has
			// already resolved and stopped, so the lead must too (it read as "random notes after
			// the hold"). The ending is just the fill bar → the ringing tonic.
			if ( playTop && bar % 2 == 0 && !isEnding )
				RenderLeadPhrase( barTick, chord, leadRng, exprRng );
		}
	}

	// The song's final downbeat. The whole band resolves home to the tonic on the "one" and
	// lets the chord ring out into the tail RingOutTail reserved — a landing, not a turnaround.
	// The previous bar's fill (see RenderSection) leads in and its terminal crash lands right
	// here, so the ending itself only adds the kick + the sustained, decaying chord.
	void RenderEnding( int barTick, Rng noise )
	{
		int at = _time.TickToSample( barTick );
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
}
