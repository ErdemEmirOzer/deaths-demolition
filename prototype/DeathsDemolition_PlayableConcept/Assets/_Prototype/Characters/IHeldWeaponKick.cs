using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-087 (C-00 06.10, S-02 with S-06 · council K/wp-087/konsey/KARAR-ArmsCarryKick-06.10.md): the held weapon's kick that the
    /// weapon-first hold (<see cref="WeaponHoldIk"/>) carries at its grip pivot, so the arms take it and the weapon stays in both
    /// hands. The source (S-02's WeaponVisualRig) publishes it; the hold applies it; WeaponController hands the hold the active
    /// weapon's source (Classic or Special — never found in the hierarchy, both rigs sit under one mount).
    /// </summary>
    public interface IHeldWeaponKick
    {
        /// <summary>0–1: the share of the kick the arms carry (the hold's weight); the body inside the rig keeps 1 − share.</summary>
        float ArmsCarryKick { get; }

        /// <summary>
        /// The arms' kick in the hold pivot's frame (x right, y up, z along the aim): (0, 0, −back) m, back ≥ 0 — one way, capped,
        /// NOT multiplied by <see cref="ArmsCarryKick"/> (the hold blends by its own weight).
        /// </summary>
        Vector3 KickLocalOffset { get; }

        /// <summary>The arms' muzzle rise about the pivot's +X (Euler(−up°, 0, 0), up ≥ 0) — one way, capped, not × share.</summary>
        Quaternion KickLocalRotation { get; }
    }
}
