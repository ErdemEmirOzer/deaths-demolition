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
            public Vector2 ModScale = Vector2.one;   // a road module stretched along / across its axis to fit its run (0.8–1.25)
            public string[] PartsOff = new string[0];   // kit collider parts switched off (a well's bucket, a stall's stools)
            public bool Collider = true;
        }

        public sealed class Lamp
        {
            public string Id, Group, Family, Source, Light, Mount;
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
            public readonly List<string> BannedTops = new List<string>(), Warnings = new List<string>(), RaisedTops = new List<string>();
            public int InnerFadesRemoved, WalkColliders, PartsOff;
            public override string ToString() => "layout v5: " + Buildings + " buildings (" + Bays + " bays), " + Items + " items, " + Lamps + " lamps (" + BakedLights + " baked lights, "
                                                 + PoolLamps + " pool, " + Emissive + " emissive, " + Unlit + " unlit), " + Ropes + " ropes, " + Cleared + " plants cleared, " + Skipped + " skipped"
                                                 + (SkippedFamilies.Count > 0 ? " [" + string.Join(", ", SkippedFamilies) + "]" : "") + ", " + RaisedTops.Count + " collider tops raised over the banned band, "
                                                 + BannedTops.Count + " banned collider tops, " + InnerFadesRemoved + " inner fade states folded into shells, " + WalkColliders + " road walk colliders, "
                                                 + PartsOff + " kit collider parts off, " + Warnings.Count + " warnings";

            /// <summary>Every warning and collider note, one per line (the builder logs it after the report line).</summary>
            public string Details()
            {
                var lines = new List<string>();
                foreach (string w in Warnings) lines.Add("[LAYOUT] warning: " + w);
                foreach (string t in RaisedTops) lines.Add("[LAYOUT] raised: " + t);
                foreach (string t in BannedTops) lines.Add("[LAYOUT] BANNED top: " + t);
                return string.Join("\n", lines);
            }
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
                    Footprint = MaxOf(List(p, "footprint")), ModScale = Vec2(ArtJson.Get(p, "modScale"), Vector2.one), PartsOff = Strings(List(p, "colliderPartsOff")),
                });
            }

            foreach (object o in List(root, "lamps"))
            {
                Dictionary<string, object> l = ArtJson.AsObject(o, "lamp");
                plan.Lamps.Add(new Lamp
                {
                    Id = Str(l, "id", ""), Group = Str(l, "group", ""), Family = ArtJson.AsString(ArtJson.Require(l, "family"), "family"), Source = Str(l, "source", Ap030),
                    Light = Str(l, "light", "baked"), Lit = Bool(l, "lit", true), Mount = Str(l, "mount", ""), Variation = (int)Num(l, "variation", 1f), X = Num(l, "x", 0f), Z = Num(l, "z", 0f), Y = Num(l, "y", 0f),
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
            s_report = report;
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
                GameObject go = TryPlace(parent, item.Source, item.Family, item.Variation, item.X, item.Z, item.Y, item.Yaw, item.Scale, RoleOf(item), report, item.ModScale);
                if (go == null) continue;
                go.name = string.IsNullOrEmpty(item.Id) ? go.name : item.Id + "_" + item.Family;
                if (Array.IndexOf(MovingGroups, item.Group) >= 0) Unbatch(go);   // S-07 08.10: the floating tōrō bob and drift
                if (item.Role == "road") report.WalkColliders += KeepWalkable(go, item);   // the stones and decks walk, nothing on them blocks
                else if (!item.Collider) WorldV5Dress.Visual(go);
                else
                {
                    if (item.PartsOff.Length > 0) report.PartsOff += PartsOff(go, item);
                    AuditTop(go, item, report);
                }
                report.Items++;
            }

            foreach (Lamp lamp in plan.Lamps)
            {
                Transform parent = Group(clusters, interiors, lamp.Group);
                GameObject go = TryPlace(parent, lamp.Source, lamp.Family, lamp.Variation, lamp.X, lamp.Z, lamp.Y, lamp.Yaw, 1f, "dressing", report);
                if (go == null) continue;
                go.name = LampPrefix + lamp.Id + "_" + (lamp.Lit ? lamp.Light : "unlit");
                WorldV5Dress.Visual(go);   // lamps are not obstacles (hanging, on furniture, by a door)
                if (Swings(lamp)) Unbatch(go);   // S-07 08.10: a hanging chōchin / tsuri sways (LanternFlames' pendulum)
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

        /// <summary>Road modules and pieces of ≥ 2 m footprint are lightmapped like a structure; plants and the rest (props, debris) are probe-lit dressing.</summary>
        private static string RoleOf(Item item)
        {
            if (item.Role == "road") return "structure";   // the AP-030 road kit: a lightmapped walking surface
            if (Array.IndexOf(PlantFamilies, item.Family) >= 0) return "dressing";
            return item.Footprint >= 2f ? "structure" : "dressing";
        }

        private static readonly string[] PlantFamilies = { "satsuki", "groundcover", "pond_reeds", "lotus", "bamboo_fallen" };

        /// <summary>A plan piece, or null (counted) when its AP-030 kit is not in the project yet. An AP-023 piece that does not exist throws.</summary>
        private static GameObject TryPlace(Transform parent, string source, string family, int variation, float x, float z, float y, float yaw, float scale, string role, Report report, Vector2? modScale = null)
        {
            string ap = string.IsNullOrEmpty(source) ? Ap023 : source;
            string prefab = ArtLibraryPaths.Prefab(ap, family, "SM_" + family + "_V" + variation.ToString("00"));
            if (ap != Ap023 && AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null)
            {
                report.Skipped++;
                report.SkippedFamilies.Add(ap + "/" + family + "_V" + variation.ToString("00"));
                return null;
            }

            Vector2 m = modScale ?? Vector2.one;   // Blender space: the module's +X along its run, +Y across, +Z up
            return WorldV5Dress.Place(parent, ap, family, variation, Mz(x, z, y, -yaw) * Matrix4x4.Scale(new Vector3(scale * m.x, scale * m.y, scale)), Vector3.zero, role);
        }

        /// <summary>
        /// S-07 (08.10) · Pieces its LanternFlames / tōrō code moves at runtime must not be static-batched (Unity merges them and they can no
        /// longer move): the hanging lamps and the floating tōrō group. Their other static flags stay (the baked light is the LampLight's).
        /// </summary>
        public static readonly string[] MovingGroups = { "M8_toro" };

        public static readonly string[] SwingingFamilies = { "lantern_chochin", "lantern_tsuri" };

        public static bool Swings(Lamp lamp) => lamp.Mount == "hang" || Array.IndexOf(SwingingFamilies, lamp.Family) >= 0;

        private static void Unbatch(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(t.gameObject) & ~StaticEditorFlags.BatchingStatic);
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
                {
                    // the plan's localX left the opening on the far side: the door piece is symmetric along its axis (joints ±1.8), so a half turn
                    // puts its −Y opening on the plan's door side in the same span
                    yaw += 180f;
                    report.Warnings.Add(b.Id + " bay " + i + ": kairo_door turned 180° so its opening faces the plan's door " + bay.Door + " (plan localX " + bay.LocalX + ")");
                }
                GameObject piece = WorldV5Dress.Place(shell, Ap023, bay.Piece, bay.Variation, Mz(bay.X, bay.Z, 0f, yaw), Vector3.zero, "structure");
                piece.name = b.Id + "_" + i.ToString("00") + "_" + bay.Piece + "_V" + bay.Variation.ToString("00");
                if (bay.Piece == "kairo_bay" && (bay.Variation == 2 || bay.Variation == 3)) WorldV5Dress.RailsOff(piece);
                if (bay.Piece == WorldV5Dress.KairoDoorFamily && WorldV5Dress.LintelPass(piece) != 1) report.Warnings.Add(b.Id + " bay " + i + ": lintel collider not found once");
                Roof(piece);
                report.Bays++;
            }

            report.InnerFadesRemoved += FoldFades(shell);
            return shell;
        }

        /// <summary>
        /// The kit's Roof children carry their own <see cref="OccluderFadeState"/>, which the fader would find first (it walks up from the hit
        /// collider) — only the roof would fade. Under a shell they go, so every occluder in the building resolves to the shell's one state.
        /// </summary>
        private static int FoldFades(Transform shell)
        {
            int n = 0;
            foreach (OccluderFadeState state in shell.GetComponentsInChildren<OccluderFadeState>(true))
            {
                if (state.transform == shell) continue;
                UnityEngine.Object.DestroyImmediate(state);
                n++;
            }

            return n;
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
            report.InnerFadesRemoved += FoldFades(shell);
            return shell;
        }

        /// <summary>A kit building the dress already placed (R2–R4, WorldV5Dress.KitBuildings "R2_EastPavilion" …): its whole shell fades from now on, for the room inside.</summary>
        private static Transform Existing(Transform worldRoot, Building b, Report report)
        {
            foreach (Transform t in worldRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(b.Id + "_", StringComparison.Ordinal) || !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                if (t.GetComponent<OccluderFadeState>() == null) t.gameObject.AddComponent<OccluderFadeState>();
                report.InnerFadesRemoved += FoldFades(t);
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

        /// <summary>
        /// The placed piece's collider tops over its base. A box whose top lands in the banned 0.31–0.49 m band (a body reads it as a step, a
        /// shot grazes it — the kit's tawara bale is 0.42, the yukimi lantern's foot 0.47) grows up to <see cref="RaisedTop"/>: still solid,
        /// no longer a step. Any other collider in the band is reported (the tests hold the band empty).
        /// </summary>
        private static void AuditTop(GameObject go, Item item, Report report)
        {
            Physics.SyncTransforms();
            float y = go.transform.position.y;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(false))
            {
                float rise = c.bounds.max.y - y;
                if (rise <= BannedTopMin || rise >= BannedTopMax || c.bounds.size.y < 0.05f) continue;
                int axis = UpAxis(c.transform, out float sign, out float metresPerUnit);
                if (c is BoxCollider && axis >= 0)
                {
                    var box = (BoxCollider)c;
                    float grow = (RaisedTop - rise) / metresPerUnit;   // in the box's own units, along whichever local axis points up (the kit's is often Z)
                    Vector3 size = box.size, centre = box.center;
                    size[axis] += grow;
                    centre[axis] += sign * grow * 0.5f;
                    box.size = size;
                    box.center = centre;
                    report.RaisedTops.Add(item.Id + " " + item.Family + "/" + c.name + " " + rise.ToString("0.00") + " -> " + RaisedTop.ToString("0.00") + " m");
                }
                else report.BannedTops.Add(item.Id + " " + item.Family + " V" + item.Variation.ToString("00") + "/" + c.name + " top " + rise.ToString("0.00") + " m");
            }
        }

        /// <summary>The local axis (0 x, 1 y, 2 z) of <paramref name="t"/> that points along world up within 8°, its sign, and its metres per unit; −1 if none.</summary>
        private static int UpAxis(Transform t, out float sign, out float metresPerUnit)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 local = Vector3.zero;
                local[i] = 1f;
                Vector3 world = t.TransformVector(local);
                float len = world.magnitude, dot = len > 1e-6f ? Vector3.Dot(world / len, Vector3.up) : 0f;
                if (Mathf.Abs(dot) < 0.99f) continue;
                sign = Mathf.Sign(dot);
                metresPerUnit = len;
                return i;
            }

            sign = 1f;
            metresPerUnit = 1f;
            return -1;
        }

        /// <summary>A banned-band box grows to this top over its base (≥ 0.5: the gameplay review's shoot-over class).</summary>
        public const float RaisedTop = 0.55f;

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
            holder.transform.position = socket != null ? socket.position : BodyCentre(go);
            var light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(lamp.Kelvin, MinKelvin, MaxKelvin));
            light.intensity = lamp.Intensity;
            light.range = lamp.Source == Ap030 ? Mathf.Min(lamp.Range, MaxSmallLampRange) : lamp.Range;
            light.bounceIntensity = 1f;
            light.shadows = LightShadows.Soft;
            light.shadowRadius = 0.05f;
            if (socket == null && lamp.Source != Ap023) report.Warnings.Add(lamp.Id + " " + lamp.Family + ": no " + SocketLight + " — light at the body's centre");
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

        /// <summary>A socketless lamp's light point (the AP-023 lanterns carry no sockets): the centre of its LOD0 renderers' bounds — the paper body
        /// of a chōchin / tsuri, the light chamber's height of a stone lantern near enough; the base + 0.6 m if it has none.</summary>
        private static Vector3 BodyCentre(GameObject go)
        {
            var b = new Bounds();
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.IndexOf("LOD1", StringComparison.Ordinal) >= 0 || r.name.IndexOf("LOD2", StringComparison.Ordinal) >= 0) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }

            return any ? b.center : go.transform.position + Vector3.up * SocketFallbackHeight;
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

        // ---------------------------------------------------------------- kit colliders by the manifest's class / part

        private static readonly Dictionary<string, ArtLibraryManifest> s_manifests = new Dictionary<string, ArtLibraryManifest>(StringComparer.Ordinal);

        /// <summary>
        /// The manifest classes and parts of a prefab's Geometry/COL_i colliders, in ArtLibraryPrefabBuilder's own order (a "none" proxy builds
        /// nothing; broken pieces and roof parts sit elsewhere; one COL per shell).
        /// </summary>
        private static List<(string cls, string part)> GeometryColliders(string ap, string family, int variation)
        {
            if (!s_manifests.TryGetValue(ap, out ArtLibraryManifest manifest))
            {
                manifest = ArtLibraryManifest.FromJson(File.ReadAllText(ArtLibraryPaths.ManifestJson(ap)));
                s_manifests[ap] = manifest;
            }

            string prefab = "SM_" + family + "_V" + variation.ToString("00");
            var list = new List<(string, string)>();
            foreach (ArtLibraryManifest.Family f in manifest.families)
            foreach (ArtLibraryManifest.Variation v in f.variations)
            {
                if (v.prefab != prefab) continue;
                foreach (ArtLibraryManifest.Collider c in v.colliders)
                {
                    if (c.IsNone || c.broken || c.part == "roof") continue;
                    int shells = c.shellBoxes != null && c.shellBoxes.Count > 0 ? c.shellBoxes.Count : c.shells;
                    for (int s = 0; s < shells; s++) list.Add((c.@class, c.part));
                }

                return list;
            }

            return list;
        }

        private static Collider GeometryCollider(GameObject go, int index)
        {
            Transform t = go.transform.Find(ArtLibraryPrefabBuilder.GeometryName + "/COL_" + index);
            return t != null ? t.GetComponent<Collider>() : null;
        }

        /// <summary>A road module (stone, deck, drain, stepping stone): its walkable colliders stay — a foot on a 0.30 m boardwalk stands on it —
        /// every other class goes (nothing on a path blocks). Returns the walk colliders kept.</summary>
        private static int KeepWalkable(GameObject go, Item item)
        {
            List<(string cls, string part)> cols = GeometryColliders(item.Source, item.Family, item.Variation);
            int kept = 0;
            for (int i = 0; i < cols.Count; i++)
            {
                Collider c = GeometryCollider(go, i);
                if (c == null) continue;
                if (cols[i].cls == "walkable") kept++;
                else c.gameObject.SetActive(false);
            }

            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))   // anything not in the manifest's geometry list (roof, broken)
                if (c.transform.parent == null || c.transform.parent.name != ArtLibraryPrefabBuilder.GeometryName) c.gameObject.SetActive(false);
            return kept;
        }

        /// <summary>The plan's colliderPartsOff: the kit colliders of those manifest parts go (a well's bucket under 0.30 m, a stall's stools).</summary>
        private static int PartsOff(GameObject go, Item item)
        {
            List<(string cls, string part)> cols = GeometryColliders(item.Source, item.Family, item.Variation);
            int n = 0;
            for (int i = 0; i < cols.Count; i++)
            {
                if (cols[i].part == null || Array.FindIndex(item.PartsOff, t => cols[i].part.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) < 0) continue;
                Collider c = GeometryCollider(go, i);
                if (c == null) continue;
                c.gameObject.SetActive(false);
                n++;
            }

            if (n == 0) Warnings(item, "colliderPartsOff " + string.Join(",", item.PartsOff) + " matched no kit collider part");
            return n;
        }

        private static Report s_report;

        private static void Warnings(Item item, string what)
        {
            s_report?.Warnings.Add(item.Id + " " + item.Family + ": " + what);
        }

        // ---------------------------------------------------------------- json helpers

        private static Vector2 Vec2(object v, Vector2 fallback)
        {
            if (!(v is List<object> l) || l.Count < 2) return fallback;
            return new Vector2(ArtJson.AsFloat(l[0], "modScale"), ArtJson.AsFloat(l[1], "modScale"));
        }

        private static string[] Strings(List<object> values)
        {
            var list = new List<string>();
            foreach (object v in values) list.Add(ArtJson.AsString(v, "colliderPartsOff"));
            return list.ToArray();
        }

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
