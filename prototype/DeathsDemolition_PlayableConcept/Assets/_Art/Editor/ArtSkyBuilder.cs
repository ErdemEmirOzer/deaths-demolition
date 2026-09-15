using System.IO;
using DD.Prototype.World;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Overcast sky for the pilot scene (ADR-011-4 candidate, not a product
    /// decision).
    ///
    /// WHY NOT THE RUN SCENE'S PROCEDURAL SKY. Run.unity uses Skybox/Procedural with the sun
    /// at 42° elevation and atmosphere thickness 1.55. The gameplay camera looks 49° down
    /// and never sees the horizon, so nobody noticed that at a horizontal camera the sky
    /// reads as a warm sunset — the first pilot presentation capture showed an orange
    /// horizon, which contradicts DEC-068 (post-rain overcast) and Presentation A (flat
    /// background 0.63/0.68/0.73 linear, no sun disk). The pilot therefore swaps only the
    /// SKY for a flat overcast gradient whose horizon equals the fog colour, so fog and sky
    /// meet seamlessly. Sun, ambient and fog stay identical to Run.unity so the performance
    /// comparison stays apples to apples; the final palette/weather decision goes to the
    /// user with the art-director's comparison.
    ///
    /// Assets: <c>Textures/Derived/T_sky_BaseColor.png</c> (4x256 latitude gradient,
    /// BaseColor role so the import contract treats it as sRGB colour) and
    /// <c>Materials/M_sky_overcast.mat</c> (Skybox/Panoramic, latitude-longitude, 360°).
    /// </summary>
    public static class ArtSkyBuilder
    {
        public const string SkyTexturePath = ArtPipelinePaths.TexturesDerived + "/T_sky_BaseColor.png";
        /// <summary>
        /// WP-012 (ADR-011-4 closure): the ONE overcast sky material, shared by the pilot,
        /// the showcase and Run.unity. Under Resources because WorldAtmosphere (DD.Prototype)
        /// loads it by name at runtime; DD.Art references the asset directly.
        /// </summary>
        public const string SkyMaterialPath = ArtLibraryPaths.SkyMaterial;
        public const string SkyShaderName = "Skybox/Panoramic";

        /// <summary>Horizon colour = Run.unity fog colour, so the far fog dissolves into the sky.</summary>
        public static readonly Color Horizon = WorldAtmosphere.FogColor;

        /// <summary>Zenith: a little darker and cooler than the horizon (overcast dome).</summary>
        public static readonly Color Zenith = new Color(0.60f, 0.64f, 0.68f);

        /// <summary>Below the horizon (only visible from a low camera looking down past the ground): the Run skybox ground tint.</summary>
        public static readonly Color Nadir = new Color(0.498f, 0.49f, 0.451f);

        private const int Width = 4;
        private const int Height = 256;

        public static Material Build()
        {
            EnsureFolder(ArtPipelinePaths.TexturesDerived);
            EnsureFolder(System.IO.Path.GetDirectoryName(SkyMaterialPath).Replace('\\', '/'));

            // Rows run bottom (nadir) → top (zenith); the horizon sits in the middle row of the
            // latitude-longitude mapping. Colours are authored in sRGB (BaseColor role).
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                float v = (y + 0.5f) / Height;
                Color c = v < 0.5f
                    ? Color.Lerp(Nadir, Horizon, Mathf.SmoothStep(0f, 1f, v * 2f))
                    : Color.Lerp(Horizon, Zenith, Mathf.SmoothStep(0f, 1f, (v - 0.5f) * 2f));
                for (int x = 0; x < Width; x++)
                {
                    pixels[y * Width + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            bool changed = !File.Exists(SkyTexturePath) || !SameBytes(File.ReadAllBytes(SkyTexturePath), png);
            if (changed)
            {
                File.WriteAllBytes(SkyTexturePath, png);
                AssetDatabase.ImportAsset(SkyTexturePath, ImportAssetOptions.ForceUpdate);
            }

            var skyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyTexturePath);
            Shader shader = Shader.Find(SkyShaderName);
            if (shader == null)
            {
                throw new System.InvalidOperationException("[ArtPipeline] Shader '" + SkyShaderName + "' not found.");
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            bool created = mat == null;
            if (created)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, SkyMaterialPath);
            }

            mat.shader = shader;
            mat.SetTexture("_MainTex", skyTex);
            mat.SetFloat("_Mapping", 1f);      // latitude-longitude layout
            mat.SetFloat("_ImageType", 0f);    // 360°
            mat.SetFloat("_Layout", 0f);       // none (not stereo)
            mat.SetFloat("_Exposure", 1f);
            mat.SetFloat("_Rotation", 0f);
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            mat.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            EditorUtility.SetDirty(mat);
            Debug.Log("[ArtPipeline] Overcast sky " + (created ? "created" : "updated") + ": " + SkyMaterialPath +
                      " (gradient " + (changed ? "rewritten" : "unchanged") + ", horizon " + Horizon + ")");
            return mat;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
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
