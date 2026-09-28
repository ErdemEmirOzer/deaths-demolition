using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
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
            int commitsBefore = DD.Prototype.Profile.ProfileRunCommitter.Commits;
            int progressLines = 0;
            Application.LogCallback onLog = (m, st, t) => { if (m != null && m.StartsWith("[Progress]", StringComparison.Ordinal)) progressLines++; };
            Application.logMessageReceived += onLog;
            _bot = QaRunBot.Arm(_dir, 1, 26, 300f, false);
            yield return Bekle(() => _bot == null || _bot.Finished, 330f, "Bot bir roundu 330 s içinde bitiremedi.");
            Application.logMessageReceived -= onLog;
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
            // WP-076 (R1 28.09): the bot closes the run against the isolated profile → one commit, one [Progress] line
            Assert.AreEqual(commitsBefore + 1, DD.Prototype.Profile.ProfileRunCommitter.Commits, "bot koşuyu kapatıp profil commit'i yapmadı");
            Assert.AreEqual(1, progressLines, "[Progress] satırı yok");
            Assert.AreEqual(s.totalKills, s.spawnCount, "tamamlanan round: spawn = kill = havuz (R1 28.09: bot, kurulmadan önceki ilk refill tikini saymıyordu)");
            Assert.IsTrue(s.pass, s.note);
            Assert.AreEqual(devicesBefore, InputSystem.devices.Count, "sanal gamepad koşu sonunda kaldırılmalı");
            QaReport report = QaReport.FromJson(File.ReadAllText(Path.Combine(_dir, "qa-report.json")));
            Assert.IsTrue(report.pass, report.Summarize());
            Directory.Delete(_dir, true);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Bot_OlumKaniti_CallbackSirasiSonrasiTekKayitVeBaglamYazar()
        {
            _bot = QaRunBot.Arm(_dir, 12, 61, 300f, false);
            yield return Bekle(() => _bot != null && _bot.Running, KurulumZamanAsimi, "Bot run-ready olmadı.");
            PlayerRig rig = PlayerRegistry.Primary;
            Assert.IsNotNull(rig);
            Assert.IsNotNull(rig.Health);
            rig.Health.ApplyDamage(rig.Health.Max * 4f, rig.transform.position - rig.transform.forward, "Zombie.Ultra.Overhead");

            yield return Bekle(() => _bot == null || _bot.Finished, 15f, "Ölüm sonrası bot kanıtı yazılmadı.");
            Assert.IsNotNull(_bot);
            string jsonPath = Path.Combine(_dir, "bot-summary.json");
            string eventsPath = Path.Combine(_dir, "events.txt");
            Assert.IsTrue(File.Exists(jsonPath));
            Assert.IsTrue(File.Exists(eventsPath));
            QaRunBot.Summary persisted = JsonUtility.FromJson<QaRunBot.Summary>(File.ReadAllText(jsonPath));
            string eventsText = File.ReadAllText(eventsPath);

            Assert.AreEqual(1, persisted.deaths, "RunEnded ve AnyPlayerDied ölümü iki kez saymamalı.");
            Assert.GreaterOrEqual(persisted.deathRound, 1);
            Assert.GreaterOrEqual(persisted.deathTimeSeconds, 0f);
            Assert.AreEqual("Zombie.Ultra.Overhead", persisted.deathKilledBy);
            Assert.AreEqual("Zombie.Ultra.Overhead", persisted.fatalSource);
            Assert.Greater(persisted.fatalAppliedAmount, 0f);
            Assert.Greater(persisted.fatalHealthBefore, persisted.fatalHealthAfter);
            Assert.LessOrEqual(persisted.fatalHealthAfter, 0f);
            StringAssert.Contains("damage #", eventsText);
            StringAssert.Contains("player-died", eventsText);
            Assert.IsFalse(persisted.pass, "Gerçek ölüm exact soak için PASS olamaz.");
            Directory.Delete(_dir, true);
        }

        [Test]
        public void ExactGate_IstenenRoundSayisiniEksiksizZorunluKilar()
        {
            Assert.IsTrue(QaRunBot.ExactRunPassed(12, 12, 0, 0));
            Assert.IsFalse(QaRunBot.ExactRunPassed(4, 12, 0, 0), "Eski 4/12 kabulü kapanmalı.");
            Assert.IsFalse(QaRunBot.ExactRunPassed(12, 12, 5, 0));
            Assert.IsFalse(QaRunBot.ExactRunPassed(12, 12, 0, 9));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Bot_ArtefaktDiziniYazilamazsa_NonzeroCikisDurumuUretir()
        {
            File.WriteAllText(_dir, "directory blocker");
            LogAssert.Expect(LogType.Error, new Regex(@"^\[QaRunBot\] artifact directory unavailable: IOException:"));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[QaRunBot\] write failed: System\.IO\.IOException:"));
            _bot = QaRunBot.Arm(_dir, 1, 26, 300f, false);
            yield return Bekle(() => _bot != null && _bot.Finished, 10f, "Yazma hatası fail-closed bitmedi.");

            Assert.IsTrue(_bot.ArtifactWriteFailed);
            Assert.AreEqual(4, _bot.ExitCode);
            Assert.IsFalse(_bot.Result.pass);
            File.Delete(_dir);
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
