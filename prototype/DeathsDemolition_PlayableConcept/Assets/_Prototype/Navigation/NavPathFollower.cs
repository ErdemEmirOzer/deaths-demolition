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

        // ------------------------------------------------------------------ corner hold
        // WP-076 B9 (M-23, C-00 30.09; AI-SPAWN-04 on 4934e80f/12c7215c): a corner inside the
        // reach radius is consumed once passed; otherwise it is held only while skipping it
        // is blocked (budgeted, projected segment query) and never longer than CornerHoldMaxSeconds.

        /// <summary>PROTOTYPE-ONLY, invented: longest hold of one blocked corner, s; then it is consumed anyway (<see cref="CornerHoldTimeouts"/>).</summary>
        public const float CornerHoldMaxSeconds = 0.5f;

        /// <summary>PROTOTYPE-ONLY, invented: minimum seconds between two segment queries for the same held corner, so held bodies do not drain the shared repath budget.</summary>
        public const float CornerRequerySeconds = 0.1f;

        /// <summary>PROTOTYPE-ONLY, invented: slack added to <see cref="BodyRadius"/> for the corner reach radius, m.</summary>
        public const float CornerReachMargin = 0.1f;

        /// <summary>Two corners closer than this (squared, XZ) are the same corner across repaths, m².</summary>
        private const float CornerMatchSquared = 0.01f;

        /// <summary>
        /// WP-076 B9 (M-23): owner's body radius, m (0 = unset). A corner is in reach within
        /// max(<see cref="CornerArriveRadius"/>, BodyRadius + <see cref="CornerReachMargin"/>), so a wide
        /// body that cannot get within the arrive radius still enters the bounded hold.
        /// </summary>
        public float BodyRadius;

        private INavSegmentQuery _segmentQuery;

        /// <summary>WP-076 B9 (M-23): segment/budget source of the corner hold. Null (default) = <see cref="CrowdService.Paths"/>.</summary>
        public INavSegmentQuery SegmentQuery
        {
            get => _segmentQuery ?? CrowdService.Paths;
            set => _segmentQuery = value;
        }

        private bool _holdTracked;          // _holdCorner is the corner being evaluated
        private Vector3 _holdCorner;
        private bool _holdHasResult;        // a segment query answered for _holdCorner
        private bool _holdBlocked;          // last answer for _holdCorner
        private float _holdNextQuery;
        private bool _holdStarted;
        private float _holdStartTime;
        private bool _hasReleasedCorner;    // last timed-out corner: a repath that re-adds it is not held again
        private Vector3 _releasedCorner;

        /// <summary>Corner holds started since the last reset (all followers).</summary>
        public static int CornerHoldsStarted { get; private set; }

        /// <summary>Holds released by <see cref="CornerHoldMaxSeconds"/> rather than by passing or a clear segment (all followers).</summary>
        public static int CornerHoldTimeouts { get; private set; }

        public static void ResetCornerHoldCounters()
        {
            CornerHoldsStarted = 0;
            CornerHoldTimeouts = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetCornerHoldCounters();
        }

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

        // Bridge-only state; ordinary Gate/Corridor uses the existing queue branch below.
        private NavRegionVolume _bridgeTracked, _bridgeDetour;
        private bool _bridgeDetourAttempted;
        private RegionNavRouter.BridgeAdmission _bridgeAdmission;

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

            AdvanceCorner(position, now);

            if (_hasPath && _cornerIndex < _cornerCount)
            {
                steeringTarget = _corners[_cornerIndex];
                // WP-030: chokepoint queue - hold at the mouth (slow, not stopped) while the throat is full, for a bounded time.
                RegionNavProfile profile = RegionNavProfile.Active;
                if (HoldsChokepoints)
                {
                    // WP-040: a corridor closer never yields at the mouth.
                    ClearBridgeState();
                    _queueVolume = null;
                    _queuedSeconds = 0f;
                    SpeedFactor = 1f;
                    return true;
                }

                if (_bridgeAdmission.Id != 0 && (_bridgeAdmission.Volume.Contains(position)
                    || !RegionNavRouter.HasBridgeAdmission(profile, _bridgeAdmission.Volume, now, _bridgeAdmission)))
                {
                    RegionNavRouter.CancelBridgeAdmission(_bridgeAdmission);
                    _bridgeAdmission = default;
                }
                // WP-096 R3 (son10b V6 Waterfall / C8_Bridge stations: 5 bodies held still at both mouths, queue 3–7 s): the bridge queue
                // meters a crossing; when the target stands on the deck itself (the AlternativeAhead rule's "deck is the destination") the
                // body must reach it — the ordinary WP-030 queue below (creep, MaxQueueSeconds) applies, as in R2 (FINAL a1 PASS)
                NavRegionVolume bridge = profile.FindBridgeMouth(position, steeringTarget, goal);
                if (bridge != null)
                {
                    if (!ReferenceEquals(_bridgeTracked, bridge))
                    {
                        ClearBridgeQueue();
                        _queuedSeconds = 0f; // A previous ordinary throat must not donate its wait time to a new Bridge.
                        _bridgeTracked = bridge;
                        if (!ReferenceEquals(_bridgeDetour, bridge)) { _bridgeDetour = null; _bridgeDetourAttempted = false; }
                    }
                    bool queue = RegionNavRouter.ShouldQueueBridge(profile, position, steeringTarget, _queuedSeconds, now, _bridgeAdmission, out _);
                    if (queue && _queuedSeconds >= Mathf.Max(0f, profile.BridgeQueueReleaseSeconds))
                    {
                        bool wantsDetour = profile.BridgeTimeoutDetour && bridge.hasAlternative
                            && RegionNavRouter.AlternativeAhead(bridge, position, goal, profile.AlternativeAheadMargin);
                        if (wantsDetour && !ReferenceEquals(_bridgeDetour, bridge))
                        {
                            _bridgeDetour = bridge;
                            _bridgeDetourAttempted = false;
                            _nextRepathTime = 0f; // One early repath; subsequent tries keep the existing per-body interval/budget.
                        }
                        if ((!wantsDetour || _bridgeDetourAttempted)
                            && RegionNavRouter.TryBridgeTimedOutRelease(profile, bridge, _queuedSeconds, now, out var admitted))
                        {
                            _bridgeAdmission = admitted;
                            queue = false;
                        }
                    }
                    if (!queue && !RegionNavRouter.HasBridgeAdmission(profile, bridge, now, _bridgeAdmission))
                        queue = !RegionNavRouter.NoteBridgeAdmission(profile, bridge, now, out _bridgeAdmission);
                    if (queue)
                    {
                        _queueVolume = bridge;
                        _queuedSeconds += Time.deltaTime;
                        SpeedFactor = 0f; // Stop at a Bridge mouth; ordinary Gate/Corridor retains its original creep.
                    }
                    else
                    {
                        _queueVolume = null;
                        SpeedFactor = 1f;
                    }
                    return true;
                }
                ClearBridgeQueue();

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

            ClearBridgeQueue();
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
            ClearHold();
            _hasReleasedCorner = false;
            ClearBridgeState();
        }

        private void ClearBridgeQueue()
        {
            RegionNavRouter.CancelBridgeAdmission(_bridgeAdmission);
            _bridgeAdmission = default;
            if (_bridgeTracked != null)
            {
                _queuedSeconds = 0f;
                if (_queueVolume != null && _queueVolume.kind == NavRegionKind.Bridge) _queueVolume = null;
            }
            _bridgeTracked = null;
        }

        private void ClearBridgeState()
        {
            ClearBridgeQueue();
            _bridgeDetour = null;
            _bridgeDetourAttempted = false;
        }

        /// <summary>
        /// Tests only (WP-076 B9 M-23): installs <paramref name="count"/> corners as a complete
        /// path, steering from corner 1 like a fresh repath. No NavMesh involved.
        /// </summary>
        public void SetCornersForTests(Vector3[] corners, int count)
        {
            Invalidate();
            _cornerCount = Mathf.Clamp(count, 0, Mathf.Min(_corners.Length, corners != null ? corners.Length : 0));
            for (int i = 0; i < _cornerCount; i++) _corners[i] = corners[i];
            _hasPath = _cornerCount >= 2;
            Status = _hasPath ? NavPathStatus.Complete : NavPathStatus.Invalid;
            _cornerIndex = _hasPath ? 1 : 0;
        }

        /// <summary>Tests only (WP-076 B9 M-23): the corner-advance step of <see cref="Tick"/> with a driven clock.</summary>
        public void AdvanceCornersForTests(Vector3 position, float now)
        {
            AdvanceCorner(position, now);
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
                else if (_bridgeDetour != null)
                {
                    RegionNavProfile profile = RegionNavProfile.Active;
                    if (Status == NavPathStatus.Complete && RegionNavRouter.AlternativeAhead(_bridgeDetour, position, goal, profile.AlternativeAheadMargin))
                    {
                        _cornerCount = RegionNavRouter.ChooseBridgeTimeoutRoute(profile, _bridgeDetour, position, goal, _corners, _cornerCount, out _rerouted);
                        _bridgeDetourAttempted = true;
                    }
                    else
                    {
                        _bridgeDetour = null;
                        _bridgeDetourAttempted = false;
                        _rerouted = false;
                    }
                }
                else _cornerCount = RegionNavRouter.ChooseRoute(RegionNavProfile.Active, position, goal, _corners, _cornerCount, _routeRoll, out _rerouted);
                if (_rerouted && !wasRerouted) RegionNavRouter.NoteRouteSwitch();   // WP-055B: a body that CHANGED to the alternative (RerouteCount counts every refresh decision)
                // Corner 0 is the agent's own position; start steering at the next one.
                _cornerIndex = 1;
            }
        }

        private void AdvanceCorner(Vector3 position, float now)
        {
            if (!_hasPath)
            {
                return;
            }

            float reach = Mathf.Max(CornerArriveRadius, BodyRadius > 0f ? BodyRadius + CornerReachMargin : 0f);
            float sqrReach = reach * reach;

            while (_cornerIndex < _cornerCount)
            {
                Vector3 corner = _corners[_cornerIndex];
                float dx = corner.x - position.x;
                float dz = corner.z - position.z;

                if (dx * dx + dz * dz > sqrReach)
                {
                    return;
                }

                // WP-076 B9 (M-23): the final corner and a passed corner are consumed at once;
                // any other corner only while skipping it would leave the NavMesh, bounded in time.
                if (_cornerIndex + 1 < _cornerCount && !CornerPassed(position) && HoldCorner(position, now))
                {
                    return;
                }

                if (_hasReleasedCorner && FlatSqr(corner, _releasedCorner) > CornerMatchSquared)
                {
                    _hasReleasedCorner = false;   // moved on past another corner; the bend may be held again later
                }

                ClearHold();
                _cornerIndex++;
            }
        }

        /// <summary>Flat XZ: past C towards the next corner, or beyond C along the incoming segment.</summary>
        private bool CornerPassed(Vector3 position)
        {
            Vector3 c = _corners[_cornerIndex];
            Vector3 n = _corners[_cornerIndex + 1];
            float px = position.x - c.x;
            float pz = position.z - c.z;

            if (px * (n.x - c.x) + pz * (n.z - c.z) > 0f)
            {
                return true;
            }

            if (_cornerIndex == 0)
            {
                return false;
            }

            Vector3 prev = _corners[_cornerIndex - 1];
            return px * (c.x - prev.x) + pz * (c.z - prev.z) >= 0f;
        }

        /// <summary>True while the current (non-final, not passed) corner must still be held.</summary>
        private bool HoldCorner(Vector3 position, float now)
        {
            Vector3 corner = _corners[_cornerIndex];

            if (_hasReleasedCorner && FlatSqr(corner, _releasedCorner) <= CornerMatchSquared)
            {
                return false;   // already timed out once; a repath re-added it
            }

            if (!_holdTracked || FlatSqr(corner, _holdCorner) > CornerMatchSquared)
            {
                // A new corner. Keyed by position, not index, so the 0.35 s repath does not restart the hold.
                ClearHold();
                _holdTracked = true;
                _holdCorner = corner;
            }

            if (!_holdHasResult || now >= _holdNextQuery)
            {
                INavSegmentQuery query = SegmentQuery;
                if (query.TryTakeQueryBudget())
                {
                    // Off-mesh source: no projection, no claim of "blocked".
                    _holdBlocked = query.TryProject(position, out Vector3 from) &&
                                   query.SegmentBlocked(from, _corners[_cornerIndex + 1]);
                    _holdHasResult = true;
                    _holdNextQuery = now + CornerRequerySeconds;
                }
            }

            // No budget and no earlier answer for this corner: not blocked.
            if (!_holdHasResult || !_holdBlocked)
            {
                return false;
            }

            if (!_holdStarted)
            {
                _holdStarted = true;
                _holdStartTime = now;
                CornerHoldsStarted++;
                return true;
            }

            if (now - _holdStartTime >= CornerHoldMaxSeconds)
            {
                CornerHoldTimeouts++;
                _hasReleasedCorner = true;
                _releasedCorner = corner;
                return false;
            }

            return true;
        }

        private void ClearHold()
        {
            _holdTracked = false;
            _holdHasResult = false;
            _holdBlocked = false;
            _holdNextQuery = 0f;
            _holdStarted = false;
            _holdStartTime = 0f;
        }

        private static float FlatSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
