using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// One of the three classic weapons, live.
    ///
    /// Holds ammo, the fire clock, the reload clock and accumulated spread bloom, and
    /// turns all of that plus the run's upgrade tiers into a <see cref="ShotRequest"/>.
    /// Deliberately not a MonoBehaviour: it has no transform of its own and no Update of
    /// its own, which makes it trivially testable and impossible to accidentally leave
    /// running after a run ends.
    ///
    /// REQ-051 is structural here: the weapon is chosen once at
    /// <see cref="Equip"/> and there is no code path that swaps it during a run. The
    /// only thing that changes it is the debug panel, which exists outside normal play.
    ///
    /// Everything numeric is re-read from <see cref="ClassicWeaponSettings"/> on every
    /// <see cref="ApplySettings"/>, so a live edit in the F1 panel changes the weapon
    /// mid-magazine.
    /// </summary>
    public sealed class ClassicWeaponRuntime
    {
        private ClassicWeaponSettings _settings;
        private WeaponModifierState _modifiers;

        private float _cooldown;
        private float _bloom;

        // ------------------------------------------------------------------ identity

        public ClassicWeaponId Id => _settings != null ? _settings.Id : ClassicWeaponId.AssaultRifle;

        public string DisplayName => _settings != null ? _settings.DisplayName : "-";

        public bool IsConfigured => _settings != null;

        // ------------------------------------------------------------------ ammo

        public int Ammo { get; private set; }

        public int MagazineSize => _settings != null ? Mathf.Max(1, _settings.MagazineSize) : 1;

        /// <summary>Debug-panel infinite ammo. Never reloads, never runs dry.</summary>
        public bool InfiniteAmmo { get; set; }

        public bool IsReloading { get; private set; }

        public float ReloadRemaining { get; private set; }

        public float ReloadDuration { get; private set; }

        public float ReloadProgress01 =>
            ReloadDuration > 0f ? Mathf.Clamp01(1f - ReloadRemaining / ReloadDuration) : 0f;

        // ------------------------------------------------------------------ derived numbers

        /// <summary>Shots per second after upgrades.</summary>
        public float EffectiveFireRate
        {
            get
            {
                if (_settings == null)
                {
                    return 1f;
                }

                float multiplier = _modifiers != null ? _modifiers.FireRateMultiplier : 1f;
                return Mathf.Max(0.05f, _settings.FireRate * multiplier);
            }
        }

        public float ShotInterval => 1f / EffectiveFireRate;

        /// <summary>Reload duration after upgrades.</summary>
        public float EffectiveReloadTime
        {
            get
            {
                if (_settings == null)
                {
                    return 1f;
                }

                float multiplier = _modifiers != null ? _modifiers.ReloadTimeMultiplier : 1f;
                return Mathf.Max(0.05f, _settings.ReloadTime * multiplier);
            }
        }

        public int PelletCount => _settings != null ? Mathf.Max(1, _settings.PelletCount) : 1;

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

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Start a run with this weapon. Fills the magazine and clears every timer.</summary>
        public void Equip(ClassicWeaponSettings settings, WeaponModifierState modifiers)
        {
            _settings = settings;
            _modifiers = modifiers;

            Ammo = MagazineSize;
            _cooldown = 0f;
            _bloom = 0f;
            IsReloading = false;
            ReloadRemaining = 0f;
            ReloadDuration = 0f;
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

            if (_settings != null && _bloom > 0f)
            {
                _bloom = Mathf.Max(0f, _bloom - _settings.SpreadBloomDecay * dt);
            }

            if (!IsReloading)
            {
                return;
            }

            ReloadRemaining -= dt;
            if (ReloadRemaining > 0f)
            {
                return;
            }

            IsReloading = false;
            ReloadRemaining = 0f;
            Ammo = MagazineSize;
            WeaponEvents.PublishReloadCompleted();
            AudioEvents.Raise(CombatAudioEvent.ReloadEnd, Vector3.zero, 1f, 40);
        }

        public bool HasAmmo => InfiniteAmmo || Ammo > 0;

        /// <summary>True when a trigger pull would actually produce a shot right now.</summary>
        public bool CanFire => _settings != null && !IsReloading && _cooldown <= 0f && HasAmmo;

        /// <summary>True when the magazine is empty and a reload is worth starting.</summary>
        public bool NeedsReload => !InfiniteAmmo && Ammo < MagazineSize;

        public bool TryBeginReload()
        {
            if (_settings == null || IsReloading || InfiniteAmmo || Ammo >= MagazineSize)
            {
                return false;
            }

            ReloadDuration = EffectiveReloadTime;
            ReloadRemaining = ReloadDuration;
            IsReloading = true;

            WeaponEvents.PublishReloadStarted(ReloadDuration);
            AudioEvents.Raise(CombatAudioEvent.ReloadStart, Vector3.zero, 1f, 40);
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
            WeaponEvents.PublishReloadInterrupted();
        }

        /// <summary>Book-keeping for one trigger pull: ammo, fire clock, spread bloom.</summary>
        public void ConsumeShot()
        {
            if (!InfiniteAmmo)
            {
                Ammo = Mathf.Max(0, Ammo - 1);
            }

            // Add rather than assign, so a frame that fires twice does not lose the
            // second interval and the weapon cannot drift faster than its fire rate.
            _cooldown += ShotInterval;

            if (_settings != null && _settings.SpreadBloomMax > 0f)
            {
                _bloom = Mathf.Min(_settings.SpreadBloomMax, _bloom + _settings.SpreadBloomPerShot);
            }
        }

        /// <summary>Current cone half-angle including accumulated bloom.</summary>
        public float CurrentSpreadDegrees =>
            _settings != null ? Mathf.Max(0f, _settings.SpreadDegrees) + _bloom : 0f;

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
        /// </summary>
        public ShotRequest BuildShot(Vector3 origin, Vector3 aimDirection)
        {
            WeaponModifierState m = _modifiers;
            bool multiPellet = PelletCount > 1;

            var request = new ShotRequest
            {
                Origin = origin,
                Direction = SpreadUtility.Apply(aimDirection, CurrentSpreadDegrees),
                Damage = _settings != null ? _settings.Damage : 1f,
                Range = Range,
                // On the shared DamageForce scale, NOT the raw config knob. See WeaponImpulse.
                ImpactForce = WeaponImpulse.ForClassic(_settings),
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

                request.RicochetChance = m.RicochetChance;
                request.RicochetBounces = m.RicochetBounces;
                request.RicochetRadius = m.RicochetRadius;
                request.RicochetDamageScale = m.RicochetDamageScale;

                request.ChainTargets = m.ChainTargets;
                request.ChainRadius = m.ChainRadius;
                request.ChainDamageScale = m.ChainDamageScale;
                request.ChainDamageFalloff = m.ChainDamageFalloff;

                // A shotgun with the Explosion branch must not roll the blast once per
                // pellet at full odds - that would be eight rolls for one trigger pull.
                request.ExplosionChance = multiPellet ? m.ExplosionChance / PelletCount : m.ExplosionChance;
                request.ExplosionRadius = m.ExplosionRadius;
                request.ExplosionDamageScale = m.ExplosionDamageScale;
            }
            else
            {
                request.PierceDamageFalloff = 1f;
            }

            return request;
        }
    }

    /// <summary>Cone spread, applied around the aim direction.</summary>
    public static class SpreadUtility
    {
        /// <summary>
        /// PROTOTYPE-ONLY: vertical spread is flattened to a third of horizontal. With a
        /// high fixed camera, vertical scatter is nearly invisible and only costs hits,
        /// while horizontal scatter is what the player actually reads as "this weapon is
        /// loose".
        /// </summary>
        public const float VerticalSpreadFactor = 0.35f;

        public static Vector3 Apply(Vector3 direction, float degrees)
        {
            if (degrees <= 0.0001f)
            {
                return direction.normalized;
            }

            Vector3 forward = direction.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }

            right.Normalize();

            float yaw = Random.Range(-degrees, degrees);
            float pitch = Random.Range(-degrees, degrees) * VerticalSpreadFactor;

            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, right);
            return rotation * forward;
        }
    }
}
