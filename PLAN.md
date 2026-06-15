# PLAN.md — flip the engine to C#-as-source, compile to WASM

> **This is a temporary working doc. Delete `PLAN.md` before this work is approved for
> upstream** (it captures the migration plan + scratch decisions, not durable project docs;
> the durable bits land in `README.md` / `CLAUDE.md`). Deleting it is a checklist item in
> "Definition of done" below.

## Decision

C# becomes the **single source of truth**. The hand-maintained C++ port (`src/`,
`bindings.cpp`, the `emcc` path) is dropped. The same `MusicGen.cs` + `VibeCodec.cs` that
the s&box plugin uses are compiled to WebAssembly and driven by `web/app.js`, so the web toy
and the s&box library run *identical* composition code — no port, no PRNG-parity test, no
duplication to keep in sync.

This supersedes the old C++/WASM port plan that used to live in this file. The cardinality /
golden-vector concerns from `CLAUDE.md` become moot for the web target (one source → both
targets), so they no longer gate this work.

Why this is viable: `MusicGen.cs` and `VibeCodec.cs` are already framework-free (only
`System` / `System.Collections.Generic` / `System.Text`). Only `SkafinityPlayer.cs` touches
s&box, and that's the scheduling driver (the analog of `app.js`), not the portable core.

## Build dependencies (machine, not vendored)

Nothing lands in the repo — SDK + workload live in system/SDK dirs, NuGet restores to
`~/.nuget`. On this Ubuntu 24.04 box:

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0      # .NET 10 LTS, from Ubuntu's repo
sudo dotnet workload install wasm-tools       # browser-wasm build workload (NuGet-sourced)
```

Fallback if the distro SDK refuses `dotnet workload install` ("not supported in this SDK"):

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0
export PATH="$HOME/.dotnet:$PATH"
dotnet workload install wasm-tools
```

Already present and reused: Node, Python3 (wasm build internals). `~/emsdk` / `emcc` is no
longer needed — the workload bundles its own emscripten.

## Steps

1. **`wasm/` .NET project** (`browser-wasm` RID, `net10.0`). It `<Compile Include>`s
   `../sbox-library/Skafinity/Code/MusicGen.cs` + `VibeCodec.cs` directly (shared source, not
   copies). `.gitignore` `bin/`, `obj/`.
2. **JS boundary** — a thin `Exports.cs` with `[JSExport]`:
   - `Generate(string seed, double[] cfg) -> float[]` (interleaved stereo PCM) + a sample-rate
     getter,
   - `EncodeVibe(double[] cfg) -> string`, `DecodeVibe(string, ...)`, `VibeFields()` (drives the
     UI), `ParseSeed(string)`.
   Mirror the field ordering the current `app.js` expects so the UI changes stay small.
3. **`web/app.js`** — boot the .NET runtime (`dotnet.create()` → `getAssemblyExports`) and call
   the exports instead of the emscripten module. Web Audio scheduling (look-ahead, crossfade,
   rolling playlist) is unchanged. Worker generation stays a worker.
4. **Retire C++** — delete `src/`, `bindings.cpp`, `test/` (C++ parity test); rewrite the
   `Makefile`: `make` → `dotnet publish -c Release` into `build/`; `make serve` unchanged;
   `make dist` → bundle (see caveat).
5. **Docs** — update `README.md` + `CLAUDE.md`: C# is source of truth, new build deps, new
   layout. (`CLAUDE.md`'s C++/parity sections get rewritten, not just trimmed.)
6. **Compile + commit the output** to the branch so it's testable. Branch + PR last.

## Caveat — "the .html"

.NET wasm output is a **bundle** (`index.html` + `_framework/dotnet.js` + `dotnet.wasm` +
assemblies) that must be **served** (`make serve`), not opened via `file://`. Commit the
published bundle so it's immediately testable. A single self-contained `skafinity.html` (like
today's emcc `dist`) needs extra base64-inlining of the runtime + assemblies and produces a
multi-MB file — treat that as a follow-up unless asked for it in this PR.

## Status / blocked on

- [x] Confirmed core (`MusicGen.cs`, `VibeCodec.cs`) is framework-free.
- [ ] **Blocked: install the toolchain above** (sudo needs a password here; user runs it).
- [ ] Everything in Steps 1–6 once the SDK + workload are present.

## Definition of done

`make serve`, open the page, hear endless crossfading ska; vibe sliders work; paste a
`vibe:tag:n` and get the same arrangement; the s&box plugin and the web toy share one source.
Then: **delete this `PLAN.md`**, fold any durable notes into `README.md` / `CLAUDE.md`, and
open the upstream PR.
