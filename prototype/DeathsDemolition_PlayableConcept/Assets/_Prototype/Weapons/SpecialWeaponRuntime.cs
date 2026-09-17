using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The Special Weapon in the player's hands, whichever of the three it is.
    ///
    /// THE THREE WEAPONS AND WHY THEY ARE THESE THREE
    /// ----------------------------------------------
    /// The brief that produced them is blunt: a Special Weapon must not be another gun.
    /// So none of the three fires a bullet as its point, and no two of them share a verb.
    ///
    ///   Kagura Bell   SWEEP    A shrine bell-tree. The trigger RINGS it and the ring
    ///                          leaves the player as a pressure front in every direction.
    ///                          Does not read the cursor at all. The answer to being
    ///                          surrounded. <see cref="KaguraWaveField"/>.
    ///
    ///   Ofuda Loom    BIND     A talisman loom. Shots SEAL bodies for almost no damage;
    ///                          once enough seals exist, firing into one collapses the
    ///                          whole net at once. Skill is target selection, not
    ///                          tracking. <see cref="OfudaSealWeb"/>.
    ///
    ///   Tide Jewel    GATHER   Ryujin's tide-jewel. Opens a well that drags the crowd
    ///                          inward for a couple of seconds and then implodes on the
    ///                          pile. Rearranges the battlefield rather than clearing it.
    ///                          <see cref="TideWell"/>.
    ///
    /// All three are WEAKER against a single body than the rotary cannon they replace
    /// (roughly 62 / 45 / 55 damage per second against one target versus the cannon's
    /// 240) and far stronger against a crowd. That shape is the point: the classic weapon
    /// stays the single-target tool, and the Special Weapon is what you pick up when
    /// there are too many of them to shoot one at a time.
    ///
    /// WHAT THE REQUEST SEPARATES IS STILL SEPARATED HERE
    /// -------------------------------------------------
    ///   REQ-056 unlimited ammo, no reload -> there is no ammo field and no reload path;
    ///   REQ-055 the active timer starts ON PICKUP -> <see cref="Begin"/> is the only
    ///           thing that starts <see cref="Remaining"/>, and the ground timer lives in
    ///           a completely different object (<see cref="SpecialWeaponPickup"/>);
    ///   REQ-058 final-seconds warning -> <see cref="InFinalSeconds"/> fires once;
    ///   REQ-059 upgrades do not touch it -> this class has NO reference to
    ///           <see cref="WeaponModifierState"/>. Not "ignores it" - cannot see it.
    ///           Adding two more weapons did not add a way in: the three field objects
    ///           above cannot see it either, and the <see cref="ShotRequest"/> upgrade
    ///           block is never populated by anything here.
    ///
    /// The only tunables it reads are under <c>PrototypeConfig.Weapons.Special</c>,
    /// re-read every frame's worth of use, so an F1 edit to a duration or a radius
    /// applies to the weapon already in hand.
    /// </summary>
    public sealed class SpecialWeaponRuntime
    {
        private SpecialWeaponConfigSection _settings;

        private readonly KaguraWaveField _waves = new KaguraWaveField();
        private readonly OfudaSealWeb _web = new OfudaSealWeb();

        private PrototypePool<TideWell> _wellPool;

        // One well template for the whole play session: PoolService keys pools by prefab, so
        // a template per runtime instance (per Run scene load) would leak a pool under the
        // DontDestroyOnLoad root on every run (WP-007; same rule as GoldService).
        private static TideWell s_wellTemplate;

        private float _cooldown;
        private bool _finalSecondsFired;

        public bool IsActive { get; private set; }

        /// <summary>Which of the three is in hand. Only meaningful while active.</summary>
        public SpecialWeaponId Id { get; private set; } = SpecialWeaponId.KaguraBell;

        /// <summary>Seconds of use left. Only counts down while active.</summary>
        public float Remaining { get; private set; }

        /// <summary>The duration this activation started with.</summary>
        public float Duration { get; private set; }

        /// <summary>The Kagura Bell's fronts in flight. F1 readout and test hook.</summary>
        public KaguraWaveField Waves => _waves;

        /// <summary>WP-056: the reach of one primary event — wave radius (bell), dart range (loom), throw range (jewel).</summary>
        public float FireRadius
        {
            get
            {
                if (_settings == null) return 0f;
                switch (Id)
                {
                    case SpecialWeaponId.OfudaLoom: return Mathf.Max(1f, _settings.OfudaLoom.Common.Range);
                    case SpecialWeaponId.TideJewel: return Mathf.Max(1f, _settings.TideJewel.ThrowRange);
                    default: return Mathf.Max(1f, _settings.KaguraBell.WaveRadius);
                }
            }
        }

        /// <summary>WP-056: the duration one primary event's field lasts — wave travel (bell), seal seconds (loom), well seconds (jewel).</summary>
        public float FireSeconds
        {
            get
            {
                if (_settings == null) return 0f;
                switch (Id)
                {
                    case SpecialWeaponId.OfudaLoom: return Mathf.Max(0.2f, _settings.OfudaLoom.SealSeconds);
                    case SpecialWeaponId.TideJewel: return Mathf.Max(0.2f, _settings.TideJewel.WellSeconds);
                    default: return Mathf.Max(0.05f, _settings.KaguraBell.WaveTravelSeconds);
                }
            }
        }

        /// <summary>The Ofuda Loom's live web. F1 readout and test hook.</summary>
        public OfudaSealWeb Web => _web;

        private SpecialWeaponCommon Common => _settings != null ? _settings.Common(Id) : null;

        public string DisplayName
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null && !string.IsNullOrEmpty(common.DisplayName)
                    ? common.DisplayName
                    : Id.ToString();
            }
        }

        public float Damage
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null ? common.Damage : 20f;
            }
        }

        public float FireRate
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null ? Mathf.Max(0.05f, common.FireRate) : 3f;
            }
        }

        public float ShotInterval => 1f / FireRate;

        public float RangeMetres
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null ? Mathf.Max(1f, common.Range) : 30f;
            }
        }

        /// <summary>Reaction force per primary event, in <see cref="DamageForce"/> units.</summary>
        public float ImpactForce
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null ? Mathf.Max(0f, common.ImpactForce) : 2.5f;
            }
        }

        public float CameraShakeScale
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null ? Mathf.Max(0f, common.CameraShake) : 0.4f;
            }
        }

        public float RecoilKick
        {
            get
            {
                SpecialWeaponCommon common = Common;
                return common != null ? Mathf.Max(0f, common.RecoilKick) : 0.08f;
            }
        }

        /// <summary>Always Heavy. That is what earns every Special Weapon hit-stop on solid hits.</summary>
        public WeaponPower Power => WeaponPower.Heavy;

        /// <summary>True once the active timer is inside the warning window (REQ-058).</summary>
        public bool InFinalSeconds =>
            IsActive && _settings != null && Remaining <= Mathf.Max(0f, _settings.FinalSecondsWarning);

        /// <summary>
        /// PROTOTYPE-ONLY per-event character kick. Uniform across the three: the
        /// character rig reads a 0-1 weight rather than a weapon, and all three are Heavy.
        /// </summary>
        public const float RecoilStrength01 = 0.3f;

        /// <summary>Re-read the config. Safe to call every frame.</summary>
        public void ApplySettings(SpecialWeaponConfigSection settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// REQ-055. The clock starts here and nowhere else - specifically NOT when the
        /// weapon spawned on the map.
        /// </summary>
        public void Begin(SpecialWeaponConfigSection settings, SpecialWeaponId id)
        {
            _settings = settings;
            Id = id;

            Duration = settings != null ? Mathf.Max(0.5f, settings.ActiveDuration) : 11f;
            Remaining = Duration;
            IsActive = true;
            _cooldown = 0f;
            _finalSecondsFired = false;

            _waves.Clear();
            _web.Clear();
            TideWell.CloseAll();

            AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponPickup, Vector3.zero, 1f, 90);
            AudioEvents.RaiseLoop(CombatAudioEvent.HeavyWeaponSpinUp, Vector3.zero, 1f, 85);
            WeaponEvents.PublishSpecialPickedUp(Duration);
        }

        /// <summary>
        /// The activation ran its course (or the debug panel ended it as if it had): the
        /// player LOST the weapon, and the expiry news cue says so. Idempotent.
        /// </summary>
        public void End()
        {
            Finish(false);
        }

        /// <summary>
        /// WP-007 (AP-10, OS-2): the activation is stopped by something other than its own
        /// timer - run end, death, new run, teardown. Same clean-up as <see cref="End"/>
        /// (fields, loop, Ended event so the classic weapon comes back), but the "you lost
        /// it" news cue is not played: there is nobody to tell, or the run is over. The
        /// lifecycle owner (the director / controller) publishes the Cancelled transition.
        /// Idempotent.
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

            // Every field object dies with the weapon. A seal that outlived the loom would
            // be a talisman with no owner, and a well that outlived the jewel would keep
            // spending the crowd budget for a weapon that is no longer in the run.
            _waves.Clear();
            _web.Clear();
            TideWell.CloseAll();

            AudioEvents.StopLoop(CombatAudioEvent.HeavyWeaponSpinUp);

            // Two separate cues on purpose: the spin-down is the OBJECT winding to a halt,
            // the expiry is the NEWS that the player has lost it. The audio task can voice
            // them completely differently per weapon. A cancel keeps the object's spin-down
            // and drops the news.
            AudioEvents.RaiseCritical(CombatAudioEvent.HeavyWeaponSpinDown, Vector3.zero, 1f, 85);
            if (!cancelled)
            {
                AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponExpired, Vector3.zero, 1f, 90);
            }

            WeaponEvents.PublishSpecialEnded();
        }

        /// <summary>
        /// Advance the clocks and every live field. Returns true on the frame the
        /// activation ends, which is the controller's cue to hand the classic weapon back
        /// (REQ-057).
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

            switch (Id)
            {
                case SpecialWeaponId.KaguraBell:
                    _waves.Tick(dt);
                    break;

                case SpecialWeaponId.OfudaLoom:
                    _web.Tick(dt);
                    break;
            }

            Remaining -= dt;

            if (!_finalSecondsFired && InFinalSeconds)
            {
                _finalSecondsFired = true;
                WeaponEvents.PublishSpecialFinalSeconds(Remaining);
                AudioEvents.RaiseCritical(CombatAudioEvent.SpecialWeaponFinalSeconds, Vector3.zero, 1f, 95);
            }

            if (Remaining > 0f)
            {
                return false;
            }

            End();
            return true;
        }

        public bool CanFire => IsActive && _cooldown <= 0f;

        public void ConsumeShot()
        {
            _cooldown += ShotInterval;
        }

        public void ClampCooldown()
        {
            float floor = -ShotInterval;
            if (_cooldown < floor)
            {
                _cooldown = floor;
            }
        }

        /// <summary>One primary event, whatever that means for the weapon in hand.</summary>
        public void Fire(Vector3 origin, Vector3 aimDirection)
        {
            if (!IsActive || _settings == null)
            {
                return;
            }

            switch (Id)
            {
                case SpecialWeaponId.OfudaLoom:
                    FireLoom(origin, aimDirection);
                    break;

                case SpecialWeaponId.TideJewel:
                    FireJewel(origin, aimDirection);
                    break;

                default:
                    FireBell(origin);
                    break;
            }
        }

        // ------------------------------------------------------------------ Kagura Bell

        private void FireBell(Vector3 origin)
        {
            // The front leaves the PLAYER, not the muzzle. A pressure ring starting half a
            // metre to one side would reach the two sides of a surrounding crowd at
            // different times, which is exactly the confusion an aim-free weapon must not
            // have.
            _waves.Emit(origin, _settings.KaguraBell);
        }

        // ------------------------------------------------------------------ Ofuda Loom

        private void FireLoom(Vector3 origin, Vector3 aimDirection)
        {
            OfudaLoomSettings loom = _settings.OfudaLoom;

            var request = new ShotRequest
            {
                Origin = origin,
                Direction = aimDirection,
                Damage = loom.Common.Damage,
                Range = Mathf.Max(1f, loom.Common.Range),
                ImpactForce = loom.Common.ImpactForce,
                Source = DamageSourceKind.Bullet,
                Power = WeaponPower.Light,

                PierceTargets = 0,
                PierceDamageFalloff = 1f,

                DrawTracer = true,
                TracerWidth = 0.03f,
                TracerLifetime = 0.07f
            };

            ShotResult result = HitscanEngine.Fire(request);

            if (result.EnemiesHit <= 0)
            {
                return;
            }

            // The dart has already done its damage through the shared path. What is left
            // is the talisman, which needs the TARGET rather than the ray - resolved from
            // the impact point rather than by casting the same ray twice.
            if (!TryResolveNearest(result.EndPoint, out DamageTarget target))
            {
                return;
            }

            bool alreadySealed = _web.IsSealed(target.Id);

            if (alreadySealed && _web.ArmedSealCount >= Mathf.Max(1, loom.MinSealsToCollapse))
            {
                _web.Collapse(loom);
                return;
            }

            _web.ApplySeal(target, loom);
        }

        /// <summary>
        /// PROTOTYPE-ONLY: how far from the impact point the talisman looks for the body it
        /// belongs on. Comfortably inside one enemy's own radius.
        /// </summary>
        private const float SealResolveRadius = 1.1f;

        private static readonly System.Collections.Generic.List<DamageTarget> s_resolveScratch =
            new System.Collections.Generic.List<DamageTarget>(8);

        private static bool TryResolveNearest(Vector3 point, out DamageTarget target)
        {
            target = default;

            int count = HitscanEngine.OverlapEnemies(point, SealResolveRadius, s_resolveScratch);
            if (count == 0)
            {
                return false;
            }

            float bestSqr = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                DamageTarget candidate = s_resolveScratch[i];
                if (!candidate.IsValid)
                {
                    continue;
                }

                float sqr = (HitscanEngine.CentreOf(candidate) - point).sqrMagnitude;
                if (sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                target = candidate;
                found = true;
            }

            return found;
        }

        // ------------------------------------------------------------------ Tide Jewel

        private void FireJewel(Vector3 origin, Vector3 aimDirection)
        {
            TideJewelSettings jewel = _settings.TideJewel;

            if (TideWell.LiveCount >= Mathf.Max(1, jewel.MaxWells))
            {
                // Refusing the throw rather than recycling the oldest well is deliberate: a
                // well that vanished before it imploded would take the crowd it had already
                // gathered with it, and the player would have no way to tell why.
                return;
            }

            EnsureWellPool();
            if (_wellPool == null)
            {
                return;
            }

            Vector3 landing = ResolveLanding(origin, aimDirection, Mathf.Max(1f, jewel.ThrowRange));

            TideWell well = _wellPool.Get(landing, Quaternion.identity);
            if (well != null)
            {
                well.Begin(jewel);
            }
        }

        /// <summary>
        /// PROTOTYPE-ONLY: how far above the landing point the ground is probed from, so a
        /// well thrown over flat ground still ends up on the floor.
        /// </summary>
        private const float JewelGroundProbe = 6f;

        private static Vector3 ResolveLanding(Vector3 origin, Vector3 aimDirection, float range)
        {
            Vector3 direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.forward;

            Vector3 landing = origin + direction * range;

            // First along the aim ray, so a jewel thrown at a wall lands at its foot rather
            // than inside it.
            if (Physics.Raycast(origin, direction, out RaycastHit forward, range,
                    PrototypeLayers.BulletHitMask, QueryTriggerInteraction.Ignore))
            {
                landing = forward.point - direction * 0.5f;
            }

            // Then straight down, so it always ends up on a surface.
            if (Physics.Raycast(landing + Vector3.up * JewelGroundProbe, Vector3.down, out RaycastHit down,
                    JewelGroundProbe * 2f, PrototypeLayers.BulletHitMask, QueryTriggerInteraction.Ignore))
            {
                landing = down.point;
            }

            return landing;
        }

        private void EnsureWellPool()
        {
            if (_wellPool != null)
            {
                return;
            }

            if (s_wellTemplate == null)
            {
                s_wellTemplate = TideWell.CreateTemplate();
            }

            // Capacity is the shipped MaxWells plus headroom: a well is released on the
            // frame it implodes, and the very next throw must not have to wait for the
            // free list.
            _wellPool = PoolService.GetOrCreate(s_wellTemplate, 2, 4, PoolExhaustedPolicy.RecycleOldest);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_wellTemplate = null;
        }
    }
}
