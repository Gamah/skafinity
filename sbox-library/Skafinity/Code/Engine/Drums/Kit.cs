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

/// <summary>
/// THE MEASURED CYMBAL — the ride, its bell and both crashes, and the first spectrum in this
/// engine that is not invented.
///
/// A cymbal's identity lives in its mode forest, and authoring one from first principles produced
/// church bells (a sparse low cluster) where a real ride keeps its sustained energy in a DENSE
/// thicket of inharmonic partials at 1–3.5 kHz, over a handful of quiet lows. Real cymbals were
/// therefore measured — and the measurements collapsed into three closed-form laws (below): a
/// ring time τ(f), constant modal density for the forest, and a strike position as a
/// log-Gaussian spectral bump. The bow and the bell are the SAME forest — one piece of metal —
/// seen through two different bumps, which is what a bell that belongs to its own cymbal means.
///
/// Provenance (the sample set is NOT vendored and must not become a dependency — what lands is
/// the constants and this citation): Virtuosity Drums by Versilian Studios & Karoryfer
/// Samples, github.com/sfzinstruments/virtuosity_drums, CC0-1.0. Measured 2026-08-02 from the
/// overhead-mic samples — oh_ride_ride_vl3_rr1/rr2 (bow), oh_ride_bell_vl3_rr1/rr2 (bell),
/// oh_crash_crash_vl3 (the bright crash) and oh_flatride_crash_vl4 (the dark one): sustained
/// partials from a 131072-point FFT starting 0.33 s after onset (peaks ≥ 12 dB over the local
/// floor, takes merged within 5 Hz); per-band ring times from exponential fits over a 4096/1024
/// STFT. tools/spectool is the reader and stays in the repo, so any number here can be re-derived.
///
/// The partials alone are still not a cymbal — the measured attack is a broadband SPLASH, and
/// between the partials sits a noise wash that decays with the same band fits. Both ride under
/// the mode bank as shaped-noise layers. The old lesson that filtered noise reads as a hat
/// applies to noise as the IDENTITY; as the bed under a measured forest it is the thing the
/// pure-sine generation was missing.
///
/// THE FOUR STRIKES ARE THREE CYMBALS, and each one is measured rather than assumed to obey the
/// ride's law. A crash is not a loud ride: the bright crash's first half-second is roar with no
/// resolved partials in it at all, and the dark one's top outlives its own body. Where the
/// measurements disagree the constants disagree — see RingTau.
/// </summary>
readonly struct CymbalModal
{
	public readonly float[] Hz;
	public readonly float[] Amp;
	public readonly float[] Tau;    // per-partial ring time, seconds
	public readonly float[] Pan;    // per-partial offset around the kit position — a cymbal is wide
	public readonly float Dur;      // render length; the longest partial decides it
	public readonly float Level;    // energy-normalised, so candidates land comparably loud
	public readonly float Stick;
	public readonly float StickCut;
	public readonly float SplashLvl;   // broadband strike burst, rel. the normalised mode bank
	public readonly float SplashTau;
	public readonly float WashLvl;     // the air between the partials
	public readonly float WashTau;
	public readonly float NoiseHp;     // where both noise layers start — a crash's roar is not a hiss
	public readonly float WashLp;      // and where the wash stops

	CymbalModal( float[] hz, float[] amp, float[] tau, float[] pan, float dur, float level,
		float stick, float stickCut, float splashLvl, float splashTau, float washLvl,
		float washTau, float noiseHp, float washLp )
	{
		Hz = hz; Amp = amp; Tau = tau; Pan = pan; Dur = dur; Level = level;
		Stick = stick; StickCut = stickCut;
		SplashLvl = splashLvl; SplashTau = splashTau; WashLvl = washLvl; WashTau = washTau;
		NoiseHp = noiseHp; WashLp = washLp;
	}

	// ── The three laws the measurements collapsed into ──
	// The ride analysis alone produced 76 sustained partials with per-strike amplitudes and
	// per-band decay fits, and all of it reduces to closed forms with a handful of named
	// constants — the tables themselves do not ship, because they were one factory's cymbals and
	// the physics is the design.

	/// <summary>LAW 1 — the ring, and it is the law that is NOT shared. On the ride every
	/// per-band exponential fit lands on τ ≈ 39/√f within take-to-take scatter (230 Hz → 2.6 s,
	/// 850 → 1.3, 2.7 k → 0.75, 5.7 k → 0.51), and the long ring is the instrument — a ride RINGS
	/// in the mix — so nothing here shortens it for tidiness. Both crashes measured a different
	/// shape, and each needs one extra term:
	///
	///  * <paramref name="knee"/> — a LOW-FREQUENCY CUT. The bright crash holds τ·√f ≈ 45 only
	///    above ~1 kHz; below that the lows die fast (0.9 s at 375 Hz where the bare law says
	///    2.0). A big thin plate struck hard dumps its low modes into the room in the first
	///    moments, and under the knee the ring falls off in proportion to the frequency.
	///  * <paramref name="sizzle"/> — a rising FLOOR. The dark crash inverts the ride: its
	///    low-mids are gone in half a second while 7–14 kHz rings for 1.5–2.0 s. That is wash and
	///    rivet behaviour rather than plate behaviour, so it is a second term that takes over
	///    where it is the longer of the two, not a different exponent on the first.
	///
	/// One expression covers all three cymbals, and every constant in it was fitted.</summary>
	static float RingTau( float hz, float k, float knee, float sizzle )
	{
		float t = k / MathF.Sqrt( hz );
		if ( knee > 0f && hz < knee ) t *= hz / knee;
		if ( sizzle > 0f ) t = MathF.Max( t, sizzle * MathF.Sqrt( hz / SizzleRef ) );
		return t;
	}
	const float SizzleRef = 8000f;   // where the sizzle term is quoted: dark crash τ ≈ 1.5 s there

	/// <summary>LAW 2 — the forest: a thin plate's flexural modal density is CONSTANT in
	/// frequency (the textbook plate result — mode count per kHz does not depend on f), so the
	/// mode positions are uniform random at the measured density, and a fraction of them split
	/// into near-degenerate pairs a few Hz apart — a real plate's ± mode pairs, split by
	/// asymmetry, and the source of the slow beating in the ring. Density goes with the plate's
	/// area over its thickness, so a big thin crash carries a denser forest than a ride.</summary>
	const float RideModesPerKHz = 27f;      // resolved sustained peaks per kHz in the ride analysis
	const float CrashModesPerKHz = 38f;     // the bright crash: bigger and thinner, and its sustain
	                                        // resolves nothing at all — a continuum is a forest too
	                                        // dense to resolve, which is what this number says
	// The top is 11 kHz because the SIZZLE IS MODES TOO: the measured sustain holds -9..-14 dB
	// of energy at 5–10 kHz ringing at ~0.5 s — exactly what τ=39/√f predicts — and cutting the
	// forest at 4 kHz measured 10–15 dB light up there against the reference.
	const float ForestLo = 210f, ForestHi = 11000f;
	const float PairChance = 0.35f;         // measured clusters: 2166/2170/2177 and kin

	/// <summary>LAW 3 — a strike position is a spectral bump. On a log-frequency axis each
	/// strike's excitation is Gaussian: the bow is one WIDE bump (the measured sustain is flat
	/// from 300 Hz to 3.2 kHz with soft edges), the bell is a NARROW clang plus a small low
	/// knock where the stick shocks the cup. Same forest, different window onto it — which is
	/// why the bell unmistakably belongs to its own cymbal.</summary>
	static float LogBump( float f, float centre, float width )
	{
		float u = MathF.Log( f / centre ) / width;
		return MathF.Exp( -0.5f * u * u );
	}

	/// <summary>One strike: a main bump, and an optional second. The crashes need the second —
	/// the bright crash's sustained energy centres at 2.2–4.7 kHz over a low knock, and the dark
	/// crash is a low-heavy attack (+13 dB at 300–700 Hz) whose sizzly top has to be excited
	/// before the ring law can let it outlive anything.</summary>
	readonly struct Strike
	{
		public readonly float Centre, Width, Centre2, Width2, Level2;
		public Strike( float centre, float width, float centre2 = 0f, float width2 = 0.3f,
			float level2 = 0f )
		{ Centre = centre; Width = width; Centre2 = centre2; Width2 = width2; Level2 = level2; }

		public float Weight( float f )
			=> LogBump( f, Centre, Width )
				+ (Level2 > 0f ? Level2 * LogBump( f, Centre2, Width2 ) : 0f);
	}

	static readonly Strike BowStrike = new( 1200f, 1.1f );
	static Strike BellStrike( float clang ) => new( clang, 0.45f, 290f, 0.25f, 0.30f );
	// The bright crash: the sustained energy centre measured 2.2–4.7 kHz at +8 dB over the mids,
	// and the attack is broadband — a wide bump up there over a small low knock.
	static readonly Strike BrightCrashStrike = new( 3200f, 0.72f, 400f, 0.55f, 0.22f );
	// The dark one is the same instrument struck the other way up: the body is low, and the top
	// it keeps longest still has to be struck to be there at all.
	static readonly Strike DarkCrashStrike = new( 520f, 0.75f, 9000f, 0.60f, 0.45f );

	/// <summary>HOW LOUD ONE STROKE IS, and it is not the same number for a ride and a crash even
	/// though the same arm strikes both. Every cymbal here is energy-normalised, so this is what
	/// that normalisation is set TO — and the ride's is far under the crash's for a reason that
	/// only appears in a groove: a ride stroke rings for seconds and is played eight times a bar,
	/// so a riding section has a dozen rings sounding at once, while the hi-hat it replaces is
	/// thirty-five milliseconds and never overlaps itself at all. Level per stroke and level in
	/// the mix are two different quantities, and it is the mix that is being balanced — measured
	/// with --levels, a ride at the crash's stroke level put country's kit 2.6 dB over the rest of
	/// its band on the strength of its riding sections alone. A crash overlaps nothing, being one
	/// gesture a phrase, so it keeps the louder stroke and its presence is tuned where every other
	/// piece of the kit's is: the house balance.</summary>
	const float StrokeLevelRide = 0.24f, StrokeLevelCrash = 0.55f;

	/// <summary>The ride's bow.</summary>
	/// <param name="splash">the broadband strike burst — the measured attack is nearly flat
	/// across the whole band, and this is most of what "cymbal" means at the moment of contact.</param>
	/// <param name="wash">the sustained air between the partials.</param>
	/// <param name="ring">scales every ring time; 1 is as measured.</param>
	public static CymbalModal Bow( float splash = 1f, float wash = 1f, float ring = 1f )
		=> Build( "skafinity:ride:forest", BowStrike, second: false, RideModesPerKHz,
			tauK: 39f, knee: 0f, sizzle: 0f, ring: ring,
			splash: 0.55f * splash, splashTau: 0.10f,
			washLvl: 0.060f * wash, washTau: 0.70f * ring,
			stick: 0.30f, stickCut: 5500f, noiseHp: 240f, washLp: 6500f,
			level: StrokeLevelRide );

	/// <summary>The bell. A ride bell is NOT a church bell: no harmonic ratio stack, no low
	/// fundamental — the measurement puts its energy in a clang cluster around 2.3 kHz over the
	/// same metal as the bow.</summary>
	/// <param name="clang">the clang bump's centre — darker or brighter bell.</param>
	public static CymbalModal Bell( float splash = 1f, float ring = 1f, float clang = 2300f )
		=> Build( "skafinity:ride:forest", BellStrike( clang ), second: true, RideModesPerKHz,
			tauK: 39f, knee: 0f, sizzle: 0f, ring: ring,
			splash: 0.40f * splash, splashTau: 0.05f,
			washLvl: 0.030f, washTau: 0.55f * ring,
			stick: 0.40f, stickCut: 6500f, noiseHp: 240f, washLp: 6500f,
			level: StrokeLevelRide );

	/// <summary>The bright crash. THE ROAR IS THE INSTRUMENT: a third of a second in, the
	/// measurement resolves essentially no partials — a crash's sustain is a continuum, and the
	/// forest only surfaces as the roar dies. So the splash here is not an attack transient, it
	/// is a layer with a third of a second of its own decay that the modes emerge from
	/// underneath, which is what the measurement describes and no amount of forest can be.</summary>
	public static CymbalModal CrashBright( float splash = 1f, float ring = 1f, float wash = 1f )
		=> Build( "skafinity:crash:bright", BrightCrashStrike, second: false, CrashModesPerKHz,
			tauK: 45f, knee: 1000f, sizzle: 0f, ring: ring,
			splash: 2.30f * splash, splashTau: 0.30f,
			washLvl: 0.55f * wash, washTau: 1.05f * ring,
			stick: 0.10f, stickCut: 6000f, noiseHp: 2400f, washLp: 6200f,
			level: StrokeLevelCrash );

	/// <summary>The dark crash — a heavier, flatter cymbal crashed rather than ridden, and the
	/// opposite shape to the bright one at both ends: real resolved lows that ring (208/211,
	/// 255/258, 284, 401 — beating pairs), a body gone in half a second, and a top that outlives
	/// everything.</summary>
	public static CymbalModal CrashDark( float splash = 1f, float ring = 1f, float wash = 1f )
		=> Build( "skafinity:crash:dark", DarkCrashStrike, second: true, RideModesPerKHz,
			tauK: 13.5f, knee: 0f, sizzle: 1.5f, ring: ring,
			splash: 1.50f * splash, splashTau: 0.22f,
			washLvl: 0.35f * wash, washTau: 0.90f * ring,
			stick: 0.10f, stickCut: 4500f, noiseHp: 200f, washLp: 4200f,
			level: StrokeLevelCrash );

	/// <param name="second">which of the two per-mode depth draws this strike keeps. Both are
	/// drawn for every mode whatever the strike is, so a cymbal's two strikes are the same metal
	/// seen through two windows — the same one-draw discipline as PickWeighted.</param>
	static CymbalModal Build( string seed, in Strike strike, bool second, float density,
		float tauK, float knee, float sizzle, float ring, float splash, float splashTau,
		float washLvl, float washTau, float stick, float stickCut, float noiseHp, float washLp,
		float level )
	{
		// ONE cymbal: the forest is grown from a fixed seed, so every strike rings the same metal
		// and every hit of every song is the same instrument. A different cymbal is a different
		// seed, because it is a different piece of metal.
		var r = new Rng( seed );
		var hz = new List<float>(); var am = new List<float>();
		var ta = new List<float>(); var pn = new List<float>();
		int count = (int)((ForestHi - ForestLo) * density / 1000f);
		float e = 0f, maxTau = 0f;
		for ( int i = 0; i < count; i++ )
		{
			float f = ForestLo + (ForestHi - ForestLo) * r.Next();
			float depth1 = 0.20f + 0.80f * r.Next();
			float depth2 = 0.20f + 0.80f * r.Next();
			bool pair = r.Next() < PairChance;
			float split = 1.5f + 6.5f * r.Next();
			float a = strike.Weight( f ) * (second ? depth2 : depth1);
			void Add( float fq, float aq )
			{
				hz.Add( fq ); am.Add( aq );
				float t = ring * RingTau( fq, tauK, knee, sizzle ) * (0.85f + r.Next() * 0.30f);
				ta.Add( t ); pn.Add( (r.Next() * 2f - 1f) * 0.30f );
				e += aq * aq; maxTau = Math.Max( maxTau, t );
			}
			Add( f, a );
			if ( pair ) Add( f + split, a * (0.45f + 0.4f * r.Next()) );
			else { r.Next(); r.Next(); }   // the pair's two draws, kept whether or not it exists
		}
		// Energy-normalised so the strikes of one cymbal land comparably loud, then set to the
		// voice's own level (see StrokeLevel).
		level /= MathF.Sqrt( Math.Max( 1e-6f, e ) );
		float dur = Math.Clamp( maxTau * 1.35f + 0.15f, 1.2f, 4.0f );
		return new CymbalModal( hz.ToArray(), am.ToArray(), ta.ToArray(), pn.ToArray(),
			dur, level, stick, stickCut, splash, splashTau, washLvl, washTau, noiseHp, washLp );
	}
}

/// <summary>
/// A CYMBAL RENDERED ONCE, so that playing it costs a copy rather than a synthesis.
///
/// The modal bank is the right sound and the wrong shape for a groove: several hundred partials
/// over a ring measured in seconds is ~250 ms of CPU per hit, and a riding section is a thousand
/// hits. So the cymbal is built ONCE per song — it is one physical object, and every strike of it
/// is the same object — and each hit stamps that render into the mix. That is what a sampler
/// does, and the reason it is not a compromise here is that the thing being repeated was measured
/// off a real cymbal in the first place.
///
/// IT IS TWO TABLES, SPLIT AT <see cref="BandSplit"/>, because a soft stroke is DARKER and not
/// merely quieter — a stick that does not dig in leaves the high modes alone. A hit mixes the two
/// with different gains (see RenderCymbal), which is the same tilt the per-hit synthesis applied
/// per partial, at two bands instead of every one. The splash rides in the high table and the
/// wash in the low, so both tilt with the layer they belong to.
///
/// What a table CANNOT vary per hit is the phase of every partial, and that is what round robins
/// are for: <see cref="CymbalTable.Variants"/> renders the same cymbal struck in a few different
/// places. The stick transient is still synthesised live per hit — it is 4 ms, it costs nothing,
/// and the attack is where the ear listens for repetition hardest.
/// </summary>
sealed class CymbalTable
{
	/// <summary>Where the tilt splits the cymbal. Above this is stick and shimmer, below it is
	/// the body — a soft stroke keeps the body and loses the top.</summary>
	public const float BandSplit = 2500f;

	public readonly float[] LoL, LoR, HiL, HiR;
	public readonly int Len;
	public readonly float Stick, StickCut;
	/// <summary>Where this cymbal sits — baked into the table, and kept so the live stick lands
	/// in the same place the metal it is hitting does.</summary>
	public readonly float Pan;
	/// <summary>Everything about this voice's level that is fixed for the song: its Vol, its
	/// Balance and the tone lean. A hit passes only its musical amplitude.</summary>
	public readonly float Bus;

	/// <summary>The empty cymbal — what a muted kit gets, so nothing is rendered and nothing is
	/// built.</summary>
	public static readonly CymbalTable Silent = new();

	CymbalTable()
	{
		LoL = LoR = HiL = HiR = Array.Empty<float>();
	}

	CymbalTable( float[] loL, float[] loR, float[] hiL, float[] hiR, int len, float stick,
		float stickCut, float bus, float pan )
	{
		LoL = loL; LoR = loR; HiL = hiL; HiR = hiR; Len = len;
		Stick = stick; StickCut = stickCut; Bus = bus; Pan = pan;
	}

	/// <summary>How many round robins a voice builds. Two is enough to break the tell and the
	/// cost is linear in it; the cymbals that are struck once a phrase build one.</summary>
	public const int Variants = 2;

	/// <summary>Render the cymbal. <paramref name="pan"/> is where it sits in the kit, and it is
	/// baked in: the stereo image of a cymbal is per song, and a partial-by-partial spread is not
	/// something a stamp can apply afterwards.</summary>
	public static CymbalTable Render( in CymbalModal m, int sr, float pan, float bus, int variant )
	{
		int len = Math.Max( 1, (int)(sr * m.Dur) );
		var loL = new float[len]; var loR = new float[len];
		var hiL = new float[len]; var hiR = new float[len];

		int n = m.Hz.Length;
		// A round robin is the same cymbal struck somewhere else: the modes are the metal and do
		// not move, but which of them the stick catches, and in what phase, does.
		uint s = 0x9e3779b9u ^ (uint)(variant * 2654435761u) | 1u;
		float peak = 1e-9f;
		for ( int p = 0; p < n; p++ ) peak = MathF.Max( peak, m.Amp[p] );

		var cx = new float[n]; var cy = new float[n];
		var rc = new float[n]; var rs = new float[n];
		var gl = new float[n]; var gr = new float[n];
		var dies = new int[n]; var order = new int[n];
		int nHi = 0;
		for ( int p = 0; p < n; p++ )
		{
			double w = 2.0 * Math.PI * Math.Min( m.Hz[p], sr * 0.45f ) / sr;
			double d = Math.Exp( -1.0 / (sr * (double)m.Tau[p]) );
			rc[p] = (float)(Math.Cos( w ) * d); rs[p] = (float)(Math.Sin( w ) * d);
			double ph = Frac( ref s ) * Math.PI;
			float jit = 1f + 0.35f * Frac( ref s );
			float a0 = m.Amp[p] * jit * m.Level;
			cx[p] = (float)(a0 * Math.Cos( ph )); cy[p] = (float)(a0 * Math.Sin( ph ));
			Osc.StereoGains( Math.Clamp( pan + m.Pan[p], -1f, 1f ), out gl[p], out gr[p] );
			if ( m.Hz[p] >= BandSplit ) nHi++;
			// A PARTIAL STOPS BEING WORTH ITS MULTIPLY long before the table ends. Its own
			// amplitude decides when: the quiet ones are inaudible from the start, and the top of
			// the forest is gone in a fraction of the ring the lows get. Culling under a floor set
			// by the bank's own peak is most of what makes a several-hundred-partial cymbal
			// affordable to render at all, and it is exact to within that floor.
			float a = MathF.Abs( m.Amp[p] * jit ) / peak;
			dies[p] = a <= Floor ? 0
				: Math.Min( len, (int)(sr * m.Tau[p] * MathF.Log( a / Floor )) + 1 );
			order[p] = p;
		}
		// TWO BANKS, each sorted longest-lived first. Sorting means the live partials are a
		// PREFIX that only ever shrinks, so the inner loop needs no per-partial test; splitting
		// means it needs no per-partial BRANCH either, which is the whole reason the tilt's two
		// bands are separate arrays rather than a flag.
		Array.Sort( dies, order );
		var lo = new Bank( n - nHi ); var hi = new Bank( nHi );
		for ( int i = n - 1; i >= 0; i-- )
		{
			int p = order[i];
			(m.Hz[p] >= BandSplit ? hi : lo).Add( cx[p], cy[p], rc[p], rs[p], gl[p], gr[p], dies[i] );
		}

		// The noise layers: the splash (the measured attack is broadband — the strike sprays
		// energy everywhere at once, and on a crash it keeps doing so for a third of a second)
		// and the wash (the air between the partials, darker and long).
		float hpA = HpC( m.NoiseHp, sr ), washLpA = LpC( m.WashLp, sr );
		float nInPrev = 0f, nHpPrev = 0f, washLp = 0f;
		double splDecay = sr * (double)Math.Max( 0.005f, m.SplashTau );
		double washDecay = sr * (double)Math.Max( 0.02f, m.WashTau );
		bool noise = m.SplashLvl > 0f || m.WashLvl > 0f;
		Osc.StereoGains( pan, out float sgL, out float sgR );

		// The longest partials outlive the table; a short fade keeps the truncation silent.
		int fade = (int)(sr * 0.12f);
		for ( int i = 0; i < len; i++ )
		{
			lo.Step( i, out float ll, out float lr );
			hi.Step( i, out float hl, out float hr );
			if ( noise )
			{
				float w = Frac( ref s );
				float hp = hpA * (nHpPrev + w - nInPrev); nInPrev = w; nHpPrev = hp;
				washLp += washLpA * (hp - washLp);
				float spl = hp * m.SplashLvl * (float)Math.Exp( -i / splDecay );
				float wsh = washLp * m.WashLvl * (float)Math.Exp( -i / washDecay );
				hl += spl * sgL; hr += spl * sgR;
				ll += wsh * sgL; lr += wsh * sgR;
			}
			int rem = len - i;
			float g = rem < fade ? rem / (float)fade : 1f;
			loL[i] = ll * g; loR[i] = lr * g;
			hiL[i] = hl * g; hiR[i] = hr * g;
		}
		return new CymbalTable( loL, loR, hiL, hiR, len, m.Stick, m.StickCut, bus, pan );
	}

	/// <summary>One band's partials, packed longest-lived first. Each is a 2-D rotation whose
	/// radius shrinks by its own decay every sample — pure recurrence, no per-sample
	/// transcendentals, which is what makes a bank of hundreds tractable at all.</summary>
	sealed class Bank
	{
		readonly float[] _cx, _cy, _rc, _rs, _gl, _gr;
		readonly int[] _dies;
		int _n, _live;

		public Bank( int cap )
		{
			cap = Math.Max( 1, cap );
			_cx = new float[cap]; _cy = new float[cap]; _rc = new float[cap]; _rs = new float[cap];
			_gl = new float[cap]; _gr = new float[cap]; _dies = new int[cap];
		}

		public void Add( float cx, float cy, float rc, float rs, float gl, float gr, int dies )
		{
			_cx[_n] = cx; _cy[_n] = cy; _rc[_n] = rc; _rs[_n] = rs;
			_gl[_n] = gl; _gr[_n] = gr; _dies[_n] = dies;
			_live = ++_n;
		}

		public void Step( int i, out float l, out float r )
		{
			while ( _live > 0 && _dies[_live - 1] <= i ) _live--;
			float sl = 0f, sr = 0f;
			for ( int p = 0; p < _live; p++ )
			{
				float x = _cx[p] * _rc[p] - _cy[p] * _rs[p];
				_cy[p] = _cx[p] * _rs[p] + _cy[p] * _rc[p];
				_cx[p] = x;
				sl += x * _gl[p]; sr += x * _gr[p];
			}
			l = sl; r = sr;
		}
	}

	/// <summary>The amplitude at which a partial is dropped, relative to the loudest in the
	/// bank — about −86 dB, i.e. under the 16-bit floor of a mix this partial shares with a
	/// whole kit.</summary>
	const float Floor = 5e-5f;

	// The table's own noise/phase stream. Local to the build, so it costs nothing from the song's
	// RNG and a cymbal is the same object however the composition around it changes.
	static float Frac( ref uint s )
	{
		s ^= s << 13; s ^= s >> 17; s ^= s << 5;
		return (s & 0xffff) / 32768f - 1f;      // −1 .. 1
	}

	// One-pole coefficients, the same forms the voices use; a table is built outside MusicGen so
	// it cannot borrow the instance methods.
	static float HpC( float fc, int sr ) => (float)(1.0 / (1.0 + 2 * Math.PI * fc / sr));
	static float LpC( float fc, int sr ) => (float)(1.0 - Math.Exp( -2 * Math.PI * fc / sr ));
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
		=> RenderKick( start, noise, amp, _kickTone, 0f );

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

	/// <summary>Build one of this song's cymbals. The forest and its ring are the cymbal's own
	/// (a fixed seed — one piece of metal, the same in every song); what the SONG contributes is
	/// where the cymbal sits in the field and what its bus does, and both are baked in because
	/// neither can be applied to a stamp afterwards.</summary>
	internal CymbalTable BuildCymbal( in CymbalModal m, float pan, float bus, int variant = 0 )
		=> _drumGain <= 0f ? CymbalTable.Silent
			: CymbalTable.Render( m, _sr, pan, bus, variant );

	/// <summary>The ride, on the right of the kit opposite the hats. The bus carries everything
	/// fixed for the song; the VOLUME knob stays at the call site, where every other cymbal hit
	/// already applies it.</summary>
	internal CymbalTable BuildRide( in CymbalModal m, int variant = 0 )
		=> BuildCymbal( m, _drumPan, _c.HatBalance * _drumHighMul, variant );

	/// <summary>A crash. The kit's two crashes are panned apart, and which side each is on is
	/// the song's own draw — so a ridden crash and the one accenting over it land opposite each
	/// other for free.</summary>
	internal CymbalTable BuildCrash( in CymbalModal m, bool dark, int variant = 0 )
		=> BuildCymbal( m, dark == _crashBrightLeft ? _drumPan : -_drumPan,
			_c.CrashBalance * _drumHighMul, variant );

	// ── This song's cymbals ──
	// Built on first use and kept: they are by far the most expensive objects the engine makes,
	// and a song that never rides must not pay for a ride. Nothing here touches the song's RNG —
	// a cymbal is a physical object with a fixed seed of its own — so building one lazily cannot
	// move a note.

	/// <summary>The bow, alternating round robins so consecutive hits are not the same waveform.
	/// <paramref name="index"/> is the hit's position in whatever is playing it.</summary>
	CymbalTable RideBow( int index ) => (index & 1) == 0
		? _rideBow0 ??= BuildRide( CymbalModal.Bow(), 0 )
		: _rideBow1 ??= BuildRide( CymbalModal.Bow(), 1 );

	CymbalTable RideBell => _rideBell ??= BuildRide( CymbalModal.Bell() );
	CymbalTable CrashBright => _crashBright ??= BuildCrash( CymbalModal.CrashBright(), dark: false );
	CymbalTable CrashDark => _crashDark ??= BuildCrash( CymbalModal.CrashDark(), dark: true );

	/// <summary>Play a cymbal: stamp the rendered object into the mix (see CymbalTable).
	///
	/// The three things that are per HIT rather than per cymbal: how hard it was struck, which
	/// is a TILT and not a fader — a soft stroke keeps the body and loses the top, so the two
	/// bands take different gains; the stick, which is synthesised live because 4 ms is free and
	/// the attack is where repetition is heard; and the choke, which is a hand on the metal.
	/// </summary>
	/// <param name="chokeAt">Absolute sample position the cymbal is grabbed at, or int.MaxValue.
	/// A choke is a fast release rather than a cut — a hand is not instantaneous and a hard stop
	/// clicks.</param>
	internal void RenderCymbal( int start, float amp, CymbalTable t, int chokeAt = int.MaxValue )
	{
		if ( _drumGain <= 0f || t == null || t.Len == 0 || amp <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		int end = Math.Min( _bufL.Length, start + t.Len );
		if ( end <= start ) return;

		uint ns = HitSeed( start );
		// No two strokes are identical, and a stamped table is identical by construction — so the
		// variation a per-hit synthesis got for free has to be put back deliberately.
		float jit = 1f + 0.09f * HitNext( ref ns );
		float bus = t.Bus * _drumGain * jit;
		float lo = amp * bus;
		// THE TILT. Level and brightness move together on a struck cymbal: this is the same
		// relationship the per-partial version applied, at two bands instead of every one.
		float hi = amp * MathF.Pow( Math.Clamp( amp, 0.05f, 1f ), 0.35f ) * bus;

		int stickLen = (int)(_sr * 0.004f);
		float sa = t.StickCut > 0f ? LpCoeff( t.StickCut ) : 0f;
		float lp = 0f;
		StereoGains( t.Pan, out float sgL, out float sgR );
		float chokeStep = (float)Math.Exp( -1.0 / Math.Max( 1.0, _sr * 0.020 ) );
		float chokeEnv = 1f;
		for ( int i = 0; start + i < end; i++ )
		{
			float l = t.LoL[i] * lo + t.HiL[i] * hi;
			float r = t.LoR[i] * lo + t.HiR[i] * hi;
			if ( t.Stick > 0f && i < stickLen )
			{
				float sn = HitNext( ref ns );
				if ( t.StickCut > 0f ) { lp += sa * (sn - lp); sn = lp; }
				float sv = sn * t.Stick * (1f - i / (float)stickLen) * lo;
				l += sv * sgL; r += sv * sgR;
			}
			if ( start + i >= chokeAt )
			{
				chokeEnv *= chokeStep;
				if ( chokeEnv < 1e-4f ) break;
			}
			_bufL[start + i] += l * chokeEnv;
			_bufR[start + i] += r * chokeEnv;
		}
	}

}
