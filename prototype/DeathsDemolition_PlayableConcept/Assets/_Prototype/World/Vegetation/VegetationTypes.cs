using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · The vegetation species (LOOK-BIBLE abbreviations in the comments). The order is the recipe /
    /// density-layer order everywhere (recipe arrays, <see cref="VegetationDensityField"/> layers). Append only.
    /// </summary>
    public enum VegetationType
    {
        GrassShort = 0,   // SS  10–20 cm
        GrassMid = 1,     // GM  30–45 cm
        GrassTall = 2,    // GT  70–100 cm (susuki)
        Fern = 3,         // FE
        LeafClump = 4,    // LC  (fuki)
        Moss = 5,         // MO
        Hydrangea = 6,    // HY  (ajisai)
        SpiderLily = 7,   // SL  (higanbana)
        Kumazasa = 8,     // KZ
        LeafLitter = 9,   // ML  (momiji litter, flat patches)
    }

    public static class VegetationTypes
    {
        public const int Count = 10;

        public static readonly string[] Codes = { "SS", "GM", "GT", "FE", "LC", "MO", "HY", "SL", "KZ", "ML" };

        /// <summary>AP-031 asset prefixes (exports/v001/fbx/VEG_*.fbx); the kit binds meshes by these.</summary>
        public static readonly string[] AssetNames =
        {
            "VEG_GrassShort", "VEG_GrassMid", "VEG_GrassTall", "VEG_Fern", "VEG_LeafClump",
            "VEG_MossPatch", "VEG_Hydrangea", "VEG_SpiderLily", "VEG_Kumazasa", "VEG_MomijiLitter"
        };
    }

    /// <summary>LOOK-BIBLE §1 zones (MASK-CONTRACT mask_b.G zone ids 1–15; 0 = automatic). 08.10 (S-05): 13 = plaza joint tufts, bare soil 15.</summary>
    public enum VegetationZone : byte
    {
        Auto = 0,
        Lawn = 1,             // Z1
        Grove = 2,            // Z2 (0–0.8 R)
        Bamboo = 3,           // Z3
        PondBank = 4,         // Z4 (0.5–2.0 m)
        PathEdge = 5,         // Z5 (forces the 0–0.4 m inner band)
        WallBase = 6,         // Z6 (0–0.9 m band)
        BuildingSurround = 7, // Z7 (0.5–1.5 m band)
        Courtyard = 8,        // Z8 (kairō edge)
        PlazaEdge = 9,        // Z9 (0–0.3 m moss)
        BridgeApproach = 10,  // Z10
        MossCourt = 11,       // Z11 (R7 koke-niwa)
        Karesansui = 12,      // Z12
        PlazaJoint = 13,      // Z01 derz: plaza stone-joint tufts (SS + MO, 0.3/m², ≤ 12 cm; density from mask_b.B: joint 255, stone face 1)
        DesirePath = 14,      // worn route
        BareSoil = 15,        // no vegetation (was 13 in v1)
        // Recipe-only rows (not mask ids):
        GroveSkirt = 20,      // Z2 0.8–1.3 R
        PondShore = 21,       // Z4 0–0.5 m
        WallBaseShadow = 22,  // Z6 shadow face
        WallOuter = 23,       // Z6 0.9–1.8 m
        BuildingShadow = 24,  // Z7 shadow face
        BambooEdge = 25,      // Z3 grove-edge KZ_A band
        BambooPathSide = 26,  // Z3 lane sides
        KaresansuiCollar = 27,// Z12 rock collar
    }

    /// <summary>What the world probe found over / at a ground cell.</summary>
    public enum VegetationObstacle : byte
    {
        None = 0,
        Wall = 1,
        Building = 2,
        Rock = 3,
        Kerb = 4,
        Small = 5,
        Tree = 6,
        Bamboo = 7,
        Shrub = 8,
        Roof = 9,
        Water = 10,
        Ground = 11,
        Ignore = 12,
    }

    /// <summary>
    /// The one per-instance hash, bit-identical in C# and in DD_VegetationCore.hlsl (DDVeg_InstanceHashU): the root's world
    /// XZ quantised to 1/64 m (a power-of-two scale, so x·64 and floor are exact on CPU and GPU), then an integer mix. The CPU
    /// sorts each chunk's instances by it and draws the first N; the shader shrinks each instance by the same value against
    /// its own distance threshold — so density thins one instance at a time, never a whole chunk (no popping).
    /// </summary>
    public static class VegetationHash
    {
        public const float Quantum = 64f;

        public static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }

        public static uint Instance(float x, float z)
        {
            int ix = (int)Mathf.Floor(x * Quantum);
            int iz = (int)Mathf.Floor(z * Quantum);
            unchecked
            {
                return Mix((uint)ix * 0x9E3779B1u ^ Mix((uint)iz));
            }
        }

        /// <summary>Top 24 bits → [0, 1).</summary>
        public static float To01(uint h)
        {
            return (h >> 8) * (1f / 16777216f);
        }

        public static float Instance01(float x, float z)
        {
            return To01(Instance(x, z));
        }

        /// <summary>Deterministic lattice hash (placement, noise). Never UnityEngine.Random.</summary>
        public static uint Cell(int x, int z, int salt)
        {
            unchecked
            {
                return Mix((uint)x * 0x8DA6B343u ^ Mix((uint)z * 0xD8163841u ^ (uint)salt * 0xCB1AB31Fu));
            }
        }

        public static float Cell01(int x, int z, int salt)
        {
            return To01(Cell(x, z, salt));
        }

        /// <summary>Smooth value noise 0..1 at world XZ with the given wavelength (m). Deterministic, allocation-free.</summary>
        public static float ValueNoise(float x, float z, float wavelength, int salt)
        {
            float inv = 1f / Mathf.Max(0.01f, wavelength);
            float fx = x * inv, fz = z * inv;
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0, tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Cell01(x0, z0, salt), b = Cell01(x0 + 1, z0, salt);
            float c = Cell01(x0, z0 + 1, salt), d = Cell01(x0 + 1, z0 + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }
    }
}
