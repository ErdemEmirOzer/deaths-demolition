using System;
using System.Collections.Generic;
using DD.Prototype.Feedback;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>What the probe reads besides physics. The world hook-up (DD.Art VegetationWorldHookup) fills it for V6.</summary>
    public sealed class VegetationProbeOptions
    {
        public Vector2 Min = new Vector2(-55f, -55f);
        public Vector2 Max = new Vector2(55f, 55f);
        /// <summary>Raycast layers: Default + Environment + CameraOccluder by default (characters, pickups, ragdolls ignored).</summary>
        public int LayerMask = -1;
        /// <summary>Colliders that ARE the ground (V6: the greybox "Ground" box). Name rules in the recipe are the fallback.</summary>
        public Collider[] GroundColliders;
        /// <summary>World rects (x0, z0, x1, z1): paved plaza (V6: Ap005SurfaceTags.V6PlazaRects).</summary>
        public Vector4[] PlazaRects;
        /// <summary>Ground path mask (V6: V5_GroundMoss _PathMask, R = gravel core) and its bounds (x0, z0, sizeX, sizeZ).</summary>
        public Texture2D PathMask;
        public Vector4 PathBounds;
        public float PathMaskThreshold = 0.5f;
        /// <summary>S-05 roads.json (wp092.roads.v1) — replaces the path mask when present.</summary>
        public List<VegetationRoad> Roads;
        /// <summary>Pond surfaces: unit-cylinder transforms (PondN_Water, local XZ radius 0.5).</summary>
        public Transform[] WaterSurfaces;
        /// <summary>
        /// Collider → kit family (AP-023 SM_&lt;family&gt;_Vnn). Null = the nearest SurfaceTag.Family up the parents. The V6 hook-up
        /// passes a name parse so the runtime (tags applied) and the edit-mode preview (no tags) classify identically.
        /// </summary>
        public Func<Transform, string> FamilyOf;
        /// <summary>The world scene whose NavRegion markers are read (volumes are built from the markers, not the registry, so
        /// edit mode — no OnEnable — reads the same rooms). Invalid = every loaded marker.</summary>
        public UnityEngine.SceneManagement.Scene Scene;
        public bool UseNavRegions = true;
        public bool UseMarkers = true;
        /// <summary>Spawn / weapon positions for the clear radii; null = WorldMarkers.Current (built only for a run, not the menu backdrop).</summary>
        public Vector3[] SpawnPoints;
        public Vector3[] WeaponPoints;
        /// <summary>Tests: skip physics; every cell is flat ground at <see cref="FlatGroundY"/>.</summary>
        public bool FlatGround;
        public float FlatGroundY;
    }

    /// <summary>One road polyline (roads.json).</summary>
    public sealed class VegetationRoad
    {
        public string Id = "";
        public string Type = "gravel";
        public float Width = 2.4f;
        public Vector2[] Points = Array.Empty<Vector2>();

        public float HalfWidth => Width * 0.5f * (Type == "stepstone" ? 0.6f : 1f);
    }

    /// <summary>
    /// WP-093 · The fallback mask builder's physics half (MASK-CONTRACT §5): a top-down ray per grid cell classifies what
    /// stands over the ground (wall, building, rock, kerb, roof, bamboo, tree, shrub, water) from SurfaceTag families,
    /// greybox names and materials (tables in <see cref="VegetationRecipe.ObstacleTable"/>), records the ground height,
    /// then stamps plaza rects, paths (roads.json or the ground path mask), pond ellipses, NavRegion rooms / gates and the
    /// spawn / weapon clear points. Runs once at world load (allocates then); ~194 k rays at 0.25 m over ±55 m.
    /// </summary>
    public static class VegetationWorldProbe
    {
        public static VegetationGrid Probe(VegetationProbeOptions options, VegetationRecipe recipe)
        {
            if (options == null) options = new VegetationProbeOptions();
            if (recipe == null) recipe = VegetationRecipe.Load();
            var grid = new VegetationGrid(options.Min, options.Max, recipe.probeCell);
            grid.DefaultGroundY = options.FlatGroundY;

            if (options.FlatGround)
            {
                for (int i = 0; i < grid.Count; i++)
                {
                    grid.Set(i, VegetationGrid.Flag.Ground);
                    grid.GroundY[i] = options.FlatGroundY;
                }
            }
            else
            {
                Raycast(grid, options, recipe);
            }

            StampPlaza(grid, options.PlazaRects);
            if (options.Roads != null && options.Roads.Count > 0) StampRoads(grid, options.Roads);
            else StampPathMask(grid, options);
            StampWater(grid, options.WaterSurfaces);
            if (options.UseNavRegions) StampNavRegions(grid, recipe, options.Scene);
            if (options.UseMarkers) AddMarkerClearPoints(grid, recipe, options);
            return grid;
        }

        // ------------------------------------------------------------------ physics

        private static void Raycast(VegetationGrid grid, VegetationProbeOptions options, VegetationRecipe recipe)
        {
            Physics.SyncTransforms();
            var hits = new RaycastHit[24];
            var classCache = new Dictionary<int, VegetationObstacle>(1024);
            var treeSeen = new HashSet<int>();
            var ground = new HashSet<int>();
            if (options.GroundColliders != null)
            {
                for (int i = 0; i < options.GroundColliders.Length; i++)
                {
                    if (options.GroundColliders[i] != null) ground.Add(options.GroundColliders[i].GetInstanceID());
                }
            }

            int mask = options.LayerMask == -1 ? DefaultMask() : options.LayerMask;
            float top = recipe.probeTop;
            for (int z = 0; z < grid.Height; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, z);
                    Vector2 c = grid.CellCenter(x, z);
                    int n = Physics.RaycastNonAlloc(new Vector3(c.x, top, c.y), Vector3.down, hits, top + 30f, mask, QueryTriggerInteraction.Ignore);
                    SortByDistance(hits, n);
                    VegetationObstacle obstacle = VegetationObstacle.None;
                    float obstacleTop = float.NegativeInfinity;
                    bool found = false;
                    for (int h = 0; h < n; h++)
                    {
                        Collider col = hits[h].collider;
                        if (col == null) continue;
                        int id = col.GetInstanceID();
                        if (!classCache.TryGetValue(id, out VegetationObstacle cls))
                        {
                            cls = ground.Contains(id) ? VegetationObstacle.Ground : Classify(col, recipe, options.FamilyOf);
                            classCache[id] = cls;
                        }

                        switch (cls)
                        {
                            case VegetationObstacle.Ground:
                                grid.GroundY[i] = hits[h].point.y;
                                grid.Set(i, VegetationGrid.Flag.Ground);
                                found = true;
                                break;
                            case VegetationObstacle.Roof:
                                grid.Set(i, VegetationGrid.Flag.Roofed);
                                break;
                            case VegetationObstacle.Water:
                                grid.Set(i, VegetationGrid.Flag.Water);
                                break;
                            case VegetationObstacle.Bamboo:
                                grid.Set(i, VegetationGrid.Flag.Bamboo);
                                break;
                            case VegetationObstacle.Shrub:
                                grid.Set(i, VegetationGrid.Flag.Shrub);
                                break;
                            case VegetationObstacle.Tree:
                                AddTree(grid, col, recipe, treeSeen, options.FamilyOf);
                                break;
                            case VegetationObstacle.Ignore:
                                break;
                            default:
                                if (Priority(cls) > Priority(obstacle)) obstacle = cls;
                                obstacleTop = Mathf.Max(obstacleTop, hits[h].point.y);
                                break;
                        }

                        if (found) break;
                    }

                    if (!found)
                    {
                        grid.GroundY[i] = float.NaN;
                    }

                    grid.Obstacle[i] = (byte)obstacle;
                    grid.ObstacleHeight[i] = obstacle != VegetationObstacle.None && found ? Mathf.Max(0f, obstacleTop - grid.GroundY[i]) : 0f;
                }
            }
        }

        public static int DefaultMask()
        {
            int env = LayerMask.NameToLayer(DD.Prototype.Core.PrototypeLayers.EnvironmentName);
            int occ = LayerMask.NameToLayer(DD.Prototype.Core.PrototypeLayers.CameraOccluderName);
            int zone = LayerMask.NameToLayer(DD.Prototype.Core.PrototypeLayers.WorldZoneName);   // WP-103: the grove's blocks (zone only, no collision)
            int m = 1;
            if (env >= 0) m |= 1 << env;
            if (occ >= 0) m |= 1 << occ;
            if (zone >= 0) m |= 1 << zone;
            return m;
        }

        private static void SortByDistance(RaycastHit[] hits, int n)
        {
            for (int a = 1; a < n; a++)
            {
                RaycastHit key = hits[a];
                int b = a - 1;
                while (b >= 0 && hits[b].distance > key.distance)
                {
                    hits[b + 1] = hits[b];
                    b--;
                }

                hits[b + 1] = key;
            }
        }

        private static int Priority(VegetationObstacle o)
        {
            switch (o)
            {
                case VegetationObstacle.Wall: return 5;
                case VegetationObstacle.Building: return 4;
                case VegetationObstacle.Rock: return 3;
                case VegetationObstacle.Small: return 2;
                case VegetationObstacle.Kerb: return 1;
                default: return 0;
            }
        }

        /// <summary>The kit family of a collider: <paramref name="familyOf"/>, else the nearest SurfaceTag.Family.</summary>
        public static string FamilyOf(Collider col, Func<Transform, string> familyOf, out Transform familyRoot)
        {
            familyRoot = null;
            if (familyOf != null)
            {
                for (Transform t = col.transform; t != null; t = t.parent)
                {
                    string f = familyOf(t);
                    if (!string.IsNullOrEmpty(f)) { familyRoot = t; return f; }
                }

                return null;
            }

            SurfaceTag tag = col.GetComponentInParent<SurfaceTag>();
            if (tag == null) return null;
            familyRoot = tag.transform;
            return tag.Family;
        }

        /// <summary>
        /// Collider → obstacle class: the kit family table, then greybox names / Greybox_ materials; unknown = Small (solid).
        /// Deliberately no SurfaceTag.Surface fallback: the tags exist at runtime only, and the class must not depend on them.
        /// </summary>
        public static VegetationObstacle Classify(Collider col, VegetationRecipe recipe, Func<Transform, string> familyOf = null)
        {
            VegetationRecipe.ObstacleTable t = recipe.obstacles;
            string family = FamilyOf(col, familyOf, out _);
            if (!string.IsNullOrEmpty(family))
            {
                if (Contains(t.wall, family)) return VegetationObstacle.Wall;
                if (Contains(t.building, family)) return VegetationObstacle.Building;
                if (Contains(t.rock, family)) return VegetationObstacle.Rock;
                if (Contains(t.kerb, family)) return VegetationObstacle.Kerb;
                if (Contains(t.small, family)) return VegetationObstacle.Small;
                if (Contains(t.tree, family)) return VegetationObstacle.Tree;
                if (Contains(t.bamboo, family)) return VegetationObstacle.Bamboo;
                if (Contains(t.shrub, family)) return VegetationObstacle.Shrub;
            }

            string name = col.name;
            if (Exact(t.groundNames, name)) return VegetationObstacle.Ground;
            if (Prefix(t.roofNames, name)) return VegetationObstacle.Roof;
            if (Prefix(t.bambooNames, name)) return VegetationObstacle.Bamboo;
            if (Prefix(t.treeNames, name)) return VegetationObstacle.Tree;
            if (Prefix(t.wallNames, name)) return VegetationObstacle.Wall;
            if (Prefix(t.kerbNames, name)) return VegetationObstacle.Kerb;
            if (Prefix(t.buildingNames, name)) return VegetationObstacle.Building;

            var renderer = col.GetComponent<Renderer>();
            string mat = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;
            if (mat.StartsWith("Greybox_", StringComparison.Ordinal))
            {
                switch (mat.Substring(8))
                {
                    case "ground": return VegetationObstacle.Ground;
                    case "wall": return VegetationObstacle.Wall;
                    case "roof": return VegetationObstacle.Roof;
                    case "floor": return VegetationObstacle.Building;
                    case "tree": return VegetationObstacle.Tree;
                    case "water": return VegetationObstacle.Water;
                    case "prop": return VegetationObstacle.Small;
                }
            }

            return VegetationObstacle.Small;   // unknown and solid: conservative, nothing grows through it
        }

        private static void AddTree(VegetationGrid grid, Collider col, VegetationRecipe recipe, HashSet<int> seen, Func<Transform, string> familyOf)
        {
            string family = FamilyOf(col, familyOf, out Transform familyRoot) ?? string.Empty;
            Transform root = familyRoot != null ? familyRoot : col.transform;
            if (!seen.Add(root.GetInstanceID())) return;
            bool tagged = familyRoot != null;
            VegetationRecipe.ObstacleTable t = recipe.obstacles;
            int grove = IndexOf(t.groveFamilies, family);
            bool litter = Contains(t.litterFamilies, family);
            Vector3 p = tagged ? root.position : col.bounds.center;
            float radius = grove >= 0 && t.groveCrownRadius != null && grove < t.groveCrownRadius.Length ? t.groveCrownRadius[grove] : 3f;
            grid.Trees.Add(new VegetationGrid.Tree { Position = new Vector2(p.x, p.z), Radius = radius, Grove = grove >= 0, Litter = litter });
        }

        private static bool Contains(string[] list, string value) => IndexOf(list, value) >= 0;

        private static int IndexOf(string[] list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return -1;
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        private static bool Exact(string[] list, string value) => IndexOf(list, value) >= 0;

        private static bool Prefix(string[] list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < list.Length; i++)
            {
                if (!string.IsNullOrEmpty(list[i]) && value.StartsWith(list[i], StringComparison.Ordinal)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ stamps

        public static void StampPlaza(VegetationGrid grid, Vector4[] rects)
        {
            if (rects == null) return;
            for (int r = 0; r < rects.Length; r++)
            {
                Vector4 q = rects[r];
                ForCellsInRect(grid, q.x, q.y, q.z, q.w, i => grid.Set(i, VegetationGrid.Flag.Plaza));
            }
        }

        public static void StampRoads(VegetationGrid grid, List<VegetationRoad> roads)
        {
            for (int r = 0; r < roads.Count; r++)
            {
                VegetationRoad road = roads[r];
                if (road == null || road.Points == null) continue;
                float hw = road.HalfWidth;
                for (int s = 0; s + 1 < road.Points.Length; s++)
                {
                    Vector2 a = road.Points[s], b = road.Points[s + 1];
                    float x0 = Mathf.Min(a.x, b.x) - hw, x1 = Mathf.Max(a.x, b.x) + hw;
                    float z0 = Mathf.Min(a.y, b.y) - hw, z1 = Mathf.Max(a.y, b.y) + hw;
                    ForCellsInRect(grid, x0, z0, x1, z1, i =>
                    {
                        if (DistanceToSegment(grid.CellCenter(i), a, b) <= hw) grid.Set(i, VegetationGrid.Flag.Path);
                    });
                }
            }
        }

        private static void StampPathMask(VegetationGrid grid, VegetationProbeOptions o)
        {
            Texture2D mask = o.PathMask;
            if (mask == null) return;
            if (!mask.isReadable)
            {
                Debug.LogWarning("[Vegetation] path mask '" + mask.name + "' is not readable; paths come from physics only.");
                return;
            }

            Vector4 b = o.PathBounds;
            if (b.z <= 0f || b.w <= 0f) return;
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 c = grid.CellCenter(i);
                float u = (c.x - b.x) / b.z, v = (c.y - b.y) / b.w;
                if (u < 0f || v < 0f || u > 1f || v > 1f) continue;
                if (mask.GetPixelBilinear(u, v).r >= o.PathMaskThreshold) grid.Set(i, VegetationGrid.Flag.Path);
            }
        }

        /// <summary>Pond surfaces: a scaled unit cylinder (radius 0.5 in local XZ), yawed — the greybox PondN_Water.</summary>
        public static void StampWater(VegetationGrid grid, Transform[] surfaces)
        {
            if (surfaces == null) return;
            for (int s = 0; s < surfaces.Length; s++)
            {
                Transform t = surfaces[s];
                if (t == null) continue;
                Vector3 e = t.lossyScale * 0.5f;
                float reach = Mathf.Max(Mathf.Abs(e.x), Mathf.Abs(e.z));
                Vector3 p = t.position;
                ForCellsInRect(grid, p.x - reach, p.z - reach, p.x + reach, p.z + reach, i =>
                {
                    Vector2 c = grid.CellCenter(i);
                    Vector3 local = t.InverseTransformPoint(new Vector3(c.x, p.y, c.y));
                    if (local.x * local.x + local.z * local.z <= 0.25f) grid.Set(i, VegetationGrid.Flag.Water);
                });
            }
        }

        private static void StampNavRegions(VegetationGrid grid, VegetationRecipe recipe, UnityEngine.SceneManagement.Scene scene)
        {
            var markers = new List<NavRegionMarker>(64);
            if (scene.IsValid() && scene.isLoaded)
            {
                var buffer = new List<NavRegionMarker>(64);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(false, buffer);
                    markers.AddRange(buffer);
                }
            }
            else
            {
                markers.AddRange(UnityEngine.Object.FindObjectsOfType<NavRegionMarker>());
            }

            for (int m = 0; m < markers.Count; m++)
            {
                if (markers[m] == null || !markers[m].isActiveAndEnabled) continue;
                NavRegionVolume v = markers[m].CreateVolume();   // from the marker's data: the same in edit mode (no OnEnable) and play
                if (v.kind == NavRegionKind.Room)
                {
                    bool court = Contains(recipe.regions.mossCourtRooms, v.id);
                    float hx = v.size.x * 0.5f, hz = v.size.z * 0.5f;
                    float x0 = v.center.x - hx, x1 = v.center.x + hx, z0 = v.center.z - hz, z1 = v.center.z + hz;
                    if (v.polygonXZ != null && v.polygonXZ.Length >= 3)
                    {
                        x0 = z0 = float.MaxValue;
                        x1 = z1 = float.MinValue;
                        for (int k = 0; k < v.polygonXZ.Length; k++)
                        {
                            x0 = Mathf.Min(x0, v.polygonXZ[k].x); x1 = Mathf.Max(x1, v.polygonXZ[k].x);
                            z0 = Mathf.Min(z0, v.polygonXZ[k].y); z1 = Mathf.Max(z1, v.polygonXZ[k].y);
                        }
                    }

                    ForCellsInRect(grid, x0, z0, x1, z1, i =>
                    {
                        Vector2 c = grid.CellCenter(i);
                        if (!v.Contains(new Vector3(c.x, 0f, c.y))) return;
                        grid.Set(i, court ? VegetationGrid.Flag.MossCourt : VegetationGrid.Flag.Excluded);
                    });
                }
                else if (v.kind == NavRegionKind.Gate)
                {
                    grid.ClearPoints.Add(new VegetationGrid.ClearPoint
                    {
                        Position = new Vector2(v.center.x, v.center.z),
                        Radius = recipe.transitions.doorClear + Mathf.Max(v.size.x, v.size.z) * 0.5f,
                        Kind = VegetationGrid.ClearKind.Door
                    });
                }
            }
        }

        private static void AddMarkerClearPoints(VegetationGrid grid, VegetationRecipe recipe, VegetationProbeOptions options)
        {
            WorldMarkers.MarkerSet set = WorldMarkers.Current;
            Vector3[] spawns = options.SpawnPoints ?? set.SpawnPositions;
            Vector3[] weapons = options.WeaponPoints ?? set.WeaponPositions;
            if (spawns != null)
            {
                for (int i = 0; i < spawns.Length; i++)
                {
                    Vector3 p = spawns[i];
                    grid.ClearPoints.Add(new VegetationGrid.ClearPoint { Position = new Vector2(p.x, p.z), Radius = recipe.transitions.spawnClear, Kind = VegetationGrid.ClearKind.Spawn });
                }
            }

            if (weapons != null)
            {
                for (int i = 0; i < weapons.Length; i++)
                {
                    Vector3 p = weapons[i];
                    grid.ClearPoints.Add(new VegetationGrid.ClearPoint { Position = new Vector2(p.x, p.z), Radius = recipe.transitions.weaponClear, Kind = VegetationGrid.ClearKind.Weapon });
                }
            }
        }

        public static void ForCellsInRect(VegetationGrid grid, float x0, float z0, float x1, float z1, Action<int> act)
        {
            int ax = Mathf.Max(0, Mathf.FloorToInt((x0 - grid.Origin.x) / grid.Cell));
            int az = Mathf.Max(0, Mathf.FloorToInt((z0 - grid.Origin.y) / grid.Cell));
            int bx = Mathf.Min(grid.Width - 1, Mathf.FloorToInt((x1 - grid.Origin.x) / grid.Cell));
            int bz = Mathf.Min(grid.Height - 1, Mathf.FloorToInt((z1 - grid.Origin.y) / grid.Cell));
            for (int z = az; z <= bz; z++)
            {
                for (int x = ax; x <= bx; x++)
                {
                    Vector2 c = grid.CellCenter(x, z);
                    if (c.x < x0 || c.x > x1 || c.y < z0 || c.y > z1) continue;
                    act(grid.Index(x, z));
                }
            }
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }
    }
}
