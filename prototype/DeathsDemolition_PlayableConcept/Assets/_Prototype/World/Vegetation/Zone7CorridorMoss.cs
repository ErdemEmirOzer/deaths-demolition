using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    // Final zone-7 corridor MO floor. Called once after Accents, before placement.
    // Existing MO roots and all other layers are untouched. Any unknown prevents the density increase.
    internal static class Zone7CorridorMoss
    {
        private const int Mo = (int)VegetationType.Moss;
        private const float FloorUnitsPerM2 = 0.4f, MaximumHeight = 0.45f;

        internal static void Apply(VegetationGrid grid, VegetationRecipe recipe, VegetationDensityField field,
            byte[] finalZone, bool[] finalCorridor, byte[] clear)
        {
            if (grid == null || recipe == null || field == null || finalZone == null || finalCorridor == null || clear == null
                || grid.Count != field.Count || finalZone.Length != grid.Count || finalCorridor.Length != grid.Count
                || clear.Length != grid.Count || grid.Width != field.Width || grid.Height != field.Height
                || grid.Cell != field.Cell || !grid.Origin.Equals(field.Origin)) return;
            VegetationRecipe.Species species = recipe.Get(VegetationType.Moss);
            float layerScale = field.LayerScale[Mo];
            float maxTier = Mathf.Max(recipe.densityPerformance, Mathf.Max(recipe.densityHigh, recipe.densityCinematic));
            if (species == null || species.variants < 1 || !Finite(layerScale) || layerScale <= 0f || !Finite(maxTier)
                || maxTier <= 0f || !Finite(species.heightMin) || !Finite(species.heightMax)
                || species.heightMin <= 0f || species.heightMax < species.heightMin || !Finite(species.unitsPerInstance)) return;
            Bounds[] bounds; float[] nominal;
            try
            {
                if (!ReadActualBounds(species.variants, out bounds, out nominal)) return;
            }
            catch (System.Exception error)
            {
                // Optional density floor: unavailable mesh data preserves the authored baseline.
                Debug.LogWarning("[Vegetation] zone7 MO floor skipped: " + error.GetType().Name);
                return;
            }
            byte[] moss = field.Layers[Mo];
            for (int i = 0; i < field.Count; i++)
            {
                if (!Eligible(grid, field, finalZone, finalCorridor, clear, i)) continue;
                float density = FloorUnitsPerM2 * VegetationGrid.Multiplier(grid.DensityMul[i]) / Mathf.Max(1f, species.unitsPerInstance);
                int target = Mathf.Clamp(Mathf.RoundToInt(density / layerScale * 255f), 0, 255);
                if (target <= moss[i]) continue; // Floor, never additive; never remove existing MO.
                float maxExpected = target * layerScale * (1f / 255f) * field.Cell * field.Cell * maxTier;
                // Only k=0 can be added under the current narrow recipe. Unknown future multi-root recipes remain baseline.
                if (!Finite(maxExpected) || maxExpected >= 1f) continue;
                if (Fits(i, grid, field, species, bounds, nominal, finalZone, finalCorridor, clear)) moss[i] = (byte)target;
            }
        }

        private static bool ReadActualBounds(int variants, out Bounds[] bounds, out float[] nominal)
        {
            bounds = null; nominal = null;
            VegetationKit kit = VegetationKit.Load();
            VegetationKit.Entry entry = kit != null ? kit.Find(VegetationType.Moss) : null;
            if (entry == null || !entry.HasModels) return false;
            // Match the DrawSet's shared LOD count and variant-to-model mapping. Union every submitted mesh LOD.
            int lods = 3;
            foreach (GameObject model in entry.models)
            {
                if (model == null) return false;
                lods = Mathf.Min(lods, Mathf.Max(1, VegetationKit.LodCount(model)));
            }
            bounds = new Bounds[variants]; nominal = new float[variants];
            for (int v = 0; v < variants; v++)
                for (int lod = 0; lod < lods; lod++)
                {
                    Mesh mesh = VegetationKit.ExtractLod(entry.models[v % entry.models.Length], lod);
                    if (mesh == null) return false;
                    try
                    {
                        Bounds b = mesh.bounds;
                        if (!Finite(b.min.x) || !Finite(b.min.y) || !Finite(b.min.z) || !Finite(b.max.x)
                            || !Finite(b.max.y) || !Finite(b.max.z) || b.size.x <= 0f || b.size.z <= 0f || b.max.y <= 0f) return false;
                        if (lod == 0) { bounds[v] = b; nominal[v] = Mathf.Max(1e-3f, b.max.y); }
                        else bounds[v].Encapsulate(b);
                    }
                    finally
                    {
#if UNITY_EDITOR
                        Object.DestroyImmediate(mesh);
#else
                        Object.Destroy(mesh);
#endif
                    }
                }
            return true;
        }

        private static bool Eligible(VegetationGrid grid, VegetationDensityField field, byte[] zone, bool[] corridor, byte[] clear, int i)
        {
            float cap = field.HeightCapAt(i);
            return grid.Growable(i) && grid.Obstacle[i] == (byte)VegetationObstacle.None
                && zone[i] == (byte)VegetationZone.BuildingSurround && corridor[i] && clear[i] == 0
                && Finite(grid.GroundY[i]) && Finite(field.GroundY[i]) && grid.GroundY[i] == field.GroundY[i]
                && cap > 0f && cap <= MaximumHeight;
        }

        private static bool Fits(int i, VegetationGrid grid, VegetationDensityField field, VegetationRecipe.Species species,
            Bounds[] bounds, float[] nominal, byte[] zone, bool[] corridor, byte[] clear)
        {
            int seed = VegetationSystem.Seed, slot = Mo * 64;
            float jx = VegetationHash.Cell01(i, slot, seed + 1), jz = VegetationHash.Cell01(i, slot, seed + 2);
            float yaw = VegetationHash.Cell01(i, slot, seed + 3) * 360f;
            float hr = VegetationHash.Cell01(i, slot, seed + 4);
            float height = Mathf.Lerp(species.heightMin, species.heightMax, hr) * field.HeightScaleAt(i);
            float cap = field.HeightCapAt(i);
            if (cap > 0f && height > cap)
            {
                if (cap < species.heightMin * 0.5f) return false;
                height = cap;
            }
            // w5 (10.10): VariantHint carries the flower accents' colours (VegetationPlacer.AccentVariant) and the accents may now
            // stand on the corridor's edge — the corridor moss picks by hash, as it always did on corridor cells (no hint was written there).
            int variant = Mathf.Min(species.variants - 1, (int)(VegetationHash.Cell01(i, slot, seed + 5) * species.variants));
            float sy = height / nominal[variant];
            float sxz = Mathf.Lerp(1f, sy, 0.5f) * Mathf.Lerp(0.9f, 1.1f, VegetationHash.Cell01(i, slot, seed + 6));
            float rootY = field.GroundY[i];
            var root = new Vector3(field.Origin.x + (i % field.Width) * field.Cell + jx * field.Cell, rootY,
                field.Origin.y + (i / field.Width) * field.Cell + jz * field.Cell);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Matrix4x4 matrix = Matrix4x4.TRS(root, rotation, new Vector3(sxz, sy, sxz));
            Bounds b = bounds[variant]; Vector3 worldCenter = matrix.MultiplyPoint3x4(b.center);
            Vector3 right = rotation * Vector3.right, forward = rotation * Vector3.forward;
            Vector2 u = new Vector2(right.x, right.z), v = new Vector2(forward.x, forward.z);
            Vector2 center = new Vector2(worldCenter.x, worldCenter.z);
            float hx = b.extents.x * sxz, hz = b.extents.z * sxz;
            if (!Finite(height) || !Finite(sy) || !Finite(sxz) || sy <= 0f || sxz <= 0f) return false;
            float rx = Mathf.Abs(u.x) * hx + Mathf.Abs(v.x) * hz, rz = Mathf.Abs(u.y) * hx + Mathf.Abs(v.y) * hz;
            if (center.x - rx < grid.Origin.x || center.y - rz < grid.Origin.y
                || center.x + rx > grid.Origin.x + grid.Width * grid.Cell || center.y + rz > grid.Origin.y + grid.Height * grid.Cell) return false;
            int x0 = Mathf.FloorToInt((center.x - rx - grid.Origin.x) / grid.Cell) - 1;
            int x1 = Mathf.FloorToInt((center.x + rx - grid.Origin.x) / grid.Cell) + 1;
            int z0 = Mathf.FloorToInt((center.y - rz - grid.Origin.y) / grid.Cell) - 1;
            int z1 = Mathf.FloorToInt((center.y + rz - grid.Origin.y) / grid.Cell) + 1;
            float tileHalf = grid.Cell * 0.5f, top = rootY + b.max.y * sy;
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
            {
                Vector2 tile = new Vector2(grid.Origin.x + (x + 0.5f) * grid.Cell, grid.Origin.y + (z + 0.5f) * grid.Cell);
                Vector2 delta = tile - center;
                if (!Overlap(delta, Vector2.right, u, v, hx, hz, tileHalf)
                    || !Overlap(delta, Vector2.up, u, v, hx, hz, tileHalf)
                    || !Overlap(delta, u, u, v, hx, hz, tileHalf) || !Overlap(delta, v, u, v, hx, hz, tileHalf)) continue;
                if (x < 0 || z < 0 || x >= grid.Width || z >= grid.Height) return false;
                int j = grid.Index(x, z);
                // Conservative flat support: no fallback/default Y and no registration tolerance or gate reinterpretation.
                if (!Eligible(grid, field, zone, corridor, clear, j) || field.GroundY[j] != rootY
                    || top - field.GroundY[j] > field.HeightCapAt(j) || top - field.GroundY[j] > MaximumHeight) return false;
            }
            return true;
        }

        private static bool Overlap(Vector2 delta, Vector2 axis, Vector2 u, Vector2 v, float hx, float hz, float tileHalf)
        {
            float reach = Mathf.Abs(Vector2.Dot(u, axis)) * hx + Mathf.Abs(Vector2.Dot(v, axis)) * hz
                + tileHalf * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y));
            return Mathf.Abs(Vector2.Dot(delta, axis)) <= reach + 1e-6f; // Boundary contact is included conservatively.
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
