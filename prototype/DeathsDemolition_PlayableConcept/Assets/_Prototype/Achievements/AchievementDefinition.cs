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
        ProfileLevel = 10,

        /// <summary>WP-077: tree capstones owned at the end of ONE run (weapon + body lane).</summary>
        TreeCapstones = 11,

        /// <summary>WP-077: tree keystones owned at the end of ONE run (branch keystones + the weapon seal).</summary>
        TreeKeystones = 12,

        /// <summary>WP-077: Hearth stacks bought in ONE run.</summary>
        HearthStacks = 13,

        /// <summary>WP-077: lanes finished in ONE run — capstone owned and a tier at its maximum.</summary>
        TreeFullLanes = 14,

        /// <summary>WP-077: weapons that ended a run owning their capstone (lifetime only).</summary>
        CapstoneWeapons = 15,

        /// <summary>WP-077: Special Link II owned at the end of ONE run (both links of the Special).</summary>
        SpecialLinkII = 16,

        /// <summary>WP-077: the highest mastery level (M1–M5) any one weapon has (lifetime only).</summary>
        WeaponMastery = 17,

        /// <summary>WP-077 A10: a weapon / Special feat (<see cref="AchievementDefinition.Feat"/>): this run's value, or the lifetime one.</summary>
        Feat = 18
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

        /// <summary>
        /// WP-077 B4: an in-run tree effect-id prefix ("cap.", "ks.", "link.") the rule depends on. While no
        /// live system reads such an effect the achievement cannot be earned, so it is not listed or evaluated
        /// (the tree's "unwired = LOCKED" rule, RunTreeEffects). Empty = always available.
        /// </summary>
        public string RequiresEffect = string.Empty;

        /// <summary>WP-077 A10: which feat a <see cref="AchievementCondition.Feat"/> rule reads (a counter, never a gameplay value).</summary>
        public FeatKind Feat;

        public bool IsUsable => !string.IsNullOrEmpty(Id) && Enum.IsDefined(typeof(AchievementCondition), Condition);

        /// <summary>
        /// Usable, and for a tree rule: its effect is live AND the round-end offer sells tree nodes (K10 node cards) —
        /// a live link nobody can buy yet is still unearnable.
        /// </summary>
        public bool IsAvailable => IsUsable && (string.IsNullOrEmpty(RequiresEffect) ||
                                                (DD.Prototype.Rounds.RunTreeEffects.AnyLive(RequiresEffect) && DD.Prototype.Rounds.RunTreeEffects.NodeCardsShipped));

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
