using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-103 (R4 #4, S-05) · The walk scan: "where nothing is in the way, the hero walks". The player's own CharacterController
    /// (PlayerMotor + PrototypeConfig: radius 0.45, height 1.8, step 0.35, slope 45°, skin 0.03) walks the top-down world
    /// (World_V6_Greybox as the top-down runtime loads it, no FPTP scene) on a <see cref="Cell"/> grid, breadth first from the
    /// spawn, in eight directions. Every step it cannot finish is classified by what stopped it:
    /// <list type="bullet">
    /// <item><b>Visible</b>: a collider whose own renderers (LOD / prefab unit) cover the contact point: a real obstacle.</item>
    /// <item><b>Oversize</b>: a collider with renderers that end more than <see cref="VisibleSlack"/> before the contact point.</item>
    /// <item><b>Invisible</b>: a collider with no renderer of its own and no small renderer at the contact point.</item>
    /// <item><b>LowStep</b>: a drawn obstacle or a rise between the step (0.35) and <see cref="LowStepMax"/>: seen, but low enough that
    /// a player reads it as a step (a veranda, a kerb, a planter rim).</item>
    /// <item><b>StepFail</b>: nothing higher than a step is in the way, yet the controller stopped.</item>
    /// <item><b>Ledge / Slope</b>: the ground rises more than <see cref="LowStepMax"/> or is steeper than the slope limit.</item>
    /// <item><b>Boundary / Water</b>: the world boundary's walls and the pond rims (by their root), by design.</item>
    /// </list>
    /// "Seen nothing, still stuck" = Invisible + Oversize + StepFail: the WP-103 acceptance wants it at 0.
    /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.WorldWalkScan.Run -ddOut &lt;dir&gt; [-ddRadius 0.45] [-ddLabel name]</c>
    /// writes <c>walk-scan-&lt;label&gt;.json</c> and a north-up plan <c>walk-scan-&lt;label&gt;.png</c> (4 px per cell).
    /// </summary>
    public static class WorldWalkScan
    {
        public const float Cell = 0.5f;
        public const float DefaultRadius = 0.45f, Height = 1.8f, Step = 0.35f, SlopeLimit = 45f, Skin = 0.03f;
        public const float HalfExtent = 64f;
        public const float VisibleSlack = 0.15f;
        public const float LowStepMax = 0.6f;
        public const int PixelsPerCell = 4;
        /// <summary>A step arrives when it ends within this of its grid point (contact slides along a wall stay under it).</summary>
        public const float ArriveTolerance = 0.1f;
        public static readonly Vector3 Spawn = new Vector3(0f, 0f, -3f);

        public enum Kind { Visible, LowStep, Oversize, Invisible, StepFail, Ledge, Slope, Boundary, Water }

        [Serializable]
        public sealed class Blocker
        {
            public string kind;
            public string unit;
            public string collider;
            public int edges;
            public float x, y, z;
            public float top;   // collider top above the walker's feet (m)
            public string via;  // the drawn renderer that makes it visible (small or large index), else the nearest within 1.5 m
        }

        [Serializable]
        public sealed class Report
        {
            public string scene;
            public float radius, step, cell;
            public int nodes;
            public float reachableM2;
            public int edgesBlocked, visible, lowStep, oversize, invisible, stepFail, ledge, slope, boundary, water;
            public int seenNothingStuck;   // invisible + oversize + stepFail edges
            public List<Blocker> blockers = new List<Blocker>();
            public List<Edge> unexplained = new List<Edge>();   // every StepFail / Invisible / Oversize edge (from, step, end)
        }

        [Serializable]
        public sealed class Edge
        {
            public string kind;
            public float x, y, z, dx, dz, ex, ey, ez;
        }

        private struct Node
        {
            public Vector3 Pos;
            public int Ix, Iz;
        }

        private static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
        };

        public static void Run()
        {
            string outDir = Arg("-ddOut") ?? "Temp/WalkScan";
            float radius = float.TryParse(Arg("-ddRadius"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : DefaultRadius;
            string label = Arg("-ddLabel") ?? ("r" + radius.ToString("0.00", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            string probe = Arg("-ddProbe");
            if (!string.IsNullOrEmpty(probe))
            {
                File.WriteAllText(Path.Combine(outDir, "walk-probe-" + label + ".txt"), ProbePoints(probe), new UTF8Encoding(false));
                return;
            }
            Report report = Scan(radius, Path.Combine(outDir, "walk-scan-" + label + ".png"));
            report.scene = GreyboxV6Builder.ScenePath;
            File.WriteAllText(Path.Combine(outDir, "walk-scan-" + label + ".json"), JsonUtility.ToJson(report, true) + "\n", new UTF8Encoding(false));
            Debug.Log(Describe(report));
        }

        /// <summary>Diagnostics: colliders and drawn renderers within 1.5 m of each "x,z" point (":"-separated), world bounds.</summary>
        public static string ProbePoints(string points)
        {
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            foreach (string pt in points.Split(':', ';'))
            {
                string[] xz = pt.Split(',');
                if (xz.Length < 2) continue;
                var p = new Vector3(float.Parse(xz[0], CultureInfo.InvariantCulture), 0.8f, float.Parse(xz[1], CultureInfo.InvariantCulture));
                sb.Append("== ").Append(pt).Append('\n');
                foreach (Collider c in Physics.OverlapCapsule(p + Vector3.down * 0.7f, p + Vector3.up * 1.2f, 1.5f, ~0, QueryTriggerInteraction.Collide))
                    sb.Append("  COL ").Append(LayerMask.LayerToName(c.gameObject.layer)).Append(' ').Append(c.GetType().Name).Append(c.isTrigger ? " trigger" : "")
                      .Append(" min ").Append(c.bounds.min.ToString("F2")).Append(" max ").Append(c.bounds.max.ToString("F2")).Append(' ').Append(PathOf(c.transform)).Append('\n');
                foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                {
                    if (!VisualIndex.Drawn(r)) continue;
                    Bounds b = r.bounds;
                    if (b.SqrDistance(p) > 1.5f * 1.5f) continue;
                    sb.Append("  REN ").Append(r.name).Append(" min ").Append(b.min.ToString("F2")).Append(" max ").Append(b.max.ToString("F2")).Append(' ').Append(PathOf(r.transform)).Append('\n');
                }
            }

            return sb.ToString();
        }

        public static string Describe(Report r)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[WalkScan] r {0:0.00} nodes {1} reachable {2:0} m² blocked {3}: visible {4} lowStep {5} oversize {6} invisible {7} stepFail {8} ledge {9} slope {10} boundary {11} water {12} -> seen-nothing-stuck {13}",
                r.radius, r.nodes, r.reachableM2, r.edgesBlocked, r.visible, r.lowStep, r.oversize, r.invisible, r.stepFail, r.ledge, r.slope, r.boundary, r.water, r.seenNothingStuck);
        }

        /// <summary>Walks the open scene from the spawn; writes the plan PNG when <paramref name="pngPath"/> is set.</summary>
        public static Report Scan(float radius, string pngPath)
        {
            int playerLayer = PrototypeLayers.Player;
            int mask = 0;
            for (int l = 0; l < 32; l++)
            {
                if (!Physics.GetIgnoreLayerCollision(playerLayer, l)) mask |= 1 << l;
            }

            mask &= ~(1 << playerLayer);
            Physics.SyncTransforms();
            var visuals = new VisualIndex { Radius = radius };
            var report = new Report { radius = radius, step = Step, cell = Cell };
            var probe = new GameObject("WalkScanProbe") { layer = playerLayer, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cc = probe.AddComponent<CharacterController>();
                cc.radius = radius;
                cc.height = Height;
                cc.center = new Vector3(0f, Height * 0.5f, 0f);
                cc.slopeLimit = SlopeLimit;
                cc.stepOffset = Mathf.Min(Step, Height * 0.5f);
                cc.skinWidth = Skin;
                cc.minMoveDistance = 0f;
                Walk(cc, probe.transform, mask, visuals, report, pngPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            return report;
        }

        private static void Walk(CharacterController cc, Transform t, int mask, VisualIndex visuals, Report report, string pngPath)
        {
            int n = Mathf.RoundToInt(2f * HalfExtent / Cell) + 1;
            int half = n / 2;
            var visited = new HashSet<long>();
            var reachedCell = new bool[n * n];
            var mark = new Kind?[n * n];
            var byUnit = new Dictionary<string, Blocker>(StringComparer.Ordinal);
            var queue = new Queue<Node>();

            if (!Physics.Raycast(new Vector3(Spawn.x, 40f, Spawn.z), Vector3.down, out RaycastHit g0, 80f, mask, QueryTriggerInteraction.Ignore))
            {
                throw new InvalidOperationException("[WalkScan] no ground under the spawn");
            }

            var start = new Node { Pos = g0.point, Ix = Mathf.RoundToInt(Spawn.x / Cell), Iz = Mathf.RoundToInt(Spawn.z / Cell) };
            Place(t, start.Pos);
            cc.Move(Vector3.down * 0.5f);
            start.Pos.y = t.position.y;
            visited.Add(Key(start.Ix, start.Iz, start.Pos.y));
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Node node = queue.Dequeue();
                report.nodes++;
                int ci = (node.Ix + half) + (node.Iz + half) * n;
                if (ci >= 0 && ci < reachedCell.Length) reachedCell[ci] = true;
                foreach (Vector2Int d in Dirs)
                {
                    int tx = node.Ix + d.x, tz = node.Iz + d.y;
                    if (Mathf.Abs(tx) > half || Mathf.Abs(tz) > half) continue;
                    var target = new Vector3(tx * Cell, node.Pos.y, tz * Cell);
                    Vector3 delta = target - node.Pos;
                    delta.y = 0f;
                    Vector3 end = TryStep(cc, t, node.Pos, delta, out bool ok);
                    if (ok)
                    {
                        long key = Key(tx, tz, end.y);
                        if (visited.Add(key)) queue.Enqueue(new Node { Pos = end, Ix = tx, Iz = tz });   // where the controller stands, not the grid point (no skin overlap)
                        continue;
                    }

                    report.edgesBlocked++;
                    // a diagonal that an axis step already explains (a corner, a wall alongside) is that axis' obstacle
                    Vector3 explain = delta;
                    if (d.x != 0 && d.y != 0)
                    {
                        var ax = new Vector3(delta.x, 0f, 0f);
                        var az = new Vector3(0f, 0f, delta.z);
                        TryStep(cc, t, node.Pos, ax, out bool okX);
                        TryStep(cc, t, node.Pos, az, out bool okZ);
                        if (!okX) explain = ax;
                        else if (!okZ) explain = az;
                    }

                    Kind kind = Classify(node.Pos, explain, mask, visuals, out string unit, out string colliderPath, out Vector3 at, out float top, out string via);
                    Count(report, kind);
                    if (kind == Kind.StepFail || kind == Kind.Invisible || kind == Kind.Oversize)
                        report.unexplained.Add(new Edge { kind = kind.ToString(), x = node.Pos.x, y = node.Pos.y, z = node.Pos.z, dx = delta.x, dz = delta.z, ex = end.x, ey = end.y, ez = end.z });
                    int mi = ci;
                    if (mi >= 0 && mi < mark.Length && (mark[mi] == null || Rank(kind) > Rank(mark[mi].Value))) mark[mi] = kind;
                    string bk = kind + "|" + unit;
                    if (!byUnit.TryGetValue(bk, out Blocker b))
                    {
                        b = new Blocker { kind = kind.ToString(), unit = unit, collider = colliderPath, x = at.x, y = at.y, z = at.z, top = top, via = via };
                        byUnit.Add(bk, b);
                    }

                    b.edges++;
                }
            }

            report.reachableM2 = 0f;
            foreach (bool r in reachedCell) if (r) report.reachableM2 += Cell * Cell;
            report.seenNothingStuck = report.invisible + report.oversize + report.stepFail;
            report.blockers.AddRange(byUnit.Values);
            report.blockers.Sort((a, b) => Rank(Parse(b.kind)) != Rank(Parse(a.kind)) ? Rank(Parse(b.kind)).CompareTo(Rank(Parse(a.kind))) : b.edges.CompareTo(a.edges));
            if (!string.IsNullOrEmpty(pngPath)) WritePlan(pngPath, n, reachedCell, mark);
        }

        /// <summary>One step of the controller from <paramref name="feet"/>; ok when it ends on the target column (a drop allowed).</summary>
        private static Vector3 TryStep(CharacterController cc, Transform t, Vector3 feet, Vector3 delta, out bool ok)
        {
            Place(t, feet);
            cc.Move(delta * 0.5f);
            cc.Move(delta * 0.5f);
            CollisionFlags down = cc.Move(Vector3.down * (Step + 0.3f));
            if ((down & CollisionFlags.Below) == 0) cc.Move(Vector3.down * 8f);   // a drop: the hero falls
            Vector3 end = t.position;
            Vector3 target = feet + delta;
            ok = new Vector2(end.x - target.x, end.z - target.z).magnitude < ArriveTolerance;
            return end;
        }

        private static Kind Classify(Vector3 feet, Vector3 delta, int mask, VisualIndex visuals, out string unit, out string colliderPath, out Vector3 at, out float top, out string via)
        {
            via = "";
            unit = "";
            colliderPath = "";
            top = 0f;
            float len = delta.magnitude;
            Vector3 dir = delta / Mathf.Max(1e-4f, len);
            float r = visuals.Radius;

            // 1) the body above a step: what a step cannot climb
            float lift = r + Step + 0.05f;
            if (Physics.CapsuleCast(feet + Vector3.up * lift, feet + Vector3.up * Mathf.Max(lift, Height - r), r * 0.98f, dir, out RaycastHit hit,
                    len + 0.1f, mask, QueryTriggerInteraction.Ignore))
            {
                at = hit.point;
                Kind special = Describe(hit.collider, out unit, out colliderPath);
                top = hit.collider.bounds.max.y - feet.y;
                if (special != Kind.Visible) return special;
                return visuals.Classify(UnitRoot(hit.collider.transform), hit.point, out via);
            }

            // 2) the feet: the full-width body from 5 cm up to the step band finds a low obstacle (also one the body only grazes on
            // its side) or a rise in the ground
            if (Physics.CapsuleCast(feet + Vector3.up * (r + 0.05f), feet + Vector3.up * lift, r * 0.98f, dir, out RaycastHit low,
                    len + 0.1f, mask, QueryTriggerInteraction.Ignore))
            {
                at = low.point;
                Kind special = Describe(low.collider, out unit, out colliderPath);
                if (special != Kind.Visible) return special;
                // its top: the first surface down just past the contact
                top = Physics.Raycast(low.point + dir * 0.06f + Vector3.up * 3f, Vector3.down, out RaycastHit over, 4f, mask, QueryTriggerInteraction.Ignore)
                    ? over.point.y - feet.y : low.collider.bounds.max.y - feet.y;
                if (top <= Step + 0.02f) return Kind.StepFail;   // a step the controller should have taken
                Kind seen = visuals.Classify(UnitRoot(low.collider.transform), low.point, out via);
                if (seen != Kind.Visible) return seen;
                return top <= LowStepMax ? Kind.LowStep : Kind.Visible;
            }

            // 3) already touching it inside the skin (a cast that starts inside a collider misses it): the touched collider in the
            // direction of the step, ground under the feet excluded
            Vector3 centre = feet + Vector3.up * (Height * 0.5f);
            foreach (Collider c in Physics.OverlapCapsule(feet + Vector3.up * (r + 0.06f), feet + Vector3.up * (Height - r), r + Skin + 0.02f, mask,
                         QueryTriggerInteraction.Ignore))
            {
                if (c.bounds.max.y <= feet.y + Step) continue;
                bool exact = !(c is MeshCollider mc) || mc.convex;
                Vector3 cp = exact ? c.ClosestPoint(centre) : c.bounds.ClosestPoint(centre);
                Vector3 to = cp - centre;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-6f && Vector3.Dot(to.normalized, dir) < 0.2f) continue;
                at = cp;
                top = c.bounds.max.y - feet.y;
                Kind special = Describe(c, out unit, out colliderPath);
                if (special != Kind.Visible) return special;
                Kind seen = visuals.Classify(UnitRoot(c.transform), cp, out via);
                via = "touching:" + via;
                return seen != Kind.Visible ? seen : top <= LowStepMax ? Kind.LowStep : Kind.Visible;
            }

            // 4) nothing at body or feet: the ground (a drop is walkable, so a rise or a slope)
            Vector3 target = feet + delta;
            at = target;
            if (Physics.Raycast(target + Vector3.up * 3f, Vector3.down, out RaycastHit g, 12f, mask, QueryTriggerInteraction.Ignore))
            {
                top = g.point.y - feet.y;
                colliderPath = PathOf(g.collider.transform);
                unit = PathOf(UnitRoot(g.collider.transform));
                if (top > Step) return top <= LowStepMax ? Kind.LowStep : Kind.Ledge;
                if (Vector3.Angle(g.normal, Vector3.up) > SlopeLimit) return Kind.Slope;
                via = "overlap:" + Overlaps(feet, r, mask) + " | at target:" + Overlaps(new Vector3(target.x, g.point.y, target.z), r, mask);
                return Kind.StepFail;
            }

            unit = "(hole)";
            return Kind.Ledge;
        }

        /// <summary>Diagnostics: the colliders the body capsule (skin included) overlaps at <paramref name="feet"/>, ground under it excluded.</summary>
        private static string Overlaps(Vector3 feet, float r, int mask)
        {
            var sb = new StringBuilder();
            Collider[] hits = Physics.OverlapCapsule(feet + Vector3.up * (r + 0.06f), feet + Vector3.up * (Height - r), r + Skin, mask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                if (c.bounds.max.y <= feet.y + 0.06f) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(c.name).Append('(').Append(c.GetType().Name).Append(' ').Append((c.bounds.max.y - feet.y).ToString("0.00", CultureInfo.InvariantCulture)).Append(')');
                if (sb.Length > 160) break;
            }

            return sb.Length > 0 ? sb.ToString() : "-";
        }

        /// <summary>Boundary walls and pond rims by their root; anything else is judged by what is drawn (returns Visible).</summary>
        private static Kind Describe(Collider c, out string unit, out string colliderPath)
        {
            colliderPath = PathOf(c.transform);
            unit = PathOf(UnitRoot(c.transform));
            if (colliderPath.IndexOf(WorldBoundaryBuilder.PondRimsName, StringComparison.Ordinal) >= 0) return Kind.Water;
            if (colliderPath.IndexOf(WorldBoundaryBuilder.ObjectName, StringComparison.Ordinal) >= 0) return Kind.Boundary;
            return Kind.Visible;
        }

        private static void Count(Report r, Kind k)
        {
            switch (k)
            {
                case Kind.Visible: r.visible++; break;
                case Kind.Oversize: r.oversize++; break;
                case Kind.Invisible: r.invisible++; break;
                case Kind.LowStep: r.lowStep++; break;
                case Kind.StepFail: r.stepFail++; break;
                case Kind.Ledge: r.ledge++; break;
                case Kind.Slope: r.slope++; break;
                case Kind.Boundary: r.boundary++; break;
                case Kind.Water: r.water++; break;
            }
        }

        private static int Rank(Kind k)
        {
            switch (k)
            {
                case Kind.Invisible: return 7;
                case Kind.Oversize: return 6;
                case Kind.StepFail: return 5;
                case Kind.LowStep: return 4;
                case Kind.Slope: return 4;
                case Kind.Ledge: return 3;
                case Kind.Water: return 2;
                case Kind.Boundary: return 1;
                default: return 0;
            }
        }

        private static Kind Parse(string s) => (Kind)Enum.Parse(typeof(Kind), s);

        private static Color32 ColourOf(Kind k)
        {
            switch (k)
            {
                case Kind.Invisible: return new Color32(235, 30, 40, 255);
                case Kind.Oversize: return new Color32(220, 40, 220, 255);
                case Kind.StepFail: return new Color32(255, 150, 20, 255);
                case Kind.LowStep: return new Color32(150, 200, 60, 255);
                case Kind.Slope: return new Color32(240, 220, 40, 255);
                case Kind.Ledge: return new Color32(140, 90, 50, 255);
                case Kind.Water: return new Color32(40, 200, 230, 255);
                case Kind.Boundary: return new Color32(60, 90, 230, 255);
                default: return new Color32(90, 90, 90, 255);
            }
        }

        private static void WritePlan(string path, int n, bool[] reached, Kind?[] mark)
        {
            int s = n * PixelsPerCell;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color32[s * s];
            var empty = new Color32(18, 18, 22, 255);
            var walk = new Color32(70, 150, 80, 255);
            for (int iz = 0; iz < n; iz++)
            {
                for (int ix = 0; ix < n; ix++)
                {
                    int i = ix + iz * n;
                    Color32 c = mark[i].HasValue && mark[i].Value != Kind.Visible ? ColourOf(mark[i].Value) : reached[i] ? walk : empty;
                    if (mark[i].HasValue && mark[i].Value == Kind.Visible && reached[i]) c = new Color32(55, 110, 62, 255);
                    for (int py = 0; py < PixelsPerCell; py++)
                    for (int pxx = 0; pxx < PixelsPerCell; pxx++)
                        px[(ix * PixelsPerCell + pxx) + (iz * PixelsPerCell + py) * s] = c;   // Texture2D rows go up: +z (north) is up
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void Place(Transform t, Vector3 feet)
        {
            t.position = feet;
            Physics.SyncTransforms();
        }

        private static long Key(int ix, int iz, float y)
        {
            int iy = Mathf.FloorToInt(y / 0.8f);
            return ((long)(ix + 4096) << 40) | ((long)(iz + 4096) << 20) | (long)(iy + 4096);
        }

        /// <summary>The unit a collider belongs to: the nearest LODGroup, else the outermost prefab instance, else itself.</summary>
        private static Transform UnitRoot(Transform t)
        {
            LODGroup lod = t.GetComponentInParent<LODGroup>(true);
            if (lod != null) return lod.transform;
            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
            return prefabRoot != null ? prefabRoot.transform : t;
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        /// <summary>The renderers the top-down camera draws, indexed on a 2 m grid for the "is anything drawn here" question.</summary>
        private sealed class VisualIndex
        {
            private const float Bin = 2f;
            private const float SmallVolume = 60f;   // m³: props, not halls or ground slabs
            private readonly Dictionary<long, List<(Bounds b, string name)>> _small = new Dictionary<long, List<(Bounds, string)>>();
            private readonly Dictionary<long, List<(Bounds b, string name)>> _large = new Dictionary<long, List<(Bounds, string)>>();
            public float Radius = DefaultRadius;

            public VisualIndex()
            {
                foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                {
                    if (!Drawn(r)) continue;
                    Bounds b = r.bounds;
                    if (b.size.y < 0.2f && (b.size.x > 3f || b.size.z > 3f)) continue;   // ground slabs and decals
                    Dictionary<long, List<(Bounds, string)>> index = b.size.x * b.size.y * b.size.z > SmallVolume ? _large : _small;
                    string name = r.transform.parent != null ? r.transform.parent.name + "/" + r.name : r.name;
                    int x0 = Mathf.FloorToInt(b.min.x / Bin), x1 = Mathf.FloorToInt(b.max.x / Bin);
                    int z0 = Mathf.FloorToInt(b.min.z / Bin), z1 = Mathf.FloorToInt(b.max.z / Bin);
                    for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long k = ((long)(x + 100000) << 32) | (uint)(z + 100000);
                        if (!index.TryGetValue(k, out List<(Bounds, string)> list)) index.Add(k, list = new List<(Bounds, string)>());
                        list.Add((b, name));
                    }
                }
            }

            public static bool Drawn(Renderer r)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) return false;
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
                return r.sharedMaterial != null;
            }

            public Kind Classify(Transform unit, Vector3 p, out string via)
            {
                via = "";
                bool own = false;
                foreach (Renderer r in unit.GetComponentsInChildren<Renderer>(true))
                {
                    if (!Drawn(r)) continue;
                    own = true;
                    Bounds b = r.bounds;
                    b.Expand(2f * VisibleSlack);
                    if (b.Contains(p)) { via = "own:" + r.name; return Kind.Visible; }
                }

                if (own) { via = "own-short"; return Kind.Oversize; }
                long k = ((long)(Mathf.FloorToInt(p.x / Bin) + 100000) << 32) | (uint)(Mathf.FloorToInt(p.z / Bin) + 100000);
                if (Find(_small, k, p, 2f * VisibleSlack, out via)) { via = "small:" + via; return Kind.Visible; }
                if (Find(_large, k, p, 2f * VisibleSlack, out via)) { via = "large:" + via; return Kind.Visible; }
                via = Find(_small, k, p, 3f, out string near) || Find(_large, k, p, 3f, out near) ? "near:" + near : "near:-";
                return Kind.Invisible;
            }

            private static bool Find(Dictionary<long, List<(Bounds b, string name)>> index, long k, Vector3 p, float expand, out string name)
            {
                name = "";
                if (!index.TryGetValue(k, out List<(Bounds b, string name)> list)) return false;
                foreach ((Bounds b0, string n) in list)
                {
                    Bounds b = b0;
                    b.Expand(expand);
                    if (b.Contains(p)) { name = n; return true; }
                }

                return false;
            }
        }
    }
}
