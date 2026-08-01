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
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	void RenderKick( int start, Rng noise )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		int dur = (int)(_sr * 0.17f);          // a little longer tail for thump (was 0.13)
		double decay = dur * 0.31;             // slightly slower decay = a touch more boom
		double subDecay = dur * 0.55;          // sub layer rings longer for weight
		double phase = 0, subPhase = 0;
		// noise.Next() only fires in the fixed 3ms click below, so changing dur/decay
		// here does NOT shift the drum RNG stream (patterns are preserved).
		int clickLen = (int)(_sr * 0.003f);
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			phase += (127f - 80f * MathF.Min( 1f, t * 2.6f )) / _sr;   // pitch drop 127→47
			subPhase += 44f / _sr;                                     // steady sub fundamental
			float env = (float)Math.Exp( -i / decay );
			float subEnv = (float)Math.Exp( -i / subDecay );
			float body = (float)Math.Tanh( MathF.Sin( (float)(phase * 2 * Math.PI) ) * 1.6f ) * env;
			float sub = MathF.Sin( (float)(subPhase * 2 * Math.PI) ) * 0.3f * subEnv;
			float click = i < clickLen ? (noise.Next() * 2f - 1f) * 0.55f * (1f - i / (float)clickLen) : 0f;
			float v = (body + sub + click) * _c.KickVol * _c.KickBalance * _drumGain * _drumLowMul;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	// One-pole high-pass coefficient (unconditionally stable).
	float HpCoeff( float fc ) => (float)(1.0 / (1.0 + 2 * Math.PI * fc / _sr));

	void RenderSnare( int start, Rng noise, bool ghost )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// dur and the single noise.Next()/sample are kept exactly so the drum RNG stream
		// is unchanged — only the timbre (more shell body) is rerolled.
		int dur = (int)(_sr * (ghost ? 0.06f : 0.15f));
		double decay = dur * (ghost ? 0.3 : 0.32);
		double phase = 0, phase2 = 0;
		float amp = _c.SnareVol * _c.SnareBalance * (ghost ? 0.3f : 1f) * _drumGain;
		float a = HpCoeff( 1350f );           // slightly crisper wire crack (was 1200)
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			float env = (float)Math.Exp( -i / decay );
			float drop = 1f - 0.14f * t;       // shell pitch sags a touch → "dow"
			phase += 185f * drop / _sr;
			phase2 += 268f * drop / _sr;
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			// two-tone shell body, a bit fuller than before, vs the wire layer
			float body = (MathF.Sin( (float)(phase * 2 * Math.PI) ) + MathF.Sin( (float)(phase2 * 2 * Math.PI) ) * 0.6f) * 0.375f;
			float v = ((float)Math.Tanh( hp * 1.2f ) * 0.6f + body) * env * amp;
			_bufL[start + i] += v; _bufR[start + i] += v;
		}
	}

	void RenderTom( int start, float baseFreq, Rng noise )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// Pan by PITCH so a given tom always sits in the same spot (rack/high left → floor/low
		// right). Mapped across the commonly-played fill range (~145 Hz floor .. 260 Hz rack) so a
		// normal fill sweeps the full field and the low toms read clearly to the right; anything
		// below the range clamps hard right. Scaled by the STEREO WIDTH slider via _drumPan.
		float u = Math.Clamp( (baseFreq - 145f) / (260f - 145f), 0f, 1f ); // 0 = low/floor, 1 = high/rack
		StereoGains( -_drumPan * (u * 2f - 1f), out float gL, out float gR );
		int dur = (int)(_sr * 0.18f);
		double decay = dur * 0.3;
		double attackDecay = dur * 0.06;        // fast-decaying upper partial → beater "snap"
		double phase = 0, phase2 = 0;
		// deterministic per-hit stick attack — a local LFSR so the shared drum RNG stream
		// (and therefore every other pattern) is left byte-identical.
		uint ns = (uint)start * 2654435761u | 1u;
		int clickLen = (int)(_sr * 0.006f);
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float t = (float)i / dur;
			float pf = baseFreq * (1f - 0.22f * t);   // less pitch sag → holds its note, more bite
			phase += pf / _sr;
			phase2 += pf * 2.5f / _sr;                 // inharmonic upper partial for attack snap
			float env = (float)Math.Exp( -i / decay );
			float aenv = (float)Math.Exp( -i / attackDecay );
			float body = MathF.Sin( (float)(phase * 2 * Math.PI) ) * env;
			float snap = MathF.Sin( (float)(phase2 * 2 * Math.PI) ) * aenv * 0.5f;
			float click = 0f;
			if ( i < clickLen )
			{
				ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
				click = ((ns & 0xffff) / 32768f - 1f) * 0.45f * (1f - i / (float)clickLen);
			}
			float v = (body + snap + click) * _c.TomVol * _c.TomBalance * _drumGain * _drumLowMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	void RenderHat( int start, bool open, float amp, Rng noise )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// Closed/open hats live on the left of the kit (the hi-hat stand); ride sits opposite.
		StereoGains( -_drumPan, out float gL, out float gR );
		int dur = (int)(_sr * (open ? 0.16f : 0.035f));
		double decay = dur * 0.4;
		float a = HpCoeff( 7000f );
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env * amp * _c.HatBalance * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	// Two crash colours off one voice: the bright crash (short-ish, high-passed high) and a
	// dark crash — lower cutoff, longer wash, a touch quieter — for a bigger china/ride-crash.
	// The kit has two crashes panned apart (±25%); the bright/dark colour picks which physical
	// cymbal, and _crashBrightLeft (per song) decides which side that is.
	void RenderCrash( int start, Rng noise, bool dark = false )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		StereoGains( (dark == _crashBrightLeft ? _drumPan : -_drumPan), out float gL, out float gR );
		int dur = (int)(_sr * (dark ? 0.9f : 0.6f));
		double decay = dur * (dark ? 0.5 : 0.45);
		float a = HpCoeff( dark ? 2600f : 4000f );
		float amp = _c.CrashVol * _c.CrashBalance * (dark ? 0.85f : 1f);
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * env * amp * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}

	// Ride cymbal — a sustained, high-passed noise wash, nothing more. A cymbal is mostly
	// filtered noise; we deliberately render *only* that body and no pitched component. Earlier
	// versions layered inharmonic sine partials for a metallic "ping"/bell, but any tonal layer
	// (however quiet or detuned) read as a pitched ring/ding, so it's gone entirely. The bell
	// hit (on the beat) just rings a touch longer than the bow hit (on the "and"). Tracks off
	// HatVol via the caller's amp so the existing DRUMS knobs still balance it.
	void RenderRide( int start, bool bell, float amp, Rng noise )
	{
		if ( _drumGain <= 0f ) return;
		start = Math.Max( 0, start + _time.DrumPush );
		// Ride sits opposite the hats, on the right of the kit.
		StereoGains( _drumPan, out float gL, out float gR );
		int dur = (int)(_sr * (bell ? 0.34f : 0.22f));
		double decay = dur * 0.42;
		float a = HpCoeff( 7000f );
		float inPrev = 0f, outPrev = 0f;
		int end = Math.Min( _bufL.Length, start + dur );
		for ( int i = 0; start + i < end; i++ )
		{
			float env = (float)Math.Exp( -i / decay );
			float n = noise.Next() * 2f - 1f;
			float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
			float v = hp * 0.7f * env * amp * _c.HatBalance * _drumGain * _drumHighMul;
			_bufL[start + i] += v * gL; _bufR[start + i] += v * gR;
		}
	}
}
