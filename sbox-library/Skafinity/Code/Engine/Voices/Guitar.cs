using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The guitar comps: the rock riff, the country strum, the punk downstroke and the metal gallop.
// Each plays its genre's own figure (see CompFigure) — what they share is the voice, not the
// rhythm.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// The rhythm guitar's timbre, per genre. This is the part that belongs NEXT TO THE VOICE
	// rather than in GenreProfile: it is the sound of one instrument, not the identity of the
	// genre. Country is a clean bright strum, rock an overdriven chunk, punk brighter and
	// snottier, metal heavy and tight.
	(float Drive, float CutEnv, float Reso) RhythmGtrTone() => _genre switch
	{
		2 => (0.8f + 0.3f * MathF.Max( 1f, _c.RhythmGtrDrive ), 2600f, 0.8f),
		3 => (4f + MathF.Max( 1f, _c.RhythmGtrDrive ), 1100f, 0.7f),
		4 => (2.2f + MathF.Max( 1f, _c.RhythmGtrDrive ), 2000f, 0.75f),
		_ => (1.5f + MathF.Max( 1f, _c.RhythmGtrDrive ), 1400f, 0.8f),
	};

	/// <summary>One guitar note. Everything above funnels through here so the accent/energy
	/// scaling (and the genre's mix trim) is applied in exactly one place.</summary>
	void EmitGuitar( int tick, int durTicks, int midi, float vel, bool ring, int voices )
	{
		var (drive, cutEnv, reso) = RhythmGtrTone();
		int dur = _time.SpanSamples( tick, durTicks );
		double dec = _time.SpanSeconds( tick, durTicks ) * (ring ? 0.8 : 0.3);
		RenderPatch( _time.TickToSample( tick ), dur, Midi( midi ), new Patch
		{
			Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
			Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance * _midMul / Math.Max( 1, voices )
				* NoteGain( tick, vel ),
			Attack = 0.002f, Decay = dec, Sustain = ring ? 0.45f : 0f, Sustained = ring,
			Cutoff = _c.RhythmGtrCutoff, CutEnv = cutEnv, Reso = reso,
			Drive = drive, Pan = 0f,
		} );
	}

	// ── Rock: a two-bar riff motif ──
	// Placed hits that ring, not an every-eighth chug. RhythmGtrChug shortens the ringing hits
	// toward a palm mute; the figure decides WHERE they are.
	void RenderRiffBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		int root = ChordRoot( chord ) + 12;               // chunky register, an octave up
		var degs = ChordDegrees( chord );
		int triBase = _rootMidi + _keyShift + 12;
		foreach ( var h in hits )
		{
			bool ring = h.Value != CompFigure.Mute;
			if ( h.Value == CompFigure.Mute )
			{
				EmitGuitar( h.Tick, Math.Max( 1, (int)(h.SpanTicks * (0.5f - 0.25f * chug)) ),
					root, h.Vel * 0.65f, false, 1 );
				continue;
			}
			int len = (int)(h.SpanTicks * (h.Value == CompFigure.Ring ? 1f - 0.4f * chug : 0.45f));
			foreach ( var d in degs )
				EmitGuitar( h.Tick, Math.Max( 1, len ), ScaleMidi( triBase, d ), h.Vel, ring, degs.Length );
		}
	}

	// ── Country: the "chick" ──
	// A clean strum of the full voicing on the offbeats, over the bass's "boom". Bright, short
	// and never distorted — the genre's bite comes from the twang, not from gain.
	void RenderStrumBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		int triBase = _rootMidi + _keyShift + 12;
		var degs = ChordDegrees( chord );
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(h.SpanTicks * (0.7f - 0.4f * chug)) );
			// A strum is not a block chord: the strings sound in sequence. A couple of ticks per
			// string is enough for the ear to hear a pick moving across them.
			for ( int i = 0; i < degs.Length; i++ )
				EmitGuitar( h.Tick + i * 2, len, ScaleMidi( triBase, degs[i] ), h.Vel, true, degs.Length );
		}
	}

	// ── Punk: downstrokes ──
	// Every hit is the same hit, hard and short, one chord per bar. The genre's whole rhythmic
	// idea is that nothing varies, so the only shaping is the accent pattern underneath.
	void RenderDownstrokeBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		int root = ChordRoot( chord ) + 12;
		var degs = ChordDegrees( chord );
		int triBase = _rootMidi + _keyShift + 12;
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(h.SpanTicks * (0.85f - 0.35f * chug)) );
			foreach ( var d in degs )
				EmitGuitar( h.Tick, len, ScaleMidi( triBase, d ), h.Vel, true, degs.Length );
		}
	}

	// ── Metal: the palm-muted gallop ──
	// The figure is authored at the sixteenth. Muted cells are the low root with no chord at all;
	// ring cells open into the power chord. That contrast IS the riff.
	void RenderGallopBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		int root = ChordRoot( chord );                    // low, chunky — no octave bump
		var degs = ChordDegrees( chord );
		int triBase = _rootMidi + _keyShift;
		foreach ( var h in hits )
		{
			if ( h.Value == CompFigure.Mute )
			{
				EmitGuitar( h.Tick, Math.Max( 1, (int)(h.SpanTicks * Math.Max( 0.25f, 0.55f - 0.3f * chug )) ),
					root, h.Vel * 0.6f, false, 1 );
				continue;
			}
			int len = Math.Max( 1, (int)(h.SpanTicks * 0.9f) );
			foreach ( var d in degs )
				EmitGuitar( h.Tick, len, ScaleMidi( triBase, d ), h.Vel, true, degs.Length );
		}
	}
}
