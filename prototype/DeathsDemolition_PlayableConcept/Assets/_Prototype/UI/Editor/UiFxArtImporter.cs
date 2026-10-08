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
    /// WP-098A (C-00 08.10) · AP-034 v002.1 icon polish (README "İçe alım"): I1 <c>ap034_icons_g1cavity_1024</c> is data -
    /// R cavity / AO, G ink density on G1's UVs one-to-one - linear, clamp, no mips (the Icon mode reads mip 0 only), BC7;
    /// I2 <c>ap034_tile_inkgrain_256</c> is one linear channel, repeat, BC4, read 1:1 in screen pixels - point, no mips;
    /// I3 <c>ap034_ramp_sheen_256x16</c> is an sRGB colour ramp, clamp, no mips, uncompressed (16 KiB; row d reads none of
    /// it - imported for the sheen colour pass, not bound).
    /// </summary>
    public sealed class UiFxArtImporter : AssetPostprocessor
    {
        public const string FxRoot = "Assets/_Prototype/UI/Resources/Fx/";
        public const string InkKitName = "AP012_v004_ink_timemaps_1024";
        public const string IconCavityName = "ap034_icons_g1cavity_1024";
        public const string IconGrainName = "ap034_tile_inkgrain_256";
        public const string IconSheenRampName = "ap034_ramp_sheen_256x16";

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
            else if (name == IconCavityName)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                standalone.format = TextureImporterFormat.BC7;
            }
            else if (name == IconGrainName)
            {
                importer.textureType = TextureImporterType.SingleChannel;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                importer.SetTextureSettings(settings);
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Point;   // 1:1 in screen pixels: one texel per pixel, the Twin's index
                importer.textureCompression = TextureImporterCompression.Compressed;
                standalone.format = TextureImporterFormat.BC4;
            }
            else if (name.StartsWith("ap034_kit_", StringComparison.Ordinal) && name.EndsWith("_time", StringComparison.Ordinal))
            {
                // WP-098A (C-00 08.10): AP-034 v001.1 motion time maps (M3 wipe) - data, one linear channel, clamp, mipmaps (the
                // shader never reads below mip 3, as the A8 kit), BC4.
                importer.textureType = TextureImporterType.SingleChannel;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                importer.SetTextureSettings(settings);
                importer.sRGBTexture = false;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Compressed;
                standalone.format = TextureImporterFormat.BC4;
            }
            else if (name == IconSheenRampName)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                standalone.format = TextureImporterFormat.RGBA32;
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
