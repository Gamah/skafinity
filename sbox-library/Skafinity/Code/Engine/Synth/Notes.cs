using System;
using System.Collections.Generic;

namespace Skafinity;

// The pitched-note queue. Composition only enqueues; synthesis happens later in
// RenderPitchedRange, which is what lets the render be chunked across workers.
//
// Part of the MusicGen engine — see MusicGen.cs.

// Pitched note events collected during ComposePlan, then synthesized by
// RenderPitchedRange. Synthesis pulls no RNG, so windows parallelize across threads.
struct NoteEvent { public int Start, Dur; public float Freq; public Patch P; }

public sealed partial class MusicGen
{
	readonly List<NoteEvent> _events = new();

	// During ComposePlan this only enqueues; the synthesis happens in RenderPitchedRange.
	// When DoubleTrack is on, every (non-drum) note is widened into two decorrelated takes
	// panned apart — see the Config "width" block. `lead` selects the wider lead spread.
	void RenderPatch( int start, int dur, float freq, Patch p, bool lead = false, bool mono = false )
	{
		if ( start < 0 || dur <= 0 || p.Voices < 1 ) return;
		// Bass stays centred (mono): doubling/detuning low frequencies smears the low end and
		// cancels in mono, so the bass is the one voice kept dead-centre regardless of width.
		if ( mono || _c.DoubleTrack < 0.5f )
		{
			_events.Add( new NoteEvent { Start = start, Dur = dur, Freq = freq, P = p } );
			return;
		}
		// The STEREO WIDTH slider (_widthScale) scales the whole effect: pan AND the
		// decorrelation (detune / delay / jitter / phase / per-note variation, all scaled inside
		// AddTake). At 100% it's the full design width; at 0% both takes collapse onto each other
		// at centre → effectively mono.
		float width = Math.Clamp( lead ? _c.WidthLead : _c.WidthBacking, 0f, 1f ) * _widthScale;
		float halfCents = _c.WidthDetune * 0.5f * _widthScale;
		int delay = (int)(_c.WidthDelayMs * 0.001f * _sr * _widthScale);
		int jit = (int)(_c.WidthJitterMs * 0.001f * _sr * _widthScale);
		// Take A sits left and slightly flat; take B sits right, slightly sharp, and lags by the
		// constant offset. Both get independent phase + per-note amp/cutoff/timing variation, so
		// the two channels are genuinely different performances (the source of the width) rather
		// than a phantom-centre mono copy.
		AddTake( start, dur, freq, p, 0, -width, -halfCents, 0, jit );
		AddTake( start, dur, freq, p, 1, +width, +halfCents, delay, jit );
	}

	// Enqueue one double-tracking take. All per-note variation is derived from a local hash of
	// (start, freq, take) so it stays deterministic and pulls no RNG (synthesis must remain a
	// pure function for the parallel RenderPitchedRange windows).
	void AddTake( int start, int dur, float freq, Patch p, int take, float pan, float cents, int delayBase, int jit )
	{
		uint s = unchecked( (uint)start * 2654435761u + (uint)(int)freq * 40503u )
			^ (take == 0 ? 0x85EBCA6Bu : 0xC2B2AE35u);
		float r1 = Hash01( ref s );   // start phase
		float r2 = Hash01( ref s );   // amplitude
		float r3 = Hash01( ref s );   // cutoff
		float r4 = Hash01( ref s );   // timing jitter
		int st = start + delayBase + (int)((r4 * 2f - 1f) * jit);
		if ( st < 0 ) st = 0;
		var q = p;
		q.Pan = pan;
		// Phase + per-note variation also scale with the width slider, so at 0% the two takes
		// become identical and centred (clean mono) instead of a flangy centred double.
		q.PhaseSeed = r1 * _widthScale;
		q.Amp = p.Amp * (1f + (r2 * 2f - 1f) * _c.WidthAmpVar * _widthScale);
		q.Cutoff = p.Cutoff * (1f + (r3 * 2f - 1f) * _c.WidthCutoffVar * _widthScale);
		float f = freq * (float)Math.Pow( 2.0, cents / 1200.0 );
		_events.Add( new NoteEvent { Start = st, Dur = dur, Freq = f, P = q } );
	}

	// Fast deterministic [0,1) hash step (xorshift32).
	static float Hash01( ref uint s )
	{
		s ^= s << 13; s ^= s >> 17; s ^= s << 5;
		return (s & 0xFFFFFFu) / 16777216f;
	}
}
