# Skafinity — procedural ska/reggae-rock for s&box

A self-contained **s&box code library** that streams an endless, deterministic procedural
ska / reggae-rock track, generated entirely from a short shareable seed. No audio assets —
the music is synthesised from scratch and scheduled over a `SoundStream`.

This is the sound-generator core of [skafinity](../../) / the Rotaliate music engine, with
every game-specific dependency (player data, networking, UI) stripped out. It is just the
generator + a playback component you drop on a `GameObject`.

## Install

Libraries live in your project's `Libraries/` folder ([docs](https://sbox.game/dev/doc/code/libraries)).
Copy the `Skafinity/` folder there:

```
<your-project>/Libraries/Skafinity/
  Skafinity.sbproj
  Code/
    MusicGen.cs        # composer + subtractive synth (portable, deterministic)
    VibeCodec.cs       # base-36 "vibe" knob encoding (the shareable seed fragment)
    SkafinityPlayer.cs  # the Component: streaming, looping, crossfade, look-ahead, export
    Skafinity.csproj
```

Open the editor once and s&box references the library from your game code automatically. All
public types live in the `Skafinity` namespace.

## Usage

Add a **`SkafinityPlayer`** component to a GameObject in your scene. It auto-plays on start.
Everything is tunable from the inspector (grouped: Music, Seed, Output, Crossfade, Tempo,
Mix, Tone, Feel, Stereo, Instrument, Horns).

```csharp
var music = gameObject.Components.Get<SkafinityPlayer>();

// Play a specific shareable seed: "vibe:tag:n", "tag:n", or just "tag"
music.PlaySeed( "bd44ac2a:23" );

// Walk the infinite sequence
music.NextSong();          // n+1, crossfades when the current loop runs out
music.PrevSong();          // n-1
music.SetN( 100 );         // jump

// Vibe knobs (the shareable subset of the config)
music.RerollVibe();        // randomise the vibe, keep per-instrument volumes
music.SetVibe( 0, 0.5f );  // set field 0 (TEMPO MIN) from a 0..1 fraction

string seed = music.CurrentSeed;       // "vibe:tag:n" — share this
var cfg     = music.EffectiveConfig(); // the MusicGen.Config currently in effect

// Write the current loop to a WAV under FileSystem.Data
string file = music.SaveCurrentToFile();
```

You can also generate audio without the component, off any thread:

```csharp
// One-shot WAV bytes
byte[] wav = MusicGen.Generate( "mytag:0", new MusicGen.Config { TargetSeconds = 60f } );

// Or raw 16-bit mono PCM
short[] pcm = MusicGen.GenerateSamples( "mytag:0", new MusicGen.Config(), out int sampleRate );
```

## Key settings

| Group | What it does |
|---|---|
| **Music** | Master `Enabled` / `Volume`, `LiveReload` (regenerate on knob change), `MixerName`, `AutoPlay` |
| **Seed** | `Tag`, `StartN`, `Vibe` override, `PersistProgress` + `SaveSlot` (resume across sessions) |
| **Output** | `SampleRate`, `TargetSeconds`, `RenderThreads` (synthesis is split across worker threads) |
| **Crossfade** | `Crossfade` window, `CrossfadeOverlap`, `LoopsPerSong`, `AheadCount` (look-ahead depth) |
| Tempo / Mix / Tone / Feel / Stereo / Instrument / Horns | The full generator knob set — see `MusicGen.Config` |

## Determinism

Same seed → same song, on every machine. The generator uses a portable `xmur3` → `mulberry32`
PRNG with a fixed call order (all 32-bit unsigned wrapping arithmetic). The PRNG seed string is
`"{tag}:{n}"` (empty tag ⇒ `"skafinity"`). Composition is the must-match part; the `Vibe` string
overrides the subset of knobs `VibeCodec` covers, the rest come from `MusicGen.Config` defaults.

`VibeCodec` is **genre-aware and append-only**. The vibe string is `[genre char][globals]
[instrument grid]`, the grid reserving up to 8 instruments × 4 columns at fixed positions
(`1 + globals + i*4 + c`). Each genre (Ska, Rock) has its own instrument grid; `Fields(genre)`
is the per-genre list the UI iterates. Never reorder or remove — only append globals,
instrument slots, or columns — or existing shared seeds change meaning.

## License

Inherits the repository license (see `../../LICENSE`).
