using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// One-shot logging for the navigation and spawn layers.
    ///
    /// Acceptance criterion 27 forbids console ERRORS during normal use, and the map
    /// (task G-06) does not exist yet, so every "the world is not ready" condition here
    /// is a WARNING that fires at most once per key. The same helper is used by the spawn
    /// layer so a missing NavMesh cannot spam the console for a whole run.
    /// </summary>
    public static class NavigationDiagnostics
    {
        private static readonly HashSet<string> s_seen = new HashSet<string>();
        private static readonly Dictionary<string, float> s_throttled = new Dictionary<string, float>();

        /// <summary>Warn once per key for the lifetime of the play session.</summary>
        public static void WarnOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key) || s_seen.Contains(key))
            {
                return;
            }

            s_seen.Add(key);
            Debug.LogWarning(message);
        }

        /// <summary>Warn at most once every <paramref name="interval"/> seconds per key.</summary>
        public static void WarnThrottled(string key, float interval, string message)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (s_throttled.TryGetValue(key, out float last) && now - last < interval)
            {
                return;
            }

            s_throttled[key] = now;
            Debug.LogWarning(message);
        }

        /// <summary>Allows a recovered condition (NavMesh appeared) to warn again if it breaks later.</summary>
        public static void Forget(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                s_seen.Remove(key);
                s_throttled.Remove(key);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_seen.Clear();
            s_throttled.Clear();
        }
    }
}
