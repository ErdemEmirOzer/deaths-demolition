using System;
using System.Reflection;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-056 · Special Weapon usage v2 (weapon side): the usage bus carries the table's ids
    /// per kind without allocating; the per-weapon usage data sits inside the DEC-024..026 /
    /// REQ-055..058 bands (active vs ground timers separate, no magazine / reload, field
    /// durations shorter than the activation, warning before the end); the runtime's reach /
    /// seconds per primary event follow the weapon; rumble per Special under the WP-024 ceiling.
    /// </summary>
    [TestFixture]
    [Category("WP056")]
    public sealed class SpecialWeaponUsageV2Tests
    {
        private static PrototypeConfig Config => Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);

        [SetUp]
        public void SetUp()
        {
            SpecialWeaponUsageEvents.ResetForTests();   // Count is process-wide: earlier fixtures publish too
        }

        [TearDown]
        public void TearDown()
        {
            SpecialWeaponUsageEvents.ResetForTests();
        }

        [Test]
        public void KullanimVerisi_Bantlar_REQ055_058()
        {
            SpecialWeaponConfigSection s = Config.Weapons.Special;
            Assert.Greater(s.GroundLifetime, 0f, "REQ-055 yerde bekleme süresi ayrı");
            Assert.Greater(s.FinalSecondsWarning, 0f);

            foreach (SpecialWeaponId id in SpecialWeaponConfigSection.All)
            {
                SpecialWeaponCommon c = s.Common(id);
                Assert.IsNotNull(c, id + " Common");
                Assert.IsNotNull(c.Definition, id + " tanımı (Resources/Arsenal)");
                Assert.Greater(s.CapSeconds(id), 0f, "REQ-055 kullanım süresi (tavan)");
                Assert.AreNotEqual(s.CapSeconds(id), s.GroundLifetime, "REQ-055: iki süre ayrı değerler");
                Assert.Less(s.FinalSecondsWarning, s.CapSeconds(id), "REQ-058: son saniye uyarısı süreden önce");
                Assert.That(c.FireRate, Is.InRange(0.2f, 20f), id + " FireRate (cooldown) bandı");
                Assert.GreaterOrEqual(c.Range, 1f, id + " Range");
                Assert.That(c.RumbleFraction, Is.InRange(0f, 1f), id + " RumbleFraction");
                Assert.That(c.RumbleMilliseconds, Is.InRange(0, InputPreferences.MaxRumbleMilliseconds), id + " RumbleMilliseconds ≤ 120");
                Assert.That(c.CameraShake, Is.InRange(0f, 1f), id + " CameraShake 0..1");
                // DEC-097: a magazine weapon with a reserve (REQ-056's "no ammo" is replaced).
                Assert.Greater(c.Definition.ammo.magazine, 0, id + " şarjör");
                Assert.Greater(c.Definition.ammo.reserve, 0, id + " yedek");
            }

            // The chain finishes inside the activation; a puddle outlives a single shot but not the cap.
            WeaponDefinition arc = s.Common(SpecialWeaponId.Raiju).Definition;
            WeaponDefinition gel = s.Common(SpecialWeaponId.Namekuji).Definition;
            Assert.Less((arc.arcChain.maxTargets - 1) * arc.arcChain.jumpDelaySeconds + arc.arcChain.shockSeconds, s.CapSeconds(SpecialWeaponId.Raiju));
            Assert.Less(gel.gelPuddle.puddleLifetimeSeconds, s.CapSeconds(SpecialWeaponId.Namekuji));
        }

        [Test]
        public void KullanimKimlikleri_TabloylaBirebir_VeYayinAyirmaYapmaz()
        {
            foreach (SpecialWeaponId id in SpecialWeaponConfigSection.All)
            {
                SpecialWeaponUsageIds row = SpecialWeaponUsage.For(id);
                Assert.AreEqual(id, row.Id);
                Assert.IsFalse(string.IsNullOrEmpty(row.FireVfxFamily), id + " fire family");
                Assert.IsFalse(string.IsNullOrEmpty(row.FieldVfxFamily), id + " field family");
                Assert.AreNotEqual(row.FireVfxFamily, row.FieldVfxFamily, id + " fire ≠ field");
            }

            int received = 0;
            SpecialUsageEvent last = default;
            SpecialWeaponUsageEvents.Usage += e => { received++; last = e; };
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Namekuji, SpecialUsageKind.FieldSpawned, SpecialWeaponUsage.For(SpecialWeaponId.Namekuji).FieldVfxFamily,
                new Vector3(1f, 0f, 2f), Vector3.up, 7f, 2.2f, 0);
            Assert.AreEqual(1, received);
            Assert.AreEqual(1, SpecialWeaponUsageEvents.Count);
            Assert.AreEqual(SpecialUsageKind.FieldSpawned, last.Kind);
            Assert.AreEqual("sw_gel_puddle", last.UsageId);
            Assert.AreEqual(7f, last.Radius);
            Assert.AreEqual(2.2f, last.Seconds);
            Assert.AreEqual(SpecialWeaponUsageEvents.Last.UsageId, last.UsageId);
            Assert.IsFalse(last.Target.IsValid, "alan olayında hedef yok");

            // Hit kind (C-00 / S-03 handshake): the target, the force, the falloff and the transaction ride the same struct.
            var root = new GameObject("hit-target");
            try
            {
                var target = new DamageTarget { Root = root.transform, Id = 42, Height = 1.8f };
                SpecialWeaponUsageEvents.PublishHit(SpecialWeaponId.Raiju, "sw_arc_chain", target, new Vector3(2f, 1f, 3f), Vector3.forward, 2.5f, 0.55f, 0.2f);
                Assert.AreEqual(SpecialUsageKind.Hit, last.Kind);
                Assert.AreEqual(42, last.Target.Id);
                Assert.AreSame(root.transform, last.Target.Root);
                Assert.AreEqual(2.5f, last.Force);
                Assert.AreEqual(0.55f, last.Falloff01, 1e-5f);
                Assert.AreEqual(1, last.Count);
                Assert.AreEqual(DD.Prototype.Damage.DamageTransaction.Current, last.TransactionId, "işlem kimliği yayın anındaki DamageTransaction.Current");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            // ADR-037-1: a publish with a subscriber allocates nothing (readonly struct, string reference from the table).
            var yem = new byte[0];
            HeapWindow.Result control = HeapWindow.Measure(() => { yem = new byte[64 * 1024]; }, 0);
            Assert.Greater(control.MonoDelta, 32 * 1024, "pozitif kontrol: ölçüm ayırmayı görmedi");
            HeapWindow.Result window = HeapWindow.Measure(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    SpecialWeaponUsageEvents.Publish(SpecialWeaponId.Raiju, SpecialUsageKind.Fired, SpecialWeaponUsage.For(SpecialWeaponId.Raiju).FireVfxFamily,
                        Vector3.zero, Vector3.forward, 9.5f, 0.34f, i);
                }
            }, 0);
            Assert.LessOrEqual(window.MonoDelta, 4 * 1024, "200 yayın yığını büyüttü (" + window.MonoDelta + " B)");
            Assert.AreEqual(0, window.Gen0);
            GC.KeepAlive(yem);
        }

        [Test]
        public void Runtime_AtisMenziliVeSuresi_SilahaGore()
        {
            PrototypeConfig config = Config;
            SpecialWeaponConfigSection s = config.Weapons.Special;
            var rt = new SpecialWeaponRuntime();
            rt.ApplySettings(s);

            rt.Begin(s, SpecialWeaponId.Raiju);
            WeaponArcChainBlock arc = rt.Definition.arcChain;
            Assert.AreEqual(arc.jumpRadius, rt.FireRadius, 1e-4f, "Raiju: sıçrama yarıçapı");
            Assert.AreEqual((arc.maxTargets - 1) * arc.jumpDelaySeconds + arc.shockSeconds, rt.FireSeconds, 1e-4f, "Raiju: zincir süresi");
            rt.Cancel();

            rt.Begin(s, SpecialWeaponId.Namekuji);
            Assert.AreEqual(rt.Definition.gelPuddle.puddleRadius, rt.FireRadius, 1e-4f, "Namekuji: birikinti yarıçapı");
            Assert.AreEqual(rt.Definition.gelPuddle.puddleLifetimeSeconds, rt.FireSeconds, 1e-4f, "Namekuji: birikinti süresi");
            rt.Cancel();
        }
    }
}
