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
        // C-00 01.10 GC finding (HitFeedbackDirector.Update 4224 B in Kalabalik40 after the V6 / FP-arms suites): the scene
        // stamp was only compared every RescanInterval, so a scene change was noticed up to 5 s late and the allocating Scan
        // (FindObjectsOfType + .name reads) landed in steady play. The stamp is now read every frame (0 B) and a changed scene
        // is scanned at once, then once more SettleFrames later (a world that finishes building over a few frames) — both
        // inside a scene's first half second, never on a steady frame. Scan has its own ProfilerMarker.
        public const int SettleFrames = 30;
        private static readonly Unity.Profiling.ProfilerMarker s_scanMarker = new Unity.Profiling.ProfilerMarker("LanternFlames.Scan");
        private int _settleFrame = -1;
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

        // ------------------------------------------------------------------ WP-047E · point-light budget (AD SHOULD 2, S-05 lighting budget)

        /// <summary>Lights follow the nearest lit lanterns: at most this many at once (URP per-object additional-light limit is 4 — WP-029 asset).</summary>
        public const int MaxLanternLights = 6;
        public const float LanternLightRange = 1.6f;
        public const float LanternLightIntensity = 0.35f;
        public const float LanternLightMaxCameraMetres = 26f;
        public const float LanternLightReassignSeconds = 0.5f;
        /// <summary>Off switch for the budget review (S-05); default on.</summary>
        public static bool PointLightsEnabled = true;

        private readonly List<Light> _lights = new List<Light>(MaxLanternLights);
        private readonly int[] _lightAnchor = new int[MaxLanternLights];
        private float _nextLightPass;

        public int LightsLit
        {
            get { int n = 0; for (int i = 0; i < _lights.Count; i++) if (_lights[i] != null && _lights[i].enabled) n++; return n; }
        }

        private void TickLights(float now)
        {
            if (!PointLightsEnabled || _anchors.Count == 0 || now < _nextLightPass) return;
            _nextLightPass = now + LanternLightReassignSeconds;
            if (!FeedbackCameraRef.TryGetPosition(out Vector3 camera)) return;

            if (_lights.Count == 0)
            {
                for (int i = 0; i < MaxLanternLights; i++)
                {
                    var go = new GameObject("LanternLight");
                    go.transform.SetParent(_root, false);
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = LanternLightRange;
                    l.intensity = LanternLightIntensity;
                    l.color = new Color(FlameTint.r, FlameTint.g, FlameTint.b, 1f);
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    _lights.Add(l);
                    _lightAnchor[i] = -1;
                }
            }

            // Nearest anchors within reach, by insertion sort into the light slots (≤ 40 anchors × 6 slots, no allocation).
            float max2 = LanternLightMaxCameraMetres * LanternLightMaxCameraMetres;
            for (int i = 0; i < MaxLanternLights; i++) _lightAnchor[i] = -1;
            for (int a = 0; a < _anchors.Count; a++)
            {
                float d = (_anchors[a] - camera).sqrMagnitude;
                if (d > max2) continue;
                for (int slot = 0; slot < MaxLanternLights; slot++)
                {
                    int cur = _lightAnchor[slot];
                    if (cur >= 0 && (_anchors[cur] - camera).sqrMagnitude <= d) continue;
                    for (int k = MaxLanternLights - 1; k > slot; k--) _lightAnchor[k] = _lightAnchor[k - 1];
                    _lightAnchor[slot] = a;
                    break;
                }
            }

            for (int i = 0; i < MaxLanternLights; i++)
            {
                Light l = _lights[i];
                if (l == null) continue;
                int a = _lightAnchor[i];
                if (a < 0) { l.enabled = false; continue; }
                l.transform.position = _anchors[a] + Vector3.up * 0.08f;
                l.enabled = true;
            }
        }

        public const float FlickerHz = 20f;
        public const float FlickerAmplitude = 0.10f;
        private float _nextFlicker;

        /// <summary>AD SHOULD (WP-047E): ±10 % alpha flicker per flame (phase by index) so the point lights read as fire, not lamps.</summary>
        private void TickFlicker(float now)
        {
            if (_flames.Count == 0 || now < _nextFlicker) return;
            _nextFlicker = now + 1f / FlickerHz;
            for (int i = 0; i < _flames.Count; i++)
            {
                FlipbookEffect f = _flames[i];
                if (f == null || !f.IsSpawned) continue;
                float a = 1f - FlickerAmplitude * (0.5f + 0.5f * Mathf.Sin(now * 9.7f + i * 1.7f) * Mathf.Sin(now * 23.1f + i * 0.9f));
                f.SetAlphaCap(a);
            }
        }

        public void Tick(float now)
        {
            TickFlicker(now);
            TickLights(now);
            if (_cards == null)
            {
                return;
            }

            // One scan per scene + one settle scan (the FindObjectsOfType arrays allocate — never on a steady frame, ADR-037-1);
            // a scene with no lanterns is remembered as such.
            int stamp = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle + UnityEngine.SceneManagement.SceneManager.sceneCount * 7919;
            if (stamp != _sceneStamp)
            {
                _sceneStamp = stamp;
                _settleFrame = Time.frameCount + SettleFrames;
                using (s_scanMarker.Auto()) Scan();
                return;
            }

            if (_settleFrame >= 0 && Time.frameCount >= _settleFrame)
            {
                _settleFrame = -1;
                using (s_scanMarker.Auto()) Scan();
            }
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
            for (int i = 0; i < _lights.Count; i++) if (_lights[i] != null) _lights[i].enabled = false;
        }
    }
}
