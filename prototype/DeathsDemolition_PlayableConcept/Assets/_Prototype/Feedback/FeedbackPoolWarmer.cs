using System.Collections.Generic;
using DD.Prototype.Core.Pooling;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// WP-047 (S-02 WP-053 finding, ADR-037-1): a prewarmed pool instance is inactive until its first Get, so its
    /// <c>Awake</c> — the flipbook / decal quad child, the property block, the TextMesh lookup — ran inside the first
    /// hit or explosion frame that used it (≈ 12 allocations each; 128 blood marks' worth over a round). Every
    /// prototype feedback pool is activated once here at configure time: Get everything, Release everything.
    /// </summary>
    public static class FeedbackPoolWarmer
    {
        private static readonly List<PooledBehaviour> s_scratch = new List<PooledBehaviour>(128);

        public static PrototypePool<T> Warm<T>(PrototypePool<T> pool) where T : PooledBehaviour
        {
            if (pool == null)
            {
                return null;
            }

            s_scratch.Clear();
            int target = pool.CountTotal;
            for (int i = 0; i < target; i++)
            {
                T instance = pool.Get(Vector3.zero, Quaternion.identity);
                if (instance == null)
                {
                    break;
                }

                s_scratch.Add(instance);
            }

            for (int i = 0; i < s_scratch.Count; i++)
            {
                pool.Release((T)s_scratch[i]);
            }

            s_scratch.Clear();
            return pool;
        }
    }
}
