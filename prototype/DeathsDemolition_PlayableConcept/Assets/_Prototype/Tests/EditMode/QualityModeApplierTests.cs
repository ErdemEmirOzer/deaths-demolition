using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-070 · S-05 (C-00 06.10) · The cascade decision of <see cref="QualityModeApplier"/>: min(tier cap, camera-mode profile)
    /// inside URP's 1..4, and no write to a URP asset outside play mode (the assets are shared project files).
    /// </summary>
    public sealed class QualityModeApplierTests
    {
        private static readonly CameraMode[] Modes = { CameraMode.TopDown, CameraMode.ThirdPerson, CameraMode.FirstPerson };

        [Test]
        public void KaskadKarari_VarsayilanProfiller_KademeModTablosu()
        {
            // [tier, mode] with the WP-070 default profiles (TD 2 / TP 3 / FP 4) under the caps Performans 2, Yüksek 4, Sinematik 4.
            int[,] expected = { { 2, 2, 2 }, { 2, 3, 3 }, { 2, 3, 4 } };   // High FP 3 (C-00 06.10, cap 3)
            for (int t = 0; t < QualityTiers.LevelCount; t++)
            {
                for (int m = 0; m < Modes.Length; m++)
                {
                    CameraModeProfile profile = CameraModeProfile.Default(Modes[m]);
                    int cascades = QualityModeApplier.CascadesFor((QualityTier)t, profile);
                    Assert.AreEqual(expected[t, m], cascades, (QualityTier)t + " / " + Modes[m]);
                    Assert.AreEqual(QualityTiers.ShadowCascades((QualityTier)t, profile.ShadowCascades), cascades, "sözleşme formülü: " + (QualityTier)t + " / " + Modes[m]);
                }
            }
        }

        [Test]
        public void GolgeMesafesiKarari_VarsayilanProfiller_KademeModTablosu()
        {
            // [tier, mode] with the WP-070 default profiles (TD 60 / TP 60 / FP 70 m) under the caps Performans 45, Yüksek 60, Sinematik 120;
            // FINAL 10.10 perf: High top-down 40 m (QualityTiers.TopDownHighShadowDistance), every other cell as before.
            float[,] expected = { { 45f, 45f, 45f }, { 40f, 60f, 60f }, { 60f, 60f, 70f } };
            for (int t = 0; t < QualityTiers.LevelCount; t++)
            {
                for (int m = 0; m < Modes.Length; m++)
                {
                    CameraModeProfile profile = CameraModeProfile.Default(Modes[m]);
                    float distance = QualityModeApplier.ShadowDistanceFor((QualityTier)t, Modes[m], profile);
                    Assert.AreEqual(expected[t, m], distance, 1e-4f, (QualityTier)t + " / " + Modes[m]);
                    Assert.AreEqual(QualityTiers.ShadowDistance((QualityTier)t, Modes[m], profile.ShadowDistance), distance, 1e-4f, "sözleşme formülü");
                }
            }
        }

        [Test]
        public void KaskadKarari_AralikDisiProfil_UrpSinirindaKalir()
        {
            CameraModeProfile profile = CameraModeProfile.Default(CameraMode.FirstPerson);
            foreach (int bad in new[] { 0, -3 })
            {
                profile.ShadowCascades = bad;
                for (int t = 0; t < QualityTiers.LevelCount; t++) Assert.AreEqual(1, QualityModeApplier.CascadesFor((QualityTier)t, profile), "alt sınır " + bad);
            }

            profile.ShadowCascades = 9;
            for (int t = 0; t < QualityTiers.LevelCount; t++)
            {
                Assert.AreEqual(QualityTiers.ShadowCascadeCap[t], QualityModeApplier.CascadesFor((QualityTier)t, profile), "kademe tavanı " + (QualityTier)t);
                Assert.LessOrEqual(QualityModeApplier.CascadesFor((QualityTier)t, profile), 4, "URP üst sınırı");
            }
        }

        [Test]
        public void SinematikIkiKaskadBolmesi_Mesafe60m_C0SiniriMetreKorunur()
        {
            // AD MUST-4a (06.10): Cinematic TD renders 2 cascades at the profile's 60 m; the split keeps C0's boundary where
            // 0.25 x 120 m put it (30 m) — only Cinematic TD reads the two-cascade split (TP 3, FP / menu 4)
            Object asset = AssetDatabase.LoadMainAssetAtPath("Assets/Settings/DD_URP_Cinematic.asset");
            Assert.IsNotNull(asset, "DD_URP_Cinematic varlığı yok");
            float split = new SerializedObject(asset).FindProperty("m_Cascade2Split").floatValue;
            Assert.AreEqual(0.5f, split, 1e-4f, "Sinematik m_Cascade2Split");
            float distance = QualityModeApplier.ShadowDistanceFor(QualityTier.Sinematik, CameraModeProfile.Default(CameraMode.TopDown));
            Assert.AreEqual(0.25f * QualityTiers.ShadowDistanceCap[(int)QualityTier.Sinematik], split * distance, 1e-3f, "C0 sınırı metre olarak korunmadı");
        }

        [Test]
        public void EditModeda_ApplyNow_UrpVarliginaYazmaz()
        {
            RenderPipelineAssetProbe probe = RenderPipelineAssetProbe.Active();
            CameraMode before = CameraModeService.Current;
            int written = QualityModeApplier.RecordedAssetCount;
            try
            {
                // pick a mode whose decision differs from the asset, so a write would show
                QualityTier tier = QualityTiers.Current;
                CameraMode differing = before;
                bool found = false;
                foreach (CameraMode mode in Modes)
                {
                    if (QualityModeApplier.CascadesFor(tier, CameraModeService.Profile(mode)) == probe.Cascades) continue;
                    differing = mode;
                    found = true;
                    break;
                }

                if (!found) Assert.Ignore("etkin kademede (" + tier + ") her modun kararı varlık değerine eşit; yazım görünmez");
                CameraModeService.Request(differing, instant: true);
                QualityModeApplier.ApplyNow();
                Assert.AreEqual(probe.Cascades, RenderPipelineAssetProbe.Active().Cascades, "EditMode'da URP varlığı değişti (" + probe.Name + ")");
                Assert.AreEqual(written, QualityModeApplier.RecordedAssetCount, "EditMode'da kayıt tutuldu");
            }
            finally
            {
                CameraModeService.Request(before, instant: true);
            }
        }

        /// <summary>FINAL 10.10 perf: the High × TopDown lite switch (sky-only planar, small push map) is a play-mode state only and never
        /// outlives <see cref="QualityModeApplier.RestoreOriginals"/>.</summary>
        [Test]
        public void EditModeda_YuksekTepedenHafifYol_KapaliKalir_RestoreTemizler()
        {
            CameraMode before = CameraModeService.Current;
            try
            {
                CameraModeService.Request(CameraMode.TopDown, instant: true);
                QualityModeApplier.ApplyNow();
                Assert.IsFalse(QualityModeApplier.HighTopDownLite, "EditMode'da Yüksek × TD hafif yolu açıldı");
                QualityModeApplier.RestoreOriginals();
                Assert.IsFalse(QualityModeApplier.HighTopDownLite, "RestoreOriginals hafif yolu kapatmadı");
            }
            finally
            {
                CameraModeService.Request(before, instant: true);
            }
        }

        /// <summary>The active level's URP asset read through its serialized field (this assembly does not reference URP).</summary>
        private readonly struct RenderPipelineAssetProbe
        {
            public readonly string Name;
            public readonly int Cascades;

            private RenderPipelineAssetProbe(string name, int cascades)
            {
                Name = name;
                Cascades = cascades;
            }

            public static RenderPipelineAssetProbe Active()
            {
                Object asset = QualitySettings.renderPipeline != null ? (Object)QualitySettings.renderPipeline : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                Assert.IsNotNull(asset, "etkin render pipeline varlığı yok");
                SerializedProperty p = new SerializedObject(asset).FindProperty("m_ShadowCascadeCount");
                Assert.IsNotNull(p, asset.name + ": m_ShadowCascadeCount yok");
                return new RenderPipelineAssetProbe(asset.name, p.intValue);
            }
        }
    }
}
