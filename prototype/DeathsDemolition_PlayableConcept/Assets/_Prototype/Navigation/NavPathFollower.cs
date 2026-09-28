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

        /// <summary>The existing movement heading dead zone, shared with the owning controller.</summary>
        public const float SteeringDeadZoneSquared = 0.0001f;

        /// <summary>Status of the last completed path calculation.</summary>
        public NavPathStatus Status { get; private set; } = NavPathStatus.Invalid;

        /// <summary>True when the follower currently holds a complete path.</summary>
        public bool HasCompletePath => _hasPath && Status == NavPathStatus.Complete;

        /// <summary>Corners still ahead of the agent.</summary>
        public int RemainingCorners => Mathf.Max(0, _cornerCount - _cornerIndex);

        // WP-030 (ADR-030-1): regional routing state of this follower.
        private float _routeRoll;            // 0..1 from the member's spawn seed: route diversification (REQ-112)
        private float _queuedSeconds;        // time spent queued at a chokepoint mouth
        private bool _rerouted;              // the current path is the alternative route
        private NavRegionVolume _queueVolume;

        /// <summary>Speed factor the owner applies this tick: 1, or the profile's QueueSpeedFactor while queued at a full chokepoint.</summary>
        public float SpeedFactor { get; private set; } = 1f;
        public bool IsQueued => _queueVolume != null;

        /// <summary>
        /// WP-040 (Elite B corridor closing): this member takes the direct route (no WP-030
        /// alternative) and never queues at a chokepoint mouth — it walks into the throat and
        /// occupies it, so the lighter bodies behind it are the ones that queue (occupancy counts it).
        /// </summary>
        public bool HoldsChokepoints { get; set; }
        public bool IsRerouted => _rerouted;
        public NavRegionVolume QueueVolume => _queueVolume;
        public float QueuedSeconds => _queuedSeconds;

        /// <summary>Route roll from the spawn seed (deterministic per life); 0.5 when never set.</summary>
        public void SetRouteSeed(uint seed)
        {
            uint x = seed ^ 0x9E3779B9u; x ^= x >> 16; x *= 0x85EBCA6Bu; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
            _routeRoll = (x >> 8) * (1f / 16777216f);
        }

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
                // WP-030: chokepoint queue - hold at the mouth (slow, not stopped) while the throat is full, for a bounded time.
                RegionNavProfile profile = RegionNavProfile.Active;
                if (HoldsChokepoints)
                {
                    // WP-040: a corridor closer never yields at the mouth.
                    _queueVolume = null;
                    _queuedSeconds = 0f;
                    SpeedFactor = 1f;
                    return true;
                }

                if (RegionNavRouter.ShouldQueue(profile, position, steeringTarget, _queuedSeconds, out NavRegionVolume volume))
                {
                    _queueVolume = volume;
                    _queuedSeconds += Time.deltaTime;
                    SpeedFactor = profile.QueueSpeedFactor;
                    return true;
                }

                if (_queueVolume != null && (volume == null || volume.Contains(position))) _queuedSeconds = 0f;
                _queueVolume = null;
                SpeedFactor = 1f;
                return true;
            }

            _queueVolume = null;
            SpeedFactor = 1f;
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
            _queuedSeconds = 0f;
            _queueVolume = null;
            _rerouted = false;
            SpeedFactor = 1f;
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
                // WP-030: regional route comparison (alternative around a costly volume when it is cheap enough for this member's roll).
                // WP-040: a corridor closer keeps the direct corners (it goes THROUGH the throat by design).
                bool wasRerouted = _rerouted;
                if (HoldsChokepoints) _rerouted = false;
                else _cornerCount = RegionNavRouter.ChooseRoute(RegionNavProfile.Active, position, goal, _corners, _cornerCount, _routeRoll, out _rerouted);
                if (_rerouted && !wasRerouted) RegionNavRouter.NoteRouteSwitch();   // WP-055B: a body that CHANGED to the alternative (RerouteCount counts every refresh decision)
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

                // A nearby turn is still required when skipping it would cut across
                // a NavMesh boundary. Keep the normal arrival radius on open segments
                // and at the final corner, but preserve the walkable route at bends.
                // Once the body reaches the movement heading dead zone, consume the
                // turn: holding it there leaves both the controller and crowd idle.
                if (_cornerIndex + 1 < _cornerCount && delta.sqrMagnitude > SteeringDeadZoneSquared &&
                    NavMesh.Raycast(position, _corners[_cornerIndex + 1], out NavMeshHit _, CrowdService.Paths.AreaMask))
                {
                    return;
                }

                _cornerIndex++;
            }
        }
    }
}
