using System.Collections.Generic;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// The authored 12-point layout (REQ-065, request: "~12 points is enough").
    ///
    /// MAP-INDEPENDENT BY DESIGN
    /// -------------------------
    /// The map arrives with task G-06, so the layout is defined as DATA here, positioned
    /// for the approved topology: a 90x90 m blockout centred on the origin, courtyard in
    /// the middle, a continuous but deliberately imperfect ring around it
    /// (+Z north palace front, +X east sakura garden, -Z south koi pond and narrow bridge,
    /// -X west bamboo corridor). Every point sits 30-36 m from the centre, which lands
    /// inside the preferred 18-35 m band for a player anywhere near the courtyard.
    ///
    /// The numbers are PROTOTYPE-ONLY and invented; the request specifies the count and
    /// the topology, never the coordinates.
    ///
    /// DESIGNER BASE WEIGHTS - the reasoning, not a product decision:
    ///   1.00  main approaches (palace gates, sakura grove, bamboo corridor): the routes
    ///         the ring is built around;
    ///   0.85-0.95  corner and secondary approaches;
    ///   0.70  S_BridgeSouthEnd: the narrow bridge is a choke. Flooding it turns REQ-047's
    ///         "no total traffic lock" into a coin flip, so it starts deliberately low.
    /// </summary>
    public static class DefaultSpawnPointLayout
    {
        /// <summary>Number of authored points.</summary>
        public const int PointCount = 12;

        /// <summary>Fills a caller-owned list with fresh copies of the authored layout.</summary>
        public static void Fill(List<SpawnPointDefinition> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();

            Add(target, "N_PalaceGateWest", RingRegion.North, -8f, 34f, 1.00f,
                "West half of the palace front, at the wide stone path.");
            Add(target, "N_PalaceGateEast", RingRegion.North, 8f, 34f, 1.00f,
                "East half of the palace front. Mirrors the west gate.");
            Add(target, "NE_StonePathCorner", RingRegion.North, 24f, 26f, 0.85f,
                "Corner where the stone path bends into the sakura garden.");
            Add(target, "E_SakuraGroveNorth", RingRegion.East, 34f, 10f, 1.00f,
                "North edge of the sakura garden.");
            Add(target, "E_SakuraGroveSouth", RingRegion.East, 34f, -10f, 1.00f,
                "South edge of the sakura garden, on the way to the pond.");
            Add(target, "SE_PondApproach", RingRegion.East, 25f, -25f, 0.80f,
                "Approach to the koi pond from the east shore.");
            Add(target, "S_BridgeSouthEnd", RingRegion.South, 4f, -35f, 0.70f,
                "Far end of the narrow bridge. Deliberately low weight: this is the choke " +
                "point REQ-047 is about.");
            Add(target, "S_ShorePathWest", RingRegion.South, -14f, -31f, 0.95f,
                "Alternative shore path that runs parallel to the bridge.");
            Add(target, "SW_BambooEntry", RingRegion.West, -27f, -24f, 0.85f,
                "Southern entrance of the bamboo garden.");
            Add(target, "W_BambooCorridorSouth", RingRegion.West, -34f, -8f, 1.00f,
                "South mouth of the enclosed bamboo corridor.");
            Add(target, "W_BambooCorridorNorth", RingRegion.West, -34f, 10f, 1.00f,
                "North mouth of the enclosed bamboo corridor.");
            Add(target, "NW_ServiceGate", RingRegion.North, -26f, 25f, 0.90f,
                "Service gate behind the palace, closing the ring back to the north.");
        }

        /// <summary>Allocates and returns a fresh copy of the authored layout.</summary>
        public static List<SpawnPointDefinition> Create()
        {
            var list = new List<SpawnPointDefinition>(PointCount);
            Fill(list);
            return list;
        }

        private static void Add(List<SpawnPointDefinition> target, string id, RingRegion region,
            float x, float z, float weight, string notes)
        {
            target.Add(new SpawnPointDefinition
            {
                Id = id,
                Region = region,
                Position = new Vector3(x, 0f, z),
                BaseWeight = weight,
                Enabled = true,
                FaceMapCenter = true,
                FacingYaw = 0f,
                Notes = notes
            });
        }
    }
}
