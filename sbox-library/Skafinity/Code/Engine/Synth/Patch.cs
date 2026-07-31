using System;
using System.Collections.Generic;

namespace Skafinity;

// Patch — the subtractive voice definition every pitched note is rendered through:
// unison oscillators → optional high-pass → resonant low-pass with a cutoff envelope.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
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
}
