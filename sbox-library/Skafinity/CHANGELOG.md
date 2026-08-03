# Skafinity — changelog

Versions track the [skafinity repo's GitHub releases](https://github.com/Gamah/skafinity/releases).
This file covers the **s&box library surface** — the engine's audio changes are summarised at the
bottom of each entry rather than enumerated, because a seed's render is expected to change between
versions and is never treated as a contract.

---

## v0.5.0

### Added

| | |
|---|---|
| `SkafinityTheme` (`Code/UI/`) | **New.** The panel's palette, derived at runtime from one `Accent` (`Color?`). Unset = neutral gray/black. `Bg`, `Cell`, `CellFill`, `CellFillSoft`, `AccentCss`, `AccentBg`, `Text`, `TextDim` are CSS strings the panel binds inline. |
| `SkafinityCommands` (`Code/`) | **New.** Nine client-side console commands: `skafinity_spawn` / `_despawn` / `_panel` / `_theme` / `_seed` / `_next` / `_prev` / `_genre` / `_reroll` / `_save` / `_status` / `_explain`. **Delete this file if you don't want them in your shipped game.** |
| `SkafinityPlayer.MasterReverb` | `[Property, Group("Stereo"), Range(0,1)]`, default `0.5`. The GLOBAL `REVERB` vibe knob previously had no inspector resting value at all. |
| `SkafinityPlayer.HouseConfigCount` | `public int` — how many values were read from `skafinity.config.json`. **Zero means it never mounted**, so the baseline mix is compiled defaults rather than the shared file. Nothing else reports this. |
| `SkafinityPlayer.ExplainCurrent()` | `public string` — what the composer decided for the playing song (tempo, swing, key, changes, voicing, groove, form, and which cymbal each section's hand is on). Re-plans the song, so expect a hitch. |
| `VibeCodec.SongSeed( tag, n )` | `public static string` — the PRNG stream a song is composed from. Use this rather than building `"{tag}:{n}"` by hand; see *Fixed*. |
| `MusicGen.Config.RideBalance` | New field (default `0.407`), a new `AdvancedFields` entry, and a new `skafinity.config.json` key. The ride previously shared `HatBalance`. |

### Removed

Nothing was removed from the public API.

**But the SCSS palette is gone**, and that is breaking if you had overridden it. `SkafinityMusicPanel.razor.scss` no longer declares `$bg / $btn / $accent / $text / …`; it keeps layout, type, radii and **all borders**. Colour is now runtime — re-theme with `SkafinityTheme.Accent`, not by editing the vendored copy.

### Changed — inspector

| Property | Was | Now | Why |
|---|---|---|---|
| `Genre` | `Range( 0, 1 )` | `Range( 0, 5 )` | Six genres ship. Four were unreachable from the inspector. |
| `KeysVol/Cutoff/Drive/Chug`, `RhythmGtr*`, `LeadGtr*` | `Group( "Rock" )` | `Group( "Guitars / Keys" )` | Not rock-only: every genre but ska comps on KEYS or RHYTHM GTR, ska's choruses play RHYTHM GTR, and LEAD GTR is the lead wherever the genre isn't horn-led. |
| `RhythmGtrDrive` | `Range( 1, 5 )` | `Range( 1, 6 )` | Metal's grid reaches 6. |
| `LeadGtrDrive` | `Range( 1, 5 )` | `Range( 1, 11 )` | Rock and punk floor this knob at 5; the old range couldn't reach the value the vibe grid uses. |

### Changed — defaults (audible)

These were the component **overriding the engine's shipped mix**. A player property that differs from its `MusicGen.Config` default is this component disagreeing with the song; `SampleRate` (32000 vs 44100) is now the only one that legitimately does, and says so.

| | Was | Now |
|---|---|---|
| `SnareVol` | `0.70` | `1.00` |
| `TomVol` | `0.60` | `1.00` |
| `HatVol` | `0.22` | `1.00` |
| `CrashVol` | `0.35` | `1.00` |
| `PanAmount` | `0.4` | `1.0` |
| `LeadGtrDrive` | `3.6` | `5.0` |

The kit trims predated both the kit rebuild and the measured `*Balance` entries in `skafinity.config.json`, so they were double-dipping against them. **The kit is noticeably louder than 0.4.0** — retune in the shared config, not here.

> ⚠️ A `[Property]` default only applies to *new* components. If you have a `SkafinityPlayer` authored into a scene or prefab, it carries the old values serialized and will keep them — reset the component. Games that build the player at runtime (the `LocalMusicSystem` / `LocalHud` pattern) are unaffected.

### Changed — house mix (`skafinity.config.json`)

| Key | Was | Now |
|---|---|---|
| `HatBalance` | `0.407` | `0.288` |
| `RideBalance` | *(did not exist)* | `0.407` |

`RideBalance` is seeded from `HatBalance`'s old value, so the split changed nothing on its own.

### Fixed

- **Untagged seeds were a different song in-game than on the web.** The player spelled the empty-tag station `"skafinity"` while the engine and the web both spell it `"rotaliate"`, so `vibe::23` — and the panel's "Use default" — resolved to a station the web cannot reach. One definition now (`VibeCodec.SongSeed`).
- **`Array.Clone()` is not on the s&box API whitelist** (`SB1000`), so the shared engine did not compile in s&box. Replaced. It is an explicit deny entry in `Sandbox.Access/Rules/Types.cs`, not an omission — `Array.Sort`/`Empty`/`Copy` are all fine.
- **Songs began with a cymbal that sounded pre-struck.** The first song faded up from silence over the *crossfade* window (3.75 s), which crushes the strike and lets the ring arrive at full level a second later. The fade from silence is now its own much shorter number.
- **The panel had no way to be seen.** It ships no launcher, so a freshly-added `SkafinityMusicPanel` rendered nothing until a host bound `IsOpen`. `skafinity_panel` now builds a throwaway player + `ScreenPanel` + board in any scene — and never a second of anything if the scene already has one.
- The reroll message claimed it kept the genre. It has never kept the genre.

### Audio

Expected to differ from 0.4.0 — no golden-audio contract. Open hats ring shorter, hats are 3 dB back, and the ride was rebuilt: level down, its tail given a knee so it stops darkening as it rings, and its strike moved from 1.2 kHz to 4.2 kHz so it sits above the guitars rather than inside them. **The cymbals are explicitly not finished** — the crash has not been touched and is now the least-examined voice in the kit. See `PLAN.md` row 40.
