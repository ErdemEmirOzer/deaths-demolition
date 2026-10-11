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
    /// and the reload magazine visual). With no attachment mounted they are neutral: the muzzle as given; rl_1 a tube, every other
    /// weapon its part. Allocation-free.
    /// </summary>
    public static class WeaponAttachments
    {
        /// <summary>The mount of the classic weapon in hand (<see cref="WeaponAttachPresenter"/> sets it), or null.</summary>
        public static WeaponAttachMount Active { get; set; }

        /// <summary>Muzzle flash and tracer origin: the E5 power ring's muzzle when one is mounted and shown on this weapon, else
        /// <paramref name="muzzle"/>. The hit ray's origin never moves (Wp087 aim alignment).</summary>
        public static Transform FlashOrigin(Transform muzzle)
        {
            WeaponAttachMount m = Active;
            if (m == null || !m.Shown || m.PowerMuzzle == null || muzzle == null) return muzzle;
            // the rig's Muzzle marker sits beside the model under the rig body (WeaponVisualRig.SetModel keeps it)
            Transform rigBody = m.transform.parent != null ? m.transform.parent : m.transform;
            return muzzle.IsChildOf(rigBody) ? m.PowerMuzzle : muzzle;
        }

        /// <summary>The reload magazine visual of <paramref name="id"/>: the E6 belt box while mounted (B4), rl_1's tube, else the weapon's part.</summary>
        public static MagazineVisualKind MagazineVisualFor(ClassicWeaponId id)
        {
            if (id != ClassicWeaponId.Rl1) return MagazineVisualKind.Default;
            WeaponAttachMount m = Active;
            return m != null && m.Shown && m.Row != null && m.Row.id == ClassicWeaponId.Rl1 && m.Set.Mag == 5 ? MagazineVisualKind.Box : MagazineVisualKind.Tube;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;
    }
}
