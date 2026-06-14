// Emscripten/embind boundary. This file is the ONLY Emscripten-aware translation unit;
// music_gen / vibe_codec / prng stay plain C++. Built by the Makefile's emcc target.
#include <emscripten/bind.h>
#include <emscripten/val.h>
#include "music_gen.h"
#include "vibe_codec.h"
#include <string>
#include <vector>
#include <cstdlib>

using namespace emscripten;
using namespace ska;

// Copy a C++ float vector into a fresh JS Float32Array (slice() copies off the WASM heap
// so the result survives the vector being freed).
static val to_f32(const std::vector<float>& v) {
    val ta = val::global("Float32Array").new_(typed_memory_view(v.size(), v.data()));
    return ta.call<val>("slice");
}

static std::vector<float> to_vec(const val& a) { return vecFromJSArray<float>(a); }

// generateSong(seedStr, cfgArray) -> { sampleRate, left, right }
static val generateSong(const std::string& seed, const val& cfgArr) {
    Config c = config_from_vector(to_vec(cfgArr));
    MusicGen g(c);
    Song s = g.generate(seed);
    val out = val::object();
    out.set("sampleRate", s.sampleRate);
    out.set("left", to_f32(s.left));
    out.set("right", to_f32(s.right));
    const ComposeInfo& in = g.info();
    val info = val::object();
    info.set("bpm", in.bpm); info.set("fast", in.fast);
    info.set("scale", in.scaleIndex); info.set("prog", in.progIndex);
    info.set("instrument", in.instrument); info.set("bars", in.barCount);
    out.set("info", info);
    return out;
}

// WAV bytes (Uint8Array) for download. stereo=false downmixes to mono (game parity).
static val songToWav(const std::string& seed, const val& cfgArr, bool stereo) {
    Config c = config_from_vector(to_vec(cfgArr));
    MusicGen g(c);
    Song s = g.generate(seed);
    auto bytes = wav_from_song(s, stereo);
    val ta = val::global("Uint8Array").new_(typed_memory_view(bytes.size(), bytes.data()));
    return ta.call<val>("slice");
}

static val defaultConfig() { return to_f32(config_to_vector(Config{})); }
static int  configSize() { return config_vector_size(); }

static std::string encodeVibe(const val& cfgArr) {
    return vibe_encode(config_from_vector(to_vec(cfgArr)));
}

// Apply a vibe string onto cfg and return the updated cfg array.
static val decodeVibe(const std::string& vibe, const val& cfgArr) {
    Config c = config_from_vector(to_vec(cfgArr));
    vibe_apply(vibe, c);
    return to_f32(config_to_vector(c));
}

static bool looksLikeVibe(const std::string& s) { return vibe_looks_like(s); }
static int  vibeFieldCount() { return vibe_length(); }

static val vibeFieldInfo(int i) {
    const auto& f = vibe_fields();
    val o = val::object();
    if (i < 0 || i >= (int)f.size()) return o;
    o.set("name", f[i].name);
    o.set("min", f[i].min);
    o.set("max", f[i].max);
    o.set("isInt", f[i].isInt);
    val ch = val::array();
    for (size_t k = 0; k < f[i].choices.size(); k++) ch.set(k, f[i].choices[k]);
    o.set("choices", ch);
    return o;
}

static val setVibeField(const val& cfgArr, int i, float norm) {
    Config c = config_from_vector(to_vec(cfgArr));
    const auto& f = vibe_fields();
    if (i >= 0 && i < (int)f.size()) f[i].setNorm(c, norm);
    return to_f32(config_to_vector(c));
}

static float getVibeNorm(const val& cfgArr, int i) {
    Config c = config_from_vector(to_vec(cfgArr));
    const auto& f = vibe_fields();
    return (i >= 0 && i < (int)f.size()) ? f[i].getNorm(c) : 0.0f;
}

static std::string vibeDisplay(const val& cfgArr, int i) {
    Config c = config_from_vector(to_vec(cfgArr));
    const auto& f = vibe_fields();
    return (i >= 0 && i < (int)f.size()) ? f[i].display(c) : std::string();
}

// parseSeed(str) -> { vibe, tag, n, hasN } — mirrors MusicController.PlaySeed parsing.
static val parseSeed(const std::string& seedIn) {
    std::string seed = seedIn;
    size_t a = seed.find_first_not_of(" \t\r\n");
    size_t b = seed.find_last_not_of(" \t\r\n");
    seed = (a == std::string::npos) ? "" : seed.substr(a, b - a + 1);

    std::vector<std::string> p;
    size_t start = 0;
    while (true) {
        size_t pos = seed.find(':', start);
        if (pos == std::string::npos) { p.push_back(seed.substr(start)); break; }
        p.push_back(seed.substr(start, pos - start));
        start = pos + 1;
    }

    std::string vibe = "", tag = "";
    int n = 0; bool hasN = false;
    auto tryInt = [](const std::string& s, int& out) -> bool {
        if (s.empty()) return false;
        char* e = nullptr;
        long v = std::strtol(s.c_str(), &e, 10);
        if (e == s.c_str() || *e != '\0') return false;
        out = (int)v; return true;
    };

    if (!seed.empty()) {
        if (p.size() >= 3) { vibe = p[0]; tag = p[1]; if (tryInt(p[2], n)) hasN = true; }
        else if (p.size() == 2) {
            int v;
            if (tryInt(p[1], v)) { tag = p[0]; n = v; hasN = true; }
            else if (vibe_looks_like(p[0])) { vibe = p[0]; tag = p[1]; }
            else tag = p[0];
        } else tag = p[0];
    }

    val o = val::object();
    o.set("vibe", vibe); o.set("tag", tag); o.set("n", n); o.set("hasN", hasN);
    return o;
}

EMSCRIPTEN_BINDINGS(skafinity) {
    function("generateSong", &generateSong);
    function("songToWav", &songToWav);
    function("defaultConfig", &defaultConfig);
    function("configSize", &configSize);
    function("encodeVibe", &encodeVibe);
    function("decodeVibe", &decodeVibe);
    function("looksLikeVibe", &looksLikeVibe);
    function("vibeFieldCount", &vibeFieldCount);
    function("vibeFieldInfo", &vibeFieldInfo);
    function("setVibeField", &setVibeField);
    function("getVibeNorm", &getVibeNorm);
    function("vibeDisplay", &vibeDisplay);
    function("parseSeed", &parseSeed);
}
