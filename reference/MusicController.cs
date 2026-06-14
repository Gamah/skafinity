using System;
using System.Threading.Tasks;
using Rotaliate.Game;
using Sandbox;

namespace Rotaliate.Audio;

/// <summary>
/// Plays the procedural ska / reggae-rock track (see <see cref="MusicGen"/>) for the
/// local player. Lives on the Room GO in lobby.scene (per-client singleton, not
/// networked) so its generator knobs are authorable in the inspector.
///
/// Generates a track from the active player tag and loops it. The editor knobs tune
/// the GENERATOR globally (not per-tag); with <see cref="LiveReload"/> on, tweaking a
/// knob in play mode regenerates after a short settle. On/off + volume are world
/// settings (<see cref="PlayerData.MusicEnabled"/> / <see cref="PlayerData.MusicVolume"/>);
/// these editor values are the dev baseline they multiply against.
/// </summary>
public sealed class MusicController : Component
{
	public static MusicController Instance { get; private set; }

	// Playback uses SoundStream (raw PCM pushed from memory) — MusicPlayer reading a
	// WAV from FileSystem.Data returned a live handle but produced no audio. Nothing is
	// written to disk unless the player asks (SaveCurrentToFile, wired to a panel button).

	// ── Master (dev baseline; players toggle via world settings) ──
	[Property, Group( "Music" )] public bool Enabled { get; set; } = true;
	[Property, Group( "Music" ), Range( 0f, 2f )] public float Volume { get; set; } = 0.7f;
	[Property, Group( "Music" )] public bool LiveReload { get; set; } = true;

	// ── Output ──
	[Property, Group( "Output" ), Range( 8000, 48000 )] public int SampleRate { get; set; } = 32000;
	/// <summary>Target track length; bar count adapts to tempo to hit this.</summary>
	[Property, Group( "Output" ), Range( 30f, 180f )] public float TargetSeconds { get; set; } = 80f;

	// ── Tempo (main = laid-back reggae-rock; Fast = uptempo ska) ──
	[Property, Group( "Tempo" ), Range( 60, 200 )] public int BpmMin { get; set; } = 86;
	[Property, Group( "Tempo" ), Range( 60, 200 )] public int BpmMax { get; set; } = 104;
	[Property, Group( "Tempo" ), Range( 0f, 1f )] public float FastChance { get; set; } = 0.30f;
	[Property, Group( "Tempo" ), Range( 100, 220 )] public int FastBpmMin { get; set; } = 150;
	[Property, Group( "Tempo" ), Range( 100, 220 )] public int FastBpmMax { get; set; } = 168;
	[Property, Group( "Tempo" ), Range( 0f, 0.4f )] public float Swing { get; set; } = 0.14f;
	[Property, Group( "Tempo" ), Range( 0f, 0.4f )] public float FastSwing { get; set; } = 0.05f;

	// ── Mix ──
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float BassVol { get; set; } = 0.68f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float SkankVol { get; set; } = 0.95f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float OrganVol { get; set; } = 0.42f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float MelodyVol { get; set; } = 0.34f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float HornVol { get; set; } = 0.22f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float KickVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float SnareVol { get; set; } = 0.70f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float TomVol { get; set; } = 0.60f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float HatVol { get; set; } = 0.22f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float CrashVol { get; set; } = 0.35f;

	// ── Tone ──
	[Property, Group( "Tone" ), Range( 0f, 40f )] public float Detune { get; set; } = 14f;
	[Property, Group( "Tone" ), Range( 80f, 1200f )] public float BassCutoff { get; set; } = 380f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float SkankCutoff { get; set; } = 3000f;
	[Property, Group( "Tone" ), Range( 0f, 2000f )] public float SkankHighpass { get; set; } = 500f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float LeadCutoff { get; set; } = 3200f;
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
	[Property, Group( "Feel" ), Range( 0f, 0.2f )] public float TripletChance { get; set; } = 0.06f;
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

	// Crossfade window (also the first song's fade-in from silence), seconds. The two
	// songs only overlap (both audible) for CrossfadeOverlap of this window, centred —
	// so the rest of the window is the outgoing/incoming song playing in the clear.
	[Property, Group( "Music" ), Range( 0.5f, 8f )] public float Crossfade { get; set; } = 3.75f;
	[Property, Group( "Music" ), Range( 0f, 1f )] public float CrossfadeOverlap { get; set; } = 0.5f;
	/// <summary>How many times each song's loop plays before crossfading on (2 = play
	/// through, then loop once more, then switch).</summary>
	[Property, Group( "Music" ), Range( 1, 4 )] public int LoopsPerSong { get; set; } = 2;
	/// <summary>How many upcoming songs to keep pre-generated (built one-per-tick so the
	/// fill never stalls a frame).</summary>
	[Property, Group( "Music" ), Range( 1, 8 )] public int AheadCount { get; set; } = 5;

	SoundStream _stream;
	SoundHandle _handle;
	int _sr;

	short[] _curRaw;            // current song, full single loop (raw, for export)
	readonly System.Collections.Generic.List<short[]> _ahead = new(); // pre-generated songs n+1, n+2, …
	int _curN;                 // index of the currently-playing song
	int _curReserve;           // samples of the current song's tail held back for the crossfade
	double _pushedSeconds;     // total audio pushed to the stream
	TimeSince _sinceStart;     // wall clock since playback started
	int _lastConfigHash;
	bool _dirty;
	TimeSince _dirtySince;
	bool _starting;            // StartSequenceAsync is in flight
	bool _fillingAhead;        // FillAhead is in flight
	bool Generating => _starting || _fillingAhead;
	int _seq;                  // bumped on each StartSequence; stale async results are discarded
	bool _flatConfigured;      // ConfigureFlat applied to the live handle
	bool _restartPending;      // a debounced restart (vibe edit) is queued
	TimeSince _restartPendingSince;

	/// <summary>The seed tag in use (own player tag when no override is set).</summary>
	public string SeedTag
	{
		get
		{
			var t = PlayerData.Load()?.MusicTag;
			return string.IsNullOrEmpty( t ) ? (PlayerData.Load()?.PlayerTag ?? "") : t;
		}
	}
	/// <summary>Raw override tag ("" = own).</summary>
	public string TagField => PlayerData.Load()?.MusicTag ?? "";
	public int N => _curN;
	/// <summary>The effective vibe — the persisted override if set, else the encoded
	/// inspector knobs. Always populated so the seed round-trips.</summary>
	public string CurrentVibe => VibeCodec.Encode( BuildConfig() );
	/// <summary>Shareable seed for the playing song: <c>vibe:tag:n</c>.</summary>
	public string CurrentSeed => $"{CurrentVibe}:{SeedTag}:{_curN}";

	string Seed( int n ) => SeedFor( SeedTag, n );
	// Build the PRNG seed from an already-resolved tag — so worker/continuation code
	// never has to call back into PlayerData (SeedTag) off the main thread.
	static string SeedFor( string tag, int n ) => $"{( string.IsNullOrEmpty( tag ) ? "rotaliate" : tag.ToLowerInvariant() )}:{n}";

	protected override void OnStart()
	{
		Instance = this;
		_lastConfigHash = ConfigHash();
		_curN = Math.Max( 0, PlayerData.Load()?.MusicN ?? 0 );
		Log.Info( $"MusicController.OnStart: seed='{Seed( _curN )}' enabled={Enabled} vol={TargetVolume()}" );
		StartSequence();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
		_seq++;            // invalidate any in-flight worker generation
		_handle?.Stop();
		_handle = null;
		_stream = null;
	}

	protected override void OnUpdate()
	{
		if ( _handle != null )
			_handle.Volume = TargetVolume();

		// Keep the look-ahead buffer topped up. Generation runs on a worker thread
		// (FillAhead), so this never blocks the frame.
		if ( !Generating && _curRaw != null && _ahead.Count < Math.Max( 1, AheadCount ) )
			_ = FillAhead( _seq );

		// When the queued audio is about to run out, crossfade into the next song.
		if ( _stream != null && _ahead.Count > 0 && _curRaw != null
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

		// Debounced restart for vibe-slider edits: only regenerate once the player has
		// stopped dragging, so a sweep across ticks doesn't kick a generation per click.
		if ( _restartPending && !Generating && _restartPendingSince > 0.35f )
		{
			_restartPending = false;
			StartSequence();
		}
	}

	/// <summary>Make the stream play as flat 2D, the documented way: SpacialBlend=0 (per
	/// sound/playing-sounds.md, 0 = fully 2D) and — so the handle can't be panned/attenuated
	/// by the listener at all — parent it to the camera with FollowParent, the same
	/// mechanism GameObject.PlaySound uses to ride a moving object. Applied once per handle.</summary>
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
		// Route to the Music mixer instead of the default Game mixer (TargetMixer per
		// sound/playing-sounds.md; Mixer.FindMixerByName per media/video.md).
		var music = Sandbox.Audio.Mixer.FindMixerByName( "Music" );
		if ( music != null )
			_handle.TargetMixer = music;
		_flatConfigured = true;
	}

	float TargetVolume()
	{
		var d = PlayerData.Load();
		bool on = Enabled && (d?.MusicEnabled ?? true);
		float userVol = d?.MusicVolume ?? 1f;
		return on ? Volume * userVol : 0f;
	}

	MusicGen.Config BuildConfig()
	{
		var cfg = BuildKnobConfig();
		// A persisted vibe overrides the important knobs (so a shared vibe:tag:n
		// reproduces the same voicing regardless of this client's inspector knobs).
		var vibe = PlayerData.Load()?.MusicVibe;
		if ( !string.IsNullOrEmpty( vibe ) )
			VibeCodec.Apply( vibe, cfg );
		return cfg;
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
		Detune = Detune,
		BassCutoff = BassCutoff,
		SkankCutoff = SkankCutoff,
		SkankHighpass = SkankHighpass,
		LeadCutoff = LeadCutoff,
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
	};

	int ConfigHash()
	{
		var h = new HashCode();
		h.Add( SampleRate ); h.Add( TargetSeconds );
		h.Add( BpmMin ); h.Add( BpmMax ); h.Add( FastChance );
		h.Add( FastBpmMin ); h.Add( FastBpmMax ); h.Add( Swing ); h.Add( FastSwing );
		h.Add( BassVol ); h.Add( SkankVol ); h.Add( OrganVol ); h.Add( MelodyVol ); h.Add( HornVol );
		h.Add( KickVol ); h.Add( SnareVol ); h.Add( TomVol ); h.Add( HatVol ); h.Add( CrashVol );
		h.Add( Detune ); h.Add( BassCutoff ); h.Add( SkankCutoff ); h.Add( SkankHighpass );
		h.Add( LeadCutoff ); h.Add( Resonance );
		h.Add( BassDrive ); h.Add( SkankDrive ); h.Add( MelodyDrive ); h.Add( HornDrive );
		h.Add( MasterDrive ); h.Add( MasterPeak );
		h.Add( OctavePopChance ); h.Add( OrganBubbleChance ); h.Add( KickSyncChance );
		h.Add( GhostSnareChance ); h.Add( FillChance );
		h.Add( DrumBusy ); h.Add( TripletChance );
		h.Add( MelodyRestChance ); h.Add( MelodyLeapChance ); h.Add( MelodyVibrato );
		h.Add( PanAmount );
		h.Add( TrumpetWeight ); h.Add( SaxWeight ); h.Add( OrganWeight ); h.Add( TromboneWeight );
		h.Add( ForceInstrument );
		h.Add( HornSectionChance ); h.Add( HornDensity );
		return h.ToHashCode();
	}

	// Pure synthesis (no engine APIs) — safe to run on a worker thread. Returns one
	// full loop downmixed to mono plus the sample rate it was rendered at.
	static (short[] Mono, int Sr) GenMono( string seedStr, MusicGen.Config cfg )
	{
		var stereo = MusicGen.GenerateSamples( seedStr, cfg, out int sr );
		int frames = stereo.Length / MusicGen.Channels;
		var mono = new short[frames];
		for ( int i = 0; i < frames; i++ )
			mono[i] = (short)((stereo[i * 2] + stereo[i * 2 + 1]) / 2);
		return (mono, sr);
	}

	// Run the (pure, CPU-heavy) synthesis on a worker thread so it never blocks the
	// frame. The config/seed are captured on the main thread; the await resumes on the
	// main thread, where _sr is assigned. GameTask.RunInThreadAsync is the s&box
	// worker-thread API (see ../sbox-docs hotloading.md) — verify the signature in editor.
	// The seed string and config MUST be resolved on the main thread by the caller (they
	// read PlayerData); only the pure synthesis runs on the worker.
	async Task<short[]> GenerateMonoAsync( string seedStr, MusicGen.Config cfg )
	{
		(short[] Mono, int Sr) r = default;
		await GameTask.RunInThreadAsync( () => { r = GenMono( seedStr, cfg ); return Task.CompletedTask; } );
		_sr = r.Sr;
		return r.Mono;
	}

	int FadeSamples => Math.Max( 1, (int)(Math.Clamp( Crossfade, 0.25f, 8f ) * _sr) );

	/// <summary>Top the look-ahead buffer up to <see cref="AheadCount"/>, generating each
	/// song on a worker thread. Fire-and-forget from OnUpdate; <paramref name="seq"/> guards
	/// against a sequence restart landing a stale song in the buffer.</summary>
	async Task FillAhead( int seq )
	{
		if ( Generating ) return;
		try
		{
			_fillingAhead = true;
			// Resolve tag + config once, on the main thread, for all look-ahead songs.
			string tag = SeedTag;
			var cfg = BuildConfig();
			while ( seq == _seq && _curRaw != null && _ahead.Count < Math.Max( 1, AheadCount ) )
			{
				int n = _curN + 1 + _ahead.Count;
				var song = await GenerateMonoAsync( SeedFor( tag, n ), cfg );
				if ( seq != _seq ) return;   // sequence restarted while we were generating
				_ahead.Add( song );
			}
		}
		catch ( Exception e ) { Log.Warning( $"MusicController: FillAhead failed: {e.Message}" ); }
		finally { _fillingAhead = false; }
	}

	/// <summary>Write <see cref="LoopsPerSong"/> passes of <paramref name="raw"/> to the
	/// stream, given the first <paramref name="headConsumed"/> samples of pass 0 were
	/// already emitted (e.g. inside an incoming crossfade), holding back the final
	/// <paramref name="reserve"/> samples for the next crossfade. Optional fade-in over
	/// the first <paramref name="fadeIn"/> samples of pass 0. Returns samples written.</summary>
	int WriteSongBody( short[] raw, int headConsumed, int reserve, int fadeIn )
	{
		int loops = Math.Max( 1, LoopsPerSong );
		int total = 0;
		for ( int loop = 0; loop < loops; loop++ )
		{
			int start = loop == 0 ? headConsumed : 0;
			int end = loop == loops - 1 ? raw.Length - reserve : raw.Length;
			if ( end <= start ) continue;
			int len = end - start;
			var seg = new short[len];
			for ( int i = 0; i < len; i++ )
			{
				int idx = start + i;
				float g = (loop == 0 && fadeIn > 0 && idx < fadeIn) ? (float)idx / fadeIn : 1f;
				seg[i] = (short)(raw[idx] * g);
			}
			_stream.WriteData( seg );
			total += len;
		}
		return total;
	}

	/// <summary>(Re)start the infinite sequence at the current tag/n. Bumps the sequence
	/// token (invalidating any in-flight generation), stops the current handle, then kicks
	/// the async (worker-thread) start so the caller never blocks.</summary>
	public void StartSequence()
	{
		int seq = ++_seq;
		_ahead.Clear();
		_handle?.Stop();
		_handle = null;
		_stream = null;
		_flatConfigured = false;
		_curRaw = null;
		_ = StartSequenceAsync( seq );
	}

	// The first song fades in; thereafter songs play LoopsPerSong passes and crossfade
	// into the pre-generated next. Synthesis is offloaded; stream setup is on the main thread.
	async Task StartSequenceAsync( int seq )
	{
		try
		{
			_starting = true;
			int n = Math.Max( 0, PlayerData.Load()?.MusicN ?? 0 );
			string tag = SeedTag;
			var cfg = BuildConfig();
			var raw = await GenerateMonoAsync( SeedFor( tag, n ), cfg );
			if ( seq != _seq ) return;   // superseded by a newer StartSequence

			_curN = n;
			_curRaw = raw;
			int fade = Math.Min( FadeSamples, _curRaw.Length / 3 );
			_curReserve = fade;

			_stream = new SoundStream( _sr );

			// First song: LoopsPerSong passes, fade-in over the first `fade`, last `fade`
			// of the final pass held back for the crossfade into the next song.
			int written = WriteSongBody( _curRaw, 0, _curReserve, fade );
			_pushedSeconds = written / (double)_sr;

			_handle = _stream.Play();
			if ( _handle != null )
			{
				_handle.Volume = TargetVolume();
				ConfigureFlat();
			}
			_sinceStart = 0;

			Log.Info( $"MusicController: StartSequence seed='{Seed( _curN )}' sr={_sr} loop={_curRaw.Length / (float)_sr:0.0}s ×{LoopsPerSong} handle={( _handle == null ? "NULL" : "ok" )}" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"MusicController: StartSequence failed: {e.Message}" );
		}
		finally { _starting = false; }
	}

	// Queue the crossfade from the current song's tail into the next song's head, then
	// the next song's body. Advances n (persisted); the following song is topped up by
	// OnUpdate's look-ahead fill.
	void PushTransition()
	{
		try
		{
			// No generation here (look-ahead is filled separately), so don't touch the
			// in-flight flags — clobbering would let a second FillAhead start mid-flight.
			var next = _ahead[0];
			_ahead.RemoveAt( 0 );

			// Crossfade window = the current song's held-back tail (so there's no gap or
			// overlap even when songs differ in length). The two songs only overlap for
			// CrossfadeOverlap of this window, centred — the rest plays in the clear.
			int W = Math.Min( _curReserve, next.Length / 3 );
			int curStart = _curRaw.Length - W;
			int cross = Math.Clamp( (int)(W * CrossfadeOverlap), 1, W );
			int ws = (W - cross) / 2;     // overlap starts here
			int we = ws + cross;          // overlap ends here

			var xf = new short[W];
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
				xf[i] = (short)Math.Clamp( _curRaw[curStart + i] * gOut + next[i] * gIn, -32768, 32767 );
			}
			_stream.WriteData( xf );

			// next song: LoopsPerSong passes, first W of pass 0 already in the crossfade,
			// last `nextReserve` of the final pass held back for the following crossfade.
			int nextReserve = Math.Min( FadeSamples, next.Length / 3 );
			int written = WriteSongBody( next, W, nextReserve, 0 );
			_pushedSeconds += (W + written) / (double)_sr;

			_curRaw = next;
			_curReserve = nextReserve;
			_curN++;
			var data = PlayerData.Load() ?? new PlayerData();
			data.MusicN = _curN;
			data.Save();
		}
		catch ( Exception e )
		{
			Log.Warning( $"MusicController: PushTransition failed: {e.Message}" );
		}
	}

	/// <summary>Play a pasted shareable seed in any of the forms <c>vibe:tag:n</c>,
	/// <c>tag:n</c>, or <c>tag</c>. Missing components are left unchanged; a vibe is only
	/// applied when one is present. Persists; restarts.</summary>
	public void PlaySeed( string seed )
	{
		string vibe = null, tag = null; int? n = null;
		seed = seed?.Trim();
		if ( !string.IsNullOrEmpty( seed ) )
		{
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

		var own = (PlayerData.Load()?.PlayerTag ?? "").ToLowerInvariant();
		var data = PlayerData.Load() ?? new PlayerData();
		tag = tag?.Trim().ToLowerInvariant();
		data.MusicTag = (string.IsNullOrEmpty( tag ) || tag == own) ? "" : tag;
		if ( vibe != null ) data.MusicVibe = VibeCodec.LooksLikeVibe( vibe ) ? vibe.ToLowerInvariant() : "";
		if ( n.HasValue ) data.MusicN = Math.Max( 0, n.Value );
		data.Save();
		StartSequence();
	}

	/// <summary>Set just the seed tag (empty/own tag = back to your own). Persists; restarts.</summary>
	public void SetTag( string tag ) => PlaySeed( tag );

	/// <summary>The config currently in effect (inspector knobs with any persisted vibe
	/// applied) — the source the music panel reads its vibe slider positions from.</summary>
	public MusicGen.Config EffectiveConfig() => BuildConfig();

	/// <summary>Set vibe field <paramref name="index"/> (see <see cref="VibeCodec.Fields"/>)
	/// from a 0..1 fraction, persist the re-encoded vibe, and restart so it's audible.</summary>
	public void SetVibe( int index, float norm )
	{
		if ( index < 0 || index >= VibeCodec.Fields.Count ) return;
		var cfg = BuildConfig();
		VibeCodec.Fields[index].SetNorm( cfg, norm );
		var data = PlayerData.Load() ?? new PlayerData();
		data.MusicVibe = VibeCodec.Encode( cfg );
		data.Save();
		// The panel reads slider positions from the saved vibe immediately; the audio
		// restarts on a short debounce (OnUpdate) so a drag across ticks isn't a
		// generation storm.
		_restartPending = true;
		_restartPendingSince = 0;
	}

	/// <summary>Back to your own tag and your own (knob) vibe. Persists; restarts.</summary>
	public void UseOwn()
	{
		var data = PlayerData.Load() ?? new PlayerData();
		data.MusicTag = "";
		data.MusicVibe = "";
		data.Save();
		StartSequence();
	}

	/// <summary>Jump to song index n in the sequence (clamped ≥ 0). Persists; restarts.</summary>
	public void SetN( int n )
	{
		var data = PlayerData.Load() ?? new PlayerData();
		data.MusicN = Math.Max( 0, n );
		data.Save();
		StartSequence();
	}

	public void StepN( int delta ) => SetN( _curN + delta );

	/// <summary>Write the playing song's raw loop (no fade) to a WAV under
	/// FileSystem.Data. Returns the filename, or null on failure.</summary>
	public string SaveCurrentToFile()
	{
		if ( _curRaw == null || _sr <= 0 ) return null;
		var tag = string.IsNullOrEmpty( SeedTag ) ? "unknown" : SeedTag.ToLowerInvariant();
		var name = $"{tag}_{_curN}.wav";
		try
		{
			FileSystem.Data.WriteAllBytes( name, MusicGen.WavFromSamples( _curRaw, 1, _sr ) );
			return name;
		}
		catch ( Exception e )
		{
			Log.Warning( $"MusicController: save failed: {e.Message}" );
			return null;
		}
	}

	public void ApplyUserSettings()
	{
		if ( _handle == null && !Generating && TargetVolume() > 0f ) StartSequence();
		else if ( _handle != null ) _handle.Volume = TargetVolume();
	}
}
