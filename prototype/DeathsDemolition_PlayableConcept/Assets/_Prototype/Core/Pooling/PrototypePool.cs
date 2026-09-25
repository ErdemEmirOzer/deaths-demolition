using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Pooling
{
    /// <summary>
    /// Typed object pool. Zero allocation on Get and Release once prewarmed:
    /// the free list is a Stack, the live list is a List, and no lookup, no
    /// GetComponent and no Find happens on either path.
    ///
    /// Created through <see cref="PoolService.GetOrCreate{T}"/>, not directly.
    /// </summary>
    public sealed class PrototypePool<T> : IPrototypePool where T : PooledBehaviour
    {
        private readonly T _prefab;
        private readonly Transform _root;
        private readonly Stack<T> _available;
        private readonly List<T> _spawned;
        private readonly bool _prefabWasActive;

        private int _capacity;
        private bool _grewWarningLogged;

        internal PrototypePool(T prefab, Transform root, int prewarm, int capacity, PoolExhaustedPolicy policy)
        {
            _prefab = prefab;
            _root = root;
            _capacity = Mathf.Max(1, capacity);
            ExhaustedPolicy = policy;
            Name = prefab != null ? prefab.name : "<null prefab>";
            _prefabWasActive = prefab != null && prefab.gameObject.activeSelf;

            _available = new Stack<T>(_capacity);
            _spawned = new List<T>(_capacity);

            Prewarm(prewarm);
        }

        public string Name { get; }
        public int CountSpawned => _spawned.Count;
        public int CountAvailable => _available.Count;
        public int Capacity => _capacity;
        public int CountTotal => _spawned.Count + _available.Count;
        public PoolExhaustedPolicy ExhaustedPolicy { get; }

        /// <summary>Live instances. Read-only; do not mutate while iterating.</summary>
        public IReadOnlyList<T> Spawned => _spawned;

        /// <summary>Raise the ceiling at runtime, e.g. when a live config edit widens a budget.</summary>
        public void SetCapacity(int capacity)
        {
            _capacity = Mathf.Max(1, capacity);
        }

        public void Prewarm(int count)
        {
            PurgeDestroyed();
            int target = Mathf.Min(count, _capacity);
            while (CountTotal < target)
            {
                T instance = CreateInstance();
                if (instance == null)
                {
                    return;
                }

                instance.gameObject.SetActive(false);
                _available.Push(instance);
            }
        }

        /// <summary>
        /// WP-069 (CI cb78bfb4): drop entries destroyed behind the pool's back — an instance parented to something that died
        /// with its scene (a muzzle flash on the weapon), a test's cleanup — so <see cref="CountTotal"/> counts live
        /// instances and <see cref="Prewarm"/> can refill. Load-time call; allocates only when the free list holds a dead entry.
        /// </summary>
        public int PurgeDestroyed()
        {
            int removed = 0;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] == null)
                {
                    _spawned.RemoveAt(i);
                    removed++;
                }
            }

            bool deadFree = false;
            foreach (T candidate in _available)
            {
                if (candidate == null)
                {
                    deadFree = true;
                    break;
                }
            }

            if (deadFree)
            {
                T[] entries = _available.ToArray();   // top first
                _available.Clear();
                for (int i = entries.Length - 1; i >= 0; i--)
                {
                    if (entries[i] != null) _available.Push(entries[i]);
                    else removed++;
                }
            }

            return removed;
        }

        public T Get(Vector3 position, Quaternion rotation)
        {
            return Get(position, rotation, null);
        }

        /// <summary>
        /// Check an instance out. Position and rotation are applied before the object is
        /// activated, so OnSpawned and OnEnable already see the final transform.
        /// </summary>
        public T Get(Vector3 position, Quaternion rotation, Transform parent)
        {
            T instance = TakeAvailable();

            if (instance == null)
            {
                if (CountTotal < _capacity)
                {
                    instance = CreateInstance();
                }
                else
                {
                    instance = HandleExhausted();
                }
            }

            if (instance == null)
            {
                return null;
            }

            Transform t = instance.transform;
            if (parent != null)
            {
                t.SetParent(parent, false);
            }
            else if (t.parent != _root)
            {
                t.SetParent(_root, false);
            }

            t.SetPositionAndRotation(position, rotation);

            _spawned.Add(instance);
            instance.gameObject.SetActive(true);
            instance.InvokeSpawned();
            return instance;
        }

        public void Release(T instance)
        {
            if (instance == null || !instance.IsSpawned)
            {
                return;
            }

            int index = _spawned.IndexOf(instance);
            if (index >= 0)
            {
                _spawned.RemoveAt(index);
            }

            ReturnToFreeList(instance);
        }

        void IPrototypePool.ReleaseObject(PooledBehaviour instance)
        {
            Release(instance as T);
        }

        public void ReleaseAllSpawned()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                ReturnToFreeList(_spawned[i]);
            }

            _spawned.Clear();
        }

        public void DestroyAll()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                DestroyInstance(_spawned[i]);
            }

            _spawned.Clear();

            while (_available.Count > 0)
            {
                DestroyInstance(_available.Pop());
            }
        }

        // ------------------------------------------------------------------ internals

        private T TakeAvailable()
        {
            while (_available.Count > 0)
            {
                T candidate = _available.Pop();
                if (candidate != null)
                {
                    return candidate;
                }
                // Destroyed behind the pool's back (scene teardown). Skip it.
            }

            return null;
        }

        private T CreateInstance()
        {
            if (_prefab == null)
            {
                Debug.LogError($"[Pool] '{Name}' has no prefab.");
                return null;
            }

            // Instantiate inactive so Awake/OnEnable do not run before the pool owns it.
            if (_prefabWasActive)
            {
                _prefab.gameObject.SetActive(false);
            }

            T instance = Object.Instantiate(_prefab, _root);

            if (_prefabWasActive)
            {
                _prefab.gameObject.SetActive(true);
            }

            instance.name = Name;
            instance.OwnerPool = this;
            instance.IsSpawned = false;
            return instance;
        }

        private T HandleExhausted()
        {
            switch (ExhaustedPolicy)
            {
                case PoolExhaustedPolicy.RecycleOldest:
                    if (_spawned.Count == 0)
                    {
                        return null;
                    }

                    T oldest = _spawned[0];
                    _spawned.RemoveAt(0);
                    ReturnToFreeList(oldest);
                    return TakeAvailable();

                case PoolExhaustedPolicy.GrowWithWarning:
                    if (!_grewWarningLogged)
                    {
                        _grewWarningLogged = true;
                        Debug.LogWarning(
                            $"[Pool] '{Name}' exceeded its capacity of {_capacity}. Growing. " +
                            "This is a budget problem, not a pool problem.");
                    }

                    return CreateInstance();

                default:
                    return null;
            }
        }

        private void ReturnToFreeList(T instance)
        {
            if (instance == null)
            {
                return;
            }

            instance.InvokeDespawned();

            if (instance == null)
            {
                return;
            }

            instance.gameObject.SetActive(false);

            Transform t = instance.transform;
            if (t.parent != _root)
            {
                t.SetParent(_root, false);
            }

            _available.Push(instance);
        }

        private static void DestroyInstance(T instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(instance.gameObject);
            }
            else
            {
                Object.DestroyImmediate(instance.gameObject);
            }
        }
    }
}
