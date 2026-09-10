using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-001 · Aşama 5 — statik denetimde (code-02 A1/A2) kanıtlanan iki yaşam döngüsü
    /// hatasının runtime yeniden üretimi ve düzeltme sonrası regresyon koruması.
    ///
    /// A1: ölüm beat'i (Results'a geçiş gecikmesi) sırasında pause menüsünden ana menüye
    ///     çıkılırsa, DDOL koşucudaki gecikmeli hand-off Main Menu'nün üstüne Results'ı
    ///     yüklememeli.
    /// A2: koşu bittiğinde yerde duran Special Weapon'ın HUD anlık görüntüsü ikinci koşuya
    ///     "ON THE FIELD" olarak taşınmamalı.
    /// </summary>
    [TestFixture]
    public sealed class FlowRaceTests
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
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator OlumBeatindeAnaMenuyeCikis_ResultsMenununUstuneYuklenmez()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector director = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            PauseScreen pause = Object.FindObjectOfType<PauseScreen>();
            Assert.IsNotNull(pause, "PauseScreen yok.");

            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "race-a1");
            yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            Assert.AreEqual(PrototypeScenes.Run, SceneManager.GetActiveScene().name, "Ölüm beat'i başlamadan Results yüklendi.");

            // Ölüm beat'i içinde: pause → MAIN MENU → END RUN (ConfirmQuit'in yaptığı çağrı).
            pause.SetPaused(true);
            yield return null;
            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] race.a1 abort-in-death-beat -> " + SceneManager.GetActiveScene().name);

            // Gecikmeli hand-off penceresinin (1.6 s) kesin geçmesini bekle.
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f)
            {
                yield return null;
            }

            Debug.Log("[DD-TEST] race.a1 after-delay scene=" + SceneManager.GetActiveScene().name);
            Assert.AreEqual(PrototypeScenes.MainMenu, SceneManager.GetActiveScene().name,
                "A1: ölüm beat'inde ana menüye çıkıldıktan sonra gecikmeli hand-off Results'ı yükledi.");
            Assert.AreEqual(0, AktifSayi<ResultsScreen>(), "A1: Main Menu'de ResultsScreen nesnesi var.");
            Assert.AreEqual(1, AktifSayi<MainMenuScreen>(), "Main Menu ekranı tekil değil.");
            yield return SimulasyonSerbestOlmali("a1-menu");
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator KosuSonundaYerdeKalanSpecialWeapon_IkinciKosuHudunaTasinmaz()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            SpecialWeaponDirector sw = Object.FindObjectOfType<SpecialWeaponDirector>();
            Assert.IsNotNull(sw, "SpecialWeaponDirector yok.");
            Assert.IsTrue(sw.ForceSpawn(), "Special Weapon zorla düşürülemedi.");
            yield return Bekle(() => WeaponEvents.Special.Phase == SpecialWeaponPhase.OnGround, FazZamanAsimi,
                "Special Weapon yere düşmedi (HUD snapshot OnGround olmadı).");
            Vector3 yerdekiKonum = WeaponEvents.Special.WorldPosition;
            Debug.Log("[DD-TEST] race.a2 run1 special=" + WeaponEvents.Special.Phase + " at " + yerdekiKonum);

            PlayerRig rig = PlayerRegistry.Primary;
            RunDirector director = RunDirector.Instance;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "race-a2");
            yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return null;
            yield return null;

            Debug.Log("[DD-TEST] race.a2 run2 special=" + WeaponEvents.Special.Phase + " at " + WeaponEvents.Special.WorldPosition);
            Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase,
                "A2: önceki koşunun yerdeki Special Weapon HUD durumu ikinci koşuya taşındı.");
            Assert.AreEqual(0, AktifSayi<SpecialWeaponPickup>(), "İkinci koşu başında yerde pickup nesnesi var.");
            Debug.Log("[DD-TEST] race.a2=OK");

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }
    }
}
