using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 B · World v5 top-down layout: the layout plan (K/wp-092/layout, schema <see cref="Schema"/>, copied to <see cref="LayoutJson"/>)
    /// on top of the v6 dress. It adds the enterable buildings (nagaya rows of AP-023 kairō bays, the kit pavilion, the kit buildings
    /// re-dressed), whose shells fade top-down as one so the room inside reads; their furniture and lamps (AP-030); the narrative clusters
    /// and the moments. GreyboxV6Builder runs it after WorldV5Dress and before the NavMesh bake, and the buildings join the rooms (region
    /// volumes, acoustic spaces) through <see cref="Rooms"/>. A family whose kit is not in the project yet (AP-030 before its delivery) is
    /// skipped and counted, never guessed; an AP-023 family that does not exist fails the build.
    /// Coordinates: Unity world metres (+X east, +Z north); a yaw is Unity's (degrees about +Y, clockwise seen from above).
    /// </summary>
    public static class WorldV5Layout
    {
        public const string LayoutJson = GreyboxV6Builder.DataFolder + "/layout-v6.json";
        public const string Schema = "wp092.layout.v1";
        public const string RootName = "Layout v5 (WP-092)";
        public const string BuildingGroupName = "Buildings", ClusterGroupName = "Clusters";
        public const string ShellSuffix = "_Shell", InteriorSuffix = "_Interior", WallOccluderName = "WallOccluder";
        public const string LampPrefix = "Lamp_", LampLightName = "LampLight", SocketLight = "SOCKET_Light", SocketFlame = "SOCKET_Flame";
        public const string Ap023 = "AP-023", Ap030 = "AP-030";

        /// <summary>
        /// The kairō bay (AP023-build-architecture-a-v001.py): a straight bay is 2.4 m joint to joint, its floor ±2.37, its wall lines ±2.235,
        /// walkable at +0.25. The end bay (V06 wall, V07 step entry) is half a bay: its end on its pivot line, its joint 1.2 m behind.
        /// </summary>
        public const float BayLength = 2.4f, HalfBay = 1.2f, WallLine = 2.235f, BayHalfWidth = 2.37f, KitFloor = 0.25f, NagayaHeight = 3.2f;

        /// <summary>The faded camera-side facade: from just over the floor to the wall plate (the camera line to a body by that wall crosses it ≈ 1.6 m up).</summary>
        public const float WallFadeBottom = 0.6f, WallFadeTop = 3.3f, WallFadeThickness = 0.1f;

        public const float DefaultLampRange = 4f, DefaultLampIntensity = 1.5f, DefaultKelvin = 2600f, SocketFallbackHeight = 0.6f;

        /// <summary>AD (AP-030 v001, 07.10): small lamps lit, ≥ 2400 K, reach ≤ 4 m, soft shadows — the plan's values are held to these.</summary>
        public const float MinKelvin = 2400f, MaxKelvin = 2700f, MaxSmallLampRange = 4f;

        /// <summary>The kit buildings the dress already placed (WorldV5Dress.KitBuildings) by their plan id: re-dressed, their shells now fade as one.</summary>
        public static readonly string[] ShellFadeKitBuildings = { "R2", "R3", "R4" };

        /// <summary>Collider tops the gameplay review bans (GAMEPLAY-CONSTRAINTS: a shot at feet + 1.39 m grazes them, a body reads them as a step).</summary>
        public const float BannedTopMin = 0.31f, BannedTopMax = 0.49f;

        /// <summary>Vegetation the dress planted where a new building now stands (a tree through a roof): removed, counted.</summary>
        private static readonly string[] ClearedFamilies = { "bamboo_v2", "bamboo_stem", "bamboo_canopy", "black_pine", "sakura_v2", "momiji", "sugi", "satsuki", "groundcover", "violet_tree" };

        // ---------------------------------------------------------------- the plan

        public sealed class Door
        {
            public float X, Z, Width;
            public char Facing;   // N / E / S / W: the way out
        }

        /// <summary>One kit piece of a nagaya as the plan lays it: the piece and variation, its pivot, the world direction of the kit's +X
        /// (+X / −X / +Z / −Z), and for a door piece the side its opening faces.</summary>
        public sealed class Bay
        {
            public string Piece;
            public int Variation;
            public float X, Z;
            public string LocalX;
            public char Door;
        }

        public sealed class Building
        {
            public string Id, Name, Recipe, Acoustic, SouthWall;
            public float X0, Z0, X1, Z1, Yaw, Height, Floor;
            public bool New;   // a building the plan adds (B1–B6); the others (R1–R4, K1) are re-dressed where they stand
            public int Capacity;
            public readonly List<Bay> Bays = new List<Bay>();
            public readonly List<Door> Doors = new List<Door>();
            public bool IsNagaya => Recipe == "nagaya";
            public bool IsExisting => !New;
        }

        public sealed class Item
        {
            public string Id, Group, Family, Source, Role;
            public int Variation = 1;
            public float X, Z, Y, Yaw, Scale = 1f, ColTop, Footprint;
            public bool Collider = true;
        }

        public sealed class Lamp
        {
            public string Id, Group, Family, Source, Light;
            public bool Lit = true;
            public int Variation = 1;
            public float X, Z, Y, Yaw, Kelvin = DefaultKelvin, Range = DefaultLampRange, Intensity = DefaultLampIntensity;
        }

        /// <summary>A hanging rope (M7's festival line): a sagging tube from one eave to another, no collider.</summary>
        public sealed class Rope
        {
            public string Id, Group;
            public Vector3 From, To;
            public float Sag = 0.5f, Diameter = 0.025f;
            public int Segments = 16;
            public bool CastShadows;
        }

        public sealed class Plan
        {
            public string Version;
            public readonly List<Building> Buildings = new List<Building>();
            public readonly List<Item> Items = new List<Item>();
            public readonly List<Lamp> Lamps = new List<Lamp>();
            public readonly List<Rope> Ropes = new List<Rope>();
        }

        /// <summary>What the pass placed (the builder's report line and the tests).</summary>
        public sealed class Report
        {
            public int Buildings, Bays, Items, Lamps, BakedLights, PoolLamps, Emissive, Unlit, Ropes, Cleared;
            public readonly SortedSet<string> SkippedFamilies = new SortedSet<string>(StringComparer.Ordinal);
            public int Skipped;
            public readonly List<string> BannedTops = new List<string>(), Warnings = new List<string>();
            public override string ToString() => "layout v5: " + Buildings + " buildings (" + Bays + " bays), " + Items + " items, " + Lamps + " lamps (" + BakedLights + " baked lights, "
                                                 + PoolLamps + " pool, " + Emissive + " emissive, " + Unlit + " unlit), " + Ropes + " ropes, " + Cleared + " plants cleared, " + Skipped + " skipped"
                                                 + (SkippedFamilies.Count > 0 ? " [" + string.Join(", ", SkippedFamilies) + "]" : "") + ", " + BannedTops.Count + " banned collider tops, "
                                                 + Warnings.Count + " warnings";
        }

        /// <summary>The plan at <paramref name="path"/>, or null when there is none (the world builds as before).</summary>
        public static Plan Load(string path = LayoutJson)
        {
            if (!File.Exists(path)) return null;
            Dictionary<string, object> root = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(path)), "layout");
            string schema = ArtJson.AsString(ArtJson.Require(root, "schema"), "schema");
            if (schema != Schema) throw new FormatException("[LAYOUT] " + path + " schema " + schema + " (want " + Schema + ")");
            var plan = new Plan { Version = Str(root, "version", "") };
            foreach (object o in List(root, "buildings"))
            {
                Dictionary<string, object> b = ArtJson.AsObject(o, "building");
                var building = new Building
                {
                    Id = ArtJson.AsString(ArtJson.Require(b, "id"), "building id"), Name = Str(b, "name", ""), Recipe = Str(b, "recipe", "nagaya"),
                    Acoustic = Str(b, "acoustic", null), SouthWall = Str(b, "southWall", "fade"), Yaw = Num(b, "yaw", 0f), Height = Num(b, "height", NagayaHeight),
                    Floor = Num(b, "floorY", KitFloor), Capacity = (int)Num(b, "capacity", 0f), New = Bool(b, "new", false),
                };
                List<object> rect = ArtJson.AsList(ArtJson.Require(b, "rect"), building.Id + " rect");
                building.X0 = Mathf.Min(ArtJson.AsFloat(rect[0]), ArtJson.AsFloat(rect[2]));
                building.X1 = Mathf.Max(ArtJson.AsFloat(rect[0]), ArtJson.AsFloat(rect[2]));
                building.Z0 = Mathf.Min(ArtJson.AsFloat(rect[1]), ArtJson.AsFloat(rect[3]));
                building.Z1 = Mathf.Max(ArtJson.AsFloat(rect[1]), ArtJson.AsFloat(rect[3]));
                foreach (object o2 in List(b, "bays"))
                {
                    Dictionary<string, object> bay = ArtJson.AsObject(o2, building.Id + " bay");
                    List<object> pivot = ArtJson.AsList(ArtJson.Require(bay, "pivot"), building.Id + " bay pivot");
                    string door = Str(bay, "door", null);
                    building.Bays.Add(new Bay
                    {
                        Piece = ArtJson.AsString(ArtJson.Require(bay, "piece"), "bay piece"), Variation = Variation(ArtJson.Require(bay, "var")),
                        X = ArtJson.AsFloat(pivot[0]), Z = ArtJson.AsFloat(pivot[1]), LocalX = Str(bay, "localX", "+X"), Door = door != null ? char.ToUpperInvariant(door[0]) : '\0',
                    });
                }

                foreach (object d in List(b, "doors"))
                {
                    Dictionary<string, object> door = ArtJson.AsObject(d, building.Id + " door");
                    string facing = Str(door, "facing", "S");
                    building.Doors.Add(new Door { X = Num(door, "x", 0f), Z = Num(door, "z", 0f), Width = Num(door, "width", 2.0f), Facing = char.ToUpperInvariant(facing[0]) });
                }

                plan.Buildings.Add(building);
            }

            foreach (object o in List(root, "placements"))
            {
                Dictionary<string, object> p = ArtJson.AsObject(o, "placement");
                plan.Items.Add(new Item
                {
                    Id = Str(p, "id", ""), Group = Str(p, "group", ""), Family = ArtJson.AsString(ArtJson.Require(p, "family"), "family"), Source = Str(p, "source", Ap023),
                    Role = Str(p, "role", "dressing"), Variation = (int)Num(p, "variation", 1f), X = Num(p, "x", 0f), Z = Num(p, "z", 0f), Y = Num(p, "y", 0f),
                    Yaw = Num(p, "yaw", 0f), Scale = Num(p, "scale", 1f), Collider = Bool(p, "collider", true), ColTop = Num(p, "colTop", 0f),
                    Footprint = MaxOf(List(p, "footprint")),
                });
            }

            foreach (object o in List(root, "lamps"))
            {
                Dictionary<string, object> l = ArtJson.AsObject(o, "lamp");
                plan.Lamps.Add(new Lamp
                {
                    Id = Str(l, "id", ""), Group = Str(l, "group", ""), Family = ArtJson.AsString(ArtJson.Require(l, "family"), "family"), Source = Str(l, "source", Ap030),
                    Light = Str(l, "light", "baked"), Lit = Bool(l, "lit", true), Variation = (int)Num(l, "variation", 1f), X = Num(l, "x", 0f), Z = Num(l, "z", 0f), Y = Num(l, "y", 0f),
                    Yaw = Num(l, "yaw", 0f), Kelvin = Num(l, "kelvin", DefaultKelvin), Range = Num(l, "range", DefaultLampRange), Intensity = Num(l, "intensity", DefaultLampIntensity),
                });
            }

            foreach (object o in List(root, "ropes"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "rope");
                plan.Ropes.Add(new Rope
                {
                    Id = ArtJson.AsString(ArtJson.Require(r, "id"), "rope id"), Group = Str(r, "group", ""), From = Vec(ArtJson.Require(r, "from"), "rope from"),
                    To = Vec(ArtJson.Require(r, "to"), "rope to"), Sag = Num(r, "sag", 0.5f), Diameter = Num(r, "diameter", 0.025f), Segments = (int)Num(r, "segments", 16f),
                    CastShadows = Bool(r, "castShadows", false),
                });
            }

            return plan;
        }

        /// <summary>"V06" or 6.</summary>
        private static int Variation(object v)
        {
            if (v is string s) return int.Parse(s.TrimStart('V', 'v'), System.Globalization.CultureInfo.InvariantCulture);
            return ArtJson.AsInt(v, "variation");
        }

        /// <summary>[x, y, z] or {x, y, z} (y up).</summary>
        private static Vector3 Vec(object v, string what)
        {
            if (v is List<object> l) return new Vector3(ArtJson.AsFloat(l[0], what), ArtJson.AsFloat(l[1], what), ArtJson.AsFloat(l[2], what));
            Dictionary<string, object> o = ArtJson.AsObject(v, what);
            return new Vector3(Num(o, "x", 0f), Num(o, "y", 0f), Num(o, "z", 0f));
        }

        // ---------------------------------------------------------------- rooms (regions, acoustic spaces)

        /// <summary>The plan's buildings as rooms: the wall rectangle, the kit floor, the doors on their walls; capacity by area unless authored.</summary>
        public static List<GreyboxV6Builder.Room> Rooms(Plan plan)
        {
            var rooms = new List<GreyboxV6Builder.Room>();
            if (plan == null) return rooms;
            foreach (Building b in plan.Buildings)
            {
                if (b.IsExisting) continue;   // R1–R4 are GreyboxV6Builder.Rooms already, K1 a corridor
                Rect r = WallRect(b);
                var doors = new List<GreyboxV6Builder.Door>();
                foreach (Door d in b.Doors) doors.Add(new GreyboxV6Builder.Door(d.Facing, d.Facing == 'N' || d.Facing == 'S' ? d.X : d.Z, d.Width));
                float area = r.width * r.height;
                rooms.Add(new GreyboxV6Builder.Room
                {
                    Id = b.Id, Acoustic = b.Acoustic, X0 = r.xMin, X1 = r.xMax, Z0 = r.yMin, Z1 = r.yMax, Floor = b.Floor, Height = b.Height,
                    Doors = doors.ToArray(), Capacity = b.Capacity > 0 ? b.Capacity : area < 30f ? 3 : area < 60f ? 4 : 6,
                });
            }

            return rooms;
        }

        /// <summary>The building's wall rectangle in world x / z (the plan's rect: a nagaya's floor 4.74 m wide, end wall to end).</summary>
        public static Rect WallRect(Building b) => Rect.MinMaxRect(b.X0, b.Z0, b.X1, b.Z1);

        /// <summary>Probe volumes for the interiors (the 6 m world grid cannot hold a lamp's pool): every plan building's wall rectangle, floor to ceiling.</summary>
        public static List<Bounds> InteriorVolumes(Plan plan)
        {
            var list = new List<Bounds>();
            if (plan == null) return list;
            foreach (Building b in plan.Buildings)
            {
                Rect r = WallRect(b);
                list.Add(new Bounds(new Vector3(r.center.x, b.Floor + b.Height * 0.5f, r.center.y), new Vector3(r.width, b.Height, r.height)));
            }

            return list;
        }

        // ---------------------------------------------------------------- the pass

        public static Report Apply(Transform worldRoot, Plan plan)
        {
            var report = new Report();
            if (plan == null) return report;
            var root = new GameObject(RootName).transform;
            root.SetParent(worldRoot, false);
            var buildings = new GameObject(BuildingGroupName).transform;
            buildings.SetParent(root, false);
            var clusters = new GameObject(ClusterGroupName).transform;
            clusters.SetParent(root, false);
            var interiors = new Dictionary<string, Transform>(StringComparer.Ordinal);

            foreach (Building b in plan.Buildings)
            {
                Transform shell = null;
                if (b.New)
                {
                    report.Cleared += ClearPlants(worldRoot, WallRect(b), 0.8f);
                    shell = b.IsNagaya ? Nagaya(buildings, b, report) : KitBuilding(buildings, b, report);
                    report.Buildings++;
                }
                else if (Array.IndexOf(ShellFadeKitBuildings, b.Id) >= 0) shell = Existing(worldRoot, b, report);

                if (shell != null && b.SouthWall == "fade") SouthWallOccluder(shell, b);
                var interior = new GameObject(b.Id + InteriorSuffix).transform;
                interior.SetParent(buildings, false);
                interiors[b.Id] = interior;
            }

            foreach (Rope rope in plan.Ropes)
            {
                RopeMesh(Group(clusters, interiors, rope.Group), rope);
                report.Ropes++;
            }

            foreach (Item item in plan.Items)
            {
                Transform parent = Group(clusters, interiors, item.Group);
                GameObject go = TryPlace(parent, item.Source, item.Family, item.Variation, item.X, item.Z, item.Y, item.Yaw, item.Scale, RoleOf(item), report);
                if (go == null) continue;
                go.name = string.IsNullOrEmpty(item.Id) ? go.name : item.Id + "_" + item.Family;
                if (!item.Collider) WorldV5Dress.Visual(go);
                else AuditTop(go, item, report);
                report.Items++;
            }

            foreach (Lamp lamp in plan.Lamps)
            {
                Transform parent = Group(clusters, interiors, lamp.Group);
                GameObject go = TryPlace(parent, lamp.Source, lamp.Family, lamp.Variation, lamp.X, lamp.Z, lamp.Y, lamp.Yaw, 1f, "dressing", report);
                if (go == null) continue;
                go.name = LampPrefix + lamp.Id + "_" + (lamp.Lit ? lamp.Light : "unlit");
                WorldV5Dress.Visual(go);   // lamps are not obstacles (hanging, on furniture, by a door)
                LampLight(go, lamp, report);
                report.Lamps++;
            }

            return report;
        }

        private static Transform Group(Transform clusters, Dictionary<string, Transform> interiors, string group)
        {
            if (!string.IsNullOrEmpty(group) && interiors.TryGetValue(group, out Transform interior)) return interior;
            string name = string.IsNullOrEmpty(group) ? "Ungrouped" : group;
            Transform t = clusters.Find(name);
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(clusters, false);
            return t;
        }

        /// <summary>Plants stay probe-lit dressing; a piece of ≥ 2 m footprint is lightmapped like a structure; the rest (props, debris) is probe-lit dressing.</summary>
        private static string RoleOf(Item item)
        {
            if (Array.IndexOf(PlantFamilies, item.Family) >= 0) return "dressing";
            return item.Footprint >= 2f ? "structure" : "dressing";
        }

        private static readonly string[] PlantFamilies = { "satsuki", "groundcover", "pond_reeds", "lotus", "bamboo_fallen" };

        /// <summary>A plan piece, or null (counted) when its AP-030 kit is not in the project yet. An AP-023 piece that does not exist throws.</summary>
        private static GameObject TryPlace(Transform parent, string source, string family, int variation, float x, float z, float y, float yaw, float scale, string role, Report report)
        {
            string ap = string.IsNullOrEmpty(source) ? Ap023 : source;
            string prefab = ArtLibraryPaths.Prefab(ap, family, "SM_" + family + "_V" + variation.ToString("00"));
            if (ap != Ap023 && AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null)
            {
                report.Skipped++;
                report.SkippedFamilies.Add(ap + "/" + family + "_V" + variation.ToString("00"));
                return null;
            }

            return WorldV5Dress.Place(parent, ap, family, variation, Mz(x, z, y, -yaw) * Matrix4x4.Scale(Vector3.one * scale), Vector3.zero, role);
        }

        /// <summary>Blender-space placement matrix (x, y = Unity z, z = height; a counter-clockwise yaw about Blender +Z).</summary>
        private static Matrix4x4 Mz(float x, float y, float z, float degrees)
        {
            return Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.AngleAxis(degrees, Vector3.forward), Vector3.one);
        }

        // ---------------------------------------------------------------- buildings

        /// <summary>
        /// A nagaya (row house) from the kit's kairō pieces exactly as the plan lays them (bays[]: piece, variation, pivot, the world way of the
        /// kit's +X): kairo_bay V02 closed bays (rails off, it has none), V06 end walls (their side rails stay — a removed rail would leave a
        /// ~1 m gap, banned 0.9–2.0 m), kairo_door V01 (2.0 m clear on its −Y side) and kairo_end_chumon V01 (the 4.2 m open end). The shell —
        /// bays, roofs, the faded facade — sits under one <see cref="OccluderFadeState"/>, so a roof or the camera-side wall in the camera's
        /// line fades the whole building while the furniture inside (a sibling group) stays solid.
        /// </summary>
        private static Transform Nagaya(Transform group, Building b, Report report)
        {
            var shell = new GameObject(b.Id + ShellSuffix).transform;
            shell.SetParent(group, false);
            shell.gameObject.AddComponent<OccluderFadeState>();
            for (int i = 0; i < b.Bays.Count; i++)
            {
                Bay bay = b.Bays[i];
                float yaw = BlenderYaw(bay.LocalX);
                if (bay.Door != '\0' && bay.Piece == "kairo_door" && Vector2.Dot(-KitY(yaw), Facing(bay.Door)) < 0.5f)
                    report.Warnings.Add(b.Id + " bay " + i + ": kairo_door −Y does not face " + bay.Door);
                GameObject piece = WorldV5Dress.Place(shell, Ap023, bay.Piece, bay.Variation, Mz(bay.X, bay.Z, 0f, yaw), Vector3.zero, "structure");
                piece.name = b.Id + "_" + i.ToString("00") + "_" + bay.Piece + "_V" + bay.Variation.ToString("00");
                if (bay.Piece == "kairo_bay" && (bay.Variation == 2 || bay.Variation == 3)) WorldV5Dress.RailsOff(piece);
                if (bay.Piece == WorldV5Dress.KairoDoorFamily && WorldV5Dress.LintelPass(piece) != 1) report.Warnings.Add(b.Id + " bay " + i + ": lintel collider not found once");
                Roof(piece);
                report.Bays++;
            }

            return shell;
        }

        /// <summary>The Blender ccw yaw that turns the kit's +X to the plan's world direction (Blender y = Unity z).</summary>
        private static float BlenderYaw(string localX)
        {
            switch (localX)
            {
                case "+X": return 0f;
                case "+Z": return 90f;
                case "-X": return 180f;
                case "-Z": return 270f;
                default: throw new FormatException("[LAYOUT] localX " + localX + " (want ±X / ±Z)");
            }
        }

        /// <summary>A kit building (pavilion, …) at the plan rect's centre, its shell faded as one.</summary>
        private static Transform KitBuilding(Transform group, Building b, Report report)
        {
            var shell = new GameObject(b.Id + ShellSuffix).transform;
            shell.SetParent(group, false);
            shell.gameObject.AddComponent<OccluderFadeState>();
            GameObject go = TryPlace(shell, Ap023, b.Recipe, 1, (b.X0 + b.X1) * 0.5f, (b.Z0 + b.Z1) * 0.5f, 0f, b.Yaw, 1f, "structure", report);
            if (go == null) return shell;
            go.name = b.Id;
            Roof(go);
            return shell;
        }

        /// <summary>A kit building the dress already placed (R2–R4, WorldV5Dress.KitBuildings "R2_EastPavilion" …): its whole shell fades from now on, for the room inside.</summary>
        private static Transform Existing(Transform worldRoot, Building b, Report report)
        {
            foreach (Transform t in worldRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(b.Id + "_", StringComparison.Ordinal) || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                if (t.GetComponent<OccluderFadeState>() == null) t.gameObject.AddComponent<OccluderFadeState>();
                return t;
            }

            report.Warnings.Add(b.Id + ": no dressed building of that name");
            return null;
        }

        private static void Roof(GameObject instance)
        {
            Transform roof = instance.transform.Find(ArtLibraryPrefabBuilder.RoofName);
            if (roof != null) WorldV5Dress.RoofOccluder(instance.transform, roof);
        }

        /// <summary>The kit's +Y in world x / z for a Blender ccw yaw (Blender y = Unity z).</summary>
        private static Vector2 KitY(float blenderYaw)
        {
            float a = blenderYaw * Mathf.Deg2Rad;
            return new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
        }

        private static Vector2 Facing(char facing)
        {
            switch (facing)
            {
                case 'N': return Vector2.up;
                case 'S': return Vector2.down;
                case 'E': return Vector2.right;
                default: return Vector2.left;
            }
        }

        /// <summary>
        /// The camera-side (south) facade as fade-only occluders: thin boxes in the wall's own thickness (CameraOccluder, Wall kind, out of the
        /// NavMesh) between the doors, under the shell's fade state. Bullets and bodies meet the wall itself first; the doors stay clear.
        /// </summary>
        private static void SouthWallOccluder(Transform shell, Building b)
        {
            Rect r = WallRect(b);
            float z = r.yMin;
            var gaps = new List<(float a, float c)>();
            foreach (Door d in b.Doors)
                if (d.Facing == 'S') gaps.Add((d.X - Mathf.Max(d.Width, BayLength) * 0.5f, d.X + Mathf.Max(d.Width, BayLength) * 0.5f));
            gaps.Sort((p, q) => p.a.CompareTo(q.a));
            var go = new GameObject(WallOccluderName) { layer = PrototypeLayers.CameraOccluder };
            go.transform.SetParent(shell, false);
            go.AddComponent<CameraOccluderClass>().Kind = CameraOccluderKind.Wall;
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            float cursor = r.xMin + 0.35f, height = WallFadeTop - WallFadeBottom;
            void Segment(float a, float c)
            {
                if (c - a < 0.3f) return;
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3((a + c) * 0.5f, b.Floor + WallFadeBottom + height * 0.5f, z);
                box.size = new Vector3(c - a, height, WallFadeThickness);
            }

            foreach ((float a, float c) in gaps)
            {
                Segment(cursor, a);
                cursor = Mathf.Max(cursor, c);
            }

            Segment(cursor, r.xMax - 0.35f);
            if (go.GetComponent<BoxCollider>() == null) UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>Plants of the dress inside a new building's walls (+ margin): removed; the count goes to the report.</summary>
        private static int ClearPlants(Transform worldRoot, Rect walls, float margin)
        {
            Rect r = Rect.MinMaxRect(walls.xMin - margin, walls.yMin - margin, walls.xMax + margin, walls.yMax + margin);
            var doomed = new List<GameObject>();
            foreach (Transform t in worldRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                if (!r.Contains(new Vector2(t.position.x, t.position.z))) continue;
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (source == null) continue;
                foreach (string family in ClearedFamilies)
                    if (source.name.StartsWith("SM_" + family + "_V", StringComparison.Ordinal)) { doomed.Add(t.gameObject); break; }
            }

            foreach (GameObject go in doomed) UnityEngine.Object.DestroyImmediate(go);
            return doomed.Count;
        }

        // ---------------------------------------------------------------- items and lamps

        /// <summary>The placed piece's collider top over its base: in the banned band it is reported (the tests hold the band empty).</summary>
        private static void AuditTop(GameObject go, Item item, Report report)
        {
            Physics.SyncTransforms();
            float top = float.MinValue;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(false)) top = Mathf.Max(top, c.bounds.max.y);
            if (top == float.MinValue) return;
            float rise = top - go.transform.position.y;
            if (rise > BannedTopMin && rise < BannedTopMax) report.BannedTops.Add(item.Id + " " + item.Family + " V" + item.Variation.ToString("00") + " top " + rise.ToString("0.00") + " m");
        }

        /// <summary>
        /// The lamp's light (light plan 07.10): "baked" and "pool" lamps take a baked point light at the kit's SOCKET_Light (2200–2700 K) for
        /// the single bake — a "pool" lamp's realtime light on the characters comes from S-07's LanternFlames pool, which finds it by its name
        /// and its SOCKET_Flame; "emissive" lamps glow through their material only.
        /// </summary>
        private static void LampLight(GameObject go, Lamp lamp, Report report)
        {
            if (!lamp.Lit) { GlowOff(go); report.Unlit++; return; }
            if (lamp.Light == "pool") report.PoolLamps++;
            if (lamp.Light == "emissive") { report.Emissive++; return; }
            Transform socket = FindDeep(go.transform, SocketLight);
            var holder = new GameObject(LampLightName);
            holder.transform.SetParent(go.transform, false);
            holder.transform.position = socket != null ? socket.position : go.transform.position + Vector3.up * SocketFallbackHeight;
            var light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(lamp.Kelvin, MinKelvin, MaxKelvin));
            light.intensity = lamp.Intensity;
            light.range = lamp.Source == Ap030 ? Mathf.Min(lamp.Range, MaxSmallLampRange) : lamp.Range;
            light.bounceIntensity = 1f;
            light.shadows = LightShadows.Soft;
            light.shadowRadius = 0.05f;
            if (socket == null) report.Warnings.Add(lamp.Id + " " + lamp.Family + ": no " + SocketLight + " — light at +" + SocketFallbackHeight + " m");
            report.BakedLights++;
        }

        /// <summary>A dead lamp (the oil store's spares): its glow slot swapped for a copy that does not emit — dark on screen and in the bake.</summary>
        private static void GlowOff(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    mats[i] = OffVariant(mats[i]);
                    changed = true;
                }

                if (changed) r.sharedMaterials = mats;
            }
        }

        private static Material OffVariant(Material glow)
        {
            string path = GreyboxV6Builder.DataFolder + "/Materials/V5_" + glow.name + "_off.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(glow);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.CopyPropertiesFromMaterial(glow);

            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The rope: a 6-sided tube along y(t) = lerp(from, to) − 4·sag·t·(1 − t), in the kit's rope material, no collider, out of the bake's charts.</summary>
        private static void RopeMesh(Transform parent, Rope rope)
        {
            const int sides = 6;
            int segments = Mathf.Max(2, rope.Segments);
            float radius = rope.Diameter * 0.5f;
            Vector3 At(float t) => Vector3.Lerp(rope.From, rope.To, t) + Vector3.down * (4f * rope.Sag * t * (1f - t));
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float length = 0f;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                if (i > 0) length += Vector3.Distance(At(t), At((i - 1) / (float)segments));
                Vector3 tangent = (At(Mathf.Min(1f, t + 0.01f)) - At(Mathf.Max(0f, t - 0.01f))).normalized;
                Vector3 side = Vector3.Cross(tangent, Vector3.up).normalized, up = Vector3.Cross(side, tangent);
                for (int k = 0; k <= sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    vertices.Add(At(t) + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(k / (float)sides, length / 0.1f));
                }
            }

            for (int i = 0; i < segments; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = i * (sides + 1) + k, b = a + sides + 1;
                triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }

            var mesh = new Mesh { name = rope.Id + "_rope" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(rope.Id + "_Rope") { layer = PrototypeLayers.Environment };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.ArtRoot + "/Materials/M_ap023_rope.mat");
            renderer.shadowCastingMode = rope.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform hit = FindDeep(t.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }

        // ---------------------------------------------------------------- json helpers

        private static float MaxOf(List<object> values)
        {
            float m = 0f;
            foreach (object v in values) m = Mathf.Max(m, ArtJson.AsFloat(v, "footprint"));
            return m;
        }

        private static List<object> List(Dictionary<string, object> o, string key)
        {
            object v = ArtJson.Get(o, key);
            return v == null ? new List<object>() : ArtJson.AsList(v, key);
        }

        private static string Str(Dictionary<string, object> o, string key, string fallback)
        {
            object v = ArtJson.Get(o, key);
            return v == null ? fallback : ArtJson.AsString(v, key);
        }

        private static float Num(Dictionary<string, object> o, string key, float fallback)
        {
            object v = ArtJson.Get(o, key);
            return v == null ? fallback : ArtJson.AsFloat(v, key);
        }

        private static bool Bool(Dictionary<string, object> o, string key, bool fallback)
        {
            object v = ArtJson.Get(o, key);
            return v == null ? fallback : ArtJson.AsBool(v, key);
        }
    }
}
