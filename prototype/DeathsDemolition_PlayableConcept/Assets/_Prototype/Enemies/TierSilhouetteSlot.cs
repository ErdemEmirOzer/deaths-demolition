using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-028 · The version's silhouette piece under an AP-007 anchor. Until AP-014 delivers
    /// the meshes this is a placeholder primitive (one per body, pooled with it, tinted with
    /// the version colour through a property block - no material instance). AP-014 replaces
    /// <see cref="Apply"/>'s primitive with the delivered mesh at the same anchor; the slot,
    /// its anchor rule and the tint hook stay.
    /// </summary>
    public sealed class TierSilhouetteSlot : MonoBehaviour
    {
        public const string ObjectName = "TierSilhouette";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private TierSilhouetteAnchor _anchor = TierSilhouetteAnchor.None;
        private EnemyTier _tier = EnemyTier.Standard;

        public TierSilhouetteAnchor Anchor => _anchor;
        public EnemyTier Tier => _tier;
        public bool IsShown => _renderer != null && _renderer.enabled;

        /// <summary>Create (or fetch) the slot object under <paramref name="parent"/>.</summary>
        public static TierSilhouetteSlot Ensure(Transform parent)
        {
            if (parent == null) return null;
            Transform existing = parent.Find(ObjectName);
            if (existing != null)
            {
                TierSilhouetteSlot found = existing.GetComponent<TierSilhouetteSlot>();
                if (found != null) return found;
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = ObjectName;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);   // a look, never a hitbox (DEC-029: damage only through the attack window)
            go.transform.SetParent(parent, false);
            var slot = go.AddComponent<TierSilhouetteSlot>();
            slot._renderer = go.GetComponent<MeshRenderer>();
            slot._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            slot._renderer.enabled = false;
            return slot;
        }

        /// <summary>Show the piece for <paramref name="tier"/> per its profile; hide when the profile has none.</summary>
        public void Apply(EnemyTier tier, TierBehaviourProfile profile) => Apply(tier, profile, Vector3.zero);

        /// <summary><paramref name="basis"/>: extra local offset (the placeholder rig has no anchors; the piece hangs from the visual root).</summary>
        public void Apply(EnemyTier tier, TierBehaviourProfile profile, Vector3 basis)
        {
            _tier = tier;
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (profile == null || !profile.HasSilhouette)
            {
                Hide();
                return;
            }

            _anchor = profile.SilhouetteAnchor;
            transform.localPosition = basis + profile.SilhouetteOffset;
            transform.localRotation = Quaternion.identity;
            transform.localScale = profile.SilhouetteSize;
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, profile.Tint);
            _block.SetColor(ColorId, profile.Tint);
            _renderer.SetPropertyBlock(_block);
            _renderer.enabled = true;
        }

        public void Hide()
        {
            _anchor = TierSilhouetteAnchor.None;
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}
