using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// AD r4 MUST M2 (01.10) · the muzzle flash's long axis comes straight from the WP-073 §1.1 table (VfxWeaponProfile.FlashLength),
    /// not from the old WorldSize × Max(0.5, shot scale) × FlashScale chain that drew 54–73 % of it.
    /// </summary>
    [TestFixture]
    [Category("WP073")]
    public sealed class Wp073FlashSizeTests
    {
        [TestCase("ar_1", 0.55f)]
        [TestCase("ar_2", 0.495f)]
        [TestCase("smg_1", 0.35f)]
        [TestCase("smg_2", 0.40f)]
        [TestCase("sg_1", 0.60f)]
        [TestCase("sg_2", 0.60f)]
        [TestCase("lmg_1", 0.60f)]
        [TestCase("lmg_2", 0.54f)]
        [TestCase("sr_1", 1.20f)]
        [TestCase("rl_1", 0.80f)]
        public void FlashLength_IsTheDirectionTableLength(string slot, float metres)
        {
            VfxWeaponProfile p = VfxWeaponProfiles.BySlot(slot);
            Assert.IsNotNull(p, slot);
            Assert.AreEqual(metres, p.FlashLength, 1e-4f, slot + ": §1.1 boy");
            float quadLong = p.FlashLength / Mathf.Max(1f, Mathf.Max(p.FlashStretch.x, p.FlashStretch.y)) * Mathf.Max(p.FlashStretch.x, p.FlashStretch.y);
            Assert.GreaterOrEqual(quadLong, metres * 0.99f, slot + ": gerilmiş dörtgenin uzun ekseni tablo boyunda");
        }
    }
}
