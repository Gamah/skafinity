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
	float _f;
	readonly float _q;

	public BandPass( float fc, float q, int sr )
	{
		_low = 0f; _band = 0f;
		_f = Coeff( fc, sr );
		// The floor admits a mode whose natural ring is seconds long: a resonator's decay IS its
		// bandwidth (τ ≈ 1/(π·q·fc)), so a floor of 0.004 capped a 370 Hz mode's ring at ~0.2 s
		// and no envelope can put back what the filter has already damped. q → 0 is a marginally
		// stable oscillator, not an unstable one, so the only cost of a tiny q is a quiet output —
		// which the Q-normalisation below makes explicit and the caller's level compensates.
		_q = Math.Clamp( q, 0.0002f, 2f );
	}

	/// <summary>The frequency coefficient, exposed so a caller can retune a running filter
	/// (see Retune). Clamped well under Nyquist: the Chamberlin form goes unstable as f
	/// approaches 2.</summary>
	public static float Coeff( float fc, int sr )
		=> (float)(2 * Math.Sin( Math.PI * Math.Min( fc, sr * 0.15f ) / sr ));

	/// <summary>Move the centre frequency without resetting the state — struck metal's modes
	/// fall slightly as the strike energy dissipates, and that glide is a property of the
	/// ringing, so it has to happen to the resonance mid-ring rather than between hits.</summary>
	public void Retune( float coeff ) => _f = coeff;

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
	/// tell as a kick that never varies.</summary>
	public const float OpenHatDurMin = 0.45f, OpenHatDurMax = 0.75f;
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

readonly struct CrashTone
{
	public readonly float Dur;
	public readonly double DecayFrac;
	public readonly float Cut;
	public readonly float Level;

	public CrashTone( float dur, double decayFrac, float cut, float level )
	{
		Dur = dur; DecayFrac = decayFrac; Cut = cut; Level = level;
	}

	public CrashTone With( float dur = -1f, double decayFrac = -1.0, float cut = -1f, float level = -1f )
		=> new( dur < 0 ? Dur : dur, decayFrac < 0 ? DecayFrac : decayFrac,
			cut < 0 ? Cut : cut, level < 0 ? Level : level );

	public static readonly CrashTone Bright = new( dur: 0.6f, decayFrac: 0.45, cut: 4000f, level: 1f );
	public static readonly CrashTone Dark = new( dur: 0.9f, decayFrac: 0.5, cut: 2600f, level: 0.85f );

	public static CrashTone For( bool dark ) => dark ? Dark : Bright;
}

/// <summary>Where on the cymbal, and with what. A ride is one piece of metal that makes several
/// completely different sounds depending on where it is struck; today it makes one.</summary>
enum RideArt
{
	/// <summary>Tip on the bow: the ping. Defined attack, short wash — the articulation a ride
	/// pattern is actually made of.</summary>
	Ping,
	/// <summary>Shoulder on the bow: the wash. No stick definition, much more sustain.</summary>
	Wash,
	/// <summary>The edge — the crashiest thing a ride does short of being crashed.</summary>
	Edge,
	/// <summary>Crash-riding: an eighth pattern played on a crash, which is a technique rather
	/// than a cymbal.</summary>
	CrashRide,
	/// <summary>The bell.</summary>
	Bell,
}

readonly struct RideTone
{
	public readonly float Dur;
	public readonly double DecayFrac;
	public readonly float Cut;
	public readonly float Level;        // level of the noise wash
	public readonly float Stick;        // stick definition on the attack, 0 = none
	public readonly float StickCut;     // and its low-pass, so it is a stick and not a hiss

	/// <summary>THE RESONANCES. A cymbal's bell rings — that is the whole difference between a
	/// bell and a hat — and it rings at several inharmonic frequencies at once. Band-passed noise
	/// at a high Q gives exactly that: a pitch CENTRE with no pitch, which is what the deleted
	/// sine-partial version could never be. What made the earlier attempt read as a hat in a
	/// strange state was the Q, not the idea: a lightly-resonated noise band is a filtered hat.
	/// null = no resonances, which is the bow.</summary>
	public readonly float[] BandHz;
	public readonly float[] BandLevel;
	public readonly float BandQ;
	/// <summary>The resonances outlive the strike — a bell rings on long after the wash has
	/// gone, and that ratio is most of what says "bell".</summary>
	public readonly double BandDecayFrac;
	/// <summary>PER-MODE Q — and therefore PER-MODE DECAY, because a resonator's ring time is
	/// its bandwidth (τ ≈ 1/(π·q·fc)). On a struck bell the low modes ring for seconds and the
	/// high ones are gone in tens of milliseconds, and that divergence over time is a large part
	/// of what identifies the sound; a stack under one decay is a chord, not a bell. null keeps
	/// the shared BandQ with the index taper, which is every pre-gen-4 tone.</summary>
	public readonly float[] BandQs;
	/// <summary>Strike glide: the modes start this far SHARP and settle onto BandHz as the
	/// strike energy dissipates, over roughly GlideMs. Struck metal does this in its first tens
	/// of milliseconds; 0 = none.</summary>
	public readonly float GlideCents;
	public readonly float GlideMs;

	public RideTone( float dur, double decayFrac, float cut, float level, float stick,
		float stickCut = 0f, float[] bandHz = null, float[] bandLevel = null, float bandQ = 0.03f,
		double bandDecayFrac = 1.0, float[] bandQs = null, float glideCents = 0f,
		float glideMs = 25f )
	{
		Dur = dur; DecayFrac = decayFrac; Cut = cut; Level = level; Stick = stick;
		StickCut = stickCut; BandHz = bandHz; BandLevel = bandLevel; BandQ = bandQ;
		BandDecayFrac = bandDecayFrac; BandQs = bandQs; GlideCents = glideCents;
		GlideMs = glideMs;
	}

	public RideTone With( float dur = -1f, double decayFrac = -1.0, float cut = -1f,
		float level = -1f, float stick = -1f, float bandQ = -1f, double bandDecayFrac = -1.0,
		float[] bandHz = null, float[] bandLevel = null, float[] bandQs = null,
		float glideCents = -1f, float glideMs = -1f )
		=> new( dur < 0 ? Dur : dur, decayFrac < 0 ? DecayFrac : decayFrac, cut < 0 ? Cut : cut,
			level < 0 ? Level : level, stick < 0 ? Stick : stick, StickCut,
			bandHz ?? BandHz, bandLevel ?? BandLevel,
			bandQ < 0 ? BandQ : bandQ, bandDecayFrac < 0 ? BandDecayFrac : bandDecayFrac,
			bandQs ?? BandQs, glideCents < 0 ? GlideCents : glideCents,
			glideMs < 0 ? GlideMs : glideMs );

	/// <summary>The bow hit the groove path has always played.</summary>
	public static readonly RideTone Bow = new(
		dur: 0.22f, decayFrac: 0.42, cut: 7000f, level: 0.7f, stick: 0f );

	/// <summary>Bell candidate C, and the incumbent: the bow hit rung a little longer. No timbre
	/// difference at all — the honest statement of what the engine plays today.</summary>
	public static readonly RideTone BellDurationOnly = new(
		dur: 0.34f, decayFrac: 0.42, cut: 7000f, level: 0.7f, stick: 0f );

	// ── The bow articulations ──
	// A PING IS A DEFINED STRIKE. High-passed noise has no definition anywhere in it, so it can
	// only ever be a hat played on the wrong side of the kit: the stick lands, and what should be
	// a point of contact is a broadband hiss. The ping gets a short, tight resonance around where
	// a ride's stick actually speaks, and a LOW-PASSED stick transient rather than white noise.

	public static readonly RideTone Ping = new(
		dur: 0.34f, decayFrac: 0.34, cut: 6000f, level: 0.34f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 3150f, 5400f }, bandLevel: new[] { 1.7f, 0.8f },
		bandQ: 0.055f, bandDecayFrac: 0.55 );

	// A RIDE HAS A BOTTOM. The stick's contact is the top two modes, but the cymbal it is
	// attached to is a large piece of metal whose lowest modes are down in the hundreds of hertz,
	// and they are what makes a ride sound like a ride rather than like a small bright cymbal.
	// The ping above has definition and no body; these give it one.

	public static readonly RideTone PingLowA = new(
		dur: 0.55f, decayFrac: 0.30, cut: 6000f, level: 0.30f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 380f, 3150f, 5400f }, bandLevel: new[] { 2.6f, 1.7f, 0.8f },
		bandQ: 0.040f, bandDecayFrac: 0.85 );

	public static readonly RideTone PingLowB = new(
		dur: 0.55f, decayFrac: 0.30, cut: 6000f, level: 0.30f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 560f, 3150f, 5400f }, bandLevel: new[] { 2.6f, 1.7f, 0.8f },
		bandQ: 0.040f, bandDecayFrac: 0.85 );

	public static readonly RideTone PingLowTwo = new(
		dur: 0.60f, decayFrac: 0.32, cut: 6000f, level: 0.28f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 370f, 615f, 3150f, 5400f },
		bandLevel: new[] { 2.2f, 1.5f, 1.7f, 0.8f },
		bandQ: 0.038f, bandDecayFrac: 0.90 );

	public static readonly RideTone Wash = new(
		dur: 0.46f, decayFrac: 0.55, cut: 5400f, level: 0.72f, stick: 0f );

	public static readonly RideTone Edge = new(
		dur: 0.60f, decayFrac: 0.50, cut: 3300f, level: 0.85f, stick: 0.12f, stickCut: 4000f );

	public static readonly RideTone CrashRideBow = new(
		dur: 0.72f, decayFrac: 0.50, cut: 3900f, level: 0.80f, stick: 0.18f, stickCut: 4500f );

	// ── The bell candidates ──
	// All three are noise through high-Q resonances. Bell A is a real ride bell's cluster — an
	// inharmonic stack that rings a long time under a quiet wash. Bell B is the same idea lower
	// and heavier (a bigger bell). Bell C keeps a wash you can hear, for the case where a fully
	// ringing bell reads as a different instrument rather than as part of this kit.

	public static readonly RideTone BellNarrow = new(
		dur: 1.05f, decayFrac: 0.30, cut: 8000f, level: 0.16f, stick: 0.50f, stickCut: 6000f,
		bandHz: new[] { 1180f, 2360f, 3510f, 5290f },
		bandLevel: new[] { 5.8f, 9.0f, 5.4f, 2.5f },
		bandQ: 0.012f, bandDecayFrac: 0.95 );

	public static readonly RideTone BellWide = new(
		dur: 1.25f, decayFrac: 0.32, cut: 7000f, level: 0.14f, stick: 0.44f, stickCut: 5000f,
		bandHz: new[] { 830f, 1690f, 2570f, 4120f },
		bandLevel: new[] { 6.5f, 8.6f, 5.0f, 2.3f },
		bandQ: 0.009f, bandDecayFrac: 1.05 );

	public static readonly RideTone BellWashy = new(
		dur: 0.85f, decayFrac: 0.36, cut: 7200f, level: 0.34f, stick: 0.42f, stickCut: 6000f,
		bandHz: new[] { 1180f, 2360f, 3510f },
		bandLevel: new[] { 4.3f, 6.5f, 3.6f },
		bandQ: 0.030f, bandDecayFrac: 0.80 );

	// ── Generation 4: the ring, treated as struck metal ──
	// The lead came from the ping, not from the bell attempts: PingLowTwo ALLOWED TO RING was the
	// first thing on this branch heard as bell-adjacent (RIDE.md). These start there and add the
	// three properties of struck metal that a static filter bank cannot have:
	//  - per-mode decay: each mode's own Q sets its own ring time, so the lows ring on for the
	//    better part of a second while the stick modes are gone in tens of milliseconds — a stack
	//    under one decay is a chord, not a bell;
	//  - close-mode beating: the low modes are PAIRS a few Hz apart, because a real bell's
	//    partials come in near-degenerate pairs and the warble between them is diagnostic;
	//  - a strike glide: the stack starts slightly sharp and settles as the strike energy
	//    dissipates.
	// Still noise-excited resonators, not sine partials: each hit's mode phases and amplitudes
	// come off the strike noise, so no two bells ring identically — that per-hit variation is the
	// argument for why this is not generation 1 wearing envelopes.

	/// <summary>The q whose natural ring is `ringSec` (τ ≈ 1/(π·q·fc)) — a mode's decay IS its
	/// bandwidth, which is why per-mode decay is per-mode Q rather than an envelope bank.</summary>
	public static float ModeQ( float fc, float ringSec ) => 1f / (MathF.PI * fc * ringSec);

	/// <summary>A long ring is a narrow band, and a narrow band catches almost none of a 3.5 ms
	/// strike — so a mode's level must scale with its ring time for its onset to survive the
	/// physics. `onset` is the mode's strike amplitude in the stack, decay-independent.</summary>
	public static float ModeLevel( float onset, float ringSec ) => onset * MathF.PI * ringSec;

	public static readonly RideTone BellRing = new(
		dur: 1.15f, decayFrac: 0.25, cut: 6000f, level: 0.18f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 367f, 373f, 611f, 618f, 3150f, 5400f },
		bandLevel: new[] { ModeLevel( 15f, 0.80f ), ModeLevel( 15f, 0.80f ),
			ModeLevel( 10f, 0.60f ), ModeLevel( 10f, 0.60f ),
			ModeLevel( 55f, 0.10f ), ModeLevel( 35f, 0.045f ) },
		bandQs: new[] { ModeQ( 367f, 0.80f ), ModeQ( 373f, 0.80f ),
			ModeQ( 611f, 0.60f ), ModeQ( 618f, 0.60f ),
			ModeQ( 3150f, 0.10f ), ModeQ( 5400f, 0.045f ) },
		glideCents: 20f, glideMs: 25f );

	/// <summary>The same bell lower — mode set scaled by 0.78, lows ringing longer, for the case
	/// where BellRing reads as too small a cymbal.</summary>
	public static readonly RideTone BellRingLow = new(
		dur: 1.30f, decayFrac: 0.25, cut: 5000f, level: 0.18f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 286f, 291f, 477f, 482f, 2460f, 4210f },
		bandLevel: new[] { ModeLevel( 15f, 1.00f ), ModeLevel( 15f, 1.00f ),
			ModeLevel( 10f, 0.75f ), ModeLevel( 10f, 0.75f ),
			ModeLevel( 55f, 0.12f ), ModeLevel( 35f, 0.055f ) },
		bandQs: new[] { ModeQ( 286f, 1.00f ), ModeQ( 291f, 1.00f ),
			ModeQ( 477f, 0.75f ), ModeQ( 482f, 0.75f ),
			ModeQ( 2460f, 0.12f ), ModeQ( 4210f, 0.055f ) },
		glideCents: 20f, glideMs: 30f );

	/// <summary>And higher — scaled by 1.3, shorter lows, for the case where the bell should sit
	/// clear above the bow rather than inside its register.</summary>
	public static readonly RideTone BellRingHigh = new(
		dur: 1.00f, decayFrac: 0.25, cut: 6000f, level: 0.18f, stick: 0.55f, stickCut: 5500f,
		bandHz: new[] { 477f, 485f, 794f, 803f, 4095f, 6600f },
		bandLevel: new[] { ModeLevel( 15f, 0.60f ), ModeLevel( 15f, 0.60f ),
			ModeLevel( 10f, 0.45f ), ModeLevel( 10f, 0.45f ),
			ModeLevel( 55f, 0.08f ), ModeLevel( 35f, 0.040f ) },
		bandQs: new[] { ModeQ( 477f, 0.60f ), ModeQ( 485f, 0.60f ),
			ModeQ( 794f, 0.45f ), ModeQ( 803f, 0.45f ),
			ModeQ( 4095f, 0.08f ), ModeQ( 6600f, 0.040f ) },
		glideCents: 20f, glideMs: 22f );

	public static RideTone For( RideArt a ) => a switch
	{
		RideArt.Wash => Wash,
		RideArt.Edge => Edge,
		RideArt.CrashRide => CrashRideBow,
		RideArt.Bell => BellNarrow,
		_ => Ping,
	};
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
	// The legacy pan-by-pitch entry point, kept byte-identical while the grooves still call it.
	// It maps a FREQUENCY onto a hardcoded 145–260 Hz range, so a fill reaching below 145 Hz
	// clamps hard right and its lowest drums share one position — which is what the indexed
	// overload below exists to make unwriteable. This one goes when the grooves are rewired.
	void RenderTom( int start, float baseFreq, Rng noise, float amp = 1f )
	{
		if ( _drumGain <= 0f ) return;
		float u = Math.Clamp( (baseFreq - 145f) / (260f - 145f), 0f, 1f ); // 0 = low/floor, 1 = high/rack
		StereoGains( -_drumPan * (u * 2f - 1f), out float gL, out float gR );
		RenderTomAt( start, baseFreq, gL, gR, amp, TomTone.Default );
	}

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
	void RenderHat( int start, bool open, float amp, Rng noise )
		=> RenderHat( start, open ? 1f : 0f, amp, noise, HatTone.Default );

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

	// ── Crash ──
	// Two crash colours off one voice: the bright crash (short-ish, high-passed high) and a
	// dark crash — lower cutoff, longer wash, a touch quieter — for a bigger china/ride-crash.
	// The kit has two crashes panned apart (±25%); the bright/dark colour picks which physical
	// cymbal, and _crashBrightLeft (per song) decides which side that is.
	void RenderCrash( int start, Rng noise, bool dark = false )
		=> RenderCrash( start, noise, dark, 1f, CrashTone.For( dark ) );

	/// <summary>A crash at a LEVEL. It is the one kit voice that had no gain parameter at all, so
	/// every crash in every song landed at full level whatever the energy, the velocity or the
	/// genre's accent said.</summary>
	internal void RenderCrash( int start, Rng noise, bool dark, float amp, in CrashTone k,
		int chokeAt = int.MaxValue )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		StereoGains( (dark == _crashBrightLeft ? _drumPan : -_drumPan), out float gL, out float gR );
		int dur = (int)(_sr * k.Dur);
		double decay = dur * k.DecayFrac;
		float a = HpCoeff( k.Cut );
		float lvl = amp * _c.CrashVol * _c.CrashBalance * k.Level;
		float chokeStep = (float)Math.Exp( -1.0 / Math.Max( 1.0, _sr * 0.020 ) );
		float chokeEnv = 1f;
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			if ( start + i >= chokeAt ) chokeEnv *= chokeStep;
			float v = hp * env * chokeEnv * lvl * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	// ── Ride ──
	// A cymbal is mostly filtered noise, and this voice renders only that body. Earlier versions
	// layered inharmonic SINE partials for a metallic ping/bell, and any tonal layer — however
	// quiet, however detuned — read as a pitched ring; that is gone and is not coming back. The
	// bell candidates below reach for a CENTRE without a pitch by band-passing the noise instead.
	void RenderRide( int start, bool bell, float amp, Rng noise )
		=> RenderRide( start, amp, noise, bell ? RideTone.BellDurationOnly : RideTone.Bow );

	internal void RenderRide( int start, float amp, Rng noise, in RideTone k,
		int chokeAt = int.MaxValue )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// Ride sits opposite the hats, on the right of the kit.
		StereoGains( _drumPan, out float gL, out float gR );
		int dur = (int)(_sr * k.Dur);
		double decay = dur * k.DecayFrac;
		float a = HpCoeff( k.Cut );
		int nb = k.BandHz == null ? 0 : k.BandHz.Length;
		var bp = nb > 0 ? new BandPass[nb] : null;
		// The Q rises with the partial: the higher modes of a struck cymbal ring tighter, and a
		// flat Q across the stack is what turns a bell back into a filtered wash. A gen-4 tone
		// gives every mode its OWN q instead, because a mode's q is its decay (see ModeQ).
		for ( int b = 0; b < nb; b++ )
			bp[b] = new BandPass( k.BandHz[b],
				k.BandQs != null ? k.BandQs[b] : k.BandQ / (1f + 0.35f * b), _sr );
		// The strike glide: the stack starts GlideCents sharp and settles onto BandHz over
		// GlideMs. Retuning lerps the frequency COEFFICIENT, which over a few tens of cents is
		// the frequency to well under a cent.
		float[] glideTo = null, glideFrom = null;
		double glideDecay = 0;
		if ( nb > 0 && k.GlideCents > 0f )
		{
			glideTo = new float[nb]; glideFrom = new float[nb];
			float up = (float)Math.Pow( 2.0, k.GlideCents / 1200.0 );
			for ( int b = 0; b < nb; b++ )
			{
				glideTo[b] = BandPass.Coeff( k.BandHz[b], _sr );
				glideFrom[b] = BandPass.Coeff( k.BandHz[b] * up, _sr );
			}
			glideDecay = _sr * Math.Max( 1f, k.GlideMs ) * 0.001;
		}
		// The wash is the strike; the resonances outlive it. That ratio is most of "bell".
		double bandDecay = dur * k.BandDecayFrac;
		double strikeDecay = _sr * 0.0035;   // how long the stick is in contact
		int stickLen = (int)(_sr * 0.006f);
		float sa = k.StickCut > 0f ? LpCoeff( k.StickCut ) : 0f;
		float stickLp = 0f;
		uint ns = HitSeed( start );
		float chokeStep = (float)Math.Exp( -1.0 / Math.Max( 1.0, _sr * 0.018 ) );
		float chokeEnv = 1f;
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * k.Level * env;
			if ( nb > 0 )
			{
				// A BELL IS STRUCK, so the modes are excited by the STRIKE and then ring on their
				// own. Feeding them the whole noise stream is a filter sitting on a wash — which
				// is a hat in a strange state, and is what these sounded like — and it is also why
				// they were so quiet: a tight resonance passes almost none of a broadband input.
				float ex = n * (float)Math.Exp( -i / strikeDecay );
				float benv = (float)Math.Exp( -i / bandDecay );
				if ( glideTo != null )
				{
					float g = (float)Math.Exp( -i / glideDecay );
					for ( int b = 0; b < nb; b++ )
						bp[b].Retune( glideTo[b] + (glideFrom[b] - glideTo[b]) * g );
				}
				for ( int b = 0; b < nb; b++ ) v += bp[b].Next( ex ) * k.BandLevel[b] * benv;
			}
			if ( k.Stick > 0f && i < stickLen )
			{
				float sn = HitNext( ref ns );
				if ( k.StickCut > 0f ) { stickLp += sa * (sn - stickLp); sn = stickLp; }
				v += sn * k.Stick * (1f - i / (float)stickLen);
			}
			if ( start + i >= chokeAt ) chokeEnv *= chokeStep;
			// As in RenderHat: original order, with the choke's neutral 1f last.
			v = v * amp * _c.HatBalance * _drumGain * _drumHighMul * chokeEnv;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	internal void RenderRide( int start, RideArt art, float amp, Rng noise )
		=> RenderRide( start, amp, noise, RideTone.For( art ) );

	/// <summary>A ride crescendo into a downbeat: the wash swelled by playing into it, which is
	/// what a drummer does rather than turning a fader up.</summary>
	internal void RenderRideSwell( int start, int spanSamples, Rng noise, float fromAmp,
		float toAmp, int stepSamples )
	{
		var tone = RideTone.Wash;
		for ( int at = 0; at < spanSamples; at += Math.Max( 1, stepSamples ) )
		{
			float u = spanSamples <= 0 ? 1f : at / (float)spanSamples;
			RenderRide( start + at, fromAmp + (toAmp - fromAmp) * u * u, noise, tone );
		}
	}
}
