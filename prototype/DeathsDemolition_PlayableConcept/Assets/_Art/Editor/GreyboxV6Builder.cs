using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Navigation;
using DD.Prototype.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-074 B1 · World v5 greybox (planning/work-packages/WP-074-WORLD-V5-DIRECTION.md §0 K1–K5 + Ek B §2): the ±55 m layout v6
    /// as collider boxes in its own dev-only scene <see cref="SceneName"/> — the player-facing World_AP005 is untouched. The same
    /// runtime path as the authored world (Ap005WorldBootstrap: markers from the greybox placement asset, atmosphere, NavMesh
    /// verification, run systems), loaded into Run with <c>-ddWorldSource AP005 -ddWorldScene World_V6_Greybox</c>.
    /// NavMesh v5 keeps the WP-038 rules: climb 0.30, the ring (±55, NavMesh 0.5 m inside), pond rims, fall guard, island linker.
    /// Writes the data the stakeholders read: spawn-gates-v6.json (S-03), regions-v6.json (S-03), acoustic-spaces-v6.json (S-08).
    /// WP-074 B5b-1: the scene is World v5's dev scene — <see cref="WorldV5Dress"/> dresses it with the AP-023 kit (its colliders
    /// replace the boxes they dress) before the bake; <c>-ddGreyboxOnly</c> builds the bare boxes.
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.GreyboxV6Builder.BuildCli</c>. Deterministic; rebuilt in place.
    /// </summary>
    public static class GreyboxV6Builder
    {
        public const string SceneName = ArtLibraryPaths.GreyboxV6SceneName;
        public const string ScenePath = ArtLibraryPaths.GreyboxV6Scene;
        public const string DataFolder = "Assets/_Art/Environment/World/V6";
        public const string PlacementPath = ArtLibraryPaths.GreyboxV6Placement;
        public const string NavMeshPath = DataFolder + "/NavMesh-" + SceneName + ".asset";
        public const string SpawnGatesJson = DataFolder + "/spawn-gates-v6.json";
        public const string RegionsJson = DataFolder + "/regions-v6.json";
        public const string AcousticJson = DataFolder + "/acoustic-spaces-v6.json";
        public const string GreyboxRootName = "Greybox v6";
        public const float HalfExtent = 55f, VistaOuter = 85f, WallHeight = 2.4f, WallThickness = 0.6f, KairoFloor = 0.25f, PlinthHeight = 1.2f;

        // ---------------------------------------------------------------- layout v6 data (Ek B §2, §0 decisions)

        public struct Door
        {
            public char Side;       // N/S/E/W wall of a room (axis-aligned)
            public float At;        // centre along that wall (x for N/S, z for E/W)
            public float Width;
            public Door(char side, float at, float width) { Side = side; At = at; Width = width; }
        }

        public sealed class Room
        {
            public string Id, Acoustic;
            public float X0, X1, Z0, Z1, Floor, Height;
            public Door[] Doors;
            public bool Walls = true, RoofOnly;
        }

        public sealed class Corridor
        {
            public string Id;
            public Vector2[] Path;
            public float Width, Height;
            public bool Closed;
        }

        public struct Gate
        {
            public string Id, OldId, Kind;
            public Vector2 Position;
            public Gate(string id, string oldId, string kind, float x, float z) { Id = id; OldId = oldId; Kind = kind; Position = new Vector2(x, z); }
        }

        public static readonly Room[] Rooms =
        {
            // R1 main hall on the 1.2 m plinth (§0 K2): 3 front doors + E + W (the rear door gave way to K6, see B1 note). WP-074 B5b-1:
            // on the AP-023 hall's wall lines (x ±16.5, z 39…49) and open bays (front x −9 / 0 / 9 at 2.6 m clear, E/W z 44 at 2.3 m).
            new Room { Id = "R1_MainHall", Acoustic = "Hall", X0 = -16.5f, X1 = 16.5f, Z0 = 39f, Z1 = 49f, Floor = PlinthHeight, Height = 6.5f,
                Doors = new[] { new Door('S', -9f, 2.6f), new Door('S', 0f, 2.6f), new Door('S', 9f, 2.6f), new Door('E', 44f, 2.3f), new Door('W', 44f, 2.3f) } },
            // WP-074 B5b-5: R2–R4 on the AP-023 buildings' wall lines and openings (pavilion 8.4 × 6.4, W / S / E; kura 10 × 8 with its
            // two end doors E / W — the greybox S door moves to W; tea house 6 × 6 raised 0.45, N / E entries 1.8 m off its axes).
            new Room { Id = "R2_EastPavilion", Acoustic = "Hall", X0 = 30.8f, X1 = 39.2f, Z0 = 24.8f, Z1 = 31.2f, Floor = 0.25f, Height = 3.8f,
                Doors = new[] { new Door('W', 28f, 2.46f), new Door('S', 35f, 2.76f), new Door('E', 28f, 2.46f) } },
            new Room { Id = "R3_Kura", Acoustic = "Hall", X0 = -44f, X1 = -34f, Z0 = 36f, Z1 = 44f, Floor = 0.3f, Height = 4f,
                Doors = new[] { new Door('E', 40f, 2.0f), new Door('W', 40f, 2.0f) } },
            new Room { Id = "R4_TeaHouse", Acoustic = "Hall", X0 = -35f, X1 = -29f, Z0 = -39f, Z1 = -33f, Floor = 0.45f, Height = 3.6f,
                Doors = new[] { new Door('N', -33.8f, 2.16f), new Door('E', -37.8f, 2.16f) } },
            new Room { Id = "R5_Azumaya", Acoustic = "Corridor", X0 = 30f, X1 = 36f, Z0 = -36f, Z1 = -30f, Height = 3.2f, Walls = false, RoofOnly = true },
            // v007-B: the kit chūmon × 1.2 wide (WorldV5Dress.Chumon) — its axis opening 6.0 m (C-00: ≥ 6 m), the side doors 2 m.
            new Room { Id = "R6_Chumon", Acoustic = "Corridor", X0 = -4.8f, X1 = 4.8f, Z0 = 18f, Z1 = 23f, Height = 5f,
                Doors = new[] { new Door('S', 0f, 6f), new Door('N', 0f, 6f), new Door('E', 20.5f, 2f), new Door('W', 20.5f, 2f) } },
        };

        /// <summary>A corridor's centre-line path by id (the dress lays its kit kairō on the same line).</summary>
        public static Vector2[] CorridorPath(string id)
        {
            foreach (Corridor c in Corridors) if (c.Id == id) return c.Path;
            throw new ArgumentException("no corridor " + id);
        }

        public static readonly Corridor[] Corridors =
        {
            new Corridor { Id = "K1_WestKairo", Path = new[] { new Vector2(-33f, -16f), new Vector2(-33f, 20f) }, Width = 5.2f, Height = 3.4f, Closed = true },
            // v007-B (C-00 25.09): the angled links on the kit's 30 / 45 / 60° corners (AP-023 kairo_angle: 3.6 m arms either side of
            // the vertex) — K2 two 45° right turns from K1's north end into K3's west side (junction z 30 → 28.69); K4 keeps its oblique
            // start on K3's east side (−60°, start x 26 → 25.38: the R2 pavilion's SW corner leaves no room for a turn there), 60° left
            // onto z 22, 60° right to its open end (x 44 → 45.38).
            new Corridor { Id = "K2_NorthWestLink", Path = new[] { new Vector2(-33f, 20f), new Vector2(-33f, 23.6f), new Vector2(-27.909f, 28.691f), new Vector2(-24.309f, 28.691f) }, Width = 4f, Height = 3.4f },
            new Corridor { Id = "K3_CloisterWest", Path = new[] { new Vector2(-22f, 22f), new Vector2(-22f, 36f) }, Width = 4f, Height = 3.4f },
            new Corridor { Id = "K3_CloisterEast", Path = new[] { new Vector2(22f, 22f), new Vector2(22f, 36f) }, Width = 4f, Height = 3.4f },
            new Corridor { Id = "K4_EastKairo", Path = new[] { new Vector2(25.381f, 30f), new Vector2(30f, 22f), new Vector2(45.381f, 22f), new Vector2(50f, 14f) }, Width = 4f, Height = 3.4f },
            new Corridor { Id = "K5_SouthShore", Path = new[] { new Vector2(-22f, -46f), new Vector2(0f, -46f) }, Width = 3.5f, Height = 3.2f },
            new Corridor { Id = "K6_RearKairo", Path = new[] { new Vector2(-22f, 52.5f), new Vector2(22f, 52.5f) }, Width = 4f, Height = 3.2f },
        };

        /// <summary>K1 wall openings (Ek B §2.3): east doors z −7 / 7 (4 m), west door z 0 (3 m); the two ends are open mouths.</summary>
        public static readonly (char side, float z, float width)[] K1Doors = { ('E', -7f, 4f), ('E', 7f, 4f), ('W', 0f, 3f) };

        /// <summary>Spawn gates (Ek B §3.4): the 12 edge gates keep the prototype ids (placement), the inner gates are data for S-03 — 6, plus
        /// the four of S-03's coverage scenario C (C-00 25.09; S-03's measured coordinates, bound by its test 5bb18807). Set 3 (C-00 30.09,
        /// 92232235) moved three; WP-074 B9 (M-27): the two by the R7 court are named for where they stand (were I_ShoreEast / I_GreatSakuraSouth).</summary>
        public static readonly Gate[] Gates =
        {
            new Gate("N_RearWest", "N_PalaceGateWest", "edge", -20f, 53f), new Gate("N_RearEast", "N_PalaceGateEast", "edge", 20f, 53f),
            new Gate("NE_Corner", "NE_StonePathCorner", "edge", 44f, 44f), new Gate("E_GardenNorth", "E_SakuraGroveNorth", "edge", 52f, 16f),
            new Gate("E_GardenSouth", "E_SakuraGroveSouth", "edge", 52f, -14f), new Gate("SE_Shore", "SE_PondApproach", "edge", 42f, -42f),
            new Gate("S_BridgeSouth", "S_BridgeSouthEnd", "edge", 4f, -52f), new Gate("S_ShoreWest", "S_ShorePathWest", "edge", -16f, -52f),
            new Gate("SW_TeaHouse", "SW_BambooEntry", "edge", -44f, -44f), new Gate("W_BambooSouth", "W_BambooCorridorSouth", "edge", -52f, -18f),
            new Gate("W_BambooNorth", "W_BambooCorridorNorth", "edge", -52f, 18f), new Gate("NW_ServiceGate", "NW_ServiceGate", "edge", -46f, 46f),
            new Gate("I_WestKairoSouth", null, "inner", -33f, -18.5f), new Gate("I_CloisterWest", null, "inner", -22f, 14f),
            new Gate("I_R7SouthWest", null, "inner", 24f, -8f), new Gate("I_R7NorthWest", null, "inner", 24f, 14f),
            new Gate("I_ServiceYardSouth", null, "inner", -36f, 31.5f), new Gate("I_HallRear", null, "inner", 0f, 52.5f),
            new Gate("I_CloisterEast", null, "inner", 22f, 24f), new Gate("I_ShoreWest", null, "inner", -18f, -30f),
            // I_ServiceYardEast: S-03's (−28, 40) sits on the yard's east wall (x −28) — 1.5 m inside, like I_ServiceYardSouth.
            new Gate("I_ServiceYardEast", null, "inner", -29.5f, 40f), new Gate("I_PlazaSouthWest", null, "inner", -14f, -18f),
        };

        /// <summary>Weapon points (Ek B §3.6, first 12 of 14 in the prototype id order; the last two are listed in the json).</summary>
        public static readonly Vector2[] WeaponPositions =
        {
            new Vector2(0f, 30f), new Vector2(0f, 47f), new Vector2(35f, 28f), new Vector2(36f, 1f), new Vector2(20f, -22f), new Vector2(4f, -16.5f),
            new Vector2(-32f, -36f), new Vector2(-33f, 2f), new Vector2(-38f, 32f), new Vector2(-45f, 0f), new Vector2(-10f, -44f), new Vector2(33f, -33f),
        };

        public static readonly Vector2[] ExtraWeaponPositions = { new Vector2(10f, -8f), new Vector2(30f, 20f) };

        /// <summary>Bamboo lanes (Ek B §2.3): polylines, 3 m wide; the grove is solid everywhere else in its band.</summary>
        public static readonly Vector2[][] BambooLanes =
        {
            new[] { new Vector2(-45f, -26f), new Vector2(-45f, 26f) },
            new[] { new Vector2(-36f, 0f), new Vector2(-45f, 0f) },
            new[] { new Vector2(-33f, -16f), new Vector2(-45f, -26f) },
            new[] { new Vector2(-33f, 20f), new Vector2(-45f, 26f) },
            new[] { new Vector2(-54f, -18f), new Vector2(-45f, -14f) },
            new[] { new Vector2(-54f, 18f), new Vector2(-45f, 14f) },
        };

        /// <summary>Garden paths kept clear of trees (Ek B §2.4 / §3.8), half-width 2.5 m.</summary>
        public static readonly Vector2[][] GardenPaths =
        {
            new[] { new Vector2(13f, 1f), new Vector2(27f, 1f) },
            new[] { new Vector2(36f, -8f), new Vector2(30f, -18f), new Vector2(28f, -30f) },
            new[] { new Vector2(45f, -8f), new Vector2(48f, -20f), new Vector2(40f, -34f), new Vector2(28f, -41f) },
            new[] { new Vector2(45f, 1f), new Vector2(54f, 1f) },
            new[] { new Vector2(36f, 10f), new Vector2(36f, 20f) },
            new[] { new Vector2(44f, 44f), new Vector2(40f, 40f), new Vector2(36f, 34f) },
            new[] { new Vector2(52f, 16f), new Vector2(44f, 20f) },
            new[] { new Vector2(52f, -14f), new Vector2(45f, -8f) },
            new[] { new Vector2(42f, -42f), new Vector2(34f, -38f) },
        };

        private static readonly string[] RegionIds = { "center", "north", "east", "south", "west" };

        /// <summary>regions-v6 (Ek B §5): x/z polygons.</summary>
        public static readonly float[][] RegionPolygons =
        {
            Circle(0f, 0f, 14f, 16),
            new[] { -28f, 18f, 28f, 18f, 28f, 55f, -28f, 55f },
            new[] { 26f, -40f, 55f, -40f, 55f, 55f, 28f, 55f, 28f, 20f, 26f, 20f },
            new[] { -30f, -55f, 30f, -55f, 30f, -14f, -30f, -14f },
            new[] { -55f, -46f, -26f, -46f, -26f, 30f, -28f, 30f, -28f, 55f, -55f, 55f },
        };

        // ---------------------------------------------------------------- build

        private static readonly Dictionary<string, Material> s_materials = new Dictionary<string, Material>();

        public static void BuildCli()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            string report = Build(Array.IndexOf(args, "-ddGreyboxOnly") < 0, Array.IndexOf(args, "-ddBake") >= 0);
            Debug.Log("[GreyboxV6] " + report);
        }

        /// <param name="dress">WP-074 B5b-1: dress the layout with the AP-023 kit (<see cref="WorldV5Dress"/>) before the bake.</param>
        /// <param name="bake">WP-074 B8: bake the scene's lighting after the build (WorldV5Lighting; needs a graphics device — the GPU lightmapper).</param>
        public static string Build(bool dress = true, bool bake = false)
        {
            EnsureFolder(DataFolder);
            EnsureFolder(DataFolder + "/Materials");
            s_materials.Clear();

            // The authored world's bootstrap settings and ponds are the reference (copied, not moved).
            var apPlacement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            if (apPlacement == null) throw new InvalidOperationException("AP005 placement missing");
            List<(Vector2 centre, Vector2 semi, float yaw)> basins = PlacementImporter.BasinHoles(apPlacement);
            // WP-075 C6: the dressed World v5 takes its own look profile (LUT v2); the bare greybox keeps DD_Overcast_v1.
            VolumeProfile volume = dress ? WorldV5Look.EnsureProfile() : AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GUIDToAssetPath("e99c26b47c9984ab8adfa33bbddead21"));
            if (dress) ScreenAtmosphereSetup.Apply();   // WP-075 C4 B / C5: the TP / FP screen-space shafts + height fog feature
            if (dress)   // AD MUST-1 / DEC-101: the kit roles the v6 look retints (bamboo leaves, roof tiles, plaster)
                foreach (string role in new[] { "ap023_bamboo_leaves", "ap023_bamboo_culm", "ap023_roof_tiles", "ap023_plaster" }) ArtMaterialBuilder.BuildLibraryRole(ArtLibraryMaterialRoles.Get(role));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(Ap005WorldBootstrap.RootName);
            new GameObject(Ap005WorldBootstrap.InstancesName).transform.SetParent(root.transform, false);
            var greybox = new GameObject(GreyboxRootName);
            greybox.transform.SetParent(root.transform, false);
            Transform g = greybox.transform;

            int boxes = 0;
            // Ground (to the vista edge) — the NavMesh stops at the ring.
            boxes += Box(g, "Ground", new Vector3(0f, -0.1f, 0f), new Vector3(2f * VistaOuter, 0.2f, 2f * VistaOuter), "ground");
            boxes += BuildRingWall(g);
            boxes += BuildVista(g);
            boxes += BuildArena(g);
            boxes += BuildPalace(g);
            foreach (Room r in Rooms) if (r.Id != "R1_MainHall") boxes += BuildRoom(g, r);
            foreach (Corridor c in Corridors) boxes += BuildCorridor(g, c);
            boxes += BuildServiceYard(g);
            boxes += BuildEast(g);
            boxes += BuildSouth(g, basins);
            boxes += BuildBamboo(g);
            WorldV5Dress.Report dressed = dress ? WorldV5Dress.Apply(g, root.transform) : null;
            // R3 #1 (AD 07.10 M1): the ground and the water keep the runtime ambient (the approved Look B tone), not the baked probes
            int probeOff = WorldV5Lighting.ApplyProbeUsage(root.transform);

            // WP-074 B1c · S-03 WP-076 B6 §5.2 region volumes (rooms, room exits, chokepoints).
            BuildRegionVolumes(root.transform);

            WorldPlacementAsset placement = WritePlacement(apPlacement);
            var bootstrap = root.AddComponent<Ap005WorldBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("_placement").objectReferenceValue = placement;
            so.FindProperty("_volumeProfile").objectReferenceValue = volume;
            so.ApplyModifiedPropertiesWithoutUndo();

            // NavMesh v5: ring volumes, bake at body climb, ring + rims + fall guard, island links (WP-038M/N/P/R rules).
            WorldBoundaryBuilder.BuildRingNavVolumes(root.transform, HalfExtent);
            Physics.SyncTransforms();
            int triangles = Bake(root.transform);
            WorldBoundaryBuilder.Build(root.transform, basins, HalfExtent, InPondStripExclusion);
            Physics.SyncTransforms();
            Vector3 seed = NavMesh.SamplePosition(placement.playerStart, out NavMeshHit sh, 3f, NavMesh.AllAreas) ? sh.position : placement.playerStart;
            int links = NavMeshIslandLinker.LinkIslands(root.transform, seed);

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterDevScene();
            WriteJson();
            AssetDatabase.SaveAssets();
            string baked = bake ? ", " + WorldV5Lighting.Bake(scene, root.transform) : "";
            return boxes + " boxes" + (dressed != null ? " (" + dressed + ")" : "") + ", " + probeOff + " renderers off the probes, NavMesh " + triangles + " triangles, " + links + " island links, placement " + placement.spawnPoints.Count + " spawns / " + placement.weaponPoints.Count + " weapons -> " + ScenePath + baked;
        }

        // ---------------------------------------------------------------- pieces

        private static int BuildRingWall(Transform g)
        {
            // The ring made visible (E-5): a 2.4 m tsuiji just outside the invisible ring's inner face.
            int n = 0;
            float span = 2f * (HalfExtent + WallThickness), off = HalfExtent + WallThickness * 0.5f;
            n += Box(g, "RingWall_N", new Vector3(0f, WallHeight * 0.5f, off), new Vector3(span, WallHeight, WallThickness), "wall");
            n += Box(g, "RingWall_S", new Vector3(0f, WallHeight * 0.5f, -off), new Vector3(span, WallHeight, WallThickness), "wall");
            n += Box(g, "RingWall_E", new Vector3(off, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, span), "wall");
            n += Box(g, "RingWall_W", new Vector3(-off, WallHeight * 0.5f, 0f), new Vector3(WallThickness, WallHeight, span), "wall");
            return n;
        }

        private static int BuildVista(Transform g)
        {
            // 55–85 m: walk-free depth (roof blocks, tree masses, one pagoda) behind the ring wall.
            int n = 0;
            var rng = new System.Random(740);
            for (int i = 0; i < 40; i++)
            {
                float a = (float)(i / 40.0 * Math.PI * 2.0 + rng.NextDouble() * 0.1);
                float r = 62f + (float)rng.NextDouble() * 18f;
                float x = Mathf.Clamp(Mathf.Cos(a) * r * 1.2f, -VistaOuter + 3f, VistaOuter - 3f), z = Mathf.Clamp(Mathf.Sin(a) * r * 1.2f, -VistaOuter + 3f, VistaOuter - 3f);
                if (Mathf.Abs(x) < HalfExtent + 4f && Mathf.Abs(z) < HalfExtent + 4f) continue;
                bool tree = i % 3 != 0;
                float h = tree ? 10f + (float)rng.NextDouble() * 10f : 6f + (float)rng.NextDouble() * 4f;
                float w = tree ? 5f : 10f + (float)rng.NextDouble() * 6f;
                n += Box(g, (tree ? "VistaTree_" : "VistaRoof_") + i, new Vector3(x, h * 0.5f, z), new Vector3(w, h, tree ? w : 8f), tree ? "tree" : "roof");
            }

            n += Box(g, "VistaPagoda_NE", new Vector3(70f, 14f, 70f), new Vector3(8f, 28f, 8f), "roof");
            return n;
        }

        private static int BuildArena(Transform g)
        {
            // Centre r 14 keeps its identity (nothing over 0.45 m but the four planter corners) + the axis lanterns.
            int n = 0;
            foreach (float sx in new[] { -9f, 9f })
            foreach (float sz in new[] { -9f, 9f })
                n += Box(g, "Planter_" + sx + "_" + sz, new Vector3(sx, 0.225f, sz), new Vector3(3f, 0.45f, 3f), "floor");
            foreach (float z in new[] { 14f, 8f, -14.5f })
            foreach (float x in new[] { -3f, 3f })
                n += Box(g, "Lantern_" + x + "_" + z, new Vector3(x, 1.05f, z), new Vector3(0.6f, 2.1f, 0.6f), "prop");
            // Karamon on the south axis (§0 K5): 6 m opening, four posts and a roof.
            foreach (float x in new[] { -3.2f, 3.2f })
            foreach (float z in new[] { -16.8f, -15.2f })
                n += Box(g, "Karamon_Post_" + x + "_" + z, new Vector3(x, 3f, z), new Vector3(0.45f, 6f, 0.45f), "wall");
            n += Roof(g, "Karamon_Roof", new Vector3(0f, 6.25f, -16f), new Vector3(8f, 0.5f, 3.2f));
            return n;
        }

        private static int BuildPalace(Transform g)
        {
            int n = 0;
            Room hall = Rooms[0];
            // Plinth (kidan) 1.2 m with the engawa ring; stairs 6 × 0.20 m (NavMesh climb 0.30 → a ramp).
            n += Box(g, "Hall_Plinth", new Vector3(0f, PlinthHeight * 0.5f, 43.25f), new Vector3(39f, PlinthHeight, 13.5f), "floor");
            foreach ((float x, float w) in new[] { (-10f, 3f), (0f, 6f), (10f, 3f) })
                n += Stairs(g, "Hall_StairS_" + x, new Vector2(x, 36.5f), new Vector2(0f, -1f), w);
            n += Stairs(g, "Hall_StairE", new Vector2(19.5f, 44f), new Vector2(1f, 0f), 2.4f);
            n += Stairs(g, "Hall_StairW", new Vector2(-19.5f, 44f), new Vector2(-1f, 0f), 2.4f);
            n += BuildRoom(g, hall);
            // Interior shoji partitions (x ±6, z 42…46, open ends) and the dais (+0.30).
            n += Box(g, "Hall_Screen_W", new Vector3(-6f, PlinthHeight + 1.5f, 44f), new Vector3(0.2f, 3f, 4f), "wall");
            n += Box(g, "Hall_Screen_E", new Vector3(6f, PlinthHeight + 1.5f, 44f), new Vector3(0.2f, 3f, 4f), "wall");
            n += Box(g, "Hall_Dais", new Vector3(0f, PlinthHeight + 0.15f, 47.5f), new Vector3(12f, 0.3f, 3f), "floor");
            // Forecourt walls x ±26, z 20…36 (doors z 28…32) and the south wall at z 20 around the chūmon.
            n += Wall(g, "Court_W", new Vector2(-26f, 20f), new Vector2(-26f, 36f), WallHeight, WallThickness, new[] { (30f - 20f, 4f) });
            n += Wall(g, "Court_E", new Vector2(26f, 20f), new Vector2(26f, 36f), WallHeight, WallThickness, new[] { (30f - 20f, 4f) });
            n += Wall(g, "Court_S_W", new Vector2(-26f, 20f), new Vector2(-4f, 20f), WallHeight, WallThickness, null);
            n += Wall(g, "Court_S_E", new Vector2(4f, 20f), new Vector2(26f, 20f), WallHeight, WallThickness, null);
            return n;
        }

        private static int BuildServiceYard(Transform g)
        {
            // Fenced (1.8 m) yard x −48…−28, z 30…48: S gate (x −36), E gate to K2 (z 32), W gate (z 46).
            int n = 0;
            n += Wall(g, "Yard_S", new Vector2(-48f, 30f), new Vector2(-28f, 30f), 1.8f, 0.3f, new[] { (12f, 4f) });
            n += Wall(g, "Yard_E", new Vector2(-28f, 30f), new Vector2(-28f, 48f), 1.8f, 0.3f, new[] { (2f, 4f) });
            n += Wall(g, "Yard_N", new Vector2(-48f, 48f), new Vector2(-28f, 48f), 1.8f, 0.3f, null);
            n += Wall(g, "Yard_W", new Vector2(-48f, 30f), new Vector2(-48f, 48f), 1.8f, 0.3f, new[] { (16f, 3f) });
            return n;
        }

        private static int BuildEast(Transform g)
        {
            int n = 0;
            // R7 great sakura courtyard: 1.8 m walls, 4 gates; the tree island inside.
            n += Wall(g, "R7_W", new Vector2(27f, -8f), new Vector2(27f, 10f), 1.8f, WallThickness, new[] { (9f, 4f) });
            n += Wall(g, "R7_E", new Vector2(45f, -8f), new Vector2(45f, 10f), 1.8f, WallThickness, new[] { (9f, 3.5f) });
            n += Wall(g, "R7_N", new Vector2(27f, 10f), new Vector2(45f, 10f), 1.8f, WallThickness, new[] { (9f, 4f) });
            n += Wall(g, "R7_S", new Vector2(27f, -8f), new Vector2(45f, -8f), 1.8f, WallThickness, new[] { (9f, 4f) });
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                n += Tree(g, "R7_Sakura_" + i, new Vector2(36f + Mathf.Cos(a) * 2.2f, 1f + Mathf.Sin(a) * 2.2f), 0.4f);
            }

            foreach ((string name, Vector2 p, float radius) in GardenTreeSpots()) n += Tree(g, name, p, radius);
            return n;
        }

        /// <summary>The great sakura on the R7 island (WorldV5Dress places one V01 there over the six greybox trunks).</summary>
        public static readonly Vector2 GreatSakura = new Vector2(36f, 1f);

        /// <summary>
        /// WP-074 B9 (Y7, AD panel 30.09 — author + refuter): garden spots kept open lawn although the grid would place a tree. (37.52, −13.82)
        /// opened when set 3 moved I_GreatSakuraSouth's 4 m clearance away; a tree there hid the east sakura from the west path and filled
        /// the lawn wedge on the R7 south-gate axis, so the lawn stays as the user played it and the AD accepted it (decision B).
        /// </summary>
        public static readonly Vector2[] GardenOpenSpots = { new Vector2(37.52f, -13.82f) };

        /// <summary>
        /// The sakura / maple garden and the NE pine corner: a jittered 5 m grid (seed 741, two draws per cell before the clear test,
        /// so the jitter never depends on it), kept where <see cref="TreeSpotClear"/> allows and not in <see cref="GardenOpenSpots"/>. Scene-independent — the greybox trunks, the
        /// dress (WorldV5Dress.GardenTrees) and the parity test all read this one list (WP-074 B9, Y7).
        /// </summary>
        public static List<(string name, Vector2 position, float radius)> GardenTreeSpots()
        {
            var spots = new List<(string, Vector2, float)>();
            var rng = new System.Random(741);
            for (float x = 28f; x <= 53f; x += 5f)
            for (float z = -40f; z <= 44f; z += 5f)
            {
                var p = new Vector2(x + (float)(rng.NextDouble() - 0.5) * 2.5f, z + (float)(rng.NextDouble() - 0.5) * 2.5f);
                if (TreeSpotClear(p) && !Array.Exists(GardenOpenSpots, o => Vector2.Distance(o, p) < 0.1f)) spots.Add(("Tree_" + x + "_" + z, p, z > 34f ? 0.5f : 0.35f));
            }

            return spots;
        }

        private static int BuildSouth(Transform g, List<(Vector2 centre, Vector2 semi, float yaw)> basins)
        {
            int n = 0;
            // Ponds: water surface (visual) + NotWalkable strips 0.5 m past the ellipse (the rims are built by WorldBoundaryBuilder).
            for (int b = 0; b < basins.Count; b++) n += PondStrips(g, b, basins[b]);
            // Bridge x 4, z −18…−39.5 (deck 0.25, rails), the stepping causeway x −8 with its island, karesansui, K5 is a corridor.
            Crossing c8 = Crossings[C8Bridge], c1 = Crossings[C1Causeway], island = Crossings[C1Island];
            n += Box(g, "Bridge_Deck", new Vector3(c8.Centre.x, 0.125f, c8.Centre.y), new Vector3(c8.Width, 0.25f, c8.Length), "floor");
            n += Box(g, "Bridge_Rail_W", new Vector3(c8.Centre.x - c8.Width * 0.5f + 0.05f, 0.75f, c8.Centre.y), new Vector3(0.1f, 1f, c8.Length - 2f), "wall");
            n += Box(g, "Bridge_Rail_E", new Vector3(c8.Centre.x + c8.Width * 0.5f - 0.05f, 0.75f, c8.Centre.y), new Vector3(0.1f, 1f, c8.Length - 2f), "wall");
            n += Box(g, "Causeway", new Vector3(c1.Centre.x, 0.1f, c1.Centre.y), new Vector3(c1.Width, 0.2f, c1.Length), "floor");
            n += Box(g, "Causeway_Island", new Vector3(island.Centre.x, 0.1f, island.Centre.y), new Vector3(island.Width, 0.2f, island.Length), "floor");
            n += Wall(g, "Karesansui_N", new Vector2(12f, -16f), new Vector2(28f, -16f), 0.9f, 0.4f, new[] { (6f, 3f) });
            n += Wall(g, "Karesansui_E", new Vector2(28f, -30f), new Vector2(28f, -16f), 0.9f, 0.4f, new[] { (8f, 3f) });
            foreach (Vector2 rock in new[] { new Vector2(16f, -20f), new Vector2(22f, -19f), new Vector2(25f, -25f), new Vector2(15f, -27f), new Vector2(20f, -25.5f) })
                n += Box(g, "Karesansui_Rock_" + rock.x + "_" + rock.y, new Vector3(rock.x, 0.6f, rock.y), new Vector3(1.6f, 1.2f, 1.2f), "prop");
            return n;
        }

        private static int BuildBamboo(Transform g)
        {
            // Grove band x −54…−37.5, z −30…30: solid (6 m) except the 3 m lanes, 3 m round the spawn gates and the K1 doors.
            int n = 0;
            for (float x = -54f; x < -37.5f; x += 1f)
            {
                float runStart = float.NaN;
                for (float z = -30f; z <= 30.001f; z += 1f)
                {
                    var c = new Vector2(x + 0.5f, z + 0.5f);
                    bool solid = z < 30f && GroveCellSolid(c);
                    if (solid && float.IsNaN(runStart)) runStart = z;
                    if (!solid && !float.IsNaN(runStart))
                    {
                        n += Box(g, "Bamboo_" + x + "_" + runStart, new Vector3(x + 0.5f, 3f, (runStart + z) * 0.5f), new Vector3(1f, 6f, z - runStart), "tree");
                        runStart = float.NaN;
                    }
                }
            }

            return n;
        }

        public static bool GroveCellSolid(Vector2 c)
        {
            foreach (Vector2[] lane in BambooLanes)
                if (DistanceToPolyline(c, lane) < 1.5f + 0.71f) return false;
            foreach (Gate gate in Gates)
                if (Vector2.Distance(c, gate.Position) < 3.5f) return false;
            return true;
        }

        /// <summary>
        /// WP-076 B9 (M-28, C-00 30.09; S-05 handshake): the builder's reserved zones — the R7 walls + island block and the
        /// karesansui — where no tree, and no spawn gate, goes. Shared with S-03's SpawnGateCoverageProbe so a gate set is
        /// judged against the same rule the builder applies.
        /// </summary>
        public static bool InReservedZone(Vector2 p, out string zone)
        {
            if (p.x > 25f && p.x < 47f && p.y > -10f && p.y < 12f) { zone = "R7"; return true; }           // R7 walls + island
            if (p.x > 10f && p.x < 30f && p.y > -32f && p.y < -14f) { zone = "Karesansui"; return true; }   // karesansui
            zone = null;
            return false;
        }

        private static bool TreeSpotClear(Vector2 p)
        {
            if (Mathf.Abs(p.x) > HalfExtent - 2f || Mathf.Abs(p.y) > HalfExtent - 2f) return false;
            foreach (Vector2[] path in GardenPaths) if (DistanceToPolyline(p, path) < 2.5f) return false;
            foreach (Corridor c in Corridors) if (DistanceToPolyline(p, c.Path) < c.Width * 0.5f + 1.5f) return false;
            foreach (Room r in Rooms) if (p.x > r.X0 - 2f && p.x < r.X1 + 2f && p.y > r.Z0 - 2f && p.y < r.Z1 + 2f) return false;
            if (InReservedZone(p, out _)) return false;   // R7 walls + island, karesansui (WP-076 B9: shared with the spawn probe)
            foreach (Gate gate in Gates) if (Vector2.Distance(p, gate.Position) < 4f) return false;
            foreach (Vector2 w in WeaponPositions) if (Vector2.Distance(p, w) < 3f) return false;
            return true;
        }

        /// <summary>S-03 §5.2 room capacities (bodies queued before a room counts as full; R5 azumaya open, R7 the sakura court).</summary>
        public static readonly Dictionary<string, int> RoomCapacity = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "R1_MainHall", 8 }, { "R2_EastPavilion", 6 }, { "R3_Kura", 6 }, { "R4_TeaHouse", 4 }, { "R5_Azumaya", 3 }, { "R6_Chumon", 3 }, { R7Id, 8 }
        };

        public const string R7Id = "R7_SakuraCourt", RegionGroupName = "Region Volumes";
        /// <summary>A door no wider than this is a chokepoint (Ek B §3.3: clear width ≤ 2.5 m).</summary>
        public const float ChokepointWidth = 2.5f;

        /// <summary>
        /// WP-074 B1c · The region volumes S-03's router reads (NavRegionMarker, WP-076 B6 §5.2): a Room per room (R1–R6 + the R7 sakura
        /// court; capacity per <see cref="RoomCapacity"/>, cost 1.0); a Gate per room door (roomId = the room): a chokepoint door
        /// (≤ 2.5 m) queues 2 (≤ 2.2 m) or 3 (2.4 m) at cost 1.15 with the nearest ≥ 3.5 m opening within 15 m as its alternative, a
        /// wider door is an exit only (capacity 0, cost 1.0); the C8 bridge (Bridge, 2, 1.35), the C1 stepping causeway (Corridor, 1,
        /// 1.3) and the closed K1 kairō (Corridor, 6, 1.3). World-axis boxes; the markers register themselves in play.
        /// </summary>
        public static int BuildRegionVolumes(Transform root)
        {
            var group = new GameObject(RegionGroupName).transform;
            group.SetParent(root, false);
            var openings = new List<(string room, string id, Vector3 centre, float width, char side, float floor)>();
            foreach (Room r in Rooms)
            {
                if (r.Doors == null) continue;
                var perSide = new Dictionary<char, int>();
                foreach (Door d in r.Doors)
                {
                    perSide.TryGetValue(d.Side, out int k);
                    perSide[d.Side] = k + 1;
                    Vector3 c = d.Side == 'S' ? new Vector3(d.At, r.Floor, r.Z0) : d.Side == 'N' ? new Vector3(d.At, r.Floor, r.Z1)
                        : d.Side == 'W' ? new Vector3(r.X0, r.Floor, d.At) : new Vector3(r.X1, r.Floor, d.At);
                    openings.Add((r.Id, r.Id + "_" + d.Side + k, c, d.Width, d.Side, r.Floor));
                }
            }

            // R7 great sakura court: 1.8 m walls at x 27…45 / z −8…10, one gate per wall 9 m along (BuildEast).
            openings.Add((R7Id, R7Id + "_W0", new Vector3(27f, 0f, 1f), 4f, 'W', 0f));
            openings.Add((R7Id, R7Id + "_E0", new Vector3(45f, 0f, 1f), 3.5f, 'E', 0f));
            openings.Add((R7Id, R7Id + "_N0", new Vector3(36f, 0f, 10f), 4f, 'N', 0f));
            openings.Add((R7Id, R7Id + "_S0", new Vector3(36f, 0f, -8f), 4f, 'S', 0f));

            int n = 0;
            foreach (Room r in Rooms)
            {
                float h = r.Height;
                Marker(group, r.Id, NavRegionKind.Room, new Vector3((r.X0 + r.X1) * 0.5f, r.Floor + h * 0.5f, (r.Z0 + r.Z1) * 0.5f), new Vector3(r.X1 - r.X0, h, r.Z1 - r.Z0),
                    1f, RoomCapacity[r.Id], null, "");
                n++;
            }

            Marker(group, R7Id, NavRegionKind.Room, new Vector3(36f, 2f, 1f), new Vector3(18f, 4f, 18f), 1f, RoomCapacity[R7Id], null, "");
            n++;
            foreach (var o in openings)
            {
                bool choke = o.width <= ChokepointWidth + 1e-3f;
                Vector3? alt = null;
                if (choke)
                {
                    float best = 15f;
                    foreach (var other in openings)
                    {
                        if (other.id == o.id || other.width < 3.5f - 1e-3f) continue;
                        float dist = Vector3.Distance(other.centre, o.centre);
                        if (dist <= best) { best = dist; alt = other.centre; }
                    }
                }

                Vector3 size = o.side == 'S' || o.side == 'N' ? new Vector3(o.width, 2.4f, 1.6f) : new Vector3(1.6f, 2.4f, o.width);
                Marker(group, o.id, NavRegionKind.Gate, o.centre + Vector3.up * 1.2f, size, choke ? 1.15f : 1f, choke ? (o.width <= 2.2f + 1e-3f ? 2 : 3) : 0, alt, o.room);
                n++;
            }

            Crossing c8 = Crossings[C8Bridge], c1 = Crossings[C1Causeway];
            Marker(group, c8.Id, NavRegionKind.Bridge, new Vector3(c8.Centre.x, 0.25f + 1.2f, c8.Centre.y), new Vector3(c8.Width, 2.4f, c8.Length), 1.35f, 2, C8ShoreAlternative, "");
            Marker(group, c1.Id, NavRegionKind.Corridor, new Vector3(c1.Centre.x, 0.2f + 1.2f, c1.Centre.y), new Vector3(c1.Width, 2.4f, c1.Length), 1.3f, 1, C1ShoreAlternative, "");
            Marker(group, "K1_WestKairo", NavRegionKind.Corridor, new Vector3(-33f, KairoFloor + 1.7f, 2f), new Vector3(5.2f, 3.4f, 36f), 1.3f, 6, null, "");
            return n + 3;
        }

        private static void Marker(Transform group, string id, NavRegionKind kind, Vector3 centre, Vector3 size, float cost, int capacity, Vector3? alternative, string roomId)
        {
            var go = new GameObject("Region_" + id);
            go.transform.SetParent(group, false);
            go.transform.position = centre;
            go.AddComponent<NavRegionMarker>().Configure(id, kind, size, null, cost, capacity, alternative.HasValue, alternative ?? Vector3.zero, roomId);
        }

        private static int BuildRoom(Transform g, Room r)
        {
            int n = 0;
            float t = 0.3f, y = r.Floor;
            if (r.Walls)
            {
                n += Wall(g, r.Id + "_S", new Vector2(r.X0, r.Z0), new Vector2(r.X1, r.Z0), r.Height, t, Gaps(r, 'S', r.X0), y);
                n += Wall(g, r.Id + "_N", new Vector2(r.X0, r.Z1), new Vector2(r.X1, r.Z1), r.Height, t, Gaps(r, 'N', r.X0), y);
                n += Wall(g, r.Id + "_W", new Vector2(r.X0, r.Z0), new Vector2(r.X0, r.Z1), r.Height, t, Gaps(r, 'W', r.Z0), y);
                n += Wall(g, r.Id + "_E", new Vector2(r.X1, r.Z0), new Vector2(r.X1, r.Z1), r.Height, t, Gaps(r, 'E', r.Z0), y);
            }
            else
            {
                foreach (float x in new[] { r.X0, r.X1 })
                foreach (float z in new[] { r.Z0, r.Z1 })
                    n += Box(g, r.Id + "_Post_" + x + "_" + z, new Vector3(x, y + r.Height * 0.5f, z), new Vector3(0.3f, r.Height, 0.3f), "wall");
            }

            n += Roof(g, r.Id + "_Roof", new Vector3((r.X0 + r.X1) * 0.5f, y + r.Height + 0.25f, (r.Z0 + r.Z1) * 0.5f), new Vector3(r.X1 - r.X0 + 1.2f, 0.5f, r.Z1 - r.Z0 + 1.2f));
            return n;
        }

        private static (float at, float width)[] Gaps(Room r, char side, float origin)
        {
            var list = new List<(float, float)>();
            if (r.Doors != null)
                foreach (Door d in r.Doors)
                    if (d.Side == side) list.Add((d.At - origin, d.Width));
            return list.ToArray();
        }

        private static int BuildCorridor(Transform g, Corridor c)
        {
            int n = 0;
            for (int i = 0; i + 1 < c.Path.Length; i++)
            {
                Vector2 a = c.Path[i], b = c.Path[i + 1], d = (b - a).normalized, side = new Vector2(-d.y, d.x);
                float len = Vector2.Distance(a, b), yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                Vector2 m = (a + b) * 0.5f;
                // Floor +0.25 (§0 K3: under the 0.30 climb with margin), roof above.
                n += Box(g, c.Id + "_Floor_" + i, new Vector3(m.x, KairoFloor * 0.5f, m.y), new Vector3(c.Width, KairoFloor, len + (i + 2 < c.Path.Length ? c.Width : 0f)), "floor", yaw);
                n += Roof(g, c.Id + "_Roof_" + i, new Vector3(m.x, KairoFloor + c.Height + 0.15f, m.y), new Vector3(c.Width + 1f, 0.3f, len + 0.6f), yaw);
                if (c.Closed)
                {
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector2 off = side * s * (c.Width * 0.5f);
                        char wallSide = s < 0 ? 'E' : 'W';
                        var gaps = new List<(float, float)>();
                        foreach ((char ds, float dz, float dw) in K1Doors) if (ds == wallSide) gaps.Add((dz - a.y, dw));
                        n += Wall(g, c.Id + "_Wall" + wallSide, a + off, b + off, c.Height, 0.3f, gaps.ToArray(), KairoFloor);
                    }
                }
                else
                {
                    // Open sides: posts on both outer lines every 2.4 m (never inside the clear width).
                    for (float u = 0f; u <= len + 0.01f; u += 2.4f)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector2 p = a + d * u + side * s * (c.Width * 0.5f + 0.14f);
                        n += Box(g, c.Id + "_Post_" + i + "_" + u.ToString("F1", CultureInfo.InvariantCulture) + "_" + s, new Vector3(p.x, KairoFloor + c.Height * 0.5f, p.y), new Vector3(0.27f, c.Height, 0.27f), "wall");
                    }
                }
            }

            return n;
        }

        private static int PondStrips(Transform g, int index, (Vector2 centre, Vector2 semi, float yaw) b)
        {
            int n = 0;
            float grow = 0.5f, a = b.semi.x + grow, c = b.semi.y + grow;
            // N3 (WP-081 B, menu-n2-n4 RAPOR §N3 b): the builder's own ellipse disc in place of the 20-sided Cylinder — the same local
            // contract (unit disc of radius 0.5 at local y 1, the transform's scale is the ellipse), so the shore, the lotus, the
            // atmosphere sheets and the NotWalkable rims read it as before; vertex colour R = depth ramp, G = foam band (DD_Water_v2).
            var water = new GameObject("Pond" + index + "_Water");   // the Default layer, as the Cylinder primitive had
            water.transform.SetParent(g, false);
            water.transform.position = new Vector3(b.centre.x, 0.02f, b.centre.y);
            water.transform.rotation = Quaternion.Euler(0f, -b.yaw, 0f);
            water.transform.localScale = new Vector3(b.semi.x * 2f, 0.01f, b.semi.y * 2f);
            water.AddComponent<MeshFilter>().sharedMesh = PondWaterMesh(index, b.semi);
            water.AddComponent<MeshRenderer>().sharedMaterial = Mat("water");
            n++;
            // NotWalkable strips along the ellipse's local x (1 m), local height −1…0.1 m so bridge/causeway tops stay walkable.
            // WP-075 (C-00 28.09, zombies held at the C1 slab joint and the C8 plank ends): the AP-023 decks top out at 0.15–0.16 m, a
            // voxel above the strips' 0.1 m — the bake carved holes in them, the island linker bridged the holes with links the bodies
            // stop at, and the rim builder, finding no NavMesh there, fenced the C1 causeway. The strips now skip the deck footprints
            // (PondStripExclusions): the decks bake whole, the rims leave their crossing gaps.
            float ang = b.yaw * Mathf.Deg2Rad;
            Vector2 ToWorld(float x, float z) => b.centre + new Vector2(x * Mathf.Cos(ang) - z * Mathf.Sin(ang), x * Mathf.Sin(ang) + z * Mathf.Cos(ang));
            for (float lx = -a; lx < a; lx += 1f)
            {
                float mid = lx + 0.5f, half = c * Mathf.Sqrt(Mathf.Max(0f, 1f - (mid * mid) / (a * a)));
                if (half < 0.05f) continue;
                var runs = new List<(float z0, float z1)>();
                float start = float.NaN;
                for (float z = -half; z <= half + 1e-4f; z += PondStripStep)
                {
                    bool deck = InPondStripExclusion(ToWorld(mid - 0.51f, z)) || InPondStripExclusion(ToWorld(mid, z)) || InPondStripExclusion(ToWorld(mid + 0.51f, z));
                    if (!deck && float.IsNaN(start)) start = z;
                    if (deck && !float.IsNaN(start)) { runs.Add((start, z - PondStripStep)); start = float.NaN; }
                }

                if (!float.IsNaN(start)) runs.Add((start, half));
                for (int r = 0; r < runs.Count; r++)
                {
                    (float z0, float z1) = runs[r];
                    if (z1 - z0 < 0.05f) continue;
                    string name = "Pond" + index + "_NotWalkable_" + lx.ToString("F0", CultureInfo.InvariantCulture) + (runs.Count > 1 ? "_" + r : string.Empty);
                    var go = new GameObject(name) { layer = PrototypeLayers.Environment };
                    go.transform.SetParent(g, false);
                    Vector2 world = ToWorld(mid, (z0 + z1) * 0.5f);
                    go.transform.position = new Vector3(world.x, -0.45f, world.y);
                    go.transform.rotation = Quaternion.Euler(0f, -b.yaw, 0f);
                    NavMeshModifierVolume v = go.AddComponent<NavMeshModifierVolume>();
                    v.center = Vector3.zero;
                    v.size = new Vector3(1.02f, 1.1f, z1 - z0);
                    v.area = ArtSurfaceImporter.NavAreaNotWalkable;
                }
            }

            return n;
        }

        public const float PondStripStep = 0.1f;

        /// <summary>A pond crossing's deck: centre (world XZ), width across (x) and length along (z).</summary>
        public readonly struct Crossing
        {
            public readonly string Id;
            public readonly Vector2 Centre;
            public readonly float Width, Length;

            public Crossing(string id, float x, float z, float width, float length)
            {
                Id = id;
                Centre = new Vector2(x, z);
                Width = width;
                Length = length;
            }

            public Vector2 Min => Centre - new Vector2(Width, Length) * 0.5f;
            public Vector2 Max => Centre + new Vector2(Width, Length) * 0.5f;
        }

        public const int C8Bridge = 0, C1Causeway = 1, C1Island = 2;

        /// <summary>
        /// WP-074 B9 (M-39) · The crossings — one source for the greybox decks and rails, the C8 / C1 region markers, the pond-strip
        /// exclusions and the AP-023 dress (WorldV5Dress.Bridges): the C8 plank bridge (x 4, 3 m, z −39.5…−18) and the C1 slab
        /// causeway (x −8, 2 m, z −37…−20) with its 3 m island. WP-088 (R2): C1 ended at z −33, 2.4 m short of Pond1's south rim
        /// (−35.4) — every path from its south end turned back over it; now it lands 1.6 m onto the south bank like the north end.
        /// </summary>
        public static readonly Crossing[] Crossings =
        {
            new Crossing("C8_Bridge", 4f, -28.75f, 3f, 21.5f),
            new Crossing("C1_SteppingCauseway", -8f, -28.5f, 2f, 17f),
            new Crossing("C1_Island", -8f, -27f, 3f, 3f),
        };

        /// <summary>
        /// WP-088 (R2) · The shore alternatives of the two crossings (NavRegionMarker.alternative; S-03's router sends a crowd round
        /// the pond when the deck is full): measured on the V6 NavMesh as the shortest waypoint whose path north end → waypoint →
        /// south end stays off both decks. C8: the east shore, 36.4 m against 24.4 m over the bridge (×1.49), 2.25 m from the nearest
        /// NavMesh edge (open — the V6 stall there was the router sending a body back to it, RegionNavRouter.AlternativeAhead);
        /// C1 (once it reached the south bank): the west shore at the deck's middle, 23.4 m against 20.0 m (×1.17).
        /// </summary>
        public static readonly Vector3 C8ShoreAlternative = new Vector3(16f, 0.1f, -30.8f), C1ShoreAlternative = new Vector3(-13.3f, 0.1f, -28.5f);

        /// <summary>The deck footprints the pond NotWalkable strips leave out (world XZ min / max) — the <see cref="Crossings"/>.</summary>
        public static readonly (Vector2 min, Vector2 max)[] PondStripExclusions = Array.ConvertAll(Crossings, c => (c.Min, c.Max));

        /// <summary>N3 · pond water disc: <see cref="PondWaterSegments"/> rim vertices.</summary>
        public const int PondWaterSegments = 128;

        /// <summary>N3 · metric insets of the disc's rings from the shore; the depth ramp reaches 1 at <see cref="PondDepthRamp"/>, the foam band is <see cref="PondFoamBand"/> wide.</summary>
        public static readonly float[] PondRingInsets = { 0f, 0.15f, 0.3f, 0.6f, 1.2f, 2.4f };
        public const float PondDepthRamp = 1.2f, PondFoamBand = 0.3f;

        public static string PondWaterMeshPath(int index) => DataFolder + "/V6_Pond" + index + "_Water.asset";

        /// <summary>
        /// N3 · the pond's water as a unit disc (radius 0.5, local y 1 = the old Cylinder's top face) for the transform's ellipse scale
        /// (2 × <paramref name="semi"/>): rings at <see cref="PondRingInsets"/> metres in from the shore measured on the ellipse, a centre
        /// vertex; colour R = clamp01(inset / <see cref="PondDepthRamp"/>), G = 1 within <see cref="PondFoamBand"/> of the shore. Saved
        /// (or rewritten in place) at <see cref="PondWaterMeshPath"/>.
        /// </summary>
        public static Mesh PondWaterMesh(int index, Vector2 semi)
        {
            int seg = PondWaterSegments, rings = PondRingInsets.Length;
            var verts = new List<Vector3>(seg * rings + 1);
            var colors = new List<Color>(seg * rings + 1);
            var uvs = new List<Vector2>(seg * rings + 1);
            for (int k = 0; k < rings; k++)
            {
                float inset = PondRingInsets[k];
                for (int i = 0; i < seg; i++)
                {
                    float t = i * Mathf.PI * 2f / seg, cos = Mathf.Cos(t), sin = Mathf.Sin(t);
                    float radius = Mathf.Sqrt(semi.x * cos * semi.x * cos + semi.y * sin * semi.y * sin);   // metres to the rim along this ray
                    float r = Mathf.Clamp01(1f - inset / Mathf.Max(0.01f, radius));
                    var v = new Vector3(0.5f * r * cos, 1f, 0.5f * r * sin);
                    verts.Add(v);
                    uvs.Add(new Vector2(v.x + 0.5f, v.z + 0.5f));
                    colors.Add(new Color(Mathf.Clamp01(inset / PondDepthRamp), inset <= PondFoamBand + 1e-4f ? 1f : 0f, 0f, 1f));
                }
            }

            int centre = verts.Count;
            verts.Add(new Vector3(0f, 1f, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            colors.Add(new Color(1f, 0f, 0f, 1f));
            var tris = new List<int>(seg * rings * 6);
            for (int k = 0; k + 1 < rings; k++)
            {
                for (int i = 0; i < seg; i++)
                {
                    int a = k * seg + i, b = k * seg + (i + 1) % seg, c = (k + 1) * seg + i, d = (k + 1) * seg + (i + 1) % seg;
                    tris.AddRange(new[] { a, c, b, b, c, d });   // clockwise from above: the face looks up
                }
            }

            int last = (rings - 1) * seg;
            for (int i = 0; i < seg; i++) tris.AddRange(new[] { last + i, centre, last + (i + 1) % seg });

            string path = PondWaterMeshPath(index);
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = "V6_Pond" + index + "_Water" };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        public static bool InPondStripExclusion(Vector2 p)
        {
            foreach ((Vector2 min, Vector2 max) in PondStripExclusions)
                if (p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y) return true;
            return false;
        }

        private static int Stairs(Transform g, string name, Vector2 top, Vector2 outward, float width)
        {
            int n = 0;
            float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
            for (int s = 0; s < 6; s++)
            {
                float h = PlinthHeight - (s + 1) * 0.2f + 0.2f;   // 1.2, 1.0 … 0.2 (top tread flush with the plinth edge)
                Vector2 c = top + outward * (0.2f + s * 0.4f);
                if (h <= 0.01f) continue;
                n += Box(g, name + "_" + s, new Vector3(c.x, (h - 0.2f) * 0.5f + 0.0f, c.y), new Vector3(width, Mathf.Max(0.2f, h - 0.2f), 0.4f), "floor", yaw);
            }

            return n;
        }

        private static int Wall(Transform g, string name, Vector2 a, Vector2 b, float height, float thickness, (float at, float width)[] gaps, float baseY = 0f)
        {
            // Solid spans between the gaps (gap centres measured from a along a→b).
            float len = Vector2.Distance(a, b);
            Vector2 d = (b - a) / Mathf.Max(1e-4f, len);
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            var cuts = new List<(float s, float e)>();
            if (gaps != null) foreach ((float at, float w) in gaps) cuts.Add((at - w * 0.5f, at + w * 0.5f));
            cuts.Sort((x, y) => x.s.CompareTo(y.s));
            int n = 0;
            float cursor = 0f;
            foreach ((float s, float e) in cuts)
            {
                if (s > cursor + 0.05f) n += Span(cursor, s);
                cursor = Mathf.Max(cursor, e);
            }

            if (len > cursor + 0.05f) n += Span(cursor, len);
            return n;

            int Span(float s0, float s1)
            {
                Vector2 m = a + d * ((s0 + s1) * 0.5f);
                return Box(g, name + "_" + s0.ToString("F1", CultureInfo.InvariantCulture), new Vector3(m.x, baseY + height * 0.5f, m.y), new Vector3(thickness, height, s1 - s0), "wall", yaw);
            }
        }

        private static int Tree(Transform g, string name, Vector2 p, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.layer = PrototypeLayers.Environment;
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3(p.x, 2.5f, p.y);
            go.transform.localScale = new Vector3(radius * 2f, 2.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat("tree");
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            return 1;
        }

        private static int Roof(Transform g, string name, Vector3 centre, Vector3 size, float yaw = 0f)
        {
            // Roofs: CameraOccluder layer (top-down fade), CameraOccluderClass Roof (TP does not fade them — WP-070 E-20), out of the bake.
            Box(g, name, centre, size, "roof", yaw, PrototypeLayers.CameraOccluder);
            Transform t = g.Find(name);
            t.gameObject.AddComponent<CameraOccluderClass>().Kind = CameraOccluderKind.Roof;
            t.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            return 1;
        }

        private static int Box(Transform g, string name, Vector3 centre, Vector3 size, string material, float yaw = 0f, int layer = -1)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer >= 0 ? layer : PrototypeLayers.Environment;
            go.transform.SetParent(g, false);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(material);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
            return 1;
        }

        private static Material Mat(string key)
        {
            if (s_materials.TryGetValue(key, out Material m)) return m;
            string path = DataFolder + "/Materials/Greybox_" + key + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }

            Color c = key switch
            {
                "ground" => new Color(0.42f, 0.44f, 0.45f),
                "wall" => new Color(0.78f, 0.76f, 0.70f),
                "roof" => new Color(0.30f, 0.32f, 0.35f),
                "floor" => new Color(0.55f, 0.50f, 0.44f),
                "water" => new Color(0.30f, 0.42f, 0.46f),
                "tree" => new Color(0.36f, 0.48f, 0.30f),
                _ => new Color(0.62f, 0.60f, 0.56f),
            };
            m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            s_materials[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- NavMesh, placement, data

        private static int Bake(Transform root)
        {
            var go = new GameObject("NavMesh Surface");
            go.transform.SetParent(root, false);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3(0f, 26f * 0.5f - 4f, 0f);
            surface.size = new Vector3(2f * HalfExtent + 6f, 26f, 2f * HalfExtent + 6f);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            surface.defaultArea = 0;
            surface.overrideVoxelSize = true;
            surface.voxelSize = PlacementImporter.NavMeshVoxelSize;
            surface.minRegionArea = PlacementImporter.NavMeshMinRegionArea;
            surface.buildHeightMesh = false;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            Physics.SyncTransforms();
            PlacementImporter.BuildAtBodyClimb(surface);
            NavMeshData data = surface.navMeshData;
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(data, existing);
                existing.name = Path.GetFileNameWithoutExtension(NavMeshPath);
                surface.navMeshData = existing;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                data.name = Path.GetFileNameWithoutExtension(NavMeshPath);
                AssetDatabase.CreateAsset(data, NavMeshPath);
            }

            surface.RemoveData();
            surface.AddData();
            return NavMesh.CalculateTriangulation().indices.Length / 3;
        }

        private static WorldPlacementAsset WritePlacement(WorldPlacementAsset ap)
        {
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(PlacementPath);
            if (placement == null)
            {
                placement = ScriptableObject.CreateInstance<WorldPlacementAsset>();
                AssetDatabase.CreateAsset(placement, PlacementPath);
            }

            placement.schema = "dd.world-placement/greybox-v6";
            placement.space = ap.space;
            placement.package = "WP-074 B1";
            placement.version = "v6-greybox";
            placement.playerStart = ap.playerStart;
            placement.instances = new List<WorldPlacementAsset.Instance>();
            placement.spawnPoints = new List<WorldPlacementAsset.Point>();
            foreach (WorldPlacementAsset.Point old in ap.spawnPoints)
                foreach (Gate gate in Gates)
                    if (gate.OldId == old.id)
                        placement.spawnPoints.Add(new WorldPlacementAsset.Point { id = old.id, position = new Vector3(gate.Position.x, 0f, gate.Position.y), weight = old.weight });
            // WP-074 B1b · the v6 layout (WorldLayout.Current) and the six inner gates (S-03 WP-076 B6: SpawnPointMarker Kind = Inner,
            // ids from spawn-gates-v6.json) at their floor height — a ray from 2.5 m (under the roofs) to the deck (corridors +0.25).
            placement.layout = "v6";
            placement.innerSpawnPoints = new List<WorldPlacementAsset.Point>();
            Physics.SyncTransforms();
            int floorMask = ~((1 << PrototypeLayers.CameraOccluder) | (1 << 2));
            foreach (Gate gate in Gates)
            {
                if (gate.Kind != "inner") continue;
                var from = new Vector3(gate.Position.x, 2.5f, gate.Position.y);
                float y = Physics.Raycast(from, Vector3.down, out RaycastHit hit, 5f, floorMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
                placement.innerSpawnPoints.Add(new WorldPlacementAsset.Point { id = gate.Id, position = new Vector3(gate.Position.x, y, gate.Position.y), weight = 1f });
            }

            placement.weaponPoints = new List<WorldPlacementAsset.Point>();
            for (int i = 0; i < ap.weaponPoints.Count && i < WeaponPositions.Length; i++)
                placement.weaponPoints.Add(new WorldPlacementAsset.Point { id = ap.weaponPoints[i].id, position = new Vector3(WeaponPositions[i].x, 0f, WeaponPositions[i].y), weight = ap.weaponPoints[i].weight });
            placement.regions = new List<WorldPlacementAsset.Region>();
            for (int r = 0; r < RegionIds.Length; r++)
            {
                WorldPlacementAsset.Region old = ap.regions.Find(x => x.id == RegionIds[r]);
                placement.regions.Add(new WorldPlacementAsset.Region { id = RegionIds[r], label = old != null ? old.label : RegionIds[r], identity = old != null ? old.identity : "", sightlineClass = old != null ? old.sightlineClass : "", polygonXZ = RegionPolygons[r] });
            }

            EditorUtility.SetDirty(placement);
            return placement;
        }

        /// <summary>C-00 25.09: World v5 is the default world — a release entry right after World_AP005 (the release list stays contiguous:
        /// Boot … Results, World_AP005, World_V6_Greybox, then the dev-only pilot and showcase), and DD_WorldSource on V6.</summary>
        private static void RegisterDevScene()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            int at = scenes.FindIndex(s => s.path == ArtLibraryPaths.Ap005WorldScene);
            scenes.Insert(at >= 0 ? at + 1 : scenes.Count, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            PlacementImporter.EnsureWorldSourceAsset(WorldSource.V6);
        }

        private static void WriteJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema\": \"dd.spawn-gates/v6\",\n  \"source\": \"WP-074 B1 greybox (Ek B §3.4); proposal to S-03 WP-076\",\n  \"halfExtent\": 55,\n  \"playerStart\": [0, -3],\n  \"gates\": [\n");
            for (int i = 0; i < Gates.Length; i++)
            {
                Gate gate = Gates[i];
                sb.Append("    { \"id\": \"").Append(gate.Id).Append("\", \"prototypeId\": ").Append(gate.OldId != null ? "\"" + gate.OldId + "\"" : "null")
                  .Append(", \"kind\": \"").Append(gate.Kind).Append("\", \"x\": ").Append(F(gate.Position.x)).Append(", \"z\": ").Append(F(gate.Position.y)).Append(" }").Append(i + 1 < Gates.Length ? ",\n" : "\n");
            }

            sb.Append("  ],\n  \"weapons\": [");
            var weapons = new List<Vector2>(WeaponPositions);
            weapons.AddRange(ExtraWeaponPositions);
            for (int i = 0; i < weapons.Count; i++) sb.Append(i == 0 ? "" : ", ").Append("[").Append(F(weapons[i].x)).Append(", ").Append(F(weapons[i].y)).Append("]");
            sb.Append("]\n}\n");
            File.WriteAllText(SpawnGatesJson, sb.ToString());

            sb.Clear();
            sb.Append("{\n  \"schema\": \"dd.regions/v6\",\n  \"halfExtent\": 55,\n  \"regions\": [\n");
            for (int r = 0; r < RegionIds.Length; r++)
                sb.Append("    { \"id\": \"").Append(RegionIds[r]).Append("\", \"polygonXZ\": [").Append(string.Join(", ", Array.ConvertAll(RegionPolygons[r], F))).Append("] }").Append(r + 1 < RegionIds.Length ? ",\n" : "\n");
            sb.Append("  ],\n  \"rooms\": [\n");
            for (int i = 0; i < Rooms.Length; i++)
            {
                Room room = Rooms[i];
                sb.Append("    { \"id\": \"").Append(room.Id).Append("\", \"box\": [").Append(F(room.X0)).Append(", ").Append(F(room.Z0)).Append(", ").Append(F(room.X1)).Append(", ").Append(F(room.Z1))
                  .Append("], \"floor\": ").Append(F(room.Floor)).Append(", \"exits\": ").Append(room.Doors != null ? room.Doors.Length : 4).Append(" }").Append(i + 1 < Rooms.Length ? ",\n" : "\n");
            }

            sb.Append("  ]\n}\n");
            File.WriteAllText(RegionsJson, sb.ToString());

            sb.Clear();
            sb.Append("{\n  \"schema\": \"dd.acoustic-spaces/v6\",\n  \"note\": \"WP-074 §3 / Ek B §4; Enclosure Open | Corridor | Hall; outside every space = Open\",\n  \"spaces\": [\n");
            var spaces = new List<string>();
            foreach (Room room in Rooms)
            {
                var thresholds = new List<string>();
                if (room.Doors != null)
                    foreach (Door d in room.Doors)
                    {
                        Vector2 p = d.Side == 'N' ? new Vector2(d.At, room.Z1) : d.Side == 'S' ? new Vector2(d.At, room.Z0) : d.Side == 'E' ? new Vector2(room.X1, d.At) : new Vector2(room.X0, d.At);
                        thresholds.Add("[" + F(p.x) + ", " + F(p.y) + ", " + F(d.Width) + "]");
                    }
                spaces.Add("    { \"id\": \"" + room.Id + "\", \"enclosure\": \"" + room.Acoustic + "\", \"polygonXZ\": [" + F(room.X0) + ", " + F(room.Z0) + ", " + F(room.X1) + ", " + F(room.Z0) + ", " + F(room.X1) + ", " + F(room.Z1) + ", " + F(room.X0) + ", " + F(room.Z1)
                           + "], \"floor\": " + F(room.Floor) + ", \"ceiling\": " + F(room.Floor + room.Height) + ", \"areaM2\": " + F((room.X1 - room.X0) * (room.Z1 - room.Z0)) + ", \"thresholds\": [" + string.Join(", ", thresholds) + "] }");
            }

            foreach (Corridor c in Corridors)
            {
                var poly = new List<string>();
                for (int i = 0; i + 1 < c.Path.Length; i++)
                {
                    Vector2 a = c.Path[i], b = c.Path[i + 1], d = (b - a).normalized, side = new Vector2(-d.y, d.x) * (c.Width * 0.5f);
                    poly.Add("[" + string.Join(", ", F((a - side).x), F((a - side).y), F((b - side).x), F((b - side).y), F((b + side).x), F((b + side).y), F((a + side).x), F((a + side).y)) + "]");
                }

                spaces.Add("    { \"id\": \"" + c.Id + "\", \"enclosure\": \"Corridor\", \"closed\": " + (c.Closed ? "true" : "false") + ", \"segmentsXZ\": [" + string.Join(", ", poly) + "], \"ceiling\": " + F(KairoFloor + c.Height) + " }");
            }

            sb.Append(string.Join(",\n", spaces)).Append("\n  ]\n}\n");
            File.WriteAllText(AcousticJson, sb.ToString());
            AssetDatabase.ImportAsset(SpawnGatesJson);
            AssetDatabase.ImportAsset(RegionsJson);
            AssetDatabase.ImportAsset(AcousticJson);
        }

        // ---------------------------------------------------------------- helpers

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static float[] Circle(float cx, float cz, float r, int n)
        {
            var p = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                p[2 * i] = cx + Mathf.Cos(a) * r;
                p[2 * i + 1] = cz + Mathf.Sin(a) * r;
            }

            return p;
        }

        public static float DistanceToPolyline(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], ab = line[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }

            return best;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
