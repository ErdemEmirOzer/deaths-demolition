using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-034: the runtime <see cref="IWeaponRosterSource"/> (roster data + WP-009 profile
    /// entitlement) and the wiring of the roster's branch gate into the upgrade shop
    /// (ADR-034-1). Installed once per process; no scene object.
    /// </summary>
    public sealed class WeaponRosterSource : IWeaponRosterSource
    {
        private static WeaponRosterSource s_instance;
        private readonly List<WeaponRosterEntry> _entries = new List<WeaponRosterEntry>(4);

        public static WeaponRosterSource Instance => s_instance ?? (s_instance = new WeaponRosterSource());

        public IReadOnlyList<WeaponRosterEntry> Entries
        {
            get
            {
                _entries.Clear();
                PrototypeConfig config = PrototypeConfig.Active;
                IReadOnlyList<ClassicWeaponDefinition> enabled = WeaponRoster.Enabled;
                ClassicWeaponId def = WeaponRoster.DefaultWeapon;
                for (int i = 0; i < enabled.Count; i++)
                {
                    ClassicWeaponDefinition d = enabled[i];
                    ClassicWeaponSettings s = d.Settings(config);
                    string name = s != null && !string.IsNullOrEmpty(s.DisplayName) ? s.DisplayName : d.Id.ToString();
                    _entries.Add(new WeaponRosterEntry(d.RosterKey, d.Id, name, d.TypeClass, d.Silhouette, d.ShotModel,
                        d.UnlockLevel(config), ProfileService.IsWeaponUnlocked(d.Id), d.Id == def, d.CoreBranches, d.HudIconId, d.SortOrder));
                }

                return _entries;
            }
        }

        public ClassicWeaponId DefaultWeapon => WeaponRoster.DefaultWeapon;

        public bool IsSelectable(ClassicWeaponId id)
        {
            return WeaponRoster.IsEnabled(id) && ProfileService.IsWeaponUnlocked(id);
        }

        /// <summary>Which branches the shop may offer for the weapon in the player's hands right now.</summary>
        public static bool IsBranchOfferedForSelectedWeapon(UpgradeBranch branch)
        {
            return WeaponRoster.IsBranchOffered(WeaponRegistry.SelectedWeapon, branch);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            s_instance = null;
            WeaponRosterSourceRegistry.SetCurrent(Instance);
            UpgradeCardCatalog.WeaponBranchGate = IsBranchOfferedForSelectedWeapon;
        }
    }
}
