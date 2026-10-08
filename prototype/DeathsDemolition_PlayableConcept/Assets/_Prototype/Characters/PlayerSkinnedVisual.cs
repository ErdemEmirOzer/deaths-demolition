using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using UnityEngine;
using DD.Prototype.Feedback;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-016/018 · The AP-006 player on the M1 player rig. Lives on the <c>Player_AP006</c>
    /// prefab root (written by <c>ArtCharacterPrefabBuilder</c>); <c>PlayerRigFactory</c>
    /// instantiates it under the rig's <c>Visual</c> node, hides the primitive parts and calls
    /// <see cref="Attach"/>. Skin = albedo swap on the one shared material through a property
    /// block (three renderers, one material asset — WP-016 §2); the weapon mount moves to
    /// <c>socket_weapon_r</c>.
    ///
    /// WP-018 (ADR-018-2): the AP-009 Animator owns the pose — base layer 2-D locomotion
    /// (idle / walk ring / run ring on MoveX·MoveY, rate LocoRate — WP-032B: the AP-009B v002 run
    /// cycles are the outer ring), deaths ×2; <c>override_upper</c> hit_light and the pickup
    /// (WP-032B · DEC-082: the grab plays over the legs, no movement lock); <c>additive</c> fire_recoil;
    /// <c>aim_layer</c> = <see cref="PlayerAimLayer"/> turning Spine/Chest/UpperChest toward the
    /// aim (REQ-038) on top of the base. <c>PlayerProceduralAnimator</c> forwards its hooks
    /// here and writes nothing to the hidden block rig; the ProceduralPoseBridge is gone.
    ///
    /// WP-082 R3 (LOCO-SOZLESME): with the AP-026 controller (LocoSpeed: Simple1D rings → SimpleDirectional2D 8 ways) the base
    /// layer is fed LocoSpeed (m/s), the body-space unit direction (hip hysteresis + smoothing) and LocoRate = v / honest speed;
    /// the v4 starts / stops run on V4Time (distance-matched). The AP-022 4-way controller keeps its path (no LocoSpeed).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSkinnedVisual : MonoBehaviour
    {
        public const string PrefabName = "Player_AP006";
        public const string WeaponSocketName = "socket_weapon_r";
        public const string BackSocketName = "socket_back";
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        [SerializeField] private Animator _animator;
        [SerializeField] private LODGroup _lodGroup;
        [SerializeField] private SkinnedMeshRenderer[] _renderers = new SkinnedMeshRenderer[0];
        [Tooltip("WP-071 B4b-3: the player model asset the FP viewmodel arms are copied from (player_base.fbx).")]
        [SerializeField] private GameObject _fpModel;
        private FirstPersonArms _fpArms;
        /// <summary>WP-071 B4b-3: the FP viewmodel arms (null without an FP set; tests).</summary>
        public FirstPersonArms FirstPersonArms => _fpArms;

        /// <summary>Editor (prefab builder): the model the FP arms copy.</summary>
        public void ConfigureFirstPerson(GameObject model, Mesh armsMesh = null, Material armsMaterial = null, Texture2D[] armsAlbedos = null)
        {
            _fpModel = model; _fpArmsMesh = armsMesh; _fpArmsMaterial = armsMaterial; _fpArmsAlbedos = armsAlbedos;
        }
        [Tooltip("WP-071 B4b-3: the arms-only copy of the LOD0 body the FP viewmodel draws (builder-generated).")]
        [SerializeField] private Mesh _fpArmsMesh;
        [SerializeField] private Material _fpArmsMaterial;       // AP-022 FP arms v2 (own UVs / maps)
        [SerializeField] private Texture2D[] _fpArmsAlbedos;     // per PlayerSkin
        [SerializeField] private Transform _weaponSocket;
        [SerializeField] private Transform _backSocket;
        [SerializeField] private CharacterAnimationSet _animation;
        [SerializeField] private CharacterAnimationEvents _events;
        [SerializeField] private PlayerAimLayer _aimLayer;
        [SerializeField] private CharacterLookAt _lookAt;   // WP-032
        [SerializeField] private FootIkAdapter _footIk;     // WP-032

        private MaterialPropertyBlock _block;
        // Animator.StringToHash may be negative: never use the id as a "not set" sentinel.
        private static readonly int SpeedId = Animator.StringToHash(CharacterAnimatorContract.Speed);
        private static readonly int MoveXId = Animator.StringToHash(CharacterAnimatorContract.MoveX);
        private static readonly int MoveYId = Animator.StringToHash(CharacterAnimatorContract.MoveY);
        private static readonly int LocoRateId = Animator.StringToHash(CharacterAnimatorContract.LocoRate);
        private static readonly int StillId = Animator.StringToHash(CharacterAnimatorContract.Still);
        private static readonly int V4ClipId = Animator.StringToHash(LocomotionV4.ClipParam);
        // WP-082 R3 (LOCO-SOZLESME §1): the AP-026 8-way tree's speed and the v4 motion time
        private static readonly int LocoSpeedId = Animator.StringToHash(CharacterAnimatorContract.LocoSpeed);
        private static readonly int V4TimeId = Animator.StringToHash(LocomotionV4.TimeParam);
        private static readonly int LocomotionStateHash = Animator.StringToHash(CharacterAnimatorContract.LocomotionState);
        private static readonly int FireId = Animator.StringToHash(CharacterAnimatorContract.Fire);
        private static readonly int HitId = Animator.StringToHash(CharacterAnimatorContract.HitLight);
        private static readonly int DieId = Animator.StringToHash(CharacterAnimatorContract.Die);
        private static readonly int DeathStyleId = Animator.StringToHash(CharacterAnimatorContract.DeathStyle);
        private static readonly int PickupId = Animator.StringToHash(CharacterAnimatorContract.Pickup);
        private static readonly int AliveId = Animator.StringToHash(CharacterAnimatorContract.Alive);
        // WP-032C · AP-009C weapon handling (S-02 WP-042 drives fire / reload / switch through PlayerProceduralAnimator or the WeaponEvents below)
        private static readonly int WeaponId = Animator.StringToHash(CharacterAnimatorContract.Weapon);
        private static readonly int ReloadId = Animator.StringToHash(CharacterAnimatorContract.Reload);
        private static readonly int ReloadRateId = Animator.StringToHash(CharacterAnimatorContract.ReloadRate);
        private static readonly int ReloadEmptyId = Animator.StringToHash(CharacterAnimatorContract.ReloadEmpty);
        private static readonly int SwitchOutId = Animator.StringToHash(CharacterAnimatorContract.SwitchOut);
        private static readonly int SwitchInId = Animator.StringToHash(CharacterAnimatorContract.SwitchIn);
        private static readonly int LowerId = Animator.StringToHash(CharacterAnimatorContract.Lower);
        private static readonly int RaiseId = Animator.StringToHash(CharacterAnimatorContract.Raise);
        private static readonly int SwHoldId = Animator.StringToHash(CharacterAnimatorContract.SwHold);
        private static readonly int HurtId = Animator.StringToHash(CharacterAnimatorContract.Hurt);
        private static readonly int HurtStyleId = Animator.StringToHash(CharacterAnimatorContract.HurtStyle);
        private static readonly int SwFireId = Animator.StringToHash(CharacterAnimatorContract.SwFire);   // WP-032E
        private int _weapon;
        private int _swHold;
        private SecondaryChainDriver _chains;   // WP-032F
        public SecondaryChainDriver Chains => _chains;
        private int _overrideLayer = -1;
        /// <summary>WP-032C: the ClassicWeaponId index the upper layer is posed for (0 A, 1 B, 2 C; 3 = no weapon clip → v001 recoil, Empty upper).</summary>
        public int Weapon => _weapon;
        /// <summary>WP-032C: 0 none, 1 Kagura Bell, 2 Ofuda Loom in the right hand.</summary>
        public int SpecialHold => _swHold;

        private Func<Vector3> _planarVelocity;
        private Func<float> _referenceSpeed;
        private Func<Vector3> _aimDirection;
        private int _appliedSkin = -1;
        private bool _authored;
        private bool _dying;
        private bool _deathEnded;
        private bool _subscribed;
        private Vector2 _move;
        private float _rate;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds per unit of the locomotion blend parameters (MoveX / MoveY / LocoRate smoothing). " +
            "Same value as the WP-016 Speed smoothing. Invented.", true)]
        private const float MoveSmoothing = 0.12f;

        [PrototypeOnly(PendingDecisions.None,
            "Highest child time scale of a locomotion clip (walk_fwd at the 6.2 m/s reference is ×1.9; the walk carries no " +
            "weight there since WP-032B's run ring). Cap, invented.", true)]
        public const float MaxLocoRate = 2.6f;   // WP-071 MUST-4 (d): AP-022 walk_back needs 2.43, the walk strafes 2.95 → 2.6 caps only the strafes (P-06 AD refuter B)

        [PrototypeOnly(PendingDecisions.None, "WP-071 MUST-3: the body counts as standing under this speed (m/s) after StillHoldSeconds — the idle breath state. P-06 AD refuter B.", true)]
        public const float StillSpeed = 0.1f, StillHoldSeconds = 0.2f;

        [PrototypeOnly(PendingDecisions.None, "WP-071 MUST-4 (e): the foot lock engages only while the dominant ring child's slide share is at or under this (TP budget, CadenceBand.SlideClose).", true)]
        public const float LockMaxSlide = CadenceBand.SlideClose;

        [PrototypeOnly(PendingDecisions.None,
            "Highest playback rate of the run cycles (P-06 ×1.6: faster reads as a flicker). WP-032B needed 2.0 on the v002 run_fwd (3.75 m/s on the " +
            "Unity rig); WP-032C: the v003 run_fwd stride is 1.05 m (Blender 5.69 m/s no-slide), so 6.2 m/s sits under the cap again. Cap.", true)]
        public const float MaxRunRate = CadenceBand.Max;   // WP-071 K3: the run cycles stay in the cadence band; faster travel slides

        // ------------------------------------------------------------ WP-082 R3 · AP-026 8-way locomotion (LOCO-SOZLESME §4–§5)

        [PrototypeOnly(PendingDecisions.None, "WP-082 R3 §5.2 hip hysteresis: the fore / aft hemisphere switches only past 90° ± the dead band, held this long (PLAN §4.3 proposal; tuned by measurement).", true)]
        public const float HipCenterDegrees = 90f, HipDeadBandDegrees = 10f, HipHoldSeconds = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "WP-082 R3 §5.3: the tree direction turns toward the hysteresis angle at this rate (MoveSmoothing 0.12 s per 90°; the top-down aim turns 720°/s).", true)]
        public const float DirectionDegreesPerSecond = 750f;

        [PrototypeOnly(PendingDecisions.None, "WP-082 R3 §4.1 / §5.3: under this planar speed (m/s) the body stands — LocoRate 1, direction and hemisphere frozen.", true)]
        public const float StandSpeed = 0.05f;

        [PrototypeOnly(PendingDecisions.None, "WP-082 R3 §2.4: the fallback cross-fades into Locomotion (start exit / stop exit / cancelled start, seconds) when the motion-time state does not take its exit-time transition.", true)]
        public const float V4StartExitSeconds = 0.10f, V4StopExitSeconds = 0.15f, V4CancelSeconds = 0.15f;

        private RuntimeAnimatorController _scannedController;
        private bool _scannedInitialized;
        private bool _hasLocoSpeed, _hasV4Time, _ringsReady, _loco8, _warnedNoRings;
        // 1-D slot thresholds (m/s, hero) and the ring children's honest-speed inputs [ (slot − 1) × 8 + dir ]: stride (hero m) / cycle (s)
        private readonly float[] _ringThreshold = new float[4];
        private readonly float[] _ringStride = new float[24];
        private readonly float[] _ringCycle = new float[24];
        private float _standCycle = 1f;
        private float _honest = 1f, _blendCycle = 1f;
        // §5: the hip hemisphere (true = fore), its hold timer, the clamped side, the smoothed tree angle (degrees, 0 fwd, +90 right)
        private bool _fore = true, _hasHeading;
        private float _hemiHold, _treeAngle;
        private int _clampSide = 1;
        // §7–§8: the playing v4 clip's motion time and its exit fallback
        private LocomotionV4Tables _v4Tables;
        private readonly LocomotionV4Clock _v4Clock = new LocomotionV4Clock();
        private bool _v4Exiting, _v4FadeIssued;
        private int _v4Frames;
        private float _v4ExitOffset, _v4ExitSeconds;

        /// <summary>WP-082 R3: the controller carries the AP-026 8-way tree (LocoSpeed) — MoveX / MoveY are the unit direction, LocoSpeed m/s.</summary>
        public bool Locomotion8 => _loco8;
        /// <summary>WP-082 R3 §4.1: the honest speed of the current blend (Σ w·S / Σ w·T, m/s; tests / probes).</summary>
        public float HonestSpeed => _honest;
        /// <summary>WP-082 R3 §5: the fore hemisphere is active (hip hysteresis; tests / probes).</summary>
        public bool HipFore => _fore;
        /// <summary>WP-082 R3: the v4 motion-time clock (tests / probes).</summary>
        public LocomotionV4Clock V4Clock => _v4Clock;
        /// <summary>WP-082 R3: the v4 tables bound at Attach (null without them).</summary>
        public LocomotionV4Tables V4Tables => _v4Tables;
        /// <summary>WP-082 R3 §8.2: the braking rate the stop's remaining distance assumes (PlayerConfigSection.Deceleration; the rig factory may set the live value).</summary>
        public float V4Deceleration { get => _v4.Deceleration; set => _v4.Deceleration = Mathf.Max(0.1f, value); }

        public Animator Animator => _animator;
        private WeaponGripIk _gripIk;
        private static readonly int[] s_restHashes =
        {
            Animator.StringToHash(CharacterAnimatorContract.AimState + "_0"), Animator.StringToHash(CharacterAnimatorContract.AimState + "_1"),
            Animator.StringToHash(CharacterAnimatorContract.AimState + "_2"), Animator.StringToHash(CharacterAnimatorContract.SwHoldState + "_1"),
            Animator.StringToHash(CharacterAnimatorContract.SwHoldState + "_2"), Animator.StringToHash(CharacterAnimatorContract.SwHoldState + "_3"),
            Animator.StringToHash(CharacterAnimatorContract.AimV3State),
            // WP-083 R3 (S-02 triage, C-00 07.10 option a): sr_1's bolt cycle keeps the hold and the support grip on — the clip's own
            // rotation rides on the hold (WeaponMountAlignment.HoldDelta) instead of the gates closing and the weapon swinging to the
            // clip's pose (~91 cm at the muzzle); the right hand's bolt stroke is not shown (top-down, accepted)
            Animator.StringToHash(CharacterAnimatorContract.CycleV3State),
            // ... and its focused variant (AdsWeight > 0.5 at the shot; top-down too, FocusHeld) — the same cycle, held the same way
            Animator.StringToHash(CharacterAnimatorContract.CycleAdsV3State)
        };
        private static readonly SkinnedMeshRenderer[] s_noRenderers = new SkinnedMeshRenderer[0];
        private SkinnedMeshRenderer[] _headRenderers;

        /// <summary>WP-071 B4: the left-hand grip IK (tests / probes).</summary>
        public WeaponGripIk GripIk => _gripIk;

        /// <summary>
        /// WP-071 (S-01 WP-070 FP): the head's own renderers, one per LOD, once P-06's rig v002 splits the head off the body
        /// (renderer name contains "Head") — FP sets them ShadowsOnly. Empty until then.
        /// </summary>
        public SkinnedMeshRenderer[] HeadRenderers
        {
            get
            {
                if (_headRenderers != null) return _headRenderers;
                int n = 0;
                for (int i = 0; i < _renderers.Length; i++) if (_renderers[i] != null && _renderers[i].name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0) n++;
                if (n == 0) return _headRenderers = s_noRenderers;
                _headRenderers = new SkinnedMeshRenderer[n];
                n = 0;
                for (int i = 0; i < _renderers.Length; i++) if (_renderers[i] != null && _renderers[i].name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0) _headRenderers[n++] = _renderers[i];
                return _headRenderers;
            }
        }

        /// <summary>WP-071 B4: the upper layer holds the weapon (Aim_x / SwHold_x, or blending into one) — the grip IK may hold the left hand.</summary>
        /// <summary>Tests: the grip gate's inputs.</summary>
        public bool UpperAtRestForTest => UpperAtRest();
        public int OverrideLayerIndex => ResolveOverrideLayer();

        /// <summary>
        /// WP-071 B4: the override_upper index, resolved once the Animator is initialised — Attach can run while the body is
        /// inactive (layerCount 0), which left it at −1 for the whole run (the grip gate and CancelReload then saw "no layer").
        /// </summary>
        private int ResolveOverrideLayer()
        {
            if (_overrideLayer >= 0 || _animator == null || !_animator.isInitialized) return _overrideLayer;
            for (int i = 0; i < _animator.layerCount; i++) if (_animator.GetLayerName(i) == CharacterAnimatorContract.OverrideUpperLayer) { _overrideLayer = i; break; }
            return _overrideLayer;
        }

        private int _additiveLayer = -1;
        private float _additiveWeight = -1f;

        /// <summary>
        /// WP-071 (C-00 05.10 decision B, S-02 measurement): in first person the TP fire additive (the body's recoil kick) weighs 0 —
        /// FpViewPose carries the TP mount deviation into FP for the 0.6 s FpClipMotion window after a switch / Special return, and the
        /// TP fire clip rode in with it (ar_2 f14–20 +8–16°). The TP body is hidden in FP (only its shadow loses the kick); the
        /// clips are untouched. Third person / top-down keep weight 1.
        /// </summary>
        private void MuteTpAdditiveInFirstPerson()
        {
            if (_additiveLayer < 0)
            {
                if (!_animator.isInitialized) return;
                _additiveLayer = _animator.GetLayerIndex(CharacterAnimatorContract.AdditiveLayer);
                if (_additiveLayer < 0) return;
            }

            float w = DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson ? 0f : 1f;
            if (w == _additiveWeight) return;
            _animator.SetLayerWeight(_additiveLayer, w);
            _additiveWeight = w;
        }

        /// <summary>WP-071 (C-00 05.10): the TP fire additive's weight this visual last set (tests).</summary>
        public float TpAdditiveWeight => _additiveWeight;

        private static readonly int s_cycleHash = Animator.StringToHash(CharacterAnimatorContract.CycleV3State);
        private static readonly int s_cycleAdsHash = Animator.StringToHash(CharacterAnimatorContract.CycleAdsV3State);

        /// <summary>WP-083 R3: the override layer is in (or entering) the v3 bolt cycle, focused or not.</summary>
        private bool UpperInCycle()
        {
            if (ResolveOverrideLayer() < 0) return false;
            int h = _animator.IsInTransition(_overrideLayer) ? _animator.GetNextAnimatorStateInfo(_overrideLayer).shortNameHash : _animator.GetCurrentAnimatorStateInfo(_overrideLayer).shortNameHash;
            return h == s_cycleHash || h == s_cycleAdsHash;
        }

        private bool UpperAtRest()
        {
            if (ResolveOverrideLayer() < 0) return true;
            int h = _animator.IsInTransition(_overrideLayer) ? _animator.GetNextAnimatorStateInfo(_overrideLayer).shortNameHash : _animator.GetCurrentAnimatorStateInfo(_overrideLayer).shortNameHash;
            for (int i = 0; i < s_restHashes.Length; i++) if (s_restHashes[i] == h) return true;
            return false;
        }
        private LocomotionLean _lean;
        /// <summary>WP-071 B2 readouts (tests).</summary>
        public LocomotionLean Lean => _lean;
        public LODGroup LodGroup => _lodGroup;
        public SkinnedMeshRenderer[] Renderers => _renderers;
        public Transform WeaponSocket => _weaponSocket;
        /// <summary>WP-082 / WP-083 B2b: the left hand's magazine socket (<see cref="Characters.MagazineSocket"/>; S-02 WeaponMagazineOwnership).</summary>
        public Transform MagazineSocket => _magSocket;
        private Transform _magSocket;
        /// <summary>WP-083 B2e: the held classic weapon's magazine part (S-02 WeaponMagazineVisual; null without one).</summary>
        public static Transform HeldMagazinePart()
        {
            DD.Prototype.Weapons.WeaponController c = DD.Prototype.Weapons.WeaponRegistry.ActiveController;
            return c != null && c.MagazineVisual != null ? c.MagazineVisual.Part : null;
        }

        /// <summary>WP-083 B2e: the reload's magazine contact (left hand on the well on the beats).</summary>
        public MagazineContactIk ContactIk => _contactIk;
        private MagazineContactIk _contactIk;
        /// <summary>WP-083 B2c: the weapon-first right-hand hold (null while WeaponHoldIk.Enabled was off at Attach).</summary>
        public WeaponHoldIk HoldIk => _holdIk;
        private WeaponHoldIk _holdIk;
        public Transform BackSocket => _backSocket;
        public CharacterAnimationSet AnimationSet => _animation;
        public CharacterAnimationEvents Events => _events;
        public PlayerAimLayer AimLayer => _aimLayer;
        public CharacterLookAt LookAt => _lookAt;
        public FootIkAdapter FootIk => _footIk;

        [PrototypeOnly(PendingDecisions.None, "Distance ahead along the aim the head looks at, metres. Invented.", true)]
        private const float AimLookDistance = 8f;

        private float _runTimeScale = 1f;
        // WP-071 MUST-3/4: the ring children's contact speeds per direction (0 fwd, 1 back, 2 left, 3 right) and the time scales baked against them
        private readonly float[] _walkSpeed = { 1f, 1f, 1f, 1f };
        private readonly float[] _runSpeed = { 1f, 1f, 1f, 1f };
        private bool _hasStill;
        // WP-083 B3b: the v4 start / stop / pivot driver — live only while the controller carries the LocomotionV4 sub-state machine
        private bool _hasV4;
        private readonly LocomotionV4Events _v4 = new LocomotionV4Events();
        private Func<Vector3> _commandedVelocity;
        private readonly float[] _v4GaitSpeeds = new float[4];
        /// <summary>WP-083 B3b: the v4 event detector (tests) and whether this controller answers it.</summary>
        public LocomotionV4Events V4Events => _v4;
        public bool HasLocomotionV4 => _hasV4;
        private float _stillFor;
        private float _mps;
        /// <summary>WP-071 MUST-3 readouts (tests): the walk-ring radius along the current move direction, and whether the breath state is asked for.</summary>
        public float WalkRingRadius { get; private set; }
        public bool StillRequested { get; private set; }
        public int AppliedSkinIndex => _appliedSkin;
        /// <summary>True when the AP-009 controller drives this body (false on the rig-test set).</summary>
        public bool Authored => _authored;
        public bool IsDying => _dying;
        /// <summary>death_end arrived (the clip holds its last frame).</summary>
        public bool DeathFinished => _deathEnded;
        /// <summary>Smoothed locomotion blend input (tests).</summary>
        public Vector2 Move => _move;
        public float LocoRate => _rate;

        /// <summary>Editor (prefab builder): wire the serialized references once.</summary>
        public void Configure(Animator animator, LODGroup lodGroup, SkinnedMeshRenderer[] renderers, Transform weaponSocket, Transform backSocket,
            CharacterAnimationSet animation, CharacterAnimationEvents events, PlayerAimLayer aimLayer, CharacterLookAt lookAt = null, FootIkAdapter footIk = null)
        {
            _lookAt = lookAt;
            _footIk = footIk;
            _animator = animator;
            _lodGroup = lodGroup;
            _renderers = renderers ?? new SkinnedMeshRenderer[0];
            _weaponSocket = weaponSocket;
            _backSocket = backSocket;
            _animation = animation;
            _events = events;
            _aimLayer = aimLayer;
        }

        /// <summary>
        /// Bind to the M1 rig: <paramref name="visual"/> is the rig's Visual node (this object is
        /// re-parented under it at the origin). The delegates read the motor / aim each frame
        /// (no reference into the player module from here). Returns the socket the weapon
        /// mount should be parented to (null when the rig has none).
        /// </summary>
        public Transform Attach(Transform visual, Func<Vector3> planarVelocity, Func<float> referenceSpeed, Func<Vector3> aimDirection, Func<Vector3> commandedVelocity = null)
        {
            _commandedVelocity = commandedVelocity;   // WP-083 B3b: the motor's command (input × speed) — the v4 start / stop / pivot heading
            transform.SetParent(visual, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _planarVelocity = planarVelocity;
            _referenceSpeed = referenceSpeed;
            _aimDirection = aimDirection;

            if (_animator != null)
            {
                // The player is always on screen; AlwaysAnimate keeps the death hold / pickup
                // timing exact even when the camera cuts (results hand-off).
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _animator.applyRootMotion = false;
                if (_animation != null && _animation.Controller != null && _animator.runtimeAnimatorController == null)
                {
                    _animator.runtimeAnimatorController = _animation.Controller;
                }
            }

            _authored = _animation != null && _animation.IsAuthored && _animator != null && _animator.runtimeAnimatorController != null;
            // WP-032B: the run ring's child time scale (reference / run_fwd no-slide speed) — the MaxRunRate clamp divides by it.
            float runSpeed = _animation != null ? _animation.SpeedOf(CharacterAnimatorContract.PlayerRunFwd) : 0f;
            if (_animation != null)
            {
                string[] walks = { CharacterAnimatorContract.PlayerWalkFwd, CharacterAnimatorContract.PlayerWalkBack, CharacterAnimatorContract.PlayerStrafeLeft, CharacterAnimatorContract.PlayerStrafeRight };
                string[] runs = { CharacterAnimatorContract.PlayerRunFwd, CharacterAnimatorContract.PlayerRunBack, CharacterAnimatorContract.PlayerRunStrafeLeft, CharacterAnimatorContract.PlayerRunStrafeRight };
                for (int i = 0; i < 4; i++)
                {
                    _walkSpeed[i] = Mathf.Max(0.1f, _animation.SpeedOf(walks[i]));
                    _runSpeed[i] = Mathf.Max(0.1f, _animation.SpeedOf(runs[i]));
                }
            }

            _v4.Reset();
            _v4Clock.Reset();
            _v4Exiting = _v4FadeIssued = false;
            _hasHeading = false;
            _fore = true;
            // WP-083 B3b: the gait bands from the AP-026 forward loops (their no-slide speeds); a missing loop keeps the band ordered
            for (int g = 0; g < _v4GaitSpeeds.Length; g++)
            {
                float s = _animation != null ? _animation.SpeedOf(LocomotionV4.ClipPrefix + LocomotionV4.Gaits[g] + "_fwd") : 0f;
                _v4GaitSpeeds[g] = s > 0.05f ? s : (g + 1) * 1.6f;
            }
            BindLocomotionTables();   // WP-082 R3: the 8-way ring tables, the v4 distance tables and gait speeds (§7.1)
            ScanParameters();
            _runTimeScale = runSpeed > 0.01f && _animation != null ? RunChildTimeScale(_animation.LocomotionReferenceSpeed, runSpeed) : 1f;
            if (_events == null && _animator != null) _events = _animator.GetComponent<CharacterAnimationEvents>();
            if (_aimLayer == null && _animator != null) _aimLayer = _animator.GetComponent<PlayerAimLayer>();
            if (_aimLayer != null) _aimLayer.Bind(_animator, aimDirection, _authored);
            // WP-071 B4: the left hand on the weapon's grip_l after the mount alignment (order 910)
            if (_gripIk == null && _animator != null) _gripIk = _animator.GetComponent<WeaponGripIk>() ?? _animator.gameObject.AddComponent<WeaponGripIk>();
            if (_gripIk != null && _animator != null && _animator.isHuman)
            {
                _gripIk.Configure(_animator, _weaponSocket, () => _authored && !_dying && UpperAtRest());
            }

            // WP-083 B2c: the weapon-first right-hand hold (order 890, before the mount and the left-hand grip; added only while
            // WeaponHoldIk.Enabled; the grip IK's gate, and a classic weapon in the hand — its rows are the classic weapons'; a Special
            // (SwHold > 0, or its v3 set) keeps main's path until it has a row of its own, C-00 06.10)
            if (_holdIk == null && _animator != null) _holdIk = _animator.GetComponent<WeaponHoldIk>();
            if (_holdIk == null && _animator != null && _animator.isHuman && WeaponHoldIk.Enabled) _holdIk = _animator.gameObject.AddComponent<WeaponHoldIk>();
            if (_holdIk != null)
            {
                _holdIk.Configure(_animator, _weaponSocket, aimDirection, () => _authored && !_dying && _swHold == 0 && !_specialV3 && UpperAtRest(),
                    () => _gripIk != null && _gripIk.TryGetSupportGoal(out Vector3 g) ? g : (Vector3?)null);
            }

            // WP-082 / WP-083 B2b: the left hand's magazine socket (S-02 WeaponMagazineOwnership; the Hero v2 rig may carry its own)
            if (_magSocket == null && _animator != null) _magSocket = Characters.MagazineSocket.Ensure(_animator);
            // WP-083 B2e: the left hand on the magazine well on the reload's Detach / Insert beats (H3; on by default, C-00 05.10)
            if (_contactIk == null && _animator != null && _animator.isHuman) _contactIk = _animator.GetComponent<MagazineContactIk>() ?? _animator.gameObject.AddComponent<MagazineContactIk>();
            if (_contactIk != null)
            {
                // third-person views only (in first person this body is not seen; the FP arms carry their own contact)
                _contactIk.Configure(_animator, _magSocket, () => _specialReload ? _specialMagazineVisual.Part : HeldMagazinePart(),
                    () => _authored && !_dying && DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson);
            }
            // WP-032: the head looks along the SAME aim the aim layer turns to (single source, S-02); feet on the real ground.
            if (_lookAt == null && _animator != null) _lookAt = _animator.GetComponent<CharacterLookAt>();
            if (_footIk == null && _animator != null) _footIk = _animator.GetComponent<FootIkAdapter>();
            if (_lookAt != null)
            {
                _lookAt.Bind(
                    () => { Vector3 a = aimDirection != null ? aimDirection() : Vector3.zero; return a.sqrMagnitude > 1e-6f ? transform.position + Vector3.up * 1.4f + a.normalized * AimLookDistance : Vector3.zero; },
                    () => _authored && !_dying);
            }

            // WP-071: foot lock where the cadence is honest (walk ring rate ≈ 1; run ring when its clip rate ≤ LockMaxRate)
            // WP-071 MUST-4 (e): the lock follows the DOMINANT ring child's clip rate and slide share (run_back at 6.2 m/s slides 30 % → no lock)
            if (_footIk != null) _footIk.Bind(() => _authored && !_dying, () => true, LockGateOpen);
            // WP-082 R3 (E3 RAPOR §L3, S-03 WP-096): the swing foot keeps its animated lift — the per-foot solve pulled both feet to
            // the ground every frame, dropping the pelvis 0.19–0.27 m and reading every frame as contact
            if (_footIk != null) _footIk.PreserveSwingLift = true;
            // WP-071 B2: procedural lean on the spine from the planar acceleration
            if (_lean == null && _animator != null) _lean = _animator.GetComponent<LocomotionLean>() ?? _animator.gameObject.AddComponent<LocomotionLean>();
            if (_lean != null) _lean.Configure(HumanoidBones.Bone(_animator, HumanBodyBones.Spine), transform, () => _authored && !_dying);
            // WP-032F: sash / pouch / gourd / hachimaki tails — the player is always LOD0
            if (_chains == null) _chains = _animator.GetComponent<SecondaryChainDriver>();
            if (_chains != null) _chains.Bind(() => _authored);
            if (!_subscribed)
            {
                if (_events != null) _events.Raised += OnAnimationEvent;
                WeaponEvents.SpecialWeaponPickedUp += OnSpecialPickedUp;
                // WP-032C: the weapon-handling beats S-02 already publishes (WP-007/034) — the classic weapon's Aim / Reload / Fire clips follow them.
                WeaponEvents.WeaponEquipped += OnWeaponEquipped;
                WeaponEvents.ReloadStarted += OnReloadStarted;
                WeaponEvents.ReloadInterrupted += OnReloadInterrupted;
                WeaponEvents.SpecialChanged += OnSpecialChanged;
                WeaponEvents.SpecialWeaponEnded += OnSpecialEnded;
                WeaponEvents.SpecialWeaponCleared += OnSpecialEnded;
                SpecialWeaponUsageEvents.Usage += OnSpecialUsage;   // WP-032E · S-02 WP-056 (ADR-056-1): Activated → hold pose, Fired → swing beat, Deactivated → classic Aim
                _subscribed = true;
            }
            SubscribeFeelReload();

            if (_authored)
            {
                BindWeaponSets();
                if (_fpModel != null && _animation.FpController != null && _fpArms == null)
                {
                    _fpArms = GetComponent<FirstPersonArms>() ?? gameObject.AddComponent<FirstPersonArms>();
                    // shown for a classic weapon with an FP set; hidden while a Special is held (S-02 74419a07: the Special keeps its own FP view)
                    _fpArms.Configure(_fpModel, _animation, planarVelocity, () => _authored && !_dying && _swHold == 0, _renderers.Length > 0 ? _renderers[0] : null, () => _weaponDef, _fpArmsMesh,
                        _fpArmsMaterial, _fpArmsAlbedos, () => _appliedSkin);
                }
                _animator.SetBool(AliveId, true);
                _overrideLayer = -1;
                for (int i = 0; i < _animator.layerCount; i++) if (_animator.GetLayerName(i) == CharacterAnimatorContract.OverrideUpperLayer) _overrideLayer = i;
                SetWeapon(_weapon);
            }

            return _weaponSocket;
        }

        private void OnEnable() { if (_subscribed) SubscribeFeelReload(); }
        private void OnDisable()
        {
            UnsubscribeFeelReload();
            CancelReload();
        }
        private void SubscribeFeelReload()
        {
            if (_feelSubscribed) return;
            FeelEvents.ReloadStarted += OnFeelReloadStarted;
            FeelEvents.ReloadEnded += OnFeelReloadEnded;
            _feelSubscribed = true;
        }
        private void UnsubscribeFeelReload()
        {
            if (!_feelSubscribed) return;
            FeelEvents.ReloadStarted -= OnFeelReloadStarted;
            FeelEvents.ReloadEnded -= OnFeelReloadEnded;
            _feelSubscribed = false;
        }
        private void OnDestroy()
        {
            UnsubscribeFeelReload();
            EndSpecialReload();
            if (_subscribed)
            {
                if (_events != null) _events.Raised -= OnAnimationEvent;
                WeaponEvents.SpecialWeaponPickedUp -= OnSpecialPickedUp;
                WeaponEvents.WeaponEquipped -= OnWeaponEquipped;
                WeaponEvents.ReloadStarted -= OnReloadStarted;
                WeaponEvents.ReloadInterrupted -= OnReloadInterrupted;
                WeaponEvents.SpecialChanged -= OnSpecialChanged;
                WeaponEvents.SpecialWeaponEnded -= OnSpecialEnded;
                WeaponEvents.SpecialWeaponCleared -= OnSpecialEnded;
                SpecialWeaponUsageEvents.Usage -= OnSpecialUsage;
                _subscribed = false;
            }
        }

        private void OnAnimationEvent(CharacterAnimationEvent e)
        {
            if (_specialReload && ReloadPartCalibration.TryStep(e, out MagazineStep partStep))
                _specialMagazine.OnEvent(partStep);
            if (e == CharacterAnimationEvent.DeathEnd && _dying) _deathEnded = true;
            // WP-071 B4 (AP-022); WP-083 R3 (C-00 07.10): not on sr_1's bolt cycle — the hold keeps the weapon at its pivot there, and a
            // support hand following the clip's bolt stroke would work it in the air beside the held weapon; it stays on the handguard
            else if (e == CharacterAnimationEvent.LeftHandRelease) { if (_gripIk != null && !UpperInCycle()) _gripIk.SetReleased(true); }
            else if (e == CharacterAnimationEvent.LeftHandGrab) { if (_gripIk != null) _gripIk.SetReleased(false); }
            else if (_footIk != null) FootLockEvent(_footIk, e);   // WP-071 B2
        }

        /// <summary>WP-071 B2: plant / lift events drive the foot lock (shared by the player and zombie visuals).</summary>
        internal static void FootLockEvent(FootIkAdapter ik, CharacterAnimationEvent e)
        {
            if (e == CharacterAnimationEvent.FootLeft || e == CharacterAnimationEvent.FootRight) ik.NotifyPlant();
            else if (e == CharacterAnimationEvent.FootLOff) ik.NotifyLift(0);
            else if (e == CharacterAnimationEvent.FootROff) ik.NotifyLift(1);
        }

        private void OnSpecialPickedUp(float activeDuration)
        {
            PlayPickup();
        }

        // WP-071 B4b (K8): a weapon whose AnimSet has v3 clips plays the generic v3 states through its override controller;
        // the others keep WP-069's interim class pose (Arsenal.LegacyPoseIndex).
        private void OnWeaponEquipped(ClassicWeaponId id) => EquipWeapon(DD.Prototype.Core.Weapons.Arsenal.Get(id), id);

        // ---------------------------------------------------------------- WP-071 B4b per-weapon override controllers

        /// <summary>
        /// WP-071 B4b: the TP v3 weapon states — still OFF: with P-06's per-weapon-grip re-export the stock measures 3.7 cm in Unity
        /// (0 in Blender) — the Humanoid retarget moves the arms 1–4 cm from the authored pose (measured: RH–LH −3.2, LH–head −4.4 cm),
        /// over C-00's 2 cm gate. Off = the legacy class pose. The FP arms are independent: <see cref="FirstPersonV3Arms"/>.
        /// </summary>
        public static bool V3WeaponSets = false;

        /// <summary>
        /// WP-071 B4c (C-00 26.09): the TP v3 sets ON in the game — smg_2, the sniper, the launcher and the LMGs (P-06's per-weapon-grip
        /// exports; S-02's HerSilah_…KundakOmuz holds their stock ≤ 2 cm in the live TP pose). The AR / smg_1 pair stays on the legacy pose
        /// (<see cref="V3WeaponSets"/>, 3.7 cm).
        /// </summary>
        public static readonly string[] TpV3Sets = { "smg_2", "sr_1", "rl_1", "lmg_1", "lmg_2" };

        /// <summary>The TP v3 path for a set: all sets with <see cref="V3WeaponSets"/> (tests), else <see cref="TpV3Sets"/>.</summary>
        public static bool TpV3On(string animSet) => V3WeaponSets || (animSet != null && System.Array.IndexOf(TpV3Sets, animSet) >= 0);

        /// <summary>
        /// WP-071 B4b: the FP viewmodel arms (a copy of the player model on the viewmodel layer) — OFF by default (C-00 26.09): fpView v4 / v5
        /// moved the weapon up and forward and the B4b arms-only body cut (the kimono sleeve) now enters the frame half-way; the weapon
        /// stays on fpView without arms. On again with P-06's FP arms v2 + finger-grip clips (the WP-071 fpik batch).
        /// </summary>
        public static bool FirstPersonV3Arms = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetV3Statics() { V3WeaponSets = false; FirstPersonV3Arms = false; }

        private AnimatorOverrideController _runtimeOverrides;
        private readonly List<string> _setKeys = new List<string>();
        private readonly List<int> _setIndex = new List<int>();
        private readonly List<List<KeyValuePair<AnimationClip, AnimationClip>>> _setOverrides = new List<List<KeyValuePair<AnimationClip, AnimationClip>>>();
        private DD.Prototype.Core.Weapons.WeaponDefinition _weaponDef;
        private float _burstStart = -1f;

        /// <summary>Fire beats that reached the Animator (a burst weapon counts one per burst; tests).</summary>
        public int FireTriggers { get; private set; }
        /// <summary>The v3 animation set applied (WeaponDefinition.AnimSet), null on the legacy pose path (tests / probes).</summary>
        public string WeaponAnimSet { get; private set; }

        /// <summary>
        /// One runtime override controller per player (assigned once at Attach — swapping controllers would rebind and reset every
        /// layer); an equip copies that weapon's override pairs into it (cached at Attach, no allocation per equip).
        /// </summary>
        private void BindWeaponSets()
        {
            if (_runtimeOverrides != null || _animation == null || _animation.WeaponSets == null || _animation.WeaponSets.Count == 0) return;
            _runtimeOverrides = new AnimatorOverrideController(_animation.Controller) { name = "DD_Player_runtime" };
            _animator.runtimeAnimatorController = _runtimeOverrides;
            for (int i = 0; i < _animation.WeaponSets.Count; i++)
            {
                CharacterAnimationSet.WeaponAnimSet ws = _animation.WeaponSets[i];
                if (ws == null || ws.overrides == null) continue;
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(ws.overrides.overridesCount);
                ws.overrides.GetOverrides(pairs);
                _setKeys.Add(ws.animSet);
                _setIndex.Add(i);
                _setOverrides.Add(pairs);
            }
        }

        private static readonly int CycleId = Animator.StringToHash(CharacterAnimatorContract.Cycle);
        private static readonly int AdsWeightId = Animator.StringToHash(CharacterAnimatorContract.AdsWeight);
        private static readonly int FireHeldId = Animator.StringToHash(CharacterAnimatorContract.FireHeld);

        [PrototypeOnly(PendingDecisions.None, "WP-071 r2: a shot within this many fire intervals of the previous one continues a sustained string (the LMG fire loop takes over from the per-shot kick; it ends that long after the last shot). Invented.", true)]
        public const float FireLoopChainIntervals = 1.6f;
        private float _lastShotTime = -1f;
        private bool _fireHeld;
        private DD.Prototype.Weapons.WeaponController _weaponController;

        private bool CurrentSetHasFireLoop()
        {
            int k = WeaponAnimSet != null ? _setKeys.IndexOf(WeaponAnimSet) : -1;
            return k >= 0 && _animation.WeaponSets[_setIndex[k]].hasFireLoop;
        }

        private float FireIntervalSeconds => _weaponDef != null && _weaponDef.fire != null && _weaponDef.fire.rpm > 0f ? 60f / _weaponDef.fire.rpm : 0.1f;

        /// <summary>WP-071 r2: the TP v3 ADS weight (the gameplay's linear ClassicWeaponRuntime.Ads01) and the end of a sustained string.</summary>
        private void TickV3Weapon()
        {
            if (_weapon != CharacterAnimatorContract.WeaponPoseV3) { _animator.SetFloat(AdsWeightId, 0f); SetFireHeld(false); return; }
            if (_weaponController == null) _weaponController = GetComponentInParent<DD.Prototype.Weapons.WeaponController>();
            DD.Prototype.Weapons.ClassicWeaponRuntime classic = _weaponController != null ? _weaponController.Classic : null;
            _animator.SetFloat(AdsWeightId, _swHold == 0 && classic != null ? classic.Ads01 : 0f);
            if (_fireHeld && Time.time - _lastShotTime > FireLoopChainIntervals * FireIntervalSeconds) SetFireHeld(false);
        }

        private void SetFireHeld(bool on)
        {
            if (_fireHeld == on) return;
            _fireHeld = on;
            _animator.SetBool(FireHeldId, on);
        }

        private bool CurrentSetHasCycle()
        {
            int k = WeaponAnimSet != null ? _setKeys.IndexOf(WeaponAnimSet) : -1;
            return k >= 0 && _animation.WeaponSets[_setIndex[k]].hasCycle;
        }

        /// <summary>Equip: the v3 set when the weapon's AnimSet has one, else the legacy class pose.</summary>
        public void EquipWeapon(DD.Prototype.Core.Weapons.WeaponDefinition def, ClassicWeaponId id)
        {
            CancelReload();
            _weaponDef = def;
            _classicId = id;
            if (_holdIk != null) _holdIk.SetWeapon(id);   // WP-083 B2: the per-weapon hold row
            if (_gripIk != null)
            {
                // WP-083 B2f (council 06.10; b2f15): the support hand grasps the weapon's element — not on lmg_2 / rl_1's vertical
                // foregrips, whose authored grip already wraps them, nor on smg_1 (AD MUST-1: its clip's forearm clamps the wrap)
                _gripIk.SetGraspElement(WeaponGripIk.ElementOf(id));
                _gripIk.SetGrasp(WeaponGripIk.GraspOf(id));
            }
            _specialV3 = false;   // a classic equip ends a v3 Special hold
            _lastShotTime = -1f;   // WP-071 r2: a sustained string never carries over to the next weapon
            if (_animator != null) SetFireHeld(false);
            _burstStart = -1f;
            int k = def != null && TpV3On(def.AnimSet) && _runtimeOverrides != null ? _setKeys.IndexOf(def.AnimSet) : -1;
            ApplyFpArmsSet(force: true);
            if (k < 0)
            {
                WeaponAnimSet = null;
                if (_gripIk != null) _gripIk.SetHandInWeapon(false, Vector3.zero);
                SetWeapon(DD.Prototype.Core.Weapons.Arsenal.LegacyPoseIndex(id));
                return;
            }

            if (WeaponAnimSet != _setKeys[k]) _runtimeOverrides.ApplyOverrides(_setOverrides[k]);
            WeaponAnimSet = _setKeys[k];
            CharacterAnimationSet.WeaponAnimSet ws = _animation.WeaponSets[_setIndex[k]];
            if (_gripIk != null) _gripIk.SetHandInWeapon(ws.hasLeftHand, ws.leftHandInWeapon);
            SetWeapon(CharacterAnimatorContract.WeaponPoseV3);
        }

        private ClassicWeaponId _classicId;
        private bool _specialV3;
        private bool _feelSubscribed, _specialReload, _specialReloadRestoreLegacy;
        private SpecialWeaponId _specialReloadId;
        private readonly WeaponMagazineOwnership _specialMagazine = new WeaponMagazineOwnership();
        private readonly WeaponMagazineVisual _specialMagazineVisual = new WeaponMagazineVisual();
        private readonly float[] _specialMarks = new float[WeaponMagazineOwnership.StepCount];
        public bool SpecialReloadPresentationActive => _specialReload;
        public WeaponMagazineOwnership SpecialMagazine => _specialMagazine;
        public WeaponMagazineVisual SpecialMagazineVisual => _specialMagazineVisual;

        /// <summary>True while a Special plays through its TP v3 set (<see cref="EquipSpecialV3"/>).</summary>
        public bool SpecialV3Active => _specialV3;

        /// <summary>
        /// WP-071 M-48 (C-00 30.09): the Special's TP v3 set (AP-022 sw_arc / sw_gel, SpecialWeaponKeys.Of) when it is switched on
        /// (TpV3Sets / V3WeaponSets — OFF by default until the right-grip AD and C-00's decision): SwHold 0 → the set's overrides →
        /// the v3 pose. The classic weapon's definition, burst clock and FP arms are not touched; the classic id is kept for the
        /// way back. Returns false (the SwHold interim pose stays) when the set is off or missing.
        /// </summary>
        public bool EquipSpecialV3(SpecialWeaponId id, bool reloadOnly = false)
        {
            string key = DD.Prototype.Core.Weapons.SpecialWeaponKeys.Of(id);
            int k = key != null && (TpV3On(key) || reloadOnly) && _runtimeOverrides != null ? _setKeys.IndexOf(key) : -1;
            if (k < 0) return false;
            SetSpecialHold(0);
            if (_gripIk != null) _gripIk.SetGrasp(false);   // WP-083 B2f: the grasp is the classic handguard's
            if (WeaponAnimSet != _setKeys[k]) _runtimeOverrides.ApplyOverrides(_setOverrides[k]);
            WeaponAnimSet = _setKeys[k];
            CharacterAnimationSet.WeaponAnimSet ws = _animation.WeaponSets[_setIndex[k]];
            if (_gripIk != null) _gripIk.SetHandInWeapon(ws.hasLeftHand, ws.leftHandInWeapon);
            SetWeapon(CharacterAnimatorContract.WeaponPoseV3);
            _specialV3 = true;
            return true;
        }

        /// <summary>WP-071 B4b: the reload the gameplay started is the empty one (its duration is the definition's reloadEmptySeconds — the event carries only the duration; S-02 ClassicWeaponRuntime picks tac / empty).</summary>
        private bool IsEmptyReload(float seconds)
        {
            if (_weaponDef == null || _weaponDef.ammo == null) return false;
            return Mathf.Abs(seconds - _weaponDef.ammo.reloadEmptySeconds) < Mathf.Abs(seconds - _weaponDef.ammo.reloadTacSeconds);
        }
        private void OnReloadStarted(float duration) => PlayReload(duration, WeaponEvents.LastReloadEmpty);   // S-02 d3f3b52e: written just before the event
        private void OnReloadInterrupted() => CancelReload();
        private void OnSpecialEnded() { CancelReload(); SetSpecialHold(0); }

        private void OnFeelReloadStarted(FeelReload e)
        {
            if (!e.Special || !_authored || _dying || !isActiveAndEnabled) return;
            WeaponController controller = WeaponRegistry.ActiveController;
            if (controller == null || !controller.SpecialWeaponActive || controller.Special.Id != e.SpecialId) return;
            string key = DD.Prototype.Core.Weapons.SpecialWeaponKeys.Of(e.SpecialId);
            if (key != "sw_arc" && key != "sw_gel") return;
            bool restoreLegacy = !_specialV3;
            CancelReload();
            if ((!_specialV3 || WeaponAnimSet != key) && !EquipSpecialV3(e.SpecialId, reloadOnly: true)) return;
            _specialReloadRestoreLegacy = restoreLegacy;
            _specialReload = true;
            _specialReloadId = e.SpecialId;
            _specialMagazineVisual.Bind(controller.SpecialRigBody, key);
            _specialMagazine.Begin(tactical: !e.Empty);
            _specialMagazine.Stepped += OnSpecialMagazineStep;
            CharacterAnimationSet.Entry entry = _animation.Find(CharacterAnimatorContract.PlayerWeaponClipV3(key, e.Empty ? "reload_empty" : "reload_tac"));
            for (int i = 0; i < _specialMarks.Length; i++) _specialMarks[i] = WeaponMagazineOwnership.MagazineMarks[i];
            if (entry?.clip != null && entry.clip.length > 0f)
                foreach (AnimationEvent beat in entry.clip.events)
                    if (ReloadPartCalibration.TryStep(CharacterAnimationEvents.Parse(beat.functionName), out MagazineStep step))
                        _specialMarks[(int)step] = beat.time / entry.clip.length;
            // mag_hand_attach is omitted in v009: keep it BETWEEN detach and insert, not before the authored detach.
            _specialMarks[(int)MagazineStep.HandAttach] = Mathf.Lerp(_specialMarks[0], _specialMarks[2], 0.5f);
            PlayReload(e.DurationSeconds, e.Empty);
        }
        private void OnFeelReloadEnded(FeelReload e)
        {
            if (e.Special && _specialReload && e.SpecialId == _specialReloadId) CancelReload();
        }
        private void OnSpecialMagazineStep(MagazineStep step, bool fromEvent)
        {
            if (step == MagazineStep.Detach || step == MagazineStep.HandAttach)
            {
                // The FP Special has no arm/part calibration. Keep its model complete in FP.
                if (DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson)
                    _specialMagazineVisual.ToHand(_magSocket);
            }
            else _specialMagazineVisual.Home();
        }
        private void EndSpecialReload()
        {
            bool restoreLegacy = _specialReload && _specialReloadRestoreLegacy;
            _specialReloadRestoreLegacy = false;
            _specialReload = false;
            _specialMagazine.Stepped -= OnSpecialMagazineStep;
            _specialMagazine.End();
            _specialMagazineVisual.Home();
            if (restoreLegacy)
            {
                _specialV3 = false;
                EquipWeapon(_weaponDef, _classicId);
                WeaponController c = WeaponRegistry.ActiveController;
                if (c != null && c.SpecialWeaponActive && c.Special.Id == _specialReloadId) SetSpecialHold(HoldIndexOf(_specialReloadId));
            }
        }
        private void TickSpecialReload()
        {
            if (!_specialReload) return;
            WeaponController c = WeaponRegistry.ActiveController;
            if (_dying || c == null || !c.SpecialWeaponActive || c.Special.Id != _specialReloadId || !c.Special.IsReloading)
            {
                CancelReload();
                return;
            }
            if (DD.Prototype.CameraSystem.CameraModeService.Current == DD.Prototype.CameraSystem.CameraMode.FirstPerson)
                _specialMagazineVisual.Home();
            _specialMagazine.Tick(c.Special.ReloadProgress01, c.Special.ReloadDuration, _specialMarks);
        }

        /// <summary>WP-032E: the usage bus is the authoritative Special beat surface (WP-056); the HUD-state path above stays as the fallback for a Special without usage events.</summary>
        private void OnSpecialUsage(SpecialUsageEvent e)
        {
            switch (e.Kind)
            {
                case SpecialUsageKind.Activated: if (!EquipSpecialV3(e.Id)) SetSpecialHold(HoldIndexOf(e.Id)); break;
                // Fired: the swing is already fired through IWeaponHost.OnWeaponFired → PlayerProceduralAnimator.PlayFireRecoil → PlayFire()
                // (WeaponController.FireSpecialOnce keeps that call); a second trigger here would only re-arm the same beat.
                case SpecialUsageKind.Deactivated: CancelReload(); SetSpecialHold(0); break;
            }
        }

        /// <summary>SwHold index of a Special: 1 Kagura Bell (sweep), 2 Ofuda Loom (cast); any other Special uses the bell hold until AP-009 ships its clip.</summary>
        // WP-069 B4 interim (S-02, until the AP-022 sw_arc / sw_gel clips): Raiju on the bell hold, Namekuji on the loom hold.
        public static int HoldIndexOf(SpecialWeaponId id) => id == SpecialWeaponId.Namekuji ? 2 : 1;

        private void OnSpecialChanged(SpecialWeaponHudState state)
        {
            // The hold pose follows the ACTIVE special (the pickup clip's pickup_done frame is when the hand closes on it).
            if (state.Phase == SpecialWeaponPhase.Active) SetSpecialHold(HoldIndexOf(state.WeaponId));
            else if (state.Phase == SpecialWeaponPhase.None) SetSpecialHold(0);
            DD.Prototype.Core.Weapons.WeaponDefinition special = state.Phase == SpecialWeaponPhase.Active ? SpecialWeaponConfigSection.DefinitionOf(state.WeaponId) : null;
            if (special != _fpSpecialDef && (state.Phase == SpecialWeaponPhase.Active || state.Phase == SpecialWeaponPhase.None))
            {
                _fpSpecialDef = special;
                ApplyFpArmsSet(force: false);
            }
        }

        // WP-069 B5-9 (C-00 30.09, option C): a weapon whose presentation.fpArms is off shows no FP arms — the classic in hand
        // (EquipWeapon) and the active Special alike (a Special with fpArms off hides them while it is out; the classic's set
        // returns when it ends). The set is re-applied only when it changes (SpecialChanged is published with the HUD state).
        private DD.Prototype.Core.Weapons.WeaponDefinition _fpSpecialDef;
        private string _fpSetApplied = "\0";

        private void ApplyFpArmsSet(bool force)
        {
            if (_fpArms == null) return;
            DD.Prototype.Core.Weapons.WeaponDefinition def = _weaponDef;
            bool off = _fpSpecialDef != null && !_fpSpecialDef.FpArms;
            string set = !off && FirstPersonV3Arms && def != null && def.FpArms ? def.AnimSet : null;
            if (!force && set == _fpSetApplied) return;
            _fpSetApplied = set;
            _fpArms.SetWeaponSet(set, def != null ? def.FpViewPose.scale : 1f);
        }

        // ------------------------------------------------------------- WP-032C weapon handling

        /// <summary>The classic weapon held (ClassicWeaponId index): Aim_<w> is the upper rest, Fire_<w> the recoil, Reload_<w> / Switch_<w> the one-shots.</summary>
        public void SetWeapon(int weapon)
        {
            _weapon = Mathf.Clamp(weapon, 0, CharacterAnimatorContract.WeaponPoseV3);
            if (_authored) _animator.SetInteger(WeaponId, _weapon);
        }

        /// <summary>Reload: the weapon's clip rate-scaled so mag_out / mag_in / chamber land inside <paramref name="seconds"/> (WeaponConfig.ReloadTime).</summary>
        public void PlayReload(float seconds) => PlayReload(seconds, IsEmptyReload(seconds));

        /// <summary>Reload with the gameplay's tac / empty verdict (<paramref name="empty"/>; S-02 WeaponEvents.LastReloadEmpty).</summary>
        public void PlayReload(float seconds, bool empty)
        {
            if (!_authored || _dying) return;
            ReloadPartCalibration.ConfigureSocket(_magSocket, WeaponAnimSet);
            if (!_specialV3 && _fpArms != null) _fpArms.Reload(seconds, empty);   // the FP arms' own set, whatever the TP path
            if (_weapon == 3) return;
            string clip;
            if (_weapon == CharacterAnimatorContract.WeaponPoseV3)
            {
                _animator.SetBool(ReloadEmptyId, empty);
                clip = CharacterAnimatorContract.PlayerWeaponClipV3(WeaponAnimSet, empty ? "reload_empty" : "reload_tac");
            }
            else clip = CharacterAnimatorContract.PlayerWeaponClip(_weapon, "reload");
            CharacterAnimationSet.Entry entry = _animation.Find(clip);
            float clipSeconds = entry?.seconds ?? 0f;
            _animator.SetFloat(ReloadRateId, seconds > 0.05f && clipSeconds > 0.05f ? clipSeconds / seconds : 1f);
            _animator.SetTrigger(ReloadId);
            Matrix4x4? partMatrix = ReloadPartCalibration.TryGet(WeaponAnimSet, out Matrix4x4 m) ? m : (Matrix4x4?)null;
            if (_contactIk != null) _contactIk.BeginReload(seconds, entry?.clip, partMatrix, _specialReload ? _specialMagazine : null);
        }

        /// <summary>Reload interrupted (special pickup / death): straight back to the rest pose.</summary>
        public void CancelReload()
        {
            EndSpecialReload();
            if (!_authored || _animator == null) return;
            if (_contactIk != null) _contactIk.CancelReload();
            if (_fpArms != null) _fpArms.CancelContact();
            _animator.ResetTrigger(ReloadId);
            AnimatorStateInfo over = ResolveOverrideLayer() >= 0 ? _animator.GetCurrentAnimatorStateInfo(_overrideLayer) : default;
            if (_overrideLayer >= 0 && (over.IsName(CharacterAnimatorContract.ReloadState + "_" + _weapon) || over.IsName(CharacterAnimatorContract.ReloadTacState) || over.IsName(CharacterAnimatorContract.ReloadEmptyState)))
            {
                _animator.CrossFadeInFixedTime(RestStateName(), 0.1f, _overrideLayer);
            }
        }

        /// <summary>Weapon switch: the outgoing clip (switch_away event), then <see cref="SetWeapon"/> + <see cref="PlaySwitchIn"/> when S-02 swaps the mount.</summary>
        public void PlaySwitchOut() { if (_authored && !_dying) { _animator.SetTrigger(SwitchOutId); if (_fpArms != null) _fpArms.SwitchOut(); } }
        public void PlaySwitchIn() { if (_authored && !_dying) { _animator.SetTrigger(SwitchInId); if (_fpArms != null) _fpArms.SwitchIn(); } }
        /// <summary>Weapon lowered / raised (lowered / raised events) — menu / results hand-off beats.</summary>
        public void PlayLower() { if (_authored && !_dying) _animator.SetTrigger(LowerId); }
        public void PlayRaise() { if (_authored && !_dying) _animator.SetTrigger(RaiseId); }

        /// <summary>Special Weapon in the right hand: 0 none, 1 Kagura Bell, 2 Ofuda Loom — the SwHold loop replaces the Aim until it ends.</summary>
        public void SetSpecialHold(int hold)
        {
            // WP-071 M-48: a v3 Special plays on SwHold 0 through its own set — the HUD-state path's hold index is ignored while it
            // is out, and every end path (Deactivated / Ended / Cleared / Phase None → hold 0) brings the classic set back
            if (_specialV3 && hold > 0) return;
            if (_specialV3 && hold == 0)
            {
                _specialV3 = false;
                EquipWeapon(_weaponDef, _classicId);
            }

            _swHold = Mathf.Clamp(hold, 0, 2);
            if (_authored) _animator.SetInteger(SwHoldId, _swHold);
            if (_gripIk != null && !_specialV3) _gripIk.SetGrasp(_swHold == 0 && WeaponGripIk.GraspOf(_classicId));   // WP-083 B2f: the classic weapon's
        }

        /// <summary>Damage taken with a v003 read: upper = hurt_A (from the right, override_upper), else hurt_B (base, gut hit with a step). Movement, aim and fire untouched (REQ-044/045).</summary>
        public void PlayHurt(bool upperBody)
        {
            if (!_authored || _dying) return;
            _animator.SetInteger(HurtStyleId, upperBody ? 0 : 1);
            _animator.SetTrigger(HurtId);
        }

        private string RestStateName()
        {
            if (_swHold > 0) return CharacterAnimatorContract.SwHoldState + "_" + _swHold;
            if (_weapon == CharacterAnimatorContract.WeaponPoseV3) return CharacterAnimatorContract.AimV3State;
            return _weapon <= 2 ? CharacterAnimatorContract.AimState + "_" + _weapon : CharacterAnimatorContract.EmptyState;
        }

        // ------------------------------------------------------------------ driver hooks

        /// <summary>Weapon fired: the additive recoil clip (once per trigger pull; stacks naturally by re-trigger). WP-032E: while a Special Weapon is held (SwHold 1/2) the use beat is the generated swing (SwFire_<hold>) instead of the classic recoil.</summary>
        public void PlayFire()
        {
            if (!_authored || _dying) return;
            // WP-071 B4b: a burst weapon's v3 fire clip (TP and FP) is the whole burst (P-06 ar_2: 3 kicks) — only the burst's first shot
            // starts it. The shots arrive on frame boundaries (a 60 Hz frame can push the last one past count × interval): the window
            // reaches half-way into the gap before the next burst (burstDelaySeconds), which frame jitter never crosses.
            bool burstBeat = true;
            if (_swHold == 0 && _weaponDef != null && _weaponDef.fire != null && _weaponDef.fire.fireMode == DD.Prototype.Core.Weapons.WeaponFireMode.Burst)
            {
                float now = Time.time, window = Mathf.Max(0, _weaponDef.fire.burstCount - 1) * _weaponDef.fire.burstIntervalSeconds + 0.5f * _weaponDef.fire.burstDelaySeconds;
                burstBeat = !(_burstStart >= 0f && now - _burstStart <= window);
                if (burstBeat) _burstStart = now;
            }

            if (_swHold == 0 && _fpArms != null && (burstBeat || _fpArms.WeaponSet == null)) _fpArms.Fire();
            if (_swHold == 0 && _weapon == CharacterAnimatorContract.WeaponPoseV3 && !burstBeat) return;   // the legacy TP recoil stays per shot
            // WP-071 r2: a sustained LMG string loops fire_loop (FireHeld) instead of re-triggering the per-shot kick
            if (_swHold == 0 && _weapon == CharacterAnimatorContract.WeaponPoseV3 && CurrentSetHasFireLoop())
            {
                float now = Time.time;
                bool chained = _lastShotTime >= 0f && now - _lastShotTime <= FireLoopChainIntervals * FireIntervalSeconds;
                _lastShotTime = now;
                if (chained) { SetFireHeld(true); FireTriggers++; return; }
            }

            FireTriggers++;
            _animator.SetTrigger(_swHold > 0 ? SwFireId : FireId);
            if (_swHold == 0 && _weapon == CharacterAnimatorContract.WeaponPoseV3 && CurrentSetHasCycle()) _animator.SetTrigger(CycleId);   // WP-071 B4c: the sniper's bolt
        }

        /// <summary>Damage taken: the upper-body hit clip. Never touches movement, aim or fire (REQ-044/045).</summary>
        public void PlayHit()
        {
            if (!_authored || _dying) return;
            _animator.SetTrigger(HitId);
        }

        /// <summary>
        /// One-way death. <paramref name="localHitDirection"/> = where the killing hit pushed the body
        /// (character space): forward → death_fwd, backward → death_back, else <paramref name="coin"/>.
        /// </summary>
        public void PlayDeath(Vector3 localHitDirection, bool coin)
        {
            if (!_authored || _dying) return;
            CancelReload();
            _dying = true;
            _deathEnded = false;
            SetFireHeld(false);   // WP-071 r2: a dying body never keeps the LMG fire loop
            // WP-032C: a sideways push (|x| dominant) is the v003 death_side (2); the coin only breaks a dead-centre tie.
            int style = Mathf.Abs(localHitDirection.x) > Mathf.Abs(localHitDirection.z) && Mathf.Abs(localHitDirection.x) > 0.2f ? 2
                : localHitDirection.z > 0.2f ? 0 : localHitDirection.z < -0.2f ? 1 : coin ? 0 : 1;
            _animator.SetBool(AliveId, false);
            _animator.SetInteger(DeathStyleId, style);
            _animator.SetTrigger(DieId);
        }

        /// <summary>Special Weapon picked up (WP-007 event): the grab clip on the upper-body override layer (WP-032B · DEC-082 — the legs keep walking, nothing locks the motor).</summary>
        public void PlayPickup()
        {
            if (!_authored || _dying) return;
            _animator.SetTrigger(PickupId);
        }

        /// <summary>Albedo swap for a skin (index = PlayerSkin). Same material asset for every skin; one property block per renderer.</summary>
        public void ApplySkin(int skinIndex, Texture2D albedo)
        {
            if (albedo == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                SkinnedMeshRenderer r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetTexture(BaseMapId, albedo);
                r.SetPropertyBlock(_block);
            }

            _appliedSkin = skinIndex;
            if (_fpArms != null) _fpArms.SyncSkin();
        }

        /// <summary>The albedo currently bound on the first renderer (tests).</summary>
        public Texture BoundAlbedo()
        {
            if (_renderers.Length == 0 || _renderers[0] == null) return null;
            if (_block == null) _block = new MaterialPropertyBlock();
            _renderers[0].GetPropertyBlock(_block);
            return _block.GetTexture(BaseMapId);
        }

        private float _speed;

        private void Update()
        {
            TickSpecialReload();
            if (_animator == null || _animation == null) return;
            float dt = Time.deltaTime;
            Vector3 v = _planarVelocity != null ? _planarVelocity() : Vector3.zero;
            float mps = v.magnitude;
            if (_lean != null) _lean.Feed(v);

            if (!_authored)
            {
                float target = _animation.SpeedParameterFor(mps);
                _speed = dt > 0f ? Mathf.MoveTowards(_speed, target, dt / MoveSmoothing) : target;
                _animator.SetFloat(SpeedId, _speed);
                return;
            }

            if (_dying) return;
            // WP-082 R3: the parameter set is looked up once per controller (Attach, the runtime override controller, a late init)
            if (_animator.runtimeAnimatorController != _scannedController || (!_scannedInitialized && _animator.isInitialized)) ScanParameters();
            TickV3Weapon();
            MuteTpAdditiveInFirstPerson();

            _mps = mps;
            _stillFor = mps < StillSpeed ? _stillFor + dt : 0f;
            StillRequested = _stillFor >= StillHoldSeconds;
            if (_hasStill) _animator.SetBool(StillId, StillRequested);
            if (_loco8) TickLocomotion8(v, mps, dt);
            else TickLocomotionLegacy(v, mps, dt);
            if (_hasV4) TickV4(v, mps, dt);
            _ = _referenceSpeed;   // the motor's live top speed stays available for a follow-up run cycle
        }

        /// <summary>
        /// The AP-022 4-way controller (no LocoSpeed): local velocity over the set's reference speed on the 2-D blend axes. The
        /// child time scales were baked against the same reference, so |v| / reference is the exact no-slide rate at the blend
        /// corners; near idle the rate eases back to 1 so the idle breath does not freeze.
        /// </summary>
        private void TickLocomotionLegacy(Vector3 v, float mps, float dt)
        {
            float reference = _animation.LocomotionReferenceSpeed;
            Vector3 local = transform.InverseTransformDirection(v) / reference;
            Vector2 targetMove = new Vector2(Mathf.Clamp(local.x, -1f, 1f), Mathf.Clamp(local.z, -1f, 1f));
            float speed01 = Mathf.Clamp01(mps / reference);
            // WP-032B: the run ring's clip rate is LocoRate × its child time scale; cap it at MaxRunRate (P-06).
            // WP-071 MUST-3 (P-06 AD refuter B): inside the walk ring the walk plays at its authored rate 1 (LocoRate = the ring radius along
            // the move direction × the child's time scale = 1) and the blend weight shortens the stride; outside it |v| / reference is honest.
            // The old Lerp(1, s, s) played the AP-022 walk ring at 1.80× (406 spm at 2.6 m/s).
            WalkRingRadius = RingAlong(targetMove, _walkSpeed) / reference;
            float targetRate = Mathf.Min(Mathf.Max(speed01, WalkRingRadius), MaxRunRate / _runTimeScale);
            if (dt > 0f)
            {
                _move = Vector2.MoveTowards(_move, targetMove, dt / MoveSmoothing);
                _rate = Mathf.MoveTowards(_rate, targetRate, dt / MoveSmoothing);
            }
            else
            {
                _move = targetMove;
                _rate = targetRate;
            }

            _animator.SetFloat(MoveXId, _move.x);
            _animator.SetFloat(MoveYId, _move.y);
            // Child time scales in the blend tree are reference / clip speed_mps, so the state
            // speed is simply |v| / reference (idle at 0 has no foot to slide).
            _animator.SetFloat(LocoRateId, Mathf.Max(_rate, 0.05f));
        }

        /// <summary>
        /// WP-082 R3 (LOCO-SOZLESME §4.1, §5): the AP-026 8-way tree — LocoSpeed = |v| (m/s, unsmoothed: the motor is continuous and
        /// a lag is a slide); MoveX / MoveY = the body-space unit direction after the hip hysteresis (±<see cref="HipDeadBandDegrees"/>
        /// around ±90°, held <see cref="HipHoldSeconds"/>) and the angle smoothing (<see cref="DirectionDegreesPerSecond"/>);
        /// LocoRate = clamp(v / honest speed, CadenceBand) with honest = Σ w·S / Σ w·T over the blend's own weights. Zero allocation.
        /// </summary>
        private void TickLocomotion8(Vector3 v, float mps, float dt)
        {
            if (mps >= StandSpeed)
            {
                Vector3 local = transform.InverseTransformDirection(v);
                float theta = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;   // 0 forward, +90 right (LocomotionV4.Direction8)
                float a = Mathf.Abs(theta);
                if (!_hasHeading) { _fore = a <= HipCenterDegrees; _hemiHold = 0f; }
                bool wantFore = a < HipCenterDegrees - HipDeadBandDegrees ? true : a > HipCenterDegrees + HipDeadBandDegrees ? false : _fore;
                if (wantFore != _fore)
                {
                    _hemiHold += dt;
                    if (_hemiHold >= HipHoldSeconds) { _fore = wantFore; _hemiHold = 0f; }
                }
                else _hemiHold = 0f;

                // θ_eff: the fore hemisphere never weighs a back_* child (and the aft never a fwd_*); at its edge only left / right.
                // On the clamped edge the side is kept (a straight-back travel in the fore hemisphere would flip ±90 every frame).
                bool clamped = _fore ? a >= HipCenterDegrees : a <= HipCenterDegrees;
                int side = theta > 0f ? 1 : theta < 0f ? -1 : _clampSide;
                if (clamped) side = _clampSide; else _clampSide = side;
                float eff = side * (_fore ? Mathf.Min(a, HipCenterDegrees) : Mathf.Max(a, HipCenterDegrees));
                if (!_hasHeading || dt <= 0f) _treeAngle = eff;
                else _treeAngle = Mathf.DeltaAngle(0f, Mathf.MoveTowardsAngle(_treeAngle, eff, DirectionDegreesPerSecond * dt));
                _hasHeading = true;
            }

            float rad = _treeAngle * Mathf.Deg2Rad;
            _move = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            _honest = HonestSpeedOf(mps, _treeAngle);
            _rate = mps < StandSpeed || _honest < 0.01f ? 1f : CadenceBand.Clamp(mps / _honest);
            WalkRingRadius = 0f;   // the AP-022 walk ring's readout; the 8-way tree has no ring radius
            _animator.SetFloat(LocoSpeedId, mps);
            _animator.SetFloat(MoveXId, _move.x);
            _animator.SetFloat(MoveYId, _move.y);
            _animator.SetFloat(LocoRateId, _rate);
        }

        /// <summary>
        /// WP-082 R3 §4.1: the honest speed of the tree at (<paramref name="mps"/>, <paramref name="angle"/>) — Simple1D weights over
        /// the slot thresholds (linear, clamped at the ends) × the two neighbouring direction children's angular weights; stand
        /// slot S = 0, T = walk_fwd. Also keeps Σ w·T (the blend's cycle seconds) for the fallback cross-fade offsets.
        /// </summary>
        private float HonestSpeedOf(float mps, float angle)
        {
            if (!_ringsReady) { _blendCycle = 1f; return mps; }
            int lo = 0;
            float w = 0f;
            if (mps >= _ringThreshold[3]) { lo = 2; w = 1f; }
            else if (mps > _ringThreshold[0])
            {
                for (int i = 0; i < 3; i++)
                {
                    if (mps > _ringThreshold[i + 1]) continue;
                    lo = i;
                    float span = _ringThreshold[i + 1] - _ringThreshold[i];
                    w = span > 1e-4f ? (mps - _ringThreshold[i]) / span : 1f;
                    break;
                }
            }

            float f = angle / 45f;
            f -= Mathf.Floor(f / 8f) * 8f;
            int j0 = (int)f;
            if (j0 > 7) j0 = 7;
            int j1 = (j0 + 1) & 7;
            float wj = f - j0;
            float num = 0f, den = 0f;
            for (int s = 0; s < 2; s++)
            {
                int slot = lo + s;
                float ws = s == 0 ? 1f - w : w;
                if (ws <= 0f) continue;
                if (slot == 0) { den += ws * _standCycle; continue; }
                int b = (slot - 1) * 8;
                num += ws * ((1f - wj) * _ringStride[b + j0] + wj * _ringStride[b + j1]);
                den += ws * ((1f - wj) * _ringCycle[b + j0] + wj * _ringCycle[b + j1]);
            }

            _blendCycle = den > 1e-4f ? den : _standCycle;
            return den > 1e-4f ? num / den : 0f;
        }

        /// <summary>
        /// WP-082 R3 (§6–§8): the v4 event (top-down: no pivot / turn — <see cref="LocomotionV4Events.AimDriven"/>), its motion time
        /// (<see cref="LocomotionV4.TimeParam"/> written before <see cref="LocomotionV4.ClipParam"/>, the same frame), and the exits.
        /// </summary>
        private void TickV4(Vector3 v, float mps, float dt)
        {
            _v4.AimDriven = !DD.Prototype.CameraSystem.CameraModeService.IsLookMode;
            _v4.ContractLead = true;   // S-06 B3: never IsLocked(0) — the foot lock is closed at rest, every start read "left"
            _v4.Tables = _v4Tables;
            _v4.PhaseNow = CurrentLoopPhase();
            // one frame per event: the AnyState transition reads it this update, None clears it the next (no trigger left armed)
            int id = _v4.Feed(v, _commandedVelocity != null ? _commandedVelocity() : (Vector3?)null, transform.rotation, _referenceSpeed != null ? _referenceSpeed() : 0f,
                false, _v4GaitSpeeds, dt);
            if (id != LocomotionV4.None)
            {
                if (_hasV4Time && _v4Tables != null && _v4Tables.Has(id)) _v4Clock.Begin(id, _v4.EntryT, _v4Tables.Seconds(id), transform.position);
                else _v4Clock.End();
                _v4Exiting = _v4FadeIssued = false;
                _v4Frames = 0;
            }
            else if (_v4Clock.Current != LocomotionV4Clock.Kind.None) TickV4Clock(mps, dt);

            if (_hasV4Time) _animator.SetFloat(V4TimeId, _v4Clock.Current != LocomotionV4Clock.Kind.None ? _v4Clock.T : 0f);
            _animator.SetInteger(V4ClipId, id);
        }

        private void TickV4Clock(float mps, float dt)
        {
            int id = _v4Clock.Id;
            int hash = LocomotionV4.StateHash(id);
            bool inTransition = _animator.IsInTransition(0);
            bool current = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash;
            bool entering = inTransition && _animator.GetNextAnimatorStateInfo(0).shortNameHash == hash;
            _v4Frames++;
            if (_v4Exiting)
            {
                // the exit: the outgoing clip keeps its own rate through the blend; the fallback cross-fade when the motion-time state
                // did not take its exit-time transition within a frame (LOCO-SOZLESME §2.4, [ölçülmedi])
                if (!current && !entering) { _v4Clock.End(); _v4Exiting = false; return; }
                _v4Clock.Coast(dt);
                if (!_v4FadeIssued && !inTransition && _v4Frames > 2) CrossFadeToLocomotion(_v4ExitSeconds, _v4ExitOffset);
                return;
            }

            // the state never came (no state for the clip, or the base layer was taken — hurt / death): no v4 time
            if (!current && !entering && _v4Frames > 2) { _v4Clock.End(); return; }
            LocomotionV4Clock.Step step = _v4Clock.Tick(_v4Tables, transform.position, mps, _v4.Deceleration, _v4Tables.ExitT(id), dt);
            if (step == LocomotionV4Clock.Step.Cancel)
            {
                // §7.2: the body did not go (a wall) — back to Locomotion (the stand slot: no phase)
                CrossFadeToLocomotion(V4CancelSeconds, 0f);
            }
            else if (step == LocomotionV4Clock.Step.Exit)
            {
                bool start = _v4Clock.Current == LocomotionV4Clock.Kind.Start;
                _v4ExitOffset = start ? _v4Tables.ExitOffset(id) : 0f;
                _v4ExitSeconds = start ? V4StartExitSeconds : V4StopExitSeconds;
                _v4Exiting = true;
                _v4FadeIssued = false;
                _v4Frames = 0;
            }
            else if (_v4Clock.Current == LocomotionV4Clock.Kind.Stop && _v4.IsMoving)
            {
                // §8.4: commanded again during the stop (not a reversal, so no new start) — back to the loop at the stop pose's phase
                CrossFadeToLocomotion(V4StopExitSeconds, _v4Tables.PhaseAt(id, _v4Clock.T));
            }
        }

        /// <summary>
        /// Base layer → Locomotion at a loop phase (normalised; converted to the state's seconds with the blend's cycle Σ w·T). The
        /// outgoing clip keeps its own rate until the base layer has left it (V4Time is not dropped to 0 under the blend).
        /// </summary>
        private void CrossFadeToLocomotion(float seconds, float phase)
        {
            float cycle = _loco8 ? _blendCycle : 1f;
            _animator.CrossFadeInFixedTime(LocomotionStateHash, seconds, 0, Mathf.Repeat(phase, 1f) * cycle);
            _v4Exiting = true;
            _v4FadeIssued = true;
            _v4Frames = 0;
        }

        /// <summary>§8.3: the loop phase (0 = foot_l) of the pose now — the playing v4 clip's table, else the Locomotion state's normalised time.</summary>
        private float CurrentLoopPhase()
        {
            if (_v4Clock.Current != LocomotionV4Clock.Kind.None && _v4Tables != null && _v4Tables.Has(_v4Clock.Id)) return _v4Tables.PhaseAt(_v4Clock.Id, _v4Clock.T);
            if (!_animator.isInitialized) return 0f;
            AnimatorStateInfo s = _animator.GetCurrentAnimatorStateInfo(0);
            return s.shortNameHash == LocomotionStateHash ? s.normalizedTime - Mathf.Floor(s.normalizedTime) : 0f;
        }

        /// <summary>WP-082 R3: the parameters this controller answers — once per controller (the scan allocates the parameter array).</summary>
        private void ScanParameters()
        {
            _scannedController = _animator != null ? _animator.runtimeAnimatorController : null;
            _scannedInitialized = _animator != null && _animator.isInitialized;
            bool was8 = _loco8;
            _hasStill = _hasV4 = _hasLocoSpeed = _hasV4Time = false;
            if (_scannedController != null)
            {
                foreach (AnimatorControllerParameter prm in _animator.parameters)
                {
                    if (prm.nameHash == StillId) _hasStill = true;
                    else if (prm.nameHash == V4ClipId) _hasV4 = true;
                    else if (prm.nameHash == LocoSpeedId) _hasLocoSpeed = true;
                    else if (prm.nameHash == V4TimeId) _hasV4Time = true;
                }
            }

            // a LocoSpeed controller is always fed on its own parameters (else its 1-D tree would sit in the stand slot); without
            // the set's ring tables LocoRate stays 1 and the foot lock stays closed
            _loco8 = _hasLocoSpeed;
            if (_loco8 && !_ringsReady && !_warnedNoRings)
            {
                _warnedNoRings = true;
                Debug.LogWarning("[PlayerSkinnedVisual] WP-082: the controller has " + CharacterAnimatorContract.LocoSpeed + " but the animation set has no 8-way ring tables — LocoRate 1, foot lock closed.");
            }
            if (_loco8 != was8) { _hasHeading = false; _fore = true; _hemiHold = 0f; _treeAngle = 0f; _clampSide = 1; }
        }

        /// <summary>
        /// WP-082 R3 (§4.2, §7.1, §11): the set's locomotion tables into flat arrays, once — the 1-D thresholds, stride × k and
        /// cycle seconds per ring child, the v4 tables by id and the v4 gait speeds (template × k; ≤ 0 = off → +∞).
        /// </summary>
        private void BindLocomotionTables()
        {
            _ringsReady = false;
            _v4Tables = null;
            CharacterAnimationSet.LocomotionTables t = _animation != null ? _animation.Locomotion : null;
            if (t == null) return;
            float k = t.heroStrideScale > 0.01f ? t.heroStrideScale : 1f;
            if (t.HasRings)
            {
                int filled = 0;
                for (int i = 0; i < _ringStride.Length; i++) { _ringStride[i] = 0f; _ringCycle[i] = 0f; }
                for (int i = 0; i < t.loops.Count; i++)
                {
                    CharacterAnimationSet.LocoLoop l = t.loops[i];
                    if (l == null || l.ringSlot < 1 || l.ringSlot > 3 || l.dirIndex < 0 || l.dirIndex > 7 || l.cycleSecondsNatural <= 0.01f) continue;
                    int b = (l.ringSlot - 1) * 8 + l.dirIndex;
                    if (_ringCycle[b] <= 0f) filled++;
                    _ringStride[b] = l.strideCycleMTemplate * k;
                    _ringCycle[b] = l.cycleSecondsNatural;
                }

                bool ordered = true;
                for (int i = 0; i < 4; i++) _ringThreshold[i] = t.ringThresholdMps[i];
                for (int i = 0; i < 3; i++) if (_ringThreshold[i + 1] <= _ringThreshold[i]) ordered = false;
                _standCycle = _ringCycle[0] > 0f ? _ringCycle[0] : 1f;   // walk_fwd (§2.1 slot 0)
                _ringsReady = filled == 24 && ordered;
            }

            if (t.v4 != null && t.v4.Count > 0) _v4Tables = new LocomotionV4Tables(t);
            if (t.v4GaitSpeedsMpsTemplate != null && t.v4GaitSpeedsMpsTemplate.Length >= _v4GaitSpeeds.Length)
            {
                for (int g = 0; g < _v4GaitSpeeds.Length; g++)
                {
                    float s = t.v4GaitSpeedsMpsTemplate[g];
                    _v4GaitSpeeds[g] = s > 0f ? s * k : float.PositiveInfinity;   // jog in R3: never chosen
                }
            }
        }

        /// <summary>WP-071 MUST-3/4: a ring's contact speed along a move direction (fwd / back by |y|, the strafe side by |x|).</summary>
        private static float RingAlong(Vector2 move, float[] speeds)
        {
            float ax = Mathf.Abs(move.x), ay = Mathf.Abs(move.y);
            if (ax + ay < 1e-4f) return speeds[0];
            float fb = move.y >= 0f ? speeds[0] : speeds[1];
            float side = move.x < 0f ? speeds[2] : speeds[3];
            return (ax * side + ay * fb) / (ax + ay);
        }

        /// <summary>
        /// The foot-lock gate. WP-082 R3 (LOCO-SOZLESME §10, the 8-way tree): moving, the base layer in Locomotion and not in a
        /// transition (closed through every v4 state — AP-026 runtime_foot_ik false), LocoRate ≤ CadenceBand.LockMaxRate and the slide
        /// share ≤ CadenceBand.SlideTopDown. WP-071 MUST-4 (e), the AP-022 4-way controller: the dominant ring child (walk inside
        /// the ring, run past the halfway to the run ring) plays at ≤ CadenceBand.LockMaxRate with a slide share ≤ <see cref="LockMaxSlide"/>.
        /// </summary>
        private bool LockGateOpen()
        {
            if (_loco8)
            {
                if (!_ringsReady || _mps < StillSpeed || _move.sqrMagnitude < 0.25f || !_animator.isInitialized) return false;
                if (_animator.IsInTransition(0) || _animator.GetCurrentAnimatorStateInfo(0).shortNameHash != LocomotionStateHash) return false;
                return _rate <= CadenceBand.LockMaxRate && CadenceBand.SlideFraction(_mps, _honest, _rate) <= CadenceBand.SlideTopDown;
            }

            if (_move.sqrMagnitude <= 0.004f || _mps < StillSpeed || _animation == null) return false;
            float reference = _animation.LocomotionReferenceSpeed;
            float walk = RingAlong(_move, _walkSpeed), run = RingAlong(_move, _runSpeed);
            float speed01 = _mps / reference;
            float runRadius = RunRingRadius(_move);   // D-1: run_back sits at the backward multiplier (×0.85)
            bool runDominant = speed01 > 0.5f * (walk / reference + runRadius);
            float spd = runDominant ? run : walk;
            float scale = runDominant ? RunChildTimeScale(reference, spd, runRadius) : ChildTimeScale(reference, spd);
            float clipRate = _rate * scale;
            // inside the walk ring the blend weight shortens the stride (the weighted planted foot carries |v|): no mean slide by construction
            float slide = !runDominant && _mps <= walk ? 0f : Mathf.Abs(_mps - clipRate * spd) / Mathf.Max(0.01f, _mps);
            return clipRate <= CadenceBand.LockMaxRate && slide <= LockMaxSlide;
        }

        /// <summary>Blend-tree child time scale that shows no foot slide at the reference speed (builder).</summary>
        public static float ChildTimeScale(float referenceSpeed, float clipSpeedMps)
        {
            return clipSpeedMps > 0.01f ? Mathf.Min(MaxLocoRate, Mathf.Max(0.1f, referenceSpeed) / clipSpeedMps) : 1f;
        }

        /// <summary>
        /// WP-071 D-1 (C-00 01.10, user: the backward multiplier — today ×0.85, PlayerConfigSection.BackwardSpeedMultiplier; S-06 B2):
        /// a run child at <paramref name="radius"/> of the reference (run_back at PlayerConfigSection.BackwardSpeedMultiplier) — its
        /// clip rate at that radius is radius × time scale, kept inside the cadence band. AP-022 4-way controller only.
        /// </summary>
        public static float RunChildTimeScale(float referenceSpeed, float clipSpeedMps, float radius) => Mathf.Min(CadenceBand.Max / Mathf.Max(0.1f, radius), ChildTimeScale(referenceSpeed, clipSpeedMps));

        /// <summary>
        /// WP-071 D-1: the run ring's radius along a move direction — the motor's backward scale (S-02 PlayerMotor.BackwardScale:
        /// lerp(1, PlayerConfigSection.BackwardSpeedMultiplier, the backward share)): 1 forward / side, 0.85 straight back, ≈ 0.89 at 45° back.
        /// </summary>
        public static float RunRingRadius(Vector2 move)
        {
            if (move.sqrMagnitude < 1e-8f) return 1f;
            return Mathf.Lerp(1f, DD.Prototype.Core.Config.PlayerConfigSection.BackwardSpeedMultiplier, Mathf.Clamp01(-move.normalized.y));
        }

        /// <summary>WP-071 K3: a run-ring child's time scale — honest up to the cadence band, beyond it the run slides (run_back / strafes at 6.2 m/s).</summary>
        public static float RunChildTimeScale(float referenceSpeed, float clipSpeedMps) => Mathf.Min(CadenceBand.Max, ChildTimeScale(referenceSpeed, clipSpeedMps));

        /// <summary>Renderers of the M1 primitive body to hide when this visual is attached.</summary>
        public static void HidePrimitiveParts(Transform visual, ICollection<Renderer> hidden)
        {
            if (visual == null) return;
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].enabled)
                {
                    renderers[i].enabled = false;
                    hidden?.Add(renderers[i]);
                }
            }
        }
    }
}
