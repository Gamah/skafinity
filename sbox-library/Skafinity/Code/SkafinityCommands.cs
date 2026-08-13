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
	/// <summary>Name of the GameObject <see cref="Spawn"/> builds. Also how <see cref="Despawn"/>
	/// finds it again, so it only ever destroys its own rig and never a scene-authored player.</summary>
	const string RigName = "Skafinity (console)";

	// Every command needs the player, and "there isn't one" is the single most likely reason a
	// command does nothing — so say so rather than failing silently.
	static SkafinityPlayer Player()
	{
		var scene = Sandbox.Game.ActiveScene;
		if ( scene == null ) { Log.Warning( "[Skafinity] no active scene." ); return null; }

		var p = scene.GetAllComponents<SkafinityPlayer>().FirstOrDefault();
		if ( p == null )
			Log.Warning( "[Skafinity] no SkafinityPlayer in the scene — run skafinity_spawn for a "
				+ "throwaway one, or add the component to a GameObject yourself." );
		return p;
	}

	/// <summary>Build a throwaway board (and a player, if the scene hasn't got one) on a runtime
	/// GameObject, so the library can be tried in ANY scene without one being authored for it.
	/// <c>skafinity_despawn</c> removes it.</summary>
	/// <remarks>It never makes a SECOND of anything: a board already in the scene — a previous rig
	/// or the game's own UI — is the one it hands back, and a player already in the scene is the
	/// one the board drives. So this is safe to run in a game that has Skafinity wired up properly,
	/// where it does nothing but tell you so.</remarks>
	/// <remarks>Client-local and never saved: <c>NetworkMode.Never</c> so it is not replicated,
	/// <c>GameObjectFlags.NotSaved</c> so it cannot end up committed in someone's scene file. It
	/// carries its OWN <c>ScreenPanel</c> rather than hunting for the scene's, which is what makes
	/// it work in a scene that has no UI root at all. Shape copied from rotaliate-client's
	/// <c>LocalMusicSystem</c>, which builds the same three components for real.</remarks>
	[ConCmd( "skafinity_spawn" )]
	public static void Spawn()
	{
		var panel = BuildRig( out bool created );
		if ( panel == null ) return;

		Log.Info( created
			? $"[Skafinity] rig ready — '{RigName}'. skafinity_panel opens it, skafinity_despawn removes it."
			: $"[Skafinity] nothing spawned — this scene already has a board, on '{panel.GameObject?.Name}'. "
			  + "skafinity_panel opens it." );
	}

	/// <summary>Destroy the rig <c>skafinity_spawn</c> built. Leaves a scene-authored player alone —
	/// it only removes the GameObject it made.</summary>
	[ConCmd( "skafinity_despawn" )]
	public static void Despawn()
	{
		var scene = Sandbox.Game.ActiveScene;
		if ( scene == null ) { Log.Warning( "[Skafinity] no active scene." ); return; }

		var rig = FindRig( scene );
		if ( rig == null ) { Log.Info( "[Skafinity] no console rig to remove." ); return; }

		rig.Destroy();
		Log.Info( $"[Skafinity] removed '{RigName}'." );
	}

	/// <summary>Open/close the settings board — the panel ships no launcher of its own, so this is
	/// how you see it at all. Builds a rig first if the scene has no board (see
	/// <see cref="Spawn"/>), so this one command works in an empty scene.</summary>
	[ConCmd( "skafinity_panel" )]
	public static void TogglePanel()
	{
		// Nothing to toggle rather than nothing to do: the point of the command is to see the board,
		// and needing a scene authored first is the whole problem it exists to solve. BuildRig
		// returns the scene's own board where there is one, so this never makes a second.
		var panel = BuildRig( out bool created );
		if ( panel == null ) return;
		if ( created )
			Log.Info( $"[Skafinity] no board in the scene — built '{RigName}'. skafinity_despawn removes it." );

		panel.Toggle();
		Log.Info( $"[Skafinity] board {( panel.IsOpen ? "OPEN" : "closed" )}." );
	}

	// The rig itself. Returns the board to drive — an existing one wherever there is one — or null
	// with a reason logged. `created` says whether anything was actually built.
	static SkafinityMusicPanel BuildRig( out bool created )
	{
		created = false;
		var scene = Sandbox.Game.ActiveScene;
		if ( scene == null ) { Log.Warning( "[Skafinity] no active scene." ); return null; }

		// A play-mode thing. In the editor's edit scene there is no audio to hear and no reason to
		// be adding objects to what someone is authoring, even unsaveable ones.
		if ( scene.IsEditor )
		{
			Log.Warning( "[Skafinity] press play first — the rig is built into the running scene, not the edit scene." );
			return null;
		}

		// A headless server has no audio device and no screen; the player is DontExecuteOnServer
		// for the same reason, so building it there would produce an inert object and a puzzle.
		if ( Application.IsDedicatedServer )
		{
			Log.Warning( "[Skafinity] not on a dedicated server — Skafinity is client-side (audio + UI)." );
			return null;
		}

		// NEVER a second board. Any panel already in the scene is the one to drive, whether it is a
		// previous rig or one the game authored — two boards would be two sets of controls over one
		// player, and the second would be the game's own UI duplicated by a debug command.
		var existing = scene.GetAllComponents<SkafinityMusicPanel>().FirstOrDefault();
		if ( existing != null ) return existing;

		created = true;
		var go = new GameObject( true, RigName ) { Flags = GameObjectFlags.NotSaved };
		go.NetworkMode = NetworkMode.Never;   // strictly local: this is a test rig, not game state

		// Its own UI root, so this works in a scene with no ScreenPanel of its own.
		go.Components.Create<ScreenPanel>();

		// Reuse a player the scene already has rather than starting a second soundtrack over it.
		var player = scene.GetAllComponents<SkafinityPlayer>().FirstOrDefault()
			?? go.Components.Create<SkafinityPlayer>();

		var panel = go.Components.Create<SkafinityMusicPanel>();
		panel.Player = player;
		return panel;
	}

	// Only ever OUR GameObject: matched by name, so a scene-authored player is never a candidate.
	static GameObject FindRig( Scene scene )
	{
		foreach ( var p in scene.GetAllComponents<SkafinityMusicPanel>() )
			if ( p.GameObject != null && p.GameObject.Name == RigName )
				return p.GameObject;
		return null;
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

	/// <summary>Play a seed: <c>tag:n[:genre][:vibe]</c> (a bare <c>tag</c> is song 0). Pass
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

	/// <summary>Reroll the vibe: every knob thrown somewhere new and PINNED there, keeping the genre
	/// and your per-instrument volumes. <c>skafinity_station</c> is the other die — a different song
	/// rather than a different taste.</summary>
	[ConCmd( "skafinity_reroll" )]
	public static void Reroll()
	{
		var p = Player();
		if ( p == null ) return;

		p.RerollVibe();
		Log.Info( $"[Skafinity] rerolled — {p.CurrentSeed}" );
	}

	/// <summary>A fresh random station at song 0. Anything pinned stays pinned.</summary>
	[ConCmd( "skafinity_station" )]
	public static void Station()
	{
		var p = Player();
		if ( p == null ) return;

		p.RerollStation();
		Log.Info( $"[Skafinity] new station — {p.StationSeed}" );
	}

	/// <summary>Flip shuffle: on, every next song is a whole new station rather than the next song of
	/// this one.</summary>
	[ConCmd( "skafinity_shuffle" )]
	public static void ToggleShuffle()
	{
		var p = Player();
		if ( p == null ) return;

		p.SetShuffle( !p.Shuffle );
		Log.Info( $"[Skafinity] shuffle {( p.Shuffle ? "on" : "off" )} — {p.StationSeed}" );
	}

	/// <summary>Pause or resume, keeping the place in the song.</summary>
	[ConCmd( "skafinity_pause" )]
	public static void TogglePause()
	{
		var p = Player();
		if ( p == null ) return;

		p.TogglePlay();
		var at = p.Playhead();
		Log.Info( $"[Skafinity] {( p.IsPaused ? "paused" : "playing" )} at {SkafinityBoard.Time( at.Time, at.Duration > 0 )}" );
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
		var at = p.Playhead();
		Log.Info( "── skafinity_status ──" );
		Log.Info( $"   seed      {p.CurrentSeed}   (n {p.N}, genre {genre} = {VibeCodec.Genres[genre]})" );
		Log.Info( $"   station   {p.StationSeed}   (position {p.Position} on the line)" );
		Log.Info( $"   transport {( p.Enabled ? "on" : "MUTED" )}, vol {p.Volume:0.00}, "
			+ $"{( p.IsPaused ? "paused" : p.IsPlaying ? "playing" : "not playing" )} "
			+ $"{SkafinityBoard.Time( at.Time, at.Duration > 0 )} / {SkafinityBoard.Time( at.Duration, at.Duration > 0 )}"
			+ $"{( p.IsBuffering ? ", BUFFERING" : p.IsGenerating ? ", generating ahead" : "" )}" );
		Log.Info( $"   rolling   genre {( p.GenrePinned ? "pinned" : "per song" )}, "
			+ $"vibe {( p.VibePinned ? "pinned" : "per song" )}" );
		Log.Info( $"   shuffle   {( p.Shuffle ? "on — every next song is a new station" : "off — walking this station" )}" );
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
