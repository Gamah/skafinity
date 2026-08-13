using System;
using System.Collections.Generic;
using System.Text;

namespace Skafinity;

/// <summary>
/// The knob grid, and its compact hex encoding — the "vibe" half of a seed
/// (<c>tag:n[:genre][:vibe]</c>; the string as a whole is <see cref="SeedCodec"/>'s).
///
/// WIRE FORMAT — one GLOBAL grid, genre-independent and fixed width:
///   <c>[voice 0 cols 1..4][voice 1 cols 1..4]…</c>, one hex digit per cell,
///   <see cref="VoiceCount"/> × <see cref="WireColumns"/> = <see cref="VibeLength"/> chars.
///
/// An instrument sits at the SAME index in every genre, whether or not that genre plays it, and
/// every cell is a fixed (Config field, range) pair — <see cref="Cells"/> — that no genre may
/// redefine. That is what makes a vibe portable: pin one, let the genre roll, and each song reads
/// the same 36 numbers through whatever voices it happens to use. A genre chooses which cells it
/// EXPOSES as sliders and what to call them (<see cref="GenreDef"/>), and nothing else.
///
/// The wire carries a NORMALISED level (0..15 over the cell's range), not a raw value, so a
/// genre's character comes from its voice code and <see cref="GenreProfile"/> — the places that
/// already hold it — rather than from a per-genre range on the knob.
///
/// A vibe is EXACTLY <see cref="VibeLength"/> hex chars. Short, long or non-hex is not a vibe;
/// there is no pad-with-defaults degrade, because a half-read grid is a song nobody chose. Growing
/// the grid (a voice, a 5th column) changes that length and invalidates every shared vibe: it is a
/// format break, not an append. Volume never travels — it is column 0, a local mix preference.
///
/// Lossy by design (16 levels/cell) but stable: Encode(Apply(s)) == s for any valid s.
/// </summary>
public static class VibeCodec
{
	internal const string Hex = "0123456789abcdef";
	public const int Levels = 16;       // one hex digit per knob
	public const int Columns = 5;       // 0 volume, then four travelling columns
	/// <summary>First column that travels. Column 0 is VOLUME — a local mix preference
	/// (see <see cref="ReadVolumes"/>), so the whole column is skipped rather than encoded.</summary>
	public const int WireFirstColumn = 1;
	public const int WireColumns = Columns - WireFirstColumn;

	public sealed class Field
	{
		public string Name;
		public float Min, Max;
		public bool Int;
		/// <summary>Discrete option labels (value = Min + index); null for a continuous knob.</summary>
		public string[] Choices;
		public Func<MusicGen.Config, float> Get;
		public Action<MusicGen.Config, float> Set;
		/// <summary>Instrument row this knob belongs to.</summary>
		public string Voice;
		/// <summary>Matrix column: 0 volume, 1..4 the travelling columns.</summary>
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
		string voice, int column, string[] choices = null )
		=> new() { Name = name, Min = min, Max = max, Int = isInt, Get = get, Set = set,
			Voice = voice, Column = column, Choices = choices };

	// ── The global voice table ────────────────────────────────────────────────────────────
	// Identity is the VOICE, not the label a genre puts on it: ska's "LEAD" and pop's "LEAD" are
	// two different voices (MELODY and LEAD GTR), and pop's "SYNTH" is rock's KEYS. Index order is
	// the wire order and is fixed; a genre's display order is its own (see GenreDef.Rows).
	public const int VoiceMelody = 4;

	sealed class VoiceDef
	{
		public string Name;
		public Field Volume;        // column 0 — never on the wire
		public Field[] Cells;       // columns 1..4, null where the grid has no knob at all
	}

	static VoiceDef V( string name, Func<MusicGen.Config, float> volGet, Action<MusicGen.Config, float> volSet,
		Field c1, Field c2, Field c3, Field c4 )
		=> new() { Name = name, Volume = F( "VOLUME", 0f, 1.5f, false, volGet, volSet, name, 0 ),
			Cells = new[] { c1, c2, c3, c4 } };

	// The cell table. A cell's Config field and RANGE are global — the same 16 levels mean the same
	// thing in every genre, which is what a portable vibe requires. Where a genre wants a different
	// floor or a different amount of an effect, that already lives in its voice code (Guitar.cs and
	// Lead.cs offset the drive per genre) or in GenreProfile; it must not come back here as a
	// per-genre range, or a pinned vibe stops meaning one thing.
	static readonly VoiceDef[] Voices =
	{
		V( "DRUMS", c => c.DrumVol, ( c, v ) => c.DrumVol = v,
			F( "TONE", 0f, 1f, false, c => c.DrumTone, ( c, v ) => c.DrumTone = v, "DRUMS", 1 ),
			F( "BUSY", 0f, 1f, false, c => c.DrumBusy, ( c, v ) => c.DrumBusy = v, "DRUMS", 2 ),
			F( "DRIVE", 0f, 1f, false, c => c.DrumDrive, ( c, v ) => c.DrumDrive = v, "DRUMS", 3 ),
			null ),
		V( "BASS", c => c.BassVol, ( c, v ) => c.BassVol = v,
			F( "TONE", 80f, 1200f, false, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v, "BASS", 1 ),
			F( "DRIVE", 1f, 4f, false, c => c.BassDrive, ( c, v ) => c.BassDrive = v, "BASS", 2 ),
			F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v, "BASS", 3 ),
			F( "TRIPLETS", 0f, 0.1f, false, c => c.BassTriplets, ( c, v ) => c.BassTriplets = v, "BASS", 4 ) ),
		V( "SKANK", c => c.SkankVol, ( c, v ) => c.SkankVol = v,
			F( "TONE", 500f, 8000f, false, c => c.SkankCutoff, ( c, v ) => c.SkankCutoff = v, "SKANK", 1 ),
			F( "BITE", 0f, 2000f, false, c => c.SkankHighpass, ( c, v ) => c.SkankHighpass = v, "SKANK", 2 ),
			F( "CHOP", 0.15f, 1f, false, c => c.SkankChop, ( c, v ) => c.SkankChop = v, "SKANK", 3 ),
			null ),
		V( "ORGAN", c => c.OrganVol, ( c, v ) => c.OrganVol = v,
			F( "TONE", 500f, 8000f, false, c => c.OrganCutoff, ( c, v ) => c.OrganCutoff = v, "ORGAN", 1 ),
			F( "BUBBLE", 0f, 1f, false, c => c.OrganBubbleChance, ( c, v ) => c.OrganBubbleChance = v, "ORGAN", 2 ),
			F( "VIBRATO", 0f, 12f, false, c => c.OrganVibrato, ( c, v ) => c.OrganVibrato = v, "ORGAN", 3 ),
			null ),
		V( "MELODY", c => c.MelodyVol, ( c, v ) => c.MelodyVol = v,
			F( "TONE", 500f, 8000f, false, c => c.LeadCutoff, ( c, v ) => c.LeadCutoff = v, "MELODY", 1 ),
			F( "JUMPINESS", 0f, 1f, false, c => c.MelodyLeapChance, ( c, v ) => c.MelodyLeapChance = v, "MELODY", 2 ),
			F( "TRIPLETS", 0f, 0.1f, false, c => c.TripletChance, ( c, v ) => c.TripletChance = v, "MELODY", 3 ),
			null ),
		V( "HORNS", c => c.HornVol, ( c, v ) => c.HornVol = v,
			F( "TONE", 500f, 8000f, false, c => c.HornCutoff, ( c, v ) => c.HornCutoff = v, "HORNS", 1 ),
			F( "SECTION", 0f, 1f, false, c => c.HornSectionChance, ( c, v ) => c.HornSectionChance = v, "HORNS", 2 ),
			F( "DENSITY", 0f, 1f, false, c => c.HornDensity, ( c, v ) => c.HornDensity = v, "HORNS", 3 ),
			null ),
		V( "KEYS", c => c.KeysVol, ( c, v ) => c.KeysVol = v,
			F( "TONE", 500f, 8000f, false, c => c.KeysCutoff, ( c, v ) => c.KeysCutoff = v, "KEYS", 1 ),
			F( "DISTORTION", 1f, 5f, false, c => c.KeysDrive, ( c, v ) => c.KeysDrive = v, "KEYS", 2 ),
			F( "CHUG", 0f, 1f, false, c => c.KeysChug, ( c, v ) => c.KeysChug = v, "KEYS", 3 ),
			null ),
		V( "RHYTHM GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v,
			F( "TONE", 500f, 8000f, false, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v, "RHYTHM GTR", 1 ),
			F( "DISTORTION", 1f, 6f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "RHYTHM GTR", 2 ),
			F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "RHYTHM GTR", 3 ),
			null ),
		V( "LEAD GTR", c => c.LeadGtrVol, ( c, v ) => c.LeadGtrVol = v,
			F( "TONE", 500f, 8000f, false, c => c.LeadGtrCutoff, ( c, v ) => c.LeadGtrCutoff = v, "LEAD GTR", 1 ),
			F( "DISTORTION", 1f, 6f, false, c => c.LeadGtrDrive, ( c, v ) => c.LeadGtrDrive = v, "LEAD GTR", 2 ),
			F( "BENDINESS", 0f, 1f, false, c => c.LeadGtrBend, ( c, v ) => c.LeadGtrBend = v, "LEAD GTR", 3 ),
			null ),
	};

	public static int VoiceCount => Voices.Length;
	/// <summary>Exact length of a vibe string, derived from the grid so nothing restates it.</summary>
	public static readonly int VibeLength = Voices.Length * WireColumns;

	/// <summary>The wire position of voice <paramref name="v"/>'s column <paramref name="col"/>.</summary>
	static int Pos( int v, int col ) => v * WireColumns + (col - WireFirstColumn);

	/// <summary>Is there a knob at wire position <paramref name="pos"/>? The grid is rectangular, so
	/// some cells are holes — a voice with three columns still reserves its fourth. A hole encodes
	/// as '0' and decodes to nothing, and a roll must leave it at '0' too: anything else would
	/// re-encode to '0' and make a rolled vibe fail to round-trip.</summary>
	public static bool HasCell( int pos )
	{
		if ( pos < 0 || pos >= VibeLength ) return false;
		return Voices[pos / WireColumns].Cells[pos % WireColumns] != null;
	}

	// ── Advanced / tuning-only knobs ──
	// Config fields that shape the BASELINE MIX (peak balances, kit presence) rather than a
	// song's shareable identity. They are NOT in the vibe wire (Encode/Apply never touch them)
	// and NOT in Fields() (so they don't appear as per-genre sliders). Membership in THIS list
	// is exactly the "config value, not a vibe slider" marker. Surfaced to the host (web:
	// config.json) by NAME — names match the MusicGen.Config field 1:1 — so the house mix can
	// be retuned at runtime without a rebuild. Ranges are generous tuning bounds, not the seed
	// grid. Genre-independent; not positional, so nothing here can shift a wire cell.
	public static readonly Field[] AdvancedFields =
	{
		F( "KitPresence", 0f, 4f, false, c => c.KitPresence, ( c, v ) => c.KitPresence = v, null, 0 ),
		// The stereo image, and the house's scale over each song's drawn reverb. Both were vibe
		// sliders; both are environment rather than music. 1 = as designed.
		F( "PanAmount", 0f, 1f, false, c => c.PanAmount, ( c, v ) => c.PanAmount = v, null, 0 ),
		F( "MasterReverb", 0f, 2f, false, c => c.MasterReverb, ( c, v ) => c.MasterReverb = v, null, 0 ),
		// How far each genre's own mix profile (GenreProfile.Mix) is taken. 1 = as designed,
		// 0 = every genre through one neutral mix. The SHAPE of a genre's mix is character and
		// lives in the profile; what the house retunes at runtime is how far to push it.
		F( "GenreMix", 0f, 2f, false, c => c.GenreMix, ( c, v ) => c.GenreMix = v, null, 0 ),
		F( "KickBalance", 0f, 2f, false, c => c.KickBalance, ( c, v ) => c.KickBalance = v, null, 0 ),
		F( "SnareBalance", 0f, 2f, false, c => c.SnareBalance, ( c, v ) => c.SnareBalance = v, null, 0 ),
		F( "TomBalance", 0f, 2f, false, c => c.TomBalance, ( c, v ) => c.TomBalance = v, null, 0 ),
		F( "HatBalance", 0f, 2f, false, c => c.HatBalance, ( c, v ) => c.HatBalance = v, null, 0 ),
		F( "RideBalance", 0f, 2f, false, c => c.RideBalance, ( c, v ) => c.RideBalance = v, null, 0 ),
		F( "CrashBalance", 0f, 2f, false, c => c.CrashBalance, ( c, v ) => c.CrashBalance = v, null, 0 ),
		F( "BassBalance", 0f, 2f, false, c => c.BassBalance, ( c, v ) => c.BassBalance = v, null, 0 ),
		F( "SkankBalance", 0f, 2f, false, c => c.SkankBalance, ( c, v ) => c.SkankBalance = v, null, 0 ),
		F( "OrganBalance", 0f, 2f, false, c => c.OrganBalance, ( c, v ) => c.OrganBalance = v, null, 0 ),
		F( "MelodyBalance", 0f, 2f, false, c => c.MelodyBalance, ( c, v ) => c.MelodyBalance = v, null, 0 ),
		F( "HornBalance", 0f, 2f, false, c => c.HornBalance, ( c, v ) => c.HornBalance = v, null, 0 ),
		F( "KeysBalance", 0f, 2f, false, c => c.KeysBalance, ( c, v ) => c.KeysBalance = v, null, 0 ),
		F( "RhythmGtrBalance", 0f, 2f, false, c => c.RhythmGtrBalance, ( c, v ) => c.RhythmGtrBalance = v, null, 0 ),
		F( "LeadGtrBalance", 0f, 2f, false, c => c.LeadGtrBalance, ( c, v ) => c.LeadGtrBalance = v, null, 0 ),
		// Stereo double-tracking / width (see MusicGen.Config "width" block).
		F( "DoubleTrack", 0f, 1f, false, c => c.DoubleTrack, ( c, v ) => c.DoubleTrack = v, null, 0 ),
		F( "WidthBacking", 0f, 1f, false, c => c.WidthBacking, ( c, v ) => c.WidthBacking = v, null, 0 ),
		F( "WidthLead", 0f, 1f, false, c => c.WidthLead, ( c, v ) => c.WidthLead = v, null, 0 ),
		// Bounded at 20 cents, not 50: half a quarter-tone between two takes is not a double, it is
		// a tuning error, and this is a house-config field with no way for a listener to undo it.
		F( "WidthDetune", 0f, 20f, false, c => c.WidthDetune, ( c, v ) => c.WidthDetune = v, null, 0 ),
		F( "WidthDelayMs", 0f, 40f, false, c => c.WidthDelayMs, ( c, v ) => c.WidthDelayMs = v, null, 0 ),
		F( "WidthJitterMs", 0f, 30f, false, c => c.WidthJitterMs, ( c, v ) => c.WidthJitterMs = v, null, 0 ),
		F( "WidthAmpVar", 0f, 1f, false, c => c.WidthAmpVar, ( c, v ) => c.WidthAmpVar = v, null, 0 ),
		F( "WidthCutoffVar", 0f, 1f, false, c => c.WidthCutoffVar, ( c, v ) => c.WidthCutoffVar = v, null, 0 ),
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

	// ── Genres: which cells they expose, in what order, under what name ───────────────────
	// A genre NEVER redefines what a cell does — it picks the rows a listener gets sliders for and
	// the words on them. Everything a genre sounds like is in its voice code and GenreProfile.
	sealed class RowDef
	{
		public int Voice;                 // index into Voices
		public string Label;              // what this genre calls it (null = the voice's own name)
		public int[] Columns;             // which travelling columns it exposes
		public string[] Labels;           // per-column name override (null entry = the cell's own)
	}

	sealed class GenreDef
	{
		public string Name;
		public RowDef[] Rows;             // display order
	}

	// All four cells of a voice, under the voice's own names. The common case.
	static RowDef R( int voice, string label = null )
		=> new() { Voice = voice, Label = label, Columns = null, Labels = null };

	// A subset of a voice's cells, optionally renamed: R( V, "SYNTH", (1, null), (3, "PLUCK") ).
	static RowDef R( int voice, string label, params (int col, string name)[] cells )
	{
		var cols = new int[cells.Length];
		var names = new string[cells.Length];
		for ( int i = 0; i < cells.Length; i++ ) { cols[i] = cells[i].col; names[i] = cells[i].name; }
		return new RowDef { Voice = voice, Label = label, Columns = cols, Labels = names };
	}

	const int Drums = 0, Bass = 1, Skank = 2, Organ = 3, Melody = 4, Horns = 5,
		Keys = 6, RhythmGtr = 7, LeadGtr = 8;

	static readonly GenreDef[] GenreDefs =
	{
		// Ska-Punk. The chorus guitar is the RHYTHM GTR voice: third-wave ska drops the skank for
		// driven power chords once the section is loud (GenreProfile.LoudComp).
		new() { Name = "Ska-Punk", Rows = new[]
		{
			R( Bass ), R( Skank ), R( Organ ), R( Melody, "LEAD" ), R( Horns ), R( Drums ),
			R( RhythmGtr, "CHORUS GTR" ),
		} },
		new() { Name = "Rock", Rows = new[]
		{
			R( Drums ), R( Bass ), R( Keys ), R( LeadGtr ), R( RhythmGtr ),
		} },
		// Country — clean strummed open chords, honky-tonk piano, twangy telecaster lead. The
		// cleaner floor under each DISTORTION knob is in Guitar.cs / Lead.cs / Keys.cs.
		new() { Name = "Country", Rows = new[]
		{
			R( Drums ), R( Bass ), R( RhythmGtr ), R( Keys ), R( LeadGtr ),
		} },
		new() { Name = "Metal", Rows = new[]
		{
			R( Drums ), R( Bass ), R( RhythmGtr ), R( LeadGtr ),
		} },
		// Punk — "lean punk" / power-pop: rock's voices without the keys.
		new() { Name = "Punk", Rows = new[]
		{
			R( Drums ), R( Bass ), R( RhythmGtr ), R( LeadGtr ),
		} },
		// Pop — modern synth/dance-pop. The KEYS voice run clean and bright (PLUCK tightens the
		// ringing pad toward stabs) and the LEAD GTR voice run clean as a plucky synth lead. Both
		// hide their DISTORTION cell: pop's clean floor is Keys.cs / Lead.cs, and a slider that
		// the voice code overrules is a lie. The cell still TRAVELS — every vibe is full width.
		new() { Name = "Pop", Rows = new[]
		{
			R( Drums ), R( Bass ),
			R( Keys, "SYNTH", (1, null), (3, "PLUCK") ),
			R( LeadGtr, "LEAD", (1, null), (3, "GLIDE") ),
		} },
	};

	public static int GenreCount => GenreDefs.Length;
	public static IReadOnlyList<string> Genres
	{
		get { var a = new string[GenreDefs.Length]; for ( int i = 0; i < a.Length; i++ ) a[i] = GenreDefs[i].Name; return a; }
	}

	static GenreDef Def( int genre ) => GenreDefs[Math.Clamp( genre, 0, GenreDefs.Length - 1 )];

	/// <summary>A genre's row as the UI shows it: the voice's field, relabelled where the genre
	/// says so. The returned Field is a copy — the global cell is never mutated.</summary>
	static Field Labelled( Field cell, string voiceLabel, string nameOverride )
	{
		if ( cell == null ) return null;
		if ( voiceLabel == null && nameOverride == null ) return cell;
		return new Field
		{
			Name = nameOverride ?? cell.Name, Min = cell.Min, Max = cell.Max, Int = cell.Int,
			Choices = cell.Choices, Get = cell.Get, Set = cell.Set,
			Voice = voiceLabel ?? cell.Voice, Column = cell.Column,
		};
	}

	/// <summary>Flat list of the sliders <paramref name="genre"/> shows, in display order: each
	/// row's volume then its exposed cells. Each field carries its <see cref="Field.Voice"/> /
	/// <see cref="Field.Column"/>, so the UI lays the matrix out without a second table.</summary>
	public static IReadOnlyList<Field> Fields( int genre )
	{
		var list = new List<Field>();
		foreach ( var row in Def( genre ).Rows )
		{
			var v = Voices[row.Voice];
			list.Add( Labelled( v.Volume, row.Label, null ) );
			if ( row.Columns == null )
			{
				foreach ( var cell in v.Cells )
					if ( cell != null ) list.Add( Labelled( cell, row.Label, null ) );
			}
			else
			{
				for ( int i = 0; i < row.Columns.Length; i++ )
				{
					var cell = v.Cells[row.Columns[i] - WireFirstColumn];
					if ( cell != null ) list.Add( Labelled( cell, row.Label, row.Labels[i] ) );
				}
			}
		}
		return list;
	}

	/// <summary>True if <paramref name="f"/> is a per-instrument VOLUME knob — column 0 of an
	/// instrument row, kept out of the shareable seed and persisted per-voice instead.</summary>
	public static bool IsVolume( Field f ) => f != null && f.Voice != null && f.Column == 0;

	/// <summary>Read the per-instrument volumes of <paramref name="genre"/> off
	/// <paramref name="c"/> as a <c>voice → 0..1 level</c> map. The key is the label this genre
	/// uses, which is what the UI shows; a voice a genre renames (pop's "SYNTH") therefore keeps
	/// its own level there. Merge this into a single store across genres.</summary>
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

	// ── The wire ──────────────────────────────────────────────────────────────────────────

	/// <summary>Encode the whole global grid off <paramref name="c"/>. Genre-independent: the
	/// result depends on the knobs and on nothing else, so re-encoding after a genre change hands
	/// back the same string.</summary>
	public static string Encode( MusicGen.Config c )
	{
		if ( c == null ) return "";
		var sb = new StringBuilder( VibeLength );
		foreach ( var v in Voices )
			foreach ( var cell in v.Cells )
				sb.Append( cell != null ? Quant( cell, c ) : Hex[0] );
		return sb.ToString();
	}

	static char Quant( Field f, MusicGen.Config c )
	{
		int q = (int)Math.Round( f.GetNorm( c ) * (Levels - 1) );
		return Hex[Math.Clamp( q, 0, Levels - 1 )];
	}

	/// <summary>True if <paramref name="s"/> is a vibe: exactly <see cref="VibeLength"/> hex
	/// chars. There is no near-miss — see the class remarks on why short does not degrade.</summary>
	public static bool IsVibe( string s )
	{
		if ( s == null || s.Length != VibeLength ) return false;
		foreach ( var ch in s )
			if ( Hex.IndexOf( char.ToLowerInvariant( ch ) ) < 0 ) return false;
		return true;
	}

	/// <summary>Apply a vibe string onto <paramref name="c"/> in place. Returns false (touching
	/// nothing) if it is not a vibe — callers that need to TELL the listener use
	/// <see cref="SeedCodec.TryParse"/>, which is where the message lives.</summary>
	public static bool Apply( string vibe, MusicGen.Config c )
	{
		if ( c == null || !IsVibe( vibe ) ) return false;
		vibe = vibe.ToLowerInvariant();
		for ( int v = 0; v < Voices.Length; v++ )
			foreach ( var cell in Voices[v].Cells )
			{
				if ( cell == null ) continue;
				int q = Hex.IndexOf( vibe[Pos( v, cell.Column )] );
				cell.SetNorm( c, q / (float)(Levels - 1) );
			}
		return true;
	}

	// ── Rolling ───────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Roll a whole vibe STRING — every cell of the global grid, genre-independent.
	///
	/// This is the one definition of what "reroll" means, shared by every player, so the two
	/// drivers cannot answer the question differently. It produces a string rather than editing a
	/// Config on purpose: a rolled vibe is full width like any other, so it can be pinned into a
	/// seed and heard identically under a genre that was rolled separately.
	///
	/// Randomness is the CALLER's: <paramref name="rnd"/> returns values in [0,1). A driver that
	/// wants a throwaway roll passes a session RNG; one that wants a reproducible roll passes a
	/// seeded stream (see <see cref="SeedCodec.RollVibeFor"/>). The engine stays free of any
	/// ambient RNG.
	/// </summary>
	public static string RollVibe( Func<float> rnd )
	{
		if ( rnd == null ) return new string( Hex[0], VibeLength );
		var sb = new StringBuilder( VibeLength );
		for ( int i = 0; i < VibeLength; i++ )
		{
			if ( !HasCell( i ) ) { sb.Append( Hex[0] ); continue; }   // a hole stays a hole
			// Guard the top of the range: a generator returning exactly 1.0 must not index off the
			// end of the alphabet.
			int q = (int)(rnd() * Levels);
			sb.Append( Hex[Math.Clamp( q, 0, Levels - 1 )] );
		}
		return sb.ToString();
	}

	/// <summary>Roll a genre index from the same kind of caller-owned stream.</summary>
	public static int RollGenre( Func<float> rnd )
		=> rnd == null ? 0 : Math.Clamp( (int)(rnd() * GenreCount), 0, GenreCount - 1 );

	/// <summary>Roll the per-instrument volumes of <paramref name="genre"/> in place — the one
	/// thing a vibe string cannot carry, for a driver that wants the mix rolled too.</summary>
	public static void RollVolumes( int genre, MusicGen.Config c, Func<float> rnd )
	{
		if ( c == null || rnd == null ) return;
		foreach ( var f in Fields( genre ) )
			if ( IsVolume( f ) ) f.SetNorm( c, rnd() );
	}
}
