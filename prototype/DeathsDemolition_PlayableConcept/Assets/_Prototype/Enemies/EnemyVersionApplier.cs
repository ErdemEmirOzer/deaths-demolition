using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-028 · Applies a <see cref="TierBehaviourProfile"/> to a body: the armour tendency at
    /// the spawn roll, the attack timer scales after the config read, the silhouette slot
    /// under the profile's anchor. Pure helpers, shared with the Editor simulator so the
    /// reported odds are the odds the roll uses.
    /// </summary>
    public static class EnemyVersionApplier
    {
        /// <summary>The equip chance the spawn roll uses: the DEC-021 value (<c>config.ArmorChance</c>, unchanged) through the tier's tendency.</summary>
        public static float ArmourChance(PrototypeConfig config, TierBehaviourProfile profile, EnemyTier tier, ArmorSlot slot, int round)
        {
            if (config == null) return 0f;
            float baseChance = config.ArmorChance(tier, slot, round);
            return profile != null ? profile.ArmourChance(slot, baseChance) : Mathf.Clamp01(baseChance);
        }

        /// <summary>The anchor transform a silhouette piece hangs from (null = none / no anchors on this body).</summary>
        public static Transform AnchorFor(in ArmorAnchors anchors, TierSilhouetteAnchor anchor)
        {
            switch (anchor)
            {
                case TierSilhouetteAnchor.Head: return anchors.Helmet;
                case TierSilhouetteAnchor.Shoulder:
                case TierSilhouetteAnchor.Back: return anchors.Body;
                default: return null;
            }
        }

        /// <summary>Show / hide the body's silhouette slot for the tier. Returns the slot (null when the body has no usable anchor).</summary>
        public static TierSilhouetteSlot ApplySilhouette(TierSilhouetteSlot slot, in ArmorAnchors anchors, EnemyTier tier, TierBehaviourProfile profile, Transform fallbackParent) =>
            ApplySilhouette(slot, in anchors, tier, profile, fallbackParent, null);

        /// <summary>
        /// WP-036: with a skinned body and the AP-014 library, the tier's real parts are bound (skinned to the
        /// body's bones, LODGroup-registered) and the WP-028 placeholder never shows; otherwise the placeholder rule.
        /// </summary>
        public static TierSilhouetteSlot ApplySilhouette(TierSilhouetteSlot slot, in ArmorAnchors anchors, EnemyTier tier, TierBehaviourProfile profile, Transform fallbackParent, ZombieAppearance appearance)
        {
            ZombieTierLibrary library = ZombieTierLibrary.Active;
            if (library != null && library.IsReady && appearance != null && appearance.IsSkinned && appearance.Skinned != null)
            {
                Transform skinnedAnchor = AnchorFor(anchors, profile != null ? profile.SilhouetteAnchor : TierSilhouetteAnchor.None);
                Transform slotParent = skinnedAnchor != null ? skinnedAnchor : fallbackParent;
                if (slot == null && slotParent != null) slot = TierSilhouetteSlot.Ensure(slotParent);
                if (slot != null)
                {
                    int bound = slot.ApplySkinned(tier, library, appearance.Skinned, appearance.Look.fit);
                    if (bound > 0 || tier == EnemyTier.Standard) return slot;   // parts bound, or a version without a part (Standard) - no placeholder either way
                }
            }

            if (profile == null || !profile.HasSilhouette)
            {
                slot?.Hide();
                return slot;
            }

            Transform anchorT = AnchorFor(anchors, profile.SilhouetteAnchor);
            Transform parent = anchorT != null ? anchorT : fallbackParent;
            if (parent == null)
            {
                slot?.Hide();
                return slot;
            }

            // The placeholder rig (no AP-007 anchors) hangs the piece from the visual root at a body-height basis.
            Vector3 basis = anchorT != null ? Vector3.zero : FallbackBasis(profile.SilhouetteAnchor);

            if (slot == null || slot.transform.parent != parent)
            {
                if (slot != null) slot.Hide();
                slot = TierSilhouetteSlot.Ensure(parent);
            }

            slot.Apply(tier, profile, basis);
            return slot;
        }

        private static Vector3 FallbackBasis(TierSilhouetteAnchor anchor)
        {
            switch (anchor)
            {
                case TierSilhouetteAnchor.Head: return new Vector3(0f, ZombieBodyBuilder.StandHeight * 0.92f, 0f);
                case TierSilhouetteAnchor.Shoulder:
                case TierSilhouetteAnchor.Back: return new Vector3(0f, ZombieBodyBuilder.StandHeight * 0.70f, 0f);
                default: return Vector3.zero;
            }
        }
    }
}
