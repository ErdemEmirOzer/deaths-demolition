using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Core.Pooling
{
    /// <summary>
    /// Owns every pool in the build and the DontDestroyOnLoad transform they live under.
    ///
    /// Pools are keyed by prefab instance id, so two systems asking for a pool of the
    /// same prefab share one pool. Scene changes automatically return every live
    /// instance; leaving a run entirely should call <see cref="DestroyAll"/>.
    /// </summary>
    public static class PoolService
    {
        private const string RootName = "[Prototype Pools]";

        private static readonly Dictionary<int, IPrototypePool> s_pools = new Dictionary<int, IPrototypePool>(32);
        private static readonly List<IPrototypePool> s_poolList = new List<IPrototypePool>(32);
        private static Transform s_root;
        private static bool s_hooked;

        /// <summary>Parent of every pooled instance. Survives scene loads.</summary>
        public static Transform Root
        {
            get
            {
                if (s_root == null)
                {
                    var go = new GameObject(RootName);
                    Object.DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.NotEditable;
                    s_root = go.transform;
                }

                return s_root;
            }
        }

        /// <summary>Every registered pool, for the F1 panel and for budget reporting.</summary>
        public static IReadOnlyList<IPrototypePool> Pools => s_poolList;

        /// <summary>
        /// Fetch the shared pool for a prefab, creating it on first use.
        /// Call this once in Awake and keep the reference; never call it per spawn.
        /// </summary>
        public static PrototypePool<T> GetOrCreate<T>(
            T prefab,
            int prewarm,
            int capacity,
            PoolExhaustedPolicy policy = PoolExhaustedPolicy.ReturnNull) where T : PooledBehaviour
        {
            if (prefab == null)
            {
                Debug.LogError("[PoolService] GetOrCreate called with a null prefab.");
                return null;
            }

            EnsureHooked();

            int key = prefab.GetInstanceID();
            if (s_pools.TryGetValue(key, out IPrototypePool existing))
            {
                var typed = existing as PrototypePool<T>;
                if (typed != null)
                {
                    if (capacity > typed.Capacity)
                    {
                        typed.SetCapacity(capacity);
                    }

                    typed.Prewarm(prewarm);
                    return typed;
                }

                Debug.LogError($"[PoolService] Prefab '{prefab.name}' is already pooled as a different type.");
                return null;
            }

            var pool = new PrototypePool<T>(prefab, Root, prewarm, capacity, policy);
            s_pools.Add(key, pool);
            s_poolList.Add(pool);
            return pool;
        }

        public static bool TryGet<T>(T prefab, out PrototypePool<T> pool) where T : PooledBehaviour
        {
            pool = null;
            if (prefab == null)
            {
                return false;
            }

            if (s_pools.TryGetValue(prefab.GetInstanceID(), out IPrototypePool existing))
            {
                pool = existing as PrototypePool<T>;
            }

            return pool != null;
        }

        /// <summary>Return every live instance in every pool. Keeps the instances alive.</summary>
        public static void ReleaseAllSpawned()
        {
            for (int i = 0; i < s_poolList.Count; i++)
            {
                s_poolList[i].ReleaseAllSpawned();
            }
        }

        /// <summary>Destroy every instance in every pool and forget the pools.</summary>
        public static void DestroyAll()
        {
            for (int i = 0; i < s_poolList.Count; i++)
            {
                s_poolList[i].DestroyAll();
            }

            s_poolList.Clear();
            s_pools.Clear();
        }

        private static void EnsureHooked()
        {
            if (s_hooked)
            {
                return;
            }

            s_hooked = true;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            // Pooled instances live under a DontDestroyOnLoad root, so they survive the
            // load. Anything still spawned belongs to the scene that just went away.
            ReleaseAllSpawned();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_hooked)
            {
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            }

            s_pools.Clear();
            s_poolList.Clear();
            s_root = null;
            s_hooked = false;
        }
    }
}
