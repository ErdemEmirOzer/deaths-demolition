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
    /// WP-023: başarım kural motoru, unlock kataloğu, Run Results veri modeli, şema v4 göçü ve
    /// crash-safe / exactly-once davranışı. İzole profil yolu; gerçek profil yalnız kopya olarak okunur.
    /// </summary>
    [TestFixture]
    [Category("WP023")]
    public sealed class AchievementsSystemTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp023", "prototype-profile.json");

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
            TryDeleteDir(Path.GetDirectoryName(TestPath));
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path, "override kayboldu — gerçek profile yazılabilir");
            ProfileStore.SaveStageHook = null;
            AchievementCatalog.TestSeam.Override(null);
            UnlockCatalog.TestSeam.Override(null);
            AchievementRunTracker.Clear();
            ProfileService.ResetProfile();
            Assert.IsFalse(ProfileStore.SaveLocked, "fixture kilitli başlamamalı");
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.SaveStageHook = null;
            AchievementCatalog.TestSeam.Override(null);
            UnlockCatalog.TestSeam.Override(null);
            AchievementRunTracker.Clear();
        }

        // ------------------------------------------------------------------ yardımcılar

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
            if (!File.Exists(path))
            {
                return "MISSING";
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
            }
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
        }

        private static string Kimlik()
        {
            return CompletionAuthority.TestSeam.IssueForNextRun();
        }

        private static RunSummary Ozet(string id, int reached, int completed, int kills, int killXp, int roundXp,
            int gold = 0, int upgrades = 0, int armor = 0, int ultra = 0)
        {
            return new RunSummary
            {
                CompletionId = id,
                ReachedRound = reached,
                CompletedRounds = completed,
                TotalKills = kills,
                StandardKills = kills - ultra,
                UltraKills = ultra,
                KillXp = killXp,
                CompletedRoundXp = roundXp,
                TotalXpEarned = killXp + roundXp,
                ArmorPiecesBroken = armor,
                GoldEarned = gold,
                GoldSpent = gold / 2,
                GoldUnspent = gold - gold / 2,
                UpgradesPurchased = upgrades,
                Weapon = ClassicWeaponId.AssaultRifle,
                BuildDescription = "P1",
                EndReason = RunEndReason.PlayerDeath,
                HighestTierKilled = ultra > 0 ? EnemyTier.Ultra : EnemyTier.Standard
            };
        }

        private static AchievementDefinition Tanim(string id, AchievementCondition c, int threshold,
            AchievementScope scope = AchievementScope.Lifetime, EnemyTier tier = EnemyTier.Standard, AchievementRarity rarity = AchievementRarity.Common)
        {
            return new AchievementDefinition { Id = id, DisplayName = id, Description = id + " desc", Condition = c, Threshold = threshold, Scope = scope, TargetTier = tier, Rarity = rarity };
        }

        private static ProfileData Profil()
        {
            var p = new ProfileData { ProfileLineageId = Soy };
            p.Sanitize();
            return p;
        }

        // ------------------------------------------------------------------ katalog

        [Test]
        public void Katalog_ResourcesTanTamYuklenir_KimliklerTekil_OynanisEtkisiYok()
        {
            IReadOnlyList<AchievementDefinition> defs = AchievementCatalog.Definitions;
            Assert.GreaterOrEqual(defs.Count, 24, "ilk set ≈ 24 başarım");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (AchievementDefinition d in defs)
            {
                Assert.IsTrue(d.IsUsable, d.Id);
                Assert.IsTrue(ids.Add(d.Id), "tekrar eden id: " + d.Id);
                Assert.GreaterOrEqual(d.Threshold, 1);
                Assert.IsFalse(string.IsNullOrEmpty(d.DisplayName));
            }

            // WP-005 kimlikleri korunur: mevcut profillerde kazanılmış olanlar eşleşmeye devam eder.
            foreach (string legacy in new[] { "first_blood", "armor_breaker", "reach_round_5", "defeat_an_ultra" })
            {
                Assert.IsNotNull(AchievementCatalog.Find(legacy), legacy);
            }

            // Sunum şekli (UI sözleşmesi) aynı listeyi taşır.
            Assert.AreEqual(defs.Count, ProfileService.AllAchievements.Count);
            Assert.AreEqual(defs[0].Id, ProfileService.AllAchievements[0].Id);

            // Tanım yalnız sayaç + eşik taşır; hiçbir alan oynanış değeri değildir (DEC-007). RequiresEffect (WP-077 B4)
            // yalnız görünürlük kapısıdır: ağaç etkisi canlı değilken başarım listelenmez.
            FieldInfo[] alanlar = typeof(AchievementDefinition).GetFields(BindingFlags.Public | BindingFlags.Instance);
            CollectionAssert.AreEquivalent(new[] { "Id", "DisplayName", "Description", "Condition", "Threshold", "TargetTier", "Scope", "Rarity", "RequiresEffect", "Feat" },
                Array.ConvertAll(alanlar, f => f.Name));
        }

        [Test]
        public void Katalog_Parse_GecersizGirdilerDuser_KalanYuklenir()
        {
            string json = "{\"Version\":1,\"Definitions\":[" +
                          "{\"Id\":\"ok1\",\"Condition\":\"TotalKills\",\"Threshold\":3}," +
                          "{\"Id\":\"\",\"Condition\":\"TotalKills\"}," +
                          "{\"Id\":\"ok1\",\"Condition\":\"TotalKills\"}," +
                          "{\"Id\":\"bad_cond\",\"Condition\":\"Nope\"}," +
                          "{\"Id\":\"bad_tier\",\"Condition\":\"TierKills\",\"TargetTier\":\"Boss\"}," +
                          "{\"Id\":\"bad_scope\",\"Condition\":\"RunsPlayed\",\"Scope\":\"SingleRun\"}," +
                          "{\"Id\":\"bad_gold\",\"Condition\":\"GoldEarned\",\"Scope\":\"Lifetime\"}," +
                          "{\"Id\":\"ok2\",\"Condition\":\"TierKills\",\"TargetTier\":\"Ultra\",\"Threshold\":0,\"Scope\":\"SingleRun\",\"Rarity\":\"Epic\"}]}";
            for (int i = 0; i < 6; i++)
            {
                LogAssert.Expect(LogType.Warning, new Regex("dropped"));
            }

            int dropped;
            List<AchievementDefinition> list = AchievementCatalog.Parse(json, out dropped);
            Assert.AreEqual(6, dropped);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("ok1", list[0].Id);
            Assert.AreEqual(3, list[0].Threshold);
            Assert.AreEqual("ok2", list[1].Id);
            Assert.AreEqual(1, list[1].Threshold, "eşik < 1 → 1");
            Assert.AreEqual(EnemyTier.Ultra, list[1].TargetTier);
            Assert.AreEqual(AchievementScope.SingleRun, list[1].Scope);
            Assert.AreEqual(AchievementRarity.Epic, list[1].Rarity);

            int d2;
            Assert.AreEqual(0, AchievementCatalog.Parse(string.Empty, out d2).Count);
            LogAssert.Expect(LogType.Warning, new Regex("could not be parsed"));
            Assert.AreEqual(0, AchievementCatalog.Parse("{{{", out d2).Count);
        }

        // -------------------------------------------------------------------- motor

        [Test]
        public void Motor_HerKosul_OmurBoyuEsikte_Kazanilir()
        {
            var defs = new List<AchievementDefinition>
            {
                Tanim("a_kills", AchievementCondition.TotalKills, 10),
                Tanim("a_ultra", AchievementCondition.TierKills, 2, tier: EnemyTier.Ultra),
                Tanim("a_round", AchievementCondition.ReachRound, 4),
                Tanim("a_rounds", AchievementCondition.RoundsCompleted, 3),
                Tanim("a_armor", AchievementCondition.ArmorPiecesBroken, 2),
                Tanim("a_special", AchievementCondition.SpecialWeaponPickups, 1),
                Tanim("a_flawless", AchievementCondition.FlawlessRounds, 2),
                Tanim("a_runs", AchievementCondition.RunsPlayed, 2),
                Tanim("a_level", AchievementCondition.ProfileLevel, 2)
            };

            ProfileData p = Profil();
            p.HighestRound = 4; p.RoundsCompleted = 3; p.TotalKills = 10; p.StandardKills = 8; p.UltraKills = 2;
            p.ArmorPiecesBroken = 2; p.SpecialWeaponPickups = 1; p.FlawlessRounds = 2;
            p.RunsPlayed = 2; p.ProfileLevel = 2;
            var input = new RunRewardInput(Ozet("x", 1, 0, 0, 0, 0));
            var earned = new List<AchievementDefinition>();
            AchievementEngine.Evaluate(p, input, RunFeatCounters.None, defs, earned);
            Assert.AreEqual(defs.Count, earned.Count, "her koşul tam eşikte kazanılır");
            Assert.AreEqual(0, AchievementEngine.LifetimeValue(p, Tanim("g", AchievementCondition.GoldEarned, 1)), "gold'un ömür boyu değeri yok (REQ-018)");
            Assert.AreEqual(defs.Count, p.Achievements.Count);

            // Eşiğin bir altı: hiçbiri.
            ProfileData q = Profil();
            q.HighestRound = 3; q.RoundsCompleted = 2; q.TotalKills = 9; q.StandardKills = 8; q.UltraKills = 1;
            q.ArmorPiecesBroken = 1; q.SpecialWeaponPickups = 0; q.FlawlessRounds = 1;
            q.RunsPlayed = 1; q.ProfileLevel = 1;
            earned.Clear();
            AchievementEngine.Evaluate(q, input, RunFeatCounters.None, defs, earned);
            Assert.AreEqual(0, earned.Count);
            Assert.AreEqual(0, q.Achievements.Count);

            // İkinci değerlendirme aynı profilde yeni bir şey üretmez (idempotan).
            earned.Clear();
            AchievementEngine.Evaluate(p, input, RunFeatCounters.None, defs, earned);
            Assert.AreEqual(0, earned.Count);
        }

        [Test]
        public void Motor_TekKosuKapsami_YalnizBuKosuyuOkur()
        {
            var defs = new List<AchievementDefinition>
            {
                Tanim("r_kills", AchievementCondition.TotalKills, 40, AchievementScope.SingleRun),
                Tanim("r_flawless", AchievementCondition.FlawlessRounds, 2, AchievementScope.SingleRun),
                Tanim("r_gold", AchievementCondition.GoldEarned, 50, AchievementScope.SingleRun)
            };

            // Ömür boyu sayaçlar eşiğin çok üstünde, bu koşu altında → kazanılmaz.
            ProfileData p = Profil();
            p.TotalKills = 500; p.StandardKills = 500; p.FlawlessRounds = 10;
            var az = new RunRewardInput(Ozet("x", 3, 2, 39, 39, 10));
            var earned = new List<AchievementDefinition>();
            AchievementEngine.Evaluate(p, az, new RunFeatCounters { FlawlessRounds = 1, GoldEarned = 49 }, defs, earned);
            Assert.AreEqual(0, earned.Count);

            var cok = new RunRewardInput(Ozet("y", 3, 2, 40, 40, 10));
            AchievementEngine.Evaluate(p, cok, new RunFeatCounters { FlawlessRounds = 2, GoldEarned = 50 }, defs, earned);
            Assert.AreEqual(3, earned.Count);
            Assert.AreEqual(40, AchievementEngine.RunValue(cok, RunFeatCounters.None, defs[0]));
            Assert.AreEqual(0, AchievementEngine.RunValue(cok, RunFeatCounters.None, Tanim("z", AchievementCondition.RunsPlayed, 1)), "RunsPlayed'in koşu değeri yok");
        }

        // -------------------------------------------------------------- transaction

        [Test]
        public void Transaction_BasarimVeIcerik_AyniYazimda_ExactlyOnce()
        {
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition>
            {
                Tanim("t_kill", AchievementCondition.TotalKills, 5, rarity: AchievementRarity.Rare),
                Tanim("t_gold", AchievementCondition.GoldEarned, 30, AchievementScope.SingleRun)
            });
            UnlockCatalog.TestSeam.Override(new List<UnlockDefinition>
            {
                new UnlockDefinition { Id = "title_x", Type = UnlockContentType.Cosmetic, DisplayName = "Title X", Detail = "d", Condition = UnlockCondition.Achievement, Key = "t_kill" },
                new UnlockDefinition { Id = "ind_y", Type = UnlockContentType.Indicator, DisplayName = "Ind Y", Detail = "d", Condition = UnlockCondition.Round, Key = "3", NumericKey = 3 },
                new UnlockDefinition { Id = "title_z", Type = UnlockContentType.Cosmetic, DisplayName = "Title Z", Detail = "d", Condition = UnlockCondition.Level, Key = "9", NumericKey = 9 }
            });

            RunSummary o = Ozet(Kimlik(), 3, 2, 5, 5, 20, gold: 40, upgrades: 1);
            RunRewardReport r = ProfileService.ApplyRun(o, new RunFeatCounters { SpecialWeaponPickups = 1, FlawlessRounds = 1 });
            Assert.IsTrue(r.Applied);
            CollectionAssert.AreEqual(new[] { "t_kill", "t_gold" }, Array.ConvertAll(new List<AchievementDefinition>(r.EarnedDefinitions).ToArray(), d => d.Id));
            Assert.AreEqual(2, r.NewAchievements.Count);
            CollectionAssert.AreEqual(new[] { "title_x", "ind_y" }, Array.ConvertAll(new List<UnlockDefinition>(r.OpenedContent).ToArray(), u => u.Id), "başarımla açılan içerik aynı transaction'da");
            Assert.AreEqual(r.OpenedContent.Count, r.NewUnlocks.Count, "L1'de merdiven unlock'u yok; NewUnlocks = katalog içeriği");

            ProfileData p = ProfileService.Current;
            Assert.AreEqual(40, r.Feats.GoldEarned, "tek koşu gold sayacı makbuzda");
            Assert.AreEqual(1, r.Feats.UpgradesPurchased);
            StringAssert.DoesNotContain("Gold", File.ReadAllText(TestPath), "gold profile yazılmaz (REQ-018)");
            Assert.AreEqual(1, p.SpecialWeaponPickups);
            Assert.AreEqual(1, p.FlawlessRounds);
            CollectionAssert.AreEquivalent(new[] { "title_x", "ind_y" }, p.UnlockedContent);
            Assert.IsTrue(ProfileService.IsContentUnlocked("title_x"));
            Assert.IsFalse(ProfileService.IsContentUnlocked("title_z"));
            Assert.IsFalse(ProfileService.IsContentUnlocked("unknown"));

            // Diskte de aynı (tek yazım).
            ProfileData disk;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out disk));
            CollectionAssert.AreEquivalent(new[] { "t_kill", "t_gold" }, disk.Achievements);
            CollectionAssert.AreEquivalent(new[] { "title_x", "ind_y" }, disk.UnlockedContent);
            Assert.AreEqual(ProfileData.CurrentVersion, disk.SaveVersion);

            // Aynı koşu ikinci kez: orijinal makbuz ya da Duplicate; hiçbir şey ikinci kez açılmaz.
            string once = JsonUtility.ToJson(disk);
            RunRewardReport r2 = ProfileService.ApplyRun(o, new RunFeatCounters { SpecialWeaponPickups = 5, FlawlessRounds = 5 });
            Assert.IsTrue(ReferenceEquals(r2, r) || r2.Outcome == ProfileCommitOutcome.Duplicate);
            ProfileData disk2;
            ProfileStore.ReadFile(TestPath, out disk2);
            Assert.AreEqual(once, JsonUtility.ToJson(disk2), "replay diske dokunmaz");
            Assert.AreEqual(1, ProfileService.Current.SpecialWeaponPickups, "replay feat'leri ikinci kez saymaz");

            // Yeniden yükleme + yeni koşu: kazanılmışlar yeniden 'new' olmaz.
            YenidenYukle();
            RunRewardReport r3 = ProfileService.ApplyRun(Ozet(Kimlik(), 1, 0, 1, 1, 0));
            Assert.IsTrue(r3.Applied);
            Assert.AreEqual(0, r3.NewAchievements.Count);
            Assert.AreEqual(0, r3.OpenedContent.Count);
        }

        [Test]
        public void Transaction_SaveBasarisiz_HicbirSeyAcilmaz_RetryTamBirKez()
        {
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition> { Tanim("c_kill", AchievementCondition.TotalKills, 1) });
            UnlockCatalog.TestSeam.Override(new List<UnlockDefinition>
            {
                new UnlockDefinition { Id = "c_title", Type = UnlockContentType.Cosmetic, DisplayName = "T", Condition = UnlockCondition.Achievement, Key = "c_kill" }
            });

            RunSummary o = Ozet(Kimlik(), 2, 1, 3, 3, 20, gold: 10);
            ProfileStore.SaveStageHook = s => { if (s == ProfileSaveStage.Replace) throw new IOException("injected"); };
            // WP-063: günlük yalnız aşama + istisna türünü yazar (istisna metni yol/OS ayrıntısı sızdırabilir).
            LogAssert.Expect(LogType.Warning, new Regex("Save failed at Replace \\(IOException\\)"));
            LogAssert.Expect(LogType.Warning, new Regex("Profile not saved"));
            RunRewardReport fail = ProfileService.ApplyRun(o, new RunFeatCounters { SpecialWeaponPickups = 1 });
            ProfileStore.SaveStageHook = null;

            Assert.AreEqual(ProfileCommitOutcome.SaveFailed, fail.Outcome);
            Assert.AreEqual(0, fail.NewAchievements.Count);
            Assert.AreEqual(0, fail.EarnedDefinitions.Count);
            Assert.AreEqual(0, fail.OpenedContent.Count);
            Assert.AreEqual(0, ProfileService.Current.Achievements.Count, "başarısız yazımda RAM değişmez");
            Assert.AreEqual(0, ProfileService.Current.UnlockedContent.Count);
            Assert.AreEqual(0, ProfileService.Current.SpecialWeaponPickups);
            Assert.IsFalse(ProfileService.LastResults.Applied);
            Assert.AreEqual(0, ProfileService.LastResults.NewAchievements.Count);

            // Aynı token retry → tam bir kez uygulanır.
            RunRewardReport ok = ProfileService.ApplyRun(o, new RunFeatCounters { SpecialWeaponPickups = 1 });
            Assert.IsTrue(ok.Applied);
            Assert.AreEqual(1, ok.EarnedDefinitions.Count);
            Assert.AreEqual(1, ok.OpenedContent.Count);
            Assert.AreEqual(1, ProfileService.Current.SpecialWeaponPickups);
            Assert.AreEqual(10, ok.Feats.GoldEarned);

            RunRewardReport dup = ProfileService.ApplyRun(o, new RunFeatCounters { SpecialWeaponPickups = 1 });
            Assert.IsTrue(ReferenceEquals(dup, ok) || dup.Outcome == ProfileCommitOutcome.Duplicate);
            Assert.AreEqual(1, ProfileService.Current.SpecialWeaponPickups, "çökme sonrası yeniden uygulama idempotan");
        }

        [Test]
        public void Tracker_FeatlerTokenaBagli_BaskaKosuyaSizmaz()
        {
            string token = Kimlik();
            AchievementRunTracker.TestSeam.ArmFor(token, new RunFeatCounters { SpecialWeaponPickups = 2, FlawlessRounds = 1 });
            Assert.AreEqual(2, AchievementRunTracker.CountersFor(token).SpecialWeaponPickups);
            Assert.AreEqual(0, AchievementRunTracker.CountersFor("ddc1:other").SpecialWeaponPickups, "başka kimlik: hiçbir feat");
            Assert.AreEqual(0, AchievementRunTracker.CountersFor(null).FlawlessRounds);

            // Servis, özetin kimliğiyle eşleşen sayaçları kendisi alır.
            RunRewardReport r = ProfileService.ApplyRun(Ozet(token, 2, 1, 1, 1, 10));
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(2, r.Feats.SpecialWeaponPickups);
            Assert.AreEqual(2, ProfileService.Current.SpecialWeaponPickups);
            Assert.AreEqual(1, ProfileService.Current.FlawlessRounds);

            // Yeni koşu başlangıcı sayaçları sıfırlar.
            string token2 = Kimlik();
            AchievementRunTracker.Begin(token2);
            Assert.AreEqual(0, AchievementRunTracker.CountersFor(token2).SpecialWeaponPickups);
            Assert.AreEqual(0, AchievementRunTracker.CountersFor(token).SpecialWeaponPickups, "eski token artık bağlı değil");
        }

        // ------------------------------------------------------------ unlock kataloğu

        [Test]
        public void UnlockKatalog_ResourcesTanYuklenir_KosulTurleri_SilahDalBosSablon()
        {
            IReadOnlyList<UnlockDefinition> entries = UnlockCatalog.Entries;
            Assert.GreaterOrEqual(entries.Count, 6);
            int cosmetics = 0;
            foreach (UnlockDefinition e in entries) { if (e.Type == UnlockContentType.Title || e.Type == UnlockContentType.Badge) cosmetics++; }
            Assert.GreaterOrEqual(cosmetics, 6, "unvan/rozet girdileri");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnlockDefinition e in entries)
            {
                Assert.IsTrue(ids.Add(e.Id), e.Id);
                // WP-035: JSON yalnız unvan/rozet/gösterge taşır; silah girdileri roster'dan türetilir (Weapon), dal girdisi yok.
                Assert.IsTrue(e.Type == UnlockContentType.Title || e.Type == UnlockContentType.Badge ||
                              e.Type == UnlockContentType.Indicator || e.Type == UnlockContentType.Weapon, "beklenmeyen tür: " + e.Id);
                Assert.AreNotEqual(UnlockContentType.Branch, e.Type, "dal girdisi yok: " + e.Id);
                if (e.Type == UnlockContentType.Weapon)
                {
                    Assert.AreEqual(UnlockCondition.Level, e.Condition, "roster silahı seviye koşullu: " + e.Id);
                    Assert.IsTrue(DD.Prototype.Core.Weapons.WeaponIdMigration.TryGetRuntimeId(e.Id, out _), "WP-077 v5 silah anahtarı: " + e.Id);
                }

                if (e.Condition == UnlockCondition.Achievement)
                {
                    Assert.IsNotNull(AchievementCatalog.Find(e.Key), e.Id + " → tanımsız başarım " + e.Key);
                }
                else
                {
                    Assert.GreaterOrEqual(e.NumericKey, 1, e.Id);
                }
            }

            // Skinler katalogda yer almaz (DEC-018: baştan açık).
            foreach (UnlockDefinition e in entries)
            {
                StringAssert.DoesNotContain("skin", e.Id.ToLowerInvariant());
            }
        }

        [Test]
        public void UnlockKatalog_Parse_TurVeKosulDogrulanir_SilahAdiEnumOlmali()
        {
            string json = "{\"Version\":1,\"Entries\":[" +
                          "{\"Id\":\"AutoSmg\",\"Type\":\"Weapon\",\"Condition\":\"Achievement\",\"Key\":\"first_blood\"}," +
                          "{\"Id\":\"NotAWeapon\",\"Type\":\"Weapon\",\"Condition\":\"Achievement\",\"Key\":\"first_blood\"}," +
                          "{\"Id\":\"Piercing\",\"Type\":\"Branch\",\"Condition\":\"Level\",\"Key\":\"3\"}," +
                          "{\"Id\":\"bad_key\",\"Type\":\"Cosmetic\",\"Condition\":\"Round\",\"Key\":\"zero\"}," +
                          "{\"Id\":\"bad_type\",\"Type\":\"Skin\",\"Condition\":\"Level\",\"Key\":\"1\"}," +
                          "{\"Id\":\"ok\",\"Type\":\"Indicator\",\"Condition\":\"Round\",\"Key\":\"7\"}]}";
            for (int i = 0; i < 3; i++)
            {
                LogAssert.Expect(LogType.Warning, new Regex("dropped"));
            }

            int dropped;
            List<UnlockDefinition> list = UnlockCatalog.Parse(json, out dropped);
            Assert.AreEqual(3, dropped);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(UnlockContentType.Weapon, list[0].Type);
            Assert.AreEqual(UnlockContentType.Branch, list[1].Type);
            Assert.AreEqual(3, list[1].NumericKey);
            Assert.AreEqual(7, list[2].NumericKey);

            // Silah/dal girdisi merdivenle aynı listelere adıyla yazılır (entitlement kapısı aynı).
            ProfileData p = Profil();
            p.Achievements.Add("first_blood");
            p.ProfileLevel = 3;
            var yeni = new List<UnlockDefinition>();
            UnlockCatalog.Grant(p, list, yeni);
            Assert.AreEqual(2, yeni.Count);
            Assert.IsTrue(ProfileProgression.IsWeaponEntitled(p, ClassicWeaponId.AutoSmg));
            Assert.IsTrue(ProfileProgression.IsBranchEntitled(p, UpgradeBranch.Piercing));
            Assert.IsFalse(p.Has(p.UnlockedContent, "ok"), "round 7 yok");
            yeni.Clear();
            UnlockCatalog.Grant(p, list, yeni);
            Assert.AreEqual(0, yeni.Count, "ek yalnız bir kez 'new'");
        }

        // ------------------------------------------------------------ results verisi

        [Test]
        public void RunResultsData_DEC036Alanlari_TamVeSalt()
        {
            AchievementCatalog.TestSeam.Override(new List<AchievementDefinition> { Tanim("d_ultra", AchievementCondition.TierKills, 1, tier: EnemyTier.Ultra, rarity: AchievementRarity.Epic) });
            UnlockCatalog.TestSeam.Override(new List<UnlockDefinition>
            {
                new UnlockDefinition { Id = "d_title", Type = UnlockContentType.Cosmetic, DisplayName = "Title D", Detail = "det", Condition = UnlockCondition.Achievement, Key = "d_ultra" }
            });
            Assert.IsFalse(ProfileService.LastResults.HasRun, "koşu öncesi boş model");
            Assert.AreSame(ProfileService.LastResults, ProfileService.ResultsSource.Current);

            RunSummary o = Ozet(Kimlik(), 4, 3, 12, 30, 90, gold: 70, upgrades: 2, armor: 3, ultra: 1);
            o.RunDuration = 61.5f;
            o.KilledBy = "zombie";
            RunRewardReport r = ProfileService.ApplyRun(o, new RunFeatCounters { SpecialWeaponPickups = 1, FlawlessRounds = 2 });
            Assert.IsTrue(r.Applied);
            RunResultsData d = ProfileService.LastResults;
            Assert.AreSame(d, ProfileService.ResultsSource.Current);
            Assert.IsTrue(d.HasRun && d.Applied);
            Assert.AreEqual(4, d.ReachedRound); Assert.AreEqual(3, d.CompletedRounds);
            Assert.AreEqual(12, d.TotalKills); Assert.AreEqual(11, d.StandardKills); Assert.AreEqual(1, d.UltraKills);
            Assert.AreEqual(EnemyTier.Ultra, d.HighestTierKilled); Assert.AreEqual(3, d.ArmorPiecesBroken);
            Assert.AreEqual(1, d.SpecialWeaponPickups); Assert.AreEqual(2, d.FlawlessRounds);
            Assert.AreEqual(70, d.GoldEarned); Assert.AreEqual(35, d.GoldSpent); Assert.AreEqual(35, d.GoldUnspent); Assert.AreEqual(2, d.UpgradesPurchased);
            Assert.AreEqual(30, d.KillXp); Assert.AreEqual(90, d.CompletedRoundXp); Assert.AreEqual(120, d.TotalXpEarned);
            Assert.AreEqual(1, d.LevelBefore); Assert.AreEqual(2, d.LevelAfter); Assert.IsTrue(d.LeveledUp);
            Assert.AreEqual(r.LevelRequirementAfter, d.LevelRequirementAfter);
            Assert.AreEqual(61.5f, d.RunDurationSeconds); Assert.AreEqual("zombie", d.KilledBy); Assert.AreEqual(RunEndReason.PlayerDeath, d.EndReason);
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, d.Weapon); Assert.AreEqual("P1", d.BuildDescription);
            Assert.IsTrue(d.IsNewHighestRound); Assert.IsTrue(d.IsBestRunXp);
            Assert.AreEqual(1, d.LifetimeRunsPlayed); Assert.AreEqual(4, d.LifetimeHighestRound); Assert.AreEqual(12, d.LifetimeTotalKills);
            Assert.AreEqual(1, d.NewAchievements.Count);
            Assert.AreEqual("d_ultra", d.NewAchievements[0].Id); Assert.AreEqual(AchievementRarity.Epic, d.NewAchievements[0].Rarity);
            Assert.AreEqual(r.NewUnlocks.Count, d.NewUnlocks.Count, "merdiven + katalog birlikte");
            RunResultsUnlock son = d.NewUnlocks[d.NewUnlocks.Count - 1];
            Assert.AreEqual("d_title", son.Id); Assert.AreEqual(UnlockContentType.Cosmetic, son.Type); Assert.AreEqual("Title D", son.Title);
            Assert.IsTrue(d.HasRewards);
            Assert.AreEqual(string.Empty, d.RecordNotice);

            // Listeler salt-okunur; model profile referans taşımaz.
            Assert.IsFalse(d.NewAchievements is List<RunResultsAchievement>);
            Assert.IsFalse(d.NewUnlocks is List<RunResultsUnlock>);

            // Sonraki daha kısa koşu: "new best" yok; kayıt yine tam.
            RunResultsData d2 = ProfileService.ApplyRun(Ozet(Kimlik(), 2, 1, 1, 1, 0)) != null ? ProfileService.LastResults : null;
            Assert.IsFalse(d2.IsNewHighestRound); Assert.IsFalse(d2.IsBestRunXp);
            Assert.AreEqual(2, d2.LifetimeRunsPlayed);

            ProfileService.ClearLastReport();
            Assert.IsFalse(ProfileService.LastResults.HasRun);
        }

        [Test]
        public void RunResultsData_KaydedilmeyenKosu_KosuGorunur_OdulYok()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no completion identity"));
            RunRewardReport r = ProfileService.ApplyRun(Ozet(string.Empty, 3, 2, 9, 9, 40, gold: 12));
            Assert.AreEqual(ProfileCommitOutcome.MissingIdentity, r.Outcome);
            RunResultsData d = ProfileService.LastResults;
            Assert.IsTrue(d.HasRun);
            Assert.IsFalse(d.Applied);
            Assert.AreEqual(ProfileCommitOutcome.MissingIdentity, d.Outcome);
            Assert.AreEqual(3, d.ReachedRound); Assert.AreEqual(9, d.TotalKills); Assert.AreEqual(12, d.GoldEarned);
            Assert.AreEqual(0, d.NewAchievements.Count); Assert.AreEqual(0, d.NewUnlocks.Count);
            Assert.IsFalse(d.IsNewHighestRound);
            StringAssert.Contains("not recorded", d.RecordNotice);
            Assert.AreEqual(1, d.LevelBefore); Assert.AreEqual(1, d.LevelAfter);
        }

        // ------------------------------------------------------------------ şema v4

        private static string V3Belge(int runs, int ordinal, string ach)
        {
            return "{\"SaveVersion\":3,\"ProfileLevel\":2,\"XpIntoLevel\":40,\"TotalXpEarned\":140,\"HighestRound\":3,\"RunsPlayed\":" + runs +
                   ",\"RoundsCompleted\":5,\"TotalKills\":31,\"StandardKills\":29,\"SpecialKills\":0,\"EliteKills\":2,\"UltraKills\":0,\"UnattributedKills\":0," +
                   "\"ArmorPiecesBroken\":2,\"BestRunXp\":80,\"Achievements\":[" + ach + "],\"UnlockedWeapons\":[\"AssaultRifle\",\"AutoSmg\"]," +
                   "\"UnlockedBranches\":[\"Piercing\"],\"ProfileLineageId\":\"" + Soy + "\",\"LastAppliedCompletionOrdinal\":" + ordinal +
                   ",\"LastSavedUtc\":\"2026-09-14T00:00:00.0000000Z\"}";
        }

        [Test]
        public void SemaV4_V3Belge_KayipsizGoc_LineageOrdinalAynen_Idempotan_ReplayReddi()
        {
            File.WriteAllText(TestPath, V3Belge(11, 7, "\"first_blood\",\"reach_round_5\""));
            YenidenYukle();
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.LastLoadStatus);
            Assert.AreEqual(ProfileData.CurrentVersion, p.SaveVersion);
            Assert.AreEqual(2, p.ProfileLevel); Assert.AreEqual(40, p.XpIntoLevel); Assert.AreEqual(140, p.TotalXpEarned);
            Assert.AreEqual(3, p.HighestRound); Assert.AreEqual(11, p.RunsPlayed); Assert.AreEqual(5, p.RoundsCompleted);
            Assert.AreEqual(31, p.TotalKills); Assert.AreEqual(29, p.StandardKills); Assert.AreEqual(2, p.EliteKills); Assert.AreEqual(0, p.UnattributedKills);
            Assert.AreEqual(2, p.ArmorPiecesBroken); Assert.AreEqual(80, p.BestRunXp);
            CollectionAssert.AreEqual(new[] { "first_blood", "reach_round_5" }, p.Achievements);
            CollectionAssert.AreEqual(new[] { "ar_1", "smg_1" }, p.UnlockedWeapons, "WP-077 v5: M3 silah adları WP-069 anahtarlarına göçer");
            CollectionAssert.IsSubsetOf(new[] { "Piercing" }, p.UnlockedBranches, "seviye merdiveni yükleme sırasında ekleyebilir; dosyadaki kayıp yok");
            ProfileData ham; Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out ham));
            CollectionAssert.AreEqual(new[] { "Piercing" }, ham.UnlockedBranches, "diskteki v3 belge dokunulmadı (ilk save'e kadar)");
            Assert.AreEqual(Soy, p.ProfileLineageId, "lineage aynen");
            Assert.AreEqual(7, p.LastAppliedCompletionOrdinal, "ordinal aynen: uygulanmışlar replay edilemez");
            Assert.AreEqual(0, p.SpecialWeaponPickups); Assert.AreEqual(0, p.FlawlessRounds);
            Assert.AreEqual(0, p.UnlockedContent.Count);
            Assert.IsTrue(p.IsValid());

            // Eski soyun uygulanmış ordinal'leri (1..7) reddedilir; sonraki (8) yalnız canlı yetkiyle.
            RunRewardReport eski = ProfileService.ApplyRun(Ozet(CompletionAuthority.TestSeam.Forge(Soy, 3, CompletionAuthority.TestSeam.NewNonce()), 2, 1, 1, 1, 0));
            Assert.AreEqual(ProfileCommitOutcome.Duplicate, eski.Outcome, "ordinal ≤ yüksek su işareti → uygulanmış sayılır, profil değişmez");
            Assert.AreEqual(CompletionTokenVerdict.AlreadyApplied, ProfileService.LastVerdict);
            Assert.AreEqual(11, ProfileService.Current.RunsPlayed);
            LogAssert.Expect(LogType.Warning, new Regex("not valid"));
            RunRewardReport gap = ProfileService.ApplyRun(Ozet(CompletionAuthority.TestSeam.Forge(Soy, 9, CompletionAuthority.TestSeam.NewNonce()), 2, 1, 1, 1, 0));
            Assert.AreEqual(ProfileCommitOutcome.InvalidIdentity, gap.Outcome, "sıradaki olmayan ordinal (gap) reddedilir");
            Assert.AreEqual(CompletionTokenVerdict.OrdinalGap, ProfileService.LastVerdict);

            // Yazıp yeniden yükleme: v4 belge; ikinci yükleme baytça aynı (LastSavedUtc hariç alanlar).
            Assert.IsTrue(ProfileService.Save());
            ProfileData d1; Assert.AreEqual(ProfileLoadStatus.Ok, ProfileStore.ReadFile(TestPath, out d1));
            string once = JsonUtility.ToJson(d1);
            StringAssert.Contains("\"SaveVersion\":" + ProfileData.CurrentVersion, File.ReadAllText(TestPath).Replace(" ", string.Empty));
            YenidenYukle();
            Assert.AreEqual(once, JsonUtility.ToJson(ProfileService.Current), "idempotan");

            // Yeni koşu ordinal 8 ile uygulanır; sayaçlar taban üstüne eklenir.
            RunRewardReport r = ProfileService.ApplyRun(Ozet(Kimlik(), 1, 0, 2, 2, 0, gold: 5));
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(8, ProfileService.Current.LastAppliedCompletionOrdinal);
            Assert.AreEqual(33, ProfileService.Current.TotalKills);
        }

        [Test]
        public void SemaV4_GercekProfilKopyasi_V3IseKayipsizGoc()
        {
            // Gerçek kullanıcı profili yalnız OKUNUR; kopya izole yolda göçürülür (v1/v2/v3 → v4). Dosya yoksa ya da v4+ ise atlanır.
            if (!File.Exists(RealPath))
            {
                Assert.Ignore("gerçek profil yok");
            }

            string json = File.ReadAllText(RealPath);
            ProfileData ham = JsonUtility.FromJson<ProfileData>(json); // göçsüz ham değerler
            ProfileData dogrulama;
            ProfileLoadStatus st = ProfileMigration.Parse(json, out dogrulama);
            if (st != ProfileLoadStatus.Ok || ham == null || ham.SaveVersion >= ProfileData.CurrentVersion || json.IndexOf("\"SaveVersion\"", StringComparison.Ordinal) < 0)
            {
                Assert.Ignore("gerçek profil göç adayı değil / okunamadı: " + st);
            }

            int hamVersiyon = ham.SaveVersion;
            File.WriteAllText(TestPath, json);
            YenidenYukle();
            ProfileData p = ProfileService.Current;
            Assert.AreEqual(ProfileData.CurrentVersion, p.SaveVersion);
            foreach (string alan in new[] { "ProfileLevel", "XpIntoLevel", "TotalXpEarned", "HighestRound", "RunsPlayed", "RoundsCompleted", "StandardKills", "SpecialKills", "EliteKills", "UltraKills", "ArmorPiecesBroken", "BestRunXp" })
            {
                FieldInfo f = typeof(ProfileData).GetField(alan);
                Assert.AreEqual(f.GetValue(ham), f.GetValue(p), alan);
            }

            Assert.AreEqual(Math.Max(0, ham.TotalKills), p.TotalKills, "toplam kill korunur (fazlası UnattributedKills'e)");
            CollectionAssert.AreEqual(ham.Achievements, p.Achievements);
            var hamAnahtar = new List<string>();   // WP-077 v5: M3 adları WP-069 anahtarlarına göçer
            foreach (string w in ham.UnlockedWeapons) hamAnahtar.Add(DD.Prototype.Core.Weapons.WeaponIdMigration.FromLegacyName(w) ?? w);
            CollectionAssert.IsSubsetOf(hamAnahtar, p.UnlockedWeapons);
            CollectionAssert.IsSubsetOf(ham.UnlockedBranches, p.UnlockedBranches);
            if (hamVersiyon >= 3)
            {
                Assert.AreEqual(ham.ProfileLineageId, p.ProfileLineageId, "v3 lineage aynen");
                Assert.AreEqual(ham.LastAppliedCompletionOrdinal, p.LastAppliedCompletionOrdinal, "v3 ordinal aynen");
            }
            else
            {
                Assert.IsTrue(ProfileData.IsLineageId(p.ProfileLineageId), "v1/v2: lineage üretilir");
                Assert.AreEqual(0, p.LastAppliedCompletionOrdinal);
            }

            Assert.AreEqual(0, p.UnlockedContent.Count);
            Assert.AreEqual(0, p.SpecialWeaponPickups);
            Assert.IsTrue(p.IsValid());
            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek dosyaya dokunulmadı");
            Debug.Log("[DD-TEST] wp023.realcopy from=v" + hamVersiyon + " runs=" + p.RunsPlayed + " kills=" + p.TotalKills);
        }

        [Test]
        public void SemaV4_BozukIcerikListesi_TemizlenirVeDogrulanir()
        {
            ProfileData p = Profil();
            p.UnlockedContent = new List<string> { "a", string.Empty, "a", null, "b" };
            p.SpecialWeaponPickups = -5; p.FlawlessRounds = -1;
            Assert.IsFalse(p.IsValid());
            p.Sanitize();
            CollectionAssert.AreEqual(new[] { "a", "b" }, p.UnlockedContent);
            Assert.AreEqual(0, p.SpecialWeaponPickups); Assert.AreEqual(0, p.FlawlessRounds);
            Assert.IsTrue(p.IsValid());
            ProfileData c = p.Clone();
            c.UnlockedContent.Add("c");
            Assert.AreEqual(2, p.UnlockedContent.Count, "klon derin");
        }

        [Test]
        public void UnlockKatalogu_R1B_P5_EskiAdVeAnahtarTekGirdi()
        {
            // R1-B P5 (C-00 30.09): the duplicate check runs on the normalised id — "AutoSmg" and "smg_1" are one entry.
            string json = "{\"Version\":1,\"Entries\":[" +
                          "{\"Id\":\"AutoSmg\",\"Type\":\"Weapon\",\"Condition\":\"Achievement\",\"Key\":\"first_blood\"}," +
                          "{\"Id\":\"smg_1\",\"Type\":\"Weapon\",\"Condition\":\"Achievement\",\"Key\":\"first_blood\"}]}";
            LogAssert.Expect(LogType.Warning, new Regex("duplicate id 'smg_1'"));
            int dropped;
            List<UnlockDefinition> list = UnlockCatalog.Parse(json, out dropped);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(1, dropped);
            Assert.AreEqual("smg_1", list[0].Id);
        }
    }
}
