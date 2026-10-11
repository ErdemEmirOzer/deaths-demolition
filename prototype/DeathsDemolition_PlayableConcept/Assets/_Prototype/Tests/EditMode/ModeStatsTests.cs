using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 / WP-102 · mod ayrımı: koşu sonu ModeStats (Normal / Zor / Demolition), eski HighestRound = her modun en
    /// iyisi, Kül/XP mod çarpanı (config; Normal bayt-eşit), ReachRound her modda sayılır, ReachRoundInMode başarımları
    /// (Zor R10/R20, Demolition R3/R5/R8/R10), sonuç ekranının mod "yeni en iyi" verisi. İzole profil yolu.
    /// </summary>
    [TestFixture]
    public sealed class ModeStatsTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp104-mode", "prototype-profile.json");

        private static readonly string RealPath = Path.Combine(Application.persistentDataPath, ProfileStore.FileName);
        private string _realHashBefore;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
            Directory.CreateDirectory(Path.GetDirectoryName(TestPath));
            ProfileStore.SetPathOverride(TestPath);
            Bosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Bosalt();
            ProfileStore.SetPathOverride(null);
            try { Directory.Delete(Path.GetDirectoryName(TestPath), true); } catch (Exception) { }
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path, "override kayboldu");
            ProfileStore.SaveStageHook = null;
            ProfileService.ResetProfile();
        }

        private static void Bosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        private static string HashOf(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static ProgressionConfigSection Prog => PrototypeConfig.Active.Progression;

        private static RunSummary Ozet(int reached, int xp, int mode, int kills = 0, int nodes = 0)
        {
            return new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = reached, CompletedRounds = Math.Max(0, reached - 1), CompletedRoundXp = xp,
                TotalKills = kills, StandardKills = kills, TotalXpEarned = xp,
                Weapon = ClassicWeaponId.AssaultRifle, Mode = mode, TreeNodesOwned = nodes, TreeTier3Owned = nodes / 3, SynergiesOwned = 1,
                EndReason = RunEndReason.PlayerDeath
            };
        }

        private static int RarityKul(RunRewardReport r)
        {
            int k = 0;
            foreach (AchievementDefinition d in r.EarnedDefinitions) k += Prog.AshForRarity((int)d.Rarity);
            return k;
        }

        [Test]
        public void KosuSonu_ModAyrimi_EskiHighestRoundHerModunEnIyisi()
        {
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(7, 100, TreeMode.Normal, kills: 50, nodes: 9)).Applied);
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(9, 100, TreeMode.Hard, kills: 30, nodes: 12)).Applied);
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(4, 100, TreeMode.Demolition, kills: 80, nodes: 6)).Applied);
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(5, 100, TreeMode.Normal, kills: 70, nodes: 4)).Applied);

            ProfileData p = ProfileService.Current;
            ModeStatsEntry n = p.ModeStatsFor(TreeMode.Normal), h = p.ModeStatsFor(TreeMode.Hard), d = p.ModeStatsFor(TreeMode.Demolition);
            Assert.AreEqual(7, n.HighestRound); Assert.AreEqual(2, n.Runs); Assert.AreEqual(120, n.TotalKills); Assert.AreEqual(70, n.BestKills); Assert.AreEqual(9, n.BestNodes);
            Assert.AreEqual(9, h.HighestRound); Assert.AreEqual(1, h.Runs); Assert.AreEqual(30, h.TotalKills); Assert.AreEqual(30, h.BestKills); Assert.AreEqual(12, h.BestNodes);
            Assert.AreEqual(4, d.HighestRound); Assert.AreEqual(1, d.Runs); Assert.AreEqual(80, d.TotalKills); Assert.AreEqual(80, d.BestKills); Assert.AreEqual(6, d.BestNodes);
            Assert.AreEqual(9, p.HighestRound, "eski HighestRound = her modun en iyisi");
            Assert.AreEqual(4, p.RunsPlayed);
            Assert.AreEqual(230, p.TotalKills, "50 + 30 + 80 + 70");

            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out ProfileData disk));
            Assert.AreEqual(JsonUtility.ToJson(p), JsonUtility.ToJson(disk), "canlı = disk");
        }

        [Test]
        public void Girdi_ModVarsayilanNormal_BilinmeyenModNormal_AgacSayaclariKirpilir()
        {
            var bos = new RunRewardInput(new RunSummary { ReachedRound = 3 });
            Assert.AreEqual(TreeMode.Normal, bos.Mode);
            Assert.AreEqual(0, bos.TreeNodesOwned);
            var bilinmeyen = new RunRewardInput(new RunSummary { ReachedRound = 3, Mode = 7, TreeNodesOwned = -4, TreeTier3Owned = 9, SynergiesOwned = -1 });
            Assert.AreEqual(TreeMode.Normal, bilinmeyen.Mode);
            Assert.AreEqual(0, bilinmeyen.TreeNodesOwned);
            Assert.AreEqual(0, bilinmeyen.TreeTier3Owned, "kademe III sayısı sahip olunanı aşamaz");
            Assert.AreEqual(0, bilinmeyen.SynergiesOwned);
            var zor = new RunRewardInput(new RunSummary { ReachedRound = 3, Mode = TreeMode.Hard, TreeNodesOwned = 10, TreeTier3Owned = 3, SynergiesOwned = 2 });
            Assert.AreEqual(TreeMode.Hard, zor.Mode);
            Assert.AreEqual(10, zor.TreeNodesOwned); Assert.AreEqual(3, zor.TreeTier3Owned); Assert.AreEqual(2, zor.SynergiesOwned);
        }

        [Test]
        public void Carpan_ConfigtenGelir_Formul()
        {
            Assert.AreEqual(1f, Prog.AshModeMultiplier(TreeMode.Normal));
            Assert.AreEqual(1.25f, Prog.AshModeMultiplier(TreeMode.Hard));
            Assert.AreEqual(1.6f, Prog.AshModeMultiplier(TreeMode.Demolition));
            Assert.AreEqual(1f, Prog.AshModeMultiplier(9), "bilinmeyen mod ×1");
            Assert.AreEqual(1f, new ProgressionConfigSection { AshModeMultipliers = null }.AshModeMultiplier(1), "eksik dizi ×1");

            Assert.AreEqual(10, ProfileProgression.RunXpAsh(300, 30, 1f));
            Assert.AreEqual(12, ProfileProgression.RunXpAsh(300, 30, 1.25f), "floor(375 / 30)");
            Assert.AreEqual(16, ProfileProgression.RunXpAsh(300, 30, 1.6f), "480 / 30 tam: float 1.6f tabanı kaydırmaz");
            Assert.AreEqual(7, ProfileProgression.RunXpAsh(149, 30, 1.6f), "floor(238,4 / 30)");
            Assert.AreEqual(29, ProfileProgression.RunXpAsh(899, 30, 1f), "Normal = eski tamsayı bölmesi");
            Assert.AreEqual((long)int.MaxValue * 16 / 10, ProfileProgression.RunXpAsh(int.MaxValue, 1, 1.6f), "taşma yok");
        }

        [Test]
        public void KosuKulu_ModCarpaniyla_KilometreTasiCarpilmaz_NormalBaytEsit()
        {
            int perAsh = Math.Max(1, Prog.XpPerAsh);
            RunRewardReport normal = ProfileService.ApplyRun(Ozet(3, 600, TreeMode.Normal));
            Assert.AreEqual(600 / perAsh + RarityKul(normal), normal.AshEarned, "Normal: eski formül aynen");

            RunRewardReport zor = ProfileService.ApplyRun(Ozet(3, 600, TreeMode.Hard));
            Assert.AreEqual((int)ProfileProgression.RunXpAsh(600, perAsh, 1.25f) + RarityKul(zor), zor.AshEarned);

            RunRewardReport demo = ProfileService.ApplyRun(Ozet(10, 600, TreeMode.Demolition));
            Assert.AreEqual((int)ProfileProgression.RunXpAsh(600, perAsh, 1.6f) + Prog.AshFirstRound10PerWeapon + RarityKul(demo), demo.AshEarned,
                "çarpan yalnız XP Kül'üne; ilk R10 kilometre taşı aynen");
            Assert.AreEqual(600 * 3, ProfileService.Current.TotalXpEarned, "profil XP'si çarpılmaz");
        }

        [Test]
        public void ModBasarimlari_ReachRoundHerModda_ModKurallariYalnizKendiModunda()
        {
            RunRewardReport zor = ProfileService.ApplyRun(Ozet(10, 100, TreeMode.Hard));
            ProfileData p = ProfileService.Current;
            Assert.IsTrue(p.Achievements.Contains("hard_round_10"), "Zor R10");
            Assert.IsTrue(p.Achievements.Contains("reach_round_10"), "ReachRound Zor'da da sayılır");
            Assert.IsFalse(p.Achievements.Contains("hard_round_20"));
            Assert.IsFalse(p.Achievements.Contains("demolition_round_3"), "başka mod sayılmaz");
            Assert.AreEqual(0, zor.ModeHighestRoundBefore);

            RunRewardReport demo = ProfileService.ApplyRun(Ozet(5, 100, TreeMode.Demolition));
            p = ProfileService.Current;
            Assert.IsTrue(p.Achievements.Contains("demolition_round_3"));
            Assert.IsTrue(p.Achievements.Contains("demolition_round_5"));
            Assert.IsFalse(p.Achievements.Contains("demolition_round_8"));
            Assert.AreEqual(0, demo.ModeHighestRoundBefore);

            RunRewardReport normal = ProfileService.ApplyRun(Ozet(12, 100, TreeMode.Normal));
            Assert.IsFalse(ProfileService.Current.Achievements.Contains("hard_round_20"), "Normal R12 Zor başarımı vermez");
            Assert.IsTrue(normal.Applied);

            RunRewardReport zor2 = ProfileService.ApplyRun(Ozet(8, 100, TreeMode.Hard));
            Assert.AreEqual(10, zor2.ModeHighestRoundBefore, "modun önceki en iyisi");
            RunResultsData sonuc = RunResultsData.From(zor2, ProfileService.Current);
            Assert.AreEqual(TreeMode.Hard, sonuc.Mode);
            Assert.IsFalse(sonuc.IsNewModeBest, "R8 < Zor en iyisi R10");
            RunResultsData sonucDemo = RunResultsData.From(demo, ProfileService.Current);
            Assert.IsTrue(sonucDemo.IsNewModeBest, "ilk Demolition koşusu modun yeni en iyisi");
        }

        [Test]
        public void Katalog_ModBasarimlari_VeAyristirma()
        {
            var beklenen = new Dictionary<string, (int mode, int threshold)>
            {
                { "hard_round_10", (TreeMode.Hard, 10) }, { "hard_round_20", (TreeMode.Hard, 20) },
                { "demolition_round_3", (TreeMode.Demolition, 3) }, { "demolition_round_5", (TreeMode.Demolition, 5) },
                { "demolition_round_8", (TreeMode.Demolition, 8) }, { "demolition_round_10", (TreeMode.Demolition, 10) }
            };
            foreach (KeyValuePair<string, (int mode, int threshold)> e in beklenen)
            {
                AchievementDefinition d = AchievementCatalog.Find(e.Key);
                Assert.IsNotNull(d, e.Key);
                Assert.AreEqual(AchievementCondition.ReachRoundInMode, d.Condition, e.Key);
                Assert.AreEqual(e.Value.mode, d.Mode, e.Key);
                Assert.AreEqual(e.Value.threshold, d.Threshold, e.Key);
                Assert.AreEqual(AchievementScope.Lifetime, d.Scope, e.Key);
                Assert.Greater(Prog.AshForRarity((int)d.Rarity), 0, e.Key + " Kül ödülü nadirlik kademesinden");
            }

            LogAssert.Expect(LogType.Warning, new Regex("unknown mode"));
            string json = "{\"Version\":1,\"Definitions\":[" +
                          "{\"Id\":\"m_none\",\"Condition\":\"ReachRoundInMode\",\"Threshold\":3}," +
                          "{\"Id\":\"m_run\",\"Condition\":\"ReachRoundInMode\",\"Mode\":\"Demolition\",\"Threshold\":3,\"Scope\":\"SingleRun\"}]}";
            List<AchievementDefinition> list = AchievementCatalog.Parse(json, out int dropped);
            Assert.AreEqual(1, dropped, "modsuz mod kuralı düşer");
            Assert.AreEqual(1, list.Count);
            var feats = new RunFeatCounters();
            var demoKosu = new RunRewardInput(new RunSummary { ReachedRound = 4, Mode = TreeMode.Demolition });
            var normalKosu = new RunRewardInput(new RunSummary { ReachedRound = 9, Mode = TreeMode.Normal });
            var bos = new ProfileData();
            Assert.IsTrue(AchievementEngine.IsSatisfied(bos, demoKosu, feats, list[0]), "tek koşu: kendi modunda");
            Assert.IsFalse(AchievementEngine.IsSatisfied(bos, normalKosu, feats, list[0]), "tek koşu: başka modda sayılmaz");
        }
    }
}
