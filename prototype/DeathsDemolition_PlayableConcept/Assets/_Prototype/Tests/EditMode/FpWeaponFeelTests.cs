using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 FP weapon feel (C-00 26.09) · the pure layer: rest is exactly the fpView pose, the A4.2 view-model kick
    /// (peak, recovery, ADS scaling, auto stacking), A4.1 sway (gain per weight class, rotation cap, ADS), the view-model bob,
    /// reduced motion off, the ADS ease and FOV factors, the kick table in step with S-07's CameraModesFeel rows.
    /// </summary>
    [Category("FPTP")]
    [TestFixture]
    [Category("WP069")]
    public sealed class FpWeaponFeelTests
    {
        private const float Dt = 1f / 60f;

        private static void Run(FpWeaponFeel f, float seconds, float yawRate = 0f, float pitchRate = 0f, float speed = 0f, float ads = 0f,
            FeelWeight w = FeelWeight.W2, bool reduced = false)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Dt) f.Tick(Dt, yawRate, pitchRate, speed, ads, w, reduced);
        }

        private static float RotDeg(Quaternion q) => Quaternion.Angle(Quaternion.identity, q);

        private static float Signed(float a) => a > 180f ? a - 360f : a;

        [Test]
        public void Dinlenme_OfsetTamSifir_FpViewDegismez()
        {
            var f = new FpWeaponFeel();
            Run(f, 2f);
            Assert.AreEqual(Vector3.zero, f.Position);
            Assert.AreEqual(0f, RotDeg(f.Rotation), 1e-4f);
            Assert.AreEqual(0f, f.AdsEased);
        }

        [Test]
        public void Kick_ar1_A42Tepesi_VeToparlanma()
        {
            var f = new FpWeaponFeel();
            f.Shot("ar_1", FeelWeight.W2, 0f, 1f, false);
            f.Tick(1e-5f, 0f, 0f, 0f, 0f, FeelWeight.W2, false);   // the peak, before the spring moves
            Assert.AreEqual(-0.030f, f.Position.z, 0.0005f, "geri tepme 3,0 cm");
            Assert.AreEqual(0.006f, f.Position.y, 0.0003f, "yukarı 0,6 cm");
            Assert.AreEqual(-2.2f, Signed(f.Rotation.eulerAngles.x), 0.05f, "namlu yukarı 2,2°");
            Run(f, 0.11f * 1.6f);
            Assert.Less(f.Position.magnitude, 0.003f, "toparlanma (VM top. 0,11 s)");
            Assert.Less(RotDeg(f.Rotation), 0.25f);
        }

        /// <summary>
        /// WP-069 M4 (C-00 01.10, S-07 AD r4): rl_1's view-model kick is fed over ≈ 3 frames eased in — the firing frame shows a
        /// small share (the rocket leaves the un-kicked muzzle in that frame), the whole kick lands by the ramp's end (the same
        /// total), a second shot mid-ramp first lands the rest; ar_1 stays instant.
        /// </summary>
        [Test]
        public void M4_RoketTekmesi_UcKareyeYayilir_AtisKaresiKucuk_ToplamAyni_Ar1Aninda()
        {
            const float frame = 1f / 60f;
            FpWeaponFeel.VmKick k = FpWeaponFeel.KickFor("rl_1");
            var f = new FpWeaponFeel();
            f.Shot("rl_1", FeelWeight.W4, 0f, 1f, false);
            f.Tick(frame, 0f, 0f, 0f, 0f, FeelWeight.W4, false);
            Assert.Less(-f.Position.z, 0.2f * k.BackCm * 0.01f, "atış karesinde geri tepme küçük pay");
            Assert.Greater(f.KickPending, 0.5f, "tekmenin çoğu sonraki karelerde");
            float peak = -f.Position.z;
            for (int i = 0; i < 4; i++) { f.Tick(frame, 0f, 0f, 0f, 0f, FeelWeight.W4, false); peak = Mathf.Max(peak, -f.Position.z); }
            Assert.AreEqual(0f, f.KickPending, 1e-6f, "rampa 3 karede biter");
            Assert.Greater(peak, 0.85f * k.BackCm * 0.01f, "toplam tepme aynı (yay yalnız 3 karede biraz geri çeker)");

            var ar = new FpWeaponFeel();
            ar.Shot("ar_1", FeelWeight.W2, 0f, 1f, false);
            Assert.AreEqual(0f, ar.KickPending, "ar_1 anında (rampa bir kareden kısa)");

            var iki = new FpWeaponFeel();
            iki.Shot("rl_1", FeelWeight.W4, 0f, 1f, false);
            iki.Tick(frame, 0f, 0f, 0f, 0f, FeelWeight.W4, false);
            float once = -iki.Position.z;
            iki.Shot("rl_1", FeelWeight.W4, 0f, 1f, false);   // mid-ramp: the rest of the first kick lands now
            iki.Tick(1e-5f, 0f, 0f, 0f, 0f, FeelWeight.W4, false);   // Position is published by Tick
            Assert.Greater(-iki.Position.z, once + 0.5f * k.BackCm * 0.01f, "ikinci atış öncekinin kalanını düşürmez");
        }

        [Test]
        public void Kick_ADS_GeriX04_PitchKorunur_DigerX06()
        {
            var hip = new FpWeaponFeel();
            var ads = new FpWeaponFeel();
            hip.Shot("sg_1", FeelWeight.W2, 0f, 1f, false);
            ads.Shot("sg_1", FeelWeight.W2, 1f, 1f, false);
            // M4 (C-00 01.10): sg_1's kick ramps over 0.03 s — sample once it has landed; the kick is linear, the ratios are the same.
            hip.Tick(FpWeaponFeel.KickRampMaxSeconds, 0f, 0f, 0f, 0f, FeelWeight.W2, false);
            ads.Tick(FpWeaponFeel.KickRampMaxSeconds, 0f, 0f, 0f, 1f, FeelWeight.W2, false);
            Assert.AreEqual(0.4f, ads.Position.z / hip.Position.z, 0.02f, "geri ×0,4");
            Assert.AreEqual(0.6f, ads.Position.y / hip.Position.y, 0.02f, "yukarı ×0,6");
            Assert.AreEqual(Signed(hip.Rotation.eulerAngles.x), Signed(ads.Rotation.eulerAngles.x), 0.05f, "pitch korunur");
        }

        [Test]
        public void Kick_Otomatik_Yigilir_Tavan18_BirakincaSifirlanir()
        {
            var f = new FpWeaponFeel();
            for (int i = 0; i < 12; i++)
            {
                f.Shot("lmg_1", FeelWeight.W3, 0f, i % 2 == 0 ? 1f : -1f, false);
                Run(f, 0.1f, 0f, 0f, 0f, 0f, FeelWeight.W3);
            }

            Assert.AreEqual(12, f.ShotsInString);
            FpWeaponFeel.VmKick k = FpWeaponFeel.KickFor("lmg_1");
            Assert.LessOrEqual(-f.Position.z, k.BackCm * 0.01f * FpWeaponFeel.AutoStackCap + 1e-4f, "geri tavanı 1,8×");
            Run(f, 0.3f, 0f, 0f, 0f, 0f, FeelWeight.W3);
            Assert.AreEqual(0, f.ShotsInString, "0,25 s ateşsiz → dizi sıfır");
        }

        [Test]
        public void Sway_100dps_W2_12cm_12derece_AgirlikVeADS()
        {
            float Lag(FeelWeight w, float ads)
            {
                var f = new FpWeaponFeel();
                Run(f, 1.5f, 100f, 0f, 0f, ads, w);
                return f.Position.x;
            }

            Assert.AreEqual(-0.012f, Lag(FeelWeight.W2, 0f), 0.0006f, "1,2 cm @ 100°/s (bakışın tersine)");
            Assert.AreEqual(-0.012f * 0.8f, Lag(FeelWeight.W1, 0f), 0.0006f, "W1 ×0,8");
            Assert.AreEqual(-0.012f * 1.4f, Lag(FeelWeight.W3, 0f), 0.0008f, "W3 ×1,4");
            Assert.AreEqual(-0.012f * 0.35f, Lag(FeelWeight.W2, 1f), 0.0004f, "ADS ×0,35");

            var g = new FpWeaponFeel();
            Run(g, 1.5f, 300f, -300f, 0f, 0f, FeelWeight.W4);
            Vector3 e = g.Rotation.eulerAngles;
            Assert.LessOrEqual(Mathf.Abs(Signed(e.y)), FpWeaponFeel.SwayRotCapDeg + 0.05f, "dönüş tavanı 2°");
            Assert.LessOrEqual(Mathf.Abs(Signed(e.x)), FpWeaponFeel.SwayRotCapDeg + 0.05f);
            Run(g, 1.5f, 0f, 0f, 0f, 0f, FeelWeight.W4);
            Assert.Less(g.Position.magnitude, 0.0005f, "bakış durunca yerine döner");
        }

        [Test]
        public void Bob_Yuruyus_KameraBandiX15_DuruncaSoner_ADSx02()
        {
            float MaxVert(float speed, float ads)
            {
                var f = new FpWeaponFeel();
                Run(f, 0.5f, 0f, 0f, speed, ads);
                float m = 0f;
                for (int i = 0; i < 120; i++)
                {
                    f.Tick(Dt, 0f, 0f, speed, ads, FeelWeight.W2, false);
                    m = Mathf.Max(m, -f.Position.y);
                }

                return m;
            }

            float walk = MaxVert(CameraModesFeel.BobWalkSpeed, 0f);
            Assert.AreEqual(CameraModesFeel.BobWalk.VerticalCm * FpWeaponFeel.VmBobWalkScale * 0.01f, walk, 0.002f, "yürüyüş dikey ≈ 1,4 × 1,5 cm");
            Assert.AreEqual(walk * FpWeaponFeel.VmBobAdsScale, MaxVert(CameraModesFeel.BobWalkSpeed, 1f), 0.001f, "ADS ×0,2");
            Assert.AreEqual(0f, MaxVert(0.2f, 0f), 1e-5f, "0,35 m/s altı bob yok");
        }

        [Test]
        public void AzaltilmisHareket_SwayBobKick_Kapali()
        {
            var f = new FpWeaponFeel();
            for (int i = 0; i < 5; i++)
            {
                f.Shot("rl_1", FeelWeight.W4, 0f, 1f, true);
                Run(f, 0.2f, 200f, 80f, 6f, 0f, FeelWeight.W4, true);
            }

            Assert.AreEqual(Vector3.zero, f.Position);
            Assert.AreEqual(0f, RotDeg(f.Rotation), 1e-4f);
        }

        [Test]
        public void ADS_Egrisi_UclarVeCikis_FovCarpanlari()
        {
            Assert.AreEqual(0f, FpWeaponFeel.EaseAds(0f));
            Assert.AreEqual(1f, FpWeaponFeel.EaseAds(1f));
            float prev = 0f;
            for (int i = 1; i <= 20; i++)
            {
                float v = FpWeaponFeel.EaseAds(i / 20f);
                Assert.GreaterOrEqual(v, prev - 1e-5f, "monoton");
                prev = v;
            }

            Assert.Greater(FpWeaponFeel.EaseAds(0.5f), 0.85f, "ease-out (0,22, 1, 0,36, 1)");
            Assert.AreEqual(0.85f, FpWeaponFeel.AdsWorldFovScale(WeaponClass.AssaultRifle));
            Assert.AreEqual(0.85f, FpWeaponFeel.AdsWorldFovScale(WeaponClass.Smg));
            Assert.AreEqual(0.85f, FpWeaponFeel.AdsWorldFovScale(WeaponClass.Lmg));
            Assert.AreEqual(0.90f, FpWeaponFeel.AdsWorldFovScale(WeaponClass.Shotgun));
            Assert.AreEqual(0.45f, FpWeaponFeel.AdsWorldFovScale(WeaponClass.Sniper));
            Assert.AreEqual(0.80f, FpWeaponFeel.AdsWorldFovScale(WeaponClass.Launcher));
        }

        [Test]
        public void KickTablosu_S07GeriDegerleriyleAyni()
        {
            foreach (CameraWeaponFeelRow row in CameraModesFeel.Weapons)
            {
                Assert.AreEqual(row.VmBackCm, FpWeaponFeel.KickFor(row.WeaponId).BackCm, 1e-4f, row.WeaponId + ": VM geri cm S-07 satırıyla aynı değil");
            }
        }
    }
}
