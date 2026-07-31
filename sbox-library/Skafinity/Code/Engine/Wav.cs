using System;
using System.Collections.Generic;

namespace Skafinity;

// Output: float mix → interleaved 16-bit PCM, and the WAV container for export.
//
// Part of the MusicGen engine — see MusicGen.cs.

public sealed partial class MusicGen
{
	// ── Output ──
	short[] ToShorts( float gain )
	{
		int n = _bufL.Length;
		var s = new short[n * Channels];
		for ( int i = 0; i < n; i++ )
		{
			s[i * 2] = ToS16( _bufL[i] * gain );
			s[i * 2 + 1] = ToS16( _bufR[i] * gain );
		}
		return s;
	}

	static short ToS16( float v ) => (short)(Math.Clamp( v, -1f, 1f ) * 32767f);

	/// <summary>Wrap already-rendered 16-bit samples in a WAV (for export). Mono or
	/// interleaved stereo per <paramref name="channels"/>.</summary>
	public static byte[] WavFromSamples( short[] samples, int channels, int sampleRate )
	{
		int dataSize = samples.Length * 2;
		int blockAlign = channels * 2;
		var bytes = new System.Collections.Generic.List<byte>( 44 + dataSize );
		void Str( string s ) { foreach ( var ch in s ) bytes.Add( (byte)ch ); }
		void U32( uint v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); bytes.Add( (byte)(v >> 16) ); bytes.Add( (byte)(v >> 24) ); }
		void U16( ushort v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); }
		Str( "RIFF" ); U32( (uint)(36 + dataSize) ); Str( "WAVE" );
		Str( "fmt " ); U32( 16 ); U16( 1 ); U16( (ushort)channels );
		U32( (uint)sampleRate ); U32( (uint)(sampleRate * blockAlign) ); U16( (ushort)blockAlign ); U16( 16 );
		Str( "data" ); U32( (uint)dataSize );
		foreach ( var s in samples ) { ushort u = (ushort)s; bytes.Add( (byte)u ); bytes.Add( (byte)(u >> 8) ); }
		return bytes.ToArray();
	}

	byte[] EncodeWav( float gain )
	{
		int n = _bufL.Length;
		int dataSize = n * 4;
		var bytes = new System.Collections.Generic.List<byte>( 44 + dataSize );
		void Str( string s ) { foreach ( var ch in s ) bytes.Add( (byte)ch ); }
		void U32( uint v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); bytes.Add( (byte)(v >> 16) ); bytes.Add( (byte)(v >> 24) ); }
		void U16( ushort v ) { bytes.Add( (byte)v ); bytes.Add( (byte)(v >> 8) ); }
		Str( "RIFF" ); U32( (uint)(36 + dataSize) ); Str( "WAVE" );
		Str( "fmt " ); U32( 16 ); U16( 1 ); U16( 2 );
		U32( (uint)_sr ); U32( (uint)(_sr * 4) ); U16( 4 ); U16( 16 );
		Str( "data" ); U32( (uint)dataSize );
		for ( int i = 0; i < n; i++ )
		{
			ushort l = (ushort)ToS16( _bufL[i] * gain );
			ushort r = (ushort)ToS16( _bufR[i] * gain );
			bytes.Add( (byte)l ); bytes.Add( (byte)(l >> 8) );
			bytes.Add( (byte)r ); bytes.Add( (byte)(r >> 8) );
		}
		return bytes.ToArray();
	}
}
