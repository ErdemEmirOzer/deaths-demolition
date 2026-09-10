using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// The single entry point of the navigation + crowd layer, and the CROSS-TASK
    /// CONTRACT other tasks code against.
    ///
    ///     // enemy (task G-04)
    ///     void OnEnable()  { CrowdService.Register(this); }
    ///     void OnDisable() { CrowdService.Unregister(this); }
    ///
    ///     // anything that needs a walkable distance (spawn weighting, AI, UI)
    ///     float d  = CrowdService.PathDistance(a, b);      // walked metres, not crow flies
    ///     bool  ok = CrowdService.IsReachable(from, to);   // REQ-068: complete path only
    ///
    ///     // player controller (task owning Assets/_Prototype/Player)
    ///     CrowdService.SetPlayer(transform, config.Player.CollisionRadius);
    ///
    /// It is static so no task has to find, inject or order a MonoBehaviour, and it
    /// self-hosts: the first Register call creates the DontDestroyOnLoad runtime that
    /// ticks the solver, so the crowd works even before the map scene (G-06) adds one.
    /// </summary>
    public static class CrowdService
    {
        private static CrowdSolver s_solver;
        private static NavPathService s_paths;
        private static Transform s_playerTransform;
        private static float s_playerRadius = 0.45f;
        private static CrowdPlayerState s_playerState;

        /// <summary>The crowd solver. Exposed for the F1 panel and for tests.</summary>
        public static CrowdSolver Solver
        {
            get
            {
                if (s_solver == null)
                {
                    s_solver = new CrowdSolver();
                }

                return s_solver;
            }
        }

        /// <summary>The NavMesh query layer. Paths and walkable distance only.</summary>
        public static NavPathService Paths
        {
            get
            {
                if (s_paths == null)
                {
                    s_paths = new NavPathService();
                }

                return s_paths;
            }
        }

        // ------------------------------------------------------------------ membership

        /// <summary>Adds a member to the crowd. Safe to call twice.</summary>
        public static void Register(ICrowdMember member)
        {
            if (member == null)
            {
                return;
            }

            Solver.Register(member);
            NavigationSystemBehaviour.EnsureExists();
        }

        /// <summary>Removes a member. MUST be called from OnDisable of the member.</summary>
        public static void Unregister(ICrowdMember member)
        {
            if (member == null || s_solver == null)
            {
                return;
            }

            s_solver.Unregister(member);
        }

        public static int MemberCount => s_solver != null ? s_solver.MemberCount : 0;

        /// <summary>Living crowd members within a radius. Allocation free.</summary>
        public static int CountMembersWithin(Vector3 position, float radius)
        {
            return s_solver != null ? s_solver.CountMembersWithin(position, radius) : 0;
        }

        // ------------------------------------------------------------------ nav queries

        /// <summary>
        /// WALKABLE path length between two points - the metric the spawn weighting is
        /// required to use (REQ-067), not the straight line. Falls back to the straight
        /// line while no NavMesh exists; use <see cref="TryPathDistance"/> to tell the
        /// two apart.
        ///
        /// COST: one NavMesh.CalculatePath. Fine for spawn selection (a handful per
        /// second); do NOT call it per enemy per frame - use <see cref="NavPathFollower"/>,
        /// which repaths on an interval and respects a per-frame query budget.
        /// </summary>
        public static float PathDistance(Vector3 a, Vector3 b)
        {
            return Paths.PathDistance(a, b);
        }

        /// <summary>
        /// REQ-068. True only for a COMPLETE path; a partial path counts as unreachable.
        /// While no NavMesh exists at all this answers
        /// <see cref="NavPathService.AssumeReachableWhenDegraded"/> so the prototype keeps
        /// running before the map lands.
        /// </summary>
        public static bool IsReachable(Vector3 from, Vector3 to)
        {
            return Paths.IsReachable(from, to);
        }

        /// <summary>Path length plus the reason behind it.</summary>
        public static bool TryPathDistance(Vector3 a, Vector3 b, out float distance, out NavPathStatus status)
        {
            return Paths.TryPathDistance(a, b, out distance, out status);
        }

        /// <summary>Projects a point onto the NavMesh; false when nothing is close enough.</summary>
        public static bool TryProjectToNavMesh(Vector3 point, float maxDistance, out Vector3 result)
        {
            return Paths.TryProject(point, maxDistance, out result);
        }

        /// <summary>False while the scene has no baked NavMesh. Never an error state.</summary>
        public static bool IsNavMeshAvailable => Paths.IsNavMeshAvailable;

        // ------------------------------------------------------------------ player

        /// <summary>
        /// Registers the player as the crowd's hard obstacle. Called by the player
        /// controller; if nobody calls it the runtime falls back to the "Player" tag.
        /// </summary>
        public static void SetPlayer(Transform player, float radius)
        {
            s_playerTransform = player;
            s_playerRadius = Mathf.Max(0.05f, radius);
        }

        public static Transform PlayerTransform => s_playerTransform;

        public static float PlayerRadius => s_playerRadius;

        /// <summary>Position, radius and smoothed velocity of the player. Updated per tick.</summary>
        public static CrowdPlayerState PlayerState => s_playerState;

        /// <summary>True when a player is currently registered and alive.</summary>
        public static bool HasPlayer => s_playerState.HasPlayer;

        /// <summary>Smoothed player heading, or zero while standing still.</summary>
        public static Vector3 PlayerHeading
        {
            get
            {
                Vector3 v = s_playerState.Velocity;
                v.y = 0f;
                float sqr = v.x * v.x + v.z * v.z;
                return sqr > 0.04f ? v / Mathf.Sqrt(sqr) : Vector3.zero;
            }
        }

        /// <summary>Ring region the player is standing in right now.</summary>
        public static RingRegion PlayerRegion =>
            s_playerState.HasPlayer ? RingRegionMap.Classify(s_playerState.Position) : RingRegion.Center;

        internal static void SetPlayerState(in CrowdPlayerState state)
        {
            s_playerState = state;
        }

        internal static void ClearPlayerBinding()
        {
            s_playerTransform = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_solver = null;
            s_paths = null;
            s_playerTransform = null;
            s_playerRadius = 0.45f;
            s_playerState = default;
        }
    }
}
