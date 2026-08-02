using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Spectral analysis of ride cymbal samples: sustained partials, per-band decay, envelope.
class Program
{
    static double[] LoadWav(string path, out int sr)
    {
        var b = File.ReadAllBytes(path);
        int pos = 12; sr = 44100; int dataOff = -1, dataLen = 0; int bits = 16, ch = 1;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int len = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ")
            {
                ch = BitConverter.ToInt16(b, pos + 10);
                sr = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == "data") { dataOff = pos + 8; dataLen = len; break; }
            pos += 8 + len + (len & 1);
        }
        int n = dataLen / (bits / 8) / ch;
        var x = new double[n];
        for (int i = 0; i < n; i++)
            x[i] = BitConverter.ToInt16(b, dataOff + i * 2 * ch) / 32768.0;
        return x;
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b2 = i + k + len / 2;
                    double tr = re[b2] * cr - im[b2] * ci;
                    double ti = re[b2] * ci + im[b2] * cr;
                    re[b2] = re[a] - tr; im[b2] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    static double[] Mag(double[] x, int off, int n)
    {
        var re = new double[n]; var im = new double[n];
        for (int i = 0; i < n; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
            re[i] = (off + i < x.Length ? x[off + i] : 0) * w;
        }
        Fft(re, im);
        var m = new double[n / 2];
        for (int i = 0; i < n / 2; i++) m[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
        return m;
    }

    static int Onset(double[] x)
    {
        double peak = 0; for (int i = 0; i < x.Length; i++) peak = Math.Max(peak, Math.Abs(x[i]));
        for (int i = 0; i < x.Length; i++) if (Math.Abs(x[i]) > peak * 0.3) return i;
        return 0;
    }

    static void Main(string[] args)
    {
        // --sus <seconds>: where the sustained-partial window opens, from the onset. The
        // default suits a ride; a crash's roar buries its partials for the first second or so,
        // and they only resolve once it has died back (--sus 1.5).
        double susSec = 0.33;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--sus" && i + 1 < args.Length) susSec = double.Parse(args[++i]);
            else paths.Add(args[i]);
        }

        foreach (var path in paths)
        {
            int sr;
            var x = LoadWav(path, out sr);
            int on = Onset(x);
            Console.WriteLine($"\n════ {Path.GetFileName(path)}  sr={sr}  len={x.Length / (double)sr:0.00}s  onset={on / (double)sr:0.000}s");

            // ── Sustained partials: long FFT over the ring, starting well past the attack ──
            int N = 131072; // ~3 s at 44.1k, ~0.34 Hz bins
            int sus = on + (int)(sr * susSec);
            var m = Mag(x, sus, N);
            double binHz = sr / (double)N;
            // smoothed baseline (median-ish over ±60 bins), peaks must clear it by 12 dB
            var floor = new double[m.Length];
            int W = 60;
            for (int i = 0; i < m.Length; i++)
            {
                double s = 0; int c = 0;
                for (int j = Math.Max(0, i - W); j < Math.Min(m.Length, i + W); j++) { s += m[j]; c++; }
                floor[i] = s / c;
            }
            var peaks = new List<(double hz, double db, double rel)>();
            int lo = (int)(150 / binHz), hi = (int)(14000 / binHz);
            for (int i = lo; i < hi && i < m.Length - 2; i++)
            {
                if (m[i] > m[i - 1] && m[i] > m[i + 1] && m[i] > m[i - 2] && m[i] > m[i + 2]
                    && m[i] > floor[i] * 4.0)
                {
                    // parabolic interp
                    double a = Math.Log(m[i - 1]), b0 = Math.Log(m[i]), c0 = Math.Log(m[i + 1]);
                    double d = 0.5 * (a - c0) / (a - 2 * b0 + c0 + 1e-12);
                    peaks.Add(((i + d) * binHz, 20 * Math.Log10(m[i] + 1e-12), 20 * Math.Log10(m[i] / (floor[i] + 1e-12))));
                }
            }
            var top = peaks.OrderByDescending(p => p.db).Take(40).OrderBy(p => p.hz).ToList();
            double max = top.Count > 0 ? top.Max(p => p.db) : 0;
            Console.WriteLine($"  SUSTAINED PARTIALS ({susSec:0.00}s in, 3s window; dB rel strongest, prominence over local floor):");
            foreach (var p in top)
                Console.WriteLine($"    {p.hz,8:0.0} Hz   {p.db - max,6:0.0} dB   prom {p.rel,5:0.0} dB");

            // ── Per-band decay: STFT, fit log-RMS slope ──
            int win = 4096, hop = 1024;
            int frames = (x.Length - on - win) / hop;
            frames = Math.Min(frames, (int)(5.0 * sr / hop));
            double[][] spec = new double[frames][];
            for (int f = 0; f < frames; f++) spec[f] = Mag(x, on + f * hop, win);
            double bhz = sr / (double)win;
            Console.WriteLine("  BAND DECAY (τ of exponential fit, and level in the 50–400ms window):");
            double[] edges = { 200, 300, 450, 700, 1000, 1500, 2200, 3200, 4700, 6800, 10000, 14000 };
            for (int e = 0; e + 1 < edges.Length; e++)
            {
                int b0 = (int)(edges[e] / bhz), b1 = (int)(edges[e + 1] / bhz);
                var lev = new double[frames];
                for (int f = 0; f < frames; f++)
                {
                    double s = 0; for (int i = b0; i < b1 && i < spec[f].Length; i++) s += spec[f][i] * spec[f][i];
                    lev[f] = Math.Sqrt(s / Math.Max(1, b1 - b0));
                }
                double pk = lev.Max();
                // fit from -6dB to -40dB rel peak
                var ts = new List<double>(); var ys = new List<double>();
                for (int f = 0; f < frames; f++)
                {
                    double rel = lev[f] / (pk + 1e-15);
                    if (rel < 0.5 && rel > 0.01) { ts.Add(f * hop / (double)sr); ys.Add(Math.Log(lev[f] + 1e-15)); }
                }
                double tau = double.NaN;
                if (ts.Count > 4)
                {
                    double mt = ts.Average(), my = ys.Average();
                    double num = 0, den = 0;
                    for (int i = 0; i < ts.Count; i++) { num += (ts[i] - mt) * (ys[i] - my); den += (ts[i] - mt) * (ts[i] - mt); }
                    double slope = num / den;
                    tau = -1.0 / slope;
                }
                int f0 = (int)(0.05 * sr / hop), f1 = (int)(0.4 * sr / hop);
                double early = 0; int cc = 0;
                for (int f = f0; f < f1 && f < frames; f++) { early += lev[f]; cc++; }
                early /= Math.Max(1, cc);
                Console.WriteLine($"    {edges[e],6:0}–{edges[e + 1],-6:0} Hz  τ = {tau,6:0.00} s   lvl {20 * Math.Log10(early + 1e-12),6:0.0} dB");
            }

            // ── Attack vs sustain tilt: 20ms window at onset vs the sustain spectrum ──
            var atk = Mag(x, on, 2048);
            Console.WriteLine("  ATTACK (first 46ms) band levels:");
            double abhz = sr / 2048.0;
            for (int e = 0; e + 1 < edges.Length; e++)
            {
                int b0 = (int)(edges[e] / abhz), b1 = Math.Max((int)(edges[e + 1] / abhz), (int)(edges[e] / abhz) + 1);
                double s = 0; for (int i = b0; i < b1 && i < atk.Length; i++) s += atk[i] * atk[i];
                s = Math.Sqrt(s / (b1 - b0));
                Console.WriteLine($"    {edges[e],6:0}–{edges[e + 1],-6:0} Hz  {20 * Math.Log10(s + 1e-12),6:0.0} dB");
            }
        }
    }
}

