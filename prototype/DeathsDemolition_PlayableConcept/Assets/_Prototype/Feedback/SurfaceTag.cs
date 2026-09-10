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

            s_cache[key] = resolved;
            return resolved;
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
