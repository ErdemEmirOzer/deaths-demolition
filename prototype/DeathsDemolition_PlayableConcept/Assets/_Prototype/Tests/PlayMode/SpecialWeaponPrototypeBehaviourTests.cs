using System.Collections;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-007 · PrototypeOnly KARAKTERİZASYON — ürün kararı değil.
    ///
    /// Bu fixture, onaylı bir ürün kararı OLMAYAN mevcut prototip davranışlarını değiştirmeden
    /// sabitler (ana prompt: "mevcut davranışı değiştirmeden koru, testle karakterize et,
    /// canonical karar olarak sunma"):
    ///   KS-3  Round sınırı: yerdeki ya da aktif Special Weapon yeni rounda geçer (SpecialWeaponDirector.OnRoundStarted).
    ///         Ek gözlem (B-3): sınırı aşan süreç bitince o round için ilk-spawn roll'u yapılmaz.
    ///   KS-4  Pickup anında yarım reload İPTAL edilir (WeaponController.ActivateSpecialWeapon → CancelReload); ilerleme korunmaz.
    ///   KS-6  Silah kimliği spawn anında roll edilir, pickup anında değil.
    /// Değerler config'ten okunur; sayı literal'i yok. Değiştirilmesi gerekirse ÇD-2/ÇD-3 → kullanıcı kararı.
    /// </summary>
    [TestFixture]
    [Category("PrototypeOnly")]
    [Category("WP007")]
    public sealed class SpecialWeaponPrototypeBehaviourTests
    {
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

        /// <summary>[KS-3][TM-51] Yerdeki pickup round sınırını aşar: round biter, yenisi başlar, pickup yerde ve timer sürer.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RoundSiniri_YerdekiPickup_YeniRoundaGecer_Karakterizasyon()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            float kalanOnce = pickup.Remaining;

            SpecialWeaponRoundBridge.NotifyRoundEnded(_s.Tur);
            _s.Tur++;
            _s.Ilerleme = 0f;
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            yield return SpecialWeaponSahne.Kareler(Kare);

            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.OnGround, _s.Director.State, "Bugünkü prototipte yerdeki pickup round sınırında yaşar (karakterizasyon).");
            Assert.AreEqual(1, SpecialWeaponSahne.AktifPickupSayisi());
            Assert.AreSame(pickup, _s.CanliPickup());
            Assert.Less(pickup.Remaining, kalanOnce, "Round sınırında ground timer durmaz (karakterizasyon).");
            Assert.AreEqual(0, _s.Director.SpawnsThisRound, "Yeni roundda sayaç sıfırlanır (karakterizasyon: erken dönüşten önce).");
        }

        /// <summary>
        /// [KS-3][B-3] Sınırı aşan süreç bittiğinde yeni round için ilk-spawn roll'u yapılmaz (sayaç
        /// sıfırlanmış olsa da): director Idle kalır, ikinci-spawn yolu config'te kapalıysa schedule yok.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RoundSiniri_SurecBitince_IlkRollYapilmaz_Karakterizasyon()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            _s.PickuptanUzaklas(pickup, SpecialWeaponPickup.PickupRadius * 4f);

            SpecialWeaponRoundBridge.NotifyRoundEnded(_s.Tur);
            _s.Tur++;
            _s.Ilerleme = 0f;
            SpecialWeaponRoundBridge.NotifyRoundStarted(_s.Tur);
            yield return null;
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.OnGround, _s.Director.State);

            Time.captureDeltaTime = Adim(_s.Special.GroundLifetime);
            yield return SpecialWeaponSahne.Bekle(() => SpecialWeaponSahne.AktifPickupSayisi() == 0, SpecialWeaponSahne.FazZamanAsimi, "Yerde süre dolmadı.");
            _s.Ilerleme = 1f;
            yield return SpecialWeaponSahne.Kareler(Kare);
            Time.captureDeltaTime = 0f;

            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State,
                "Bugünkü prototipte sınırı aşan süreç bitince yeni round için ilk roll yapılmaz (karakterizasyon, B-3).");
            Assert.AreEqual(-1f, _s.Director.ScheduledProgress, 0.0001f);
            Assert.AreEqual(0, _s.Director.SpawnsThisRound);
            Assert.AreEqual(0, SpecialWeaponSahne.AktifPickupSayisi());
        }

        /// <summary>
        /// DEC-099 (D6, kullanıcı 24.09) · elde tutulan Special round sonunda biter: Cancelled(RoundEnded, Active),
        /// director Idle, klasik silah geri gelir; yeni round normal planlanabilir. (Eski KS-3 karakterizasyonu: elde kalırdı.)
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator RoundSiniri_AktifSpecial_RoundSonundaBiter()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            _s.PickupaGit(_s.CanliPickup());
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Special aktif olmadı.");

            var iptaller = new System.Collections.Generic.List<SpecialWeaponCancelInfo>();
            System.Action<SpecialWeaponCancelInfo> onCancel = i => iptaller.Add(i);
            WeaponEvents.SpecialWeaponCancelled += onCancel;
            try
            {
                SpecialWeaponRoundBridge.NotifyRoundEnded(_s.Tur);
                yield return SpecialWeaponSahne.Kareler(Kare);
            }
            finally
            {
                WeaponEvents.SpecialWeaponCancelled -= onCancel;
            }

            Assert.IsFalse(_s.Controller.SpecialWeaponActive, "aktif Special round sonunda biter");
            Assert.AreEqual(SpecialWeaponDirector.SpecialWeaponState.Idle, _s.Director.State);
            Assert.AreEqual(1, iptaller.Count);
            Assert.AreEqual(SpecialWeaponCancelReason.RoundEnded, iptaller[0].Reason);
            Assert.AreEqual(SpecialWeaponPhase.Active, iptaller[0].PhaseAtCancel);
            Assert.IsFalse(WeaponEvents.Classic.TemporarilyDisabled, "klasik silah geri geldi");
        }

        /// <summary>[KS-4] Pickup anında süren reload iptal edilir: ReloadInterrupted 1, reload durumu düşer, ilerleme korunmaz; dönüşte park anındaki magazin.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Pickup_YarimReloadIptalEdilir_Karakterizasyon()
        {
            yield return Kur();
            int kesildi = 0;
            int tamamlandi = 0;
            System.Action kesilenDinle = () => kesildi++;
            System.Action tamamDinle = () => tamamlandi++;
            WeaponEvents.ReloadInterrupted += kesilenDinle;
            WeaponEvents.ReloadCompleted += tamamDinle;
            try
            {
                yield return _s.SpawnEt(Pencere);
                SpecialWeaponPickup pickup = _s.CanliPickup();
                ClassicWeaponRuntime klasik = _s.Controller.Classic;

                // Birkaç atış, sonra reload başlat ve yarısında pickup'a ışınlan.
                Time.captureDeltaTime = KareAdimi;
                _s.Girdi.AtesBasili = true;
                yield return SpecialWeaponSahne.Kareler(Kare);
                _s.Girdi.AtesBasili = false;
                _s.Girdi.DoldurBasti = true;
                yield return null;
                _s.Girdi.DoldurBasti = false;
                Assert.IsTrue(klasik.IsReloading, "Öncül: reload başlamadı.");
                int mermiParkta = klasik.Ammo;
                yield return null;
                Assert.IsTrue(klasik.IsReloading, "Öncül: reload bir karede bitti; 'yarım' ölçülemez.");
                Assert.Greater(klasik.ReloadProgress01, 0f);
                Assert.Less(klasik.ReloadProgress01, 1f);

                _s.PickupaGit(pickup);
                yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Pickup alınmadı.");
                Time.captureDeltaTime = 0f;

                Assert.AreEqual(1, kesildi, "Bugünkü prototipte pickup süren reload'u keser (karakterizasyon; ürün kararı değil).");
                Assert.AreEqual(0, tamamlandi, "Reload pickup'ta tamamlanmış sayılmaz.");
                Assert.IsFalse(klasik.IsReloading);
                Assert.AreEqual(0f, klasik.ReloadRemaining, 0.0001f, "Reload ilerlemesi korunmaz: kalan süre sıfırlanır (karakterizasyon).");
                Assert.AreEqual(mermiParkta, klasik.Ammo, "Park anındaki magazin değişti.");

                Time.captureDeltaTime = Adim(_s.Special.ActiveDuration);
                yield return SpecialWeaponSahne.Bekle(() => !_s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Aktif süre bitmedi.");
                Time.captureDeltaTime = 0f;
                yield return null;
                Assert.AreEqual(mermiParkta, klasik.Ammo, "Dönüşte magazin park anındakinden farklı.");
                Assert.IsFalse(klasik.IsReloading, "Dönüşte reload kendiliğinden sürmez (karakterizasyon).");
            }
            finally
            {
                WeaponEvents.ReloadInterrupted -= kesilenDinle;
                WeaponEvents.ReloadCompleted -= tamamDinle;
                Time.captureDeltaTime = 0f;
            }
        }

        /// <summary>[KS-6] Silah kimliği spawn anında belirlenir: yerdeki pickup, director'ın roll'u ve alınan runtime aynı kimliği taşır.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator SilahKimligi_SpawnAnindaRollEdilir_Karakterizasyon()
        {
            yield return Kur();
            yield return _s.SpawnEt(Pencere);
            SpecialWeaponPickup pickup = _s.CanliPickup();
            SpecialWeaponId yerdeki = pickup.WeaponId;
            Assert.AreEqual(yerdeki, _s.Director.LastRolledWeapon, "Director roll'u ile yerdeki kimlik farklı.");
            Assert.AreEqual(yerdeki, WeaponEvents.Special.WeaponId, "HUD snapshot yerdeki kimliği taşımıyor.");

            _s.PickupaGit(pickup);
            yield return SpecialWeaponSahne.Bekle(() => _s.Controller.SpecialWeaponActive, SpecialWeaponSahne.FazZamanAsimi, "Pickup alınmadı.");
            Assert.AreEqual(yerdeki, _s.Controller.Special.Id, "Alınan runtime kimliği yerdekinden farklı (KS-6).");
            Assert.AreEqual(yerdeki, WeaponEvents.Special.WeaponId);
        }
    }
}
