using System.Collections.Generic;
using DD.Prototype.Core.Pooling;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047 D · The AP-010B `lantern_flame` loop (4×4, 24 fps, 0.12 × 0.18 m) on the lanterns of the
    /// loaded world. Anchors are not authored yet (WP-029 look B / AP-017 prefabs), so the flame sits
    /// in the firebox of every lantern instance (SurfaceTag.Family, WP-047): bounds centre + 8 % of the extents.
    /// One scan per world load (allocating, off the hot path), at most <see cref="MaxFlames"/> flames,
    /// each a pooled looping card. The scan re-arms when the scene changes (renderer count differs).
    /// </summary>
    public sealed class LanternFlames
    {
        public const int MaxFlames = 40;
        public const float RescanInterval = 5f;
        private const float LoopSeconds = 3600f;
        /// <summary>The firebox window of the AP-003B stone lantern sits just above the bounds centre (0.6 put the flame inside the roof mass).</summary>
        public const float FireboxHeightFraction = 0.08f;
        /// <summary>×1.6 on the manifest 0.12 × 0.18 m so the flame still reads (~10 px) at the 20 m gameplay camera.</summary>
        public const float FlameScale = 1.6f;
        /// <summary>The greyscale flame sheet carries no tint in the manifest. AD M5 (WP-047): (1.0, 0.60, 0.28) × 0.9 — h ≈ 28° (≥ 12° off the gold orb's 41°), v 0.9, no HDR gain (bloom threshold 1.25; style guide §2.2 / §2.4).</summary>
        public static readonly Color FlameTint = new Color(1.0f * 0.9f, 0.60f * 0.9f, 0.28f * 0.9f, 1f);

        private readonly List<FlipbookEffect> _flames = new List<FlipbookEffect>(MaxFlames);
        private readonly List<Vector3> _anchors = new List<Vector3>(MaxFlames);
        private PrototypePool<FlipbookEffect> _cards;
        private Transform _root;
        private float _nextScan;
        private int _sceneStamp = -1;

        public int Placed => _flames.Count;
        public int Scans { get; private set; }

        /// <summary>Own pool (the shared flipbook pool must not be starved by 40 looping flames).</summary>
        public void Configure(Transform templateRoot, PrototypePool<FlipbookEffect> unused)
        {
            _root = templateRoot;
            if (_cards == null)
            {
                var go = new GameObject("FX_AP010B_LanternFlame");
                go.transform.SetParent(templateRoot, false);
                go.SetActive(false);
                _cards = FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(go.AddComponent<FlipbookEffect>(), MaxFlames, MaxFlames, PoolExhaustedPolicy.RecycleOldest));
            }
        }

        public void Tick(float now)
        {
            if (_cards == null || now < _nextScan)
            {
                return;
            }

            _nextScan = now + RescanInterval;
            // One scan per scene (the FindObjectsOfType arrays allocate — never on a steady frame, ADR-037-1);
            // a scene with no lanterns is remembered as such.
            int stamp = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle + UnityEngine.SceneManagement.SceneManager.sceneCount * 7919;
            if (stamp == _sceneStamp)
            {
                return;
            }

            _sceneStamp = stamp;
            Scan();
        }

        /// <summary>Find the lanterns and light them. Allocates (FindObjectsOfType) — once per world load.</summary>
        public int Scan()
        {
            Scans++;
            Clear();
            if (!VfxLibrary.TryGetPlayable(VfxIds.LanternFlame, out VfxCatalogEntry row))
            {
                return 0;
            }

            // AP005 world instances are renamed AP005_iNNNN (WP-014); the family survives on the SurfaceTag that
            // Ap005SurfaceTags writes on every instance root (Family, WP-047). Library / pilot scenes keep prefab names.
            SurfaceTag[] tags = Object.FindObjectsOfType<SurfaceTag>();
            for (int i = 0; i < tags.Length && _anchors.Count < MaxFlames; i++)
            {
                SurfaceTag tag = tags[i];
                if (tag == null || !tag.gameObject.activeInHierarchy) continue;
                bool lantern = tag.Family != null && tag.Family.IndexOf("lantern", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!lantern) lantern = NamedLantern(tag.transform);
                if (!lantern) continue;
                MeshRenderer r = tag.GetComponentInChildren<MeshRenderer>();
                if (r == null) continue;
                Bounds b = r.bounds;
                if (b.size.y < 0.4f) continue;   // a lantern fragment / decal, not the body
                _anchors.Add(new Vector3(b.center.x, b.center.y + b.extents.y * FireboxHeightFraction, b.center.z));
            }

            if (_anchors.Count == 0)
            {
                // No tags (pilot / library scene): fall back to prefab names on the renderer hierarchy.
                MeshRenderer[] renderers = Object.FindObjectsOfType<MeshRenderer>();
                for (int i = 0; i < renderers.Length && _anchors.Count < MaxFlames; i++)
                {
                    MeshRenderer r = renderers[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (!(NamedLantern(r.transform) || NamedLantern(r.transform.parent) || (r.transform.parent != null && NamedLantern(r.transform.parent.parent)))) continue;
                    if (r.name.IndexOf("LOD", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.name.IndexOf("LOD0", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Bounds b = r.bounds;
                    if (b.size.y < 0.4f) continue;
                    _anchors.Add(new Vector3(b.center.x, b.center.y + b.extents.y * FireboxHeightFraction, b.center.z));
                }
            }

            for (int i = 0; i < _anchors.Count; i++)
            {
                FlipbookEffect fx = _cards.Get(_anchors[i], Quaternion.identity);
                if (fx == null) break;
                fx.Play(row, FlipbookEffect.Facing.CameraYaw, row.WorldSizeMetres * FlameScale, FlameTint, 1f, 0f, LoopSeconds);
                _flames.Add(fx);
            }

            return _flames.Count;
        }

        private static bool NamedLantern(Transform t)
        {
            if (t == null) return false;
            string name = t.name;
            return name.IndexOf("lantern", System.StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("flame", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        public void Clear()
        {
            for (int i = 0; i < _flames.Count; i++)
            {
                if (_flames[i] != null && _flames[i].IsSpawned) _flames[i].Despawn();
            }

            _flames.Clear();
            _anchors.Clear();
        }
    }
}
