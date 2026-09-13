using System;
using System.Reflection;
using DD.Prototype.Core.Config;
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
    /// Süreler ve eşikler config'ten okunur (K4); sayı literal'i yok. Kagura Bell yalnız
    /// fixture kimliğidir (PrototypeOnly), saldırı biçimi burada sınanmaz.
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

        /// <summary>[TM-27] Aktif süre pickup (Begin) anında tam süreden başlar; süre config'tendir.</summary>
        [Test]
        public void Begin_AktifSureTamSuredenBaslar()
        {
            int alindi = 0;
            float bildirilenSure = -1f;
            Action<float> dinle = d => { alindi++; bildirilenSure = d; };
            WeaponEvents.SpecialWeaponPickedUp += dinle;
            try
            {
                _rt.Begin(_s, SpecialWeaponId.KaguraBell);
            }
            finally
            {
                WeaponEvents.SpecialWeaponPickedUp -= dinle;
            }

            Assert.IsTrue(_rt.IsActive);
            Assert.AreEqual(_s.ActiveDuration, _rt.Duration, 0.0001f, "Duration config'ten gelmedi.");
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
            bool bitti = _rt.Tick(_s.ActiveDuration);
            Assert.IsFalse(bitti);
            Assert.AreEqual(once, _rt.Remaining, 0.0001f, "Aktif olmayan runtime'ın süresi değişti.");
        }

        /// <summary>[TM-28][TM-29] Runtime'da mermi ya da reload durumu yoktur — yapısal olarak (yansıma).</summary>
        [Test]
        public void MermiVeReloadDurumu_YapisalOlarakYok()
        {
            const BindingFlags hepsi = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            foreach (MemberInfo m in typeof(SpecialWeaponRuntime).GetMembers(hepsi))
            {
                string ad = m.Name.ToLowerInvariant();
                Assert.IsFalse(ad.Contains("ammo") || ad.Contains("magazine") || ad.Contains("reload"),
                    "SpecialWeaponRuntime içinde mermi/reload üyesi var: " + m.Name + " (REQ-056).");
            }
        }

        /// <summary>[TM-28] Atış tüketimi yalnız atış aralığını (cooldown) etkiler; süreyi ve başka bir sayacı değil.</summary>
        [Test]
        public void ConsumeShot_YalnizAtisAraligi_SureyiDegistirmez()
        {
            _rt.Begin(_s, SpecialWeaponId.KaguraBell);
            float kalan = _rt.Remaining;
            Assert.IsTrue(_rt.CanFire);
            _rt.ConsumeShot();
            Assert.IsFalse(_rt.CanFire, "Atış sonrası aralık dolmadan tekrar ateş edilebiliyor.");
            Assert.AreEqual(kalan, _rt.Remaining, 0.0001f, "ConsumeShot aktif süreyi değiştirdi.");

            // Aralık kadar ilerleyince tekrar ateş edilebilir; aralık config'teki atış hızından türer.
            _rt.Tick(_rt.ShotInterval);
            Assert.IsTrue(_rt.CanFire, "Atış aralığı dolduğu hâlde ateş kapalı.");
            Assert.AreEqual(1f / Mathf.Max(0.05f, _s.Common(SpecialWeaponId.KaguraBell).FireRate), _rt.ShotInterval, 0.0001f);
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
                _rt.Begin(_s, SpecialWeaponId.KaguraBell);
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
                _rt.Begin(_s, SpecialWeaponId.KaguraBell);
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
