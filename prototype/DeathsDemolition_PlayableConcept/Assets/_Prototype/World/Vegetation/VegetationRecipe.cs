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
            [Tooltip("Albedo saturation ceiling: HSV s of the sRGB albedo, the bible's unit (1 = none; the shader converts it for its linear albedo). AD v1.2: SS/GM 0.48, GT 0.42, FE/LC/MO/KZ 0.50.")]
            [Range(0f, 1f)] public float saturationCap = 1f;
            [Tooltip("w5 (10.10): linear red ≤ green × this on the final albedo — the yellow / brown blade texels toward green (0 = off: flowers, litter).")]
            [Range(0f, 1f)] public float yellowClamp;
            [Tooltip("AD v1.2 (10.10): tint (sRGB) on the kit family material copy (_BaseColor) — the one tone lever of a species whose atlas carries the colour (root / tip go white there). White = the atlas as authored (HY, SL, ML: the flowers and the litter keep their own colour).")]
            public Color kitTint = Color.white;
            [Tooltip("AD v1.2 (10.10) petal shift, per variant: target flower albedo (sRGB) of the masked atlas texels; alpha = amount (0 = the atlas as authored). A masked texel becomes target × its luma / petalRefLuma, so the atlas pattern is kept and the mean lands on the target.")]
            public Color[] petalColours = Array.Empty<Color>();
            [Tooltip("Per variant: mean linear luma of the masked atlas texels of that variant's mesh (measured on the AP-031 flora atlas).")]
            public float[] petalRefLuma = Array.Empty<float>();
            [Tooltip("Atlas UV rect (u0, v0, u1, v1) the petal mask is limited to.")]
            public Vector4 petalRect;
            [Tooltip("Petal mask on the linear texel: saturate(dot(rgb, xyz) × w) — HY (0, −1, 1) = b − g, SL (1, −1, 0) = r − g.")]
            public Vector4 petalKey;
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

        /// <remarks>
        /// AD v1.2 (10.10, user note "more purple / pink / lilac sprinkled in"): a tsuyu (rainy-season) garden from the kit
        /// that exists — no new species. Ajisai (HY) in one-colour clusters, 3 colours (A #695DA8 40 % water / shade, B #A35281
        /// 35 % sun, C #8B529E 25 %); hanashōbu (SL variants 1/2) in shore strips; kurinsō (SL variant 3) in shaded clusters
        /// with fuki (LC) leaves. The red higanbana strips (SL variant 0) are the v1 signature, unchanged. Every accent stays
        /// off the flower-forbidden ground (corridor, paths ≥ 0.8 m, plaza, karesansui, desire paths, bare soil, bridge
        /// approaches, spawn ≤ 3 m, weapon / door ≤ 2.5 m, under a crown 0–0.8 R; HY taller than 0.55 m keeps 1.5 m from
        /// the corridor).
        /// </remarks>
        [Serializable]
        public sealed class Accents
        {
            [Header("Hydrangea (HY) clusters — AD v1.2 (10.10)")]
            [Tooltip("World total of ajisai clusters (AD v1.2: 40–55; v1 30 at 8 m).")]
            // w4 (C-00 10.10, K5 a must: "purple / pink / mauve clusters readable in the TD frames"): w3's 48 clusters of 2–4 plants
            // read as single heads (0–1 cluster ≥ 4000 px per frame) — 120 clusters of 6–10 plants, Ø 1.6–2.4, groups of 2–4.
            public int hydrangeaClusters = 120;
            [Tooltip("A group's first cluster keeps this far (m, centre to centre) from every cluster placed before it.")]
            public float hydrangeaSpacing = 3.5f;
            [Tooltip("Cluster radius (m): Ø 1.6–2.4.")]
            public float hydrangeaRadiusMin = 0.8f, hydrangeaRadiusMax = 1.2f;
            public int hydrangeaPlantsMin = 6, hydrangeaPlantsMax = 10;
            [Tooltip("Clusters per asymmetric group.")]
            public int hydrangeaGroupMin = 2, hydrangeaGroupMax = 4;
            [Tooltip("Edge gap (m) between neighbouring clusters of one group.")]
            public float hydrangeaGroupGapMin = 0.3f, hydrangeaGroupGapMax = 1.0f;
            [Tooltip("Colour shares by variant: 0 A (water / shade), 1 B (sun), 2 C.")]
            public float[] hydrangeaShares = { 0.40f, 0.35f, 0.25f };
            [Tooltip("A cluster is wet / shaded within this distance (m) of water or of a building / wall shadow face.")]
            public float hydrangeaShadeReach = 4f;
            [Header("Spider lily (SL variant 0, higanbana) strips — the v1 signature, unchanged")]
            public int spiderLilyStripsMin = 6, spiderLilyStripsMax = 10;
            public float spiderLilySpacing = 6f, spiderLilyLengthMin = 1.5f, spiderLilyLengthMax = 3f, spiderLilyWidth = 0.3f;
            public int spiderLilyPerStripMin = 2, spiderLilyPerStripMax = 4;
            [Tooltip("The v1 SL layer scale (6/m²) clamped the strip density; kept so the higanbana strips stay as they were.")]
            public float spiderLilyStripDensityCap = 6f;
            [Tooltip("World rects (x0, z0, x1, z1) where strips may go (bible §1 SL Yerler).")]
            public Vector4[] spiderLilyRegions =
            {
                new Vector4(-20f, -42f, 0f, -38f),
                new Vector4(27f, -31f, 37f, -7f),
                new Vector4(-36f, -32f, -28f, -26f),
            };
            [Header("Hanashōbu (SL variants 1 / 2) shore strips — AD v1.2")]
            public int irisStripsMin = 18, irisStripsMax = 28;   // w4: 10–16 strips of 5–8 → 18–28 (K5)
            [Tooltip("Band (m from the water) the strips lie in (PondShore / PondBank). w6: 0.5–1.2 → 0.4–1.6 (a straight strip on a curved shore left the 0.7 m band).")]
            public float irisWaterMin = 0.4f, irisWaterMax = 1.6f;
            public float irisLengthMin = 1.2f, irisLengthMax = 2.5f, irisWidthMin = 0.4f, irisWidthMax = 0.6f;
            public int irisPerStripMin = 8, irisPerStripMax = 14;
            [Tooltip("Edge gap (m) between hanashōbu strips / to a higanbana strip.")]
            public float irisGap = 2f, irisHiganbanaGap = 4f;
            [Header("Kurinsō (SL variant 3) shade clusters — AD v1.2")]
            public int primroseClustersMin = 24, primroseClustersMax = 36;   // w4: 12–20 of 5–8 → 24–36 of 9–14, Ø 1.0–1.6 (K5)
            [Tooltip("Cluster radius (m): Ø 1.0–1.6.")]
            public float primroseRadiusMin = 0.5f, primroseRadiusMax = 0.8f;
            public int primrosePlantsMin = 9, primrosePlantsMax = 14;
            [Tooltip("Fuki (LC) leaves in a ring around the flowers.")]
            public int primroseLeavesMin = 3, primroseLeavesMax = 4;
            public float primroseLeafRing = 0.35f;
            [Tooltip("Centre distance (m) between kurinsō clusters.")]
            public float primroseSpacing = 3.5f;
            [Header("Flower ground (AD v1.2 exclusions)")]
            [Tooltip("Distance (m) a flower keeps from a path edge / a plaza edge.")]
            public float flowerPathClear = 0.8f, flowerPlazaClear = 1.5f;
            [Tooltip("Distance (m) from a spawn point / a weapon or door point (weapons and doors: their own clear radius + the difference to 2.5 m).")]
            public float flowerSpawnClear = 3f, flowerPickupClear = 2.5f;
            [Tooltip("w5 (10.10): flowers may stand inside the corridor only this close (m) to its edge — a border along the lanes, by water, walls and buildings (V6's corridor mask covers 100 % of the pond bank, 88 % of the building band, 80 % of the lawn).")]
            public float flowerCorridorMargin = 1.2f;
            [Tooltip("Within this distance (m) of the corridor a flower may stand no taller than flowerTallCorridorHeight.")]
            public float flowerTallCorridorDistance = 1.5f, flowerTallCorridorHeight = 0.55f;
            [Tooltip("Edge gap (m) between two different accents (no shared cells, no colour clash).")]
            public float flowerAccentGap = 1f;
            [Tooltip("Moss-court rim (m inside its edge) where ajisai may stand. An ajisai cell whose zone caps lower than mossCourtHydrangeaCap takes that cap (the court's 0.15 m would delete them; w4: the pond bank's 0.45 m squashed them to single heads).")]
            public float mossCourtRim = 1f, mossCourtHydrangeaCap = 0.7f;
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
        [Tooltip("Back-light transmission tone. AD v1.2 (10.10): #C8B47E → #9EC07E (the warm straw tone read as yellowing).")]
        public Color translucencyTone = Hex(0x9EC07E);
        [Header("Ground under grass (principle 1: soil ≥ 8 L* darker than the grass; P-05 08.10 §2)")]
        [Tooltip("Ground albedo × this at full grass cover (DD/GroundMoss reads _DD_VegCoverTex).")]
        [Range(0.4f, 1f)] public float groundAlbedoUnderGrass = 0.78f;
        [Tooltip("Ground occlusion (indirect diffuse + specular) at full cover — the specular occlusion ≈ ×0.45.")]
        [Range(0f, 1f)] public float groundOcclusionUnderGrass = 0.45f;
        [Tooltip("Ground smoothness × this at full cover (the direct back-lit specular).")]
        [Range(0f, 1f)] public float groundSmoothnessUnderGrass = 0.55f;
        [Header("Cover on paving (AD S1, 10.10)")]
        [Tooltip("The colour pass made the plaza / path / joint tufts read as green confetti on the stone: there the cover keeps its hue, saturation × this. w9: the AD's 0.65 / 0.85 left them at s 0.34 / v 0.36 (before the colour pass 0.32 / 0.17, with fewer tufts) — 0.5 / 0.6 for the calm the AD asked for.")]
        [Range(0f, 1f)] public float pavedCoverSaturation = 0.5f;
        [Tooltip("… and value × this.")]
        [Range(0f, 1f)] public float pavedCoverValue = 0.6f;
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

        /// <remarks>
        /// AD v1.2 (10.10, user note "the ground reads brown / yellow, the palette washed out"): the kit's 10 species all carry
        /// their colour in the AP-031 atlas, so the recipe's root / tip never reached the screen — the tone lever is
        /// <see cref="Species.kitTint"/> (SS / GM #DDFFDF, GT #E5FFE7, FE / LC / KZ #EDFFEE, MO #EDFFF1; HY / SL / ML white) with
        /// the saturation ceilings SS / GM 0.48, GT 0.42, FE / LC / MO / KZ 0.50 (MO from 0.42). Root / tip / dry below are the
        /// placeholder fallbacks, re-toned to the same greener palette (dry share down: SS 0.03, GM 0.08, GT 0.30, FE 0.02,
        /// LC 0.03); HY leaf #46543A → #3E5A35, KZ edge #A6AA9B.
        /// Flowers (AD v1.2 tsuyu garden): HY 3 variants = 3 ajisai colours on the kit models [A, B, A]; SL 4 variants =
        /// higanbana red (the atlas, unchanged) + hanashōbu A / B + kurinsō on the one kit model. The petal masks / reference
        /// lumas are measured on T_VEG_Flora_BaseColor (linear): HY heads b − g ≥ 0.037 on every opaque head texel (HA1/HA2,
        /// HB1/HB2 and the head part of the LOD2 cards D3/D4), ≤ −0.027 on leaves (HL) and stems (ST3); SL crowns r − g ≥
        /// 0.067 (SL1/SL2), stems (ST4) negative. Mean luma: HA heads (model A) 0.167, HB heads (model B) 0.221, SL crowns 0.089.
        /// maxDensity HY 16 / SL 16: the accents write one plant per 0.25 m cell (exact plant counts at High).
        /// </remarks>
        public static Species[] DefaultSpecies()
        {
            Species[] s =
            {
                S(VegetationType.GrassShort, 3, 0.10f, 0.20f, 0.35f, 0f, 0x2F4729, 0x547043, 0x7B8066, 0x626C48, 0x626C48, 0.03f, 0.10f, false, 16f, PlaceholderShape.Blades, 14, 3, 0.022f),
                S(VegetationType.GrassMid, 2, 0.30f, 0.45f, 0.50f, 0f, 0x344D2C, 0x516B40, 0x7B8066, 0x8A8668, 0x8A8668, 0.08f, 0.20f, true, 10f, PlaceholderShape.Blades, 12, 4, 0.024f),
                // LOOK-BIBLE v1.1 (AD 08.10, AP-031 Parti A M1): plume #737267 (value down: capsule − GT luma 0.110). AD v1.2: stem / tip / dry greener.
                S(VegetationType.GrassTall, 2, 0.70f, 1.00f, 0.60f, 0f, 0x405234, 0x566643, 0x727560, 0x737267, 0x737267, 0.30f, 0.30f, true, 4f, PlaceholderShape.Blades, 14, 5, 0.026f),
                S(VegetationType.Fern, 2, 0.40f, 0.60f, 0.70f, 0.5f, 0x2C4224, 0x486639, 0x60634F, 0x4C5838, 0x4C5838, 0.02f, 0.25f, true, 3f, PlaceholderShape.Fronds, 7, 5, 0.09f),
                S(VegetationType.LeafClump, 2, 0.20f, 0.35f, 0.50f, 0.5f, 0x374D2C, 0x577046, 0x6B7A5F, 0x6A7258, 0x6A7258, 0.03f, 0.20f, true, 3f, PlaceholderShape.BroadLeaves, 6, 3, 0.16f),
                S(VegetationType.Moss, 3, 0.02f, 0.05f, 0.90f, 1f, 0x354D29, 0x405731, 0x405731, 0x3A462B, 0x3A462B, 0f, 0f, false, 3f, PlaceholderShape.Patch, 10, 1, 0f),
                S(VegetationType.Hydrangea, 3, 0.60f, 0.90f, 0.90f, 1f, 0x3A4630, 0x3E5A35, 0x3E5A35, 0x64728E, 0x8E7C8C, 0f, 0.20f, true, 16f, PlaceholderShape.FlowerShrub, 9, 3, 0.12f),
                S(VegetationType.SpiderLily, 4, 0.30f, 0.45f, 0.30f, 0.2f, 0x3E4A2E, 0x4E5A38, 0x4E5A38, 0x7C463C, 0x7C463C, 0f, 0.15f, false, 16f, PlaceholderShape.FlowerStems, 4, 4, 0.012f),
                S(VegetationType.Kumazasa, 2, 0.40f, 0.80f, 0.80f, 1f, 0x364D2B, 0x4B663B, 0x4B663B, 0xA6AA9B, 0xA6AA9B, 0f, 0.15f, true, 3f, PlaceholderShape.Bamboo, 7, 3, 0.06f),
                S(VegetationType.LeafLitter, 2, 0.004f, 0.012f, 0.60f, 1f, 0x6E4632, 0x86603E, 0x6A5A44, 0x86603E, 0x86603E, 0.15f, 0f, false, 4f, PlaceholderShape.Litter, 12, 1, 0.075f),
            };
            s[(int)VegetationType.LeafLitter].unitsPerInstance = 12f;
            s[(int)VegetationType.LeafLitter].hueJitterDegrees = 6f;
            s[(int)VegetationType.LeafLitter].farKeep = 0.2f;
            s[(int)VegetationType.Moss].farKeep = 0.6f;
            s[(int)VegetationType.GrassTall].farKeep = 0.6f;
            s[(int)VegetationType.Hydrangea].farKeep = 1f;
            s[(int)VegetationType.SpiderLily].farKeep = 1f;

            // AD v1.2 (10.10): kit tint + saturation ceiling (HSV s, sRGB). w4 (S-05, measured on the w3 TD frames, colour_gate.py):
            // the v1.2 tints left the lit grass at hue 76–80° (yellow-green; K2 wants a 92–110° median) and P95 s 0.60 (≤ 0.50) —
            // red × 0.62 / blue × 1.05 more in linear (v1.2 tint × that) and the ceilings × ~0.83 bring the modelled frame to
            // ≈ 100° / P95 0.47–0.50. Luminance of a typical blade −6 %.
            // w5 (w4 frames: hue in band, P95 s 0.55–0.62 on the cover): ceilings × 0.8 (SS / GM 0.32, GT 0.29, others 0.34).
            Tone(s[(int)VegetationType.GrassShort], 0xB3FFE4, 0.32f);
            Tone(s[(int)VegetationType.GrassMid], 0xB3FFE4, 0.32f);
            Tone(s[(int)VegetationType.GrassTall], 0xB9FFEC, 0.29f);
            Tone(s[(int)VegetationType.Fern], 0xC0FFF3, 0.34f);
            Tone(s[(int)VegetationType.LeafClump], 0xC0FFF3, 0.34f);
            Tone(s[(int)VegetationType.Moss], 0xC0FFF6, 0.34f);
            Tone(s[(int)VegetationType.Kumazasa], 0xC0FFF3, 0.34f);

            // AD v1.2 (10.10) tsuyu garden: per-variant petal colours on the flora atlas (see remarks for the measurements).
            Species hy = s[(int)VegetationType.Hydrangea];
            // w6 (K4 beacon reserve s > .55 ∧ v > .80: the sunlit pink reached it in td6, 377 px): value × 0.85, pink chroma a touch down
            hy.petalColours = new[] { Hex(0x594F8F), Hex(0x8A4A6F), Hex(0x764686) };   // A water / shade, B sun, C (AD v1.2 #695DA8 / #A35281 / #8B529E)
            hy.petalRefLuma = new[] { 0.167f, 0.221f, 0.167f };                         // kit models [A (HA heads), B (HB heads), A]
            hy.petalRect = new Vector4(0.25f, 0.5f, 0.875f, 1f);                       // heads HA/HB (u .625–.875, v .75–1) ∪ LOD2 cards D3/D4
            hy.petalKey = new Vector4(0f, -1f, 1f, 40f);                               // b − g; 0.025 → full
            Species sl = s[(int)VegetationType.SpiderLily];
            sl.petalColours = new[] { new Color(0f, 0f, 0f, 0f), Hex(0x6C4A94), Hex(0x825C9E), Hex(0x8E4272) };   // higanbana (atlas), hanashōbu A / B, kurinsō (w6: #A14880 × 0.88, K4)
            sl.petalRefLuma = new[] { 0f, 0.089f, 0.089f, 0.089f };
            sl.petalRect = new Vector4(0f, 0.625f, 0.25f, 0.75f);                       // crowns SL1 / SL2
            sl.petalKey = new Vector4(1f, -1f, 0f, 30f);                                // r − g; 0.033 → full
            return s;
        }

        /// <summary>
        /// w5 (C-00 10.10, S-06 AD N2 "the dark hero costume melts into the dark ground", to be solved on the ground side): the TD
        /// cover's luminance × this (linear). The costume reads linear Y ≈ 0.009 against a shaded moss ring ≈ 0.03.
        /// </summary>
        public const float CoverLuminanceLift = 1.15f;
        /// <summary>w5: the green species' yellow clamp (linear red ≤ green × this; 0.45 ≈ sRGB 0.70).</summary>
        public const float GreenYellowClamp = 0.45f;

        private static void Tone(Species sp, int tint, float saturationCap)
        {
            // the kit tint is sRGB (SetColor linearises): lift in linear, back to sRGB (> 1 allowed — _BaseColor is a multiplier)
            Color lin = Hex(tint).linear * CoverLuminanceLift;
            lin.a = 1f;
            sp.kitTint = lin.gamma;
            sp.saturationCap = saturationCap;
            sp.yellowClamp = GreenYellowClamp;
        }

        /// <summary>
        /// The variant's petal shift for the shader (_PetalShift): xyz = the target in linear RGB ÷ the variant's reference
        /// luma, w = amount; zero = the atlas as authored (no colours, amount 0 or no reference). Load time only.
        /// </summary>
        public static Vector4 PetalShift(Species sp, int variant)
        {
            if (sp == null || sp.petalColours == null || variant < 0 || variant >= sp.petalColours.Length) return Vector4.zero;
            Color c = sp.petalColours[variant];
            float amount = Mathf.Clamp01(c.a);
            float reference = sp.petalRefLuma != null && variant < sp.petalRefLuma.Length ? sp.petalRefLuma[variant] : 0f;
            if (amount <= 0f || reference <= 1e-4f) return Vector4.zero;
            Color lin = c.linear;
            return new Vector4(lin.r / reference, lin.g / reference, lin.b / reference, amount);
        }

        private static ZoneRecipe Z(VegetationZone zone, float ss, float gm, float gt, float fe, float lc, float mo, float hy, float sl, float kz, float ml, float heightScale = 1f, float heightCap = 0f)
        {
            return new ZoneRecipe { zone = zone, density = new[] { ss, gm, gt, fe, lc, mo, hy, sl, kz, ml }, heightScale = heightScale, heightCap = heightCap };
        }

        /// <remarks>
        /// R3 10.10 (S-05, user item 1 "the map full, the ground in 3D grass and plants"; K/wp-092/evidence/s05-1010/ANALIZ.md): the
        /// ground-id measure found 70 % of the bare vegetation ground where short grass was 0 — BuildingSurround (5.6 points of the
        /// 8-pose mean, 49 % of its visible cells bare), MossCourt 2.6, Bamboo 1.4, PondBank 1.3, Lawn 1.0. Filled within the bible's
        /// language: BuildingSurround SS 0 → 6 + MO 0.4 (the 0.6 m cap keeps the base readable; the amaochi drip strip stays
        /// BareSoil), BuildingShadow SS 0 → 3, MossCourt MO 1.2 → 2.4 + SS 4 → 6 (a koke-niwa carpet), Bamboo floor ML 0 → 12
        /// (1 litter patch/m²) + MO 0.4 → 0.6, PondBank SS 7 → 9, Lawn SS 10 → 12 + MO 0.25 → 0.4, Courtyard SS 4 → 6, plaza joints
        /// SS 0.2 → 0.5 + MO 0.1 → 0.3 (≤ 12 cm tufts). Paths, paving, desire paths and karesansui stay as designed.
        /// w4 (C-00 10.10, ANALIZ-w3.md: the veg-ground gate measured 54 % bare, BuildingSurround 39 % of its visible cells,
        /// MossCourt 43 %, the bamboo floor 85 %, PondBank / Lawn 27 %; flat moss texture counts bare, the answer is 3D tufts):
        /// BuildingSurround SS 6 → 8, GM 0.6 → 2.5 (twice the footprint per instance), MO 0.4 → 0.8; MossCourt SS 6 → 9, MO 2.4 →
        /// 3.0 (the species' quantisation ceiling); bamboo floor FE 0.3 → 0.8, MO 0.6 → 1.2, KZ 1.2 → 2.4 (sasa); PondBank SS 9 → 11,
        /// GM 1.5 → 2; Lawn SS 12 → 13, GM 1 → 1.5; Courtyard SS 6 → 9.
        /// </remarks>
        public static ZoneRecipe[] DefaultZones()
        {
            return new[]
            {
                //                          SS    GM    GT    FE    LC    MO    HY  SL  KZ    ML
                Z(VegetationZone.Lawn,             13f,  1.5f, 0f,   0.1f, 0.2f, 0.4f,  0f, 0f, 0f,   0f),
                Z(VegetationZone.Grove,            5f,   0f,   0f,   0.25f,0.4f, 0.6f,  0f, 0f, 0f,   0f, 0.8f),
                Z(VegetationZone.GroveSkirt,       8f,   1.5f, 0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.Bamboo,           0f,   0.2f, 0f,   0.8f, 0f,   1.6f,  0f, 0f, 2.4f, 4f),   // w7: litter 12 → 4, moss 1.2 → 1.6 (td3 K1 / K3 / K7)
                Z(VegetationZone.BambooEdge,       0f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 1.2f, 0f, 1f, 0.6f),
                Z(VegetationZone.BambooPathSide,   3f,   0f,   0f,   0f,   0f,   0.4f,  0f, 0f, 0f,   0f, 0.7f),
                Z(VegetationZone.PondShore,        0f,   1.0f, 0f,   0.3f, 0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.PondBank,         11f,  2.0f, 0f,   0f,   0f,   0.4f,  0f, 0f, 0f,   0f, 1f, 0.45f),
                Z(VegetationZone.PathEdge,         3f,   0f,   0f,   0f,   0f,   0.3f,  0f, 0f, 0f,   0f, 0.5f),
                Z(VegetationZone.WallBase,         0f,   1.0f, 2.0f, 0.4f, 0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.WallBaseShadow,   0f,   1.0f, 1.0f, 1.0f, 0f,   0f,    0f, 0f, 0.4f, 0f),
                Z(VegetationZone.WallOuter,        10f,  1.0f, 0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.BuildingSurround, 8f,   2.5f, 0f,   0f,   0.3f, 0.8f,  0f, 0f, 0f,   0f, 1f, 0.6f),
                Z(VegetationZone.BuildingShadow,   3f,   0f,   0f,   0.3f, 0f,   0.5f,  0f, 0f, 0.3f, 0f, 1f, 0.6f),
                Z(VegetationZone.Courtyard,        9f,   0f,   0f,   0f,   0f,   0.8f,  0f, 0f, 0f,   0f, 0.7f, 0.5f),
                Z(VegetationZone.PlazaEdge,        0f,   0f,   0f,   0f,   0f,   0.6f,  0f, 0f, 0f,   0f),
                Z(VegetationZone.BridgeApproach,   3f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f, 0.6f, 0.45f),
                Z(VegetationZone.MossCourt,        9f,   0f,   0f,   0f,   0f,   3.0f,  0f, 0f, 0f,   0f, 0.7f, 0.15f),
                Z(VegetationZone.Karesansui,       0f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                Z(VegetationZone.KaresansuiCollar, 0f,   0f,   0f,   0f,   0f,   1.0f,  0f, 0f, 0f,   0f, 1f, 0.05f),
                Z(VegetationZone.BareSoil,         0f,   0f,   0f,   0f,   0f,   0f,    0f, 0f, 0f,   0f),
                // 08.10 (S-05): plaza joint tufts, base 0.3/m² (× mask_b.B: joint lines 255 ≈ ×2, stone faces 1 ≈ ×0), ≤ 12 cm.
                Z(VegetationZone.PlazaJoint,       0.5f, 0f,   0f,   0f,   0f,   0.3f,  0f, 0f, 0f,   0f, 0.8f, 0.12f),
                Z(VegetationZone.DesirePath,       7f,   0f,   0f,   0f,   0f,   0.2f,  0f, 0f, 0f,   0f, 0.7f),
            };
        }
    }
}
