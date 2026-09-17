using System;
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Diagnostics.Telemetry;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Qa;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-045 · in-Editor QA bot runs (WP-026 `QaRunBot`, no Player build — HIZLI HAT §6) that
    /// produce real `dd-run-telemetry-v1` documents for the calibration step. The telemetry
    /// folder is `DD_WP045_BOT_DIR` when set (the batch chain hands the same folder to
    /// SimulatorCalibration), else temporaryCachePath.
    /// </summary>
    [Category("WP045")]
    public sealed class RunTelemetryBotTests
    {
        private const float KurulumZamanAsimi = 40f;
        private QaRunBot.Driver _bot;
        private string _telemetryDir;

        private static string TelemetryDir()
        {
            string env = Environment.GetEnvironmentVariable("DD_WP045_BOT_DIR");
            return string.IsNullOrEmpty(env) ? Path.Combine(Application.temporaryCachePath, "dd-wp045-bot") : env;
        }

        [UnitySetUp]
        public IEnumerator Kurulum()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _telemetryDir = TelemetryDir();
            Directory.CreateDirectory(_telemetryDir);
            RunTelemetry.TestSeam.Configure(true, _telemetryDir);
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null &&
                                     RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
        }

        [UnityTearDown]
        public IEnumerator Temizlik()
        {
            if (_bot != null)
            {
                UnityEngine.Object.Destroy(_bot.gameObject);
                _bot = null;
                yield return null;
            }

            if (RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Dead)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 4f && SceneManager.GetActiveScene().name != PrototypeScenes.Results) yield return null;
                yield return null;
            }

            RunFlowClock.ClearHolds();
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator B1_Bot3Round_Seed26_TelemetriBelgesi()
        {
            yield return BotKosusu(26);
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator B2_Bot3Round_Seed27_TelemetriBelgesi()
        {
            yield return BotKosusu(27);
        }

        private IEnumerator BotKosusu(int seed)
        {
            Assert.IsTrue(RunTelemetry.InRun, "Run sahnesi RunStarted ile kaydı başlattı");
            string botDir = Path.Combine(Application.temporaryCachePath, "dd-wp045-botout-" + seed + "-" + DateTime.UtcNow.Ticks);
            _bot = QaRunBot.Arm(botDir, 3, seed, 300f, false);
            yield return Bekle(() => _bot == null || _bot.Finished, 800f, "Bot 3 roundu 800 s içinde bitiremedi.");
            Assert.IsNotNull(_bot);
            QaRunBot.Summary s = _bot.Result;
            Assert.IsNotNull(s, "bot özeti");
            Debug.Log("[DD-TEST] wp045.bot seed=" + seed + " roundsCompleted=" + s.roundsCompleted + " kills=" + s.totalKills + " deaths=" + s.deaths);

            // Koşuyu bitir (RunEnded → telemetri belgesi yazılır).
            if (RunDirector.Instance != null && RunDirector.Instance.Phase != RunPhase.Dead)
            {
                PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp045-end");
                yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Dead, 30f, "Dead fazı gelmedi.");
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
            }

            RunTelemetryDocument doc = RunTelemetry.LastDocument;
            Assert.IsNotNull(doc, "telemetri belgesi");
            Assert.GreaterOrEqual(doc.roundCount, Math.Max(1, s.roundsCompleted), "round kayıtları");
            int completed = 0, kills = 0;
            foreach (RoundRecord r in doc.rounds) { if (r.completed) completed++; kills += r.killsStandard + r.killsSpecial + r.killsElite + r.killsUltra; }
            Assert.AreEqual(s.roundsCompleted, completed, "tamamlanan round sayısı bot özetiyle aynı");
            Assert.AreEqual(s.totalKills, kills, "kill sayısı bot özetiyle aynı (aynı EnemyDied olayı)");
            Assert.IsTrue(File.Exists(RunTelemetry.LastPath) && RunTelemetry.LastPath.StartsWith(_telemetryDir), "belge kalibrasyon klasöründe");
            Debug.Log("[DD-TEST] wp045.telemetry seed=" + seed + " path=" + RunTelemetry.LastPath + " rounds=" + doc.roundCount + " ttk=" + doc.ttk.Length);
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t0 > saniye) Assert.Fail(aciklama);
                yield return null;
            }
        }
    }
}
