using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-008 · gold ödül modeli (DEC-043 / REQ-074): Total = Base + Version Bonus + Spawn
    /// Armor Bonus — saf, deterministik, sahnesiz. Sayılar config'ten okunur (PrototypeOnly),
    /// hiçbir sayı kilitlenmez; test formülü ve idempotent hak edişi ölçer.
    ///
    /// ÇÜRÜTME KOŞULU: bir bileşen yanlış toplanırsa, bir kombinasyon eksik sayılırsa,
    /// snapshot sonrası bir şey değeri değiştirirse veya aynı yaşam iki kez ödenirse test
    /// kırmızıdır.
    /// </summary>
    [TestFixture]
    public sealed class GoldRewardBreakdownTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private int Standard => _config.GoldReward(EnemyTier.Standard);

        // ------------------------------------------------------------------ 1 · base

        [Test]
        public void BaseOdul_StandardDegeri_HerVersiyonIcinAyni()
        {
            Assert.Greater(Standard, 0, "Test öncülü: Standard gold değeri pozitif.");
            Assert.AreEqual(Standard, GoldRewardCalculator.BaseReward(_config));

            foreach (EnemyTier tier in System.Enum.GetValues(typeof(EnemyTier)))
            {
                GoldRewardValue v = GoldRewardCalculator.Compute(tier, SpawnArmorComposition.None, _config);
                Assert.AreEqual(Standard, v.Base, tier + " base ≠ Standard değeri");
            }

            Assert.AreEqual(0, GoldRewardCalculator.BaseReward(null), "Config yokken base 0.");
        }

        // ------------------------------------------------------------------ 2 · version bonus

        [Test]
        public void VersiyonBonusu_StandardSifir_DigerleriDegerEksiBase_ToplamConfigDegeri()
        {
            foreach (EnemyTier tier in System.Enum.GetValues(typeof(EnemyTier)))
            {
                int configValue = _config.GoldReward(tier);
                int expectedBonus = Mathf.Max(0, configValue - Standard);
                GoldRewardValue v = GoldRewardCalculator.Compute(tier, SpawnArmorComposition.None, _config);

                Assert.AreEqual(expectedBonus, v.VariantContribution, tier + " version bonus");
                Assert.AreEqual(0, v.ArmorContribution, tier + " zırhsız → armor 0");
                Assert.AreEqual(configValue, v.Total, tier + " zırhsız toplam = config değeri (geriye dönük davranış korunur)");
            }

            Assert.AreEqual(0, GoldRewardCalculator.VersionBonus(EnemyTier.Standard, _config));
            Assert.AreEqual(0, GoldRewardCalculator.VersionBonus(EnemyTier.Ultra, null));

            // Sıra: Special ≥ Standard, Elite ≥ Special, Ultra ≥ Elite (prototip merdiveni 2/5/10/25).
            Assert.GreaterOrEqual(GoldRewardCalculator.VersionBonus(EnemyTier.Ultra, _config), GoldRewardCalculator.VersionBonus(EnemyTier.Elite, _config));
            Assert.GreaterOrEqual(GoldRewardCalculator.VersionBonus(EnemyTier.Elite, _config), GoldRewardCalculator.VersionBonus(EnemyTier.Special, _config));
        }

        // ------------------------------------------------------------------ 3 · eight armour combinations

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public void ZirhKombinasyonu_SlotBonuslariToplanir(bool helmet, bool body, bool leg)
        {
            EconomyConfigSection e = _config.Economy;
            int expected = (helmet ? e.ArmorGoldBonusHelmet : 0) + (body ? e.ArmorGoldBonusBody : 0) + (leg ? e.ArmorGoldBonusLeg : 0);
            var armor = new SpawnArmorComposition(helmet, body, leg);

            Assert.AreEqual(expected, GoldRewardCalculator.ArmorBonus(armor, _config));
            Assert.AreEqual((helmet ? 1 : 0) + (body ? 1 : 0) + (leg ? 1 : 0), armor.PieceCount);

            foreach (EnemyTier tier in System.Enum.GetValues(typeof(EnemyTier)))
            {
                GoldRewardValue v = GoldRewardCalculator.Compute(tier, armor, _config);
                Assert.AreEqual(expected, v.ArmorContribution, tier + " armor bonus");
                Assert.AreEqual(Standard + Mathf.Max(0, _config.GoldReward(tier) - Standard) + expected, v.Total, tier + " toplam");
                Assert.AreEqual(v.Base + v.VariantContribution + v.ArmorContribution, v.Total, "Toplam üç yuvanın toplamı.");
            }
        }

        [Test]
        public void ZirhBonusu_NegatifConfig_SifiraKirpilir_ArmorSlotNone_Sifir()
        {
            _config.Economy.ArmorGoldBonusHelmet = -5;
            Assert.AreEqual(0, _config.Economy.ArmorGoldBonusFor(ArmorSlot.Helmet));
            Assert.AreEqual(0, _config.Economy.ArmorGoldBonusFor(ArmorSlot.None));
            Assert.AreEqual(_config.Economy.ArmorGoldBonusBody, GoldRewardCalculator.ArmorBonus(new SpawnArmorComposition(true, true, false), _config));
        }

        // ------------------------------------------------------------------ 4 · snapshot immutability

        [Test]
        public void Snapshot_SpawnAnindaDonar_SonrakiConfigDegisikligiVeyaZirhKaybiDegeriDegistirmez()
        {
            var armor = new SpawnArmorComposition(true, true, true);
            EnemyRewardSnapshot snap = GoldRewardCalculator.Snapshot(EnemyTier.Elite, armor, _config);
            int totalAtSpawn = snap.Total;
            Assert.IsTrue(snap.IsValid);
            Assert.AreEqual(EnemyTier.Elite, snap.Tier);
            Assert.AreEqual(3, snap.Armor.PieceCount);

            // "Zırh kırıldı": snapshot bir değer nesnesidir; canlı zırh durumu okunmaz. Config
            // bile sonradan değişse snapshot aynı kalır (ölümde yeniden hesap yok).
            _config.Economy.ArmorGoldBonusHelmet += 10;
            _config.Enemies.Find(EnemyTier.Elite).GoldReward += 10;
            Assert.AreEqual(totalAtSpawn, snap.Total, "Snapshot spawn anındaki değeri taşır.");

            // Ölüm anındaki çözümleme snapshot'ı aynen öder; olayın düz alanı ve config yok sayılır.
            GoldRewardValue paid = GoldRewardValue.Resolve(in snap, 999, EnemyTier.Standard, _config);
            Assert.AreEqual(totalAtSpawn, paid.Total);
            Assert.AreEqual(snap.Base, paid.Base);
            Assert.AreEqual(snap.VersionBonus, paid.VariantContribution);
            Assert.AreEqual(snap.ArmorBonus, paid.ArmorContribution);

            // Snapshot'sız (yabancı üretici) → WP-004 kuralı: düz değer, yalnız base yuvası.
            GoldRewardValue fallback = GoldRewardValue.Resolve(in Default, 7, EnemyTier.Standard, _config);
            Assert.AreEqual(7, fallback.Total);
            Assert.AreEqual(0, fallback.ArmorContribution);
            Assert.AreEqual(0, fallback.VariantContribution);
        }

        private static readonly EnemyRewardSnapshot Default = default;

        [Test]
        public void Deterministik_AyniGirdiAyniCikti_RastgelelikYok()
        {
            var armor = new SpawnArmorComposition(true, false, true);
            GoldRewardValue a = GoldRewardCalculator.Compute(EnemyTier.Ultra, armor, _config);
            for (int i = 0; i < 100; i++)
            {
                GoldRewardValue b = GoldRewardCalculator.Compute(EnemyTier.Ultra, armor, _config);
                Assert.AreEqual(a.Total, b.Total);
                Assert.AreEqual(a.ArmorContribution, b.ArmorContribution);
                Assert.AreEqual(a.VariantContribution, b.VariantContribution);
            }
        }

        // ------------------------------------------------------------------ 5/6 · one reward per life, replay idempotent

        [Test]
        public void AyniLifeId_TekOdul_TekrarOlumDespawnReplay_IkinciOdulYok()
        {
            var ledger = new GoldRewardLedger();
            ledger.BeginRun(1, lifeWatermark: 0);
            EnemyRewardSnapshot snap = GoldRewardCalculator.Snapshot(EnemyTier.Special, new SpawnArmorComposition(false, true, false), _config);
            int value = GoldRewardValue.Resolve(in snap, 0, EnemyTier.Special, _config).Total;
            Assert.Greater(value, 0);

            Assert.AreEqual(GoldRewardVerdict.Granted, ledger.TryClaim(41, value), "İlk ölüm olayı hak eder.");
            Assert.AreEqual(GoldRewardVerdict.Duplicate, ledger.TryClaim(41, value), "Aynı yaşamın ikinci ölüm olayı (replay) hak etmez.");
            Assert.AreEqual(GoldRewardVerdict.Duplicate, ledger.TryClaim(41, value + 100), "Farklı değerle replay bile hak etmez.");
            Assert.AreEqual(1, ledger.Granted);
            Assert.AreEqual(2, ledger.RejectedDuplicate);

            // Despawn / corpse: olay üretmez — ledger'a hiçbir çağrı gelmez; sayaç sabit kalır.
            Assert.AreEqual(1, ledger.Granted);

            // Yeni yaşam (havuzdan geri dönen gövde) yeni id ile yeniden hak eder; eski id hâlâ kapalı.
            Assert.AreEqual(GoldRewardVerdict.Granted, ledger.TryClaim(42, value));
            Assert.AreEqual(GoldRewardVerdict.Duplicate, ledger.TryClaim(41, value));
            Assert.AreEqual(2, ledger.Granted);
        }

        [Test]
        public void Cuzdan_AyniTransactionReplay_ToplamiIkiKezArtirmaz()
        {
            var wallet = new GoldWallet();
            wallet.InitializeForRun(3);
            int tx = wallet.NextTransactionId();
            Assert.AreEqual(GoldTransactionResult.Applied, wallet.Credit(6, GoldSource.EnemyPickup, tx));
            Assert.AreEqual(GoldTransactionResult.Duplicate, wallet.Credit(6, GoldSource.EnemyPickup, tx));
            Assert.AreEqual(6, wallet.Balance);
            Assert.AreEqual(6, wallet.TotalEarned);
        }
    }
}
