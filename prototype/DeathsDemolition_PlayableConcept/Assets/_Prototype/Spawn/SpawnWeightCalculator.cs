using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>Gates that may be dropped, in order, when no point survives a strict pass.</summary>
    [Flags]
    public enum SpawnRelaxation
    {
        None = 0,

        /// <summary>Ignore the on-screen / behind-camera fairness rejections.</summary>
        Screen = 1,

        /// <summary>Ignore the per-point cooldown.</summary>
        Cooldown = 2,

        /// <summary>Ignore the "must have a complete NavMesh path" rule. Last resort only.</summary>
        Reachability = 4
    }

    /// <summary>Per-input result of one evaluation. Debug surface only.</summary>
    public struct SpawnWeightBreakdown
    {
        public float Base;
        public float Distance;
        public float Region;
        public float Heading;
        public float Recency;
        public float Cooldown;
        public float Occupancy;
        public float Density;
        public float Visibility;   // WP-030: on-screen factor (1 off screen)
        public float Total;
        public bool Valid;
        public string Reason;
    }

    /// <summary>Everything about the player and the world one evaluation pass needs.</summary>
    public struct SpawnWeightInputs
    {
        public Vector3 PlayerPosition;
        public RingRegion PlayerRegion;
        public Vector3 PlayerVelocity;
        public float Now;
        public bool NavDegraded;
    }

    /// <summary>
    /// REQ-066 / REQ-067 / REQ-068 / REQ-069 / REQ-070 - the nine-input spawn weight.
    ///
    ///   weight = designerBase
    ///          * fDistance    (walkable path distance vs the 12 / 18-35 / cut-off bands)
    ///          * fRegion      (player's current ring region)
    ///          * fHeading     (player's movement direction)
    ///          * fRecency     (how recently this point was used)
    ///          * fCooldown    (hard 0 while the ~4 s point cooldown runs)
    ///          * fOccupancy   (enemies alive that came from this point)
    ///          * fDensity     (living enemies near the point)
    ///          * gValid       (0/1: enabled, min distance, fairness, complete path)
    ///
    /// The distance input is the WALKABLE path distance, never the straight line - that is
    /// the whole reason the NavMesh half of this task exists. A point across the koi pond
    /// is 20 m away as the crow flies and 55 m along the bridge; weighting it by the crow
    /// flight would drip-feed enemies that arrive a full round later.
    ///
    /// Pure and static: no state, no allocation, unit-testable without a scene.
    /// </summary>
    public static class SpawnWeightCalculator
    {
        public static float Evaluate(
            SpawnPointRuntime point,
            in SpawnWeightInputs inputs,
            SpawnConfigSection config,
            SpawnWeightTuning tuning,
            SpawnRelaxation relaxation,
            out SpawnWeightBreakdown breakdown)
        {
            breakdown = default;
            breakdown.Reason = "ok";
            breakdown.Base = 1f;
            breakdown.Distance = 1f;
            breakdown.Region = 1f;
            breakdown.Heading = 1f;
            breakdown.Recency = 1f;
            breakdown.Cooldown = 1f;
            breakdown.Occupancy = 1f;
            breakdown.Density = 1f;

            if (point == null || config == null || tuning == null)
            {
                breakdown.Reason = "no data";
                return 0f;
            }

            // ---------------------------------------------------------- 1. designer base
            breakdown.Base = Mathf.Max(0f, point.BaseWeight) * Mathf.Max(0f, config.DesignerWeightScale);

            if (!point.Enabled)
            {
                return Reject(point, ref breakdown, "disabled");
            }

            if (breakdown.Base <= 0f)
            {
                return Reject(point, ref breakdown, "base weight 0");
            }

            // ---------------------------------------------------------- 9. valid path
            bool pathOk = point.PathStatus == NavPathStatus.Complete ||
                          (inputs.NavDegraded && point.PathStatus == NavPathStatus.Degraded);

            if (!pathOk && config.RequireReachablePath && (relaxation & SpawnRelaxation.Reachability) == 0)
            {
                // REQ-068. A PathPartial is explicitly not good enough: the enemy would
                // walk to the far side of a wall and stand there for the rest of the round.
                return Reject(point, ref breakdown,
                    point.PathStatus == NavPathStatus.Partial ? "partial path" : "no path");
            }

            // ---------------------------------------------------------- 4. path distance
            float distance = point.PathDistance;

            if (distance < config.MinSpawnDistance)
            {
                return Reject(point, ref breakdown, "too close");
            }

            if (distance > config.MaxUsefulDistance)
            {
                return Reject(point, ref breakdown, "too far");
            }

            breakdown.Distance = DistanceFactor(distance, config, tuning);

            // ---------------------------------------------------------- fairness gates
            if ((relaxation & SpawnRelaxation.Screen) == 0)
            {
                if (point.IsOnScreen && point.StraightDistance < config.OnScreenRejectDistance)
                {
                    return Reject(point, ref breakdown, "on screen");
                }

                if (point.IsBehindCamera && point.StraightDistance < tuning.BehindCameraRejectDistance)
                {
                    return Reject(point, ref breakdown, "behind camera");
                }
            }

            // ---------------------------------------------------------- 6. point cooldown
            float sinceUse = inputs.Now - point.LastUsedTime;
            if (sinceUse < config.PointCooldown && (relaxation & SpawnRelaxation.Cooldown) == 0)
            {
                breakdown.Cooldown = 0f;
                return Reject(point, ref breakdown, "cooldown");
            }

            // ---------------------------------------------------------- 2. ring region
            breakdown.Region = RegionFactor(point.Region, inputs.PlayerRegion, config, tuning);

            // ---------------------------------------------------------- 3. heading
            breakdown.Heading = HeadingFactor(point.WorldPosition, inputs, config, tuning);

            // ---------------------------------------------------------- 5. recent use
            breakdown.Recency = RecencyFactor(sinceUse, config, tuning);

            // ---------------------------------------------------------- 7. point occupancy
            breakdown.Occupancy = Mathf.Max(
                tuning.OccupancyFloor,
                1f - config.PerPointOccupancyPenalty * point.AliveFromHere);

            // ---------------------------------------------------------- 8. local density
            breakdown.Density = Mathf.Max(
                tuning.DensityFloor,
                1f - config.LocalDensityPenalty * point.LocalDensity);

            // ---------------------------------------------------------- 9. visibility (WP-030)
            breakdown.Visibility = point.IsOnScreen && (relaxation & SpawnRelaxation.Screen) == 0 ? tuning.OnScreenFactor : 1f;

            float total = breakdown.Base
                          * breakdown.Distance
                          * breakdown.Region
                          * breakdown.Heading
                          * breakdown.Recency
                          * breakdown.Cooldown
                          * breakdown.Occupancy
                          * breakdown.Density
                          * breakdown.Visibility;

            if (total < config.MinimumUsableWeight)
            {
                return Reject(point, ref breakdown, "weight below floor");
            }

            breakdown.Total = total;
            breakdown.Valid = true;
            point.LastWeight = total;
            point.LastRejectReason = "ok";
            return total;
        }

        /// <summary>
        /// 12 m hard floor, flat bonus across the preferred 18-35 m band, linear fade to a
        /// floor at MaxUsefulDistance. Both ends are walkable metres.
        /// </summary>
        private static float DistanceFactor(float distance, SpawnConfigSection config, SpawnWeightTuning tuning)
        {
            float min = config.MinSpawnDistance;
            float preferredMin = Mathf.Max(min, config.PreferredDistanceMin);
            float preferredMax = Mathf.Max(preferredMin, config.PreferredDistanceMax);
            float maxUseful = Mathf.Max(preferredMax, config.MaxUsefulDistance);
            float bonus = config.PreferredBandBonus;

            if (distance >= preferredMin && distance <= preferredMax)
            {
                return bonus;
            }

            if (distance < preferredMin)
            {
                float span = Mathf.Max(0.01f, preferredMin - min);
                float t = Mathf.Clamp01((distance - min) / span);
                return Mathf.Lerp(tuning.NearDistanceFloor, bonus, t);
            }

            float farSpan = Mathf.Max(0.01f, maxUseful - preferredMax);
            float ft = Mathf.Clamp01((distance - preferredMax) / farSpan);
            return Mathf.Lerp(bonus, tuning.FarDistanceFloor, ft);
        }

        private static float RegionFactor(RingRegion pointRegion, RingRegion playerRegion,
            SpawnConfigSection config, SpawnWeightTuning tuning)
        {
            if (pointRegion == playerRegion)
            {
                return config.SameRegionBonus;
            }

            return RingRegionMap.AreAdjacent(pointRegion, playerRegion) ? 1f : tuning.DistantRegionFactor;
        }

        /// <summary>
        /// Points the player is walking towards are favoured, points behind are damped, and
        /// a standing player makes the whole input neutral.
        /// </summary>
        private static float HeadingFactor(Vector3 pointPosition, in SpawnWeightInputs inputs,
            SpawnConfigSection config, SpawnWeightTuning tuning)
        {
            Vector3 velocity = inputs.PlayerVelocity;
            velocity.y = 0f;

            float speedSqr = velocity.x * velocity.x + velocity.z * velocity.z;
            if (speedSqr < tuning.HeadingIdleSpeed * tuning.HeadingIdleSpeed)
            {
                return 1f;
            }

            Vector3 toPoint = pointPosition - inputs.PlayerPosition;
            toPoint.y = 0f;

            float toPointSqr = toPoint.x * toPoint.x + toPoint.z * toPoint.z;
            if (toPointSqr < 0.0001f)
            {
                return 1f;
            }

            float dot = (velocity.x * toPoint.x + velocity.z * toPoint.z) /
                        (Mathf.Sqrt(speedSqr) * Mathf.Sqrt(toPointSqr));

            float t = Mathf.Clamp01((dot + 1f) * 0.5f);
            return Mathf.Lerp(tuning.BehindPlayerFactor, config.AheadOfPlayerBonus, t);
        }

        /// <summary>
        /// Monotone in the time since the point was last used, starting at
        /// RecentUsePenalty and reaching 1 after RecentUseMemory seconds.
        ///
        /// It is deliberately monotone from t = 0, not from the end of the cooldown: when
        /// demand outruns the twelve points and the cooldown gate has to be relaxed, this
        /// factor is what makes the COOLEST point win, instead of a coin flip between
        /// twelve equally penalised gates.
        /// </summary>
        private static float RecencyFactor(float sinceUse, SpawnConfigSection config, SpawnWeightTuning tuning)
        {
            if (sinceUse >= tuning.RecentUseMemory)
            {
                return 1f;
            }

            float t = Mathf.Clamp01(sinceUse / Mathf.Max(0.01f, tuning.RecentUseMemory));
            return Mathf.Lerp(config.RecentUsePenalty, 1f, t);
        }

        private static float Reject(SpawnPointRuntime point, ref SpawnWeightBreakdown breakdown, string reason)
        {
            breakdown.Total = 0f;
            breakdown.Valid = false;
            breakdown.Reason = reason;
            point.LastWeight = 0f;
            point.LastRejectReason = reason;
            return 0f;
        }
    }
}
