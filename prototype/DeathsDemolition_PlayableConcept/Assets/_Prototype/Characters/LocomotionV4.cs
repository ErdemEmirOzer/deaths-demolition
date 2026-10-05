using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-083 B3b (C-00 05.10, R2 N2-05 · locomotion v4 skeleton): the start / stop / pivot / turn contract over the AP-026 clip
    /// plan (AP026-clips-v1: four gaits × eight directions × lead foot for starts and stops, pivots 90 / 135 / 180 to either side
    /// on either foot, turn 45 in place). One stable id per clip drives the Animator's LocomotionV4 sub-state machine through the
    /// int parameter <see cref="ClipParam"/> (set for one frame, AnyState → the clip's state; no trigger left armed when a clip is
    /// missing). ArtAnimatorBuilder creates a state only for the clips the set carries — none today, so the controller is
    /// unchanged until AP-024 / AP-026 deliver. Rig-agnostic (names and directions only).
    /// </summary>
    public static class LocomotionV4
    {
        public const string ClipPrefix = "AP026_tp_";
        public const string StateMachine = "LocomotionV4";
        /// <summary>Int parameter: the clip id to enter this frame, <see cref="None"/> otherwise.</summary>
        public const string ClipParam = "V4Clip";
        public const int None = -1;

        /// <summary>The eight travel directions in body space, clockwise from forward (AP-026 direction names).</summary>
        public static readonly string[] Directions = { "fwd", "fwd_r", "right", "back_r", "back", "back_l", "left", "fwd_l" };
        public static readonly string[] Gaits = { "walk", "jog", "run", "sprint" };
        public static readonly int[] PivotAngles = { 90, 135, 180 };

        public const int StartBase = 0, StopBase = 64, PivotBase = 128, TurnBase = 140, Count = 142;

        /// <summary>Direction index (0..7) of a body-space planar vector (x right, y forward); -1 for a zero vector.</summary>
        public static int Direction8(Vector2 local)
        {
            if (local.sqrMagnitude < 1e-8f) return -1;
            float deg = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;   // 0 forward, +90 right
            return ((Mathf.RoundToInt(deg / 45f) % 8) + 8) % 8;
        }

        public static int StartId(int gait, int dir, bool leftFoot) => StartBase + gait * 16 + dir * 2 + (leftFoot ? 0 : 1);
        public static int StopId(int gait, int dir, bool leftFoot) => StopBase + gait * 16 + dir * 2 + (leftFoot ? 0 : 1);
        public static int PivotId(int angleIndex, bool toRight, bool leftFoot) => PivotBase + angleIndex * 4 + (toRight ? 2 : 0) + (leftFoot ? 0 : 1);
        public static int TurnId(bool leftFoot) => TurnBase + (leftFoot ? 0 : 1);

        /// <summary>The AP-026 clip name of an id (null outside 0..Count-1).</summary>
        public static string ClipName(int id)
        {
            if (id < 0 || id >= Count) return null;
            string foot = (id & 1) == 0 ? "l" : "r";
            if (id < PivotBase)
            {
                int k = id < StopBase ? id - StartBase : id - StopBase;
                return ClipPrefix + Gaits[k / 16] + (id < StopBase ? "_start_" : "_stop_") + Directions[(k % 16) / 2] + "_" + foot;
            }

            if (id < TurnBase)
            {
                int k = id - PivotBase;
                return ClipPrefix + "pivot_" + PivotAngles[k / 4] + ((k & 2) != 0 ? "_r_" : "_l_") + foot;
            }

            return ClipPrefix + "turn_45_" + foot;
        }

        /// <summary>The sub-state machine's state name of an id (the clip name without the prefix).</summary>
        public static string StateName(int id)
        {
            string clip = ClipName(id);
            return clip == null ? null : "V4_" + clip.Substring(ClipPrefix.Length);
        }

        /// <summary>The gait band of a speed: the nearest gait reference speed (m/s) in <paramref name="gaitSpeeds"/> (walk…sprint).</summary>
        public static int GaitFor(float speed, float[] gaitSpeeds)
        {
            int best = 0;
            for (int i = 1; i < gaitSpeeds.Length && i < Gaits.Length; i++)
                if (Mathf.Abs(speed - gaitSpeeds[i]) < Mathf.Abs(speed - gaitSpeeds[best])) best = i;
            return best;
        }
    }

    /// <summary>
    /// WP-083 B3b: start / stop / pivot detection — the event a v4 clip answers. The decision reads where the body is heading: the
    /// motor's commanded velocity when the owner gives it (PlayerMotor.CommandedPlanarVelocity: input × speed before acceleration),
    /// else the velocity a short <see cref="Lookahead"/> ahead (velocity + smoothed acceleration × lookahead, never past a stop along
    /// the travel — from velocity alone a brake and a reversal look the same until the body has stopped, so without the command a
    /// reversal reads as stop + start and pivots come only from the command).
    /// <list type="bullet">
    /// <item>Start: from rest (≥ <see cref="RestSeconds"/> under <see cref="RestSpeed"/>, or still now) the heading reaches
    /// <see cref="MoveSpeed"/> — direction of the heading in body space, gait = the target speed's band.</item>
    /// <item>Pivot: moving at ≥ <see cref="MoveSpeed"/>, the heading turns ≥ <see cref="PivotMinDegrees"/> from the travel (nearest
    /// of 90 / 135 / 180, side by sign; then <see cref="PivotCooldown"/>).</item>
    /// <item>Stop: moving ≥ <see cref="MovingSeconds"/>, the heading stays under <see cref="MoveSpeed"/> × 0.5 for
    /// <see cref="StopConfirm"/> (braking, or commanded) — direction / gait of the last steady (not braking) travel.</item>
    /// </list>
    /// The lead foot is the one NOT planted. Zero allocation.
    /// </summary>
    public sealed class LocomotionV4Events
    {
        public const float RestSpeed = 0.15f, MoveSpeed = 0.6f, RestSeconds = 0.1f, MovingSeconds = 0.2f;
        public const float Lookahead = 0.2f, AccelSmoothing = 0.05f, PivotMinDegrees = 67.5f, PivotCooldown = 0.3f, StopConfirm = 0.05f;

        private enum Phase { Rest, Moving, Stopping }

        private Phase _phase;
        private float _rest, _moving, _cooldown, _stopHold;
        private Vector3 _lastVelocity, _accel, _steadyDir, _lastDir;
        private float _steadySpeed;
        private bool _hasLast;

        public int LastEvent { get; private set; } = LocomotionV4.None;
        /// <summary>The heading of the last tick (commanded, or predicted; tests / diagnostics).</summary>
        public Vector3 Heading { get; private set; }

        public void Reset()
        {
            _phase = Phase.Rest;
            _rest = _moving = _cooldown = _stopHold = 0f;
            _lastVelocity = _accel = _steadyDir = _lastDir = Vector3.zero;
            _steadySpeed = 0f;
            _hasLast = false;
            LastEvent = LocomotionV4.None;
            Heading = Vector3.zero;
        }

        /// <summary>One tick: returns the clip id to play now or <see cref="LocomotionV4.None"/>.</summary>
        public int Feed(Vector3 worldVelocity, Vector3? commanded, Quaternion bodyRotation, float targetSpeed, bool leftPlanted, float[] gaitSpeeds, float dt)
        {
            if (dt <= 1e-5f) return LocomotionV4.None;
            worldVelocity.y = 0f;
            if (_hasLast) _accel = Vector3.Lerp(_accel, (worldVelocity - _lastVelocity) / dt, 1f - Mathf.Exp(-dt / AccelSmoothing));
            _lastVelocity = worldVelocity;
            _hasLast = true;
            float speed = worldVelocity.magnitude;
            if (speed > RestSpeed) _lastDir = worldVelocity / speed;
            Vector3 heading;
            if (commanded.HasValue)
            {
                heading = commanded.Value;
                heading.y = 0f;
            }
            else
            {
                heading = worldVelocity + _accel * Lookahead;
                float along = Vector3.Dot(heading, _lastDir);
                if (along < 0f) heading -= _lastDir * along;   // a brake predicts a stop, never a reversal
            }

            Heading = heading;
            float ahead = heading.magnitude;
            bool braking = Vector3.Dot(_accel, worldVelocity) < 0f;
            bool lead = !leftPlanted;   // the lead foot steps first: the free one
            _cooldown = Mathf.Max(0f, _cooldown - dt);
            int id = LocomotionV4.None;

            switch (_phase)
            {
                case Phase.Rest:
                {
                    bool settled = _rest >= RestSeconds;
                    _rest = speed < RestSpeed ? _rest + dt : 0f;
                    if (ahead >= MoveSpeed)
                    {
                        if (settled || speed < RestSpeed) id = Start(heading, bodyRotation, Mathf.Max(ahead, targetSpeed), lead, gaitSpeeds);
                        _phase = Phase.Moving;
                        _moving = _stopHold = 0f;
                    }

                    break;
                }
                case Phase.Moving:
                    _moving += dt;
                    if (speed >= MoveSpeed && !braking) { _steadyDir = worldVelocity / speed; _steadySpeed = speed; }
                    if (_cooldown <= 0f && speed >= MoveSpeed && ahead >= MoveSpeed)
                    {
                        float turn = Vector3.SignedAngle(worldVelocity, heading, Vector3.up);
                        if (Mathf.Abs(turn) >= PivotMinDegrees)
                        {
                            int a = Mathf.Abs(turn) < 112.5f ? 0 : Mathf.Abs(turn) < 157.5f ? 1 : 2;
                            id = LocomotionV4.PivotId(a, turn > 0f, lead);
                            _cooldown = PivotCooldown;
                            _stopHold = 0f;
                            break;
                        }
                    }

                    _stopHold = (braking || commanded.HasValue) && ahead < MoveSpeed * 0.5f ? _stopHold + dt : 0f;
                    if (_stopHold >= StopConfirm - 1e-4f)
                    {
                        if (_moving >= MovingSeconds && _steadyDir.sqrMagnitude > 0f)
                        {
                            int dir = LocomotionV4.Direction8(Local(bodyRotation, _steadyDir));
                            if (dir >= 0) id = LocomotionV4.StopId(LocomotionV4.GaitFor(_steadySpeed, gaitSpeeds), dir, lead);
                        }

                        _phase = Phase.Stopping;
                    }

                    break;
                case Phase.Stopping:
                    if (speed < RestSpeed) { _phase = Phase.Rest; _rest = 0f; }
                    else if (!braking && ahead >= MoveSpeed)
                    {
                        // re-accelerated before rest: a start only when the travel turned back (a reversal without the motor's command)
                        if (Vector3.Dot(worldVelocity, _steadyDir) < 0f) id = Start(heading, bodyRotation, Mathf.Max(ahead, targetSpeed), lead, gaitSpeeds);
                        _phase = Phase.Moving;
                        _moving = _stopHold = 0f;
                    }

                    break;
            }

            if (id != LocomotionV4.None) LastEvent = id;
            return id;
        }

        private static int Start(Vector3 heading, Quaternion body, float speed, bool lead, float[] gaitSpeeds)
        {
            int dir = LocomotionV4.Direction8(Local(body, heading));
            return dir >= 0 ? LocomotionV4.StartId(LocomotionV4.GaitFor(speed, gaitSpeeds), dir, lead) : LocomotionV4.None;
        }

        private static Vector2 Local(Quaternion body, Vector3 dirW)
        {
            Vector3 l = Quaternion.Inverse(body) * dirW;
            return new Vector2(l.x, l.z);
        }
    }
}
