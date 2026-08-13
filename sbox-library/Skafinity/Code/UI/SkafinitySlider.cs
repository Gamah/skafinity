using System;
using Sandbox;
using Sandbox.UI;

namespace Skafinity;

/// <summary>
/// A draggable slider, built out of panels this library owns and styled entirely from code.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>Sandbox.UI.SliderControl</c>: in this project it cannot be drawn at all.</b>
/// It is built, and it takes the mouse — dragging one moves its value — but every panel inside it
/// (<c>.inner</c>, <c>.track</c>, <c>.thumb</c>) sits at zero height, and a track with no height
/// paints nothing however it is coloured. Nothing sizes them: the base addon's stylesheet, which is
/// what dresses these parts in a game, does not reach this UI, and a stylesheet here cannot cross
/// into another component's insides to do it instead. Nor can an inline style, which cannot reach a
/// panel we did not create.</para>
///
/// <para>That is not a guess and it is not slider-specific. A stock <c>SwitchControl</c> — another
/// base component with its own sheet and an unmistakable pill to draw — is equally invisible, and
/// terryball's own settings screen, copied in whole with only its wiring removed, does not draw its
/// sliders either. A panel known to work where it came from does not work here, so the difference is
/// the project rather than anything written in this library. Whether that is a library-vs-game
/// thing is still open; it is being tracked separately.</para>
///
/// <para>So this owns the whole thing: three child panels, every value set as an INLINE style, and
/// no dependency on a cascade that may not arrive. That also hands the accent back — the theme is a
/// runtime colour, and a panel we build is a panel we can paint, which the stock control could never
/// have been.</para>
///
/// <para>The drag is <see cref="SliderControl"/>'s, because that part of it was never the problem:
/// press anywhere on the track to jump there, and <see cref="Panel.HasActive"/> is what keeps a drag
/// alive — the UI system holds it on the pressed panel until release, including a release that
/// happens somewhere else entirely.</para>
/// </remarks>
public sealed class SkafinitySlider : Panel
{
	/// <summary>Left end of the range.</summary>
	[Parameter] public float Min { get; set; } = 0f;
	/// <summary>Right end of the range.</summary>
	[Parameter] public float Max { get; set; } = 1f;
	/// <summary>Round to this. 0 = continuous; 1 snaps to whole numbers, which is what the vibe's
	/// knobs want — the seed encodes one level per base-36 character and a slider should not be able
	/// to land between two of them.</summary>
	[Parameter] public float Step { get; set; }
	/// <summary>Where the thumb is. Clamped into the range when drawn.</summary>
	[Parameter] public float Value { get; set; }
	/// <summary>Called on every change while dragging — the caller debounces if it needs to.</summary>
	[Parameter] public Action<float> OnValueChanged { get; set; }
	/// <summary>Dimmed and inert. What the seek bar is before the song has a length worth drawing
	/// against.</summary>
	[Parameter] public bool Disabled { get; set; }
	/// <summary>Fixed width in pixels. 0 (the default) takes the slack of a row, or the full width of
	/// a column.</summary>
	[Parameter] public float FixedWidth { get; set; }

	/// <summary>Height of the whole control, and of the pointer target with it — the track is a thin
	/// line inside it, because a 6px-tall thing is not something anybody can hit.</summary>
	public const float Height = 24f;
	const float TrackHeight = 6f;
	const float ThumbSize = 14f;
	// The thumb is centred on its position, so half of it hangs off each end of the track and needs
	// somewhere to hang.
	const float EndInset = 8f;

	readonly Panel _track;
	readonly Panel _fill;
	readonly Panel _thumb;

	// What was last drawn, so Tick only touches the style when something actually moved. Razor
	// re-sets every [Parameter] on each rebuild, and a style write is a layout.
	float _drawnFraction = float.NaN;
	bool _drawnDisabled;
	float _drawnWidth = float.NaN;
	Color _drawnAccent;

	public SkafinitySlider()
	{
		_track = new Panel { Parent = this };
		_fill = new Panel { Parent = _track };
		_thumb = new Panel { Parent = _track };

		Style.FlexDirection = FlexDirection.Row;
		Style.AlignItems = Align.Center;
		Style.Height = Length.Pixels( Height );
		// Only until the first Tick, which owns width/grow/shrink together — they are one decision.
		Style.FlexShrink = 0;
		Style.PointerEvents = PointerEvents.All;

		_track.Style.Position = PositionMode.Relative;
		_track.Style.FlexGrow = 1;
		_track.Style.Height = Length.Pixels( TrackHeight );
		_track.Style.MarginLeft = Length.Pixels( EndInset );
		_track.Style.MarginRight = Length.Pixels( EndInset );
		SetRadius( _track, TrackHeight * 0.5f );

		_fill.Style.Position = PositionMode.Absolute;
		_fill.Style.Left = Length.Pixels( 0 );
		_fill.Style.Top = Length.Pixels( 0 );
		_fill.Style.Height = Length.Percent( 100 );
		SetRadius( _fill, TrackHeight * 0.5f );

		_thumb.Style.Position = PositionMode.Absolute;
		_thumb.Style.Width = Length.Pixels( ThumbSize );
		_thumb.Style.Height = Length.Pixels( ThumbSize );
		// Half the thumb back, so `left` is its CENTRE rather than its left edge.
		_thumb.Style.MarginLeft = Length.Pixels( -ThumbSize * 0.5f );
		_thumb.Style.Top = Length.Pixels( (TrackHeight - ThumbSize) * 0.5f );
		SetRadius( _thumb, ThumbSize * 0.5f );
	}

	static void SetRadius( Panel p, float r )
	{
		p.Style.BorderTopLeftRadius = Length.Pixels( r );
		p.Style.BorderTopRightRadius = Length.Pixels( r );
		p.Style.BorderBottomLeftRadius = Length.Pixels( r );
		p.Style.BorderBottomRightRadius = Length.Pixels( r );
	}

	/// <summary>Where the thumb sits, 0..1 across the range.</summary>
	float Fraction => Max > Min ? Math.Clamp( (Value - Min) / (Max - Min), 0f, 1f ) : 0f;

	public override void Tick()
	{
		base.Tick();

		// The row/column question: a fixed width, or take what there is. A row hands out slack
		// through FlexGrow; a column stretches a child to its width, and asking for 100% there is
		// what stops a slider being as narrow as nothing.
		float wanted = FixedWidth;
		if ( wanted != _drawnWidth )
		{
			_drawnWidth = wanted;
			if ( wanted > 0 )
			{
				Style.Width = Length.Pixels( wanted );
				Style.FlexGrow = 0;
				Style.FlexShrink = 0;
			}
			else
			{
				// 100% is a BASE size, not a final one: in a column it is what stops the slider being
				// as narrow as nothing, and in a row it is a full row's width asked for before the
				// labels beside it have had theirs. So it must also be allowed to shrink — without
				// that, a slider in a row overflows its panel by exactly the width of its siblings,
				// and whatever sits to its right is pushed off the edge.
				Style.Width = Length.Percent( 100 );
				Style.FlexGrow = 1;
				Style.FlexShrink = 1;
			}
		}

		// The palette is a runtime value a host can change at any moment, so it is folded into the
		// same "has anything moved" check as the value.
		var accent = SkafinityTheme.AccentColor;
		float f = Fraction;
		if ( f == _drawnFraction && Disabled == _drawnDisabled && accent == _drawnAccent ) return;
		_drawnFraction = f;
		_drawnDisabled = Disabled;
		_drawnAccent = accent;

		_fill.Style.Width = Length.Percent( f * 100f );
		_thumb.Style.Left = Length.Percent( f * 100f );

		// Neutral trough, accent fill — the same split the rest of the board uses, except that here
		// the accent CAN be reached, because these are our panels.
		_track.Style.BackgroundColor = SkafinityTheme.TrackColor;
		_fill.Style.BackgroundColor = accent;
		_thumb.Style.BackgroundColor = accent;
		Style.Opacity = Disabled ? 0.4f : 1f;
		Style.PointerEvents = Disabled ? PointerEvents.None : PointerEvents.All;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( Disabled ) return;
		Apply( Mouse.Position );
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );
		// HasActive is the drag: the UI system keeps it on the panel that was pressed until the
		// button is released, wherever the pointer has got to by then.
		if ( Disabled || !HasActive ) return;
		Apply( Mouse.Position );
		e.StopPropagation();
	}

	// Screen x → a value on the range, snapped to Step. Measured against the TRACK rather than the
	// whole control, so the ends line up with the ends of the line you can see.
	void Apply( Vector2 pos )
	{
		var r = _track.Box.Rect;
		if ( r.Width <= 0f ) return;
		float t = MathX.LerpInverse( pos.x, r.Left, r.Right, true );
		float v = MathX.LerpTo( Min, Max, t, true );
		if ( Step > 0f ) v = v.SnapToGrid( Step );
		if ( v == Value ) return;
		Value = v;
		OnValueChanged?.Invoke( v );
	}
}
