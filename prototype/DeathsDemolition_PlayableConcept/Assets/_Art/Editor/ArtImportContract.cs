using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Import Contract v1 (ADR-011-1). The single source of truth for how a
    /// model or texture under <c>Assets/_Art</c> is imported. <see cref="ArtAssetPostprocessor"/>
    /// applies it on every import; the EditMode asset gate (<c>ArtAssetGateTests</c>) verifies
    /// every asset against <see cref="ValidateModel"/> / <see cref="ValidateTexture"/> so a
    /// hand-edited importer can never ship silently.
    ///
    /// WHY THE VALUES ARE WHAT THEY ARE (measured in the WP-011 import study,
    /// evidence/study1-defaults/import-study.json):
    ///  - AP-001 FBX files (Blender 5.2, axis_forward -Z / axis_up Y, FBX_SCALE_UNITS,
    ///    embed_textures) import at file scale 1.0 = 1 m, so <c>useFileScale</c> stays on and
    ///    <c>globalScale</c> is 1.
    ///  - Axis (measured, evidence/study1-defaults and study2-contract): with the default
    ///    importer the Mesh sub-asset holds (-bx, by, bz) for a Blender vertex (bx, by, bz)
    ///    and the node carries the exporter's -90° X rotation, so the model lands at
    ///    (-bx, bz, -by) — a 180° yaw away from the AP-001 world contract "Blender X = Unity X
    ///    (east), Blender Y = Unity Z (north), Blender Z = Unity Y (up)" = (bx, bz, by). With
    ///    <c>bakeAxisConversion</c> the Mesh sub-asset becomes (bx, by, -bz) and the node
    ///    carries +90° about X, which lands exactly on the contract: Rx(+90°)·(bx, by, -bz) =
    ///    (bx, bz, by). The family prefab therefore uses the Mesh sub-assets directly under a
    ///    geometry child rotated by the exact <see cref="GeometryRotation"/> (the importer's
    ///    own node shows 89.98° from float drift; the exact quaternion is used instead).
    ///    Nothing is mirrored; chirality is preserved.
    ///  - Embedded textures are never extracted: the FBX only carries BaseColor+Normal and
    ///    materials are authored from the 14 stand-alone AP-001 PNGs, so
    ///    <c>materialImportMode = None</c> (no Material sub-asset, no Texture2D sub-asset).
    ///  - Tangents are recomputed with MikkTSpace (same algorithm Blender used). AP-003 asks
    ///    that a recomputed basis is never reported as the native one; it is not.
    ///  - Read/Write stays off: colliders are primitives built from part bounds, NavMesh is
    ///    baked from physics colliders, and nothing on the CPU reads vertex data at runtime.
    /// </summary>
    public static class ArtImportContract
    {
        public const string Version = "ADR-011-1 Import Contract v1 (2026-09-15; WP-012: cutout BaseColor alpha, ADR-012-2; WP-015: texture class table, ADR-015-1; WP-016: v1.1 skinned characters, ADR-016-1)";

        /// <summary>WP-016 · Skinned character assets live here (ADR-016-1): <c>Characters/Player</c>, <c>Characters/Zombie</c>.</summary>
        public const string CharactersRoot = ArtRoot + "/Characters";
        /// <summary>WP-029 · Only models under here take generated lightmap UVs (Contract v1.2); weapons, VFX props and characters never sit in a lightmap.</summary>
        public const string EnvironmentRoot = ArtRoot + "/Environment";

        /// <summary>WP-038 · AP-015P surface plates and water meshes (probe-lit by contract, huge flat charts) skip generated lightmap UVs.</summary>
        public const string SurfacesRoot = EnvironmentRoot + "/Ground/AP015";
        public const string WaterRoot = EnvironmentRoot + "/Water/AP015";

        public static bool IsSurfaceModel(string assetPath)
        {
            string p = assetPath != null ? assetPath.Replace('\\', '/') : string.Empty;
            return p.StartsWith(SurfacesRoot + "/", StringComparison.Ordinal) || p.StartsWith(WaterRoot + "/", StringComparison.Ordinal);
        }

        public static bool WantsLightmapUv(string assetPath)
        {
            return ModelGenerateSecondaryUV && assetPath != null && assetPath.Replace('\\', '/').StartsWith(EnvironmentRoot + "/", StringComparison.Ordinal) && !IsSurfaceModel(assetPath);
        }

        /// <summary>Every asset below this folder is under contract.</summary>
        public const string ArtRoot = "Assets/_Art";

        /// <summary>
        /// Rotation the family prefab applies to the imported Mesh sub-assets so prefab space
        /// equals the AP-001 world contract (see class remarks): +90° about X, exactly.
        /// </summary>
        public static Quaternion GeometryRotation => Quaternion.Euler(GeometryRotationEulerX, 0f, 0f);

        public const float GeometryRotationEulerX = 90f;

        // ----------------------------------------------------------------- models

        public const float ModelGlobalScale = 1f;
        public const bool ModelUseFileScale = true;
        public const bool ModelBakeAxisConversion = true;

        /// <summary>
        /// WP-042B: the P-06 hand-held weapon exports (AP-013 under <c>Weapons/Classic</c>) are authored
        /// for the DEFAULT importer frame (root = grip, +Z barrel, +X port — measured: the AP-009C
        /// blockouts import correctly without baking, and baking mirrors Z); they skip the world
        /// axis bake. Every other model keeps <see cref="ModelBakeAxisConversion"/>.
        /// </summary>
        public static bool BakesAxis(string assetPath)
        {
            return assetPath == null || !assetPath.StartsWith(ArtRoot + "/Weapons/Classic/", StringComparison.Ordinal);
        }
        public const ModelImporterNormals ModelNormals = ModelImporterNormals.Import;
        public const ModelImporterTangents ModelTangents = ModelImporterTangents.CalculateMikk;
        public const ModelImporterMeshCompression ModelMeshCompression = ModelImporterMeshCompression.Off;
        /// <summary>WP-038 (ADR-015-1 v4) · Environment models ship with Medium mesh compression (vertex / normal / tangent / UV quantised; Meshes were 31 % of the build); skinned characters stay Off (ArtCharacterContract — normal / tangent precision on the deforming surface).</summary>
        public const ModelImporterMeshCompression EnvironmentMeshCompression = ModelImporterMeshCompression.Medium;

        public static ModelImporterMeshCompression MeshCompressionOf(string assetPath)
        {
            return assetPath != null && assetPath.Replace('\\', '/').StartsWith(EnvironmentRoot + "/", StringComparison.Ordinal) ? EnvironmentMeshCompression : ModelMeshCompression;
        }
        public const bool ModelIsReadable = false;

        /// <summary>
        /// WP-038E · The nine AP-015P ground plates stay CPU-readable: their LOD0 vertex colour (AP015_Mask, A = wetness) seeds the
        /// AP-018 puddle decal projectors at import (<c>ArtSurfaceImporter.BuildPuddleDecals</c>). ~55 k vertices, no other model is readable.
        /// </summary>
        public static bool ModelIsReadableOf(string assetPath)
        {
            return assetPath != null && System.IO.Path.GetFileName(assetPath).StartsWith("AP015_ground_", StringComparison.Ordinal) && IsSurfaceModel(assetPath);
        }
        public const ModelImporterIndexFormat ModelIndexFormat = ModelImporterIndexFormat.Auto;
        public const bool ModelOptimizeMeshPolygons = true;
        public const bool ModelOptimizeMeshVertices = true;
        public const bool ModelWeldVertices = true;
        /// <summary>
        /// WP-029 (ADR-029-2): lightmap UVs are generated for every ENVIRONMENT model (the AP
        /// exports carry no UV2; the world v2 bakes lightmaps). Characters stay without (skinned,
        /// probe-lit; <c>ArtCharacterContract</c> overrides to false). WP-011..017 imported with false.
        /// </summary>
        public const bool ModelGenerateSecondaryUV = true;
        public const bool CharacterGenerateSecondaryUV = false;
        public const float ModelSecondaryUVHardAngle = 88f;
        // Chart margin is computed for the bake resolution (ADR-029-2) instead of a fixed texel count at a
        // 1024 lightmap: a fixed 4 left 0.5 texel between the charts of a 128² object at bake2 (1096 overlaps).
        public const ModelImporterSecondaryUVMarginMethod ModelSecondaryUVMarginMethod = ModelImporterSecondaryUVMarginMethod.Calculate;
        public const float ModelSecondaryUVMinLightmapResolution = 8f;    // texels/m — the LOWEST resolution the margin must survive: ½ × ArtLightingBake.LightmapResolution (at 16 the margin left 122 sub-texel chart gaps, at 32 it shrank and left 299 — bake5/bake6)
        public const float ModelSecondaryUVMinObjectScale = 1f;
        public const float ModelSecondaryUVAngleDistortion = 8f;
        public const float ModelSecondaryUVAreaDistortion = 15f;
        public const bool ModelSwapUVChannels = false;
        public const ModelImporterMaterialImportMode ModelMaterialImportMode = ModelImporterMaterialImportMode.None;
        public const ModelImporterAnimationType ModelAnimationType = ModelImporterAnimationType.None;
        public const bool ModelImportAnimation = false;
        public const bool ModelImportBlendShapes = false;
        public const bool ModelImportCameras = false;
        public const bool ModelImportLights = false;
        public const bool ModelImportVisibility = false;
        public const bool ModelPreserveHierarchy = false;
        public const bool ModelKeepQuads = false;

        // --------------------------------------------------------------- textures

        /// <summary>Texture map role, decided from the file name (<c>T_&lt;role&gt;_&lt;Map&gt;.png</c>).</summary>
        public enum TextureRole
        {
            /// <summary>Not a contract texture name; imported with the BaseColor rules and flagged by the gate.</summary>
            Unknown = 0,
            /// <summary>sRGB albedo.</summary>
            BaseColor = 1,
            /// <summary>Tangent-space normal, OpenGL +Y, linear.</summary>
            Normal = 2,
            /// <summary>AP packed map (R unused, G roughness, B metallic). Linear. Kept for provenance; not bound to a material.</summary>
            Packed = 3,
            /// <summary>Derived URP mask: R metallic, A smoothness. Linear. WP-016 character sets also carry AO in G (URP occlusion channel).</summary>
            MetallicSmoothness = 4,
            /// <summary>WP-016 · AP-007 ID/tint mask (R skin, G cloth, B accessory, A emissive-permitted). Linear, alpha kept; provenance + DetailMask source.</summary>
            Mask = 5,
            /// <summary>WP-016 · Derived URP detail mask: A = cloth (Mask.G) so a version tint reaches cloth only. Linear, alpha kept.</summary>
            DetailMask = 6,
            /// <summary>WP-037 · AP-014 VFX sheet (flipbook / card / tile): sRGB colour with its alpha kept (<c>alphaIsTransparency</c>). Always the Vfx class.</summary>
            Sheet = 7
        }

        /// <summary>
        /// WP-015 · ADR-015-1 texture policy. Every contract texture belongs to a distance class
        /// decided by the role's most demanding use in the AP-005 world (WP-014 placement: a
        /// role shared by a paving plate and a prop takes the paving class). The class fixes the
        /// shipped maximum size and whether the mips stream. The numbers are the gameplay
        /// camera's: 19.9 m / −49° at 1080p resolves ~55–70 px per metre, so a 4 m tile is
        /// sampled at mip ≥ 2 of a 2048 map even on the ground; 1024 on walls and foliage and
        /// 512 on props lose nothing the fixed camera can show. Measured before the policy
        /// (WP-014, all 63 shipped maps at 2048): 337 MB in the build, +315 MB resident.
        /// </summary>
        public enum TextureClass
        {
            /// <summary>Tiling ground and paving: 2048.</summary>
            Ground = 0,
            /// <summary>Walls, timber, roofs, module surfaces: 1024.</summary>
            Structure = 1,
            /// <summary>Foliage atlases, bark, moss, culms: 1024.</summary>
            Foliage = 2,
            /// <summary>Dressing props (lanterns, benches, crates): ≤ 512. WP-017: the six AP-005B prop roles (prop_*).</summary>
            Prop = 3,
            /// <summary>Breakables: ≤ 512. Reserved; no role today.</summary>
            Breakable = 4,
            /// <summary>WP-037 · VFX sheets (<see cref="TextureRole.Sheet"/>): 1024, never streamed (an effect shows on its first frame; streaming would show the lowest mip first).</summary>
            Vfx = 7,
            /// <summary>WP-042B · weapon atlases (AP-013 classic set, AP-014 Special Weapon set): 1024, held at character distance, streamed like everything else.</summary>
            Weapon = 8,
            /// <summary>The 4×256 sky gradient (ArtSkyBuilder): 256, never streamed (a skybox has no renderer for the streaming system to rate).</summary>
            Sky = 5,
            /// <summary>WP-016 · Skinned characters (player, zombie atlas): 2048 — the one thing the fixed camera frames at 100 px and the player stares at (ADR-016-1).</summary>
            Character = 6
        }

        public static int MaxSizeOf(TextureClass c)
        {
            switch (c)
            {
                case TextureClass.Ground: return 2048;
                case TextureClass.Structure: return 1024;
                case TextureClass.Foliage: return 1024;
                case TextureClass.Prop: return 512;
                case TextureClass.Breakable: return 512;
                case TextureClass.Sky: return 1024;   // WP-038E (2): the AP-019 sky band lat-long (was 256 for the 4×256 gradient)
                case TextureClass.Character: return 2048;
                case TextureClass.Vfx: return 1024;
                case TextureClass.Weapon: return 1024;
                default: throw new ArgumentOutOfRangeException(nameof(c));
            }
        }

        /// <summary>Mipmap streaming is on for every class that a renderer samples; the sky is not one.</summary>
        public static bool StreamsMipmaps(TextureClass c)
        {
            return c != TextureClass.Sky && c != TextureClass.Vfx;
        }

        // WP-075 C1 (ADR-015-1 v3): the texture streaming budget is per quality tier — DD.Prototype.Core.QualityTiers.StreamingBudgetMb (512 / 1024 / 2048 MB).

        /// <summary>ADR-015-1 resident ceiling for the art texture set (probe counter delta against the procedural Run), MB.</summary>
        public const int ArtTextureMemoryBudgetMb = 160;

        /// <summary>
        /// WP-017 · ADR-015-1 v2 shipped-bytes budgets (PNG-header estimate at the class sizes,
        /// full mip chain, every map a material binds): the world set (Ground / Structure /
        /// Foliage / Prop / Breakable / Sky) ≤ 120 MB, the character set (Character class:
        /// player, zombie atlas, armour atlas) ≤ 80 MB, total ≤ 200 MB. WP-016 measured 158.7 MB
        /// against the single 160 MB line with the character set at 48 MB; the armour atlas
        /// (+22.4 MB at 2048) and the AP-005B props (+6.3 MB at 512) needed the split.
        /// If the character set exceeds its line the armour atlas drops to 1024 (technical rule, reported).
        /// </summary>
        /// <summary>v5 (WP-038E): 120 → 125 — the AP-018 wet inputs (2.6 MB at 512²) and the AP-019 sky band; the character line keeps its 35 MB headroom, the 220 total holds.</summary>
        public const int ShippedWorldBudgetMb = 125;
        public const int ShippedCharacterBudgetMb = 100;   // v3 (WP-020, C-00 technical decision): the AP-007 second cloth atlas (3 × 2K maps)
        /// <summary>WP-042B (ADR-015-1 v4 line, S-05 handshake): the weapon set — AP-014 Special Weapon atlas + AP-013 classic atlas (1K each, BaseColor + Normal + derived MS) on its own line (moved out of the character set).</summary>
        public const int ShippedWeaponBudgetMb = 12;
        public const int ShippedTotalBudgetMb = 220;       // v3 (WP-020); the weapon set fits inside it (measured 201 MB with the weapon line at 8.0)

        /// <summary>
        /// ADR-015-2 release build ceiling, MB (Editor build report "Complete build size").
        /// WP-017 (ADR-015-1 v2 note): 400 → 450. Measured 297 (WP-015) → 373 (WP-016 characters)
        /// → 425.5 (WP-017 armour + props + world v2, after heavy roof meshes left static batching:
        /// 507 with them batched). The texture budgets hold (world 116.8 / 120, character 69.3 / 80);
        /// the growth is content (meshes + character atlases), not policy drift.
        /// </summary>
        public const int ReleaseBuildBudgetMb = 450;

        public static bool IsCharacterClass(TextureClass c)
        {
            return c == TextureClass.Character;
        }

        /// <summary>WP-042B: the weapon set (its own budget line; neither world nor character).</summary>
        public static bool IsWeaponClass(TextureClass c)
        {
            return c == TextureClass.Weapon;
        }

        /// <summary>
        /// Role slug → class (ADR-015-1 table). Roles are the <c>&lt;role&gt;</c> of
        /// <c>T_&lt;role&gt;_&lt;Map&gt;.png</c>; every map of a role shares the class.
        /// A role missing here is a contract violation (the gate reports it) and imports at the
        /// Prop size so an unclassified map can never ship at 2048 by accident.
        /// </summary>
        private static readonly Dictionary<string, TextureClass> ClassOfRoleTable = new Dictionary<string, TextureClass>(StringComparer.Ordinal)
        {
            // Ground / paving (AP-005 ground + hero_earth plates, courtyard paving, curbs, shore gravel).
            { "earth", TextureClass.Ground },
            { "gravel", TextureClass.Ground },
            { "stone", TextureClass.Ground },
            // WP-038 · AP-015P water v2 inputs (1024 / 512 sources; Ground class, streamed; the ring sheet is a VFX Sheet).
            { "water_large", TextureClass.Ground },
            { "water_small", TextureClass.Ground },
            { "water_flow", TextureClass.Ground },
            { "water_foam", TextureClass.Ground },
            { "water_sparkle", TextureClass.Ground },
            { "water_caustic", TextureClass.Ground },
            { "water_rings", TextureClass.Vfx },
            // WP-038E · AP-018 Look B wet surfaces: wet stone albedo + wet packed maps (derived MetallicSmoothness like the dry tiles),
            // the damp / puddle tiles and the koi-pond shore wetness map (bound masks), the puddle decal sheet + normal (Vfx-class sheet).
            { "wet_stone", TextureClass.Ground },
            { "wet_earth", TextureClass.Ground },
            { "wet_gravel", TextureClass.Ground },
            { "wet_damp", TextureClass.Ground },
            { "wet_puddle", TextureClass.Ground },
            { "wet_shore", TextureClass.Ground },
            { "decal_puddle", TextureClass.Vfx },
            // WP-038E (2) · AP-019 sky / horizon plates: three unlit horizon sheets (alpha, 1024 cap) and the overcast sky band (Sky class source of the derived lat-long).
            { "horizon_near", TextureClass.Vfx },
            { "horizon_mid", TextureClass.Vfx },
            { "horizon_far", TextureClass.Vfx },
            { "skyband", TextureClass.Sky },
            // Structure (palace timber/screens, plaster, roofs, shore-path and basin module surfaces).
            { "wood", TextureClass.Structure },
            { "plaster", TextureClass.Structure },
            { "plaster_hf003", TextureClass.Structure },
            { "roof", TextureClass.Structure },
            { "earth_01", TextureClass.Structure },
            { "earth_02", TextureClass.Structure },
            { "earth_03", TextureClass.Structure },
            // Foliage / atlas (AP-003 sets, AP-001 bark, AP-003B culm).
            { "bark", TextureClass.Foliage },
            { "bark_01", TextureClass.Foliage },
            { "bark_02", TextureClass.Foliage },
            { "bark_03", TextureClass.Foliage },
            { "moss_01", TextureClass.Foliage },
            { "moss_02", TextureClass.Foliage },
            { "moss_03", TextureClass.Foliage },
            { "green_foliage_01", TextureClass.Foliage },
            { "green_foliage_02", TextureClass.Foliage },
            { "green_foliage_03", TextureClass.Foliage },
            { "sakura_foliage_01", TextureClass.Foliage },
            { "sakura_foliage_02", TextureClass.Foliage },
            { "sakura_foliage_03", TextureClass.Foliage },
            { "bamboo_culm", TextureClass.Foliage },
            // Sky gradient.
            { "sky", TextureClass.Sky },
            // WP-016 characters (ADR-016-1): the AP-006 player set (three skin albedos + shared normal/ORM) and the AP-007 zombie atlas.
            { "player", TextureClass.Character },
            { "player_skin1", TextureClass.Character },
            { "player_skin2b", TextureClass.Character },   // WP-017: skin 2 = 2b Steel/Indigo (user 2026-09-16); 2a retired
            { "player_skin3", TextureClass.Character },
            { "zombie_atlas", TextureClass.Character },
            { "zombie_cloth", TextureClass.Character },    // WP-020 · AP-007 v002 second (cloth) atlas
            // WP-017 · AP-008 armour atlas (one 2K set for every family / variant / state).
            { "zombie_armor", TextureClass.Character },
            // WP-036 · AP-014 tier silhouette parts atlas (1K set; S-03, one-line handshake with S-05).
            { "zombie_tier", TextureClass.Character },
            // WP-037 / WP-042B · weapon atlases (1K sets held at character distance; S-02, one-line handshake with S-05):
            // AP-014 Special Weapons and the AP-013 classic A/B/C set.
            { "sw_atlas", TextureClass.Weapon },
            { "weapons", TextureClass.Weapon },
            // WP-047E · AP-013C casings (separate 256 set; the weapons atlas is full — P-06 note): Weapon class.
            { "casings", TextureClass.Weapon },
            // WP-017 · AP-005B prop families (Tripo H3.1 sets; the first Prop-class roles) and the generated roof course overlay.
            { "prop_tsukubai", TextureClass.Prop },
            { "prop_handcart", TextureClass.Prop },
            { "prop_strawbales", TextureClass.Prop },
            { "prop_barrels", TextureClass.Prop },
            { "prop_mossrock", TextureClass.Prop },
            { "prop_shed", TextureClass.Prop },
            // WP-038I · AP-017 Phase 2 props (Higgsfield → Tripo H3.1, one 1K set each): Prop class 512, AP-005B path.
            { "prop_ofuda_post", TextureClass.Prop },
            { "prop_drying_rack_cloth", TextureClass.Prop },
            { "prop_well_head_tsurube", TextureClass.Prop },
            { "prop_breakable_sake_jar", TextureClass.Prop },
            // WP-038L · AP-017 Phase 2b props (1K sets → Prop 512; the fish trap's LOD albedo and the koi RGBA card sheet are BaseColor-only roles).
            { "prop_kagura_mask_stand", TextureClass.Prop },
            { "prop_breakable_tea_set", TextureClass.Prop },
            { "prop_fish_trap", TextureClass.Prop },
            { "prop_fish_trap_lod", TextureClass.Prop },
            { "prop_furin_wind_chime", TextureClass.Prop },
            { "prop_hishaku_ladle_rack", TextureClass.Prop },
            { "prop_koi_shoal", TextureClass.Prop },
            { "roof_courses", TextureClass.Structure }
        };

        public static IEnumerable<KeyValuePair<string, TextureClass>> TextureClassTable => ClassOfRoleTable;

        /// <summary>The class of a role slug, or null when the role is not in the ADR-015-1 table.</summary>
        public static TextureClass? ClassOfRole(string roleSlug)
        {
            return roleSlug != null && ClassOfRoleTable.TryGetValue(roleSlug, out TextureClass c) ? c : (TextureClass?)null;
        }

        /// <summary>The class the contract assigns a texture path: Vfx for a <see cref="TextureRole.Sheet"/>, else its role's table class; null when the role is unclassified.</summary>
        public static TextureClass? ClassifiedClassOf(string assetPath)
        {
            return RoleOf(assetPath) == TextureRole.Sheet ? TextureClass.Vfx : ClassOfRole(RoleSlugOf(assetPath));
        }

        /// <summary>The class a texture imports with: its classified class, Prop for an unclassified role.</summary>
        public static TextureClass ClassOf(string assetPath)
        {
            return ClassifiedClassOf(assetPath) ?? TextureClass.Prop;
        }

        /// <summary>WP-038 (ADR-015-1 v4) · Derived masks (MetallicSmoothness, DetailMask) ship at most 1024²: the metallic /
        /// smoothness / occlusion / cloth-gate channels carry no detail the gameplay camera resolves beyond that, and they
        /// were 31 % of the 2048 texture bytes (WP-039 build report). Albedo and normal keep their class size.</summary>
        public const int DerivedMaskMaxSize = 1024;

        public static bool IsDerivedMaskRole(TextureRole role)
        {
            return role == TextureRole.MetallicSmoothness || role == TextureRole.DetailMask;
        }

        /// <summary>
        /// WP-038H (DEC-086 growth gate, main release 551.8 MB): the packed source masks (<c>_Mask</c>: metallic / AO / smoothness /
        /// cloth-gate channels) ship under the same 1024² cap as the masks derived from them. They were budgeted as provenance-only
        /// (WP-016), but the tier tint materials bind them as <c>_TierMask</c> (WP-036), so the two AP-007/AP-008 atlases shipped at
        /// 2048² (5.3 MB each) next to their 1024² derived maps; a lerp mask on a 2K atlas resolves nothing beyond 1024 at the
        /// gameplay rig. Albedo and normal keep their class size.
        /// </summary>
        public static bool IsCappedMaskRole(TextureRole role)
        {
            return IsDerivedMaskRole(role) || role == TextureRole.Mask;
        }

        /// <summary>
        /// WP-038I · Prop / Breakable-class masks (the AP-005B and AP-017 Tripo props, 512² sets) ship at 256²: half the class size,
        /// the same ratio the 2048² classes keep with <see cref="DerivedMaskMaxSize"/>. A ≤ 2 m prop reads 1.3 cm per mask texel at
        /// 256 — nothing the gameplay rig resolves — and the ten prop roles stay 2.6 MB under the ADR-015-1 v5 world line.
        /// </summary>
        public const int PropMaskMaxSize = 256;

        public static int MaskMaxSizeOf(TextureClass c)
        {
            return c == TextureClass.Prop || c == TextureClass.Breakable ? PropMaskMaxSize : DerivedMaskMaxSize;
        }

        /// <summary>
        /// WP-038L · Prop / Breakable-class normal maps ship at 256² (the AP-017 Phase 2b import took the world line to 127.3 / 125 MB
        /// and the release to 550.9 / 550 MB). A ≤ 2 m prop spans ~80 px at the gameplay rig, so the GPU samples the 128² mip of a
        /// 512² normal there — mip 0 was never on screen; albedo keeps the class size (512²).
        /// </summary>
        public const int PropNormalMaxSize = 256;

        public static int MaxSizeOf(TextureClass c, TextureRole role)
        {
            int size = MaxSizeOf(c);
            if (role == TextureRole.Normal && (c == TextureClass.Prop || c == TextureClass.Breakable)) return Mathf.Min(size, PropNormalMaxSize);
            return IsCappedMaskRole(role) ? Mathf.Min(size, MaskMaxSizeOf(c)) : size;
        }

        /// <summary>Shipped max size for a texture path (its class's size, derived masks capped).</summary>
        /// <summary>WP-038E · every AP-018 <c>wet_*</c> map (wet stone albedo, wet packed maps and their derived masks; the tiles are 512 already) ships at 512² — wet reads through the blend (plate wetness ≤ 0.55), and the world set stays under the 120 MB budget (ADR-015-1 v2).</summary>
        public const int WetPackedMaxSize = 512;

        public static int MaxTextureSizeOf(string assetPath)
        {
            int size = MaxSizeOf(ClassOf(assetPath), RoleOf(assetPath));
            string slug = RoleSlugOf(assetPath);
            if (slug != null && slug.StartsWith("wet_", StringComparison.Ordinal)) size = Mathf.Min(size, WetPackedMaxSize);
            return size;
        }

        /// <summary>WP-038 · Mask-role textures a material actually samples (the AP-015P water data maps); the AP packs / ID masks stay provenance-only.</summary>
        public static bool IsBoundMask(string assetPath)
        {
            string slug = RoleSlugOf(assetPath);
            return slug != null && (slug.StartsWith("water_", StringComparison.Ordinal) || slug.StartsWith("wet_", StringComparison.Ordinal));
        }

        /// <summary>Estimated bytes a texture occupies in the build (BC7/BC5 = 1 byte per texel, full mip chain), capped at the source size.</summary>
        public static long EstimatedShippedBytes(int sourceWidth, int sourceHeight, TextureClass c, bool mipmaps)
        {
            return EstimatedShippedBytes(sourceWidth, sourceHeight, MaxSizeOf(c), mipmaps);
        }

        public static long EstimatedShippedBytes(int sourceWidth, int sourceHeight, TextureClass c, TextureRole role, bool mipmaps)
        {
            return EstimatedShippedBytes(sourceWidth, sourceHeight, MaxSizeOf(c, role), mipmaps);
        }

        /// <summary>Per asset: the class / role cap plus the path-specific caps (WP-038E wet packed 512).</summary>
        public static long EstimatedShippedBytes(string assetPath, int sourceWidth, int sourceHeight, bool mipmaps)
        {
            return EstimatedShippedBytes(sourceWidth, sourceHeight, MaxTextureSizeOf(assetPath), mipmaps);
        }

        private static long EstimatedShippedBytes(int sourceWidth, int sourceHeight, int cap, bool mipmaps)
        {
            int w = sourceWidth, h = sourceHeight;
            while (Mathf.Max(w, h) > cap)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }

            long bytes = 0;
            while (w >= 1 && h >= 1)
            {
                bytes += (long)Mathf.Max(4, w) * Mathf.Max(4, h);   // block formats pad to 4×4
                if (!mipmaps)
                {
                    break;
                }

                w /= 2;
                h /= 2;
            }

            return bytes;
        }

        /// <summary>WP-011 value; ADR-015-1 keeps it as the largest class size (Ground).</summary>
        public const int TextureMaxSize = 2048;
        public const bool TextureMipmaps = true;
        public const FilterMode TextureFilter = FilterMode.Trilinear;
        public const TextureWrapMode TextureWrap = TextureWrapMode.Repeat;
        public const int TextureAniso = 4;
        public const TextureImporterCompression TextureCompression = TextureImporterCompression.Compressed;
        public const bool TextureCrunched = false;
        public const string StandalonePlatform = "Standalone";
        public const TextureImporterFormat StandaloneColorFormat = TextureImporterFormat.BC7;
        public const TextureImporterFormat StandaloneNormalFormat = TextureImporterFormat.BC5;
        public const TextureImporterFormat StandaloneMaskFormat = TextureImporterFormat.BC7;

        /// <summary>Alpha clip threshold the cutout materials use (AP-003 sets were dilated for 0.5); mips preserve coverage at this value.</summary>
        public const float CutoutAlphaReference = 0.5f;

        /// <summary>
        /// WP-012 · The <c>&lt;role&gt;</c> part of <c>T_&lt;role&gt;_&lt;Map&gt;.png</c> (null when the name
        /// is not a contract name). Roles may contain underscores (<c>green_foliage_01</c>).
        /// </summary>
        public static string RoleSlugOf(string assetPath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (RoleOf(assetPath) == TextureRole.Unknown || !name.StartsWith("T_", StringComparison.Ordinal))
            {
                return null;
            }

            int last = name.LastIndexOf('_');
            return name.Substring(2, last - 2);
        }

        /// <summary>
        /// WP-012 · True for a BaseColor map whose alpha is an authored cutout mask (the AP-003
        /// foliage sets, <see cref="ArtLibraryMaterialRoles.IsCutoutTextureRole"/>). Such a map
        /// keeps its alpha (<c>alphaSource FromInput</c>, <c>alphaIsTransparency</c>); every
        /// other BaseColor map drops it, as in WP-011.
        /// </summary>
        public static bool KeepsCutoutAlpha(string assetPath)
        {
            return RoleOf(assetPath) == TextureRole.BaseColor && ArtLibraryMaterialRoles.IsCutoutTextureRole(RoleSlugOf(assetPath));
        }

        public static TextureRole RoleOf(string assetPath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (!name.StartsWith("T_", StringComparison.Ordinal))
            {
                return TextureRole.Unknown;
            }

            int last = name.LastIndexOf('_');
            if (last <= 2)
            {
                return TextureRole.Unknown;
            }

            switch (name.Substring(last + 1))
            {
                case "BaseColor": return TextureRole.BaseColor;
                case "Normal": return TextureRole.Normal;
                case "Packed": return TextureRole.Packed;
                case "MetallicSmoothness": return TextureRole.MetallicSmoothness;
                case "Mask": return TextureRole.Mask;
                case "DetailMask": return TextureRole.DetailMask;
                case "Sheet": return TextureRole.Sheet;
                default: return TextureRole.Unknown;
            }
        }

        /// <summary>Every non-colour map is linear data: normals, packed/derived masks, the AP-007 ID mask and its detail mask.</summary>
        public static bool IsLinearRole(TextureRole role)
        {
            return role == TextureRole.Normal || role == TextureRole.Packed || role == TextureRole.MetallicSmoothness ||
                   role == TextureRole.Mask || role == TextureRole.DetailMask;
        }

        /// <summary>Roles whose alpha channel carries data (smoothness; the ID mask's emissive flag; the detail mask's cloth gate; a sheet's coverage).</summary>
        public static bool KeepsAlpha(TextureRole role)
        {
            return role == TextureRole.MetallicSmoothness || role == TextureRole.Mask || role == TextureRole.DetailMask || role == TextureRole.Sheet;
        }

        public static TextureImporterFormat FormatOf(TextureRole role)
        {
            return role == TextureRole.Normal ? StandaloneNormalFormat
                : role == TextureRole.BaseColor || role == TextureRole.Unknown || role == TextureRole.Sheet ? StandaloneColorFormat
                : StandaloneMaskFormat;
        }

        public static bool IsUnderContract(string assetPath)
        {
            return assetPath != null && assetPath.StartsWith(ArtRoot + "/", StringComparison.Ordinal);
        }

        public static bool IsContractModel(string assetPath)
        {
            return IsUnderContract(assetPath) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsContractTexture(string assetPath)
        {
            return IsUnderContract(assetPath) && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !IsLutTexture(assetPath) && !IsMenuPlateTexture(assetPath);
        }

        // ------------------------------------------------------------------ WP-068 menu plates

        /// <summary>
        /// WP-068 slice 6: the engine-captured MainMenu / loading backdrop (Look B plate stack, MenuPlateCaptureTests) lives here,
        /// in a Resources folder so the UI loads it by name (<c>MenuPlates/plate_far</c> …), and follows its own importer rules
        /// (<see cref="ApplyMenuPlate"/>), not the T_&lt;role&gt;_&lt;Map&gt; contract — the LUT precedent.
        /// </summary>
        public const string MenuPlateRoot = ArtRoot + "/Resources/MenuPlates";
        public const string MenuPlateFar = "plate_far", MenuPlateMid = "plate_mid", MenuPlateNear = "plate_near", MenuPlateLanternMask = "plate_lantern_mask";
        public const int MenuPlateMaxSize = 4096;

        public static bool IsMenuPlateTexture(string assetPath)
        {
            return assetPath != null && assetPath.Replace('\\', '/').StartsWith(MenuPlateRoot + "/", StringComparison.Ordinal) && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A backdrop layer: full size (2560×1440, never downsized), no mips (the menu draws it at 0.5–1× like the AP-012 plate —
        /// and Unity leaves an NPOT texture with a mip chain uncompressed: the first build shipped 55 MB of RGBA), no mip
        /// streaming (UI images are not renderers — the streamer would leave them at the lowest mip), clamp, bilinear, crunched
        /// on Standalone — the far layer DXT1 (opaque), the mid / near layers DXT5 (straight alpha, dilated by Unity); the lantern
        /// mask is a linear single-channel BC4. §7 P1/P2 of the WP-068 direction.
        /// </summary>
        public static bool ApplyMenuPlate(TextureImporter importer, string assetPath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool mask = name == MenuPlateLanternMask, opaque = name == MenuPlateFar;
            bool changed = false;
            changed |= Set(importer.textureType, mask ? TextureImporterType.SingleChannel : TextureImporterType.Default, v => importer.textureType = v);
            if (mask)
            {
                TextureImporterSettings ts = new TextureImporterSettings();
                importer.ReadTextureSettings(ts);
                if (ts.singleChannelComponent != TextureImporterSingleChannelComponent.Red)
                {
                    ts.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                    importer.SetTextureSettings(ts);
                    changed = true;
                }
            }

            changed |= Set(importer.sRGBTexture, !mask, v => importer.sRGBTexture = v);
            changed |= Set(importer.alphaSource, opaque || mask ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput, v => importer.alphaSource = v);
            changed |= Set(importer.alphaIsTransparency, !opaque && !mask, v => importer.alphaIsTransparency = v);
            changed |= Set(importer.mipmapEnabled, false, v => importer.mipmapEnabled = v);
            changed |= Set(importer.streamingMipmaps, false, v => importer.streamingMipmaps = v);
            changed |= Set(importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
            changed |= Set(importer.filterMode, FilterMode.Bilinear, v => importer.filterMode = v);
            changed |= Set(importer.anisoLevel, 0, v => importer.anisoLevel = v);
            changed |= Set(importer.npotScale, TextureImporterNPOTScale.None, v => importer.npotScale = v);
            changed |= Set(importer.maxTextureSize, MenuPlateMaxSize, v => importer.maxTextureSize = v);
            changed |= Set(importer.textureCompression, TextureImporterCompression.Compressed, v => importer.textureCompression = v);
            changed |= Set(importer.crunchedCompression, !mask, v => importer.crunchedCompression = v);
            changed |= Set(importer.compressionQuality, 50, v => importer.compressionQuality = v);
            TextureImporterFormat format = MenuPlateFormat(name);
            TextureImporterPlatformSettings st = importer.GetPlatformTextureSettings(StandalonePlatform);
            if (!st.overridden || st.format != format || st.maxTextureSize != MenuPlateMaxSize || st.crunchedCompression != !mask || st.compressionQuality != 50)
            {
                st.overridden = true;
                st.format = format;
                st.maxTextureSize = MenuPlateMaxSize;
                st.textureCompression = TextureImporterCompression.Compressed;
                st.crunchedCompression = !mask;
                st.compressionQuality = 50;
                importer.SetPlatformTextureSettings(st);
                changed = true;
            }

            return changed;
        }

        public static TextureImporterFormat MenuPlateFormat(string name)
        {
            if (name == MenuPlateLanternMask) return TextureImporterFormat.BC4;
            return name == MenuPlateFar ? TextureImporterFormat.DXT1Crunched : TextureImporterFormat.DXT5Crunched;
        }

        // ------------------------------------------------------------------ WP-029 LUTs

        /// <summary>WP-029 (ADR-029-1): colour-grading LUT strips live here and follow their own importer rules, not the T_&lt;role&gt;_&lt;Map&gt; contract.</summary>
        public const string LutRoot = ArtRoot + "/Lighting/LUT";

        public static bool IsLutTexture(string assetPath)
        {
            return assetPath != null && assetPath.Replace('\\', '/').StartsWith(LutRoot + "/", StringComparison.Ordinal) && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A URP user LUT: linear (sRGB off — the strip's bytes ARE the sRGB-coded output), no
        /// mips, uncompressed RGBA32, bilinear, clamp, no streaming, never downsized. The
        /// Volume's Color Lookup validates height = LUT size and width = size², so the
        /// pipeline asset's <c>colorGradingLutSize</c> (32) picks <c>DD_Look_v1.png</c> at 1024×32.
        /// </summary>
        public static bool ApplyLut(TextureImporter importer)
        {
            bool changed = false;
            changed |= Set(importer.textureType, TextureImporterType.Default, v => importer.textureType = v);
            changed |= Set(importer.sRGBTexture, false, v => importer.sRGBTexture = v);
            changed |= Set(importer.mipmapEnabled, false, v => importer.mipmapEnabled = v);
            changed |= Set(importer.filterMode, FilterMode.Bilinear, v => importer.filterMode = v);
            changed |= Set(importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
            changed |= Set(importer.anisoLevel, 0, v => importer.anisoLevel = v);
            changed |= Set(importer.streamingMipmaps, false, v => importer.streamingMipmaps = v);
            changed |= Set(importer.textureCompression, TextureImporterCompression.Uncompressed, v => importer.textureCompression = v);
            changed |= Set(importer.alphaSource, TextureImporterAlphaSource.None, v => importer.alphaSource = v);
            changed |= Set(importer.maxTextureSize, 8192, v => importer.maxTextureSize = v);
            changed |= Set(importer.npotScale, TextureImporterNPOTScale.None, v => importer.npotScale = v);
            TextureImporterPlatformSettings st = importer.GetPlatformTextureSettings(StandalonePlatform);
            if (!st.overridden || st.format != TextureImporterFormat.RGBA32 || st.maxTextureSize != 8192 || st.textureCompression != TextureImporterCompression.Uncompressed)
            {
                st.overridden = true;
                st.format = TextureImporterFormat.RGBA32;
                st.maxTextureSize = 8192;
                st.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SetPlatformTextureSettings(st);
                changed = true;
            }

            return changed;
        }

        public static List<string> ValidateLut(TextureImporter importer)
        {
            var v = new List<string>();
            Check(v, "sRGBTexture", importer.sRGBTexture, false);
            Check(v, "mipmapEnabled", importer.mipmapEnabled, false);
            Check(v, "filterMode", importer.filterMode, FilterMode.Bilinear);
            Check(v, "wrapMode", importer.wrapMode, TextureWrapMode.Clamp);
            Check(v, "streamingMipmaps", importer.streamingMipmaps, false);
            Check(v, "textureCompression", importer.textureCompression, TextureImporterCompression.Uncompressed);
            TextureImporterPlatformSettings st = importer.GetPlatformTextureSettings(StandalonePlatform);
            if (!st.overridden || st.format != TextureImporterFormat.RGBA32) v.Add("Standalone format: " + (st.overridden ? st.format.ToString() : "not overridden") + " != RGBA32");
            return v;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Writes the contract into a model importer. Returns true if anything changed.</summary>
        public static bool Apply(ModelImporter importer)
        {
            bool changed = false;
            changed |= Set(importer.globalScale, ModelGlobalScale, v => importer.globalScale = v);
            changed |= Set(importer.useFileScale, ModelUseFileScale, v => importer.useFileScale = v);
            changed |= Set(importer.bakeAxisConversion, ModelBakeAxisConversion && BakesAxis(importer.assetPath), v => importer.bakeAxisConversion = v);
            changed |= Set(importer.importNormals, ModelNormals, v => importer.importNormals = v);
            changed |= Set(importer.importTangents, ModelTangents, v => importer.importTangents = v);
            changed |= Set(importer.meshCompression, MeshCompressionOf(importer.assetPath), v => importer.meshCompression = v);
            changed |= Set(importer.isReadable, ModelIsReadableOf(importer.assetPath) || ModelIsReadable, v => importer.isReadable = v);
            changed |= Set(importer.indexFormat, ModelIndexFormat, v => importer.indexFormat = v);
            changed |= Set(importer.optimizeMeshPolygons, ModelOptimizeMeshPolygons, v => importer.optimizeMeshPolygons = v);
            changed |= Set(importer.optimizeMeshVertices, ModelOptimizeMeshVertices, v => importer.optimizeMeshVertices = v);
            changed |= Set(importer.weldVertices, ModelWeldVertices, v => importer.weldVertices = v);
            bool uv2 = WantsLightmapUv(importer.assetPath);
            changed |= Set(importer.generateSecondaryUV, uv2, v => importer.generateSecondaryUV = v);
            if (uv2)
            {
                changed |= Set(importer.secondaryUVHardAngle, ModelSecondaryUVHardAngle, v => importer.secondaryUVHardAngle = v);
                changed |= Set(importer.secondaryUVMarginMethod, ModelSecondaryUVMarginMethod, v => importer.secondaryUVMarginMethod = v);
                changed |= Set(importer.secondaryUVMinLightmapResolution, ModelSecondaryUVMinLightmapResolution, v => importer.secondaryUVMinLightmapResolution = v);
                changed |= Set(importer.secondaryUVMinObjectScale, ModelSecondaryUVMinObjectScale, v => importer.secondaryUVMinObjectScale = v);
                changed |= Set(importer.secondaryUVAngleDistortion, ModelSecondaryUVAngleDistortion, v => importer.secondaryUVAngleDistortion = v);
                changed |= Set(importer.secondaryUVAreaDistortion, ModelSecondaryUVAreaDistortion, v => importer.secondaryUVAreaDistortion = v);
            }
            changed |= Set(importer.swapUVChannels, ModelSwapUVChannels, v => importer.swapUVChannels = v);
            changed |= Set(importer.materialImportMode, ModelMaterialImportMode, v => importer.materialImportMode = v);
            changed |= Set(importer.animationType, ModelAnimationType, v => importer.animationType = v);
            changed |= Set(importer.importAnimation, ModelImportAnimation, v => importer.importAnimation = v);
            changed |= Set(importer.importBlendShapes, ModelImportBlendShapes, v => importer.importBlendShapes = v);
            changed |= Set(importer.importCameras, ModelImportCameras, v => importer.importCameras = v);
            changed |= Set(importer.importLights, ModelImportLights, v => importer.importLights = v);
            changed |= Set(importer.importVisibility, ModelImportVisibility, v => importer.importVisibility = v);
            changed |= Set(importer.preserveHierarchy, ModelPreserveHierarchy, v => importer.preserveHierarchy = v);
            changed |= Set(importer.keepQuads, ModelKeepQuads, v => importer.keepQuads = v);
            return changed;
        }

        /// <summary>Writes the contract into a texture importer for its role. Returns true if anything changed.</summary>
        public static bool Apply(TextureImporter importer, TextureRole role)
        {
            bool changed = false;
            bool linear = IsLinearRole(role);
            TextureImporterType type = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterFormat format = FormatOf(role);

            changed |= Set(importer.textureType, type, v => importer.textureType = v);
            changed |= Set(importer.sRGBTexture, !linear, v => importer.sRGBTexture = v);
            // WP-015 (ADR-015-1): size and streaming come from the role's distance class.
            TextureClass textureClass = ClassOf(importer.assetPath);
            int maxSize = MaxTextureSizeOf(importer.assetPath);
            changed |= Set(importer.mipmapEnabled, TextureMipmaps, v => importer.mipmapEnabled = v);
            changed |= Set(importer.streamingMipmaps, StreamsMipmaps(textureClass), v => importer.streamingMipmaps = v);
            changed |= Set(importer.maxTextureSize, maxSize, v => importer.maxTextureSize = v);
            changed |= Set(importer.textureCompression, TextureCompression, v => importer.textureCompression = v);
            changed |= Set(importer.crunchedCompression, TextureCrunched, v => importer.crunchedCompression = v);
            changed |= Set(importer.filterMode, TextureFilter, v => importer.filterMode = v);
            changed |= Set(importer.wrapMode, TextureWrap, v => importer.wrapMode = v);
            changed |= Set(importer.anisoLevel, TextureAniso, v => importer.anisoLevel = v);
            bool cutout = KeepsCutoutAlpha(importer.assetPath);
            // A VFX sheet's alpha is its coverage (blended, not clipped): transparency dilation, no coverage-preserving mips.
            bool sheet = role == TextureRole.Sheet;
            changed |= Set(importer.alphaIsTransparency, cutout || sheet, v => importer.alphaIsTransparency = v);
            // The mask keeps its alpha (smoothness) and a cutout BaseColor keeps its opacity;
            // every other colour/normal/packed map carries none worth keeping.
            TextureImporterAlphaSource alpha = KeepsAlpha(role) || cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            changed |= Set(importer.alphaSource, alpha, v => importer.alphaSource = v);
            // Cutout mips keep their coverage at the clip threshold (WP-012 art review: leaf
            // cards thinned to skeletons past ~30 m because box-filtered mip alpha shrinks).
            changed |= Set(importer.mipMapsPreserveCoverage, cutout, v => importer.mipMapsPreserveCoverage = v);
            if (cutout)
            {
                changed |= Set(importer.alphaTestReferenceValue, CutoutAlphaReference, v => importer.alphaTestReferenceValue = v);
            }

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(StandalonePlatform);
            if (!standalone.overridden || standalone.format != format || standalone.maxTextureSize != maxSize ||
                standalone.textureCompression != TextureCompression || standalone.crunchedCompression != TextureCrunched)
            {
                standalone.overridden = true;
                standalone.format = format;
                standalone.maxTextureSize = maxSize;
                standalone.textureCompression = TextureCompression;
                standalone.crunchedCompression = TextureCrunched;
                importer.SetPlatformTextureSettings(standalone);
                changed = true;
            }

            return changed;
        }

        // --------------------------------------------------------------- validate

        /// <summary>Every field that differs from the contract, as "field: actual != expected". Empty = compliant.</summary>
        public static List<string> ValidateModel(ModelImporter importer)
        {
            var v = new List<string>();
            Check(v, "globalScale", importer.globalScale, ModelGlobalScale);
            Check(v, "useFileScale", importer.useFileScale, ModelUseFileScale);
            Check(v, "bakeAxisConversion", importer.bakeAxisConversion, ModelBakeAxisConversion && BakesAxis(importer.assetPath));
            Check(v, "importNormals", importer.importNormals, ModelNormals);
            Check(v, "importTangents", importer.importTangents, ModelTangents);
            Check(v, "meshCompression", importer.meshCompression, MeshCompressionOf(importer.assetPath));
            Check(v, "isReadable", importer.isReadable, ModelIsReadableOf(importer.assetPath) || ModelIsReadable);
            Check(v, "indexFormat", importer.indexFormat, ModelIndexFormat);
            Check(v, "optimizeMeshPolygons", importer.optimizeMeshPolygons, ModelOptimizeMeshPolygons);
            Check(v, "optimizeMeshVertices", importer.optimizeMeshVertices, ModelOptimizeMeshVertices);
            Check(v, "weldVertices", importer.weldVertices, ModelWeldVertices);
            bool uv2 = WantsLightmapUv(importer.assetPath);
            Check(v, "generateSecondaryUV", importer.generateSecondaryUV, uv2);
            // The chart settings only matter where lightmap UVs are generated (environment models; characters / weapons / VFX props stay UV2-less).
            if (uv2)
            {
                Check(v, "secondaryUVHardAngle", importer.secondaryUVHardAngle, ModelSecondaryUVHardAngle);
                Check(v, "secondaryUVMarginMethod", importer.secondaryUVMarginMethod, ModelSecondaryUVMarginMethod);
                Check(v, "secondaryUVMinLightmapResolution", importer.secondaryUVMinLightmapResolution, ModelSecondaryUVMinLightmapResolution);
                Check(v, "secondaryUVMinObjectScale", importer.secondaryUVMinObjectScale, ModelSecondaryUVMinObjectScale);
                Check(v, "secondaryUVAngleDistortion", importer.secondaryUVAngleDistortion, ModelSecondaryUVAngleDistortion);
                Check(v, "secondaryUVAreaDistortion", importer.secondaryUVAreaDistortion, ModelSecondaryUVAreaDistortion);
            }

            Check(v, "swapUVChannels", importer.swapUVChannels, ModelSwapUVChannels);
            Check(v, "materialImportMode", importer.materialImportMode, ModelMaterialImportMode);
            Check(v, "animationType", importer.animationType, ModelAnimationType);
            Check(v, "importAnimation", importer.importAnimation, ModelImportAnimation);
            Check(v, "importBlendShapes", importer.importBlendShapes, ModelImportBlendShapes);
            Check(v, "importCameras", importer.importCameras, ModelImportCameras);
            Check(v, "importLights", importer.importLights, ModelImportLights);
            Check(v, "importVisibility", importer.importVisibility, ModelImportVisibility);
            Check(v, "preserveHierarchy", importer.preserveHierarchy, ModelPreserveHierarchy);
            Check(v, "keepQuads", importer.keepQuads, ModelKeepQuads);
            return v;
        }

        public static List<string> ValidateTexture(TextureImporter importer, TextureRole role)
        {
            var v = new List<string>();
            if (role == TextureRole.Unknown)
            {
                v.Add("name: not a contract texture name (T_<role>_<BaseColor|Normal|Packed|MetallicSmoothness|Mask|DetailMask|Sheet>.png)");
            }

            bool linear = IsLinearRole(role);
            TextureImporterType type = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterFormat format = FormatOf(role);
            bool cutout = KeepsCutoutAlpha(importer.assetPath);
            bool sheet = role == TextureRole.Sheet;
            TextureImporterAlphaSource alpha = KeepsAlpha(role) || cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;

            TextureClass? roleClass = ClassifiedClassOf(importer.assetPath);
            if (role != TextureRole.Unknown && roleClass == null)
            {
                v.Add("class: role '" + RoleSlugOf(importer.assetPath) + "' is not in the ADR-015-1 texture class table (imports at the Prop size)");
            }

            TextureClass textureClass = roleClass ?? TextureClass.Prop;
            int maxSize = MaxTextureSizeOf(importer.assetPath);

            Check(v, "textureType", importer.textureType, type);
            Check(v, "sRGBTexture", importer.sRGBTexture, !linear);
            Check(v, "mipmapEnabled", importer.mipmapEnabled, TextureMipmaps);
            Check(v, "streamingMipmaps", importer.streamingMipmaps, StreamsMipmaps(textureClass));
            Check(v, "maxTextureSize", importer.maxTextureSize, maxSize);
            Check(v, "textureCompression", importer.textureCompression, TextureCompression);
            Check(v, "crunchedCompression", importer.crunchedCompression, TextureCrunched);
            Check(v, "filterMode", importer.filterMode, TextureFilter);
            Check(v, "wrapMode", importer.wrapMode, TextureWrap);
            Check(v, "anisoLevel", importer.anisoLevel, TextureAniso);
            Check(v, "alphaIsTransparency", importer.alphaIsTransparency, cutout || sheet);
            Check(v, "alphaSource", importer.alphaSource, alpha);
            Check(v, "mipMapsPreserveCoverage", importer.mipMapsPreserveCoverage, cutout);
            if (cutout)
            {
                Check(v, "alphaTestReferenceValue", importer.alphaTestReferenceValue, CutoutAlphaReference);
            }

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(StandalonePlatform);
            Check(v, "Standalone.overridden", standalone.overridden, true);
            Check(v, "Standalone.format", standalone.format, format);
            Check(v, "Standalone.maxTextureSize", standalone.maxTextureSize, maxSize);
            return v;
        }

        private static bool Set<T>(T current, T expected, Action<T> write)
        {
            if (EqualityComparer<T>.Default.Equals(current, expected))
            {
                return false;
            }

            write(expected);
            return true;
        }

        private static void Check<T>(List<string> v, string field, T actual, T expected)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                v.Add(field + ": " + actual + " != " + expected);
            }
        }
    }
}
