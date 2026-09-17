using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Marks a piece of the world as stone, wood, bamboo, water and so on, so a bullet
    /// landing on it can pick the right impact burst and impact sound (step 5 of the
    /// twelve-step chain).
    ///
    /// Drop it on any collider or on a parent of several. The world task owns placing
    /// these; an unmarked collider simply resolves to <see cref="SurfaceKind.Default"/>
    /// and the prototype keeps working.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTag : MonoBehaviour
    {
        [SerializeField] private SurfaceKind _surface = SurfaceKind.Stone;

        /// <summary>
        /// WP-047 · The placement family this instance came from (AP005 world: written by Ap005SurfaceTags next
        /// to the surface). World instances are renamed AP005_iNNNN, so this is the only runtime handle on the
        /// family (lantern flames, breakable identities). Empty on hand-placed tags.
        /// </summary>
        public string Family = string.Empty;

        public SurfaceKind Surface
        {
            get => _surface;
            set
            {
                _surface = value;
                SurfaceResolver.Invalidate();
            }
        }
    }

    /// <summary>
    /// Collider to <see cref="SurfaceKind"/>, resolved once per collider and then
    /// answered from a dictionary.
    ///
    /// This exists because the request bans expensive per-frame lookups in a crowd. A
    /// hitscan weapon at 15 shots/s with 8 pellets asks this question 120 times a
    /// second; walking the hierarchy every time is exactly the mistake being avoided.
    /// The first sighting of a collider costs one GetComponentInParent, every later one
    /// costs an int-keyed dictionary probe and allocates nothing.
    /// </summary>
    public static class SurfaceResolver
    {
        private static readonly Dictionary<int, SurfaceKind> s_cache = new Dictionary<int, SurfaceKind>(256);
        private static bool s_hooked;

        /// <summary>Cached collider count. F1 readout.</summary>
        public static int CachedColliders => s_cache.Count;

        public static SurfaceKind Resolve(Collider collider)
        {
            if (collider == null)
            {
                return SurfaceKind.Default;
            }

            EnsureHooked();

            int key = collider.GetInstanceID();
            if (s_cache.TryGetValue(key, out SurfaceKind cached))
            {
                return cached;
            }

            SurfaceKind resolved = SurfaceKind.Default;
            SurfaceTag tag = collider.GetComponentInParent<SurfaceTag>();
            if (tag != null)
            {
                resolved = tag.Surface;
            }
            else if (collider.gameObject.layer == DD.Prototype.Core.PrototypeLayers.EnemyHitbox)
            {
                resolved = SurfaceKind.Flesh;
            }
            else
            {
                // WP-019: the AP library prefabs carry no SurfaceTag; their material role name
                // (M_<role>) says what they are made of. One walk per collider, then cached.
                resolved = FromMaterialRole(collider);
            }

            s_cache[key] = resolved;
            return resolved;
        }

        /// <summary>
        /// Material role → surface for the AP-002/003/003B/005 prefabs (ADR-012-2 names the
        /// materials M_&lt;role&gt;). The nearest renderer under the collider's prefab root wins;
        /// unknown roles stay Default (stone impact).
        /// </summary>
        public static SurfaceKind FromMaterialRole(Collider collider)
        {
            Transform t = collider.transform;
            Renderer r = t.GetComponentInChildren<Renderer>(true);
            if (r == null && t.parent != null)
            {
                r = t.parent.GetComponentInChildren<Renderer>(true);
            }

            if (r == null || r.sharedMaterial == null)
            {
                return SurfaceKind.Default;
            }

            return FromMaterialName(r.sharedMaterial.name);
        }

        /// <summary>Pure mapping for tests: "M_stone" → Stone, "M_wood"/"M_bark_02"/"M_bamboo_culm" → Wood, "M_earth_01"/"M_gravel"/"M_moss" → Dirt, foliage → Foliage, pond → Water.</summary>
        public static SurfaceKind FromMaterialName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return SurfaceKind.Default;
            }

            string n = materialName.StartsWith("M_", System.StringComparison.Ordinal) ? materialName.Substring(2) : materialName;
            if (n.StartsWith("stone", System.StringComparison.Ordinal) || n.StartsWith("plaster", System.StringComparison.Ordinal) || n.StartsWith("roof", System.StringComparison.Ordinal) ||
                n.StartsWith("prop_tsukubai", System.StringComparison.Ordinal) || n.StartsWith("prop_mossrock", System.StringComparison.Ordinal))
                return SurfaceKind.Stone;
            if (n.StartsWith("wood", System.StringComparison.Ordinal) || n.StartsWith("bark", System.StringComparison.Ordinal) || n.StartsWith("bamboo", System.StringComparison.Ordinal) ||
                n.StartsWith("prop_handcart", System.StringComparison.Ordinal) || n.StartsWith("prop_barrels", System.StringComparison.Ordinal) || n.StartsWith("prop_shed", System.StringComparison.Ordinal) || n.StartsWith("prop_strawbales", System.StringComparison.Ordinal))
                return SurfaceKind.Wood;
            if (n.StartsWith("earth", System.StringComparison.Ordinal) || n.StartsWith("gravel", System.StringComparison.Ordinal) || n.StartsWith("moss", System.StringComparison.Ordinal))
                return SurfaceKind.Dirt;
            if (n.StartsWith("green", System.StringComparison.Ordinal) || n.StartsWith("sakura", System.StringComparison.Ordinal))
                return SurfaceKind.Foliage;
            if (n.StartsWith("pond_water", System.StringComparison.Ordinal))
                return SurfaceKind.Water;
            return SurfaceKind.Default;
        }

        /// <summary>Drop the cache. Called on scene change and whenever a tag is edited at runtime.</summary>
        public static void Invalidate()
        {
            s_cache.Clear();
        }

        private static void EnsureHooked()
        {
            if (s_hooked)
            {
                return;
            }

            s_hooked = true;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnSceneChanged(Scene from, Scene to)
        {
            s_cache.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_hooked)
            {
                SceneManager.activeSceneChanged -= OnSceneChanged;
            }

            s_hooked = false;
            s_cache.Clear();
        }
    }
}
