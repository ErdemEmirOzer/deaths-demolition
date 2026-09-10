using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// Reusable NavMesh path follower for a single agent - the "path" half of the S4
    /// split, offered to the enemy task (G-04) so it never has to switch NavMeshAgent
    /// avoidance on.
    ///
    /// RECOMMENDED ENEMY SETUP
    /// -----------------------
    ///   * NavMeshAgent with obstacleAvoidanceType = NoObstacleAvoidance,
    ///     updateRotation = false, and movement applied through agent.Move(); or no
    ///     NavMeshAgent at all plus <see cref="ClampToNavMesh"/>.
    ///   * Enemy Update:
    ///         _follower.Tick(transform.position, playerPosition, out Vector3 corner);
    ///         SteeringTarget = corner;                       // ICrowdMember
    ///         Vector3 own = (corner - pos).normalized * tierSpeed;
    ///         Move((own + _crowdCorrection) * dt);           // crowd correction added
    ///
    /// Turning NavMeshAgent avoidance back on would re-create exactly the REQ-046 /
    /// REQ-047 conflict this architecture exists to resolve.
    ///
    /// ALLOCATION: one corner buffer per follower, allocated once. Repaths are spread
    /// over time (interval + per-instance phase) and additionally gated by the shared
    /// per-frame query budget in <see cref="NavPathService"/>, so 40 enemies cannot pile
    /// 40 CalculatePath calls into one frame.
    /// </summary>
    public sealed class NavPathFollower
    {
        private readonly Vector3[] _corners;

        private int _cornerCount;
        private int _cornerIndex;
        private float _nextRepathTime;
        private Vector3 _lastGoal;
        private bool _hasPath;

        /// <summary>PROTOTYPE-ONLY, invented: seconds between repaths for one agent.</summary>
        public float RepathInterval = 0.35f;

        /// <summary>PROTOTYPE-ONLY, invented: goal movement that forces an early repath, m.</summary>
        public float GoalMoveTolerance = 1.5f;

        /// <summary>PROTOTYPE-ONLY, invented: distance at which a corner counts as reached, m.</summary>
        public float CornerArriveRadius = 0.65f;

        /// <summary>Status of the last completed path calculation.</summary>
        public NavPathStatus Status { get; private set; } = NavPathStatus.Invalid;

        /// <summary>True when the follower currently holds a complete path.</summary>
        public bool HasCompletePath => _hasPath && Status == NavPathStatus.Complete;

        /// <summary>Corners still ahead of the agent.</summary>
        public int RemainingCorners => Mathf.Max(0, _cornerCount - _cornerIndex);

        public NavPathFollower(int cornerCapacity = 32)
        {
            _corners = new Vector3[Mathf.Clamp(cornerCapacity, 4, NavPathService.MaxCorners)];

            // Phase-shift the first repath so a wave of enemies spawned in the same tick
            // does not repath in lockstep forever.
            _nextRepathTime = Time.time + Random.value * RepathInterval;
        }

        /// <summary>
        /// Advances the follower. <paramref name="steeringTarget"/> is what the enemy
        /// should expose as <see cref="ICrowdMember.SteeringTarget"/>.
        /// Returns true when the target came from a real path rather than a fallback.
        /// </summary>
        public bool Tick(Vector3 position, Vector3 goal, out Vector3 steeringTarget)
        {
            float now = Time.time;
            bool goalMoved = (goal - _lastGoal).sqrMagnitude > GoalMoveTolerance * GoalMoveTolerance;

            if (!_hasPath || goalMoved || now >= _nextRepathTime)
            {
                if (CrowdService.Paths.TryTakeQueryBudget())
                {
                    Recalculate(position, goal);
                    _nextRepathTime = now + Mathf.Max(0.05f, RepathInterval);
                }
                else if (!_hasPath)
                {
                    // Budget spent this frame; try again next frame rather than stalling.
                    steeringTarget = goal;
                    return false;
                }
            }

            AdvanceCorner(position);

            if (_hasPath && _cornerIndex < _cornerCount)
            {
                steeringTarget = _corners[_cornerIndex];
                return true;
            }

            steeringTarget = goal;
            return false;
        }

        /// <summary>Drops the current path, forcing a fresh calculation on the next tick.</summary>
        public void Invalidate()
        {
            _hasPath = false;
            _cornerCount = 0;
            _cornerIndex = 0;
            Status = NavPathStatus.Invalid;
        }

        /// <summary>
        /// Keeps a position on the walkable surface after the crowd correction pushed it.
        /// Enemies that do not use a NavMeshAgent must run their final position through
        /// this, otherwise a strong push can shove them off the mesh.
        /// </summary>
        public static Vector3 ClampToNavMesh(Vector3 position, float maxDistance = 1.5f)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                return hit.position;
            }

            return position;
        }

        private void Recalculate(Vector3 position, Vector3 goal)
        {
            _lastGoal = goal;
            _cornerCount = CrowdService.Paths.CalculateCorners(position, goal, _corners, out NavPathStatus status);
            Status = status;
            _cornerIndex = 0;
            _hasPath = _cornerCount >= 2 && status != NavPathStatus.Invalid;

            if (_hasPath)
            {
                // Corner 0 is the agent's own position; start steering at the next one.
                _cornerIndex = 1;
            }
        }

        private void AdvanceCorner(Vector3 position)
        {
            if (!_hasPath)
            {
                return;
            }

            float sqrArrive = CornerArriveRadius * CornerArriveRadius;

            while (_cornerIndex < _cornerCount)
            {
                Vector3 delta = _corners[_cornerIndex] - position;
                delta.y = 0f;

                if (delta.sqrMagnitude > sqrArrive)
                {
                    return;
                }

                _cornerIndex++;
            }
        }
    }
}
