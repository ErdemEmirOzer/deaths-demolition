using System.Collections;
using System.IO;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.Telemetry;
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
    /// <summary>WP-045 · gerçek koşu → `dd-run-telemetry-v1` dosyası (izole dizin), round/kill/altın/hasar alanları olaylardan dolar.</summary>
    [TestFixture]
    [Category("WP045")]
    public sealed class RunTelemetrySliceTests
    {
        private static readonly string Dir = Path.Combine(Application.temporaryCachePath, "dd-wp045-playmode-" + System.Diagnostics.Process.GetCurrentProcess().Id);
        private float _eskiTimeScale;

        [SetUp]
        public void SetUp()
        {
            _eskiTimeScale = Time.timeScale;
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            RunTelemetry.TestSeam.Configure(true, Dir);
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            Time.timeScale = _eskiTimeScale;
            LogAssert.ignoreFailingMessages = false;
            if (EnemySpawnDirector.Instance != null)
            {
                typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(EnemySpawnDirector.Instance, true);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            try { Directory.Delete(Dir, true); } catch (System.Exception) { }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator T1_GercekKosu_TelemetriDosyasi_AlanlarOlaylardan()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path, "Profil yolu izole değil.");
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return Bekle(() => EnemySpawnDirector.Instance != null && GoldService.Instance != null, 25f, "director yok");
            typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(EnemySpawnDirector.Instance, false);
            EnemySpawnDirector.Instance.ClearAllEnemies();
            EnemyFactory.ClearAll();
            GoldService.Instance.ClearOrbs(false);
            yield return null;
            Assert.IsTrue(RunTelemetry.InRun, "RunStarted ile kayıt başlar");

            Vector3 merkez = PlayerRegistry.Primary.transform.position;
            for (int i = 0; i < 3; i++)
            {
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, RunDirector.Instance.Round, merkez + new Vector3(14f + i * 1.2f, 0f, 3f), Quaternion.identity);
                Assert.IsNotNull(z);
                z.Kill();
                yield return null;
            }

            yield return null;
            RoundRecord anlik = RunTelemetry.CurrentRound;
            Assert.AreEqual(1, anlik.round);
            Assert.AreEqual(3, anlik.killsStandard, "EnemyDied olayından kill sayımı");

            PlayerRig rig = PlayerRegistry.Primary;
            rig.Health.DiagnosisInvulnerable = false;
            rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp045-death");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Dead, FazZamanAsimi, "Dead fazı gelmedi.");
            yield return SahneyiBekle(PrototypeScenes.Results);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1f) yield return null;

            RunTelemetryDocument doc = RunTelemetry.LastDocument;
            Assert.IsNotNull(doc, "RunEnded → belge");
            Assert.IsFalse(RunTelemetry.InRun);
            Assert.AreEqual(RunTelemetry.Schema, doc.schema);
            Assert.AreEqual(1, doc.roundCount);
            Assert.AreEqual(3, doc.rounds[0].killsStandard);
            Assert.AreEqual(3, doc.totalKills);
            Assert.AreEqual("PlayerDeath", doc.endReason);
            Assert.AreEqual(RunEvents.LastRunSummary.GoldEarned, doc.goldEarned);
            Assert.AreEqual(RunEvents.LastRunSummary.Weapon.ToString(), doc.weapon);
            Assert.IsFalse(doc.rounds[0].completed, "ölümle biten round tamamlanmadı");
            Assert.Greater(doc.runSeconds, 0f);
            Assert.IsNotNull(RunTelemetry.LastPath); Assert.IsTrue(File.Exists(RunTelemetry.LastPath));
            Assert.IsTrue(RunTelemetry.LastPath.StartsWith(Dir), "izole dizin");
            long bytes = new FileInfo(RunTelemetry.LastPath).Length;
            Assert.Less(bytes, 200 * 1024);
            Debug.Log("[DD-TEST] wp045.T1=OK kills=" + doc.totalKills + " bytes=" + bytes + " path=" + RunTelemetry.LastPath);

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }
    }
}
