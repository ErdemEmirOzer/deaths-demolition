using System.Collections.Generic;
using DD.Prototype.Audio;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The armed player. Owns the classic weapon, the Special Weapon and the run build,
    /// and runs steps 1 to 4 of the twelve-step chain for every trigger pull.
    ///
    /// SETUP
    /// Add this to the player's aim pivot - the transform that already turns to face the
    /// cursor. Nothing else is required: the visual rigs, the muzzle and the feedback
    /// director all build themselves. Assign <see cref="_aimSource"/> only if the aim
    /// direction is not this transform's forward.
    ///
    /// INPUT
    /// Read through <c>InputSourceRegistry.Current</c> only. There is no reference to
    /// UnityEngine.InputSystem anywhere in this folder, per the core contract.
    /// REQ-039 is <see cref="IInputSource.FireHeld"/>: every weapon here is automatic,
    /// so holding is firing and no separate auto/semi mode exists.
    ///
    /// PAUSE
    /// Every clock uses scaled delta time, so <c>Time.timeScale = 0</c> stops the
    /// weapon, the reload, the Special Weapon timer and the effects together, with no
    /// pause-aware code in this class at all.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponController : PrototypeConfigBehaviour
    {
        [Header("Aim")]
        [Tooltip("Optional. Leave empty to use this transform's forward.")]
        [SerializeField] private MonoBehaviour _aimSourceBehaviour = null;

        [Tooltip("Flatten the shot direction onto the horizontal plane. Enemies stand on the ground.")]
        [SerializeField] private bool _flattenAim = true;

        [Header("Rigs - leave empty to auto-build primitive placeholders")]
        [SerializeField] private WeaponVisualRig _classicRig;
        [SerializeField] private WeaponVisualRig _specialRig;

        [Header("PROTOTYPE-ONLY presentation")]
        [PrototypeOnly(PendingDecisions.None,
            "Camera shake in metres when a weapon's CameraShake scale is 1. The scale in " +
            "PrototypeConfig is a 0-1 weight, not a distance; this converts it.", true)]
        [SerializeField] private float _shakeMetresAtScaleOne = 0.12f;

        [PrototypeOnly(PendingDecisions.None, "Base shake duration in seconds.", true)]
        [SerializeField] private float _shakeBaseDuration = 0.06f;

        [PrototypeOnly(PendingDecisions.None, "Extra shake duration per unit of shake scale.", true)]
        [SerializeField] private float _shakeDurationPerScale = 0.05f;

        [PrototypeOnly(PendingDecisions.None, "Muzzle flash size in metres for a Medium weapon.", true)]
        [SerializeField] private float _muzzleFlashSize = 0.28f;

        [PrototypeOnly(PendingDecisions.None, "Muzzle flash light intensity for a Medium weapon.", true)]
        [SerializeField] private float _muzzleFlashLight = 3.2f;

        [PrototypeOnly(PendingDecisions.None,
            "Extra size and light multiplier applied to the Special Weapon's flash.", true)]
        [SerializeField] private float _specialFlashMultiplier = 1.6f;

        [PrototypeOnly(PendingDecisions.None,
            "WP-042: height above the character root the classic damage ray starts at, metres — the WP-002 placeholder " +
            "muzzle height (socket 1.37 + 0.02). The ray keeps this height whatever model / hold the weapon has, so the " +
            "hit region (upper body vs head) does not change with the AP-013 muzzle marker or between A/B/C holds " +
            "(1.36 / 1.37 / 1.24 m); the tracer and flash still start at the visual muzzle. Invented.", true)]
        [SerializeField] private float _shotRayHeight = 1.39f;

        /// <summary>WP-042 readout: the damage-ray height policy (tests).</summary>
        public float ShotRayHeight => _shotRayHeight;

        // ------------------------------------------------------------------ state

        private readonly ClassicWeaponRuntime _classic = new ClassicWeaponRuntime();
        private readonly SpecialWeaponRuntime _special = new SpecialWeaponRuntime();
        private readonly WeaponModifierState _modifiers = new WeaponModifierState();

        private IAimSource _aimSource;
        private IWeaponHost _host;
        private ClassicWeaponId _equippedId = ClassicWeaponId.AssaultRifle;
        private bool _equipped;

        // Reload edge detection. The runtime object owns the clock; the controller owns
        // everything that has to happen on the two edges of it, so the presentation is in
        // one place instead of scattered through the runtime.
        private bool _wasReloading;
        private bool _reloadInsertFired;

        // ---- WP-042 · mechanism slots, pickup hand-off, authored classic model
        private PlayerSkinnedVisual _skinned;
        private ClassicWeaponId _classicModelId = (ClassicWeaponId)(-1);
        private WeaponMechanismTimeline _timeline = WeaponMechanismTimeline.Magazine;
        private float _casingAt = -1f;
        private bool _casingOwed;   // WP-087: a pump shot's shell not yet out (the clip's eject or the deadline pays it, once)
        private int _casingSerial;  // WP-087: the shot that owes it
        private bool _magOutFired;

        /// <summary>
        /// WP-087 (C-00 05.10, marker alignment audit): a pump shot's shell waits for the character clip's eject event at most this
        /// long past the timeline's delay, then leaves from the deadline. In first person the third-person fire clip is weighed 0
        /// (WP-087 H4b, decision B), its eject never arrives, and sg_1 ejected no shell and played no shell sound.
        /// </summary>
        public const float PumpCasingGraceSeconds = 0.15f;
        private readonly WeaponMagazineOwnership _magazine = new WeaponMagazineOwnership();   // WP-082 (S-02 contract v1)
        private readonly WeaponMagazineVisual _magazineVisual = new WeaponMagazineVisual();   // WP-082 visual: part_mag ↔ socket_mag_l
        private CameraMode _magazineMode;   // the camera mode the reload began in (a change ends the ownership)
        private int _seatsFired;
        private int _seatsFromClip;   // §12: the clip's seats this reload — one per cartridge / rocket, whichever synonym named it
        private bool _chamberFired;
        private bool _pickupDonePending;
        private float _pickupDoneAt = -1f;

        // WP-069: the trigger is producing shots (FireStarted / FireStopped edges for S-08 / S-07).
        private bool _firing;

        // WP-069 B2: the character's motor — movement widens the cone, firing / focus slow the step.
        private PlayerMotor _motor;
        private WeaponMountAlignment _mountAlignment;
        private PlayerSkinnedVisual _skinnedVisual;

        // WP-069 B4: the aim point (Namekuji's top-down arc lands at the cursor) and Raiju's self-shock slow.
        private PlayerAim _playerAim;
        private float _selfSlowUntil = -1f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 B4: move-speed multiplier while Raiju's self-shock slow lasts (DIRECTION §7a: 0.4 s slow).", true)]
        private const float SelfShockSlowMultiplier = 0.5f;

        // AP-009C clip events resolved by NAME against the S-06 enum (None until WP-032C adds them).
        private static readonly CharacterAnimationEvent s_eject = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.Eject]);
        private static readonly CharacterAnimationEvent s_magOut = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.MagOut]);
        private static readonly CharacterAnimationEvent s_magIn = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.MagIn]);
        private static readonly CharacterAnimationEvent s_magIn2 = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.MagIn2]);
        private static readonly CharacterAnimationEvent s_magIn3 = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.MagIn3]);
        private static readonly CharacterAnimationEvent s_chamber = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.Chamber]);
        private static readonly CharacterAnimationEvent s_pickupDone = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.PickupDone]);

        // ------------------------------------------------------------------ public surface

        /// <summary>The run build. This is the object the upgrade cards write into.</summary>
        public WeaponModifierState Modifiers => _modifiers;

        /// <summary>The classic weapon. Never null, never swapped mid-run (REQ-051).</summary>
        public ClassicWeaponRuntime Classic => _classic;

        /// <summary>The Special Weapon. Inactive unless one has been picked up.</summary>
        public SpecialWeaponRuntime Special => _special;

        /// <summary>True while a Special Weapon is in hand and the classic weapon is parked.</summary>
        public bool SpecialWeaponActive => _special.IsActive;

        /// <summary>Where shots come from. Follows the recoil.</summary>
        public Transform Muzzle => ActiveRig != null ? ActiveRig.Muzzle : transform;

        private WeaponVisualRig ActiveRig => _special.IsActive ? _specialRig : _classicRig;

        /// <summary>WP-042: the AP-009C clips carry the mechanism events on this character (else the timer fallback runs).</summary>
        public bool ClipDrivesMechanism => s_eject != CharacterAnimationEvent.None && _skinned != null && _skinned.Authored;

        /// <summary>WP-042: the hand model waits for the clip's pickup_done (or the fallback timeout).</summary>
        public bool PickupDonePending => _pickupDonePending;

        /// <summary>WP-042 readout: the classic rig carries the roster model (blockout / AP-013).</summary>
        public bool ClassicRigHasAuthoredModel => _classicRig != null && _classicRig.HasAuthoredModel;

        /// <summary>
        /// WP-059C: every renderer of the weapon in hand (classic + Special rigs, models and placeholders).
        /// The readability captures (DEC-077: S-05 CharacterTests, S-10 QaReadabilityGate) exclude these so
        /// the figure mask is the body — since WP-059B the weapon sits level in front of the chest and would
        /// otherwise count as player pixels / hide the body band.
        /// </summary>
        public void CollectWeaponRenderers(List<Renderer> into)
        {
            if (into == null) return;
            if (_classicRig != null) into.AddRange(_classicRig.GetComponentsInChildren<Renderer>(true));
            if (_specialRig != null) into.AddRange(_specialRig.GetComponentsInChildren<Renderer>(true));
        }

        /// <summary>WP-059C: true when <paramref name="r"/> belongs to the weapon in hand (see <see cref="CollectWeaponRenderers"/>).</summary>
        public bool IsWeaponRenderer(Renderer r)
        {
            if (r == null) return false;
            return (_classicRig != null && r.transform.IsChildOf(_classicRig.transform)) || (_specialRig != null && r.transform.IsChildOf(_specialRig.transform));
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (GetComponent<FpViewPoseDriver>() == null) gameObject.AddComponent<FpViewPoseDriver>().Bind(this);   // WP-069 FP view pose (order 950)
            _magazine.Stepped += OnMagazineStep;   // WP-082 visual
            if (_aimSourceBehaviour != null)
            {
                _aimSource = _aimSourceBehaviour as IAimSource;
                _host = _aimSourceBehaviour as IWeaponHost;
            }

            if (_aimSource == null)
            {
                // Picks up a PlayerWeaponBinding that was added to the same rig, whether
                // by the installer or by hand, without either side knowing the other's
                // execution order.
                var found = GetComponentInParent<PlayerWeaponBinding>();
                if (found != null)
                {
                    BindHost(found);
                }
            }

            EnsureRigs();
        }

        /// <summary>
        /// Hand the weapon its character. Called by <see cref="PlayerWeaponBinding"/>;
        /// safe before or after Awake, and safe to call twice.
        /// </summary>
        public void BindHost(PlayerWeaponBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            _aimSourceBehaviour = binding;
            _aimSource = binding;
            _host = binding;

            ReparentRigs();
            _skinned = transform.root.GetComponentInChildren<PlayerSkinnedVisual>(true);
            _motor = transform.root.GetComponentInChildren<PlayerMotor>(true);
            _playerAim = transform.root.GetComponentInChildren<PlayerAim>(true);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            WeaponRegistry.Register(this);

            // Touch the director so its pools exist before the first shot rather than on
            // the first shot, which would be a hitch at exactly the wrong moment.
            _ = HitFeedbackDirector.Instance;

            WeaponDebugHooks.EnsureExists();
            CharacterAnimationEvents.Any += OnClipEvent;
            RunEvents.RoundStarted += OnRoundStarted;
        }

        /// <summary>WP-069 B2: every round start tops the reserve up to that round's allowance.</summary>
        private void OnRoundStarted(int round)
        {
            _classic.RefillReserveForRound(round);
        }

        protected override void OnDisable()
        {
            SetFiring(false);
            WeaponRegistry.Unregister(this);
            CharacterAnimationEvents.Any -= OnClipEvent;
            RunEvents.RoundStarted -= OnRoundStarted;
            if (_motor != null) _motor.WeaponSpeedMultiplier = 1f;

            if (_special.IsActive)
            {
                // WP-007 (AP-10 / OS-2): a controller going away with a Special Weapon in
                // hand is a CANCEL (scene unload, teardown), not an expiry - no "you lost
                // it" news, and the snapshot must not stay Active into the next scene.
                _special.Cancel();
                PublishSpecialState(true);
            }

            base.OnDisable();
        }

        /// <summary>
        /// Every weapon number is re-read here, so an F1 edit to damage, fire rate,
        /// magazine size or reload time lands on the weapon already in the player's hands.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            ClassicWeaponSettings settings = config.Weapons.Find(
                _equipped ? _equippedId : WeaponRegistry.SelectedWeapon);

            if (!_equipped)
            {
                EquipClassic(settings != null ? settings.Id : ClassicWeaponId.AssaultRifle, config);
            }
            else
            {
                _classic.ApplySettings(settings);
            }

            _special.ApplySettings(config.Weapons.Special);
        }

        // ------------------------------------------------------------------ equip

        /// <summary>
        /// Choose the run's classic weapon. Called once when a run starts. REQ-050 /
        /// REQ-051: nothing in normal play calls this a second time.
        /// </summary>
        public void EquipClassic(ClassicWeaponId id, PrototypeConfig config = null)
        {
            EquipClassicCore(id, config, true);
        }

        /// <summary>
        /// WP-107 (R4, user 11.10 note 8): a weapon bought at a wall during the run (the product decision that ends REQ-050 /
        /// REQ-051's one-weapon run). It swaps the classic weapon (a full magazine and this round's reserve) without writing
        /// <see cref="WeaponRegistry.SelectedWeapon"/> - the next run still starts with the Prepare Run pick, and the run's
        /// summary / mastery stay with it. The same weapon is an ammo refill (<see cref="RefillClassic"/>). Refused while a
        /// Special is in hand (it parks the classic weapon).
        /// </summary>
        public bool BuyClassic(ClassicWeaponId id, int round)
        {
            if (SpecialWeaponActive)
            {
                return false;
            }

            if (_equipped && _equippedId == id)
            {
                return RefillClassic(round);
            }

            if (Config == null || Config.Weapons.FindExact(id) == null)
            {
                return false;
            }

            SkinnedVisual()?.PlayPickup();   // S-06 (R4): the upper body takes it off the wall, then the switch-in brings it up
            EquipClassicCore(id, null, false);
            _classic.RefillReserveForRound(round);
            PublishClassicState(true);
            return true;
        }

        /// <summary>WP-107 (R4): the wall buy ammo refill of the weapon in hand. False (nothing changes) while a Special is in hand or the ammo is already full.</summary>
        public bool RefillClassic(int round)
        {
            if (SpecialWeaponActive || !_equipped || _classic.IsAmmoFull(round))
            {
                return false;
            }

            _classic.RefillFull(round);
            PublishClassicState(true);
            SkinnedVisual()?.PlayPickup();
            return true;
        }

        /// <summary>WP-107 (R4): the classic weapon has a full magazine and this round's reserve.</summary>
        public bool IsClassicAmmoFull(int round)
        {
            return _equipped && _classic.IsAmmoFull(round);
        }

        private bool EquipClassicCore(ClassicWeaponId id, PrototypeConfig config, bool rememberSelection)
        {
            PrototypeConfig c = config != null ? config : Config;
            if (c == null)
            {
                return false;
            }

            ClassicWeaponSettings settings = c.Weapons.Find(id);
            if (settings == null)
            {
                return false;
            }

            bool switching = _equipped && _equippedId != settings.Id;
            _equippedId = settings.Id;
            _equipped = true;
            _classic.Equip(settings, _modifiers);
            _timeline = WeaponMechanismTimeline.For(settings);
            _casingAt = -1f;
            _casingOwed = false;
            EnsureClassicModel();

            if (rememberSelection) WeaponRegistry.SelectedWeapon = _equippedId;   // WP-107: a wall buy keeps the run's pick
            WeaponEvents.PublishWeaponEquipped(_equippedId);
            PublishClassicState(true);
            if (switching) SkinnedVisual()?.PlaySwitchIn();   // WP-069 (S-06 B4b): the new weapon comes up
            if (switching) _clipMotionUntil = Time.time + FpClipMotionSeconds;   // WP-069 FP feel: the switch clip shows in FP
            return true;
        }

        /// <summary>The player's authored body (the switch clips), found once.</summary>
        private PlayerSkinnedVisual SkinnedVisual()
        {
            if (_skinnedVisual == null)
            {
                PlayerRig rig = GetComponentInParent<PlayerRig>();
                _skinnedVisual = rig != null && rig.Animation != null ? rig.Animation.Skinned : null;
            }

            return _skinnedVisual;
        }

        /// <summary>
        /// REQ-055 / REQ-057. The Special Weapon was collected: its clock starts now and
        /// the classic weapon is parked, not discarded.
        /// </summary>
        public void ActivateSpecialWeapon(SpecialWeaponId id = SpecialWeaponId.Raiju)
        {
            if (Config == null || _special.IsActive)
            {
                return;
            }

            _classic.CancelReload();
            _special.Begin(Config.Weapons.Special, id, raisePickedUp: false);

            // WP-042 (DEC-082): the grab is an upper-body clip; the hand model shows at its
            // pickup_done event — or at the fallback timeout when the clip cannot carry it
            // (no authored character, interrupted clip). Firing is not gated.
            _pickupDonePending = s_pickupDone != CharacterAnimationEvent.None && _skinned != null && _skinned.Authored;
            _pickupDoneAt = Time.time + SpecialWeaponUsage.PickupDoneFallbackSeconds;

            SetRigVisibility();
            PublishClassicState(true);
            // WP-059C (C-00): the ONE Active notify — straight from the ground phase to the active phase in the
            // same frame (the pickup no longer publishes its own) — and only then the PickedUp news, so a
            // PickedUp handler reading WeaponEvents.Special sees THIS weapon's name / duration (S-09 banner).
            PublishSpecialState(true);
            WeaponEvents.PublishSpecialPickedUp(_special.Duration);
            // WP-056: the usage bus — Activated carries the active seconds (S-03/S-07/S-08 consumers).
            SpecialWeaponUsageEvents.Publish(id, SpecialUsageKind.Activated, SpecialWeaponUsage.For(id).FireVfxFamily,
                transform.position, transform.forward, 0f, _special.Duration, 0);
        }

        /// <summary>End the Special Weapon early, as if its timer had run out. Debug panel and tests.</summary>
        public void DeactivateSpecialWeapon()
        {
            if (!_special.IsActive)
            {
                return;
            }

            _special.End();
            HandleSpecialEnded();
        }

        /// <summary>
        /// WP-007 (AP-10): stop the Special Weapon because the run is over (death, run end,
        /// new run). Same hand-back of the classic weapon as an expiry, without the expiry
        /// news cue. Returns true when there was something to cancel.
        /// </summary>
        public bool CancelSpecialWeapon()
        {
            if (!_special.IsActive)
            {
                return false;
            }

            _special.Cancel();
            HandleSpecialEnded();
            return true;
        }

        /// <summary>Wipe the build and refill the magazine. Called when a run starts.</summary>
        public void ResetForNewRun()
        {
            _modifiers.ResetForNewRun();
            DeactivateSpecialWeapon();
            StaggerGovernor.Clear();

            if (Config != null)
            {
                EquipClassic(WeaponRegistry.SelectedWeapon, Config);
            }
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (Config == null)
            {
                return;
            }

            // WP-069 (C-00 grip decision B): the weapon in hand sits by its definition's grip offset (the Special while held).
            if (_mountAlignment == null && RigParent != null)
            {
                _mountAlignment = RigParent.GetComponent<WeaponMountAlignment>();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (WeaponMountAlignment.Diagnostics)
                {
                    Debug.Log("[DD-MOUNT] controller mount=" + RigParent.name + " parent=" + (RigParent.parent != null ? RigParent.parent.name : "null") + " alignment=" + (_mountAlignment != null));
                }
#endif
            }
            if (_mountAlignment != null)
            {
                WeaponDefinition held = _special.IsActive ? _special.Definition : _classic.Definition;
                _mountAlignment.GripOffset = held != null ? held.GripOffset : WeaponGrip.DefaultOffset;
            }

            // WP-087 C (C-00 06.10; councils K/wp-087/konsey/KARAR-ArmsCarryKick-06.10.md + K/wp-083/KARAR-HELDKICK-API-06.10.KONSOLIDE.md):
            // the weapon-first hold carries the classic rig's kick at its pivot (the arms) and the rig's body keeps 1 − the hold's weight
            // of the same frame (WeaponRigCarriedPose, 895). The hold runs for the classic weapons only (B2e), so a Special — and no
            // hold — leaves both rigs on the old path; first person: the hold's weight is 0, the body keeps all of it.
            PlayerSkinnedVisual skinned = SkinnedVisual();
            WeaponHoldIk hold = skinned != null ? skinned.HoldIk : null;
            bool carry = hold != null && _classicRig != null && !_special.IsActive;
            if (hold != null) hold.KickSource = carry ? _classicRig : null;
            if (_classicRig != null) _classicRig.CarriedBy = carry ? hold : null;
            if (_specialRig != null) _specialRig.CarriedBy = null;

            float dt = Time.deltaTime;
            IInputSource input = InputSourceRegistry.Current;

            bool fireHeld = input.IsEnabled && input.FireHeld;
            bool firePressed = input.IsEnabled && input.FirePressed;
            bool reloadPressed = input.IsEnabled && input.ReloadPressed;

            // WP-069 B2: focus / ADS and movement feed the cone before the clocks advance.
            _classic.SetFocus(input.IsEnabled && !_special.IsActive && input.FocusHeld());
            _classic.SetMovement01(_motor != null ? _motor.Speed01 : 0f);

            if (_special.IsActive)
            {
                SetFiring(false);   // the classic weapon is parked
                _classic.CancelBurst();   // R1-B W3: a burst does not outlive the parking
                if (_special.Tick(dt))
                {
                    HandleSpecialEnded();
                }
                else
                {
                    TickSpecialFiring(fireHeld, firePressed, reloadPressed, dt);
                }
            }
            else
            {
                _classic.Tick(dt);
                TickClassicFiring(fireHeld, firePressed, reloadPressed);
            }

            TickReloadPresentation();
            TickMechanismFallback();

            if (_motor != null)
            {
                float weaponSpeed = _special.IsActive ? 1f : _classic.MoveSpeedMultiplier(_firing);
                if (Time.time < _selfSlowUntil) weaponSpeed *= SelfShockSlowMultiplier;   // WP-069 B4 Raiju self-shock
                _motor.WeaponSpeedMultiplier = weaponSpeed;
            }

            PublishClassicState(false);
            PublishSpecialState(false);
        }

        // ------------------------------------------------------------------ classic firing

        private void TickClassicFiring(bool fireHeld, bool firePressed, bool reloadPressed)
        {
            // Dead player or frozen simulation: nothing below may run, not even the reload
            // key or the dry-fire click. WP-002: the gate used to sit after those two, so a
            // reload could start (and its start cue play) while the world stood still.
            if (!CanFireNow())
            {
                SetFiring(false);
                _classic.ClampCooldown();
                _classic.CancelBurst();   // R1-B W3: a blend / death / freeze ends the burst, it does not resume without a trigger
                return;
            }

            if (reloadPressed)
            {
                _classic.TryBeginReload();
            }

            // WP-069 B2: the trigger cuts a per-shell reload short — the shells already in stay.
            if (firePressed && _classic.IsReloading)
            {
                _classic.TryInterruptShellReload();
            }

            if (!_classic.HasAmmo && !_classic.IsReloading)
            {
                if (firePressed)
                {
                    WeaponEvents.PublishDryFire();
                    AudioEvents.Raise(CombatAudioEvent.ClassicWeaponDryFire, Muzzle.position, 1f, 30);
                    FeelEvents.RaiseDryFire(new FeelDryFire(_equippedId, Muzzle.position));   // WP-095: the feel layers' one source
                }

                // Holding the trigger on an empty magazine reloads: with hold-to-fire as
                // the only firing mode, forcing a separate keypress here is friction, not
                // depth.
                if (fireHeld || firePressed)
                {
                    _classic.TryBeginReload();
                }
            }

            // WP-069 B2 · fire modes. Auto: while held. Single: one shot per cycle — a press during the
            // cycle is buffered, a held trigger refires at the cycle ceiling (A4: Single's rpm is the
            // ceiling; the M3 hold-to-fire kept for the gamepad). Burst: a press (or a hold, after the
            // burst delay) starts the burst, whose remaining shots fire whatever the trigger does.
            WeaponFireMode mode = _classic.FireMode;
            if (mode == WeaponFireMode.Single && firePressed)
            {
                _classic.BufferPress();
            }
            else if (firePressed && FeelTimeScale.GlobalStopActive)
            {
                // WP-095 K1 (C-00 08.10): a press during the top-down global hit-stop is queued, never lost. At 0.06 the
                // cooldown barely moves, so an auto / burst tap that fits inside the stop would end before the weapon is
                // ready; the scaled press buffer (Single's) carries it past the stop. Only top-down ever has a global stop.
                _classic.BufferPress();
            }

            if (mode == WeaponFireMode.Burst && (fireHeld || firePressed || _classic.PressBuffered) && !_classic.BurstActive && _classic.CanFire)
            {
                _classic.BeginBurst();
            }

            bool wantsShot = mode == WeaponFireMode.Single ? fireHeld || _classic.PressBuffered
                : mode == WeaponFireMode.Burst ? _classic.BurstActive || fireHeld
                : fireHeld || _classic.PressBuffered;   // WP-095 K1: an auto press queued during the global stop

            if (!wantsShot)
            {
                SetFiring(false);
                _classic.ClampCooldown();
                return;
            }

            int shots = 0;
            while (_classic.CanFire && shots < ClassicWeaponRuntime.MaxShotsPerFrame)
            {
                FireClassicOnce();
                shots++;
                if (mode == WeaponFireMode.Single || (mode == WeaponFireMode.Burst && !_classic.BurstActive))
                {
                    break;   // one shot per cycle; a finished burst waits for its delay
                }
            }

            // Held but not able to shoot (empty, reloading) ends the burst of sound; a cooldown gap does not.
            if (shots > 0) SetFiring(true);
            else if (!_classic.HasAmmo || _classic.IsReloading) SetFiring(false);

            _classic.ClampCooldown();
        }

        /// <summary>WP-069: publish the fire start / stop edges once per change.</summary>
        private void SetFiring(bool firing)
        {
            if (_firing == firing)
            {
                return;
            }

            _firing = firing;
            if (firing) WeaponEvents.PublishFireStarted(_equippedId);
            else WeaponEvents.PublishFireStopped(_equippedId);
        }

        /// <summary>WP-087 (C-00 05.10): counts the classic shots fired (one per FireClassicOnce, a burst's each); the mechanism events and
        /// VFX's CasingEvents.Launched carry it, so the casing sound can find the casing the VFX threw for that shot.</summary>
        public int ShotSerial { get; private set; }

        private void FireClassicOnce()
        {
            ShotSerial++;
            // WP-042: top-down, the shot leaves at a fixed height above the root (hit-region parity across
            // weapon models / holds); the visual muzzle only feeds the flash. WP-069 (ADR-069-1 §7): in the
            // look modes the projectile leaves the muzzle towards the camera's aim point (parallax corrected).
            Vector3 muzzle = Muzzle.position;
            bool lookMode = CameraModeService.Current != CameraMode.TopDown;
            Vector3 origin = lookMode ? muzzle : new Vector3(muzzle.x, transform.root.position.y + _shotRayHeight, muzzle.z);
            Vector3 aim = ResolveAimDirection();

            // Steps 1 and 3: procedural recoil and mechanical movement. Two layers, on
            // purpose - the gun kicks (rig) and the character absorbs it (host). Either
            // one alone reads as half a shot.
            WeaponFeelId feelId = WeaponFeelIds.From(_equippedId);
            WeaponFeel feelRow = FeelRuntime.Current.Weapon(feelId);
            // C-00 01.10: in first person the view-model kick is FpWeaponFeel's A4.2 row alone — the rig's own kick on top of it
            // was a double count (sg_1 16.6° total). Third person / top-down keep the rig kick.
            if (_classicRig != null && CameraModeService.Current != CameraMode.FirstPerson)
            {
                // M3: the rig column (camera strength below keeps recoilKickMeters); WP-087 C: the row's arms back gain (the hold's channel)
                _classicRig.PlayFireMotion(feelRow.rigKickMeters, feelRow.rigRecoverySeconds, feelRow.rigUpShare,
                    FeelRuntime.Current.Recoil.HeldKickBackGain(feelId));
            }

            _host?.OnWeaponFired(RecoilStrength01(feelRow.recoilKickMeters, _classic.Power), aim);

            // Step 2: muzzle flash.
            float powerScale = PowerScale(_classic.Power);
            HitFeedbackDirector.Instance.ReportMuzzleFlash(
                Muzzle, _muzzleFlashSize * powerScale, _muzzleFlashLight * powerScale);

            // Step 4 and everything downstream, once per pellet — WP-069 ADR-069-1: as projectiles that
            // fly at the weapon's speed and resolve their hits (and branches) where they land.
            // Review R1-A #2: a ray never reports the collider it starts in, so a muzzle already inside a wall or
            // a point-blank body would pass through it. Check the stretch from inside the player to the muzzle
            // first; when something is there, every pellet starts inside the player and resolves it on its
            // first step (the wall stops it, the body takes the hit — the WP-076 grab escape included).
            Vector3 interior = transform.root.position + Vector3.up * _shotRayHeight;
            bool blocked = MuzzlePathBlocked(interior, origin, out Vector3 blockedDirection);

            int pellets = _classic.PelletCount;
            int presentationGroupId = FeelEventRouter.NextGroupId();
            for (int i = 0; i < pellets; i++)
            {
                ShotRequest request = _classic.BuildShot(origin, aim, i);
                if (blocked)
                {
                    request.Origin = interior;
                    request.Direction = blockedDirection;
                }

                request.HasTracerOrigin = true;
                request.TracerOrigin = muzzle;
                request.PresentationGroupId = presentationGroupId;
                ProjectileSystem.Spawn(request, _classic.Launch(i));
            }

            // Step 11: controlled camera response, scaled by weapon weight and clamped
            // again inside the bridge so aim survives.
            ApplyFireShake(feelRow.shakeWeight, _classic.Power, aim);

            // Step 12: the fire layer of the layered sound.
            // WP-072: the cue names the weapon (WP-069 WeaponDefinition.AudioId; A/B/C → AR1/SMG1/SG1 without a definition).
            AudioEvents.RaiseWeapon(CombatAudioEvent.ClassicWeaponFire, _classic.Definition?.AudioId ?? WeaponAudioIdentity.ForClassic(_classic.Id), origin, powerScale, 50);

            _classic.ConsumeShot();

            // WP-095 (R3): the feel layers' one shot event — one per trigger pull, after the round is spent (AmmoLeft = what the HUD shows next)
            FeelEvents.RaiseShot(new FeelShot(false, _equippedId, default, feelId, origin, aim, pellets, presentationGroupId, ShotSerial,
                powerScale, _classic.Ammo, _classic.MagazineSize));

            // WP-069 B2: game recoil. Look modes kick the camera (the rig drains and recovers it);
            // top-down keeps it on the shot as horizontal drift (ClassicWeaponRuntime.RecoilYawDegrees).
            if (lookMode && (_classic.LastRecoilPitch != 0f || _classic.LastRecoilYaw != 0f))
            {
                CameraReactions.AddRecoil(_classic.LastRecoilPitch, _classic.LastRecoilYaw);
            }

            FpFeelShot(_classicFeel, _classic.Definition);   // WP-069 FP feel: the view-model kick (A4.2)

            // WP-042 / WP-042C: the casing slot. A/B eject at the SHOT — always from here, never from the
            // clip: the 6/5-frame additive fire clip is retriggered per shot and drops its frame-1 event
            // under auto fire (measured on WP-032C: 3 shots → 2 events). C ejects at the pump-back —
            // the clip's eject event when the character carries it, else the 0.40 s timer.
            // C-00 30.09 M-21: a break-open (sg_2) ejects nothing at the shot — both shells leave when it opens (reload).
            if (_timeline.ShotCasings > 0)
            {
                if (_timeline.CasingDelaySeconds <= 0f) PublishMechanism(WeaponMechanismKind.CasingEjected, false);
                else
                {
                    // WP-087: owed until paid — by the clip's eject when it plays (third person), else by the deadline
                    _casingOwed = true;
                    _casingSerial = ShotSerial;
                    _casingAt = Time.time + _timeline.CasingDelaySeconds + (ClipDrivesMechanism ? PumpCasingGraceSeconds : 0f);
                }
            }
        }

        // ------------------------------------------------------------------ reload presentation

        /// <summary>
        /// Everything a reload LOOKS and SOUNDS like. Driven from the two edges of
        /// <see cref="ClassicWeaponRuntime.IsReloading"/> rather than from the call sites,
        /// so a reload started by the reload key, by an empty trigger pull or by a future
        /// caller all present identically - and so a reload cut short by the Special
        /// Weapon still tidies up after itself.
        /// </summary>
        private void TickReloadPresentation()
        {
            bool reloading = _classic.IsReloading;

            if (reloading && !_wasReloading)
            {
                _reloadInsertFired = false;
                _magOutFired = false;
                _seatsFired = 0;
                _seatsFromClip = 0;
                _chamberFired = false;
                if (MagazineReload)
                {
                    // WP-082: the magazine's owner (rl_1: an empty tube); §12 (C-00 05.10): a tactical reload has three beats, no bolt release
                    _magazine.Begin(_equippedId == ClassicWeaponId.Rl1, !_classic.ReloadIsEmpty);
                    _magazineVisual.Bind(_equippedId == ClassicWeaponId.Rl1 || _classicRig == null ? null : _classicRig.transform);
                    _magazineMode = CameraModeService.Current;
                }

                if (_classicRig != null)
                {
                    _classicRig.PlayReloadMotion(_classic.ReloadDuration);
                }

                _host?.OnReloadStarted(_classic.ReloadDuration);
                FeelEvents.RaiseReloadStarted(new FeelReload(false, _equippedId, default, _classic.ReloadDuration,
                    _classic.ReloadIsEmpty, false, Muzzle.position));   // WP-095: every start, whatever started it
            }
            else if (!reloading && _wasReloading)
            {
                // WP-069 B2: a reserve-limited or per-shell reload can run to term without a full
                // magazine, so the runtime's own flag is the "completed" signal.
                bool completed = _classic.LastReloadCompleted;
                if (completed) _magazine.Tick(1f, _classic.ReloadDuration);   // WP-082: the beats a clip did not send land first
                _magazine.End();   // complete / cancelled / switched / a Special: the magazine is in the weapon
                _magazineVisual.Home();

                if (_classicRig != null)
                {
                    _classicRig.CancelReloadMotion();
                }

                _host?.OnReloadFinished(completed);
                FeelEvents.RaiseReloadEnded(new FeelReload(false, _equippedId, default, _classic.ReloadDuration, false, completed,
                    Muzzle.position));   // WP-095: every exit — completed, cut by the trigger, switched, a Special
            }
            else if (reloading && !_reloadInsertFired
                     && _classic.ReloadProgress01 >= ReloadInsertProgress)
            {
                // The middle layer of the three-part reload sound. Without it a reload is
                // two clicks with a silent gap, which reads as a stutter rather than as a
                // magazine going in.
                _reloadInsertFired = true;
                AudioEvents.Raise(CombatAudioEvent.ReloadInsert, Muzzle.position, 1f, 40);
            }

            if (reloading && _magazine.Active && CameraModeService.Current != _magazineMode)
            {
                // WP-082: a camera change mid-reload ends the ownership — the hand that held the magazine is not the one shown now
                _magazine.End();
                _magazineVisual.Home();
            }

            if (reloading) _magazine.Tick(_classic.ReloadProgress01, _classic.ReloadDuration);
            _wasReloading = reloading;
        }

        /// <summary>
        /// WP-082 visual: a landed beat moves the magazine part. Detach → the hand; hand_attach → the spent magazine leaves the
        /// hand (MagazineDropped, here at the hand — no longer at mag_out) and the part stays as the fresh one; insert → home.
        /// rl_1's tube has nothing to drop.
        /// </summary>
        private void OnMagazineStep(MagazineStep step, bool fromEvent)
        {
            switch (step)
            {
                case MagazineStep.Detach:
                    _magazineVisual.ToHand(MagazineHandSocket());
                    break;
                case MagazineStep.HandAttach:
                    if (_equippedId != ClassicWeaponId.Rl1)
                    {
                        Transform hand = _magazineVisual.Socket != null ? _magazineVisual.Socket : MagazineHandSocket();
                        if (hand != null) WeaponEvents.PublishMechanism(new WeaponMechanismEvent(WeaponMechanismKind.MagazineDropped, _equippedId, hand.position, Vector3.down, fromEvent, ShotSerial));
                        else PublishMechanism(WeaponMechanismKind.MagazineDropped, fromEvent);
                    }

                    _magazineVisual.ToHand(MagazineHandSocket());
                    break;
                case MagazineStep.Insert:
                    _magazineVisual.Home();
                    break;
            }
        }

        /// <summary>WP-082: the hand that carries the magazine in the camera mode shown — FP arms when they are shown, else the body.</summary>
        private Transform MagazineHandSocket()
        {
            if (CameraModeService.Current == CameraMode.FirstPerson)
            {
                FirstPersonArms arms = FirstPersonArms.Shown;
                return arms != null && arms.Visible ? arms.MagazineSocket : null;   // FP without arms: the magazine stays in the weapon
            }

            PlayerSkinnedVisual body = SkinnedVisual();
            return body != null ? body.MagazineSocket : null;
        }

        /// <summary>WP-082 readout: the magazine part's visual (tests, S-10 H3).</summary>
        public WeaponMagazineVisual MagazineVisual => _magazineVisual;

        /// <summary>WP-082: the classic weapon reloads a magazine (or rl_1's tube) — not shell by shell, not a break-open.</summary>
        private bool MagazineReload => !_timeline.Shells && _timeline.ReloadCasings <= 0;

        /// <summary>§12 readout: seats the clip sent this reload (mag_in / mag_insert / shell_in / rocket_in — one per cartridge; tests, S-08).</summary>
        public int SeatsFromClipThisReload => _seatsFromClip;

        /// <summary>WP-082 readout: who holds the classic weapon's magazine during a reload (tests, S-10 H3 counters).</summary>
        public WeaponMagazineOwnership Magazine => _magazine;

        /// <summary>
        /// PROTOTYPE-ONLY: point in the reload at which the magazine-insert layer plays.
        /// Past halfway so the three layers are audibly spaced even on the shotgun's
        /// short reload.
        /// </summary>
        private const float ReloadInsertProgress = 0.62f;

        // ------------------------------------------------------------------ special firing

        private void TickSpecialFiring(bool fireHeld, bool firePressed, bool reloadPressed, float dt)
        {
            if (_specialRig != null)
            {
                _specialRig.SetSpin(fireHeld ? 1f : 0.15f);
            }

            if (!CanFireNow())
            {
                _special.ClampCooldown();
                return;
            }

            if (reloadPressed)
            {
                _special.TryBeginReload();
            }

            // WP-069 B4: one shot per cycle at most — Raiju charges first (release cancels), Namekuji is single-fire.
            if (_special.UpdateTrigger(fireHeld, firePressed, dt))
            {
                FireSpecialOnce();
            }

            _special.ClampCooldown();
        }

        /// <summary>WP-069 B4: Raiju's self-shock slows the player for <paramref name="seconds"/> (DIRECTION §7a).</summary>
        public void ApplySpecialSelfSlow(float seconds)
        {
            if (seconds > 0f)
            {
                _selfSlowUntil = Mathf.Max(_selfSlowUntil, Time.time + seconds);
            }
        }

        /// <summary>
        /// Review R1-A #2: true when world geometry or a body hitbox lies between a point inside the player
        /// (<paramref name="interior"/>) and the shot origin; <paramref name="direction"/> then points at it.
        /// </summary>
        public static bool MuzzlePathBlocked(Vector3 interior, Vector3 shotOrigin, out Vector3 direction)
        {
            Vector3 delta = shotOrigin - interior;
            float distance = delta.magnitude;
            direction = distance > 1e-4f ? delta / distance : Vector3.forward;
            if (distance <= 1e-3f)
            {
                return false;
            }

            // C-00 30.09: a body capsule between the player and the muzzle counts too (point-blank; the round then starts inside the
            // player and meets the capsule on its first step — HitscanEngine.CapsuleOnlyHit).
            int mask = DD.Prototype.Core.PrototypeLayers.BulletHitMask | (1 << DD.Prototype.Core.PrototypeLayers.Enemy);
            if (!Physics.Raycast(interior, direction, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            Vector3 toHit = hit.point - interior;
            if (toHit.sqrMagnitude > 1e-6f)
            {
                direction = toHit.normalized;
            }

            return true;
        }

        private void FireSpecialOnce()
        {
            // Same origin rule as the classic weapon: top-down a fixed height above the root (region parity), the
            // look modes the muzzle along the camera's aim.
            Vector3 muzzle = Muzzle.position;
            bool lookMode = CameraModeService.Current != CameraMode.TopDown;
            Vector3 origin = lookMode ? muzzle : new Vector3(muzzle.x, transform.root.position.y + _shotRayHeight, muzzle.z);
            Vector3 aim = ResolveAimDirection();
            SpecialWeaponId id = _special.Id;
            WeaponFeel feelRow = FeelRuntime.Current.Weapon(WeaponFeelIds.From(id));

            if (_specialRig != null)
            {
                if (CameraModeService.Current != CameraMode.FirstPerson) _specialRig.PlayFireMotion(feelRow.rigKickMeters, feelRow.rigRecoverySeconds, feelRow.rigUpShare);   // C-00 01.10: FP = A4.2 only
            }

            _host?.OnWeaponFired(SpecialWeaponRuntime.RecoilStrength01, aim);

            HitFeedbackDirector.Instance.ReportMuzzleFlash(
                Muzzle,
                _muzzleFlashSize * _specialFlashMultiplier,
                _muzzleFlashLight * _specialFlashMultiplier);

            bool hasAimPoint = _playerAim != null && _playerAim.HasAimPoint;
            Vector3 aimPoint = hasAimPoint ? _playerAim.AimWorldPoint : origin + aim * 10f;
            _special.Fire(origin, aim, aimPoint, hasAimPoint);

            // WP-069 × WP-073 A4.2 (C-00 25.09): the Special's game recoil through the classic path — look modes kick the camera.
            _special.ApplyGameRecoil();
            if (lookMode && (_special.LastRecoilPitch != 0f || _special.LastRecoilYaw != 0f))
            {
                CameraReactions.AddRecoil(_special.LastRecoilPitch, _special.LastRecoilYaw);
            }

            FpFeelShot(_specialFeel, _special.Definition);   // WP-069 FP feel: the view-model kick (A4.2)

            ApplyFireShake(feelRow.shakeWeight, WeaponPower.Heavy, aim);

            AudioEvents.Raise(CombatAudioEvent.HeavyWeaponFire, origin, 1f, 60);

            // WP-056 / WP-069 B4: Fired (radius = the chain hop / puddle radius, seconds = the chain run / puddle life);
            // Raiju's orb flight is a field that ends at its contact or fizzle (SpecialImpacts).
            SpecialWeaponUsageEvents.Publish(id, SpecialUsageKind.Fired, SpecialWeaponUsage.For(id).FireVfxFamily,
                origin, aim, _special.FireRadius, _special.FireSeconds, 0);
            if (id == SpecialWeaponId.Raiju && _special.Definition != null)
            {
                SpecialWeaponUsageEvents.Publish(id, SpecialUsageKind.FieldSpawned, SpecialWeaponUsage.For(id).FieldVfxFamily,
                    origin, aim, _special.Definition.projectile.sweepRadius, _special.Definition.projectile.maxLifetimeSeconds, 0);
            }

            _special.ConsumeShot();
            FeelEvents.RaiseShot(new FeelShot(true, default, id, WeaponFeelIds.From(id), origin, aim, 1, 0, 0, 1f, _special.Ammo,
                _special.MagazineSize));   // WP-095: the feel layers' one shot event
        }

        private void HandleSpecialEnded()
        {
            // REQ-057: the classic weapon comes back automatically, with the magazine it
            // had when it was parked.
            _pickupDonePending = false;
            SpecialWeaponUsageEvents.Publish(_special.Id, SpecialUsageKind.Deactivated, SpecialWeaponUsage.For(_special.Id).FireVfxFamily,
                transform.position, transform.forward, 0f, 0f, 0);
            SetRigVisibility();
            PublishClassicState(true);
            PublishSpecialState(true);
            SkinnedVisual()?.PlaySwitchIn();   // WP-069 (S-06 B4b): the parked classic comes back up
            _clipMotionUntil = Time.time + FpClipMotionSeconds;
        }

        // ------------------------------------------------------------------ shared

        private bool CanFireNow()
        {
            // WP-070 §5.1: fire / reload / Special are swallowed while the camera blends between modes.
            if (CameraModeService.IsTransitioning)
            {
                return false;
            }

            return _aimSource == null || _aimSource.CanFire;
        }

        private Vector3 ResolveAimDirection()
        {
            Vector3 direction = _aimSource != null ? _aimSource.AimDirection : transform.forward;

            // WP-069: only the top-down mode is planar; third / first person aim in 3D (pitch).
            if (_flattenAim && CameraModeService.Current == CameraMode.TopDown)
            {
                direction.y = 0f;
            }

            return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        }

        private void ApplyFireShake(float scale, WeaponPower power, Vector3 aimDirection)
        {
            if (scale <= 0f)
            {
                return;
            }

            // WP-095 (R3): top-down fire shake is K1 / K3 of TopDownFeelCamera (fed by FeelEvents.ShotFired); this path would
            // shake a second time. First / third person keep it.
            if (CameraModeService.Current == CameraMode.TopDown)
            {
                return;
            }

            FireShakeFeel feel = FeelRuntime.Current.FireShake;
            float amplitude = scale * feel.metersAtWeightOne;
            float duration = feel.baseSeconds + feel.secondsPerWeight * scale;

            ShakeProfile profile =
                power == WeaponPower.Heavy && _special.IsActive ? ShakeProfile.HeavyFire :
                power == WeaponPower.Heavy ? ShakeProfile.ShotgunFire :
                ShakeProfile.ClassicFire;

            // The shotgun is the one weapon whose response is a single directed kick
            // rather than a rattle, so it is the one that gets a direction. Passing a
            // direction for the automatics would turn sustained fire into a drift, which
            // is exactly what REQ-126 forbids - the camera is allowed to shake, never to
            // walk the frame off the cursor.
            if (profile == ShakeProfile.ShotgunFire)
            {
                CameraShakeBridge.Shake(amplitude, duration, profile, -aimDirection);
                return;
            }

            CameraShakeBridge.Shake(amplitude, duration, profile);
        }

        /// <summary>
        /// PROTOTYPE-ONLY: recoil kick, which is a metres-of-weapon-travel number, mapped
        /// onto the 0-1-ish scale the character animator wants. The shipped kicks are
        /// roughly 0.05 (SMG) to 0.20 (shotgun), so the divisor lands the shotgun near 1
        /// and the SMG near 0.25 - which is the example pair in the animator's own docs.
        /// </summary>
        private static float RecoilStrength01(float recoilKick, WeaponPower power)
        {
            float normalised = recoilKick / Mathf.Max(0.05f, FeelRuntime.Current.Recoil.animatorFullStrengthKickMeters);

            // A weapon with no authored kick still has weight; fall back to its power
            // class so a config with RecoilKick left at zero does not silence the
            // character's reaction entirely.
            if (normalised <= 0.01f)
            {
                normalised = power == WeaponPower.Heavy ? 0.9f
                    : power == WeaponPower.Light ? 0.25f
                    : 0.5f;
            }

            return Mathf.Clamp(normalised, 0.05f, 1.5f);
        }

        private static float PowerScale(WeaponPower power)
        {
            switch (power)
            {
                case WeaponPower.Heavy: return 1.5f;
                case WeaponPower.Light: return 0.75f;
                default: return 1f;
            }
        }

        // ------------------------------------------------------------------ HUD publishing

        private void PublishClassicState(bool notify)
        {
            var state = new ClassicWeaponHudState
            {
                Id = _classic.Id,
                DisplayName = _classic.DisplayName,
                AmmoInMagazine = _classic.InfiniteAmmo ? _classic.MagazineSize : _classic.Ammo,
                MagazineSize = _classic.MagazineSize,
                IsReloading = _classic.IsReloading,
                ReloadProgress01 = _classic.ReloadProgress01,
                ReloadRemaining = _classic.ReloadRemaining,
                ReloadDuration = _classic.ReloadDuration,
                TemporarilyDisabled = _special.IsActive,
                BuildDescription = _modifiers.DescribeBuild(),
                CurrentSpreadDegrees = _classic.CurrentSpreadDegrees,   // WP-068 U12a honest reticle
                PelletCount = _classic.PelletsPerShell,   // WP-077: per shell — the ladder must not re-lay on every sg_2 seal pull
                PelletRingDegrees = _classic.PelletRingDegrees,
                ReserveAmmo = _classic.ReserveAmmo,
                ReserveInfinite = _classic.ReserveInfinite,
                FireMode = _classic.FireMode,
                AdsWeight01 = _classic.Ads01,
                FloorRefill01 = _classic.FloorRefill01
            };

            WeaponEvents.PublishClassic(state, notify);
        }

        private void PublishSpecialState(bool notify)
        {
            SpecialWeaponHudState state = WeaponEvents.Special;

            if (_special.IsActive)
            {
                state.Phase = SpecialWeaponPhase.Active;
                state.WeaponId = _special.Id;
                state.DisplayName = _special.DisplayName;
                state.ActiveRemaining = _special.Remaining;
                state.ActiveDuration = _special.Duration;
                state.FinalSecondsWarning = _special.InFinalSeconds;
                // WP-069 B4 (DEC-097): magazine + reserve counter, charge, reload.
                state.AmmoInMagazine = _special.Ammo;
                state.MagazineSize = _special.MagazineSize;
                state.ReserveAmmo = _special.ReserveAmmo;
                state.IsReloading = _special.IsReloading;
                state.ReloadProgress01 = _special.ReloadProgress01;
                state.Charge01 = _special.Charge01;
                state.Limit = _special.Definition != null ? _special.Definition.specialLifecycle.limit : SpecialLimit.Time;

                // The ground phase is over; clearing it stops the HUD from drawing a
                // stale world marker under the player's feet.
                state.GroundRemaining = 0f;
                state.GroundDuration = 0f;
                state.GroundBlinkPhase = 0f;

                WeaponEvents.PublishSpecial(state, notify);
                return;
            }

            if (state.Phase != SpecialWeaponPhase.Active)
            {
                // Not our phase. The pickup owns the snapshot while the weapon is on the
                // ground, and nobody owns it while there is none - so write nothing.
                return;
            }

            state.Phase = SpecialWeaponPhase.None;
            state.ActiveRemaining = 0f;
            state.FinalSecondsWarning = false;
            WeaponEvents.PublishSpecial(state, true);
        }

        // ------------------------------------------------------------------ rig setup

        private void EnsureRigs()
        {
            if (_classicRig == null)
            {
                _classicRig = CreateRig("ClassicWeaponRig", Vector3.zero);
            }

            if (_specialRig == null)
            {
                _specialRig = CreateRig("SpecialWeaponRig", Vector3.zero);
                if (_specialRig.Body != null)
                {
                    // A visibly bigger silhouette, so the swap reads instantly.
                    _specialRig.Body.localScale = new Vector3(1.5f, 1.5f, 1.35f);
                }
            }

            SetRigVisibility();
        }

        private WeaponVisualRig CreateRig(string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(RigParent, false);
            go.transform.localPosition = localPosition;
            return go.AddComponent<WeaponVisualRig>();
        }

        /// <summary>
        /// The character's weapon mount when there is one, else this transform. Going
        /// through the mount is what makes the weapon inherit the character's aim sway
        /// and reload pose instead of floating beside it.
        /// </summary>
        private Transform RigParent
        {
            get
            {
                Transform mount = _host != null ? _host.WeaponMount : null;
                return mount != null ? mount : transform;
            }
        }

        /// <summary>
        /// Move existing rigs onto the mount. Needed because the host can arrive after
        /// Awake has already built the placeholders.
        /// </summary>
        private void ReparentRigs()
        {
            Transform parent = RigParent;

            if (_classicRig != null && _classicRig.transform.parent != parent)
            {
                _classicRig.transform.SetParent(parent, false);
                _classicRig.transform.localPosition = Vector3.zero;
                _classicRig.transform.localRotation = Quaternion.identity;
            }

            if (_specialRig != null && _specialRig.transform.parent != parent)
            {
                _specialRig.transform.SetParent(parent, false);
                _specialRig.transform.localPosition = Vector3.zero;
                _specialRig.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// WP-037 (AP-014): put the authored hand model of the active Special Weapon on the
        /// special rig (once per weapon id; the placeholder primitives stay for a weapon
        /// without a model — Tide Jewel — or without the library).
        /// </summary>
        private SpecialWeaponId _specialModelId = (SpecialWeaponId)(-1);

        private void EnsureSpecialModel()
        {
            if (_specialRig == null || !_special.IsActive || _specialModelId == _special.Id)
            {
                return;
            }

            SpecialWeaponVisualLibrary library = SpecialWeaponVisualLibrary.Current;
            SpecialWeaponVisualLibrary.Entry entry = library != null ? library.Find(_special.Id) : null;
            if (entry == null || !entry.HasHand)
            {
                return;
            }

            // WP-069 B4c: the AP-021 model (ADR-042-1 frame, own sockets — the rig takes its Muzzle) before the AP-014 hand meshes.
            GameObject model = entry.HasModel
                ? SpecialWeaponVisualLibrary.InstantiateModel(entry, null, s_handLodHeights)
                : library.material != null ? SpecialWeaponVisualLibrary.Instantiate("Model", entry.handLods, library.material, null, s_handLodHeights) : null;
            if (model == null)
            {
                return;
            }

            _specialRig.SetModel(model);
            _specialModelId = _special.Id;
        }

        private static readonly float[] s_handLodHeights = { 0.15f, 0.06f, 0.01f };

        /// <summary>WP-037 readout: the special rig carries the AP-014 hand model (tests / probe).</summary>
        public bool SpecialRigHasAuthoredModel => _specialRig != null && _specialRig.HasAuthoredModel;

        /// <summary>WP-069 B4c readout: the special rig's muzzle (the AP-021 model's Muzzle socket once installed).</summary>
        public Transform SpecialRigMuzzle => _specialRig != null ? _specialRig.Muzzle : null;

        /// <summary>WP-069 B4c readout: the special rig's model root (tests).</summary>
        public Transform SpecialRigBody => _specialRig != null ? _specialRig.Body : null;

        /// <summary>WP-037 readout: the special rig body is visible (tests).</summary>
        public bool SpecialRigVisible => _specialRig != null && _specialRig.Body != null && _specialRig.Body.gameObject.activeSelf;

        private void SetRigVisibility()
        {
            bool special = _special.IsActive;
            if (special)
            {
                EnsureSpecialModel();
            }

            if (_classicRig != null)
            {
                _classicRig.SetVisible(!special);
            }

            if (_specialRig != null)
            {
                // WP-042: hidden until the grab clip hands the weapon to the right hand (pickup_done / fallback).
                _specialRig.SetVisible(special && !_pickupDonePending);
                _specialRig.SetSpin(special ? 0.15f : 0f);
            }
        }

        // ------------------------------------------------------------------ WP-042 · classic model (ADR-042-1)

        /// <summary>
        /// Put the roster model of the equipped classic weapon on the classic rig (once per weapon
        /// id): the AP-009C blockout today, the AP-013 model when it replaces the resource — same
        /// sockets, so the muzzle / ejection / mag markers come from the model.
        /// </summary>
        private void EnsureClassicModel()
        {
            if (_classicRig == null || _classicModelId == _equippedId)
            {
                return;
            }

            // WP-042B: the AP-013 model from the library first; the AP-009C blockout (roster
            // ModelResource) for a weapon the library does not carry.
            ClassicWeaponVisualLibrary library = ClassicWeaponVisualLibrary.Current;
            ClassicWeaponVisualLibrary.Entry entry = library != null ? library.Find(_equippedId) : null;

            // WP-069 B2: a v3 weapon without its own AP-013 model borrows its class sibling's until the AP-021 import.
            if (entry == null && library != null && _classic.Definition != null)
            {
                entry = library.Find(_classic.Definition.presentation.placeholderVisual);
            }

            GameObject model = null;
            if (entry != null && entry.HasModel)
            {
                model = ClassicWeaponVisualLibrary.Instantiate(entry, library.material, s_classicLodHeights);
                ClassicModelSource = ClassicModelKind.Authored;
            }
            else
            {
                ClassicWeaponDefinition definition = WeaponRoster.Find(_equippedId);
                if (definition == null || string.IsNullOrEmpty(definition.ModelResource))
                {
                    return;
                }

                var prefab = Resources.Load<GameObject>(definition.ModelResource);
                if (prefab == null)
                {
                    Debug.LogWarning("[WeaponController] classic model resource missing: " + definition.ModelResource);
                    return;
                }

                model = Instantiate(prefab);
                model.name = "Model";
                ClassicModelSource = ClassicModelKind.Blockout;
            }

            // WP-087 (C-00 06.10, B-03 review finding): a magazine part a reload left in the left hand goes home into the model it
            // belongs to BEFORE SetModel destroys that model (a switch mid-reload, a QA / debug equip after this frame's Update) —
            // otherwise the part outlives its model under the hand's socket and the old magazine rides along in the hand for good
            _magazine.End();
            _magazineVisual.Home();
            _classicRig.SetModel(model);
            _classicModelId = _equippedId;
        }

        /// <summary>WP-042B: LOD transition heights for the weapon in hand (LOD0 ≥ 12 % of the screen, LOD1 ≥ 5 %, LOD2 ≥ 1 %). Screen-height fractions, not tunables.</summary>
        private static readonly float[] s_classicLodHeights = { 0.12f, 0.05f, 0.01f };

        public enum ClassicModelKind { None = 0, Blockout = 1, Authored = 2 }

        /// <summary>WP-042B readout: which model the classic rig carries (tests / probe).</summary>
        public ClassicModelKind ClassicModelSource { get; private set; }

        // ------------------------------------------------------------------ WP-069 · the FP view pose

        private readonly FpViewPoseState _classicFpPose = new FpViewPoseState();
        private readonly FpViewPoseState _specialFpPose = new FpViewPoseState();

        // WP-069 · the FP weapon feel layer (C-00 26.09): ADS pose + lens, sway, bob, view-model kick on top of fpView.
        private readonly FpWeaponFeel _classicFeel = new FpWeaponFeel();
        private readonly FpWeaponFeel _specialFeel = new FpWeaponFeel();
        private bool _feelHasLast;
        private float _feelLastYaw, _feelLastPitch;
        private Vector3 _feelLastPos;
        private uint _feelShots;
        private float _clipMotionUntil = -1f, _clipMotionWeight;

        /// <summary>How long after a switch the mount's clip motion carries into the FP view model (the switch clips run ≤ 0.6 s).</summary>
        public const float FpClipMotionSeconds = 0.6f;

        /// <summary>WP-087 readout: the switch / reload clip motion's weight on the FP weapon this frame (0 … 1; tests).</summary>
        public float FpClipMotionWeight => _clipMotionWeight;

        /// <summary>WP-069 readout: the classic's FP feel layer (tests).</summary>
        public FpWeaponFeel ClassicFeel => _classicFeel;

        /// <summary>WP-069 readout: the Special's FP feel layer (tests).</summary>
        public FpWeaponFeel SpecialFeel => _specialFeel;

        /// <summary>
        /// WP-069 × WP-071 B4b (C-00 25.09): the first-person arms' weapon socket while S-06's FirstPersonArms is active (it
        /// sets this; null otherwise). With it the FP weapon follows the arms — root = socket − R·gripOffset (× the pose's
        /// scale), R the socket's rotation, no added kick (the clip carries it); without it the fpView pose places the weapon.
        /// The socket belongs to the classic's FP set: a Special in hand ignores it and keeps its own fpView pose.
        /// fpView stays the authoring target and the single owner of FP placement: P-06's FP rest clip puts the weapon at the
        /// fpView pose, and FP_GorusPozu gates the frame with the arms on too.
        /// </summary>
        public static Transform FirstPersonWeaponSocket { get; set; }

        /// <summary>WP-069 readout: the classic rig sits at its FP view pose this frame (tests).</summary>
        public bool ClassicFpViewPosed => _classicFpPose.Posed;

        /// <summary>WP-069 readout: the Special rig sits at its FP view pose this frame (tests).</summary>
        public bool SpecialFpViewPosed => _specialFpPose.Posed;

        /// <summary>Called by <see cref="FpViewPoseDriver"/> after the mount alignment and the grip IK (order 950).</summary>
        internal void ApplyFpViewPose()
        {
            Transform character = _motor != null ? _motor.transform : transform;
            TickFpFeel(character);
            float clip = _clipMotionWeight;
            _classicFpPose.Tick(_classicRig, _classic != null ? _classic.Definition : null, character, true, _classicFeel, clip);
            // the arms' socket is the classic's FP set — a Special in hand keeps its own fpView pose
            _specialFpPose.Tick(_specialRig, _special.IsActive ? _special.Definition : null, character, false, _specialFeel, clip);
        }

        /// <summary>
        /// WP-069 FP feel: look rates from the viewmodel anchor, planar speed from the character, the gameplay ADS weight; the ADS
        /// lens for the camera (FpAdsLens); the clip-motion weight (switch / reload only — the fire kick is the feel layer's).
        /// </summary>
        private void TickFpFeel(Transform character)
        {
            Transform anchor = CameraView.ViewmodelAnchor;
            float dt = Time.deltaTime;
            if (anchor == null)
            {
                _classicFeel.Reset();
                _specialFeel.Reset();
                _feelHasLast = false;
                _clipMotionWeight = 0f;
                FpAdsLens.Ads01 = 0f;
                FpAdsLens.WorldFovScale = 1f;
                return;
            }

            Vector3 f = anchor.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 pos = character.position;
            float yawRate = 0f, pitchRate = 0f, speed = 0f;
            if (_feelHasLast && dt > 0f)
            {
                yawRate = Mathf.DeltaAngle(_feelLastYaw, yaw) / dt;
                pitchRate = (pitch - _feelLastPitch) / dt;
                Vector3 d = pos - _feelLastPos;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _feelLastYaw = yaw;
            _feelLastPitch = pitch;
            _feelLastPos = pos;
            _feelHasLast = true;

            bool reduced = CameraModeService.Settings != null && CameraModeService.Settings.ReducedMotion;
            WeaponDefinition classicDef = _classic != null ? _classic.Definition : null;
            if (classicDef != null)
            {
                _classicFeel.Tick(dt, yawRate, pitchRate, speed, _special.IsActive ? 0f : _classic.Ads01, CameraModesFeel.For(classicDef.weaponId).Weight, reduced);
            }

            WeaponDefinition specialDef = _special.IsActive ? _special.Definition : null;
            if (specialDef != null) _specialFeel.Tick(dt, yawRate, pitchRate, speed, 0f, CameraModesFeel.For(specialDef.weaponId).Weight, reduced);
            else _specialFeel.Reset();

            float ads = specialDef == null && classicDef != null ? _classicFeel.AdsEased : 0f;
            FpAdsLens.Ads01 = ads;
            FpAdsLens.WorldFovScale = classicDef != null ? Mathf.Lerp(1f, FpWeaponFeel.AdsWorldFovScale(classicDef.weaponClass), ads) : 1f;

            bool clipMotion = Time.time < _clipMotionUntil || (_classic != null && _classic.IsReloading);
            _clipMotionWeight = Mathf.MoveTowards(_clipMotionWeight, clipMotion ? 1f : 0f, dt > 0f ? dt / 0.1f : 1f);
        }

        /// <summary>WP-069 FP feel: one view-model kick for the weapon that fired (first person only; the yaw side is a deterministic hash).</summary>
        private void FpFeelShot(FpWeaponFeel feel, WeaponDefinition def)
        {
            if (feel == null || def == null || CameraView.ViewmodelAnchor == null) return;
            _feelShots++;
            float side = ((_feelShots * 2654435761u) >> 16 & 1u) == 0u ? 1f : -1f;
            bool reduced = CameraModeService.Settings != null && CameraModeService.Settings.ReducedMotion;
            feel.Shot(def.weaponId, CameraModesFeel.For(def.weaponId).Weight, feel.AdsEased, side, reduced);
        }

        /// <summary>
        /// WP-069 (C-00 25.09) · while a rig's weapon (classic, and the Special in hand) is on the <see cref="CameraLayers.FirstPersonView"/> layer it
        /// sits at its definition's <see cref="WeaponFpViewPose"/> relative to the viewmodel camera (grip position, rotation,
        /// scale) instead of the hidden third-person hand — TIMES the mount's animated deviation from its rest aim
        /// (WeaponMountAlignment: mount = facing × delta), so the fire kick and the reload travel still turn it. Stays a
        /// child of the mount (the FP layer switch and the LOD0 pin keep working); off that layer the local pose goes back
        /// to identity. No allocation.
        /// </summary>
        private sealed class FpViewPoseState
        {
            private Renderer _probe;
            private bool _posed;

            public bool Posed => _posed;

            private WeaponDefinition _sightDef;
            private bool _hasSight;
            private Vector3 _sightLocal;

            public void Tick(WeaponVisualRig rig, WeaponDefinition definition, Transform character, bool followArms, FpWeaponFeel feel, float clipMotion)
            {
                if (rig == null)
                {
                    return;
                }

                Transform t = rig.transform;
                if (_probe == null || !_probe.gameObject.activeInHierarchy || !_probe.transform.IsChildOf(t))
                {
                    _probe = rig.GetComponentInChildren<Renderer>(false);
                }

                Transform anchor = CameraView.ViewmodelAnchor;
                bool firstPerson = anchor != null && definition != null && _probe != null && _probe.gameObject.layer == CameraLayers.FirstPersonView;
                if (!firstPerson)
                {
                    if (_posed)
                    {
                        t.localPosition = Vector3.zero;
                        t.localRotation = Quaternion.identity;
                        t.localScale = Vector3.one;
                        _posed = false;
                    }

                    return;
                }

                WeaponFpViewPose pose = definition.FpViewPose;
                Transform armSocket = followArms ? FirstPersonWeaponSocket : null;
                if (armSocket != null && armSocket.gameObject.activeInHierarchy)
                {
                    Quaternion r = armSocket.rotation;
                    t.SetPositionAndRotation(armSocket.position - r * (definition.GripOffset * pose.scale), r);
                    t.localScale = new Vector3(pose.scale, pose.scale, pose.scale);
                    _posed = true;
                    return;
                }

                // The mount's animated deviation (the TP clips) carries into FP only while switching / reloading — the fire
                // kick is the feel layer's A4.2 view-model kick (the TP fire clip turned the FP weapon ~17°).
                Quaternion kick = Quaternion.identity;
                Vector3 forward = character.forward;
                forward.y = 0f;
                if (clipMotion > 0f && t.parent != null && forward.sqrMagnitude > 1e-6f)
                {
                    kick = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(Quaternion.LookRotation(forward.normalized, Vector3.up)) * t.parent.rotation, clipMotion);
                }

                Vector3 viewPos = pose.position;
                Quaternion viewRot = Quaternion.Euler(pose.euler);
                if (feel != null)
                {
                    // ADS: the model's sight_ads (the eye point on the sight line) on the anchor, the barrel along the view.
                    float a = feel.AdsEased;
                    if (a > 0f && SightAds(t, definition, out Vector3 sight))
                    {
                        viewPos = Vector3.Lerp(viewPos, -(sight * pose.scale), a);
                        viewRot = Quaternion.Slerp(viewRot, Quaternion.identity, a);
                    }

                    viewPos += feel.Position;
                    viewRot = feel.Rotation * viewRot;
                }

                t.SetPositionAndRotation(anchor.TransformPoint(viewPos), anchor.rotation * viewRot * kick);
                t.localScale = new Vector3(pose.scale, pose.scale, pose.scale);
                _posed = true;
            }

            /// <summary>The model's sight_ads in the weapon frame (cached per definition; false when the model has none).</summary>
            private bool SightAds(Transform root, WeaponDefinition definition, out Vector3 local)
            {
                if (_sightDef != definition)
                {
                    _sightDef = definition;
                    Transform node = WeaponSocketContract.Find(root, definition.presentation.sockets.sightAds);
                    _hasSight = node != null;
                    _sightLocal = _hasSight ? root.InverseTransformPoint(node.position) : Vector3.zero;
                }

                local = _sightLocal;
                return _hasSight;
            }
        }

        // ------------------------------------------------------------------ WP-069 T3 · the FP weapon at LOD0

        private readonly ViewmodelLodPin _classicLodPin = new ViewmodelLodPin();
        private readonly ViewmodelLodPin _specialLodPin = new ViewmodelLodPin();

        /// <summary>WP-069 T3 readout: the classic rig's LODGroup is pinned to LOD0 (its weapon is on the FirstPersonView layer).</summary>
        public bool ClassicViewmodelLodPinned => _classicLodPin.Pinned;

        /// <summary>WP-069 T3 readout: the same for the Special rig.</summary>
        public bool SpecialViewmodelLodPinned => _specialLodPin.Pinned;

        private void LateUpdate()
        {
            _classicLodPin.Tick(_classicRig);
            _specialLodPin.Tick(_specialRig);
        }

        /// <summary>
        /// WP-069 T3 (S-01 WP-070V finding: the FP view drew AP013_A_rifle_LOD2). While a rig's weapon sits on the
        /// <see cref="CameraLayers.FirstPersonView"/> layer — the camera rig's FP presentation, blend edges included — its
        /// LODGroup is pinned to LOD0 with no cross-fade (the builders' animated CrossFade otherwise dithers the view model
        /// through the switch); off that layer the distance pick and the group's own fade come back. Per frame, no
        /// allocation; a model swap (equip, Special) destroys the old group and the next frame reads the new one.
        /// </summary>
        private sealed class ViewmodelLodPin
        {
            private LODGroup _group;
            private Renderer _probe;
            private bool _pinned;
            private LODFadeMode _fadeMode;
            private bool _animateFade;

            public bool Pinned => _pinned && _group != null;

            public void Tick(WeaponVisualRig rig)
            {
                if (rig == null)
                {
                    return;
                }

                if (_group == null || _probe == null || !_probe.gameObject.activeInHierarchy)
                {
                    if (_group != null && _pinned) Release();   // a hidden group is not left pinned
                    _group = rig.GetComponentInChildren<LODGroup>(false);
                    _probe = _group != null ? _group.GetComponentInChildren<Renderer>(false) : null;
                    _pinned = false;
                    if (_probe == null)
                    {
                        return;
                    }
                }

                bool firstPerson = _probe.gameObject.layer == CameraLayers.FirstPersonView;
                if (firstPerson == _pinned)
                {
                    return;
                }

                if (firstPerson)
                {
                    _fadeMode = _group.fadeMode;
                    _animateFade = _group.animateCrossFading;
                    _group.fadeMode = LODFadeMode.None;
                    _group.ForceLOD(0);
                }
                else
                {
                    Release();
                }

                _pinned = firstPerson;
            }

            private void Release()
            {
                _group.ForceLOD(-1);
                _group.fadeMode = _fadeMode;
                _group.animateCrossFading = _animateFade;
            }
        }

        // ------------------------------------------------------------------ WP-042 · mechanism slots

        /// <summary>Timer fallback for the slots (casing after the pump, reload marks by progress, pickup hand-off timeout).</summary>
        private void TickMechanismFallback()
        {
            if (_casingAt >= 0f && Time.time >= _casingAt)
            {
                _casingAt = -1f;
                if (_casingOwed)
                {
                    _casingOwed = false;
                    PublishMechanism(WeaponMechanismKind.CasingEjected, false, _casingSerial);
                }
            }

            if (_pickupDonePending && Time.time >= _pickupDoneAt)
            {
                _pickupDonePending = false;
                SetRigVisibility();
            }

            // C-00 30.09 M-21 interim: a break-open's spent shells follow its timeline even under a clip — sg_2 borrows the
            // shell-by-shell reload clip (no mag_out event) until its own set lands; the shared flag keeps a later clip's
            // mag_out from doubling them.
            if (_classic.IsReloading && _timeline.ReloadCasings > 0 && ClipDrivesMechanism && !_magOutFired && _classic.ReloadProgress01 >= _timeline.MagOutProgress)
            {
                _magOutFired = true;
                PublishMagOut(false);
            }

            if (_classic.IsReloading && !ClipDrivesMechanism)
            {
                float p = _classic.ReloadProgress01;
                if (!_timeline.Shells && !_magOutFired && p >= _timeline.MagOutProgress)
                {
                    _magOutFired = true;
                    PublishMagOut(false);
                }

                if (_timeline.Shells && _classic.ShellByShellReload)
                {
                    // R1-B W7 (C-00 30.09): one seat per shell the runtime actually loads — not the timeline's fixed three
                    while (_seatsFired < _classic.ShellsLoadedThisReload)
                    {
                        _seatsFired++;
                        PublishMechanism(WeaponMechanismKind.ShellSeated, false);
                    }
                }
                else
                {
                    while (_seatsFired < _timeline.SeatCount && p >= _timeline.SeatProgress(_seatsFired))
                    {
                        _seatsFired++;
                        PublishMechanism(WeaponMechanismKind.ShellSeated, false);
                    }
                }

                if (!_chamberFired && p >= _timeline.ChamberProgress)
                {
                    _chamberFired = true;
                    // §12 (C-00 05.10): a tactical magazine reload keeps its chambered round — the timer never invents a bolt release
                    if (!(MagazineReload && !_classic.ReloadIsEmpty)) PublishMechanism(WeaponMechanismKind.Chambered, false);
                }
            }
        }

        /// <summary>AP-009C clip events of THIS character (WP-032C enum names resolved at load; inert until then).</summary>
        private void OnClipEvent(CharacterAnimationEvents sender, CharacterAnimationEvent e)
        {
            if (sender == null || e == CharacterAnimationEvent.None || sender.transform.root != transform.root)
            {
                return;
            }

            if (e == s_pickupDone)
            {
                if (_pickupDonePending)
                {
                    _pickupDonePending = false;
                    SetRigVisibility();
                }

                return;
            }

            if (_special.IsActive || !ClipDrivesMechanism)
            {
                return;
            }

            if (e == s_eject)
            {
                // pump weapons only (A/B eject at the shot); WP-087: the owed shell, once — the deadline has not paid it yet
                if (_timeline.CasingDelaySeconds > 0f && _timeline.ShotCasings > 0 && _casingOwed)
                {
                    _casingOwed = false;
                    _casingAt = -1f;
                    PublishMechanism(WeaponMechanismKind.CasingEjected, true, _casingSerial);
                }
            }
            else if (e == s_magOut) { if (_timeline.ReloadCasings <= 0 || !_magOutFired) { _magOutFired = true; PublishMagOut(true); } }
            // §12 (C-00 05.10, AP-026-B): rl_1's round goes in (rocket_in) / seats (rocket_seat); a shotgun's shell_in is one seat per
            // cartridge. A synonym on the same frame (sg_1's mag_insert + shell_in) reaches here once — CharacterAnimationEvents collapses it.
            else if (e == s_magIn || e == s_magIn2 || e == s_magIn3 || e == CharacterAnimationEvent.ShellIn || e == CharacterAnimationEvent.RocketIn)
            {
                if (_classic.IsReloading) _seatsFromClip++;
                PublishMechanism(WeaponMechanismKind.ShellSeated, true);
            }
            else if (e == s_chamber || e == CharacterAnimationEvent.RocketSeat) PublishMechanism(WeaponMechanismKind.Chambered, true);

            // WP-082: the reload's magazine beats (mag_out / mag_hand_attach / mag_in / chamber); forward only, so the TP body's
            // event and a repeat cannot double a beat. Presentation: the ammo stays on the runtime's timer.
            if (_magazine.Active && WeaponMagazineOwnership.TryStepFor(e, out MagazineStep step)) _magazine.OnEvent(step);
        }

        /// <summary>The mag_out mark: a magazine drops — or, on a break-open (C-00 30.09 M-21), the spent shells fly.</summary>
        private void PublishMagOut(bool fromClip)
        {
            if (_timeline.ReloadCasings <= 0)
            {
                // WP-082: a magazine weapon's drop is the hand_attach beat now (OnMagazineStep) — at mag_out the magazine goes to
                // the hand. Only a reload the ownership does not track (none today) keeps the old drop here.
                if (!_magazine.Active) PublishMechanism(WeaponMechanismKind.MagazineDropped, fromClip);
                return;
            }

            for (int i = 0; i < _timeline.ReloadCasings; i++) PublishMechanism(WeaponMechanismKind.CasingEjected, fromClip);
        }

        private void PublishMechanism(WeaponMechanismKind kind, bool fromClip, int shotSerial = -1)
        {
            Transform socket = kind == WeaponMechanismKind.CasingEjected
                ? (_classicRig != null ? _classicRig.Ejection : Muzzle)
                : (_classicRig != null ? _classicRig.MagMarker : Muzzle);
            Vector3 direction = kind == WeaponMechanismKind.CasingEjected ? socket.right : Vector3.down;
            WeaponEvents.PublishMechanism(new WeaponMechanismEvent(kind, _equippedId, socket.position, direction, fromClip, shotSerial >= 0 ? shotSerial : ShotSerial));
        }
    }
}
