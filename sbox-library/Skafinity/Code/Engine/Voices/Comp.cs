using System;
using System.Collections.Generic;

namespace Skafinity;

// The chordal layer's dispatch: the genre's comp figure, played the genre's way.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>The main chordal voice for a bar. The FIGURE comes from the genre's table (a
	/// pattern with its own length, so a two-bar riff is a two-bar riff) and the STYLE says what
	/// the voice does with each hit. Three genres sharing one comping rhythm was the loudest
	/// duplication left in the engine — this is the seam that fixes it.</summary>
	/// <param name="loud">The section is loud enough that the genre changes technique — see
	/// <see cref="GenreProfile.LoudComp"/>. One voice, one chord, a different instrument gesture;
	/// the caller has already picked the matching figure.</param>
	void RenderCompVoice( int barTick, int to, int chord, Pattern fig, Rng rng, Rng exprRng,
		bool loud = false )
	{
		var hits = fig.Slice( barTick, to, _sectionTick, _feel );
		// Remember what the riff played: where the bass doubles it (metal, and punk's unison
		// option) it reads these onsets rather than a table of its own.
		_riffOnsets.AddRange( hits );

		switch ( loud ? _prof.LoudComp : _prof.Comp )
		{
			case CompStyle.Riff: RenderRiffBar( hits, chord, rng, exprRng ); break;
			case CompStyle.BoomChick: RenderStrumBar( hits, chord, rng, exprRng ); break;
			case CompStyle.Downstroke: RenderDownstrokeBar( hits, chord, rng, exprRng ); break;
			case CompStyle.Gallop: RenderGallopBar( hits, chord, rng, exprRng ); break;
			case CompStyle.Pad: RenderPadBar( hits, chord, rng, exprRng ); break;
			default: RenderSkankBar( hits, chord, rng, exprRng ); break;
		}
	}

	/// <summary>How long a comp hit rings.
	///
	/// NOT simply "until the next onset". A figure with uneven gaps then produces a note with an
	/// uneven length every single bar — the "short, longggg" shape that made the backing read as
	/// one repeated cell however varied the figure was. A chord rings for up to two beats and a
	/// stab is a stab; past that the voice is silent and the next hit lands into space, which is
	/// what a played part actually sounds like.</summary>
	static int CompLen( int spanTicks, bool ring )
		=> Math.Max( 1, Math.Min( spanTicks, ring ? Timing.TicksPerBeat * 2 : Timing.TicksPerEighth ) );

	/// <summary>The second chordal voice — the keys/piano/synth layer, where the genre has one.
	/// It never doubles the main voice: rock's organ answers the riff's gaps, country's piano
	/// hits the backbeat the guitar leaves alone, pop's arp moves over a pad that does not.
	/// </summary>
	void RenderKeysVoice( int barTick, int to, int chord, Rng rng, Rng exprRng )
	{
		var hits = _keysFig.Slice( barTick, to, _sectionTick, _feel );
		switch ( _prof.Keys )
		{
			case KeysStyle.HonkyTonk: RenderHonkyTonkBar( hits, chord, rng, exprRng ); break;
			case KeysStyle.Arp: RenderArpBar( hits, chord, rng, exprRng ); break;
			default: RenderKeysStabBar( hits, chord, rng, exprRng ); break;
		}
	}
}
