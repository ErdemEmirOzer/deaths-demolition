using System.Collections.Generic;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-004 · koşu cüzdanı sözleşmesi — sahnesiz, saf. REQ-018 (gold yalnız koşu içinde),
    /// "gold ↔ XP asla" (bu sınıfın XP ile teması yok — derleme düzeyinde), "harcanmamış
    /// gold roundlar arası taşınır" (round geçişi cüzdana dokunmaz).
    ///
    /// ÇÜRÜTME KOŞULU: her assert testin kendi saydığı olayla karşılaştırılır; cüzdanın
    /// kendi sayaçları yalnız "beyan" olarak okunur ve olay sayısıyla çapraz kontrol edilir.
    /// </summary>
    [TestFixture]
    public sealed class GoldWalletContractTests
    {
        private GoldWallet _cuzdan;
        private List<int> _degisimler;
        private List<GoldWalletSnapshot> _anlik;
        private int _kredilenen;

        [SetUp]
        public void SetUp()
        {
            _cuzdan = new GoldWallet();
            _degisimler = new List<int>();
            _anlik = new List<GoldWalletSnapshot>();
            _kredilenen = 0;

            _cuzdan.Changed += b => _degisimler.Add(b);
            _cuzdan.SnapshotChanged += s => _anlik.Add(s);
            _cuzdan.Credited += (a, b, p) => _kredilenen += a;
        }

        [Test]
        public void AcilmamisCuzdan_IslemKabulEtmez()
        {
            Assert.IsFalse(_cuzdan.IsOpen);
            Assert.AreEqual(GoldTransactionResult.NoRun, _cuzdan.Credit(5, GoldSource.EnemyPickup, 1));
            Assert.AreEqual(GoldTransactionResult.NoRun, _cuzdan.TrySpend(1, GoldSource.UpgradePurchase, 2));
            Assert.IsFalse(_cuzdan.Credit(5, Vector3.zero), "Etiketsiz kredi de koşu dışında reddedilmeli.");
            Assert.AreEqual(0, _cuzdan.Balance);
            Assert.AreEqual(0, _degisimler.Count, "Reddedilen işlem olay yükseltmemeli.");
        }

        [Test]
        public void InitializeForRun_VarsayilanKosuBakiyesiSifir()
        {
            _cuzdan.InitializeForRun(7);
            Assert.IsTrue(_cuzdan.IsOpen);
            Assert.AreEqual(7, _cuzdan.RunId);
            Assert.AreEqual(0, _cuzdan.Balance, "Yeni koşu sıfır bakiyeyle başlar; profil seviyesi başlangıç bonusu vermez.");
            Assert.AreEqual(0, _cuzdan.TotalEarned);
            Assert.AreEqual(1, _anlik.Count, "Açılış bir snapshot yayınlar.");
            Assert.AreEqual(7, _anlik[0].RunId);
        }

        [Test]
        public void Credit_BakiyeyiDogruArtirir_KaynakToplamlariAyrilir()
        {
            _cuzdan.InitializeForRun(1);

            Assert.AreEqual(GoldTransactionResult.Applied, _cuzdan.Credit(2, GoldSource.EnemyPickup, 10));
            Assert.AreEqual(GoldTransactionResult.Applied, _cuzdan.Credit(3, GoldSource.RoundClearAutoCollect, 11));
            Assert.AreEqual(GoldTransactionResult.Applied, _cuzdan.Credit(4, GoldSource.EnemyPickup, 12));

            Assert.AreEqual(9, _cuzdan.Balance);
            Assert.AreEqual(9, _cuzdan.TotalEarned);
            Assert.AreEqual(6, _cuzdan.EarnedByPickup);
            Assert.AreEqual(3, _cuzdan.EarnedByAutoCollect);
            Assert.AreEqual(9, _kredilenen, "Credited olayı toplamı bakiyeyle eşleşmeli.");
            Assert.AreEqual(3, _cuzdan.AppliedTransactions);
            Assert.AreEqual(new[] { 0, 2, 5, 9 }, _degisimler.ToArray(), "Changed: açılış + üç kredi.");
        }

        [Test]
        public void NegatifVeSifirKredi_Reddedilir_StateDegismez()
        {
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(5, GoldSource.EnemyPickup, 1);
            int olay = _degisimler.Count;

            Assert.AreEqual(GoldTransactionResult.InvalidAmount, _cuzdan.Credit(-3, GoldSource.EnemyPickup, 2));
            Assert.AreEqual(GoldTransactionResult.InvalidAmount, _cuzdan.Credit(0, GoldSource.EnemyPickup, 3));
            Assert.AreEqual(5, _cuzdan.Balance);
            Assert.AreEqual(1, _cuzdan.AppliedTransactions, "Reddedilen id tüketilmemeli.");
            Assert.AreEqual(olay, _degisimler.Count);
        }

        [Test]
        public void AyniTransactionId_IkinciKezUygulanmaz()
        {
            _cuzdan.InitializeForRun(1);

            Assert.AreEqual(GoldTransactionResult.Applied, _cuzdan.Credit(2, GoldSource.EnemyPickup, 42));
            Assert.AreEqual(GoldTransactionResult.Duplicate, _cuzdan.Credit(2, GoldSource.EnemyPickup, 42));
            Assert.AreEqual(GoldTransactionResult.Duplicate, _cuzdan.Credit(99, GoldSource.RoundClearAutoCollect, 42),
                "Aynı id farklı miktar/kaynakla da reddedilmeli.");
            Assert.AreEqual(2, _cuzdan.Balance);
            Assert.AreEqual(1, _cuzdan.AppliedTransactions);

            // Harcama da aynı id uzayını paylaşır: kredi id'siyle harcama reddedilir.
            Assert.AreEqual(GoldTransactionResult.Duplicate, _cuzdan.TrySpend(1, GoldSource.UpgradePurchase, 42));
            Assert.AreEqual(2, _cuzdan.Balance);
        }

        [Test]
        public void NextTransactionId_KosuIcindeTekrarEtmez_YeniKosudaSifirlanir()
        {
            _cuzdan.InitializeForRun(1);
            var gorulen = new HashSet<int>();
            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(gorulen.Add(_cuzdan.NextTransactionId()), "Koşu içinde id tekrar etti.");
            }

            _cuzdan.ResetForRunEnd();
            _cuzdan.InitializeForRun(2);
            Assert.AreEqual(1, _cuzdan.NextTransactionId(), "Yeni koşu id serisi baştan başlar (tekrar kümesi de temiz).");
        }

        [Test]
        public void TrySpend_YeterliBakiyede_MaliyetiBirKezDuser()
        {
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(50, GoldSource.EnemyPickup, 1);

            Assert.AreEqual(GoldTransactionResult.Applied, _cuzdan.TrySpend(30, GoldSource.UpgradePurchase, 2));
            Assert.AreEqual(20, _cuzdan.Balance);
            Assert.AreEqual(30, _cuzdan.TotalSpent);
            Assert.AreEqual(50, _cuzdan.TotalEarned, "Harcama kazancı değiştirmez.");

            Assert.AreEqual(GoldTransactionResult.Duplicate, _cuzdan.TrySpend(30, GoldSource.UpgradePurchase, 2),
                "Aynı satın alma işlemi ikinci kez düşmez.");
            Assert.AreEqual(20, _cuzdan.Balance);
        }

        [Test]
        public void TrySpend_YetersizBakiyede_HicbirSeyDegismez()
        {
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(10, GoldSource.EnemyPickup, 1);
            int olay = _degisimler.Count;

            Assert.AreEqual(GoldTransactionResult.InsufficientBalance, _cuzdan.TrySpend(11, GoldSource.UpgradePurchase, 2));
            Assert.AreEqual(10, _cuzdan.Balance);
            Assert.AreEqual(0, _cuzdan.TotalSpent);
            Assert.AreEqual(1, _cuzdan.AppliedTransactions, "Başarısız harcama id tüketmez.");
            Assert.AreEqual(olay, _degisimler.Count, "Başarısız harcama olay yükseltmez.");
            Assert.IsFalse(_cuzdan.TrySpend(11), "Eski bool imza da reddetmeli.");
            Assert.IsTrue(_cuzdan.TrySpend(10), "Tam bakiye harcanabilir.");
            Assert.AreEqual(0, _cuzdan.Balance, "Negatif bakiye asla oluşmaz.");
            Assert.AreEqual(GoldTransactionResult.InvalidAmount, _cuzdan.TrySpend(0, GoldSource.UpgradePurchase, 9));
            Assert.AreEqual(GoldTransactionResult.InvalidAmount, _cuzdan.TrySpend(-5, GoldSource.UpgradePurchase, 10));
        }

        [Test]
        public void RoundDegisimi_CuzdaniKorur_OlumSifirlar()
        {
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(37, GoldSource.EnemyPickup, 1);

            // Round geçişinde cüzdana dokunan hiçbir API yoktur: "round değişimi" cüzdan
            // açısından hiçbir çağrı yapılmamasıdır. Bakiye 37 kalır.
            Assert.AreEqual(37, _cuzdan.Balance);
            Assert.AreEqual(1, _cuzdan.RunId);

            _cuzdan.ResetForRunEnd();
            Assert.AreEqual(0, _cuzdan.Balance, "Ölüm cüzdanı sıfırlar.");
            Assert.AreEqual(0, _cuzdan.TotalEarned);
            Assert.AreEqual(0, _cuzdan.TotalSpent);
            Assert.AreEqual(0, _cuzdan.EarnedByPickup);
            Assert.IsFalse(_cuzdan.IsOpen);
            Assert.AreEqual(GoldTransactionResult.NoRun, _cuzdan.Credit(5, GoldSource.EnemyPickup, 3),
                "Koşu bittikten sonra gecikmiş kredi kabul edilmez.");
        }

        [Test]
        public void YeniKosu_OncekiBakiyeyiDevralmaz()
        {
            _cuzdan.InitializeForRun(1);
            _cuzdan.Credit(120, GoldSource.EnemyPickup, 1);

            // Koşu sonu reset'i atlanmış olsa bile yeni koşu açılışı her şeyi siler.
            _cuzdan.InitializeForRun(2);
            Assert.AreEqual(0, _cuzdan.Balance);
            Assert.AreEqual(0, _cuzdan.TotalEarned);
            Assert.AreEqual(2, _cuzdan.RunId);
            Assert.AreEqual(0, _cuzdan.AppliedTransactions);
        }

        [Test]
        public void Snapshot_AuthoritativeStateIleEslesir()
        {
            _cuzdan.InitializeForRun(3);
            _cuzdan.Credit(8, GoldSource.EnemyPickup, 1);
            _cuzdan.Credit(4, GoldSource.RoundClearAutoCollect, 2);
            _cuzdan.TrySpend(5, GoldSource.UpgradePurchase, 3);

            GoldWalletSnapshot son = _anlik[_anlik.Count - 1];
            Assert.AreEqual(_cuzdan.Snapshot, son, "Son yayınlanan snapshot canlı snapshot ile birebir aynı olmalı.");
            Assert.AreEqual(7, son.Balance);
            Assert.AreEqual(12, son.TotalEarned);
            Assert.AreEqual(5, son.TotalSpent);
            Assert.AreEqual(8, son.EarnedByPickup);
            Assert.AreEqual(4, son.EarnedByAutoCollect);
            Assert.AreEqual(3, son.RunId);
            Assert.AreEqual(3, son.Version, "Üç kabul edilen işlem → sürüm 3.");
            Assert.AreEqual(_cuzdan.Balance, RunEvents.Current.Gold, "HUD anlık görüntüsü cüzdanla aynı karede güncellenir.");
        }

        [Test]
        public void DebugSet_KosuDisindaEtkisiz()
        {
            _cuzdan.DebugSet(500);
            Assert.AreEqual(0, _cuzdan.Balance);
            _cuzdan.InitializeForRun(1);
            _cuzdan.DebugSet(500);
            Assert.AreEqual(500, _cuzdan.Balance);
            _cuzdan.DebugSet(-1);
            Assert.AreEqual(0, _cuzdan.Balance, "DebugSet negatif bakiye üretemez.");
        }
    }
}
