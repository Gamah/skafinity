using System;
using System.Collections.Generic;

namespace Skafinity;

// Per-note pitch shaping — vibrato, bend-in, glide, scoop. An Expression is the per-
// instrument propensity; a Voicing is one roll off it, baked onto a Patch at emit time.
//
// Part of the MusicGen engine — see MusicGen.cs.

// ── Instrument expression ──
// Four expressive PROPERTIES every pitched voice can lean on (drums are excluded). Each
// instrument gets a genre-specific PROPENSITY for each, "based on what it is" — a brass
// lead sings and scoops, a bass slides, a power-chord guitar stays dead straight. The
// realization is the per-note pitch shaping in RenderEvent (vibrato depth + bend envelope).
//   Vib    — #1 vibrato depth (a constant lean, no per-note roll)
//   BendIn — #2 bend up INTO the note from a step below (per-note chance)
//   Glide  — #3 portamento from the previous note's pitch (per-note chance)
//   Scoop  — #4 bend up-and-back within the note (per-note chance)
//   Bend   — #5 THE BEND: up to a target part way through the note, held or released
//
// The first four are APPROACH gestures — a singer or a horn easing onto a pitch — and they are
// right for what they are. None of them is the thing a listener points at and calls a bend, which
// happens in the MIDDLE of a note and goes somewhere: a semitone or a whole step pushed up and
// held. That is #5, and it carries its own DEPTH because depth is a property of the instrument
// and the style, not a constant: a pedal-steel-inflected country lead bends a whole step, a shred
// line bends a semitone on the way past. Propensity alone could not say "bends deep" from "bends
// barely", so a genre could not have an opinion about it.
readonly struct Expression
{
	public readonly float Vib, BendIn, Glide, Scoop, Bend;

	/// <summary>How far this instrument bends, in SEMITONES — 1 is the semitone bend, 2 the whole
	/// step. Only <see cref="Bend"/> reads it; the approach gestures keep their own small fixed
	/// leans, because "eased onto from a fifth of a semitone below" is what an approach IS.</summary>
	public readonly float BendDepth;

	public Expression( float vib, float bendIn, float glide, float scoop,
		float bend = 0f, float bendDepth = 0f )
	{
		Vib = vib; BendIn = bendIn; Glide = glide; Scoop = scoop;
		Bend = bend; BendDepth = bendDepth;
	}
}

// A rolled-per-note voicing: the concrete pitch-shaping a note will get. Vibrato is a
// constant depth (no draw); bend-in/glide/scoop are rolled against their propensities, so
// only voices that lean on them ever pull from the expression stream.
struct Voicing
{
	public float VibDepth, BendSemis, BendTime, ScoopSemis;
	public float BendUpSemis, BendUpStart, BendUpTime, BendUpHold;
}

public sealed partial class MusicGen
{

	const int NoPrev = int.MinValue; // "no previous note" sentinel for glide

	// The per-instrument propensity table — genre-aware. Leads route here by genre
	// ("LEAD" = ska brass, "LEAD GTR" = rock guitar). Rock lead's BENDINESS knob drives its
	// bend-in + scoop directly (that's what "bendiness" is). Tune these by ear.
	Expression Expr( string voice )
	{
		switch ( voice )
		{
			case "BASS":       return _genre switch
			{
				1 => new Expression( 0f, 0f, 0.10f, 0f ),     // rock: locked
				2 => new Expression( 0f, 0f, 0.12f, 0.03f ),  // country: a subtle slide
				3 => default,                                 // metal: dead straight, fast
				4 => default,                                 // punk: dead straight, fast
				5 => default,                                 // pop: tight synth bass, no slide
				_ => new Expression( 0f, 0f, 0.25f, 0.05f ),  // reggae bass slides
			};
			case "SKANK":      return default;                          // staccato chops — dead straight
			case "ORGAN":      return new Expression( 0.15f, 0f, 0f, 0f ); // gentle bubble vibrato (only blooms on held notes)
			case "LEAD":       return new Expression( 0.35f, 0.15f, 0.10f, 0.25f ); // brass sings + scoops
			case "HORNS":      return new Expression( 0.20f, 0f, 0f, 0.20f ); // section stabs fall/scoop
			case "KEYS":       return default;                          // organ comp — locked, no wobble
			case "RHYTHM GTR": return default;                          // power chords — straight
			case "LEAD GTR":
			{
				float knob = _c.LeadGtrBend;
				// COUNTRY BENDS UP INTO THE NOTE; IT DOES NOT SCOOP EVERY NOTE. Both propensities
				// used to come off one floored value, so raising the twang raised the wobble with
				// it and half the line arrived off-pitch at the front and humped in the middle. A
				// bend up into the note is the telecaster gesture; a scoop inside it is a horn's,
				// and country's lead is not a horn. So the floor stays on BendIn alone, the scoop
				// falls back to whatever the listener's knob says, and the twang the floor was
				// reaching for is carried by the real bend instead — a whole step, which is what a
				// bender or a steel plays and what the old ±0.3 of a semitone never was.
				return _genre switch
				{
					// The RATE is a weighting, not a floor — see BendBias. The floor said "45% of
					// every note that can carry one" and there was no position on the slider at
					// which country bent rarely; what the floor was really reaching for is that
					// the genre still reads as country at the knob's low end, and 0.2 against a
					// bias that reaches ~2.4 on a long landing note keeps that while leaving the
					// runs alone.
					2 => new Expression( 0.30f, MathF.Max( knob, 0.5f ), 0.10f, knob,
						bend: MathF.Max( knob * 0.7f, 0.2f ), bendDepth: 2f ),
					// Shred passes through its notes; when it bends it is a semitone on the way by.
					3 => new Expression( 0.30f, knob, 0.10f, knob, bend: knob * 0.5f, bendDepth: 1f ),
					// POP DOES NOT BEND: its lead is a plucky synth, not a string, and it is only
					// in this case at all because it is not the ska horn section. Its knob is
					// labelled GLIDE in the vibe grid (VibeCodec) and glide is exactly what it
					// buys — a bend here would be the one knob in the toy whose label describes a
					// different gesture from the one it performs.
					5 => new Expression( 0.30f, knob, 0.10f, knob ),
					// Rock, punk, and ska when its lead is a guitar. Pop is NOT here — it has its
					// own case above, because reaching this default by elimination is how a synth
					// came to bend a string.
					_ => new Expression( 0.30f, knob, 0.10f, knob, bend: knob * 0.8f, bendDepth: 2f ),
				};
			}
			default:           return default;
		}
	}

	/// <summary>A bend needs a middle of the note to happen in. Under this the note is over before
	/// the hand has finished moving, so a rolled bend is simply not applied — it is not a shorter
	/// bend, because the gesture does not scale.</summary>
	const float BendMinSeconds = 0.30f;

	/// <summary>Where a bend sits inside its note, in seconds: past the attack, up over a beat of
	/// the hand, and — when it is released rather than held — a moment at pitch before it comes
	/// back. Seconds rather than ticks, for the reason BendTime is (see Patch).</summary>
	const float BendStartSeconds = 0.09f, BendRiseSeconds = 0.11f, BendHoldSeconds = 0.10f;

	/// <summary>How much THIS note wants a bend, as a multiplier on the instrument's propensity.
	///
	/// A FLAT PER-NOTE CHANCE CANNOT SAY WHERE A PLAYER BENDS, and that is the whole of it. Country
	/// ran at a floor of 0.45 — at least 45% of every note long enough to carry one, with no
	/// position on the slider at which the genre bent rarely — and the listening note was exactly
	/// that: right gesture, too often. A smaller constant is the wrong repair, because what is
	/// wrong is not the number but that one number is being asked to describe a hand. A bender or a
	/// pedal steel leans on the long note and on the note a phrase LANDS on, and passes straight
	/// through the run.
	///
	/// So it is two factors and they multiply. LENGTH, because the gesture needs a note to happen
	/// in — this is a preference, not the hard test in <see cref="Roll"/>, which throws away a bend
	/// the note is too short to carry at all. And PHRASE POSITION, read over each HALF of the
	/// phrase rather than the whole of it: a call-and-answer lands twice, and the end of the call
	/// is as much a landing as the end of the answer.</summary>
	/// <param name="phraseU">where this note sits in its phrase — 0 at the start, 1 at the end.</param>
	internal static float BendBias( int spanTicks, float phraseU )
	{
		float len = Math.Clamp( spanTicks / (float)(Timing.TicksPerBeat * 2), 0f, 1f );
		float u = phraseU * 2f - MathF.Floor( phraseU * 2f );   // position within the current half
		return (0.4f + 1.2f * len) * (0.6f + 0.9f * u * u);
	}

	/// <param name="noteSeconds">How long the note lasts, for the bend's length test only. 0 means
	/// "the caller does not know", which reads as too short — a voice that wants bends passes it.</param>
	/// <param name="bendBias">See <see cref="BendBias"/>. 1 is "no opinion", which is what a voice
	/// with no phrase to speak of passes.</param>
	Voicing Roll( in Expression ex, int midi, int prevMidi, Rng rng, float noteSeconds = 0f,
		float bendBias = 1f )
	{
		var v = new Voicing();
		// Vibrato depth is a SMALL pitch fraction (lean 0.5 ≈ ±10 cents) and it's delayed in
		// the synth, so notes read locked-on, not seasick. BendTime is in SECONDS — a quick
		// slide that resolves and locks, never a fraction of a long held note.
		if ( ex.Vib > 0f ) v.VibDepth = 0.003f + 0.006f * ex.Vib;
		if ( ex.Glide > 0f && prevMidi != NoPrev && rng.Chance( ex.Glide ) )
		{
			v.BendSemis = Math.Clamp( (prevMidi - midi) * 0.3f, -2f, 2f ); // lean toward the prev pitch, not all the way
			v.BendTime = 0.13f;                                      // ~130 ms portamento
		}
		else if ( ex.BendIn > 0f && rng.Chance( ex.BendIn ) )
		{
			v.BendSemis = rng.Chance( 0.5f ) ? -0.3f : -0.55f;       // a subtle lean up into pitch
			v.BendTime = 0.09f;                                      // ~90 ms bend up into pitch
		}
		if ( ex.Scoop > 0f && rng.Chance( ex.Scoop ) )
			v.ScoopSemis = rng.Chance( 0.5f ) ? 0.15f : 0.3f;        // a slight attack hump
		// The bend. Both draws happen inside the branch whatever the note length, and the LENGTH
		// TEST comes after them: a note's duration must never decide how many values the
		// expression stream gives up, or the same seed would compose differently every time a
		// figure changed a note's span. A note too short to carry a bend just does not get the one
		// it rolled.
		if ( ex.Bend > 0f && ex.BendDepth > 0f && rng.Chance( ex.Bend * bendBias ) )
		{
			// A BEND IS MOSTLY RELEASED, because a held one REPLACES the composed note. The
			// melody's pitch was chosen against the chord it sits on (NearestSoundingTone); a bend
			// that never comes back un-chooses it and the tune's own hook arrives on a different
			// note every statement. Held bends stay in the vocabulary as the minority — that is a
			// player leaning on one note — rather than as what a bend usually is.
			bool release = rng.Chance( 0.7f );
			float depth = rng.Chance( 0.3f ) ? MathF.Max( 1f, ex.BendDepth - 1f ) : ex.BendDepth;
			int semis = BendSemisTo( midi, depth );
			if ( noteSeconds >= BendMinSeconds && semis > 0 )
			{
				v.BendUpSemis = semis;
				v.BendUpStart = BendStartSeconds;
				v.BendUpTime = BendRiseSeconds;
				v.BendUpHold = release ? BendHoldSeconds : 0f;
			}
		}
		return v;
	}

	/// <summary>
	/// How far a bend from <paramref name="midi"/> actually travels: to the nearest note OF THE
	/// SONG'S KEY at or above <paramref name="depth"/> semitones up, and never back to the note it
	/// started from. 0 means there is nothing in reach and the bend does not happen.
	///
	/// A PLAYER BENDS TO A NOTE, NOT BY AN INTERVAL. The string arrives at the next tone of the
	/// scale, which is a whole step in some places and a semitone in others — that is the same
	/// fact about seven-note scales that <see cref="Harmony.VoicedTone"/> exists for, arriving
	/// here through the melody instead of through a chord. Bent by a fixed interval the note lands
	/// off the key on every degree whose step is the other size: a whole step off the third or the
	/// seventh of a major scale, a semitone off almost anywhere. It is at its worst on a bend that
	/// is HELD, because the note then spends its whole tail out of the key rather than passing
	/// through — which is what "out of tune" means when nothing has actually been mistuned.
	///
	/// Depth stays the instrument's PREFERENCE — how far the hand reaches, which is the thing a
	/// genre has an opinion about — rather than the distance the pitch travels.
	/// </summary>
	int BendSemisTo( int midi, float depth )
		=> Harmony.BendSemis( _scale, ((midi - _rootMidi) % 12 + 12) % 12, depth );

	// Bake a rolled voicing onto a patch. VibDepth is harmless unless the patch carries a
	// vibrato RATE (p.Vibrato) — so a voice the user muted to 0 Hz stays dry — which means a
	// voice that wants expression-vibrato must set its own rate in its patch literal.
	static void ApplyVoicing( ref Patch p, in Voicing v )
	{
		if ( v.VibDepth > 0f ) p.VibDepth = v.VibDepth;
		p.BendSemis = v.BendSemis; p.BendTime = v.BendTime; p.ScoopSemis = v.ScoopSemis;
		p.BendUpSemis = v.BendUpSemis; p.BendUpStart = v.BendUpStart;
		p.BendUpTime = v.BendUpTime; p.BendUpHold = v.BendUpHold;
	}
}
