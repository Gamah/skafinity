# skafinity — C#-as-source ska engine compiled to WebAssembly with the .NET wasm-tools
# workload (the same MusicGen.cs / VibeCodec.cs the s&box library ships, no port).
#
#   make            → publish the engine, stage build/_framework for the web layer
#   make dev        → same, but skip AOT (much faster to build; identical composition)
#   make test       → node smoke test of the JS↔wasm boundary (needs build/)
#   make serve      → static server rooted at repo so web/ can fetch build/
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

.PHONY: all dev stage test serve dist clean

all:
	$(DOTNET) publish $(PROJECT) -c Release
	@$(MAKE) --no-print-directory stage

# Faster iteration: interpreted runtime (no AOT). Composition/output are identical; only
# the per-sample synthesis loop runs slower.
dev:
	$(DOTNET) publish $(PROJECT) -c Release -p:RunAOTCompilation=false
	@$(MAKE) --no-print-directory stage

# Copy just the runtime bundle the page loads (web/engine.js imports ../build/_framework).
stage:
	rm -rf build/_framework
	mkdir -p build
	cp -r $(PUBDIR) build/_framework
	@echo "staged build/_framework ($$(ls build/_framework | wc -l) files)"

test:
	$(NODE) test/smoke.mjs

serve:
	@echo "serving on http://localhost:$(PORT)/web/  (Ctrl-C to stop)"
	python3 -m http.server $(PORT)

# A true single self-contained .html needs the whole .NET runtime + assemblies base64-inlined
# (multi-MB) — deferred. For now the toy is the served bundle (web/ + build/_framework).
dist:
	@echo "dist (single-file inline of the .NET runtime) is not implemented yet — serve the"
	@echo "bundle with 'make serve'. See PLAN/README for the follow-up."
	@exit 1

clean:
	rm -rf build wasm/bin wasm/obj
