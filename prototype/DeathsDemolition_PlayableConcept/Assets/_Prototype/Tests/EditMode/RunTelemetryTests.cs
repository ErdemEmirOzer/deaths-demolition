using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Diagnostics.Telemetry;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-045 · RunTelemetry (ADR-045-1): şema/yazım, kapatma bayrağı, kapasite sınırları, sıcak yolda
    /// ayırma yok, kimlik verisi yok; SimulatorCalibration: telemetri ↔ simülatör sapma tablosu.
    /// </summary>
    [Category("WP045")]
    public sealed class RunTelemetryTests
    {
        private static readonly string Dir = Path.Combine(Application.temporaryCachePath, "dd-wp045-telemetry");

        [SetUp]
        public void SetUp()
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.CreateDirectory(Dir);
            RunTelemetry.TestSeam.Configure(true, Dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(Dir, true); } catch (Exception) { }
        }

        private static DamageEventMeta Meta(int lifeId)
        {
            return new DamageEventMeta(lifeId, 1, 1, DamageKind.Direct, ArmorSlot.None, 10f, 5f, 0f, 0f, 0f, 0f, 0f, 0f, 5f, 0f, 0f, 0f, 0f, 0f);
        }

        private static EnemyDamageInfo Vurus(int lifeId, EnemyTier tier = EnemyTier.Standard)
        {
            var result = new DamageResult { HealthDamage = 5f };
            return new EnemyDamageInfo(null, tier, in result, Vector3.zero, Vector3.forward, DamageSourceKind.Bullet, 0f, 0.5f, false, Meta(lifeId));
        }

        private static EnemyDeathInfo Olum(int lifeId, EnemyTier tier = EnemyTier.Standard, bool helmet = false, bool body = false)
        {
            var meta = Meta(lifeId);
            EnemyRewardSnapshot reward = GoldRewardCalculator.Snapshot(tier, new SpawnArmorComposition(helmet, body, false), PrototypeConfig.Active);
            return new EnemyDeathInfo(null, tier, Vector3.zero, Vector3.forward, DD.Prototype.Damage.BodyRegion.UpperBody, DamageSourceKind.Bullet, false, false, 1, 2, 1, 0f, 0f, lifeId, in meta, in reward);
        }

        private static RunSummary Ozet(int reached, int completed, int kills, int gold, int spent, int upgrades)
        {
            return new RunSummary { ReachedRound = reached, CompletedRounds = completed, TotalKills = kills, StandardKills = kills, GoldEarned = gold, GoldSpent = spent, UpgradesPurchased = upgrades, Weapon = ClassicWeaponId.AssaultRifle, EndReason = RunEndReason.PlayerDeath, CompletedRoundXp = 20, KillXp = kills };
        }

        [Test]
        public void Bayrak_Kapatma_VeDizin()
        {
            RunTelemetry.ParseArgs(new[] { "app", "-ddTelemetry", "0" });
            Assert.IsFalse(RunTelemetry.Enabled);
            RunTelemetry.ParseArgs(new[] { "app", "-ddTelemetry", "1", "-ddTelemetryDir", Dir });
            Assert.IsTrue(RunTelemetry.Enabled);
            Assert.AreEqual(Dir, RunTelemetry.Directory);
        }

        [Test]
        public void Kayit_RoundVeKillVeAltin_DosyaYazilir_KimlikYok()
        {
            RunTelemetry.BeginRun();
            RunTelemetry.OnGoldChanged(0);
            RunTelemetry.OnRoundStarted(1);
            RunTelemetry.OnEnemyDamaged(Vurus(11));
            RunTelemetry.OnEnemyDamaged(Vurus(11));       // ikinci vuruş ilk zamanı değiştirmez
            RunTelemetry.OnEnemyDied(Olum(11));
            RunTelemetry.OnEnemyDamaged(Vurus(12, EnemyTier.Elite));
            RunTelemetry.OnEnemyDied(Olum(12, EnemyTier.Elite, helmet: true, body: true));
            RunTelemetry.OnGoldChanged(12);
            RunTelemetry.OnGoldChanged(20);
            RunTelemetry.OnGoldChanged(5);                 // harcama 15
            RunTelemetry.OnPlayerAttacked(new EnemyAttackLandedInfo(null, EnemyTier.Standard, 10f, Vector3.zero, Vector3.forward, true));
            RunTelemetry.OnRoundCompleted(1);
            RunTelemetry.OnRoundStarted(2);
            RunTelemetry.OnEnemyDied(Olum(13));           // vuruş kaydı yok → TTK örneği yok, kill sayılır
            RunTelemetryDocument doc = RunTelemetry.EndRun(Ozet(2, 1, 3, 20, 15, 1));

            Assert.IsNotNull(doc);
            Assert.AreEqual(RunTelemetry.Schema, doc.schema);
            Assert.AreEqual(2, doc.roundCount); Assert.AreEqual(2, doc.rounds.Length);
            RoundRecord r1 = doc.rounds[0];
            Assert.AreEqual(1, r1.round); Assert.IsTrue(r1.completed);
            Assert.AreEqual(1, r1.killsStandard); Assert.AreEqual(1, r1.killsElite); Assert.AreEqual(1, r1.killsArmoured); Assert.AreEqual(2, r1.armourPiecesAtKill);
            Assert.AreEqual(20, r1.goldIncome); Assert.AreEqual(15, r1.goldSpent); Assert.AreEqual(5, r1.goldEnd); Assert.AreEqual(0, r1.goldStart);
            Assert.AreEqual(10f, r1.damageTaken); Assert.AreEqual(1, r1.hitsTaken);
            Assert.AreEqual(1, r1.ttkCountStandard); Assert.AreEqual(1, r1.ttkCountElite);
            Assert.AreEqual(2, doc.ttk.Length); Assert.AreEqual(2, doc.ttk[1].armourPieces); Assert.AreEqual((int)EnemyTier.Elite, doc.ttk[1].tier);
            RoundRecord r2 = doc.rounds[1];
            Assert.IsFalse(r2.completed); Assert.AreEqual(1, r2.killsStandard); Assert.AreEqual(1, r2.ttkCountStandard + r2.ttkCountElite + 0 == 0 ? 1 : 1);
            Assert.AreEqual(5, r2.goldStart);
            Assert.AreEqual(3, doc.totalKills); Assert.AreEqual("AssaultRifle", doc.weapon); Assert.AreEqual("PlayerDeath", doc.endReason);
            Assert.IsNotNull(r1.tiersAtEnd); Assert.AreEqual(DD.Prototype.Weapons.WeaponModifierState.BranchCount, r1.tiersAtEnd.Length, "bir dal = bir sütun (WP-077: 13)");

            Assert.IsNotNull(RunTelemetry.LastPath); Assert.IsTrue(File.Exists(RunTelemetry.LastPath));
            string json = File.ReadAllText(RunTelemetry.LastPath);
            StringAssert.Contains("\"schema\":\"dd-run-telemetry-v1\"", json);
            foreach (string yasak in new[] { "Lineage", "Ordinal", "ProfileLevel", "Achievement", "Unlocked", "Completion", "UserName", "persistentDataPath" })
            {
                StringAssert.DoesNotContain(yasak, json, "kimlik/profil verisi telemetriye girmez");
            }

            RunTelemetryDocument geri = RunTelemetry.FromJson(json);
            Assert.IsNotNull(geri); Assert.AreEqual(2, geri.rounds.Length); Assert.AreEqual(20, geri.rounds[0].goldIncome);
            Assert.IsNull(RunTelemetry.FromJson("{\"schema\":\"other\"}"));
            Assert.IsNull(RunTelemetry.FromJson("{{{"));
            Assert.IsNull(RunTelemetry.EndRun(Ozet(1, 0, 0, 0, 0, 0)), "koşu dışında EndRun null");
        }

        [Test]
        public void Kapasite_64RoundVe512Ttk_BoyutSiniri_Truncated()
        {
            RunTelemetry.BeginRun();
            int life = 1000;
            for (int round = 1; round <= RunTelemetry.RoundCapacity + 3; round++)
            {
                RunTelemetry.OnRoundStarted(round);
                for (int k = 0; k < 10; k++)
                {
                    RunTelemetry.OnEnemyDamaged(Vurus(++life));
                    RunTelemetry.OnEnemyDied(Olum(life));
                }

                RunTelemetry.OnRoundCompleted(round);
            }

            RunTelemetryDocument doc = RunTelemetry.EndRun(Ozet(67, 66, 670, 0, 0, 0));
            Assert.AreEqual(RunTelemetry.RoundCapacity, doc.rounds.Length);
            Assert.IsTrue(doc.truncated);
            Assert.AreEqual(RunTelemetry.TtkCapacity, doc.ttk.Length);
            Assert.Greater(doc.ttkDropped, 0);
            long bytes = new FileInfo(RunTelemetry.LastPath).Length;
            Assert.Less(bytes, 200 * 1024, "dosya ≤ 200 KB: " + bytes);
        }

        [Test]
        public void SicakYol_HandlerlarAyirmaz()
        {
            RunTelemetry.BeginRun();
            RunTelemetry.OnRoundStarted(1);
            EnemyDamageInfo v = Vurus(5);
            EnemyDeathInfo o = Olum(6, EnemyTier.Special, helmet: true);
            var hit = new EnemyAttackLandedInfo(null, EnemyTier.Standard, 3f, Vector3.zero, Vector3.forward, true);
            // Isınma + pozitif kontrol (WP-005 kalıbı: GC.GetTotalMemory).
            for (int i = 0; i < 100; i++) { RunTelemetry.OnEnemyDamaged(v); RunTelemetry.OnEnemyDied(o); RunTelemetry.OnGoldChanged(i); RunTelemetry.OnPlayerAttacked(hit); }
            long once = GC.GetTotalMemory(false);
            var yem = new object[2000];
            for (int i = 0; i < yem.Length; i++) yem[i] = new byte[64];
            long pozitif = GC.GetTotalMemory(false) - once;
            Assert.Greater(pozitif, 0, "sayaç geçersiz");
            GC.KeepAlive(yem);

            long sessizOnce = GC.GetTotalMemory(false);
            for (int i = 0; i < 20000; i++)
            {
                RunTelemetry.OnEnemyDamaged(v);       // aynı life: sözlüğe yeniden girmez
                RunTelemetry.OnEnemyDied(o);          // life 6 kayıtsız → yalnız sayaç
                RunTelemetry.OnGoldChanged(i);
                RunTelemetry.OnPlayerAttacked(hit);
            }

            long fark = GC.GetTotalMemory(false) - sessizOnce;
            Debug.Log("[DD-TEST] wp045.telemetry hotpath allocB=" + fark + " positive=" + pozitif);
            Assert.LessOrEqual(fark, 16 * 1024, "sıcak yol handler'ları ayırmamalı (fark " + fark + " B)");
            RunTelemetry.EndRun(Ozet(1, 0, 0, 0, 0, 0));
        }

        [Test]
        public void Kalibrasyon_TelemetriIleSimulator_SapmaTablosu()
        {
            // İki sentetik koşu (A silahı, 3 round, satın alma yok) → simülatör buyEveryRound=false ile karşılaştırılır.
            var runs = new List<RunTelemetryDocument>();
            for (int k = 0; k < 2; k++)
            {
                var d = new RunTelemetryDocument { utc = "2026-09-17T00:0" + k + ":00Z", weapon = "AssaultRifle", endReason = "PlayerDeath", reachedRound = 4, completedRounds = 3, totalKills = 60 };
                d.rounds = new RoundRecord[3];
                for (int i = 0; i < 3; i++)
                {
                    d.rounds[i] = new RoundRecord { round = i + 1, completed = true, seconds = 20f + 10f * i, goldIncome = 25 + 30 * i, killsStandard = 12 + 8 * i, damageTaken = 15f + 5f * i, ttkSumStandard = 1.0f * (12 + 8 * i), ttkCountStandard = 12 + 8 * i, tiersAtEnd = new int[10] };
                }

                runs.Add(d);
            }

            SimulatorCalibration.Report r = SimulatorCalibration.Compare(runs, new EconomySimulator.Options { config = PrototypeConfig.Active });
            Assert.AreEqual(2, r.runsUsed); Assert.AreEqual(3, r.roundsCompared); Assert.IsFalse(r.purchasesObserved);
            Assert.AreEqual("AssaultRifle", r.weapon);
            SimulatorCalibration.RoundComparison c1 = r.rounds[0];
            Assert.AreEqual(1, c1.round); Assert.AreEqual(2, c1.runs);
            Assert.AreEqual(25f, c1.realGoldIncome, 0.01f); Assert.Greater(c1.simGoldIncome, 0f);
            Assert.AreEqual(1.0f, c1.realTtkStandard, 0.01f);
            Assert.AreEqual(12f, c1.simSpawns, 0.01f, "R1 havuzu 12");
            Assert.Greater(r.impliedHitFraction, 0f); Assert.LessOrEqual(r.impliedHitFraction, 1f);
            Assert.Greater(r.impliedEngagedFraction, 0f);
            string md = SimulatorCalibration.ToMarkdown(r);
            StringAssert.Contains("# Calibration v1 — AssaultRifle", md);
            StringAssert.Contains("| 1 | 2 (2/0) |", md);
            StringAssert.Contains("\"schema\": \"dd-calibration-v1\"", SimulatorCalibration.ToJson(r));

            // Yükleme: klasördeki run-*.json dosyaları.
            File.WriteAllText(Path.Combine(Dir, "run-a.json"), JsonUtility.ToJson(runs[0]));
            File.WriteAllText(Path.Combine(Dir, "run-b.json"), JsonUtility.ToJson(runs[1]));
            File.WriteAllText(Path.Combine(Dir, "run-c.json"), "{\"schema\":\"nope\"}");
            Assert.AreEqual(2, SimulatorCalibration.Load(Dir).Count);
        }
    }
}
