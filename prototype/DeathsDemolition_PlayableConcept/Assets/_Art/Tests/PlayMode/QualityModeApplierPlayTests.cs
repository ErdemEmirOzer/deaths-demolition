#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension (the calls stay fully qualified: NUnit has its own Is)

namespace DD.Art.Tests.PlayMode
{
    /// <summary>
    /// WP-070 · S-05 (C-00 06.10, bug fix) · <see cref="QualityModeApplier"/> in play: after a static-bus clear and a scene load,
    /// every tier × camera mode renders the profile's cascade count (min(tier cap, mode)) through the events alone — the level
    /// change (QualityTiers.Changed), the mode request (CameraModeService.Changed) and a profile edit (ProfilesChanged, 0 B) —
    /// and <see cref="QualityModeApplier.RestoreOriginals"/> leaves every level's URP asset at the value in its file.
    /// </summary>
    [TestFixture]
    [Category("WP070")]
    public sealed class QualityModeApplierPlayTests
    {
        private const float SahneZamanAsimi = 60f;
        private static readonly CameraMode[] Modes = { CameraMode.TopDown, CameraMode.ThirdPerson, CameraMode.FirstPerson };
        private static readonly Regex CascadeLine = new Regex(@"^\s*m_ShadowCascadeCount:\s*(\d+)\s*$", RegexOptions.Multiline);

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Kaskad_KademeVeModOlaylari_ProfilKaskadiUygulanir_GeriYuklemeDosyaDegeri()
        {
            LogAssert.ignoreFailingMessages = true;
            int count = QualitySettings.count;
            Assert.AreEqual(QualityTiers.LevelCount, count, "kalite seviyesi sayısı");
            int levelBefore = QualitySettings.GetQualityLevel();
            CameraMode modeBefore = CameraModeService.Current;
            bool enabledBefore = QualityModeApplier.Enabled;
            var assets = new UniversalRenderPipelineAsset[count];
            var disk = new int[count];
            for (int i = 0; i < count; i++)
            {
                assets[i] = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
                Assert.IsNotNull(assets[i], "seviye " + i + " URP varlığı");
                disk[i] = FileCascades(assets[i]);
            }

            // [tier, mode] with the WP-070 default profiles: Performans 2/2/2, Yüksek 2/3/4, Sinematik 2/3/4 (before the fix: the asset's 2 / 2 / 4)
            int[,] expected = { { 2, 2, 2 }, { 2, 3, 4 }, { 2, 3, 4 } };
            try
            {
                QualityModeApplier.Enabled = true;
                // ResetForTests clears the service's events (as every camera fixture does): the next scene load must re-arm the applier
                CameraModeService.ResetForTests();
                SceneManager.LoadScene(PrototypeScenes.MainMenu, LoadSceneMode.Single);
                yield return null;
                float t0 = Time.realtimeSinceStartup;
                while (!(SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu && SceneManager.GetActiveScene().isLoaded) && Time.realtimeSinceStartup - t0 < SahneZamanAsimi) yield return null;
                Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name, "MainMenu yüklenmedi");
                yield return null;

                for (int t = 0; t < count; t++)
                {
                    QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier((QualityTier)t, count), true);
                    yield return null;
                    Assert.AreEqual((QualityTier)t, QualityTiers.Current, "kademe geçişi");
                    foreach (CameraMode mode in Modes)
                    {
                        CameraModeService.Request(mode, instant: true);
                        Assert.AreEqual(mode, CameraModeService.Current, "mod geçişi");
                        var active = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                        Assert.AreSame(assets[QualitySettings.GetQualityLevel()], active, "etkin varlık seviyenin varlığı değil");
                        Assert.AreEqual(expected[t, (int)mode], active.shadowCascadeCount, (QualityTier)t + " / " + mode + " (" + active.name + ")");
                        Assert.AreEqual(QualityModeApplier.CascadesFor((QualityTier)t, CameraModeService.CurrentProfile), active.shadowCascadeCount, "karar fonksiyonu");
                    }
                }

                // ProfilesChanged (S-05 / S-07 tune a mode through SetProfile): applied at once, and the event path allocates nothing
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Assert.AreEqual(CameraMode.FirstPerson, CameraModeService.Current);
                CameraModeProfile fp = CameraModeService.Profile(CameraMode.FirstPerson);
                CameraModeProfile fp3 = fp;
                fp3.ShadowCascades = 3;
                CameraModeService.SetProfile(CameraMode.FirstPerson, fp3);
                Assert.AreEqual(3, asset.shadowCascadeCount, "ProfilesChanged uygulanmadı");
                CameraModeService.SetProfile(CameraMode.FirstPerson, fp);   // warm both directions once
                byte[] bait = null;
                Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
                Assert.That(() =>
                {
                    CameraModeService.SetProfile(CameraMode.FirstPerson, fp3);
                    CameraModeService.SetProfile(CameraMode.FirstPerson, fp);
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "profil olayı → kaskad yazımı yönetilen bellek ayırdı (yem " + (bait != null ? bait.Length : 0) + " B)");
                Assert.AreEqual(4, asset.shadowCascadeCount, "profil geri");

                // the measurement switch: off writes nothing
                QualityModeApplier.Enabled = false;
                CameraModeService.Request(CameraMode.TopDown, instant: true);
                Assert.AreEqual(4, asset.shadowCascadeCount, "Enabled=false iken yazıldı");
                QualityModeApplier.Enabled = true;
                QualityModeApplier.ApplyNow();
                Assert.AreEqual(QualityModeApplier.CascadesFor(QualityTiers.Current, CameraModeService.CurrentProfile), asset.shadowCascadeCount, "ApplyNow");
            }
            finally
            {
                CameraModeService.SetProfile(CameraMode.FirstPerson, CameraModeProfile.Default(CameraMode.FirstPerson));
                CameraModeService.Request(modeBefore, instant: true);
                if (QualitySettings.GetQualityLevel() != levelBefore) QualitySettings.SetQualityLevel(levelBefore, true);
                QualityModeApplier.Enabled = enabledBefore;
                QualityModeApplier.RestoreOriginals();
                LogAssert.ignoreFailingMessages = false;
            }

            for (int i = 0; i < count; i++) Assert.AreEqual(disk[i], assets[i].shadowCascadeCount, assets[i].name + ": geri yükleme dosyadaki değere dönmedi");
            Assert.AreEqual(0, QualityModeApplier.RecordedAssetCount, "kayıt boşalmadı");
            Assert.AreEqual(levelBefore, QualitySettings.GetQualityLevel(), "kalite seviyesi eski hâline dönmedi");
            Assert.AreEqual(modeBefore, CameraModeService.Current, "kamera modu eski hâline dönmedi");
        }

        /// <summary>The count in the asset file on disk (not the in-memory object the applier writes).</summary>
        private static int FileCascades(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            Assert.IsFalse(string.IsNullOrEmpty(path), asset.name + ": varlık yolu yok");
            Match m = CascadeLine.Match(File.ReadAllText(path));
            Assert.IsTrue(m.Success, path + ": m_ShadowCascadeCount satırı yok");
            return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
#endif
