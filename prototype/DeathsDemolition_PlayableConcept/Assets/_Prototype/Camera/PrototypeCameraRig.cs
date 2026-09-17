using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// Distant, elevated, FIXED ANGLE third person camera. REQ-030 to REQ-035.
    ///
    /// SHAPE OF THE RIG
    /// ----------------
    ///     [Camera Rig]        <- this component. Holds the STABLE pose: follow
    ///                            position plus look-ahead, and a rotation that is
    ///                            computed once from the height/back offset and then
    ///                            never touched again.
    ///       Camera            <- the actual Camera. Its localPosition / localRotation
    ///                            carry the shake and nothing else.
    ///
    /// Because shake lives one level below the stable pose, and because every aim query
    /// goes through <see cref="ScreenPointToAimPlane"/> which rebuilds the ray from the
    /// stable pose, shake can never move the world point under the cursor. That is the
    /// mechanism behind "camera shake must not disturb aim".
    ///
    /// WHAT THIS RIG DELIBERATELY CANNOT DO
    ///   * rotate on player input      (REQ-031) - there is no input path into rotation
    ///   * change field of view        (REQ-034) - FOV is written only in ApplyConfig
    ///   * snap or cut to the player   - the focus point is always smooth-damped
    ///
    /// The yaw is authorable so the world designer can frame the courtyard, but it is a
    /// scene-authoring value, never a runtime one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeCameraRig : PrototypeConfigBehaviour
    {
        /// <summary>The rig every other system reads. Null before a run starts.</summary>
        public static PrototypeCameraRig Active { get; private set; }

        [Header("Targets")]
        [Tooltip("What the camera follows. Usually the player rig's camera follow target.")]
        [SerializeField] private Transform _followTarget;

        [Tooltip("Supplies the aim look-ahead direction. Optional; without it the framing is centred.")]
        [SerializeField] private MonoBehaviour _lookAheadProviderBehaviour;

        [Header("Camera")]
        [SerializeField] private Camera _camera;

        [Tooltip("Child transform that carries the shake offset. Must be a child of this rig.")]
        [SerializeField] private Transform _shakeNode;

        [Header("Fixed framing")]
        [PrototypeOnly(PendingDecisions.None,
            "Compass yaw of the fixed camera. Scene authoring value; the player can never change it.", true)]
        [SerializeField] private float _yawDegrees = 0f;

        [PrototypeOnly(PendingDecisions.None,
            "Height above the follow target that the camera actually aims at, so the player sits slightly low in frame.",
            true)]
        [SerializeField] private float _focusHeightOffset = 1f;

        [PrototypeOnly(PendingDecisions.None, "Near clip plane.", true)]
        [SerializeField] private float _nearClip = 0.3f;

        [PrototypeOnly(PendingDecisions.None, "Far clip plane. 90x90 m map plus fog margin.", true)]
        [SerializeField] private float _farClip = 260f;

        [Header("Look-ahead")]
        [PrototypeOnly(PendingDecisions.None,
            "Fraction of the cursor's distance from the player used as look-ahead, before the config clamp. " +
            "Below 1 so the frame leads the aim without chasing the cursor.", true)]
        [SerializeField] private float _pointerLookAheadFactor = 0.35f;

        // ------------------------------------------------------------ cached config

        private float _height = 15f;
        private float _backDistance = 13f;
        private float _fieldOfView = 42f;
        private float _followSmoothTime = 0.12f;
        private float _maxLookAhead = 2.5f;
        private float _lookAheadSmoothTime = 0.18f;
        private float _shakeIntensity = 1f;
        private float _maxShakeAmplitude = 0.22f;

        // ------------------------------------------------------------ derived state

        private Vector3 _offsetFromFocus = new Vector3(0f, 15f, -13f);
        private Quaternion _fixedRotation = Quaternion.identity;
        private Vector3 _planarForward = Vector3.forward;
        private Vector3 _planarRight = Vector3.right;

        private Vector3 _focus;
        private Vector3 _focusVelocity;
        private Vector3 _lookAhead;
        private Vector3 _lookAheadVelocity;
        private bool _warmedUp;

        private ICameraLookAheadProvider _lookAheadProvider;

        private DebugPanelSlider _shakeSlider;
        private DebugPanelButton _testPunch;
        private DebugPanelButton _testRumble;
        private DebugPanelReadout _readout;

        // ---------------------------------------------------------------- accessors

        /// <summary>The camera component. Never move it directly; call the shake service.</summary>
        public Camera Camera
        {
            get { return _camera; }
        }

        /// <summary>Follow target, assignable at runtime when the player spawns.</summary>
        public Transform FollowTarget
        {
            get { return _followTarget; }
            set
            {
                _followTarget = value;
                _warmedUp = false;
            }
        }

        /// <summary>
        /// Camera forward flattened onto the ground plane. This is the basis WASD is
        /// resolved against (REQ-036). Constant while the rig lives.
        /// </summary>
        public Vector3 PlanarForward
        {
            get { return _planarForward; }
        }

        /// <summary>Camera right flattened onto the ground plane.</summary>
        public Vector3 PlanarRight
        {
            get { return _planarRight; }
        }

        /// <summary>Shake-free world position of the camera.</summary>
        public Vector3 StablePosition
        {
            get { return transform.position; }
        }

        /// <summary>Shake-free world rotation of the camera.</summary>
        public Quaternion StableRotation
        {
            get { return transform.rotation; }
        }

        /// <summary>Assign whatever supplies the aim look-ahead direction, normally PlayerAim.</summary>
        public void SetLookAheadProvider(ICameraLookAheadProvider provider)
        {
            _lookAheadProvider = provider;
            _lookAheadProviderBehaviour = provider as MonoBehaviour;
        }

        /// <summary>
        /// Pause-menu hook. Writes the shake intensity into the live config so every
        /// rig, current and future, picks it up. 0 disables shake entirely, 1 is normal.
        /// </summary>
        public static void SetShakeIntensity(float value)
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null)
            {
                return;
            }

            config.Camera.ShakeIntensity = Mathf.Clamp(value, 0f, 2f);
            config.NotifyChanged();
        }

        /// <summary>Current shake intensity from the live config, 1 when there is none.</summary>
        public static float GetShakeIntensity()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            return config != null ? config.Camera.ShakeIntensity : 1f;
        }

        // ------------------------------------------------------------------ lifetime

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = GetComponentInChildren<Camera>();
            }

            if (_shakeNode == null && _camera != null)
            {
                _shakeNode = _camera.transform;
            }

            if (_lookAheadProvider == null && _lookAheadProviderBehaviour != null)
            {
                _lookAheadProvider = _lookAheadProviderBehaviour as ICameraLookAheadProvider;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            Active = this;
            DeathSequence.ReleasePushIn();   // WP-059: a new run's rig starts at the full offset
            CameraShakeService.Register(this);

            _shakeSlider = DebugPanelRegistry.AddSlider(
                DebugPanelTabs.Misc,
                "Camera Shake Intensity",
                GetShakeIntensity,
                SetShakeIntensity,
                0f, 2f, false, -50);

            _testPunch = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Misc,
                "Test Shake (Punch)",
                () => CameraShakeService.Shake(0.15f, 0.2f, ShakeKind.Punch, transform.forward),
                -49);

            _testRumble = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Misc,
                "Test Shake (Rumble)",
                () => CameraShakeService.Shake(0.2f, 0.45f, ShakeKind.Rumble),
                -48);

            _readout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Camera", Describe, -47);
        }

        protected override void OnDisable()
        {
            DebugPanelRegistry.Remove(_shakeSlider);
            DebugPanelRegistry.Remove(_testPunch);
            DebugPanelRegistry.Remove(_testRumble);
            DebugPanelRegistry.Remove(_readout);
            _shakeSlider = null;
            _testPunch = null;
            _testRumble = null;
            _readout = null;

            CameraShakeService.Unregister(this);

            if (Active == this)
            {
                Active = null;
            }

            base.OnDisable();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
        }

        // -------------------------------------------------------------------- config

        protected override void ApplyConfig(PrototypeConfig config)
        {
            CameraConfigSection cam = config.Camera;
            _height = cam.Height;
            _backDistance = cam.BackDistance;
            _fieldOfView = cam.FieldOfView;
            _followSmoothTime = Mathf.Max(0f, cam.FollowSmoothTime);
            _maxLookAhead = Mathf.Max(0f, cam.MaxAimLookAhead);
            _lookAheadSmoothTime = Mathf.Max(0f, cam.LookAheadSmoothTime);
            _shakeIntensity = Mathf.Max(0f, cam.ShakeIntensity);
            _maxShakeAmplitude = Mathf.Max(0f, config.Feedback.MaxShakeAmplitude);

            // Derived framing. Recomputed on every live edit, which is what makes the
            // F1 panel able to reframe the camera mid-run.
            Quaternion yaw = Quaternion.Euler(0f, _yawDegrees, 0f);
            _offsetFromFocus = yaw * new Vector3(0f, _height, -_backDistance);

            Vector3 toFocus = -_offsetFromFocus;
            if (toFocus.sqrMagnitude < 1e-6f)
            {
                toFocus = Vector3.forward;
            }

            _fixedRotation = Quaternion.LookRotation(toFocus.normalized, Vector3.up);

            Vector3 forward = _fixedRotation * Vector3.forward;
            forward.y = 0f;
            _planarForward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            _planarRight = Vector3.Cross(Vector3.up, _planarForward).normalized;

            if (_camera != null)
            {
                _camera.fieldOfView = _fieldOfView;
                _camera.nearClipPlane = _nearClip;
                _camera.farClipPlane = _farClip;
                _camera.orthographic = false;
            }

            transform.rotation = _fixedRotation;
        }

        // -------------------------------------------------------------------- update

        private void LateUpdate()
        {
            if (!HasConfig)
            {
                return;
            }

            float dt = Time.deltaTime;

            if (_followTarget != null)
            {
                Vector3 anchor = _followTarget.position + Vector3.up * _focusHeightOffset;
                Vector3 desiredLookAhead = ComputeLookAhead(anchor);

                if (!_warmedUp)
                {
                    _focus = anchor;
                    _lookAhead = desiredLookAhead;
                    _focusVelocity = Vector3.zero;
                    _lookAheadVelocity = Vector3.zero;
                    _warmedUp = true;
                }
                else
                {
                    _lookAhead = Vector3.SmoothDamp(
                        _lookAhead, desiredLookAhead, ref _lookAheadVelocity, _lookAheadSmoothTime, Mathf.Infinity, dt);

                    _focus = Vector3.SmoothDamp(
                        _focus, anchor, ref _focusVelocity, _followSmoothTime, Mathf.Infinity, dt);
                }
            }

            // Stable pose. Rotation is written once in ApplyConfig and re-asserted here
            // so nothing external can rotate the rig without being immediately undone.
            // WP-059: the death beat pulls the camera a little towards the fallen body
            // (GameFeelSettings.DeathCameraPushIn of the offset, eased with the slow-down).
            float pushIn = DeathSequence.PushIn01 * GameFeelSettings.Active.DeathCameraPushIn;
            transform.SetPositionAndRotation(_focus + _lookAhead + _offsetFromFocus * (1f - pushIn), _fixedRotation);

            ApplyShake(dt);
        }

        private Vector3 ComputeLookAhead(Vector3 anchor)
        {
            if (_lookAheadProvider == null || _maxLookAhead <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 offset;
            if (!_lookAheadProvider.TryGetLookAheadOffset(
                    anchor, _pointerLookAheadFactor, _maxLookAhead, out offset))
            {
                return Vector3.zero;
            }

            offset.y = 0f;
            return Vector3.ClampMagnitude(offset, _maxLookAhead);
        }

        private void ApplyShake(float dt)
        {
            if (_shakeNode == null)
            {
                return;
            }

            Vector3 localOffset;
            float roll;
            CameraShakeService.Sample(
                dt, _maxShakeAmplitude, _shakeIntensity, _fixedRotation, out localOffset, out roll);

            _shakeNode.localPosition = localOffset;
            _shakeNode.localRotation = Quaternion.Euler(0f, 0f, roll);
        }

        // ----------------------------------------------------------------- aim query

        /// <summary>
        /// Projects a screen point onto the horizontal plane at <paramref name="planeY"/>
        /// using the rig's SHAKE-FREE pose.
        ///
        /// The ray is taken from the real camera (so lens, viewport and any render
        /// scaling stay correct), converted into camera-local space and then rebuilt
        /// against the stable pose. That removes the shake offset and the shake roll
        /// exactly, without the caller having to know shake exists.
        /// </summary>
        public bool ScreenPointToAimPlane(Vector2 screenPoint, float planeY, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;

            if (_camera == null)
            {
                return false;
            }

            Ray ray = _camera.ScreenPointToRay(screenPoint);
            Transform camTransform = _camera.transform;

            Vector3 localOrigin = camTransform.InverseTransformPoint(ray.origin);
            Vector3 localDirection = camTransform.InverseTransformDirection(ray.direction);

            Vector3 origin = transform.position + transform.rotation * localOrigin;
            Vector3 direction = transform.rotation * localDirection;

            if (Mathf.Abs(direction.y) < 1e-5f)
            {
                return false;
            }

            float distance = (planeY - origin.y) / direction.y;
            if (distance <= 0f)
            {
                return false;
            }

            worldPoint = origin + direction * distance;
            return true;
        }

        private string Describe()
        {
            Vector3 p = transform.position;
            return string.Format(
                "pos ({0:0.0}, {1:0.0}, {2:0.0})  fov {3:0.0}  lookahead {4:0.00} m  shakes {5}",
                p.x, p.y, p.z, _camera != null ? _camera.fieldOfView : 0f, _lookAhead.magnitude,
                CameraShakeService.ActiveCount);
        }
    }

    /// <summary>
    /// Implemented by whatever decides where the frame should lead. PlayerAim
    /// implements it; nothing else needs to.
    /// </summary>
    public interface ICameraLookAheadProvider
    {
        /// <summary>
        /// Returns the desired world-space framing offset from <paramref name="anchor"/>.
        /// The rig clamps the result to <paramref name="maxLookAhead"/> anyway, so
        /// returning something large is safe. Return false to centre the frame.
        /// </summary>
        bool TryGetLookAheadOffset(Vector3 anchor, float pointerFactor, float maxLookAhead, out Vector3 offset);
    }
}
