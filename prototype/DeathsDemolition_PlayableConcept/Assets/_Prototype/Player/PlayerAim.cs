using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Aim direction, character facing and camera look-ahead. REQ-033, REQ-037, REQ-038.
    ///
    /// INDEPENDENCE (REQ-037)
    /// ----------------------
    /// This component writes ONLY the character's rotation. It never reads or writes
    /// velocity, and PlayerMotor resolves WASD against the fixed camera basis rather
    /// than against the character's facing. Movement and aim are therefore genuinely
    /// orthogonal: running north-east while shooting south-west costs nothing and
    /// changes neither speed nor accuracy.
    ///
    /// POINTER AIM (REQ-038)
    /// ---------------------
    /// The cursor is projected onto a horizontal plane at
    /// PrototypeConfig.Player.AimPlaneHeight above the character's feet, i.e. roughly
    /// chest height, so the cursor sits where the bullets go rather than where the
    /// ground is. The projection uses the camera rig's SHAKE-FREE pose, which is what
    /// makes "camera shake must not disturb aim" true by construction rather than by
    /// keeping the shake small.
    ///
    /// STICK AIM
    /// ---------
    /// The right stick is a direction, not a point, so it is resolved against the same
    /// fixed camera basis WASD uses. Written but NOT verified: there is no gamepad on
    /// the build machine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAim : PrototypeConfigBehaviour, ICameraLookAheadProvider
    {
        [PrototypeOnly(PendingDecisions.None,
            "How far ahead the stick aim point is placed. Only affects where effects converge, not accuracy.", true)]
        [SerializeField] private float _stickAimDistance = 12f;

        [PrototypeOnly(PendingDecisions.None,
            "Minimum cursor distance from the character before the aim direction updates. " +
            "Stops the character spinning when the cursor sits on top of it.", true)]
        [SerializeField] private float _minAimDistance = 0.35f;

        private float _rotationSpeed;
        private float _aimPlaneHeight;

        private IInputSource _inputOverride;
        private Vector3 _aimDirection = Vector3.forward;
        private Vector3 _aimWorldPoint;
        private bool _hasAimPoint;
        private AimSourceKind _aimKind = AimSourceKind.Pointer;

        /// <summary>False freezes facing without touching movement. Used on death.</summary>
        public bool ControlEnabled { get; set; } = true;

        /// <summary>Normalised horizontal direction the character is aiming at.</summary>
        public Vector3 AimDirection
        {
            get { return _aimDirection; }
        }

        /// <summary>
        /// World point the player is aiming at, on the aim plane. Weapons should fire
        /// towards this rather than towards transform.forward, so a shot fired during
        /// the turn still goes where the cursor is.
        /// </summary>
        public Vector3 AimWorldPoint
        {
            get { return _aimWorldPoint; }
        }

        /// <summary>False when the cursor could not be projected, e.g. no camera yet.</summary>
        public bool HasAimPoint
        {
            get { return _hasAimPoint; }
        }

        /// <summary>Which device is currently driving aim.</summary>
        public AimSourceKind AimKind
        {
            get { return _aimKind; }
        }

        /// <summary>Height of the aim plane above the character's feet.</summary>
        public float AimPlaneHeight
        {
            get { return _aimPlaneHeight; }
        }

        /// <summary>World-space height of the aim plane.</summary>
        public float AimPlaneY
        {
            get { return transform.position.y + _aimPlaneHeight; }
        }

        /// <summary>Angle in degrees between the current facing and the aim direction.</summary>
        public float FacingErrorDegrees
        {
            get { return Vector3.Angle(transform.forward, _aimDirection); }
        }

        public void SetInputSource(IInputSource source)
        {
            _inputOverride = source;
        }

        private IInputSource ActiveInput
        {
            get { return _inputOverride ?? InputSourceRegistry.Current; }
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            PlayerConfigSection player = config.Player;
            _rotationSpeed = Mathf.Max(0f, player.RotationSpeed);
            _aimPlaneHeight = Mathf.Max(0f, player.AimPlaneHeight);
        }

        private void Start()
        {
            _aimDirection = transform.forward;
            _aimWorldPoint = transform.position + _aimDirection * _stickAimDistance;
        }

        private void Update()
        {
            if (!HasConfig)
            {
                return;
            }

            IInputSource input = ActiveInput;
            if (input != null)
            {
                _aimKind = input.AimKind;

                if (ControlEnabled)
                {
                    if (_aimKind == AimSourceKind.Stick)
                    {
                        UpdateStickAim(input);
                    }
                    else
                    {
                        UpdatePointerAim(input);
                    }
                }
            }

            RotateTowardsAim();
        }

        private void UpdatePointerAim(IInputSource input)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig == null)
            {
                _hasAimPoint = false;
                return;
            }

            Vector3 point;
            if (!rig.ScreenPointToAimPlane(input.AimScreenPosition, AimPlaneY, out point))
            {
                _hasAimPoint = false;
                return;
            }

            _aimWorldPoint = point;
            _hasAimPoint = true;

            Vector3 delta = point - transform.position;
            delta.y = 0f;

            // Below the threshold the cursor is effectively on top of the character;
            // keeping the previous direction is far better than spinning.
            if (delta.sqrMagnitude < _minAimDistance * _minAimDistance)
            {
                return;
            }

            _aimDirection = delta.normalized;
        }

        private void UpdateStickAim(IInputSource input)
        {
            Vector2 stick = input.AimStick;
            if (stick.sqrMagnitude < 1e-4f)
            {
                // Stick released: hold the last direction rather than snapping to a default.
                _aimWorldPoint = transform.position + _aimDirection * _stickAimDistance;
                _hasAimPoint = true;
                return;
            }

            Vector3 forward;
            Vector3 right;
            GetCameraBasis(out forward, out right);

            Vector3 direction = right * stick.x + forward * stick.y;
            direction.y = 0f;

            if (direction.sqrMagnitude > 1e-6f)
            {
                _aimDirection = direction.normalized;
            }

            _aimWorldPoint = transform.position + Vector3.up * _aimPlaneHeight
                             + _aimDirection * _stickAimDistance;
            _hasAimPoint = true;
        }

        /// <summary>
        /// REQ-038. Turns towards the aim direction at RotationSpeed degrees per second.
        /// RotateTowards is an exact angular step, so the turn takes the same wall-clock
        /// time at any frame rate. Scaled delta time, so pause freezes the turn too.
        /// </summary>
        private void RotateTowardsAim()
        {
            if (_aimDirection.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(_aimDirection, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, _rotationSpeed * dt);
        }

        private static void GetCameraBasis(out Vector3 forward, out Vector3 right)
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

        // ------------------------------------------------- ICameraLookAheadProvider

        /// <summary>
        /// REQ-033. Frames slightly towards where the player is aiming.
        ///
        /// Pointer: a fraction of the cursor's own offset, so the frame leads the aim
        /// but never chases the cursor to the screen edge.
        /// Stick: the stick direction scaled by how far it is pushed, so a light push
        /// barely shifts the frame.
        ///
        /// The rig clamps whatever comes back to PrototypeConfig.Camera.MaxAimLookAhead
        /// (2.5 m), so this method cannot push the player off screen.
        /// </summary>
        public bool TryGetLookAheadOffset(Vector3 anchor, float pointerFactor, float maxLookAhead, out Vector3 offset)
        {
            offset = Vector3.zero;

            if (_aimKind == AimSourceKind.Stick)
            {
                IInputSource input = ActiveInput;
                float push = input != null ? Mathf.Clamp01(input.AimStick.magnitude) : 0f;
                offset = _aimDirection * (push * maxLookAhead);
                return push > 0f;
            }

            if (!_hasAimPoint)
            {
                return false;
            }

            Vector3 delta = _aimWorldPoint - anchor;
            delta.y = 0f;
            offset = delta * pointerFactor;
            return true;
        }
    }
}
