using System;
using System.Reflection;
using DD.Prototype.Core.Config;
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

        [TearDown]
        public void TearDown()
        {
            SpecialWeaponUsageEvents.ResetForTests();
        }

        [Test]
        public void KullanimVerisi_Bantlar_REQ055_058()
        {
            SpecialWeaponConfigSection s = Config.Weapons.Special;
            Assert.Greater(s.ActiveDuration, 0f, "REQ-055 kullanım süresi");
            Assert.Greater(s.GroundLifetime, 0f, "REQ-055 yerde bekleme süresi ayrı");
            Assert.AreNotEqual(s.ActiveDuration, s.GroundLifetime, "REQ-055: iki süre ayrı değerler");
            Assert.Less(s.FinalSecondsWarning, s.ActiveDuration, "REQ-058: son saniye uyarısı süreden önce");
            Assert.Greater(s.FinalSecondsWarning, 0f);

            foreach (SpecialWeaponId id in new[] { SpecialWeaponId.KaguraBell, SpecialWeaponId.OfudaLoom, SpecialWeaponId.TideJewel })
            {
                SpecialWeaponCommon c = s.Common(id);
                Assert.IsNotNull(c, id + " Common");
                Assert.That(c.FireRate, Is.InRange(0.2f, 20f), id + " FireRate (cooldown) bandı");
                Assert.GreaterOrEqual(c.Range, 1f, id + " Range");
                Assert.That(c.RumbleFraction, Is.InRange(0f, 1f), id + " RumbleFraction");
                Assert.That(c.RumbleMilliseconds, Is.InRange(0, InputPreferences.MaxRumbleMilliseconds), id + " RumbleMilliseconds ≤ 120");
                Assert.That(c.CameraShake, Is.InRange(0f, 1f), id + " CameraShake 0..1");
            }

            // REQ-056: no magazine / reload concept on any Special settings type (reflection over the public fields).
            foreach (System.Type t in new[] { typeof(SpecialWeaponCommon), typeof(KaguraBellSettings), typeof(OfudaLoomSettings), typeof(TideJewelSettings) })
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    StringAssert.DoesNotContain("Magazine", f.Name, t.Name + " REQ-056: mermi yok");
                    StringAssert.DoesNotContain("Reload", f.Name, t.Name + " REQ-056: yeniden doldurma yok");
                }
            }

            // Field durations sit inside the activation; the loom's collapse rule is satisfiable; the well pulls more than once.
            Assert.Less(s.KaguraBell.WaveTravelSeconds, s.ActiveDuration, "dalga aktif süre içinde biter");
            Assert.GreaterOrEqual(s.KaguraBell.WaveRadius, 1f);
            Assert.LessOrEqual(s.OfudaLoom.SealSeconds, s.ActiveDuration, "mühür süresi ≤ aktif süre");
            Assert.Less(s.OfudaLoom.SealArmSeconds, s.OfudaLoom.SealSeconds, "kurulma süresi mühürden kısa");
            Assert.That(s.OfudaLoom.MinSealsToCollapse, Is.InRange(2, s.OfudaLoom.MaxSeals), "çökme eşiği 2..MaxSeals");
            Assert.Less(s.TideJewel.WellSeconds, s.ActiveDuration, "kuyu aktif süre içinde biter");
            Assert.Less(s.TideJewel.PullInterval, s.TideJewel.WellSeconds, "kuyu en az bir kez çeker");
            Assert.GreaterOrEqual(s.TideJewel.MaxWells, 1);
            Assert.GreaterOrEqual(s.TideJewel.ThrowRange, 1f);

            // The per-weapon pulse reads as three different weapons (bell heaviest, loom lightest).
            Assert.Greater(s.KaguraBell.Common.RumbleFraction, s.TideJewel.Common.RumbleFraction);
            Assert.Greater(s.TideJewel.Common.RumbleFraction, s.OfudaLoom.Common.RumbleFraction);
        }

        [Test]
        public void KullanimKimlikleri_TabloylaBirebir_VeYayinAyirmaYapmaz()
        {
            foreach (SpecialWeaponId id in new[] { SpecialWeaponId.KaguraBell, SpecialWeaponId.OfudaLoom, SpecialWeaponId.TideJewel })
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
            SpecialWeaponUsageEvents.Publish(SpecialWeaponId.TideJewel, SpecialUsageKind.FieldSpawned, SpecialWeaponUsage.For(SpecialWeaponId.TideJewel).FieldVfxFamily,
                new Vector3(1f, 0f, 2f), Vector3.up, 7f, 2.2f, 0);
            Assert.AreEqual(1, received);
            Assert.AreEqual(1, SpecialWeaponUsageEvents.Count);
            Assert.AreEqual(SpecialUsageKind.FieldSpawned, last.Kind);
            Assert.AreEqual("sw_tide_well", last.UsageId);
            Assert.AreEqual(7f, last.Radius);
            Assert.AreEqual(2.2f, last.Seconds);
            Assert.AreEqual(SpecialWeaponUsageEvents.Last.UsageId, last.UsageId);
            Assert.IsFalse(last.Target.IsValid, "alan olayında hedef yok");

            // Hit kind (C-00 / S-03 handshake): the target, the force, the falloff and the transaction ride the same struct.
            var root = new GameObject("hit-target");
            try
            {
                var target = new DamageTarget { Root = root.transform, Id = 42, Height = 1.8f };
                SpecialWeaponUsageEvents.PublishHit(SpecialWeaponId.KaguraBell, "sw_kagura_field", target, new Vector3(2f, 1f, 3f), Vector3.forward, 2.5f, 0.55f, 0.2f);
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
                    SpecialWeaponUsageEvents.Publish(SpecialWeaponId.KaguraBell, SpecialUsageKind.Fired, SpecialWeaponUsage.For(SpecialWeaponId.KaguraBell).FireVfxFamily,
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

            rt.Begin(s, SpecialWeaponId.KaguraBell);
            Assert.AreEqual(s.KaguraBell.WaveRadius, rt.FireRadius, 1e-4f, "çan: dalga yarıçapı");
            Assert.AreEqual(s.KaguraBell.WaveTravelSeconds, rt.FireSeconds, 1e-4f, "çan: dalga süresi");
            rt.Cancel();

            rt.Begin(s, SpecialWeaponId.OfudaLoom);
            Assert.AreEqual(s.OfudaLoom.Common.Range, rt.FireRadius, 1e-4f, "loom: dart menzili");
            Assert.AreEqual(s.OfudaLoom.SealSeconds, rt.FireSeconds, 1e-4f, "loom: mühür süresi");
            rt.Cancel();

            rt.Begin(s, SpecialWeaponId.TideJewel);
            Assert.AreEqual(s.TideJewel.ThrowRange, rt.FireRadius, 1e-4f, "mücevher: atış menzili");
            Assert.AreEqual(s.TideJewel.WellSeconds, rt.FireSeconds, 1e-4f, "mücevher: kuyu süresi");
            rt.Cancel();
        }
    }
}
