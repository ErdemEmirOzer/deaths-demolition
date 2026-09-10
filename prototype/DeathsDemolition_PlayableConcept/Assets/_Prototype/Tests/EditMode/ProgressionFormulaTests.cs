using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bağımsız doğrulama · XP tablosu, seviye eşiği, unlock merdiveni.
    ///
    /// Beklenen değerler SABİT (TS-201). Referans:
    ///   seviye eşiği = 100 + 75(L-1)
    ///   tur XP       = 15 + 5R
    ///   öldürme XP   = 1 + tier bonusu (0/1/3/8)
    /// </summary>
    [TestFixture]
    public sealed class ProgressionFormulaTests
    {
        private ProgressionConfigSection _prog;

        [SetUp]
        public void SetUp()
        {
            _prog = new ProgressionConfigSection();
        }

        // L        1    2    3    4    5    6    10
        // eşik   100  175  250  325  400  475   775
        [TestCase(1, 100)]
        [TestCase(2, 175)]
        [TestCase(3, 250)]
        [TestCase(4, 325)]
        [TestCase(5, 400)]
        [TestCase(6, 475)]
        [TestCase(10, 775)]
        [TestCase(20, 1525)]
        public void LevelRequirement_SabitBeklenenDeger(int level, int beklenen)
        {
            Assert.AreEqual(beklenen, _prog.LevelRequirement(level),
                "L" + level + " eşiği 100+75(L-1) ile uyuşmuyor.");
        }

        [Test]
        public void LevelRequirement_L1AltiniKirpar()
        {
            Assert.AreEqual(100, _prog.LevelRequirement(0));
            Assert.AreEqual(100, _prog.LevelRequirement(-4));
        }

        // R       1   2   3   4   5   8  10  12
        // XP     20  25  30  35  40  55  65  75
        [TestCase(1, 20)]
        [TestCase(2, 25)]
        [TestCase(3, 30)]
        [TestCase(4, 35)]
        [TestCase(5, 40)]
        [TestCase(8, 55)]
        [TestCase(10, 65)]
        [TestCase(12, 75)]
        public void RoundCompletionXp_SabitBeklenenDeger(int round, int beklenen)
        {
            Assert.AreEqual(beklenen, _prog.RoundCompletionXp(round),
                "R" + round + " tur XP'si 15+5R ile uyuşmuyor.");
        }

        [Test]
        public void XpKurallari_IstenmeyenKaynaklarKapali()
        {
            Assert.AreEqual(1, _prog.XpPerKill, "Öldürme başına düz XP 1 olmalı.");
            Assert.IsFalse(_prog.GoldGrantsXp, "Altın XP vermemeli.");
            Assert.IsFalse(_prog.PlaytimeGrantsXp, "Oynama süresi XP vermemeli.");
            Assert.IsFalse(_prog.IncompleteRoundGrantsRoundXp, "Bitmemiş tur, tur XP'si vermemeli.");
        }

        [Test]
        public void KillXp_TierBonuslariSabit()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                // 1 düz XP + tier bonusu (0 / 1 / 3 / 8)
                Assert.AreEqual(1, config.KillXp(EnemyTier.Standard), "Standard öldürme XP'si 1 olmalı.");
                Assert.AreEqual(2, config.KillXp(EnemyTier.Special), "Special öldürme XP'si 1+1=2 olmalı.");
                Assert.AreEqual(4, config.KillXp(EnemyTier.Elite), "Elite öldürme XP'si 1+3=4 olmalı.");
                Assert.AreEqual(9, config.KillXp(EnemyTier.Ultra), "Ultra öldürme XP'si 1+8=9 olmalı.");
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void GoldReward_TierOdulleriSabit()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                Assert.AreEqual(2, config.GoldReward(EnemyTier.Standard));
                Assert.AreEqual(5, config.GoldReward(EnemyTier.Special));
                Assert.AreEqual(10, config.GoldReward(EnemyTier.Elite));
                Assert.AreEqual(25, config.GoldReward(EnemyTier.Ultra));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>
        /// BİLİNEN VE KASITLI SINIRLAMA — ölçülen bulgu, hata değil.
        /// L4'e ulaşmak 100+175+250 = 525 kümülatif XP ister. R8'e kadar giden bir
        /// koşu yaklaşık 545-600 XP üretir; yani unlock merdiveni TEK koşuda tükenir.
        /// Test bu sayıyı kilitler ki iddia sayısal kanıta bağlansın.
        /// </summary>
        [Test]
        public void OlculenBulgu_UnlockMerdiveniTekKosudaTukenir()
        {
            int l4Kumulatif = _prog.LevelRequirement(1) + _prog.LevelRequirement(2) + _prog.LevelRequirement(3);
            Assert.AreEqual(525, l4Kumulatif, "L4 kümülatif eşiği 525 olmalı.");

            // R1..R8 tamamlanan tur XP'si: sum(15+5R) for R=1..8
            int turXp = 0;
            for (int r = 1; r <= 8; r++)
            {
                turXp += _prog.RoundCompletionXp(r);
            }

            Assert.AreEqual(300, turXp, "R1-R8 tur XP toplamı 300 olmalı.");

            // R1..R8 havuz toplamı, hepsi Standard varsayımıyla en düşük öldürme XP'si.
            var rounds = new RoundConfigSection();
            int enAzOldurmeXp = 0;
            for (int r = 1; r <= 8; r++)
            {
                enAzOldurmeXp += rounds.TotalEnemyPool(r) * _prog.XpPerKill;
            }

            Assert.AreEqual(348, enAzOldurmeXp, "R1-R8 havuz toplamı 348 düşman olmalı.");

            int enAzToplam = turXp + enAzOldurmeXp;
            Assert.AreEqual(648, enAzToplam);
            Assert.Greater(enAzToplam, l4Kumulatif,
                "R8 koşusu L4 eşiğini geçmiyorsa 'merdiven tek koşuda tükeniyor' bulgusu geçersizdir.");

            // Merdivenin tepesi: en yüksek unlock seviyesi 3 (Combat Shotgun).
            var weapons = new WeaponConfigSection();
            int enYuksekUnlock = 1;
            for (int i = 0; i < weapons.ClassicWeapons.Count; i++)
            {
                enYuksekUnlock = Mathf.Max(enYuksekUnlock, weapons.ClassicWeapons[i].UnlockProfileLevel);
            }

            Assert.AreEqual(3, enYuksekUnlock, "En yüksek silah unlock seviyesi 3 olmalı.");
        }

        [Test]
        public void SilahUnlockSeviyeleri_Sabit()
        {
            var weapons = new WeaponConfigSection();
            Assert.AreEqual(1, weapons.Find(ClassicWeaponId.AssaultRifle).UnlockProfileLevel);
            Assert.AreEqual(2, weapons.Find(ClassicWeaponId.AutoSmg).UnlockProfileLevel);
            Assert.AreEqual(3, weapons.Find(ClassicWeaponId.CombatShotgun).UnlockProfileLevel);
        }

        [Test]
        public void Skinler_UcuBirdenKozmetikVeBastanAcik()
        {
            Assert.AreEqual(3, _prog.Skins.Count, "Üç skin tanımlı olmalı.");
            Assert.AreEqual(SkinId.IvoryCrimson, _prog.DefaultSkin);
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, _prog.DefaultWeapon);
        }
    }
}
