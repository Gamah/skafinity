using System;
using System.Collections.Generic;

namespace Skafinity;

// Patch — the subtractive voice definition every pitched note is rendered through:
// unison oscillators → optional high-pass → resonant low-pass with a cutoff envelope.
//
// Part of the MusicGen engine — see MusicGen.cs.

// ── Synth core: unison osc → optional high-pass → resonant low-pass (cutoff
//    envelope) → soft drive → AD/sustain amp env. ──
struct Patch
{
	public int Osc;        // 0 sine 1 saw 2 square 3 triangle 4 pulse (see Duty)
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
	// ── Timbre beyond "a waveform through a low-pass" ──
	// Four parameters, each one a physical property of a real sounding body that a detuned saw
	// into an SVF cannot express at any setting. All four are INERT at zero, so a patch that
	// does not set them renders exactly as it did before.
	public float Duty;     // Osc 4 only: pulse width 0..1. Nulls every (1/Duty)th harmonic —
	                       // spectral shape a low-pass cannot reach. 0.5 == square.
	public float DutyEnv;  // Duty swept by this much over the note's decay (PWM). The comb of
	                       // nulls moves while every harmonic stays on its own frequency, so the
	                       // timbre evolves without the pitch or the brightness ceiling moving.
	public float Pluck;    // 0 = off. Else the PLUCK POSITION along the string, 0..1 of its
	                       // length. A string displaced at a point beta has no energy in any mode
	                       // with a node there, so its harmonics carry a factor sin(pi n beta) —
	                       // which is exactly the response of a single comb delay of beta periods
	                       // (see RenderEvent). Bridge-ward (small) is thin and nasal, centre
	                       // (0.5) is round and has no even harmonics at all. This is WHERE the
	                       // string was hit, and no filter is a position.
	public float CutEnvSec; // Seconds for the CUTOFF envelope's decay. 0 = follow Decay (the old
	                       // behaviour). A real body's high partials die faster than its low ones
	                       // — damping grows with mode number — so the brightness and the loudness
	                       // decay at DIFFERENT rates, and tying them together is what makes a
	                       // note read as a filter sweep rather than as something ringing.
	public float DriveEnv; // Extra tanh drive at full envelope, as a fraction of Drive. A
	                       // non-linearity's harmonic ladder steepens with input level, so a
	                       // LOUD note through one is a BRIGHTER note — the defining behaviour of
	                       // a blown brass instrument and of a valve amp, and the one thing a
	                       // fixed drive can never do. Level is held constant across the sweep
	                       // (RenderEvent normalises by the running drive), so this changes the
	                       // spectrum only and the amp envelope still owns loudness.
	// ── Expression (per-note pitch shaping; see Expression/Voicing) ──
	public float VibDepth;   // vibrato depth as a pitch fraction (0 → legacy 0.005 when Vibrato>0)
	public float BendSemis;  // pitch offset in semitones at note START, glides to 0 (bend-in / glide); −ve starts below
	public float BendTime;   // 0..1 fraction of the note over which BendSemis glides to 0
	public float ScoopSemis; // height (semitones) of a mid-note bend-up-and-back hump (0 = none)
	// THE BEND — the one a listener would name as one, and the only gesture here that moves the
	// note AWAY from its pitch rather than easing onto it. Everything above is an approach: it
	// starts off-pitch and resolves. This starts ON pitch, pushes up by BendUpSemis, and stays
	// there — or comes back, if BendUpHold says how long to sit at the top first. All three times
	// are SECONDS, never a fraction of the note, so a bend is the same physical gesture whatever
	// the tempo and whatever the note length.
	public float BendUpSemis; // semitones bent UP part way through the note (0 = none)
	public float BendUpStart; // seconds into the note where the bend begins
	public float BendUpTime;  // seconds the bend takes to reach pitch (and to come back down)
	public float BendUpHold;  // seconds held at pitch before releasing; 0 = held to the end
	public float PhaseSeed;  // oscillator start phase (0..1); 0 = legacy in-phase start. Used to
	                         // decorrelate the two double-tracking takes (see RenderPatch).
}

public sealed partial class MusicGen
{
}
