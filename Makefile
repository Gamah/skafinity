# skafinity — C#-as-source ska engine compiled to WebAssembly with the .NET wasm-tools
# workload (the same MusicGen.cs / VibeCodec.cs the s&box library ships, no port).
#
# ── Docker (the deploy/serve path — no local .NET needed) ──
#   make up         → build the wasm bundle in Docker + serve it (nginx, container
#                     skafinity-1, host 127.0.0.1:6970 — loopback so it stays behind ufw)
#   make rebuild    → rebuild the image from scratch (no cache) and restart
#   make down       → stop and remove the container
#   make logs       → follow the container logs
#   make ps         → container status
#
# ── Local (.NET SDK on the host) ──
#   make            → publish the engine, stage web/_framework for the web layer
#   make build      → compile-only typecheck of the shared C# (no publish/stage) — the fast
#                     synth-check after editing MusicGen.cs / VibeCodec.cs / Exports.cs
#   make dev        → same as all, but skip AOT (much faster to build; identical composition)
#   make deploy     → clean, verified release build: wipes stale artifacts, full AOT
#                     publish, then runs the smoke test (the cruft-free bundle to ship)
#   make test       → node smoke test of the JS↔wasm boundary (needs web/_framework/)
#   make serve      → static server rooted at web/ (quick no-Docker preview; `make up` is
#                     the real, nginx-parity host)
#   make dist       → (follow-up) single-file bundle; see note below
#   make clean
#
# One-time setup: Docker (for `make up`), or dotnet-sdk-10.0 + `dotnet workload install
# wasm-tools` for the local targets.

DOTNET   ?= dotnet
# Resolve the binary (command -v skips a stale `node` *directory* an emsdk PATH may shadow it
# with). Override with `make test NODE=/path/to/node` if needed.
NODE     ?= $(shell command -v node)
PROJECT   = wasm/Skafinity.Wasm.csproj
PUBROOT   = wasm/bin/Release/net10.0/publish
PUBDIR    = $(PUBROOT)/wwwroot/_framework
PORT     ?= 8000
COMPOSE   = docker compose -f docker/docker-compose.yml

# Bare `make` stays the local publish (the docker targets below are first in the file but
# are opt-in via `make up`).
.DEFAULT_GOAL := all

.PHONY: all build dev deploy stage test serve dist release clean up rebuild down logs ps

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
# stays incremental while the staged bundle only ever holds the canonical files.
all:
	rm -rf $(PUBROOT)
	$(DOTNET) publish $(PROJECT) -c Release
	@$(MAKE) --no-print-directory stage

# Synth check: compile-only (no AOT, no publish, no web/_framework staging). The fast path
# after editing MusicGen.cs / VibeCodec.cs (or the Cfg boundary in Exports.cs) — it
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

test:
	$(NODE) test/smoke.mjs

serve:
	@echo "serving on http://localhost:$(PORT)/  (docroot web/; Ctrl-C to stop)"
	python3 -m http.server $(PORT) -d web

# A true single self-contained .html needs the whole .NET runtime + assemblies base64-inlined
# (multi-MB) — deferred. For now the toy is the served bundle (web/, incl. web/_framework).
dist:
	@echo "dist (single-file inline of the .NET runtime) is not implemented yet — serve the"
	@echo "bundle with 'make serve'. See PLAN/README for the follow-up."
	@exit 1

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
