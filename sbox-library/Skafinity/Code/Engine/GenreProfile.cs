using System;

namespace Skafinity;

/// <summary>
/// Per-genre character that is NOT a user knob — the things a genre simply *is*, which a
/// listener should never have to dial in and a shuffle should never randomise across.
///
/// Today this carries the swing band. It is the intended home for the rest of the scattered
/// per-genre behaviour (tempo band, chord rhythm, drum style, comp figure, bass voice, mix
/// trims, section map) — see PLAN.md.
/// </summary>
readonly struct GenreProfile
{
	/// <summary>Swing band, as a fraction of an eighth note. Drawn per song from the seed the
	/// same way tempo is, so a genre has a consistent feel with room to vary.</summary>
	public readonly float SwingMin, SwingMax;

	GenreProfile( float swingMin, float swingMax )
	{
		SwingMin = swingMin;
		SwingMax = swingMax;
	}

	// Swing is a genre's identity, not a preference. Ska lives on a pushed offbeat; metal and
	// punk are machine-straight and any shuffle at all reads as sloppy; rock and pop sit near
	// straight with a touch of human push; country keeps a light shuffle under the train beat.
	static readonly GenreProfile[] Profiles =
	{
		new( 0.10f, 0.22f ),   // 0 ska
		new( 0.00f, 0.08f ),   // 1 rock
		new( 0.04f, 0.16f ),   // 2 country
		new( 0.00f, 0.02f ),   // 3 metal
		new( 0.00f, 0.03f ),   // 4 punk
		new( 0.00f, 0.05f ),   // 5 pop
	};

	public static GenreProfile For( int genre ) => Profiles[Math.Clamp( genre, 0, Profiles.Length - 1 )];

	/// <summary>This song's swing, drawn from the genre's band.</summary>
	/// <param name="fast">Uptempo songs tighten toward the straight end — at speed a wide
	/// shuffle stops reading as a groove and starts reading as a drag.</param>
	public float DrawSwing( Rng rng, bool fast )
	{
		float t = rng.Next();
		float swing = SwingMin + t * (SwingMax - SwingMin);
		return fast ? swing * 0.5f : swing;
	}
}
