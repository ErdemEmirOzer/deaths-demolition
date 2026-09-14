using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-008 · round sonu geliştirme OTURUMU (DEC-047/048/049/050, REQ-078/079) — sahnesiz,
    /// saf: kendi cüzdanı, enjekte rank otoritesi, enjekte unlock yüklemi ve tohumlu RNG.
    /// Hiçbir dal/maliyet sayısı kilitlenmez; kartlar teklif üzerinden okunur.
    ///
    /// ÇÜRÜTME KOŞULU: bir hak yenilenirse, bir komut iki kez para harcarsa, kart sayısı
    /// üçten saparsa, geçersiz teklif para keserse veya reroll üçünü birden değiştirirse
    /// test kırmızıdır.
    /// </summary>
    [TestFixture]
    public sealed class UpgradeOfferSessionTests
    {
        private PrototypeConfig _config;
        private GoldWallet _cuzdan;
        private WeaponModifierState _rank;
        private RoundUpgradeShop _dukkan;
        private readonly List<UpgradeCardOffer> _satinAlinanlar = new List<UpgradeCardOffer>();
        private readonly List<int> _reroller = new List<int>();
        private int _teklifDegisti;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            _cuzdan = new GoldWallet();
            _cuzdan.InitializeForRun(1);
            _rank = new WeaponModifierState();
            _dukkan = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(2024) };
            _satinAlinanlar.Clear();
            _reroller.Clear();
            _teklifDegisti = 0;
            _dukkan.Purchased += c => _satinAlinanlar.Add(c);
            _dukkan.Rerolled += c => _reroller.Add(c);
            _dukkan.OfferChanged += () => _teklifDegisti++;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        private void Ac(int round = 1, int gold = 1000, int runId = 1)
        {
            if (gold > 0)
            {
                _cuzdan.Credit(gold, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            }

            _dukkan.Open(round, _config, _cuzdan, _rank, runId);
            Assert.IsTrue(_dukkan.IsOpen);
        }

        private static List<UpgradeBranch> Dallar(RoundUpgradeShop shop)
        {
            var list = new List<UpgradeBranch>();
            for (int i = 0; i < shop.Cards.Count; i++) list.Add(shop.Cards[i].Branch);
            return list;
        }

        private static void UcFarkliGecerli(RoundUpgradeShop shop)
        {
            Assert.AreEqual(UpgradeOfferStatus.Ready, shop.Status);
            Assert.AreEqual(UpgradeOfferPolicy.CardsPerOffer, shop.Cards.Count, "Tam üç kart.");
            for (int i = 0; i < shop.Cards.Count; i++)
            {
                Assert.IsTrue(shop.Cards[i].IsValid);
                Assert.AreEqual(UpgradeEligibility.Eligible, shop.Eligibility(shop.Cards[i].Branch), shop.Cards[i].Branch + " uygun olmalı.");
                for (int j = i + 1; j < shop.Cards.Count; j++)
                {
                    Assert.AreNotEqual(shop.Cards[i].Branch, shop.Cards[j].Branch, "Aynı upgrade kimliği iki kez gösterilemez.");
                }
            }
        }

        // ------------------------------------------------------------------ 7 · three distinct valid cards

        [Test]
        public void HerAcilis_TamUcFarkliGecerliKart()
        {
            for (int round = 1; round <= 30; round++)
            {
                _dukkan.Open(round, _config, _cuzdan, _rank, 1);
                UcFarkliGecerli(_dukkan);
                Assert.AreEqual(round, _dukkan.Round);
                Assert.AreEqual(1, _dukkan.Revision, "Yeni teklif revizyon 1'den başlar.");
                Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.PurchaseEntitlement);
                Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.RerollEntitlement);
                _dukkan.Close();
            }
        }

        // ------------------------------------------------------------------ 8 · locked / maxed filtered

        [Test]
        public void KilitliVeMaxlanmisDallar_TeklifeGirmez()
        {
            _dukkan.UnlockedPredicate = b => b != UpgradeBranch.Chain && b != UpgradeBranch.Explosion;
            _rank.SetTier(UpgradeBranch.Piercing, _config.Economy.Find(UpgradeBranch.Piercing).MaxTier, _config);

            for (int round = 1; round <= 40; round++)
            {
                _dukkan.Open(round, _config, _cuzdan, _rank, 1);
                UcFarkliGecerli(_dukkan);
                List<UpgradeBranch> dallar = Dallar(_dukkan);
                Assert.IsFalse(dallar.Contains(UpgradeBranch.Chain), "Kilitli dal teklif edilmez.");
                Assert.IsFalse(dallar.Contains(UpgradeBranch.Explosion), "Kilitli dal teklif edilmez.");
                Assert.IsFalse(dallar.Contains(UpgradeBranch.Piercing), "Maxlanmış dal teklif edilmez.");
                Assert.AreEqual(UpgradeEligibility.Locked, _dukkan.Eligibility(UpgradeBranch.Chain));
                Assert.AreEqual(UpgradeEligibility.Maxed, _dukkan.Eligibility(UpgradeBranch.Piercing), "Açık dükkân enjekte rank otoritesini okur.");
                Assert.AreEqual(UpgradeEligibility.Eligible, _dukkan.Eligibility(UpgradeBranch.FireRate));
                _dukkan.Close();
            }
        }

        // ------------------------------------------------------------------ 9/10 · expensive card shown, purchase refused

        [Test]
        public void PahaliKart_GosterilirAmaSatinAlinamaz_HarcamaYok()
        {
            Ac(gold: 0);
            UcFarkliGecerli(_dukkan);
            _cuzdan.Credit(1, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());   // 1 gold: her kart pahalı (min maliyet 15)

            for (int i = 0; i < _dukkan.Cards.Count; i++)
            {
                Assert.Greater(_dukkan.Cards[i].Cost, _cuzdan.Balance, "Test öncülü: kart bakiyeden pahalı.");
                Assert.IsTrue(_dukkan.IsPurchasable(i), "Pahalı kart yine de geçerli/satılabilir listede.");
                Assert.IsFalse(_dukkan.CanAfford(i), "Satın alma düğmesi devre dışı.");
            }

            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.InsufficientGold, _dukkan.LastResult);
            Assert.AreEqual(1, _cuzdan.Balance);
            Assert.AreEqual(0, _cuzdan.TotalSpent);
            Assert.AreEqual(0, _rank.TotalTiers);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.PurchaseEntitlement, "Ret hak tüketmez.");
            Assert.AreEqual(1, _dukkan.Revision, "Ret revizyonu değiştirmez.");
        }

        // ------------------------------------------------------------------ 11 · one purchase per round

        [Test]
        public void RoundBasinaTekSatinAlma_IkinciKartReddedilir_TeklifKilitlenir()
        {
            Ac();
            UpgradeCardOffer ilk = _dukkan.Cards[0];
            UpgradeCardOffer ikinci = _dukkan.Cards[1];

            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            int bakiye = _cuzdan.Balance;
            int tierler = _rank.TotalTiers;
            Assert.AreEqual(1000 - ilk.Cost, bakiye);
            Assert.AreEqual(1, tierler);
            Assert.IsTrue(_dukkan.IsLocked);
            Assert.AreEqual(UpgradeEntitlement.Used, _dukkan.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Locked, _dukkan.RerollEntitlement, "Satın alma reroll'u kapatır.");
            Assert.AreEqual(2, _dukkan.Revision);

            Assert.IsFalse(_dukkan.TryPurchase(1, _dukkan.SessionId, _dukkan.Revision), "İkinci kart alınamaz.");
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance, "İkinci deneme harcamaz.");
            Assert.AreEqual(tierler, _rank.TotalTiers);
            Assert.AreEqual(0, _rank.GetTier(ikinci.Branch));
            Assert.IsFalse(_dukkan.IsPurchasable(1));
            Assert.IsFalse(_dukkan.CanAfford(1));
            Assert.AreEqual(1, _satinAlinanlar.Count);
            Assert.AreEqual(1, _dukkan.PurchasesThisRound);
            Assert.AreEqual(RerollAvailability.LockedByPurchase, _dukkan.RerollAvailability);
        }

        // ------------------------------------------------------------------ 12 · reopen / pause do not renew

        [Test]
        public void AyniRound_YenidenAcilis_HakkiVeTeklifiYenilemez()
        {
            Ac();
            List<UpgradeBranch> kartlar = Dallar(_dukkan);
            int oturum = _dukkan.SessionId;
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            int bakiye = _cuzdan.Balance;
            int revizyon = _dukkan.Revision;

            // "Ekran kapatılıp açıldı" / "pause-resume" / "aynı round oturumu yeniden bağlandı".
            for (int i = 0; i < 3; i++)
            {
                _dukkan.Close();
                Assert.IsFalse(_dukkan.IsOpen);
                _dukkan.Open(1, _config, _cuzdan, _rank, 1);
                Assert.IsTrue(_dukkan.IsOpen);
                Assert.AreEqual(oturum, _dukkan.SessionId, "Aynı round: aynı teklif kimliği (ücretsiz yeni teklif yok).");
                Assert.AreEqual(revizyon, _dukkan.Revision);
                CollectionAssert.AreEqual(kartlar, Dallar(_dukkan), "Kartlar aynen korunur.");
                Assert.IsTrue(_dukkan.Cards[0].Purchased);
                Assert.IsTrue(_dukkan.IsLocked, "Satın alma hakkı yenilenmez.");
                Assert.AreEqual(UpgradeEntitlement.Locked, _dukkan.RerollEntitlement, "Reroll hakkı yenilenmez.");
                Assert.IsFalse(_dukkan.TryPurchase(1, _dukkan.SessionId, _dukkan.Revision));
                Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);
                Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
                Assert.AreEqual(UpgradeRerollResult.OfferLocked, _dukkan.LastRerollResult);
                Assert.AreEqual(bakiye, _cuzdan.Balance);
            }

            // Reroll hakkı da yeniden açılışta yenilenmez (satın alma yokken).
            _dukkan.DiscardOffer();
            _dukkan.Open(2, _config, _cuzdan, _rank, 1);
            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision), "Yeni round: reroll hakkı taze.");
            int bakiye2 = _cuzdan.Balance;
            _dukkan.Close();
            _dukkan.Open(2, _config, _cuzdan, _rank, 1);
            Assert.AreEqual(UpgradeEntitlement.Used, _dukkan.RerollEntitlement, "Yeniden açılış reroll hakkını geri vermez.");
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.RerollUsed, _dukkan.LastRerollResult);
            Assert.AreEqual(bakiye2, _cuzdan.Balance);

            // Yeni round → yeni teklif, taze haklar; farklı koşu → yeni teklif.
            _dukkan.Close();
            _dukkan.Open(3, _config, _cuzdan, _rank, 1);
            Assert.AreNotEqual(oturum, _dukkan.SessionId);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.RerollEntitlement);
        }

        // ------------------------------------------------------------------ 13 · replay of the same purchase command

        [Test]
        public void AyniSatinAlmaKomutu_TekrarOynatilir_IkinciHarcamaYok_IkinciUpgradeYok()
        {
            Ac();
            int oturum = _dukkan.SessionId;
            int revizyon = _dukkan.Revision;
            UpgradeCardOffer kart = _dukkan.Cards[2];

            Assert.IsTrue(_dukkan.TryPurchase(2, oturum, revizyon));
            int bakiye = _cuzdan.Balance;
            int harcanan = _cuzdan.TotalSpent;

            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(_dukkan.TryPurchase(2, oturum, revizyon), "Aynı komut (aynı oturum + revizyon) yeniden oynatıldı.");
                Assert.AreEqual(UpgradePurchaseResult.StaleRevision, _dukkan.LastResult, "Satın alma revizyonu yükseltti; eski komut bayat.");
            }

            Assert.IsFalse(_dukkan.TryPurchase(2, oturum, _dukkan.Revision), "Güncel revizyonla bile aynı kart ikinci kez alınamaz.");
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance);
            Assert.AreEqual(harcanan, _cuzdan.TotalSpent);
            Assert.AreEqual(1, _rank.GetTier(kart.Branch));
            Assert.AreEqual(1, _satinAlinanlar.Count);
        }

        // ------------------------------------------------------------------ 14 · stale session / revision

        [Test]
        public void BayatOturumVeRevizyon_GuvenleReddedilir()
        {
            Ac(round: 1);
            int eskiOturum = _dukkan.SessionId;
            int eskiRevizyon = _dukkan.Revision;
            _dukkan.Close();
            _dukkan.Open(2, _config, _cuzdan, _rank, 1);
            int bakiye = _cuzdan.Balance;

            Assert.IsFalse(_dukkan.TryPurchase(0, eskiOturum, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.SessionMismatch, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.TryReroll(eskiOturum, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.SessionMismatch, _dukkan.LastRerollResult);

            // Reroll revizyonu yükseltir; reroll öncesi çizilmiş ekranın komutu bayattır.
            int revOnce = _dukkan.Revision;
            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, revOnce));
            Assert.AreEqual(revOnce + 1, _dukkan.Revision);
            int bakiyeReroll = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, revOnce));
            Assert.AreEqual(UpgradePurchaseResult.StaleRevision, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, revOnce));
            Assert.AreEqual(UpgradeRerollResult.StaleRevision, _dukkan.LastRerollResult);
            Assert.AreEqual(bakiyeReroll, _cuzdan.Balance, "Bayat komutlar harcamaz.");
            Assert.AreEqual(0, _rank.TotalTiers);
            Assert.AreEqual(bakiye - _dukkan.RerollCost, bakiyeReroll);
            Assert.AreEqual(eskiRevizyon, 1);
        }

        // ------------------------------------------------------------------ 15-19 · reroll

        [Test]
        public void BasariliTekReroll_BirVeyaIkiSlot_UcFarkli_GoldBirKez_IkinciReddedilir()
        {
            Ac();
            List<UpgradeBranch> once = Dallar(_dukkan);
            int cost = _dukkan.RerollCost;
            Assert.Greater(cost, 0, "Test öncülü: reroll ücretli.");
            Assert.AreEqual(RerollAvailability.Available, _dukkan.RerollAvailability);
            int bakiye = _cuzdan.Balance;
            int degisti = _teklifDegisti;

            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.Rerolled, _dukkan.LastRerollResult);
            UcFarkliGecerli(_dukkan);
            List<UpgradeBranch> sonra = Dallar(_dukkan);
            int fark = 0;
            for (int i = 0; i < 3; i++) if (once[i] != sonra[i]) fark++;
            Assert.That(fark, Is.InRange(1, 2), "Tam olarak bir veya iki slot değişir; en az bir kart korunur.");
            Assert.AreEqual(fark, _dukkan.LastRerollReplacedSlots.Count);
            Assert.AreEqual(bakiye - cost, _cuzdan.Balance, "Reroll ücreti tam bir kez kesildi.");
            Assert.AreEqual(cost, _cuzdan.SpentOnRerolls);
            Assert.AreEqual(0, _cuzdan.SpentOnUpgrades);
            Assert.AreEqual(cost, _cuzdan.TotalSpent);
            Assert.AreEqual(1, _reroller.Count);
            Assert.AreEqual(cost, _reroller[0]);
            Assert.AreEqual(1, _dukkan.RerollsThisRound);
            Assert.AreEqual(cost, _dukkan.GoldSpentThisRound);
            Assert.AreEqual(UpgradeEntitlement.Used, _dukkan.RerollEntitlement);
            Assert.AreEqual(2, _dukkan.Revision);
            Assert.Greater(_teklifDegisti, degisti, "OfferChanged yükseldi (UI polling'siz yeniden çizer).");
            Assert.AreEqual(0, _rank.TotalTiers, "Reroll build'e dokunmaz.");

            // 16 · ikinci reroll
            bakiye = _cuzdan.Balance;
            List<UpgradeBranch> kilit = Dallar(_dukkan);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.RerollUsed, _dukkan.LastRerollResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance);
            CollectionAssert.AreEqual(kilit, Dallar(_dukkan));
            Assert.AreEqual(RerollAvailability.Used, _dukkan.RerollAvailability);

            // Reroll sonrası satın alma hâlâ mümkün (reroll satın almadan önce kullanıldı).
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(cost + _dukkan.Cards[0].Cost, _cuzdan.TotalSpent, "Toplam harcama = reroll + satın alma.");
        }

        [Test]
        public void Reroll_SatinAlmaSonrasi_Reddedilir()
        {
            Ac();
            Assert.IsTrue(_dukkan.TryPurchase(1, _dukkan.SessionId, _dukkan.Revision));
            int bakiye = _cuzdan.Balance;
            List<UpgradeBranch> kartlar = Dallar(_dukkan);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.OfferLocked, _dukkan.LastRerollResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance);
            CollectionAssert.AreEqual(kartlar, Dallar(_dukkan));
            Assert.AreEqual(0, _reroller.Count);
            Assert.AreEqual(0, _cuzdan.SpentOnRerolls);
        }

        [Test]
        public void Reroll_YetersizGold_Reddedilir_HakKorunur()
        {
            Ac(gold: 0);
            int cost = _dukkan.RerollCost;
            _cuzdan.Credit(cost - 1, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            Assert.AreEqual(RerollAvailability.TooExpensive, _dukkan.RerollAvailability);
            Assert.IsFalse(_dukkan.CanReroll);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.InsufficientGold, _dukkan.LastRerollResult);
            Assert.AreEqual(cost - 1, _cuzdan.Balance);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.RerollEntitlement, "Ret hakkı tüketmez.");
            Assert.AreEqual(1, _dukkan.Revision);

            _cuzdan.Credit(1, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            Assert.AreEqual(RerollAvailability.Available, _dukkan.RerollAvailability);
            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(0, _cuzdan.Balance, "Tam ücret, tam bir kez.");
        }

        [Test]
        public void Reroll_Deterministik_AyniTohumAyniSonuc()
        {
            List<UpgradeBranch> a = RerollSonucu(77);
            List<UpgradeBranch> b = RerollSonucu(77);
            CollectionAssert.AreEqual(a, b, "Enjekte RNG ile reroll tekrar üretilebilir.");
        }

        private List<UpgradeBranch> RerollSonucu(int seed)
        {
            var wallet = new GoldWallet();
            wallet.InitializeForRun(9);
            wallet.Credit(500, GoldSource.EnemyPickup, wallet.NextTransactionId());
            var shop = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(seed) };
            shop.Open(1, _config, wallet, new WeaponModifierState(), 9);
            Assert.IsTrue(shop.TryReroll(shop.SessionId, shop.Revision));
            return Dallar(shop);
        }

        // ------------------------------------------------------------------ 20 · no valid offer -> no charge, safe error

        [Test]
        public void GecerliUcKartUretilemez_KonfigurasyonHatasi_HarcamaYok_KartUydurulmaz()
        {
            // Yalnız iki dal uygun kalsın: unlock yüklemi sekizini kilitler.
            _dukkan.UnlockedPredicate = b => b == UpgradeBranch.Piercing || b == UpgradeBranch.Chain;
            LogAssert.Expect(LogType.Error, new Regex("Upgrade offer configuration error: NotEnoughEligibleBranches"));
            Ac();

            Assert.IsTrue(_dukkan.IsOpen, "Ekran güvenli hata durumunda AÇIK kalır (Continue çalışır).");
            Assert.AreEqual(UpgradeOfferStatus.ConfigurationError, _dukkan.Status);
            Assert.AreEqual(UpgradeOfferFault.NotEnoughEligibleBranches, _dukkan.Fault);
            Assert.AreEqual(0, _dukkan.Cards.Count, "Duplicate/geçersiz kart uydurulmaz.");
            Assert.AreEqual(1000, _cuzdan.Balance, "Gold kesilmez.");
            Assert.AreEqual(RerollAvailability.Unavailable, _dukkan.RerollAvailability);

            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.OfferUnavailable, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.OfferUnavailable, _dukkan.LastRerollResult);
            Assert.AreEqual(1000, _cuzdan.Balance);
            Assert.AreEqual(0, _rank.TotalTiers);

            // Continue: kapanır, sonraki round temiz açılır (Continue davranışı açık).
            _dukkan.Close();
            Assert.IsFalse(_dukkan.IsOpen);
            _dukkan.UnlockedPredicate = _ => true;
            _dukkan.Open(2, _config, _cuzdan, _rank, 1);
            UcFarkliGecerli(_dukkan);
        }

        [Test]
        public void TabloBos_KonfigurasyonHatasi_GuvenliDurum()
        {
            _config.Economy.Branches.Clear();
            LogAssert.Expect(LogType.Error, new Regex("Upgrade offer configuration error: NoUpgradeTable"));
            Ac();
            Assert.AreEqual(UpgradeOfferStatus.ConfigurationError, _dukkan.Status);
            Assert.AreEqual(UpgradeOfferFault.NoUpgradeTable, _dukkan.Fault);
            Assert.AreEqual(0, _dukkan.Cards.Count);
            Assert.AreEqual(1000, _cuzdan.Balance);
        }

        [Test]
        public void TabloDuplicateDal_KonfigurasyonHatasi()
        {
            _config.Economy.Branches.Add(new UpgradeBranchSettings { Branch = UpgradeBranch.Piercing, DisplayName = "Piercing (dup)", OfferWeight = 1f, MaxTier = 3 });
            LogAssert.Expect(LogType.Error, new Regex("Upgrade offer configuration error: DuplicateBranch"));
            Ac();
            Assert.AreEqual(UpgradeOfferFault.DuplicateBranch, _dukkan.Fault);
            Assert.AreEqual(0, _dukkan.Cards.Count);
            Assert.AreEqual(1000, _cuzdan.Balance);
        }

        // ------------------------------------------------------------------ 21 · reroll cannot be produced -> gold and right kept

        [Test]
        public void Reroll_AlternatifDalYok_GoldVeHakKorunur_KartlarKalir()
        {
            // Tam üç dal uygun: teklif kurulur, reroll için değiştirilecek aday kalmaz.
            _dukkan.UnlockedPredicate = b => b == UpgradeBranch.Piercing || b == UpgradeBranch.Chain || b == UpgradeBranch.FireRate;
            Ac();
            UcFarkliGecerli(_dukkan);
            List<UpgradeBranch> kartlar = Dallar(_dukkan);
            Assert.AreEqual(RerollAvailability.NoAlternatives, _dukkan.RerollAvailability);

            LogAssert.Expect(LogType.Warning, new Regex("no eligible branch outside the current offer"));
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.NoReplacementAvailable, _dukkan.LastRerollResult);
            Assert.AreEqual(1000, _cuzdan.Balance, "Gold kesilmez.");
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.RerollEntitlement, "Hak korunur.");
            Assert.AreEqual(1, _dukkan.Revision);
            CollectionAssert.AreEqual(kartlar, Dallar(_dukkan), "Kartlar aynen kalır; satın alma hâlâ mümkün.");
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
        }

        [Test]
        public void Reroll_UcretSifir_KonfigurasyonHatasi_UcretsizRerollYok()
        {
            _config.Economy.RerollCost = 0;
            Ac();
            List<UpgradeBranch> kartlar = Dallar(_dukkan);
            Assert.AreEqual(RerollAvailability.Unavailable, _dukkan.RerollAvailability);
            LogAssert.Expect(LogType.Error, new Regex("RerollCost must be positive"));
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.InvalidCost, _dukkan.LastRerollResult);
            CollectionAssert.AreEqual(kartlar, Dallar(_dukkan));
            Assert.AreEqual(1000, _cuzdan.Balance);
        }

        // ------------------------------------------------------------------ 22 · Special Weapon isolation

        [Test]
        public void SpecialWeapon_TeklifVeRerollYolundanBagimsiz()
        {
            string root = Path.Combine(Application.dataPath, "_Prototype");
            string runtime = File.ReadAllText(Path.Combine(root, "Weapons", "SpecialWeaponRuntime.cs"));
            string director = File.ReadAllText(Path.Combine(root, "Weapons", "SpecialWeaponDirector.cs"));
            foreach (string yasak in new[] { "RoundUpgradeShop", "UpgradeOfferPolicy", "UpgradeCardOffer", "TryReroll", "RunBuildState" })
            {
                StringAssert.DoesNotContain(yasak, runtime, "SpecialWeaponRuntime teklif/reroll/build yüzeyine bağlanmamalı (REQ-059).");
                StringAssert.DoesNotContain(yasak, director, "SpecialWeaponDirector teklif/reroll/build yüzeyine bağlanmamalı (REQ-059).");
            }

            string shop = File.ReadAllText(Path.Combine(root, "Rounds", "RoundUpgradeShop.cs"));
            string policy = File.ReadAllText(Path.Combine(root, "Rounds", "UpgradeOfferPolicy.cs"));
            StringAssert.DoesNotContain("SpecialWeapon", shop, "Dükkân Special Weapon'a dokunmaz.");
            StringAssert.DoesNotContain("SpecialWeapon", policy);

            // Runtime yüzeyi: satın alma ve reroll yalnız enjekte WeaponModifierState'i değiştirir.
            Ac();
            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(1, _rank.TotalTiers);
        }

        // ------------------------------------------------------------------ 23/24 · Gold Spent accounting

        [Test]
        public void GoldSpent_SatinAlmaArtiReroll_TekSatir_ReplayIkiKezSaymaz()
        {
            Ac();
            var stats = new RunStatsTracker();
            _dukkan.Purchased += c => stats.RecordGoldSpent(c.Cost);
            _dukkan.Rerolled += c => stats.RecordRerollSpent(c);

            Assert.IsTrue(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            int reroll = _dukkan.RerollCost;
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            int kart = _dukkan.Cards[0].Cost;

            Assert.AreEqual(kart + reroll, _cuzdan.TotalSpent);
            Assert.AreEqual(kart, _cuzdan.SpentOnUpgrades);
            Assert.AreEqual(reroll, _cuzdan.SpentOnRerolls);
            Assert.AreEqual(_cuzdan.SpentOnUpgrades + _cuzdan.SpentOnRerolls, _cuzdan.TotalSpent);
            Assert.AreEqual(kart + reroll, stats.GoldSpent, "Tracker aynası: purchases + rerolls.");
            Assert.AreEqual(1, stats.UpgradesPurchased, "Reroll bir upgrade satın alması olarak sayılmaz.");
            Assert.AreEqual(1, stats.RerollsUsed);

            // Replay: komutlar yeniden oynatılır, toplamlar değişmez.
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, 1));
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, 2));
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(kart + reroll, _cuzdan.TotalSpent);
            Assert.AreEqual(kart + reroll, stats.GoldSpent);

            // Results satırı: RunSummary.GoldSpent tek sayı = purchases + rerolls; ayrı reroll satırı yok.
            var summary = new RunSummary
            {
                ReachedRound = 2, CompletedRounds = 1, Weapon = ClassicWeaponId.AssaultRifle, BuildDescription = _rank.DescribeBuild(),
                GoldEarned = _cuzdan.TotalEarned, GoldSpent = _cuzdan.TotalSpent, GoldSpentOnUpgrades = _cuzdan.SpentOnUpgrades,
                GoldSpentOnRerolls = _cuzdan.SpentOnRerolls, GoldUnspent = _cuzdan.Balance, UpgradesPurchased = stats.UpgradesPurchased,
                RerollsUsed = stats.RerollsUsed, EndReason = RunEndReason.PlayerDeath
            };
            var report = new DD.Prototype.Profile.RunRewardReport.Draft { HasRun = true, Summary = summary, LevelBefore = 1, LevelAfter = 1, LevelRequirementAfter = 100 }.Seal();
            ResultsViewModel vm = ResultsViewModel.From(report, id => id.ToString());
            int goldSpentLines = 0;
            for (int i = 0; i < vm.Economy.Count; i++)
            {
                StringAssert.DoesNotContain("Reroll", vm.Economy[i].Label, "Ayrı reroll satırı yok (karar 20).");
                if (vm.Economy[i].Label == "Gold Spent")
                {
                    goldSpentLines++;
                    Assert.AreEqual((kart + reroll).ToString(), vm.Economy[i].Value, "Gold Spent = purchases + rerolls.");
                }
            }

            Assert.AreEqual(1, goldSpentLines);
        }

        // ------------------------------------------------------------------ edge cases (test-engineer review)

        [Test]
        public void KapaliCuzdan_RerollVeSatinAlma_Reddedilir_HakKorunur()
        {
            Ac();
            List<UpgradeBranch> kartlar = Dallar(_dukkan);
            _cuzdan.ResetForRunEnd();   // ölüm sırası: cüzdan kapandı, dükkân henüz kapanmadı
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.InsufficientGold, _dukkan.LastRerollResult, "Kapalı cüzdan 0 bakiye okutur.");
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.InsufficientGold, _dukkan.LastResult);
            Assert.AreEqual(0, _rank.TotalTiers);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.PurchaseEntitlement);
            Assert.AreEqual(UpgradeEntitlement.Available, _dukkan.RerollEntitlement);
            Assert.AreEqual(1, _dukkan.Revision);
            CollectionAssert.AreEqual(kartlar, Dallar(_dukkan));
            Assert.AreEqual(0, _cuzdan.TotalSpent);
        }

        [Test]
        public void RerollPahali_KartUcuz_RerollKapali_SatinAlmaAcik()
        {
            _config.Economy.RerollCost = 100;
            Ac(gold: 0);
            int enUcuz = int.MaxValue;
            int indeks = -1;
            for (int i = 0; i < _dukkan.Cards.Count; i++) if (_dukkan.Cards[i].Cost < enUcuz) { enUcuz = _dukkan.Cards[i].Cost; indeks = i; }
            _cuzdan.Credit(enUcuz, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());   // kart tam alınır, reroll alınamaz

            Assert.AreEqual(RerollAvailability.TooExpensive, _dukkan.RerollAvailability);
            Assert.IsTrue(_dukkan.CanAfford(indeks), "Kart karşılanabilir.");
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.InsufficientGold, _dukkan.LastRerollResult, "Etiket ve ret nedeni aynı (TooExpensive ↔ InsufficientGold).");
            Assert.AreEqual(enUcuz, _cuzdan.Balance);
            Assert.IsTrue(_dukkan.TryPurchase(indeks, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(0, _cuzdan.Balance);
        }

        [Test]
        public void F1Overloadlari_KilitSonrasi_GuncelOturumuAlirVeReddedilir()
        {
            Ac();
            Assert.IsTrue(_dukkan.TryPurchase(0));   // F1 yolu: güncel oturum + revizyon
            int bakiye = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(1));
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.TryReroll());
            Assert.AreEqual(UpgradeRerollResult.OfferLocked, _dukkan.LastRerollResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance);
        }

        [Test]
        public void ConfigYok_KonfigurasyonHatasi_HarcamaYok()
        {
            _cuzdan.Credit(100, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            LogAssert.Expect(LogType.Error, new Regex("Upgrade offer configuration error: NoConfig"));
            _dukkan.Open(1, null, _cuzdan, _rank, 1);
            Assert.IsTrue(_dukkan.IsOpen);
            Assert.AreEqual(UpgradeOfferStatus.ConfigurationError, _dukkan.Status);
            Assert.AreEqual(UpgradeOfferFault.NoConfig, _dukkan.Fault);
            Assert.AreEqual(0, _dukkan.Cards.Count);
            Assert.AreEqual(0, _dukkan.RerollCost);
            Assert.AreEqual(RerollAvailability.Unavailable, _dukkan.RerollAvailability);
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.OfferUnavailable, _dukkan.LastResult);
            Assert.IsFalse(_dukkan.TryReroll(_dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradeRerollResult.OfferUnavailable, _dukkan.LastRerollResult);
            Assert.AreEqual(100, _cuzdan.Balance);
        }

        [Test]
        public void HavuzTukendi_MaxlanmisKalanDallar_GuvenliDurum_UyariSeviyesi()
        {
            // Sekiz dal tavanda, iki dal açık: tablo sağlam, koşu tabloyu aşmış → PoolExhausted (uyarı, hata değil).
            var dallar = (UpgradeBranch[])System.Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < dallar.Length; i++)
            {
                if (dallar[i] == UpgradeBranch.FireRate || dallar[i] == UpgradeBranch.ReloadSpeed) continue;
                _rank.SetTier(dallar[i], _config.Economy.Find(dallar[i]).MaxTier, _config);
            }

            LogAssert.Expect(LogType.Warning, new Regex("every remaining branch is at its ceiling"));
            Ac();
            Assert.AreEqual(UpgradeOfferStatus.ConfigurationError, _dukkan.Status, "Aynı güvenli durum (karar 17).");
            Assert.AreEqual(UpgradeOfferFault.PoolExhausted, _dukkan.Fault);
            Assert.AreEqual(0, _dukkan.Cards.Count);
            Assert.AreEqual(1000, _cuzdan.Balance);
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision));
            Assert.AreEqual(UpgradePurchaseResult.OfferUnavailable, _dukkan.LastResult);
        }

        [Test]
        public void BayatTeklif_DisRankDegisimi_RevizyonuYukseltir()
        {
            Ac();
            int rev = _dukkan.Revision;
            UpgradeCardOffer kart = _dukkan.Cards[0];
            _rank.SetTier(kart.Branch, 1, _config);   // F1: dal dışarıdan değişti, kart bayat
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, rev));
            Assert.AreEqual(UpgradePurchaseResult.StaleOffer, _dukkan.LastResult);
            Assert.AreEqual(rev + 1, _dukkan.Revision, "Kartlar yeniden fiyatlandı → revizyon arttı.");
            Assert.AreEqual(1, _dukkan.Cards[0].OwnedTier);
            int bakiye = _cuzdan.Balance;
            Assert.IsFalse(_dukkan.TryPurchase(0, _dukkan.SessionId, rev), "Eski revizyonu quote eden komut yeni fiyattan alamaz.");
            Assert.AreEqual(UpgradePurchaseResult.StaleRevision, _dukkan.LastResult);
            Assert.AreEqual(bakiye, _cuzdan.Balance);
            Assert.IsTrue(_dukkan.TryPurchase(0, _dukkan.SessionId, _dukkan.Revision), "Güncel revizyon satın alır.");
        }

        [Test]
        public void SatinAlmaSirasi_KontrollerHarcamadanOnce_YarimDurumYok()
        {
            // Silahsız (rank otoritesi yok): hiçbir kontrol para harcamaz.
            var shop = new RoundUpgradeShop { UnlockedPredicate = _ => true, Random = new SeededUpgradeOfferRandom(5) };
            shop.Open(1, _config, _cuzdan, null, 1);
            _cuzdan.Credit(1000, GoldSource.EnemyPickup, _cuzdan.NextTransactionId());
            Assert.IsFalse(shop.TryPurchase(0, shop.SessionId, shop.Revision));
            Assert.AreEqual(UpgradePurchaseResult.NoWeapon, shop.LastResult);
            Assert.AreEqual(1000, _cuzdan.Balance);
            Assert.AreEqual(UpgradeEntitlement.Available, shop.PurchaseEntitlement, "Başarısız adım hak tüketmez.");
            Assert.AreEqual(1, shop.Revision);
        }
    }
}
