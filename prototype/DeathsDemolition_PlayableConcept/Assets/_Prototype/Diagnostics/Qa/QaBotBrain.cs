#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine;

namespace DD.Prototype.Qa
{
    /// <summary>What the bot sees this frame (world space, y-up). Pure data so the brain can be unit-tested.</summary>
    public struct QaBotObservation
    {
        public Vector3 PlayerPosition;
        public Vector3 CameraForward;   // planar, normalised
        public Vector3 CameraRight;     // planar, normalised
        public bool HasTarget;
        public Vector3 TargetPosition;
        public float TargetDistance;
        public bool TargetLineOfSight;
        public int NearbyEnemies;        // within CrowdRadius
        public Vector3 CrowdDirection;   // planar unit vector toward the centroid of the nearby enemies (zero when none)
        public float HealthRatio;
        public float SecondsSinceDamage; // large when never damaged
        public bool WeaponCanFire;
        public bool WeaponNeedsReload;
        public bool WeaponEmpty;
        public float WeaponAmmoRatio;    // WP-064B: magazine fill 0..1 (1 when unknown / Special in hand)
        public float NearestThreatDistance; // WP-064B: planar distance to the closest live zombie (large when none)
        public bool HasWaypoint;
        public Vector3 WaypointDirection; // planar unit vector toward the next path corner
        public float Time;
    }

    public enum QaBotMode
    {
        Patrol = 0,
        Engage = 1,
        Retreat = 2,
        Kite = 3      // WP-064B: threat-field escape (open ground away from the crowd, trigger held)
    }

    /// <summary>Stick-space command for the virtual gamepad plus the mode chosen this frame.</summary>
    public struct QaBotCommand
    {
        public Vector2 Move;
        public Vector2 Aim;
        public bool Fire;
        public bool Reload;
        public QaBotMode Mode;
    }

    /// <summary>
    /// WP-026 · Deterministic decision layer of the run bot. Aim at the nearest zombie, fire in
    /// bursts while it is in range with a clear line, keep a fighting distance, retreat for a
    /// short window after taking damage, otherwise walk the patrol waypoint. All randomness
    /// comes from the seeded <see cref="System.Random"/> handed in by the driver.
    /// </summary>
    public sealed class QaBotBrain
    {
        public const float FireRange = 24f;
        public const float EngageRadius = 22f;
        public const float CrowdRadius = 12f;
        public const float PreferredDistanceMin = 8f;    // kite: the player (6.2 m/s) out-runs every tier (≤ 4.6 m/s)
        public const float PreferredDistanceMax = 14f;
        public const float RetreatSeconds = 1.0f;
        public const float LowHealthRetreatRatio = 0.65f;
        public const int CrowdRetreatCount = 5;
        public const float SafeReloadDistance = 10f;
        public const float ThreatRadius = 11f;             // WP-064B: a zombie this close hands movement to the escape planner
        public const float ThreatRadiusLowHealth = 15f;   // below half health the planner takes over earlier

        /// <summary>
        /// WP-064B · Score of one escape direction (higher is better). Pure so the rule is unit-tested:
        /// <paramref name="clear"/> = free NavMesh length along the direction (≤ 6 m), <paramref name="openness"/> =
        /// distance from the probe point to the nearest NavMesh edge (corners score low), <paramref name="threats"/> /
        /// <paramref name="weights"/> = planar zombie positions and their weight (telegraphing / Elite / Ultra heavier).
        /// Terms: predicted danger at the probe (inverse-square), bodies inside the walking lane (the crowd wall that
        /// pinned the bot in run 6), heading into the crowd, plus clearance, openness, continuity and an optional
        /// attraction (a Special Weapon pickup).
        /// </summary>
        public static float ScoreDirection(Vector3 position, Vector3 direction, float clear, float openness,
            Vector3[] threats, float[] weights, int count, Vector3 previousDirection, Vector3 attraction)
        {
            return ScoreDirection(position, direction, clear, openness, threats, weights, count, previousDirection, attraction, Vector3.zero, 0f);
        }

        /// <summary>
        /// As above plus a pull back toward the arena <paramref name="centre"/> that grows past 55 % of the
        /// <paramref name="ringHalfExtent"/> (WP-064B soak 13: a round-8 crowd of 17 herded the bot into the ±45 m ring's corner).
        /// </summary>
        public static float ScoreDirection(Vector3 position, Vector3 direction, float clear, float openness,
            Vector3[] threats, float[] weights, int count, Vector3 previousDirection, Vector3 attraction, Vector3 centre, float ringHalfExtent)
        {
            if (clear < 1.2f)
            {
                return -1e6f;   // a wall / edge within one stride
            }

            float step = Mathf.Min(clear, 3.5f);
            Vector3 probe = position + direction * step;
            float danger = 0f, lane = 0f, heading = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 toZ = threats[i] - position; toZ.y = 0f;
                float dist = toZ.magnitude;
                Vector3 fromProbe = threats[i] - probe; fromProbe.y = 0f;
                float pd = Mathf.Max(0.45f, fromProbe.magnitude);
                danger += weights[i] / (pd * pd);
                if (dist > 1e-3f)
                {
                    float along = Vector3.Dot(toZ, direction);
                    if (along > 0f)
                    {
                        heading += weights[i] * (along / dist) / Mathf.Max(1f, dist);
                        float lateral = Mathf.Sqrt(Mathf.Max(0f, dist * dist - along * along));
                        if (along < 4f && lateral < 1.1f) lane += weights[i];
                    }
                }
            }

            // WP-064B v3: time-to-contact veto (velocity obstacle, linear) — the player runs at 6.2 m/s along the direction, each
            // body closes at 4.6 m/s toward the player; contact radius 1.1 m (+3 m for a weight ≥ 3 = telegraphing body)
            float ttcMin = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 rel = threats[i] - position; rel.y = 0f;
                float dist = rel.magnitude;
                if (dist > 12f || dist < 1e-3f) continue;
                Vector3 vz = -rel / dist * 4.6f;                 // body velocity toward the player
                Vector3 vr = direction * 6.2f - vz;               // player velocity relative to the body
                float r = weights[i] >= 3f ? 4.1f : 1.1f;
                float a = vr.sqrMagnitude, b = -2f * Vector3.Dot(rel, vr), c = rel.sqrMagnitude - r * r;
                if (c <= 0f) { ttcMin = 0f; break; }
                float disc = b * b - 4f * a * c;
                if (disc < 0f || a < 1e-4f) continue;
                float t = (-b - Mathf.Sqrt(disc)) / (2f * a);
                if (t > 0f && t < ttcMin) ttcMin = t;
            }
            float ttcTerm = ttcMin < 1.2f ? 100f : 6f * Mathf.Pow(Mathf.Clamp01((3f - ttcMin) / 1.8f), 2f);

            float score = -9f * danger - 4f * lane - 3f * heading - ttcTerm + 0.35f * Mathf.Min(clear, 6f) + 0.8f * Mathf.Min(openness, 6f);
            if (previousDirection.sqrMagnitude > 1e-4f) score += 0.4f * Vector3.Dot(direction, previousDirection);
            if (attraction.sqrMagnitude > 1e-4f) score += 1.2f * Vector3.Dot(direction, attraction);   // magnitude carries the weight (pickup 1, plaza loop 1.8)
            if (ringHalfExtent > 0f)
            {
                Vector3 toCentre = centre - position; toCentre.y = 0f;
                float edge = Mathf.Max(Mathf.Abs(probe.x - centre.x), Mathf.Abs(probe.z - centre.z)) / ringHalfExtent;   // 0 centre … 1 ring
                float pull = Mathf.Clamp01((edge - 0.55f) / 0.35f);
                if (toCentre.sqrMagnitude > 1e-4f) score += 3f * pull * Vector3.Dot(direction, toCentre.normalized);
                if (edge > 0.9f) score -= 6f * (edge - 0.9f) / 0.1f;   // the last 4.5 m before the ring are a trap
            }
            return score;
        }

        private readonly System.Random _random;
        private float _strafeSign = 1f;
        private float _nextStrafeFlip;
        private bool _reloadLatch;
        private float _blockedSince = -1f;

        public QaBotBrain(int seed)
        {
            _random = new System.Random(seed);
        }

        public QaBotCommand Decide(in QaBotObservation o)
        {
            var cmd = new QaBotCommand();

            // ---- mode
            if (o.HasTarget && (o.SecondsSinceDamage < RetreatSeconds || o.HealthRatio < LowHealthRetreatRatio || o.NearbyEnemies >= CrowdRetreatCount))
            {
                cmd.Mode = QaBotMode.Retreat;
            }
            else if (o.HasTarget && o.TargetDistance <= EngageRadius)
            {
                cmd.Mode = QaBotMode.Engage;
            }
            else
            {
                cmd.Mode = QaBotMode.Patrol;
            }

            // ---- aim: always toward the target when one exists (stick holds the last direction otherwise)
            Vector3 toTarget = Vector3.zero;
            if (o.HasTarget)
            {
                toTarget = o.TargetPosition - o.PlayerPosition;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 1e-6f)
                {
                    toTarget.Normalize();
                    cmd.Aim = WorldToStick(toTarget, o.CameraForward, o.CameraRight);
                }
            }

            // ---- fire: hold the trigger while a target is in range with a clear line (automatic weapon; the
            // runtime paces shots by its own cooldown, so the trigger is not toggled per frame)
            cmd.Fire = o.HasTarget && o.TargetDistance <= FireRange && o.TargetLineOfSight && o.WeaponCanFire;

            // ---- reload: when empty; or top up in a safe window (nothing within SafeReloadDistance) once the
            // magazine is under half — WP-064B: the run-6 death started with an empty magazine at 2 m
            bool safeWindow = o.NearestThreatDistance > SafeReloadDistance;
            bool wantReload = o.WeaponEmpty
                              || (o.WeaponNeedsReload && (!o.HasTarget || o.TargetDistance > FireRange))
                              || (o.WeaponNeedsReload && safeWindow && o.WeaponAmmoRatio < 0.5f);
            cmd.Reload = wantReload && !_reloadLatch;
            _reloadLatch = wantReload;   // one press per request; PrototypeInputSource reads WasPressedThisFrame

            // ---- move
            Vector3 moveWorld = Vector3.zero;
            switch (cmd.Mode)
            {
                case QaBotMode.Retreat:
                    moveWorld = o.CrowdDirection.sqrMagnitude > 1e-4f ? -o.CrowdDirection : -toTarget;
                    break;

                case QaBotMode.Engage:
                    // blocked fire line: slide sideways until the line clears (flip the side every second)
                    if (o.HasTarget && !o.TargetLineOfSight)
                    {
                        if (_blockedSince < 0f) _blockedSince = o.Time;
                        if (o.Time - _blockedSince > 1f) { _strafeSign = -_strafeSign; _blockedSince = o.Time; }
                        moveWorld = (Vector3.Cross(Vector3.up, toTarget) * _strafeSign * 0.9f + toTarget * 0.3f).normalized;
                        break;
                    }

                    _blockedSince = -1f;
                    if (o.Time >= _nextStrafeFlip)
                    {
                        _strafeSign = _random.NextDouble() < 0.5 ? -1f : 1f;
                        _nextStrafeFlip = o.Time + 1.2f + (float)_random.NextDouble() * 1.8f;
                    }

                    Vector3 side = Vector3.Cross(Vector3.up, toTarget) * _strafeSign;
                    Vector3 away = o.CrowdDirection.sqrMagnitude > 1e-4f ? -o.CrowdDirection : -toTarget;
                    if (o.TargetDistance > PreferredDistanceMax)
                    {
                        moveWorld = (toTarget * 0.7f + side * 0.3f).normalized;   // close in slowly
                    }
                    else if (o.TargetDistance < PreferredDistanceMin)
                    {
                        moveWorld = (away * 0.85f + side * 0.35f).normalized;      // kite backwards, keep shooting
                    }
                    else
                    {
                        moveWorld = (side * 0.8f + away * 0.3f).normalized;        // circle, drifting away from the pack
                    }

                    // crowd pressure: several bodies close → prioritise distance over the strafe
                    if (o.NearbyEnemies >= 3)
                    {
                        moveWorld = (away * 0.8f + side * 0.3f).normalized;
                    }

                    break;

                default:
                    if (o.HasWaypoint)
                    {
                        moveWorld = o.WaypointDirection;
                    }

                    break;
            }

            if (moveWorld.sqrMagnitude > 1e-6f)
            {
                cmd.Move = WorldToStick(moveWorld, o.CameraForward, o.CameraRight);
            }

            return cmd;
        }

        /// <summary>Inverse of PlayerMotor/PlayerAim's camera basis: stick = (dot right, dot forward).</summary>
        public static Vector2 WorldToStick(Vector3 worldDirection, Vector3 cameraForward, Vector3 cameraRight)
        {
            Vector3 d = worldDirection;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-8f)
            {
                return Vector2.zero;
            }

            d.Normalize();
            var stick = new Vector2(Vector3.Dot(d, cameraRight), Vector3.Dot(d, cameraForward));
            return Vector2.ClampMagnitude(stick, 1f);
        }

        /// <summary>Round-trip used by the tests: stick → world through the same basis the game uses.</summary>
        public static Vector3 StickToWorld(Vector2 stick, Vector3 cameraForward, Vector3 cameraRight)
        {
            Vector3 d = cameraRight * stick.x + cameraForward * stick.y;
            d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.zero;
        }
    }
}
#endif
