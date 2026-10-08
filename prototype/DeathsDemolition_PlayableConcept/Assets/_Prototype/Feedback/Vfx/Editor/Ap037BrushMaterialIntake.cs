#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx.Editor
{
    /// <summary>Run explicitly after the v001.1 textures and shader have been copied to this worktree.</summary>
    public static class Ap037BrushMaterialIntake
    {
        public const string TextureRoot = "Assets/_Art/VFX/AP037/v001.1";
        public const string MaterialRoot = "Assets/_Prototype/Feedback/Resources/VFX/AP037";
        private static readonly string[] Textures =
        {
            "T_VFX_BrushSplat_Kill_A", "T_VFX_BrushSplat_Kill_B", "T_VFX_BrushStain_Atlas",
            "T_VFX_InkRing_Multi_Mask", "T_VFX_InkRing_Multi_Time",
            "T_VFX_BrushShockRing_Mask", "T_VFX_BrushShockRing_Time", "T_VFX_InkDroplets_Atlas"
        };
        private static readonly string[] Materials = { "KillA", "KillB", "Stain", "Multi", "Shock", "Droplets" };
        private static readonly int[] MaskIndices = { 0, 1, 2, 3, 5, 7 };

        [MenuItem("Deaths Demolition/VFX/Intake AP037 v001.1 materials")]
        public static void Build()
        {
            Shader shader = Shader.Find("DeathsDemolition/VFX/AP037Brush");
            if (shader == null) throw new InvalidOperationException("AP037 shader missing; import shader before intake.");
            // Validate the complete input set before changing importers or creating any materials.
            for (int i = 0; i < Textures.Length; i++)
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "/" + Textures[i] + ".png") == null)
                    throw new InvalidOperationException("AP037 input missing: " + Textures[i]);
            for (int i = 0; i < Textures.Length; i++)
            {
                string path = TextureRoot + "/" + Textures[i] + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
                importer.alphaIsTransparency = false; importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.CompressedHQ;
                bool time = Textures[i].EndsWith("_Time", StringComparison.Ordinal);
                importer.alphaSource = time ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.mipMapsPreserveCoverage = Textures[i] == "T_VFX_InkDroplets_Atlas";
                importer.alphaTestReferenceValue = .5f;
                var platform = importer.GetDefaultPlatformTextureSettings();
                platform.format = time ? TextureImporterFormat.BC4 : TextureImporterFormat.BC7;
                platform.maxTextureSize = 1024; importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
            EnsureFolder(MaterialRoot);
            for (int i = 0; i < Materials.Length; i++)
            {
                string path = MaterialRoot + "/" + Materials[i] + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.SetTexture("_Mask", AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "/" + Textures[MaskIndices[i]] + ".png"));
                bool time = i == 3 || i == 4;
                material.SetFloat("_UseTime", time ? 1f : 0f);
                material.SetFloat("_InkCap", i == 4 ? 0f : 1f);
                material.SetFloat("_Soft", i == 4 ? .06f : .015f);
                material.SetTexture("_TimeMask", time
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(TextureRoot + "/" + Textures[MaskIndices[i] + 1] + ".png")
                    : Texture2D.blackTexture);
                material.SetVector("_AtlasUV", new Vector4(1f, 1f, 0f, 0f));
                material.SetColor("_Tint", Color.white); material.SetFloat("_Alpha", 1f);
                material.SetFloat("_Reveal", 1f); material.SetFloat("_WetScale", 1f);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
#endif
