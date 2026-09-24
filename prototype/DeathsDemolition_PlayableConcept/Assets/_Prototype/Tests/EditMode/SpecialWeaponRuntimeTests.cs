using System;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-007 · Eldeki Special Weapon (SpecialWeaponRuntime) saf sözleşmesi — kare döngüsü
    /// olmadan, <c>Begin/Tick/End</c> ile sürülür. REQ-055 / REQ-056 / REQ-058 (AP-6, AP-7,
    /// AP-8, AP-9 · TM-20, TM-27, TM-28, TM-29, TM-34).
    ///
    /// Süreler ve eşikler config'ten / tanımdan okunur (K4). WP-069 B4 (DEC-097): Special artık şarjörlü
    /// (Raiju 3/9, Namekuji 8/16), dolumlu ve süre tavanlı (40 / 45 s); REQ-056 "sınırsız mermi" DEC-097 ile kalktı.
    /// </summary>
    [TestFixture]
    [Category("WP007")]
    public sealed class SpecialWeaponRuntimeTests
    {
        /// <summary>Test parametresi: bir süreyi kaç adımda tüketeceğimiz. Ürün sayısı değil.</summary>
        private const int AdimSayisi = 40;

        private SpecialWeaponConfigSection _s;
        private SpecialWeaponRuntime _rt;

        [SetUp]
        public void SetUp()
        {
            _s = new SpecialWeaponConfigSection();
            _rt = new SpecialWeaponRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            if (_rt != null && _rt.IsActive)
            {
                _rt.End();
            }
        }

        /// <summary>[TM-27] Aktif süre (tavan) pickup (Begin) anında tam süreden başlar; şarjör ve yedek dolu.</summary>
        [Test]
        public void Begin_AktifSureTamSuredenBaslar()
        {
            int alindi = 0;
            float bildirilenSure = -1f;
            Action<float> dinle = d => { alindi++; bildirilenSure = d; };
            WeaponEvents.SpecialWeaponPickedUp += dinle;
            try
            {
                _rt.Begin(_s, SpecialWeaponId.Raiju);
            }
            finally
            {
                WeaponEvents.SpecialWeaponPickedUp -= dinle;
            }

            Assert.IsTrue(_rt.IsActive);
            Assert.AreEqual(_s.CapSeconds(SpecialWeaponId.Raiju), _rt.Duration, 0.0001f, "Duration tavandan gelmedi.");
            Assert.AreEqual(3, _rt.Ammo, "Raiju şarjörü");
            Assert.AreEqual(9, _rt.ReserveAmmo, "Raiju yedeği");
            Assert.AreEqual(_rt.Duration, _rt.Remaining, 0.0001f, "Remaining tam süreden başlamadı.");
            Assert.AreEqual(1, alindi, "PickedUp olayı tam bir kez.");
            Assert.AreEqual(_rt.Duration, bildirilenSure, 0.0001f, "PickedUp olayı aktif süreyi taşımalı (REQ-055).");
            Assert.IsFalse(_rt.InFinalSeconds, "Başlangıçta son-saniye penceresi açık olamaz.");
        }

        /// <summary>[TM-20] Aktif değilken (yerdeyken) Tick süreyi ilerletmez ve bitiş üretmez.</summary>
        [Test]
        public void AktifDegilken_TickSureIlerletmez()
        {
            Assert.IsFalse(_rt.IsActive);
            float once = _rt.Remaining;
            bool bitti = _rt.Tick(_s.CapSeconds(SpecialWeaponId.Raiju));
            Assert.IsFalse(bitti);
            Assert.AreEqual(once, _rt.Remaining, 0.0001f, "Aktif olmayan runtime'ın süresi değişti.");
        }

        /// <summary>WP-069 B4 (DEC-097): son mermi otomatik dolum başlatır, dolum yedekten alır; şarjör ve yedek bitince Special düşer.</summary>
        [Test]
        public void Sarjor_OtomatikDolum_CephaneBitinceDuser()
        {
            _rt.Begin(_s, SpecialWeaponId.Raiju);
            int bitis = 0;
            Action dinle = () => bitis++;
            WeaponEvents.SpecialWeaponEnded += dinle;
            try
            {
                for (int i = 0; i < 3; i++) { Assert.IsTrue(_rt.CanFire || i > 0); _rt.ConsumeShot(); _rt.Tick(_rt.ShotInterval); }
                Assert.AreEqual(0, _rt.Ammo);
                Assert.IsTrue(_rt.IsReloading, "son mermi hücre değişimini başlatır");
                Assert.AreEqual(3.6f, _rt.ReloadDuration, 1e-4f, "boştan dolum 3,6 s");
                Assert.IsFalse(_rt.CanFire, "dolumda ateş yok");
                _rt.Tick(3.7f);
                Assert.AreEqual(3, _rt.Ammo);
                Assert.AreEqual(6, _rt.ReserveAmmo);

                int guard = 0;
                while (_rt.IsActive && guard++ < 100)
                {
                    if (_rt.CanFire) _rt.ConsumeShot();
                    _rt.Tick(1f);
                }

                Assert.IsFalse(_rt.IsActive, "12 atış bitince Special düşer (AmmoOrTime)");
                Assert.Less(guard, 100);
                Assert.Greater(_rt.Remaining, -1f);
                Assert.AreEqual(1, bitis, "Ended bir kez");
            }
            finally
            {
                WeaponEvents.SpecialWeaponEnded -= dinle;
            }
        }

        /// <summary>WP-069 B4: Raiju 0,25 s şarj ister — erken bırakmak iptal eder (mermi harcanmaz), basılı tutmak ateşler.</summary>
        [Test]
        public void Raiju_Sarj_ErkenBirakmaIptal_BasiliTutmakAteşler()
        {
            _rt.Begin(_s, SpecialWeaponId.Raiju);
            int iptal = 0, basla = 0;
            Action<SpecialUsageEvent> dinle = e => { if (e.Kind == SpecialUsageKind.ChargeCancelled) iptal++; if (e.Kind == SpecialUsageKind.ChargeStarted) basla++; };
            SpecialWeaponUsageEvents.Usage += dinle;
            try
            {
                Assert.IsFalse(_rt.UpdateTrigger(true, true, 0.016f), "ilk kare şarjı başlatır, ateşlemez");
                Assert.IsTrue(_rt.IsCharging);
                Assert.IsFalse(_rt.UpdateTrigger(true, false, 0.1f));
                Assert.Greater(_rt.Charge01, 0.3f);
                Assert.IsFalse(_rt.UpdateTrigger(false, false, 0.016f), "bırakınca iptal");
                Assert.AreEqual(1, iptal);
                Assert.AreEqual(3, _rt.Ammo, "iptal mermi harcamaz");

                Assert.IsFalse(_rt.UpdateTrigger(true, true, 0.016f));
                bool atti = false;
                for (int i = 0; i < 30 && !atti; i++) atti = _rt.UpdateTrigger(true, false, 0.016f);
                Assert.IsTrue(atti, "0,25 s basılı tutunca ateş");
                Assert.AreEqual(2, basla);
            }
            finally
            {
                SpecialWeaponUsageEvents.Usage -= dinle;
            }

            var gel = new SpecialWeaponRuntime();
            gel.Begin(_s, SpecialWeaponId.Namekuji);
            Assert.AreEqual(8, gel.Ammo);
            Assert.IsTrue(gel.UpdateTrigger(true, false, 0.016f), "Namekuji şarjsız: basılı tetik ateşler");
            gel.End();
        }

        /// <summary>WP-069 B4: tepeden jel yayı imleç mesafesine iner (düz zemin düşük açı; ≤ 20 m).</summary>
        [Test]
        public void Namekuji_TepedenYay_ImlecMesafesineAci()
        {
            WeaponDefinition gel = SpecialWeaponConfigSection.DefinitionOf(SpecialWeaponId.Namekuji);
            Vector3 d20 = SpecialWeaponRuntime.ArcDirection(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 30f), true, gel);
            Vector3 d10 = SpecialWeaponRuntime.ArcDirection(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 10f), true, gel);
            float a20 = Mathf.Asin(d20.y) * Mathf.Rad2Deg;
            float a10 = Mathf.Asin(d10.y) * Mathf.Rad2Deg;
            Assert.AreEqual(0.5f * Mathf.Asin(12f * 20f / (22f * 22f)) * Mathf.Rad2Deg, a20, 0.05f, "30 m imleç 20 m'ye kırpılır");
            Assert.Less(a10, a20);
            Assert.Greater(a10, 0f);
        }

        /// <summary>[TM-34] Son-saniye uyarısı eşik geçişinde aynı aktivasyonda YALNIZ bir kez üretilir.</summary>
        [Test]
        public void SonSaniyeUyarisi_EsikGecisinde_YalnizBirKez()
        {
            int uyari = 0;
            float bildirilenKalan = -1f;
            Action<float> dinle = k => { uyari++; bildirilenKalan = k; };
            WeaponEvents.SpecialWeaponFinalSeconds += dinle;
            try
            {
                _rt.Begin(_s, SpecialWeaponId.Raiju);
                float adim = _rt.Duration / AdimSayisi;
                int esikSonrasiTick = 0;
                while (_rt.IsActive)
                {
                    bool esikteydi = _rt.InFinalSeconds;
                    _rt.Tick(adim);
                    if (esikteydi)
                    {
                        esikSonrasiTick++;
                    }
                }

                Assert.Greater(esikSonrasiTick, 1, "Eşik penceresi birden fazla tick sürmedi: 'yalnız bir kez' ayırt edilemez (öncül).");
                Assert.AreEqual(1, uyari, "Son-saniye uyarısı tam bir kez üretilmeli (REQ-058).");
                Assert.LessOrEqual(bildirilenKalan, _s.FinalSecondsWarning + 0.0001f, "Uyarı eşikten önce üretildi.");
                Assert.Greater(bildirilenKalan, 0f, "Uyarı süre bittikten sonra üretildi.");
            }
            finally
            {
                WeaponEvents.SpecialWeaponFinalSeconds -= dinle;
            }
        }

        /// <summary>[AP-9] Süre bitince Tick tam o karede true döner, Ended bir kez yayınlanır, ikinci End sessizdir.</summary>
        [Test]
        public void SureBitince_TekBitis_IkinciEndSessiz()
        {
            int bitis = 0;
            Action dinle = () => bitis++;
            WeaponEvents.SpecialWeaponEnded += dinle;
            try
            {
                _rt.Begin(_s, SpecialWeaponId.Raiju);
                float adim = _rt.Duration / AdimSayisi;
                int bitisKaresi = 0;
                for (int i = 0; i < AdimSayisi * 2 && _rt.IsActive; i++)
                {
                    if (_rt.Tick(adim))
                    {
                        bitisKaresi++;
                    }
                }

                Assert.AreEqual(1, bitisKaresi, "Tick bitişi tam bir karede bildirmeli.");
                Assert.IsFalse(_rt.IsActive);
                Assert.AreEqual(0f, _rt.Remaining, 0.0001f);
                Assert.AreEqual(1, bitis, "Ended bir kez.");

                _rt.End();
                Assert.AreEqual(1, bitis, "İkinci End yeni Ended üretti (aynı bitiş iki kez).");
                Assert.IsFalse(_rt.Tick(adim), "Bitmiş runtime Tick'te yeniden bitiş üretti.");
            }
            finally
            {
                WeaponEvents.SpecialWeaponEnded -= dinle;
            }
        }

        /// <summary>[REQ-059][TM-32 eşi] Runtime, WeaponModifierState'i yapısal olarak göremez (mevcut yansıma testinin ikizi; kayıp 0).</summary>
        [Test]
        public void ModifierState_YapisalOlarakGorunmez()
        {
            const BindingFlags hepsi = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            foreach (FieldInfo f in typeof(SpecialWeaponRuntime).GetFields(hepsi))
            {
                Assert.AreNotEqual(typeof(WeaponModifierState), f.FieldType, "Alan WeaponModifierState tipinde: " + f.Name);
            }

            foreach (MethodInfo m in typeof(SpecialWeaponRuntime).GetMethods(hepsi))
            {
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Assert.AreNotEqual(typeof(WeaponModifierState), p.ParameterType, "Parametre WeaponModifierState: " + m.Name);
                }
            }
        }
    }
}
