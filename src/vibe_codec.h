// Port of reference/VibeCodec.cs — base-36 encoding of the vibe knobs.
// The Field list ORDER is the wire format: append only, never reorder/remove.
#pragma once
#include "music_gen.h"
#include <string>
#include <vector>
#include <functional>

namespace ska {

struct VibeField {
    std::string name;
    float min, max;
    bool isInt;
    std::vector<std::string> choices;  // empty for a continuous knob
    std::function<float(const Config&)> get;
    std::function<void(Config&, float)> set;

    float getNorm(const Config& c) const;
    void  setNorm(Config& c, float norm) const;
    std::string display(const Config& c) const;
};

const std::vector<VibeField>& vibe_fields();
int  vibe_length();                                    // VibeCodec.Length
std::string vibe_encode(const Config& c);              // VibeCodec.Encode
void vibe_apply(const std::string& vibe, Config& c);   // VibeCodec.Apply
bool vibe_looks_like(const std::string& s);            // VibeCodec.LooksLikeVibe

} // namespace ska
