using System;
using System.Collections.Generic;

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
		_tag = string.IsNullOrEmpty( tag ) ? "rotaliate" : tag;
		_genre = Math.Clamp( _c.Genre, 0, 5 );
		var rng = new Rng( Xmur3( _tag.ToLowerInvariant() ) );

		// TEMPO BIAS — punk always runs hot (it's the fast genre); the roll still consumes one
		// draw so every other genre's later picks stay byte-identical.
		_fast = rng.Chance( _c.FastChance ) || _genre == 4;
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
			4 => 2,                                       // punk: straight backbeat (fast)
			5 => 4,                                       // pop: four-on-the-floor
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
		float rideBase = _genre switch { 1 => 0.55f, 3 => 0.65f, 2 => 0.30f, 4 => 0.20f, 5 => 0.30f, _ => 0.40f };
		_ridePref = Math.Clamp( rideBase - 0.25f + 0.5f * rng.Next(), 0f, 1f );
		// Which side the two crashes sit on (±25%); flips per song so the stereo image varies.
		_crashBrightLeft = rng.Chance( 0.5f );
		// This song's backbeat kick personality — which off-beat eighths the kick leans into
		// beyond the fixed beat-1 & 3 anchors. Only the straight backbeat (rock/country/fast
		// ska) reads it; drawn last so it shifts no earlier choice.
		_kickAccents = rng.Pick( BackbeatKickAccents );

		_swing = _fast ? _c.FastSwing : _c.Swing;
		if ( _genre == 5 ) _swing *= 0.25f;               // pop sits on a tight, straight dance grid
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
			// lay down the groove first, then the chordal voice, then the horns/lead on top. The
			// thresholds are derived from the intro length (not hardcoded bar numbers) so the build
			// always spans the whole intro and can't silently collapse if part.Bars changes: the
			// chord enters a quarter of the way in, the top half-way. (4-bar intro ⇒ bars 1 and 2.)
			bool playChord = !isIntro || bar >= part.Bars / 4;
			bool playTop = !isIntro || bar >= part.Bars / 2;

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
					case 4: // punk: lean — power-chord guitar carries it, no keys
						RenderRhythmGuitarBar( barStart, spe, secPerEighth, chord, rhythmRng, exprRng );
						break;
					case 5: // pop: synth comp (the keys voice, run clean + bright)
						RenderKeysBar( barStart, spe, secPerEighth, chord, keysRng, exprRng );
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
}
