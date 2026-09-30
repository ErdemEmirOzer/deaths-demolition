using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// DEC-112 Third Eye (cap.ar_2) · S-02 tags the burst's 3rd round with RunTreeHooks.TreeRoundThirdEye at spawn; the tracer
    /// renderer draws that round as a full Kor tracer with a cold steel glow, whatever the class rhythm says (hit or miss).
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073ThirdEyeTrailTests
    {
        [Test]
        public void ThirdEyeFlag_IsRecognised_OnlyWhenSet()
        {
            Assert.IsTrue(TracerRibbonRenderer.IsThirdEyeRound(RunTreeHooks.TreeRoundThirdEye));
            Assert.IsTrue(TracerRibbonRenderer.IsThirdEyeRound(RunTreeHooks.TreeRoundThirdEye | 1));
            Assert.IsFalse(TracerRibbonRenderer.IsThirdEyeRound(0));
            Assert.IsFalse(TracerRibbonRenderer.IsThirdEyeRound(~RunTreeHooks.TreeRoundThirdEye & 0xFF));
            Assert.Greater(TracerRibbonRenderer.ThirdEyeGlowIntensity, 1f, "hale tam iz parlaklığında");
            Assert.Greater(VfxWeaponProfiles.Ar2.GlowWidth, 0f, "ar_2 hale katmanı var (ton görünür)");
        }
    }
}
