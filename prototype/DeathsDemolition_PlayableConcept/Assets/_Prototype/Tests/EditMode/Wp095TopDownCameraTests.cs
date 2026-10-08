using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-095 (R3) · tepeden kamera K1–K5 saf matematiği (K/wp-095/HIS-SOZLESMESI.md §2–§3, §4.5, §6, §7; F2-KAMERA-GOREV.md).
    /// <see cref="TopDownFeelCamera"/> doğrudan sürülür: sahne, rig, Unity karesi yok. Kare = 1/60 s; 1080p tepeden kamerada
    /// (yükseklik 15, geri 13, FOV 42) 1 m ≈ 71 px. Profil her testte <see cref="FeelProfileTD.ResetToDefaults"/> ile kurulur
    /// (§3 / §4.0 varsayılanları); sayılar oradan okunur, teste yalnız sözleşmenin px tablosu yazılıdır.
    /// </summary>
    [TestFixture]
    [Category("WP095")]
    public sealed class Wp095TopDownCameraTests
    {
        private const float Adim = 1f / 60f;
        private const float PxPerMetre = 71f;                       // his sözleşmesi "Birimler"
        private const float BakisYumusatma = 0.18f;                 // CameraConfigSection look-ahead smoothing (K4 yön takibi)
        private static readonly Vector3 TepedenOfset = new Vector3(0f, 15f, -13f);
        private static readonly float Mesafe = TepedenOfset.magnitude;

        private FeelProfileTD _profil;
        private Quaternion _rot;
        private TopDownFeelCamera _kam;

        [SetUp]
        public void Kur()
        {
            _rot = Quaternion.LookRotation(-TepedenOfset.normalized, Vector3.up);   // the rig's fixed top-down rotation (yaw 0)
            _profil = YeniProfil();
            _kam = new TopDownFeelCamera { Profile = _profil };
        }

        [TearDown]
        public void Yik()
        {
            if (_profil != null) Object.DestroyImmediate(_profil);
            _profil = null;
            _kam = null;
        }

        private static FeelProfileTD YeniProfil()
        {
            FeelProfileTD p = ScriptableObject.CreateInstance<FeelProfileTD>();
            p.ResetToDefaults();
            p.hideFlags = HideFlags.HideAndDontSave;
            return p;
        }

        private void Bilesik(out Vector3 dugum, out float roll, float oyuncu = 1f, bool azHareket = false)
        {
            _kam.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, oyuncu, azHareket, Mesafe, out dugum, out roll);
        }

        private float AtesGenligi(float travma) => _profil.fireMaxOffsetMeters * Mathf.Pow(travma, _profil.traumaExponent);

        private void Ilerlet(TopDownFeelCamera kam, float saniye)
        {
            int n = Mathf.CeilToInt(saniye / Adim - 1e-3f);
            for (int i = 0; i < n; i++) kam.TickEnvelopes(Adim, BakisYumusatma);
        }

        // ================================================================ K1

        /// <summary>
        /// §2 / §3: 2 s basılı tetik, 60 fps. Kalıcı ateş sarsıntısı (1–2 s arası zarf ortalaması) sözleşme tablosundaki px'e
        /// oturur (≤ %5 üstü, ≥ %10 altı); tepe ve gerçek düğüm ofseti silahın travma tavanını (0,24 × cap² m) hiç aşmaz; roll 0.
        /// </summary>
        [TestCase(ClassicWeaponId.AutoSmg, 900f, 2.3f)]
        [TestCase(ClassicWeaponId.AssaultRifle, 600f, 2.6f)]
        [TestCase(ClassicWeaponId.Lmg1, 720f, 3.7f)]
        public void K1_KaliciAtes_SozlesmePxTavaninda_TepeSilahTavaniniAsmaz(ClassicWeaponId silah, float rpm, float kaliciPx)
        {
            WeaponFeelTD satir = _profil.Weapon(silah);
            Assert.Greater(satir.fireTraumaAdd, 0f, "profilde " + silah + " satırı yok");
            float aralik = 60f / rpm;
            float sonraki = 0f;
            float tavanPx = AtesGenligi(satir.fireTraumaCap) * PxPerMetre;
            float toplam = 0f, tepePx = 0f, tepeOfsetPx = 0f;
            int n = 0;
            for (int kare = 0; kare < 120; kare++)
            {
                float t = kare * Adim;
                while (t + 1e-4f >= sonraki)
                {
                    _kam.Shot(satir, Vector3.forward, false);
                    sonraki += aralik;
                }

                Bilesik(out Vector3 dugum, out float roll);
                Assert.AreEqual(0f, roll, 0f, "K1 roll'u 0 olmalı (kare " + kare + ")");
                float px = AtesGenligi(_kam.FireTrauma) * PxPerMetre;
                tepePx = Mathf.Max(tepePx, px);
                tepeOfsetPx = Mathf.Max(tepeOfsetPx, dugum.magnitude * PxPerMetre);
                if (kare >= 60)
                {
                    toplam += px;
                    n++;
                }

                _kam.AdvanceTrauma(Adim);
            }

            float kalici = toplam / n;
            Debug.Log("[DD-TEST] wp095.k1 " + silah + " kalici=" + kalici.ToString("F2") + " px (sozlesme " + kaliciPx + ") tepe=" +
                      tepePx.ToString("F2") + " ofset tepe=" + tepeOfsetPx.ToString("F2") + " tavan=" + tavanPx.ToString("F2"));
            Assert.LessOrEqual(kalici, kaliciPx * 1.05f, "kalıcı ateş sarsıntısı sözleşme tablosunun üstünde");
            Assert.GreaterOrEqual(kalici, kaliciPx * 0.9f, "kalıcı ateş sarsıntısı sözleşmenin çok altında (ölçüm boş?)");
            Assert.LessOrEqual(tepePx, tavanPx + 1e-3f, "travma silahın tavanını aştı");
            Assert.LessOrEqual(tepeOfsetPx, tavanPx + 1e-3f, "düğüm ofseti silahın tavanını aştı");
            Assert.Greater(tepeOfsetPx, 0f, "ölçüm boş: düğüm hiç oynamadı");
        }

        /// <summary>
        /// §1.2: ateşte roll 0; gürültünün ekrandaki nişan eksenine dik bileşeni × aimLateralShare (0,35), eksen boyunca × 1.
        /// Aynı girdiyle aimLateralShare = 1 olan ikiz kamera sürülür: dik bileşen her karede tam 0,35 katı, boy bileşeni aynı.
        /// </summary>
        [Test]
        public void K1_RollSifir_NisanaDikBilesen035()
        {
            FeelProfileTD tam = YeniProfil();
            try
            {
                tam.aimLateralShare = 1f;
                var ikiz = new TopDownFeelCamera { Profile = tam };
                WeaponFeelTD ar = _profil.Weapon(ClassicWeaponId.AssaultRifle);
                Vector3 nisan = new Vector3(1f, 0f, 1f).normalized;
                Vector3 nisanYerel = Quaternion.Inverse(_rot) * nisan;
                Vector2 eksen = new Vector2(nisanYerel.x, nisanYerel.y).normalized;
                Vector2 yan = new Vector2(-eksen.y, eksen.x);
                float enBuyukYan = 0f, enBuyukBoy = 0f;
                for (int kare = 0; kare < 120; kare++)
                {
                    if (kare % 6 == 0)
                    {
                        _kam.Shot(ar, nisan, false);
                        ikiz.Shot(ar, nisan, false);
                    }

                    _kam.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 1f, false, Mesafe, out Vector3 d, out float roll);
                    ikiz.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 1f, false, Mesafe, out Vector3 dTam, out float rollTam);
                    Assert.AreEqual(0f, roll, 0f, "ateş roll'u 0 değil");
                    Assert.AreEqual(0f, d.z, 1e-7f, "K1 ekran düzleminde kalır");
                    float genlik = AtesGenligi(_kam.FireTrauma);
                    Vector2 o = new Vector2(d.x, d.y), oTam = new Vector2(dTam.x, dTam.y);
                    float y = Vector2.Dot(o, yan), yTam = Vector2.Dot(oTam, yan);
                    float b = Vector2.Dot(o, eksen), bTam = Vector2.Dot(oTam, eksen);
                    Assert.AreEqual(yTam * _profil.aimLateralShare, y, 1e-6f, "dik bileşen × aimLateralShare değil (kare " + kare + ")");
                    Assert.AreEqual(bTam, b, 1e-6f, "nişan ekseni boyunca bileşen × 1 değil (kare " + kare + ")");
                    Assert.LessOrEqual(Mathf.Abs(y), genlik * _profil.aimLateralShare + 1e-6f);
                    Assert.LessOrEqual(Mathf.Abs(b), genlik + 1e-6f);
                    enBuyukYan = Mathf.Max(enBuyukYan, Mathf.Abs(y));
                    enBuyukBoy = Mathf.Max(enBuyukBoy, Mathf.Abs(b));
                    _kam.AdvanceTrauma(Adim);
                    ikiz.AdvanceTrauma(Adim);
                }

                Assert.Greater(enBuyukYan, 1e-4f, "ölçüm boş: dik bileşen hiç oluşmadı");
                Assert.Greater(enBuyukBoy, 1e-4f, "ölçüm boş: boy bileşeni hiç oluşmadı");
            }
            finally
            {
                Object.DestroyImmediate(tam);
            }
        }

        // ================================================================ K2

        /// <summary>§2: dünya travması tavanda (T = 1) tutulur; roll her karede ≤ rollCapDegrees × T² (≤ 0,4°) ve gerçekten oluşur.</summary>
        [Test]
        public void K2_Roll_04DereceTavaninda()
        {
            float enBuyukRoll = 0f;
            for (int kare = 0; kare < 240; kare++)
            {
                _kam.World(1f);
                Assert.AreEqual(1f, _kam.WorldTrauma, 1e-6f);
                Bilesik(out Vector3 dugum, out float roll);
                Assert.LessOrEqual(Mathf.Abs(roll), _profil.rollCapDegrees + 1e-5f, "K2 roll'u tavanı aştı (kare " + kare + ")");
                Assert.LessOrEqual(_kam.Last.WorldMeters, _profil.worldMaxOffsetMeters + 1e-6f);
                Assert.LessOrEqual(dugum.magnitude, _profil.aggregateCapMeters + 1e-5f);
                enBuyukRoll = Mathf.Max(enBuyukRoll, Mathf.Abs(roll));
                _kam.AdvanceTrauma(Adim);
            }

            Assert.LessOrEqual(_profil.rollCapDegrees, 0.4f + 1e-6f, "sözleşme roll tavanı 0,4°");
            Assert.Greater(enBuyukRoll, 0.05f, "ölçüm boş: K2 roll'u hiç oluşmadı");

            // T = 0.5 → roll ≤ cap × 0.25
            var yarim = new TopDownFeelCamera { Profile = _profil };
            yarim.World(0.5f);
            for (int kare = 0; kare < 60; kare++)
            {
                yarim.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 1f, false, Mesafe, out Vector3 _, out float r);
                Assert.LessOrEqual(Mathf.Abs(r), _profil.rollCapDegrees * Mathf.Pow(yarim.WorldTrauma, _profil.traumaExponent) + 1e-5f);
                yarim.AdvanceTrauma(Adim);
            }
        }

        // ================================================================ K3

        /// <summary>
        /// §2 K3: sürekli ateşte −nişan tepmesi sustainedKickCapMeters'ta (0,04 m) doyar; tek atış kendi tepmesini korur
        /// (sg_1 0,10 m); tek olay ≤ 0,14 m. Yay ağırlığı satırın kickSpring'i.
        /// </summary>
        [Test]
        public void K3_SurekliAtes004Doyar_TekAtisKendiTepmesi_TekOlay014()
        {
            WeaponFeelTD ar = _profil.Weapon(ClassicWeaponId.AssaultRifle);
            Vector3 tepme = Vector3.zero;
            for (int i = 0; i < 20; i++)
            {
                _kam.Shot(ar, Vector3.forward, false);
                _kam.ApplyKicks(ref tepme, _rot);
                Assert.LessOrEqual(tepme.magnitude, _profil.sustainedKickCapMeters + 1e-6f, "sürekli ateş tepmesi tavanı aştı");
            }

            Assert.AreEqual(_profil.sustainedKickCapMeters, tepme.magnitude, 1e-5f, "ölçüm boş: tavana dayanmadı");
            Assert.Less(Vector3.Dot(_rot * tepme, Vector3.forward), 0f, "ateş tepmesi −nişan yönünde olmalı");
            Assert.AreEqual(ar.kickSpring, _kam.KickWeight);

            WeaponFeelTD sg = _profil.Weapon(ClassicWeaponId.CombatShotgun);
            var tek = new TopDownFeelCamera { Profile = _profil };
            Vector3 tekTepme = Vector3.zero;
            tek.Shot(sg, Vector3.forward, false);
            tek.ApplyKicks(ref tekTepme, _rot);
            Assert.AreEqual(sg.kickMeters, tekTepme.magnitude, 1e-5f, "tek atış kendi tepmesini korumalı (sg_1)");
            Assert.AreEqual(sg.kickSpring, tek.KickWeight);

            var olay = new TopDownFeelCamera { Profile = _profil };
            Vector3 olayTepme = Vector3.zero;
            olay.Kick(Vector3.right, 0.5f);
            olay.ApplyKicks(ref olayTepme, _rot);
            Assert.AreEqual(TopDownFeelCamera.SingleEventKickCapMeters, olayTepme.magnitude, 1e-5f, "tek olay 0,14 m'yi aşmamalı");
            Assert.Greater(Vector3.Dot(_rot * olayTepme, Vector3.right), 0f, "olay tepmesi istenen yönde");
        }

        // ================================================================ toplam kenet · oyuncu ölçeği · azaltılmış hareket

        /// <summary>
        /// §2 / §6: K1 + K2 + K3 (+ doğrudan CameraShakeService) ≤ aggregateCapMeters (0,22 m) ve roll ≤ rollCapDegrees, kanallar
        /// doyurulmuşken bile; K5 kenetten sonra +Z'de. Oyuncu ölçeği 0 → düğüm 0 (K5 dahil). Azaltılmış hareket → toplam ≤ 0,22 × 0,3,
        /// K4 = 0, K5 = 0.
        /// </summary>
        [Test]
        public void Toplam022Kenet_OyuncuOlcegiSifir_AzaltilmisHareket03VeK4K5Sifir()
        {
            WeaponFeelTD rl = _profil.Weapon(ClassicWeaponId.Rl1);
            Vector3 tepme = new Vector3(0.14f, 0.10f, 0.05f);
            Vector3 eski = new Vector3(0.20f, -0.10f, 0f);
            _kam.Shot(rl, Vector3.forward, false);
            _kam.FireHeld(true);
            _kam.World(1f);
            Assert.IsTrue(_kam.Zoom(-0.08f));
            Ilerlet(_kam, 0.1f);   // K5 tepesinde, K4 yükseliyor

            float enBuyuk = 0f;
            for (int kare = 0; kare < 30; kare++)
            {
                _kam.Shot(rl, Vector3.forward, false);
                _kam.World(1f);
                _kam.Compose(_rot, eski, 1f, tepme, 1f, 1f, false, Mesafe, out Vector3 d, out float roll);
                Assert.LessOrEqual(_kam.Last.TotalMeters, _profil.aggregateCapMeters + 1e-5f, "K1 + K2 + K3 toplamı tavanı aştı");
                Assert.LessOrEqual(Mathf.Abs(roll), _profil.rollCapDegrees + 1e-5f, "roll tavanı aşıldı");
                Vector3 kenetli = d - new Vector3(0f, 0f, _kam.Last.ZoomMeters);
                Assert.AreEqual(_kam.Last.TotalMeters, kenetli.magnitude, 1e-5f, "K5 kenetin dışında, yalnız +Z'de olmalı");
                enBuyuk = Mathf.Max(enBuyuk, _kam.Last.TotalMeters);
                _kam.AdvanceTrauma(Adim);
            }

            Assert.Greater(enBuyuk, _profil.aggregateCapMeters * 0.95f, "ölçüm boş: kenete dayanılmadı");
            Assert.Greater(_kam.Last.ZoomMeters, 0f, "ölçüm boş: K5 yakınlaşması yok (−.08 = +Z)");
            Assert.LessOrEqual(_kam.Last.ZoomMeters, TopDownFeelCamera.ZoomCapFraction * Mesafe + 1e-4f, "K5 %8 tavanı aşıldı");
            Assert.Greater(_kam.LeanOffset(false).magnitude, 0f, "ölçüm boş: K4 yok");

            _kam.Compose(_rot, eski, 1f, tepme, 1f, 0f, false, Mesafe, out Vector3 sifir, out float sifirRoll);
            Assert.Less(sifir.magnitude, 1e-9f, "oyuncu ölçeği 0 → düğüm 0 olmalı (K5 dahil): " + sifir);
            Assert.AreEqual(0f, sifirRoll, 1e-9f);

            _kam.Compose(_rot, eski, 1f, tepme, 1f, 1f, true, Mesafe, out Vector3 az, out float azRoll);
            Assert.LessOrEqual(_kam.Last.TotalMeters, _profil.aggregateCapMeters * CameraModesFeel.ReducedShakeScale + 1e-5f, "azaltılmış hareket × 0,3");
            Assert.Greater(_kam.Last.TotalMeters, 0f, "azaltılmış harekette sarsıntı kapanmaz, küçülür");
            Assert.AreEqual(0f, _kam.Last.ZoomMeters, 0f, "azaltılmış harekette K5 = 0");
            Assert.LessOrEqual(Mathf.Abs(azRoll), _profil.rollCapDegrees * CameraModesFeel.ReducedShakeScale + 1e-5f);
            Assert.AreEqual(Vector3.zero, _kam.LeanOffset(true), "azaltılmış harekette K4 = 0");

            // Oyuncu ölçeği yarıda: toplam ve K5 yarıya iner.
            _kam.Compose(_rot, eski, 1f, tepme, 1f, 0.5f, false, Mesafe, out Vector3 _, out float _);
            Assert.LessOrEqual(_kam.Last.TotalMeters, _profil.aggregateCapMeters * 0.5f + 1e-5f);
        }

        // ================================================================ patlama düşümü

        /// <summary>§4.5: A(d) ≤ 3,2 m'de 1, 12 m'de 0 (üs 1,5); 12 m'deki patlama K2 / K3 / K5 üretmez, dibindeki satırın tamamını, arası A(d) katını.</summary>
        [Test]
        public void Patlama_AdDusumu_12mdeSifir()
        {
            Assert.AreEqual(1f, CameraModesFeel.ExplosionFalloff(3.2f), 1e-6f);
            Assert.AreEqual(0f, CameraModesFeel.ExplosionFalloff(12f), 1e-6f);
            EventFeelTD satir = _profil.Event(FeelEventId.Explosion);
            Vector3 oyuncu = Vector3.zero;

            _kam.Explosion(new Vector3(12f, 0f, 0f), oyuncu);
            Vector3 tepme = Vector3.zero;
            _kam.ApplyKicks(ref tepme, _rot);
            Assert.AreEqual(0f, _kam.WorldTrauma, 0f, "12 m'de K2 0 olmalı");
            Assert.AreEqual(0f, tepme.magnitude, 0f, "12 m'de K3 0 olmalı");
            Assert.AreEqual(0, _kam.ZoomPunches, "12 m'de K5 0 olmalı");

            _kam.Explosion(new Vector3(2f, 0f, 0f), oyuncu);
            _kam.ApplyKicks(ref tepme, _rot);
            Assert.AreEqual(satir.worldTraumaAdd, _kam.WorldTrauma, 1e-6f, "dipte K2 satırın tamamı");
            Assert.AreEqual(satir.kickMeters, tepme.magnitude, 1e-5f, "dipte K3 satırın tamamı");
            Assert.Less((_rot * tepme).x, 0f, "K3 patlamadan uzağa");
            Assert.AreEqual(1, _kam.ZoomPunches);

            var orta = new TopDownFeelCamera { Profile = _profil };
            float a = CameraModesFeel.ExplosionFalloff(7.6f);
            Assert.Greater(a, 0f);
            Assert.Less(a, 1f);
            orta.Explosion(new Vector3(0f, 0f, 7.6f), oyuncu);
            Assert.AreEqual(satir.worldTraumaAdd * a, orta.WorldTrauma, 1e-5f, "arada K2 × A(d)");
            Ilerlet(orta, _profil.zoomInSeconds + Adim);
            Assert.AreEqual(satir.zoomPunch * a, orta.ZoomFraction, 2e-3f, "arada K5 × A(d)");
        }

        // ================================================================ anahtarlar

        /// <summary>§1.8 / §9 S5: her katman anahtarı tek başına kapatılınca yalnız o kanal 0 olur, ötekiler sürer.</summary>
        [TestCase("fireTrauma")]
        [TestCase("worldTrauma")]
        [TestCase("kick")]
        [TestCase("fireLean")]
        [TestCase("zoomPunch")]
        public void Anahtarlar_HerBiriYalnizKendiKanaliniKapatir(string anahtar)
        {
            switch (anahtar)
            {
                case "fireTrauma": _profil.fireTrauma = false; break;
                case "worldTrauma": _profil.worldTrauma = false; break;
                case "kick": _profil.kick = false; break;
                case "fireLean": _profil.fireLean = false; break;
                case "zoomPunch": _profil.zoomPunch = false; break;
                default: Assert.Fail("bilinmeyen anahtar " + anahtar); break;
            }

            WeaponFeelTD ar = _profil.Weapon(ClassicWeaponId.AssaultRifle);
            Vector3 tepme = Vector3.zero;
            float ates = 0f, dunya = 0f, tepmeM = 0f, yakin = 0f, egilme = 0f;
            _kam.Shot(ar, Vector3.forward, false);
            _kam.FireHeld(true);
            _kam.Explosion(new Vector3(2f, 0f, 2f), Vector3.zero);
            for (int kare = 0; kare < 20; kare++)
            {
                if (kare % 6 == 0) _kam.Shot(ar, Vector3.forward, false);
                _kam.TickEnvelopes(Adim, BakisYumusatma);
                _kam.ApplyKicks(ref tepme, _rot);
                _kam.Compose(_rot, Vector3.zero, 0f, tepme, 0f, 1f, false, Mesafe, out Vector3 _, out float _);
                ates = Mathf.Max(ates, _kam.Last.FireMeters);
                dunya = Mathf.Max(dunya, _kam.Last.WorldMeters);
                tepmeM = Mathf.Max(tepmeM, _kam.Last.KickMeters);
                yakin = Mathf.Max(yakin, Mathf.Abs(_kam.Last.ZoomMeters));
                egilme = Mathf.Max(egilme, _kam.LeanOffset(false).magnitude);
                _kam.AdvanceTrauma(Adim);
            }

            Assert.AreEqual(anahtar == "fireTrauma", ates == 0f, "K1 fireTrauma=" + _profil.fireTrauma + " → " + ates);
            Assert.AreEqual(anahtar == "worldTrauma", dunya == 0f, "K2 worldTrauma=" + _profil.worldTrauma + " → " + dunya);
            Assert.AreEqual(anahtar == "kick", tepmeM == 0f, "K3 kick=" + _profil.kick + " → " + tepmeM);
            Assert.AreEqual(anahtar == "fireLean", egilme == 0f, "K4 fireLean=" + _profil.fireLean + " → " + egilme);
            Assert.AreEqual(anahtar == "zoomPunch", yakin == 0f, "K5 zoomPunch=" + _profil.zoomPunch + " → " + yakin);
        }

        // ================================================================ K4 / K5 zarfları

        /// <summary>
        /// §2 K4: giriş leanInSeconds (easeOutCubic) tamamlanır — tek basış da bir kez eğilir; bırakınca leanHoldSeconds bekler,
        /// leanOutSeconds'ta (easeInOutSine) 0'a döner; donuk saat (dt 0) ilerletmez. |bakış önü + eğilme| ≤ 3 m (C-00 K7).
        /// </summary>
        [Test]
        public void K4_Zarf_GirisTutmaDonus_DonukSaatDurur_ToplamUcMetre()
        {
            WeaponFeelTD sr = _profil.Weapon(ClassicWeaponId.Sr1);
            Vector3 nisan = Vector3.right;
            _kam.Shot(sr, nisan, false);
            _kam.FireHeld(true);
            _kam.FireHeld(false);   // tek basış
            Ilerlet(_kam, _profil.leanInSeconds * 0.5f);
            float yarida = _kam.Lean01;
            Assert.Greater(yarida, 0.5f, "easeOutCubic yarıda %50'nin üstünde olmalı");
            Assert.Less(yarida, 1f);
            _kam.TickEnvelopes(0f, BakisYumusatma);
            Assert.AreEqual(yarida, _kam.Lean01, 0f, "donuk saatte zarf ilerlememeli");

            Ilerlet(_kam, _profil.leanInSeconds * 0.5f + Adim);
            Assert.AreEqual(1f, _kam.Lean01, 1e-5f, "giriş tamamlanmalı (bırakılmış olsa da)");
            Vector3 egilme = _kam.LeanOffset(false);
            Assert.AreEqual(sr.fireLeanMeters, egilme.magnitude, 1e-3f);
            Assert.Greater(Vector3.Dot(egilme.normalized, nisan), 0.999f, "eğilme nişan yönünde");

            Ilerlet(_kam, _profil.leanHoldSeconds - 3f * Adim);
            Assert.AreEqual(1f, _kam.Lean01, 1e-5f, "bekleme süresince tam kalmalı");
            Ilerlet(_kam, 3f * Adim + _profil.leanOutSeconds * 0.5f);
            Assert.Greater(_kam.Lean01, 0f);
            Assert.Less(_kam.Lean01, 1f, "dönüşte olmalı");
            Ilerlet(_kam, _profil.leanOutSeconds * 0.5f + 2f * Adim);
            Assert.AreEqual(0f, _kam.Lean01, 0f, "dönüş bitmeli");

            Vector3 bakisOnu = nisan * 2.5f;
            Vector3 kenetli = TopDownFeelCamera.ClampLean(bakisOnu, nisan * sr.fireLeanMeters, _profil.lookAheadTotalCapMeters);
            Assert.AreEqual(_profil.lookAheadTotalCapMeters, (bakisOnu + kenetli).magnitude, 1e-4f, "|bakış önü + eğilme| ≤ 3 m");
            Assert.AreEqual(3f, _profil.lookAheadTotalCapMeters, 1e-6f, "C-00 K7: toplam 3,0 m");
        }

        /// <summary>
        /// §2 K5: giriş zoomInSeconds, dönüş zoomOutSeconds; zayıf darbe güçlüyü kesmez, güçlü olan büyütür; ± %8 tavanı (C-00 K6).
        /// Raund başı nefesi +%4'ten 0,8 s'de 0'a döner, yakında zombi varsa atlanır.
        /// </summary>
        [Test]
        public void K5_Zarf_Birlestirme_YuzdeSekiz_RaundBasiNefesi()
        {
            Assert.IsTrue(_kam.Zoom(-0.03f));
            Ilerlet(_kam, _profil.zoomInSeconds);
            Assert.AreEqual(-0.03f, _kam.ZoomFraction, 1e-6f, "giriş sonunda tepe");
            Assert.IsFalse(_kam.Zoom(-0.015f), "zayıf darbe güçlüyü kesmemeli");
            Assert.IsTrue(_kam.Zoom(-0.2f), "güçlü darbe büyütür");
            Ilerlet(_kam, _profil.zoomInSeconds);
            Assert.AreEqual(-TopDownFeelCamera.ZoomCapFraction, _kam.ZoomFraction, 1e-6f, "%8 tavanı");
            Ilerlet(_kam, _profil.zoomOutSeconds + 2f * Adim);
            Assert.AreEqual(0f, _kam.ZoomFraction, 0f, "dönüş bitmeli");

            Assert.IsFalse(_kam.RoundStart(true), "8 m içinde zombi varsa nefes atlanır");
            Assert.AreEqual(0f, _kam.ZoomFraction, 0f);
            Assert.IsTrue(_kam.RoundStart(false));
            // S-02 (08.10): the breath eases in (roundStartBreathInSeconds) — a +4 % step in one frame read as a pop
            Assert.Less(_kam.ZoomFraction, _profil.Event(FeelEventId.RoundStart).zoomPunch * 0.5f, "nefes sıçramaz, yumuşak girer");
            Ilerlet(_kam, _profil.roundStartBreathInSeconds + Adim);
            Assert.AreEqual(_profil.Event(FeelEventId.RoundStart).zoomPunch, _kam.ZoomFraction, 2e-3f, "girişin sonunda +%4");
            Ilerlet(_kam, _profil.roundStartBreathSeconds * 0.5f);
            Assert.Greater(_kam.ZoomFraction, 0f);
            Ilerlet(_kam, _profil.roundStartBreathSeconds * 0.5f + 2f * Adim);
            Assert.AreEqual(0f, _kam.ZoomFraction, 0f, "nefes 0,8 s'de biter");
        }

        // ================================================================ GC

        /// <summary>S-02 (08.10) §4.11: the last zombie's focus shift rides the node after the clamp — scaled by the player, gone in reduced motion and with the slow-motion switch off.</summary>
        [Test]
        public void SonZombiOdagi_DugumdeTavandanSonra_OyuncuOlcegi_AzaltilmisHareketteSifir()
        {
            Vector3 kayma = new Vector3(1.2f, 0f, 0.5f);
            _kam.SetFocusShift(kayma);
            _kam.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 1f, false, Mesafe, out Vector3 tam, out float _);
            Assert.AreEqual(kayma.x, tam.x, 1e-5f, "odak kayması düğüme eklenir (0,22 m sarsıntı tavanının dışında, çerçeve hareketi)");
            Assert.AreEqual(kayma.magnitude, _kam.Last.FocusMeters, 1e-5f);

            _kam.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 0.5f, false, Mesafe, out Vector3 yarim, out float _);
            Assert.AreEqual(kayma.x * 0.5f, yarim.x, 1e-5f, "oyuncu ölçeği");

            _kam.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 1f, true, Mesafe, out Vector3 azaltilmis, out float _);
            Assert.AreEqual(0f, azaltilmis.x, 1e-6f, "azaltılmış hareket: odak kayması yok");

            _profil.lastZombieSlowMo = false;
            _kam.Compose(_rot, Vector3.zero, 0f, Vector3.zero, 0f, 1f, false, Mesafe, out Vector3 kapali, out float _);
            Assert.AreEqual(0f, kapali.x, 1e-6f, "son zombi anahtarı kapalı");
            _kam.SetFocusShift(Vector3.zero);
        }

        /// <summary>
        /// §7: kanalların sıcak yolu (atış, olaylar, zarflar, tepme, bileşim, çürüme) 0 ayırma. GC.Alloc kaydedicisi
        /// (Unity'nin AllocatingGCMemory kısıtının yöntemi) pozitif kontrolle: bilinen `new object[8]` görülmeli.
        /// </summary>
        [Test]
        public void GC_SicakYol_SifirAyirma_PozitifKontrollu()
        {
            WeaponFeelTD ar = _profil.Weapon(ClassicWeaponId.AssaultRifle);
            WeaponFeelTD sw = _profil.Weapon(SpecialWeaponId.Raiju);
            Vector3 tepme = Vector3.zero;
            TopDownFeelCamera kam = _kam;
            Action govde = () =>
            {
                for (int kare = 0; kare < 240; kare++)
                {
                    kam.Shot(ar, Vector3.forward, false);
                    if (kare % 30 == 0) kam.Shot(sw, Vector3.left, true);
                    kam.FireHeld(kare % 40 < 30);
                    if (kare % 20 == 0) kam.Explosion(new Vector3(3f, 0f, 1f), Vector3.zero);
                    if (kare % 25 == 0) kam.PlayerDamaged(0.1f, false, Vector3.back);
                    if (kare % 50 == 0) kam.LastZombie();
                    if (kare % 60 == 0) kam.RoundStart(false);
                    kam.Event(FeelEventId.MultiKill_T2, Vector3.right);
                    kam.EventWithoutKick(FeelEventId.HeavyDeath_Ultra);
                    kam.Kick(Vector3.forward, 0.02f);
                    kam.TickEnvelopes(Adim, BakisYumusatma);
                    TopDownFeelCamera.ClampLean(Vector3.right * 2.5f, kam.LeanOffset(false), 3f);
                    kam.ApplyKicks(ref tepme, _rot);
                    kam.Compose(_rot, Vector3.zero, 0f, tepme, 0f, 1f, false, Mesafe, out Vector3 _, out float _);
                    kam.AdvanceTrauma(Adim);
                }
            };

            govde();   // ısınma: JIT, statik kurucular, FeelEvents.Thresholds
            int pozitif = Ayirmalar(() =>
            {
                object[] bilinen = new object[8];
                GC.KeepAlive(bilinen);
            });
            Assert.Greater(pozitif, 0, "pozitif kontrol: GC.Alloc kaydedicisi bilinen ayırmayı görmedi (ölçüm kör)");
            int sayi = Ayirmalar(govde);
            Debug.Log("[DD-TEST] wp095.kamera gc pozitif=" + pozitif + " govde=" + sayi);
            Assert.AreEqual(0, sayi, "tepeden kamera sıcak yolu ayırma yaptı");
        }

        /// <summary>GC.Alloc samples of one synchronous call on this thread (the AllocatingGCMemory constraint's recorder, as a count).</summary>
        private static int Ayirmalar(Action action)
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }

        // ================================================================ belirlenimcilik

        /// <summary>O3: aynı girdi dizisi iki kamerada aynı düğüm dizisini üretir (rastgelelik yok; replay farkı 0).</summary>
        [Test]
        public void Belirlenimcilik_AyniGirdiAyniKareler()
        {
            var ikiz = new TopDownFeelCamera { Profile = _profil };
            WeaponFeelTD lmg = _profil.Weapon(ClassicWeaponId.Lmg1);
            Vector3 t1 = Vector3.zero, t2 = Vector3.zero;
            for (int kare = 0; kare < 180; kare++)
            {
                if (kare % 5 == 0)
                {
                    _kam.Shot(lmg, new Vector3(1f, 0f, 2f), false);
                    ikiz.Shot(lmg, new Vector3(1f, 0f, 2f), false);
                }

                if (kare == 40)
                {
                    _kam.Explosion(new Vector3(4f, 0f, 0f), Vector3.zero);
                    ikiz.Explosion(new Vector3(4f, 0f, 0f), Vector3.zero);
                }

                _kam.ApplyKicks(ref t1, _rot);
                ikiz.ApplyKicks(ref t2, _rot);
                _kam.TickEnvelopes(Adim, BakisYumusatma);
                ikiz.TickEnvelopes(Adim, BakisYumusatma);
                _kam.Compose(_rot, Vector3.zero, 0f, t1, 0f, 1f, false, Mesafe, out Vector3 a, out float ra);
                ikiz.Compose(_rot, Vector3.zero, 0f, t2, 0f, 1f, false, Mesafe, out Vector3 b, out float rb);
                Assert.AreEqual(a, b, "kare " + kare + " farklı");
                Assert.AreEqual(ra, rb, 0f);
                _kam.AdvanceTrauma(Adim);
                ikiz.AdvanceTrauma(Adim);
            }
        }
    }
}
