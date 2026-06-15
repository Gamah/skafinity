using System;
using System.Threading.Tasks;
using Sandbox;

namespace Skafinity;

/// <summary>
/// Streams an endless, deterministic procedural ska / reggae-rock track (see
/// <see cref="MusicGen"/>) through Web Audio-style scheduling over a <see cref="SoundStream"/>.
///
/// Drop this <see cref="Component"/> on any GameObject. It generates a ~80s loop from the
/// seed <c>tag:n</c>, plays it <see cref="LoopsPerSong"/> times, then equal-power crossfades
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
	/// <summary>How many times each loop plays before crossfading on (2 = play through, loop once, switch).</summary>
	[Property, Group( "Crossfade" ), Range( 1, 4 )] public int LoopsPerSong { get; set; } = 2;
	/// <summary>How many upcoming songs to keep pre-generated (built one-per-tick so the fill never stalls a frame).</summary>
	[Property, Group( "Crossfade" ), Range( 1, 8 )] public int AheadCount { get; set; } = 5;

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
	[Property, Group( "Feel" ), Range( 0f, 0.3f )] public float DrumPush { get; set; } = 0.13f;
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

	SoundStream _stream;
	SoundHandle _handle;
	int _sr;

	short[] _curRaw;            // current song, full single loop (raw, for export)
	readonly System.Collections.Generic.List<short[]> _ahead = new(); // pre-generated n+1, n+2, …
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

	/// <summary>Currently-playing song index.</summary>
	public int N => _curN;
	/// <summary>The effective vibe — the override if set, else the encoded inspector knobs.</summary>
	public string CurrentVibe => VibeCodec.Encode( BuildConfig() );
	/// <summary>Shareable seed for the playing song: <c>vibe:tag:n</c>.</summary>
	public string CurrentSeed => $"{CurrentVibe}:{SeedTag}:{_curN}";
	/// <summary>True once a stream handle is live and audible.</summary>
	public bool IsPlaying => _handle != null;

	string SeedTag => string.IsNullOrEmpty( Tag ) ? "" : Tag;
	// Build the PRNG seed string from a resolved tag, so worker code never re-reads state.
	static string SeedFor( string tag, int n ) => $"{(string.IsNullOrEmpty( tag ) ? "skafinity" : tag.ToLowerInvariant())}:{n}";
	string Seed( int n ) => SeedFor( SeedTag, n );

	protected override void OnStart()
	{
		_lastConfigHash = ConfigHash();
		_curN = Math.Max( 0, PersistProgress ? LoadN() ?? StartN : StartN );
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

		// Keep the look-ahead buffer topped up. Generation runs on a worker thread so this
		// never blocks the frame.
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
		// A vibe override sets the important knobs (so a shared vibe:tag:n reproduces the same
		// voicing regardless of this client's inspector knobs).
		if ( !string.IsNullOrEmpty( Vibe ) )
			VibeCodec.Apply( Vibe, cfg );
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
		DrumPush = DrumPush,
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
		h.Add( DrumBusy ); h.Add( DrumPush ); h.Add( TripletChance ); h.Add( BassTriplets );
		h.Add( MelodyRestChance ); h.Add( MelodyLeapChance ); h.Add( MelodyVibrato );
		h.Add( PanAmount );
		h.Add( TrumpetWeight ); h.Add( SaxWeight ); h.Add( OrganWeight ); h.Add( TromboneWeight );
		h.Add( ForceInstrument );
		h.Add( HornSectionChance ); h.Add( HornDensity );
		h.Add( Tag ); h.Add( Vibe );
		return h.ToHashCode();
	}

	// Run the (pure, CPU-heavy) synthesis on worker threads so it never blocks the frame AND so
	// no single worker burst runs long enough to trip s&box's ~1000ms no-yield advisory.
	// Composition + drum synthesis are RNG-bound and stay sequential (BeginPlan, one worker); the
	// pitched voices pull no RNG, so they fan out across RenderThreads disjoint windows joined by
	// Task.WhenAll; the master+downmix runs on one worker.
	async Task<short[]> GenerateMonoAsync( string seedStr, MusicGen.Config cfg )
	{
		MusicGen g = null;
		await GameTask.RunInThreadAsync( () => { g = MusicGen.BeginPlan( seedStr, cfg ); return Task.CompletedTask; } );

		int total = g.TotalSamples;
		int k = Math.Clamp( RenderThreads, 1, 8 );
		if ( k <= 1 )
		{
			await GameTask.RunInThreadAsync( () => { g.RenderPitchedRange( 0, total ); return Task.CompletedTask; } );
		}
		else
		{
			var jobs = new Task[k];
			for ( int i = 0; i < k; i++ )
			{
				int from = (int)((long)total * i / k);
				int to = (int)((long)total * (i + 1) / k);
				jobs[i] = GameTask.RunInThreadAsync( () => { g.RenderPitchedRange( from, to ); return Task.CompletedTask; } );
			}
			await Task.WhenAll( jobs );
		}

		short[] mono = null;
		await GameTask.RunInThreadAsync( () => { mono = g.FinishMono(); return Task.CompletedTask; } );
		_sr = g.SampleRate;
		return mono;
	}

	int FadeSamples => Math.Max( 1, (int)(Math.Clamp( Crossfade, 0.25f, 8f ) * _sr) );

	/// <summary>Top the look-ahead buffer up to <see cref="AheadCount"/>, generating each song on
	/// a worker thread. Fire-and-forget from OnUpdate; <paramref name="seq"/> guards against a
	/// sequence restart landing a stale song in the buffer.</summary>
	async Task FillAhead( int seq )
	{
		if ( Generating ) return;
		try
		{
			_fillingAhead = true;
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
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: FillAhead failed: {e.Message}" ); }
		finally { _fillingAhead = false; }
	}

	/// <summary>Write <see cref="LoopsPerSong"/> passes of <paramref name="raw"/> to the stream,
	/// given the first <paramref name="headConsumed"/> samples of pass 0 were already emitted,
	/// holding back the final <paramref name="reserve"/> samples for the next crossfade. Optional
	/// fade-in over the first <paramref name="fadeIn"/> samples of pass 0. Returns samples written.</summary>
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

	/// <summary>(Re)start the infinite sequence at the current tag/n. Bumps the sequence token
	/// (invalidating any in-flight generation), stops the current handle, then kicks the async
	/// (worker-thread) start so the caller never blocks.</summary>
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

	// The first song fades in; thereafter songs play LoopsPerSong passes and crossfade into the
	// pre-generated next. Synthesis is offloaded; stream setup is on the main thread.
	async Task StartSequenceAsync( int seq )
	{
		try
		{
			_starting = true;
			int n = Math.Max( 0, _curN );
			string tag = SeedTag;
			var cfg = BuildConfig();
			var raw = await GenerateMonoAsync( SeedFor( tag, n ), cfg );
			if ( seq != _seq ) return;   // superseded by a newer StartSequence

			_curN = n;
			_curRaw = raw;
			int fade = Math.Min( FadeSamples, _curRaw.Length / 3 );
			_curReserve = fade;

			_stream = new SoundStream( _sr );

			// First song: LoopsPerSong passes, fade-in over the first `fade`, last `fade` of the
			// final pass held back for the crossfade into the next song.
			int written = WriteSongBody( _curRaw, 0, _curReserve, fade );
			_pushedSeconds = written / (double)_sr;

			_handle = _stream.Play();
			if ( _handle != null )
			{
				_handle.Volume = TargetVolume();
				ConfigureFlat();
			}
			_sinceStart = 0;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: StartSequence failed: {e.Message}" );
		}
		finally { _starting = false; }
	}

	// Queue the crossfade from the current song's tail into the next song's head, then the next
	// song's body. Advances n (persisted if enabled); the following song is topped up by OnUpdate.
	void PushTransition()
	{
		try
		{
			var next = _ahead[0];
			_ahead.RemoveAt( 0 );

			// Crossfade window = the current song's held-back tail (so there's no gap or overlap
			// even when songs differ in length). The two songs only overlap for CrossfadeOverlap
			// of this window, centred — the rest plays in the clear.
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

			// next song: LoopsPerSong passes, first W of pass 0 already in the crossfade, last
			// `nextReserve` of the final pass held back for the following crossfade.
			int nextReserve = Math.Min( FadeSamples, next.Length / 3 );
			int written = WriteSongBody( next, W, nextReserve, 0 );
			_pushedSeconds += (W + written) / (double)_sr;

			_curRaw = next;
			_curReserve = nextReserve;
			_curN++;
			if ( PersistProgress ) SaveN( _curN );
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

	/// <summary>Jump to song index n in the sequence (clamped ≥ 0). Restarts.</summary>
	public void SetN( int n )
	{
		_curN = Math.Max( 0, n );
		if ( PersistProgress ) SaveN( _curN );
		StartSequence();
	}

	/// <summary>Step the song index by <paramref name="delta"/> (e.g. +1 / -1). Restarts.</summary>
	public void StepN( int delta ) => SetN( _curN + delta );
	/// <summary>Skip to the next song in the sequence.</summary>
	public void NextSong() => StepN( 1 );
	/// <summary>Step back to the previous song in the sequence.</summary>
	public void PrevSong() => StepN( -1 );

	/// <summary>Set vibe field <paramref name="index"/> (see <see cref="VibeCodec.Fields"/>) from
	/// a 0..1 fraction, store the re-encoded <see cref="Vibe"/>, and restart on a short debounce.</summary>
	public void SetVibe( int index, float norm )
	{
		if ( index < 0 || index >= VibeCodec.Fields.Count ) return;
		var cfg = BuildConfig();
		VibeCodec.Fields[index].SetNorm( cfg, norm );
		Vibe = VibeCodec.Encode( cfg );
		_restartPending = true;
		_restartPendingSince = 0;
	}

	// Per-instrument volume fields are left alone by Reroll.
	static readonly System.Collections.Generic.HashSet<string> VolumeFields =
		new() { "BASS", "SKANK", "ORGAN", "LEAD", "HORNS", "DRUMS" };

	/// <summary>Randomize every vibe knob except the per-instrument volumes; store + restart once.</summary>
	public void RerollVibe()
	{
		var cfg = BuildConfig();
		var rng = System.Random.Shared;
		foreach ( var f in VibeCodec.Fields )
		{
			if ( VolumeFields.Contains( f.Name ) ) continue;
			f.SetNorm( cfg, rng.NextSingle() );
		}
		if ( cfg.BpmMin > cfg.BpmMax ) (cfg.BpmMin, cfg.BpmMax) = (cfg.BpmMax, cfg.BpmMin);
		Vibe = VibeCodec.Encode( cfg );
		_restartPending = true;
		_restartPendingSince = 0;
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
}
