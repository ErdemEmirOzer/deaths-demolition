using System;
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
    /// WP-077 B2a · profil şeması v5 (meta YOL): v4 → v5 göçü (K11 seviye korunur, silah anahtarları WP-069,
    /// geriye dönük Kül), koşu Kül'ü ApplyRun transaction'ında (WP-009H exactly-once: tekrar yok, save
    /// hatasında yok), tek seferlik kilometre taşları, ustalık, XP eğrisi 100·L + 10·L² ve L50 tavanı,
    /// WP-078 [Progress] satırı. İzole profil yolu; gerçek profile dokunulmaz.
    /// </summary>
    [TestFixture]
    public sealed class ProfileV5Tests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp077-v5", "prototype-profile.json");

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

        [TearDown]
        public void TearDown() => ProfileStore.SaveStageHook = null;

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

        private static RunSummary Ozet(int reached, int roundXp, int kills = 0, ClassicWeaponId silah = ClassicWeaponId.AssaultRifle, int capstones = 0)
        {
            return new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = reached,
                CompletedRounds = Math.Max(0, reached - 1),
                TotalKills = kills,
                ClassicWeaponKills = kills,   // DEC-111: every kill of these runs is the classic weapon's
                StandardKills = kills,
                KillXp = kills,
                CompletedRoundXp = roundXp,
                TotalXpEarned = roundXp + kills,
                Weapon = silah,
                TreeCapstones = capstones,
                EndReason = RunEndReason.PlayerDeath
            };
        }

        private static int BeklenenKul(RunRewardReport r, int xp, int kilometreTasi)
        {
            int k = xp / Math.Max(1, Prog.XpPerAsh) + kilometreTasi;
            foreach (AchievementDefinition d in r.EarnedDefinitions) k += Prog.AshForRarity((int)d.Rarity);
            return k;
        }

        [Test]
        public void Egri_K4_VeTavan()
        {
            Assert.AreEqual(110, ProfileService.LevelRequirement(1));
            Assert.AreEqual(240, ProfileService.LevelRequirement(2));
            Assert.AreEqual(6000, ProfileService.LevelRequirement(20));
            Assert.AreEqual(int.MaxValue, ProfileService.LevelRequirement(Prog.LevelCap), "tavan: seviye durur");

            LevelProgress p = ProfileProgression.ApplyXp(Prog.LevelCap, 0, 1_000_000, ProfileService.LevelRequirement);
            Assert.AreEqual(Prog.LevelCap, p.Level, "L50'de XP seviye yükseltmez");
            LevelProgress q = ProfileProgression.ApplyXp(Prog.LevelCap - 1, 0, 1_000_000, ProfileService.LevelRequirement);
            Assert.AreEqual(Prog.LevelCap, q.Level, "L49 → L50'de durur");
        }

        [Test]
        public void SonucVerisi_KilitAcma_KimliktenOkunur_MetindenDegil()
        {
            // C-00 30.09: RunResultsData tells a branch from a weapon by ProfileUnlockEntry.Kind (WP-068 R1-E), not by the English
            // "Upgrade branch" prefix — a Turkish (or any) detail line still lands as the right type with the entry's own id.
            var draft = new RunRewardReport.Draft { HasRun = true, Summary = new RunSummary { ReachedRound = 4, CompletedRounds = 3 } };
            draft.NewUnlocks.Add(new ProfileUnlockEntry("Delme", "Yükseltme dalı - tur sonunda teklif edilebilir", ProfileUnlockKind.Branch, "Piercing"));
            draft.NewUnlocks.Add(new ProfileUnlockEntry("Sanren", "Klasik silah - sonraki koşudan önce seçilebilir", ProfileUnlockKind.Weapon, "ar_2"));
            draft.NewUnlocks.Add(new ProfileUnlockEntry("Legacy", "Upgrade branch - can now be offered at the end of a round"));
            RunResultsData d = RunResultsData.From(draft.Seal(), new ProfileData());
            Assert.AreEqual(3, d.NewUnlocks.Count);
            Assert.AreEqual(UnlockContentType.Branch, d.NewUnlocks[0].Type, "Türkçe metinli dal yine dal");
            Assert.AreEqual("Piercing", d.NewUnlocks[0].Id);
            Assert.AreEqual(UnlockContentType.Weapon, d.NewUnlocks[1].Type);
            Assert.AreEqual("ar_2", d.NewUnlocks[1].Id, "kimliğin kendi anahtarı");
            Assert.AreEqual(UnlockContentType.Branch, d.NewUnlocks[2].Type, "kimliksiz eski girdi metin önekine düşer");
        }

        [Test]
        public void Ustalik_DEC111_YalnizBaslangicKlasikSilahininOldurmeleri()
        {
            // DEC-111 (user 30.09): mastery counts only the starting classic weapon's kills — Special and unowned kills do not.
            RunSummary o = Ozet(5, 100, kills: 30);
            o.ClassicWeaponKills = 18;
            Assert.IsTrue(ProfileService.ApplyRun(o).Applied);
            Assert.AreEqual(18, ProfileService.Current.MasteryKills("ar_1"), "yalnız klasik silahın öldürmeleri");
            Assert.AreEqual(30, ProfileService.Current.TotalKills, "toplam öldürme istatistiği değişmez");

            RunSummary fazla = Ozet(5, 100, kills: 5);
            fazla.ClassicWeaponKills = 99;   // a summary can never credit more than it killed
            Assert.IsTrue(ProfileService.ApplyRun(fazla).Applied);
            Assert.AreEqual(23, ProfileService.Current.MasteryKills("ar_1"));

            var istatistik = new RunStatsTracker();
            istatistik.RecordKill(EnemyTier.Standard, 1, true);
            istatistik.RecordKill(EnemyTier.Elite, 3, false);
            Assert.AreEqual(2, istatistik.TotalKills);
            Assert.AreEqual(1, istatistik.ClassicWeaponKills);
            istatistik.Reset();
            Assert.AreEqual(0, istatistik.ClassicWeaponKills);
        }

        [Test]
        public void Tavan_R1B_P2_XpSeviyeyeBirikmez_MaxOkunur()
        {
            // R1-B P2 (C-00 30.09): at the cap nothing banks into the level (TotalXpEarned / Ash count elsewhere), so a later
            // cap raise cannot turn into hundreds of level-ups; the UI reads IsAtLevelCap and shows MAX.
            LevelProgress p = ProfileProgression.ApplyXp(Prog.LevelCap, 500, 1_000_000, ProfileService.LevelRequirement);
            Assert.AreEqual(Prog.LevelCap, p.Level);
            Assert.AreEqual(0, p.XpIntoLevel, "tavanda seviyeye XP birikmez");
            Assert.AreEqual(0, p.LevelUps);
            Assert.IsTrue(ProfileService.IsAtLevelCap(Prog.LevelCap));
            Assert.IsFalse(ProfileService.IsAtLevelCap(Prog.LevelCap - 1));
            LevelProgress q = ProfileProgression.ApplyXp(Prog.LevelCap - 1, 0, 1_000_000, ProfileService.LevelRequirement);
            Assert.AreEqual(Prog.LevelCap, q.Level);
            Assert.AreEqual(0, q.XpIntoLevel, "L50'ye varınca artan XP de birikmez");
        }

        [Test]
        public void Gocu_V4_SahteMetaAlanlari_Temizlenir_R1B_P6()
        {
            // R1-B P6: a real v4 build cannot write the v5 meta fields; a "v4" document carrying them would get free nodes.
            var v4 = new ProfileData { SaveVersion = 4, ProfileLevel = 5, TotalXpEarned = 900, ProfileLineageId = ProfileData.NewLineageId() };
            v4.MetaNodes.Add("pa_eco_1");
            v4.MetaRespecCount = 2;
            v4.MetaSeen = 1;
            v4.AshMilestones.Add("capstone");
            v4.WeaponMastery.Add(new WeaponMasteryEntry { Weapon = "ar_1", Kills = 999, Capstones = 3 });
            v4.MetaPinnedNode = "pa_eco_2";
            ProfileMigration.Migrate(v4);
            Assert.AreEqual(ProfileData.CurrentVersion, v4.SaveVersion);
            Assert.IsEmpty(v4.MetaNodes, "sahte düğümler bedava kalmaz");
            Assert.AreEqual(0, v4.MetaRespecCount);
            Assert.AreEqual(0, v4.MetaSeen);
            Assert.IsEmpty(v4.AshMilestones);
            Assert.IsEmpty(v4.WeaponMastery);
            Assert.AreEqual(string.Empty, v4.MetaPinnedNode);
            Assert.AreEqual(900 / Prog.XpPerAsh, v4.AshBalance, "geriye dönük Kül yine verilir");
            Assert.IsTrue(v4.IsValid());
        }

        [Test]
        public void GeriyeDonukKul_R1B_P4_YedekOranDEC098()
        {
            // R1-B P4: the back-grant never throws (a config / catalog failure falls back to XP only at the DEC-098 rate).
            Assert.AreEqual(30, ProfileMigration.FallbackXpPerAsh);
            Assert.AreEqual(0, ProfileMigration.BackGrantAsh(null));
        }

        [Test]
        public void Gocu_V4ToV5_SeviyeKorunur_SilahAnahtari_GeriyeDonukKul_Idempotan()
        {
            var v4 = new ProfileData
            {
                SaveVersion = 4, ProfileLevel = 9, XpIntoLevel = 40, TotalXpEarned = 2900, RunsPlayed = 3,
                ProfileLineageId = ProfileData.NewLineageId(), LastAppliedCompletionOrdinal = 3
            };
            v4.UnlockedWeapons.Add("AssaultRifle");
            v4.UnlockedWeapons.Add("AutoSmg");
            v4.Achievements.Add("first_blood");
            string soy = v4.ProfileLineageId;

            ProfileMigration.Migrate(v4);
            Assert.AreEqual(ProfileData.CurrentVersion, v4.SaveVersion);
            Assert.AreEqual(9, v4.ProfileLevel, "K11: seviye yeni eğriyle yeniden hesaplanmaz");
            Assert.AreEqual(40, v4.XpIntoLevel);
            CollectionAssert.AreEqual(new[] { "ar_1", "smg_1" }, v4.UnlockedWeapons, "WP-069 anahtarları; açık silah kilitlenmez");
            Assert.AreEqual(soy, v4.ProfileLineageId, "lineage aynen");
            Assert.AreEqual(3, v4.LastAppliedCompletionOrdinal, "ordinal aynen → uygulanmış koşu yeniden uygulanmaz");

            AchievementDefinition fb = AchievementCatalog.Find("first_blood");
            int beklenen = 2900 / Prog.XpPerAsh + (fb != null ? Prog.AshForRarity((int)fb.Rarity) : 0);
            Assert.AreEqual(beklenen, v4.AshEarnedTotal, "geriye dönük Kül = XP/40 + başarım nadirliği");
            Assert.AreEqual(beklenen, v4.AshBalance);
            Assert.AreEqual(0, v4.AshSpentTotal);
            Assert.IsTrue(v4.IsValid());

            string once = JsonUtility.ToJson(v4);
            ProfileMigration.Migrate(v4);
            Assert.AreEqual(once, JsonUtility.ToJson(v4), "göç idempotan");
        }

        /// <summary>A v3 document as the 14.09 builds wrote it (WP-009H): no v4 feat / content fields, no v5 PATH fields.</summary>
        private static string V3Json(string lineage)
        {
            return "{\"SaveVersion\":3,\"ProfileLevel\":8,\"XpIntoLevel\":60,\"TotalXpEarned\":2600,\"HighestRound\":11," +
                   "\"RunsPlayed\":13,\"RoundsCompleted\":71,\"TotalKills\":905,\"StandardKills\":820,\"SpecialKills\":61," +
                   "\"EliteKills\":22,\"UltraKills\":2,\"UnattributedKills\":0,\"ArmorPiecesBroken\":130,\"BestRunXp\":420," +
                   "\"Achievements\":[\"first_blood\",\"reach_round_10\",\"defeat_an_ultra\"]," +
                   "\"UnlockedWeapons\":[\"AssaultRifle\",\"AutoSmg\",\"CombatShotgun\"],\"UnlockedBranches\":[\"Piercing\",\"FireRate\"]," +
                   "\"ProfileLineageId\":\"" + lineage + "\",\"LastAppliedCompletionOrdinal\":13,\"LastSavedUtc\":\"2026-09-14T20:11:02Z\"}";
        }

        [Test]
        public void Gocu_V3ToV4ToV5_K11SeviyeTabani_SilahAnahtari_GeriyeDonukKul()
        {
            // C-00 (2026-09-25): the user's real profile is back at its 14.09 v3 state; v5 must read it (v3 → v4 → v5).
            string soy = ProfileData.NewLineageId();
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileMigration.Parse(V3Json(soy), out ProfileData p));
            Assert.AreEqual(ProfileData.CurrentVersion, p.SaveVersion);
            Assert.AreEqual(8, p.ProfileLevel, "K11: seviye yeni eğriyle (2 600 XP → L6) düşürülmez");
            Assert.AreEqual(60, p.XpIntoLevel);
            Assert.AreEqual(2600, p.TotalXpEarned);
            Assert.AreEqual(13, p.RunsPlayed);
            Assert.AreEqual(905, p.TotalKills);
            Assert.AreEqual(soy, p.ProfileLineageId, "lineage aynen");
            Assert.AreEqual(13, p.LastAppliedCompletionOrdinal, "ordinal aynen → uygulanmış koşu yeniden uygulanmaz");
            CollectionAssert.AreEqual(new[] { "ar_1", "smg_1", "sg_1" }, p.UnlockedWeapons, "açık silah kilitlenmez, WP-069 anahtarı");
            Assert.AreEqual(0, p.SpecialWeaponPickups, "v4 alanları sıfırdan");
            Assert.IsNotNull(p.UnlockedContent);

            int kul = 2600 / Prog.XpPerAsh;
            foreach (string id in new[] { "first_blood", "reach_round_10", "defeat_an_ultra" })
            {
                AchievementDefinition d = AchievementCatalog.Find(id);
                if (d != null) kul += Prog.AshForRarity((int)d.Rarity);
            }

            Assert.AreEqual(kul, p.AshEarnedTotal, "geriye dönük Kül = XP / bölen + başarım nadirliği");
            Assert.AreEqual(kul, p.AshBalance);
            Assert.IsTrue(p.IsValid());

            string once = JsonUtility.ToJson(p);
            ProfileMigration.Migrate(p);
            Assert.AreEqual(once, JsonUtility.ToJson(p), "göç idempotan");
        }

        [Test]
        public void Gocu_V3DosyasindanYukle_SonrakiKosuUygulanir_SeviyeDusmez()
        {
            File.WriteAllText(TestPath, V3Json(ProfileData.NewLineageId()));
            Bosalt();
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(8, p.ProfileLevel);
            Assert.AreEqual(ProfileData.CurrentVersion, p.SaveVersion);

            RunRewardReport r = ProfileService.ApplyRun(Ozet(6, 150, 20));
            Assert.IsTrue(r.Applied);
            Assert.GreaterOrEqual(ProfileService.Current.ProfileLevel, 8, "göç sonrası ilk koşu seviyeyi düşürmez");
            Assert.AreEqual(14, ProfileService.Current.RunsPlayed);
            StringAssert.Contains("\"SaveVersion\": " + ProfileData.CurrentVersion, File.ReadAllText(TestPath).Replace("\"SaveVersion\":" + ProfileData.CurrentVersion, "\"SaveVersion\": " + ProfileData.CurrentVersion), "diske v5 yazıldı");
        }

        [Test]
        public void Kul_KosuSonunda_AyniTransactionda_TekrarYok_SaveHatasindaYok()
        {
            RunSummary o = Ozet(4, 380, kills: 20);
            RunRewardReport r = ProfileService.ApplyRun(o);
            Assert.IsTrue(r.Applied);
            int beklenen = BeklenenKul(r, 400, 0);
            Assert.AreEqual(beklenen, r.AshEarned);
            Assert.AreEqual(beklenen, ProfileService.Current.AshBalance);
            Assert.AreEqual(beklenen, ProfileService.Current.AshEarnedTotal);
            Assert.AreEqual(20, ProfileService.Current.MasteryKills("ar_1"), "ustalık: silah anahtarına öldürme");

            Assert.AreSame(r, ProfileService.ApplyRun(o), "aynı kimlik → duplicate");
            Assert.AreEqual(beklenen, ProfileService.Current.AshBalance, "duplicate Kül vermez");

            ProfileStore.SaveStageHook = s => { if (s == ProfileSaveStage.Replace) throw new IOException("injected"); };
            LogAssert.Expect(LogType.Warning, new Regex("Save failed at Replace \\(IOException\\)"));
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            RunRewardReport f = ProfileService.ApplyRun(Ozet(3, 200));
            ProfileStore.SaveStageHook = null;
            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, f.Outcome);
            Assert.AreEqual(0, f.AshEarned, "başarısız makbuz Kül listelemez");
            Assert.AreEqual(beklenen, ProfileService.Current.AshBalance, "başarısız save'de RAM değişmez");
        }

        [Test]
        public void KilometreTaslari_TekSeferlik_R10SilahBasina_Doruk()
        {
            RunRewardReport a = ProfileService.ApplyRun(Ozet(10, 400, capstones: 1));
            Assert.AreEqual(BeklenenKul(a, 400, Prog.AshFirstRound10PerWeapon + Prog.AshFirstCapstone), a.AshEarned, "ilk R10 (ar_1) + ilk doruk");
            RunRewardReport b = ProfileService.ApplyRun(Ozet(11, 400, capstones: 1));
            Assert.AreEqual(BeklenenKul(b, 400, 0), b.AshEarned, "aynı silahla ikinci R10 ve ikinci doruk kilometre taşı değil");
            RunRewardReport c = ProfileService.ApplyRun(Ozet(10, 400, silah: ClassicWeaponId.AutoSmg));
            Assert.AreEqual(BeklenenKul(c, 400, Prog.AshFirstRound10PerWeapon), c.AshEarned, "başka silahla ilk R10 yine sayılır");
            CollectionAssert.IsSubsetOf(new[] { "r10:ar_1", "r10:smg_1", "capstone" }, ProfileService.Current.AshMilestones);
        }

        [Test]
        public void ProgressSatiri_WP078()
        {
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Progress\] xp=120 ash=\d+ ashBalance=\d+ meta=0 level=1->\d+ round=3 weapon=ar_1 keystones=0 capstones=0 hearth=0"));
            Assert.IsTrue(ProfileService.ApplyRun(Ozet(3, 120)).Applied);
        }

        [Test]
        public void Sanitize_KulDengesi_VeGecersizlik()
        {
            var p = new ProfileData { AshEarnedTotal = 100, AshSpentTotal = 30, AshBalance = 999, ProfileLineageId = ProfileData.NewLineageId() };
            Assert.IsFalse(p.IsValid(), "bakiye ≠ kazanılan − harcanan geçersiz");
            p.Sanitize();
            Assert.AreEqual(70, p.AshBalance);
            p.AshSpentTotal = 500;
            p.Sanitize();
            Assert.AreEqual(100, p.AshSpentTotal, "harcanan kazanılanı aşamaz");
            Assert.AreEqual(0, p.AshBalance);
            p.WeaponMastery.Add(new WeaponMasteryEntry { Weapon = "AutoSmg", Kills = 5 });
            p.WeaponMastery.Add(new WeaponMasteryEntry { Weapon = "smg_1", Kills = 7 });
            p.Sanitize();
            Assert.AreEqual(1, p.WeaponMastery.Count, "eski ad + anahtar birleşir");
            Assert.AreEqual(12, p.MasteryKills("smg_1"));
            Assert.IsTrue(p.IsValid());
        }
    }
}
