using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>WP-097 K1 (03 §1): one slapback reflection of the player's shot — a delayed, darker, quieter copy from one direction.</summary>
    public struct SlapTap
    {
        /// <summary>Probe ray index (0–7, <see cref="AcousticProbe.Direction"/>).</summary>
        public int Ray;

        /// <summary>World-fixed unit direction of the reflecting wall (the copy sounds from listener + Dir · <see cref="WeaponReflections.SlapPanMeters"/>).</summary>
        public Vector3 Dir;

        /// <summary>Metres to the wall (one way).</summary>
        public float Distance;

        /// <summary>Seconds after the dry shot (2d / 343).</summary>
        public float Delay;

        /// <summary>dB against the dry shot, capped at <see cref="WeaponReflections.SlapCeilingDb"/>.</summary>
        public float GainDb;

        /// <summary>Fixed low-pass cutoff (Hz).</summary>
        public float CutoffHz;
    }

    /// <summary>
    /// WP-097 K1 · top-down weapon reflections (04 decision 1, 6, 7; 03 §1–§2). Pure static arithmetic so the EditMode tests can
    /// pin every number; the playback lives in <see cref="WeaponFireAudio"/> (Reflect pool) and the geometry in
    /// <see cref="AcousticProbe"/>.
    ///
    /// SLAPBACK: a wall d metres away returns the shot after the round trip 2d at 343 m/s, spherically spread over 2d
    /// (−20·log10(2d) dB, −6.02 dB per doubling), less 1 dB wall loss, plus a single +9 dB "top-down hyper-real" lift the sound
    /// designer owns. Air and material darken it: cutoff 16 kHz·10^(−2d/80), clamped 2.5–16 kHz. Taps under −30 dB drop; no tap
    /// may be louder than −10 dB (true-peak headroom). Taps are chosen greedily nearest first, at least 90° apart (neighbouring
    /// rays of one wall are one reflection) and at least 6 ms apart (no comb filter); three for a single shot, two per string
    /// shot, a string's slap set at most every 0.12 s.
    ///
    /// <see cref="Enabled"/> is the one switch: false = the R2 weapon path exactly (no slap, no echo, no string tail bed, no
    /// probe guess) — the S4 room-tail measurement and the old listening kit run with it off, so their definition does not move.
    /// </summary>
    public static class WeaponReflections
    {
        /// <summary>
        /// WP-097 K1 decision 7: top-down reflections on (default). Off = R2 behaviour (S4 MekanKuyrugu_EDC_Olcum and the
        /// SilahKiti listening kit set it false and restore true in their finally). The mix values of decisions 4 / 5 / 5b
        /// (open wet −6, world duck, spatial 0.20) are not reflections and do not follow it.
        /// </summary>
        public static bool Enabled = true;

        public const float SpeedOfSound = 343f;

        /// <summary>One wall bounce (dB).</summary>
        public const float WallLossDb = 1f;

        /// <summary>The top-down "hyper-real" lift on every tap (dB). The sound designer's one knob.</summary>
        public const float SlapLiftDb = 9f;

        /// <summary>Taps under this (dB against the dry shot) are not played.</summary>
        public const float SlapFloorDb = -30f;

        /// <summary>No tap louder than this (dB against the dry shot): true-peak headroom (R3 margin plan, 0.26 dB TP pay).</summary>
        public const float SlapCeilingDb = -10f;

        public const float CutoffMaxHz = 16000f;
        public const float CutoffMinHz = 2500f;

        /// <summary>Path length (m) per decade of cutoff loss: 16 kHz·10^(−2d/80).</summary>
        public const float CutoffDecadeMeters = 80f;

        /// <summary>Least angle (degrees) between two chosen taps — neighbouring 45° rays of one wall are one reflection.</summary>
        public const float MinTapAngleDegrees = 90f;

        /// <summary>Least delay difference (s) between two chosen taps — closer is a comb filter, not two reflections.</summary>
        public const float MinTapDelaySeconds = 0.006f;

        /// <summary>Taps per single shot / per string shot.</summary>
        public const int MaxTapsSingle = 3;
        public const int MaxTapsString = 2;

        /// <summary>A string's slap set at most this often (s): 900 RPM → ≤ 8.3 sets/s × 2 taps, inside the Reflect pool of 8.</summary>
        public const float SlapMinInterval = 0.12f;

        /// <summary>Reflections are wider than the dry shot (0.20 top-down): the east wall answers from the right.</summary>
        public const float SlapSpatial = 0.65f;

        /// <summary>A tap sounds from listener + Dir · 5 m: inside the 8 m rolloff minimum, so its level is <see cref="SlapDb"/> alone.</summary>
        public const float SlapPanMeters = 5f;

        /// <summary>Open-air echo (03 §2): diffuse, delayed by the far geometry, darker with openness.</summary>
        public const float EchoSpatial = 0.2f;
        public const float EchoDelayMinSeconds = 0.08f;
        public const float EchoDelayMaxSeconds = 0.35f;

        /// <summary>Echo delay when the far rays hit nothing (s).</summary>
        public const float EchoDelayDefaultSeconds = 0.18f;
        public const float EchoDbClosed = -20f;
        public const float EchoDbOpen = -14f;
        public const float EchoCutoffClosedHz = 3000f;
        public const float EchoCutoffOpenHz = 5000f;

        /// <summary>Round trip at the speed of sound: 2d / 343 (8 m → 46.6 ms).</summary>
        public static float DelaySeconds(float meters) => 2f * meters / SpeedOfSound;

        /// <summary>Tap level against the dry shot (dB), uncapped: −20·log10(2d) − wall loss + lift (4 m −10.1, 10 m −18.0, 40 m −30.1).</summary>
        public static float SlapDb(float meters) => -20f * Mathf.Log10(Mathf.Max(0.01f, 2f * meters)) - WallLossDb + SlapLiftDb;

        /// <summary>Tap low-pass (Hz): 16 kHz·10^(−2d/80) in [2.5, 16] kHz (4 m 12.7 k, 10 m 9.0 k, 20 m 5.1 k, ≥ 32 m 2.5 k).</summary>
        public static float SlapCutoffHz(float meters) => Mathf.Clamp(CutoffMaxHz * Mathf.Pow(10f, -2f * meters / CutoffDecadeMeters), CutoffMinHz, CutoffMaxHz);

        /// <summary>Echo delay (s) from the far geometry's median distance; <see cref="EchoDelayDefaultSeconds"/> when there is none (≤ 0).</summary>
        public static float EchoDelay(float farMedianMeters)
        {
            return farMedianMeters > 0f ? Mathf.Clamp(DelaySeconds(farMedianMeters), EchoDelayMinSeconds, EchoDelayMaxSeconds) : EchoDelayDefaultSeconds;
        }

        /// <summary>Echo level against the dry shot (dB): −20 enclosed … −14 fully open.</summary>
        public static float EchoDb(float openness) => Mathf.Lerp(EchoDbClosed, EchoDbOpen, Mathf.Clamp01(openness));

        /// <summary>Echo low-pass (Hz): 3 kHz … 5 kHz with openness.</summary>
        public static float EchoCutoffHz(float openness) => Mathf.Lerp(EchoCutoffClosedHz, EchoCutoffOpenHz, Mathf.Clamp01(openness));

        /// <summary>
        /// Greedy tap choice over the probe's rays: nearest (loudest) first, each new tap ≥ 90° from and ≥ 6 ms apart from every
        /// tap already chosen; taps under the floor or beyond <see cref="AcousticProbe.MaxSlapMeters"/> never qualify.
        /// <paramref name="dist"/> / <paramref name="valid"/> are indexed by ray (<see cref="AcousticProbe.RayCount"/>; a shorter
        /// array is read as far as it goes). Writes at most min(maxTaps, outTaps.Length) taps nearest first and returns the count.
        /// Allocation-free.
        /// </summary>
        public static int SelectTaps(float[] dist, bool[] valid, int maxTaps, SlapTap[] outTaps)
        {
            if (dist == null || valid == null || outTaps == null)
            {
                return 0;
            }

            int rays = Mathf.Min(Mathf.Min(dist.Length, valid.Length), AcousticProbe.RayCount);
            int limit = Mathf.Min(maxTaps, outTaps.Length);
            int count = 0;
            while (count < limit)
            {
                int best = -1;
                for (int i = 0; i < rays; i++)
                {
                    if (!valid[i])
                    {
                        continue;
                    }

                    float d = dist[i];
                    if (!(d >= AcousticProbe.MinSlapMeters && d <= AcousticProbe.MaxSlapMeters) || SlapDb(d) < SlapFloorDb)
                    {
                        continue;
                    }

                    if (best >= 0 && d >= dist[best])
                    {
                        continue;
                    }

                    if (!Compatible(i, d, outTaps, count))
                    {
                        continue;
                    }

                    best = i;
                }

                if (best < 0)
                {
                    break;
                }

                float db = dist[best];
                outTaps[count] = new SlapTap
                {
                    Ray = best,
                    Dir = AcousticProbe.Direction(best),
                    Distance = db,
                    Delay = DelaySeconds(db),
                    GainDb = Mathf.Min(SlapDb(db), SlapCeilingDb),
                    CutoffHz = SlapCutoffHz(db)
                };
                count++;
            }

            return count;
        }

        /// <summary>Ray <paramref name="ray"/> at <paramref name="meters"/> is not chosen yet, ≥ 90° and ≥ 6 ms from every chosen tap.</summary>
        private static bool Compatible(int ray, float meters, SlapTap[] chosen, int count)
        {
            Vector3 dir = AcousticProbe.Direction(ray);
            float delay = DelaySeconds(meters);
            for (int k = 0; k < count; k++)
            {
                if (chosen[k].Ray == ray)
                {
                    return false;
                }

                // 0.5° slack: two rays exactly 90° apart are separate walls (the 45° grid's float error must not reject them).
                if (Vector3.Angle(dir, chosen[k].Dir) < MinTapAngleDegrees - 0.5f)
                {
                    return false;
                }

                if (Mathf.Abs(delay - chosen[k].Delay) < MinTapDelaySeconds)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
