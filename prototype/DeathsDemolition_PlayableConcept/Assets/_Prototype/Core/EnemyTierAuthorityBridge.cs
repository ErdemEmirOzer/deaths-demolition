using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core
{
    /// <summary>
    /// WP-010 (ADR-010-1): the enemy task's read-only window onto the run's single tier
    /// authority, which lives on the Spawn side (<c>EnemySpawnDirector</c> →
    /// <c>SpawnTierSelector</c> → <c>TierSelectionPolicy</c>). The F1 enemy tab (debug spawn
    /// round, "Fill To Safety Ceiling" mix, the "Tier schedule" readout) reads THROUGH this
    /// bridge, so Enemies/ keeps its isolation boundary (only <c>ZombieSpawnBinding</c>
    /// touches <c>DD.Prototype.Spawn</c>) and there is still exactly one place that decides
    /// a tier. The director binds the three delegates when it enables and clears them when
    /// it disables; unbound = "no spawn director" (round 1, Standard, readout says so).
    /// Pattern: <c>SpecialWeaponRoundBridge</c> (WP-007).
    /// </summary>
    public static class EnemyTierAuthorityBridge
    {
        /// <summary>Current round of the director, or null when no director is enabled.</summary>
        public static Func<int> RoundProvider;

        /// <summary>The authority's weighted draw for the current round (same weights, same source of chance as real spawns).</summary>
        public static Func<PrototypeConfig, EnemyTier> WeightedDrawProvider;

        /// <summary>One-line state of the authority for the F1 readout.</summary>
        public static Func<string> DescribeProvider;

        public static bool IsBound => RoundProvider != null && WeightedDrawProvider != null;

        public static int CurrentRound()
        {
            Func<int> p = RoundProvider;
            return p != null ? Mathf.Max(1, p()) : 1;
        }

        public static EnemyTier DrawWeighted(PrototypeConfig config)
        {
            Func<PrototypeConfig, EnemyTier> p = WeightedDrawProvider;
            return p != null && config != null ? p(config) : EnemyTier.Standard;
        }

        public static string Describe()
        {
            Func<string> p = DescribeProvider;
            return p != null ? p() : "no spawn director";
        }

        /// <summary>Bind the three providers of one director (the authority of the run).</summary>
        public static void Bind(Func<int> round, Func<PrototypeConfig, EnemyTier> weightedDraw, Func<string> describe)
        {
            RoundProvider = round;
            WeightedDrawProvider = weightedDraw;
            DescribeProvider = describe;
        }

        /// <summary>
        /// Clear the providers, but only those <paramref name="round"/> / <paramref name="weightedDraw"/> /
        /// <paramref name="describe"/> still are - a director going away must not cut the bond of a
        /// director that replaced it (S-01 pre-decision, Y-2).
        /// </summary>
        public static void UnbindIf(Func<int> round, Func<PrototypeConfig, EnemyTier> weightedDraw, Func<string> describe)
        {
            if (ReferenceEquals(RoundProvider, round)) RoundProvider = null;
            if (ReferenceEquals(WeightedDrawProvider, weightedDraw)) WeightedDrawProvider = null;
            if (ReferenceEquals(DescribeProvider, describe)) DescribeProvider = null;
        }

        /// <summary>Clear every provider unconditionally (domain reload / tests).</summary>
        public static void Unbind()
        {
            RoundProvider = null;
            WeightedDrawProvider = null;
            DescribeProvider = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Unbind();
        }
    }
}
