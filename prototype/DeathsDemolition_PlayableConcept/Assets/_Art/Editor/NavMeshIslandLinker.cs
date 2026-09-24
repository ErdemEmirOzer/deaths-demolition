using System.Collections.Generic;
using DD.Prototype.Core;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-038P · NavMesh pieces a body can walk to but no path reaches. The bake's 0.5 m agent erosion (+ one voxel) closes
    /// flat passages of about 0.96–1.2 m between bamboo culms, benches, posts and rocks that a body (CharacterController r 0.45
    /// + 0.03 skin) still fits through: the pocket behind becomes a NavMesh island. A zombie pushed into it snaps to that island
    /// and no path leaves it — the same round-hang as the pocket beside the south bridge post (S-10 soak seed 61, run 14; that
    /// one is closed by the rail skirts). Every triangle is labelled by whether a complete path joins it to the seed (the player
    /// start); the rest are grouped into islands; an island is body-reachable when a body capsule, lifted by the 0.35 m step,
    /// sweeps unobstructed from one of its vertices to the nearest main-mesh vertex (≤ 2 m, within a step in height). Such an
    /// island gets a bidirectional <see cref="NavMeshLink"/> along exactly that swept line — the NavMesh then goes where the
    /// body goes (a lower agent radius would do the opposite: paths through gaps a body cannot pass). Roof tops and pockets
    /// fenced in for good stay islands and are harmless.
    /// </summary>
    public static class NavMeshIslandLinker
    {
        public const string RootName = "NavMesh Island Links (WP-038P)";
        public const float BodyRadius = 0.45f, BodySkin = 0.03f, BodyHeight = 1.8f, StepHeight = 0.35f, SearchGap = 2f;

        public struct Island
        {
            public float Area, MinY, MaxY, GapToMain;
            public Vector3 Centroid, CrossFrom, CrossTo;
            public int Triangles;
            public bool BodyCrosses;
        }

        /// <summary>Everything a player body collides with (the physics matrix row of the Player layer), bodies excluded.</summary>
        public static int BodyMask()
        {
            int mask = 0, player = PrototypeLayers.Player;
            for (int l = 0; l < 32; l++) if (!Physics.GetIgnoreLayerCollision(player, l)) mask |= 1 << l;
            return mask & ~(1 << player) & ~(1 << PrototypeLayers.Enemy);
        }

        /// <summary>Islands of the loaded NavMesh relative to <paramref name="seed"/> (largest first), with the body crossing.</summary>
        public static List<Island> Islands(Vector3 seed, out int mainTriangles, out float mainArea)
        {
            Physics.SyncTransforms();
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            int n = tri.indices.Length / 3;
            var main = new bool[n];
            var path = new NavMeshPath();
            mainTriangles = 0;
            mainArea = 0f;
            var mainVerts = new List<Vector3>();
            for (int t = 0; t < n; t++)
            {
                Vector3 a = tri.vertices[tri.indices[3 * t]], b = tri.vertices[tri.indices[3 * t + 1]], c = tri.vertices[tri.indices[3 * t + 2]];
                main[t] = NavMesh.CalculatePath(seed, (a + b + c) / 3f, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!main[t]) continue;
                mainTriangles++;
                mainArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                mainVerts.Add(a); mainVerts.Add(b); mainVerts.Add(c);
            }

            // Union the other triangles by shared (1 cm) vertices.
            var parent = new Dictionary<int, int>();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var byVertex = new Dictionary<Vector3Int, int>();
            for (int t = 0; t < n; t++)
            {
                if (main[t]) continue;
                parent[t] = t;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 v = tri.vertices[tri.indices[3 * t + k]];
                    var key = new Vector3Int(Mathf.RoundToInt(v.x * 100f), Mathf.RoundToInt(v.y * 100f), Mathf.RoundToInt(v.z * 100f));
                    if (byVertex.TryGetValue(key, out int other)) parent[Find(t)] = Find(other); else byVertex[key] = t;
                }
            }

            var groups = new Dictionary<int, List<int>>();
            foreach (int t in new List<int>(parent.Keys))
            {
                int r = Find(t);
                if (!groups.TryGetValue(r, out List<int> g)) groups[r] = g = new List<int>();
                g.Add(t);
            }

            int mask = BodyMask();
            float bodyR = BodyRadius + BodySkin;
            var islands = new List<Island>();
            foreach (List<int> g in groups.Values)
            {
                var isl = new Island { MinY = float.MaxValue, MaxY = float.MinValue, GapToMain = float.MaxValue, Triangles = g.Count };
                var pairs = new List<(float d, Vector3 from, Vector3 to)>();
                Vector3 sum = Vector3.zero;
                foreach (int t in g)
                {
                    Vector3 a = tri.vertices[tri.indices[3 * t]], b = tri.vertices[tri.indices[3 * t + 1]], c = tri.vertices[tri.indices[3 * t + 2]];
                    float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                    isl.Area += area;
                    sum += (a + b + c) / 3f * area;
                    foreach (Vector3 v in new[] { a, b, c })
                    {
                        isl.MinY = Mathf.Min(isl.MinY, v.y);
                        isl.MaxY = Mathf.Max(isl.MaxY, v.y);
                        Vector3 best = Vector3.zero;
                        float bestD = SearchGap * SearchGap;
                        foreach (Vector3 m in mainVerts)
                        {
                            if (Mathf.Abs(m.y - v.y) > StepHeight) continue;
                            float dx = m.x - v.x, dz = m.z - v.z, d = dx * dx + dz * dz;
                            if (d < isl.GapToMain * isl.GapToMain) isl.GapToMain = Mathf.Sqrt(d);
                            if (d < bestD) { bestD = d; best = m; }
                        }

                        if (bestD < SearchGap * SearchGap) pairs.Add((bestD, v, best));
                    }
                }

                isl.Centroid = isl.Area > 0f ? sum / isl.Area : Vector3.zero;
                // Shortest clear crossing first: the link follows the line the body capsule actually sweeps.
                pairs.Sort((x, y) => x.d.CompareTo(y.d));
                foreach (var (_, from, to) in pairs)
                {
                    Vector3 dir = to - from; dir.y = 0f;
                    float len = dir.magnitude;
                    float baseY = Mathf.Max(from.y, to.y) + StepHeight;
                    Vector3 p0 = new Vector3(from.x, baseY + bodyR, from.z), p1 = new Vector3(from.x, Mathf.Max(baseY + bodyR, from.y + BodyHeight - BodyRadius), from.z);
                    bool clear = len < 1e-3f || (!Physics.CheckCapsule(p0, p1, bodyR, mask, QueryTriggerInteraction.Ignore) &&
                                                 !Physics.CapsuleCast(p0, p1, bodyR, dir / len, len, mask, QueryTriggerInteraction.Ignore));
                    if (!clear) continue;
                    isl.BodyCrosses = true;
                    isl.CrossFrom = from;
                    isl.CrossTo = to;
                    break;
                }

                islands.Add(isl);
            }

            islands.Sort((x, y) => y.Area.CompareTo(x.Area));
            return islands;
        }

        /// <summary>
        /// (Re)builds one bidirectional link per body-reachable island under <paramref name="root"/>; returns the link count.
        /// Run after the bake (the links are not a bake source) and before anything that reads connectivity.
        /// </summary>
        public static int LinkIslands(Transform root, Vector3 seed)
        {
            Transform old = root.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject(RootName);
            parent.transform.SetParent(root, false);
            int count = 0;
            foreach (Island isl in Islands(seed, out _, out _))
            {
                if (!isl.BodyCrosses) continue;
                var go = new GameObject("Link_" + count.ToString("00") + "_" + isl.Centroid.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "_" + isl.Centroid.z.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                go.transform.SetParent(parent.transform, false);
                var link = go.AddComponent<NavMeshLink>();
                link.agentTypeID = 0;
                link.startPoint = go.transform.InverseTransformPoint(isl.CrossFrom);
                link.endPoint = go.transform.InverseTransformPoint(isl.CrossTo);
                link.width = 0f;
                link.bidirectional = true;
                link.area = 0;
                link.costModifier = -1;
                count++;
            }

            return count;
        }
    }
}
