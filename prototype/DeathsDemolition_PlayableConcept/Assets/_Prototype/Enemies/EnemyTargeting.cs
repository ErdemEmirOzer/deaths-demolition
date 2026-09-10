using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// Who the swarm is walking towards.
    ///
    /// Deliberately NOT a reference to the player class. The request's own technology line
    /// says gameplay systems must not be bolted onto a global player singleton, so the
    /// enemy family knows exactly one thing about the player: a Transform. Whoever owns the
    /// player (G-02) calls SetPlayer once on spawn and Clear on death.
    ///
    /// The tag fallback exists only so an enemy dropped into a scene by the F1 panel still
    /// has something to chase. It resolves at most once per second and never per enemy.
    /// </summary>
    public static class EnemyTargeting
    {

        private static Transform s_player;

        /// <summary>The player transform, or null when there is nobody to chase.</summary>
        public static Transform Player
        {
            get
            {
                if (s_player != null)
                {
                    return s_player;
                }

                // No tag discovery. Nothing in this project carries the "Player" tag, so
                // FindGameObjectWithTag always returned null: a fallback that looks like a
                // defence but only hides a broken handoff. SetPlayer is now mandatory and
                // HandoffIntegrityTests guards that it is actually called.
                return null;
            }
        }

        public static bool HasPlayer => Player != null;

        /// <summary>Authoritative assignment. Call from the player's own spawn code.</summary>
        public static void SetPlayer(Transform player)
        {
            s_player = player;
        }

        public static void Clear()
        {
            s_player = null;
        }

        /// <summary>Player position, or a supplied fallback when there is no player.</summary>
        public static Vector3 PlayerPositionOr(Vector3 fallback)
        {
            Transform player = Player;
            return player != null ? player.position : fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_player = null;
        }
    }
}
