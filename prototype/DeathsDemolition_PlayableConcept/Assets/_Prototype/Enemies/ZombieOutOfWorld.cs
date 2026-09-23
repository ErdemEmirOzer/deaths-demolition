using System;
using DD.Prototype.Core.Config;
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

        public enum OutOfWorldReason { Fell = 0, Stranded = 1 }

        /// <summary>WP-061K: a body was removed for leaving the world (tier, where, why). Telemetry / QA; no gameplay listener.</summary>
        public static event Action<EnemyTier, Vector3, OutOfWorldReason> OutOfWorldRemoved;

        /// <summary>Removals since the last reset (tests / probes).</summary>
        public static int OutOfWorldRemovals { get; private set; }

        private float _lastOnMeshY, _offMeshSince = -1f, _nextWorldCheck;
        private bool _hasOnMeshY;

        private void ResetOutOfWorld()
        {
            _hasOnMeshY = false; _offMeshSince = -1f;
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

        private bool RemoveOutOfWorld(OutOfWorldReason reason, Vector3 where)
        {
            if (!_alive || _dying) return false;
            OutOfWorldRemovals++;
            try { OutOfWorldRemoved?.Invoke(_tier, where, reason); }
            catch (Exception e) { Debug.LogException(e); }
            Despawn();   // ledger: NotifyDespawned → RegisterDespawn (alive − 1); no EnemyDied, no reward
            return true;
        }

        /// <summary>Tests: zero the removal counter.</summary>
        public static void ResetOutOfWorldStatics() => OutOfWorldRemovals = 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOutOfWorldDomain() { OutOfWorldRemovals = 0; OutOfWorldRemoved = null; }
    }
}
