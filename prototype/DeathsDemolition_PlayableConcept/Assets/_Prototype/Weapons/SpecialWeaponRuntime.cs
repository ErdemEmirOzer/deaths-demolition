using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The Special Weapon in the player's hands — WP-069 B4 (DEC-094: exactly two; DEC-097: ammo + time cap).
    ///
    ///   Raiju (sw_arc)    CHAIN    A coil gun. Hold the trigger 0.25 s to charge; the orb flies at 14 m/s and the
    ///                              body it strikes starts a chain of up to 8 (6 m hops, 0.08 s apart) that trembles
    ///                              and falls. Releasing during the charge cancels without spending the cell.
    ///                              Resolution: <see cref="SpecialImpacts"/> (orb contact) → S-03's ChainTargetResolver /
    ///                              ZombieEnemy.ApplyShock.
    ///   Namekuji (sw_gel) SLIDE    A gel canister. A magenta blob arcs (22 m/s, 12 m/s² — top-down aims the arc at the
    ///                              cursor, ≤ 20 m); a direct hit liquefies (tier rules) and the ground takes a slippery
    ///                              puddle (B4b, S-03's GelPuddleRegistry).
    ///
    /// Both are MAGAZINE weapons now (Raiju 3 / 9 cells, Namekuji 8 / 16), reload by cell / canister and leave the hand
    /// when their ammo is gone OR the time cap runs out (40 s / 45 s), whichever comes first (<see cref="SpecialLimit"/>).
    /// REQ-055 still holds (the cap clock starts on pickup), REQ-058 the final-seconds warning (time cap), REQ-059 no
    /// upgrade reaches it (this class cannot see <see cref="WeaponModifierState"/>). Numbers: the WeaponDefinition v3
    /// assets (Resources/Arsenal sw_arc / sw_gel); the drop rules stay in <c>PrototypeConfig.Weapons.Special</c>.
    /// </summary>
    public sealed class SpecialWeaponRuntime
    {
        private SpecialWeaponConfigSection _settings;
        private WeaponDefinition _def;

        private float _cooldown;
        private bool _finalSecondsFired;
        private int _reserve;
        private float _reloadRemaining;
        private bool _charging;
        private float _charge;

        public bool IsActive { get; private set; }

        /// <summary>Which of the two is in hand. Only meaningful while active.</summary>
        public SpecialWeaponId Id { get; private set; } = SpecialWeaponId.Raiju;

        /// <summary>The weapon's v3 definition (null in a bare test without the Arsenal).</summary>
        public WeaponDefinition Definition => _def;

        /// <summary>Seconds of the time cap left. Only counts down while active.</summary>
        public float Remaining { get; private set; }

        /// <summary>The time cap this activation started with (DEC-097: 40 s / 45 s).</summary>
        public float Duration { get; private set; }

        // ------------------------------------------------------------------ ammo (DEC-097)

        public int Ammo { get; private set; }

        public int MagazineSize => _def != null ? Mathf.Max(1, _def.ammo.magazine) : 1;

        public int ReserveAmmo => _reserve;

        public bool IsReloading => _reloadRemaining > 0f;

        public float ReloadDuration { get; private set; }

        public float ReloadRemaining => Mathf.Max(0f, _reloadRemaining);

        public float ReloadProgress01 => ReloadDuration > 0f && IsReloading ? Mathf.Clamp01(1f - _reloadRemaining / ReloadDuration) : 0f;

        /// <summary>Magazine and reserve both empty (and no reload running): the weapon leaves the hand.</summary>
        public bool OutOfAmmo => !IsReloading && Ammo <= 0 && _reserve <= 0;

        // ------------------------------------------------------------------ charge (Raiju)

        public float ChargeSeconds => _def != null ? Mathf.Max(0f, _def.fire.chargeSeconds) : 0f;

        public bool IsCharging => _charging;

        public float Charge01 => _charging && ChargeSeconds > 0f ? Mathf.Clamp01(_charge / ChargeSeconds) : 0f;

        // ------------------------------------------------------------------ readouts

        private SpecialWeaponCommon Common => _settings != null ? _settings.Common(Id) : null;

        public string DisplayName
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null && !string.IsNullOrEmpty(common.DisplayName) ? common.DisplayName : Id.ToString();
            }
        }

        public float Damage => Common != null ? Common.Damage : 0f;

        /// <summary>Cycles per second (Raiju's charge comes on top).</summary>
        public float FireRate => Common != null ? Mathf.Max(0.05f, Common.FireRate) : 1f;

        public float ShotInterval => 1f / FireRate;

        public float RangeMetres => Common != null ? Mathf.Max(1f, Common.Range) : 30f;

        /// <summary>Reaction force per primary event, in <see cref="DamageForce"/> units.</summary>
        public float ImpactForce => Common != null ? Mathf.Max(0f, Common.ImpactForce) : 0f;

        public float CameraShakeScale => Common != null ? Mathf.Max(0f, Common.CameraShake) : 0.4f;

        public float RecoilKick => Common != null ? Mathf.Max(0f, Common.RecoilKick) : 0.08f;

        /// <summary>WP-056 usage payload: the reach of one primary event — the chain's hop radius (Raiju), the puddle radius (Namekuji).</summary>
        public float FireRadius
        {
            get
            {
                if (_def == null) return 0f;
                return Id == SpecialWeaponId.Namekuji ? _def.gelPuddle.puddleRadius : _def.arcChain.jumpRadius;
            }
        }

        /// <summary>WP-056 usage payload: how long one primary event lasts — the chain's run (Raiju), a puddle's life (Namekuji).</summary>
        public float FireSeconds
        {
            get
            {
                if (_def == null) return 0f;
                return Id == SpecialWeaponId.Namekuji
                    ? _def.gelPuddle.puddleLifetimeSeconds
                    : Mathf.Max(0, _def.arcChain.maxTargets - 1) * _def.arcChain.jumpDelaySeconds + _def.arcChain.shockSeconds;
            }
        }

        /// <summary>Always Heavy. That is what earns every Special Weapon hit-stop on solid hits.</summary>
        public WeaponPower Power => WeaponPower.Heavy;

        /// <summary>True once the time cap is inside the warning window (REQ-058).</summary>
        public bool InFinalSeconds =>
            IsActive && _settings != null && Remaining <= Mathf.Max(0f, _settings.FinalSecondsWarning);

        /// <summary>PROTOTYPE-ONLY per-event character kick (0-1 weight for the character rig).</summary>
        public const float RecoilStrength01 = 0.3f;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Re-read the config. Safe to call every frame.</summary>
        public void ApplySettings(SpecialWeaponConfigSection settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// REQ-055. The clock starts here and nowhere else - specifically NOT when the weapon spawned on the map.
        /// Full magazine and reserve, the definition's time cap.
        /// </summary>
        /// <param name="raisePickedUp">
        /// WP-059C: false lets the owner raise <c>SpecialWeaponPickedUp</c> itself AFTER it has published the Active
        /// snapshot (S-09 reads the new weapon's name from the snapshot in the PickedUp handler).
        /// </param>
        public void Begin(SpecialWeaponConfigSection settings, SpecialWeaponId id, bool raisePickedUp = true)
        {
            _settings = settings;
            Id = id;
            _def = settings != null ? settings.Common(id).Definition : SpecialWeaponConfigSection.DefinitionOf(id);

            Duration = settings != null ? settings.CapSeconds(id) : (_def != null && _def.specialLifecycle.activeCapSeconds > 0f ? _def.specialLifecycle.activeCapSeconds : 11f);
            Remaining = Duration;
            IsActive = true;
            _cooldown = 0f;
            _finalSecondsFired = false;
            Ammo = MagazineSize;
            _reserve = _def != null ? Mathf.Max(0, _def.ammo.reserve) : 0;
            _reloadRemaining = 0f;
            ReloadDuration = 0f;
            _charging = false;
            _charge = 0f;

            AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponPickup, Vector3.zero, 1f, 90);
            AudioEvents.RaiseLoop(CombatAudioEvent.HeavyWeaponSpinUp, Vector3.zero, 1f, 85);
            if (raisePickedUp)
            {
                WeaponEvents.PublishSpecialPickedUp(Duration);
            }
        }

        /// <summary>The activation ran its course (ammo or cap): the player LOST the weapon. Idempotent.</summary>
        public void End()
        {
            Finish(false);
        }

        /// <summary>
        /// WP-007 (AP-10, OS-2): stopped by something other than its own limits - run end, death, new run, teardown.
        /// Same clean-up as <see cref="End"/> without the "you lost it" news cue. Idempotent. Orbs / chains already
        /// in flight finish on their own (they are projectiles now, not fields of this object).
        /// </summary>
        public void Cancel()
        {
            Finish(true);
        }

        private void Finish(bool cancelled)
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            Remaining = 0f;
            _reloadRemaining = 0f;
            _charging = false;
            _charge = 0f;

            AudioEvents.StopLoop(CombatAudioEvent.HeavyWeaponSpinUp);
            AudioEvents.RaiseCritical(CombatAudioEvent.HeavyWeaponSpinDown, Vector3.zero, 1f, 85);
            if (!cancelled)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponExpired, Vector3.zero, 1f, 90);
            }

            WeaponEvents.PublishSpecialEnded();
        }

        /// <summary>
        /// Advance the clocks: cooldown, reload, the time cap. Returns true on the frame the activation ends (cap
        /// reached, or magazine and reserve empty), the controller's cue to hand the classic weapon back (REQ-057).
        /// </summary>
        public bool Tick(float dt)
        {
            if (!IsActive)
            {
                return false;
            }

            if (_cooldown > 0f)
            {
                _cooldown -= dt;
            }

            if (_reloadRemaining > 0f)
            {
                _reloadRemaining -= dt;
                if (_reloadRemaining <= 0f)
                {
                    _reloadRemaining = 0f;
                    int take = Mathf.Min(MagazineSize - Ammo, _reserve);
                    Ammo += take;
                    _reserve -= take;
                    SpecialWeaponUsageEvents.Publish(Id, SpecialUsageKind.ReloadEnded, SpecialWeaponUsage.For(Id).FireVfxFamily,
                        Vector3.zero, Vector3.forward, 0f, ReloadDuration, Ammo);
                }
            }

            Remaining -= dt;

            if (!_finalSecondsFired && InFinalSeconds)
            {
                _finalSecondsFired = true;
                WeaponEvents.PublishSpecialFinalSeconds(Remaining);
                AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponFinalSeconds, Vector3.zero, 1f, 95);
            }

            SpecialLimit limit = _def != null ? _def.specialLifecycle.limit : SpecialLimit.Time;
            bool timeUp = Remaining <= 0f && limit != SpecialLimit.Ammo;
            bool ammoOut = OutOfAmmo && limit != SpecialLimit.Time;
            if (!timeUp && !ammoOut)
            {
                return false;
            }

            End();
            return true;
        }

        /// <summary>The weapon can release a shot right now (cycle over, a round in the magazine, not reloading).</summary>
        public bool CanFire => IsActive && _cooldown <= 0f && Ammo > 0 && !IsReloading;

        /// <summary>
        /// WP-069 B4 trigger rules, one call per frame after <see cref="Tick"/>. Returns true when a shot leaves now.
        /// Namekuji: single, a held trigger refires at the cycle ceiling. Raiju: the trigger starts a
        /// <see cref="ChargeSeconds"/> charge; holding through it fires, releasing cancels (no ammo spent).
        /// </summary>
        public bool UpdateTrigger(bool held, bool pressed, float dt)
        {
            if (!IsActive)
            {
                return false;
            }

            float charge = ChargeSeconds;
            if (charge <= 0f)
            {
                return (held || pressed) && CanFire;
            }

            if (_charging)
            {
                if (!held && !pressed)
                {
                    _charging = false;
                    _charge = 0f;
                    SpecialWeaponUsageEvents.Publish(Id, SpecialUsageKind.ChargeCancelled, SpecialWeaponUsage.For(Id).FireVfxFamily,
                        Vector3.zero, Vector3.forward, 0f, 0f, 0);
                    return false;
                }

                _charge += dt;
                if (_charge + 1e-5f < charge)
                {
                    return false;
                }

                _charging = false;
                _charge = 0f;
                return CanFire;
            }

            if ((held || pressed) && CanFire)
            {
                _charging = true;
                _charge = 0f;
                SpecialWeaponUsageEvents.Publish(Id, SpecialUsageKind.ChargeStarted, SpecialWeaponUsage.For(Id).FireVfxFamily,
                    Vector3.zero, Vector3.forward, 0f, charge, 0);
            }

            return false;
        }

        /// <summary>Book-keeping for one shot: the round, the cycle, and the automatic reload on the last round.</summary>
        public void ConsumeShot()
        {
            _cooldown += ShotInterval;
            Ammo = Mathf.Max(0, Ammo - 1);
            if (Ammo <= 0)
            {
                TryBeginReload();
            }
        }

        /// <summary>Start a cell / canister change (reload key, or automatically on the last round). False when full, empty-handed or already reloading.</summary>
        public bool TryBeginReload()
        {
            if (!IsActive || IsReloading || Ammo >= MagazineSize || _reserve <= 0 || _def == null)
            {
                return false;
            }

            _charging = false;
            _charge = 0f;
            ReloadDuration = Mathf.Max(0.05f, Ammo <= 0 ? _def.ammo.reloadEmptySeconds : _def.ammo.reloadTacSeconds);
            _reloadRemaining = ReloadDuration;
            SpecialWeaponUsageEvents.Publish(Id, SpecialUsageKind.ReloadStarted, SpecialWeaponUsage.For(Id).FireVfxFamily,
                Vector3.zero, Vector3.forward, 0f, ReloadDuration, Ammo);
            return true;
        }

        public void ClampCooldown()
        {
            float floor = -ShotInterval;
            if (_cooldown < floor)
            {
                _cooldown = floor;
            }
        }

        // ------------------------------------------------------------------ firing

        /// <summary>The Arsenal runtime index a Special's projectiles carry (100 Raiju, 101 Namekuji).</summary>
        public static ClassicWeaponId ProjectileWeaponId(SpecialWeaponId id) => (ClassicWeaponId)(100 + (int)id);

        /// <summary>The Special behind a projectile's weapon index, false for classic projectiles.</summary>
        public static bool TryGetSpecial(ClassicWeaponId projectileWeapon, out SpecialWeaponId id)
        {
            int v = (int)projectileWeapon - 100;
            id = v == 1 ? SpecialWeaponId.Namekuji : SpecialWeaponId.Raiju;
            return v == 0 || v == 1;
        }

        /// <summary>
        /// One primary event: the orb / the blob leaves <paramref name="origin"/> as a projectile. Raiju flies along the aim
        /// (top-down planar, look modes 3D — an orb into the floor fizzles; review R1-A #4). Namekuji arcs: top-down pitches
        /// the throw so it lands at the cursor (≤ autoArcMaxRange), look modes throw along the 3D aim under gravity.
        /// </summary>
        public void Fire(Vector3 origin, Vector3 aimDirection, Vector3 aimPoint, bool hasAimPoint)
        {
            if (!IsActive || _def == null)
            {
                return;
            }

            Vector3 direction = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector3.forward;
            if (Id == SpecialWeaponId.Namekuji && CameraModeService.Current == CameraMode.TopDown)
            {
                direction = ArcDirection(origin, direction, aimPoint, hasAimPoint, _def);
            }

            var shot = new ShotRequest
            {
                Origin = origin,
                Direction = direction,
                Damage = _def.fire.damage,
                Range = RangeMetres,
                ImpactForce = ImpactForce,
                Source = Id == SpecialWeaponId.Raiju ? DamageSourceKind.Chain : DamageSourceKind.Bullet,
                Power = WeaponPower.Heavy,
                PierceDamageFalloff = 1f,
                DrawTracer = false
            };

            ProjectileLaunch launch = ProjectileLaunch.From(_def, ProjectileWeaponId(Id), RangeMetres);
            ProjectileSystem.Spawn(shot, launch);
        }

        /// <summary>Top-down gel throw: the low ballistic angle that lands the blob at the cursor distance (clamped to the auto-arc range).</summary>
        public static Vector3 ArcDirection(Vector3 origin, Vector3 planarAim, Vector3 aimPoint, bool hasAimPoint, WeaponDefinition def)
        {
            Vector3 flat = planarAim;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            flat.Normalize();

            float maxRange = def != null && def.gelPuddle.autoArcMaxRange > 0f ? def.gelPuddle.autoArcMaxRange : 20f;
            float distance = maxRange;
            if (hasAimPoint)
            {
                Vector3 d = aimPoint - origin;
                d.y = 0f;
                distance = Mathf.Clamp(d.magnitude, 1f, maxRange);
            }

            float v = def != null ? Mathf.Max(1f, def.projectile.speed) : 22f;
            float g = def != null ? Mathf.Max(0.01f, def.projectile.gravity) : 12f;
            float s = Mathf.Clamp(g * distance / (v * v), -1f, 1f);
            float theta = 0.5f * Mathf.Asin(s);   // flat-ground low solution; the drop from shot height lands it a touch long
            return (flat * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta)).normalized;
        }
    }
}
