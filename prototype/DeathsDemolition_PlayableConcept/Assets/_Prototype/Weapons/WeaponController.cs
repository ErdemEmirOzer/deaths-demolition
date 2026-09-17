using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
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
        private bool _magOutFired;
        private int _seatsFired;
        private bool _chamberFired;
        private bool _pickupDonePending;
        private float _pickupDoneAt = -1f;

        // AP-009C clip events resolved by NAME against the S-06 enum (None until WP-032C adds them).
        private static readonly CharacterAnimationEvent s_eject = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.Eject]);
        private static readonly CharacterAnimationEvent s_magOut = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.MagOut]);
        private static readonly CharacterAnimationEvent s_magIn = CharacterAnimationEvents.Parse(SpecialWeaponUsage.ClipEventNames[(int)WeaponClipEvent.MagIn]);
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

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
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
        }

        protected override void OnDisable()
        {
            WeaponRegistry.Unregister(this);
            CharacterAnimationEvents.Any -= OnClipEvent;

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
            PrototypeConfig c = config != null ? config : Config;
            if (c == null)
            {
                return;
            }

            ClassicWeaponSettings settings = c.Weapons.Find(id);
            if (settings == null)
            {
                return;
            }

            _equippedId = settings.Id;
            _equipped = true;
            _classic.Equip(settings, _modifiers);
            _timeline = WeaponMechanismTimeline.For(settings);
            _casingAt = -1f;
            EnsureClassicModel();

            WeaponRegistry.SelectedWeapon = _equippedId;
            WeaponEvents.PublishWeaponEquipped(_equippedId);
            PublishClassicState(true);
        }

        /// <summary>
        /// REQ-055 / REQ-057. The Special Weapon was collected: its clock starts now and
        /// the classic weapon is parked, not discarded.
        /// </summary>
        public void ActivateSpecialWeapon(SpecialWeaponId id = SpecialWeaponId.KaguraBell)
        {
            if (Config == null || _special.IsActive)
            {
                return;
            }

            _classic.CancelReload();
            _special.Begin(Config.Weapons.Special, id);

            // WP-042 (DEC-082): the grab is an upper-body clip; the hand model shows at its
            // pickup_done event — or at the fallback timeout when the clip cannot carry it
            // (no authored character, interrupted clip). Firing is not gated.
            _pickupDonePending = s_pickupDone != CharacterAnimationEvent.None && _skinned != null && _skinned.Authored;
            _pickupDoneAt = Time.time + SpecialWeaponUsage.PickupDoneFallbackSeconds;

            SetRigVisibility();
            PublishClassicState(true);
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

            float dt = Time.deltaTime;
            IInputSource input = InputSourceRegistry.Current;

            bool fireHeld = input.IsEnabled && input.FireHeld;
            bool firePressed = input.IsEnabled && input.FirePressed;
            bool reloadPressed = input.IsEnabled && input.ReloadPressed;

            if (_special.IsActive)
            {
                if (_special.Tick(dt))
                {
                    HandleSpecialEnded();
                }
                else
                {
                    TickSpecialFiring(fireHeld);
                }
            }
            else
            {
                _classic.Tick(dt);
                TickClassicFiring(fireHeld, firePressed, reloadPressed);
            }

            TickReloadPresentation();
            TickMechanismFallback();

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
                _classic.ClampCooldown();
                return;
            }

            if (reloadPressed)
            {
                _classic.TryBeginReload();
            }

            if (!_classic.HasAmmo && !_classic.IsReloading)
            {
                if (firePressed)
                {
                    WeaponEvents.PublishDryFire();
                    AudioEvents.Raise(CombatAudioEvent.ClassicWeaponDryFire, Muzzle.position, 1f, 30);
                }

                // Holding the trigger on an empty magazine reloads: with hold-to-fire as
                // the only firing mode, forcing a separate keypress here is friction, not
                // depth.
                if (fireHeld || firePressed)
                {
                    _classic.TryBeginReload();
                }
            }

            if (!fireHeld)
            {
                _classic.ClampCooldown();
                return;
            }

            int shots = 0;
            while (_classic.CanFire && shots < ClassicWeaponRuntime.MaxShotsPerFrame)
            {
                FireClassicOnce();
                shots++;
            }

            _classic.ClampCooldown();
        }

        private void FireClassicOnce()
        {
            // WP-042: the damage ray leaves at a fixed height above the root (hit-region parity across
            // weapon models / holds); the visual muzzle only feeds the tracer and the flash.
            Vector3 muzzle = Muzzle.position;
            Vector3 origin = new Vector3(muzzle.x, transform.root.position.y + _shotRayHeight, muzzle.z);
            Vector3 aim = ResolveAimDirection();

            // Steps 1 and 3: procedural recoil and mechanical movement. Two layers, on
            // purpose - the gun kicks (rig) and the character absorbs it (host). Either
            // one alone reads as half a shot.
            if (_classicRig != null)
            {
                _classicRig.PlayFireMotion(_classic.RecoilKick, _classic.RecoilRecoverySeconds, _classic.RecoilUpShare);
            }

            _host?.OnWeaponFired(RecoilStrength01(_classic.RecoilKick, _classic.Power), aim);

            // Step 2: muzzle flash.
            float powerScale = PowerScale(_classic.Power);
            HitFeedbackDirector.Instance.ReportMuzzleFlash(
                Muzzle, _muzzleFlashSize * powerScale, _muzzleFlashLight * powerScale);

            // Step 4 and everything downstream, once per pellet.
            int pellets = _classic.PelletCount;
            for (int i = 0; i < pellets; i++)
            {
                ShotRequest request = _classic.BuildShot(origin, aim, i);
                request.HasTracerOrigin = true;
                request.TracerOrigin = muzzle;
                HitscanEngine.Fire(request);
            }

            // Step 11: controlled camera response, scaled by weapon weight and clamped
            // again inside the bridge so aim survives.
            ApplyFireShake(_classic.CameraShakeScale, _classic.Power, aim);

            // Step 12: the fire layer of the layered sound.
            AudioEvents.Raise(CombatAudioEvent.ClassicWeaponFire, origin, powerScale, 50);

            _classic.ConsumeShot();

            // WP-042: the casing slot — from the clip's eject event when the character carries it,
            // else now (A/B, frame 1) or at the pump-back (C, 0.40 s).
            if (!ClipDrivesMechanism)
            {
                if (_timeline.CasingDelaySeconds <= 0f) PublishMechanism(WeaponMechanismKind.CasingEjected, false);
                else _casingAt = Time.time + _timeline.CasingDelaySeconds;
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
                _chamberFired = false;

                if (_classicRig != null)
                {
                    _classicRig.PlayReloadMotion(_classic.ReloadDuration);
                }

                _host?.OnReloadStarted(_classic.ReloadDuration);
            }
            else if (!reloading && _wasReloading)
            {
                // Ammo is only refilled by a reload that ran to term, so a full magazine
                // is exactly the "completed" signal - no extra flag can drift out of sync
                // with it.
                bool completed = _classic.Ammo >= _classic.MagazineSize;

                if (_classicRig != null)
                {
                    _classicRig.CancelReloadMotion();
                }

                _host?.OnReloadFinished(completed);
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

            _wasReloading = reloading;
        }

        /// <summary>
        /// PROTOTYPE-ONLY: point in the reload at which the magazine-insert layer plays.
        /// Past halfway so the three layers are audibly spaced even on the shotgun's
        /// short reload.
        /// </summary>
        private const float ReloadInsertProgress = 0.62f;

        // ------------------------------------------------------------------ special firing

        private void TickSpecialFiring(bool fireHeld)
        {
            if (_specialRig != null)
            {
                _specialRig.SetSpin(fireHeld ? 1f : 0.15f);
            }

            if (!fireHeld || !CanFireNow())
            {
                _special.ClampCooldown();
                return;
            }

            int shots = 0;
            while (_special.CanFire && shots < ClassicWeaponRuntime.MaxShotsPerFrame)
            {
                FireSpecialOnce();
                shots++;
            }

            _special.ClampCooldown();
        }

        private void FireSpecialOnce()
        {
            Vector3 origin = Muzzle.position;
            Vector3 aim = ResolveAimDirection();

            if (_specialRig != null)
            {
                _specialRig.PlayFireMotion(_special.RecoilKick);
            }

            _host?.OnWeaponFired(SpecialWeaponRuntime.RecoilStrength01, aim);

            // The Kagura Bell has no muzzle: a flash at the barrel would contradict the one
            // thing that weapon says about itself, which is that the event leaves the whole
            // character rather than a point on it. The other two are thrown or fired, so
            // they keep it - and keep it at the Special Weapon's larger scale.
            if (_special.Id != SpecialWeaponId.KaguraBell)
            {
                HitFeedbackDirector.Instance.ReportMuzzleFlash(
                    Muzzle,
                    _muzzleFlashSize * _specialFlashMultiplier,
                    _muzzleFlashLight * _specialFlashMultiplier);
            }

            // The bell's pressure front leaves the CHARACTER; the other two leave the
            // muzzle. This controller lives on the player's aim pivot, so its own
            // transform IS the character - and passing that rather than the muzzle is
            // what keeps an aim-free weapon aim-free.
            Vector3 eventOrigin = _special.Id == SpecialWeaponId.KaguraBell
                ? transform.position
                : origin;

            _special.Fire(eventOrigin, aim);

            ApplyFireShake(_special.CameraShakeScale, WeaponPower.Heavy, aim);

            AudioEvents.Raise(CombatAudioEvent.HeavyWeaponFire, origin, 1f, 60);

            _special.ConsumeShot();
        }

        private void HandleSpecialEnded()
        {
            // REQ-057: the classic weapon comes back automatically, with the magazine it
            // had when it was parked.
            _pickupDonePending = false;
            SetRigVisibility();
            PublishClassicState(true);
            PublishSpecialState(true);
        }

        // ------------------------------------------------------------------ shared

        private bool CanFireNow()
        {
            return _aimSource == null || _aimSource.CanFire;
        }

        private Vector3 ResolveAimDirection()
        {
            Vector3 direction = _aimSource != null ? _aimSource.AimDirection : transform.forward;

            if (_flattenAim)
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

            float amplitude = scale * _shakeMetresAtScaleOne;
            float duration = _shakeBaseDuration + _shakeDurationPerScale * scale;

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
        private const float RecoilKickAtFullStrength = 0.2f;

        private static float RecoilStrength01(float recoilKick, WeaponPower power)
        {
            float normalised = recoilKick / RecoilKickAtFullStrength;

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
                BuildDescription = _modifiers.DescribeBuild()
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

                // The ground phase is over; clearing it stops the HUD from drawing a
                // stale world marker under the player's feet.
                state.GroundRemaining = 0f;
                state.GroundDuration = 0f;

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
            if (entry == null || !entry.HasHand || library.material == null)
            {
                return;
            }

            GameObject model = SpecialWeaponVisualLibrary.Instantiate("Model", entry.handLods, library.material, null, s_handLodHeights);
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

            _classicRig.SetModel(model);
            _classicModelId = _equippedId;
        }

        /// <summary>WP-042B: LOD transition heights for the weapon in hand (LOD0 ≥ 12 % of the screen, LOD1 ≥ 5 %, LOD2 ≥ 1 %). Screen-height fractions, not tunables.</summary>
        private static readonly float[] s_classicLodHeights = { 0.12f, 0.05f, 0.01f };

        public enum ClassicModelKind { None = 0, Blockout = 1, Authored = 2 }

        /// <summary>WP-042B readout: which model the classic rig carries (tests / probe).</summary>
        public ClassicModelKind ClassicModelSource { get; private set; }

        // ------------------------------------------------------------------ WP-042 · mechanism slots

        /// <summary>Timer fallback for the slots (casing after the pump, reload marks by progress, pickup hand-off timeout).</summary>
        private void TickMechanismFallback()
        {
            if (_casingAt >= 0f && Time.time >= _casingAt)
            {
                _casingAt = -1f;
                PublishMechanism(WeaponMechanismKind.CasingEjected, false);
            }

            if (_pickupDonePending && Time.time >= _pickupDoneAt)
            {
                _pickupDonePending = false;
                SetRigVisibility();
            }

            if (_classic.IsReloading && !ClipDrivesMechanism)
            {
                float p = _classic.ReloadProgress01;
                if (!_timeline.Shells && !_magOutFired && p >= _timeline.MagOutProgress)
                {
                    _magOutFired = true;
                    PublishMechanism(WeaponMechanismKind.MagazineDropped, false);
                }

                while (_seatsFired < _timeline.SeatCount && p >= _timeline.SeatProgress(_seatsFired))
                {
                    _seatsFired++;
                    PublishMechanism(WeaponMechanismKind.ShellSeated, false);
                }

                if (!_chamberFired && p >= _timeline.ChamberProgress)
                {
                    _chamberFired = true;
                    PublishMechanism(WeaponMechanismKind.Chambered, false);
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

            if (e == s_eject) PublishMechanism(WeaponMechanismKind.CasingEjected, true);
            else if (e == s_magOut) PublishMechanism(WeaponMechanismKind.MagazineDropped, true);
            else if (e == s_magIn) PublishMechanism(WeaponMechanismKind.ShellSeated, true);
            else if (e == s_chamber) PublishMechanism(WeaponMechanismKind.Chambered, true);
        }

        private void PublishMechanism(WeaponMechanismKind kind, bool fromClip)
        {
            Transform socket = kind == WeaponMechanismKind.CasingEjected
                ? (_classicRig != null ? _classicRig.Ejection : Muzzle)
                : (_classicRig != null ? _classicRig.MagMarker : Muzzle);
            Vector3 direction = kind == WeaponMechanismKind.CasingEjected ? socket.right : Vector3.down;
            WeaponEvents.PublishMechanism(new WeaponMechanismEvent(kind, _equippedId, socket.position, direction, fromClip));
        }
    }
}
