using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019b · The distant LOD of the blood decals (directive §10: "uzakta birleşik LOD").
    /// A settled mark far from the player is copied into one static mesh chunk per
    /// material (world-space quad, vertex colour = dried recolour × alpha cap) and its pool
    /// slot is released, so a round's worth of blood costs one draw call per material
    /// instead of one per mark. Chunks are rings of <see cref="QuadsPerChunk"/> quads:
    /// past that the oldest quad in the chunk is overwritten. Lists are pre-sized and the
    /// mesh is refilled with the List overloads, so absorbing allocates nothing after warm-up.
    /// </summary>
    public sealed class BloodDecalMerger
    {
        public const int QuadsPerChunk = 256;
        public const int MaxChunks = 4;
        private const float RebuildInterval = 0.5f;

        private sealed class Chunk
        {
            public Material Material;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public readonly List<Vector3> Vertices = new List<Vector3>(QuadsPerChunk * 4);
            public readonly List<Vector2> Uvs = new List<Vector2>(QuadsPerChunk * 4);
            public readonly List<Color> Colours = new List<Color>(QuadsPerChunk * 4);
            public readonly List<int> Triangles = new List<int>(QuadsPerChunk * 6);
            public int Count;
            public int Next;
            public bool Dirty;
        }

        private readonly List<Chunk> _chunks = new List<Chunk>(MaxChunks);
        private Transform _root;
        private float _nextRebuild;

        public int ChunkCount => _chunks.Count;

        /// <summary>Quads currently drawn by the chunks (all materials).</summary>
        public int MergedQuads
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _chunks.Count; i++) n += _chunks[i].Count;
                return n;
            }
        }

        public int Absorbed { get; private set; }

        public void Configure(Transform root)
        {
            if (_root == null)
            {
                var go = new GameObject("FX_AP010_BloodMerged");
                go.transform.SetParent(root, false);
                _root = go.transform;
            }
        }

        /// <summary>Copies the mark's quad into the chunk of its material. False when no chunk is free.</summary>
        public bool Absorb(DecalEffect decal)
        {
            if (_root == null || decal == null || decal.Row == null || decal.Row.Material == null || decal.Quad == null)
            {
                return false;
            }

            Chunk chunk = ChunkFor(decal.Row.Material);
            if (chunk == null)
            {
                return false;
            }

            decal.Freeze();
            Transform q = decal.Quad;
            // Vertex colours are not gamma-converted on upload: the linear gain goes in directly. Painted v003 marks take the dry darkening only.
            Color colour;
            if (decal.Row.Family == VfxFamily.Blood)
            {
                Color wet = BloodPalette.DecalWet, dry = BloodPalette.DecalDry;
                colour = new Color(Mathf.GammaToLinearSpace(dry.r) / Mathf.GammaToLinearSpace(wet.r), Mathf.GammaToLinearSpace(dry.g) / Mathf.GammaToLinearSpace(wet.g), Mathf.GammaToLinearSpace(dry.b) / Mathf.GammaToLinearSpace(wet.b), 1f);
            }
            else
            {
                colour = BloodPalette.TintForLinear(decal.Row, BloodPalette.DecalDry, BloodPalette.MarkCalibration);
            }
            colour.a = decal.AlphaCap;
            Rect cell = CellRect(decal.Row, decal.AtlasCell);

            int slot = chunk.Count < QuadsPerChunk ? chunk.Count : chunk.Next;
            int v0 = slot * 4;
            if (chunk.Count < QuadsPerChunk)
            {
                for (int i = 0; i < 4; i++)
                {
                    chunk.Vertices.Add(Vector3.zero);
                    chunk.Uvs.Add(Vector2.zero);
                    chunk.Colours.Add(colour);
                }

                chunk.Triangles.Add(v0); chunk.Triangles.Add(v0 + 2); chunk.Triangles.Add(v0 + 1);
                chunk.Triangles.Add(v0); chunk.Triangles.Add(v0 + 3); chunk.Triangles.Add(v0 + 2);
                chunk.Count++;
            }
            else
            {
                chunk.Next = (chunk.Next + 1) % QuadsPerChunk;
            }

            // Same corner order as VfxLibrary.Quad; the quad's -Z normal faces out of the surface already.
            chunk.Vertices[v0] = q.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
            chunk.Vertices[v0 + 1] = q.TransformPoint(new Vector3(0.5f, -0.5f, 0f));
            chunk.Vertices[v0 + 2] = q.TransformPoint(new Vector3(0.5f, 0.5f, 0f));
            chunk.Vertices[v0 + 3] = q.TransformPoint(new Vector3(-0.5f, 0.5f, 0f));
            chunk.Uvs[v0] = new Vector2(cell.xMin, cell.yMin);
            chunk.Uvs[v0 + 1] = new Vector2(cell.xMax, cell.yMin);
            chunk.Uvs[v0 + 2] = new Vector2(cell.xMax, cell.yMax);
            chunk.Uvs[v0 + 3] = new Vector2(cell.xMin, cell.yMax);
            chunk.Colours[v0] = colour; chunk.Colours[v0 + 1] = colour; chunk.Colours[v0 + 2] = colour; chunk.Colours[v0 + 3] = colour;
            chunk.Dirty = true;
            Absorbed++;
            return true;
        }

        /// <summary>Pushes dirty chunks to their meshes, at most every 0.5 s (or now when <paramref name="force"/>).</summary>
        public void Tick(float now, bool force = false)
        {
            if (!force && now < _nextRebuild)
            {
                return;
            }

            _nextRebuild = now + RebuildInterval;
            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk c = _chunks[i];
                if (!c.Dirty)
                {
                    continue;
                }

                c.Dirty = false;
                Mesh m = c.Mesh;
                m.Clear(true);
                m.SetVertices(c.Vertices);
                m.SetUVs(0, c.Uvs);
                m.SetColors(c.Colours);
                m.SetTriangles(c.Triangles, 0, false);
                m.RecalculateBounds();
                c.Renderer.enabled = c.Count > 0;
            }
        }

        /// <summary>Drops every merged quad (round reset).</summary>
        public void Clear()
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                Chunk c = _chunks[i];
                c.Vertices.Clear(); c.Uvs.Clear(); c.Colours.Clear(); c.Triangles.Clear();
                c.Count = 0; c.Next = 0; c.Dirty = true;
            }

            Tick(0f, true);
        }

        private Chunk ChunkFor(Material material)
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                if (_chunks[i].Material == material)
                {
                    return _chunks[i];
                }
            }

            if (_chunks.Count >= MaxChunks)
            {
                return null;
            }

            var go = new GameObject("BloodChunk_" + material.name);
            go.transform.SetParent(_root, false);
            var mesh = new Mesh { name = "BloodChunk" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.enabled = false;
            var chunk = new Chunk { Material = material, Mesh = mesh, Renderer = renderer };
            _chunks.Add(chunk);
            return chunk;
        }

        private static Rect CellRect(VfxCatalogEntry row, int cell)
        {
            int cols = Mathf.Max(1, row.Columns), rows = Mathf.Max(1, row.Rows);
            int col = cell % cols, r = cell / cols;
            return new Rect((float)col / cols, 1f - (float)(r + 1) / rows, 1f / cols, 1f / rows);
        }
    }
}
