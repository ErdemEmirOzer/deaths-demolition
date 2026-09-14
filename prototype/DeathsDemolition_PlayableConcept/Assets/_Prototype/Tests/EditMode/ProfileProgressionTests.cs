using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-009 · Profil aritmetiği (saf, disk yok, config yok): iki XP kaynağı, seviye döngüsü,
    /// istatistik toplamları, kill invariantı, unlock/achievement değerlendirmesi, doygun toplama.
    ///
    /// Kilitlenen kurallar (DEC-008, REQ-021..028, §4.5, §4.7, §E): Total XP = tamamlanan round XP +
    /// kill XP; TotalXpEarned/negatif/çelişkili özet alanları XP üretemez; gold/harcama/upgrade/
    /// reroll/süre girdide bile yok; seviye döngüsü patolojik config'te sonlanır; sayaçlar
    /// wraparound ile negatife dönmez; unlock yalnız eklenir; achievement altyapısı sentetik
    /// tanımlarla çalışır (mevcut liste final içerik değildir).
    /// </summary>
    [TestFixture]
    [Category("WP009")]
    public sealed class ProfileProgressionTests
    {
        // ------------------------------------------------------------------ yardımcılar

        private static RunSummary Ozet(int reached, int completed, int roundXp, int killXp,
            int standard = 0, int special = 0, int elite = 0, int ultra = 0)
        {
            return new RunSummary
            {
                CompletionId = "ddc1:" + Soy + ":1:" + Guid.NewGuid().ToString("N"),
                ReachedRound = reached,
                CompletedRounds = completed,
                CompletedRoundXp = roundXp,
                KillXp = killXp,
                TotalXpEarned = roundXp + killXp,
                StandardKills = standard,
                SpecialKills = special,
                EliteKills = elite,
                UltraKills = ultra,
                TotalKills = standard + special + elite + ultra,
                EndReason = RunEndReason.PlayerDeath
            };
        }

        /// <summary>Sabit test soyu (IsValid lineage ister; v3).</summary>
        private const string Soy = "0123456789abcdef0123456789abcdef";

        private static int Egri(int level)
        {
            return 100 + 75 * (Math.Max(1, level) - 1);
        }

        /// <summary>Sentetik merdiven: SMG L2, Shotgun L3, Explosion L2, Chain L3; testler eşiği değiştirebilir.</summary>
        private sealed class SahteMerdiven : IUnlockLadder
        {
            public readonly Dictionary<ClassicWeaponId, int> Silah = new Dictionary<ClassicWeaponId, int>
            {
                { ClassicWeaponId.AssaultRifle, 1 }, { ClassicWeaponId.AutoSmg, 2 }, { ClassicWeaponId.CombatShotgun, 3 }
            };

            public readonly Dictionary<UpgradeBranch, int> Dal = new Dictionary<UpgradeBranch, int>();

            public int UnlockLevelFor(ClassicWeaponId id) => Silah.TryGetValue(id, out int l) ? l : 1;
            public int UnlockLevelFor(UpgradeBranch branch) => Dal.TryGetValue(branch, out int l) ? l : 1;
            public string WeaponDisplayName(ClassicWeaponId id) => "W-" + id;
            public string BranchDisplayName(UpgradeBranch branch) => "B-" + branch;

            public SahteMerdiven()
            {
                Dal[UpgradeBranch.Explosion] = 2;
                Dal[UpgradeBranch.Chain] = 3;
            }
        }

        private static AchievementSettings Tanim(string id, AchievementTrigger trigger, int threshold, EnemyTier tier = EnemyTier.Standard)
        {
            return new AchievementSettings
            {
                Id = id, DisplayName = "T-" + id, Description = "d-" + id, Trigger = trigger, Threshold = threshold, TargetTier = tier
            };
        }

        // ------------------------------------------------------------------ XP kaynakları

        [Test]
        public void ToplamXp_YalnizRoundArtiKill()
        {
            var g = new RunRewardInput(Ozet(3, 2, 45, 17, standard: 17));
            Assert.AreEqual(45, g.CompletedRoundXp);
            Assert.AreEqual(17, g.KillXp);
            Assert.AreEqual(62, g.TotalXp, "Total XP awarded = Completed Round XP + Kill XP");
        }

        [Test]
        public void CeliskiliTotalXpEarned_XpUretemez()
        {
            RunSummary o = Ozet(3, 2, 45, 17, standard: 17);
            o.TotalXpEarned = 9999;
            Assert.AreEqual(62, new RunRewardInput(o).TotalXp, "özetteki toplam alanı okunmaz");

            o = Ozet(3, 0, 0, 0);
            o.TotalXpEarned = 500;
            Assert.AreEqual(0, new RunRewardInput(o).TotalXp, "iki kaynak sıfırken TotalXpEarned fallback'i YOK (§4.5)");
        }

        [Test]
        public void NegatifAlanlar_SifiraKirpilir_EkstraXpYok()
        {
            RunSummary o = Ozet(-4, -9, -100, -50, standard: -3, special: -1);
            o.ArmorPiecesBroken = -7;
            o.TotalKills = -8;
            var g = new RunRewardInput(o);
            Assert.AreEqual(0, g.TotalXp);
            Assert.AreEqual(0, g.ReachedRound);
            Assert.AreEqual(0, g.CompletedRounds);
            Assert.AreEqual(0, g.TotalKills);
            Assert.AreEqual(0, g.ArmorPiecesBroken);
        }

        [Test]
        public void TamamlananRoundlar_UlasilandanFazlaOlamaz()
        {
            var g = new RunRewardInput(Ozet(2, 7, 30, 0));
            Assert.AreEqual(2, g.CompletedRounds, "tamamlanan ≤ ulaşılan");
        }

        [Test]
        public void GoldHarcamaUpgradeRerollSure_GirdideYok()
        {
            RunSummary o = Ozet(2, 1, 20, 5, standard: 5);
            o.GoldEarned = 999;
            o.GoldSpent = 500;
            o.GoldSpentOnUpgrades = 400;
            o.GoldSpentOnRerolls = 100;
            o.UpgradesPurchased = 9;
            o.RerollsUsed = 3;
            o.RunDuration = 36000f;
            var g = new RunRewardInput(o);
            Assert.AreEqual(25, g.TotalXp, "gold/harcama/upgrade/reroll/süre XP farkı oluşturmaz");

            // Şema kilidi: girdi tipinde gold/upgrade/reroll/süre alanı yok.
            foreach (var f in typeof(RunRewardInput).GetFields())
            {
                string ad = f.Name.ToLowerInvariant();
                Assert.IsFalse(ad.Contains("gold") || ad.Contains("upgrade") || ad.Contains("reroll") || ad.Contains("duration") || ad.Contains("time"),
                    "RunRewardInput içinde XP dışı kaynak alanı: " + f.Name);
            }
        }

        [TestCase(EnemyTier.Standard)]
        [TestCase(EnemyTier.Special)]
        [TestCase(EnemyTier.Elite)]
        [TestCase(EnemyTier.Ultra)]
        public void TierKill_Parametrik_IstatistikVeInvariant(EnemyTier tier)
        {
            RunSummary o = Ozet(1, 0, 0, 12);
            switch (tier)
            {
                case EnemyTier.Standard: o.StandardKills = 4; break;
                case EnemyTier.Special: o.SpecialKills = 4; break;
                case EnemyTier.Elite: o.EliteKills = 4; break;
                case EnemyTier.Ultra: o.UltraKills = 4; break;
            }

            o.TotalKills = 4;
            var g = new RunRewardInput(o);
            Assert.AreEqual(4, g.KillsFor(tier));
            Assert.AreEqual(4, g.TotalKills);

            var p = new ProfileData { ProfileLineageId = Soy };
            ProfileProgression.ApplyStatistics(p, g);
            Assert.AreEqual(4, p.KillsForTierName(tier.ToString()));
            Assert.AreEqual(4, p.TotalKills);
            Assert.AreEqual(0, p.UnattributedKills);
            Assert.IsTrue(p.IsValid());
            Assert.AreEqual(12, p.TotalXpEarned, "yarım rounddaki kill XP'si korunur; round XP yok");
            Assert.AreEqual(0, p.RoundsCompleted);
        }

        [Test]
        public void OzetToplamKill_TierToplamindanBuyukse_AtanmamisKovaya_Uydurulmaz()
        {
            RunSummary o = Ozet(1, 0, 0, 0, standard: 3);
            o.TotalKills = 10;
            var g = new RunRewardInput(o);
            Assert.AreEqual(7, g.UnattributedKills);
            Assert.AreEqual(10, g.TotalKills);

            var p = new ProfileData { ProfileLineageId = Soy };
            ProfileProgression.ApplyStatistics(p, g);
            Assert.AreEqual(3, p.StandardKills, "eksik kill'ler tier'lara dağıtılmaz");
            Assert.AreEqual(7, p.UnattributedKills);
            Assert.AreEqual(10, p.TotalKills);
            Assert.IsTrue(p.IsValid());

            o.TotalKills = 1; // tier toplamından KÜÇÜK → tier toplamı kazanır
            g = new RunRewardInput(o);
            Assert.AreEqual(0, g.UnattributedKills);
            Assert.AreEqual(3, g.TotalKills);
        }

        [Test]
        public void SifirKosu_SifirKill_Guvenli()
        {
            var p = new ProfileData { ProfileLineageId = Soy };
            var g = new RunRewardInput(Ozet(0, 0, 0, 0));
            ProfileProgression.ApplyStatistics(p, g);
            LevelProgress lp = ProfileProgression.ApplyXp(p.ProfileLevel, p.XpIntoLevel, g.TotalXp, Egri);
            Assert.AreEqual(1, p.RunsPlayed, "sıfır koşu da bir koşudur");
            Assert.AreEqual(0, p.TotalKills);
            Assert.AreEqual(0, p.TotalXpEarned);
            Assert.AreEqual(1, lp.Level);
            Assert.AreEqual(0, lp.XpIntoLevel);
            Assert.IsTrue(p.IsValid());
        }

        [Test]
        public void KosuSayaci_NTamamlananRound_YalnizOnlarinRoundXpsi_YarimRoundKillXp()
        {
            // Birikim tarafı (RunStatsTracker → RunSummary): 3 tamamlanan round + 4. roundda kill.
            var stats = new RunStatsTracker();
            int beklenenRoundXp = 0;
            for (int r = 1; r <= 3; r++)
            {
                stats.RecordRoundCompleted(15 + 5 * r);
                beklenenRoundXp += 15 + 5 * r;
            }

            stats.RecordKill(EnemyTier.Standard, 1);
            stats.RecordKill(EnemyTier.Elite, 4);
            Assert.AreEqual(3, stats.CompletedRounds);
            Assert.AreEqual(beklenenRoundXp, stats.CompletedRoundXp, "yalnız tamamlanan roundların XP'si; 4. round yok");
            Assert.AreEqual(5, stats.KillXp, "yarım rounddaki kill XP'si sayılır");
            Assert.AreEqual(beklenenRoundXp + 5, stats.TotalXp);
            stats.RecordRoundCompleted(-50);
            Assert.AreEqual(beklenenRoundXp, stats.CompletedRoundXp, "negatif round XP eklenmez");
        }

        // ------------------------------------------------------------------ seviye

        [Test]
        public void SinirXp_TamGereksinim_SeviyeAtlarSifirKalir()
        {
            LevelProgress lp = ProfileProgression.ApplyXp(1, 0, 100, Egri);
            Assert.AreEqual(2, lp.Level);
            Assert.AreEqual(0, lp.XpIntoLevel);
            Assert.AreEqual(1, lp.LevelUps);
            Assert.IsFalse(lp.Capped);

            lp = ProfileProgression.ApplyXp(1, 0, 99, Egri);
            Assert.AreEqual(1, lp.Level);
            Assert.AreEqual(99, lp.XpIntoLevel);
        }

        [Test]
        public void CokluSeviyeAtlama()
        {
            // 100 + 175 + 250 = 525 → L4, artan 75
            LevelProgress lp = ProfileProgression.ApplyXp(1, 0, 600, Egri);
            Assert.AreEqual(4, lp.Level);
            Assert.AreEqual(75, lp.XpIntoLevel);
            Assert.AreEqual(3, lp.LevelUps);
        }

        [TestCase(0)]
        [TestCase(-50)]
        public void PatolojikGereksinim_SonsuzDonguYok_SinirliVeDogru(int gereksinim)
        {
            LevelProgress lp = ProfileProgression.ApplyXp(1, 0, 1000, _ => gereksinim);
            Assert.IsTrue(lp.Capped, "0/negatif gereksinim: 1 XP = 1 seviye; kapak devreye girer");
            Assert.AreEqual(1 + ProfileProgression.MaxLevelUpsPerTransaction, lp.Level);
            Assert.AreEqual(1000 - ProfileProgression.MaxLevelUpsPerTransaction, lp.XpIntoLevel, "artan XP kaybolmaz");
            Assert.GreaterOrEqual(lp.XpIntoLevel, 0);
        }

        [Test]
        public void NullGereksinim_VeSeviyeTavani_Guvenli()
        {
            LevelProgress lp = ProfileProgression.ApplyXp(int.MaxValue, 0, 50, Egri);
            Assert.AreEqual(int.MaxValue, lp.Level, "seviye int.MaxValue'yu aşamaz");
            Assert.AreEqual(50, lp.XpIntoLevel);

            lp = ProfileProgression.ApplyXp(1, 0, 3, null);
            Assert.AreEqual(4, lp.Level, "null eğri = 1 XP/seviye, sınırlı");
        }

        [Test]
        public void TasmaGirdileri_DoygunTopla_NegatifeDonmez()
        {
            Assert.AreEqual(int.MaxValue, ProfileProgression.SaturatingAdd(int.MaxValue, 1));
            Assert.AreEqual(int.MaxValue, ProfileProgression.SaturatingAdd(int.MaxValue - 5, 100));
            Assert.AreEqual(int.MinValue, ProfileProgression.SaturatingAdd(int.MinValue, -1));
            Assert.AreEqual(int.MaxValue, ProfileProgression.SaturatingSum(int.MaxValue, int.MaxValue, 1, 1));

            LevelProgress lp = ProfileProgression.ApplyXp(1, int.MaxValue - 10, int.MaxValue, Egri);
            Assert.GreaterOrEqual(lp.XpIntoLevel, 0);
            Assert.GreaterOrEqual(lp.Level, 1);

            var p = new ProfileData
            {
                ProfileLineageId = Soy,
                TotalXpEarned = int.MaxValue - 1, BestRunXp = int.MaxValue, RunsPlayed = int.MaxValue,
                RoundsCompleted = int.MaxValue, StandardKills = int.MaxValue, TotalKills = int.MaxValue,
                ArmorPiecesBroken = int.MaxValue, HighestRound = int.MaxValue
            };
            RunSummary o = Ozet(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, standard: int.MaxValue, special: int.MaxValue, elite: 5, ultra: 5);
            o.TotalKills = int.MaxValue;
            o.ArmorPiecesBroken = int.MaxValue;
            var g = new RunRewardInput(o);
            Assert.AreEqual(int.MaxValue, g.TotalXp);
            Assert.AreEqual(int.MaxValue, g.TotalKills);
            ProfileProgression.ApplyStatistics(p, g);
            Assert.AreEqual(int.MaxValue, p.TotalXpEarned);
            Assert.AreEqual(int.MaxValue, p.RunsPlayed);
            Assert.AreEqual(int.MaxValue, p.RoundsCompleted);
            Assert.AreEqual(int.MaxValue, p.StandardKills);
            Assert.AreEqual(int.MaxValue, p.TotalKills);
            Assert.AreEqual(int.MaxValue, p.ArmorPiecesBroken);
            Assert.AreEqual(int.MaxValue, p.BestRunXp);
            Assert.IsTrue(p.IsValid(), "doygun sayaçlar geçerli kalır");
            p.Sanitize();
            Assert.IsTrue(p.IsValid());
        }

        // ------------------------------------------------------------------ kill invariantı

        [Test]
        public void Sanitize_KillInvarianti_KayipsizOnarim()
        {
            var p = new ProfileData { ProfileLineageId = Soy, TotalKills = 17, StandardKills = 17 };
            p.Sanitize();
            Assert.AreEqual(0, p.UnattributedKills);
            Assert.AreEqual(17, p.TotalKills);

            p = new ProfileData { ProfileLineageId = Soy, TotalKills = 40, StandardKills = 31 }; // legacy: toplam > tier toplamı
            p.Sanitize();
            Assert.AreEqual(9, p.UnattributedKills, "fark atanmamış kovaya; tier'lara dağıtılmaz");
            Assert.AreEqual(31, p.StandardKills);
            Assert.AreEqual(40, p.TotalKills, "toplam korunur (kayıpsız)");
            Assert.IsTrue(p.IsValid());

            p = new ProfileData { ProfileLineageId = Soy, TotalKills = 3, StandardKills = 10, EliteKills = 2 }; // toplam < tier toplamı
            p.Sanitize();
            Assert.AreEqual(12, p.TotalKills, "tanıklı kill'ler geri sayılmaz");
            Assert.AreEqual(0, p.UnattributedKills);

            p = new ProfileData { ProfileLineageId = Soy, TotalKills = 40, StandardKills = 31 };
            p.Sanitize();
            string once = UnityEngine.JsonUtility.ToJson(p);
            p.Sanitize();
            Assert.AreEqual(once, UnityEngine.JsonUtility.ToJson(p), "Sanitize idempotent");
        }

        [Test]
        public void IsValid_BozukToplamVeKopyaIdReddedilir()
        {
            var p = new ProfileData { ProfileLineageId = Soy, TotalKills = 5, StandardKills = 2 };
            Assert.IsFalse(p.IsValid(), "TotalKills ≠ tier + atanmamış");
            p.Sanitize();
            Assert.IsTrue(p.IsValid());

            p.Achievements.Add("a");
            p.Achievements.Add("a");
            Assert.IsFalse(p.IsValid(), "kopya id geçersiz");
            p.Sanitize();
            Assert.AreEqual(1, p.Achievements.Count);
            Assert.IsTrue(p.IsValid());

            p.ProfileLevel = 0;
            Assert.IsFalse(p.IsValid());
        }

        [Test]
        public void Clone_DerinKopya()
        {
            var p = new ProfileData { ProfileLineageId = Soy, ProfileLevel = 3, TotalKills = 4, StandardKills = 4, LastAppliedCompletionOrdinal = 7 };
            p.Achievements.Add("x");
            ProfileData k = p.Clone();
            k.Achievements.Add("y");
            k.ProfileLevel = 9;
            k.LastAppliedCompletionOrdinal = 8;
            k.ProfileLineageId = "ffffffffffffffffffffffffffffffff";
            Assert.AreEqual(1, p.Achievements.Count);
            Assert.AreEqual(3, p.ProfileLevel);
            Assert.AreEqual(7, p.LastAppliedCompletionOrdinal);
            Assert.AreEqual(Soy, p.ProfileLineageId);
        }

        [Test]
        public void ReplayDurumu_SabitBoyut_LineageVeOrdinal()
        {
            // WP-009H: anti-replay durumu iki sabit alandır; halka/liste yoktur (şema kilidi).
            var alanlar = new List<string>();
            foreach (var f in typeof(ProfileData).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                alanlar.Add(f.Name);
            }

            CollectionAssert.Contains(alanlar, "ProfileLineageId");
            CollectionAssert.Contains(alanlar, "LastAppliedCompletionOrdinal");
            CollectionAssert.DoesNotContain(alanlar, "AppliedCompletionIds");
            Assert.IsNull(typeof(ProfileData).GetField("AppliedCompletionIdCapacity"), "kapasite sabiti kaldırıldı");

            var p = new ProfileData { ProfileLineageId = Soy, LastAppliedCompletionOrdinal = -5 };
            p.Sanitize();
            Assert.AreEqual(-5, p.LastAppliedCompletionOrdinal, "replay durumu Sanitize ile ONARILMAZ (sessiz onarım replay'i yeniden açar)");
            Assert.IsFalse(p.IsValid(), "negatif ordinal geçersiz belge → corrupt prosedürü");
            p.LastAppliedCompletionOrdinal = 0;
            Assert.IsTrue(p.IsValid());
            Assert.IsTrue(ProfileData.IsLineageId(Soy));
            Assert.IsFalse(ProfileData.IsLineageId(string.Empty));
            Assert.IsFalse(ProfileData.IsLineageId("0123456789ABCDEF0123456789ABCDEF"), "büyük harf kabul edilmez");
            Assert.IsFalse(ProfileData.IsLineageId("0123456789abcdef0123456789abcde"));
            Assert.IsTrue(ProfileData.IsLineageId(ProfileData.NewLineageId()));
            var soysuz = new ProfileData();
            soysuz.Sanitize();
            Assert.IsFalse(soysuz.IsValid(), "lineage'sız profil geçersizdir; Sanitize uydurmaz");
        }

        // ------------------------------------------------------------------ unlock

        [Test]
        public void Unlock_YalnizEklenir_MerdivenYukselinceGeriAlinmaz()
        {
            var merdiven = new SahteMerdiven();
            var p = new ProfileData { ProfileLineageId = Soy, ProfileLevel = 2 };
            var yeni = new List<ProfileUnlockEntry>();
            ProfileProgression.GrantUnlocksForLevel(p, merdiven, yeni);
            Assert.IsTrue(ProfileProgression.IsWeaponEntitled(p, ClassicWeaponId.AutoSmg));
            Assert.IsTrue(ProfileProgression.IsBranchEntitled(p, UpgradeBranch.Explosion));
            Assert.IsFalse(ProfileProgression.IsWeaponEntitled(p, ClassicWeaponId.CombatShotgun));
            Assert.IsFalse(ProfileProgression.IsBranchEntitled(p, UpgradeBranch.Chain));
            int ilk = yeni.Count;
            Assert.Greater(ilk, 0);

            // Config eşiği sonradan yükselir: kazanılmış entitlement AÇIK kalır; yeni kazanım olmaz.
            merdiven.Silah[ClassicWeaponId.AutoSmg] = 50;
            merdiven.Dal[UpgradeBranch.Explosion] = 50;
            yeni.Clear();
            ProfileProgression.GrantUnlocksForLevel(p, merdiven, yeni);
            Assert.AreEqual(0, yeni.Count, "aynı unlock ikinci kez 'new' değildir");
            Assert.IsTrue(ProfileProgression.IsWeaponEntitled(p, ClassicWeaponId.AutoSmg), "eşik yükselse de kazanılmış içerik kilitlenmez");
            Assert.IsTrue(ProfileProgression.IsBranchEntitled(p, UpgradeBranch.Explosion));

            // Taze L2 profil aynı yükselmiş merdivende SMG'yi KAZANMAZ (merdiven yalnız yeni kazanımı belirler).
            var taze = new ProfileData { ProfileLineageId = Soy, ProfileLevel = 2 };
            ProfileProgression.GrantUnlocksForLevel(taze, merdiven, null);
            Assert.IsFalse(ProfileProgression.IsWeaponEntitled(taze, ClassicWeaponId.AutoSmg));
            Assert.IsTrue(ProfileProgression.IsWeaponEntitled(taze, ClassicWeaponId.AssaultRifle));
        }

        [Test]
        public void BilinmeyenId_IcerikAcmaz_AmaKorunur()
        {
            var p = new ProfileData { ProfileLineageId = Soy };
            p.UnlockedWeapons.Add("Bazooka");
            p.UnlockedBranches.Add("Laser");
            p.Achievements.Add("ghost_achievement");
            p.Sanitize();
            Assert.IsTrue(p.IsValid());
            Assert.AreEqual(1, p.UnlockedWeapons.Count, "bilinmeyen id saklanır (karantina: yerinde tutulur)");
            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                Assert.IsFalse(ProfileProgression.IsWeaponEntitled(p, id), "tanımlı hiçbir silah bilinmeyen id ile açılmaz: " + id);
            }

            foreach (UpgradeBranch b in Enum.GetValues(typeof(UpgradeBranch)))
            {
                Assert.IsFalse(ProfileProgression.IsBranchEntitled(p, b));
            }

            Assert.IsFalse(ProfileProgression.IsWeaponEntitled(p, (ClassicWeaponId)999), "tanımsız enum değeri de açık değildir");
            Assert.IsFalse(ProfileProgression.IsWeaponEntitled(null, ClassicWeaponId.AssaultRifle));
        }

        [Test]
        public void Unlock_KopyaEntitlementOlusmaz()
        {
            var merdiven = new SahteMerdiven();
            var p = new ProfileData { ProfileLineageId = Soy, ProfileLevel = 3 };
            ProfileProgression.GrantUnlocksForLevel(p, merdiven, null);
            ProfileProgression.GrantUnlocksForLevel(p, merdiven, null);
            ProfileProgression.GrantUnlocksForLevel(p, merdiven, null);
            Assert.AreEqual(3, p.UnlockedWeapons.Count);
            Assert.AreEqual(Enum.GetValues(typeof(UpgradeBranch)).Length, p.UnlockedBranches.Count);
            Assert.IsTrue(p.IsValid());
        }

        // ------------------------------------------------------------------ achievement (sentetik)

        [Test]
        public void Achievement_SentetikTanimlar_HerTetik_BirKezKazanilir()
        {
            var tanimlar = new List<AchievementSettings>
            {
                Tanim("syn_kill", AchievementTrigger.FirstKill, 3),
                Tanim("syn_armor", AchievementTrigger.ArmorPieceBroken, 2),
                Tanim("syn_round", AchievementTrigger.ReachRound, 4),
                Tanim("syn_elite", AchievementTrigger.KillTier, 1, EnemyTier.Elite),
                Tanim("syn_zero", AchievementTrigger.FirstKill, 0), // eşik < 1 → 1
                null,
                Tanim(string.Empty, AchievementTrigger.FirstKill, 1),
                Tanim("syn_unknown", (AchievementTrigger)999, 1)
            };

            var p = new ProfileData { ProfileLineageId = Soy };
            RunSummary o = Ozet(4, 3, 60, 5, standard: 2, elite: 1);
            o.ArmorPiecesBroken = 2;
            var g = new RunRewardInput(o);
            ProfileProgression.ApplyStatistics(p, g);
            var kazanilan = new List<ProfileUnlockEntry>();
            ProfileProgression.EvaluateAchievements(p, g, tanimlar, kazanilan);

            CollectionAssert.AreEquivalent(new[] { "syn_kill", "syn_armor", "syn_round", "syn_elite", "syn_zero" }, p.Achievements);
            Assert.AreEqual(5, kazanilan.Count);
            Assert.AreEqual("T-syn_kill", kazanilan[0].Title);

            kazanilan.Clear();
            ProfileProgression.EvaluateAchievements(p, g, tanimlar, kazanilan);
            Assert.AreEqual(0, kazanilan.Count, "kazanılmış achievement ikinci kez 'new' olmaz");
            Assert.AreEqual(5, p.Achievements.Count);
        }

        [Test]
        public void Achievement_BirikimliEsik_KosuTekBasinaYetmeseDe()
        {
            var tanimlar = new List<AchievementSettings> { Tanim("syn_armor5", AchievementTrigger.ArmorPieceBroken, 5) };
            var p = new ProfileData { ProfileLineageId = Soy, ArmorPiecesBroken = 4 };
            RunSummary o = Ozet(1, 0, 0, 0);
            o.ArmorPiecesBroken = 1;
            var g = new RunRewardInput(o);
            ProfileProgression.ApplyStatistics(p, g);
            var kazanilan = new List<ProfileUnlockEntry>();
            ProfileProgression.EvaluateAchievements(p, g, tanimlar, kazanilan);
            Assert.AreEqual(1, kazanilan.Count, "lifetime toplam eşiği aşınca kazanılır");
        }

        [Test]
        public void Achievement_TanimYok_VeyaNull_HataYok()
        {
            var p = new ProfileData { ProfileLineageId = Soy };
            var g = new RunRewardInput(Ozet(9, 8, 500, 50, standard: 50));
            ProfileProgression.EvaluateAchievements(p, g, null, null);
            ProfileProgression.EvaluateAchievements(p, g, new List<AchievementSettings>(), new List<ProfileUnlockEntry>());
            ProfileProgression.EvaluateAchievements(null, g, new List<AchievementSettings>(), null);
            Assert.AreEqual(0, p.Achievements.Count);
        }
    }
}
