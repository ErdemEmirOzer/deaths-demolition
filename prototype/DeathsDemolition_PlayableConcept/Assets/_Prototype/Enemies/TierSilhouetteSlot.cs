using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-028 / WP-036 · The version's silhouette piece(s). Two modes on one component:
    ///   * <b>skinned</b> (WP-036, AP-014): up to two parts per version, each a SkinnedMeshRenderer
    ///     per LOD bound to the body's ZombieRig bones by name, hung from the AP-007 anchor the
    ///     manifest names (anchor_torso / socket_back), registered once per body with the LODGroup;
    ///     an unused renderer carries no mesh. Materials from <see cref="ZombieTierLibrary"/>.
    ///   * <b>placeholder</b> (WP-028): a pooled primitive under the anchor, used only when the
    ///     library is absent or the body is not skinned (M1 block body).
    /// Never a hitbox (no collider; DEC-029: damage only through the attack window).
    /// </summary>
    public sealed class TierSilhouetteSlot : MonoBehaviour
    {
        public const string ObjectName = "TierSilhouette";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const int LodCount = ZombieTierLibrary.LodCount;
        private const int MaxParts = ZombieTierLibrary.MaxPartsPerTier;

        private MeshRenderer _renderer;               // placeholder
        private MaterialPropertyBlock _block;
        private TierSilhouetteAnchor _anchor = TierSilhouetteAnchor.None;
        private EnemyTier _tier = EnemyTier.Standard;

        // skinned parts (WP-036): [part * LodCount + lod]
        private SkinnedMeshRenderer[] _partRenderers;
        private bool _lodRegistered;
        private readonly ZombieTierLibrary.Part[] _partBuffer = new ZombieTierLibrary.Part[MaxParts];
        private int _partCount;
        private string _fit = "";

        public TierSilhouetteAnchor Anchor => _anchor;
        public EnemyTier Tier => _tier;
        /// <summary>Placeholder primitive visible, or at least one skinned part carrying a mesh.</summary>
        public bool IsShown => (_renderer != null && _renderer.enabled) || _partCount > 0;
        /// <summary>WP-036: skinned AP-014 parts bound this life (0 = placeholder or none).</summary>
        public int SkinnedPartCount => _partCount;
        public string Fit => _fit;
        public SkinnedMeshRenderer PartRenderer(int part, int lod) => _partRenderers != null && part < MaxParts && lod < LodCount ? _partRenderers[part * LodCount + lod] : null;
        public ZombieTierLibrary.Part PartAt(int index) => index < _partCount ? _partBuffer[index] : null;

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

        /// <summary>Show the placeholder piece for <paramref name="tier"/> per its profile; hide when the profile has none.</summary>
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

        /// <summary>
        /// WP-036: bind the tier's AP-014 parts for this life (skinned body). Returns the number of
        /// parts bound (0 → caller falls back to the placeholder). Renderers are created on first
        /// use and registered with the body's LODGroup once; later lives only swap meshes.
        /// </summary>
        public int ApplySkinned(EnemyTier tier, ZombieTierLibrary library, ZombieSkinnedVisual skinned, string fit)
        {
            _tier = tier;
            _fit = fit ?? "";
            ClearSkinned();
            if (library == null || !library.IsReady || skinned == null || skinned.SkeletonRoot == null) return 0;
            _partCount = library.PartsOf(tier, _partBuffer);
            if (_partCount == 0) return 0;

            EnsureRenderers(skinned);
            for (int p = 0; p < _partCount; p++)
            {
                ZombieTierLibrary.Part part = _partBuffer[p];
                ZombieTierLibrary.Fit f = part.FitOf(_fit);
                Material m = library.PartMaterial(tier, part.materialIndex);
                for (int lod = 0; lod < LodCount; lod++)
                {
                    SkinnedMeshRenderer r = _partRenderers[p * LodCount + lod];
                    ZombieModuleLibrary.MeshRef mesh = f != null && f.lods != null && lod < f.lods.Length ? f.lods[lod] : null;
                    Bind(r, mesh, skinned);
                    if (m != null && r.sharedMaterial != m) r.sharedMaterial = m;
                }
            }

            if (_renderer != null) _renderer.enabled = false;   // the placeholder never shows next to real parts
            _anchor = TierSilhouetteAnchor.None;
            return _partCount;
        }

        public void Hide()
        {
            _anchor = TierSilhouetteAnchor.None;
            if (_renderer != null) _renderer.enabled = false;
            ClearSkinned();
        }

        private void ClearSkinned()
        {
            _partCount = 0;
            if (_partRenderers == null) return;
            for (int i = 0; i < _partRenderers.Length; i++)
            {
                SkinnedMeshRenderer r = _partRenderers[i];
                if (r != null && r.sharedMesh != null) { r.sharedMesh = null; r.enabled = false; }
            }
        }

        private void EnsureRenderers(ZombieSkinnedVisual skinned)
        {
            if (_partRenderers != null) return;
            _partRenderers = new SkinnedMeshRenderer[MaxParts * LodCount];
            Transform root = skinned.SkeletonRoot != null ? skinned.SkeletonRoot.parent ?? skinned.SkeletonRoot : transform;
            for (int p = 0; p < MaxParts; p++)
            {
                for (int lod = 0; lod < LodCount; lod++)
                {
                    var go = new GameObject("TierPart" + p + "_LOD" + lod);
                    go.transform.SetParent(root, false);
                    var r = go.AddComponent<SkinnedMeshRenderer>();
                    r.enabled = false;
                    r.updateWhenOffscreen = false;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.skinnedMotionVectors = false;
                    r.renderingLayerMask = ZombieSkinnedVisual.CharacterRenderingLayers;   // WP-038G: the character fill light lives on a Rendering Layer — runtime tier parts must join it or stay dark
                    _partRenderers[p * LodCount + lod] = r;
                }
            }

            RegisterLods(skinned.LodGroup);
        }

        /// <summary>Append the part renderers to the body's LOD levels (once per body; LODGroup then drives their enabled state).</summary>
        private void RegisterLods(LODGroup group)
        {
            if (_lodRegistered || group == null) return;
            LOD[] lods = group.GetLODs();
            int n = Mathf.Min(lods.Length, LodCount);
            for (int lod = 0; lod < n; lod++)
            {
                Renderer[] old = lods[lod].renderers ?? new Renderer[0];
                var next = new Renderer[old.Length + MaxParts];
                for (int i = 0; i < old.Length; i++) next[i] = old[i];
                for (int p = 0; p < MaxParts; p++) next[old.Length + p] = _partRenderers[p * LodCount + lod];
                lods[lod].renderers = next;
            }

            group.SetLODs(lods);
            _lodRegistered = true;
        }

        private static void Bind(SkinnedMeshRenderer renderer, ZombieModuleLibrary.MeshRef mesh, ZombieSkinnedVisual skinned)
        {
            if (renderer == null) return;
            if (mesh == null || mesh.mesh == null || mesh.boneNames == null)
            {
                renderer.sharedMesh = null;
                renderer.enabled = false;
                return;
            }

            var bones = new Transform[mesh.boneNames.Length];
            for (int i = 0; i < bones.Length; i++) bones[i] = skinned.Bone(mesh.boneNames[i]);
            renderer.sharedMesh = mesh.mesh;
            renderer.bones = bones;
            renderer.rootBone = skinned.Bone(mesh.rootBone) ?? skinned.SkeletonRoot;
            renderer.enabled = true;
        }
    }
}
