using System;
using UnityEngine;

namespace DD.Prototype.Difficulty
{
    /// <summary>
    /// WP-102 · one mode's numbers (design K/c-00/R4/teslim/S-03-11.10-1.md, C-00 approved 11.10). Tier arrays are indexed by
    /// <c>(int)EnemyTier</c> (Standard, Special/Runner, Elite, Ultra). A negative value (an IntroductionRound of 0) keeps the shipped
    /// PrototypeConfig value; scales multiply it. Normal is never applied (the shipped asset runs as it is), so its row only names
    /// the mode. S-04 tunes the numbers here; code reads them at run start.
    /// </summary>
    [Serializable]
    public sealed class DifficultyModeSettings
    {
        public DifficultyMode Mode;

        [Header("Tiers (Standard, Runner, Elite, Ultra) — negative / 0 round = keep the shipped value")]
        [Tooltip("Move speed m/s per tier. Honest on every clip of the tier's pool: speed / (clip speed × body scale) stays inside the clip's cadence band at the corners (±4 % speed jitter, the tier's body scale ±6 %) — DifficultyTests checks it.")]
        public float[] MoveSpeed = { -1f, -1f, -1f, -1f };
        public int[] IntroductionRound = { 0, 0, 0, 0 };
        public float[] SpawnWeight = { -1f, -1f, -1f, -1f };
        public float[] SpawnWeightGrowthPerRound = { -1f, -1f, -1f, -1f };
        public float[] SpawnWeightMax = { -1f, -1f, -1f, -1f };

        [Header("Attack / health (× the shipped value)")]
        public float DamageScale = 1f;
        public float TelegraphScale = 1f;
        public float RecoveryScale = 1f;
        public float CooldownScale = 1f;
        [Tooltip("A scaled telegraph never drops below this (s): the wind-up stays readable.")]
        public float MinTelegraphSeconds = 0.25f;
        public float HealthScale = 1f;
        [Tooltip("Round health growth (shipped 0.12); negative = keep.")]
        public float HealthMultiplierPerRound = -1f;

        [Header("Rounds")]
        public float PoolScale = 1f;
        public float ActiveBase = -1f;
        public float ActivePerRound = -1f;
        [Tooltip("Never above the shipped AbsoluteSafetyCeiling (40).")]
        public int ActiveCap = -1;
        public float SpawnRefillInterval = -1f;
        public int MaxSpawnsPerRefillTick = -1;

        [Header("Economy (C-00 KARAR-WP104 §11.5)")]
        [Tooltip("× every kill's gold (exact over the run: the fraction carries to the next kill). The upgrade price is not scaled.")]
        public float GoldScale = 1f;
        [Tooltip("Gold in the wallet when the run opens.")]
        public int StartingGold = 0;

        [Header("Armour (every tier)")]
        public float ArmorGrowthScale = 1f;
        public float ArmorMaxAdd = 0f;

        [Header("Spawn placement")]
        public float MinSpawnDistance = -1f;
        public float PreferredDistanceMin = -1f;
        public float PreferredDistanceMax = -1f;
        public float AheadOfPlayerBonus = -1f;

        [Header("Crowd AI")]
        [Tooltip("× the chokepoint queue's MaxQueueSeconds (WP-030).")]
        public float QueueSecondsScale = 1f;
        [Tooltip("Extra concurrent swipe slots and wind-ups (AttackSlotBoard).")]
        public int AttackSlotBonus = 0;
        [Tooltip("WP-054 flank share of a Standard body; negative = keep (0.35).")]
        public float FlankShareStandard = -1f;
        [Tooltip("Share of the NON-flanking bodies that pursue predictively.")]
        [Range(0f, 1f)] public float InterceptShare = 0f;
        [Tooltip("Share of the NON-flanking bodies that cut the player's escape path.")]
        [Range(0f, 1f)] public float CutShare = 0f;
        [Tooltip("Intercept lead cap (s): goal = player + velocity × min(distance / own speed, this).")]
        public float InterceptLeadMaxSeconds = 0f;
        [Tooltip("A cutter's goal: this far ahead of the player along its escape direction (m).")]
        public float CutDistance = 5f;
        [Tooltip("A cutter takes the cut only when it gets there this much before the player could (s).")]
        public float CutArrivalMarginSeconds = 0.3f;

        [Header("Gaits (the speeds above need them — no foot slide)")]
        [Tooltip("Standard walks the run pool (run_feral A/B, run_stumble A): honest from 4.14 to 5.32 m/s.")]
        public bool RunningCrowd;
        [Tooltip("Elite runs (run_stumble A, run_feral B): its 1.16 body is honest from 4.78 to 6.17 m/s.")]
        public bool RunningElite;
        [Tooltip("Ultra power-walks (walk_heavy B + AP-022 walk_heavy): its 1.48 body is honest from 2.57 to 3.15 m/s. No run clip carries "
                 + "a 1.48 body below 6.1 m/s — an Ultra run needs a heavy_run clip (design §2.4 fallback, S-06 / P-06).")]
        public bool UltraFastHeavyWalk;
        [Tooltip("Elite walks the AP-022 walk_heavy: its 1.16 body is honest from 2.02 to 2.64 m/s.")]
        public bool EliteFastHeavyWalk;

        public bool IsNeutral => Mode == DifficultyMode.Normal;
    }

    /// <summary>
    /// WP-102 · the difficulty data (<c>Resources/DD_Difficulty</c>). Without the asset the code defaults below apply (the same
    /// numbers the shipped asset carries).
    /// </summary>
    [CreateAssetMenu(fileName = "DD_Difficulty", menuName = "Deaths Demolition/Difficulty Profile")]
    public sealed class DifficultyProfile : ScriptableObject
    {
        public const string ResourcesPath = "DD_Difficulty";

        [SerializeField] private DifficultyModeSettings[] _modes = { Defaults(DifficultyMode.Normal), Defaults(DifficultyMode.Hard), Defaults(DifficultyMode.Demolition) };

        public DifficultyModeSettings For(DifficultyMode mode)
        {
            if (_modes != null)
                for (int i = 0; i < _modes.Length; i++)
                    if (_modes[i] != null && _modes[i].Mode == mode) return _modes[i];
            return null;
        }

        private static DifficultyProfile s_active;

        /// <summary>The shipped asset, else a code-default instance.</summary>
        public static DifficultyProfile Active
        {
            get
            {
                if (s_active == null) s_active = Resources.Load<DifficultyProfile>(ResourcesPath);
                if (s_active == null) s_active = CreateInstance<DifficultyProfile>();
                return s_active;
            }
        }

        /// <summary>Tests: replace the active profile (null = load the asset again).</summary>
        public static void SetActiveForTests(DifficultyProfile profile) => s_active = profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_active = null;

        /// <summary>
        /// The design numbers (S-03-11.10-1). The speeds are fitted to the gait cadence bands for the tier's body size at the jitter
        /// corners (gt MUST-1 12.10 — see the WP-102 delivery): Hard Standard 2.78 / Runner 4.9, Demolition Runner 5.05 / Elite 4.9 /
        /// Ultra 3.1 (a fast heavy walk: no run clip carries the 1.48 Executioner).
        /// </summary>
        public static DifficultyModeSettings Defaults(DifficultyMode mode)
        {
            var s = new DifficultyModeSettings { Mode = mode };
            switch (mode)
            {
                case DifficultyMode.Hard:
                    s.MoveSpeed = new[] { 2.78f, 4.9f, 2.5f, -1f };
                    s.IntroductionRound = new[] { 0, 2, 4, 6 };
                    s.SpawnWeight = new[] { -1f, 22f, 11f, 4f };
                    s.SpawnWeightGrowthPerRound = new[] { -1f, 4f, 2f, 1f };
                    s.SpawnWeightMax = new[] { -1f, 50f, 30f, 14f };
                    s.DamageScale = 1.4f; s.TelegraphScale = 0.85f; s.RecoveryScale = 0.85f; s.CooldownScale = 0.8f;
                    s.HealthScale = 1.15f; s.HealthMultiplierPerRound = 0.14f;
                    s.PoolScale = 1.25f; s.ActiveBase = 9f; s.ActivePerRound = 2.5f; s.ActiveCap = 36;
                    s.SpawnRefillInterval = 0.35f; s.MaxSpawnsPerRefillTick = 2;
                    s.ArmorGrowthScale = 1.33f; s.ArmorMaxAdd = 0.10f;
                    s.MinSpawnDistance = 11f; s.PreferredDistanceMin = 16f; s.PreferredDistanceMax = 32f; s.AheadOfPlayerBonus = 1.6f;
                    s.QueueSecondsScale = 0.8f; s.AttackSlotBonus = 1;
                    s.FlankShareStandard = 0.45f; s.InterceptShare = 0.55f; s.CutShare = 0f; s.InterceptLeadMaxSeconds = 0.8f;
                    s.EliteFastHeavyWalk = true;
                    s.GoldScale = 1.25f;
                    break;
                case DifficultyMode.Demolition:
                    s.MoveSpeed = new[] { 4.3f, 5.05f, 4.9f, 3.1f };
                    s.IntroductionRound = new[] { 0, 1, 2, 3 };
                    s.SpawnWeight = new[] { -1f, 30f, 14f, 6f };
                    s.SpawnWeightGrowthPerRound = new[] { -1f, 5f, 3f, 1.5f };
                    s.SpawnWeightMax = new[] { -1f, 60f, 36f, 18f };
                    s.DamageScale = 1.8f; s.TelegraphScale = 0.7f; s.RecoveryScale = 0.7f; s.CooldownScale = 0.6f;
                    s.HealthScale = 1.25f; s.HealthMultiplierPerRound = 0.16f;
                    s.PoolScale = 1.4f; s.ActiveBase = 12f; s.ActivePerRound = 3f; s.ActiveCap = 40;
                    s.SpawnRefillInterval = 0.25f; s.MaxSpawnsPerRefillTick = 3;
                    s.ArmorGrowthScale = 1.83f; s.ArmorMaxAdd = 0.20f;
                    s.MinSpawnDistance = 10f; s.PreferredDistanceMin = 13f; s.PreferredDistanceMax = 26f; s.AheadOfPlayerBonus = 2.2f;
                    s.QueueSecondsScale = 0.48f; s.AttackSlotBonus = 2;
                    s.FlankShareStandard = 0.35f; s.InterceptShare = 0.6f; s.CutShare = 0.4f; s.InterceptLeadMaxSeconds = 1.4f;
                    s.CutDistance = 5f; s.CutArrivalMarginSeconds = 0.3f;
                    s.RunningCrowd = true; s.RunningElite = true; s.UltraFastHeavyWalk = true;
                    s.GoldScale = 1.8f; s.StartingGold = 150;   // target: 700–1 000 cumulative gold by the end of R3 — WP-104 B5a (S-04, 12.10): ×2,5 gave ~1 116 in the B5 model, ×1,8 ~845
                    break;
            }

            return s;
        }
    }
}
