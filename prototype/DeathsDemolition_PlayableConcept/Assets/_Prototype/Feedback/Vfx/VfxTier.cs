using System;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>WP-073 · the three VFX quality tiers (M4-R1 §4.7; direction §5).</summary>
    public enum VfxTierLevel
    {
        Performance = 0,
        High = 1,
        Cinematic = 2
    }

    /// <summary>
    /// WP-073 · The VFX tier every WP-073 system reads (direction §5: per-item caps, lifetimes and the ms budget —
    /// High ≤ 3.0 ms, Cinematic ≤ 5.5, Performance ≤ 1.2 at 1080p). It follows S-05's WP-075 contract
    /// (<see cref="DD.Prototype.Core.QualityTiers"/>, QUALITY-TIERS-V2.md); <c>-ddVfxTier performance|high|cinematic</c>
    /// and <see cref="Override"/> pin it (captures, tests). Systems re-read on <see cref="Changed"/>.
    /// </summary>
    public static class VfxTier
    {
        private static VfxTierLevel? s_override;
        private static VfxTierLevel? s_arg;
        private static bool s_argRead;
        private static int s_lastQuality = -1;
        private static VfxTierLevel s_current = VfxTierLevel.High;

        /// <summary>Raised when the tier in effect changes (quality level, override).</summary>
        public static event Action<VfxTierLevel> Changed;

        /// <summary>Tests / captures: pin a tier (null = follow the quality level).</summary>
        public static VfxTierLevel? Override
        {
            get => s_override;
            set
            {
                s_override = value;
                Refresh();
            }
        }

        public static VfxTierLevel Current
        {
            get
            {
                int q = QualitySettings.GetQualityLevel();
                if (q != s_lastQuality)
                {
                    Refresh();
                }

                return s_current;
            }
        }

        /// <summary>Per-tier pick (Performance, High, Cinematic) — the direction's table columns.</summary>
        public static T Pick<T>(T performance, T high, T cinematic)
        {
            switch (Current)
            {
                case VfxTierLevel.Performance: return performance;
                case VfxTierLevel.Cinematic: return cinematic;
                default: return high;
            }
        }

        public static void Refresh()
        {
            s_lastQuality = QualitySettings.GetQualityLevel();
            VfxTierLevel next = s_override ?? ArgTier() ?? (VfxTierLevel)(int)DD.Prototype.Core.QualityTiers.Current;
            if (next == s_current)
            {
                return;
            }

            s_current = next;
            Changed?.Invoke(next);
        }

        private static VfxTierLevel? ArgTier()
        {
            if (!s_argRead)
            {
                s_argRead = true;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] != "-ddVfxTier") continue;
                    string v = args[i + 1].ToLowerInvariant();
                    s_arg = v.StartsWith("perf") ? VfxTierLevel.Performance : v.StartsWith("cin") ? VfxTierLevel.Cinematic : VfxTierLevel.High;
                }
            }

            return s_arg;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_override = null;
            s_lastQuality = -1;
            s_current = VfxTierLevel.High;
            Changed = null;
        }
    }
}
