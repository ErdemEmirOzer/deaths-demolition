using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.Weapons
{
    // ======================================================================== WP-069 A4 enums

    public enum WeaponSlotKind { Classic = 0, Special = 1 }

    public enum WeaponClass { AssaultRifle = 0, Smg = 1, Shotgun = 2, Lmg = 3, Sniper = 4, Launcher = 5, Special = 6 }

    public enum WeaponFireMode { Single = 0, Burst = 1, Auto = 2 }

    public enum WeaponPelletPattern { Single = 0, CenterRings = 1, Gauss = 2 }

    public enum WeaponReloadKind { Magazine = 0, PerShell = 1, Box = 2, Single = 3, Cell = 4, Canister = 5 }

    public enum ProjectileKind { Bullet = 0, Pellet = 1, Rocket = 2, Orb = 3, Gel = 4 }

    public enum WeaponCaliber { Light = 0, Medium = 1, Heavy = 2, Explosive = 3 }

    public enum WeaponCasing { None = 0, Rifle = 1, Pistol = 2, Shell = 3, Link = 4, Large = 5 }

    public enum WeaponSilhouetteV3 { Long = 0, LongX = 1, Medium = 2, ShortThick = 3, Heavy = 4, Tube = 5, Special = 6 }

    public enum WeaponUnlockV3 { Default = 0, ProfileLevel = 1, Meta = 2 }

    public enum SpecialLimit { Time = 0, Ammo = 1, AmmoOrTime = 2 }

    public enum ExplosionSemantics { Chance = 0, Blast = 1 }

    public enum RecoilYawPattern { None = 0, Random = 1, Pattern = 2 }

    // ======================================================================== WP-069 A4 blocks

    /// <summary>Half-angles in degrees for one camera mode: hip fire and ADS (top-down: the "focus" key).</summary>
    [Serializable]
    public struct SpreadPair
    {
        public float hip;
        public float ads;

        public SpreadPair(float hip, float ads) { this.hip = hip; this.ads = ads; }
    }

    [Serializable]
    public sealed class PelletRing
    {
        public int count = 3;
        [Range(0f, 1f)] public float radiusShare = 1f;
    }

    [Serializable]
    public sealed class WeaponFireBlock
    {
        public WeaponFireMode fireMode = WeaponFireMode.Auto;
        [Tooltip("Effective rounds per minute (Single: the cycle ceiling).")] public float rpm = 600f;
        public int burstCount = 3;
        public float burstIntervalSeconds = 0.05f;
        public float burstDelaySeconds = 0.26f;
        [Tooltip("Damage per pellet.")] public float damage = 10f;
        public int pelletCount = 1;
        public WeaponPelletPattern pelletPattern = WeaponPelletPattern.Single;
        public PelletRing[] rings = new PelletRing[0];
        public float ringJitterShare;
        public float gaussSigmaShare = 0.45f;
        [Tooltip("Trigger held this long before the shot leaves; releasing early cancels without spending ammo.")] public float chargeSeconds;
        public bool dryFire = true;
    }

    [Serializable]
    public sealed class WeaponAmmoBlock
    {
        public int magazine = 30;
        [Tooltip("Rounds carried for the run beyond the magazine; −1 = infinite (the M3 behaviour).")] public int reserve = -1;
        [Tooltip("Reserve refill at each round start: reserve × (1 + coef × (round − 1)) (council B2: 0.12; rl_1 0.06).")]
        public float reserveRoundCoef = 0.12f;
        [Tooltip("Magazine and reserve both empty: this long later one magazine drips into the reserve (the 'kese' floor — the player is never weaponless).")]
        public float floorRefillDelaySeconds = 4f;
        public int floorRefillMagazines = 1;
        public int chamberExtra;
        public WeaponReloadKind reloadKind = WeaponReloadKind.Magazine;
        [Tooltip("Magazine / Box / Cell / Canister: the whole tactical reload. PerShell: the start + end overhead around the shells.")]
        public float reloadTacSeconds = 1.5f;
        [Tooltip("As reloadTacSeconds with the chamber / pump / bolt step (empty magazine).")]
        public float reloadEmptySeconds = 1.5f;
        [Tooltip("PerShell: seconds per shell inserted.")]
        public float perShellSeconds = 0.55f;
        public bool reloadCancellable;
    }

    [Serializable]
    public sealed class WeaponProjectileBlock
    {
        public ProjectileKind kind = ProjectileKind.Bullet;
        [Tooltip("m/s")] public float speed = 260f;
        [Tooltip("m/s², downward")] public float gravity = 9.81f;
        [Tooltip("m")] public float maxRange = 45f;
        public float maxLifetimeSeconds = 2f;
        [Tooltip("0 = ray sweep; > 0 = sphere sweep (rocket, orb, gel).")] public float sweepRadius;
        [Tooltip("Bodies passed through (the Piercing branch adds on top).")] public int penetration;
        [Range(0f, 1f)] public float penetrationDamageKeep = 0.6f;
        public float falloffStart = 45f;
        public float falloffEnd = 45f;
        [Range(0f, 1f)] public float falloffMinShare = 1f;
        [Tooltip("Impact on the DamageForce scale (was StaggerImpulse).")] public float impulse = 0.15f;
        [Range(0f, 1f)] public float stagger = 0.25f;
        [Tooltip("Extra head multiplier on top of the regional ×2.0 (1 = the rule).")] public float headMultiplier = 1f;
        [Tooltip("Multiplier on the damage dealt to armour durability.")] public float armorDamageMultiplier = 1f;
        public WeaponCaliber caliber = WeaponCaliber.Medium;
    }

    [Serializable]
    public sealed class WeaponAccuracyBlock
    {
        public SpreadPair topDown = new SpreadPair(1.4f, 0.7f);
        public SpreadPair thirdPerson = new SpreadPair(1.8f, 0.5f);
        public SpreadPair firstPerson = new SpreadPair(1.6f, 0.35f);
        [Tooltip("Negative = the settle ('oturma') curve.")] public float bloomPerShot = 0.4f;
        public float bloomMax = 3.5f;
        [Tooltip("Settle curve floor as a share of the base cone (lmg_2: 3.5° → 1.2° = 0.35); 0 = no settle.")]
        [Range(0f, 1f)] public float settleFloorShare;
        public float bloomDecayPerSecond = 6f;
        public float bloomDecayDelaySeconds = 0.1f;
        public float moveSpreadMultiplier = 1f;
        public float adsSeconds = 0.25f;
        public float moveSpeedMultiplier = 1f;
        public float adsMoveSpeedMultiplier = 0.75f;
        public float focusMoveSpeedMultiplier = 0.75f;
        [Header("Game-effect recoil (the aim really moves; the visual kick is FeelConfig)")]
        public float recoilPitchPerShot;
        public RecoilYawPattern recoilYawPattern = RecoilYawPattern.None;
        public float[] recoilYawValues = new float[0];
        public int recoilStableShots;
        public float recoilRecoverySeconds = 0.35f;
        public float recoilAccumulationCap = 4f;
        [Range(0f, 1f)] public float topDownYawShare = 0.6f;
    }

    [Serializable]
    public sealed class WeaponExplosiveBlock
    {
        public bool enabled;
        public float directHitDamage;
        public float blastDamage;
        public float innerRadius;
        public float outerRadius;
        public float falloffExponent = 1f;
        [Tooltip("Head / Upper / Lower; empty = the EnemyConfig explosion model.")] public float[] regionShares = new float[0];
        [Range(0f, 1f)] public float selfDamageShare;
        public float selfDamageMax;
        public float armingDistance;
        public float dudDamage;
        public float dudFizzleSeconds = 1f;
        public float impulse = 1f;
        public float lingerDps;
        public float lingerSeconds;
    }

    [Serializable]
    public sealed class WeaponRocketBlock
    {
        public float launchSpeed;
        public float acceleration;
        public float maxSpeed;
        public float boostSeconds;
        public float gravityBoost;
        public float gravityCoast;
        public float wobbleDegreesPerStep;
        public float wobbleStepSeconds = 0.1f;
        public float selfDetonateSeconds;
        public float smokeTrailSeconds;
    }

    /// <summary>A tier's answer to a Special: kill outright, or a share of max HP plus a stun / knock-down.</summary>
    [Serializable]
    public sealed class SpecialTierRule
    {
        public EnemyTier tier = EnemyTier.Standard;
        public bool kill = true;
        [Tooltip("Kill only up to this round (0 = every round); past it the share applies.")] public int killUpToRound;
        [Range(0f, 1f)] public float maxHpShare;
        public float stunSeconds;
        public float immuneSeconds;
    }

    [Serializable]
    public sealed class WeaponArcChainBlock
    {
        public int maxTargets;
        public float jumpRadius;
        public float jumpDelaySeconds;
        public bool nearestUnhit = true;
        public float shockSeconds;
        public SpecialTierRule[] tierRules = new SpecialTierRule[0];
        public float selfShockRadius;
        public float selfShockDamage;
        public float selfShockFirstRadius;
        public float selfShockFirstDamage;
        public float selfSlowSeconds;
        public float orbFizzleSeconds;
        public bool ignoresArmor = true;
    }

    [Serializable]
    public sealed class WeaponGelPuddleBlock
    {
        public SpecialTierRule[] directHitRules = new SpecialTierRule[0];
        public float puddleRadius;
        public float deathPuddleRadius;
        public float deathPuddleSeconds;
        public float puddleLifetimeSeconds;
        public int maxPuddles;
        public float fadeSeconds = 0.5f;
        public SpecialTierRule[] slipRules = new SpecialTierRule[0];
        public float slipFallSeconds;
        public float playerSlideSpeedMultiplier = 1f;
        public float playerSteerMultiplier = 1f;
        public float slideExitSeconds;
        public float autoArcMaxRange;
        public bool ignoresArmor = true;
    }

    [Serializable]
    public sealed class WeaponSpecialLifecycleBlock
    {
        public SpecialLimit limit = SpecialLimit.AmmoOrTime;
        public float activeCapSeconds;
        public float groundSeconds = 22f;
        public float spawnWeight = 1f;
    }

    [Serializable]
    public sealed class WeaponKeystone
    {
        public string id = "";
        public string displayName = "";
        [TextArea] public string effect = "";
    }

    [Serializable]
    public sealed class WeaponUpgradeBlock
    {
        public UpgradeBranch[] coreBranches = new UpgradeBranch[WeaponBranchContract.CoreBranchesPerWeapon];
        public WeaponKeystone keystone = new WeaponKeystone();
        public ExplosionSemantics explosionSemantics = ExplosionSemantics.Chance;
        public UpgradeBranch[] forbiddenBranches = new UpgradeBranch[0];
    }

    [Serializable]
    public sealed class WeaponUnlockBlock
    {
        public WeaponUnlockV3 kind = WeaponUnlockV3.ProfileLevel;
        public int unlockOrder;
        [Tooltip("DEC-008 profile level while the kind is ProfileLevel (the M3 rule; WP-077 moves unlocks to the meta tree).")] public int profileLevel = 1;
        public int metaCost;
        public Vector2Int shopPriceBand;
    }

    [Serializable]
    public sealed class WeaponSockets
    {
        public string root = "socket_weapon_r";
        public string muzzle = "Muzzle";
        public string ejection = "Ejection";
        public string gripLeft = "grip_l";
        public string sightAds = "sight_ads";
        public string[] attachments = { "attach_barrel", "attach_top", "attach_stock" };
    }

    [Serializable]
    public sealed class WeaponPresentationBlock
    {
        [Tooltip("S-08 sound identity; empty = derived (weaponId upper case).")] public string audioId = "";
        [Tooltip("Looped fire sound (Auto ∧ rpm ≥ 600).")] public bool fireLoop;
        public WeaponCasing casing = WeaponCasing.Rifle;
        [Tooltip("S-06 animation set; empty = derived (weaponId).")] public string animSet = "";
        public string[] mechanismEvents = new string[0];
        public string muzzleVfx = "";
        public string tracerVfx = "";
        public string impactVfx = "";
        public string hudIconId = "";
        public string modelTp = "";
        public string modelFp = "";
        [Tooltip("Until the AP-021 model lands: show the in-hand model / pose of this M3 weapon (class sibling).")]
        public ClassicWeaponId placeholderVisual = ClassicWeaponId.AssaultRifle;
        public WeaponSockets sockets = new WeaponSockets();
    }

    // ======================================================================== the definition

    /// <summary>
    /// WP-069 A4 · WeaponDefinition v3 — one weapon as DATA (one asset per weapon under
    /// <c>Weapons/Arsenal/Resources/Arsenal</c>). The GAME numbers live here (damage, fire,
    /// ammo, ballistics, spread, game-effect recoil, explosives, Special mechanics, upgrades,
    /// unlock); the FEEL numbers (visual kick, per-mode camera reaction, rumble) stay in the
    /// WP-062 FeelConfig rows keyed by the same <see cref="weaponId"/>; art / sound / animation
    /// ASSETS stay with their owners and are referenced here by identity only.
    ///
    /// Runtime access goes through <see cref="Arsenal"/>. The M3 consumers of
    /// <c>PrototypeConfig.Weapons.Find(id)</c> read a per-config copy built from this asset
    /// (<see cref="ClassicWeaponSettings.Definition"/> points back here), so cloned configs
    /// (simulator, profile tests) keep their clone-scoped numbers.
    /// </summary>
    [CreateAssetMenu(fileName = "WD_", menuName = "Deaths Demolition/Weapon Definition v3")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable key for data, saves and telemetry: ar_1 … rl_1, sw_arc, sw_gel.")]
        public string weaponId = "ar_1";
        [Tooltip("Runtime index (ClassicWeaponId value; Specials use their own ids).")]
        public ClassicWeaponId id = ClassicWeaponId.AssaultRifle;
        public WeaponSlotKind slot = WeaponSlotKind.Classic;
        public WeaponClass weaponClass = WeaponClass.AssaultRifle;
        public string displayName = "Assault Rifle";
        [Tooltip("HUD, ≤ 10 characters.")] public string shortName = "";
        [TextArea] public string description = "";
        public int sortOrder;
        public bool enabled = true;
        public WeaponSilhouetteV3 silhouette = WeaponSilhouetteV3.Long;
        [Tooltip("Old ClassicWeaponId names this weapon answers to in saved profiles.")] public string[] legacyNames = new string[0];

        public WeaponFireBlock fire = new WeaponFireBlock();
        public WeaponAmmoBlock ammo = new WeaponAmmoBlock();
        public WeaponProjectileBlock projectile = new WeaponProjectileBlock();
        public WeaponAccuracyBlock accuracy = new WeaponAccuracyBlock();
        public WeaponExplosiveBlock explosive = new WeaponExplosiveBlock();
        public WeaponRocketBlock rocket = new WeaponRocketBlock();
        public WeaponArcChainBlock arcChain = new WeaponArcChainBlock();
        public WeaponGelPuddleBlock gelPuddle = new WeaponGelPuddleBlock();
        public WeaponSpecialLifecycleBlock specialLifecycle = new WeaponSpecialLifecycleBlock();
        public WeaponUpgradeBlock upgrade = new WeaponUpgradeBlock();
        public WeaponUnlockBlock unlock = new WeaponUnlockBlock();
        public WeaponPresentationBlock presentation = new WeaponPresentationBlock();

        // ------------------------------------------------------------------ derived identities

        [NonSerialized] private string _audioIdCache;
        [NonSerialized] private string _audioIdCacheKey;

        /// <summary>
        /// S-08 sound identity: the override, else weaponId upper case with the underscore before a digit
        /// dropped (ar_1 → AR1, lmg_2 → LMG2, sw_arc → SW_ARC). Cached — read on the fire path, no allocation.
        /// </summary>
        public string AudioId
        {
            get
            {
                if (!string.IsNullOrEmpty(presentation.audioId)) return presentation.audioId;
                if (_audioIdCache != null && ReferenceEquals(_audioIdCacheKey, weaponId)) return _audioIdCache;
                _audioIdCacheKey = weaponId;
                _audioIdCache = DeriveAudioId(weaponId);
                return _audioIdCache;
            }
        }

        /// <summary>The derivation rule behind <see cref="AudioId"/> (tests, tools).</summary>
        public static string DeriveAudioId(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var sb = new System.Text.StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c == '_' && i + 1 < key.Length && char.IsDigit(key[i + 1])) continue;
                sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString();
        }

        /// <summary>S-06 animation set (weaponId unless overridden).</summary>
        public string AnimSet => string.IsNullOrEmpty(presentation.animSet) ? weaponId : presentation.animSet;

        /// <summary>S-09 HUD icon key.</summary>
        public string HudIconId => string.IsNullOrEmpty(presentation.hudIconId) ? "Weapon." + weaponId : presentation.hudIconId;

        /// <summary>Shots per second (from <see cref="WeaponFireBlock.rpm"/>).</summary>
        public float ShotsPerSecond => Mathf.Max(0.05f, fire.rpm / 60f);

        /// <summary>Spread pair for a camera mode index (0 TopDown, 1 ThirdPerson, 2 FirstPerson).</summary>
        public SpreadPair SpreadFor(int cameraMode)
        {
            switch (cameraMode)
            {
                case 1: return accuracy.thirdPerson;
                case 2: return accuracy.firstPerson;
                default: return accuracy.topDown;
            }
        }

        /// <summary>
        /// Damage share at <paramref name="travelled"/> metres from the muzzle: 1 up to
        /// <see cref="WeaponProjectileBlock.falloffStart"/>, linear to <see cref="WeaponProjectileBlock.falloffMinShare"/>
        /// at <see cref="WeaponProjectileBlock.falloffEnd"/>, then held.
        /// </summary>
        public static float FalloffShare(WeaponProjectileBlock p, float travelled)
        {
            if (p == null || p.falloffEnd <= p.falloffStart + 1e-4f)
            {
                return 1f;
            }

            float t = Mathf.InverseLerp(p.falloffStart, p.falloffEnd, travelled);
            return Mathf.Lerp(1f, Mathf.Clamp01(p.falloffMinShare), t);
        }

        // ------------------------------------------------------------------ legacy view (M3 consumers)

        /// <summary>
        /// A fresh <see cref="ClassicWeaponSettings"/> carrying this weapon's numbers under the M3
        /// field names, for the consumers that still read <c>PrototypeConfig.Weapons</c>. Feel
        /// fields (kick, shake, rumble) are not game data and keep the ClassicWeaponSettings
        /// defaults — their source is the FeelConfig row.
        /// </summary>
        public ClassicWeaponSettings CreateLegacySettings()
        {
            float ringShare = 0f;
            if (fire.pelletPattern == WeaponPelletPattern.CenterRings && fire.rings != null && fire.rings.Length > 0 && fire.rings[fire.rings.Length - 1] != null)
            {
                ringShare = fire.rings[fire.rings.Length - 1].radiusShare;
            }

            var settings = new ClassicWeaponSettings
            {
                Id = id,
                DisplayName = displayName,
                Damage = fire.damage,
                FireRate = ShotsPerSecond,
                MagazineSize = Mathf.Max(1, ammo.magazine),
                // Per-shell: the M3 figure is a full magazine from empty — overhead + one interval per shell.
                ReloadTime = ammo.reloadKind == WeaponReloadKind.PerShell
                    ? ammo.reloadTacSeconds + Mathf.Max(0f, ammo.perShellSeconds) * Mathf.Max(1, ammo.magazine)
                    : ammo.reloadTacSeconds,
                PelletCount = Mathf.Max(1, fire.pelletCount),
                SpreadDegrees = accuracy.topDown.hip,
                SpreadBloomPerShot = accuracy.bloomPerShot,
                SpreadBloomMax = accuracy.bloomMax,
                SpreadBloomDecay = accuracy.bloomDecayPerSecond,
                StaggerImpulse = projectile.impulse,
                Range = projectile.maxRange,
                PelletRingRadiusShare = ringShare,
                PelletRingJitterShare = fire.ringJitterShare,
                UnlockProfileLevel = unlock.kind == WeaponUnlockV3.Default ? 1 : Mathf.Max(1, unlock.profileLevel),
                Definition = this
            };

            // Feel is not game data: the M3 feel fields mirror this weapon's FeelConfig row (WP-062).
            // WP-069 B2: every classic weapon has a row (the v3 ones borrow their class sibling's until S-07's pass).
            if (slot == WeaponSlotKind.Classic && (int)id >= 0)
            {
                WeaponFeel row = FeelRuntime.Current.Weapon(WeaponFeelIds.From(id));
                settings.RecoilKick = row.recoilKickMeters;
                settings.RecoilRecoverySeconds = row.recoilRecoverySeconds;
                settings.RecoilUpShare = row.recoilUpShare;
                settings.CameraShake = row.shakeWeight;
                settings.RumbleFraction = row.rumbleFraction;
                settings.RumbleMilliseconds = row.rumbleDurationMs;
            }

            return settings;
        }

        /// <summary>WP-069 B1 migration: the unlock block from an M3 settings entry (open by default, or its DEC-008 level).</summary>
        public void SetUnlockFromM3(ClassicWeaponSettings m3, bool openByDefault)
        {
            unlock.kind = openByDefault ? WeaponUnlockV3.Default : WeaponUnlockV3.ProfileLevel;
            unlock.profileLevel = m3 != null ? m3.UnlockProfileLevel : 1;
        }

        // ------------------------------------------------------------------ validation (A4 gate)

        /// <summary>Structural and band validation. Appends one line per fault; true when clean.</summary>
        public bool Validate(List<string> faults)
        {
            int before = faults.Count;
            string tag = "[" + weaponId + "] ";

            if (string.IsNullOrWhiteSpace(weaponId)) faults.Add(tag + "weaponId empty");
            if (string.IsNullOrWhiteSpace(displayName)) faults.Add(tag + "displayName empty");
            if (fire.rpm <= 0f) faults.Add(tag + "rpm must be > 0");
            if (fire.damage < 0f) faults.Add(tag + "damage negative");
            if (fire.pelletCount < 1) faults.Add(tag + "pelletCount < 1");
            if (fire.fireMode == WeaponFireMode.Burst && fire.burstCount < 2) faults.Add(tag + "burst needs burstCount ≥ 2");
            if (ammo.magazine < 1) faults.Add(tag + "magazine < 1");
            if (ammo.reloadEmptySeconds + 1e-4f < ammo.reloadTacSeconds) faults.Add(tag + "reloadEmpty < reloadTac");
            if (projectile.speed <= 0f) faults.Add(tag + "projectile speed must be > 0");
            if (projectile.maxRange <= 0f) faults.Add(tag + "maxRange must be > 0");
            if (projectile.falloffEnd + 1e-4f < projectile.falloffStart) faults.Add(tag + "falloffEnd < falloffStart");
            if (accuracy.topDown.hip < 0f || accuracy.thirdPerson.hip < 0f || accuracy.firstPerson.hip < 0f) faults.Add(tag + "spread negative");
            if (presentation.fireLoop != (fire.fireMode == WeaponFireMode.Auto && fire.rpm >= 600f)) faults.Add(tag + "fireLoop must equal Auto ∧ rpm ≥ 600");

            if (slot == WeaponSlotKind.Classic)
            {
                UpgradeBranch[] core = upgrade.coreBranches;
                if (core == null || core.Length != WeaponBranchContract.CoreBranchesPerWeapon)
                {
                    faults.Add(tag + "coreBranches must have exactly " + WeaponBranchContract.CoreBranchesPerWeapon + " entries");
                }
                else
                {
                    for (int i = 0; i < core.Length; i++)
                    {
                        if (!WeaponBranchContract.IsWeaponCore(core[i])) faults.Add(tag + core[i] + " is not a weapon-core branch");
                        for (int j = i + 1; j < core.Length; j++)
                        {
                            if (core[i] == core[j]) faults.Add(tag + "duplicate core branch " + core[i]);
                        }
                    }
                }
            }

            if (upgrade.forbiddenBranches != null && upgrade.coreBranches != null)
            {
                for (int i = 0; i < upgrade.coreBranches.Length; i++)
                {
                    if (Array.IndexOf(upgrade.forbiddenBranches, upgrade.coreBranches[i]) >= 0) faults.Add(tag + upgrade.coreBranches[i] + " is forbidden for this weapon");
                }
            }

            if (fire.pelletCount > 1 && upgrade.coreBranches != null)
            {
                for (int i = 0; i < upgrade.coreBranches.Length; i++)
                {
                    UpgradeBranch b = upgrade.coreBranches[i];
                    if (b == UpgradeBranch.Chain || b == UpgradeBranch.Ricochet) faults.Add(tag + b + " cannot be a pellet weapon's core branch (ADR-034-1)");
                }
            }

            if (ammo.reserve >= 0 && ammo.floorRefillDelaySeconds <= 0f) faults.Add(tag + "finite reserve needs a floor refill delay");

            if (weaponClass == WeaponClass.Launcher && (!explosive.enabled || rocket.maxSpeed <= 0f))
            {
                faults.Add(tag + "launcher needs explosive + rocket blocks");
            }

            if (slot == WeaponSlotKind.Special && specialLifecycle.activeCapSeconds <= 0f && specialLifecycle.limit != SpecialLimit.Ammo)
            {
                faults.Add(tag + "special needs an active cap");
            }

            return faults.Count == before;
        }
    }
}
