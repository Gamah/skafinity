using System;
using System.Collections.Generic;

namespace Skafinity;

/// <summary>Which cells of a bar a voice is ALLOWED to play on. This is what keeps ska's skank
/// offbeat by RULE rather than by table — the arranger may move an onset, but not off the class
/// the genre's technique lives in, so a skank cannot drift onto the downbeat however loud the
/// accent grid is there.
///
/// It is the single property that has to survive the whole arranger, and it is the reason the
/// arranger cannot simply write onsets wherever the accent grid is loud. A genre's identity is
/// WHERE it plays; its arrangement is which of those places it uses this time.</summary>
enum CellClass
{
	/// <summary>Every sixteenth — metal's gallop, and anything that subdivides freely.</summary>
	Sixteenths,
	/// <summary>Every eighth — the rock riff, the pop pad.</summary>
	Eighths,
	/// <summary>The beats only — punk's downstrokes.</summary>
	Downbeats,
	/// <summary>The "and" of each beat only — the ska skank and country's chick.</summary>
	Offbeats,
}

/// <summary>
/// THE SECTION'S RHYTHMIC SKELETON — what every part is written against.
///
/// Before this, every voice picked its figure from its own small authored table and no voice knew
/// what any other was playing. The single exception was the riff-doubling bass, and it was the
/// only lockup in the engine that was not a coincidence: pop's pad landed on the kick 100% of the
/// time and ska's skank 1%, and neither number was decided by anyone.
///
/// The skeleton is deliberately DERIVED, not drawn. The kit is table-driven and its grooves are
/// fitted to a played corpus (see <see cref="DrumGroove"/>), so the drums are the measured
/// reference the arranger writes against rather than another client of it — which is why the
/// accent grid comes off the kick and the snare and the genre's own measured accent weights, and
/// why nothing here rolls a die to decide where the section leans.
///
/// Everything is on the section's own SIXTEENTH grid. Occupancy fills in as each part is placed,
/// so a voice arranged later can see what the ones before it took — that is what "one authority
/// arranges every part at once" amounts to in practice.
/// </summary>
sealed class Skeleton
{
	public const int CellTicks = Timing.TicksPerEighth / 2;

	/// <summary>First tick of the section, and how many sixteenth cells long it is.</summary>
	public readonly int StartTick, Cells;

	/// <summary>Cells per bar — the seam and allowed-class tests are per bar.</summary>
	public readonly int BarCells;

	/// <summary>Where the section leans, 0..1, off the groove's kick and snare and the genre's
	/// measured accent weights.</summary>
	public readonly float[] Accent;

	/// <summary>Where the kick lands. The bass's lock reads this directly rather than the accent
	/// grid — "agrees with the kick" is a different claim from "is loud in the same place".</summary>
	public readonly bool[] Kick;

	/// <summary>Phrase ends: every four bars, and the section's last bar. Where a band converges.
	/// </summary>
	public readonly bool[] Seam;

	/// <summary>Where the tune has an onset, and where it is holding a note through.</summary>
	public readonly bool[] TuneOn, TuneHold;

	/// <summary>What the parts placed so far have taken. Mutated as the arranger works down the
	/// voices, which is the whole point of arranging them in one pass.</summary>
	public readonly bool[] Taken;

	public Skeleton( int startTick, int ticks, int barTicks )
	{
		StartTick = startTick;
		Cells = Math.Max( 1, ticks / CellTicks );
		BarCells = Math.Max( 1, barTicks / CellTicks );
		Accent = new float[Cells];
		Kick = new bool[Cells];
		Seam = new bool[Cells];
		TuneOn = new bool[Cells];
		TuneHold = new bool[Cells];
		Taken = new bool[Cells];
	}

	/// <summary>The cell a SONG tick falls in, or −1 outside the section.</summary>
	public int CellAt( int tick )
	{
		int c = (tick - StartTick) / CellTicks;
		return c < 0 || c >= Cells ? -1 : c;
	}

	/// <summary>Whether a cell is one this voice's technique may play on.</summary>
	public bool Allows( CellClass cls, int cell )
	{
		int inBar = cell % BarCells;
		return cls switch
		{
			CellClass.Sixteenths => true,
			CellClass.Eighths => inBar % 2 == 0,
			CellClass.Downbeats => inBar % 4 == 0,
			_ => inBar % 4 == 2,
		};
	}
}

/// <summary>How a voice arranges itself against the skeleton: where it may play, and what it is
/// pulled toward or pushed away from. The parameters are the genre's (see
/// <see cref="GenreProfile"/>) because they say HOW a part behaves, never WHAT it plays — the
/// figure is still the genre's own authored gesture.</summary>
readonly struct ArrangeRole
{
	public readonly CellClass Cells;
	/// <summary>Pull toward cells the kick plays — how hard this voice locks to the drums.</summary>
	public readonly float Kick;
	/// <summary>Push away from cells another part has already taken, and from the tune's landings.
	/// </summary>
	public readonly float Complement;
	/// <summary>Pull toward phrase seams, where a band converges.</summary>
	public readonly float Seam;

	public ArrangeRole( CellClass cells, float kick, float complement, float seam )
	{ Cells = cells; Kick = kick; Complement = complement; Seam = seam; }
}

// The arranger. Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	/// <summary>The current section's skeleton, published in <c>RenderSection</c> alongside
	/// <c>_energy</c> / <c>_feel</c> / <c>_keyShift</c> — the same mechanism, so a voice reads it
	/// the way it reads those.</summary>
	Skeleton _skeleton;

	/// <summary>Build the section's skeleton, then arrange each part against it in turn.
	///
	/// ORDER IS THE DESIGN: the bass goes first because its role is to agree with the kick, which
	/// is already decided; the comp then sees the bass and the tune and can complement them; the
	/// keys see all three. Arranging them independently against a fixed grid would give every voice
	/// the same answer, which is the failure mode this whole phase has to avoid.</summary>
	void PlanArrangement( in Part part, int sectionTick, int barTicks, Pattern tune, string bk )
	{
		var sk = new Skeleton( sectionTick, _sectionTicks, barTicks );
		_skeleton = sk;

		// ── the accent grid, off the kit ──
		foreach ( var h in _groove.Kick.Slice( sectionTick, sectionTick + _sectionTicks, sectionTick, _feel ) )
		{
			int c = sk.CellAt( h.Tick );
			if ( c < 0 ) continue;
			sk.Kick[c] = true;
			sk.Accent[c] += h.Vel;
		}
		foreach ( var h in _groove.Snare.Slice( sectionTick, sectionTick + _sectionTicks, sectionTick, _feel ) )
		{
			int c = sk.CellAt( h.Tick );
			if ( c >= 0 ) sk.Accent[c] += h.Value == DrumGroove.Ghost ? 0.3f : h.Vel;
		}
		// The genre's own measured accent weights on top: a country bar leans on its offbeat and a
		// metal bar is deliberately flat, and that is a property of the genre rather than of the
		// groove it drew.
		for ( int c = 0; c < sk.Cells; c++ )
		{
			int inBar = c % sk.BarCells;
			float metric = inBar == 0 ? _prof.AccentDown
				: inBar % 4 != 0 ? _prof.AccentOff
				: (inBar / 4) % 2 == 1 ? _prof.AccentBack : 1f;
			sk.Accent[c] = Math.Min( 1f, sk.Accent[c] * metric * 0.6f );
		}

		// ── the seams ──
		// A phrase ends every four bars, and the section's last bar is one whatever its length.
		for ( int bar = 4; bar * sk.BarCells < sk.Cells; bar += 4 )
			sk.Seam[bar * sk.BarCells - 1] = true;
		if ( sk.Cells > 0 ) sk.Seam[sk.Cells - 1] = true;

		// ── the tune's occupancy ──
		// The tune is written first and everything else is written against it, which is the order a
		// song is actually made in. Note that the tune is exempt from the section's feel, so it is
		// sliced at the nominal rate here exactly as RenderTune slices it.
		if ( tune != null )
		{
			int anchor = _sectionTicks > 0 && _sectionTicks < tune.LengthTicks
				? sectionTick - (tune.LengthTicks - _sectionTicks) : sectionTick;
			foreach ( var h in tune.Slice( sectionTick, sectionTick + _sectionTicks, anchor ) )
			{
				int c = sk.CellAt( h.Tick );
				if ( c < 0 ) continue;
				sk.TuneOn[c] = true;
				int held = Math.Min( h.SpanTicks, Timing.TicksPerBeat * 2 ) / Skeleton.CellTicks;
				for ( int k = 1; k < held && c + k < sk.Cells; k++ ) sk.TuneHold[c + k] = true;
			}
		}

		// ── the parts ──
		// EVERY CHORUS AGREES, AND THE CHORUS IS STILL ARRANGED. Those are two different claims and
		// conflating them is what would make this whole phase a no-op: if a chorus quoted the TABLE
		// rather than quoting the other choruses, the song's own rhythm section would stay one entry
		// out of a table of three, which is the ceiling this exists to break — and the choruses are
		// most of what a listener hears as the song.
		//
		// So the chorus is arranged ONCE and cached as the song's own part. Every later chorus
		// reuses the cached line rather than re-deriving it: the guarantee becomes structural
		// instead of resting on the skeleton happening to come out the same at three different
		// points in the song.
		var rng = new Rng( $"{_tag}:arr:{bk}" );
		if ( part.Type == Section.Chorus )
		{
			if ( !_chorusArranged )
			{
				_songBass = Arrange( _songBass, sk, rng, _prof.BassRole, _prof.BassPatterns );
				_songComp = Arrange( _songComp, sk, rng, _prof.CompRole, _prof.CompFigures );
				// The LOUD figure is the chorus part in any genre that changes technique when the
				// section is loud, so leaving it un-arranged would leave exactly the bars a listener
				// remembers coming straight out of a table of two.
				if ( _songLoud != null )
					_songLoud = Arrange( _songLoud, sk, rng, _prof.LoudCompRole ?? _prof.CompRole,
						_prof.LoudCompFigures );
				if ( _songKeys != null )
					_songKeys = Arrange( _songKeys, sk, rng, _prof.KeysRole, _prof.KeysFigures );
				_chorusArranged = true;
			}
			else { MarkTaken( sk, _songBass ); MarkTaken( sk, _songComp ); MarkTaken( sk, _songKeys ); }
			_bassPat = _songBass; _compFig = _songComp; _keysFig = _songKeys;
			return;
		}

		_bassPat = Arrange( _bassPat, sk, rng, _prof.BassRole, _prof.BassPatterns );
		_compFig = Arrange( _compFig, sk, rng, _prof.CompRole, _prof.CompFigures );
		if ( _keysFig != null )
			_keysFig = Arrange( _keysFig, sk, rng, _prof.KeysRole, _prof.KeysFigures );
	}

	/// <summary>Whether the song's chorus parts have been arranged yet. The chorus is arranged the
	/// first time one is rendered and every later chorus reuses that line.</summary>
	bool _chorusArranged;

	/// <summary>Write a figure's onsets into the skeleton's occupancy without changing it — what a
	/// quoted part still owes the parts arranged after it.</summary>
	void MarkTaken( Skeleton sk, Pattern fig )
	{
		if ( fig == null ) return;
		foreach ( var h in fig.Slice( sk.StartTick, sk.StartTick + _sectionTicks, sk.StartTick, _feel ) )
		{
			int c = sk.CellAt( h.Tick );
			if ( c >= 0 ) sk.Taken[c] = true;
		}
	}

	/// <summary>How often a non-chorus section plays its figure verbatim rather than working on
	/// it. The rest of the weight is shared over the four mutations below.</summary>
	const float QuoteWeight = 1f;

	/// <summary>
	/// Arrange one part: the genre's authored figure, worked on against the section's skeleton.
	///
	/// THE TABLES ARE SEED MATERIAL, NOT A CEILING. The authored figures are each genre's
	/// characteristic gestures and none of them is deleted — what changes is that a section's part
	/// is now figure x mutation x skeleton rather than one entry out of a table of three. That
	/// product is where the state count comes from: the whole rhythm section used to have twelve
	/// states in punk over five hundred songs, because it was the product of three table sizes and
	/// randomness cannot reach past a table size.
	///
	/// Every mutation stays inside the genre's allowed cell class, so a skank stays offbeat and a
	/// punk downstroke stays on the beat however loud the accent grid is elsewhere.
	/// </summary>
	Pattern Arrange( Pattern fig, Skeleton sk, Rng rng, in ArrangeRole role, Pattern[] table )
	{
		if ( fig == null || fig.Count == 0 ) { return fig; }

		// The draw is taken whatever the outcome, so a genre's mutation rate cannot change how much
		// of this stream the next voice sees — the same discipline PickOrNull keeps in the composer.
		float mutate = Math.Clamp( _prof.MutateRate, 0f, 1f );
		int op = rng.WeightedIndex( new[]
		{
			(int)MathF.Round( QuoteWeight * (1f - mutate) * 100f ),   // quote
			(int)MathF.Round( mutate * 30f ),                         // drop
			(int)MathF.Round( mutate * 30f ),                         // add
			(int)MathF.Round( mutate * 25f ),                         // displace
			(int)MathF.Round( mutate * 15f ),                         // recombine
		} );

		var ticks = new List<int>();
		var values = new List<int>();
		var vels = new List<float>();
		for ( int i = 0; i < fig.Count; i++ )
		{ ticks.Add( fig.TickAt( i ) ); values.Add( fig.ValueAt( i ) ); vels.Add( fig.VelAt( i ) ); }

		switch ( op )
		{
			case 1: Drop( ticks, values, vels, fig, sk, rng, role ); break;
			case 2: Add( ticks, values, vels, fig, sk, rng, role ); break;
			case 3: Displace( ticks, values, vels, fig, sk, rng, role ); break;
			case 4: Recombine( ticks, values, vels, fig, rng, table ); break;
		}

		var arranged = ticks.Count == 0 ? fig
			: new Pattern( fig.LengthTicks, ticks.ToArray(), values.ToArray(), vels.ToArray() );
		MarkTaken( sk, arranged );
		return arranged;
	}

	// ── scoring ──
	// A figure loops inside the section, so a candidate position is judged over EVERY repetition it
	// will actually be played at rather than over the first one. A one-bar figure in an eight-bar
	// section is played eight times; scoring it against bar 1 alone would arrange it for a bar it
	// spends seven eighths of its life away from.
	float Score( int figTick, Pattern fig, Skeleton sk, in ArrangeRole role )
	{
		float sum = 0; int n = 0;
		for ( int rep = 0; ; rep++ )
		{
			int t = sk.StartTick + (int)Math.Round( (rep * (double)fig.LengthTicks + figTick) / Math.Max( 0.01f, _feel ) );
			int c = sk.CellAt( t );
			if ( c < 0 ) break;
			sum += sk.Accent[c]
				+ role.Kick * (sk.Kick[c] ? 1f : 0f)
				+ role.Seam * (sk.Seam[c] ? 1f : 0f)
				- role.Complement * ((sk.TuneOn[c] ? 1f : 0f) + (sk.Taken[c] ? 0.6f : 0f));
			n++;
		}
		return n == 0 ? float.NegativeInfinity : sum / n;
	}

	bool AllowedFigTick( int figTick, Pattern fig, Skeleton sk, in ArrangeRole role )
	{
		int c = sk.CellAt( sk.StartTick + (int)Math.Round( figTick / Math.Max( 0.01f, _feel ) ) );
		return c >= 0 && figTick % Skeleton.CellTicks == 0 && sk.Allows( role.Cells, c );
	}

	/// <summary>DROP the onset that fights hardest with what is already there — a tune landing the
	/// comp is stepping on, most often. Never the figure's first onset: a figure that loses its
	/// downbeat is a different figure.</summary>
	void Drop( List<int> ticks, List<int> values, List<float> vels, Pattern fig, Skeleton sk,
		Rng rng, in ArrangeRole role )
	{
		if ( ticks.Count <= 2 ) return;
		int worst = -1; float worstScore = float.MaxValue;
		for ( int i = 1; i < ticks.Count; i++ )
		{
			float s = Score( ticks[i], fig, sk, role );
			if ( s < worstScore ) { worstScore = s; worst = i; }
		}
		if ( worst < 0 ) return;
		ticks.RemoveAt( worst ); values.RemoveAt( worst ); vels.RemoveAt( worst );
	}

	/// <summary>ADD an onset on the best free cell of the genre's allowed class. The new hit takes
	/// its VALUE from the onset before it, so it is the same gesture played once more rather than a
	/// cell type the figure never used.</summary>
	void Add( List<int> ticks, List<int> values, List<float> vels, Pattern fig, Skeleton sk,
		Rng rng, in ArrangeRole role )
	{
		int best = -1; float bestScore = float.NegativeInfinity;
		for ( int t = 0; t < fig.LengthTicks; t += Skeleton.CellTicks )
		{
			if ( ticks.Contains( t ) || !AllowedFigTick( t, fig, sk, role ) ) continue;
			float s = Score( t, fig, sk, role );
			if ( s > bestScore ) { bestScore = s; best = t; }
		}
		if ( best < 0 ) return;
		int at = 0;
		while ( at < ticks.Count && ticks[at] < best ) at++;
		int from = Math.Max( 0, at - 1 );
		ticks.Insert( at, best );
		values.Insert( at, values[from] );
		vels.Insert( at, vels[from] * 0.9f );
	}

	/// <summary>DISPLACE one onset by a cell, staying inside the allowed class — the same figure
	/// with one hit pushed or pulled. Not the first onset, for the same reason DROP spares it.
	/// </summary>
	void Displace( List<int> ticks, List<int> values, List<float> vels, Pattern fig, Skeleton sk,
		Rng rng, in ArrangeRole role )
	{
		if ( ticks.Count <= 1 ) return;
		int i = 1 + rng.Int( ticks.Count - 1 );
		int step = rng.Chance( 0.5f ) ? Skeleton.CellTicks : -Skeleton.CellTicks;
		// The allowed class is often coarser than a sixteenth, so widen the move rather than giving
		// up: a skank displaced by one cell can never be legal, displaced by two it is.
		for ( int k = 1; k <= 4; k++ )
		{
			int t = ticks[i] + step * k;
			if ( t <= 0 || t >= fig.LengthTicks || ticks.Contains( t ) ) continue;
			if ( !AllowedFigTick( t, fig, sk, role ) ) continue;
			// The VALUE and the VELOCITY move with the tick. A displace that moved only the tick
			// would keep the figure's cells and lose which hit was which — the chop that got pushed
			// would arrive wearing the next chop's articulation.
			int v = values[i]; float g = vels[i];
			ticks.RemoveAt( i ); values.RemoveAt( i ); vels.RemoveAt( i );
			int at = 0;
			while ( at < ticks.Count && ticks[at] < t ) at++;
			ticks.Insert( at, t ); values.Insert( at, v ); vels.Insert( at, g );
			return;
		}
	}

	/// <summary>RECOMBINE: take one bar of the phrase from another figure in the same genre's
	/// table. The genre's own vocabulary, re-cut — which is why this is the mutation that reaches
	/// furthest without ever producing a gesture the genre does not have.</summary>
	void Recombine( List<int> ticks, List<int> values, List<float> vels, Pattern fig, Rng rng,
		Pattern[] table )
	{
		if ( table == null || table.Length < 2 ) return;
		Pattern other = null;
		for ( int tries = 0; tries < 4 && other == null; tries++ )
		{
			var p = table[rng.Int( table.Length )];
			if ( !ReferenceEquals( p, fig ) ) other = p;
		}
		if ( other == null ) return;

		int barTicks = _time.BarTicks;
		int bars = Math.Max( 1, fig.LengthTicks / barTicks );
		int bar = rng.Int( bars );
		int from = bar * barTicks, to = from + barTicks;

		for ( int i = ticks.Count - 1; i >= 0; i-- )
			if ( ticks[i] >= from && ticks[i] < to )
			{ ticks.RemoveAt( i ); values.RemoveAt( i ); vels.RemoveAt( i ); }

		// ONE bar of the other figure, folded onto this bar — and taken from ONE of its bars, not
		// every bar of it collapsed together. `tick % barTicks` maps a two-bar figure's second bar
		// back onto its first, so reading the whole thing would interleave two bars' onsets into
		// one and hand back a list that no longer ascends.
		int otherBar = (rng.Int( Math.Max( 1, other.LengthTicks / barTicks ) )) * barTicks;
		for ( int i = 0; i < other.Count; i++ )
		{
			int ot = other.TickAt( i );
			if ( ot < otherBar || ot >= otherBar + barTicks ) continue;
			int t = ot - otherBar + from;
			if ( t < from || t >= to || ticks.Contains( t ) ) continue;
			int at = 0;
			while ( at < ticks.Count && ticks[at] < t ) at++;
			ticks.Insert( at, t ); values.Insert( at, other.ValueAt( i ) ); vels.Insert( at, other.VelAt( i ) );
		}
	}
}
