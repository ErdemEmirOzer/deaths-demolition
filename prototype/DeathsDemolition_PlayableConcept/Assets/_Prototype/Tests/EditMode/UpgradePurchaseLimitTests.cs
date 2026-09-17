using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-049C · round başına satın alma sayacı (DEC-087, Ö-6a onayı 2026-09-17: sevk 2 kart; DEC-071'in tek
    /// satın alması kalktı). Testler 1 ve 2 değerlerinde aynı sözleşmeyi doğrular: sayaç limite kadar alır, limitte
    /// PurchaseLimitReached; reroll İLK satın almada kapanır (DEC-050 değişmez); yeniden açılış hakkı
    /// yenilemez (DEC-071 oturum kuralı); klamp 1..3; simülatör sevk kuralını config'den okur.
    /// </summary>
    [TestFixture]
    public sealed class UpgradePurchaseLimitTests
    {
        private PrototypeConfig _config;
        private GoldWallet _cuzdan;
        private WeaponModifierState _rank;
        private RoundUpgradeShop _dukkan;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _cuzdan = new GoldWallet();
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(5000, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            _rank = new WeaponModifierState();
            _dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(2024) };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void SevkVerisi_DEC087_IkiKart()
        {
            Assert.IsNotNull(PrototypeConfig.Active);
            Assert.AreEqual(2, UpgradeOfferPolicy.PurchaseLimit(PrototypeConfig.Active), "DEC-087 (Ö-6a): sevkte round başına iki kart");
            Assert.AreEqual(2, new EconomyConfigSection().PurchasesPerRound, "kod varsayılanı da 2");
            Assert.AreEqual(UpgradeOfferPolicy.MaxPurchasesPerRound, UpgradeOfferPolicy.PurchaseLimit(null), "config yoksa DEC-071 geri dönüşü (1)");
        }

        [Test]
        public void Klamp_1_ile_KartSayisi()
        {
            _config.Economy.PurchasesPerRound = 0;
            Assert.AreEqual(1, UpgradeOfferPolicy.PurchaseLimit(_config), "0 → 1");
            _config.Economy.PurchasesPerRound = -4;
            Assert.AreEqual(1, UpgradeOfferPolicy.PurchaseLimit(_config), "negatif → 1");
            _config.Economy.PurchasesPerRound = 9;
            Assert.AreEqual(UpgradeOfferPolicy.CardsPerOffer, UpgradeOfferPolicy.PurchaseLimit(_config), "üç karttan fazlası alınamaz");
            _config.Economy.PurchasesPerRound = 2;
            Assert.AreEqual(2, UpgradeOfferPolicy.PurchaseLimit(_config));
        }

        [Test]
        public void Limit1_MevcutSozlesme_IkinciKartReddedilir()
        {
            _config.Economy.PurchasesPerRound = 1;
            _dukkan.Open(1, _config, _cuzdan, _rank, 1);
            Assert.AreEqual(1, _dukkan.PurchaseLimit);
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.IsTrue(_dukkan.IsLocked);
            Assert.AreEqual(UpgradeEntitlement.Used, _dukkan.PurchaseEntitlement);
            Assert.IsFalse(_dukkan.TryPurchase(1, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);
            Assert.AreEqual(1, _dukkan.PurchasesThisRound);
        }

        [Test]
        public void Limit2_IkiKartAlinir_UcuncuReddedilir_RerollIlkAlimdaKapanir()
        {
            _config.Economy.PurchasesPerRound = 2;
            _dukkan.Open(1, _config, _cuzdan, _rank, 1);
            Assert.AreEqual(2, _dukkan.PurchaseLimit);
            Assert.AreEqual(RerollAvailability.Available, _dukkan.RerollAvailability, "satın alma öncesi reroll açık (DEC-050)");

            int bakiye = _cuzdan.Balance;
            UpgradeCardOffer ilk = _dukkan.Cards[0];
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision), "birinci kart");
            Assert.AreEqual(bakiye - ilk.Cost, _cuzdan.Balance);
            Assert.IsFalse(_dukkan.IsLocked, "limit 2: ilk alımdan sonra teklif hâlâ açık");
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Locked, _dukkan.RerollEntitlement, "DEC-050: reroll İLK satın almada kapanır");
            Assert.AreEqual(RerollAvailability.LockedByPurchase, _dukkan.RerollAvailability);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision), "reroll artık yok");
            Assert.IsFalse(_dukkan.IsPurchasable(0), "alınan kart tekrar alınamaz");
            Assert.IsTrue(_dukkan.IsPurchasable(1), "ikinci kart alınabilir");

            int revizyon = _dukkan.Revision;
            UpgradeCardOffer ikinci = _dukkan.Cards[1];
            bakiye = _cuzdan.Balance;
            Assert.IsTrue(_dukkan.TryPurchase(1, _dukkan.SessionId, _dukkan.Revision), "ikinci kart");
            Assert.AreEqual(bakiye - ikinci.Cost, _cuzdan.Balance, "ikinci kart kendi fiyatını keser");
            Assert.AreEqual(revizyon + 1, _dukkan.Revision, "her satın alma revizyonu artırır");
            Assert.AreEqual(2, _dukkan.PurchasesThisRound);
            Assert.AreEqual(ilk.Cost + ikinci.Cost, _dukkan.GoldSpentThisRound);
            Assert.AreEqual(1, _rank.GetTier(ilk.Branch)); Assert.AreEqual(1, _rank.GetTier(ikinci.Branch));
            Assert.IsTrue(_dukkan.IsLocked, "limitte teklif kilitlenir; yalnız Continue");
            Assert.AreEqual(UpgradeEntitlement.Used, _dukkan.PurchaseEntitlement);

            bakiye = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(2, _dukkan.SessionId, _dukkan.Revision), "üçüncü kart yok");
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance, "ret harcamaz");
            Assert.IsFalse(_dukkan.IsPurchasable(2));
        }

        [Test]
        public void Limit2_YenidenAcilis_SayaciYenilemez()
        {
            _config.Economy.PurchasesPerRound = 2;
            _dukkan.Open(3, _config, _cuzdan, _rank, 7);
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            int oturum = _dukkan.SessionId;
            List<UpgradeBranch> kartlar = new List<UpgradeBranch>();
            for (int i = 0; i < _dukkan.Cards.Count; i++) kartlar.Add(_dukkan.Cards[i].Branch);

            // Ekran kapanıp açılır / pause-resume: aynı round + run → sayaç ve teklif korunur (DEC-071 oturum kuralı).
            _dukkan.Close();
            _dukkan.Open(3, _config, _cuzdan, _rank, 7);
            Assert.AreEqual(oturum, _dukkan.SessionId);
            Assert.AreEqual(1, _dukkan.PurchasesThisRound, "sayaç yenilenmez");
            Assert.AreEqual(2, _dukkan.PurchaseLimit);
            for (int i = 0; i < _dukkan.Cards.Count; i++) Assert.AreEqual(kartlar[i], _dukkan.Cards[i].Branch, "teklif aynı");
            Assert.IsTrue(_dukkan.TryPurchase(1, _dukkan.SessionId, _dukkan.Revision), "kalan hak kullanılır");
            Assert.IsTrue(_dukkan.IsLocked);

            // Sonraki round yeni oturum: sayaç sıfır.
            _dukkan.Close();
            _dukkan.Open(4, _config, _cuzdan, _rank, 7);
            Assert.AreEqual(0, _dukkan.PurchasesThisRound);
            Assert.IsFalse(_dukkan.IsLocked);
        }

        [Test]
        public void Simulator_SevkKuraliniConfigtenOkur()
        {
            Assert.IsNotNull(PrototypeConfig.Active);
            PrototypeConfig klon = Object.Instantiate(PrototypeConfig.Active);
            try
            {
                klon.Economy.PurchasesPerRound = 1;
                EconomySimulator.Report bir = EconomySimulator.Run(new EconomySimulator.Options { config = klon, seed = 1u, firstRound = 1, lastRound = 8 });
                EconomySimulator.Report sevk = EconomySimulator.Run(new EconomySimulator.Options { config = PrototypeConfig.Active, seed = 1u, firstRound = 1, lastRound = 8 });
                int harcamaBir = 0, harcamaSevk = 0;
                foreach (EconomySimulator.RoundRow r in bir.weapons[0].rounds) harcamaBir += r.goldSpent;
                foreach (EconomySimulator.RoundRow r in sevk.weapons[0].rounds) harcamaSevk += r.goldSpent;
                Assert.Greater(harcamaSevk, harcamaBir, "sevk (2 kart) senaryo olmadan da eski kuraldan (1 kart) fazla harcar");
                Assert.AreEqual(2, UpgradeOfferPolicy.PurchaseLimit(PrototypeConfig.Active), "sevk varlığı dokunulmadı");
            }
            finally
            {
                Object.DestroyImmediate(klon);
            }
        }
    }
}
