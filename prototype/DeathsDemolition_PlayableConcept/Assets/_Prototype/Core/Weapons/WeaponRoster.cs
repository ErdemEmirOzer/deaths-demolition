using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>
    /// WP-034: the loaded classic weapon roster — every <see cref="ClassicWeaponDefinition"/>
    /// under Resources/WeaponRoster, sorted by <see cref="ClassicWeaponDefinition.SortOrder"/>.
    /// Lookups by runtime id, the default weapon (DEC-037/039), the per-weapon branch
    /// gate (ADR-034-1) and validation. Pure data; no gameplay state.
    /// </summary>
    public static class WeaponRoster
    {
        public const string ResourceFolder = "WeaponRoster";

        private static readonly List<ClassicWeaponDefinition> s_all = new List<ClassicWeaponDefinition>(4);
        private static readonly List<ClassicWeaponDefinition> s_enabled = new List<ClassicWeaponDefinition>(4);
        private static bool s_loaded;
        private static bool s_fromTests;

        /// <summary>
        /// ADR-034-1: when true the upgrade shop offers a weapon only its three core
        /// branches plus the shared player branches. Data switch for the user's decision on
        /// the branch principle; default = proposal (a).
        /// </summary>
        public static bool EnforceCoreBranchSets { get; set; } = true;

        /// <summary>Every definition (enabled or not), sorted. Loads lazily.</summary>
        public static IReadOnlyList<ClassicWeaponDefinition> All
        {
            get { EnsureLoaded(); return s_all; }
        }

        /// <summary>Enabled definitions with a runtime id, sorted.</summary>
        public static IReadOnlyList<ClassicWeaponDefinition> Enabled
        {
            get { EnsureLoaded(); return s_enabled; }
        }

        public static bool IsLoaded => s_loaded;

        /// <summary>The definition of a runtime id, or null.</summary>
        public static ClassicWeaponDefinition Find(ClassicWeaponId id)
        {
            EnsureLoaded();
            for (int i = 0; i < s_all.Count; i++)
            {
                ClassicWeaponDefinition d = s_all[i];
                if (d != null && d.HasRuntimeId && d.Id == id) return d;
            }

            return null;
        }

        /// <summary>The definition of a roster key ("A".."D"), or null.</summary>
        public static ClassicWeaponDefinition FindByKey(string rosterKey)
        {
            EnsureLoaded();
            for (int i = 0; i < s_all.Count; i++)
            {
                if (s_all[i] != null && string.Equals(s_all[i].RosterKey, rosterKey, StringComparison.OrdinalIgnoreCase)) return s_all[i];
            }

            return null;
        }

        public static bool IsEnabled(ClassicWeaponId id)
        {
            ClassicWeaponDefinition d = Find(id);
            return d != null && d.Enabled;
        }

        /// <summary>
        /// DEC-037/039: the weapon a fresh session starts on — the first enabled entry with
        /// <see cref="WeaponUnlockKind.Default"/>, else the first enabled entry, else the
        /// historical AssaultRifle.
        /// </summary>
        public static ClassicWeaponId DefaultWeapon
        {
            get
            {
                EnsureLoaded();
                for (int i = 0; i < s_enabled.Count; i++)
                {
                    if (s_enabled[i].UnlockKind == WeaponUnlockKind.Default) return s_enabled[i].Id;
                }

                return s_enabled.Count > 0 ? s_enabled[0].Id : ClassicWeaponId.AssaultRifle;
            }
        }

        /// <summary>
        /// The branch gate (ADR-034-1). True when the branch may be offered while
        /// <paramref name="weapon"/> is equipped: a shared player branch, or one of the
        /// weapon's core branches. A weapon without a definition, or the gate switched off,
        /// keeps the pre-WP-034 behaviour (everything offered).
        /// </summary>
        public static bool IsBranchOffered(ClassicWeaponId weapon, UpgradeBranch branch)
        {
            if (!EnforceCoreBranchSets)
            {
                return true;
            }

            ClassicWeaponDefinition d = Find(weapon);
            if (d == null)
            {
                return true;
            }

            return WeaponBranchContract.IsSharedPlayer(branch) || d.HasCoreBranch(branch);
        }

        /// <summary>Validate every definition and the roster as a whole. Returns the fault list (empty = clean).</summary>
        public static List<string> Validate()
        {
            EnsureLoaded();
            var faults = new List<string>();
            if (s_all.Count == 0)
            {
                faults.Add("roster empty: no ClassicWeaponDefinition under Resources/" + ResourceFolder);
                return faults;
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<ClassicWeaponId>();
            var triples = new List<string>();
            bool anyDefault = false;
            for (int i = 0; i < s_all.Count; i++)
            {
                ClassicWeaponDefinition d = s_all[i];
                if (d == null)
                {
                    faults.Add("null definition");
                    continue;
                }

                d.Validate(faults);
                if (!keys.Add(d.RosterKey ?? string.Empty)) faults.Add("duplicate RosterKey " + d.RosterKey);
                if (d.HasRuntimeId && !ids.Add(d.Id)) faults.Add("duplicate runtime id " + d.Id);
                if (d.Enabled && d.UnlockKind == WeaponUnlockKind.Default) anyDefault = true;

                if (d.Enabled && d.CoreBranches != null && d.CoreBranches.Length == WeaponBranchContract.CoreBranchesPerWeapon)
                {
                    var sorted = new List<UpgradeBranch>(d.CoreBranches);
                    sorted.Sort();
                    string triple = string.Join("+", sorted);
                    if (triples.Contains(triple)) faults.Add("two enabled weapons share the same core triple " + triple);
                    triples.Add(triple);
                }
            }

            if (!anyDefault) faults.Add("no enabled definition is the Default (DEC-037/039) weapon");
            return faults;
        }

        /// <summary>Test seam: replace the loaded roster (null restores Resources loading).</summary>
        public static void SetForTests(IList<ClassicWeaponDefinition> definitions)
        {
            s_all.Clear();
            s_enabled.Clear();
            if (definitions == null)
            {
                s_loaded = false;
                s_fromTests = false;
                return;
            }

            s_all.AddRange(definitions);
            Sort();
            s_loaded = true;
            s_fromTests = true;
        }

        private static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_all.Clear();
            s_enabled.Clear();
            ClassicWeaponDefinition[] loaded = Resources.LoadAll<ClassicWeaponDefinition>(ResourceFolder);
            if (loaded != null)
            {
                s_all.AddRange(loaded);
            }

            Sort();
            s_loaded = true;
        }

        private static void Sort()
        {
            s_all.Sort((a, b) =>
            {
                int c = a.SortOrder.CompareTo(b.SortOrder);
                return c != 0 ? c : string.CompareOrdinal(a.RosterKey, b.RosterKey);
            });

            s_enabled.Clear();
            for (int i = 0; i < s_all.Count; i++)
            {
                if (s_all[i] != null && s_all[i].Enabled && s_all[i].HasRuntimeId) s_enabled.Add(s_all[i]);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (!s_fromTests)
            {
                s_all.Clear();
                s_enabled.Clear();
                s_loaded = false;
            }

            EnforceCoreBranchSets = true;
        }
    }
}
