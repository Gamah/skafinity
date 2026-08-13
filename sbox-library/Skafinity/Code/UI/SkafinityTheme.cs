using Sandbox;

namespace Skafinity;

/// <summary>
/// Runtime palette for <see cref="SkafinityMusicPanel"/>. The whole palette derives from one
/// hue, so a consuming game retints the board by setting a single colour:
/// <code>SkafinityTheme.Accent = Color.Parse( myAccentHex );</code>
/// Leave it unset and the board is neutral gray-on-black, which is what a drop-in library
/// should look like — colour is the consumer's call, not this library's.
/// </summary>
/// <remarks>
/// s&amp;box-only: this is UI, so it lives outside <c>Code/Engine/</c> (which stays
/// framework-free for the wasm build).
///
/// SCSS variables are compile-time, so a vendored copy could only be re-themed by editing it —
/// which is exactly what the house rule against patching a vendored library forbids. The panel
/// therefore binds these as inline <c>style=</c> values, the same way rotaliate/gambit's wall
/// boards bind <c>WallTheme</c>; <c>SkafinityMusicPanel.razor.scss</c> keeps the non-colour
/// tokens (layout, fonts, radii) and every border.
/// </remarks>
public static class SkafinityTheme
{
	// Unset = neutral. A mid gray rather than a dark one: the palette below scales DOWN for the
	// fills and UP toward white for the text, so the hue it starts from has to sit between them
	// for both ends to land — a near-black accent gives invisible tick fills.
	static readonly Color NeutralAccent = Color.Parse( "#7a7a7a" ) ?? Color.White;

	/// <summary>The hue the whole palette derives from. Null (the default) = neutral gray/black.
	/// Set it once at startup, or whenever your game's own theme changes — the panel folds this
	/// into its build hash, so a change re-renders the board.</summary>
	public static Color? Accent { get; set; }

	static Color Hue => Accent ?? NeutralAccent;

	// Derived palette. The factors are WallTheme's, so a game that passes its wall accent in gets
	// the same board it already has elsewhere — and #2f9450 reproduces the panel's original
	// hardcoded green.
	/// <summary>Board background (near-black tint of the accent).</summary>
	public static string Bg => Rgb( Scale( Hue, 0.09f ) );
	/// <summary>Button / cell / queue-entry fill — deeper than the board.</summary>
	public static string Cell => Rgb( Scale( Hue, 0.04f ) );
	/// <summary>Filled ticks and selected volume cells.</summary>
	public static string CellFill => Rgb( Scale( Hue, 0.6f ) );
	/// <summary>A wash of <see cref="CellFill"/> — a cached queue entry.</summary>
	public static string CellFillSoft => Rgba( Scale( Hue, 0.6f ), 0.25f );
	/// <summary>The accent itself: progress bars, "on" text, status lines.</summary>
	public static string AccentCss => Rgb( Hue );
	/// <summary>Background of an active toggle / the current queue entry.</summary>
	public static string AccentBg => Rgba( Hue, 0.2f );
	/// <summary>Primary text (button labels).</summary>
	public static string Text => Rgba( Mix( Hue, 0.8f ), 0.9f );
	/// <summary>Labels and secondary lines.</summary>
	public static string TextDim => Rgba( Mix( Hue, 0.72f ), 0.7f );

	// ── The same palette, as colours ──
	// Everything above is a CSS string because the razor spends it in a style= attribute. Panels this
	// library builds and styles from C# (SkafinitySlider) need the value itself, so the two that one
	// needs are exposed here as well. Same source, so they cannot drift.
	/// <summary>The accent itself. What a slider's fill and thumb are painted with.</summary>
	public static Color AccentColor => Hue;
	/// <summary>A slider's trough — neutral, like the stylesheet's borders, so it reads against any
	/// accent a host passes in.</summary>
	public static Color TrackColor => new Color( 1f, 1f, 1f, 0.18f );

	// Component-wise helpers (avoid relying on Color operators), matching the sibling repos' style.
	static Color Scale( Color c, float f ) => new Color( c.r * f, c.g * f, c.b * f );
	static Color Mix( Color c, float towardWhite ) => new Color(
		c.r + ( 1f - c.r ) * towardWhite,
		c.g + ( 1f - c.g ) * towardWhite,
		c.b + ( 1f - c.b ) * towardWhite );

	static string Rgb( Color c ) => $"rgb({To255( c.r )},{To255( c.g )},{To255( c.b )})";
	static string Rgba( Color c, float a ) => $"rgba({To255( c.r )},{To255( c.g )},{To255( c.b )},{a})";
	static int To255( float v ) => (int)System.MathF.Round( System.Math.Clamp( v, 0f, 1f ) * 255f );
}
