using System;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// WP-068 slice 4c · Import contract for the DD/UI/Fx kit under <c>Assets/_Prototype/UI/Resources/Fx/</c> (AP-012 v004).
    /// The A8 ink kit is data, not colour: RGBA time maps (R underline, G divider, B hanko, A blot; 1.0 = empty), linear,
    /// one BC7 texture (C-00), clamp, mipmaps (the shader never reads below mip 3 — time precision). The kinpaku / washi
    /// tiles are single-channel, linear, repeat (BC4). The four <c>Ink.*</c> static masks are the marks' shapes: white +
    /// alpha sprites the time map gates.
    /// </summary>
    public sealed class UiFxArtImporter : AssetPostprocessor
    {
        public const string FxRoot = "Assets/_Prototype/UI/Resources/Fx/";
        public const string InkKitName = "AP012_v004_ink_timemaps_1024";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(FxRoot, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            var standalone = new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 1024 };

            if (name == InkKitName)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                standalone.format = TextureImporterFormat.BC7;
            }
            else if (name.StartsWith("AP012_tile_", StringComparison.Ordinal))
            {
                importer.textureType = TextureImporterType.SingleChannel;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                importer.SetTextureSettings(settings);
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Compressed;
                standalone.format = TextureImporterFormat.BC4;
            }
            else
            {
                // Ink.* static masks: white + alpha sprites (tinted at runtime), 1:1 UI class.
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                standalone.format = TextureImporterFormat.BC7;
            }

            importer.SetPlatformTextureSettings(standalone);
        }
    }
}
