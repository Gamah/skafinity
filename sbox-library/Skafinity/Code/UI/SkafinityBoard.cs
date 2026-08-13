using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>
/// The board's PRESENTATION RULES, with no widget toolkit in sight: what the controls are called,
/// what they say when they change, how a length or a playlist row is worded, and how the vibe's
/// fields fall into a grid. Everything here is a pure function of engine state.
/// </summary>
/// <remarks>
/// <para>This exists because the same board is drawn twice — once as a Razor panel here, once as
/// <c>web/skafinity-element.js</c> — and the half that drifts between two drawings of one design is
/// never the layout, it is the wording and the small derived decisions: which button is disabled,
/// what a row says when nothing is cached, whether a knob repeats its column's name. Those are
/// written down once, here.</para>
///
/// <para><b>Framework-free on purpose, and not yet shared.</b> Nothing in this file touches
/// <c>Sandbox.*</c>, so it can move under <c>Code/Engine/</c> — the folder both targets compile —
/// the day the web asks the wasm for it instead of keeping its own copy. It is deliberately NOT
/// there yet: a file under <c>Engine/</c> is a file in the wasm bundle, and adding one costs a full
/// AOT re-stage (see the stale-bundle gate in CLAUDE.md) for code nothing on that side calls yet.
/// Keep it free of engine-target-hostile types so that move stays a move rather than a rewrite.</para>
/// </remarks>
public static class SkafinityBoard
{
	// ── The grid ────────────────────────────────────────────────────────────────────────────────
	/// <summary>One header per vibe-matrix column. Column 0 (VOLUME) is a local mix preference and
	/// never travels; columns 1..4 are the wire. The grid is rectangular, so a voice with nothing in
	/// its last column simply leaves that cell empty.</summary>
	public static readonly string[] ColumnHeaders = { "VOLUME", "TONE", "CHARACTER", "EXTRA", "MORE" };

	/// <summary>One row of the per-instrument mixer: a voice and its cells, one per
	/// <see cref="ColumnHeaders"/> entry, null where this genre leaves a column empty.</summary>
	public readonly struct MatrixRow
	{
		/// <summary>Voice name — the row label (BASS, DRUMS, …).</summary>
		public string Voice { get; init; }
		/// <summary>Cells by column index; null = this genre has no knob there.</summary>
		public VibeCodec.Field[] Cells { get; init; }
	}

	/// <summary>Lay the genre's vibe fields out as the mixer grid: one row per voice, in the
	/// library's own display order. Fields with no voice are GLOBAL and come back from
	/// <see cref="Globals"/> instead.</summary>
	/// <remarks>Driven entirely from the field metadata, so a new genre — or a new knob — is a pure
	/// engine change and there is no field table in any UI.</remarks>
	public static List<MatrixRow> Matrix( int genre )
	{
		var order = new List<string>();
		var byVoice = new Dictionary<string, VibeCodec.Field[]>();
		foreach ( var f in VibeCodec.Fields( genre ) )
		{
			if ( f.Voice == null ) continue;
			if ( !byVoice.TryGetValue( f.Voice, out var cells ) )
			{
				cells = new VibeCodec.Field[ColumnHeaders.Length];
				byVoice[f.Voice] = cells;
				order.Add( f.Voice );
			}
			if ( f.Column >= 0 && f.Column < cells.Length ) cells[f.Column] = f;
		}
		var rows = new List<MatrixRow>( order.Count );
		foreach ( var v in order ) rows.Add( new MatrixRow { Voice = v, Cells = byVoice[v] } );
		return rows;
	}

	/// <summary>The genre's knobs that belong to no instrument — the GLOBAL strip under the grid.
	/// Often empty (the globals have been retired to reserved wire slots), and a heading over an
	/// empty grid reads as a panel that failed to draw something, so callers check.</summary>
	public static List<VibeCodec.Field> Globals( int genre )
	{
		var list = new List<VibeCodec.Field>();
		foreach ( var f in VibeCodec.Fields( genre ) )
			if ( f.Voice == null ) list.Add( f );
		return list;
	}

	/// <summary>What to write above a knob in the grid: nothing when the column header already says
	/// it (VOLUME under VOLUME reads as a mistake), the field's own name otherwise.</summary>
	public static string KnobLabel( VibeCodec.Field f, int column ) =>
		f == null ? "" : f.Name == ColumnHeaders[column] ? "" : f.Name;

	/// <summary>Index of a field within its genre's field list — what
	/// <see cref="SkafinityPlayer.SetVibe"/> takes.</summary>
	public static int FieldIndex( int genre, VibeCodec.Field field )
	{
		var fields = VibeCodec.Fields( genre );
		for ( int i = 0; i < fields.Count; i++ )
			if ( ReferenceEquals( fields[i], field ) ) return i;
		return -1;
	}

	/// <summary>Which of a choice field's options a 0..1 value selects.</summary>
	public static int ChoiceIndex( VibeCodec.Field f, float norm ) =>
		f?.Choices == null ? 0
		: Math.Clamp( (int)MathF.Round( norm * (f.Choices.Length - 1) ), 0, f.Choices.Length - 1 );

	/// <summary>…and the 0..1 value that selects option <paramref name="k"/>.</summary>
	public static float ChoiceNorm( VibeCodec.Field f, int k ) =>
		f?.Choices == null || f.Choices.Length < 2 ? 0f : k / (float)(f.Choices.Length - 1);

	// ── Numbers as words ────────────────────────────────────────────────────────────────────────
	/// <summary>Shown where a length would be if there were one. A song that has not been rendered
	/// has no length to state, and an honest dash beats 0:00 — which reads as a song of no length.</summary>
	public const string NoTime = "–:––";

	/// <summary>m:ss, or <see cref="NoTime"/> when the length is not known yet.</summary>
	public static string Time( double seconds, bool known = true )
	{
		if ( !known || double.IsNaN( seconds ) ) return NoTime;
		int t = (int)Math.Round( Math.Max( 0, seconds ) );
		return $"{t / 60}:{(t % 60):00}";
	}

	/// <summary>A 0..1 fraction as a CSS width.</summary>
	public static string Percent( float f ) => $"{(int)MathF.Round( Math.Clamp( f, 0f, 1f ) * 100 )}%";

	/// <summary>Genre name for an id, or "?" — a UI drawing a row for a genre the engine does not
	/// have should show that rather than throw.</summary>
	public static string GenreName( int g ) =>
		g >= 0 && g < VibeCodec.GenreCount ? VibeCodec.Genres[g] : "?";

	/// <summary>The caret column of a playlist row: on the song you are hearing, nothing otherwise.
	/// A column rather than a prefix so every row's number starts at the same x.</summary>
	public static string RowCaret( SkafinityPlayer.QueueEntry e ) => e.Current ? "▶" : "";

	/// <summary>What a playlist row says on its right-hand side. Generating rows draw a bar instead
	/// and never reach this.</summary>
	public static string RowStatus( SkafinityPlayer.QueueEntry e ) =>
		e.Current ? Copy.RowNow : e.Cached ? Copy.RowReady : e.Past ? Copy.RowGone : Copy.RowPending;

	// ── The words ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Every user-visible string on the board, in one place. The tooltips carry the reason a
	/// control exists, which is the part that is genuinely hard to reconstruct — "reroll" and
	/// "randomize" are both dice, and only their tooltips say why there are two.</summary>
	public static class Copy
	{
		// Transport
		public const string Prev = "⏮";
		public const string PrevTitle = "Previous song";
		public const string Play = "▶";
		public const string Pause = "⏸";
		public const string PlayTitle = "Play / Pause";
		public const string Next = "⏭";
		public const string NextTitle = "Next song";
		/// <summary>Ends in the hash on purpose — see <see cref="Hash"/>.</summary>
		public const string NowPlaying = "now playing #";
		/// <summary>A number sign, ALONE, as its own label.</summary>
		/// <remarks>A label whose text is LONGER than one character and begins with <c>#</c> is a
		/// localisation token: the engine looks the rest up as a phrase and renders what comes back,
		/// so <c>#24</c> silently becomes <c>24</c>. That is why no label here is built as "#" plus a
		/// number — the hash either ends the text before it, or stands alone in a panel of its own,
		/// where the length rule leaves it untouched.</remarks>
		public const string Hash = "#";
		public const string Volume = "vol";
		public const string SeekTitle = "Seek within this song";
		/// <summary>Playback is stalled on a song being rendered — as opposed to the silent
		/// background look-ahead, which nobody needs to be told about.</summary>
		public static string Generating( int n ) => $"generating #{n}…";

		// Seed
		public const string SeedPlaceholder = "tag:n[:genre][:vibe]";
		/// <summary>Typing a seed and being handed a stopped transport is a dead end, so the button
		/// says play, because that is what it does.</summary>
		public const string SeedGo = "play";
		public const string CopySong = "copy seed";
		public const string CopySongTitle = "Copy this song fully written down — the genre and the vibe spelled out, so it plays the same anywhere";
		public const string CopyStation = "copy station";
		public const string CopyStationTitle = "Copy the seed as it stands — whatever it leaves rolling keeps rolling";
		public const string Copied = "copied!";

		// What plays
		public const string Genre = "genre";
		public const string GenreRandom = "Random";
		public const string Reroll = "🎲 reroll";
		public const string RerollTitle = "A fresh station at song 0 — anything you have pinned stays pinned";
		public const string ShuffleOn = "🔀 shuffle: ON";
		public const string ShuffleOff = "🔀 shuffle: OFF";
		public const string ShuffleTitle = "Every next song is a whole new station rather than the next song of this one";
		public const string Tinker = "🎛 tinker";
		public const string TinkerOpen = "hide knobs";

		// The knobs
		public const string VibeHeading = "vibe";
		public const string GlobalHeading = "GLOBAL";
		public const string VibeRoll = "🎲 randomize";
		public const string VibeRollTitle = "Throw every knob somewhere new and keep it — the seed carries these values";
		public const string VibeRandom = "↺ random each song";
		public const string VibeRandomTitle = "Stop pinning these knobs — let every song roll its own again";

		// The playlist
		public const string PlaylistHeading = "playlist";
		public const string JumpTo = "jump to";
		public const string JumpGo = "go";
		public const string RowNow = "now";
		public const string RowReady = "ready";
		public const string RowGone = "gone";
		public const string RowPending = "—";
		public const string Export = "⬇ .wav";
		public const string ExportBusy = "⬇ …";
		public static string ExportTitle( int n ) => $"Export #{n}";

		// What the message line says. A refused seed says so INLINE and changes nothing: a toast
		// that has faded is no help to somebody looking at a board that did nothing.
		public static string Saved( string file ) => $"Saved {file} to your s&box data folder";
		public const string SaveFailed = "Couldn't save song";
		public static string Playing( string seed ) => $"Playing {seed}";
		public const string NewStation = "New station";
		public const string VibeRolled = "Threw every knob but the volumes, and pinned them";
		public const string VibeUnpinned = "Every song rolls its own vibe again — what changes is the songs after this one";
		public const string GenreUnpinned = "Every song rolls its own genre again";
	}
}
