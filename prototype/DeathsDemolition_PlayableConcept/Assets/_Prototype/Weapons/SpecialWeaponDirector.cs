using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using UnityEngine;
using UnityEngine.AI;

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
    ///   "usually one per round"                         -> _spawnsThisRound, capped at 1 by default
    ///   "round 8+, 10% chance of a sequential second"   -> rolled only after the first completes
    ///   "not too close to the player"                   -> Special.MinDistanceFromPlayer
    ///   "a validated, reachable point"                  -> NavMesh.SamplePosition on the candidate
    ///
    /// The state machine is the load-bearing part. "One at a time" is not a check
    /// performed at spawn time - it is the fact that there is exactly one field holding
    /// the live pickup and exactly one path back to Idle.
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

        /// <summary>PROTOTYPE-ONLY: how many Specials one round may produce in total.</summary>
        private const int MaxSpawnsPerRoundDefault = 1;

        /// <summary>
        /// PROTOTYPE-ONLY: progress mark for a sequential second drop is pushed this far
        /// past the current progress, so it cannot land in the same instant the first ended.
        /// </summary>
        private const float SecondSpawnProgressOffset = 0.12f;

        /// <summary>PROTOTYPE-ONLY: a scheduled drop never lands past this progress mark.</summary>
        private const float LatestSpawnProgress = 0.85f;

        /// <summary>PROTOTYPE-ONLY: NavMesh sample tolerance when validating a drop point.</summary>
        private const float NavSampleRadius = 2.5f;

        [Header("PROTOTYPE-ONLY fallback when no spawn points are authored yet")]
        [PrototypeOnly(PendingDecisions.None,
            "Distance in front of the player used when the map has no SpecialWeaponSpawnPoint.", true)]
        [SerializeField] private float _fallbackDistance = 24f;

        private readonly List<SpecialWeaponSpawnPoint> _candidates = new List<SpecialWeaponSpawnPoint>(16);

        private PrototypePool<SpecialWeaponPickup> _pickupPool;
        private SpecialWeaponPickup _live;

        private SpecialWeaponState _state = SpecialWeaponState.Idle;
        private float _scheduledProgress = -1f;
        private int _spawnsThisRound;
        private int _round;
        private bool _noSpawnPointsLogged;
        private SpecialWeaponId _lastRolledWeapon = SpecialWeaponId.KaguraBell;

        /// <summary>Current phase. F1 readout and test hook.</summary>
        public SpecialWeaponState State => _state;

        /// <summary>Progress mark the next drop is waiting for, or -1 when nothing is scheduled.</summary>
        public float ScheduledProgress => _scheduledProgress;

        public int SpawnsThisRound => _spawnsThisRound;

        /// <summary>Which weapon the live or most recent drop is. F1 readout and test hook.</summary>
        public SpecialWeaponId LastRolledWeapon => _lastRolledWeapon;

        // ------------------------------------------------------------------ lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();

            SpecialWeaponRoundBridge.RoundStarted += OnRoundStarted;
            SpecialWeaponRoundBridge.RoundEnded += OnRoundEnded;

            WeaponDebugHooks.EnsureExists();
        }

        protected override void OnDisable()
        {
            SpecialWeaponRoundBridge.RoundStarted -= OnRoundStarted;
            SpecialWeaponRoundBridge.RoundEnded -= OnRoundEnded;

            base.OnDisable();
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            if (_pickupPool != null)
            {
                return;
            }

            SpecialWeaponPickup template = SpecialWeaponPickup.CreateTemplate();

            // Capacity 2 rather than 1: the pool releases on the frame the pickup is
            // collected, and a debug-triggered spawn in that same frame must not have to
            // wait for the free list.
            _pickupPool = PoolService.GetOrCreate(template, 1, 2, PoolExhaustedPolicy.RecycleOldest);
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

            if (SpecialWeaponRoundBridge.RoundProgress01 < _scheduledProgress)
            {
                return;
            }

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

            _state = SpecialWeaponState.Idle;
            TryScheduleSequentialSecond();
        }

        // ------------------------------------------------------------------ round hooks

        private void OnRoundStarted(int round)
        {
            _round = round;
            _spawnsThisRound = 0;

            // A drop that is still on the ground or in use survives a round boundary
            // rather than being cancelled: the request never says a round end removes it,
            // and taking a weapon out of the player's hands at a round boundary would be
            // worse than letting it run out.
            if (_state == SpecialWeaponState.OnGround || _state == SpecialWeaponState.InUse)
            {
                return;
            }

            _state = SpecialWeaponState.Idle;
            _scheduledProgress = -1f;

            TryScheduleForRound(round, false);
        }

        private void OnRoundEnded(int round)
        {
            if (_state == SpecialWeaponState.Scheduled)
            {
                // The round finished before the drop was due. It simply does not happen.
                _state = SpecialWeaponState.Idle;
                _scheduledProgress = -1f;
            }
        }

        private void TryScheduleForRound(int round, bool isSecond)
        {
            if (Config == null || _state != SpecialWeaponState.Idle)
            {
                return;
            }

            if (_spawnsThisRound >= MaxSpawnsPerRoundDefault && !isSecond)
            {
                return;
            }

            SpecialWeaponConfigSection s = Config.Weapons.Special;

            float chance = isSecond
                ? s.SecondSpawnChance
                : Config.SpecialWeaponSpawnChance(round);

            if (chance <= 0f || Random.value > chance)
            {
                return;
            }

            float min = Mathf.Clamp01(Mathf.Min(s.SpawnWindowMin, s.SpawnWindowMax));
            float max = Mathf.Clamp01(Mathf.Max(s.SpawnWindowMin, s.SpawnWindowMax));

            float mark = Random.Range(min, max);

            if (isSecond)
            {
                mark = Mathf.Max(mark, SpecialWeaponRoundBridge.RoundProgress01 + SecondSpawnProgressOffset);
            }

            if (mark > LatestSpawnProgress)
            {
                // Too late to be worth chasing; the request explicitly does not want a
                // drop appearing as the round is about to end.
                return;
            }

            _scheduledProgress = mark;
            _state = SpecialWeaponState.Scheduled;
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

            if (_spawnsThisRound >= MaxSpawnsPerRoundDefault + 1)
            {
                return;
            }

            TryScheduleForRound(_round, true);
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

            _state = SpecialWeaponState.Scheduled;
            return SpawnNow();
        }

        private bool SpawnNow()
        {
            if (Config == null || _pickupPool == null)
            {
                return false;
            }

            if (!TryPickDropPosition(out Vector3 position))
            {
                // Nowhere valid to put it. Stay scheduled and try again next frame rather
                // than dropping the round's chance on the floor.
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
            _spawnsThisRound++;

            // WHICH of the three drops is rolled HERE, at spawn time, not at pickup time:
            // the drop's silhouette and beacon colour have to name the weapon while it is
            // still on the ground, because that is the information the player uses to
            // decide whether the run across the map is worth making (G-21).
            SpecialWeaponId weapon = s.RollWeapon(Random.value);
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

            _state = SpecialWeaponState.Idle;
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

            // Pass one: authored points that are far enough away AND on the NavMesh.
            CollectCandidates(points, player, hasPlayer, minDistance, true);

            // Pass two: same points without the NavMesh check, for a map that has not been
            // baked yet. Deliberately not an error - the world task may bake later.
            if (_candidates.Count == 0)
            {
                CollectCandidates(points, player, hasPlayer, minDistance, false);
            }

            // Pass three: ignore the distance rule rather than never spawning at all.
            if (_candidates.Count == 0)
            {
                CollectCandidates(points, player, hasPlayer, 0f, false);
            }

            if (_candidates.Count > 0)
            {
                position = PickWeighted(_candidates);
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
            bool requireNavMesh)
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

                if (requireNavMesh && !IsReachable(drop))
                {
                    continue;
                }

                _candidates.Add(point);
            }
        }

        private static bool IsReachable(Vector3 position)
        {
            return NavMesh.SamplePosition(position, out _, NavSampleRadius, NavMesh.AllAreas);
        }

        private static Vector3 PickWeighted(List<SpecialWeaponSpawnPoint> candidates)
        {
            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += EffectiveWeight(candidates[i]);
            }

            if (total <= 0f)
            {
                return candidates[Random.Range(0, candidates.Count)].DropPosition;
            }

            float roll = Random.value * total;
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
        /// PROTOTYPE-ONLY: off-route points are weighted 1.6x, because REQ-054 wants the
        /// drop to pull the player off their safe lap rather than sit on it.
        /// </summary>
        private static float EffectiveWeight(SpecialWeaponSpawnPoint point)
        {
            return point.Weight * (point.OffRoute ? 1.6f : 1f);
        }

        private bool TryFallbackPosition(Vector3 player, float distance, out Vector3 position)
        {
            float angle = Random.Range(0f, 360f);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
            Vector3 candidate = player + offset;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, distance * 0.5f, NavMesh.AllAreas))
            {
                position = hit.position + Vector3.up * 0.8f;
                return true;
            }

            position = candidate + Vector3.up * 0.8f;
            return true;
        }
    }
}
