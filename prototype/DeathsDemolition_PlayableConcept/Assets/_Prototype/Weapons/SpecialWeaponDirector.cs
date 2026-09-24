using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// Decides whether a Special Weapon appears, when in the round, and where.
    ///
    /// The rules the request states, and where each one lives:
    ///
    ///   "from after round 3, each round has a chance"   -> Config.SpecialWeaponSpawnChance(round)
    ///   "spawn at 30-70% of round progress"             -> _scheduledProgress, rolled at round start
    ///   "never right at the start or near the end"      -> the same window, which excludes both
    ///   "one at a time, on the ground OR in use"        -> the <see cref="SpecialWeaponState"/> machine
    ///   "no new spawn before the previous process ends" -> Idle is only re-entered from OnPickupFinished
    ///                                                      and from the end of the active duration
    ///   "usually one per round"                         -> _spawnsThisRound, capped at MaxSpawnsPerRound
    ///   "round 8+, 10% chance of a sequential second"   -> rolled only after the first completes
    ///   "not too close to the player"                   -> Special.MinDistanceFromPlayer
    ///   "a validated, reachable point"                  -> ISpecialDropValidator (NavMesh) on the candidate
    ///
    /// The state machine is the load-bearing part. "One at a time" is not a check
    /// performed at spawn time - it is the fact that there is exactly one field holding
    /// the live pickup and exactly one path back to Idle.
    ///
    /// WP-007 hardening (no product decision, every number PrototypeOnly and unchanged):
    ///   * the process is bounded by the RUN, not the scene: RunStarted wipes it, RunEnded
    ///     cancels it (AP-10) - a same-scene restart or the 1.6 s between death and the
    ///     results screen cannot leave a pickup, a runtime or a schedule alive;
    ///   * chance and placement go through injectable seams (<see cref="ISpecialWeaponRandom"/>,
    ///     <see cref="ISpecialDropValidator"/>) with the shipped behaviour as default;
    ///   * a drop with no valid, reachable position is NOT placed at a raw coordinate; the
    ///     schedule survives and is retried when the round's progress moves, and it is
    ///     abandoned (with a reason) when the round ends;
    ///   * every transition is observable (<see cref="WeaponEvents.SpecialWeaponScheduled"/>,
    ///     <see cref="WeaponEvents.SpecialWeaponCancelled"/>, <see cref="WeaponEvents.SpecialWeaponCleared"/>);
    ///   * a second director is refused with a diagnostic instead of silently competing.
    /// Which weapon a drop IS stays a payload of the process (rolled at spawn, KS-6); the
    /// lifecycle never branches on it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpecialWeaponDirector : PrototypeConfigBehaviour
    {
        public enum SpecialWeaponState
        {
            /// <summary>Nothing scheduled for this round, or this round is done with it.</summary>
            Idle = 0,

            /// <summary>A drop is rolled and waiting for the round to reach its progress mark.</summary>
            Scheduled = 1,

            /// <summary>Lying on the map, ground timer running.</summary>
            OnGround = 2,

            /// <summary>Picked up, use timer running. No new spawn may start.</summary>
            InUse = 3
        }

        /// <summary>PROTOTYPE-ONLY: how many first drops one round may produce; the sequential second is on top (see <see cref="MaxSpawnsPerRound"/>).</summary>
        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Usually one per round; the rare sequential second is rolled separately.", true)]
        public const int MaxSpawnsPerRoundDefault = 1;

        /// <summary>
        /// PROTOTYPE-ONLY: progress mark for a sequential second drop is pushed this far
        /// past the current progress, so it cannot land in the same instant the first ended.
        /// </summary>
        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Progress offset for the sequential second drop.", true)]
        public const float SecondSpawnProgressOffset = 0.12f;

        /// <summary>PROTOTYPE-ONLY: a scheduled drop never lands past this progress mark.</summary>
        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Latest round progress a drop may still land at ('not near the end').", true)]
        public const float LatestSpawnProgress = 0.85f;

        /// <summary>PROTOTYPE-ONLY: NavMesh sample tolerance when validating a drop point.</summary>
        [PrototypeOnly(PendingDecisions.None, "NavMesh sample tolerance for a drop point.", true)]
        public const float NavSampleRadius = 2.5f;

        /// <summary>PROTOTYPE-ONLY: off-route points are weighted this much more (REQ-054 pulls the player off the safe lap).</summary>
        [PrototypeOnly(PendingDecisions.None, "Off-route drop point weight multiplier.", true)]
        public const float OffRouteWeightMultiplier = 1.6f;

        private const string DuplicateWarning =
            "[SpecialWeapon] A second SpecialWeaponDirector was found. Only the first one runs; this one is disabled.";

        private const string NoValidPositionLog =
            "[SpecialWeapon] No valid, reachable drop position this round; the scheduled drop was abandoned (not placed at a raw coordinate).";

        [Header("PROTOTYPE-ONLY fallback when no spawn points are authored yet")]
        [PrototypeOnly(PendingDecisions.None,
            "Distance in front of the player used when the map has no SpecialWeaponSpawnPoint.", true)]
        [SerializeField] private float _fallbackDistance = 24f;

        private static SpecialWeaponDirector s_instance;
        private static int s_duplicateInstanceCount;

        // One pickup template for the whole play session: PoolService keys pools by prefab,
        // so a template per director (per Run scene load) would leak a pool under the
        // DontDestroyOnLoad root on every run (WP-007; same rule as GoldService).
        private static SpecialWeaponPickup s_pickupTemplate;

        private readonly List<SpecialWeaponSpawnPoint> _candidates = new List<SpecialWeaponSpawnPoint>(16);

        private PrototypePool<SpecialWeaponPickup> _pickupPool;
        private SpecialWeaponPickup _live;

        private ISpecialWeaponRandom _random = UnityRandomSource.Instance;
        private ISpecialDropValidator _validator = NavMeshDropValidator.Instance;

        private SpecialWeaponState _state = SpecialWeaponState.Idle;
        private float _scheduledProgress = -1f;
        private float _lastPlacementAttemptProgress = -1f;
        private bool _placementBlocked;
        private int _spawnsThisRound;
        private int _round;
        private int _placementFailureCount;
        private bool _noSpawnPointsLogged;
        private bool _noValidPositionLogged;
        private bool _duplicate;
        private SpecialWeaponId _lastRolledWeapon = SpecialWeaponId.Raiju;

        /// <summary>Current phase. F1 readout and test hook.</summary>
        public SpecialWeaponState State => _state;

        /// <summary>Progress mark the next drop is waiting for, or -1 when nothing is scheduled.</summary>
        public float ScheduledProgress => _scheduledProgress;

        public int SpawnsThisRound => _spawnsThisRound;

        /// <summary>Which weapon the live or most recent drop is. F1 readout and test hook.</summary>
        public SpecialWeaponId LastRolledWeapon => _lastRolledWeapon;

        /// <summary>
        /// WP-007 (AP-3, TM-13): how many FIRST drops a round may produce; the sequential
        /// second (SecondSpawnFromRound / SecondSpawnChance) is allowed on top, so the
        /// total per round is this plus one. Defaults to the PrototypeOnly constant; a test
        /// or an installer may configure it. Not a product decision.
        /// </summary>
        public int MaxSpawnsPerRound { get; set; } = MaxSpawnsPerRoundDefault;

        /// <summary>How many times a due drop could not be placed (no valid, reachable position). Should stay 0 on an authored map.</summary>
        public int PlacementFailureCount => _placementFailureCount;

        /// <summary>The one director that runs. Null when none is enabled.</summary>
        public static SpecialWeaponDirector Instance => s_instance;

        /// <summary>How many directors were refused as duplicates this session. Diagnostic; should stay 0.</summary>
        public static int DuplicateInstanceCount => s_duplicateInstanceCount;

        /// <summary>Replace the source of chance (tests, probe). Null restores the shipped <see cref="UnityRandomSource"/>.</summary>
        public void SetRandomSource(ISpecialWeaponRandom source)
        {
            _random = source ?? UnityRandomSource.Instance;
        }

        /// <summary>Replace the drop validator (tests). Null restores the shipped <see cref="NavMeshDropValidator"/>.</summary>
        public void SetDropValidator(ISpecialDropValidator validator)
        {
            _validator = validator ?? NavMeshDropValidator.Instance;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnEnable()
        {
            if (s_instance != null && s_instance != this)
            {
                // PB-8/PB-9: a second director would be a second schedule, a second pickup
                // pool and a second "one at a time". Refuse it loudly rather than let two
                // of them compete for the same HUD snapshot.
                _duplicate = true;
                s_duplicateInstanceCount++;
                Debug.LogWarning(DuplicateWarning, this);
                enabled = false;
                return;
            }

            s_instance = this;

            base.OnEnable();

            SpecialWeaponRoundBridge.RoundStarted += OnRoundStarted;
            SpecialWeaponRoundBridge.RoundEnded += OnRoundEnded;
            SpecialWeaponRoundBridge.RunStarted += OnRunStarted;
            SpecialWeaponRoundBridge.RunEnded += OnRunEnded;

            WeaponDebugHooks.EnsureExists();
        }

        protected override void OnDisable()
        {
            if (_duplicate)
            {
                _duplicate = false;
                return;
            }

            SpecialWeaponRoundBridge.RoundStarted -= OnRoundStarted;
            SpecialWeaponRoundBridge.RoundEnded -= OnRoundEnded;
            SpecialWeaponRoundBridge.RunStarted -= OnRunStarted;
            SpecialWeaponRoundBridge.RunEnded -= OnRunEnded;

            // AP-10: the director going away (scene unload, shutdown) takes its process with
            // it - the pickup's loop, the snapshot and the runtime must not outlive it.
            CancelAll(SpecialWeaponCancelReason.Teardown);

            if (s_instance == this)
            {
                s_instance = null;
            }

            base.OnDisable();
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            if (_pickupPool != null)
            {
                return;
            }

            if (s_pickupTemplate == null)
            {
                s_pickupTemplate = SpecialWeaponPickup.CreateTemplate();
            }

            // Capacity 2 rather than 1: the pool releases on the frame the pickup is
            // collected, and a debug-triggered spawn in that same frame must not have to
            // wait for the free list.
            _pickupPool = PoolService.GetOrCreate(s_pickupTemplate, 1, 2, PoolExhaustedPolicy.RecycleOldest);
        }

        private void Update()
        {
            if (_state != SpecialWeaponState.Scheduled)
            {
                TrackActiveUse();
                return;
            }

            if (!SpecialWeaponRoundBridge.IsBound)
            {
                return;
            }

            float progress = SpecialWeaponRoundBridge.RoundProgress01;
            if (progress < _scheduledProgress)
            {
                return;
            }

            // AP-4 / PB-4: a due drop that found no valid position is retried only when the
            // round's progress has moved since the failed attempt, not every frame.
            if (_placementBlocked && Mathf.Approximately(progress, _lastPlacementAttemptProgress))
            {
                return;
            }

            _lastPlacementAttemptProgress = progress;
            SpawnNow();
        }

        /// <summary>
        /// The one path from InUse back to Idle. Watching the controller rather than
        /// listening to an event keeps this correct even if the activation was ended by
        /// something other than its own timer - run teardown, debug panel, player death.
        /// </summary>
        private void TrackActiveUse()
        {
            if (_state != SpecialWeaponState.InUse)
            {
                return;
            }

            WeaponController controller = WeaponRegistry.ActiveController;
            if (controller != null && controller.SpecialWeaponActive)
            {
                return;
            }

            FinishProcess();
            TryScheduleSequentialSecond();
        }

        /// <summary>A process (ground or in hand) is over and everything of it is gone: Idle + Cleared.</summary>
        private void FinishProcess()
        {
            _state = SpecialWeaponState.Idle;
            _scheduledProgress = -1f;
            _placementBlocked = false;
            _live = null;
            WeaponEvents.PublishSpecialCleared();
        }

        // ------------------------------------------------------------------ run hooks (WP-007, AP-10)

        private void OnRunStarted(int runId)
        {
            CancelAll(SpecialWeaponCancelReason.RunStarted);
            _round = 0;
            _spawnsThisRound = 0;
            _placementFailureCount = 0;
            _noValidPositionLogged = false;
        }

        private void OnRunEnded()
        {
            CancelAll(SpecialWeaponCancelReason.RunEnded);
        }

        /// <summary>
        /// Stop whatever the process is - scheduled, on the ground or in hand - and return to
        /// Idle with nothing left behind: no pickup, no beacon, no loop, no runtime, no
        /// timer, no callback, snapshot None. Publishes Cancelled (and Cleared when a drop
        /// had actually existed). Idempotent.
        /// </summary>
        private void CancelAll(SpecialWeaponCancelReason reason)
        {
            switch (_state)
            {
                case SpecialWeaponState.Scheduled:
                    _state = SpecialWeaponState.Idle;
                    _scheduledProgress = -1f;
                    _placementBlocked = false;
                    WeaponEvents.PublishSpecialCancelled(reason, SpecialWeaponPhase.None);
                    return;

                case SpecialWeaponState.OnGround:
                {
                    SpecialWeaponPickup live = _live;
                    _live = null;
                    if (live != null)
                    {
                        live.Cancel();
                    }

                    WeaponEvents.PublishSpecialCancelled(reason, SpecialWeaponPhase.OnGround);
                    FinishProcess();
                    return;
                }

                case SpecialWeaponState.InUse:
                {
                    WeaponController controller = WeaponRegistry.ActiveController;
                    if (controller != null)
                    {
                        controller.CancelSpecialWeapon();
                    }

                    WeaponEvents.PublishSpecialCancelled(reason, SpecialWeaponPhase.Active);
                    FinishProcess();
                    return;
                }

                default:
                    return;
            }
        }

        // ------------------------------------------------------------------ round hooks

        private void OnRoundStarted(int round)
        {
            _round = round;
            _spawnsThisRound = 0;

            // A drop that is still on the ground or in use survives a round boundary
            // rather than being cancelled: the request never says a round end removes it,
            // and taking a weapon out of the player's hands at a round boundary would be
            // worse than letting it run out. (KS-3: characterised, not a product decision.)
            if (_state == SpecialWeaponState.OnGround || _state == SpecialWeaponState.InUse)
            {
                return;
            }

            _state = SpecialWeaponState.Idle;
            _scheduledProgress = -1f;
            _placementBlocked = false;

            TryScheduleForRound(round, false);
        }

        private void OnRoundEnded(int round)
        {
            if (_state == SpecialWeaponState.Scheduled)
            {
                // The round finished before the drop was due (or before a valid position
                // existed for it). It simply does not happen.
                SpecialWeaponCancelReason reason = _placementBlocked
                    ? SpecialWeaponCancelReason.NoValidPosition
                    : SpecialWeaponCancelReason.RoundEnded;
                CancelAll(reason);
            }
        }

        private void TryScheduleForRound(int round, bool isSecond)
        {
            if (Config == null || _state != SpecialWeaponState.Idle)
            {
                return;
            }

            if (_spawnsThisRound >= Mathf.Max(0, MaxSpawnsPerRound) && !isSecond)
            {
                return;
            }

            SpecialWeaponConfigSection s = Config.Weapons.Special;

            float chance = isSecond
                ? s.SecondSpawnChance
                : Config.SpecialWeaponSpawnChance(round);

            if (!RollEligibility(chance, _random))
            {
                return;
            }

            if (!TryComputeSpawnMark(s, isSecond, SpecialWeaponRoundBridge.RoundProgress01, _random, out float mark))
            {
                // Too late to be worth chasing; the request explicitly does not want a
                // drop appearing as the round is about to end.
                return;
            }

            _scheduledProgress = mark;
            _lastPlacementAttemptProgress = -1f;
            _placementBlocked = false;
            _state = SpecialWeaponState.Scheduled;
            WeaponEvents.PublishSpecialScheduled(mark);
        }

        private void TryScheduleSequentialSecond()
        {
            if (Config == null)
            {
                return;
            }

            SpecialWeaponConfigSection s = Config.Weapons.Special;

            if (_round < s.SecondSpawnFromRound)
            {
                return;
            }

            if (_spawnsThisRound >= Mathf.Max(0, MaxSpawnsPerRound) + 1)
            {
                return;
            }

            TryScheduleForRound(_round, true);
        }

        // ------------------------------------------------------------------ pure policy (EditMode-testable)

        /// <summary>The eligibility roll: a drop happens when the roll lands inside the chance. Chance 0 never, 1 always.</summary>
        public static bool RollEligibility(float chance, ISpecialWeaponRandom random)
        {
            if (chance <= 0f || random == null)
            {
                return false;
            }

            return random.Value() <= chance;
        }

        /// <summary>
        /// The scheduling policy: a uniform mark inside the config window, pushed past the
        /// current progress by <see cref="SecondSpawnProgressOffset"/> for a sequential
        /// second, and refused when it would land past <see cref="LatestSpawnProgress"/>.
        /// </summary>
        public static bool TryComputeSpawnMark(
            SpecialWeaponConfigSection s, bool isSecond, float currentProgress, ISpecialWeaponRandom random, out float mark)
        {
            mark = -1f;
            if (s == null || random == null)
            {
                return false;
            }

            float min = Mathf.Clamp01(Mathf.Min(s.SpawnWindowMin, s.SpawnWindowMax));
            float max = Mathf.Clamp01(Mathf.Max(s.SpawnWindowMin, s.SpawnWindowMax));

            float candidate = random.Range(min, max);

            if (isSecond)
            {
                candidate = Mathf.Max(candidate, Mathf.Clamp01(currentProgress) + SecondSpawnProgressOffset);
            }

            if (candidate > LatestSpawnProgress)
            {
                return false;
            }

            mark = candidate;
            return true;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>
        /// Force a drop right now, ignoring the round schedule but NOT the one-at-a-time
        /// rule. This is the debug panel's "Trigger Special Weapon".
        /// </summary>
        public bool ForceSpawn()
        {
            if (_state == SpecialWeaponState.OnGround || _state == SpecialWeaponState.InUse)
            {
                return false;
            }

            SpecialWeaponState previous = _state;
            _state = SpecialWeaponState.Scheduled;
            if (SpawnNow())
            {
                return true;
            }

            // Nowhere to put it: fall back to what we were rather than leaving a ghost
            // schedule with no mark behind (debug path only).
            _state = previous;
            return false;
        }

        private bool SpawnNow()
        {
            if (Config == null || _pickupPool == null)
            {
                return false;
            }

            if (!TryPickDropPosition(out Vector3 position))
            {
                // Nowhere valid to put it. Stay scheduled: Update retries when the round's
                // progress moves, OnRoundEnded abandons it with a reason. Never a raw
                // coordinate (AP-4). The counter and the lifecycle lock are untouched.
                _placementBlocked = true;
                _placementFailureCount++;
                if (!_noValidPositionLogged)
                {
                    _noValidPositionLogged = true;
                    Debug.Log(NoValidPositionLog);
                }

                return false;
            }

            SpecialWeaponConfigSection s = Config.Weapons.Special;

            SpecialWeaponPickup pickup = _pickupPool.Get(position, Quaternion.identity);
            if (pickup == null)
            {
                return false;
            }

            _live = pickup;
            _state = SpecialWeaponState.OnGround;
            _scheduledProgress = -1f;
            _placementBlocked = false;
            _spawnsThisRound++;

            // WHICH of the three drops is rolled HERE, at spawn time, not at pickup time:
            // the drop's silhouette and beacon colour have to name the weapon while it is
            // still on the ground, because that is the information the player uses to
            // decide whether the run across the map is worth making (G-21). The lifecycle
            // itself never reads the identity again (KS-6).
            SpecialWeaponId weapon = s.RollWeapon(_random.Value());
            _lastRolledWeapon = weapon;

            pickup.Finished = OnPickupFinished;
            pickup.Begin(weapon, s.DisplayName(weapon), s.GroundLifetime);
            return true;
        }

        private void OnPickupFinished(SpecialWeaponPickup pickup, bool collected)
        {
            if (_live == pickup)
            {
                _live = null;
            }

            if (collected)
            {
                // The use timer is running; the next spawn waits for it (REQ-052).
                _state = SpecialWeaponState.InUse;
                return;
            }

            FinishProcess();
            TryScheduleSequentialSecond();
        }

        // ------------------------------------------------------------------ placement

        private bool TryPickDropPosition(out Vector3 position)
        {
            position = Vector3.zero;

            if (Config == null)
            {
                return false;
            }

            float minDistance = Mathf.Max(0f, Config.Weapons.Special.MinDistanceFromPlayer);
            Vector3 player = WeaponRegistry.PlayerPosition;
            bool hasPlayer = WeaponRegistry.HasActiveController;

            IReadOnlyList<SpecialWeaponSpawnPoint> points = SpecialWeaponSpawnPoint.All;

            _candidates.Clear();

            // Pass one: authored points that are far enough away AND reachable.
            CollectCandidates(points, player, hasPlayer, minDistance, true);

            // Pass two: same points without the reachability check, for a map that has not
            // been baked yet. Deliberately not an error - the world task may bake later.
            if (_candidates.Count == 0)
            {
                CollectCandidates(points, player, hasPlayer, minDistance, false);
            }

            // Pass three: ignore the distance rule rather than never spawning at all.
            // (KS-8: the pass policy is the prototype's, characterised and unchanged.)
            if (_candidates.Count == 0)
            {
                CollectCandidates(points, player, hasPlayer, 0f, false);
            }

            if (_candidates.Count > 0)
            {
                position = PickWeighted(_candidates, _random);
                return true;
            }

            if (points.Count == 0 && !_noSpawnPointsLogged)
            {
                _noSpawnPointsLogged = true;
                Debug.Log(
                    "[SpecialWeapon] No SpecialWeaponSpawnPoint in the scene yet. " +
                    "Falling back to a point near the player. Not an error.");
            }

            if (!hasPlayer)
            {
                return false;
            }

            return TryFallbackPosition(player, Mathf.Max(minDistance, _fallbackDistance), out position);
        }

        private void CollectCandidates(
            IReadOnlyList<SpecialWeaponSpawnPoint> points,
            Vector3 player,
            bool hasPlayer,
            float minDistance,
            bool requireReachable)
        {
            for (int i = 0; i < points.Count; i++)
            {
                SpecialWeaponSpawnPoint point = points[i];
                if (point == null || point.Weight <= 0f)
                {
                    continue;
                }

                Vector3 drop = point.DropPosition;

                if (hasPlayer && minDistance > 0f)
                {
                    Vector3 flat = drop - player;
                    flat.y = 0f;
                    if (flat.sqrMagnitude < minDistance * minDistance)
                    {
                        continue;
                    }
                }

                if (requireReachable && !_validator.IsReachable(drop))
                {
                    continue;
                }

                _candidates.Add(point);
            }
        }

        /// <summary>Weighted pick over the candidate list. Pure: same random sequence, same candidates, same point (TM-18).</summary>
        public static Vector3 PickWeighted(List<SpecialWeaponSpawnPoint> candidates, ISpecialWeaponRandom random)
        {
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += EffectiveWeight(candidates[i]);
            }

            if (total <= 0f)
            {
                return candidates[random.Range(0, candidates.Count)].DropPosition;
            }

            float roll = random.Value() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= EffectiveWeight(candidates[i]);
                if (roll <= 0f)
                {
                    return candidates[i].DropPosition;
                }
            }

            return candidates[candidates.Count - 1].DropPosition;
        }

        /// <summary>
        /// PROTOTYPE-ONLY: off-route points are weighted up, because REQ-054 wants the
        /// drop to pull the player off their safe lap rather than sit on it.
        /// </summary>
        public static float EffectiveWeight(SpecialWeaponSpawnPoint point)
        {
            return point.Weight * (point.OffRoute ? OffRouteWeightMultiplier : 1f);
        }

        private bool TryFallbackPosition(Vector3 player, float distance, out Vector3 position)
        {
            float angle = _random.Range(0f, 360f);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            Vector3 candidate = player + offset;

            // AP-4: only reachable ground is a drop position. A raw candidate with no
            // reachable ground under it is a cancelled drop, not a drop in the void.
            if (_validator.TryProject(candidate, distance * 0.5f, out Vector3 projected))
            {
                position = projected + Vector3.up * 0.8f;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_duplicateInstanceCount = 0;
            s_pickupTemplate = null;
        }
    }
}
