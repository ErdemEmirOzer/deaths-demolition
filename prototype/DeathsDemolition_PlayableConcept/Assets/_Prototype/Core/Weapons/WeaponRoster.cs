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
    ///
    /// WP-069 B2b: the Arsenal v3 classics without a roster asset (ar_2 … rl_1, ids 3..9) are added
    /// from their <see cref="WeaponDefinition"/> — identity, sort order, the enabled flag (rl_1 stays off
    /// until B3), the core triple, the unlock kind (meta path, DEC-095) and the sockets; the class
    /// sibling's roster entry lends the visual identifiers (icon, blockout, SFX) until AP-021 / N19.
    /// The A / B / C assets keep their own data (their triples equal ar_1 / smg_1 / sg_1's).
    /// </summary>
    public static class WeaponRoster
    {
        public const string ResourceFolder = "WeaponRoster";

        private static readonly List<ClassicWeaponDefinition> s_all = new List<ClassicWeaponDefinition>(12);
        private static readonly List<ClassicWeaponDefinition> s_enabled = new List<ClassicWeaponDefinition>(12);
        private static readonly List<ClassicWeaponDefinition> s_fromArsenal = new List<ClassicWeaponDefinition>(8);
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

            AppendArsenal();
            Sort();
            s_loaded = true;
        }

        /// <summary>WP-069 B2b: one roster entry per Arsenal classic whose runtime id has no roster asset.</summary>
        private static void AppendArsenal()
        {
            DropArsenalEntries();
            IReadOnlyList<WeaponDefinition> arsenal = Arsenal.All;
            if (arsenal == null)
            {
                return;
            }

            for (int i = 0; i < arsenal.Count; i++)
            {
                WeaponDefinition def = arsenal[i];
                if (def == null || def.slot != WeaponSlotKind.Classic || FindLoaded(def.id) != null)
                {
                    continue;
                }

                ClassicWeaponDefinition entry = FromArsenal(def, FindLoaded(def.presentation.placeholderVisual));
                s_fromArsenal.Add(entry);
                s_all.Add(entry);
            }
        }

        private static ClassicWeaponDefinition FindLoaded(ClassicWeaponId id)
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                if (s_all[i] != null && s_all[i].HasRuntimeId && s_all[i].Id == id) return s_all[i];
            }

            return null;
        }

        /// <summary>The roster view of an Arsenal classic (runtime-only instance, never saved).</summary>
        public static ClassicWeaponDefinition FromArsenal(WeaponDefinition def, ClassicWeaponDefinition sibling)
        {
            var d = ScriptableObject.CreateInstance<ClassicWeaponDefinition>();
            d.name = "Weapon_" + def.weaponId + " (Arsenal)";
            d.hideFlags = HideFlags.HideAndDontSave;
            d.RosterKey = def.weaponId;
            d.HasRuntimeId = true;
            d.Id = def.id;
            d.Enabled = def.enabled;
            d.SortOrder = def.sortOrder;
            d.TypeClass = sibling != null ? sibling.TypeClass : WeaponTypeClass.Rifle;
            d.Silhouette = sibling != null ? sibling.Silhouette : WeaponSilhouetteClass.Medium;
            d.ShotModel = def.fire.pelletCount > 1 ? WeaponShotModel.Pellet : WeaponShotModel.Hitscan;
            d.CoreBranches = def.upgrade.coreBranches != null ? (UpgradeBranch[])def.upgrade.coreBranches.Clone() : new UpgradeBranch[WeaponBranchContract.CoreBranchesPerWeapon];
            d.UnlockKind = def.unlock.kind == WeaponUnlockV3.Default ? WeaponUnlockKind.Default
                : def.unlock.kind == WeaponUnlockV3.ProfileLevel ? WeaponUnlockKind.ProfileLevel
                : WeaponUnlockKind.MetaPath;
            WeaponSockets sockets = def.presentation.sockets;
            if (sockets != null)
            {
                d.SocketRoot = sockets.root;
                d.MuzzleSocket = sockets.muzzle;
                d.EjectionSocket = sockets.ejection;
            }

            d.MuzzleVfxFamily = Either(def.presentation.muzzleVfx, sibling != null ? sibling.MuzzleVfxFamily : d.MuzzleVfxFamily);
            d.ImpactVfxFamily = Either(def.presentation.impactVfx, sibling != null ? sibling.ImpactVfxFamily : d.ImpactVfxFamily);
            d.HudIconId = Either(def.presentation.hudIconId, sibling != null ? sibling.HudIconId : d.HudIconId);
            if (sibling != null)
            {
                d.FireSfx = sibling.FireSfx;
                d.ReloadSfx = sibling.ReloadSfx;
                d.ModelResource = sibling.ModelResource;
            }

            return d;
        }

        private static string Either(string own, string fallback) => string.IsNullOrEmpty(own) ? fallback : own;

        private static void DropArsenalEntries()
        {
            for (int i = 0; i < s_fromArsenal.Count; i++)
            {
                if (s_fromArsenal[i] != null) UnityEngine.Object.DestroyImmediate(s_fromArsenal[i]);
            }

            s_fromArsenal.Clear();
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
                DropArsenalEntries();
                s_loaded = false;
            }

            EnforceCoreBranchSets = true;
        }
    }
}
