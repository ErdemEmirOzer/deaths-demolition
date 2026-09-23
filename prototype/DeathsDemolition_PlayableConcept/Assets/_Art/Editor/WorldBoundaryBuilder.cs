using System;
using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-038M · The playable-area boundary of World_AP005: the five region polygons of the placement form exactly the ±45 m square
    /// (WorldLayout.MapSize), every spawn / weapon point / marker lies inside ±42.5, and the NavMesh bake volume stops at ±48. The
    /// perimeter walls leave gates open onto the outer-band plates (45–80 m, scenery, no NavMesh) — the S-10 soak (seed 61) walked
    /// out through the south gate and off the outer band's edge at z −80. This builds a closed ring of four invisible box walls whose
    /// inner faces sit on the ±45 m line, on the built-in Ignore Raycast layer: the Player and Enemy bodies collide with it (physics
    /// matrix), while every gameplay ray (bullets, aim, camera, VFX) uses explicit Environment / CameraOccluder / EnemyHitbox masks and
    /// passes through; the NavMesh bake (Environment | CameraOccluder) never collects it. The same object carries WorldFallGuard.
    /// Pond rims: the koi basins are holes in the ground plates (no collider under the water) behind rim stones lower than the
    /// player's 0.35 m step — a player stepped in and fell forever. Each basin ellipse (the NavMesh pond footprint,
    /// PlacementImporter.BasinHoles) gets a closed fence of 0.5 m invisible segments on the same layer, except where a raised
    /// walkable crossing (bridge deck / shore module: NavMesh above <see cref="PondCrossingY"/>) passes over the rim. Where the
    /// plates' real basin cut reaches past the ellipse (and any other hole cell of the 0.25 m grid outside the ponds) an invisible
    /// floor plug at the neighbouring ground height closes it.
    /// Idempotent: everything is rebuilt in place. Called by PlacementImporter.BuildScene (after the bake) and by <see cref="ApplyCli"/>.
    /// </summary>
    public static class WorldBoundaryBuilder
    {
        public const string ObjectName = "World Boundary (WP-038M)";
        public const float HalfExtent = WorldLayout.MapSize * 0.5f;   // 45 m: the region polygons' union
        public const float WallThickness = 1f;
        public const float WallBottom = -2f;
        public const float WallTop = 10f;
        public const int Layer = 2;   // Ignore Raycast (built-in)
        public const string PondRimsName = "Pond Rims";
        public const float PondRimBottom = -1f, PondRimTop = 1.2f, PondRimThickness = 0.3f, PondSegment = 0.5f;
        /// <summary>NavMesh this high over a rim segment is a crossing (bridge deck ≥ 0.25 m, shore module): no fence there.</summary>
        public const float PondCrossingY = 0.2f;
        public const string PlugsName = "Ground Plugs";
        public const float PlugCell = 0.25f, PlugDepth = 0.5f;

        public static GameObject Build(Transform root, List<(Vector2 centre, Vector2 semi, float yaw)> basins)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            Transform old = root.Find(ObjectName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var go = new GameObject(ObjectName);
            go.transform.SetParent(root, false);
            go.layer = Layer;
            float span = 2f * (HalfExtent + WallThickness);
            float height = WallTop - WallBottom;
            float mid = (WallTop + WallBottom) * 0.5f;
            float off = HalfExtent + WallThickness * 0.5f;
            AddWall(go, "North", new Vector3(0f, mid, off), new Vector3(span, height, WallThickness));
            AddWall(go, "South", new Vector3(0f, mid, -off), new Vector3(span, height, WallThickness));
            AddWall(go, "East", new Vector3(off, mid, 0f), new Vector3(WallThickness, height, span));
            AddWall(go, "West", new Vector3(-off, mid, 0f), new Vector3(WallThickness, height, span));
            BuildPondRims(go, basins, out int segments, out int crossings);
            int plugs = BuildGroundPlugs(go, basins, out int plugCells);

            NavMeshModifier modifier = go.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = true;   // never part of the bake, whatever the surface's layer mask becomes
            WorldFallGuard guard = go.AddComponent<WorldFallGuard>();
            guard.Configure(HalfExtent - 1f, 12f, new Vector3(0f, 0.1f, -3f));
            Debug.Log("[WorldBoundary] ring ±" + HalfExtent + " m + " + basins.Count + " pond rims (" + segments + " segments, " + crossings + " crossing gaps) + " + plugs + " ground plugs (" + plugCells + " cells)");
            return go;
        }

        /// <summary>Ellipse point of a basin in world XZ (inverse of PlacementImporter.InsideHole's frame change).</summary>
        public static Vector2 PondPoint((Vector2 centre, Vector2 semi, float yaw) b, float t)
        {
            float lx = b.semi.x * Mathf.Cos(t), lz = b.semi.y * Mathf.Sin(t);
            float a = b.yaw * Mathf.Deg2Rad;
            return b.centre + new Vector2(lx * Mathf.Cos(a) - lz * Mathf.Sin(a), lx * Mathf.Sin(a) + lz * Mathf.Cos(a));
        }

        /// <summary>Segments per basin: 0.5 m chords (Ramanujan perimeter).</summary>
        public static int PondSegments((Vector2 centre, Vector2 semi, float yaw) b)
        {
            float a = b.semi.x, c = b.semi.y;
            float perimeter = Mathf.PI * (3f * (a + c) - Mathf.Sqrt((3f * a + c) * (a + 3f * c)));
            return Mathf.Max(12, Mathf.CeilToInt(perimeter / PondSegment));
        }

        /// <summary>True when a raised walkable crossing (NavMesh above <see cref="PondCrossingY"/>) passes over this rim point.</summary>
        public static bool IsCrossing(Vector2 p)
        {
            int ground = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            if (!Physics.Raycast(new Vector3(p.x, 3f, p.y), Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) return false;
            if (hit.point.y <= PondCrossingY) return false;
            return NavMesh.SamplePosition(hit.point, out NavMeshHit nav, 0.35f, NavMesh.AllAreas) && nav.position.y > PondCrossingY;
        }

        /// <summary>True inside any basin ellipse (the pond footprint behind the rim fence).</summary>
        public static bool InsidePond(List<(Vector2 centre, Vector2 semi, float yaw)> basins, float x, float z)
        {
            foreach ((Vector2 centre, Vector2 semi, float yaw) b in basins)
            {
                float a = b.yaw * Mathf.Deg2Rad, dx = x - b.centre.x, dz = z - b.centre.y;
                float lx = dx * Mathf.Cos(a) + dz * Mathf.Sin(a), lz = -dx * Mathf.Sin(a) + dz * Mathf.Cos(a);
                if ((lx * lx) / (b.semi.x * b.semi.x) + (lz * lz) / (b.semi.y * b.semi.y) <= 1f) return true;
            }

            return false;
        }

        /// <summary>
        /// Hole cells (no Environment / CameraOccluder surface under a 0.25 m cell centre) inside the square and outside every pond:
        /// merged into z-runs of equal height and plugged with an invisible floor whose top is the highest ground among the cell's
        /// neighbours 0.5 m away (0 when none) — the surface simply continues. Returns the plug count.
        /// </summary>
        private static int BuildGroundPlugs(GameObject parent, List<(Vector2 centre, Vector2 semi, float yaw)> basins, out int cells)
        {
            cells = 0;
            var root = new GameObject(PlugsName);
            root.transform.SetParent(parent.transform, false);
            root.layer = Layer;
            int ground = (1 << PrototypeLayers.Environment) | (1 << PrototypeLayers.CameraOccluder);
            int n = Mathf.RoundToInt(2f * HalfExtent / PlugCell);
            int plugs = 0;
            for (int ix = 0; ix < n; ix++)
            {
                float x = -HalfExtent + (ix + 0.5f) * PlugCell;
                int runStart = -1;
                float runTop = 0f;
                for (int iz = 0; iz <= n; iz++)
                {
                    float z = -HalfExtent + (iz + 0.5f) * PlugCell;
                    bool hole = iz < n && !InsidePond(basins, x, z) && !Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, 8f, ground, QueryTriggerInteraction.Ignore);
                    float top = hole ? NeighbourGround(x, z, ground) : 0f;
                    if (runStart >= 0 && (!hole || Mathf.Abs(top - runTop) > 0.01f))
                    {
                        float z0 = -HalfExtent + runStart * PlugCell, z1 = -HalfExtent + iz * PlugCell;
                        var plug = new GameObject("Plug_" + ix + "_" + runStart);
                        plug.transform.SetParent(root.transform, false);
                        plug.layer = Layer;
                        BoxCollider box = plug.AddComponent<BoxCollider>();
                        box.center = new Vector3(x, runTop - PlugDepth * 0.5f, (z0 + z1) * 0.5f);
                        box.size = new Vector3(PlugCell + 0.02f, PlugDepth, z1 - z0 + 0.02f);
                        plugs++;
                        runStart = -1;
                    }

                    if (hole)
                    {
                        cells++;
                        if (runStart < 0) { runStart = iz; runTop = top; }
                    }
                }
            }

            return plugs;
        }

        private static float NeighbourGround(float x, float z, int ground)
        {
            float best = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f;
                var from = new Vector3(x + 0.5f * Mathf.Cos(a), 3f, z + 0.5f * Mathf.Sin(a));
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 8f, ground, QueryTriggerInteraction.Ignore) && hit.point.y < 0.5f) best = Mathf.Max(best, hit.point.y);
            }

            return best == float.MinValue ? 0f : best;
        }

        private static void BuildPondRims(GameObject parent, List<(Vector2 centre, Vector2 semi, float yaw)> basins, out int segments, out int crossings)
        {
            segments = 0;
            crossings = 0;
            var rims = new GameObject(PondRimsName);
            rims.transform.SetParent(parent.transform, false);
            rims.layer = Layer;
            Physics.SyncTransforms();
            float height = PondRimTop - PondRimBottom, mid = (PondRimTop + PondRimBottom) * 0.5f;
            for (int b = 0; b < basins.Count; b++)
            {
                int n = PondSegments(basins[b]);
                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = PondPoint(basins[b], 2f * Mathf.PI * i / n), p1 = PondPoint(basins[b], 2f * Mathf.PI * (i + 1) / n);
                    Vector2 m = (p0 + p1) * 0.5f;
                    if (IsCrossing(m)) { crossings++; continue; }
                    Vector2 d = p1 - p0;
                    var seg = new GameObject("Pond" + b + "_" + i);
                    seg.transform.SetParent(rims.transform, false);
                    seg.layer = Layer;
                    seg.transform.SetPositionAndRotation(new Vector3(m.x, mid, m.y), Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)));
                    BoxCollider box = seg.AddComponent<BoxCollider>();
                    box.size = new Vector3(PondRimThickness, height, d.magnitude + 0.1f);   // 5 cm overlap each end: no seam at the chord joints
                    segments++;
                }
            }
        }

        private static void AddWall(GameObject parent, string name, Vector3 centre, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(parent.transform, false);
            wall.layer = Layer;
            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.isTrigger = false;
        }

        /// <summary>Batch entry: add / rebuild the ring and the fall guard in World_AP005 without a re-import (no renderer → no rebake).</summary>
        public static void ApplyCli()
        {
            var scene = EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            GameObject root = GameObject.Find(Ap005WorldBootstrap.RootName);
            if (root == null) throw new InvalidOperationException("[WorldBoundary] world root missing");
            var placement = AssetDatabase.LoadAssetAtPath<WorldPlacementAsset>(ArtLibraryPaths.Ap005PlacementAsset);
            if (placement == null) throw new InvalidOperationException("[WorldBoundary] placement asset missing: " + ArtLibraryPaths.Ap005PlacementAsset);
            Build(root.transform, PlacementImporter.BasinHoles(placement));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[WorldBoundary] layer " + LayerMask.LayerToName(Layer) + " + WorldFallGuard written to " + ArtLibraryPaths.Ap005WorldScene);
        }
    }
}
