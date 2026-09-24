using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>WP-069 A4: the ordered list of every <see cref="WeaponDefinition"/> (Resources/Arsenal/ArsenalCatalog).</summary>
    [CreateAssetMenu(fileName = "ArsenalCatalog", menuName = "Deaths Demolition/Arsenal Catalog")]
    public sealed class ArsenalCatalog : ScriptableObject
    {
        public const string ResourcesPath = "Arsenal/ArsenalCatalog";

        public List<WeaponDefinition> weapons = new List<WeaponDefinition>();

        /// <summary>Catalog-level validation: every entry valid, ids and runtime indices unique.</summary>
        public bool Validate(List<string> faults)
        {
            int before = faults.Count;
            var keys = new HashSet<string>();
            var classicIds = new HashSet<ClassicWeaponId>();
            for (int i = 0; i < weapons.Count; i++)
            {
                WeaponDefinition d = weapons[i];
                if (d == null)
                {
                    faults.Add("[catalog] null entry at " + i);
                    continue;
                }

                d.Validate(faults);
                if (!keys.Add(d.weaponId)) faults.Add("[catalog] duplicate weaponId " + d.weaponId);
                if (d.slot == WeaponSlotKind.Classic && !classicIds.Add(d.id)) faults.Add("[catalog] duplicate runtime id " + d.id);
            }

            return faults.Count == before;
        }
    }
}
