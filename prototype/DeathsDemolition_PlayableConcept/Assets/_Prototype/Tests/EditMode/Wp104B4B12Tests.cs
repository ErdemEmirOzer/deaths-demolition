using DD.Prototype.Rounds;
using DD.Prototype.Weapons.Attachments;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B4-2 · B12 on the weapon attachments (S-06, C-00 12.10; AD-VISUAL §2, S-07's AddOnEmissive M9 values) · the numbers
    /// alone: the element tints, the brightness ladder (element III 1.4 · IV 1.6 · V 2.0, the add-on ladder below), Reduce Flashes
    /// under the bloom threshold, the mote rates, the edge ring's dot count.
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B4B12Tests
    {
        private const float Bloom = 1.25f;

        [Test]
        public void ElementRenkleri_AdVisual15()
        {
            Assert.AreEqual(WeaponAttachB12.FireGlow, WeaponAttachB12.GlowFor(TreeElement.Fire));
            Assert.AreEqual(WeaponAttachB12.IceGlow, WeaponAttachB12.GlowFor(TreeElement.Ice));
            Assert.AreEqual(WeaponAttachB12.ShockGlow, WeaponAttachB12.GlowFor(TreeElement.Shock));
            Assert.AreEqual(WeaponAttachB12.VenomGlow, WeaponAttachB12.GlowFor(TreeElement.Venom));
            Assert.AreEqual(Color.black, WeaponAttachB12.GlowFor(TreeElement.None));
            Assert.AreEqual(0xFF / 255f, WeaponAttachB12.FireGlow.r, 1e-4f, "ateş #FF5A1F");
            Assert.AreEqual(0x5A / 255f, WeaponAttachB12.FireGlow.g, 1e-4f);
            Assert.AreEqual(0xE6 / 255f, WeaponAttachB12.IceGlow.g, 1e-4f, "buz #8CE6FF");
            Assert.AreEqual(0xE0 / 255f, WeaponAttachB12.VenomGlow.g, 1e-4f, "zehir #A8E04A");
        }

        [Test]
        public void Parlaklik_M9Merdiveni_IIIBloomUstunde()
        {
            float[] beklenen = { 0f, 0.3f, 0.6f, 1.4f, 1.6f, 2.0f };
            for (int t = 0; t < beklenen.Length; t++) Assert.AreEqual(beklenen[t], WeaponAttachB12.IntensityFor(t, 1f), 1e-6f, "kademe " + t);
            Assert.AreEqual(2.0f, WeaponAttachB12.IntensityFor(9, 1f), 1e-6f, "V üstü V'te kalır");
            Assert.Greater(WeaponAttachB12.IntensityFor(3, 1f), Bloom, "III bloom eşiğinin üstünde (S-07 M9: E4 III 1,4)");
            Assert.Less(WeaponAttachB12.IntensityFor(2, 1f), Bloom, "II altında");
        }

        [Test]
        public void ReduceFlashes_BloomAltinda_ZerrecikVeKenarYarisi()
        {
            for (int t = 0; t <= 5; t++) Assert.Less(WeaponAttachB12.IntensityFor(t, 0.4f), Bloom, "kademe " + t + ": Reduce Flashes bloom altında");
            Assert.Greater(WeaponAttachB12.IntensityFor(4, 0.4f), 0f, "kısılır, kaybolmaz");
            Assert.AreEqual(0f, WeaponAttachB12.MoteRateFor(2, 1f), "II'de zerrecik yok");
            Assert.AreEqual(4f, WeaponAttachB12.MoteRateFor(3, 1f));
            Assert.AreEqual(7f, WeaponAttachB12.MoteRateFor(4, 1f));
            Assert.AreEqual(12f, WeaponAttachB12.MoteRateFor(5, 1f));
            Assert.AreEqual(6f, WeaponAttachB12.MoteRateFor(5, 0.4f), "Reduce Flashes: zerrecik yarısı");
            Assert.AreEqual(WeaponAttachB12.EdgeDots, WeaponAttachB12.EdgeDotsFor(1f));
            Assert.AreEqual(WeaponAttachB12.EdgeDots / 2, WeaponAttachB12.EdgeDotsFor(0.4f), "Reduce Flashes: kenar noktası yarısı");
        }

        [Test]
        public void KenarHalkasi_AlevinYuzde30undanAz()
        {
            // AD-VISUAL §1.2 (b): the element edge is ≤ 30 % of the flash's area — 8 dots of 3 cm on the 0.28 m card
            float flash = 0.28f * 0.28f, dot = Mathf.PI * 0.015f * 0.015f;
            Assert.Less(WeaponAttachB12.EdgeDots * dot / flash, 0.30f);
        }
    }
}
