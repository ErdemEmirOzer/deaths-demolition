using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using UnityEngine;

namespace DD.Prototype.Difficulty
{
    /// <summary>
    /// WP-102 · what the intercepting and cutting bodies read about the player, once per frame for the whole crowd (lazy, first
    /// reader of the frame; no allocation): its planar velocity and its escape direction — where it runs, or, standing, the
    /// open side of the ring around it (the widest gap among 8 slices of the live bodies within <see cref="RingRadius"/>, nearest
    /// to its facing). The math is in static pure functions for the EditMode tests.
    /// </summary>
    public static class PursuitPlanner
    {
        /// <summary>Slower than this (m/s) the player is "standing": the escape direction is the ring's open side.</summary>
        public const float MovingSpeed = 1f;
        public const float RingRadius = 10f;
        public const int Slices = 8;

        private static int s_frame = -1;
        private static Vector3 s_velocity, s_escape = Vector3.forward;
        private static float s_speed;
        private static readonly int[] s_counts = new int[Slices];

        public static Vector3 PlayerVelocity { get { Refresh(); return s_velocity; } }
        public static float PlayerSpeed { get { Refresh(); return s_speed; } }
        public static Vector3 EscapeDirection { get { Refresh(); return s_escape; } }

        private static void Refresh()
        {
            int frame = Time.frameCount;
            if (frame == s_frame) return;
            s_frame = frame;
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null || rig.Motor == null)
            {
                s_velocity = Vector3.zero; s_speed = 0f;
                return;
            }

            Vector3 v = rig.Motor.PlanarVelocity; v.y = 0f;
            s_velocity = v;
            s_speed = v.magnitude;
            if (s_speed >= MovingSpeed)
            {
                s_escape = v / s_speed;
                return;
            }

            Vector3 p = rig.transform.position;
            System.Array.Clear(s_counts, 0, Slices);
            var live = ZombieEnemy.Live;
            for (int i = 0; i < live.Count; i++)
            {
                ZombieEnemy z = live[i];
                if (z == null || !z.IsAlive) continue;
                Vector3 d = z.transform.position - p; d.y = 0f;
                if (d.sqrMagnitude > RingRadius * RingRadius || d.sqrMagnitude < 1e-4f) continue;
                s_counts[SliceOf(d)]++;
            }

            Vector3 facing = rig.transform.forward; facing.y = 0f;
            s_escape = OpenSide(s_counts, facing);
        }

        /// <summary>The slice (0 = +Z, clockwise seen from above) a planar direction falls in.</summary>
        public static int SliceOf(Vector3 d)
        {
            float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;   // 0 = +Z, 90 = +X
            if (a < 0f) a += 360f;
            return Mathf.Clamp((int)((a + 180f / Slices) / (360f / Slices)) % Slices, 0, Slices - 1);
        }

        /// <summary>A slice's centre direction.</summary>
        public static Vector3 SliceDirection(int slice)
        {
            float a = slice * (360f / Slices) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        /// <summary>The least occupied slice (empty first), ties to the one nearest <paramref name="facing"/>: the ring's open side.</summary>
        public static Vector3 OpenSide(int[] counts, Vector3 facing)
        {
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.forward;
            facing.Normalize();
            int best = 0, bestCount = int.MaxValue;
            float bestDot = -2f;
            for (int i = 0; i < Slices; i++)
            {
                float dot = Vector3.Dot(SliceDirection(i), facing);
                if (counts[i] < bestCount || (counts[i] == bestCount && dot > bestDot)) { best = i; bestCount = counts[i]; bestDot = dot; }
            }

            return SliceDirection(best);
        }

        /// <summary>Predictive pursuit: the lead offset from the player — velocity × min(distance / own speed, lead cap).</summary>
        public static Vector3 InterceptOffset(float distance, Vector3 playerVelocity, float ownSpeed, float leadMaxSeconds)
        {
            if (leadMaxSeconds <= 0f) return Vector3.zero;
            float lead = Mathf.Min(distance / Mathf.Max(0.5f, ownSpeed), leadMaxSeconds);
            return playerVelocity * lead;
        }

        /// <summary>
        /// A cutter takes the cut point only when it gets there <paramref name="marginSeconds"/> before the player could reach it
        /// (the player standing counts as 1 m/s: it would have to set off first).
        /// </summary>
        public static bool CutReachable(float ownDistanceToCut, float ownSpeed, float cutDistance, float playerSpeed, float marginSeconds)
        {
            float own = ownDistanceToCut / Mathf.Max(0.5f, ownSpeed);
            float player = cutDistance / Mathf.Max(MovingSpeed, playerSpeed);
            return own + marginSeconds <= player;
        }

        /// <summary>Tests: forget the frame cache.</summary>
        public static void ResetForTests() => s_frame = -1;
    }
}
