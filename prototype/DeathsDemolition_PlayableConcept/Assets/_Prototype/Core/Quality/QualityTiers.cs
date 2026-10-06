using System;
using UnityEngine;

namespace DD.Prototype.Core
{
    /// <summary>WP-075 · Quality tier v2 (planning/work-packages/QUALITY-TIERS-V2.md §1): Performans ≤ 8,0 ms, Yüksek ≤ 16,6 ms (DEC-092), Sinematik ≤ 33,3 ms — the default (DEC-100).</summary>
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

        /// <summary>DEC-100 (user, 2026-09-25): the game's default tier is Cinematic — 30 fps (avg ≤ 33.3 / p99 ≤ 40 ms), top quality; High (60 fps, DEC-092) and Performance stay selectable.</summary>
        public const QualityTier DefaultTier = QualityTier.Sinematik;

        /// <summary>QualitySettings level names by tier (player-facing; the settings screen upper-cases them).</summary>
        public static readonly string[] LevelNames = { "Performance", "High", "Cinematic" };

        /// <summary>Contract §3: LOD bias base, shadow distance cap (m), cascade cap, texture streaming budget (MB, ADR-015-1 v3). Cinematic r2 (DEC-100): 3.0 / 120 m / 4 / 3072. High cascade cap 3 (C-00 06.10: High FP 4 -> 3, AD MUST-3 — no visible gain from the 4th, +7 % draws / +33 % casters).</summary>
        public static readonly float[] LodBiasBase = { 1f, 2f, 3f };
        public static readonly float[] ShadowDistanceCap = { 45f, 60f, 120f };
        public static readonly int[] ShadowCascadeCap = { 2, 3, 4 };
        public static readonly int[] StreamingBudgetMb = { 512, 1024, 3072 };

        /// <summary>Raised with the new tier when the active level moves to another tier.</summary>
        public static event Action<QualityTier> Changed;

        public static QualityTier Current => TierOfLevel(QualitySettings.GetQualityLevel(), QualitySettings.names.Length);

        /// <summary>
        /// WP-078 (S-10 perf matrix): <c>-ddQualityTier performans|yuksek|sinematik</c> (also performance|high|cinematic or 0|1|2;
        /// env <c>DD_QUALITY_TIER</c>) runs this process at that tier and never writes ui-settings.json — the player's saved choice
        /// stays on disk; UiSettings applies <see cref="LevelOverride"/> over it for this process only.
        /// </summary>
        public const string CommandLineFlag = "-ddQualityTier", EnvironmentVariable = "DD_QUALITY_TIER";

        /// <summary>The QualitySettings level forced by <see cref="CommandLineFlag"/> for this process, or null.</summary>
        public static int? LevelOverride { get; private set; }

        public static bool TryParseTier(string value, out QualityTier tier)
        {
            tier = QualityTier.Yuksek;
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "0": case "performans": case "performance": tier = QualityTier.Performans; return true;
                case "1": case "yuksek": case "yüksek": case "high": tier = QualityTier.Yuksek; return true;
                case "2": case "sinematik": case "cinematic": tier = QualityTier.Sinematik; return true;
                default: return false;
            }
        }

        /// <summary>The QualitySettings level of a tier in a layout of <paramref name="levelCount"/> levels (3 = direct; otherwise the first level of its third).</summary>
        public static int LevelOfTier(QualityTier tier, int levelCount)
        {
            if (levelCount <= 0) return 0;
            if (levelCount == LevelCount) return (int)tier;
            return Mathf.Clamp(((int)tier * levelCount + LevelCount - 1) / LevelCount, 0, levelCount - 1);
        }

        /// <summary>Tier of a QualitySettings level in a layout of <paramref name="levelCount"/> levels (3 = direct; otherwise thirds).</summary>
        public static QualityTier TierOfLevel(int level, int levelCount)
        {
            if (levelCount <= 0) return DefaultTier;
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
            LevelOverride = null;
            QualitySettings.activeQualityLevelChanged -= OnLevelChanged;
            QualitySettings.activeQualityLevelChanged += OnLevelChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyCommandLine()
        {
            string value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], CommandLineFlag, StringComparison.OrdinalIgnoreCase)) value = args[i + 1];
            if (!string.IsNullOrEmpty(value)) ApplyOverride(value);
        }

        /// <summary>Forces the tier named by <paramref name="value"/> for this process (<see cref="CommandLineFlag"/>); false and a warning when it names none.</summary>
        public static bool ApplyOverride(string value)
        {
            if (!TryParseTier(value, out QualityTier tier))
            {
                Debug.LogWarning("[QualityTiers] " + CommandLineFlag + " '" + value + "' is not performans|yuksek|sinematik; the saved level stands.");
                return false;
            }

            int level = LevelOfTier(tier, QualitySettings.names.Length);
            LevelOverride = level;
            if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
            Debug.Log("[QualityTiers] command-line tier " + tier + " (level " + level + " " + QualitySettings.names[level] + "), this process only — ui-settings.json untouched");
            return true;
        }

        /// <summary>Drops the process override (tests); the level stays where it is until the settings apply again.</summary>
        public static void ClearOverride()
        {
            LevelOverride = null;
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
