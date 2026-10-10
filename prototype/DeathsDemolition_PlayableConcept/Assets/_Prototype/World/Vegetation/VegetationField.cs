using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The semantic grid of a world (what is where): ground height, path / plaza / water / exclusion flags, the
    /// obstacle class over each cell and its height, the corridor and zone overrides from S-05's masks (MASK-CONTRACT v1),
    /// trees and clear points. Filled by <see cref="VegetationWorldProbe"/> (physics) and <see cref="VegetationMaskSet"/>
    /// (S-05 masks); read by <see cref="VegetationZoner"/>. Built once per world load; never touched per frame.
    /// </summary>
    public sealed class VegetationGrid
    {
        [Flags]
        public enum Flag : ushort
        {
            None = 0,
            Ground = 1,
            Path = 2,
            Plaza = 4,
            Water = 8,
            Excluded = 16,
            Roofed = 32,
            Bamboo = 64,
            MossCourt = 128,
            Shrub = 256,
        }

        public enum ClearKind : byte
        {
            Spawn = 0,
            Weapon = 1,
            Door = 2,
        }

        public struct Tree
        {
            public Vector2 Position;
            public float Radius;
            public bool Grove;
            public bool Litter;
        }

        public struct ClearPoint
        {
            public Vector2 Position;
            public float Radius;
            public ClearKind Kind;
        }

        public readonly int Width;
        public readonly int Height;
        public readonly float Cell;
        public readonly Vector2 Origin;

        public readonly ushort[] Flags;
        public readonly byte[] Obstacle;
        public readonly float[] GroundY;
        /// <summary>Obstacle top above the ground (m); a wall seed's height.</summary>
        public readonly float[] ObstacleHeight;
        /// <summary>0 = automatic, 1 = not corridor, 2 = corridor (mask_a.A).</summary>
        public readonly byte[] Corridor;
        /// <summary>Forced <see cref="VegetationZone"/> (mask_b.G), 0 = automatic.</summary>
        public readonly byte[] Zone;
        /// <summary>0 = ×1, else value/128 (mask_b.B / mask_b.A).</summary>
        public readonly byte[] DensityMul;
        public readonly byte[] HeightMul;

        public readonly List<Tree> Trees = new List<Tree>(128);
        public readonly List<ClearPoint> ClearPoints = new List<ClearPoint>(64);

        public float DefaultGroundY;

        public VegetationGrid(Vector2 min, Vector2 max, float cell)
        {
            Cell = Mathf.Max(0.05f, cell);
            Origin = min;
            Width = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / Cell));
            Height = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / Cell));
            int n = Width * Height;
            Flags = new ushort[n];
            Obstacle = new byte[n];
            GroundY = new float[n];
            ObstacleHeight = new float[n];
            Corridor = new byte[n];
            Zone = new byte[n];
            DensityMul = new byte[n];
            HeightMul = new byte[n];
        }

        public int Count => Width * Height;

        public int Index(int x, int z) => z * Width + x;

        public Vector2 CellCenter(int x, int z) => new Vector2(Origin.x + (x + 0.5f) * Cell, Origin.y + (z + 0.5f) * Cell);

        public Vector2 CellCenter(int i) => CellCenter(i % Width, i / Width);

        public bool TryCell(float wx, float wz, out int x, out int z)
        {
            x = Mathf.FloorToInt((wx - Origin.x) / Cell);
            z = Mathf.FloorToInt((wz - Origin.y) / Cell);
            return x >= 0 && z >= 0 && x < Width && z < Height;
        }

        public bool Has(int i, Flag f) => (Flags[i] & (ushort)f) != 0;

        public void Set(int i, Flag f) => Flags[i] = (ushort)(Flags[i] | (ushort)f);

        public void Clear(int i, Flag f) => Flags[i] = (ushort)(Flags[i] & ~(ushort)f);

        /// <summary>A cell vegetation may grow on at all (before recipes): ground, not path / plaza / water / excluded / roofed, no solid obstacle.</summary>
        public bool Growable(int i)
        {
            const ushort blocked = (ushort)(Flag.Path | Flag.Plaza | Flag.Water | Flag.Excluded | Flag.Roofed);
            if ((Flags[i] & (ushort)Flag.Ground) == 0 || (Flags[i] & blocked) != 0) return false;
            return !IsSolid((VegetationObstacle)Obstacle[i]);
        }

        public static bool IsSolid(VegetationObstacle o)
        {
            return o == VegetationObstacle.Wall || o == VegetationObstacle.Building || o == VegetationObstacle.Rock
                   || o == VegetationObstacle.Kerb || o == VegetationObstacle.Small;
        }

        public static float Multiplier(byte v) => v == 0 ? 1f : v / 128f;
    }

    /// <summary>
    /// WP-093 · Per-species instance densities on a world grid (the zoner's output, the placer's input). Layers are bytes:
    /// 255 = the species' <see cref="VegetationRecipe.Species.maxDensity"/> instances/m².
    /// </summary>
    public sealed class VegetationDensityField
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float Cell;
        public readonly Vector2 Origin;
        public readonly byte[][] Layers;
        public readonly float[] LayerScale;
        /// <summary>128 = ×1 (0..2).</summary>
        public readonly byte[] HeightScale;
        /// <summary>0 = no cap, else value × <see cref="HeightCapStep"/> m.</summary>
        public readonly byte[] HeightCap;
        /// <summary>Flower accents' colour variant (AD v1.2, 10.10): HY in the low nibble, SL in the high nibble, each variant + 1; 0 = none.
        /// Read by the accent species only (VegetationPlacer.AccentVariant); the other species pick their variant by hash.</summary>
        public readonly byte[] VariantHint;
        /// <summary>1 = the cell is paving (plaza, path, plaza joint): the draw set mutes the cover there (AD S1, 10.10).</summary>
        public readonly byte[] Paved;
        public readonly float[] GroundY;
        public float DefaultGroundY;

        public const float HeightCapStep = 0.005f;

        public VegetationDensityField(int width, int height, float cell, Vector2 origin, VegetationRecipe recipe)
        {
            Width = width;
            Height = height;
            Cell = cell;
            Origin = origin;
            int n = width * height;
            Layers = new byte[VegetationTypes.Count][];
            LayerScale = new float[VegetationTypes.Count];
            for (int t = 0; t < VegetationTypes.Count; t++)
            {
                Layers[t] = new byte[n];
                LayerScale[t] = recipe != null ? Mathf.Max(0.01f, recipe.Get((VegetationType)t).maxDensity) : 16f;
            }

            HeightScale = new byte[n];
            HeightCap = new byte[n];
            VariantHint = new byte[n];
            Paved = new byte[n];
            GroundY = new float[n];
            for (int i = 0; i < n; i++) HeightScale[i] = 128;
        }

        public int Count => Width * Height;

        public Vector2 CellCenter(int i) => new Vector2(Origin.x + (i % Width + 0.5f) * Cell, Origin.y + (i / Width + 0.5f) * Cell);

        public void SetDensity(int type, int i, float perM2)
        {
            Layers[type][i] = (byte)Mathf.Clamp(Mathf.RoundToInt(perM2 / LayerScale[type] * 255f), 0, 255);
        }

        public float Density(int type, int i) => Layers[type][i] * LayerScale[type] * (1f / 255f);

        public void SetHeight(int i, float scale, float capMetres)
        {
            HeightScale[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(scale * 128f), 1, 255);
            HeightCap[i] = capMetres <= 0f ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt(capMetres / HeightCapStep), 1, 255);
        }

        public float HeightScaleAt(int i) => HeightScale[i] / 128f;

        public float HeightCapAt(int i) => HeightCap[i] == 0 ? 0f : HeightCap[i] * HeightCapStep;

        /// <summary>Total expected instances (Σ density × cell area) per species; test / report readout.</summary>
        public float ExpectedInstances(int type)
        {
            float sum = 0f;
            byte[] layer = Layers[type];
            for (int i = 0; i < layer.Length; i++) sum += layer[i];
            return sum * LayerScale[type] / 255f * Cell * Cell;
        }
    }
}
