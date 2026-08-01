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
| 88 | The lead gets buried, and sits an octave low in some genres | Listening note after the tune + mix work. Two suspects, and they compound. **Level:** the mix rebalance measured the comp against the kit and pulled the lead down with it (`LeadLevel()` in `Lead.cs` — 0.45 punk, 0.50 country, 0.65 ska/rock), which was right when the lead played the odd phrase and is wrong now that it carries the tune. The lead is the melody: it should sit ON TOP, a few dB over the kit, not level with it. Re-measure with `--levels` against a target of roughly +2 dB rather than 0. **Register:** `LeadBase()` puts the line at `_rootMidi + 21` (rock) or `+24` (everything else) over a root of MIDI 28–35, so a rock lead lives around MIDI 49–64 — the same octave as the rhythm guitar's chords and the keys, which is exactly where a melody disappears. Metal's shred sits at +26 and reads fine, which is the clue. Try +33/+36 for the guitar-lead genres and check it against the comp registers (`triBase = _rootMidi + 12`, keys `+24`) so the tune has an octave of its own. Both are cheap to try; do the register first, since a line in the wrong octave cannot be fixed with gain. |
| 14 | Non-4/4 | `Timing` already carries `BeatsPerBar` and a `BeatGrouping` array (nothing reads the grouping yet), `ComposePlan` picks the meter in one place, and patterns are meter-agnostic now — a `Pattern` carries its own tick length and nothing in a voice assumes eight cells to a bar. 6/8 and 12/8 first (12/8 is the natural home of a one-drop shuffle; a country bass lesson in 6/8 turned up in the research). Compound meter wants a `BeatTicks` on `Timing` (48 simple, 72 compound) rather than a second grid. Changing meter *within* a song is the section map's job — `Part` already carries per-bar beat counts for anomalous measures, so it is the same mechanism widened. |
| 6 | `make dist` — single-file bundle | The one non-musical thing still deferred, and the Makefile currently exits 1 with a note. A true self-contained `.html` needs the whole .NET runtime + assemblies base64-inlined (multi-MB). Until it exists the toy is only distributable as a served directory, which is fine for the hosted site but not for "hand someone a file". |
