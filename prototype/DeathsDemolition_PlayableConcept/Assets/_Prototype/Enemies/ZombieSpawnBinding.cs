using UnityEngine;
using DD.Prototype.Core.Config;
using DD.Prototype.Spawn;

namespace DD.Prototype.Enemies
{
    // =====================================================================================
    // ISOLATION BOUNDARY - the ONLY file in Enemies/ that touches DD.Prototype.Spawn.
    // -------------------------------------------------------------------------------------
    // G-06 owns the spawn director. It takes an instance out of its own pool, places it,
    // and hands over an EnemySpawnContext. It never learns a type from the enemy task and
    // never needs a death callback: it detects retirement from PooledBehaviour.IsSpawned
    // going false, which happens when this enemy despawns itself after its death reaction.
    //
    // WHO OWNS WHAT AT SPAWN TIME
    //   the director  : which tier, which point, which round, and the round-scaled numbers
    //   this enemy    : armour roll, silhouette, gait, and every derived combat value
    //
    // The context's Health / AttackDamage / MoveSpeed are already resolved through
    // PrototypeConfig's own formulas. This binding does NOT re-derive them, and it does not
    // second-guess them either: it feeds tier and round into the same ApplyConfig path the
    // F1 panel uses, so one code path produces the numbers whether the enemy came from the
    // director, from a debug button, or from a live config edit ten seconds later.
    // =====================================================================================

    public sealed partial class ZombieEnemy : ISpawnedEnemy
    {
        /// <summary>True when the spawn director flagged this as a tier's first appearance (REQ-071).</summary>
        public bool IsGuaranteedIntroduction { get; private set; }

        /// <summary>Spawn point index this enemy came from, or -1 for a debug spawn.</summary>
        public int SpawnPointIndex { get; private set; } = -1;

        public void OnSpawnedFromPoint(in EnemySpawnContext context)
        {
            IsGuaranteedIntroduction = context.GuaranteedIntroduction;
            SpawnPointIndex = context.PointIndex;

            // EnemyFactory.NextSeed is not reachable from here without coupling the two
            // spawn routes, so derive a deterministic seed from the context itself: the
            // same round, point and tier always dress the same zombie.
            uint seed = unchecked((uint)(
                context.Round * 73856093 ^
                (context.PointIndex + 1) * 19349663 ^
                ((int)context.Tier + 1) * 83492791 ^
                Mathf.RoundToInt(context.Position.x * 31f + context.Position.z * 17f)));

            Initialise(context.Tier, context.Round, seed, PrototypeConfig.Active);

            // Sanity cross-check, not a re-derivation: if the director's scaled health and
            // ours disagree, the two sides are reading different config state and that is
            // worth knowing loudly rather than debugging as a feel problem later.
            if (context.Health > 0f && Mathf.Abs(context.Health - MaxHealth) > 0.51f)
            {
                Debug.LogWarning(
                    $"[Zombie] Spawn context health {context.Health:0.##} disagrees with the " +
                    $"config-derived {MaxHealth:0.##} for {context.Tier} in round {context.Round}. " +
                    "One of the two sides is reading a stale PrototypeConfig.", this);
            }
        }

        private void ResetSpawnBinding()
        {
            IsGuaranteedIntroduction = false;
            SpawnPointIndex = -1;
        }
    }
}
