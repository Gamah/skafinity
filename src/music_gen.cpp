#include "music_gen.h"
#include "prng.h"
#include <cmath>
#include <algorithm>

namespace ska {

static const double PI = 3.14159265358979323846;

// C#'s Math.Round / MathF.Round default to banker's rounding (round half to even).
// std::nearbyint honours the FE_TONEAREST default rounding mode, which is the same.
static inline double cs_round(double v) { return std::nearbyint(v); }
static inline int clampi(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
static inline float clampf(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }

// ── Harmony tables (mirror MusicGen) ──
static const std::vector<std::vector<int>> Scales = {
    {0, 2, 4, 5, 7, 9, 11}, // major
    {0, 2, 4, 5, 7, 9, 10}, // mixolydian
    {0, 2, 4, 5, 7, 9, 11}, // major (weighted twice)
    {0, 2, 3, 5, 7, 9, 10}, // dorian
};
static const std::vector<std::vector<int>> Progressions = {
    {0, 4, 5, 3}, {0, 3, 4, 4}, {0, 5, 3, 4}, {0, 6, 3, 0}, {5, 3, 0, 4}, {0, 3, 0, 4},
};
static const int Rest = -99, Approach = 99;
static const std::vector<std::vector<int>> BassPatterns = {
    {0, Rest, 0, 12, Rest, 7, 5, Approach},
    {Rest, Rest, 0, Rest, 0, Rest, 7, Approach},
    {0, Rest, 7, Rest, 12, Rest, 7, Approach},
    {0, 12, 0, 7, 0, 12, 7, Approach},
    {0, Rest, 0, Rest, 5, Rest, 7, Approach},
};
static const int EighthsPerBar = 8;

static std::string to_lower(const std::string& s) {
    std::string r = s;
    for (char& c : r) if (c >= 'A' && c <= 'Z') c += 32;
    return r;
}

static inline float osc(int t, double p) {
    switch (t) {
        case 0: return std::sin(static_cast<float>(p * 2 * PI));
        case 1: return static_cast<float>(2 * p - 1);
        case 2: return p < 0.5 ? 1.0f : -1.0f;
        default: return 4.0f * std::fabs(static_cast<float>(p) - 0.5f) - 1.0f;
    }
}

static inline float midi_freq(int m) { return 440.0f * std::pow(2.0f, (m - 69) / 12.0f); }

static const float Sqrt2 = 1.41421356f;
static void stereo_gains(float pan, float& gL, float& gR) {
    pan = clampf(pan, -1.0f, 1.0f);
    double ang = (pan + 1) * 0.5 * (PI / 2);
    gL = static_cast<float>(std::cos(ang)) * Sqrt2;
    gR = static_cast<float>(std::sin(ang)) * Sqrt2;
}

float MusicGen::hpCoeff(float fc) const {
    return static_cast<float>(1.0 / (1.0 + 2 * PI * fc / _sr));
}

struct MusicGen::Patch {
    int Osc = 0, Voices = 0;
    float Detune = 0, Amp = 0, Attack = 0;
    double Decay = 0;
    float Sustain = 0;
    bool Sustained = false;
    float Cutoff = 0, CutEnv = 0, Reso = 0, Highpass = 0, Drive = 0, Pan = 0, Vibrato = 0, Breath = 0;
};

int MusicGen::scaleMidi(int baseMidi, int degree) const {
    int len = static_cast<int>(_scale.size());
    int oct = static_cast<int>(std::floor(degree / static_cast<double>(len)));
    return baseMidi + _scale[degree - oct * len] + 12 * oct;
}
int MusicGen::chordRoot(int c) const { return scaleMidi(_rootMidi, _prog[c]); }

float MusicGen::compose(const std::string& seed) {
    std::string melodicTag = seed.empty() ? "rotaliate" : to_lower(seed);
    Rng rng(xmur3(melodicTag));

    _fast = rng.chance(_c.FastChance);
    int bpm = _fast
        ? _c.FastBpmMin + rng.next_int(std::max(1, _c.FastBpmMax - _c.FastBpmMin + 1))
        : _c.BpmMin + rng.next_int(std::max(1, _c.BpmMax - _c.BpmMin + 1));

    _info.scaleIndex = rng.next_int(static_cast<int>(Scales.size()));        // == Pick(Scales)
    _scale = Scales[_info.scaleIndex];
    _info.progIndex = rng.next_int(static_cast<int>(Progressions.size()));
    _prog = Progressions[_info.progIndex];
    _rootMidi = 28 + rng.next_int(8);
    _lead = pickInstrument(rng);
    _leadPan = (rng.next() * 2.0f - 1.0f) * _c.PanAmount;
    _info.bassPatIndex = rng.next_int(static_cast<int>(BassPatterns.size()));
    _bassPat = BassPatterns[_info.bassPatIndex];
    _drumStyle = _fast ? 2 : rng.next_int(2);
    // Every song carries all melodic voices — organ bubble, skank, and horns are no longer
    // rolled on/off. The RNG draws are kept so songs that already had them stay bit-identical
    // downstream; only previously-bare songs gain the voice (and its extra horn-mask draws).
    rng.chance(_c.OrganBubbleChance);               // draw kept for stream stability
    _organBubble = true;

    rng.chance(_c.HornSectionChance);               // draw kept for stream stability
    _hasHorns = true;
    _hornMask.assign(EighthsPerBar, 0);
    _hornMask[0] = 1;
    for (int e = 1; e < EighthsPerBar; e++)
        _hornMask[e] = rng.chance(_c.HornDensity * (e % 2 == 1 ? 1.3f : 0.5f)) ? 1 : 0;

    float swing = _fast ? _c.FastSwing : _c.Swing;
    double secPerEighth = 60.0 / bpm / 2.0;
    int spe = static_cast<int>(cs_round(_sr * secPerEighth));

    // Drum push/pull: a per-song-constant timing bias on the whole kit (negative = push
    // ahead of the beat, positive = lay back). Own RNG stream → main composition order is
    // untouched. Biased toward pushing so the backbeat never feels "super late".
    Rng pushRng(xmur3("push:" + seed));   // NB: resolved seed, matching the other colon streams
    _drumPush = static_cast<int>(cs_round((pushRng.next() - 0.62f) * _c.DrumPush * spe));

    int barCount = std::max(1, _c.Bars);
    if (_c.TargetSeconds > 1.0f) {
        double barSec = EighthsPerBar * secPerEighth;
        barCount = clampi(static_cast<int>(cs_round(_c.TargetSeconds / barSec / 8.0)) * 8, 16, 128);
    }

    int total = spe * EighthsPerBar * barCount;
    _bufL.assign(total, 0.0f);
    _bufR.assign(total, 0.0f);
    // Colon-prefixed streams seed off the resolved seed (the existing port's convention —
    // see CLAUDE.md / PR note; the C# uses the raw player tag). Keeping it consistent here.
    Rng noise(xmur3("drums:" + seed));
    Rng bassOrn(xmur3("bass:" + seed));
    Rng hornOrn(xmur3("horn:" + seed));

    for (int bar = 0; bar < barCount; bar++) {
        int chord = (bar / 2) % static_cast<int>(_prog.size());
        int nextChord = ((bar / 2) + 1) % static_cast<int>(_prog.size());
        int barStart = bar * EighthsPerBar * spe;
        bool phraseEnd = (bar % 4) == 3;

        renderBassBar(barStart, spe, secPerEighth, chord, nextChord, rng, bassOrn);
        renderRhythmBar(barStart, spe, secPerEighth, chord, swing, rng);
        renderDrumBar(barStart, spe, chord, phraseEnd, rng, noise);
        if (bar % 2 == 0) renderLeadPhrase(barStart, spe, secPerEighth, chord, rng);
        if (_hasHorns) renderHornStabs(barStart, spe, secPerEighth, chord, hornOrn);
    }

    // Master: peak-normalize the mix to 1.0 BEFORE the soft-clipper so it always has
    // headroom — otherwise a hot sustained bed (all voices flat at 1.0) saturates the tanh
    // and swallows the drum transients. MasterDrive now sets how hard a peak-normalized
    // signal hits the clipper, so dynamics stay intact.
    float rawPeak = 0.0f;
    for (int i = 0; i < total; i++)
        rawPeak = std::max(rawPeak, std::max(std::fabs(_bufL[i]), std::fabs(_bufR[i])));
    float pre = rawPeak > 0.0001f ? _c.MasterDrive / rawPeak : _c.MasterDrive;

    float peak = 0.0f;
    for (int i = 0; i < total; i++) {
        float l = static_cast<float>(std::tanh(_bufL[i] * pre));
        float r = static_cast<float>(std::tanh(_bufR[i] * pre));
        _bufL[i] = l; _bufR[i] = r;
        float a = std::max(std::fabs(l), std::fabs(r));
        if (a > peak) peak = a;
    }

    _info.fast = _fast;
    _info.bpm = bpm;
    _info.rootMidi = _rootMidi;
    _info.instrument = _lead;
    _info.drumStyle = _drumStyle;
    _info.organBubble = _organBubble;
    _info.hasHorns = _hasHorns;
    _info.barCount = barCount;
    _info.sampleRate = _sr;

    return peak > 0.0001f ? _c.MasterPeak / peak : 1.0f;
}

void MusicGen::renderBassBar(int barStart, int spe, double secPerEighth, int chord, int nextChord, Rng& rng, Rng& bassOrn) {
    int root = chordRoot(chord);
    // Bass has its own ornament knob (BASS TRIPLETS), nudged up a touch by overall kit
    // busyness so a busy vibe gets a busier bass.
    float ornChance = _c.BassTriplets * 0.5f + _c.DrumBusy * 0.05f;
    for (int e = 0; e < EighthsPerBar; e++) {
        int off = _bassPat[e];
        if (off == Rest) continue;
        int midi;
        if (off == Approach) {
            int target = chordRoot(nextChord);
            midi = target - (rng.chance(0.5f) ? 1 : 2);
        } else {
            midi = root + off;
            if (off == 0 && e > 0 && rng.chance(_c.OctavePopChance)) midi += 12;
        }
        int len = 1;
        while (e + len < EighthsPerBar && _bassPat[e + len] == Rest) len++;

        // Chop a standalone (non-sustaining) note into a 16th pair or 16th-note triplet.
        // Driven by a dedicated stream so the main composition RNG order is unchanged.
        if (off != Approach && len == 1 && bassOrn.chance(ornChance)) {
            int n = bassOrn.chance(0.65f) ? 2 : 3;          // 16th pair / 16th triplet
            int step = spe / n;
            int moves[3] = {0, 7, 12};                      // root / fifth / octave
            for (int k = 0; k < n; k++) {
                int bm = midi + (k == 0 ? 0 : moves[bassOrn.next_int(3)]);
                emitBass(barStart + e * spe + k * step, static_cast<int>(step * 0.9f), bm, secPerEighth / n * 0.8);
            }
            continue;
        }

        emitBass(barStart + e * spe, static_cast<int>(spe * len * 0.95f), midi, secPerEighth * len * 0.8);
    }
}

void MusicGen::emitBass(int at, int dur, int midi, double decaySec) {
    // Triangle body for a round, deep reggae/dub bass (saw alone read as too buzzy) — plus a
    // quieter square layer underneath whose odd harmonics add presence/definition. Both share
    // the bass low-pass so the tone stays warm.
    Patch p; p.Osc = 3; p.Voices = 2; p.Detune = _c.Detune * 0.4f;
    p.Amp = _c.BassVol; p.Attack = 0.004f; p.Decay = decaySec;
    p.Sustain = 0.55f; p.Sustained = true;
    p.Cutoff = _c.BassCutoff; p.CutEnv = 350.0f; p.Reso = 0.9f; p.Drive = _c.BassDrive; p.Pan = 0.0f;
    renderPatch(at, dur, midi_freq(midi), p);

    Patch q; q.Osc = 2; q.Voices = 1; q.Detune = 0.0f;
    q.Amp = _c.BassVol * 0.4f; q.Attack = 0.004f; q.Decay = decaySec;
    q.Sustain = 0.55f; q.Sustained = true;
    q.Cutoff = _c.BassCutoff; q.CutEnv = 350.0f; q.Reso = 0.9f; q.Drive = _c.BassDrive; q.Pan = 0.0f;
    renderPatch(at, dur, midi_freq(midi), q);
}

void MusicGen::renderRhythmBar(int barStart, int spe, double secPerEighth, int chord, float swing, Rng& rng) {
    (void)secPerEighth; (void)rng; // matches C#: rng is passed but unused here
    // +24: skank/organ sit an octave above the old register — at +12 (E2..B2) the chop was
    // too low/muddy to cut through. Organ stays a further octave down via the -12 below.
    int gBase = _rootMidi + 24;
    int degs[4] = {_prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 7};
    int chopDur = static_cast<int>(spe * clampf(_c.SkankChop, 0.15f, 1.0f));
    for (int e = 1; e < EighthsPerBar; e += 2) {
        int at = barStart + e * spe + static_cast<int>(swing * spe);
        for (int d : degs) {
            Patch p; p.Osc = 1; p.Voices = 3; p.Detune = _c.Detune;
            p.Amp = _c.SkankVol / 4.0f; p.Attack = 0.002f; p.Decay = 0.10;
            p.Sustain = 0.0f; p.Sustained = false;
            p.Cutoff = _c.SkankCutoff; p.CutEnv = 1500.0f; p.Reso = 0.8f;
            p.Highpass = _c.SkankHighpass; p.Drive = _c.SkankDrive; p.Pan = 0.0f;
            renderPatch(at, chopDur, midi_freq(scaleMidi(gBase, d)), p);
        }
        if (_organBubble) {
            for (int d : degs) {
                Patch p; p.Osc = 0; p.Voices = 2; p.Detune = _c.Detune * 0.5f;
                p.Amp = _c.OrganVol / 4.0f; p.Attack = 0.004f; p.Decay = 0.16;
                p.Sustain = 0.3f; p.Sustained = false;
                p.Cutoff = _c.OrganCutoff; p.CutEnv = 0.0f; p.Reso = 1.0f; p.Drive = 1.1f; p.Pan = 0.0f; p.Vibrato = _c.OrganVibrato;
                renderPatch(at, static_cast<int>(spe * 0.55f), midi_freq(scaleMidi(gBase, d) - 12), p);
            }
        }
    }
}

void MusicGen::renderLeadPhrase(int barStart, int spe, double secPerEighth, int chord, Rng& rng) {
    int slots = EighthsPerBar * 2;
    int melBase = _rootMidi + 24;
    int tones[4] = {_prog[chord], _prog[chord] + 2, _prog[chord] + 4, _prog[chord] + 6};
    int degree = tones[rng.next_int(3)];
    float amp = _c.MelodyVol;
    float drive = _c.MelodyDrive;

    int e = 0;
    while (e < slots) {
        if (rng.chance(_c.MelodyRestChance)) { e++; continue; }
        if (rng.chance(_c.TripletChance)) {
            float r = rng.next();
            int n, spanE;
            if (r < 0.25f) { n = 2; spanE = 1; }
            else if (r < 0.50f) { n = 3; spanE = 1; }
            else if (r < 0.80f) { n = 3; spanE = 2; }
            else { n = 3; spanE = 4; }
            if (e + spanE > slots) spanE = 1;
            int span = spanE * spe;
            int step = span / n;
            for (int k = 0; k < n; k++) {
                int d2 = clampi(degree + (k - n / 2), _prog[chord] - 3, _prog[chord] + 10);
                renderLead(barStart + e * spe + k * step, static_cast<int>(step * 0.9f),
                           scaleMidi(melBase, d2), amp, secPerEighth * spanE / static_cast<double>(n) * 0.85, drive);
            }
            e += spanE;
            continue;
        }

        int len = 1 + rng.next_int(3);
        if (e + len > slots) len = slots - e;
        bool strong = (e % 2) == 0;
        if (strong) {
            int best = tones[0], bestD = 999;
            for (int t : tones) {
                for (int oc = -7; oc <= 14; oc += 7) {
                    int cand = t + (oc / 7) * 7;
                    int dist = std::abs(cand - degree);
                    if (dist < bestD) { bestD = dist; best = cand; }
                }
            }
            degree = best;
        } else {
            int step = rng.chance(_c.MelodyLeapChance) ? (rng.chance(0.5f) ? 3 : -3)
                                                       : (rng.chance(0.5f) ? 1 : -1);
            degree = clampi(degree + step, _prog[chord] - 3, _prog[chord] + 10);
        }
        renderLead(barStart + e * spe, static_cast<int>(spe * len * 0.9f), scaleMidi(melBase, degree),
                   amp, secPerEighth * len * 0.7f, drive);
        e += len;
    }
}

void MusicGen::renderHornStabs(int barStart, int spe, double secPerEighth, int chord, Rng& orn) {
    int baseMidi = _rootMidi + 19;
    int degs[3] = {_prog[chord], _prog[chord] + 2, _prog[chord] + 4};
    float spread = _c.PanAmount * 0.7f;
    int six = spe / 2;
    float ornChance = 0.18f + _c.TripletChance; // ~0.24 default; rides the same knob

    // one chord-tone voice
    auto Note = [&](int at, int dur, int k, double dec, float gain) {
        Patch p; p.Osc = 1; p.Voices = 3; p.Detune = _c.Detune;
        p.Amp = _c.HornVol / 3.0f * gain; p.Attack = 0.008f; p.Decay = dec;
        p.Sustain = 0.2f; p.Sustained = false;
        p.Cutoff = _c.HornCutoff; p.CutEnv = 1200.0f; p.Reso = 1.0f; p.Drive = _c.HornDrive;
        p.Pan = spread * (k / 2.0f * 2.0f - 1.0f);
        renderPatch(at, dur, midi_freq(scaleMidi(baseMidi, degs[k])), p);
    };
    auto Stab = [&](int at, int dur, double dec, float gain) {
        for (int k = 0; k < 3; k++) Note(at, dur, k, dec, gain);
    };

    for (int e = 0; e < EighthsPerBar; e++) {
        if (!_hornMask[e]) continue;
        int at = barStart + e * spe;

        if (six > 0 && orn.chance(ornChance)) {
            float r = orn.next();
            if (r < 0.4f) {
                // rolling arpeggio: chord tones climb across a 16th-triplet
                int step = spe / 3;
                for (int k = 0; k < 3; k++)
                    Note(at + k * step, static_cast<int>(step * 0.9f), k, secPerEighth / 3 * 0.8, 1.0f);
                continue;
            }
            if (r < 0.75f) {
                // 16th pair: stab on the beat, softer echo on the "e"
                Stab(at, static_cast<int>(six * 0.85f), secPerEighth * 0.5 * 0.8, 1.0f);
                Stab(at + six, static_cast<int>(six * 0.85f), secPerEighth * 0.5 * 0.7, 0.6f);
                continue;
            }
            // grace pickup: a soft single tone just before the block stab
            Note(at - six, static_cast<int>(six * 0.8f), 0, secPerEighth * 0.5 * 0.6, 0.5f);
            Stab(at, static_cast<int>(spe * 0.6f), 0.22, 1.0f);
            continue;
        }

        // plain stab — length varies a touch so even straight bars aren't identical
        float lenMul = 0.45f + orn.next() * 0.35f;
        Stab(at, static_cast<int>(spe * lenMul), 0.22, 1.0f);
    }
}

int MusicGen::pickInstrument(Rng& rng) {
    if (_c.ForceInstrument >= 0 && _c.ForceInstrument <= 3) return _c.ForceInstrument;
    float tw = std::max(0.0f, _c.TrumpetWeight), sw = std::max(0.0f, _c.SaxWeight);
    float ow = std::max(0.0f, _c.OrganWeight), bw = std::max(0.0f, _c.TromboneWeight);
    float sum = tw + sw + ow + bw;
    if (sum <= 0.0f) return 0;
    float r = rng.next() * sum;
    if ((r -= tw) < 0.0f) return 0;
    if ((r -= sw) < 0.0f) return 1;
    if ((r -= ow) < 0.0f) return 2;
    return 3;
}

void MusicGen::renderLead(int at, int dur, int midi, float amp, double decaySec, float drive) {
    Patch p;
    switch (_lead) {
        case 0: // Trumpet
            p.Osc = 1; p.Voices = 3; p.Detune = _c.Detune * 0.7f; p.Amp = amp;
            p.Attack = 0.01f; p.Decay = decaySec; p.Sustain = 0.7f; p.Sustained = true;
            p.Cutoff = _c.LeadCutoff; p.CutEnv = 1800.0f; p.Reso = 1.0f; p.Drive = drive;
            p.Pan = _leadPan; p.Vibrato = _c.MelodyVibrato;
            renderPatch(at, dur, midi_freq(midi), p);
            break;
        case 3: // Trombone
            p.Osc = 1; p.Voices = 3; p.Detune = _c.Detune * 0.7f; p.Amp = amp * 1.1f;
            p.Attack = 0.02f; p.Decay = decaySec; p.Sustain = 0.7f; p.Sustained = true;
            p.Cutoff = _c.LeadCutoff * 0.7f; p.CutEnv = 900.0f; p.Reso = 1.0f; p.Drive = std::max(1.0f, drive * 0.8f);
            p.Pan = _leadPan; p.Vibrato = _c.MelodyVibrato * 0.7f;
            renderPatch(at, dur, midi_freq(midi - 12), p);
            break;
        case 1: // Sax
            p.Osc = 3; p.Voices = 2; p.Detune = _c.Detune * 0.5f; p.Amp = amp * 1.15f;
            p.Attack = 0.014f; p.Decay = decaySec; p.Sustain = 0.75f; p.Sustained = true;
            p.Cutoff = _c.LeadCutoff; p.CutEnv = 1400.0f; p.Reso = 0.7f; p.Drive = std::max(1.2f, drive);
            p.Pan = _leadPan; p.Vibrato = _c.MelodyVibrato; p.Breath = 0.03f;
            renderPatch(at, dur, midi_freq(midi), p);
            break;
        case 2: // Organ
            p.Osc = 0; p.Voices = 3; p.Detune = _c.Detune * 0.6f; p.Amp = amp;
            p.Attack = 0.006f; p.Decay = decaySec * 1.5; p.Sustain = 0.9f; p.Sustained = true;
            p.Cutoff = 2600.0f; p.CutEnv = 0.0f; p.Reso = 1.0f; p.Drive = 1.15f;
            p.Pan = _leadPan; p.Vibrato = _c.MelodyVibrato * 0.9f;
            renderPatch(at, dur, midi_freq(midi), p);
            break;
    }
}

void MusicGen::renderPatch(int start, int dur, float freq, const Patch& p) {
    if (start < 0 || dur <= 0 || p.Voices < 1) return;
    float gL, gR; stereo_gains(p.Pan, gL, gR);
    int atk = std::max(1, static_cast<int>(p.Attack * _sr));
    double decSamp = std::max(1.0, p.Decay * _sr);
    int rel = std::max(1, static_cast<int>(0.006f * _sr));
    int voices = std::min(8, p.Voices);

    double* ph = _ph; double* inc = _inc;
    for (int v = 0; v < voices; v++) {
        ph[v] = 0;
        float cents = voices == 1 ? 0.0f : (v - (voices - 1) * 0.5f) * p.Detune;
        inc[v] = freq * std::pow(2.0, cents / 1200.0) / _sr;
    }

    float low = 0, band = 0;
    float reso = clampf(p.Reso, 0.2f, 2.0f);
    float dnorm = p.Drive > 1.0f ? 1.0f / static_cast<float>(std::tanh(p.Drive)) : 1.0f;
    float hpA = p.Highpass > 0.0f ? static_cast<float>(1.0 / (1.0 + 2 * PI * p.Highpass / _sr)) : 0.0f;
    float hpInPrev = 0.0f, hpOutPrev = 0.0f;
    uint32_t bn = 0x9E3779B9u;

    int bufLen = static_cast<int>(_bufL.size());
    int end = std::min(bufLen, start + dur);
    int relStart = dur - rel;
    for (int i = 0; start + i < end; i++) {
        float env;
        if (i < atk) env = static_cast<float>(i) / atk;
        else if (p.Sustained) {
            float d = static_cast<float>(std::exp(-(i - atk) / decSamp));
            env = p.Sustain + (1.0f - p.Sustain) * d;
        } else env = static_cast<float>(std::exp(-(i - atk) / decSamp));
        if (i >= relStart) env *= std::max(0.0f, static_cast<float>(dur - i) / rel);
        if (env < 0.0006f && i > atk && !p.Sustained) break;

        float s = 0.0f;
        float vib = p.Vibrato > 0.0f
            ? static_cast<float>(1.0 + 0.005 * std::sin(i / static_cast<double>(_sr) * p.Vibrato * 2 * PI))
            : 1.0f;
        for (int v = 0; v < voices; v++) {
            s += osc(p.Osc, ph[v] - std::floor(ph[v]));
            ph[v] += inc[v] * vib;
        }
        s /= voices;
        if (p.Breath > 0.0f) {
            bn = bn * 1664525u + 1013904223u;
            s += (bn / 4294967296.0f * 2.0f - 1.0f) * p.Breath;
        }
        if (hpA > 0.0f) {
            float hp = hpA * (hpOutPrev + s - hpInPrev);
            hpInPrev = s; hpOutPrev = hp; s = hp;
        }

        float cut = p.Cutoff + (p.CutEnv > 0.0f ? p.CutEnv * static_cast<float>(std::exp(-i / decSamp)) : 0.0f);
        float f = static_cast<float>(2 * std::sin(PI * std::min(cut, _sr * 0.16f) / _sr));
        float high = s - low - reso * band;
        band += f * high;
        low += f * band;
        float outp = low;

        if (p.Drive > 1.0f) outp = static_cast<float>(std::tanh(outp * p.Drive)) * dnorm;
        float val = outp * env * p.Amp;
        _bufL[start + i] += val * gL;
        _bufR[start + i] += val * gR;
    }
}

// ── Drums ──
void MusicGen::renderDrumBar(int barStart, int spe, int chord, bool phraseEnd, Rng& rng, Rng& noise) {
    (void)chord;
    // Knob ceiling was too frantic: scale so DRUM BUSY 100% reads as the old 75%.
    float busy = clampf(_c.DrumBusy, 0.0f, 1.0f) * 0.75f;
    int six = spe / 2;
    for (int e = 0; e < EighthsPerBar; e++) {
        int at = barStart + e * spe;
        bool open = e == 7;
        renderHat(at, open, (e % 2 == 1 ? _c.HatVol : _c.HatVol * 0.6f), noise);
        if (!open && six > 0 && noise.chance(busy))
            renderHat(at + six, false, _c.HatVol * 0.4f, noise);
    }
    if (phraseEnd && rng.chance(_c.FillChance)) {
        renderKickSnareGroove(barStart, spe, 0, 6, busy, noise);
        renderFill(barStart + 6 * spe, spe, noise, rng);
        return;
    }
    renderKickSnareGroove(barStart, spe, 0, EighthsPerBar, busy, noise);
}

void MusicGen::renderKickSnareGroove(int barStart, int spe, int from, int to, float busy, Rng& noise) {
    int six = spe / 2;
    for (int e = from; e < to; e++) {
        int at = barStart + e * spe;
        switch (_drumStyle) {
            case 0:
                if (e == 4) { renderKick(at, noise); renderSnare(at, noise, false); }
                else if (e == 2 && noise.chance(_c.GhostSnareChance * (0.4f + busy))) renderSnare(at, noise, true);
                break;
            case 1:
                if (e % 2 == 0) renderKick(at, noise);
                if (e == 2 || e == 6) renderSnare(at, noise, false);
                break;
            default:
                if (e == 0 || e == 4 || (e == 3 && noise.chance(_c.KickSyncChance * (0.4f + busy)))) renderKick(at, noise);
                if (e == 2 || e == 6) renderSnare(at, noise, false);
                else if (noise.chance(_c.GhostSnareChance * busy)) renderSnare(at, noise, true);
                break;
        }
        if (six > 0 && e != 4 && noise.chance(_c.GhostSnareChance * busy * 0.5f))
            renderSnare(at + six, noise, true);
    }
}

void MusicGen::renderFill(int at, int spe, Rng& noise, Rng& rng) {
    int n = rng.chance(_c.TripletChance) ? (rng.chance(0.5f) ? 3 : 6) : 4;
    int step = (spe * 2) / n;
    float toms[6] = {200.0f, 165.0f, 135.0f, 110.0f, 90.0f, 72.0f};
    for (int i = 0; i < n; i++) {
        int t = at + i * step;
        if (rng.chance(0.5f)) renderSnare(t, noise, false);
        else renderTom(t, toms[i], noise);
    }
    renderCrash(at + n * step, noise);
}

void MusicGen::renderKick(int start, Rng& noise) {
    start = std::max(0, start + _drumPush);
    int dur = static_cast<int>(_sr * 0.17f);    // longer tail for thump (was 0.13)
    double decay = dur * 0.31;                   // slightly slower decay = a touch more boom
    double subDecay = dur * 0.55;                // sub layer rings longer for weight
    double phase = 0, subPhase = 0;
    // noise.next() only fires in the fixed 3ms click below, so changing dur/decay does NOT
    // shift the drum RNG stream (patterns are preserved).
    int clickLen = static_cast<int>(_sr * 0.003f);
    int end = std::min(static_cast<int>(_bufL.size()), start + dur);
    for (int i = 0; start + i < end; i++) {
        float t = static_cast<float>(i) / dur;
        phase += (127.0f - 80.0f * std::min(1.0f, t * 2.6f)) / _sr;   // pitch drop 127→47
        subPhase += 44.0f / _sr;                                      // steady sub fundamental
        float env = static_cast<float>(std::exp(-i / decay));
        float subEnv = static_cast<float>(std::exp(-i / subDecay));
        float body = static_cast<float>(std::tanh(std::sin(static_cast<float>(phase * 2 * PI)) * 1.6f)) * env;
        float sub = std::sin(static_cast<float>(subPhase * 2 * PI)) * 0.3f * subEnv;
        float click = i < clickLen ? (noise.next() * 2.0f - 1.0f) * 0.55f * (1.0f - i / static_cast<float>(clickLen)) : 0.0f;
        float v = (body + sub + click) * _c.KickVol * _drumGain;
        _bufL[start + i] += v; _bufR[start + i] += v;
    }
}

void MusicGen::renderSnare(int start, Rng& noise, bool ghost) {
    start = std::max(0, start + _drumPush);
    // dur and the single noise.next()/sample are kept exactly so the drum RNG stream is
    // unchanged — only the timbre (more shell body) is rerolled.
    int dur = static_cast<int>(_sr * (ghost ? 0.06f : 0.15f));
    double decay = dur * (ghost ? 0.3 : 0.32);
    double phase = 0, phase2 = 0;
    float amp = _c.SnareVol * (ghost ? 0.3f : 1.0f) * _drumGain;
    float a = hpCoeff(1350.0f);           // slightly crisper wire crack (was 1200)
    float inPrev = 0.0f, outPrev = 0.0f;
    int end = std::min(static_cast<int>(_bufL.size()), start + dur);
    for (int i = 0; start + i < end; i++) {
        float t = static_cast<float>(i) / dur;
        float env = static_cast<float>(std::exp(-i / decay));
        float drop = 1.0f - 0.14f * t;     // shell pitch sags a touch → "dow"
        phase += 185.0f * drop / _sr;
        phase2 += 268.0f * drop / _sr;
        float n = noise.next() * 2.0f - 1.0f;
        float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
        // two-tone shell body, a bit fuller than before, vs the wire layer
        float body = (std::sin(static_cast<float>(phase * 2 * PI)) + std::sin(static_cast<float>(phase2 * 2 * PI)) * 0.6f) * 0.375f;
        float v = (static_cast<float>(std::tanh(hp * 1.2f)) * 0.6f + body) * env * amp;
        _bufL[start + i] += v; _bufR[start + i] += v;
    }
}

void MusicGen::renderTom(int start, float baseFreq, Rng& noise) {
    (void)noise;
    start = std::max(0, start + _drumPush);
    int dur = static_cast<int>(_sr * 0.18f);
    double decay = dur * 0.3;
    double phase = 0;
    int end = std::min(static_cast<int>(_bufL.size()), start + dur);
    for (int i = 0; start + i < end; i++) {
        float t = static_cast<float>(i) / dur;
        phase += (baseFreq * (1.0f - 0.35f * t)) / _sr;
        float env = static_cast<float>(std::exp(-i / decay));
        float v = std::sin(static_cast<float>(phase * 2 * PI)) * env * _c.TomVol * _drumGain;
        _bufL[start + i] += v; _bufR[start + i] += v;
    }
}

void MusicGen::renderHat(int start, bool open, float amp, Rng& noise) {
    start = std::max(0, start + _drumPush);
    int dur = static_cast<int>(_sr * (open ? 0.16f : 0.035f));
    double decay = dur * 0.4;
    float a = hpCoeff(7000.0f);
    float inPrev = 0.0f, outPrev = 0.0f;
    int end = std::min(static_cast<int>(_bufL.size()), start + dur);
    for (int i = 0; start + i < end; i++) {
        float env = static_cast<float>(std::exp(-i / decay));
        float n = noise.next() * 2.0f - 1.0f;
        float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
        float v = hp * env * amp * _drumGain;
        _bufL[start + i] += v; _bufR[start + i] += v;
    }
}

void MusicGen::renderCrash(int start, Rng& noise) {
    start = std::max(0, start + _drumPush);
    int dur = static_cast<int>(_sr * 0.6f);
    double decay = dur * 0.45;
    float a = hpCoeff(4000.0f);
    float inPrev = 0.0f, outPrev = 0.0f;
    int end = std::min(static_cast<int>(_bufL.size()), start + dur);
    for (int i = 0; start + i < end; i++) {
        float env = static_cast<float>(std::exp(-i / decay));
        float n = noise.next() * 2.0f - 1.0f;
        float hp = a * (outPrev + n - inPrev); inPrev = n; outPrev = hp;
        float v = hp * env * _c.CrashVol * _drumGain;
        _bufL[start + i] += v; _bufR[start + i] += v;
    }
}

Song MusicGen::generate(const std::string& seed) {
    float gain = compose(seed);
    Song s; s.sampleRate = _sr;
    s.left.resize(_bufL.size());
    s.right.resize(_bufR.size());
    for (size_t i = 0; i < _bufL.size(); i++) {
        s.left[i] = _bufL[i] * gain;
        s.right[i] = _bufR[i] * gain;
    }
    return s;
}

// ── WAV writer (port of MusicGen.WavFromSamples; quantise matches ToS16/Clamp) ──
static int16_t to_s16(float v) {
    if (v < -1.0f) v = -1.0f; else if (v > 1.0f) v = 1.0f;
    return static_cast<int16_t>(v * 32767.0f);
}

std::vector<uint8_t> wav_from_song(const Song& song, bool stereo, float gain) {
    int channels = stereo ? 2 : 1;
    size_t frames = song.left.size();
    int dataSize = static_cast<int>(frames * channels * 2);
    int blockAlign = channels * 2;
    std::vector<uint8_t> b;
    b.reserve(44 + dataSize);
    auto Str = [&](const char* s) { while (*s) b.push_back(static_cast<uint8_t>(*s++)); };
    auto U32 = [&](uint32_t v) { b.push_back(v); b.push_back(v >> 8); b.push_back(v >> 16); b.push_back(v >> 24); };
    auto U16 = [&](uint16_t v) { b.push_back(v & 0xff); b.push_back(v >> 8); };
    Str("RIFF"); U32(36 + dataSize); Str("WAVE");
    Str("fmt "); U32(16); U16(1); U16(channels);
    U32(song.sampleRate); U32(song.sampleRate * blockAlign); U16(blockAlign); U16(16);
    Str("data"); U32(dataSize);
    for (size_t i = 0; i < frames; i++) {
        if (stereo) {
            uint16_t l = static_cast<uint16_t>(to_s16(song.left[i] * gain));
            uint16_t r = static_cast<uint16_t>(to_s16(song.right[i] * gain));
            b.push_back(l & 0xff); b.push_back(l >> 8);
            b.push_back(r & 0xff); b.push_back(r >> 8);
        } else {
            uint16_t m = static_cast<uint16_t>(to_s16((song.left[i] + song.right[i]) * 0.5f * gain));
            b.push_back(m & 0xff); b.push_back(m >> 8);
        }
    }
    return b;
}

// ── Config <-> vector boundary (fixed order) ──
// Each entry: a getter and setter on Config. Ints are carried as float and rounded back.
struct CfgAcc { std::function<float(const Config&)> get; std::function<void(Config&, float)> set; };

static const std::vector<CfgAcc>& cfg_accessors() {
    static const std::vector<CfgAcc> a = {
        {[](const Config& c){return (float)c.SampleRate;}, [](Config& c, float v){c.SampleRate=(int)cs_round(v);}},
        {[](const Config& c){return c.TargetSeconds;}, [](Config& c, float v){c.TargetSeconds=v;}},
        {[](const Config& c){return (float)c.Bars;}, [](Config& c, float v){c.Bars=(int)cs_round(v);}},
        {[](const Config& c){return (float)c.BpmMin;}, [](Config& c, float v){c.BpmMin=(int)cs_round(v);}},
        {[](const Config& c){return (float)c.BpmMax;}, [](Config& c, float v){c.BpmMax=(int)cs_round(v);}},
        {[](const Config& c){return c.FastChance;}, [](Config& c, float v){c.FastChance=v;}},
        {[](const Config& c){return (float)c.FastBpmMin;}, [](Config& c, float v){c.FastBpmMin=(int)cs_round(v);}},
        {[](const Config& c){return (float)c.FastBpmMax;}, [](Config& c, float v){c.FastBpmMax=(int)cs_round(v);}},
        {[](const Config& c){return c.Swing;}, [](Config& c, float v){c.Swing=v;}},
        {[](const Config& c){return c.FastSwing;}, [](Config& c, float v){c.FastSwing=v;}},
        {[](const Config& c){return c.BassVol;}, [](Config& c, float v){c.BassVol=v;}},
        {[](const Config& c){return c.SkankVol;}, [](Config& c, float v){c.SkankVol=v;}},
        {[](const Config& c){return c.OrganVol;}, [](Config& c, float v){c.OrganVol=v;}},
        {[](const Config& c){return c.MelodyVol;}, [](Config& c, float v){c.MelodyVol=v;}},
        {[](const Config& c){return c.HornVol;}, [](Config& c, float v){c.HornVol=v;}},
        {[](const Config& c){return c.KickVol;}, [](Config& c, float v){c.KickVol=v;}},
        {[](const Config& c){return c.SnareVol;}, [](Config& c, float v){c.SnareVol=v;}},
        {[](const Config& c){return c.TomVol;}, [](Config& c, float v){c.TomVol=v;}},
        {[](const Config& c){return c.HatVol;}, [](Config& c, float v){c.HatVol=v;}},
        {[](const Config& c){return c.CrashVol;}, [](Config& c, float v){c.CrashVol=v;}},
        {[](const Config& c){return c.Detune;}, [](Config& c, float v){c.Detune=v;}},
        {[](const Config& c){return c.BassCutoff;}, [](Config& c, float v){c.BassCutoff=v;}},
        {[](const Config& c){return c.SkankCutoff;}, [](Config& c, float v){c.SkankCutoff=v;}},
        {[](const Config& c){return c.SkankHighpass;}, [](Config& c, float v){c.SkankHighpass=v;}},
        {[](const Config& c){return c.LeadCutoff;}, [](Config& c, float v){c.LeadCutoff=v;}},
        {[](const Config& c){return c.Resonance;}, [](Config& c, float v){c.Resonance=v;}},
        {[](const Config& c){return c.BassDrive;}, [](Config& c, float v){c.BassDrive=v;}},
        {[](const Config& c){return c.SkankDrive;}, [](Config& c, float v){c.SkankDrive=v;}},
        {[](const Config& c){return c.MelodyDrive;}, [](Config& c, float v){c.MelodyDrive=v;}},
        {[](const Config& c){return c.HornDrive;}, [](Config& c, float v){c.HornDrive=v;}},
        {[](const Config& c){return c.MasterDrive;}, [](Config& c, float v){c.MasterDrive=v;}},
        {[](const Config& c){return c.MasterPeak;}, [](Config& c, float v){c.MasterPeak=v;}},
        {[](const Config& c){return c.OctavePopChance;}, [](Config& c, float v){c.OctavePopChance=v;}},
        {[](const Config& c){return c.OrganBubbleChance;}, [](Config& c, float v){c.OrganBubbleChance=v;}},
        {[](const Config& c){return c.KickSyncChance;}, [](Config& c, float v){c.KickSyncChance=v;}},
        {[](const Config& c){return c.GhostSnareChance;}, [](Config& c, float v){c.GhostSnareChance=v;}},
        {[](const Config& c){return c.FillChance;}, [](Config& c, float v){c.FillChance=v;}},
        {[](const Config& c){return c.DrumBusy;}, [](Config& c, float v){c.DrumBusy=v;}},
        {[](const Config& c){return c.TripletChance;}, [](Config& c, float v){c.TripletChance=v;}},
        {[](const Config& c){return c.MelodyRestChance;}, [](Config& c, float v){c.MelodyRestChance=v;}},
        {[](const Config& c){return c.MelodyLeapChance;}, [](Config& c, float v){c.MelodyLeapChance=v;}},
        {[](const Config& c){return c.MelodyVibrato;}, [](Config& c, float v){c.MelodyVibrato=v;}},
        {[](const Config& c){return c.PanAmount;}, [](Config& c, float v){c.PanAmount=v;}},
        {[](const Config& c){return c.TrumpetWeight;}, [](Config& c, float v){c.TrumpetWeight=v;}},
        {[](const Config& c){return c.SaxWeight;}, [](Config& c, float v){c.SaxWeight=v;}},
        {[](const Config& c){return c.OrganWeight;}, [](Config& c, float v){c.OrganWeight=v;}},
        {[](const Config& c){return c.TromboneWeight;}, [](Config& c, float v){c.TromboneWeight=v;}},
        {[](const Config& c){return (float)c.ForceInstrument;}, [](Config& c, float v){c.ForceInstrument=(int)cs_round(v);}},
        {[](const Config& c){return c.HornSectionChance;}, [](Config& c, float v){c.HornSectionChance=v;}},
        {[](const Config& c){return c.HornDensity;}, [](Config& c, float v){c.HornDensity=v;}},
        // appended for the QoL-polish port (keep at the end — fixed JS<->WASM order)
        {[](const Config& c){return c.DrumVol;}, [](Config& c, float v){c.DrumVol=v;}},
        {[](const Config& c){return c.SkankChop;}, [](Config& c, float v){c.SkankChop=v;}},
        {[](const Config& c){return c.OrganCutoff;}, [](Config& c, float v){c.OrganCutoff=v;}},
        {[](const Config& c){return c.OrganVibrato;}, [](Config& c, float v){c.OrganVibrato=v;}},
        {[](const Config& c){return c.HornCutoff;}, [](Config& c, float v){c.HornCutoff=v;}},
        {[](const Config& c){return c.DrumPush;}, [](Config& c, float v){c.DrumPush=v;}},
        {[](const Config& c){return c.BassTriplets;}, [](Config& c, float v){c.BassTriplets=v;}},
    };
    return a;
}

int config_vector_size() { return static_cast<int>(cfg_accessors().size()); }

std::vector<float> config_to_vector(const Config& c) {
    std::vector<float> v;
    for (auto& a : cfg_accessors()) v.push_back(a.get(c));
    return v;
}

Config config_from_vector(const std::vector<float>& v) {
    Config c;
    const auto& acc = cfg_accessors();
    for (size_t i = 0; i < acc.size() && i < v.size(); i++) acc[i].set(c, v[i]);
    return c;
}

} // namespace ska
