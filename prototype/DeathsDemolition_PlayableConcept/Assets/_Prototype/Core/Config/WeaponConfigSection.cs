using System;
using System.Collections.Generic;
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

        [PrototypeOnly(PendingDecisions.ProfileXp, "Profile level required to unlock. AR=1, SMG=2, Shotgun=3.")]
        public int UnlockProfileLevel = 1;
    }

    /// <summary>Which of the three Special Weapons a drop is.</summary>
    public enum SpecialWeaponId
    {
        /// <summary>Shrine bell-tree. Rings an expanding pressure wave. Verb: sweep.</summary>
        KaguraBell = 0,

        /// <summary>Talisman loom. Seals targets, then collapses the web. Verb: bind.</summary>
        OfudaLoom = 1,

        /// <summary>Ryujin's tide jewel. Opens a gravity well, then implodes it. Verb: gather.</summary>
        TideJewel = 2
    }

    /// <summary>
    /// The knobs every Special Weapon has, whatever its verb is.
    ///
    /// Split out rather than repeated three times so the F1 panel, the HUD and the
    /// stagger contract all read one shape no matter which weapon is in hand.
    /// </summary>
    [Serializable]
    public sealed class SpecialWeaponCommon
    {
        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "In-game name. INVENTED: the request names no Special Weapon; these three are " +
            "prototype content answering 'fantastical, not another gun', not an approved roster.", true)]
        public string DisplayName = "Special Weapon";

        [PrototypeOnly(PendingDecisions.CombatScaling, "Damage of the weapon's primary event.", true)]
        public float Damage = 20f;

        [PrototypeOnly(PendingDecisions.None, "Primary events per second.", true)]
        public float FireRate = 3f;

        [PrototypeOnly(PendingDecisions.None,
            "Reaction force in DamageForce units, where 1 = one rifle bullet. The enemy " +
            "staggers at 2.4 and is physically shoved at 4.5.", true)]
        public float ImpactForce = 2.5f;

        [PrototypeOnly(PendingDecisions.None, "Camera shake weight, 0-1. Clamped again by CameraShakeBridge.", true)]
        public float CameraShake = 0.4f;

        [PrototypeOnly(PendingDecisions.None, "Procedural recoil kick applied to the weapon model.", true)]
        public float RecoilKick = 0.08f;

        [PrototypeOnly(PendingDecisions.None, "Effective reach in metres.", true)]
        public float Range = 30f;

        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "Relative chance this weapon is the one that drops. Equal weights = uniform.", true)]
        public float SpawnWeight = 1f;
    }

    /// <summary>
    /// PROTOTYPE-ONLY Special Weapon 1 of 3: the Kagura Bell.
    ///
    /// A bronze shrine bell-tree. Pulling the trigger RINGS it, and the ring leaves the
    /// player as a visible pressure front that shoves and damages everything it crosses.
    /// There is no aiming: the verb is SWEEP, and it is the answer to being surrounded.
    /// </summary>
    [Serializable]
    public sealed class KaguraBellSettings
    {
        public SpecialWeaponCommon Common = new SpecialWeaponCommon
        {
            DisplayName = "Kagura Bell",
            Damage = 26f,
            FireRate = 2.4f,

            // Above the enemy's 4.5 knockback gate on purpose: the whole point of the
            // bell is that the crowd physically falls back off the player.
            ImpactForce = 5f,

            CameraShake = 0.85f,
            RecoilKick = 0.16f,
            Range = 9.5f,
            SpawnWeight = 1f
        };

        [PrototypeOnly(PendingDecisions.None, "Radius the pressure front reaches before it dies, in metres.", true)]
        public float WaveRadius = 9.5f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds the front takes to reach WaveRadius. 9.5 m in 0.34 s is ~28 m/s: fast " +
            "enough to read as a shockwave, slow enough to see arrive.", true)]
        public float WaveTravelSeconds = 0.34f;

        [PrototypeOnly(PendingDecisions.CombatScaling,
            "Fraction of Damage an enemy standing at the rim takes. Centre takes full.", true)]
        public float FalloffAtRim = 0.55f;
    }

    /// <summary>
    /// PROTOTYPE-ONLY Special Weapon 2 of 3: the Ofuda Loom.
    ///
    /// A lacquered loom that throws paper talismans. A talisman SEALS the enemy it hits.
    /// Seals see each other; once enough of them exist, hitting any sealed enemy collapses
    /// the whole web at once. The verb is BIND: build a net, then spend it.
    /// </summary>
    [Serializable]
    public sealed class OfudaLoomSettings
    {
        public SpecialWeaponCommon Common = new SpecialWeaponCommon
        {
            DisplayName = "Ofuda Loom",
            Damage = 9f,
            FireRate = 5f,

            // A sheet of paper. Deliberately below the 2.4 stagger gate: the dart itself
            // must never be the reason an enemy stops walking, or the build phase would
            // feel like the payoff.
            ImpactForce = 0.35f,

            CameraShake = 0.12f,
            RecoilKick = 0.03f,
            Range = 38f,
            SpawnWeight = 1f
        };

        [PrototypeOnly(PendingDecisions.None, "Seconds a seal survives on an enemy before it burns out.", true)]
        public float SealSeconds = 6f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds before a fresh seal can take part in a collapse. Stops a 5/s trigger " +
            "from detonating the seal it just applied.", true)]
        public float SealArmSeconds = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Most seals that may exist at once. Also the ceiling on collapse links drawn " +
            "in one frame, which is why it is a readability number and not only a balance one.", true)]
        public int MaxSeals = 12;

        [PrototypeOnly(PendingDecisions.None,
            "Below this many armed seals a hit on a sealed enemy refreshes instead of " +
            "collapsing. Without it every accidental double-tap wastes the web.", true)]
        public int MinSealsToCollapse = 3;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Damage every sealed enemy takes when the web collapses.", true)]
        public float CollapseDamage = 34f;

        [PrototypeOnly(PendingDecisions.None,
            "Reaction force of a collapse, in DamageForce units. Above the 2.4 stagger " +
            "gate, below the 4.5 shove gate: the web stops the crowd, it does not scatter it.", true)]
        public float CollapseImpactForce = 2.6f;
    }

    /// <summary>
    /// PROTOTYPE-ONLY Special Weapon 3 of 3: the Tide Jewel.
    ///
    /// Ryujin's tide-jewel in a lacquer cradle. Throwing it opens a well that drags
    /// everything nearby inward, then implodes. The verb is GATHER: it does not kill the
    /// crowd so much as collect it and then delete the pile.
    /// </summary>
    [Serializable]
    public sealed class TideJewelSettings
    {
        public SpecialWeaponCommon Common = new SpecialWeaponCommon
        {
            DisplayName = "Tide Jewel",
            Damage = 6f,
            FireRate = 1.1f,

            // The inward tug. Above the 4.5 shove gate, because a pull that does not
            // move anybody is just a damage-over-time field with a fancy name.
            ImpactForce = 5.2f,

            CameraShake = 0.3f,
            RecoilKick = 0.08f,
            Range = 22f,
            SpawnWeight = 1f
        };

        [PrototypeOnly(PendingDecisions.None, "How far the jewel can be placed from the player, in metres.", true)]
        public float ThrowRange = 22f;

        [PrototypeOnly(PendingDecisions.None, "Radius the well reaches, in metres.", true)]
        public float WellRadius = 7f;

        [PrototypeOnly(PendingDecisions.None, "Seconds the well pulls before it implodes.", true)]
        public float WellSeconds = 2.2f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds between pulls. 0.6 s against the enemy's 1.1 s stagger cooldown means " +
            "roughly every second pull actually moves a given body, which is what stops " +
            "the well from being a stun-lock (REQ-124).", true)]
        public float PullInterval = 0.6f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Damage of one pull tick.", true)]
        public float PullDamage = 6f;

        [PrototypeOnly(PendingDecisions.CombatScaling, "Damage of the implosion at the centre.", true)]
        public float ImplosionDamage = 90f;

        [PrototypeOnly(PendingDecisions.None,
            "Most wells alive at once. This is the crowd-budget knob for this weapon: " +
            "wells x radius bounds how many hit events per second it can produce.", true)]
        public int MaxWells = 2;
    }

    /// <summary>
    /// PROTOTYPE-ONLY Special Weapon rules shared by all three weapons: when one appears,
    /// how long it waits on the ground, how long it lasts in hand.
    ///
    /// Everything here is the DROP, not the weapon. The weapons themselves are the three
    /// settings objects below.
    /// </summary>
    [Serializable]
    public sealed class SpecialWeaponConfigSection
    {
        public KaguraBellSettings KaguraBell = new KaguraBellSettings();

        public OfudaLoomSettings OfudaLoom = new OfudaLoomSettings();

        public TideJewelSettings TideJewel = new TideJewelSettings();

        [PrototypeOnly(PendingDecisions.SpecialWeapon, "Request value: 11 s active duration")]
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

        /// <summary>The shared knobs of one weapon. Never null for a defined id.</summary>
        public SpecialWeaponCommon Common(SpecialWeaponId id)
        {
            switch (id)
            {
                case SpecialWeaponId.OfudaLoom: return OfudaLoom.Common;
                case SpecialWeaponId.TideJewel: return TideJewel.Common;
                default: return KaguraBell.Common;
            }
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
        /// Roll which of the three drops, weighted by SpawnWeight. Falls back to the bell
        /// rather than to nothing when every weight has been zeroed in the F1 panel.
        /// </summary>
        public SpecialWeaponId RollWeapon(float roll01)
        {
            float bell = SpawnWeight(SpecialWeaponId.KaguraBell);
            float loom = SpawnWeight(SpecialWeaponId.OfudaLoom);
            float jewel = SpawnWeight(SpecialWeaponId.TideJewel);

            float total = bell + loom + jewel;
            if (total <= 0f)
            {
                return SpecialWeaponId.KaguraBell;
            }

            float pick = Mathf.Clamp01(roll01) * total;

            if (pick < bell)
            {
                return SpecialWeaponId.KaguraBell;
            }

            return pick < bell + loom ? SpecialWeaponId.OfudaLoom : SpecialWeaponId.TideJewel;
        }
    }

    /// <summary>All weapon data in one place.</summary>
    [Serializable]
    public sealed class WeaponConfigSection
    {
        public List<ClassicWeaponSettings> ClassicWeapons = new List<ClassicWeaponSettings>
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
                UnlockProfileLevel = 3
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
                UnlockProfileLevel = 5
            }
        };

        public SpecialWeaponConfigSection Special = new SpecialWeaponConfigSection();

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
