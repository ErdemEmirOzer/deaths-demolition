#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Rendering;
using DD.Prototype.UI;
using DD.Prototype.World;
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
    /// and <see cref="QualityModeApplier.RestoreOriginals"/> leaves every level's URP asset at the value in its file. Under the
    /// menu's cinematic backdrop (<see cref="WorldViewer.Active"/>, WP-081) the assets keep their file values (gt Y-2).
    /// <para>Top Down only (user 06.10): the matrix and the profile edit run on TD; the TP / FP rows are their own
    /// <c>[Category("FPTP")]</c> test, which main-ci's core leaves out.</para>
    /// </summary>
    [TestFixture]
    public sealed class QualityModeApplierPlayTests
    {
        private const float SahneZamanAsimi = 60f;
        private static readonly CameraMode[] TdModes = { CameraMode.TopDown };
        private static readonly CameraMode[] FptpModes = { CameraMode.ThirdPerson, CameraMode.FirstPerson };
        private static readonly Regex CascadeLine = new Regex(@"^\s*m_ShadowCascadeCount:\s*(\d+)\s*$", RegexOptions.Multiline);
        private static readonly Regex DistanceLine = new Regex(@"^\s*m_ShadowDistance:\s*([0-9.]+)\s*$", RegexOptions.Multiline);

        [Category("WP070")]
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Kaskad_KademeVeModOlaylari_ProfilKaskadiUygulanir_GeriYuklemeDosyaDegeri()
        {
            return Matris(TdModes);
        }

        /// <summary>The same matrix over TP and FP (the profile edit on FP), outside the Top Down suite.</summary>
        [Category("WP070")]
        [UnityTest]
        [Timeout(180000)]
        [Category("FPTP")]
        public IEnumerator Kaskad_KademeVeModOlaylari_TPFP_ProfilKaskadiUygulanir_GeriYuklemeDosyaDegeri()
        {
            return Matris(FptpModes);
        }

        /// <summary>
        /// Every tier × <paramref name="modes"/> through the level and mode events, then a profile edit of the last mode
        /// (ProfilesChanged, 0 B) and the measurement switch, at the last tier (Sinematik, cap 4); the restore after it.
        /// </summary>
        private static IEnumerator Matris(CameraMode[] modes)
        {
            LogAssert.ignoreFailingMessages = true;
            int count = QualitySettings.count;
            Assert.AreEqual(QualityTiers.LevelCount, count, "kalite seviyesi sayısı");
            int levelBefore = QualitySettings.GetQualityLevel();
            CameraMode modeBefore = CameraModeService.Current;
            bool enabledBefore = QualityModeApplier.Enabled;
            var assets = new UniversalRenderPipelineAsset[count];
            var disk = new int[count];
            var diskDistance = new float[count];
            for (int i = 0; i < count; i++)
            {
                assets[i] = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
                Assert.IsNotNull(assets[i], "seviye " + i + " URP varlığı");
                disk[i] = FileCascades(assets[i]);
                diskDistance[i] = FileDistance(assets[i]);
            }

            // [tier, mode] with the WP-070 default profiles: Performans 2/2/2, Yüksek 2/3/3 (cap 3, C-00 06.10), Sinematik 2/3/4 (before the fix: the asset's 2 / 2 / 4)
            // — on TD only Sinematik differs from its file (4 cascades / 120 m), so the profile edit below runs there
            int[,] expected = { { 2, 2, 2 }, { 2, 3, 3 }, { 2, 3, 4 } };
            // shadow distance (C-00 06.10 decision 3): min(cap 45 / 60 / 120, profile TD 60 / TP 60 / FP 70)
            float[,] expectedDistance = { { 45f, 45f, 45f }, { 60f, 60f, 60f }, { 60f, 60f, 70f } };
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
                // WP-081: MainMenu starts the cinematic backdrop, under which the assets keep their own values (the test below);
                // the matrix is the run's path, so the backdrop goes first, as when a run starts
                if (MenuCinematic.Current != null)
                {
                    Object.Destroy(MenuCinematic.Current);
                    yield return null;
                }

                Assert.IsFalse(WorldViewer.Active, "menü arka planı gitmedi");
                // the assets back to their files: every row below is written by the level / mode events alone (on TD, where the
                // mode request is a no-op, the Sinematik row only through QualityTiers.Changed)
                QualityModeApplier.RestoreOriginals();

                for (int t = 0; t < count; t++)
                {
                    QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier((QualityTier)t, count), true);
                    yield return null;
                    Assert.AreEqual((QualityTier)t, QualityTiers.Current, "kademe geçişi");
                    foreach (CameraMode mode in modes)
                    {
                        CameraModeService.Request(mode, instant: true);
                        Assert.AreEqual(mode, CameraModeService.Current, "mod geçişi");
                        var active = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                        Assert.AreSame(assets[QualitySettings.GetQualityLevel()], active, "etkin varlık seviyenin varlığı değil");
                        Assert.AreEqual(expected[t, (int)mode], active.shadowCascadeCount, (QualityTier)t + " / " + mode + " (" + active.name + ")");
                        Assert.AreEqual(QualityModeApplier.CascadesFor((QualityTier)t, CameraModeService.CurrentProfile), active.shadowCascadeCount, "karar fonksiyonu");
                        Assert.AreEqual(expectedDistance[t, (int)mode], active.shadowDistance, 1e-3f, "gölge mesafesi " + (QualityTier)t + " / " + mode);
                    }
                }

                // ProfilesChanged (S-05 / S-07 tune a mode through SetProfile): applied at once, and the event path allocates nothing
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                CameraMode edited = modes[modes.Length - 1];
                int profileCascades = expected[(int)QualityTier.Sinematik, (int)edited];
                Assert.AreEqual(QualityTier.Sinematik, QualityTiers.Current, "profil düzenlemesi Sinematik'te (tavan 4)");
                Assert.AreEqual(edited, CameraModeService.Current);
                CameraModeProfile before = CameraModeService.Profile(edited);
                CameraModeProfile three = before;
                three.ShadowCascades = 3;
                Assert.AreNotEqual(3, profileCascades, "ön koşul: düzenleme profilin kaskadını değiştirmeli");
                CameraModeService.SetProfile(edited, three);
                Assert.AreEqual(3, asset.shadowCascadeCount, "ProfilesChanged uygulanmadı");
                CameraModeService.SetProfile(edited, before);   // warm both directions once
                byte[] bait = null;
                Assert.That(() => { bait = new byte[64 * 1024]; }, UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: kayıtçı ayırmayı görmeli");
                Assert.That(() =>
                {
                    CameraModeService.SetProfile(edited, three);
                    CameraModeService.SetProfile(edited, before);
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "profil olayı → kaskad yazımı yönetilen bellek ayırdı (yem " + (bait != null ? bait.Length : 0) + " B)");
                Assert.AreEqual(profileCascades, asset.shadowCascadeCount, "profil geri");

                // the measurement switch: off writes nothing (a profile event while off), ApplyNow catches up once it is on
                QualityModeApplier.Enabled = false;
                CameraModeService.SetProfile(edited, three);
                Assert.AreEqual(profileCascades, asset.shadowCascadeCount, "Enabled=false iken yazıldı");
                QualityModeApplier.Enabled = true;
                QualityModeApplier.ApplyNow();
                Assert.AreEqual(3, asset.shadowCascadeCount, "ApplyNow");
                Assert.AreEqual(QualityModeApplier.CascadesFor(QualityTiers.Current, CameraModeService.CurrentProfile), asset.shadowCascadeCount, "ApplyNow karar fonksiyonu");
                CameraModeService.SetProfile(edited, before);
            }
            finally
            {
                foreach (CameraMode mode in modes) CameraModeService.SetProfile(mode, CameraModeProfile.Default(mode));
                CameraModeService.Request(modeBefore, instant: true);
                if (QualitySettings.GetQualityLevel() != levelBefore) QualitySettings.SetQualityLevel(levelBefore, true);
                QualityModeApplier.Enabled = enabledBefore;
                QualityModeApplier.RestoreOriginals();
                LogAssert.ignoreFailingMessages = false;
            }

            for (int i = 0; i < count; i++) Assert.AreEqual(disk[i], assets[i].shadowCascadeCount, assets[i].name + ": geri yükleme dosyadaki değere dönmedi");
            for (int i = 0; i < count; i++) Assert.AreEqual(diskDistance[i], assets[i].shadowDistance, 1e-3f, assets[i].name + ": gölge mesafesi dosyadaki değere dönmedi");
            Assert.AreEqual(0, QualityModeApplier.RecordedAssetCount, "kayıt boşalmadı");
            Assert.AreEqual(levelBefore, QualitySettings.GetQualityLevel(), "kalite seviyesi eski hâline dönmedi");
            Assert.AreEqual(modeBefore, CameraModeService.Current, "kamera modu eski hâline dönmedi");
        }

        [Category("WP070")]
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Kaskad_MenuArkaPlani_WorldViewerAktif_VarlikDegerleriKalir()
        {
            LogAssert.ignoreFailingMessages = true;
            int count = QualitySettings.count;
            int levelBefore = QualitySettings.GetQualityLevel();
            CameraMode modeBefore = CameraModeService.Current;
            bool enabledBefore = QualityModeApplier.Enabled;
            bool viewerBefore = WorldViewer.Active;
            try
            {
                QualityModeApplier.Enabled = true;
                WorldViewer.Active = false;
                QualitySettings.SetQualityLevel(QualityTiers.LevelOfTier(QualityTier.Sinematik, count), true);
                yield return null;
                CameraModeService.Request(CameraMode.TopDown, instant: true);
                QualityModeApplier.ApplyNow();   // re-arms: an earlier fixture may have cleared the service's events
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Assert.IsNotNull(asset, "etkin URP varlığı yok");
                int fileCascades = FileCascades(asset);
                float fileDistance = FileDistance(asset);
                Assert.AreEqual(2, asset.shadowCascadeCount, "oyun yolu: Sinematik TD profil kaskadı");
                Assert.AreEqual(60f, asset.shadowDistance, 1e-3f, "oyun yolu: Sinematik TD profil mesafesi");

                // the menu backdrop owns the world: the next event leaves the asset at its file values (the TD profile edit
                // to 3 cascades would give 3 / 60 m; Top Down only, user 06.10 — a profile event, not a TP request)
                Assert.AreNotEqual(3, fileCascades, "ön koşul: dosya kaskadı düzenlenen profilden ayırt edilebilmeli");
                CameraModeProfile td = CameraModeService.Profile(CameraMode.TopDown);
                CameraModeProfile td3 = td;
                td3.ShadowCascades = 3;
                WorldViewer.Active = true;
                CameraModeService.SetProfile(CameraMode.TopDown, td3);
                Assert.AreEqual(fileCascades, asset.shadowCascadeCount, "menü arka planı: kaskad dosya değeri değil");
                Assert.AreEqual(fileDistance, asset.shadowDistance, 1e-3f, "menü arka planı: mesafe dosya değeri değil");
                Assert.AreEqual(0, QualityModeApplier.RecordedAssetCount, "menü arka planında kayıt kaldı");

                // the backdrop goes and its world scene unloads: the profile applies again without a mode / tier event
                WorldViewer.Active = false;
                Scene probe = SceneManager.CreateScene("S05 applier unload probe");
                yield return SceneManager.UnloadSceneAsync(probe);
                Assert.AreEqual(3, asset.shadowCascadeCount, "arka plan gidince TD profili uygulanmadı (sceneUnloaded)");
                Assert.AreEqual(60f, asset.shadowDistance, 1e-3f, "arka plan gidince TD mesafesi uygulanmadı (sceneUnloaded)");
            }
            finally
            {
                WorldViewer.Active = viewerBefore;
                CameraModeService.SetProfile(CameraMode.TopDown, CameraModeProfile.Default(CameraMode.TopDown));
                CameraModeService.Request(modeBefore, instant: true);
                if (QualitySettings.GetQualityLevel() != levelBefore) QualitySettings.SetQualityLevel(levelBefore, true);
                QualityModeApplier.Enabled = enabledBefore;
                QualityModeApplier.RestoreOriginals();
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>The shadow distance in the asset file on disk.</summary>
        private static float FileDistance(Object asset)
        {
            Match m = DistanceLine.Match(File.ReadAllText(AssetDatabase.GetAssetPath(asset)));
            Assert.IsTrue(m.Success, asset.name + ": m_ShadowDistance satırı yok");
            return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
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
