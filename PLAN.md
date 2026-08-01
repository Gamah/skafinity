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
| 80 | Make `make test-engine` fast enough to iterate on | It is ~3–4 minutes a run and the loop (bless → change → run) is 30–40 minutes, which is now the main cost of engine work. It is almost entirely RENDERING, and the fix is mostly to render less and to render at once: **(1) one render per genre instead of one per voice** — the mix-balance and grid checks solo each of 9 voices in each of 6 genres, so 54 full songs are rendered where 6 would do if `NoteEvent` carried a voice tag and the checks filtered events by it (that also makes `--levels`/`--grid` instant). **(2) Parallelise across songs** — every render is independent and the RNG is per-instance, so `Parallel.ForEach` over the digest matrix and over the genres is safe today; the engine also already supports intra-song parallelism (`BeginPlan` → `RenderPitchedRange` windows → `FinishStereo`), which the harness does not use. **(3) The digest matrix renders at the default 44.1 kHz** — halving that to 22.05 kHz halves the cost of the slowest section and only needs a re-bless (the digests are a within-build tripwire, not a contract). **(4)** `dotnet run` rebuilds first; a `--no-build` path after an explicit build saves ~10 s a cycle. The host has 8 cores and the harness uses one, so (2) is worth real money — but (1) is the bigger win and makes the diagnostics usable interactively. |
| 14 | Non-4/4 | `Timing` already carries `BeatsPerBar` and a `BeatGrouping` array (nothing reads the grouping yet), `ComposePlan` picks the meter in one place, and patterns are meter-agnostic now — a `Pattern` carries its own tick length and nothing in a voice assumes eight cells to a bar. 6/8 and 12/8 first (12/8 is the natural home of a one-drop shuffle; a country bass lesson in 6/8 turned up in the research). Compound meter wants a `BeatTicks` on `Timing` (48 simple, 72 compound) rather than a second grid. Changing meter *within* a song is the section map's job — `Part` already carries per-bar beat counts for anomalous measures, so it is the same mechanism widened. |
| 6 | `make dist` — single-file bundle | The one non-musical thing still deferred, and the Makefile currently exits 1 with a note. A true self-contained `.html` needs the whole .NET runtime + assemblies base64-inlined (multi-MB). Until it exists the toy is only distributable as a served directory, which is fine for the hosted site but not for "hand someone a file". |
