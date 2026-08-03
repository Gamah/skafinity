using System.Linq;
using Sandbox;

namespace Skafinity;

/// <summary>
/// Console commands for driving the player and the panel from inside the editor.
///
/// <para>They exist because the two things a host most needs to try are the two things it cannot
/// reach without writing code first. The board <b>ships no launcher</b> — visibility is
/// host-driven on purpose, so it imposes nothing on your HUD — which means a freshly-dropped
/// <see cref="SkafinityMusicPanel"/> renders nothing at all until you have bound
/// <see cref="SkafinityMusicPanel.IsOpen"/> to something. And <see cref="SkafinityTheme.Accent"/>
/// is a static a game sets at startup, so seeing what your colour looks like used to mean a
/// rebuild per guess. <c>skafinity_panel</c> and <c>skafinity_theme</c> are those two, live.</para>
///
/// <para>The rest are the seed: play one, step it, switch genre, reroll, and read back either the
/// player's state or what the composer actually decided.</para>
///
/// <para>s&amp;box-only (outside <c>Code/Engine/</c>), and client-side — the player is client-only
/// (<c>DontExecuteOnServer</c>), so these are too.</para>
/// </summary>
public static class SkafinityCommands
{
	// Every command needs the player, and "there isn't one" is the single most likely reason a
	// command does nothing — so say so rather than failing silently.
	static SkafinityPlayer Player()
	{
		var scene = Sandbox.Game.ActiveScene;
		if ( scene == null ) { Log.Warning( "[Skafinity] no active scene." ); return null; }

		var p = scene.GetAllComponents<SkafinityPlayer>().FirstOrDefault();
		if ( p == null ) Log.Warning( "[Skafinity] no SkafinityPlayer in the scene — add the component to a GameObject." );
		return p;
	}

	/// <summary>Open/close the settings board. The panel ships no launcher of its own, so in the
	/// editor this is how you see it at all.</summary>
	[ConCmd( "skafinity_panel" )]
	public static void TogglePanel()
	{
		var scene = Sandbox.Game.ActiveScene;
		if ( scene == null ) { Log.Warning( "[Skafinity] no active scene." ); return; }

		var panel = scene.GetAllComponents<SkafinityMusicPanel>().FirstOrDefault();
		if ( panel == null )
		{
			Log.Warning( "[Skafinity] no SkafinityMusicPanel in the scene — add the component to a "
				+ "GameObject under a ScreenPanel." );
			return;
		}

		panel.Toggle();
		Log.Info( $"[Skafinity] board {( panel.IsOpen ? "OPEN" : "closed" )}." );
	}

	/// <summary>Retint the board from one colour: <c>skafinity_theme #ff8a3d</c>. Pass
	/// <c>clear</c> (or <c>none</c> / <c>neutral</c>) to go back to the neutral gray/black default.
	/// This is the whole of what a consuming game does — it sets
	/// <see cref="SkafinityTheme.Accent"/> once — so what you see here is what you get by shipping
	/// that one line.</summary>
	[ConCmd( "skafinity_theme" )]
	public static void SetTheme( string accent )
	{
		if ( string.IsNullOrWhiteSpace( accent ) || accent is "clear" or "none" or "neutral" )
		{
			SkafinityTheme.Accent = null;
			Log.Info( "[Skafinity] theme cleared — neutral gray/black (the library default)." );
			return;
		}

		var c = Color.Parse( accent );
		if ( c == null )
		{
			Log.Warning( $"[Skafinity] couldn't parse '{accent}' as a colour — try a hex like #2f9450." );
			return;
		}

		SkafinityTheme.Accent = c;
		Log.Info( $"[Skafinity] accent = {accent}. In your game: SkafinityTheme.Accent = Color.Parse( \"{accent}\" );" );
	}

	/// <summary>Play a seed: <c>vibe:tag:n</c>, <c>tag:n</c> or a bare <c>tag</c>. Pass
	/// <c>default</c> to go back to the default tag and vibe.</summary>
	[ConCmd( "skafinity_seed" )]
	public static void PlaySeed( string seed )
	{
		var p = Player();
		if ( p == null ) return;

		if ( seed is "default" ) { p.SetTag( "" ); Log.Info( "[Skafinity] back to the default tag and vibe." ); return; }

		p.PlaySeed( seed );
		Log.Info( $"[Skafinity] playing {p.CurrentSeed}" );
	}

	/// <summary>Next song in the sequence.</summary>
	[ConCmd( "skafinity_next" )]
	public static void Next() { var p = Player(); if ( p == null ) return; p.NextSong(); Log.Info( $"[Skafinity] → {p.CurrentSeed}" ); }

	/// <summary>Previous song — replays the exact earlier song, not a fresh one.</summary>
	[ConCmd( "skafinity_prev" )]
	public static void Prev() { var p = Player(); if ( p == null ) return; p.PrevSong(); Log.Info( $"[Skafinity] ← {p.CurrentSeed}" ); }

	/// <summary>Switch genre by index. Run it with a junk index to print the roster.</summary>
	[ConCmd( "skafinity_genre" )]
	public static void SetGenre( int genre )
	{
		var p = Player();
		if ( p == null ) return;

		if ( genre < 0 || genre >= VibeCodec.GenreCount )
		{
			Log.Warning( $"[Skafinity] genre {genre} is out of range. {Roster()}" );
			return;
		}

		p.SetGenre( genre );
		Log.Info( $"[Skafinity] genre {genre} = {VibeCodec.Genres[genre]} — {p.CurrentSeed}" );
	}

	/// <summary>Reroll the vibe: a new genre and every knob, keeping your per-instrument volumes.</summary>
	[ConCmd( "skafinity_reroll" )]
	public static void Reroll()
	{
		var p = Player();
		if ( p == null ) return;

		p.RerollVibe();
		Log.Info( $"[Skafinity] rerolled — {p.CurrentSeed}" );
	}

	/// <summary>Write the playing song to a .wav under the s&amp;box data folder.</summary>
	[ConCmd( "skafinity_save" )]
	public static void Save()
	{
		var p = Player();
		if ( p == null ) return;

		var name = p.SaveCurrentToFile();
		Log.Info( string.IsNullOrEmpty( name )
			? "[Skafinity] couldn't save — nothing rendered yet?"
			: $"[Skafinity] saved {name} to your s&box data folder." );
	}

	/// <summary>What the player is doing right now: the seed, the transport, and whether the
	/// shared house mix actually loaded.</summary>
	[ConCmd( "skafinity_status" )]
	public static void Status()
	{
		var p = Player();
		if ( p == null ) return;

		int genre = p.EffectiveConfig()?.Genre ?? 0;
		Log.Info( "── skafinity_status ──" );
		Log.Info( $"   seed      {p.CurrentSeed}   (n {p.N}, genre {genre} = {VibeCodec.Genres[genre]})" );
		Log.Info( $"   transport {( p.Enabled ? "on" : "MUTED" )}, vol {p.Volume:0.00}, "
			+ $"{( p.IsPlaying ? "playing" : "not playing" )}"
			+ $"{( p.IsBuffering ? ", BUFFERING" : p.IsGenerating ? ", generating ahead" : "" )}" );
		Log.Info( $"   shuffle   {( p.RandomEverySong ? "on — every song freezes a fresh vibe + genre" : "off" )}" );
		Log.Info( $"   output    {p.SampleRate} Hz, {p.RenderThreads} render thread(s)" );
		// Zero here is the interesting case: the baseline mix is then the engine's compiled
		// defaults, not the file the web toy reads, and nothing else would ever say so.
		Log.Info( p.HouseConfigCount > 0
			? $"   housemix  {p.HouseConfigCount} values from skafinity.config.json"
			: "   housemix  NOT LOADED — skafinity.config.json isn't mounted, so the baseline mix is "
			  + "the compiled defaults rather than the shared file. Check it shipped with the addon." );
		Log.Info( $"   theme     {( SkafinityTheme.Accent == null ? "neutral (accent unset)" : SkafinityTheme.Accent.ToString() )}" );
		Log.Info( $"   {Roster()}" );
	}

	/// <summary>What the composer decided for the song playing: tempo, swing, key, changes,
	/// voicing, groove, part and tune lengths, ending, and the form. This is the "why does this
	/// seed sound wrong" tool — reading the decisions beats inferring them from the audio.
	/// Re-plans the song, so expect a short hitch.</summary>
	[ConCmd( "skafinity_explain" )]
	public static void Explain()
	{
		var p = Player();
		if ( p == null ) return;

		Log.Info( $"── skafinity_explain {p.CurrentSeed} ──" );
		Log.Info( p.ExplainCurrent() );
	}

	static string Roster() =>
		"genres: " + string.Join( "  ", VibeCodec.Genres.Select( ( g, i ) => $"{i}={g}" ) );
}
