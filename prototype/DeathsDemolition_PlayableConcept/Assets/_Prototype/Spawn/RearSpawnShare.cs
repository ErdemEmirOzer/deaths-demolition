using DD.Prototype.CameraSystem;
using DD.Prototype.Enemies;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// WP-076 §5.1 / B7 (S-01 WP-070 A-7) · the share of recent spawns that came up behind the camera, over a sliding
    /// window of the last <see cref="ThreatSettings.RearSpawnWindow"/> spawns, and the penalty it puts on rear points:
    /// at or above the mode's cap (FP 35 %, TP 45 %, top-down none) a rear point weighs ×0 — ×0.25 when no other
    /// path-ready point is left — and below it ×0.7. Fixed ring, 0 allocation.
    /// </summary>
    public static class RearSpawnShare
    {
        private static readonly bool[] s_ring = new bool[32];
        private static int s_head, s_count;

        public static void Record(bool rear)
        {
            s_ring[s_head] = rear;
            s_head = (s_head + 1) % s_ring.Length;
            if (s_count < s_ring.Length) s_count++;
        }

        /// <summary>Rear share of the last <paramref name="window"/> spawns (0 when none yet).</summary>
        public static float Share(int window)
        {
            int n = System.Math.Min(System.Math.Max(1, window), s_count);
            if (n == 0) return 0f;
            int rear = 0;
            for (int i = 1; i <= n; i++) if (s_ring[(s_head - i + s_ring.Length) % s_ring.Length]) rear++;
            return (float)rear / n;
        }

        public static int Count => s_count;

        /// <summary>The weight penalty (0..1; factor = 1 − penalty) for rear points under <paramref name="mode"/>.</summary>
        public static float Penalty(ThreatSettings s, int mode, bool otherPointReady)
        {
            float cap = s.ByMode(s.RearSpawnCap, mode, 1f);
            if (cap >= 1f) return 0f;
            if (Share(s.RearSpawnWindow) >= cap) return otherPointReady ? 1f : 1f - s.RearSpawnNoAlternativeFactor;
            return 1f - s.RearSpawnBelowCapFactor;
        }

        public static void ResetStatics() { s_head = 0; s_count = 0; for (int i = 0; i < s_ring.Length; i++) s_ring[i] = false; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() => ResetStatics();

        internal static int CurrentMode => (int)CameraModeService.Current;
    }
}
