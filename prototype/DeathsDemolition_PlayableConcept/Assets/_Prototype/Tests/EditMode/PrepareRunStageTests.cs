using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Weapons;
using DD.Prototype.UI;
using DD.Prototype.UI.EditorTools;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// R4 WP-106 P1 · Prepare Run's weapon stage, the rules that need no frame: the target's size, the 80 mm lens, AD M1's
    /// orbit and its shares of the well, the clamps, the zoom and wheel steps, the one-scale fit, the seats on the post caps
    /// (checked against the imported AP-021 meshes), the muzzle's rest heading along the posts, the id table, the json's lights
    /// and AD M2's panel wash, S1's mirror ([−x, z, −y]), the pad's view controls through UiInput (a stub source), the imported
    /// set when it is there, and the stage file's hand-made motion count (T6's patterns).
    /// </summary>
    [TestFixture]
    [Category("WP106")]
    public sealed class PrepareRunStageTests
    {
        private const float Eps = 1e-3f;

        // ------------------------------------------------------------------ target and lens

        [Test]
        public void HedefBoyutu_KisaKenarEnCok1024_UzunKenarEnCok2048_EnAz16()
        {
            Assert.AreEqual(new Vector2Int(640, 360), PrepareRunStage.TargetSize(new Vector2(640f, 360f), PrepareRunStage.MaxShortSide));
            Assert.AreEqual(new Vector2Int(1820, 1024), PrepareRunStage.TargetSize(new Vector2(2560f, 1440f), PrepareRunStage.MaxShortSide), "kısa kenar 1024'e iner, oran korunur");
            Vector2Int tall = PrepareRunStage.TargetSize(new Vector2(600f, 4000f), PrepareRunStage.MaxShortSide);
            Assert.AreEqual(PrepareRunStage.MaxLongSide, tall.y, "uzun kenar 2048'i aşmaz");
            Assert.AreEqual(307, tall.x, "oran korunur");
            Assert.AreEqual(new Vector2Int(16, 16), PrepareRunStage.TargetSize(Vector2.zero, PrepareRunStage.MaxShortSide), "boş yerleşim ilk karede 16²");
        }

        [Test]
        public void Lens80mm_SensorAuto_YatayCerceveGenisligeOturur()
        {
            Assert.AreEqual(80f, PrepareRunStage.LensMm, "AD M1: 80 mm");
            float well = PrepareRunStage.VerticalFov(PrepareRunStage.LensMm, PrepareRunStage.SensorMm, WellAspect);
            Assert.AreEqual(20.3f, well, 0.05f, "615 × 490 kuyu: 36 mm yatay → dikey 20.3°");
            Assert.AreEqual(25.36f, HorizontalFov(WellAspect), 0.05f, "yatay 25.4°");
            float square = PrepareRunStage.VerticalFov(PrepareRunStage.LensMm, PrepareRunStage.SensorMm, 1f);
            Assert.AreEqual(25.36f, square, 0.05f, "kare: 36 mm dikey");
            Assert.AreEqual(square, PrepareRunStage.VerticalFov(PrepareRunStage.LensMm, PrepareRunStage.SensorMm, 0.5f), 1e-4f, "dikey çerçeve: sensör uzun kenara (dikeye) oturur");
        }

        // ------------------------------------------------------------------ the orbit

        [Test]
        public void IlkPoz_AdM1Kamerasi_AynaliAzimut()
        {
            Assert.AreEqual(4.4f, PrepareRunStage.InitialDistance, Eps, "AD M1: 4.4 m");
            Assert.AreEqual(10f, PrepareRunStage.InitialPitch, Eps, "AD M1: 10°");
            Assert.AreEqual(-28.6f, PrepareRunStage.InitialAzimuth, Eps, "AP-040'ın 28.6°'si aynalı (S1)");
            Assert.Less((PrepareRunStage.OrbitTarget - new Vector3(0f, 0.94f, 0f)).magnitude, Eps, "hedef: kapaklara oturan silahların merkezi");
            Assert.Less((PrepareRunStage.InitialCamera - new Vector3(-2.07f, 1.70f, 3.80f)).magnitude, 0.01f, "AD M1 kamerası, alçak hedefte: " + PrepareRunStage.InitialCamera);
            Assert.That(PrepareRunStage.InitialPitch, NUnit.Framework.Is.InRange(PrepareRunStage.PitchMin, PrepareRunStage.PitchMax));
            Assert.That(PrepareRunStage.InitialDistance, NUnit.Framework.Is.InRange(PrepareRunStage.DistanceMin, PrepareRunStage.DistanceMax));
            Assert.AreEqual(30f, PrepareRunStage.PitchMax, "AD M1: eğim tavanı 30°");
            Assert.AreEqual(3.0f, PrepareRunStage.DistanceMin, Eps);
            Assert.AreEqual(5.6f, PrepareRunStage.DistanceMax, Eps);

            Vector3 offset = PrepareRunStage.InitialCamera - PrepareRunStage.OrbitTarget;
            Assert.AreEqual(PrepareRunStage.InitialAzimuth, PrepareRunStage.AzimuthOf(offset), 0.01f, "yörünge geri döner");
            Assert.AreEqual(PrepareRunStage.InitialPitch, PrepareRunStage.PitchOf(offset), 0.01f);
            Assert.AreEqual(PrepareRunStage.InitialDistance, offset.magnitude, Eps);
        }

        [Test]
        public void Kelepceler_EgimMesafeDonus()
        {
            Assert.AreEqual(PrepareRunStage.PitchMin, PrepareRunStage.ClampPitch(-40f));
            Assert.AreEqual(PrepareRunStage.PitchMax, PrepareRunStage.ClampPitch(90f));
            Assert.AreEqual(20f, PrepareRunStage.ClampPitch(20f));
            Assert.AreEqual(PrepareRunStage.DistanceMin, PrepareRunStage.ClampDistance(0.5f));
            Assert.AreEqual(PrepareRunStage.DistanceMax, PrepareRunStage.ClampDistance(50f));
            Assert.AreEqual(10f, PrepareRunStage.WrapYaw(370f), Eps, "dönüş serbest, (−180, 180]'e katlanır");
            Assert.AreEqual(170f, PrepareRunStage.WrapYaw(-190f), Eps);
            Assert.AreEqual(180f, Mathf.Abs(PrepareRunStage.WrapYaw(180f)), Eps);
        }

        [Test]
        public void Yakinlasma_AdimVeTekerlek()
        {
            float d = PrepareRunStage.InitialDistance;
            Assert.AreEqual(d / PrepareRunStage.ZoomStep, PrepareRunStage.ZoomedDistance(d, 1f), Eps, "+1 adım yakınlaşır");
            Assert.AreEqual(d * PrepareRunStage.ZoomStep, PrepareRunStage.ZoomedDistance(d, -1f), Eps, "−1 adım uzaklaşır (4.4 × 1.12 = 4.93 < 5.6)");
            float near = PrepareRunStage.DistanceMin * 1.05f;
            Assert.AreEqual(PrepareRunStage.DistanceMin, PrepareRunStage.ZoomedDistance(near, 1f), Eps, "sınırın bir adım içinden: yakın uçta durur");
            Assert.AreEqual(PrepareRunStage.DistanceMin, PrepareRunStage.ZoomedDistance(d, 100f), Eps);
            Assert.AreEqual(PrepareRunStage.DistanceMax, PrepareRunStage.ZoomedDistance(d, -100f), Eps);

            Assert.AreEqual(1f, PrepareRunStage.ScrollSteps(6f), Eps, "Input System UI modülü: Windows tekerlek çentiği 6 satır = 1 adım");
            Assert.AreEqual(-1f, PrepareRunStage.ScrollSteps(-6f), Eps);
            Assert.AreEqual(PrepareRunStage.MaxScrollSteps, PrepareRunStage.ScrollSteps(600f), Eps, "tek olay en çok 2 adım");
            Assert.Greater(PrepareRunStage.ScrollSteps(1f), 0f, "eski modülün 1 satırı da yakınlaştırır");
        }

        // ------------------------------------------------------------------ the weapon

        [Test]
        public void Sigdirma_TekUniformOlcek_SinifUzunlugu_YukseklikTavani()
        {
            float rifle = PrepareRunStage.FitScale(new Vector3(0.1f, 0.22f, 1.2f), 0.95f, PrepareRunStage.MaxWeaponHeight);
            Assert.AreEqual(0.95f / 1.2f, rifle, 1e-4f, "namlu ekseni sınıf uzunluğuna");
            float sideways = PrepareRunStage.FitScale(new Vector3(1.2f, 0.22f, 0.1f), 0.95f, PrepareRunStage.MaxWeaponHeight);
            Assert.AreEqual(rifle, sideways, 1e-4f, "yatay uzun kenar (x ya da z)");
            float tall = PrepareRunStage.FitScale(new Vector3(0.1f, 0.8f, 0.9f), 0.95f, PrepareRunStage.MaxWeaponHeight);
            Assert.AreEqual(PrepareRunStage.MaxWeaponHeight / 0.8f, tall, 1e-4f, "boy tavanı bağlar");
            Assert.AreEqual(1f, PrepareRunStage.FitScale(Vector3.zero, 0.95f, PrepareRunStage.MaxWeaponHeight), "boş sınırlar: ölçek 1");
        }

        [Test]
        public void Oturtma_YedekDuzKapakDuzleminde_KoltukTablosuOlcekBekcisi()
        {
            var bounds = new Bounds(new Vector3(0.01f, 0.05f, 0.3f), new Vector3(0.1f, 0.3f, 1.2f));
            Vector3 seat = PrepareRunStage.SeatOffset(bounds, 0.5f);
            Assert.AreEqual(-0.005f, seat.x, 1e-5f);
            Assert.AreEqual(-0.15f, seat.z, 1e-5f);
            Vector3 foot = bounds.min * 0.5f + seat;
            Assert.AreEqual(PrepareRunStage.CapBelowSocket, foot.y, 1e-5f, "yedek: en alt nokta kapakların düzleminde (soketin 4.6 cm altı)");
            Assert.AreEqual(-0.046f, PrepareRunStage.CapBelowSocket, 1e-4f);

            Assert.IsTrue(PrepareRunStage.TrySeat("ar_1", 0.7917f, out PrepareRunStage.Seat ar), "ar_1 koltuğu");
            Assert.IsFalse(PrepareRunStage.TrySeat("ar_1", 0.80f, out PrepareRunStage.Seat stale), "ölçek tutmazsa (yeniden dışa aktarım) koltuk yok");
            Assert.AreEqual("ar_1", stale.Key, "eski koltuk uyarı için döner");
            Assert.IsFalse(PrepareRunStage.TrySeat("blockout", 1f, out PrepareRunStage.Seat none));
            Assert.IsNull(none.Key);

            var model = new Bounds(new Vector3(0f, 0.01f, 0.2f), new Vector3(0.07f, 0.2f, 1.2f));
            Vector3 position = PrepareRunStage.SeatPosition(model, ar.Scale, ar);
            Vector3 centre = position + PrepareRunStage.SeatRotation(ar) * (model.center * ar.Scale);
            Assert.Less((centre - new Vector3(0f, ar.Lift, ar.Slide)).magnitude, 1e-5f, "LOD0 merkezi (0, lift, slide)");

            // AD M1 acceptance: each weapon's centre in the 40-60 % band from the top of the well (±10 % of the frame's height at rest).
            float frameHeight = 2f * PrepareRunStage.InitialDistance * Mathf.Tan(PrepareRunStage.VerticalFov(PrepareRunStage.LensMm, PrepareRunStage.SensorMm, WellAspect) * 0.5f * Mathf.Deg2Rad);
            var keys = new HashSet<string>();
            foreach (PrepareRunStage.Seat s in PrepareRunStage.Seats)
            {
                float height = PrepareRunStage.WeaponSocketHeight + s.Lift;
                Assert.LessOrEqual(Mathf.Abs(height - PrepareRunStage.OrbitTarget.y) / frameHeight, 0.10f, s.Key + ": merkez " + height.ToString("F3") + " m dikey bandın dışında");
                Assert.IsTrue(keys.Add(s.Key), s.Key + " bir kez");
                Assert.IsTrue(WeaponIdMigration.TryGetRuntimeId(s.Key, out _), s.Key + " klasik silah");
                // S-09 12.10: rl_1 slides 0.1325 m (6.7 %) to lie on both caps - seated over centred; the rest stay within ±6 %.
                Assert.LessOrEqual(Mathf.Abs(s.Slide), s.Key == "rl_1" ? 0.14f : 0.12f, s.Key + ": kuyunun ±%6'sı (rl_1 %7)");
                Assert.LessOrEqual(Mathf.Abs(s.Tilt), 10f, s.Key + ": eğim ≤ 10°");
                if (s.Shim > 0f)
                {
                    Assert.AreEqual(s.Gap, s.Shim, 1e-4f, s.Key + ": takoz boşluğun yüksekliğinde");
                    Assert.AreEqual(1, Mathf.Abs(s.ShimPost), s.Key + ": takoz bir başlıkta (−1 arka, +1 ön)");
                    Assert.LessOrEqual(s.Shim, 0.08f, s.Key + ": takoz ≤ 8 cm");
                }
                else
                {
                    Assert.LessOrEqual(s.Gap, 0.001f, s.Key + ": takozsuz koltuk iki başlığa oturur");
                }
            }

            Assert.AreEqual(10, keys.Count, "on klasik");
        }

        [Test]
        public void SinifUzunluklari_AdM1Paylari_DinlenmeEnYakinEnUzak()
        {
            Assert.Less(PrepareRunStage.DisplayLength(WeaponClass.Smg), PrepareRunStage.DisplayLength(WeaponClass.Shotgun));
            Assert.Less(PrepareRunStage.DisplayLength(WeaponClass.Shotgun), PrepareRunStage.DisplayLength(WeaponClass.AssaultRifle));
            Assert.Less(PrepareRunStage.DisplayLength(WeaponClass.AssaultRifle), PrepareRunStage.DisplayLength(WeaponClass.Lmg));
            Assert.LessOrEqual(PrepareRunStage.DisplayLength(WeaponClass.Lmg), PrepareRunStage.DisplayLength(WeaponClass.Sniper));

            // AD M1 (L·f / (36·d), the sensor across the well): AR ≥ 45 %, SMG ≥ 38 % at rest; no class over 85 % at the nearest zoom.
            Assert.AreEqual(0.48f, Share(WeaponClass.AssaultRifle, PrepareRunStage.InitialDistance), 0.01f, "AR dinlenmede %48");
            Assert.AreEqual(0.40f, Share(WeaponClass.Smg, PrepareRunStage.InitialDistance), 0.01f, "SMG %40");
            Assert.AreEqual(0.56f, Share(WeaponClass.Sniper, PrepareRunStage.InitialDistance), 0.01f, "SR %56");
            Assert.GreaterOrEqual(Share(WeaponClass.AssaultRifle, PrepareRunStage.InitialDistance), 0.45f);
            Assert.GreaterOrEqual(Share(WeaponClass.Smg, PrepareRunStage.InitialDistance), 0.38f);
            Assert.AreEqual(0.38f, Share(WeaponClass.AssaultRifle, PrepareRunStage.DistanceMax), 0.01f, "AR en uzakta %38");
            foreach (WeaponClass c in Enum.GetValues(typeof(WeaponClass)))
            {
                Assert.LessOrEqual(Share(c, PrepareRunStage.DistanceMin), 0.85f, c + ": en yakın yakınlaşmada %85'i geçmez");
            }
        }

        [Test]
        public void Namlu_DayanakHattinda_IlkBakistaEkranSolunda_GorusDikgen()
        {
            Assert.Less((Quaternion.Euler(0f, PrepareRunStage.MountYaw, 0f) * Vector3.forward - Vector3.right).magnitude, 1e-4f, "namlu tablanın +X'inde: iki dayanağın hattı");

            Vector3 forward = PrepareRunStage.OrbitTarget - PrepareRunStage.InitialCamera;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 barrel = Quaternion.Euler(0f, PrepareRunStage.TurntableRestYaw, 0f) * (Quaternion.Euler(0f, PrepareRunStage.MountYaw, 0f) * Vector3.forward);
            Assert.Less(Vector3.Dot(barrel, right), -0.999f, "namlu ekranın soluna (AP-036 yan görünüşleri)");
            Assert.AreEqual(0f, Vector3.Dot(barrel, forward), 1e-3f, "namlu ilk bakışa dik: tam yan profil");
        }

        [Test]
        public void Anahtarlar_GocTablosuylaTutarli()
        {
            foreach (ClassicWeaponId id in Enum.GetValues(typeof(ClassicWeaponId)))
            {
                string key = PrepareRunStage.KeyOf(id);
                Assert.IsNotNull(key, id + " anahtarı");
                Assert.IsTrue(WeaponIdMigration.TryGetRuntimeId(key, out ClassicWeaponId back), key);
                Assert.AreEqual(id, back, key + " göç tablosuyla aynı kimliğe döner");
                Assert.IsTrue(PrepareRunStage.TryResolve(key, out ClassicWeaponId resolved));
                Assert.AreEqual(id, resolved);
            }

            Assert.IsTrue(PrepareRunStage.TryResolve("AssaultRifle", out ClassicWeaponId legacy), "M3 adı da çözülür");
            Assert.AreEqual(ClassicWeaponId.AssaultRifle, legacy);
            Assert.IsFalse(PrepareRunStage.TryResolve("sw_arc", out _), "Special kaideye çıkmaz");
            Assert.IsFalse(PrepareRunStage.TryResolve("nope", out _));
            Assert.IsFalse(PrepareRunStage.TryResolve(null, out _));
        }

        // ------------------------------------------------------------------ the light

        [Test]
        public void Isiklar_JsonDonusumuyleAyni_PanelYikamaSilahaDusmez_Siddet()
        {
            // preview/AP040_PrepareRun_v001.json lights[] → Unity through [−x, z, −y] (S1): the key light by the warm lantern at x +1.05.
            Vector3[] unityPositions = { new Vector3(1.05f, 1.47f, -0.205f), new Vector3(-1.05f, 1.77f, -0.205f), new Vector3(0f, 2.65f, -0.85f), new Vector3(0f, 2.6f, -0.5f) };
            Vector3[] unityAims = { new Vector3(0f, 0.94f, 0f), new Vector3(0f, 1.05f, 0f), new Vector3(0f, 0.85f, 0f), new Vector3(0f, 1.1f, -1.3f) };
            float[] kelvin = { 2800f, 6500f, 3400f, 3000f };
            Assert.AreEqual(4, PrepareRunStage.Lights.Length);
            for (int i = 0; i < 4; i++)
            {
                PrepareRunStage.StageLight l = PrepareRunStage.Lights[i];
                Assert.Less((unityPositions[i] - l.Position).magnitude, 1e-4f, l.Name + " konumu");
                Assert.Less((unityAims[i] - l.Aim).magnitude, 1e-4f, l.Name + " hedefi");
                Assert.AreEqual(kelvin[i], l.Kelvin, 1e-4f);
                Assert.That(l.Angle, NUnit.Framework.Is.InRange(30f, 120f));
            }

            Assert.AreEqual(1.3f, PrepareRunStage.StageExposure, Eps, "AD M2: pozlama 1.3");
            Assert.AreEqual(1.6f, PrepareRunStage.SpotCoverRadius, Eps, "AD M2: koni 1.6 m");
            Assert.AreEqual(105f / (Mathf.PI * Mathf.PI) * 1.3f, PrepareRunStage.Lights[0].Intensity, 1e-4f, "P / π² × pozlama");
            Assert.AreEqual(0.5f, PrepareRunStage.FillTrim, Eps, "C-00 M2 oranı: dolgu yarıda");
            Assert.AreEqual(62f / (Mathf.PI * Mathf.PI) * 1.3f * PrepareRunStage.FillTrim, PrepareRunStage.Lights[1].Intensity, 1e-4f, "dolgu × kırpma");
            Assert.AreEqual(1f, PrepareRunStage.Lights[0].Trim, Eps, "anahtar (P95) ve kontur dokunulmadı");
            Assert.AreEqual(1f, PrepareRunStage.Lights[2].Trim, Eps);
            Assert.AreEqual(1f, PrepareRunStage.Lights[3].Trim, Eps, "yıkama dokunulmadı");

            // The fill's cone misses the paper crop (w106-after3 m2-pil 880..920 × 320..360 px: the panel near (0.1, 1.7, −1.3)).
            PrepareRunStage.StageLight fill = PrepareRunStage.Lights[1];
            float paperOff = Vector3.Angle(fill.Aim - fill.Position, new Vector3(0.1f, 1.7f, -1.3f) - fill.Position);
            Assert.Greater(paperOff, fill.Angle * 0.5f, "dolgu panel kırpmasına düşmez: " + paperOff.ToString("F1") + "°");
            Assert.AreEqual(107f, PrepareRunStage.Lights[0].Angle, 1f, "anahtar koni ~107°");

            PrepareRunStage.StageLight wash = PrepareRunStage.Lights[3];
            Assert.AreEqual("PR_PanelWash", wash.Name);
            Assert.AreEqual(72f, wash.Angle, Eps, "AD M2: 72°");
            Assert.AreEqual(8f * 1.3f, wash.Intensity, 1e-3f, "8 (pozlama 1'de) × 1.3");
            Assert.IsFalse(wash.Shadows);
            Vector3 axis = (wash.Aim - wash.Position).normalized;
            float offAxis = Vector3.Angle(axis, new Vector3(0f, 1.05f, 0f) - wash.Position);
            Assert.Greater(offAxis, wash.Angle * 0.5f + 5f, "silah (0, 1.05, 0) panel yıkamasının konisi dışında: " + offAxis.ToString("F1") + "°");
            Assert.Less(Vector3.Angle(axis, new Vector3(0f, 1.1f, -1.3f) - wash.Position), 1f, "panelin ortasına bakar");

            Assert.IsTrue(PrepareRunStage.Lights[0].Shadows, "yalnız anahtar ışık gölge düşürür");
            Assert.IsFalse(PrepareRunStage.Lights[1].Shadows);
            Assert.IsFalse(PrepareRunStage.Lights[2].Shadows);
            Assert.AreEqual(1.4f, PrepareRunStageBuilder.GlowIntensity, Eps, "AD M2: fener parıltısı 1.4");
        }

        [Test]
        public void Ayna_TekDonusum_SicakFenerSolda_SogukSagda()
        {
            Assert.AreEqual(new Vector3(-1f, 3f, -2f), PrepareRunStage.BlenderToUnity(new Vector3(1f, 2f, 3f)), "[−x, z, −y]: Unity'nin FBX içe aktarımı");
            Assert.AreEqual(4, PrepareRunStage.Set.Length);
            Assert.AreEqual(new Vector3(0f, 0f, -1.3f), PrepareRunStage.Set[1].Position, "panel z −1.3");
            Assert.AreEqual(1.05f, PrepareRunStage.Set[2].Position.x, Eps, "sıcak anahtar fener V01 x +1.05");
            Assert.AreEqual(-1.05f, PrepareRunStage.Set[3].Position.x, Eps, "soğuk dolgu fener V02 x −1.05");
            Assert.AreEqual(PrepareRunStageBuilder.KeyLanternModel, PrepareRunStage.Set[2].Model);
            Assert.AreEqual(PrepareRunStageBuilder.FillLanternModel, PrepareRunStage.Set[3].Model);
            Assert.AreEqual(PrepareRunStageBuilder.PedestalModel, PrepareRunStage.Set[0].Model);
            Assert.AreEqual(PrepareRunStageBuilder.BackdropModel, PrepareRunStage.Set[1].Model);

            // From the first pose (AP-040 v001 PNG): the warm lantern on the left, the cool one on the right.
            Vector3 forward = PrepareRunStage.OrbitTarget - PrepareRunStage.InitialCamera;
            forward.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, forward.normalized);
            Assert.Less(Vector3.Dot(PrepareRunStage.Set[2].Position - PrepareRunStage.InitialCamera, right), 0f, "sıcak fener solda");
            Assert.Greater(Vector3.Dot(PrepareRunStage.Set[3].Position - PrepareRunStage.InitialCamera, right), 0f, "soğuk fener sağda");
        }

        // ------------------------------------------------------------------ the pad's view controls (UiInput)

        [Test]
        public void UiInput_GorunumEksenleri_CoreInputUzerinden_YalnizPadde()
        {
            IInputSource was = InputSourceRegistry.Current;
            var source = new StubViewSource { Enabled = true, Scheme = InputSchemeKind.Gamepad, Look = new Vector2(0.5f, -0.25f) };
            try
            {
                UiInput.ClearSimulatedViewInput();
                InputSourceRegistry.SetCurrent(source);
                Assert.AreEqual(new Vector2(0.5f, -0.25f), UiInput.ViewStick(), "sağ çubuk = Core/Input look stick");
                Assert.AreEqual(0f, UiInput.ViewZoomAxis());

                source.Fire = true;
                Assert.AreEqual(1f, UiInput.ViewZoomAxis(), "RT (Fire) yakınlaştırır");
                source.Focus = true;
                Assert.AreEqual(0f, UiInput.ViewZoomAxis(), "iki tetik birden: 0");
                source.Fire = false;
                Assert.AreEqual(-1f, UiInput.ViewZoomAxis(), "LT (Focus) uzaklaştırır");

                source.Scheme = InputSchemeKind.KeyboardMouse;
                Assert.AreEqual(0f, UiInput.ViewZoomAxis(), "farede aynı aksiyonlar sürükleme düğmeleri: yakınlaşma yok");

                source.Enabled = false;
                Assert.AreEqual(Vector2.zero, UiInput.ViewStick(), "askıdaki kaynak (yeniden bağlama) okunmaz");

                InputSourceRegistry.Clear();
                Assert.AreEqual(Vector2.zero, UiInput.ViewStick(), "gerçek kaynak yok: sıfır");
                Assert.AreEqual(0f, UiInput.ViewZoomAxis());
            }
            finally
            {
                InputSourceRegistry.SetCurrent(was);
            }
        }

        [Test]
        public void UiInput_GorunumTohumu_TestGirdisiVerir_Temizlenince_Cihaz()
        {
            try
            {
                UiInput.SimulateViewInput(new Vector2(2f, 0f), 3f);
                Assert.AreEqual(new Vector2(1f, 0f), UiInput.ViewStick(), "çubuk büyüklüğü ≤ 1");
                Assert.AreEqual(1f, UiInput.ViewZoomAxis(), "tetik ekseni −1..1");
            }
            finally
            {
                UiInput.ClearSimulatedViewInput();
            }

            Assert.AreEqual(Vector2.zero, UiInput.ViewStick(), "EditMode'da gerçek kaynak yok");
        }

        // ------------------------------------------------------------------ the imported set

        [Test]
        public void Sahne_IceAktarildiysa_SoketlerTemizlikKatmanYerlesim()
        {
            var prefab = Resources.Load<GameObject>(PrepareRunStage.StageResource);
            if (prefab == null)
            {
                Assert.Ignore("AP-040 Prepare Run seti içe aktarılmadı (PrepareRunStageBuilder.BuildCli -ddAp040Export …); sahne çıplak pivotla çalışır.");
            }

            Transform turntable = Find(prefab.transform, PrepareRunStage.TurntableSocket);
            Transform socket = Find(prefab.transform, PrepareRunStage.WeaponSocket);
            Assert.IsNotNull(turntable, "SOCKET_Turntable");
            Assert.IsNotNull(socket, "SOCKET_Weapon");
            Assert.IsTrue(socket.IsChildOf(turntable), "silah soketi rotorla döner");
            Assert.AreEqual(PrepareRunStage.WeaponSocketHeight, prefab.transform.InverseTransformPoint(socket.position).y, 0.005f, "SOCKET_Weapon 0.94 m");
            Assert.AreEqual(0.70f, prefab.transform.InverseTransformPoint(turntable.position).y, 0.005f, "SOCKET_Turntable 0.70 m");

            // AD S1: the builder places the set through the stage's one conversion (the stage re-places an older prefab too).
            foreach (PrepareRunStage.SetPiece piece in PrepareRunStage.Set)
            {
                Transform t = prefab.transform.Find(piece.Name);
                Assert.IsNotNull(t, piece.Name);
                Assert.Less((t.localPosition - piece.Position).magnitude, 1e-4f, piece.Name + " " + t.localPosition + " ≠ " + piece.Position + " (builder yeniden koşulmalı)");
            }

            Assert.IsEmpty(prefab.GetComponentsInChildren<LODGroup>(true), "yakın plan: LODGroup yok");
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "çarpışma yok");
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(PrepareRunStage.StageLayer, t.gameObject.layer, t.name + " sahne katmanında");
                Assert.IsFalse(t.name.EndsWith("_COL", StringComparison.Ordinal), t.name + " vekil gövde kaldırılmalı");
                Assert.IsFalse(t.name.EndsWith("_LOD1", StringComparison.Ordinal) || t.name.EndsWith("_LOD2", StringComparison.Ordinal), t.name + " LOD1/2 kaldırılmalı");
            }

            int renderers = 0;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                renderers++;
                foreach (Material m in r.sharedMaterials)
                {
                    Assert.IsNotNull(m, r.name + " malzemesi");
                    StringAssert.StartsWith("M_AP040_PR_", m.name, r.name + " sahnenin URP Lit malzemesi");
                    Assert.IsNotNull(m.GetTexture("_BaseMap"), m.name + " BaseColor atlası");
                    if (m.name == PrepareRunStageBuilder.GlowWarmMaterial)
                    {
                        Color expected = Mathf.CorrelatedColorTemperatureToRGB(2800f) * PrepareRunStageBuilder.GlowIntensity;
                        Assert.AreEqual(expected.maxColorComponent, m.GetColor("_EmissionColor").maxColorComponent, 0.01f, "AD M2 parıltısı 1.4 (builder yeniden koşulmalı)");
                    }
                }
            }

            Assert.GreaterOrEqual(renderers, 5, "LOD0: kaide tabanı + tablası, panel, iki fener");

            // C-00 12.10: the post shim - the +x cap block under the turntable, inactive, the shared material, its top at its origin.
            Transform shim = Find(prefab.transform, PrepareRunStage.ShimName);
            Assert.IsNotNull(shim, PrepareRunStage.ShimName + " (builder yeniden koşulmalı)");
            Assert.IsTrue(shim.IsChildOf(turntable), "takoz rotorla döner");
            Assert.IsFalse(shim.gameObject.activeSelf, "takoz yalnız ar_2 / smg_1'de açılır");
            Mesh shimMesh = shim.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(shimMesh);
            Assert.AreEqual(PrepareRunStage.PostCapLength, shimMesh.bounds.size.x, 0.004f, "başlığın boyu");
            Assert.AreEqual(PrepareRunStage.PostCapDepth, shimMesh.bounds.size.z, 0.004f, "başlığın derinliği");
            Assert.AreEqual(0.056f, shimMesh.bounds.size.y, 0.003f, "başlık bloğunun yüksekliği");
            Assert.AreEqual(0f, shimMesh.bounds.max.y, 0.001f, "üst yüz orijinde");
            Assert.AreEqual(PrepareRunStage.PostOffset, shim.localPosition.x, 0.003f, "+x başlığının üstünde");
            Assert.AreEqual(PrepareRunStage.PostCapTop - 0.70f, shim.localPosition.y, 0.003f);
            StringAssert.StartsWith("M_AP040_PR_Shared", shim.GetComponent<MeshRenderer>().sharedMaterial.name, "aynı malzeme");
        }

        // ------------------------------------------------------------------ AD M1 · the seats against the imported meshes

        /// <summary>
        /// Each library model as the stage builds it (fit + <see cref="PrepareRunStage.Seats"/>), its LOD0 triangles in the weapon
        /// mount, clipped to the two cap footprints: the lower underside lies on the caps (± 2 mm), the other within the seat's gap
        /// (+ 2 mm; ≤ 3 mm for a two-cap seat), the lowest point ≥ 0.71 m (over the disc) - the offline measure checked against
        /// Unity's own import (axes, scale, mirror).
        /// </summary>
        [Test]
        public void Koltuklar_IceAktarilmisMeshlerle_KapaklaraOturur()
        {
            var lib = Resources.Load<ClassicWeaponVisualLibrary>(ClassicWeaponVisualLibrary.ResourcePath);
            Assert.IsNotNull(lib, "DD_ClassicWeapons");
            int checkedModels = 0;
            foreach (ClassicWeaponVisualLibrary.Entry entry in lib.entries)
            {
                if (entry == null || !entry.HasModel) continue;
                GameObject go = ClassicWeaponVisualLibrary.Instantiate(entry, lib.material, null);
                try
                {
                    Transform root = go.transform;
                    Bounds bounds = AuthoredWeaponModel.LocalLod0Bounds(root);
                    WeaponDefinition def = Arsenal.Get(entry.id);
                    float scale = PrepareRunStage.FitScale(bounds.size, PrepareRunStage.DisplayLength(def != null ? def.weaponClass : WeaponClass.AssaultRifle), PrepareRunStage.MaxWeaponHeight);
                    Assert.IsTrue(PrepareRunStage.TrySeat(entry.rosterKey, scale, out PrepareRunStage.Seat seat),
                        entry.rosterKey + ": koltuk yok ya da ölçek " + scale.ToString("F4") + " ≠ " + seat.Scale.ToString("F4") + " (model değişti: seat.py yeniden)");

                    Matrix4x4 toMount = Matrix4x4.TRS(PrepareRunStage.SeatPosition(bounds, scale, seat), PrepareRunStage.SeatRotation(seat), Vector3.one * scale) * root.worldToLocalMatrix;
                    var tris = new List<Vector3>(60000);
                    float lowest = float.MaxValue;
                    foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (f.sharedMesh == null || AuthoredWeaponModel.LodIndexOf(f.name) != 0) continue;
                        Matrix4x4 m = toMount * f.transform.localToWorldMatrix;
                        Vector3[] v = f.sharedMesh.vertices;
                        int[] idx = f.sharedMesh.triangles;
                        for (int i = 0; i < idx.Length; i++)
                        {
                            Vector3 q = m.MultiplyPoint3x4(v[idx[i]]);
                            tris.Add(q);
                            lowest = Mathf.Min(lowest, q.y);
                        }
                    }

                    float rear = Underside(tris, -PrepareRunStage.PostOffset);
                    float front = Underside(tris, PrepareRunStage.PostOffset);
                    Assert.IsFalse(float.IsInfinity(rear) || float.IsInfinity(front), entry.rosterKey + ": bir kapağın üstünde silah yok");
                    // C-00 12.10: a seat with a shim lies on the shim's top over its cap (cap + shim) and on the bare other cap.
                    float rearSupport = PrepareRunStage.CapBelowSocket + (seat.ShimPost < 0 ? seat.Shim : 0f);
                    float frontSupport = PrepareRunStage.CapBelowSocket + (seat.ShimPost > 0 ? seat.Shim : 0f);
                    float rearGap = rear - rearSupport, frontGap = front - frontSupport;
                    float low = Mathf.Min(rearGap, frontGap), gap = Mathf.Abs(rearGap - frontGap);
                    Assert.AreEqual(0f, low, 0.002f, entry.rosterKey + ": alt yüz dayanakta (arka " + rear.ToString("F4") + ", ön " + front.ToString("F4") + ", takoz " + seat.Shim.ToString("F4") + ")");
                    Assert.LessOrEqual(gap, 0.003f, entry.rosterKey + ": öbür dayanağa boşluk " + gap.ToString("F4") + " m (≤ 1 cm kabul; iki dayanak)");
                    Assert.GreaterOrEqual(PrepareRunStage.WeaponSocketHeight + lowest, 0.71f - 0.001f, entry.rosterKey + ": en alt nokta disk üstünde");
                    TestContext.WriteLine(entry.rosterKey + " seat gap " + gap.ToString("F4") + " lowest " + (PrepareRunStage.WeaponSocketHeight + lowest).ToString("F3"));
                    checkedModels++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            Assert.AreEqual(10, checkedModels, "on klasik model");
        }

        // ------------------------------------------------------------------ T6 (hand-made motion) count

        [Test]
        public void SahneDosyasi_ElYapimiHareketSayaci_T6Desenleri()
        {
            string path = Path.Combine(Application.dataPath, "_Prototype", "UI", "Runtime", "Prepare", "PrepareRunStage.cs");
            Assert.IsTrue(File.Exists(path), path);
            string text = File.ReadAllText(path);
            string[] patterns = { @"Mathf\.Lerp|Color\.Lerp", @"unscaledDeltaTime", @"MoveTowards", @"new Color\(", @"void (Late)?Update\(\)" };
            int[] limit = { 0, 0, 0, 0, 1 };   // one LateUpdate: the pad, the auto turn on UiMotion's clock, the draw decision
            for (int i = 0; i < patterns.Length; i++)
            {
                Assert.LessOrEqual(Regex.Matches(text, patterns[i]).Count, limit[i], "PrepareRunStage.cs '" + patterns[i] + "'");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The preview well at 1080p (AD M1: 615 × 490 px).</summary>
        private const float WellAspect = 615f / 490f;

        private static float HorizontalFov(float aspect)
        {
            float v = PrepareRunStage.VerticalFov(PrepareRunStage.LensMm, PrepareRunStage.SensorMm, aspect) * 0.5f * Mathf.Deg2Rad;
            return 2f * Mathf.Atan(Mathf.Tan(v) * aspect) * Mathf.Rad2Deg;
        }

        /// <summary>A class's share of the well's width at <paramref name="distance"/> (its display length over the frame's width there).</summary>
        private static float Share(WeaponClass c, float distance)
        {
            float width = 2f * distance * Mathf.Tan(HorizontalFov(WellAspect) * 0.5f * Mathf.Deg2Rad);
            return PrepareRunStage.DisplayLength(c) / width;
        }

        /// <summary>The lowest y of the triangles (mount space, 3 points each) inside a cap's footprint centred at z = <paramref name="post"/>.</summary>
        private static float Underside(List<Vector3> tris, float post)
        {
            float z0 = post - PrepareRunStage.PostCapLength * 0.5f, z1 = post + PrepareRunStage.PostCapLength * 0.5f;
            float x0 = -PrepareRunStage.PostCapDepth * 0.5f, x1 = PrepareRunStage.PostCapDepth * 0.5f;
            float best = float.PositiveInfinity;
            var poly = new List<Vector3>(8);
            var next = new List<Vector3>(8);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if (Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < z0 || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > z1) continue;
                if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < x0 || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > x1) continue;
                poly.Clear();
                poly.Add(a);
                poly.Add(b);
                poly.Add(c);
                Clip(poly, next, 2, z0, true);
                Clip(poly, next, 2, z1, false);
                Clip(poly, next, 0, x0, true);
                Clip(poly, next, 0, x1, false);
                for (int k = 0; k < poly.Count; k++) best = Mathf.Min(best, poly[k].y);
            }

            return best;
        }

        /// <summary>Sutherland-Hodgman against one axis plane (keep ≥ or ≤ <paramref name="value"/>), in place.</summary>
        private static void Clip(List<Vector3> poly, List<Vector3> scratch, int axis, float value, bool keepGreater)
        {
            scratch.Clear();
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                bool inA = keepGreater ? a[axis] >= value : a[axis] <= value;
                bool inB = keepGreater ? b[axis] >= value : b[axis] <= value;
                if (inA) scratch.Add(a);
                if (inA != inB) scratch.Add(Vector3.Lerp(a, b, (value - a[axis]) / (b[axis] - a[axis])));
            }

            poly.Clear();
            poly.AddRange(scratch);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = Find(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>A device source with the look stick, the scheme and the two triggers' actions (Fire, Focus).</summary>
        private sealed class StubViewSource : IExtendedInputSource, IFocusInputSource, ILookInputSource
        {
            public bool Enabled;
            public InputSchemeKind Scheme;
            public Vector2 Look;
            public bool Fire;
            public bool Focus;

            public bool IsEnabled => Enabled;
            public Vector2 Move => Vector2.zero;
            public AimSourceKind AimKind => AimSourceKind.Stick;
            public Vector2 AimScreenPosition => Vector2.zero;
            public Vector2 AimStick => Look;
            public bool FireHeld => Fire;
            public bool FirePressed => false;
            public bool FireReleased => false;
            public bool ReloadPressed => false;
            public bool PausePressed => false;
            public bool DebugPanelPressed => false;
            public bool SpecialHeld => false;
            public bool SpecialPressed => false;
            public bool InteractPressed => false;
            public InputSchemeKind ActiveScheme => Scheme;
            public float StickDeadzone => 0.15f;
            public bool FocusHeld => Focus;
            public Vector2 LookDelta => Vector2.zero;
            public Vector2 LookStick => Look;
            public bool ShoulderSwapPressed => false;

#pragma warning disable 67
            public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67

            public void Enable()
            {
                Enabled = true;
            }

            public void Disable()
            {
                Enabled = false;
            }
        }
    }
}
