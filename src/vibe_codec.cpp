#include "vibe_codec.h"
#include <cmath>
#include <algorithm>

namespace ska {

static const char* Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
static const int Levels = 36;

static inline double cs_round(double v) { return std::nearbyint(v); }
static inline float clampf(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
static int alphabet_index(char c) {
    for (int i = 0; i < Levels; i++) if (Alphabet[i] == c) return i;
    return -1;
}

float VibeField::getNorm(const Config& c) const {
    return clampf((get(c) - min) / (max - min), 0.0f, 1.0f);
}
void VibeField::setNorm(Config& c, float norm) const {
    float v = min + clampf(norm, 0.0f, 1.0f) * (max - min);
    if (isInt || !choices.empty()) v = static_cast<float>(cs_round(v));
    set(c, v);
}
std::string VibeField::display(const Config& c) const {
    float v = get(c);
    if (!choices.empty()) {
        int idx = static_cast<int>(clampf(static_cast<float>(cs_round(v - min)), 0.0f, static_cast<float>(choices.size() - 1)));
        return choices[idx];
    }
    if (isInt) return std::to_string(static_cast<int>(cs_round(v)));
    if (min == 0.0f && max <= 1.0f) return std::to_string(static_cast<int>(cs_round(v * 100))) + "%";
    return std::to_string(static_cast<int>(cs_round(v)));
}

// Order is the wire format — append only. Must equal reference/VibeCodec.cs.
const std::vector<VibeField>& vibe_fields() {
    static const std::vector<VibeField> f = {
        {"TEMPO MIN", 60, 200, true, {}, [](const Config& c){return (float)c.BpmMin;}, [](Config& c, float v){c.BpmMin=(int)v;}},
        {"TEMPO MAX", 60, 200, true, {}, [](const Config& c){return (float)c.BpmMax;}, [](Config& c, float v){c.BpmMax=(int)v;}},
        {"FAST CHANCE", 0.0f, 1.0f, false, {}, [](const Config& c){return c.FastChance;}, [](Config& c, float v){c.FastChance=v;}},
        {"SWING", 0.0f, 0.4f, false, {}, [](const Config& c){return c.Swing;}, [](Config& c, float v){c.Swing=v;}},
        {"BASS", 0.0f, 1.5f, false, {}, [](const Config& c){return c.BassVol;}, [](Config& c, float v){c.BassVol=v;}},
        {"SKANK", 0.0f, 1.5f, false, {}, [](const Config& c){return c.SkankVol;}, [](Config& c, float v){c.SkankVol=v;}},
        {"ORGAN", 0.0f, 1.5f, false, {}, [](const Config& c){return c.OrganVol;}, [](Config& c, float v){c.OrganVol=v;}},
        {"LEAD", 0.0f, 1.5f, false, {}, [](const Config& c){return c.MelodyVol;}, [](Config& c, float v){c.MelodyVol=v;}},
        {"HORNS", 0.0f, 1.5f, false, {}, [](const Config& c){return c.HornVol;}, [](Config& c, float v){c.HornVol=v;}},
        {"BASS TONE", 80.0f, 1200.0f, false, {}, [](const Config& c){return c.BassCutoff;}, [](Config& c, float v){c.BassCutoff=v;}},
        {"SKANK TONE", 500.0f, 8000.0f, false, {}, [](const Config& c){return c.SkankCutoff;}, [](Config& c, float v){c.SkankCutoff=v;}},
        {"LEAD TONE", 500.0f, 8000.0f, false, {}, [](const Config& c){return c.LeadCutoff;}, [](Config& c, float v){c.LeadCutoff=v;}},
        {"RESONANCE", 0.2f, 2.0f, false, {}, [](const Config& c){return c.Resonance;}, [](Config& c, float v){c.Resonance=v;}},
        {"OCTAVE POP", 0.0f, 1.0f, false, {}, [](const Config& c){return c.OctavePopChance;}, [](Config& c, float v){c.OctavePopChance=v;}},
        {"ORGAN BUBBLE", 0.0f, 1.0f, false, {}, [](const Config& c){return c.OrganBubbleChance;}, [](Config& c, float v){c.OrganBubbleChance=v;}},
        {"DRUM FILLS", 0.0f, 1.0f, false, {}, [](const Config& c){return c.FillChance;}, [](Config& c, float v){c.FillChance=v;}},
        {"STEREO WIDTH", 0.0f, 1.0f, false, {}, [](const Config& c){return c.PanAmount;}, [](Config& c, float v){c.PanAmount=v;}},
        {"LEAD INSTR", -1, 3, true, {"RNG","TRUMPET","SAX","ORGAN","TROMBONE"}, [](const Config& c){return (float)c.ForceInstrument;}, [](Config& c, float v){c.ForceInstrument=(int)v;}},
        {"HORN SECTION", 0.0f, 1.0f, false, {}, [](const Config& c){return c.HornSectionChance;}, [](Config& c, float v){c.HornSectionChance=v;}},
        {"HORN DENSITY", 0.0f, 1.0f, false, {}, [](const Config& c){return c.HornDensity;}, [](Config& c, float v){c.HornDensity=v;}},
        {"DRUM BUSY", 0.0f, 1.0f, false, {}, [](const Config& c){return c.DrumBusy;}, [](Config& c, float v){c.DrumBusy=v;}},
        {"TRIPLETS", 0.0f, 0.1f, false, {}, [](const Config& c){return c.TripletChance;}, [](Config& c, float v){c.TripletChance=v;}},
    };
    return f;
}

int vibe_length() { return static_cast<int>(vibe_fields().size()); }

std::string vibe_encode(const Config& c) {
    const auto& fields = vibe_fields();
    std::string s;
    s.reserve(fields.size());
    for (const auto& f : fields) {
        int q = static_cast<int>(cs_round(f.getNorm(c) * (Levels - 1)));
        if (q < 0) q = 0; else if (q > Levels - 1) q = Levels - 1;
        s.push_back(Alphabet[q]);
    }
    return s;
}

void vibe_apply(const std::string& vibeIn, Config& c) {
    if (vibeIn.empty()) return;
    std::string vibe = vibeIn;
    // trim + lower
    size_t a = vibe.find_first_not_of(" \t\r\n");
    size_t b = vibe.find_last_not_of(" \t\r\n");
    if (a == std::string::npos) return;
    vibe = vibe.substr(a, b - a + 1);
    for (char& ch : vibe) if (ch >= 'A' && ch <= 'Z') ch += 32;

    const auto& fields = vibe_fields();
    int n = std::min(static_cast<int>(vibe.size()), static_cast<int>(fields.size()));
    for (int i = 0; i < n; i++) {
        int q = alphabet_index(vibe[i]);
        if (q < 0) continue;
        fields[i].setNorm(c, q / static_cast<float>(Levels - 1));
    }
}

bool vibe_looks_like(const std::string& sIn) {
    int len = vibe_length();
    if (sIn.empty() || static_cast<int>(sIn.size()) < len - 4 || static_cast<int>(sIn.size()) > len) return false;
    for (char ch : sIn) {
        if (ch >= 'A' && ch <= 'Z') ch += 32;
        if (alphabet_index(ch) < 0) return false;
    }
    return true;
}

} // namespace ska
