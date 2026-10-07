using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-081 B · N2 (C-00 06.10, (a)) · The v6 sky without the 2 px line at compass 270° (K/wp-038m/kadro-0610/menu-n2-n4 §N2):
    /// DD_Sky_v3 moves from Skybox/Panoramic to DD/SkyPanorama — the built-in's 360° lat-long path with seamless derivatives — and
    /// keeps everything else. The contract here: the shader compiles with the built-in's four properties (same types, attributes,
    /// defaults: the values mean the same), one pass and no keyword; the material is the same asset (GUID, Resources name, the V6
    /// scene's RenderSettings) on the new shader with the old values and nothing stale; the panorama keeps its import contract
    /// (mips on — no contract exception) and its content wraps across u 0/1, which the shader's U Repeat sampling joins.
    /// The shader's own effect is a GPU fact: the two Explicit diagnostics render the old and the new sky round compass 270° and
    /// measure the line with menu_n2_n4_olc.py's line_ratio (ported) — the first Unity run is the real evidence.
    /// </summary>
    public sealed class SkyPanoramaTests
    {
        private const string BuiltInPanoramic = "Skybox/Panoramic";

        // DD_Sky_v3 at 3b0e4327 (Skybox/Panoramic): the asset and the values N2 carries over unchanged.
        private const string SkyV3Guid = "0a51f40e4f4ec41f49f2cccfeecfa703";
        private const string PanoramaGuid = "2a0141904f862456692573194a81acbe";
        private static readonly Color OldTint = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        private const float OldExposure = 1f, OldRotation = 0f;
        private static readonly string[] Carried = { "_MainTex", "_Tint", "_Exposure", "_Rotation" };

        // ================================================================ the shader

        [Test]
        public void SkyPanorama_Derlenir_BuiltInDortOzellik_TekPas_AnahtarYok()
        {
            Assert.AreEqual("DD/SkyPanorama", ArtSkyBuilder.SkyV3ShaderName);
            Shader shader = Shader.Find(ArtSkyBuilder.SkyV3ShaderName);
            Assert.IsNotNull(shader, "kaynak yok: " + ArtSkyBuilder.SkyV3ShaderName);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "derleme hatası: " + Messages(shader));
            Assert.AreEqual((int)RenderQueue.Background, shader.renderQueue, "Background kuyruğu (built-in ile aynı)");
            Assert.AreEqual("Skybox", shader.FindSubshaderTagValue(0, new ShaderTagId("PreviewType")).name);
            ShaderData data = ShaderUtil.GetShaderData(shader);
            Assert.AreEqual(1, data.SubshaderCount, "tek SubShader");
            Assert.AreEqual(1, data.GetSubshader(0).PassCount, "tek pas");
            Assert.AreEqual(0, (int)shader.keywordSpace.keywordCount, "anahtar yok: tek varyant");

            // The built-in's properties, one for one: the material's numbers keep their meaning ([Gamma] exposure, sRGB tint).
            Shader builtIn = Shader.Find(BuiltInPanoramic);
            Assert.IsNotNull(builtIn, BuiltInPanoramic);
            Assert.AreEqual(Carried.Length, shader.GetPropertyCount(), "yalnız taşınan dört özellik");
            const ShaderPropertyFlags meaning = ShaderPropertyFlags.Gamma | ShaderPropertyFlags.HDR | ShaderPropertyFlags.NoScaleOffset;
            foreach (string p in Carried)
            {
                int i = shader.FindPropertyIndex(p), j = builtIn.FindPropertyIndex(p);
                Assert.GreaterOrEqual(i, 0, p);
                Assert.GreaterOrEqual(j, 0, p + " (built-in)");
                ShaderPropertyType type = shader.GetPropertyType(i);
                Assert.AreEqual(builtIn.GetPropertyType(j), type, p + " türü");
                Assert.AreEqual(builtIn.GetPropertyFlags(j) & meaning, shader.GetPropertyFlags(i) & meaning, p + " bayrakları");
                switch (type)
                {
                    case ShaderPropertyType.Range:
                        Assert.AreEqual(builtIn.GetPropertyRangeLimits(j), shader.GetPropertyRangeLimits(i), p + " aralığı");
                        Assert.AreEqual(builtIn.GetPropertyDefaultFloatValue(j), shader.GetPropertyDefaultFloatValue(i), 0f, p + " varsayılanı");
                        break;
                    case ShaderPropertyType.Float:
                        Assert.AreEqual(builtIn.GetPropertyDefaultFloatValue(j), shader.GetPropertyDefaultFloatValue(i), 0f, p + " varsayılanı");
                        break;
                    case ShaderPropertyType.Color:
                        Assert.AreEqual(builtIn.GetPropertyDefaultVectorValue(j), shader.GetPropertyDefaultVectorValue(i), p + " varsayılanı");
                        break;
                    case ShaderPropertyType.Texture:
                        Assert.AreEqual(TextureDimension.Tex2D, shader.GetPropertyTextureDimension(i), p + " 2D");
                        Assert.AreEqual(builtIn.GetPropertyTextureDefaultName(j), shader.GetPropertyTextureDefaultName(i), p + " varsayılanı");
                        break;
                }
            }

            Assert.IsTrue((shader.GetPropertyFlags(shader.FindPropertyIndex("_Exposure")) & ShaderPropertyFlags.Gamma) != 0, "[Gamma] pozlama");
        }

        // ================================================================ the material

        [Test]
        public void GokV3_AyniVarlik_YeniShader_EskiDegerler_EskiKalintiYok()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ArtSkyBuilder.SkyV3MaterialPath);
            Assert.IsNotNull(mat, ArtSkyBuilder.SkyV3MaterialPath);
            Assert.AreEqual(SkyV3Guid, AssetDatabase.AssetPathToGUID(ArtSkyBuilder.SkyV3MaterialPath), "yerinde yeniden üretim: GUID aynı");
            Assert.AreSame(mat, Resources.Load<Material>(WorldAtmosphere.SkyV3ResourceName), "WorldAtmosphere aynı varlığı yükler");
            Assert.AreEqual(WorldAtmosphere.SkyV3ResourceName, WorldAtmosphere.SkyResourceFor(WorldLayoutKind.V6));
            Assert.AreEqual(ArtSkyBuilder.SkyV3ShaderName, mat.shader.name, "N2: DD_Sky_v3 DD/SkyPanorama'da");

            // The Skybox/Panoramic values of 3b0e4327, unchanged (and the builder's constants are those values).
            Texture panorama = mat.GetTexture("_MainTex");
            Assert.IsNotNull(panorama, "_MainTex");
            Assert.AreEqual(ArtSkyBuilder.SkyDec102SourcePath, AssetDatabase.GetAssetPath(panorama), "aynı panorama");
            Assert.AreEqual(PanoramaGuid, AssetDatabase.AssetPathToGUID(ArtSkyBuilder.SkyDec102SourcePath));
            Assert.AreEqual(Vector2.one, mat.GetTextureScale("_MainTex"));
            Assert.AreEqual(Vector2.zero, mat.GetTextureOffset("_MainTex"));
            Assert.AreEqual(OldTint, mat.GetColor("_Tint"), "_Tint");
            Assert.AreEqual(OldExposure, mat.GetFloat("_Exposure"), 0f, "_Exposure");
            Assert.AreEqual(OldRotation, mat.GetFloat("_Rotation"), 0f, "_Rotation (+Z u 0,25'te; güneş ve bulut gölgesi hizası)");
            Assert.AreEqual(OldTint, ArtSkyBuilder.SkyV3Tint);
            Assert.AreEqual(OldExposure, ArtSkyBuilder.SkyV3Exposure, 0f);
            Assert.AreEqual(OldRotation, ArtSkyBuilder.SkyV3Rotation, 0f);

            // Nothing of Skybox/Panoramic left in the asset: every saved property is the shader's, no keyword; the queue is the shader's.
            var so = new SerializedObject(mat);
            Assert.AreEqual(-1, so.FindProperty("m_CustomRenderQueue").intValue, "kuyruk shader'dan (Background)");
            var stale = new List<string>();
            foreach (string list in new[] { "m_SavedProperties.m_TexEnvs", "m_SavedProperties.m_Ints", "m_SavedProperties.m_Floats", "m_SavedProperties.m_Colors" })
            {
                SerializedProperty entries = so.FindProperty(list);
                if (entries == null) continue;
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty first = entries.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                    string name = first == null ? null : first.propertyType == SerializedPropertyType.String ? first.stringValue : first.FindPropertyRelative("name")?.stringValue;
                    if (string.IsNullOrEmpty(name) || !mat.HasProperty(name)) stale.Add(list + ":" + name);
                }
            }

            Assert.IsEmpty(stale, "eski özellik kaldı: " + string.Join(", ", stale));
            Assert.IsEmpty(mat.shaderKeywords, "anahtar kaldı: " + string.Join(", ", mat.shaderKeywords));
            Assert.AreEqual(0, so.FindProperty("m_InvalidKeywords").arraySize, "geçersiz anahtar kaldı");

            // Scope: v1 / v2 stay on the built-in; the V6 scene still points at this asset; the sun disc reads the same panorama, same rotation.
            Assert.AreEqual(ArtSkyBuilder.SkyShaderName, AssetDatabase.LoadAssetAtPath<Material>(ArtSkyBuilder.SkyMaterialPath).shader.name, "v1");
            Assert.AreEqual(ArtSkyBuilder.SkyShaderName, AssetDatabase.LoadAssetAtPath<Material>(ArtSkyBuilder.SkyV2MaterialPath).shader.name, "v2");
            StringAssert.Contains("m_SkyboxMaterial: {fileID: 2100000, guid: " + SkyV3Guid, File.ReadAllText(GreyboxV6Builder.ScenePath), "V6 RenderSettings");
            var sun = AssetDatabase.LoadAssetAtPath<Material>(GreyboxV6Builder.DataFolder + "/Materials/V5_Sky_SunDisc.mat");
            Assert.IsNotNull(sun, "V5_Sky_SunDisc");
            Assert.AreSame(panorama, sun.GetTexture("_SkyPano"), "güneş diski aynı panoramayı okur");
            Assert.AreEqual(mat.GetFloat("_Rotation"), sun.GetFloat("_SkyRotation"), 0f, "güneş diski aynı dönüşte");
        }

        // ================================================================ the panorama

        [Test]
        public void GokV3Panoramasi_SozlesmeAyni_MipAcik_USarmasiSurekli()
        {
            string path = ArtSkyBuilder.SkyDec102SourcePath;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, path);
            List<string> contract = ArtImportContract.ValidateTexture(importer, ArtImportContract.RoleOf(path));
            Assert.IsEmpty(contract, "sözleşme istisnası yok: " + string.Join("; ", contract));
            Assert.IsTrue(importer.mipmapEnabled, "N2 B2 (mip kapatma) reddedildi: mip açık kalır");
            Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapModeU, "u 0/1 dikişi Repeat ile birleşir (GRAD örneklemesi buna dayanır)");
            Assert.IsTrue(importer.sRGBTexture, "LDR sRGB (DecodeHDR × 1)");
            Assert.AreEqual(TextureImporterShape.Texture2D, importer.textureShape, "2D lat-long (küp değil)");
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings(ArtImportContract.StandalonePlatform);
            Assert.AreEqual(ArtImportContract.StandaloneColorFormat, standalone.format, "BC7: HDR/RGBM değil");

            // The content wraps: the column pair the sampler joins at u 0/1 (last, first) is no rougher than the panorama's own
            // neighbouring pairs (3b0e4327: 0.84 levels against a median 0.46 and a largest 1.14 — RAPOR §N2 kanıt 6).
            var png = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(png.LoadImage(File.ReadAllBytes(path), false), "PNG çözülemedi (LFS?)");
                int w = png.width, h = png.height;
                Assert.AreEqual(2 * h, w, "360° × 180° lat-long (2:1)");
                Color32[] px = png.GetPixels32();
                var pair = new double[w];   // pair[x]: mean |c(x) − c(x − 1)| over rows and channels; pair[0] is the wrap (x − 1 = w − 1)
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 a = px[row + x], b = px[row + (x + w - 1) % w];
                        pair[x] += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
                    }
                }

                for (int x = 0; x < w; x++) pair[x] /= 3.0 * h;
                var inner = new double[w - 1];
                Array.Copy(pair, 1, inner, 0, w - 1);
                Array.Sort(inner);
                double median = inner[inner.Length / 2], largest = inner[inner.Length - 1];
                string report = string.Format(CultureInfo.InvariantCulture, "wrap {0:0.000}, median {1:0.000}, largest {2:0.000}", pair[0], median, largest);
                Debug.Log("[DD-TEST] n2.panorama.wrap " + report);
                Assert.LessOrEqual(pair[0], largest, "u 0/1 çifti iç sütun çiftlerinin en pürüzlüsünden pürüzlü: " + report);
                Assert.LessOrEqual(pair[0], 3.0 * median, "u 0/1 çifti medyanın 3 katından pürüzlü: " + report);
            }
            finally
            {
                Object.DestroyImmediate(png);
            }
        }

        // ================================================================ GPU diagnostics (the first Unity run is the evidence)

        private const int Width = 640, Height = 360;
        private const float VerticalFov = 60f;
        private const float SweepStep = 0.05f;   // RAPOR §N2 doğrulama 3: 0,05° adım, ≥ 40 kare
        private const int SweepHalf = 20;        // 41 frames, compass 269.00° … 271.00°
        private const float RatioClean = 2.5f, RatioLine = 4f;   // menu_n2_n4_olc.py n2: PASS ≤ 2.5, a line > 4
        private const float BandLowDegrees = 12f, BandHighDegrees = 28f;   // the measured rows: open sky above the V6 horizon plates

        /// <summary>
        /// Sky only (no scene), camera at the origin, level, yaw 270° ± 1° in 0.05° steps: the built-in (DD_Sky_v3 as it was) shows the
        /// line in the frames whose wrap falls inside a 2×2 quad (the positive control), DD/SkyPanorama in none (line ratio ≤ 2.5), and
        /// the two agree to 1/255 outside the seam's quad columns (RAPOR §N2 açık soru 1). DD_SKY_N2_OUT (optional): every frame as PNG
        /// (old/, new/) and frames.tsv (name, seam x, rows) for menu_n2_n4_olc.py's line_ratio (arac/n2_bati_olc.py).
        /// </summary>
        [Test]
        [Explicit("WP-081 N2 GPU diagnostic: run by full name (needs a graphics device)")]
        [Category("Diagnostics")]
        public void GokDikisi_270Tarama_SadeceGok_EskiCizer_YeniCizmez_Parite()
        {
            RequireGpu();
            Material before = OldSky(), after = AssetDatabase.LoadAssetAtPath<Material>(ArtSkyBuilder.SkyV3MaterialPath);
            Assert.AreEqual(ArtSkyBuilder.SkyV3ShaderName, after.shader.name);
            Material previousSky = RenderSettings.skybox;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            string outDir = OutDir("sky");
            var go = new GameObject("N2 sky camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var tsv = new StringBuilder("frame\tdeltaDeg\tseamX\tratioOld\tratioNew\tparityMax\tparityOver1\n");
            int framesWithLine = 0, parityMax = 0;
            float worstNew = 0f, worstOld = 0f;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;   // no cyan placeholder in the measured frames
                Camera cam = MakeCamera(go, rt);
                cam.cullingMask = 0;   // the skybox only, whatever scene the runner has open
                cam.transform.position = Vector3.zero;
                for (int k = -SweepHalf; k <= SweepHalf; k++)
                {
                    float delta = k * SweepStep;
                    cam.transform.rotation = Quaternion.Euler(0f, 270f + delta, 0f);
                    float seamX = SeamColumn(delta, cam.aspect);
                    RenderSettings.skybox = before;
                    Color32[] a = Grab(cam, rt, read, true);
                    float ratioOld = LineRatio(Luma(a), seamX, BandRows());
                    Save(outDir, "old", k, read);
                    RenderSettings.skybox = after;
                    Color32[] b = Grab(cam, rt, read, true);
                    float ratioNew = LineRatio(Luma(b), seamX, BandRows());
                    Save(outDir, "new", k, read);

                    // Parity outside the seam's quad columns (the built-in's line is 2 px at an even column pair round seamX).
                    int c0 = (int)Math.Floor(seamX - 0.5f) - 2, c1 = c0 + 5, maxDiff = 0, over1 = 0;
                    for (int i = 0; i < a.Length; i++)
                    {
                        int x = i % Width;
                        if (x >= c0 && x <= c1) continue;
                        int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                        maxDiff = Math.Max(maxDiff, d);
                        if (d > 1) over1++;
                    }

                    if (ratioOld > RatioLine) framesWithLine++;
                    worstOld = Math.Max(worstOld, ratioOld);
                    worstNew = Math.Max(worstNew, ratioNew);
                    parityMax = Math.Max(parityMax, maxDiff);
                    tsv.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1:+0.00;-0.00;0.00}\t{2:0.00}\t{3:0.00}\t{4:0.00}\t{5}\t{6}\n", FrameName(k), delta, seamX, ratioOld, ratioNew, maxDiff, over1);
                }
            }
            finally
            {
                RenderSettings.skybox = previousSky;
                ShaderUtil.allowAsyncCompilation = previousAsync;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(before);
            }

            string summary = string.Format(CultureInfo.InvariantCulture, "frames {0}, old line (> {1}) in {2}, worst old {3:0.0}, worst new {4:0.00} (≤ {5}), parity max {6}/255, {7}",
                2 * SweepHalf + 1, RatioLine, framesWithLine, worstOld, worstNew, RatioClean, parityMax, Setup());
            WriteReport(outDir, tsv, summary);
            Debug.Log("[DD-TEST] n2.sky.sweep " + summary);
            if (framesWithLine == 0) Assert.Inconclusive("eski gökte de çizgi ölçülmedi: kurgu dikişi yakalamıyor — " + summary);
            Assert.LessOrEqual(worstNew, RatioClean, "DD/SkyPanorama'da 270° çizgisi: " + summary);
            Assert.LessOrEqual(parityMax, 1, "dikiş dörtlüsü dışında built-in ile fark > 1/255: " + summary);
        }

        /// <summary>
        /// V6, a west-facing TP-height pose on the plaza (PanoramaRevealCompare's plaza-W-TP eye, level), the same sweep with the
        /// runtime environment (WorldAtmosphere, layout V6): the line in the open sky above the horizon plates — old present (control),
        /// new absent. The scene is opened, never saved. DD_SKY_N2_OUT as above (v6/).
        /// </summary>
        [Test]
        [Explicit("WP-081 N2 GPU diagnostic: run by full name (needs a graphics device; opens World_V6_Greybox)")]
        [Category("Diagnostics")]
        public void GokDikisi_270Tarama_V6BatiPozu_EskiCizer_YeniCizmez()
        {
            RequireGpu();
            EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath, OpenSceneMode.Single);
            WorldLayoutKind previousLayout = WorldLayout.Current;
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            Material before = OldSky(), after = null;
            string outDir = OutDir("v6");
            var go = new GameObject("N2 V6 camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var tsv = new StringBuilder("frame\tdeltaDeg\tseamX\tratioOld\tratioNew\n");
            int framesWithLine = 0;
            float worstNew = 0f, worstOld = 0f;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                WorldLayout.SetCurrent(WorldLayoutKind.V6);
                WorldAtmosphere.ApplyEnvironment();
                after = RenderSettings.skybox;
                Assert.AreEqual(ArtSkyBuilder.SkyV3ShaderName, after != null ? after.shader.name : "(yok)", "V6 ortamı DD_Sky_v3'ü kurar");
                Camera cam = MakeCamera(go, rt);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 1200f;
                cam.transform.position = new Vector3(6f, 2.2f, 0f);   // PanoramaRevealCompare plaza-W-TP
                for (int k = -SweepHalf; k <= SweepHalf; k++)
                {
                    float delta = k * SweepStep;
                    cam.transform.rotation = Quaternion.Euler(0f, 270f + delta, 0f);
                    float seamX = SeamColumn(delta, cam.aspect);
                    RenderSettings.skybox = before;
                    float ratioOld = LineRatio(Luma(Grab(cam, rt, read, true)), seamX, BandRows());
                    Save(outDir, "old", k, read);
                    RenderSettings.skybox = after;
                    float ratioNew = LineRatio(Luma(Grab(cam, rt, read, true)), seamX, BandRows());
                    Save(outDir, "new", k, read);
                    if (ratioOld > RatioLine) framesWithLine++;
                    worstOld = Math.Max(worstOld, ratioOld);
                    worstNew = Math.Max(worstNew, ratioNew);
                    tsv.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1:+0.00;-0.00;0.00}\t{2:0.00}\t{3:0.00}\t{4:0.00}\n", FrameName(k), delta, seamX, ratioOld, ratioNew);
                }
            }
            finally
            {
                if (after != null) RenderSettings.skybox = after;
                WorldLayout.SetCurrent(previousLayout);
                ShaderUtil.allowAsyncCompilation = previousAsync;
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(before);
            }

            string summary = string.Format(CultureInfo.InvariantCulture, "frames {0}, old line (> {1}) in {2}, worst old {3:0.0}, worst new {4:0.00} (≤ {5}), {6}",
                2 * SweepHalf + 1, RatioLine, framesWithLine, worstOld, worstNew, RatioClean, Setup());
            WriteReport(outDir, tsv, summary);
            Debug.Log("[DD-TEST] n2.v6.west " + summary);
            if (framesWithLine == 0) Assert.Inconclusive("eski gökte de çizgi ölçülmedi (bant kapalı mı?): " + summary);
            Assert.LessOrEqual(worstNew, RatioClean, "V6 batı pozunda 270° çizgisi: " + summary);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The measured pipeline: the line is 2 px at render scale 1 (the editor's default tier, Cinematic); another scale resamples it.</summary>
        private static string Setup()
        {
            UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
            return string.Format(CultureInfo.InvariantCulture, "quality {0}, renderScale {1}, {2}", QualitySettings.names[QualitySettings.GetQualityLevel()],
                urp != null ? urp.renderScale : float.NaN, SystemInfo.graphicsDeviceType);
        }

        private static void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("GPU yok (-nographics): ölçüm Player/GPU'lu editörde");
        }

        /// <summary>DD_Sky_v3 as it was at 3b0e4327 (ArtSkyBuilder.BuildV3 before N2), in memory: the "before" of the A/B.</summary>
        private static Material OldSky()
        {
            Shader shader = Shader.Find(BuiltInPanoramic);
            Assert.IsNotNull(shader, BuiltInPanoramic);
            var m = new Material(shader) { name = "DD_Sky_v3 (Skybox/Panoramic, before N2)", hideFlags = HideFlags.HideAndDontSave };
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtSkyBuilder.SkyDec102SourcePath));
            m.SetFloat("_Mapping", 1f);
            m.SetFloat("_ImageType", 0f);
            m.SetFloat("_Layout", 0f);
            m.SetFloat("_Exposure", OldExposure);
            m.SetFloat("_Rotation", OldRotation);
            m.SetColor("_Tint", OldTint);
            m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            return m;
        }

        private static Camera MakeCamera(GameObject go, RenderTexture rt)
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = VerticalFov;
            cam.allowMSAA = false;
            cam.targetTexture = rt;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;   // the sky's own values: no grade, no bloom, no dither
            data.antialiasing = AntialiasingMode.None;
            data.dithering = false;
            return cam;
        }

        /// <summary>The column of world −X (Skybox/Panoramic's u 0/1) for a level camera at yaw 270° + delta: x_cam / z_cam = −tan(delta).</summary>
        private static float SeamColumn(float deltaDegrees, float aspect)
        {
            double tanHalfH = Math.Tan(VerticalFov * 0.5 * Math.PI / 180.0) * aspect;
            return (float)((0.5 - 0.5 * Math.Tan(deltaDegrees * Math.PI / 180.0) / tanHalfH) * Width);
        }

        /// <summary>Top-origin rows of the elevation band (BandLowDegrees … BandHighDegrees) for a level camera.</summary>
        private static (int y0, int y1) BandRows()
        {
            double tanHalfV = Math.Tan(VerticalFov * 0.5 * Math.PI / 180.0);
            int Row(double degrees) => (int)Math.Round((0.5 - 0.5 * Math.Tan(degrees * Math.PI / 180.0) / tanHalfV) * Height);
            return (Row(BandHighDegrees), Row(BandLowDegrees));
        }

        private static Color32[] Grab(Camera cam, RenderTexture rt, Texture2D read, bool warm)
        {
            if (warm) cam.Render();   // the first render after a skybox swap compiles / binds; the measured one is the second
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            read.Apply(false);
            RenderTexture.active = previous;
            return read.GetPixels32();
        }

        /// <summary>Pillow's RGB → L (ITU-R 601-2 in 16-bit fixed point, rounded), top-origin rows like the PNG the Python tool reads.</summary>
        private static float[] Luma(Color32[] bottomUp)
        {
            var l = new float[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                int src = y * Width, dst = (Height - 1 - y) * Width;
                for (int x = 0; x < Width; x++)
                {
                    Color32 c = bottomUp[src + x];
                    l[dst + x] = (c.r * 19595 + c.g * 38470 + c.b * 7471 + 0x8000) >> 16;
                }
            }

            return l;
        }

        /// <summary>
        /// menu_n2_n4_olc.py line_ratio, ported: per column the mean |L(x) − ½(L(x − 1) + L(x + 1))| over the rows, the largest in the
        /// window round the expected column over the median of ±80 px (> 4 a line, ≤ 2.5 clean).
        /// </summary>
        private static float LineRatio(float[] luma, float x, (int y0, int y1) rows)
        {
            int n = Width - 2;
            var sc = new double[n];
            for (int y = rows.y0; y < rows.y1; y++)
            {
                int row = y * Width;
                for (int j = 0; j < n; j++) sc[j] += Math.Abs(luma[row + j + 1] - 0.5 * (luma[row + j] + luma[row + j + 2]));
            }

            for (int j = 0; j < n; j++) sc[j] /= rows.y1 - rows.y0;
            int xi = (int)Math.Round(x);
            double win = 0;
            for (int j = Math.Max(0, xi - 9); j < Math.Min(n, xi + 7); j++) win = Math.Max(win, sc[j]);
            int b0 = Math.Max(0, xi - 81), b1 = Math.Min(n, xi + 79);
            var bg = new double[Math.Max(0, b1 - b0)];
            Array.Copy(sc, b0, bg, 0, bg.Length);
            Array.Sort(bg);
            double median = bg.Length == 0 ? 0 : bg.Length % 2 == 1 ? bg[bg.Length / 2] : 0.5 * (bg[bg.Length / 2 - 1] + bg[bg.Length / 2]);
            return (float)(win / Math.Max(median, 1e-6));
        }

        private static string FrameName(int k) => "yaw" + (270f + k * SweepStep).ToString("000.00", CultureInfo.InvariantCulture);

        private static string OutDir(string leaf)
        {
            string root = Environment.GetEnvironmentVariable("DD_SKY_N2_OUT");
            if (string.IsNullOrEmpty(root)) return null;
            string dir = Path.Combine(root, leaf);
            Directory.CreateDirectory(Path.Combine(dir, "old"));
            Directory.CreateDirectory(Path.Combine(dir, "new"));
            return dir;
        }

        private static void Save(string outDir, string which, int k, Texture2D frame)
        {
            if (outDir == null) return;
            File.WriteAllBytes(Path.Combine(outDir, which, FrameName(k) + ".png"), frame.EncodeToPNG());
        }

        private static void WriteReport(string outDir, StringBuilder tsv, string summary)
        {
            if (outDir == null) return;
            (int y0, int y1) = BandRows();
            File.WriteAllText(Path.Combine(outDir, "frames.tsv"), tsv.ToString());
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary + string.Format(CultureInfo.InvariantCulture,
                "\nrows (top-origin) {0}..{1}, {2}x{3}, vfov {4}, step {5}°\n", y0, y1, Width, Height, VerticalFov, SweepStep));
        }

        private static string Messages(Shader shader)
        {
            var lines = new List<string>();
            foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader)) lines.Add(m.severity + " " + m.message + " (" + m.file + ":" + m.line + ")");
            return string.Join("; ", lines);
        }
    }
}
