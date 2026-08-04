using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>Which part an onset belongs to. Only the voices whose PLACEMENT is a composed
/// decision are here — the busy layer's ghosts and the kick-sync stray are per-bar rolls on top
/// of a groove rather than a part anyone wrote, and counting them would put a knob's setting into
/// a measurement of what the genre plays.</summary>
enum TraceVoice { Kick, Snare, Cymbal, Bass, Comp, Keys, Tune }

/// <summary>
/// Every onset a song composed, per voice, in TICKS.
///
/// This exists so a sweep over hundreds of songs can ask what the parts actually played without
/// re-deriving it. The alternative — walking the structure a second time and slicing the same
/// patterns — is a SECOND IMPLEMENTATION of every precedence rule in the composer (which figure a
/// bar plays: hemiola, then the loud figure, then the flourish, then the section's own; whether
/// the bass reads the riff; whether a section sings a tune at all), and two implementations of
/// that drift. When they drift the tool starts lying with a straight face, which is worse than
/// not having it — so the onsets are recorded by the voices themselves, at the moment they play,
/// and there is exactly one answer to what a bar played.
///
/// Attach one with <see cref="MusicGen.Trace"/> before composing. Null (the default) costs a
/// null check per bar per voice and nothing else.
/// </summary>
sealed class PlanTrace
{
	const int Voices = 7;

	readonly List<int>[] _ticks = new List<int>[Voices];

	public PlanTrace()
	{
		for ( int i = 0; i < Voices; i++ ) _ticks[i] = new List<int>();
	}

	/// <summary>The onsets one voice played, in composition order (which is bar order).</summary>
	public List<int> Of( TraceVoice v ) => _ticks[(int)v];

	public void Add( TraceVoice v, int tick ) => _ticks[(int)v].Add( tick );

	/// <summary>Record a bar's worth of sliced hits.</summary>
	public void Add( TraceVoice v, List<Hit> hits )
	{
		var list = _ticks[(int)v];
		for ( int i = 0; i < hits.Count; i++ ) list.Add( hits[i].Tick );
	}
}
