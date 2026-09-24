using System;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-021 · ITU-R BS.1770-4 integrated loudness (K-weighted, absolute + relative
    /// gated) and a 4x-oversampled true-peak estimate, on raw float samples.
    ///
    /// Used by the Editor catalog tool to stamp the manifest and by the EditMode test to
    /// prove the stamp against the clip that actually shipped (the "LUFS/tepe manifest
    /// testi"). Coefficients are the 48 kHz set; other rates are resampled to 48 kHz by
    /// nearest neighbour first, which is accurate to well under 0.5 LU for the material
    /// this measures (the standard's own tolerance is ±1 LU).
    /// </summary>
    public static class LoudnessMeter
    {
        public const int ReferenceRate = 48000;

        // Stage 1: high-shelf (pre-filter), 48 kHz.
        private const double PreB0 = 1.53512485958697, PreB1 = -2.69169618940638, PreB2 = 1.19839281085285;
        private const double PreA1 = -1.69065929318241, PreA2 = 0.73248077421585;

        // Stage 2: RLB high-pass, 48 kHz.
        private const double RlbB0 = 1.0, RlbB1 = -2.0, RlbB2 = 1.0;
        private const double RlbA1 = -1.99004745483398, RlbA2 = 0.99007225036621;

        public struct Result
        {
            public float IntegratedLufs;
            public float TruePeakDb;
            public float Seconds;
            public bool Silent;
        }

        /// <summary>Measures a clip (mixed to mono). Reads the sample data; Editor/test use, not per frame.</summary>
        public static Result Measure(AudioClip clip)
        {
            if (clip == null || clip.samples == 0)
            {
                return new Result { IntegratedLufs = float.NegativeInfinity, TruePeakDb = float.NegativeInfinity, Silent = true };
            }

            float[] data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            float[] mono = ToMono(data, clip.channels);
            return Measure(mono, clip.frequency);
        }

        public static Result Measure(float[] mono, int sampleRate)
        {
            float[] x = sampleRate == ReferenceRate ? mono : ResampleNearest(mono, sampleRate, ReferenceRate);
            var result = new Result { Seconds = x.Length / (float)ReferenceRate };
            result.TruePeakDb = TruePeak(x);
            result.IntegratedLufs = Integrated(x);
            result.Silent = float.IsNegativeInfinity(result.IntegratedLufs);
            return result;
        }

        /// <summary>
        /// WP-068 s11: ungated K-weighted loudness of one short window (LUFS-style, −0.691 + 10·log10 mean-square) -
        /// for comparing a UI cue against the bed in LU on output snapshots too short for the 400 ms gating blocks.
        /// </summary>
        public static float WindowLoudness(float[] mono, int sampleRate)
        {
            float[] x = sampleRate == ReferenceRate ? mono : ResampleNearest(mono, sampleRate, ReferenceRate);
            float[] y = KWeight(x);
            double s = 0;
            for (int i = 0; i < y.Length; i++) s += (double)y[i] * y[i];
            return (float)(-0.691 + 10.0 * Math.Log10(s / Math.Max(1, y.Length) + 1e-20));
        }

        public static float[] ToMono(float[] interleaved, int channels)
        {
            if (channels <= 1)
            {
                return interleaved;
            }

            int frames = interleaved.Length / channels;
            float[] mono = new float[frames];
            float inv = 1f / channels;
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                int b = i * channels;
                for (int c = 0; c < channels; c++)
                {
                    sum += interleaved[b + c];
                }

                mono[i] = sum * inv;
            }

            return mono;
        }

        private static float[] ResampleNearest(float[] x, int from, int to)
        {
            int n = (int)((long)x.Length * to / from);
            float[] y = new float[Math.Max(1, n)];
            for (int i = 0; i < y.Length; i++)
            {
                int j = (int)((long)i * from / to);
                y[i] = x[Math.Min(j, x.Length - 1)];
            }

            return y;
        }

        private static float[] KWeight(float[] x)
        {
            float[] y = new float[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double xi = x[i];
                double yi = PreB0 * xi + PreB1 * x1 + PreB2 * x2 - PreA1 * y1 - PreA2 * y2;
                x2 = x1; x1 = xi; y2 = y1; y1 = yi;
                y[i] = (float)yi;
            }

            x1 = x2 = y1 = y2 = 0;
            for (int i = 0; i < y.Length; i++)
            {
                double xi = y[i];
                double yi = RlbB0 * xi + RlbB1 * x1 + RlbB2 * x2 - RlbA1 * y1 - RlbA2 * y2;
                x2 = x1; x1 = xi; y2 = y1; y1 = yi;
                y[i] = (float)yi;
            }

            return y;
        }

        private static float Integrated(float[] x)
        {
            float[] k = KWeight(x);
            int block = (int)(0.4f * ReferenceRate);
            int hop = (int)(0.1f * ReferenceRate);
            if (k.Length < block)
            {
                Array.Resize(ref k, block);
            }

            int n = 1 + (k.Length - block) / hop;
            double[] ms = new double[n];
            double[] lk = new double[n];
            for (int b = 0; b < n; b++)
            {
                double sum = 0;
                int start = b * hop;
                for (int i = 0; i < block; i++)
                {
                    double v = k[start + i];
                    sum += v * v;
                }

                ms[b] = sum / block;
                lk[b] = -0.691 + 10.0 * Math.Log10(ms[b] + 1e-20);
            }

            double acc = 0; int cnt = 0;
            for (int b = 0; b < n; b++)
            {
                if (lk[b] > -70.0) { acc += ms[b]; cnt++; }
            }

            if (cnt == 0)
            {
                return float.NegativeInfinity;
            }

            double rel = -0.691 + 10.0 * Math.Log10(acc / cnt) - 10.0;
            acc = 0; cnt = 0;
            for (int b = 0; b < n; b++)
            {
                if (lk[b] > -70.0 && lk[b] > rel) { acc += ms[b]; cnt++; }
            }

            return cnt == 0 ? float.NegativeInfinity : (float)(-0.691 + 10.0 * Math.Log10(acc / cnt));
        }

        /// <summary>4x oversampled peak via 4-tap linear+cubic interpolation, in dBTP.</summary>
        private static float TruePeak(float[] x)
        {
            double peak = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double a = i > 0 ? x[i - 1] : x[i];
                double b = x[i];
                double c = i + 1 < x.Length ? x[i + 1] : x[i];
                double d = i + 2 < x.Length ? x[i + 2] : c;
                peak = Math.Max(peak, Math.Abs(b));
                for (int s = 1; s < 4; s++)
                {
                    double t = s / 4.0;
                    // Catmull-Rom between b and c
                    double v = 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
                    peak = Math.Max(peak, Math.Abs(v));
                }
            }

            return peak <= 1e-9 ? float.NegativeInfinity : (float)(20.0 * Math.Log10(peak));
        }
    }
}
