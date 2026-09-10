using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// Live state of one spawn point: the authored data plus everything the weighting
    /// needs to remember between selections. One instance per point, created once per
    /// run - the selection loop never allocates.
    /// </summary>
    public sealed class SpawnPointRuntime
    {
        public SpawnPointRuntime(int index, SpawnPointDefinition definition)
        {
            Index = index;
            Definition = definition;
            Id = definition.Id;
            Region = definition.Region;
            BaseWeight = definition.BaseWeight;
            Enabled = definition.Enabled;
            WorldPosition = definition.Position;
            NavPosition = definition.Position;
        }

        public int Index { get; }
        public SpawnPointDefinition Definition { get; }

        public string Id { get; private set; }
        public RingRegion Region { get; set; }
        public float BaseWeight { get; set; }
        public bool Enabled { get; set; }

        /// <summary>Authored position, or the marker's transform once the map binds one.</summary>
        public Vector3 WorldPosition { get; set; }

        /// <summary>The world position projected onto the NavMesh; equals WorldPosition while degraded.</summary>
        public Vector3 NavPosition { get; set; }

        /// <summary>True once a scene marker (task G-06) has taken over the position.</summary>
        public bool HasMarker { get; set; }

        // ------------------------------------------------------------- per-run state

        /// <summary>Time.time of the last spawn from this point. Drives cooldown and recency.</summary>
        public float LastUsedTime { get; set; } = -999f;

        /// <summary>Enemies spawned here that are still alive (weighting input 7).</summary>
        public int AliveFromHere { get; set; }

        /// <summary>Enemies spawned here in the current round. Debug readout.</summary>
        public int SpawnedThisRound { get; set; }

        /// <summary>Living enemies near the point at the last evaluation (weighting input 8).</summary>
        public int LocalDensity { get; set; }

        // ------------------------------------------------------------- cached queries

        public float PathDistance { get; set; }
        public float StraightDistance { get; set; }
        public NavPathStatus PathStatus { get; set; } = NavPathStatus.Invalid;
        public float PathCachedAt { get; set; } = -999f;

        /// <summary>Inside the camera frustum at the last evaluation (fairness gate, REQ-069).</summary>
        public bool IsOnScreen { get; set; }

        /// <summary>Behind the camera plane at the last evaluation (ambush gate, REQ-069).</summary>
        public bool IsBehindCamera { get; set; }

        // ------------------------------------------------------------- last evaluation

        public float LastWeight { get; set; }
        public string LastRejectReason { get; set; } = "not evaluated";

        public void ResetForRound()
        {
            LastUsedTime = -999f;
            AliveFromHere = 0;
            SpawnedThisRound = 0;
            LocalDensity = 0;
            LastWeight = 0f;
            LastRejectReason = "not evaluated";
            PathCachedAt = -999f;
            PathStatus = NavPathStatus.Invalid;
        }

        /// <summary>Rotation a spawned enemy gets: facing the courtyard unless authored otherwise.</summary>
        public Quaternion SpawnRotation()
        {
            if (Definition != null && !Definition.FaceMapCenter)
            {
                return Quaternion.Euler(0f, Definition.FacingYaw, 0f);
            }

            Vector3 toCenter = RingRegionMap.MapCenter - WorldPosition;
            toCenter.y = 0f;

            if (toCenter.sqrMagnitude < 0.0001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(toCenter.normalized, Vector3.up);
        }

        public void ApplyMarker(string id, Vector3 worldPosition, RingRegion region, bool overrideRegion,
            float baseWeightOverride, bool enabled)
        {
            Id = string.IsNullOrEmpty(id) ? Id : id;
            WorldPosition = worldPosition;
            NavPosition = worldPosition;
            HasMarker = true;
            Enabled = enabled;

            if (overrideRegion)
            {
                Region = region;
            }

            if (baseWeightOverride >= 0f)
            {
                BaseWeight = baseWeightOverride;
            }

            PathCachedAt = -999f;
        }
    }
}
