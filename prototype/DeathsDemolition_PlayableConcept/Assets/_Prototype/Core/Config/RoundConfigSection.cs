using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// PROTOTYPE-ONLY round scaling formulas, expressed as coefficients so the
    /// curve can be reshaped live from the F1 panel without touching code.
    /// </summary>
    [Serializable]
    public sealed class RoundConfigSection
    {
        [Header("Total Enemy Pool = PoolBase + PoolPerRound * (R-1) + floor(PoolPowerScale * (R-1)^PoolPowerExponent)")]
        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 12")]
        public float PoolBase = 12f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 6")]
        public float PoolPerRound = 6f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 2.5")]
        public float PoolPowerScale = 2.5f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 1.15")]
        public float PoolPowerExponent = 1.15f;

        [Header("Target Active Enemies = min(ActiveBase + ActivePerRound * R, ActiveCap)")]
        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 7")]
        public float ActiveBase = 7f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 2")]
        public float ActivePerRound = 2f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 32")]
        public int ActiveCap = 32;

        [PrototypeOnly(PendingDecisions.None, "Request value: 40. Absolute technical safety ceiling, never exceeded.")]
        public int AbsoluteSafetyCeiling = 40;

        [Header("Per-round multipliers")]
        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 1 + 0.12 * (R-1)")]
        public float HealthMultiplierPerRound = 0.12f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 1 + 0.08 * (R-1)")]
        public float DamageMultiplierPerRound = 0.08f;

        [Header("Flow")]
        [PrototypeOnly(PendingDecisions.None, "Request value: ~0.45 s refill interval while below target")]
        public float SpawnRefillInterval = 0.45f;

        [PrototypeOnly(PendingDecisions.None, "Request value: 3 s countdown before the next round auto-starts")]
        public float RoundStartCountdown = 3f;

        [PrototypeOnly(PendingDecisions.None, "How many enemies the refill tick may release at once.", true)]
        public int MaxSpawnsPerRefillTick = 2;

        /// <summary>Total pool for a round. Request formula, coefficients live.</summary>
        public int TotalEnemyPool(int round)
        {
            int r = Mathf.Max(1, round);
            float steps = r - 1;
            float power = steps <= 0f ? 0f : Mathf.Pow(steps, PoolPowerExponent);
            return Mathf.Max(1, Mathf.RoundToInt(PoolBase + PoolPerRound * steps + Mathf.Floor(PoolPowerScale * power)));
        }

        /// <summary>Desired simultaneous living enemies, already clamped by the safety ceiling.</summary>
        public int TargetActiveEnemies(int round)
        {
            int r = Mathf.Max(1, round);
            int target = Mathf.FloorToInt(ActiveBase + ActivePerRound * r);
            target = Mathf.Min(target, ActiveCap);
            return Mathf.Clamp(target, 1, AbsoluteSafetyCeiling);
        }

        public float EnemyHealthMultiplier(int round)
        {
            return 1f + HealthMultiplierPerRound * Mathf.Max(0, round - 1);
        }

        public float EnemyDamageMultiplier(int round)
        {
            return 1f + DamageMultiplierPerRound * Mathf.Max(0, round - 1);
        }
    }

    /// <summary>
    /// DEC-021 OVERRIDE TABLE.
    ///
    /// The approved decision DEC-021 requires round difficulty to scale through a
    /// HYBRID system: a formula baseline plus hand-authored armour ratios and
    /// Special/Elite/Ultra distributions on specific rounds. Since WP-010 the mechanism
    /// is implemented (PrototypeConfig.ArmorChance / TierSpawnWeight / ForcedTierCount,
    /// RoundDifficultyOverrideRules, TierSelectionPolicy): an authored row beats the
    /// formula for the fields it overrides, a field it does not override falls back to
    /// the formula.
    ///
    /// WP-050 (user approval 2026-09-17, WP-041 proposal Ö-5; DEC-085 recorded by S-04): the
    /// SHIPPED asset carries three authored rows — R8 / R11 / R14 armour jumps for the
    /// Standard + Runner crowd (<see cref="ArmorTiers"/> scope) plus an Executioner share
    /// (tier weight, forced minimum). Code defaults (a fresh instance) stay EMPTY so the
    /// formula tests keep their baseline. Row values are PROTOTYPE-ONLY tuning.
    /// </summary>
    [Serializable]
    public sealed class RoundDifficultyOverride
    {
        [Tooltip("Round number this override applies to.")]
        public int Round = 0;

        [Tooltip("When true, ArmorChances replaces the tier's own armour odds for this round.")]
        public bool OverrideArmorChances = false;

        public ArmorChanceSettings ArmorChances = new ArmorChanceSettings();

        [Tooltip("WP-050 (ADR-010-3 addendum): tiers the armour override applies to. EMPTY = every tier (the WP-010 behaviour); " +
                 "listed = only those tiers read the row, the others keep their own DEC-021 odds (so a Standard/Runner armour jump never lowers Guard/Executioner).")]
        public List<EnemyTier> ArmorTiers = new List<EnemyTier>();

        /// <summary>True when the row's armour override applies to <paramref name="tier"/> (empty scope = all tiers).</summary>
        public bool ArmorAppliesTo(EnemyTier tier)
        {
            if (!OverrideArmorChances) return false;
            if (ArmorTiers == null || ArmorTiers.Count == 0) return true;
            for (int i = 0; i < ArmorTiers.Count; i++) if (ArmorTiers[i] == tier) return true;
            return false;
        }

        [Tooltip("When true, TierWeights replaces the computed tier selection weights for this round.")]
        public bool OverrideTierWeights = false;

        public List<TierWeightOverride> TierWeights = new List<TierWeightOverride>();

        [Tooltip("When true, ForcedTierCounts guarantees a minimum number of the listed tiers this round.")]
        public bool OverrideForcedCounts = false;

        public List<TierCountOverride> ForcedTierCounts = new List<TierCountOverride>();
    }

    [Serializable]
    public sealed class TierWeightOverride
    {
        public EnemyTier Tier = EnemyTier.Standard;
        public float Weight = 0f;
    }

    [Serializable]
    public sealed class TierCountOverride
    {
        public EnemyTier Tier = EnemyTier.Standard;
        public int Count = 0;
    }

    /// <summary>PROTOTYPE-ONLY dynamic spawn point selection and safety values.</summary>
    [Serializable]
    public sealed class SpawnConfigSection
    {
        [Header("Safety geometry")]
        [PrototypeOnly(PendingDecisions.None, "Request value: 12 m minimum spawn distance")]
        public float MinSpawnDistance = 12f;

        [PrototypeOnly(PendingDecisions.None, "Request value: preferred band 18-35 m")]
        public float PreferredDistanceMin = 18f;

        [PrototypeOnly(PendingDecisions.None, "Request value: preferred band 18-35 m")]
        public float PreferredDistanceMax = 35f;

        [PrototypeOnly(PendingDecisions.None, "Beyond this path distance a point gets zero weight ('too far to be meaningful').", true)]
        public float MaxUsefulDistance = 62f;

        [PrototypeOnly(PendingDecisions.None, "Request value: ~4 s cooldown on the same point")]
        public float PointCooldown = 4f;

        [Header("Weighting - all PROTOTYPE-ONLY tuning knobs")]
        [PrototypeOnly(PendingDecisions.None, "Multiplier on the designer-authored base weight.", true)]
        public float DesignerWeightScale = 1f;

        [PrototypeOnly(PendingDecisions.None, "Bonus for points inside the preferred distance band.", true)]
        public float PreferredBandBonus = 1.6f;

        [PrototypeOnly(PendingDecisions.None, "Bonus for points in the ring region the player is heading towards.", true)]
        public float AheadOfPlayerBonus = 1.35f;

        [PrototypeOnly(PendingDecisions.None, "Bonus for points in the player's current ring region.", true)]
        public float SameRegionBonus = 1.15f;

        [PrototypeOnly(PendingDecisions.None, "Penalty factor applied while a point is still cooling down.", true)]
        public float RecentUsePenalty = 0.25f;

        [PrototypeOnly(PendingDecisions.None, "Penalty per enemy already alive that came from this point.", true)]
        public float PerPointOccupancyPenalty = 0.12f;

        [PrototypeOnly(PendingDecisions.None, "Penalty per enemy alive inside LocalDensityRadius of the point.", true)]
        public float LocalDensityPenalty = 0.09f;

        [PrototypeOnly(PendingDecisions.None, "Radius used to measure local enemy density around a point.", true)]
        public float LocalDensityRadius = 9f;

        [PrototypeOnly(PendingDecisions.None, "Smallest weight a point may keep before being treated as unusable.", true)]
        public float MinimumUsableWeight = 0.01f;

        [Header("Validity")]
        [PrototypeOnly(PendingDecisions.None, "A point is rejected if it has no reachable NavMesh path to the player.")]
        public bool RequireReachablePath = true;

        [PrototypeOnly(PendingDecisions.None, "Reject points inside the camera frustum this close to the player.", true)]
        public float OnScreenRejectDistance = 20f;
    }
}
