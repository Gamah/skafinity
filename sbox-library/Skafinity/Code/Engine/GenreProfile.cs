using System;

namespace Skafinity;

/// <summary>How the genre's main chordal voice comps. The figure itself is a
/// <see cref="Pattern"/>; this says what a hit MEANS when that voice plays it.</summary>
enum CompStyle
{
	/// <summary>Ska: the offbeat chop, short and bright.</summary>
	Skank,
	/// <summary>Rock: a two-bar power-chord riff motif — hits that ring, not an eighth-note wall.</summary>
	Riff,
	/// <summary>Country: the "chick" — a clean strum on the offbeats, over the bass's boom.</summary>
	BoomChick,
	/// <summary>Punk: relentless downstrokes, one chord per bar.</summary>
	Downstroke,
	/// <summary>Metal: the palm-muted gallop, chord accents on the ring hits.</summary>
	Gallop,
	/// <summary>Pop: a held pad under everything.</summary>
	Pad,
}

/// <summary>How the genre's SECOND chordal voice (keys / piano / synth) plays, when it has one.
/// </summary>
enum KeysStyle
{
	None,
	/// <summary>Rock: the syncopated Charleston organ comp.</summary>
	Stabs,
	/// <summary>Country: honky-tonk piano answering on 2 and 4.</summary>
	HonkyTonk,
	/// <summary>Pop: a sixteenth-note arpeggio over the pad.</summary>
	Arp,
}

/// <summary>What kind of line the lead plays. One <c>RenderLeadPhrase</c> served every genre with
/// the same rest chance, the same leap logic and the same register, which made the melody the
/// most interchangeable thing in the song.</summary>
enum LeadStyle
{
	/// <summary>Ska: the horn line — call in the first phrase, answer in the second.</summary>
	HornLine,
	/// <summary>Rock: mid-register, bendy, spare.</summary>
	Bluesy,
	/// <summary>Country: pentatonic double-stops and heavy bends.</summary>
	DoubleStop,
	/// <summary>Metal: fast scalar runs across the whole register.</summary>
	Shred,
	/// <summary>Punk: mostly absent; doubles the guitar's riff when it does play.</summary>
	Unison,
	/// <summary>Pop: a two-bar hook, REPEATED — no other genre repeats a motif.</summary>
	Hook,
}

/// <summary>How a song lands. Every song used to end on the identical fixed pad — same
/// oscillator, same envelope, same length, whatever the genre and whatever the song — which is
/// why the last four seconds of every track sounded like the same track.</summary>
enum EndingStyle
{
	/// <summary>The band hits the tonic together and lets it ring out.</summary>
	Ring,
	/// <summary>A short, hard unison stab and then nothing. Punk and metal stop; they do not fade.
	/// </summary>
	StopHit,
	/// <summary>A real cadence: the V chord, then the tonic landing on beat 3.</summary>
	Cadence,
	/// <summary>The figure keeps going and falls away — the pop fade, without the fade-out.</summary>
	Fall,
}

/// <summary>Per-genre mix trim. Not a knob and not a per-song draw: a genre's records simply
/// sound like this. Scaled globally by <c>Config.GenreMix</c> so the house can dial the whole
/// effect back at runtime without a rebuild.</summary>
readonly struct MixProfile
{
	/// <summary>Multipliers on the master reverb wet, the stereo width, and the three broad
	/// bands (bass/low, body/mid, cymbals+top).</summary>
	public readonly float Reverb, Width, Low, Mid, High;

	public MixProfile( float reverb, float width, float low, float mid, float high )
	{ Reverb = reverb; Width = width; Low = low; Mid = mid; High = high; }
}

/// <summary>
/// Per-genre character that is NOT a user knob — the things a genre simply *is*, which a
/// listener should never have to dial in and a shuffle should never randomise across.
///
/// The line this table draws: it holds what the COMPOSER reads — harmony tables, tempo band,
/// swing band, harmonic rhythm, the FORM, the comp figures, the grooves, the lead grammar, the
/// accent weights. Per-voice TIMBRE (a genre's lead distortion, its bass register and filter,
/// its comp expression) stays next to the voice that renders it, because that is the sound of
/// one instrument rather than the identity of the genre.
///
/// Everything drawn here is drawn per song from the seed, exactly the way tempo is, so a genre
/// has a consistent character with room to vary between songs. EVERY GENRE PULLS THE SAME
/// NUMBER OF VALUES out of the song stream — a weighted draw is one <c>Next()</c> whatever the
/// table holds, and a genre with a fixed value consumes no draw at all in either direction.
/// </summary>
sealed class GenreProfile
{
	// ── Feel ──
	/// <summary>Chance this song swings AT ALL. Swing is a decision before it is a depth: a band
	/// alone cannot say "straight", because its floor is a swing so shallow the ear reads it as
	/// straight anyway — which made "how swung" and "swung or not" the same number, and neither
	/// one legible. A genre that never swings declares 0 and needs no band.</summary>
	public float SwingChance { get; init; }

	/// <summary>Swing band, as a fraction of an eighth note — how deep, GIVEN that the song swings.
	/// Because a straight song is now reachable directly, the floor here is what the genre's swing
	/// sounds like when it is present, not the point where it fades out.</summary>
	public float SwingMin { get; init; }
	public float SwingMax { get; init; }

	/// <summary>Chance this song takes a genuine 2:1 TRIPLET SHUFFLE instead of drawing from the
	/// swing band. A shuffle is a different feel, not a wider swing: widening the band to reach
	/// ~0.33 would just make ordinary songs sloppy on the way there. Ska and country only.</summary>
	public float ShuffleChance { get; init; }
	public float ShuffleMin { get; init; } = 0.30f;
	public float ShuffleMax { get; init; } = 0.36f;

	/// <summary>The genre's own tempo band, and its uptempo band (drawn when the song rolls
	/// FAST). One band per genre is the point: metal at country's tempo is not metal.</summary>
	public int BpmMin { get; init; }
	public int BpmMax { get; init; }
	public int FastBpmMin { get; init; }
	public int FastBpmMax { get; init; }

	/// <summary>Always draw from the fast band — the genre has no laid-back mode.</summary>
	public bool AlwaysFast { get; init; }

	/// <summary>Where the listener's TEMPO knob SATURATES for this genre — the slowest and fastest
	/// this music is still itself.
	///
	/// The knob is one global 0.70–1.45 multiplier over whatever the genre drew, and a symmetric
	/// range chosen against no band in particular put its ends outside every genre at once: ska
	/// 268, metal 290 and punk 290 at the top, metal 63 and country 67 at the bottom. None of
	/// those are tempos these genres are, so the usable part of the slider was its middle.
	///
	/// Narrowing the knob would have been one number, but it takes headroom off punk and ska that
	/// they can genuinely use. The band is the genre's and the knob is the preference riding on
	/// top — so the knob keeps its full travel in the UI and each genre stops where it stops
	/// being itself. These are ceilings for the KNOB: the drawn band must sit inside them, so at
	/// neutral nothing is ever clamped.</summary>
	public int TempoFloor { get; init; }
	public int TempoCeil { get; init; }

	/// <summary>Bars per chord — the harmonic rhythm. 2 is the reggae/rock norm; 1 makes the
	/// four-chord loop itself the four-bar hypermeasure, which is what punk and pop do.</summary>
	public int ChordBars { get; init; } = 2;

	/// <summary>Base lean toward riding the ride cymbal rather than the closed hats. The song
	/// spreads around this and each section rolls against the result.</summary>
	public float RideLean { get; init; }

	/// <summary>True if the lead line is the ska horn section rather than a lead guitar.</summary>
	public bool HornLead { get; init; }

	// ── Form ──
	/// <summary>The genre's section map (see <see cref="SongForm"/>).</summary>
	public Part[] Form { get; init; }

	// ── Harmony ──
	/// <summary>The harmony tables this genre draws from — one weighted draw each, so a genre's
	/// draw count never depends on how many entries its tables have.</summary>
	public int[][] Scales { get; init; }
	public int[] ScaleWeights { get; init; }
	public int[][] Progressions { get; init; }
	/// <summary>Chord voicings in scale-degree space (see <see cref="Harmony"/>).</summary>
	public int[][] Voicings { get; init; }
	public int[] VoicingWeights { get; init; }

	// ── Parts ──
	/// <summary>The genre's bass library. Patterns carry their own length, so these are the
	/// two- and four-bar phrases the genre actually plays, not one bar repeated.</summary>
	public Pattern[] BassPatterns { get; init; }

	/// <summary>The comp figures of the genre's main chordal voice, and how to play them.</summary>
	public Pattern[] CompFigures { get; init; }
	public CompStyle Comp { get; init; }

	/// <summary>What the main chordal voice becomes once the section is loud enough, and the
	/// figures it plays there. Null (the default) means the genre comps one way all song.
	///
	/// This is a genre's DYNAMIC, not a second genre: the same voice, the same chord, a different
	/// instrument technique because the section asked for one. Third-wave ska is the case that
	/// needs it — a clean offbeat skank through the verse dropping into distorted power chords for
	/// the chorus is the single most recognisable thing about the style, and no amount of tuning
	/// the skank reaches it, because the chorus is not a louder skank but a different part.
	///
	/// The threshold is on the section's ENERGY rather than on its type, so it stays the same kind
	/// of value as everything else voices read — a genre that wanted its bridge loud would get it
	/// by giving the bridge energy, not by naming the bridge here.</summary>
	public Pattern[] LoudCompFigures { get; init; }
	public CompStyle LoudComp { get; init; }
	public float LoudFrom { get; init; } = 0.9f;


	/// <summary>The second chordal voice — the keys/piano/synth layer, where the genre has one.
	/// </summary>
	public Pattern[] KeysFigures { get; init; }
	public KeysStyle Keys { get; init; } = KeysStyle.None;

	/// <summary>The genre's drum grooves (see <see cref="DrumGroove"/>), drawn per song.</summary>
	public DrumGroove[] Grooves { get; init; }
	public int[] GrooveWeights { get; init; }

	/// <summary>How the lead phrases.</summary>
	public LeadStyle Lead { get; init; }
	/// <summary>Phrase length in bars, and the chance a phrase is a rest instead. A genre whose
	/// lead is mostly absent (punk) simply rests most phrases rather than being special-cased.
	/// </summary>
	public int LeadPhraseBars { get; init; } = 2;
	public float LeadSilence { get; init; }

	/// <summary>True if the bass follows the riff — reading the rhythm guitar's onsets rather
	/// than playing an independent pattern. Metal's two real modes (a pedal point, or doubling
	/// the riff) are both relational; punk gets the unison option from the same switch.</summary>
	public float RiffBassChance { get; init; }

	// ── Dynamics ──
	/// <summary>Velocity weights by metric position: the downbeat, the backbeat (2 and 4), and
	/// the offbeats. This is what a genre's accent pattern IS — country leans on the offbeat
	/// "chick", metal flattens everything, ska pushes the offbeat hardest.</summary>
	public float AccentDown { get; init; } = 1f;
	public float AccentBack { get; init; } = 1f;
	public float AccentOff { get; init; } = 0.85f;

	/// <summary>The genre's mix trim.</summary>
	public MixProfile Mix { get; init; }

	/// <summary>How this genre's songs land, drawn per song. One weighted draw, like everything
	/// else here, so the ending costs the same single value in every genre.</summary>
	public EndingStyle[] Endings { get; init; }
	public int[] EndingWeights { get; init; }

	// Swing is a genre's identity, not a preference — and it is a yes/no before it is a depth.
	// Country is the genre that kept the shuffle; rock takes one a quarter of the time and is
	// otherwise straight; ska (third wave), metal, punk and pop never swing at all. Tempo and
	// swing are not independent in the real music either: the slow end of a lineage shuffles and
	// the fast end goes straight, which is why the uptempo roll halves the CHANCE of a swing.
	//
	// Tempo is the same kind of value. One shared 130–185 band made a metal song and a country
	// song run at the same speed, which is most of what "they all sound alike" was — so each
	// genre carries its own band and its own uptempo band.
	static readonly GenreProfile[] Profiles =
	{
		// ── ska — THIRD WAVE (ska-punk): straight and fast, clean skank verses into loud choruses ──
		// Genre 0 was tuned as first-wave/rocksteady — shuffled, 130–175, a melodic reggae bass under
		// a roomy bass-forward mix and a form that deliberately did not climb. That is a real music
		// and it is not this one. The waves run ska (late 50s–60s, shuffled, out of American R&B) →
		// rocksteady → reggae, and then 2 Tone (1979, straight, punk-sharpened) → the third wave
		// (90s US ska-punk); the tempo band alone already put this genre in the last of those, so
		// every other value was describing a different era than the tempo was.
		new()
		{
			SwingChance = 0f,   // 2 Tone dropped the shuffle for punk's straight eighths; the third wave never took it back
			// Anchored on a record rather than on an adjective. "Ska is fast" first put this band at
			// 150–190, which made a REFERENCE-FAST ska song the median and left the tempo knob no
			// usable travel upward. Reel Big Fish's "Sell Out" is ~103 bpm as counted, i.e. ~206 in
			// THIS engine's units — the skank fires once per beat here (the "and" of each beat), so
			// the engine counts the double-time reading and every ska tempo has to be converted
			// before it means anything. 206 is a fast ska song, so it belongs at the CEILING; the
			// ordinary band sits well under it and the knob is what reaches up there.
			BpmMin = 138, BpmMax = 172, FastBpmMin = 165, FastBpmMax = 190,
			TempoFloor = 118, TempoCeil = 210,
			ChordBars = 2, RideLean = 0.20f, HornLead = true,
			Endings = new[] { EndingStyle.StopHit, EndingStyle.Ring, EndingStyle.Cadence },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Ska,
			Scales = Harmony.SkaScales, ScaleWeights = Harmony.SkaScaleWeights,
			Progressions = Harmony.SkaProgressions,
			Voicings = Harmony.SkaVoicings, VoicingWeights = Harmony.SkaVoicingWeights,
			BassPatterns = Harmony.SkaBass,
			CompFigures = CompFigure.Ska, Comp = CompStyle.Skank,
			// The dynamic that IS third-wave ska: the skank stops for the chorus and the same voice
			// plays power chords through a driven amp. LoudComp reuses punk's downstroke because the
			// technique genuinely is punk's — what keeps the genre distinct is that it only does this
			// half the time, over ska's harmony, with the horns still on top.
			LoudCompFigures = CompFigure.SkaLoud, LoudComp = CompStyle.Downstroke,
			Grooves = DrumGroove.Ska, GrooveWeights = new[] { 3, 2 },
			Lead = LeadStyle.HornLine, LeadPhraseBars = 2, LeadSilence = 0.15f,
			AccentDown = 0.95f, AccentBack = 1.05f, AccentOff = 1.1f, // the offbeat is still the loud one
			// Dry and bright — a 90s record, not a 60s room. The high trim is a TIMBRE call and was
			// checked not to be a level one: moving it 0.95→1.15 shifts the kit's RMS by 0.02%,
			// because RMS is kick and snare energy and this rides the hats and cymbals on top.
			Mix = new MixProfile( 0.65f, 1f, 0.95f, 1.05f, 1.15f ),
		},
		// ── rock — mid-tempo minor vamps behind a straight backbeat ──
		new()
		{
			// A quarter of rock songs are shuffle-rock and the rest are dead straight. The old
			// 0–0.08 band was neither: it swung every song by an amount described as "a touch of
			// human push", which a swing warp cannot deliver — the warp moves EVERY offbeat eighth
			// late by the SAME amount, which is a groove. Human feel is DrumPush and Expression.
			SwingChance = 0.25f, SwingMin = 0.10f, SwingMax = 0.16f,
			BpmMin = 110, BpmMax = 160, FastBpmMin = 150, FastBpmMax = 175,
			TempoFloor = 88, TempoCeil = 185,
			ChordBars = 2, RideLean = 0.55f,
			Endings = new[] { EndingStyle.Ring, EndingStyle.StopHit, EndingStyle.Cadence },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Rock,
			Scales = Harmony.RockScales, ScaleWeights = Harmony.RockScaleWeights,
			Progressions = Harmony.RockProgressions,
			Voicings = Harmony.RockVoicings, VoicingWeights = Harmony.RockVoicingWeights,
			BassPatterns = Harmony.RockBass,
			CompFigures = CompFigure.Rock, Comp = CompStyle.Riff,
			KeysFigures = CompFigure.RockKeys, Keys = KeysStyle.Stabs,
			Grooves = DrumGroove.Rock, GrooveWeights = new[] { 3, 2 },
			Lead = LeadStyle.Bluesy, LeadPhraseBars = 2, LeadSilence = 0.20f,
			AccentDown = 1.05f, AccentBack = 1.1f, AccentOff = 0.8f,
			Mix = new MixProfile( 1f, 1f, 1f, 1.05f, 1f ),
		},
		// ── country — the slowest band; a light shuffle under the train beat ──
		new()
		{
			// The genre that kept the shuffle. Straight country is real country, so this is not 1.
			SwingChance = 0.45f, SwingMin = 0.10f, SwingMax = 0.18f, ShuffleChance = 0.18f,
			BpmMin = 95, BpmMax = 130, FastBpmMin = 130, FastBpmMax = 150,
			TempoFloor = 78, TempoCeil = 162,
			ChordBars = 2, RideLean = 0.30f,
			Endings = new[] { EndingStyle.Cadence, EndingStyle.Ring, EndingStyle.Fall },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Country,
			Scales = Harmony.CountryScales, ScaleWeights = Harmony.CountryScaleWeights,
			Progressions = Harmony.CountryProgressions,
			Voicings = Harmony.CountryVoicings, VoicingWeights = Harmony.CountryVoicingWeights,
			BassPatterns = Harmony.CountryBass,
			CompFigures = CompFigure.Country, Comp = CompStyle.BoomChick,
			KeysFigures = CompFigure.CountryKeys, Keys = KeysStyle.HonkyTonk,
			Grooves = DrumGroove.Country, GrooveWeights = new[] { 3, 2 },
			Lead = LeadStyle.DoubleStop, LeadPhraseBars = 2, LeadSilence = 0.25f,
			AccentDown = 1f, AccentBack = 1.05f, AccentOff = 1f,    // boom AND chick carry weight
			Mix = new MixProfile( 0.75f, 0.85f, 1f, 1.05f, 0.95f ), // dry and centred
		},
		// ── metal — the widest band: doom-slow through thrash, under a double-kick ──
		new()
		{
			SwingChance = 0f,   // machine-straight; the old 0–0.02 band was ~3 ms and claimed a feel it never had
			BpmMin = 90, BpmMax = 160, FastBpmMin = 160, FastBpmMax = 200,
			TempoFloor = 70, TempoCeil = 210,
			ChordBars = 2, RideLean = 0.65f,
			Endings = new[] { EndingStyle.StopHit, EndingStyle.Ring },
			EndingWeights = new[] { 4, 2 },
			Form = SongForm.Metal,
			Scales = Harmony.MetalScales, ScaleWeights = Harmony.MetalScaleWeights,
			Progressions = Harmony.MetalProgressions,
			Voicings = Harmony.MetalVoicings, VoicingWeights = Harmony.MetalVoicingWeights,
			BassPatterns = Harmony.MetalBass,
			CompFigures = CompFigure.Metal, Comp = CompStyle.Gallop,
			Grooves = DrumGroove.Metal, GrooveWeights = new[] { 3, 2 },
			Lead = LeadStyle.Shred, LeadPhraseBars = 2, LeadSilence = 0.12f,
			RiffBassChance = 0.75f,
			AccentDown = 1f, AccentBack = 1f, AccentOff = 0.95f,    // deliberately flat: it's a wall
			Mix = new MixProfile( 0.45f, 1f, 1.05f, 0.85f, 1.05f ), // dry, mid-scooped
		},
		// ── punk — always hot, and a chord per bar so the four-chord loop IS the hypermeasure ──
		new()
		{
			SwingChance = 0f,   // machine-straight
			BpmMin = 165, BpmMax = 200, FastBpmMin = 165, FastBpmMax = 200, AlwaysFast = true,
			TempoFloor = 130, TempoCeil = 225,
			ChordBars = 1, RideLean = 0.20f,
			Endings = new[] { EndingStyle.StopHit, EndingStyle.Ring },
			EndingWeights = new[] { 5, 1 },
			Form = SongForm.Punk,
			Scales = Harmony.PunkScales, ScaleWeights = Harmony.PunkScaleWeights,
			Progressions = Harmony.PunkProgressions,
			Voicings = Harmony.PunkVoicings, VoicingWeights = Harmony.PunkVoicingWeights,
			BassPatterns = Harmony.PunkBass,
			CompFigures = CompFigure.Punk, Comp = CompStyle.Downstroke,
			Grooves = DrumGroove.Punk, GrooveWeights = new[] { 3, 2 },
			Lead = LeadStyle.Unison, LeadPhraseBars = 2, LeadSilence = 0.65f,
			RiffBassChance = 0.35f,
			AccentDown = 1.1f, AccentBack = 1.15f, AccentOff = 0.9f,
			Mix = new MixProfile( 0.7f, 0.95f, 0.95f, 1.1f, 1f ),
		},
		// ── pop — dance tempo over four-on-the-floor, a chord per bar ──
		new()
		{
			SwingChance = 0f,   // quantised by construction — the grid is the genre
			BpmMin = 100, BpmMax = 128, FastBpmMin = 124, FastBpmMax = 140,
			TempoFloor = 84, TempoCeil = 152,
			ChordBars = 1, RideLean = 0.30f,
			Endings = new[] { EndingStyle.Fall, EndingStyle.Ring, EndingStyle.Cadence },
			EndingWeights = new[] { 3, 2, 1 },
			Form = SongForm.Pop,
			Scales = Harmony.PopScales, ScaleWeights = Harmony.PopScaleWeights,
			Progressions = Harmony.PopProgressions,
			Voicings = Harmony.PopVoicings, VoicingWeights = Harmony.PopVoicingWeights,
			BassPatterns = Harmony.PopBass,
			CompFigures = CompFigure.Pop, Comp = CompStyle.Pad,
			KeysFigures = CompFigure.PopArp, Keys = KeysStyle.Arp,
			Grooves = DrumGroove.Pop, GrooveWeights = new[] { 3, 2 },
			Lead = LeadStyle.Hook, LeadPhraseBars = 2, LeadSilence = 0.20f,
			AccentDown = 1.1f, AccentBack = 1.05f, AccentOff = 0.85f,
			Mix = new MixProfile( 1.1f, 1.2f, 1.1f, 0.95f, 1.15f ),  // wide, bright, loud sub
		},
	};

	public static int Count => Profiles.Length;

	public static GenreProfile For( int genre ) => Profiles[Math.Clamp( genre, 0, Profiles.Length - 1 )];

	/// <summary>This song's swing. Either a draw from the genre's band, or — for the genres that
	/// have one — a genuine 2:1 triplet shuffle, which is a FEEL rather than a wider band.
	/// Both paths consume the same two draws so the choice shifts nothing downstream.</summary>
	/// <param name="fast">Uptempo songs tighten toward the straight end — at speed a wide
	/// shuffle stops reading as a groove and starts reading as a drag. It halves the CHANCE, never
	/// the depth: scaling the depth pushed the shallow end of the band under the threshold where
	/// swing is audible at all, and it did it worst exactly where the eighth was already shortest,
	/// so a fast song's "reduced swing" was a straight song wearing a number. A song either swings
	/// at a depth its genre means or it does not swing.</param>
	public float DrawSwing( Rng rng, bool fast )
	{
		bool shuffle = rng.Chance( ShuffleChance ) && !fast;
		bool swung = rng.Chance( fast ? SwingChance * 0.5f : SwingChance );
		float t = rng.Next();
		if ( shuffle ) return ShuffleMin + t * (ShuffleMax - ShuffleMin);
		return swung ? SwingMin + t * (SwingMax - SwingMin) : 0f;
	}

	/// <summary>This song's tempo, drawn from the genre's band (its uptempo band when
	/// <paramref name="fast"/>) and then scaled by the listener's TEMPO knob. The band is the
	/// genre's; the knob is the preference riding on top, so a song can be pushed or dragged
	/// without a country song ever ending up at metal's tempo.
	///
	/// The knob SATURATES at the genre's own <see cref="TempoFloor"/>/<see cref="TempoCeil"/>
	/// rather than at one shared 40–300. A single symmetric 0.70–1.45 multiplier cannot be right
	/// for six bands at once: its ends were ska 268, metal 290 and country 67, so the ends of the
	/// slider were unplayable everywhere and only its middle was usable.</summary>
	public int DrawBpm( Rng rng, bool fast, float tempoScale )
	{
		int lo = fast ? FastBpmMin : BpmMin, hi = fast ? FastBpmMax : BpmMax;
		int bpm = lo + rng.Int( Math.Max( 1, hi - lo + 1 ) );
		return Math.Clamp( (int)MathF.Round( bpm * tempoScale ), TempoFloor, TempoCeil );
	}

	/// <summary>This song's groove, drawn from the genre's own table — one weighted draw, never
	/// a shared <c>switch</c> default. Rock, country and punk used to fall through to the same
	/// straight backbeat, which is three of six genres playing identical drums.</summary>
	public DrumGroove DrawGroove( Rng rng ) => rng.PickWeighted( Grooves, GrooveWeights );

	/// <summary>This song's lean toward the ride cymbal, spread ±0.25 around the genre's base so
	/// some songs strongly prefer one. Each SECTION then rolls its own hats-or-ride against it.</summary>
	public float DrawRidePref( Rng rng ) => Math.Clamp( RideLean - 0.25f + 0.5f * rng.Next(), 0f, 1f );
}
