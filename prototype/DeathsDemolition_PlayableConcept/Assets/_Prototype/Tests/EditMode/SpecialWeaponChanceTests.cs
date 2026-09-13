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

            ClassicWeaponSettings ar = w.Find(ClassicWeaponId.AssaultRifle);
            Assert.AreEqual(11f, ar.Damage, 0.0001f);
            Assert.AreEqual(8.5f, ar.FireRate, 0.0001f);
            Assert.AreEqual(30, ar.MagazineSize);
            Assert.AreEqual(1, ar.PelletCount);

            ClassicWeaponSettings smg = w.Find(ClassicWeaponId.AutoSmg);
            Assert.AreEqual(7f, smg.Damage, 0.0001f);
            Assert.AreEqual(13f, smg.FireRate, 0.0001f);
            Assert.AreEqual(42, smg.MagazineSize);

            ClassicWeaponSettings sg = w.Find(ClassicWeaponId.CombatShotgun);
            Assert.AreEqual(7f, sg.Damage, 0.0001f, "Shotgun hasarı SAÇMA BAŞINA 7'dir.");
            Assert.AreEqual(8, sg.PelletCount);
            Assert.AreEqual(7, sg.MagazineSize);
            Assert.AreEqual(1.2f, sg.FireRate, 0.0001f);
        }

        [Test]
        public void ArmorChance_TurBasinaBuyumeVeTavan()
        {
            var enemies = new EnemyConfigSection();
            ArmorChanceSettings std = enemies.Find(EnemyTier.Standard).ArmorChances;

            // Standard R1: kask .15 · gövde .12 · bacak .08 · büyüme .025 · tavan .75
            Assert.AreEqual(0.15f, std.Resolve(ArmorSlot.Helmet, 1), 0.0005f);
            Assert.AreEqual(0.12f, std.Resolve(ArmorSlot.Body, 1), 0.0005f);
            Assert.AreEqual(0.08f, std.Resolve(ArmorSlot.Leg, 1), 0.0005f);
            Assert.AreEqual(0f, std.Resolve(ArmorSlot.None, 1), 0.0005f);

            // R5: 0.15 + 0.025*4 = 0.25
            Assert.AreEqual(0.25f, std.Resolve(ArmorSlot.Helmet, 5), 0.0005f);
            // R25: 0.15 + 0.025*24 = 0.75 (tam tavan)
            Assert.AreEqual(0.75f, std.Resolve(ArmorSlot.Helmet, 25), 0.0005f);
            // R100: tavana kırpılır
            Assert.AreEqual(0.75f, std.Resolve(ArmorSlot.Helmet, 100), 0.0005f);
        }
    }
}
