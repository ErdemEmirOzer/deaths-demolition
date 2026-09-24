using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using DD.Prototype.Characters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
    ///
    /// WP-070 (M4-R1 N2) — CAMERA MODES
    /// --------------------------------
    /// Everything above describes <see cref="CameraMode.TopDown"/>, which is unchanged. In
    /// <see cref="CameraMode.ThirdPerson"/> / <see cref="CameraMode.FirstPerson"/> the rig reads
    /// look input (mouse delta / right stick) into a yaw/pitch view state; the stable pose is
    /// an over-the-shoulder boom (sphere-cast pull-in) or the eye point. The same two
    /// invariants hold in every mode: shake/kick/bob live on the child node only, and every
    /// aim query rebuilds its ray from the stable pose (<see cref="TryGetAimRay"/>).
    /// <see cref="PlanarForward"/>/<see cref="PlanarRight"/> follow the view yaw in TP/FP, so
    /// PlayerMotor and stick aim become camera-relative without any change on their side.
    /// Mode changes blend the pose over <see cref="CameraModeService.TransitionSeconds"/> of
    /// UNSCALED time (the blend is visible behind the pause menu). Direction document:
    /// planning/work-packages/WP-070-CAMERA-MODES-DIRECTION.md.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
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

        [Header("WP-070 · Third person (defaults; S-07 CameraModesFeel replaces the feel values)")]
        [PrototypeOnly(PendingDecisions.None, "TP pivot height above the feet (chest).", true)]
        [SerializeField] private float _tpPivotHeight = 1.45f;
        [PrototypeOnly(PendingDecisions.None, "TP camera height above the pivot.", true)]
        [SerializeField] private float _tpHeightAbovePivot = 0.35f;
        [PrototypeOnly(PendingDecisions.None, "TP shoulder offset (signed by CameraModeSettings.Shoulder).", true)]
        [SerializeField] private float _tpShoulderOffset = 0.55f;
        [PrototypeOnly(PendingDecisions.None, "TP boom length behind the pivot.", true)]
        [SerializeField] private float _tpDistance = 2.8f;
        [PrototypeOnly(PendingDecisions.None, "TP collision probe radius.", true)]
        [SerializeField] private float _tpCollisionRadius = 0.25f;
        [PrototypeOnly(PendingDecisions.None, "TP minimum boom length when pushed in by a wall.", true)]
        [SerializeField] private float _tpMinDistance = 0.6f;
        [PrototypeOnly(PendingDecisions.None, "Seconds the boom takes to open again after a wall (pull-in is instant).", true)]
        [SerializeField] private float _tpReturnSeconds = 0.25f;
        [PrototypeOnly(PendingDecisions.None, "TP position follow smoothing (rotation is 1:1).", true)]
        [SerializeField] private float _tpFollowSmoothSeconds = 0.06f;
        [PrototypeOnly(PendingDecisions.None, "Below this boom length the player is hidden (shadow kept).", true)]
        [SerializeField] private float _tpHidePlayerDistance = 0.75f;
        [SerializeField] private float _tpPitchMin = -55f;
        [SerializeField] private float _tpPitchMax = 70f;
        [SerializeField] private float _tpStartPitch = 12f;

        [Header("WP-070 · First person")]
        [PrototypeOnly(PendingDecisions.None, "Eye height above the feet when no head bone is bound (AP-006 ronin, S-06 verifies).", true)]
        [SerializeField] private float _fpEyeHeight = 1.62f;
        [PrototypeOnly(PendingDecisions.None, "Eye point forward of the head centre (S-06: ~0.10 m + near 0.05-0.08 hides the head interior until the head submesh split).", true)]
        [SerializeField] private float _fpEyeForward = 0.12f;
        [PrototypeOnly(PendingDecisions.None, "FP wall probe: below this the weapon lowers (S-06 PlayLower).", true)]
        [SerializeField] private float _fpWallLowerDistance = 0.6f;
        [PrototypeOnly(PendingDecisions.None, "FP wall probe: above this the weapon raises again (hysteresis, S-06 PlayRaise).", true)]
        [SerializeField] private float _fpWallRaiseDistance = 0.8f;
        [SerializeField] private float _fpWallProbeRadius = 0.15f;
        [SerializeField] private float _fpPitchMin = -80f;
        [SerializeField] private float _fpPitchMax = 80f;

        [Header("WP-070 · Look + feel (defaults until S-07 CameraModesFeel)")]
        [SerializeField] private float _stickPitchShare = 0.7f;
        [SerializeField] private float _stickResponseExponent = 1.6f;
        [SerializeField] private float _recoilReturnDegreesPerSecond = 8f;
        [SerializeField] private float _kickReturnRate = 14f;
        [SerializeField] private float _bobFrequencyHz = 1.9f;
        [SerializeField] private float _bobVertical = 0.018f;
        [SerializeField] private float _bobLateral = 0.010f;

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

        // ------------------------------------------------------------ WP-070 mode state

        private float _viewYaw;
        private float _viewPitch;
        private Vector2 _recoil;                 // accumulated view recoil (pitch up, yaw right), part of the view
        private Vector3 _kickOffset;
        private float _kickRoll;
        private float _bobPhase;
        private float _fovPunch;
        private float _fovPunchDecayPerSecond;
        private float _shoulderBlend = 1f;       // -1 left .. +1 right
        private float _tpBoom = -1f;             // current boom length, <0 = uninitialised
        private float _tpBoomVelocity;
        private Vector3 _tpPivot;
        private Vector3 _tpPivotVelocity;
        private bool _tpPivotWarm;
        private Vector3 _fromPosition;
        private Quaternion _fromRotation = Quaternion.identity;
        private float _fromFov = 42f;
        private bool _cursorLockedByRig;
        private Vector3 _lastFeet;
        [SerializeField] private float _bobReferenceSpeed = 5.5f;
        private bool _playerHidden;
        private readonly List<Renderer> _playerRenderers = new List<Renderer>(32);
        private readonly Dictionary<GameObject, int> _viewmodelLayers = new Dictionary<GameObject, int>(16);
        private Camera _viewmodelCamera;
        private Transform _cachedBody;
        private PlayerSkinnedVisual _visual;
        private float _fpEyeHeightLive = -1f;
        private bool _weaponLowered;
        private readonly Dictionary<Renderer, ShadowCastingMode> _playerShadowModes = new Dictionary<Renderer, ShadowCastingMode>(32);

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
                _lastFeet = value != null ? value.position : Vector3.zero;
                if (CameraModeService.IsLookMode)
                {
                    EnterMode(CameraModeService.Current, instant: true);   // WP-070: look where the new body faces
                }
            }
        }

        /// <summary>
        /// Camera forward flattened onto the ground plane. This is the basis WASD is
        /// resolved against (REQ-036). Constant while the rig lives.
        /// </summary>
        public Vector3 PlanarForward
        {
            get { return CameraModeService.IsLookMode ? Quaternion.Euler(0f, _viewYaw, 0f) * Vector3.forward : _planarForward; }
        }

        /// <summary>Camera right flattened onto the ground plane.</summary>
        public Vector3 PlanarRight
        {
            get { return CameraModeService.IsLookMode ? Quaternion.Euler(0f, _viewYaw, 0f) * Vector3.right : _planarRight; }
        }

        /// <summary>WP-070: TP/FP view yaw in degrees (compass, world Y).</summary>
        public float ViewYaw
        {
            get { return _viewYaw; }
        }

        /// <summary>WP-070: TP/FP view pitch in degrees (positive looks down), recoil included.</summary>
        public float ViewPitch
        {
            get { return _viewPitch - _recoil.x; }
        }

        /// <summary>WP-070 test/QA hook: sets the TP/FP view angles directly (bot look target, tests).</summary>
        public void SetViewAngles(float yawDegrees, float pitchDegrees)
        {
            _viewYaw = Mathf.Repeat(yawDegrees, 360f);
            ClampPitch(pitchDegrees);
        }

        /// <summary>WP-070: current third-person boom length (distance pivot → camera); -1 outside TP.</summary>
        public float ThirdPersonBoom
        {
            get { return CameraModeService.Current == CameraMode.ThirdPerson ? _tpBoom : -1f; }
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
            FeelRuntime.Changed += ApplyFeel;
            ApplyFeel(FeelRuntime.Current);

            Active = this;
            DeathSequence.ReleasePushIn();   // WP-059: a new run's rig starts at the full offset
            CameraShakeService.Register(this);

            // WP-070: a run starts in the saved / command-line mode, snapped.
            CameraModeService.Changed += OnModeChanged;
            CameraModeService.SettingsChanged += OnModeSettingsChanged;
            CameraModeService.Request(CameraModeService.ResolveStartMode(), instant: true);
            EnterMode(CameraModeService.Current, instant: true);

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
            FeelRuntime.Changed -= ApplyFeel;
            DebugPanelRegistry.Remove(_shakeSlider);
            DebugPanelRegistry.Remove(_testPunch);
            DebugPanelRegistry.Remove(_testRumble);
            DebugPanelRegistry.Remove(_readout);
            _shakeSlider = null;
            _testPunch = null;
            _testRumble = null;
            _readout = null;

            CameraShakeService.Unregister(this);

            CameraModeService.Changed -= OnModeChanged;
            CameraModeService.SettingsChanged -= OnModeSettingsChanged;
            ReleaseCursor();
            ShowPlayer();
            RaiseWeaponIfLowered();
            if (_viewmodelCamera != null)
            {
                _viewmodelCamera.enabled = false;
            }

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
            _shakeIntensity = Mathf.Max(0f, config.Camera.ShakeIntensity);
            ApplyFeel(FeelRuntime.Current);
        }

        private void ApplyFeel(FeelSnapshot feel)
        {
            CameraFeel cam = feel.Camera;
            _height = cam.heightMeters;
            _backDistance = cam.backDistanceMeters;
            _fieldOfView = cam.fieldOfViewDegrees;
            _yawDegrees = cam.yawDegrees;
            _focusHeightOffset = cam.focusHeightOffsetMeters;
            _pointerLookAheadFactor = cam.pointerLookAheadFactor;
            _followSmoothTime = cam.followSmoothSeconds;
            _maxLookAhead = cam.maxLookAheadMeters;
            _lookAheadSmoothTime = cam.lookAheadSmoothSeconds;
            _maxShakeAmplitude = feel.Shake.nominalAggregateCapMeters;

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

            if (CameraModeService.Current != CameraMode.TopDown)
            {
                return;   // WP-070: TP/FP own lens and rotation every LateUpdate
            }

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
            CameraModeService.SetFrozen(RunFlowClock.IsFrozen);
            CameraMode mode = CameraModeService.Current;
            ConsumeReactions(mode, dt);   // before the pose: recoil is part of this frame's view

            Vector3 position;
            Quaternion rotation;
            float fov;
            if (mode == CameraMode.TopDown)
            {
                ComputeTopDownPose(dt, out position, out rotation, out fov);
            }
            else
            {
                UpdateLook(mode, dt);
                if (mode == CameraMode.ThirdPerson)
                {
                    ComputeThirdPersonPose(dt, out position, out rotation, out fov);
                }
                else
                {
                    ComputeFirstPersonPose(out position, out rotation, out fov);
                }
            }

            // WP-070: blend from the pose the rig had when the mode changed (unscaled time,
            // so the blend plays behind the pause menu).
            if (CameraModeService.IsTransitioning)
            {
                float seconds = CameraModeService.TransitionSeconds(CameraModeService.Previous, mode);
                CameraModeService.AdvanceTransition(seconds > 0f ? Time.unscaledDeltaTime / seconds : 1f);
                float t = CameraModeService.Transition01;
                t = t * t * (3f - 2f * t);
                position = Vector3.Lerp(_fromPosition, position, t);
                rotation = Quaternion.Slerp(_fromRotation, rotation, t);
                fov = Mathf.Lerp(_fromFov, fov, t);
            }

            transform.SetPositionAndRotation(position, rotation);
            ApplyLens(mode, fov, dt);
            ApplyViewmodelCamera(mode);
            ApplyShake(dt, mode);
            ApplyCursor();
            ApplyPlayerVisibility(mode);
        }

        private void ComputeTopDownPose(float dt, out Vector3 position, out Quaternion rotation, out float fov)
        {
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
            position = _focus + _lookAhead + _offsetFromFocus * (1f - pushIn);
            rotation = _fixedRotation;
            fov = _fieldOfView;
        }

        // ------------------------------------------------------------ WP-070 modes

        private Vector3 Feet
        {
            get { return _followTarget != null ? _followTarget.position : transform.position; }
        }

        private void OnModeChanged(CameraModeChange change)
        {
            EnterMode(change.To, change.Instant);
        }

        private void OnModeSettingsChanged()
        {
            // Nothing cached: FOV / shoulder / sensitivity are read live every frame.
        }

        /// <summary>Snapshots the blend origin and seeds the view state for the new mode.</summary>
        private void EnterMode(CameraMode mode, bool instant)
        {
            if (mode != CameraMode.FirstPerson)
            {
                RaiseWeaponIfLowered();
            }
            else
            {
                CalibrateEyeHeight();
            }

            _fromPosition = transform.position;
            _fromRotation = transform.rotation;
            _fromFov = _camera != null ? _camera.fieldOfView : _fieldOfView;
            _recoil = Vector2.zero;
            _fovPunch = 0f;

            if (mode == CameraMode.TopDown)
            {
                _warmedUp = false;   // focus snaps to the player; the blend hides the jump
                transform.rotation = instant ? _fixedRotation : transform.rotation;
                return;
            }

            if (CameraModeService.Previous == CameraMode.TopDown || instant)
            {
                // Entering a look mode from top-down: look where the character faces.
                Transform body = _followTarget != null && _followTarget.parent != null ? _followTarget.parent : _followTarget;
                Vector3 facing = body != null ? body.forward : transform.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude < 1e-6f)
                {
                    facing = _planarForward;
                }

                _viewYaw = Quaternion.LookRotation(facing.normalized, Vector3.up).eulerAngles.y;
                _viewPitch = mode == CameraMode.ThirdPerson ? _tpStartPitch : 0f;
            }

            ClampPitch(_viewPitch);
            _tpBoom = -1f;
            _tpPivotWarm = false;
            _shoulderBlend = CameraModeService.Settings.Shoulder == CameraShoulder.Left ? -1f : 1f;
        }

        private void ClampPitch(float pitch)
        {
            CameraMode mode = CameraModeService.Current;
            float min = mode == CameraMode.FirstPerson ? _fpPitchMin : _tpPitchMin;
            float max = mode == CameraMode.FirstPerson ? _fpPitchMax : _tpPitchMax;
            _viewPitch = Mathf.Clamp(pitch, min, max);
        }

        /// <summary>
        /// Mouse delta and right stick → yaw/pitch. Frozen clock (pause) and look blocks stop
        /// look input; the transition itself keeps playing. Stick uses scaled time so a
        /// hit-stop does not fling the view.
        /// </summary>
        private void UpdateLook(CameraMode mode, float dt)
        {
            if (RunFlowClock.IsFrozen || CameraModeService.IsLookBlocked)
            {
                return;
            }

            IInputSource input = InputSourceRegistry.Current;
            if (input == null || !input.IsEnabled)
            {
                return;
            }

            CameraModeSettings settings = CameraModeService.Settings;
            float invert = settings.InvertY ? -1f : 1f;

            // WP-070 B2: shoulder swap (TP only). A settings change, so the persistence layer
            // saves it like any other camera preference.
            if (mode == CameraMode.ThirdPerson && input.ReadShoulderSwapPressed())
            {
                CameraModeSettings next = settings.Clone();
                next.Shoulder = settings.Shoulder == CameraShoulder.Right ? CameraShoulder.Left : CameraShoulder.Right;
                CameraModeService.ApplySettings(next, applyMode: false);
                settings = CameraModeService.Settings;
            }

            Vector2 mouse = input.ReadLookDelta();
            float yaw = mouse.x * settings.LookSensitivity;
            float pitch = -mouse.y * settings.LookSensitivity * invert;

            Vector2 stick = input.ReadLookStick();
            float magnitude = stick.magnitude;
            if (magnitude > 1e-4f && dt > 0f)
            {
                float response = Mathf.Pow(Mathf.Clamp01(magnitude), _stickResponseExponent) / magnitude;
                yaw += stick.x * response * settings.StickLookDegreesPerSecond * dt;
                pitch -= stick.y * response * settings.StickLookDegreesPerSecond * _stickPitchShare * dt * invert;
            }

            _viewYaw = Mathf.Repeat(_viewYaw + yaw, 360f);
            ClampPitch(_viewPitch + pitch);
        }

        private Quaternion ViewRotation()
        {
            // Recoil is part of the view (direction doc D-20): the aim ray follows it.
            return Quaternion.Euler(_viewPitch - _recoil.x, _viewYaw + _recoil.y, 0f);
        }

        private void ComputeThirdPersonPose(float dt, out Vector3 position, out Quaternion rotation, out float fov)
        {
            rotation = ViewRotation();
            Vector3 pivotTarget = Feet + Vector3.up * _tpPivotHeight;
            if (!_tpPivotWarm || dt <= 0f)
            {
                _tpPivot = pivotTarget;
                _tpPivotVelocity = Vector3.zero;
                _tpPivotWarm = true;
            }
            else
            {
                _tpPivot = Vector3.SmoothDamp(_tpPivot, pivotTarget, ref _tpPivotVelocity, _tpFollowSmoothSeconds, Mathf.Infinity, dt);
            }

            float shoulderTarget = CameraModeService.Settings.Shoulder == CameraShoulder.Left ? -1f : 1f;
            _shoulderBlend = Mathf.MoveTowards(_shoulderBlend, shoulderTarget, dt * 4f);

            Vector3 local = new Vector3(_tpShoulderOffset * _shoulderBlend, _tpHeightAbovePivot, -_tpDistance);
            Vector3 desired = _tpPivot + rotation * local;
            Vector3 toCamera = desired - _tpPivot;
            float length = toCamera.magnitude;
            Vector3 direction = length > 1e-5f ? toCamera / length : -(rotation * Vector3.forward);

            float allowed = length;
            RaycastHit hit;
            if (Physics.SphereCast(_tpPivot, _tpCollisionRadius, direction, out hit, length,
                    PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Max(_tpMinDistance, hit.distance);
            }

            if (_tpBoom < 0f || allowed < _tpBoom || dt <= 0f)
            {
                _tpBoom = allowed;          // pull-in is instant: never a frame inside the wall
                _tpBoomVelocity = 0f;
            }
            else
            {
                _tpBoom = Mathf.SmoothDamp(_tpBoom, allowed, ref _tpBoomVelocity, _tpReturnSeconds, Mathf.Infinity, dt);
            }

            position = _tpPivot + direction * _tpBoom;
            fov = CameraModeService.Settings.ThirdPersonFov;
        }

        private void ComputeFirstPersonPose(out Vector3 position, out Quaternion rotation, out float fov)
        {
            rotation = ViewRotation();
            Quaternion yawOnly = Quaternion.Euler(0f, _viewYaw, 0f);
            float eye = _fpEyeHeightLive > 0f ? _fpEyeHeightLive : _fpEyeHeight;
            position = Feet + Vector3.up * eye + yawOnly * (Vector3.forward * _fpEyeForward);
            UpdateWallProbe(Feet + Vector3.up * eye, yawOnly * Vector3.forward);

            // FirstPersonFov is horizontal degrees at 16:9 (Hor+, C-00 24.09); Unity wants vertical.
            float horizontal = CameraModeService.Settings.FirstPersonFov;
            fov = 2f * Mathf.Atan(Mathf.Tan(horizontal * 0.5f * Mathf.Deg2Rad) / CameraModeSettings.FovReferenceAspect) * Mathf.Rad2Deg;
        }

        private void ApplyLens(CameraMode mode, float fov, float dt)
        {
            if (_camera == null)
            {
                return;
            }

            // FOV punch eases back linearly over its authored duration (direction doc §6).
            if (_fovPunch != 0f && dt > 0f)
            {
                _fovPunch = Mathf.MoveTowards(_fovPunch, 0f, _fovPunchDecayPerSecond * dt);
            }

            bool reduced = CameraModeService.Settings.ReducedMotion;
            _camera.fieldOfView = fov + (reduced || mode == CameraMode.TopDown ? 0f : _fovPunch);
            if (mode == CameraMode.TopDown && !CameraModeService.IsTransitioning)
            {
                _camera.nearClipPlane = _nearClip;
                _camera.farClipPlane = _farClip;
                return;
            }

            CameraModeProfile profile = CameraModeService.CurrentProfile;
            _camera.nearClipPlane = profile.NearClip > 0f ? profile.NearClip : _nearClip;
            _camera.farClipPlane = profile.FarClip > 0f ? profile.FarClip : _farClip;
        }

        private void ApplyCursor()
        {
            if (CameraModeService.CursorIntentHandledExternally)
            {
                return;   // UiCursor (S-09) is the single writer once it consumes the intent
            }

            bool want = CameraModeService.CursorIntent == CameraCursorIntent.Locked && Application.isFocused;
            if (want == _cursorLockedByRig)
            {
                return;
            }

            _cursorLockedByRig = want;
            Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
        }

        private void ReleaseCursor()
        {
            if (_cursorLockedByRig && !CameraModeService.CursorIntentHandledExternally)
            {
                Cursor.lockState = CursorLockMode.None;
            }

            _cursorLockedByRig = false;
        }

        /// <summary>
        /// FP: the body renders shadows only until slice 3 (head-only hiding + viewmodel layer).
        /// TP: the player hides when the boom is pushed closer than <see cref="_tpHidePlayerDistance"/>.
        /// Shadows always stay. Renderers are re-collected on every hide so weapon swaps are covered.
        /// </summary>
        private void ApplyPlayerVisibility(CameraMode mode)
        {
            bool hide = mode == CameraMode.FirstPerson && (!CameraModeService.IsTransitioning || CameraModeService.Transition01 > 0.8f);
            if (mode == CameraMode.ThirdPerson && _tpBoom >= 0f && _tpBoom < _tpHidePlayerDistance)
            {
                hide = true;
            }

            if (hide)
            {
                HidePlayer(mode == CameraMode.FirstPerson);
            }
            else
            {
                ShowPlayer();
            }
        }

        /// <summary>
        /// Hides the player body (shadows kept). In FP (<paramref name="weaponToViewmodel"/>) the
        /// weapon under the hand socket moves to the <see cref="CameraLayers.FirstPersonView"/>
        /// layer instead: the overlay camera draws it after a depth clear, so it never clips into
        /// walls (Phase 1: world FOV; S-06 WP-071 viewmodel arms replace it under ViewmodelAnchor).
        /// </summary>
        private void HidePlayer(bool weaponToViewmodel)
        {
            Transform body = ResolveBody();
            if (body == null)
            {
                return;
            }

            Transform weaponRoot = weaponToViewmodel && _visual != null ? _visual.WeaponSocket : null;
            int vmLayer = CameraLayers.FirstPersonView;
            body.GetComponentsInChildren(false, _playerRenderers);
            for (int i = 0; i < _playerRenderers.Count; i++)
            {
                Renderer r = _playerRenderers[i];
                if (r == null)
                {
                    continue;
                }

                if (weaponRoot != null && vmLayer >= 0 && r.transform.IsChildOf(weaponRoot))
                {
                    GameObject go = r.gameObject;
                    if (go.layer != vmLayer)
                    {
                        if (!_viewmodelLayers.ContainsKey(go))
                        {
                            _viewmodelLayers.Add(go, go.layer);
                        }

                        go.layer = vmLayer;
                    }

                    continue;
                }

                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    continue;
                }

                if (!_playerShadowModes.ContainsKey(r))
                {
                    _playerShadowModes.Add(r, r.shadowCastingMode);
                }

                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            _playerHidden = true;
        }

        private void ShowPlayer()
        {
            if (!_playerHidden)
            {
                return;
            }

            foreach (KeyValuePair<Renderer, ShadowCastingMode> pair in _playerShadowModes)
            {
                if (pair.Key != null)
                {
                    pair.Key.shadowCastingMode = pair.Value;
                }
            }

            _playerShadowModes.Clear();
            foreach (KeyValuePair<GameObject, int> pair in _viewmodelLayers)
            {
                if (pair.Key != null)
                {
                    pair.Key.layer = pair.Value;
                }
            }

            _viewmodelLayers.Clear();
            _playerHidden = false;
        }

        private Transform ResolveBody()
        {
            Transform body = _followTarget != null && _followTarget.parent != null ? _followTarget.parent : null;
            if (body != _cachedBody)
            {
                _cachedBody = body;
                _visual = body != null ? body.GetComponentInChildren<PlayerSkinnedVisual>(true) : null;
            }

            return body;
        }

        /// <summary>
        /// FP eye height from the Humanoid head bone at mode entry (+6 cm to the eyes), clamped to
        /// a plausible band; a fixed height keeps the view deterministic (no animation jitter,
        /// direction doc D-16) while matching the character's real size (S-06).
        /// </summary>
        private void CalibrateEyeHeight()
        {
            _fpEyeHeightLive = -1f;
            ResolveBody();
            Animator animator = _visual != null ? _visual.Animator : null;
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
            {
                _fpEyeHeightLive = Mathf.Clamp(head.position.y - Feet.y + 0.06f, 1.4f, 1.85f);
            }
        }

        /// <summary>
        /// FP wall probe (S-06): a short sphere cast from the eye along the view yaw lowers the
        /// weapon below <see cref="_fpWallLowerDistance"/> and raises it above
        /// <see cref="_fpWallRaiseDistance"/> (hysteresis). Firing is never blocked here.
        /// </summary>
        private void UpdateWallProbe(Vector3 eye, Vector3 forward)
        {
            ResolveBody();
            if (_visual == null)
            {
                return;
            }

            RaycastHit hit;
            float distance = Physics.SphereCast(eye, _fpWallProbeRadius, forward, out hit, _fpWallRaiseDistance + 0.1f,
                PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore) ? hit.distance : float.MaxValue;

            if (!_weaponLowered && distance < _fpWallLowerDistance)
            {
                _weaponLowered = true;
                _visual.PlayLower();
            }
            else if (_weaponLowered && distance > _fpWallRaiseDistance)
            {
                RaiseWeaponIfLowered();
            }
        }

        private void RaiseWeaponIfLowered()
        {
            if (_weaponLowered && _visual != null)
            {
                _visual.PlayRaise();
            }

            _weaponLowered = false;
        }

        /// <summary>WP-070 B3: the viewmodel overlay camera (null until first FP entry).</summary>
        public Camera ViewmodelCamera
        {
            get { return _viewmodelCamera; }
        }

        /// <summary>WP-070 B3: true when the viewmodel camera is a URP Overlay in this rig's camera stack.</summary>
        public bool IsViewmodelOverlayStacked
        {
            get
            {
                if (_viewmodelCamera == null || _camera == null)
                {
                    return false;
                }

                UniversalAdditionalCameraData vm = _viewmodelCamera.GetUniversalAdditionalCameraData();
                UniversalAdditionalCameraData world = _camera.GetUniversalAdditionalCameraData();
                return vm != null && vm.renderType == CameraRenderType.Overlay
                       && world != null && world.cameraStack != null && world.cameraStack.Contains(_viewmodelCamera);
            }
        }

        /// <summary>WP-070: FP weapon lowered by the wall probe (readout / tests).</summary>
        public bool IsWeaponLoweredByWall
        {
            get { return _weaponLowered; }
        }

        /// <summary>
        /// WP-070 B3: the viewmodel overlay camera (URP camera stack). Child of the world camera,
        /// so it inherits shake/recoil; draws only the FirstPersonView layer after a depth clear;
        /// never tagged MainCamera (S-03: Camera.main stays the world camera). Enabled in FP only.
        /// </summary>
        private void ApplyViewmodelCamera(CameraMode mode)
        {
            int vmLayer = CameraLayers.FirstPersonView;
            if (_camera == null || vmLayer < 0)
            {
                return;
            }

            bool fp = mode == CameraMode.FirstPerson;
            if (!fp && _viewmodelCamera == null)
            {
                return;
            }

            if (_viewmodelCamera == null)
            {
                _camera.cullingMask &= ~(1 << vmLayer);
                var go = new GameObject("ViewmodelCamera");
                go.transform.SetParent(_camera.transform, false);
                _viewmodelCamera = go.AddComponent<Camera>();
                _viewmodelCamera.cullingMask = 1 << vmLayer;
                _viewmodelCamera.clearFlags = CameraClearFlags.Depth;
                _viewmodelCamera.nearClipPlane = 0.01f;
                _viewmodelCamera.farClipPlane = 20f;
                UniversalAdditionalCameraData overlay = _viewmodelCamera.GetUniversalAdditionalCameraData();
                overlay.renderType = CameraRenderType.Overlay;
                overlay.renderShadows = false;
                UniversalAdditionalCameraData baseData = _camera.GetUniversalAdditionalCameraData();
                if (baseData != null && baseData.cameraStack != null && !baseData.cameraStack.Contains(_viewmodelCamera))
                {
                    baseData.cameraStack.Add(_viewmodelCamera);
                }

                var anchor = new GameObject("ViewmodelAnchor");
                anchor.transform.SetParent(go.transform, false);
                anchor.layer = vmLayer;
                ViewmodelAnchor = anchor.transform;
            }

            if (_viewmodelCamera.enabled != fp)
            {
                _viewmodelCamera.enabled = fp;
            }

            if (fp)
            {
                _viewmodelCamera.fieldOfView = CameraLayers.ViewmodelFov;
            }
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

        /// <summary>
        /// WP-070 reactions (S-07 sink), drained before the pose is built: recoil joins the VIEW
        /// (and therefore the aim ray this very frame) and returns at the recoil return rate;
        /// kick / FOV punch are cosmetic (child node / lens) and never touch aim.
        /// </summary>
        private void ConsumeReactions(CameraMode mode, float dt)
        {
            CameraModeSettings settings = CameraModeService.Settings;
            Vector2 recoil;
            Vector3 kick;
            float kickRoll, fovPunch, fovSeconds;
            CameraReactions.Drain(out recoil, out kick, out kickRoll, out fovPunch, out fovSeconds);

            if (dt > 0f)
            {
                _recoil = Vector2.MoveTowards(_recoil, Vector2.zero, _recoilReturnDegreesPerSecond * dt);
                float k = 1f - Mathf.Exp(-_kickReturnRate * dt);
                _kickOffset = Vector3.Lerp(_kickOffset, Vector3.zero, k);
                _kickRoll = Mathf.Lerp(_kickRoll, 0f, k);
            }

            if (mode != CameraMode.TopDown)
            {
                _recoil += recoil;
                if (fovPunch != 0f && !settings.ReducedMotion)
                {
                    _fovPunch = fovPunch;
                    _fovPunchDecayPerSecond = Mathf.Abs(fovPunch) / Mathf.Max(0.01f, fovSeconds);
                }
            }

            float cosmetic = settings.ReducedMotion ? 0f : 1f;
            float translation = mode == CameraMode.FirstPerson ? 0f : 1f;   // D-15: no FP translation
            _kickOffset += kick * translation * cosmetic;
            _kickRoll += kickRoll * cosmetic;
        }

        private void ApplyShake(float dt, CameraMode mode)
        {
            if (_shakeNode == null)
            {
                return;
            }

            CameraModeSettings settings = CameraModeService.Settings;
            float modeScale = Mathf.Max(0f, CameraModeService.CurrentProfile.ShakeScale);
            if (settings.ReducedMotion)
            {
                modeScale *= 0.3f;
            }

            Vector3 localOffset;
            float roll;
            CameraShakeService.Sample(
                dt, _maxShakeAmplitude * modeScale, _shakeIntensity,
                mode == CameraMode.TopDown ? _fixedRotation : transform.rotation, out localOffset, out roll);

            Vector3 bob = Vector3.zero;
            if (mode == CameraMode.FirstPerson && dt > 0f)
            {
                float speed01;
                bool grounded;
                if (!CameraReactions.ReadBob(out speed01, out grounded))
                {
                    // Nobody fed the bob this frame (S-07 feel layer not bound yet): estimate
                    // locomotion from the feet's planar speed.
                    Vector3 step = Feet - _lastFeet;
                    step.y = 0f;
                    speed01 = Mathf.Clamp01(step.magnitude / dt / Mathf.Max(0.1f, _bobReferenceSpeed));
                    grounded = true;
                }
                float amount = grounded ? speed01 * settings.HeadBobScale * (settings.ReducedMotion ? 0f : 1f) : 0f;
                _bobPhase = Mathf.Repeat(_bobPhase + dt * _bobFrequencyHz * Mathf.Max(0.25f, speed01), 1f);
                float wave = _bobPhase * Mathf.PI * 2f;
                bob = new Vector3(Mathf.Sin(wave) * _bobLateral, Mathf.Abs(Mathf.Sin(wave)) * _bobVertical, 0f) * amount;
            }

            if (mode == CameraMode.FirstPerson)
            {
                localOffset = Vector3.zero;   // D-15: FP shake is rotation only
            }

            _lastFeet = Feet;
            _shakeNode.localPosition = localOffset + _kickOffset + bob;
            _shakeNode.localRotation = Quaternion.Euler(0f, 0f, roll + _kickRoll);
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

        // ------------------------------------------------------------ WP-070 API

        /// <summary>
        /// WP-070: first-person viewmodel anchor (hands + weapon root on the
        /// <see cref="CameraLayers.FirstPersonView"/> layer). Null until the FP rig builds it.
        /// </summary>
        public Transform ViewmodelAnchor { get; private set; }

        /// <summary>Colliders the TP/FP centre ray can stop on: world blockers and enemy hitboxes.</summary>
        public static int AimRayMask
        {
            get { return DD.Prototype.Core.PrototypeLayers.AimBlockMask | (1 << DD.Prototype.Core.PrototypeLayers.EnemyHitbox); }
        }

        /// <summary>Maximum TP/FP aim ray length; beyond it the aim point is placed at this range.</summary>
        public const float AimRayMaxDistance = 120f;

        /// <summary>
        /// WP-070: the SHAKE-FREE aim ray for the active mode.
        ///   TopDown — the cursor ray (<paramref name="screenPoint"/>) onto the horizontal aim
        ///             plane at <paramref name="aimPlaneY"/>; identical to <see cref="ScreenPointToAimPlane"/>.
        ///   TP / FP — the screen-centre ray from the stable pose against world blockers and
        ///             enemy hitboxes (<see cref="AimRayMask"/>); without a hit the point sits at
        ///             <see cref="AimRayMaxDistance"/>. <paramref name="screenPoint"/> is ignored.
        /// Weapons fire from the muzzle towards <see cref="CameraAimRay.HitPoint"/> (parallax
        /// resolution with S-02 WP-069 ADR-069-1).
        /// </summary>
        public bool TryGetAimRay(Vector2 screenPoint, float aimPlaneY, out CameraAimRay ray)
        {
            return TryGetAimRay(screenPoint, aimPlaneY, AimRayMaxDistance, out ray);
        }

        /// <summary>WP-070 (S-02): same as above with the TP/FP ray length set by the caller (weapon max range).</summary>
        public bool TryGetAimRay(Vector2 screenPoint, float aimPlaneY, float maxDistance, out CameraAimRay ray)
        {
            ray = default(CameraAimRay);
            maxDistance = maxDistance > 0f ? maxDistance : AimRayMaxDistance;
            if (_camera == null)
            {
                return false;
            }

            if (CameraModeService.Current == CameraMode.TopDown)
            {
                Vector3 point;
                if (!ScreenPointToAimPlane(screenPoint, aimPlaneY, out point))
                {
                    return false;
                }

                Vector3 origin = transform.position;
                Vector3 delta = point - origin;
                ray.Origin = origin;
                ray.Distance = delta.magnitude;
                ray.Direction = ray.Distance > 1e-6f ? delta / ray.Distance : transform.forward;
                ray.HitPoint = point;
                ray.HasHit = true;
                return true;
            }

            ray.Origin = transform.position;
            ray.Direction = transform.rotation * Vector3.forward;
            RaycastHit hit;
            if (Physics.Raycast(ray.Origin, ray.Direction, out hit, maxDistance, AimRayMask, QueryTriggerInteraction.Collide))
            {
                ray.HitPoint = hit.point;
                ray.Distance = hit.distance;
                ray.HasHit = true;
            }
            else
            {
                ray.HitPoint = ray.Origin + ray.Direction * maxDistance;
                ray.Distance = maxDistance;
                ray.HasHit = false;
            }

            return true;
        }

        private string Describe()
        {
            Vector3 p = transform.position;
            return string.Format(
                "{6}  pos ({0:0.0}, {1:0.0}, {2:0.0})  fov {3:0.0}  lookahead {4:0.00} m  shakes {5}  yaw {7:0} pitch {8:0}",
                p.x, p.y, p.z, _camera != null ? _camera.fieldOfView : 0f, _lookAhead.magnitude,
                CameraShakeService.ActiveCount, CameraModeService.Current, _viewYaw, ViewPitch);
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
