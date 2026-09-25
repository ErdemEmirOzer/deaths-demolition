using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// The gameplay positions: twelve enemy spawn points, twelve Special Weapon points
    /// and the player start.
    ///
    /// WHY THE COORDINATES HERE DIFFER FROM THE AUTHORED LAYOUT
    /// -------------------------------------------------------
    /// <see cref="DefaultSpawnPointLayout"/> was written before the map existed and says
    /// so; its coordinates are a guess at a topology that had not been built yet. The
    /// contract in <see cref="SpawnPointMarker"/> is that a marker's transform WINS over
    /// the layout, and that is exactly what this file uses. Two of the twelve were
    /// genuinely wrong once the geometry existed and are corrected here:
    ///
    ///   N_PalaceGateWest / N_PalaceGateEast   z 34 -> 30. The layout put both gates
    ///       inside the palace footprint (the building runs z 34..44). They now stand on
    ///       the forecourt directly in front of the two gate alcoves, which is what the
    ///       ids always meant.
    ///   S_BridgeSouthEnd                      z -35 -> -41. z -35 is the MIDDLE of the
    ///       bridge, on the deck, 1.4 m in the air. The "far end" is where the bridge
    ///       lands on the outer shore road.
    ///   NE_StonePathCorner                    z 26 -> 22. The corner the id names is
    ///       where the corner WATCHTOWER stands: its base occupies x 23.6..30.4,
    ///       z 24.6..31.4, so (24, 26) was four metres INSIDE a stone building. The
    ///       NavMesh self-check saw it as PathPartial and it was the map's one REQ-068
    ///       violation. z 22 is the same bend, three metres clear of the base, on the
    ///       stone the forecourt band and the north-east descent actually share.
    ///
    /// The other nine were already on walkable route surface and moved by less than a
    /// metre, or not at all.
    ///
    /// HOW PRIVATE SERIALIZED FIELDS GET WRITTEN
    /// -----------------------------------------
    /// <see cref="SpawnPointMarker"/> and <see cref="SpecialWeaponSpawnPoint"/> belong to
    /// other tasks and keep their fields private, which is correct. Rather than reflect
    /// into them, this uses <see cref="JsonUtility.FromJsonOverwrite"/>, which writes
    /// exactly the fields Unity itself serialises and nothing else.
    ///
    /// The spawn marker objects are created INACTIVE and activated afterwards, because
    /// SpawnPointMarker.OnEnable reads the id and warns about an empty one; AddComponent
    /// on an inactive object does not fire OnEnable, so the id is always in place first.
    /// </summary>
    public static class WorldMarkers
    {
        public struct SpawnPlacement
        {
            public readonly string Id;
            public readonly float X;
            public readonly float Z;
            public readonly string Where;
            /// <summary>WP-074 B1b · edge gate (default) or an inner gate (S-03 v6 rules; SpawnPointMarker._kind).</summary>
            public readonly SpawnPointKind Kind;

            public SpawnPlacement(string id, float x, float z, string where) : this(id, x, z, where, SpawnPointKind.Edge)
            {
            }

            public SpawnPlacement(string id, float x, float z, string where, SpawnPointKind kind)
            {
                Id = id;
                X = x;
                Z = z;
                Where = where;
                Kind = kind;
            }
        }

        /// <summary>The twelve enemy entrances, at their real positions on the built map.</summary>
        public static readonly SpawnPlacement[] Spawns =
        {
            new SpawnPlacement("N_PalaceGateWest", -8f, 30f,
                "Forecourt stone, directly in front of the west palace gate alcove."),
            new SpawnPlacement("N_PalaceGateEast", 8f, 30f,
                "Forecourt stone, in front of the east palace gate alcove."),
            new SpawnPlacement("NE_StonePathCorner", 24f, 22f,
                "Corner where the 14 m forecourt band bends south into the sakura garden, " +
                "clear of the watchtower that stands on the bend itself."),
            new SpawnPlacement("E_SakuraGroveNorth", 34f, 10f,
                "North end of the sakura garden, on the ring between the two forks."),
            new SpawnPlacement("E_SakuraGroveSouth", 34f, -10f,
                "South end of the sakura garden, on the ring heading for the pond."),
            new SpawnPlacement("SE_PondApproach", 25f, -25f,
                "Junction where the ring, the east shore road and the SE radial meet."),
            new SpawnPlacement("S_BridgeSouthEnd", 4f, -41f,
                "Outer shore road at the far foot of the narrow bridge."),
            new SpawnPlacement("S_ShorePathWest", -14f, -31f,
                "West shore road, the alternative that parallels the bridge (REQ-110)."),
            new SpawnPlacement("SW_BambooEntry", -27f, -24f,
                "Junction of the SW radial, the pagoda spur and the ring climb."),
            new SpawnPlacement("W_BambooCorridorSouth", -33f, -11f,
                "Inside the enclosed corridor, one metre from its south mouth."),
            new SpawnPlacement("W_BambooCorridorNorth", -33f, 11f,
                "Inside the enclosed corridor, one metre from its north mouth."),
            new SpawnPlacement("NW_ServiceGate", -26f, 25f,
                "West end of the forecourt band, where the ring closes back to the north.")
        };

        public struct WeaponPlacement
        {
            public readonly string Name;
            public readonly float X;
            public readonly float Z;
            public readonly float Weight;
            public readonly bool OffRoute;
            public readonly string Why;

            public WeaponPlacement(string name, float x, float z, float weight, bool offRoute, string why)
            {
                Name = name;
                X = x;
                Z = z;
                Weight = weight;
                OffRoute = offRoute;
                Why = why;
            }
        }

        /// <summary>
        /// REQ-054. Five of the twelve are <c>OffRoute</c>: reachable, but not on the lap
        /// the player is already running. Without them the drop lands wherever the player
        /// happens to be and the requirement's whole point - "leave your safe route" -
        /// evaporates.
        /// </summary>
        public static readonly WeaponPlacement[] Weapons =
        {
            new WeaponPlacement("SW_CourtyardNorth", -1f, 9f, 0.9f, false,
                "Centre gravel, at the mouth of the palace road. The safe, obvious one."),
            new WeaponPlacement("SW_PalaceVeranda", 0f, 33f, 1.0f, false,
                "On the palace veranda between the two gates. Wide open, long sightlines."),
            new WeaponPlacement("SW_ForecourtEast", 17f, 26f, 1.0f, false,
                "Forecourt band under the watchtower."),
            new WeaponPlacement("SW_GreatSakuraFork", 32f, 4f, 1.0f, false,
                "Between the inner and outer fork around the Great Sakura."),
            new WeaponPlacement("SW_SakuraDeepEast", 41f, 15f, 1.2f, true,
                "Deep in the east grove behind a low garden wall. No route within 8 m."),
            new WeaponPlacement("SW_PondEastShore", 19.5f, -28f, 1.2f, true,
                "Off the east shore road with the pond at your back. Nowhere to retreat."),
            new WeaponPlacement("SW_BridgeCrown", 4f, -28.75f, 0.8f, true,
                "The crown of the narrow bridge. The choke point, deliberately weighted " +
                "down: it is the highest risk position on the map."),
            new WeaponPlacement("SW_PagodaBase", -27.5f, -29f, 1.0f, false,
                "On the pagoda spur in the south-west."),
            new WeaponPlacement("SW_BambooDeepWest", -42.5f, 2f, 1.2f, true,
                "Deep bamboo west of the bypass. Enclosed, and you must come back out."),
            new WeaponPlacement("SW_CorridorDoor", -33f, 0f, 1.0f, false,
                "Inside the enclosed corridor at the mid doorway."),
            new WeaponPlacement("SW_BellTowerBack", -36f, 32f, 1.2f, true,
                "Behind the bell tower in the north-west. Completely off the ring."),
            new WeaponPlacement("SW_ShoreWestBend", -15f, -37f, 1.0f, false,
                "West shore road, the safe parallel to the bridge.")
        };

        /// <summary>
        /// Creates the markers and returns the positions foliage must keep clear of.
        /// </summary>
        /// <summary>
        /// WP-014 · One marker set: the twelve spawns, the twelve weapon points and the
        /// player start with explicit world positions. The procedural world fills it from
        /// the tables above (heights from its own terrain); the AP-005 world fills it from
        /// placement.json (ADR-014-1). Same ids either way, so the spawn director and the
        /// weapon selector never learn which world they are in.
        /// </summary>
        public struct MarkerSet
        {
            public SpawnPlacement[] Spawns;
            public Vector3[] SpawnPositions;
            public WeaponPlacement[] Weapons;
            public Vector3[] WeaponPositions;
            public Vector3 PlayerStart;
        }

        public static MarkerSet ProceduralSet()
        {
            var set = new MarkerSet
            {
                Spawns = Spawns,
                SpawnPositions = new Vector3[Spawns.Length],
                Weapons = Weapons,
                WeaponPositions = new Vector3[Weapons.Length],
                PlayerStart = WorldLayout.PlayerStart
            };

            for (int i = 0; i < Spawns.Length; i++)
            {
                set.SpawnPositions[i] = Ground(Spawns[i].X, Spawns[i].Z);
            }

            for (int i = 0; i < Weapons.Length; i++)
            {
                set.WeaponPositions[i] = Ground(Weapons[i].X, Weapons[i].Z);
            }

            return set;
        }

        public static List<Vector2> Build(Transform root)
        {
            return Build(root, ProceduralSet());
        }

        /// <summary>
        /// WP-074 B5b-5 · The set the last <see cref="Build(Transform, MarkerSet)"/> placed (Spawns == null before any build).
        /// WorldNavigation verifies these positions, not the prototype constants: in the v6 layout the constants' spots (±45 m
        /// layout) are other places, and a kit boulder on one of them turned the check red while the real gates were fine.
        /// </summary>
        public static MarkerSet Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent() => Current = default;

        public static List<Vector2> Build(Transform root, MarkerSet set)
        {
            Current = set;
            var keepOut = new List<Vector2>(set.Spawns.Length + set.Weapons.Length + 1);

            // The courtyard IS the origin, so the ring classifier needs no offset - but
            // say it out loud rather than relying on the default, because the default is
            // what RingRegionMap resets to and a silent agreement is not an agreement.
            RingRegionMap.MapCenter = Vector3.zero;
            RingRegionMap.CenterRadius = WorldLayout.CenterRadius;

            BuildSpawns(WorldPiece.Group(root, "Spawn Points"), set, keepOut);
            BuildWeapons(WorldPiece.Group(root, "Special Weapon Points"), set, keepOut);
            BuildPlayerStart(root, set.PlayerStart, keepOut);

            return keepOut;
        }

        private static void BuildSpawns(Transform group, MarkerSet set, List<Vector2> keepOut)
        {
            for (int i = 0; i < set.Spawns.Length; i++)
            {
                SpawnPlacement p = set.Spawns[i];

                var go = new GameObject("Spawn_" + p.Id);
                go.SetActive(false);
                go.transform.SetParent(group, false);
                go.transform.position = set.SpawnPositions[i];
                go.transform.rotation = FaceCentre(p.X, p.Z);

                SpawnPointMarker marker = go.AddComponent<SpawnPointMarker>();
                JsonUtility.FromJsonOverwrite(
                    "{\"_pointId\":\"" + p.Id + "\"," +
                    "\"_overrideRegion\":false," +
                    "\"_baseWeightOverride\":-1.0," +
                    "\"_pointEnabled\":true," +
                    "\"_kind\":" + (int)p.Kind + "," +
                    "\"_gizmoRadius\":1.2}", marker);

                go.SetActive(true);
                keepOut.Add(new Vector2(p.X, p.Z));
            }
        }

        private static void BuildWeapons(Transform group, MarkerSet set, List<Vector2> keepOut)
        {
            for (int i = 0; i < set.Weapons.Length; i++)
            {
                WeaponPlacement p = set.Weapons[i];

                var go = new GameObject(p.Name);
                go.SetActive(false);
                go.transform.SetParent(group, false);
                go.transform.position = set.WeaponPositions[i];

                SpecialWeaponSpawnPoint point = go.AddComponent<SpecialWeaponSpawnPoint>();
                JsonUtility.FromJsonOverwrite(
                    "{\"_weight\":" + p.Weight.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                    "\"_offRoute\":" + (p.OffRoute ? "true" : "false") + "," +
                    "\"_hoverHeight\":0.85}", point);

                go.SetActive(true);
                keepOut.Add(new Vector2(p.X, p.Z));
            }
        }

        private static void BuildPlayerStart(Transform root, Vector3 playerStart, List<Vector2> keepOut)
        {
            var go = new GameObject("Player Start (Round 1)");
            go.SetActive(false);
            go.transform.SetParent(root, false);
            go.transform.position = playerStart;

            // Facing 0 is the camera's forward, which is +Z: the player opens the run
            // looking up the palace road at the main gate and the palace roof behind it.
            go.AddComponent<PlayerSpawner>();

            go.SetActive(true);
            keepOut.Add(new Vector2(playerStart.x, playerStart.z));
        }

        /// <summary>
        /// Puts a marker on the surface instead of at y = 0, which matters for the three
        /// positions that are not on flat ground: the veranda, the courtyard dais and the
        /// crown of the bridge.
        /// </summary>
        private static Vector3 Ground(float x, float z)
        {
            WorldLayout.TryGroundHeight(x, z, out float y);

            if (WorldLayout.IsInCourtyard(x, z) &&
                x > -9.9f && x < -2.1f && z > 1.1f && z < 8.9f)
            {
                y = WorldLayout.DaisY;
            }

            if (Mathf.Abs(x) <= 16f && z >= 32f && z <= 34f)
            {
                y = WorldLayout.VerandaY;
            }

            return new Vector3(x, y + 0.05f, z);
        }

        private static Quaternion FaceCentre(float x, float z)
        {
            var toCentre = new Vector3(-x, 0f, -z);
            return toCentre.sqrMagnitude < 0.01f
                ? Quaternion.identity
                : Quaternion.LookRotation(toCentre.normalized, Vector3.up);
        }
    }
}
