using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics;
using DD.Prototype.Feedback;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-111 R4 (S-10 12.10, the tree end perf cell) · PilotPerfProbe's -ddPilotWeapon / -ddPilotAutoFire pure parts: the weapon key
    /// (a v3 key or a ClassicWeaponId name, never a silent fallback) and the 04 window's shot / blast counters (armed once, zeroed as
    /// the window opens, disarmed after). No Player, no scene.
    /// </summary>
    [TestFixture]
    [Category("WP111")]
    public sealed class PilotPerfProbeFireTests
    {
        [TearDown]
        public void Disarm() => PilotPerfProbe.DisarmWindowCounters();

        [Test]
        public void Silah_V3AnahtariYaDaEnumAdi_BilinmeyenReddedilir()
        {
            Assert.IsTrue(PilotPerfProbe.TryParseWeapon("rl_1", out ClassicWeaponId id, out string key));
            Assert.AreEqual(ClassicWeaponId.Rl1, id); Assert.AreEqual("rl_1", key);
            Assert.IsTrue(PilotPerfProbe.TryParseWeapon("  RL1 ", out id, out key), "an enum name, any case, blanks trimmed");
            Assert.AreEqual(ClassicWeaponId.Rl1, id); Assert.AreEqual("rl_1", key);
            Assert.IsTrue(PilotPerfProbe.TryParseWeapon("AssaultRifle", out id, out key));
            Assert.AreEqual("ar_1", key);
            foreach (string bad in new[] { null, "", "9", "rl_9", "nuke" })
            {
                Assert.IsFalse(PilotPerfProbe.TryParseWeapon(bad, out id, out key), "'" + bad + "' is refused (no AssaultRifle fallback)");
                Assert.IsNull(key);
            }

            Assert.AreEqual("sg_1", PilotPerfProbe.WeaponKey(ClassicWeaponId.CombatShotgun));
        }

        [Test]
        public void PencereSayaclari_IkiKezKurulsaBileTekSayar_SifirlanirSokulunceDurur()
        {
            PilotPerfProbe.ArmWindowCounters();
            PilotPerfProbe.ArmWindowCounters();   // armed twice: still one subscription
            PilotPerfProbe.ZeroWindowCounters();
            FeelEvents.RaiseExplosion(Vector3.zero, 5f, 0, ExplosionSource.Rocket);
            FeelEvents.RaiseExplosion(Vector3.zero, 5f, 0, ExplosionSource.Rocket);
            Assert.AreEqual(2, PilotPerfProbe.WindowBlasts);
            Assert.AreEqual(0, PilotPerfProbe.WindowShots);
            PilotPerfProbe.DisarmWindowCounters();
            FeelEvents.RaiseExplosion(Vector3.zero, 5f, 0, ExplosionSource.Rocket);
            Assert.AreEqual(2, PilotPerfProbe.WindowBlasts, "disarmed: the count stays readable, nothing more is added");
            PilotPerfProbe.ZeroWindowCounters();
            Assert.AreEqual(0, PilotPerfProbe.WindowBlasts);
        }
    }
}
