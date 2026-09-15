using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Packed AP map → URP MetallicSmoothness mask (brief B-2).
    ///
    /// AP-001 bakes a glTF-style packed map: R unused (1), G roughness, B metallic, A 1.
    /// URP/Lit reads metallic from R and smoothness from A of <c>_MetallicGlossMap</c>
    /// (with <c>_SmoothnessTextureChannel</c> = Metallic Alpha), so the channels have to
    /// move: R ← B, A ← 255 − G (smoothness = 1 − roughness), G and B zero. Nothing else
    /// is touched - no resampling, no colour management (the map is linear data), and the
    /// 16-bit source is quantised to 8 bits by rounding, which is exactly what BC7 would
    /// have done to it anyway.
    ///
    /// The result is a DERIVED asset. It lives in <c>Textures/Derived</c>, never in the
    /// byte-identical source set, and carries a provenance sidecar naming the source
    /// file, its SHA-256 and the algorithm, so the asset gate can prove the mask was
    /// produced from the source it claims and re-generate it when the source changes.
    ///
    /// Idempotent and quiet in git: when the mask bytes and the recorded source hash are
    /// already current, neither file is rewritten (the sidecar keeps its first timestamp).
    /// </summary>
    public static class ArtTextureRepack
    {
        public const string Algorithm =
            "MetallicSmoothness v1: out.R = src.B (metallic), out.G = 0, out.B = 0, out.A = 255 - src.G " +
            "(smoothness = 1 - roughness); 16-bit source channels rounded to 8-bit (v * 255 / 65535); " +
            "8-bit RGBA PNG, no resampling";

        [Serializable]
        public sealed class Provenance
        {
            public string source;
            public string sourceSha256;
            public string algorithm;
            public string utc;
            public string outputSha256;
        }

        /// <summary>Repacks every role that ships a packed map (<see cref="ArtFamilies.PackedTextureRoles"/>).</summary>
        public static void BuildAll()
        {
            string[] roles = ArtFamilies.PackedTextureRoles;
            for (int i = 0; i < roles.Length; i++)
            {
                BuildMask(roles[i]);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ArtPipeline] Texture repack: " + roles.Length + " masks in " + ArtPipelinePaths.TexturesDerived);
        }

        /// <summary>WP-012 · Repacks every library texture role that ships a Packed map (<see cref="ArtLibraryMaterialRoles.PackedTextureRoles"/>).</summary>
        public static void BuildAllLibrary()
        {
            string[] roles = ArtLibraryMaterialRoles.PackedTextureRoles();
            for (int i = 0; i < roles.Length; i++)
            {
                BuildMask(roles[i]);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ArtPipeline] Library masks: " + roles.Length + " in " + ArtPipelinePaths.TexturesDerived);
        }

        /// <summary>
        /// Builds (or refreshes) the mask for one role and returns its asset path. Throws
        /// when the packed source is missing or cannot be decoded.
        /// </summary>
        public static string BuildMask(string role)
        {
            string sourcePath = ArtPipelinePaths.SourceTexture(role, "Packed");
            string outputPath = ArtPipelinePaths.DerivedMask(role);
            string provenancePath = ArtPipelinePaths.DerivedMaskProvenance(role);

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("[ArtPipeline] Packed source texture is missing for role '" + role + "'",
                    sourcePath);
            }

            byte[] sourceBytes = File.ReadAllBytes(sourcePath);
            string sourceSha = Sha256Hex(sourceBytes);

            byte[] png = Repack(sourceBytes, sourcePath);
            string outputSha = Sha256Hex(png);

            EnsureFolder(ArtPipelinePaths.TexturesDerived);

            bool current = File.Exists(outputPath) && File.Exists(provenancePath) &&
                           Sha256Hex(File.ReadAllBytes(outputPath)) == outputSha &&
                           ReadProvenanceSourceSha(provenancePath) == sourceSha;

            if (!current)
            {
                File.WriteAllBytes(outputPath, png);

                var provenance = new Provenance
                {
                    source = sourcePath,
                    sourceSha256 = sourceSha,
                    algorithm = Algorithm,
                    utc = DateTime.UtcNow.ToString("o"),
                    outputSha256 = outputSha
                };
                File.WriteAllText(provenancePath, JsonUtility.ToJson(provenance, true) + "\n", new UTF8Encoding(false));
            }

            // Import (or re-import) so ArtAssetPostprocessor applies the contract to the new
            // PNG before any material binds it. Unchanged bytes still need a first import
            // when the file is new to the AssetDatabase.
            if (!current || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(outputPath)))
            {
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(provenancePath, ImportAssetOptions.ForceUpdate);
            }

            Debug.Log("[ArtPipeline] Mask " + (current ? "current" : "written") + ": " + outputPath +
                      " (source sha256 " + sourceSha.Substring(0, 12) + "…)");
            return outputPath;
        }

        /// <summary>Lower-case hex SHA-256, the same encoding the manifests use.</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------- internals

        /// <summary>Decodes the packed PNG, moves the channels and encodes an 8-bit PNG.</summary>
        private static byte[] Repack(byte[] sourceBytes, string sourcePath)
        {
            // Linear = true only documents intent; LoadImage never colour-converts and the
            // pixels below are read raw. The texture is temporary and destroyed on exit.
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Texture2D mask = null;
            try
            {
                if (!ImageConversion.LoadImage(source, sourceBytes, false))
                {
                    throw new InvalidOperationException("[ArtPipeline] Could not decode " + sourcePath);
                }

                int width = source.width;
                int height = source.height;
                Color32[] pixels = ReadPixels(source);

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    pixels[i] = new Color32(p.b, 0, 0, (byte)(255 - p.g));
                }

                mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                mask.SetPixels32(pixels);
                mask.Apply(false, false);

                byte[] png = ImageConversion.EncodeToPNG(mask);
                if (png == null || png.Length == 0)
                {
                    throw new InvalidOperationException("[ArtPipeline] PNG encode failed for " + sourcePath);
                }

                return png;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (mask != null)
                {
                    UnityEngine.Object.DestroyImmediate(mask);
                }
            }
        }

        /// <summary>
        /// Raw 8-bit channels of a texture LoadImage produced. A 16-bit PNG decodes to
        /// RGBA64, which is read directly and rounded so the two high bits of precision
        /// are not simply truncated; everything else goes through GetPixels32.
        /// </summary>
        private static Color32[] ReadPixels(Texture2D texture)
        {
            if (texture.format == TextureFormat.RGBA64 || texture.format == TextureFormat.RGB48)
            {
                int channels = texture.format == TextureFormat.RGBA64 ? 4 : 3;
                NativeArray<ushort> raw = texture.GetPixelData<ushort>(0);
                int count = texture.width * texture.height;
                var result = new Color32[count];
                for (int i = 0; i < count; i++)
                {
                    int o = i * channels;
                    byte a = channels == 4 ? To8(raw[o + 3]) : (byte)255;
                    result[i] = new Color32(To8(raw[o]), To8(raw[o + 1]), To8(raw[o + 2]), a);
                }

                return result;
            }

            return texture.GetPixels32();
        }

        private static byte To8(ushort v)
        {
            return (byte)((v * 255 + 32767) / 65535);
        }

        private static string ReadProvenanceSourceSha(string provenancePath)
        {
            try
            {
                var p = JsonUtility.FromJson<Provenance>(File.ReadAllText(provenancePath));
                return p != null ? p.sourceSha256 : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
