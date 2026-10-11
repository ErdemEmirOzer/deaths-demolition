using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons.Attachments
{
    /// <summary>WP-104 B4 (S-06): the reload magazine visual — the weapon's own part, rl_1's rocket tube, or the E6 belt box.</summary>
    public enum MagazineVisualKind
    {
        Default = 0,
        Tube = 1,
        Box = 2,
    }

    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10) · the attachment lookups the weapon path asks (S-02 hooks: the muzzle flash / tracer origin
    /// and the reload magazine visual). Neutral until the B4 body lands: the muzzle as given; rl_1 a tube, every other weapon its part.
    /// </summary>
    public static class WeaponAttachments
    {
        /// <summary>Muzzle flash and tracer origin: the E5 power ring's muzzle when one is mounted, else <paramref name="muzzle"/>. No allocation.
        /// The hit ray's origin never moves (Wp087 aim alignment).</summary>
        public static Transform FlashOrigin(Transform muzzle) => muzzle;

        /// <summary>The reload magazine visual of <paramref name="id"/>: the E6 belt box when mounted (B4), rl_1's tube, else the weapon's part.</summary>
        public static MagazineVisualKind MagazineVisualFor(ClassicWeaponId id) => id == ClassicWeaponId.Rl1 ? MagazineVisualKind.Tube : MagazineVisualKind.Default;
    }
}
