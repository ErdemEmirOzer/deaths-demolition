using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// Regions of the approved ring topology (request: MAP section).
    /// Centre courtyard plus four outer regions that form a continuous, deliberately
    /// imperfect ring: north palace front, east sakura garden, south koi pond with the
    /// narrow bridge, west bamboo corridor.
    /// </summary>
    public enum RingRegion
    {
        Center = 0,
        North = 1,
        East = 2,
        South = 3,
        West = 4
    }

    /// <summary>
    /// Turns a world position into a ring region. Map-independent on purpose: the map
    /// arrives with task G-06, so the classifier only needs a centre and a radius, both
    /// of which G-06 can overwrite in one line from its own bootstrap:
    ///
    ///     RingRegionMap.MapCenter = courtyardTransform.position;
    ///     RingRegionMap.CenterRadius = 15f;
    ///
    /// Axis convention: +Z north, +X east, -Z south, -X west. It matches the authored
    /// spawn layout in <see cref="DD.Prototype.Spawn.DefaultSpawnPointLayout"/>.
    /// </summary>
    public static class RingRegionMap
    {
        /// <summary>PROTOTYPE-ONLY: courtyard radius that counts as "centre". Invented.</summary>
        public const float DefaultCenterRadius = 14f;

        private static Vector3 s_mapCenter = Vector3.zero;
        private static float s_centerRadius = DefaultCenterRadius;

        /// <summary>World position of the courtyard. G-06 may overwrite it.</summary>
        public static Vector3 MapCenter
        {
            get => s_mapCenter;
            set => s_mapCenter = value;
        }

        /// <summary>Radius around <see cref="MapCenter"/> classified as <see cref="RingRegion.Center"/>.</summary>
        public static float CenterRadius
        {
            get => s_centerRadius;
            set => s_centerRadius = Mathf.Max(0.5f, value);
        }

        public static RingRegion Classify(Vector3 worldPosition)
        {
            return Classify(worldPosition, s_mapCenter, s_centerRadius);
        }

        public static RingRegion Classify(Vector3 worldPosition, Vector3 center, float centerRadius)
        {
            float dx = worldPosition.x - center.x;
            float dz = worldPosition.z - center.z;
            float sqr = dx * dx + dz * dz;

            if (sqr <= centerRadius * centerRadius)
            {
                return RingRegion.Center;
            }

            // Diagonal split: the axis with the larger magnitude wins.
            if (Mathf.Abs(dz) >= Mathf.Abs(dx))
            {
                return dz >= 0f ? RingRegion.North : RingRegion.South;
            }

            return dx >= 0f ? RingRegion.East : RingRegion.West;
        }

        /// <summary>
        /// Neighbouring regions along the ring, used to reason about "where the player is
        /// heading" without hard-coding the map. Centre neighbours everything.
        /// </summary>
        public static bool AreAdjacent(RingRegion a, RingRegion b)
        {
            if (a == b)
            {
                return true;
            }

            if (a == RingRegion.Center || b == RingRegion.Center)
            {
                return true;
            }

            // Ring order: North - East - South - West - North.
            return !((a == RingRegion.North && b == RingRegion.South) ||
                     (a == RingRegion.South && b == RingRegion.North) ||
                     (a == RingRegion.East && b == RingRegion.West) ||
                     (a == RingRegion.West && b == RingRegion.East));
        }

        public static string ToShortLabel(RingRegion region)
        {
            switch (region)
            {
                case RingRegion.North: return "N";
                case RingRegion.East: return "E";
                case RingRegion.South: return "S";
                case RingRegion.West: return "W";
                default: return "C";
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_mapCenter = Vector3.zero;
            s_centerRadius = DefaultCenterRadius;
        }
    }
}
