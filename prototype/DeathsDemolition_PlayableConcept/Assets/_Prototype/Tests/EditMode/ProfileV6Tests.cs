using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B1 · profil şeması v6 (C-00 11.10 karar 2, §4.14): v5 → v6 tam ücretsiz respec (AshBalance = AshEarnedTotal,
    /// harcanan 0, MetaNodes boş, MetaRespecCount artmaz, tek seferlik iade bildirimi), WeaponMastery / AshMilestones /
    /// lineage / ordinal korunur, ModeStats Normal'e tohumlanır; göçten önce v5 dosyasının bayt-bayt <c>.v5.bak</c> yedeği
    /// (üzerine yazılmaz; yazılamazsa göç diske inmez); idempotan + exactly-once; sahte "v5 + v6 alanı" belgesi yeniden kurulur.
    /// İzole profil yolu; gerçek profile dokunulmaz.
    /// </summary>
    [TestFixture]
    public sealed class ProfileV6Tests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp104-v6", "prototype-profile.json");

        private static readonly string RealPath = Path.Combine(Application.persistentDataPath, ProfileStore.FileName);
        private static string LegacyPath => TestPath + ProfileStore.LegacyV5BackupSuffix;
        private string _realHashBefore;
        private string _realLegacyHashBefore;
        private string _realFamilyBefore;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
            _realLegacyHashBefore = HashOf(RealPath + ProfileStore.LegacyV5BackupSuffix);
            _realFamilyBefore = RealFamily();
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
            Assert.AreEqual(_realLegacyHashBefore, HashOf(RealPath + ProfileStore.LegacyV5BackupSuffix), "gerçek .v5.bak değişmemeli (yoksa oluşmamalı)");
            Assert.AreEqual(_realFamilyBefore, RealFamily(), "gerçek profil klasöründeki profil dosyaları (ad + SHA) aynı kalmalı");
        }

        /// <summary>Every real profile file (prototype-profile.json*) by name and SHA, the OS lease file excluded. Read only.</summary>
        private static string RealFamily()
        {
            string dir = Path.GetDirectoryName(RealPath);
            if (!Directory.Exists(dir)) return "MISSING_DIRECTORY";
            string[] files = Directory.GetFiles(dir, ProfileStore.FileName + "*");
            Array.Sort(files, StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (string f in files)
            {
                if (f.EndsWith(ProfileStore.LockSuffix, StringComparison.Ordinal)) continue;
                sb.Append(Path.GetFileName(f)).Append(':').Append(HashOf(f)).Append('\n');
            }

            return sb.ToString();
        }

        [SetUp]
        public void SetUp()
        {
            ProfileStore.SaveStageHook = null;
            Temizle();
            ProfileStore.SetPathOverride(TestPath);   // clears a read-only lock a previous test may have left
            Assert.AreEqual(TestPath, ProfileStore.Path, "override kayboldu");
            Bosalt();
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.SaveStageHook = null;
            Temizle();
            ProfileStore.SetPathOverride(TestPath);
            Bosalt();
        }

        private static void Temizle()
        {
            if (Directory.Exists(LegacyPath)) Directory.Delete(LegacyPath, true);
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(TestPath), Path.GetFileName(TestPath) + "*"))
            {
                if (!f.EndsWith(ProfileStore.LockSuffix, StringComparison.Ordinal)) File.Delete(f);
            }
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

        private const string Soy = "0123456789abcdef0123456789abcdef";

        /// <summary>A v5 document as the R3 builds (b46a3ca4) wrote it: meta PATH spent (420 of 600 Ash), mastery, milestones, a pin.</summary>
        private static string V5Json(int earned = 600, int spent = 420, int highest = 14, string extra = "")
        {
            return "{\"SaveVersion\":5,\"ProfileLevel\":12,\"XpIntoLevel\":100,\"TotalXpEarned\":12000,\"HighestRound\":" + highest + "," +
                   "\"RunsPlayed\":30,\"RoundsCompleted\":250,\"TotalKills\":3000,\"StandardKills\":2800,\"SpecialKills\":150," +
                   "\"EliteKills\":45,\"UltraKills\":5,\"UnattributedKills\":0,\"ArmorPiecesBroken\":300,\"BestRunXp\":900," +
                   "\"SpecialWeaponPickups\":12,\"FlawlessRounds\":4,\"Achievements\":[\"first_blood\",\"reach_round_10\"]," +
                   "\"UnlockedWeapons\":[\"ar_1\",\"smg_1\",\"ar_2\"],\"UnlockedBranches\":[\"Piercing\"],\"UnlockedContent\":[]," +
                   "\"SelectedTitle\":\"\",\"SelectedBadge\":\"\",\"AchievementsSeen\":0,\"ContentSeen\":0," +
                   "\"AshBalance\":" + (earned - Math.Min(spent, earned)) + ",\"AshEarnedTotal\":" + earned + ",\"AshSpentTotal\":" + spent + "," +
                   "\"MetaNodes\":[\"w_ar_2\",\"pa_eco_1\",\"pa_eco_2\",\"pa_eco_3\",\"pa_eco_4\"],\"MetaRespecCount\":1," +
                   "\"MetaPinnedNode\":\"w_lmg_1\",\"MetaSeen\":5,\"AshMilestones\":[\"r10:ar_1\",\"capstone\"]," +
                   "\"WeaponMastery\":[{\"Weapon\":\"ar_1\",\"Kills\":2000,\"Capstones\":2},{\"Weapon\":\"smg_1\",\"Kills\":300,\"Capstones\":0}]," +
                   extra +
                   "\"ProfileLineageId\":\"" + Soy + "\",\"LastAppliedCompletionOrdinal\":30,\"LastSavedUtc\":\"2026-10-10T20:00:00Z\"}";
        }

        private static RunSummary Ozet(int reached, int xp, int mode = TreeMode.Normal)
        {
            return new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = reached, CompletedRounds = Math.Max(0, reached - 1), CompletedRoundXp = xp, TotalXpEarned = xp,
                Weapon = ClassicWeaponId.AssaultRifle, Mode = mode, EndReason = RunEndReason.PlayerDeath
            };
        }

        [Test]
        public void Gocu_V5DosyasindanYukle_TamRespec_IadeBildirimi_KorunanAlanlar()
        {
            File.WriteAllText(TestPath, V5Json());
            ProfileData p = ProfileService.Current;

            Assert.AreEqual(6, p.SaveVersion);
            Assert.AreEqual(600, p.AshEarnedTotal, "kazanılan aynen");
            Assert.AreEqual(600, p.AshBalance, "tam iade: bakiye = kazanılan");
            Assert.AreEqual(0, p.AshSpentTotal);
            Assert.IsEmpty(p.MetaNodes, "meta düğümleri boşalır (silah sayfası emekli)");
            Assert.AreEqual(string.Empty, p.MetaPinnedNode);
            Assert.AreEqual(0, p.MetaSeen);
            Assert.AreEqual(1, p.MetaRespecCount, "göç respec sayacını artırmaz");
            Assert.AreEqual(420, p.PendingAshRefundNotice, "tek seferlik bildirim = iade miktarı");
            Assert.AreEqual(420, ProfileService.PendingAshRefundNotice);

            Assert.AreEqual(2000, p.MasteryKills("ar_1"), "WeaponMastery korunur");
            Assert.AreEqual(300, p.MasteryKills("smg_1"));
            Assert.AreEqual(2, p.CapstoneWeaponCount > 0 ? p.WeaponMastery[0].Capstones : -1);
            CollectionAssert.AreEqual(new[] { "r10:ar_1", "capstone" }, p.AshMilestones, "AshMilestones korunur");
            Assert.AreEqual(Soy, p.ProfileLineageId, "lineage aynen");
            Assert.AreEqual(30, p.LastAppliedCompletionOrdinal, "ordinal aynen");
            Assert.AreEqual(12, p.ProfileLevel);
            Assert.AreEqual(3000, p.TotalKills);
            CollectionAssert.IsSubsetOf(new[] { "ar_1", "smg_1", "ar_2" }, p.UnlockedWeapons, "açık silah kilitlenmez");
            Assert.IsTrue(p.IsValid());

            ProfileData ham;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out ham));
            StringAssert.Contains("\"SaveVersion\":5", File.ReadAllText(TestPath), "ilk yazıma kadar disk v5");
        }

        /// <summary>WP-112 (C-00 12.10): a profile that owns the retired aw_rethink loads without it, its 150 Ash back in full, the
        /// refund notice once; acknowledged and reloaded, nothing is refunded twice.</summary>
        [Test]
        public void Wp112_EmekliUyanisDugumu_YuklemedeKulIade_BildirimBirKez()
        {
            File.WriteAllText(TestPath, V5Json());
            ProfileData p = ProfileService.Current;   // a valid v6 profile (the v5 step ran)
            p.MetaNodes.Clear();
            p.MetaNodes.Add("aw_codex");
            p.MetaNodes.Add("aw_rethink");
            p.MetaPinnedNode = "aw_rethink";
            p.MetaSeen = 2;
            p.AshSpentTotal = 250;
            p.AshBalance = p.AshEarnedTotal - p.AshSpentTotal;
            p.PendingAshRefundNotice = 0;
            Assert.IsTrue(p.IsValid());
            Assert.IsTrue(ProfileService.Save());
            StringAssert.Contains("aw_rethink", File.ReadAllText(TestPath), "the old build's profile on disk");

            Bosalt();
            ProfileData q = ProfileService.Current;
            CollectionAssert.AreEqual(new[] { "aw_codex" }, q.MetaNodes, "the retired node is gone, the rest kept");
            Assert.AreEqual(100, q.AshSpentTotal, "aw_rethink's 150 back");
            Assert.AreEqual(q.AshEarnedTotal - 100, q.AshBalance);
            Assert.AreEqual(150, ProfileService.PendingAshRefundNotice, "refund notice = the Ash given back");
            Assert.AreEqual(string.Empty, q.MetaPinnedNode);
            Assert.LessOrEqual(q.MetaSeen, q.MetaNodes.Count);
            Assert.IsTrue(q.IsValid());

            Assert.AreEqual(MetaCommandResult.Done, ProfileService.AcknowledgeAshRefundNotice());
            Assert.IsFalse(File.ReadAllText(TestPath).Contains("aw_rethink"), "the acknowledgement wrote the profile without it");
            Bosalt();
            ProfileData r = ProfileService.Current;
            Assert.AreEqual(0, ProfileService.PendingAshRefundNotice, "notice once");
            Assert.AreEqual(100, r.AshSpentTotal, "no second refund");
            Assert.AreEqual(0, ProfileMigration.RetireMetaNodes(r), "idempotent");
        }

        [Test]
        public void V5Bak_BaytEsit_UzerineYazilmaz()
        {
            string v5 = V5Json();
            File.WriteAllText(TestPath, v5);
            byte[] once = File.ReadAllBytes(TestPath);
            LogAssert.Expect(LogType.Log, new Regex(@"version-5 profile was kept byte for byte as 'prototype-profile\.json\.v5\.bak'"));
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(6, p.SaveVersion);
            Assert.IsTrue(File.Exists(LegacyPath), "yükleme anında, ilk yazımdan önce yedek alındı");
            CollectionAssert.AreEqual(once, File.ReadAllBytes(LegacyPath), "yedek bayt-bayt v5");

            Assert.IsTrue(ProfileService.ApplyRun(Ozet(4, 200)).Applied);
            StringAssert.Contains("\"SaveVersion\":6", File.ReadAllText(TestPath), "göç diske indi");
            CollectionAssert.AreEqual(once, File.ReadAllBytes(LegacyPath), "kayıt yedeğe dokunmaz");

            // A second (different) v5 document later: the first backup is never overwritten.
            File.WriteAllText(TestPath, V5Json(earned: 700, spent: 100));
            Bosalt();
            Assert.AreEqual(100, ProfileService.Current.PendingAshRefundNotice);
            Assert.IsTrue(ProfileService.Save());
            CollectionAssert.AreEqual(once, File.ReadAllBytes(LegacyPath), "var olan .v5.bak üzerine yazılmaz");
            Assert.IsFalse(File.Exists(LegacyPath + ProfileStore.CandidateSuffix), "ara dosya kalmaz");
        }

        [Test]
        public void V5Bak_Yazilamazsa_GocDiskeInmez_OturumSaltOkunur()
        {
            string v5 = V5Json();
            File.WriteAllText(TestPath, v5);
            Directory.CreateDirectory(LegacyPath);   // the backup cannot be written
            LogAssert.Expect(LogType.Warning, new Regex("previous-version profile could not be backed up"));
            LogAssert.Expect(LogType.Warning, new Regex("could not be backed up before its upgrade"));
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(6, p.SaveVersion, "bellekte göç edilmiş profil oynanır");
            Assert.IsTrue(ProfileStore.SaveLocked, "yedeksiz göç diske yazılmaz: oturum salt okunur");

            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved \\(Refused\\)"));
            Assert.IsFalse(ProfileService.Save());
            Assert.AreEqual(v5, File.ReadAllText(TestPath), "v5 dosyası bayt bayt aynı");
            Assert.IsFalse(File.Exists(TestPath + ProfileStore.BackupSuffix), ".bak da yazılmadı");
        }

        [Test]
        public void V5Bak_KayitZincirinde_Yazilamazsa_KayitReddedilir_EskiDosyaKalir()
        {
            // The save chain itself (not only the load) refuses to replace a pre-v6 file it could not keep.
            ProfileData canli = ProfileService.Current;   // fresh v6 in memory
            string v5 = V5Json();
            File.WriteAllText(TestPath, v5);              // a v5 file appears under it (another build, a restore)
            Directory.CreateDirectory(LegacyPath);
            LogAssert.Expect(LogType.Warning, new Regex("previous-version profile could not be backed up"));
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved \\(BackupCopy\\)"));
            ProfileSaveResult r = ProfileStore.Save(canli.Clone());
            Assert.IsFalse(r.Success);
            Assert.AreEqual(ProfileSaveStage.BackupCopy, r.FailedStage);
            Assert.AreEqual(v5, File.ReadAllText(TestPath), "eski dosya değişmedi");
            Assert.IsFalse(File.Exists(TestPath + ProfileStore.CandidateSuffix), "aday silindi");

            Directory.Delete(LegacyPath);
            Assert.IsTrue(ProfileStore.Save(canli.Clone()).Success, "yedek yazılabilince kayıt geçer");
            Assert.AreEqual(v5, File.ReadAllText(LegacyPath), "önce yedek alındı");
        }

        [Test]
        public void Idempotan_IkinciGoc_Degistirmez()
        {
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileMigration.Parse(V5Json(), out ProfileData p));
            string bir = JsonUtility.ToJson(p);
            ProfileMigration.Migrate(p);
            Assert.AreEqual(bir, JsonUtility.ToJson(p), "göç idempotan");

            // The migrated document, written and read back as v6, is never refunded again.
            p.AshSpentTotal = 100;   // something bought after the migration
            p.Sanitize();
            string v6 = JsonUtility.ToJson(p);
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileMigration.Parse(v6, out ProfileData q));
            Assert.AreEqual(100, q.AshSpentTotal, "v6 belge yeniden iade almaz");
            Assert.AreEqual(500, q.AshBalance);
            Assert.AreEqual(420, q.PendingAshRefundNotice, "bildirim büyümez");
        }

        [Test]
        public void ExactlyOnce_IadeBirKez_BildirimTemizlenir_SonrakiKosuUygulanir()
        {
            File.WriteAllText(TestPath, V5Json());
            Assert.AreEqual(600, ProfileService.Current.AshBalance);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.TryBuyMetaNode("aw_codex"));
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.AcknowledgeAshRefundNotice());
            Assert.AreEqual(0, ProfileService.PendingAshRefundNotice);
            Assert.AreEqual(MetaCommandResult.Done, ProfileService.AcknowledgeAshRefundNotice(), "idempotan");

            Bosalt();   // relaunch
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(500, p.AshBalance, "iade ikinci kez gelmez");
            Assert.AreEqual(100, p.AshSpentTotal);
            CollectionAssert.AreEqual(new[] { "aw_codex" }, p.MetaNodes, "göç sonrası alım korunur");
            Assert.AreEqual(0, p.PendingAshRefundNotice);

            RunRewardReport r = ProfileService.ApplyRun(Ozet(3, 90));
            Assert.IsTrue(r.Applied, "lineage + ordinal korundu: sıradaki koşu kabul");
            Assert.AreEqual(31, ProfileService.Current.LastAppliedCompletionOrdinal);
        }

        [Test]
        public void SahteV5_V6AlanlariTasirsa_YenidenKurulur_IadeKazanilaniAsamaz()
        {
            string sahte = "\"ModeStats\":[{\"HighestRound\":99,\"BestKills\":9,\"Runs\":9,\"TotalKills\":9,\"BestNodes\":9}," +
                           "{\"HighestRound\":99,\"BestKills\":0,\"Runs\":0,\"TotalKills\":0,\"BestNodes\":0},{}],\"PendingAshRefundNotice\":9999,";
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileMigration.Parse(V5Json(earned: 100, spent: 500, extra: sahte), out ProfileData p));
            Assert.AreEqual(100, p.PendingAshRefundNotice, "bildirim gerçek iadedir, dosyadaki değil; harcanan kazanılanı aşamaz");
            Assert.AreEqual(100, p.AshBalance);
            Assert.AreEqual(14, p.ModeStatsFor(TreeMode.Normal).HighestRound, "Normal eski HighestRound'dan");
            Assert.AreEqual(0, p.ModeStatsFor(TreeMode.Hard).HighestRound, "sahte Zor kaydı silinir");
            Assert.AreEqual(14, p.HighestRound, "sahte 99 HighestRound'a sızmaz");
            Assert.IsTrue(p.IsValid());
        }

        [Test]
        public void ModeStatsGocu_NormalTohumlanir_DigerleriSifir_V4DenDogrudan()
        {
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileMigration.Parse(V5Json(), out ProfileData p));
            Assert.AreEqual(TreeMode.Count, p.ModeStats.Length);
            ModeStatsEntry n = p.ModeStatsFor(TreeMode.Normal);
            Assert.AreEqual(14, n.HighestRound);
            Assert.AreEqual(30, n.Runs);
            Assert.AreEqual(3000, n.TotalKills);
            Assert.AreEqual(0, n.BestKills, "bilinmeyen en iyi uydurulmaz");
            Assert.AreEqual(0, p.HighestRoundIn(TreeMode.Hard));
            Assert.AreEqual(0, p.HighestRoundIn(TreeMode.Demolition));
            Assert.AreEqual(14, p.HighestRound, "eski okuyucular için HighestRound aynen");

            // v4 straight to v6: the v5 back-grant, then a respec of nothing.
            var v4 = new ProfileData { SaveVersion = 4, ProfileLevel = 5, TotalXpEarned = 900, HighestRound = 6, RunsPlayed = 3, ProfileLineageId = Soy };
            ProfileMigration.Migrate(v4);
            Assert.AreEqual(6, v4.SaveVersion);
            Assert.AreEqual(0, v4.PendingAshRefundNotice);
            Assert.AreEqual(v4.AshEarnedTotal, v4.AshBalance);
            Assert.AreEqual(6, v4.HighestRoundIn(TreeMode.Normal));
            Assert.IsTrue(v4.IsValid());
        }

        [Test]
        public void GercekYol_EditorYuklemesi_YedekYazmaz_Kapi()
        {
            // C-00 §4.14 (11.10, root cause of b1-profile-2300): an EditMode test that read the real profile (no override)
            // made the load-time backup write the real .v5.bak. The backup is written only in a sandbox or on the player path.
            Assert.IsTrue(ProfileStore.IsSandbox, "bu fixture sandbox'ta");
            Assert.IsTrue(ProfileStore.LegacyBackupAllowed, "sandbox'ta yedek yazılır");
            Assert.IsTrue(Application.isBatchMode || !Application.isPlaying, "EditMode / batch");

            string family = RealFamily();
            bool realLegacyBefore = File.Exists(RealPath + ProfileStore.LegacyV5BackupSuffix);
            ProfileStore.SaveStageHook = s => throw new IOException("belt: no save may run in this test");
            try
            {
                ProfileStore.SetPathOverride(RealPath);   // an override that names the real file is NOT a sandbox
                Assert.IsFalse(ProfileStore.IsSandbox);
                Assert.IsFalse(ProfileStore.LegacyBackupAllowed, "batch / EditMode + gerçek yol → yedek yazılmaz");
                ProfileStore.SetPathOverride(null);
                Assert.IsFalse(ProfileStore.IsSandbox);
                Assert.IsFalse(ProfileStore.LegacyBackupAllowed);
                // (The real profile itself is not loaded here: WeaponRosterTests reads it in the same run, and the guard
                // window around this run is the evidence that such a read writes nothing.)
            }
            finally
            {
                ProfileStore.SaveStageHook = null;
                ProfileStore.SetPathOverride(TestPath);
                Bosalt();
            }

            Assert.AreEqual(realLegacyBefore, File.Exists(RealPath + ProfileStore.LegacyV5BackupSuffix), "gerçek .v5.bak oluşmadı");
            Assert.AreEqual(family, RealFamily(), "gerçek profil dosyaları bayt bayt aynı");
        }

        [Test]
        public void SurumOkuyucu_DosyadakiSurumu_GocOncesiVerir()
        {
            Assert.AreEqual(5, ProfileMigration.ReadDocumentVersion(V5Json()));
            Assert.AreEqual(0, ProfileMigration.ReadDocumentVersion("{\"ProfileLevel\":3}"));
            Assert.AreEqual(0, ProfileMigration.ReadDocumentVersion("not json"));
            Assert.AreEqual(0, ProfileMigration.ReadDocumentVersion(null));
            Assert.AreEqual(6, ProfileMigration.ReadDocumentVersion(JsonUtility.ToJson(new ProfileData())));
        }
    }
}
