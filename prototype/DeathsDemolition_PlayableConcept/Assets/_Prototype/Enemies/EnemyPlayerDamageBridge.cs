using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.PlayerSystem;

namespace DD.Prototype.Enemies
{
    // =====================================================================================
    // ISOLATION BOUNDARY - the ONLY file in Enemies/ that touches DD.Prototype.PlayerSystem.
    // -------------------------------------------------------------------------------------
    // G-02 owns the player. Its damage contract:
    //     PlayerHealth.ApplyDamage(float amount, Vector3 sourcePosition, string sourceId)
    //   resolved from a collider with GetComponentInParent<PlayerHealth>().
    //
    // REQ-044: no invulnerability window, no cooldown, no per-source throttle. Two attacks
    // that land on the same frame both apply in full. Nothing in this file may add a gate -
    // if a second Executioner smash lands during the first one's active window, the player
    // takes both, and that is the intended threat model of being surrounded.
    //
    // The GetComponentInParent walk happens ONCE per collider and is then cached by
    // instance id, because an Ultra swinging into a crowd would otherwise pay for that walk
    // on every active frame of every swing.
    //
    // CALLED BY: EnemyAttackState.TryConnect, and nowhere else. That is not a style note.
    // This file shipped with ZERO callers, and the result was that enemies could not damage
    // the player at all - 30 connecting swings over 12 measured seconds carried 300 damage
    // that nothing ever applied. If this reference count ever returns to zero the swarm is
    // harmless again, and EnemyDamagesPlayerTests is what says so out loud.
    // =====================================================================================

    internal static class EnemyPlayerDamageBridge
    {
        private static readonly Dictionary<int, PlayerHealth> s_cache = new Dictionary<int, PlayerHealth>(8);

        /// <summary>
        /// Apply an enemy attack to whatever player is behind this collider.
        /// Returns false when the collider is not the player after all.
        /// </summary>
        public static bool TryApply(Collider collider, float amount, Vector3 sourcePosition, string sourceId)
        {
            PlayerHealth health = Resolve(collider);
            if (health == null)
            {
                return false;
            }

            health.ApplyDamage(amount, sourcePosition, sourceId);
            return true;
        }

        private static PlayerHealth Resolve(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            int key = collider.GetInstanceID();
            if (s_cache.TryGetValue(key, out PlayerHealth cached))
            {
                // Unity's null overload catches a destroyed player between runs.
                if (cached != null)
                {
                    return cached;
                }

                s_cache.Remove(key);
            }

            PlayerHealth found = collider.GetComponentInParent<PlayerHealth>();
            if (found != null)
            {
                s_cache[key] = found;
            }

            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cache.Clear();
        }
    }
}
