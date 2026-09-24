using System;
using UnityEngine;

namespace DD.Prototype.Core
{
    /// <summary>WP-075 · Quality tier v2 (planning/work-packages/QUALITY-TIERS-V2.md §1): Performans ≤ 8,0 ms, Yüksek ≤ 16,6 ms (DEC-092), Sinematik.</summary>
    public enum QualityTier
    {
        Performans = 0,
        Yuksek = 1,
        Sinematik = 2,
    }

    /// <summary>
    /// WP-075 · The quality tier API every R1 package reads (contract §2). The tier is the active QualitySettings level
    /// (three levels, <see cref="LevelNames"/>, each with its own URP asset — the player picks it in the settings screen via
    /// UiSettings.Quality); a saved index from the six-level layout maps 0–1 → Performans, 2–3 → Yüksek, 4–5 → Sinematik.
    /// A feature owner reads its own per-tier value with <see cref="Pick{T}(T,T,T)"/> and re-reads on <see cref="Changed"/>.
    /// Global Unity/URP values are written only by the S-05 quality module; the tier × camera-mode combination (contract §3,
    /// WP-070 CameraModeProfile) is <see cref="LodBias"/> / <see cref="ShadowDistance"/> / <see cref="ShadowCascades"/>.
    /// </summary>
    public static class QualityTiers
    {
        public const int LevelCount = 3;

        /// <summary>QualitySettings level names by tier (player-facing; the settings screen upper-cases them).</summary>
        public static readonly string[] LevelNames = { "Performance", "High", "Cinematic" };

        /// <summary>Contract §3: LOD bias base, shadow distance cap (m), cascade cap, texture streaming budget (MB, ADR-015-1 v3).</summary>
        public static readonly float[] LodBiasBase = { 1f, 2f, 2.5f };
        public static readonly float[] ShadowDistanceCap = { 45f, 60f, 80f };
        public static readonly int[] ShadowCascadeCap = { 2, 4, 4 };
        public static readonly int[] StreamingBudgetMb = { 512, 1024, 2048 };

        /// <summary>Raised with the new tier when the active level moves to another tier.</summary>
        public static event Action<QualityTier> Changed;

        public static QualityTier Current => TierOfLevel(QualitySettings.GetQualityLevel(), QualitySettings.names.Length);

        /// <summary>Tier of a QualitySettings level in a layout of <paramref name="levelCount"/> levels (3 = direct; otherwise thirds).</summary>
        public static QualityTier TierOfLevel(int level, int levelCount)
        {
            if (levelCount <= 0) return QualityTier.Yuksek;
            level = Mathf.Clamp(level, 0, levelCount - 1);
            if (levelCount == LevelCount) return (QualityTier)level;
            return (QualityTier)Mathf.Clamp(level * LevelCount / levelCount, 0, LevelCount - 1);
        }

        public static T Pick<T>(T performans, T yuksek, T sinematik)
        {
            return Pick(Current, performans, yuksek, sinematik);
        }

        public static T Pick<T>(QualityTier tier, T performans, T yuksek, T sinematik)
        {
            return tier == QualityTier.Performans ? performans : tier == QualityTier.Sinematik ? sinematik : yuksek;
        }

        /// <summary>Contract §3: max(1, tier base × camera-mode factor).</summary>
        public static float LodBias(QualityTier tier, float modeLodBiasFactor)
        {
            return Mathf.Max(1f, LodBiasBase[(int)tier] * modeLodBiasFactor);
        }

        /// <summary>Contract §3: min(tier cap, camera-mode shadow distance).</summary>
        public static float ShadowDistance(QualityTier tier, float modeShadowDistance)
        {
            return Mathf.Min(ShadowDistanceCap[(int)tier], modeShadowDistance);
        }

        /// <summary>Contract §3: min(tier cap, camera-mode cascade count).</summary>
        public static int ShadowCascades(QualityTier tier, int modeCascades)
        {
            return Mathf.Min(ShadowCascadeCap[(int)tier], modeCascades);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            QualitySettings.activeQualityLevelChanged -= OnLevelChanged;
            QualitySettings.activeQualityLevelChanged += OnLevelChanged;
        }

        private static void OnLevelChanged(int previous, int current)
        {
            int count = QualitySettings.names.Length;
            QualityTier after = TierOfLevel(current, count);
            if (TierOfLevel(previous, count) == after) return;
            try
            {
                Changed?.Invoke(after);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
