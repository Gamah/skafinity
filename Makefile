# skafinity — C++/WASM ska engine.
#   make        → build the WASM module into build/ (needs emscripten / emcc)
#   make test   → native g++ build of the parity/smoke test (no emcc needed)
#   make serve  → static server rooted at repo so web/ can fetch build/
#   make dist   → single self-contained skafinity.html (WASM inlined)
#   make clean

EMCC      ?= emcc
CXX       ?= g++
NODE      ?= node
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

# One genuinely self-contained file: SINGLE_FILE inlines the .wasm as base64 into the
# emscripten .js; web/inline.mjs then embeds that + app.js + a Blob worker + the CSS into
# a single skafinity.html (root of the repo, the shareable artifact).
dist: skafinity.html

skafinity.html: $(SRC) src/*.h web/app.js web/worker.js web/style.css web/index.html web/inline.mjs | build
	$(EMCC) $(EMFLAGS) -s SINGLE_FILE=1 $(SRC) -o build/skafinity.single.js
	$(NODE) web/inline.mjs build/skafinity.single.js skafinity.html

serve: all
	@echo "serving on http://localhost:$(PORT)/web/  (Ctrl-C to stop)"
	python3 -m http.server $(PORT)

build:
	mkdir -p build

clean:
	rm -rf build dist skafinity.html
