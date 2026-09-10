using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-001 · Aşama 5.6 — normal ölüm → Results yolu profili TAM BİR KEZ uygular ve diske
    /// yazar; Results → Main Menu geçişi ikinci bir uygulama tetiklemez.
    ///
    /// Üç bağımsız ölçüm: RunEnded/Changed olay sayaçları, bellekteki RunsPlayed farkı ve
    /// diskteki dosyanın (izole test profili) yeniden okunması + mtime/LastSavedUtc.
    /// </summary>
    [TestFixture]
    public sealed class ProfileApplyOnceTests
    {
        private float _eskiTimeScale;
        private int _ended;
        private int _changed;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
            _ended = 0;
            _changed = 0;
            RunEvents.RunEnded += OnEnded;
            ProfileService.Changed += OnChanged;
        }

        [TearDown]
        public void TearDown()
        {
            RunEvents.RunEnded -= OnEnded;
            ProfileService.Changed -= OnChanged;
            LogAssert.ignoreFailingMessages = false;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
        }

        private void OnEnded(RunSummary s) => _ended++;
        private void OnChanged() => _changed++;

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator KosuSonundaApplyRunTamBirKezVeChangedTamBirKez()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return BootTanMainMenuye();

            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path,
                "Profil yolu izole değil — gerçek profil yazılabilirdi; test durduruldu.");

            int runsBefore = ProfileService.Current.RunsPlayed;
            string savedBefore = ProfileService.Current.LastSavedUtc;
            long mtimeBefore = File.Exists(ProfileStore.Path) ? File.GetLastWriteTimeUtc(ProfileStore.Path).Ticks : 0L;
            Debug.Log("[DD-TEST] applyOnce.before runsPlayed=" + runsBefore + " saved=" + savedBefore);

            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector director = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "apply-once");
            yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);

            // Results sahnesinde 1.6 s gecikmeli akış + birkaç kare sonra ölçüm.
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2f)
            {
                yield return null;
            }

            Assert.AreEqual(1, _ended, "RunEnded tam bir kez yayınlanmalı.");
            Assert.AreEqual(1, _changed, "ProfileService.Changed tam bir kez (ApplyRun) tetiklenmeli.");
            Assert.AreEqual(runsBefore + 1, ProfileService.Current.RunsPlayed, "Bellekte RunsPlayed tam +1 olmalı.");
            RunRewardReport rapor = ProfileService.LastReport;
            Assert.IsNotNull(rapor, "LastReport yok.");
            Assert.IsTrue(rapor.HasRun, "LastReport.HasRun false.");
            Assert.IsFalse(rapor.SaveFailed, "Profil kaydı başarısız.");
            Assert.AreEqual(RunEndReason.PlayerDeath, rapor.Summary.EndReason, "Koşu sonu sebebi ölüm olmalı.");

            // Disk: dosya yeniden okunur, +1 ve yeni zaman damgası.
            Assert.IsTrue(File.Exists(ProfileStore.Path), "Profil dosyası diskte yok: " + ProfileStore.Path);
            var disk = JsonUtility.FromJson<ProfileData>(File.ReadAllText(ProfileStore.Path));
            Assert.AreEqual(runsBefore + 1, disk.RunsPlayed, "Diskte RunsPlayed tam +1 olmalı.");
            Assert.AreNotEqual(savedBefore, disk.LastSavedUtc, "LastSavedUtc değişmedi — disk yazımı olmadı.");
            Assert.Greater(File.GetLastWriteTimeUtc(ProfileStore.Path).Ticks, mtimeBefore, "Profil dosyası mtime ilerlemedi.");
            Debug.Log("[DD-TEST] applyOnce.results ended=" + _ended + " changed=" + _changed +
                      " runsPlayed=" + ProfileService.Current.RunsPlayed + " disk=" + disk.RunsPlayed);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            yield return null;
            yield return null;

            Assert.AreEqual(1, _ended, "Main Menu'ye dönüş RunEnded'ı tekrar yayınladı.");
            Assert.AreEqual(1, _changed, "Main Menu'ye dönüş profili tekrar uyguladı.");
            Assert.AreEqual(runsBefore + 1, ProfileService.Current.RunsPlayed, "Main Menu'de RunsPlayed değişti.");
            var disk2 = JsonUtility.FromJson<ProfileData>(File.ReadAllText(ProfileStore.Path));
            Assert.AreEqual(runsBefore + 1, disk2.RunsPlayed, "Main Menu'ye dönüşte disk RunsPlayed değişti.");
            Debug.Log("[DD-TEST] applyOnce=OK");
        }
    }
}
