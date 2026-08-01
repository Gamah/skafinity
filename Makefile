# skafinity — C#-as-source ska engine compiled to WebAssembly with the .NET wasm-tools
# workload (the same Code/Engine/ the s&box library ships, no port).
#
# ── Docker (the deploy/serve path — no local .NET needed) ──
#   make fast       → serve the COMMITTED web/ (incl. web/_framework) with stock nginx —
#                     no build, starts in a second. The everyday target.
#   make up         → build the wasm bundle from source in Docker, then serve it (~2 min).
#                     Use after an engine change, or to prove the bundle rebuilds.
#                     Both: nginx, container skafinity-1, host 127.0.0.1:6970 — loopback so
#                     it stays behind ufw.
#   make rebuild    → rebuild the image from scratch (no cache) and restart
#   make down       → stop and remove the container (either flavour)
#   make logs       → follow the container logs
#   make ps         → container status
#
# ── Local (.NET SDK on the host) ──
#   make            → publish the engine, stage web/_framework for the web layer. ~2 min on an
#                     8-core box, and the cost is irreducible native work: the emcc -O2 compile
#                     of aot-instances (~32 s), the emcc link + wasm-opt (~25 s), the Mono AOT
#                     pass (~14 s) and the trimmer (~10 s). See CLAUDE.md for the full table —
#                     it has been measured, so don't re-guess at it.
#   make build      → compile-only typecheck of the shared C# (no publish/stage) — the fast
#                     synth-check after editing Code/Engine/ or Exports.cs
#   make dev        → same as all, but skip AOT: ~22 s instead of ~2 min, and composition is
#                     identical (only the per-sample synthesis loop runs slower in the browser).
#                     THE INNER LOOP — reach for it while iterating and run a full `make` before
#                     you commit web/_framework.
#   make deploy     → clean, verified release build: wipes stale artifacts, full AOT
#                     publish, then runs the smoke test (the cruft-free bundle to ship)
#   make test       → node tests of the JS↔wasm boundary AND the page's engine surface
#                     (needs web/_framework/)
#   make test-engine→ engine-only C# tests: compile Code/Engine/ alone and assert on
#                     composition (PRNG, harmony, structure, vibe codec, WAV). Needs no
#                     s&box, no wasm workload and no browser, so it is the check that
#                     actually runs on a dev host — use it after any engine change.
#   make serve      → static server rooted at web/ (quick no-Docker preview; `make up` is
#                     the real, nginx-parity host)
#   make dist       → package for handing out: dist/ (a GitHub-Pages-ready static site)
#                     plus dist/skafinity.html (ONE self-contained file, runtime inlined)
#   make test-dist  → build dist/ and boot the single file's inlined runtime under node
#   make clean
#
# One-time setup: Docker (for `make up`), or dotnet-sdk-10.0 + `dotnet workload install
# wasm-tools` for the local targets.

# Resolve the toolchains once. Both fall back to a shared ~/.local/share/toolchains/ copy when
# the host has none on PATH, so a dev box without a system-wide .NET or node still runs the
# targets. Override either explicitly: `make test NODE=/path/to/node`.
DOTNET   ?= $(shell command -v dotnet || echo $(HOME)/.local/share/toolchains/dotnet10/dotnet)
# command -v skips a stale `node` *directory* an emsdk PATH may shadow the binary with. The
# emscripten pack also ships a node, but it is v18 and too old for the ESM dotnet.js — don't
# reach for that one.
NODE     ?= $(shell command -v node || ls -d $(HOME)/.local/share/toolchains/node*/bin/node 2>/dev/null | tail -1)
PROJECT   = wasm/Skafinity.Wasm.csproj
ENGINE_TESTS = test/engine/Skafinity.EngineTests.csproj
PUBROOT   = wasm/bin/Release/net10.0/publish
PUBDIR    = $(PUBROOT)/wwwroot/_framework
PORT     ?= 8000
COMPOSE   = docker compose -f docker/docker-compose.yml
FASTCOMP  = docker compose -f docker/docker-compose.fast.yml

# Bare `make` stays the local publish (the docker targets below are first in the file but
# are opt-in via `make up`).
.DEFAULT_GOAL := all

.PHONY: all build dev deploy stage test test-engine test-engine-bless test-dist serve dist release clean fast up rebuild down logs ps

# ── Docker: serve the committed bundle as-is. web/_framework is in the repo, so there is
# nothing to compile — stock nginx bind-mounts web/ and is up in a second. Same container,
# port and nginx.conf as `up`; the only difference is where the bundle came from. ──
fast:
	@test -f web/_framework/dotnet.js || { \
		echo "web/_framework is missing — nothing to serve." >&2; \
		echo "Build it with 'make up' (Docker) or 'make' (local .NET SDK)." >&2; exit 1; }
	$(FASTCOMP) up -d
	@echo "skafinity-1 up (committed bundle, no build) — http://127.0.0.1:6970/"

# ── Docker: build the wasm bundle inside the image and serve it with nginx. The container
# is skafinity-1 and the port is loopback-bound (127.0.0.1:6970) so Docker's iptables rules
# can't punch through ufw — a host reverse proxy fronts it publicly. ──
up:
	$(COMPOSE) up -d --build
	@echo "skafinity-1 up — http://127.0.0.1:6970/  (loopback; front it with your host proxy)"

rebuild:
	$(COMPOSE) build --no-cache
	$(COMPOSE) up -d

down:
	$(COMPOSE) down

logs:
	$(COMPOSE) logs -f

ps:
	$(COMPOSE) ps

# Wipe the publish OUTPUT dir first: `dotnet publish` never prunes old content-hashed
# assemblies, so re-publishing into a dirty dir accumulates stale *.wasm that `stage` then
# copies into web/. Clearing just $(PUBROOT) (not obj/) keeps the AOT cache, so the rebuild
# stays incremental while the staged bundle only ever holds the canonical files — measured at
# zero: an unchanged re-publish is 9 s with or without the rm, because everything it deletes is
# re-copied out of obj/.
all:
	rm -rf $(PUBROOT)
	$(DOTNET) publish $(PROJECT) -c Release
	@$(MAKE) --no-print-directory stage

# Synth check: compile-only (no AOT, no publish, no web/_framework staging). The fast path
# after editing Code/Engine/ (or the Cfg boundary in Exports.cs) — it
# typechecks the shared C# and catches every compile error without rebuilding the bundle.
build:
	$(DOTNET) build $(PROJECT) -c Release

# Faster iteration: interpreted runtime (no AOT). Composition/output are identical; only
# the per-sample synthesis loop runs slower.
dev:
	rm -rf $(PUBROOT)
	$(DOTNET) publish $(PROJECT) -c Release -p:RunAOTCompilation=false
	@$(MAKE) --no-print-directory stage

# Ship build: a full from-scratch rebuild + smoke test. `all` already wipes the publish dir
# so the staged bundle is cruft-free on every build; `deploy` goes further and clears
# wasm/bin + wasm/obj (the AOT cache too) for a guaranteed-clean release, then runs the smoke
# test so the staged web/ is verified before it goes out.
deploy:
	@$(MAKE) --no-print-directory clean
	@$(MAKE) --no-print-directory all
	@$(MAKE) --no-print-directory test
	@echo "deploy: clean AOT bundle staged in web/ and smoke test passed"

# Copy just the runtime bundle the page loads (web/engine.js imports ./_framework). Staging
# it under web/ keeps the page self-contained: point any static server's docroot at web/.
stage:
	rm -rf web/_framework
	cp -r $(PUBDIR) web/_framework
	cp sbox-library/Skafinity/skafinity.config.json web/config.json
	@echo "staged web/_framework ($$(ls web/_framework | wc -l) files) + web/config.json"

# Two halves: smoke.mjs checks the raw [JSExport] boundary; page.mjs checks the surface the
# PAGE uses (the `mod` object engine.js returns, against every call app.js/worker.js make).
# Both derive what they expect from the source, so a new export or a new mod.* call is covered
# without editing a list here.
test:
	$(NODE) test/smoke.mjs
	$(NODE) test/page.mjs

# Engine-only tests: compile Code/Engine/ alone (no s&box, no wasm workload, no browser) and
# assert on composition — PRNG determinism, harmony maths, song structure, the vibe codec,
# the WAV container. This is the safety net for engine work and the one test that runs on a
# bare dev host, so reach for it before `make test`.
test-engine:
	$(DOTNET) run --project $(ENGINE_TESTS) -c Release

# Re-record the render digests. They are a tripwire for refactors that are meant to be PURE
# (bless before the change, `make test-engine` after, expect silence) — NOT a promise that
# audio is stable across commits. Re-bless in the same commit as any deliberate audible change.
test-engine-bless:
	$(DOTNET) run --project $(ENGINE_TESTS) -c Release -- --bless

serve:
	@echo "serving on http://localhost:$(PORT)/  (docroot web/; Ctrl-C to stop)"
	python3 -m http.server $(PORT) -d web

# Two shippable artifacts out of the already-built web/ bundle. Both need web/_framework, so
# guard on it the way `fast` does rather than assembling a payload that 404s at boot.
#
#   dist/            — a GitHub-Pages-ready static site. It is not `cp -r web dist`, and it
#                      earns its own target for exactly three reasons:
#                        • .nojekyll — Pages runs Jekyll, which EXCLUDES directories whose name
#                          starts with an underscore. Without that file `_framework/` is
#                          silently dropped from the published site and the page dies at boot
#                          with a 404 on dotnet.js. This is the single trap in deploying it.
#                        • no *.br/*.gz — a plain static host serves the uncompressed files and
#                          the duplicates are dead weight.
#                        • config.json is re-copied from the CANONICAL
#                          sbox-library/Skafinity/skafinity.config.json, so a hand-edited
#                          web/config.json can never ship. This is the deploy-path equivalent
#                          of `stage` (and of what the Dockerfile does for the image).
#                      Every path in web/ is relative, so a /<repo>/ project-page subpath needs
#                      no rewriting.
#   dist/skafinity.html — ONE self-contained file: page, glue and the whole .NET runtime
#                      inlined. ~10 MB (base64 of a 7 MB runtime). Serve it over http — it is
#                      NOT a file:// artifact. It rides inside dist/ so the deployed site also
#                      offers the standalone as a download.
dist:
	@test -f web/_framework/dotnet.js || { \
		echo "web/_framework is missing — nothing to package." >&2; \
		echo "Build it with 'make' (local .NET SDK) or 'make up' (Docker)." >&2; exit 1; }
	rm -rf dist
	mkdir -p dist
	cp web/index.html web/app.js web/engine.js web/worker.js web/style.css dist/
	cp sbox-library/Skafinity/skafinity.config.json dist/config.json
	mkdir -p dist/_framework
	find web/_framework -maxdepth 1 -type f ! -name '*.br' ! -name '*.gz' -exec cp {} dist/_framework/ \;
	touch dist/.nojekyll
	$(NODE) tools/bundle-single.mjs dist/skafinity.html
	@echo "dist/ ready ($$(du -sh dist | cut -f1)) — publish it as the Pages branch/folder root."

# Boot the single-file artifact's inlined runtime under node and render a song. Browser-only
# bits (the blob-URL Worker, AudioContext, the DOM) are out of reach here and say so in the
# test's own comments; run it after any change to web/*.js or tools/bundle-single.mjs.
test-dist: dist
	$(NODE) test/dist-single.mjs

# Package the runtime bundle the web layer loads — engine.js + worker.js + the staged
# _framework (minus the brotli/gzip duplicates a plain static server doesn't use) — into a
# release tarball for downstream vendoring. rotaliate fetches the latest release of this
# asset at `make up` and wraps it into its music screen, so the game and the web toy run the
# identical composition engine. Run `make deploy` first so web/_framework is a clean AOT
# build, then `make release`, then `gh release create vX.Y.Z $(RELEASE_TARBALL)`.
RELEASE_TARBALL ?= skafinity-web.tar.gz
release:
	@test -f web/_framework/dotnet.js || { echo "web/_framework missing — run 'make deploy' first" >&2; exit 1; }
	rm -f $(RELEASE_TARBALL)
	tar -czf $(RELEASE_TARBALL) --exclude='*.br' --exclude='*.gz' \
		-C web engine.js worker.js _framework
	@echo "packaged $(RELEASE_TARBALL) ($$(du -h $(RELEASE_TARBALL) | cut -f1))"
	@echo "publish with: gh release create vX.Y.Z $(RELEASE_TARBALL) --title ... --notes ..."

clean:
	rm -rf web/_framework wasm/bin wasm/obj
