using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Camera-relative WASD movement. REQ-036, REQ-037, REQ-042, REQ-045, REQ-046.
    ///
    /// WHY A CHARACTER CONTROLLER
    /// --------------------------
    /// REQ-046 wants the player to be physically stopped by zombies while REQ-045 wants
    /// control to never be interrupted. A dynamic rigidbody would let the swarm shove
    /// the player around, which reads as losing control. A CharacterController sweeps
    /// and slides against every non-trigger collider, so a zombie body genuinely blocks
    /// the path, but nothing external can ever apply velocity to the player. Being
    /// surrounded becomes a real threat without a single frame of stolen input.
    ///
    /// Integration requirement for the enemy task: enemy bodies need a NON-TRIGGER
    /// collider on the Enemy layer. Player and Enemy are not ignored in the collision
    /// matrix, so that is all it takes.
    ///
    /// THREE RATES, NOT ONE (sharpness)
    /// --------------------------------
    /// Velocity is driven towards the input's target velocity with a rate that depends
    /// on WHAT THE STEP IS DOING, not on which key is held:
    ///
    ///     building speed  (the step raises the speed)           Acceleration          28
    ///     shedding speed, input released (target == 0)          Deceleration          60
    ///     shedding speed, input points somewhere else           CounterStrafeBraking  90
    ///
    /// The regime is decided geometrically. With u = normalize(target - v), the step
    /// lowers the speed exactly while dot(v, u) is negative, and the switch happens after
    /// a path length of -dot(v, u) in velocity space. That is a closed form, so a single
    /// frame can contain both regimes without any guesswork - see StepPlanarVelocity.
    ///
    /// Why asymmetric: the player reads control from how fast the character stops being
    /// wrong. Building speed slowly keeps the weight; shedding it fast is what "precise"
    /// feels like. Symmetric rates make the same character feel like it is on ice.
    ///
    /// FRAME RATE INDEPENDENCE
    /// -----------------------
    /// Two separate promises, both exact, neither of them a per-frame lerp factor:
    ///
    ///   velocity  each segment integrates v += u * rate * segmentTime, and the segment
    ///             boundary is computed rather than sampled. Velocity as a function of
    ///             wall-clock time is therefore identical at 30, 60 and 120 fps.
    ///
    ///   position  a segment's displacement is the trapezoid
    ///             (vStart + vEnd) * 0.5 * segmentTime, the exact integral of a linear
    ///             velocity ramp. The old code multiplied the frame's END velocity by dt,
    ///             which overshoots by 0.5 * rate * dt * T: an 11% difference in stopping
    ///             distance between 30 and 120 fps. Where the character comes to rest is
    ///             exactly what precise control means, so that error is not acceptable at
    ///             any frame rate.
    ///
    /// NO DASH, NO DODGE (REQ-042). There is deliberately no impulse entry point on
    /// this class; adding one would be a design change, not a code change. The three
    /// rates above only ever move the velocity TOWARDS the input's target velocity, so
    /// no combination of inputs can produce a speed above MoveSpeed. The one exception is
    /// a floor rule, not an input: Namekuji's gel puddle (WP-069 §7b) raises the target
    /// speed ×<see cref="GelSpeedMultiplier"/> and scales every rate ×<see cref="GelSteerMultiplier"/>
    /// while the player stands in one, ramping back over <see cref="GelExitSeconds"/>.
    ///
    /// THE MOVEMENT SPEED UPGRADE
    /// -------------------------
    /// The top speed is <c>PlayerConfigSection.MoveSpeed</c> multiplied by the run's
    /// MovementSpeed tier (see <see cref="PlayerUpgradeBinding"/>). The multiply happens in
    /// <see cref="ApplyConfig"/> and nowhere else, so it is recomputed on a live config edit
    /// AND on a card purchase, and <see cref="MaxSpeed"/> / <see cref="Speed01"/> keep
    /// describing the speed the player actually has.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : PrototypeConfigBehaviour
    {
        [PrototypeOnly(PendingDecisions.None,
            "Downward acceleration. Harsher than real gravity so the blockout's low steps feel crisp.", true)]
        [SerializeField] private float _gravity = -24f;

        [PrototypeOnly(PendingDecisions.None,
            "Downward velocity held while grounded, so the controller stays glued to slopes.", true)]
        [SerializeField] private float _groundedStickSpeed = -2f;

        [PrototypeOnly(PendingDecisions.None, "Slope the player can walk up.", true)]
        [SerializeField] private float _slopeLimitDegrees = 45f;

        [PrototypeOnly(PendingDecisions.None, "Step height the player can walk over.", true)]
        [SerializeField] private float _stepOffset = 0.35f;

        [PrototypeOnly(PendingDecisions.None, "Contact tolerance of the capsule sweep.", true)]
        [SerializeField] private float _skinWidth = 0.03f;

        private readonly PlayerUpgradeBinding _upgrades = new PlayerUpgradeBinding();

        private CharacterController _controller;
        private IInputSource _inputOverride;

        private float _moveSpeed;
        private float _acceleration;
        private float _deceleration;
        private float _counterBrake;
        private float _radius;
        private float _height;

        /// <summary>
        /// (0.1 mm)^2. Squared so the per-frame check needs no square root.
        /// </summary>
        private const float BlockedStepEpsilonSqr = 1e-8f;

        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private Vector2 _rawInput;

        /// <summary>False stops movement without touching aim or firing. Used on death.</summary>
        public bool ControlEnabled { get; set; } = true;

        /// <summary>
        /// WP-069 B2: the weapon's slowdown on the target speed (firing a heavy weapon, focus / ADS),
        /// 0.1..1, written each frame by the WeaponController. Never raises the top speed.
        /// </summary>
        public float WeaponSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// WP-069 B4 (DIRECTION §7b): Namekuji's puddle under the player — the target speed ×1.8 and every rate
        /// ×0.35 (less control, no fall, no damage, firing unaffected), back to normal 0.3 s after stepping out.
        /// Written from the sw_gel data by SpecialGelEffects.
        /// </summary>
        public static float GelSpeedMultiplier = 1.8f;
        public static float GelSteerMultiplier = 0.35f;
        public static float GelExitSeconds = 0.3f;

        private float _gel01;
        private bool _inGel;

        /// <summary>WP-069 B4: standing in a gel puddle this frame.</summary>
        public bool InGel => _inGel;

        /// <summary>WP-069 B4: 1 in a puddle, ramping to 0 over <see cref="GelExitSeconds"/> after leaving it.</summary>
        public float Gel01 => _gel01;

        /// <summary>Current horizontal velocity in world space.</summary>
        public Vector3 PlanarVelocity
        {
            get { return _planarVelocity; }
        }

        /// <summary>Horizontal speed as a 0..1 fraction of the configured maximum.</summary>
        public float Speed01
        {
            get { return _moveSpeed > 0f ? Mathf.Clamp01(_planarVelocity.magnitude / _moveSpeed) : 0f; }
        }

        /// <summary>Raw stick / WASD vector this frame, before the camera basis is applied.</summary>
        public Vector2 RawInput
        {
            get { return _rawInput; }
        }

        public bool IsGrounded
        {
            get { return _controller != null && _controller.isGrounded; }
        }

        /// <summary>
        /// Top speed actually in force: config speed times the MovementSpeed upgrade
        /// multiplier. Anything that normalises against "full speed" wants this one.
        /// </summary>
        public float MaxSpeed
        {
            get { return _moveSpeed; }
        }

        /// <summary>Rate that BUILDS speed, m/s^2. Config value, after clamping.</summary>
        public float Acceleration
        {
            get { return _acceleration; }
        }

        /// <summary>Rate that sheds speed when the input is released, m/s^2.</summary>
        public float Deceleration
        {
            get { return _deceleration; }
        }

        /// <summary>Rate that sheds the speed a new input direction opposes, m/s^2.</summary>
        public float CounterStrafeBraking
        {
            get { return _counterBrake; }
        }

        /// <summary>Owned MovementSpeed tiers as a multiplier on the configured speed. F1 readout.</summary>
        public float MoveSpeedUpgradeMultiplier
        {
            get { return _upgrades.MoveSpeedMultiplier; }
        }

        /// <summary>Inject a different input source, e.g. a replay or a test harness.</summary>
        public void SetInputSource(IInputSource source)
        {
            _inputOverride = source;
        }

        private IInputSource ActiveInput
        {
            get { return _inputOverride ?? InputSourceRegistry.Current; }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        /// <summary>
        /// Bound BEFORE base.OnEnable, because base.OnEnable runs the first ApplyConfig and
        /// that one already has to see the run's owned tiers.
        /// </summary>
        protected override void OnEnable()
        {
            _upgrades.Changed += OnUpgradesChanged;
            _upgrades.Bind();

            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            _upgrades.Unbind();
            _upgrades.Changed -= OnUpgradesChanged;
        }

        /// <summary>A MovementSpeed card was bought, or the build was wiped. Same answer either way.</summary>
        private void OnUpgradesChanged()
        {
            if (HasConfig)
            {
                ApplyConfig(Config);
            }
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            PlayerConfigSection player = config.Player;

            // REQ-036 speed times the run's MovementSpeed tier. The tier is a multiplier and
            // never an absolute, so re-tuning MoveSpeed in the F1 panel keeps a 3-tier build
            // exactly 3 tiers better.
            _moveSpeed = Mathf.Max(0f, player.MoveSpeed) * _upgrades.MoveSpeedMultiplier;
            // The three rates are read, not clamped against each other. The intended
            // ordering (accelerate <= decelerate <= counter-brake) is a DESIGN contract and
            // is guarded by PlayerMovementSharpnessTests, not silently forced here: a
            // designer dragging Deceleration below Acceleration in the F1 panel should see
            // the ice they asked for, and see a red test.
            _acceleration = Mathf.Max(0.01f, player.Acceleration);
            _deceleration = Mathf.Max(0.01f, player.Deceleration);
            _counterBrake = Mathf.Max(0.01f, player.CounterStrafeBraking);
            _radius = Mathf.Max(0.05f, player.CollisionRadius);
            _height = Mathf.Max(_radius * 2f, player.CollisionHeight);

            if (_controller == null)
            {
                _controller = GetComponent<CharacterController>();
            }

            if (_controller == null)
            {
                return;
            }

            _controller.radius = _radius;
            _controller.height = _height;
            _controller.center = new Vector3(0f, _height * 0.5f, 0f);
            _controller.slopeLimit = _slopeLimitDegrees;
            _controller.stepOffset = Mathf.Min(_stepOffset, _height * 0.5f);
            _controller.skinWidth = _skinWidth;
            _controller.minMoveDistance = 0f;
        }

        private void Update()
        {
            if (_controller == null || !HasConfig)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                // Paused. Freeze in place; the input source stays live for the menu.
                return;
            }

            IInputSource input = ActiveInput;
            _rawInput = ControlEnabled && input != null ? input.Move : Vector2.zero;

            Vector3 forward;
            Vector3 right;
            GetMovementBasis(out forward, out right);

            Vector3 desired = right * _rawInput.x + forward * _rawInput.y;
            if (desired.sqrMagnitude > 1f)
            {
                desired.Normalize();
            }

            TickGel(dt);
            float gelSpeed = Mathf.Lerp(1f, GelSpeedMultiplier, _gel01);
            float gelSteer = Mathf.Lerp(1f, GelSteerMultiplier, _gel01);
            // WP-077 ks.body.evade: the tree's speed burst after a hit rides above the weapon clamp.
            Vector3 targetVelocity = desired * (_moveSpeed * Mathf.Clamp(WeaponSpeedMultiplier, 0.1f, 1f) * gelSpeed * DD.Prototype.Rounds.RunTreeHooks.MoveSpeedScale);

            // Advances _planarVelocity AND returns the exact ground distance covered while
            // it was advancing, which is not the same thing as velocity * dt.
            Vector3 planarStep = StepPlanarVelocity(targetVelocity, dt, gelSteer);

            if (_controller.isGrounded && _verticalVelocity <= 0f)
            {
                _verticalVelocity = _groundedStickSpeed;
            }
            else
            {
                _verticalVelocity += _gravity * dt;
            }

            // Vertical is deliberately left on the old velocity * dt integration: gravity,
            // the grounded stick speed and stepOffset were measured together and are
            // guarded by PlayerWallContactTests. Sharpness is a horizontal problem.
            Vector3 motion = planarStep;
            motion.y = _verticalVelocity * dt;

            Vector3 before = _controller.transform.position;
            _controller.Move(motion);
            DropBlockedVelocity(planarStep, _controller.transform.position - before);
        }

        /// <summary>
        /// Removes the part of <see cref="_planarVelocity"/> that the controller was not
        /// actually allowed to spend, and nothing else.
        ///
        /// Without this the motor keeps believing it is running at full speed INTO a wall
        /// or a crowd it is not moving through. The player then has to brake a velocity
        /// they never had before they can turn away - measured at 0.069 s and 0.214 m of
        /// pure lag, and it lands precisely when control matters most: in contact.
        ///
        /// The correction is deliberately one-sided. <paramref name="blocked"/> is what was
        /// asked for minus what was achieved, so it points into the obstacle; only the
        /// component of velocity pushing that way is removed. The along-surface component
        /// is untouched, which is what keeps sliding along a wall fast (PlayerWallContactTests).
        /// </summary>
        private void DropBlockedVelocity(Vector3 requestedStep, Vector3 achievedStep)
        {
            requestedStep.y = 0f;
            achievedStep.y = 0f;

            Vector3 blocked = requestedStep - achievedStep;

            // Contact resolution, skinWidth and slope handling all nibble sub-millimetre
            // amounts every frame. Correcting for those would bleed speed on open ground,
            // so anything under a tenth of a millimetre per frame is treated as no contact.
            if (blocked.sqrMagnitude < BlockedStepEpsilonSqr)
            {
                return;
            }

            Vector3 into = blocked.normalized;
            float pushing = Vector3.Dot(_planarVelocity, into);
            if (pushing > 0f)
            {
                _planarVelocity -= into * pushing;
            }
        }

        /// <summary>
        /// One frame of horizontal integration. Writes <see cref="_planarVelocity"/> and
        /// returns the displacement to feed <c>CharacterController.Move</c>.
        ///
        /// THE FRAME IS SPLIT, NOT SAMPLED
        /// -------------------------------
        /// Velocity travels along the straight segment from v to targetVelocity, so with
        ///
        ///     u = normalize(targetVelocity - v)      constant while the input is held
        ///
        /// the speed falls exactly while dot(v, u) is negative and rises afterwards. The
        /// turning point sits at path length -dot(v, u), a closed form. The frame is cut
        /// there and each piece is integrated with its own rate, which is why a 30 fps
        /// frame that contains a whole counter-strafe reversal lands on the same velocity
        /// AND the same position as eight 240 fps frames.
        ///
        /// Three segments is the hard maximum for a constant target: brake, accelerate,
        /// coast. The trailing coast outside the loop only ever catches float residue.
        ///
        /// REQ-042: every branch moves v towards targetVelocity and stops there. There is
        /// no path through this method that adds speed the input did not ask for.
        /// </summary>
        private Vector3 StepPlanarVelocity(Vector3 targetVelocity, float dt, float rateScale = 1f)
        {
            // Input released -> stopping brake. Input pointing elsewhere -> counter-strafe
            // brake. Chosen once per frame: the target does not move inside a frame.
            // rateScale < 1 only on gel (WP-069 §7b): every rate slows, so the player slides.
            rateScale = Mathf.Clamp(rateScale, 0.05f, 1f);
            float brakeRate = (targetVelocity.sqrMagnitude > 1e-8f ? _counterBrake : _deceleration) * rateScale;
            float accelerationRate = _acceleration * rateScale;

            Vector3 displacement = Vector3.zero;
            float remaining = dt;

            for (int segment = 0; segment < 3 && remaining > 0f; segment++)
            {
                Vector3 toTarget = targetVelocity - _planarVelocity;
                float pathLeft = toTarget.magnitude;

                if (pathLeft <= 1e-6f)
                {
                    _planarVelocity = targetVelocity;
                    break;
                }

                Vector3 direction = toTarget / pathLeft;

                // Negative: this step is taking speed away, so it is braking, not driving.
                float along = Vector3.Dot(_planarVelocity, direction);

                float rate;
                float segmentLength;
                if (along < 0f)
                {
                    rate = brakeRate;
                    segmentLength = Mathf.Min(pathLeft, -along);
                }
                else
                {
                    rate = accelerationRate;
                    segmentLength = pathLeft;
                }

                float segmentTime = segmentLength / rate;
                if (segmentTime > remaining)
                {
                    segmentTime = remaining;
                }

                Vector3 endVelocity = _planarVelocity + direction * (rate * segmentTime);

                // Exact integral of a linear ramp, so the distance covered while changing
                // speed does not depend on where the frame boundaries happen to fall.
                displacement += (_planarVelocity + endVelocity) * (0.5f * segmentTime);

                _planarVelocity = endVelocity;
                remaining -= segmentTime;
            }

            if (remaining > 0f)
            {
                displacement += _planarVelocity * remaining;
            }

            return displacement;
        }

        /// <summary>
        /// WP-069 B4: S-03's puddle registry says whether the feet stand in gel; the slide starts at once and
        /// fades over <see cref="GelExitSeconds"/>. Usage events for S-07 / S-08 (the player's slide cues).
        /// </summary>
        private void TickGel(float dt)
        {
            bool inGel = DD.Prototype.Enemies.GelPuddleRegistry.TryFind(transform.position, out int puddle);
            if (inGel)
            {
                _gel01 = 1f;
            }
            else if (_gel01 > 0f)
            {
                _gel01 = GelExitSeconds > 0f ? Mathf.Max(0f, _gel01 - dt / GelExitSeconds) : 0f;
            }

            if (inGel == _inGel)
            {
                return;
            }

            _inGel = inGel;
            DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Namekuji,
                inGel ? DD.Prototype.Weapons.SpecialUsageKind.PlayerSlideStarted : DD.Prototype.Weapons.SpecialUsageKind.PlayerSlideEnded,
                DD.Prototype.Weapons.SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).FieldVfxFamily, transform.position, _planarVelocity, 0f, inGel ? 0f : GelExitSeconds, puddle);
        }

        /// <summary>
        /// REQ-036. The movement basis is the fixed camera's yaw, flattened onto the
        /// ground. It is constant while the rig lives, so "forward" never drifts under
        /// the player's fingers. With no rig present the world axes are used, which
        /// keeps the motor testable without a camera.
        /// </summary>
        private static void GetMovementBasis(out Vector3 forward, out Vector3 right)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig != null)
            {
                forward = rig.PlanarForward;
                right = rig.PlanarRight;
                return;
            }

            forward = Vector3.forward;
            right = Vector3.right;
        }

        /// <summary>
        /// Hard teleport, for spawning and for the debug panel. Disables the controller
        /// around the move so the capsule does not sweep across the map.
        /// </summary>
        public void Teleport(Vector3 position)
        {
            if (_controller == null)
            {
                transform.position = position;
                return;
            }

            bool wasEnabled = _controller.enabled;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = wasEnabled;

            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _gel01 = 0f;
        }

        /// <summary>Stops the player dead without disabling control.</summary>
        public void ClearVelocity()
        {
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
