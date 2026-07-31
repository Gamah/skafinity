using System;
using System.Collections.Generic;

namespace Skafinity;

// The portable PRNG. xmur3 hashes the seed string, mulberry32 streams from it — every
// musical choice in the engine comes out of here, which is why one seed gives one song.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Portable PRNG (mirror exactly in the web client) ──
	static uint Xmur3( string str )
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

	sealed class Rng
	{
		uint _a;
		public Rng( uint seed ) { _a = seed; }
		public float Next()
		{
			_a = unchecked( _a + 0x6D2B79F5u );
			uint t = _a;
			t = unchecked( (t ^ (t >> 15)) * (t | 1u) );
			t ^= unchecked( t + (t ^ (t >> 7)) * (t | 61u) );
			return (t ^ (t >> 14)) / 4294967296f;
		}
		public int Int( int n ) => n <= 0 ? 0 : Math.Min( n - 1, (int)(Next() * n) );
		public bool Chance( float p ) => Next() < p;
		public T Pick<T>( T[] arr ) => arr[Int( arr.Length )];
	}
}
