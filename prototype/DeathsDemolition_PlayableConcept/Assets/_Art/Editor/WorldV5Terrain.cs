using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 B5 · The V6 world's ground as a real surface (user 07.10: "yeşil zeminler 2d ve amatör görünüyor … zemin 3d"). The
    /// 170 m greybox box keeps its collider (the walkable surface and the NavMesh source are unchanged) and loses its renderer; the
    /// visible ground is a chunked mesh (<see cref="ChunksPerSide"/>² chunks of <see cref="ChunkSize"/> m; <see cref="FineStep"/> m
    /// grid inside ±<see cref="FineHalf"/>, <see cref="CoarseStep"/> m in the vista ring) with micro-relief and a layered material
    /// (DD/GroundTerrain: moss / soil / dirt / gravel / pebble / mud, Look B wetness and puddles) driven by two world masks.
    /// <para>Relief (≤ <see cref="MaxRelief"/> m): where bodies walk the ground only dips (hollows ≤ <see cref="WalkDip"/> m, path
    /// beds −<see cref="PathBed"/> m) — the runtime's flat effects (decals, splats, ground fire at +0.01–0.03 m over y 0) can never sink
    /// under it; mounds only where nobody stands (the bamboo band, beyond the ring, root flares, wall banks). Exactly flat (0) on the
    /// plaza and its kerbs, the hall plinth, rooms + 1.5 m, kairō floors, the crossings + 1.5 m, the karesansui, the ponds + 1 m,
    /// spawn gates (2.5 m), weapon points (1.5 m) and door / gate openings (1.5 m).</para>
    /// <para>Masks (world XZ, ±<see cref="Half"/> m, linear, PNG under <see cref="Folder"/>): A <see cref="MaskASize"/>² (0.083 m/px) R
    /// gravel, G dirt, B mud / rut (depression, puddles), A paved (≥ 0.75 covered by a mesh; 0–0.5 the 0.25 m contact halo outside it);
    /// B <see cref="MaskBSize"/>² R moss, G litter, B wet, A pebble. Paths come from roads.json (<see cref="RoadsJsonPath"/>, the
    /// ROADS-CONTRACT schema wp092.roads.v1) when it is present, else from the layout's own lanes (<see cref="InterimRoads"/>).</para>
    /// Deterministic (hash noise, no rng); rebuilt in place.
    /// </summary>
    public static class WorldV5Terrain
    {
        public const string ShaderName = "DD/GroundTerrain";
        public const string RootName = "Ground_Terrain", ChunkPrefix = "GroundTerrain_";
        public const float Half = GreyboxV6Builder.VistaOuter, ChunkSize = 17f, FineStep = 0.5f, CoarseStep = 1f, FineHalf = 68f;
        public const int ChunksPerSide = 10;
        /// <summary>Relief: the cap, the walkable hollows, the path bed, the wall bank and the root flare (metres).</summary>
        public const float MaxRelief = 0.15f, WalkDip = 0.06f, PathBed = 0.03f, MinRelief = -0.08f, WallBank = 0.04f, RootFlare = 0.10f;
        /// <summary>No relief past this (the ring wall stands at 55–55.6; the coarse ring from <see cref="FineHalf"/> is flat, so no T-junction crack).</summary>
        public const float ReliefFadeStart = 60f, ReliefFadeEnd = 64f;
        public const int MaskASize = 2048, MaskBSize = 1024;
        /// <summary>Paved channel coding: a value ≥ this is under a paving mesh (no grass, no puddle); below, the contact halo.</summary>
        public const float PavedCore = 0.75f, PavedHalo = 0.25f;

        public static string Folder => GreyboxV6Builder.DataFolder + "/Ground";
        public static string MaskAPath => Folder + "/T_V6_GroundMaskA.png";
        public static string MaskBPath => Folder + "/T_V6_GroundMaskB.png";
        public static string MeshPath => Folder + "/V6_GroundTerrain.asset";
        public static string MaterialPath => GreyboxV6Builder.DataFolder + "/Materials/V6_GroundTerrain.mat";
        /// <summary>The layout's road network (ROADS-CONTRACT wp092.roads.v1; the layout builder writes it here — S-07's runtime reads the same
        /// TextAsset, MASK-CONTRACT §4.3). Absent: the interim paths. Env DD_ROADS_JSON overrides (tests, trials).</summary>
        public static string RoadsJsonPath => "Assets/_Art/Resources/WorldRoads/" + GreyboxV6Builder.SceneName + ".json";

        // ---------------------------------------------------------------- inputs

        public enum RoadType { Gravel, Dirt, Ishidatami, Boardwalk, Stepstone }
        public enum ZoneKind { Soil, Moss, Mud, Gravel, Wet }

        public sealed class Road
        {
            public string Id;
            public RoadType Type;
            public float Width;
            public Vector2[] Points;
            /// <summary>Cart ruts (dirt roads ≥ 2.2 m): two water-holding tracks ±<see cref="RutOffset"/> m off the centre line.</summary>
            public bool Ruts;
            /// <summary>Bamboo-leaf litter on the surface (the grove lanes).</summary>
            public bool Litter;

            public bool Paved => Type == RoadType.Ishidatami || Type == RoadType.Boardwalk || Type == RoadType.Stepstone;
        }

        public sealed class Zone
        {
            public string Id;
            public ZoneKind Kind;
            public Vector2[] Polygon;
        }

        public struct Pond
        {
            public Vector2 Centre, Semi;
            public float YawDeg;

            /// <summary>Signed metric distance to the rim (negative inside), along the ray from the centre (the PondStrips frame).</summary>
            public float Distance(Vector2 p)
            {
                float a = YawDeg * Mathf.Deg2Rad;
                Vector2 d = p - Centre;
                float lx = d.x * Mathf.Cos(a) + d.y * Mathf.Sin(a), lz = -d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a);
                float r = Mathf.Sqrt(lx * lx + lz * lz);
                if (r < 1e-4f) return -Mathf.Min(Semi.x, Semi.y);
                float c = lx / r, s = lz / r;
                float rim = 1f / Mathf.Sqrt((c / Semi.x) * (c / Semi.x) + (s / Semi.y) * (s / Semi.y));
                return r - rim;
            }
        }

        public struct Tree
        {
            public Vector2 Position;
            public float Trunk, Canopy;
            public bool Pine;
        }

        public struct Segment
        {
            public Vector2 A, B;
            public float HalfThickness, Height;
            /// <summary>A LOOK-BIBLE Z6 wall (tsuiji, court wall, fence, low wall): its footprint seeds the wall-foot band (veg mask_b.R). Building and kairō walls are not.</summary>
            public bool Foot;
            public Segment(Vector2 a, Vector2 b, float halfThickness, float height, bool foot = false) { A = a; B = b; HalfThickness = halfThickness; Height = height; Foot = foot; }
        }

        /// <summary>Everything the ground reads — the layout tables, the ponds and the road network. Scene-independent but for the ponds.</summary>
        public sealed class Inputs
        {
            public List<Road> Roads = new List<Road>();
            public List<Zone> Zones = new List<Zone>();
            public List<Pond> Ponds = new List<Pond>();
            public List<Tree> Trees = new List<Tree>();
            public List<Segment> Walls = new List<Segment>();
            /// <summary>Paved / covered rects (x0, z0, x1, z1): plaza (+ kerbs), plinth, room floors, crossings, karesansui.</summary>
            public List<Vector4> Paved = new List<Vector4>();
            /// <summary>Building footprints that get a drip strip (amaochi) round them.</summary>
            public List<Vector4> Buildings = new List<Vector4>();
            /// <summary>Flat rects with their margin (x0, z0, x1, z1) + margin.</summary>
            public List<(Vector4 rect, float margin)> FlatRects = new List<(Vector4, float)>();
            public List<(Vector2 at, float radius)> FlatDiscs = new List<(Vector2, float)>();
            /// <summary>Corridor floors: centre line + half width (flat, paved).</summary>
            public List<(Vector2[] path, float half)> Corridors = new List<(Vector2[], float)>();
            /// <summary>The kit-paved roads (ishidatami, boardwalk): flat under and 0.6 m beside them (the pieces lie on y 0).</summary>
            public List<(Vector2[] path, float half)> FlatLines = new List<(Vector2[], float)>();
            /// <summary>Trampled spots (spawn gates, door thresholds): worn soil.</summary>
            public List<(Vector2 at, float radius)> Worn = new List<(Vector2, float)>();
            /// <summary>Beaten-earth yards (x0, z0, x1, z1).</summary>
            public List<Vector4> Yards = new List<Vector4>();
            /// <summary>Moss gardens (koke-niwa; x0, z0, x1, z1).</summary>
            public List<Vector4> MossGardens = new List<Vector4>();
            public string RoadSource = "layout";
            /// <summary>The layout plan's vegetation inputs (layout-data.json; null: the interim zoning).</summary>
            public VegLayout Layout;
        }

        // ---------------------------------------------------------------- the layout plan (layout-data.json, wp092.layout.v1)

        /// <summary>What the ground and the S-07 masks read from the layout plan: the zones' vegZone ids, the overrides, the clear points, the buildings.</summary>
        public sealed class VegLayout
        {
            public string Version = "";
            public readonly List<(string id, byte zone, Vector2[] polygon, float ringMin)> Zones = new List<(string, byte, Vector2[], float)>();
            public readonly List<(string id, byte zone, Vector2[] polygon, float ringMin)> Overrides = new List<(string, byte, Vector2[], float)>();
            public readonly List<(Vector2 at, float r, string kind, string id)> Clear = new List<(Vector2, float, string, string)>();
            public readonly List<(string id, Vector4 rect, bool isNew, List<Vector2> doors)> Buildings = new List<(string, Vector4, bool, List<Vector2>)>();
        }

        /// <summary>The editor-side copy of the layout plan's ground / vegetation part (written by <see cref="ExportMasksCli"/>; a V6 rebuild reads it).</summary>
        public static string LayoutCopyPath => Folder + "/V6_GroundLayout.json";

        public static string ReadLayoutJson()
        {
            string env = Environment.GetEnvironmentVariable("DD_LAYOUT_JSON");
            if (!string.IsNullOrEmpty(env)) return File.ReadAllText(env);
            return File.Exists(LayoutCopyPath) ? File.ReadAllText(LayoutCopyPath) : null;
        }

        /// <summary>wp092.layout.v1: zones[] (id, vegZone, polygon), vegZoneOverrides[] (id, vegZone, polygon | rule "|x| veya |z| ≥ n"), clear[] (x, z, r, kind, id), buildings[] (id, rect, new, doors[x, z]). Throws on a malformed file.</summary>
        public static VegLayout ParseLayout(string json)
        {
            Dictionary<string, object> root = ArtJson.AsObject(ArtJson.Parse(json), "layout");
            string schema = ArtJson.AsString(ArtJson.Require(root, "schema"), "schema");
            if (schema != "wp092.layout.v1") throw new FormatException("[GROUND] layout schema " + schema + " (want wp092.layout.v1)");
            var layout = new VegLayout { Version = ArtJson.Get(root, "version") is object v ? ArtJson.AsString(v, "version") : "" };
            foreach ((string key, List<(string, byte, Vector2[], float)> into) in new[] { ("zones", layout.Zones), ("vegZoneOverrides", layout.Overrides) })
            {
                object list = ArtJson.Get(root, key);
                if (list == null) continue;
                foreach (object o in ArtJson.AsList(list, key))
                {
                    Dictionary<string, object> z = ArtJson.AsObject(o, key);
                    string id = ArtJson.AsString(ArtJson.Require(z, "id"), key + " id");
                    int zone = ArtJson.AsInt(ArtJson.Require(z, "vegZone"), id + ".vegZone");
                    if (zone < 0 || zone > 255) throw new FormatException("[GROUND] " + id + " vegZone " + zone);
                    object poly = ArtJson.Get(z, "polygon");
                    float ringMin = float.MaxValue;
                    Vector2[] polygon = null;
                    if (poly != null) polygon = Points(poly, id + ".polygon");
                    else
                    {
                        string rule = ArtJson.Get(z, "rule") is object r ? ArtJson.AsString(r, id + ".rule") : "";
                        System.Text.RegularExpressions.MatchCollection m = System.Text.RegularExpressions.Regex.Matches(rule, @"[0-9]+(?:[.,][0-9]+)?");
                        if (m.Count == 0) throw new FormatException("[GROUND] " + id + ": neither a polygon nor a ring rule");
                        ringMin = float.Parse(m[m.Count - 1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                    }

                    into.Add((id, (byte)zone, polygon, ringMin));
                }
            }

            if (ArtJson.Get(root, "clear") is object clear)
                foreach (object o in ArtJson.AsList(clear, "clear"))
                {
                    Dictionary<string, object> c = ArtJson.AsObject(o, "clear");
                    layout.Clear.Add((new Vector2(ArtJson.AsFloat(ArtJson.Require(c, "x"), "clear.x"), ArtJson.AsFloat(ArtJson.Require(c, "z"), "clear.z")),
                        ArtJson.AsFloat(ArtJson.Require(c, "r"), "clear.r"), ArtJson.AsString(ArtJson.Require(c, "kind"), "clear.kind"),
                        ArtJson.Get(c, "id") is object cid ? ArtJson.AsString(cid, "clear.id") : null));
                }

            if (ArtJson.Get(root, "buildings") is object buildings)
                foreach (object o in ArtJson.AsList(buildings, "buildings"))
                {
                    Dictionary<string, object> b = ArtJson.AsObject(o, "building");
                    string id = ArtJson.AsString(ArtJson.Require(b, "id"), "building id");
                    List<object> rect = ArtJson.AsList(ArtJson.Require(b, "rect"), id + ".rect");
                    if (rect.Count != 4) throw new FormatException("[GROUND] " + id + ".rect is not [x0, z0, x1, z1]");
                    var r = new Vector4(ArtJson.AsFloat(rect[0]), ArtJson.AsFloat(rect[1]), ArtJson.AsFloat(rect[2]), ArtJson.AsFloat(rect[3]));
                    var doors = new List<Vector2>();
                    if (ArtJson.Get(b, "doors") is object dl)
                        foreach (object d in ArtJson.AsList(dl, id + ".doors"))
                        {
                            Dictionary<string, object> door = ArtJson.AsObject(d, id + ".door");
                            doors.Add(new Vector2(ArtJson.AsFloat(ArtJson.Require(door, "x")), ArtJson.AsFloat(ArtJson.Require(door, "z"))));
                        }

                    bool isNew = ArtJson.Get(b, "new") is object nv && ArtJson.AsBool(nv, id + ".new");
                    layout.Buildings.Add((id, r, isNew, doors));
                }

            return layout;
        }

        /// <summary>The layout plan's ground / vegetation part as its own small JSON (the editor-side copy, <see cref="LayoutCopyPath"/>).</summary>
        public static string LayoutCopyJson(VegLayout layout, string source)
        {
            var sb = new StringBuilder();
            sb.Append("{\"schema\":\"wp092.layout.v1\",\"version\":\"").Append(layout.Version).Append("\",\n \"source\":\"").Append(source.Replace("\\", "/").Replace("\"", "'"))
              .Append(" (WP-092 B5: zones, vegZoneOverrides, clear, buildings — the ground and S-07 mask inputs)\",");
            foreach ((string key, List<(string id, byte zone, Vector2[] polygon, float ringMin)> list) in new[] { ("zones", layout.Zones), ("vegZoneOverrides", layout.Overrides) })
            {
                sb.Append("\n \"").Append(key).Append("\":[");
                for (int i = 0; i < list.Count; i++)
                {
                    (string id, byte zone, Vector2[] polygon, float ringMin) = list[i];
                    sb.Append(i == 0 ? "\n  " : ",\n  ").Append("{\"id\":\"").Append(id).Append("\",\"vegZone\":").Append(zone);
                    if (polygon != null) sb.Append(",\"polygon\":").Append(PointsJson(polygon));
                    else sb.Append(",\"polygon\":null,\"rule\":\"|x| veya |z| ≥ ").Append(Fm(ringMin)).Append("\"");
                    sb.Append("}");
                }

                sb.Append("],");
            }

            sb.Append("\n \"clear\":[");
            for (int i = 0; i < layout.Clear.Count; i++)
            {
                (Vector2 at, float r, string kind, string id) = layout.Clear[i];
                sb.Append(i == 0 ? "\n  " : ",\n  ").Append("{\"x\":").Append(Fm(at.x)).Append(",\"z\":").Append(Fm(at.y)).Append(",\"r\":").Append(Fm(r)).Append(",\"kind\":\"").Append(kind).Append("\"");
                if (id != null) sb.Append(",\"id\":\"").Append(id).Append("\"");
                sb.Append("}");
            }

            sb.Append("],\n \"buildings\":[");
            for (int i = 0; i < layout.Buildings.Count; i++)
            {
                (string id, Vector4 rect, bool isNew, List<Vector2> doors) = layout.Buildings[i];
                sb.Append(i == 0 ? "\n  " : ",\n  ").Append("{\"id\":\"").Append(id).Append("\",\"rect\":[").Append(Fm(rect.x)).Append(",").Append(Fm(rect.y)).Append(",").Append(Fm(rect.z)).Append(",").Append(Fm(rect.w))
                  .Append("],\"new\":").Append(isNew ? "true" : "false").Append(",\"doors\":[");
                for (int k = 0; k < doors.Count; k++) sb.Append(k == 0 ? "" : ",").Append("{\"x\":").Append(Fm(doors[k].x)).Append(",\"z\":").Append(Fm(doors[k].y)).Append("}");
                sb.Append("]}");
            }

            sb.Append("]}\n");
            return sb.ToString();
        }

        private static string PointsJson(Vector2[] points)
        {
            var sb = new StringBuilder("[");
            for (int k = 0; k < points.Length; k++) sb.Append(k == 0 ? "" : ",").Append("[").Append(Fm(points[k].x)).Append(",").Append(Fm(points[k].y)).Append("]");
            return sb.Append("]").ToString();
        }

        public const float RutOffset = 0.55f, RutHalf = 0.13f;

        /// <summary>Interim paths until roads.json lands: the grove lanes (dirt, bamboo litter), the garden paths (gravel), the gate approaches (dirt, ruts).</summary>
        public static List<Road> InterimRoads()
        {
            var roads = new List<Road>();
            for (int i = 0; i < GreyboxV6Builder.BambooLanes.Length; i++)
                roads.Add(new Road { Id = "lane_" + i, Type = RoadType.Dirt, Width = i == 0 ? 2.1f : 1.9f, Points = GreyboxV6Builder.BambooLanes[i], Litter = true });
            for (int i = 0; i < GreyboxV6Builder.GardenPaths.Length; i++)
                roads.Add(new Road { Id = "garden_" + i, Type = RoadType.Gravel, Width = 1.9f, Points = GreyboxV6Builder.GardenPaths[i] });
            for (int i = 0; i < WorldV5Moss.ApproachPaths.Length; i++)
            {
                Vector2[] p = WorldV5Moss.ApproachPaths[i];
                float len = 0f;
                for (int k = 1; k < p.Length; k++) len += Vector2.Distance(p[k - 1], p[k]);
                roads.Add(new Road { Id = "approach_" + i, Type = RoadType.Dirt, Width = 2.3f, Points = p, Ruts = len > 8f });
            }

            return roads;
        }

        /// <summary>The ground's inputs from the layout tables; <paramref name="ponds"/> from the greybox (null: none).</summary>
        public static Inputs LayoutInputs(IEnumerable<Pond> ponds, string roadsJson, string layoutJson = null)
        {
            var inputs = new Inputs();
            if (ponds != null) inputs.Ponds.AddRange(ponds);
            if (!string.IsNullOrEmpty(roadsJson))
            {
                ParseRoads(roadsJson, inputs.Roads, inputs.Zones);
                inputs.RoadSource = "roads.json";
            }
            else inputs.Roads.AddRange(InterimRoads());
            foreach (Road r in inputs.Roads)
                if (r.Type == RoadType.Ishidatami || r.Type == RoadType.Boardwalk) inputs.FlatLines.Add((r.Points, r.Width * 0.5f));

            // Paved / covered: the plaza (+ the kerb line 0.3 m outside its open edges), the hall plinth with its stairs, the karesansui bed.
            foreach (Vector4 r in WorldV5Ground.PlazaRects) inputs.Paved.Add(r);
            foreach ((Vector2 a, Vector2 b) in WorldV5Ground.KerbEdges)
                inputs.Paved.Add(new Vector4(Mathf.Min(a.x, b.x) - 0.3f, Mathf.Min(a.y, b.y) - 0.3f, Mathf.Max(a.x, b.x) + 0.3f, Mathf.Max(a.y, b.y) + 0.3f));
            var plinth = new Vector4(-19.5f, 36.5f, 19.5f, 50f);
            inputs.Paved.Add(plinth);
            inputs.Paved.Add(new Vector4(-11.5f, 34f, 11.5f, 36.5f));   // the three south stairs
            inputs.Paved.Add(new Vector4(19.5f, 42.8f, 22f, 45.2f));
            inputs.Paved.Add(new Vector4(-22f, 42.8f, -19.5f, 45.2f));
            var karesansui = new Vector4(12f, -30f, 28f, -16f);
            inputs.Paved.Add(karesansui);
            foreach (GreyboxV6Builder.Crossing c in GreyboxV6Builder.Crossings) inputs.Paved.Add(new Vector4(c.Min.x, c.Min.y, c.Max.x, c.Max.y));

            foreach (Vector4 r in WorldV5Ground.PlazaRects) inputs.FlatRects.Add((r, 0.9f));   // the kerb line (0.3 m) + 0.6 m, corners included
            inputs.FlatRects.Add((plinth, 1.0f));
            inputs.FlatRects.Add((new Vector4(-22f, 34f, 22f, 50f), 0.5f));
            inputs.FlatRects.Add((karesansui, 0.3f));
            foreach (GreyboxV6Builder.Crossing c in GreyboxV6Builder.Crossings) inputs.FlatRects.Add((new Vector4(c.Min.x, c.Min.y, c.Max.x, c.Max.y), 1.5f));

            foreach (GreyboxV6Builder.Room r in GreyboxV6Builder.Rooms)
            {
                if (r.Id == "R1_MainHall") continue;   // on the plinth
                var rect = new Vector4(r.X0, r.Z0, r.X1, r.Z1);
                inputs.FlatRects.Add((rect, 1.5f));
                if (r.Walls) inputs.Paved.Add(rect);   // R5 azumaya is posts and a roof: its ground shows
                inputs.Buildings.Add(rect);
                if (r.Walls)
                {
                    inputs.Walls.Add(new Segment(new Vector2(r.X0, r.Z0), new Vector2(r.X1, r.Z0), 0.15f, r.Height));
                    inputs.Walls.Add(new Segment(new Vector2(r.X0, r.Z1), new Vector2(r.X1, r.Z1), 0.15f, r.Height));
                    inputs.Walls.Add(new Segment(new Vector2(r.X0, r.Z0), new Vector2(r.X0, r.Z1), 0.15f, r.Height));
                    inputs.Walls.Add(new Segment(new Vector2(r.X1, r.Z0), new Vector2(r.X1, r.Z1), 0.15f, r.Height));
                }

                if (r.Doors == null) continue;
                foreach (GreyboxV6Builder.Door d in r.Doors)
                {
                    Vector2 at = d.Side == 'S' ? new Vector2(d.At, r.Z0 - 0.8f) : d.Side == 'N' ? new Vector2(d.At, r.Z1 + 0.8f)
                        : d.Side == 'W' ? new Vector2(r.X0 - 0.8f, d.At) : new Vector2(r.X1 + 0.8f, d.At);
                    inputs.Worn.Add((at, 1.4f));
                }
            }

            foreach (GreyboxV6Builder.Corridor c in GreyboxV6Builder.Corridors)
            {
                inputs.Corridors.Add((c.Path, c.Width * 0.5f));
                if (!c.Closed) continue;
                for (int i = 0; i + 1 < c.Path.Length; i++)
                {
                    Vector2 a = c.Path[i], b = c.Path[i + 1], d = (b - a).normalized, side = new Vector2(-d.y, d.x) * (c.Width * 0.5f);
                    inputs.Walls.Add(new Segment(a + side, b + side, 0.15f, c.Height));
                    inputs.Walls.Add(new Segment(a - side, b - side, 0.15f, c.Height));
                }
            }

            // Walls: the ring tsuiji (inner face ±55), the forecourt, the yard and R7 fences (their gaps open), the karesansui's low walls.
            float ring = GreyboxV6Builder.HalfExtent + GreyboxV6Builder.WallThickness * 0.5f;
            inputs.Walls.Add(new Segment(new Vector2(-ring, ring), new Vector2(ring, ring), 0.3f, GreyboxV6Builder.WallHeight, true));
            inputs.Walls.Add(new Segment(new Vector2(-ring, -ring), new Vector2(ring, -ring), 0.3f, GreyboxV6Builder.WallHeight, true));
            inputs.Walls.Add(new Segment(new Vector2(ring, -ring), new Vector2(ring, ring), 0.3f, GreyboxV6Builder.WallHeight, true));
            inputs.Walls.Add(new Segment(new Vector2(-ring, -ring), new Vector2(-ring, ring), 0.3f, GreyboxV6Builder.WallHeight, true));
            AddWall(inputs, new Vector2(-26f, 20f), new Vector2(-26f, 36f), 0.3f, GreyboxV6Builder.WallHeight, new[] { (10f, 4f) });
            AddWall(inputs, new Vector2(26f, 20f), new Vector2(26f, 36f), 0.3f, GreyboxV6Builder.WallHeight, new[] { (10f, 4f) });
            AddWall(inputs, new Vector2(-26f, 20f), new Vector2(-4f, 20f), 0.3f, GreyboxV6Builder.WallHeight, null);
            AddWall(inputs, new Vector2(4f, 20f), new Vector2(26f, 20f), 0.3f, GreyboxV6Builder.WallHeight, null);
            foreach ((Vector2 a, Vector2 b, (float at, float width)[] gaps) in WorldV5Dress.FenceLines) AddWall(inputs, a, b, 0.15f, 1.8f, gaps);
            foreach ((Vector2 a, Vector2 b) in new[] { (new Vector2(12f, -16f), new Vector2(16.5f, -16f)), (new Vector2(19.5f, -16f), new Vector2(28f, -16f)),
                                                       (new Vector2(28f, -30f), new Vector2(28f, -23.5f)), (new Vector2(28f, -20.5f), new Vector2(28f, -16f)) })
                inputs.Walls.Add(new Segment(a, b, 0.2f, 0.9f, true));

            // Openings: the forecourt doors, K1's doors, the R7 and yard gates — flat and trodden.
            foreach (Vector2 at in new[] { new Vector2(-26f, 30f), new Vector2(26f, 30f), new Vector2(-30.4f, -7f), new Vector2(-30.4f, 7f), new Vector2(-35.6f, 0f),
                                           new Vector2(27f, 1f), new Vector2(45f, 1f), new Vector2(36f, 10f), new Vector2(36f, -8f),
                                           new Vector2(-36f, 30f), new Vector2(-28f, 32f), new Vector2(-48f, 46f), new Vector2(0f, -18f), new Vector2(0f, 20f) })
            {
                inputs.FlatDiscs.Add((at, 1.5f));
                inputs.Worn.Add((at, 1.6f));
            }

            foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates)
            {
                inputs.FlatDiscs.Add((g.Position, 2.5f));
                inputs.Worn.Add((g.Position, 2.6f));
            }

            foreach (Vector2 w in GreyboxV6Builder.WeaponPositions) inputs.FlatDiscs.Add((w, 1.5f));
            foreach (Vector2 w in GreyboxV6Builder.ExtraWeaponPositions) inputs.FlatDiscs.Add((w, 1.5f));

            inputs.Yards.Add(new Vector4(-48f, 30f, -28f, 48f));          // the service yard: beaten earth
            inputs.MossGardens.Add(new Vector4(27f, -8f, 45f, 10f));      // R7, the sakura court: koke-niwa (LOOK-BIBLE Z11)

            foreach ((string name, Vector2 position, float radius) in GreyboxV6Builder.GardenTreeSpots())
            {
                (string family, int variation, Vector3 pos, Quaternion rot, Vector3 scale) t = WorldV5Dress.GardenTreeAt(new Vector3(position.x, 0f, position.y), false);
                float canopy = t.family == "black_pine" ? 2.6f : t.family == "momiji" ? (t.variation == 3 ? 1.8f : 3.2f) : 3.6f;
                inputs.Trees.Add(new Tree { Position = position, Trunk = radius, Canopy = canopy * t.scale.x, Pine = t.family == "black_pine" });
            }

            inputs.Trees.Add(new Tree { Position = GreyboxV6Builder.GreatSakura, Trunk = 2.6f, Canopy = 4.6f });
            if (!string.IsNullOrEmpty(layoutJson)) ApplyLayout(inputs, ParseLayout(layoutJson));
            return inputs;
        }

        /// <summary>
        /// The layout plan on the ground: its new buildings (the content pass's nagaya rows: floors, walls, drip strips, door thresholds,
        /// flat round them) and its clear points (spawn / weapon / door: flat, the spawns and doors trodden). The zones feed only the S-07
        /// masks (<see cref="BuildVegetationMasks"/>).
        /// </summary>
        public static void ApplyLayout(Inputs inputs, VegLayout layout)
        {
            inputs.Layout = layout;
            foreach ((string id, Vector4 rect, bool isNew, List<Vector2> doors) in layout.Buildings)
            {
                if (!isNew) continue;   // R1–R4 and K1 are the builder's own rooms / corridor, already in
                inputs.Paved.Add(rect);
                inputs.Buildings.Add(rect);
                inputs.FlatRects.Add((rect, 1.5f));
                inputs.Walls.Add(new Segment(new Vector2(rect.x, rect.y), new Vector2(rect.z, rect.y), 0.15f, 3.65f));
                inputs.Walls.Add(new Segment(new Vector2(rect.x, rect.w), new Vector2(rect.z, rect.w), 0.15f, 3.65f));
                inputs.Walls.Add(new Segment(new Vector2(rect.x, rect.y), new Vector2(rect.x, rect.w), 0.15f, 3.65f));
                inputs.Walls.Add(new Segment(new Vector2(rect.z, rect.y), new Vector2(rect.z, rect.w), 0.15f, 3.65f));
                foreach (Vector2 d in doors)
                {
                    inputs.FlatDiscs.Add((d, 1.5f));
                    inputs.Worn.Add((d, 1.4f));
                }
            }

            foreach (var (at, r, kind, _) in layout.Clear)
            {
                inputs.FlatDiscs.Add((at, Mathf.Min(r, 2.5f)));
                if (kind != "weapon") inputs.Worn.Add((at, Mathf.Min(r, 2.6f)));
            }
        }

        /// <summary>A Z6 wall or fence from a to b with its gaps (centre along a→b, width) open.</summary>
        private static void AddWall(Inputs inputs, Vector2 a, Vector2 b, float half, float height, (float at, float width)[] gaps)
        {
            float len = Vector2.Distance(a, b);
            Vector2 d = (b - a) / len;
            float cursor = 0f;
            if (gaps != null)
                foreach ((float at, float width) in gaps)
                {
                    if (at - width * 0.5f > cursor + 0.05f) inputs.Walls.Add(new Segment(a + d * cursor, a + d * (at - width * 0.5f), half, height, true));
                    cursor = at + width * 0.5f;
                }

            if (len > cursor + 0.05f) inputs.Walls.Add(new Segment(a + d * cursor, b, half, height, true));
        }

        /// <summary>wp092.roads.v1 (ROADS-CONTRACT): roads (id, type, width, points [[x, z] …]) and zones (id, kind, polygon). Throws on a malformed file.</summary>
        public static void ParseRoads(string json, List<Road> roads, List<Zone> zones)
        {
            Dictionary<string, object> root = ArtJson.AsObject(ArtJson.Parse(json), "roads.json");
            string schema = ArtJson.AsString(ArtJson.Require(root, "schema"), "schema");
            if (schema != "wp092.roads.v1") throw new FormatException("[GROUND] roads.json schema " + schema + " (want wp092.roads.v1)");
            foreach (object o in ArtJson.AsList(ArtJson.Require(root, "roads"), "roads"))
            {
                Dictionary<string, object> r = ArtJson.AsObject(o, "road");
                string id = ArtJson.AsString(ArtJson.Require(r, "id"), "road id");
                string type = ArtJson.AsString(ArtJson.Require(r, "type"), id + ".type");
                RoadType t = type switch
                {
                    "gravel" => RoadType.Gravel, "dirt" => RoadType.Dirt, "ishidatami" => RoadType.Ishidatami, "boardwalk" => RoadType.Boardwalk,
                    "stepstone" => RoadType.Stepstone, _ => throw new FormatException("[GROUND] road " + id + " type " + type),
                };
                float width = ArtJson.AsFloat(ArtJson.Require(r, "width"), id + ".width");
                Vector2[] points = Points(ArtJson.Require(r, "points"), id + ".points");
                if (points.Length < 2) throw new FormatException("[GROUND] road " + id + " needs ≥ 2 points");
                float len = 0f;
                for (int k = 1; k < points.Length; k++) len += Vector2.Distance(points[k - 1], points[k]);
                roads.Add(new Road { Id = id, Type = t, Width = width, Points = points, Ruts = t == RoadType.Dirt && width >= 2.2f && len > 8f, Litter = !IsPaved(t) && InBambooBand(points) });
            }

            object zoneList = ArtJson.Get(root, "zones");
            if (zoneList == null) return;
            foreach (object o in ArtJson.AsList(zoneList, "zones"))
            {
                Dictionary<string, object> z = ArtJson.AsObject(o, "zone");
                string id = ArtJson.AsString(ArtJson.Require(z, "id"), "zone id");
                string kind = ArtJson.AsString(ArtJson.Require(z, "kind"), id + ".kind");
                ZoneKind k = kind switch
                {
                    "soil" => ZoneKind.Soil, "moss" => ZoneKind.Moss, "mud" => ZoneKind.Mud, "gravel" => ZoneKind.Gravel, "wet" => ZoneKind.Wet,
                    _ => throw new FormatException("[GROUND] zone " + id + " kind " + kind),
                };
                Vector2[] polygon = Points(ArtJson.Require(z, "polygon"), id + ".polygon");
                if (polygon.Length < 3) throw new FormatException("[GROUND] zone " + id + " needs ≥ 3 points");
                zones.Add(new Zone { Id = id, Kind = k, Polygon = polygon });
            }
        }

        private static bool IsPaved(RoadType t) => t == RoadType.Ishidatami || t == RoadType.Boardwalk || t == RoadType.Stepstone;

        /// <summary>A lane through the bamboo band (its mean point at x &lt; −36.5, |z| &lt; 31) carries the culm-leaf litter.</summary>
        public static bool InBambooBand(Vector2[] points)
        {
            Vector2 mean = Vector2.zero;
            foreach (Vector2 q in points) mean += q;
            mean /= points.Length;
            return mean.x < -36.5f && Mathf.Abs(mean.y) < 31f;
        }

        private static Vector2[] Points(object value, string what)
        {
            List<object> list = ArtJson.AsList(value, what);
            var points = new Vector2[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                List<object> xz = ArtJson.AsList(list[i], what + "[" + i + "]");
                if (xz.Count != 2) throw new FormatException("[GROUND] " + what + "[" + i + "] is not [x, z]");
                points[i] = new Vector2(ArtJson.AsFloat(xz[0], what), ArtJson.AsFloat(xz[1], what));
            }

            return points;
        }

        public static string ReadRoadsJson()
        {
            string env = Environment.GetEnvironmentVariable("DD_ROADS_JSON");
            if (!string.IsNullOrEmpty(env)) return File.ReadAllText(env);
            return File.Exists(RoadsJsonPath) ? File.ReadAllText(RoadsJsonPath) : null;
        }

        // ---------------------------------------------------------------- noise (deterministic, shared by relief and masks)

        public static float Hash(int x, int z, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(salt * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffffff) / 16777215f;
            }
        }

        /// <summary>Value noise 0…1, smooth (C1), period-free.</summary>
        public static float Noise(float x, float z, int salt)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            float ux = fx * fx * (3f - 2f * fx), uz = fz * fz * (3f - 2f * fz);
            float a = Hash(ix, iz, salt), b = Hash(ix + 1, iz, salt), c = Hash(ix, iz + 1, salt), d = Hash(ix + 1, iz + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uz);
        }

        /// <summary>Two octaves 0…1 at <paramref name="scale"/> m and 0.37 × it.</summary>
        public static float Field(Vector2 p, float scale, int salt)
        {
            return 0.65f * Noise(p.x / scale, p.y / scale, salt) + 0.35f * Noise(p.x / (scale * 0.37f) + 11.2f, p.y / (scale * 0.37f) + 3.9f, salt + 1);
        }

        /// <summary>Path edge breakup: ±0.3 m at a 2 m wavelength + a 0.6 m ripple (LOOK-BIBLE Z5: no ruled edge).</summary>
        public static float EdgeNoise(Vector2 p)
        {
            return (Noise(p.x / 2f, p.y / 2f, 31) - 0.5f) * 0.46f + (Noise(p.x / 0.6f, p.y / 0.6f, 37) - 0.5f) * 0.16f;
        }

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ---------------------------------------------------------------- geometry helpers

        public static float RectDistance(Vector4 r, Vector2 p)
        {
            float dx = Mathf.Max(r.x - p.x, 0f, p.x - r.z), dz = Mathf.Max(r.y - p.y, 0f, p.y - r.w);
            if (dx > 0f || dz > 0f) return Mathf.Sqrt(dx * dx + dz * dz);
            return -Mathf.Min(Mathf.Min(p.x - r.x, r.z - p.x), Mathf.Min(p.y - r.y, r.w - p.y));
        }

        public static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b, out float t, out float side)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            t = l2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            Vector2 q = a + ab * t;
            side = ab.x * (p.y - a.y) - ab.y * (p.x - a.x);
            return Vector2.Distance(p, q);
        }

        /// <summary>Distance to a polyline, and the signed lateral offset from the nearest segment (left +).</summary>
        public static float PolylineDistance(Vector2 p, Vector2[] line, out float lateral)
        {
            float best = float.MaxValue;
            lateral = 0f;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                float d = SegmentDistance(p, line[i], line[i + 1], out _, out float side);
                if (d < best) { best = d; lateral = side >= 0f ? d : -d; }
            }

            return best;
        }

        public static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        public static float PolygonEdgeDistance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) best = Mathf.Min(best, SegmentDistance(p, poly[j], poly[i], out _, out _));
            return best;
        }

        // ---------------------------------------------------------------- relief

        /// <summary>0 where the ground must stay flat, 1 one metre past every flat zone (smooth).</summary>
        public static float FlatWeight(Inputs inputs, Vector2 p)
        {
            float w = 1f;
            foreach ((Vector4 rect, float margin) in inputs.FlatRects)
            {
                float d = RectDistance(rect, p);
                if (d < margin + 1f) w = Mathf.Min(w, Smooth(margin, margin + 1f, d));
                if (w <= 0f) return 0f;
            }

            foreach ((Vector2 at, float radius) in inputs.FlatDiscs)
            {
                float d = Vector2.Distance(at, p);
                if (d < radius + 1f) w = Mathf.Min(w, Smooth(radius, radius + 1f, d));
                if (w <= 0f) return 0f;
            }

            foreach ((Vector2[] path, float half) in inputs.Corridors)
            {
                float d = PolylineDistance(p, path, out _);
                if (d < half + 1.6f) w = Mathf.Min(w, Smooth(half + 0.6f, half + 1.6f, d));
                if (w <= 0f) return 0f;
            }

            foreach (Pond pond in inputs.Ponds)
            {
                float d = pond.Distance(p);
                if (d < 2f) w = Mathf.Min(w, Smooth(1f, 2f, d));
                if (w <= 0f) return 0f;
            }

            foreach ((Vector2[] path, float half) in inputs.FlatLines)
            {
                float d = PolylineDistance(p, path, out _);
                if (d < half + 1.6f) w = Mathf.Min(w, Smooth(half + 0.6f, half + 1.6f, d));
                if (w <= 0f) return 0f;
            }

            return w;
        }

        /// <summary>How much of a path surface (gravel / dirt) covers <paramref name="p"/> (0…1, the mask's own edge).</summary>
        public static float PathCover(Inputs inputs, Vector2 p)
        {
            float cover = 0f;
            foreach (Road r in inputs.Roads)
            {
                if (r.Paved) continue;
                float d = PolylineDistance(p, r.Points, out _);
                if (d > r.Width) continue;
                cover = Mathf.Max(cover, 1f - Smooth(r.Width * 0.5f - 0.2f, r.Width * 0.5f + 0.25f, d + EdgeNoise(p)));
            }

            return cover;
        }

        /// <summary>Where nobody walks: the bamboo band off its lanes and gates, and past the ring (0…1).</summary>
        public static float Unwalked(Vector2 p)
        {
            float ring = GreyboxV6Builder.HalfExtent + 0.6f;
            float outside = Smooth(ring, ring + 1.5f, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
            if (p.x < -37f && p.x > -55f && p.y > -30.5f && p.y < 30.5f)
            {
                float band = Smooth(-37.5f, -38.8f, p.x) * Smooth(30f, 28.8f, Mathf.Abs(p.y));
                float lane = float.MaxValue;
                foreach (Vector2[] l in GreyboxV6Builder.BambooLanes) lane = Mathf.Min(lane, GreyboxV6Builder.DistanceToPolyline(p, l));
                float gate = float.MaxValue;
                foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates) gate = Mathf.Min(gate, Vector2.Distance(p, g.Position));
                outside = Mathf.Max(outside, band * Smooth(2.3f, 3.3f, lane) * Smooth(3.6f, 4.6f, gate));
            }

            return outside;
        }

        /// <summary>The ground height at <paramref name="p"/> (metres over the walkable y 0): walkable hollows and path beds, mounds where nobody walks, root flares and wall banks; 0 in flat zones.</summary>
        public static float Height(Inputs inputs, Vector2 p)
        {
            float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
            if (edge >= ReliefFadeEnd) return 0f;
            float flat = FlatWeight(inputs, p);
            if (flat <= 0f) return 0f;

            float n1 = Field(p, 7.5f, 101), n2 = Field(p, 2.6f, 103);
            float hollow = -WalkDip * Smooth(0.52f, 0.78f, n1 * 0.72f + n2 * 0.28f);
            float h = Mathf.Max(hollow - PathBed * PathCover(inputs, p), MinRelief);

            float unwalked = Unwalked(p);
            if (unwalked > 0f) h = Mathf.Lerp(h, MaxRelief * 0.85f * (0.55f * n1 + 0.45f * n2), unwalked);

            foreach (Tree t in inputs.Trees)
            {
                float d = Vector2.Distance(p, t.Position);
                if (d < t.Trunk + 1.4f) h = Mathf.Max(h, RootFlare * (1f - Smooth(t.Trunk, t.Trunk + 1.4f, d)) * (0.7f + 0.3f * n2));
            }

            foreach (Segment w in inputs.Walls)
            {
                float d = SegmentDistance(p, w.A, w.B, out _, out _) - w.HalfThickness;
                if (d < 0.35f) h = Mathf.Max(h, WallBank * (1f - Smooth(0f, 0.35f, d)));
            }

            h *= flat * (1f - Smooth(ReliefFadeStart, ReliefFadeEnd, edge));
            return Mathf.Clamp(h, MinRelief, MaxRelief);
        }

        /// <summary>Contact occlusion 0…1 (1 = open): wall feet, trunks and building bases.</summary>
        public static float ContactOcclusion(Inputs inputs, Vector2 p)
        {
            float ao = 1f;
            foreach (Segment w in inputs.Walls)
            {
                float d = SegmentDistance(p, w.A, w.B, out _, out _) - w.HalfThickness;
                if (d < 1.2f) ao = Mathf.Min(ao, Mathf.Lerp(0.55f, 1f, Smooth(0f, Mathf.Min(1.2f, 0.4f * w.Height), d)));
            }

            foreach (Tree t in inputs.Trees)
            {
                float d = Vector2.Distance(p, t.Position) - t.Trunk;
                if (d < 1.4f) ao = Mathf.Min(ao, Mathf.Lerp(0.62f, 1f, Smooth(0f, 1.4f, d)));
            }

            foreach (Vector4 b in inputs.Buildings)
            {
                float d = RectDistance(b, p);
                if (d < 1.5f) ao = Mathf.Min(ao, Mathf.Lerp(0.6f, 1f, Smooth(0f, 1.5f, Mathf.Max(0f, d))));
            }

            return ao;
        }

        // ---------------------------------------------------------------- masks

        public sealed class Masks
        {
            public float[] Gravel, Dirt, Mud, Paved;   // A
            public float[] Moss, Litter, Wet, Pebble;  // B

            public Masks()
            {
                int a = MaskASize * MaskASize, b = MaskBSize * MaskBSize;
                Gravel = new float[a]; Dirt = new float[a]; Mud = new float[a]; Paved = new float[a];
                Moss = new float[b]; Litter = new float[b]; Wet = new float[b]; Pebble = new float[b];
            }
        }

        public static float TexelA => 2f * Half / MaskASize;
        public static float TexelB => 2f * Half / MaskBSize;

        /// <summary>World XZ of a mask texel centre.</summary>
        public static Vector2 TexelCentre(int x, int y, int size) => new Vector2(-Half + (x + 0.5f) * 2f * Half / size, -Half + (y + 0.5f) * 2f * Half / size);

        private static void TexelRange(Vector2 min, Vector2 max, int size, out int x0, out int y0, out int x1, out int y1)
        {
            float texel = 2f * Half / size;
            x0 = Mathf.Clamp(Mathf.FloorToInt((min.x + Half) / texel), 0, size - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((min.y + Half) / texel), 0, size - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt((max.x + Half) / texel), 0, size - 1);
            y1 = Mathf.Clamp(Mathf.CeilToInt((max.y + Half) / texel), 0, size - 1);
        }

        private static void BoundsOf(Vector2[] points, float pad, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 q in points) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
            min -= Vector2.one * pad;
            max += Vector2.one * pad;
        }

        /// <summary>Rasterises every ground feature into the two world masks (max-blend per channel; A at 0.083 m, B at 0.166 m).</summary>
        public static Masks BuildMasks(Inputs inputs)
        {
            var m = new Masks();
            int sa = MaskASize, sb = MaskBSize;

            // Base layer B: the open ground's moss tendency (shade, tone field) is the shader's; the mask adds the places moss lives.
            // Grove floor: bamboo litter, moss and soil (LOOK-BIBLE Z3).
            for (int y = 0; y < sb; y++)
            for (int x = 0; x < sb; x++)
            {
                Vector2 p = TexelCentre(x, y, sb);
                int i = y * sb + x;
                float unwalked = Unwalked(p);
                bool grove = p.x < -37f && p.x > -55f && Mathf.Abs(p.y) < 30.5f;
                if (grove) { m.Litter[i] = Mathf.Max(m.Litter[i], 0.75f * unwalked + 0.35f); m.Moss[i] = Mathf.Max(m.Moss[i], 0.45f * unwalked); }
                else if (unwalked > 0f) { m.Litter[i] = Mathf.Max(m.Litter[i], 0.5f * unwalked); m.Moss[i] = Mathf.Max(m.Moss[i], 0.55f * unwalked); }
            }

            // Moss gardens (R7 koke-niwa): moss to the fences.
            foreach (Vector4 g in inputs.MossGardens) StampRectB(m.Moss, g, 0.4f, 1f);

            // Yards: beaten earth, damp in patches.
            foreach (Vector4 yard in inputs.Yards)
            {
                TexelRange(new Vector2(yard.x, yard.y), new Vector2(yard.z, yard.w), sa, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sa);
                    float inside = Smooth(0.4f, 1.6f, -RectDistance(yard, p) + EdgeNoise(p));
                    if (inside <= 0f) continue;
                    int i = y * sa + x;
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], inside * (0.7f + 0.3f * Field(p, 3f, 211)));
                    m.Mud[i] = Mathf.Max(m.Mud[i], inside * 0.5f * Smooth(0.55f, 0.75f, Field(p, 2.2f, 213)));
                }
            }

            // Trodden ground: spawn gates, door thresholds, openings.
            foreach ((Vector2 at, float radius) in inputs.Worn)
            {
                TexelRange(at - Vector2.one * (radius + 0.6f), at + Vector2.one * (radius + 0.6f), sa, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sa);
                    float w = 1f - Smooth(radius * 0.45f, radius + 0.4f, Vector2.Distance(p, at) + EdgeNoise(p) * 1.4f);
                    int i = y * sa + x;
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.62f * w);
                }
            }

            // Trees: a bare root zone, litter under the crown (drifted 0.3 R downwind, +X / +Z at 24°), moss in the shade.
            var downwind = new Vector2(Mathf.Cos(24f * Mathf.Deg2Rad), Mathf.Sin(24f * Mathf.Deg2Rad));
            foreach (Tree t in inputs.Trees)
            {
                float reach = t.Canopy * 1.4f;
                TexelRange(t.Position - Vector2.one * reach, t.Position + Vector2.one * reach, sb, out int x0, out int y0, out int x1, out int y1);
                Vector2 litterCentre = t.Position + downwind * (0.3f * t.Canopy);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sb);
                    int i = y * sb + x;
                    float dl = Vector2.Distance(p, litterCentre) / t.Canopy;
                    float n = Field(p, 1.3f, 307);
                    m.Litter[i] = Mathf.Max(m.Litter[i], (1f - Smooth(0.55f, 1.3f, dl + (n - 0.5f) * 0.4f)) * (t.Pine ? 0.45f : 0.9f));
                    float dm = Vector2.Distance(p, t.Position) / t.Canopy;
                    m.Moss[i] = Mathf.Max(m.Moss[i], (1f - Smooth(0.3f, 0.9f, dm)) * 0.55f);
                }

                float root = t.Trunk + 1.1f;
                TexelRange(t.Position - Vector2.one * (root + 0.5f), t.Position + Vector2.one * (root + 0.5f), sa, out x0, out y0, out x1, out y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sa);
                    float w = 1f - Smooth(t.Trunk + 0.2f, root, Vector2.Distance(p, t.Position) + EdgeNoise(p));
                    int i = y * sa + x;
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.55f * w);
                }
            }

            // Walls: moss along the foot in 3–7 m runs (gaps between, Z6), stronger on the shaded (south-facing) side; a damp line.
            foreach (Segment w in inputs.Walls)
            {
                float band = Mathf.Min(1.4f, 0.5f + 0.4f * w.Height);
                BoundsOf(new[] { w.A, w.B }, band + w.HalfThickness + 0.5f, out Vector2 min, out Vector2 max);
                TexelRange(min, max, sb, out int x0, out int y0, out int x1, out int y1);
                Vector2 dir = (w.B - w.A).normalized;
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sb);
                    float d = SegmentDistance(p, w.A, w.B, out float t, out float side) - w.HalfThickness;
                    if (d > band || d < -0.05f) continue;
                    Vector2 normal = side >= 0f ? new Vector2(-dir.y, dir.x) : new Vector2(dir.y, -dir.x);
                    float shade = 0.6f + 0.4f * Mathf.Clamp01(-normal.y);
                    float along = Vector2.Dot(p - w.A, dir);
                    float runs = Smooth(0.35f, 0.5f, Noise(along / 5f, w.A.x * 0.13f + w.A.y * 0.07f, 401));
                    int i = y * sb + x;
                    m.Moss[i] = Mathf.Max(m.Moss[i], (1f - Smooth(band * 0.35f, band, d)) * shade * Mathf.Lerp(0.45f, 1f, runs));
                    m.Wet[i] = Mathf.Max(m.Wet[i], (1f - Smooth(0f, 0.45f, d)) * 0.5f * shade);
                }
            }

            // Buildings: the amaochi drip strip (river pebbles, 0–0.6 m), wet along its line.
            foreach (Vector4 b in inputs.Buildings)
            {
                TexelRange(new Vector2(b.x - 1f, b.y - 1f), new Vector2(b.z + 1f, b.w + 1f), sa, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sa);
                    float d = RectDistance(b, p);
                    if (d < 0f || d > 0.8f) continue;
                    float strip = 1f - Smooth(0.5f, 0.62f, d);
                    int i = y * sa + x;
                    m.Gravel[i] = Mathf.Max(m.Gravel[i], strip);
                    m.Mud[i] = Mathf.Max(m.Mud[i], 0.35f * (1f - Smooth(0.25f, 0.5f, Mathf.Abs(d - 0.45f))));
                }

                TexelRange(new Vector2(b.x - 1f, b.y - 1f), new Vector2(b.z + 1f, b.w + 1f), sb, out x0, out y0, out x1, out y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sb);
                    float d = RectDistance(b, p);
                    if (d < -0.2f || d > 0.9f) continue;
                    int i = y * sb + x;
                    m.Pebble[i] = Mathf.Max(m.Pebble[i], 1f - Smooth(0.6f, 0.9f, d));
                    m.Wet[i] = Mathf.Max(m.Wet[i], 0.6f * (1f - Smooth(0.6f, 0.9f, d)));
                }
            }

            // Ponds: a pebble shore (0–0.7 m), a mud band (to 1.6 m), damp moss banks (to 2.5 m); mud under the water.
            foreach (Pond pond in inputs.Ponds)
            {
                float reach = Mathf.Max(pond.Semi.x, pond.Semi.y) + 3f;
                TexelRange(pond.Centre - Vector2.one * reach, pond.Centre + Vector2.one * reach, sa, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sa);
                    float d = pond.Distance(p) + EdgeNoise(p) * 0.6f;
                    if (d > 2f) continue;
                    int i = y * sa + x;
                    if (d < 0f) { m.Mud[i] = 1f; continue; }
                    m.Gravel[i] = Mathf.Max(m.Gravel[i], (1f - Smooth(0.45f, 0.75f, d)) * (0.75f + 0.25f * Field(p, 0.9f, 503)));
                    m.Mud[i] = Mathf.Max(m.Mud[i], 0.7f * (1f - Smooth(0.5f, 1.6f, d)));
                }

                TexelRange(pond.Centre - Vector2.one * reach, pond.Centre + Vector2.one * reach, sb, out x0, out y0, out x1, out y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = TexelCentre(x, y, sb);
                    float d = pond.Distance(p);
                    if (d > 3f) continue;
                    int i = y * sb + x;
                    m.Pebble[i] = Mathf.Max(m.Pebble[i], 1f - Smooth(0.7f, 1.0f, d));
                    m.Wet[i] = Mathf.Max(m.Wet[i], 1f - Smooth(0.3f, 2.6f, d));
                    m.Moss[i] = Mathf.Max(m.Moss[i], 0.7f * Smooth(0.5f, 1.0f, d) * (1f - Smooth(1.8f, 2.8f, d)));
                }
            }

            // Zones (roads.json hints).
            foreach (Zone z in inputs.Zones) StampZone(m, z);

            // Roads: gravel / dirt surfaces with broken edges, a worn dirt fringe beside gravel, ruts; paved roads as covered strips.
            foreach (Road r in inputs.Roads) StampRoad(m, r);

            // Paved / covered last (it wins): the core, then the 0.25 m contact halo outside it (coded below PavedCore).
            foreach (Vector4 r in inputs.Paved) StampPavedRect(m, r);
            foreach ((Vector2[] path, float half) in inputs.Corridors) StampPavedLine(m, path, half);
            return m;
        }

        private static void StampRectB(float[] channel, Vector4 r, float soft, float value)
        {
            int sb = MaskBSize;
            TexelRange(new Vector2(r.x - soft, r.y - soft), new Vector2(r.z + soft, r.w + soft), sb, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = TexelCentre(x, y, sb);
                float w = 1f - Smooth(-soft, soft, RectDistance(r, p));
                int i = y * sb + x;
                channel[i] = Mathf.Max(channel[i], w * value);
            }
        }

        private static void StampZone(Masks m, Zone z)
        {
            int sa = MaskASize;
            BoundsOf(z.Polygon, 1f, out Vector2 min, out Vector2 max);
            TexelRange(min, max, sa, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = TexelCentre(x, y, sa);
                float d = PolygonEdgeDistance(p, z.Polygon) * (InPolygon(p, z.Polygon) ? 1f : -1f) + EdgeNoise(p);
                float w = Smooth(-0.6f, 0.6f, d);
                if (w <= 0f) continue;
                int i = y * sa + x;
                switch (z.Kind)
                {
                    case ZoneKind.Soil: m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.65f * w); break;
                    case ZoneKind.Mud: m.Mud[i] = Mathf.Max(m.Mud[i], 0.8f * w); m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.5f * w); break;
                    case ZoneKind.Gravel: m.Gravel[i] = Mathf.Max(m.Gravel[i], 0.9f * w); break;
                    case ZoneKind.Moss:
                    case ZoneKind.Wet:
                        int bx = Mathf.Clamp(x * MaskBSize / sa, 0, MaskBSize - 1), by = Mathf.Clamp(y * MaskBSize / sa, 0, MaskBSize - 1), j = by * MaskBSize + bx;
                        if (z.Kind == ZoneKind.Moss) m.Moss[j] = Mathf.Max(m.Moss[j], w);
                        else m.Wet[j] = Mathf.Max(m.Wet[j], 0.8f * w);
                        break;
                }
            }
        }

        private static void StampRoad(Masks m, Road r)
        {
            int sa = MaskASize;
            float half = r.Width * 0.5f;
            BoundsOf(r.Points, half + 1.2f, out Vector2 min, out Vector2 max);
            TexelRange(min, max, sa, out int x0, out int y0, out int x1, out int y1);
            float along0 = Hash(r.Id.Length, r.Points.Length, 601) * 100f;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = TexelCentre(x, y, sa);
                float d = PolylineDistance(p, r.Points, out float lateral);
                if (d > half + 1.2f) continue;
                int i = y * sa + x;
                if (r.Type == RoadType.Stepstone)
                {
                    // Stepping stones (kit pieces, lead): the ground shows between them — a trodden line of soil through the moss.
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.42f * (1f - Smooth(half * 0.35f, half * 0.75f, d + EdgeNoise(p) * 0.3f)));
                    continue;
                }

                if (r.Paved)
                {
                    // The kit mesh is the surface; the mask marks it covered, with its contact halo (StampPaved's coding).
                    float core = 1f - Smooth(half - 0.04f, half + 0.04f, d);
                    float halo = (1f - Smooth(half, half + PavedHalo, d)) * 0.5f;
                    m.Paved[i] = Mathf.Max(m.Paved[i], core >= 0.5f ? Mathf.Lerp(PavedCore, 1f, core) : halo);
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.45f * (1f - Smooth(half, half + 0.35f, d + EdgeNoise(p) * 0.5f)));
                    continue;
                }

                float e = d + EdgeNoise(p);
                float surface = 1f - Smooth(half - 0.12f, half + 0.12f, e);
                if (r.Type == RoadType.Gravel)
                {
                    m.Gravel[i] = Mathf.Max(m.Gravel[i], surface);
                    // A worn soil fringe between the gravel and the moss (edge wear, 0.45 m).
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], 0.55f * (1f - Smooth(half + 0.05f, half + 0.5f, e)) * (1f - surface * 0.6f));
                }
                else
                {
                    m.Dirt[i] = Mathf.Max(m.Dirt[i], surface);
                    // Damp centre, puddle seeds along the way.
                    m.Mud[i] = Mathf.Max(m.Mud[i], surface * 0.28f * Smooth(0.45f, 0.7f, Field(p, 1.8f, 607)));
                    if (r.Ruts)
                    {
                        float rut = 1f - Smooth(RutHalf * 0.5f, RutHalf, Mathf.Abs(Mathf.Abs(lateral) - RutOffset) + (Noise(p.x / 0.4f, p.y / 0.4f, 611) - 0.5f) * 0.05f);
                        float run = Smooth(0.3f, 0.45f, Noise((p.x + p.y) / 3.5f + along0, 0.5f, 613));
                        m.Mud[i] = Mathf.Max(m.Mud[i], rut * run * surface);
                    }
                }
            }

            if (!r.Litter) return;
            int sb = MaskBSize;
            TexelRange(min, max, sb, out x0, out y0, out x1, out y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = TexelCentre(x, y, sb);
                float d = PolylineDistance(p, r.Points, out _);
                int i = y * sb + x;
                m.Litter[i] = Mathf.Max(m.Litter[i], 0.45f * (1f - Smooth(half * 0.4f, half + 0.6f, d)) + 0.25f * Smooth(half * 0.6f, half + 0.3f, d));
            }
        }

        private static void StampPavedRect(Masks m, Vector4 r)
        {
            int sa = MaskASize;
            TexelRange(new Vector2(r.x - 0.5f, r.y - 0.5f), new Vector2(r.z + 0.5f, r.w + 0.5f), sa, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = TexelCentre(x, y, sa);
                float d = RectDistance(r, p);
                int i = y * sa + x;
                float v = d <= 0f ? 1f : d < PavedHalo ? 0.5f * (1f - d / PavedHalo) : 0f;
                if (v >= PavedCore) { m.Paved[i] = 1f; m.Gravel[i] = 0f; m.Dirt[i] = 0f; m.Mud[i] = 0f; }
                else if (m.Paved[i] < PavedCore) m.Paved[i] = Mathf.Max(m.Paved[i], v);
            }
        }

        private static void StampPavedLine(Masks m, Vector2[] path, float half)
        {
            int sa = MaskASize;
            BoundsOf(path, half + 0.5f, out Vector2 min, out Vector2 max);
            TexelRange(min, max, sa, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = TexelCentre(x, y, sa);
                float d = PolylineDistance(p, path, out _) - half;
                int i = y * sa + x;
                float v = d <= 0f ? 1f : d < PavedHalo ? 0.5f * (1f - d / PavedHalo) : 0f;
                if (v >= PavedCore) { m.Paved[i] = 1f; m.Gravel[i] = 0f; m.Dirt[i] = 0f; m.Mud[i] = 0f; }
                else if (m.Paved[i] < PavedCore) m.Paved[i] = Mathf.Max(m.Paved[i], v);
            }
        }

        // ---------------------------------------------------------------- mesh

        public static float StepOf(int cx, int cz)
        {
            float x0 = -Half + cx * ChunkSize, z0 = -Half + cz * ChunkSize;
            bool fine = x0 >= -FineHalf - 1e-3f && x0 + ChunkSize <= FineHalf + 1e-3f && z0 >= -FineHalf - 1e-3f && z0 + ChunkSize <= FineHalf + 1e-3f;
            return fine ? FineStep : CoarseStep;
        }

        /// <summary>Quads under the plaza slate and the hall plinth are left out (fully covered; the slate sits 12 mm over y 0).</summary>
        public static bool Covered(Vector2 a, Vector2 b)
        {
            foreach (Vector4 r in WorldV5Ground.PlazaRects)
                if (a.x >= r.x + 0.3f && b.x <= r.z - 0.3f && a.y >= r.y + 0.3f && b.y <= r.w - 0.3f) return true;
            return a.x >= -19.2f && b.x <= 19.2f && a.y >= 36.8f && b.y <= 49.7f;
        }

        /// <summary>One chunk's mesh: positions (relief), normals (central differences of <see cref="Height"/>, seamless across chunks), colour R hollow depth (0…1 of <see cref="WalkDip"/>), G contact occlusion.</summary>
        public static Mesh ChunkMesh(Inputs inputs, int cx, int cz, Mesh mesh)
        {
            float step = StepOf(cx, cz), x0 = -Half + cx * ChunkSize, z0 = -Half + cz * ChunkSize;
            int n = Mathf.RoundToInt(ChunkSize / step);
            var verts = new Vector3[(n + 1) * (n + 1)];
            var normals = new Vector3[verts.Length];
            var colors = new Color32[verts.Length];
            const float e = 0.25f;
            for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                var p = new Vector2(x0 + i * step, z0 + j * step);
                float h = Height(inputs, p);
                int k = j * (n + 1) + i;
                verts[k] = new Vector3(p.x, h, p.y);
                float hx = Height(inputs, p + new Vector2(e, 0f)) - Height(inputs, p - new Vector2(e, 0f));
                float hz = Height(inputs, p + new Vector2(0f, e)) - Height(inputs, p - new Vector2(0f, e));
                normals[k] = new Vector3(-hx / (2f * e), 1f, -hz / (2f * e)).normalized;
                colors[k] = new Color32((byte)Mathf.RoundToInt(255f * Mathf.Clamp01(-h / WalkDip)), (byte)Mathf.RoundToInt(255f * ContactOcclusion(inputs, p)), 0, 255);
            }

            var tris = new List<int>(n * n * 6);
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                var a = new Vector2(x0 + i * step, z0 + j * step);
                if (Covered(a, a + new Vector2(step, step))) continue;
                int k = j * (n + 1) + i;
                // Split along the diagonal that follows the relief (the lower pair), so hollows read round.
                float d0 = verts[k].y + verts[k + n + 2].y, d1 = verts[k + 1].y + verts[k + n + 1].y;
                if (d0 <= d1) tris.AddRange(new[] { k, k + n + 1, k + n + 2, k, k + n + 2, k + 1 });
                else tris.AddRange(new[] { k, k + n + 1, k + 1, k + 1, k + n + 1, k + n + 2 });
            }

            mesh.Clear();
            mesh.indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.colors32 = colors;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------- build

        public struct Report
        {
            public int Chunks, Triangles, Vertices;
            public float MinHeight, MaxHeight;
            public string RoadSource;
            public int Roads;
            public override string ToString() => "ground " + Chunks + " chunks, " + Triangles + " tris, relief " + MinHeight.ToString("F3", CultureInfo.InvariantCulture) + "…" + MaxHeight.ToString("F3", CultureInfo.InvariantCulture) + " m, " + Roads + " roads (" + RoadSource + ")";
        }

        /// <summary>The ponds as the greybox built them (Pond{i}_Water: centre, scale = 2 × semi, rotation −yaw).</summary>
        public static List<Pond> PondsOf(Transform greybox)
        {
            var ponds = new List<Pond>();
            for (int i = 0; i < greybox.childCount; i++)
            {
                Transform t = greybox.GetChild(i);
                if (!t.name.StartsWith("Pond", StringComparison.Ordinal) || !t.name.EndsWith("_Water", StringComparison.Ordinal)) continue;
                ponds.Add(new Pond { Centre = new Vector2(t.position.x, t.position.z), Semi = new Vector2(t.localScale.x, t.localScale.z) * 0.5f, YawDeg = -t.eulerAngles.y });
            }

            return ponds;
        }

        /// <summary>
        /// Builds the ground under <paramref name="dress"/>: masks (PNG, linear), the material, the chunk meshes (one asset), the chunk
        /// renderers (no collider, no shadow cast, off the baked probes like the box was: AD 07.10 M1); the greybox box keeps its collider
        /// and loses its renderer.
        /// </summary>
        public static Report Build(Transform dress, Transform greybox)
        {
            Transform box = greybox.Find("Ground");
            if (box == null) throw new InvalidOperationException("greybox Ground missing");
            Inputs inputs = LayoutInputs(PondsOf(greybox), ReadRoadsJson(), ReadLayoutJson());
            EnsureFolder(Folder);
            Masks masks = BuildMasks(inputs);
            Texture2D maskA = WriteMask(MaskAPath, MaskASize, masks.Gravel, masks.Dirt, masks.Mud, masks.Paved);
            Texture2D maskB = WriteMask(MaskBPath, MaskBSize, masks.Moss, masks.Litter, masks.Wet, masks.Pebble);
            WriteVegetationMasks(inputs, BuildVegetationMasks(inputs));
            Material material = BuildMaterial(maskA, maskB);

            var root = new GameObject(RootName) { layer = PrototypeLayers.Environment }.transform;
            root.SetParent(dress, false);
            var report = new Report { MinHeight = float.MaxValue, MaxHeight = float.MinValue, RoadSource = inputs.RoadSource, Roads = inputs.Roads.Count };
            Dictionary<string, Mesh> existing = LoadMeshes();
            var meshes = new List<Mesh>();
            for (int cz = 0; cz < ChunksPerSide; cz++)
            for (int cx = 0; cx < ChunksPerSide; cx++)
            {
                string name = ChunkPrefix + cx + "_" + cz;
                if (!existing.TryGetValue(name, out Mesh mesh)) mesh = new Mesh { name = name };
                ChunkMesh(inputs, cx, cz, mesh);
                meshes.Add(mesh);
                report.Chunks++;
                report.Triangles += mesh.triangles.Length / 3;
                report.Vertices += mesh.vertexCount;
                report.MinHeight = Mathf.Min(report.MinHeight, mesh.bounds.min.y);
                report.MaxHeight = Mathf.Max(report.MaxHeight, mesh.bounds.max.y);

                var go = new GameObject(name) { layer = PrototypeLayers.Environment };
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                r.allowOcclusionWhenDynamic = true;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }

            SaveMeshes(meshes, existing);
            WorldV5GroundStones.Build(dress, inputs);
            // The box: collider and NavMesh source as before, nothing drawn (its old DD/GroundMoss material would still load six 2K maps).
            var boxRenderer = box.GetComponent<MeshRenderer>();
            if (boxRenderer != null) UnityEngine.Object.DestroyImmediate(boxRenderer);
            Debug.Log("[GROUND] " + report);
            return report;
        }

        private static Dictionary<string, Mesh> LoadMeshes()
        {
            var map = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath))
                if (o is Mesh mesh) map[mesh.name] = mesh;
            return map;
        }

        private static void SaveMeshes(List<Mesh> meshes, Dictionary<string, Mesh> existing)
        {
            if (existing.Count == 0 || AssetDatabase.LoadMainAssetAtPath(MeshPath) == null)
            {
                AssetDatabase.CreateAsset(meshes[0], MeshPath);
                for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
            }
            else
            {
                foreach (Mesh mesh in meshes)
                {
                    if (!AssetDatabase.Contains(mesh)) AssetDatabase.AddObjectToAsset(mesh, MeshPath);
                    EditorUtility.SetDirty(mesh);
                }
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Writes a linear RGBA mask PNG and imports it: no sRGB, mips, clamp, trilinear; <paramref name="compressed"/> BC7 (the
        /// shader's masks) or uncompressed and editor-readable (the S-07 fields: exact values, read by a placement tool, never shipped).
        /// </summary>
        public static Texture2D WriteMask(string path, int size, float[] r, float[] g, float[] b, float[] a, bool compressed = true)
        {
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(Byte(r[i]), Byte(g[i]), Byte(b[i]), Byte(a[i]));
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = size;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = compressed ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            importer.isReadable = !compressed;
            ClearPlatformOverrides(importer);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static byte Byte(float v) => (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(v));

        /// <summary>Platform overrides off (the old T_&lt;role&gt; contract left a Standalone 1024 / BC7 override on these data masks): every target imports the default settings.</summary>
        public static readonly string[] OverridePlatforms = { "Standalone", "WebGL", "iPhone", "Android" };

        private static void ClearPlatformOverrides(TextureImporter importer)
        {
            foreach (string platform in OverridePlatforms) importer.ClearPlatformTextureSettings(platform);
        }

        /// <summary>
        /// The layer sets (the shipped ADR-015 / AP-023 source textures; the shader's grade is tuned to them). AP-031's T_GroundDetail_*
        /// (S-07, 2 m tiles) are not picked up on their own: a swap re-tunes the grade, so it goes in with its own TD frames.
        /// </summary>
        public static readonly (string slot, string[] candidates)[] LayerTextures =
        {
            ("_MossMap", new[] { "T_moss_02_BaseColor" }),
            ("_MossNormal", new[] { "T_moss_02_Normal" }),
            ("_Moss2Map", new[] { "T_moss_03_BaseColor" }),
            ("_SoilMap", new[] { "T_earth_02_BaseColor" }),
            ("_SoilNormal", new[] { "T_earth_02_Normal" }),
            ("_DirtMap", new[] { "T_earth_01_BaseColor" }),
            ("_GravelMap", new[] { "T_ap023_pebble_beach_BaseColor" }),   // the pebble set at 0.42 m reads as stones at TD; T_ap023_gravel is grain
            ("_GravelNormal", new[] { "T_ap023_pebble_beach_Normal" }),
            ("_PebbleMap", new[] { "T_ap023_pebble_beach_BaseColor" }),
            ("_PebbleNormal", new[] { "T_ap023_pebble_beach_Normal" }),
        };

        public static Texture2D FindTexture(string[] candidates)
        {
            foreach (string name in candidates)
            {
                string source = ArtPipelinePaths.SourceTexture(name.Substring(2, name.LastIndexOf('_') - 2), name.Substring(name.LastIndexOf('_') + 1));
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(source);
                if (t != null) return t;
                foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }

            return null;
        }

        public static Material BuildMaterial(Texture2D maskA, Texture2D maskB)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException(ShaderName + " missing");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, MaterialPath);
            }

            m.shader = shader;
            var defaults = new Material(shader);   // the shader's defaults are the tuning (a rebuild re-reads them)
            m.CopyPropertiesFromMaterial(defaults);
            UnityEngine.Object.DestroyImmediate(defaults);
            foreach ((string slot, string[] candidates) in LayerTextures)
            {
                Texture2D t = FindTexture(candidates);
                if (t == null) throw new InvalidOperationException("[GROUND] no texture for " + slot + " (" + string.Join(", ", candidates) + ")");
                m.SetTexture(slot, t);
            }

            m.SetTexture("_MaskA", maskA);
            m.SetTexture("_MaskB", maskB);
            m.SetVector("_MaskBounds", new Vector4(-Half, -Half, 2f * Half, 2f * Half));
            m.SetFloat("_MaskTexel", TexelA);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------------------------------------------------------------- S-07 hand-off: wp093.vegmask.v1 (K/wp-093/MASK-CONTRACT.md)

        public const string VegSchema = "wp093.vegmask.v1", VegVersion = "v001", VegDate = "2026-10-07";
        /// <summary>masks.json "version": <see cref="VegVersion"/>, or -vegVersion / env DD_VEGMASK_VERSION (the final layout's export: v002).</summary>
        public static string VegVersionNow => Arg("-vegVersion") ?? Environment.GetEnvironmentVariable("DD_VEGMASK_VERSION") ?? VegVersion;
        public const float VegHalf = GreyboxV6Builder.HalfExtent;
        public const int VegPixelsPerMeter = 4;
        public static int VegSize => Mathf.RoundToInt(2f * VegHalf * VegPixelsPerMeter);
        public static float VegCell => 1f / VegPixelsPerMeter;
        public static string VegFolder => "Assets/_Art/Resources/VegetationMasks/" + GreyboxV6Builder.SceneName;
        public static string VegMaskAPath => VegFolder + "/veg_mask_a.png";
        public static string VegMaskBPath => VegFolder + "/veg_mask_b.png";
        public static string VegJsonPath => VegFolder + "/masks.json";
        public static string VegClearPath => VegFolder + "/veg_clear.json";
        /// <summary>Corridor coding (mask_a.A): ≥ 128 corridor, 1–127 not a corridor (edge / unwalked band), 0 the runtime's own rule.</summary>
        public const byte VegCorridor = 255, VegNotCorridor = 64;
        public const float CorridorClearance = 1.5f;

        /// <summary>
        /// The contract's zone ids (mask_b.G, LOOK-BIBLE §1). 13 was "bare soil" in wp093.vegmask.v1 §2; S-07 redefined it (07.10,
        /// layout S07-NOTLAR (4)b) as Z01 derz — the plaza's joint tufts: the plaza (Ap005SurfaceTags.V6PlazaRects) carries 13 with the
        /// slate joint lines in mask_b.B. Bare earth (the old 13) is a mask_a.G exclusion now. masks.json "zoneNotes" says so.
        /// </summary>
        public enum VegZone : byte { Auto = 0, Lawn = 1, Grove = 2, Bamboo = 3, PondBank = 4, PathEdge = 5, WallFoot = 6, Building = 7, Court = 8, PlazaEdge = 9, BridgeApproach = 10, MossCourt = 11, Karesansui = 12, PlazaJoints = 13, Wear = 14 }

        /// <summary>mask_b.B on the plaza (zone 13): a slate joint crosses the cell (×2, the most the contract codes) / a stone face (≈ ×0).</summary>
        public const byte JointDensity = 255, FaceDensity = 1;
        /// <summary>Slate Mask R (AO) under this is a joint line (the faces sit at ≈ 250, the grout 76–150).</summary>
        public const byte JointLevel = 170;
        /// <summary>How near a mask cell's centre a joint must pass to mark the cell (m).</summary>
        public const float JointReach = 0.08f;

        /// <summary>
        /// The interim zoning — the one table (first match wins) until the layout plan's zones replace it. Z5 (path edge) and Z6 (wall
        /// foot) are not painted: the runtime grows them from mask_a.R's edge and mask_b.R's footprints (contract §2), so a painted band
        /// would only fight it. Path, water and exclusion cells keep 0 (no plant grows there).
        /// </summary>
        public static readonly (VegZone zone, string rule)[] VegZoneTable =
        {
            (VegZone.PlazaJoints, "the plaza (V6PlazaRects): joint tufts only, mask_b.B = the slate joint lines (the paving stays mask_a.R)"),
            (VegZone.Karesansui, "the rock garden bed x 12…28, z −30…−16"),
            (VegZone.MossCourt, "R7 sakura court (koke-niwa) x 27…45, z −8…10"),
            (VegZone.PondBank, "≤ 2.0 m from a pond rim"),
            (VegZone.BridgeApproach, "≤ 4 m from a crossing end (C8, C1, island)"),
            (VegZone.PlazaEdge, "≤ 4 m from the plaza paving (no GT within 4 m)"),
            (VegZone.Court, "≤ 1.2 m from a kairō floor, and the yard's 1.5 m fence foot"),
            (VegZone.Building, "≤ 1.5 m from a building (R2–R6) beyond its drip strip"),
            (VegZone.Bamboo, "the bamboo band x −54…−37.5, |z| < 30, off its lanes"),
            (VegZone.Wear, "trodden: spawn gates 2.6 m, door thresholds and openings 1.4–1.6 m"),
            (VegZone.Grove, "≤ 1.3 × crown radius of a garden tree"),
            (VegZone.Lawn, "everything else walkable"),
        };

        public sealed class VegMasks
        {
            public Color32[] A, B;
            public List<(Vector2 at, float r, string kind, string id)> Clear = new List<(Vector2, float, string, string)>();
            /// <summary>Plaza cells a slate joint crosses (VegSize²), for the summary.</summary>
            public int JointCells, PlazaCells;
        }

        private static Vector2 VegCentre(int i, int j) => new Vector2(-VegHalf + (i + 0.5f) * VegCell, -VegHalf + (j + 0.5f) * VegCell);

        private static void VegRange(Vector2 min, Vector2 max, out int x0, out int y0, out int x1, out int y1)
        {
            int s = VegSize;
            x0 = Mathf.Clamp(Mathf.FloorToInt((min.x + VegHalf) / VegCell), 0, s - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((min.y + VegHalf) / VegCell), 0, s - 1);
            x1 = Mathf.Clamp(Mathf.CeilToInt((max.x + VegHalf) / VegCell), 0, s - 1);
            y1 = Mathf.Clamp(Mathf.CeilToInt((max.y + VegHalf) / VegCell), 0, s - 1);
        }

        private static void VegSegment(bool[] grid, Vector2 a, Vector2 b, float half)
        {
            BoundsOf(new[] { a, b }, half + VegCell, out Vector2 min, out Vector2 max);
            VegRange(min, max, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (SegmentDistance(VegCentre(x, y), a, b, out _, out _) <= half) grid[y * VegSize + x] = true;
        }

        private static void VegRect(bool[] grid, Vector4 r, float margin)
        {
            VegRange(new Vector2(r.x - margin, r.y - margin), new Vector2(r.z + margin, r.w + margin), out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (RectDistance(r, VegCentre(x, y)) <= margin) grid[y * VegSize + x] = true;
        }

        /// <summary>
        /// The contract's two masks from the builder's own data (deterministic; ±55 m, 4 px/m, binary channels — no soft edges, the
        /// runtime fades from the edges). mask_a: R path / paving surface (the ground's own irregular path edge), G exclusion (interiors
        /// and under roofs + 0.6 m, the hall plinth, kairō floors + 0.5 m, decks, kerbs, the drip strips, the yard's beaten earth), B
        /// water, A corridor (walkable and ≥ 1.5 m from a known obstacle: 255; the near-obstacle edge and the unwalked bamboo band: 64).
        /// mask_b: R the Z6 walls' and fences' footprints in decimetres, G the zone — the layout plan's vegZone (overrides, then zones) when
        /// <see cref="Inputs.Layout"/> is set, else <see cref="VegZoneTable"/> — and 13 over the plaza; B 0 (×1) but on the plaza: the
        /// slate joint lines <see cref="JointDensity"/>, the faces <see cref="FaceDensity"/>; A 0 (×1). Clearances → veg_clear.json (the
        /// layout's clear[] when set). <paramref name="joints"/>: <see cref="PlazaJointCells"/> (null: computed here).
        /// </summary>
        public static VegMasks BuildVegetationMasks(Inputs inputs, bool[] joints = null)
        {
            joints ??= PlazaJointCells();
            int s = VegSize, n = s * s;
            var path = new bool[n];
            var exclusion = new bool[n];
            var water = new bool[n];
            var obstacle = new bool[n];
            var wallFoot = new byte[n];

            // Path / paving surface.
            foreach (Road r in inputs.Roads)
            {
                float half = r.Type == RoadType.Stepstone ? r.Width * 0.5f * 0.6f : r.Width * 0.5f;
                BoundsOf(r.Points, half + 0.5f, out Vector2 min, out Vector2 max);
                VegRange(min, max, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = VegCentre(x, y);
                    float d = PolylineDistance(p, r.Points, out _) + (r.Paved ? 0f : EdgeNoise(p));
                    if (d <= half) path[y * s + x] = true;
                }
            }

            foreach (Vector4 r in WorldV5Ground.PlazaRects) VegRect(path, r, 0f);

            // Exclusion: interiors / under roofs, the plinth, kairō floors, decks, kerbs, drip strips, beaten earth.
            foreach (Vector4 b in inputs.Buildings) VegRect(exclusion, b, 0.6f);
            VegRect(exclusion, new Vector4(-19.5f, 36.5f, 19.5f, 50f), 0.9f);   // the hall: plinth + eaves
            foreach (Vector4 r in inputs.Paved)
            {
                bool karesansui = Mathf.Approximately(r.x, 12f) && Mathf.Approximately(r.y, -30f);
                // Plaza paving stays Path, but must not itself exclude its zone-13 joint tufts.
                // Exact source-rectangle match only: kerbs, decks, stairs and overlapping physical exclusions stay blocked.
                bool plazaPaving = Array.IndexOf(WorldV5Ground.PlazaRects, r) >= 0;
                if (!karesansui && !plazaPaving) VegRect(exclusion, r, 0f);   // the rock garden is Z12 (moss collars round its rocks)
            }

            foreach ((Vector2[] line, float half) in inputs.Corridors)
                for (int k = 0; k + 1 < line.Length; k++) VegSegment(exclusion, line[k], line[k + 1], half + 0.5f);
            foreach (Vector4 yard in inputs.Yards) VegRect(exclusion, new Vector4(yard.x + 1.5f, yard.y + 1.5f, yard.z - 1.5f, yard.w - 1.5f), 0f);
            VegLayout layout = inputs.Layout;
            if (layout != null)
            {
                // The content pass's buildings (floors, interiors) and the layout's bare / interior zones (vegZone 13 outside the plaza).
                foreach ((string id, Vector4 rect, bool isNew, List<Vector2> doors) in layout.Buildings) VegRect(exclusion, rect, 0.6f);
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Vector2 p = VegCentre(x, y);
                    if (!InPlaza(p) && LayoutZone(layout, p) == (byte)VegZone.PlazaJoints) exclusion[y * s + x] = true;
                }
            }

            // Water.
            foreach (Pond pond in inputs.Ponds)
            {
                float reach = Mathf.Max(pond.Semi.x, pond.Semi.y) + VegCell;
                VegRange(pond.Centre - Vector2.one * reach, pond.Centre + Vector2.one * reach, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (pond.Distance(VegCentre(x, y)) <= 0f) water[y * s + x] = true;
            }

            // Obstacles for the corridor (the builder's solid set: walls, fences, buildings, plinth, trunks, the bamboo band).
            foreach (Segment w in inputs.Walls) VegSegment(obstacle, w.A, w.B, w.HalfThickness + VegCell * 0.52f);   // the ring's footprint is just outside ±55
            foreach (Vector4 b in inputs.Buildings) VegRect(obstacle, b, 0f);
            foreach (Tree t in inputs.Trees) VegSegment(obstacle, t.Position, t.Position, t.Trunk);
            VegRect(obstacle, new Vector4(-19.5f, 36.5f, 19.5f, 50f), 0f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                if (Unwalked(VegCentre(x, y)) > 0.5f) obstacle[y * s + x] = true;
            float[] toObstacle = DistanceField(obstacle, s, VegCell);
            float[] toWater = DistanceField(water, s, VegCell);

            // Z6 seeds: the footprints of the ring tsuiji, the forecourt walls, the yard and R7 fences, the rock garden's low walls (dm).
            // A cell counts when it overlaps the footprint (half a cell of slack): the ring's 55.0–55.6 footprint marks the last row.
            foreach (Segment w in inputs.Walls)
            {
                if (!w.Foot) continue;
                byte dm = (byte)Mathf.Clamp(Mathf.RoundToInt(w.Height * 10f), 1, 255);
                float half = w.HalfThickness + VegCell * 0.52f;
                BoundsOf(new[] { w.A, w.B }, half + VegCell, out Vector2 min, out Vector2 max);
                VegRange(min, max, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (SegmentDistance(VegCentre(x, y), w.A, w.B, out _, out _) <= half) wallFoot[y * s + x] = (byte)Mathf.Max(wallFoot[y * s + x], dm);
            }

            var masks = new VegMasks { A = new Color32[n], B = new Color32[n] };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int i = y * s + x;
                Vector2 p = VegCentre(x, y);
                bool walkable = !obstacle[i] && !water[i];
                byte corridor = path[i] || exclusion[i] || water[i] ? (byte)0 : walkable && toObstacle[i] >= CorridorClearance ? VegCorridor : VegNotCorridor;
                masks.A[i] = new Color32(path[i] ? (byte)255 : (byte)0, exclusion[i] ? (byte)255 : (byte)0, water[i] ? (byte)255 : (byte)0, corridor);
                if (InPlaza(p))
                {
                    // Z01 derz: the paving stays a path surface in mask_a; the tufts live on the joint lines only.
                    masks.PlazaCells++;
                    if (joints[i]) masks.JointCells++;
                    masks.B[i] = new Color32(wallFoot[i], (byte)VegZone.PlazaJoints, joints[i] ? JointDensity : FaceDensity, 0);
                    continue;
                }

                byte zone = path[i] || water[i] || exclusion[i] ? (byte)VegZone.Auto
                    : layout != null ? LayoutZone(layout, p) : (byte)VegZoneAt(inputs, p, toWater[i], obstacle[i]);
                masks.B[i] = new Color32(wallFoot[i], zone, 0, 0);
            }

            if (layout != null)
            {
                foreach ((Vector2 at, float r, string kind, string id) c in layout.Clear) masks.Clear.Add(c);
                return masks;
            }

            foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates) masks.Clear.Add((g.Position, 3f, "spawn", g.Id));
            foreach (Vector2 w in GreyboxV6Builder.WeaponPositions) masks.Clear.Add((w, 1.5f, "weapon", null));
            foreach (Vector2 w in GreyboxV6Builder.ExtraWeaponPositions) masks.Clear.Add((w, 1.5f, "weapon", null));
            foreach ((Vector2 at, float radius) in inputs.Worn)
            {
                bool gate = false;
                foreach (GreyboxV6Builder.Gate g in GreyboxV6Builder.Gates) if (Vector2.Distance(g.Position, at) < 0.01f) { gate = true; break; }
                if (!gate) masks.Clear.Add((at, 1.5f, "door", null));
            }

            return masks;
        }

        /// <summary>The plaza fight area (Ap005SurfaceTags.V6PlazaRects, the runtime's own plaza test).</summary>
        public static bool InPlaza(Vector2 p)
        {
            foreach (Vector4 r in DD.Art.Ap005SurfaceTags.V6PlazaRects) if (p.x > r.x && p.x < r.z && p.y > r.y && p.y < r.w) return true;
            return false;
        }

        /// <summary>The layout plan's vegZone at <paramref name="p"/>: the first override (polygon, or the ring rule |x| or |z| ≥ n), then the first zone; 0 outside all.</summary>
        public static byte LayoutZone(VegLayout layout, Vector2 p)
        {
            foreach (List<(string id, byte zone, Vector2[] polygon, float ringMin)> list in new[] { layout.Overrides, layout.Zones })
                foreach ((string id, byte zone, Vector2[] polygon, float ringMin) z in list)
                {
                    bool hit = z.polygon != null ? InPolygon(p, z.polygon) : Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) >= z.ringMin;
                    if (hit) return z.zone;
                }

            return 0;
        }

        /// <summary>
        /// Which 0.25 m mask cells a slate joint crosses on the plaza: the plaza mesh's own layout (WorldV5Ground.TilesMesh — 2 m cells,
        /// odd rows shifted a half cell, a variant and a quarter turn per cell from CellPick) over each variant's Mask R (AO: the grout
        /// lines), the minimum of R over the cell's patch of the tile below <see cref="JointLevel"/>.
        /// </summary>
        public static bool[] PlazaJointCells()
        {
            int s = VegSize;
            var joints = new bool[s * s];
            const int Coarse = 128;   // 1024² tile → 8 px (1.6 cm) blocks, the minimum R of each
            int variants = WorldV5Ground.SlateSets.Length;
            var blocks = new byte[variants][];
            for (int v = 0; v < variants; v++)
            {
                string path = ArtPipelinePaths.SourceTexture(WorldV5Ground.SlateSets[v], "Mask");
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (!tex.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("[GROUND] slate mask unreadable: " + path);
                Color32[] px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                UnityEngine.Object.DestroyImmediate(tex);
                var b = new byte[Coarse * Coarse];
                for (int i = 0; i < b.Length; i++) b[i] = 255;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = (y * Coarse / h) * Coarse + x * Coarse / w;
                    if (px[y * w + x].r < b[k]) b[k] = px[y * w + x].r;
                }

                blocks[v] = b;
            }

            float cell = WorldV5Ground.Cell;
            foreach (Vector4 r in WorldV5Ground.PlazaRects)
                for (int cz = Mathf.FloorToInt(r.y / cell); cz * cell < r.w; cz++)
                for (int cx = Mathf.FloorToInt((r.x - WorldV5Ground.RowShift(cz)) / cell); cx * cell + WorldV5Ground.RowShift(cz) < r.z; cx++)
                {
                    float left = cx * cell + WorldV5Ground.RowShift(cz);
                    float x0 = Mathf.Max(r.x, left), x1 = Mathf.Min(r.z, left + cell), z0 = Mathf.Max(r.y, cz * cell), z1 = Mathf.Min(r.w, (cz + 1) * cell);
                    if (x1 - x0 < 1e-3f || z1 - z0 < 1e-3f) continue;
                    (int variant, int quarter) = WorldV5Ground.CellPick(cx, cz, variants);
                    byte[] b = blocks[variant];
                    VegRange(new Vector2(x0, z0), new Vector2(x1, z1), out int i0, out int j0, out int i1, out int j1);
                    for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        // A 16 cm patch round the mask cell's centre (where S-07's tuft stands) ∩ the plaza cell, in the tile's UV (a quarter
                        // turn maps a rect to a rect): a joint must pass within 8 cm of the centre, so the lines stay lines at 4 px/m.
                        Vector2 centre = VegCentre(i, j);
                        float ax = Mathf.Max(x0, centre.x - JointReach), bx = Mathf.Min(x1, centre.x + JointReach);
                        float az = Mathf.Max(z0, centre.y - JointReach), bz = Mathf.Min(z1, centre.y + JointReach);
                        if (bx - ax < 1e-4f || bz - az < 1e-4f) continue;
                        Vector2 u0 = WorldV5Ground.Turn(new Vector2((ax - left) / cell, az / cell - cz), quarter);
                        Vector2 u1 = WorldV5Ground.Turn(new Vector2((bx - left) / cell, bz / cell - cz), quarter);
                        int bu0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(u0.x, u1.x) * Coarse), 0, Coarse - 1), bu1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(u0.x, u1.x) * Coarse) - 1, 0, Coarse - 1);
                        int bv0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(u0.y, u1.y) * Coarse), 0, Coarse - 1), bv1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(u0.y, u1.y) * Coarse) - 1, 0, Coarse - 1);
                        byte min = 255;
                        for (int bv = bv0; bv <= bv1; bv++)
                        for (int bu = bu0; bu <= bu1; bu++)
                            if (b[bv * Coarse + bu] < min) min = b[bv * Coarse + bu];
                        if (min < JointLevel) joints[j * s + i] = true;
                    }
                }

            return joints;
        }

        private static bool InYard(Inputs inputs, Vector2 p)
        {
            foreach (Vector4 yard in inputs.Yards) if (RectDistance(yard, p) < 0f) return true;
            return false;
        }

        /// <summary><see cref="VegZoneTable"/> as code (first match wins).</summary>
        public static VegZone VegZoneAt(Inputs inputs, Vector2 p, float toWater, bool obstacle)
        {
            foreach (Vector4 yard in inputs.Yards)
            {
                float d = RectDistance(yard, p);
                if (d < 0f) return VegZone.Court;   // the beaten-earth core is a mask_a.G exclusion (zone 0)
            }

            if (p.x > 12f && p.x < 28f && p.y > -30f && p.y < -16f) return VegZone.Karesansui;
            foreach (Vector4 g in inputs.MossGardens) if (RectDistance(g, p) < 0f) return VegZone.MossCourt;
            if (toWater <= 2f) return VegZone.PondBank;
            foreach (GreyboxV6Builder.Crossing c in GreyboxV6Builder.Crossings)
                if (Vector2.Distance(p, new Vector2(c.Centre.x, c.Min.y)) < 4f || Vector2.Distance(p, new Vector2(c.Centre.x, c.Max.y)) < 4f) return VegZone.BridgeApproach;
            foreach (Vector4 r in WorldV5Ground.PlazaRects) if (RectDistance(r, p) < 4f) return VegZone.PlazaEdge;
            foreach ((Vector2[] line, float half) in inputs.Corridors) if (PolylineDistance(p, line, out _) - half < 1.2f) return VegZone.Court;
            foreach (Vector4 b in inputs.Buildings) if (RectDistance(b, p) < 1.5f) return VegZone.Building;
            if (p.x < -37.5f && p.x > -54f && Mathf.Abs(p.y) < 30f && obstacle) return VegZone.Bamboo;
            foreach ((Vector2 at, float radius) in inputs.Worn) if (Vector2.Distance(p, at) < radius) return VegZone.Wear;
            foreach (Tree t in inputs.Trees) if (Vector2.Distance(p, t.Position) < 1.3f * t.Canopy) return VegZone.Grove;
            return VegZone.Lawn;
        }

        /// <summary>Writes veg_mask_a / veg_mask_b (contract import: Read/Write, linear, uncompressed, no mips, Point, Clamp), masks.json and veg_clear.json.</summary>
        public static void WriteVegetationMasks(Inputs inputs, VegMasks masks)
        {
            EnsureFolder(VegFolder);
            int s = VegSize;
            foreach ((string path, Color32[] px) in new[] { (VegMaskAPath, masks.A), (VegMaskBPath, masks.B) })
            {
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false, true);
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = Mathf.NextPowerOfTwo(s);
                importer.npotScale = TextureImporterNPOTScale.None;
                ClearPlatformOverrides(importer);
                importer.SaveAndReimport();
            }

            File.WriteAllText(VegJsonPath, VegJson(inputs));
            var clear = new StringBuilder("{\"points\":[");
            for (int i = 0; i < masks.Clear.Count; i++)
            {
                clear.Append(i == 0 ? "\n  " : ",\n  ").Append("{\"x\":").Append(Fm(masks.Clear[i].at.x)).Append(",\"z\":").Append(Fm(masks.Clear[i].at.y)).Append(",\"r\":").Append(Fm(masks.Clear[i].r)).Append(",\"kind\":\"").Append(masks.Clear[i].kind).Append("\"");
                if (masks.Clear[i].id != null) clear.Append(",\"id\":\"").Append(masks.Clear[i].id).Append("\"");
                clear.Append("}");
            }

            clear.Append("\n]}\n");
            File.WriteAllText(VegClearPath, clear.ToString());
            AssetDatabase.ImportAsset(VegJsonPath);
            AssetDatabase.ImportAsset(VegClearPath);
        }

        private static string Fm(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static string VegJson(Inputs inputs)
        {
            return "{\"schema\":\"" + VegSchema + "\",\"version\":\"" + VegVersionNow + "\",\"scene\":\"" + GreyboxV6Builder.SceneName + "\",\n" +
                   " \"minX\":" + Fm(-VegHalf) + ",\"minZ\":" + Fm(-VegHalf) + ",\"maxX\":" + Fm(VegHalf) + ",\"maxZ\":" + Fm(VegHalf) + ",\"width\":" + VegSize + ",\"height\":" + VegSize + ",\n" +
                   " \"source\":\"S-05 WP-092 zemin ajanı, " + VegDate + ", WorldV5Terrain.BuildVegetationMasks / ExportMasksCli (roads: " + inputs.RoadSource + "; zones: " +
                   (inputs.Layout != null ? "layout-data.json " + inputs.Layout.Version + " zones[].vegZone + vegZoneOverrides[]; clear: layout clear[]" : "interim VegZoneTable") + ")\",\n" +
                   " \"zoneNotes\":{\"13\":\"Z01 derz: plaza joints only (Ap005SurfaceTags.V6PlazaRects; mask_a.R stays 255 = paving), density channel (mask_b.B) = joint lines 255, stone faces 1. Bare earth / interiors elsewhere (the layout's other vegZone 13) are mask_a.G exclusions with zone 0.\"}}\n";
        }

        /// <summary>Exact Euclidean distance (metres) to the nearest set cell (Felzenszwalb–Huttenlocher, two 1D passes); 0 on set cells.</summary>
        public static float[] DistanceField(bool[] set, int size, float texel)
        {
            const float Far = 1e12f;   // > any squared texel distance, exact in double
            var g = new float[size * size];
            for (int i = 0; i < g.Length; i++) g[i] = set[i] ? 0f : Far;
            var f = new float[size];
            var d = new float[size];
            var v = new int[size];
            var z = new double[size + 1];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++) f[y] = g[y * size + x];
                Edt1D(f, d, v, z, size);
                for (int y = 0; y < size; y++) g[y * size + x] = d[y];
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) f[x] = g[y * size + x];
                Edt1D(f, d, v, z, size);
                for (int x = 0; x < size; x++) g[y * size + x] = Mathf.Sqrt(d[x]) * texel;
            }

            return g;
        }

        private static void Edt1D(float[] f, float[] d, int[] v, double[] z, int n)
        {
            int k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                double s = Intersect(f, v[k], q);
                while (s <= z[k])
                {
                    k--;
                    s = Intersect(f, v[k], q);
                }

                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int r = v[k];
                d[q] = (float)((double)(q - r) * (q - r) + f[r]);
            }
        }

        private static double Intersect(float[] f, int r, int q) => ((f[q] + (double)q * q) - (f[r] + (double)r * r)) / (2.0 * q - 2.0 * r);

        // ---------------------------------------------------------------- batch

        /// <summary>
        /// WP-092 B5 · the before / after switch for the evidence run: DD_GROUND_LEGACY=1 (or <see cref="BeforeAfterCli"/>'s first
        /// half) keeps the WP-075 ground (DD/GroundMoss on the box, 256² path mask) and the plaza slate's uncapped sun specular.
        /// </summary>
        public static bool Legacy => s_legacy || Environment.GetEnvironmentVariable("DD_GROUND_LEGACY") == "1";
        private static bool s_legacy;

        /// <summary>
        /// The plaza slate's (DD/GroundTiles) sun highlight controls — the sun-disc smoothness floor, the highlight knee and the planar
        /// clamp: the shader defaults, or none of them for the legacy frames (cap 1, knee 10⁴ ≈ off, planar 64).
        /// </summary>
        public static void SetPlazaSpecularCap(bool legacy)
        {
            foreach (string set in WorldV5Ground.SlateSets)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(GreyboxV6Builder.DataFolder + "/Materials/V5_Ground_" + set + ".mat");
                if (m == null || !m.HasProperty("_DirectSmoothnessCap")) continue;
                foreach ((string property, float off) in new[] { ("_DirectSmoothnessCap", 1f), ("_DirectSpecMax", 1e4f), ("_PlanarMax", 64f) })
                    if (m.HasProperty(property)) m.SetFloat(property, legacy ? off : m.shader.GetPropertyDefaultFloatValue(m.shader.FindPropertyIndex(property)));
                EditorUtility.SetDirty(m);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The bake's sun set-up without the bake (WorldV5Lighting.Bake's first step: the baked-sun light at the V6 angle, ConfigureSun,
        /// the flare, RenderSettings.sun, the environment), saved into the scene so WorldTdCapture lights the frames like the game. The
        /// one lightmap bake stays the lead's, at the end of WP-092.
        /// </summary>
        public static void PrepareCaptureSun()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            DD.Prototype.World.WorldLayoutKind previous = DD.Prototype.World.WorldLayout.Current;
            DD.Prototype.World.WorldLayout.SetCurrent(DD.Prototype.World.WorldLayoutKind.V6);
            try
            {
                var bootstrap = UnityEngine.Object.FindObjectOfType<Ap005WorldBootstrap>();
                if (bootstrap == null) throw new InvalidOperationException("[GROUND] V6 scene has no Ap005WorldBootstrap");
                Light sun = DD.Prototype.World.WorldAtmosphere.FindBakedSun();
                if (sun == null)
                {
                    sun = new GameObject(DD.Prototype.World.WorldAtmosphere.BakedSunName).AddComponent<Light>();
                    sun.transform.SetParent(bootstrap.transform, false);
                }

                sun.transform.rotation = Quaternion.Euler(DD.Prototype.World.WorldAtmosphere.SunEulerFor(DD.Prototype.World.WorldLayoutKind.V6));
                DD.Prototype.World.WorldAtmosphere.ConfigureSun(sun);
                sun.lightmapBakeType = LightmapBakeType.Mixed;
                sun.bounceIntensity = 1f;
                WorldV5SunFlare.Attach(sun);
                RenderSettings.sun = sun;
                DD.Prototype.World.WorldAtmosphere.ApplyEnvironment();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                DD.Prototype.World.WorldLayout.SetCurrent(previous);
            }
        }

        /// <summary>Batch: the V6 rebuild (no lightmap bake), the capture sun and the WorldTdCapture set into DD_TD_OUT, in one editor session.</summary>
        public static void BuildAndCaptureCli()
        {
            GreyboxV6Builder.BuildCli();
            SetPlazaSpecularCap(Legacy);
            PrepareCaptureSun();
            WorldTdCapture.RunAll();
            Debug.Log("[GROUND] build + capture done");
        }

        /// <summary>Batch (look iteration): the V6 rebuild with this ground, the capture sun, the top map and the eight TD poses into DD_TD_OUT.</summary>
        public static void CaptureCli()
        {
            GreyboxV6Builder.BuildCli();
            SetPlazaSpecularCap(false);
            PrepareCaptureSun();
            WorldTdCapture.RenderTopMap();
            WorldTdCapture.RenderPoses();
            Debug.Log("[GROUND] capture done");
        }

        /// <summary>
        /// Batch: DD_TD_OUT/before (legacy ground, uncapped plaza) and DD_TD_OUT/after (this ground), each rebuilt and captured the same
        /// way — top map, the eight TD poses with bare-ground.json, the close looks (near/), and after/ground-budget.json; then the S-07
        /// hand-off when -vegOut / DD_VEGMASK_OUT is set (<see cref="ExportMasksCli"/>).
        /// </summary>
        public static void BeforeAfterCli()
        {
            string root = Environment.GetEnvironmentVariable("DD_TD_OUT");
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("[GROUND] set DD_TD_OUT");
            ImportPlanInputs();   // the final roads / layout plan first: the "after" rebuild reads their repo copies
            try
            {
                foreach (bool legacy in new[] { true, false })
                {
                    s_legacy = legacy;
                    GreyboxV6Builder.BuildCli();
                    SetPlazaSpecularCap(legacy);
                    PrepareCaptureSun();
                    Environment.SetEnvironmentVariable("DD_TD_OUT", Path.Combine(root, legacy ? "before" : "after"));
                    WorldTdCapture.RenderTopMap();
                    WorldTdCapture.RenderPoses();
                    foreach ((string name, Vector2 feet) in StudySpots) Near(Path.Combine(root, legacy ? "before" : "after", "near"), name, feet);
                    if (!legacy) WriteBudget();
                }
            }
            finally
            {
                s_legacy = false;
                Environment.SetEnvironmentVariable("DD_TD_OUT", root);
            }

            if (!string.IsNullOrEmpty(Arg("-vegOut") ?? Environment.GetEnvironmentVariable("DD_VEGMASK_OUT"))) ExportMasksCli();
            Debug.Log("[GROUND] before / after done");
        }

        // ---------------------------------------------------------------- budget (TD view, no render)

        /// <summary>
        /// The ground's cost in the eight TD views (the rig's frustum, the scene as built): visible terrain / stone chunks (= draw calls,
        /// one SRP Batcher batch per material), their vertices and triangles, and the totals → DD_TD_OUT/ground-budget.json.
        /// </summary>
        public static void WriteBudget()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Transform dressRoot = GameObject.Find(WorldV5Dress.RootName).transform;
            var groups = new[] { (RootName, dressRoot.Find(RootName)), (WorldV5GroundStones.RootName, dressRoot.Find(WorldV5GroundStones.RootName)) };
            var go = new GameObject("GROUND budget camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = WorldTdCapture.RigFov;
            cam.aspect = (float)WorldTdCapture.Width / WorldTdCapture.Height;
            var sb = new StringBuilder("{");
            try
            {
                foreach ((string group, Transform root) in groups)
                {
                    int chunks = 0, verts = 0, tris = 0;
                    if (root != null)
                        foreach (Transform c in root)
                        {
                            Mesh m = c.GetComponent<MeshFilter>().sharedMesh;
                            chunks++;
                            verts += m.vertexCount;
                            tris += (int)(m.GetIndexCount(0) / 3);
                        }

                    sb.Append("\n \"").Append(group).Append("\": {\"chunks\": ").Append(chunks).Append(", \"vertices\": ").Append(verts).Append(", \"triangles\": ").Append(tris).Append(", \"poses\": {");
                    bool first = true;
                    foreach ((string name, Vector2 feet) in WorldTdCapture.Poses)
                    {
                        var focus = new Vector3(feet.x, WorldTdCapture.FocusHeight, feet.y);
                        cam.transform.SetPositionAndRotation(focus + WorldTdCapture.RigOffset, Quaternion.LookRotation(-WorldTdCapture.RigOffset.normalized, Vector3.up));
                        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
                        int vc = 0, vv = 0, vt = 0;
                        if (root != null)
                            foreach (Transform c in root)
                            {
                                var r = c.GetComponent<MeshRenderer>();
                                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                                Mesh m = c.GetComponent<MeshFilter>().sharedMesh;
                                vc++;
                                vv += m.vertexCount;
                                vt += (int)(m.GetIndexCount(0) / 3);
                            }

                        sb.Append(first ? "" : ", ").Append("\"").Append(name).Append("\": {\"drawCalls\": ").Append(vc).Append(", \"vertices\": ").Append(vv).Append(", \"triangles\": ").Append(vt).Append("}");
                        first = false;
                    }

                    sb.Append("}},");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            sb.Length--;
            sb.Append("\n}\n");
            string dir = Environment.GetEnvironmentVariable("DD_TD_OUT");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("[GROUND] set DD_TD_OUT");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "ground-budget.json"), sb.ToString());
            Debug.Log("[GROUND] budget -> " + Path.Combine(dir, "ground-budget.json"));
        }

        // ---------------------------------------------------------------- S-07 hand-off CLI (re-runnable)

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>
        /// Batch, re-runnable — the S-07 hand-off from the layout plan:
        /// <list type="number">
        /// <item><see cref="ImportPlanInputs"/>: <c>-roadsJson &lt;path&gt;</c> (env DD_ROADS_JSON; K/wp-092/layout/roads.json, wp092.roads.v1)
        /// → the runtime copy <see cref="RoadsJsonPath"/>; <c>-layoutJson &lt;path&gt;</c> (env DD_LAYOUT_JSON; K/wp-092/layout/layout-data.json,
        /// wp092.layout.v1) → <see cref="LayoutCopyPath"/>. Without them the copies already in the repo are used.</item>
        /// <item>The contract masks from the V6 scene's ponds and those copies into <see cref="VegFolder"/> (import per MASK-CONTRACT
        /// §4.2): zones from the plan's vegZone, the plaza as zone 13 "Z01 derz" with its joint lines, clear points from its clear[].</item>
        /// <item><c>-vegOut &lt;dir&gt;</c> (or env DD_VEGMASK_OUT; K/wp-092/ground/veg-masks/v001): the K copy — the four files,
        /// preview_overlay.png (the masks over the TD orthographic top view of the scene as built) and SHA256SUMS.</item>
        /// </list>
        /// The ground itself (T_V6_GroundMask*, the chunks, the stones) follows the same runtime copy at the next V6 rebuild.
        /// </summary>
        public static void ExportMasksCli()
        {
            string outDir = Arg("-vegOut") ?? Environment.GetEnvironmentVariable("DD_VEGMASK_OUT");
            ImportPlanInputs();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            GameObject greybox = GameObject.Find(GreyboxV6Builder.GreyboxRootName);
            if (greybox == null) throw new InvalidOperationException("[GROUND] V6 scene has no " + GreyboxV6Builder.GreyboxRootName);
            if (!File.Exists(RoadsJsonPath)) throw new InvalidOperationException("[GROUND] no road network: pass -roadsJson K/wp-092/layout/roads.json (or DD_ROADS_JSON)");
            Inputs inputs = LayoutInputs(PondsOf(greybox.transform), File.ReadAllText(RoadsJsonPath), File.Exists(LayoutCopyPath) ? File.ReadAllText(LayoutCopyPath) : null);
            VegMasks masks = BuildVegetationMasks(inputs);
            WriteVegetationMasks(inputs, masks);
            Debug.Log("[GROUND] vegetation masks " + VegSize + "² (roads: " + inputs.RoadSource + ", " + inputs.Roads.Count + "; zones: " + (inputs.Layout != null ? "layout " + inputs.Layout.Version : "interim") +
                      "; plaza joints " + masks.JointCells + "/" + masks.PlazaCells + " cells; clear " + masks.Clear.Count + ") -> " + VegFolder);
            if (!string.IsNullOrEmpty(outDir)) WriteKCopy(outDir, masks);
        }

        /// <summary>
        /// The plan's files into the repo copies the ground and the masks read: -roadsJson / DD_ROADS_JSON → <see cref="RoadsJsonPath"/>
        /// (<see cref="WriteRoadsRuntimeCopy"/>), -layoutJson / DD_LAYOUT_JSON → <see cref="LayoutCopyPath"/> (its zones, overrides, clear
        /// points and buildings). Either is validated first; a malformed file throws and leaves its copy as it was.
        /// </summary>
        public static void ImportPlanInputs()
        {
            string roadsPath = Arg("-roadsJson") ?? Environment.GetEnvironmentVariable("DD_ROADS_JSON");
            string layoutPath = Arg("-layoutJson") ?? Environment.GetEnvironmentVariable("DD_LAYOUT_JSON");
            WriteRoadsRuntimeCopy(roadsPath);
            if (!string.IsNullOrEmpty(layoutPath))
            {
                VegLayout layout = ParseLayout(File.ReadAllText(layoutPath));   // throws on a malformed plan: the copy is left as it was
                EnsureFolder(Folder);
                File.WriteAllText(LayoutCopyPath, LayoutCopyJson(layout, Path.GetFileName(layoutPath) + " " + layout.Version));
                AssetDatabase.ImportAsset(LayoutCopyPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[GROUND] layout copy " + LayoutCopyPath + " <- " + layoutPath + " (" + layout.Zones.Count + " zones, " + layout.Overrides.Count + " overrides, " + layout.Clear.Count + " clear, " + layout.Buildings.Count + " buildings)");
            }
        }

        /// <summary>The runtime roads copy: the given layout roads.json, validated and copied verbatim (none given: the copy is kept as it is).</summary>
        public static void WriteRoadsRuntimeCopy(string roadsPath)
        {
            if (string.IsNullOrEmpty(roadsPath)) return;
            string json = File.ReadAllText(roadsPath);
            ParseRoads(json, new List<Road>(), new List<Zone>());   // throws on a malformed file: the runtime copy is left as it was
            EnsureFolder(Path.GetDirectoryName(RoadsJsonPath).Replace('\\', '/'));
            File.WriteAllText(RoadsJsonPath, json);
            AssetDatabase.ImportAsset(RoadsJsonPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[GROUND] roads runtime copy " + RoadsJsonPath + " <- " + roadsPath);
        }

        private static void WriteKCopy(string outDir, VegMasks masks)
        {
            Directory.CreateDirectory(outDir);
            string[] files = { "veg_mask_a.png", "veg_mask_b.png", "masks.json", "veg_clear.json" };
            foreach (string f in files) File.Copy(Path.Combine(VegFolder, f), Path.Combine(outDir, f), true);

            // The preview: the TD orthographic top view (WorldTdCapture's plan, ±90 m) cropped to ±55 m, 12 px/m, the masks over it.
            string tmp = Path.Combine(outDir, ".preview-topmap");
            string previous = Environment.GetEnvironmentVariable("DD_TD_OUT");
            Environment.SetEnvironmentVariable("DD_TD_OUT", tmp);
            try { WorldTdCapture.RenderTopMap(); }
            finally { Environment.SetEnvironmentVariable("DD_TD_OUT", previous); }
            var top = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            top.LoadImage(File.ReadAllBytes(Path.Combine(tmp, "topmap.png")));
            const int ppm = 12;
            int side = Mathf.RoundToInt(2f * VegHalf * ppm), s = VegSize;
            var px = new Color32[side * side];
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                float wx = -VegHalf + (x + 0.5f) / ppm, wz = -VegHalf + (y + 0.5f) / ppm;
                Color c = top.GetPixelBilinear((wx + WorldTdCapture.MapHalf) / (2f * WorldTdCapture.MapHalf), (wz + WorldTdCapture.MapHalf) / (2f * WorldTdCapture.MapHalf)) * 0.55f;
                int i = Mathf.Clamp(Mathf.FloorToInt((wz + VegHalf) * VegPixelsPerMeter), 0, s - 1) * s + Mathf.Clamp(Mathf.FloorToInt((wx + VegHalf) * VegPixelsPerMeter), 0, s - 1);
                Color32 a = masks.A[i], b = masks.B[i];
                if (a.a >= 128) c = Color.Lerp(c, new Color(0.25f, 0.85f, 0.35f), 0.22f);        // corridor
                else if (a.a > 0) c = Color.Lerp(c, new Color(0.95f, 0.85f, 0.2f), 0.18f);       // walkable, not corridor
                if (a.r >= 128) c = Color.Lerp(c, new Color(1f, 0.58f, 0.15f), 0.6f);            // path / paving surface
                if (a.g >= 128) c = Color.Lerp(c, new Color(0.9f, 0.15f, 0.15f), 0.5f);          // exclusion
                if (a.b >= 128) c = Color.Lerp(c, new Color(0.2f, 0.45f, 1f), 0.65f);            // water
                if (b.r > 0) c = Color.Lerp(c, Color.white, 0.85f);                              // Z6 wall-foot seed
                if (b.g == (byte)VegZone.PlazaJoints && b.b == JointDensity) c = Color.Lerp(c, new Color(0.2f, 0.95f, 0.9f), 0.7f);   // plaza joint lines
                int ix = Mathf.FloorToInt((wx + VegHalf) * VegPixelsPerMeter), iz = Mathf.FloorToInt((wz + VegHalf) * VegPixelsPerMeter);
                bool zoneEdge = (ix + 1 < s && masks.B[i + 1].g != b.g) || (iz + 1 < s && masks.B[i + s].g != b.g);
                if (zoneEdge) c = Color.Lerp(c, new Color(0.1f, 0.1f, 0.1f), 0.7f);              // zone (mask_b.G) boundaries
                c.a = 1f;
                px[y * side + x] = c;
            }

            UnityEngine.Object.DestroyImmediate(top);
            var preview = new Texture2D(side, side, TextureFormat.RGBA32, false);
            preview.SetPixels32(px);
            preview.Apply(false);
            File.WriteAllBytes(Path.Combine(outDir, "preview_overlay.png"), preview.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(preview);
            Directory.Delete(tmp, true);

            var sums = new StringBuilder();
            using (var sha = System.Security.Cryptography.SHA256.Create())
                foreach (string f in new[] { "veg_mask_a.png", "veg_mask_b.png", "masks.json", "veg_clear.json", "preview_overlay.png" })
                {
                    byte[] hash = sha.ComputeHash(File.ReadAllBytes(Path.Combine(outDir, f)));
                    sums.Append(BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()).Append("  ").Append(f).Append('\n');
                }

            File.WriteAllText(Path.Combine(outDir, "SHA256SUMS"), sums.ToString());
            Debug.Log("[GROUND] K copy -> " + outDir);
        }

        // ---------------------------------------------------------------- look study (batch, evidence only)

        /// <summary>Close looks at the ground (feet XZ, camera height): the TD rig's angle at a third of its distance.</summary>
        public static readonly (string name, Vector2 feet)[] StudySpots =
        {
            ("near-garden-path", new Vector2(31f, -12f)), ("near-lane-bamboo", new Vector2(-44f, -6f)), ("near-sw-lawn", new Vector2(-36f, -42f)),
            ("near-r7-moss", new Vector2(33f, -3f)), ("near-yard", new Vector2(-40f, 36f)), ("near-pond-bank", new Vector2(-6f, -36f)),
        };

        /// <summary>
        /// Batch: one V6 rebuild, the capture sun, then DD_TD_OUT/&lt;variant&gt;/ for each look variant (the eight TD poses; the layer /
        /// albedo debug views on two poses) and DD_TD_OUT/near/ (<see cref="StudySpots"/>, the rig angle at 1/3 distance). Variants set
        /// V6_GroundTerrain's properties in memory only (the saved material keeps the shader defaults). Then the S-07 hand-off
        /// (<see cref="ExportMasksCli"/>; -vegOut / DD_VEGMASK_OUT).
        /// </summary>
        public static void StudyCli()
        {
            string root = Environment.GetEnvironmentVariable("DD_TD_OUT");
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("[GROUND] set DD_TD_OUT");
            GreyboxV6Builder.BuildCli();
            SetPlazaSpecularCap(false);
            PrepareCaptureSun();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            try
            {
                Environment.SetEnvironmentVariable("DD_TD_OUT", Path.Combine(root, "after"));
                WorldTdCapture.RenderTopMap();
                WorldTdCapture.RenderPoses();
                foreach ((string name, Vector2 feet) in StudySpots) Near(Path.Combine(root, "near"), name, feet);
                EditorUtility.SetDirty(material);   // kept in memory across the captures' scene opens (reset to 0 below, never saved otherwise)
                material.SetFloat("_DebugView", 1f);
                foreach ((string name, Vector2 feet) in StudySpots) Near(Path.Combine(root, "near-layers"), name, feet);
                material.SetFloat("_DebugView", 2f);
                foreach ((string name, Vector2 feet) in StudySpots) Near(Path.Combine(root, "near-albedo"), name, feet);
                material.SetFloat("_DebugView", 0f);
            }
            finally
            {
                material.SetFloat("_DebugView", 0f);
                Environment.SetEnvironmentVariable("DD_TD_OUT", root);
            }

            ExportMasksCli();
            Debug.Log("[GROUND] study done");
        }

        private static void Near(string dir, string name, Vector2 feet)
        {
            Directory.CreateDirectory(dir);
            string previous = Environment.GetEnvironmentVariable("DD_TD_OUT");
            Environment.SetEnvironmentVariable("DD_TD_OUT", dir);
            try { WorldTdCapture.RenderAt(name, feet, WorldTdCapture.RigOffset / 3f); }
            finally { Environment.SetEnvironmentVariable("DD_TD_OUT", previous); }
        }
    }
}
