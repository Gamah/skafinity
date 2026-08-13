using System;
using System.Text;

namespace Skafinity;

/// <summary>
/// The seed STRING — what a whole song is, and the only thing that has to travel between two
/// people for them to hear the same thing.
///
///   <c>tag:n[:genre][:vibe]</c>
///
/// * <c>tag</c> — the station: <c>[A-Za-z0-9_-]</c> only. Anything else (a colon, a space, a
///   slash) is a parse ERROR, never a coerced string, because a seed that quietly becomes a
///   different seed is worse than one that is refused.
/// * <c>n</c> — the song index in that station's endless line, and it keeps the job it has always
///   had: Prev/Next are n±1, the look-ahead queue walks an ordered timeline of them, and n is what
///   lets you go back to a song fifty ago that nothing anywhere remembers. Optional in the string
///   (a bare <c>tag</c> is song 0 of that station), because typing a station name is how you go
///   somewhere new.
/// * <c>genre</c> and <c>vibe</c> — both optional, both hex, and ORDER-FREE: they are told apart
///   by length. One char is a genre; <see cref="VibeCodec.VibeLength"/> chars is a vibe. Any other
///   length is an error rather than a guess.
///
/// ABSENT MEANS ROLLED. An omitted genre or vibe is derived deterministically from (tag, n), so it
/// changes with every song and the station stays a station. Present means PINNED. The two roll
/// from separate streams, so pinning one does not move the other: pin a vibe and let genres roll,
/// pin a genre and let vibes roll, pin both and move only n.
///
/// That is why the vibe is genre-independent and full width (see <see cref="VibeCodec"/>) — a
/// pinned vibe has to mean the same thing under a genre that rolled out from under it.
/// </summary>
public static class SeedCodec
{
	/// <summary>No genre pinned — roll it from (tag, n).</summary>
	public const int RolledGenre = -1;

	/// <summary>A parsed seed. <see cref="Genre"/> is <see cref="RolledGenre"/> and
	/// <see cref="Vibe"/> is null where the string pinned nothing.</summary>
	public struct Seed
	{
		public string Tag;
		public int N;
		public int Genre;
		public string Vibe;

		public bool GenrePinned => Genre >= 0;
		public bool VibePinned => Vibe != null;
	}

	/// <summary>The station a tag names: trimmed and lower-cased, so "Gamah" and " gamah " are one
	/// station rather than three, with the default for an empty tag.</summary>
	/// <remarks>Every stream name in the toy is built from this, on BOTH targets, because the
	/// fallback word is load-bearing: it is part of what song a seed with no tag resolves to, and
	/// a host that picks its own makes <c>:23</c> a different song there than everywhere else. It
	/// has been exactly that — the s&amp;box player spelled the fallback "skafinity" while the
	/// engine and the web spelled it "rotaliate".</remarks>
	public static string Station( string tag ) =>
		string.IsNullOrWhiteSpace( tag ) ? "rotaliate" : tag.Trim().ToLowerInvariant();

	/// <summary>The PRNG stream song <paramref name="n"/> is COMPOSED from — what a host hands
	/// <see cref="MusicGen.Generate"/>/<see cref="MusicGen.BeginPlan"/> as the tag.</summary>
	public static string SongSeed( string tag, int n ) => $"{Station( tag )}:{n}";

	/// <summary>The stream song <paramref name="n"/>'s VIBE is rolled from.</summary>
	public static string VibeSeed( string tag, int n ) => $"{Station( tag )}:vibe:{n}";

	/// <summary>The stream song <paramref name="n"/>'s GENRE is rolled from. Separate from
	/// <see cref="VibeSeed"/> on purpose: pinning the vibe must not change which genres a station
	/// plays, and pinning the genre must not change which vibes it rolls.</summary>
	public static string GenreSeed( string tag, int n ) => $"{Station( tag )}:genre:{n}";

	/// <summary>Song <paramref name="n"/>'s rolled vibe — the same string on any machine, in any
	/// player, forever. This is what lets an endless line still BE its seed: nothing has to be
	/// remembered for Prev to replay exactly what was heard.</summary>
	public static string RollVibeFor( string tag, int n )
	{
		var rng = new Rng( VibeSeed( tag, n ) );
		return VibeCodec.RollVibe( rng.Next );
	}

	/// <summary>Song <paramref name="n"/>'s rolled genre.</summary>
	public static int RollGenreFor( string tag, int n )
	{
		var rng = new Rng( GenreSeed( tag, n ) );
		return VibeCodec.RollGenre( rng.Next );
	}

	/// <summary>
	/// The station at position <paramref name="p"/> of a SHUFFLED line — a player that answers each
	/// "next" with a whole new station rather than the next song of this one.
	///
	/// It is DERIVED from the root rather than drawn fresh, and that is the whole design: a random
	/// tag per song would make the line unrepeatable, so Prev could only work by remembering every
	/// station visited, and a reload would lose the lot. Derived, the shuffled line is still just a
	/// seed — walkable in both directions, the same everywhere, and reproducible from one string.
	///
	/// Position 0 is the root itself, so a pasted seed plays the song it names before the shuffle
	/// takes over.
	/// </summary>
	public static string RollTagFor( string root, int p )
	{
		if ( p <= 0 ) return root ?? "";
		var rng = new Rng( $"{Station( root )}:tag:{p}" );
		var sb = new StringBuilder( 8 );
		for ( int i = 0; i < 8; i++ )
		{
			int q = Math.Clamp( (int)(rng.Next() * 36), 0, 35 );
			sb.Append( q < 10 ? (char)('0' + q) : (char)('a' + q - 10) );
		}
		return sb.ToString();
	}

	// ── Parsing ───────────────────────────────────────────────────────────────────────────

	static bool IsTagChar( char ch ) =>
		(ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')
		|| ch == '_' || ch == '-';

	static bool IsHex( string s )
	{
		foreach ( var ch in s )
			if ( VibeCodec.Hex.IndexOf( char.ToLowerInvariant( ch ) ) < 0 ) return false;
		return true;
	}

	/// <summary>Parse a seed string. On failure <paramref name="error"/> is a sentence fit to show
	/// a listener under the seed box, and <paramref name="seed"/> is left at its default — there is
	/// no partial success, because half a seed is a song nobody asked for.</summary>
	public static bool TryParse( string s, out Seed seed, out string error )
	{
		seed = default;
		error = null;
		s = (s ?? "").Trim();
		if ( s.Length == 0 ) { error = "a seed looks like tag:n"; return false; }

		var parts = s.Split( ':' );
		if ( parts.Length > 4 ) { error = "too many parts — tag:n[:genre][:vibe]"; return false; }

		foreach ( var ch in parts[0] )
			if ( !IsTagChar( ch ) )
			{
				error = $"'{ch}' is not allowed in a station name (letters, digits, _ and - only)";
				return false;
			}

		int n = 0;
		if ( parts.Length >= 2 )
		{
			if ( parts[1].Length == 0 ) { error = "the song number is missing"; return false; }
			foreach ( var ch in parts[1] )
				if ( ch < '0' || ch > '9' ) { error = $"'{parts[1]}' is not a song number"; return false; }
			if ( !int.TryParse( parts[1], out n ) ) { error = "that song number is too big"; return false; }
		}

		int genre = RolledGenre;
		string vibe = null;
		for ( int i = 2; i < parts.Length; i++ )
		{
			var p = parts[i];
			if ( p.Length == 0 ) { error = "an empty part — drop the extra ':'"; return false; }
			if ( !IsHex( p ) ) { error = $"'{p}' is not hex (0-9, a-f)"; return false; }
			if ( p.Length == 1 )
			{
				if ( genre != RolledGenre ) { error = "two genres in one seed"; return false; }
				int g = VibeCodec.Hex.IndexOf( char.ToLowerInvariant( p[0] ) );
				if ( g >= VibeCodec.GenreCount )
				{
					error = $"there is no genre '{p}' (0-{VibeCodec.Hex[VibeCodec.GenreCount - 1]})";
					return false;
				}
				genre = g;
			}
			else if ( p.Length == VibeCodec.VibeLength )
			{
				if ( vibe != null ) { error = "two vibes in one seed"; return false; }
				vibe = p.ToLowerInvariant();
			}
			else
			{
				error = $"a vibe is {VibeCodec.VibeLength} characters, not {p.Length}";
				return false;
			}
		}

		seed = new Seed { Tag = parts[0], N = n, Genre = genre, Vibe = vibe };
		return true;
	}

	/// <summary>The canonical string for a seed: pinned parts written genre-then-vibe, rolled parts
	/// left out. Round-trips through <see cref="TryParse"/>.</summary>
	public static string Format( Seed seed )
	{
		var sb = new StringBuilder();
		sb.Append( seed.Tag ?? "" ).Append( ':' ).Append( Math.Max( 0, seed.N ) );
		if ( seed.GenrePinned ) sb.Append( ':' ).Append( VibeCodec.Hex[Math.Clamp( seed.Genre, 0, VibeCodec.GenreCount - 1 )] );
		if ( seed.VibePinned ) sb.Append( ':' ).Append( seed.Vibe );
		return sb.ToString();
	}

	/// <summary>The same seed with everything it left to chance written down — what "copy this
	/// song" hands over, as opposed to "copy this station", which is <see cref="Format"/> of the
	/// seed as it stands.</summary>
	public static Seed Resolved( Seed seed )
	{
		if ( !seed.GenrePinned ) seed.Genre = RollGenreFor( seed.Tag, seed.N );
		if ( !seed.VibePinned ) seed.Vibe = RollVibeFor( seed.Tag, seed.N );
		return seed;
	}

	/// <summary>Put a seed's song onto <paramref name="c"/>: its genre and its 36 knobs, pinned or
	/// rolled. Per-instrument volumes are NOT touched — they are a local mix preference, so a host
	/// overlays its own after this (<see cref="VibeCodec.ApplyVolumes"/>).</summary>
	public static void Apply( Seed seed, MusicGen.Config c )
	{
		if ( c == null ) return;
		var r = Resolved( seed );
		c.Genre = Math.Clamp( r.Genre, 0, VibeCodec.GenreCount - 1 );
		VibeCodec.Apply( r.Vibe, c );
	}
}
