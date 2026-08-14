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
	// A copy of an int[], spelled out. NOT `(int[])a.Clone()`: s&box compiles this same source
	// against an API whitelist that DENIES Array.Clone specifically, so it is a compile error
	// there (SB1000) while compiling fine here and in the wasm build — a build we cannot run, so
	// the fix has to be a habit rather than a test.
	//
	// It is a deliberate carve-out rather than an omission [SOURCE, read 2026-08-03]:
	// Sandbox.Access/Rules/Types.cs allows "System.Array*" and then denies
	// "!System.Private.CoreLib/System.Array.Clone*" on the next line — one of only seven deny
	// entries in the whole ruleset, sitting directly under the same treatment of
	// Object.MemberwiseClone. So the rule is about that ONE MEMBER, not about System.Array:
	// Array.Sort, Array.Empty and Array.Copy are all allowed and all used in this engine.
	static int[] CopyOf( int[] src )
	{
		var d = new int[src.Length];
		for ( int i = 0; i < src.Length; i++ ) d[i] = src[i];
		return d;
	}

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
	// possibly in parallel).
	//
	// EVERY GENRE PULLS THE SAME NUMBER OF VALUES out of this stream. A weighted draw is one
	// Next() however the table is weighted, a genre with no second chordal voice still takes its
	// figure draw, and the ska-only rolls (lead instrument, organ bubble, horn section) happen
	// for everyone. A knob decides WHAT plays, never how many values the composer pulls.
	void ComposePlan( string tag )
	{
		_events.Clear();
		_chorusArranged = false;
		int beatsPerBar = 4;   // 4/4 today; Timing carries it so voices never assume
		_tag = string.IsNullOrEmpty( tag ) ? "rotaliate" : tag;
		_genre = Math.Clamp( _c.Genre, 0, GenreProfile.Count - 1 );
		var prof = GenreProfile.For( _genre );
		_prof = prof;
		_chordBars = Math.Max( 1, prof.ChordBars );
		_hornLead = prof.HornLead;
		var rng = new Rng( _tag.ToLowerInvariant() );

		// Whether this song takes the genre's uptempo band. The genre's own odds now (see
		// GenreProfile.FastChance) rather than a knob's — one draw either way, so the draw count
		// still cannot depend on the genre.
		_fast = rng.Chance( prof.FastChance );
		int bpm = _bpm = prof.DrawBpm( rng, _fast );
		_scale = rng.PickWeighted( prof.Scales, prof.ScaleWeights );
		// THE CHANGES ARE DRAWN AGAINST THE MODE, not just alongside it. A progression row is a
		// roman numeral written as a degree, and a degree means a different chord in each of a
		// genre's scales — including, on one degree per mode, a chord that cannot be rooted at all
		// (see Harmony.PlayableProgressions). Substituting here rather than at the point of use is
		// what lets the bass, the voice-leading plan and the tune's chord-tone snap all read the
		// same changes; one draw either way, so the draw count still cannot depend on the scale.
		_prog = rng.Pick( Harmony.PlayableProgressions( _scale, prof.Progressions ) );
		// The song's chord vocabulary — a triad, a 7th, a sus, a bare power chord. Every chordal
		// voice reads this one voicing, so the guitar and the keys agree about what the chord IS
		// while still playing different rhythms.
		_voicing = rng.PickWeighted( prof.Voicings, prof.VoicingWeights );
		// A sus is the one voicing that is not a chord quality but a delayed third, so the song
		// also carries the spelling it resolves to (see Harmony.SuspendedVoice). Not a draw and
		// not a second voicing: it is the same chord with its suspension landing.
		_susVoice = Harmony.SuspendedVoice( _voicing );
		_voicingRes = _voicing;
		if ( _susVoice >= 0 )
		{
			_voicingRes = CopyOf( _voicing );
			_voicingRes[_susVoice] = Harmony.Third;
		}
		// How each chord inverts to stay near the one before it. A property of the changes and
		// the voicing, so it is decided once and every chordal voice reads the same table — the
		// guitar and the keys must agree on the inversion as much as on the chord. Costs no draw.
		var plan = Harmony.PlanVoiceLeading( _scale, _prog, _voicing );
		_vlShift = plan.Shift; _vlRot = plan.Rot;
		_endingPrev = null;
		_rootMidi = 28 + rng.Int( 8 );                    // E1..B1 bass root
		// Which horn/organ voice takes the ska lead, weighted by the config. Rolled for every
		// genre so the draw count doesn't depend on the genre; only ska reads the result (the
		// rest route the lead to a guitar in RenderLeadNote).
		_lead = PickInstrument( rng );
		_leadPan = (rng.Next() * 2f - 1f) * _c.PanAmount;
		// NOTE the ceiling: a genre's Mix.Width above 1 cannot be reached at the design width, so
		// pop's 1.2 lands at 1.0 like everyone else. The RELATIVE widths still hold (country 0.85
		// sits inside pop's), which is what the profile is for.
		_widthScale = Math.Clamp( _c.PanAmount * prof.Mix.Width * _c.GenreMix, 0f, 1f );
		_drumPan = DrumPan * _widthScale;
		_bassPat = _songBass = rng.Pick( prof.BassPatterns );
		_compFig = _songComp = rng.Pick( prof.CompFigures );
		// What the chordal voice plays where the section is loud enough to change technique. Drawn
		// for every genre so the draw count does not depend on whether the genre has a loud comp;
		// only the genres with one read the result (PickOrNull takes its value either way).
		_songLoud = PickOrNull( rng, prof.LoudCompFigures );
		// The second chordal voice's figure. Drawn even where the genre has none, so the genres
		// that do have one are not the only ones consuming the value.
		_keysFig = _songKeys = PickOrNull( rng, prof.KeysFigures );
		_groove = _songGroove = prof.DrawGroove( rng );
		_songKick = _songGroove.Kick; _songSnare = _songGroove.Snare;
		// WHO GOES FIRST. Drawn once per song and for every genre, so the draw count does not depend
		// on the answer: the band writing to the kit and the kit writing to the band are two
		// different mechanisms for the same cohesion, and a genre has an opinion about which one it
		// is (see GenreProfile.KitLeadsChance).
		_kitLeads = rng.Chance( prof.KitLeadsChance );
		// Metal's bass either pedals under the riff or doubles it; punk sometimes takes the same
		// unison. Both are RELATIONAL, so this decides whether the bass reads the guitar's onsets
		// at render time rather than which table it plays from.
		_riffBass = rng.Chance( prof.RiffBassChance );
		// Whether this song has an organ bubbling under the skank, and whether the horn section
		// backs the lead. Both are ska arrangement choices the ORGAN BUBBLE / HORN SECTION knobs
		// set the odds of; rolled for every genre for the same draw-count reason as the lead.
		_organBubble = rng.Chance( _c.OrganBubbleChance );
		_hasHorns = rng.Chance( _c.HornSectionChance );
		_hornFig = HornFigure( rng, beatsPerBar );
		// How much this song leans on the ride cymbal vs the closed hats for the main pulse.
		// Every song can do both — each SECTION rolls its own choice against this preference.
		_ridePref = prof.DrawRidePref( rng );
		// Which side the two crashes sit on (±25%); flips per song so the stereo image varies.
		// How much room this song sits in — per-song character drawn from a band, the way tempo and
		// swing are, and then trimmed by the genre's own profile in Master(). A fixed value made
		// every song of a genre sit in the identical space.
		_reverbWet = ReverbMin + rng.Next() * (ReverbMax - ReverbMin);
		_crashBrightLeft = rng.Chance( 0.5f );
		// THE KIT IS SET UP ONCE, the way a drummer sets one up: three toms tuned in the genre's
		// intervals from the song's own key, and the rack on one side or the other. Drawn from
		// _rootMidi and never from a section's _keyShift — nothing re-reads it, so the toms cannot
		// drift with a mid-song key change, and only the pitch CLASS is used so the set stays a
		// drum-sized set whatever octave the song is written in.
		_tomKit = TomKit.Tuned( prof.Toms, _rootMidi, rackLeft: rng.Chance( 0.5f ) );
		// And the nuance the audition approved as BANDS rather than values (see KitNuance): the
		// same drum does not make the identical sound twice, and which point of each band this
		// kit sits at is as much a property of a song as its tempo is.
		_kickTone = KickTone.Default.With(
			clickCut: KitNuance.At( KitNuance.ClickCutMin, KitNuance.ClickCutMax, rng.Next() ),
			jitter: 0.35f );
		// The cymbals' own bands. Both audition rounds came back "all of these work", which is a
		// finding about nuance rather than an undecided question — so the song draws a point out of
		// each band instead of the engine picking one by ear and freezing it into every song.
		var cy = CymbalDraw.Draw( rng );
		_rideBow = BuildCymbal( CymbalBands.Bow( cy.RideSplash, cy.RideWash, cy.RideRing ), 0 );
		_rideBell = BuildCymbal( CymbalBands.Bell( ring: cy.BellRing, clang: cy.BellClang ), 1 );
		_crashBright = BuildCymbal( CymbalBands.CrashBright( cy.BrightSplash, cy.BrightRing ), 2 );
		_crashDark = BuildCymbal( CymbalBands.CrashDark( cy.DarkSplash, cy.DarkRing, cy.DarkWash ), 3 );
		float hatU = rng.Next(), footU = rng.Next();
		_hatTone = HatTone.Default.With(
			openDur: KitNuance.At( KitNuance.OpenHatDurMin, KitNuance.OpenHatDurMax, hatU ),
			openCut: KitNuance.OpenHatCut, decayFrac: 0.45 + 0.05 * hatU,
			openCurve: KitNuance.HatOpenCurve );
		_footTone = HatTone.Foot.With(
			attackSec: KitNuance.At( KitNuance.FootAttackMin, KitNuance.FootAttackMax, footU ),
			closedDur: KitNuance.At( KitNuance.FootDurMin, KitNuance.FootDurMax, footU ),
			closedCut: KitNuance.At( KitNuance.FootCutMin, KitNuance.FootCutMax, 1f - footU ) );
		// How the song lands. Every song used to end on the same fixed pad, whatever the genre.
		_ending = rng.PickWeighted( prof.Endings, prof.EndingWeights );

		// Swing is the genre's own feel, drawn per song from its band exactly the way tempo is —
		// not a knob, so a reroll can never hand metal a shuffle. Ska-punk and country may instead
		// draw a genuine 2:1 triplet shuffle, which is a different feel rather than more swing.
		//
		// DRAWN BEFORE THE TUNE, because the tune has to know. Under a shuffle the beat's own
		// subdivision is the triplet, and a melody written in straight sixteenths against that is
		// not syncopation, it is two grids at once — see Melody.Draw.
		float swing = prof.DrawSwing( rng, _fast );

		// The song's TUNE — the thing a listener hums back. Drawn off its own streams, so a song
		// having a melody shifts nothing else in the composition.
		DrawTunes( beatsPerBar * Timing.TicksPerBeat, swing > 0f );
		double secPerEighth = 60.0 / bpm / 2.0;
		int spe = (int)Math.Round( _sr * secPerEighth );

		// Drum tone (toms↔cymbals) → per-voice gain split, and drive (pull↔push) → a constant
		// kit timing bias (− = ahead/push, + = behind/lay back; 0.5 = dead on).
		float dt = Math.Clamp( _c.DrumTone, 0f, 1f );
		_drumTone = dt;
		// Gentle gain lean (neutral at 0.5 so the balanced kit is untouched there), then the
		// genre's own mix trim on top: metal dry and mid-scooped, pop bright, country centred.
		_drumLowMul = (1.2f - 0.4f * dt) * MixTrim( prof.Mix.Low );
		_drumHighMul = (0.7f + 0.6f * dt) * MixTrim( prof.Mix.High );
		_midMul = MixTrim( prof.Mix.Mid );
		int drumPush = (int)Math.Round( (0.5f - Math.Clamp( _c.DrumDrive, 0f, 1f )) * 2f * 0.13f * spe );

		// Lay out the structure first — the time base is built over the song's full tick span,
		// so it has to know how long the song is, and the sections carry the tempo curve.
		// THE FORM IS DRAWN ONCE AND CACHED. Off its own stream, so a form that varies costs the
		// song stream nothing and its draw-count rule holds unchanged; and cached because four
		// diagnostics read it back to build bar rulers, and a ruler derived from a second draw is a
		// ruler for a different song.
		var structure = _form = DrawForm( _prof, new Rng( $"{_tag}:form" ) );
		_sectionStart = new int[structure.Count];
		int totalTicks = 0;
		for ( int si = 0; si < structure.Count; si++ )
		{
			_sectionStart[si] = totalTicks;
			totalTicks += SectionTicks( structure[si], beatsPerBar );
		}

		// The time base the whole band shares from here on. Ticks are the grid; the swing is a
		// warp applied on the way out to samples, not a quantisation; and the per-tick delta
		// carries the tempo CURVE — each section's own tempo, plus the ritard over the final
		// bars (a song that just stops reads as a loop point, not an ending).
		double baseDelta = spe / (double)Timing.TicksPerEighth;
		int ritardTicks = Math.Min( totalTicks / 2, 2 * beatsPerBar * Timing.TicksPerBeat );
		int ritardFrom = Math.Max( 0, totalTicks - ritardTicks );
		var delta = new double[totalTicks + 2];
		for ( int t = 0; t < delta.Length; t++ )
		{
			float mul = structure[SectionAt( t, structure.Count )].TempoMul;
			double d = baseDelta / Math.Max( 0.5f, mul );
			if ( t > ritardFrom && ritardTicks > 0 )
				d *= 1.0 + RitardAmount * (t - ritardFrom) / (double)ritardTicks;
			delta[t] = d;
		}
		_time = new Timing( beatsPerBar, totalTicks, t => delta[Math.Min( t, delta.Length - 1 )],
			swing, drumPush, _sr );

		// Size to the structure plus a ring-out tail. The tail grows with the ritard — it is a
		// fixed number of SECONDS at the song's nominal tempo, and by the last bar the song is
		// running slower than that, so a constant tail would be outrun by its own ending.
		int total = _time.TotalSamples + (int)(_sr * RingOutTail * (1f + RitardAmount));
		_bufL = new float[total];
		_bufR = new float[total];

		for ( int si = 0; si < structure.Count; si++ )
			RenderSection( structure[si], si, _sectionStart[si], beatsPerBar,
				si + 1 < structure.Count ? structure[si + 1] : structure[si] );
	}

	/// <summary>How much slower the song's final bars run than its nominal tempo.</summary>
	const double RitardAmount = 0.22;

	/// <summary>Which chord of the progression bar <paramref name="bar"/> of a section sits on.
	/// One definition, because the tune has to be able to ask the same question — a melody drawn
	/// against the changes only stays consonant if it is sung over the changes it was drawn for.
	/// </summary>
	internal int ChordIndexAt( in Part part, int bar )
		=> (bar / _chordBars) % _prog.Length;

	/// <summary>Length of a section in ticks, honouring any anomalous (short) bars.</summary>
	internal static int SectionTicks( in Part p, int beatsPerBar )
	{
		int ticks = 0;
		for ( int bar = 0; bar < p.Bars; bar++ )
			ticks += BarBeats( p, bar, beatsPerBar ) * Timing.TicksPerBeat;
		return ticks;
	}

	/// <summary>Beats in one bar of a section. Normally the song's meter; a section may name a
	/// short bar (Biamonte's "anomalous measure" — a 2/4 inside a 4/4 context), which is how a
	/// transition can cut a beat rather than politely filling the bar.</summary>
	static int BarBeats( in Part p, int bar, int beatsPerBar )
		=> p.BarBeats != null && bar < p.BarBeats.Length ? Math.Max( 1, p.BarBeats[bar] ) : beatsPerBar;

	/// <summary>Which section a tick falls in.</summary>
	int SectionAt( int tick, int count )
	{
		for ( int i = count - 1; i >= 0; i-- )
			if ( tick >= _sectionStart[i] ) return i;
		return 0;
	}

	/// <summary>Draw a figure from a table that may not exist for this genre. The draw is taken
	/// either way — see the draw-count rule on <see cref="ComposePlan"/>.</summary>
	static Pattern PickOrNull( Rng rng, Pattern[] table )
	{
		int i = rng.Int( Math.Max( 1, table?.Length ?? 1 ) );
		return table == null || table.Length == 0 ? null : table[Math.Min( i, table.Length - 1 )];
	}

	/// <summary>The horn section's figure: a TWO-BAR call and response. The mask used to be eight
	/// slots reused for the whole song, so the section played the identical stab pattern in every
	/// bar of every chorus; bar 2 answering bar 1 is the actual ska convention, and it is what a
	/// pattern with its own length buys.</summary>
	Pattern HornFigure( Rng rng, int beatsPerBar )
	{
		int per = beatsPerBar * Timing.TicksPerBeat / Timing.TicksPerEighth;
		var call = new int[per];
		call[0] = CompFigure.Stab;
		for ( int e = 1; e < per; e++ )
			call[e] = rng.Chance( _c.HornDensity * (e % 2 == 1 ? 1.3f : 0.5f) )
				? CompFigure.Stab : Harmony.Rest;

		// The answer is the call displaced by a beat and opened up: it lands where the call left
		// space, and pushes into the next bar on the last eighth.
		var cells = new int[per * 2];
		for ( int e = 0; e < per; e++ )
		{
			cells[e] = call[e];
			cells[per + e] = call[(e + 2) % per];
		}
		cells[per] = Harmony.Rest;                 // the answer holds off the downbeat…
		cells[per * 2 - 1] = CompFigure.Stab;      // …and pushes into the next call
		return Pattern.Eighths( cells );
	}

	// Render one section. Each voice gets its own per-section RNG stream keyed so that repeats
	// of a section type reproduce identical backing, while the lead key folds in the verse
	// index (so the Nth verse's lead differs) and the fill key folds in the absolute section
	// index (so every section closes with a unique fill).
	void RenderSection( Part part, int absIndex, int sectionTick, int beatsPerBar, Part next )
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
		// chorus rides-or-hats the same, but a verse can differ).
		_ride = new Rng( $"{_tag}:ride:{bk}" ).Chance( _ridePref );
		var fillRng = new Rng( $"{_tag}:fill:{absIndex}" );
		var fillNoise = new Rng( $"{_tag}:fillnoise:{absIndex}" );

		// ── the section's figures ──
		// The comp, keys and bass figures were drawn ONCE PER SONG, so a two-bar figure really was
		// everything a listener ever heard — the backing read as one cell repeated for three
		// minutes. The chorus keeps the song's own figure (that is the song's identity, and every
		// chorus must agree); the other sections draw their own off a stream keyed by section TYPE,
		// so a verse contrasts with the chorus while both verses still match each other.
		//
		// THE GROOVE IS ONE OF THEM NOW. It was the last draw in the engine that happened once per
		// song and never again — the kit's two or three table entries were a genre's whole drumming,
		// and one of them was a whole SONG's. Its own stream, so a section acquiring a groove of its
		// own moves nothing else in the kit.
		if ( part.Type != Section.Chorus )
		{
			var figRng = new Rng( $"{_tag}:figure:{bk}" );
			_compFig = figRng.Pick( _prof.CompFigures );
			_keysFig = PickOrNull( figRng, _prof.KeysFigures );
			_bassPat = figRng.Pick( _prof.BassPatterns );
			_groove = _prof.DrawGroove( new Rng( $"{_tag}:groove:{bk}" ) );
		}
		else
		{
			_compFig = _songComp; _keysFig = _songKeys; _bassPat = _songBass;
			_groove = _songGroove;
		}
		_kickFig = _groove.Kick; _snareFig = _groove.Snare;
		var tune = TuneFor( part.Type );

		// ── the section's own state ──
		// Everything below here reads these rather than asking "am I in a verse?": the energy
		// contour, the half/double-time feel, and the key.
		_sectionTick = sectionTick;
		_sectionTicks = SectionTicks( part, beatsPerBar );
		_energy = Math.Clamp( part.Energy, 0f, 1f );
		_feel = part.Feel;
		_keyShift = part.KeyShift;
		_sectionType = part.Type;
		// A CRASH-RIDE IS A TECHNIQUE, not a third cymbal: at the top of a genre's dynamic the
		// cymbal hand moves off the ride and onto a crash, and the whole song lifts because the
		// pulse is now being played on the loudest thing in the kit. It rides on top of the ride
		// roll, so a section that was not riding at all does not suddenly acquire a cymbal, and
		// it is a threshold on ENERGY rather than on section type, exactly like LoudComp.
		_crashRide = _ride && _energy >= _prof.CrashRideFrom;
		// And the foot's part under it (see FootOccupancy). Its own stream, so a section acquiring
		// a pedal figure moves nothing else in the kit, and drawn per section because that is the
		// scale a drummer holds a figure over.
		_sections.Add( new SectionInfo( _time.TickToSample( sectionTick ),
			_time.TickToSample( sectionTick + _sectionTicks ), _ride, _crashRide, $"{part.Type}",
			_groove.Name ) );
		var footRng = new Rng( $"{_tag}:foot:{bk}" );
		_footCells = 0;
		for ( int i = 0; i < 8; i++ )
			if ( footRng.Chance( FootOccupancy[i] ) ) _footCells |= 1 << i;

		// ── the arrangement ──
		// One authority writes every part of this section against one skeleton, in one pass, off
		// its own stream (so the song stream's draw count is untouched and its rule holds
		// unchanged). Everything it needs exists by now: the section's state is published, the
		// figures are drawn, the groove is a set of Patterns and the tune is a Pattern.
		//
		// It replaces _bassPat / _compFig / _keysFig with ARRANGED versions of themselves, which is
		// why nothing downstream changed: a voice still slices the figure it was handed. Rewriting
		// each voice to read the skeleton directly would have put the arranger's rules in six
		// places and left every one of them able to disagree about what the section is — the same
		// argument PlanTrace makes about re-deriving the plan.
		PlanArrangement( part, sectionTick, beatsPerBar * Timing.TicksPerBeat, tune, bk );
		// Recorded AFTER the arrangement, so what a sweep reads is the part the section will play
		// rather than the table entry it started from.
		Trace?.Mark( sectionTick, _sectionTicks, beatsPerBar * Timing.TicksPerBeat, part.Type,
			_groove.Name, _kickFig, _snareFig, _groove.Cymbal );


		bool isIntro = part.Type == Section.Intro;
		bool isEnding = part.Type == Section.Ending;
		// A fill that runs a whole bar or two has to be going somewhere: into a chorus, or out of
		// a breakdown. Everywhere else it stays a beat or two.
		bool bigBoundary = next.Type == Section.Chorus || part.Type == Section.Breakdown
			|| part.Type == Section.PreChorus;

		// ── the section's bar layout and its closing fill ──
		// Bars are laid out first because a fill may be longer than the bar it lands in: it is
		// planned once, in ticks, and the KIT stops where it begins. The melodic voices play
		// through it, the way a band does — only the drums hand over.
		var barStart = new int[part.Bars];
		var barLen = new int[part.Bars];
		int cursor = sectionTick;
		for ( int bar = 0; bar < part.Bars; bar++ )
		{
			barLen[bar] = BarBeats( part, bar, beatsPerBar ) * Timing.TicksPerBeat;
			barStart[bar] = cursor;
			cursor += barLen[bar];
		}

		// The ending's fill moves one bar earlier so it sets up the final hit instead of pushing
		// past the end into nothing.
		int fillBar = isEnding ? part.Bars - 2 : part.Bars - 1;
		int fillFrom = int.MaxValue, fillTo = 0;
		if ( fillBar >= 0 && fillRng.Chance( _c.FillChance ) )
		{
			fillTo = barStart[fillBar] + barLen[fillBar];
			fillFrom = Math.Max( sectionTick, FillStart( barStart[fillBar], barLen[fillBar], bigBoundary, fillRng ) );
		}

		for ( int bar = 0; bar < part.Bars; bar++ )
		{
			int barTick = barStart[bar];
			int barTicks = barLen[bar];
			_barTick = barTick;

			// Harmonic rhythm is the genre's (GenreProfile.ChordBars): 2 bars/chord is the
			// reggae-rock norm, while punk and pop take 1 so the four-chord loop IS the four-bar
			// hypermeasure.
			//
			// nextChord is the NEXT BAR's chord, not the next slot's: with 2 bars/chord the first
			// of the pair does not change harmony, and a bass approach note walking into a chord
			// that is still a bar away just lands wrong.
			int chord = ChordIndexAt( part, bar );
			int nextChord = ChordIndexAt( part, bar + 1 );

			// Where a suspension lands: half way through THIS CHORD's span, whatever the genre's
			// harmonic rhythm is. At 2 bars/chord that is the second bar; at pop's 1 it is the
			// second half of the bar, which is why this is a tick and not a bar index — a sus that
			// only ever resolved on a bar line could never resolve at all where the chord IS a bar.
			int chordBar0 = bar / _chordBars * _chordBars;
			int chordTicks = 0;
			for ( int b = chordBar0; b < part.Bars && b < chordBar0 + _chordBars; b++ )
				chordTicks += barLen[b];
			_susResolveTick = barStart[chordBar0] + chordTicks / 2;

			// The ending lands on a held tonic chord that rings out — the band stops on the
			// "one", it doesn't roll forward as if looping. The bar before it fills to lead in.
			if ( isEnding && bar == part.Bars - 1 )
			{
				// The final chord is led out of the chord that was actually sounding before it
				// (see EndingChord), so the ending has to be told what that was. Read at the last
				// tick of the previous bar, which is past any suspension's landing — the same
				// resolved spelling the ending itself uses.
				_endingPrev = bar > 0
					? ChordMidis( EndingBase(), ChordIndexAt( part, bar - 1 ), barTick - 1 )
					: null;
				RenderEnding( barTick, barTicks, noise );
				break;
			}

			// The cadential regrouping: over a hemiola section's last two bars the chordal voice
			// swaps its figure for one whose length does not divide the bar, so the comp and the
			// bar line pull apart on the way into the next section.
			bool hemiola = part.Hemiola && bar >= part.Bars - 2;

			// Intro build-in: rather than slamming in at full band, the voices enter a layer at a
			// time — bass + drums first, then the chordal voice, then the horns/lead on top. The
			// thresholds are derived from the intro length so the build always spans it.
			bool playChord = !isIntro || bar >= part.Bars / 4;
			bool playTop = (!isIntro || bar >= part.Bars / 2) && _energy > 0.32f;

			// RENDER ORDER. Where the bass follows the riff it has to know what the riff played,
			// so the chordal voice goes first and the bass reads its onsets. Everywhere else the
			// bass leads, as it always has.
			_riffOnsets.Clear();
			if ( _riffBass && playChord )
			{
				RenderComp( barTick, barTicks, chord, rhythmRng, keysRng, exprRng, hemiola );
				RenderBassBar( barTick, barTicks, chord, nextChord, bassRng, bassOrn, exprRng );
			}
			else
			{
				RenderBassBar( barTick, barTicks, chord, nextChord, bassRng, bassOrn, exprRng );
				if ( playChord )
					RenderComp( barTick, barTicks, chord, rhythmRng, keysRng, exprRng, hemiola );
			}

			if ( _hornLead && _hasHorns && playTop )
				RenderHornStabs( barTick, barTicks, chord, hornRng, exprRng );

			RenderDrumBar( barTick, barTicks, fillFrom, noise );

			// The melody. Where the section has a tune it SINGS it — the same one every time that
			// section comes round, which is what makes a chorus a chorus. Sections without one (a
			// solo, and metal's verses) are where the genre's lead grammar improvises instead.
			if ( playTop && !isEnding )
			{
				if ( tune != null ) RenderTune( tune, barTick, barTicks, chord, leadRng, exprRng );
				else if ( bar % Math.Max( 1, _prof.LeadPhraseBars ) == 0 )
					RenderLeadPhrase( barTick, barTicks, chord, leadRng, exprRng );
			}
		}

		// The fill, once, wherever it started — a beat, or the two bars before a final chorus.
		if ( fillTo > 0 ) RenderFill( fillFrom, fillTo, fillNoise, fillRng );
	}

	/// <summary>The chordal layer: the genre's main comp figure, plus its keys/piano/synth voice
	/// where it has one. Which of them plays what is <see cref="CompStyle"/>'s business.</summary>
	void RenderComp( int barTick, int barTicks, int chord, Rng rhythmRng, Rng keysRng, Rng exprRng,
		bool hemiola )
	{
		int to = barTick + barTicks;
		// A loud section changes the TECHNIQUE, not just the level: the genre's loud figure through
		// its loud style (third-wave ska's clean skank becoming driven power chords). The hemiola
		// still wins where a section regroups — that is a metric device and it outranks the timbre.
		bool loud = _songLoud != null && _energy >= _prof.LoudFrom;

		// ── the flourish ──
		// A player throws a flick in SOMETIMES. It used to be one entry in the genre's figure
		// table, and figures are drawn per section — so a song that drew it played the flourish
		// every two bars for a whole chorus, on a schedule, and a song that did not never heard
		// the genre's signature gesture. Neither is an ornament; the first is a loop and the second
		// is a coin toss. So it is rolled per OCCURRENCE, over the two-bar window the flourish
		// figures are written on, which is the way the lead's ornaments have always worked.
		//
		// The roll happens for every genre on every window, ornament or not, so carrying one costs
		// no extra values out of the composition stream (the PickOrNull discipline).
		if ( (barTick - _sectionTick) % (barTicks * 2) == 0 )
		{
			_compOrn = rhythmRng.Chance( OrnamentChance ) && _prof.CompOrnament != null;
			_keysOrn = keysRng.Chance( OrnamentChance ) && _prof.KeysOrnament != null;
		}
		// The hemiola outranks it, the same way it outranks the loud figure: a metric device beats
		// a gesture. So does the loud technique — a third-wave chorus is a different part, not the
		// skank with a flick on it.
		bool plain = !hemiola && !loud;
		var fig = hemiola ? CompFigure.Hemiola
			: loud ? _songLoud
			: _compOrn ? _prof.CompOrnament : _compFig;
		RenderCompVoice( barTick, to, chord, fig, rhythmRng, exprRng, loud );
		if ( _prof.Keys != KeysStyle.None && _keysFig != null && _energy > 0.35f )
			RenderKeysVoice( barTick, to, chord, keysRng, exprRng, plain && _keysOrn );
	}

	// ── the ending ──
	// The song's last bar, played by the band that played the rest of it. This used to be a fixed
	// pad — one oscillator, one envelope, one length, the same in every genre and every song — so
	// however different two songs were, they ended identically. It is the genre's own comp voice,
	// its own bass and its own kit now, and WHICH ending is a per-song draw (see EndingStyle).
	void RenderEnding( int barTick, int barTicks, Rng noise )
	{
		int at = _time.TickToSample( barTick );
		int beat = Timing.TicksPerBeat;
		double tail = RingOutTail * 0.92;

		switch ( _ending )
		{
			case EndingStyle.StopHit:
				// Everything at once, short, then nothing. A punk song does not ring out.
				RenderKick( at, noise );
				RenderCrashCym( at, _c.CrashVol * KitGain( barTick, 1f, 0.5f ), _crashBright, false );
				EndingChord( barTick, beat / 2, 0.35, 1f );
				EmitBass( at, _time.SpanSamples( barTick, beat / 2 ), ChordRoot( 0 ), 0.3,
					NoteGain( 1f ), default );
				break;

			case EndingStyle.Cadence:
				// V, then home on beat 3 — an actual cadence rather than a single held chord.
				EndingChord( barTick, beat, 0.5, 0.8f, rootDegree: 4 );
				EmitBass( at, _time.SpanSamples( barTick, beat ), ChordRoot4(), 0.45,
					NoteGain( 0.85f ), default );
				RenderKick( at, noise );
				int land = barTick + 2 * beat;
				RenderKick( _time.TickToSample( land ), noise );
				RenderCrashCym( _time.TickToSample( land ), _c.CrashVol * KitGain( land, 1f, 0.5f ),
					_crashBright, false );
				EndingChord( land, (int)(_sr * tail), tail, 1f, samples: true );
				EmitBass( _time.TickToSample( land ), (int)(_sr * tail), ChordRoot( 0 ), tail,
					NoteGain( 1f ), default );
				break;

			case EndingStyle.Fall:
				// The figure keeps going and falls away — four hits, each quieter, no final crash.
				for ( int i = 0; i < 4; i++ )
				{
					int t = barTick + i * beat;
					float v = 1f - i * 0.22f;
					EndingChord( t, beat, 0.4, v );
					if ( i % 2 == 0 ) RenderKick( _time.TickToSample( t ), noise );
					EmitBass( _time.TickToSample( t ), _time.SpanSamples( t, beat ), ChordRoot( 0 ),
						0.4, NoteGain( v ), default );
				}
				break;

			default: // Ring — the band hits the tonic together and lets it decay into the tail.
				RenderKick( at, noise );
				EndingChord( barTick, (int)(_sr * tail), tail, 1f, samples: true );
				EmitBass( at, (int)(_sr * tail), ChordRoot( 0 ), tail * 1.1, NoteGain( 1f ), default );
				break;
		}
	}

	/// <summary>The fifth of the key — the chord a cadence leans on before it lands.</summary>
	int ChordRoot4() => Harmony.ScaleMidi( _rootMidi + _keyShift, _scale, 4 );

	/// <summary>The final chord, voiced and TIMBRED as the genre's own chordal voice: a ska song
	/// ends on horns, a metal song on the riff guitar, a pop song on the synth. The song's own
	/// voicing carries too, so a ska ending lands on its 9th and a metal one on a bare fifth.
	/// </summary>
	/// <param name="rootDegree">Scale degree the chord is built on, or −1 for the song's HOME
	/// chord — which is the progression's first slot, so the band lands where the bass lands
	/// (RenderEnding plays ChordRoot(0) under it). The degree used to be folded in as
	/// <c>_prog[0] + d + shift</c> on top of a <c>ChordDegrees</c> that had already added
	/// <c>_prog[0]</c>: harmless while every progression started on the tonic, and a chord built a
	/// third or a sixth off home for the pop and punk loops that do not.</param>
	/// <summary>The register the ending's chord is voiced in — the genre's own chordal voice, so a
	/// ska song lands on its horn section and a metal one on its guitar.</summary>
	int EndingBase() => Register( _prof.Comp switch
	{
		CompStyle.Skank => 2,    // ska: the horn section
		CompStyle.Pad => 2,      // pop: the synth
		_ => 1,                  // the guitar genres
	} );

	void EndingChord( int tick, int dur, double decay, float vel, int rootDegree = -1,
		bool samples = false )
	{
		int at = _time.TickToSample( tick );
		int durSamples = samples ? dur : _time.SpanSamples( tick, dur );
		bool ring = decay > 0.4;
		int root = rootDegree < 0 ? _prog[0] : rootDegree;
		// The register is the genre's own chordal voice; a driven guitar drops its third here too
		// (see GuitarMidis) — a song must not land on the one chord it spent three minutes avoiding.
		int chordBase = EndingBase();
		// A song lands on a chord that STATES its quality, so the final chord is always the resolved
		// spelling — a suspension is a thing owed, and the ending is where it is paid.
		//
		// AND IT IS VOICE-LED, like every other change. Building it in root position was deliberate
		// once — a song should land where its genre voices the chord rather than where the last
		// change left the register — but it made the ending the ONE unled change in the song and put
		// it on the most exposed moment there is, so the last chord could leap a seventh out of the
		// one before it. A cadence is a change; the register the band has held for three minutes is
		// where it lands; and the ritard is not cover for a jump. The genre's own voicing and its
		// own register still decide the chord — this only picks the inversion, inside the same one
		// octave every other change gets (Harmony.LeadToward). _endingPrev is what was sounding
		// before it, which is null for the first chord of the ending, and a null simply leaves root
		// position where there is nothing to lead from.
		var tones = VoicedMidis( chordBase, root, _voicingRes );
		var lead = Harmony.LeadToward( _scale, _endingPrev, chordBase, root, _voicingRes );
		for ( int i = 0; i < tones.Length; i++ ) tones[i] += lead[i];
		_endingPrev = CopyOf( tones );
		if ( _prof.Comp is not (CompStyle.Skank or CompStyle.Pad) )
			tones = DrivenVoicing( tones, _voicingRes );

		foreach ( var midi in tones )
		{
			Patch p;
			switch ( _prof.Comp )
			{
				case CompStyle.Skank:   // ska: the horn section holds the last chord
					p = new Patch
					{
						Osc = 1, Voices = 3, Detune = _c.Detune,
						Amp = _c.HornVol * _c.HornBalance * _midMul / tones.Length * NoteGain( vel ),
						Attack = 0.01f, Decay = decay, Sustain = ring ? 0.35f : 0f, Sustained = false,
						Cutoff = _c.HornCutoff, CutEnv = 1200f, Reso = 1.0f, Drive = _c.HornDrive,
						Pan = 0f, Vibrato = _c.MelodyVibrato,
					};
					break;
				case CompStyle.Pad:     // pop: the synth
					p = new Patch
					{
						Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
						Amp = _c.KeysVol * _c.KeysBalance * KeysLevel() * _midMul / tones.Length
							* NoteGain( vel ),
						Attack = 0.004f, Decay = decay, Sustain = ring ? 0.5f : 0f, Sustained = false,
						Cutoff = _c.KeysCutoff, CutEnv = 250f, Reso = 1.0f, Drive = KeysDriveFor(),
						Pan = 0f,
					};
					break;
				default:                // the guitar genres, through their own tone
					var (drive, cutEnv, reso, level) = RhythmGtrTone();
					p = new Patch
					{
						Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
						Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance * level * _midMul / tones.Length
							* NoteGain( vel ),
						Attack = 0.002f, Decay = decay, Sustain = ring ? 0.4f : 0f, Sustained = false,
						Cutoff = _c.RhythmGtrCutoff, CutEnv = cutEnv, Reso = reso, Drive = drive,
						Pan = 0f,
					};
					break;
			}
			// Pop's ending is the keys voice, and the keys are not double-tracked (see EmitKeys).
			RenderPatch( at, durSamples, Midi( midi ), p, mono: _prof.Comp == CompStyle.Pad );
		}
	}

	// ── dynamics ──
	// Velocity as a first-class value: the pattern cell's own weight times the section's energy.
	// Every voice scales its level through here rather than inventing its own — a per-patch
	// constant with two ad-hoc exceptions was the flat, mechanical tell that survived every
	// rhythmic fix.
	//
	// THERE IS NO TICK HERE, AND THAT IS THE POINT. This used to multiply in MetricGain, so a
	// pitched note's level was decided by WHERE IN THE BAR IT LANDED — and the weights MetricGain
	// returns were measured off drum hits. A melody is drawn on the eighth grid, so it alternates
	// on-beat and off-beat constantly and its level stepped with it: 3 dB a note in rock and 5 dB
	// in pop, out of metric position alone. That is a drummer's dynamic worn by a singer, and it
	// reads as strange as it is. A DATASET OF DRUM VELOCITIES CAN SAY WHAT A DRUMMER DOES AND
	// CANNOT SAY WHAT THE BAND DOES — GenreProfile's accent block already said so as a caveat, and
	// it is the rule now. Taking the parameter away is what makes it one: a pitched voice cannot
	// ask for a metric accent, the same way Register makes a non-octave base unwriteable.
	//
	// A phrase-shaped dynamic for the melody is a different thing and a real one — it would come
	// off the TUNE rather than off the grid, so it belongs in Melody, and it is a PLAN row.
	float NoteGain( float vel ) => vel * EnergyGain( 0.35f );

	/// <summary>The kit's gain: the genre's accent weight for where <paramref name="tick"/> falls
	/// in the bar, times the cell's own velocity and the section's energy. The accent weights were
	/// measured off drums and this is the only door out of MetricGain — see NoteGain.</summary>
	/// <param name="depth">how much this drum voice cares about the section's energy.</param>
	float KitGain( int tick, float vel, float depth ) => vel * MetricGain( tick ) * EnergyGain( depth );

	/// <summary>The genre's accent weight for where <paramref name="tick"/> falls in the bar.</summary>
	float MetricGain( int tick )
	{
		int bar = _time.BarTicks;
		int rel = ((tick - _barTick) % bar + bar) % bar;
		if ( rel == 0 ) return _prof.AccentDown;
		if ( rel % Timing.TicksPerBeat != 0 ) return _prof.AccentOff;
		return (rel / Timing.TicksPerBeat) % 2 == 1 ? _prof.AccentBack : 1f;
	}

	/// <summary>Section energy as a gain. <paramref name="depth"/> is how much this voice cares:
	/// 0 = plays at full level in a breakdown, 1 = disappears entirely.</summary>
	float EnergyGain( float depth ) => 1f - depth * (1f - _energy);

	/// <summary>A genre mix trim, scaled by the runtime GENRE MIX amount so the whole per-genre
	/// mix can be dialled back (or off) from skafinity.config.json without a rebuild.</summary>
	float MixTrim( float trim ) => 1f + (trim - 1f) * Math.Clamp( _c.GenreMix, 0f, 2f );
}
