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
    ///
    /// Since WP-003 the context also carries the enemy's lifecycle <see cref="Ticket"/>
    /// and the <see cref="Lifecycle"/> sink it reports to - see <see cref="IEnemyLifecycleSink"/>.
    /// </summary>
    public readonly struct EnemySpawnContext
    {
        public EnemySpawnContext(EnemyTier tier, int round, int pointIndex, string pointId,
            RingRegion region, Vector3 position, Quaternion rotation,
            float health, float attackDamage, float moveSpeed, bool guaranteedIntroduction,
            int ticket, IEnemyLifecycleSink lifecycle)
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
            Ticket = ticket;
            Lifecycle = lifecycle;
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

        /// <summary>
        /// The round ledger's ticket for this life. Positive for a director spawn; 0 for an
        /// enemy that did not come through the director (debug spawns have no ticket).
        /// </summary>
        public int Ticket { get; }

        /// <summary>Where the enemy reports its death and its despawn. Null for a debug spawn.</summary>
        public IEnemyLifecycleSink Lifecycle { get; }
    }

    /// <summary>
    /// CROSS-TASK CONTRACT - implemented by the pooled enemy behaviour (task G-04) on the
    /// same component that derives from PooledBehaviour.
    ///
    ///     public sealed class PrototypeZombie : PooledBehaviour, ISpawnedEnemy, ICrowdMember
    ///     {
    ///         public void OnSpawnedFromPoint(in EnemySpawnContext context) { ... }
    ///         public bool IsAlive => ...;
    ///     }
    ///
    /// THE LIFECYCLE, IN ORDER (WP-003)
    /// --------------------------------
    ///   1. registration   the director reserves a ledger ticket and takes a body from the pool;
    ///   2. activation     <see cref="OnSpawnedFromPoint"/> runs; the director then reads
    ///                     <see cref="IsAlive"/> - false means the activation failed and the
    ///                     body goes straight back, the ticket is rolled back, nothing is
    ///                     lost from the pool silently;
    ///   3. alive          the enemy fights; the ledger counts it towards the target;
    ///   4. death          the enemy calls <see cref="IEnemyLifecycleSink.NotifyDied"/> once,
    ///                     from its death path - it stops counting as alive, its body still
    ///                     counts against the ceiling;
    ///   5. despawn        the enemy calls <see cref="IEnemyLifecycleSink.NotifyDespawned"/>
    ///                     when it returns to the pool (death linger over, or cleanup);
    ///   6. run end        the director despawns every body it owns; late reports carry a
    ///                     ticket the next round's ledger does not know and are ignored.
    ///
    /// Both notifications are idempotent on the receiving side, so an enemy that reports
    /// twice cannot move a counter twice.
    /// </summary>
    public interface ISpawnedEnemy
    {
        /// <summary>
        /// Called immediately after the instance was taken from the pool and placed.
        /// Apply health, damage, speed, tier visuals and armour here, then start moving.
        /// Keep <see cref="EnemySpawnContext.Ticket"/> and <see cref="EnemySpawnContext.Lifecycle"/>
        /// for the reports.
        /// </summary>
        void OnSpawnedFromPoint(in EnemySpawnContext context);

        /// <summary>True from a successful activation until death. A corpse is not alive.</summary>
        bool IsAlive { get; }
    }

    /// <summary>
    /// The director's side of the enemy lifecycle contract. The enemy never learns the
    /// director's type; it only holds this interface and its ticket.
    /// </summary>
    public interface IEnemyLifecycleSink
    {
        /// <summary>The enemy holding <paramref name="ticket"/> died. Report once; repeats are ignored.</summary>
        void NotifyDied(int ticket);

        /// <summary>The body holding <paramref name="ticket"/> returned to the pool. Report once; repeats are ignored.</summary>
        void NotifyDespawned(int ticket);
    }
}
