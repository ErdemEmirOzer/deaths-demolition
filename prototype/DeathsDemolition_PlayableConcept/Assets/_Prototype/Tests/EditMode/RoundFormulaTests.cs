using DD.Prototype.Core.Config;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bağımsız doğrulama · tur ölçekleme formülleri.
    ///
    /// ÇÜRÜTME KOŞULU (TS-201): beklenen değerler config'ten TÜRETİLMEZ, elle
    /// hesaplanıp SABİT yazılır. Beklenen değeri koda sorarsak test kendini
    /// doğrular ve hiçbir şey kanıtlamaz.
    ///
    /// Referans (GT'nin kendi float32 hesabı, koddan bağımsız):
    ///   havuz   = 12 + 6(R-1) + floor(2.5 * (R-1)^1.15)
    ///   aktif   = min(7 + 2R, 32), mutlak tavan 40
    ///   can     = 1 + 0.12(R-1)
    ///   hasar   = 1 + 0.08(R-1)
    /// </summary>
    [TestFixture]
    public sealed class RoundFormulaTests
    {
        private RoundConfigSection _rounds;

        [SetUp]
        public void SetUp()
        {
            // Her test kendi örneğini kurar; hiçbir test sıralı bağımlılık kurmaz.
            _rounds = new RoundConfigSection();
        }

        // ------------------------------------------------------------ havuz formülü

        // R      1   2   3   4   5   6   7   8   9  10   11   12   13   14   15
        // havuz 12  20  29  38  48  57  67  77  87  97  107  117  127  137  147
        [TestCase(1, 12)]
        [TestCase(2, 20)]
        [TestCase(3, 29)]
        [TestCase(4, 38)]
        [TestCase(5, 48)]
        [TestCase(6, 57)]
        [TestCase(7, 67)]
        [TestCase(8, 77)]
        [TestCase(9, 87)]
        [TestCase(10, 97)]
        [TestCase(11, 107)]
        [TestCase(12, 117)]
        [TestCase(13, 127)]
        [TestCase(14, 137)]
        [TestCase(15, 147)]
        public void TotalEnemyPool_SabitBeklenenDeger(int round, int beklenen)
        {
            Assert.AreEqual(beklenen, _rounds.TotalEnemyPool(round),
                "R" + round + " havuzu 12+6(R-1)+floor(2.5(R-1)^1.15) ile uyuşmuyor.");
        }

        [Test]
        public void TotalEnemyPool_R1AltiniKirpar()
        {
            Assert.AreEqual(12, _rounds.TotalEnemyPool(0), "R=0 R=1'e kırpılmalı.");
            Assert.AreEqual(12, _rounds.TotalEnemyPool(-5), "Negatif tur R=1'e kırpılmalı.");
        }

        [Test]
        public void TotalEnemyPool_MonotonArtar()
        {
            int onceki = _rounds.TotalEnemyPool(1);
            for (int r = 2; r <= 40; r++)
            {
                int simdi = _rounds.TotalEnemyPool(r);
                Assert.Greater(simdi, onceki, "Havuz R" + r + "'de artmadı; eğri monoton olmalı.");
                onceki = simdi;
            }
        }

        // ------------------------------------------------------- hedef aktif düşman

        // R      1   2   3   4   5   6   7   8   9  10  11  12  13  14  20  50
        // aktif  9  11  13  15  17  19  21  23  25  27  29  31  32  32  32  32
        [TestCase(1, 9)]
        [TestCase(2, 11)]
        [TestCase(3, 13)]
        [TestCase(4, 15)]
        [TestCase(5, 17)]
        [TestCase(6, 19)]
        [TestCase(7, 21)]
        [TestCase(8, 23)]
        [TestCase(9, 25)]
        [TestCase(10, 27)]
        [TestCase(11, 29)]
        [TestCase(12, 31)]
        [TestCase(13, 32)]
        [TestCase(14, 32)]
        [TestCase(20, 32)]
        [TestCase(50, 32)]
        [TestCase(999, 32)]
        public void TargetActiveEnemies_SabitBeklenenDeger(int round, int beklenen)
        {
            Assert.AreEqual(beklenen, _rounds.TargetActiveEnemies(round),
                "R" + round + " hedef aktif sayısı min(7+2R,32) ile uyuşmuyor.");
        }

        /// <summary>
        /// Sabotaj S-1'in yakalayıcısı: tavan kaldırılırsa bu test kırmızı yanar.
        /// Tavan ilk kez R13'te bağlar (7+2*13 = 33 > 32).
        /// </summary>
        [Test]
        public void TargetActiveEnemies_Tavan32TamOlarakR13teBaglar()
        {
            Assert.AreEqual(31, _rounds.TargetActiveEnemies(12), "R12 tavana değmemeli.");
            Assert.AreEqual(32, _rounds.TargetActiveEnemies(13), "R13'te tavan 32'ye kırpmalı.");

            for (int r = 13; r <= 200; r++)
            {
                Assert.AreEqual(32, _rounds.TargetActiveEnemies(r),
                    "R" + r + " tavanı aştı; min(7+2R,32) ihlal edildi.");
            }
        }

        [Test]
        public void TargetActiveEnemies_MutlakGuvenlikTavaniniAsmaz()
        {
            Assert.AreEqual(40, _rounds.AbsoluteSafetyCeiling, "Mutlak güvenlik tavanı 40 olmalı.");

            for (int r = 1; r <= 500; r++)
            {
                Assert.LessOrEqual(_rounds.TargetActiveEnemies(r), 40,
                    "R" + r + " mutlak 40 tavanını aştı.");
                Assert.GreaterOrEqual(_rounds.TargetActiveEnemies(r), 1,
                    "R" + r + " hedef aktif 1'in altına düştü.");
            }
        }

        /// <summary>
        /// Bilinen ve kasıtlı sınırlama: ActiveCap 32 iken performans hedefi "40 civarı".
        /// Ölçülen bulgu — hata değil. 40 yükü yalnız F1 zorlamasıyla oluşur.
        /// </summary>
        [Test]
        public void OlculenBulgu_DogalTavan32PerformansHedefi40tanDusuk()
        {
            int dogalTavan = _rounds.TargetActiveEnemies(1000);
            Assert.AreEqual(32, dogalTavan);
            Assert.Less(dogalTavan, _rounds.AbsoluteSafetyCeiling,
                "Doğal tavan mutlak tavana eşitse '40 yükü doğal oluşmaz' bulgusu geçersizdir.");
        }

        // ---------------------------------------------------------- can/hasar çarpanı

        [TestCase(1, 1.00f)]
        [TestCase(2, 1.12f)]
        [TestCase(3, 1.24f)]
        [TestCase(4, 1.36f)]
        [TestCase(5, 1.48f)]
        [TestCase(8, 1.84f)]
        [TestCase(10, 2.08f)]
        [TestCase(15, 2.68f)]
        public void EnemyHealthMultiplier_SabitBeklenenDeger(int round, float beklenen)
        {
            Assert.AreEqual(beklenen, _rounds.EnemyHealthMultiplier(round), 0.0005f,
                "R" + round + " can çarpanı 1+0.12(R-1) ile uyuşmuyor.");
        }

        [TestCase(1, 1.00f)]
        [TestCase(2, 1.08f)]
        [TestCase(3, 1.16f)]
        [TestCase(5, 1.32f)]
        [TestCase(8, 1.56f)]
        [TestCase(10, 1.72f)]
        [TestCase(15, 2.12f)]
        public void EnemyDamageMultiplier_SabitBeklenenDeger(int round, float beklenen)
        {
            Assert.AreEqual(beklenen, _rounds.EnemyDamageMultiplier(round), 0.0005f,
                "R" + round + " hasar çarpanı 1+0.08(R-1) ile uyuşmuyor.");
        }

        [Test]
        public void Carpanlar_R1deTamOlarakBir()
        {
            Assert.AreEqual(1f, _rounds.EnemyHealthMultiplier(1), 0.0001f);
            Assert.AreEqual(1f, _rounds.EnemyDamageMultiplier(1), 0.0001f);
            Assert.AreEqual(1f, _rounds.EnemyHealthMultiplier(0), 0.0001f, "R<1 çarpanı 1'in altına inmemeli.");
            Assert.AreEqual(1f, _rounds.EnemyDamageMultiplier(-3), 0.0001f, "R<1 çarpanı 1'in altına inmemeli.");
        }

        // ------------------------------------------------------------------ DEC-021

        /// <summary>
        /// DEC-021'in elle-tanımlı yarısı prototipte YOK (kullanıcı kararı t-0002).
        /// Bu test o kararın hâlâ geçerli olduğunu kilitler: liste dolarsa kırmızı yanar.
        /// </summary>
        [Test]
        public void OlculenBulgu_Dec021ElleTanimliYariBos()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                Assert.AreEqual(0, config.RoundDifficultyOverrides.Count,
                    "DEC-021 override listesi dolmuş; prototip artık elle zorluk kararı veriyor.");
                Assert.IsFalse(config.TryGetRoundOverride(1, out _));
                Assert.IsFalse(config.TryGetRoundOverride(8, out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
