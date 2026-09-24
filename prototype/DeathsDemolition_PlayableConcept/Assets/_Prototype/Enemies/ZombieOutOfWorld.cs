using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-061K · defensive layer for a body that left the playable world (S-10 soak 61-3: a zombie chasing the
    /// player off the south edge fell forever and the round never completed — the last living enemy could not
    /// die). The world's boundary walls are S-05's (WP-038M); this is the enemy-side net:
    ///
    ///   fell       more than <see cref="FallDepth"/> below the last point the body stood on the NavMesh,
    ///              or below the absolute floor <see cref="AbsoluteFloorY"/> → removed at once
    ///   stranded   off the NavMesh (no sample within <see cref="SampleRadius"/>) for <see cref="StrandedSeconds"/> → removed
    ///
    /// Removal is a DESPAWN, never a death: back to the pool, the population ledger retires the ticket (a living
    /// body leaving lowers the alive count, so PoolDepleted → Completing proceeds as for a kill), no reward
    /// (no EnemyDied → no gold, XP, kill statistics or achievement progress), one telemetry line
    /// (<see cref="OutOfWorldRemoved"/>). Checked every <see cref="CheckInterval"/> per body (phase-spread),
    /// NavMesh.SamplePosition only — 0 allocation.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        [PrototypeOnly(PendingDecisions.None, "Out-of-world net: metres below the last on-mesh point that count as a fall; absolute floor; off-mesh seconds; check cadence; NavMesh sample radius.", true)]
        public const float FallDepth = 6f, AbsoluteFloorY = -50f, StrandedSeconds = 3f, CheckInterval = 0.25f, SampleRadius = 2f;

        public enum OutOfWorldReason { Fell = 0, Stranded = 1, Stuck = 2 }

        [PrototypeOnly(PendingDecisions.None, "WP-061L stuck watchdog: seconds without closing on the player, metres of closing that count as progress, metres of own movement below which the body is 'not moving', check cadence, recovery side-step speed.", true)]
        public const float StuckWindowSeconds = 9f, StuckProgressMetres = 1f, StuckMoveMetres = 0.75f, StuckCheckInterval = 0.5f, StuckSideStepSpeed = 2.5f;

        /// <summary>Tests: shorten the stuck window (≤ 0 = the shipped <see cref="StuckWindowSeconds"/>).</summary>
        public static float StuckWindowOverride { get; set; }

        /// <summary>WP-061L: bodies removed by the stuck watchdog (subset of <see cref="OutOfWorldRemovals"/>).</summary>
        public static int StuckRemovals { get; private set; }
        /// <summary>WP-061L: recoveries tried (repath + side step) — a recovered body never reaches removal.</summary>
        public static int StuckRecoveries { get; private set; }

        private float _stuckWindowStart = -1f, _stuckBest, _nextStuckCheck;
        private Vector3 _stuckAnchor;
        private bool _stuckRecoveryTried;

        /// <summary>WP-061K: a body was removed for leaving the world (tier, where, why). Telemetry / QA; no gameplay listener.</summary>
        public static event Action<EnemyTier, Vector3, OutOfWorldReason, ZombieBodyState> OutOfWorldRemoved;   // WP-076: + body state (target: 0 while sliding / downed / shocked)

        /// <summary>Removals since the last reset (tests / probes).</summary>
        public static int OutOfWorldRemovals { get; private set; }

        private float _lastOnMeshY, _offMeshSince = -1f, _nextWorldCheck;
        private bool _hasOnMeshY;

        private void ResetOutOfWorld()
        {
            _hasOnMeshY = false; _offMeshSince = -1f;
            _stuckWindowStart = -1f; _stuckRecoveryTried = false;
            _nextStuckCheck = Time.time + StuckCheckInterval * ((Mathf.Abs(GetInstanceID()) % 5) / 5f);
            _nextWorldCheck = Time.time + CheckInterval * ((Mathf.Abs(GetInstanceID()) % 4) / 4f);   // phase spread
        }

        /// <summary>Returns true when the body was removed this tick (the caller stops ticking it).</summary>
        private bool TickOutOfWorld()
        {
            float now = Time.time;
            Vector3 p = transform.position;
            if (p.y < AbsoluteFloorY) return RemoveOutOfWorld(OutOfWorldReason.Fell, p);
            if (now < _nextWorldCheck) return false;
            _nextWorldCheck = now + CheckInterval;
            if (IsRising) { _offMeshSince = -1f; return false; }   // the rise handshake starts on the mesh by construction

            if (NavMesh.SamplePosition(p, out NavMeshHit hit, SampleRadius, NavMesh.AllAreas))
            {
                _offMeshSince = -1f;
                if (p.y >= hit.position.y - 1f) { _lastOnMeshY = hit.position.y; _hasOnMeshY = true; }
            }
            else if (_offMeshSince < 0f) _offMeshSince = now;

            if (_hasOnMeshY && p.y < _lastOnMeshY - FallDepth) return RemoveOutOfWorld(OutOfWorldReason.Fell, p);
            if (_offMeshSince >= 0f && now - _offMeshSince >= StrandedSeconds) return RemoveOutOfWorld(OutOfWorldReason.Stranded, p);
            return false;
        }

        /// <summary>
        /// WP-061L · the body stands ON the NavMesh but makes no progress toward the player (S-10 stability seed 26:
        /// the last bodies of round 12 sat on a NavMesh island at the west ring for 120 s). Over one window
        /// (<see cref="StuckWindowSeconds"/>) the body must close by <see cref="StuckProgressMetres"/>; if it did not
        /// AND it either has no complete path or has hardly moved, the first time it is re-routed with a side step,
        /// the second time it is removed like an out-of-world body (despawn, no reward). A body that is fighting
        /// (inside 1.6 × attack range), queued at a chokepoint, waiting in the crowd, netted, staggering or rising
        /// never counts — a real crowd chasing the player keeps closing or keeps moving.
        /// </summary>
        private bool TickStuck()
        {
            float now = Time.time;
            if (now < _nextStuckCheck) return false;
            _nextStuckCheck = now + StuckCheckInterval;
            Transform target = ResolveTarget();

            Vector3 p = transform.position;
            float distance = target != null ? FlatDistance(p, target.position) : -1f;
            bool exempt = target == null || IsRising || _waiting || IsNetted || _animator.IsStaggering || _attack.IsAttacking
                          || _attack.IsGrabbing || _bodyStateOverride != ZombieBodyState.Standing   // WP-076 §2.4-4: held grab, slide / downed / shock
                          || (_pathFollower != null && _pathFollower.IsQueued)
                          || distance <= _attack.Range * 1.6f;
            if (exempt || _stuckWindowStart < 0f || distance < _stuckBest - StuckProgressMetres)
            {
                if (!exempt && _stuckWindowStart >= 0f) _stuckRecoveryTried = false;   // real progress after a recovery
                StartStuckWindow(now, p, distance);
                return false;
            }

            float window = StuckWindowOverride > 0f ? StuckWindowOverride : StuckWindowSeconds;
            if (now - _stuckWindowStart < window) return false;

            bool noPath = _pathFollower == null || !_pathFollower.HasCompletePath;
            bool notMoving = FlatDistance(p, _stuckAnchor) < StuckMoveMetres;
            if (!noPath && !notMoving)
            {
                StartStuckWindow(now, p, distance);   // moving on a complete path (a kited chase) — not stuck
                return false;
            }

            if (!_stuckRecoveryTried)
            {
                _stuckRecoveryTried = true;
                StuckRecoveries++;
                _pathFollower?.Invalidate();
                Vector3 to = target.position - p; to.y = 0f;
                Vector3 side = to.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, to.normalized) : transform.right;
                if (((GetInstanceID() + (int)(now * 10f)) & 1) == 0) side = -side;
                _knockbackVelocity += side * StuckSideStepSpeed;
                StartStuckWindow(now, p, distance);
                return false;
            }

            StuckRemovals++;
            return RemoveOutOfWorld(OutOfWorldReason.Stuck, p);
        }

        private void StartStuckWindow(float now, Vector3 p, float distance)
        {
            _stuckWindowStart = now; _stuckAnchor = p; _stuckBest = distance;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        private bool RemoveOutOfWorld(OutOfWorldReason reason, Vector3 where)
        {
            if (!_alive || _dying) return false;
            OutOfWorldRemovals++;
            try { OutOfWorldRemoved?.Invoke(_tier, where, reason, BodyState); }
            catch (Exception e) { Debug.LogException(e); }
            Despawn();   // ledger: NotifyDespawned → RegisterDespawn (alive − 1); no EnemyDied, no reward
            return true;
        }

        /// <summary>Tests: zero the removal counter.</summary>
        public static void ResetOutOfWorldStatics() { OutOfWorldRemovals = 0; StuckRemovals = 0; StuckRecoveries = 0; StuckWindowOverride = 0f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOutOfWorldDomain() { OutOfWorldRemovals = 0; StuckRemovals = 0; StuckRecoveries = 0; StuckWindowOverride = 0f; OutOfWorldRemoved = null; s_roundBase = default; }

        // WP-078 §5.6 (S-10): the net's counters as one Player-log line per completed round, telemetry on or off (soak reads it).
        private static (int recover, int stuck, int oow, int fell) s_roundBase;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BindRoundLog()
        {
            DD.Prototype.Rounds.RunEvents.RoundStarted -= OnRoundStartedLog; DD.Prototype.Rounds.RunEvents.RoundStarted += OnRoundStartedLog;
            DD.Prototype.Rounds.RunEvents.RoundCompleted -= OnRoundCompletedLog; DD.Prototype.Rounds.RunEvents.RoundCompleted += OnRoundCompletedLog;
        }

        private static void OnRoundStartedLog(int round) => s_roundBase = (StuckRecoveries, StuckRemovals, OutOfWorldRemovals, CorpsesFellOffWorld);

        private static void OnRoundCompletedLog(int round)
        {
            Debug.Log("[OutOfWorld] round=" + round + " stuckRecover=" + (StuckRecoveries - s_roundBase.recover) + " stuckRemove=" + (StuckRemovals - s_roundBase.stuck)
                      + " oow=" + (OutOfWorldRemovals - s_roundBase.oow) + " corpseFell=" + (CorpsesFellOffWorld - s_roundBase.fell) + " totalOow=" + OutOfWorldRemovals);
        }
    }
}
