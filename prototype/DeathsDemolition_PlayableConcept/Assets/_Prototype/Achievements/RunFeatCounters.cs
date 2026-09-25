using System;

namespace DD.Prototype.Achievements
{
    /// <summary>
    /// WP-077 A10 (R1 content pack §6): the weapon / Special feats the twelve new achievements read. Never renumber —
    /// the profile stores the lifetime totals by this index.
    /// </summary>
    public enum FeatKind
    {
        /// <summary>Max enemies hit by one Raiju shot (chain).</summary>
        RaijuChainMax = 0,
        /// <summary>Kills made by Raiju (sum; lifetime).</summary>
        RaijuKills = 1,
        /// <summary>Full Raiju loads spent without the player being shocked (sum).</summary>
        RaijuGrounded = 2,
        /// <summary>Max gel puddles alive at once.</summary>
        PuddlesMax = 3,
        /// <summary>Executioners (Ultra) knocked down by gel (sum; lifetime).</summary>
        GelKnockdownUltra = 4,
        /// <summary>Enemies killed while sliding in gel (sum).</summary>
        GelSlideKills = 5,
        /// <summary>Max kills by one Tomi (sr_1) round.</summary>
        TomiKillsPerShotMax = 6,
        /// <summary>Max kills by one Bohiya (rl_1) rocket.</summary>
        BohiyaKillsPerRocketMax = 7,
        /// <summary>Guards killed by both Nito (sg_2) barrels in one breath (sum; lifetime).</summary>
        NitoDoubleGuard = 8,
        /// <summary>Max armour pieces broken with one Kurogane (lmg_2) magazine.</summary>
        KuroganePlatesPerMagMax = 9,
        /// <summary>Runners (Special tier) killed with Tetsui (smg_2) (sum).</summary>
        TetsuiRunnerKills = 10,
        /// <summary>Classic weapons that have reached round 10 (lifetime; read from the profile's first-R10 records).</summary>
        ArsenalRound10 = 11
    }

    public static class FeatKinds
    {
        public const int Count = 12;

        /// <summary>True for a feat that keeps the best single event of a run (and of a life) instead of a total.</summary>
        public static bool IsMax(FeatKind k) =>
            k == FeatKind.RaijuChainMax || k == FeatKind.PuddlesMax || k == FeatKind.TomiKillsPerShotMax ||
            k == FeatKind.BohiyaKillsPerRocketMax || k == FeatKind.KuroganePlatesPerMagMax;
    }

    /// <summary>
    /// The in-run counters that <see cref="Rounds.RunSummary"/> does not carry but the
    /// achievement rules read: collected by <see cref="AchievementRunTracker"/> from the
    /// existing run/weapon/damage events, handed to the profile transaction next to the
    /// summary, folded into the profile in the SAME write (exactly-once, WP-009H). Every
    /// value is a non-negative count; nothing here feeds XP, gold or unlock levels.
    /// </summary>
    [Serializable]
    public struct RunFeatCounters
    {
        /// <summary>Special weapons picked up this run.</summary>
        public int SpecialWeaponPickups;

        /// <summary>Rounds completed this run without an enemy attack landing on the player.</summary>
        public int FlawlessRounds;

        /// <summary>
        /// Gold picked up this run, copied from the summary by the transaction for the
        /// SINGLE-RUN gold rules only. Never folded into the profile (REQ-018), never XP.
        /// </summary>
        public int GoldEarned;

        /// <summary>Upgrade cards bought this run (single-run rules only; not persisted).</summary>
        public int UpgradesPurchased;

        /// <summary>
        /// WP-077 A10: the run's weapon / Special feats, indexed by <see cref="FeatKind"/> (null = none). "Max" feats hold
        /// the run's best single event (a chain, a shot, a rocket, a magazine), "sum" feats the run's total.
        /// </summary>
        public int[] FeatValues;

        public int Feat(FeatKind kind)
        {
            int i = (int)kind;
            return FeatValues != null && i >= 0 && i < FeatValues.Length ? Math.Max(0, FeatValues[i]) : 0;
        }

        public static RunFeatCounters None => default;

        public RunFeatCounters Clamped()
        {
            return new RunFeatCounters
            {
                SpecialWeaponPickups = Math.Max(0, SpecialWeaponPickups),
                FlawlessRounds = Math.Max(0, FlawlessRounds),
                GoldEarned = Math.Max(0, GoldEarned),
                UpgradesPurchased = Math.Max(0, UpgradesPurchased),
                FeatValues = FeatValues != null ? (int[])FeatValues.Clone() : null
            };
        }
    }
}
