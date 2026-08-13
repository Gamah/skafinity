## The seed format

```
tag:n[:genre][:vibe]
```

Four parts, two of them optional and **order-free**. `SeedCodec` (in `Code/Engine/`) is the only
parser on either target — the web asks the wasm rather than re-implementing it, because a seed is
precisely the thing two people have to agree about, and two parsers eventually disagree.

- **`tag`** — the station. `[A-Za-z0-9_-]` only; anything else (a colon, a space, a slash) is a
  parse **error**, never a coerced string. It is trimmed and lower-cased into a *station*, so
  `Gamah` and ` gamah ` are one station rather than three, and an empty tag is the fixed word
  `rotaliate` (`SeedCodec.Station`). That fallback is load-bearing on both targets: a host that
  spells it differently plays a different song from the same seed.
- **`n`** — the song index in that station's endless line, and it keeps the job it has always had:
  **Prev/Next are `n±1`**, the look-ahead queue walks an ordered timeline of them, and `n` is what
  lets you go back to a song fifty ago that nothing anywhere remembers — no history is stored,
  because none has to be. Optional in the string: a bare `tag` is song 0 of that station, which is
  how you go somewhere new by typing a word.
- **`genre`** — one hex char. One is enough until there are more than 16 genres, at which point it
  becomes two; that is a straightforward future change, not something to design for now.
- **`vibe`** — exactly `VibeCodec.VibeLength` hex chars (36 today). Anything else is an error.

The two optional parts are told apart by **length**, so their order does not matter: one char is a
genre, a full-width string is a vibe.

### Absent means rolled, present means pinned

An omitted genre or vibe is **derived deterministically from (tag, n)** and therefore changes with
every song — the station keeps being a station. A written-down one is **pinned** and every song
plays it. So:

| seed | what it does |
|---|---|
| `gamah:0` | a station: both genre and vibe roll, every song different |
| `gamah:0:3` | metal forever, vibes still rolling |
| `gamah:0:8a4c…` | one vibe, heard through whatever genre each song rolls |
| `gamah:0:3:8a4c…` | one song, exactly — move only `n` |

Genre and vibe roll from **separate PRNG streams** (`SeedCodec.GenreSeed` / `VibeSeed`, alongside
the composer's own `SongSeed`), so pinning one never moves the other: pin a vibe and the station
plays the genres it always did.

**Old seeds break.** That is the standing rule for this repo (see the top of `PLAN.md`) — there is
no back-compat shim and none is wanted.

### Nothing degrades

A malformed seed is refused with a sentence, and **nothing changes**: typed mid-session it leaves
the music playing, arriving from a link it leaves the widget silent. There is deliberately no
"apply the part I understood" path — half a seed is a song nobody asked for, and the person who
sent the link has no way to find out it landed wrong.

### The vibe is ONE GLOBAL GRID (`VibeCodec`)

```
[voice 0 cols 1..4][voice 1 cols 1..4]…      one hex digit per cell
```

`VoiceCount` × `WireColumns` = `VibeLength` characters, always, in every genre. The nine voices are
DRUMS, BASS, SKANK, ORGAN, MELODY, HORNS, KEYS, RHYTHM GTR, LEAD GTR, and **an instrument sits at
the same index whether or not the genre plays it**. Every cell is a fixed (Config field, range)
pair that no genre may redefine.

That is what makes a vibe portable, and portability is the whole reason for the shape: pin a vibe,
let the genre roll, and each song reads the same 36 numbers through whatever band it happens to be.
A per-genre grid — which is what this used to be — would make a pinned vibe nonsense the moment the
genre changed under it.

A genre chooses only **which cells it shows as sliders and what to call them**. Ska's `LEAD` is the
MELODY voice and pop's `LEAD` is the LEAD GTR voice; pop's `SYNTH` is rock's KEYS; pop hides the
DISTORTION cell on both of its voices because its voice code runs them clean. A hidden cell still
travels — every vibe is full width — so a genre change hears what was already there.

Everything a genre *sounds* like lives in its voice code and in `GenreProfile`. Guitar.cs and
Lead.cs already offset the drive per genre, which is why the knob's range does not need to.

- **The wire carries a normalised level** (0..15 over the cell's own range), not a raw value. Same
  fraction of travel, whatever the genre.
- **Column 0 is VOLUME and never travels.** It is a local mix preference, persisted per voice name
  and overlaid after the seed (`VibeCodec.ReadVolumes`/`ApplyVolumes`).
- **Lossy but stable**: `Encode(Apply(s)) == s` for any valid `s`, in every genre.
- **Growing the grid is a format break**, not an append: a voice or a fifth column changes
  `VibeLength` and invalidates every shared vibe. There is no append room left, on purpose — the
  old format's append-only rule bought back-compat nobody wanted and cost a genre-shaped wire.

The JS UI reads the field list — each field's `voice`, `column`, name and range — straight from the
wasm exports (`VibeFieldName/Min/Max/IsInt/Voice/Column/Choices`, genre-parameterised) and lays the
matrix out generically. There is no second field table: edit `VibeCodec.cs`.

### Shuffle is about the LINE, not the knobs

"A different vibe every song" needs no switch — it is what a seed with nothing pinned already does.
The shuffle toggle answers a different question: whether **next** means the next song of this
station (off) or a **whole new station at song 0** (on).

Those stations are *derived* (`SeedCodec.RollTagFor`), not drawn fresh, and position 0 is always the
root. Otherwise Prev could only work by remembering every station visited and a reload would lose
the lot; derived, a shuffled line is still just a seed.

---
