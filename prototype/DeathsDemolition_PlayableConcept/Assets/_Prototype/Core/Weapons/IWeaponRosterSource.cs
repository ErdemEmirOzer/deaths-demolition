using System.Collections.Generic;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>One selectable weapon as the Skin &amp; Weapon Selection screen (S-09, WP-022) needs it.</summary>
    public readonly struct WeaponRosterEntry
    {
        public WeaponRosterEntry(string rosterKey, ClassicWeaponId id, string displayName, WeaponTypeClass typeClass,
            WeaponSilhouetteClass silhouette, WeaponShotModel shotModel, int unlockLevel, bool unlocked, bool isDefault,
            UpgradeBranch[] coreBranches, string hudIconId, int sortOrder)
        {
            RosterKey = rosterKey;
            Id = id;
            DisplayName = displayName;
            TypeClass = typeClass;
            Silhouette = silhouette;
            ShotModel = shotModel;
            UnlockLevel = unlockLevel;
            Unlocked = unlocked;
            IsDefault = isDefault;
            CoreBranches = coreBranches;
            HudIconId = hudIconId;
            SortOrder = sortOrder;
        }

        public string RosterKey { get; }
        public ClassicWeaponId Id { get; }
        public string DisplayName { get; }
        public WeaponTypeClass TypeClass { get; }
        public WeaponSilhouetteClass Silhouette { get; }
        public WeaponShotModel ShotModel { get; }

        /// <summary>Profile level that opens it (1 = open from the start).</summary>
        public int UnlockLevel { get; }

        /// <summary>Whether THIS profile may take it (WP-009 entitlement).</summary>
        public bool Unlocked { get; }

        /// <summary>DEC-037/039 default selection.</summary>
        public bool IsDefault { get; }

        public UpgradeBranch[] CoreBranches { get; }
        public string HudIconId { get; }
        public int SortOrder { get; }
    }

    /// <summary>
    /// WP-034: what the selection screen reads instead of enumerating <see cref="ClassicWeaponId"/>:
    /// only enabled roster weapons, in roster order, with their unlock state and the
    /// default. Implemented in the runtime assembly (profile access); found through
    /// <see cref="WeaponRosterSourceRegistry"/>.
    /// </summary>
    public interface IWeaponRosterSource
    {
        /// <summary>Enabled weapons in roster order. Snapshot built on each call (unlock state is live).</summary>
        IReadOnlyList<WeaponRosterEntry> Entries { get; }

        /// <summary>DEC-037/039 default.</summary>
        ClassicWeaponId DefaultWeapon { get; }

        bool IsSelectable(ClassicWeaponId id);
    }

    /// <summary>Where the selection screen finds the live source. Null before the runtime installs it.</summary>
    public static class WeaponRosterSourceRegistry
    {
        public static IWeaponRosterSource Current { get; private set; }

        public static void SetCurrent(IWeaponRosterSource source)
        {
            Current = source;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
        }
    }
}
