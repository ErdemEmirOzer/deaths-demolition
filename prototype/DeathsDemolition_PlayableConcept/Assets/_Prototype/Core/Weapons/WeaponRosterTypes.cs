using System.Collections.Generic;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Core.Weapons
{
    /// <summary>Weapon TYPE class of a roster entry (WP-024 proposal: A rifle, B carbine, C shotgun, D heavy pistol). A class, not a name — no lore.</summary>
    public enum WeaponTypeClass
    {
        Rifle = 0,
        Carbine = 1,
        Shotgun = 2,
        HeavyPistol = 3
    }

    /// <summary>Silhouette / length class for readability at the raised camera (REQ-105) and for AP-013 modelling.</summary>
    public enum WeaponSilhouetteClass
    {
        Long = 0,
        Medium = 1,
        Short = 2
    }

    /// <summary>How the weapon resolves a trigger pull in <c>HitscanEngine</c>.</summary>
    public enum WeaponShotModel
    {
        /// <summary>One ray per trigger pull.</summary>
        Hitscan = 0,

        /// <summary>Several pellets per trigger pull (PelletCount &gt; 1).</summary>
        Pellet = 1
    }

    /// <summary>How the weapon is opened for selection (data; the entitlement itself is the WP-009 profile ladder).</summary>
    public enum WeaponUnlockKind
    {
        /// <summary>Open from the first run (DEC-037/039 default weapon).</summary>
        Default = 0,

        /// <summary>Opens at the profile level in <c>ClassicWeaponSettings.UnlockProfileLevel</c> (DEC-008).</summary>
        ProfileLevel = 1
    }

    /// <summary>
    /// WP-034 (ADR-034-1): how an upgrade branch resolves on a MULTI-PELLET shot. Fixed
    /// by test; the numbers of the branches themselves stay PrototypeOnly.
    /// </summary>
    public enum PelletBranchResolution
    {
        /// <summary>Applies to every pellet (a shotgun with Piercing pierces with each pellet — the intended multiplier).</summary>
        PerPellet = 0,

        /// <summary>The chance is divided by the pellet count so one trigger pull carries the authored odds once.</summary>
        PelletNormalized = 1,

        /// <summary>Applies once per trigger pull (first pellet only): Chain and Ricochet never multiply with the pellet count.</summary>
        PerShot = 2
    }

    /// <summary>The branch sets and the pellet contract, as data shared by the roster, the shop gate and the tests.</summary>
    public static class WeaponBranchContract
    {
        /// <summary>Branches that transform the classic weapon itself; a roster entry picks three of these as its core set.</summary>
        public static readonly UpgradeBranch[] WeaponCoreBranches =
        {
            UpgradeBranch.Piercing, UpgradeBranch.Ricochet, UpgradeBranch.Explosion,
            UpgradeBranch.Chain, UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed,
            UpgradeBranch.ArmorBreak, UpgradeBranch.Impact, UpgradeBranch.Cluster   // WP-077 / WP-069 §6
        };

        /// <summary>Branches on the player, offered with every weapon (proposal principle (a): 3 core + 4 shared).</summary>
        public static readonly UpgradeBranch[] SharedPlayerBranches =
        {
            UpgradeBranch.MovementSpeed, UpgradeBranch.MaximumHealth,
            UpgradeBranch.RegenerationDelay, UpgradeBranch.RegenerationRate
        };

        public const int CoreBranchesPerWeapon = 3;

        public static bool IsWeaponCore(UpgradeBranch branch)
        {
            for (int i = 0; i < WeaponCoreBranches.Length; i++)
            {
                if (WeaponCoreBranches[i] == branch) return true;
            }

            return false;
        }

        public static bool IsSharedPlayer(UpgradeBranch branch)
        {
            for (int i = 0; i < SharedPlayerBranches.Length; i++)
            {
                if (SharedPlayerBranches[i] == branch) return true;
            }

            return false;
        }

        /// <summary>
        /// The pellet contract (ADR-034-1). Piercing per pellet; Explosion pellet-normalized;
        /// Chain and Ricochet per shot. FireRate / ReloadSpeed and the player branches do
        /// not touch a pellet at all (PerShot by definition).
        /// </summary>
        public static PelletBranchResolution ResolutionOf(UpgradeBranch branch)
        {
            switch (branch)
            {
                case UpgradeBranch.Piercing: return PelletBranchResolution.PerPellet;
                case UpgradeBranch.Explosion: return PelletBranchResolution.PelletNormalized;
                case UpgradeBranch.Chain:
                case UpgradeBranch.Ricochet:
                default: return PelletBranchResolution.PerShot;
            }
        }
    }
}
