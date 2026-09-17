using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · Family 6 on a gold packet: the AP-010 pouch mesh (albedo + HDR emissive
    /// coins so bloom lifts them), a pulsing additive sparkle card above it, and — while
    /// the packet is being pulled — the trail card stretched from the pouch towards the
    /// player (REQ-014, manifest: 1.0 × 0.15 m, head at the pouch, alpha ≤ 0.6).
    /// Attached by <see cref="GoldOrb"/> in place of its WP-004 sphere; the orb keeps
    /// owning scale, spin and lifetime. No per-instance material: tint/alpha by property block.
    /// </summary>
    public sealed class GoldPackVisual : MonoBehaviour
    {
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        private MeshRenderer _sparkle;
        private Transform _sparkleT;
        private MeshRenderer _trail;
        private Transform _trailT;
        private MaterialPropertyBlock _block;
        private float _clock;
        private bool _pulling;
        private Vector3 _pullTarget;

        public bool IsPulling => _pulling;

        /// <summary>
        /// Builds the pouch under <paramref name="parent"/>. Returns null when the catalog has no
        /// gold pack, so the caller keeps its own visual.
        /// </summary>
        public static GoldPackVisual TryAttach(Transform parent, out MeshRenderer pouchRenderer)
        {
            pouchRenderer = null;
            if (!VfxLibrary.TryGetPlayable(VfxIds.GoldPackMesh, out VfxCatalogEntry pack) || pack.Mesh == null)
            {
                return null;
            }

            var go = new GameObject("Visual");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = pack.Mesh;
            pouchRenderer = go.AddComponent<MeshRenderer>();
            pouchRenderer.sharedMaterial = pack.Material;
            pouchRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pouchRenderer.receiveShadows = true;
            pouchRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var visual = go.AddComponent<GoldPackVisual>();
            visual.Build();
            return visual;
        }

        private void Build()
        {
            _block = new MaterialPropertyBlock();
            if (VfxLibrary.TryGetPlayable(VfxIds.CardGoldSparkle, out VfxCatalogEntry sparkle))
            {
                _sparkleT = Card("Sparkle", sparkle.Material, sparkle.WorldSizeMetres, out _sparkle);
                _sparkleT.localPosition = new Vector3(0f, 0.16f, 0f);
            }

            if (VfxLibrary.TryGetPlayable(VfxIds.CardGoldTrail, out VfxCatalogEntry trail))
            {
                _trailT = Card("Trail", trail.Material, 1f, out _trail);
                _trail.enabled = false;
            }
        }

        private Transform Card(string name, Material material, float size, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform.parent, false);   // sibling of the pouch: the orb scales the pouch, not the cards
            go.transform.localScale = new Vector3(size, size, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = VfxLibrary.Quad;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// <summary>Called by the orb every frame it is being pulled; <paramref name="target"/> is the player position.</summary>
        public void SetPulling(bool pulling, Vector3 target)
        {
            _pulling = pulling;
            _pullTarget = target;
            if (_trail != null)
            {
                _trail.enabled = pulling;
            }
        }

        private void OnDisable()
        {
            _pulling = false;
            if (_trail != null) _trail.enabled = false;
        }

        private void Update()
        {
            _clock += Time.deltaTime;
            bool haveViewer = FeedbackCameraRef.TryGetPosition(out Vector3 viewer);

            if (_sparkle != null && haveViewer)
            {
                // Manifest: glow 0.6 m pulsing scale 0.85–1.0 — the card's soft glow fills the 0.6 m; the
                // star core reads at ~0.12 m. AD fix 2 (v002): 0.45 m card, alpha 0.3–0.45; AD M8 (v003 card, 6–8 stars baked): alpha 0.5–0.7 so the stars peak.
                float pulse = 0.925f + 0.075f * Mathf.Sin(_clock * 4.2f);
                _sparkleT.localScale = new Vector3(0.45f * pulse, 0.45f * pulse, 1f);
                Vector3 to = viewer - _sparkleT.position;
                if (to.sqrMagnitude > 1e-4f)
                {
                    _sparkleT.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up);
                }

                _block.SetColor(TintId, VfxLibrary.GoldTint);
                _block.SetFloat(AlphaId, 0.60f + 0.10f * Mathf.Sin(_clock * 6.1f));
                _sparkle.SetPropertyBlock(_block);
            }

            if (_pulling && _trail != null && haveViewer)
            {
                Vector3 from = transform.parent.position;
                Vector3 dir = _pullTarget - from;
                float len = dir.magnitude;
                if (len > 0.05f)
                {
                    // Card v=1 is the head (at the pouch): the quad's +Y points to the pouch, so it
                    // is centred half a length AHEAD of the pouch along the pull, rotated to look at the viewer.
                    dir /= len;
                    float length = Mathf.Min(1.0f, len);
                    _trailT.position = from + dir * (length * 0.5f);
                    Vector3 toViewer = viewer - _trailT.position;
                    _trailT.rotation = Quaternion.LookRotation(-toViewer.normalized, -dir);
                    _trailT.localScale = new Vector3(0.15f, length, 1f);
                    _block.SetColor(TintId, VfxLibrary.GoldTint);
                    _block.SetFloat(AlphaId, 0.6f);
                    _trail.SetPropertyBlock(_block);
                }
            }
        }
    }
}
