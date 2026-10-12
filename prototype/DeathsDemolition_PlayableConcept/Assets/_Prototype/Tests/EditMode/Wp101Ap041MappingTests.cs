using System.Collections.Generic;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-101 (R4, AP-041) · the pure tables the dressing reads: weapon class → TD muzzle family (C-00 11.10: SMG → pistol,
    /// shotgun → pellet, launcher stays AP-010), the three flash variants and four holes in turn, the surface grouping of the
    /// AP-010 table kept for the AP-041 impact sheets, and which surfaces trade their mark for an AP-041 hole.
    /// </summary>
    [TestFixture]
    [Category("WP101")]
    public sealed class Wp101Ap041MappingTests
    {
        [TestCase(VfxWeaponClass.AssaultRifle, Ap041MuzzleClass.Rifle)]
        [TestCase(VfxWeaponClass.Smg, Ap041MuzzleClass.Pistol)]
        [TestCase(VfxWeaponClass.Shotgun, Ap041MuzzleClass.Pellet)]
        [TestCase(VfxWeaponClass.Lmg, Ap041MuzzleClass.Lmg)]
        [TestCase(VfxWeaponClass.Sniper, Ap041MuzzleClass.Sniper)]
        [TestCase(VfxWeaponClass.Launcher, Ap041MuzzleClass.None)]
        public void NamluSinifi_SilahSinifindan(VfxWeaponClass weaponClass, Ap041MuzzleClass expected)
        {
            Assert.AreEqual(expected, Ap041Vfx.MuzzleClassOf(weaponClass));
        }

        [Test]
        public void HerProfil_SinifaGore_TdKartiVarYaDaRoketatar()
        {
            foreach (VfxWeaponProfile p in VfxWeaponProfiles.All)
            {
                Ap041MuzzleClass c = Ap041Vfx.MuzzleClassOf(p.Class);
                if (p.Class == VfxWeaponClass.Launcher) Assert.AreEqual(Ap041MuzzleClass.None, c, p.Slot);
                else Assert.IsNotNull(Ap041Vfx.MuzzleId(c, 0), p.Slot + ": TD kartı yok");
                Assert.Greater(p.FlashLength, 0f, p.Slot + ": §1.1 boyu TD kartın uzun ekseni (Wp073FlashSizeTests)");
            }
        }

        [Test]
        public void NamluVaryantlari_UcluDongu_On5Benzersiz()
        {
            var all = new HashSet<string>();
            for (int c = 0; c < 5; c++)
            {
                var cls = (Ap041MuzzleClass)c;
                var three = new HashSet<string>();
                for (int v = 0; v < Ap041Vfx.MuzzleVariants; v++)
                {
                    string id = Ap041Vfx.MuzzleId(cls, v);
                    Assert.IsTrue(Ap041Vfx.IsMuzzleFlashId(id), id);
                    Assert.IsTrue(three.Add(id), cls + " varyant tekrarı");
                    all.Add(id);
                }

                Assert.AreEqual(Ap041Vfx.MuzzleId(cls, 0), Ap041Vfx.MuzzleId(cls, 3), "4. atış 1. varyanta döner");
                Assert.AreEqual(Ap041Vfx.MuzzleId(cls, 2), Ap041Vfx.MuzzleId(cls, -1));
            }

            Assert.AreEqual(15, all.Count);
            Assert.IsNull(Ap041Vfx.MuzzleId(Ap041MuzzleClass.None, 0));
            Assert.IsFalse(Ap041Vfx.IsMuzzleFlashId(VfxIds.Ap041MuzzleSmoke), "duman flaş sayılmaz");
            Assert.IsFalse(Ap041Vfx.IsMuzzleFlashId(VfxIds.Muzzle), "AP-010 fb_muzzle AP-041 değil");
        }

        [TestCase(SurfaceKind.Stone, VfxIds.Ap041ImpactStone, 0)]
        [TestCase(SurfaceKind.Default, VfxIds.Ap041ImpactStone, 0)]
        [TestCase(SurfaceKind.Armor, VfxIds.Ap041ImpactStone, 0)]
        [TestCase(SurfaceKind.Wood, VfxIds.Ap041ImpactWood, 1)]
        [TestCase(SurfaceKind.Foliage, VfxIds.Ap041ImpactWood, -1)]
        [TestCase(SurfaceKind.Paper, VfxIds.Ap041ImpactWood, -1)]
        [TestCase(SurfaceKind.Dirt, VfxIds.Ap041ImpactEarth, -1)]
        [TestCase(SurfaceKind.Water, VfxIds.Ap041ImpactEarth, -1)]
        [TestCase(SurfaceKind.Metal, VfxIds.Ap041ImpactMetal, 2)]
        [TestCase(SurfaceKind.Flesh, VfxIds.Ap041ImpactFlesh, -1)]
        public void Yuzey_IsabetVeDelikSeti(SurfaceKind surface, string impact, int decalSet)
        {
            Assert.AreEqual(impact, Ap041Vfx.ImpactIdFor(surface));
            Assert.AreEqual(decalSet, Ap041Vfx.DecalSetOf(surface), "dirt AP-010 toprak izinde kalır; izsiz yüzeyler izsiz");
        }

        [Test]
        public void Delikler_DortluDongu_On2Benzersiz()
        {
            var all = new HashSet<string>();
            for (int s = 0; s < Ap041Vfx.DecalSets; s++)
            {
                for (int v = 0; v < Ap041Vfx.DecalVariants; v++) Assert.IsTrue(all.Add(Ap041Vfx.DecalId(s, v)));
                Assert.AreEqual(Ap041Vfx.DecalId(s, 0), Ap041Vfx.DecalId(s, 4));
            }

            Assert.AreEqual(12, all.Count);
            Assert.IsNull(Ap041Vfx.DecalId(-1, 0));
            Assert.IsNull(Ap041Vfx.DecalId(3, 0));
        }
    }
}
