using DD.Prototype.Core.Config;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bağımsız doğrulama · Special Weapon ihtimal eğrisi.
    ///
    /// Referans (SABİT, TS-201):
    ///   R &lt; 4  -&gt; 0
    ///   R &gt;= 4 -&gt; min(0.35 + 0.03 * (R - 4), 0.65)
    ///   tavan ilk kez R14'te bağlar (0.35 + 0.03*10 = 0.65)
    /// </summary>
    [TestFixture]
    public sealed class SpecialWeaponChanceTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        // R      1     2     3     4     5     6     7     8     9    10
        // p     .00   .00   .00   .35   .38   .41   .44   .47   .50   .53
        [TestCase(1, 0.00f)]
        [TestCase(2, 0.00f)]
        [TestCase(3, 0.00f)]
        [TestCase(4, 0.35f)]
        [TestCase(5, 0.38f)]
        [TestCase(6, 0.41f)]
        [TestCase(7, 0.44f)]
        [TestCase(8, 0.47f)]
        [TestCase(9, 0.50f)]
        [TestCase(10, 0.53f)]
        [TestCase(11, 0.56f)]
        [TestCase(12, 0.59f)]
        [TestCase(13, 0.62f)]
        [TestCase(14, 0.65f)]
        [TestCase(15, 0.65f)]
        [TestCase(30, 0.65f)]
        [TestCase(200, 0.65f)]
        public void SpecialWeaponSpawnChance_SabitBeklenenDeger(int round, float beklenen)
        {
            Assert.AreEqual(beklenen, _config.SpecialWeaponSpawnChance(round), 0.0005f,
                "R" + round + " Special Weapon ihtimali eğriyle uyuşmuyor.");
        }

        [Test]
        public void IlkUygunTurdanOnceKesinlikleSifir()
        {
            var special = new SpecialWeaponConfigSection();
            Assert.AreEqual(4, special.FirstEligibleRound, "İlk uygun tur 4 olmalı (istek: 3'ten sonra).");

            for (int r = -5; r <= 3; r++)
            {
                Assert.AreEqual(0f, _config.SpecialWeaponSpawnChance(r), 0.0001f,
                    "R" + r + " için ihtimal sıfır olmalı.");
            }
        }

        [Test]
        public void TavanYuzde65iAsmaz()
        {
            for (int r = 4; r <= 300; r++)
            {
                Assert.LessOrEqual(_config.SpecialWeaponSpawnChance(r), 0.65f + 0.0001f,
                    "R" + r + " ihtimali %65 tavanını aştı.");
            }

            Assert.AreEqual(0.62f, _config.SpecialWeaponSpawnChance(13), 0.0005f, "R13 tavana değmemeli.");
            Assert.AreEqual(0.65f, _config.SpecialWeaponSpawnChance(14), 0.0005f, "Tavan ilk kez R14'te bağlar.");
        }

        [Test]
        public void EgriMonotonArtar_R4tenTavanaKadar()
        {
            float onceki = -1f;
            for (int r = 4; r <= 14; r++)
            {
                float simdi = _config.SpecialWeaponSpawnChance(r);
                Assert.Greater(simdi, onceki, "R" + r + " ihtimali artmadı.");
                onceki = simdi;
            }
        }

        [Test]
        public void SpecialWeaponYapilandirmasi_Sabit()
        {
            var s = new SpecialWeaponConfigSection();

            // Ortak zamanlama. ActiveDuration istek degeri, DEGISMEDI.
            Assert.AreEqual(11f, s.ActiveDuration, 0.0001f, "Aktif sure 11 sn olmali.");

            // G-21: kullanicinin acik karari. Eski deger 12 sn idi; olculen yuruyus
            // mesafesi medyan gecerli birakma noktasi icin 67 m = 10.9 sn oldugundan
            // 12 sn ulasilamaz bir sureydi.
            Assert.AreEqual(22f, s.GroundLifetime, 0.0001f, "Yerde durma suresi 22 sn olmali.");
            Assert.Greater(s.GroundLifetime, 12f, "Yer omru eski 12 sn degerinin uzerinde olmali.");

            Assert.AreEqual(0.35f, s.BaseSpawnChance, 0.0001f);
            Assert.AreEqual(0.65f, s.MaxSpawnChance, 0.0001f);
            Assert.AreEqual(0.30f, s.SpawnWindowMin, 0.0001f);
            Assert.AreEqual(0.70f, s.SpawnWindowMax, 0.0001f);
            Assert.AreEqual(8, s.SecondSpawnFromRound, "Ikinci Special R8'den itibaren mumkun.");
            Assert.AreEqual(0.10f, s.SecondSpawnChance, 0.0001f);
            Assert.AreEqual(3f, s.FinalSecondsWarning, 0.0001f, "Son saniye uyarisi 3 sn, degismedi.");
            Assert.Less(s.SpawnWindowMin, s.SpawnWindowMax, "Dogus penceresi ters cevrilmis.");
        }

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
