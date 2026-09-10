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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-000 · Aşama 2.5 kapsam boşluğu — art arda iki koşu ve koşu verisinin sıfırlanması.
    ///
    /// Boot → MainMenu → Selection → Run#1 (gold ve yükseltme kirletilir) → ölüm →
    /// Results → MainMenu → Selection → Run#2. İkinci koşuda RunDirector yeni örnek,
    /// tur 1, tamamlanan tur 0, cüzdan 0, yükseltme katmanları 0, oyuncu canı tam ve
    /// hayatta olmalı. Kalıcı profil ise (test yoluna yönlendirilmiş) ilk koşuyu
    /// saymış olmalı — koşu verisi ile profil verisinin ayrımı burada ölçülür.
    /// </summary>
    [TestFixture]
    public sealed class SecondRunTests
    {
        private const float SahneZamanAsimi = 20f;
        private const float FazZamanAsimi = 25f;

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
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator IkinciKosuTemizBaslarProfilIlkKosuyuSayar()
        {
            // Sahne yükleme uyarıları akışı kesmesin; hüküm assert'lerle verilir.
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return SahneyiBekle(PrototypeScenes.MainMenu);

            int runsPlayedBaslangic = ProfileService.Current.RunsPlayed;

            // ------------------------------------------------------------ Run #1
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;

            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector ilk = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsNotNull(silah, "Run #1: etkin silah kontrolcüsü yok.");
            Assert.IsNotNull(ilk.Wallet, "Run #1: cüzdan yok.");

            // Koşuya özel veriyi bilerek kirlet: gold, yükseltme, can.
            ilk.Wallet.Credit(137, rig.transform.position);
            silah.Modifiers.AddTier(UpgradeBranch.FireRate, 1, PrototypeConfig.Active);
            Assert.AreEqual(137, ilk.Wallet.Balance, "Run #1: cüzdan kredilenmedi.");
            Assert.AreEqual(1, silah.Modifiers.GetTier(UpgradeBranch.FireRate), "Run #1: yükseltme uygulanmadı.");

            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "gt-second-run");
            yield return Bekle(() => ilk.Phase == RunPhase.Dead, FazZamanAsimi, "Run #1 Dead fazına geçmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);

            // Profil (test yolunda) ilk koşuyu saydı mı — koşu → profil köprüsü tek giriş.
            Assert.AreEqual(runsPlayedBaslangic + 1, ProfileService.Current.RunsPlayed,
                "İlk koşu profile işlenmedi (RunsPlayed).");
            Assert.GreaterOrEqual(ProfileService.Current.HighestRound, 1,
                "İlk koşu profile işlenmedi (HighestRound).");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Assert.IsFalse(RunFlowClock.IsFrozen, "Ana menüye dönüşte simülasyon donmuş kaldı.");

            // ------------------------------------------------------------ Run #2
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector ikinci = RunDirector.Instance;
            PlayerRig rig2 = PlayerRegistry.Primary;
            WeaponController silah2 = WeaponRegistry.ActiveController;

            Assert.IsTrue(ilk == null, "Run #1'in RunDirector örneği ikinci koşuda hâlâ yaşıyor.");
            Assert.IsNotNull(ikinci, "Run #2: RunDirector oluşmadı.");
            Assert.AreEqual(RunPhase.Fighting, ikinci.Phase, "Run #2 Fighting fazında başlamalı.");
            Assert.AreEqual(1, ikinci.Round, "Run #2 1. turdan başlamalı.");
            Assert.AreEqual(0, ikinci.Stats.CompletedRounds, "Run #2 tamamlanan tur sayısı 0 olmalı.");
            Assert.AreEqual(0, ikinci.Stats.TotalKills, "Run #2 kill sayısı 0 olmalı.");
            Assert.AreEqual(0, ikinci.RoundKills, "Run #2 tur kill sayısı 0 olmalı.");
            Assert.IsNotNull(ikinci.Wallet, "Run #2: cüzdan yok.");
            Assert.AreEqual(0, ikinci.Wallet.Balance, "Run #2 cüzdanı ilk koşunun altınını devraldı.");
            Assert.IsNotNull(silah2, "Run #2: etkin silah kontrolcüsü yok.");
            for (int i = 0; i < 10; i++)
            {
                var dal = (UpgradeBranch)i;
                Assert.AreEqual(0, silah2.Modifiers.GetTier(dal),
                    "Run #2 '" + dal + "' yükseltme katmanı sıfırlanmadı.");
            }

            Assert.IsFalse(rig2.Health.IsDead, "Run #2 oyuncusu ölü başladı.");
            Assert.AreEqual(rig2.Health.Max, rig2.Health.Current, 0.0001f, "Run #2 oyuncu canı tam değil.");
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, WeaponRegistry.SelectedWeapon,
                "Oturum boyu silah seçimi korunmalı.");
            Assert.AreEqual(runsPlayedBaslangic + 1, ProfileService.Current.RunsPlayed,
                "Run #2 başlangıcı profili değiştirmemeli.");

            Debug.Log("[DD-TEST] secondRun round=" + ikinci.Round + " gold=" + ikinci.Wallet.Balance +
                      " health=" + rig2.Health.Current + "/" + rig2.Health.Max +
                      " runsPlayed=" + ProfileService.Current.RunsPlayed);

            // Temizlik: Play Mode'da bırakılan koşuyu düzgün kapat.
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        private static IEnumerator KosuHazirOlsun()
        {
            yield return Bekle(() => RunDirector.Instance != null, FazZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, FazZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, FazZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, FazZamanAsimi,
                "Koşu Fighting fazına girmedi.");
        }

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string aciklama)
        {
            float baslangic = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - baslangic > saniye)
                {
                    Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                }

                yield return null;
            }
        }

        private static IEnumerator SahneyiBekle(string ad)
        {
            yield return Bekle(
                () => SceneManager.GetActiveScene().name == ad && SceneManager.GetActiveScene().isLoaded,
                SahneZamanAsimi,
                "'" + ad + "' sahnesi etkin hale gelmedi. Şu an: '" + SceneManager.GetActiveScene().name + "'");
        }
    }
}
