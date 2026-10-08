using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10, S3-28) · the macOS Player icon is the AP-035 app icon (1024², alpha; dark_safe
    /// emblem). Idempotent and API-only (no hand-written ProjectSettings YAML): the icon's importer is set to what an
    /// application icon needs (default texture, sRGB, no mips, uncompressed, ≤ 1024), then every Standalone application
    /// icon size and the default icon point at it. A second run changes nothing. The PNG lives outside Resources - it is
    /// not game data. Batch: <c>-executeMethod DD.Prototype.Core.EditorTools.AppIconSetup.ApplyBatch</c>.
    /// </summary>
    public static class AppIconSetup
    {
        public const string IconPath = "Assets/_Prototype/Core/AppIcon/app_icon_1024.png";

        [MenuItem("Tools/Deaths Demolition/Apply App Icon (AP-035)", priority = 30)]
        public static void Apply()
        {
            bool changed = Run();
            Debug.Log("[AppIcon] " + (changed ? "applied " : "already set ") + IconPath);
        }

        /// <summary>Batch entry: exits 0 when the icon is in place, 1 when it could not be set.</summary>
        public static void ApplyBatch()
        {
            int code = 0;
            try
            {
                Apply();
                code = IsApplied() ? 0 : 1;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AppIcon] " + e.Message);
                code = 1;
            }

            EditorApplication.Exit(code);
        }

        /// <summary>True when every Standalone application icon slot and the default icon are the AP-035 icon.</summary>
        public static bool IsApplied()
        {
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                return false;
            }

            Texture2D[] icons = PlayerSettings.GetIcons(NamedBuildTarget.Standalone, IconKind.Application);
            if (icons == null || icons.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] != icon)
                {
                    return false;
                }
            }

            Texture2D[] fallback = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            return fallback != null && fallback.Length > 0 && fallback[0] == icon;
        }

        private static bool Run()
        {
            bool changed = EnsureImporter();
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                throw new System.InvalidOperationException("app icon missing: " + IconPath);
            }

            if (IsApplied())
            {
                return changed;
            }

            int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Application);
            var standalone = new Texture2D[Mathf.Max(1, sizes.Length)];
            for (int i = 0; i < standalone.Length; i++)
            {
                standalone[i] = icon;
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, standalone, IconKind.Application);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static bool EnsureImporter()
        {
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            bool dirty = importer.textureType != TextureImporterType.Default
                         || !importer.sRGBTexture
                         || importer.mipmapEnabled
                         || importer.textureCompression != TextureImporterCompression.Uncompressed
                         || importer.npotScale != TextureImporterNPOTScale.None
                         || !importer.alphaIsTransparency
                         || importer.maxTextureSize != 1024;
            if (!dirty)
            {
                return false;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            return true;
        }
    }
}
