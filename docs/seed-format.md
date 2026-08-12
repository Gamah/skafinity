## The seed format

`vibe:tag:n` (same as the game's `SkafinityPlayer.CurrentSeed`):

- `tag` — any string (a name, a word). It seeds the PRNG together with `n`: the per-song PRNG
  seed string is **`"{tag}:{n}"`** (empty tag ⇒ `"rotaliate"`).
- `n` — song index in the infinite sequence (0, 1, 2 …). Prev/Next step `n`.
- `vibe` — a base-36 string at **16 levels/knob** (`VibeCodec.Levels`), encoding the genre + knob
  overrides. The **first char is the genre** (0 = Ska-Punk, 1 = Rock, 2 = Country, 3 = Metal, 4 = Punk, 5 = Pop); the rest
  follow the fixed wire grid below. Empty/absent ⇒ default knobs (genre 0).

Parsing (in `web/engine.js`, `parseSeed`) mirrors the controller: accept `vibe:tag:n`,
`tag:n`, or `tag`. The page reads a seed out of `location.hash` at boot and then clears it; the
widget's copy button is what builds a shareable link back out of the seed that is playing
(`share-base` in `docs/embedding.md`).

### VibeCodec wire format (genre-aware, append-only)

The wire layout is **genre-independent**: `[genre char][global block][instrument grid]`,
where the grid reserves up to `MaxInstruments` (8) blocks of 4 columns
(volume / tone / character / extra). Column `c` of instrument `i` always lives at
`1 + globals + i*4 + c`, so adding a genre, an instrument, or a 5th column never shifts an
existing position. **Append-only means**: append global knobs, append instrument slots
(≤ 8), and only ever append columns past the 4th — never reorder/remove. `Apply` ignores
trailing positions a shorter string lacks (older/other-genre seeds degrade gracefully). Each
genre defines its own instrument grid (Ska-Punk 6 instruments, Rock 4). The JS UI reads the field
list — including each field's `voice`/`column` — straight from the wasm exports
(`VibeFieldName/Min/Max/IsInt/Voice/Column/Choices`, all genre-parameterized) and lays out
the matrix generically, so there's no second field table to keep in lockstep — just edit
`VibeCodec.cs`.

---
