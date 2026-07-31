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

	// The song's room, from the two Config knobs that shape it — trimmed by the genre's own mix
	// profile. A room is part of what a genre sounds like: ska is recorded in one, metal is
	// recorded dry and close, country sits in between and centred. The REVERB knob still rides on
	// top; the trim moves what "0.5" means for this genre.
	void ApplyReverb()
	{
		float wet = Math.Clamp( _c.MasterReverb * MixTrim( _prof.Mix.Reverb ), 0f, 1f );
		if ( wet <= 0.0001f ) return;
		float feedback = 0.70f + 0.28f * Math.Clamp( _c.ReverbDecay, 0f, 1f ); // tail length
		Reverb.Process( _bufL, 0, wet, feedback, _sr );
		Reverb.Process( _bufR, Reverb.StereoSpread, wet, feedback, _sr );
	}
}
