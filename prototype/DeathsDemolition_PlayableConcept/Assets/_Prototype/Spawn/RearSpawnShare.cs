using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// WP-076 §5.1 / B7 (S-01 WP-070 A-7) · the share of recent spawns that came up behind the camera, over a sliding
    /// window of the last <see cref="Window"/> spawns, and the penalty it puts on rear points: at or above the mode's cap
    /// (FP 35 %, TP 45 %, top-down none) a rear point weighs ×0 — ×0.25 when no other path-ready point is left — and
    /// below it ×0.7. Fixed ring, 0 allocation. The table lives here, on the Spawn side (Spawn → Enemies direction lock,
    /// EnemyVersionPolicyTests P22).
    /// </summary>
    public static class RearSpawnShare
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 rear-hemisphere spawn share cap by camera mode (TopDown, TP, FP; 1 = none), sliding window (spawns), factor below the cap, factor at the cap when no other path-ready point exists.", true)]
        public static readonly float[] Cap = { 1f, 0.45f, 0.35f };
        public const int Window = 8;
        public const float BelowCapFactor = 0.7f, NoAlternativeFactor = 0.25f;

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
        public static float Penalty(int mode, bool otherPointReady)
        {
            float cap = Cap[mode < 0 ? 0 : mode > 2 ? 2 : mode];
            if (cap >= 1f) return 0f;
            if (Share(Window) >= cap) return otherPointReady ? 1f : 1f - NoAlternativeFactor;
            return 1f - BelowCapFactor;
        }

        public static void ResetStatics() { s_head = 0; s_count = 0; for (int i = 0; i < s_ring.Length; i++) s_ring[i] = false; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() => ResetStatics();

        internal static int CurrentMode => (int)CameraModeService.Current;
    }
}
