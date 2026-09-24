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
    /// PlacementImporter.BasinHoles) gets a closed fence of 0.5 m invisible segments on the same layer, except where walkable
    /// NavMesh passes through the fence (bridge deck, shore module — WP-038N rule: an invisible fence never cuts the NavMesh, see
    /// <see cref="CutsNavMesh"/>) or a bridge rail skirt already closes the pond (WP-038Q). Where the
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
        /// <summary>WP-038N: NavMesh this close to a fence segment's centre line (half the thickness + 0.15 m) is walked through — no fence.</summary>
        public const float NavMeshClearance = PondRimThickness * 0.5f + 0.15f;
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

        public const string RingNavRootName = "Ring NavMesh (WP-038R)";
        /// <summary>WP-038R: the walkable NavMesh stops this far inside the ring's inner face (body radius + skin + 2 cm).</summary>
        public const float RingNavInset = 0.5f, RingNavDepth = 100f;
        /// <summary>
        /// WP-038R: the volumes span only the ground band (the bridge rail volumes' bottom up to 3 m). Taller ones (±20 m) moved the
        /// bake's vertical voxel origin and the 0.35 m east-gate stone pile (WP-038N) rounded under the 0.30 m climb again.
        /// </summary>
        public const float RingNavBottom = -0.6f, RingNavTop = 3f;

        /// <summary>
        /// WP-038R · The ring is physics only (not in the bake), so the NavMesh ran on through it to the plates' edge (±48 m): an
        /// agent planned through the wall and pressed against its inner face (S-10 stability soak seed 26: the last bodies held
        /// at x −44 for 120 s). Four NotWalkable volumes from <see cref="RingNavInset"/> inside the inner face outward — a
        /// NotWalkable volume does not erode, so the walkable edge is exactly there and a body on it clears the wall. Build
        /// before the bake (PlacementImporter); idempotent.
        /// </summary>
        public static int BuildRingNavVolumes(Transform root)
        {
            Transform old = root.Find(RingNavRootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject(RingNavRootName) { layer = PrototypeLayers.Environment };
            parent.transform.SetParent(root, false);
            float inner = HalfExtent - RingNavInset, span = 2f * (HalfExtent + RingNavDepth), mid = inner + RingNavDepth * 0.5f;
            float y = (RingNavBottom + RingNavTop) * 0.5f, h = RingNavTop - RingNavBottom;
            (string name, Vector3 centre, Vector3 size)[] sides =
            {
                ("North", new Vector3(0f, y, mid), new Vector3(span, h, RingNavDepth)),
                ("South", new Vector3(0f, y, -mid), new Vector3(span, h, RingNavDepth)),
                ("East", new Vector3(mid, y, 0f), new Vector3(RingNavDepth, h, span)),
                ("West", new Vector3(-mid, y, 0f), new Vector3(RingNavDepth, h, span)),
            };
            foreach ((string name, Vector3 centre, Vector3 size) in sides)
            {
                var go = new GameObject("RingNav_" + name) { layer = PrototypeLayers.Environment };   // the surface's mask collects volumes by layer
                go.transform.SetParent(parent.transform, false);
                go.transform.position = centre;
                NavMeshModifierVolume v = go.AddComponent<NavMeshModifierVolume>();
                v.center = Vector3.zero;
                v.size = size;
                v.area = ArtSurfaceImporter.NavAreaNotWalkable;
            }

            return sides.Length;
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

        /// <summary>
        /// WP-038N (C-00 rule: an invisible fence never cuts walkable NavMesh — the fences are not in the bake, so the AI would path
        /// straight into them). True when NavMesh lies within <see cref="NavMeshClearance"/> of the segment p0–p1 at any height the
        /// fence spans (probed at five points × four heights). Ground NavMesh ends ≈ one agent radius (0.5 m) outside the pond
        /// ellipse, so only what crosses the rim — the bridge deck, the shore module — is caught.
        /// </summary>
        public static bool CutsNavMesh(Vector2 p0, Vector2 p1)
        {
            for (int k = 0; k <= 4; k++)
            {
                Vector2 q = Vector2.Lerp(p0, p1, k * 0.25f);
                for (float y = PondRimBottom + 0.5f; y <= PondRimTop; y += 0.5f)
                {
                    if (!NavMesh.SamplePosition(new Vector3(q.x, y, q.y), out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
                    if (hit.position.y < PondRimBottom || hit.position.y > PondRimTop) continue;
                    if (DistanceToSegment(new Vector2(hit.position.x, hit.position.z), p0, p1) <= NavMeshClearance) return true;
                }
            }

            return false;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
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

        /// <summary>WP-038Q · True when the box touches a bridge rail skirt (built before the rims, PlacementImporter.BuildBridgeRailNavVolumes).</summary>
        private static bool OverlapsBridgeSkirt(Vector3 centre, Vector3 halfExtents, Quaternion rotation)
        {
            foreach (Collider c in Physics.OverlapBox(centre, halfExtents, rotation, 1 << Layer, QueryTriggerInteraction.Collide))
                if (c.transform.parent != null && c.transform.parent.name == PlacementImporter.BridgeRailSkirtRootName) return true;
            return false;
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
                    if (CutsNavMesh(p0, p1)) { crossings++; continue; }
                    Vector2 d = p1 - p0;
                    // WP-038Q: where the pond passes under a bridge its rail skirts already close it — a fence segment there only
                    // narrowed the deck (Pond0_48 / 51 left 0.97 m for a 0.96 m body: the soak seed-61 run-25 wedge).
                    if (OverlapsBridgeSkirt(new Vector3(m.x, mid, m.y), new Vector3(PondRimThickness, height, d.magnitude + 0.1f) * 0.5f, Quaternion.LookRotation(new Vector3(d.x, 0f, d.y)))) { crossings++; continue; }
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
