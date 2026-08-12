## Layout

```
skafinity/
  CLAUDE.md
  reference/              # read-only original C# (context only)
  sbox-library/Skafinity/ # the s&box library — Code/Engine/ is THE source
    skafinity.config.json # canonical shared house-mix config (make copies it to web/)
    Code/
      Engine/             # ← the framework-free engine; BOTH targets compile exactly this
        MusicGen.cs       #   core: per-song state, ctor, public entry points
        MusicGen.Config.cs#   every knob the composer + synth read
        Rng.cs            #   xmur3 → mulberry32, the root of every musical choice
        Harmony.cs        #   per-genre scale/progression/voicing/bass tables + degree→pitch
        Pattern.cs        #   THE RHYTHMIC UNIT: a figure with its own LengthTicks
        Melody.cs         #   THE TUNE: call-and-answer motifs per section type
        CompFigure.cs     #   the comp figures — what rhythm each genre's chordal voices play
        Structure.cs      #   Section, Part (energy/feel/key/tempo), the per-genre song forms
        GenreProfile.cs   #   per-genre character that is NOT a knob (form, grooves, accents…)
        Timing.cs         #   THE TIME BASE: ticks -> samples, tempo accumulator, swing
        Compose.cs        #   the composition pass (plan song → render sections)
        Expression.cs     #   per-note pitch shaping (vibrato/bend/glide/scoop)
        Master.cs         #   reverb, soft-clip, normalize
        Wav.cs            #   float mix → PCM, WAV container
        VibeCodec.cs      #   seed encoding + the AdvancedFields registry
        Voices/           #   Comp (dispatch), Bass, Skank, Lead, Keys, Guitar, Horns
        Drums/            #   Groove (DrumGroove tables + the kit/fill pass) + Kit (voices)
        Synth/            #   Patch, Notes (queue), Render, Osc
      SkafinityPlayer.cs  # s&box-only playback driver — outside the glob
      SkafinityCommands.cs# s&box-only console commands — the only way to try this target
      UI/                 # s&box-only Razor panel + its runtime palette — outside the glob
  docker/                 # Dockerfile + compose (nginx on loopback 6970; external Caddy fronts it)
  wasm/
    Skafinity.Wasm.csproj # browser-wasm project; <Compile Include>s the shared .cs
    Exports.cs            # [JSExport] boundary: generate, vibe codec, WAV, config <-> double[]
    runtimeconfig.template.json
  web/
    index.html            # the toy page — a header, a <skafinity-player>, a footer
    embed-light.html      # a light/Bootstrap-ish host page for the element (sniff demo)
    embed-dark.html       # a dark/hand-rolled host page for the element (sniff demo)
    engine.js             # boots .NET (with download progress), exposes the `mod` API
    skafinity-element.js  # <skafinity-player>: shadow root, UI, host-style sniffing
    player.js             # THE TRANSPORT: scheduling/look-ahead/timeline, headless + instanceable
    palette.js            # the palette derivation — a port of Code/UI/SkafinityTheme.cs, DOM-free
    app.js                # the toy page's host script (hash sync, light/dark switch) — ~60 lines
    queue.js              # the sequencer's generation queue — DOM-free so a node test can drive it
    worker.js             # generation worker (its own runtime instance)
    style.css             # the PAGE's chrome only; the widget styles itself in its shadow root
    config.json           # house-mix overlay fetched at startup (make-copied from sbox-library)
    _framework/           # published runtime bundle (committed; rebuilt by `make`)
  tools/
    bundle-single.mjs     # builds dist/skafinity.html (the whole runtime inlined into one file)
  test/
    smoke.mjs             # node smoke test of the JS↔wasm boundary
    page.mjs              # the mod.* surface the player/element/worker actually call
    queue.mjs             # the scheduler's generation queue (no wasm — runs on a bare checkout)
    player.mjs            # the headless transport, with engine/context/worker injected
    element.mjs           # <skafinity-player> against a stub DOM (no CSS engine — see its header)
    palette.mjs           # the palette, checked against the factors read out of SkafinityTheme.cs
    dist-single.mjs       # boots dist/skafinity.html's inlined runtime under node
    engine/               # engine-only C# harness (make test-engine) — runs without s&box
  Makefile
```

---

## Parity — one build, two targets

**The scope of "same seed ⇒ same song" is a single build.** Within one build the web and the
game must agree, because they compile the *same* `MusicGen.cs`/`VibeCodec.cs` — there is no
second implementation to drift. That is the whole parity guarantee, and it is structural: the
old "mirror the PRNG / draw order / Config defaults in C++" rules are obsolete; don't
reintroduce a hand-port.

**Audio is NOT stable across commits, and nothing should be written as if it were.** Engine
work is expected to change what a seed sounds like — that is the point of most of `PLAN.md`.
There is no golden-audio contract, no back-compat shim for old seeds, and a changed render is
not a regression. Don't add reproducibility machinery beyond what a refactor needs to prove
itself, and don't let it creep into the docs as a promise.

The places parity *within a build* can break:

- **Don't fork the engine.** Edit `sbox-library/Skafinity/Code/Engine/` once; never copy a
  file into `wasm/`. Both csprojs glob that folder.
- **The config round-trip.** The live `Config` crosses into JS as an opaque flat `double[]`
  (see `Cfg.To`/`Cfg.From` in `Exports.cs`). If you add a `Config` field that the vibe or a
  song depends on, add it to *both* `Cfg.To` and `Cfg.From` (and bump `Cfg.Size`), or edits
  to it won't survive the boundary.
- **`float` vs `double`.** Keep `MusicGen`'s `float`/`double` exactly as-is; the wasm runtime
  matches .NET semantics, so leave them be.
