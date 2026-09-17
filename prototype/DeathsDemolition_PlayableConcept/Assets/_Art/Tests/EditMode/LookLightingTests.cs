using System;
using System.Collections.Generic;
using System.IO;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-029 · The look recipe as a LUT (ADR-029-1) and the bake policy (ADR-029-2): the
    /// neutral strips are true identities on their own importer contract; the CPU sampler and
    /// the ΔE tool are sane (identity → 0); the chosen look (when present) is the pipeline's LUT
    /// size, imports linear/uncompressed, is bound to the overcast profile at full contribution
    /// and scores ≤ 6 mean ΔE against its Lightroom frame; the renderer carries the SSAO
    /// feature, the 60 m shadows and MSAA 2× (ADR-025-2) with HDR untouched; World_AP005 owns the Mixed sun,
    /// the probe group, the reflection probes and the lighting settings; environment models
    /// carry lightmap UVs and the characters do not; the bake report stays in budget.
    /// </summary>
    [TestFixture]
    [Category("WP029")]
    public sealed class LookLightingTests
    {
        private static readonly Color32[] Samples =
        {
            new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255), new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(0, 0, 255, 255),
            new Color32(128, 128, 128, 255), new Color32(33, 66, 99, 255), new Color32(200, 180, 150, 255), new Color32(12, 250, 140, 255), new Color32(90, 20, 210, 255)
        };

        // ================================================================ LUT strips

        [Test]
        public void NotrSeritler_1024x32_4096x64_Kimlik_ImporterSozlesmesi()
        {
            foreach ((string path, int size) in new[] { (LutBuilder.Neutral32, 32), (LutBuilder.Neutral64, 64) })
            {
                Assert.IsTrue(File.Exists(path), path + " yok (Run Look koşmalı)");
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, path);
                Assert.IsEmpty(ArtImportContract.ValidateLut(importer), path + ": " + string.Join("; ", ArtImportContract.ValidateLut(importer)));
                LutBuilder.Strip strip = LutBuilder.Load(path);
                Assert.IsTrue(strip.IsValid, path + " şerit değil");
                Assert.AreEqual(size, strip.size);
                Assert.AreEqual(size * size, strip.width);
                foreach (Color32 c in Samples)
                {
                    Color32 o = LutBuilder.Sample(strip, c);
                    Assert.LessOrEqual(Math.Abs(o.r - c.r) + Math.Abs(o.g - c.g) + Math.Abs(o.b - c.b), size == 32 ? 3 : 2, path + " kimlik " + c + " → " + o);
                }

                // Corner texels: (0,0,0) bottom-left of block 0, (1,1,1) top-right of the last block.
                Assert.AreEqual(new Color32(0, 0, 0, 255), strip.Texel(0, 0));
                Assert.AreEqual(new Color32(255, 255, 255, 255), strip.Texel(strip.width - 1, strip.height - 1));
                Assert.AreEqual(255, strip.Texel(size - 1, 0).r, "kırmızı blok içinde x boyunca");
                Assert.AreEqual(255, strip.Texel(0, size - 1).g, "yeşil yukarı doğru");
                Assert.AreEqual(255, strip.Texel((size - 1) * size, 0).b, "mavi blok seçer");
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(LutBuilder.Neutral32);
            Assert.IsNotNull(tex);
            Assert.AreEqual(1024, tex.width); Assert.AreEqual(32, tex.height);
            Assert.AreEqual(0, tex.mipmapCount - 1, "mip yok");
        }

        [Test]
        public void DeltaEAraci_KimlikSifir_FarkPozitif()
        {
            var a = new Color32[Samples.Length * 100];
            for (int i = 0; i < a.Length; i++) a[i] = Samples[i % Samples.Length];
            Assert.AreEqual(0f, LutBuilder.MeanDeltaE(a, a, out float p95, out float max), 1e-6f);
            Assert.AreEqual(0f, max, 1e-6f);
            var b = (Color32[])a.Clone();
            for (int i = 0; i < b.Length; i++) b[i] = new Color32((byte)Mathf.Min(255, b[i].r + 20), b[i].g, b[i].b, 255);
            float mean = LutBuilder.MeanDeltaE(a, b, out p95, out max);
            Assert.Greater(mean, 1f); Assert.GreaterOrEqual(p95, mean); Assert.GreaterOrEqual(max, p95);
            // The identity strip leaves a frame unchanged through the CPU sampler.
            LutBuilder.Strip strip = LutBuilder.Load(LutBuilder.Neutral32);
            Color32[] graded = LutBuilder.Apply(strip, a);
            Assert.Less(LutBuilder.MeanDeltaE(a, graded, out _, out float maxId), 0.6f, "kimlik LUT kareyi değiştirmemeli");
            Assert.Less(maxId, 2.5f);
            Debug.Log("[DD-TEST] wp029.lut.tool identityMax=" + maxId.ToString("F3") + " shiftedMean=" + mean.ToString("F2"));
        }

        [Test]
        public void SecilenLook_VarsaSeritSozlesmesi_ProfilBaglantisi_DeltaE()
        {
            bool exists = File.Exists(LutBuilder.Look);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            Assert.IsNotNull(profile);
            Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping) && tonemapping.mode.value == TonemappingMode.Neutral, "Neutral tonemapping (DEC-077 altın ayrışması)");
            if (!exists)
            {
                bool hasLookup = profile.TryGet(out ColorLookup inactive);
                Assert.IsTrue(!hasLookup || !inactive.active || inactive.texture.value == null, "LUT yokken Color Lookup etkin olmamalı");
                Debug.Log("[DD-TEST] wp029.look REPORTED: DD_Look_v1.png yok (kullanıcı seçimi bekleniyor) — LUT kapıları atlandı");
                return;
            }

            var importer = AssetImporter.GetAtPath(LutBuilder.Look) as TextureImporter;
            Assert.IsEmpty(ArtImportContract.ValidateLut(importer), string.Join("; ", ArtImportContract.ValidateLut(importer)));
            LutBuilder.Strip strip = LutBuilder.Load(LutBuilder.Look);
            Assert.IsTrue(strip.IsValid);
            Assert.AreEqual(LutBuilder.PipelineLutSize, strip.size, "pipeline colorGradingLutSize ile eş");
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ArtLightingBake.UrpAssetPath);
            Assert.AreEqual(strip.size, urp.colorGradingLutSize);
            Assert.IsTrue(File.Exists(LutBuilder.RecipeJson), "reçete kaydı");
            Assert.IsTrue(profile.TryGet(out ColorLookup lookup) && lookup.active, "Color Lookup etkin");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(LutBuilder.Look), lookup.texture.value);
            Assert.AreEqual(1f, lookup.contribution.value, 1e-4f);

            string val = ArtImportContract.LutRoot + "/Validation";
            string src = val + "/center-post-source.png", tgt = val + "/center-post-lightroom.png";
            if (File.Exists(src) && File.Exists(tgt))
            {
                LutBuilder.Result r = LutBuilder.Measure(LutBuilder.Look, src, tgt, null);
                Debug.Log("[DD-TEST] wp029.look.deltaE mean=" + r.meanDeltaE.ToString("F2") + " p95=" + r.p95DeltaE.ToString("F2") + " max=" + r.maxDeltaE.ToString("F2") + " moved=" + r.sourceVsTargetDeltaE.ToString("F2") + " (" + r.width + "x" + r.height + ")");
                Assert.LessOrEqual(r.meanDeltaE, LutBuilder.MaxMeanDeltaE, "ADR-029-1: Lightroom ↔ LUT ortalama ΔE ≤ 6");
            }
            else
            {
                Debug.LogWarning("[DD-TEST] wp029.look REPORTED: doğrulama çifti yok (" + val + ")");
            }
        }

        // ================================================================ renderer

        [Test]
        public void Renderer_SsaoOzelligi_Golge60m4Kaskad_Msaa2x_HdrLutBoyutuDegismedi()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ArtLightingBake.UrpRendererPath);
            Assert.IsNotNull(renderer);
            ScriptableRendererFeature ssao = null;
            foreach (ScriptableRendererFeature f in renderer.rendererFeatures) if (f != null && f.GetType().FullName == ArtLightingBake.SsaoTypeName) ssao = f;
            Assert.IsNotNull(ssao, "SSAO renderer feature");
            Assert.IsTrue(ssao.isActive);
            var so = new SerializedObject(ssao);
            SerializedProperty s = so.FindProperty("m_Settings");
            Assert.IsNotNull(s);
            Assert.IsTrue(s.FindPropertyRelative("Downsample").boolValue, "düşük maliyet: downsample");
            Assert.IsTrue(s.FindPropertyRelative("AfterOpaque").boolValue, "düşük maliyet: after opaque");
            Assert.AreEqual(ArtLightingBake.SsaoIntensity, s.FindPropertyRelative("Intensity").floatValue, 1e-4f);
            Assert.AreEqual(ArtLightingBake.SsaoRadius, s.FindPropertyRelative("Radius").floatValue, 1e-4f);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ArtLightingBake.UrpAssetPath);
            Assert.AreEqual(ArtLightingBake.ShadowDistance, asset.shadowDistance, 1e-3f);
            Assert.AreEqual(ArtLightingBake.ShadowCascadeCount, asset.shadowCascadeCount);
            // WP-038G (M3 closure K1/K2): 2 cascades, SSAO from depth (no DepthNormals prepass), and the realtime character fill in the world scene.
            Assert.AreEqual(2, asset.shadowCascadeCount, "WP-038G: 2 cascade");
            Assert.AreEqual(ArtLightingBake.SsaoSource, s.FindPropertyRelative("Source").enumValueIndex, "SSAO kaynağı Depth (WP-038G)");
            Assert.AreEqual(2, asset.msaaSampleCount, "MSAA 4× → 2× (ADR-025-2 ölçümü: RT sürücü belleği 413,6 → 334,3 MB; C-00 teknik kararı WP-029'da uygulandı)");
            Assert.IsTrue(asset.supportsHDR, "HDR WP-025'e kadar değişmez");
            Assert.AreEqual(32, asset.colorGradingLutSize);
            Assert.AreEqual(ColorGradingMode.LowDynamicRange, asset.colorGradingMode, "LDR grading (kullanıcı LUT'u sRGB'de)");
        }

        // ================================================================ world bake set-up

        [Test]
        public void Dunya_BakedGunes_ProbeGrubu_ReflectionProbelar_LightingAyarlari()
        {
            Scene scene = EditorSceneManager.OpenScene(ArtLibraryPaths.Ap005WorldScene, OpenSceneMode.Single);
            Transform root = null;
            foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == Ap005WorldBootstrap.RootName) root = go.transform;
            Assert.IsNotNull(root);

            Light sun = WorldAtmosphere.FindBakedSun();
            Assert.IsNotNull(sun, "baked güneş");
            Assert.AreEqual(LightmapBakeType.Mixed, sun.lightmapBakeType);
            Assert.AreEqual(WorldAtmosphere.SunIntensity, sun.intensity, 1e-4f);
            Assert.AreEqual(WorldAtmosphere.SunShadowStrength, sun.shadowStrength, 1e-4f);
            Assert.AreEqual(LightShadows.Soft, sun.shadows);
            Assert.Less(Quaternion.Angle(sun.transform.rotation, Quaternion.Euler(WorldAtmosphere.SunEuler)), 0.01f);

            // WP-038G (M3 closure K2): the realtime character fill — never on the world layers, never baked, no shadows.
            Light fill = null;
            foreach (Light l in root.GetComponentsInChildren<Light>(true)) if (l.name == ArtLightingBake.CharacterFillName) fill = l;
            Assert.IsNotNull(fill, "karakter dolgu ışığı (WP-038G)");
            Assert.AreEqual(LightType.Directional, fill.type);
            Assert.AreEqual(LightmapBakeType.Realtime, fill.lightmapBakeType);
            Assert.AreEqual(LightShadows.None, fill.shadows);
            Assert.AreEqual(ArtLightingBake.CharacterFillIntensity, fill.intensity, 1e-4f);
            // URP ignores Light.cullingMask: the reach is the character rendering layer (light layers on in the pipeline asset).
            var fillData = fill.GetComponent<UniversalAdditionalLightData>();
            Assert.IsNotNull(fillData, "dolgu ışığı UniversalAdditionalLightData");
            Assert.AreEqual(ArtLightingBake.CharacterLightLayer, fillData.renderingLayers, "dolgu ışığı yalnız karakter rendering layer'ına düşer");
            Assert.AreEqual(0u, fillData.renderingLayers & 1u, "dolgu ışığı varsayılan (dünya) rendering layer'ına düşmez");
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ArtLightingBake.UrpAssetPath);
            Assert.IsTrue(pipeline.useRenderingLayers, "pipeline asset: light layers açık (WP-038G)");
            RenderPipelineGlobalSettings globals = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            Assert.IsNotNull(globals, "URP global settings");
            var gso = new SerializedObject(globals);
            uint valid = (uint)gso.FindProperty("m_ValidRenderingLayers").intValue;
            Assert.AreEqual(ArtLightingBake.CharacterLightLayer, valid & ArtLightingBake.CharacterLightLayer, "karakter rendering layer'ı global ayarlarda adlı (URP ToValidRenderingLayers maskesi)");
            Assert.AreEqual(ArtLightingBake.CharacterLightLayerName, gso.FindProperty("m_RenderingLayerNames").GetArrayElementAtIndex(1).stringValue, "rendering layer 1 adı");
            var sunData = sun.GetComponent<UniversalAdditionalLightData>();
            Assert.IsTrue(sunData == null || (sunData.renderingLayers & 1u) != 0u, "güneş varsayılan rendering layer'ı aydınlatır");

            Transform probes = root.Find(ArtLightingBake.ProbeGroupName);
            Assert.IsNotNull(probes);
            var group = probes.GetComponent<LightProbeGroup>();
            Assert.IsNotNull(group);
            Assert.GreaterOrEqual(group.probePositions.Length, 1000, "probe sayısı");
            var refl = root.Find(ArtLightingBake.ReflectionProbesName);
            Assert.IsNotNull(refl);
            ReflectionProbe[] rps = refl.GetComponentsInChildren<ReflectionProbe>(true);
            Assert.AreEqual(6, rps.Length, "5 bölge + gölet");
            foreach (ReflectionProbe rp in rps) Assert.AreEqual(ReflectionProbeMode.Baked, rp.mode, rp.name);

            Assert.IsNotNull(Lightmapping.lightingSettings, "lighting settings");
            Assert.AreEqual(ArtLightingBake.LightingSettingsPath, AssetDatabase.GetAssetPath(Lightmapping.lightingSettings));
            Assert.AreEqual(MixedLightingMode.IndirectOnly, Lightmapping.lightingSettings.mixedBakeMode, "Baked Indirect: direkt ışık + gölge gerçek zamanlı kalır");
            Assert.IsTrue(Lightmapping.lightingSettings.ao);
            Assert.AreEqual(ArtLightingBake.LightmapResolution, Lightmapping.lightingSettings.lightmapResolution, 1e-3f);

            int contribute = 0, statics = 0, dressingLightmapped = 0, lod0Charts = 0, lod0Small = 0, lowerLodCharts = 0, lowerLodContribute = 0, policyBreaks = 0;
            Transform instances = root.Find(Ap005WorldBootstrap.InstancesName);
            for (int i = 0; i < instances.childCount; i++)
            {
                Transform t = instances.GetChild(i);
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                var tag = t.GetComponent<RegionTag>();
                if ((flags & StaticEditorFlags.OccludeeStatic) != 0) statics++;
                if ((flags & StaticEditorFlags.ContributeGI) == 0) continue;
                contribute++;
                if (tag != null && tag.Role == "dressing") dressingLightmapped++;

                // ADR-029-2: only LOD0 takes a chart; LOD1+ are probe-lit and do not contribute.
                var lodGroup = t.GetComponentInChildren<LODGroup>(true);
                if (lodGroup == null) continue;
                LOD[] lods = lodGroup.GetLODs();
                for (int l = 0; l < lods.Length; l++)
                {
                    foreach (Renderer r in lods[l].renderers)
                    {
                        var mr = r as MeshRenderer;
                        if (mr == null) continue;
                        bool contributes = (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0;
                        bool chart = contributes && new SerializedObject(mr).FindProperty("m_ReceiveGI").intValue == (int)ReceiveGI.Lightmaps;   // the serialized field; the property getter is not authoritative without ContributeGI
                        if (l == 0)
                        {
                            bool big = ArtLightingBake.ChartTexels(mr) >= ArtLightingBake.MinChartTexels;
                            if (chart) lod0Charts++; else lod0Small++;
                            if (chart != big || contributes != big) policyBreaks++;
                        }
                        else { if (chart) lowerLodCharts++; if (contributes) lowerLodContribute++; }
                    }
                }
            }

            Assert.Greater(contribute, 150, "yapı + zemin lightmap'te");
            Assert.AreEqual(0, dressingLightmapped, "dressing probe ile aydınlanır (ADR-029-2)");
            Assert.Greater(statics, contribute);
            Assert.Greater(lod0Charts, 100, "LOD0 renderer'ları lightmap alır");
            Assert.AreEqual(0, policyBreaks, "LOD0: chart ⇔ yüzey ≥ MinChartTexels (ADR-029-2)");
            Assert.AreEqual(0, lowerLodCharts, "LOD1+ probe ile aydınlanır, chart almaz (ADR-029-2)");
            Assert.AreEqual(0, lowerLodContribute, "LOD1+ bake'e katkı vermez (ADR-029-2)");
            Debug.Log("[DD-TEST] wp029.world sun=Mixed probes=" + group.probePositions.Length + " reflection=" + rps.Length + " contributeGI=" + contribute + " lod0Charts=" + lod0Charts + " lod0Small=" + lod0Small);
        }

        [Test]
        public void BakeRaporu_VarsaLightmapDosyalari_ButceIcinde()
        {
            if (!File.Exists(ArtLightingBake.BakeReportPath))
            {
                Debug.LogWarning("[DD-TEST] wp029.bake REPORTED: bake raporu yok (tools/bake-lighting.sh koşmadı)");
                return;
            }

            var report = JsonUtility.FromJson<ArtLightingBake.BakeReport>(File.ReadAllText(ArtLightingBake.BakeReportPath));
            Assert.IsTrue(report.ok, "bake OK");
            Assert.Greater(report.lightmapCount, 0, "lightmap sayısı");
            Assert.LessOrEqual(report.lightmapBytes, (long)ArtLightingBake.LightmapBudgetMb * 1048576L, "lightmap bütçesi ≤ 64 MB (ADR-029-2)");
            string folder = Path.Combine(Path.GetDirectoryName(ArtLibraryPaths.Ap005WorldScene), ArtLibraryPaths.Ap005WorldSceneName).Replace('\\', '/');
            int onDisk = 0;
            foreach (string f in report.files) if (File.Exists(Path.Combine(folder, f))) onDisk++;
            Assert.AreEqual(report.files.Length, onDisk, "rapordaki dosyalar diskte");
            Debug.Log("[DD-TEST] wp029.bake lightmaps=" + report.lightmapCount + " files=" + report.files.Length + " MB=" + (report.lightmapBytes / 1048576.0).ToString("F1") + " seconds=" + report.bakeSeconds.ToString("F0") + " lightmapper=" + report.lightmapper);
        }

        // ================================================================ contract v1.2

        [Test]
        public void SozlesmeV12_CevreModelleriLightmapUV_KarakterlerUVsiz()
        {
            int env = 0, chars = 0;
            var hatalar = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtImportContract.ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!ArtImportContract.IsContractModel(path)) continue;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                bool character = ArtCharacterContract.IsCharacterModel(path);
                bool expected = character ? ArtImportContract.CharacterGenerateSecondaryUV : ArtImportContract.WantsLightmapUv(path);
                if (importer.generateSecondaryUV != expected) hatalar.Add(path + ": generateSecondaryUV " + importer.generateSecondaryUV);
                if (!character && ArtImportContract.WantsLightmapUv(path))
                {
                    // ADR-029-2: chart margins are computed for the bake resolution, not a fixed texel count.
                    if (importer.secondaryUVMarginMethod != ArtImportContract.ModelSecondaryUVMarginMethod) hatalar.Add(path + ": secondaryUVMarginMethod " + importer.secondaryUVMarginMethod);
                    if (Math.Abs(importer.secondaryUVMinLightmapResolution - ArtImportContract.ModelSecondaryUVMinLightmapResolution) > 1e-3f) hatalar.Add(path + ": secondaryUVMinLightmapResolution " + importer.secondaryUVMinLightmapResolution);
                }

                if (character) chars++; else env++;
            }

            Assert.IsEmpty(hatalar, string.Join("\n", hatalar));
            // WP-038D: the character set grows with every animation / weapon package (85 at WP-032B, 134 at WP-032F/WP-059) — the
            // contract here is "every character FBX is UV2-free", counted by ArtCharacterTests, not pinned again.
            Assert.Greater(env, 150); Assert.GreaterOrEqual(chars, 85, "karakter FBX kümesi (WP-032B 85 → WP-032F/059 134)");   // env sayısı silah/VFX modellerini de içerir (UV2'siz)
            Assert.LessOrEqual(ArtImportContract.ModelSecondaryUVMinLightmapResolution, ArtLightingBake.LightmapResolution, "margin, bake çözünürlüğünün altındaki bir çözünürlüğü de kaldıracak şekilde hesaplanır (küçük değer = geniş margin)");
            var sample = AssetDatabase.LoadAssetAtPath<GameObject>(ArtLibraryPaths.Fbx("AP-002", "plaster_wall", "plaster_wall_LOD0.fbx"));
            Assert.IsNotNull(sample);
            Mesh mesh = sample.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Assert.IsTrue(mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount, "çevre mesh'i UV2 taşır");
            Debug.Log("[DD-TEST] wp029.contract env=" + env + " characters=" + chars);
        }
    }
}
