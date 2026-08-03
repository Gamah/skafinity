using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The kit's voices: synthesised kick, snare, tom, hat, crash and ride.
//
// Each one returns immediately when the kit is muted (_drumGain 0) — it would write silence.
// The guard sits INSIDE the voices rather than at the call site because the caller interleaves
// pattern decisions with these calls: RenderDrumBar draws noise.Chance() to pick tom-vs-ghost
// and RenderFill draws rng.Chance() between hits, so skipping a call would move the stream.
// The per-voice `noise` draws being skipped are local — `noise` is a fresh per-block Rng, and
// when the kit is muted nothing downstream reads it.
//
// EVERY VOICE IS PARAMETERISED BY A TONE STRUCT, and every struct's Default reproduces the
// numbers the groove path has always played. A candidate tuning is an argument, not an edit, so
// the audition diagnostic can sweep one without the grooves hearing about it — which is what
// lets a kit be chosen by listening rather than by rebuilding between takes.
//
// Part of the MusicGen engine — see MusicGen.cs.

/// <summary>Two-pole band-pass (Chamberlin SVF), for the cymbal voices' resonant clusters. The
/// synth's own SVF is inline in the pitched render loop (Synth/Render.cs) and is not reachable
/// from here; this is the same filter, kept next to the voices that use it.</summary>
struct BandPass
{
	float _low, _band;
	readonly float _f, _q;

	public BandPass( float fc, float q, int sr )
	{
		_low = 0f; _band = 0f;
		// Clamped well under Nyquist: the Chamberlin form goes unstable as f approaches 2.
		_f = (float)(2 * Math.Sin( Math.PI * Math.Min( fc, sr * 0.15f ) / sr ));
		_q = Math.Clamp( q, 0.004f, 2f );
	}

	/// <summary>NORMALISED BY Q. A resonant band-pass has a centre gain of about 1/Q, so a
	/// ringing resonance is ~100× louder than a gentle one for the same input — which makes the
	/// Q a level control as well as a bandwidth, and there is no setting of the two that is then
	/// independently correct. Scaling by Q on the way out separates them again: how tight the
	/// resonance is, and how loud it is, become two numbers.</summary>
	public float Next( float x )
	{
		float high = x - _low - _q * _band;
		_band += _f * high;
		_low += _f * _band;
		return _band * _q;
	}
}

/// <summary>
/// WHAT THE AUDITION APPROVED, and it is mostly RANGES rather than values.
///
/// Several of round 2's questions came back "all of these work" — the click's corner at 1.8, 3.5
/// and 6 kHz; the rimshot's crack at 2.4, 3.2 and 4.2 kHz; both cross-sticks; both foot chicks.
/// That is not an undecided answer. A kit is a physical object being hit by a person, and the
/// same drum does not make the identical sound twice; a band of values that all read as the
/// right drum is exactly what NUANCE is, and picking one point out of it by ear would be
/// throwing the finding away. Phase 2 draws from these per song (and per hit where it says so),
/// which is the same reason RenderKick's round-robin jitter exists.
///
/// NOTHING HERE IS WIRED IN YET. The tone structs' Defaults are still what the grooves play, so
/// the render digests are untouched and Phase 1 stays provably pure. These are the numbers
/// Phase 2 wires, and they are recorded here rather than in a comment so they cannot drift.
/// </summary>
static class KitNuance
{
	/// <summary>Kick click low-pass corner. Full-band was the high tick on every body.</summary>
	public const float ClickCutMin = 1800f, ClickCutMax = 6000f;

	/// <summary>Rimshot crack centre. Darker reads as a bigger drum, brighter as a harder hit —
	/// both are the same articulation, so this is per HIT, not per song.</summary>
	public const float RimCrackMin = 2400f, RimCrackMax = 4200f;

	/// <summary>Cross-stick, between the two that were kept: the higher/thinner knock and the
	/// lower/thicker one. Crack centre and thud level move together.</summary>
	public const float StickCrackMin = 1350f, StickCrackMax = 1750f;
	public const float StickThudMin = 0.34f, StickThudMax = 0.50f;

	/// <summary>Foot chick, between the two that were kept: the tighter, brighter chick and the
	/// slower, darker one. All three move together — a foot that closes slower is duller and
	/// longer, because it is the same motion done differently.</summary>
	public const float FootAttackMin = 0.011f, FootAttackMax = 0.022f;
	public const float FootDurMin = 0.085f, FootDurMax = 0.115f;
	public const float FootCutMin = 2600f, FootCutMax = 3400f;

	/// <summary>The open hi-hat's ring, and its corner. Approved as a range for the same reason
	/// the others are: the tail of an open hat is how hard the foot was off it, which is a
	/// different amount every bar. A hat that rings the identical 600 ms eight times is the same
	/// tell as a kick that never varies.
	///
	/// <para>The band was 0.45–0.75 s, and that is a hat measured on its own rather than one in a
	/// pattern. With <c>decayFrac</c> ~0.45 it is a 200–340 ms time constant, while an eighth at
	/// the top of a genre's band is ~185 ms — so the open hat was still half up when the next
	/// stroke landed, on every open cell, and a groove that plays them continuously (the country
	/// train beat) reads as a wash that never clears rather than as an open hat. The range is kept
	/// because the nuance is real; it is the LENGTH that was a solo measurement.</para>
	///
	/// <para>THE VALUE HERE IS THE ONE THAT COUNTS: <c>HatTone.Default.openDur</c> is overridden
	/// per song from this band (see Compose.cs), so editing the preset moves nothing a listener
	/// hears — the digests not budging is what says so.</para></summary>
	public const float OpenHatDurMin = 0.26f, OpenHatDurMax = 0.42f;
	public const float OpenHatCut = 6250f;

	/// <summary>Where half open sits. The pedal's travel is geometric (see RenderHat), and this is
	/// the exponent on it: 1 is a straight ratio sweep, below 1 opens the low end of the travel
	/// sooner. A steady lift has to sound steady, which is the thing this number is set against —
	/// a single half-open hat cannot tell you whether the map is even, only whether one point on
	/// it is pleasant.</summary>
	public const float HatOpenCurve = 0.50f;

	/// <summary>A LIFT LANDS ON A CHOKE. The foot comes down on the downbeat and whatever is
	/// ringing stops; that is the event, and it reads correctly whether or not a stick lands with
	/// it. What does NOT work is a foot chick as the landing — the chick is its own articulation
	/// (it is the hat speaking on its own, on 2 and 4, under silence) and it has nothing to say
	/// at the end of a phrase that a choke has not already said.</summary>
	/// <summary>THE CYMBALS' BANDS. Carried over from the audition rounds that swept them on the
	/// mode-forest cymbal: they are parameters of the same three laws — how much splash, how much
	/// wash, how long the ring, where the bell's clang sits — so they transfer, but they were
	/// approved on a different spelling of those laws and want a fresh listen. Only what was
	/// actually swept is a band: the bell's splash and the bright crash's wash were never varied in
	/// front of a listener and stay at 1.</summary>
	public const float RideSplashMin = 0.5f, RideSplashMax = 1.8f;
	public const float RideWashMin = 1.0f, RideWashMax = 1.8f;
	public const float RideRingMin = 1.0f, RideRingMax = 1.4f;
	public const float BellClangMin = 2000f, BellClangMax = 2600f;
	public const float BellRingMin = 1.0f, BellRingMax = 1.4f;
	public const float CrashSplashMin = 0.5f, CrashSplashMax = 1.6f;
	public const float CrashRingMin = 0.7f, CrashRingMax = 1.4f;
	public const float DarkSplashMin = 0.5f, DarkSplashMax = 1.0f;
	public const float DarkWashMin = 1.0f, DarkWashMax = 1.6f;
	public const float DarkRingMin = 1.0f, DarkRingMax = 1.3f;

	/// <summary>Interpolate a kit nuance. <paramref name="u"/> is 0..1 — a per-song or per-hit
	/// draw. One helper so a nuance is always read the same way.</summary>
	public static float At( float min, float max, float u )
		=> min + (max - min) * Math.Clamp( u, 0f, 1f );
}

// ── The tone structs ──

/// <summary>The kick's body. Defaults are what the grooves have always played.</summary>
readonly struct KickTone
{
	public readonly float Dur;          // seconds
	public readonly double DecayFrac;   // decay time as a fraction of Dur
	public readonly float StartHz;      // pitch at the attack
	public readonly float DropHz;       // how far the pitch falls
	public readonly float DropRate;     // how fast it falls (in units of 1/Dur)
	public readonly float Drive;        // tanh drive on the body
	public readonly float SubHz;
	public readonly float SubLevel;
	public readonly double SubDecayFrac;
	public readonly float ClickLevel;
	public readonly float ClickSec;
	/// <summary>Low-pass corner on the click. 0 leaves it full-band — which is white noise, and
	/// reads as a high tick sitting on top of the drum rather than as a beater hitting a head.
	/// A beater is a soft mass on a skin: the attack it makes is mid-band.</summary>
	public readonly float ClickCut;
	public readonly float Beater;       // level of the beater-return transient (0 = none)
	public readonly float BeaterSec;    // how long after the hit the beater comes back
	public readonly float Jitter;       // round-robin variation depth, 0..1

	public KickTone( float dur, double decayFrac, float startHz, float dropHz, float dropRate,
		float drive, float subHz, float subLevel, double subDecayFrac, float clickLevel,
		float clickSec, float clickCut, float beater, float beaterSec, float jitter )
	{
		Dur = dur; DecayFrac = decayFrac; StartHz = startHz; DropHz = dropHz; DropRate = dropRate;
		Drive = drive; SubHz = subHz; SubLevel = subLevel; SubDecayFrac = subDecayFrac;
		ClickLevel = clickLevel; ClickSec = clickSec; ClickCut = clickCut;
		Beater = beater; BeaterSec = beaterSec;
		Jitter = jitter;
	}

	public KickTone With( float dur = -1f, double decayFrac = -1.0, float startHz = -1f,
		float dropHz = -1f, float dropRate = -1f, float drive = -1f, float subHz = -1f,
		float subLevel = -1f, double subDecayFrac = -1.0, float clickLevel = -1f,
		float clickSec = -1f, float clickCut = -1f, float beater = -1f, float jitter = -1f )
		=> new( dur < 0 ? Dur : dur, decayFrac < 0 ? DecayFrac : decayFrac,
			startHz < 0 ? StartHz : startHz, dropHz < 0 ? DropHz : dropHz,
			dropRate < 0 ? DropRate : dropRate, drive < 0 ? Drive : drive,
			subHz < 0 ? SubHz : subHz, subLevel < 0 ? SubLevel : subLevel,
			subDecayFrac < 0 ? SubDecayFrac : subDecayFrac,
			clickLevel < 0 ? ClickLevel : clickLevel, clickSec < 0 ? ClickSec : clickSec,
			clickCut < 0 ? ClickCut : clickCut,
			beater < 0 ? Beater : beater, BeaterSec, jitter < 0 ? Jitter : jitter );

	public static readonly KickTone Default = new(
		dur: 0.17f, decayFrac: 0.31, startHz: 127f, dropHz: 80f, dropRate: 2.6f, drive: 1.6f,
		subHz: 44f, subLevel: 0.3f, subDecayFrac: 0.55, clickLevel: 0.55f, clickSec: 0.003f,
		clickCut: 0f, beater: 0f, beaterSec: 0.023f, jitter: 0f );
}

/// <summary>How the snare is struck. The single-hit articulations are tone presets; the flam and
/// the buzz are gestures made of several hits and have their own entry points.</summary>
enum SnareHit { Hit, Ghost, Rimshot, CrossStick, SnaresOff }

readonly struct SnareTone
{
	public readonly float Dur;
	public readonly double DecayFrac;
	public readonly float Hz1, Hz2;
	public readonly float Body2;      // level of the second shell partial
	public readonly float BodyLevel;
	public readonly float Sag;        // how far the shell pitch falls over the hit
	public readonly float Wire;       // amount of snare-wire noise
	public readonly float WireCut;    // wire high-pass corner
	public readonly float WireDrive;
	/// <summary>How hard this articulation is struck, relative to a plain backbeat.</summary>
	public readonly float Level;
	/// <summary>THE CRACK: a tight, fast band of noise at the attack — stick on rim, wood on wood.
	/// It is what a rimshot and a cross-stick actually ARE. Reaching for them with the shell
	/// partials instead is what makes a rimshot ring like a tom and a cross-stick read as a clave:
	/// two loud sines with a slow decay are a pitched percussion instrument, whatever they are
	/// labelled. 0 = no crack, which is the plain backbeat.</summary>
	public readonly float CrackHz, CrackQ, CrackLevel;
	public readonly double CrackDecayFrac;
	/// <summary>A little low knock under the crack — the shell moving. Not a tone.</summary>
	public readonly float ThudHz, ThudLevel;

	public SnareTone( float dur, double decayFrac, float hz1, float hz2, float body2,
		float bodyLevel, float sag, float wire, float wireCut, float wireDrive, float level,
		float crackHz = 0f, float crackQ = 0.35f, float crackLevel = 0f,
		double crackDecayFrac = 0.10, float thudHz = 0f, float thudLevel = 0f )
	{
		Dur = dur; DecayFrac = decayFrac; Hz1 = hz1; Hz2 = hz2; Body2 = body2;
		BodyLevel = bodyLevel; Sag = sag; Wire = wire; WireCut = wireCut; WireDrive = wireDrive;
		Level = level;
		CrackHz = crackHz; CrackQ = crackQ; CrackLevel = crackLevel;
		CrackDecayFrac = crackDecayFrac; ThudHz = thudHz; ThudLevel = thudLevel;
	}

	public SnareTone With( float dur = -1f, double decayFrac = -1.0, float hz1 = -1f, float hz2 = -1f,
		float bodyLevel = -1f, float sag = -1f, float wire = -1f, float wireCut = -1f,
		float level = -1f, float crackHz = -1f, float crackLevel = -1f, float thudLevel = -1f )
		=> new( dur < 0 ? Dur : dur, decayFrac < 0 ? DecayFrac : decayFrac,
			hz1 < 0 ? Hz1 : hz1, hz2 < 0 ? Hz2 : hz2, Body2,
			bodyLevel < 0 ? BodyLevel : bodyLevel, sag < 0 ? Sag : sag,
			wire < 0 ? Wire : wire, wireCut < 0 ? WireCut : wireCut, WireDrive,
			level < 0 ? Level : level, crackHz < 0 ? CrackHz : crackHz, CrackQ,
			crackLevel < 0 ? CrackLevel : crackLevel, CrackDecayFrac, ThudHz,
			thudLevel < 0 ? ThudLevel : thudLevel );

	public static readonly SnareTone Default = new(
		dur: 0.15f, decayFrac: 0.32, hz1: 185f, hz2: 268f, body2: 0.6f, bodyLevel: 0.375f,
		sag: 0.14f, wire: 0.6f, wireCut: 1350f, wireDrive: 1.2f, level: 1f );

	/// <summary>The ghost note — the groove path's `ghost: true`, byte for byte.</summary>
	public static readonly SnareTone Ghost = new(
		dur: 0.06f, decayFrac: 0.3, hz1: 185f, hz2: 268f, body2: 0.6f, bodyLevel: 0.375f,
		sag: 0.14f, wire: 0.6f, wireCut: 1350f, wireDrive: 1.2f, level: 0.3f );

	/// <summary>Stick on rim and head together: the shell speaks louder and higher, the wires
	/// crack harder, and the whole thing is shorter than a struck note.</summary>
	public static readonly SnareTone Rimshot = new(
		dur: 0.14f, decayFrac: 0.20, hz1: 300f, hz2: 452f, body2: 0.5f, bodyLevel: 0.13f,
		sag: 0.26f, wire: 0.95f, wireCut: 1900f, wireDrive: 2.6f, level: 1.25f,
		crackHz: 3200f, crackQ: 0.55f, crackLevel: 2.2f, crackDecayFrac: 0.055 );

	/// <summary>Stick laid across the head, struck on the rim: a woody KNOCK. It is damped by the
	/// hand holding the stick down, so it does not ring — and it is the ringing, not the pitch,
	/// that makes a bright short tone read as a clave.</summary>
	public static readonly SnareTone CrossStick = new(
		dur: 0.05f, decayFrac: 0.085, hz1: 520f, hz2: 735f, body2: 0.45f, bodyLevel: 0.24f,
		sag: 0.30f, wire: 0.07f, wireCut: 2400f, wireDrive: 1.0f, level: 0.85f,
		crackHz: 1750f, crackQ: 0.75f, crackLevel: 1.3f, crackDecayFrac: 0.10,
		thudHz: 155f, thudLevel: 0.34f );

	/// <summary>Wires thrown off: no crack, just the shell — which is what makes it read as a
	/// tom rather than as a quiet snare.</summary>
	public static readonly SnareTone SnaresOff = new(
		dur: 0.22f, decayFrac: 0.30, hz1: 178f, hz2: 253f, body2: 0.62f, bodyLevel: 0.52f,
		sag: 0.22f, wire: 0.04f, wireCut: 1350f, wireDrive: 1.0f, level: 1f );

	public static SnareTone For( SnareHit h ) => h switch
	{
		SnareHit.Ghost => Ghost,
		SnareHit.Rimshot => Rimshot,
		SnareHit.CrossStick => CrossStick,
		SnareHit.SnaresOff => SnaresOff,
		_ => Default,
	};
}

/// <summary>How a three-piece tom set is tuned. The interval is the character; which pitch it
/// starts from comes from the song's key.</summary>
enum TomTune
{
	/// <summary>Two stacked perfect fourths — the conventional tuning, and the one whose fills
	/// read as a descending scale rather than as a slide.</summary>
	Fourths,
	/// <summary>Fifths: a wider spread, so each drum is unmistakably its own drum.</summary>
	Wide,
	/// <summary>Stacked major thirds — close-tuned, so a fill reads as one gesture across a kit
	/// rather than as three separate notes.</summary>
	Thirds,
	/// <summary>A fixed physical set that ignores the key entirely. A drummer does not retune
	/// between songs, and this is the candidate that says so.</summary>
	Fixed,
}

/// <summary>THE KIT, not a pitch: three tom pitches in fixed positions, addressed by INDEX.
///
/// 0 is the rack tom (highest), 2 the floor (lowest). Everything downstream takes the index, so
/// a fill's position across the stereo field comes from which drum was hit — the old map from a
/// frequency onto a hardcoded 145–260 Hz range could be, and was, driven off its own bottom end
/// by the fills that used it. Same trick as Register(octaves): the wrong version is unwriteable.
/// </summary>
readonly struct TomKit
{
	public const int Count = 3;

	readonly float _f0, _f1, _f2;
	public readonly bool RackLeft;

	public TomKit( float f0, float f1, float f2, bool rackLeft = true )
	{
		_f0 = f0; _f1 = f1; _f2 = f2; RackLeft = rackLeft;
	}

	public float Hz( int i ) => i <= 0 ? _f0 : i == 1 ? _f1 : _f2;

	/// <summary>Where this drum sits, −1 hard left … +1 hard right, before the kit's own spread.
	/// The caller scales it (the drums' stereo width is one number and lives outside the kit).
	/// </summary>
	public float Pan( int i )
	{
		float u = Math.Clamp( i, 0, Count - 1 ) / (Count - 1f);   // 0 rack … 1 floor
		return (RackLeft ? 1f : -1f) * (u * 2f - 1f);
	}

	/// <summary>The tuning for a song in this key. The key sets WHICH pitch the set starts on;
	/// the shape sets the intervals. Only the pitch CLASS is read, so the set stays inside a
	/// drum-sized range whatever octave the song is written in — and it is drawn from the song's
	/// root rather than a section's key shift, so toms cannot drift mid-song.</summary>
	public static TomKit Tuned( TomTune shape, int rootMidi, bool rackLeft = true )
	{
		if ( shape == TomTune.Fixed ) return new TomKit( 196f, 147f, 110f, rackLeft );
		int pc = ((rootMidi % 12) + 12) % 12;
		int floorMidi = 41 + pc;                       // 87 .. 165 Hz — a floor tom's range
		int step = shape switch { TomTune.Wide => 7, TomTune.Thirds => 4, _ => 5 };
		return new TomKit( Midi( floorMidi + 2 * step ), Midi( floorMidi + step ),
			Midi( floorMidi ), rackLeft );
	}
}

readonly struct TomTone
{
	public readonly float Dur;
	public readonly double DecayFrac;
	public readonly float Sag;            // how far the head's pitch falls over the hit
	public readonly float SnapMul;        // the inharmonic upper partial, as a ratio
	public readonly float SnapLevel;
	public readonly double SnapDecayFrac;
	public readonly float ClickLevel;     // stick attack
	public readonly float ClickSec;

	public TomTone( float dur, double decayFrac, float sag, float snapMul, float snapLevel,
		double snapDecayFrac, float clickLevel, float clickSec )
	{
		Dur = dur; DecayFrac = decayFrac; Sag = sag; SnapMul = snapMul; SnapLevel = snapLevel;
		SnapDecayFrac = snapDecayFrac; ClickLevel = clickLevel; ClickSec = clickSec;
	}

	public TomTone With( float dur = -1f, double decayFrac = -1.0, float sag = -1f,
		float snapLevel = -1f, float clickLevel = -1f )
		=> new( dur < 0 ? Dur : dur, decayFrac < 0 ? DecayFrac : decayFrac, sag < 0 ? Sag : sag,
			SnapMul, snapLevel < 0 ? SnapLevel : snapLevel, SnapDecayFrac,
			clickLevel < 0 ? ClickLevel : clickLevel, ClickSec );

	public static readonly TomTone Default = new(
		dur: 0.18f, decayFrac: 0.3, sag: 0.22f, snapMul: 2.5f, snapLevel: 0.5f,
		snapDecayFrac: 0.06, clickLevel: 0.45f, clickSec: 0.006f );
}

/// <summary>What the hi-hat does. Openness is a continuum and lives outside this — these are the
/// articulations that are not simply "how far open".</summary>
enum HatHit { Stick, Foot, Splash }

readonly struct HatTone
{
	public readonly float ClosedDur, OpenDur;
	public readonly double DecayFrac;
	public readonly float ClosedCut, OpenCut;
	public readonly float Level;
	public readonly float LowThud;      // the foot's pedal-board thump; 0 for a stick hit
	/// <summary>An ATTACK RAMP. A stick hit starts instantly; a foot chick does not — the cymbals
	/// travel together and the sound arrives over a few milliseconds. That ramp is the difference
	/// between "shhck" and a quiet closed hit, and no amount of filtering substitutes for it.</summary>
	public readonly float AttackSec;
	/// <summary>The curve openness travels on. Linear puts half-open half way between a 35 ms tick
	/// and an open hat, which is nowhere near half way in what is HEARD — the ear reads a ratio,
	/// not a difference, so the middle of a linear map is still a closed hat.</summary>
	public readonly float OpenCurve;
	/// <summary>Loose cymbals rattling against each other. It peaks at half open, because that is
	/// the only place two cymbals are touching AND free to move.</summary>
	public readonly float SizzleHz, SizzleDepth;

	public HatTone( float closedDur, float openDur, double decayFrac, float closedCut,
		float openCut, float level, float lowThud, float attackSec = 0f, float openCurve = 1f,
		float sizzleHz = 0f, float sizzleDepth = 0f )
	{
		ClosedDur = closedDur; OpenDur = openDur; DecayFrac = decayFrac; ClosedCut = closedCut;
		OpenCut = openCut; Level = level; LowThud = lowThud;
		AttackSec = attackSec; OpenCurve = openCurve; SizzleHz = sizzleHz; SizzleDepth = sizzleDepth;
	}

	public HatTone With( float closedDur = -1f, float openDur = -1f, double decayFrac = -1.0,
		float closedCut = -1f, float openCut = -1f, float level = -1f, float attackSec = -1f,
		float openCurve = -1f, float sizzleHz = -1f, float sizzleDepth = -1f )
		=> new( closedDur < 0 ? ClosedDur : closedDur, openDur < 0 ? OpenDur : openDur,
			decayFrac < 0 ? DecayFrac : decayFrac, closedCut < 0 ? ClosedCut : closedCut,
			openCut < 0 ? OpenCut : openCut, level < 0 ? Level : level, LowThud,
			attackSec < 0 ? AttackSec : attackSec, openCurve < 0 ? OpenCurve : openCurve,
			sizzleHz < 0 ? SizzleHz : sizzleHz, sizzleDepth < 0 ? SizzleDepth : sizzleDepth );

	/// <remarks>openDur here is only the base a song varies FROM: Compose.cs overrides it out of
	/// KitNuance.OpenHatDurMin/Max on every song, so this number reaches nothing but the audition
	/// path. Change the band, not this.</remarks>
	public static readonly HatTone Default = new(
		closedDur: 0.035f, openDur: 0.16f, decayFrac: 0.4, closedCut: 7000f, openCut: 7000f,
		level: 1f, lowThud: 0f );

	/// <summary>The foot chick: the pedal closing the cymbals with no stick involved. Duller
	/// than a struck closed hat and carrying the board's own thump.</summary>
	public static readonly HatTone Foot = new(
		closedDur: 0.085f, openDur: 0.085f, decayFrac: 0.34, closedCut: 3400f, openCut: 3400f,
		level: 0.9f, lowThud: 0.16f, attackSec: 0.011f );

	/// <summary>Foot splash: opened and closed again in one motion — a short open hat with a
	/// bright top and no tail to speak of.</summary>
	public static readonly HatTone Splash = new(
		closedDur: 0.26f, openDur: 0.26f, decayFrac: 0.30, closedCut: 8200f, openCut: 8200f,
		level: 0.85f, lowThud: 0.10f );

	public static HatTone For( HatHit h ) => h switch
	{
		HatHit.Foot => Foot,
		HatHit.Splash => Splash,
		_ => Default,
	};
}

/// <summary>
/// THE CYMBAL, DISTILLED — a measured spectrum spent on thirteen components instead of four
/// hundred.
///
/// Real cymbals were measured for this (provenance below) and the measurement collapsed into three
/// laws. The first attempt spent them on a MODE FOREST: ~390 resolved partials for the ride, each
/// with its own ring time. It was accurate, and it was wrong twice over — it cost ~250 ms of CPU a
/// hit, and it out-detailed every other voice in the engine by two orders of magnitude. The rest of
/// this kit is two or three sines and some filtered noise; a cymbal built to a different standard
/// does not sit in that mix at any level, because the problem is not that it is loud. So the laws
/// are kept and the spelling is not:
///
///   * <b>τ·√f constant</b> → <b>PER-BAND DECAY</b>. Seven noise bands whose ring times fall as
///     1/√f. This is the whole of what says "struck metal" and it is seven numbers.
///   * <b>a mode forest at constant density</b> → <b>BAND-LIMITED NOISE</b>. Density the ear cannot
///     resolve into partials IS noise; four hundred resonators were an expensive way to spell it.
///   * <b>beating near-pairs</b> → <b>ONE LOW PAIR</b> of real partials, quiet, down where the ear
///     resolves the beat and where a cymbal's size is heard.
///   * <b>strike position as a log-Gaussian bump</b> → <b>the band gains</b>, one evaluation each.
///   * splash and wash ride underneath, as they always did.
///
/// THIS IS NOT GENERATION 2, AND THE DIFFERENCE IS ONE PROPERTY. Lightly band-passed noise was
/// tried early and came back "hats in weird states" — correct, and the cause was that it had ONE
/// decay for the whole voice. A noise band that dies uniformly is a hat. Bands whose ring times
/// diverge by a factor of five across the spectrum are a cymbal, and that divergence is measured
/// rather than dialled. Equally it is not generation 5, which made the cymbal out of pure
/// waveforms and produced church bells every time: the only tonal components here are one quiet
/// low pair, and everything above them is noise. The two failures bracket the target — uniform
/// decay is a hat, resolvable partials are a bell — and per-band decay is what sits between them.
///
/// Provenance (NOT vendored, and must not become a dependency — what lands is these constants and
/// this citation): Virtuosity Drums by Versilian Studios &amp; Karoryfer Samples,
/// github.com/sfzinstruments/virtuosity_drums, CC0-1.0. Measured 2026-08-02 from the overhead-mic
/// samples — oh_ride_ride_vl3 (bow), oh_ride_bell_vl3 (bell), oh_crash_crash_vl3 (bright crash),
/// oh_flatride_crash_vl4 (dark crash). Sustained partials from a 131072-point FFT starting 0.33 s
/// after onset; per-band ring times from exponential fits over a 4096/1024 STFT. tools/spectool is
/// the reader and stays in the repo, so every number here can be re-derived — and --cymbal writes
/// one dry hit per cymbal to feed it, because a spectrum fitted to a measurement is not fitted
/// until the RESULT has been measured the same way.
/// </summary>
readonly struct CymbalBands
{
	/// <summary>The noise bands: centre, gain, and the ring time that band decays with.</summary>
	public readonly float[] Hz, Amp, Tau;
	public readonly float Dur, Level, Stick, StickCut;
	public readonly float SplashLvl, SplashTau, WashLvl, WashTau, NoiseHp, WashLp;
	/// <summary>Where the SPLASH starts, separately from the wash. A crash's attack is measured
	/// broadband and stays that way; a ride's is a stick touching metal, which is a high-frequency
	/// event — and it is the only part of a ride that lands in a band the rest of the arrangement
	/// leaves empty. One shared corner meant every attempt to make the stroke cut also added to
	/// the mids, where it is masked and does nothing but thicken.</summary>
	public readonly float SplashHp;

	CymbalBands( float[] hz, float[] amp, float[] tau, float dur, float level, float stick,
		float stickCut, float splashLvl, float splashTau, float washLvl, float washTau,
		float noiseHp, float washLp, float splashHp )
	{
		Hz = hz; Amp = amp; Tau = tau; Dur = dur; Level = level; Stick = stick; StickCut = stickCut;
		SplashLvl = splashLvl; SplashTau = splashTau; WashLvl = washLvl; WashTau = washTau;
		NoiseHp = noiseHp; WashLp = washLp; SplashHp = splashHp;
	}

	// ── The laws ──

	/// <summary>LAW 1 — the ring, and it is the law that is NOT shared between cymbals. On the ride
	/// every per-band fit lands on τ ≈ 39/√f within take-to-take scatter (230 Hz → 2.6 s, 850 → 1.3,
	/// 2.7 k → 0.75, 5.7 k → 0.51), and the long ring is the instrument. Both crashes measured a
	/// different shape and each needs one extra term:
	///
	///  * <paramref name="knee"/> — a LOW CUT. The bright crash holds τ·√f ≈ 45 only above ~1 kHz;
	///    below that its lows die fast (0.9 s at 375 Hz where the bare law says 2.0). A big thin
	///    plate struck hard dumps its low modes into the room at once.
	///  * <paramref name="sizzle"/> — a rising FLOOR. The dark crash inverts the ride: low-mids gone
	///    in half a second while 7–14 kHz rings for 1.5–2.0 s. Wash and rivet behaviour rather than
	///    plate behaviour, so it is a second term taking over where it is the longer of the two.
	/// </summary>
	static float RingTau( float hz, float k, float knee, float sizzle )
	{
		float t = k / MathF.Sqrt( hz );
		if ( knee > 0f && hz < knee ) t *= hz / knee;
		if ( sizzle > 0f ) t = MathF.Max( t, sizzle * MathF.Sqrt( hz / SizzleRef ) );
		return t;
	}
	const float SizzleRef = 8000f;   // where the sizzle term is quoted: dark crash τ ≈ 1.5 s there

	/// <summary>LAW 2 — the band set. The forest's density said the partials are unresolvable, so
	/// what matters is only how the energy and the ring vary ACROSS frequency: seven bands,
	/// geometrically spaced, is enough resolution for a curve that changes by a factor of five over
	/// the whole range. The top must reach ~12 kHz — the measured sustain holds −9…−14 dB at
	/// 5–10 kHz ringing at ~0.5 s, and an earlier version cut off at 4 kHz and measured 10–15 dB
	/// light against the reference.</summary>
	/// <summary>NO TONAL COMPONENTS AT ALL, and the band set reaches down instead. An earlier pass
	/// kept one low PAIR of real partials for the measured beating — two sines a few Hz apart, on
	/// the argument that a beat is not a pitch. It is a pitch: 232 Hz ringing for two and a half
	/// seconds under noise bands that decay much faster is the most exposed thing in the voice, and
	/// it read as a sine sitting inside every cymbal. That is generation 5's church bell arriving
	/// through a side door, at a lower level and with a better excuse. The bottom band carries the
	/// cymbal's size instead, as noise, which is what the rest of the voice is made of.</summary>
	const int Bands = 8;
	const float BandLo = 180f, BandHi = 12000f;
	/// <summary>Band width, as the filter's damping. Wide enough that the filter itself does not
	/// ring — its own decay is under 2 ms at the bottom band — because the DECAY here is the
	/// envelope's job. A resonance that rings is a partial, and partials are what generation 5
	/// proved a cymbal must not be made of.</summary>
	const float BandQ = 0.7f;

	/// <summary>LAW 3 — a strike position is a spectral bump on a log axis. The bow is one wide
	/// bump (the measured sustain is flat 300 Hz–3.2 kHz with soft edges); the bell is a narrow
	/// clang plus a small low knock where the stick shocks the cup; the bright crash centres at
	/// 2.2–4.7 kHz over a low knock; the dark crash is low-heavy with a sizzle top that has to be
	/// excited before the ring law can let it outlive anything.</summary>
	static float LogBump( float f, float centre, float width )
	{
		float u = MathF.Log( f / centre ) / width;
		return MathF.Exp( -0.5f * u * u );
	}

	readonly struct Strike
	{
		public readonly float Centre, Width, Centre2, Width2, Level2;
		public Strike( float centre, float width, float centre2 = 0f, float width2 = 0.3f,
			float level2 = 0f )
		{
			Centre = centre; Width = width;
			Centre2 = centre2; Width2 = width2; Level2 = level2;
		}
		public float Weight( float f )
			=> LogBump( f, Centre, Width ) + (Level2 > 0f ? Level2 * LogBump( f, Centre2, Width2 ) : 0f);
	}

	// THE BOW'S BUMP IS A MIX DECISION AND NOT THE MEASUREMENT. The reference puts a real ride's
	// sustain at ~1.2 kHz, and that is where this sat — which is both darker than the hi-hat it
	// stands in for and, worse, exactly where the guitars are. Measured against an open hat as
	// spectral centroid: the hat is 12.5 kHz at the attack and STILL 12.4 kHz half a second later,
	// because it is one high-passed noise with one decay; the ride was 10.4 kHz falling to 8.8 kHz,
	// because tau=k/sqrt(f) means the low bands outlive the high ones and a cymbal built on that law
	// ALWAYS darkens as it rings. So a ride reads as the dark voice against a hat that never moves,
	// which is backwards for the one carrying the pulse. At 4200 Hz with the noise floor lifted the
	// tail comes to 10.9 kHz — still under the hat, and 2.1 kHz brighter than it was.
	static readonly Strike BowStrike = new( 4200f, 0.95f );
	static Strike BellStrike( float clang ) => new( clang, 0.45f, 290f, 0.25f, 0.30f );
	static readonly Strike BrightCrashStrike = new( 3200f, 0.80f, 400f, 0.55f, 0.25f );
	static readonly Strike DarkCrashStrike = new( 520f, 0.75f, 9000f, 0.60f, 0.45f );

	/// <summary>How loud one STROKE is, and it is not the same for a ride and a crash even though
	/// the same arm strikes both. A ride stroke rings for seconds and is played eight times a bar,
	/// so a riding section has a dozen rings sounding at once; the hi-hat it replaces is 35 ms and
	/// never overlaps itself. Level per stroke and level in the mix are two different quantities —
	/// measured with --levels, a ride at the crash's stroke level put country's whole kit 2.6 dB
	/// over the rest of its band on its riding sections alone. A crash overlaps nothing, being one
	/// gesture a phrase, so it keeps the louder stroke.</summary>
	// StrokeLevelRide was 0.30 and went to 0.95 in one +10 dB step, by ear, to stop the ride being
	// buried — and that commit's own message flagged the result as "worth watching". This is that
	// watch firing: measured in the >2.5 kHz band, where nothing else in a ska arrangement lives,
	// 0.95 puts the ride +6.9 dB over the ENTIRE REST OF THE MIX and holds that band within 12 dB
	// of its own peak 43% of the time. 0.30 was +1.4 dB and 12.8%, which is the buried the boost
	// was aimed at; 0.55 is +3.65 dB and 21.3% — present without being the arrangement.
	//
	// The lesson is about the measurement, not the number: a level set by ear against ONE
	// balance ("can I hear the ride?") has no way to notice the other one ("is the ride now the
	// loudest thing in its band?"), and a +10 dB single step is where that goes wrong. The
	// duty-cycle-in-band reading answers both and is what the next change to this should use.
	const float StrokeLevelRide = 0.45f, StrokeLevelCrash = 0.60f;

	/// <summary>The extra decay a cymbal takes on WHILE IT IS BEING PLAYED — see RenderCymbal's
	/// chokeTau. A stroke landing on a ringing cymbal excites it and damps it, because the stick is
	/// in contact with the metal, and that is the difference between a ride and a drone. It has to
	/// compound over a stroke train the way the physics does: ring time falls with frequency, so at
	/// riding eighths a train stacks the 250 Hz band +7.6 dB over a single stroke against +2.4 dB at
	/// 5 kHz, and it is the LOW ring that runs away. A flat level cut cannot fix that — it takes the
	/// attack down with the drone. An added decay is frequency-dependent in the right direction: a
	/// fixed extra rate costs a 2.5 s band most of its tail and a 0.5 s one very little.</summary>
	public const float RestrikeTau = 0.70f;

	/// <summary>How much of the measured crash ring is kept. THIS IS A MIX DECISION AND NOT A
	/// MEASUREMENT — a real crash in a room rings for three or four seconds and the analysis says
	/// so, but a crash lands at every phrase end and every section start here, so at that density
	/// the full ring never clears before the next one and the arrangement swims. The law stays
	/// legible and the departure from it is one number rather than a quietly re-fitted constant.
	/// The ride is untouched: it is struck far more often but its strokes damp each other
	/// (RestrikeTau), which is the physical version of the same problem.</summary>
	const float CrashRingScale = 0.45f;

	/// <summary>THE STROKE HAS TO CUT, and level is the wrong lever for that. Measured against the
	/// hi-hat it replaces, the ride carries MORE energy — and it still disappears in a band mix,
	/// because of where that energy sits: a hat is high-passed noise with essentially everything
	/// above 5 kHz, a band nothing else in the arrangement occupies, while the ride's measured
	/// sustain is a bump at 1.2 kHz spreading flat from 180 Hz up, straight through the guitars.
	/// Equal energy, very unequal audibility, and raising the level only adds to the part that is
	/// masked. The splash is the answer and it is faithful to the measurement: the sustain is
	/// mid-centred but the ATTACK is broadband, so a bigger splash is a per-stroke click in the
	/// clear rather than more mud. That is what a ride's ping is, and it is inside the 0.5–1.8
	/// band the audition approved.</summary>
	/// <remarks>THE KNEE IS WHY A RIDE STROKE STOPS READING AS A CRASH. Measured per band as the
	/// time to fall 20 dB, the ride against the bright crash was low 2.80 s vs 1.00, mid 2.53 vs
	/// 0.89, upper-mid 2.03 vs 0.87, top 1.53 vs 0.81 — while the two spectra's band WEIGHTS are
	/// within a dB of each other (mid −8.4 dB vs −8.5). A ride whose spectral balance is a crash's
	/// and whose tail is 2.8× longer is a crash that will not stop, and that is what it sounded
	/// like wherever the mix got sparse enough to expose it. The crash already carried a knee for
	/// this reason; the ride carried none, so its mids rang at the full τ=k/√f. At 2 kHz the mid
	/// comes to 1.47 s — still 1.65× the crash, because a ride SHOULD sustain longer than one, and
	/// no longer the same object. The stick attack is untouched (peak still inside the first 20 ms),
	/// which is the point: this shortens the wash behind the ping, not the ping.</remarks>
	public static CymbalBands Bow( float splash = 1f, float wash = 1f, float ring = 1f )
		=> Build( BowStrike, tauK: 39f, knee: 3500f, sizzle: 0f, ring: ring,
			splash: 1.25f * splash, splashTau: 0.10f, washLvl: 0.060f * wash,
			washTau: 0.70f * ring, stick: 0.55f, stickCut: 9000f, noiseHp: 900f, washLp: 6500f,
			level: StrokeLevelRide, splashHp: 3200f );

	/// <summary>The bell. A ride bell is not a church bell: no harmonic stack and no low
	/// fundamental — the measurement puts its energy in a clang cluster around 2.3 kHz over the
	/// same metal as the bow.</summary>
	public static CymbalBands Bell( float splash = 1f, float ring = 1f, float clang = 2300f )
		=> Build( BellStrike( clang ), tauK: 39f, knee: 3500f, sizzle: 0f, ring: ring,
			splash: 0.40f * splash, splashTau: 0.05f, washLvl: 0.030f,
			washTau: 0.55f * ring, stick: 0.40f, stickCut: 6500f, noiseHp: 240f, washLp: 6500f,
			level: StrokeLevelRide, splashHp: 2600f );

	/// <summary>The bright crash. THE ROAR IS THE INSTRUMENT: a third of a second in, the
	/// measurement resolves essentially no partials at all. So the splash is not an attack
	/// transient here, it is a layer with its own third of a second of decay.</summary>
	public static CymbalBands CrashBright( float splash = 1f, float ring = 1f, float wash = 1f )
		=> Build( BrightCrashStrike, tauK: 45f, knee: 1000f, sizzle: 0f, ring: ring * CrashRingScale,
			splash: 2.30f * splash, splashTau: 0.30f, washLvl: 0.55f * wash,
			washTau: 1.05f * ring, stick: 0.10f, stickCut: 6000f, noiseHp: 2400f, washLp: 6200f,
			level: StrokeLevelCrash, splashHp: 900f );

	/// <summary>The dark crash — a heavier, flatter cymbal crashed rather than ridden, and the
	/// opposite shape at both ends: resolved lows that ring, a body gone in half a second, and a
	/// top that outlives everything.</summary>
	public static CymbalBands CrashDark( float splash = 1f, float ring = 1f, float wash = 1f )
		=> Build( DarkCrashStrike, tauK: 13.5f, knee: 0f, sizzle: 1.5f, ring: ring * CrashRingScale,
			splash: 1.50f * splash, splashTau: 0.22f, washLvl: 0.35f * wash,
			washTau: 0.90f * ring, stick: 0.10f, stickCut: 4500f, noiseHp: 200f, washLp: 4200f,
			level: StrokeLevelCrash, splashHp: 200f );

	static CymbalBands Build( in Strike strike, float tauK, float knee, float sizzle, float ring,
		float splash, float splashTau, float washLvl, float washTau, float stick, float stickCut,
		float noiseHp, float washLp, float level, float splashHp )
	{
		var hz = new float[Bands]; var am = new float[Bands]; var ta = new float[Bands];
		float step = MathF.Pow( BandHi / BandLo, 1f / (Bands - 1) );
		float e = 0f, maxTau = 0f;
		for ( int b = 0; b < Bands; b++ )
		{
			float f = BandLo * MathF.Pow( step, b );
			hz[b] = f;
			am[b] = strike.Weight( f );
			ta[b] = ring * RingTau( f, tauK, knee, sizzle );
			e += am[b] * am[b];
			maxTau = MathF.Max( maxTau, ta[b] );
		}
		// Energy-normalised, so the four strikes land comparably before the stroke level is applied.
		float lvl = level / MathF.Sqrt( MathF.Max( 1e-6f, e ) );
		// Long enough for the longest component to reach about −45 dB, and no longer: every sample
		// past that is a multiply spent on silence, and this voice is rendered per HIT.
		// Long enough for the longest band to reach about −31 dB and no longer. A cymbal in a room
		// keeps going past that; a cymbal in a mix with a whole kit over it does not, and every
		// sample past the point it stops being audible is a multiply spent on silence.
		float dur = Math.Clamp( maxTau * 3.6f, 0.6f, 3.0f );
		return new CymbalBands( hz, am, ta, dur, lvl, stick, stickCut,
			splash, splashTau, washLvl, washTau, noiseHp, washLp, splashHp );
	}
}

/// <summary>This song's cymbals as NUMBERS — the per-song point each of KitNuance's cymbal bands
/// sits at. Drawn in ComposePlan so every genre pulls the same values in the same order, for the
/// same reason a kick's click corner is drawn there: a kit is a physical object and the same
/// cymbal does not make the identical sound twice.</summary>
readonly struct CymbalDraw
{
	public readonly float RideSplash, RideWash, RideRing, BellClang, BellRing;
	public readonly float BrightSplash, BrightRing, DarkSplash, DarkWash, DarkRing;

	CymbalDraw( float rideSplash, float rideWash, float rideRing, float bellClang, float bellRing,
		float brightSplash, float brightRing, float darkSplash, float darkWash, float darkRing )
	{
		RideSplash = rideSplash; RideWash = rideWash; RideRing = rideRing;
		BellClang = bellClang; BellRing = bellRing;
		BrightSplash = brightSplash; BrightRing = brightRing;
		DarkSplash = darkSplash; DarkWash = darkWash; DarkRing = darkRing;
	}

	public static readonly CymbalDraw Default = new( 1f, 1f, 1f, 2300f, 1f, 1f, 1f, 1f, 1f, 1f );

	public static CymbalDraw Draw( Rng rng ) => new(
		KitNuance.At( KitNuance.RideSplashMin, KitNuance.RideSplashMax, rng.Next() ),
		KitNuance.At( KitNuance.RideWashMin, KitNuance.RideWashMax, rng.Next() ),
		KitNuance.At( KitNuance.RideRingMin, KitNuance.RideRingMax, rng.Next() ),
		KitNuance.At( KitNuance.BellClangMin, KitNuance.BellClangMax, rng.Next() ),
		KitNuance.At( KitNuance.BellRingMin, KitNuance.BellRingMax, rng.Next() ),
		KitNuance.At( KitNuance.CrashSplashMin, KitNuance.CrashSplashMax, rng.Next() ),
		KitNuance.At( KitNuance.CrashRingMin, KitNuance.CrashRingMax, rng.Next() ),
		KitNuance.At( KitNuance.DarkSplashMin, KitNuance.DarkSplashMax, rng.Next() ),
		KitNuance.At( KitNuance.DarkWashMin, KitNuance.DarkWashMax, rng.Next() ),
		KitNuance.At( KitNuance.DarkRingMin, KitNuance.DarkRingMax, rng.Next() ) );
}

public sealed partial class MusicGen
{
	/// <summary>A deterministic per-hit stick/round-robin stream. A LOCAL LFSR seeded on the hit's
	/// own sample position: two hits differ, the same hit is always the same, and the shared drum
	/// RNG stream — and therefore every other pattern in the song — is left byte-identical.</summary>
	static uint HitSeed( int start ) => (uint)start * 2654435761u | 1u;

	static float HitNext( ref uint s )
	{
		s ^= s << 13; s ^= s >> 17; s ^= s << 5;
		return (s & 0xffff) / 32768f - 1f;      // −1 .. 1
	}

	// ── Kick ──
	// The kit's three struck voices take a LEVEL, defaulting to 1 so the groove path reads
	// exactly as it did. It exists for the fill, which is a phrase and needs dynamics: an even
	// stream of equally-loud hits reads as a wall however few of them there are.
	void RenderKick( int start, Rng noise, float amp = 1f )
		=> RenderKick( start, noise, amp, KickTone.Default, 0f );

	/// <param name="pan">−1 … +1. A double pedal is two beaters on two sides of one drum, so the
	/// alternation is a POSITION, not a second kick sound. 0 is the single pedal.</param>
	internal void RenderKick( int start, Rng noise, float amp, in KickTone k, float pan )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );

		// Round-robin: no two strokes of a real pedal are the same, and a straight sixteenth run
		// is where identical ones stop reading as a drum. Pitch and level move together, the way
		// a harder stroke does.
		float jp = 1f, jl = 1f;
		if ( k.Jitter > 0f )
		{
			uint js = HitSeed( start );
			jp = 1f + k.Jitter * 0.09f * HitNext( ref js );
			jl = 1f + k.Jitter * 0.16f * HitNext( ref js );
		}

		float gL = 1f, gR = 1f;
		if ( pan != 0f ) StereoGains( pan, out gL, out gR );

		int dur = (int)(_sr * k.Dur);
		double decay = dur * k.DecayFrac;
		double subDecay = dur * k.SubDecayFrac;
		double phase = 0, subPhase = 0;
		// noise.Next() only fires inside the click below, so changing dur/decay here does NOT
		// shift the drum RNG stream (patterns are preserved).
		int clickLen = (int)(_sr * k.ClickSec);
		float ca = k.ClickCut > 0f ? LpCoeff( k.ClickCut ) : 0f;
		float clickLp = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			phase += (k.StartHz - k.DropHz * MathF.Min( 1f, t * k.DropRate )) * jp / _sr;
			subPhase += k.SubHz / _sr;
			float env = (float)Math.Exp( -i / decay );
			float subEnv = (float)Math.Exp( -i / subDecay );
			float body = (float)Math.Tanh( MathF.Sin( (float)(phase * 2 * Math.PI) ) * k.Drive ) * env;
			float sub = MathF.Sin( (float)(subPhase * 2 * Math.PI) ) * k.SubLevel * subEnv;
			float click = 0f;
			if ( i < clickLen )
			{
				float cn = noise.Next() * 2f - 1f;
				if ( k.ClickCut > 0f ) { clickLp += ca * (cn - clickLp); cn = clickLp; }
				click = cn * k.ClickLevel * (1f - i / (float)clickLen);
			}
			float v = (body + sub + click) * amp * jl * _c.KickVol * _c.KickBalance * _drumGain * _drumLowMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}

		// The beater coming back off the head. It is the SAME stroke, not a second note, so it
		// carries no click of its own and cannot recurse.
		if ( k.Beater > 0f )
			RenderKick( start + (int)(_sr * k.BeaterSec), noise, amp * k.Beater,
				k.With( clickLevel: 0f, beater: 0f, jitter: 0f ), pan );
	}

	// One-pole high-pass coefficient (unconditionally stable).
	float HpCoeff( float fc ) => (float)(1.0 / (1.0 + 2 * Math.PI * fc / _sr));

	// One-pole low-pass coefficient.
	float LpCoeff( float fc ) => (float)(1.0 - Math.Exp( -2 * Math.PI * fc / _sr ));

	// ── Snare ──
	void RenderSnare( int start, Rng noise, bool ghost, float level = 1f )
		=> RenderSnare( start, noise, level, ghost ? SnareTone.Ghost : SnareTone.Default );

	internal void RenderSnare( int start, Rng noise, float level, in SnareTone s )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// dur and the single noise.Next()/sample are kept exactly so the drum RNG stream
		// is unchanged — only the timbre is a parameter.
		int dur = (int)(_sr * s.Dur);
		double decay = dur * s.DecayFrac;
		double phase = 0, phase2 = 0;
		float amp2 = level * _c.SnareVol * _c.SnareBalance * s.Level * _drumGain;
		float a = HpCoeff( s.WireCut );
		var crack = s.CrackLevel > 0f ? new BandPass( s.CrackHz, s.CrackQ, _sr ) : default;
		double crackDecay = dur * s.CrackDecayFrac;
		double thudPhase = 0;
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			float env = (float)Math.Exp( -i / decay );
			float drop = 1f - s.Sag * t;       // shell pitch sags a touch → "dow"
			phase += s.Hz1 * drop / _sr;
			phase2 += s.Hz2 * drop / _sr;
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float body = (MathF.Sin( (float)(phase * 2 * Math.PI) )
				+ MathF.Sin( (float)(phase2 * 2 * Math.PI) ) * s.Body2) * s.BodyLevel;
			float v = ((float)Math.Tanh( hp * s.WireDrive ) * s.Wire + body) * env;
			if ( s.CrackLevel > 0f )
				v += crack.Next( n ) * s.CrackLevel * (float)Math.Exp( -i / crackDecay );
			if ( s.ThudLevel > 0f )
			{
				thudPhase += s.ThudHz / _sr;
				v += MathF.Sin( (float)(thudPhase * 2 * Math.PI) ) * s.ThudLevel
					* (float)Math.Exp( -i / (dur * 0.09) );
			}
			v = v * amp2;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	internal void RenderSnare( int start, Rng noise, SnareHit hit, float amp = 1f )
		=> RenderSnare( start, noise, amp, SnareTone.For( hit ) );

	/// <summary>A flam: the grace note is a hand that arrives early and quieter, and the pair is
	/// heard as ONE thickened stroke rather than as two notes. The spacing is in MILLISECONDS —
	/// it is a physical property of two sticks, not a subdivision, so it must not scale with
	/// tempo (see the strum-spread note in CLAUDE.md).</summary>
	internal void RenderSnareFlam( int start, Rng noise, float amp = 1f, float graceMs = 24f,
		float graceLevel = 0.55f )
	{
		RenderSnare( start - (int)(_sr * graceMs * 0.001f), noise, amp * graceLevel, SnareTone.Default );
		RenderSnare( start, noise, amp, SnareTone.Default );
	}

	/// <summary>A buzz/press roll across a span: the stick is leaned into the head and the
	/// bounces run together. Many quiet, closely-spaced strokes, so it is a texture with a
	/// crescendo rather than a rhythm.</summary>
	internal void RenderSnareBuzz( int start, int spanSamples, Rng noise, float fromAmp = 0.18f,
		float toAmp = 0.85f, float spacingMs = 26f )
	{
		int step = Math.Max( 1, (int)(_sr * spacingMs * 0.001f) );
		var tone = SnareTone.Default.With( dur: 0.05f, decayFrac: 0.26, wire: 0.85f );
		for ( int i = 0, at = 0; at < spanSamples; i++, at += step )
		{
			float u = spanSamples <= step ? 1f : at / (float)spanSamples;
			uint s = HitSeed( start + at );
			float wobble = 1f + 0.18f * HitNext( ref s );
			RenderSnare( start + at, noise, (fromAmp + (toAmp - fromAmp) * u) * wobble, tone );
		}
	}

	// ── Toms ──

	/// <summary>A tom of the kit, by INDEX. 0 is the rack, 2 the floor; where it sits in the
	/// field is a property of which drum it is, so no fill can drive it off the end of a range.
	/// </summary>
	internal void RenderTom( int start, in TomKit kit, int index, Rng noise, float amp, in TomTone tone )
	{
		if ( _drumGain <= 0f ) return;
		StereoGains( -_drumPan * kit.Pan( index ), out float gL, out float gR );
		RenderTomAt( start, kit.Hz( index ), gL, gR, amp, tone );
	}

	void RenderTomAt( int start, float baseFreq, float gL, float gR, float amp, in TomTone k )
	{
		start = Math.Max( 0, start + _time.DrumPush );
		int dur = (int)(_sr * k.Dur);
		double decay = dur * k.DecayFrac;
		double attackDecay = dur * k.SnapDecayFrac;   // fast-decaying upper partial → beater "snap"
		double phase = 0, phase2 = 0;
		uint ns = HitSeed( start );
		int clickLen = (int)(_sr * k.ClickSec);
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			float pf = baseFreq * (1f - k.Sag * t);    // pitch sag: how far the head lets go
			phase += pf / _sr;
			phase2 += pf * k.SnapMul / _sr;            // inharmonic upper partial for attack snap
			float env = (float)Math.Exp( -i / decay );
			float aenv = (float)Math.Exp( -i / attackDecay );
			float body = MathF.Sin( (float)(phase * 2 * Math.PI) ) * env;
			float snap = MathF.Sin( (float)(phase2 * 2 * Math.PI) ) * aenv * k.SnapLevel;
			float click = 0f;
			if ( i < clickLen )
				click = HitNext( ref ns ) * k.ClickLevel * (1f - i / (float)clickLen);
			float v = (body + snap + click) * amp * _c.TomVol * _c.TomBalance * _drumGain * _drumLowMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	// ── Hats ──

	/// <summary>The hi-hat. OPENNESS IS A CONTINUUM — a pedal is a distance, not a switch — and
	/// <paramref name="chokeAt"/> is where the foot closes it again. An open hat with nothing
	/// choking it rings through whatever comes next, which is why pop's open-on-every-offbeat
	/// smears: the tail is longer than the gap. The open→closed pair is the gesture.</summary>
	/// <param name="chokeAt">Absolute sample position the foot closes at, or int.MaxValue.</param>
	internal void RenderHat( int start, float openness, float amp, Rng noise, in HatTone h,
		int chokeAt = int.MaxValue )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// Closed/open hats live on the left of the kit (the hi-hat stand); ride sits opposite.
		StereoGains( -_drumPan, out float gL, out float gR );
		openness = Math.Clamp( openness, 0f, 1f );
		// THE MAP IS GEOMETRIC. A closed hat and an open one are a factor of seventeen apart in
		// length, and the ear reads that as a RATIO rather than as a difference: interpolated
		// linearly, most of the pedal's travel lands within a few percent of fully open, and two
		// positions a third of the range apart are the same hat. Stepping by a constant ratio puts
		// an even amount of audible change under every part of the travel, which is what a foot
		// lifting steadily has to sound like.
		float u = h.OpenCurve == 1f || openness <= 0f || openness >= 1f
			? openness : MathF.Pow( openness, h.OpenCurve );
		float durSec = openness <= 0f ? h.ClosedDur
			: openness >= 1f ? h.OpenDur
			: h.ClosedDur * MathF.Pow( h.OpenDur / h.ClosedDur, u );
		float cut = openness <= 0f ? h.ClosedCut
			: openness >= 1f ? h.OpenCut
			: h.ClosedCut * MathF.Pow( h.OpenCut / h.ClosedCut, u );
		int dur = (int)(_sr * durSec);
		double decay = dur * h.DecayFrac;
		float a = HpCoeff( cut );
		// The choke itself: the cymbals meeting is a fast release, not a cut — a hard stop
		// clicks, and a drummer's foot is not instantaneous either.
		float chokeStep = (float)Math.Exp( -1.0 / Math.Max( 1.0, _sr * 0.012 ) );
		float chokeEnv = 1f;
		double lowPhase = 0, sizzlePhase = 0;
		int attack = (int)(_sr * h.AttackSec);
		// Loose cymbals rattle most when they are half touching, and not at all when open or shut.
		float sizzle = h.SizzleDepth * 4f * u * (1f - u);
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			if ( attack > 0 && i < attack ) env *= i / (float)attack;
			if ( sizzle > 0f )
			{
				sizzlePhase += h.SizzleHz / _sr;
				env *= 1f - sizzle * 0.5f * (1f + MathF.Sin( (float)(sizzlePhase * 2 * Math.PI) ));
			}
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env;
			if ( h.LowThud > 0f )
			{
				lowPhase += 96f / _sr;
				v += MathF.Sin( (float)(lowPhase * 2 * Math.PI) ) * h.LowThud
					* (float)Math.Exp( -i / (dur * 0.12) );
			}
			if ( start + i >= chokeAt ) chokeEnv *= chokeStep;
			// Left to right, and the two new factors last: at their neutral 1f they are exact
			// identities, so the groove path's arithmetic is unchanged to the bit.
			v = v * amp * _c.HatBalance * _drumGain * _drumHighMul * chokeEnv * h.Level;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	internal void RenderHat( int start, HatHit hit, float amp, Rng noise )
		=> RenderHat( start, hit == HatHit.Splash ? 1f : 0f, amp, noise, HatTone.For( hit ) );

	// ── Cymbals ──
	// The ride, its bell, and the two crashes: one voice, four sets of constants (CymbalBands).
	// Rendered PER HIT, like every other voice in this kit — seven filtered-noise bands and two
	// partials is cheap enough that a riding section can afford it, which is the whole reason the
	// distilled version exists.

	/// <summary>A hand on the metal: the cymbal stops. Fast, but not a cut — a hard stop clicks,
	/// and a hand is not instantaneous either.</summary>
	internal const float HandChoke = 0.020f;

	/// <param name="chokeAt">Absolute sample position something lands on the cymbal, or
	/// int.MaxValue.</param>
	/// <param name="chokeTau">How fast it takes the ring away. <see cref="HandChoke"/> is a hand
	/// and the cymbal is gone; <see cref="CymbalBands.RestrikeTau"/> is the STICK LANDING AGAIN,
	/// which is the same event seen from the other end and is not a smaller choke — it is a
	/// shorter decay for as long as the cymbal is being played, so it compounds over a stroke
	/// train the way the physics does.</param>
	/// <summary>The ride and its bell, on the right of the kit opposite the hats.</summary>
	void RenderRideCym( int at, float amp, float[][] t, int chokeAt = int.MaxValue,
		float chokeTau = HandChoke )
		=> RenderCymbal( at, amp, t, _c.RideBalance, _drumPan, chokeAt, chokeTau );

	/// <summary>A crash. The kit's two crashes are panned apart and which side each is on is the
	/// song's own draw, so a ridden crash and the one accenting over it land opposite each other
	/// for free.</summary>
	void RenderCrashCym( int at, float amp, float[][] t, bool dark,
		int chokeAt = int.MaxValue, float chokeTau = HandChoke )
		=> RenderCymbal( at, amp, t, _c.CrashBalance,
			dark == _crashBrightLeft ? _drumPan : -_drumPan, chokeAt, chokeTau );

	/// <summary>
	/// SYNTHESISED ONCE PER SONG, THEN STAMPED. The distillation fixed what the cymbal IS; it does
	/// not fix what a ride COSTS, because that is a property of the pattern: a 2.5-second ring
	/// struck eight times a bar overlaps itself twenty deep, and rendering each stroke in full pays
	/// for all of it. Synthesising the object once and adding it per hit is what a sampler does,
	/// and it is honest here for the same reason the bands are — a cymbal is one physical object
	/// and every strike is that object.
	///
	/// It is two tables, split at 2.5 kHz, because a soft stroke is DARKER and not merely quieter.
	/// Variants are round robins: they cost about three milliseconds each now, where the mode
	/// forest's cost a quarter of a second, so the repeat-tell is cheap to break.
	/// </summary>
	internal float[][] BuildCymbal( in CymbalBands c, int variant )
	{
		int dur = (int)(_sr * c.Dur);
		var lo = new float[dur]; var hi = new float[dur];
		SynthCymbal( c, lo, hi, (uint)(variant * 2654435761u) | 1u );
		return new[] { lo, hi };
	}

	/// <param name="chokeAt">Absolute sample position something lands on the cymbal.</param>
	/// <param name="chokeTau">How fast it takes the ring away — <see cref="HandChoke"/> for a hand,
	/// <see cref="CymbalBands.RestrikeTau"/> for the stick landing again.</param>
	internal void RenderCymbal( int start, float amp, float[][] t, float balance, float pan,
		int chokeAt = int.MaxValue, float chokeTau = HandChoke )
	{
		if ( _drumGain <= 0f || amp <= 0f || t == null ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		int end = Math.Min( _bufL.Length, start + t[0].Length );
		if ( end <= start ) return;
		StereoGains( pan, out float gL, out float gR );
		uint js = HitSeed( start );
		float jit = 1f + 0.07f * HitNext( ref js );
		float bus = balance * _drumGain * _drumHighMul * jit;
		float loG = amp * bus;
		// A soft stroke is darker, not merely quieter — a stick that does not dig in leaves the top
		// of the cymbal alone.
		float hiG = amp * MathF.Pow( Math.Clamp( amp, 0.05f, 1f ), 0.35f ) * bus;
		float chokeStep = (float)Math.Exp( -1.0 / Math.Max( 1.0, _sr * (double)chokeTau ) );
		float chokeEnv = 1f;
		for ( int i = 0; start + i < end; i++ )
		{
			if ( start + i >= chokeAt )
			{
				chokeEnv *= chokeStep;
				if ( chokeEnv < 1e-4f ) break;
			}
			float v = (t[0][i] * loG + t[1][i] * hiG) * chokeEnv;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	/// <summary>The voice itself: seven filtered-noise bands with their own decays, the low pair,
	/// splash and wash. Written into a lo/hi pair so a stroke's brightness can vary at stamp time.
	/// </summary>
	void SynthCymbal( in CymbalBands c, float[] outLo, float[] outHi, uint seed )
	{
		int dur = outLo.Length;

		int nb = c.Hz.Length;
		var bp = new BandPass[nb];
		var env = new float[nb]; var dec = new float[nb];
		var dies = new int[nb];
		for ( int b = 0; b < nb; b++ )
		{
			bp[b] = new BandPass( c.Hz[b], CymbalBandQ, _sr );
			env[b] = 1f;
			dec[b] = (float)Math.Exp( -1.0 / (_sr * (double)c.Tau[b]) );
			// Each band stops when it stops being worth its multiplies; they differ by a factor of
			// five in ring time, so most are gone long before the table is.
			dies[b] = Math.Min( dur, (int)(_sr * c.Tau[b] * 5.0f) + 1 );
		}

		uint ns = seed;

		int stickLen = (int)(_sr * 0.004f);
		float sa = c.StickCut > 0f ? LpCoeff( c.StickCut ) : 0f;
		float lp = 0f;
		float hpA = HpCoeff( c.NoiseHp ), washLpA = LpCoeff( c.WashLp );
		float splHpA = HpCoeff( c.SplashHp );
		float nInPrev = 0f, nHpPrev = 0f, washLp = 0f, sInPrev = 0f, sHpPrev = 0f;
		double splDecay = _sr * (double)Math.Max( 0.005f, c.SplashTau );
		double washDecay = _sr * (double)Math.Max( 0.02f, c.WashTau );
		// The fade keeps the truncation silent: the longest band still has tail left at the end.
		int fade = (int)(_sr * 0.10f);
		for ( int i = 0; i < dur; i++ )
		{
			float n = noiseNext( ref ns );
			float lo = 0f, hi = 0f;
			for ( int b = 0; b < nb; b++ )
			{
				if ( i >= dies[b] ) continue;
				float v = bp[b].Next( n ) * c.Amp[b] * env[b];
				env[b] *= dec[b];
				if ( c.Hz[b] >= BandSplit ) hi += v; else lo += v;
			}
			// The splash (broadband, and on a crash it keeps going for a third of a second) and the
			// wash (the air, darker and long) — the layers the mode forest never carried anyway.
			float hp = hpA * (nHpPrev + n - nInPrev); nInPrev = n; nHpPrev = hp;
			washLp += washLpA * (hp - washLp);
			// The splash rides in the high table and the wash in the low, so each tilts with the
			// layer it belongs to.
			float shp = splHpA * (sHpPrev + n - sInPrev); sInPrev = n; sHpPrev = shp;
			hi += shp * c.SplashLvl * (float)Math.Exp( -i / splDecay );
			lo += washLp * c.WashLvl * (float)Math.Exp( -i / washDecay );
			if ( c.Stick > 0f && i < stickLen )
			{
				float sn = HitNext( ref ns );
				if ( c.StickCut > 0f ) { lp += sa * (sn - lp); sn = lp; }
				hi += sn * c.Stick * (1f - i / (float)stickLen);
			}
			int rem = dur - i;
			float k = c.Level * (rem < fade ? rem / (float)fade : 1f);
			outLo[i] = lo * k; outHi[i] = hi * k;
		}
	}

	/// <summary>Where the tilt splits the cymbal: above this is stick and shimmer, below it is the
	/// body.</summary>
	const float BandSplit = 2500f;

	const float CymbalBandQ = 0.7f;

	/// <summary>The cymbal's noise source. Its own per-hit stream, so no two strokes are the same
	/// waveform and the shared drum RNG is untouched — the thing a rendered-once table had to buy
	/// back with round robins, and gets here for free.</summary>
	static float noiseNext( ref uint s ) => HitNext( ref s );
}
