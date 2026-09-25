using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Weapons.EditorTools
{
    /// <summary>
    /// WP-069 · writes the WeaponDefinition v3 assets and the ArsenalCatalog
    /// (Assets/_Prototype/Weapons/Arsenal/Resources/Arsenal). Deterministic and idempotent:
    /// an existing asset is overwritten in place (GUID kept).
    ///
    /// B2: the ten classic weapons of the Arsenal v3 identity sheet (WP-069-ARSENAL-V3-DIRECTION
    /// §0 / §1 §2–§6, council numbers — PROTOTYPE-ONLY, DEC pending). A/B/C migrate in place
    /// (ar_1 / smg_1 / sg_1 keep their assets and runtime ids 0/1/2); rl_1 is written disabled
    /// until B3 brings the rocket and the blast. Sibling models stand in until AP-021 lands.
    ///
    /// Batch: -executeMethod DD.Prototype.Weapons.EditorTools.ArsenalAssetBuilder.Build
    /// </summary>
    public static class ArsenalAssetBuilder
    {
        public const string Folder = "Assets/_Prototype/Weapons/Arsenal/Resources/Arsenal";

        /// <summary>B1 entry point kept for the batch scripts; builds the current (B2) arsenal.</summary>
        public static void BuildB1() => Build();

        [MenuItem("Deaths Demolition/Weapons/Build Arsenal (WP-069)")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);

            var defs = new List<WeaponDefinition>
            {
                Write(Ar1()), Write(Smg1()), Write(Sg1()),
                Write(Ar2()), Write(Lmg1()), Write(Sr1()), Write(Smg2()), Write(Sg2()), Write(Lmg2()), Write(Rl1()),
                Write(SwArc()), Write(SwGel())
            };

            string catalogPath = Folder + "/ArsenalCatalog.asset";
            ArsenalCatalog catalog = AssetDatabase.LoadAssetAtPath<ArsenalCatalog>(catalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ArsenalCatalog>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }

            catalog.weapons = defs;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var faults = new List<string>();
            bool ok = catalog.Validate(faults);
            Debug.Log("[WP-069] Arsenal built: " + defs.Count + " definitions, valid=" + ok + (ok ? "" : "\n  " + string.Join("\n  ", faults)));
        }

        // ================================================================== the ten (DIRECTION §1 §3–§6)

        private static WeaponDefinition Ar1()
        {
            WeaponDefinition d = New("ar_1", ClassicWeaponId.AssaultRifle, WeaponClass.AssaultRifle, "Hinawa", WeaponSilhouetteV3.Long, 0,
                "Güvenilir, ritmik; her tetikte küçük bir sarsıntı.", "AssaultRifle");
            Fire(d, WeaponFireMode.Auto, 600f, 12f);
            Ammo(d, 30, 210, 1.6f, 2.1f, WeaponReloadKind.Magazine);
            // Impulse 0.13 (council 0.15): at 600 RPM the M3 0.15 would stagger a running Standard under sustained fire
            // (REQ-123: accumulator 1/(1 − 0.5^(0.1/0.12)) × 1.15 = 2.62 > 2.4). 0.13 → 2.27 running, 1.98 walking,
            // 2.67 while telegraphing (the interrupt stays) — HitReactionContractTests.
            Ballistics(d, 260f, 60f, 30f, 0.70f, 1, 0.60f, 0.13f, 0.25f, 1f, 1f, WeaponCaliber.Medium);
            Spread(d, 1.4f, 0.7f, 1.8f, 0.5f, 1.6f, 0.35f, 0.40f, 3.5f, 6f, 1.5f, 0.92f, 0.25f);
            Recoil(d, 0.55f, RecoilYawPattern.Pattern, new[] { 0.25f, 0.25f, -0.25f, -0.25f, 0.25f, 0.25f }, 6, 0.35f);
            Core(d, UpgradeBranch.Piercing, UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed, "kodo", "Kōdō", "6 atış sonrası bloom −%50");
            d.SetUnlockOpen(0);
            Look(d, WeaponCasing.Rifle, ClassicWeaponId.AssaultRifle, "weapon_rifle");
            return d;
        }

        private static WeaponDefinition Ar2()
        {
            WeaponDefinition d = New("ar_2", ClassicWeaponId.Ar2, WeaponClass.AssaultRifle, "Sanren", WeaponSilhouetteV3.Long, 3,
                "Üç mermilik keskin tokat; disiplinli oyuncuya kafa ödülü.", null);
            Fire(d, WeaponFireMode.Burst, 500f, 16f);
            d.fire.burstCount = 3;
            d.fire.burstIntervalSeconds = 0.05f;
            d.fire.burstDelaySeconds = 0.26f;
            Ammo(d, 18, 144, 1.8f, 2.3f, WeaponReloadKind.Magazine);   // DEC-099: 24 / 168 → 18 / 144 (ar_1 leads sustained DPS and ammo)
            Ballistics(d, 320f, 70f, 40f, 0.75f, 1, 0.65f, 0.20f, 0.30f, 1.15f, 1.2f, WeaponCaliber.Medium);
            Spread(d, 1.0f, 0.45f, 1.3f, 0.3f, 1.15f, 0.2f, 0.30f, 2.8f, 7f, 1.6f, 0.92f, 0.28f);
            Recoil(d, 0.70f, RecoilYawPattern.Pattern, new[] { 0.15f }, 3, 0.30f);
            Core(d, UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.FireRate, "kubi", "Kubi", "kafa ek çarpanı ×1,15 → ×1,5");
            d.SetUnlockByMeta(3, 3);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.Rifle, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        private static WeaponDefinition Smg1()
        {
            WeaponDefinition d = New("smg_1", ClassicWeaponId.AutoSmg, WeaponClass.Smg, "Shippu", WeaponSilhouetteV3.ShortThick, 1,
                "Kesintisiz vızıltı, kalçadan koşarak; mermi yağmuru.", "AutoSmg");
            Fire(d, WeaponFireMode.Auto, 900f, 8f);
            Ammo(d, 42, 294, 1.5f, 2.0f, WeaponReloadKind.Magazine);
            Ballistics(d, 170f, 40f, 14f, 0.55f, 0, 1f, 0.08f, 0.12f, 1f, 0.7f, WeaponCaliber.Light);
            Spread(d, 2.8f, 1.6f, 3.5f, 1.1f, 3.2f, 0.8f, 0.55f, 6.5f, 9f, 1.15f, 1.00f, 0.18f);
            Recoil(d, 0.30f, RecoilYawPattern.Random, new[] { 0.45f }, 0, 0.25f);
            Core(d, UpgradeBranch.Chain, UpgradeBranch.Ricochet, UpgradeBranch.FireRate, "kaze", "Kaze", "ateş ederken hareket ×1,10");
            d.SetUnlockByLadder(1, 3, 1);   // DEC-085 Ö-3 ladder level; meta band 1 for WP-077
            Look(d, WeaponCasing.Pistol, ClassicWeaponId.AutoSmg, "weapon_carbine");
            return d;
        }

        private static WeaponDefinition Smg2()
        {
            WeaponDefinition d = New("smg_2", ClassicWeaponId.Smg2, WeaponClass.Smg, "Tetsui", WeaponSilhouetteV3.Medium, 6,
                "Ağır tabanca mermisi; her vuruş zombiyi geri iter.", null);
            Fire(d, WeaponFireMode.Auto, 540f, 14f);
            Ammo(d, 24, 192, 1.7f, 2.2f, WeaponReloadKind.Magazine);
            Ballistics(d, 200f, 42f, 18f, 0.60f, 1, 0.50f, 0.45f, 0.45f, 1f, 0.9f, WeaponCaliber.Medium);
            Spread(d, 2.2f, 1.2f, 2.8f, 0.85f, 2.5f, 0.6f, 0.65f, 5.5f, 8f, 1.3f, 0.96f, 0.20f);
            Recoil(d, 0.75f, RecoilYawPattern.Pattern, new[] { 0.40f, -0.40f }, 8, 0.30f);
            Core(d, UpgradeBranch.Impact, UpgradeBranch.Chain, UpgradeBranch.FireRate, "tsuchi", "Tsuchi", "stagger 0,45 → 0,7, Runner düşer");
            d.SetUnlockByMeta(6, 6);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.Pistol, ClassicWeaponId.AutoSmg, "");
            return d;
        }

        private static WeaponDefinition Sg1()
        {
            WeaponDefinition d = New("sg_1", ClassicWeaponId.CombatShotgun, WeaponClass.Shotgun, "Rappa", WeaponSilhouetteV3.ShortThick, 2,
                "Çan ağızdan dolu bir avuç; pompalayınca dünya sarsılır.", "CombatShotgun");
            Fire(d, WeaponFireMode.Single, 72f, 9f);
            d.fire.pelletCount = 8;
            d.fire.pelletPattern = WeaponPelletPattern.CenterRings;
            // DEC-099: a tight pattern — sg_1 is the shotgun that reaches (rings 0.40 / 1.00 → 0.25 / 0.55 of the cone).
            d.fire.rings = new[] { new PelletRing { count = 3, radiusShare = 0.25f }, new PelletRing { count = 4, radiusShare = 0.55f } };
            d.fire.ringJitterShare = 0.10f;
            // PerShell: tac = start 0,40 + end 0,35 around 0,55 s per shell; empty adds the 0,5 s pump.
            Ammo(d, 7, 42, 0.75f, 1.25f, WeaponReloadKind.PerShell);
            d.ammo.perShellSeconds = 0.55f;
            d.ammo.reloadCancellable = true;
            Ballistics(d, 120f, 24f, 6f, 0.35f, 0, 1f, 0.85f, 0.60f, 1f, 0.8f, WeaponCaliber.Heavy);
            d.projectile.kind = ProjectileKind.Pellet;
            Spread(d, 7.5f, 5.5f, 8.5f, 4.0f, 8.0f, 3.5f, 1.0f, 10f, 10f, 1.2f, 0.90f, 0.30f);
            Recoil(d, 2.4f, RecoilYawPattern.Random, new[] { 0.5f }, 0, 0.55f);
            Core(d, UpgradeBranch.Explosion, UpgradeBranch.Piercing, UpgradeBranch.ReloadSpeed, "manin", "Manin", "pellet 8 → 10");
            d.SetUnlockByLadder(2, 5, 2);   // DEC-085 Ö-3
            Look(d, WeaponCasing.Shell, ClassicWeaponId.CombatShotgun, "weapon_shotgun");
            return d;
        }

        private static WeaponDefinition Sg2()
        {
            WeaponDefinition d = New("sg_2", ClassicWeaponId.Sg2, WeaponClass.Shotgun, "Nito", WeaponSilhouetteV3.Medium, 7,
                "İki namlu, iki gürleme, sonra kırıp doldur; kumar hissi.", null);
            Fire(d, WeaponFireMode.Single, 200f, 11f);
            d.fire.pelletCount = 10;
            d.fire.pelletPattern = WeaponPelletPattern.Gauss;
            d.fire.gaussSigmaShare = 0.70f;   // DEC-099: 0.45 → 0.70, sg_2 is the close-range spray
            Ammo(d, 2, 24, 3.0f, 3.0f, WeaponReloadKind.Magazine);   // DEC-099: reserve 40 → 24, break-open reload 1.7 → 3.0 s
            Ballistics(d, 130f, 20f, 8f, 0.40f, 0, 1f, 0.90f, 0.70f, 1f, 0.8f, WeaponCaliber.Heavy);
            d.projectile.kind = ProjectileKind.Pellet;
            Spread(d, 5.0f, 3.8f, 6.0f, 2.7f, 5.5f, 2.4f, 1.5f, 8f, 10f, 1.2f, 0.92f, 0.28f);
            Recoil(d, 3.0f, RecoilYawPattern.Random, new[] { 0.6f }, 2, 0.60f);
            Core(d, UpgradeBranch.Explosion, UpgradeBranch.Impact, UpgradeBranch.ReloadSpeed, "sohatsu", "Sōhatsu", "iki namlu tek tetikte (20 pellet, kick 5°)");
            d.SetUnlockByMeta(7, 7);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.Shell, ClassicWeaponId.CombatShotgun, "");
            return d;
        }

        private static WeaponDefinition Lmg1()
        {
            WeaponDefinition d = New("lmg_1", ClassicWeaponId.Lmg1, WeaponClass.Lmg, "Mukade", WeaponSilhouetteV3.Heavy, 4,
                "Bitmeyen kutu-şerit, ağır ama sürekli; yere basan ayak.", null);
            Fire(d, WeaponFireMode.Auto, 720f, 11f);
            Ammo(d, 100, 300, 4.4f, 5.2f, WeaponReloadKind.Box);
            d.ammo.floorRefillDelaySeconds = 8f;
            Ballistics(d, 250f, 55f, 28f, 0.65f, 1, 0.60f, 0.15f, 0.20f, 1f, 1f, WeaponCaliber.Medium);
            Spread(d, 2.4f, 1.4f, 3.0f, 1.0f, 2.8f, 0.7f, 0.25f, 4.5f, 5f, 1.9f, 0.80f, 0.45f);
            Recoil(d, 0.45f, RecoilYawPattern.Random, new[] { 0.5f }, 0, 0.50f);
            Core(d, UpgradeBranch.Piercing, UpgradeBranch.Chain, UpgradeBranch.ReloadSpeed, "nagaya", "Nagaya", "şarjör 100 → 150");
            d.SetUnlockByMeta(4, 4);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.Link, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        private static WeaponDefinition Lmg2()
        {
            WeaponDefinition d = New("lmg_2", ClassicWeaponId.Lmg2, WeaponClass.Lmg, "Kurogane", WeaponSilhouetteV3.Heavy, 8,
                "İlk atışlar dağınık, namlu oturunca çelik gibi; her mermi iki gövde.", null);
            Fire(d, WeaponFireMode.Auto, 420f, 24f);
            Ammo(d, 60, 90, 5.0f, 5.8f, WeaponReloadKind.Magazine);   // DEC-099: reserve 180 → 90 (lmg_1 carries more per round)
            d.ammo.floorRefillDelaySeconds = 8f;
            Ballistics(d, 300f, 60f, 32f, 0.70f, 2, 0.70f, 0.50f, 0.55f, 1f, 1.3f, WeaponCaliber.Heavy);
            // "Oturma": the cone starts wide and settles to 35 % of the base under sustained fire; reopens at 4°/s on release.
            Spread(d, 3.5f, 2.2f, 4.4f, 1.5f, 4.0f, 1.1f, -0.40f, 0f, 4f, 2.2f, 0.70f, 0.55f);
            d.accuracy.settleFloorShare = 0.35f;
            Recoil(d, 0.90f, RecoilYawPattern.Pattern, new[] { -0.35f, 0.35f }, 6, 0.60f);
            Core(d, UpgradeBranch.Piercing, UpgradeBranch.ArmorBreak, UpgradeBranch.ReloadSpeed, "ishizue", "Ishizue", "oturma 6 → 3 atış, taban 1,2° → 0,9°");
            d.SetUnlockByMeta(8, 8);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.Rifle, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        private static WeaponDefinition Sr1()
        {
            WeaponDefinition d = New("sr_1", ClassicWeaponId.Sr1, WeaponClass.Sniper, "Tomi", WeaponSilhouetteV3.LongX, 5,
                "Bir nefes, bir sürgü, dört gövde; sessizlik sonra tokat.", null);
            Fire(d, WeaponFireMode.Single, 48f, 55f);
            Ammo(d, 5, 40, 2.6f, 3.2f, WeaponReloadKind.Magazine);
            Ballistics(d, 420f, 90f, 60f, 0.85f, 4, 0.90f, 0.80f, 0.80f, 1.25f, 1.6f, WeaponCaliber.Heavy);   // DEC-099: pierce keep 0.80 → 0.90
            Spread(d, 3.0f, 0.15f, 3.8f, 0.10f, 3.5f, 0.08f, 2.0f, 6f, 8f, 2.5f, 0.85f, 0.40f);
            Recoil(d, 4.5f, RecoilYawPattern.Random, new[] { 0.3f }, 1, 0.90f);
            // DEC-099 (S-04 D2): ArmorBreak → ReloadSpeed — ×1.6 armour only mattered from the 3rd body on, the branch from the 5th.
            Core(d, UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.ReloadSpeed, "kabuto_wari", "Kabuto-wari", "kafa isabeti zırhı yok sayar");
            d.upgrade.forbiddenBranches = new[] { UpgradeBranch.Chain };
            d.SetUnlockByMeta(5, 5);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.Large, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        private static WeaponDefinition Rl1()
        {
            WeaponDefinition d = New("rl_1", ClassicWeaponId.Rl1, WeaponClass.Launcher, "Bohiya", WeaponSilhouetteV3.Tube, 9,
                "Tüpten ateş oku fırlar, hızlanır, duman bırakır; patlama yeri sarsar.", null);
            d.enabled = true;    // WP-069 B3: the rocket flight and the blast are in (RocketImpacts)
            Fire(d, WeaponFireMode.Single, 20f, 110f);
            Ammo(d, 1, 8, 2.8f, 2.8f, WeaponReloadKind.Single);
            d.ammo.reserveRoundCoef = 0.06f;
            d.ammo.floorRefillDelaySeconds = 6f;
            Ballistics(d, 24f, 70f, 70f, 1f, 0, 1f, 1.0f, 1.0f, 1f, 1f, WeaponCaliber.Explosive);
            d.projectile.kind = ProjectileKind.Rocket;
            d.projectile.sweepRadius = 0.12f;
            d.projectile.maxLifetimeSeconds = 2.2f;
            Spread(d, 1.5f, 0.6f, 1.9f, 0.4f, 1.7f, 0.3f, 0f, 0f, 0f, 1.4f, 0.78f, 0.45f);
            Recoil(d, 6.0f, RecoilYawPattern.None, new float[0], 1, 1.0f);
            d.explosive.enabled = true;
            d.explosive.directHitDamage = 110f;
            d.explosive.blastDamage = 160f;
            d.explosive.innerRadius = 1.8f;
            d.explosive.outerRadius = 5.0f;
            d.explosive.falloffExponent = 1f;
            d.explosive.selfDamageShare = 0.45f;
            d.explosive.selfDamageMax = 72f;
            d.explosive.armingDistance = 4.0f;
            d.explosive.dudDamage = 30f;
            d.explosive.dudFizzleSeconds = 1.0f;
            d.explosive.impulse = 1.0f;
            d.rocket.launchSpeed = 24f;
            d.rocket.acceleration = 60f;
            d.rocket.maxSpeed = 46f;
            d.rocket.boostSeconds = 0.6f;
            d.rocket.gravityBoost = 3f;
            d.rocket.gravityCoast = 6f;
            d.rocket.wobbleDegreesPerStep = 0.3f;
            d.rocket.wobbleStepSeconds = 0.1f;
            d.rocket.selfDetonateSeconds = 2.2f;
            d.rocket.smokeTrailSeconds = 1.8f;
            // Chain / Ricochet never on rl_1 (per-shot branches on a blast).
            Core(d, UpgradeBranch.Explosion, UpgradeBranch.Cluster, UpgradeBranch.ReloadSpeed, "hibana", "Hibana", "patlama alanında 3 s yanan zemin (4 HP/s)");
            d.upgrade.explosionSemantics = ExplosionSemantics.Blast;
            d.upgrade.forbiddenBranches = new[] { UpgradeBranch.Chain, UpgradeBranch.Ricochet };
            d.SetUnlockByMeta(9, 10);   // DEC-095 meta tree (S-04 WP-077), band per DIRECTION §6
            Look(d, WeaponCasing.None, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        // ================================================================== the two Specials (DIRECTION §7a / §7b, DEC-094 / DEC-097)

        private static WeaponDefinition SwArc()
        {
            WeaponDefinition d = New("sw_arc", (ClassicWeaponId)100, WeaponClass.Special, "Raiju", WeaponSilhouetteV3.Special, 100,
                "Küre hedefe çarpar, kalabalık titreyerek çöker.", null);
            d.slot = WeaponSlotKind.Special;
            // single; charge 0.25 s + cycle 0.9 s (≈ 52 RPM ceiling); releasing during the charge cancels without spending ammo.
            Fire(d, WeaponFireMode.Single, 60f / 0.9f, 0f);
            d.fire.chargeSeconds = 0.25f;
            Ammo(d, 3, 9, 3.0f, 3.6f, WeaponReloadKind.Cell);
            d.ammo.reserveRoundCoef = 0f;
            d.ammo.floorRefillDelaySeconds = 0f;
            d.ammo.floorRefillMagazines = 0;
            d.ammo.reloadCancellable = false;   // the trigger does not cut the cell swap
            Ballistics(d, 14f, 42f, 42f, 1f, 0, 1f, 0.25f, 0f, 1f, 1f, WeaponCaliber.Explosive);
            d.projectile.kind = ProjectileKind.Orb;
            d.projectile.maxLifetimeSeconds = 3f;
            d.projectile.sweepRadius = 0.35f;
            Spread(d, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 0.40f);
            Recoil(d, 5f, RecoilYawPattern.None, new float[0], 1, 0.40f);
            WeaponArcChainBlock c = d.arcChain;
            c.maxTargets = 8;
            c.jumpRadius = 6f;
            c.jumpDelaySeconds = 0.08f;
            c.nearestUnhit = true;
            c.shockSeconds = 1.2f;
            c.tierRules = new[]
            {
                Rule(EnemyTier.Standard, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Special, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Elite, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Ultra, false, 0, 0.60f, 2.5f, 0f)   // 🔴 #2 default: 60 % + 2.5 s, the chain continues
            };
            c.selfShockRadius = 2.5f;
            c.selfShockDamage = 25f;
            c.selfShockFirstRadius = 1.5f;
            c.selfShockFirstDamage = 40f;
            c.selfSlowSeconds = 0.4f;
            c.orbFizzleSeconds = 0.3f;
            c.ignoresArmor = true;
            Lifecycle(d, 40f);
            Look(d, WeaponCasing.None, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        private static WeaponDefinition SwGel()
        {
            WeaponDefinition d = New("sw_gel", (ClassicWeaponId)101, WeaponClass.Special, "Namekuji", WeaponSilhouetteV3.Special, 101,
                "Mor jel yay çizer, yer kayar, zombiler devrilir.", null);
            d.slot = WeaponSlotKind.Special;
            Fire(d, WeaponFireMode.Single, 60f / 0.45f, 0f);   // cycle 0.45 s (≈ 133 RPM ceiling)
            Ammo(d, 8, 16, 2.4f, 2.8f, WeaponReloadKind.Canister);
            d.ammo.reserveRoundCoef = 0f;
            d.ammo.floorRefillDelaySeconds = 0f;
            d.ammo.floorRefillMagazines = 0;
            d.ammo.reloadCancellable = false;
            Ballistics(d, 22f, 40f, 40f, 1f, 0, 1f, 0f, 0f, 1f, 1f, WeaponCaliber.Light);
            d.projectile.kind = ProjectileKind.Gel;
            d.projectile.gravity = 12f;   // the arc (12° up ≈ 20 m); top-down aims the arc at the cursor (≤ 20 m)
            d.projectile.maxLifetimeSeconds = 3f;
            d.projectile.sweepRadius = 0.25f;
            Spread(d, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 0.20f);
            Recoil(d, 3f, RecoilYawPattern.None, new float[0], 1, 0.20f);
            WeaponGelPuddleBlock g = d.gelPuddle;
            g.directHitRules = new[]
            {
                Rule(EnemyTier.Standard, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Special, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Elite, true, 10, 0.60f, 1.5f, 0f),   // R ≤ 10 dies; later 60 % + 1.5 s knockdown
                Rule(EnemyTier.Ultra, false, 0, 0.25f, 1.5f, 0f)
            };
            g.puddleRadius = 1.6f;
            g.deathPuddleRadius = 0.9f;
            g.deathPuddleSeconds = 8f;
            g.puddleLifetimeSeconds = 12f;   // DEC-099: 25 → 12 s
            g.maxPuddles = 8;                // DEC-099: 12 → 8
            g.fadeSeconds = 0.5f;
            g.slipRules = new[]
            {
                Rule(EnemyTier.Standard, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Special, true, 0, 0f, 0f, 0f),
                Rule(EnemyTier.Elite, true, 10, 0.60f, 1.5f, 0f),   // DEC-099: R ≤ 10 dies; later 60 % (as the direct hit)
                Rule(EnemyTier.Ultra, false, 0, 0.20f, 0f, 3f)   // falls, 20 %, 3 s immune after getting up
            };
            g.slipFallSeconds = 0.9f;
            g.playerSlideSpeedMultiplier = 1.8f;
            g.playerSteerMultiplier = 0.35f;
            g.slideExitSeconds = 0.3f;
            g.autoArcMaxRange = 20f;
            g.ignoresArmor = true;
            Lifecycle(d, 45f);
            Look(d, WeaponCasing.None, ClassicWeaponId.AssaultRifle, "");
            return d;
        }

        private static SpecialTierRule Rule(EnemyTier tier, bool kill, int killUpToRound, float maxHpShare, float stunSeconds, float immuneSeconds)
        {
            return new SpecialTierRule { tier = tier, kill = kill, killUpToRound = killUpToRound, maxHpShare = maxHpShare, stunSeconds = stunSeconds, immuneSeconds = immuneSeconds };
        }

        private static void Lifecycle(WeaponDefinition d, float capSeconds)
        {
            d.specialLifecycle.limit = SpecialLimit.AmmoOrTime;
            d.specialLifecycle.activeCapSeconds = capSeconds;
            d.specialLifecycle.groundSeconds = 22f;
            d.specialLifecycle.spawnWeight = 1f;
        }

        // ================================================================== block helpers

        private static WeaponDefinition New(string key, ClassicWeaponId id, WeaponClass cls, string displayName, WeaponSilhouetteV3 silhouette,
            int sort, string description, string legacyName)
        {
            WeaponDefinition d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.name = "WD_" + key;
            d.weaponId = key;
            d.id = id;
            d.slot = WeaponSlotKind.Classic;
            d.weaponClass = cls;
            d.displayName = displayName;
            d.shortName = displayName.Length <= 10 ? displayName : displayName.Substring(0, 10);
            d.description = description;
            d.sortOrder = sort;
            d.enabled = true;
            d.silhouette = silhouette;
            d.legacyNames = string.IsNullOrEmpty(legacyName) ? new string[0] : new[] { legacyName };
            return d;
        }

        private static void Fire(WeaponDefinition d, WeaponFireMode mode, float rpm, float damage)
        {
            d.fire.fireMode = mode;
            d.fire.rpm = rpm;
            d.fire.damage = damage;
            d.fire.pelletCount = 1;
            d.fire.pelletPattern = WeaponPelletPattern.Single;
            d.presentation.fireLoop = mode == WeaponFireMode.Auto && rpm >= 600f;
        }

        private static void Ammo(WeaponDefinition d, int magazine, int reserve, float tac, float empty, WeaponReloadKind kind)
        {
            d.ammo.magazine = magazine;
            d.ammo.reserve = reserve;
            d.ammo.reloadTacSeconds = tac;
            d.ammo.reloadEmptySeconds = empty;
            d.ammo.reloadKind = kind;
            d.ammo.reserveRoundCoef = 0.12f;
            d.ammo.floorRefillDelaySeconds = 4f;
            d.ammo.floorRefillMagazines = 1;
        }

        private static void Ballistics(WeaponDefinition d, float speed, float maxRange, float fullDamageTo, float minShare, int penetration,
            float keep, float impulse, float stagger, float head, float armor, WeaponCaliber caliber)
        {
            WeaponProjectileBlock p = d.projectile;
            p.kind = ProjectileKind.Bullet;
            p.speed = speed;
            // Review R1-A #15: no drop on the damage sweep for bullets / pellets (WP-042 hit-region parity at every
            // range; §A1 called the drop cosmetic). A trail may still draw one. Rockets bring their own gravity (B3).
            p.gravity = 0f;
            p.maxRange = maxRange;
            p.maxLifetimeSeconds = maxRange / Mathf.Max(1f, speed) + 0.5f;
            p.falloffStart = fullDamageTo;
            p.falloffEnd = maxRange;
            p.falloffMinShare = minShare;
            p.penetration = penetration;
            p.penetrationDamageKeep = keep;
            p.impulse = impulse;
            p.stagger = stagger;
            p.headMultiplier = head;
            p.armorDamageMultiplier = armor;
            p.caliber = caliber;
        }

        private static void Spread(WeaponDefinition d, float tdHip, float tdAds, float tpHip, float tpAds, float fpHip, float fpAds,
            float bloom, float bloomMax, float decay, float moveSpread, float moveSpeed, float adsSeconds)
        {
            WeaponAccuracyBlock a = d.accuracy;
            a.topDown = new SpreadPair(tdHip, tdAds);
            a.thirdPerson = new SpreadPair(tpHip, tpAds);
            a.firstPerson = new SpreadPair(fpHip, fpAds);
            a.bloomPerShot = bloom;
            a.bloomMax = bloomMax;
            a.bloomDecayPerSecond = decay;
            a.bloomDecayDelaySeconds = 0.1f;
            a.moveSpreadMultiplier = moveSpread;
            a.moveSpeedMultiplier = moveSpeed;
            a.adsSeconds = adsSeconds;
            a.adsMoveSpeedMultiplier = 0.75f;
            a.focusMoveSpeedMultiplier = 0.75f;
        }

        private static void Recoil(WeaponDefinition d, float pitch, RecoilYawPattern pattern, float[] yaw, int stableShots, float recovery)
        {
            WeaponAccuracyBlock a = d.accuracy;
            a.recoilPitchPerShot = pitch;
            a.recoilYawPattern = pattern;
            a.recoilYawValues = yaw;
            a.recoilStableShots = stableShots;
            a.recoilRecoverySeconds = recovery;
            a.recoilAccumulationCap = 4f;
            a.topDownYawShare = 0.6f;
        }

        private static void Core(WeaponDefinition d, UpgradeBranch a, UpgradeBranch b, UpgradeBranch c, string keystoneId, string keystoneName, string keystoneEffect)
        {
            d.upgrade.coreBranches = new[] { a, b, c };
            d.upgrade.keystone = new WeaponKeystone { id = keystoneId, displayName = keystoneName, effect = keystoneEffect };
            d.upgrade.explosionSemantics = ExplosionSemantics.Chance;
            d.upgrade.forbiddenBranches = new UpgradeBranch[0];
        }

        private static void Look(WeaponDefinition d, WeaponCasing casing, ClassicWeaponId placeholder, string legacyHudIcon)
        {
            d.presentation.casing = casing;
            d.presentation.placeholderVisual = placeholder;
            d.presentation.hudIconId = legacyHudIcon;   // empty = the derived Weapon.<weaponId> key (S-09 N19)
            ClassicWeaponDefinition roster = (int)d.id <= 2 ? WeaponRoster.Find(d.id) : null;
            d.presentation.modelTp = roster != null ? roster.ModelResource : "";
        }

        // ================================================================== first-person view poses (C-00 25.09)

        /// <summary>
        /// The first-person view pose per classic (grip in the viewmodel camera's space, pitch / yaw, scale), tuned in the
        /// Run scene at 1080p rest against the C-00 v2 target (lower right, ≈ 120–220 k px, grip and magazine on screen,
        /// barrel converging on the crosshair 8 m out) — the framing C-00 approved in review, carried from S-01's former
        /// screen transform (scale 0.75 about the bottom-left, shift −12 %) onto the identity transform: each weapon's grip,
        /// barrel axis and up through that linear map give the rigid pose (silhouette IoU 0.81–0.89 against the reviewed frame);
        /// measured by WeaponModelSliceTests.FP_GorusPozu_OnSilah_KadrajBandinda. S-06 fits the FP arms to them (B4b).
        /// </summary>
        private static readonly Dictionary<string, WeaponFpViewPose> FpPoses = new Dictionary<string, WeaponFpViewPose>
        {
            { "ar_1", WeaponFpViewPose.At(0.252f, -0.150f, 0.35f, -1.12f, -1.89f, 0.841f) },
            { "smg_1", WeaponFpViewPose.At(0.100f, -0.124f, 0.25f, -0.91f, -0.74f, 0.831f) },
            { "sg_1", WeaponFpViewPose.At(0.196f, -0.126f, 0.35f, -0.94f, -1.47f, 0.837f) },
            { "ar_2", WeaponFpViewPose.At(0.324f, -0.192f, 0.45f, -1.46f, -2.46f, 0.845f) },
            { "smg_2", WeaponFpViewPose.At(0.252f, -0.126f, 0.35f, -0.94f, -1.89f, 0.841f) },
            { "sg_2", WeaponFpViewPose.At(0.196f, -0.150f, 0.35f, -1.12f, -1.47f, 0.837f) },
            { "lmg_1", WeaponFpViewPose.At(0.432f, -0.257f, 0.60f, -1.98f, -3.34f, 0.851f) },
            { "lmg_2", WeaponFpViewPose.At(0.432f, -0.162f, 0.60f, -1.25f, -3.34f, 0.853f) },
            { "sr_1", WeaponFpViewPose.At(0.252f, -0.173f, 0.35f, -1.30f, -1.89f, 0.840f) },
            { "rl_1", WeaponFpViewPose.At(0.252f, -0.223f, 0.45f, -1.69f, -1.92f, 0.839f) },
        };

        // ================================================================== asset writing

        private static WeaponDefinition Write(WeaponDefinition fresh)
        {
            if (FpPoses.TryGetValue(fresh.weaponId, out WeaponFpViewPose fp))
            {
                fresh.presentation.fpView = fp;
            }

            string path = Folder + "/" + fresh.name + ".asset";
            WeaponDefinition existing = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }

            EditorUtility.CopySerialized(fresh, existing);
            existing.name = fresh.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(fresh);
            return existing;
        }
    }
}
