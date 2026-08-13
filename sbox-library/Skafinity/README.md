# Skafinity — procedural music for s&box

A self-contained **s&box code library** that streams an endless, deterministic procedural
song — **ska-punk, rock, country, metal, punk or pop** — generated entirely from a short
shareable seed. No audio assets — the music is synthesised from scratch and scheduled over a
`SoundStream`.

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
    Engine/            # the composer + subtractive synth, one file per concern. Framework-free
                       #   and deterministic — MusicGen.cs is the entry point, VibeCodec.cs the
                       #   base-36 "vibe" encoding behind the shareable seed.
    SkafinityPlayer.cs # the object: streaming, looping, crossfade, look-ahead, export
    SkafinityCommands.cs # console commands for driving it in the editor (see below)
    Skafinity.csproj
    UI/
      SkafinityMusicPanel.razor       # optional drop-in settings panel (PanelComponent)
      SkafinityMusicPanel.razor.scss  # its layout/type tokens
      SkafinityTheme.cs               # its palette — one accent colour (see below)
```

Open the editor once and s&box references the library from your game code automatically. All
public types live in the `Skafinity` namespace.

## Usage — the object

Add a **`SkafinityPlayer`** component to a GameObject in your scene. It auto-plays on start.
To play on the mixer's Music channel, set **`MixerName = "Music"`** (any mixer name; empty =
default mixer). Everything is tunable from the inspector (grouped: Music, Seed, Output,
Crossfade, Tempo, Mix, Tone, Feel, Stereo, Instrument, Horns, Genre, Guitars / Keys).

```csharp
var music = gameObject.Components.Get<SkafinityPlayer>();

// Play a specific shareable seed: "tag:n[:genre][:vibe]" (a bare tag is song 0)
music.PlaySeed( "bd44ac2a:23" );

// Walk the infinite sequence
music.NextSong();          // n+1, crossfades when the current loop runs out
music.PrevSong();          // n-1
music.SetN( 100 );         // jump

// Vibe knobs (the shareable subset of the config)
music.RerollVibe();                    // randomise the vibe knobs, keep per-instrument volumes
music.RerollVibe( includeVolumes: true, includeGenre: true ); // opt-in full shuffle (also rolls volumes + genre)
music.SetVibe( 0, 0.5f );              // set field 0 of VibeCodec.Fields(genre) from a 0..1 fraction
music.SetGenre( 1 );                   // switch genre (re-encodes the vibe so it sticks)
music.RandomEverySong = true;          // re-roll the vibe each new song (keeps your volumes + genre)

string seed = music.CurrentSeed;       // fully resolved — share this and they hear THIS song
var cfg     = music.EffectiveConfig(); // the MusicGen.Config currently in effect

// Write the current loop to a WAV under FileSystem.Data
string file = music.SaveCurrentToFile();
```

You can also generate audio without the component, off any thread:

```csharp
// The tag is the PRNG stream, "{station}:{n}". Build it with SeedCodec.SongSeed rather than by
// hand — it decides the station an empty tag falls back to, and every target must agree.
string seed = VibeCodec.SongSeed( "mytag", 0 );   // "mytag:0"

// One-shot WAV bytes
byte[] wav = MusicGen.Generate( seed, new MusicGen.Config() );

// Or raw interleaved-stereo 16-bit PCM
short[] pcm = MusicGen.GenerateSamples( seed, new MusicGen.Config(), out int sampleRate );
```

## Usage — the optional panel

If you want players to tweak the music in-game, add a **`SkafinityMusicPanel`**:

1. A GameObject with a **`ScreenPanel`** component (the UI root).
2. A child GameObject with **`SkafinityMusicPanel`** on it.

That's it. The panel auto-finds a `SkafinityPlayer` in the scene (or set its `Player`
property explicitly). The board's visibility is **host-driven** — it ships no launcher of its
own, so it imposes nothing on your HUD. Show it by setting `IsOpen` (or calling `Toggle()`)
from your game; bind that to a hotkey, your pause menu, or your own button. When open it
offers: now-playing seed + copy, prev/next, paste-a-seed, mute, volume, genre, per-instrument
vibe mixer, global knobs, reroll, "random every song", and save-to-`.wav`. Every control just
calls the player's public API, so anything the panel does you can do from code too.

**Re-theming — one colour.** The board is **neutral gray/black out of the box**, because a
drop-in library shouldn't impose a palette on your world. Give it your own accent and the whole
palette derives from it:

```csharp
SkafinityTheme.Accent = Color.Parse( "#ff8a3d" );   // any hue; null = back to neutral
```

Set it once at startup, or whenever your own theme changes — the panel folds it into its build
hash, so it re-renders. That is the entire API; there is nothing to override and nothing to edit,
which matters because **a vendored copy of this library should never be patched** — re-syncing
would blow the edit away. `UI/SkafinityMusicPanel.razor.scss` keeps only layout, type and radii.
Or skip the panel entirely and build your own UI against the same `SkafinityPlayer` API.

## Console commands

`SkafinityCommands.cs` registers a handful of commands so you can try all of this from the editor
without writing wiring code first — which you would otherwise have to, since the board has no
launcher and the accent is a static:

| Command | |
|---|---|
| `skafinity_spawn` | Build a throwaway player + board on a runtime GameObject, so you can try the library **in any scene without authoring one**. Never makes a second of anything — an existing board or player is reused. |
| `skafinity_despawn` | Remove that rig (and only that rig). |
| `skafinity_panel` | Open/close the board. **The only way to see it** before you've bound `IsOpen` — and it spawns the rig for you if the scene has no board, so this one command works from cold. |
| `skafinity_theme <hex\|clear>` | Retint live — exactly what setting `SkafinityTheme.Accent` does. |
| `skafinity_seed <seed>` | Play `vibe:tag:n`, `tag:n`, a bare `tag`, or `default`. |
| `skafinity_next` / `skafinity_prev` | Step the sequence. |
| `skafinity_genre <n>` | Switch genre; a junk index prints the roster. |
| `skafinity_reroll` | New genre + knobs, keeping your volumes. |
| `skafinity_save` | Write the playing song to a `.wav`. |
| `skafinity_status` | Seed, transport, and whether `skafinity.config.json` actually mounted. |
| `skafinity_explain` | What the composer decided — tempo, swing, key, changes, voicing, groove, form. |

They're client-side, like the player itself. Delete the file if you don't want them shipped.

## Key settings

| Group | What it does |
|---|---|
| **Music** | Master `Enabled` / `Volume`, `LiveReload` (regenerate on knob change), `MixerName`, `AutoPlay`, `RandomEverySong` (shuffle) |
| **Seed** | `Tag`, `StartN`, `Vibe` override, `PersistProgress` + `SaveSlot` (resume across sessions) |
| **Output** | `SampleRate` (32 kHz — below the engine's own default, since a game renders while it draws), `RenderThreads` (synthesis is split across worker threads) |
| **Crossfade** | `Crossfade` window, `CrossfadeOverlap`, `AheadCount` (look-ahead depth), `PcmCacheRadius` |
| **Genre** | `Genre` — 0 Ska-Punk · 1 Rock · 2 Country · 3 Metal · 4 Punk · 5 Pop. Prefer `SetGenre()` at runtime; a `Vibe` carries its own genre and otherwise wins. |
| Tempo / Mix / Tone / Feel / Stereo / Instrument / Horns / Guitars / Keys | The full generator knob set — see `MusicGen.Config` |

Every knob here mirrors a `MusicGen.Config` default, and the inspector value wins for any song
without a vibe — so **a value that differs from the engine's default is this component overriding
the shipped mix**. `SampleRate` is the only one meant to. The per-instrument levels in **Mix** are
`1.0` deliberately: the baseline balance between voices is set in `skafinity.config.json` below,
and a trim here rides on top of it.

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
`"{station}:{n}"`, where the station is the tag trimmed and lower-cased, or `"rotaliate"` when
it's empty — build it with `VibeCodec.SongSeed` rather than by hand, since that fallback is part
of what song an untagged seed *is*. Composition is the must-match part; the `Vibe` string
overrides the subset of knobs `VibeCodec` covers, the rest come from `MusicGen.Config` defaults.

`VibeCodec` is **genre-aware and append-only**. The vibe string is `[genre char][globals]
[instrument grid]`, the grid reserving up to 8 instruments × the WIRE columns at fixed positions
(`1 + globals + i*WireColumns + (c - WireFirstColumn)`). The globals block is currently empty and
column 0 (VOLUME) never travels — a listener's levels are a local preference, not part of the song
— so every char in a seed is a knob somebody can hear. Each of the six genres has its own
instrument grid; `Fields(genre)` is the per-genre list the UI iterates. Never reorder or remove —
only append instrument slots or columns — or existing shared seeds change meaning.

## License

Inherits the repository license (see `../../LICENSE`).
