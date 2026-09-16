using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-035: kozmetik veri katmanı (unvan/rozet seçimi, "yeni" bayrakları), başarım ilerleme kaynağı,
    /// roster'dan türetilen silah unlock girdileri (merdivenle çelişki yok) ve Run Results ekonomi alanları.
    /// İzole profil yolu; gerçek profile dokunulmaz.
    /// </summary>
    [TestFixture]
    [Category("WP035")]
    public sealed class CosmeticsBindingTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-editmode-wp035", "prototype-profile.json");

        private static readonly string RealPath =
            Path.Combine(Application.persistentDataPath, ProfileStore.FileName);

        private const string Soy = "0123456789abcdef0123456789abcdef";
        private string _realHashBefore;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
            Directory.CreateDirectory(Path.GetDirectoryName(TestPath));
            ProfileStore.SetPathOverride(TestPath);
            OnbellegiBosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            OnbellegiBosalt();
            ProfileStore.SetPathOverride(null);
            try { Directory.Delete(Path.GetDirectoryName(TestPath), true); } catch (Exception) { }
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path);
            ProfileStore.SaveStageHook = null;
            AchievementCatalog.TestSeam.Override(null);
            UnlockCatalog.TestSeam.Override(null);
            ProfileService.SessionSkinProvider = null;
            ProfileService.ResetProfile();
            Assert.IsFalse(ProfileStore.SaveLocked);
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.SaveStageHook = null;
            AchievementCatalog.TestSeam.Override(null);
            UnlockCatalog.TestSeam.Override(null);
            ProfileService.SessionSkinProvider = null;
        }

        private static void OnbellegiBosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastResults", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        }

        private static void YenidenYukle()
        {
            OnbellegiBosalt();
            ProfileService.EnsureLoaded();
        }

        private static string HashOf(string path)
        {
            if (!File.Exists(path)) return "MISSING";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
            }
        }

        private static RunSummary Ozet(int reached, int kills, int killXp, int roundXp, int gold = 0, int spentUpg = 0, int spentReroll = 0, int upgrades = 0, int rerolls = 0)
        {
            return new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = reached, CompletedRounds = Math.Max(0, reached - 1),
                TotalKills = kills, StandardKills = kills, KillXp = killXp, CompletedRoundXp = roundXp, TotalXpEarned = killXp + roundXp,
                GoldEarned = gold, GoldSpent = spentUpg + spentReroll, GoldSpentOnUpgrades = spentUpg, GoldSpentOnRerolls = spentReroll,
                GoldUnspent = Math.Max(0, gold - spentUpg - spentReroll), UpgradesPurchased = upgrades, RerollsUsed = rerolls,
                Weapon = ClassicWeaponId.AssaultRifle, BuildDescription = "P1", EndReason = RunEndReason.PlayerDeath
            };
        }

        private static AchievementDefinition Tanim(string id, AchievementCondition c, int t, AchievementScope scope = AchievementScope.Lifetime)
        {
            return new AchievementDefinition { Id = id, DisplayName = id, Description = id + " desc", Condition = c, Threshold = t, Scope = scope };
        }

        private static UnlockDefinition Icerik(string id, UnlockContentType type, UnlockCondition cond, string key, int numeric = 0)
        {
            return new UnlockDefinition { Id = id, Type = type, DisplayName = "N " + id, Detail = "d", Condition = cond, Key = key, NumericKey = numeric };
        }

        // ------------------------------------------------------------ unvan / rozet

        [Test]
        public void UnvanRozet_SecimYalnizAcilmisIcerik_KaliciVeReloadSonrasi_YeniBayraklari()
        {
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition> { Tanim("c_kill", AchievementCondition.TotalKills, 1) });
            UnlockCatalog.TestSeam.Override(new List<UnlockDefinition>
            {
                Icerik("title_a", UnlockContentType.Title, UnlockCondition.Achievement, "c_kill"),
                Icerik("title_b", UnlockContentType.Title, UnlockCondition.Round, "9", 9),
                Icerik("badge_a", UnlockContentType.Badge, UnlockCondition.Achievement, "c_kill"),
                Icerik("ind_a", UnlockContentType.Indicator, UnlockCondition.Achievement, "c_kill")
            });

            ICosmeticsSource k = ProfileService.CosmeticsSource;
            Assert.AreEqual(2, k.Titles.Count); Assert.AreEqual(1, k.Badges.Count); Assert.AreEqual(PlayerSkins.Count, k.Skins.Count);
            Assert.IsFalse(k.Titles[0].Unlocked);
            Assert.IsFalse(k.SelectTitle("title_a"), "açılmamış unvan seçilemez");
            Assert.AreEqual(string.Empty, k.SelectedTitle);
            Assert.AreEqual(0, k.NewCount);

            RunRewardReport r = ProfileService.ApplyRun(Ozet(2, 3, 3, 20));
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(3, r.OpenedContent.Count, "title_a + badge_a + ind_a aynı transaction'da");
            Assert.AreEqual(3, k.NewCount, "üç yeni içerik");
            Assert.IsTrue(k.Titles[0].Unlocked && k.Titles[0].IsNew && !k.Titles[0].Selected);
            Assert.IsFalse(k.Titles[1].Unlocked, "round 9 yok");

            Assert.IsTrue(k.SelectTitle("title_a"));
            Assert.IsFalse(k.SelectTitle("badge_a"), "rozet unvan olarak seçilemez");
            Assert.IsFalse(k.SelectBadge("title_a"));
            Assert.IsTrue(k.SelectBadge("badge_a"));
            Assert.IsFalse(k.SelectTitle("ind_a"), "gösterge kozmetik seçimi değil");
            Assert.AreEqual("title_a", k.SelectedTitle); Assert.AreEqual("badge_a", k.SelectedBadge);
            Assert.IsTrue(k.Titles[0].Selected);

            k.MarkContentSeen();
            Assert.AreEqual(0, k.NewCount);
            Assert.IsFalse(k.Titles[0].IsNew);

            // Kalıcı: diskten yeniden yükleme aynı seçimi ve görüldü durumunu getirir.
            YenidenYukle();
            Assert.AreEqual("title_a", ProfileService.CosmeticsSource.SelectedTitle);
            Assert.AreEqual("badge_a", ProfileService.CosmeticsSource.SelectedBadge);
            Assert.AreEqual(0, ProfileService.CosmeticsSource.NewCount);
            Assert.IsTrue(ProfileService.CosmeticsSource.SelectTitle(string.Empty), "boş = temizle");
            Assert.AreEqual(string.Empty, ProfileService.CosmeticsSource.SelectedTitle);

            // Skinler: hep açık; seçim oturumdan (DEC-039), profile yazılmaz.
            ProfileService.SessionSkinProvider = () => PlayerSkin.MossCream;
            IReadOnlyList<CosmeticEntry> skins = ProfileService.CosmeticsSource.Skins;
            Assert.IsTrue(skins[2].Selected && skins[2].Unlocked && skins[2].Kind == CosmeticKind.Skin);
            StringAssert.DoesNotContain("Skin", File.ReadAllText(TestPath), "skin seçimi profile yazılmaz");
        }

        [Test]
        public void ProfilDosyasi_SecimBozuksa_SanitizeTemizler_EskiV4AnahtarsizYuklenir()
        {
            // Eski (WP-023) v4 belge: yeni anahtarlar yok → varsayılanlar; kazanılmış hiçbir şey değişmez.
            File.WriteAllText(TestPath, "{\"SaveVersion\":4,\"ProfileLevel\":2,\"XpIntoLevel\":5,\"TotalXpEarned\":105,\"HighestRound\":3,\"RunsPlayed\":2,\"RoundsCompleted\":2,\"TotalKills\":6,\"StandardKills\":6,\"SpecialKills\":0,\"EliteKills\":0,\"UltraKills\":0,\"UnattributedKills\":0,\"ArmorPiecesBroken\":0,\"BestRunXp\":60,\"SpecialWeaponPickups\":1,\"FlawlessRounds\":0,\"Achievements\":[\"first_blood\"],\"UnlockedWeapons\":[\"AssaultRifle\"],\"UnlockedBranches\":[],\"UnlockedContent\":[\"title_first_blood\"],\"ProfileLineageId\":\"" + Soy + "\",\"LastAppliedCompletionOrdinal\":2,\"LastSavedUtc\":\"\"}");
            YenidenYukle();
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(string.Empty, p.SelectedTitle); Assert.AreEqual(string.Empty, p.SelectedBadge);
            Assert.AreEqual(0, p.AchievementsSeen); Assert.AreEqual(0, p.ContentSeen);
            Assert.AreEqual(1, ProfileService.AchievementProgressSource.NewCount, "eski dosyada kazanılmış = yeni");
            Assert.AreEqual(2, p.LastAppliedCompletionOrdinal); Assert.AreEqual(Soy, p.ProfileLineageId);
            Assert.IsTrue(p.IsValid());

            // Elle bozulmuş seçim / sayaç: temizlenir, dosya reddedilmez.
            ProfileData q = p.Clone();
            q.SelectedTitle = "not_opened"; q.SelectedBadge = null; q.AchievementsSeen = 99; q.ContentSeen = -3;
            Assert.IsFalse(q.IsValid());
            q.Sanitize();
            Assert.AreEqual(string.Empty, q.SelectedTitle); Assert.AreEqual(string.Empty, q.SelectedBadge);
            Assert.AreEqual(1, q.AchievementsSeen); Assert.AreEqual(0, q.ContentSeen);
            Assert.IsTrue(q.IsValid());
            q.SelectedTitle = "title_first_blood"; q.Sanitize();
            Assert.AreEqual("title_first_blood", q.SelectedTitle, "açılmış içerik seçimi korunur");
        }

        // ------------------------------------------------------------ ilerleme kaynağı

        [Test]
        public void IlerlemeKaynagi_MevcutEsikYuzde_YeniBayragi_SonKazanilan()
        {
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition>
            {
                Tanim("p_kills", AchievementCondition.TotalKills, 10),
                Tanim("p_round", AchievementCondition.ReachRound, 2),
                Tanim("p_run", AchievementCondition.TotalKills, 40, AchievementScope.SingleRun)
            });
            UnlockCatalog.TestSeam.Override(new List<UnlockDefinition>());

            IAchievementProgressSource s = ProfileService.AchievementProgressSource;
            Assert.AreEqual(3, s.TotalCount); Assert.AreEqual(0, s.EarnedCount); Assert.AreEqual(string.Empty, s.LastEarnedId);

            Assert.IsTrue(ProfileService.ApplyRun(Ozet(2, 4, 4, 20)).Applied);
            IReadOnlyList<AchievementProgressEntry> e = s.Entries;
            Assert.AreEqual("p_kills", e[0].Id); Assert.IsFalse(e[0].Earned); Assert.AreEqual(4, e[0].Current); Assert.AreEqual(10, e[0].Threshold); Assert.AreEqual(0.4f, e[0].Progress01, 0.001f);
            Assert.IsTrue(e[1].Earned && e[1].IsNew); Assert.AreEqual(1f, e[1].Progress01); Assert.AreEqual(2, e[1].Current);
            Assert.IsFalse(e[2].Earned); Assert.AreEqual(0, e[2].Current, "tek koşu kapsamı: ilerleme yok");
            Assert.AreEqual(1, s.EarnedCount); Assert.AreEqual(1, s.NewCount); Assert.AreEqual("p_round", s.LastEarnedId);

            s.MarkAchievementsSeen();
            Assert.AreEqual(0, s.NewCount);
            Assert.IsFalse(s.Entries[1].IsNew);

            Assert.IsTrue(ProfileService.ApplyRun(Ozet(1, 6, 6, 0)).Applied);
            Assert.AreEqual("p_kills", s.LastEarnedId);
            Assert.AreEqual(1, s.NewCount, "yalnız yeni kazanılan");
            Assert.AreEqual(10, s.Entries[0].Current);
            YenidenYukle();
            Assert.AreEqual(1, ProfileService.AchievementProgressSource.NewCount, "görüldü sayacı kalıcı");
        }

        // ------------------------------------------------------------ roster silah girdileri

        [Test]
        public void RosterSilahlari_KatalogtaReferans_MerdivenleAyniSeviye_DukkanDalKapisiDegismez()
        {
            IReadOnlyList<ClassicWeaponDefinition> roster = WeaponRoster.Enabled;
            Assume.That(roster.Count, Is.GreaterThan(0), "roster yüklü değil");
            PrototypeConfig config = PrototypeConfig.Active;
            int derived = 0;
            foreach (ClassicWeaponDefinition d in roster)
            {
                if (!d.HasRuntimeId || d.UnlockKind != WeaponUnlockKind.ProfileLevel) continue;
                UnlockDefinition u = UnlockCatalog.Find(d.Id.ToString());
                Assert.IsNotNull(u, "roster silahı katalogda: " + d.Id);
                Assert.AreEqual(UnlockContentType.Weapon, u.Type);
                Assert.AreEqual(UnlockCondition.Level, u.Condition);
                Assert.AreEqual(d.UnlockLevel(config), u.NumericKey, "katalog seviyesi = roster");
                Assert.AreEqual(ProfileService.UnlockLevelFor(d.Id), u.NumericKey, "katalog seviyesi = merdiven (çelişki yok): " + d.Id);
                derived++;
            }

            Assert.Greater(derived, 0);

            // Kullanıcı kararı (DEC-083, S-02 yazar): A/B/C etkin, D devre dışı → D için katalog girdisi yok;
            // A varsayılan (Default kind) girdi gerektirmez, B/C seviye koşullu.
            foreach (ClassicWeaponDefinition d in WeaponRoster.All)
            {
                if (d == null || d.Enabled || !d.HasRuntimeId) continue;
                Assert.IsNull(UnlockCatalog.Find(d.Id.ToString()), "devre dışı roster silahı katalogda olmamalı: " + d.RosterKey);
            }

            // Aynı transaction: merdiven açar, katalog aynı id'yi ikinci kez 'new' yapmaz.
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition>());
            ProfileData p = new ProfileData { ProfileLineageId = Soy, ProfileLevel = 99 };
            p.Sanitize();
            var yeni = new List<ProfileUnlockEntry>();
            ProfileProgression.GrantUnlocksForLevel(p, ProfileService.Ladder, yeni);
            var katalog = new List<UnlockDefinition>();
            UnlockCatalog.Grant(p, UnlockCatalog.Entries, katalog);
            foreach (UnlockDefinition u in katalog)
            {
                Assert.AreNotEqual(UnlockContentType.Weapon, u.Type, "silah merdivenden zaten açık; katalog tekrarlamaz: " + u.Id);
            }

            // Dükkân dal kapısı: silah açılışı dal sunumunu değiştirmez (roster çekirdek dal kümesi).
            foreach (ClassicWeaponDefinition d in roster)
            {
                if (!d.HasRuntimeId) continue;
                foreach (UpgradeBranch b in (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch)))
                {
                    bool once = WeaponRoster.IsBranchOffered(d.Id, b);
                    Assert.AreEqual(once, WeaponRoster.IsBranchOffered(d.Id, b), "kapı deterministik");
                    if (!WeaponBranchContract.IsSharedPlayer(b) && !d.HasCoreBranch(b))
                    {
                        Assert.IsFalse(once, d.Id + " için çekirdek dışı dal sunulmaz: " + b);
                    }
                }
            }

            // Bir kez daha türetme: id tekrarı yok.
            var list = new List<UnlockDefinition>(UnlockCatalog.Entries);
            int before = list.Count;
            UnlockCatalog.AppendRosterWeapons(list);
            Assert.AreEqual(before, list.Count, "mevcut id'ler yeniden eklenmez");
        }

        // ------------------------------------------------------------ Results ekonomi alanları

        [Test]
        public void RunResultsData_EkonomiAlanlari_OzettenDolar_ProfileGirmez()
        {
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition>
            {
                Tanim("e_gold", AchievementCondition.GoldEarned, 100, AchievementScope.SingleRun),
                Tanim("e_upg", AchievementCondition.UpgradesPurchased, 2, AchievementScope.SingleRun)
            });
            UnlockCatalog.TestSeam.Override(new List<UnlockDefinition>());
            RunRewardReport r = ProfileService.ApplyRun(Ozet(3, 5, 5, 40, gold: 120, spentUpg: 60, spentReroll: 10, upgrades: 2, rerolls: 1));
            Assert.IsTrue(r.Applied);
            RunResultsData d = ProfileService.LastResults;
            Assert.AreEqual(120, d.GoldEarned); Assert.AreEqual(70, d.GoldSpent); Assert.AreEqual(60, d.GoldSpentOnUpgrades);
            Assert.AreEqual(10, d.GoldSpentOnRerolls); Assert.AreEqual(50, d.GoldUnspent); Assert.AreEqual(2, d.UpgradesPurchased); Assert.AreEqual(1, d.RerollsUsed);
            Assert.AreEqual(2, d.NewAchievements.Count, "ekonomi olayları başarım motoruna bağlı (tek koşu)");
            string json = File.ReadAllText(TestPath);
            StringAssert.DoesNotContain("Gold", json, "gold profile girmez (REQ-018)");
            StringAssert.DoesNotContain("Upgrades", json);
        }
    }
}
