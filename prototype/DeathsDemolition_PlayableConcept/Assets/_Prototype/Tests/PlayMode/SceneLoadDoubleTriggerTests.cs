using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-001 · Aşama 5.4 — sahne geçişlerinde statik olaylar iki kez tetiklenmez ve UI
    /// kurulumu sahne başına bir kez yapılır.
    ///
    /// Üç bağımsız ölçüm: SceneManager.sceneLoaded abone sayısının tam döngü sonunda
    /// büyümemesi (yalnız testin kendi abonesi), testin kendi sayacında her yükleme için
    /// tam bir kayıt, ve her sahnede tekil ekran/EventSystem nesnesi.
    /// </summary>
    [TestFixture]
    public sealed class SceneLoadDoubleTriggerTests
    {
        private readonly List<string> _yuklemeler = new List<string>();
        private int _aktifDegisim;
        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
            _yuklemeler.Clear();
            _aktifDegisim = 0;
            SceneManager.sceneLoaded += OnLoaded;
            SceneManager.activeSceneChanged += OnActive;
        }

        [TearDown]
        public void TearDown()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.activeSceneChanged -= OnActive;
            LogAssert.ignoreFailingMessages = false;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
        }

        private void OnLoaded(Scene s, LoadSceneMode m) => _yuklemeler.Add(s.name + "/" + m);
        private void OnActive(Scene a, Scene b) => _aktifDegisim++;

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator SahneGecisindeUiKurulumuTekTetik()
        {
            LogAssert.ignoreFailingMessages = true;

            int sceneLoadedBefore = AboneSayisi(typeof(SceneManager), "sceneLoaded");
            int activeBefore = AboneSayisi(typeof(SceneManager), "activeSceneChanged");

            yield return BootTanMainMenuye();
            yield return null;
            Assert.AreEqual(1, AktifSayi<MainMenuScreen>(), "MainMenu'de tekil MainMenuScreen yok.");
            Assert.AreEqual(1, AktifSayi<EventSystem>(), "MainMenu'de EventSystem sayısı 1 değil.");
            int loadedAfterBoot = AboneSayisi(typeof(SceneManager), "sceneLoaded");
            int activeAfterBoot = AboneSayisi(typeof(SceneManager), "activeSceneChanged");

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            Assert.AreEqual(1, AktifSayi<SelectionScreen>(), "Selection'da tekil SelectionScreen yok.");
            Assert.AreEqual(1, AktifSayi<EventSystem>(), "Selection'da EventSystem sayısı 1 değil.");

            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return null;
            Assert.AreEqual(1, AktifSayi<RunHudScreen>(), "Run'da tekil RunHudScreen yok.");
            Assert.AreEqual(1, AktifSayi<UpgradeScreen>(), "Run'da tekil UpgradeScreen yok.");
            Assert.AreEqual(1, AktifSayi<PauseScreen>(), "Run'da tekil PauseScreen yok.");
            Assert.AreEqual(1, AktifSayi<SelectedSkinApplier>(), "Run'da tekil SelectedSkinApplier yok.");
            Assert.AreEqual(1, AktifSayi<EventSystem>(), "Run'da EventSystem sayısı 1 değil.");

            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "scene-load");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);
            yield return null;
            Assert.AreEqual(1, AktifSayi<ResultsScreen>(), "Results'ta tekil ResultsScreen yok.");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            yield return null;
            Assert.AreEqual(1, AktifSayi<MainMenuScreen>(), "Döngü sonunda tekil MainMenuScreen yok.");

            // 1) Abone büyümesi yok: yalnız testin kendi abonesi (+1) kalıcı; kurulum kancası tek kez.
            int loadedAfter = AboneSayisi(typeof(SceneManager), "sceneLoaded");
            int activeAfter = AboneSayisi(typeof(SceneManager), "activeSceneChanged");
            Debug.Log("[DD-TEST] sceneLoaded.subs before=" + sceneLoadedBefore + " afterBoot=" + loadedAfterBoot + " after=" + loadedAfter +
                      " | activeSceneChanged before=" + activeBefore + " afterBoot=" + activeAfterBoot + " after=" + activeAfter);
            Assert.AreEqual(loadedAfterBoot, loadedAfter, "sceneLoaded abone sayısı tam döngüde büyüdü (kanca birikimi).");
            Assert.AreEqual(activeAfterBoot, activeAfter, "activeSceneChanged abone sayısı tam döngüde büyüdü.");

            // 2) Kendi sayacı: her yükleme tam bir kez; ardışık tekrar yok.
            string sira = string.Join(" -> ", _yuklemeler);
            Debug.Log("[DD-TEST] sceneLoaded.sequence=" + sira + " activeChanges=" + _aktifDegisim);
            // WP-014/WP-020 (S-05, WP-039 P4): with WorldSource = AP005 the Run scene loads the
            // world scene ADDITIVELY right after itself (WorldBootstrap). That is the one legitimate
            // additive load; it does not change the active scene and must happen exactly once per Run.
            string dunyaSahnesi;
            bool ap005 = WorldSourceSettings.Resolve(out dunyaSahnesi) == WorldSource.AP005;
            var beklenenListe = new List<string>
            {
                PrototypeScenes.Boot, PrototypeScenes.MainMenu, PrototypeScenes.Selection, PrototypeScenes.Run
            };
            if (ap005)
            {
                beklenenListe.Add(dunyaSahnesi);
            }

            beklenenListe.AddRange(new[]
            {
                PrototypeScenes.Results, PrototypeScenes.MainMenu, PrototypeScenes.Selection, PrototypeScenes.MainMenu
            });
            string[] beklenen = beklenenListe.ToArray();
            var gozlenen = new List<string>();
            for (int i = 0; i < _yuklemeler.Count; i++)
            {
                gozlenen.Add(_yuklemeler[i].Split('/')[0]);
            }

            CollectionAssert.AreEqual(beklenen, gozlenen, "Sahne yükleme dizisi beklenenden farklı (çift tetik/atlama).");
            int additive = 0;
            for (int i = 0; i < _yuklemeler.Count; i++)
            {
                string sahne = _yuklemeler[i].Split('/')[0];
                if (ap005 && sahne == dunyaSahnesi)
                {
                    Assert.IsTrue(_yuklemeler[i].EndsWith("/Additive"), "Dünya sahnesi additive yüklenmeli: " + _yuklemeler[i]);
                    additive++;
                    continue;
                }

                Assert.IsTrue(_yuklemeler[i].EndsWith("/Single"), "Additive yükleme gözlendi: " + _yuklemeler[i]);
            }

            Assert.AreEqual(ap005 ? 1 : 0, additive, "Dünya sahnesi Run başına tam bir kez additive yüklenmeli.");
            Assert.AreEqual(_yuklemeler.Count - additive, _aktifDegisim, "activeSceneChanged sayısı tekil yükleme sayısıyla eşit olmalı (additive dünya sahnesi aktif sahneyi değiştirmez).");
            Debug.Log("[DD-TEST] sceneLoadSingle=OK");
        }
    }
}
