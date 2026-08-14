using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>
/// The TUNE — the part of a song a listener could hum back.
///
/// Everything the engine generated before this was accompaniment plus an improvisation: the
/// chordal voices played a rhythm figure, and the lead invented a fresh phrase every two bars.
/// That is a backing track, not a song. Real rock, punk, ska and pop songs are built on a
/// MELODY that recurs — the chorus states the same tune every time it comes round, and that
/// repetition is what makes it a chorus rather than another eight bars.
///
/// A tune is a <see cref="Pattern"/> whose cell values are SCALE DEGREES relative to the key's
/// tonic (not to the current chord), so the line keeps its shape while the harmony moves under
/// it — which is what a melody is. <see cref="MusicGen.RenderTune"/> resolves a degree against
/// the bar's chord on the strong beats, so the tune stays consonant without being re-written
/// chord by chord.
///
/// Because it is a Pattern it inherits everything patterns get: it anchors to the section (so a
/// four-bar tune restarts with the chorus) and it stretches under a half-time feel.
/// </summary>
static class Melody
{
	/// <summary>Cell value for a rest — no onset, the previous note holds.</summary>
	public const int Rest = Harmony.Rest;

	/// <summary>The AMBITUS — the range a whole tune is written in, in SCALE DEGREES from the key's
	/// tonic: from the sixth below it up to the third above the octave. Twelve degrees, about an
	/// octave and a fifth in a major scale, and deliberately lopsided — a melody sits above its
	/// tonic and only dips under it, so a symmetric range would spend half of itself where no tune
	/// goes.
	///
	/// It is an authored bound rather than a measured one: what it is FOR is that a line which
	/// wanders further stops being singable, and singable is what makes the thing a tune. The
	/// number is a judgement about that and nothing more.</summary>
	public const int DegreeMin = -2, DegreeMax = 9;

	/// <summary>How many scale degrees ONE PHRASE may cover — eight, an octave in a major scale,
	/// inside the twelve the whole tune may reach.
	///
	/// A RANGE AND AN AMBITUS ARE TWO DIFFERENT NUMBERS AND ONE CANNOT DO BOTH JOBS. The ambitus is
	/// a whole-song figure — how far the tune goes over all of it — and a tune here is 2–8 bars, so
	/// bounding a single phrase with it was measuring one thing and spending it on another. What a
	/// phrase actually does is orbit a register: it opens somewhere, moves about an octave around
	/// that, and the tune gets its wider reach from the phrases sitting in DIFFERENT places rather
	/// than from any one of them wandering.
	///
	/// So the window is drawn per phrase and anchored on the note the phrase opens on
	/// (<see cref="Opens"/>), which is why the opening degree keeps its weighting instead of being
	/// folded into a window drawn first. Inside a phrase this is the bound the walk reflects off
	/// and the centre <see cref="Centre"/> pulls toward; the ambitus stays the outer wall.
	///
	/// THE WINDOW BOUNDS WHERE A LINE WANDERS, NOT WHERE IT MAY BE PUT. An answer transposes its
	/// call bodily (<see cref="AnswerOp.SequenceUp2"/> takes it up two degrees), and that is a
	/// deliberate move rather than a walk drifting out of register — so <see cref="Answer"/>
	/// reflects off the ambitus. Folding a sequence back into the call's window would flatten the
	/// one gesture in the tune whose whole point is that it goes somewhere else.
	///
	/// Authored like everything else here (there is no melodic corpus in this repo). The published
	/// pop-melody work that gives whole-song ambitus at around two octaves measures range on a
	/// rolling two-bar window for exactly this reason, but its figures are for a different roster
	/// and are not borrowed as a number — this is a judgement about a phrase being one gesture in
	/// one register, and <c>--stats</c> reports what the engine actually does with it.</summary>
	public const int PhraseSpan = 8;

	/// <summary>The note lengths a tune may be written in, in ticks: sixteenth, eighth, dotted
	/// eighth, quarter, dotted quarter, half. <see cref="Timing.TicksPerBeat"/> is 48, so every one
	/// of them is exact and <see cref="Timing"/> needs nothing — the same clean division the
	/// thirty-second work already proved.
	///
	/// The line every genre's weights are split on is the QUARTER: the first three are shorter than
	/// a beat and the last three are a beat or longer, which is what <c>move</c> leans on to make a
	/// verse sparser than its chorus without a second density mechanism.</summary>
	public static readonly int[] Lengths = { 12, 24, 36, 48, 72, 96 };

	/// <summary>Index of the first length that is a beat or longer.</summary>
	const int LongFrom = 3;

	/// <summary>How a phrase answers itself. The answer used to not be DRAWN at all: it was the
	/// call's degrees minus one, every genre, every song, with a forced tonic on the end — so half
	/// of every tune in the engine was a mechanical transform of the other half.</summary>
	public enum AnswerOp
	{
		/// <summary>The call a step lower — the old behaviour, and still the heaviest weight in
		/// most genres because it is genuinely the commonest answer in this music.</summary>
		Transpose,
		/// <summary>The same line with a different landing: identical degrees, and the last two
		/// re-drawn to step home. What a chorus does.</summary>
		NewTail,
		/// <summary>The call restated a degree higher — a question answered with a bigger question,
		/// which still resolves because the last note is the tonic either way.</summary>
		SequenceUp,
		/// <summary>Restated two degrees higher.</summary>
		SequenceUp2,
		/// <summary>Mirrored about the call's first degree: where the call rose, the answer falls.
		/// </summary>
		Invert,
	}

	public static readonly AnswerOp[] Answers =
	{
		AnswerOp.Transpose, AnswerOp.NewTail, AnswerOp.SequenceUp, AnswerOp.SequenceUp2, AnswerOp.Invert,
	};

	/// <summary>No operator to avoid — what an antecedent passes, having nothing before it.</summary>
	const AnswerOp NoOp = (AnswerOp)(-1);

	/// <summary>How the CONSEQUENT opens — the one decision that makes a period a period.
	///
	/// A period is two call/answer pairs: an antecedent that leaves the line open and a consequent
	/// that closes it. Both pairs are built by exactly the machinery below; what varies is how much
	/// of the antecedent's call the consequent's call keeps. That is the classical taxonomy and it
	/// is also the whole variation budget — the answers repeat their own call's rhythm either way,
	/// so if the consequent's call does not move, nothing in the tune's second half is new.</summary>
	public enum PeriodShape
	{
		/// <summary>PARALLEL — the consequent restates the call note for note and differs only in
		/// how it answers. The commonest period in this music, and the one that makes the tune
		/// unmistakably one tune; it is also the least new material, which is why it is not the
		/// only shape.</summary>
		Parallel,
		/// <summary>VARIED — the consequent keeps the call's rhythm and sings a fresh contour over
		/// it. The rhythm is what a listener remembers, so this reads as the same phrase said again
		/// differently rather than as a second idea.</summary>
		Varied,
		/// <summary>CONTRASTING — the consequent opens with a phrase of its own, rhythm and all.
		/// The departure, and the only shape that puts a second rhythm in the tune.</summary>
		Contrasting,
	}

	public static readonly PeriodShape[] Shapes =
	{
		PeriodShape.Parallel, PeriodShape.Varied, PeriodShape.Contrasting,
	};

	/// <summary>Authored, not measured — there is no melodic corpus in this repo (see the note on
	/// <c>GenreProfile.Tune</c>) and this is a judgement about how much a tune may move under
	/// itself. It is one table rather than six because nothing found says a genre has an opinion
	/// about it; a genre that turns out to want one puts weights in <see cref="TuneVocab"/>, the
	/// way <see cref="Answers"/> already does.</summary>
	static readonly int[] ShapeWeights = { 4, 3, 3 };

	/// <summary>The same judgement for a tune whose phrases are at <see cref="MinPhraseBars"/>.
	///
	/// A NEW RHYTHM ENTERS A TUNE AT THE CONSEQUENT'S CALL OR NOWHERE, and only
	/// <see cref="PeriodShape.Contrasting"/> brings one — so at the floor phrase length the other two
	/// shapes hand the most exposed voice in the mix ONE two-bar rhythm to sing four times, which is
	/// the shape the period exists to be bigger than, arriving one level up. It lands hardest on the
	/// genres whose harmonic cycle is shortest (pop and punk: <c>ChordBars</c> 1 × a four-chord loop
	/// puts every phrase at the floor), i.e. the two the period work was written for.
	///
	/// A lean rather than a rule: a parallel period is a real and common shape and stays drawable at
	/// two bars, it just stops being the likeliest one there. Longer phrases keep the table above,
	/// because a four-bar phrase restated is a tune with room to be recognised rather than a loop.
	/// </summary>
	static readonly int[] ShortShapeWeights = { 2, 3, 5 };

	/// <summary>Where an ANTECEDENT lands: a chord tone that is not the tonic, which is what leaves
	/// the line open. The fifth is the half cadence proper and takes most of the weight; the third
	/// is the softer one. Landing home here would close the tune half way through it and make the
	/// consequent an appendix rather than an answer.</summary>
	static readonly int[] HalfCadence = { 4, 2 };
	static readonly int[] HalfCadenceWeights = { 3, 2 };

	/// <summary>The fewest bars a phrase may be. A period is four phrases, so a tune shorter than
	/// four of these is two phrases and no period — a one-bar "phrase" is a fragment, and four of
	/// them is a tune that restates itself every bar, which is the defect this exists to fix
	/// arriving from the other direction.</summary>
	public const int MinPhraseBars = 2;

	/// <summary>How many phrases a tune of <paramref name="bars"/> bars is written in: four (a
	/// period) where they are long enough to be phrases, two (a plain call and answer) otherwise.
	/// </summary>
	public static int PhraseCount( int bars ) => bars >= 4 * MinPhraseBars ? 4 : 2;

	/// <summary>Where a tune may open — chord tones only, weighted toward the tonic and the fifth,
	/// with the octave reachable.</summary>
	static readonly int[] Opens = { 0, 2, 4, 7 };
	static readonly int[] OpenWeights = { 5, 3, 4, 2 };

	/// <summary>How far the phrase leans uphill at its start and downhill at its end — the MELODIC
	/// ARCH. Phrases in this music (and in every corpus anyone has counted) rise and then fall on
	/// average, and a plain random walk does not: it wanders, and the only thing that ever brought
	/// it home was the forced tonic on the last note, which is a landing with no approach to it.
	///
	/// 0 would be the old coin toss; 0.5 would make direction deterministic and turn every tune
	/// into the same hill. This is a lean on a draw, not a shape imposed on one.</summary>
	const float Arch = 0.25f;

	/// <summary>How hard the line is pulled back toward the middle of its PHRASE WINDOW —
	/// TESSITURA, the fact that a melody orbits a central pitch rather than diffusing across
	/// everything it is allowed to sing.
	///
	/// It is what actually keeps a tune off the range ends. <see cref="Reflect"/> is a BACKSTOP: it
	/// stops a line parking at a boundary, but a walk with no centre still spends its time out
	/// there, and the arch makes that worse in the first half of every phrase by leaning uphill
	/// whatever the register already is. The two are different jobs and both are needed — this
	/// decides where the line lives, reflection decides what happens when it arrives at an edge
	/// anyway. The centre it pulls toward is the PHRASE's (<see cref="PhraseSpan"/>), so a phrase
	/// orbits its own register rather than the middle of everything the tune may reach.</summary>
	const float Centre = 0.30f;

	/// <summary>
	/// Draw a tune: <paramref name="bars"/> bars built as a PERIOD where they are long enough for
	/// one, and as a plain call and answer where they are not.
	///
	/// A call and answer is a pair of phrases — the first states a shape and leaves it open, the
	/// second repeats that rhythm and resolves it home. That symmetry is most of what makes a line
	/// sound composed rather than generated, and a fresh random phrase every two bars never sounds
	/// like a tune however good the notes are.
	///
	/// A PERIOD IS TWO OF THOSE PAIRS AND SITS ABOVE THEM, NOT INSTEAD OF THEM. The antecedent
	/// (call, answer) lands on a chord tone that is not the tonic and so leaves the line open; the
	/// consequent (call, answer) opens from the antecedent — restating it, varying it, or departing
	/// from it (<see cref="PeriodShape"/>) — and resolves home. That is what puts repetition at the
	/// whole tune's length and variation at a phrase's, instead of the binary shape the tune had
	/// before this: two phrases, one rhythm between them, looped to fill the section and repeated
	/// identically at every chorus.
	///
	/// THE RHYTHM REPEAT STAYS, WITHIN A PAIR. Varying an answer's rhythm stops its two phrases
	/// being heard as a question and an answer at all; the only rhythmic freedom an answer gets is
	/// where its last notes land, and that arrives through <see cref="AnswerOp.NewTail"/> rather
	/// than through a second rhythm draw. A new rhythm enters a tune at the CONSEQUENT'S CALL or
	/// nowhere. The 100%-tonic ending stays too — that is not a defect to be varied away, it is
	/// what makes the thing a tune.
	/// </summary>
	/// <param name="v">The genre's vocabulary — the note lengths it sings in, how often it rests,
	/// how often it leaps, and how it answers itself.</param>
	/// <param name="move">How much this line moves relative to the genre's own table: 1 for a
	/// chorus, less for the sparser verse tune. It leans the length draw toward the long end rather
	/// than being a second density knob sitting beside the weights.</param>
	/// <param name="swung">True where the song swings or shuffles. THE SIXTEENTH COMES OUT OF THE
	/// MENU: under a shuffle the beat's own subdivision IS the triplet, and Timing's warp puts a
	/// straight sixteenth at a third of the beat while the band's eighth-based figures sit on the
	/// beat and at two thirds. That is not syncopation, it is two grids at once, and it reads as
	/// the lead pushing against a band it does not line up with. A shuffled genre's melody moves in
	/// eighths and the shuffle does the subdividing.</param>
	public static Pattern Draw( Rng rng, int bars, int barTicks, in TuneVocab v, float move = 1f,
		bool swung = false )
	{
		int phrases = PhraseCount( bars );
		int phraseTicks = barTicks * Math.Max( 1, bars / phrases );
		var ticks = new List<int>();
		var degrees = new List<int>();

		// The genre's length table, leaned toward the long end for a verse. At move = 1 both
		// factors are 1 and the table is the genre's verbatim.
		var weights = new int[Lengths.Length];
		for ( int i = 0; i < weights.Length; i++ )
		{
			float w = v.LengthWeights[i] * (i < LongFrom ? move : 2f - move);
			// Anything that does not divide the eighth: the sixteenth AND the dotted eighth, which
			// lands mid-eighth for the same reason and was the half of this that was easy to miss.
			if ( swung && Lengths[i] % Timing.TicksPerEighth != 0 ) w = 0f;
			weights[i] = Math.Max( 0, (int)MathF.Round( w * 8f ) );
		}

		// ── the antecedent ──
		var callRhythm = DrawRhythm( rng, phraseTicks, weights, v );
		var callDegrees = DrawContour( rng, callRhythm.Count, v );
		Emit( ticks, degrees, 0, callRhythm, callDegrees );

		// A HALF CADENCE IS WHAT MAKES THE CONSEQUENT NECESSARY. With a period the antecedent lands
		// on a chord tone that is not the tonic and stays open; with only two phrases there is
		// nothing after it, so it resolves the way it always did.
		int open = phrases == 4 ? HalfCadence[rng.WeightedIndex( HalfCadenceWeights )] : 0;
		Emit( ticks, degrees, phraseTicks,
			callRhythm, Answer( rng, callDegrees, v, open, NoOp, out var anteOp ) );

		if ( phrases == 4 )
		{
			var shape = rng.PickWeighted( Shapes,
				phraseTicks <= MinPhraseBars * barTicks ? ShortShapeWeights : ShapeWeights );
			// BOTH DRAWN FOR EVERY SHAPE, so swapping one shape for another does not shift the rest
			// of the tune's stream — the discipline PickOrNull keeps in the composer. A parallel
			// consequent pays for a phrase it does not sing.
			var freshRhythm = DrawRhythm( rng, phraseTicks, weights, v );
			var conRhythm = shape == PeriodShape.Contrasting ? freshRhythm : callRhythm;
			var freshDegrees = DrawContour( rng, conRhythm.Count, v );
			var conDegrees = shape == PeriodShape.Parallel ? callDegrees : freshDegrees;

			Emit( ticks, degrees, 2 * phraseTicks, conRhythm, conDegrees );
			// The consequent answers with its own operator — that is what a parallel period varies,
			// and it is the only thing it varies, so it must ACTUALLY vary: drawn without replacement
			// against the antecedent's, because the same operator over the same call is the same
			// phrase but for its last note, and a period whose second half is its first half with a
			// different landing is a two-phrase tune wearing four.
			Emit( ticks, degrees, 3 * phraseTicks,
				conRhythm, Answer( rng, conDegrees, v, 0, anteOp, out _ ) );
		}

		// A held final note, so the tune breathes before it comes round again.
		return new Pattern( bars * barTicks, ticks.ToArray(), degrees.ToArray() );
	}

	/// <summary>Append one phrase's onsets (offset to <paramref name="at"/>) and its degrees.
	/// </summary>
	static void Emit( List<int> ticks, List<int> degrees, int at, List<int> rhythm, List<int> pitches )
	{
		for ( int i = 0; i < rhythm.Count; i++ ) { ticks.Add( at + rhythm[i] ); degrees.Add( pitches[i] ); }
	}

	/// <summary>One phrase's RHYTHM — the onsets, in ticks from the phrase's own start.
	///
	/// Rhythm first, and separately from the pitches: a melody's rhythm is what gets remembered,
	/// and drawing it on its own is what lets an answer repeat it exactly.
	///
	/// A REST IS AN OMITTED ONSET, not a cell. Leaving the tick out means the previous note's
	/// SpanTicks simply grows to cover the gap, and RenderTune's two-beat length cap turns the
	/// remainder into real silence — so rests cost the renderer nothing. A Melody.Rest CELL
	/// would be read as a DEGREE by RenderTune, which has no rest arm, and sung.</summary>
	static List<int> DrawRhythm( Rng rng, int phraseTicks, int[] weights, in TuneVocab v )
	{
		var rhythm = new List<int>();
		for ( int t = 0; t < phraseTicks; )
		{
			// THE PHRASE RE-ANCHORS TO THE BEAT, and without this the widened vocabulary is worse
			// than the two lengths it replaced. Free-running the accumulator over the menu means a
			// dotted eighth or a sixteenth shifts EVERY REMAINING NOTE of the phrase by a non-beat
			// amount, permanently — the line rotates against the bar and never comes back, which is
			// a 3-against-4 running for eight bars rather than a melody. It reads as the lead being
			// out of time with the band, because it is.
			//
			// The rule is the one a player reads off a stave: inside a beat you may only play what
			// fits the rest of it. So a dotted eighth is followed by a sixteenth, a sixteenth by
			// whatever fills the remaining three, and the next beat starts on the beat. Notes still
			// land on the "and" and on sixteenths — what they cannot do is drift.
			int inBeat = t % Timing.TicksPerBeat;
			int len;
			if ( inBeat == 0 ) len = Lengths[rng.WeightedIndex( weights )];
			else
			{
				int room = Timing.TicksPerBeat - inBeat;
				var fits = new int[Lengths.Length];
				bool any = false;
				for ( int i = 0; i < Lengths.Length; i++ )
					if ( Lengths[i] <= room ) { fits[i] = weights[i]; any |= weights[i] > 0; }
				len = any ? Lengths[rng.WeightedIndex( fits )] : room;
			}
			// Never open the phrase on silence: a tune that starts by not being there has no shape
			// for the answer to repeat.
			if ( rhythm.Count == 0 || !rng.Chance( v.Rest ) ) rhythm.Add( t );
			t += len;
		}
		// A call of one note is not a call. Only reachable when every cell after the first drew a
		// rest, which is rare and still worth not shipping.
		if ( rhythm.Count < 2 ) rhythm.Add( phraseTicks / 2 );
		return rhythm;
	}

	/// <summary>One phrase's CONTOUR — <paramref name="notes"/> degrees relative to the key's tonic.
	///
	/// Three things shape it and none of them is a random walk: the arch (see <see cref="Arch"/>),
	/// post-skip reversal (below), and reflection off the range ends instead of a clamp.
	///
	/// A phrase opens on a CHORD TONE — a melody that opens on the second or the seventh is a
	/// melody that starts by needing to resolve — weighted toward the tonic and the fifth, where
	/// far more tunes actually start, with the octave reachable. A uniform draw over three values
	/// is the sort of thing that shows up in a sweep as 33/33/33 and in a listen as "they all start
	/// the same way".</summary>
	static List<int> DrawContour( Rng rng, int notes, in TuneVocab v )
	{
		var degrees = new List<int>( notes );
		int degree = Opens[rng.WeightedIndex( OpenWeights )];
		// THE PHRASE'S OWN WINDOW, drawn around the note the phrase opens on so that the opening
		// degree keeps its weighting and cannot land outside its own register. Where the window may
		// sit is what gives the tune its wider reach: two phrases an octave apart cover the ambitus
		// between them without either of them wandering.
		int loMin = Math.Max( DegreeMin, degree - (PhraseSpan - 1) );
		int loMax = Math.Min( degree, DegreeMax - (PhraseSpan - 1) );
		if ( loMax < loMin ) loMax = loMin;
		int lo = Math.Min( loMin + rng.Int( loMax - loMin + 1 ), DegreeMax );
		int hi = Math.Min( lo + PhraseSpan - 1, DegreeMax );
		int owed = 0;
		for ( int i = 0; i < notes; i++ )
		{
			degrees.Add( degree );

			bool leap = rng.Next() < v.Leap;
			// A leap is a third, a fourth or a fifth. It used to be a third and nothing else, in
			// every genre and every song — "a leap" was one interval wearing a general name.
			int size = leap ? 2 + rng.Int( 3 ) : 1;
			int sign;
			if ( owed != 0 )
			{
				// POST-SKIP REVERSAL: a melody that jumps comes back. It is one of the most robust
				// findings there is about how tunes are actually written, and it is also what makes
				// a leap read as a gesture rather than as the line relocating.
				sign = owed;
				owed = 0;
			}
			else
			{
				float u = notes < 2 ? 0.5f : i / (float)(notes - 1);
				// Where in the PHRASE'S window this note sits, −1 at the bottom and +1 at the top.
				float mid = (lo + hi) / 2f, half = Math.Max( 1f, (hi - lo) / 2f );
				float pos = (degree - mid) / half;
				sign = rng.Chance( Math.Clamp( 0.5f + Arch * (1f - 2f * u) - Centre * pos, 0.05f, 0.95f ) )
					? 1 : -1;
			}
			if ( leap ) owed = -sign;
			degree = Reflect( degree + sign * size, lo, hi );
		}
		return degrees;
	}

	/// <summary>ANSWER a call: the same rhythm, the call's degrees put through one of the genre's
	/// <see cref="AnswerOp"/>s, landing on <paramref name="last"/> — the tonic where this phrase
	/// closes the tune, an open chord tone where it is an antecedent handing over to a consequent.
	/// Every operator answers the same question and every one of them lands in the same place.
	/// </summary>
	/// <param name="avoid">An operator this phrase may not take — the one the phrase before it took,
	/// or <see cref="NoOp"/> where there is nothing before it.</param>
	/// <param name="used">The operator drawn, for the next phrase to avoid.</param>
	static List<int> Answer( Rng rng, List<int> call, in TuneVocab v, int last, AnswerOp avoid,
		out AnswerOp used )
	{
		// WITHOUT REPLACEMENT, by zeroing a weight rather than by re-drawing: one draw either way,
		// so which operator the phrase before it took cannot shift the rest of the tune's stream.
		// A genre that weights a single operator keeps it — an answer it would never write is worse
		// than an answer heard twice.
		var weights = v.AnswerWeights;
		int i0 = Array.IndexOf( Answers, avoid );
		if ( i0 >= 0 && weights != null && weights.Length == Answers.Length )
		{
			int rest = 0;
			for ( int i = 0; i < weights.Length; i++ ) if ( i != i0 ) rest += Math.Max( 0, weights[i] );
			if ( rest > 0 )
			{
				var w = new int[weights.Length];
				Array.Copy( weights, w, weights.Length );
				w[i0] = 0;
				weights = w;
			}
		}
		var op = used = rng.PickWeighted( Answers, weights );
		// Drawn for every operator, so swapping one for another does not shift the rest of the
		// tune's stream — the same discipline PickOrNull keeps in the composer.
		int approach = rng.Chance( 0.65f ) ? 1 : -1;
		int n = call.Count;
		int first = call[0];
		var answer = new List<int>( n );
		for ( int i = 0; i < n; i++ )
		{
			if ( i == n - 1 ) { answer.Add( Reflect( last ) ); continue; }
			int d = op switch
			{
				AnswerOp.NewTail => i == n - 2 ? last + approach : call[i],
				AnswerOp.SequenceUp => call[i] + 1,
				AnswerOp.SequenceUp2 => call[i] + 2,
				AnswerOp.Invert => 2 * first - call[i],
				_ => call[i] - 1,
			};
			answer.Add( Reflect( d ) );
		}
		return answer;
	}

	/// <summary>Fold a degree back inside the singable range by REFLECTING off its ends.
	///
	/// A clamp is sticky: a line that reaches a boundary and keeps stepping outward parks there,
	/// which is where 10–18% of every tune's notes sat and where most of its repeated adjacent
	/// notes came from — the two are the same defect seen from either side. Reflection keeps the
	/// range (that is what makes a tune singable) and turns the wall into a turn.
	///
	/// Off the AMBITUS — the outer wall, which is what a transposed answer folds against.</summary>
	internal static int Reflect( int degree ) => Reflect( degree, DegreeMin, DegreeMax );

	/// <summary>Fold a degree back inside an arbitrary window by reflecting off its ends — the
	/// walk inside one phrase uses its own (<see cref="PhraseSpan"/>).</summary>
	internal static int Reflect( int degree, int lo, int hi )
	{
		for ( int guard = 0; guard < 8 && (degree < lo || degree > hi); guard++ )
		{
			if ( degree < lo ) degree = 2 * lo - degree;
			if ( degree > hi ) degree = 2 * hi - degree;
		}
		return Math.Clamp( degree, lo, hi );
	}
}

// The tune, and how a bar of it is played. Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// The song's tunes, drawn once per song off their own streams (so having them shifts nothing
	// else in the composition) and keyed by SECTION TYPE. The chorus tune is the hook: identical
	// every chorus, which is the whole reason a chorus reads as one. The verse tune is a second,
	// sparser line — same song, different words.
	Pattern _chorusTune, _verseTune;

	// How long one PHRASE of them is. A diagnostic wanting to read a tune phrase by phrase cannot
	// re-derive this without re-deciding the period, which is the re-implementation PlanTrace
	// exists to avoid — so the composer writes it down.
	int _tunePhraseTicks;

	/// <summary>One phrase of the song's tunes, in ticks (diagnostics — see <see cref="Melody"/>).
	/// </summary>
	internal int TunePhraseTicks => _tunePhraseTicks;

	/// <summary>The tune this section sings, or null where the section is not a place for one:
	/// a solo is where the genre's lead grammar improvises, an intro is a build-in, and the
	/// ending has already resolved.</summary>
	Pattern TuneFor( Section s ) => !SectionSingsTune( s ) ? null
		: s == Section.Chorus ? _chorusTune : _verseTune;

	/// <summary>Whether a section TYPE is a place for a tune at all. Static because it is a
	/// property of the form rather than of a drawn song — which is what lets a form be checked for
	/// putting its feel changes somewhere the melody can contrast with them.</summary>
	internal static bool SectionSingsTune( Section s ) =>
		s is Section.Chorus or Section.Verse or Section.PreChorus or Section.Bridge;

	/// <summary>Draw the song's tunes — one for choruses, a sparser one for verses. Every genre
	/// gets both: "riff-led" does not mean melody-free, and metal verses with no tune left four
	/// and eight bar holes where the lead simply did not play.</summary>
	void DrawTunes( int barTicks, bool swung )
	{
		// The vocabulary is the GENRE's (GenreProfile.Tune). It used to be a switch on _prof.Lead
		// right here, which is the `if ( _genre == … )` smell one level removed: two genres sharing
		// a LeadStyle got the same tune vocabulary, and where their densities matched the draws
		// agreed and the tunes came back identical.
		// The tune is a WHOLE NUMBER OF HARMONIC CYCLES — the bars it takes the progression to come
		// round (ChordBars x the progression's length), capped at eight. A four-bar tune over an
		// eight-bar cycle states itself twice, and the second statement lands over different
		// chords than it was written against: same notes, different harmony, which is exactly the
		// "the lead clashes with the backing" it sounds like. Matching the cycle means every
		// repetition sits over the changes it was drawn for.
		int cycle = Math.Clamp( _chordBars * _prog.Length, 2, 8 );
		// A PERIOD NEEDS FOUR PHRASES, AND A WHOLE NUMBER OF CYCLES IS STILL ALIGNED TO THE CHANGES.
		// The clamp above is not about length, it is about a tune's statements landing over the
		// chords they were drawn against — and a tune of exactly two cycles does, bar for bar. So a
		// genre whose cycle is short doubles the tune rather than being stuck with two phrases: punk
		// and pop (ChordBars 1 x a four-chord progression) went from a 4-bar tune whose rhythmic cell
		// was 2 bars, stated twice and looped, to an 8-bar period. Eight is the ceiling because a
		// section is eight bars — a tune longer than the section it is sung in never finishes.
		int bars = cycle;
		while ( bars * 2 <= 8 ) bars *= 2;
		_tunePhraseTicks = barTicks * bars / Melody.PhraseCount( bars );
		// THE GENRE IS IN THE TUNE'S STREAM, and it is the only stream it is in.
		//
		// Without it the genre reached Draw through nothing but `density` and `leap`, so where two
		// genres' densities were close the draws mostly agreed and the tunes came back
		// BYTE-IDENTICAL: over 500 songs, rock and country sang the same melody 53% of the time and
		// punk and pop 52%. Same n, different key, different kit, literally the same tune — which is
		// most of why the roster read as one band.
		//
		// The SONG stream (ComposePlan's `new Rng( _tag )`) still has no genre in it, and that is a
		// feature rather than an oversight: genre 0 and genre 3 at the same tag:n share the root
		// note, the pan, the ride preference and the whole kit draw, so the same song in two genres
		// is a thing the toy can do. That is worth more than the variation putting the genre there
		// would buy, and it is why the genre goes in the TUNE streams and nowhere else.
		//
		// IT IS A DIFFERENT DRAW, NOT A GUARANTEED DIFFERENT TUNE, and that distinction is the
		// point. Two genres landing on a similar melody at one seed is the toy doing what it is
		// for; what was wrong before was that they landed there RELIABLY, off a stream that could
		// not tell them apart. Nothing here should ever grow into machinery that forces two genres
		// to diverge — the collision rate is something `--stats` reports, not something the engine
		// enforces.
		_chorusTune = Melody.Draw( new Rng( $"{_tag}:tune:{_genre}:chorus" ), bars, barTicks, _prof.Tune, 1f, swung );
		// The verse tune is the same vocabulary, sung with fewer notes in it — same song,
		// different words.
		_verseTune = Melody.Draw( new Rng( $"{_tag}:tune:{_genre}:verse" ), bars, barTicks, _prof.Tune, 0.8f, swung );
	}

	/// <summary>Play one bar of the section's tune.
	///
	/// Degrees are relative to the KEY, so the tune keeps its shape as the chords move. What
	/// keeps it consonant is resolution on the strong beats only: a note landing on a beat is
	/// pulled to the nearest tone of the bar's chord, while the notes between beats are free to
	/// pass through. Snapping everything would rewrite the tune chord by chord — which is
	/// exactly the "no tune, just an improvisation over the changes" this replaces.</summary>
	void RenderTune( Pattern tune, int barTick, int barTicks, int chord, Rng rng, Rng exprRng )
	{
		int melBase = LeadBase();
		var tones = ChordDegrees( chord );
		bool guitarLead = !_hornLead;
		float amp = (guitarLead ? _c.LeadGtrVol * _c.LeadGtrBalance : _c.MelodyVol * _c.MelodyBalance)
			* _midMul;
		float drive = guitarLead ? _c.LeadGtrDrive : _c.MelodyDrive;
		var ex = guitarLead ? Expr( "LEAD GTR" ) : Expr( "LEAD" );
		int prevMidi = NoPrev;

		// A SECTION SHORTER THAN THE TUNE SINGS THE TUNE'S END, not its beginning. A four-bar
		// pre-chorus over an eight-bar tune stated the call and was cut off by the chorus before
		// the answer ever arrived — a phrase interrupted by the next phrase, which is what "two
		// ideas at once" sounds like. Pulling the anchor back lands the tune's resolution exactly
		// on the section's last bar, which is what a pre-chorus is for.
		int anchor = _sectionTicks > 0 && _sectionTicks < tune.LengthTicks
			? _sectionTick - (tune.LengthTicks - _sectionTicks)
			: _sectionTick;
		// THE TUNE IS EXEMPT FROM THE SECTION'S FEEL, and that exemption IS half/double time.
		// Part.Feel is the RHYTHM SECTION's pattern rate: when a section halves or doubles, the
		// band changes rate underneath a vocal that stays exactly where it was — that contrast is
		// the entire gesture, and it is what makes a double-time chorus lift rather than sound
		// like the tape sped up. Scaling the hook by the same multiplier deletes the gesture and
		// leaves only a faster song. So the tune slices at the nominal rate; every other voice
		// (comp, keys, bass, horns, kit) reads _feel.
		var sung = tune.Slice( barTick, barTick + barTicks, anchor );
		Trace?.Add( TraceVoice.Tune, sung );
		foreach ( var h in sung )
		{
			int degree = h.Value;
			int len = Math.Min( h.SpanTicks, Timing.TicksPerBeat * 2 );
			bool onBeat = (h.Tick - _barTick) % Timing.TicksPerBeat == 0;

			// What resolves is the note the ear has TIME to hear against the chord: anything on a
			// beat, and anything held for a beat or more. A quick note between beats is a passing
			// tone and is left alone — that is the difference between a melody and an arpeggio.
			// (Snapping only the on-beat notes left long off-beat non-chord tones ringing over the
			// backing for up to two beats, which is what a clash sounds like.)
			bool resolve = onBeat || len >= Timing.TicksPerBeat;
			if ( resolve ) degree = NearestChordTone( tones, degree );
			int midi = ScaleMidi( melBase, degree );
			// The degree snap chose WHICH chord tone; this puts the note on the pitch the chord
			// actually sounds, which is not the same thing on every degree (see NearestSoundingTone).
			if ( resolve ) midi = NearestSoundingTone( midi, chord, h.Tick );
			// Where this note sits in the TUNE, which is the phrase a bend leans into. The tune's
			// own length is the cycle, so this is the same 0..1 whatever bar the section is on.
			float pu = ((h.Tick - anchor) % tune.LengthTicks + tune.LengthTicks)
					% tune.LengthTicks / (float)tune.LengthTicks;
			var vc = Roll( ex, midi, prevMidi, exprRng, (float)_time.SpanSeconds( h.Tick, len ),
					BendBias( len, pu ) );
			prevMidi = midi;
			RenderLeadNote( _time.TickToSample( h.Tick ), _time.SpanSamples( h.Tick, len * 0.92 ),
				midi, amp * NoteGain( h.Vel ), _time.SpanSeconds( h.Tick, len ) * 0.8,
				drive, vc );

			// The genre's own hand on the same tune: country punctuates it with double-stops, metal
			// runs between its notes. The line is the same either way — this is ORNAMENT, not a
			// different melody, which is the difference between a genre playing a song and a genre
			// having its own song. Ornament also means occasional: harmonising every long note in
			// parallel thirds replaces the melody with a two-note chord (see EmitDoubleStop).
			if ( _prof.Lead == LeadStyle.DoubleStop && len >= Timing.TicksPerEighth * 2
				&& rng.Chance( DoubleStopChance ) )
				EmitDoubleStop( h.Tick, len, degree, amp * NoteGain( h.Vel ) );
			else if ( _prof.Lead == LeadStyle.Shred && len >= Timing.TicksPerBeat && rng.Chance( 0.18f ) )
				for ( int k = 1; k <= 3; k++ )
				{
					int m2 = ScaleMidi( melBase, degree + k );
					RenderLeadNote( _time.EvenSpan( h.Tick + len / 2, len / 2, (k - 1) / 3.0 ),
						_time.SpanSamples( h.Tick, len / 8.0 ), m2, amp * 0.8f * NoteGain( h.Vel ),
						_time.SpanSeconds( h.Tick, len / 8.0 ) * 0.8, drive, vc );
				}
		}
	}
}
