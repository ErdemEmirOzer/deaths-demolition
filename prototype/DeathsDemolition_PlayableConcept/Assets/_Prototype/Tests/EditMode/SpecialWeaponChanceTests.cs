using DD.Prototype.Core.Config;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bağımsız doğrulama · klasik silah ve zırh ihtimali sabitleri.
    ///
    /// WP-007: Special Weapon ihtimal eğrisi ve yapılandırma sabitleri (TS-201) ikiye
    /// ayrıldı — config-relative sözleşme <see cref="SpecialWeaponContractTests"/>,
    /// PrototypeOnly karakterizasyon <see cref="SpecialWeaponPrototypeCharacterizationTests"/>
    /// (gövdeler ve test adları birebir taşındı). Burada kalanlar Special dışıdır ve
    /// dokunulmamıştır.
    /// </summary>
    [TestFixture]
    public sealed class SpecialWeaponChanceTests
    {
        [Test]
        public void KlasikSilahlar_SabitDegerler()
        {
            var w = new WeaponConfigSection();

            // DEC-097 (WP-069 B2): the three legacy slots read the Arsenal v3 definitions (ar_1 / smg_1 / sg_1).
            ClassicWeaponSettings ar = w.Find(ClassicWeaponId.AssaultRifle);
            Assert.AreEqual(12f, ar.Damage, 0.0001f, "ar_1 12 (DEC-097; M3 11)");
            Assert.AreEqual(10f, ar.FireRate, 0.0001f, "ar_1 600 RPM (DEC-097; M3 8,5/s)");
            Assert.AreEqual(30, ar.MagazineSize);
            Assert.AreEqual(1, ar.PelletCount);

            ClassicWeaponSettings smg = w.Find(ClassicWeaponId.AutoSmg);
            Assert.AreEqual(8f, smg.Damage, 0.0001f, "smg_1 8 (DEC-085 Ö-4, DEC-097)");
            Assert.AreEqual(15f, smg.FireRate, 0.0001f, "smg_1 900 RPM (DEC-097; M3 13/s)");
            Assert.AreEqual(42, smg.MagazineSize);

            ClassicWeaponSettings sg = w.Find(ClassicWeaponId.CombatShotgun);
            Assert.AreEqual(9f, sg.Damage, 0.0001f, "sg_1 SAÇMA BAŞINA 9 (DEC-097; DEC-085 8)");
            Assert.AreEqual(8, sg.PelletCount);
            Assert.AreEqual(7, sg.MagazineSize);
            Assert.AreEqual(1.2f, sg.FireRate, 0.0001f);
        }

        [Test]
        public void ArmorChance_TurBasinaBuyumeVeTavan()
        {
            var enemies = new EnemyConfigSection();
            ArmorChanceSettings std = enemies.Find(EnemyTier.Standard).ArmorChances;

            // Standard R1: kask .15 · gövde .12 · bacak .08 · büyüme .012 · tavan .55 (DEC-085 Ö-2, WP-050: büyüme ×0,5, tavan 0,75 → 0,55)
            Assert.AreEqual(0.15f, std.Resolve(ArmorSlot.Helmet, 1), 0.0005f);
            Assert.AreEqual(0.12f, std.Resolve(ArmorSlot.Body, 1), 0.0005f);
            Assert.AreEqual(0.08f, std.Resolve(ArmorSlot.Leg, 1), 0.0005f);
            Assert.AreEqual(0f, std.Resolve(ArmorSlot.None, 1), 0.0005f);

            // R5: 0.15 + 0.012*4 = 0.198
            Assert.AreEqual(0.198f, std.Resolve(ArmorSlot.Helmet, 5), 0.0005f);
            // R25: 0.15 + 0.012*24 = 0.438 (tavanın altında)
            Assert.AreEqual(0.438f, std.Resolve(ArmorSlot.Helmet, 25), 0.0005f);
            // R100: tavana kırpılır (0.55)
            Assert.AreEqual(0.55f, std.Resolve(ArmorSlot.Helmet, 100), 0.0005f);
        }
    }
}
