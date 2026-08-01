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
	// Level, like the bass's, is measured rather than guessed (`--levels`): punk's constant
	// downstrokes put 5 dB more energy into the mix than rock's placed riff at the same balance,
	// which is exactly the "the backing is too loud" the comp rewrite exposed. The comp is meant
	// to sit UNDER the kit — it is the bed, not the song.
	(float Drive, float CutEnv, float Reso, float Level) RhythmGtrTone() => _genre switch
	{
		2 => (0.8f + 0.3f * MathF.Max( 1f, _c.RhythmGtrDrive ), 2600f, 0.8f, 0.55f), // country strum
		3 => (4f + MathF.Max( 1f, _c.RhythmGtrDrive ), 1100f, 0.7f, 0.80f),          // metal riff
		4 => (2.2f + MathF.Max( 1f, _c.RhythmGtrDrive ), 2000f, 0.75f, 0.35f),       // punk downstrokes
		_ => (1.5f + MathF.Max( 1f, _c.RhythmGtrDrive ), 1400f, 0.8f, 0.50f),        // rock riff
	};

	/// <summary>Effective drive at which the rhythm guitar stops playing thirds. Rock reaches this
	/// a notch above its DISTORTION minimum, punk and metal are always past it, and country's clean
	/// strum never is — which is the line a real player draws too.</summary>
	const float DirtyChord = 3f;

	/// <summary>The chord the RHYTHM GUITAR plays: the song's voicing, minus the third once the amp
	/// is driven.
	///
	/// A THIRD THROUGH HEAVY GAIN IS NOT A CHORD, IT IS A BEAT NOTE. Distortion is a non-linearity,
	/// so it generates sum and difference tones between everything fed into it; a root and a fifth
	/// are a simple enough ratio to survive that, and a third is not. This is the whole reason
	/// electric guitarists play power chords through a driven amp and full triads through a clean
	/// one, and playing a rock triad at DISTORTION 4 read exactly the way it reads on a real amp —
	/// "something is out of tune".
	///
	/// The SONG's voicing is untouched (every chordal voice still agrees what the chord IS — see
	/// ComposePlan): this drops a note on the way out of one voice. A sus4 or a power chord has no
	/// third and passes through whole, which is why those voicings work driven in the first place.
	/// The keys keep the third, so the band still states the quality — guitar on the fifths,
	/// keyboard on the colour, which is how the parts are actually divided.</summary>
	int[] GuitarDegrees( int chord )
	{
		var degs = ChordDegrees( chord );
		if ( RhythmGtrTone().Drive < DirtyChord ) return degs;
		var keep = new List<int>( degs.Length );
		for ( int i = 0; i < degs.Length && i < _voicing.Length; i++ )
			if ( _voicing[i] != Harmony.Third ) keep.Add( degs[i] );
		return keep.Count > 0 ? keep.ToArray() : degs;
	}

	/// <summary>One guitar note. Everything above funnels through here so the accent/energy
	/// scaling (and the genre's mix trim) is applied in exactly one place.</summary>
	/// <param name="nudgeMs">A sub-musical offset in MILLISECONDS, for the strum spread. It is not
	/// a tick offset: a tick is a musical unit and scales with tempo, so a "couple of ticks per
	/// string" is 5 ms at metal tempo and 25 ms at country's — which is a guitar audibly out of
	/// time, not a strum. Anything that is a physical gesture rather than a musical position
	/// belongs in real time (the kit's push/lay-back is in samples for the same reason).</param>
	void EmitGuitar( int tick, int durTicks, int midi, float vel, bool ring, int voices,
		float nudgeMs = 0f )
	{
		var (drive, cutEnv, reso, level) = RhythmGtrTone();
		int dur = _time.SpanSamples( tick, durTicks );
		double dec = _time.SpanSeconds( tick, durTicks ) * (ring ? 0.8 : 0.3);
		int at = _time.TickToSample( tick ) + (int)(nudgeMs * 0.001f * _sr);
		RenderPatch( at, dur, Midi( midi ), new Patch
		{
			Osc = 1, Voices = 2, Detune = _c.Detune * 0.5f,
			Amp = _c.RhythmGtrVol * _c.RhythmGtrBalance * level * _midMul / Math.Max( 1, voices )
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
		var degs = GuitarDegrees( chord );
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
			int len = (int)(CompLen( h.SpanTicks, h.Value == CompFigure.Ring )
				* (h.Value == CompFigure.Ring ? 1f - 0.4f * chug : 0.9f));
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
		var degs = GuitarDegrees( chord );
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(CompLen( h.SpanTicks, true ) * (0.7f - 0.4f * chug)) );
			// A strum is not a block chord: the strings sound in sequence. ~4 ms per string is a
			// pick crossing them — enough for the ear to hear the gesture, short enough that the
			// chord still lands on the beat.
			for ( int i = 0; i < degs.Length; i++ )
				EmitGuitar( h.Tick, len, ScaleMidi( triBase, degs[i] ), h.Vel, true, degs.Length,
					nudgeMs: i * 4f );
		}
	}

	// ── Punk: downstrokes ──
	// Every hit is the same hit, hard and short, one chord per bar. The genre's whole rhythmic
	// idea is that nothing varies, so the only shaping is the accent pattern underneath.
	void RenderDownstrokeBar( List<Hit> hits, int chord, Rng rng, Rng exprRng )
	{
		float chug = Math.Clamp( _c.RhythmGtrChug, 0f, 1f );
		int root = ChordRoot( chord ) + 12;
		var degs = GuitarDegrees( chord );
		int triBase = _rootMidi + _keyShift + 12;
		foreach ( var h in hits )
		{
			int len = Math.Max( 1, (int)(CompLen( h.SpanTicks, true ) * (0.85f - 0.35f * chug)) );
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
		var degs = GuitarDegrees( chord );
		int triBase = _rootMidi + _keyShift;
		foreach ( var h in hits )
		{
			if ( h.Value == CompFigure.Mute )
			{
				EmitGuitar( h.Tick, Math.Max( 1, (int)(h.SpanTicks * Math.Max( 0.25f, 0.55f - 0.3f * chug )) ),
					root, h.Vel * 0.6f, false, 1 );
				continue;
			}
			int len = Math.Max( 1, (int)(CompLen( h.SpanTicks, true ) * 0.9f) );
			foreach ( var d in degs )
				EmitGuitar( h.Tick, len, ScaleMidi( triBase, d ), h.Vel, true, degs.Length );
		}
	}
}
