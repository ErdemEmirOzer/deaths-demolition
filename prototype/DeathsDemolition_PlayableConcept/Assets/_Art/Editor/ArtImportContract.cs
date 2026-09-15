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
        public const string Version = "ADR-011-1 Import Contract v1 (2026-09-15)";

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

        public const int TextureMaxSize = 2048;
        public const bool TextureMipmaps = true;
        public const bool TextureStreamingMipmaps = false;
        public const FilterMode TextureFilter = FilterMode.Trilinear;
        public const TextureWrapMode TextureWrap = TextureWrapMode.Repeat;
        public const int TextureAniso = 4;
        public const TextureImporterCompression TextureCompression = TextureImporterCompression.Compressed;
        public const bool TextureCrunched = false;
        public const string StandalonePlatform = "Standalone";
        public const TextureImporterFormat StandaloneColorFormat = TextureImporterFormat.BC7;
        public const TextureImporterFormat StandaloneNormalFormat = TextureImporterFormat.BC5;
        public const TextureImporterFormat StandaloneMaskFormat = TextureImporterFormat.BC7;

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
            changed |= Set(importer.mipmapEnabled, TextureMipmaps, v => importer.mipmapEnabled = v);
            changed |= Set(importer.streamingMipmaps, TextureStreamingMipmaps, v => importer.streamingMipmaps = v);
            changed |= Set(importer.maxTextureSize, TextureMaxSize, v => importer.maxTextureSize = v);
            changed |= Set(importer.textureCompression, TextureCompression, v => importer.textureCompression = v);
            changed |= Set(importer.crunchedCompression, TextureCrunched, v => importer.crunchedCompression = v);
            changed |= Set(importer.filterMode, TextureFilter, v => importer.filterMode = v);
            changed |= Set(importer.wrapMode, TextureWrap, v => importer.wrapMode = v);
            changed |= Set(importer.anisoLevel, TextureAniso, v => importer.anisoLevel = v);
            changed |= Set(importer.alphaIsTransparency, false, v => importer.alphaIsTransparency = v);
            // The mask keeps its alpha (smoothness); colour/normal/packed maps carry none worth keeping.
            TextureImporterAlphaSource alpha = role == TextureRole.MetallicSmoothness ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            changed |= Set(importer.alphaSource, alpha, v => importer.alphaSource = v);

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(StandalonePlatform);
            if (!standalone.overridden || standalone.format != format || standalone.maxTextureSize != TextureMaxSize ||
                standalone.textureCompression != TextureCompression || standalone.crunchedCompression != TextureCrunched)
            {
                standalone.overridden = true;
                standalone.format = format;
                standalone.maxTextureSize = TextureMaxSize;
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
            TextureImporterAlphaSource alpha = role == TextureRole.MetallicSmoothness ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;

            Check(v, "textureType", importer.textureType, type);
            Check(v, "sRGBTexture", importer.sRGBTexture, !linear);
            Check(v, "mipmapEnabled", importer.mipmapEnabled, TextureMipmaps);
            Check(v, "streamingMipmaps", importer.streamingMipmaps, TextureStreamingMipmaps);
            Check(v, "maxTextureSize", importer.maxTextureSize, TextureMaxSize);
            Check(v, "textureCompression", importer.textureCompression, TextureCompression);
            Check(v, "crunchedCompression", importer.crunchedCompression, TextureCrunched);
            Check(v, "filterMode", importer.filterMode, TextureFilter);
            Check(v, "wrapMode", importer.wrapMode, TextureWrap);
            Check(v, "anisoLevel", importer.anisoLevel, TextureAniso);
            Check(v, "alphaIsTransparency", importer.alphaIsTransparency, false);
            Check(v, "alphaSource", importer.alphaSource, alpha);

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(StandalonePlatform);
            Check(v, "Standalone.overridden", standalone.overridden, true);
            Check(v, "Standalone.format", standalone.format, format);
            Check(v, "Standalone.maxTextureSize", standalone.maxTextureSize, TextureMaxSize);
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
