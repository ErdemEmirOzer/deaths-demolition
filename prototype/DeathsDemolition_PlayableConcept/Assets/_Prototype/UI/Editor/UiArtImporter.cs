using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// Import contract for the UI art under <c>Assets/_Prototype/UI/Art/</c> (WP-022, ADR-022-1).
    ///
    /// Every PNG there becomes a Sprite: no mipmaps, clamp, bilinear, sRGB, alpha is
    /// transparency, max 1024 (ADR-015-1 UI class). Atlases listed in the AP-012 manifest
    /// next to them are sliced into named sub-sprites with the manifest's rects, pivots and
    /// 9-slice borders, so the art package - not a hand-edited importer - decides where
    /// each sprite is. Re-importing after a manifest change re-slices.
    /// </summary>
    public sealed class UiArtImporter : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/_Prototype/UI/Art/";
        public const string ManifestName = "AP012-ui-manifest.json";

        [Serializable]
        private sealed class ManifestSprite
        {
            public string name;
            public int x, y, w, h;
            public float[] pivot;
            public int[] border;
        }

        [Serializable]
        private sealed class ManifestAtlas
        {
            public string file;
            public int width, height;
            public ManifestSprite[] sprites;
        }

        [Serializable]
        private sealed class Manifest
        {
            public string schema;
            public ManifestAtlas[] atlases;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.npotScale = TextureImporterNPOTScale.None;

            // A manifest edit must re-slice the atlas even when the PNG bytes are unchanged.
            string manifestPath = Path.Combine(Path.GetDirectoryName(assetPath) ?? string.Empty, ManifestName).Replace('\\', '/');
            if (File.Exists(manifestPath))
            {
                context.DependsOnSourceAsset(manifestPath);
            }

            ManifestAtlas atlas = FindAtlas(assetPath);
            if (atlas == null)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                return;
            }

            importer.spriteImportMode = SpriteImportMode.Multiple;
            var sheet = new SpriteMetaData[atlas.sprites.Length];
            for (int i = 0; i < atlas.sprites.Length; i++)
            {
                ManifestSprite s = atlas.sprites[i];
                var meta = new SpriteMetaData
                {
                    name = s.name,
                    rect = new Rect(s.x, s.y, s.w, s.h),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = s.pivot != null && s.pivot.Length == 2 ? new Vector2(s.pivot[0], s.pivot[1]) : new Vector2(0.5f, 0.5f),
                    border = s.border != null && s.border.Length == 4
                        ? new Vector4(s.border[0], s.border[1], s.border[2], s.border[3])
                        : Vector4.zero
                };
                sheet[i] = meta;
            }

#pragma warning disable CS0618 // spritesheet is the 2022.3 API for scripted slicing
            importer.spritesheet = sheet;
#pragma warning restore CS0618
        }

        /// <summary>The manifest entry for a PNG, found next to it; null for single sprites.</summary>
        private static ManifestAtlas FindAtlas(string pngPath)
        {
            string folder = Path.GetDirectoryName(pngPath);
            string manifestPath = folder == null ? null : Path.Combine(folder, ManifestName).Replace('\\', '/');
            if (manifestPath == null || !File.Exists(manifestPath))
            {
                return null;
            }

            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UI] Could not read " + manifestPath + ": " + e.Message);
                return null;
            }

            if (manifest == null || manifest.atlases == null)
            {
                return null;
            }

            string file = Path.GetFileName(pngPath);
            for (int i = 0; i < manifest.atlases.Length; i++)
            {
                if (string.Equals(manifest.atlases[i].file, file, StringComparison.Ordinal))
                {
                    return manifest.atlases[i];
                }
            }

            return null;
        }

        /// <summary>Editor-side listing of the atlas sprite names a manifest declares (tests, builder).</summary>
        public static List<string> ManifestSpriteNames(string manifestPath)
        {
            var names = new List<string>();
            if (!File.Exists(manifestPath))
            {
                return names;
            }

            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest?.atlases == null)
            {
                return names;
            }

            foreach (ManifestAtlas atlas in manifest.atlases)
            {
                if (atlas.sprites == null)
                {
                    continue;
                }

                foreach (ManifestSprite s in atlas.sprites)
                {
                    names.Add(s.name);
                }
            }

            return names;
        }
    }
}
