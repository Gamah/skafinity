# Skafinity — procedural ska/reggae-rock for s&box

A self-contained **s&box code library** that streams an endless, deterministic procedural
ska / reggae-rock track, generated entirely from a short shareable seed. No audio assets —
the music is synthesised from scratch and scheduled over a `SoundStream`.

This is the sound-generator core of [skafinity](../../) / the Rotaliate music engine, with
every game-specific dependency (player data, networking) stripped out.

It ships as **two pieces** you can mix and match:

- **The object** — `SkafinityPlayer`, a `Component` you drop on a `GameObject`. It generates
  + streams the music (optionally onto a named mixer channel) and exposes the whole knob set.
  This is all you need; drive it from the inspector or from code.
- **The optional panel** — `SkafinityMusicPanel`, a drop-in Razor `PanelComponent` that finds
  a `SkafinityPlayer` and offers the knobs as in-game UI. Add it only if you want players to
  tweak the vibe themselves; the engine needs nothing from it.

## Install

Libraries live in your project's `Libraries/` folder ([docs](https://sbox.game/dev/doc/code/libraries)).
Copy the `Skafinity/` folder there:

```
<your-project>/Libraries/Skafinity/
  Skafinity.sbproj
  skafinity.config.json # baseline house-mix (peak balances) — edit to retune without recompiling
  Code/
    MusicGen.cs        # composer + subtractive synth (portable, deterministic)
    VibeCodec.cs       # base-36 "vibe" knob encoding (the shareable seed fragment)
    SkafinityPlayer.cs # the object: streaming, looping, crossfade, look-ahead, export
    Skafinity.csproj
    UI/
      SkafinityMusicPanel.razor       # optional drop-in settings panel (PanelComponent)
      SkafinityMusicPanel.razor.scss  # its styling (re-themeable — see below)
```

Open the editor once and s&box references the library from your game code automatically. All
public types live in the `Skafinity` namespace.

## Usage — the object

Add a **`SkafinityPlayer`** component to a GameObject in your scene. It auto-plays on start.
To play on the mixer's Music channel, set **`MixerName = "Music"`** (any mixer name; empty =
default mixer). Everything is tunable from the inspector (grouped: Music, Seed, Output,
Crossfade, Tempo, Mix, Tone, Feel, Stereo, Instrument, Horns, Genre, Rock).

```csharp
var music = gameObject.Components.Get<SkafinityPlayer>();

// Play a specific shareable seed: "vibe:tag:n", "tag:n", or just "tag"
music.PlaySeed( "bd44ac2a:23" );

// Walk the infinite sequence
music.NextSong();          // n+1, crossfades when the current loop runs out
music.PrevSong();          // n-1
music.SetN( 100 );         // jump

// Vibe knobs (the shareable subset of the config)
music.RerollVibe();                    // randomise the vibe knobs, keep per-instrument volumes
music.RerollVibe( includeVolumes: true, includeGenre: true ); // opt-in full shuffle (also rolls volumes + genre)
music.SetVibe( 0, 0.5f );              // set field 0 (TEMPO MIN) from a 0..1 fraction
music.SetGenre( 1 );                   // switch genre (re-encodes the vibe so it sticks)
music.RandomEverySong = true;          // re-roll the vibe each new song (keeps your volumes + genre)

string seed = music.CurrentSeed;       // "vibe:tag:n" — share this
var cfg     = music.EffectiveConfig(); // the MusicGen.Config currently in effect

// Write the current loop to a WAV under FileSystem.Data
string file = music.SaveCurrentToFile();
```

You can also generate audio without the component, off any thread:

```csharp
// One-shot WAV bytes
byte[] wav = MusicGen.Generate( "mytag:0", new MusicGen.Config { TargetSeconds = 60f } );

// Or raw interleaved-stereo 16-bit PCM
short[] pcm = MusicGen.GenerateSamples( "mytag:0", new MusicGen.Config(), out int sampleRate );
```

## Usage — the optional panel

If you want players to tweak the music in-game, add a **`SkafinityMusicPanel`**:

1. A GameObject with a **`ScreenPanel`** component (the UI root).
2. A child GameObject with **`SkafinityMusicPanel`** on it.

That's it. The panel auto-finds a `SkafinityPlayer` in the scene (or set its `Player`
property explicitly). A floating ♪ button toggles the settings board: now-playing seed +
copy, prev/next, paste-a-seed, mute, volume, genre, per-instrument vibe mixer, global knobs,
reroll, "random every song", and save-to-`.wav`. Every control just calls the player's public
API, so anything the panel does you can do from code too.

**Re-theming.** The whole palette is a block of SCSS variables at the top of
`UI/SkafinityMusicPanel.razor.scss` (`$bg`, `$btn`, `$accent`, …). Override those to restyle —
nothing below that block hardcodes a colour. Or skip the panel entirely and build your own UI
against the same `SkafinityPlayer` API.

## Key settings

| Group | What it does |
|---|---|
| **Music** | Master `Enabled` / `Volume`, `LiveReload` (regenerate on knob change), `MixerName`, `AutoPlay`, `RandomEverySong` (shuffle) |
| **Seed** | `Tag`, `StartN`, `Vibe` override, `PersistProgress` + `SaveSlot` (resume across sessions) |
| **Output** | `SampleRate`, `TargetSeconds`, `RenderThreads` (synthesis is split across worker threads) |
| **Crossfade** | `Crossfade` window, `CrossfadeOverlap`, `LoopsPerSong`, `AheadCount` (look-ahead depth) |
| Tempo / Mix / Tone / Feel / Stereo / Instrument / Horns | The full generator knob set — see `MusicGen.Config` |

## House-mix config

`skafinity.config.json` (next to `Skafinity.sbproj`) tunes the **baseline mix** — the per-voice
peak balances, kit presence, and **stereo width** that sit *under* the seed's vibe.
`SkafinityPlayer` reads it at startup (`FileSystem.Mounted`) and overlays its `advanced` block
onto every generated config, so you can re-balance the kit/instruments or retune the width by
editing one JSON file instead of recompiling:

```json
{ "advanced": { "TomBalance": 0.78, "HatBalance": 0.407, "BassBalance": 0.733, "WidthBacking": 0.5, "WidthLead": 1.0 } }
```

Keys match `MusicGen.Config` field names 1:1 (see `VibeCodec.AdvancedFields`); unknown keys are
ignored and values are clamped per field. These are **not** vibe knobs — they shape the house
mix, not a song's shareable identity, so they never appear in the seed or the panel's sliders.
This is the *same* file the web toy uses (the web build copies it from here), so both stay in
sync. Missing/invalid file → the engine's built-in defaults.

## Stereo image

The mix is panned across the field rather than summed to centre. The kit is placed like a real
drumset — hats left, ride right, toms spread by pitch (rack → left, floor → right), the two
crashes split L/R (side chosen per song) — and every non-drum voice is **double-tracked**: two
slightly-detuned, independently-phased takes panned apart, so the width comes from genuine
decorrelation, not a mono signal copied to both channels. Bass stays centred for a tight low
end. The `STEREO WIDTH` vibe knob (`Config.PanAmount`) is a 0–1 master that scales the whole
image — the drum spread and the double-tracking amount — from full down to mono. The
double-tracking knobs (`DoubleTrack`, `WidthBacking`, `WidthLead`, `WidthDetune`,
`WidthDelayMs`, `WidthJitterMs`, `WidthAmpVar`, `WidthCutoffVar`) live in the house-mix config
above — tune them without a rebuild.

## Determinism

Same seed → same song, on every machine. The generator uses a portable `xmur3` → `mulberry32`
PRNG with a fixed call order (all 32-bit unsigned wrapping arithmetic). The PRNG seed string is
`"{tag}:{n}"` (empty tag ⇒ `"skafinity"`). Composition is the must-match part; the `Vibe` string
overrides the subset of knobs `VibeCodec` covers, the rest come from `MusicGen.Config` defaults.

`VibeCodec` is **genre-aware and append-only**. The vibe string is `[genre char][globals]
[instrument grid]`, the grid reserving up to 8 instruments × 4 columns at fixed positions
(`1 + globals + i*4 + c`). Each genre (Ska, Rock, Country, Metal) has its own instrument grid; `Fields(genre)`
is the per-genre list the UI iterates. Never reorder or remove — only append globals,
instrument slots, or columns — or existing shared seeds change meaning.

## License

Inherits the repository license (see `../../LICENSE`).
