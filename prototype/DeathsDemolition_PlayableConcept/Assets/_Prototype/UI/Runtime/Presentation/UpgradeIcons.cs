using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Optional icon per upgrade branch, and the fallback drawn when there is none
    /// (WP-005, closing the WP-004 hand-off note "UI icon reference field").
    ///
    /// WHY THE REFERENCE LIVES HERE AND NOT ON THE UPGRADE DEFINITION
    /// <c>UpgradeBranchSettings</c> is a gameplay record: cost, tiers, effect. Putting a
    /// <c>Sprite</c> on it would make the economy config depend on a UI asset type and
    /// on whatever framework draws it, and would touch the serialised config asset for
    /// a purely presentational field. So the definition stays as it is, the branch enum
    /// is the key, and art can register a sprite per branch here - from a loader, an
    /// addressable, or a hand-authored catalogue - without the shop knowing.
    ///
    /// THE FALLBACK IS A DESIGNED STATE, NOT A MISSING ONE
    /// A branch with no sprite draws a monogram tile: the same short code the build
    /// line already uses ("P", "FR", "RL" ...), set on a recessed disc. It is the same
    /// size, the same place and the same weight as a real icon would be, so a screen
    /// with three fallbacks and a screen with three icons have the same layout, and a
    /// missing asset can never move a card's text. No final icon is produced here;
    /// that is M3 art direction.
    /// </summary>
    public static class UpgradeIcons
    {
        /// <summary>
        /// Where art drops the M3 sprites: <c>Resources/UI/UpgradeIcons/&lt;Branch&gt;</c>, one
        /// sprite per <see cref="UpgradeBranch"/> name. Nothing lives there in M1; the
        /// loader registers whatever it finds and is silent about the rest.
        /// </summary>
        public const string ResourcesFolder = "UI/UpgradeIcons/";

        private static readonly Dictionary<UpgradeBranch, Sprite> s_sprites = new Dictionary<UpgradeBranch, Sprite>(12);
        private static bool s_loaded;

        /// <summary>
        /// Registers every sprite present under <see cref="ResourcesFolder"/>. Called once
        /// per session by the UI installer; safe to call again. Returns how many it found.
        /// </summary>
        public static int LoadFromResources()
        {
            if (s_loaded)
            {
                return s_sprites.Count;
            }

            s_loaded = true;

            var branches = (UpgradeBranch[])System.Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < branches.Length; i++)
            {
                // A missing asset is the expected case in M1: Resources.Load returns null
                // without logging, and the card draws its monogram.
                Sprite sprite = Resources.Load<Sprite>(ResourcesFolder + branches[i]);
                if (sprite == null)
                {
                    // WP-022: the AP-012 icon atlas ("Upgrade.<Branch>") is the M3 source;
                    // the per-branch folder stays the override for a hand-placed sprite.
                    sprite = UiArt.UpgradeIcon(branches[i]);
                }

                if (sprite != null)
                {
                    Register(branches[i], sprite);
                }
            }

            return s_sprites.Count;
        }

        /// <summary>Makes <paramref name="sprite"/> the icon for a branch. Null clears it.</summary>
        public static void Register(UpgradeBranch branch, Sprite sprite)
        {
            if (sprite == null)
            {
                s_sprites.Remove(branch);
                return;
            }

            s_sprites[branch] = sprite;
        }

        /// <summary>True and the sprite when art has registered one for the branch.</summary>
        public static bool TryGet(UpgradeBranch branch, out Sprite sprite)
        {
            return s_sprites.TryGetValue(branch, out sprite) && sprite != null;
        }

        public static bool Has(UpgradeBranch branch)
        {
            return TryGet(branch, out _);
        }

        /// <summary>
        /// One or two upper-case letters that stand in for the icon. Mirrors the build
        /// code in <c>WeaponModifierState.DescribeBuild</c> for the weapon branches so
        /// the card and the HUD build line agree; the utility branches get the same
        /// treatment.
        /// </summary>
        public static string Monogram(UpgradeBranch branch)
        {
            switch (branch)
            {
                case UpgradeBranch.Piercing: return "P";
                case UpgradeBranch.Ricochet: return "R";
                case UpgradeBranch.Explosion: return "X";
                case UpgradeBranch.Chain: return "C";
                case UpgradeBranch.FireRate: return "FR";
                case UpgradeBranch.ReloadSpeed: return "RL";
                case UpgradeBranch.MovementSpeed: return "MS";
                case UpgradeBranch.MaximumHealth: return "HP";
                case UpgradeBranch.RegenerationDelay: return "RD";
                case UpgradeBranch.RegenerationRate: return "RR";
                case UpgradeBranch.ArmorBreak: return "AB";   // WP-077 / WP-069 §6
                case UpgradeBranch.Impact: return "IM";
                case UpgradeBranch.Cluster: return "CL";
                default: return "?";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_sprites.Clear();
            s_loaded = false;
        }
    }
}
