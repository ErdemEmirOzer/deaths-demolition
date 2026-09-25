using DD.Prototype.Core;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-075 C1 · Quality tiers v2 (planning/work-packages/QUALITY-TIERS-V2.md): the tier API (level → tier, the six-level
    /// compatibility map, Pick, the tier × camera-mode formulas of §3) and the project layout — three levels Performance /
    /// High / Cinematic, default High, High = the former Ultra level on the unchanged DD_URP_PipelineAsset, Performance and
    /// Cinematic on their own URP assets with the §4.1 values.
    /// </summary>
    public sealed class QualityTiersTests
    {
        private static SerializedObject Asset(string path)
        {
            Object o = AssetDatabase.LoadAssetAtPath<Object>(path);
            Assert.IsNotNull(o, path);
            return new SerializedObject(o);
        }

        [Test]
        public void Kademe_SeviyeEslemesi_UcSeviyeDogrudan_AltiSeviyeUcteBir()
        {
            for (int i = 0; i < 3; i++) Assert.AreEqual((QualityTier)i, QualityTiers.TierOfLevel(i, 3));
            QualityTier[] six = { QualityTier.Performans, QualityTier.Performans, QualityTier.Yuksek, QualityTier.Yuksek, QualityTier.Sinematik, QualityTier.Sinematik };
            for (int i = 0; i < 6; i++) Assert.AreEqual(six[i], QualityTiers.TierOfLevel(i, 6), "altı seviye " + i);
            Assert.AreEqual(QualityTier.Sinematik, QualityTiers.TierOfLevel(9, 3), "taşan indeks kıskaçlanır");
            Assert.AreEqual(QualityTiers.DefaultTier, QualityTiers.TierOfLevel(0, 0));
            Assert.AreEqual(QualityTier.Sinematik, QualityTiers.DefaultTier, "DEC-100: varsayılan Sinematik");
            Assert.AreEqual("y", QualityTiers.Pick(QualityTier.Yuksek, "p", "y", "s"));
            Assert.AreEqual("p", QualityTiers.Pick(QualityTier.Performans, "p", "y", "s"));
            Assert.AreEqual("s", QualityTiers.Pick(QualityTier.Sinematik, "p", "y", "s"));
        }

        /// <summary>WP-078 (S-10 perf matrix): -ddQualityTier forces the tier for this process only; the saved UiSettings choice stays and cannot pull the level back.</summary>
        [Test]
        public void KomutSatiriKademe_SurecYerel_KayitliAyarSeviyeyiGeriCekmez()
        {
            foreach ((string v, QualityTier t) in new[] { ("performans", QualityTier.Performans), ("Performance", QualityTier.Performans), ("0", QualityTier.Performans),
                         ("yuksek", QualityTier.Yuksek), ("YÜKSEK", QualityTier.Yuksek), ("high", QualityTier.Yuksek), ("sinematik", QualityTier.Sinematik), ("cinematic", QualityTier.Sinematik), ("2", QualityTier.Sinematik) })
            {
                Assert.IsTrue(QualityTiers.TryParseTier(v, out QualityTier parsed), v);
                Assert.AreEqual(t, parsed, v);
            }

            Assert.IsFalse(QualityTiers.TryParseTier("ultra", out _));
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual((QualityTier)i, QualityTiers.TierOfLevel(QualityTiers.LevelOfTier((QualityTier)i, 3), 3));
                Assert.AreEqual((QualityTier)i, QualityTiers.TierOfLevel(QualityTiers.LevelOfTier((QualityTier)i, 6), 6), "altı seviye");
            }

            int levelBefore = QualitySettings.GetQualityLevel(), savedBefore = UiSettings.Quality;
            try
            {
                LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("command-line tier Sinematik"));
                Assert.IsTrue(QualityTiers.ApplyOverride("sinematik"));
                Assert.AreEqual(2, QualityTiers.LevelOverride);
                Assert.AreEqual(QualityTier.Sinematik, QualityTiers.Current);
                UiSettings.Quality = savedBefore == 0 ? 1 : 0;   // the settings screen / a saved file apply their own level…
                Assert.AreEqual(QualityTier.Sinematik, QualityTiers.Current, "kayıtlı ayar komut satırı kademesini geri çekmemeli");
                Assert.AreNotEqual(2, UiSettings.Quality, "oyuncunun seçimi kayıtta kalır");
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("is not performans"));
                Assert.IsFalse(QualityTiers.ApplyOverride("ultra"));
                Assert.AreEqual(2, QualityTiers.LevelOverride, "geçersiz değer mevcut zorlamayı bozmaz");
            }
            finally
            {
                QualityTiers.ClearOverride();
                UiSettings.Quality = savedBefore;
                QualitySettings.SetQualityLevel(levelBefore, false);
            }
        }

        [Test]
        public void Kademe_ModFormulu_LodBiasGolgeKaskat_SozlesmeTablosu()
        {
            // §3: Yüksek → Tepeden 2,0 / TP 1,2 / FP 1,4; Performans her modda 1,0; Sinematik (r2, DEC-100) TP 1,8 / FP 2,1.
            Assert.AreEqual(2f, QualityTiers.LodBias(QualityTier.Yuksek, 1f), 1e-4f);
            Assert.AreEqual(1.2f, QualityTiers.LodBias(QualityTier.Yuksek, 0.6f), 1e-4f);
            Assert.AreEqual(1.4f, QualityTiers.LodBias(QualityTier.Yuksek, 0.7f), 1e-4f);
            Assert.AreEqual(1f, QualityTiers.LodBias(QualityTier.Performans, 0.6f), 1e-4f);
            Assert.AreEqual(2.1f, QualityTiers.LodBias(QualityTier.Sinematik, 0.7f), 1e-4f);
            Assert.AreEqual(1.8f, QualityTiers.LodBias(QualityTier.Sinematik, 0.6f), 1e-4f);
            Assert.AreEqual(120f, QualityTiers.ShadowDistance(QualityTier.Sinematik, 200f), 1e-4f);
            // gölge = min(kademe tavanı, mod): Tepeden 60/2, TP 60/3, FP 70/4.
            Assert.AreEqual(60f, QualityTiers.ShadowDistance(QualityTier.Yuksek, 70f), 1e-4f);
            Assert.AreEqual(45f, QualityTiers.ShadowDistance(QualityTier.Performans, 60f), 1e-4f);
            Assert.AreEqual(70f, QualityTiers.ShadowDistance(QualityTier.Sinematik, 70f), 1e-4f);
            Assert.AreEqual(2, QualityTiers.ShadowCascades(QualityTier.Yuksek, 2));
            Assert.AreEqual(3, QualityTiers.ShadowCascades(QualityTier.Yuksek, 3));
            Assert.AreEqual(2, QualityTiers.ShadowCascades(QualityTier.Performans, 4));
        }

        [Test]
        public void Proje_UcSeviye_VarsayilanCinematic_HerSeviyeKendiUrpVarligi()
        {
            CollectionAssert.AreEqual(QualityTiers.LevelNames, QualitySettings.names, "seviye adları");
            SerializedObject qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            Assert.AreEqual((int)QualityTier.Sinematik, qs.FindProperty("m_CurrentQuality").intValue, "varsayılan seviye Cinematic (DEC-100)");
            SerializedProperty defaults = qs.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
                Assert.AreEqual((int)QualityTier.Sinematik, defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue, "platform varsayılanı Cinematic");
            SerializedProperty levels = qs.FindProperty("m_QualitySettings");
            string[] pipelines = { "Assets/Settings/DD_URP_Performance.asset", "Assets/Settings/DD_URP_PipelineAsset.asset", "Assets/Settings/DD_URP_Cinematic.asset" };
            for (int t = 0; t < 3; t++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(t);
                Assert.AreEqual(QualityTiers.LodBiasBase[t], level.FindPropertyRelative("lodBias").floatValue, 1e-4f, QualityTiers.LevelNames[t] + " lodBias");
                Assert.AreEqual(QualityTiers.StreamingBudgetMb[t], level.FindPropertyRelative("streamingMipmapsMemoryBudget").floatValue, 1e-3f, QualityTiers.LevelNames[t] + " akış bütçesi");
                Assert.AreEqual(pipelines[t], AssetDatabase.GetAssetPath(level.FindPropertyRelative("customRenderPipeline").objectReferenceValue), QualityTiers.LevelNames[t] + " URP varlığı");
                Assert.AreEqual(new[] { 0, 2, 4 }[t], level.FindPropertyRelative("antiAliasing").intValue, QualityTiers.LevelNames[t] + " seviye MSAA = URP varlığı (Unity etkin seviyede eşitler)");
            }

            SerializedObject high = Asset(pipelines[1]), performance = Asset(pipelines[0]), cinematic = Asset(pipelines[2]);
            // High = bugünkü görünüm (WP-038J/WP-064 değerleri).
            Assert.AreEqual(2, high.FindProperty("m_MSAA").intValue);
            Assert.AreEqual(1f, high.FindProperty("m_RenderScale").floatValue, 1e-4f);
            Assert.AreEqual(60f, high.FindProperty("m_ShadowDistance").floatValue, 1e-3f);
            Assert.AreEqual(2, high.FindProperty("m_ShadowCascadeCount").intValue);
            // Performance §4.1.
            Assert.AreEqual(1, performance.FindProperty("m_MSAA").intValue, "MSAA kapalı");
            Assert.AreEqual(0.85f, performance.FindProperty("m_RenderScale").floatValue, 1e-4f);
            Assert.AreEqual(45f, performance.FindProperty("m_ShadowDistance").floatValue, 1e-3f);
            Assert.AreEqual(2, performance.FindProperty("m_ShadowCascadeCount").intValue);
            Assert.IsFalse(performance.FindProperty("m_SoftShadowsSupported").boolValue);
            Object perfRenderer = performance.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
            Assert.AreEqual("Assets/Settings/DD_URP_Renderer_Performance.asset", AssetDatabase.GetAssetPath(perfRenderer));
            bool ssaoOff = false;
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/DD_URP_Renderer_Performance.asset"))
                if (sub != null && sub.name == "ScreenSpaceAmbientOcclusion") ssaoOff = !new SerializedObject(sub).FindProperty("m_Active").boolValue;
            Assert.IsTrue(ssaoOff, "Performance renderer SSAO kapalı");
            // Cinematic §4.1 (r2, DEC-100): 4096 soft (high) shadows 120 m / 4 cascades, its own renderer with SSAO 2.0 / 0.40 m.
            Assert.AreEqual(4, cinematic.FindProperty("m_MSAA").intValue, "MSAA 4×");
            Assert.AreEqual(4096, cinematic.FindProperty("m_MainLightShadowmapResolution").intValue);
            Assert.AreEqual(120f, cinematic.FindProperty("m_ShadowDistance").floatValue, 1e-3f);
            Assert.AreEqual(4, cinematic.FindProperty("m_ShadowCascadeCount").intValue);
            Assert.IsTrue(cinematic.FindProperty("m_SoftShadowsSupported").boolValue);
            SerializedProperty soft = cinematic.FindProperty("m_SoftShadowQuality");
            Assert.AreEqual("High", soft.enumNames[soft.enumValueIndex]);
            Assert.AreEqual("Assets/Settings/DD_URP_Renderer_Cinematic.asset", AssetDatabase.GetAssetPath(cinematic.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue));
            // C-00 25.09: every tier carries the feature-free planar renderer at index 1 (the planar reflection's nested render; the
            // tier renderer's SSAO threw in release players).
            foreach (SerializedObject tierAsset in new[] { performance, high, cinematic })
            {
                SerializedProperty list = tierAsset.FindProperty("m_RendererDataList");
                Assert.AreEqual(2, list.arraySize, tierAsset.targetObject.name + " renderer listesi");
                Object planarRenderer = list.GetArrayElementAtIndex(1).objectReferenceValue;
                Assert.AreEqual("Assets/Settings/DD_URP_Renderer_Planar.asset", AssetDatabase.GetAssetPath(planarRenderer), tierAsset.targetObject.name);
                Assert.AreEqual(0, new SerializedObject(planarRenderer).FindProperty("m_RendererFeatures").arraySize, "planar renderer özelliksiz");
            }
            bool ssaoCinematic = false;
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/DD_URP_Renderer_Cinematic.asset"))
            {
                if (sub == null || sub.name != "ScreenSpaceAmbientOcclusion") continue;
                var ssao = new SerializedObject(sub);
                Assert.IsTrue(ssao.FindProperty("m_Active").boolValue, "Cinematic SSAO açık");
                Assert.AreEqual(2.0f, ssao.FindProperty("m_Settings.Intensity").floatValue, 1e-4f);
                Assert.AreEqual(0.40f, ssao.FindProperty("m_Settings.Radius").floatValue, 1e-4f);
                SerializedProperty blur = ssao.FindProperty("m_Settings.BlurQuality");
                Assert.AreEqual("High", blur.enumNames[blur.enumValueIndex]);
                ssaoCinematic = true;
            }

            Assert.IsTrue(ssaoCinematic, "Cinematic renderer SSAO");
        }
    }
}
