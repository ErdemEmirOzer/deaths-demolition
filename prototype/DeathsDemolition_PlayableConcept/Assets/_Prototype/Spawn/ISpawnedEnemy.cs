using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// Everything the spawn director hands an enemy at the moment it leaves the pool.
    ///
    /// All scaled numbers are resolved through PrototypeConfig's own formulas
    /// (EnemyHealth, EnemyAttackDamage, EnemyHealthMultiplier ...), never re-derived here,
    /// so a live edit of the round curve in the F1 panel is felt by the very next enemy.
    /// </summary>
    public readonly struct EnemySpawnContext
    {
        public EnemySpawnContext(EnemyTier tier, int round, int pointIndex, string pointId,
            RingRegion region, Vector3 position, Quaternion rotation,
            float health, float attackDamage, float moveSpeed, bool guaranteedIntroduction)
        {
            Tier = tier;
            Round = round;
            PointIndex = pointIndex;
            PointId = pointId;
            Region = region;
            Position = position;
            Rotation = rotation;
            Health = health;
            AttackDamage = attackDamage;
            MoveSpeed = moveSpeed;
            GuaranteedIntroduction = guaranteedIntroduction;
        }

        public EnemyTier Tier { get; }
        public int Round { get; }

        /// <summary>Index into <see cref="SpawnPointRegistry.Points"/>.</summary>
        public int PointIndex { get; }

        public string PointId { get; }
        public RingRegion Region { get; }

        /// <summary>Final world position, already projected onto the NavMesh when one exists.</summary>
        public Vector3 Position { get; }

        /// <summary>Facing the courtyard unless the point authored a yaw.</summary>
        public Quaternion Rotation { get; }

        /// <summary>config.EnemyHealth(tier, round) - already round-scaled.</summary>
        public float Health { get; }

        /// <summary>config.EnemyAttackDamage(tier, round) - already round-scaled.</summary>
        public float AttackDamage { get; }

        /// <summary>Tier move speed from config. Not round-scaled; the request never scales it.</summary>
        public float MoveSpeed { get; }

        /// <summary>True when this enemy is the guaranteed first appearance of its tier (REQ-071).</summary>
        public bool GuaranteedIntroduction { get; }
    }

    /// <summary>
    /// CROSS-TASK CONTRACT - implemented by the pooled enemy behaviour (task G-04) on the
    /// same component that derives from PooledBehaviour.
    ///
    ///     public sealed class PrototypeZombie : PooledBehaviour, ISpawnedEnemy, ICrowdMember
    ///     {
    ///         public void OnSpawnedFromPoint(in EnemySpawnContext context) { ... }
    ///     }
    ///
    /// The director does NOT need an event back when the enemy dies: it detects a retired
    /// enemy from PooledBehaviour.IsSpawned going false, i.e. from the enemy simply calling
    /// Despawn() on death. That keeps the two tasks free of any hard type dependency.
    /// </summary>
    public interface ISpawnedEnemy
    {
        /// <summary>
        /// Called immediately after the instance was taken from the pool and placed.
        /// Apply health, damage, speed, tier visuals and armour here, then start moving.
        /// </summary>
        void OnSpawnedFromPoint(in EnemySpawnContext context);
    }
}
