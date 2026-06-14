// Native parity + smoke test for the C++ port. No Emscripten — plain g++.
// Prints PRNG golden vectors + composition choices, round-trips a vibe, and writes a
// real WAV so we can confirm the synthesis produces audio. `make test` runs this.
#include "../src/prng.h"
#include "../src/music_gen.h"
#include "../src/vibe_codec.h"
#include <cstdio>
#include <fstream>
#include <string>

using namespace ska;

static void dump_rng(const char* seed) {
    Rng r(xmur3(seed));
    printf("Rng.next() x32 for \"%s\":\n", seed);
    for (int i = 0; i < 32; i++) {
        printf("%.9f%s", r.next(), (i % 8 == 7) ? "\n" : " ");
    }
}

static const char* INSTR[] = {"Trumpet", "Sax", "Organ", "Trombone"};

static void dump_compose(const std::string& seed) {
    MusicGen g(Config{});
    Song s = g.generate(seed);
    const ComposeInfo& in = g.info();
    printf("compose \"%s\": fast=%d bpm=%d scale=%d prog=%d root=%d instr=%s bassPat=%d "
           "drumStyle=%d organBubble=%d horns=%d bars=%d sr=%d frames=%zu\n",
           seed.c_str(), in.fast, in.bpm, in.scaleIndex, in.progIndex, in.rootMidi,
           INSTR[in.instrument], in.bassPatIndex, in.drumStyle, in.organBubble, in.hasHorns,
           in.barCount, in.sampleRate, s.left.size());
}

int main() {
    printf("=== PRNG golden vectors ===\n");
    dump_rng("rotaliate");
    dump_rng("drums:rotaliate");
    dump_rng("bd44ac2a:23");

    printf("\n=== Composition choices ===\n");
    dump_compose("rotaliate:0");
    dump_compose("gamah:0");
    dump_compose("gamah:1");
    dump_compose("bd44ac2a:23");

    printf("\n=== Vibe codec ===\n");
    Config def{};
    std::string v = vibe_encode(def);
    printf("default vibe (%d fields): %s\n", vibe_length(), v.c_str());
    Config c2{};
    vibe_apply(v, c2);
    std::string v2 = vibe_encode(c2);
    printf("round-trip: %s  (stable=%d)\n", v2.c_str(), v == v2);
    printf("looksLikeVibe(default)=%d  looksLikeVibe(\"gamah\")=%d\n",
           vibe_looks_like(v), vibe_looks_like("gamah"));

    // Smoke: write a real stereo WAV so the synthesis can be heard / validated.
    printf("\n=== Synthesis smoke ===\n");
    MusicGen g(Config{});
    Song song = g.generate("gamah:0");
    auto wav = wav_from_song(song, true);
    std::ofstream out("build/smoke_gamah_0.wav", std::ios::binary);
    out.write(reinterpret_cast<const char*>(wav.data()), wav.size());
    out.close();
    printf("wrote build/smoke_gamah_0.wav (%zu bytes, %.1fs)\n",
           wav.size(), song.left.size() / (double)song.sampleRate);

    printf("\nOK\n");
    return 0;
}
