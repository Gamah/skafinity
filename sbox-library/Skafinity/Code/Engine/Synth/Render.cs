using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// Pitched synthesis — turn queued note events into samples.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>Synthesize every pitched event whose span overlaps <c>[from, to)</c>,
	/// writing ONLY samples inside that window. Safe to call concurrently for disjoint
	/// windows: each output index is owned by exactly one window, a boundary-spanning
	/// note is re-rendered from its own start by each window (the SVF / high-pass state
	/// can't be resumed mid-stream), and each window walks <c>_events</c> in order, so
	/// writes never collide and the per-index sum order is deterministic.</summary>
	public void RenderPitchedRange( int from, int to )
	{
		from = Math.Max( 0, from );
		to = Math.Min( _bufL.Length, to );
		if ( to <= from ) return;
		var ph = new double[8];
		var inc = new double[8];
		// Pluck-comb scratch: one delay line per WINDOW, long enough for a full period of the
		// lowest note any voice plays (4096 samples is 10.8 Hz at 44.1 kHz), cleared per note.
		// Window-owned for the same reason ph/inc are — RenderPitchedRange's windows run on
		// different threads and synthesis must stay a pure function of the event.
		var comb = new float[4096];
		var events = _events;
		for ( int k = 0; k < events.Count; k++ )
		{
			var ev = events[k];
			// A silent note sums zero into the buffer, so synthesising it is pure waste. This is
			// the same "audible" test Onsets() applies, and it is what makes soloing one voice
			// cost one voice: the mix mutes by amplitude, so a soloed render still CARRIES every
			// other voice's events and used to render all of them at Amp 0.
			if ( ev.P.Amp <= 0f ) continue;
			int end = Math.Min( _bufL.Length, ev.Start + ev.Dur );
			if ( end <= from || ev.Start >= to ) continue; // no overlap with this window
			RenderEvent( ev, from, to, ph, inc, comb );
		}
	}

	// One pitched note. Computes from the note's own start (the running filter / breath
	// state can't be resumed mid-note) but writes only within [clipFrom, clipTo), and
	// stops once past clipTo since later windows own those samples. ph/inc are caller-
	// owned scratch (per-thread → no shared state).
	void RenderEvent( in NoteEvent ev, int clipFrom, int clipTo, double[] ph, double[] inc,
		float[] comb )
	{
		int start = ev.Start, dur = ev.Dur;
		float freq = ev.Freq;
		var p = ev.P;
		StereoGains( p.Pan, out float gL, out float gR );
		int atk = Math.Max( 1, (int)(p.Attack * _sr) );
		double decSamp = Math.Max( 1.0, p.Decay * _sr );
		int rel = Math.Max( 1, (int)(0.006f * _sr) );
		int voices = Math.Min( 8, p.Voices );

		for ( int v = 0; v < voices; v++ )
		{
			ph[v] = p.PhaseSeed;   // 0 for un-doubled notes → identical to the old in-phase start
			float cents = voices == 1 ? 0f : (v - (voices - 1) * 0.5f) * p.Detune;
			inc[v] = freq * Math.Pow( 2, cents / 1200.0 ) / _sr;
		}

		float low = 0, band = 0;
		float reso = Math.Clamp( p.Reso, 0.2f, 2f );
		float dnorm = p.Drive > 1f ? 1f / (float)Math.Tanh( p.Drive ) : 1f;
		// THE PLUCK. A string pinned at both ends and displaced at a fraction beta of its length
		// cannot excite any mode that has a node at beta, so its nth harmonic arrives scaled by
		// sin(pi n beta). A one-tap comb x[n] - x[n - beta.T] has magnitude 2|sin(pi n beta)| at
		// the nth harmonic of period T, so the comb IS that initial condition — not an imitation
		// of it. Halved to keep the peak where an un-plucked note's was. The delay is fractional
		// and read with linear interpolation: rounding it to a whole sample detunes the comb by
		// up to half a sample, which at the top of the register moves the nulls by a whole
		// harmonic. T is the NOMINAL period: a pluck happens once, at the start, so the comb must
		// not follow the vibrato or the bend that come after it.
		float combDelay = 0f;
		int cw = 0;
		if ( p.Pluck > 0f )
		{
			combDelay = Math.Clamp( p.Pluck, 0.02f, 0.98f ) * (_sr / Math.Max( 20f, freq ));
			if ( combDelay > comb.Length - 2 ) combDelay = comb.Length - 2;
			Array.Clear( comb, 0, comb.Length );
		}
		float hpA = p.Highpass > 0f ? (float)(1.0 / (1.0 + 2 * Math.PI * p.Highpass / _sr)) : 0f;
		float hpInPrev = 0f, hpOutPrev = 0f;
		uint bn = 0x9E3779B9u;

		int end = Math.Min( Math.Min( _bufL.Length, start + dur ), clipTo );
		int relStart = dur - rel;
		// Expression windows (samples): vibrato holds off then ramps in; the scoop is a quick
		// attack gesture. Kept fixed/absolute so a long held note locks on pitch after them.
		// The SVF coefficient only moves while the cutoff envelope does. Without one it is a
		// constant, so it is computed once here instead of a Sin() every sample — the same value,
		// not an approximation of it.
		bool cutMoves = p.CutEnv > 0f;
		bool dutyMoves = p.Osc == 4 && p.DutyEnv != 0f;
		float fixedF = cutMoves ? 0f
			: (float)(2 * Math.Sin( Math.PI * Math.Min( p.Cutoff, _sr * 0.16f ) / _sr ));
		// Both envelopes decay at the same rate, so both are walked as a running multiply rather
		// than an Exp() per sample per envelope per note — the inner loop's largest single cost.
		// The accumulators are double: over the ~10^6 samples of the longest note that is a
		// relative drift on the order of 10^-13, which is below the 16-bit output's last bit.
		double decStep = Math.Exp( -1.0 / decSamp );
		// The cutoff envelope gets its OWN time constant when the patch asks for one. Damping in
		// a real body grows with mode number, so the brightness outruns the loudness; sharing one
		// decay forces them equal, which is audibly a filter closing rather than a note dying.
		double cutStep = p.CutEnvSec > 0f
			? Math.Exp( -1.0 / Math.Max( 1.0, p.CutEnvSec * _sr ) ) : decStep;
		double ampDecay = 1.0;   // exp( -(i - atk) / decSamp ), advanced once past the attack
		double cutDecay = 1.0;   // exp( -i / decSamp ), advanced from the note's start
		int vibDelay = (int)(0.18f * _sr);
		int vibRamp = Math.Max( 1, (int)(0.16f * _sr) );
		int scoopWin = Math.Max( 1, (int)(0.16f * _sr) );
		for ( int i = 0; start + i < end; i++ )
		{
			float env;
			if ( i < atk ) env = (float)i / atk;
			else
			{
				float d = (float)ampDecay;
				env = p.Sustained ? p.Sustain + (1f - p.Sustain) * d : d;
				ampDecay *= decStep;
			}
			if ( i >= relStart ) env *= Math.Max( 0f, (float)(dur - i) / rel );
			if ( env < 0.0006f && i > atk && !p.Sustained ) break;

			float s = 0f;
			// Vibrato: subtle, and DELAYED so the note locks on pitch first and only blooms a
			// wobble if it's held — short notes stay dead-on. Depth is a small pitch fraction.
			float vib = 1f;
			if ( p.Vibrato > 0f && p.VibDepth > 0f )
			{
				float ramp = MathF.Max( 0f, (i - vibDelay) / (float)vibRamp );
				if ( ramp > 1f ) ramp = 1f;
				if ( ramp > 0f )
					vib = (float)(1.0 + p.VibDepth * ramp * Math.Sin( i / (double)_sr * p.Vibrato * 2 * Math.PI ));
			}
			// Pitch-bend envelope (semitones) on top of vibrato. Both are QUICK gestures over a
			// short fixed window so the note then sits locked on its target pitch (bendMul == 1):
			// BendSemis snaps to 0 over BendTime seconds (bend-in / glide); ScoopSemis is a fast
			// up-and-back hump confined to the attack (bend-and-release).
			float bendSemis = 0f;
			if ( p.BendSemis != 0f && p.BendTime > 0f )
			{
				int bt = Math.Min( dur, Math.Max( 1, (int)(p.BendTime * _sr) ) );
				if ( i < bt ) { float u = i / (float)bt; bendSemis += p.BendSemis * (1f - u * u * (3f - 2f * u)); }
			}
			if ( p.ScoopSemis != 0f && i < scoopWin )
				bendSemis += p.ScoopSemis * MathF.Sin( (float)(i / (float)Math.Min( dur, scoopWin ) * Math.PI) );
			// The BEND. The two gestures above start off-pitch and resolve onto it inside a short
			// window at the note's front; this one starts ON pitch and leaves it, part way in, and
			// either stays up or comes back — which is why it reads as a bend rather than as an
			// attack. Windows are absolute seconds for the same reason BendTime is: a bend is a
			// hand moving a string, so it must not scale with tempo or with note length.
			if ( p.BendUpSemis != 0f && p.BendUpTime > 0f )
			{
				int b0 = (int)(p.BendUpStart * _sr);
				int rise = Math.Max( 1, (int)(p.BendUpTime * _sr) );
				if ( i >= b0 )
				{
					float u = Math.Min( 1f, (i - b0) / (float)rise );
					float amt = u * u * (3f - 2f * u);
					if ( p.BendUpHold > 0f )
					{
						int r0 = b0 + rise + (int)(p.BendUpHold * _sr);
						if ( i >= r0 )
						{
							float w = Math.Min( 1f, (i - r0) / (float)rise );
							amt = 1f - w * w * (3f - 2f * w);
						}
					}
					bendSemis += p.BendUpSemis * amt;
				}
			}
			float bendMul = bendSemis != 0f ? (float)Math.Pow( 2.0, bendSemis / 12.0 ) : 1f;
			// Pulse width, swept by the same note-start exponential the cutoff envelope uses, so
			// a PWM voice relaxes toward its nominal duty as the note settles.
			float duty = p.Osc == 4 ? p.Duty + p.DutyEnv * (float)cutDecay : 0f;
			for ( int v = 0; v < voices; v++ )
			{
					double dt = inc[v] * vib * bendMul;
					double pv = ph[v] - Math.Floor( ph[v] );
					s += p.Osc == 4 ? PulseOsc( pv, dt, duty ) : BlepOsc( p.Osc, pv, dt );
					ph[v] += dt;
			}
			s /= voices;
			if ( combDelay > 0f )
			{
				comb[cw] = s;
				float rp = cw - combDelay;
				if ( rp < 0 ) rp += comb.Length;
				int r0 = (int)rp;
				float fr = rp - r0;
				int r1 = r0 + 1 == comb.Length ? 0 : r0 + 1;
				s = 0.5f * (s - (comb[r0] + (comb[r1] - comb[r0]) * fr));
				if ( ++cw == comb.Length ) cw = 0;
			}
			if ( p.Breath > 0f )
			{
				bn = unchecked( bn * 1664525u + 1013904223u );
				s += (bn / 4294967296f * 2f - 1f) * p.Breath;
			}
			if ( hpA > 0f )
			{
				float hp = hpA * (hpOutPrev + s - hpInPrev);
				hpInPrev = s; hpOutPrev = hp; s = hp;
			}

			// resonant low-pass (Chamberlin SVF) with cutoff envelope.
			// Clamp to ~sr/6 to keep the SVF stable.
			float f = fixedF;
			if ( cutMoves )
			{
				float cut = p.Cutoff + p.CutEnv * (float)cutDecay;
				f = (float)(2 * Math.Sin( Math.PI * Math.Min( cut, _sr * 0.16f ) / _sr ));
			}
			// Advanced here, AFTER both readers (the duty sweep above and the cutoff just now),
			// so the envelope's first sample is 1.0 for each of them.
			if ( cutMoves || dutyMoves ) cutDecay *= cutStep;
			float high = s - low - reso * band;
			band += f * high;
			low += f * band;
			float outp = low;

			if ( p.DriveEnv > 0f && p.Drive > 0f )
			{
				// A tanh's harmonic ladder steepens with input level: loud is BRIGHT, not merely
				// louder. So the drive rides the amp envelope, which is what a blown horn and a
				// pushed valve amp both do. Normalising by the RUNNING drive rather than by a
				// fixed one keeps the gesture spectral: the amp envelope still owns loudness, and
				// without it a dull quiet note would also be an attenuated one and the attack
				// would soften twice. That costs a second tanh per sample, which is why this
				// branch exists at all rather than the drive simply always tracking env.
				double dr = p.Drive * (1f + p.DriveEnv * env);
				double th = Math.Tanh( dr );
				outp = th > 1e-6 ? (float)(Math.Tanh( outp * dr ) / th) : outp;
			}
			else if ( p.Drive > 1f ) outp = (float)Math.Tanh( outp * p.Drive ) * dnorm;
			float val = outp * env * p.Amp;
			int idx = start + i;
			if ( idx >= clipFrom )
			{
				_bufL[idx] += val * gL;
				_bufR[idx] += val * gR;
			}
		}
	}
}
