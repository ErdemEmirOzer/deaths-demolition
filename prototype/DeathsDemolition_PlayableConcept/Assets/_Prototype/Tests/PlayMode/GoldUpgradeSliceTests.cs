using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-004 · M1 GOLD → PAKET → CÜZDAN → GELİŞTİRME → SONRAKİ ROUND nöbetçisi — Run
    /// sahnesinde, gerçek zincirle (ZombieEnemy.Kill → Die → EnemyDied → GoldService →
    /// GoldOrb → GoldWallet; RoundLedger → RoundCleared → RunDirector sweep → shop),
    /// gerçek kare, gerçek Metal. REQ-018 · REQ-059 · REQ-076.
    ///
    /// Sayılar kilitlenmez: Standard gold değeri config'ten okunur (PrototypeOnly), magnet
    /// yarıçapı GoldService'ten. Kontrollü senaryolarda director spawn'ı reflection ile
    /// kapatılır (F1 "Spawning enabled" alanı) ve düşmanlar EnemyFactory.Spawn (biletsiz)
    /// ile yerleştirilir; round bitişi senaryosu gerçek ledger ile koşar.
    /// </summary>
    [TestFixture]
    public sealed class GoldUpgradeSliceTests
    {
        private const float KurulumZamanAsimi = 40f;
        private const float FazZamanAsimi = 25f;

        private RunDirector _run;
        private EnemySpawnDirector _director;
        private GoldService _gold;
        private PlayerRig _rig;
        private int _standardDeger;

        // LogAssert.ignoreFailingMessages sahne yükleme uyarıları için açık; ekonomi/koşu
        // hataları yine de yakalansın diye kendi hata toplayıcımız var (TearDown'da 0 olmalı).
        private readonly List<string> _ekonomiHatalari = new List<string>();

        [SetUp]
        public void SetUp()
        {
            RoundUpgradeShop.TreeV2TestOverride = false;   // WP-104: these slices pin the v1 card shop (retired with the S-09 V2 screen)
            _ekonomiHatalari.Clear();
            Application.logMessageReceived += HataTopla;
        }

        private void HataTopla(string mesaj, string iz, LogType tur)
        {
            if (tur == LogType.Warning && mesaj.Contains("A second "))
            {
                _ekonomiHatalari.Add("Warning: " + mesaj);   // duplicate RunDirector/GoldService/EnemySpawnDirector
                return;
            }

            if (tur != LogType.Error && tur != LogType.Exception && tur != LogType.Assert) return;
            if (mesaj.Contains("[Run]") || mesaj.Contains("[Gold]") || mesaj.Contains("[GoldProbe]") ||
                mesaj.Contains("NullReferenceException") || mesaj.Contains("MissingReferenceException") ||
                mesaj.Contains("InvalidOperationException"))
            {
                _ekonomiHatalari.Add(tur + ": " + mesaj);
            }
        }

        // ============================================================== kurulum

        private IEnumerator RunSahnesiniHazirla(bool spawnKapali)
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null, KurulumZamanAsimi, "RunDirector oluşmadı.");
            yield return Bekle(() => EnemySpawnDirector.Instance != null, KurulumZamanAsimi, "EnemySpawnDirector oluşmadı.");
            yield return Bekle(() => GoldService.Instance != null, KurulumZamanAsimi, "GoldService oluşmadı.");
            yield return Bekle(() => PlayerRegistry.Primary != null, KurulumZamanAsimi, "Oyuncu doğmadı.");
            yield return Bekle(() => WeaponRegistry.ActiveController != null, KurulumZamanAsimi, "Silah kontrolcüsü yok.");

            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _gold = GoldService.Instance;
            _rig = PlayerRegistry.Primary;

            if (spawnKapali)
            {
                SpawnAyarla(false);
            }

            yield return Bekle(() => _run.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");

            if (spawnKapali)
            {
                _director.ClearAllEnemies();
                EnemyFactory.ClearAll();
                _gold.ClearOrbs(false);
                yield return null;
                Assert.AreEqual(0, ZombieEnemy.Live.Count, "Kontrollü kurulum: sahnede zombi kalmamalı.");
                Assert.AreEqual(0, _gold.LiveOrbCount);

                // Batchmode kareleri 0.3 ms'ye kadar düşebiliyor (ölçüldü: iter2c/iter2d); ölçekli
                // saat gerçek zamandan kopuyor. Kontrollü senaryolar 60 Hz sabit kareyle koşar;
                // pause (timeScale 0) yine deltaTime 0 üretir.
                Time.captureDeltaTime = 1f / 60f;
            }

            Assert.IsTrue(_gold.Wallet.IsOpen, "Koşu başında cüzdan açık olmalı.");
            Assert.AreEqual(_run.RunId, _gold.Wallet.RunId, "Cüzdan koşu kimliğini taşımalı.");
            Assert.IsTrue(_gold.Ledger.IsRunLive, "Koşu başında hak ediş ledger'ı açık olmalı.");

            PrototypeConfig config = PrototypeConfig.Active;
            Assert.IsNotNull(config);
            _standardDeger = config.GoldReward(EnemyTier.Standard);
            Assert.Greater(_standardDeger, 0, "Standard gold değeri 0: test öncülü bozuk.");

            Assert.AreEqual(UnityEngine.Rendering.GraphicsDeviceType.Metal, SystemInfo.graphicsDeviceType,
                "WP-004 PlayMode testleri gerçek Metal yürütmesinde koşmalı (batchmode, -nographics YOK).");
            Debug.Log("[DD-TEST] wp004.gfx=" + SystemInfo.graphicsDeviceType + " standardGold=" + _standardDeger +
                      " magnet=" + _gold.MagnetRadius + " contact=" + _gold.ContactRadius + " runId=" + _run.RunId);
        }


        // WP-049C (DEC-087): sevk artık round başına 2 kart. Bu WP-008 sözleşme testi "limit dolunca kilit"
        // davranışını limit = 1 ile belgeler (config değeri olarak hâlâ geçerli); DEC-087 sevk limiti
        // UpgradePurchaseLimitTests'te, ekranın ikinci seçim akışı S-09 UI paketinde doğrulanır.
        private PrototypeConfig _sevkConfigDec071, _dec071Klon;
        private void Dec071Klonu()
        {
            _sevkConfigDec071 = PrototypeConfig.Active;
            _dec071Klon = UnityEngine.Object.Instantiate(_sevkConfigDec071);
            _dec071Klon.Economy.PurchasesPerRound = 1;
            PrototypeConfig.SetActive(_dec071Klon);
        }
        private void Dec071KlonunuGeriAl()
        {
            if (_sevkConfigDec071 != null && PrototypeConfig.Active != _sevkConfigDec071) PrototypeConfig.SetActive(_sevkConfigDec071);
            if (_dec071Klon != null) UnityEngine.Object.DestroyImmediate(_dec071Klon);
            _sevkConfigDec071 = null; _dec071Klon = null;
        }

        [TearDown]
        public void TearDown()
        {
            RoundUpgradeShop.TreeV2TestOverride = null;
            Application.logMessageReceived -= HataTopla;
            Dec071KlonunuGeriAl();
            Time.captureDeltaTime = 0f;
            RunFlowClock.ClearHolds();
            if (EnemySpawnDirector.Instance != null)
            {
                SpawnAyarla(true);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(0, _ekonomiHatalari.Count,
                "Test boyunca ekonomi/koşu hatası loglandı:\n" + string.Join("\n", _ekonomiHatalari));
        }

        /// <summary>F1 "Spawning enabled" alanı: yalnız test kurulumu için, üretim yüzeyi değişmez.</summary>
        private static void SpawnAyarla(bool acik)
        {
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "EnemySpawnDirector._spawningEnabled alanı bulunamadı (F1 anahtarı yeniden adlandırıldı mı?).");
            f.SetValue(EnemySpawnDirector.Instance, acik);
        }

        private Vector3 OyuncuKonumu => _rig.transform.position;

        /// <summary>
        /// A BARE Standard zombie. WP-004's flat-value assertions ("one Standard kill = the
        /// Standard value") are about the unarmoured zombie; since WP-008 an armoured
        /// Standard (WP-006 spawn roll) pays base + spawn-armour bonus, so the harness strips
        /// the roll (the test seam retakes the spawn snapshot) to keep these tests
        /// deterministic. The armoured path is EconomyUpgradeRulesSliceTests' business.
        /// </summary>
        private ZombieEnemy Dogur(Vector3 konum)
        {
            ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, _run.Round, konum, Quaternion.identity);
            Assert.IsNotNull(z, "Test zombisi doğmadı.");
            Assert.IsTrue(z.IsAlive);
            z.FitArmorComposition(false, false, false);
            Assert.AreEqual(_standardDeger, z.Reward.Total, "Zırhsız Standard: ödül = base.");
            return z;
        }

        private static GoldOrb TekPaket()
        {
            GoldOrb[] hepsi = UnityEngine.Object.FindObjectsOfType<GoldOrb>(false);
            GoldOrb bulunan = null;
            int aktif = 0;
            for (int i = 0; i < hepsi.Length; i++)
            {
                if (hepsi[i].IsSpawned)
                {
                    aktif++;
                    bulunan = hepsi[i];
                }
            }

            Assert.AreEqual(1, aktif, "Tam bir aktif paket beklenirken " + aktif + " bulundu.");
            return bulunan;
        }

        private static int AktifPaketSayisi()
        {
            GoldOrb[] hepsi = UnityEngine.Object.FindObjectsOfType<GoldOrb>(false);
            int aktif = 0;
            for (int i = 0; i < hepsi.Length; i++)
            {
                if (hepsi[i].IsSpawned) aktif++;
            }

            return aktif;
        }

        private static int TumYasayanlariOldur()
        {
            int n = 0;
            IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] != null && live[i].IsAlive)
                {
                    live[i].Kill();
                    n++;
                }
            }

            return n;
        }

        private static IEnumerator AkisiYatistir()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f && SceneManager.GetActiveScene().name != PrototypeScenes.Results)
            {
                yield return null;
            }

            yield return null;
            Debug.Log("[DD-TEST] wp004 akış yatıştı: scene=" + SceneManager.GetActiveScene().name);
        }

        // ============================================================== P1-P2 · tek paket, yerde kalır

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P1_P2_TekKill_TekPaketOlumKonumunda_SureyleKaybolmaz()
        {
            yield return RunSahnesiniHazirla(spawnKapali: true);

            Vector3 olumKonumu = OyuncuKonumu + new Vector3(12f, 0f, 0f);
            ZombieEnemy z = Dogur(olumKonumu);
            int lifeId = z.LifeId;
            Assert.Greater(lifeId, _gold.Ledger.LifeWatermark, "Test zombisi bu koşunun yaşamı olmalı.");

            int kredi = 0;
            _gold.Wallet.Credited += (a, b, p) => kredi++;

            int paketOnce = _gold.PacketsSpawned;

            // Hasar vermek gold üretmez: öldürmeyen gerçek bir isabet → paket 0, hak ediş 0.
            var yaralama = new DamageHit
            {
                Amount = Mathf.Max(0.5f, z.MaxHealth * 0.25f),
                Region = DD.Prototype.Damage.BodyRegion.UpperBody,
                Point = z.transform.position + Vector3.up,
                Direction = Vector3.forward,
                ImpactForce = 1f,
                Source = DamageSourceKind.Bullet
            };
            DamageResult yara = z.ApplyHit(in yaralama);
            Assert.IsFalse(yara.Killed, "Test öncülü: %25 hasar öldürmemeli.");
            Assert.IsTrue(z.IsAlive);
            Assert.AreEqual(paketOnce, _gold.PacketsSpawned, "Hasar vermek paket üretmez.");
            Assert.AreEqual(0, _gold.Ledger.Granted, "Hasar vermek hak ediş üretmez.");
            Assert.AreEqual(0, _gold.Wallet.Balance);

            z.Kill();

            // Aynı kare: hak ediş + paket (ölüm olayı senkron).
            Assert.AreEqual(1, _gold.Ledger.Granted, "Bir ölüm bir hak ediş.");
            Assert.AreEqual(paketOnce + 1, _gold.PacketsSpawned, "Bir ölüm tam bir paket.");
            Assert.AreEqual(1, _gold.LiveOrbCount);
            Assert.AreEqual(0, _gold.FallbackCredits, "Havuz yolu: doğrudan kredi güvenlik yolu kullanılmadı.");
            Assert.AreEqual(0, kredi, "Ölüm anında kredi YOK; gold paketle gelir.");
            Assert.AreEqual(0, _gold.Wallet.Balance);

            yield return null;
            GoldOrb paket = TekPaket();
            Vector3 fark = paket.transform.position - olumKonumu;
            fark.y = 0f;
            Assert.Less(fark.magnitude, 2f, "Paket ölüm konumunun yakınında doğmalı (saçılım ≤ ~1 m): " + fark.magnitude);
            Assert.AreEqual(_standardDeger, paket.Amount);
            Assert.AreEqual(_run.RunId, paket.Packet.RunId);

            // Yerde kalır: 60 kare × 0.5 s = 30 s simülasyon (eski 22 s ömrün üstü); oyuncu uzak (12 m > magnet).
            Time.captureDeltaTime = 0.5f;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            Time.captureDeltaTime = 1f / 60f;
            yield return null;

            Assert.AreEqual(1, _gold.LiveOrbCount, "Paket 30 s simülasyon sonunda hâlâ yerde.");
            Assert.IsTrue(paket.IsSpawned);
            Assert.AreEqual(GoldPacketState.Waiting, paket.Packet.State, "Paket beklemede; süreyle kaybolmadı.");
            Assert.AreEqual(0, _gold.Wallet.Balance, "Kendiliğinden kredi de yok.");
            Assert.AreEqual(0, kredi);

            Debug.Log("[DD-TEST] wp004.p1p2 packets=1 dist=" + fark.magnitude.ToString("0.00") + " simSec=30 state=" + paket.Packet.State);
        }

        // ============================================================== P3-P5 · magnet, tek kredi, tekrar yok, XP sabit

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P3_P5_P24_Magnet_TekKredi_TekrarYok_ProfilXpSabit()
        {
            yield return RunSahnesiniHazirla(spawnKapali: true);

            int xpOnce = ProfileService.Current.TotalXpEarned;
            int levelOnce = ProfileService.Current.ProfileLevel;
            int intoOnce = ProfileService.Current.XpIntoLevel;
            int profilDegisti = 0;
            Action profilDinle = () => profilDegisti++;
            ProfileService.Changed += profilDinle;

            // WP-098B his olayları: Landed → MagnetStart → FeelEvents.GoldCollected (S-02, cüzdan kredisinden) → (HUD varsa) FlightArrived.
            // Köprü cüzdanı oyunun kendi yolundan bağlar (RoundStarted / EnemyDied); Rearm sahne değişimindekiyle aynı, idempotent.
            FeelEvents.Rearm();
            var hisSirasi = new List<string>();
            int toplamaAnindaBakiye = -1;
            FeelGold sonToplama = default;
            Action<GoldLandedFeel> hisInis = e => hisSirasi.Add("Landed");
            Action<GoldMagnetFeel> hisMagnet = e => hisSirasi.Add("MagnetStart");
            Action<FeelGold> hisToplama = e => { hisSirasi.Add("GoldCollected"); sonToplama = e; toplamaAnindaBakiye = _gold.Wallet.Balance; };
            Action<GoldArrivedFeel> hisVaris = e => hisSirasi.Add("FlightArrived");
            GoldFeelEvents.Landed += hisInis;
            GoldFeelEvents.MagnetStart += hisMagnet;
            FeelEvents.GoldCollected += hisToplama;
            GoldFeelEvents.FlightArrived += hisVaris;
            int yoldaOnce = HudGoldTarget.InFlight;

            ZombieEnemy z = Dogur(OyuncuKonumu + new Vector3(12f, 0f, 0f));
            z.Kill();
            yield return null;
            GoldOrb paket = TekPaket();

            // Hop bitsin (Waiting), sonra oyuncu magnet yarıçapının içine girsin.
            yield return Bekle(() => paket.Packet.State == GoldPacketState.Waiting, 5f, "Paket yere inmedi.");

            int kredi = 0;
            int krediMiktar = 0;
            _gold.Wallet.Credited += (a, b, p) => { kredi++; krediMiktar += a; };

            Vector3 hedef = paket.transform.position + new Vector3(-Mathf.Max(0.5f, _gold.MagnetRadius - 0.6f), 0f, 0f);
            hedef.y = OyuncuKonumu.y;
            _rig.Teleport(hedef, Quaternion.identity);

            int attractedKare = -1;
            for (int i = 0; i < 5 && attractedKare < 0; i++)
            {
                yield return null;
                if (paket.IsAttracted) attractedKare = i;
            }

            Assert.GreaterOrEqual(attractedKare, 0, "Oyuncu magnet alanına girince paket çekilmeye başlamalı.");

            yield return Bekle(() => kredi > 0, 5f, "Magnet paketi oyuncuya ulaştırmadı.");
            Assert.AreEqual(1, kredi, "Ulaşınca bakiye TAM BİR kez arttı.");
            Assert.AreEqual(_standardDeger, krediMiktar);
            Assert.AreEqual(_standardDeger, _gold.Wallet.Balance);
            Assert.AreEqual(_standardDeger, _gold.Wallet.EarnedByPickup);
            Assert.AreEqual(1, _gold.PacketsCollected);
            Assert.AreEqual(_gold.Wallet.Balance, RunEvents.Current.Gold, "HUD anlık görüntüsü cüzdanla aynı.");

            // Tekrar gelir yok: 30 kare daha; paket flaş (HUD yok) ya da HUD uçuşu (WP-098B, unscaled ≤ 0,35 s) sonrası havuza döner.
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            yield return Bekle(() => _gold.LiveOrbCount == 0, 2f, "Toplanan paket havuza dönmedi (flaş/HUD uçuşu bitmedi).");
            Assert.AreEqual(1, kredi, "Aynı paket ikinci kez gelir üretmedi.");
            Assert.AreEqual(_standardDeger, _gold.Wallet.Balance);
            Assert.AreEqual(0, _gold.LiveOrbCount, "Toplanan paket havuza döndü.");
            Assert.AreEqual(0, AktifPaketSayisi());

            GoldFeelEvents.Landed -= hisInis;
            GoldFeelEvents.MagnetStart -= hisMagnet;
            FeelEvents.GoldCollected -= hisToplama;
            GoldFeelEvents.FlightArrived -= hisVaris;
            Assert.GreaterOrEqual(hisSirasi.Count, 3, "His olayları: " + string.Join(",", hisSirasi));
            Assert.AreEqual("Landed", hisSirasi[0], "His sırası: " + string.Join(",", hisSirasi));
            Assert.AreEqual("MagnetStart", hisSirasi[1], "His sırası: " + string.Join(",", hisSirasi));
            Assert.AreEqual("GoldCollected", hisSirasi[2], "His sırası: " + string.Join(",", hisSirasi));
            Assert.AreEqual(_standardDeger, sonToplama.Amount, "FeelGold.Amount = paket");
            Assert.AreEqual(_standardDeger, sonToplama.Balance, "FeelGold.Balance = kredi sonrası bakiye");
            Assert.LessOrEqual(hisSirasi.Count, 4, "Paket başına en çok bir FlightArrived: " + string.Join(",", hisSirasi));
            if (hisSirasi.Count == 4) Assert.AreEqual("FlightArrived", hisSirasi[3]);
            Assert.AreEqual(_standardDeger, toplamaAnindaBakiye, "Kredi toplama anında (uçuştan bağımsız).");
            Assert.AreEqual(yoldaOnce, HudGoldTarget.InFlight, "Her Depart bir Arrive ile kapandı.");

            // Profil XP: gold toplamak profile hiçbir şey yazmaz.
            ProfileService.Changed -= profilDinle;
            Assert.AreEqual(xpOnce, ProfileService.Current.TotalXpEarned, "Gold toplamak profil XP'sini değiştirdi.");
            Assert.AreEqual(levelOnce, ProfileService.Current.ProfileLevel);
            Assert.AreEqual(intoOnce, ProfileService.Current.XpIntoLevel);
            Assert.AreEqual(0, profilDegisti, "Gold işlemi profil Changed olayı üretmedi.");

            Debug.Log("[DD-TEST] wp004.p3p5 attractedFrame=" + attractedKare + " credits=" + kredi + " balance=" + _gold.Wallet.Balance +
                      " profileXp=" + ProfileService.Current.TotalXpEarned + " unchanged");
        }

        // ============================================================== P6 · düşman başına bir paket

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P6_OnKillAyniKare_OnPaket_DirectCreditSifir()
        {
            yield return RunSahnesiniHazirla(spawnKapali: true);

            var zombiler = new List<ZombieEnemy>(10);
            for (int i = 0; i < 10; i++)
            {
                float aci = i * Mathf.PI * 2f / 10f;
                float r = 10f + (i % 3) * 2f;   // 10-14 m halka: magnet dışı
                zombiler.Add(Dogur(OyuncuKonumu + new Vector3(Mathf.Cos(aci) * r, 0f, Mathf.Sin(aci) * r)));
            }

            for (int i = 0; i < zombiler.Count; i++)
            {
                zombiler[i].Kill();
            }

            Assert.AreEqual(10, _gold.Ledger.Granted, "10 ölüm 10 hak ediş.");
            Assert.AreEqual(10, _gold.PacketsSpawned, "10 ölüm 10 paket.");
            Assert.AreEqual(10, _gold.LiveOrbCount);
            Assert.AreEqual(0, _gold.FallbackCredits, "Hiçbir kill doğrudan kredi güvenlik yoluna düşmedi.");
            Assert.AreEqual(0, _gold.Ledger.Rejected);
            Assert.AreEqual(0, _gold.Wallet.Balance, "Paketler yerde; henüz kredi yok.");

            yield return null;
            Assert.AreEqual(10, AktifPaketSayisi());

            // WP-008 (DEC-043): a Standard zombie can spawn WITH armour (WP-006 roll), and its
            // packet then carries base + spawn-armour bonus. The floor holds the sum of the
            // ten spawn snapshots, never a flat 10 × base.
            int beklenenToplam = 0;
            for (int i = 0; i < zombiler.Count; i++)
            {
                Assert.IsTrue(zombiler[i].Reward.IsValid);
                Assert.AreEqual(_standardDeger, zombiler[i].Reward.Base, "Base = Standard değeri.");
                Assert.AreEqual(0, zombiler[i].Reward.VersionBonus, "Standard versiyon bonusu 0.");
                beklenenToplam += zombiler[i].Reward.Total;
            }

            Assert.GreaterOrEqual(beklenenToplam, 10 * _standardDeger);
            Assert.AreEqual(beklenenToplam, _gold.OutstandingGold, "Yerdeki gold = on spawn snapshot'ının toplamı.");

            // Aynı gövdenin ikinci ölüm çağrısı: Kill() _alive koruması + ledger → paket yok.
            zombiler[0].Kill();
            Assert.AreEqual(10, _gold.PacketsSpawned, "Duplicate ölüm ikinci paket üretmedi.");

            // Despawn (Remove) gold üretmez.
            int once = _gold.PacketsSpawned;
            int hakEdis = _gold.Ledger.Granted;
            EnemyFactory.ClearAll();
            yield return null;
            Assert.AreEqual(once, _gold.PacketsSpawned, "Cleanup/despawn paket üretmedi.");
            Assert.AreEqual(hakEdis, _gold.Ledger.Granted);

            Debug.Log("[DD-TEST] wp004.p6 kills=10 packets=" + _gold.PacketsSpawned + " fallback=" + _gold.FallbackCredits +
                      " outstandingGold=" + _gold.OutstandingGold);
        }

        // ============================================================== P7 · cesetler toplamayı engellemez

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P7_CesetlerinUstundenPaketlerToplanir()
        {
            yield return RunSahnesiniHazirla(spawnKapali: true);

            Vector3 yigin = OyuncuKonumu + new Vector3(8f, 0f, 0f);
            int beklenenToplam = 0;   // WP-008: sum of the three spawn snapshots (armour may be rolled on a Standard)
            for (int i = 0; i < 3; i++)
            {
                beklenenToplam += Dogur(yigin + new Vector3(i * 0.15f, 0f, 0f)).Reward.Total;
            }

            Assert.AreEqual(3, TumYasayanlariOldur());
            Assert.AreEqual(3, _gold.LiveOrbCount);

            // Cesetler yerde (Live listesinde, ölü, controller kapalı — WP-002 sözleşmesi).
            Assert.AreEqual(3, ZombieEnemy.Live.Count, "Üç ceset yerde.");
            for (int i = 0; i < ZombieEnemy.Live.Count; i++)
            {
                Assert.IsFalse(ZombieEnemy.Live[i].IsAlive);
            }

            int kredi = 0;
            _gold.Wallet.Credited += (a, b, p) => kredi++;

            // Oyuncu cesetlerin yanına ışınlanır: yığın merkezinden (magnet − 1.5 m) uzağa, böylece
            // en fazla ~0.95 m saçılan her paket magnet yarıçapının içinde kalır; paketler inince çekilir.
            Vector3 hedef = yigin + new Vector3(-Mathf.Max(0.5f, _gold.MagnetRadius - 1.5f), 0f, 0f);
            hedef.y = OyuncuKonumu.y;
            _rig.Teleport(hedef, Quaternion.identity);

            yield return Bekle(() => kredi >= 3, 8f, "Cesetlerin üstündeki paketler toplanamadı.");
            Assert.AreEqual(3, kredi);
            Assert.AreEqual(beklenenToplam, _gold.Wallet.Balance, "Bakiye = üç spawn snapshot'ının toplamı (WP-008).");
            Assert.GreaterOrEqual(_gold.Wallet.Balance, 3 * _standardDeger);
            Assert.AreEqual(0, _gold.CreditRefusals);
            Assert.AreEqual(0, _gold.PacketsLost);

            Debug.Log("[DD-TEST] wp004.p7 corpses=" + ZombieEnemy.Live.Count + " credits=" + kredi + " balance=" + _gold.Wallet.Balance);
        }

        // ============================================================== P8-P9 · pause

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P8_P9_Pause_MagnetDonar_SonraGuvenliDevam()
        {
            yield return RunSahnesiniHazirla(spawnKapali: true);

            ZombieEnemy z = Dogur(OyuncuKonumu + new Vector3(12f, 0f, 0f));
            z.Kill();
            yield return null;
            GoldOrb paket = TekPaket();
            yield return Bekle(() => paket.Packet.State == GoldPacketState.Waiting, 5f, "Paket yere inmedi.");

            // Magnet başlasın, sonra pause.
            Vector3 hedef = paket.transform.position + new Vector3(-(_gold.MagnetRadius - 0.3f), 0f, 0f);
            hedef.y = OyuncuKonumu.y;
            _rig.Teleport(hedef, Quaternion.identity);
            yield return Bekle(() => paket.IsAttracted, 2f, "Magnet başlamadı.");

            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;   // aynı karede deltaTime hâlâ > 0 olabilir; bir kare sonra 0
            Assert.IsTrue(RunFlowClock.IsFrozen);
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);

            Vector3 donmusKonum = paket.transform.position;
            float saat = paket.Packet.MagnetClock;
            int bakiye = _gold.Wallet.Balance;

            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0f, (paket.transform.position - donmusKonum).magnitude, 0.0001f, "Pause'ta magnet hareketi ilerledi.");
            Assert.AreEqual(saat, paket.Packet.MagnetClock, 0.0001f, "Pause'ta magnet saati ilerledi.");
            Assert.AreEqual(bakiye, _gold.Wallet.Balance, "Pause'ta gold kredilendi.");
            Assert.AreEqual(GoldPacketState.Magnetizing, paket.Packet.State);

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return Bekle(() => _gold.Wallet.Balance > bakiye, 5f, "Pause sonrası magnet paketi getirmedi.");
            Assert.AreEqual(_standardDeger, _gold.Wallet.Balance);
            yield return SimulasyonSerbestOlmali("wp004.p9");

            Debug.Log("[DD-TEST] wp004.p8p9 pauseFrames=30 delta=0 balanceAfter=" + _gold.Wallet.Balance);
        }

        // ============================================================== P10-P19 · round bitişi → sweep → ekran → satın alma → sonraki round

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P10_P19_RoundBitisi_Sweep_Ekran_SatinAlma_SonrakiRound()
        {
            yield return RunSahnesiniHazirla(spawnKapali: false);
            Dec071Klonu();   // WP-049C: bu test limit = 1 sözleşmesini belgeler (bkz. yardımcı notu)
            _rig.Health.DiagnosisInvulnerable = true;   // ölçüm oyuncu ölümüyle kesilmesin (tanı)

            // Temiz bir round 1: director spawn'ı gerçek, oyuncu merkezde (spawn noktaları uzak → paketler yerde kalır).
            RunFlowClock.ClearHolds();
            _run.BeginRound(1);
            yield return null;
            Assert.AreEqual(RunPhase.Fighting, _run.Phase);
            Assert.AreEqual(RoundState.Active, _director.State);
            int havuz = _director.RoundPoolTotal;
            Assert.Greater(havuz, 0);

            // RoundCleared karesinde (RunDirector sweep'i ÇAĞIRDIKTAN sonra yayınlanır) neyi gördüğümüzü kaydet.
            int tamamlanmaSayisi = 0;
            float tamamlanmaTimeScale = -1f;
            int tamamlanmaBakiye = -1;
            int tamamlanmaOutstanding = -1;
            GoldAutoCollectReport tamamlanmaSweep = default;
            RoundReport tamamlanmaRapor = default;
            bool tamamlanmaErtelendi = false;   // WP-095 K5
            float tamamlanmaAni = 0f;
            Action<int> tamamlanma = r =>
            {
                tamamlanmaSayisi++;
                tamamlanmaRapor = _run.LastRoundReport;   // DEC-113: donmuş olmalı (sweep sonrası, RoundCompleted öncesi)
                tamamlanmaTimeScale = Time.timeScale;
                tamamlanmaErtelendi = _run.RoundEndHoldDeferred;
                tamamlanmaAni = Time.realtimeSinceStartup;
                tamamlanmaBakiye = _gold.Wallet.Balance;
                tamamlanmaOutstanding = _gold.OutstandingPacketCount;
                tamamlanmaSweep = _gold.LastAutoCollect;
            };
            RunEvents.RoundCompleted += tamamlanma;

            int sweepOlayi = 0;
            _gold.AutoCollected += r => sweepOlayi++;

            // WP-098B: FeelEvents.GoldCollected cüzdan kredisi başına bir kez (süpürme toplu değil: süpürülen her sikke bir olay);
            // süpürme tek karede koştuğu için zincir indeksi sikke sikke artar (perde merdiveni). HUD varsa sikke başına bir FlightArrived.
            FeelEvents.Rearm();
            var hisZincir = new List<int>();
            int hisToplamMiktar = 0, hisSonBakiye = -1;
            int supurmeVaris = 0;
            Action<FeelGold> hisToplama = e => { hisZincir.Add(e.ChainIndex); hisToplamMiktar += e.Amount; hisSonBakiye = e.Balance; };
            Action<GoldArrivedFeel> hisSupurmeVaris = e => { if (e.Kind == GoldCollectKind.RoundClearAuto) supurmeVaris++; };
            FeelEvents.GoldCollected += hisToplama;
            GoldFeelEvents.FlightArrived += hisSupurmeVaris;

            // WP-008: every kill pays its spawn snapshot (Standard base + rolled armour bonus);
            // the round's income is the sum of those, read off the death events.
            int roundOdulToplami = 0;
            Action<EnemyDeathInfo> odulTopla = info => { roundOdulToplami += info.GoldReward; Assert.IsTrue(info.Reward.IsValid); Assert.AreEqual(info.Reward.Total, info.GoldReward); };
            DamageEvents.EnemyDied += odulTopla;

            // Havuzu tüket: her karede yaşayan her director zombisini öldür. Oyuncu merkezde;
            // spawn noktaları magnet dışında → paketler yerde birikir (P10).
            float t0 = Time.realtimeSinceStartup;
            int oldurulen = 0;
            int enSonPaketSayisi = 0;
            while (tamamlanmaSayisi == 0 && Time.realtimeSinceStartup - t0 < 60f)
            {
                oldurulen += TumYasayanlariOldur();
                enSonPaketSayisi = Mathf.Max(enSonPaketSayisi, _gold.OutstandingPacketCount);
                yield return null;
            }

            RunEvents.RoundCompleted -= tamamlanma;
            DamageEvents.EnemyDied -= odulTopla;

            Assert.AreEqual(1, tamamlanmaSayisi, "RoundCleared tam bir kez yayınlanmalı.");
            Assert.AreEqual(havuz, oldurulen, "Havuzun tamamı öldürüldü.");
            Assert.AreEqual(havuz, _gold.Ledger.Granted, "Her düşman bir hak ediş.");
            Assert.AreEqual(havuz, _gold.PacketsSpawned, "Her düşman bir paket.");
            Assert.AreEqual(0, _gold.FallbackCredits);
            Assert.Greater(enSonPaketSayisi, 0, "Round boyunca yerde paket birikmiş olmalı (kaybolmadı).");

            // RoundCleared anı: dünya donuk, sweep BİTMİŞ, bakiye tam.
            // WP-095 K5 (C-00 08.10) is the root cause of the branch: top-down with the last zombie's slow motion running, the
            // round-end hold is DEFERRED until the moment ends (≤ 1.0 s); the sweep and RoundCompleted stay in the clear frame.
            // Deferred: the world still runs (slowly) at RoundCompleted and the freeze must land within 1.0 s (+ one frame).
            // Not deferred (first / third person, the slow motion off): the pre-K5 expectation, frozen at RoundCompleted.
            if (tamamlanmaErtelendi)
            {
                Assert.Greater(tamamlanmaTimeScale, 0f, "K5: ertelenen donmada RoundCleared anında dünya hâlâ akar (ağır çekim).");
                float enUzunKare = 0f;
                while (!RunFlowClock.HasHold(RunFlowClock.RoundEndHold) && Time.realtimeSinceStartup - tamamlanmaAni < 3f)
                {
                    yield return null;
                    enUzunKare = Mathf.Max(enUzunKare, Time.unscaledDeltaTime);
                }

                float erteleme = Time.realtimeSinceStartup - tamamlanmaAni;
                Debug.Log("[DD-TEST] wp095.k5 p10 erteleme=" + erteleme.ToString("F3") + "s enUzunKare=" + enUzunKare.ToString("F3"));
                Assert.IsTrue(RunFlowClock.HasHold(RunFlowClock.RoundEndHold), "K5: ertelenen RoundEndHold gelmedi.");
                Assert.LessOrEqual(erteleme, DD.Prototype.Feedback.FeelTimeScale.MaxRoundEndDeferSeconds + enUzunKare + 0.05f,
                    "K5: raund sonu donması RoundCompleted'den ≤ 1,0 s sonra (bir kare payı).");
                Assert.AreEqual(0f, Time.timeScale, 0.0001f, "K5: ertelenen donma geldi, dünya donuk.");
            }
            else
            {
                Assert.AreEqual(0f, tamamlanmaTimeScale, 0.0001f, "RoundCleared anında timeScale 0 (RoundEndHold).");
            }
            Assert.AreEqual(0, tamamlanmaOutstanding, "RoundCleared yayınlanırken yerde bekleyen paket kalmamış olmalı.");
            Assert.GreaterOrEqual(roundOdulToplami, havuz * _standardDeger, "Her kill en az base öder.");
            Assert.AreEqual(roundOdulToplami, tamamlanmaBakiye, "Bakiye = round'un spawn snapshot ödüllerinin toplamı, RoundCleared anında (WP-008).");
            Assert.AreEqual(1, tamamlanmaSweep.Round);
            Assert.AreEqual(_run.RunId, tamamlanmaSweep.RunId);
            Assert.AreEqual(havuz - _gold.PacketsCollected, tamamlanmaSweep.Packets, "Sweep = elle alınmayan paketler (son kill dahil).");
            Assert.AreEqual(_gold.Wallet.EarnedByAutoCollect, tamamlanmaSweep.Amount, "Sweep tutarı = auto-collect ile kredilenen gold.");
            Assert.AreEqual(_gold.Wallet.EarnedByPickup + _gold.Wallet.EarnedByAutoCollect, _gold.Wallet.TotalEarned);
            Assert.AreEqual(roundOdulToplami, _gold.Wallet.TotalEarned);
            Assert.AreEqual(1, sweepOlayi, "Sweep olayı bir kez.");

            // DEC-113 Tur Raporu: RoundCompleted dinleyicisi turun raporunu görür — öldürmeler ve süpürme dahil turun tüm altını.
            Assert.AreEqual(1, tamamlanmaRapor.Round, "Tur Raporu RoundCompleted'dan önce donar.");
            Assert.AreEqual(oldurulen, tamamlanmaRapor.Kills);
            Assert.AreEqual(roundOdulToplami, tamamlanmaRapor.GoldIncome, "Turun altını süpürme dahil.");
            Assert.GreaterOrEqual(tamamlanmaRapor.DamageTaken, 0f);

            // Sweep görseli unscaled: 0.5 s içinde bütün paketler havuzda.
            float t1 = Time.realtimeSinceStartup;
            yield return Bekle(() => _gold.LiveOrbCount == 0, 2f, "Sweep uçuşu donuk dünyada bitmedi.");
            float ucus = Time.realtimeSinceStartup - t1;
            Assert.AreEqual(0, AktifPaketSayisi());

            FeelEvents.GoldCollected -= hisToplama;
            GoldFeelEvents.FlightArrived -= hisSupurmeVaris;
            Assert.AreEqual(havuz, hisZincir.Count, "Her kredi tek GoldCollected: elle " + _gold.PacketsCollected + " + süpürme " + tamamlanmaSweep.Packets + " (WP-098B).");
            Assert.AreEqual(_gold.Wallet.TotalEarned, hisToplamMiktar, "GoldCollected toplamı = kazanılan altın.");
            Assert.AreEqual(tamamlanmaBakiye, hisSonBakiye, "Son GoldCollected bakiyesi = RoundCleared bakiyesi.");
            for (int i = hisZincir.Count - tamamlanmaSweep.Packets + 1; i < hisZincir.Count; i++)
            {
                Assert.AreEqual(hisZincir[i - 1] + 1, hisZincir[i], "Süpürme tek karede: zincir indeksi sikke sikke +1 (" + string.Join(",", hisZincir) + ").");
            }

            Assert.IsTrue(supurmeVaris == 0 || supurmeVaris == tamamlanmaSweep.Packets,
                "HUD varsa sikke başına tek Arrive, yoksa hiç: " + supurmeVaris + "/" + tamamlanmaSweep.Packets);

            // Ekran doğru bakiyeyle açılır (1.1 s beat, unscaled).
            yield return Bekle(() => _run.Phase == RunPhase.Upgrading, FazZamanAsimi, "Upgrade ekranı açılmadı.");
            Assert.IsTrue(_run.Shop.IsOpen);
            Assert.AreEqual(_gold.Wallet.Balance, _run.Shop.Gold, "Ekran bakiyesi cüzdanla aynı.");
            Assert.AreEqual(roundOdulToplami, _run.LastRoundEnd.BalanceAtOpen, "Ekran auto-collect DAHİL bakiyeyle açıldı.");
            Assert.AreEqual(tamamlanmaSweep.Packets, _run.LastRoundEnd.SweptPackets);
            Assert.AreEqual(_run.Shop.SessionId, _run.LastRoundEnd.ShopSessionId);
            Assert.AreEqual(_gold.Wallet.Balance, RunEvents.Current.Gold);
            Assert.IsTrue(RunFlowClock.IsFrozen);
            int sessionR1 = _run.Shop.SessionId;

            // Ekran sırasında spawn yok, hasar yok (60 kare). Yanı başına biletsiz zombi bile.
            int spawnedOnce = _director.Population.SpawnedTotal;
            int hasar = 0;
            Action<PlayerDamageInfo> hasarDinle = i => hasar++;
            PlayerHealth.AnyDamaged += hasarDinle;
            _rig.Health.DiagnosisInvulnerable = false;
            ZombieEnemy komsu = Dogur(OyuncuKonumu + new Vector3(0.9f, 0f, 0f));
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            PlayerHealth.AnyDamaged -= hasarDinle;
            Assert.AreEqual(spawnedOnce, _director.Population.SpawnedTotal, "Upgrade ekranında director spawn üretti.");
            Assert.AreEqual(0, hasar, "Upgrade ekranında oyuncu hasar aldı.");
            Assert.AreEqual(RunPhase.Upgrading, _run.Phase);
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);
            komsu.Remove();
            _rig.Health.DiagnosisInvulnerable = true;

            // TEK satın alma (WP-008 / DEC-048: round başına en fazla bir geliştirme — artık ürün
            // kuralı ve dükkân tarafından uygulanıyor). Kart klasik silahın ya da oyuncunun
            // doğrulanabilir bir PrototypeOnly özelliğini değiştirmeli: teklif rastgeledir, test
            // ilk geçerli kartı alır ve dalın ölçüm değerini (OlcumDegeri) önce/sonra karşılaştırır.
            // (WP-004'teki "dükkânı 40 kez yeniden aç" dikişi kaldırıldı: WP-008'de aynı round'un
            // yeniden açılışı aynı teklifi geri getirir — ücretsiz yeni teklif üretmek ürün
            // kuralına aykırıdır.) Gerçek gold yetmezse etiketsiz kredi ile tamamlanır (test dikişi).
            WeaponController silah = WeaponRegistry.ActiveController;
            Assert.IsNotNull(silah);
            // Sevkte düğüm kartları açık (RunTreeNodeCards): teklif keystone / Kese / Ocak da taşıyabilir ve onlar
            // dal kademesini artırmaz. Bu sözleşme bir DAL KADEMESİ alımını ölçer → yalnız kademe kartı seçilir
            // (FINAL dab326ab A1 triyajı, S-04 01.10: ilk geçerli kart düğümse "Rank tam bir kez arttı" düşüyordu).
            int kartIndeksi = -1;
            for (int i = 0; i < _run.Shop.Cards.Count; i++)
            {
                UpgradeCardOffer c = _run.Shop.Cards[i];
                bool kademe = !c.IsNode || c.NodeKind == RunTreeNodeKind.Tier;
                if (c.IsValid && !c.Purchased && kademe) { kartIndeksi = i; break; }
            }

            Assert.GreaterOrEqual(kartIndeksi, 0, "Teklifte geçerli kademe kartı yok.");
            Assert.AreEqual(3, _run.Shop.Cards.Count, "WP-008: tam üç kart.");
            UpgradeCardOffer kart = _run.Shop.Cards[kartIndeksi];
            if (_gold.Wallet.Balance < kart.Cost)
            {
                Assert.IsTrue(_gold.Wallet.Credit(kart.Cost, OyuncuKonumu));
            }

            int bakiyeOnce = _gold.Wallet.Balance;
            float olcumOnce = OlcumDegeri(kart.Branch, silah);
            float atesAraligiOnce = silah.Classic.ShotInterval;
            float dolumOnce = silah.Classic.EffectiveReloadTime;
            int tierOnce = silah.Modifiers.GetTier(kart.Branch);
            int purchasesOnce = _run.Stats.UpgradesPurchased;
            int xpOnce = _run.Stats.TotalXp;

            Assert.IsTrue(_run.Shop.TryPurchase(kartIndeksi, _run.Shop.SessionId), "Satın alma başarısız: " + _run.Shop.LastResult);
            yield return null;

            float olcumSonra = OlcumDegeri(kart.Branch, silah);
            Assert.AreEqual(bakiyeOnce - kart.Cost, _gold.Wallet.Balance, "Satın alma bakiyeyi maliyet kadar azalttı (tam bir kez).");
            Assert.AreEqual(tierOnce + 1, silah.Modifiers.GetTier(kart.Branch), "Rank tam bir kez arttı.");
            Assert.AreNotEqual(olcumOnce, olcumSonra, kart.Branch + " için doğrulanabilir PrototypeOnly özellik değişmedi (" + olcumOnce + " → " + olcumSonra + ").");
            if (kart.Branch == UpgradeBranch.FireRate)
            {
                Assert.Less(silah.Classic.ShotInterval, atesAraligiOnce, "Fire Rate kartı klasik silahın atış aralığını kısaltmalı.");
            }
            else if (kart.Branch == UpgradeBranch.ReloadSpeed)
            {
                Assert.Less(silah.Classic.EffectiveReloadTime, dolumOnce, "Reload Speed kartı klasik silahın dolum süresini kısaltmalı.");
            }
            else
            {
                Assert.AreEqual(atesAraligiOnce, silah.Classic.ShotInterval, "Silah dışı/başka dal atış aralığına dokunmaz.");
                Assert.AreEqual(dolumOnce, silah.Classic.EffectiveReloadTime, "Silah dışı/başka dal dolum süresine dokunmaz.");
            }

            Assert.AreEqual(purchasesOnce + 1, _run.Stats.UpgradesPurchased);
            Assert.AreEqual(xpOnce, _run.Stats.TotalXp, "Satın alma koşu XP'sine dokunmadı.");
            Assert.AreEqual(1, _run.Build.PurchaseCount);
            Assert.AreEqual(kart.Cost, _run.Build.TotalGoldSpent);
            Assert.AreEqual(_gold.Wallet.Balance, _run.Shop.Gold, "Ekran anında yenilendi (authoritative snapshot).");
            Assert.IsTrue(_run.Shop.Cards[kartIndeksi].Purchased);

            // Çift tıklama aynı kartta reddedilir; WP-008: ikinci kart da reddedilir (round başına tek satın alma).
            int bakiyeCift = _gold.Wallet.Balance;
            Assert.IsFalse(_run.Shop.TryPurchase(kartIndeksi, _run.Shop.SessionId));
            Assert.AreEqual(bakiyeCift, _gold.Wallet.Balance);
            Assert.IsFalse(_run.Shop.TryPurchase((kartIndeksi + 1) % 3, _run.Shop.SessionId));
            Assert.AreEqual(UpgradePurchaseResult.PurchaseLimitReached, _run.Shop.LastResult);
            Assert.AreEqual(bakiyeCift, _gold.Wallet.Balance);
            Assert.IsTrue(_run.Shop.IsLocked);
            Debug.Log("[DD-TEST] wp004.p17 weaponCard=" + kart.Branch + " shotInterval " + atesAraligiOnce.ToString("0.####") + "->" +
                      silah.Classic.ShotInterval.ToString("0.####") + " reload " + dolumOnce.ToString("0.###") + "->" + silah.Classic.EffectiveReloadTime.ToString("0.###"));

            // Sonraki round: gold ve build korunur; eski oturum reddedilir.
            int bakiyeR1 = _gold.Wallet.Balance;
            int tierR1 = silah.Modifiers.GetTier(kart.Branch);
            string buildR1 = _run.Build.DescribeBuild();
            _run.CloseUpgradeScreen();
            yield return Bekle(() => _run.Phase == RunPhase.Fighting && _run.Round == 2, FazZamanAsimi, "Round 2 başlamadı.");
            yield return SimulasyonSerbestOlmali("wp004.r2");

            Assert.AreEqual(bakiyeR1, _gold.Wallet.Balance, "Kalan gold sonraki round'a taşındı.");
            Assert.AreEqual(tierR1, silah.Modifiers.GetTier(kart.Branch), "Geliştirme sonraki round'da korundu.");
            Assert.AreEqual(buildR1, _run.Build.DescribeBuild());
            Assert.AreEqual(1, _run.Build.PurchaseCount, "Satın alma kaydı sonraki round'da korundu.");
            Assert.AreEqual(_run.RunId, _gold.Wallet.RunId);
            Assert.AreEqual(0, _gold.LiveOrbCount, "Round 2 başında yerde eski paket yok.");

            // Eski ekranın gecikmiş callback'i: sessionR1 ile satın alma → RED (dükkân kapalı).
            Assert.IsFalse(_run.Shop.IsOpen);
            Assert.IsFalse(_run.Shop.TryPurchase(0, sessionR1));
            Assert.AreEqual(bakiyeR1, _gold.Wallet.Balance, "Eski oturum callback'i gold harcamadı.");

            Debug.Log("[DD-TEST] wp004.p10p19 pool=" + havuz + " swept=" + tamamlanmaSweep.Packets + "/" + tamamlanmaSweep.Amount +
                      "g manual=" + _gold.PacketsCollected + " balanceAtOpen=" + _run.LastRoundEnd.BalanceAtOpen +
                      " flight=" + ucus.ToString("0.00") + "s card=" + kart.Branch + " cost=" + kart.Cost +
                      " measure " + olcumOnce.ToString("0.###") + "->" + olcumSonra.ToString("0.###") + " r2Balance=" + _gold.Wallet.Balance);
        }

        /// <summary>Bir dalın klasik silah/oyuncu üzerindeki doğrulanabilir (PrototypeOnly) özelliği.</summary>
        private float OlcumDegeri(UpgradeBranch dal, WeaponController silah)
        {
            switch (dal)
            {
                case UpgradeBranch.FireRate: return silah.Classic.EffectiveFireRate;
                case UpgradeBranch.ReloadSpeed: return silah.Classic.EffectiveReloadTime;
                case UpgradeBranch.Piercing: return silah.Modifiers.PierceTargets;
                case UpgradeBranch.Ricochet: return silah.Modifiers.RicochetChance + silah.Modifiers.RicochetBounces;
                case UpgradeBranch.Explosion: return silah.Modifiers.ExplosionChance + silah.Modifiers.ExplosionRadius;
                case UpgradeBranch.Chain: return silah.Modifiers.ChainTargets;
                case UpgradeBranch.MovementSpeed: return _rig.Motor.MaxSpeed;
                case UpgradeBranch.MaximumHealth: return _rig.Health.Max;
                case UpgradeBranch.RegenerationDelay: return _rig.Health.RegenDelaySeconds;
                case UpgradeBranch.RegenerationRate: return _rig.Health.RegenRatePerSecond;
                default: return silah.Modifiers.TotalTiers;
            }
        }

        // ============================================================== P20-P22 · ölüm, yeni koşu, duplicate yok, abort

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator P20_P22_Olum_GoldVeBuildSifir_YeniKosuTemiz_DuplicateYok()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();

            int xpOnce = ProfileService.Current.TotalXpEarned;

            // ------------------------------------------------------------ Koşu 1
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector run1 = RunDirector.Instance;
            GoldService gold1 = GoldService.Instance;
            PlayerRig rig1 = PlayerRegistry.Primary;
            WeaponController silah1 = WeaponRegistry.ActiveController;
            int runId1 = run1.RunId;
            Dictionary<string, int> abone1 = AboneAnlikGoruntu();
            int walletChanged1 = OrnekAboneSayisi(gold1.Wallet, "Changed");

            Assert.AreEqual(1, AktifSayi<GoldService>());
            Assert.AreEqual(1, AktifSayi<UpgradeScreen>());
            Assert.AreEqual(1, AktifSayi<RunDirector>());

            // Kirlet: paketler yerde, gold cüzdanda, build satın alınmış.
            SpawnAyarla(false);
            EnemySpawnDirector.Instance.ClearAllEnemies();
            yield return null;
            for (int i = 0; i < 3; i++)
            {
                EnemyFactory.Spawn(EnemyTier.Standard, 1, rig1.transform.position + new Vector3(10f + i, 0f, 0f), Quaternion.identity);
            }

            Assert.AreEqual(3, TumYasayanlariOldur());
            Assert.AreEqual(3, gold1.LiveOrbCount);
            Assert.IsTrue(gold1.Wallet.Credit(40, rig1.transform.position));
            silah1.Modifiers.AddTier(UpgradeBranch.FireRate, 1, PrototypeConfig.Active);
            int standardDeger = PrototypeConfig.Active.GoldReward(EnemyTier.Standard);
            Assert.AreEqual(40, gold1.Wallet.Balance);
            Assert.AreEqual(1, silah1.Modifiers.GetTier(UpgradeBranch.FireRate));

            // Ölüm: özet ölüm ÖNCESİ gold'u taşır; sonra cüzdan/paket/ledger sıfırlanır.
            RunSummary ozet = default;
            int bitis = 0;
            Action<RunSummary> bitisDinle = s => { ozet = s; bitis++; };
            RunEvents.RunEnded += bitisDinle;
            rig1.Health.ApplyDamage(rig1.Health.Max * 10f, rig1.transform.position + Vector3.forward, "wp-004-test");
            yield return Bekle(() => run1.Phase == RunPhase.Dead, FazZamanAsimi, "Ölüm Dead fazına geçmedi.");
            RunEvents.RunEnded -= bitisDinle;

            Assert.AreEqual(1, bitis);
            Assert.AreEqual(40, ozet.GoldUnspent, "Özet ölüm öncesi bakiyeyi taşır.");
            Assert.AreEqual(0, gold1.Wallet.Balance, "Ölüm cüzdanı sıfırladı.");
            Assert.IsFalse(gold1.Wallet.IsOpen);
            Assert.AreEqual(0, gold1.LiveOrbCount, "Ölümde yerdeki paketler kredisiz temizlendi.");
            Assert.AreEqual(3, gold1.PacketsLost, "Üç paket gold'uyla birlikte yok oldu.");
            Assert.IsFalse(gold1.Ledger.IsRunLive, "Ölümden sonra hak ediş kapalı.");
            Assert.IsFalse(run1.Build.IsRunLive);

            // Ölüm sonrası gecikmiş kill: paket/kredi yok (ledger kapalı, cüzdan kapalı).
            ZombieEnemy gec = EnemyFactory.Spawn(EnemyTier.Standard, 1, rig1.transform.position + new Vector3(6f, 0f, 0f), Quaternion.identity);
            int paketOnce = gold1.PacketsSpawned;
            gec.Kill();
            Assert.AreEqual(paketOnce, gold1.PacketsSpawned, "Koşu bittikten sonra ölüm paket üretmedi.");
            Assert.AreEqual(0, gold1.Wallet.Balance);
            Assert.Greater(gold1.Ledger.RejectedRunNotLive, 0);

            yield return SahneyiBekle(PrototypeScenes.Results);
            Assert.AreEqual(xpOnce + ozet.TotalXpEarned, ProfileService.Current.TotalXpEarned,
                "Profil XP yalnız koşu özetinin XP'si kadar arttı; gold hiçbir katkı yapmadı.");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Assert.IsNull(GoldService.Instance, "Ana menüde GoldService kalmamalı.");
            Assert.AreEqual(0, AktifSayi<GoldOrb>(), "Ana menüde aktif paket kalmamalı.");

            // ------------------------------------------------------------ Koşu 2
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();

            RunDirector run2 = RunDirector.Instance;
            GoldService gold2 = GoldService.Instance;
            WeaponController silah2 = WeaponRegistry.ActiveController;

            Assert.AreEqual(1, AktifSayi<GoldService>(), "İkinci koşuda birden fazla GoldService.");
            Assert.AreEqual(1, AktifSayi<UpgradeScreen>(), "İkinci koşuda birden fazla UpgradeScreen.");
            Assert.AreEqual(1, AktifSayi<RunDirector>());
            Assert.AreEqual(0, gold2.Wallet.Balance, "Yeni koşu temiz cüzdan.");
            Assert.IsTrue(gold2.Wallet.IsOpen);
            Assert.AreEqual(0, gold2.LiveOrbCount, "Yeni koşu temiz saha.");
            Assert.AreEqual(0, silah2.Modifiers.TotalTiers, "Yeni koşu temiz build.");
            Assert.AreEqual("Stock", run2.Build.DescribeBuild());
            Assert.AreEqual(0, gold2.PacketsSpawned);
            Assert.AreEqual(0, gold2.Ledger.Granted);
            Assert.Greater(gold2.Ledger.LifeWatermark, 0, "Yeni koşunun bayat-yaşam sınırı öncekilerin üstünde.");
            Assert.Greater(run2.RunId, runId1, "Koşu kimliği oturum boyunca artar; ikinci koşu birincinin kimliğini taşımaz.");
            Assert.AreEqual(run2.RunId, gold2.Wallet.RunId);

            Dictionary<string, int> abone2 = AboneAnlikGoruntu();
            AboneleriKarsilastir("wp004 run2 vs run1 statik aboneler", abone1, abone2);
            Assert.AreEqual(walletChanged1, OrnekAboneSayisi(gold2.Wallet, "Changed"),
                "İkinci koşuda cüzdan Changed abone sayısı değişti (duplicate pickup/upgrade dinleyici).");

            // Yeni koşuda gold normal işler (ilk paket yeni koşunun yaşamıyla).
            SpawnAyarla(false);
            EnemySpawnDirector.Instance.ClearAllEnemies();
            yield return null;
            ZombieEnemy z2 = EnemyFactory.Spawn(EnemyTier.Standard, 1, PlayerRegistry.Primary.transform.position + new Vector3(10f, 0f, 0f), Quaternion.identity);
            z2.FitArmorComposition(false, false, false);   // WP-008: bare Standard = flat value (test seam)
            z2.Kill();
            Assert.AreEqual(1, gold2.PacketsSpawned, "Yeni koşuda kill yeni paket üretir.");
            Assert.AreEqual(1, gold2.Ledger.Granted);
            Assert.AreEqual(0, gold2.Ledger.RejectedStale);

            // İkinci koşunun round-1 sweep'i ilk koşunun (run1, round1) anahtarıyla karışmaz:
            // aynı round numarası, farklı koşu → sweep çalışır ve paketi kredilendirir.
            GoldAutoCollectReport sweep2 = gold2.CollectOutstanding(1);
            Assert.AreEqual(run2.RunId, sweep2.RunId);
            Assert.AreEqual(1, sweep2.Packets, "Koşu 2 round 1 sweep'i koşu 1'in round 1 sweep'i tarafından engellenmedi.");
            Assert.AreEqual(standardDeger, sweep2.Amount);
            Assert.AreEqual(standardDeger, gold2.Wallet.Balance);
            Assert.AreEqual(0, gold2.CollectOutstanding(1).Packets, "Aynı (koşu, round) ikinci sweep 0 toplar.");

            Debug.Log("[DD-TEST] wp004.p20p22 run1=" + runId1 + " lost=" + gold1.PacketsLost + " summaryGold=" + ozet.GoldUnspent +
                      " run2Balance=" + gold2.Wallet.Balance + " run2Tiers=" + silah2.Modifiers.TotalTiers +
                      " walletChangedSubs " + walletChanged1 + "==" + OrnekAboneSayisi(gold2.Wallet, "Changed"));

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>Bir örnek olayının abone sayısı (backing field reflection). Duplicate dinleyici tespiti.</summary>
        private static int OrnekAboneSayisi(object hedef, string olay)
        {
            FieldInfo f = hedef.GetType().GetField(olay, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return -1;
            var d = f.GetValue(hedef) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        // ============================================================== performans gözlemi

        /// <summary>
        /// GÖZLEM, KAPI DEĞİL (onaylı performans bütçesi yok). Gerçek kare, captureDeltaTime 0.
        /// Ölçülen: (a) 30 kill'in aynı karede paket spawn maliyeti (ms, GC B — havuz ısınmış),
        /// (b) 30 yerdeki paketle 120 kare CPU (kare ms p50/p95, GC B/kare), (c) 30 paketin
        /// round-clear sweep'i tek karede (ms, GC B), (d) 100 cüzdan kredisi (GC B),
        /// (e) DescribeBuild/snapshot 1000 çağrı (GC B — önbellek), (f) upgrade ekranının
        /// açıldığı karenin süresi, (g) ikinci koşuya heap farkı. Tek assert: ölçüm yapıldı.
        /// </summary>
        [UnityTest]
        [Category("Perf")]
        [Timeout(300000)]
        public IEnumerator PerformansGozlemi_30Paket_Sweep_Cuzdan_Build()
        {
            yield return RunSahnesiniHazirla(spawnKapali: true);
            Time.captureDeltaTime = 0f;
            _rig.Health.DiagnosisInvulnerable = true;

            var gcAlloc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long mono0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var sw = new System.Diagnostics.Stopwatch();

            try
            {
                // Isınma: bir kill + sweep (havuz ve JIT).
                Dogur(OyuncuKonumu + new Vector3(12f, 0f, 0f)).Kill();
                yield return null;
                _gold.ClearOrbs(true);
                yield return Bekle(() => ZombieEnemy.Live.Count == 0 && _gold.LiveOrbCount == 0, 10f, "Isınma temizlenmedi.");
                for (int i = 0; i < 30; i++) yield return null;

                // (b0) Taban: 0 paket, 0 zombi, 120 kare (aynı sahne, aynı kurulum).
                var tabanMs = new List<float>(128);
                long gcTaban = 0;
                float onceki0 = Time.realtimeSinceStartup;
                for (int i = 0; i < 120; i++)
                {
                    yield return null;
                    float simdi = Time.realtimeSinceStartup;
                    tabanMs.Add((simdi - onceki0) * 1000f);
                    onceki0 = simdi;
                    if (gcAlloc.Valid) gcTaban += gcAlloc.LastValue;
                }

                tabanMs.Sort();
                float tabanP50 = tabanMs[tabanMs.Count / 2];

                // (a) 30 kill aynı karede.
                var zombiler = new List<ZombieEnemy>(30);
                for (int i = 0; i < 30; i++)
                {
                    float aci = i * Mathf.PI * 2f / 30f;
                    float r = 9f + (i % 3) * 2f;
                    zombiler.Add(Dogur(OyuncuKonumu + new Vector3(Mathf.Cos(aci) * r, 0f, Mathf.Sin(aci) * r)));
                }

                yield return null;
                long allocKillOnce = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                for (int i = 0; i < zombiler.Count; i++)
                {
                    zombiler[i].Kill();
                }

                sw.Stop();
                long allocKill = GC.GetAllocatedBytesForCurrentThread() - allocKillOnce;
                double msKill = sw.Elapsed.TotalMilliseconds;
                Assert.AreEqual(30, _gold.LiveOrbCount);
                yield return null;
                long gcKillFrame = gcAlloc.Valid ? gcAlloc.LastValue : -1;

                // Cesetler havuza dönsün: ölçüm yalnız yerdeki 30 paketi görsün.
                yield return Bekle(() => ZombieEnemy.Live.Count == 0, 10f, "Cesetler havuza dönmedi.");
                Assert.AreEqual(30, _gold.LiveOrbCount, "30 paket hâlâ yerde olmalı.");
                yield return null;

                // (b) 30 paket yerde, 0 zombi, 120 kare.
                var kareMs = new List<float>(128);
                long gcToplam = 0;
                float onceki = Time.realtimeSinceStartup;
                for (int i = 0; i < 120; i++)
                {
                    yield return null;
                    float simdi = Time.realtimeSinceStartup;
                    kareMs.Add((simdi - onceki) * 1000f);
                    onceki = simdi;
                    if (gcAlloc.Valid) gcToplam += gcAlloc.LastValue;
                }

                kareMs.Sort();
                float p50 = kareMs[kareMs.Count / 2];
                float p95 = kareMs[Mathf.Min(kareMs.Count - 1, Mathf.RoundToInt(kareMs.Count * 0.95f))];

                // (c) Sweep 30 paket, tek çağrı (RoundCleared yolunun aynısı: CollectOutstanding).
                long allocSweepOnce = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                GoldAutoCollectReport rapor = _gold.CollectOutstanding(_run.Round);
                sw.Stop();
                long allocSweep = GC.GetAllocatedBytesForCurrentThread() - allocSweepOnce;
                double msSweep = sw.Elapsed.TotalMilliseconds;
                Assert.AreEqual(30, rapor.Packets);
                yield return Bekle(() => _gold.LiveOrbCount == 0, 3f, "Sweep uçuşu bitmedi.");

                // (d) 100 cüzdan kredisi.
                GoldWallet cuzdan = _gold.Wallet;
                long allocCreditOnce = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                for (int i = 0; i < 100; i++)
                {
                    cuzdan.Credit(1, GoldSource.EnemyPickup, cuzdan.NextTransactionId(), OyuncuKonumu);
                }

                sw.Stop();
                long allocCredit = GC.GetAllocatedBytesForCurrentThread() - allocCreditOnce;
                double msCredit = sw.Elapsed.TotalMilliseconds;

                // (e) DescribeBuild + snapshot 1000 çağrı (build değişmedi → 0 B beklenir).
                WeaponController silah = WeaponRegistry.ActiveController;
                string d0 = _run.Build.DescribeBuild();
                string w0 = silah.Modifiers.DescribeBuild();
                long allocBuildOnce = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++)
                {
                    string d = _run.Build.DescribeBuild();
                    string w = silah.Modifiers.DescribeBuild();
                    RunBuildSnapshot snap = _run.Build.Snapshot;
                    if (!ReferenceEquals(d, d0) || !ReferenceEquals(w, w0) || snap.Version < 0) { Assert.Fail("Build açıklaması önbelleklenmedi."); }
                }

                long allocBuild = GC.GetAllocatedBytesForCurrentThread() - allocBuildOnce;

                // (f) Upgrade ekranı açılış karesi.
                _run.OpenUpgradeScreen();
                float tOpen = Time.realtimeSinceStartup;
                yield return null;
                float msOpen = (Time.realtimeSinceStartup - tOpen) * 1000f;
                long gcOpen = gcAlloc.Valid ? gcAlloc.LastValue : -1;
                Assert.AreEqual(RunPhase.Upgrading, _run.Phase);
                yield return null;
                float tIdle = Time.realtimeSinceStartup;
                yield return null;
                float msUpgradingIdle = (Time.realtimeSinceStartup - tIdle) * 1000f;
                long gcUpgradingIdle = gcAlloc.Valid ? gcAlloc.LastValue : -1;

                long mono1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();

                Debug.Log("[DD-TEST] wp004.perf killx30 ms=" + msKill.ToString("0.000") + " allocB=" + allocKill + " frameGC=" + gcKillFrame +
                          " | idle0pk p50=" + tabanP50.ToString("0.00") + "ms gcB/frame=" + (gcTaban / 120) +
                          " | idle30pk frames=120 p50=" + p50.ToString("0.00") + "ms p95=" + p95.ToString("0.00") + "ms gcB/frame=" + (gcToplam / 120) +
                          " | sweep30 ms=" + msSweep.ToString("0.000") + " allocB=" + allocSweep +
                          " | credit100 ms=" + msCredit.ToString("0.000") + " allocB=" + allocCredit +
                          " | describe1000 allocB=" + allocBuild +
                          " | upgradeOpenFrame ms=" + msOpen.ToString("0.00") + " gcB=" + gcOpen + " upgradingIdle ms=" + msUpgradingIdle.ToString("0.00") + " gcB=" + gcUpgradingIdle +
                          " | monoHeap " + (mono0 / 1024) + "KB -> " + (mono1 / 1024) + "KB pool=" + _gold.PacketsSpawned + " spawned lit? gfx=" + SystemInfo.graphicsDeviceType);

                Assert.Greater(kareMs.Count, 0);
                Assert.IsFalse(float.IsNaN(p50) || float.IsInfinity(p50));
            }
            finally
            {
                gcAlloc.Dispose();
                _rig.Health.DiagnosisInvulnerable = false;
                _run.CloseUpgradeScreen();
            }
        }

        // ============================================================== abort: upgrade açılmaz

        [UnityTest]
        [Category("WP004")]
        [Timeout(300000)]
        public IEnumerator PA_OlumIleRoundBitisiYarisi_OlumKazanir_UpgradeAcilmaz()
        {
            yield return RunSahnesiniHazirla(spawnKapali: false);

            RunFlowClock.ClearHolds();
            _run.BeginRound(1);
            yield return null;
            yield return Bekle(() => ZombieEnemy.Live.Count > 0, 12f, "Director zombi doğurmadı.");

            // Aynı karede: oyuncu ölür, SONRA son düşmanlar ölür. Ölüm terminaldir.
            int fazUpgrading = 0;
            Action<RunPhase> fazDinle = p => { if (p == RunPhase.Upgrading || p == RunPhase.RoundComplete) fazUpgrading++; };
            RunEvents.PhaseChanged += fazDinle;

            _rig.Health.ApplyDamage(_rig.Health.Max * 10f, _rig.transform.position + Vector3.forward, "wp-004-race");
            int oldurulen = TumYasayanlariOldur();
            Assert.AreEqual(RunPhase.Dead, _run.Phase);
            Assert.AreEqual(RoundState.Aborted, _director.State, "Ölüm round'u Aborted yapar.");

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            RunEvents.PhaseChanged -= fazDinle;
            Assert.AreEqual(RunPhase.Dead, _run.Phase, "Ölüm sonrası faz Dead kalır.");
            Assert.AreEqual(0, fazUpgrading, "Aborted koşuda RoundComplete/Upgrading açılmadı.");
            Assert.IsFalse(_run.Shop.IsOpen);
            Assert.AreEqual(0, _gold.Wallet.Balance, "Ölümle gold yok oldu.");
            Assert.AreEqual(0, _gold.LiveOrbCount);
            Assert.IsFalse(_gold.Ledger.IsRunLive);

            Debug.Log("[DD-TEST] wp004.race killed=" + oldurulen + " phase=" + _run.Phase + " ledger=" + _director.State);
            yield return AkisiYatistir();
        }

        // ============================================================== WP-095 K1 / K5 · son zombi, ertelenen raund sonu donması

        private bool _k5Tamamlandi;
        private bool _k5Ertelendi;
        private float _k5Ani;

        /// <summary>
        /// A real round 1 (director spawn, real ledger), top-down, killed to its last zombie; returns in the frame after
        /// RoundCompleted with the deferral and the moment recorded. The slow motion and the stop must be switched on.
        /// </summary>
        private IEnumerator K5_SonZombiyeKadarOldur()
        {
            yield return RunSahnesiniHazirla(spawnKapali: false);
            _rig.Health.DiagnosisInvulnerable = true;   // ölçüm oyuncu ölümüyle kesilmesin (tanı)
            Assert.AreEqual(DD.Prototype.CameraSystem.CameraMode.TopDown, DD.Prototype.CameraSystem.CameraModeService.Current, "K1/K5 yalnız tepeden.");
            DD.Prototype.Feedback.FeelProfileTD profil = DD.Prototype.Feedback.FeelProfileTD.Active;
            Assume.That(profil.lastZombieSlowMo, "FeelProfile_TD.lastZombieSlowMo kapalı: K5 ertelemesi yok.");
            DD.Prototype.Feedback.FeelHitStopTD.Rearm();

            RunFlowClock.ClearHolds();
            _run.BeginRound(1);
            yield return null;
            Assert.AreEqual(RunPhase.Fighting, _run.Phase);

            int sonZombiOnce = DD.Prototype.Feedback.FeelEvents.Count(DD.Prototype.Feedback.FeelEventKind.LastZombie);
            int agirCekimOnce = DD.Prototype.Feedback.FeelTimeScale.LastZombies;
            _k5Tamamlandi = false;
            _k5Ertelendi = false;
            Action<int> tamam = r =>
            {
                _k5Tamamlandi = true;
                _k5Ertelendi = _run.RoundEndHoldDeferred;
                _k5Ani = Time.realtimeSinceStartup;
            };
            RunEvents.RoundCompleted += tamam;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (!_k5Tamamlandi && Time.realtimeSinceStartup - t0 < 60f)
                {
                    TumYasayanlariOldur();
                    yield return null;
                }
            }
            finally
            {
                RunEvents.RoundCompleted -= tamam;
            }

            Assert.IsTrue(_k5Tamamlandi, "Raund tamamlanmadı.");
            Assert.Greater(DD.Prototype.Feedback.FeelEvents.Count(DD.Prototype.Feedback.FeelEventKind.LastZombie), sonZombiOnce,
                "Son zombi anı (FeelEvents.LastZombie) yayınlanmadı.");
            Assert.AreEqual(agirCekimOnce + 1, DD.Prototype.Feedback.FeelTimeScale.LastZombies, "Ağır çekim başlamadı (tek kez).");
            Assert.IsTrue(_k5Ertelendi, "K5: TD + ağır çekimde RoundEndHold RoundCompleted anında alınmamalı.");
        }

        /// <summary>
        /// WP-095 (C-00 K1 / K5 08.10; his sözleşmesi §4.11, §9 S4): the round's last zombie dies in a real round → the world goes
        /// into the slow motion (≤ lastZombieTimeScale), the round-end freeze waits for the moment and lands ≤ 1.0 s after
        /// RoundCompleted, and when the upgrade screen closes the clock is 1 and the arbiter idle.
        /// </summary>
        [UnityTest]
        [Category("WP095")]
        [Timeout(300000)]
        public IEnumerator Wp095_SonZombi_AgirCekim_DonmaBirSaniyeIcinde_SondaBir()
        {
            yield return K5_SonZombiyeKadarOldur();

            float enDusuk = Time.timeScale;
            float enUzunKare = 0f;
            while (!RunFlowClock.HasHold(RunFlowClock.RoundEndHold) && Time.realtimeSinceStartup - _k5Ani < 3f)
            {
                enDusuk = Mathf.Min(enDusuk, Time.timeScale);
                yield return null;
                enUzunKare = Mathf.Max(enUzunKare, Time.unscaledDeltaTime);
            }

            float erteleme = Time.realtimeSinceStartup - _k5Ani;
            Debug.Log("[DD-TEST] wp095.k5 erteleme=" + erteleme.ToString("F3") + "s enDusukOlcek=" + enDusuk.ToString("F3") +
                      " enUzunKare=" + enUzunKare.ToString("F3") + " durma=" + DD.Prototype.Feedback.FeelTimeScale.Stops +
                      " stoppedSec=" + DD.Prototype.Feedback.FeelTimeScale.StoppedSeconds.ToString("F3"));
            Assert.IsTrue(RunFlowClock.HasHold(RunFlowClock.RoundEndHold), "K5: ertelenen raund sonu donması gelmedi.");
            Assert.LessOrEqual(erteleme, DD.Prototype.Feedback.FeelTimeScale.MaxRoundEndDeferSeconds + enUzunKare + 0.05f,
                "K5: donma RoundCompleted'den ≤ 1,0 s sonra (bir kare payı).");
            Assert.LessOrEqual(enDusuk, DD.Prototype.Feedback.FeelProfileTD.Active.lastZombieTimeScale + 0.01f,
                "Ağır çekim olmadı (en düşük ölçek " + enDusuk.ToString("F3") + ").");
            Assert.IsFalse(DD.Prototype.Feedback.FeelTimeScale.LastZombieActive, "Donma geldiğinde son zombi anı bitmiş olmalı.");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Raund sonu donması: dünya donuk.");

            yield return Bekle(() => _run.Phase == RunPhase.Upgrading, FazZamanAsimi, "Upgrade ekranı açılmadı.");
            _run.CloseUpgradeScreen();
            yield return null;
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Sonunda timeScale 1.");
            Assert.IsFalse(DD.Prototype.Feedback.FeelTimeScale.HasWork, "Saat hakemi boşta.");
            _rig.Health.DiagnosisInvulnerable = false;
        }

        /// <summary>
        /// WP-095 (C-00 K1, his sözleşmesi §9 S4): pause in the last zombie's slow motion → the world stops (the pause always
        /// wins), the envelope and the K5 deferral wait (a pause does not count towards the 1.0 s safety); resume → the slow
        /// motion goes on where it stopped, the round-end freeze follows when it ends, and the clock is 1 at the end.
        /// </summary>
        [UnityTest]
        [Category("WP095")]
        [Timeout(300000)]
        public IEnumerator Wp095_SonZombiAgirCekimdeDuraklat_Surdur_ZarfTamamlanir()
        {
            yield return K5_SonZombiyeKadarOldur();
            Assert.IsTrue(DD.Prototype.Feedback.FeelTimeScale.LastZombieActive, "Ağır çekim RoundCompleted'in ardından sürüyor olmalı.");

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.2f && DD.Prototype.Feedback.FeelTimeScale.LastZombieActive)
            {
                yield return null;
            }

            Assert.IsTrue(DD.Prototype.Feedback.FeelTimeScale.LastZombieActive, "Ağır çekim 0,2 s içinde bitmemeli.");
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            float gecen = DD.Prototype.Feedback.FeelTimeScale.LastZombieElapsed;
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Duraklatma kazanır.");
            Assert.IsTrue(DD.Prototype.Feedback.FeelTimeScale.LastZombieActive, "TD: duraklatma zarfı askıya alır (K1).");

            float p0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - p0 < 1.5f)
            {
                yield return null;   // longer than the whole envelope and the K5 safety
            }

            Assert.AreEqual(gecen, DD.Prototype.Feedback.FeelTimeScale.LastZombieElapsed, 1e-5f, "Donukken zarf yaşlanmaz.");
            Assert.IsTrue(DD.Prototype.Feedback.FeelTimeScale.LastZombieActive);
            Assert.IsTrue(_run.RoundEndHoldDeferred, "Duraklatma ertelemeyi tüketmez (K5 emniyeti çalışan zamanı sayar).");
            Assert.IsFalse(RunFlowClock.HasHold(RunFlowClock.RoundEndHold));
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);

            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return null;
            yield return null;
            Assert.Less(Time.timeScale, 0.999f, "Sürdürünce ağır çekim kaldığı yerden (ölçek " + Time.timeScale.ToString("F3") + ").");
            Assert.Greater(DD.Prototype.Feedback.FeelTimeScale.LastZombieElapsed, gecen, "Zarf sürüyor.");

            float r0 = Time.realtimeSinceStartup;
            while (!RunFlowClock.HasHold(RunFlowClock.RoundEndHold) && Time.realtimeSinceStartup - r0 < 3f)
            {
                yield return null;
            }

            Assert.IsTrue(RunFlowClock.HasHold(RunFlowClock.RoundEndHold), "Zarf bitince raund sonu donması gelmedi.");
            Assert.IsFalse(DD.Prototype.Feedback.FeelTimeScale.LastZombieActive, "Zarf tamamlandı.");
            Assert.AreEqual(0f, Time.timeScale, 0.0001f);

            yield return Bekle(() => _run.Phase == RunPhase.Upgrading, FazZamanAsimi, "Upgrade ekranı açılmadı.");
            _run.CloseUpgradeScreen();
            yield return null;
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Sonunda timeScale 1.");
            Assert.IsFalse(DD.Prototype.Feedback.FeelTimeScale.HasWork);
            _rig.Health.DiagnosisInvulnerable = false;
        }
    }
}
