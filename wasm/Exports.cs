// JS boundary for the web toy — the analog of the old emcc bindings.cpp. This is the ONLY
// web-specific file; MusicGen.cs / VibeCodec.cs are the shared, framework-free core compiled
// straight from ../sbox-library. JS (web/engine.js) boots the .NET runtime and calls these.
//
// Marshalling notes:
//   • The live Config crosses the boundary as a flat double[] ("cfg"). JS treats it as an
//     opaque token — only this file knows the layout (see Cfg.To / Cfg.From). That keeps the
//     vibe edits round-tripping through JS without JS needing to understand any field.
//   • PCM / WAV are large, so they stay in wasm memory and are handed out as a MemoryView
//     (zero-copy view; JS .slice()s it off-heap immediately). Small results (cfg, meta, field
//     metadata) use ordinary array/string marshalling.

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Skafinity;

Console.WriteLine("skafinity engine ready");

[SupportedOSPlatform("browser")]
public partial class Engine
{
	// Last render kept alive so the MemoryView getters can hand JS a view into it.
	static float[] _left = Array.Empty<float>();
	static float[] _right = Array.Empty<float>();
	static byte[] _wav = Array.Empty<byte>();
	static double _sampleRate;

	static System.Collections.Generic.IReadOnlyList<VibeCodec.Field> Fields => VibeCodec.Fields;

	// ── Generation ──────────────────────────────────────────────────────────────────────
	// Render one full loop for `seed`. Returns the frame count; the channel data is pulled
	// separately via ChannelBytes (kept off the return path because it's multi-MB).
	[JSExport]
	internal static int GenerateSong( string seed, [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg )
	{
		short[] s = MusicGen.GenerateSamples( seed, Cfg.From( cfg ), out int sr );
		int frames = s.Length / MusicGen.Channels;
		_left = new float[frames];
		_right = new float[frames];
		for ( int i = 0; i < frames; i++ )
		{
			_left[i] = s[i * 2] / 32768f;
			_right[i] = s[i * 2 + 1] / 32768f;
		}
		_sampleRate = sr;
		return frames;
	}

	[JSExport]
	internal static double SampleRate() => _sampleRate;

	// 0 = left, anything else = right. Bytes of the float channel; JS reinterprets as Float32.
	[JSExport]
	[return: JSMarshalAs<JSType.MemoryView>]
	internal static Span<byte> ChannelBytes( int channel )
		=> MemoryMarshal.AsBytes( (channel == 0 ? _left : _right).AsSpan() );

	// WAV bytes for download. stereo=false downmixes to mono (game parity).
	[JSExport]
	internal static int GenerateWav( string seed, [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg, bool stereo )
	{
		short[] s = MusicGen.GenerateSamples( seed, Cfg.From( cfg ), out int sr );
		short[] outSamples;
		int channels;
		if ( stereo )
		{
			outSamples = s;
			channels = 2;
		}
		else
		{
			int frames = s.Length / 2;
			outSamples = new short[frames];
			for ( int i = 0; i < frames; i++ )
				outSamples[i] = (short)Math.Clamp( (s[i * 2] + s[i * 2 + 1]) / 2, short.MinValue, short.MaxValue );
			channels = 1;
		}
		_wav = MusicGen.WavFromSamples( outSamples, channels, sr );
		return _wav.Length;
	}

	[JSExport]
	[return: JSMarshalAs<JSType.MemoryView>]
	internal static Span<byte> WavBytes() => _wav.AsSpan();

	// ── Config / vibe ───────────────────────────────────────────────────────────────────
	[JSExport]
	[return: JSMarshalAs<JSType.Array<JSType.Number>>]
	internal static double[] DefaultConfig() => Cfg.To( new MusicGen.Config() );

	[JSExport]
	internal static int ConfigSize() => Cfg.Size;

	[JSExport]
	internal static string EncodeVibe( [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg )
		=> VibeCodec.Encode( Cfg.From( cfg ) );

	[JSExport]
	[return: JSMarshalAs<JSType.Array<JSType.Number>>]
	internal static double[] DecodeVibe( string vibe, [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg )
	{
		var c = Cfg.From( cfg );
		VibeCodec.Apply( vibe, c );
		return Cfg.To( c );
	}

	[JSExport]
	internal static bool LooksLikeVibe( string s ) => VibeCodec.LooksLikeVibe( s );

	[JSExport]
	internal static int VibeFieldCount() => VibeCodec.Length;

	[JSExport]
	internal static string VibeFieldName( int i ) => Fields[i].Name;

	[JSExport]
	internal static double VibeFieldMin( int i ) => Fields[i].Min;

	[JSExport]
	internal static double VibeFieldMax( int i ) => Fields[i].Max;

	[JSExport]
	internal static bool VibeFieldIsInt( int i ) => Fields[i].Int;

	[JSExport]
	[return: JSMarshalAs<JSType.Array<JSType.String>>]
	internal static string[] VibeFieldChoices( int i ) => Fields[i].Choices ?? Array.Empty<string>();

	[JSExport]
	[return: JSMarshalAs<JSType.Array<JSType.Number>>]
	internal static double[] SetVibeField( [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg, int i, double norm )
	{
		var c = Cfg.From( cfg );
		Fields[i].SetNorm( c, (float)norm );
		return Cfg.To( c );
	}

	[JSExport]
	internal static double GetVibeNorm( [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg, int i )
		=> Fields[i].GetNorm( Cfg.From( cfg ) );

	[JSExport]
	internal static string VibeDisplay( [JSMarshalAs<JSType.Array<JSType.Number>>] double[] cfg, int i )
		=> Fields[i].Display( Cfg.From( cfg ) );
}

// Flat double[] <-> MusicGen.Config. The order is internal (JS never reads individual
// entries) but must stay self-consistent between To and From. Covers every Config field so
// a vibe edit made on one side is fully preserved across the boundary.
static class Cfg
{
	public const int Size = 57;

	public static double[] To( MusicGen.Config c ) => new double[]
	{
		c.SampleRate, c.TargetSeconds, c.Bars, c.BpmMin, c.BpmMax, c.FastChance,
		c.FastBpmMin, c.FastBpmMax, c.Swing, c.FastSwing,
		c.BassVol, c.SkankVol, c.OrganVol, c.MelodyVol, c.HornVol,
		c.KickVol, c.SnareVol, c.TomVol, c.HatVol, c.CrashVol, c.DrumVol,
		c.Detune, c.BassCutoff, c.SkankCutoff, c.SkankHighpass, c.SkankChop,
		c.LeadCutoff, c.OrganCutoff, c.OrganVibrato, c.HornCutoff, c.Resonance,
		c.BassDrive, c.SkankDrive, c.MelodyDrive, c.HornDrive, c.MasterDrive, c.MasterPeak,
		c.OctavePopChance, c.OrganBubbleChance, c.KickSyncChance, c.GhostSnareChance,
		c.FillChance, c.DrumBusy, c.DrumPush, c.TripletChance, c.BassTriplets,
		c.MelodyRestChance, c.MelodyLeapChance, c.MelodyVibrato, c.PanAmount,
		c.TrumpetWeight, c.SaxWeight, c.OrganWeight, c.TromboneWeight, c.ForceInstrument,
		c.HornSectionChance, c.HornDensity,
	};

	public static MusicGen.Config From( double[] a )
	{
		var c = new MusicGen.Config();
		if ( a == null || a.Length < Size ) return c;
		int i = 0;
		c.SampleRate = (int)a[i++]; c.TargetSeconds = (float)a[i++]; c.Bars = (int)a[i++];
		c.BpmMin = (int)a[i++]; c.BpmMax = (int)a[i++]; c.FastChance = (float)a[i++];
		c.FastBpmMin = (int)a[i++]; c.FastBpmMax = (int)a[i++]; c.Swing = (float)a[i++]; c.FastSwing = (float)a[i++];
		c.BassVol = (float)a[i++]; c.SkankVol = (float)a[i++]; c.OrganVol = (float)a[i++]; c.MelodyVol = (float)a[i++]; c.HornVol = (float)a[i++];
		c.KickVol = (float)a[i++]; c.SnareVol = (float)a[i++]; c.TomVol = (float)a[i++]; c.HatVol = (float)a[i++]; c.CrashVol = (float)a[i++]; c.DrumVol = (float)a[i++];
		c.Detune = (float)a[i++]; c.BassCutoff = (float)a[i++]; c.SkankCutoff = (float)a[i++]; c.SkankHighpass = (float)a[i++]; c.SkankChop = (float)a[i++];
		c.LeadCutoff = (float)a[i++]; c.OrganCutoff = (float)a[i++]; c.OrganVibrato = (float)a[i++]; c.HornCutoff = (float)a[i++]; c.Resonance = (float)a[i++];
		c.BassDrive = (float)a[i++]; c.SkankDrive = (float)a[i++]; c.MelodyDrive = (float)a[i++]; c.HornDrive = (float)a[i++]; c.MasterDrive = (float)a[i++]; c.MasterPeak = (float)a[i++];
		c.OctavePopChance = (float)a[i++]; c.OrganBubbleChance = (float)a[i++]; c.KickSyncChance = (float)a[i++]; c.GhostSnareChance = (float)a[i++];
		c.FillChance = (float)a[i++]; c.DrumBusy = (float)a[i++]; c.DrumPush = (float)a[i++]; c.TripletChance = (float)a[i++]; c.BassTriplets = (float)a[i++];
		c.MelodyRestChance = (float)a[i++]; c.MelodyLeapChance = (float)a[i++]; c.MelodyVibrato = (float)a[i++]; c.PanAmount = (float)a[i++];
		c.TrumpetWeight = (float)a[i++]; c.SaxWeight = (float)a[i++]; c.OrganWeight = (float)a[i++]; c.TromboneWeight = (float)a[i++]; c.ForceInstrument = (int)a[i++];
		c.HornSectionChance = (float)a[i++]; c.HornDensity = (float)a[i++];
		return c;
	}
}
