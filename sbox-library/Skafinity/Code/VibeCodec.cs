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
/// where the global block is <see cref="GlobalFields"/> in order, and the instrument grid
/// reserves up to <see cref="MaxInstruments"/> blocks of 4 columns
/// (volume / tone / character / extra). Column <c>c</c> of instrument <c>i</c> always lives
/// at position <c>1 + globals + i*4 + c</c>, so adding a genre, an instrument, or a 5th
/// column never shifts an existing position. APPEND-ONLY now means: append global knobs,
/// append instrument slots (≤ MaxInstruments), and only ever append columns past the 4th.
/// <see cref="Apply"/> ignores trailing chars a shorter string lacks, so a vibe from a
/// client with fewer slots still parses (the missing knobs keep their config defaults).
///
/// Lossy by design (16 levels/knob) but stable: Encode(Decode(s)) == s.
/// </summary>
public static class VibeCodec
{
	const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
	public const int Levels = 16;   // one hex digit per knob
	public const int Columns = 4;       // volume, tone, character, extra
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
			if ( Min == 0f && Max <= 1f ) return $"{(int)Math.Round( v * 100 )}%";
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

	// Shared GLOBAL knobs — the wire's global block, in order (append-only).
	static readonly Field[] GlobalFields =
	{
		F( "TEMPO MIN", 60, 200, true, c => c.BpmMin, ( c, v ) => c.BpmMin = (int)v ),
		F( "TEMPO MAX", 60, 200, true, c => c.BpmMax, ( c, v ) => c.BpmMax = (int)v ),
		F( "TEMPO BIAS", 0f, 1f, false, c => c.FastChance, ( c, v ) => c.FastChance = v ),
		F( "SWING", 0f, 0.4f, false, c => c.Swing, ( c, v ) => c.Swing = v ),
		F( "RESONANCE", 0.2f, 2f, false, c => c.Resonance, ( c, v ) => c.Resonance = v ),
		F( "STEREO WIDTH", 0f, 1f, false, c => c.PanAmount, ( c, v ) => c.PanAmount = v ),
	};

	sealed class GenreDef
	{
		public string Name;
		public Field[][] Grid; // Grid[instrument][column]
	}

	// Per-genre instrument grids. Each row is volume / tone / character / extra. Order is the
	// display order AND the wire instrument-slot order — append instruments, never reorder.
	static GenreDef Ska()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "VOLUME", 0f, 1.5f, false, g, s, v, 0 );
		Field tone( string v, float lo, float hi, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "TONE", lo, hi, false, g, s, v, 1 );
		return new GenreDef
		{
			Name = "Ska",
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
			},
		};
	}

	static GenreDef Rock()
	{
		Field vol( string v, Func<MusicGen.Config, float> g, Action<MusicGen.Config, float> s )
			=> F( "VOLUME", 0f, 1.5f, false, g, s, v, 0 );
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
					F( "RUNS", 0f, 0.1f, false, c => c.LeadGtrTriplets, ( c, v ) => c.LeadGtrTriplets = v, "LEAD GTR", 3 ) ),
				// Appended after LEAD GTR to keep the wire instrument-slot order stable (KEYS kept
				// slot 2's positions; this twangy rhythm guitar takes a fresh appended slot).
				Row( "RHYTHM GTR", vol( "RHYTHM GTR", c => c.RhythmGtrVol, ( c, v ) => c.RhythmGtrVol = v ),
					tone( "RHYTHM GTR", 500f, 8000f, c => c.RhythmGtrCutoff, ( c, v ) => c.RhythmGtrCutoff = v ),
					F( "DISTORTION", 1f, 5f, false, c => c.RhythmGtrDrive, ( c, v ) => c.RhythmGtrDrive = v, "RHYTHM GTR", 2 ),
					F( "CHUG", 0f, 1f, false, c => c.RhythmGtrChug, ( c, v ) => c.RhythmGtrChug = v, "RHYTHM GTR", 3 ) ),
			},
		};
	}

	// DRUMS is the same four knobs in every genre: volume / tone (toms↔cymbals) / busy /
	// drive (pull↔push).
	static Field[] DrumsRow() => Row( "DRUMS",
		F( "VOLUME", 0f, 1.5f, false, c => c.DrumVol, ( c, v ) => c.DrumVol = v, "DRUMS", 0 ),
		F( "TONE", 0f, 1f, false, c => c.DrumTone, ( c, v ) => c.DrumTone = v, "DRUMS", 1 ),
		F( "BUSY", 0f, 1f, false, c => c.DrumBusy, ( c, v ) => c.DrumBusy = v, "DRUMS", 2 ),
		F( "DRIVE", 0f, 1f, false, c => c.DrumDrive, ( c, v ) => c.DrumDrive = v, "DRUMS", 3 ) );

	static readonly GenreDef[] GenreDefs = { Ska(), Rock() };

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
		var list = new List<Field>( GlobalFields );
		foreach ( var row in Def( genre ).Grid )
			foreach ( var f in row )
				if ( f != null ) list.Add( f );
		return list;
	}

	/// <summary>Encode the vibe-defining knobs of <paramref name="c"/> (including its genre)
	/// to a base-36 string.</summary>
	public static string Encode( MusicGen.Config c )
	{
		if ( c == null ) return "";
		int genre = Math.Clamp( c.Genre, 0, GenreDefs.Length - 1 );
		var sb = new StringBuilder();
		sb.Append( Alphabet[genre] );
		foreach ( var f in GlobalFields ) sb.Append( Quant( f, c ) );
		foreach ( var row in Def( genre ).Grid )
			for ( int col = 0; col < Columns; col++ )
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
			for ( int col = 0; col < Columns; col++ )
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

	/// <summary>Largest possible well-formed vibe length: genre + globals + the full reserved
	/// instrument grid.</summary>
	public static int MaxLength => 1 + GlobalFields.Length + MaxInstruments * Columns;

	/// <summary>True if <paramref name="s"/> looks like a vibe token: all base-36 and within
	/// the vibe length band. The floor stays well above an 8-char player tag (and the 9-char
	/// default "rotaliate") so the two never collide in <c>vibe:tag:n</c>.</summary>
	public static bool LooksLikeVibe( string s )
	{
		if ( string.IsNullOrEmpty( s ) || s.Length < 16 || s.Length > MaxLength ) return false;
		foreach ( var ch in s.ToLowerInvariant() )
			if ( Alphabet.IndexOf( ch ) < 0 ) return false;
		return true;
	}
}
