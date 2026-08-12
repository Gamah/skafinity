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
where the grid reserves up to `MaxInstruments` (8) blocks of one char per **wire column**.
A row has 4 columns (volume / tone / character / extra) but only columns
`WireFirstColumn`..3 travel: **column 0 is VOLUME**, a local mix preference persisted per voice
rather than part of the song's identity, so the whole column is skipped instead of being encoded
as filler. **The global block is empty** — every knob that ever lived there turned out to belong
to `GenreProfile` or to the house config, and the block sits in front of the grid, so putting one
back would shift every position and invalidate every shared seed (see the note in `VibeCodec.cs`).
Between them that is why a seed has no fixed run of `0`s in it: ska is 22 chars, metal 13, and
each one is a knob.

Wire column `c` of instrument `i` lives at `1 + globals + i*WireColumns + (c - WireFirstColumn)`,
so adding a genre, an instrument, or a 5th column never shifts an existing position.
**Append-only means**: append instrument slots (≤ 8), and only ever append columns past the
last — never reorder/remove. A retired knob *inside a row* still holds its cell (filler char, and
`Pop` has two). `Apply` ignores
trailing positions a shorter string lacks (older/other-genre seeds degrade gracefully). Each
genre defines its own instrument grid (Ska-Punk 6 instruments, Rock 4). The JS UI reads the field
list — including each field's `voice`/`column` — straight from the wasm exports
(`VibeFieldName/Min/Max/IsInt/Voice/Column/Choices`, all genre-parameterized) and lays out
the matrix generically, so there's no second field table to keep in lockstep — just edit
`VibeCodec.cs`.

---
