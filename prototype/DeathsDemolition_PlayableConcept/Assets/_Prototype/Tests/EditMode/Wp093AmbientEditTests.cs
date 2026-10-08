using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-093 Faz 1b (LOOK-BIBLE §5) · the pure numbers: the lantern flicker sample is smooth (two octaves 1.3 / 6.5 Hz, nothing over
    /// 8 Hz, in [−1, 1]) and drives the card, its scale and the light the same way (all rise with it: α ±10 %, scale ±8 %, light ±12 %;
    /// reduce-flashes scales them); spark caps and look; ring / smoke looks inside the readability caps (α ≤ 0.35 / 0.12, ring life
    /// ≤ 1.8 s); the smoke ray clearance; the CPU mirror of DD_GroundTiles' puddle field is a deterministic 0..1 field; the procedural
    /// sprites are empty at the edge.
    /// </summary>
    [TestFixture]
    [Category("WP093")]
    public sealed class Wp093AmbientEditTests
    {
        [Test]
        public void FenerSinyali_Yumusak_DortHertzAlti_Sinirli()
        {
            Assert.LessOrEqual(LanternFlames.FlickerMaxHz, 8f, "§5: > 8 Hz yok");
            Assert.LessOrEqual(LanternFlames.FlickerFastHz, LanternFlames.FlickerMaxHz);
            Assert.AreEqual(1f, LanternFlames.FlickerSlowWeight + LanternFlames.FlickerFastWeight, 1e-5f, "örnek [−1, 1]");
            // Cubic-eased value noise in [−1, 1]: slope ≤ 1.5 · 2 · f per octave, weighted.
            float maxSlope = 3f * (LanternFlames.FlickerSlowWeight * LanternFlames.FlickerSlowHz + LanternFlames.FlickerFastWeight * LanternFlames.FlickerFastHz);
            const float dt = 1f / 240f;
            for (int flame = 0; flame < LanternFlames.MaxFlames; flame++)
            {
                float prev = LanternFlames.FlickerSignal(flame, 100f);
                float lo = prev, hi = prev;
                for (int k = 1; k < 2400; k++)
                {
                    float s = LanternFlames.FlickerSignal(flame, 100f + k * dt);
                    Assert.That(s, Is.InRange(-1.0001f, 1.0001f));
                    Assert.LessOrEqual(Mathf.Abs(s - prev) / dt, maxSlope * 1.02f, "sıçrama: strob");
                    lo = Mathf.Min(lo, s);
                    hi = Mathf.Max(hi, s);
                    prev = s;
                }

                Assert.Greater(hi - lo, 0.5f, "alev " + flame + " titremiyor");
            }

            // Neighbouring lanterns do not pulse in step.
            Assert.AreNotEqual(LanternFlames.FlickerSignal(0, 3f), LanternFlames.FlickerSignal(1, 3f));
        }

        [Test]
        public void KartVeIsik_AyniYondeTitrer_FlasAzaltmaIkisiniDeKisar()
        {
            float i0 = LanternFlames.LanternLightIntensity;
            Assert.AreEqual(1f, LanternFlames.CardAlphaFor(1f, 1f), 1e-5f);
            Assert.AreEqual(1f - LanternFlames.FlickerAmplitude, LanternFlames.CardAlphaFor(-1f, 1f), 1e-5f);
            Assert.AreEqual(i0 * (1f + LanternFlames.LightFlickerAmplitude), LanternFlames.LightIntensityFor(1f, 1f), 1e-5f);
            Assert.AreEqual(i0 * (1f - LanternFlames.LightFlickerAmplitude), LanternFlames.LightIntensityFor(-1f, 1f), 1e-5f);
            Assert.AreEqual(0.12f, LanternFlames.LightFlickerAmplitude, 1e-5f, "ışık ±%12");

            float prevA = LanternFlames.CardAlphaFor(-1f, 1f), prevI = LanternFlames.LightIntensityFor(-1f, 1f);
            for (int k = 1; k <= 20; k++)
            {
                float s = -1f + k * 0.1f;
                float a = LanternFlames.CardAlphaFor(s, 1f), i = LanternFlames.LightIntensityFor(s, 1f);
                Assert.Greater(a, prevA, "kart sinyalle artmalı");
                Assert.Greater(i, prevI, "ışık kartla aynı yönde");
                prevA = a;
                prevI = i;
            }

            // Reduce flashes (FlashScale 0.4): the swings shrink to 40 %; 0 = steady.
            Assert.AreEqual(i0 * (1f + 0.4f * LanternFlames.LightFlickerAmplitude), LanternFlames.LightIntensityFor(1f, 0.4f), 1e-5f);
            Assert.AreEqual(1f - 0.4f * LanternFlames.FlickerAmplitude, LanternFlames.CardAlphaFor(-1f, 0.4f), 1e-5f);
            Assert.AreEqual(i0, LanternFlames.LightIntensityFor(-1f, 0f), 1e-5f);

            // §5: the flame scale ±8 % from the same sample, rising with the light.
            Assert.AreEqual(1f + LanternFlames.FlameScaleAmplitude, LanternFlames.FlameScaleFor(1f, 1f), 1e-5f);
            Assert.AreEqual(1f - LanternFlames.FlameScaleAmplitude, LanternFlames.FlameScaleFor(-1f, 1f), 1e-5f);
            Assert.AreEqual(0.08f, LanternFlames.FlameScaleAmplitude, 1e-5f);
            Assert.Greater(LanternFlames.FlameScaleFor(0.5f, 1f), LanternFlames.FlameScaleFor(0.4f, 1f));
        }

        [Test]
        public void Ap030_AileHaritasi_PisirilmisAileler_IsikKatmani()
        {
            foreach (string f in new[] { "andon", "kerosene_lamp", "wall_sconce" })
                CollectionAssert.Contains(LanternFlames.LanternFamilies, f, "aile haritası " + f);
            foreach (string n in new[] { "andon", "SM_andon_V01", "kerosene_lamp_table", "kerosene_lamp_hanging", "SM_wall_sconce_V02", "lantern_kasuga", "lantern_oribe",
                         "lantern_yukimi", "lantern_chochin", "lantern_tsuri", "SM_lantern_kasuga_V01" })
                Assert.IsTrue(LanternFlames.IsLanternFamily(n), n + " fener ailesi olmalı");
            foreach (string n in new[] { "fence_yotsume", "abandoned_cart", "FX_AP010B_LanternFlame", "", null })
                Assert.IsFalse(LanternFlames.IsLanternFamily(n), (n ?? "null") + " fener değil");

            foreach (string n in new[] { "andon", "kerosene_lamp_table", "wall_sconce" }) Assert.IsTrue(LanternFlames.IsBakedFamily(n), n + " pişirilmiş");
            foreach (string n in new[] { "lantern_kasuga", "lantern_chochin", "lantern_tsuri" }) Assert.IsFalse(LanternFlames.IsBakedFamily(n), n + " açık hava");

            // UniversalRenderPipelineGlobalSettings: bit 0 Default, bit 1 Characters — the baked-family light reaches characters only.
            Assert.AreEqual(2u, LanternFlames.CharactersOnlyRenderingLayers);
            Assert.AreEqual(LanternFlames.FlameSocketName, "SOCKET_Flame");
            Assert.AreEqual(LanternFlames.LightSocketName, "SOCKET_Light");
        }

        [Test]
        public void Ap030_ParlamaEmisyonu_IsikIleAyniSalinim()
        {
            var baseE = new Color(1.2f, 0.7f, 0.35f, 1f);
            for (float s = -1f; s <= 1.0001f; s += 0.25f)
            {
                Color e = LanternFlames.GlowEmissionFor(baseE, s, 1f);
                float kGlow = e.r / baseE.r - 1f, kLight = LanternFlames.LightIntensityFor(s, 1f) / LanternFlames.LanternLightIntensity - 1f;
                Assert.AreEqual(kLight, kGlow, 1e-5f, "parlama ve ışık aynı örnek, aynı ±%12");
                Assert.AreEqual(e.g / baseE.g, e.r / baseE.r, 1e-5f, "renk tonu korunur");
            }

            Assert.AreEqual(baseE, LanternFlames.GlowEmissionFor(baseE, 0.7f, 0f), "flaş azaltma 0: sabit");
        }

        [Test]
        public void Kivilcim_KademeTavani_KucukVeSicak()
        {
            Assert.AreEqual(0, LanternFlames.SparkCap(VfxTierLevel.Performance), "Performans: kıvılcım yok");
            Assert.That(LanternFlames.SparkCap(VfxTierLevel.High), Is.InRange(1, 30));
            Assert.AreEqual(30, LanternFlames.SparkCap(VfxTierLevel.Cinematic), "≤ 30 canlı");
            Assert.AreEqual(3, LanternFlames.MaxSparksPerLantern);
            Assert.AreEqual(0.010f, LanternFlames.SparkSizeMin, 1e-5f, "§5 1,0–1,5 cm");
            Assert.AreEqual(0.015f, LanternFlames.SparkSizeMax, 1e-5f);
            Assert.AreEqual(2.5f, LanternFlames.SparkLifeMin, 1e-5f, "§5 ömür 2,5–4 s");
            Assert.AreEqual(4f, LanternFlames.SparkLifeMax, 1e-5f);
            Assert.That(LanternFlames.SparkRiseMin, Is.InRange(0.08f, 0.15f));
            Assert.That(LanternFlames.SparkRiseMax, Is.InRange(0.08f, 0.15f));
            Color32 c = LanternFlames.SparkTint;
            Assert.AreEqual(new Color32(0xE8, 0xB4, 0x8C, 255), c, "§5 #E8B48C");
            Assert.LessOrEqual(LanternFlames.SparkAlpha, 0.25f, "§5 koridor bandında α ≤ 0,25");
            Color32 h = LanternFlames.HaloTint;
            Assert.AreEqual(new Color32(0xE8, 0xA8, 0x70, 255), h, "§5 hale #E8A870");
            Assert.AreEqual(0.7f, LanternFlames.HaloDiameter, 1e-5f);
            Assert.AreEqual(0.12f, LanternFlames.HaloAlpha, 1e-5f);
        }

        [Test]
        public void HalkaVeDuman_OkunurlukTavanlari_Kademe()
        {
            for (int c = 0; c < AmbientVfxSystem.RingCauseCount; c++)
            {
                AmbientVfxSystem.RingLook((AmbientVfxSystem.RingCause)c, out float diameter, out float lifeMin, out float lifeMax, out float alpha, out _);
                Assert.LessOrEqual(alpha, AmbientVfxSystem.RingAlphaCap, "halka α ≤ 0,35");
                Assert.LessOrEqual(lifeMax, AmbientVfxSystem.RingLifeMax, "halka ≤ 0,8 s");
                Assert.LessOrEqual(lifeMin, lifeMax);
                Assert.That(diameter, Is.InRange(0.2f, 1.0f), "ince halka, gölü kaplamaz (§5 Ø ≤ 1,0 m)");
            }

            Assert.AreEqual(0.35f, AmbientVfxSystem.RingAlphaCap, 1e-5f);
            Assert.LessOrEqual(AmbientVfxSystem.SmokeAlphaMin, AmbientVfxSystem.SmokeAlphaMax);
            Assert.LessOrEqual(AmbientVfxSystem.SmokeAlphaMax, AmbientVfxSystem.SmokeAlphaCap);
            Assert.LessOrEqual(AmbientVfxSystem.SmokeAlphaCap, 0.12f, "duman α ≤ 0,12");
            Assert.AreEqual(7f, AmbientVfxSystem.SmokeCeiling, 1e-5f, "§5 zeminden 7 m'de söner");
            Assert.AreEqual(4f, AmbientVfxSystem.SmokeRayClear, 1e-5f, "§5 kamera ışınına ≥ 4 m");
            Assert.AreEqual(2, AmbientVfxSystem.V6SmokeSources.Length, "§5 iki kaynak (R4 çay evi, ambar avlusu)");
            Assert.AreEqual(12, AmbientVfxSystem.MaxPuddleRings, "§5 karede ≤ 12 birikinti halkası");
            Color32 smoke = AmbientVfxSystem.SmokeTint;
            Assert.AreEqual(new Color32(0xAE, 0xB5, 0xBC, 255), smoke, "§5 #AEB5BC");

            VfxTierLevel[] tiers = { VfxTierLevel.Performance, VfxTierLevel.High, VfxTierLevel.Cinematic };
            for (int i = 1; i < tiers.Length; i++)
            {
                Assert.GreaterOrEqual(AmbientVfxSystem.RingCap(tiers[i]), AmbientVfxSystem.RingCap(tiers[i - 1]));
                Assert.GreaterOrEqual(AmbientVfxSystem.SmokeTarget(tiers[i]), AmbientVfxSystem.SmokeTarget(tiers[i - 1]));
                Assert.GreaterOrEqual(AmbientVfxSystem.PuddleDropsPerSecond(tiers[i]), AmbientVfxSystem.PuddleDropsPerSecond(tiers[i - 1]));
            }

            Assert.LessOrEqual(AmbientVfxSystem.SmokeSources(VfxTierLevel.Cinematic), 4, "az kaynak");
            Assert.That(AmbientVfxSystem.WispsPerSource(VfxTierLevel.High), Is.InRange(12, 20), "§5 kaynak başına 12–20");
            Assert.LessOrEqual(AmbientVfxSystem.RingCap(VfxTierLevel.Cinematic), 64);
            Assert.IsFalse(AmbientVfxSystem.EventRings(VfxTierLevel.Performance), "Performans: damla / yaprak halkası yok");
            Assert.IsTrue(AmbientVfxSystem.EventRings(VfxTierLevel.High));
        }

        [Test]
        public void BirikintiAlani_CpuAynasi_Belirlenimci_SifirBir()
        {
            int above = 0, total = 0;
            for (float x = -16f; x < 16f; x += 0.37f)
            for (float z = -14f; z < 19.7f; z += 0.37f)
            {
                float h = AmbientVfxSystem.GroundHash(x, z);
                Assert.That(h, Is.InRange(0f, 1f));
                float f = AmbientVfxSystem.PuddleField(x, z, 7f);
                Assert.That(f, Is.InRange(0f, 1f));
                Assert.AreEqual(f, AmbientVfxSystem.PuddleField(x, z, 7f), 0f, "belirlenimci");
                if (f > 0.62f) above++;
                total++;
            }

            // DD_GroundTiles: "_PuddleField 0.62 ≈ 25 % cover" — the mirror must land in that neighbourhood.
            float cover = (float)above / total;
            Debug.Log("[DD-TEST] wp093.puddle cover=" + cover.ToString("F3"));
            Assert.That(cover, Is.InRange(0.05f, 0.5f), "birikinti örtüsü ≈ %25 olmalı (ayna kaydı)");
        }

        [Test]
        public void Sprite_HalkaVeNokta_KenardaBos()
        {
            Assert.AreEqual(0f, AmbientVfxTextures.RingAlpha(0f), 0.02f, "halka ortası boş");
            Assert.Greater(AmbientVfxTextures.RingAlpha(0.86f), 0.9f, "halka tepesi");
            Assert.AreEqual(0f, AmbientVfxTextures.RingAlpha(0.99f), 1e-5f);
            Assert.Greater(AmbientVfxTextures.DotAlpha(0f), 0.9f);
            Assert.AreEqual(0f, AmbientVfxTextures.DotAlpha(1f), 1e-5f);
            Assert.Less(AmbientVfxTextures.DotAlpha(0.9f), 0.1f);
            Assert.Greater(AmbientVfxTextures.GlowAlpha(0f), 0.95f);
            Assert.AreEqual(0f, AmbientVfxTextures.GlowAlpha(1f), 1e-5f);
        }

        [Test]
        public void DumanSutunu_KameraIsininaUzaklik()
        {
            var camera = new Vector3(0f, 15f, -13f);
            var chest = new Vector3(0f, 1.1f, 0f);
            // A column 8 m to the side never nears the ray; one straddling it does.
            Assert.Greater(AmbientVfxSystem.ColumnNearRay(new Vector3(-8f, 4.25f, 8f), 7f, camera, chest), 7.9f);
            Assert.Less(AmbientVfxSystem.ColumnNearRay(new Vector3(0f, 4f, -6f), 7f, camera, chest), 1f);
        }
    }
}
