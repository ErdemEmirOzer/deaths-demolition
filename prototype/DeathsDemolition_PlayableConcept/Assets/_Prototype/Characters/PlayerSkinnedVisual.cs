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

        private void OnWeaponEquipped(ClassicWeaponId id) => SetWeapon((int)id);
        private void OnReloadStarted(float duration) => PlayReload(duration);
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
        public static int HoldIndexOf(SpecialWeaponId id) => id == SpecialWeaponId.OfudaLoom ? 2 : id == SpecialWeaponId.TideJewel ? 3 : 1;   // WP-032G: AP-009G jewel hold

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
            _weapon = Mathf.Clamp(weapon, 0, 3);
            if (_authored) _animator.SetInteger(WeaponId, _weapon);
        }

        /// <summary>Reload: the weapon's clip rate-scaled so mag_out / mag_in / chamber land inside <paramref name="seconds"/> (WeaponConfig.ReloadTime).</summary>
        public void PlayReload(float seconds)
        {
            if (!_authored || _dying || _weapon > 2) return;
            float clipSeconds = _animation.Find(CharacterAnimatorContract.PlayerWeaponClip(_weapon, "reload"))?.seconds ?? 0f;
            _animator.SetFloat(ReloadRateId, seconds > 0.05f && clipSeconds > 0.05f ? clipSeconds / seconds : 1f);
            _animator.SetTrigger(ReloadId);
        }

        /// <summary>Reload interrupted (special pickup / death): straight back to the rest pose.</summary>
        public void CancelReload()
        {
            if (!_authored) return;
            _animator.ResetTrigger(ReloadId);
            if (_overrideLayer >= 0 && _animator.GetCurrentAnimatorStateInfo(_overrideLayer).IsName(CharacterAnimatorContract.ReloadState + "_" + _weapon))
            {
                _animator.CrossFadeInFixedTime(RestStateName(), 0.1f, _overrideLayer);
            }
        }

        /// <summary>Weapon switch: the outgoing clip (switch_away event), then <see cref="SetWeapon"/> + <see cref="PlaySwitchIn"/> when S-02 swaps the mount.</summary>
        public void PlaySwitchOut() { if (_authored && !_dying) _animator.SetTrigger(SwitchOutId); }
        public void PlaySwitchIn() { if (_authored && !_dying) _animator.SetTrigger(SwitchInId); }
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
            return _weapon <= 2 ? CharacterAnimatorContract.AimState + "_" + _weapon : CharacterAnimatorContract.EmptyState;
        }

        // ------------------------------------------------------------------ driver hooks

        /// <summary>Weapon fired: the additive recoil clip (once per trigger pull; stacks naturally by re-trigger). WP-032E: while a Special Weapon is held (SwHold 1/2) the use beat is the generated swing (SwFire_<hold>) instead of the classic recoil.</summary>
        public void PlayFire()
        {
            if (!_authored || _dying) return;
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
