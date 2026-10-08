using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The placed world: per (species, variant) group one matrix array ordered by chunk, then by the instance hash
    /// (ascending), with per-chunk [start, count) ranges and chunk bounds. Drawing the first N of a chunk's range is the
    /// CPU half of the distance density fade (see <see cref="VegetationHash"/>).
    /// </summary>
    public sealed class VegetationLayout
    {
        public sealed class Group
        {
            public VegetationType Type;
            public int Variant;
            public Matrix4x4[] Matrices;
            public int[] ChunkStart;
            public int[] ChunkCount;
            public int Count => Matrices != null ? Matrices.Length : 0;
        }

        public int ChunksX;
        public int ChunksZ;
        public Vector2 Origin;
        public float ChunkSize;
        public Bounds[] ChunkBounds;
        public bool[] ChunkUsed;
        public Group[] Groups;
        public int TotalInstances;
        public readonly int[] InstancesPerType = new int[VegetationTypes.Count];

        public int ChunkCount => ChunksX * ChunksZ;

        public int ChunkIndex(float x, float z)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((x - Origin.x) / ChunkSize), 0, ChunksX - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z - Origin.y) / ChunkSize), 0, ChunksZ - 1);
            return cz * ChunksX + cx;
        }

        /// <summary>Nearest and farthest distance from <paramref name="p"/> to an AABB (0 inside). Allocation-free.</summary>
        public static void DistanceRange(Bounds b, Vector3 p, out float near, out float far)
        {
            near = Mathf.Sqrt(b.SqrDistance(p));
            Vector3 min = b.min, max = b.max;
            float fx = Mathf.Max(Mathf.Abs(p.x - min.x), Mathf.Abs(p.x - max.x));
            float fy = Mathf.Max(Mathf.Abs(p.y - min.y), Mathf.Abs(p.y - max.y));
            float fz = Mathf.Max(Mathf.Abs(p.z - min.z), Mathf.Abs(p.z - max.z));
            far = Mathf.Sqrt(fx * fx + fy * fy + fz * fz);
        }

        /// <summary>Distance density: 1 up to fadeStart, down to farKeep at fadeEnd (shader: DDVeg_DensityAt).</summary>
        public static float DensityAt(float distance, float fadeStart, float fadeEnd, float farKeep)
        {
            float t = Mathf.Clamp01((distance - fadeStart) / Mathf.Max(0.01f, fadeEnd - fadeStart));
            return Mathf.Lerp(1f, farKeep, t);
        }

        /// <summary>TD camera height factor: authored at <paramref name="reference"/> m, thinner above, never below <paramref name="min"/>.</summary>
        public static float HeightFactor(float cameraHeightAboveGround, float reference, float min)
        {
            if (cameraHeightAboveGround <= reference) return 1f;
            return Mathf.Clamp(reference / Mathf.Max(0.01f, cameraHeightAboveGround), min, 1f);
        }

        /// <summary>
        /// How many of a chunk's hash-sorted instances to submit: every instance whose hash is below the nearest point's
        /// density (+ the shader's grow band), so the GPU-side per-instance fade never needs one that was not drawn.
        /// </summary>
        public static int DrawCount(int count, float densityAtNearest, float band)
        {
            if (count <= 0) return 0;
            int n = Mathf.CeilToInt(count * Mathf.Clamp01(densityAtNearest + band)) + 1;
            return n > count ? count : n;
        }
    }

    /// <summary>
    /// WP-093 · Density field → instances. Deterministic: every random choice is a lattice hash of (cell, species, slot,
    /// seed) — same field + seed + tier scale → bit-identical matrices. Runs once per world load / tier change.
    /// </summary>
    public static class VegetationPlacer
    {
        private struct Inst
        {
            public int Chunk;
            public float Hash;
            public Matrix4x4 Matrix;
            public Vector3 Position;
            public float Top;
            public float Reach;
        }

        /// <param name="nominalHeight">[type][variant] LOD0 mesh height (m) the instance scale is measured against.</param>
        public static VegetationLayout Place(VegetationDensityField field, VegetationRecipe recipe, float tierScale, int seed, float[][] nominalHeight)
        {
            if (recipe == null) recipe = VegetationRecipe.Load();
            float chunk = Mathf.Max(1f, recipe.chunkSize);
            var layout = new VegetationLayout
            {
                Origin = field.Origin,
                ChunkSize = chunk,
                ChunksX = Mathf.Max(1, Mathf.CeilToInt(field.Width * field.Cell / chunk)),
                ChunksZ = Mathf.Max(1, Mathf.CeilToInt(field.Height * field.Cell / chunk)),
            };

            int chunkCount = layout.ChunksX * layout.ChunksZ;
            var groupLists = new List<List<Inst>>();
            var groupType = new List<VegetationType>();
            var groupVariant = new List<int>();
            var firstGroup = new int[VegetationTypes.Count];
            for (int t = 0; t < VegetationTypes.Count; t++)
            {
                VegetationRecipe.Species sp = recipe.Get((VegetationType)t);
                firstGroup[t] = groupLists.Count;
                for (int v = 0; v < Mathf.Max(1, sp.variants); v++)
                {
                    groupLists.Add(new List<Inst>(256));
                    groupType.Add((VegetationType)t);
                    groupVariant.Add(v);
                }
            }

            float cellArea = field.Cell * field.Cell;
            for (int i = 0; i < field.Count; i++)
            {
                int cx = i % field.Width, cz = i / field.Width;
                float x0 = field.Origin.x + cx * field.Cell, z0 = field.Origin.y + cz * field.Cell;
                float groundY = float.IsNaN(field.GroundY[i]) ? field.DefaultGroundY : field.GroundY[i];
                float hScale = field.HeightScaleAt(i);
                float hCap = field.HeightCapAt(i);
                for (int t = 0; t < VegetationTypes.Count; t++)
                {
                    byte raw = field.Layers[t][i];
                    if (raw == 0) continue;
                    VegetationRecipe.Species sp = recipe.Get((VegetationType)t);
                    float expected = raw * field.LayerScale[t] * (1f / 255f) * cellArea * tierScale;
                    int count = (int)expected;
                    if (VegetationHash.Cell01(i, t, seed) < expected - count) count++;
                    int variants = Mathf.Max(1, sp.variants);
                    for (int k = 0; k < count; k++)
                    {
                        int slot = t * 64 + k;
                        float jx = VegetationHash.Cell01(i, slot, seed + 1);
                        float jz = VegetationHash.Cell01(i, slot, seed + 2);
                        float yaw = VegetationHash.Cell01(i, slot, seed + 3) * 360f;
                        float hr = VegetationHash.Cell01(i, slot, seed + 4);
                        float height = Mathf.Lerp(sp.heightMin, sp.heightMax, hr) * hScale;
                        if (hCap > 0f && height > hCap)
                        {
                            if (hCap < sp.heightMin * 0.5f) continue;   // would read squashed: leave it out
                            height = hCap;
                        }

                        byte hint = field.VariantHint[i];
                        int variant = hint > 0 ? Mathf.Min(hint - 1, variants - 1) : Mathf.Min(variants - 1, (int)(VegetationHash.Cell01(i, slot, seed + 5) * variants));
                        float nominal = Nominal(nominalHeight, t, variant, sp);
                        float sy = height / nominal;
                        float sxz = Mathf.Lerp(1f, sy, 0.5f) * Mathf.Lerp(0.9f, 1.1f, VegetationHash.Cell01(i, slot, seed + 6));
                        var pos = new Vector3(x0 + jx * field.Cell, groundY + (t == (int)VegetationType.LeafLitter ? 0.002f : 0f), z0 + jz * field.Cell);
                        Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(sxz, sy, sxz));
                        groupLists[firstGroup[t] + variant].Add(new Inst
                        {
                            Chunk = layout.ChunkIndex(pos.x, pos.z),
                            Hash = VegetationHash.Instance01(pos.x, pos.z),
                            Matrix = m,
                            Position = pos,
                            Top = pos.y + height,
                            Reach = sp.footprint * 0.5f * sxz + height * 0.5f,
                        });
                    }
                }
            }

            layout.ChunkBounds = new Bounds[chunkCount];
            layout.ChunkUsed = new bool[chunkCount];
            var groups = new VegetationLayout.Group[groupLists.Count];
            for (int g = 0; g < groupLists.Count; g++)
            {
                List<Inst> list = groupLists[g];
                list.Sort((a, b) => a.Chunk != b.Chunk ? a.Chunk.CompareTo(b.Chunk) : a.Hash.CompareTo(b.Hash));
                var group = new VegetationLayout.Group
                {
                    Type = groupType[g],
                    Variant = groupVariant[g],
                    Matrices = new Matrix4x4[list.Count],
                    ChunkStart = new int[chunkCount],
                    ChunkCount = new int[chunkCount],
                };
                for (int k = 0; k < list.Count; k++)
                {
                    Inst inst = list[k];
                    group.Matrices[k] = inst.Matrix;
                    if (group.ChunkCount[inst.Chunk] == 0) group.ChunkStart[inst.Chunk] = k;
                    group.ChunkCount[inst.Chunk]++;
                    var b = new Bounds(inst.Position + Vector3.up * ((inst.Top - inst.Position.y) * 0.5f),
                        new Vector3(inst.Reach * 2f, Mathf.Max(0.05f, inst.Top - inst.Position.y) + 0.1f, inst.Reach * 2f));
                    if (layout.ChunkUsed[inst.Chunk]) layout.ChunkBounds[inst.Chunk].Encapsulate(b);
                    else
                    {
                        layout.ChunkBounds[inst.Chunk] = b;
                        layout.ChunkUsed[inst.Chunk] = true;
                    }
                }

                groups[g] = group;
                layout.TotalInstances += list.Count;
                layout.InstancesPerType[(int)group.Type] += list.Count;
            }

            layout.Groups = groups;
            return layout;
        }

        private static float Nominal(float[][] nominalHeight, int t, int variant, VegetationRecipe.Species sp)
        {
            if (nominalHeight != null && t < nominalHeight.Length && nominalHeight[t] != null && variant < nominalHeight[t].Length && nominalHeight[t][variant] > 1e-4f)
            {
                return nominalHeight[t][variant];
            }

            return Mathf.Max(1e-3f, (sp.heightMin + sp.heightMax) * 0.5f);
        }
    }
}
