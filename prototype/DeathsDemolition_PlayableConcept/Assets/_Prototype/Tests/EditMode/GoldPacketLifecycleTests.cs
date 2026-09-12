using DD.Prototype.Economy;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-004 · gold paketi sahiplik/durum sözleşmesi — sahnesiz, saf.
    /// "Paket yalnız bir kez initialize edilir", "manuel/auto-collect tek kredi",
    /// "yarış duplicate üretmez", "collected tekrar toplanamaz", "pool reuse eski
    /// callback taşımaz (Generation)", "süreyle despawn yok (timer yok)", "pause'ta magnet
    /// saati ilerlemez", "run reset kalan paket kayıtlarını temizler".
    /// Cüzdanla birlikte: aynı paket iki ayrı yoldan gelse bile cüzdan tek kez öder.
    /// </summary>
    [TestFixture]
    public sealed class GoldPacketLifecycleTests
    {
        private GoldPacketLifecycle _paket;

        [SetUp]
        public void SetUp()
        {
            _paket = new GoldPacketLifecycle();
        }

        [Test]
        public void Begin_YalnizBirKez()
        {
            Assert.AreEqual(GoldPacketState.Recycled, _paket.State);
            Assert.IsTrue(_paket.Begin(transactionId: 5, amount: 2, runId: 1, round: 1));
            Assert.AreEqual(GoldPacketState.Spawned, _paket.State);
            Assert.AreEqual(2, _paket.Amount);
            Assert.AreEqual(5, _paket.TransactionId);
            Assert.AreEqual(1, _paket.Generation);

            Assert.IsFalse(_paket.Begin(6, 9, 1, 1), "Yaşam sürerken ikinci Begin reddedilir.");
            Assert.AreEqual(2, _paket.Amount, "Reddedilen Begin hiçbir alanı değiştirmez.");
            Assert.AreEqual(5, _paket.TransactionId);
            Assert.AreEqual(1, _paket.Generation);
        }

        [Test]
        public void Begin_GecersizSayilarReddedilir()
        {
            Assert.IsFalse(_paket.Begin(0, 2, 1, 1), "txId 0 reddedilir.");
            Assert.IsFalse(_paket.Begin(1, 0, 1, 1), "Miktar 0 reddedilir.");
            Assert.IsFalse(_paket.Begin(1, -2, 1, 1), "Negatif miktar reddedilir.");
            Assert.IsFalse(_paket.Begin(1, 2, 0, 1), "Koşusuz paket reddedilir.");
            Assert.AreEqual(GoldPacketState.Recycled, _paket.State);
        }

        [Test]
        public void ManuelCollect_YalnizBirKredi()
        {
            _paket.Begin(1, 2, 1, 1);
            _paket.Land();
            Assert.AreEqual(GoldPacketState.Waiting, _paket.State);

            Assert.IsTrue(_paket.TryCollect(GoldCollectKind.Manual, out int a));
            Assert.AreEqual(2, a);
            Assert.AreEqual(GoldPacketState.Collected, _paket.State);

            Assert.IsFalse(_paket.TryCollect(GoldCollectKind.Manual, out int b), "Collected paket tekrar toplanamaz.");
            Assert.AreEqual(0, b);
        }

        [Test]
        public void AutoCollect_YalnizBirKredi_HopSirasindaDaAlinir()
        {
            _paket.Begin(1, 2, 1, 1);
            // Son kill'in paketi RoundCleared anında hâlâ hop'ta (Spawned): yine toplanır.
            Assert.IsTrue(_paket.TryCollect(GoldCollectKind.RoundClearAuto, out int a));
            Assert.AreEqual(2, a);
            Assert.AreEqual(GoldPacketState.AutoCollected, _paket.State);
            Assert.IsFalse(_paket.TryCollect(GoldCollectKind.RoundClearAuto, out _), "İkinci sweep aynı paketi almaz.");
        }

        [Test]
        public void ManuelVeAutoCollectYarisi_DuplicateKrediUretmez()
        {
            var cuzdan = new GoldWallet();
            cuzdan.InitializeForRun(1);

            _paket.Begin(cuzdan.NextTransactionId(), 2, 1, 1);
            _paket.Land();
            _paket.Attract();
            Assert.AreEqual(GoldPacketState.Magnetizing, _paket.State);

            // Manuel yol kazanır…
            Assert.IsTrue(_paket.TryCollect(GoldCollectKind.Manual, out int m));
            Assert.AreEqual(GoldTransactionResult.Applied, cuzdan.Credit(m, GoldSource.EnemyPickup, _paket.TransactionId));

            // …aynı karede sweep gelir: durum reddeder; koruma atlansa bile cüzdan aynı id'yi reddeder.
            Assert.IsFalse(_paket.TryCollect(GoldCollectKind.RoundClearAuto, out _));
            Assert.AreEqual(GoldTransactionResult.Duplicate,
                cuzdan.Credit(2, GoldSource.RoundClearAutoCollect, _paket.TransactionId));
            Assert.AreEqual(2, cuzdan.Balance, "Aynı paket iki kez ödenmedi.");
        }

        [Test]
        public void PoolReuse_EskiCallbackYeniPaketiEtkilemez()
        {
            _paket.Begin(1, 2, 1, 1);
            int eskiNesil = _paket.Generation;
            int eskiTx = _paket.TransactionId;
            _paket.Recycle();

            Assert.IsTrue(_paket.Begin(2, 5, 1, 2));
            Assert.AreNotEqual(eskiNesil, _paket.Generation, "Yeni yaşam yeni nesil.");
            Assert.AreNotEqual(eskiTx, _paket.TransactionId, "Yeni yaşam yeni işlem kimliği.");

            // Eski yaşamın geç gelen 'toplandı' çağrısı nesil karşılaştırmasıyla bayat sayılır.
            bool bayat = eskiNesil != _paket.Generation;
            Assert.IsTrue(bayat);
            Assert.AreEqual(5, _paket.Amount, "Yeni paket eski değeri taşımaz.");
        }

        [Test]
        public void SureyleDespawn_Yoktur()
        {
            _paket.Begin(1, 2, 1, 1);
            _paket.Land();

            // Yaşam döngüsünde saat/ömür kavramı yok: ne kadar 'tick' edilirse edilsin bekler.
            for (int i = 0; i < 100000; i++)
            {
                _paket.TickMagnet(0.016f);
            }

            Assert.AreEqual(GoldPacketState.Waiting, _paket.State, "Yerdeki paket süreyle kaybolmaz.");
            Assert.IsTrue(_paket.IsOutstanding);
            Assert.AreEqual(2, _paket.Amount);
        }

        [Test]
        public void Pause_MagnetSaatiIlerlemez()
        {
            _paket.Begin(1, 2, 1, 1);
            _paket.Land();
            _paket.Attract();

            _paket.TickMagnet(0.05f);
            float once = _paket.MagnetClock;
            Assert.Greater(once, 0f);

            // Pause: scaled delta 0 → saat sabit.
            for (int i = 0; i < 60; i++)
            {
                _paket.TickMagnet(0f);
            }

            Assert.AreEqual(once, _paket.MagnetClock, 1e-6f, "Pause'ta magnet saati ilerlemedi.");

            _paket.TickMagnet(0.05f);
            Assert.Greater(_paket.MagnetClock, once, "Pause sonrası güvenli devam.");
        }

        [Test]
        public void Attract_YalnizWaitingdenGecerlidir()
        {
            _paket.Begin(1, 2, 1, 1);
            Assert.IsFalse(_paket.Attract(), "Hop sırasında magnet başlamaz.");
            _paket.Land();
            Assert.IsTrue(_paket.Attract());
            Assert.IsFalse(_paket.Attract(), "Zaten çekiliyor.");
        }

        [Test]
        public void RunReset_KalanPaketKayitlariniTemizler_GoldKaybiRaporlanir()
        {
            _paket.Begin(1, 2, 1, 1);
            _paket.Land();

            bool kayip = _paket.Recycle();
            Assert.IsTrue(kayip, "Bekleyen paket kredisiz geri döndü → gold kaybı işaretlenir (koşu sonu).");
            Assert.AreEqual(GoldPacketState.Recycled, _paket.State);
            Assert.AreEqual(0, _paket.Amount);
            Assert.AreEqual(0, _paket.TransactionId);
            Assert.AreEqual(0, _paket.RunId);
            Assert.IsFalse(_paket.IsOutstanding);

            // Toplanmış paketin geri dönüşü kayıp değildir.
            _paket.Begin(2, 2, 1, 1);
            _paket.TryCollect(GoldCollectKind.Manual, out _);
            Assert.IsFalse(_paket.Recycle());
        }
    }
}
