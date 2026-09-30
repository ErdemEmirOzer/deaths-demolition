using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The classic weapon in hand, live.
    ///
    /// Holds ammo (magazine + WP-069 reserve), the fire clock, the reload clock, accumulated
    /// spread bloom, the focus / ADS weight and the game-effect recoil, and turns all of that
    /// plus the run's upgrade tiers into a <see cref="ShotRequest"/>. Deliberately not a
    /// MonoBehaviour: no transform, no Update of its own — trivially testable and impossible
    /// to leave running after a run ends.
    ///
    /// REQ-051 is structural: the weapon is chosen once at <see cref="Equip"/>; only the
    /// debug panel swaps it outside normal play.
    ///
    /// WP-069 B2: a weapon equipped with a v3 <see cref="WeaponDefinition"/> (through the config
    /// copy's <see cref="ClassicWeaponSettings.Definition"/>) gets its fire mode (single / burst /
    /// auto), finite reserve with the round refill and the empty-reserve floor, tactical / empty /
    /// per-shell reloads, its pellet pattern (centre + rings, Gauss), the settle curve (negative
    /// bloom), per-mode hip / ADS cones, movement spread and the game recoil. A hand-built
    /// settings object without a definition keeps the exact M3 behaviour (auto, infinite reserve,
    /// one reload time, single ring, immediate bloom decay) — the M3 tests rely on it.
    ///
    /// Everything numeric is re-read on <see cref="ApplySettings"/>, so a live F1 edit lands
    /// mid-magazine.
    /// </summary>
    public sealed class ClassicWeaponRuntime
    {
        [PrototypeOnly(PendingDecisions.None, "WP-069 B2: share of a per-shell reload's tactical overhead spent before the first shell (the rest is the close-up after the last). From the council's 0.40 / 0.35 split.", true)]
        public const float PerShellStartShare = 0.53f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 B2: a single-fire trigger press made during the cycle is kept this long and fires when the weapon is ready (no dropped taps).", true)]
        public const float SinglePressBufferSeconds = 0.25f;

        [PrototypeOnly(PendingDecisions.None, "WP-069 B2: top-down recoil drift starts recovering this long after the last shot.", true)]
        public const float RecoilRecoveryDelaySeconds = 0.05f;

        private ClassicWeaponSettings _settings;
        private WeaponModifierState _modifiers;

        private float _cooldown;
        private float _bloom;
        private float _sinceShot = 999f;

        // WP-069 B2 state
        private int _reserve;
        private float _floorTimer;
        private int _burstLeft;
        private float _pressBuffer;
        private bool _focusHeld;
        private float _ads01;
        private float _move01;
        private int _shotInString;
        private float _stringSeconds;   // WP-077 ks.firerate.warmup: seconds of the current shot string (held fire)
        private float _recoilYaw;
        private float _recoilRecoverRate;
        private bool _emptyReload;
        private int _shellsQueued;
        private int _shellsLoaded;
        private float _reloadElapsed;
        private float _shellStartSeconds;
        private float _shellSeconds;
        private int _roundsSinceReload = NoReloadYet;   // WP-077 fork.lmg_2.y: rounds fired since the last completed reload

        private const int NoReloadYet = 1 << 20;

        // ------------------------------------------------------------------ identity

        public ClassicWeaponId Id => _settings != null ? _settings.Id : ClassicWeaponId.AssaultRifle;

        public string DisplayName => _settings != null ? _settings.DisplayName : "-";

        public bool IsConfigured => _settings != null;

        /// <summary>WP-069: the v3 definition behind the equipped numbers (null for a hand-built settings object).</summary>
        public WeaponDefinition Definition => _settings != null ? _settings.Definition : null;

        /// <summary>WP-069: single / burst / auto (auto without a definition — the M3 hold-to-fire).</summary>
        public WeaponFireMode FireMode => Definition != null ? Definition.fire.fireMode : WeaponFireMode.Auto;

        // ------------------------------------------------------------------ ammo

        public int Ammo { get; private set; }

        public int MagazineSize => _settings != null ? Mathf.Max(1, DD.Prototype.Rounds.RunTreeHooks.MagazineSize(Id, _settings.MagazineSize)) : 1;   // + WP-077 fork.smg_1.x / seal.lmg_1

        /// <summary>
        /// WP-069 × WP-077 (link.arc.storm): put up to <paramref name="rounds"/> into the magazine — parked or in hand —,
        /// never above <see cref="MagazineSize"/>, the reserve untouched. Returns the rounds that went in.
        /// </summary>
        public int AddRounds(int rounds)
        {
            if (rounds <= 0 || _settings == null) return 0;
            int added = Mathf.Min(rounds, MagazineSize - Ammo);
            if (added <= 0) return 0;
            Ammo += added;
            return added;
        }

        /// <summary>Debug-panel infinite ammo. Never reloads, never runs dry.</summary>
        public bool InfiniteAmmo { get; set; }

        /// <summary>WP-069: no reserve limit (v3 ammo.reserve &lt; 0, the M3 behaviour, or no definition).</summary>
        public bool ReserveInfinite => Definition == null || Definition.ammo.reserve < 0;

        /// <summary>WP-069: rounds carried beyond the magazine (0 while <see cref="ReserveInfinite"/>).</summary>
        public int ReserveAmmo => ReserveInfinite ? 0 : _reserve;

        /// <summary>WP-069 "kese" floor: 0..1 towards the next magazine dripping in while magazine and reserve are both empty (0 otherwise).</summary>
        public float FloorRefill01
        {
            get
            {
                if (ReserveInfinite || Definition.ammo.floorRefillDelaySeconds <= 0f) return 0f;
                return Mathf.Clamp01(_floorTimer / Definition.ammo.floorRefillDelaySeconds);
            }
        }

        public bool IsReloading { get; private set; }

        public float ReloadRemaining { get; private set; }

        public float ReloadDuration { get; private set; }

        public float ReloadProgress01 =>
            ReloadDuration > 0f ? Mathf.Clamp01(1f - ReloadRemaining / ReloadDuration) : 0f;

        /// <summary>WP-069: the last reload ran to its end (false after an interrupt / cancel).</summary>
        public bool LastReloadCompleted { get; private set; }

        /// <summary>WP-069: the reload in flight started on an empty magazine (the chamber / pump / bolt step).</summary>
        public bool ReloadIsEmpty => IsReloading && _emptyReload;

        // ------------------------------------------------------------------ derived numbers

        /// <summary>Shots per second after upgrades (single / auto; a burst's pace is its own intervals).</summary>
        public float EffectiveFireRate
        {
            get
            {
                if (_settings == null)
                {
                    return 1f;
                }

                float multiplier = _modifiers != null ? _modifiers.FireRateMultiplier : 1f;
                return Mathf.Max(0.05f, _settings.FireRate * multiplier * DD.Prototype.Rounds.RunTreeHooks.WeaponFireRateScale(Id, _stringSeconds));
            }
        }

        public float ShotInterval => 1f / EffectiveFireRate;

        private float ReloadMultiplier => (_modifiers != null ? _modifiers.ReloadTimeMultiplier : 1f) * DD.Prototype.Rounds.RunTreeHooks.ReloadTimeScale(Id);   // + WP-077 fork.sg_2.y

        /// <summary>Reload duration after upgrades (the tactical one for a v3 magazine weapon).</summary>
        public float EffectiveReloadTime
        {
            get
            {
                if (_settings == null)
                {
                    return 1f;
                }

                return Mathf.Max(0.05f, _settings.ReloadTime * ReloadMultiplier);
            }
        }

        public int PelletCount => _settings != null ? Mathf.Max(1, DD.Prototype.Rounds.RunTreeHooks.PelletCount(Id, _settings.PelletCount, InfiniteAmmo ? int.MaxValue : Ammo)) : 1;   // + WP-077 seal.sg_1 / fork.sg_1.y / seal.sg_2

        /// <summary>
        /// Pellets in ONE shell (the tree's extra pellets included, seal.sg_2's second barrel not) — what the HUD's
        /// ammo ladder lays out. PelletCount is per trigger pull and doubles on sg_2 while two rounds are loaded.
        /// </summary>
        public int PelletsPerShell => _settings != null ? Mathf.Max(1, DD.Prototype.Rounds.RunTreeHooks.PelletCount(Id, _settings.PelletCount, 1)) : 1;

        /// <summary>Shotguns are Heavy, SMGs Light, everything else Medium. Gates hit-stop.</summary>
        public WeaponPower Power
        {
            get
            {
                if (_settings == null)
                {
                    return WeaponPower.Medium;
                }

                if (_settings.PelletCount > 1 || _settings.StaggerImpulse >= 0.5f)
                {
                    return WeaponPower.Heavy;
                }

                return _settings.StaggerImpulse <= 0.1f ? WeaponPower.Light : WeaponPower.Medium;
            }
        }

        /// <summary>Camera shake scale for one trigger pull, before the bridge clamps it.</summary>
        public float CameraShakeScale => _settings != null ? Mathf.Max(0f, _settings.CameraShake) : 0f;

        public float RecoilKick => _settings != null ? Mathf.Max(0f, _settings.RecoilKick) : 0f;

        public float Range => _settings != null ? Mathf.Max(1f, _settings.Range) : 30f;

        // ---- WP-042 feel knobs (ClassicWeaponSettings passthrough, clamped to the request's ceilings)

        /// <summary>Seconds the rig kick takes to settle (WeaponVisualRig spring); 0.10 when unset.</summary>
        public float RecoilRecoverySeconds => _settings != null && _settings.RecoilRecoverySeconds > 0.005f ? _settings.RecoilRecoverySeconds : 0.10f;

        /// <summary>Share of the kick that reads as muzzle rise, 0..1.</summary>
        public float RecoilUpShare => _settings != null ? Mathf.Clamp01(_settings.RecoilUpShare) : 0.15f;

        /// <summary>Gamepad pulse per shot as a fraction of the WP-024 rumble ceiling, 0..1.</summary>
        public float RumbleFraction => _settings != null ? Mathf.Clamp01(_settings.RumbleFraction) : 0.5f;

        /// <summary>Gamepad pulse length per shot, ms, ≤ 120 (the request's ceiling).</summary>
        public int RumbleMilliseconds => _settings != null ? Mathf.Clamp(_settings.RumbleMilliseconds, 0, 120) : 60;

        /// <summary>WP-042 / WP-069: a centre + ring pattern (the M3 single ring, or the v3 CenterRings).</summary>
        public bool UsesPelletRing
        {
            get
            {
                if (_settings == null || _settings.PelletCount <= 1) return false;
                WeaponDefinition d = Definition;
                if (d != null) return d.fire.pelletPattern == WeaponPelletPattern.CenterRings && d.fire.rings != null && d.fire.rings.Length > 0;
                return _settings.PelletRingRadiusShare > 0f;
            }
        }

        /// <summary>Ring phase of the current trigger pull (rolled on pellet 0; tests).</summary>
        public float PelletRingPhase => _ringPhase;

        private float _ringPhase;

        // ------------------------------------------------------------------ WP-069 · focus, movement, recoil readouts

        /// <summary>0 = hip, 1 = fully focused / aimed (eased over the weapon's ADS time).</summary>
        public float Ads01 => _ads01;

        /// <summary>Top-down game recoil: accumulated horizontal drift applied to the shot, degrees (right +).</summary>
        public float RecoilYawDegrees => _recoilYaw;

        /// <summary>The last shot's game recoil impulse (pitch up +, yaw right +) — the look modes feed it to the camera.</summary>
        public float LastRecoilPitch { get; private set; }

        public float LastRecoilYaw { get; private set; }

        /// <summary>WP-069: the focus / ADS input this frame (controller).</summary>
        public void SetFocus(bool held)
        {
            _focusHeld = held;
        }

        /// <summary>WP-069: the player's planar speed as a share of top speed, 0..1 (movement spread).</summary>
        public void SetMovement01(float speed01)
        {
            _move01 = Mathf.Clamp01(speed01);
        }

        /// <summary>
        /// WP-069: the player's move speed multiplier from the weapon — its own "while firing" factor and the
        /// focus / ADS slowdown (top-down focus or look-mode ADS). 1 without a definition.
        /// </summary>
        public float MoveSpeedMultiplier(bool firing)
        {
            WeaponDefinition d = Definition;
            if (d == null) return 1f;
            float m = firing ? DD.Prototype.Rounds.RunTreeHooks.FiringMoveSpeed(Id, Mathf.Max(0.1f, d.accuracy.moveSpeedMultiplier)) : 1f;   // + WP-077 seal.smg_1 / fork.lmg_1.y
            float focus = CameraModeService.Current == CameraMode.TopDown ? d.accuracy.focusMoveSpeedMultiplier : d.accuracy.adsMoveSpeedMultiplier;
            return m * Mathf.Lerp(1f, Mathf.Max(0.1f, focus), _ads01);
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Start a run with this weapon. Fills the magazine and the round-1 reserve, clears every timer.</summary>
        public void Equip(ClassicWeaponSettings settings, WeaponModifierState modifiers)
        {
            _settings = settings;
            _modifiers = modifiers;

            Ammo = MagazineSize;
            _cooldown = 0f;
            _bloom = 0f;
            _sinceShot = 999f;
            _burstLeft = 0;
            _pressBuffer = 0f;
            _ads01 = 0f;
            _shotInString = 0;
            _roundsSinceReload = NoReloadYet;
            _recoilYaw = 0f;
            _floorTimer = 0f;
            IsReloading = false;
            ReloadRemaining = 0f;
            ReloadDuration = 0f;
            LastReloadCompleted = false;
            _reserve = 0;   // a new run starts from the round-1 allowance, never a previous run's leftovers
            RefillReserveForRound(1);
        }

        /// <summary>
        /// Re-point at a (possibly edited) settings object without disturbing ammo or a
        /// reload in flight. This is what makes a live config edit land mid-magazine.
        /// </summary>
        public void ApplySettings(ClassicWeaponSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _settings = settings;
            Ammo = Mathf.Min(Ammo, MagazineSize);
        }

        /// <summary>
        /// WP-069 (council B2): each round start fills the reserve to reserve × (1 + coef × (round − 1)) —
        /// the "shots per kill" budget stays level as zombies scale. Never takes rounds away.
        /// </summary>
        public void RefillReserveForRound(int round)
        {
            WeaponDefinition d = Definition;
            if (d == null || d.ammo.reserve < 0)
            {
                _reserve = 0;
                return;
            }

            int target = Mathf.RoundToInt(d.ammo.reserve * (1f + Mathf.Max(0f, d.ammo.reserveRoundCoef) * Mathf.Max(0, round - 1)));
            _reserve = Mathf.Max(_reserve, target);
            _floorTimer = 0f;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>
        /// Advance the clocks. Scaled delta on purpose: pause and hit-stop both freeze
        /// the weapon exactly as they freeze everything else.
        /// </summary>
        public void Tick(float dt)
        {
            if (_cooldown > 0f)
            {
                _cooldown -= dt;
            }

            if (_pressBuffer > 0f)
            {
                _pressBuffer -= dt;
            }

            _sinceShot += dt;
            _stringSeconds = _shotInString > 0 ? _stringSeconds + dt : 0f;
            WeaponDefinition d = Definition;

            // Bloom returns to 0 (from above, or from the settle floor below) once the delay after the last shot passes.
            if (_settings != null && _bloom != 0f)
            {
                float delay = d != null ? d.accuracy.bloomDecayDelaySeconds : 0f;
                float rate = d != null ? d.accuracy.bloomDecayPerSecond : _settings.SpreadBloomDecay;
                if (_sinceShot >= delay)
                {
                    _bloom = Mathf.MoveTowards(_bloom, 0f, Mathf.Max(0f, rate) * dt);
                }
            }

            // Focus / ADS weight.
            if (d != null)
            {
                float adsRate = 1f / Mathf.Max(0.02f, d.accuracy.adsSeconds);
                _ads01 = Mathf.MoveTowards(_ads01, _focusHeld ? 1f : 0f, adsRate * dt);
            }
            else
            {
                _ads01 = 0f;
            }

            // Top-down recoil drift recovers after the last shot; a pause in fire restarts the pattern.
            if (_sinceShot >= RecoilRecoveryDelaySeconds && _recoilYaw != 0f)
            {
                _recoilYaw = Mathf.MoveTowards(_recoilYaw, 0f, _recoilRecoverRate * dt);
            }

            if (d != null && _sinceShot >= Mathf.Max(0.05f, d.accuracy.recoilRecoverySeconds))
            {
                _shotInString = 0;
            }

            TickFloor(dt);

            if (!IsReloading)
            {
                return;
            }

            if (_shellSeconds > 0f)
            {
                TickShellReload(dt);
                return;
            }

            ReloadRemaining -= dt;
            if (ReloadRemaining > 0f)
            {
                return;
            }

            IsReloading = false;
            ReloadRemaining = 0f;
            int needed = MagazineSize - Ammo;
            int take = ReserveInfinite ? needed : Mathf.Min(needed, _reserve);
            Ammo += take;
            if (!ReserveInfinite) _reserve -= take;

            // WP-077 ks.reload.fullmag: a reload from empty loads past full (from the reserve like any round).
            int extra = DD.Prototype.Rounds.RunTreeHooks.FullMagExtraRounds(_emptyReload, MagazineSize);
            if (!ReserveInfinite) extra = Mathf.Min(extra, _reserve);
            if (extra > 0)
            {
                Ammo += extra;
                if (!ReserveInfinite) _reserve -= extra;
            }

            LastReloadCompleted = true;
            _roundsSinceReload = 0;
            WeaponEvents.PublishReloadCompleted();
            AudioEvents.Raise(CombatAudioEvent.ReloadEnd, Vector3.zero, 1f, 40);
        }

        private void TickFloor(float dt)
        {
            if (ReserveInfinite || InfiniteAmmo || Ammo > 0 || _reserve > 0 || IsReloading)
            {
                _floorTimer = 0f;
                return;
            }

            WeaponDefinition d = Definition;
            _floorTimer += dt;
            if (_floorTimer >= Mathf.Max(0.1f, d.ammo.floorRefillDelaySeconds))
            {
                _floorTimer = 0f;
                _reserve += MagazineSize * Mathf.Max(1, d.ammo.floorRefillMagazines);
            }
        }

        /// <summary>Per-shell reload: start overhead, one shell per interval from the reserve, then the close-up (+ pump when empty).</summary>
        private void TickShellReload(float dt)
        {
            _reloadElapsed += dt;
            ReloadRemaining = Mathf.Max(0f, ReloadDuration - _reloadElapsed);

            while (_shellsLoaded < _shellsQueued && _reloadElapsed >= _shellStartSeconds + (_shellsLoaded + 1) * _shellSeconds)
            {
                _shellsLoaded++;
                Ammo = Mathf.Min(MagazineSize, Ammo + 1);
                if (!ReserveInfinite) _reserve = Mathf.Max(0, _reserve - 1);
            }

            if (_reloadElapsed < ReloadDuration)
            {
                return;
            }

            IsReloading = false;
            ReloadRemaining = 0f;
            _shellSeconds = 0f;
            LastReloadCompleted = true;
            _roundsSinceReload = 0;
            WeaponEvents.PublishReloadCompleted();
            AudioEvents.Raise(CombatAudioEvent.ReloadEnd, Vector3.zero, 1f, 40);
        }

        public bool HasAmmo => InfiniteAmmo || Ammo > 0;

        /// <summary>True when the trigger would produce a shot right now (the clock allows it and there is ammo).</summary>
        public bool CanFire => _settings != null && !IsReloading && _cooldown <= 0f && HasAmmo;

        /// <summary>True when the magazine is not full and a reload is worth starting.</summary>
        public bool NeedsReload => !InfiniteAmmo && Ammo < MagazineSize;

        /// <summary>WP-069: a burst is in progress (its remaining shots fire whatever the trigger does).</summary>
        public bool BurstActive => _burstLeft > 0;

        /// <summary>WP-069: start a burst of the definition's count (burst weapons only).</summary>
        public void BeginBurst()
        {
            WeaponDefinition d = Definition;
            if (d == null || d.fire.fireMode != WeaponFireMode.Burst || _burstLeft > 0)
            {
                return;
            }

            _burstLeft = Mathf.Max(1, DD.Prototype.Rounds.RunTreeHooks.BurstCount(Id, d.fire.burstCount));   // + WP-077 fork.ar_2.y
        }

        /// <summary>WP-069 single fire: remember a press made during the cycle (fires when ready).</summary>
        public void BufferPress()
        {
            _pressBuffer = SinglePressBufferSeconds;
        }

        /// <summary>WP-069 single fire: a buffered press is waiting; consumed by the shot.</summary>
        public bool PressBuffered => _pressBuffer > 0f;

        public bool TryBeginReload()
        {
            if (_settings == null || IsReloading || InfiniteAmmo || Ammo >= MagazineSize)
            {
                return false;
            }

            if (!ReserveInfinite && _reserve <= 0)
            {
                return false;   // nothing to load: the floor refill will drip a magazine in
            }

            _burstLeft = 0;
            _emptyReload = Ammo <= 0;
            LastReloadCompleted = false;
            WeaponDefinition d = Definition;
            _shellSeconds = 0f;
            _reloadElapsed = 0f;

            if (d != null && d.ammo.reloadKind == WeaponReloadKind.PerShell)
            {
                int needed = MagazineSize - Ammo;
                _shellsQueued = ReserveInfinite ? needed : Mathf.Min(needed, _reserve);
                _shellsLoaded = 0;
                float mult = ReloadMultiplier;
                _shellStartSeconds = d.ammo.reloadTacSeconds * PerShellStartShare * mult;
                _shellSeconds = Mathf.Max(0.05f, d.ammo.perShellSeconds * mult);
                float close = d.ammo.reloadTacSeconds * (1f - PerShellStartShare) * mult;
                if (_emptyReload) close += Mathf.Max(0f, d.ammo.reloadEmptySeconds - d.ammo.reloadTacSeconds) * mult;
                ReloadDuration = _shellStartSeconds + _shellsQueued * _shellSeconds + close;
            }
            else if (d != null)
            {
                ReloadDuration = Mathf.Max(0.05f, (_emptyReload ? d.ammo.reloadEmptySeconds : d.ammo.reloadTacSeconds) * ReloadMultiplier);
            }
            else
            {
                ReloadDuration = EffectiveReloadTime;
            }

            ReloadRemaining = ReloadDuration;
            IsReloading = true;

            WeaponEvents.PublishReloadStarted(ReloadDuration, _emptyReload);
            AudioEvents.Raise(CombatAudioEvent.ReloadStart, Vector3.zero, 1f, 40);
            return true;
        }

        /// <summary>
        /// WP-069: the trigger interrupts a cancellable per-shell reload — the shells already in stay, the
        /// weapon can fire now. True when an interrupt happened.
        /// </summary>
        public bool TryInterruptShellReload()
        {
            WeaponDefinition d = Definition;
            if (!IsReloading || _shellSeconds <= 0f || d == null || !d.ammo.reloadCancellable || Ammo <= 0)
            {
                return false;
            }

            CancelReload();
            return true;
        }

        /// <summary>Stop a reload without finishing it. The magazine keeps whatever was in it.</summary>
        public void CancelReload()
        {
            if (!IsReloading)
            {
                return;
            }

            IsReloading = false;
            ReloadRemaining = 0f;
            _shellSeconds = 0f;
            LastReloadCompleted = false;
            WeaponEvents.PublishReloadInterrupted();
        }

        /// <summary>Book-keeping for one shot: ammo, fire clock (mode-aware), spread bloom, game recoil.</summary>
        public void ConsumeShot()
        {
            if (!InfiniteAmmo)
            {
                // WP-077: seal.sg_2 takes both barrels' rounds, cap.lmg_1's free window none.
                Ammo = Mathf.Max(0, Ammo - DD.Prototype.Rounds.RunTreeHooks.RoundsPerPull(Id, Ammo));
            }

            if (_roundsSinceReload < NoReloadYet) _roundsSinceReload++;
            WeaponDefinition d = Definition;
            float fireRateMult = (_modifiers != null ? Mathf.Max(0.05f, _modifiers.FireRateMultiplier) : 1f) * DD.Prototype.Rounds.RunTreeHooks.WeaponFireRateScale(Id, _stringSeconds);

            // Add rather than assign, so a frame that fires twice does not lose the
            // second interval and the weapon cannot drift faster than its fire rate.
            if (d != null && d.fire.fireMode == WeaponFireMode.Burst && _burstLeft > 0)
            {
                _burstLeft--;
                if (Ammo <= 0 && !InfiniteAmmo) _burstLeft = 0;
                _cooldown += _burstLeft > 0 ? Mathf.Max(0.01f, d.fire.burstIntervalSeconds) : Mathf.Max(0.01f, d.fire.burstDelaySeconds * DD.Prototype.Rounds.RunTreeHooks.BurstDelayScale(Id) / fireRateMult);
            }
            else
            {
                _cooldown += ShotInterval;
            }

            _pressBuffer = 0f;
            _sinceShot = 0f;

            if (d != null)
            {
                float perShot = DD.Prototype.Rounds.RunTreeHooks.BloomPerShot(Id, d.accuracy.bloomPerShot);   // + WP-077 fork.ar_1.y / seal.lmg_2
                if (perShot < 0f && d.accuracy.settleFloorShare > 0f)
                {
                    float floor = -HipAdsBase() * (1f - Mathf.Clamp01(d.accuracy.settleFloorShare));
                    _bloom = Mathf.Max(floor, _bloom + perShot);
                }
                else if (d.accuracy.bloomMax > 0f)
                {
                    _bloom = Mathf.Min(d.accuracy.bloomMax, _bloom + perShot);
                }

                ApplyGameRecoil(d);
            }
            else if (_settings != null && _settings.SpreadBloomMax > 0f)
            {
                _bloom = Mathf.Min(_settings.SpreadBloomMax, _bloom + _settings.SpreadBloomPerShot);
            }
        }

        private void ApplyGameRecoil(WeaponDefinition d)
        {
            WeaponAccuracyBlock a = d.accuracy;
            float yaw = 0f;
            float[] values = a.recoilYawValues;
            float amplitude = 0f;
            if (values != null) for (int i = 0; i < values.Length; i++) amplitude = Mathf.Max(amplitude, Mathf.Abs(values[i]));

            switch (a.recoilYawPattern)
            {
                case RecoilYawPattern.Pattern:
                    yaw = values != null && values.Length > 0 && (_shotInString < a.recoilStableShots || a.recoilStableShots <= 0)
                        ? values[_shotInString % values.Length]
                        : Random.Range(-amplitude, amplitude);
                    break;
                case RecoilYawPattern.Random:
                    yaw = Random.Range(-amplitude, amplitude);
                    break;
            }

            _shotInString++;
            LastRecoilPitch = a.recoilPitchPerShot;
            LastRecoilYaw = yaw;

            // Top-down: only the horizontal drift has a game effect (the aim is planar), scaled by its share.
            float cap = Mathf.Max(0.01f, a.recoilAccumulationCap) * Mathf.Max(amplitude, 0.01f) * a.topDownYawShare;
            _recoilYaw = Mathf.Clamp(_recoilYaw + yaw * a.topDownYawShare, -cap, cap);
            _recoilRecoverRate = Mathf.Abs(_recoilYaw) / Mathf.Max(0.05f, a.recoilRecoverySeconds);
        }

        // ------------------------------------------------------------------ spread

        /// <summary>Hip → ADS cone for the active camera mode, eased by <see cref="Ads01"/> (top-down hip = the F1-editable config copy).</summary>
        private float HipAdsBase()
        {
            if (_settings == null) return 0f;
            WeaponDefinition d = _settings.Definition;
            if (d == null) return Mathf.Max(0f, _settings.SpreadDegrees);
            int mode = (int)CameraModeService.Current;
            SpreadPair pair = d.SpreadFor(mode);
            float hip = DD.Prototype.Rounds.RunTreeHooks.HipSpread(Id, CameraModeService.Current == CameraMode.TopDown ? _settings.SpreadDegrees : pair.hip);   // + WP-077 seal.lmg_2
            return Mathf.Max(0f, Mathf.Lerp(hip, pair.ads, _ads01));
        }

        /// <summary>
        /// WP-069: the cone before bloom — hip / ADS for the active camera mode, widened by movement
        /// (moveSpreadMultiplier at full speed); the M3 SpreadDegrees without a definition.
        /// </summary>
        public float BaseSpreadDegrees
        {
            get
            {
                float b = HipAdsBase();
                WeaponDefinition d = Definition;
                if (d != null && d.accuracy.moveSpreadMultiplier > 0f)
                {
                    b *= Mathf.Lerp(1f, d.accuracy.moveSpreadMultiplier, _move01);
                }

                return b;
            }
        }

        /// <summary>Current cone half-angle including accumulated bloom (the settle curve may take it below the base).</summary>
        public float CurrentSpreadDegrees =>
            _settings != null ? Mathf.Max(0f, (BaseSpreadDegrees + DD.Prototype.Rounds.RunTreeHooks.Bloom(Id, _bloom, _shotInString)) * DD.Prototype.Rounds.RunTreeHooks.SpreadScale(Id)) : 0f;   // + WP-077 seal.ar_1 / fork.ar_1.x / fork.sg_1.x

        /// <summary>
        /// WP-069 ADR-069-1: how this weapon's projectiles fly (v3 projectile block; a fast straight
        /// bullet at the M3 range for a hand-built settings object). Pellet weapons fly Pellet kind.
        /// </summary>
        /// <summary>WP-069 §6: the Explosion branch reads "Blast" on this weapon (rl_1) — no chance roll, a bigger rocket blast.</summary>
        public bool UsesBlastSemantics => Definition != null && Definition.upgrade != null && Definition.upgrade.explosionSemantics == ExplosionSemantics.Blast;

        public ProjectileLaunch Launch(int pelletIndex)
        {
            ProjectileLaunch launch = ProjectileLaunch.From(Definition, Id, Range);
            launch.PelletIndex = pelletIndex;
            launch.HeadMultiplier = DD.Prototype.Rounds.RunTreeHooks.HeadMultiplier(Id, launch.HeadMultiplier);   // WP-077 seal.ar_2
            if (_modifiers != null)
            {
                launch.ArmorMultiplier *= _modifiers.ArmorBreakMultiplier;   // WP-069 §6 ArmorBreak
                if (launch.Kind == ProjectileKind.Rocket)
                {
                    // WP-069 B3 (§6): on a launcher the Explosion branch is the blast's size / damage, Cluster the bomblets (2 / 3 / 4).
                    if (UsesBlastSemantics) launch.BlastTier = _modifiers.GetTier(UpgradeBranch.Explosion);
                    int cluster = _modifiers.GetTier(UpgradeBranch.Cluster);
                    launch.ClusterBomblets = cluster > 0 ? cluster + 1 + DD.Prototype.Rounds.RunTreeHooks.ClusterExtraBomblets : 0;   // + WP-077 ks.cluster.seedling
                }
            }

            if (PelletCount > 1 && launch.Kind == ProjectileKind.Bullet)
            {
                launch.Kind = ProjectileKind.Pellet;
            }

            return launch;
        }

        /// <summary>WP-068 U12a: horizontal half-angle of the (outer) pellet ring; the Gauss σ cone for a Gauss pattern; 0 for a single bullet.</summary>
        public float PelletRingDegrees
        {
            get
            {
                if (_settings == null || PelletCount <= 1) return 0f;
                WeaponDefinition d = Definition;
                if (d == null) return UsesPelletRing ? Mathf.Max(0f, _settings.PelletRingRadiusShare) * CurrentSpreadDegrees : 0f;
                if (d.fire.pelletPattern == WeaponPelletPattern.Gauss) return Mathf.Clamp01(d.fire.gaussSigmaShare) * CurrentSpreadDegrees;
                if (!UsesPelletRing) return 0f;
                PelletRing outer = d.fire.rings[d.fire.rings.Length - 1];
                return outer != null ? Mathf.Max(0f, outer.radiusShare) * CurrentSpreadDegrees : 0f;
            }
        }

        /// <summary>Guard against a long frame turning into a burst. Never fire more than this per frame.</summary>
        public const int MaxShotsPerFrame = 3;

        public bool CooldownElapsed => _cooldown <= 0f;

        /// <summary>Clamp the fire clock so a stall cannot bank shots.</summary>
        public void ClampCooldown()
        {
            float floor = -ShotInterval;
            if (_cooldown < floor)
            {
                _cooldown = floor;
            }
        }

        // ------------------------------------------------------------------ shot construction

        /// <summary>
        /// Build one pellet's shot. Called <see cref="PelletCount"/> times per trigger
        /// pull; the spread roll differs each time, everything else is shared.
        /// Single-pellet callers (and the pre-WP-034 signature) build pellet 0.
        /// </summary>
        public ShotRequest BuildShot(Vector3 origin, Vector3 aimDirection)
        {
            return BuildShot(origin, aimDirection, 0);
        }

        /// <summary>
        /// WP-034 (ADR-034-1) pellet contract on a multi-pellet weapon: Piercing rides
        /// every pellet (the intended multiplier), Explosion is pellet-normalized (chance
        /// divided by the pellet count, since WP-004), Chain and Ricochet fire once per
        /// trigger pull — only pellet 0 carries them. A single-pellet weapon is unchanged.
        /// </summary>
        public ShotRequest BuildShot(Vector3 origin, Vector3 aimDirection, int pelletIndex)
        {
            WeaponModifierState m = _modifiers;
            bool multiPellet = PelletCount > 1;
            bool perShotCarrier = !multiPellet || pelletIndex == 0;
            WeaponDefinition def = Definition;

            // WP-069: the top-down game recoil drifts the shot sideways (the look modes move the camera instead).
            if (def != null && _recoilYaw != 0f && CameraModeService.Current == CameraMode.TopDown)
            {
                aimDirection = Quaternion.AngleAxis(_recoilYaw, Vector3.up) * aimDirection;
            }

            // WP-042: a pellet weapon fires one centre pellet and a ring (equal arc length on the
            // flattened cone, random phase per trigger pull, small per-pellet jitter) — the fan
            // reads the same every shot and the mean hit count is unchanged. WP-069: several rings
            // (sg_1: centre + 3 @ 40 % + 4 @ 100 %) or a Gauss cone (sg_2). Single-pellet weapons
            // keep the WP-002 independent scatter.
            Vector3 direction;
            bool lookFrame = CameraModeService.Current != CameraMode.TopDown;   // review R1-A #16
            if (multiPellet && def != null && def.fire.pelletPattern == WeaponPelletPattern.Gauss)
            {
                direction = SpreadUtility.ApplyGauss(aimDirection, CurrentSpreadDegrees, def.fire.gaussSigmaShare, lookFrame);
            }
            else if (UsesPelletRing)
            {
                if (pelletIndex == 0) _ringPhase = Random.value;
                if (def != null)
                {
                    direction = RingDirection(def, aimDirection, pelletIndex, lookFrame);
                }
                else
                {
                    direction = SpreadUtility.ApplyRing(aimDirection, CurrentSpreadDegrees, pelletIndex, PelletCount,
                        _settings.PelletRingRadiusShare, _settings.PelletRingJitterShare, _ringPhase, lookFrame);
                }
            }
            else
            {
                direction = SpreadUtility.Apply(aimDirection, CurrentSpreadDegrees, lookFrame);
            }

            var request = new ShotRequest
            {
                Origin = origin,
                Direction = direction,
                Damage = (_settings != null ? _settings.Damage : 1f) * (_modifiers != null ? _modifiers.DamageMultiplier : 1f)   // WP-077 Hearth
                    * DD.Prototype.Rounds.RunTreeHooks.ShotDamageScale(Id, _move01, _sinceShot, _roundsSinceReload),   // WP-077 fork.lmg_1.x / fork.lmg_2.y / fork.sr_1.x
                Range = Range,
                // On the shared DamageForce scale, NOT the raw config knob. See WeaponImpulse.
                ImpactForce = WeaponImpulse.ForClassic(_settings, _modifiers != null ? _modifiers.ImpactStaggerBonus : 0f),   // WP-069 §6 Impact
                Source = multiPellet ? DamageSourceKind.Pellet : DamageSourceKind.Bullet,
                Power = Power,

                DrawTracer = true,
                TracerWidth = multiPellet ? 0.022f : 0.03f,
                TracerLifetime = multiPellet ? 0.075f : 0.09f
            };

            if (m != null)
            {
                request.PierceTargets = m.PierceTargets;
                request.PierceDamageFalloff = m.PierceDamageFalloff;

                // Per-shot branches: the first pellet of a trigger pull carries them, the
                // rest of the spread does not (WeaponBranchContract.ResolutionOf == PerShot).
                request.RicochetChance = perShotCarrier ? m.RicochetChance : 0f;
                request.RicochetBounces = perShotCarrier ? DD.Prototype.Rounds.RunTreeHooks.RicochetBounces(Id, m.RicochetBounces) : 0;   // + WP-077 cap.smg_1
                request.RicochetRadius = m.RicochetRadius;
                request.RicochetDamageScale = m.RicochetDamageScale;

                // WP-077 link.arc.conductor: +1 jump while Raiju's link window is open (the Chain branch must be owned).
                request.ChainTargets = perShotCarrier ? m.ChainTargets + (m.HasChain ? SpecialLinks.ConductorBonusJumps() : 0) : 0;
                request.ChainRadius = m.ChainRadius;
                request.ChainDamageScale = m.ChainDamageScale;
                request.ChainDamageFalloff = m.ChainDamageFalloff;

                // A shotgun with the Explosion branch must not roll the blast once per
                // pellet at full odds - that would be eight rolls for one trigger pull.
                // WP-069 B3: a launcher never rolls it — its Explosion tiers grow the rocket's own blast (Launch).
                request.ExplosionChance = UsesBlastSemantics ? 0f : multiPellet ? m.ExplosionChance / PelletCount : m.ExplosionChance;
                request.ExplosionRadius = m.ExplosionRadius;
                request.ExplosionDamageScale = m.ExplosionDamageScale;
            }
            else
            {
                request.PierceDamageFalloff = 1f;
            }

            return request;
        }

        /// <summary>WP-069: pellet 0 is the centre; the rest fill the definition's rings in order, each ring at equal arc length.</summary>
        private Vector3 RingDirection(WeaponDefinition def, Vector3 aim, int pelletIndex, bool lookFrame)
        {
            float degrees = CurrentSpreadDegrees;
            float jitter = def.fire.ringJitterShare;
            if (pelletIndex <= 0)
            {
                return SpreadUtility.ApplyRing(aim, degrees, 0, 2, 0f, jitter, _ringPhase, lookFrame);
            }

            int k = pelletIndex - 1;
            PelletRing[] rings = def.fire.rings;
            for (int r = 0; r < rings.Length; r++)
            {
                PelletRing ring = rings[r];
                int count = ring != null ? Mathf.Max(1, ring.count) : 1;
                if (k < count)
                {
                    // A per-ring phase offset so stacked rings do not line their pellets up.
                    float phase = _ringPhase + r * 0.5f / count;
                    return SpreadUtility.ApplyRing(aim, degrees, k + 1, count + 1, ring != null ? ring.radiusShare : 1f, jitter, phase, lookFrame);
                }

                k -= count;
            }

            return SpreadUtility.Apply(aim, degrees, lookFrame);   // more pellets than the rings hold (a keystone): scatter
        }
    }

    /// <summary>Cone spread, applied around the aim direction.</summary>
    public static class SpreadUtility
    {
        /// <summary>
        /// PROTOTYPE-ONLY: the top-down pellet ring's vertical axis, a third of the horizontal one — it only shapes the ring's
        /// equal-arc-length spacing, so the fan seen from above keeps its WP-042 look. Top-down shots themselves carry NO
        /// vertical scatter (C-00 30.09, decision B): the aim is planar (WP-069 DIRECTION §4b rule 2, extended from the kick to
        /// the scatter), the ray sits at 1.39 m so the struck region does not flicker (WP-042 region parity, DEC-097), and a
        /// vertical scatter there never read as "loose" — it only cost hits, and once the ray met the head sphere it turned
        /// into random ×2 criticals. TP / FP keep their round cone (review R1-A #16).
        /// </summary>
        public const float VerticalSpreadFactor = 0.35f;

        public static Vector3 Apply(Vector3 direction, float degrees, bool lookFrame = false)
        {
            if (degrees <= 0.0001f)
            {
                return direction.normalized;
            }

            Vector3 forward = direction.normalized;
            Vector3 right = RightOf(forward);

            float yaw, pitch;
            if (lookFrame)
            {
                // Review R1-A #16: TP/FP — a round cone in the aim's own frame (uniform over the disc of the half-angle).
                float r = degrees * Mathf.Sqrt(Random.value);
                float a = Random.value * 2f * Mathf.PI;
                yaw = r * Mathf.Cos(a);
                pitch = r * Mathf.Sin(a);
            }
            else
            {
                yaw = Random.Range(-degrees, degrees);
                Random.Range(-degrees, degrees);   // C-00 30.09 B: top-down pitch 0; the draw stays so the yaw sequence is unchanged
                pitch = 0f;
            }

            return Rotate(forward, right, yaw, pitch, lookFrame);
        }

        private static Vector3 RightOf(Vector3 forward)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }

            return right.normalized;
        }

        /// <summary>
        /// Yaw / pitch offsets onto the aim. Top-down: yaw about world up (the planar aim's own up). Look modes
        /// (review R1-A #16): yaw about the aim's own up, so a pitched aim keeps its full horizontal scatter.
        /// </summary>
        private static Vector3 Rotate(Vector3 forward, Vector3 right, float yaw, float pitch, bool lookFrame)
        {
            Vector3 yawAxis = lookFrame ? Vector3.Cross(forward, right) : Vector3.up;
            Quaternion rotation = Quaternion.AngleAxis(yaw, yawAxis) * Quaternion.AngleAxis(pitch, right);
            return rotation * forward;
        }

        /// <summary>
        /// WP-042 pellet pattern. Pellet 0 is the centre (the aim reward); pellets 1..n−1 sit on
        /// an ellipse of horizontal radius <paramref name="ringShare"/> × <paramref name="degrees"/>
        /// and vertical radius × <see cref="VerticalSpreadFactor"/> (top-down: flattened onto its horizontal axis after the
        /// spacing, C-00 30.09 B — the fan from above is unchanged), spaced by EQUAL ARC LENGTH
        /// (equal parameter angle would bunch pellets at the flat ends), rotated by
        /// <paramref name="phase01"/> turns, each jittered by ±<paramref name="jitterShare"/> ×
        /// <paramref name="degrees"/>. No allocation.
        /// </summary>
        public static Vector3 ApplyRing(Vector3 direction, float degrees, int pelletIndex, int pelletCount,
            float ringShare, float jitterShare, float phase01, bool lookFrame = false)
        {
            float vFactor = lookFrame ? 1f : VerticalSpreadFactor;   // review R1-A #16: a round ring in TP/FP
            Vector3 forward = direction.normalized;
            if (degrees <= 0.0001f || pelletCount <= 1)
            {
                return forward;
            }

            Vector3 right = RightOf(forward);

            float jitter = Mathf.Max(0f, jitterShare) * degrees;
            float yaw, pitch;
            if (pelletIndex <= 0)
            {
                yaw = Random.Range(-jitter, jitter);
                pitch = Random.Range(-jitter, jitter) * vFactor;
                if (!lookFrame) pitch = 0f;   // C-00 30.09 B: planar top-down (the draw kept: same yaw sequence)
            }
            else
            {
                float a = Mathf.Max(0f, ringShare) * degrees;          // horizontal radius, degrees
                float b = a * vFactor;                                  // vertical radius
                int ringCount = pelletCount - 1;
                // Equal-arc-length parameter for this pellet: walk the ellipse in RingSegments steps.
                float target = ((pelletIndex - 1) + Mathf.Repeat(phase01, 1f)) / ringCount;
                float t = RingParameterAtArcFraction(a, b, target);
                yaw = a * Mathf.Cos(t) + Random.Range(-jitter, jitter);
                pitch = b * Mathf.Sin(t) + Random.Range(-jitter, jitter) * vFactor;
                if (!lookFrame) pitch = 0f;   // C-00 30.09 B: the ring flattens to its horizontal fan; yaw spacing as before
            }

            return Rotate(forward, right, yaw, pitch, lookFrame);
        }

        /// <summary>
        /// WP-069 (sg_2): Gauss cone — yaw ~ N(0, σ·degrees) and, in TP / FP, pitch ~ N(0, σ·degrees), both clamped to the
        /// cone; top-down pitch 0 (C-00 30.09 B). Box–Muller over Random.value; no allocation.
        /// </summary>
        public static Vector3 ApplyGauss(Vector3 direction, float degrees, float sigmaShare, bool lookFrame = false)
        {
            Vector3 forward = direction.normalized;
            if (degrees <= 0.0001f)
            {
                return forward;
            }

            Vector3 right = RightOf(forward);
            float sigma = Mathf.Max(0.01f, sigmaShare) * degrees;
            float u1 = Mathf.Max(1e-6f, Random.value);
            float u2 = Random.value;
            float mag = Mathf.Sqrt(-2f * Mathf.Log(u1));
            float yaw = Mathf.Clamp(mag * Mathf.Cos(2f * Mathf.PI * u2) * sigma, -degrees, degrees);
            float pitch = lookFrame ? Mathf.Clamp(mag * Mathf.Sin(2f * Mathf.PI * u2) * sigma, -degrees, degrees) : 0f;   // C-00 30.09 B: planar top-down
            return Rotate(forward, right, yaw, pitch, lookFrame);
        }

        private const int RingSegments = 64;

        /// <summary>The ellipse parameter t (radians) at which the cumulative arc length equals fraction × perimeter.</summary>
        public static float RingParameterAtArcFraction(float a, float b, float fraction)
        {
            fraction = Mathf.Repeat(fraction, 1f);
            float total = 0f;
            float px = a, py = 0f;
            for (int i = 1; i <= RingSegments; i++)
            {
                float t = i * (2f * Mathf.PI / RingSegments);
                float x = a * Mathf.Cos(t), y = b * Mathf.Sin(t);
                total += Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                px = x; py = y;
            }

            if (total <= 0f) return 0f;
            float goal = fraction * total, walked = 0f;
            px = a; py = 0f;
            for (int i = 1; i <= RingSegments; i++)
            {
                float t = i * (2f * Mathf.PI / RingSegments);
                float x = a * Mathf.Cos(t), y = b * Mathf.Sin(t);
                float seg = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                if (walked + seg >= goal)
                {
                    float f = seg > 0f ? (goal - walked) / seg : 0f;
                    return (i - 1 + f) * (2f * Mathf.PI / RingSegments);
                }

                walked += seg;
                px = x; py = y;
            }

            return 2f * Mathf.PI;
        }
    }
}
