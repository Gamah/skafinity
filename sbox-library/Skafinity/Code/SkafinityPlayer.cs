using System;
using System.Threading.Tasks;
using Sandbox;

namespace Skafinity;

/// <summary>
/// Streams an endless, deterministic procedural song sequence — ska, rock, country, metal,
/// punk or pop, per the seed's genre (see
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
// DontExecuteOnServer: Skafinity is client-only (audio + UI). On a dedicated server the engine
// skips this component's OnStart/OnEnabled/OnUpdate, so MusicGen/VibeCodec are never driven and
// nothing tries to render or play audio on a headless host. Runtime marker (not #if SERVER) so the
// type still exists in every build and consumers compile unchanged.
public sealed class SkafinityPlayer : Component, Component.DontExecuteOnServer
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
	/// <summary>Roll a fresh GENRE for each new song, the way a seed with no genre part does. Off =
	/// <see cref="Genre"/> is pinned and every song plays it. ON by default.</summary>
	/// <remarks>The genre and the vibe get a switch each because the SEED gives them a part each:
	/// <c>tag:n[:genre][:vibe]</c> pins either alone, so one switch over both could not express half
	/// the seeds the engine parses (pin a genre and let vibes roll, or the reverse). These two ARE
	/// that pinning — <see cref="StationSeed"/> writes down whatever they leave rolling.</remarks>
	[Property, Group( "Music" )] public bool RandomGenreEverySong { get; set; } = true;
	/// <summary>Roll a fresh VIBE (every knob but the per-instrument volumes, which are a local mix
	/// preference) for each new song. Off = the live knobs / the <see cref="Vibe"/> override are
	/// pinned and every song plays them. ON by default — endless variety out of the box.</summary>
	/// <inheritdoc cref="RandomGenreEverySong" path="/remarks"/>
	[Property, Group( "Music" )] public bool RandomVibeEverySong { get; set; } = true;
	/// <summary>What NEXT means. Off (the default): walk this station — position p is song p of it,
	/// so Prev replays exactly what was heard and a shared link describes the line. On: every next
	/// song is a whole NEW station at song 0.</summary>
	/// <remarks>Deliberately NOT "a different vibe every song" — that is what a seed with nothing
	/// pinned already does, and it needs no switch (see <see cref="RandomVibeEverySong"/>). The
	/// stations a shuffled line visits are DERIVED from the root tag
	/// (<see cref="SeedCodec.RollTagFor"/>) rather than drawn fresh, which is what keeps a shuffled
	/// line a line: nothing is remembered, and the whole thing still reproduces from one string.</remarks>
	[Property, Group( "Music" )] public bool Shuffle { get; set; }

	// ── Seed ──
	/// <summary>Seed tag — any string (a name, a word). Empty falls back to "skafinity".</summary>
	[Property, Group( "Seed" )] public string Tag { get; set; } = "";
	/// <summary>Song index in the infinite sequence (0,1,2…). <see cref="StepN"/>/<see cref="NextSong"/> walk it.</summary>
	[Property, Group( "Seed" )] public int StartN { get; set; } = 0;
	/// <summary>Optional base-36 vibe override (see <see cref="VibeCodec"/>). When set it overrides
	/// the matching inspector knobs, so a shared vibe reproduces the same voicing on any client.</summary>
	[Property, Group( "Seed" )] public string Vibe { get; set; } = "";
	/// <summary>Persist the player state across sessions (FileSystem.Data, keyed by <see cref="SaveSlot"/>):
	/// the seed (tag + song index + vibe) and the listening settings not covered by the seed
	/// (shuffle, mute, volume). ON by default so the player picks up where it left off.</summary>
	[Property, Group( "Seed" )] public bool PersistProgress { get; set; } = true;
	[Property, Group( "Seed" )] public string SaveSlot { get; set; } = "default";

	// ── Output ──
	/// <summary>Render rate. Deliberately below the engine's own 44100 default: a game renders
	/// songs while it is also drawing frames, and the synthesis cost is linear in this. It is the
	/// one place this player is meant to disagree with <see cref="MusicGen.Config"/>.</summary>
	[Property, Group( "Output" ), Range( 8000, 48000 )] public int SampleRate { get; set; } = 32000;
	/// <summary>Worker threads the pitched-voice synthesis is split across (composition + drums
	/// stay single-threaded). Keeps each worker burst under s&amp;box's ~1000ms no-yield advisory.</summary>
	[Property, Group( "Output" ), Range( 1, 8 )] public int RenderThreads { get; set; } = 6;

	// ── Crossfade / scheduling ──
	/// <summary>Crossfade window between songs, seconds. The two songs are only both-audible for
	/// <see cref="CrossfadeOverlap"/> of this, centred. The first song's fade up from silence is
	/// separate and much shorter — see <c>StartFadeSeconds</c>.</summary>
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

	// ── Tempo ──
	// The tempo BAND belongs to the genre (Engine/GenreProfile.cs), not to a property here.
	// TEMPO IS THE GENRE'S AND NOTHING ELSE REACHES IT. Two [Property] knobs used to sit here
	// mirroring Config.TempoScale / Config.FastChance; both are gone (see VibeCodec's reserved
	// slots for why), and a host property that overrides an anchored tempo band is exactly the
	// drift this file's own header warns about.

	// ── Mix ──
	// These are the PLAYER's overlay on the engine's own defaults, so a value here that isn't 1.0
	// is this host disagreeing with the shipped mix. It shouldn't: the baseline balance between
	// the kit voices is measured (the *Balance entries in skafinity.config.json, applied through
	// ApplyAdvanced) and a per-voice trim here double-dips against it. Keep them at 1.0 and retune
	// the shared config — that is the file both targets read.
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float BassVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float SkankVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float OrganVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float MelodyVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float HornVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float KickVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float SnareVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float TomVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float HatVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float CrashVol { get; set; } = 1.00f;
	[Property, Group( "Mix" ), Range( 0f, 1.5f )] public float DrumVol { get; set; } = 1.00f;

	// ── Tone ──
	[Property, Group( "Tone" ), Range( 0f, 20f )] public float Detune { get; set; } = 7f;
	[Property, Group( "Tone" ), Range( 80f, 1200f )] public float BassCutoff { get; set; } = 380f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float SkankCutoff { get; set; } = 3000f;
	[Property, Group( "Tone" ), Range( 0f, 2000f )] public float SkankHighpass { get; set; } = 500f;
	[Property, Group( "Tone" ), Range( 0.15f, 1f )] public float SkankChop { get; set; } = 0.5f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float LeadCutoff { get; set; } = 3200f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float OrganCutoff { get; set; } = 1400f;
	[Property, Group( "Tone" ), Range( 0f, 12f )] public float OrganVibrato { get; set; } = 5.5f;
	[Property, Group( "Tone" ), Range( 500f, 8000f )] public float HornCutoff { get; set; } = 3200f;
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
	/// <summary>Master reverb send — the GLOBAL "REVERB" vibe knob's resting value.</summary>

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

	// ── Genre ──
	/// <summary>Which genre the song is: 0 = Ska-Punk, 1 = Rock, 2 = Country, 3 = Metal, 4 = Punk,
	/// 5 = Pop. Prefer <see cref="SetGenre"/> at runtime — an existing <see cref="Vibe"/> carries a
	/// genre of its own in its first character and otherwise wins over this.</summary>
	/// <remarks>The upper bound tracks <c>VibeCodec.GenreCount</c>. A <c>[Range]</c> takes a
	/// constant, so adding a genre means bumping it here too; out-of-range values are clamped
	/// rather than trusted, so a stale bound only ever costs the inspector its reach.</remarks>
	[Property, Group( "Genre" ), Range( 0, 5 )] public int Genre { get; set; } = 0;

	// ── Guitars / keys ──
	// Not rock-only any more: every genre except ska draws its chordal voice from KEYS or
	// RHYTHM GTR, ska's loud sections play RHYTHM GTR as the chorus guitar (GenreProfile.LoudComp),
	// and LEAD GTR is the lead everywhere the genre isn't horn-led. Ranges here span every genre's
	// grid in VibeCodec, which is why the DISTORTION bounds are wider than any single genre's.
	// KEYS — the chordal comp (held/offbeat voicings; rock, metal and pop route to it).
	[Property, Group( "Guitars / Keys" ), Range( 0f, 1.5f )] public float KeysVol { get; set; } = 1.00f;
	[Property, Group( "Guitars / Keys" ), Range( 500f, 8000f )] public float KeysCutoff { get; set; } = 1700f;
	[Property, Group( "Guitars / Keys" ), Range( 1f, 5f )] public float KeysDrive { get; set; } = 3.2f;
	[Property, Group( "Guitars / Keys" ), Range( 0f, 1f )] public float KeysChug { get; set; } = 0.5f;
	// RHYTHM GTR — strummed/chugged chords: country's clean strum, punk's downstroke, metal's
	// tremolo, ska's chorus power chords.
	[Property, Group( "Guitars / Keys" ), Range( 0f, 1.5f )] public float RhythmGtrVol { get; set; } = 1.00f;
	[Property, Group( "Guitars / Keys" ), Range( 500f, 8000f )] public float RhythmGtrCutoff { get; set; } = 2600f;
	[Property, Group( "Guitars / Keys" ), Range( 1f, 6f )] public float RhythmGtrDrive { get; set; } = 2.8f;
	[Property, Group( "Guitars / Keys" ), Range( 0f, 1f )] public float RhythmGtrChug { get; set; } = 0.5f;
	// LEAD GTR — the sung lead wherever the genre isn't horn-led (and pop's plucky synth, whose
	// knob is a GLIDE rather than a bend).
	[Property, Group( "Guitars / Keys" ), Range( 0f, 1.5f )] public float LeadGtrVol { get; set; } = 1.00f;
	[Property, Group( "Guitars / Keys" ), Range( 500f, 8000f )] public float LeadGtrCutoff { get; set; } = 2600f;
	// Rock and punk floor this knob at 5 — the old top of the range — so the default sits there.
	[Property, Group( "Guitars / Keys" ), Range( 1f, 11f )] public float LeadGtrDrive { get; set; } = 5.0f;
	[Property, Group( "Guitars / Keys" ), Range( 0f, 1f )] public float LeadGtrBend { get; set; } = 0.30f;

	SoundStream _stream;
	SoundHandle _handle;
	int _sr;

	short[] _curRaw;            // current song PCM (== _pcm[_pos]); kept for the crossfade + export
	// Navigable timeline (see issue #14). Every store here is keyed by timeline POSITION, not by song
	// index — the two are the same thing with shuffle off and are not with it on (see SongAt):
	//  • _ledger  — p → frozen vibe seed, and _genreLedger — p → frozen genre. A song's rolled vibe
	//    and genre are DERIVED from the (tag, n) that position resolves to, so these are caches
	//    rather than state: dropping an entry re-derives the identical value, which is what makes the
	//    line walkable both ways, shareable, and stable across a reload. Genre and vibe are separate
	//    stores because a seed may pin either one alone. Cleared only on a full StartSequence.
	//  • _pcm     — p → interleaved stereo PCM, pruned to |p − _pos| ≤ PcmCacheRadius so Prev/Next is
	//    instant within the window; outside it we regenerate from the seed that position derives.
	readonly System.Collections.Generic.Dictionary<int, string> _ledger = new();
	readonly System.Collections.Generic.Dictionary<int, int> _genreLedger = new();
	readonly System.Collections.Generic.Dictionary<int, short[]> _pcm = new();
	// Per-song synthesis progress (0..1) for songs currently being generated; absent ⇒ not generating.
	readonly System.Collections.Generic.Dictionary<int, float> _genProgress = new();
	int _pos;                  // timeline position of the AUDIBLE song (see _scheduled)
	int _writePos;             // …and of the song at the WRITE head, which runs ahead of it
	// Where the seed joined the line. With shuffle off this is just the song index position 0 starts
	// counting from; with it on, position 0 is the seed's own song and every other position is a
	// station of its own, so the two cannot be the same number.
	int _baseN;
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
	// WHAT IS AUDIBLE, as opposed to what has been written. A SoundStream is a FIFO, so a song is
	// pushed seconds before it can be heard — the crossfade into the next song is queued while the
	// current one still has bars to play. Advancing the "now playing" index at push time is what made
	// the old board name the next song early; these entries carry the stream time each song's first
	// sample lands at, and OnUpdate promotes one to audible when the clock reaches it. That is also
	// where the seek bar's position comes from, so the two cannot disagree.
	readonly System.Collections.Generic.List<Playing> _scheduled = new();
	readonly record struct Playing( int Pos, double StartSeconds, double Offset, double Duration );
	// Paused is NOT stopped: the stream is torn down (a SoundStream cannot be rewound) and this is
	// how far into the audible song it was, so the next play comes back in where it left off rather
	// than at the top of the song. Also where a scrub lands while nothing is playing.
	bool _paused;
	double _resumeOffset;
	int _lastConfigHash;
	bool _dirty;
	TimeSince _dirtySince;
	bool _starting;            // StartSequenceAsync is in flight
	bool _fillingAhead;        // FillAhead is in flight
	bool _seeking;             // SeekToAsync (manual Prev/Next to an uncached n) is in flight
	int _bufferingPos = -1;      // the song a foreground seek is waiting on, or -1 when not buffering
	bool Generating => _starting || _fillingAhead || _seeking;
	int _seq;                  // bumped on each StartSequence; stale async results are discarded
	bool _flatConfigured;      // ConfigureFlat applied to the live handle
	bool _restartPending;      // a debounced restart (vibe edit) is queued
	TimeSince _restartPendingSince;

	/// <summary>Song index of the playing song WITHIN ITS OWN STATION — what "now playing #12" means
	/// and what a seed writes down. Under shuffle every position is its own station at song 0, so
	/// this is 0 for every song but the one the seed named; <see cref="Position"/> is the slot on the
	/// timeline and is what Prev/Next and the playlist address.</summary>
	public int N => SongAt( _pos ).N;
	/// <summary>Slot on the timeline of the playing song. Equal to <see cref="N"/> with
	/// <see cref="Shuffle"/> off, and the thing to pass to <see cref="SeekTo"/> either way.</summary>
	public int Position => _pos;
	/// <summary>The effective vibe of the *playing* song: its frozen ledger seed when one exists
	/// (so a shuffled song reports the vibe you actually hear), else the live knobs/override.</summary>
	public string CurrentVibe => VibeForPos( _pos );
	/// <summary>Shareable seed for the playing song: <c>tag:n:genre:vibe</c>, FULLY RESOLVED — the
	/// genre and vibe are written down even when this player rolled them, so whoever is handed it
	/// hears this song rather than whatever their own station rolls at that index.</summary>
	public string CurrentSeed
	{
		get
		{
			var s = SongAt( _pos );
			return SeedCodec.Format( new SeedCodec.Seed
			{
				Tag = s.Tag, N = s.N, Genre = GenreForPos( _pos ), Vibe = VibeForPos( _pos ),
			} );
		}
	}

	/// <summary>Shareable seed for the STATION: the seed exactly as it stands, so whatever this
	/// player left rolling keeps rolling for whoever is handed it. The counterpart to
	/// <see cref="CurrentSeed"/>, and the reason there are two copy buttons — "share this" means one
	/// of two different things, and neither can be recovered from the other.</summary>
	public string StationSeed
	{
		get
		{
			var s = SongAt( _pos );
			return SeedCodec.Format( new SeedCodec.Seed
			{
				Tag = s.Tag,
				N = s.N,
				Genre = RandomGenreEverySong ? SeedCodec.RolledGenre : Math.Clamp( Genre, 0, VibeCodec.GenreCount - 1 ),
				Vibe = RandomVibeEverySong ? null : VibeForPos( _pos ),
			} );
		}
	}
	/// <summary>True once a stream handle is live and audible.</summary>
	public bool IsPlaying => _handle != null;
	/// <summary>Stopped on purpose, holding its place in the song — the ⏸ state, as opposed to the
	/// silence of a song still being generated. A resume comes back in where this was taken.</summary>
	public bool IsPaused => _paused;
	/// <summary>True while any synthesis is in flight (foreground seek or background look-ahead fill).</summary>
	public bool IsGenerating => Generating;
	/// <summary>True while playback is stalled waiting on the song you asked to seek to (vs. silent
	/// background fill). Pair with <see cref="Timeline"/>'s per-entry progress for a "Generating…"
	/// indicator.</summary>
	public bool IsBuffering => _bufferingPos >= 0;
	/// <summary>How many house-mix values were read out of <c>skafinity.config.json</c>. Zero means
	/// the file wasn't mounted, and the baseline mix is the engine's compiled defaults rather than
	/// the shared one both targets are supposed to read — a silent failure worth being able to see
	/// (see <c>skafinity_status</c>).</summary>
	public int HouseConfigCount => _houseConfig?.Count ?? 0;

	/// <summary>What the composer decided for the song playing right now — tempo, key, changes,
	/// voicing, groove, figure and tune lengths, ending, and the form with each section's
	/// energy/feel. The same read-out the engine test harness's <c>--seed</c> prints, which is the
	/// tool for "this seed sounds wrong": reading the decisions beats inferring them from the
	/// audio.</summary>
	/// <remarks>Plans the song again to get it, which is a composition pass plus the drum
	/// synthesis — expect a hitch of up to a second or so. It is a diagnostic, not something to
	/// call per frame.</remarks>
	public string ExplainCurrent() => MusicGen.BeginPlan( SeedForPos( _pos ), ConfigForPos( _pos ) ).Explain();

	/// <summary>One entry in the navigable timeline (see <see cref="Timeline"/>).</summary>
	public readonly struct QueueEntry
	{
		/// <summary>Song index within this entry's OWN station — what the row shows. Under shuffle
		/// that is 0 for every row, which is why the row also carries <see cref="Position"/>.</summary>
		public int N { get; init; }
		/// <summary>Slot on the timeline: what <see cref="SkafinityPlayer.SeekTo"/> and the export
		/// take. Anything that addresses a song by where it sits in the line uses this, not N.</summary>
		public int Position { get; init; }
		/// <summary>Station tag this entry belongs to. The root tag for every row with shuffle off.</summary>
		public string Tag { get; init; }
		/// <summary>This entry is behind the playhead — already heard.</summary>
		public bool Past { get; init; }
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

	/// <summary>Snapshot the timeline around the playing song: <paramref name="back"/> history entries
	/// (p−back…p−1), the current song, and <paramref name="fwd"/> look-ahead entries (p+1…p+fwd). For
	/// the playlist view; reflects cached-vs-needs-regenerate and in-flight generation state.</summary>
	public System.Collections.Generic.List<QueueEntry> Timeline( int back, int fwd )
	{
		var list = new System.Collections.Generic.List<QueueEntry>();
		int lo = Math.Max( 0, _pos - Math.Max( 0, back ) );
		int hi = _pos + Math.Max( 0, fwd );
		for ( int p = lo; p <= hi; p++ )
		{
			var s = SongAt( p );
			list.Add( new QueueEntry
			{
				N = s.N,
				Position = p,
				Tag = s.Tag,
				Past = p < _pos,
				Vibe = VibeForPos( p ),
				Genre = GenreForPos( p ),
				Cached = _pcm.ContainsKey( p ),
				Current = p == _pos,
				Progress = _genProgress.TryGetValue( p, out var g ) ? g : -1f,
			} );
		}
		return list;
	}

	// RESOLVED, not raw: an unset Tag is a station all the same (SeedCodec's fallback word), and it
	// is the station every seed this player shows, copies or rolls a shuffle tag from must name. Left
	// raw, a fresh player writes ":129" — a seed that plays correctly and reads as truncated, and
	// which tells whoever is handed it nothing about which station it came from.
	string SeedTag => SeedCodec.Station( Tag );
	// Build the PRNG seed string from a resolved tag, so worker code never re-reads state. The
	// spelling is the ENGINE's (SeedCodec.SongSeed) rather than this host's: it decides what song
	// an untagged seed is, and the web resolves the same one.
	static string SeedFor( string tag, int n ) => SeedCodec.SongSeed( tag, n );
	string SeedForPos( int p ) { var s = SongAt( p ); return SeedFor( s.Tag, s.N ); }

	// ── The timeline, and what a position resolves to ────────────────────────────────────────────
	// Shuffle OFF: one station, walked by song index — the position IS the index, which is what lets
	// Prev walk back to a song fifty ago that nothing remembers. Shuffle ON: every position is its
	// own station at song 0, and those stations are DERIVED from the root tag (SeedCodec.RollTagFor)
	// rather than drawn fresh. That is what keeps a shuffled line a LINE: Prev replays exactly what
	// was heard without anything having been remembered, and the whole thing still reproduces from
	// one string. Position 0 is always the seed as given, so a pasted seed plays the song it names
	// before the shuffle takes over.
	/// <summary>The station and song index timeline position <paramref name="p"/> plays.</summary>
	public (string Tag, int N) SongAt( int p )
	{
		p = Math.Max( 0, p );
		if ( !Shuffle ) return (SeedTag, p);
		return p == 0 ? (SeedTag, _baseN) : (SeedCodec.RollTagFor( SeedTag, p ), 0);
	}

	protected override void OnStart()
	{
		_baseN = Math.Max( 0, StartN );
		if ( PersistProgress )
		{
			// Full JSON state first; fall back to the legacy .n progress file for old saves.
			if ( !LoadState() )
				_baseN = Math.Max( 0, LoadN() ?? StartN );
		}
		// Where the seed joined the line is a SONG INDEX; the position it starts at is that index
		// when the line is this one station, and 0 when every next song is a station of its own.
		_pos = Shuffle ? 0 : _baseN;
		_lastConfigHash = ConfigHash();
		_lastStateHash = StateHash();
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

		// The clock reaching a queued song is what makes it the one playing — see _scheduled. Until
		// then it is written but inaudible, and everything a listener is shown (the seed, the mixer,
		// the playlist, the seek bar) has to describe what they can hear.
		PromoteAudible();

		// Keep the forward look-ahead window of the PCM cache topped up. Generation runs on a worker
		// thread so this never blocks the frame.
		if ( !Generating && _curRaw != null && NeedsFill() )
			_ = FillAhead( _seq );

		// When the queued audio is about to run out, crossfade into the pre-rendered next song.
		if ( _stream != null && _curRaw != null && !_paused && _pcm.ContainsKey( _writePos + 1 )
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

		// Debounced state persistence: any change to the tracked settings (tag/n/vibe/shuffle/mute/
		// volume — e.g. from a UI panel toggling Enabled or dragging Volume) is written once settled.
		if ( PersistProgress )
		{
			int sh = StateHash();
			if ( sh != _lastStateHash && !_stateDirty ) { _stateDirty = true; _stateDirtySince = 0; }
			if ( _stateDirty && _stateDirtySince > 1f )
			{
				_stateDirty = false;
				if ( StateHash() != _lastStateHash ) SaveState();
			}
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

	float TargetVolume()
	{
		if ( !Enabled ) return 0f;
		var v = Volume;
		// Honour the sound-options music slider (Preferences.MusicVolume). The engine already applies it,
		// but ONLY to a mixer named "Music" AND only in a real build (Mixer.FinishMixing gates on
		// !Application.IsEditor). So we apply it ourselves in every other case — in the editor (any mixer),
		// and in a build when we're NOT on the Music mixer — and skip it only when the engine will, so it's
		// never scaled twice.
		bool engineWillApply = !Application.IsEditor
			&& string.Equals( MixerName, "Music", StringComparison.OrdinalIgnoreCase );
		if ( !engineWillApply )
			v *= Preferences.MusicVolume;
		return v;
	}

	/// <summary>The config the PLAYING song was synthesised with — what a UI should draw its knobs
	/// from. Deliberately the audible song rather than the live knobs: with the vibe left rolling,
	/// those two are different configs and a mixer that shows the one you cannot hear is worse than
	/// no mixer.</summary>
	public MusicGen.Config EffectiveConfig() => ConfigForPos( _pos );

	MusicGen.Config BuildConfig()
	{
		var cfg = BuildKnobConfig();
		// Shared house-mix baseline (peak balances / kit presence) from skafinity.config.json —
		// the same file the web toy reads. Independent of the vibe/volume knobs below.
		VibeCodec.ApplyAdvanced( _houseConfig, cfg );
		// A vibe override sets the important knobs (so a shared seed reproduces the same voicing
		// regardless of this client's inspector knobs). Anything that is not a whole grid is not a
		// vibe, and Apply refuses it rather than half-applying it.
		VibeCodec.Apply( Vibe, cfg );
		// Per-instrument volumes are NOT in the seed — overlay the persisted per-voice mix on top.
		VibeCodec.ApplyVolumes( cfg.Genre, _vols, cfg );
		return cfg;
	}

	// The vibe the song at position p plays. Pinned (a pasted seed, a knob drag) it is whatever was
	// pinned; rolled, it is DERIVED from the (tag, n) that position resolves to — so the "random"
	// line is a fixed path you can walk both ways, and the ledger is a cache of that derivation
	// rather than the only copy of it.
	string VibeForPos( int p )
	{
		if ( _ledger.TryGetValue( p, out var v ) ) return v;
		// Pinned, a song TRACKS the live knobs and is deliberately not cached, so a knob edit
		// followed by a restart is always picked up.
		if ( !RandomVibeEverySong ) return VibeCodec.Encode( BuildKnobOnlyVibe() );
		var s = SongAt( p );
		var rolled = SeedCodec.RollVibeFor( s.Tag, s.N );
		_ledger[p] = rolled;
		return rolled;
	}

	// The genre the song at position p plays — the same story as the vibe, off its own stream so that
	// pinning one never moves the other.
	int GenreForPos( int p )
	{
		if ( _genreLedger.TryGetValue( p, out var g ) ) return g;
		if ( !RandomGenreEverySong ) return Math.Clamp( Genre, 0, VibeCodec.GenreCount - 1 );
		var s = SongAt( p );
		int rolled = SeedCodec.RollGenreFor( s.Tag, s.N );
		_genreLedger[p] = rolled;
		return rolled;
	}

	// The vibe (override-or-knobs) the live player would encode, WITHOUT the house mix/volumes that
	// aren't part of the seed — used as the un-pinned vibe for non-shuffle songs.
	MusicGen.Config BuildKnobOnlyVibe()
	{
		var cfg = BuildKnobConfig();
		VibeCodec.Apply( Vibe, cfg );      // ignored unless Vibe is a whole grid
		return cfg;
	}

	// The full Config to synthesise position p with: the live knobs + house mix + volumes, but with
	// THAT song's genre and vibe applied (not the player's single live pair). This is what makes each
	// queued song its own composition and keeps CurrentSeed honest.
	MusicGen.Config ConfigForPos( int p )
	{
		var cfg = BuildKnobConfig();
		VibeCodec.ApplyAdvanced( _houseConfig, cfg );
		cfg.Genre = GenreForPos( p );
		VibeCodec.Apply( VibeForPos( p ), cfg );
		VibeCodec.ApplyVolumes( cfg.Genre, _vols, cfg );
		return cfg;
	}

	// Drop PCM outside the ±PcmCacheRadius window around the current song (ledger strings are kept).
	void PrunePcm()
	{
		int r = Math.Max( 0, PcmCacheRadius );
		var drop = new System.Collections.Generic.List<int>();
		foreach ( var n in _pcm.Keys )
			if ( Math.Abs( n - _pos ) > r ) drop.Add( n );
		foreach ( var n in drop ) _pcm.Remove( n );
	}

	MusicGen.Config BuildKnobConfig() => new()
	{
		SampleRate = SampleRate,
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
		h.Add( SampleRate );
		h.Add( BassVol ); h.Add( SkankVol ); h.Add( OrganVol ); h.Add( MelodyVol ); h.Add( HornVol );
		h.Add( KickVol ); h.Add( SnareVol ); h.Add( TomVol ); h.Add( HatVol ); h.Add( CrashVol ); h.Add( DrumVol );
		h.Add( Detune ); h.Add( BassCutoff ); h.Add( SkankCutoff ); h.Add( SkankHighpass ); h.Add( SkankChop );
		h.Add( LeadCutoff ); h.Add( OrganCutoff ); h.Add( OrganVibrato ); h.Add( HornCutoff );
		h.Add( BassDrive ); h.Add( SkankDrive ); h.Add( MelodyDrive ); h.Add( HornDrive );
		h.Add( MasterDrive ); h.Add( MasterPeak );
		h.Add( OctavePopChance ); h.Add( OrganBubbleChance ); h.Add( KickSyncChance );
		h.Add( GhostSnareChance ); h.Add( FillChance );
		h.Add( DrumBusy ); h.Add( DrumTone ); h.Add( DrumDrive ); h.Add( TripletChance ); h.Add( BassTriplets );
		h.Add( MelodyRestChance ); h.Add( MelodyLeapChance ); h.Add( MelodyVibrato );
		h.Add( TrumpetWeight ); h.Add( SaxWeight ); h.Add( OrganWeight ); h.Add( TromboneWeight );
		h.Add( ForceInstrument );
		h.Add( HornSectionChance ); h.Add( HornDensity );
		h.Add( Genre );
		h.Add( KeysVol ); h.Add( KeysCutoff ); h.Add( KeysDrive ); h.Add( KeysChug );
		h.Add( RhythmGtrVol ); h.Add( RhythmGtrCutoff ); h.Add( RhythmGtrDrive ); h.Add( RhythmGtrChug );
		h.Add( LeadGtrVol ); h.Add( LeadGtrCutoff ); h.Add( LeadGtrDrive ); h.Add( LeadGtrBend );
		h.Add( Tag ); h.Add( Vibe ); h.Add( RandomGenreEverySong ); h.Add( RandomVibeEverySong );
		h.Add( Shuffle );
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
		for ( int n = _pos + 1; n <= _pos + ForwardWindow; n++ )
			if ( !_pcm.ContainsKey( n ) ) return true;
		return false;
	}

	int FadeFrames => Math.Max( 1, (int)(Math.Clamp( Crossfade, 0.25f, 8f ) * _sr) );

	// Coming up from silence at the start of a session. Short on purpose and NOT the crossfade
	// window: a crossfade is long because two songs have to trade places without either being
	// heard to stop, and nothing is being traded here. See BeginAt for what the long version did
	// to the opening bars.
	// MEASURED rather than judged, on four real renders (see web/app.js, which carries the same
	// constant and the same reasoning): dry, a song's opening strike sits +2.5 to +5.6 dB above the
	// ring half a second later. A 2.9 s ramp returns it at -28 to -31 dB, and 0.15 s — what this
	// was — still leaves it 14-17 dB out, i.e. audibly a cymbal that was hit before the song
	// started. At 0.01 s the balance is the dry balance on every seed.
	const float StartFadeSeconds = 0.012f;
	int StartFadeFrames => Math.Max( 1, (int)(StartFadeSeconds * _sr) );

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
			while ( seq == _seq && _curRaw != null )
			{
				// Generate the nearest missing forward slot (nearest first so an imminent crossfade
				// is satisfied before distant look-ahead).
				int target = -1;
				for ( int p = _pos + 1; p <= _pos + ForwardWindow; p++ )
					if ( !_pcm.ContainsKey( p ) ) { target = p; break; }
				if ( target < 0 ) break;

				var cfg = ConfigForPos( target );   // resolves+freezes this song's vibe
				short[] song;
				try { song = await GenerateStereoAsync( SeedForPos( target ), cfg, target ); }
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
			// The fade counts from the first frame WRITTEN, not from the song's downbeat: a scrub
			// comes in mid-song and still has to come up from silence without clicking.
			float g = (fadeIn > 0 && i < fadeIn) ? (float)i / fadeIn : 1f;
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
		_genreLedger.Clear();
		_pcm.Clear();
		_genProgress.Clear();
		_bufferingPos = -1;
		// Nothing is pinned into the ledger here any more. A pin is now per seed part and applies to
		// EVERY song (RandomGenreEverySong / RandomVibeEverySong), which is what the seed means — so
		// VibeForPos/GenreForPos already answer with it and a ledger entry could only shadow them.
		StopStream();
		// Paused, a restart is a change of what WILL play, not a reason to start playing: the caches
		// have just been dropped, so the next Resume regenerates under whatever changed. Kicking the
		// sequence here would answer a knob drag by starting the music behind a listener's back.
		if ( _paused ) return;
		_ = StartSequenceAsync( seq );
	}

	// Tear the stream down: what a restart, a seek and a pause all begin with. The playhead is NOT
	// remembered here — a pause takes its offset before calling this, and everything else means to
	// land on a downbeat.
	void StopStream()
	{
		_handle?.Stop();
		_handle = null;
		_stream = null;
		_flatConfigured = false;
		_curRaw = null;
		_scheduled.Clear();
	}

	// The current song fades in; thereafter songs play through once and crossfade into the
	// pre-rendered next. Synthesis is offloaded; stream setup is on the main thread.
	async Task StartSequenceAsync( int seq )
	{
		try
		{
			_starting = true;
			int p = Math.Max( 0, _pos );
			short[] raw;
			try { raw = await GenerateStereoAsync( SeedForPos( p ), ConfigForPos( p ), p ); }
			finally { _genProgress.Remove( p ); }
			if ( seq != _seq ) return;   // superseded by a newer StartSequence

			_pos = p;
			_pcm[p] = raw;
			BeginAt( p, _resumeOffset );
			_resumeOffset = 0;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: StartSequence failed: {e.Message}" );
		}
		finally { if ( seq == _seq ) _starting = false; }   // don't let a superseded run clear a live flag
	}

	/// <summary>Navigate to timeline position <paramref name="p"/> while PRESERVING the timeline
	/// (ledger + PCM cache). If p's PCM is resident it plays instantly; otherwise playback stalls in a
	/// "buffering" state (<see cref="IsBuffering"/>) while it regenerates from the seed that position
	/// derives. This is what Prev/Next and the playlist call — Prev replays the exact earlier songs.
	/// Restarts the SoundStream (a manual jump breaks the crossfade chain), but does NOT discard the
	/// timeline the way <see cref="StartSequence"/> does.</summary>
	/// <param name="offset">Seconds into the song to come in at — a resume or a scrub. Every other
	/// caller lands on the downbeat.</param>
	public void SeekTo( int p, double offset = 0 )
	{
		p = Math.Max( 0, p );
		int seq = ++_seq;
		StopStream();
		_pos = p;
		// Paused, a jump is only a choice of where the next play comes in. Setting the offset
		// unconditionally is what stops a Prev/Next taken while paused from inheriting the offset of
		// the song it was paused out of.
		_resumeOffset = Math.Max( 0, offset );
		if ( PersistProgress ) SaveN( _pos );
		PrunePcm();
		if ( _paused ) return;
		_ = SeekToAsync( seq, p );
	}

	async Task SeekToAsync( int seq, int p )
	{
		try
		{
			_seeking = true;
			short[] raw;
			if ( _pcm.TryGetValue( p, out var cached ) )
			{
				raw = cached;            // instant: within the cache window
			}
			else
			{
				_bufferingPos = p;         // outside the window → surface a "Generating…" state
				try { raw = await GenerateStereoAsync( SeedForPos( p ), ConfigForPos( p ), p ); }
				finally { _genProgress.Remove( p ); }
				if ( seq != _seq ) return;   // superseded by a newer seek/restart
				_pcm[p] = raw;
			}
			if ( seq != _seq ) return;
			BeginAt( p, _resumeOffset );
			_resumeOffset = 0;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: SeekTo failed: {e.Message}" );
		}
		finally { if ( seq == _seq ) { _seeking = false; _bufferingPos = -1; } }
	}

	// Open the stream on song n's resident PCM: fade in from silence, hold back the tail for the next
	// crossfade, start the handle. Main-thread; p's PCM must already be in _pcm.
	// `offset` is seconds into the song to come in at — a resume or a scrub, and zero for every other
	// caller. It is consumed as frames skipped off the head, which is the only rewind a SoundStream
	// has: what has been written cannot be taken back, so a scrub is a new stream on the same PCM.
	void BeginAt( int p, double offset = 0 )
	{
		_curRaw = _pcm[p];
		_curReserve = Math.Min( FadeFrames, Frames( _curRaw ) / 3 );
		double songSeconds = Frames( _curRaw ) / (double)_sr;
		// Landing past the end of the BODY would open a stream with nothing to write — the tail is
		// held back for the crossfade, so the last playable instant is short of the song's length by
		// the reserve, not by zero.
		double body = songSeconds - _curReserve / (double)_sr;
		double into = Math.Clamp( offset, 0, Math.Max( 0, body - 0.25 ) );
		int head = (int)(into * _sr);

		_stream = new SoundStream( _sr, MusicGen.Channels );

		// The tail held back for the crossfade is the crossfade's length; the fade UP FROM SILENCE
		// is not, and used to be. A ramp over the whole crossfade window put the first bars of the
		// song under it — half a second in, a 3.75 s linear ramp is still at 0.13 (-17.5 dB) — and
		// what that does to a drum kit is specific rather than merely quiet: it crushes the STRIKE
		// and lets the RING arrive at full level a second later, so every cymbal in the opening
		// sounds like it was hit before the song started. Coming out of silence only has to not
		// click.
		int written = WriteSongBody( _curRaw, head, _curReserve, Math.Min( StartFadeFrames, _curReserve ) );
		_pushedSeconds = written / (double)_sr;

		_handle = _stream.Play();
		if ( _handle != null )
		{
			_handle.Volume = TargetVolume();
			ConfigureFlat();
		}
		_sinceStart = 0;
		_paused = false;
		_writePos = p;
		// This song is audible from the first frame of the new stream. Everything queued after it
		// lands later and says so, which is what keeps "now playing" honest.
		_scheduled.Clear();
		_scheduled.Add( new Playing( p, 0, into, songSeconds ) );
		PrunePcm();
	}

	// Queue the crossfade from the write head's tail into the next (pre-rendered) song's head, then
	// the next song's body. It does NOT advance the audible position: what has been written is not
	// yet what is being heard, and the promotion happens in OnUpdate when the clock reaches the entry
	// this queues. The next song's vibe was already frozen when FillAhead rendered it — under shuffle
	// that is how the band changes between songs, with no clear-and-regenerate churn.
	void PushTransition()
	{
		try
		{
			var next = _pcm[_writePos + 1];
			double startSeconds = _pushedSeconds;   // stream time the incoming song's first frame lands at

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
			_writePos++;
			_scheduled.Add( new Playing( _writePos, startSeconds, 0, Frames( next ) / (double)_sr ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: PushTransition failed: {e.Message}" );
		}
	}

	// ── The playhead ──
	// A SoundStream is a FIFO with no cursor to read, so where we are in the song is arithmetic on
	// how long it has been playing (_sinceStart) against the stream time each song was written at.
	// Nothing is asked of the engine for this, and nothing is guessed: an unrendered song reports a
	// duration of 0 rather than an assumed length, because songs genuinely differ in length.

	// Retire queued songs the clock has reached, so _pos is the song a listener can hear.
	void PromoteAudible()
	{
		if ( _handle == null || _scheduled.Count == 0 ) return;
		bool moved = false;
		// (double) deliberately: TimeSince declares its comparisons against float and int only, so a
		// bare `>=` against these seconds would not resolve.
		while ( _scheduled.Count > 1 && (double)_sinceStart >= _scheduled[1].StartSeconds )
		{
			_scheduled.RemoveAt( 0 );
			moved = true;
		}
		if ( !moved ) return;
		_pos = _scheduled[0].Pos;
		if ( PersistProgress ) SaveN( _pos );
		PrunePcm();   // the playhead moved — drop anything now outside the ±radius window
	}

	/// <summary>Where the playhead is in the audible song. <see cref="SongPosition.Duration"/> is 0
	/// until the song is rendered — an unknown length is reported as unknown rather than assumed, so
	/// a bar drawn against it goes inert instead of lying.</summary>
	public readonly struct SongPosition
	{
		/// <summary>Timeline position of the song this describes.</summary>
		public int Position { get; init; }
		/// <summary>Seconds into the song.</summary>
		public double Time { get; init; }
		/// <summary>Length of the song in seconds, or 0 when it has not been rendered.</summary>
		public double Duration { get; init; }
		/// <summary>Time/Duration, or 0 when the length is unknown.</summary>
		public float Ratio { get; init; }
		/// <summary>Audible right now (as opposed to paused or still generating).</summary>
		public bool Playing { get; init; }
	}

	/// <inheritdoc cref="SongPosition"/>
	public SongPosition Playhead()
	{
		double duration = _scheduled.Count > 0 ? _scheduled[0].Duration
			: _pcm.TryGetValue( _pos, out var raw ) ? Frames( raw ) / (double)_sr : 0;
		double time;
		if ( _handle != null && _scheduled.Count > 0 )
			time = Math.Clamp( _sinceStart - _scheduled[0].StartSeconds + _scheduled[0].Offset, 0, duration );
		else
			time = duration > 0 ? Math.Min( _resumeOffset, duration ) : _resumeOffset;
		return new SongPosition
		{
			Position = _pos,
			Time = time,
			Duration = duration,
			Ratio = duration > 0 ? (float)(time / duration) : 0f,
			Playing = _handle != null,
		};
	}

	/// <summary>Stop, holding the place in the song. Nothing is suspended — the stream is torn down,
	/// because a SoundStream cannot be paused or rewound — so this remembers how far in it got and
	/// <see cref="Resume"/> re-opens the same PCM at that offset.</summary>
	public void Pause()
	{
		if ( _paused ) return;
		var here = Playhead();
		// Right at the end there is nothing left to resume INTO; start the next song cleanly instead.
		if ( here.Duration > 0 && here.Time < here.Duration - 0.25 )
		{
			_resumeOffset = here.Time;
		}
		else
		{
			_resumeOffset = 0;
			if ( here.Duration > 0 ) _pos++;
		}
		_seq++;              // anything in flight would come back and start playing
		StopStream();
		_paused = true;
	}

	/// <summary>Come back in where <see cref="Pause"/> left off.</summary>
	public void Resume()
	{
		if ( !_paused ) return;
		_paused = false;
		SeekTo( _pos, _resumeOffset );
	}

	/// <summary>⏯ — pause if playing, resume if paused.</summary>
	public void TogglePlay()
	{
		if ( _paused ) Resume();
		else Pause();
	}

	/// <summary>Scrub inside the audible song. The whole song is already in memory, so this costs a
	/// stream restart and nothing else. Paused, it only moves where the next play comes in.</summary>
	public void SeekWithin( double seconds )
	{
		double duration = Playhead().Duration;
		double t = Math.Max( 0, duration > 0 ? Math.Min( seconds, Math.Max( 0, duration - 0.25 ) ) : seconds );
		if ( _paused || _handle == null ) { _resumeOffset = t; return; }
		SeekTo( _pos, t );
	}

	// ── Public control surface ──

	/// <summary>Play a shareable seed — <c>tag:n[:genre][:vibe]</c>, parsed by the engine
	/// (<see cref="SeedCodec.TryParse"/>) so this player and the web toy cannot disagree about what
	/// a string means. What the seed leaves out keeps rolling; what it pins is pinned. Returns false
	/// and changes NOTHING if the string is not a seed — half a seed is a song nobody asked for, so
	/// a caller with a UI should show <paramref name="error"/> rather than start playing.</summary>
	public bool PlaySeed( string seed, out string error )
	{
		if ( !SeedCodec.TryParse( seed, out var s, out error ) ) return false;
		Tag = (s.Tag ?? "").Trim().ToLowerInvariant();
		_baseN = Math.Max( 0, s.N );
		_pos = Shuffle ? 0 : _baseN;
		// A pinned part becomes the live value and stays pinned for every song; an absent one goes
		// back to rolling per song, which is what the seed leaving it out means.
		Vibe = s.VibePinned ? s.Vibe : "";
		if ( s.GenrePinned ) Genre = s.Genre;
		RandomGenreEverySong = !s.GenrePinned;
		RandomVibeEverySong = !s.VibePinned;
		_resumeOffset = 0;         // a different song: there is nothing to resume into
		if ( PersistProgress ) SaveN( _pos );
		StartSequence();
		return true;
	}

	/// <inheritdoc cref="PlaySeed(string, out string)"/>
	public bool PlaySeed( string seed ) => PlaySeed( seed, out _ );

	/// <summary>Set just the seed tag (empty = the default "skafinity" seed). Restarts.</summary>
	public void SetTag( string tag )
	{
		Tag = string.IsNullOrEmpty( tag ) ? "" : tag.Trim().ToLowerInvariant();
		StartSequence();
	}

	/// <summary>Jump to timeline position p (clamped ≥ 0), preserving the timeline. Plays cached PCM
	/// instantly when in the window, else buffers while it regenerates.</summary>
	public void SetN( int p ) => SeekTo( p );

	/// <summary>Step the timeline position by <paramref name="delta"/> (e.g. +1 / -1), preserving the
	/// timeline so Prev replays the exact earlier songs.</summary>
	public void StepN( int delta ) => SeekTo( _pos + delta );
	/// <summary>Skip to the next song in the line.</summary>
	public void NextSong() => StepN( 1 );
	/// <summary>Step back to the previous song in the line.</summary>
	public void PrevSong() => StepN( -1 );

	/// <summary>Set vibe field <paramref name="index"/> (see <see cref="VibeCodec.Fields(int)"/>) from
	/// a 0..1 fraction, store the re-encoded <see cref="Vibe"/>, and restart on a short debounce.</summary>
	public void SetVibe( int index, float norm )
	{
		// From the AUDIBLE song, not the live knobs: with the vibe rolling they are different
		// configs, and moving one slider has to leave the other 35 where they were heard.
		var cfg = ConfigForPos( _pos );
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
			// Dragging a knob PINS the vibe — otherwise the next song rolls the edit away and the
			// slider is a control that does nothing past the crossfade. RollVibe is the way back out.
			Vibe = VibeCodec.Encode( cfg );
			RandomVibeEverySong = false;
		}
		_restartPending = true;
		_restartPendingSince = 0;
	}

	/// <summary>Switch genre and restart, KEEPING the vibe that is playing. The vibe is
	/// genre-independent, so this is the same 36 knobs heard through a different band rather than a
	/// new song — which is what someone reaching for the genre dropdown mid-song is asking for. The
	/// live vibe is pinned on the way through, or the restart would roll a fresh one and the change
	/// would sound like a reroll.</summary>
	public void SetGenre( int genre )
	{
		Vibe = CurrentVibe;
		RandomVibeEverySong = false;
		Genre = Math.Clamp( genre, 0, VibeCodec.GenreCount - 1 );
		RandomGenreEverySong = false;
		StartSequence();
	}

	/// <summary>Hand the genre back to the station: every song rolls its own again. The counterpart
	/// to <see cref="SetGenre"/>, and the reason the genre dropdown needs a "Random" entry — without
	/// one there is no way back out of a genre once one has been chosen.</summary>
	public void RollGenre() => SetRandomGenreEverySong( true );

	/// <summary>Hand the VIBE back to the station: every song rolls its own again. The way out of a
	/// dragged knob — and it may well move nothing you can hear, because the song already playing
	/// keeps the vibe it resolved to; what changes is what comes NEXT.</summary>
	public void RollVibe()
	{
		Vibe = "";
		SetRandomVibeEverySong( true );
	}

	/// <summary>Reroll the SEED: a fresh random station at song 0. Anything pinned stays pinned,
	/// because a pin is a choice and this is a request for a different song, not a different
	/// taste.</summary>
	public void RerollStation()
	{
		Tag = RandomTag();
		_baseN = 0;
		_pos = 0;
		_resumeOffset = 0;
		if ( PersistProgress ) SaveN( _pos );
		StartSequence();
	}

	// Eight base-36 characters — a tag nobody has to read out, the same shape the web toy draws.
	static string RandomTag()
	{
		var sb = new System.Text.StringBuilder( 8 );
		for ( int i = 0; i < 8; i++ )
		{
			int q = System.Random.Shared.Next( 36 );
			sb.Append( q < 10 ? (char)('0' + q) : (char)('a' + q - 10) );
		}
		return sb.ToString();
	}

	/// <summary>Throw every knob somewhere new and PIN it there — the die over the mixer. It always
	/// moves every slider, because it draws a fresh vibe rather than handing the knobs back to the
	/// station (that is <see cref="RollVibe"/>, and a die that does nothing when nothing was pinned
	/// is a die that looks broken). The per-instrument volumes and the GENRE are left alone by
	/// default: a die on a mixer re-voices the band, it does not swap the band — pass
	/// <paramref name="includeVolumes"/> / <paramref name="includeGenre"/> for a full shuffle. Pass
	/// <paramref name="restart"/> = false to re-voice without yanking the playhead — the caller is
	/// then responsible for letting the change take effect (e.g. by clearing the look-ahead so
	/// upcoming songs regenerate with the new vibe).</summary>
	public void RerollVibe( bool includeVolumes = false, bool includeGenre = false, bool restart = true )
	{
		Vibe = VibeCodec.RollVibe( System.Random.Shared.NextSingle );
		RandomVibeEverySong = false;
		if ( includeGenre )
		{
			Genre = VibeCodec.RollGenre( System.Random.Shared.NextSingle );
			RandomGenreEverySong = false;
		}
		if ( includeVolumes )
		{
			// Volumes are not in the wire at all, so they are rolled separately and captured into
			// the persisted per-voice store.
			var cfg = BuildConfig();
			VibeCodec.RollVolumes( cfg.Genre, cfg, System.Random.Shared.NextSingle );
			foreach ( var kv in VibeCodec.ReadVolumes( cfg.Genre, cfg ) ) _vols[kv.Key] = kv.Value;
			SaveVols();
		}
		if ( restart )
		{
			_restartPending = true;
			_restartPendingSince = 0;
		}
	}

	/// <summary>Let the GENRE roll per song again, or pin it to <see cref="Genre"/>. Rebuilds the
	/// forward timeline so the change takes immediately; history keeps what it was.</summary>
	public void SetRandomGenreEverySong( bool on )
	{
		if ( RandomGenreEverySong == on ) return;
		RandomGenreEverySong = on;
		ReresolveForward();
	}

	/// <summary>Let the VIBE roll per song again, or pin the live knobs. Rebuilds the forward
	/// timeline so the change takes immediately; history keeps what it was.</summary>
	public void SetRandomVibeEverySong( bool on )
	{
		if ( RandomVibeEverySong == on ) return;
		RandomVibeEverySong = on;
		ReresolveForward();
	}

	/// <summary>Flip <see cref="Shuffle"/>: on, every next song is a whole new station rather than the
	/// next song of this one. The song PLAYING is carried across the change — the switch is heard as
	/// a change of what comes next rather than as a jump — and since the timeline behind every
	/// position now describes different songs, it rebuilds rather than keeping a cache that lies.</summary>
	public void SetShuffle( bool on )
	{
		if ( Shuffle == on ) return;
		// Read the audible song BEFORE the flag moves: SongAt answers under whichever mode is set,
		// and carrying that song across the change is the whole point of this block.
		var here = SongAt( _pos );
		Shuffle = on;
		Tag = here.Tag;
		_baseN = here.N;
		// Shuffled, the playing song becomes position 0 of the new line (the root in both modes);
		// unshuffled, its position is its song index again.
		_pos = Shuffle ? 0 : here.N;
		_resumeOffset = 0;
		StartSequence();
	}

	// Drop the frozen line from the current song forward so upcoming songs re-resolve under whatever
	// just changed; history (p < _pos) keeps its frozen vibes so Prev still replays what you heard.
	// Softer than StartSequence on purpose — the seed's TAG has not moved, so the songs behind you
	// are still the same songs and their PCM is still worth having.
	void ReresolveForward()
	{
		var fwd = new System.Collections.Generic.List<int>();
		foreach ( var p in _ledger.Keys ) if ( p >= _pos ) fwd.Add( p );
		foreach ( var p in fwd ) _ledger.Remove( p );
		fwd.Clear();
		foreach ( var p in _genreLedger.Keys ) if ( p >= _pos ) fwd.Add( p );
		foreach ( var p in fwd ) _genreLedger.Remove( p );
		fwd.Clear();
		foreach ( var p in _pcm.Keys ) if ( p >= _pos ) fwd.Add( p );
		foreach ( var p in fwd ) _pcm.Remove( p );
		SeekTo( _pos );   // regenerate the current song under the new mode, keeping history cached
	}

	/// <summary>Is the genre written into the seed rather than rolled? What a UI's "hand it back to
	/// the station" control reads, so it can be off when there is nothing to hand back.</summary>
	public bool GenrePinned => !RandomGenreEverySong;
	/// <summary>Is the vibe written into the seed rather than rolled? See <see cref="GenrePinned"/>.</summary>
	public bool VibePinned => !RandomVibeEverySong;

	/// <summary>Write the playing song's raw loop (no fade) to a stereo WAV under FileSystem.Data.
	/// Returns the filename written, or null on failure.</summary>
	public string SaveCurrentToFile() => WriteWav( _pos, _curRaw );

	/// <summary>Write the song at timeline position <paramref name="p"/> to a WAV under
	/// FileSystem.Data, rendering it first if it is outside the PCM cache. Returns the filename, or
	/// null on failure. What the playlist's per-row save calls.</summary>
	/// <remarks>Async because a song outside the cache has to be synthesised, which is seconds of
	/// work — doing it on the frame would hitch the game for a save nobody asked to wait for.</remarks>
	public async Task<string> SaveToFileAsync( int p )
	{
		p = Math.Max( 0, p );
		if ( _pcm.TryGetValue( p, out var cached ) ) return WriteWav( p, cached );
		short[] raw;
		try { raw = await GenerateStereoAsync( SeedForPos( p ), ConfigForPos( p ), p ); }
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: save render failed: {e.Message}" ); return null; }
		finally { _genProgress.Remove( p ); }
		return WriteWav( p, raw );
	}

	// The file is named for the song, not for the slot it happened to sit in: under shuffle the
	// position is this session's line and means nothing to whoever is handed the file, while the
	// station tag and index name the song anywhere.
	string WriteWav( int p, short[] raw )
	{
		if ( raw == null || _sr <= 0 ) return null;
		var s = SongAt( p );
		var tag = string.IsNullOrEmpty( s.Tag ) ? "skafinity" : s.Tag.ToLowerInvariant();
		var name = $"{tag}_{s.N}.wav";
		try
		{
			// MusicGen.Channels, because that is what `raw` is: the interleaved stereo buffer the
			// SoundStream is fed. Passing 1 here writes stereo PCM under a mono header — half speed
			// with the channels folded into each other, which sounds like a mix decision rather than
			// a broken file, so it is worth being explicit about where the number comes from.
			FileSystem.Data.WriteAllBytes( name, MusicGen.WavFromSamples( raw, MusicGen.Channels, _sr ) );
			return name;
		}
		catch ( Exception e )
		{
			Log.Warning( $"SkafinityPlayer: save failed: {e.Message}" );
			return null;
		}
	}

	// ── Player-state persistence (FileSystem.Data, keyed by SaveSlot) ──
	// One JSON with the seed (tag / n / vibe) AND the listening settings that aren't reproducible
	// from the seed (shuffle, mute, volume). Per-voice mix levels stay in their own .vol file
	// (SaveVols) since they're edited on a different cadence. Loaded in OnStart; saved whenever any
	// of the tracked fields change (debounced in OnUpdate) and on every song advance/seek.
	string StateFile => $"skafinity_{(string.IsNullOrEmpty( SaveSlot ) ? "default" : SaveSlot)}.json";
	// Legacy pre-JSON progress file (just the song index) — still read as a fallback.
	string ProgressFile => $"skafinity_{(string.IsNullOrEmpty( SaveSlot ) ? "default" : SaveSlot)}.n";

	// A state file written before the genre/vibe switches split simply has neither field, so it loads
	// with both rolling — which is the default a fresh install gets. Nothing to migrate.
	class SavedState
	{
		public string Tag { get; set; } = "";
		/// <summary>Song INDEX, not timeline position — under shuffle the position is this session's
		/// line and reproduces nothing, while the index names a song in a station.</summary>
		public int N { get; set; }
		public string Vibe { get; set; } = "";
		public int Genre { get; set; }
		public bool RandomGenreEverySong { get; set; } = true;
		public bool RandomVibeEverySong { get; set; } = true;
		public bool Shuffle { get; set; }
		public bool Enabled { get; set; } = true;
		public float Volume { get; set; } = 0.7f;
	}

	int _lastStateHash;
	TimeSince _stateDirtySince;
	bool _stateDirty;

	int StateHash()
	{
		var h = new HashCode();
		h.Add( Tag ); h.Add( N ); h.Add( Vibe ); h.Add( Genre );
		h.Add( RandomGenreEverySong ); h.Add( RandomVibeEverySong ); h.Add( Shuffle );
		h.Add( Enabled ); h.Add( Volume );
		return h.ToHashCode();
	}

	void SaveState()
	{
		try
		{
			FileSystem.Data.WriteAllText( StateFile, Json.Serialize( new SavedState
			{
				Tag = Tag ?? "",
				// The SONG the playhead is on, not its slot: a resumed session rejoins the station
				// where it left off, and under shuffle the slot describes a line that is gone.
				N = N,
				Vibe = Vibe ?? "",
				Genre = Genre,
				RandomGenreEverySong = RandomGenreEverySong,
				RandomVibeEverySong = RandomVibeEverySong,
				Shuffle = Shuffle,
				Enabled = Enabled,
				Volume = Volume,
			} ) );
			_lastStateHash = StateHash();
		}
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: save state failed: {e.Message}" ); }
	}

	// Apply the saved state over the inspector defaults. Returns true when a state file was loaded.
	bool LoadState()
	{
		try
		{
			if ( !FileSystem.Data.FileExists( StateFile ) ) return false;
			var s = Json.Deserialize<SavedState>( FileSystem.Data.ReadAllText( StateFile ) );
			if ( s == null ) return false;
			Tag = s.Tag ?? "";
			_baseN = Math.Max( 0, s.N );
			Vibe = s.Vibe ?? "";
			Genre = Math.Clamp( s.Genre, 0, VibeCodec.GenreCount - 1 );
			RandomGenreEverySong = s.RandomGenreEverySong;
			RandomVibeEverySong = s.RandomVibeEverySong;
			Shuffle = s.Shuffle;
			Enabled = s.Enabled;
			Volume = Math.Clamp( s.Volume, 0f, 2f );
			return true;
		}
		catch ( Exception e ) { Log.Warning( $"SkafinityPlayer: load state failed: {e.Message}" ); }
		return false;
	}

	void SaveN( int n ) => SaveState();

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
