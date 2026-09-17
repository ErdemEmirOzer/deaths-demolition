using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>
    /// WP-034: one classic weapon of the roster as DATA (ScriptableObject under
    /// Assets/_Prototype/Weapons/Roster/Resources/WeaponRoster). Identity, type and
    /// silhouette class, shot model, the three weapon-core branches, the unlock kind,
    /// the socket / VFX / SFX / HUD identifiers the AP-013 visual contract binds to.
    ///
    /// The NUMBERS (damage, fire rate, magazine, reload, pellets, spread, range …) are
    /// NOT duplicated here: they stay in <see cref="PrototypeConfig.Weapons"/>
    /// (PrototypeOnly, F1-editable, single source) and are reached through
    /// <see cref="Settings"/>. A definition without a runtime id (the optional D) is
    /// data only — it never reaches the runtime until the user enables it, which means
    /// adding its <see cref="ClassicWeaponId"/> and its settings entry.
    /// </summary>
    [CreateAssetMenu(fileName = "Weapon_", menuName = "Deaths Demolition/Classic Weapon Definition")]
    public sealed class ClassicWeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable roster key from the WP-024 proposal: A, B, C, D.")]
        public string RosterKey = "A";

        [Tooltip("False for a data-only entry (no ClassicWeaponId, no settings) — the optional D.")]
        public bool HasRuntimeId = true;

        public ClassicWeaponId Id = ClassicWeaponId.AssaultRifle;

        [Tooltip("Disabled entries are never selectable and never offered; they exist for the visual contract.")]
        public bool Enabled = true;

        [Tooltip("Order in the selection list and the AP-013 work order.")]
        public int SortOrder;

        [Header("Class")]
        public WeaponTypeClass TypeClass = WeaponTypeClass.Rifle;
        public WeaponSilhouetteClass Silhouette = WeaponSilhouetteClass.Long;
        public WeaponShotModel ShotModel = WeaponShotModel.Hitscan;

        [Header("Branches (proposal principle (a): 3 weapon-core + the shared player branches)")]
        public UpgradeBranch[] CoreBranches = new UpgradeBranch[WeaponBranchContract.CoreBranchesPerWeapon];

        [Header("Unlock (the level itself is ClassicWeaponSettings.UnlockProfileLevel)")]
        public WeaponUnlockKind UnlockKind = WeaponUnlockKind.ProfileLevel;

        [Header("AP-013 visual contract identifiers")]
        [Tooltip("Rig socket the weapon root is parented to (AP-006 PlayerRig).")]
        public string SocketRoot = "socket_weapon_r";

        [Tooltip("Child transform on the weapon model: muzzle flash / tracer origin (WeaponVisualRig marker name).")]
        public string MuzzleSocket = "Muzzle";

        [Tooltip("Child transform on the weapon model: casing ejection point.")]
        public string EjectionSocket = "Ejection";

        [Tooltip("Child transforms for the three core-branch attachment visuals (one per core branch, same order).")]
        public string[] AttachmentSockets = { "attach_barrel", "attach_top", "attach_stock" };

        [Tooltip("AP-010 VFX family for the muzzle flash.")]
        public string MuzzleVfxFamily = "muzzle";

        [Tooltip("AP-010 VFX family for the impact.")]
        public string ImpactVfxFamily = "impact";

        [Tooltip("AudioSoundId NAME of the fire sound (Audio library).")]
        public string FireSfx = "RifleFire";

        [Tooltip("AudioSoundId NAME of the reload sound set start.")]
        public string ReloadSfx = "ReloadStart";

        [Tooltip("HUD icon identifier (S-09 sprite key).")]
        public string HudIconId = "weapon_rifle";

        [Tooltip("WP-042 (ADR-042-1): Resources path of the model root shown at socket_weapon_r — the AP-009C blockout until the AP-013 model replaces it (same sockets). Empty = the WP-002 primitive rig body.")]
        public string ModelResource = "";

        /// <summary>The PrototypeOnly numbers of this weapon, or null (data-only entry / no config).</summary>
        public ClassicWeaponSettings Settings(PrototypeConfig config)
        {
            if (!HasRuntimeId || config == null || config.Weapons == null)
            {
                return null;
            }

            return config.Weapons.Find(Id);
        }

        /// <summary>Profile level that opens the weapon (1 = open from the start). From the settings; 1 when unknown.</summary>
        public int UnlockLevel(PrototypeConfig config)
        {
            if (UnlockKind == WeaponUnlockKind.Default)
            {
                return 1;
            }

            ClassicWeaponSettings s = Settings(config);
            return s != null ? Mathf.Max(1, s.UnlockProfileLevel) : 1;
        }

        public bool HasCoreBranch(UpgradeBranch branch)
        {
            if (CoreBranches == null)
            {
                return false;
            }

            for (int i = 0; i < CoreBranches.Length; i++)
            {
                if (CoreBranches[i] == branch) return true;
            }

            return false;
        }

        /// <summary>Structural validation. Appends one line per fault; returns true when clean.</summary>
        public bool Validate(List<string> faults)
        {
            int before = faults.Count;
            string tag = "[" + RosterKey + "/" + name + "] ";

            if (string.IsNullOrWhiteSpace(RosterKey)) faults.Add(tag + "RosterKey empty");
            if (CoreBranches == null || CoreBranches.Length != WeaponBranchContract.CoreBranchesPerWeapon)
            {
                faults.Add(tag + "CoreBranches must have exactly " + WeaponBranchContract.CoreBranchesPerWeapon + " entries");
            }
            else
            {
                for (int i = 0; i < CoreBranches.Length; i++)
                {
                    if (!WeaponBranchContract.IsWeaponCore(CoreBranches[i]))
                    {
                        faults.Add(tag + CoreBranches[i] + " is not a weapon-core branch");
                    }

                    for (int j = i + 1; j < CoreBranches.Length; j++)
                    {
                        if (CoreBranches[i] == CoreBranches[j]) faults.Add(tag + "duplicate core branch " + CoreBranches[i]);
                    }
                }

                // Pellet weapons never carry the per-shot multipliers (Chain / Ricochet): a
                // pellet count would turn them into a multiplier the balance cannot hold.
                if (ShotModel == WeaponShotModel.Pellet && (HasCoreBranch(UpgradeBranch.Chain) || HasCoreBranch(UpgradeBranch.Ricochet)))
                {
                    faults.Add(tag + "a pellet weapon cannot carry Chain or Ricochet as a core branch");
                }
            }

            if (string.IsNullOrWhiteSpace(SocketRoot)) faults.Add(tag + "SocketRoot empty");
            if (string.IsNullOrWhiteSpace(MuzzleSocket)) faults.Add(tag + "MuzzleSocket empty");
            if (string.IsNullOrWhiteSpace(EjectionSocket)) faults.Add(tag + "EjectionSocket empty");
            if (AttachmentSockets == null || AttachmentSockets.Length != WeaponBranchContract.CoreBranchesPerWeapon)
            {
                faults.Add(tag + "AttachmentSockets must have one entry per core branch");
            }

            if (string.IsNullOrWhiteSpace(MuzzleVfxFamily) || string.IsNullOrWhiteSpace(ImpactVfxFamily)) faults.Add(tag + "VFX family empty");
            if (string.IsNullOrWhiteSpace(FireSfx) || string.IsNullOrWhiteSpace(ReloadSfx)) faults.Add(tag + "SFX id empty");
            if (string.IsNullOrWhiteSpace(HudIconId)) faults.Add(tag + "HudIconId empty");
            if (Enabled && !HasRuntimeId) faults.Add(tag + "an enabled entry needs a runtime id");

            return faults.Count == before;
        }
    }
}
