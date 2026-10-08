using System;
using System.Collections.Generic;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Props;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-013 · Atmosfer/VFX kapıları: bitki materyalleri DD/Foliage Wind üzerinde ve doğru
    /// durumda, su v1 saydam, parçacık prefabları bütçe içinde, volume profili tam,
    /// BreakableProp altı prefabda bağlı ve durum makinesi doğru, builder idempotan.
    /// </summary>
    [TestFixture]
    [Category("WP013")]
    public sealed class ArtVfxGateTests
    {
        [Test]
        public void RuzgarShaderi_VarHatasizDortPass_SrpBatcherUyumlu()
        {
            Shader shader = Shader.Find(ArtMaterialBuilder.FoliageWindShaderName);
            Assert.IsNotNull(shader, "DD/Foliage Wind bulunamadı (Assets/_Art/Shaders).");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "DD/Foliage Wind derleme hatası: " + Mesajlar(shader));
            // Shader.passCount reads the ACTIVE subshader, which under -nographics is the fallback; read the authored subshader.
            int passes = ShaderUtil.GetShaderData(shader).GetSubshader(0).PassCount;
            Assert.AreEqual(4, passes, "ForwardLit + ShadowCaster + DepthOnly + DepthNormals bekleniyor.");
            foreach (string prop in new[] { "_BaseMap", "_BaseColor", "_BumpMap", "_MetallicGlossMap", "_Cutoff", "_Cull", "_AlphaToMask", "_WindScale", "_BackfaceLighting", "_Translucency" })
            {
                Assert.IsTrue(shader.FindPropertyIndex(prop) >= 0, "DD/Foliage Wind özelliği eksik: " + prop);
            }

            Shader water = Shader.Find(ArtMaterialBuilder.WaterShaderName);
            Assert.IsNotNull(water, "DD/Water v1 bulunamadı.");
            Assert.IsFalse(ShaderUtil.ShaderHasError(water), "DD/Water v1 derleme hatası: " + Mesajlar(water));
            Debug.Log("[DD-TEST] wp013.gate.shaders foliagePasses=" + passes + " waterPasses=" + ShaderUtil.GetShaderData(water).GetSubshader(0).PassCount);
        }

        [Test]
        public void BitkiMateryalleri_FoliageWindUzerinde_KesimVeCiftYuzDogru()
        {
            string[] roller = ArtLibraryMaterialRoles.RolesOn(ArtLibraryMaterialRoles.ShaderKind.FoliageWind);
            Assert.AreEqual(24, roller.Length, "3 green cutout + 3 green solid + 3 sakura + bamboo_culm + AP-017 furin (WP-038L) = 11; + AP-023 v005 (WP-074 B5a): 9 atlases (bamboo_leaves, groundcover, hishigaki_card, momiji_sprigs, pine_needles, reeds_iris, sakura_sprigs, satsuki, violet_sprigs) + ap023_bamboo_culm = 21 rüzgâr rolü; v007: + lotus = 22; v008: + pine_needles_pad (single-sided) = 23; WP-092 C AP-030 v002: + ap030_props_cloth (çamaşır / noren kumaşı) = 24.");
            var ihlaller = new List<string>();
            foreach (string rol in roller)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(rol));
                if (m == null) { ihlaller.Add(rol + ": materyal yok"); continue; }
                if (m.shader == null || m.shader.name != ArtMaterialBuilder.FoliageWindShaderName) { ihlaller.Add(rol + ": shader " + (m.shader != null ? m.shader.name : "null")); continue; }
                ArtLibraryMaterialRoles.Spec spec = ArtLibraryMaterialRoles.Get(rol);
                bool cutout = spec.Kind == ArtLibraryMaterialRoles.Kind.Cutout;
                if (m.IsKeywordEnabled("_ALPHATEST_ON") != cutout) ihlaller.Add(rol + ": _ALPHATEST_ON " + m.IsKeywordEnabled("_ALPHATEST_ON") + " (beklenen " + cutout + ")");
                if (Mathf.Abs(m.GetFloat("_Cull") - (cutout && !spec.SingleSided ? 0f : 2f)) > 0.01f) ihlaller.Add(rol + ": _Cull " + m.GetFloat("_Cull"));
                if (Mathf.Abs(m.GetFloat("_AlphaToMask") - (cutout ? 1f : 0f)) > 0.01f) ihlaller.Add(rol + ": _AlphaToMask " + m.GetFloat("_AlphaToMask"));
                if (Mathf.Abs(m.GetFloat("_WindScale") - 1f) > 0.01f) ihlaller.Add(rol + ": _WindScale " + m.GetFloat("_WindScale"));
                if (Mathf.Abs(m.GetFloat("_BackfaceLighting") - 1f) > 0.01f) ihlaller.Add(rol + ": _BackfaceLighting " + m.GetFloat("_BackfaceLighting"));
                if (m.GetTexture("_BaseMap") == null) ihlaller.Add(rol + ": _BaseMap yok");
                if (spec.HasNormalMap != m.IsKeywordEnabled("_NORMALMAP")) ihlaller.Add(rol + ": _NORMALMAP " + m.IsKeywordEnabled("_NORMALMAP"));
                if (m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) ihlaller.Add(rol + ": saydam olmamalı");
                if (!m.enableInstancing) ihlaller.Add(rol + ": instancing kapalı");
                if (cutout && m.renderQueue != (int)RenderQueue.AlphaTest) ihlaller.Add(rol + ": renderQueue " + m.renderQueue);
            }

            // Lit kalanlar gerçekten Lit (yosun, kabuk, taş): rüzgâr yalnız bitkide.
            foreach (string rol in new[] { "moss_01", "bark_01", "stone_weathered", "gravel", "plaster_hf003" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(rol));
                if (m == null || m.shader == null || m.shader.name != ArtMaterialBuilder.LitShaderName) ihlaller.Add(rol + ": URP/Lit olmalı");
            }

            Assert.IsEmpty(ihlaller, "Bitki materyal ihlali (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp013.gate.foliageMaterials roles=" + roller.Length);
        }

        [Test]
        public void SuMateryali_WaterV1_SaydamDerinlikYazmaz()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("pond_water"));
            Assert.IsNotNull(m, "M_pond_water yok.");
            Assert.AreEqual(ArtMaterialBuilder.WaterShaderName, m.shader.name);
            Assert.AreEqual((int)RenderQueue.Transparent, m.renderQueue);
            Assert.AreEqual(0f, m.GetFloat("_ZWrite"), 0.01f, "Su derinlik yazmamalı (arkasındaki cutout kartlar/kıyı görünür kalmalı).");
            Assert.AreEqual((float)BlendMode.SrcAlpha, m.GetFloat("_SrcBlend"), 0.01f);
            Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, m.GetFloat("_DstBlend"), 0.01f);
            Assert.AreEqual("Transparent", m.GetTag("RenderType", false));
            Color deep = m.GetColor("_BaseColor").linear;
            Assert.AreEqual(0.03695f, deep.r, 0.01f, "Derin renk AP-003B pond_water albedosu olmalı.");
            Assert.Greater(m.GetFloat("_DepthFade"), 0f);
        }

        [Test]
        public void ParcacikPrefablari_ButceIcinde_GpuInstancedRuzgarli()
        {
            var ihlaller = new List<string>();
            KontrolDrift(ArtVfxBuilder.PollenPrefab, ArtVfxBuilder.PollenMaxParticles, ArtVfxBuilder.PollenEmission, 0.06f, ihlaller);
            KontrolDrift(ArtVfxBuilder.PetalsPrefab, ArtVfxBuilder.PetalsMaxParticles, ArtVfxBuilder.PetalsEmission, ArtVfxBuilder.PetalSizeMax, ihlaller);

            // Run bölge tablosu: toplam tavan (yoğunluk ağırlıklı) 480'i geçmez (REQ-104 sayı+boyut argümanı).
            int toplam = 0;
            foreach (WorldAtmosphere.DriftRegion r in WorldAtmosphere.DefaultRunRegions())
            {
                int tavan = r.Kind == WorldAtmosphere.DriftKind.Pollen ? ArtVfxBuilder.PollenMaxParticles : ArtVfxBuilder.PetalsMaxParticles;
                toplam += Mathf.RoundToInt(tavan * Mathf.Max(r.Density, 0.05f));
            }

            if (toplam > 480) ihlaller.Add("Run bölge tablosu toplam tavanı " + toplam + " > 480");

            var burst = AssetDatabase.LoadAssetAtPath<GameObject>(ArtVfxBuilder.BreakBurstPrefab);
            if (burst == null) ihlaller.Add("DD_BreakBurst prefabı yok");
            else
            {
                int patlama = 0;
                foreach (ParticleSystem ps in burst.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.main.loop) ihlaller.Add("Burst döngülü olmamalı: " + ps.name);
                    if (ps.main.playOnAwake) ihlaller.Add("Burst playOnAwake kapalı olmalı: " + ps.name);
                    if (ps.main.stopAction != ParticleSystemStopAction.Destroy) ihlaller.Add("Burst stopAction Destroy olmalı: " + ps.name);
                    patlama += ps.main.maxParticles;
                }

                if (patlama > ArtVfxBuilder.BurstChips + ArtVfxBuilder.BurstDust) ihlaller.Add("Burst parçacık toplamı " + patlama);
            }

            Assert.IsEmpty(ihlaller, "Parçacık bütçe ihlali (" + ihlaller.Count + "):\n" + string.Join("\n", ihlaller));
            Debug.Log("[DD-TEST] wp013.gate.particles runCeiling=" + toplam);
        }

        private static void KontrolDrift(string yol, int tavan, float emisyon, float boyutTavan, List<string> ihlaller)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(yol);
            if (go == null) { ihlaller.Add(yol + ": prefab yok"); return; }
            var drift = go.GetComponent<AtmosphereDrift>();
            var ps = go.GetComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (drift == null || ps == null || r == null) { ihlaller.Add(yol + ": AtmosphereDrift/ParticleSystem/Renderer eksik"); return; }
            if (drift.MaxParticles > tavan) ihlaller.Add(yol + ": AtmosphereDrift tavanı " + drift.MaxParticles + " > " + tavan);
            if (ps.main.maxParticles > tavan) ihlaller.Add(yol + ": maxParticles " + ps.main.maxParticles + " > " + tavan);
            if (drift.EmissionAtDensityOne > emisyon + 0.01f) ihlaller.Add(yol + ": emisyon " + drift.EmissionAtDensityOne);
            if (ps.main.startSize.constantMax > boyutTavan + 0.001f) ihlaller.Add(yol + ": startSize max " + ps.main.startSize.constantMax + " > " + boyutTavan);
            if (r.maxParticleSize > 0.02f + 1e-4f) ihlaller.Add(yol + ": maxParticleSize " + r.maxParticleSize);
            if (r.renderMode != ParticleSystemRenderMode.Mesh || !r.enableGPUInstancing || r.mesh == null) ihlaller.Add(yol + ": Mesh + GPU instancing bekleniyor");
            if (!ps.externalForces.enabled) ihlaller.Add(yol + ": External Forces (rüzgâr) kapalı");
            if (r.shadowCastingMode != ShadowCastingMode.Off) ihlaller.Add(yol + ": gölge kapalı olmalı");
            if (r.sharedMaterial == null || r.sharedMaterial.shader == null || r.sharedMaterial.shader.name != ArtVfxBuilder.ParticlesUnlitShaderName) ihlaller.Add(yol + ": materyal Particles/Unlit değil");
        }

        [Test]
        public void VolumeProfili_PostFxAlanlariTam_KameradaAcik()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtLibraryPaths.OvercastVolumeProfile);
            Assert.IsNotNull(profile, "DD_Overcast_v1 yok.");
            Assert.IsTrue(profile.TryGet(out Tonemapping tone) && tone.mode.overrideState && (int)tone.mode.value == WorldAtmosphere.PostFx.TonemappingMode, "Tonemapping");
            Assert.IsTrue(profile.TryGet(out ColorAdjustments color) && color.contrast.overrideState && Mathf.Abs(color.contrast.value - WorldAtmosphere.PostFx.Contrast) < 0.01f
                          && Mathf.Abs(color.saturation.value - WorldAtmosphere.PostFx.Saturation) < 0.01f, "ColorAdjustments");
            Assert.IsTrue(profile.TryGet(out WhiteBalance white) && Mathf.Abs(white.temperature.value - WorldAtmosphere.PostFx.WhiteBalanceTemperature) < 0.01f, "WhiteBalance");
            Assert.IsTrue(profile.TryGet(out Vignette vignette) && Mathf.Abs(vignette.intensity.value - WorldAtmosphere.PostFx.VignetteIntensity) < 0.01f, "Vignette");
            Assert.IsTrue(profile.TryGet(out Bloom bloom) && Mathf.Abs(bloom.threshold.value - WorldAtmosphere.PostFx.BloomThreshold) < 0.01f, "Bloom eşiği (REQ-105: yalnız > 1 emissive)");
            // WP-038J: the bloom chain is four mip iterations with bilinear upsampling (setPass 16 → 10; same-frame ΔE p99 1.4).
            Assert.AreEqual(WorldAtmosphere.PostFx.BloomMaxIterations, bloom.maxIterations.value, "Bloom maxIterations (WP-038J)");
            Assert.AreEqual(WorldAtmosphere.PostFx.BloomHighQualityFiltering, bloom.highQualityFiltering.value, "Bloom HQ filtering (WP-038J)");
            Assert.IsTrue(WorldAtmosphere.PostFx.Enabled, "WP-013: post-fx kamerada açık olmalı.");

            // Kamera fabrikası bayrağı gerçekten yazıyor.
            var go = new GameObject("wp013-cam-test");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                WorldAtmosphere.ConfigureCamera(cam);
                Assert.IsTrue(cam.GetUniversalAdditionalCameraData().renderPostProcessing);
                Assert.AreEqual(AntialiasingMode.None, cam.GetUniversalAdditionalCameraData().antialiasing, "AA = MSAA (URP asset), post AA yok.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            // Renderer: PostProcessData atanmadan URP post-fx hiç çalışmaz (WP-011/012 "fiilen kapalı" kök nedeni).
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(ArtVfxBuilder.RendererAssetPath);
            Assert.IsNotNull(renderer);
            var ppd = new SerializedObject(renderer).FindProperty("postProcessData").objectReferenceValue;
            Assert.IsNotNull(ppd, "DD_URP_Renderer.postProcessData boş: post-fx kamerada açık olsa da render edilmez.");

            // Sis: REQ-104 — lineer, gameplay menzilinin (≈45 m) dışından başlar.
            Assert.GreaterOrEqual(WorldAtmosphere.FogStart, 45f, "Sis başlangıcı savaş menzilinin içine girmemeli (REQ-104).");
            Assert.Greater(WorldAtmosphere.FogEnd, WorldAtmosphere.FogStart);
        }

        [Test]
        public void RuzgarSozlesmesi_GlobalYazilir_SakinSifir()
        {
            WorldWind.ApplyCalm();
            Vector4 v = Shader.GetGlobalVector(WorldWind.WindVectorId);
            Assert.AreEqual(0f, v.z, 1e-6f, "Sakin rüzgâr strength 0 yazmalı.");
            WorldWind.Apply(new Vector3(0f, 5f, 1f), 3f, 12.5f);
            v = Shader.GetGlobalVector(WorldWind.WindVectorId);
            Assert.AreEqual(0f, v.x, 1e-5f); Assert.AreEqual(1f, v.y, 1e-5f, "Yön xz düzleminde normalize.");
            Assert.AreEqual(1f, v.z, 1e-6f, "Strength 0..1'e kırpılır.");
            Assert.AreEqual(12.5f, v.w, 1e-6f);
            Vector4 p = Shader.GetGlobalVector(WorldWind.WindParamsId);
            Assert.AreEqual(WorldAtmosphere.Wind.SwayAmplitude, p.x, 1e-6f);
            Assert.AreEqual(WorldAtmosphere.Wind.FlutterFrequency, p.w, 1e-6f);
            Assert.Less(WorldAtmosphere.Wind.SwayAmplitude, 0.5f, "Tepe sapması 0.5 m altında (siluet korunur).");
            WorldWind.ApplyCalm();
        }

        [Test]
        public void KirilabilirPrefablar_AltiVaryasyonBagli_DurumMakinesi()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab SM_small_breakable", new[] { ArtPipelinePaths.ArtRoot + "/Environment" });
            Assert.AreEqual(6, guids.Length, "AP-003B small_breakable 6 varyasyon.");
            foreach (string guid in guids)
            {
                string yol = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(yol);
                var prop = prefab.GetComponent<BreakableProp>();
                Assert.IsNotNull(prop, yol + ": BreakableProp yok.");
                Assert.IsNotNull(prop.Geometry, yol + ": Geometry referansı yok.");
                Assert.IsNotNull(prop.Broken, yol + ": Broken referansı yok.");
                Assert.IsFalse(prop.Broken.gameObject.activeSelf, yol + ": Broken prefabda kapalı olmalı.");
                Assert.IsTrue(prop.Geometry.gameObject.activeSelf, yol + ": Geometry prefabda açık olmalı.");
                Assert.Greater(prop.Broken.GetComponentsInChildren<Collider>(true).Length, 0, yol + ": Broken proxy collider'ı yok.");
            }

            // Durum makinesi (edit-mode kopyası; Awake koşmaz, ResolveChildren elle).
            var ornek = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0])));
            try
            {
                var prop = ornek.GetComponent<BreakableProp>();
                prop.ResolveChildren();
                prop.ResetToIntact();
                Assert.IsFalse(prop.IsBroken);
                int once = BreakableProp.BreakCount;
                Collider proxy = prop.Geometry.GetComponentInChildren<Collider>(true);
                Assert.IsNotNull(proxy);
                Assert.IsTrue(PropImpacts.TryGet(proxy, out BreakableProp bulunan) && bulunan == prop, "Sağlam proxy kayıtlı olmalı.");
                Assert.IsTrue(PropImpacts.Report(proxy, ornek.transform.position, Vector3.forward), "İlk isabet kırmalı.");
                Assert.IsTrue(prop.IsBroken);
                Assert.IsFalse(prop.Geometry.gameObject.activeSelf);
                Assert.IsTrue(prop.Broken.gameObject.activeSelf);
                Assert.AreEqual(once + 1, BreakableProp.BreakCount);
                Assert.IsFalse(prop.Hit(Vector3.zero, Vector3.forward), "İkinci isabet sessiz.");
                Assert.AreEqual(once + 1, BreakableProp.BreakCount);
                prop.ResetToIntact();
                Assert.IsFalse(prop.IsBroken);
                Assert.IsTrue(prop.Geometry.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ornek);
            }

            Assert.IsFalse(PropImpacts.TryGet(null, out _));
            Debug.Log("[DD-TEST] wp013.gate.breakables prefabs=6 stateMachine=ok");
        }

        [Test]
        public void MateryalUreticisi_Idempotan_IkinciKosuDiffSifir()
        {
            var once = new Dictionary<string, string>();
            foreach (ArtLibraryMaterialRoles.Spec spec in ArtLibraryMaterialRoles.All)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(spec.Role));
                Assert.IsNotNull(m, spec.Role);
                once[spec.Role] = EditorJsonUtility.ToJson(m);
            }

            ArtMaterialBuilder.BuildAllLibraryRoles();

            var farkli = new List<string>();
            foreach (ArtLibraryMaterialRoles.Spec spec in ArtLibraryMaterialRoles.All)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material(spec.Role));
                if (EditorJsonUtility.ToJson(m) != once[spec.Role]) farkli.Add(spec.Role);
            }

            Assert.IsEmpty(farkli, "İkinci koşuda değişen materyal: " + string.Join(", ", farkli));
        }

        private static string Mesajlar(Shader shader)
        {
            ShaderMessage[] mesajlar = ShaderUtil.GetShaderMessages(shader);
            var satirlar = new List<string>();
            foreach (ShaderMessage m in mesajlar) satirlar.Add(m.severity + " " + m.file + ":" + m.line + " " + m.message);
            return string.Join("\n", satirlar);
        }
    }
}
