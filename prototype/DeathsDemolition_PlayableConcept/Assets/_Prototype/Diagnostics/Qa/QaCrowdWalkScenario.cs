using System;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-111 R4 (S-06 + S-01; R4 kit plan B3 / acceptance matrix D2) · <c>-ddQaBotScenario crowdwalk</c>: evidence only, never a QA pass.
    /// The hero walks (not runs) ≥ 5 s through a 40-zombie crowd and passes under the great sakura's crown, so the FINAL capture holds
    /// a clip for the eye check "the planted foot does not slide in the crowd" (D2) and the tree fade (WP-109).
    /// Setup: spawns held, the player invulnerable and stood at the garden path east (20, 1); the walk runs on the NavMesh through the
    /// R7 west gate to a point under the crown's north half (the TD camera looks north, so that half covers the hero) and on toward the
    /// north gate. The crowd: 40 Standard bodies ~2.2 m either side of the route, AI off (the live zombie wall, REQ-046, would stop the
    /// hero) and on the queue idle, facing the route. The stick is held at walk pace (~1.6 m/s, the hero's walk ring) by a feedback on
    /// the measured ground speed. Readouts per second: position, speed, LocoRate, locked feet; a summary line at the end.
    /// </summary>
    public sealed class QaCrowdWalkScenario
    {
        /// <summary>The bot flag (QaRunBot arms the scenario named here; S-01's td-capture --scenario passes it to the video run).</summary>
        public const string Flag = "-ddQaBotScenario";
        public const string Name = "crowdwalk";
        [PrototypeOnly(PendingDecisions.None, "Crowd walk: target ground speed (m/s) — inside the hero walk ring (threshold ≈ 1.7).", true)]
        public const float WalkSpeed = 1.6f;
        public const int CrowdSize = 40;
        public const float SideOffset = 2.2f, ClearOfRoute = 1.4f, Arrive = 0.6f;
        private const float SettleSeconds = 1.5f, GatherSeconds = 2f, WalkTimeout = 40f;
        private static readonly Vector3 Start = new Vector3(20f, 0f, 1f);      // td-capture pose garden_path_east
        private static readonly Vector3 GateWest = new Vector3(29f, 0f, 1f);   // inside the R7 west gate (GreyboxV6Builder R7_W, gate at z 1)
        private static readonly Vector3 Sakura = new Vector3(36f, 0f, 1f);    // GreyboxV6Builder.GreatSakura
        private static readonly Vector3 GateNorth = new Vector3(36f, 0f, 8.8f); // short of the R7 north gate (z 10)

        private enum Phase { Settle, Gather, Walk, Done }
        private Phase _phase = Phase.Settle;
        private float _phaseStart = -1f, _traceAt, _stick;
        private readonly List<Vector3> _route = new List<Vector3>();
        private int _corner;
        private Vector3 _lastPos;
        private float _walkSeconds, _walkMeters, _lockedSeconds, _gateSeconds, _rateSum;
        private int _rateFrames, _placed;
        private float _crownHalfZ;
        private PlayerSkinnedVisual _pv;

        public static QaCrowdWalkScenario Create(string name) => string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) ? new QaCrowdWalkScenario() : null;

        public bool Finished => _phase == Phase.Done;

        /// <summary>Owns the bot's frame while armed (QaRunBot.Driver hands it the rig and the virtual pad).</summary>
        public void Tick(PlayerRig rig, QaVirtualGamepad pad, float runStart, Action<string> log)
        {
            float now = Time.unscaledTime;
            if (_phaseStart < 0f) _phaseStart = now;
            pad.Set(Vector2.zero, Vector2.zero, false, false, false);
            switch (_phase)
            {
                case Phase.Settle:
                    HoldSpawns();
                    if (rig.Health != null) rig.Health.DiagnosisInvulnerable = true;
                    if (now - _phaseStart < SettleSeconds) break;
                    Setup(rig, log);
                    _phase = Phase.Gather;
                    _phaseStart = now;
                    break;
                case Phase.Gather:
                    HoldSpawns();
                    if (now - _phaseStart < GatherSeconds) break;
                    _phase = Phase.Walk;
                    _phaseStart = now;
                    _lastPos = rig.transform.position;
                    _stick = rig.Motor != null && rig.Motor.MaxSpeed > 0.1f ? Mathf.Clamp(WalkSpeed / rig.Motor.MaxSpeed, 0.1f, 1f) : 0.3f;
                    log("crowdwalk walk start pos=" + V(_lastPos) + " corners=" + _route.Count + " stick=" + QaArgs.F(_stick, 2));
                    break;
                case Phase.Walk:
                    Walk(rig, pad, now, log);
                    break;
            }

            pad.Flush();
        }

        private void Setup(PlayerRig rig, Action<string> log)
        {
            Vector3 start = OnMesh(Start, 3f);
            rig.Teleport(start, Quaternion.LookRotation(Vector3.right));
            _crownHalfZ = CrownHalfZ();
            float north = Mathf.Max(3.2f, 0.6f * _crownHalfZ);   // north of the island's trunks (r 2.2 + 0.4), inside the crown
            Vector3 under = OnMesh(Sakura + new Vector3(0f, 0f, north), 2f);
            Vector3[] goals = { start, OnMesh(GateWest, 2f), under, OnMesh(GateNorth, 2f) };
            _route.Clear();
            _route.Add(start);
            var path = new NavMeshPath();
            for (int i = 1; i < goals.Length; i++)
            {
                if (!NavMesh.CalculatePath(goals[i - 1], goals[i], NavMesh.AllAreas, path) || path.corners.Length == 0) { _route.Add(goals[i]); continue; }
                for (int c = 1; c < path.corners.Length; c++) _route.Add(path.corners[c]);
            }

            _corner = 1;
            _placed = PlaceCrowd(log);
            log("crowdwalk setup start=" + V(start) + " under=" + V(under) + " crownHalfZ=" + QaArgs.F(_crownHalfZ, 1) + " route=" + QaArgs.F(RouteLength(), 1) + "m crowd=" + _placed + "/" + CrowdSize);
        }

        private void Walk(PlayerRig rig, QaVirtualGamepad pad, float now, Action<string> log)
        {
            Vector3 pos = rig.transform.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 step = pos - _lastPos; step.y = 0f;
            _lastPos = pos;
            float speed = step.magnitude / dt;
            while (_corner < _route.Count && Planar(_route[_corner] - pos).magnitude <= Arrive) _corner++;
            if (_corner >= _route.Count || now - _phaseStart > WalkTimeout)
            {
                _phase = Phase.Done;
                log("crowdwalk done " + (_corner >= _route.Count ? "arrived" : "timeout") + " walk=" + QaArgs.F(_walkSeconds, 1) + "s " + QaArgs.F(_walkMeters, 1) + "m meanSpeed="
                    + QaArgs.F(_walkSeconds > 0f ? _walkMeters / _walkSeconds : 0f, 2) + " meanLocoRate=" + QaArgs.F(_rateFrames > 0 ? _rateSum / _rateFrames : 0f, 2)
                    + " lockGateOpen=" + QaArgs.F(_gateSeconds, 1) + "s footLocked=" + QaArgs.F(_lockedSeconds, 1) + "s crowd=" + _placed + " crownHalfZ=" + QaArgs.F(_crownHalfZ, 1));
                return;
            }

            // walk pace: hold the measured ground speed at WalkSpeed (the motor scales speed with the stick; weapons and slopes vary it)
            if (now - _phaseStart > 0.5f) _stick = Mathf.Clamp(_stick + 0.05f * (WalkSpeed - speed) / Mathf.Max(WalkSpeed, 0.1f), 0.08f, 1f);
            Vector3 dir = Planar(_route[_corner] - pos).normalized;
            Vector3 fwd, right;
            CameraBasis(out fwd, out right);
            pad.Set(new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, fwd)).normalized * _stick, Vector2.zero, false, false, false);

            if (_pv == null) _pv = rig.GetComponentInChildren<PlayerSkinnedVisual>();
            PlayerSkinnedVisual pv = _pv;
            if (speed > 0.3f)
            {
                _walkSeconds += dt;
                _walkMeters += speed * dt;
                if (pv != null)
                {
                    _rateSum += pv.LocoRate;
                    _rateFrames++;
                    if (pv.FootIk != null && pv.FootIk.LockGateOpen) _gateSeconds += dt;
                    if (pv.FootIk != null && pv.FootIk.LockedFeet > 0) _lockedSeconds += dt;
                }
            }

            if (now >= _traceAt)
            {
                _traceAt = now + 1f;
                log("crowdwalk t=" + QaArgs.F(now - _phaseStart, 1) + " pos=" + V(pos) + " speed=" + QaArgs.F(speed, 2) + " stick=" + QaArgs.F(_stick, 2)
                    + " locoRate=" + (pv != null ? QaArgs.F(pv.LocoRate, 2) : "-") + " lockedFeet=" + (pv != null && pv.FootIk != null ? pv.FootIk.LockedFeet.ToString() : "-") + " corner=" + _corner + "/" + _route.Count);
            }
        }

        /// <summary>40 bodies ~2.2 m either side of the route (then 3.2 m to fill), AI off, on the queue idle, facing the route.</summary>
        private int PlaceCrowd(Action<string> log)
        {
            float length = RouteLength();
            int placed = 0;
            foreach (float side in new[] { SideOffset, SideOffset + 1f })
            {
                int want = CrowdSize - placed;
                if (want <= 0) break;
                int perSide = (want + 1) / 2;
                for (int k = 0; k < perSide && placed < CrowdSize; k++)
                {
                    float s = Mathf.Lerp(1.5f, length - 1.5f, perSide > 1 ? k / (float)(perSide - 1) : 0.5f);
                    Vector3 at, tangent;
                    PointAt(s, out at, out tangent);
                    Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x);
                    for (int sign = -1; sign <= 1 && placed < CrowdSize; sign += 2)
                    {
                        Vector3 want3 = at + normal * (side * sign);
                        if (!NavMesh.SamplePosition(want3, out NavMeshHit hit, 0.8f, NavMesh.AllAreas)) continue;
                        if (DistanceToRoute(hit.position) < ClearOfRoute) continue;
                        Vector3 face = Planar(at - hit.position);
                        ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, 1, hit.position, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : Quaternion.identity);
                        if (z == null) continue;
                        z.enabled = false;   // no AI: no chase, no zombie wall; the visual keeps animating
                        if (z.Skinned != null) z.Skinned.SetWaiting(true);
                        placed++;
                    }
                }
            }

            return placed;
        }

        /// <summary>Half the Z extent of the great sakura's canopy (renderers above 3 m within 5 m of the trunk); 4.5 m when none is found.</summary>
        private static float CrownHalfZ()
        {
            Bounds crown = default;
            bool any = false;
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                Bounds b = r.bounds;
                if (b.max.y < 3f || b.size.y < 1.5f || Planar(b.center - Sakura).magnitude > 5f) continue;
                if (any) crown.Encapsulate(b); else { crown = b; any = true; }
            }

            return any ? Mathf.Clamp(crown.max.z - Sakura.z, 2f, 9f) : 4.5f;
        }

        private static void HoldSpawns()
        {
            EnemySpawnDirector dir = EnemySpawnDirector.Instance;
            if (dir == null) return;
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && (bool)f.GetValue(dir)) { f.SetValue(dir, false); dir.ClearAllEnemies(); }
        }

        private static Vector3 OnMesh(Vector3 p, float radius) => NavMesh.SamplePosition(p, out NavMeshHit hit, radius, NavMesh.AllAreas) ? hit.position : p;

        private float RouteLength()
        {
            float l = 0f;
            for (int i = 1; i < _route.Count; i++) l += Planar(_route[i] - _route[i - 1]).magnitude;
            return l;
        }

        private void PointAt(float s, out Vector3 at, out Vector3 tangent)
        {
            for (int i = 1; i < _route.Count; i++)
            {
                Vector3 seg = Planar(_route[i] - _route[i - 1]);
                float len = seg.magnitude;
                if (len < 1e-4f) continue;
                if (s <= len || i == _route.Count - 1)
                {
                    tangent = seg / len;
                    at = _route[i - 1] + seg * Mathf.Clamp01(s / len);
                    return;
                }

                s -= len;
            }

            at = _route.Count > 0 ? _route[0] : Vector3.zero;
            tangent = Vector3.right;
        }

        private float DistanceToRoute(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < _route.Count; i++)
            {
                Vector3 a = Planar(_route[i - 1]), b = Planar(_route[i]), q = Planar(p);
                Vector3 ab = b - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
            }

            return best;
        }

        private static void CameraBasis(out Vector3 fwd, out Vector3 right)
        {
            Camera cam = Camera.main;
            fwd = cam != null ? Planar(cam.transform.forward) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = cam != null ? Planar(cam.transform.up) : Vector3.forward;
            fwd.Normalize();
            right = new Vector3(fwd.z, 0f, -fwd.x);
        }

        private static Vector3 Planar(Vector3 v) { v.y = 0f; return v; }

        private static string V(Vector3 v) => QaArgs.F(v.x, 1) + "," + QaArgs.F(v.y, 1) + "," + QaArgs.F(v.z, 1);
    }
}
