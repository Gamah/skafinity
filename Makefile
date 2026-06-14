# skafinity — C++/WASM ska engine.
#   make        → build the WASM module into build/ (needs emscripten / emcc)
#   make test   → native g++ build of the parity/smoke test (no emcc needed)
#   make serve  → static server rooted at repo so web/ can fetch build/
#   make dist   → single self-contained skafinity.html (WASM inlined)
#   make clean

EMCC      ?= emcc
CXX       ?= g++
SRC        = src/bindings.cpp src/music_gen.cpp src/vibe_codec.cpp
CORE       = src/music_gen.cpp src/vibe_codec.cpp
EMFLAGS    = -O3 -std=c++17 --bind \
             -s MODULARIZE=1 -s EXPORT_ES6=1 \
             -s ENVIRONMENT=web,worker \
             -s ALLOW_MEMORY_GROWTH=1 \
             -s EXPORT_NAME=Skafinity
PORT      ?= 8000

.PHONY: all test serve dist clean

all: build/skafinity.js

build/skafinity.js: $(SRC) src/music_gen.h src/vibe_codec.h src/prng.h | build
	$(EMCC) $(EMFLAGS) $(SRC) -o $@

# Native parity + smoke test. -ffp-contract=off keeps float math close to the C#.
test: | build
	$(CXX) -O2 -std=c++17 -Wall -ffp-contract=off test/main.cpp $(CORE) -o build/skafinity_test
	./build/skafinity_test

# One self-contained file: SINGLE_FILE inlines the .wasm as base64 into the .js, and
# dist/skafinity.html pulls that single .js in. (Run `make dist` then ship dist/.)
dist: | build dist-dir
	$(EMCC) $(EMFLAGS) -s SINGLE_FILE=1 $(SRC) -o dist/skafinity.js
	cp web/index.html web/app.js web/worker.js web/style.css dist/
	@echo "dist/ is self-contained; open dist/index.html"

serve: all
	@echo "serving on http://localhost:$(PORT)/web/  (Ctrl-C to stop)"
	python3 -m http.server $(PORT)

build:
	mkdir -p build

dist-dir:
	mkdir -p dist

clean:
	rm -rf build dist
