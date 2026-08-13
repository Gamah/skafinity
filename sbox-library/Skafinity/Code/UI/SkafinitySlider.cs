using System;
using Sandbox;
using Sandbox.UI;

namespace Skafinity;

/// <summary>
/// A draggable slider, built out of panels this library owns and styled entirely from code.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>Sandbox.UI.SliderControl</c>.</b> It works, but it cannot be dressed from
/// here, and the board's whole look is a runtime palette. Two separate walls, both found by dumping
/// the live panel tree (<c>skafinity_ui_dump</c>) rather than by reading code:</para>
///
/// <list type="bullet">
/// <item><description>A <b>static</b> <c>class="…"</c> on a razor COMPONENT is dropped — the control
/// came back carrying only its own <c>.slidercontrol</c>. The same attribute on a plain
/// <see cref="Panel"/> subclass (<c>DropDown</c>) applies fine, and a class whose value is a razor
/// expression applies too, which is why exactly one of the board's sliders looked different from
/// the rest.</description></item>
/// <item><description>Its <c>.inner</c> / <c>.track</c> / <c>.thumb</c> are built by its own razor,
/// and a stylesheet does not reach across that boundary: every rule written for them here matched
/// nothing, and the panels sat at zero height with nothing else styling them either. A track with no
/// height paints nothing however it is coloured — the control laid out, and took the mouse, and drew
/// nothing at all.</description></item>
/// </list>
///
/// <para>So this owns the whole thing: three child panels, every value set as an INLINE style, and
/// no dependency on a cascade that may not arrive. That also hands the accent back — the theme is a
/// runtime colour, and a panel we build is a panel we can paint.</para>
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
			}
			else
			{
				Style.Width = Length.Percent( 100 );
				Style.FlexGrow = 1;
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
