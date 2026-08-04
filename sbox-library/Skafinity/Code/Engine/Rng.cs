using System;

namespace Skafinity;

/// <summary>
/// The engine's PRNG — xmur3 hashes a seed string to 32 bits, mulberry32 streams from it.
/// Every musical choice comes out of here, which is why one seed gives one song.
///
/// Deliberately hand-rolled rather than <c>System.Random</c>: the algorithm is pinned by this
/// file, so it does not move under us when a runtime changes its implementation, and the same
/// source gives the same stream in the s&amp;box library and the wasm bundle alike.
///
/// Streams are cheap and are meant to be forked liberally — the composer gives each voice in
/// each section its own <c>Rng</c> keyed on the section, so adding a draw to one voice cannot
/// shift what any other voice plays.
/// </summary>
sealed class Rng
{
	uint _a;

	public Rng( uint seed ) { _a = seed; }

	/// <summary>A stream keyed on a string — the composer's usual entry point
	/// (<c>"{tag}:bass:{section}"</c> and friends).</summary>
	public Rng( string seed ) : this( Xmur3( seed ) ) { }

	/// <summary>xmur3: string → a well-mixed 32-bit seed.</summary>
	public static uint Xmur3( string str )
	{
		uint h = 1779033703u ^ (uint)str.Length;
		for ( int i = 0; i < str.Length; i++ )
		{
			h = unchecked( (h ^ str[i]) * 3432918353u );
			h = (h << 13) | (h >> 19);
		}
		h = unchecked( (h ^ (h >> 16)) * 2246822507u );
		h = unchecked( (h ^ (h >> 13)) * 3266489909u );
		return h ^ (h >> 16);
	}

	/// <summary>mulberry32: the next value in [0, 1).</summary>
	public float Next()
	{
		_a = unchecked( _a + 0x6D2B79F5u );
		uint t = _a;
		t = unchecked( (t ^ (t >> 15)) * (t | 1u) );
		t ^= unchecked( t + (t ^ (t >> 7)) * (t | 61u) );
		return (t ^ (t >> 14)) / 4294967296f;
	}

	/// <summary>A value in [0, n). Clamped, so it is always a safe table index.</summary>
	public int Int( int n ) => n <= 0 ? 0 : Math.Min( n - 1, (int)(Next() * n) );

	public bool Chance( float p ) => Next() < p;

	public T Pick<T>( T[] arr ) => arr[Int( arr.Length )];

	/// <summary>A weighted draw — ONE <see cref="Next"/> whatever the weights are.
	///
	/// The tables used to bias a draw by listing an entry twice, which quietly tied "how likely"
	/// to "how many entries" (and to the no-two-genres-share-more-than-one cap the engine test
	/// enforces). A real weighted draw separates them, and costs the same single value out of
	/// the song stream, so a genre's draw count still cannot depend on its tables.</summary>
	/// <summary>The INDEX of a weighted draw — one <see cref="Next"/>, like
	/// <see cref="PickWeighted"/>, for callers whose table is a parallel array rather than the
	/// thing being picked (the melody's note lengths against a genre's weights over them).</summary>
	public int WeightedIndex( int[] weights )
	{
		if ( weights == null || weights.Length == 0 ) return 0;
		int total = 0;
		foreach ( var w in weights ) total += Math.Max( 0, w );
		if ( total <= 0 ) return Int( weights.Length );
		float r = Next() * total;
		for ( int i = 0; i < weights.Length; i++ )
		{
			r -= Math.Max( 0, weights[i] );
			if ( r < 0f ) return i;
		}
		return weights.Length - 1;
	}

	public T PickWeighted<T>( T[] arr, int[] weights )
	{
		if ( arr == null || arr.Length == 0 ) return default;
		if ( weights == null || weights.Length != arr.Length ) return Pick( arr );
		int total = 0;
		foreach ( var w in weights ) total += Math.Max( 0, w );
		if ( total <= 0 ) return Pick( arr );
		float r = Next() * total;
		for ( int i = 0; i < arr.Length; i++ )
		{
			r -= Math.Max( 0, weights[i] );
			if ( r < 0f ) return arr[i];
		}
		return arr[arr.Length - 1];
	}
}
