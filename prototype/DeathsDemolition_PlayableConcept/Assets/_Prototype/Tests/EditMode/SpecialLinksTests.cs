using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 × WP-077 §2.4 · the Special links' window (pickup → the end of that round, Q4) and the classic-side hooks:
    /// Conductor's +1 Chain jump (only with the Chain branch owned, only while Raiju's window is open), Storm's magazine
    /// refill cap (AddRounds never above the magazine), the four link ids live in the run tree.
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class SpecialLinksTests
    {
        [SetUp]
        public void SetUp()
        {
            SpecialLinks.EnsureHooked();
            SpecialLinks.CloseAll();
            SpecialLinks.OwnedOverride = null;
        }

        [TearDown]
        public void TearDown()
        {
            SpecialLinks.CloseAll();
            SpecialLinks.OwnedOverride = null;
            SpecialWeaponUsageEvents.ResetForTests();
            SpecialLinks.EnsureHooked();
        }

        private static void Activate(SpecialWeaponId id)
        {
            SpecialWeaponUsageEvents.Publish(id, SpecialUsageKind.Activated, SpecialWeaponUsage.For(id).FireVfxFamily, Vector3.zero, Vector3.forward, 0f, 0f, 0);
        }

        [Test]
        public void Pencere_AlimdanRoundSonuna()
        {
            Assert.IsFalse(SpecialLinks.IsOpen(SpecialWeaponId.Raiju));
            Activate(SpecialWeaponId.Raiju);
            Assert.IsTrue(SpecialLinks.IsOpen(SpecialWeaponId.Raiju), "alındığında açılır");
            Assert.IsFalse(SpecialLinks.IsOpen(SpecialWeaponId.Namekuji));
            SpecialWeaponRoundBridge.NotifyRoundEnded(3);
            Assert.IsFalse(SpecialLinks.IsOpen(SpecialWeaponId.Raiju), "round sonunda kapanır");
            Activate(SpecialWeaponId.Namekuji);
            SpecialWeaponRoundBridge.NotifyRunStarted(2);
            Assert.IsFalse(SpecialLinks.IsOpen(SpecialWeaponId.Namekuji), "yeni koşu kapatır");
        }

        [Test]
        public void Iletken_ZincirDaliylaArtiBirSicrama()
        {
            PrototypeConfig config = Resources.Load<PrototypeConfig>("PrototypeConfig");
            config.Weapons.RebuildClassicView();
            var mods = new WeaponModifierState();
            var rt = new ClassicWeaponRuntime();
            rt.Equip(config.Weapons.Find(ClassicWeaponId.AutoSmg), mods);
            SpecialLinks.OwnedOverride = e => e == RunTreeEffects.LinkArcConductor;
            Activate(SpecialWeaponId.Raiju);

            Assert.AreEqual(0, rt.BuildShot(Vector3.zero, Vector3.forward, 0).ChainTargets, "Chain dalı yokken bağ zincir yaratmaz");
            mods.AddTier(UpgradeBranch.Chain, 1);
            int base1 = mods.ChainTargets;
            Assert.AreEqual(base1 + 1, rt.BuildShot(Vector3.zero, Vector3.forward, 0).ChainTargets, "İletken +1");
            SpecialWeaponRoundBridge.NotifyRoundEnded(1);
            Assert.AreEqual(base1, rt.BuildShot(Vector3.zero, Vector3.forward, 0).ChainTargets, "pencere kapanınca biter");
            SpecialLinks.OwnedOverride = e => false;
            Activate(SpecialWeaponId.Raiju);
            Assert.AreEqual(base1, rt.BuildShot(Vector3.zero, Vector3.forward, 0).ChainTargets, "düğüm alınmadıysa yok");
        }

        [Test]
        public void Firtina_AddRounds_SarjoruAsmaz()
        {
            PrototypeConfig config = Resources.Load<PrototypeConfig>("PrototypeConfig");
            config.Weapons.RebuildClassicView();
            var rt = new ClassicWeaponRuntime();
            rt.Equip(config.Weapons.Find(ClassicWeaponId.AutoSmg), new WeaponModifierState());
            int mag = rt.MagazineSize;
            Assert.AreEqual(0, rt.AddRounds(5), "dolu şarjöre girmez");
            for (int i = 0; i < 6; i++) rt.ConsumeShot();
            Assert.AreEqual(4, rt.AddRounds(4));
            Assert.AreEqual(2, rt.AddRounds(10), "tavan şarjör");
            Assert.AreEqual(mag, rt.Ammo);
            Assert.AreEqual(4, Mathf.Max(1, Mathf.FloorToInt(RunTreeCatalog.FindLink(SpecialWeaponKeys.Raiju, 2).effectValue * mag)), "smg_1 42 → %10 = 4");
        }

        [Test]
        public void DortBag_KancalariyleCanli()
        {
            foreach (string id in new[] { RunTreeEffects.LinkArcConductor, RunTreeEffects.LinkArcStorm, RunTreeEffects.LinkGelSlickFloor, RunTreeEffects.LinkGelFreeze })
            {
                Assert.IsTrue(RunTreeEffects.IsLive(id), id + " canlı listede (kanca bu commit'te)");
            }

            Assert.AreEqual(0.15f, RunTreeCatalog.FindLink(SpecialWeaponKeys.Namekuji, 1).effectValue, 1e-4f, "Kaygan Zemin +%15");
            Assert.AreEqual(1f, RunTreeCatalog.FindLink(SpecialWeaponKeys.Namekuji, 2).effectValue, 1e-4f, "Jel Hasadı +1 altın");
        }
    }
}
