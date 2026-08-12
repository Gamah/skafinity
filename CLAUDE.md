# CLAUDE.md — skafinity

**skafinity** = *ska* + *infinity*. A web toy that streams an **endless, deterministic
song** — ska, rock, country, metal, punk or pop — generated entirely in the browser from a short
shareable seed. No server, no audio assets — the music is synthesised from scratch in
WebAssembly and scheduled through the Web Audio API.

**This file is what every session needs; the deep reference lives in `docs/` and is read on
demand.** Every section that moved keeps its heading here and names the file it went to —
`docs/layout.md` (the tree, and what parity does and does not guarantee), `docs/testing.md`
(the harness, the diagnostics, what is measured and how), `docs/genre-profile.md` (per-genre
character, harmony, tempo, the kit and the cymbals, plus the house mix), `docs/composition.md`
(patterns, the tune, the arranger, section state), `docs/seed-format.md`. Nothing was rewritten
on the way out, so a heading in `docs/` is the same text it always was. **Read the file a
section points at before working in that area** — the pointer is a signpost, not a summary of
the rules.

---

## Origin & the single source of truth

The generator comes from the **Rotaliate s&box client** procedural-music engine. It now
lives here as a standalone s&box library under `sbox-library/Skafinity/` — and **that C# is
the single source of truth for both the game and this web toy**. The web build compiles the
*same* files to WebAssembly; there is no separate port to keep in sync.

| File | Role |
|---|---|
| `sbox-library/Skafinity/Code/Engine/` | **The engine — the spec.** The composer + subtractive synthesiser, split one file per concern (see the tree below). `MusicGen` is a `partial class` across the folder. **This folder is the unit both targets compile**: `wasm/Skafinity.Wasm.csproj` globs `Engine/**/*.cs` and s&box globs it implicitly, so the folder boundary is the thing that keeps the s&box-only driver and UI out of the web build. |
| `sbox-library/Skafinity/Code/Engine/MusicGen.cs` | Engine core — per-song state, constructor, public entry points (whole-song + chunked). Start here. |
| `sbox-library/Skafinity/Code/Engine/VibeCodec.cs` | Base-36 encoding of the "vibe" knobs → the shareable seed fragment. Also holds the `AdvancedFields` registry — the baseline-mix knobs that are config-only (NOT in the seed or the sliders). |
| `test/engine/` | Engine-only test harness (`make test-engine`) — compiles `Engine/**` alone into the same assembly as the tests, so it runs on a plain dev host where s&box cannot. The safety net for engine work. |
| `sbox-library/Skafinity/skafinity.config.json` | The single shared **house-mix config** (peak balances / kit presence). Canonical here; the s&box plugin reads it at runtime and `make` copies it to `web/config.json`. Edit it to retune the baseline mix without a rebuild. |
| `sbox-library/Skafinity/Code/SkafinityPlayer.cs` | The s&box playback driver (`SoundStream`, infinite `tag:n`, look-ahead, crossfade). Web equivalent is `web/app.js`; the s&box-only bits are not used on the web. |
| `sbox-library/Skafinity/Code/UI/SkafinityMusicPanel.razor` (`.scss`) | Optional drop-in Razor `PanelComponent` — finds a `SkafinityPlayer` and exposes its knobs as in-game UI (seed/prev-next, genre, per-instrument vibe mixer, mute/volume, reroll, save). s&box-only; not in the web build. |
| `sbox-library/Skafinity/Code/UI/SkafinityTheme.cs` | The panel's palette, derived at RUNTIME from one `Accent` colour so a consuming game can retint a *vendored* copy without editing it. Unset = neutral gray/black. |
| `reference/*.cs` | The original Rotaliate-client copies, kept for context. **Read-only.** The `sbox-library` copies are what actually compile. |

Everything under `Code/Engine/` is framework-free (only `System` / `System.Collections.Generic`
/ `System.Text`) — that's *why* it compiles to wasm unchanged. Keep it that way: **no s&box
(`Sandbox.*`) types and no web/Emscripten-isms anywhere in `Engine/`.** Anything web-specific
belongs in `wasm/Exports.cs`; anything s&box-specific belongs in `Code/SkafinityPlayer.cs` or
`Code/UI/`, which are outside the glob. Adding a file to `Engine/` is all it takes to ship it
to both targets — there is no file list to update.

If you change the engine, edit the `sbox-library` copy (both targets pick it up). When in
doubt the C# is right.

---

## Why this is a good web toy

- The synthesis is pure integer/float math with a portable PRNG, AOT-compiled to native
  wasm — runs far faster than real time, so we pre-render whole ~75 s loops on demand.
- A whole song is its seed (`vibe:tag:n`), so **the entire experience is a URL**. Share
  `…/web/#vibe:bd44ac2a:23` and the other person hears the exact same song.
- The web has real `<input type=range>` sliders (s&box did not), so the vibe editor is
  nicer here than in the game.

---

## Stack

| Layer | Tech |
|---|---|
| DSP / composition | **C#** (`net10.0`), compiled to **WebAssembly** via the .NET `wasm-tools` workload (`Microsoft.NET.Sdk.WebAssembly`, `RunAOTCompilation`) |
| JS boundary | `[JSExport]` in `wasm/Exports.cs`; `web/engine.js` boots the runtime and adapts the exports |
| Glue / UI | Vanilla **JS + HTML + CSS** (no framework, no bundler) |
| Audio | **Web Audio API** — `AudioBufferSourceNode`s scheduled with gain-ramp crossfades |
| Distribution | A **served** bundle — the self-contained `web/` (which includes `web/_framework`). `make dist` repackages it two ways: a GitHub-Pages-ready `dist/`, and `dist/skafinity.html` with the whole runtime inlined. Both need http; neither works from `file://`. |
| Deploy | Docker Compose (`make up`) — SDK build stage → nginx runtime stage; host reverse proxy (Caddy) fronts it with TLS |

C# is the choice because it *is* the source — same code, two targets, zero port.

---

## Layout

Moved to `docs/layout.md`.

## Parity — one build, two targets

Moved to `docs/layout.md`.

## Testing
Moved to `docs/testing.md`.

## Genre character vs. knobs (`Engine/GenreProfile.cs`)

Moved to `docs/genre-profile.md`.

## House-mix config (runtime, NOT in the seed)

Moved to `docs/genre-profile.md`.


## The time base (`Engine/Timing.cs`)

Musical positions are **integer ticks**, absolute from the song's first downbeat, at
`TicksPerBeat = 48`. Voices take a `barTick` and ask `Timing` for samples; they never compute
sample offsets themselves. Three rules keep it honest:

- **Ticks are metrical, samples are physical.** `TickToSample(tick)` is the only bridge. 48
  renders every subdivision in use exactly (8ths, 16ths, 8th- and 16th-note triplets) — do not
  "simplify" to a 16th grid, that silently deletes every triplet.
- **Grid positions shuffle; tuplets are even.** `TickToSample` applies the swing warp, for
  notes the band lands on together. A tuplet divides its *own span* into equal parts and uses
  `EvenSpan(startTick, spanTicks, frac)`, which warps only the endpoints. A shuffle is itself
  a triplet feel, so a triplet must not be warped a second time on top of it.
- **Tempo is an accumulator, not a multiply.** `Timing` walks a per-tick sample delta across
  the song. With one tempo that equals a multiply; the point is that a per-section tempo or an
  ending ritard is a matter of varying the delta, not a rewrite. Keep it that way.

Durations are spans, not positions: `SamplesForTicks`/`SecondsForTicks` carry no swing.
`DrumPush` (the kit's push/lay-back) stays in continuous sample space — it is a feel, not a
grid position.

**The tempo accumulator is now actually curved, so note LENGTHS have to read it.** The song's
per-tick delta varies with the section's `TempoMul` and ramps over the final bars (the ending
ritard). A duration therefore has a position: use `SpanSamples(fromTick, ticks)` /
`SpanSeconds(fromTick, ticks)`, which measure through the accumulator. `SamplesForTicks(ticks)`
is the *nominal*-tempo span and will cut a note short in a slowing section — it is kept for
spans with no position. Size buffers off `Timing.TotalSamples` (the finished accumulator), never
off `ticks × nominal`, and remember the ring-out tail is a number of seconds the ritard outruns
(it is scaled by `RitardAmount` in `ComposePlan` for exactly that reason).

---


## Patterns — the rhythmic unit (`Engine/Pattern.cs`)

Moved to `docs/composition.md`.

## The tune (`Engine/Melody.cs`)
Moved to `docs/composition.md`.

## One authority arranges the section (`Engine/Arrange.cs`)
Moved to `docs/composition.md`.

## Sections carry state (`Engine/Structure.cs`, `SongForm`)
Moved to `docs/composition.md`.

## The seed format

Moved to `docs/seed-format.md`.

## Audio scheduling (replaces s&box SoundStream)

`web/player.js` keeps the controller's model over Web Audio. **It is a class, not a page** — the
transport was pulled out of `app.js` so a second copy of it can exist, which is what
`<skafinity-player>` (`web/skafinity-element.js`, see `docs/embedding.md`) is built on and what the
toy page itself now consumes. Anything that reaches for the document, the URL or an un-namespaced
storage key belongs above it, not in it, and `test/player.mjs` asserts exactly that.

- `engine.js`'s `generateSong(seed, cfg)` renders **one full structured song** (stereo) —
  intro → chorus → verse(0) → chorus → verse(1) → chorus → ending (see `BuildStructure` in
  `MusicGen.cs`). PCM stays in wasm memory and comes back as a MemoryView the worker copies
  into two `Float32Array`s (valid only synchronously — copy immediately).
- JS wraps each song in an `AudioBuffer`. Because the song has an intro/ending it **plays
  once** (`LoopsPerSong` = 1, `src.loop = false`), then **equal-power crossfades** into the
  pre-rendered next song (seed `tag:(n+1)`).
- **Look-ahead:** keep `AheadCount` songs pre-rendered in a **Web Worker** (its own runtime
  instance) so a render never janks the UI.
- Persist `n` in `localStorage` so playback resumes.

**A song is CLAIMED before it is rendered, and dropping the claim is what dropping the work means.**
`web/queue.js` holds the one rule the scheduler cannot get wrong: `claimed === queued ∪ in-flight`.
A claim is released only when a render lands, so any code path that discards queued work has to
release its claims with it (`dropQueued`) — a claim with no queue entry and no worker behind it is
permanent, and `want()` will then refuse that index forever. The timeline is walked in order, so one
stranded index stops playback for the rest of the session rather than skipping a song. That is why
the queue is a DOM-free object with its own node test (`test/queue.mjs`, in `make test`) instead of
two collections inline in the scheduler: everything else in it needs a browser, and this does
not. The same asymmetry applies to workers — `seekTo` terminates only renders that fall outside the
cache window (a Prev/Next is still rendering songs the timeline wants, and a terminate costs a
runtime reboot), while `startSequence` abandons everything. And `activeNodes` is what is still
*playing*: a finished source removes itself in `onended`, because it holds its `AudioBuffer` — a
whole song's PCM — for as long as the list does.

Browsers require a user gesture before audio — `AudioContext.resume()` is gated on the play
button.

**There are two restart paths and picking the wrong one is silent.** `startSequence` is the HARD
one — it drops the PCM cache, the frozen-vibe ledger and every in-flight render, because the cfg
behind every index may have changed. `seekTo` is the SOFT one: it keeps all of that and only
re-schedules. Web Audio nodes cannot be rewound, so a pause, a resume and a scrub inside a song are
all "tear the nodes down and start the same buffer at an offset" — i.e. all SOFT. A resume routed
through the hard path still *works*, which is why it is worth naming: it just answers ▶ with a
several-second re-render of a song already in memory and a playlist full of progress bars. What
decides is `dirty`, set by the knob/genre/seed paths when they cannot restart because nothing is
playing, and cleared by `startSequence`.

`position()` is arithmetic on the audio clock (`ctx.currentTime - startTime + offset`), not
something the engine is asked for, and `duration` is 0 until the song is rendered — an unknown
length is reported as unknown rather than assumed, because songs differ in length.

**A fade UP FROM SILENCE is not a crossfade, and must not borrow its length.** A crossfade is long
because two songs have to trade places without either being heard to stop; nothing is being traded
at the start of a session. `SkafinityPlayer` used one number for both, and what a multi-second linear
ramp does to a drum kit is specific rather than merely quiet: it crushes the STRIKE and lets the RING
arrive at full level a second later, so every cymbal in the opening bars sounds like it was hit
before the song started. That is the diagnosis for "the song starts with a cymbal already ringing" —
and the tell that it is a HOST bug rather than an engine one is that the engine's own render of the
same seed starts on a clean attack (check it with `--render` and look at the first few milliseconds,
not at a block envelope, which cannot tell an attack from a mid-decay start).

---

## The embeddable element (`web/skafinity-element.js`)

Full reference in **`docs/embedding.md`** (attributes, parts, custom properties, cross-origin). The
three facts a session needs before touching the web layer:

- **The toy page is a CONSUMER of the element, not a second implementation.** `web/index.html` is a
  header, a `<skafinity-player>`, and a footer; `web/app.js` is the host script that syncs
  `location.hash` and drives the page's light/dark switch. If the widget breaks, the flagship page
  breaks with it — which is deliberate, because the alternative is a demo nobody looks at.
- **The palette is derived, never shipped.** `web/palette.js` is a port of
  `Code/UI/SkafinityTheme.cs` and `test/palette.mjs` reads the factors back out of that C# — so the
  s&box panel and the web widget cannot drift into two colour schemes. Light mode is those same
  factors reflected (`1 - f`, `Scale`↔`Mix`), not a second scheme. Add a token in one place and the
  test tells you about the other.
- **Nothing is fetched until the first play**, in the element AND on the toy page. A widget that
  costs every visitor 7.5 MB on page view is not embeddable, so `preload` is opt-in and the boot
  reports real bytes through a progress bar.

`web/embed-light.html` and `web/embed-dark.html` are two deliberately opposite host pages (light and
Bootstrap-ish; dark, serif and hand-rolled) carrying the same unconfigured element. They are the only
way to see whether the sniff is working — no test can look at it, and `test/element.mjs` runs against
a stub DOM that has no CSS engine and says so.

## Deploy (`make up`) — loopback only, Caddy fronts it

Mirrors the rotaliate/gambit/splitclicker convention: `docker/docker-compose.yml` pins the
compose project (`name: skafinity`, container `skafinity-1`) so it can't collide with the
other repos whose compose file also lives under `docker/`, and publishes

```
127.0.0.1:6970:80
```

**Never bind `0.0.0.0` and never `ufw allow 6970`.** Docker writes its own iptables chains
which are evaluated *before* ufw, so a bare `6970:80` publish is internet-reachable even
with ufw denying the port. Loopback binding + a host-side reverse proxy is the entire
mechanism; the host Caddyfile (unversioned, not in this repo) does TLS and the http→https
redirect. 6970 is skafinity's allocation on that host — `1337`, `5432`–`5436`, `6969`,
`8080`, `8081` belong to sibling services; check the host's Caddyfile before taking a new one.

**Two ways in, same container.** `web/_framework` is committed, so the bundle usually
already exists on disk and there is nothing to compile:

- **`make fast`** (`docker-compose.fast.yml`) — stock `nginx:1.27-alpine` bind-mounting
  `web/` read-only. No build stage, up in a second, and host-side edits to the page/glue are
  live on reload. **The everyday target.**
- **`make up`** (`docker-compose.yml`) — builds the wasm bundle from source in the image
  first (~2 min). For when `MusicGen.cs` / `VibeCodec.cs` / `Exports.cs` changed, or to prove
  the build still works.

They share the compose project, container name, port and `nginx.conf`, so they are
alternatives rather than a pair — starting one replaces the other, and `down`/`logs`/`ps`
act on whichever is running. `fast` guards on `web/_framework/dotnet.js` and fails with an
instruction rather than serving a 404 page if the bundle was never built.

The built image is two-stage: `mcr.microsoft.com/dotnet/sdk:10.0` installs `wasm-tools` and
publishes the bundle, then `nginx:1.27-alpine` serves `web/` plus the freshly-built
`_framework` — **no .NET at runtime**. `.dockerignore` excludes `web/_framework` so a stale
committed/local bundle can never leak into the image, and the Dockerfile re-copies the
canonical `skafinity.config.json` over `web/config.json` for the same reason (it is the
image's equivalent of `make stage`). Because everything is baked in, there are no volumes,
no `.env`, and no secrets.

## Packaging (`make dist`) — two artifacts, both served over http

`make dist` repackages the already-built `web/`; it never compiles anything. It guards on
`web/_framework/dotnet.js` the way `fast` does. **Both outputs are gitignored — commit the
target, never the artifacts.**

**`dist/` — the GitHub Pages payload.** It is deliberately not `cp -r web dist`, for three
reasons and no others:

- **`.nojekyll` — the trap.** GitHub Pages runs the published tree through Jekyll, which
  **excludes directories whose name starts with an underscore**. That is exactly `_framework/`.
  Without the zero-byte `.nojekyll` at the root, the entire runtime is silently missing from the
  deployed site and the page dies at boot on a 404 for `dotnet.js` — with nothing in the build
  log to say so. This is documented Jekyll behaviour, not something observed on a live deploy.
- It drops the `*.br`/`*.gz` duplicates, which a plain static host never serves.
- It re-copies `config.json` from the canonical `sbox-library/Skafinity/skafinity.config.json`,
  so a hand-edited `web/config.json` can never ship. It is the deploy-path `stage`.

**Every path in `web/` is relative** (`href="style.css"`, `fetch('./config.json')`,
`new URL('./worker.js', import.meta.url)`), so a project page's `/<repo>/` subpath needs no
rewriting. Keep it that way — an absolute path is what would break the Pages deploy.

**`dist/skafinity.html` — one file, ~9.5 MiB.** Built by `tools/bundle-single.mjs`. The runtime's
boot config is already embedded in `dotnet.js` (it ends in `withConfig({…resources…})`), so the
only question is where the bytes come from, and `dotnet.withResourceLoader(fn)` answers it. Three
facts that whole design rests on — all `[SOURCE]`, read out of the published `dotnet.js` on
2026-08-01, i.e. implementation detail rather than documented contract, so **re-read them if a
runtime bump breaks the build**:

- For the `dotnetjs` type the loader **must** return a URL *string* (it asserts, then `import()`s
  it) → the two runtime js modules become `data:text/javascript` URLs.
- For every other behaviour it may return a `Promise<Response>`, returned as-is → the five wasm
  assets come from a synthesized `Response` over the inlined bytes. That path skips `fetch`
  entirely, and SRI is only ever applied to *fetch options*, so **no hash check runs** and
  `disableIntegrityCheck` never has to be touched.
- The loader is minified to one- and two-letter top-level names and declares a top-level `var`,
  and `app.js` has top-level names of its own (`n` among them). Concatenating them into one
  module scope is a redeclaration `SyntaxError`, so the loader goes in an **IIFE** (a bare block
  would not contain the `var`) and returns the builder instead of exporting it.

**The workers are why this is not just "base64 everything into app.js".** Each `Worker` boots its
own runtime instance; re-parsing ~10 MB of base64 three times is not acceptable. The worker is a
**blob-URL module** carrying loader + engine + worker glue and *no* assets, and the main thread
posts it the decoded bytes **without a transfer list** — a structured-clone copy, so this realm
keeps its own runtime alive. The worker's boot awaits that init message.

**Every rewrite in the bundler is anchored on an exact source pattern and hard-fails if it stops
matching.** Renaming something in `app.js`/`engine.js`/`worker.js` should break `make dist` loudly
— a silently mis-rewritten bundle is a page that dies at boot for whoever was handed it, and there
is no server log to find it in.

**What `make test-dist` proves and what it can't.** It boots the artifact's own worker bundle
under node, on the real `loadBootResource` path, and renders a song — so the resource wiring, the
synthesized Responses, the concatenation and the asset handoff are genuinely exercised. Two
node-only substitutions are made and are stated in the test: the worker bundle and the two runtime
modules are hosted from `file:` rather than `data:`/`blob:` URLs, because emscripten's and the
loader's `ENVIRONMENT_IS_NODE` branches call `createRequire(import.meta.url)`, which node rejects
for a data: URL. A browser evaluates neither branch. **Untested without a browser:** the real
blob-URL `Worker`, `AudioContext` playback, the DOM wiring in `app.js` (the bundle is only
parse-checked), and any actual GitHub Pages deploy.

## Pages (`.github/workflows/pages.yml`) — the live site

`https://gamah.github.io/skafinity/`, deployed by Actions on every push to `master`. The workflow
runs `make dist` and `test/dist-single.mjs` on a stock runner and publishes `dist/`, so the live
tree cannot drift from what `make dist` builds on a dev box, and a single-file bundle that fails to
boot fails the job instead of being served.

**The job does not compile the engine, and that is the thing to remember.** A Pages runner has no
.NET and no `wasm-tools` workload; installing them would add ~2 min per deploy and would put audio
on the site that came from a build nobody listened to. It consumes the **committed**
`web/_framework`. So a change to `Code/Engine/**`, `wasm/Exports.cs` or `web/engine.js` is live only
once you have run a full local publish and committed the re-staged bundle — the same
bundle-matches-glue rule as ever, except Pages makes forgetting it *invisible* rather than loud:
the site keeps serving the old engine while `master` claims the new one. Page-only edits
(`index.html`, the embed demos, `app.js`, `player.js`, `palette.js`, `skafinity-element.js`,
`style.css`, `config.json`) need no publish; push and the site follows.

**The stale-bundle gate.** Because the deploy packages rather than compiles, an engine commit
that forgets to re-stage `web/_framework` breaks nothing visibly — no 404, no failing test, just
the old engine playing under a `master` that claims otherwise. `make stage` therefore writes
`web/.bundle-stamp` (`<kind> <sha256>` over every `.cs` the wasm build compiles, plus the csproj
and `runtimeconfig.template.json`), and `make check-bundle` recomputes it. The CI workflow and
the Pages deploy both run that check first, so a stale bundle fails the merge and never reaches
the site.

**CI runs on PULL REQUESTS and `preview/*`, deliberately not on every branch push.** Re-staging
the bundle is a ~2-minute AOT publish that belongs at the end of a piece of work, so a feature
branch's intermediate commits are EXPECTED to carry a stale one — running the gate on them mails
the repo owner about a state that is not a defect, and a gate that cries wolf stops being read.
Nothing reaches master except through a PR and nothing reaches the live site except master or a
`preview/*` branch, so the gate still covers everything it was written to cover. If you want the
answer earlier on a WIP branch, run `sh tools/bundle-stamp.sh check` locally — it is the same
script CI calls. `kind` exists because `make dev` stages an interpreted runtime — fresh but slow in a
browser — so the check demands `aot` and a `dev` stamp fails too. **Both `stage` and
`check-bundle` call the same `tools/bundle-stamp.sh`**, which is the point: two implementations
of "what counts as a source" would drift and the gate would quietly stop gating. Add a compiled
input (a new `Engine/` subfolder, another glob in the csproj) and it goes in `compute()` there.
What the stamp proves is that the bundle was staged from these sources; it is a guard against
forgetting, not against tampering, since it sits beside the bundle rather than being derived
from its bytes.

**A branch can be previewed live, and while it is, the site is not `master`.** `pages.yml` carries
`workflow_dispatch`, so the Actions UI offers a ref picker; what decides whether the `deploy` job
may then run is the `github-pages` environment's deployment branch policy, and it admits `master`
and `preview/*` and nothing else. Push a branch named `preview/<something>`, dispatch the workflow
against it, and that build is the live site. **The prefix is the whole safety** — an unrestricted
policy would let any branch that ever acquires a Pages trigger replace the site, so a preview has
to be named as one.

**There is ONE Pages site per repo, so a preview REPLACES the live site rather than sitting beside
it**, and it keeps serving until `master` next deploys. `concurrency: group: pages` with
`cancel-in-progress: false` queues rather than cancels, so the next push to `master` restores the
site instead of racing the preview; nothing else has to be done to put it back. The consequence to
carry: **the live site can legitimately be a branch, so "the site does not match `master`" is not
by itself a stale-bundle bug.** Read the deployment log for the ref that last deployed before
reaching for `web/_framework` — a live preview and a forgotten re-stage look identical from the
outside.

Two properties are load-bearing and easy to undo by accident. The runner must stay
`ubuntu-latest`: standard runners are free on a public repo, **larger runners are billed even
there**. And Pages must stay on the *Actions* source rather than branch-deploy — with branch-deploy
GitHub only serves a branch's root or `/docs`, never `web/`, and it would run Jekyll over the tree
(see the `.nojekyll` trap above). `dist/` is generated and gitignored; nothing built is committed.

## Conventions

- No build framework beyond `make`. `make` → publish + stage `web/_framework`; `make dev`
  skips AOT for speed; `make serve` → `python3 -m http.server` rooted at `web/` (a quick
  no-Docker preview — `make up` is the real nginx-parity host). `make test-engine` → the
  engine-only C# tests (the check that runs on a bare dev host). `make test` → the node
  tests (wasm boundary, page surface, scheduler queue, transport, element, palette — the last
  four need neither wasm nor a browser). `make fast`/`up`/`rebuild`/`down`/
  `logs`/`ps` drive the container. `make dist` → the two handout artifacts (above);
  `make test-dist` boots the single-file one.
- **The page must be served** (http), not opened via `file://` — the runtime is a fetched
  bundle, and inlining it does not change that: `dist/skafinity.html` needs http too (module
  scripts and `data:`/`blob:` imports off a `file://` origin). `web/` is self-contained (it
  includes `web/_framework`), so any static server can serve it with the docroot pointed
  straight at `web/`. `web/_framework` is committed so a clone is testable without the SDK.
- Keep `MusicGen.cs` / `VibeCodec.cs` framework-free; web-specific code goes in `Exports.cs`.
- The house-mix config has ONE canonical copy (`sbox-library/Skafinity/skafinity.config.json`);
  `make`'s `stage` step copies it to `web/config.json`. Edit the canonical and re-`make`, or edit
  `web/config.json` directly for quick web-only iteration (the next `make` overwrites it).
