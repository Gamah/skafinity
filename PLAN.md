# PLAN

**Everything here must keep working in s&box.** The engine is shared source, not a port:
`sbox-library/Skafinity/Code/Engine/**` compiles into both the s&box library and the web wasm bundle.
So every row below is bound by the same rules — the engine stays framework-free (`System`,
`System.Collections.Generic`, `System.Text` only; no `Sandbox.*`, no Emscripten-isms), anything
web-specific goes in `wasm/Exports.cs`, and any new `Config` field lands in `Cfg.To`/`Cfg.From`
(+ `Cfg.Size`) or it won't survive the JS boundary. `SkafinityPlayer.cs` and `Code/UI/` are
s&box-only and must still compile and drive the engine after each row.

Seed compatibility is **not** preserved: old `vibe:tag:n` seeds are expected to break, and the
audio a seed renders is expected to change from commit to commit. That is accepted — no
back-compat shim, no golden-audio contract, and a changed render is never a regression. The
only reproducibility that matters is *within* a build (web and s&box agreeing, and a pure
refactor proving it moved nothing).

| Rank | Item | Notes |
|---|---|---|
| 90 | The backing still loops one figure all song | Listening note after the per-genre comp work: the backing instruments still read as one repeated "short — longggg — bop" cell for the whole song. The mechanism is visible in `ComposePlan`: `_compFig`, `_keysFig` and `_bassPat` are drawn ONCE PER SONG, so a 1- or 2-bar figure is genuinely all the listener ever hears — the only things that change across sections are energy, displacement and the hemiola. `Pattern` already supports the fix; what is missing is a per-SECTION draw (off the section's own stream, so a repeated chorus still repeats) and/or a variation pass that swaps the figure's last bar. Also suspect the figures themselves: several are authored with one long span and one short one, which is exactly the "short-longggg" shape — the note LENGTH comes from `Hit.SpanTicks` (ticks to the next onset), so an uneven figure produces an uneven note, every bar, forever. Check whether comp notes should cap their length rather than always running to the next onset. |
| 86 | Re-balance the comp voices in the mix | Same listening note, other half: the backing may simply be too loud, which would make a repetitive figure far more obvious than it is. `SkankBalance`/`KeysBalance`/`RhythmGtrBalance` were peak-tuned for the OLD parts — a chop on every offbeat, an every-eighth chug — and the parts have changed underneath them (real riff figures, held pads, strums with a spread, chords now carrying 4–5 voices instead of 3 because of the new 7th/9th/add9 voicings, each divided by `notes.Length` but summing differently). Re-measure per-voice peaks per genre and retune the balances; they are `AdvancedFields`, so this is `skafinity.config.json` and needs no rebuild to try. Do this one FIRST — if the mix was the problem, row 90 gets much easier to judge by ear. |
| 14 | Non-4/4 | `Timing` already carries `BeatsPerBar` and a `BeatGrouping` array (nothing reads the grouping yet), `ComposePlan` picks the meter in one place, and patterns are meter-agnostic now — a `Pattern` carries its own tick length and nothing in a voice assumes eight cells to a bar. 6/8 and 12/8 first (12/8 is the natural home of a one-drop shuffle; a country bass lesson in 6/8 turned up in the research). Compound meter wants a `BeatTicks` on `Timing` (48 simple, 72 compound) rather than a second grid. Changing meter *within* a song is the section map's job — `Part` already carries per-bar beat counts for anomalous measures, so it is the same mechanism widened. |
| 6 | `make dist` — single-file bundle | The one non-musical thing still deferred, and the Makefile currently exits 1 with a note. A true self-contained `.html` needs the whole .NET runtime + assemblies base64-inlined (multi-MB). Until it exists the toy is only distributable as a served directory, which is fine for the hosted site but not for "hand someone a file". |
