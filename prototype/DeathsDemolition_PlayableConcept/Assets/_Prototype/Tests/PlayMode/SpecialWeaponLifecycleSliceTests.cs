using System.Collections;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · Special Weapon yaşam döngüsü dilimi — Run sahnesinde, gerçek director/pickup/
    /// controller zinciriyle. DEC-024/025/026 · REQ-052/053/055/056/057/058 · FLOW-005
    /// (AP-3, AP-5, AP-6, AP-7, AP-8, AP-9 · TM-7, 9, 10, 12, 19–26, 28–31, 33, 35, 36).
    ///
    /// Spawn kararı config kopyasına (ihtimal 1, tek noktalı pencere) ve köprü sağlayıcısına
    /// bağlanır; süreler config'ten okunur, adımlar süre/N formülüyle türetilir. Sayılar
    /// PrototypeOnly'dir, ürün kararı değildir.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponLifecycleSliceTests
    {
        /// <summary>Test parametreleri (ürün sayısı değil): mark, fast-forward bölme sayısı, kare sayısı.</summary>
        private const float Pencere = 0.5f;
        private const int Bolme = 20;
        private const int Kare = 12;
        private const float KareAdimi = 0.05f;

        /// <summary>Fast-forward adımı: süre/N ama en fazla KareAdimi — oyuncu animatörünün açık Euler yayı büyük adımda patlar (test parametresi).</summary>
        private static float Adim(float sure) => Mathf.Min(sure / Bolme, KareAdimi);

        private SpecialWeaponSahne _s;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        // ============================================================ TM-25 · REQ-053

        /// <summary>[TM-25] Spawn başına belirgin ses, ışık sütunu (beacon loop) ve HUD işareti tam bir kez başlar.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Spawn_SesBeaconVeHudIsareti_TamBirKez()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                yield return SpecialWeaponSahne.Kareler(Kare);

                Assert.AreEqual(1, sayac.Spawned, "SpecialWeaponSpawned tam bir kez.");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponSpawn), "Spawn sesi tam bir kez.");
                Assert.AreEqual(1, sayac.LoopBaslatSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop), "Beacon loop tam bir kez başlar.");
                Assert.AreEqual(1, sayac.ChangedOnGround, "HUD OnGround bildirimi tam bir kez.");
                Assert.AreEqual(SpecialWeaponPhase.OnGround, WeaponEvents.Special.Phase);
                Assert.AreEqual(1, SpecialWeaponSahne.AktifPickupSayisi(), "Yerde tam bir pickup.");
                Assert.AreEqual(1, _s.Director.SpawnsThisRound);
                Assert.AreEqual(_s.CanliPickup().transform.position, WeaponEvents.Special.WorldPosition, "HUD işareti pickup konumunu göstermeli.");
            }
        }

        // ============================================================ TM-19 / TM-20 · REQ-055

        /// <summary>[TM-19][TM-20] Yerde bekleme timer'ı spawn'la başlar; aktif timer yerdeyken ilerlemez.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator YerdeTimer_SpawnlaBaslar_AktifTimerYerdeykenIlerlemez()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup);

            Assert.AreEqual(_s.Special.GroundLifetime, pickup.Duration, 0.0001f, "Yerde bekleme süresi config'ten gelmedi.");
            Assert.LessOrEqual(pickup.Duration - pickup.Remaining, 2f * Time.deltaTime + 0.0001f, "Ground timer spawn'dan önce işlemiş.");

            float adim = Adim(_s.Special.GroundLifetime);
            Time.captureDeltaTime = adim;
            float once = pickup.Remaining;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Time.captureDeltaTime = 0f;

            Assert.AreEqual(Kare * adim, once - pickup.Remaining, adim * 0.01f, "Ground timer kare başına dt kadar ilerlemeli.");
            Assert.IsFalse(_s.Controller.Special.IsActive, "Aktif runtime yerdeyken açık.");
            Assert.AreEqual(0f, _s.Controller.Special.Remaining, 0.0001f, "Aktif timer yerdeyken ilerledi (REQ-055).");
            Assert.AreEqual(0f, WeaponEvents.Special.ActiveRemaining, 0.0001f);
        }

        // ============================================================ TM-21 · AP-5 pause

        /// <summary>[TM-21] Pause sırasında ground timer delta üretmez — pozitif kontrollü (hold yokken ilerler).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Pause_YerdeTimerDurur_PozitifKontrollu()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();

            float adim = Adim(_s.Special.GroundLifetime);
            Time.captureDeltaTime = adim;

            // (i) pozitif kontrol: ölçüm canlı
            float once = pickup.Remaining;
            float faz0 = pickup.BlinkPhase;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Assert.Greater(once - pickup.Remaining, 0f, "Pozitif kontrol: hold yokken timer ilerlemedi — ölçüm kırık.");
            Assert.AreNotEqual(faz0, pickup.BlinkPhase, "Pozitif kontrol: blink fazı ilerlemedi.");

            // (ii) hold: ölçekli saat durur
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, "Pause hold timeScale'i sıfırlamadı.");
            float dondu = pickup.Remaining;
            float faz1 = pickup.BlinkPhase;
            for (int i = 0; i < Kare; i++)
            {
                yield return null;
                Assert.AreEqual(0f, Time.deltaTime, "Pause'da deltaTime 0 değil.");
                Assert.AreEqual(dondu, pickup.Remaining, "Ground timer pause'da ilerledi (AP-5).");
            }

            Assert.AreEqual(faz1, pickup.BlinkPhase, "Blink fazı pause'da ilerledi.");

            // (iii) serbest: yeniden ilerler
            RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            yield return null;
            float serbest = pickup.Remaining;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Time.captureDeltaTime = 0f;
            Assert.Greater(serbest - pickup.Remaining, 0f, "Hold kalkınca timer ilerlemedi.");
        }

        // ============================================================ TM-22 · AP-5 expire

        /// <summary>[TM-22] Yerde süre dolunca pickup bir kez temizlenir: olay 1, ses 1, loop stop 1, HUD None, director Idle.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator YerdeSureDolunca_TekTemizlik()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                SpecialWeaponPickup pickup = _s.CanliPickup();
                _s.PickuptanUzaklas(pickup, SpecialWeaponPickup.PickupRadius * 4f);

                Time.captureDeltaTime = Adim(_s.Special.GroundLifetime);
                yield return SpecialWeaponSahne.Bekle(() => SpecialWeaponSahne.AktifPickupSayisi() == 0, SpecialWeaponSahne.FazZamanAsimi, "Yerde süre dolmadı.");
                yield return SpecialWeaponSahne.Kareler(Kare);
                Time.captureDeltaTime = 0f;

                Assert.AreEqual(1, sayac.GroundExpired, "GroundExpired olayı tam bir kez.");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponGroundExpired), "Yerde-bitti sesi tam bir kez.");
                Assert.AreEqual(1, sayac.LoopDurdurSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop), "Beacon loop tam bir kez durur.");
                Assert.AreEqual(0, sayac.PickedUp, "Süre dolduğu hâlde pickup olayı üretildi.");
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, "HUD durumu kapanmadı.");
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Director Idle'a dönmedi.");
                Assert.IsFalse(_s.Controller.Special.IsActive);
                Debug.Log("[DD-TEST] wp007.tm22 " + sayac.Ozet());
            }
        }

        // ============================================================ TM-23 / TM-26 · AP-6

        /// <summary>[TM-23][TM-26] Pickup tam bir kez kabul edilir; HUD OnGround→Active tek geçiş; yer feedback'i tam bir kez kapanır.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Pickup_TekKabul_HudTekGecis_YerFeedbackiTekKapanis()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                SpecialWeaponPickup pickup = _s.CanliPickup();
                int fazSayisiOnce = sayac.FazSirasi.Count;

                _s.PickupaGit(pickup);
                yield return SpecialWeaponSahne.Bekle(() => WeaponEvents.Special.Phase == SpecialWeaponPhase.Active, SpecialWeaponSahne.FazZamanAsimi, "Pickup alınmadı.");
                yield return SpecialWeaponSahne.Kareler(Kare);

                Assert.AreEqual(1, sayac.PickedUp, "PickedUp tam bir kez.");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponPickup), "Pickup sesi tam bir kez.");
                Assert.AreEqual(1, sayac.LoopDurdurSayisi(CombatAudioEvent.SpecialWeaponBeaconLoop), "Beacon loop pickup'ta tam bir kez durur.");
                Assert.AreEqual(1, sayac.LoopBaslatSayisi(CombatAudioEvent.HeavyWeaponSpinUp), "Aktif kullanım loop'u tam bir kez başlar.");
                Assert.AreEqual(0, sayac.GroundExpired);
                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), "Alınan pickup yerde kaldı.");
                Assert.IsTrue(_s.Controller.SpecialWeaponActive);
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.InUse, _s.Director.State);

                // Tek geçiş: OnGround'dan sonra ilk bildirim Active; araya None girmez.
                Assert.Greater(sayac.FazSirasi.Count, fazSayisiOnce, "Pickup HUD bildirimi üretmedi.");
                Assert.AreEqual(SpecialWeaponPhase.Active, sayac.FazSirasi[fazSayisiOnce], "OnGround→Active geçişi araya None sokuyor.");
                Assert.AreEqual(1, sayac.ChangedActive, "Active bildirimi tam bir kez.");
            }
        }

        // ============================================================ TM-24 · AP-5 yarış

        /// <summary>[TM-24] Süre dolma karesinde oyuncu menzile girerse tek sonuç oluşur: pickup 1, expire 0.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator PickupVeExpire_AyniKare_TekSonuc()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                SpecialWeaponPickup pickup = _s.CanliPickup();
                _s.PickuptanUzaklas(pickup, SpecialWeaponPickup.PickupRadius * 4f);

                float adim = Adim(_s.Special.GroundLifetime);
                Time.captureDeltaTime = adim;

                // Kalan süre bir adımın altına inene kadar bekle; sonraki Update hem alma hem bitiş adayı.
                yield return SpecialWeaponSahne.Bekle(() => pickup.Remaining <= adim, SpecialWeaponSahne.FazZamanAsimi, "Kalan süre adımın altına inmedi.");
                Assert.IsTrue(pickup.IsSpawned, "Pickup yarış karesinden önce bitti.");
                _s.PickupaGit(pickup);
                yield return null;
                yield return null;
                Time.captureDeltaTime = 0f;

                Assert.AreEqual(1, sayac.PickedUp, "Yarışta pickup sonucu bir kez.");
                Assert.AreEqual(0, sayac.GroundExpired, "Aynı karede hem pickup hem expire üretildi (AP-5).");
                Assert.AreEqual(0, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponGroundExpired));
                Assert.AreEqual(SpecialWeaponPhase.Active, WeaponEvents.Special.Phase);
                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi());
            }
        }

        // ============================================================ TM-9 / TM-10 · AP-3

        /// <summary>[TM-9][TM-10] Yerde ya da aktifken zorla spawn reddedilir; yerde her zaman en fazla bir pickup.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator YerdeVeAktifken_IkinciSpawnReddedilir()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            Assert.IsFalse(_s.Director.ForceSpawn(), "OnGround iken ForceSpawn kabul edildi.");
            yield return null;
            Assert.AreEqual(1, SpecialWeaponSahne.AktifPickupSayisi());
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.OnGround, _s.Director.State);

            SpecialWeaponPickup pickup = _s.CanliPickup();
            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Director.State == SpecialWeaponDirector.SpecialWeaponState.InUse, SpecialWeaponSahne.FazZamanAsimi, "InUse olmadı.");
            Assert.IsFalse(_s.Director.ForceSpawn(), "InUse iken ForceSpawn kabul edildi.");
            yield return null;
            Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi());
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.InUse, _s.Director.State);
            Assert.AreEqual(1, _s.Director.SpawnsThisRound);
        }

        // ============================================================ TM-7 · AP-2 / KS-9

        /// <summary>[TM-7] Round bitince bekleyen schedule düşer; yeni round eski mark'ı taşımaz.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RoundBitince_BekleyenScheduleDuser_YeniRoundaTasinmaz()
        {
            yield return Kur();
            _s.SpawnKesinlestir(Pencere);
            _s.Ilerleme = 0f;
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            yield return null;
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Scheduled, _s.Director.State);

            using (var sayac = new SpecialOlaySayaci())
            {
                SpecialWeaponRoundBridge.NotifyRoundEnded(_s.Tur);
                yield return null;
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Round bitince schedule düşmedi.");
                Assert.AreEqual(-1f, _s.Director.ScheduledProgress, 0.0001f);
                Assert.AreEqual(1, sayac.Cancelled, "Scheduled iptali gözlemlenemedi (OS-2 'cancelled').");
                Assert.AreEqual(SpecialWeaponCancelReason.RoundEnded, sayac.Iptaller[0].Reason);
                Assert.AreEqual(SpecialWeaponPhase.None, sayac.Iptaller[0].PhaseAtCancel);
                Assert.AreEqual(0, sayac.Cleared, "Hiç düşmeyen drop için 'cleared' üretildi.");
            }

            // Yeni round: ihtimal 0 → schedule yok; eski mark taşınmadıysa ilerleme yükselince spawn olmaz.
            _s.Special.BaseSpawnChance = 0f;
            _s.Special.MaxSpawnChance = 0f;
            _s.Config.NotifyChanged();
            _s.Tur++;
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            _s.Ilerleme = 1f;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Eski schedule yeni rounda taşındı.");
            Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi());
        }

        // ============================================================ TM-30 / 31 / 35 / 36 · REQ-057

        /// <summary>[TM-30][TM-31][TM-35][TM-36] Klasik silah yok edilmez, magazin ve build korunur, süre bitince tek seferde döner; loop/HUD temizlenir.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KlasikSilah_ParkEdilir_AyniBuildVeMagazinleDoner()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                // Magazini kısmen tüket: dönüşte aynı değerle gelmeli (sabit kare adımı: atış hızı zamanla ölçülür).
                Time.captureDeltaTime = KareAdimi;
                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Kareler(Kare);
                _s.Girdi.AtesBasili = false;
                yield return null;
                Time.captureDeltaTime = 0f;
                ClassicWeaponRuntime klasik = _s.Controller.Classic;
                int mermi = klasik.Ammo;
                string build = _s.Controller.Modifiers.DescribeBuild();
                Assert.Less(mermi, klasik.MagazineSize, "Öncül: magazin tüketilmedi (pozitif kontrol).");

                yield return _s.SpawnEt(Pencere);
                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
                yield return null;

                Assert.AreSame(klasik, _s.Controller.Classic, "Klasik runtime yok edildi/yenilendi (REQ-057).");
                Assert.IsTrue(WeaponEvents.Classic.TemporarilyDisabled, "HUD klasik silahı 'geçici devre dışı' göstermiyor.");
                Assert.AreEqual(mermi, klasik.Ammo, "Park anında magazin değişti.");
                Assert.AreEqual(build, _s.Controller.Modifiers.DescribeBuild(), "Park anında build değişti.");

                // Aktif süreyi tüket.
                Time.captureDeltaTime = Adim(_s.Special.ActiveDuration);
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Aktif süre bitmedi.");
                yield return SpecialWeaponSahne.Kareler(Kare);
                Time.captureDeltaTime = 0f;

                Assert.AreEqual(1, sayac.Ended, "Ended tam bir kez.");
                Assert.AreSame(klasik, _s.Controller.Classic);
                Assert.IsFalse(WeaponEvents.Classic.TemporarilyDisabled, "Klasik silah otomatik dönmedi.");
                Assert.AreEqual(mermi, klasik.Ammo, "Dönüşte magazin değişti (park edilen magazin geri gelmeli).");
                Assert.AreEqual(build, _s.Controller.Modifiers.DescribeBuild(), "Dönüşte build değişti.");
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, "HUD Special durumu kapanmadı.");
                Assert.AreEqual(1, sayac.LoopDurdurSayisi(CombatAudioEvent.HeavyWeaponSpinUp), "Aktif loop bitişte tam bir kez durur.");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponExpired), "Doğal bitişte Expired haber cue'su tam bir kez.");
                // Pozitif kontrol (K-7 #8 kilidinin karşıtı): canlı oyuncuda haber banner'ı görünür.
                SpecialWeaponSahne.HudBanner(out string banner, out float bannerKalan);
                Assert.AreEqual("CLASSIC WEAPON RESTORED", banner, "Doğal bitişte HUD haber banner'ı metni.");
                Assert.Greater(bannerKalan, 0f, "Doğal bitişte HUD haber banner'ı gösterilmedi (pozitif kontrol).");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.HeavyWeaponSpinDown), "Doğal bitişte SpinDown tam bir kez.");
                Assert.AreEqual(0, sayac.Cancelled, "Doğal bitiş 'cancelled' üretmemeli.");
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State);

                // Klasik silah tekrar ateş edebilir (dönüş gerçek).
                int once = klasik.Ammo;
                Time.captureDeltaTime = KareAdimi;
                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Kareler(Kare);
                _s.Girdi.AtesBasili = false;
                Time.captureDeltaTime = 0f;
                Assert.Less(klasik.Ammo, once, "Dönüşten sonra klasik silah ateş etmiyor.");
            }
        }

        // ============================================================ TM-28 / TM-29 · REQ-056

        /// <summary>[TM-28][TM-29] Special kullanımı KLASİK magazini tüketmez, klasik reload başlamaz (WP-069 B4: Special kendi şarjörünü kullanır; ateş gerçekten olur: pozitif kontrol).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AktifKullanim_MermiTuketmez_ReloadYok()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");

                ClassicWeaponRuntime klasik = _s.Controller.Classic;
                int mermi = klasik.Ammo;

                Time.captureDeltaTime = KareAdimi;
                _s.Girdi.AtesBasili = true;
                _s.Girdi.DoldurBasti = true;
                yield return SpecialWeaponSahne.Kareler(Kare * 4);
                _s.Girdi.AtesBasili = false;
                _s.Girdi.DoldurBasti = false;
                Time.captureDeltaTime = 0f;

                Assert.Greater(sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire), 0, "Pozitif kontrol: Special ateş etmedi.");
                Assert.AreEqual(mermi, klasik.Ammo, "Special ateşi klasik magazini tüketti (REQ-056).");
                Assert.IsFalse(klasik.IsReloading, "Special aktifken reload başladı (REQ-056).");
                Assert.IsFalse(WeaponEvents.Classic.IsReloading);
                Assert.IsTrue(_s.Controller.SpecialWeaponActive);
            }
        }

        // ============================================================ TM-33 · AP-7 pause

        /// <summary>[TM-33] Pause'da aktif timer ve saldırı saati ilerlemez — pozitif kontrollü.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Pause_AktifTimerVeSaldiriDurur_PozitifKontrollu()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
                SpecialWeaponRuntime rt = _s.Controller.Special;

                float adim = Adim(_s.Special.ActiveDuration);
                Time.captureDeltaTime = adim;
                _s.Girdi.AtesBasili = true;

                // (i) pozitif kontrol
                float once = rt.Remaining;
                int atisOnce = sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire);
                yield return SpecialWeaponSahne.Kareler(Kare);
                Assert.Greater(once - rt.Remaining, 0f, "Pozitif kontrol: aktif timer ilerlemedi.");
                Assert.Greater(sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire), atisOnce, "Pozitif kontrol: saldırı olmadı.");

                // (ii) hold
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                Assert.AreEqual(0f, Time.timeScale);
                float dondu = rt.Remaining;
                int atisDondu = sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire);
                int hasarDondu = sayac.Damaged.Count;
                for (int i = 0; i < Kare; i++)
                {
                    yield return null;
                    Assert.AreEqual(dondu, rt.Remaining, "Aktif timer pause'da ilerledi (AP-7).");
                }

                Assert.AreEqual(atisDondu, sayac.CueSayisi(CombatAudioEvent.HeavyWeaponFire), "Pause'da saldırı üretildi (AP-7).");
                Assert.AreEqual(hasarDondu, sayac.Damaged.Count);
                Assert.IsTrue(rt.IsActive, "Pause'da aktif süre bitti.");

                // (iii) serbest
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                float serbest = rt.Remaining;
                yield return SpecialWeaponSahne.Kareler(Kare);
                _s.Girdi.AtesBasili = false;
                Time.captureDeltaTime = 0f;
                Assert.Greater(serbest - rt.Remaining, 0f, "Hold kalkınca aktif timer ilerlemedi.");
            }
        }

        // ============================================================ TM-34 (sahne) · REQ-058

        /// <summary>[TM-34][TM-36] Son-saniye uyarısı tam bir kez (olay + ses); HUD snapshot pencerede uyarıyı taşır; bitişte kapanır.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator SonSaniyeUyarisi_OlayVeSes_TamBirKez_BitisteKapanir()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");
                SpecialWeaponRuntime rt = _s.Controller.Special;

                Time.captureDeltaTime = Adim(_s.Special.ActiveDuration);
                yield return SpecialWeaponSahne.Bekle(() => rt.InFinalSeconds, SpecialWeaponSahne.FazZamanAsimi, "Son-saniye penceresine girilmedi.");
                yield return null;
                Assert.IsTrue(WeaponEvents.Special.FinalSecondsWarning, "HUD snapshot son-saniye uyarısını taşımıyor (REQ-058).");
                Assert.AreEqual(1, sayac.FinalSeconds, "FinalSeconds olayı pencereye girişte tam bir kez.");

                yield return SpecialWeaponSahne.Bekle(() => !rt.IsActive, SpecialWeaponSahne.FazZamanAsimi, "Aktif süre bitmedi.");
                yield return SpecialWeaponSahne.Kareler(Kare);
                Time.captureDeltaTime = 0f;

                Assert.AreEqual(1, sayac.FinalSeconds, "FinalSeconds olayı aynı aktivasyonda tekrar üretildi.");
                Assert.AreEqual(1, sayac.CueSayisi(CombatAudioEvent.SpecialWeaponFinalSeconds), "Son-saniye sesi tam bir kez.");
                Assert.AreEqual(1, sayac.Ended);
                Assert.IsFalse(WeaponEvents.Special.FinalSecondsWarning, "Bitişte uyarı bayrağı temizlenmedi.");
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase);
            }
        }

        // ============================================================ TM-11 / TM-12 / TM-13 · DEC-026

        /// <summary>
        /// [TM-12][TM-11][TM-13] İkinci spawn yalnız config izin verince değerlendirilir, ilk sürecin
        /// tam temizliğinden sonra planlanır, aynı round içinde sıralı çalışır ve toplam sınırı aşmaz.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator IkinciSpawn_TamTemizlikSonrasi_Sirali_SinirAsilmaz()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                _s.SpawnKesinlestir(Pencere, ikinciSpawn: true);
                _s.Ilerleme = 0f;
                SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
                yield return null;
                _s.Ilerleme = Pencere;
                yield return SpecialWeaponSahne.Bekle(() => _s.Director.State == SpecialWeaponDirector.SpecialWeaponState.OnGround, SpecialWeaponSahne.FazZamanAsimi, "İlk spawn olmadı.");
                Assert.AreEqual(1, _s.Director.SpawnsThisRound);

                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Pickup alınmadı.");

                // Aktif süre bitene kadar ilerlemeyi mark'ta tut: ikinci spawn'ın "temizlikten önce" olmadığını ölç.
                SpecialWeaponRuntime rt = _s.Controller.Special;
                Time.captureDeltaTime = Adim(_s.Special.ActiveDuration);
                yield return SpecialWeaponSahne.Bekle(() => !rt.IsActive, SpecialWeaponSahne.FazZamanAsimi, "Aktif süre bitmedi.");
                Time.captureDeltaTime = 0f;

                // Bitiş karesi: runtime kapalı, HUD None, yerde pickup yok — ikinci süreç ancak bundan sonra.
                Assert.IsFalse(rt.IsActive);
                Assert.AreEqual(SpecialWeaponPhase.None, WeaponEvents.Special.Phase, "Bitişte HUD None değil.");
                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), "Temizlikten önce ikinci pickup yerde.");
                Assert.AreEqual(1, sayac.Ended);

                yield return SpecialWeaponSahne.Kareler(2);
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Scheduled, _s.Director.State, "İkinci spawn planlanmadı (config izin veriyor).");
                Assert.GreaterOrEqual(_s.Director.ScheduledProgress, Pencere, "İkinci mark ilk sürecin ilerlemesinin gerisinde.");
                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), "Mark'a ulaşmadan ikinci pickup düştü.");

                _s.Ilerleme = 1f;
                yield return SpecialWeaponSahne.Bekle(() => _s.Director.State == SpecialWeaponDirector.SpecialWeaponState.OnGround, SpecialWeaponSahne.FazZamanAsimi, "İkinci spawn olmadı.");
                Assert.AreEqual(2, _s.Director.SpawnsThisRound, "İkinci spawn sayaca yazılmadı.");
                Assert.AreEqual(2, sayac.Spawned);
                Assert.AreEqual(1, SpecialWeaponSahne.AktifPickupSayisi());

                // Üçüncü yok: ikinci süreç bitince (yerde süre dolar) director Idle kalır.
                _s.PickuptanUzaklas(_s.CanliPickup(), SpecialWeaponPickup.PickupRadius * 4f);
                Time.captureDeltaTime = Adim(_s.Special.GroundLifetime);
                yield return SpecialWeaponSahne.Bekle(() => SpecialWeaponSahne.AktifPickupSayisi() == 0, SpecialWeaponSahne.FazZamanAsimi, "İkinci pickup süresi dolmadı.");
                yield return SpecialWeaponSahne.Kareler(Kare);
                Time.captureDeltaTime = 0f;
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Round başına sınır aşıldı: üçüncü spawn planlandı.");
                Assert.AreEqual(2, _s.Director.SpawnsThisRound);
                Debug.Log("[DD-TEST] wp007.tm12 " + sayac.Ozet());
            }
        }

        /// <summary>[TM-11] Config ikinci spawn'a izin vermiyorsa ilk süreç bitince yeni schedule yoktur.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator IkinciSpawn_ConfigKapaliysa_Planlanmaz()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            _s.PickuptanUzaklas(_s.CanliPickup(), SpecialWeaponPickup.PickupRadius * 4f);
            Time.captureDeltaTime = Adim(_s.Special.GroundLifetime);
            yield return SpecialWeaponSahne.Bekle(() => SpecialWeaponSahne.AktifPickupSayisi() == 0, SpecialWeaponSahne.FazZamanAsimi, "Süre dolmadı.");
            _s.Ilerleme = 1f;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Time.captureDeltaTime = 0f;
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "İkinci spawn config kapalıyken planlandı.");
            Assert.AreEqual(1, _s.Director.SpawnsThisRound);
        }
        // ============================================================ OS-2 · scheduled / cleared

        /// <summary>[OS-2 scheduled][OS-2 fully cleared] Schedule olayı mark'ı taşır; yerde süre dolunca ve aktif süre bitince tam bir kez "cleared".</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Scheduled_VeCleared_Gozlemlenebilir()
        {
            yield return Kur();
            using (var sayac = new SpecialOlaySayaci())
            {
                yield return _s.SpawnEt(Pencere);
                Assert.AreEqual(1, sayac.Scheduled, "Scheduled olayı tam bir kez.");
                Assert.AreEqual(Pencere, sayac.SonMark, 0.0001f, "Scheduled olayı mark'ı taşımıyor.");

                // Yerde süre dolar → cleared 1.
                _s.PickuptanUzaklas(_s.CanliPickup(), SpecialWeaponPickup.PickupRadius * 4f);
                Time.captureDeltaTime = Adim(_s.Special.GroundLifetime);
                yield return SpecialWeaponSahne.Bekle(() => SpecialWeaponSahne.AktifPickupSayisi() == 0, SpecialWeaponSahne.FazZamanAsimi, "Süre dolmadı.");
                yield return SpecialWeaponSahne.Kareler(Kare);
                Time.captureDeltaTime = 0f;
                Assert.AreEqual(1, sayac.Cleared, "Yerde süre dolunca 'cleared' tam bir kez.");
                Assert.AreEqual(0, sayac.Cancelled);

                // İkinci süreç: pickup → aktif → biter → cleared 2.
                _s.Tur++;
                yield return _s.SpawnEt(Pencere);
                Assert.AreEqual(2, sayac.Scheduled);
                _s.PickupaGit(_s.CanliPickup());
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Pickup alınmadı.");
                Assert.AreEqual(1, sayac.Cleared, "Pickup 'cleared' üretmemeli (süreç sürüyor).");
                Time.captureDeltaTime = Adim(_s.Special.ActiveDuration);
                yield return SpecialWeaponSahne.Bekle(() => _s.Director.State == SpecialWeaponDirector.SpecialWeaponState.Idle, SpecialWeaponSahne.FazZamanAsimi, "Director Idle olmadı.");
                yield return SpecialWeaponSahne.Kareler(Kare);
                Time.captureDeltaTime = 0f;
                Assert.AreEqual(2, sayac.Cleared, "Aktif süre bitince 'cleared' tam bir kez.");
                Assert.AreEqual(1, sayac.Ended);
                Assert.AreEqual(0, sayac.Cancelled);
                Debug.Log("[DD-TEST] wp007.os2 " + sayac.Ozet());
            }
        }

        // ============================================================ TM-2 / TM-3 (sahne) · enjekte RNG

        private sealed class SabitRandom : ISpecialWeaponRandom
        {
            public float Deger;
            public SabitRandom(float deger) { Deger = deger; }
            public float Value() => Deger;
            public float Range(float min, float max) => min + (max - min) * Deger;
            public int Range(int min, int maxExclusive) => min;
        }

        /// <summary>[TM-2][TM-3] Schedule yalnız enjekte roll sonucuna bağlıdır; başarısız roll sonraki roundu kilitlemez.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator EnjekteRoll_KararVerir_BasarisizRollKilitlemez()
        {
            yield return Kur();
            SpecialWeaponConfigSection s = _s.Special;
            s.FirstEligibleRound = 1;
            s.BaseSpawnChance = 0.5f;          // test girdisi: ayırt gücü olan bir ihtimal
            s.MaxSpawnChance = 0.5f;
            s.SpawnChanceGrowthPerRound = 0f;
            s.SecondSpawnChance = 0f;
            s.SecondSpawnFromRound = int.MaxValue;
            _s.Config.NotifyChanged();

            var rng = new SabitRandom(0.9f);   // ihtimalin üstünde → planlanmaz
            _s.Director.SetRandomSource(rng);
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            yield return null;
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "İhtimalin üstündeki roll planladı.");

            rng.Deger = 0.1f;                  // ihtimalin altında → planlanır (kilit yok)
            SpecialWeaponRoundBridge.NotifyRoundEnded(_s.Tur);
            _s.Tur++;
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            yield return null;
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Scheduled, _s.Director.State, "Başarısız roll sonraki roundu kilitledi ya da roll sonucu okunmadı.");
            float min = Mathf.Min(s.SpawnWindowMin, s.SpawnWindowMax);
            float max = Mathf.Max(s.SpawnWindowMin, s.SpawnWindowMax);
            Assert.AreEqual(min + (max - min) * rng.Deger, _s.Director.ScheduledProgress, 0.0001f, "Mark enjekte roll'dan türemedi.");
        }

        // ============================================================ TM-13 · yapılandırılmış üst sınır

        /// <summary>[TM-13] Round başına ilk-spawn tavanı yapılandırılabilir: 0 iken ihtimal 1 olsa da planlama yoktur; varsayılan sabitle aynıdır.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator IlkSpawnTavani_Yapilandirilabilir_SifirIkenPlanlamaz()
        {
            yield return Kur();
            Assert.AreEqual(SpecialWeaponDirector.MaxSpawnsPerRoundDefault, _s.Director.MaxSpawnsPerRound, "Tavan varsayılanı sabitten farklı.");
            _s.SpawnKesinlestir(Pencere);
            _s.Director.MaxSpawnsPerRound = 0;
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            _s.Ilerleme = 1f;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State, "Tavan 0 iken planlandı.");
            Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi());
            Assert.AreEqual(0, _s.Director.SpawnsThisRound);
        }

        // ============================================================ TM-14 / 15 / 16 / 17 · konum doğrulayıcı

        private sealed class SahteDogrulayici : ISpecialDropValidator
        {
            public System.Func<Vector3, bool> Ulasilabilir = _ => true;
            public bool ProjeksiyonBasarili = true;
            public Vector3 Projeksiyon;
            public int ProjeksiyonCagri;

            public bool IsReachable(Vector3 drop) => Ulasilabilir(drop);

            public bool TryProject(Vector3 candidate, float maxDistance, out Vector3 projected)
            {
                ProjeksiyonCagri++;
                projected = ProjeksiyonBasarili ? Projeksiyon : candidate;
                return ProjeksiyonBasarili;
            }
        }

        /// <summary>[TM-16][TM-17][AP-4] Geçerli aday yoksa pickup oluşmaz, ham koordinata düşülmez, sayaç/kilit bozulmaz; yeniden deneme her kare değil ilerleme değişince; round bitince gerekçeli iptal.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator GecerliKonumYok_PickupOlusmaz_GuvenliIptal()
        {
            yield return Kur();
            _s.NoktalariKapat();
            var dogrulayici = new SahteDogrulayici { ProjeksiyonBasarili = false };
            _s.Director.SetDropValidator(dogrulayici);
            using (var sayac = new SpecialOlaySayaci())
            {
                _s.SpawnKesinlestir(Pencere);
                SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
                yield return null;
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Scheduled, _s.Director.State);

                _s.Ilerleme = Pencere;
                yield return SpecialWeaponSahne.Kareler(Kare);

                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi(), "Geçerli konum yokken pickup oluştu (AP-4).");
                Assert.AreEqual(0, sayac.Spawned);
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Scheduled, _s.Director.State, "Schedule düştü ya da kilit bozuldu.");
                Assert.AreEqual(0, _s.Director.SpawnsThisRound, "Başarısız yerleşim sayacı bozdu (TM-17).");
                Assert.AreEqual(1, _s.Director.PlacementFailureCount, "Başarısız yerleşim her kare yeniden denendi (PB-4).");
                Assert.AreEqual(1, dogrulayici.ProjeksiyonCagri);

                // İlerleme değişince bir kez daha denenir.
                _s.Ilerleme = Pencere + (SpecialWeaponDirector.LatestSpawnProgress - Pencere) * 0.5f;
                yield return SpecialWeaponSahne.Kareler(Kare);
                Assert.AreEqual(2, _s.Director.PlacementFailureCount, "İlerleme değişince yeniden deneme yok.");
                Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi());

                // Round biter: gerekçeli iptal, Idle.
                SpecialWeaponRoundBridge.NotifyRoundEnded(_s.Tur);
                yield return null;
                Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State);
                Assert.AreEqual(1, sayac.Cancelled);
                Assert.AreEqual(SpecialWeaponCancelReason.NoValidPosition, sayac.Iptaller[0].Reason);
                Assert.AreEqual(0, sayac.Cleared);
                Debug.Log("[DD-TEST] wp007.tm16 failures=" + _s.Director.PlacementFailureCount + " " + sayac.Ozet());
            }
        }

        /// <summary>[TM-14] Yazılı nokta yokken fallback yalnız doğrulayıcının projeksiyonuna düşer (geçerli, erişilebilir konum).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator YaziliNoktaYok_FallbackYalnizDogrulanmisKonuma()
        {
            yield return Kur();
            _s.NoktalariKapat();
            Vector3 hedef = _s.Zemin(0f, 4f);
            var dogrulayici = new SahteDogrulayici { ProjeksiyonBasarili = true, Projeksiyon = hedef };
            _s.Director.SetDropValidator(dogrulayici);

            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.IsNotNull(pickup);
            Vector3 fark = pickup.transform.position - hedef;
            fark.y = 0f;
            Assert.Less(fark.magnitude, 0.01f, "Pickup doğrulayıcının projeksiyonuna değil ham adaya düştü.");
            Assert.GreaterOrEqual(pickup.transform.position.y, hedef.y, "Pickup zeminin altında.");
            Assert.AreEqual(0, _s.Director.PlacementFailureCount);
        }

        /// <summary>[TM-15][TM-14] Erişilemez yazılı aday reddedilir; erişilebilir olan seçilir (mesafe kuralını sağlayan iki aday).</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ErisilemezAday_Reddedilir_ErisilebilirSecilir()
        {
            yield return Kur();
            _s.NoktalariKapat();
            float uzak = _s.Special.MinDistanceFromPlayer + 3f;
            SpecialWeaponSpawnPoint a = _s.TestNoktasi(_s.Zemin(uzak, 0f));
            SpecialWeaponSpawnPoint b = _s.TestNoktasi(_s.Zemin(-uzak, 0f));
            yield return null;
            Assert.AreEqual(2, SpecialWeaponSpawnPoint.All.Count, "Öncül: iki test noktası kayıtlı değil.");

            Vector3 bKonum = b.DropPosition;
            var dogrulayici = new SahteDogrulayici { Ulasilabilir = d => (d - bKonum).sqrMagnitude > 0.01f };
            _s.Director.SetDropValidator(dogrulayici);

            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            Assert.AreEqual(a.DropPosition, pickup.transform.position, "Erişilemez aday (B) seçildi ya da seçim aday dışı.");
        }

        // ============================================================ PB-8 / PB-9 · duplicate director

        /// <summary>[PB-8][PB-9] İkinci director sessizce oluşmaz: devre dışı bırakılır, sayaç ve uyarı üretir; birincisi çalışmaya devam eder.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator IkinciDirector_DevreDisiVeTanilamali()
        {
            yield return Kur();
            int once = SpecialWeaponDirector.DuplicateInstanceCount;
            Assert.AreSame(_s.Director, SpecialWeaponDirector.Instance);

            var go = new GameObject("[Duplicate Special Weapon Director]");
            SpecialWeaponDirector ikinci = go.AddComponent<SpecialWeaponDirector>();
            yield return null;
            try
            {
                Assert.IsFalse(ikinci.enabled, "İkinci director devre dışı bırakılmadı (PB-8).");
                Assert.AreEqual(once + 1, SpecialWeaponDirector.DuplicateInstanceCount, "Duplicate tanılaması sayılmadı (PB-9).");
                Assert.AreSame(_s.Director, SpecialWeaponDirector.Instance, "Instance ikinci director'a geçti.");

                yield return _s.SpawnEt(Pencere);
                Assert.AreEqual(1, SpecialWeaponSahne.AktifPickupSayisi(), "Birinci director artık spawn üretmiyor.");
            }
            finally
            {
                Object.Destroy(go);
            }

            yield return null;
            Assert.AreSame(_s.Director, SpecialWeaponDirector.Instance, "Duplicate yok edilince Instance düştü.");
        }
    }
}
