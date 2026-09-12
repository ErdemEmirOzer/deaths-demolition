using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Spawn;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-003 · round yaşam döngüsü ve nüfus defteri sözleşmesi — sahnesiz, saf.
    /// REQ-012 (havuz boş VE yaşayan yok → tamamlanma), REQ-019 (kesintisiz havuz akışı,
    /// tükenince spawn durur), REQ-072 (hedef aktif ≠ teknik tavan), REQ-006 (pause'ta saat
    /// ilerlemez).
    ///
    /// ÇÜRÜTME KOŞULU
    /// --------------
    /// Beklenen sayılar test sabitidir (havuz 12, hedef 9, tavan 40 = R1 formül değerleri
    /// olarak ELLE yazıldı; RoundFormulaTests aynı sayıları config'ten bağımsız kilitler).
    /// Defter kendi sayaçlarını okuyarak doğrulanmaz: her assert testin kendi saydığı
    /// olayla karşılaştırılır. Tamamlanma olayı bir sayaçla sayılır; ikinci yayın kırmızıdır.
    /// </summary>
    [TestFixture]
    public sealed class RoundLedgerContractTests
    {
        // R1 formül değerleri (12 + 6·0 + floor(2.5·0) = 12; min(7 + 2·1, 32) = 9; tavan 40).
        private const int R1Havuz = 12;
        private const int R1Hedef = 9;
        private const int Tavan = 40;

        private RoundLedger _defter;
        private List<RoundState> _durumlar;
        private int _tamamlanmaSayisi;
        private int _havuzTukendiSayisi;

        [SetUp]
        public void SetUp()
        {
            _defter = new RoundLedger();
            _durumlar = new List<RoundState>();
            _tamamlanmaSayisi = 0;
            _havuzTukendiSayisi = 0;

            _defter.StateChanged += s => _durumlar.Add(s);
            _defter.RoundCompleted += r => _tamamlanmaSayisi++;
            _defter.PoolDepleted += r => _havuzTukendiSayisi++;
        }

        /// <summary>Rezervasyon + aktivasyon onayı: director'ın normal spawn yolu (canlı düşman).</summary>
        private bool Dogur(out int bilet)
        {
            bool ok = _defter.TryReserveSpawn(out bilet);
            if (ok)
            {
                _defter.ConfirmActivation(bilet);
            }

            return ok;
        }

        /// <summary>Sabit tazelik nöbetçisi: R1 sabitleri sevkiyat formülünden saparsa türetmeyi yenile.</summary>
        [Test]
        public void SabitTazelik_R1DegerleriFormulleAyni()
        {
            var rounds = new RoundConfigSection();
            Assert.AreEqual(R1Havuz, rounds.TotalEnemyPool(1), "R1 havuz test sabiti formülden saptı; sabiti yenile.");
            Assert.AreEqual(R1Hedef, rounds.TargetActiveEnemies(1), "R1 hedef test sabiti formülden saptı; sabiti yenile.");
            Assert.AreEqual(Tavan, rounds.AbsoluteSafetyCeiling, "Tavan test sabiti formülden saptı; sabiti yenile.");
        }

        // ------------------------------------------------------------ state geçişleri

        [Test]
        public void StateGecisleri_NotStartedStartingActivePoolDepletedCompletingCompleted()
        {
            Assert.AreEqual(RoundState.NotStarted, _defter.State);

            _defter.BeginRound(1, 2, R1Hedef, Tavan);
            Assert.AreEqual(RoundState.Starting, _defter.State);

            Assert.IsTrue(_defter.Activate());
            Assert.AreEqual(RoundState.Active, _defter.State);
            Assert.IsFalse(_defter.Activate(), "Activate ikinci kez true dönmemeli.");

            Assert.IsTrue(Dogur(out int a));
            Assert.IsTrue(Dogur(out int b));
            // Evaluate aynı çağrıda Active → PoolDepleted geçişini yapar; dönüş değeri geçiş SONRASI durumdur.
            Assert.AreEqual(RoundState.PoolDepleted, _defter.Evaluate(), "Havuz sıfırken Evaluate PoolDepleted'a geçmeli.");
            Assert.AreEqual(RoundState.PoolDepleted, _defter.State);
            Assert.AreEqual(1, _havuzTukendiSayisi);

            _defter.RegisterDeath(a);
            Assert.AreEqual(RoundState.PoolDepleted, _defter.Evaluate(), "Bir düşman yaşarken Completing'e geçilmemeli.");

            _defter.RegisterDeath(b);
            Assert.AreEqual(RoundState.Completing, _defter.Evaluate());
            Assert.AreEqual(0, _tamamlanmaSayisi, "Evaluate tamamlanma YAYINLAMAZ; TryComplete yayınlar.");

            Assert.IsTrue(_defter.TryComplete());
            Assert.AreEqual(RoundState.Completed, _defter.State);
            Assert.AreEqual(1, _tamamlanmaSayisi);

            CollectionAssert.AreEqual(
                new[] { RoundState.Starting, RoundState.Active, RoundState.PoolDepleted, RoundState.Completing, RoundState.Completed },
                _durumlar, "State geçiş sırası beklenen zincirden farklı.");
        }

        [Test]
        public void StateGecisleri_ActiveOlmadanSpawnYok()
        {
            Assert.IsFalse(_defter.CanSpawn(out string neden), "NotStarted'da spawn olmamalı.");
            Assert.AreEqual("round not active", neden);

            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            Assert.IsFalse(_defter.CanSpawn(out _), "Starting'de spawn olmamalı.");
            Assert.IsFalse(_defter.TryReserveSpawn(out int bilet));
            Assert.AreEqual(0, bilet);

            _defter.Activate();
            Assert.IsTrue(_defter.CanSpawn(out _));
        }

        // ------------------------------------------------------------ tamamlanma tek sefer

        [Test]
        public void TamamlanmaOlayi_YalnizBirKezYayinlanir()
        {
            _defter.BeginRound(1, 1, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int bilet);
            _defter.RegisterDeath(bilet);

            for (int i = 0; i < 5; i++)
            {
                _defter.Evaluate();
                _defter.TryComplete();
            }

            Assert.AreEqual(1, _tamamlanmaSayisi, "RoundCompleted birden fazla yayınlandı.");
            Assert.AreEqual(1, _havuzTukendiSayisi, "PoolDepleted birden fazla yayınlandı.");
            Assert.IsFalse(_defter.TryComplete());

            // Tamamlanmış rounda geç gelen raporlar sayaçları oynatmaz.
            Assert.IsTrue(_defter.RegisterDespawn(bilet), "Ceset despawn raporu kabul edilmeli (occupied düşer).");
            Assert.IsFalse(_defter.RegisterDeath(bilet));
            Assert.AreEqual(1, _tamamlanmaSayisi);
            Assert.AreEqual(RoundState.Completed, _defter.State);
        }

        // ------------------------------------------------------------ havuz ve sayaçlar

        [Test]
        public void Havuz_DogruInitializeEdilir()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);

            RoundPopulationSnapshot p = _defter.Snapshot();
            Assert.AreEqual(1, p.Round);
            Assert.AreEqual(R1Havuz, p.PoolTotal);
            Assert.AreEqual(R1Havuz, p.Unspawned);
            Assert.AreEqual(0, p.SpawnedTotal);
            Assert.AreEqual(0, p.Alive);
            Assert.AreEqual(0, p.Occupied);
            Assert.AreEqual(0, p.Killed);
            Assert.AreEqual(R1Hedef, p.TargetActive);
            Assert.AreEqual(Tavan, p.Ceiling);
            Assert.AreEqual(0, p.ActivationFailures);
            Assert.AreEqual(R1Havuz, p.EnemiesRemaining);
        }

        [Test]
        public void Spawn_HavuzuBirerAzaltirVeSayaclariBirerArtirir()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();

            var biletler = new HashSet<int>();
            for (int i = 1; i <= 5; i++)
            {
                Assert.IsTrue(Dogur(out int bilet), "Spawn " + i + " reddedildi.");
                Assert.Greater(bilet, 0);
                Assert.IsTrue(biletler.Add(bilet), "Bilet tekrar etti: " + bilet);

                Assert.AreEqual(R1Havuz - i, _defter.Unspawned);
                Assert.AreEqual(i, _defter.SpawnedTotal);
                Assert.AreEqual(i, _defter.Alive);
                Assert.AreEqual(i, _defter.Occupied);
            }
        }

        [Test]
        public void AyniDusman_IkiKezYasayanSayilamaz()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int bilet);

            // "Aynı düşmanı iki kez ekleme" yolu yok: yaşayan sayısı yalnız rezervasyonla
            // artar ve her rezervasyon yeni bilet üretir. Aynı biletle ikinci bir yaşam
            // kaydı yapılamaz; ölüm sonrası "yeniden canlanma" API'de yoktur.
            Assert.AreEqual(1, _defter.Alive);
            Assert.IsTrue(_defter.IsAlive(bilet));
            Assert.IsTrue(_defter.RegisterDeath(bilet));
            Assert.IsFalse(_defter.IsAlive(bilet));
            Assert.AreEqual(0, _defter.Alive);
            Assert.IsTrue(_defter.IsTracked(bilet), "Ceset hâlâ izlenmeli (occupied).");
        }

        [Test]
        public void AyniOlum_IkiKezKillVeyaAliveAzalmasiUretmez()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);
            Dogur(out int b);

            Assert.IsTrue(_defter.RegisterDeath(a));
            Assert.IsFalse(_defter.RegisterDeath(a), "İkinci ölüm raporu kabul edilmemeli.");
            Assert.IsFalse(_defter.RegisterDeath(a));

            Assert.AreEqual(1, _defter.Killed, "Aynı ölüm iki kez sayıldı.");
            Assert.AreEqual(1, _defter.Alive, "Aynı ölüm yaşayan sayısını iki kez düşürdü.");
            Assert.AreEqual(2, _defter.Occupied, "Ceset despawn edilmeden occupied düşmemeli.");

            Assert.IsTrue(_defter.RegisterDespawn(a));
            Assert.IsFalse(_defter.RegisterDespawn(a), "İkinci despawn raporu kabul edilmemeli.");
            Assert.AreEqual(1, _defter.Occupied);
            Assert.AreEqual(1, _defter.Alive, "b hâlâ yaşıyor olmalı.");
            Assert.IsTrue(_defter.IsAlive(b));
        }

        [Test]
        public void BilinmeyenBilet_HicbirSayaciOynatmaz()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int bilet);

            Assert.IsFalse(_defter.RegisterDeath(bilet + 1000));
            Assert.IsFalse(_defter.RegisterDespawn(0));
            Assert.IsFalse(_defter.RegisterDeath(-5));
            Assert.IsFalse(_defter.Reconcile(bilet + 1));

            Assert.AreEqual(1, _defter.Alive);
            Assert.AreEqual(1, _defter.Occupied);
            Assert.AreEqual(0, _defter.Killed);
            Assert.AreEqual(0, _defter.Reconciled);
        }

        // ------------------------------------------------------------ tamamlanma kuralı

        [Test]
        public void HavuzSifirYasayanVar_RoundBitmez()
        {
            _defter.BeginRound(1, 3, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);
            Dogur(out int b);
            Dogur(out int c);
            Assert.AreEqual(0, _defter.Unspawned);

            _defter.RegisterDeath(a);
            _defter.RegisterDeath(b);

            for (int i = 0; i < 3; i++)
            {
                _defter.Evaluate();
                _defter.TryComplete();
            }

            Assert.AreEqual(RoundState.PoolDepleted, _defter.State);
            Assert.AreEqual(0, _tamamlanmaSayisi, "Havuz boş ama c yaşarken round tamamlandı.");
            Assert.AreEqual(1, _havuzTukendiSayisi);
            Assert.IsTrue(_defter.IsAlive(c));
        }

        [Test]
        public void YasayanSifirHavuzdaDusmanVar_RoundBitmez()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);
            _defter.RegisterDeath(a);
            _defter.RegisterDespawn(a);

            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(R1Havuz - 1, _defter.Unspawned);

            for (int i = 0; i < 3; i++)
            {
                _defter.Evaluate();
                _defter.TryComplete();
            }

            Assert.AreEqual(RoundState.Active, _defter.State, "Havuzda düşman varken geçici sıfır yaşayan roundu bitirmemeli.");
            Assert.AreEqual(0, _tamamlanmaSayisi);
            Assert.AreEqual(0, _havuzTukendiSayisi);
            Assert.IsTrue(_defter.CanSpawn(out _), "Havuz devam ederken yeniden spawn mümkün olmalı.");
        }

        [Test]
        public void HavuzVeYasayanSifir_RoundBiter()
        {
            _defter.BeginRound(1, 2, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);
            Dogur(out int b);
            _defter.RegisterDeath(a);
            _defter.RegisterDeath(b);

            // Cesetler henüz despawn edilmedi: occupied 2, alive 0. Tamamlanma cesetleri BEKLEMEZ.
            Assert.AreEqual(2, _defter.Occupied);
            Assert.AreEqual(RoundState.Completing, _defter.Evaluate());
            Assert.IsTrue(_defter.TryComplete());
            Assert.AreEqual(1, _tamamlanmaSayisi);
            Assert.IsTrue(_defter.Snapshot().IsCompleted);
        }

        [Test]
        public void TemizlikKaldirmasi_KillSaymazAmaYasayaniDusurur()
        {
            _defter.BeginRound(1, 2, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);

            // Remove() yolu: ölmeden despawn (round temizliği). Kill sayılmaz.
            Assert.IsTrue(_defter.RegisterDespawn(a));
            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(0, _defter.Occupied);
            Assert.AreEqual(0, _defter.Killed);
            Assert.AreEqual(1, _defter.SpawnedTotal);
        }

        // ------------------------------------------------------------ hedef ve tavan

        [Test]
        public void HedefAktif_TavaniAsamaz()
        {
            _defter.BeginRound(1, 100, Tavan + 25, Tavan);
            Assert.AreEqual(Tavan, _defter.TargetActive, "Hedef tavana kırpılmalı.");
            Assert.AreEqual(Tavan, _defter.Ceiling);

            _defter.Retarget(999, Tavan);
            Assert.AreEqual(Tavan, _defter.TargetActive);

            _defter.Retarget(5, 3);
            Assert.AreEqual(3, _defter.Ceiling);
            Assert.AreEqual(3, _defter.TargetActive, "Tavan düşünce hedef de kırpılmalı.");
        }

        [Test]
        public void Tavan_OccupiedIleTestEdilir_HedefAliveIle()
        {
            const int tavan = 4;
            const int hedef = 3;
            _defter.BeginRound(1, 100, hedef, tavan);
            _defter.Activate();

            var biletler = new List<int>();
            for (int i = 0; i < hedef; i++)
            {
                Assert.IsTrue(Dogur(out int b));
                biletler.Add(b);
            }

            // Hedef yaşayan sayısıyla ölçülür: 3 yaşayan == hedef → spawn yok, tavan (4) dolu değil.
            Assert.IsFalse(_defter.CanSpawn(out string neden));
            Assert.AreEqual("target reached", neden);

            // Üçü de ölür, cesetler yerde: alive 0 < hedef → bir spawn daha açılır (occupied 3 < 4).
            for (int i = 0; i < biletler.Count; i++)
            {
                _defter.RegisterDeath(biletler[i]);
            }

            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(hedef, _defter.Occupied);
            Assert.IsTrue(_defter.CanSpawn(out neden), neden);
            Assert.IsTrue(Dogur(out int dorduncu));
            Assert.AreEqual(tavan, _defter.Occupied);

            // O da ölür: alive 0 < hedef ama gövdeler 4 == tavan → spawn YOK. Tavan gövdeyle ölçülür.
            _defter.RegisterDeath(dorduncu);
            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(tavan, _defter.Occupied);
            Assert.IsFalse(_defter.CanSpawn(out neden), "Cesetler tavanı doldururken spawn kabul edildi.");
            Assert.AreEqual("ceiling", neden);

            // Bir ceset despawn olunca tavan açılır.
            _defter.RegisterDespawn(biletler[0]);
            Assert.IsTrue(_defter.CanSpawn(out neden), neden);
            Assert.IsTrue(Dogur(out _));
            Assert.AreEqual(tavan, _defter.Occupied);
            Assert.LessOrEqual(_defter.Occupied, _defter.Ceiling);
        }

        [Test]
        public void HedefeUlasinca_SpawnDurur_OlumleYenidenAcilir()
        {
            _defter.BeginRound(1, R1Havuz, 3, Tavan);
            _defter.Activate();
            Dogur(out int a);
            Dogur(out _);
            Dogur(out _);

            Assert.IsFalse(_defter.CanSpawn(out string neden));
            Assert.AreEqual("target reached", neden);

            _defter.RegisterDeath(a);
            Assert.IsTrue(_defter.CanSpawn(out _), "Ölümle yaşayan hedefin altına düşünce yeni spawn açılmalı (REQ-019).");
        }

        // ------------------------------------------------------------ geçersiz configuration

        [Test]
        public void GecersizConfiguration_GuvenliNormalizeEdilir()
        {
            _defter.BeginRound(-3, -10, -4, 0);

            Assert.AreEqual(1, _defter.Round, "Round 1'e kırpılmalı.");
            Assert.AreEqual(0, _defter.PoolTotal, "Negatif havuz 0 olmalı.");
            Assert.AreEqual(1, _defter.Ceiling, "Tavan en az 1 olmalı.");
            Assert.AreEqual(0, _defter.TargetActive, "Negatif hedef 0 olmalı.");

            _defter.Activate();
            Assert.IsFalse(_defter.CanSpawn(out _), "Boş havuzla spawn olmamalı.");

            // Havuzu 0 olan round derhal (yaşayan yok) tamamlanır — sonsuz bekleme yok.
            _defter.Evaluate();
            Assert.IsTrue(_defter.TryComplete());
            Assert.AreEqual(1, _tamamlanmaSayisi);
        }

        [Test]
        public void RoundPlan_ConfigsizGuvenli_ConfigliFormul()
        {
            RoundPlan bos = RoundPlan.Resolve(null, 1);
            Assert.AreEqual(0, bos.PoolTotal);
            Assert.AreEqual(0, bos.TargetActive);
            Assert.AreEqual(1, bos.Ceiling);
            Assert.IsTrue(bos.IsValid);

            var config = UnityEngine.ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                RoundPlan r1 = RoundPlan.Resolve(config, 1);
                Assert.AreEqual(1, r1.Round);
                Assert.AreEqual(R1Havuz, r1.PoolTotal);
                Assert.AreEqual(R1Hedef, r1.TargetActive);
                Assert.AreEqual(Tavan, r1.Ceiling);
                Assert.IsTrue(r1.IsValid);

                for (int r = 1; r <= 60; r++)
                {
                    RoundPlan p = RoundPlan.Resolve(config, r);
                    Assert.LessOrEqual(p.TargetActive, p.Ceiling, "R" + r + " hedef tavanı aştı.");
                    Assert.AreEqual(config.TotalEnemyPool(r), p.PoolTotal, "R" + r + " havuz formülden ayrıldı.");
                }

                RoundPlan kirpik = new RoundPlan(0, -1, 50, 0);
                Assert.AreEqual(1, kirpik.Round);
                Assert.AreEqual(0, kirpik.PoolTotal);
                Assert.AreEqual(1, kirpik.Ceiling);
                Assert.AreEqual(1, kirpik.TargetActive);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        // ------------------------------------------------------------ pause / run end / yeni round

        [Test]
        public void SpawnSaati_PauseDeltasindaIlerlemez()
        {
            var saat = new SpawnRefillClock();
            saat.Reset();

            Assert.IsTrue(saat.Advance(0.016f, 0.45f), "Kurulan saat ilk pozitif delta'da tik vermeli.");
            float kalan = saat.Remaining;
            Assert.AreEqual(0.45f, kalan, 0.0001f);

            for (int i = 0; i < 200; i++)
            {
                Assert.IsFalse(saat.Advance(0f, 0.45f), "dt=0 tik üretti.");
                Assert.IsFalse(saat.Advance(-0.02f, 0.45f), "Negatif dt tik üretti.");
            }

            Assert.AreEqual(kalan, saat.Remaining, 0.0f, "Pause deltasında kalan süre değişti.");
            Assert.AreEqual(1, saat.Ticks);

            // 0.45 s'lik gerçek ilerleme tam bir tik verir, fazlası değil.
            int tik = 0;
            for (int i = 0; i < 27; i++)
            {
                if (saat.Advance(1f / 60f, 0.45f)) tik++;
            }

            Assert.AreEqual(1, tik, "27 kare × 1/60 s = 0.45 s → tam bir tik beklenir.");
        }

        [Test]
        public void RunEnd_PendingSpawnIptalVeTamamlanmaYok()
        {
            _defter.BeginRound(1, 2, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);
            Dogur(out int b);

            _defter.Abort();
            Assert.AreEqual(RoundState.Aborted, _defter.State);
            Assert.IsFalse(_defter.IsRoundLive);
            Assert.IsFalse(_defter.CanSpawn(out string neden));
            Assert.AreEqual("round not active", neden);
            Assert.IsFalse(Dogur(out _), "Abort sonrası spawn rezervasyonu kabul edildi.");

            // Gecikmiş ölümler: havuz boş, yaşayan sıfıra düşer — yine de tamamlanma YOK.
            _defter.RegisterDeath(a);
            _defter.RegisterDeath(b);
            for (int i = 0; i < 3; i++)
            {
                _defter.Evaluate();
                _defter.TryComplete();
            }

            Assert.AreEqual(RoundState.Aborted, _defter.State);
            Assert.AreEqual(0, _tamamlanmaSayisi, "Abort sonrası gecikmiş ölüm roundu tamamladı.");
            Assert.AreEqual(2, _defter.Killed, "Abort sonrası ölümler yine sayılır (istatistik), tamamlama üretmez.");

            _defter.Abort();
            Assert.AreEqual(RoundState.Aborted, _defter.State, "Abort idempotent olmalı.");
        }

        [Test]
        public void YeniRound_OncekiRoundunYasayanKayitlariniTasimaz()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int eskiA);
            Dogur(out int eskiB);
            _defter.RegisterDeath(eskiA);

            _defter.BeginRound(2, 20, 11, Tavan);
            _defter.Activate();

            RoundPopulationSnapshot p = _defter.Snapshot();
            Assert.AreEqual(2, p.Round);
            Assert.AreEqual(0, p.Alive);
            Assert.AreEqual(0, p.Occupied);
            Assert.AreEqual(0, p.Killed);
            Assert.AreEqual(0, p.SpawnedTotal);
            Assert.AreEqual(20, p.Unspawned);

            // Eski round'un biletleri yeni deftere yabancı: hiçbir sayaç oynamaz.
            Assert.IsFalse(_defter.RegisterDespawn(eskiA));
            Assert.IsFalse(_defter.RegisterDeath(eskiB));
            Assert.IsFalse(_defter.RegisterDespawn(eskiB));
            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(0, _defter.Occupied);
            Assert.AreEqual(0, _defter.Killed);

            // Yeni round'un biletleri eskilerle çakışmaz.
            Dogur(out int yeni);
            Assert.AreNotEqual(eskiA, yeni);
            Assert.AreNotEqual(eskiB, yeni);
        }

        [Test]
        public void YeniRun_OncekiRunStateiniTasimaz()
        {
            _defter.BeginRound(4, 38, 15, Tavan);
            _defter.Activate();
            Dogur(out int bilet);
            _defter.DebugIgnorePool = true;
            _defter.Abort();

            _defter.Reset();

            RoundPopulationSnapshot p = _defter.Snapshot();
            Assert.AreEqual(RoundState.NotStarted, p.State);
            Assert.AreEqual(0, p.Round);
            Assert.AreEqual(0, p.PoolTotal);
            Assert.AreEqual(0, p.Unspawned);
            Assert.AreEqual(0, p.Alive);
            Assert.AreEqual(0, p.Occupied);
            Assert.AreEqual(0, p.Killed);
            Assert.IsFalse(_defter.DebugIgnorePool, "Debug havuz bayrağı yeni koşuya taşındı.");
            Assert.IsFalse(_defter.IsTracked(bilet));
            Assert.IsFalse(_defter.CanSpawn(out _));
        }

        // ------------------------------------------------------------ aktivasyon başarısızlığı

        [Test]
        public void SpawnInitializeBasarisizligi_SayaclariBozmaz()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();

            Assert.IsTrue(Dogur(out int bilet));
            Assert.AreEqual(R1Havuz - 1, _defter.Unspawned);

            Assert.IsTrue(_defter.RollbackSpawn(bilet), "Rollback reddedildi.");

            RoundPopulationSnapshot p = _defter.Snapshot();
            Assert.AreEqual(R1Havuz, p.Unspawned, "Başarısız spawn havuzdan sessizce kayboldu.");
            Assert.AreEqual(0, p.SpawnedTotal);
            Assert.AreEqual(0, p.Alive);
            Assert.AreEqual(0, p.Occupied);
            Assert.AreEqual(1, p.ActivationFailures, "Başarısızlık raporlanabilir olmalı.");
            Assert.IsFalse(_defter.IsTracked(bilet));

            Assert.IsFalse(_defter.RollbackSpawn(bilet), "İkinci rollback kabul edilmemeli.");
            Assert.AreEqual(1, _defter.ActivationFailures);

            // Round yine tamamlanabilir: kalan havuz normal akar.
            for (int i = 0; i < R1Havuz; i++)
            {
                Assert.IsTrue(Dogur(out int b), "Rollback sonrası havuz akmadı (" + i + ").");
                _defter.RegisterDeath(b);
            }

            _defter.Evaluate();
            Assert.IsTrue(_defter.TryComplete());
            Assert.AreEqual(R1Havuz, _defter.Killed);
        }

        /// <summary>
        /// S-01 koşulu (WP-003): aktivasyon başarısız gövde, geri alma çağrısından ÖNCE
        /// despawn raporu gönderse bile havuz stoğu geri gelir ve başarısızlık sayılır —
        /// sıra ne olursa olsun "havuzdan sessiz kayıp yok".
        /// </summary>
        [Test]
        public void AktivasyonOnaysizDespawn_RollbackSayilirHavuzGeriGelir()
        {
            _defter.BeginRound(1, R1Havuz, R1Hedef, Tavan);
            _defter.Activate();

            Assert.IsTrue(_defter.TryReserveSpawn(out int bilet));
            Assert.IsTrue(_defter.IsPendingActivation(bilet));
            Assert.AreEqual(R1Havuz - 1, _defter.Unspawned);

            // Sıra 1: önce despawn raporu (sink), sonra rollback denemesi.
            Assert.IsTrue(_defter.RegisterDespawn(bilet), "Onaysız biletin despawn raporu kabul edilmeli (rollback olarak).");
            Assert.AreEqual(R1Havuz, _defter.Unspawned, "Havuz stoğu geri gelmedi (sessiz kayıp).");
            Assert.AreEqual(0, _defter.SpawnedTotal, "Hayalet spawn kaldı.");
            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(0, _defter.Occupied);
            Assert.AreEqual(1, _defter.ActivationFailures, "Başarısızlık sayılmadı.");
            Assert.IsFalse(_defter.RollbackSpawn(bilet), "Zaten geri alınmış bilet ikinci kez geri alınmamalı.");
            Assert.AreEqual(1, _defter.ActivationFailures);
            Assert.AreEqual(R1Havuz, _defter.Unspawned);

            // Sıra 2: önce rollback, sonra geç despawn raporu.
            Assert.IsTrue(_defter.TryReserveSpawn(out int bilet2));
            Assert.IsTrue(_defter.RollbackSpawn(bilet2));
            Assert.IsFalse(_defter.RegisterDespawn(bilet2), "Geri alınmış biletin geç despawn raporu yok sayılmalı.");
            Assert.AreEqual(R1Havuz, _defter.Unspawned);
            Assert.AreEqual(2, _defter.ActivationFailures);

            // Onaylı yaşam: despawn artık normal despawn'dır (kill değil, rollback değil).
            Assert.IsTrue(_defter.TryReserveSpawn(out int bilet3));
            Assert.IsTrue(_defter.ConfirmActivation(bilet3));
            Assert.IsFalse(_defter.IsPendingActivation(bilet3));
            Assert.IsTrue(_defter.RegisterDespawn(bilet3));
            Assert.AreEqual(R1Havuz - 1, _defter.Unspawned, "Onaylı yaşamın despawn'ı havuza geri yazılmamalı.");
            Assert.AreEqual(1, _defter.SpawnedTotal);
            Assert.AreEqual(2, _defter.ActivationFailures);
            Assert.IsFalse(_defter.ConfirmActivation(bilet + 999));
        }

        [Test]
        public void Reconcile_DespawnRaporuGelmeyenGovdeyiKapatirVeSayar()
        {
            _defter.BeginRound(1, 2, R1Hedef, Tavan);
            _defter.Activate();
            Dogur(out int a);

            Assert.IsTrue(_defter.Reconcile(a));
            Assert.AreEqual(0, _defter.Alive);
            Assert.AreEqual(0, _defter.Occupied);
            Assert.AreEqual(1, _defter.Reconciled);
            Assert.AreEqual(0, _defter.Killed, "Reconcile kill değildir.");
            Assert.AreEqual(0, _defter.ActivationFailures, "Onaylı yaşamın reconcile'ı aktivasyon başarısızlığı değildir.");
            Assert.IsFalse(_defter.Reconcile(a));
        }

        // ------------------------------------------------------------ debug havuz bypass

        [Test]
        public void DebugIgnorePool_HavuzuTuketmezVeTamamlanmayiAskiyaAlir()
        {
            _defter.BeginRound(1, 2, 10, Tavan);
            _defter.Activate();
            _defter.DebugIgnorePool = true;

            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(Dogur(out int b));
                _defter.RegisterDeath(b);
            }

            Assert.AreEqual(2, _defter.Unspawned, "Debug modunda havuz tüketilmemeli.");
            _defter.Evaluate();
            Assert.IsFalse(_defter.TryComplete());
            Assert.AreEqual(RoundState.Active, _defter.State);

            _defter.DebugIgnorePool = false;
            _defter.DebugDrainPool();
            _defter.Evaluate();
            Assert.IsTrue(_defter.TryComplete(), "Debug drain + yaşayan 0 → tamamlanma.");
        }
    }
}
