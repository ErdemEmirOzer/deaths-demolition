using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-093 × WP-092 · the pure rules: S-05's lamp names parse to the right kind; the v002 moment table carries the `s07` numbers
    /// (centres, mist ceilings / α, swing amplitudes, the M3 door and pocket); the moment caps (≤ 30 petals per gust, ≤ 50 live, 15–30
    /// motes, ≤ 40 curtain drops, ≤ 12 curtain rings, ≤ 16 footstep rings); the shoji emission peaks at v ≤ 0.78; flames ≥ 140.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093LayoutEditTests
    {
        [Test]
        public void LambaAdi_TurOkunur()
        {
            Assert.IsTrue(LanternFlames.ParseLampName("Lamp_M1_L01_pool", out string id, out LampKind kind));
            Assert.AreEqual("M1_L01", id);
            Assert.AreEqual(LampKind.Pool, kind);
            Assert.IsTrue(LanternFlames.ParseLampName("Lamp_L-K1-7_pool", out id, out kind));
            Assert.AreEqual("L-K1-7", id);
            Assert.IsTrue(LanternFlames.ParseLampName("Lamp_L-B1-1_baked", out id, out kind));
            Assert.AreEqual(LampKind.Baked, kind);
            Assert.IsTrue(LanternFlames.ParseLampName("Lamp_M7_L03_emissive", out _, out kind));
            Assert.AreEqual(LampKind.Emissive, kind);
            Assert.IsTrue(LanternFlames.ParseLampName("Lamp_L-B6-2_unlit", out _, out kind));
            Assert.AreEqual(LampKind.Unlit, kind);
            foreach (string bad in new[] { "Lamp_", "Lamp__pool", "Lamp_X_lit", "SM_andon_V01", "M8_P01_andon", "", null })
                Assert.IsFalse(LanternFlames.ParseLampName(bad, out _, out _), (bad ?? "null") + " sözleşme lambası değil");
        }

        [Test]
        public void AlevTavani_LambaVeToro()
        {
            Assert.GreaterOrEqual(LanternFlames.MaxFlames, 130 + 7, "130 lamba + 7 tōrō");
            Assert.AreEqual(6, LanternFlames.MaxLanternLights, "≤ 6 havuz ışığı");
            Assert.LessOrEqual(LanternFlames.ShojiEmission.maxColorComponent * (1f + LanternFlames.LightFlickerAmplitude), 0.78f + 1e-4f, "shoji v ≤ 0,78");
            Color s = LanternFlames.ShojiEmission;
            Assert.AreEqual(0xE8 / (float)0xC8, s.r / s.g, 0.01f, "#E8C89A tonu");
            Assert.LessOrEqual(LanternFlames.ToroDriftRadius * 1.25f, 0.4f, "tōrō sürüklenme r ≤ 0,4 m");
            Assert.That(LanternFlames.ToroDriftSpeed, Is.InRange(0.02f, 0.04f), "2–4 cm/s");
            Assert.AreEqual(0.01f, LanternFlames.ToroBobMetres, 1e-5f);
            Assert.AreEqual(0.3f, LanternFlames.ToroBobHz, 1e-5f);
            Assert.AreEqual(5f, LanternFlames.ToroYawDegrees, 1e-5f);
            Assert.AreEqual(2.0f, LanternFlames.AxisStreakLength, 1e-5f);
            Assert.AreEqual(0.12f, LanternFlames.AxisStreakWidth, 1e-5f);
            Assert.AreEqual(0.4f, LanternFlames.AxisStreakAlpha, 1e-5f);
            Assert.AreEqual(0.6f, LanternFlames.ToroStreakLength, 1e-5f);
            Assert.AreEqual(0.15f, LanternFlames.ToroStreakWidth, 1e-5f);
            Assert.IsTrue(LanternFlames.IsPaperLantern("lantern_chochin") && LanternFlames.IsPaperLantern("SM_lantern_tsuri_V02"));
            Assert.IsFalse(LanternFlames.IsPaperLantern("lantern_oribe"));
        }

        [Test]
        public void AnTablosu_S07Sayilari()
        {
            MomentSpec[] m = WorldMoments.V6;
            Assert.AreEqual(8, m.Length, "M1–M8");
            for (int i = 0; i < m.Length; i++)
            {
                Assert.AreEqual("M" + (i + 1), m[i].Id);
                Assert.Greater(m[i].Radius, 0f);
                if (m[i].Mist != MomentMist.None)
                {
                    Assert.LessOrEqual(m[i].MistAlpha, MomentVfxSystem.MistAlphaCap, m[i].Id + " sis α ≤ 0,12");
                    Assert.LessOrEqual(m[i].MistTop, m[i].Mist == MomentMist.Water ? 0.4f : 0.3f, m[i].Id + " sis tavanı");
                }
            }

            MomentSpec m1 = m[0], m3 = m[2], m4 = m[3], m5 = m[4], m6 = m[5], m7 = m[6], m8 = m[7];
            Assert.AreEqual(new Vector2(-45f, 3f), m1.Center);
            Assert.AreEqual(MomentMist.Lane, m1.Mist);
            Assert.Greater(m1.MistDrift.x, 0f, "M1 sisi +x akar");
            Assert.AreEqual(4f, m1.SwingDegrees, 1e-5f, "M1 chōchin ±4°");
            Assert.IsTrue(m[1].Hanafubuki, "M2 hanafubuki");
            Assert.IsTrue(m3.Door);
            Assert.AreEqual(MomentMist.Pocket, m3.Mist);
            Assert.AreEqual(new Vector4(45f, 52f, 52f, 54.5f), m3.MistRect, "M3 soğuk cep x45–52 z52–54,5");
            Assert.LessOrEqual(m3.MistAlpha, 0.10f, "M3 cep α ≤ 0,10");
            Assert.AreEqual(2, m3.EmissiveSparks, "M3 aplikte 0–2 kıvılcım");
            Assert.IsTrue(m4.FootstepRings && m4.AxisStreaks, "M4 adım halkası + eksen şeritleri");
            Assert.AreEqual(MomentDrips.Curtain, m5.Drips);
            Assert.AreEqual(2.5f, m5.DripRadius, 1e-5f, "M5 perde r 2,5 m");
            Assert.AreEqual(3f, m5.SwingDegrees, 1e-5f, "M5 tsuri ±3°");
            Assert.IsTrue(m5.Shoji && m6.Shoji, "R1 güney ve R4 shoji");
            Assert.AreEqual(5f, m7.SwingDegrees, 1e-5f, "M7 ±5°");
            Assert.IsTrue(m7.HaloEmissive, "M7 fener başına hale");
            Assert.AreEqual(MomentMist.Water, m8.Mist);
            Assert.LessOrEqual(m8.MistAlpha, 0.10f, "M8 su sisi α ≤ 0,10");
            Assert.AreEqual(0.08f, WorldMoments.HaloAlphaFor("M1_L02", 0.12f), 1e-5f, "M1_L02 hale α 0,08");
            Assert.AreEqual(0.12f, WorldMoments.HaloAlphaFor("M7_L01", 0.12f), 1e-5f);
            CollectionAssert.Contains(AmbientVfxSystem.V6SmokeSources, new Vector2(-32f, -36f), "M6 çatı deliği dumanı (−32, −36)");
        }

        [Test]
        public void AnTavanlari_Kademe()
        {
            Assert.AreEqual(30, MomentVfxSystem.MaxPetalsPerGust, "rüzgâr cephesi başına ≤ 30");
            Assert.LessOrEqual(MomentVfxSystem.CapFor(MomentVfxSystem.Layer.Petals, VfxTierLevel.Cinematic), 50, "karede ≤ 50 yaprak");
            Assert.That(MomentVfxSystem.CapFor(MomentVfxSystem.Layer.Motes, VfxTierLevel.Performance), Is.InRange(15, 30));
            Assert.That(MomentVfxSystem.CapFor(MomentVfxSystem.Layer.Motes, VfxTierLevel.Cinematic), Is.InRange(15, 30), "kapı zerresi 15–30");
            Assert.AreEqual(40, MomentVfxSystem.MaxCurtainDrops, "perde ≤ 40 damla");
            Assert.AreEqual(12, MomentVfxSystem.MaxCurtainRings, "perde adım halkası ≤ 12");
            Assert.AreEqual(16, MomentVfxSystem.MaxFootstepRings, "adım halkası ≤ 16");
            Assert.AreEqual(0.6f, MomentVfxSystem.FootstepRingLife, 1e-5f);
            Assert.LessOrEqual(MomentVfxSystem.ShaftAlphaCap, 0.12f, "kapı kartı α ≤ 0,12");
            Assert.LessOrEqual(MomentVfxSystem.DripAlpha, 0.25f, "§5 koridor bandı α ≤ 0,25");
            for (int l = 0; l < MomentVfxSystem.LayerCount; l++)
            {
                var layer = (MomentVfxSystem.Layer)l;
                Assert.LessOrEqual(MomentVfxSystem.CapFor(layer, VfxTierLevel.Performance), MomentVfxSystem.CapFor(layer, VfxTierLevel.High));
                Assert.LessOrEqual(MomentVfxSystem.CapFor(layer, VfxTierLevel.High), MomentVfxSystem.CapFor(layer, VfxTierLevel.Cinematic));
            }
        }
    }
}
