using System;
using System.Collections.Generic;
using System.Text;

namespace Rotaliate.Audio;

/// <summary>
/// Compact, shareable encoding of the "important" <see cref="MusicGen.Config"/> knobs —
/// the ones that define a song's vibe (tempo, swing, voice mix, key tones, feel, lead
/// instrument, horns). Each field is quantised to one base-36 character over its range,
/// so the whole vibe is a short fixed-length string that travels in the seed as
/// <c>vibe:tag:n</c>.
///
/// Lossy by design (36 levels/field) but stable: Encode(Decode(s)) == s, so a vibe
/// shared by one player reproduces the same config knobs for anyone who pastes it.
/// The field ORDER below is the wire format — only ever append, never reorder.
///
/// <see cref="Fields"/> is also the source the music panel renders its vibe controls
/// from (label, range, discrete choices, get/set as a 0..1 fraction).
/// </summary>
public static class VibeCodec
{
	const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
	const int Levels = 36;

	public sealed class Field
	{
		public string Name;
		public float Min, Max;
		public bool Int;
		/// <summary>Discrete option labels (value = Min + index); null for a continuous knob.</summary>
		public string[] Choices;
		public Func<MusicGen.Config, float> Get;
		public Action<MusicGen.Config, float> Set;

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

	// Order is the wire format — append only.
	public static readonly IReadOnlyList<Field> Fields = new[]
	{
		F( "TEMPO MIN", 60, 200, true, c => c.BpmMin, ( c, v ) => c.BpmMin = (int)v ),
		F( "TEMPO MAX", 60, 200, true, c => c.BpmMax, ( c, v ) => c.BpmMax = (int)v ),
		F( "FAST CHANCE", 0f, 1f, false, c => c.FastChance, ( c, v ) => c.FastChance = v ),
		F( "SWING", 0f, 0.4f, false, c => c.Swing, ( c, v ) => c.Swing = v ),
		F( "BASS", 0f, 1.5f, false, c => c.BassVol, ( c, v ) => c.BassVol = v ),
		F( "SKANK", 0f, 1.5f, false, c => c.SkankVol, ( c, v ) => c.SkankVol = v ),
		F( "ORGAN", 0f, 1.5f, false, c => c.OrganVol, ( c, v ) => c.OrganVol = v ),
		F( "LEAD", 0f, 1.5f, false, c => c.MelodyVol, ( c, v ) => c.MelodyVol = v ),
		F( "HORNS", 0f, 1.5f, false, c => c.HornVol, ( c, v ) => c.HornVol = v ),
		F( "BASS TONE", 80f, 1200f, false, c => c.BassCutoff, ( c, v ) => c.BassCutoff = v ),
		F( "SKANK TONE", 500f, 8000f, false, c => c.SkankCutoff, ( c, v ) => c.SkankCutoff = v ),
		F( "LEAD TONE", 500f, 8000f, false, c => c.LeadCutoff, ( c, v ) => c.LeadCutoff = v ),
		F( "RESONANCE", 0.2f, 2f, false, c => c.Resonance, ( c, v ) => c.Resonance = v ),
		F( "OCTAVE POP", 0f, 1f, false, c => c.OctavePopChance, ( c, v ) => c.OctavePopChance = v ),
		F( "ORGAN BUBBLE", 0f, 1f, false, c => c.OrganBubbleChance, ( c, v ) => c.OrganBubbleChance = v ),
		F( "DRUM FILLS", 0f, 1f, false, c => c.FillChance, ( c, v ) => c.FillChance = v ),
		F( "STEREO WIDTH", 0f, 1f, false, c => c.PanAmount, ( c, v ) => c.PanAmount = v ),
		F( "LEAD INSTR", -1, 3, true, c => c.ForceInstrument, ( c, v ) => c.ForceInstrument = (int)v,
			new[] { "RNG", "TRUMPET", "SAX", "ORGAN", "TROMBONE" } ),
		F( "HORN SECTION", 0f, 1f, false, c => c.HornSectionChance, ( c, v ) => c.HornSectionChance = v ),
		F( "HORN DENSITY", 0f, 1f, false, c => c.HornDensity, ( c, v ) => c.HornDensity = v ),
		// append-only — see class doc
		F( "DRUM BUSY", 0f, 1f, false, c => c.DrumBusy, ( c, v ) => c.DrumBusy = v ),
		// triplets are potent — keep the usable band narrow (max 0.1) so a tick is subtle
		F( "TRIPLETS", 0f, 0.1f, false, c => c.TripletChance, ( c, v ) => c.TripletChance = v ),
	};

	static Field F( string name, float min, float max, bool isInt,
		Func<MusicGen.Config, float> get, Action<MusicGen.Config, float> set, string[] choices = null )
		=> new() { Name = name, Min = min, Max = max, Int = isInt, Get = get, Set = set, Choices = choices };

	/// <summary>Fixed length of a well-formed vibe string.</summary>
	public static int Length => Fields.Count;

	/// <summary>Encode the vibe-defining knobs of <paramref name="c"/> to a base-36 string.</summary>
	public static string Encode( MusicGen.Config c )
	{
		if ( c == null ) return "";
		var sb = new StringBuilder( Fields.Count );
		foreach ( var f in Fields )
		{
			int q = (int)Math.Round( f.GetNorm( c ) * (Levels - 1) );
			sb.Append( Alphabet[Math.Clamp( q, 0, Levels - 1 )] );
		}
		return sb.ToString();
	}

	/// <summary>Apply a vibe string onto <paramref name="c"/> in place. Silently ignores
	/// empty / malformed input and any trailing fields the string is too short to cover,
	/// so older/newer vibe lengths degrade gracefully.</summary>
	public static void Apply( string vibe, MusicGen.Config c )
	{
		if ( c == null || string.IsNullOrWhiteSpace( vibe ) ) return;
		vibe = vibe.Trim().ToLowerInvariant();
		int n = Math.Min( vibe.Length, Fields.Count );
		for ( int i = 0; i < n; i++ )
		{
			int q = Alphabet.IndexOf( vibe[i] );
			if ( q < 0 ) continue; // skip unknown chars, keep the knob value
			Fields[i].SetNorm( c, q / (float)(Levels - 1) );
		}
	}

	/// <summary>True if <paramref name="s"/> looks like a vibe token: all base-36 and
	/// vibe-length. Accepts a small range below <see cref="Fields"/>.Count so seeds
	/// shared by an older client (fewer fields) still parse — the missing trailing
	/// fields keep their config defaults via <see cref="Apply"/>. The lower bound stays
	/// well above an 8-char player tag so the two never collide in <c>vibe:tag:n</c>.</summary>
	public static bool LooksLikeVibe( string s )
	{
		if ( string.IsNullOrEmpty( s ) || s.Length < Fields.Count - 4 || s.Length > Fields.Count ) return false;
		foreach ( var ch in s.ToLowerInvariant() )
			if ( Alphabet.IndexOf( ch ) < 0 ) return false;
		return true;
	}
}
