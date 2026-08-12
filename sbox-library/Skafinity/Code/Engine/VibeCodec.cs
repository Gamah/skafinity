using System;
using System.Collections.Generic;
using System.Text;

namespace Skafinity;

/// <summary>
/// Compact, shareable encoding of the "important" <see cref="MusicGen.Config"/> knobs — the
/// ones that define a song's vibe (genre, tempo, per-instrument mix/tone/character). Each
/// knob is quantised to one base-36 character (16 discrete levels, i.e. a hex digit) and the genre rides in
/// the first character, so the whole vibe is a short string that travels in the seed as
/// <c>vibe:tag:n</c>.
///
/// WIRE FORMAT (genre-independent envelope, fixed positions):
///   <c>[genre char][global block][instrument grid]</c>
/// where the global block is <see cref="GlobalFields"/> in order (currently EMPTY — see there),
/// and the instrument grid reserves up to <see cref="MaxInstruments"/> blocks of one char per
/// WIRE column. The wire carries columns <see cref="WireFirstColumn"/>..<see cref="Columns"/>-1
/// (tone / character / extra); column 0 is VOLUME, a local mix preference that is deliberately
/// not part of a song's identity and never travels. Wire column <c>c</c> of instrument <c>i</c>
/// always lives at <c>1 + globals + i*WireColumns + (c - WireFirstColumn)</c>, so adding a genre,
/// an instrument, or a 5th column never shifts an existing position. APPEND-ONLY means: append
/// instrument slots (≤ MaxInstruments), and only ever append columns past the last.
/// <see cref="Apply"/> ignores trailing chars a shorter string lacks, so a vibe from a
/// client with fewer slots still parses (the missing knobs keep their config defaults).
///
/// RETIRING a knob inside a row does not remove its position: the cell becomes a reserved (null)
/// slot that encodes as filler and decodes to nothing, so every later knob keeps the wire position
/// it has always had. A GLOBAL knob has no such backstop — the global block sits in front of the
/// whole grid, so adding one (or bringing a retired one back) shifts every grid position and
/// invalidates every seed ever shared. Prefer a row knob; a global is a deliberate format break.
///
/// Lossy by design (16 levels/knob) but stable: Encode(Decode(s)) == s.
/// </summary>
public static class VibeCodec
{
	internal const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
	public const int Levels = 16;   // one hex digit per knob
	public const int Columns = 4;       // volume, tone, character, extra
	/// <summary>First column that travels on the wire. Column 0 is VOLUME — a local mix preference
	/// (see <see cref="ReadVolumes"/>), so the whole column is skipped rather than encoded as filler.</summary>
	public const int WireFirstColumn = 1;
	public const int WireColumns = Columns - WireFirstColumn;
	public const int MaxInstruments = 8; // reserved instrument slots in the wire grid

	public sealed class Field
	{
		public string Name;
		public float Min, Max;
		public bool Int;
		/// <summary>Discrete option labels (value = Min + index); null for a continuous knob.</summary>
		public string[] Choices;
		public Func<MusicGen.Config, float> Get;
		public Action<MusicGen.Config, float> Set;
		/// <summary>Instrument row this knob belongs to (null = a GLOBAL knob).</summary>
		public string Voice;
		/// <summary>Matrix column: 0 volume, 1 tone, 2 character, 3 extra (ignored for globals).</summary>
		public int Column;

		/// <summary>Current value as a 0..1 fraction of the range.</summary>
		public float GetNorm( MusicGen.Config c ) =>
			Math.Clamp( (Get( c ) - Min) / (Max - Min), 0f, 1f );

		/// <summary>Set from a 0..1 fraction (rounded for integer/discrete knobs).</summary>
		public void SetNorm( MusicGen.Config c, float norm )
		{
			float v = Min + Math.Clamp( norm, 0f, 1f ) * (Max - Min);
			if ( Int || Choices != null ) v = (float)Math.Round( v );
			Set( c, v );
		}

		/// <summary>Human-readable current value for the row header.</summary>
		public string Display( MusicGen.Config c )
		{
			float v = Get( c );
			if ( Choices != null )
			{
				int idx = (int)Math.Clamp( Math.Round( v - Min ), 0, Choices.Length - 1 );
				return Choices[idx];
			}
			if ( Int ) return ((int)Math.Round( v )).ToString();
			// A knob whose whole range fits in 0..2 is a proportion, not a count — rounding it to
			// a whole number shows the same "1" across most of its travel. Read those as percents
			// (a 0..1.5 volume, a 0.7..1.45 tempo scale); anything wider is a real quantity (Hz,
			// cents, a drive amount) and stays a number.
			if ( Max <= 2f ) return $"{(int)Math.Round( v * 100 )}%";
			return ((int)Math.Round( v )).ToString();
		}
	}

	static Field F( string name, float min, float max, bool isInt,
		Func<MusicGen.Config, float> get, Action<MusicGen.Config, float> set,
		string voice = null, int column = 0, string[] choices = null )
		=> new() { Name = name, Min = min, Max = max, Int = isInt, Get = get, Set = set,
			Voice = voice, Column = column, Choices = choices };

	// One instrument row: [volume, tone, character, extra]. A null cell reserves its grid
	// slot (kept for fixed positions) without exposing a knob.
	static Field[] Row( string voice, Field vol, Field tone, Field character, Field extra )
		=> new[] { vol, tone, character, extra };

	// A per-instrument VOLUME knob (column 0). Column 0 is off the wire entirely: volume is a local
	// mix preference persisted per-voice (see ReadVolumes/ApplyVolumes), not part of the vibe.
	static Field Vol( string voice, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
		=> F( "VOLUME", 0f, 1.5f, false, g, s, voice, 0 );

	// Shared GLOBAL knobs — the wire's global block, in front of the grid, in order.
	//
	// EMPTY, and it is meant to stay that way. Every knob that was ever here turned out to be
	// something a listener should not hold: TEMPO (min/max, then bias, then a scale over the
	// genre's own band) is anchored on records and lives in GenreProfile, as does SWING and how
	// often a genre runs hot; RESONANCE set a Config field no voice read; STEREO WIDTH and REVERB
	// are playback environment, so they are house config (AdvancedFields) rather than song
	// identity. What they share is that a global slider lets a preference beat a measurement, and
	// each is a knob for six genres at once — which is what GenreProfile is for.
	//
	// So a knob belongs in a genre's grid row or in GenreProfile. Adding one HERE shifts the whole
	// grid and invalidates every shared seed; that is the cost, and it is the reason this list is
	// the last place to reach for.
	static readonly Field[] GlobalFields = { };

	// ── Advanced / tuning-only knobs ──
	// Config fields that shape the BASELINE MIX (peak balances, kit presence) rather than a
	// song's shareable identity. They are NOT in the vibe wire (Encode/Apply never touch them)
	// and NOT in Fields() (so they don't appear as per-genre sliders). Membership in THIS list
	// is exactly the "config value, not a vibe slider" marker. Surfaced to the host (web:
	// config.json) by NAME — names match the MusicGen.Config field 1:1 — so the house mix can
	// be retuned at runtime without a rebuild. Ranges are generous tuning bounds, not the seed
	// grid. Genre-independent; append-only is irrelevant here (not positional / not in seeds).
	public static readonly Field[] AdvancedFields =
	{
		F( "KitPresence", 0f, 4f, false, c => c.KitPresence, ( c, v ) => c.KitPresence = v ),
		// The stereo image, and the house's scale over each song's drawn reverb. Both were vibe
		// sliders; both are environment rather than music. 1 = as designed.
		F( "PanAmount", 0f, 1f, false, c => c.PanAmount, ( c, v ) => c.PanAmount = v ),
		F( "MasterReverb", 0f, 2f, false, c => c.MasterReverb, ( c, v ) => c.MasterReverb = v ),
		// How far each genre's own mix profile (GenreProfile.Mix) is taken. 1 = as designed,
		// 0 = every genre through one neutral mix. The SHAPE of a genre's mix is character and
		// lives in the profile; what the house retunes at runtime is how far to push it.
		F( "GenreMix", 0f, 2f, false, c => c.GenreMix, ( c, v ) => c.GenreMix = v ),
		F( "KickBalance", 0f, 2f, false, c => c.KickBalance, ( c, v ) => c.KickBalance = v ),
		F( "SnareBalance", 0f, 2f, false, c => c.SnareBalance, ( c, v ) => c.SnareBalance = v ),
		F( "TomBalance", 0f, 2f, false, c => c.TomBalance, ( c, v ) => c.TomBalance = v ),
		F( "HatBalance", 0f, 2f, false, c => c.HatBalance, ( c, v ) => c.HatBalance = v ),
		F( "RideBalance", 0f, 2f, false, c => c.RideBalance, ( c, v ) => c.RideBalance = v ),
		F( "CrashBalance", 0f, 2f, false, c => c.CrashBalance, ( c, v ) => c.CrashBalance = v ),
		F( "BassBalance", 0f, 2f, false, c => c.BassBalance, ( c, v ) => c.BassBalance = v ),
		F( "SkankBalance", 0f, 2f, false, c => c.SkankBalance, ( c, v ) => c.SkankBalance = v ),
		F( "OrganBalance", 0f, 2f, false, c => c.OrganBalance, ( c, v ) => c.OrganBalance = v ),
		F( "MelodyBalance", 0f, 2f, false, c => c.MelodyBalance, ( c, v ) => c.MelodyBalance = v ),
		F( "HornBalance", 0f, 2f, false, c => c.HornBalance, ( c, v ) => c.HornBalance = v ),
		F( "KeysBalance", 0f, 2f, false, c => c.KeysBalance, ( c, v ) => c.KeysBalance = v ),
		F( "RhythmGtrBalance", 0f, 2f, false, c => c.RhythmGtrBalance, ( c, v ) => c.RhythmGtrBalance = v ),
		F( "LeadGtrBalance", 0f, 2f, false, c => c.LeadGtrBalance, ( c, v ) => c.LeadGtrBalance = v ),
		// Stereo double-tracking / width (see MusicGen.Config "width" block).
		F( "DoubleTrack", 0f, 1f, false, c => c.DoubleTrack, ( c, v ) => c.DoubleTrack = v ),
		F( "WidthBacking", 0f, 1f, false, c => c.WidthBacking, ( c, v ) => c.WidthBacking = v ),
		F( "WidthLead", 0f, 1f, false, c => c.WidthLead, ( c, v ) => c.WidthLead = v ),
		// Bounded at 20 cents, not 50: half a quarter-tone between two takes is not a double, it is
		// a tuning error, and this is a house-config field with no way for a listener to undo it.
		F( "WidthDetune", 0f, 20f, false, c => c.WidthDetune, ( c, v ) => c.WidthDetune = v ),
		F( "WidthDelayMs", 0f, 40f, false, c => c.WidthDelayMs, ( c, v ) => c.WidthDelayMs = v ),
		F( "WidthJitterMs", 0f, 30f, false, c => c.WidthJitterMs, ( c, v ) => c.WidthJitterMs = v ),
		F( "WidthAmpVar", 0f, 1f, false, c => c.WidthAmpVar, ( c, v ) => c.WidthAmpVar = v ),
		F( "WidthCutoffVar", 0f, 1f, false, c => c.WidthCutoffVar, ( c, v ) => c.WidthCutoffVar = v ),
	};

	/// <summary>Overlay a <c>name → raw value</c> map (the shared config file's "advanced" block)
	/// onto <paramref name="c"/>. Keys match <see cref="AdvancedFields"/> names (= Config field
	/// names) 1:1; unknown keys are ignored and values are clamped to each field's range. Both
	/// hosts use this: s&box reads the file and calls this; the web mirrors it in JS over the
	/// same field list. Call it where the baseline mix is assembled (after defaults/vibe).</summary>
	public static void ApplyAdvanced( IReadOnlyDictionary<string, float> values, MusicGen.Config c )
	{
		if ( c == null || values == null ) return;
		foreach ( var f in AdvancedFields )
			if ( values.TryGetValue( f.Name, out var v ) )
				f.Set( c, Math.Clamp( v, f.Min, f.Max ) );
	}

	sealed class GenreDef
	{
		public string Name;
		public Field[][] Grid; // Grid[instrument][column]
	}

	// Per-genre instrument grids. Each row is volume / tone / character / extra. Order is the
	// display order AND the wire instrument-slot order — append instruments, never reorder.
	static GenreDef SkaPunk()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> Vol( v, g, s );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Ska-Punk",
			Grid = new[]
			{
				Row( "BASS", vol( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v ),
					tone( "BASS", 80f, 1200f, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
					F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 2 ),
					F( "TRIPLETS", 0f, 0.1f, false, c => c.BassTriplets, ( c, v ) => c.BassTriplets = v, "BASS", 3 ) ),
				Row( "SKANK", vol( "SKANK", c => c.SkankVol, ( c, v ) => c.SkankVol = v ),
					tone( "SKANK", 500f, 8000f, c => c.SkankCutoff, ( c, v ) => c.SkankCutoff = v ),
					F( "BITE", 0f, 2000f, false, c => c.SkankHighpass, ( c, v ) => c.SkankHighpass = v, "SKANK", 2 ),
					F( "CHOP", 0.15f, 1f, false, c => c.SkankChop, ( c, v ) => c.SkankChop = v, "SKANK", 3 ) ),
				Row( "ORGAN", vol( "ORGAN", c => c.OrganVol, ( c, v ) => c.OrganVol = v ),
					tone( "ORGAN", 500f, 8000f, c => c.OrganCutoff, ( c, v ) => c.OrganCutoff = v ),
					F( "BUBBLE", 0f, 1f, false, c => c.OrganBubbleChance, ( c, v ) => c.OrganBubbleChance = v, "ORGAN", 2 ),
					F( "VIBRATO", 0f, 12f, false, c => c.OrganVibrato, ( c, v ) => c.OrganVibrato = v, "ORGAN", 3 ) ),
				Row( "LEAD", vol( "LEAD", c => c.MelodyVol, ( c, v ) => c.MelodyVol = v ),
					tone( "LEAD", 500f, 8000f, c => c.LeadCutoff, ( c, v ) => c.LeadCutoff = v ),
					F( "JUMPINESS", 0f, 1f, false, c => c.MelodyLeapChance, ( c, v ) => c.MelodyLeapChance = v, "LEAD", 2 ),
					F( "TRIPLETS", 0f, 0.1f, false, c => c.TripletChance, ( c, v ) => c.TripletChance = v, "LEAD", 3 ) ),
				Row( "HORNS", vol( "HORNS", c => c.HornVol, ( c, v ) => c.HornVol = v ),
					tone( "HORNS", 500f, 8000f, c => c.HornCutoff, ( c, v ) => c.HornCutoff = v ),
					F( "SECTION", 0f, 1f, false, c => c.HornSectionChance, ( c, v ) => c.HornSectionChance = v, "HORNS", 2 ),
					F( "DENSITY", 0f, 1f, false, c => c.HornDensity, ( c, v ) => c.HornDensity = v, "HORNS", 3 ) ),
				DrumsRow(),
				// The chorus guitar. Third-wave ska drops the skank for driven power chords once the
				// section is loud (GenreProfile.LoudComp), so the rhythm guitar is now a part a ska
				// song actually plays and the listener needs the same handles the other genres get.
				// It goes AFTER the drums, which is what append-only actually requires: a row's wire
				// position is its INDEX in this grid, so slotting it next to the other instruments
				// would have moved DRUMS from slot 5 to slot 6 and silently repointed every existing
				// ska seed's drum knobs. New rows go on the end, however untidy that reads.
				Row( "CHORUS GTR", vol( "CHORUS GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v ),
					tone( "CHORUS GTR", 500f, 8000f, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "CHORUS GTR", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "CHORUS GTR", 3 ) ),
			},
		};
	}

	static GenreDef Rock()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> Vol( v, g, s );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Rock",
			Grid = new[]
			{
				DrumsRow(),
				Row( "BASS", vol( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v ),
					tone( "BASS", 80f, 1200f, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
					F( "DRIVE", 1f, 4f, false, c => c.BassDrive, ( c, v ) => c.BassDrive = v, "BASS", 2 ),
					F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 3 ) ),
				Row( "KEYS", vol( "KEYS", c => c.KeysVol, ( c, v ) => c.KeysVol = v ),
					tone( "KEYS", 500f, 8000f, c => c.KeysCutoff, ( c, v ) => c.KeysCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.KeysDrive, ( c, v ) => c.KeysDrive = v, "KEYS", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.KeysChug, ( c, v ) => c.KeysChug = v, "KEYS", 3 ) ),
				Row( "LEAD GTR", vol( "LEAD GTR", c => c.LeadGtrVol, ( c, v ) => c.LeadGtrVol = v ),
					tone( "LEAD GTR", 500f, 8000f, c => c.LeadGtrCutoff, ( c, v ) => c.LeadGtrCutoff = v ),
					// Floor raised: the old top of the range (drive 5) is now the new minimum, keeping
					// the per-step interval the old grid had (4/11 of a drive unit) across all 16 levels.
					F( "DISTORTION", 5f, 5f + 15f * (4f / 11f), false, c => c.LeadGtrDrive, ( c, v ) => c.LeadGtrDrive = v, "LEAD GTR", 2 ),
					F( "BENDINESS", 0f, 1f, false, c => c.LeadGtrBend, ( c, v ) => c.LeadGtrBend = v, "LEAD GTR", 3 ) ),
				// Appended after LEAD GTR to keep the wire instrument-slot order stable (KEYS kept
				// slot 2's positions; this twangy rhythm guitar takes a fresh appended slot).
				Row( "RHYTHM GTR", vol( "RHYTHM GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v ),
					tone( "RHYTHM GTR", 500f, 8000f, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "RHYTHM GTR", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "RHYTHM GTR", 3 ) ),
			},
		};
	}

	static GenreDef Country()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> Vol( v, g, s );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Country",
			Grid = new[]
			{
				DrumsRow(),
				Row( "BASS", vol( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v ),
					tone( "BASS", 80f, 1200f, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
					F( "DRIVE", 1f, 4f, false, c => c.BassDrive, ( c, v ) => c.BassDrive = v, "BASS", 2 ),
					F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 3 ) ),
				// RHYTHM GTR — clean strummed open chords (the country base distortion is low, so the
				// DISTORTION knob rides over a much cleaner floor than rock's).
				Row( "RHYTHM GTR", vol( "RHYTHM GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v ),
					tone( "RHYTHM GTR", 500f, 8000f, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "RHYTHM GTR", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "RHYTHM GTR", 3 ) ),
				// KEYS — honky-tonk piano comp (cleaned up from rock's distorted organ).
				Row( "KEYS", vol( "KEYS", c => c.KeysVol, ( c, v ) => c.KeysVol = v ),
					tone( "KEYS", 500f, 8000f, c => c.KeysCutoff, ( c, v ) => c.KeysCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.KeysDrive, ( c, v ) => c.KeysDrive = v, "KEYS", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.KeysChug, ( c, v ) => c.KeysChug = v, "KEYS", 3 ) ),
				// LEAD GTR — twangy telecaster: clean base + heavy BENDINESS.
				Row( "LEAD GTR", vol( "LEAD GTR", c => c.LeadGtrVol, ( c, v ) => c.LeadGtrVol = v ),
					tone( "LEAD GTR", 500f, 8000f, c => c.LeadGtrCutoff, ( c, v ) => c.LeadGtrCutoff = v ),
					F( "DISTORTION", 1f, 6f, false, c => c.LeadGtrDrive, ( c, v ) => c.LeadGtrDrive = v, "LEAD GTR", 2 ),
					F( "BENDINESS", 0f, 1f, false, c => c.LeadGtrBend, ( c, v ) => c.LeadGtrBend = v, "LEAD GTR", 3 ) ),
			},
		};
	}

	static GenreDef Metal()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> Vol( v, g, s );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Metal",
			Grid = new[]
			{
				DrumsRow(),
				Row( "BASS", vol( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v ),
					tone( "BASS", 80f, 1200f, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
					F( "DRIVE", 1f, 4f, false, c => c.BassDrive, ( c, v ) => c.BassDrive = v, "BASS", 2 ),
					F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 3 ) ),
				// RHYTHM GTR — palm-muted gallop riff. Heavy base distortion; DISTORTION knob piles on.
				Row( "RHYTHM GTR", vol( "RHYTHM GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v ),
					tone( "RHYTHM GTR", 500f, 8000f, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v ),
					F( "DISTORTION", 1f, 6f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "RHYTHM GTR", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "RHYTHM GTR", 3 ) ),
				// LEAD GTR — fast shredding lead, heavily distorted.
				Row( "LEAD GTR", vol( "LEAD GTR", c => c.LeadGtrVol, ( c, v ) => c.LeadGtrVol = v ),
					tone( "LEAD GTR", 500f, 8000f, c => c.LeadGtrCutoff, ( c, v ) => c.LeadGtrCutoff = v ),
					F( "DISTORTION", 5f, 5f + 15f * (4f / 11f), false, c => c.LeadGtrDrive, ( c, v ) => c.LeadGtrDrive = v, "LEAD GTR", 2 ),
					F( "BENDINESS", 0f, 1f, false, c => c.LeadGtrBend, ( c, v ) => c.LeadGtrBend = v, "LEAD GTR", 3 ) ),
			},
		};
	}

	// Punk (Genre 4) — "lean punk" / power-pop. Reuses rock's voices but drops the keys: a lean
	// guitar/bass/drums kit. Bright-major harmony + fast tempo live in MusicGen, not the grid.
	static GenreDef Punk()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> Vol( v, g, s );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Punk",
			Grid = new[]
			{
				DrumsRow(),
				Row( "BASS", vol( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v ),
					tone( "BASS", 80f, 1200f, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
					F( "DRIVE", 1f, 4f, false, c => c.BassDrive, ( c, v ) => c.BassDrive = v, "BASS", 2 ),
					F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 3 ) ),
				// RHYTHM GTR — bright power chords, a touch under the lead's gain.
				Row( "RHYTHM GTR", vol( "RHYTHM GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v ),
					tone( "RHYTHM GTR", 500f, 8000f, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "RHYTHM GTR", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "RHYTHM GTR", 3 ) ),
				// LEAD GTR — snotty, lightly-driven single-note lead.
				Row( "LEAD GTR", vol( "LEAD GTR", c => c.LeadGtrVol, ( c, v ) => c.LeadGtrVol = v ),
					tone( "LEAD GTR", 500f, 8000f, c => c.LeadGtrCutoff, ( c, v ) => c.LeadGtrCutoff = v ),
					F( "DISTORTION", 1f, 6f, false, c => c.LeadGtrDrive, ( c, v ) => c.LeadGtrDrive = v, "LEAD GTR", 2 ),
					F( "BENDINESS", 0f, 1f, false, c => c.LeadGtrBend, ( c, v ) => c.LeadGtrBend = v, "LEAD GTR", 3 ) ),
			},
		};
	}

	// Pop (Genre 5) — modern synth/dance-pop. A four-on-the-floor kit, a clean bright synth comp
	// (the KEYS voice) and a plucky synth LEAD (the lead-gtr voice run clean). Bass stays tight.
	static GenreDef Pop()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> Vol( v, g, s );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Pop",
			Grid = new[]
			{
				DrumsRow(),
				Row( "BASS", vol( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v ),
					tone( "BASS", 80f, 1200f, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
					F( "DRIVE", 1f, 4f, false, c => c.BassDrive, ( c, v ) => c.BassDrive = v, "BASS", 2 ),
					F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 3 ) ),
				// SYNTH — the chordal comp (KEYS voice), run clean + bright. PLUCK tightens the
				// ringing pad toward short stabs.
				Row( "SYNTH", vol( "SYNTH", c => c.KeysVol, ( c, v ) => c.KeysVol = v ),
					tone( "SYNTH", 500f, 8000f, c => c.KeysCutoff, ( c, v ) => c.KeysCutoff = v ),
					F( "PLUCK", 0f, 1f, false, c => c.KeysChug, ( c, v ) => c.KeysChug = v, "SYNTH", 2 ),
					null ),
				// LEAD — plucky synth lead (the lead-gtr voice, run clean). GLIDE = bend/slide feel.
				Row( "LEAD", vol( "LEAD", c => c.LeadGtrVol, ( c, v ) => c.LeadGtrVol = v ),
					tone( "LEAD", 500f, 8000f, c => c.LeadGtrCutoff, ( c, v ) => c.LeadGtrCutoff = v ),
					F( "GLIDE", 0f, 1f, false, c => c.LeadGtrBend, ( c, v ) => c.LeadGtrBend = v, "LEAD", 2 ),
					null ),
			},
		};
	}

	// DRUMS is the same four knobs in every genre: volume / tone (toms↔cymbals) / busy /
	// drive (pull↔push).
	static Field[] DrumsRow() => Row( "DRUMS",
		Vol( "DRUMS", c => c.DrumVol, ( c, v ) => c.DrumVol = v ),
		F( "TONE", 0f, 1f, false, c => c.DrumTone, ( c, v ) => c.DrumTone = v, "DRUMS", 1 ),
		F( "BUSY", 0f, 1f, false, c => c.DrumBusy, ( c, v ) => c.DrumBusy = v, "DRUMS", 2 ),
		F( "DRIVE", 0f, 1f, false, c => c.DrumDrive, ( c, v ) => c.DrumDrive = v, "DRUMS", 3 ) );

	static readonly GenreDef[] GenreDefs = { SkaPunk(), Rock(), Country(), Metal(), Punk(), Pop() };

	public static int GenreCount => GenreDefs.Length;
	public static IReadOnlyList<string> Genres
	{
		get { var a = new string[GenreDefs.Length]; for ( int i = 0; i < a.Length; i++ ) a[i] = GenreDefs[i].Name; return a; }
	}

	static GenreDef Def( int genre ) => GenreDefs[Math.Clamp( genre, 0, GenreDefs.Length - 1 )];

	/// <summary>Flat list (globals then the genre's instrument grid, row-major, skipping empty
	/// cells) — the source the music panel iterates to build its controls. Each field carries
	/// its <see cref="Field.Voice"/>/<see cref="Field.Column"/> so the UI can lay out the
	/// matrix without a second table.</summary>
	public static IReadOnlyList<Field> Fields( int genre )
	{
		var list = new List<Field>();
		foreach ( var f in GlobalFields ) if ( f != null ) list.Add( f );
		foreach ( var row in Def( genre ).Grid )
			foreach ( var f in row )
				if ( f != null ) list.Add( f );
		return list;
	}

	/// <summary>True if <paramref name="f"/> is a per-instrument VOLUME knob — column 0 of an
	/// instrument row, kept out of the shareable seed and persisted per-voice instead.</summary>
	public static bool IsVolume( Field f ) => f != null && f.Voice != null && f.Column == 0;

	/// <summary>Read the per-instrument volumes of <paramref name="genre"/> off
	/// <paramref name="c"/> as a <c>voice → 0..1 level</c> map. The key is the instrument's voice
	/// NAME (not its genre/position), so a voice that appears in several genres (e.g. BASS, DRUMS)
	/// shares one persisted level. Merge this into a single store across genres.</summary>
	public static Dictionary<string, float> ReadVolumes( int genre, MusicGen.Config c )
	{
		var d = new Dictionary<string, float>();
		if ( c == null ) return d;
		foreach ( var f in Fields( genre ) )
			if ( IsVolume( f ) ) d[f.Voice] = f.GetNorm( c );
		return d;
	}

	/// <summary>Overlay a <c>voice → 0..1 level</c> map (from <see cref="ReadVolumes"/> / storage)
	/// onto <paramref name="c"/> for <paramref name="genre"/>. Voices absent from the map keep
	/// their current/default level. Call this after <see cref="Apply"/> so a song's saved mix
	/// rides on top of the seed's voicing.</summary>
	public static void ApplyVolumes( int genre, IReadOnlyDictionary<string, float> vols, MusicGen.Config c )
	{
		if ( c == null || vols == null ) return;
		foreach ( var f in Fields( genre ) )
			if ( IsVolume( f ) && vols.TryGetValue( f.Voice, out var n ) )
				f.SetNorm( c, n );
	}

	/// <summary>Encode the vibe-defining knobs of <paramref name="c"/> (including its genre)
	/// to a base-36 string.</summary>
	public static string Encode( MusicGen.Config c )
	{
		if ( c == null ) return "";
		int genre = Math.Clamp( c.Genre, 0, GenreDefs.Length - 1 );
		var sb = new StringBuilder();
		sb.Append( Alphabet[genre] );
		foreach ( var f in GlobalFields ) sb.Append( f != null ? Quant( f, c ) : Alphabet[0] );
		foreach ( var row in Def( genre ).Grid )
			for ( int col = WireFirstColumn; col < Columns; col++ )
				sb.Append( row[col] != null ? Quant( row[col], c ) : Alphabet[0] );
		return sb.ToString();
	}

	static char Quant( Field f, MusicGen.Config c )
	{
		int q = (int)Math.Round( f.GetNorm( c ) * (Levels - 1) );
		return Alphabet[Math.Clamp( q, 0, Levels - 1 )];
	}

	/// <summary>Apply a vibe string onto <paramref name="c"/> in place. Reads the genre from the
	/// first char, then walks the fixed wire positions. Silently ignores empty/malformed input
	/// and any trailing positions the string is too short to cover.</summary>
	public static void Apply( string vibe, MusicGen.Config c )
	{
		if ( c == null || string.IsNullOrWhiteSpace( vibe ) ) return;
		vibe = vibe.Trim().ToLowerInvariant();

		int genre = Alphabet.IndexOf( vibe[0] );
		if ( genre >= 0 && genre < GenreDefs.Length ) c.Genre = genre;

		int pos = 1;
		foreach ( var f in GlobalFields )
		{
			ApplyAt( vibe, pos, f, c );
			pos++;
		}
		foreach ( var row in Def( c.Genre ).Grid )
			for ( int col = WireFirstColumn; col < Columns; col++ )
			{
				ApplyAt( vibe, pos, row[col], c );
				pos++;
			}
	}

	static void ApplyAt( string vibe, int pos, Field f, MusicGen.Config c )
	{
		if ( f == null || pos >= vibe.Length ) return;
		int q = Alphabet.IndexOf( vibe[pos] );
		if ( q < 0 ) return; // skip unknown chars, keep the knob value
		f.SetNorm( c, q / (float)(Levels - 1) );
	}

	/// <summary>
	/// Randomise the vibe knobs of <paramref name="c"/> in place.
	///
	/// This is the one definition of what "reroll" means, shared by every player, so the two
	/// drivers cannot answer the question differently: a fresh genre and every knob of that
	/// genre. Per-instrument volumes are excluded by default — they are a local mix preference
	/// and never ride in the seed.
	///
	/// Randomness is the CALLER's: <paramref name="rnd"/> returns values in [0,1). A driver that
	/// wants a throwaway roll passes a session RNG; one that wants a reproducible roll passes a
	/// seeded stream (see <see cref="RollFrom"/>). The engine stays free of any ambient RNG.
	/// </summary>
	public static void Roll( MusicGen.Config c, Func<float> rnd,
		bool includeGenre = true, bool includeVolumes = false )
	{
		if ( c == null || rnd == null ) return;

		if ( includeGenre )
		{
			// Guard the top of the range: a generator returning exactly 1.0 must not index off
			// the end of the genre table.
			int g = (int)(rnd() * GenreCount);
			c.Genre = Math.Clamp( g, 0, GenreCount - 1 );
		}

		foreach ( var f in Fields( c.Genre ) )
		{
			if ( !includeVolumes && IsVolume( f ) ) continue;
			f.SetNorm( c, rnd() );
		}
	}

	/// <summary>
	/// <see cref="Roll"/> driven by a stream seeded on <paramref name="seed"/> — the same string
	/// always produces the same vibe, on any machine and in any player.
	///
	/// This is what lets an endless shuffled sequence still BE its seed: a player derives song
	/// n's vibe from <c>"{tag}:vibe:{n}"</c> rather than from session randomness, so the whole
	/// line is reproducible, shareable and survives a reload — and stepping back to an earlier
	/// song replays exactly what was heard without anything having to be remembered.
	/// </summary>
	public static void RollFrom( MusicGen.Config c, string seed,
		bool includeGenre = true, bool includeVolumes = false )
	{
		var rng = new Rng( seed ?? "" );
		Roll( c, rng.Next, includeGenre, includeVolumes );
	}

	/// <summary>The station a tag names: trimmed and lower-cased, so "Gamah" and " gamah " are one
	/// station rather than three, with the default for an empty tag.</summary>
	/// <remarks>Every stream name in the toy is built from this, on BOTH targets, because the
	/// fallback word is load-bearing: it is part of what song a seed with no tag resolves to, and
	/// a host that picks its own makes <c>vibe::23</c> a different song there than everywhere
	/// else. It has been exactly that — the s&amp;box player spelled the fallback "skafinity"
	/// while the engine and the web spelled it "rotaliate".</remarks>
	static string Station( string tag ) =>
		string.IsNullOrWhiteSpace( tag ) ? "rotaliate" : tag.Trim().ToLowerInvariant();

	/// <summary>The PRNG stream song <paramref name="n"/> is COMPOSED from — what a host hands
	/// <see cref="MusicGen.Generate"/>/<see cref="MusicGen.BeginPlan"/> as the tag.</summary>
	public static string SongSeed( string tag, int n ) => $"{Station( tag )}:{n}";

	/// <summary>The stream name song <paramref name="n"/>'s vibe is rolled from. One definition
	/// so every player walks the same line for a given tag.</summary>
	public static string VibeSeed( string tag, int n ) => $"{Station( tag )}:vibe:{n}";

	/// <summary>The shortest string <see cref="Encode"/> can produce — the leanest genre's grid.
	/// Derived rather than written down, so shrinking or growing a grid cannot leave the floor in
	/// <see cref="LooksLikeVibe"/> behind.</summary>
	static readonly int ShortestVibe = ComputeShortestVibe();

	static int ComputeShortestVibe()
	{
		int min = int.MaxValue;
		foreach ( var g in GenreDefs )
			min = Math.Min( min, 1 + GlobalFields.Length + g.Grid.Length * WireColumns );
		return min;
	}

	/// <summary>True if <paramref name="s"/> looks like a vibe token: all base-36 and no shorter
	/// than the shortest thing <see cref="Encode"/> emits. The floor is the whole test — it has to
	/// stay above an 8-char player tag (and the 9-char default "rotaliate") so the two never collide
	/// in <c>vibe:tag:n</c>; a grid lean enough to drop it there is the signal to lengthen the wire,
	/// not the floor. There is deliberately no ceiling: the wire grows as genres and instruments are
	/// appended, and a bound would silently start rejecting valid seeds.</summary>
	public static bool LooksLikeVibe( string s )
	{
		if ( string.IsNullOrEmpty( s ) || s.Length < ShortestVibe ) return false;
		foreach ( var ch in s.ToLowerInvariant() )
			if ( Alphabet.IndexOf( ch ) < 0 ) return false;
		return true;
	}
}
