using System.Collections.Generic;
using DD.Prototype.Economy;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-004 · round bitişi sırası — saf parçalarla modellenmiş zincir:
    /// son kill hak edişi → paket → (RoundCleared) → sweep → cüzdan → ekran snapshot'ı.
    /// Gerçek sahnede aynı zincir GoldService/RunDirector üzerinden PlayMode'da kanıtlanır
    /// (GoldUpgradeSliceTests); burada sıra ve idempotency, kare/sahne olmadan.
    /// </summary>
    [TestFixture]
    public sealed class RoundClearOrderingTests
    {
        private GoldWallet _cuzdan;
        private GoldRewardLedger _ledger;
        private List<GoldPacketLifecycle> _yerdekiler;
        private int _sonYasam;

        [SetUp]
        public void SetUp()
        {
            _cuzdan = new GoldWallet();
            _ledger = new GoldRewardLedger();
            _yerdekiler = new List<GoldPacketLifecycle>();
            _sonYasam = 0;

            _cuzdan.InitializeForRun(1);
            _ledger.BeginRun(1, 0);
        }

        /// <summary>Bir düşman ölür: hak ediş + paket (henüz hop'ta = Spawned).</summary>
        private GoldPacketLifecycle Oldur(int deger = 2)
        {
            int yasam = ++_sonYasam;
            if (_ledger.TryClaim(yasam, deger) != GoldRewardVerdict.Granted)
            {
                return null;
            }

            var paket = new GoldPacketLifecycle();
            Assert.IsTrue(paket.Begin(_cuzdan.NextTransactionId(), deger, 1, 1));
            _yerdekiler.Add(paket);
            return paket;
        }

        /// <summary>Round-clear sweep: bekleyen her paket kendi işlem kimliğiyle kredilenir.</summary>
        private (int paket, int miktar) Sweep()
        {
            int n = 0, toplam = 0;
            for (int i = 0; i < _yerdekiler.Count; i++)
            {
                GoldPacketLifecycle p = _yerdekiler[i];
                if (!p.IsOutstanding) continue;
                int tx = p.TransactionId;
                if (!p.TryCollect(GoldCollectKind.RoundClearAuto, out int v)) continue;
                if (_cuzdan.Credit(v, GoldSource.RoundClearAutoCollect, tx) == GoldTransactionResult.Applied)
                {
                    n++;
                    toplam += v;
                }
            }

            return (n, toplam);
        }

        [Test]
        public void SonKillRewardiOlusmadan_AutoCollectBaslamaz_SonPaketDahil()
        {
            for (int i = 0; i < 5; i++) Oldur();
            // Oyuncu ikisini elle aldı.
            _yerdekiler[0].Land(); _yerdekiler[0].TryCollect(GoldCollectKind.Manual, out int a);
            _cuzdan.Credit(a, GoldSource.EnemyPickup, _yerdekiler[0].TransactionId);
            _yerdekiler[1].Land(); _yerdekiler[1].TryCollect(GoldCollectKind.Manual, out int b);
            _cuzdan.Credit(b, GoldSource.EnemyPickup, _yerdekiler[1].TransactionId);

            // Son düşman ölür: hak ediş ve paket, RoundCleared'dan ÖNCE (aynı senkron ölüm yolu).
            GoldPacketLifecycle son = Oldur();
            Assert.IsNotNull(son);
            Assert.AreEqual(GoldPacketState.Spawned, son.State, "Son paket sweep anında hâlâ hop'ta.");
            Assert.AreEqual(6, _ledger.Granted, "Sweep'ten önce bütün hak edişler oluşmuş.");

            (int paket, int miktar) = Sweep();
            Assert.AreEqual(4, paket, "Elle alınmayan 3 + son paket = 4 sweep.");
            Assert.AreEqual(8, miktar);
            Assert.AreEqual(GoldPacketState.AutoCollected, son.State, "Son kill'in paketi kaybolmadı; sweep aldı.");
            Assert.AreEqual(12, _cuzdan.Balance, "6 düşman × 2 = 12: ekran açılmadan tamamı bakiyede.");
            Assert.AreEqual(4, _cuzdan.EarnedByPickup);
            Assert.AreEqual(8, _cuzdan.EarnedByAutoCollect);
            Assert.AreEqual(6 * 2, _cuzdan.TotalEarned, "Round tamamlanması ayrıca gold üretmedi: kazanç = ölüm değerlerinin toplamı.");
        }

        [Test]
        public void RoundCleared_BirKezIslenince_OutstandingReconcile_IkinciSweepSifir()
        {
            for (int i = 0; i < 7; i++) Oldur(3);
            (int p1, int m1) = Sweep();
            Assert.AreEqual(7, p1);
            Assert.AreEqual(21, m1);

            int outstanding = 0;
            for (int i = 0; i < _yerdekiler.Count; i++) if (_yerdekiler[i].IsOutstanding) outstanding++;
            Assert.AreEqual(0, outstanding, "Sweep sonrası bekleyen paket yok.");

            (int p2, int m2) = Sweep();
            Assert.AreEqual(0, p2, "Aynı RoundCleared ikinci kez toplamaz.");
            Assert.AreEqual(0, m2);
            Assert.AreEqual(21, _cuzdan.Balance);
            Assert.AreEqual(7, _cuzdan.AppliedTransactions, "İşlem sayısı paket sayısına eşit; ikinci sweep işlem üretmedi.");
        }

        [Test]
        public void UpgradeSnapshot_AutoCollectSonrasiBakiyeyiKullanir()
        {
            for (int i = 0; i < 3; i++) Oldur(2);
            int sweepOncesi = _cuzdan.Balance;
            (int p, int m) = Sweep();

            // Ekran snapshot'ı sweep'ten SONRA alınır: bakiye = önce + sweep miktarı.
            GoldWalletSnapshot ekran = _cuzdan.Snapshot;
            Assert.AreEqual(sweepOncesi + m, ekran.Balance);
            Assert.AreEqual(6, ekran.Balance);
            Assert.AreEqual(3, p);
        }

        [Test]
        public void AbortedRun_UpgradeAcilmaz_HakEdisVeKrediReddedilir()
        {
            Oldur();
            // Ölüm/abort: ledger kapanır, cüzdan sıfırlanır (director sırası).
            _ledger.EndRun();
            for (int i = 0; i < _yerdekiler.Count; i++) _yerdekiler[i].Recycle();
            _cuzdan.ResetForRunEnd();

            // Gecikmiş son kill: paket üretmez. Gecikmiş sweep: cüzdan reddeder.
            Assert.AreEqual(GoldRewardVerdict.RunNotLive, _ledger.TryClaim(++_sonYasam, 2));
            Assert.AreEqual(GoldTransactionResult.NoRun, _cuzdan.Credit(2, GoldSource.RoundClearAutoCollect, 99));
            Assert.AreEqual(0, _cuzdan.Balance);
            Assert.IsFalse(_cuzdan.IsOpen, "Kapalı cüzdan = açılacak ekran için bakiye yok.");
        }

        [Test]
        public void RunEndSonrasiGecikmisEvent_YeniRunStateiniDegistirmez()
        {
            // Koşu 1: 4 yaşam doğdu, 2'si öldü (paketleri yerde).
            Oldur(); Oldur();
            _sonYasam = 4;

            // Koşu sonu (director sırası): ledger kapanır, paketler kredisiz geri döner, cüzdan sıfır.
            _ledger.EndRun();
            for (int i = 0; i < _yerdekiler.Count; i++)
            {
                Assert.IsTrue(_yerdekiler[i].Recycle(), "Yerdeki paket kredisiz geri döndü (gold koşuyla yok oldu).");
            }
            _cuzdan.ResetForRunEnd();

            // Koşu 2 açılır (watermark = 4).
            _cuzdan.InitializeForRun(2);
            _ledger.BeginRun(2, 4);

            // Koşu 1'in gecikmiş ölümü (yaşam 3) yeni koşuda ödemez; eski paketler gold taşımaz.
            Assert.AreEqual(GoldRewardVerdict.StaleLife, _ledger.TryClaim(3, 2));
            for (int i = 0; i < _yerdekiler.Count; i++)
            {
                Assert.AreEqual(0, _yerdekiler[i].Amount);
                Assert.IsFalse(_yerdekiler[i].IsOutstanding);
            }

            Assert.AreEqual(0, _cuzdan.Balance, "Yeni koşu sıfırdan başlar.");
            Assert.AreEqual(0, _ledger.Granted, "Koşu 2'de henüz hak ediş yok.");
            Assert.AreEqual(GoldRewardVerdict.Granted, _ledger.TryClaim(5, 2), "Koşu 2'nin ilk yaşamı normal öder.");
        }
    }
}
