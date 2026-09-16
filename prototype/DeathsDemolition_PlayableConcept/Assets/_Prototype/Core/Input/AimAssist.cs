using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// WP-024 (ADR-024-1): stick aim assist as pure geometry. Given the raw aim
    /// direction and the candidate target points, returns the direction to aim at:
    /// the closest candidate inside the cone (half-angle <= <see cref="InputPreferences.MaxAimAssistAngleDegrees"/>,
    /// distance <= <see cref="InputPreferences.MaxAimAssistDistance"/>) pulls the
    /// direction onto itself; anything outside the cone leaves the direction untouched.
    ///
    /// BOUNDARIES (the reason this is a function and not a system): it takes a
    /// direction and returns a direction. It has no access to damage, range, fire rate,
    /// ammunition, target count or the hit pipeline, so the balance cannot change by
    /// construction. It is applied only when the aim source is the stick (gamepad);
    /// the caller decides that.
    /// </summary>
    public static class AimAssist
    {
        /// <summary>The choice made by <see cref="Resolve"/>. Readout for the F1 panel and tests.</summary>
        public readonly struct Result
        {
            public Result(Vector3 direction, int targetIndex, float angleDegrees, float distance)
            {
                Direction = direction;
                TargetIndex = targetIndex;
                AngleDegrees = angleDegrees;
                Distance = distance;
            }

            /// <summary>Normalised horizontal aim direction after assist.</summary>
            public Vector3 Direction { get; }

            /// <summary>Index into the candidate list, or -1 when nothing was inside the cone.</summary>
            public int TargetIndex { get; }

            /// <summary>Angle between the raw direction and the chosen target (degrees). 0 when no target.</summary>
            public float AngleDegrees { get; }

            /// <summary>Horizontal distance to the chosen target. 0 when no target.</summary>
            public float Distance { get; }

            public bool Assisted => TargetIndex >= 0;
        }

        /// <summary>
        /// Pick the target: inside the cone, closest by angle first, then by distance.
        /// <paramref name="origin"/> and every candidate are compared on the horizontal
        /// plane (Y ignored), matching how the stick direction is resolved.
        /// </summary>
        public static Result Resolve(Vector3 origin, Vector3 rawDirection, IReadOnlyList<Vector3> candidates,
            float maxAngleDegrees, float maxDistance)
        {
            Vector3 raw = rawDirection;
            raw.y = 0f;
            if (raw.sqrMagnitude < 1e-8f)
            {
                return new Result(rawDirection, -1, 0f, 0f);
            }

            raw.Normalize();

            maxAngleDegrees = Mathf.Clamp(maxAngleDegrees, 0f, InputPreferences.MaxAimAssistAngleDegrees);
            maxDistance = Mathf.Clamp(maxDistance, 0f, InputPreferences.MaxAimAssistDistance);
            if (candidates == null || candidates.Count == 0 || maxAngleDegrees <= 0f || maxDistance <= 0f)
            {
                return new Result(raw, -1, 0f, 0f);
            }

            int best = -1;
            float bestAngle = float.MaxValue;
            float bestDistance = float.MaxValue;
            Vector3 bestDirection = raw;
            float maxDistanceSq = maxDistance * maxDistance;

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 to = candidates[i] - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq < 1e-6f || distSq > maxDistanceSq)
                {
                    continue;
                }

                Vector3 dir = to / Mathf.Sqrt(distSq);
                float angle = Vector3.Angle(raw, dir);
                if (angle > maxAngleDegrees)
                {
                    continue;
                }

                // Smaller angle wins; at equal angle the nearer target wins.
                if (angle < bestAngle - 1e-4f || (Mathf.Abs(angle - bestAngle) <= 1e-4f && distSq < bestDistance))
                {
                    best = i;
                    bestAngle = angle;
                    bestDistance = distSq;
                    bestDirection = dir;
                }
            }

            if (best < 0)
            {
                return new Result(raw, -1, 0f, 0f);
            }

            return new Result(bestDirection, best, bestAngle, Mathf.Sqrt(bestDistance));
        }
    }
}
