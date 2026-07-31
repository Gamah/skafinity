using System;
using System.Collections.Generic;

namespace Skafinity;

// Master bus: the reverb, then soft-clip and normalize.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// Master: gentle soft-clip + normalize. The mix peak is first normalized to 1.0
	// BEFORE the soft-clipper so it always has headroom — otherwise a hot sustained
	// bed (all voices flat at 1.0) saturated the tanh and swallowed the drum
	// transients (kick/snare washed out). MasterDrive now sets how hard a
	// peak-normalized signal hits the clipper, so the dynamics stay intact.
	// Call only after every RenderPitchedRange window has completed.
	float Master()
	{
		int total = _bufL.Length;
		float rawPeak = 0f;
		for ( int i = 0; i < total; i++ )
			rawPeak = Math.Max( rawPeak, Math.Max( MathF.Abs( _bufL[i] ), MathF.Abs( _bufR[i] ) ) );
		float pre = rawPeak > 0.0001f ? _c.MasterDrive / rawPeak : _c.MasterDrive;

		for ( int i = 0; i < total; i++ )
		{
			_bufL[i] = (float)Math.Tanh( _bufL[i] * pre );
			_bufR[i] = (float)Math.Tanh( _bufR[i] * pre );
		}

		// A touch of stereo room reverb — the dry mix alone read flat/"16-bit".
		ApplyReverb();

		float peak = 0f;
		for ( int i = 0; i < total; i++ )
		{
			float a = Math.Max( MathF.Abs( _bufL[i] ), MathF.Abs( _bufR[i] ) );
			if ( a > peak ) peak = a;
		}
		return peak > 0.0001f ? _c.MasterPeak / peak : 1f;
	}

	// ── Master reverb ──
	// A Schroeder/Freeverb-style bank: several parallel damped comb filters (the dense
	// tail) feeding a chain of allpasses (diffusion). The two channels use slightly
	// different delay lengths so the room is decorrelated → real stereo width and depth.
	static readonly int[] CombBase = { 1116, 1188, 1277, 1356, 1422, 1491 }; // samples @ 44.1k
	static readonly int[] ApBase = { 556, 441, 341 };
	const int ReverbStereoSpread = 23; // R-channel delay offset for decorrelation

	void ApplyReverb()
	{
		float wet = Math.Clamp( _c.MasterReverb, 0f, 1f );
		if ( wet <= 0.0001f ) return;
		float feedback = 0.70f + 0.28f * Math.Clamp( _c.ReverbDecay, 0f, 1f ); // tail length
		const float damp = 0.25f, damp1 = 1f - damp;                            // HF damping in the tail
		const float apg = 0.5f;                                                 // allpass coefficient
		const float inGain = 0.25f;                                             // drive into the reverb
		double srk = _sr / 44100.0;                                             // scale delays to the rate

		for ( int ch = 0; ch < 2; ch++ )
		{
			var buf = ch == 0 ? _bufL : _bufR;
			int off = ch == 0 ? 0 : ReverbStereoSpread;
			int nc = CombBase.Length, na = ApBase.Length;
			var combBuf = new float[nc][];
			var combIdx = new int[nc];
			var combStore = new float[nc];
			for ( int j = 0; j < nc; j++ )
				combBuf[j] = new float[Math.Max( 1, (int)Math.Round( (CombBase[j] + off) * srk ) )];
			var apBuf = new float[na][];
			var apIdx = new int[na];
			for ( int j = 0; j < na; j++ )
				apBuf[j] = new float[Math.Max( 1, (int)Math.Round( (ApBase[j] + off) * srk ) )];

			int n = buf.Length;
			for ( int i = 0; i < n; i++ )
			{
				float input = buf[i] * inGain;
				float acc = 0f;
				for ( int j = 0; j < nc; j++ )
				{
					var cb = combBuf[j];
					int idx = combIdx[j];
					float r = cb[idx];
					combStore[j] = r * damp1 + combStore[j] * damp;
					cb[idx] = input + combStore[j] * feedback;
					if ( ++idx >= cb.Length ) idx = 0;
					combIdx[j] = idx;
					acc += r;
				}
				acc /= nc;
				for ( int j = 0; j < na; j++ )
				{
					var ab = apBuf[j];
					int idx = apIdx[j];
					float r = ab[idx];
					float o = r - acc;
					ab[idx] = acc + r * apg;
					if ( ++idx >= ab.Length ) idx = 0;
					apIdx[j] = idx;
					acc = o;
				}
				buf[i] += wet * acc;
			}
		}
	}
}
