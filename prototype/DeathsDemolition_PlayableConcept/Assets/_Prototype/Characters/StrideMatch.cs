using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-032 · Stride ↔ speed bookkeeping for the in-place cycles (AP-009 <c>speed_reference</c>):
    /// a cycle played at rate r carries the feet at clipSpeed·r; the body moves at v; the
    /// difference is skating. Rates are clamped by the drivers (zombie 0.35–2.6, player child
    /// time scales ≤ 2.4), so a cycle can only be honest inside its band — outside it the slip
    /// is reported here and the cycle is an AP-009B follow-up (run / strafe cycles).
    /// </summary>
    public static class StrideMatch
    {
        /// <summary>Ground covered by one full cycle (two steps) at rate 1, metres.</summary>
        public static float StrideMeters(float clipSpeedMps, float cycleSeconds)
        {
            return Mathf.Max(0f, clipSpeedMps) * Mathf.Max(0f, cycleSeconds);
        }

        /// <summary>The rate that shows no slip, before any clamp.</summary>
        public static float HonestRate(float bodySpeed, float clipSpeedMps)
        {
            return clipSpeedMps > 0.01f ? Mathf.Max(0f, bodySpeed) / clipSpeedMps : 1f;
        }

        /// <summary>Foot slip per second at a (clamped) rate, m/s.</summary>
        public static float SlipMetersPerSecond(float bodySpeed, float clipSpeedMps, float rate)
        {
            return Mathf.Abs(Mathf.Max(0f, bodySpeed) - Mathf.Max(0f, clipSpeedMps) * Mathf.Max(0f, rate));
        }

        /// <summary>Foot slip over one step (half a cycle) at a (clamped) rate, metres.</summary>
        public static float SlipPerStep(float bodySpeed, float clipSpeedMps, float rate, float cycleSeconds)
        {
            float stepSeconds = Mathf.Max(0f, cycleSeconds) * 0.5f / Mathf.Max(0.01f, rate);
            return SlipMetersPerSecond(bodySpeed, clipSpeedMps, rate) * stepSeconds;
        }
    }
}
