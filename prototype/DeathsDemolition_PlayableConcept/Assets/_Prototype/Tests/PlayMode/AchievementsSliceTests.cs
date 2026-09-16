using System.Collections;
using System.IO;
using System.Reflection;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-023: gerçek akışta bir koşu → Run Results veri modeli doğru (ADR-023-1); feat izleyici koşunun
    /// tokenına bağlı; çökme simülasyonu (replace hatası) → kayıp 0, retry tam bir kez. İzole profil.
    /// </summary>
    [TestFixture]
    [Category("WP023")]
    public sealed class AchievementsSliceTests
    {
        private const float KurulumZamanAsimi = 25f;
        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            ProfileStore.SaveStageHook = null;
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.SaveStageHook = null;
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
            LogAssert.ignoreFailingMessages = false;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            if (EnemySpawnDirector.Instance != null)
            {
                SpawnAyarla(true);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }

        private static IEnumerator KosuyaGir()
        {
            yield return BootTanMainMenuye();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
        }

        private static IEnumerator KontrolluSaha()
        {
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector yok.");
            yield return Bekle(() => GoldService.Instance != null, KurulumZamanAsimi, "GoldService yok.");
            SpawnAyarla(false);
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            GoldService.Instance.ClearOrbs(false);
            yield return null;
            Time.captureDeltaTime = 1f / 60f;
        }

        private static void SpawnAyarla(bool acik)
        {
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(EnemySpawnDirector.Instance, acik);
        }

        private static IEnumerator Oldur(int adet)
        {
            Vector3 merkez = PlayerRegistry.Primary.transform.position;
            for (int i = 0; i < adet; i++)
            {
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, RunDirector.Instance.Round, merkez + new Vector3(14f + i * 1.2f, 0f, 3f), Quaternion.identity);
                Assert.IsNotNull(z, "Test zombisi doğmadı.");
                z.Kill();
                yield return null;
            }

            yield return null;
        }

        private static IEnumerator OyuncuyuOldurVeResultsBekle()
        {
            RunDirector director = RunDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = false;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp023-death");
            yield return Bekle(() => director.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2f)
            {
                yield return null;
            }
        }

        private static ProfileData Disk()
        {
            ProfileData d;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(ProfileStore.Path, out d), "disk profili okunmalı");
            return d;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator A1_GercekKosu_ResultsVerisiDogru_FeatlerTokenaBagli_SemaV4()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();

            string token = RunDirector.Instance.CompletionId;
            Assert.IsNotNull(token);
            Assert.AreEqual(token, AchievementRunTracker.BoundToken, "izleyici koşu başlangıcında canlı tokena bağlanır");
            int killsOnce = ProfileService.Current.TotalKills;
            int runsOnce = ProfileService.Current.RunsPlayed;
            bool firstBloodOnce = ProfileService.HasAchievement("first_blood");

            yield return Oldur(3);
            yield return OyuncuyuOldurVeResultsBekle();

            RunRewardReport makbuz = ProfileService.LastReport;
            Assert.IsNotNull(makbuz);
            Assert.IsTrue(makbuz.Applied);
            Assert.AreEqual(token, makbuz.CompletionId);

            RunResultsData d = ProfileService.LastResults;
            Assert.AreSame(d, ProfileService.ResultsSource.Current, "tek kapı");
            Assert.IsTrue(d.HasRun && d.Applied);
            Assert.AreEqual(token, d.CompletionId);
            Assert.AreEqual(3, d.TotalKills);
            Assert.AreEqual(3, d.StandardKills);
            Assert.AreEqual(1, d.ReachedRound);
            Assert.AreEqual(0, d.CompletedRounds);
            Assert.AreEqual(RunEndReason.PlayerDeath, d.EndReason);
            Assert.AreEqual(makbuz.TotalXpEarned, d.TotalXpEarned);
            Assert.AreEqual(makbuz.KillXp, d.KillXp);
            Assert.AreEqual(makbuz.LevelAfter, d.LevelAfter);
            Assert.AreEqual(0, d.FlawlessRounds, "tamamlanan round yok");
            Assert.AreEqual(0, d.SpecialWeaponPickups);
            Assert.AreEqual(ProfileService.Current.RunsPlayed, d.LifetimeRunsPlayed);
            Assert.AreEqual(ProfileService.Current.TotalKills, d.LifetimeTotalKills);
            Assert.AreEqual(killsOnce + 3, d.LifetimeTotalKills);
            Assert.AreEqual(runsOnce + 1, d.LifetimeRunsPlayed);
            if (!firstBloodOnce)
            {
                bool var = false;
                foreach (RunResultsAchievement a in d.NewAchievements)
                {
                    var |= a.Id == "first_blood";
                }

                Assert.IsTrue(var, "ilk kill başarımı bu koşuda kazanılır ve Results verisinde görünür");
                Assert.IsTrue(ProfileService.HasAchievement("first_blood"));
            }

            Assert.AreEqual(makbuz.NewAchievements.Count, d.NewAchievements.Count);
            Assert.AreEqual(makbuz.NewUnlocks.Count, d.NewUnlocks.Count);

            // Diskte v4, RAM ile aynı; Results ekranı mevcut makbuzla uyumlu (UI değişmedi).
            ProfileData disk = Disk();
            Assert.AreEqual(4, disk.SaveVersion);
            Assert.AreEqual(ProfileService.Current.TotalKills, disk.TotalKills);
            CollectionAssert.AreEqual(ProfileService.Current.Achievements, disk.Achievements);
            ResultsScreen ekran = Object.FindObjectOfType<ResultsScreen>();
            Assert.IsNotNull(ekran);
            Assert.IsTrue(ekran.Model.HasRun && ekran.Model.Applied);
            Debug.Log("[DD-TEST] wp023.A1=OK kills=" + d.TotalKills + " newAch=" + d.NewAchievements.Count + " newUnl=" + d.NewUnlocks.Count + " lvl=" + d.LevelBefore + "->" + d.LevelAfter);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator A2_CokmeSimulasyonu_KayipSifir_RetryTamBirKez_BasarimIkiKezAcilmaz()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return KosuyaGir();
            yield return KontrolluSaha();
            yield return Oldur(2);

            string token = RunDirector.Instance.CompletionId;
            int achOnce = ProfileService.Current.Achievements.Count;
            int contentOnce = ProfileService.Current.UnlockedContent.Count;
            int killsOnce = ProfileService.Current.TotalKills;
            string diskOnce = File.ReadAllText(ProfileStore.Path);
            ProfileStore.SaveStageHook = s =>
            {
                if (s == ProfileSaveStage.Replace)
                {
                    throw new IOException("wp023 injected replace fault");
                }
            };

            yield return OyuncuyuOldurVeResultsBekle();
            ProfileStore.SaveStageHook = null;

            RunRewardReport fail = ProfileService.LastReport;
            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, fail.Outcome);
            Assert.AreEqual(0, fail.EarnedDefinitions.Count);
            Assert.AreEqual(0, fail.OpenedContent.Count);
            Assert.AreEqual(achOnce, ProfileService.Current.Achievements.Count, "başarısız yazımda başarım açılmaz");
            Assert.AreEqual(contentOnce, ProfileService.Current.UnlockedContent.Count);
            Assert.AreEqual(killsOnce, ProfileService.Current.TotalKills);
            Assert.AreEqual(diskOnce, File.ReadAllText(ProfileStore.Path), "disk = önceki (kayıp 0, kısmi yazım 0)");
            RunResultsData dFail = ProfileService.LastResults;
            Assert.IsTrue(dFail.HasRun);
            Assert.IsFalse(dFail.Applied);
            Assert.AreEqual(0, dFail.NewAchievements.Count);
            Assert.AreEqual(2, dFail.TotalKills, "koşu yine görünür");

            // Retry (aynı token, hata yok): tam bir kez; feat'ler tokena bağlı olduğu için yine aynı.
            RunRewardReport ok = ProfileService.ApplyRun(fail.Summary);
            Assert.IsTrue(ok.Applied);
            Assert.AreEqual(token, ok.CompletionId);
            Assert.AreEqual(killsOnce + 2, ProfileService.Current.TotalKills);
            Assert.AreEqual(killsOnce + 2, Disk().TotalKills);
            int achSonra = ProfileService.Current.Achievements.Count;
            Assert.GreaterOrEqual(achSonra, achOnce);
            Assert.AreEqual(ok.EarnedDefinitions.Count, achSonra - achOnce, "kazanılan = makbuzdaki");

            RunRewardReport dup = ProfileService.ApplyRun(fail.Summary);
            Assert.IsTrue(ReferenceEquals(dup, ok) || dup.Outcome == ProfileCommitOutcome.Duplicate);
            Assert.AreEqual(achSonra, ProfileService.Current.Achievements.Count, "ikinci kez açılmaz");
            Assert.AreEqual(killsOnce + 2, Disk().TotalKills);
            Debug.Log("[DD-TEST] wp023.A2=OK earned=" + ok.EarnedDefinitions.Count + " opened=" + ok.OpenedContent.Count);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }
    }
}
