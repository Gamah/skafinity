using System;

namespace Skafinity;

/// <summary>
/// Per-genre character that is NOT a user knob — the things a genre simply *is*, which a
/// listener should never have to dial in and a shuffle should never randomise across.
///
/// The line this table draws: it holds what the COMPOSER reads — harmony tables, tempo band,
/// swing band, harmonic rhythm, drum style, the shape of the arrangement. Per-voice TIMBRE
/// tables (a genre's lead distortion, its comp expression) stay next to the voice that renders
/// them, because they are the sound of one instrument rather than the identity of the genre.
///
/// Everything here is drawn per song from the seed, exactly the way tempo is, so a genre has a
/// consistent character with room to vary between songs.
/// </summary>
readonly struct GenreProfile
{
	/// <summary>Swing band, as a fraction of an eighth note.</summary>
	public readonly float SwingMin, SwingMax;

	/// <summary>The genre's own tempo band, and its uptempo band (drawn when the song rolls
	/// FAST). One band per genre is the point: metal at country's tempo is not metal.</summary>
	public readonly int BpmMin, BpmMax, FastBpmMin, FastBpmMax;

	/// <summary>Always draw from the fast band — the genre has no laid-back mode.</summary>
	public readonly bool AlwaysFast;

	/// <summary>Bars per chord — the harmonic rhythm. 2 is the reggae/rock norm; 1 makes the
	/// four-chord loop itself the four-bar hypermeasure, which is what punk and pop do.</summary>
	public readonly int ChordBars;

	/// <summary>Drum style: 0 one-drop, 1 steppers, 2 straight backbeat, 3 double-kick,
	/// 4 four-on-the-floor. -1 = the genre rolls one per song (see <see cref="DrawDrumStyle"/>).</summary>
	public readonly int DrumStyle;

	/// <summary>Base lean toward riding the ride cymbal rather than the closed hats. The song
	/// spreads around this and each section rolls against the result.</summary>
	public readonly float RideLean;

	/// <summary>True if the lead line is the ska horn section rather than a lead guitar.</summary>
	public readonly bool HornLead;

	/// <summary>The harmony tables this genre draws from — one <c>rng.Pick</c> each, so a
	/// genre's draw count never depends on how many entries its tables have.</summary>
	public readonly int[][] Scales, Progressions, BassPatterns;

	GenreProfile( float swingMin, float swingMax,
		int bpmMin, int bpmMax, int fastBpmMin, int fastBpmMax, bool alwaysFast,
		int chordBars, int drumStyle, float rideLean, bool hornLead,
		int[][] scales, int[][] progressions, int[][] bassPatterns )
	{
		SwingMin = swingMin; SwingMax = swingMax;
		BpmMin = bpmMin; BpmMax = bpmMax; FastBpmMin = fastBpmMin; FastBpmMax = fastBpmMax;
		AlwaysFast = alwaysFast;
		ChordBars = chordBars; DrumStyle = drumStyle; RideLean = rideLean; HornLead = hornLead;
		Scales = scales; Progressions = progressions; BassPatterns = bassPatterns;
	}

	// Swing is a genre's identity, not a preference. Ska lives on a pushed offbeat; metal and
	// punk are machine-straight and any shuffle at all reads as sloppy; rock and pop sit near
	// straight with a touch of human push; country keeps a light shuffle under the train beat.
	//
	// Tempo is the same kind of value. One shared 130–185 band made a metal song and a country
	// song run at the same speed, which is most of what "they all sound alike" was — so each
	// genre carries its own band and its own uptempo band.
	static readonly GenreProfile[] Profiles =
	{
		// ska — laid-back reggae-rock, pushed offbeat, rolls its own one-drop/steppers feel
		new( 0.10f, 0.22f, 130, 175, 155, 185, false, 2, -1, 0.40f, true,
			Harmony.SkaScales, Harmony.SkaProgressions, Harmony.SkaBassPatterns ),
		// rock — mid-tempo minor vamps behind a straight backbeat
		new( 0.00f, 0.08f, 110, 160, 150, 175, false, 2, 2, 0.55f, false,
			Harmony.RockScales, Harmony.RockProgressions, Harmony.RockBassPatterns ),
		// country — the slowest band; a light shuffle under the train beat
		new( 0.04f, 0.16f, 95, 130, 130, 150, false, 2, 2, 0.30f, false,
			Harmony.CountryScales, Harmony.CountryProgressions, Harmony.CountryBassPatterns ),
		// metal — the widest band: doom-slow through thrash, under a double-kick
		new( 0.00f, 0.02f, 90, 160, 160, 200, false, 2, 3, 0.65f, false,
			Harmony.MetalScales, Harmony.MetalProgressions, Harmony.MetalBassPatterns ),
		// punk — always hot, and a chord per bar so the four-chord loop IS the hypermeasure
		new( 0.00f, 0.03f, 165, 200, 165, 200, true, 1, 2, 0.20f, false,
			Harmony.PunkScales, Harmony.PunkProgressions, Harmony.PunkBassPatterns ),
		// pop — dance tempo over four-on-the-floor, a chord per bar
		new( 0.00f, 0.05f, 100, 128, 124, 140, false, 1, 4, 0.30f, false,
			Harmony.PopScales, Harmony.PopProgressions, Harmony.PopBassPatterns ),
	};

	public static int Count => Profiles.Length;

	public static GenreProfile For( int genre ) => Profiles[Math.Clamp( genre, 0, Profiles.Length - 1 )];

	/// <summary>This song's swing, drawn from the genre's band.</summary>
	/// <param name="fast">Uptempo songs tighten toward the straight end — at speed a wide
	/// shuffle stops reading as a groove and starts reading as a drag.</param>
	public float DrawSwing( Rng rng, bool fast )
	{
		float t = rng.Next();
		float swing = SwingMin + t * (SwingMax - SwingMin);
		return fast ? swing * 0.5f : swing;
	}

	/// <summary>This song's tempo, drawn from the genre's band (its uptempo band when
	/// <paramref name="fast"/>) and then scaled by the listener's TEMPO knob. The band is the
	/// genre's; the knob is the preference riding on top, so a song can be pushed or dragged
	/// without a country song ever ending up at metal's tempo.</summary>
	public int DrawBpm( Rng rng, bool fast, float tempoScale )
	{
		int lo = fast ? FastBpmMin : BpmMin, hi = fast ? FastBpmMax : BpmMax;
		int bpm = lo + rng.Int( Math.Max( 1, hi - lo + 1 ) );
		return Math.Clamp( (int)MathF.Round( bpm * tempoScale ), 40, 300 );
	}

	/// <summary>This song's drum style. A genre with a fixed style consumes no draw; ska rolls
	/// between the one-drop and the steppers feel, but goes straight-backbeat when uptempo.</summary>
	public int DrawDrumStyle( Rng rng, bool fast )
	{
		if ( DrumStyle >= 0 ) return DrumStyle;
		return fast ? 2 : rng.Int( 2 );
	}

	/// <summary>This song's lean toward the ride cymbal, spread ±0.25 around the genre's base so
	/// some songs strongly prefer one. Each SECTION then rolls its own hats-or-ride against it.</summary>
	public float DrawRidePref( Rng rng ) => Math.Clamp( RideLean - 0.25f + 0.5f * rng.Next(), 0f, 1f );
}
