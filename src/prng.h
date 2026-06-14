// Portable PRNG — xmur3 seed hash + a mulberry32-style stream.
// PARITY-CRITICAL: this must reproduce reference/MusicGen.cs's Xmur3 + Rng bit-for-bit.
// All arithmetic is 32-bit unsigned with natural wraparound (uint32_t); never widen
// mid-hash. Next() returns (t ^ (t >> 14)) / 4294967296f as a float in [0,1).
#pragma once
#include <cstdint>
#include <string>
#include <cmath>
#include <vector>

namespace ska {

// xmur3(string) -> uint32 seed. Mirror of MusicGen.Xmur3.
inline uint32_t xmur3(const std::string& str) {
    uint32_t h = 1779033703u ^ static_cast<uint32_t>(str.size());
    for (unsigned char ch : str) {
        h = (h ^ ch) * 3432918353u;
        h = (h << 13) | (h >> 19);
    }
    h = (h ^ (h >> 16)) * 2246822507u;
    h = (h ^ (h >> 13)) * 3266489909u;
    return h ^ (h >> 16);
}

// Mulberry32-style stream. Mirror of MusicGen.Rng.
struct Rng {
    uint32_t a;
    explicit Rng(uint32_t seed) : a(seed) {}

    float next() {
        a = a + 0x6D2B79F5u;
        uint32_t t = a;
        t = (t ^ (t >> 15)) * (t | 1u);
        t ^= t + (t ^ (t >> 7)) * (t | 61u);
        return static_cast<float>(t ^ (t >> 14)) / 4294967296.0f;
    }

    // n<=0 -> 0; else min(n-1, (int)(next()*n)). Truncation toward zero, as in C#.
    int next_int(int n) {
        if (n <= 0) return 0;
        int v = static_cast<int>(next() * n);
        return v < n - 1 ? v : n - 1;
    }

    bool chance(float p) { return next() < p; }

    template <typename T>
    const T& pick(const std::vector<T>& arr) { return arr[next_int(static_cast<int>(arr.size()))]; }
};

} // namespace ska
