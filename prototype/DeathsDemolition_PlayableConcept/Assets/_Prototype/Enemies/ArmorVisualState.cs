using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// The four visual states one armour slot can be in (REQ-063, REQ-129). Driven by the
    /// gameplay <c>ArmorSet</c>, never read back from the mesh: the mesh follows the model.
    /// </summary>
    public enum ArmorVisualState
    {
        /// <summary>The slot was never equipped for this life. Nothing is shown.</summary>
        Absent = 0,

        /// <summary>Equipped and at full durability.</summary>
        Present = 1,

        /// <summary>Equipped, durability below full. The attachment shows wear proportional to <c>ratio01</c>.</summary>
        Damaged = 2,

        /// <summary>Equipped, durability spent. The plate is gone and the under-cloth shows (REQ-063).</summary>
        Broken = 3
    }

    /// <summary>
    /// THE ATTACHMENT / STATE CONTRACT for the three Japanese armour pieces (REQ-115):
    /// kabuto or mask (Helmet), dō/kote (Body), haidate/suneate (Leg).
    ///
    /// The enemy calls <see cref="Apply"/> whenever a slot's model state changes; the
    /// visual decides how to show it. Today the implementation is the WP-002 placeholder
    /// plate groups on the procedural body (<see cref="ZombieAppearance"/>); the final
    /// art (M3) binds a real prefab per slot through <see cref="ArmorAnchors"/> and
    /// implements the same four states. Nothing in gameplay names a prefab, a mesh or a
    /// bone: the contract is the enum, the ratio and the anchor transforms.
    ///
    /// Rules a future implementation must keep:
    ///   * <see cref="Apply"/> is idempotent - applying the same state twice changes nothing;
    ///   * it never allocates per call (state changes happen on every armour hit);
    ///   * <see cref="StateOf"/> answers from what was last applied, not from the scene.
    /// </summary>
    public interface IArmorVisual
    {
        /// <summary>Show <paramref name="slot"/> in <paramref name="state"/>; <paramref name="ratio01"/> is the durability fraction (1 = full) for Damaged.</summary>
        void Apply(ArmorSlot slot, ArmorVisualState state, float ratio01);

        /// <summary>The state last applied to the slot. Absent until the first Apply after a reset.</summary>
        ArmorVisualState StateOf(ArmorSlot slot);
    }

    /// <summary>
    /// Where each armour piece attaches on the body. Read-only view over the rig's
    /// anchor transforms so the art pipeline can parent a real kabuto / dō / haidate
    /// under the right joint without touching gameplay code.
    /// </summary>
    public readonly struct ArmorAnchors
    {
        public ArmorAnchors(Transform helmet, Transform body, Transform leg)
        {
            Helmet = helmet;
            Body = body;
            Leg = leg;
        }

        /// <summary>Head anchor: kabuto / mask.</summary>
        public Transform Helmet { get; }

        /// <summary>Chest anchor: dō (and the kote that hang from it).</summary>
        public Transform Body { get; }

        /// <summary>Hip anchor: haidate / suneate.</summary>
        public Transform Leg { get; }

        public Transform For(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return Helmet;
                case ArmorSlot.Body: return Body;
                case ArmorSlot.Leg: return Leg;
                default: return null;
            }
        }
    }
}
