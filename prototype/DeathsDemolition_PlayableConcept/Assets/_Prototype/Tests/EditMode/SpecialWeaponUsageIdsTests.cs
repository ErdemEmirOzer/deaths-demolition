using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>WP-037 · the Special Weapon usage-id table covers every SpecialWeaponId once with valid cue ids and non-empty slot names.</summary>
    [TestFixture]
    [Category("WP037")]
    public sealed class SpecialWeaponUsageIdsTests
    {
        [Test]
        public void HerSpecialWeapon_TekSatir_GecerliKimlikler()
        {
            var ids = (SpecialWeaponId[])Enum.GetValues(typeof(SpecialWeaponId));
            Assert.AreEqual(ids.Length, SpecialWeaponUsage.Count, "Tablo satır sayısı enum ile aynı olmalı.");
            foreach (SpecialWeaponId id in ids)
            {
                SpecialWeaponUsageIds row = SpecialWeaponUsage.For(id);
                Assert.AreEqual(id, row.Id, id + " için satır yok.");
                Assert.IsTrue(Enum.IsDefined(typeof(CombatAudioEvent), row.FireCue));
                Assert.IsTrue(Enum.IsDefined(typeof(CombatAudioEvent), row.ActiveLoop));
                Assert.IsFalse(string.IsNullOrEmpty(row.FireVfxFamily), id + ": FireVfxFamily boş.");
                Assert.IsFalse(string.IsNullOrEmpty(row.FieldVfxFamily), id + ": FieldVfxFamily boş.");
                Assert.IsFalse(string.IsNullOrEmpty(row.ImpactVfxFamily), id + ": ImpactVfxFamily boş.");
                Assert.IsFalse(string.IsNullOrEmpty(row.HudIconId), id + ": HudIconId boş.");
            }

            // Fire / loop cues are the WP-007 lifecycle cues the audio map already binds (WP-021 rows).
            Assert.AreEqual(CombatAudioEvent.HeavyWeaponFire, SpecialWeaponUsage.For(SpecialWeaponId.Raiju).FireCue);
            Assert.AreEqual(CombatAudioEvent.HeavyWeaponSpinUp, SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).ActiveLoop);
        }
    }
}
