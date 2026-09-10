using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// Per-round probability that a freshly spawned enemy of a given tier wears a
    /// given armour piece. PROTOTYPE-ONLY: the request never states these odds.
    /// </summary>
    [Serializable]
    public sealed class ArmorChanceSettings
    {
        [PrototypeOnly(PendingDecisions.CombatScaling, "Helmet spawn chance at round 1.", true)]
        [Range(0f, 1f)] public float HelmetChance = 0.15f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Body armour spawn chance at round 1.", true)]
        [Range(0f, 1f)] public float BodyChance = 0.12f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Leg armour spawn chance at round 1.", true)]
        [Range(0f, 1f)] public float LegChance = 0.08f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Added to every chance per round beyond round 1.", true)]
        [Range(0f, 0.2f)] public float ChanceGrowthPerRound = 0.025f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Ceiling for any single armour chance.", true)]
        [Range(0f, 1f)] public float MaxChance = 0.75f;

        public float Resolve(ArmorSlot slot, int round)
        {
            float baseChance;
            switch (slot)
            {
                case ArmorSlot.Helmet: baseChance = HelmetChance; break;
                case ArmorSlot.Body: baseChance = BodyChance; break;
                case ArmorSlot.Leg: baseChance = LegChance; break;
                default: return 0f;
            }

            int steps = Mathf.Max(0, round - 1);
            return Mathf.Clamp(baseChance + ChanceGrowthPerRound * steps, 0f, MaxChance);
        }
    }

    /// <summary>PROTOTYPE-ONLY per-tier enemy definition.</summary>
    [Serializable]
    public sealed class EnemyTierSettings
    {
        [PrototypeOnly(PendingDecisions.None, "Identifier of an enemy version, not a tunable value.")]
        public EnemyTier Tier = EnemyTier.Standard;

        [PrototypeOnly(PendingDecisions.None, "Placeholder display name.")]
        public string DisplayName = "Standard";

        [PrototypeOnly(PendingDecisions.CombatScaling, "Multiplier over the standard base health.")]
        public float HealthMultiplier = 1f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Movement speed in m/s.")]
        public float MoveSpeed = 2.7f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Damage of one successful attack hit.")]
        public float AttackDamage = 10f;

        [PrototypeOnly(PendingDecisions.None, "Visible wind-up before the attack hitbox goes live.")]
        public float TelegraphTime = 0.55f;

        [PrototypeOnly(PendingDecisions.None, "Seconds the attack hitbox stays active.", true)]
        public float AttackActiveTime = 0.12f;

        [PrototypeOnly(PendingDecisions.None, "Recovery after the active frame, before the enemy can act again.", true)]
        public float AttackRecoveryTime = 0.45f;

        [PrototypeOnly(PendingDecisions.None, "Distance at which the attack is committed.", true)]
        public float AttackRange = 1.7f;

        [PrototypeOnly(PendingDecisions.None, "Cooldown between two attacks from the same enemy.", true)]
        public float AttackCooldown = 1.5f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Gold dropped on death. Request: 2 / 5 / 10 / 25.")]
        public int GoldReward = 2;

        [PrototypeOnly(PendingDecisions.ProfileXp, "XP on top of the flat 1 XP per kill. Request: +0 / +1 / +3 / +8.")]
        public int BonusKillXp = 0;

        [PrototypeOnly(PendingDecisions.None, "Round in which this tier is guaranteed to appear at least once.")]
        public int IntroductionRound = 1;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Selection weight once the tier has joined the pool.", true)]
        public float SpawnWeight = 100f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Weight added per round after introduction.", true)]
        public float SpawnWeightGrowthPerRound = 0f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Ceiling for the accumulated selection weight.", true)]
        public float SpawnWeightMax = 100f;

        [PrototypeOnly(PendingDecisions.None, "Physical radius; drives soft separation in the swarm.", true)]
        public float BodyRadius = 0.45f;

        [PrototypeOnly(PendingDecisions.None, "Relative mass for push-out between enemies.", true)]
        public float SeparationMass = 1f;

        [PrototypeOnly(PendingDecisions.None, "How strongly a light hit can interrupt this tier. 0 = never staggers.", true)]
        [Range(0f, 1f)] public float StaggerSusceptibility = 1f;

        public ArmorChanceSettings ArmorChances = new ArmorChanceSettings();

        public float ResolveSpawnWeight(int round)
        {
            if (round < IntroductionRound)
            {
                return 0f;
            }

            int steps = Mathf.Max(0, round - IntroductionRound);
            return Mathf.Min(SpawnWeight + SpawnWeightGrowthPerRound * steps, SpawnWeightMax);
        }
    }

    /// <summary>PROTOTYPE-ONLY regional armour durability and damage multipliers.</summary>
    [Serializable]
    public sealed class ArmorConfigSection
    {
        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 22")]
        public float HelmetDurability = 22f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 38")]
        public float BodyDurability = 38f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: 28")]
        public float LegDurability = 28f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: exposed head 2x. NOT a product decision.")]
        public float ExposedHeadMultiplier = 2f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Exposed upper body multiplier; request implies 1x.")]
        public float ExposedUpperBodyMultiplier = 1f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: leg 0.8x. NOT a product decision.")]
        public float ExposedLowerBodyMultiplier = 0.8f;

        public float DurabilityFor(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return HelmetDurability;
                case ArmorSlot.Body: return BodyDurability;
                case ArmorSlot.Leg: return LegDurability;
                default: return 0f;
            }
        }

        public float ExposedMultiplierFor(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return ExposedHeadMultiplier;
                case BodyRegion.UpperBody: return ExposedUpperBodyMultiplier;
                case BodyRegion.LowerBody: return ExposedLowerBodyMultiplier;
                default: return 1f;
            }
        }

        /// <summary>Armour piece that protects a given region, or None.</summary>
        public static ArmorSlot SlotFor(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return ArmorSlot.Helmet;
                case BodyRegion.UpperBody: return ArmorSlot.Body;
                case BodyRegion.LowerBody: return ArmorSlot.Leg;
                default: return ArmorSlot.None;
            }
        }
    }

    /// <summary>
    /// PROTOTYPE-ONLY explosion model. Total blast damage is split across three
    /// regions; an armoured region's share is absorbed entirely by that armour and
    /// any surplus is discarded rather than carried into health.
    /// </summary>
    [Serializable]
    public sealed class ExplosionConfigSection
    {
        [PrototypeOnly(PendingDecisions.ExplosionShares, "Request value: 25%")]
        [Range(0f, 1f)] public float HeadShare = 0.25f;

        [PrototypeOnly(PendingDecisions.ExplosionShares, "Request value: 45%")]
        [Range(0f, 1f)] public float UpperBodyShare = 0.45f;

        [PrototypeOnly(PendingDecisions.ExplosionShares, "Request value: 30%")]
        [Range(0f, 1f)] public float LowerBodyShare = 0.30f;

        [PrototypeOnly(PendingDecisions.None, "Blast radius of the Explosion upgrade branch.", true)]
        public float Radius = 3.2f;

        [PrototypeOnly(PendingDecisions.None, "Falloff exponent from centre to edge. 1 = linear.", true)]
        public float FalloffExponent = 1f;

        public float ShareFor(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return HeadShare;
                case BodyRegion.UpperBody: return UpperBodyShare;
                case BodyRegion.LowerBody: return LowerBodyShare;
                default: return 0f;
            }
        }
    }

    /// <summary>Enemy family, armour and explosion data.</summary>
    [Serializable]
    public sealed class EnemyConfigSection
    {
        [PrototypeOnly(PendingDecisions.CombatScaling, "Request value: standard zombie base HP 34")]
        public float StandardBaseHealth = 34f;

        public List<EnemyTierSettings> Tiers = new List<EnemyTierSettings>
        {
            new EnemyTierSettings
            {
                Tier = EnemyTier.Standard,
                DisplayName = "Standard",
                HealthMultiplier = 1f,
                MoveSpeed = 2.7f,
                AttackDamage = 10f,
                TelegraphTime = 0.55f,
                AttackActiveTime = 0.12f,
                AttackRecoveryTime = 0.45f,
                AttackRange = 1.7f,
                AttackCooldown = 1.5f,
                GoldReward = 2,
                BonusKillXp = 0,
                IntroductionRound = 1,
                SpawnWeight = 100f,
                SpawnWeightGrowthPerRound = 0f,
                SpawnWeightMax = 100f,
                BodyRadius = 0.45f,
                SeparationMass = 1f,
                StaggerSusceptibility = 1f
            },
            new EnemyTierSettings
            {
                Tier = EnemyTier.Special,
                DisplayName = "Runner",
                HealthMultiplier = 0.85f,
                MoveSpeed = 4.6f,
                AttackDamage = 9f,
                TelegraphTime = 0.35f,
                AttackActiveTime = 0.10f,
                AttackRecoveryTime = 0.55f,
                AttackRange = 2.6f,
                AttackCooldown = 1.9f,
                GoldReward = 5,
                BonusKillXp = 1,
                IntroductionRound = 3,
                SpawnWeight = 18f,
                SpawnWeightGrowthPerRound = 3f,
                SpawnWeightMax = 45f,
                BodyRadius = 0.4f,
                SeparationMass = 0.8f,
                StaggerSusceptibility = 1f
            },
            new EnemyTierSettings
            {
                Tier = EnemyTier.Elite,
                DisplayName = "Guard",
                HealthMultiplier = 2.2f,
                MoveSpeed = 2.2f,
                AttackDamage = 18f,
                TelegraphTime = 0.8f,
                AttackActiveTime = 0.2f,
                AttackRecoveryTime = 0.7f,
                AttackRange = 2.4f,
                AttackCooldown = 2.2f,
                GoldReward = 10,
                BonusKillXp = 3,
                IntroductionRound = 5,
                SpawnWeight = 9f,
                SpawnWeightGrowthPerRound = 1.6f,
                SpawnWeightMax = 26f,
                BodyRadius = 0.62f,
                SeparationMass = 2.2f,
                StaggerSusceptibility = 0.45f,
                ArmorChances = new ArmorChanceSettings
                {
                    HelmetChance = 0.45f,
                    BodyChance = 0.5f,
                    LegChance = 0.35f,
                    ChanceGrowthPerRound = 0.03f,
                    MaxChance = 0.9f
                }
            },
            new EnemyTierSettings
            {
                Tier = EnemyTier.Ultra,
                DisplayName = "Executioner",
                HealthMultiplier = 5f,
                MoveSpeed = 1.7f,
                AttackDamage = 34f,
                TelegraphTime = 1.15f,
                AttackActiveTime = 0.25f,
                AttackRecoveryTime = 1.1f,
                AttackRange = 3.1f,
                AttackCooldown = 3f,
                GoldReward = 25,
                BonusKillXp = 8,
                IntroductionRound = 8,
                SpawnWeight = 3f,
                SpawnWeightGrowthPerRound = 0.7f,
                SpawnWeightMax = 12f,
                BodyRadius = 0.9f,
                SeparationMass = 5f,
                StaggerSusceptibility = 0.12f,
                ArmorChances = new ArmorChanceSettings
                {
                    HelmetChance = 0.6f,
                    BodyChance = 0.7f,
                    LegChance = 0.5f,
                    ChanceGrowthPerRound = 0.02f,
                    MaxChance = 0.95f
                }
            }
        };

        public ArmorConfigSection Armor = new ArmorConfigSection();
        public ExplosionConfigSection Explosion = new ExplosionConfigSection();

        public EnemyTierSettings Find(EnemyTier tier)
        {
            for (int i = 0; i < Tiers.Count; i++)
            {
                if (Tiers[i] != null && Tiers[i].Tier == tier)
                {
                    return Tiers[i];
                }
            }

            return Tiers.Count > 0 ? Tiers[0] : null;
        }
    }
}
