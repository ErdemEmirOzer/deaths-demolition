using System;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Achievements
{
    /// <summary>Which lifetime or run counter an achievement reads. Data-driven; names appear verbatim in the catalog JSON.</summary>
    public enum AchievementCondition
    {
        /// <summary>Total kills (all tiers, unattributed included).</summary>
        TotalKills = 0,

        /// <summary>Kills of <see cref="AchievementDefinition.TargetTier"/>.</summary>
        TierKills = 1,

        /// <summary>Highest round fought in (lifetime) / round reached (single run).</summary>
        ReachRound = 2,

        /// <summary>Rounds actually completed.</summary>
        RoundsCompleted = 3,

        /// <summary>Armour pieces broken off enemies.</summary>
        ArmorPiecesBroken = 4,

        /// <summary>Special weapons picked up (WP-007 lifecycle: a pickup, not a spawn).</summary>
        SpecialWeaponPickups = 5,

        /// <summary>Rounds completed without the player taking a hit.</summary>
        FlawlessRounds = 6,

        /// <summary>Gold picked up in ONE run (single-run scope only: gold is never persisted, REQ-018).</summary>
        GoldEarned = 7,

        /// <summary>Upgrade cards bought in ONE run (single-run scope only).</summary>
        UpgradesPurchased = 8,

        /// <summary>Runs finished (lifetime only).</summary>
        RunsPlayed = 9,

        /// <summary>Profile level reached (lifetime only).</summary>
        ProfileLevel = 10
    }

    /// <summary>Whether the threshold is measured over the whole profile or inside one run.</summary>
    public enum AchievementScope
    {
        Lifetime = 0,
        SingleRun = 1
    }

    /// <summary>Presentation tier. Informational (Results / Achievements pane badge); no gameplay meaning.</summary>
    public enum AchievementRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3
    }

    /// <summary>
    /// One achievement rule. Data (the catalog JSON under Resources/Achievements), not
    /// code: adding or retuning an achievement never touches a system. Nothing here can
    /// change gameplay (DEC-007): an achievement is a record, never a modifier.
    /// </summary>
    [Serializable]
    public sealed class AchievementDefinition
    {
        /// <summary>Stable id stored in the profile's earned list. Never renamed once shipped.</summary>
        public string Id = string.Empty;

        public string DisplayName = string.Empty;

        public string Description = string.Empty;

        public AchievementCondition Condition = AchievementCondition.TotalKills;

        /// <summary>Counter value at which the achievement is earned. Below 1 reads as 1.</summary>
        public int Threshold = 1;

        /// <summary>Only for <see cref="AchievementCondition.TierKills"/>.</summary>
        public EnemyTier TargetTier = EnemyTier.Standard;

        public AchievementScope Scope = AchievementScope.Lifetime;

        public AchievementRarity Rarity = AchievementRarity.Common;

        public bool IsUsable => !string.IsNullOrEmpty(Id) && Enum.IsDefined(typeof(AchievementCondition), Condition);

        /// <summary>
        /// The WP-005 presentation shape the Achievements pane and its tests read
        /// (id / title / description). Kept so the UI contract does not move.
        /// </summary>
        public AchievementSettings ToSettings()
        {
            return new AchievementSettings
            {
                Id = Id,
                DisplayName = string.IsNullOrEmpty(DisplayName) ? Id : DisplayName,
                Description = Description ?? string.Empty,
                Trigger = LegacyTrigger(Condition),
                Threshold = Math.Max(1, Threshold),
                TargetTier = TargetTier
            };
        }

        private static AchievementTrigger LegacyTrigger(AchievementCondition condition)
        {
            switch (condition)
            {
                case AchievementCondition.ArmorPiecesBroken: return AchievementTrigger.ArmorPieceBroken;
                case AchievementCondition.ReachRound: return AchievementTrigger.ReachRound;
                case AchievementCondition.TierKills: return AchievementTrigger.KillTier;
                default: return AchievementTrigger.FirstKill;
            }
        }
    }
}
