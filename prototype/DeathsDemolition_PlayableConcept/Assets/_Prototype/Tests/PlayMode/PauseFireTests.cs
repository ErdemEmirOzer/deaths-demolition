using System;
using System.Collections;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>Ateş tuşu basılı tutulan sahte girdi kaynağı (S · simülasyon).</summary>
    internal sealed class AtesBasiliGirdi : IInputSource
    {
        public bool Basili;

        public bool IsEnabled => true;
        public Vector2 Move => Vector2.zero;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        public Vector2 AimStick => Vector2.zero;
        public bool FireHeld => Basili;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;
#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67
        public void Enable() { }
        public void Disable() { }
    }

    /// <summary>
    /// WP-000 · B1 — simülasyon donmuşken (pause / tur sonu / yükseltme ekranı) silah
    /// ateş etmemeli.
    ///
    /// Statik denetim (CODE-01 L8): Gameplay eylem haritası pause boyunca açık kalır ve
    /// silah yolu <see cref="RunFlowClock.IsFrozen"/>'ı okumaz; hitscan anlık işlendiği
    /// için timeScale=0'da bile bir mermi çıkabilir. Bu test önce donmuş durumda mermi
    /// sayısının değişmediğini, sonra donma kalkınca aynı girdiyle ateş edildiğini
    /// (girdi yolunun gerçekten canlı olduğunu) ölçer. İkinci ölçüm olmadan ilk ölçüm
    /// yalancı yeşil olurdu.
    /// </summary>
    [TestFixture]
    public sealed class PauseFireTests
    {
        private const float SahneZamanAsimi = 20f;
        private const float FazZamanAsimi = 25f;

        private IInputSource _oncekiGirdi;
        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
            _oncekiGirdi = InputSourceRegistry.Current;
        }

        [TearDown]
        public void TearDown()
        {
            InputSourceRegistry.SetCurrent(_oncekiGirdi);
            LogAssert.ignoreFailingMessages = false;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
        }

        [UnityTest]
        [Category("Regression")]
        [Timeout(180000)]
        public IEnumerator DonmusSimulasyondaAtesEdilmezCozulunceEdilir()
        {
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Boot, LoadSceneMode.Single);
            yield return null;
            yield return SahneyiBekle(PrototypeScenes.MainMenu);

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);

            yield return Bekle(() => RunDirector.Instance != null, FazZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, FazZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, FazZamanAsimi, "Silah bağlanmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, FazZamanAsimi,
                "Koşu Fighting fazına girmedi.");

            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsNotNull(silah.Classic, "Klasik silah çalışma zamanı yok.");
            Assert.IsFalse(silah.Classic.InfiniteAmmo, "Test sonsuz mermiyle anlamsız.");
            yield return Bekle(() => silah.Classic.CanFire, FazZamanAsimi, "Silah ateşe hazır hale gelmedi.");

            int mermiBaslangic = silah.Classic.Ammo;
            Assert.Greater(mermiBaslangic, 0, "Şarjör boş başladı.");

            // ---------------------------------------------------------- 1 · donmuş + ateş basılı
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.IsTrue(RunFlowClock.IsFrozen, "Pause tutamağı simülasyonu dondurmadı.");

            var girdi = new AtesBasiliGirdi { Basili = true };
            InputSourceRegistry.SetCurrent(girdi);

            for (int i = 0; i < 6; i++)
            {
                yield return null;
            }

            int mermiDonmus = silah.Classic.Ammo;
            Debug.Log("[DD-TEST] pauseFire frozen ammo " + mermiBaslangic + " -> " + mermiDonmus);

            // ---------------------------------------------------------- 2 · çözül + aynı girdi
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.IsFalse(RunFlowClock.IsFrozen, "Tutamak bırakılınca dünya çözülmeli.");

            float baslangic = Time.realtimeSinceStartup;
            while (silah.Classic.Ammo >= mermiDonmus && Time.realtimeSinceStartup - baslangic < 3f)
            {
                yield return null;
            }

            int mermiCozulmus = silah.Classic.Ammo;
            girdi.Basili = false;
            Debug.Log("[DD-TEST] pauseFire unfrozen ammo " + mermiDonmus + " -> " + mermiCozulmus);

            Assert.Less(mermiCozulmus, mermiDonmus,
                "Donma kalkınca basılı ateş tuşu mermi harcamadı; girdi yolu ölçülemedi (test geçersiz).");
            Assert.AreEqual(mermiBaslangic, mermiDonmus,
                "B1: simülasyon donmuşken (pause) silah ateş etti; mermi " + mermiBaslangic + " -> " + mermiDonmus);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
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
