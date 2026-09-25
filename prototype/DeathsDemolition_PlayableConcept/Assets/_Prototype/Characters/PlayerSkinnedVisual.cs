using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using UnityEngine;

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
        public void ConfigureFirstPerson(GameObject model) => _fpModel = model;
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
        public const float MaxLocoRate = 2.4f;

        [PrototypeOnly(PendingDecisions.None,
            "Highest playback rate of the run cycles (P-06 ×1.6: faster reads as a flicker). WP-032B needed 2.0 on the v002 run_fwd (3.75 m/s on the " +
            "Unity rig); WP-032C: the v003 run_fwd stride is 1.05 m (Blender 5.69 m/s no-slide), so 6.2 m/s sits under the cap again. Cap.", true)]
        public const float MaxRunRate = CadenceBand.Max;   // WP-071 K3: the run cycles stay in the cadence band; faster travel slides

        public Animator Animator => _animator;
        private WeaponGripIk _gripIk;
        private static readonly int[] s_restHashes =
        {
            Animator.StringToHash(CharacterAnimatorContract.AimState + "_0"), Animator.StringToHash(CharacterAnimatorContract.AimState + "_1"),
            Animator.StringToHash(CharacterAnimatorContract.AimState + "_2"), Animator.StringToHash(CharacterAnimatorContract.SwHoldState + "_1"),
            Animator.StringToHash(CharacterAnimatorContract.SwHoldState + "_2"), Animator.StringToHash(CharacterAnimatorContract.SwHoldState + "_3"),
            Animator.StringToHash(CharacterAnimatorContract.AimV3State)
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
        public Transform BackSocket => _backSocket;
        public CharacterAnimationSet AnimationSet => _animation;
        public CharacterAnimationEvents Events => _events;
        public PlayerAimLayer AimLayer => _aimLayer;
        public CharacterLookAt LookAt => _lookAt;
        public FootIkAdapter FootIk => _footIk;

        [PrototypeOnly(PendingDecisions.None, "Distance ahead along the aim the head looks at, metres. Invented.", true)]
        private const float AimLookDistance = 8f;

        private float _runTimeScale = 1f;
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
        public Transform Attach(Transform visual, Func<Vector3> planarVelocity, Func<float> referenceSpeed, Func<Vector3> aimDirection)
        {
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
            if (_footIk != null) _footIk.Bind(() => _authored && !_dying, () => true, () => _move.sqrMagnitude > 0.004f && _rate * (_move.sqrMagnitude > 0.5f ? _runTimeScale : 1f) <= CadenceBand.LockMaxRate);
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

            if (_authored)
            {
                BindWeaponSets();
                if (_fpModel != null && _animation.FpController != null && _fpArms == null)
                {
                    _fpArms = GetComponent<FirstPersonArms>() ?? gameObject.AddComponent<FirstPersonArms>();
                    _fpArms.Configure(_fpModel, _animation, planarVelocity, () => _authored && !_dying, _renderers.Length > 0 ? _renderers[0] : null, () => _weaponDef);
                }
                _animator.SetBool(AliveId, true);
                _overrideLayer = -1;
                for (int i = 0; i < _animator.layerCount; i++) if (_animator.GetLayerName(i) == CharacterAnimatorContract.OverrideUpperLayer) _overrideLayer = i;
                SetWeapon(_weapon);
            }

            return _weaponSocket;
        }

        private void OnDestroy()
        {
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
            if (e == CharacterAnimationEvent.DeathEnd && _dying) _deathEnded = true;
            else if (e == CharacterAnimationEvent.LeftHandRelease) { if (_gripIk != null) _gripIk.SetReleased(true); }   // WP-071 B4 (AP-022)
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
        /// WP-071 B4b: the v3 weapon path (on since the grip decision B — WeaponDefinition.gripOffset — and S-02's WeaponMountAlignment
        /// pose 5 landed). Off = every weapon on the legacy class pose (A/B tests).
        /// </summary>
        public static bool V3WeaponSets = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetV3Statics() => V3WeaponSets = true;

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

        /// <summary>Equip: the v3 set when the weapon's AnimSet has one, else the legacy class pose.</summary>
        public void EquipWeapon(DD.Prototype.Core.Weapons.WeaponDefinition def, ClassicWeaponId id)
        {
            _weaponDef = def;
            _burstStart = -1f;
            int k = V3WeaponSets && def != null && _runtimeOverrides != null ? _setKeys.IndexOf(def.AnimSet) : -1;
            if (_fpArms != null) _fpArms.SetWeaponSet(k < 0 ? null : _setKeys[k], def != null ? def.FpViewPose.scale : 1f);
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

        /// <summary>WP-071 B4b: the reload the gameplay started is the empty one (its duration is the definition's reloadEmptySeconds — the event carries only the duration; S-02 ClassicWeaponRuntime picks tac / empty).</summary>
        private bool IsEmptyReload(float seconds)
        {
            if (_weaponDef == null || _weaponDef.ammo == null) return false;
            return Mathf.Abs(seconds - _weaponDef.ammo.reloadEmptySeconds) < Mathf.Abs(seconds - _weaponDef.ammo.reloadTacSeconds);
        }
        private void OnReloadStarted(float duration) => PlayReload(duration, WeaponEvents.LastReloadEmpty);   // S-02 d3f3b52e: written just before the event
        private void OnReloadInterrupted() => CancelReload();
        private void OnSpecialEnded() => SetSpecialHold(0);

        /// <summary>WP-032E: the usage bus is the authoritative Special beat surface (WP-056); the HUD-state path above stays as the fallback for a Special without usage events.</summary>
        private void OnSpecialUsage(SpecialUsageEvent e)
        {
            switch (e.Kind)
            {
                case SpecialUsageKind.Activated: SetSpecialHold(HoldIndexOf(e.Id)); break;
                // Fired: the swing is already fired through IWeaponHost.OnWeaponFired → PlayerProceduralAnimator.PlayFireRecoil → PlayFire()
                // (WeaponController.FireSpecialOnce keeps that call); a second trigger here would only re-arm the same beat.
                case SpecialUsageKind.Deactivated: SetSpecialHold(0); break;
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
            if (!_authored || _dying || _weapon == 3) return;
            string clip;
            if (_weapon == CharacterAnimatorContract.WeaponPoseV3)
            {
                _animator.SetBool(ReloadEmptyId, empty);
                if (_fpArms != null) _fpArms.Reload(seconds, empty);
                clip = CharacterAnimatorContract.PlayerWeaponClipV3(WeaponAnimSet, empty ? "reload_empty" : "reload_tac");
            }
            else clip = CharacterAnimatorContract.PlayerWeaponClip(_weapon, "reload");
            float clipSeconds = _animation.Find(clip)?.seconds ?? 0f;
            _animator.SetFloat(ReloadRateId, seconds > 0.05f && clipSeconds > 0.05f ? clipSeconds / seconds : 1f);
            _animator.SetTrigger(ReloadId);
        }

        /// <summary>Reload interrupted (special pickup / death): straight back to the rest pose.</summary>
        public void CancelReload()
        {
            if (!_authored) return;
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
            _swHold = Mathf.Clamp(hold, 0, 2);
            if (_authored) _animator.SetInteger(SwHoldId, _swHold);
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
            // WP-071 B4b: a burst weapon's v3 fire clip is the whole burst (P-06 ar_2: 3 kicks in 15 f) — only the burst's first shot starts it
            if (_swHold == 0 && _weapon == CharacterAnimatorContract.WeaponPoseV3 && _weaponDef != null && _weaponDef.fire != null
                && _weaponDef.fire.fireMode == DD.Prototype.Core.Weapons.WeaponFireMode.Burst)
            {
                // the burst's shots arrive on frame boundaries (a 60 Hz frame can push the last one past count × interval): the window
                // reaches half-way into the gap before the next burst (burstDelaySeconds), which frame jitter never crosses
                float now = Time.time, window = Mathf.Max(0, _weaponDef.fire.burstCount - 1) * _weaponDef.fire.burstIntervalSeconds + 0.5f * _weaponDef.fire.burstDelaySeconds;
                if (_burstStart >= 0f && now - _burstStart <= window) return;
                _burstStart = now;
            }

            FireTriggers++;
            if (_swHold == 0 && _fpArms != null) _fpArms.Fire();

            _animator.SetTrigger(_swHold > 0 ? SwFireId : FireId);
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
            _dying = true;
            _deathEnded = false;
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

            // Local (character-space) velocity over the set's reference speed: the 2-D blend axes.
            // The child time scales were baked against the same reference, so |v| / reference is
            // the exact no-slide rate at the blend corners; near idle the rate eases back to 1 so
            // the idle breath does not freeze.
            float reference = _animation.LocomotionReferenceSpeed;
            Vector3 local = transform.InverseTransformDirection(v) / reference;
            Vector2 targetMove = new Vector2(Mathf.Clamp(local.x, -1f, 1f), Mathf.Clamp(local.z, -1f, 1f));
            float speed01 = Mathf.Clamp01(mps / reference);
            // WP-032B: the run ring's clip rate is LocoRate × its child time scale; cap it at MaxRunRate (P-06).
            float targetRate = Mathf.Lerp(1f, Mathf.Min(speed01, MaxRunRate / _runTimeScale), speed01);
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
            _ = _referenceSpeed;   // the motor's live top speed stays available for a follow-up run cycle
        }

        /// <summary>Blend-tree child time scale that shows no foot slide at the reference speed (builder).</summary>
        public static float ChildTimeScale(float referenceSpeed, float clipSpeedMps)
        {
            return clipSpeedMps > 0.01f ? Mathf.Min(MaxLocoRate, Mathf.Max(0.1f, referenceSpeed) / clipSpeedMps) : 1f;
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
