# skafinity — C#-as-source ska engine compiled to WebAssembly with the .NET wasm-tools
# workload (the same MusicGen.cs / VibeCodec.cs the s&box library ships, no port).
#
#   make            → publish the engine, stage web/_framework for the web layer
#   make dev        → same, but skip AOT (much faster to build; identical composition)
#   make deploy     → clean, verified release build: wipes stale artifacts, full AOT
#                     publish, then runs the smoke test (the cruft-free bundle to ship)
#   make test       → node smoke test of the JS↔wasm boundary (needs web/_framework/)
#   make serve      → static server rooted at web/ (same docroot you'd give nginx)
#   make dist       → (follow-up) single-file bundle; see note below
#   make clean
#
# One-time setup: dotnet-sdk-10.0 + `dotnet workload install wasm-tools`.

DOTNET   ?= dotnet
# Resolve the binary (command -v skips a stale `node` *directory* an emsdk PATH may shadow it
# with). Override with `make test NODE=/path/to/node` if needed.
NODE     ?= $(shell command -v node)
PROJECT   = wasm/Skafinity.Wasm.csproj
PUBDIR    = wasm/bin/Release/net10.0/publish/wwwroot/_framework
PORT     ?= 8000

.PHONY: all dev deploy stage test serve dist clean

all:
	$(DOTNET) publish $(PROJECT) -c Release
	@$(MAKE) --no-print-directory stage

# Faster iteration: interpreted runtime (no AOT). Composition/output are identical; only
# the per-sample synthesis loop runs slower.
dev:
	$(DOTNET) publish $(PROJECT) -c Release -p:RunAOTCompilation=false
	@$(MAKE) --no-print-directory stage

# Ship build: `dotnet publish` reuses wasm/bin and never prunes old hashed assemblies, so an
# incremental `make` can leave stale .wasm cruft in the staged bundle. `deploy` clears
# wasm/bin + wasm/obj first so the AOT publish regenerates only the canonical files, then
# runs the smoke test so the staged web/ is verified before it goes out.
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
	@echo "staged web/_framework ($$(ls web/_framework | wc -l) files)"

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

clean:
	rm -rf web/_framework wasm/bin wasm/obj
