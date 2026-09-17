using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-019 · ADR-019-1 · Import rules for the AP-010 effect textures under
    /// <see cref="VfxRoot"/>. They are not AP material maps (no T_&lt;role&gt;_&lt;Map&gt; names,
    /// no PBR role table), so the Import Contract v1 hands this folder to this class:
    ///
    ///   sheets / cards / decals   sRGB, straight alpha kept (alphaIsTransparency), mips on,
    ///                             CLAMP (a sheet must never wrap into the next cell), BC7,
    ///                             never streamed (an effect is always resident)
    ///   *_normal_*                tangent normal (OpenGL +Y), linear, BC5
    ///   hud marker                Sprite (single), no mips
    ///
    /// Shipped sizes (ADR-019-1 VFX class): an effect is ≤ 3.6 m across and seen from the 20 m
    /// gameplay camera, so a 2048² sheet of 256² frames is oversampled ~5×; the sheets ship at
    /// 1024 (frames 128²), decals/cards at 512, the HUD marker at 256. Estimated resident set
    /// ≈ 13 MB — the VFX line of the texture budget (<see cref="ShippedVfxBudgetMb"/>).
    /// </summary>
    public static class ArtVfxContract
    {
        public const string VfxRoot = ArtImportContract.ArtRoot + "/VFX/AP010";
        public const string ManifestPath = VfxRoot + "/AP010-vfx-manifest-v003.json";

        /// <summary>Resident budget for the VFX texture class (MB, BC7/BC5 with mips).</summary>
        /// <summary>Main VFX line. WP-019: 20 MB for the v002 set; WP-019b: 32 MB for the v003 HQ pass (ADR-019-1 amendment, reported against the release ceiling).</summary>
        public const int ShippedVfxBudgetMb = 32;
        /// <summary>WP-019b · AP-010B blood set (directive §10, high quality ≥ 2048 sheets): its own line, C-00 queue note "sevk ≤ 60 MB".</summary>
        public const int ShippedBloodBudgetMb = 60;

        /// <summary>True for a texture under a supplement folder (AP010B…), which counts against <see cref="ShippedBloodBudgetMb"/> instead of the main line.</summary>
        public static bool IsSupplementAsset(string assetPath)
        {
            return IsVfxAsset(assetPath) && !assetPath.Replace('\\', '/').StartsWith(VfxRoot + "/", StringComparison.Ordinal);
        }

        public enum VfxTextureRole
        {
            NotVfx = 0,
            Sheet = 1,
            Normal = 2,
            HudSprite = 3
        }

        /// <summary>Under <see cref="VfxRoot"/> or a supplement folder next to it (<c>AP010B</c>, the blood set — WP-019b).</summary>
        public static bool IsVfxAsset(string assetPath)
        {
            if (assetPath == null)
            {
                return false;
            }

            string p = assetPath.Replace('\\', '/');
            if (p.StartsWith(VfxRoot + "/", StringComparison.Ordinal))
            {
                return true;
            }

            string parent = VfxRoot.Substring(0, VfxRoot.LastIndexOf('/') + 1);
            if (!p.StartsWith(parent, StringComparison.Ordinal))
            {
                return false;
            }

            string folder = p.Substring(parent.Length);
            int slash = folder.IndexOf('/');
            if (slash < 0)
            {
                return false;
            }

            folder = folder.Substring(0, slash);
            return folder.Length == 6 && folder.StartsWith("AP010", StringComparison.Ordinal) && char.IsUpper(folder[5]);
        }

        /// <summary>Every VFX folder that exists now: the root plus present supplements (search roots for FindAssets).</summary>
        public static string[] VfxFolders()
        {
            var list = new System.Collections.Generic.List<string> { VfxRoot };
            string parent = VfxRoot.Substring(0, VfxRoot.LastIndexOf('/'));
            if (System.IO.Directory.Exists(parent))
            {
                foreach (string dir in System.IO.Directory.GetDirectories(parent))
                {
                    string name = System.IO.Path.GetFileName(dir);
                    if (name.Length == 6 && name.StartsWith("AP010", StringComparison.Ordinal) && char.IsUpper(name[5]))
                    {
                        list.Add(parent + "/" + name);
                    }
                }
            }

            return list.ToArray();
        }

        public static bool IsVfxTexture(string assetPath)
        {
            return IsVfxAsset(assetPath) && assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        public static VfxTextureRole RoleOf(string assetPath)
        {
            if (!IsVfxTexture(assetPath))
            {
                return VfxTextureRole.NotVfx;
            }

            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (name.Contains("_normal_"))
            {
                return VfxTextureRole.Normal;
            }

            if (name.Contains("hud_marker"))
            {
                return VfxTextureRole.HudSprite;
            }

            return VfxTextureRole.Sheet;
        }

        /// <summary>Shipped max size for a VFX texture path.</summary>
        /// <summary>
        /// Shipped size per texture (v003 sources are 1024–4096; the ship tier is the budget lever, ADR-019-1).
        /// AP010 (HQ pass): impact / muzzle / shockwave / death sheets 1024, explosion 2048 (+ emissive 1024),
        /// cards 512, scorch 1024, atlas 1024, splinter 512, gold 512, beacon column 1024 (1024×4096 → 256×1024),
        /// beacon ring / loop 1024, vignette 1024, v002 marks 256 (splats 512), HUD 256.
        /// AP010B: blood sheets 1024, droplet sheet 512, mark albedos 512, pool albedo 1024, pool growth masks 1024,
        /// normal / height packs 128 (the unlit decal shader does not read them — kept tiny, not dropped, so the
        /// catalog rows stay bound for a lit-decal pass), wave 2: 2048 sources 1024, 1024 sources 512, others as-is.
        /// </summary>
        public static int MaxSizeOf(string assetPath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (name.Contains("hud_marker")) return 256;
            if (name.StartsWith("AP010B_", StringComparison.Ordinal))
            {
                if (name.Contains("_normal_") || name.Contains("_height_")) return 128;
                if (name.Contains("droplets")) return 512;
                if (name.Contains("fb_blood_")) return 1024;
                if (name.Contains("blood_pool")) return 1024;
                if (name.Contains("decal_blood_")) return 512;
                if (name.Contains("_8x8_") || name.Contains("_2048_") || name.Contains("_2x2_")) return 1024;
                if (name.Contains("_4x4_") || name.Contains("_1024_") || name.Contains("_2x2_")) return 512;
                if (name.Contains("256x1024")) return 1024;
                return 512;
            }

            if (name.StartsWith("AP010_fb_", StringComparison.Ordinal))
            {
                return name.Contains("explosion") && !name.Contains("emissive") ? 2048 : 1024;
            }

            if (name.Contains("beacon_column")) return 1024;   // 1024×4096 → 256×1024
            if (name.Contains("beacon")) return 1024;
            if (name.Contains("vignette")) return 1024;
            if (name.Contains("atlas_armorbreak")) return 1024;
            if (name.Contains("decal_scorch")) return name.Contains("normal") ? 512 : 1024;
            if (name.StartsWith("AP010_decal_", StringComparison.Ordinal))
            {
                // 0.4 m impact marks are ~25 px at the gameplay camera: 256; the 1.2 m splats keep 512.
                return name.Contains("splat") ? 512 : 256;
            }

            if (name.Contains("gold_trail")) return 1024;         // 512×2048 → 256×1024
            return 512;                                            // cards, gold maps, sparkle
        }

        public static bool Apply(TextureImporter importer)
        {
            VfxTextureRole role = RoleOf(importer.assetPath);
            if (role == VfxTextureRole.NotVfx)
            {
                return false;
            }

            bool changed = false;
            bool normal = role == VfxTextureRole.Normal;
            bool sprite = role == VfxTextureRole.HudSprite;
            int maxSize = MaxSizeOf(importer.assetPath);
            TextureImporterFormat format = normal ? ArtImportContract.StandaloneNormalFormat : ArtImportContract.StandaloneColorFormat;

            changed |= Set(importer.textureType, normal ? TextureImporterType.NormalMap : sprite ? TextureImporterType.Sprite : TextureImporterType.Default, v => importer.textureType = v);
            if (sprite)
            {
                changed |= Set(importer.spriteImportMode, SpriteImportMode.Single, v => importer.spriteImportMode = v);
            }

            changed |= Set(importer.sRGBTexture, !normal, v => importer.sRGBTexture = v);
            changed |= Set(importer.mipmapEnabled, !sprite, v => importer.mipmapEnabled = v);
            changed |= Set(importer.streamingMipmaps, false, v => importer.streamingMipmaps = v);
            changed |= Set(importer.maxTextureSize, maxSize, v => importer.maxTextureSize = v);
            changed |= Set(importer.textureCompression, ArtImportContract.TextureCompression, v => importer.textureCompression = v);
            changed |= Set(importer.crunchedCompression, false, v => importer.crunchedCompression = v);
            changed |= Set(importer.filterMode, FilterMode.Bilinear, v => importer.filterMode = v);
            changed |= Set(importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
            changed |= Set(importer.anisoLevel, 1, v => importer.anisoLevel = v);
            changed |= Set(importer.alphaIsTransparency, !normal, v => importer.alphaIsTransparency = v);
            changed |= Set(importer.alphaSource, normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput, v => importer.alphaSource = v);
            changed |= Set(importer.mipMapsPreserveCoverage, false, v => importer.mipMapsPreserveCoverage = v);

            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(ArtImportContract.StandalonePlatform);
            if (!standalone.overridden || standalone.format != format || standalone.maxTextureSize != maxSize ||
                standalone.textureCompression != ArtImportContract.TextureCompression || standalone.crunchedCompression)
            {
                standalone.overridden = true;
                standalone.format = format;
                standalone.maxTextureSize = maxSize;
                standalone.textureCompression = ArtImportContract.TextureCompression;
                standalone.crunchedCompression = false;
                importer.SetPlatformTextureSettings(standalone);
                changed = true;
            }

            return changed;
        }

        public static List<string> Validate(TextureImporter importer)
        {
            var v = new List<string>();
            VfxTextureRole role = RoleOf(importer.assetPath);
            if (role == VfxTextureRole.NotVfx)
            {
                v.Add("path: not under " + VfxRoot);
                return v;
            }

            bool normal = role == VfxTextureRole.Normal;
            bool sprite = role == VfxTextureRole.HudSprite;
            int maxSize = MaxSizeOf(importer.assetPath);
            Check(v, "textureType", importer.textureType, normal ? TextureImporterType.NormalMap : sprite ? TextureImporterType.Sprite : TextureImporterType.Default);
            Check(v, "sRGBTexture", importer.sRGBTexture, !normal);
            Check(v, "mipmapEnabled", importer.mipmapEnabled, !sprite);
            Check(v, "streamingMipmaps", importer.streamingMipmaps, false);
            Check(v, "maxTextureSize", importer.maxTextureSize, maxSize);
            Check(v, "wrapMode", importer.wrapMode, TextureWrapMode.Clamp);
            Check(v, "alphaIsTransparency", importer.alphaIsTransparency, !normal);
            Check(v, "alphaSource", importer.alphaSource, normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput);
            TextureImporterPlatformSettings st = importer.GetPlatformTextureSettings(ArtImportContract.StandalonePlatform);
            if (!st.overridden) v.Add("Standalone: not overridden");
            Check(v, "Standalone.format", st.format, normal ? ArtImportContract.StandaloneNormalFormat : ArtImportContract.StandaloneColorFormat);
            Check(v, "Standalone.maxTextureSize", st.maxTextureSize, maxSize);
            return v;
        }

        /// <summary>Estimated resident bytes of a VFX texture at its shipped size (BC7/BC5 ≈ 1 byte/texel, full mip chain).</summary>
        public static long EstimatedShippedBytes(int sourceWidth, int sourceHeight, string assetPath, bool mipmaps)
        {
            int cap = MaxSizeOf(assetPath);
            int w = sourceWidth, h = sourceHeight;
            while (Mathf.Max(w, h) > cap)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }

            long bytes = 0;
            while (w >= 1 && h >= 1)
            {
                bytes += (long)Mathf.Max(4, w) * Mathf.Max(4, h);
                if (!mipmaps || (w == 1 && h == 1)) break;
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }

            return bytes;
        }

        private static bool Set<T>(T current, T wanted, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted))
            {
                return false;
            }

            apply(wanted);
            return true;
        }

        private static void Check<T>(List<string> v, string field, T actual, T expected)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected))
            {
                v.Add(field + ": " + actual + " (expected " + expected + ")");
            }
        }
    }
}
