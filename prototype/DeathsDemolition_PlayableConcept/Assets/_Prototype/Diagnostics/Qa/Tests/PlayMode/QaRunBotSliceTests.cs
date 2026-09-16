using System;
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Qa;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Qa.Tests.PlayMode
{
    /// <summary>
    /// WP-026 · The run bot drives a real Run scene through the Input System (virtual gamepad)
    /// for one round on the shipped world: the round completes, the report files land, and the
    /// virtual device is gone afterwards. The 8-round / 3×60 s closure run is regression.sh's job.
    /// </summary>
    [Category("WP026")]
    public sealed class QaRunBotSliceTests
    {
        private const float KurulumZamanAsimi = 40f;
        private string _dir;
        private QaRunBot.Driver _bot;

        [UnitySetUp]
        public IEnumerator Kurulum()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            _dir = Path.Combine(Application.temporaryCachePath, "wp026-bot-" + DateTime.UtcNow.Ticks);
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
        [Timeout(420000)]
        public IEnumerator Bot_BirRound_SanalGamepadle_TamamlarVeRaporYazar()
        {
            int devicesBefore = InputSystem.devices.Count;
            _bot = QaRunBot.Arm(_dir, 1, 26, 300f, false);
            yield return Bekle(() => _bot == null || _bot.Finished, 330f, "Bot bir roundu 330 s içinde bitiremedi.");
            Assert.IsNotNull(_bot);
            QaRunBot.Summary s = _bot.Result;
            Debug.Log("[DD-TEST] wp026.bot rounds=" + s.roundsCompleted + " kills=" + s.totalKills + " deaths=" + s.deaths + " spawns=" + s.spawnCount +
                      " spawnHash=" + s.spawnSequenceHash + " stuck=" + s.stuckRecoveries + " reloads=" + s.reloads + " note=" + s.note);
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "bot-summary.json")), "bot-summary.json yok");
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "qa-report.json")), "qa-report.json yok");
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "events.txt")), "events.txt yok");
            Assert.Greater(s.spawnCount, 0, "hiç spawn görülmedi");
            Assert.Greater(s.totalKills, 0, "bot hiç öldürmedi");
            Assert.AreEqual(1, s.roundsCompleted, s.note);
            Assert.IsTrue(s.pass, s.note);
            Assert.AreEqual(devicesBefore, InputSystem.devices.Count, "sanal gamepad koşu sonunda kaldırılmalı");
            QaReport report = QaReport.FromJson(File.ReadAllText(Path.Combine(_dir, "qa-report.json")));
            Assert.IsTrue(report.pass, report.Summarize());
            Directory.Delete(_dir, true);
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t0 > saniye)
                {
                    Assert.Fail(aciklama);
                }

                yield return null;
            }
        }
    }
}
