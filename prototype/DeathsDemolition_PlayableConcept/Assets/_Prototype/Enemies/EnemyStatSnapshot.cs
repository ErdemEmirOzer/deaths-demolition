using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-010 (ADR-010-4): the stat profile one enemy LIFE was given when it spawned -
    /// the tier's settings and the round multipliers of the SPAWN round, resolved once,
    /// in one place, and frozen for the life (the <c>EnemyRewardSnapshot</c> pattern of
    /// WP-008). A round boundary never touches a living enemy: the record carries the
    /// round it was priced at, not the round the game is in now.
    ///
    /// Every number here is the same formula the runtime states use (health through
    /// <see cref="PrototypeConfig.EnemyHealth"/>, damage through
    /// <see cref="PrototypeConfig.EnemyAttackDamage"/>, the rest straight from
    /// <see cref="EnemyTierSettings"/>), so the record is the readable truth of what the
    /// body is running with. Tiers differ ONLY by this profile in this package: no
    /// ability and no new behaviour (undecided product questions stay undecided). The
    /// per-tier attack style of WP-002 (EnemyAttackState: lunge/sweep/overhead/swipe)
    /// pre-exists and is untouched here; it is not part of this record.
    ///
    /// The one legitimate re-resolution is a live config edit (F1 panel): the body is
    /// re-priced with its own spawn round, never with the current one.
    /// </summary>
    public readonly struct EnemyStatSnapshot
    {
        public EnemyStatSnapshot(EnemyTier tier, int spawnRound, float maxHealth, float attackDamage, float moveSpeed,
            float telegraphTime, float attackActiveTime, float attackRecoveryTime, float attackRange, float attackCooldown,
            float bodyRadius, float separationMass, float staggerSusceptibility, float healthMultiplier, float damageMultiplier)
        {
            Tier = tier;
            SpawnRound = spawnRound;
            MaxHealth = maxHealth;
            AttackDamage = attackDamage;
            MoveSpeed = moveSpeed;
            TelegraphTime = telegraphTime;
            AttackActiveTime = attackActiveTime;
            AttackRecoveryTime = attackRecoveryTime;
            AttackRange = attackRange;
            AttackCooldown = attackCooldown;
            BodyRadius = bodyRadius;
            SeparationMass = separationMass;
            StaggerSusceptibility = staggerSusceptibility;
            HealthMultiplier = healthMultiplier;
            DamageMultiplier = damageMultiplier;
            IsValid = true;
        }

        /// <summary>Canonical identity of the version. The config's DisplayName is cosmetic and never read here.</summary>
        public EnemyTier Tier { get; }

        /// <summary>The round the profile was priced at. Never changes for the life.</summary>
        public int SpawnRound { get; }

        public float MaxHealth { get; }
        public float AttackDamage { get; }
        public float MoveSpeed { get; }
        public float TelegraphTime { get; }
        public float AttackActiveTime { get; }
        public float AttackRecoveryTime { get; }
        public float AttackRange { get; }
        public float AttackCooldown { get; }
        public float BodyRadius { get; }
        public float SeparationMass { get; }
        public float StaggerSusceptibility { get; }

        /// <summary>Round health multiplier folded into <see cref="MaxHealth"/> (readout).</summary>
        public float HealthMultiplier { get; }

        /// <summary>Round damage multiplier folded into <see cref="AttackDamage"/> (readout).</summary>
        public float DamageMultiplier { get; }

        /// <summary>False for <c>default</c> (no config at spawn, or before the first spawn).</summary>
        public bool IsValid { get; }

        /// <summary>Resolve the profile of <paramref name="tier"/> priced at <paramref name="round"/>. Deterministic, allocation-free.</summary>
        public static EnemyStatSnapshot Resolve(PrototypeConfig config, EnemyTier tier, int round) => Resolve(config, tier, round, null);

        /// <summary>
        /// WP-028: the same record with the version layer folded in (<see cref="TierBehaviourProfile"/>:
        /// telegraph / recovery / speed scales). A null or identity profile gives the WP-010 record
        /// bit for bit; the DEC-021 values themselves are never changed.
        /// </summary>
        public static EnemyStatSnapshot Resolve(PrototypeConfig config, EnemyTier tier, int round, TierBehaviourProfile profile)
        {
            if (config == null)
            {
                return default;
            }

            int r = Mathf.Max(1, round);
            EnemyTierSettings t = config.Enemies.Find(tier);
            float healthMul = config.EnemyHealthMultiplier(r);
            float damageMul = config.EnemyDamageMultiplier(r);
            float tScale = profile != null ? Mathf.Max(0.1f, profile.TelegraphScale) : 1f;
            float rScale = profile != null ? Mathf.Max(0.1f, profile.RecoveryScale) : 1f;
            float sScale = profile != null ? Mathf.Max(0.1f, profile.SpeedScale) : 1f;

            return new EnemyStatSnapshot(
                tier, r,
                config.EnemyHealth(tier, r),
                config.EnemyAttackDamage(tier, r),
                t != null ? Mathf.Max(0.1f, t.MoveSpeed * sScale) : 0f,
                t != null ? t.TelegraphTime * tScale : 0f,
                t != null ? t.AttackActiveTime : 0f,
                t != null ? t.AttackRecoveryTime * rScale : 0f,
                t != null ? t.AttackRange : 0f,
                t != null ? t.AttackCooldown : 0f,
                t != null ? Mathf.Max(0.1f, t.BodyRadius) : 0f,
                t != null ? Mathf.Max(0.1f, t.SeparationMass) : 0f,
                t != null ? Mathf.Clamp01(t.StaggerSusceptibility) : 1f,
                healthMul, damageMul);
        }

        /// <summary>True when every stat of <paramref name="other"/> equals this one (tolerance 1e-4). Test readout.</summary>
        public bool SameProfileAs(in EnemyStatSnapshot other)
        {
            return IsValid == other.IsValid && Tier == other.Tier && SpawnRound == other.SpawnRound &&
                   Near(MaxHealth, other.MaxHealth) && Near(AttackDamage, other.AttackDamage) && Near(MoveSpeed, other.MoveSpeed) &&
                   Near(TelegraphTime, other.TelegraphTime) && Near(AttackActiveTime, other.AttackActiveTime) &&
                   Near(AttackRecoveryTime, other.AttackRecoveryTime) && Near(AttackRange, other.AttackRange) &&
                   Near(AttackCooldown, other.AttackCooldown) && Near(BodyRadius, other.BodyRadius) &&
                   Near(SeparationMass, other.SeparationMass) && Near(StaggerSusceptibility, other.StaggerSusceptibility);
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-4f;
    }

    /// <summary>
    /// WP-010 (area E): the attachment contract for how an enemy VERSION looks (REQ-062/063
    /// - the final look is M3). The enemy calls <see cref="ApplyVersion"/> once per life,
    /// right after the tier is known and before the plates are fitted; the visual decides
    /// what the version looks like. Today the implementation is the WP-002 placeholder
    /// silhouette (scale / lean / palette per tier) on the procedural body
    /// (<see cref="ZombieAppearance"/>); the final art binds real models per tier through
    /// the same call. Gameplay never names a mesh, a material or a scale: the contract is
    /// the tier and the per-life random stream. The <c>IArmorVisual</c> contract of WP-006
    /// sits on top of it and is unchanged.
    /// </summary>
    public interface IEnemyVersionVisual
    {
        /// <summary>Show the body as <paramref name="tier"/>; <paramref name="random"/> is the life's own stream (variation is reproducible from the spawn seed).</summary>
        void ApplyVersion(EnemyTier tier, ref EnemyRandom random);

        /// <summary>The version last applied. Standard until the first ApplyVersion after a reset.</summary>
        EnemyTier AppliedVersion { get; }
    }
}
