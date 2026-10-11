using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Weapons.Attachments
{
    /// <summary>
    /// WP-104 B4 · the attachments one weapon shows. Mag 0 none / 1 E1 extended / 2–4 E2 drum S–L / 5 E6 belt box (rl_1);
    /// Twin 0 / 1–3 E3 S–L; Ring 0 / 1 E5 speed / 2 E5 chaos; Side the E4 element; the tiers drive S-07's glow ladder.
    /// </summary>
    public struct WeaponAttachSet : IEquatable<WeaponAttachSet>
    {
        public byte Mag, Twin, Ring, SideTier, RingTier;
        public TreeElement Side;

        public bool IsEmpty => Mag == 0 && Twin == 0 && Ring == 0 && Side == TreeElement.None;

        public bool Equals(WeaponAttachSet o) => Mag == o.Mag && Twin == o.Twin && Ring == o.Ring && Side == o.Side && SideTier == o.SideTier && RingTier == o.RingTier;
        public override bool Equals(object obj) => obj is WeaponAttachSet o && Equals(o);
        public override int GetHashCode() => Mag | (Twin << 3) | (Ring << 6) | ((int)Side << 9) | (SideTier << 13) | (RingTier << 16);
    }

    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §1, AD-VISUAL §1.4 / §2, C-00 Q5) · tree stats → attachments, in one place.
    /// Pure and allocation-free; called on tree / equip / camera events only.
    /// - Mag: rl_1 + full auto → E6 (AP-040 launcher policy; nothing else on the launcher); MagazineScale ≥ 3 (mg_double) → E2,
    ///   size by the Magazine tier (II S / III M / IV+ L); ≥ 1.5 (mg_ext) → E1.
    /// - Twin (E3): TwinBarrel, the size by the weapon (GD; C-00 Q5) — never on sg_2 (already over-under, Q3).
    /// - Ring (E5): from tier IV (AD; C-00 Q5) of Shot / Speed / Chaos, full auto off the launcher, or the endless kills;
    ///   Chaos IV+ picks the chaos ring.
    /// - Side (E4): one capsule (C-00 Q5) — the dominant element, its tier the Element branch's.
    /// </summary>
    public static class WeaponAttachRules
    {
        public const int RingFromTier = 4;

        public static WeaponAttachSet Resolve(in TreeStats s, ClassicWeaponId id, TreeElement dominant)
        {
            var a = new WeaponAttachSet();
            bool rocket = id == ClassicWeaponId.Rl1;
            int mag = s.BranchTier(TreeBranch.Magazine), speed = s.BranchTier(TreeBranch.Speed);
            int shot = s.BranchTier(TreeBranch.Shot), chaos = s.BranchTier(TreeBranch.Chaos);
            if (rocket) a.Mag = (byte)(s.FullAuto ? 5 : 0);
            else if (s.MagazineScale >= 2.99f) a.Mag = (byte)(1 + Mathf.Clamp(mag - 1, 1, 3));
            else if (s.MagazineScale >= 1.49f) a.Mag = 1;
            if (s.TwinBarrel && id != ClassicWeaponId.Sg2) a.Twin = TwinSize(id);
            if ((s.FullAuto && !rocket) || speed >= RingFromTier || shot >= RingFromTier || chaos >= RingFromTier || s.EndlessKills > 0)
            {
                a.Ring = (byte)(chaos >= RingFromTier ? 2 : 1);
                a.RingTier = (byte)Mathf.Max(Mathf.Max(speed, shot), Mathf.Max(chaos, mag));
            }

            a.Side = dominant;
            a.SideTier = (byte)(dominant != TreeElement.None ? s.BranchTier(TreeBranch.Element) : 0);
            return a;
        }

        /// <summary>E3 size by the weapon's length (GD-TREE; C-00 Q5): the long guns L, the short ones S, the rest M.</summary>
        public static byte TwinSize(ClassicWeaponId id) =>
            id == ClassicWeaponId.Lmg1 || id == ClassicWeaponId.Sr1 ? (byte)3
            : id == ClassicWeaponId.AutoSmg || id == ClassicWeaponId.Smg2 || id == ClassicWeaponId.CombatShotgun || id == ClassicWeaponId.Rl1 ? (byte)1
            : (byte)2;

        /// <summary>
        /// The element the single E4 capsule shows. S-04's TreeStats.DominantElement ("the highest-tier element, ties to the latest
        /// bought") is on its B1b branch, not on main yet; until it lands the first set flag in Fire → Ice → Shock → Venom order stands in.
        /// </summary>
        public static TreeElement DominantElement(in TreeStats s)
        {
            TreeElement e = s.Elements;
            if ((e & TreeElement.Fire) != 0) return TreeElement.Fire;
            if ((e & TreeElement.Ice) != 0) return TreeElement.Ice;
            if ((e & TreeElement.Shock) != 0) return TreeElement.Shock;
            if ((e & TreeElement.Venom) != 0) return TreeElement.Venom;
            return TreeElement.None;
        }

        /// <summary>E4 variant index of an element: fire 0, ice 1, shock 2, venom 3 (AP-040 element_capsule V01..V04); −1 for none.</summary>
        public static int ElementIndex(TreeElement e) =>
            e == TreeElement.Fire ? 0 : e == TreeElement.Ice ? 1 : e == TreeElement.Shock ? 2 : e == TreeElement.Venom ? 3 : -1;
    }

    /// <summary>WP-104 B4 · S-07's hook: the mounted attachments changed (the glow ladder, the element VFX socket, the twin muzzle flash).</summary>
    public static class WeaponAttachEvents
    {
        public static event Action<WeaponAttachMount> Changed;
        public static void RaiseChanged(WeaponAttachMount mount) => Changed?.Invoke(mount);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Changed = null;
    }
}
