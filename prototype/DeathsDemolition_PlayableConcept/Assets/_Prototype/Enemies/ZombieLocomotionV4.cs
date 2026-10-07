using UnityEngine;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-096 · zombie locomotion v4 (S-03, runtime; the pure maths is <see cref="ZombieLocomotionMath"/>):
    /// <list type="bullet">
    /// <item>P1 / P8: the gait cadence follows the body's REAL forward ground speed, measured after Move and the NavMesh re-seat,
    /// capped at its own walk intent and critically damped (<see cref="GroundForwardSpeed"/>); the lateral part is a readout
    /// (<see cref="GroundLateralSpeed"/>). A knockback or a crowd shove no longer speeds the stepping up, a jammed body idles.</item>
    /// <item>P2: the per-spawn move speed jitter (<see cref="SpeedJitter"/>, ZombieLocomotionMath.SpeedSpread) applied to the walk
    /// speed itself — the driven body's spawn ordinal picks it, so the honest rate follows it.</item>
    /// <item>P6: the own walk speed ramps at the tier acceleration (braking ×BrakeScale); the hold states (stagger, recovery,
    /// root, shock, gel) still stop at once — except a Runner's stumble, which bleeds its speed off over StumbleBrakeSeconds.</item>
    /// <item>P7: the heading turns with a limited angular acceleration under the tier's turn-rate cap, and — with
    /// <see cref="VelocityFollowsFacing"/> — the walk goes where the body faces, slowing on hard turns (no crab-walk).</item>
    /// </list>
    /// Hook for the AP-033 start / stop / pivot clips (not built — clips not delivered): <see cref="CommandedVelocity"/> is the
    /// intent before acceleration (the LocomotionV4Events "commanded" input) and the measured ground speed the travel; a hard turn
    /// past ZombieLocomotionMath.TurnSlowError is where a pivot clip would play. Zero allocation per frame.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        /// <summary>
        /// WP-096 P7 default (invented): the own walk direction follows the body's facing. The path stays reachable because the
        /// speed is shared down with the heading error (ZombieLocomotionMath.HeadingSpeedShare) and a hard turn walks the path
        /// heading again (FacingBlend) — the body never orbits a corner it must turn into. false = the WP-003 path heading.
        /// </summary>
        public const bool DefaultVelocityFollowsFacing = true;

        /// <summary>WP-096 P7 switch (tests / A-B; reset to <see cref="DefaultVelocityFollowsFacing"/> on domain load).</summary>
        public static bool VelocityFollowsFacing = DefaultVelocityFollowsFacing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLocomotionV4Statics() => VelocityFollowsFacing = DefaultVelocityFollowsFacing;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P7: inside this distance to the target (metres, flat) the walk follows the path heading at full speed — the " +
            "facing rule and its turn slow-down apply only on the approach (P12 crowd press round a standing player). Invented, tune.", true)]
        public const float FacingRuleMinDistance = 8f;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P1: critically damped smoothing time of the measured forward / lateral ground speed the cadence reads, seconds " +
            "(absorbs the 3-frame NavMesh re-seat and contact jitter). Invented, tune.", true)]
        public const float GroundSpeedSmoothing = 0.1f;

        [PrototypeOnly(PendingDecisions.None,
            "WP-096 P6: seconds over which a Runner's stumble bleeds its walk speed off along its facing (the stumble clip plays " +
            "in place; before, the body dropped 4.6 → 0 m/s in one frame). Invented, tune.", true)]
        public const float StumbleBrakeSeconds = 0.45f;

        private float _baseMoveSpeed = DefaultMoveSpeed;   // the stat snapshot's walk speed; _moveSpeed = this × _speedJitter
        private float _speedJitter = 1f;
        private float _steerSpeed;                          // P6: the accelerated own walk speed
        private float _yawRate;                             // P7: heading angular velocity, °/s
        private float _groundForward, _groundForwardRate, _groundLateral, _groundLateralRate, _groundForwardRaw;
        private bool _stumbleBraking;
        private float _stumbleDecel;
        private Vector3 _commandedVelocity;

        /// <summary>WP-096 P2: this life's move speed multiplier (1 on a block body or with GaitVariety 0).</summary>
        public float SpeedJitter => _speedJitter;
        /// <summary>WP-096 P2: the walk speed this life runs with (stat snapshot × jitter; <see cref="Stats"/> keeps the unjittered value).</summary>
        public float WalkSpeed => _moveSpeed;
        /// <summary>WP-096 P1: the smoothed real forward ground speed the cadence follows (m/s, ≥ 0, ≤ own intent).</summary>
        public float GroundForwardSpeed => _groundForward;
        /// <summary>WP-096 P1: the smoothed real lateral ground speed (+ = the body's right), readout only.</summary>
        public float GroundLateralSpeed => _groundLateral;
        /// <summary>WP-096 P1: the unsmoothed, uncapped forward ground speed of the last tick (diagnostics).</summary>
        public float GroundForwardSpeedRaw => _groundForwardRaw;
        /// <summary>WP-096 P6: the accelerated own walk speed (m/s).</summary>
        public float SteerSpeed => _steerSpeed;
        /// <summary>WP-096 P7: the heading's angular velocity (°/s, + = clockwise seen from above).</summary>
        public float YawRate => _yawRate;
        /// <summary>WP-096 hook: the own walk intent before acceleration (path heading × speed + lunge + queue sway) — the old own velocity.</summary>
        public Vector3 CommandedVelocity => _commandedVelocity;
        /// <summary>WP-096 P6: a Runner stumble is bleeding its speed off.</summary>
        public bool StumbleBraking => _stumbleBraking;

        private void ResetLocomotionV4()
        {
            _speedJitter = 1f;
            _moveSpeed = _baseMoveSpeed;
            _steerSpeed = 0f;
            _yawRate = 0f;
            _groundForward = _groundForwardRate = _groundLateral = _groundLateralRate = _groundForwardRaw = 0f;
            _stumbleBraking = false;
            _stumbleDecel = 0f;
            _commandedVelocity = Vector3.zero;
        }

        /// <summary>WP-096 P2: walk at the stat speed × <paramref name="jitter"/> for this life (Initialise, after the driven body picked it).</summary>
        private void ApplySpeedJitter(float jitter)
        {
            _speedJitter = jitter > 0f ? jitter : 1f;
            _moveSpeed = _baseMoveSpeed * _speedJitter;
        }

        /// <summary>
        /// WP-096 P6 / P7: the own walk velocity this tick. <paramref name="heading"/> is the path direction (zero = none),
        /// <paramref name="desiredSpeed"/> the speed the gameplay asks for (tier × queue × wall × slow × suppression × approach stop).
        /// <paramref name="hardHold"/>: a root / shock / gel hold, which stops even a stumbling Runner at once.
        /// </summary>
        private Vector3 SteerOwnVelocity(bool canWalk, bool hardHold, Vector3 heading, float desiredSpeed, float distanceToTarget, float dt)
        {
            Vector3 forward = FlatForward(transform);
            if (!canWalk)
            {
                if (_stumbleBraking && !hardHold && _steerSpeed > 0f)
                {
                    _steerSpeed = Mathf.MoveTowards(_steerSpeed, 0f, _stumbleDecel * dt);
                    if (_steerSpeed <= 0f) _stumbleBraking = false;
                    return forward * _steerSpeed;
                }

                // stagger / recovery / root / shock / gel / rise: the body stops at once (gameplay contract, WP-003 / WP-055 / WP-076)
                _stumbleBraking = false;
                _steerSpeed = 0f;
                return Vector3.zero;
            }

            _stumbleBraking = false;
            if (heading.sqrMagnitude < 1e-6f)
            {
                _steerSpeed = 0f;   // no usable heading (no target / inside the steering dead zone): as before, no own walk
                return Vector3.zero;
            }

            Vector3 direction = heading;
            float share = 1f;
            // Near the target the crowd shoulders the body round constantly (large heading errors every frame): there the walk keeps
            // the WP-003 path heading at full speed so the press on a standing player is unchanged (P12: 13 < 15 within 6.5 m in 20 s
            // with the facing rule everywhere, 17–19 before).
            if (VelocityFollowsFacing && distanceToTarget > FacingRuleMinDistance)
            {
                float error = Vector3.Angle(forward, heading);
                share = ZombieLocomotionMath.HeadingSpeedShare(error);
                float t = ZombieLocomotionMath.FacingBlend(error);
                direction = t >= 1f ? heading : t <= 0f ? forward : Vector3.Slerp(forward, heading, t);
                direction.y = 0f;
                float m = direction.magnitude;
                direction = m > 1e-4f ? direction / m : heading;
            }

            float accel = ZombieLocomotionMath.AccelerationFor(_tier);
            _steerSpeed = ZombieLocomotionMath.Accelerate(_steerSpeed, desiredSpeed * share, accel, accel * ZombieLocomotionMath.BrakeScale, dt);
            return direction * _steerSpeed;
        }

        /// <summary>WP-096 P7: turn towards <paramref name="facing"/> — angular velocity capped at the tier turn rate, its change at cap / TurnRampSeconds.</summary>
        private void TurnTowards(Vector3 facing, float dt)
        {
            float targetYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            float yaw = transform.eulerAngles.y;
            float alpha = ZombieLocomotionMath.TurnRampSeconds > 0f ? _turnSpeedDegrees / ZombieLocomotionMath.TurnRampSeconds : 0f;
            float next = ZombieLocomotionMath.StepYaw(yaw, ref _yawRate, targetYaw, _turnSpeedDegrees, alpha, dt);
            transform.rotation = Quaternion.Euler(0f, next, 0f);
        }

        /// <summary>
        /// WP-096 P1: the real ground motion of this tick (position now − <paramref name="before"/>, after Move and the re-seat)
        /// split along the body: the forward part, floored at 0 and capped at the own walk speed, drives the cadence; the lateral
        /// part is kept as a readout. Both critically damped (frame-rate independent).
        /// </summary>
        private void MeasureGroundMotion(Vector3 before, Vector3 ownVelocity, float dt)
        {
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            Vector3 forward = FlatForward(transform);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);   // Cross(up, forward)
            float inv = 1f / dt;
            float forwardSpeed = Vector3.Dot(moved, forward) * inv;
            float lateralSpeed = Vector3.Dot(moved, right) * inv;
            ownVelocity.y = 0f;
            _groundForwardRaw = forwardSpeed;
            float cadence = ZombieLocomotionMath.CadenceSpeed(forwardSpeed, ownVelocity.magnitude);
            ZombieLocomotionMath.CriticallyDamped(ref _groundForward, ref _groundForwardRate, cadence, GroundSpeedSmoothing, dt);
            if (_groundForward < 0f)
            {
                _groundForward = 0f;
                _groundForwardRate = 0f;
            }

            ZombieLocomotionMath.CriticallyDamped(ref _groundLateral, ref _groundLateralRate, lateralSpeed, GroundSpeedSmoothing, dt);
        }

        /// <summary>WP-096 P6: a Runner's stumble keeps its momentum — the walk speed it had bleeds off over StumbleBrakeSeconds.</summary>
        private void BeginStumbleBrake()
        {
            if (_steerSpeed <= 0f) return;
            _stumbleBraking = true;
            _stumbleDecel = _steerSpeed / Mathf.Max(0.05f, StumbleBrakeSeconds);
        }
    }
}
