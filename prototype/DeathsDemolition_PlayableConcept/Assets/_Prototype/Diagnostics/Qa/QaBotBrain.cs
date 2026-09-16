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
        public bool HasWaypoint;
        public Vector3 WaypointDirection; // planar unit vector toward the next path corner
        public float Time;
    }

    public enum QaBotMode
    {
        Patrol = 0,
        Engage = 1,
        Retreat = 2
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
            if (o.SecondsSinceDamage < RetreatSeconds && o.HasTarget)
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

            // ---- reload: when empty, or when nothing is near and the magazine is not full
            bool wantReload = o.WeaponEmpty || (o.WeaponNeedsReload && (!o.HasTarget || o.TargetDistance > FireRange));
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
