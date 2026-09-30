using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// DEC-112 (user 30.09) · the capstone tone: S-04's RunTreeHooks.CapstoneProc lights the struck body with a cold steel pulse
    /// (ZombieSkinFx.Steel) — Lightning Fang 0.25 s, Black Iron 0.5 s — full for the first 30 %, eased out to 0 at the end,
    /// dimmer under Reduce flashes. Third Eye's chain needs no tone.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073CapstoneToneTests
    {
        [Test]
        public void SteelPulse_FullThenEasesOut_ReducedDimmer_ZeroOutside()
        {
            float s = ZombieSkinFx.SteelFangSeconds;
            Vector4 start = ZombieSkinFx.SteelColour(0f, s, false);
            Assert.Greater(start.x + start.y + start.z, 0f, "darbe başında ton var");
            Assert.AreEqual(start, ZombieSkinFx.SteelColour(s * 0.29f, s, false), "ilk %30 tam");
            Vector4 late = ZombieSkinFx.SteelColour(s * 0.9f, s, false);
            Assert.Less(late.z, start.z, "sona doğru söner");
            Assert.AreEqual(Vector4.zero, ZombieSkinFx.SteelColour(s, s, false), "süre sonunda sıfır");
            Assert.AreEqual(Vector4.zero, ZombieSkinFx.SteelColour(-0.01f, s, false));
            Vector4 reduced = ZombieSkinFx.SteelColour(0f, s, true);
            Assert.AreEqual(start.z * ZombieSkinFx.SteelReduced, reduced.z, 1e-5f, "azaltılmış flaş × 0,6");
            Assert.Greater(start.z, start.x, "soğuk çelik: mavi > kırmızı");
            Assert.Less(ZombieSkinFx.SteelFangSeconds, ZombieSkinFx.SteelIronSeconds, "Kurogane darbesi daha uzun");
        }
    }
}
