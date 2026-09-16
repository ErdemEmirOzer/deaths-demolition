using System;

namespace DD.Prototype.Achievements
{
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

        public static RunFeatCounters None => default;

        public RunFeatCounters Clamped()
        {
            return new RunFeatCounters
            {
                SpecialWeaponPickups = Math.Max(0, SpecialWeaponPickups),
                FlawlessRounds = Math.Max(0, FlawlessRounds),
                GoldEarned = Math.Max(0, GoldEarned),
                UpgradesPurchased = Math.Max(0, UpgradesPurchased)
            };
        }
    }
}
