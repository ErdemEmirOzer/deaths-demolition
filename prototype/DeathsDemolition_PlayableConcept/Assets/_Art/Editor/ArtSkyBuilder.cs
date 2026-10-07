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

        /// <summary>
        /// WP-038E (2) · AP-019 overcast sky band (`T_skyband_BaseColor.png`, u = azimuth, v = elevation 0 → 90°, bottom row =
        /// horizon = fog colour). The panoramic skybox wants a full latitude-longitude image, so the derived sky is the band on
        /// the upper half and the band's bottom row (the fog colour) below the horizon — the old 4×256 gradient stays the
        /// fallback when the band is absent (Sky class cap 1024 → 1024×512 shipped).
        /// </summary>
        public const string SkyBandSourcePath = ArtPipelinePaths.TexturesSource + "/T_skyband_BaseColor.png";
        public const int LatLongWidth = 2048, LatLongHeight = 1024;

        private static byte[] BuildLatLongFromBand() => BuildLatLongFromBand(SkyBandSourcePath);

        private static byte[] BuildLatLongFromBand(string bandPath)
        {
            var band = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!ImageConversion.LoadImage(band, File.ReadAllBytes(bandPath), false)) throw new System.InvalidOperationException("[ArtPipeline] cannot decode " + bandPath);
            var tex = new Texture2D(LatLongWidth, LatLongHeight, TextureFormat.RGB24, false);
            var pixels = new Color32[LatLongWidth * LatLongHeight];
            int half = LatLongHeight / 2;
            for (int y = 0; y < LatLongHeight; y++)
            {
                // v: 0 = nadir, 0.5 = horizon, 1 = zenith. The band covers 0.5..1; below the horizon the band's bottom row (fog colour).
                float v = y < half ? 0f : (y - half + 0.5f) / half;
                for (int x = 0; x < LatLongWidth; x++)
                {
                    Color c = band.GetPixelBilinear((x + 0.5f) / LatLongWidth, v);
                    pixels[y * LatLongWidth + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex); Object.DestroyImmediate(band);
            return png;
        }

        private static byte[] BuildGradient()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                float v = (y + 0.5f) / Height;
                Color c = v < 0.5f
                    ? Color.Lerp(Nadir, Horizon, Mathf.SmoothStep(0f, 1f, v * 2f))
                    : Color.Lerp(Horizon, Zenith, Mathf.SmoothStep(0f, 1f, (v - 0.5f) * 2f));
                for (int x = 0; x < Width; x++) pixels[y * Width + x] = c;
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        /// <summary>WP-075 C3 · the v6 layout's sky (AP-023 sky band v2, P-05 sky-v2 manifest: lat-long upper half, no clouds — the clouds are planes).</summary>
        public const string SkyBandV2SourcePath = ArtPipelinePaths.TexturesSource + "/T_skyband_v2_BaseColor.png";
        public const string SkyV2TexturePath = ArtPipelinePaths.TexturesDerived + "/T_sky_v2_BaseColor.png";
        public const string SkyV2MaterialPath = ArtPipelinePaths.ArtRoot + "/Resources/DD_Sky_v2.mat";

        public static Material Build() => Build(SkyBandSourcePath, SkyTexturePath, SkyMaterialPath);

        /// <summary>WP-075 C3 · DD_Sky_v2 (Resources): WorldAtmosphere puts it on the skybox when the live layout is v6.</summary>
        public static Material BuildV2() => Build(SkyBandV2SourcePath, SkyV2TexturePath, SkyV2MaterialPath);

        /// <summary>DEC-102 r2 · the v6 layout's sky: AP-023 sky_v3's broken-cloud panorama (staged by wp-074/tools/stage-sky-v3.py, rolled
        /// to Skybox/Panoramic's azimuth: +Z at u 0.25) as a 360° lat-long, as is — no band conversion, no sun disc (DD/SunDisc draws it).</summary>
        public const string SkyDec102SourcePath = ArtPipelinePaths.TexturesSource + "/T_sky_dec102_BaseColor.png";
        public const string SkyV3MaterialPath = ArtPipelinePaths.ArtRoot + "/Resources/DD_Sky_v3.mat";

        /// <summary>
        /// WP-081 B · N2 (C-00 06.10, (a)): DD_Sky_v3's shader. Skybox/Panoramic's lat-long u wraps 1 → 0 on world −X (compass 270°) and its
        /// tex2D takes the coarsest mip in the 2×2 quads across the wrap: a 2 px line in the menu (S05, S03) and in every west-facing V6
        /// camera. DD/SkyPanorama is the built-in's 360° path with seamless derivatives (Tarini 2012); same mapping (+Z at u 0.25), rotation
        /// and colour, the texture and its import contract unchanged (mips stay on). v1 / v2 keep <see cref="SkyShaderName"/>.
        /// </summary>
        public const string SkyV3ShaderName = "DD/SkyPanorama";

        /// <summary>DD_Sky_v3's look (DEC-102 r2), carried over from its Skybox/Panoramic material unchanged: neutral tint 0.5 (× the
        /// shader's unity_ColorSpaceDouble ≈ 0.983 linear), exposure 1, rotation 0.</summary>
        public static readonly Color SkyV3Tint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        public const float SkyV3Exposure = 1f;
        public const float SkyV3Rotation = 0f;

        public static Material BuildV3()
        {
            var skyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyDec102SourcePath);
            if (skyTex == null) throw new System.InvalidOperationException("[ArtPipeline] sky v3 panorama missing: " + SkyDec102SourcePath);
            Shader shader = Shader.Find(SkyV3ShaderName);
            if (shader == null) throw new System.InvalidOperationException("[ArtPipeline] Shader '" + SkyV3ShaderName + "' not found.");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyV3MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, SkyV3MaterialPath);
            }

            // Rebuilt in place (same file, same GUID: World_V6_Greybox's RenderSettings and WorldAtmosphere's Resources.Load keep it).
            mat.shader = shader;
            mat.SetTexture("_MainTex", skyTex);
            mat.SetFloat("_Exposure", SkyV3Exposure);
            mat.SetFloat("_Rotation", SkyV3Rotation);   // DD/SunDisc reads it back (WorldV5Sky.SunDiscMaterial)
            mat.SetColor("_Tint", SkyV3Tint);
            DropStaleProperties(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>WP-081 B · N2: DD_Sky_v3 alone, saved — no V6 rebuild, no bake (WorldV5Sky.Build calls <see cref="BuildV3"/> too).
        /// Batch: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtSkyBuilder.RebuildV3</c>.</summary>
        [MenuItem("DD/Art/Rebuild Sky v3 (WP-081 N2)", priority = 333)]
        public static void RebuildV3()
        {
            Material mat = BuildV3();
            AssetDatabase.SaveAssets();
            Debug.Log("[ArtPipeline] Sky v3 rebuilt: " + SkyV3MaterialPath + " (" + mat.shader.name + ", " + AssetDatabase.GetAssetPath(mat.GetTexture("_MainTex")) + ")");
        }

        /// <summary>
        /// The asset keeps what its shader declares, nothing else: a material keeps the saved values of its previous shader's properties
        /// (here Skybox/Panoramic's _Mapping, _ImageType, _Layout, _MirrorOnBack) and the keywords its shader does not declare
        /// (_MAPPING_LATITUDE_LONGITUDE_LAYOUT) across a shader change; they go, so the .mat depends on this builder only. An entry whose
        /// name cannot be read is kept.
        /// </summary>
        private static void DropStaleProperties(Material mat)
        {
            var so = new SerializedObject(mat);
            foreach (string list in new[] { "m_SavedProperties.m_TexEnvs", "m_SavedProperties.m_Ints", "m_SavedProperties.m_Floats", "m_SavedProperties.m_Colors" })
            {
                SerializedProperty entries = so.FindProperty(list);
                if (entries == null || !entries.isArray) continue;
                for (int i = entries.arraySize - 1; i >= 0; i--)
                {
                    SerializedProperty first = entries.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                    string name = first == null ? null
                        : first.propertyType == SerializedPropertyType.String ? first.stringValue
                        : first.FindPropertyRelative("name")?.stringValue;
                    if (!string.IsNullOrEmpty(name) && !mat.HasProperty(name)) entries.DeleteArrayElementAtIndex(i);
                }
            }

            SerializedProperty invalid = so.FindProperty("m_InvalidKeywords");
            if (invalid != null && invalid.isArray) invalid.ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material Build(string bandPath, string texturePath, string materialPath)
        {
            string SkyBandSourcePath = bandPath, SkyTexturePath = texturePath, SkyMaterialPath = materialPath;
            EnsureFolder(ArtPipelinePaths.TexturesDerived);
            EnsureFolder(System.IO.Path.GetDirectoryName(SkyMaterialPath).Replace('\\', '/'));

            byte[] png = File.Exists(SkyBandSourcePath) ? BuildLatLongFromBand(SkyBandSourcePath) : BuildGradient();

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
                      " (" + (File.Exists(SkyBandSourcePath) ? "AP-019 band lat-long" : "gradient") + " " + (changed ? "rewritten" : "unchanged") + ", horizon " + Horizon + ")");
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
