using System;
using System.Collections.Generic;

namespace Skafinity;

// Per-note pitch shaping — vibrato, bend-in, glide, scoop. An Expression is the per-
// instrument propensity; a Voicing is one roll off it, baked onto a Patch at emit time.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Instrument expression ──
	// Four expressive PROPERTIES every pitched voice can lean on (drums are excluded). Each
	// instrument gets a genre-specific PROPENSITY for each, "based on what it is" — a brass
	// lead sings and scoops, a bass slides, a power-chord guitar stays dead straight. The
	// realization is the per-note pitch shaping in RenderEvent (vibrato depth + bend envelope).
	//   Vib    — #1 vibrato depth (a constant lean, no per-note roll)
	//   BendIn — #2 bend up INTO the note from a step below (per-note chance)
	//   Glide  — #3 portamento from the previous note's pitch (per-note chance)
	//   Scoop  — #4 bend up-and-back within the note (per-note chance)
	readonly struct Expression
	{
		public readonly float Vib, BendIn, Glide, Scoop;
		public Expression( float vib, float bendIn, float glide, float scoop )
		{ Vib = vib; BendIn = bendIn; Glide = glide; Scoop = scoop; }
	}

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
				// Country leans hard into bends (the telecaster twang); rock/metal ride the knob.
				float bend = _genre == 2 ? MathF.Max( _c.LeadGtrBend, 0.5f ) : _c.LeadGtrBend;
				return new Expression( 0.30f, bend, 0.10f, bend );
			}
			default:           return default;
		}
	}

	// A rolled-per-note voicing: the concrete pitch-shaping a note will get. Vibrato is a
	// constant depth (no draw); bend-in/glide/scoop are rolled against their propensities, so
	// only voices that lean on them ever pull from the expression stream.
	struct Voicing { public float VibDepth, BendSemis, BendTime, ScoopSemis; }

	Voicing Roll( in Expression ex, int midi, int prevMidi, Rng rng )
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
		return v;
	}

	// Bake a rolled voicing onto a patch. VibDepth is harmless unless the patch carries a
	// vibrato RATE (p.Vibrato) — so a voice the user muted to 0 Hz stays dry — which means a
	// voice that wants expression-vibrato must set its own rate in its patch literal.
	static void ApplyVoicing( ref Patch p, in Voicing v )
	{
		if ( v.VibDepth > 0f ) p.VibDepth = v.VibDepth;
		p.BendSemis = v.BendSemis; p.BendTime = v.BendTime; p.ScoopSemis = v.ScoopSemis;
	}
}
