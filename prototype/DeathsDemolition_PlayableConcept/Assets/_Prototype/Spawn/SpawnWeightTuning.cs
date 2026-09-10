using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// The parts of the spawn weighting that PrototypeConfig.Spawn does not already own.
    ///
    /// The bulk of the coefficients - MinSpawnDistance, the 18-35 m preferred band,
    /// MaxUsefulDistance, PointCooldown, PreferredBandBonus, AheadOfPlayerBonus,
    /// SameRegionBonus, RecentUsePenalty, PerPointOccupancyPenalty, LocalDensityPenalty,
    /// LocalDensityRadius, MinimumUsableWeight, RequireReachablePath,
    /// OnScreenRejectDistance - live in PrototypeConfig.Spawn and are read there, live,
    /// never re-derived locally. What remains are the shape constants this task had to
    /// invent to turn those bonuses into a curve, and they are all listed as invented.
    ///
    /// Assets/_Prototype/Core belongs to another task in this wave, so these cannot be
    /// added to SpawnConfigSection yet; they should move there verbatim when it reopens.
    /// </summary>
    [Serializable]
    public sealed class SpawnWeightTuning
    {
        [Header("Distance curve shape")]
        [PrototypeOnly(PendingDecisions.None,
            "Weight factor at exactly the minimum spawn distance (12 m). Rises to the " +
            "preferred-band bonus across 12->18 m.", true)]
        [Range(0f, 2f)] public float NearDistanceFloor = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Weight factor at MaxUsefulDistance. Beyond that the point is zeroed outright " +
            "(REQ-066: 'a very distant point may drop to zero').", true)]
        [Range(0f, 2f)] public float FarDistanceFloor = 0.15f;

        [Header("Region and heading")]
        [PrototypeOnly(PendingDecisions.None,
            "Factor for a point in a ring region that does not touch the player's region.", true)]
        [Range(0f, 2f)] public float DistantRegionFactor = 0.9f;

        [PrototypeOnly(PendingDecisions.None,
            "Factor for a point directly behind the player's movement direction. Below 1 so " +
            "the swarm mostly builds up ahead of the player instead of chasing from behind.", true)]
        [Range(0f, 2f)] public float BehindPlayerFactor = 0.8f;

        [PrototypeOnly(PendingDecisions.None,
            "Player speed under which heading is treated as 'standing still' and ignored, m/s.", true)]
        public float HeadingIdleSpeed = 0.6f;

        [Header("Recency")]
        [PrototypeOnly(PendingDecisions.None,
            "Seconds over which the recent-use penalty decays back to 1 after the hard " +
            "cooldown expires. REQ-070: stops every enemy pouring out of one gate.", true)]
        public float RecentUseMemory = 12f;

        [Header("Occupancy floors")]
        [PrototypeOnly(PendingDecisions.None,
            "Lower bound of the per-point occupancy factor, so a busy gate is damped, not banned.", true)]
        [Range(0f, 1f)] public float OccupancyFloor = 0.2f;

        [PrototypeOnly(PendingDecisions.None,
            "Lower bound of the local density factor.", true)]
        [Range(0f, 1f)] public float DensityFloor = 0.2f;

        [Header("Fairness (REQ-069)")]
        [PrototypeOnly(PendingDecisions.None,
            "Points closer than this AND behind the camera are rejected. The camera sits " +
            "behind and above the player, so this is the ambush guard that the on-screen " +
            "distance cannot cover.", true)]
        public float BehindCameraRejectDistance = 15f;

        [Header("Placement and cost")]
        [PrototypeOnly(PendingDecisions.None,
            "Random scatter around the point so two enemies never spawn inside each other, m.", true)]
        public float SpawnScatterRadius = 1.2f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds a cached walkable path distance stays valid before that point is re-queried.", true)]
        public float PathCacheInterval = 0.5f;

        [PrototypeOnly(PendingDecisions.None,
            "Spawn points re-pathed per refill tick. Spreads 12 NavMesh.CalculatePath calls " +
            "over several ticks instead of spiking one frame.", true)]
        [Range(1, 12)] public int PathQueriesPerTick = 4;

        [Header("Tier introduction (REQ-071)")]
        [PrototypeOnly(PendingDecisions.None,
            "Fraction of the round pool that must be spent before a guaranteed tier debut is " +
            "released, so Runner/Guard/Executioner do not walk out as enemy number one.", true)]
        [Range(0f, 0.8f)] public float GuaranteedIntroPoolFraction = 0.25f;

        public void Sanitize()
        {
            NearDistanceFloor = Mathf.Clamp(NearDistanceFloor, 0f, 2f);
            FarDistanceFloor = Mathf.Clamp(FarDistanceFloor, 0f, 2f);
            DistantRegionFactor = Mathf.Clamp(DistantRegionFactor, 0f, 2f);
            BehindPlayerFactor = Mathf.Clamp(BehindPlayerFactor, 0f, 2f);
            HeadingIdleSpeed = Mathf.Max(0f, HeadingIdleSpeed);
            RecentUseMemory = Mathf.Max(0.01f, RecentUseMemory);
            OccupancyFloor = Mathf.Clamp01(OccupancyFloor);
            DensityFloor = Mathf.Clamp01(DensityFloor);
            BehindCameraRejectDistance = Mathf.Max(0f, BehindCameraRejectDistance);
            SpawnScatterRadius = Mathf.Max(0f, SpawnScatterRadius);
            PathCacheInterval = Mathf.Max(0.05f, PathCacheInterval);
            PathQueriesPerTick = Mathf.Clamp(PathQueriesPerTick, 1, 12);
            GuaranteedIntroPoolFraction = Mathf.Clamp(GuaranteedIntroPoolFraction, 0f, 0.8f);
        }
    }
}
