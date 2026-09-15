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
        public const string Version = "ADR-011-1 Import Contract v1 (2026-09-15; WP-012: cutout BaseColor alpha, ADR-012-2; WP-015: texture class table, ADR-015-1)";

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
        public const ModelImporterNormals ModelNormals = ModelImporterNormals.Import;
        public const ModelImporterTangents ModelTangents = ModelImporterTangents.CalculateMikk;
        public const ModelImporterMeshCompression ModelMeshCompression = ModelImporterMeshCompression.Off;
        public const bool ModelIsReadable = false;
        public const ModelImporterIndexFormat ModelIndexFormat = ModelImporterIndexFormat.Auto;
        public const bool ModelOptimizeMeshPolygons = true;
        public const bool ModelOptimizeMeshVertices = true;
        public const bool ModelWeldVertices = true;
        public const bool ModelGenerateSecondaryUV = false;
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
            /// <summary>Derived URP mask: R metallic, A smoothness. Linear.</summary>
            MetallicSmoothness = 4
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
            /// <summary>Dressing props (lanterns, benches, crates): ≤ 512. Reserved for AP-015 prop sets; no role today.</summary>
            Prop = 3,
            /// <summary>Breakables: ≤ 512. Reserved; no role today.</summary>
            Breakable = 4,
            /// <summary>The 4×256 sky gradient (ArtSkyBuilder): 256, never streamed (a skybox has no renderer for the streaming system to rate).</summary>
            Sky = 5
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
                case TextureClass.Sky: return 256;
                default: throw new ArgumentOutOfRangeException(nameof(c));
            }
        }

        /// <summary>Mipmap streaming is on for every class that a renderer samples; the sky is not one.</summary>
        public static bool StreamsMipmaps(TextureClass c)
        {
            return c != TextureClass.Sky;
        }

        /// <summary>
        /// Streaming budget written to QualitySettings (every level): the ADR-015-1 resident
        /// ceiling for the art texture set. The AP-005 set fits under it fully resident
        /// (116 MB at the class sizes), so the budget is a guard, not a squeeze.
        /// </summary>
        public const int StreamingMipmapsBudgetMb = 160;

        /// <summary>ADR-015-1 resident ceiling for the art texture set (probe counter delta against the procedural Run), MB.</summary>
        public const int ArtTextureMemoryBudgetMb = 160;

        /// <summary>ADR-015-2 release build ceiling, MB (app bundle on disk).</summary>
        public const int ReleaseBuildBudgetMb = 400;

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
            { "sky", TextureClass.Sky }
        };

        public static IEnumerable<KeyValuePair<string, TextureClass>> TextureClassTable => ClassOfRoleTable;

        /// <summary>The class of a role slug, or null when the role is not in the ADR-015-1 table.</summary>
        public static TextureClass? ClassOfRole(string roleSlug)
        {
            return roleSlug != null && ClassOfRoleTable.TryGetValue(roleSlug, out TextureClass c) ? c : (TextureClass?)null;
        }

        /// <summary>The class a texture imports with: its role's class, Prop for an unclassified role.</summary>
        public static TextureClass ClassOf(string assetPath)
        {
            return ClassOfRole(RoleSlugOf(assetPath)) ?? TextureClass.Prop;
        }

        /// <summary>Shipped max size for a texture path (its class's size).</summary>
        public static int MaxTextureSizeOf(string assetPath)
        {
            return MaxSizeOf(ClassOf(assetPath));
        }

        /// <summary>Estimated bytes a texture occupies in the build (BC7/BC5 = 1 byte per texel, full mip chain), capped at the source size.</summary>
        public static long EstimatedShippedBytes(int sourceWidth, int sourceHeight, TextureClass c, bool mipmaps)
        {
            int cap = MaxSizeOf(c);
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
                default: return TextureRole.Unknown;
            }
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
            return IsUnderContract(assetPath) && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Writes the contract into a model importer. Returns true if anything changed.</summary>
        public static bool Apply(ModelImporter importer)
        {
            bool changed = false;
            changed |= Set(importer.globalScale, ModelGlobalScale, v => importer.globalScale = v);
            changed |= Set(importer.useFileScale, ModelUseFileScale, v => importer.useFileScale = v);
            changed |= Set(importer.bakeAxisConversion, ModelBakeAxisConversion, v => importer.bakeAxisConversion = v);
            changed |= Set(importer.importNormals, ModelNormals, v => importer.importNormals = v);
            changed |= Set(importer.importTangents, ModelTangents, v => importer.importTangents = v);
            changed |= Set(importer.meshCompression, ModelMeshCompression, v => importer.meshCompression = v);
            changed |= Set(importer.isReadable, ModelIsReadable, v => importer.isReadable = v);
            changed |= Set(importer.indexFormat, ModelIndexFormat, v => importer.indexFormat = v);
            changed |= Set(importer.optimizeMeshPolygons, ModelOptimizeMeshPolygons, v => importer.optimizeMeshPolygons = v);
            changed |= Set(importer.optimizeMeshVertices, ModelOptimizeMeshVertices, v => importer.optimizeMeshVertices = v);
            changed |= Set(importer.weldVertices, ModelWeldVertices, v => importer.weldVertices = v);
            changed |= Set(importer.generateSecondaryUV, ModelGenerateSecondaryUV, v => importer.generateSecondaryUV = v);
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
            bool linear = role == TextureRole.Normal || role == TextureRole.Packed || role == TextureRole.MetallicSmoothness;
            TextureImporterType type = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterFormat format = role == TextureRole.Normal ? StandaloneNormalFormat
                : role == TextureRole.MetallicSmoothness || role == TextureRole.Packed ? StandaloneMaskFormat
                : StandaloneColorFormat;

            changed |= Set(importer.textureType, type, v => importer.textureType = v);
            changed |= Set(importer.sRGBTexture, !linear, v => importer.sRGBTexture = v);
            // WP-015 (ADR-015-1): size and streaming come from the role's distance class.
            TextureClass textureClass = ClassOf(importer.assetPath);
            int maxSize = MaxSizeOf(textureClass);
            changed |= Set(importer.mipmapEnabled, TextureMipmaps, v => importer.mipmapEnabled = v);
            changed |= Set(importer.streamingMipmaps, StreamsMipmaps(textureClass), v => importer.streamingMipmaps = v);
            changed |= Set(importer.maxTextureSize, maxSize, v => importer.maxTextureSize = v);
            changed |= Set(importer.textureCompression, TextureCompression, v => importer.textureCompression = v);
            changed |= Set(importer.crunchedCompression, TextureCrunched, v => importer.crunchedCompression = v);
            changed |= Set(importer.filterMode, TextureFilter, v => importer.filterMode = v);
            changed |= Set(importer.wrapMode, TextureWrap, v => importer.wrapMode = v);
            changed |= Set(importer.anisoLevel, TextureAniso, v => importer.anisoLevel = v);
            bool cutout = KeepsCutoutAlpha(importer.assetPath);
            changed |= Set(importer.alphaIsTransparency, cutout, v => importer.alphaIsTransparency = v);
            // The mask keeps its alpha (smoothness) and a cutout BaseColor keeps its opacity;
            // every other colour/normal/packed map carries none worth keeping.
            TextureImporterAlphaSource alpha = role == TextureRole.MetallicSmoothness || cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
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
            Check(v, "bakeAxisConversion", importer.bakeAxisConversion, ModelBakeAxisConversion);
            Check(v, "importNormals", importer.importNormals, ModelNormals);
            Check(v, "importTangents", importer.importTangents, ModelTangents);
            Check(v, "meshCompression", importer.meshCompression, ModelMeshCompression);
            Check(v, "isReadable", importer.isReadable, ModelIsReadable);
            Check(v, "indexFormat", importer.indexFormat, ModelIndexFormat);
            Check(v, "optimizeMeshPolygons", importer.optimizeMeshPolygons, ModelOptimizeMeshPolygons);
            Check(v, "optimizeMeshVertices", importer.optimizeMeshVertices, ModelOptimizeMeshVertices);
            Check(v, "weldVertices", importer.weldVertices, ModelWeldVertices);
            Check(v, "generateSecondaryUV", importer.generateSecondaryUV, ModelGenerateSecondaryUV);
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
                v.Add("name: not a contract texture name (T_<role>_<BaseColor|Normal|Packed|MetallicSmoothness>.png)");
            }

            bool linear = role == TextureRole.Normal || role == TextureRole.Packed || role == TextureRole.MetallicSmoothness;
            TextureImporterType type = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureImporterFormat format = role == TextureRole.Normal ? StandaloneNormalFormat
                : role == TextureRole.MetallicSmoothness || role == TextureRole.Packed ? StandaloneMaskFormat
                : StandaloneColorFormat;
            bool cutout = KeepsCutoutAlpha(importer.assetPath);
            TextureImporterAlphaSource alpha = role == TextureRole.MetallicSmoothness || cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;

            TextureClass? roleClass = ClassOfRole(RoleSlugOf(importer.assetPath));
            if (role != TextureRole.Unknown && roleClass == null)
            {
                v.Add("class: role '" + RoleSlugOf(importer.assetPath) + "' is not in the ADR-015-1 texture class table (imports at the Prop size)");
            }

            TextureClass textureClass = roleClass ?? TextureClass.Prop;
            int maxSize = MaxSizeOf(textureClass);

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
            Check(v, "alphaIsTransparency", importer.alphaIsTransparency, cutout);
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
