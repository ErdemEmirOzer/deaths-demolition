using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-001 · DEC-037 — aynı oyun oturumunda son kullanılan skin ve klasik silah, koşu
    /// bittikten sonra Selection ekranına hazır seçili gelir.
    ///
    /// Taze test profili 1. seviyededir ve yalnız Assault Rifle açıktır; silah hatırlamayı
    /// ölçebilmek için kurulumda profil (izole test dosyasında) tek bir koşu özetiyle
    /// 2. seviyeye çıkarılır. DEC-039 (yeniden açılışta varsayılana dönüş) bir süreç sınırı
    /// gerektirir ve Player probe'uyla ölçülür; bu test yalnız oturum içi hatırlamayı ölçer.
    /// </summary>
    [TestFixture]
    public sealed class SessionSelectionTests
    {
        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator AyniOturumdaSonSecimSelectionEkraninaHazirGelir()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");

            // Kurulum: izole profili 2. seviyeye çıkar ki AutoSmg açılsın (silah hatırlama ölçülebilsin).
            if (!ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg))
            {
                var ozet = new RunSummary
                {
                    CompletionId = System.Guid.NewGuid().ToString("N"),
                    ReachedRound = 1,
                    CompletedRounds = 1,
                    CompletedRoundXp = 100,
                    TotalXpEarned = 100,
                    EndReason = RunEndReason.PlayerDeath,
                    BuildDescription = "session-selection-setup"
                };
                ProfileService.ApplyRun(ozet);
            }

            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg),
                "Kurulum AutoSmg'yi açamadı (profil seviyesi " + ProfileService.Current.ProfileLevel + "); silah hatırlama ölçülemez.");

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            Debug.Log("[DD-TEST] selection.initial skin=" + PrototypeUiFlow.SelectedSkin + " weapon=" + PrototypeUiFlow.SelectedWeapon);

            PrototypeUiFlow.SelectedSkin = PlayerSkin.MossCream;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AutoSmg;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            // Seçim koşuya gerçekten uygulandı mı (statik + sahne).
            PlayerRig rig = PlayerRegistry.Primary;
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.AreEqual(PlayerSkin.MossCream, rig.Skin, "Seçilen skin oyuncuya uygulanmadı.");
            Assert.AreEqual(ClassicWeaponId.AutoSmg, silah.Classic.Id, "Seçilen silah kuşanılmadı.");
            Assert.AreEqual(ClassicWeaponId.AutoSmg, WeaponRegistry.SelectedWeapon, "WeaponRegistry.SelectedWeapon senkron değil.");

            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "session-selection");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;

            // DEC-037: son seçim hazır seçili — statik durum + ekran.
            Assert.AreEqual(PlayerSkin.MossCream, PrototypeUiFlow.SelectedSkin, "Oturum içi skin hatırlanmadı (DEC-037).");
            Assert.AreEqual(ClassicWeaponId.AutoSmg, PrototypeUiFlow.SelectedWeapon, "Oturum içi silah hatırlanmadı (DEC-037).");
            Assert.AreNotEqual(PlayerSkins.Default, PrototypeUiFlow.SelectedSkin, "Skin varsayılana döndü (oturum içinde dönmemeli).");
            SelectionScreen ekran = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(ekran, "SelectionScreen yok.");
            UiButton[] butonlar = ekran.GetComponentsInChildren<UiButton>(true);
            int secili = 0;
            for (int i = 0; i < butonlar.Length; i++)
            {
                if (butonlar[i].IsSelectedChoice)
                {
                    secili++;
                }
            }

            Debug.Log("[DD-TEST] selection.remembered skin=" + PrototypeUiFlow.SelectedSkin + " weapon=" +
                      PrototypeUiFlow.SelectedWeapon + " selectedButtons=" + secili);
            Assert.AreEqual(2, secili, "Selection ekranında tam 2 seçili rozet (1 skin + 1 silah) beklenir.");
            Debug.Log("[DD-TEST] sessionSelection=OK");
        }
    }
}
