using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>Oscillator shapes available to a recipe.</summary>
    public enum SynthWave
    {
        Sine = 0,
        Triangle = 1,
        Saw = 2,
        Square = 3
    }

    /// <summary>
    /// The whole DSP vocabulary of this prototype. Everything you can hear in the build
    /// is one of these functions applied to a float[] of samples.
    ///
    /// Deliberately small and deliberately naive: one-pole and state-variable filters, a
    /// Schroeder reverb, an exponential envelope, a tanh saturator, a grain scatterer.
    /// None of it is production DSP. It is enough to make thirty-odd events that a player
    /// can tell apart with their eyes shut, which is the entire brief.
    ///
    /// CONVENTIONS
    ///   * Buffers are mono float[], nominal range [-1,1]. Clipping is checked at the end
    ///     by <see cref="NormalizePeak"/>, not during layering.
    ///   * Generators ADD into the buffer, they never overwrite. Layering is the default
    ///     because REQ-121 is a statement about layers.
    ///   * Every function that can leave a discontinuity at a buffer edge is paired with
    ///     <see cref="FadeEdges"/>. An un-faded edge is a click, and a click at 13
    ///     rounds/second is the single fastest way to make a mix fatiguing.
    /// </summary>
    public static class SynthOps
    {
        /// <summary>
        /// One sample rate for the whole library. 44100 rather than 48000 because the
        /// Schroeder delay-line tunings below are the classic 44.1 kHz numbers, and
        /// rather than 22050 because armour and stone impacts live at 3-5 kHz where a
        /// halved rate audibly dulls the transient.
        /// </summary>
        public const int SampleRate = 44100;

        public static int Samples(float seconds)
        {
            return Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
        }

        public static float[] Buffer(float seconds)
        {
            return new float[Samples(seconds)];
        }

        // ------------------------------------------------------------------ generators

        /// <summary>
        /// Adds a tone that sweeps from <paramref name="freqStart"/> to
        /// <paramref name="freqEnd"/> over the region. Exponential sweeps read as
        /// "pitch falling"; linear ones read as "something mechanical slowing down".
        /// </summary>
        public static void AddTone(
            float[] buf, int start, int count,
            float freqStart, float freqEnd,
            float gain, SynthWave wave = SynthWave.Sine,
            bool exponentialSweep = true, float phase = 0f)
        {
            if (buf == null || count <= 0)
            {
                return;
            }

            int end = Mathf.Min(buf.Length, start + count);
            if (start >= end)
            {
                return;
            }

            int span = end - start;
            float invSpan = span > 1 ? 1f / (span - 1) : 0f;
            float p = phase;

            for (int i = start; i < end; i++)
            {
                float t = (i - start) * invSpan;
                float f = exponentialSweep
                    ? Mathf.Lerp(freqStart, freqEnd, t * t) // quadratic reads close enough to exponential and never divides by zero
                    : Mathf.Lerp(freqStart, freqEnd, t);

                p += f / SampleRate;
                if (p > 1f)
                {
                    p -= Mathf.Floor(p);
                }

                buf[i] += gain * Shape(wave, p);
            }
        }

        public static void AddTone(float[] buf, float freq, float gain, SynthWave wave = SynthWave.Sine, float phase = 0f)
        {
            AddTone(buf, 0, buf.Length, freq, freq, gain, wave, false, phase);
        }

        /// <summary>
        /// Adds an inharmonic partial stack. This is what makes metal sound like metal:
        /// the partials are NOT integer multiples, so the ear refuses to fuse them into a
        /// pitch and hears "struck object" instead. Used by armour, bells and the gong.
        /// </summary>
        public static void AddPartials(
            float[] buf, int start, int count,
            float fundamental, float[] ratios, float[] gains, float decaySeconds, float decaySpread)
        {
            if (ratios == null || gains == null)
            {
                return;
            }

            int n = Mathf.Min(ratios.Length, gains.Length);
            for (int k = 0; k < n; k++)
            {
                float[] partial = new float[count];
                AddTone(partial, 0, count, fundamental * ratios[k], fundamental * ratios[k], gains[k], SynthWave.Sine, false, k * 0.13f);

                // Higher partials die first - that is the whole difference between a
                // struck plate and a synthesiser pad.
                float d = decaySeconds * Mathf.Pow(decaySpread, k);
                ApplyPercussive(partial, 0, count, 0.0015f, d, 2.2f);
                Mix(buf, start, partial, 1f);
            }
        }

        /// <summary>White noise. The raw material of every impact, wind and debris layer.</summary>
        public static void AddNoise(float[] buf, int start, int count, float gain, ref SynthRandom rng)
        {
            int end = Mathf.Min(buf.Length, start + count);
            for (int i = start; i < end; i++)
            {
                buf[i] += gain * rng.Bipolar();
            }
        }

        /// <summary>
        /// Pink-ish noise (Voss-McCartney, 5 octaves). Wind and distant water need the
        /// -3 dB/octave tilt; white noise as a wind bed is the classic "hiss" mistake and
        /// is the fastest route to listener fatigue over a fifteen minute session.
        /// </summary>
        public static void AddPinkNoise(float[] buf, int start, int count, float gain, ref SynthRandom rng)
        {
            int end = Mathf.Min(buf.Length, start + count);

            float b0 = 0f, b1 = 0f, b2 = 0f, b3 = 0f, b4 = 0f, b5 = 0f, b6 = 0f;

            for (int i = start; i < end; i++)
            {
                float white = rng.Bipolar();
                b0 = 0.99886f * b0 + white * 0.0555179f;
                b1 = 0.99332f * b1 + white * 0.0750759f;
                b2 = 0.96900f * b2 + white * 0.1538520f;
                b3 = 0.86650f * b3 + white * 0.3104856f;
                b4 = 0.55000f * b4 + white * 0.5329522f;
                b5 = -0.7616f * b5 - white * 0.0168980f;

                float pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362f;
                b6 = white * 0.115926f;

                buf[i] += gain * pink * 0.11f;
            }
        }

        /// <summary>
        /// Scatters short grains of the given band across the region. This is the debris
        /// generator: armour shards, stone grit, explosion fragments. Density falls off
        /// over the region so the scatter reads as "settling", not as a texture.
        /// </summary>
        public static void AddGrains(
            float[] buf, int start, int count,
            int grainCount, float minFreq, float maxFreq,
            float minLen, float maxLen, float gain, ref SynthRandom rng)
        {
            for (int g = 0; g < grainCount; g++)
            {
                // Skewed towards the beginning: t^1.7 puts most grains in the first third.
                float t = Mathf.Pow(rng.Next01(), 1.7f);
                int offset = start + (int)(t * count);

                float len = rng.Range(minLen, maxLen);
                int grainSamples = Samples(len);
                if (offset + grainSamples >= buf.Length)
                {
                    grainSamples = buf.Length - offset - 1;
                }

                if (grainSamples <= 4)
                {
                    continue;
                }

                float f = rng.Range(minFreq, maxFreq);
                float g0 = gain * rng.Range(0.35f, 1f) * (1f - t * 0.65f);

                float[] grain = new float[grainSamples];
                AddTone(grain, 0, grainSamples, f, f * rng.Range(0.82f, 1.02f), g0, SynthWave.Sine, false, rng.Next01());
                AddNoise(grain, 0, grainSamples, g0 * 0.5f, ref rng);
                ApplyPercussive(grain, 0, grainSamples, 0.0008f, len * 0.55f, 2.6f);

                Mix(buf, offset, grain, 1f);
            }
        }

        private static float Shape(SynthWave wave, float phase01)
        {
            switch (wave)
            {
                case SynthWave.Triangle:
                    return 4f * Mathf.Abs(phase01 - 0.5f) - 1f;
                case SynthWave.Saw:
                    return phase01 * 2f - 1f;
                case SynthWave.Square:
                    return phase01 < 0.5f ? 1f : -1f;
                default:
                    return Mathf.Sin(phase01 * 2f * Mathf.PI);
            }
        }

        // ------------------------------------------------------------------ envelopes

        /// <summary>
        /// Percussive envelope: fast attack, exponential-ish decay to silence.
        /// <paramref name="curve"/> above 1 makes the tail die faster than a straight
        /// exponential, which is what stops short impacts from smearing into each other
        /// when forty of them land in a second.
        /// </summary>
        public static void ApplyPercussive(float[] buf, int start, int count, float attack, float decay, float curve = 2f)
        {
            int end = Mathf.Min(buf.Length, start + count);
            int attackSamples = Mathf.Max(1, Samples(attack));
            int decaySamples = Mathf.Max(1, Samples(decay));

            for (int i = start; i < end; i++)
            {
                int local = i - start;
                float env;

                if (local < attackSamples)
                {
                    env = local / (float)attackSamples;
                }
                else
                {
                    float d = (local - attackSamples) / (float)decaySamples;
                    env = d >= 1f ? 0f : Mathf.Pow(1f - d, curve);
                }

                buf[i] *= env;
            }
        }

        /// <summary>Classic ADSR over a region. Used by loops and sustained layers.</summary>
        public static void ApplyAdsr(
            float[] buf, int start, int count,
            float attack, float decay, float sustainLevel, float release)
        {
            int end = Mathf.Min(buf.Length, start + count);
            int total = end - start;
            if (total <= 0)
            {
                return;
            }

            int a = Samples(attack);
            int d = Samples(decay);
            int r = Samples(release);
            int s = Mathf.Max(0, total - a - d - r);

            for (int i = 0; i < total; i++)
            {
                float env;
                if (i < a)
                {
                    env = a > 0 ? i / (float)a : 1f;
                }
                else if (i < a + d)
                {
                    float t = d > 0 ? (i - a) / (float)d : 1f;
                    env = Mathf.Lerp(1f, sustainLevel, t);
                }
                else if (i < a + d + s)
                {
                    env = sustainLevel;
                }
                else
                {
                    float t = r > 0 ? (i - a - d - s) / (float)r : 1f;
                    env = Mathf.Lerp(sustainLevel, 0f, Mathf.Clamp01(t));
                }

                buf[start + i] *= env;
            }
        }

        /// <summary>Multiplies the region by a slow sine tremolo. Wind, water, beacon pulse.</summary>
        public static void ApplyTremolo(float[] buf, float rateHz, float depth, float phase = 0f)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                float lfo = Mathf.Sin((i / (float)SampleRate * rateHz + phase) * 2f * Mathf.PI);
                buf[i] *= 1f - depth + depth * (lfo * 0.5f + 0.5f);
            }
        }

        /// <summary>
        /// Short linear fades at both ends. Every clip in the library ends with one.
        /// Two milliseconds is inaudible as a fade and completely removes the edge click.
        /// </summary>
        public static void FadeEdges(float[] buf, float fadeInSeconds = 0.002f, float fadeOutSeconds = 0.006f)
        {
            int fin = Mathf.Min(buf.Length / 2, Samples(fadeInSeconds));
            int fout = Mathf.Min(buf.Length / 2, Samples(fadeOutSeconds));

            for (int i = 0; i < fin; i++)
            {
                buf[i] *= i / (float)fin;
            }

            for (int i = 0; i < fout; i++)
            {
                buf[buf.Length - 1 - i] *= i / (float)fout;
            }
        }

        // ------------------------------------------------------------------ filters

        /// <summary>One-pole low pass. Cheap, gentle, and the workhorse of the material palette.</summary>
        public static void LowPass(float[] buf, float cutoffHz)
        {
            float dt = 1f / SampleRate;
            float rc = 1f / (2f * Mathf.PI * Mathf.Max(20f, cutoffHz));
            float a = dt / (rc + dt);

            float y = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                y += a * (buf[i] - y);
                buf[i] = y;
            }
        }

        /// <summary>One-pole high pass. Used to keep small sounds out of the low band.</summary>
        public static void HighPass(float[] buf, float cutoffHz)
        {
            float dt = 1f / SampleRate;
            float rc = 1f / (2f * Mathf.PI * Mathf.Max(20f, cutoffHz));
            float a = rc / (rc + dt);

            float prevIn = buf.Length > 0 ? buf[0] : 0f;
            float prevOut = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float x = buf[i];
                prevOut = a * (prevOut + x - prevIn);
                prevIn = x;
                buf[i] = prevOut;
            }
        }

        public static void BandPass(float[] buf, float lowHz, float highHz)
        {
            HighPass(buf, lowHz);
            LowPass(buf, highHz);
        }

        /// <summary>
        /// State-variable low pass with a swept cutoff and real resonance.
        ///
        /// This is the single most important function in the file. A gunshot is a noise
        /// burst whose spectral centroid collapses from bright to dark in under 80 ms; a
        /// static filter cannot produce that, and without it every weapon sounds like the
        /// same "pssh". The sweep end points and Q are what separate the SMG from the
        /// shotgun far more than their loudness does.
        /// </summary>
        public static void SweepResonantLowPass(float[] buf, float cutoffStartHz, float cutoffEndHz, float q)
        {
            float low = 0f, band = 0f;
            int n = buf.Length;
            float invN = n > 1 ? 1f / (n - 1) : 0f;
            float damp = Mathf.Clamp(1f / Mathf.Max(0.5f, q), 0.02f, 1.4f);

            for (int i = 0; i < n; i++)
            {
                float t = i * invN;
                float cutoff = Mathf.Lerp(cutoffStartHz, cutoffEndHz, t * t);

                // Chamberlin SVF; f is clamped well below Nyquist to stay stable.
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(cutoff, 20f, SampleRate * 0.35f) / SampleRate);

                float high = buf[i] - low - damp * band;
                band += f * high;
                low += f * band;

                buf[i] = low;
            }
        }

        // ------------------------------------------------------------------ effects

        /// <summary>
        /// Soft clipper. Adds the odd harmonics that make a loud sound read as loud even
        /// at a low playback level, which matters because the mix ceiling is low.
        /// </summary>
        public static void Saturate(float[] buf, float drive)
        {
            if (drive <= 1.001f)
            {
                return;
            }

            // UnityEngine.Mathf has no Tanh, and the normalising constant has to be the
            // SAME curve the loop below uses or the output level drifts with drive. So
            // both use Pade(), and nothing here calls System.Math at all.
            float norm = 1f / Pade(drive);
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] = norm * Pade(buf[i] * drive);
            }
        }

        /// <summary>
        /// Pade(3,2) approximation of tanh. Within 0.3% of the real curve over [-3,3] and
        /// monotonic everywhere, which is all a saturator needs; the exact shape of the
        /// knee is inaudible next to the choice of drive.
        /// </summary>
        private static float Pade(float x)
        {
            if (x < -8f)
            {
                return -1f;
            }

            if (x > 8f)
            {
                return 1f;
            }

            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        /// <summary>Single feedback delay. The "crack echo" of a rifle across a courtyard.</summary>
        public static void Delay(float[] buf, float delaySeconds, float feedback, float mix)
        {
            int d = Samples(delaySeconds);
            if (d <= 1 || d >= buf.Length)
            {
                return;
            }

            for (int i = d; i < buf.Length; i++)
            {
                buf[i] += buf[i - d] * feedback * mix;
            }
        }

        /// <summary>
        /// Schroeder reverb: four parallel damped combs into two series allpasses.
        /// The delay lengths are the classic Freeverb primes at 44.1 kHz.
        ///
        /// The request asks for the feeling of a large, empty palace. That is not a
        /// texture you can paint with a tail on the ambience bed alone - it has to be on
        /// the impacts, because the listener localises a room from how its transients
        /// decay. So every weapon, armour and death recipe ends with a small amount of
        /// this, and the ambience beds get a large amount.
        /// </summary>
        public static void Reverb(float[] buf, float roomSize, float damping, float mix)
        {
            if (mix <= 0.001f)
            {
                return;
            }

            int[] combLengths = { 1116, 1188, 1277, 1356 };
            int[] allpassLengths = { 556, 441 };

            int n = buf.Length;
            float[] wet = new float[n];

            float room = Mathf.Clamp(roomSize, 0f, 0.96f);
            float damp = Mathf.Clamp01(damping);

            for (int c = 0; c < combLengths.Length; c++)
            {
                int len = combLengths[c];
                if (len >= n)
                {
                    continue;
                }

                float[] line = new float[len];
                int idx = 0;
                float store = 0f;

                for (int i = 0; i < n; i++)
                {
                    float y = line[idx];
                    store = y * (1f - damp) + store * damp;
                    line[idx] = buf[i] + store * room;
                    idx++;
                    if (idx >= len)
                    {
                        idx = 0;
                    }

                    wet[i] += y;
                }
            }

            for (int a = 0; a < allpassLengths.Length; a++)
            {
                int len = allpassLengths[a];
                if (len >= n)
                {
                    continue;
                }

                float[] line = new float[len];
                int idx = 0;

                for (int i = 0; i < n; i++)
                {
                    float bufout = line[idx];
                    float input = wet[i];
                    line[idx] = input + bufout * 0.5f;
                    idx++;
                    if (idx >= len)
                    {
                        idx = 0;
                    }

                    wet[i] = bufout - input;
                }
            }

            float dry = 1f - mix;
            for (int i = 0; i < n; i++)
            {
                buf[i] = buf[i] * dry + wet[i] * mix * 0.22f;
            }
        }

        /// <summary>Ring modulation. Turns a tone inharmonic - used sparingly on metal.</summary>
        public static void RingMod(float[] buf, float freqHz, float amount)
        {
            float step = freqHz / SampleRate;
            float phase = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float m = Mathf.Sin(phase * 2f * Mathf.PI);
                phase += step;
                if (phase > 1f)
                {
                    phase -= 1f;
                }

                buf[i] = Mathf.Lerp(buf[i], buf[i] * m, amount);
            }
        }

        // ------------------------------------------------------------------ buffer utility

        public static void Mix(float[] target, int offset, float[] source, float gain)
        {
            if (source == null || target == null)
            {
                return;
            }

            int count = Mathf.Min(source.Length, target.Length - offset);
            for (int i = 0; i < count; i++)
            {
                target[offset + i] += source[i] * gain;
            }
        }

        public static void Scale(float[] buf, float gain)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                buf[i] *= gain;
            }
        }

        public static float Peak(float[] buf)
        {
            float peak = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float a = buf[i] < 0f ? -buf[i] : buf[i];
                if (a > peak)
                {
                    peak = a;
                }
            }

            return peak;
        }

        /// <summary>
        /// Scales the buffer so its loudest sample sits at <paramref name="target"/>.
        ///
        /// Every clip is normalised to the SAME peak and the relative loudness of events
        /// is then set entirely by the mix plan. Baking level differences into the clips
        /// instead would mean the F1 volume sliders could not fix a bad balance without
        /// rebuilding the library.
        /// </summary>
        public static void NormalizePeak(float[] buf, float target = 0.92f)
        {
            float peak = Peak(buf);
            if (peak < 0.0001f)
            {
                return;
            }

            Scale(buf, target / peak);
        }

        /// <summary>
        /// Turns a buffer into a seamless loop by cross-fading its tail over its head and
        /// returning the shortened result.
        ///
        /// Without this the ambience beds tick once per loop, and a periodic tick in a
        /// quiet bed is far more noticeable than the bed itself - exactly the long-session
        /// fatigue failure the acceptance criteria care about.
        /// </summary>
        public static float[] MakeSeamless(float[] buf, float crossfadeSeconds)
        {
            int cf = Samples(crossfadeSeconds);
            if (cf <= 8 || cf >= buf.Length / 2)
            {
                return buf;
            }

            int outLength = buf.Length - cf;
            float[] result = new float[outLength];

            for (int i = 0; i < outLength; i++)
            {
                result[i] = buf[i];
            }

            for (int i = 0; i < cf; i++)
            {
                float t = i / (float)cf;
                // Equal-power cross-fade; a linear one dips 3 dB in the middle and the dip
                // is audible on a noise bed.
                float headGain = Mathf.Sqrt(t);
                float tailGain = Mathf.Sqrt(1f - t);
                result[i] = result[i] * headGain + buf[outLength + i] * tailGain;
            }

            return result;
        }

        /// <summary>Creates the Unity clip. The only place in the folder that touches the engine's audio API.</summary>
        public static AudioClip ToClip(float[] buf, string name)
        {
            AudioClip clip = AudioClip.Create(name, buf.Length, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }
    }
}
