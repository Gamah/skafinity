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
		_compTrim = DensityTrim( fig, loud ? _prof.LoudCompFigures : _prof.CompFigures );
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

	/// <summary>
	/// How far the drawn comp figure is trimmed for its own DENSITY, relative to the genre's table.
	///
	/// A section draws its figure from the genre's table and the figures are not equally dense, so
	/// how loud the backing sits was decided by a draw: rock's rhythm guitar measured 4 dB apart
	/// between the seed the suite balances on and the genre's own average. Nothing was watching it —
	/// <c>--levels</c> averages the spread away and the suite's balance check is a single seed —
	/// and 4 dB is a larger move than any of the balances it sits under, handed out at random.
	///
	/// The trim is EXACTLY the arithmetic part of that and no more. N onsets of an unrelated-phase
	/// voice sum incoherently, so the level of a figure grows as √N: the trim is therefore
	/// √(table mean ÷ this figure), which cancels that growth and nothing else. What survives is
	/// everything MUSICAL about the difference — the cells' own velocities, the genre's accent
	/// weights, the section's energy, and the plain fact that a busier bar has more attacks in it.
	/// A full compensation (exponent 1) would flatten the busy figure into the sparse one, which is
	/// the mistake the other repair — levelling the figures against each other — makes by design.
	///
	/// AND IT IS MEAN-PRESERVING, which is the difference between narrowing a spread and moving a
	/// balance. √(mean ÷ d) is convex in d, so averaged over a genre's table it comes out above 1
	/// and every genre's comp would drift a little louder — a mix change smuggled in with a spread
	/// fix, and the `*Balance` values are measured numbers that would then be wrong. Dividing by the
	/// table's own average trim leaves the genre exactly where `--levels` measured it and moves only
	/// the seed-to-seed variation, which is the whole and only claim.
	///
	/// Clamped, because a table with one outlier figure should not push the whole genre's comp
	/// around, and because a trim is a correction rather than a mix control.
	/// </summary>
	internal static float DensityTrim( Pattern fig, Pattern[] table )
	{
		if ( table == null || table.Length < 2 ) return 1f;
		float d = fig.Count / (float)fig.LengthTicks;
		if ( d <= 0f ) return 1f;
		float mean = 0f;
		foreach ( var p in table ) mean += p.Count / (float)p.LengthTicks;
		mean /= table.Length;
		float norm = 0f;
		foreach ( var p in table )
		{
			float pd = p.Count / (float)p.LengthTicks;
			norm += pd > 0f ? MathF.Sqrt( mean / pd ) : 1f;
		}
		norm /= table.Length;
		if ( norm <= 0f ) return 1f;
		return Math.Clamp( MathF.Sqrt( mean / d ) / norm, 0.75f, 1.3f );
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
