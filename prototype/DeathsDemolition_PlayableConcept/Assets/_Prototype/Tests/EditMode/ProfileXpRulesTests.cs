using System;
using System.Collections.Generic;
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
    /// WP-005 · Profil XP kuralları ve kalıcılık sınırı (EditMode, izole dosya).
    ///
    /// Gerçek profil dosyasına ASLA dokunulmaz: fixture başında ProfileStore yolu geçici
    /// önbelleğe yönlendirilir ve ProfileService önbelleği reflection ile boşaltılır;
    /// sonunda önbellek yine boşaltılır ve override kaldırılır (gerçek dosya hiç
    /// yüklenmez, dolayısıyla hiç yazılmaz).
    ///
    /// Kilitlenen kurallar: tamamlanan round round XP'si verir; yarım round vermez ama
    /// kill'leri kill XP'sine ve istatistiğe girer; gold kazanma/toplama/harcama, upgrade
    /// ve süre XP vermez; aynı run iki kez uygulanmaz (çağıran sözleşmesi + ikinci
    /// uygulamanın unlock/achievement üretmemesi); alan alan disk gidiş-dönüşü; run
    /// gold/build dosyaya yazılmaz; eksik/bozuk kayıt güvenli; unlock otomatik, Claim yok.
    /// </summary>
    [TestFixture]
    [Category("WP005")]
    public sealed class ProfileXpRulesTests
    {
        private static readonly string TestPath =
            Path.Combine(Application.temporaryCachePath, "dd-pid-" + System.Diagnostics.Process.GetCurrentProcess().Id, "dd-editmode-wp005-profile.json");

        private static readonly string RealPath =
            Path.Combine(Application.persistentDataPath, ProfileStore.FileName);

        private string _realHashBefore;
        private int _changed;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _realHashBefore = HashOf(RealPath);
            ProfileStore.SetPathOverride(TestPath);
            OnbellegiBosalt();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            OnbellegiBosalt();
            ProfileStore.SetPathOverride(null);
            TryDelete(TestPath);
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(TestPath), Path.GetFileName(TestPath) + "*"))
            {
                TryDelete(f);
            }

            Assert.AreEqual(_realHashBefore, HashOf(RealPath), "gerçek profil dosyası değişmemeli");
        }

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(TestPath, ProfileStore.Path, "override kayboldu — gerçek profile yazılabilir");
            ProfileService.ResetProfile();
            _changed = 0;
            ProfileService.Changed += Sayac;
        }

        [TearDown]
        public void TearDown()
        {
            ProfileService.Changed -= Sayac;
        }

        private void Sayac()
        {
            _changed++;
        }

        // ------------------------------------------------------------------ yardımcılar

        private static void OnbellegiBosalt()
        {
            typeof(ProfileService).GetField("s_data", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            typeof(ProfileService).GetField("s_lastReport", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
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

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }

        private static RunSummary Ozet(int reached, int completed, int kills, int killXp, int roundXp)
        {
            return new RunSummary
            {
                CompletionId = CompletionAuthority.TestSeam.IssueForNextRun(),
                ReachedRound = reached,
                CompletedRounds = completed,
                TotalKills = kills,
                StandardKills = kills,
                KillXp = killXp,
                CompletedRoundXp = roundXp,
                TotalXpEarned = killXp + roundXp,
                Weapon = ClassicWeaponId.AssaultRifle,
                BuildDescription = "P1",
                EndReason = RunEndReason.PlayerDeath
            };
        }

        // ------------------------------------------------------------------ XP kuralları

        [Test]
        public void TamamlananRound_RoundXpVerir()
        {
            RunRewardReport r = ProfileService.ApplyRun(Ozet(2, 1, 0, 0, 20));
            Assert.AreEqual(20, r.CompletedRoundXp);
            Assert.AreEqual(0, r.KillXp);
            Assert.AreEqual(20, r.TotalXpEarned);
            Assert.AreEqual(20, ProfileService.Current.XpIntoLevel);
            Assert.AreEqual(1, ProfileService.Current.RoundsCompleted);
            Assert.AreEqual(1, _changed, "ApplyRun tam bir Changed yayınlar");
        }

        [Test]
        public void YarimRoundKilli_KillXpVeIstatistigeGirer_RoundXpVermez()
        {
            RunRewardReport r = ProfileService.ApplyRun(Ozet(1, 0, 4, 4, 0));
            Assert.AreEqual(0, r.CompletedRoundXp, "ölüm gerçekleşen yarım round round XP'si vermez");
            Assert.AreEqual(4, r.KillXp, "yarım rounddaki kill'ler kill XP'sine girer");
            Assert.AreEqual(4, r.TotalXpEarned);

            ProfileData p = ProfileService.Current;
            Assert.AreEqual(4, p.TotalKills, "kill'ler kalıcı istatistiğe girer");
            Assert.AreEqual(4, p.StandardKills);
            Assert.AreEqual(0, p.RoundsCompleted);
            Assert.AreEqual(1, p.HighestRound, "ulaşılan round kaydedilir");
            Assert.AreEqual(1, p.RunsPlayed);
        }

        [Test]
        public void GoldKazanmaToplamaHarcama_Upgrade_Sure_XpVermez()
        {
            var ozet = Ozet(3, 0, 0, 0, 0);
            ozet.GoldEarned = 999;
            ozet.GoldSpent = 500;
            ozet.GoldUnspent = 499;
            ozet.UpgradesPurchased = 9;
            ozet.RunDuration = 3600f;

            RunRewardReport r = ProfileService.ApplyRun(ozet);
            Assert.AreEqual(0, r.TotalXpEarned, "gold, upgrade ve süre XP üretmez");
            Assert.AreEqual(0, ProfileService.Current.XpIntoLevel);
            Assert.AreEqual(0, ProfileService.Current.TotalXpEarned);
            Assert.AreEqual(1, ProfileService.Current.ProfileLevel);

            // Koşu sayacı da aynı kuralı taşır: gold ve süre TotalXp'e dokunmaz.
            var stats = new RunStatsTracker();
            stats.RecordKill(EnemyTier.Standard, 1);
            int once = stats.TotalXp;
            stats.RecordGoldEarned(1000);
            stats.RecordGoldSpent(400);
            stats.Tick(120f);
            Assert.AreEqual(once, stats.TotalXp, "RunStatsTracker: gold/harcama/süre XP'yi değiştirmez");
            Assert.AreEqual(1000, stats.GoldEarned);
            Assert.AreEqual(1, stats.UpgradesPurchased);
        }

        [Test]
        public void AyniOzet_IkinciKez_UnlockVeAchievementTekrarUretmez()
        {
            // L1→L2: Req(1) XP (WP-077 K4: 110; 5 öldürme XP'si dahil); L2 merdiveni açılır, First Blood kazanılır.
            RunSummary ozet = Ozet(2, 1, 5, 5, ProfileService.LevelRequirement(1) - 5);
            RunRewardReport r1 = ProfileService.ApplyRun(ozet);
            Assert.AreEqual(2, r1.LevelAfter);
            Assert.IsTrue(r1.LeveledUp);
            // WP-049: merdiven açılışları veri-güdümlü (DEC-085 / Ö-3 sonrası B seviyesi değişebilir): L2'de açılan
            // her silah/dal + Title: Blooded (WP-023 kataloğu) beklenir; sayı roster/config verisinden türetilir.
            int beklenenMerdiven = 0;
            foreach (ClassicWeaponId w in (ClassicWeaponId[])System.Enum.GetValues(typeof(ClassicWeaponId))) if (ProfileService.UnlockLevelFor(w) == 2) beklenenMerdiven++;
            foreach (UpgradeBranch b in (UpgradeBranch[])System.Enum.GetValues(typeof(UpgradeBranch))) if (ProfileService.UnlockLevelFor(b) == 2) beklenenMerdiven++;
            Assert.AreEqual(beklenenMerdiven + 1, r1.NewUnlocks.Count, "L2 merdiven açılışları + Title: Blooded (first_blood'a bağlı)");
            Assert.AreEqual(1, r1.NewAchievements.Count, "First Blood");

            int seviye = ProfileService.Current.ProfileLevel;
            int silahlar = ProfileService.Current.UnlockedWeapons.Count;
            int basarimlar = ProfileService.Current.Achievements.Count;
            int xp = ProfileService.Current.TotalXpEarned;
            int kosu = ProfileService.Current.RunsPlayed;
            int kill = ProfileService.Current.TotalKills;
            int changedOnce = _changed;

            // WP-009: aynı completion identity ikinci kez UYGULANMAZ — orijinal makbuz geri döner,
            // hiçbir sayaç artmaz, Changed tekrar yayınlanmaz (kalıcı exactly-once; UI bayrağı değil).
            RunRewardReport r2 = ProfileService.ApplyRun(ozet);
            Assert.AreSame(r1, r2, "aynı kimlik → transaction'ın orijinal makbuzu");
            Assert.AreEqual(silahlar, ProfileService.Current.UnlockedWeapons.Count);
            Assert.AreEqual(basarimlar, ProfileService.Current.Achievements.Count);
            Assert.AreEqual(seviye, ProfileService.Current.ProfileLevel);
            Assert.AreEqual(xp, ProfileService.Current.TotalXpEarned, "XP iki kez eklenmez");
            Assert.AreEqual(kosu, ProfileService.Current.RunsPlayed, "RunsPlayed iki kez artmaz");
            Assert.AreEqual(kill, ProfileService.Current.TotalKills, "kill iki kez sayılmaz");
            Assert.AreEqual(changedOnce, _changed, "duplicate Changed yayınlamaz");
        }

        [Test]
        public void UnlockOtomatik_ClaimApiYok()
        {
            int hedef = ProfileService.UnlockLevelFor(ClassicWeaponId.AutoSmg);
            int gerekliXp = 0; for (int l = 1; l < hedef; l++) gerekliXp += ProfileService.LevelRequirement(l);
            RunRewardReport r = ProfileService.ApplyRun(Ozet(2, 1, 0, 0, gerekliXp));
            Assert.AreEqual(hedef, r.LevelAfter, "AutoSmg'nin roster/config açılış seviyesine ulaşıldı");
            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg), "unlock ApplyRun içinde otomatik uygulanır");
            Assert.IsTrue(ProfileService.Current.UnlockedWeapons.Contains("smg_1"), "WP-077 v5: kayıtlı silah anahtarı");

            MethodInfo[] metotlar = typeof(ProfileService).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < metotlar.Length; i++)
            {
                Assert.IsFalse(metotlar[i].Name.ToLowerInvariant().Contains("claim"), "Claim API'si olmamalı: " + metotlar[i].Name);
            }
        }

        [Test]
        public void ProfilSeviyesi_CombatVeGoldUyesiYok()
        {
            var yasak = new[] { "damage", "health", "gold", "speed" };
            var uyeler = new List<MemberInfo>();
            uyeler.AddRange(typeof(ProfileService).GetMembers(BindingFlags.Public | BindingFlags.Static));
            uyeler.AddRange(typeof(ProfileData).GetMembers(BindingFlags.Public | BindingFlags.Instance));

            for (int i = 0; i < uyeler.Count; i++)
            {
                string ad = uyeler[i].Name.ToLowerInvariant();
                for (int j = 0; j < yasak.Length; j++)
                {
                    Assert.IsFalse(ad.Contains(yasak[j]),
                        "Profil yüzeyinde combat/gold üyesi: " + uyeler[i].DeclaringType.Name + "." + uyeler[i].Name);
                }
            }
        }

        // ------------------------------------------------------------------ kalıcılık

        [Test]
        public void AlanAlan_DiskGidisDonus()
        {
            var ozet = Ozet(6, 5, 40, 40, 200);
            ozet.ArmorPiecesBroken = 3;
            ozet.EliteKills = 2;
            ozet.StandardKills = 38;
            ProfileService.ApplyRun(ozet);

            ProfileData bellek = ProfileService.Current;
            ProfileData disk = ProfileStore.Load();

            Assert.AreEqual(bellek.ProfileLevel, disk.ProfileLevel);
            Assert.AreEqual(bellek.XpIntoLevel, disk.XpIntoLevel);
            Assert.AreEqual(bellek.TotalXpEarned, disk.TotalXpEarned);
            Assert.AreEqual(6, disk.HighestRound);
            Assert.AreEqual(5, disk.RoundsCompleted);
            Assert.AreEqual(40, disk.TotalKills);
            Assert.AreEqual(38, disk.StandardKills);
            Assert.AreEqual(2, disk.EliteKills);
            Assert.AreEqual(0, disk.SpecialKills);
            Assert.AreEqual(0, disk.UltraKills);
            Assert.AreEqual(3, disk.ArmorPiecesBroken);
            Assert.AreEqual(1, disk.RunsPlayed);
            Assert.AreEqual(bellek.BestRunXp, disk.BestRunXp);
            CollectionAssert.AreEqual(bellek.UnlockedWeapons, disk.UnlockedWeapons);
            CollectionAssert.AreEqual(bellek.UnlockedBranches, disk.UnlockedBranches);
            CollectionAssert.AreEqual(bellek.Achievements, disk.Achievements);
            Assert.IsFalse(ProfileStore.LastLoadFellBack);
        }

        [Test]
        public void JsonAnahtarlari_RunGoldVeBuildIcermez()
        {
            var ozet = Ozet(3, 2, 10, 10, 45);
            ozet.GoldEarned = 77;
            ozet.GoldSpent = 30;
            ozet.BuildDescription = "P2 / FR1";
            ozet.UpgradesPurchased = 2;
            ProfileService.ApplyRun(ozet);

            string json = File.ReadAllText(TestPath);
            StringAssert.DoesNotContain("Gold", json, "profil dosyasında run gold anahtarı olmaz (REQ-018)");
            StringAssert.DoesNotContain("Build", json, "run build dosyaya yazılmaz");
            StringAssert.DoesNotContain("Tier", json, "silah rank'ları dosyaya yazılmaz");
            StringAssert.DoesNotContain("P2 / FR1", json);
            StringAssert.Contains("\"HighestRound\"", json);
            StringAssert.Contains("\"ProfileLevel\"", json);

            // Şema kilidi: ProfileData'nın public alan seti bilinen 17 alandır.
            FieldInfo[] alanlar = typeof(ProfileData).GetFields(BindingFlags.Public | BindingFlags.Instance);
            var adlar = new List<string>();
            for (int i = 0; i < alanlar.Length; i++)
            {
                adlar.Add(alanlar[i].Name);
            }

            // WP-009 (şema v2): + UnattributedKills (kayıpsız kill invariantı). WP-009H (şema v3): 8'lik
            // AppliedCompletionIds halkası KALDIRILDI (yalnız son 8 koşuyu koruyordu); yerine sabit boyutlu
            // ProfileLineageId + LastAppliedCompletionOrdinal (koşu verisi değil, history değil).
            CollectionAssert.AreEquivalent(new[]
            {
                "SaveVersion", "ProfileLevel", "XpIntoLevel", "TotalXpEarned", "HighestRound", "RunsPlayed",
                "RoundsCompleted", "TotalKills", "StandardKills", "SpecialKills", "EliteKills", "UltraKills",
                "UnattributedKills", "ArmorPiecesBroken", "BestRunXp", "Achievements", "UnlockedWeapons",
                "UnlockedBranches", "ProfileLineageId", "LastAppliedCompletionOrdinal", "LastSavedUtc",
                // WP-023 (şema v4): iki ömür boyu feat istatistiği (başarım kuralları okur; DEC-007 istatistik, modifier değil)
                // + katalog içerik listesi (DEC-038). Gold/harcama dosyaya girmez (REQ-018); koşu geçmişi yok, tunable yok.
                "SpecialWeaponPickups", "FlawlessRounds", "UnlockedContent",
                // WP-035 (aynı v4): seçili unvan/rozet (açılmış içerik id'si) + "görüldü" sayaçları; kazanım değil, tercih.
                "SelectedTitle", "SelectedBadge", "AchievementsSeen", "ContentSeen",
                // WP-077 (şema v5): meta YOL — Kül (kazanılan/harcanan/bakiye), meta düğümler, respec, pin, "görüldü",
                // tek seferlik Kül kilometre taşları, silah ustalığı (öldürme). Altın/koşu içi ağaç dosyaya girmez (REQ-018).
                "AshBalance", "AshEarnedTotal", "AshSpentTotal", "MetaNodes", "MetaRespecCount", "MetaPinnedNode",
                "MetaSeen", "AshMilestones", "WeaponMastery",
                // WP-077 A10 (v5, eksikse sıfır): silah / Special feat sayaçları — başarım istatistiği, oynanış değeri değil (DEC-007).
                "FeatLifetime"
            }, adlar, "ProfileData şeması değişti; kalıcılık sınırı yeniden gözden geçirilmeli");
            Assert.AreEqual(5, ProfileData.CurrentVersion);
            StringAssert.DoesNotContain("AppliedCompletionIds", json, "halka diske yazılmaz (v3)");
        }

        [Test]
        public void DosyaYok_VarsayilanGuvenli()
        {
            TryDelete(TestPath);
            OnbellegiBosalt();

            ProfileData p = ProfileService.Current;
            Assert.AreEqual(1, p.ProfileLevel);
            Assert.AreEqual(0, p.XpIntoLevel);
            Assert.AreEqual(0, p.TotalKills);
            Assert.AreEqual(0, p.Achievements.Count);
            Assert.IsFalse(ProfileService.RecoveredFromCorruptSave, "eksik dosya hata değil, ilk açılış");
            Assert.IsTrue(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AssaultRifle), "başlangıç silahı açık");
            Assert.IsFalse(ProfileService.IsWeaponUnlocked(ClassicWeaponId.AutoSmg));
            Assert.IsNull(ProfileService.LastReport);
        }

        [Test]
        public void BozukJson_UyariVeYedek_OyunSurer()
        {
            File.WriteAllText(TestPath, "{{{ not json");
            OnbellegiBosalt();

            LogAssert.Expect(LogType.Warning, new Regex("Profile"));
            ProfileData p = ProfileService.Current;

            Assert.AreEqual(1, p.ProfileLevel, "taze profil");
            Assert.IsTrue(ProfileService.RecoveredFromCorruptSave);
            Assert.IsTrue(File.Exists(TestPath + ".corrupt") || Directory.GetFiles(
                Path.GetDirectoryName(TestPath), Path.GetFileName(TestPath) + "*.corrupt*").Length > 0,
                "bozuk dosya yanına kaldırılır, silinmez");

            // Sonraki koşu normal kaydeder.
            RunRewardReport r = ProfileService.ApplyRun(Ozet(1, 0, 1, 1, 0));
            Assert.IsFalse(r.SaveFailed);
            Assert.AreEqual(1, ProfileStore.Load().TotalKills);
        }

        [Test]
        public void AchievementTanimiYok_HataYok_BosListe()
        {
            // WP-023: tanımlar artık katalog JSON'undan gelir (config listesi kaynak değil);
            // boş katalog → hata yok, boş liste, transaction normal kaydeder.
            DD.Prototype.Achievements.AchievementCatalog.TestSeam.Override(new List<DD.Prototype.Achievements.AchievementDefinition>());
            try
            {
                Assert.AreEqual(0, ProfileService.AllAchievements.Count);
                RunRewardReport r = ProfileService.ApplyRun(Ozet(5, 4, 30, 30, 100));
                Assert.AreEqual(0, r.NewAchievements.Count);
                Assert.AreEqual(0, ProfileService.Current.Achievements.Count);
                Assert.IsFalse(r.SaveFailed);
            }
            finally
            {
                DD.Prototype.Achievements.AchievementCatalog.TestSeam.Override(null);
            }
        }

        [Test]
        public void DalUnlockSeviyesi_TeklifKapisi_ProfilKuraliniOkur()
        {
            // WP-004 devir notu → WP-005 "karar bekliyor" → WP-008 KARAR (ürün kararı 9 / DEC-049 /
            // REQ-078): "ilgili içerik açıksa erişilebilir" — teklif kapısı (RoundUpgradeShop)
            // dalın unlock kuralını okur; varsayılan yüklem ProfileService.IsBranchUnlocked
            // (UpgradeBranchSettings.UnlockProfileLevel), testler kendi yüklemini enjekte eder.
            // Profile pane aynı eşiği görüntülemeye devam eder; iki yüzey artık tutarlıdır.
            string kaynak = File.ReadAllText(Path.Combine(
                Application.dataPath, "_Prototype", "Rounds", "RoundUpgradeShop.cs"));
            StringAssert.Contains("ProfileService.IsBranchUnlocked", kaynak, "teklif kapısı profil unlock kuralına bağlı (WP-008 kararı 9)");

            // Eşik prototip config'inden gelir; L1 profilde Chain kilitli görünür VE teklif edilmez.
            Assert.AreEqual(2, ProfileService.UnlockLevelFor(UpgradeBranch.Explosion));
            Assert.AreEqual(3, ProfileService.UnlockLevelFor(UpgradeBranch.Chain));
            Assert.IsFalse(ProfileService.IsBranchUnlocked(UpgradeBranch.Chain), "L1 profilde Chain kilitli görünür");

            var wallet = new DD.Prototype.Economy.GoldWallet();
            wallet.InitializeForRun(1);
            var shop = new DD.Prototype.Rounds.RoundUpgradeShop { Random = new DD.Prototype.Rounds.SeededUpgradeOfferRandom(1) };
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                for (int round = 1; round <= 30; round++)
                {
                    shop.Open(round, config, wallet, new DD.Prototype.Weapons.WeaponModifierState(), 1);
                    for (int i = 0; i < shop.Cards.Count; i++)
                    {
                        Assert.AreNotEqual(UpgradeBranch.Chain, shop.Cards[i].Branch, "L1 profil: Chain teklif edilmez");
                        Assert.AreNotEqual(UpgradeBranch.Explosion, shop.Cards[i].Branch, "L1 profil: Explosion teklif edilmez");
                    }

                    shop.Close();
                }

                Assert.AreEqual(DD.Prototype.Rounds.UpgradeEligibility.Locked, shop.Eligibility(UpgradeBranch.Chain));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void TurRaporu_DEC113_TurunSayilariniDondururVeYeniTurdaSifirlanir()
        {
            var stats = new RunStatsTracker();
            Assert.IsFalse(stats.LastRound.IsValid, "koşu başında rapor yok");

            stats.BeginRound();
            stats.RecordKill(EnemyTier.Standard, 1);
            stats.RecordKill(EnemyTier.Standard, 1);
            stats.RecordKill(EnemyTier.Special, 2);
            stats.RecordGoldEarned(40);
            stats.RecordGoldEarned(0);
            stats.RecordPlayerDamage(12.5f);
            stats.RecordPlayerDamage(-3f);
            stats.RecordEliteHit(7, 10f);
            stats.RecordEliteHit(7, 11f);                 // yalnız ilk isabet saati başlatır
            stats.RecordEliteHit(8, 12f);
            stats.RecordKill(EnemyTier.Elite, 5);
            stats.RecordEliteDied(7, 13f);                // 3 s
            stats.RecordKill(EnemyTier.Elite, 5);
            stats.RecordEliteDied(8, 13f);                // 1 s
            stats.RecordEliteDied(9, 20f);                // izlenmeyen ömür: örnek yok
            stats.RecordGoldEarned(25);                   // tur sonu süpürmesi rapordan önce

            RoundReport r = stats.CloseRound(4);
            Assert.AreEqual(4, r.Round);
            Assert.AreEqual(2, r.KillsStandard);
            Assert.AreEqual(1, r.KillsSpecial);
            Assert.AreEqual(2, r.KillsElite);
            Assert.AreEqual(0, r.KillsUltra);
            Assert.AreEqual(5, r.Kills);
            Assert.AreEqual(12.5f, r.DamageTaken, 1e-4f);
            Assert.AreEqual(65, r.GoldIncome);
            Assert.AreEqual(2, r.EliteTtkSamples);
            Assert.AreEqual(2f, r.EliteTtkAverage, 1e-4f);
            Assert.AreEqual(r.Round, stats.LastRound.Round);

            stats.BeginRound();
            Assert.AreEqual(0, stats.RoundKillsFor(EnemyTier.Standard));
            Assert.AreEqual(0f, stats.RoundDamageTaken);
            Assert.AreEqual(0, stats.RoundGoldIncome);
            Assert.AreEqual(4, stats.LastRound.Round, "yeni tur başlayınca son rapor kalır");
            Assert.AreEqual(3, stats.KillsFor(EnemyTier.Standard) + stats.KillsFor(EnemyTier.Special), "koşu toplamları etkilenmez");
            RoundReport bos = stats.CloseRound(5);
            Assert.AreEqual(0, bos.EliteTtkSamples);
            Assert.AreEqual(0f, bos.EliteTtkAverage);

            for (int i = 0; i < RunStatsTracker.EliteTrackCapacity + 5; i++) stats.RecordEliteHit(100 + i, 1f);   // taşma düşer, büyümez
            stats.RecordEliteDied(100 + RunStatsTracker.EliteTrackCapacity + 2, 2f);
            Assert.AreEqual(0, stats.CloseRound(6).EliteTtkSamples);

            stats.Reset();
            Assert.IsFalse(stats.LastRound.IsValid, "yeni koşu raporu siler");
        }
    }
}
