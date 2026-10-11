using System;
using System.Collections.Generic;
using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// PROTOTYPE-ONLY placeholder classic weapon. Names, numbers and feel knobs are
    /// prototype content, not an approved weapon roster.
    /// </summary>
    [Serializable]
    public sealed class ClassicWeaponSettings
    {
        /// <summary>
        /// WP-069 B2: <see cref="UnlockProfileLevel"/> of a weapon the DEC-008 profile ladder never opens — it is
        /// opened by the DEC-095 meta tree (S-04 WP-077) instead. No profile level reaches it.
        /// </summary>
        public const int NotOnProfileLadder = int.MaxValue;

        /// <summary>WP-069 (review R1-A #19): a field-for-field copy (the definition reference is shared, it is data).</summary>
        public ClassicWeaponSettings Clone() => (ClassicWeaponSettings)MemberwiseClone();

        [PrototypeOnly(PendingDecisions.None, "Identifier of a placeholder weapon, not a tunable value.")]
        public ClassicWeaponId Id = ClassicWeaponId.AssaultRifle;

        [PrototypeOnly(PendingDecisions.None, "Placeholder display name from the request.")]
        public string DisplayName = "Assault Rifle";

        [PrototypeOnly(PendingDecisions.CombatScaling, "Damage per bullet (per pellet for shotguns).")]
        public float Damage = 11f;

        [PrototypeOnly(PendingDecisions.None, "Shots per second.")]
        public float FireRate = 8.5f;

        [PrototypeOnly(PendingDecisions.None, "Magazine size.")]
        public int MagazineSize = 30;

        [PrototypeOnly(PendingDecisions.None, "Reload duration in seconds.")]
        public float ReloadTime = 1.5f;

        [PrototypeOnly(PendingDecisions.None, "Projectiles per trigger pull. Shotgun = 8, others = 1.")]
        public int PelletCount = 1;

        [PrototypeOnly(PendingDecisions.None, "Cone half-angle in degrees. Request only says 'balanced / wider / close range'.", true)]
        public float SpreadDegrees = 1.6f;

        [PrototypeOnly(PendingDecisions.None, "Extra spread added while holding fire, up to SpreadBloomMax.", true)]
        public float SpreadBloomPerShot = 0.35f;

        [PrototypeOnly(PendingDecisions.None, "Ceiling for accumulated bloom.", true)]
        public float SpreadBloomMax = 3.5f;

        [PrototypeOnly(PendingDecisions.None, "Bloom decay per second while not firing.", true)]
        public float SpreadBloomDecay = 6f;

        [PrototypeOnly(PendingDecisions.None, "Procedural recoil kick applied to the weapon model.", true)]
        public float RecoilKick = 0.09f;

        [PrototypeOnly(PendingDecisions.None, "Camera shake scale for this weapon. Must never break aim.", true)]
        public float CameraShake = 0.35f;

        [PrototypeOnly(PendingDecisions.None, "Stagger impulse pushed into the enemy hit reaction.", true)]
        public float StaggerImpulse = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "Effective range in metres before the tracer is culled.", true)]
        public float Range = 45f;

        // ---- WP-042 weapon feel v1 (game-designer outline, in-band; consumed by WeaponVisualRig / GamepadRumble / SpreadUtility)

        [PrototypeOnly(PendingDecisions.None, "WP-042: seconds the rig kick takes to settle (A ≈ one fire cycle = discrete hits, B shorter than its cycle = creeping, C long = heavy). Invented.", true)]
        public float RecoilRecoverySeconds = 0.10f;

        [PrototypeOnly(PendingDecisions.None, "WP-042: share of the kick that reads as pitch-up (the rest is slide-back); 0.15 rifle, 0.10 carbine, 0.30 shotgun. Invented.", true)]
        public float RecoilUpShare = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "WP-042: gamepad pulse per shot as a fraction of the rumble ceiling (InputPreferences.MaxRumbleIntensity). Invented.", true)]
        public float RumbleFraction = 0.67f;

        [PrototypeOnly(PendingDecisions.None, "WP-042: gamepad pulse length per shot, ms (≤ 120).", true)]
        public int RumbleMilliseconds = 70;

        [PrototypeOnly(PendingDecisions.None, "WP-042: multi-pellet pattern — one centre pellet + a ring at RingRadiusShare × SpreadDegrees with ±RingJitterShare × SpreadDegrees jitter (0 = the WP-002 independent scatter).", true)]
        public float PelletRingRadiusShare = 0.6f;

        [PrototypeOnly(PendingDecisions.None, "WP-042: per-pellet jitter on the ring as a share of SpreadDegrees.", true)]
        public float PelletRingJitterShare = 0.10f;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Profile level required to unlock. AR=1, SMG=2, Shotgun=3; WP-107 (R4, user 11.10): every classic weapon but rl_1 at 1.")]
        public int UnlockProfileLevel = 1;

        /// <summary>
        /// WP-069 A4: the v3 definition these numbers were copied from (null for a code-default
        /// entry). The runtime reads the v3-only data (ballistics, per-mode spread, fire mode …)
        /// through it; the copy itself stays config-scoped so a cloned config keeps its own numbers.
        /// </summary>
        [NonSerialized] public WeaponDefinition Definition;
    }

    /// <summary>
    /// WP-069 B4 (DEC-094 / DEC-097): which of the two Special Weapons a drop is. The M3 three (Kagura Bell,
    /// Ofuda Loom, Tide Jewel) left the game; their numbers are not reused as meanings, only as slots.
    /// </summary>
    public enum SpecialWeaponId
    {
        /// <summary>sw_arc · chain-lightning orb (DIRECTION §7a).</summary>
        Raiju = 0,

        /// <summary>sw_gel · slippery gel arc and puddles (DIRECTION §7b).</summary>
        Namekuji = 1
    }

    /// <summary>
    /// The knobs every Special Weapon has, whatever its verb is — the shape the F1 panel, the HUD, the rumble and
    /// the stagger contract read. WP-069 B4: a per-config view built from the Special's WeaponDefinition v3
    /// (Resources/Arsenal: sw_arc / sw_gel) plus its FeelConfig row; not serialized.
    /// </summary>
    [Serializable]
    public sealed class SpecialWeaponCommon
    {
        public string DisplayName = "Special Weapon";

        public float Damage = 20f;

        /// <summary>Primary events per second (the cycle; Raiju's charge comes on top).</summary>
        public float FireRate = 3f;

        /// <summary>Reaction force in DamageForce units (1 = one rifle bullet).</summary>
        public float ImpactForce = 2.5f;

        public float CameraShake = 0.4f;

        public float RecoilKick = 0.08f;

        /// <summary>Effective reach in metres (the projectile's range).</summary>
        public float Range = 30f;

        public float RumbleFraction = 0.7f;

        public int RumbleMilliseconds = 80;

        /// <summary>Relative chance this weapon is the one that drops. Equal weights = uniform.</summary>
        public float SpawnWeight = 1f;

        /// <summary>The definition the numbers came from (null without a catalog).</summary>
        [NonSerialized] public WeaponDefinition Definition;
    }

    /// <summary>
    /// PROTOTYPE-ONLY Special Weapon drop rules (REQ-052..059, DEC-024/025) and the two weapons' views.
    ///
    /// Everything serialized here is the DROP, not the weapon. The weapons are WeaponDefinition v3 assets
    /// (WP-069 A4): ammo, charge, projectile, chain / gel rules and the lifecycle cap (DEC-097: ammo + time
    /// cap, Raiju 3/9 · 40 s, Namekuji 8/16 · 45 s — replaces DEC-026's fixed 11 s).
    /// </summary>
    [Serializable]
    public sealed class SpecialWeaponConfigSection
    {
        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "WP-069 B4: only a Special WITHOUT a definition (bare EditMode) uses this; the v3 cap is the definition's " +
            "specialLifecycle.activeCapSeconds (DEC-097). The M3 request value was 11 s.")]
        public float ActiveDuration = 11f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "Seconds the drop waits on the map. Request said 12; measured against the built " +
            "map that is under the median walk to a valid drop point (67 m = 10.9 s at " +
            "MoveSpeed 6.2 m/s) before any combat detour, so the player could not reach it. " +
            "22 s covers the p90 walk (15.1 s) with a 1.35x combat-detour factor and ~1 s of " +
            "reaction, and still loses the corner-to-corner cases - which is REQ-054's point. " +
            "INVENTED: the request says 12; 22 is a measurement, not a stated value.", true)]
        public float GroundLifetime = 22f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request: can appear from after round 3 onwards.")]
        public int FirstEligibleRound = 4;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request value: 35% base chance")]
        [Range(0f, 1f)]
        public float BaseSpawnChance = 0.35f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request says chance may rise slowly; step size is a guess.", true)]
        [Range(0f, 0.2f)]
        public float SpawnChanceGrowthPerRound = 0.03f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request value: never above 65%")]
        [Range(0f, 1f)]
        public float MaxSpawnChance = 0.65f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request value: spawn between 30% and 70% of round progress")]
        [Range(0f, 1f)]
        public float SpawnWindowMin = 0.30f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request value: spawn between 30% and 70% of round progress")]
        [Range(0f, 1f)]
        public float SpawnWindowMax = 0.70f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request: second Special possible from round 8.")]
        public int SecondSpawnFromRound = 8;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request value: 10% chance of a sequential second Special")]
        [Range(0f, 1f)]
        public float SecondSpawnChance = 0.10f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Seconds of 'final seconds' audio/visual warning.", true)]
        public float FinalSecondsWarning = 3f;

        [PrototypeOnly(PendingDecisions.None, "Minimum distance from the player for a valid Special drop point.", true)]
        public float MinDistanceFromPlayer = 22f;

        /// <summary>
        /// WP-069 B4: F1 / tests — when above 0, every Special's time cap is this many seconds instead of its
        /// definition's (40 s / 45 s). Not serialized: a shipped config always runs the DEC-097 caps.
        /// </summary>
        [NonSerialized] public float CapOverrideSeconds;

        /// <summary>Every Special id, in roll order.</summary>
        public static readonly SpecialWeaponId[] All = { SpecialWeaponId.Raiju, SpecialWeaponId.Namekuji };

        [NonSerialized] private SpecialWeaponCommon[] _commonView;

        /// <summary>The shared knobs of one weapon (a per-config copy of its definition). Never null for a defined id.</summary>
        public SpecialWeaponCommon Common(SpecialWeaponId id)
        {
            if (_commonView == null)
            {
                _commonView = new SpecialWeaponCommon[All.Length];
                for (int i = 0; i < All.Length; i++) _commonView[i] = BuildCommon(All[i]);
            }

            int index = (int)id;
            return index >= 0 && index < _commonView.Length ? _commonView[index] : _commonView[0];
        }

        /// <summary>Drop the copies so the next access rebuilds them from the definitions (tests, F1 reset).</summary>
        public void RebuildCommonView()
        {
            _commonView = null;
        }

        /// <summary>The definition of a Special (Resources/Arsenal sw_arc / sw_gel), or null.</summary>
        public static WeaponDefinition DefinitionOf(SpecialWeaponId id)
        {
            return Arsenal.Get(SpecialWeaponKeys.Of(id));
        }

        private static SpecialWeaponCommon BuildCommon(SpecialWeaponId id)
        {
            WeaponDefinition d = DefinitionOf(id);
            var c = new SpecialWeaponCommon { DisplayName = id.ToString(), Definition = d };
            if (d != null)
            {
                c.DisplayName = string.IsNullOrEmpty(d.displayName) ? id.ToString() : d.displayName;
                c.Damage = d.fire.damage;
                c.FireRate = d.ShotsPerSecond;
                c.ImpactForce = d.projectile.impulse;
                c.Range = d.projectile.maxRange;
                c.SpawnWeight = d.specialLifecycle.spawnWeight;
            }

            // Feel is not game data: kick / shake / rumble mirror the weapon's FeelConfig row (WP-062).
            WeaponFeel row = FeelRuntime.Current.Weapon(WeaponFeelIds.From(id));
            c.RecoilKick = row.recoilKickMeters;
            c.CameraShake = row.shakeWeight;
            c.RumbleFraction = row.rumbleFraction;
            c.RumbleMilliseconds = row.rumbleDurationMs;
            return c;
        }

        /// <summary>WP-069 B4 (DEC-097): the active time cap of one weapon — its definition's, else <see cref="ActiveDuration"/>.</summary>
        public float CapSeconds(SpecialWeaponId id)
        {
            if (CapOverrideSeconds > 0f) return CapOverrideSeconds;
            WeaponDefinition d = Common(id).Definition;
            return d != null && d.specialLifecycle.activeCapSeconds > 0f ? d.specialLifecycle.activeCapSeconds : Mathf.Max(0.5f, ActiveDuration);
        }

        /// <summary>Display name of one weapon, empty-safe.</summary>
        public string DisplayName(SpecialWeaponId id)
        {
            SpecialWeaponCommon common = Common(id);
            return common != null && !string.IsNullOrEmpty(common.DisplayName)
                ? common.DisplayName
                : id.ToString();
        }

        /// <summary>Relative drop weight of one weapon, floored at zero.</summary>
        public float SpawnWeight(SpecialWeaponId id)
        {
            SpecialWeaponCommon common = Common(id);
            return common != null ? Mathf.Max(0f, common.SpawnWeight) : 0f;
        }

        /// <summary>
        /// Roll which of the two drops, weighted by SpawnWeight. Falls back to Raiju rather than to nothing
        /// when every weight has been zeroed in the F1 panel.
        /// </summary>
        public SpecialWeaponId RollWeapon(float roll01)
        {
            float raiju = SpawnWeight(SpecialWeaponId.Raiju);
            float namekuji = SpawnWeight(SpecialWeaponId.Namekuji);
            float total = raiju + namekuji;
            if (total <= 0f)
            {
                return SpecialWeaponId.Raiju;
            }

            return Mathf.Clamp01(roll01) * total < raiju ? SpecialWeaponId.Raiju : SpecialWeaponId.Namekuji;
        }
    }

    /// <summary>All weapon data in one place.</summary>
    [Serializable]
    public sealed class WeaponConfigSection
    {
        // WP-069 A4 (B1): the classic numbers live in the WeaponDefinition v3 assets (Resources/Arsenal).
        // This list is a per-config COPY built from them on first access (not serialized any more), so the
        // M3 readers keep `Weapons.Find(id)` and a cloned config (EconomySimulator, profile tests) keeps its
        // own clone-scoped numbers. Without a catalog (bare EditMode) the M3 code defaults below apply.
        [NonSerialized] private List<ClassicWeaponSettings> _classicView;

        public List<ClassicWeaponSettings> ClassicWeapons
        {
            get
            {
                if (_classicView == null)
                {
                    _classicView = BuildClassicView();
                }

                return _classicView;
            }
        }

        /// <summary>Drop the copy so the next access rebuilds it from the definitions (tests, F1 reset).</summary>
        public void RebuildClassicView()
        {
            _classicView = null;
        }

        /// <summary>
        /// Review R1-A #19: <c>Object.Instantiate(config)</c> copies serialized data only, so a clone rebuilds this
        /// view from the Arsenal and loses live edits made on the source (F1, a test's tweak). This carries them
        /// over — <see cref="PrototypeConfig.Clone"/> calls it.
        /// </summary>
        public void CopyClassicViewFrom(WeaponConfigSection source)
        {
            if (source == null)
            {
                return;
            }

            List<ClassicWeaponSettings> from = source.ClassicWeapons;
            var copy = new List<ClassicWeaponSettings>(from.Count);
            for (int i = 0; i < from.Count; i++) copy.Add(from[i] != null ? from[i].Clone() : null);
            _classicView = copy;
        }

        private static List<ClassicWeaponSettings> BuildClassicView()
        {
            var view = new List<ClassicWeaponSettings>();
            IReadOnlyList<WeaponDefinition> all = Arsenal.All;
            for (int i = 0; i < all.Count; i++)
            {
                WeaponDefinition d = all[i];
                if (d != null && d.enabled && d.slot == WeaponSlotKind.Classic)
                {
                    view.Add(d.CreateLegacySettings());
                }
            }

            if (view.Count == 0)
            {
                view.AddRange(CodeDefaults());
            }

            return view;
        }

        /// <summary>The M3 A/B/C numbers (DEC-083 + WP-042 / Ö-4) — the fallback without a catalog and the B1 migration source.</summary>
        public static List<ClassicWeaponSettings> CodeDefaults() => new List<ClassicWeaponSettings>
        {
            new ClassicWeaponSettings
            {
                Id = ClassicWeaponId.AssaultRifle,
                DisplayName = "Assault Rifle",
                Damage = 11f,
                FireRate = 8.5f,
                MagazineSize = 30,
                ReloadTime = 1.5f,
                PelletCount = 1,
                SpreadDegrees = 1.4f,
                SpreadBloomPerShot = 0.40f,
                SpreadBloomMax = 3.5f,
                SpreadBloomDecay = 7f,
                RecoilKick = 0.10f,
                CameraShake = 0.30f,
                StaggerImpulse = 0.15f,
                Range = 45f,
                RecoilRecoverySeconds = 0.10f,
                RecoilUpShare = 0.15f,
                RumbleFraction = 0.67f,
                RumbleMilliseconds = 70,
                PelletRingRadiusShare = 0f,
                PelletRingJitterShare = 0f,
                UnlockProfileLevel = 1
            },
            new ClassicWeaponSettings
            {
                Id = ClassicWeaponId.AutoSmg,
                DisplayName = "Auto SMG",
                Damage = 8f,   // WP-042 · Ö-4 (user 2026-09-17): 7 → 8 against armour durability
                FireRate = 13f,
                MagazineSize = 42,
                ReloadTime = 1.7f,
                PelletCount = 1,
                SpreadDegrees = 2.8f,
                SpreadBloomPerShot = 0.55f,
                SpreadBloomMax = 6.5f,
                SpreadBloomDecay = 6f,
                RecoilKick = 0.05f,
                CameraShake = 0.18f,
                StaggerImpulse = 0.08f,
                Range = 38f,
                RecoilRecoverySeconds = 0.055f,
                RecoilUpShare = 0.10f,
                RumbleFraction = 0.5f,
                RumbleMilliseconds = 45,
                PelletRingRadiusShare = 0f,
                PelletRingJitterShare = 0f,
                UnlockProfileLevel = 1   // WP-107 (R4, user 11.10): open from the start (was DEC-085 L3)
            },
            new ClassicWeaponSettings
            {
                Id = ClassicWeaponId.CombatShotgun,
                DisplayName = "Combat Shotgun",
                Damage = 8f,   // WP-042 · Ö-4 (user 2026-09-17): 7 → 8 per pellet
                FireRate = 1.2f,
                MagazineSize = 7,
                ReloadTime = 2.2f,
                PelletCount = 8,
                SpreadDegrees = 7.5f,
                SpreadBloomPerShot = 0f,
                SpreadBloomMax = 0f,
                SpreadBloomDecay = 0f,
                RecoilKick = 0.34f,
                CameraShake = 1.0f,
                StaggerImpulse = 0.85f,
                Range = 22f,
                RecoilRecoverySeconds = 0.30f,
                RecoilUpShare = 0.30f,
                RumbleFraction = 1f,
                RumbleMilliseconds = 120,
                PelletRingRadiusShare = 0.6f,
                PelletRingJitterShare = 0.10f,
                UnlockProfileLevel = 1   // WP-107 (R4, user 11.10): open from the start (was DEC-085 L5)
            }
        };

        public SpecialWeaponConfigSection Special = new SpecialWeaponConfigSection();

        /// <summary>WP-069 B2: the entry of exactly this id, or null (no first-entry fallback — see <see cref="Find"/>).</summary>
        public ClassicWeaponSettings FindExact(ClassicWeaponId id)
        {
            for (int i = 0; i < ClassicWeapons.Count; i++)
            {
                if (ClassicWeapons[i] != null && ClassicWeapons[i].Id == id)
                {
                    return ClassicWeapons[i];
                }
            }

            return null;
        }

        /// <summary>The entry of this id; the first entry when the build does not carry it (M3 fallback, never null with a roster).</summary>
        public ClassicWeaponSettings Find(ClassicWeaponId id)
        {
            for (int i = 0; i < ClassicWeapons.Count; i++)
            {
                if (ClassicWeapons[i] != null && ClassicWeapons[i].Id == id)
                {
                    return ClassicWeapons[i];
                }
            }

            return ClassicWeapons.Count > 0 ? ClassicWeapons[0] : null;
        }
    }
}
