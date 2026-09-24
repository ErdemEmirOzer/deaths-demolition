using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
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

        /// <summary>WP-069: the v3 definition behind the equipped numbers (null for a hand-built settings object).</summary>
        public WeaponDefinition Definition => _settings != null ? _settings.Definition : null;

        // ------------------------------------------------------------------ ammo

        public int Ammo { get; private set; }

        public int MagazineSize => _settings != null ? Mathf.Max(1, _settings.MagazineSize) : 1;

        /// <summary>Debug-panel infinite ammo. Never reloads, never runs dry.</summary>
        public bool InfiniteAmmo { get; set; }

        /// <summary>WP-069: no reserve limit (v3 ammo.reserve &lt; 0, the M3 behaviour, or no definition). B2 draws finite reserves.</summary>
        public bool ReserveInfinite => Definition == null || Definition.ammo.reserve < 0;

        /// <summary>WP-069: rounds carried beyond the magazine (0 while <see cref="ReserveInfinite"/>).</summary>
        public int ReserveAmmo => ReserveInfinite ? 0 : Mathf.Max(0, Definition.ammo.reserve);

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

        // ---- WP-042 feel knobs (ClassicWeaponSettings passthrough, clamped to the request's ceilings)

        /// <summary>Seconds the rig kick takes to settle (WeaponVisualRig spring); 0.10 when unset.</summary>
        public float RecoilRecoverySeconds => _settings != null && _settings.RecoilRecoverySeconds > 0.005f ? _settings.RecoilRecoverySeconds : 0.10f;

        /// <summary>Share of the kick that reads as muzzle rise, 0..1.</summary>
        public float RecoilUpShare => _settings != null ? Mathf.Clamp01(_settings.RecoilUpShare) : 0.15f;

        /// <summary>Gamepad pulse per shot as a fraction of the WP-024 rumble ceiling, 0..1.</summary>
        public float RumbleFraction => _settings != null ? Mathf.Clamp01(_settings.RumbleFraction) : 0.5f;

        /// <summary>Gamepad pulse length per shot, ms, ≤ 120 (the request's ceiling).</summary>
        public int RumbleMilliseconds => _settings != null ? Mathf.Clamp(_settings.RumbleMilliseconds, 0, 120) : 60;

        /// <summary>True when the multi-pellet pattern is the WP-042 centre + ring (PelletRingRadiusShare &gt; 0).</summary>
        public bool UsesPelletRing => _settings != null && _settings.PelletCount > 1 && _settings.PelletRingRadiusShare > 0f;

        /// <summary>Ring phase of the current trigger pull (rolled on pellet 0; tests).</summary>
        public float PelletRingPhase => _ringPhase;

        private float _ringPhase;

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

        /// <summary>
        /// WP-069: hip-fire cone half-angle for the active camera mode (v3 definition: top-down /
        /// third person / first person); the M3 SpreadDegrees without a definition.
        /// </summary>
        public float BaseSpreadDegrees
        {
            get
            {
                if (_settings == null) return 0f;
                WeaponDefinition d = _settings.Definition;
                if (d == null) return Mathf.Max(0f, _settings.SpreadDegrees);
                // Top-down keeps the (F1-editable) config copy; the other modes read the definition.
                return CameraModeService.Current == CameraMode.TopDown
                    ? Mathf.Max(0f, _settings.SpreadDegrees)
                    : Mathf.Max(0f, d.SpreadFor((int)CameraModeService.Current).hip);
            }
        }

        /// <summary>Current cone half-angle including accumulated bloom.</summary>
        public float CurrentSpreadDegrees =>
            _settings != null ? BaseSpreadDegrees + _bloom : 0f;

        /// <summary>
        /// WP-069 ADR-069-1: how this weapon's projectiles fly (v3 projectile block; a fast straight
        /// bullet at the M3 range for a hand-built settings object). Pellet weapons fly Pellet kind.
        /// </summary>
        public ProjectileLaunch Launch(int pelletIndex)
        {
            ProjectileLaunch launch = ProjectileLaunch.From(Definition, Id, Range);
            launch.PelletIndex = pelletIndex;
            if (PelletCount > 1 && launch.Kind == ProjectileKind.Bullet)
            {
                launch.Kind = ProjectileKind.Pellet;
            }

            return launch;
        }

        /// <summary>WP-068 U12a: horizontal half-angle of the pellet ring (the ApplyRing radius); 0 without the ring pattern.</summary>
        public float PelletRingDegrees =>
            UsesPelletRing ? Mathf.Max(0f, _settings.PelletRingRadiusShare) * CurrentSpreadDegrees : 0f;

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

            // WP-042: a pellet weapon fires one centre pellet and a ring (equal arc length on the
            // flattened cone, random phase per trigger pull, small per-pellet jitter) — the fan
            // reads the same every shot and the mean hit count is unchanged. Single-pellet
            // weapons and a zero ring share keep the WP-002 independent scatter.
            Vector3 direction;
            if (UsesPelletRing)
            {
                if (pelletIndex == 0) _ringPhase = Random.value;
                direction = SpreadUtility.ApplyRing(aimDirection, CurrentSpreadDegrees, pelletIndex, PelletCount,
                    _settings.PelletRingRadiusShare, _settings.PelletRingJitterShare, _ringPhase);
            }
            else
            {
                direction = SpreadUtility.Apply(aimDirection, CurrentSpreadDegrees);
            }

            var request = new ShotRequest
            {
                Origin = origin,
                Direction = direction,
                Damage = (_settings != null ? _settings.Damage : 1f) * (_modifiers != null ? _modifiers.DamageMultiplier : 1f),   // WP-077 Hearth
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

                // Per-shot branches: the first pellet of a trigger pull carries them, the
                // rest of the spread does not (WeaponBranchContract.ResolutionOf == PerShot).
                request.RicochetChance = perShotCarrier ? m.RicochetChance : 0f;
                request.RicochetBounces = perShotCarrier ? m.RicochetBounces : 0;
                request.RicochetRadius = m.RicochetRadius;
                request.RicochetDamageScale = m.RicochetDamageScale;

                request.ChainTargets = perShotCarrier ? m.ChainTargets : 0;
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

        /// <summary>
        /// WP-042 pellet pattern. Pellet 0 is the centre (the aim reward); pellets 1..n−1 sit on
        /// an ellipse of horizontal radius <paramref name="ringShare"/> × <paramref name="degrees"/>
        /// and vertical radius × <see cref="VerticalSpreadFactor"/>, spaced by EQUAL ARC LENGTH
        /// (equal parameter angle would bunch pellets at the flat ends), rotated by
        /// <paramref name="phase01"/> turns, each jittered by ±<paramref name="jitterShare"/> ×
        /// <paramref name="degrees"/>. No allocation.
        /// </summary>
        public static Vector3 ApplyRing(Vector3 direction, float degrees, int pelletIndex, int pelletCount,
            float ringShare, float jitterShare, float phase01)
        {
            Vector3 forward = direction.normalized;
            if (degrees <= 0.0001f || pelletCount <= 1)
            {
                return forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }

            right.Normalize();

            float jitter = Mathf.Max(0f, jitterShare) * degrees;
            float yaw, pitch;
            if (pelletIndex <= 0)
            {
                yaw = Random.Range(-jitter, jitter);
                pitch = Random.Range(-jitter, jitter) * VerticalSpreadFactor;
            }
            else
            {
                float a = Mathf.Max(0f, ringShare) * degrees;          // horizontal radius, degrees
                float b = a * VerticalSpreadFactor;                     // vertical radius
                int ringCount = pelletCount - 1;
                // Equal-arc-length parameter for this pellet: walk the ellipse in RingSegments steps.
                float target = ((pelletIndex - 1) + Mathf.Repeat(phase01, 1f)) / ringCount;
                float t = RingParameterAtArcFraction(a, b, target);
                yaw = a * Mathf.Cos(t) + Random.Range(-jitter, jitter);
                pitch = b * Mathf.Sin(t) + Random.Range(-jitter, jitter) * VerticalSpreadFactor;
            }

            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, right);
            return rotation * forward;
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
