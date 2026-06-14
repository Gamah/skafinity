// Port of reference/MusicGen.cs — the composer + subtractive synthesiser.
// Plain C++17 (no Emscripten) so it's host-unit-testable. See CLAUDE.md "PRNG parity".
#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include <functional>

namespace ska {

// Mirror of MusicGen.Config — every field + default must match the C# exactly.
struct Config {
    // Output
    int   SampleRate    = 32000;
    float TargetSeconds = 80.0f;  // bar count adapts to tempo to hit this
    int   Bars          = 64;     // fallback if TargetSeconds <= 0

    // Tempo
    int   BpmMin = 86, BpmMax = 104;
    float FastChance = 0.30f;
    int   FastBpmMin = 150, FastBpmMax = 168;
    float Swing = 0.14f, FastSwing = 0.05f;

    // Mix
    float BassVol = 0.68f, SkankVol = 0.95f, OrganVol = 0.42f, MelodyVol = 0.34f, HornVol = 0.22f;
    float KickVol = 1.00f, SnareVol = 0.70f, TomVol = 0.60f, HatVol = 0.22f, CrashVol = 0.35f;

    // Tone
    float Detune = 14.0f;
    float BassCutoff = 380.0f, SkankCutoff = 3000.0f, SkankHighpass = 500.0f, LeadCutoff = 3200.0f;
    float Resonance = 1.0f;
    float BassDrive = 1.5f, SkankDrive = 1.3f, MelodyDrive = 1.3f, HornDrive = 1.4f;
    float MasterDrive = 1.1f, MasterPeak = 0.95f;

    // Feel
    float OctavePopChance = 0.30f, OrganBubbleChance = 0.55f, KickSyncChance = 0.25f, GhostSnareChance = 0.35f;
    float FillChance = 0.6f, DrumBusy = 0.6f, TripletChance = 0.06f;
    float MelodyRestChance = 0.30f, MelodyLeapChance = 0.18f, MelodyVibrato = 5.0f;

    // Stereo
    float PanAmount = 0.4f;

    // Lead instrument weights (-1 = RNG; 0=Trumpet 1=Sax 2=Organ 3=Trombone)
    float TrumpetWeight = 1.0f, SaxWeight = 1.0f, OrganWeight = 0.8f, TromboneWeight = 0.4f;
    int   ForceInstrument = -1;

    // Backing horns
    float HornSectionChance = 0.5f, HornDensity = 0.35f;
};

// Diagnostics captured during Compose — used by the parity test.
struct ComposeInfo {
    bool fast = false;
    int  bpm = 0;
    int  scaleIndex = 0, progIndex = 0, bassPatIndex = 0;
    int  rootMidi = 0, instrument = 0, drumStyle = 0;
    bool organBubble = false, hasHorns = false;
    int  barCount = 0, sampleRate = 0;
};

// A rendered loop: interleaved-free stereo float channels.
struct Song {
    int sampleRate = 0;
    std::vector<float> left, right;
};

class MusicGen {
public:
    static constexpr int Channels = 2;

    explicit MusicGen(const Config& c) : _c(c), _sr(c.SampleRate) {}

    // Render one full loop for `seed` (the resolved "tag:n" string). Master gain is
    // applied so the output is the final normalised audio.
    Song generate(const std::string& seed);

    const ComposeInfo& info() const { return _info; }

private:
    Config _c;
    int _sr;
    std::vector<float> _bufL, _bufR;
    ComposeInfo _info;

    // composition state
    std::vector<int> _scale, _prog;
    int _rootMidi = 0;
    int _lead = 0;            // Instrument enum
    float _leadPan = 0.0f;
    bool _hasHorns = false;
    std::vector<char> _hornMask;
    std::vector<int> _bassPat;
    int _drumStyle = 0;
    bool _organBubble = false;
    bool _fast = false;

    float compose(const std::string& seed); // returns master gain

    int scaleMidi(int baseMidi, int degree) const;
    int chordRoot(int c) const;

    struct Patch;
    void renderBassBar(int barStart, int spe, double secPerEighth, int chord, int nextChord, struct Rng& rng);
    void renderRhythmBar(int barStart, int spe, double secPerEighth, int chord, float swing, struct Rng& rng);
    void renderLeadPhrase(int barStart, int spe, double secPerEighth, int chord, struct Rng& rng);
    void renderHornStabs(int barStart, int spe, double secPerEighth, int chord);
    int  pickInstrument(struct Rng& rng);
    void renderLead(int at, int dur, int midi, float amp, double decaySec, float drive);
    void renderPatch(int start, int dur, float freq, const Patch& p);

    void renderDrumBar(int barStart, int spe, int chord, bool phraseEnd, struct Rng& rng, struct Rng& noise);
    void renderKickSnareGroove(int barStart, int spe, int from, int to, float busy, struct Rng& noise);
    void renderFill(int at, int spe, struct Rng& noise, struct Rng& rng);
    void renderKick(int start, struct Rng& noise);
    void renderSnare(int start, struct Rng& noise, bool ghost);
    void renderTom(int start, float baseFreq, struct Rng& noise);
    void renderHat(int start, bool open, float amp, struct Rng& noise);
    void renderCrash(int start, struct Rng& noise);
    float hpCoeff(float fc) const;

    double _ph[8] = {0};
    double _inc[8] = {0};
};

// 16-bit interleaved-stereo (or downmixed mono) WAV bytes — port of MusicGen.WavFromSamples.
std::vector<uint8_t> wav_from_song(const Song& song, bool stereo, float gain = 1.0f);

// ── Canonical Config <-> float-vector boundary (JS passes Config as a Float32Array) ──
// Order is fixed; both the WASM bindings and the host test use these.
std::vector<float> config_to_vector(const Config& c);
Config config_from_vector(const std::vector<float>& v);
int    config_vector_size();

} // namespace ska
