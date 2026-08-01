using System;

namespace Skafinity;

/// <summary>
/// Master room reverb — a Schroeder/Freeverb-style bank: several parallel damped comb filters
/// (the dense tail) feeding a chain of allpasses (diffusion). The two channels use slightly
/// different delay lengths so the room is decorrelated, which is what gives it real stereo
/// width and depth rather than a mono wash in both ears.
///
/// Stateless between calls: <see cref="Process"/> allocates its own delay lines, runs one
/// channel start to finish, and drops them. The dry mix alone read flat/"16-bit".
/// </summary>
static class Reverb
{
	// Freeverb's full tuning. The bank was six combs and three allpasses, which is what made a
	// long tail read as a flutter ECHO rather than a room: at 25–34 ms apart, six repeats per
	// pass is sparse enough for the ear to count them. Density is what separates a reverb from a
	// delay line, and it costs one multiply-add per sample per stage.
	static readonly int[] CombBase = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 }; // @ 44.1k
	static readonly int[] ApBase = { 556, 441, 341, 225 };

	/// <summary>R-channel delay offset, in samples, that decorrelates the two rooms.</summary>
	public const int StereoSpread = 23;

	const float ApGain = 0.5f;   // allpass coefficient
	const float InGain = 0.25f;  // drive into the reverb

	/// <summary>Add the wet room onto <paramref name="buf"/> in place.</summary>
	/// <param name="delayOffset">Per-channel delay offset — 0 for L, <see cref="StereoSpread"/>
	/// for R.</param>
	/// <param name="wet">Wet mix, 0..1.</param>
	/// <param name="feedback">Comb feedback — the tail length.</param>
	/// <param name="damp">HF damping in the tail, 0..1. A long tail has to get DARKER as it
	/// decays or the top end accumulates instead of dying, which is the metallic ring a bright
	/// long room has; the caller scales this with the tail length.</param>
	/// <param name="sampleRate">Delay lengths are authored at 44.1k and scaled to this.</param>
	public static void Process( float[] buf, int delayOffset, float wet, float feedback, float damp,
		int sampleRate )
	{
		damp = Math.Clamp( damp, 0f, 0.95f );
		float damp1 = 1f - damp;
		double srk = sampleRate / 44100.0;

		int nc = CombBase.Length, na = ApBase.Length;
		var combBuf = new float[nc][];
		var combIdx = new int[nc];
		var combStore = new float[nc];
		for ( int j = 0; j < nc; j++ )
			combBuf[j] = new float[Math.Max( 1, (int)Math.Round( (CombBase[j] + delayOffset) * srk ) )];
		var apBuf = new float[na][];
		var apIdx = new int[na];
		for ( int j = 0; j < na; j++ )
			apBuf[j] = new float[Math.Max( 1, (int)Math.Round( (ApBase[j] + delayOffset) * srk ) )];

		int n = buf.Length;
		for ( int i = 0; i < n; i++ )
		{
			float input = buf[i] * InGain;
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
				ab[idx] = acc + r * ApGain;
				if ( ++idx >= ab.Length ) idx = 0;
				apIdx[j] = idx;
				acc = o;
			}
			buf[i] += wet * acc;
		}
	}
}
