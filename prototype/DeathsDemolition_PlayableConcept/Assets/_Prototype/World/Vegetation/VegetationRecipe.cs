using System;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · LOOK-BIBLE v1 (art-director, 07.10) as DATA: species look and size, zone recipes (instances/m² per
    /// species), transition numbers, clear radii, region data and the obstacle-family table. The defaults below are the
    /// bible's numbers at High (density scale 1.0); an asset at Resources/<see cref="ResourcesPath"/> overrides all of them,
    /// so AD tuning is an asset edit, not a code change (Assets > Create > Deaths Demolition > Vegetation Recipe).
    /// </summary>
    [CreateAssetMenu(menuName = "Deaths Demolition/Vegetation Recipe", fileName = "DD_VegetationRecipe")]
    public sealed class VegetationRecipe : ScriptableObject
    {
        public const string ResourcesPath = "DD_VegetationRecipe";

        public enum PlaceholderShape
        {
            Blades = 0,
            Fronds = 1,
            BroadLeaves = 2,
            Patch = 3,
            FlowerShrub = 4,
            FlowerStems = 5,
            Bamboo = 6,
            Litter = 7,
        }

        [Serializable]
        public sealed class Species
        {
            public VegetationType type;
            [Tooltip("Mesh variants (AP-031 _A/_B/_C). Placeholders generate this many.")]
            public int variants = 2;
            [Tooltip("Final instance height range (m), variation included (bible §3 scale ranges folded in).")]
            public float heightMin = 0.1f, heightMax = 0.2f;
            [Tooltip("Clump footprint diameter (m).")]
            public float footprint = 0.35f;
            [Tooltip("vc1 B for placeholder geometry (0 thin grass … 1 woody).")]
            [Range(0f, 1f)] public float stiffness;
            [Tooltip("BaseColor averages, sRGB (bible §3). Root → tip; dry tips; placeholder accents (flower heads, plumes, sasa edge).")]
            public Color rootColor = Color.gray, tipColor = Color.gray, dryColor = Color.gray, accentColor = Color.gray, accentColorB = Color.gray;
            [Range(0f, 1f)] public float dryFraction;
            [Tooltip("Back-light translucency (upper half only, R-weighted; bible §3 Işık).")]
            [Range(0f, 1f)] public float translucency;
            public float hueJitterDegrees = 4f, valueJitter = 0.06f, saturationJitter = 0.03f;
            [Tooltip("Albedo saturation ceiling (HSV s of the final albedo; 1 = none). LOOK-BIBLE v1.1: MO 0.42.")]
            [Range(0f, 1f)] public float saturationCap = 1f;
            [Tooltip("True when the species has an LOD2 cross card (bible: > 32 m).")]
            public bool hasLod2;
            [Tooltip("Instances/m² stored at layer value 255 (quantisation scale of the density field).")]
            public float maxDensity = 12f;
            [Tooltip("Units one instance stands for (momiji litter: leaves per patch). Recipe densities are in units/m².")]
            public float unitsPerInstance = 1f;
            [Tooltip("Fraction of the density kept at the far fade distance (distance density fade).")]
            [Range(0f, 1f)] public float farKeep = 0.35f;
            public PlaceholderShape shape;
            public int placeholderBlades = 12, placeholderSegments = 3;
            public float placeholderBladeWidth = 0.022f;
        }

        [Serializable]
        public sealed class ZoneRecipe
        {
            public VegetationZone zone;
            [Tooltip("Units/m² per species, VegetationType order: SS GM GT FE LC MO HY SL KZ ML.")]
            public float[] density = new float[VegetationTypes.Count];
            [Tooltip("Height multiplier for the zone (bible '×0.7' etc.).")]
            public float heightScale = 1f;
            [Tooltip("Height cap in metres (0 = none).")]
            public float heightCap;
        }

        [Serializable]
        public sealed class Transitions
        {
            [Header("Z5 path edge (bible Geçişler)")]
            public float pathInner = 0.4f, pathOuter = 1.2f, pathInnerMoss = 0.3f, pathInnerShortFraction = 0.3f, pathInnerHeight = 0.5f;
            public float pathRampHeightStart = 0.6f, pathMidStart = 1.0f, pathEdgeJitter = 0.3f, pathEdgeJitterWavelength = 2f;
            [Header("Z6 wall base")]
            public float wallBand = 0.9f, wallBandLow = 0.7f, lowWallHeight = 1.9f, wallOuter = 1.8f, wallMinHeight = 1.2f;
            public float wallRunWavelength = 5f, wallRunThreshold = 0.3f;
            [Header("Z7 building surround / Z8 kairō")]
            public float buildingAmaochi = 0.5f, buildingBand = 1.5f, buildingHeightCap = 0.6f;
            [Header("Z4 pond")]
            public float pondShore = 0.5f, pondBank = 2.0f, pondBankMidCap = 0.45f;
            [Header("Z9 plaza edge")]
            public float plazaMoss = 0.3f, plazaRamp = 1.0f, plazaRampStartFraction = 0.3f, plazaMidStart = 1.5f, plazaNoTall = 4f;
            [Header("Z2 grove (× crown radius R)")]
            public float groveInner = 0.8f, groveOuter = 1.3f, litterInner = 0.7f, litterOuter = 1.3f, litterDownwind = 0.3f;
            [Tooltip("Momiji litter under a crown (leaves/m², bible ML 30).")]
            public float litterLeavesPerM2 = 30f;
            [Tooltip("Wind direction the litter centre drifts toward (degrees from +X toward +Z; WorldWind authored direction).")]
            public float litterDriftDegrees = 24f;
            [Header("Z3 bamboo")]
            public float bambooEdgeBand = 1.0f, bambooEdgeJitter = 1.5f, bambooPathSide = 0.7f, bambooTallInside = 1.0f;
            [Header("Clear radii")]
            public float spawnClear = 3f, spawnShortScale = 0.6f, weaponClear = 1.5f, weaponHeightCap = 0.15f, doorClear = 1.5f, doorHeightCap = 0.2f;
            [Header("Corridor (principle 2, R1)")]
            public float corridorObstacleDistance = 1.5f, corridorHeightCap = 0.45f, corridorMidDensityCap = 1.5f, corridorLitterCap = 12f;
            [Header("Lawn clustering (Z1)")]
            public float macroNoiseWavelength = 10f, macroNoiseAmount = 0.25f;
            public float midPatchWavelength = 2.5f, midPatchCoverage = 0.15f;
            public float mossClusterWavelength = 3f, mossClusterCoverage = 0.12f;
            public float leafClusterWavelength = 1.6f, leafClusterCoverage = 0.1f;
            [Header("Light")]
            [Tooltip("Ground direction shadows fall (XZ, +X east, +Z north). Sun KKB 50° → shadows to GGD.")]
            public Vector2 shadowDirection = new Vector2(0.38f, -0.92f);
            public float shadowFaceDot = 0.3f;
        }

        [Serializable]
        public sealed class Accents
        {
            [Header("Hydrangea (HY) clusters")]
            public int hydrangeaClusters = 30;
            public float hydrangeaSpacing = 8f, hydrangeaRadius = 0.6f, hydrangeaClearance = 2.5f;
            public int hydrangeaPlantsMin = 1, hydrangeaPlantsMax = 3;
            [Header("Spider lily (SL) strips")]
            public int spiderLilyStripsMin = 6, spiderLilyStripsMax = 10;
            public float spiderLilySpacing = 6f, spiderLilyLengthMin = 1.5f, spiderLilyLengthMax = 3f, spiderLilyWidth = 0.3f;
            public int spiderLilyPerStripMin = 2, spiderLilyPerStripMax = 4;
            [Tooltip("World rects (x0, z0, x1, z1) where strips may go (bible §1 SL Yerler).")]
            public Vector4[] spiderLilyRegions =
            {
                new Vector4(-20f, -42f, 0f, -38f),
                new Vector4(27f, -31f, 37f, -7f),
                new Vector4(-36f, -32f, -28f, -26f),
            };
        }

        [Serializable]
        public sealed class Regions
        {
            [Tooltip("Z12 karesansui beds (x0, z0, x1, z1).")]
            public Vector4[] karesansui = { new Vector4(12f, -30f, 28f, -16f) };
            [Tooltip("Z10 bridge approaches (x, z, radius).")]
            public Vector3[] bridgeApproaches = { new Vector3(4f, -18f, 4f), new Vector3(4f, -39.5f, 4f), new Vector3(-8f, -20f, 4f), new Vector3(-8f, -37f, 4f) };
            [Tooltip("NavRegion Room ids that are open moss courts (Z11) rather than interiors.")]
            public string[] mossCourtRooms = { "R7_SakuraCourt" };
        }

        [Serializable]
        public sealed class ObstacleTable
        {
            [Tooltip("SurfaceTag.Family (AP-023 kit) → obstacle class.")]
            public string[] wall = { "wall_kit", "fence_kenninji", "fence_yotsume" };
            public string[] building = { "palace_kit", "kairo_open", "kairo_bay", "kairo_angle", "yagura", "azumaya", "teahouse", "kura", "pavilion", "gates", "chumon", "plank_bridge", "slab_bridge" };
            public string[] rock = { "rock_large", "pond_rock", "waterfall_rock", "pond_shore_pebble", "pond_shore_curve" };
            public string[] kerb = { "granite_kerb", "planter" };
            public string[] small = { "lantern_kasuga" };
            public string[] tree = { "sugi", "black_pine", "sakura_v2", "momiji" };
            public string[] bamboo = { "bamboo_v2" };
            public string[] shrub = { "groundcover", "satsuki", "lotus", "pond_reeds", "pond_shore_moss" };
            [Tooltip("Tree families whose crowns make a Z2 grove floor, with crown radius R (m).")]
            public string[] groveFamilies = { "sakura_v2", "momiji" };
            public float[] groveCrownRadius = { 3.5f, 3.0f };
            [Tooltip("Tree families that drop momiji litter (ML).")]
            public string[] litterFamilies = { "momiji" };
            [Tooltip("Greybox name prefixes (no family tag): name prefix → class.")]
            public string[] bambooNames = { "Bamboo_" };
            public string[] treeNames = { "Tree_" };
            public string[] roofNames = { "RoofOccluder", "Roof", "Hall_Roof" };
            public string[] wallNames = { "Wall", "RingWall", "Fence" };
            public string[] buildingNames = { "Floor", "Deck", "Stair", "Hall_", "Kairo", "Plinth" };
            public string[] kerbNames = { "Planter_", "Kerb" };
            public string[] groundNames = { "Ground" };
        }

        [Header("Species (VegetationType order)")]
        public Species[] species = DefaultSpecies();

        [Header("Zone recipes (bible §1)")]
        public ZoneRecipe[] zones = DefaultZones();

        public Transitions transitions = new Transitions();
        public Accents accents = new Accents();
        public Regions regions = new Regions();
        public ObstacleTable obstacles = new ObstacleTable();

        [Header("Tiers (bible header: Performance ×0.5, Cinematic ×1.3)")]
        public float densityPerformance = 0.5f, densityHigh = 1.0f, densityCinematic = 1.3f;

        [Header("LOD and distance (bible §2)")]
        public float lod1Distance = 22f, lod2Distance = 32f, lodCrossFade = 2f;
        public float fadeStart = 30f, fadeEnd = 42f;
        [Tooltip("TD camera height the densities are authored for; higher cameras thin the field.")]
        public float referenceCameraHeight = 15f;
        public float minHeightDensity = 0.5f;

        [Header("World probe")]
        public float probeCell = 0.25f;
        public float probeTop = 40f;
        public float chunkSize = 8f;

        [Header("Look")]
        public Color translucencyTone = Hex(0xC8B47E);
        [Header("Ground under grass (principle 1: soil ≥ 8 L* darker than the grass; P-05 08.10 §2)")]
        [Tooltip("Ground albedo × this at full grass cover (DD/GroundMoss reads _DD_VegCoverTex).")]
        [Range(0.4f, 1f)] public float groundAlbedoUnderGrass = 0.78f;
        [Tooltip("Ground occlusion (indirect diffuse + specular) at full cover — the specular occlusion ≈ ×0.45.")]
        [Range(0f, 1f)] public float groundOcclusionUnderGrass = 0.45f;
        [Tooltip("Ground smoothness × this at full cover (the direct back-lit specular).")]
        [Range(0f, 1f)] public float groundSmoothnessUnderGrass = 0.55f;
        [Range(0f, 1f)] public float alphaCutoff = 0.45f;

        private static VegetationRecipe s_default;

        /// <summary>The recipe in effect: the Resources asset, or the code defaults (the bible's numbers).</summary>
        public static VegetationRecipe Load()
        {
            var asset = Resources.Load<VegetationRecipe>(ResourcesPath);
            if (asset != null) return asset;
            if (s_default == null)
            {
                s_default = CreateInstance<VegetationRecipe>();
                s_default.name = "DD_VegetationRecipe (code defaults)";
                s_default.hideFlags = HideFlags.HideAndDontSave;
            }

            return s_default;
        }

        public Species Get(VegetationType type)
        {
            int i = (int)type;
            if (species != null && i < species.Length && species[i] != null && species[i].type == type) return species[i];
            if (species != null)
            {
                for (int k = 0; k < species.Length; k++)
                {
                    if (species[k] != null && species[k].type == type) return species[k];
                }
            }

            return DefaultSpecies()[i];
        }

        public ZoneRecipe Zone(VegetationZone zone)
        {
            if (zones != null)
            {
                for (int i = 0; i < zones.Length; i++)
                {
                    if (zones[i] != null && zones[i].zone == zone) return zones[i];
                }
            }

            ZoneRecipe[] d = DefaultZones();
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i].zone == zone) return d[i];
            }

            return d[0];
        }

        public float TierDensity(int tier)
        {
            return tier <= 0 ? densityPerformance : tier >= 2 ? densityCinematic : densityHigh;
        }

        // ------------------------------------------------------------------ defaults (LOOK-BIBLE v1)

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        private static Species S(VegetationType t, int variants, float hMin, float hMax, float foot, float stiff, int root, int tip, int dry, int accent, int accentB,
            float dryFraction, float trans, bool lod2, float maxDensity, PlaceholderShape shape, int blades, int segments, float width)
        {
            return new Species
            {
                type = t, variants = variants, heightMin = hMin, heightMax = hMax, footprint = foot, stiffness = stiff,
                rootColor = Hex(root), tipColor = Hex(tip), dryColor = Hex(dry), accentColor = Hex(accent), accentColorB = Hex(accentB),
                dryFraction = dryFraction, translucency = trans, hasLod2 = lod2, maxDensity = maxDensity, shape = shape,
                placeholderBlades = blades, placeholderSegments = segments, placeholderBladeWidth = width
            };
        }

        public static Species[] DefaultSpecies()
        {
            Species[] s =
            {
                S(VegetationType.GrassShort, 3, 0.10f, 0.20f, 0.35f, 0f, 0x3E4A30, 0x626C48, 0x8A8668, 0x626C48, 0x626C48, 0.07f, 0.10f, false, 16f, PlaceholderShape.Blades, 14, 3, 0.022f),
                S(VegetationType.GrassMid, 2, 0.30f, 0.45f, 0.50f, 0f, 0x46512F, 0x55603E, 0x8A8668, 0x8A8668, 0x8A8668, 0.20f, 0.20f, true, 10f, PlaceholderShape.Blades, 12, 4, 0.024f),
                // LOOK-BIBLE v1.1 (AD 08.10, AP-031 Parti A M1): susuki stem #616448, dry tip #736E57, plume #737267 (value down: capsule − GT luma 0.110).
                S(VegetationType.GrassTall, 2, 0.70f, 1.00f, 0.60f, 0f, 0x52553D, 0x616448, 0x736E57, 0x737267, 0x737267, 0.50f, 0.30f, true, 4f, PlaceholderShape.Blades, 14, 5, 0.026f),
                S(VegetationType.Fern, 2, 0.40f, 0.60f, 0.70f, 0.5f, 0x3A4529, 0x4C5838, 0x6A5A44, 0x4C5838, 0x4C5838, 0.03f, 0.25f, true, 3f, PlaceholderShape.Fronds, 7, 5, 0.09f),
                S(VegetationType.LeafClump, 2, 0.20f, 0.35f, 0.50f, 0.5f, 0x3E4A30, 0x4E5C3C, 0x6A7258, 0x6A7258, 0x6A7258, 0.05f, 0.20f, true, 3f, PlaceholderShape.BroadLeaves, 6, 3, 0.16f),
                S(VegetationType.Moss, 3, 0.02f, 0.05f, 0.90f, 1f, 0x323C25, 0x3A462B, 0x3A462B, 0x3A462B, 0x3A462B, 0f, 0f, false, 3f, PlaceholderShape.Patch, 10, 1, 0f),
                S(VegetationType.Hydrangea, 2, 0.60f, 0.90f, 0.90f, 1f, 0x3A4630, 0x46543A, 0x46543A, 0x64728E, 0x8E7C8C, 0f, 0.20f, true, 4f, PlaceholderShape.FlowerShrub, 9, 3, 0.12f),
                S(VegetationType.SpiderLily, 1, 0.30f, 0.45f, 0.30f, 0.2f, 0x3E4A2E, 0x4E5A38, 0x4E5A38, 0x7C463C, 0x7C463C, 0f, 0.15f, false, 6f, PlaceholderShape.FlowerStems, 4, 4, 0.012f),
                S(VegetationType.Kumazasa, 2, 0.40f, 0.80f, 0.80f, 1f, 0x3C482E, 0x4A5838, 0x4A5838, 0xA9A690, 0xA9A690, 0f, 0.15f, true, 3f, PlaceholderShape.Bamboo, 7, 3, 0.06f),
                S(VegetationType.LeafLitter, 2, 0.004f, 0.012f, 0.60f, 1f, 0x6E4632, 0x86603E, 0x6A5A44, 0x86603E, 0x86603E, 0.15f, 0f, false, 4f, PlaceholderShape.Litter, 12, 1, 0.075f),
            };
            s[(int)VegetationType.LeafLitter].unitsPerInstance = 12f;
            s[(int)VegetationType.LeafLitter].hueJitterDegrees = 6f;
            s[(int)VegetationType.LeafLitter].farKeep = 0.2f;
            s[(int)VegetationType.Moss].farKeep = 0.6f;
            s[(int)VegetationType.Moss].saturationCap = 0.42f;   // LOOK-BIBLE v1.1 (AP-031 Parti B Karar 1.7)
            s[(int)VegetationType.GrassTall].farKeep = 0.6f;
            s[(int)VegetationType.Hydrangea].farKeep = 1f;
            s[(int)VegetationType.SpiderLily].farKeep = 1f;
            return s;
        }

        private static ZoneRecipe Z(VegetationZone zone, float ss, float gm, float gt, float fe, float lc, float mo, float hy, float sl, float kz, float ml, float heightScale = 1f, float heightCap = 0f)
        {
            return new ZoneRecipe { zone = zone, density = new[] { ss, gm, gt, fe, lc, mo, hy, sl, kz, ml }, heightScale = heightScale, heightCap = heightCap };
        }

        public static ZoneRecipe[] DefaultZones()
        {
            return new[]
            {
                //                          SS    GM    GT    FE    LC    MO    HY  SL  KZ    ML
                Z(VegetationZone.Lawn,             10f,  1.0f, 0f,   0.1f, 0.2f, 0.25f, 0f, 0f, 0f,   0f),
                Z(VegetationZone.Grove,            5f,   0f,   0f,   0.25f,0.4f, 0.6f,  0f, 0f, 0f,   0f, 0.8f),
                Z(VegetationZone.GroveSkirt,       8f,   1.5f, 0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.Bamboo,           0f,   0.2f, 0f,   0.3f, 0f,   0.4f,  0f, 0f, 1.2f, 0f),
                Z(VegetationZone.BambooEdge,       0f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 1.2f, 0f, 1f, 0.6f),
                Z(VegetationZone.BambooPathSide,   3f,   0f,   0f,   0f,   0f,   0.4f,  0f, 0f, 0f,   0f, 0.7f),
                Z(VegetationZone.PondShore,        0f,   1.0f, 0f,   0.3f, 0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.PondBank,         7f,   1.5f, 0f,   0f,   0f,   0.4f,  0f, 0f, 0f,   0f, 1f, 0.45f),
                Z(VegetationZone.PathEdge,         3f,   0f,   0f,   0f,   0f,   0.3f,  0f, 0f, 0f,   0f, 0.5f),
                Z(VegetationZone.WallBase,         0f,   1.0f, 2.0f, 0.4f, 0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.WallBaseShadow,   0f,   1.0f, 1.0f, 1.0f, 0f,   0f,    0f, 0f, 0.4f, 0f),
                Z(VegetationZone.WallOuter,        10f,  1.0f, 0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.BuildingSurround, 0f,   0.6f, 0f,   0f,   0.3f, 0f,    0f, 0f, 0f,   0f, 1f, 0.6f),
                Z(VegetationZone.BuildingShadow,   0f,   0f,   0f,   0.3f, 0f,   0.5f,  0f, 0f, 0.3f, 0f, 1f, 0.6f),
                Z(VegetationZone.Courtyard,        4f,   0f,   0f,   0f,   0f,   0.8f,  0f, 0f, 0f,   0f, 0.7f, 0.5f),
                Z(VegetationZone.PlazaEdge,        0f,   0f,   0f,   0f,   0f,   0.6f,  0f, 0f, 0f,   0f),
                Z(VegetationZone.BridgeApproach,   3f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f, 0.6f, 0.45f),
                Z(VegetationZone.MossCourt,        4f,   0f,   0f,   0f,   0f,   1.2f,  0f, 0f, 0f,   0f, 0.7f, 0.15f),
                Z(VegetationZone.Karesansui,       0f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.KaresansuiCollar, 0f,   0f,   0f,   0f,   0f,   1.0f,  0f, 0f, 0f,   0f, 1f, 0.05f),
                Z(VegetationZone.BareSoil,         0f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                // 08.10 (S-05): plaza joint tufts, base 0.3/m² (× mask_b.B: joint lines 255 ≈ ×2, stone faces 1 ≈ ×0), ≤ 12 cm.
                Z(VegetationZone.PlazaJoint,       0.2f, 0f,   0f,   0f,   0f,   0.1f,  0f, 0f, 0f,   0f, 0.8f, 0.12f),
                Z(VegetationZone.DesirePath,       7f,   0f,   0f,   0f,   0f,   0.2f,  0f, 0f, 0f,   0f, 0.7f),
            };
        }
    }
}
