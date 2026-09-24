using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>
    /// WP-069 A4: runtime access to the weapon definitions. Loads the catalog once, builds two
    /// lookups (runtime index, weaponId) and never allocates on a lookup. Tests replace the
    /// catalog with <see cref="SetForTests"/>.
    /// </summary>
    public static class Arsenal
    {
        private static ArsenalCatalog s_catalog;
        private static bool s_loaded;
        private static readonly Dictionary<ClassicWeaponId, WeaponDefinition> s_byId = new Dictionary<ClassicWeaponId, WeaponDefinition>();
        private static readonly Dictionary<string, WeaponDefinition> s_byKey = new Dictionary<string, WeaponDefinition>();

        /// <summary>The loaded catalog, or null when the project has none (EditMode without assets).</summary>
        public static ArsenalCatalog Catalog
        {
            get
            {
                EnsureLoaded();
                return s_catalog;
            }
        }

        public static IReadOnlyList<WeaponDefinition> All
        {
            get
            {
                EnsureLoaded();
                return s_catalog != null ? (IReadOnlyList<WeaponDefinition>)s_catalog.weapons : System.Array.Empty<WeaponDefinition>();
            }
        }

        /// <summary>The classic weapon with this runtime index, or null.</summary>
        public static WeaponDefinition Get(ClassicWeaponId id)
        {
            EnsureLoaded();
            return s_byId.TryGetValue(id, out WeaponDefinition d) ? d : null;
        }

        /// <summary>The weapon with this key (ar_1 … sw_gel) or one of its legacy names, or null.</summary>
        public static WeaponDefinition Get(string weaponIdOrLegacyName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(weaponIdOrLegacyName)) return null;
            return s_byKey.TryGetValue(weaponIdOrLegacyName, out WeaponDefinition d) ? d : null;
        }

        /// <summary>Tests: use this catalog instead of the Resources one (null = back to Resources).</summary>
        public static void SetForTests(ArsenalCatalog catalog)
        {
            s_catalog = catalog;
            s_loaded = catalog != null;
            Rebuild();
        }

        private static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            s_catalog = Resources.Load<ArsenalCatalog>(ArsenalCatalog.ResourcesPath);
            Rebuild();
        }

        private static void Rebuild()
        {
            s_byId.Clear();
            s_byKey.Clear();
            if (s_catalog == null)
            {
                return;
            }

            for (int i = 0; i < s_catalog.weapons.Count; i++)
            {
                WeaponDefinition d = s_catalog.weapons[i];
                if (d == null) continue;
                if (d.slot == WeaponSlotKind.Classic && !s_byId.ContainsKey(d.id)) s_byId.Add(d.id, d);
                if (!string.IsNullOrEmpty(d.weaponId) && !s_byKey.ContainsKey(d.weaponId)) s_byKey.Add(d.weaponId, d);
                if (d.legacyNames != null)
                {
                    for (int n = 0; n < d.legacyNames.Length; n++)
                    {
                        string legacy = d.legacyNames[n];
                        if (!string.IsNullOrEmpty(legacy) && !s_byKey.ContainsKey(legacy)) s_byKey.Add(legacy, d);
                    }
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_catalog = null;
            s_loaded = false;
            s_byId.Clear();
            s_byKey.Clear();
        }
    }
}

namespace DD.Prototype.Core.Weapons
{
    /// <summary>
    /// WP-069 A4 · saved-profile migration table (S-04 ProfileMigration v5 calls it). Static, asset-free:
    /// the M3 <c>ClassicWeaponId</c> names stored in <c>ProfileData.UnlockedWeapons</c> map to the v3 keys.
    /// </summary>
    public static class WeaponIdMigration
    {
        /// <summary>v3 key (ar_1 …) for an M3 weapon name ("AssaultRifle" …); a v3 key passes through; null when unknown.</summary>
        public static string FromLegacyName(string name)
        {
            switch (name)
            {
                case "AssaultRifle": return "ar_1";
                case "AutoSmg": return "smg_1";
                case "CombatShotgun": return "sg_1";
                case "ar_1": case "ar_2": case "smg_1": case "smg_2": case "sg_1": case "sg_2":
                case "lmg_1": case "lmg_2": case "sr_1": case "rl_1": case "sw_arc": case "sw_gel":
                    return name;
                default:
                    return null;
            }
        }

        /// <summary>Runtime index of a v3 classic key (the M3 values 0/1/2 are kept); false for Specials / unknown keys.</summary>
        public static bool TryGetRuntimeId(string weaponId, out ClassicWeaponId id)
        {
            switch (weaponId)
            {
                case "ar_1": id = (ClassicWeaponId)0; return true;
                case "smg_1": id = (ClassicWeaponId)1; return true;
                case "sg_1": id = (ClassicWeaponId)2; return true;
                case "ar_2": id = (ClassicWeaponId)3; return true;
                case "smg_2": id = (ClassicWeaponId)4; return true;
                case "sg_2": id = (ClassicWeaponId)5; return true;
                case "lmg_1": id = (ClassicWeaponId)6; return true;
                case "lmg_2": id = (ClassicWeaponId)7; return true;
                case "sr_1": id = (ClassicWeaponId)8; return true;
                case "rl_1": id = (ClassicWeaponId)9; return true;
                default: id = (ClassicWeaponId)0; return false;
            }
        }
    }
}
