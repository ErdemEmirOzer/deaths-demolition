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

namespace DD.Prototype.Tests.EditMode
{
    using System.Collections.Generic;
    using DD.Prototype.Economy;
    using DD.Prototype.Feedback;
    using DD.Prototype.UI;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// WP-098B · altın toplama hissi — gerçek GoldService + GoldOrb gövdesi, sahnesiz, saat testten
    /// (GoldOrb.Tick). Olay sırası Landed → MagnetStart → FeelEvents.GoldCollected (S-02, cüzdan kredisinden)
    /// → FlightArrived; toplama anı S-02'nin tek olayı, GoldFeelEvents'te ikinci bir Collected yok. HUD noktası yokken
    /// bugünkü davranış (yerinde flaş, Depart/Arrive yok); kredi toplama anında, uçuş krediye dokunmaz;
    /// azaltılmış harekette uçuş atlanır, Arrive hemen. HUD noktası EditMode'da test sondasıyla verilir
    /// (GoldOrb.HudProbeForTests); Depart/Arrive gerçek HudGoldTarget'a gider (S-09 sözleşmesi).
    /// </summary>
    [TestFixture]
    public sealed class GoldFeelEventsTests
    {
        private const int Miktar = 5;
        private static readonly Vector2 HudNoktasi = new Vector2(40f, 200f);

        private readonly List<string> _sira = new List<string>();
        private readonly List<GameObject> _ekPaketler = new List<GameObject>();
        private GoldArrivedFeel _sonVaris;
        private FeelGold _sonToplama;
        private int _toplamaSayisi;
        private int _toplamaAnindaBakiye = -1;
        private int _krediSayisi;
        private bool _kopruElle;

        private GameObject _servisGo, _paketGo, _oyuncuGo, _kameraGo;
        private GoldService _servis;
        private GoldOrb _paket;
        private GoldOrb _sonMiknatis;
        private Transform _oyuncu;
        private Camera _kamera;
        private RenderTexture _rt;
        private bool _rmOnce;

        [SetUp]
        public void SetUp()
        {
            _sira.Clear();
            _sonMiknatis = null;
            _toplamaAnindaBakiye = -1;
            _toplamaSayisi = 0;
            _krediSayisi = 0;
            _rmOnce = UiMotion.ReducedMotion;
            UiMotion.ReducedMotion = false;

            // S-02 köprüsü temiz (zincir penceresi, sayaçlar); 0,6 s pencere test saatiyle.
            FeelEvents.ResetForTests();
            FeelEvents.TestClock = 10f;

            GoldFeelEvents.Landed += OnLanded;
            GoldFeelEvents.MagnetStart += OnMagnet;
            FeelEvents.GoldCollected += OnCollected;
            GoldFeelEvents.FlightArrived += OnArrived;

            _servisGo = new GameObject("GoldFeelTest.Service");
            _servis = _servisGo.AddComponent<GoldService>();
            _servis.InitializeForRun(1);
            _servis.Wallet.Credited += OnCredited;

            // Oyunda köprü cüzdanı GoldService.Instance'tan bağlar (Awake/OnEnable); EditMode'da onlar koşmaz, Instance
            // bu servis değil → köprünün kendi işleyicisi (FeelEvents.OnCredited) cüzdana elle takılır. Gerçek bağ PM P3/P10'da.
            FeelEvents.Rearm();
            _kopruElle = !ReferenceEquals(GoldService.Instance, _servis);
            if (_kopruElle) _servis.Wallet.Credited += FeelEvents.OnCredited;

            _oyuncuGo = new GameObject("GoldFeelTest.Player");
            _oyuncu = _oyuncuGo.transform;
            _oyuncu.position = new Vector3(0f, 0f, 50f);   // uzak: magnet dışı
            _servis.SetPlayerForTests(_oyuncu);

            _kameraGo = new GameObject("GoldFeelTest.Camera");
            _kamera = _kameraGo.AddComponent<Camera>();
            _rt = new RenderTexture(256, 256, 16);
            _kamera.targetTexture = _rt;   // pixelRect 256×256: ekran noktası belirli
            _kameraGo.transform.position = new Vector3(0f, 14f, -9f);
            _kameraGo.transform.LookAt(Vector3.zero);
            FeedbackCameraRef.Set(_kamera);

            _paketGo = new GameObject("GoldFeelTest.Orb");
            _paket = _paketGo.AddComponent<GoldOrb>();
            int tx = _servis.Wallet.NextTransactionId();
            Assert.IsTrue(_paket.Begin(tx, Miktar, _servis.RunId, 1, Vector3.zero, _servis, new GoldOrbTuning(), false));
        }

        [TearDown]
        public void TearDown()
        {
            GoldFeelEvents.Landed -= OnLanded;
            GoldFeelEvents.MagnetStart -= OnMagnet;
            FeelEvents.GoldCollected -= OnCollected;
            GoldFeelEvents.FlightArrived -= OnArrived;
            if (_servis != null)
            {
                _servis.Wallet.Credited -= OnCredited;
                if (_kopruElle) _servis.Wallet.Credited -= FeelEvents.OnCredited;
            }

            FeelEvents.ResetForTests();
            FeelEvents.Disarm();
            for (int i = 0; i < _ekPaketler.Count; i++) Object.DestroyImmediate(_ekPaketler[i]);
            _ekPaketler.Clear();

            GoldOrb.HudProbeForTests = null;
            UiMotion.ReducedMotion = _rmOnce;
            FeedbackCameraRef.Set(null);

            if (_kamera != null) _kamera.targetTexture = null;
            if (_rt != null) Object.DestroyImmediate(_rt);
            Object.DestroyImmediate(_paketGo);
            Object.DestroyImmediate(_oyuncuGo);
            Object.DestroyImmediate(_kameraGo);
            Object.DestroyImmediate(_servisGo);
        }

        private void OnLanded(GoldLandedFeel e) { _sira.Add("Landed"); }
        private void OnMagnet(GoldMagnetFeel e) { _sira.Add("MagnetStart"); _sonMiknatis = e.Orb; }
        private void OnCollected(FeelGold e) { _sira.Add("GoldCollected"); _toplamaSayisi++; _sonToplama = e; _toplamaAnindaBakiye = _servis.Wallet.Balance; }
        private void OnArrived(GoldArrivedFeel e) { _sira.Add("FlightArrived"); _sonVaris = e; }
        private void OnCredited(int miktar, int bakiye, Vector3 konum) { _krediSayisi++; }

        private static bool HudYok(out Vector2 p) { p = default(Vector2); return false; }
        private static bool HudVar(out Vector2 p) { p = HudNoktasi; return true; }

        /// <summary>Hop biter (Landed), oyuncu magnet içine girer (MagnetStart), paket oyuncuya ulaşır (GoldCollected).</summary>
        private void IndirVeTopla()
        {
            IndirVeTopla(_paket);
        }

        private void IndirVeTopla(GoldOrb paket)
        {
            _oyuncu.position = new Vector3(0f, 0f, 50f);   // hop sürerken magnet dışı
            paket.Tick(0.5f, 0.5f);
            Assert.AreEqual(GoldPacketState.Waiting, paket.Packet.State, "Hop bitti, paket yerde.");

            // Oyuncu noktası = konum + 0,9 m yukarı; paketten 2,5 m (magnet 3,5 m, temas 0,9 m).
            _oyuncu.position = paket.transform.position + new Vector3(2.5f, -0.9f, 0f);
            for (int i = 0; i < 600 && paket.Packet.State != GoldPacketState.Collected; i++)
            {
                paket.Tick(1f / 60f, 1f / 60f);
            }

            Assert.AreEqual(GoldPacketState.Collected, paket.Packet.State, "Magnet paketi oyuncuya ulaştırmadı.");
        }

        private GoldOrb EkPaket()
        {
            var go = new GameObject("GoldFeelTest.Orb." + (_ekPaketler.Count + 2));
            _ekPaketler.Add(go);
            GoldOrb paket = go.AddComponent<GoldOrb>();
            int tx = _servis.Wallet.NextTransactionId();
            Assert.IsTrue(paket.Begin(tx, Miktar, _servis.RunId, 1, Vector3.zero, _servis, new GoldOrbTuning(), false));
            return paket;
        }

        [Test]
        public void HudYokken_BugunkuDavranis_YerindeFlas_DepartArriveYok()
        {
            GoldOrb.HudProbeForTests = HudYok;
            int yoldaOnce = HudGoldTarget.InFlight;

            IndirVeTopla();

            CollectionAssert.AreEqual(new[] { "Landed", "MagnetStart", "GoldCollected" }, _sira);
            Assert.AreEqual(Miktar, _toplamaAnindaBakiye, "Kredi toplama anında.");
            Assert.AreEqual(Miktar, _sonToplama.Amount);
            Assert.AreEqual(Miktar, _sonToplama.Balance, "FeelGold.Balance = kredi sonrası bakiye.");
            Assert.AreEqual(0, _sonToplama.ChainIndex, "İlk toplama: zincir 0.");
            Assert.AreEqual(1, _toplamaSayisi, "Paket başına tek GoldCollected.");
            Assert.IsFalse(_paket.IsFlyingToHud, "HUD yok: uçuş yok.");
            Assert.AreEqual(yoldaOnce, HudGoldTarget.InFlight, "HUD yok: Depart çağrılmaz.");

            // Flaş ölçekli saatte; bitince gövde havuza (havuzsuz gövde: pasif).
            _paket.Tick(0.3f, 0.3f);
            Assert.IsFalse(_paketGo.activeSelf, "Flaş bitti, gövde döndü.");
            CollectionAssert.DoesNotContain(_sira, "FlightArrived", "HUD yokken FlightArrived (Arrive) yok.");
            Assert.AreEqual(1, _krediSayisi);
            Assert.AreEqual(Miktar, _servis.Wallet.Balance);
        }

        [Test]
        public void HudVarken_Sira_Landed_MagnetStart_Collected_FlightArrived_KrediUcustanBagimsiz()
        {
            GoldOrb.HudProbeForTests = HudVar;
            int yoldaOnce = HudGoldTarget.InFlight;

            IndirVeTopla();

            CollectionAssert.AreEqual(new[] { "Landed", "MagnetStart", "GoldCollected" }, _sira, "Varış uçuş bitmeden gelmez.");
            Assert.AreSame(_paket, _sonMiknatis, "MagnetStart çekilen paketi taşır.");
            Assert.AreEqual(Miktar, _toplamaAnindaBakiye, "Kredi toplama anında, uçuştan önce.");
            Assert.IsTrue(_paket.IsFlyingToHud);
            Assert.AreEqual(yoldaOnce + Miktar, HudGoldTarget.InFlight, "Depart(miktar) uçuş başında.");

            // Ölçekli saat donuk (pause / raund sonu): uçuş yine ilerler (unscaled), kredi değişmez.
            _paket.Tick(0f, 0.1f);
            _paket.Tick(0f, 0.1f);
            Assert.IsTrue(_paket.IsFlyingToHud, "0,2 s: hâlâ yolda.");
            CollectionAssert.DoesNotContain(_sira, "FlightArrived");
            Assert.AreEqual(Miktar, _servis.Wallet.Balance);

            _paket.Tick(0f, 0.15f);
            CollectionAssert.AreEqual(new[] { "Landed", "MagnetStart", "GoldCollected", "FlightArrived" }, _sira);
            Assert.AreEqual(Miktar, _sonVaris.Amount);
            Assert.IsTrue(_sonVaris.Flew);
            Assert.AreEqual(GoldCollectKind.Manual, _sonVaris.Kind);
            Assert.AreEqual(yoldaOnce, HudGoldTarget.InFlight, "Arrive(miktar) yoldakini serbest bıraktı.");
            Assert.IsFalse(_paketGo.activeSelf, "Varışta gövde döndü.");

            // Uçuşun sonu tam olarak HUD sembolünün üstünde.
            Vector3 ekran = _kamera.WorldToScreenPoint(_paket.transform.position);
            Assert.AreEqual(HudNoktasi.x, ekran.x, 0.5f);
            Assert.AreEqual(HudNoktasi.y, ekran.y, 0.5f);
            Assert.Greater(ekran.z, 0f, "Uç nokta kameranın önünde.");

            Assert.AreEqual(1, _krediSayisi, "Uçuş ikinci kredi üretmez.");
            Assert.AreEqual(Miktar, _servis.Wallet.Balance);
            Assert.AreEqual(Miktar, _servis.Wallet.EarnedByPickup);
        }

        [Test]
        public void AzaltilmisHareket_UcusAtlanir_ArriveHemen_FlasYerinde()
        {
            GoldOrb.HudProbeForTests = HudVar;
            UiMotion.ReducedMotion = true;
            int yoldaOnce = HudGoldTarget.InFlight;

            IndirVeTopla();

            CollectionAssert.AreEqual(new[] { "Landed", "MagnetStart", "GoldCollected", "FlightArrived" }, _sira, "RM: Arrive toplama karesinde.");
            Assert.IsFalse(_sonVaris.Flew);
            Assert.IsFalse(_paket.IsFlyingToHud);
            Assert.AreEqual(yoldaOnce, HudGoldTarget.InFlight, "RM: Depart yok.");
            Assert.AreEqual(Miktar, _servis.Wallet.Balance);

            _paket.Tick(0.3f, 0.3f);
            Assert.IsFalse(_paketGo.activeSelf, "RM: bugünkü flaş, sonra havuz.");
            Assert.AreEqual(1, _krediSayisi);
        }

        [Test]
        public void UcusBitince_TekVaris_HerDepartTekArrive()
        {
            GoldOrb.HudProbeForTests = HudVar;
            int yoldaOnce = HudGoldTarget.InFlight;

            IndirVeTopla();
            _paket.Tick(0f, 0.1f);
            Assert.IsTrue(_paket.IsFlyingToHud);

            // Uçuş biter; aynı gövdeye bir kare daha: ikinci varış yok.
            _paket.Tick(0f, 0.5f);
            _paket.Tick(0f, 0.5f);
            int varis = 0;
            for (int i = 0; i < _sira.Count; i++) if (_sira[i] == "FlightArrived") varis++;
            Assert.AreEqual(1, varis, "Her Depart tam bir Arrive.");
            Assert.AreEqual(yoldaOnce, HudGoldTarget.InFlight);
        }

        /// <summary>
        /// Perde merdiveni: ChainIndex S-02'nin 0,6 s penceresinden. Pencere içindeki ikinci toplama 1, pencere dışındaki
        /// üçüncü yeniden 0. Her paket tek GoldCollected, her kredi tek olay (çift kaynak yok).
        /// </summary>
        [Test]
        public void ZincirIndeksi_06sPenceresi_MerdivenVeSifirlama()
        {
            GoldOrb.HudProbeForTests = HudYok;

            FeelEvents.TestClock = 10f;
            IndirVeTopla();
            Assert.AreEqual(0, _sonToplama.ChainIndex, "ilk toplama 0");

            FeelEvents.TestClock = 10.3f;
            IndirVeTopla(EkPaket());
            Assert.AreEqual(1, _sonToplama.ChainIndex, "0,3 s sonra: merdivende 1");
            Assert.AreEqual(2 * Miktar, _sonToplama.Balance);

            FeelEvents.TestClock = 11.5f;
            IndirVeTopla(EkPaket());
            Assert.AreEqual(0, _sonToplama.ChainIndex, "1,2 s sonra: pencere dışı, yeniden 0");

            Assert.AreEqual(3, _toplamaSayisi, "üç paket, üç GoldCollected");
            Assert.AreEqual(3, _krediSayisi, "her kredi tek olay");
            Assert.AreEqual(_krediSayisi, FeelEvents.Count(FeelEventKind.GoldCollected), "köprü sayacı krediyle eşit");
        }
    }
}
