using System;
using System.Threading.Tasks;
using Sandbox;

namespace Skafinity;

/// <summary>
/// Streams an endless, deterministic procedural ska / reggae-rock track (see
/// <see cref="MusicGen"/>) through Web Audio-style scheduling over a <see cref="SoundStream"/>.
///
/// Drop this <see cref="Component"/> on any GameObject. It generates a ~80s loop from the
/// seed <c>tag:n</c>, plays it through once, then equal-power crossfades
/// into the pre-generated next song (<c>tag:n+1</c>), forever. Every generator knob is an
/// inspector <c>[Property]</c>; with <see cref="LiveReload"/> on, tweaking one regenerates
/// after a short settle so you can dial in a vibe in play mode.
///
/// A whole song is just its seed, so the arrangement is shareable: copy <see cref="CurrentSeed"/>
/// (<c>vibe:tag:n</c>) and anyone who calls <see cref="PlaySeed"/> with it hears the same track.
///
/// This is a self-contained extraction of the Rotaliate music engine with no game-specific
/// dependencies (no player data, networking, or UI). Persistence of the song index is opt-in
/// via <see cref="PersistProgress"/>.
/// </summary>
public sealed class SkafinityPlayer : Component
{
	// ── Master ──
	/// <summary>Music master switch. 'new' so it's distinct from <see cref="Component.Enabled"/>.</summary>
	[Property, Group( "Music" )] public new bool Enabled { get; set; } = true;
	[Property, Group( "Music" ), Range( 0f, 2f )] public float Volume { get; set; } = 0.7f;
	/// <summary>Regenerate automatically a moment after any generator knob changes (editor tuning).</summary>
	[Property, Group( "Music" )] public bool LiveReload { get; set; } = true;
	/// <summary>Optional mixer name to route the music to (e.g. "Music"). Empty = default mixer.</summary>
	[Property, Group( "Music" )] public string MixerName { get; set; } = "";
	/// <summary>Begin playing automatically in <see cref="OnStart"/>. Off = call <see cref="StartSequence"/> yourself.</summary>
	[Property, Group( "Music" )] public bool AutoPlay { get; set; } = true;
	/// <summary>Shuffle mode: re-randomise every knob (incl. genre) as each new song begins, so the
	/// sequence keeps reinventing itself. Volumes are left alone (a local mix preference). Off = the
	/// seed's vibe stays put.</summary>
	[Property, Group( "Music" )] public bool RandomEverySong { get; set; } = false;

	// ── Seed ──
	/// <summary>Seed tag — any string (a name, a word). Empty falls back to "skafinity".</summary>
	[Property, Group( "Seed" )] public string Tag { get; set; } = "";
	/// <summary>Song index in the infinite sequence (0,1,2…). <see cref="StepN"/>/<see cref="NextSong"/> walk it.</summary>
	[Property, Group( "Seed" )] public int StartN { get; set; } = 0;
	/// <summary>Optional base-36 vibe override (see <see cref="VibeCodec"/>). When set it overrides
	/// the matching inspector knobs, so a shared vibe reproduces the same voicing on any client.</summary>
	[Property, Group( "Seed" )] public string Vibe { get; set; } = "";
	/// <summary>Persist the current song index across sessions (FileSystem.Data, keyed by <see cref="SaveSlot"/>).</summary>
	[Property, Group( "Seed" )] public bool PersistProgress { get; set; } = false;
	[Property, Group( "Seed" )] public string SaveSlot { get; set; } = "default";

	// ── Output ──
	[Property, Group( "Output" ), Range( 8000, 48000 )] public int SampleRate { get; set; } = 32000;
	/// <summary>Target track length; bar count adapts to tempo to hit this.</summary>
	[Property, Group( "Output" ), Range( 30f, 180f )] public float TargetSeconds { get; set; } = 80f;
	/// <summary>Worker threads the pitched-voice synthesis is split across (composition + drums
	/// stay single-threaded). Keeps each worker burst under s&amp;box's ~1000ms no-yield advisory.</summary>
	[Property, Group( "Output" ), Range( 1, 8 )] public int RenderThreads { get; set; } = 6;

	// ── Crossfade / scheduling ──
	/// <summary>Crossfade window (also the first song's fade-in from silence), seconds. The two
	/// songs only both-audible for <see cref="CrossfadeOverlap"/> of this, centred.</summary>
	[Property, Group( "Crossfade" ), Range( 0.5f, 8f )] public float Crossfade { get; set; } = 3.75f;
	[Property, Group( "Crossfade" ), Range( 0f, 1f )] public float CrossfadeOverlap { get; set; } = 0.5f;
	/// <summary>How many upcoming songs to keep pre-generated (built one-per-tick so the fill never stalls a frame).</summary>
	[Property, Group( "Crossfade" ), Range( 1, 8 )] public int AheadCount { get; set; } = 5;
		/// <summary>Radius (in songs) of the PCM cache kept around the current index: songs with
		/// |n − N| ≤ PcmCacheRadius stay resident so Prev/Next within the window is instant; anything
		/// further is pruned and regenerated from its ledger seed on demand. ~10 MB/song at the
		/// default 32 kHz stereo / 80 s, so the ±5 default is ~110 MB resident — dial down on
		/// constrained targets. The seed ledger (strings only) is never pruned.</summary>
		[Property, Group( "Crossfade" ), Range( 1, 16 )] public int PcmCacheRadius { get; set; } = 5;

	// ── Tempo (main = laid-back reggae-rock; Fast = uptempo ska) ──
	[Property, Group( "Tempo" ), Range( 60, 200 )] public int BpmMin { get; set; } = 130;
	[Property, Group( "Tempo" ), Range( 60, 200 )] public int BpmMax { get; set; } = 185;
	[Property, Group( "Tempo" ), Range( 0f, 1f )] public float FastChance { get; set; } = 0.30f;
	[Property, Group( "Tempo" ), Range( 100, 220 )] public int FastBpmMin { get; set; } = 150;
	[Property, Group( "Tempo" ), Range( 100, 220 )] public int FastBpmMax { get; set; } = 168;
	[Property, Group( "Tempo" ), Range( 0f, 0.4f )] public float Swing { get; set; } = 0.14f;
	[Property, Group( "Tempo" ), Range( 0f, 0.4f )] public float FastSwing { get; set; } = 0.05f;

	// ── Mix ──
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float BassVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float SkankVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float OrganVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float MelodyVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float HornVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float KickVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float SnareVol { get; set; } = 0.70f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float TomVol { get; set; } = 0.60f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float HatVol { get; set; } = 0.22f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float CrashVol { get; set; } = 0.35f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float DrumVol { get; set; } = 1.00f;

	// ── Tone ──
	[Property, Group( "Tone" ), Range( 0f, 40f )] public float Detune { get; set; } = 14f;
	[Property, Group( "Tone" ), Range( 80f, 1200f )] public float BassCutoff { get; set; } = 380f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float SkankCutoff { get; set; } = 3000f;
	[Property, Group( "Tone" ), Range( 0f, 2000f )] public float SkankHighpass { get; set; } = 500f;
	[Property, Group( "Tone" ), Range( 0.15f, 1f )] public float SkankChop { get; set; } = 0.5f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float LeadCutoff { get; set; } = 3200f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float OrganCutoff { get; set; } = 1400f;
	[Property, Group( "Tone" ), Range( 0f, 12f )] public float OrganVibrato { get; set; } = 5.5f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float HornCutoff { get; set; } = 3200f;
	[Property, Group( "Tone" ), Range( 0.2f, 2f )] public float Resonance { get; set; } = 1.0f;
	[Property, Group( "Tone" ), Range( 1f, 4f )] public float BassDrive { get; set; } = 1.5f;
	[Property, Group( "Tone" ), Range( 1f, 4f )] public float SkankDrive { get; set; } = 1.3f;
	[Property, Group( "Tone" ), Range( 1f, 4f )] public float MelodyDrive { get; set; } = 1.3f;
	[Property, Group( "Tone" ), Range( 1f, 4f )] public float HornDrive { get; set; } = 1.4f;
	[Property, Group( "Tone" ), Range( 0.5f, 3f )] public float MasterDrive { get; set; } = 1.1f;
	[Property, Group( "Tone" ), Range( 0.2f, 1f )] public float MasterPeak { get; set; } = 0.95f;

	// ── Feel ──
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float OctavePopChance { get; set; } = 0.30f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float OrganBubbleChance { get; set; } = 0.55f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float KickSyncChance { get; set; } = 0.25f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float GhostSnareChance { get; set; } = 0.35f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float FillChance { get; set; } = 0.6f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float DrumBusy { get; set; } = 0.6f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float DrumTone { get; set; } = 0.5f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float DrumDrive { get; set; } = 0.5f;
	[Property, Group( "Feel" ), Range( 0f, 0.2f )] public float TripletChance { get; set; } = 0.06f;
	[Property, Group( "Feel" ), Range( 0f, 0.1f )] public float BassTriplets { get; set; } = 0.06f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float MelodyRestChance { get; set; } = 0.30f;
	[Property, Group( "Feel" ), Range( 0f, 1f )] public float MelodyLeapChance { get; set; } = 0.18f;
	[Property, Group( "Feel" ), Range( 0f, 12f )] public float MelodyVibrato { get; set; } = 5.0f;

	// ── Stereo ──
	[Property, Group( "Stereo" ), Range( 0f, 1f )] public float PanAmount { get; set; } = 0.4f;

	// ── Lead instrument (RNG picks one per tag, weighted; Force overrides) ──
	[Property, Group( "Instrument" ), Range( 0f, 4f )] public float TrumpetWeight { get; set; } = 1.0f;
	[Property, Group( "Instrument" ), Range( 0f, 4f )] public float SaxWeight { get; set; } = 1.0f;
	[Property, Group( "Instrument" ), Range( 0f, 4f )] public float OrganWeight { get; set; } = 0.8f;
	[Property, Group( "Instrument" ), Range( 0f, 4f )] public float TromboneWeight { get; set; } = 0.4f;
	/// <summary>-1 = RNG; 0=Trumpet 1=Sax 2=Organ 3=Trombone.</summary>
	[Property, Group( "Instrument" ), Range( -1, 3 )] public int ForceInstrument { get; set; } = -1;

	// ── Backing horns ──
	[Property, Group( "Horns" ), Range( 0f, 1f )] public float HornSectionChance { get; set; } = 0.5f;
	[Property, Group( "Horns" ), Range( 0f, 1f )] public float HornDensity { get; set; } = 0.35f;

	// ── Genre & rock instruments ──
	// Genre selects the instrument set: 0 = Ska, 1 = Rock (drums/bass/rhythm-gtr/lead-gtr).
	[Property, Group( "Genre" ), Range( 0, 1 )] public int Genre { get; set; } = 0;
	// KEYS — the offbeat-chord comp (was the "rhythm guitar"; it reads as keys).
	[Property, Group( "Rock" ), Range( 0f, 1.5f )] public float KeysVol { get; set; } = 1.00f;
	[Property, Group( "Rock" ), Range( 500f, 8000f )] public float KeysCutoff { get; set; } = 1700f;
	[Property, Group( "Rock" ), Range( 1f, 5f )] public float KeysDrive { get; set; } = 3.2f;
	[Property, Group( "Rock" ), Range( 0f, 1f )] public float KeysChug { get; set; } = 0.5f;
	// RHYTHM GTR — twangy distorted power chords (shares the lead voice, lower base distortion).
	[Property, Group( "Rock" ), Range( 0f, 1.5f )] public float RhythmGtrVol { get; set; } = 1.00f;
	[Property, Group( "Rock" ), Range( 500f, 8000f )] public float RhythmGtrCutoff { get; set; } = 2600f;
	[Property, Group( "Rock" ), Range( 1f, 5f )] public float RhythmGtrDrive { get; set; } = 2.8f;
	[Property, Group( "Rock" ), Range( 0f, 1f )] public float RhythmGtrChug { get; set; } = 0.5f;
	[Property, Group( "Rock" ), Range( 0f, 1.5f )] public float LeadGtrVol { get; set; } = 1.00f;
	[Property, Group( "Rock" ), Range( 500f, 8000f )] public float LeadGtrCutoff { get; set; } = 2600f;
	[Property, Group( "Rock" ), Range( 1f, 5f )] public float LeadGtrDrive { get; set; } = 3.6f;
	[Property, Group( "Rock" ), Range( 0f, 1f )] public float LeadGtrBend { get; set; } = 0.30f;

	SoundStream _stream;
	SoundHandle _handle;
	int _sr;

	short[] _curRaw;            // current song PCM (== _pcm[_curN]); kept for the crossfade + export
	// Navigable timeline (see issue #14). Two stores keyed by song index n:
	//  • _ledger  — n → frozen vibe seed. Under RandomEverySong a song's vibe is rolled ONCE here and
	//    then reused forever, so the "random" line is a fixed, reproducible path you can walk both ways.
	//    Kept for the whole session (just short strings). Non-random songs aren't stored (they track
	//    the live knobs). Cleared only on a full StartSequence (seed/genre/base-vibe change).
	//  • _pcm     — n → interleaved stereo PCM, pruned to |n − _curN| ≤ PcmCacheRadius so Prev/Next is
	//    instant within the window; outside it we regenerate from the ledger seed.
	readonly System.Collections.Generic.Dictionary<int, string> _ledger = new();
	readonly System.Collections.Generic.Dictionary<int, short[]> _pcm = new();
	// Per-song synthesis progress (0..1) for songs currently being generated; absent ⇒ not generating.
	readonly System.Collections.Generic.Dictionary<int, float> _genProgress = new();
	int _curN;                 // index of the currently-playing song
	// Per-instrument volumes, keyed by voice NAME (BASS, DRUMS, …) so the level follows the
	// instrument across genres. Pulled out of the vibe seed; persisted to FileSystem.Data and
	// overlaid onto every BuildConfig. See VibeCodec.ReadVolumes/ApplyVolumes.
	System.Collections.Generic.Dictionary<string, float> _vols = new();
	// Shared house-mix config (peak balances / kit presence) read from the addon's
	// skafinity.config.json — the SAME file the web toy uses. Overlaid onto every BuildConfig.
	System.Collections.Generic.Dictionary<string, float> _houseConfig = new();
	int _curReserve;           // samples of the current song's tail held back for the crossfade
	double _pushedSeconds;     // total audio pushed to the stream
	TimeSince _sinceStart;     // wall clock since playback started
	int _lastConfigHash;
	bool _dirty;
	TimeSince _dirtySince;
	bool _starting;            // StartSequenceAsync is in flight
	bool _fillingAhead;        // FillAhead is in flight
	bool _seeking;             // SeekToAsync (manual Prev/Next to an uncached n) is in flight
	int _bufferingN = -1;      // the song a foreground seek is waiting on, or -1 when not buffering
	bool Generating => _starting || _fillingAhead || _seeking;
	int _seq;                  // bumped on each StartSequence; stale async results are discarded
	bool _flatConfigured;      // ConfigureFlat applied to the live handle
	bool _restartPending;      // a debounced restart (vibe edit) is queued
	TimeSince _restartPendingSince;

	/// <summary>Currently-playing song index.</summary>
	public int N => _curN;
	/// <summary>The effective vibe of the *playing* song: its frozen ledger seed when one exists
	/// (so a shuffled song reports the vibe you actually hear), else the live knobs/override.</summary>
	public string CurrentVibe => _ledger.TryGetValue( _curN, out var v ) && !string.IsNullOrEmpty( v )
		? v : VibeCodec.Encode( BuildConfig() );
	/// <summary>Shareable seed for the playing song: <c>vibe:tag:n</c>. Accurate even under shuffle —
	/// it reproduces the exact song playing, because the vibe is the one frozen for this n.</summary>
	public string CurrentSeed => $"{CurrentVibe}:{SeedTag}:{_curN}";
	/// <summary>True once a stream handle is live and audible.</summary>
	public bool IsPlaying => _handle != null;
	/// <summary>True while any synthesis is in flight (foreground seek or background look-ahead fill).</summary>
	public bool IsGenerating => Generating;
	/// <summary>True while playback is stalled waiting on the song you asked to seek to (vs. silent
	/// background fill). Pair with <see cref="GenerationProgress"/> for a "Generating…" indicator.</summary>
	public bool IsBuffering => _bufferingN >= 0;

	/// <summary>One entry in the navigable timeline (see <see cref="Timeline"/>).</summary>
	public readonly struct QueueEntry
	{
		/// <summary>Song index in the infinite sequence.</summary>
		public int N { get; init; }
		/// <summary>The frozen vibe seed for this song, or "" if it isn't pinned yet (tracks live knobs).</summary>
		public string Vibe { get; init; }
		/// <summary>Genre id this song decodes to (see <see cref="VibeCodec.Genres"/>).</summary>
		public int Genre { get; init; }
		/// <summary>True if this song's PCM is resident (Prev/Next to it is instant).</summary>
		public bool Cached { get; init; }
		/// <summary>This song is the one currently playing.</summary>
		public bool Current { get; init; }
		/// <summary>0..1 synthesis progress if this song is being generated right now, else -1.</summary>
		public float Progress { get; init; }
	}

	/// <summary>Snapshot the timeline around the current song: <paramref name="back"/> history entries
	/// (n−back…n−1), the current song, and <paramref name="fwd"/> look-ahead entries (n+1…n+fwd). For
	/// the queue view; reflects cached-vs-needs-regenerate and in-flight generation state.</summary>
	public System.Collections.Generic.List<QueueEntry> Timeline( int back, int fwd )
	{
		var list = new System.Collections.Generic.List<QueueEntry>();
		int lo = Math.Max( 0, _curN - Math.Max( 0, back ) );
		int hi = _curN + Math.Max( 0, fwd );
		for ( int n = lo; n <= hi; n++ )
		{
			_ledger.TryGetValue( n, out var vibe );
			list.Add( new QueueEntry
			{
				N = n,
				Vibe = vibe ?? "",
				Genre = GenreOf( vibe ),
				Cached = _pcm.ContainsKey( n ),
				Current = n == _curN,
				Progress = _genProgress.TryGetValue( n, out var p ) ? p : -1f,
			} );
		}
		return list;
	}

	// Decode the genre a vibe seed resolves to (first base-36 char ⇒ genre). Null/empty ⇒ live genre.
	int GenreOf( string vibe )
	{
		if ( string.IsNullOrEmpty( vibe ) ) return BuildConfig().Genre;
		var c = BuildKnobConfig();
		VibeCodec.Apply( vibe, c );
		return c.Genre;
	}

	string SeedTag => string.IsNullOrEmpty( Tag ) ? "" : Tag;
	// Build the PRNG seed string from a resolved tag, so worker code never re-reads state.
	static string SeedFor( string tag, int n ) => $"{(string.IsNullOrEmpty( tag ) ? "skafinity" : tag.ToLowerInvariant())}:{n}";
	string Seed( int n ) => SeedFor( SeedTag, n );

	protected override void OnStart()
	{
		_lastConfigHash = ConfigHash();
		_curN = Math.Max( 0, PersistProgress ? LoadN() ?? StartN : StartN );
		_vols = LoadVols();
		_houseConfig = LoadHouseConfig();
		if ( AutoPlay ) StartSequence();
	}

	protected override void OnDestroy()
	{
		_seq++;            // invalidate any in-flight worker generation
		_handle?.Stop();
		_handle = null;
		_stream = null;
	}

	protected override void OnUpdate()
	{
		if ( _handle != null )
			_handle.Volume = TargetVolume();

		// Keep the forward look-ahead window of the PCM cache topped up. Generation runs on a worker
		// thread so this never blocks the frame.
		if ( !Generating && _curRaw != null && NeedsFill() )
			_ = FillAhead( _seq );

		// When the queued audio is about to run out, crossfade into the pre-rendered next song.
		if ( _stream != null && _curRaw != null && _pcm.ContainsKey( _curN + 1 )
			&& _pushedSeconds - _sinceStart < 2.0 )
			PushTransition();

		int h = ConfigHash();
		if ( h != _lastConfigHash )
		{
			_lastConfigHash = h;
			_dirty = true;
			_dirtySince = 0;
		}
		if ( _dirty && LiveReload && !Generating && _dirtySince > 0.5f )
		{
			_dirty = false;
			StartSequence();
		}

		// Debounced restart for vibe edits: only regenerate once edits have settled.
		if ( _restartPending && !Generating && _restartPendingSince > 0.35f )
		{
			_restartPending = false;
			StartSequence();
		}
	}

	/// <summary>Make the stream play as flat 2D (SpacialBlend=0, parented to the camera with
	/// FollowParent so the listener can't pan/attenuate it). Optionally routes to a named mixer.</summary>
	void ConfigureFlat()
	{
		if ( _handle == null || _flatConfigured ) return;
		_handle.SpacialBlend = 0f;
		var camGo = Scene?.Camera?.GameObject;
		if ( camGo.IsValid() )
		{
			_handle.Parent = camGo;
			_handle.FollowParent = true;
		}
		if ( !string.IsNullOrEmpty( MixerName ) )
		{
			var mixer = Sandbox.Audio.Mixer.FindMixerByName( MixerName );
			if ( mixer != null )
				_handle.TargetMixer = mixer;
		}
		_flatConfigured = true;
	}

	float TargetVolume() => Enabled ? Volume : 0f;

	/// <summary>The config currently in effect (inspector knobs with any <see cref="Vibe"/> applied).</summary>
	public MusicGen.Config EffectiveConfig() => BuildConfig();

	MusicGen.Config BuildConfig()
	{
		var cfg = BuildKnobConfig();
		// Shared house-mix baseline (peak balances / kit presence) from skafinity.config.json —
		// the same file the web toy reads. Independent of the vibe/volume knobs below.
		VibeCodec.ApplyAdvanced( _houseConfig, cfg );
		// A vibe override sets the important knobs (so a shared vibe:tag:n reproduces the same
		// voicing regardless of this client's inspector knobs).
		if ( !string.IsNullOrEmpty( Vibe ) )
			VibeCodec.Apply( Vibe, cfg );
		// Per-instrument volumes are NOT in the seed — overlay the persisted per-voice mix on top.
		VibeCodec.ApplyVolumes( cfg.Genre, _vols, cfg );
		return cfg;
	}

	// The frozen vibe seed for song n. Under RandomEverySong a song is rolled ONCE and pinned in the
	// ledger, so the random line is reproducible both directions; outside shuffle the song just tracks
	// the live knobs/override (not stored, so a knob edit + restart is always picked up).
	string VibeForN( int n )
	{
		if ( _ledger.TryGetValue( n, out var v ) ) return v;
		if ( !RandomEverySong ) return VibeCodec.Encode( BuildKnobOnlyVibe() );
		var rolled = RollVibe();
		_ledger[n] = rolled;
		return rolled;
	}

	// The vibe (override-or-knobs) the live player would encode, WITHOUT the house mix/volumes that
	// aren't part of the seed — used as the un-pinned vibe for non-shuffle songs.
	MusicGen.Config BuildKnobOnlyVibe()
	{
		var cfg = BuildKnobConfig();
		if ( !string.IsNullOrEmpty( Vibe ) ) VibeCodec.Apply( Vibe, cfg );
		return cfg;
	}

	// Roll a fresh random vibe seed (genre + every non-volume knob), as a string. Volumes are a local
	// mix preference and never ride in the seed. Mirrors RerollVibe's randomisation.
	string RollVibe()
	{
		var cfg = BuildKnobOnlyVibe();
		var rng = System.Random.Shared;
		cfg.Genre = rng.Next( VibeCodec.GenreCount );
		foreach ( var f in VibeCodec.Fields( cfg.Genre ) )
		{
			if ( f.Voice != null && f.Column == 0 ) continue; // skip per-instrument volumes
			f.SetNorm( cfg, rng.NextSingle() );
		}
		if ( cfg.BpmMin > cfg.BpmMax ) (cfg.BpmMin, cfg.BpmMax) = (cfg.BpmMax, cfg.BpmMin);
		return VibeCodec.Encode( cfg );
	}

	// The full Config to synthesise song n with: the live knobs + house mix + volumes, but with THIS
	// song's frozen vibe applied (not the player's single live Vibe). This is what makes each queued
	// song its own composition and keeps CurrentSeed honest.
	MusicGen.Config ConfigForN( int n )
	{
		var cfg = BuildKnobConfig();
		VibeCodec.ApplyAdvanced( _houseConfig, cfg );
		var vibe = VibeForN( n );
		if ( !string.IsNullOrEmpty( vibe ) ) VibeCodec.Apply( vibe, cfg );
		VibeCodec.ApplyVolumes( cfg.Genre, _vols, cfg );
		return cfg;
	}

	// Drop PCM outside the ±PcmCacheRadius window around the current song (ledger strings are kept).
	void PrunePcm()
	{
		int r = Math.Max( 0, PcmCacheRadius );
		var drop = new System.Collections.Generic.List<int>();
		foreach ( var n in _pcm.Keys )
			if ( Math.Abs( n - _curN ) > r ) drop.Add( n );
		foreach ( var n in drop ) _pcm.Remove( n );
	}

	MusicGen.Config BuildKnobConfig() => new()
	{
		SampleRate = SampleRate,
		TargetSeconds = TargetSeconds,
		BpmMin = BpmMin,
		BpmMax = BpmMax,
		FastChance = FastChance,
		FastBpmMin = FastBpmMin,
		FastBpmMax = FastBpmMax,
		Swing = Swing,
		FastSwing = FastSwing,
		BassVol = BassVol,
		SkankVol = SkankVol,
		OrganVol = OrganVol,
		MelodyVol = MelodyVol,
		HornVol = HornVol,
		KickVol = KickVol,
		SnareVol = SnareVol,
		TomVol = TomVol,
		HatVol = HatVol,
		CrashVol = CrashVol,
		DrumVol = DrumVol,
		Detune = Detune,
		BassCutoff = BassCutoff,
		SkankCutoff = SkankCutoff,
		SkankHighpass = SkankHighpass,
		SkankChop = SkankChop,
		LeadCutoff = LeadCutoff,
		OrganCutoff = OrganCutoff,
		OrganVibrato = OrganVibrato,
		HornCutoff = HornCutoff,
		Resonance = Resonance,
		BassDrive = BassDrive,
		SkankDrive = SkankDrive,
		MelodyDrive = MelodyDrive,
		HornDrive = HornDrive,
		MasterDrive = MasterDrive,
		MasterPeak = MasterPeak,
		OctavePopChance = OctavePopChance,
		OrganBubbleChance = OrganBubbleChance,
		KickSyncChance = KickSyncChance,
		GhostSnareChance = GhostSnareChance,
		FillChance = FillChance,
		DrumBusy = DrumBusy,
		TripletChance = TripletChance,
		BassTriplets = BassTriplets,
		MelodyRestChance = MelodyRestChance,
		MelodyLeapChance = MelodyLeapChance,
		MelodyVibrato = MelodyVibrato,
		PanAmount = PanAmount,
		TrumpetWeight = TrumpetWeight,
		SaxWeight = SaxWeight,
		OrganWeight = OrganWeight,
		TromboneWeight = TromboneWeight,
		ForceInstrument = ForceInstrument,
		HornSectionChance = HornSectionChance,
		HornDensity = HornDensity,
		Genre = Genre,
		DrumTone = DrumTone,
		DrumDrive = DrumDrive,
		KeysVol = KeysVol,
		KeysCutoff = KeysCutoff,
		KeysDrive = KeysDrive,
		KeysChug = KeysChug,
		RhythmGtrVol = RhythmGtrVol,
		RhythmGtrCutoff = RhythmGtrCutoff,
		RhythmGtrDrive = RhythmGtrDrive,
		RhythmGtrChug = RhythmGtrChug,
		LeadGtrVol = LeadGtrVol,
		LeadGtrCutoff = LeadGtrCutoff,
		LeadGtrDrive = LeadGtrDrive,
		LeadGtrBend = LeadGtrBend,
	};

	int ConfigHash()
	{
		var h = new HashCode();
		h.Add( SampleRate ); h.Add( TargetSeconds );
		h.Add( BpmMin ); h.Add( BpmMax ); h.Add( FastChance );
		h.Add( FastBpmMin ); h.Add( FastBpmMax ); h.Add( Swing ); h.Add( FastSwing );
		h.Add( BassVol ); h.Add( SkankVol ); h.Add( OrganVol ); h.Add( MelodyVol ); h.Add( HornVol );
		h.Add( KickVol ); h.Add( SnareVol ); h.Add( TomVol ); h.Add( HatVol ); h.Add( CrashVol ); h.Add( DrumVol );
		h.Add( Detune ); h.Add( BassCutoff ); h.Add( SkankCutoff ); h.Add( SkankHighpass ); h.Add( SkankChop );
		h.Add( LeadCutoff ); h.Add( OrganCutoff ); h.Add( OrganVibrato ); h.Add( HornCutoff ); h.Add( Resonance );
		h.Add( BassDrive ); h.Add( SkankDrive ); h.Add( MelodyDrive ); h.Add( HornDrive );
		h.Add( MasterDrive ); h.Add( MasterPeak );
		h.Add( OctavePopChance ); h.Add( OrganBubbleChance ); h.Add( KickSyncChance );
		h.Add( GhostSnareChance ); h.Add( FillChance );
		h.Add( DrumBusy ); h.Add( DrumTone ); h.Add( DrumDrive ); h.Add( TripletChance ); h.Add( BassTriplets );
		h.Add( MelodyRestChance ); h.Add( MelodyLeapChance ); h.Add( MelodyVibrato );
		h.Add( PanAmount );
		h.Add( TrumpetWeight ); h.Add( SaxWeight ); h.Add( OrganWeight ); h.Add( TromboneWeight );
		h.Add( ForceInstrument );
		h.Add( HornSectionChance ); h.Add( HornDensity );
		h.Add( Genre );
		h.Add( KeysVol ); h.Add( KeysCutoff ); h.Add( KeysDrive ); h.Add( KeysChug );
		h.Add( RhythmGtrVol ); h.Add( RhythmGtrCutoff ); h.Add( RhythmGtrDrive ); h.Add( RhythmGtrChug );
		h.Add( LeadGtrVol ); h.Add( LeadGtrCutoff ); h.Add( LeadGtrDrive ); h.Add( LeadGtrBend );
		h.Add( Tag ); h.Add( Vibe );
		return h.ToHashCode();
	}

	// Run the (pure, CPU-heavy) synthesis on worker threads so it never blocks the frame AND so
	// no single worker burst runs long enough to trip s&box's ~1000ms no-yield advisory.
	// Composition + drum synthesis are RNG-bound and stay sequential (BeginPlan, one worker); the
	// pitched voices pull no RNG, so they fan out across RenderThreads disjoint windows joined by
	// Task.WhenAll; the master+interleave runs on one worker. Result is interleaved stereo PCM.
	// progressN, when ≥ 0, is the song index whose 0..1 synthesis progress to publish into
	// _genProgress as the pipeline advances (plan → pitched-render jobs → master/interleave). s&box
	// marshals task continuations back to the main thread, so every SetProgress here runs on the main
	// thread alongside the UI reads — no locking needed.
	async Task<short[]> GenerateStereoAsync( string seedStr, MusicGen.Config cfg, int progressN = -1 )
	{
		SetProgress( progressN, 0.02f );
		MusicGen g = null;
		await GameTask.RunInThreadAsync( () => { g = MusicGen.BeginPlan( seedStr, cfg ); return Task.CompletedTask; } );
		SetProgress( progressN, 0.10f );

		int total = g.TotalSamples;
		int k = Math.Clamp( RenderThreads, 1, 8 );
		if ( k <= 1 )
		{
			await GameTask.RunInThreadAsync( () => { g.RenderPitchedRange( 0, total ); return Task.CompletedTask; } );
		}
		else
		{
			// WhenAny loop (rather than WhenAll) so the bar advances as each window finishes — the
			// pitched render is the long pole, so per-job ticks make the progress feel live.
			var jobs = new System.Collections.Generic.List<Task>( k );
			for ( int i = 0; i < k; i++ )
			{
				int from = (int)((long)total * i / k);
				int to = (int)((long)total * (i + 1) / k);
				jobs.Add( GameTask.RunInThreadAsync( () => { g.RenderPitchedRange( from, to ); return Task.CompletedTask; } ) );
			}
			int done = 0;
			while ( jobs.Count > 0 )
			{
				var finished = await Task.WhenAny( jobs );
				jobs.Remove( finished );
				SetProgress( progressN, 0.10f + 0.80f * (++done) / k );
			}
		}

		short[] pcm = null;
		await GameTask.RunInThreadAsync( () => { pcm = g.FinishStereo(); return Task.CompletedTask; } );
		_sr = g.SampleRate;
		SetProgress( progressN, 1f );
		return pcm;
	}

	void SetProgress( int n, float p ) { if ( n >= 0 ) _genProgress[n] = p; }

	// The forward look-ahead window we keep pre-rendered: AheadCount songs, but never beyond the PCM
	// cache radius (anything past it would just be pruned). Fill is needed when any slot is missing.
	int ForwardWindow => Math.Min( Math.Max( 1, AheadCount ), Math.Max( 1, PcmCacheRadius ) );
	bool NeedsFill()
	{
		for ( int n = _curN + 1; n <= _curN + ForwardWindow; n++ )
			if ( !_pcm.ContainsKey( n ) ) return true;
		return false;
	}

	int FadeFrames => Math.Max( 1, (int)(Math.Clamp( Crossfade, 0.25f, 8f ) * _sr) );

	// All look-ahead buffers are interleaved stereo PCM; lengths/offsets below are in frames.
	static int Frames( short[] pcm ) => pcm.Length / MusicGen.Channels;

	/// <summary>Pre-render the forward window (n+1…n+<see cref="ForwardWindow"/>) into the PCM cache,
	/// one song per iteration on a worker thread. Each song is built from its own ledger seed (so the
	/// queue is heterogeneous under shuffle, not one repeated vibe). Fire-and-forget from OnUpdate;
	/// <paramref name="seq"/> guards against a sequence restart landing a stale song in the cache.</summary>
	async Task FillAhead( int seq )
	{
		if ( Generating ) return;
		try
		{
			_fillingAhead = true;
			string tag = SeedTag;
			while ( seq == _seq && _curRaw != null )
			{
				// Generate the nearest missing forward slot (nearest first so an imminent crossfade
				// is satisfied before distant look-ahead).
				int target = -1;
				for ( int n = _curN + 1; n <= _curN + ForwardWindow; n++ )
					if ( !_pcm.ContainsKey( n ) ) { target = n; break; }
				if ( target < 0 ) break;

				var cfg = ConfigForN( target );   // resolves+freezes this song's vibe
				short[] song;
				try { song = await GenerateStereoAsync( SeedFor( tag, target ), cfg, target ); }
				finally { _genProgress.Remove( target ); }
				if ( seq != _seq ) return;        // sequence restarted while we were generating
				_pcm[target] = song;
			}
		}
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: FillAhead failed: {e.Message}" ); }
		finally { _fillingAhead = false; }
	}

	/// <summary>Write one pass of interleaved-stereo <paramref name="raw"/> to the stream, given the
	/// first <paramref name="headConsumed"/> frames were already emitted, holding back the final
	/// <paramref name="reserve"/> frames for the next crossfade. Optional fade-in over the first
	/// <paramref name="fadeIn"/> frames. Returns frames written.</summary>
	int WriteSongBody( short[] raw, int headConsumed, int reserve, int fadeIn )
	{
		const int ch = MusicGen.Channels;
		int rawFrames = raw.Length / ch;
		int start = headConsumed;
		int end = rawFrames - reserve;
		if ( end <= start ) return 0;
		int len = end - start;
		var seg = new short[len * ch];
		for ( int i = 0; i < len; i++ )
		{
			int frame = start + i;
			float g = (fadeIn > 0 && frame < fadeIn) ? (float)frame / fadeIn : 1f;
			for ( int c = 0; c < ch; c++ )
				seg[i * ch + c] = (short)(raw[frame * ch + c] * g);
		}
		_stream.WriteData( seg );
		return len;
	}

	/// <summary>(Re)start the infinite sequence at the current tag/n, rebuilding the timeline from
	/// scratch. Bumps the sequence token (invalidating any in-flight generation), clears the seed
	/// ledger AND the PCM cache (this is for seed/genre/base-vibe changes that invalidate the frozen
	/// line), stops the current handle, then kicks the async start so the caller never blocks. For a
	/// navigation that should PRESERVE the timeline (Prev/Next), use <see cref="SeekTo"/> instead.</summary>
	public void StartSequence()
	{
		int seq = ++_seq;
		_ledger.Clear();
		_pcm.Clear();
		_genProgress.Clear();
		_bufferingN = -1;
		// Pin the current song to the explicit base vibe (a pasted seed, a chosen genre, a reroll) so
		// it's honoured even under shuffle — shuffle still rolls fresh from n+1 onward. Empty vibe ⇒
		// unpinned (shuffle rolls the current song too; non-shuffle tracks the live knobs).
		if ( !string.IsNullOrEmpty( Vibe ) ) _ledger[Math.Max( 0, _curN )] = Vibe;
		_handle?.Stop();
		_handle = null;
		_stream = null;
		_flatConfigured = false;
		_curRaw = null;
		_ = StartSequenceAsync( seq );
	}

	// The current song fades in; thereafter songs play through once and crossfade into the
	// pre-rendered next. Synthesis is offloaded; stream setup is on the main thread.
	async Task StartSequenceAsync( int seq )
	{
		try
		{
			_starting = true;
			int n = Math.Max( 0, _curN );
			string tag = SeedTag;
			short[] raw;
			try { raw = await GenerateStereoAsync( SeedFor( tag, n ), ConfigForN( n ), n ); }
			finally { _genProgress.Remove( n ); }
			if ( seq != _seq ) return;   // superseded by a newer StartSequence

			_curN = n;
			_pcm[n] = raw;
			BeginAt( n );
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: StartSequence failed: {e.Message}" );
		}
		finally { if ( seq == _seq ) _starting = false; }   // don't let a superseded run clear a live flag
	}

	/// <summary>Navigate to song index <paramref name="n"/> while PRESERVING the timeline (ledger +
	/// PCM cache). If n's PCM is resident it plays instantly; otherwise playback stalls in a
	/// "buffering" state (<see cref="IsBuffering"/>) while it regenerates from n's ledger seed. This
	/// is what Prev/Next call — Prev replays the exact earlier songs, Next rolls a fresh genre on
	/// demand under shuffle. Restarts the SoundStream (a manual jump breaks the crossfade chain), but
	/// does NOT discard the timeline the way <see cref="StartSequence"/> does.</summary>
	public void SeekTo( int n )
	{
		n = Math.Max( 0, n );
		int seq = ++_seq;
		_handle?.Stop();
		_handle = null;
		_stream = null;
		_flatConfigured = false;
		_curRaw = null;
		_curN = n;
		if ( PersistProgress ) SaveN( _curN );
		PrunePcm();
		_ = SeekToAsync( seq, n );
	}

	async Task SeekToAsync( int seq, int n )
	{
		try
		{
			_seeking = true;
			short[] raw;
			if ( _pcm.TryGetValue( n, out var cached ) )
			{
				raw = cached;            // instant: within the cache window
			}
			else
			{
				_bufferingN = n;         // outside the window → surface a "Generating…" state
				string tag = SeedTag;
				try { raw = await GenerateStereoAsync( SeedFor( tag, n ), ConfigForN( n ), n ); }
				finally { _genProgress.Remove( n ); }
				if ( seq != _seq ) return;   // superseded by a newer seek/restart
				_pcm[n] = raw;
			}
			if ( seq != _seq ) return;
			BeginAt( n );
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: SeekTo failed: {e.Message}" );
		}
		finally { if ( seq == _seq ) { _seeking = false; _bufferingN = -1; } }
	}

	// Open the stream on song n's resident PCM: fade in from silence, hold back the tail for the next
	// crossfade, start the handle. Main-thread; n's PCM must already be in _pcm.
	void BeginAt( int n )
	{
		_curRaw = _pcm[n];
		int fade = Math.Min( FadeFrames, Frames( _curRaw ) / 3 );
		_curReserve = fade;

		_stream = new SoundStream( _sr, MusicGen.Channels );

		// One pass, fade-in over the first `fade`, last `fade` held back for the crossfade into n+1.
		int written = WriteSongBody( _curRaw, 0, _curReserve, fade );
		_pushedSeconds = written / (double)_sr;

		_handle = _stream.Play();
		if ( _handle != null )
		{
			_handle.Volume = TargetVolume();
			ConfigureFlat();
		}
		_sinceStart = 0;
		PrunePcm();
	}

	// Queue the crossfade from the current song's tail into the next (pre-rendered) song's head, then
	// the next song's body. Advances n (persisted if enabled); the cache window is re-pruned around
	// the new index and the next forward slot is topped up by OnUpdate. The next song's vibe was
	// already frozen in the ledger when FillAhead rendered it — under shuffle that's how the band
	// changes between songs, with no clear-and-regenerate churn and an accurate CurrentSeed.
	void PushTransition()
	{
		try
		{
			var next = _pcm[_curN + 1];

			// Crossfade window = the current song's held-back tail (so there's no gap or overlap
			// even when songs differ in length). The two songs only overlap for CrossfadeOverlap
			// of this window, centred — the rest plays in the clear.
			const int ch = MusicGen.Channels;
			int W = Math.Min( _curReserve, Frames( next ) / 3 );
			int curStart = Frames( _curRaw ) - W;
			int cross = Math.Clamp( (int)(W * CrossfadeOverlap), 1, W );
			int ws = (W - cross) / 2;     // overlap starts here
			int we = ws + cross;          // overlap ends here

			var xf = new short[W * ch];
			for ( int i = 0; i < W; i++ )
			{
				float gOut, gIn;
				if ( i < ws ) { gOut = 1f; gIn = 0f; }            // outgoing in the clear
				else if ( i >= we ) { gOut = 0f; gIn = 1f; }      // incoming in the clear
				else
				{
					double t = (i - ws + 0.5) / cross * (Math.PI / 2); // equal-power cross
					gOut = (float)Math.Cos( t );
					gIn = (float)Math.Sin( t );
				}
				for ( int c = 0; c < ch; c++ )
					xf[i * ch + c] = (short)Math.Clamp( _curRaw[(curStart + i) * ch + c] * gOut + next[i * ch + c] * gIn, -32768, 32767 );
			}
			_stream.WriteData( xf );

			// next song: one pass, first W already in the crossfade, last `nextReserve` held back
			// for the following crossfade.
			int nextReserve = Math.Min( FadeFrames, Frames( next ) / 3 );
			int written = WriteSongBody( next, W, nextReserve, 0 );
			_pushedSeconds += (W + written) / (double)_sr;

			_curRaw = next;
			_curReserve = nextReserve;
			_curN++;
			if ( PersistProgress ) SaveN( _curN );
			PrunePcm();   // n moved forward — drop anything now outside the ±radius window
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: PushTransition failed: {e.Message}" );
		}
	}

	// ── Public control surface ──

	// Parse a shareable seed in any of vibe:tag:n / tag:n / tag. Missing parts stay null.
	static void ParseSeed( string seed, out string vibe, out string tag, out int? n )
	{
		vibe = null; tag = null; n = null;
		seed = seed?.Trim();
		if ( string.IsNullOrEmpty( seed ) ) return;
		var p = seed.Split( ':' );
		if ( p.Length >= 3 ) { vibe = p[0]; tag = p[1]; if ( int.TryParse( p[2], out var v ) ) n = v; }
		else if ( p.Length == 2 )
		{
			if ( int.TryParse( p[1], out var v ) ) { tag = p[0]; n = v; }
			else if ( VibeCodec.LooksLikeVibe( p[0] ) ) { vibe = p[0]; tag = p[1]; }
			else tag = p[0];
		}
		else tag = p[0];
	}

	/// <summary>Play a shareable seed in any of the forms <c>vibe:tag:n</c>, <c>tag:n</c>, or
	/// <c>tag</c>. Missing components are left unchanged; a vibe is only applied when present.
	/// Restarts the sequence.</summary>
	public void PlaySeed( string seed )
	{
		ParseSeed( seed, out string vibe, out string tag, out int? n );
		if ( tag != null ) Tag = tag.Trim().ToLowerInvariant();
		if ( vibe != null ) Vibe = VibeCodec.LooksLikeVibe( vibe ) ? vibe.ToLowerInvariant() : "";
		if ( n.HasValue ) _curN = Math.Max( 0, n.Value );
		if ( PersistProgress ) SaveN( _curN );
		StartSequence();
	}

	/// <summary>Set just the seed tag (empty = the default "skafinity" seed). Restarts.</summary>
	public void SetTag( string tag )
	{
		Tag = string.IsNullOrEmpty( tag ) ? "" : tag.Trim().ToLowerInvariant();
		StartSequence();
	}

	/// <summary>Jump to song index n in the sequence (clamped ≥ 0), preserving the timeline. Plays
	/// cached PCM instantly when in the window, else buffers while regenerating from n's ledger seed.</summary>
	public void SetN( int n ) => SeekTo( n );

	/// <summary>Step the song index by <paramref name="delta"/> (e.g. +1 / -1), preserving the
	/// timeline so Prev replays the exact earlier songs.</summary>
	public void StepN( int delta ) => SeekTo( _curN + delta );
	/// <summary>Skip to the next song in the sequence.</summary>
	public void NextSong() => StepN( 1 );
	/// <summary>Step back to the previous song in the sequence.</summary>
	public void PrevSong() => StepN( -1 );

	/// <summary>Set vibe field <paramref name="index"/> (see <see cref="VibeCodec.Fields(int)"/>) from
	/// a 0..1 fraction, store the re-encoded <see cref="Vibe"/>, and restart on a short debounce.</summary>
	public void SetVibe( int index, float norm )
	{
		var cfg = BuildConfig();
		var fields = VibeCodec.Fields( cfg.Genre );
		if ( index < 0 || index >= fields.Count ) return;
		var f = fields[index];
		f.SetNorm( cfg, norm );
		if ( VibeCodec.IsVolume( f ) )
		{
			// Volume is a local mix preference, not part of the seed — store per-voice + persist.
			_vols[f.Voice] = norm;
			SaveVols();
		}
		else
		{
			Vibe = VibeCodec.Encode( cfg );
		}
		_restartPending = true;
		_restartPendingSince = 0;
	}

	/// <summary>Switch genre (rides in the vibe's first char): re-encode the effective config
	/// with the new genre into <see cref="Vibe"/> so it sticks over the inspector knobs, then
	/// restart. Use this rather than setting <see cref="Genre"/> directly — an existing
	/// <see cref="Vibe"/> override otherwise wins and the change wouldn't take.</summary>
	public void SetGenre( int genre )
	{
		var cfg = BuildConfig();
		cfg.Genre = Math.Clamp( genre, 0, VibeCodec.GenreCount - 1 );
		Vibe = VibeCodec.Encode( cfg );
		StartSequence();
	}

	/// <summary>Randomize the vibe knobs and restart on a short debounce. By default the
	/// per-instrument volumes (and genre) are left alone so a reroll re-voices without upending
	/// the mix; pass <paramref name="includeVolumes"/> / <paramref name="includeGenre"/> for a
	/// full shuffle. Pass <paramref name="restart"/> = false to re-voice without yanking the
	/// playhead — the caller is then responsible for letting the change take effect (e.g. by
	/// clearing the look-ahead so upcoming songs regenerate with the new vibe).</summary>
	public void RerollVibe( bool includeVolumes = false, bool includeGenre = false, bool restart = true )
	{
		var cfg = BuildConfig();
		var rng = System.Random.Shared;
		if ( includeGenre )
			cfg.Genre = rng.Next( VibeCodec.GenreCount );
		foreach ( var f in VibeCodec.Fields( cfg.Genre ) )
		{
			if ( !includeVolumes && f.Voice != null && f.Column == 0 ) continue; // skip per-instrument volumes
			f.SetNorm( cfg, rng.NextSingle() );
		}
		if ( cfg.BpmMin > cfg.BpmMax ) (cfg.BpmMin, cfg.BpmMax) = (cfg.BpmMax, cfg.BpmMin);
		Vibe = VibeCodec.Encode( cfg );
		if ( includeVolumes )
		{
			// Capture the freshly-randomized volumes into the persisted per-voice store (they
			// don't ride in the encoded vibe).
			foreach ( var kv in VibeCodec.ReadVolumes( cfg.Genre, cfg ) ) _vols[kv.Key] = kv.Value;
			SaveVols();
		}
		if ( restart )
		{
			_restartPending = true;
			_restartPendingSince = 0;
		}
	}

	/// <summary>Turn shuffle on/off and rebuild the forward timeline from the current song so the
	/// change takes immediately: ON freezes a fresh rolled vibe+genre per upcoming n, OFF reverts
	/// upcoming songs to the live knobs. History already played keeps whatever it was frozen as.</summary>
	public void SetRandomEverySong( bool on )
	{
		if ( RandomEverySong == on ) return;
		RandomEverySong = on;
		// Drop the frozen line from the current song forward so upcoming songs re-resolve under the
		// new mode; history (n < curN) keeps its frozen vibes so Prev still replays what you heard.
		var fwd = new System.Collections.Generic.List<int>();
		foreach ( var n in _ledger.Keys ) if ( n >= _curN ) fwd.Add( n );
		foreach ( var n in fwd ) _ledger.Remove( n );
		fwd.Clear();
		foreach ( var n in _pcm.Keys ) if ( n >= _curN ) fwd.Add( n );
		foreach ( var n in fwd ) _pcm.Remove( n );
		SeekTo( _curN );   // regenerate the current song under the new mode, keeping history cached
	}

	/// <summary>Write the playing song's raw loop (no fade) to a WAV under FileSystem.Data.
	/// Returns the filename written, or null on failure.</summary>
	public string SaveCurrentToFile()
	{
		if ( _curRaw == null || _sr <= 0 ) return null;
		var tag = string.IsNullOrEmpty( SeedTag ) ? "skafinity" : SeedTag.ToLowerInvariant();
		var name = $"{tag}_{_curN}.wav";
		try
		{
			FileSystem.Data.WriteAllBytes( name, MusicGen.WavFromSamples( _curRaw, 1, _sr ) );
			return name;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: save failed: {e.Message}" );
			return null;
		}
	}

	// ── Optional progress persistence (FileSystem.Data, see assets/file-system.md) ──
	string ProgressFile => $"skafinity_{(string.IsNullOrEmpty( SaveSlot ) ? "default" : SaveSlot)}.n";

	void SaveN( int n )
	{
		try { FileSystem.Data.WriteAllText( ProgressFile, n.ToString() ); }
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: save progress failed: {e.Message}" ); }
	}

	int? LoadN()
	{
		try
		{
			if ( FileSystem.Data.FileExists( ProgressFile )
				&& int.TryParse( FileSystem.Data.ReadAllText( ProgressFile ), out var v ) )
				return Math.Max( 0, v );
		}
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: load progress failed: {e.Message}" ); }
		return null;
	}

	// ── Per-instrument volume persistence (FileSystem.Data, keyed by SaveSlot) ──
	// Stored as JSON voice→0..1 level, separate from progress and from the (volume-free) seed,
	// so the mix is a local preference that survives sessions and follows each voice across genres.
	string VolumeFile => $"skafinity_{(string.IsNullOrEmpty( SaveSlot ) ? "default" : SaveSlot)}.vol";

	void SaveVols()
	{
		try { FileSystem.Data.WriteAllText( VolumeFile, Json.Serialize( _vols ) ); }
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: save volumes failed: {e.Message}" ); }
	}

	System.Collections.Generic.Dictionary<string, float> LoadVols()
	{
		try
		{
			if ( FileSystem.Data.FileExists( VolumeFile ) )
				return Json.Deserialize<System.Collections.Generic.Dictionary<string, float>>(
					FileSystem.Data.ReadAllText( VolumeFile ) ) ?? new();
		}
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: load volumes failed: {e.Message}" ); }
		return new();
	}

	// ── Shared house-mix config (read-only, shipped with the addon) ──
	// The SAME JSON the web toy uses (web/config.json is `make`-copied from the library's
	// skafinity.config.json). Its "advanced" block overlays the baseline peak-balance / level
	// mix onto every BuildConfig, so the house mix is retuned by editing one file rather than
	// recompiling. Read-only addon content → FileSystem.Mounted. See VibeCodec.ApplyAdvanced.
	const string HouseConfigFile = "skafinity.config.json";

	class HouseConfigDto { public System.Collections.Generic.Dictionary<string, float> advanced { get; set; } }

	System.Collections.Generic.Dictionary<string, float> LoadHouseConfig()
	{
		try
		{
			if ( FileSystem.Mounted.FileExists( HouseConfigFile ) )
			{
				var dto = Json.Deserialize<HouseConfigDto>( FileSystem.Mounted.ReadAllText( HouseConfigFile ) );
				return dto?.advanced ?? new();
			}
		}
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: load house config failed: {e.Message}" ); }
		return new();
	}
}
