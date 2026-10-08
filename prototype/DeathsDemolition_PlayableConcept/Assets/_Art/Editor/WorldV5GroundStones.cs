using System;
using System.Collections.Generic;
using DD.Prototype.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-092 B5 · The ground's stone layer — real 3D stones where a Japanese garden floor has them, so the ground reads as a
    /// surface with things lying on it at the TD scale and not as paint: an irregular edging of flat stones along the gravel paths
    /// (gaps between runs), a sparser scatter along the dirt lanes, the kerb line of every building's drip strip (amaochi), stones at
    /// the tree roots and the Z6 wall feet, and a scatter on the unwalked bamboo floor. Procedural (6 icosphere variants, hash-placed,
    /// deterministic), on the kit's boulder material (M_ap023_rock_moss, URP Lit: the pond rocks' SRP Batcher batch), merged per
    /// terrain chunk (one renderer per 17 m chunk that holds stones, culled with it), no collider (the walkable surface stays the
    /// box). Every stone top stays ≤ <see cref="MaxTop"/> m over y 0 (feet and the runtime's ground effects clip at most a pebble's
    /// height, and the stones are not "content" for WorldTdCapture's 6 m measure); nothing in the flat zones (plaza, rooms, crossings,
    /// gates, weapon points, openings), on a path surface, in water or on the paving.
    /// </summary>
    public static class WorldV5GroundStones
    {
        public const string RootName = "Ground_Stones", ChunkPrefix = "GroundStones_";
        public const string MaterialPath = "Assets/_Art/Materials/M_ap023_rock_moss.mat";
        public static string MeshPath => WorldV5Terrain.Folder + "/V6_GroundStones.asset";
        public const int Variants = 6;
        public const float MaxTop = 0.12f, MinHeight = 0.02f, UvTile = 0.7f;

        public enum Kind { PathEdge, LaneEdge, DripKerb, Root, WallFoot, Bamboo }

        public struct Stone
        {
            public Vector2 At;
            public float Width, Depth, Height, Yaw, Base;
            public int Variant;
            public Kind Kind;
        }

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ---------------------------------------------------------------- placement

        /// <summary>No stone here: flat zones, paving, path surfaces, water, buildings, walls, corridors (with <paramref name="r"/> of slack).</summary>
        private static bool Blocked(WorldV5Terrain.Inputs inputs, Vector2 p, float r, bool allowFlat = false)
        {
            if (Mathf.Abs(p.x) > GreyboxV6Builder.HalfExtent - 0.3f || Mathf.Abs(p.y) > GreyboxV6Builder.HalfExtent - 0.3f) return true;
            if (!allowFlat && WorldV5Terrain.FlatWeight(inputs, p) < 0.999f) return true;
            foreach (Vector4 rect in inputs.Paved) if (WorldV5Terrain.RectDistance(rect, p) < r + 0.05f) return true;
            foreach (Vector4 b in inputs.Buildings) if (WorldV5Terrain.RectDistance(b, p) < r + 0.05f) return true;
            foreach (WorldV5Terrain.Pond pond in inputs.Ponds) if (pond.Distance(p) < r + 0.2f) return true;
            foreach (WorldV5Terrain.Segment w in inputs.Walls)
                if (WorldV5Terrain.SegmentDistance(p, w.A, w.B, out _, out _) - w.HalfThickness < r + 0.03f) return true;
            foreach ((Vector2[] path, float half) in inputs.Corridors) if (WorldV5Terrain.PolylineDistance(p, path, out _) - half < r + 0.1f) return true;
            foreach ((Vector2 at, float radius) in inputs.FlatDiscs) if (Vector2.Distance(at, p) < radius + r) return true;
            foreach (WorldV5Terrain.Road road in inputs.Roads)
            {
                float half = road.Type == WorldV5Terrain.RoadType.Stepstone ? road.Width * 0.3f : road.Width * 0.5f;
                if (WorldV5Terrain.PolylineDistance(p, road.Points, out _) + (road.Paved ? 0f : WorldV5Terrain.EdgeNoise(p)) < half + r * 0.6f) return true;
            }

            return false;
        }

        private static void Add(List<Stone> stones, Stone s)
        {
            if (s.Height > 0f) stones.Add(s);
        }

        /// <summary>FNV-1a over the id's chars (string.GetHashCode is not stable across runtimes).</summary>
        private static int StableHash(string id)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in id) { h ^= ch; h *= 16777619; }
                return (int)(h & 0x7fffffff);
            }
        }

        private static Stone Make(WorldV5Terrain.Inputs inputs, Kind kind, Vector2 at, float width, float heightRatio, int seed)
        {
            float h01 = WorldV5Terrain.Hash(seed, 3, 907), d01 = WorldV5Terrain.Hash(seed, 5, 911);
            float ground = WorldV5Terrain.Height(inputs, at);
            // The top (base − 0.2 h + 1.25 h) never passes MaxTop; a stone with no room left (a high mound) is not placed (Height 0).
            float height = Mathf.Min(width * heightRatio * (0.8f + 0.4f * h01), (MaxTop - Mathf.Max(0f, ground)) / 1.05f);
            return new Stone
            {
                At = at, Width = width, Depth = width * (0.6f + 0.35f * d01), Height = height >= MinHeight ? height : 0f,
                Yaw = WorldV5Terrain.Hash(seed, 7, 919) * 360f, Variant = (int)(WorldV5Terrain.Hash(seed, 11, 929) * Variants) % Variants,
                Base = ground, Kind = kind,
            };
        }

        /// <summary>Walks a polyline at irregular steps, both sides (sign ±1): along-distance, point, unit direction and the left normal.</summary>
        private static IEnumerable<(float along, Vector2 p, Vector2 dir, Vector2 left)> Walk(Vector2[] line, float stepMin, float stepMax, int salt)
        {
            float along = 0f, next = 0f;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 1e-3f) continue;
                Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                while (next <= along + len)
                {
                    float t = next - along;
                    yield return (next, a + dir * t, dir, left);
                    next += Mathf.Lerp(stepMin, stepMax, WorldV5Terrain.Hash(Mathf.RoundToInt(next * 100f), salt, 937));
                }

                along += len;
            }
        }

        public static List<Stone> Place(WorldV5Terrain.Inputs inputs)
        {
            var stones = new List<Stone>();
            int seed = 0;

            // Paths: the gravel paths' flat edging stones (runs with gaps), the dirt lanes' sparse stones; both sides, on the mask's own
            // irregular edge (the edge sits where distance + EdgeNoise = half).
            foreach (WorldV5Terrain.Road road in inputs.Roads)
            {
                if (road.Paved) continue;
                bool gravel = road.Type == WorldV5Terrain.RoadType.Gravel;
                float half = road.Width * 0.5f;
                int salt = StableHash(road.Id) & 0xffff;
                foreach (float side in new[] { 1f, -1f })
                foreach ((float along, Vector2 p, Vector2 dir, Vector2 left) in Walk(road.Points, gravel ? 0.22f : 0.9f, gravel ? 0.40f : 2.2f, salt + (side > 0f ? 1 : 2)))
                {
                    seed++;
                    float run = WorldV5Terrain.Noise(along / (gravel ? 2.4f : 3.5f) + salt * 0.013f, side * 3.1f, 941);
                    if (run < (gravel ? 0.30f : 0.45f)) continue;   // gaps between runs: an edging, not a kerb
                    float width = gravel ? Mathf.Lerp(0.14f, 0.30f, WorldV5Terrain.Hash(seed, 13, 947)) : Mathf.Lerp(0.08f, 0.19f, WorldV5Terrain.Hash(seed, 13, 947));
                    Vector2 q = p + left * side * half;
                    float edge = half - WorldV5Terrain.EdgeNoise(q);
                    float outward = gravel ? width * 0.35f + 0.02f : Mathf.Lerp(0.05f, 0.45f, WorldV5Terrain.Hash(seed, 17, 953));
                    Vector2 at = p + left * side * (edge + outward) + dir * (WorldV5Terrain.Hash(seed, 19, 967) - 0.5f) * 0.08f;
                    if (Blocked(inputs, at, width * 0.45f)) continue;
                    Add(stones, Make(inputs, gravel ? Kind.PathEdge : Kind.LaneEdge, at, width, gravel ? 0.38f : 0.5f, seed));
                }
            }

            // The drip strips' kerb (amaochi): a line of stones on the strip's outer edge (0.55–0.62 m out), doors and openings left open.
            foreach (Vector4 b in inputs.Buildings)
            {
                Vector2[] ring = { new Vector2(b.x - 0.58f, b.y - 0.58f), new Vector2(b.z + 0.58f, b.y - 0.58f), new Vector2(b.z + 0.58f, b.w + 0.58f), new Vector2(b.x - 0.58f, b.w + 0.58f), new Vector2(b.x - 0.58f, b.y - 0.58f) };
                foreach ((float along, Vector2 p, Vector2 dir, Vector2 left) in Walk(ring, 0.24f, 0.34f, 971 + (int)(b.x * 7f + b.y * 3f)))
                {
                    seed++;
                    float width = Mathf.Lerp(0.16f, 0.24f, WorldV5Terrain.Hash(seed, 23, 977));
                    Vector2 at = p + left * (WorldV5Terrain.Hash(seed, 29, 983) - 0.5f) * 0.05f;
                    bool open = false;
                    foreach ((Vector2 c, float radius) in inputs.Worn) if (Vector2.Distance(c, at) < radius + 0.2f) { open = true; break; }
                    if (open || BlockedNearBuilding(inputs, b, at, width * 0.45f)) continue;
                    Add(stones, Make(inputs, Kind.DripKerb, at, width, 0.3f, seed));
                }
            }

            // Tree roots: 3–6 stones in the root flare, on the shaded (south) side more.
            foreach (WorldV5Terrain.Tree t in inputs.Trees)
            {
                int n = 3 + (int)(WorldV5Terrain.Hash((int)(t.Position.x * 10f), (int)(t.Position.y * 10f), 991) * 4f);
                for (int k = 0; k < n; k++)
                {
                    seed++;
                    float a = (WorldV5Terrain.Hash(seed, 31, 997) * 0.7f + 0.55f) * Mathf.PI * 2f;   // biased to the south / camera side
                    float r = t.Trunk + Mathf.Lerp(0.35f, 1.3f, WorldV5Terrain.Hash(seed, 37, 1009));
                    Vector2 at = t.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    float width = Mathf.Lerp(0.16f, 0.34f, WorldV5Terrain.Hash(seed, 41, 1013));
                    if (Blocked(inputs, at, width * 0.45f)) continue;
                    Add(stones, Make(inputs, Kind.Root, at, width, 0.42f, seed));
                }
            }

            // Z6 wall feet: a few stones in 3–7 m runs along the tsuiji / fences (both faces), 0.15–0.5 m off the footing.
            foreach (WorldV5Terrain.Segment w in inputs.Walls)
            {
                if (!w.Foot) continue;
                Vector2[] line = { w.A, w.B };
                foreach (float side in new[] { 1f, -1f })
                foreach ((float along, Vector2 p, Vector2 dir, Vector2 left) in Walk(line, 0.7f, 2.4f, 1019 + (int)(w.A.x * 3f + w.A.y * 5f) + (side > 0f ? 0 : 1)))
                {
                    seed++;
                    if (WorldV5Terrain.Noise(along / 5f, w.A.x * 0.11f + side, 1021) < 0.42f) continue;
                    float width = Mathf.Lerp(0.10f, 0.24f, WorldV5Terrain.Hash(seed, 43, 1031));
                    Vector2 at = p + left * side * (w.HalfThickness + Mathf.Lerp(0.12f, 0.5f, WorldV5Terrain.Hash(seed, 47, 1033)));
                    if (Blocked(inputs, at, width * 0.45f)) continue;
                    Add(stones, Make(inputs, Kind.WallFoot, at, width, 0.45f, seed));
                }
            }

            // The bamboo floor (unwalked): a jittered 1.6 m grid, ~25 % kept.
            for (float x = -54f; x < -37f; x += 1.6f)
            for (float z = -30f; z < 30f; z += 1.6f)
            {
                seed++;
                int ix = Mathf.RoundToInt(x * 10f), iz = Mathf.RoundToInt(z * 10f);
                if (WorldV5Terrain.Hash(ix, iz, 1039) > 0.25f) continue;
                var at = new Vector2(x + WorldV5Terrain.Hash(ix, iz, 1049) * 1.6f, z + WorldV5Terrain.Hash(ix, iz, 1051) * 1.6f);
                if (WorldV5Terrain.Unwalked(at) < 0.95f) continue;
                float width = Mathf.Lerp(0.14f, 0.38f, WorldV5Terrain.Hash(ix, iz, 1061));
                if (Blocked(inputs, at, width * 0.45f, true)) continue;
                Add(stones, Make(inputs, Kind.Bamboo, at, width, 0.4f, seed));
            }

            return stones;
        }

        /// <summary><see cref="Blocked"/> for a drip-kerb stone: the flat zone round its own building is fine, every other rule holds.</summary>
        private static bool BlockedNearBuilding(WorldV5Terrain.Inputs inputs, Vector4 own, Vector2 p, float r)
        {
            foreach (Vector4 b in inputs.Buildings) if (b != own && WorldV5Terrain.RectDistance(b, p) < r + 0.3f) return true;
            foreach (Vector4 rect in inputs.Paved) if (rect != own && WorldV5Terrain.RectDistance(rect, p) < r + 0.05f) return true;
            foreach ((Vector4 rect, float margin) in inputs.FlatRects)
            {
                if (rect == own) continue;
                bool isRoom = false;
                foreach (Vector4 b in inputs.Buildings) if (b == rect) { isRoom = true; break; }
                if (!isRoom && WorldV5Terrain.RectDistance(rect, p) < margin) return true;
            }

            foreach ((Vector2 at, float radius) in inputs.FlatDiscs) if (Vector2.Distance(at, p) < radius + r) return true;
            foreach ((Vector2[] path, float half) in inputs.Corridors) if (WorldV5Terrain.PolylineDistance(p, path, out _) - half < r + 0.1f) return true;
            foreach (WorldV5Terrain.Segment w in inputs.Walls)
                if (WorldV5Terrain.SegmentDistance(p, w.A, w.B, out _, out _) - w.HalfThickness < r + 0.03f) return true;
            foreach (WorldV5Terrain.Pond pond in inputs.Ponds) if (pond.Distance(p) < r + 0.2f) return true;
            foreach (WorldV5Terrain.Road road in inputs.Roads)
                if (WorldV5Terrain.PolylineDistance(p, road.Points, out _) < road.Width * 0.5f + r * 0.6f) return true;
            return Mathf.Abs(p.x) > GreyboxV6Builder.HalfExtent - 0.3f || Mathf.Abs(p.y) > GreyboxV6Builder.HalfExtent - 0.3f;
        }

        // ---------------------------------------------------------------- mesh

        private static readonly Dictionary<long, int> s_mid = new Dictionary<long, int>();

        /// <summary>A unit stone (x, z ∈ ±0.5, y 0…1 with a flat sunk base): a once-subdivided icosphere pushed by two smooth lobes per variant.</summary>
        public static (Vector3[] v, int[] t) UnitStone(int variant)
        {
            float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            s_mid.Clear();
            var tris = new List<int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (s_mid.TryGetValue(key, out int m)) return m;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                s_mid[key] = verts.Count - 1;
                return verts.Count - 1;
            }

            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            // Two lobes and a facet ripple per variant; then the base is cut flat at y −0.35 and the stone normalised to y 0…1.
            var l1 = new Vector3(WorldV5Terrain.Hash(variant, 1, 1103) - 0.5f, WorldV5Terrain.Hash(variant, 2, 1103) - 0.5f, WorldV5Terrain.Hash(variant, 3, 1103) - 0.5f).normalized;
            var l2 = new Vector3(WorldV5Terrain.Hash(variant, 4, 1109) - 0.5f, WorldV5Terrain.Hash(variant, 5, 1109) - 0.5f, WorldV5Terrain.Hash(variant, 6, 1109) - 0.5f).normalized;
            float ph = WorldV5Terrain.Hash(variant, 7, 1117) * 6.28f;
            var pts = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                float r = 1f + 0.16f * Vector3.Dot(v, l1) + 0.10f * Mathf.Max(0f, Vector3.Dot(v, l2)) + 0.06f * Mathf.Sin(v.x * 5.1f + v.z * 3.7f + ph);
                Vector3 p = v * r;
                if (p.y < -0.35f) p.y = -0.35f;
                pts[i] = p;
            }

            float minY = float.MaxValue, maxY = float.MinValue, maxX = 0f, maxZ = 0f;
            foreach (Vector3 p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); maxX = Mathf.Max(maxX, Mathf.Abs(p.x)); maxZ = Mathf.Max(maxZ, Mathf.Abs(p.z)); }
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3(pts[i].x / (2f * maxX), (pts[i].y - minY) / (maxY - minY), pts[i].z / (2f * maxZ));

            // Faces wholly on the cut base are never seen (the stone sits in the ground): dropped.
            var kept = new List<int>(tris.Count);
            for (int f = 0; f < tris.Count; f += 3)
                if (pts[tris[f]].y > 0.02f || pts[tris[f + 1]].y > 0.02f || pts[tris[f + 2]].y > 0.02f) kept.AddRange(new[] { tris[f], tris[f + 1], tris[f + 2] });
            return (pts, kept.ToArray());
        }

        /// <summary>The stones of one chunk merged (world space; box-projected UVs at <see cref="UvTile"/> m on the stone's own frame).</summary>
        public static void MergeInto(Mesh mesh, List<Stone> stones, (Vector3[] v, int[] t)[] units)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (Stone s in stones)
            {
                (Vector3[] uv, int[] ut) = units[s.Variant];
                Quaternion rot = Quaternion.Euler(0f, s.Yaw, 0f);
                // Sunk by a fifth of its height: no floating rim on the relief.
                var origin = new Vector3(s.At.x, s.Base - 0.2f * s.Height, s.At.y);
                var scale = new Vector3(s.Width, s.Height * 1.25f, s.Depth);
                int start = verts.Count;
                // Faceted: each triangle its own three vertices (crisp facets read as stone at 7–20 px; smooth reads as a blob).
                for (int f = 0; f < ut.Length; f += 3)
                {
                    Vector3 a = Vector3.Scale(uv[ut[f]], scale), b = Vector3.Scale(uv[ut[f + 1]], scale), c = Vector3.Scale(uv[ut[f + 2]], scale);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    foreach (Vector3 q in new[] { a, b, c })
                    {
                        Vector2 uv2 = ay >= ax && ay >= az ? new Vector2(q.x, q.z) : ax >= az ? new Vector2(q.z, q.y) : new Vector2(q.x, q.y);
                        uvs.Add(uv2 / UvTile + new Vector2(s.Variant * 0.37f, s.Yaw * 0.011f));
                        verts.Add(origin + rot * q);
                    }

                    tris.Add(verts.Count - 3);
                    tris.Add(verts.Count - 2);
                    tris.Add(verts.Count - 1);
                }

                _ = start;
            }

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        // ---------------------------------------------------------------- build

        public struct Report
        {
            public int Stones, Chunks, Triangles;
            public override string ToString() => "ground stones " + Stones + " in " + Chunks + " chunks, " + Triangles + " tris";
        }

        public static Report Build(Transform dress, WorldV5Terrain.Inputs inputs)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) throw new InvalidOperationException("[GROUND] stone material missing: " + MaterialPath);
            List<Stone> stones = Place(inputs);
            var units = new (Vector3[] v, int[] t)[Variants];
            for (int i = 0; i < Variants; i++) units[i] = UnitStone(i);

            var byChunk = new SortedDictionary<int, List<Stone>>();
            foreach (Stone s in stones)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt((s.At.x + WorldV5Terrain.Half) / WorldV5Terrain.ChunkSize), 0, WorldV5Terrain.ChunksPerSide - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((s.At.y + WorldV5Terrain.Half) / WorldV5Terrain.ChunkSize), 0, WorldV5Terrain.ChunksPerSide - 1);
                int key = cz * WorldV5Terrain.ChunksPerSide + cx;
                if (!byChunk.TryGetValue(key, out List<Stone> list)) byChunk[key] = list = new List<Stone>();
                list.Add(s);
            }

            // One sub-asset per terrain chunk, always (an empty mesh where a chunk holds no stone): the asset's objects and its main
            // object (chunk 0_0) never change with the layout, so a layout change only rewrites mesh data (r3: a stale main object broke
            // CreateAsset). An older file whose main object is another chunk is replaced once.
            int count = WorldV5Terrain.ChunksPerSide * WorldV5Terrain.ChunksPerSide;
            string mainName = ChunkPrefix + "0_0";
            UnityEngine.Object main = AssetDatabase.LoadMainAssetAtPath(MeshPath);
            bool fresh = !(main is Mesh mm && mm.name == mainName);
            if (fresh && main != null) AssetDatabase.DeleteAsset(MeshPath);
            var existing = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            if (!fresh) foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(MeshPath)) if (o is Mesh m) existing[m.name] = m;
            var root = new GameObject(RootName) { layer = PrototypeLayers.Environment }.transform;
            root.SetParent(dress, false);
            var report = new Report { Stones = stones.Count };
            var meshes = new Mesh[count];
            for (int key = 0; key < count; key++)
            {
                string name = ChunkPrefix + (key % WorldV5Terrain.ChunksPerSide) + "_" + (key / WorldV5Terrain.ChunksPerSide);
                if (!existing.TryGetValue(name, out Mesh mesh)) mesh = new Mesh { name = name };
                meshes[key] = mesh;
                if (!byChunk.TryGetValue(key, out List<Stone> list))
                {
                    mesh.Clear();
                    continue;
                }

                MergeInto(mesh, list, units);
                report.Chunks++;
                report.Triangles += mesh.triangles.Length / 3;
                var go = new GameObject(name) { layer = PrototypeLayers.Environment };
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }

            if (fresh) AssetDatabase.CreateAsset(meshes[0], MeshPath);
            for (int i = 0; i < count; i++)
            {
                if (!AssetDatabase.Contains(meshes[i])) AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
                EditorUtility.SetDirty(meshes[i]);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[GROUND] " + report);
            return report;
        }
    }
}
