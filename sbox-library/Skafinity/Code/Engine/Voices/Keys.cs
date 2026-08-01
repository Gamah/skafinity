using System;
using System.Collections.Generic;

using static Skafinity.Osc;

namespace Skafinity;

// The keyboard voices: rock's syncopated organ comp, country's honky-tonk piano, pop's pad and
// its sixteenth arpeggio. One voice, four grammars — the figure comes from the genre's table.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// The keys' timbre, per genre — next to the voice, because it is the sound of an instrument
	// rather than the identity of a genre. Rock drives its organ dirty; country's piano and pop's
	// synth stay clean and bright.
	float KeysDriveFor() => _genre == 2 || _genre == 5
		? 1f + 0.2f * MathF.Max( 1f, _c.KeysDrive )
		: MathF.Max( 1f, _c.KeysDrive );

	/// <summary>Per-genre level (measured with `--levels`). Pop's pad is a held chord and pours
	/// far more energy into the mix than country's stabs at the same balance.</summary>
	float KeysLevel() => _genre == 5 ? 0.75f : 1f;

	void EmitKeys( int tick, int durTicks, int midi, float vel, bool ring, int voices, Voicing vc )
	{
		int dur = _time.SpanSamples( tick, durTicks );
		double dec = _time.SpanSeconds( tick, durTicks ) * (ring ? 0.9 : 0.4);
		var keys = new Patch
		{
			Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
			Amp = _c.KeysVol * _c.KeysBalance * KeysLevel() * _midMul / Math.Max( 1, voices ) * NoteGain( tick, vel ),
			Attack = 0.004f, Decay = dec, Sustain = ring ? 0.6f : 0.2f, Sustained = ring,
			Cutoff = _c.KeysCutoff, CutEnv = 250f, Reso = 1.0f,
			Drive = KeysDriveFor(), Pan = 0f,
		};
		ApplyVoicing( ref keys, vc );
		RenderPatch( _time.TickToSample( tick ), dur, Midi( midi ), keys );
	}

	// ── Rock keys: the Charleston stab ──
	// Its own syncopation against the guitar's riff, an octave above it, playing the full voicing
	// where the guitar plays a power chord — different notes AND a different rhythm, so the two
	// interlock instead of doubling.
	void RenderKeysStabBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		int kBase = _rootMidi + _keyShift + 24;
		var degs = ChordDegrees( chord );
		float chug = Math.Clamp( _c.KeysChug, 0f, 1f );
		bool ring = chug < 0.5f;
		var vc = Roll( Expr( "KEYS" ), 0, NoPrev, exprRng );
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(CompLen( h.SpanTicks, ring ) * Math.Max( 0.25f, 1f - 0.7f * chug )) );
			foreach ( var d in degs )
				EmitKeys( h.Tick, len, ScaleMidi( kBase, d ), h.Vel, ring, degs.Length, vc );
		}
	}

	// ── Country keys: honky-tonk piano ──
	// It answers the backbeat rather than keeping time — short two-handed stabs on 2 and 4, with
	// the root doubled an octave down the way a piano player's left hand does.
	void RenderHonkyTonkBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		int kBase = _rootMidi + _keyShift + 24;
		var degs = ChordDegrees( chord );
		var vc = Roll( Expr( "KEYS" ), 0, NoPrev, exprRng );
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(CompLen( h.SpanTicks, false ) * 0.9f) );
			foreach ( var d in degs )
				EmitKeys( h.Tick, len, ScaleMidi( kBase, d ), h.Vel, false, degs.Length + 1, vc );
			EmitKeys( h.Tick, len, ScaleMidi( kBase, degs[0] ) - 12, h.Vel * 0.8f, false,
				degs.Length + 1, vc );
		}
	}

	// ── Pop: the pad ──
	// One hit a bar, held. The harmony is a bed here rather than a rhythm part, which is exactly
	// what lets the arp on top be the moving voice.
	void RenderPadBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		int kBase = _rootMidi + _keyShift + 24;
		var degs = ChordDegrees( chord );
		float pluck = Math.Clamp( _c.KeysChug, 0f, 1f );
		var vc = Roll( Expr( "KEYS" ), 0, NoPrev, exprRng );
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(h.SpanTicks * Math.Max( 0.2f, 1f - 0.75f * pluck )) );
			foreach ( var d in degs )
				EmitKeys( h.Tick, len, ScaleMidi( kBase, d ), h.Vel * 0.85f, pluck < 0.5f,
					degs.Length, vc );
		}
	}

	// ── Pop: the sixteenth arp ──
	// Single tones climbing the voicing, so an add9 arp reaches the 9th without the figure
	// knowing what a 9th is (the cell names a voice index; the song's voicing says what that is).
	void RenderArpBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		int kBase = _rootMidi + _keyShift + 24;
		var vc = Roll( Expr( "KEYS" ), 0, NoPrev, exprRng );
		foreach ( var h in hits )
		{
			if ( !CompFigure.IsTone( h.Value ) ) continue;
			int len = Math.Max( 1, (int)(h.SpanTicks * 0.9f) );
			EmitKeys( h.Tick, len, ScaleMidi( kBase, ChordDegree( chord, CompFigure.ToneIndex( h.Value ) ) ),
				h.Vel * 0.7f, false, 1, vc );
		}
	}
}
