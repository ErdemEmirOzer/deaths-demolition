using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Weapons.EditorTools
{
    /// <summary>
    /// WP-069 B1 · writes the WeaponDefinition v3 assets and the ArsenalCatalog
    /// (Assets/_Prototype/Weapons/Arsenal/Resources/Arsenal). Deterministic and idempotent:
    /// an existing asset is overwritten in place (GUID kept). B1 migrates the three M3 weapons
    /// A/B/C → ar_1 / smg_1 / sg_1 with their shipped numbers (DEC-083 + WP-042 / Ö-4) so only
    /// the projectile flight is new; B2 adds the seven new weapons and the DEC-096 numbers.
    ///
    /// Batch: -executeMethod DD.Prototype.Weapons.EditorTools.ArsenalAssetBuilder.BuildB1
    /// </summary>
    public static class ArsenalAssetBuilder
    {
        public const string Folder = "Assets/_Prototype/Weapons/Arsenal/Resources/Arsenal";

        [MenuItem("Deaths Demolition/Weapons/Build Arsenal (WP-069)")]
        public static void BuildB1()
        {
            Directory.CreateDirectory(Folder);
            List<ClassicWeaponSettings> m3 = WeaponConfigSection.CodeDefaults();

            var defs = new List<WeaponDefinition>
            {
                Write(Ar1(Find(m3, ClassicWeaponId.AssaultRifle))),
                Write(Smg1(Find(m3, ClassicWeaponId.AutoSmg))),
                Write(Sg1(Find(m3, ClassicWeaponId.CombatShotgun)))
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

        // ------------------------------------------------------------------ the three migrated weapons

        private static WeaponDefinition Ar1(ClassicWeaponSettings a)
        {
            WeaponDefinition d = Common(a, "ar_1", WeaponClass.AssaultRifle, WeaponSilhouetteV3.Long, 0, "AssaultRifle");
            d.projectile.speed = 260f;
            d.projectile.caliber = WeaponCaliber.Medium;
            d.projectile.stagger = 0.25f;
            d.presentation.casing = WeaponCasing.Rifle;
            d.accuracy.topDown = new SpreadPair(a.SpreadDegrees, 0.7f);
            d.accuracy.thirdPerson = new SpreadPair(1.8f, 0.5f);
            d.accuracy.firstPerson = new SpreadPair(1.6f, 0.35f);
            return d;
        }

        private static WeaponDefinition Smg1(ClassicWeaponSettings b)
        {
            WeaponDefinition d = Common(b, "smg_1", WeaponClass.Smg, WeaponSilhouetteV3.ShortThick, 1, "AutoSmg");
            d.projectile.speed = 170f;
            d.projectile.caliber = WeaponCaliber.Light;
            d.projectile.stagger = 0.12f;
            d.presentation.casing = WeaponCasing.Pistol;
            d.accuracy.topDown = new SpreadPair(b.SpreadDegrees, 1.6f);
            d.accuracy.thirdPerson = new SpreadPair(3.5f, 1.1f);
            d.accuracy.firstPerson = new SpreadPair(3.2f, 0.8f);
            return d;
        }

        private static WeaponDefinition Sg1(ClassicWeaponSettings c)
        {
            WeaponDefinition d = Common(c, "sg_1", WeaponClass.Shotgun, WeaponSilhouetteV3.ShortThick, 2, "CombatShotgun");
            d.fire.pelletPattern = WeaponPelletPattern.CenterRings;
            d.fire.rings = new[] { new PelletRing { count = Mathf.Max(1, c.PelletCount - 1), radiusShare = c.PelletRingRadiusShare } };
            d.fire.ringJitterShare = c.PelletRingJitterShare;
            d.projectile.kind = ProjectileKind.Pellet;
            d.projectile.speed = 120f;
            d.projectile.caliber = WeaponCaliber.Heavy;
            d.projectile.stagger = 0.60f;
            d.presentation.casing = WeaponCasing.Shell;
            d.accuracy.topDown = new SpreadPair(c.SpreadDegrees, 5.5f);
            d.accuracy.thirdPerson = new SpreadPair(8.5f, 4.0f);
            d.accuracy.firstPerson = new SpreadPair(8.0f, 3.5f);
            return d;
        }

        /// <summary>The M3 numbers under the v3 blocks (behaviour-preserving: no falloff, no inherent penetration, no game recoil).</summary>
        private static WeaponDefinition Common(ClassicWeaponSettings s, string key, WeaponClass cls, WeaponSilhouetteV3 silhouette, int sort, string legacyName)
        {
            WeaponDefinition d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.name = "WD_" + key;
            d.weaponId = key;
            d.id = s.Id;
            d.slot = WeaponSlotKind.Classic;
            d.weaponClass = cls;
            d.displayName = s.DisplayName;
            d.shortName = s.DisplayName.Length <= 10 ? s.DisplayName : s.DisplayName.Substring(0, 10);
            d.sortOrder = sort;
            d.enabled = true;
            d.silhouette = silhouette;
            d.legacyNames = new[] { legacyName };

            d.fire.fireMode = WeaponFireMode.Auto;   // M3: hold-to-fire for all three (REQ-039); B2 brings Single / Burst
            d.fire.rpm = s.FireRate * 60f;
            d.fire.damage = s.Damage;
            d.fire.pelletCount = Mathf.Max(1, s.PelletCount);
            d.fire.pelletPattern = WeaponPelletPattern.Single;

            d.ammo.magazine = s.MagazineSize;
            d.ammo.reserve = -1;
            d.ammo.reloadKind = WeaponReloadKind.Magazine;
            d.ammo.reloadTacSeconds = s.ReloadTime;
            d.ammo.reloadEmptySeconds = s.ReloadTime;

            d.projectile.kind = ProjectileKind.Bullet;
            d.projectile.gravity = 9.81f;
            d.projectile.maxRange = s.Range;
            d.projectile.falloffStart = s.Range;
            d.projectile.falloffEnd = s.Range;
            d.projectile.falloffMinShare = 1f;
            d.projectile.penetration = 0;
            d.projectile.impulse = s.StaggerImpulse;
            d.projectile.headMultiplier = 1f;
            d.projectile.armorDamageMultiplier = 1f;

            d.accuracy.bloomPerShot = s.SpreadBloomPerShot;
            d.accuracy.bloomMax = s.SpreadBloomMax;
            d.accuracy.bloomDecayPerSecond = s.SpreadBloomDecay;
            d.accuracy.recoilPitchPerShot = 0f;
            d.accuracy.recoilYawPattern = RecoilYawPattern.None;

            ClassicWeaponDefinition roster = WeaponRoster.Find(s.Id);
            if (roster != null)
            {
                d.upgrade.coreBranches = (UpgradeBranch[])roster.CoreBranches.Clone();
                d.presentation.hudIconId = roster.HudIconId;
                d.presentation.modelTp = roster.ModelResource;
            }

            d.SetUnlockFromM3(s, roster != null && roster.UnlockKind == WeaponUnlockKind.Default);
            d.unlock.unlockOrder = sort;
            d.presentation.fireLoop = d.fire.fireMode == WeaponFireMode.Auto && d.fire.rpm >= 600f;

            float lifetime = s.Range / 120f + 0.25f;   // slowest B1 projectile (120 m/s) over the full range, plus slack
            d.projectile.maxLifetimeSeconds = lifetime;
            return d;
        }

        // ------------------------------------------------------------------ asset writing

        private static WeaponDefinition Write(WeaponDefinition fresh)
        {
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

        private static ClassicWeaponSettings Find(List<ClassicWeaponSettings> list, ClassicWeaponId id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
            throw new System.InvalidOperationException("M3 code default missing: " + id);
        }
    }
}
