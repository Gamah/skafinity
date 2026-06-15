# TODO — Skafinity s&box library

WIP. The library is ported and self-contained but has **not been compiled or run inside the
s&box editor yet** — it was extracted by hand from `../rotaliate-client`. Verify before relying
on it.

## Must do before trusting it

- [ ] **Open in the s&box editor** and confirm `Code/Skafinity.csproj` compiles. s&box
      regenerates the csproj `Reference`/`Analyzer`/`OutputPath` lines on first load; confirm
      that happens and the build is clean (`SoundStream`, `SoundHandle`, `GameTask`,
      `Sandbox.Audio.Mixer`, `FileSystem.Data` all resolve).
- [ ] Add `SkaMusicPlayer` to a scene, hit play, confirm audio streams and **crossfades**
      between songs without a gap or click at the loop boundary.
- [ ] Confirm `RenderThreads > 1` actually parallelises (no audible artifacts vs. `=1`) and
      no worker burst trips the ~1000ms no-yield advisory.
- [ ] Test `SaveCurrentToFile()` writes a valid WAV under `FileSystem.Data`.
- [ ] Test `PersistProgress` round-trips `n` across a stop/start.

## Parity

- [ ] Capture golden vectors (first ~32 `Rng.Next()` floats + chosen bpm/scale/progression/
      instrument for a few `tag:n`) from the original C# and assert this copy reproduces them.
      The source files were copied verbatim except the namespace, so this should hold — pin it.
- [ ] Decide whether to keep this in sync with `../rotaliate-client/Code/Audio/*` by hand or
      script the copy. `MusicGen.cs` / `VibeCodec.cs` are byte-identical aside from
      `namespace Rotaliate.Audio;` → `namespace Skafinity;`.

## Nice to have

- [ ] Editor buttons (`[Button]`) on `SkaMusicPlayer` for Reroll / Next / Prev / Save.
- [ ] A tiny example scene + a minimal demo UI panel (kept out of the library proper).
- [ ] Publish via Library Manager (Org `gamah`, Ident `skafinity`) once verified.

## Notes

- Game-specific bits intentionally dropped from the original `MusicController`: `PlayerData`
  persistence, `LobbyNetworkManager` admin-follow, world on/off + volume settings, and all UI.
  Progress persistence is reimplemented opt-in via `FileSystem.Data` (`PersistProgress`).
- `VibeCodec.Fields` is append-only — its order is the wire format.
