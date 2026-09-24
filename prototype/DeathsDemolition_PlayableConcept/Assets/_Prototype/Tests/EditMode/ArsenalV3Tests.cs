using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 B1 · WeaponDefinition v3 (A4) and the projectile core (ADR-069-1) without a scene:
    /// the catalog is valid, the A/B/C migration is behaviour-preserving (the per-config copy
    /// carries exactly the M3 numbers), clones stay clone-scoped, the saved-profile table maps
    /// the M3 names, and projectile motion (range, gravity, determinism, capacity) is exact.
    /// World / body impacts are PlayMode (ProjectileSliceTests).
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class ArsenalV3Tests
    {
        private static PrototypeConfig Config => Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);

        [TearDown]
        public void TearDown()
        {
            ProjectileSystem.Clear();
        }

        // ------------------------------------------------------------------ A4 catalog

        [Test]
        public void Katalog_UcTanim_Gecerli_KimliklerBenzersiz()
        {
            ArsenalCatalog catalog = Arsenal.Catalog;
            Assert.IsNotNull(catalog, "Resources/Arsenal/ArsenalCatalog yok (ArsenalAssetBuilder.BuildB1).");
            var faults = new List<string>();
            Assert.IsTrue(catalog.Validate(faults), string.Join("\n", faults));
            Assert.AreSame(Arsenal.Get("ar_1"), Arsenal.Get(ClassicWeaponId.AssaultRifle));
            Assert.AreSame(Arsenal.Get("smg_1"), Arsenal.Get(ClassicWeaponId.AutoSmg));
            Assert.AreSame(Arsenal.Get("sg_1"), Arsenal.Get(ClassicWeaponId.CombatShotgun));
            Assert.AreSame(Arsenal.Get("sg_1"), Arsenal.Get("CombatShotgun"), "eski ad (legacyNames) aynı tanıma çıkar");
            Assert.AreEqual("AR1", Arsenal.Get("ar_1").AudioId, "audioId = weaponId büyük harf, rakamdan önceki _ düşer (§0 uzlaştırma)");
            Assert.AreEqual("RL1", WeaponDefinition.DeriveAudioId("rl_1"));
            Assert.AreEqual("SW_ARC", WeaponDefinition.DeriveAudioId("sw_arc"));
            Assert.AreSame(Arsenal.Get("ar_1").AudioId, Arsenal.Get("ar_1").AudioId, "önbellekli (ateş yolunda ayırma yok)");
            Assert.AreEqual("sg_1", Arsenal.Get("sg_1").AnimSet);
            Assert.IsTrue(Arsenal.Get("smg_1").presentation.fireLoop, "780 RPM auto → döngülü ateş");
            Assert.IsFalse(Arsenal.Get("ar_1").presentation.fireLoop, "510 RPM → tek tek");
        }

        [Test]
        public void Gocu_DavranisKorur_KonfigKopyasi_M3SayilariylaBirebir()
        {
            PrototypeConfig config = Config;
            Assert.IsNotNull(config);
            config.Weapons.RebuildClassicView();
            List<ClassicWeaponSettings> m3 = WeaponConfigSection.CodeDefaults();
            Assert.AreEqual(3, config.Weapons.ClassicWeapons.Count, "A/B/C");
            foreach (ClassicWeaponSettings d in m3)
            {
                ClassicWeaponSettings a = config.Weapons.Find(d.Id);
                string tag = d.Id + ": ";
                Assert.IsNotNull(a.Definition, tag + "kopya v3 tanımına bağlı değil");
                Assert.AreEqual(d.DisplayName, a.DisplayName, tag + "ad");
                Assert.AreEqual(d.Damage, a.Damage, 1e-5f, tag + "hasar");
                Assert.AreEqual(d.FireRate, a.FireRate, 1e-4f, tag + "atış hızı");
                Assert.AreEqual(d.MagazineSize, a.MagazineSize, tag + "şarjör");
                Assert.AreEqual(d.ReloadTime, a.ReloadTime, 1e-5f, tag + "reload");
                Assert.AreEqual(d.PelletCount, a.PelletCount, tag + "pellet");
                Assert.AreEqual(d.SpreadDegrees, a.SpreadDegrees, 1e-5f, tag + "yayılma");
                Assert.AreEqual(d.SpreadBloomPerShot, a.SpreadBloomPerShot, 1e-5f, tag + "bloom");
                Assert.AreEqual(d.SpreadBloomMax, a.SpreadBloomMax, 1e-5f, tag + "bloom tavanı");
                Assert.AreEqual(d.SpreadBloomDecay, a.SpreadBloomDecay, 1e-5f, tag + "bloom sönümü");
                Assert.AreEqual(d.StaggerImpulse, a.StaggerImpulse, 1e-5f, tag + "itki");
                Assert.AreEqual(d.Range, a.Range, 1e-5f, tag + "menzil");
                Assert.AreEqual(d.PelletRingRadiusShare, a.PelletRingRadiusShare, 1e-5f, tag + "halka payı");
                Assert.AreEqual(d.PelletRingJitterShare, a.PelletRingJitterShare, 1e-5f, tag + "halka titreşimi");
                Assert.AreEqual(d.UnlockProfileLevel, a.UnlockProfileLevel, tag + "kilit seviyesi");
                Assert.AreEqual(d.RecoilKick, a.RecoilKick, 1e-4f, tag + "his (FeelConfig satırı)");
                Assert.AreEqual(d.RumbleMilliseconds, a.RumbleMilliseconds, tag + "rumble (FeelConfig satırı)");
            }
        }

        [Test]
        public void KlonKonfig_KendiKopyasiniTasir_OrijinalDegismez()
        {
            PrototypeConfig original = Config;
            original.Weapons.RebuildClassicView();
            float before = original.Weapons.Find(ClassicWeaponId.AutoSmg).Damage;
            PrototypeConfig clone = Object.Instantiate(original);
            try
            {
                clone.Weapons.Find(ClassicWeaponId.AutoSmg).Damage = before + 50f;
                Assert.AreEqual(before + 50f, clone.Weapons.Find(ClassicWeaponId.AutoSmg).Damage, 1e-5f);
                Assert.AreEqual(before, original.Weapons.Find(ClassicWeaponId.AutoSmg).Damage, 1e-5f, "klon yazması orijinale sızdı");
                Assert.AreEqual(before, Arsenal.Get("smg_1").fire.damage, 1e-5f, "klon yazması tanım varlığına sızdı");
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void ProfilGocuTablosu_EskiAdlar_V3Anahtarlar_CalismaZamaniIndeksleri()
        {
            Assert.AreEqual("ar_1", WeaponIdMigration.FromLegacyName("AssaultRifle"));
            Assert.AreEqual("smg_1", WeaponIdMigration.FromLegacyName("AutoSmg"));
            Assert.AreEqual("sg_1", WeaponIdMigration.FromLegacyName("CombatShotgun"));
            Assert.AreEqual("rl_1", WeaponIdMigration.FromLegacyName("rl_1"));
            Assert.IsNull(WeaponIdMigration.FromLegacyName("HeavyPistol"), "D emekli");
            Assert.IsTrue(WeaponIdMigration.TryGetRuntimeId("sg_1", out ClassicWeaponId sg));
            Assert.AreEqual(ClassicWeaponId.CombatShotgun, sg, "0/1/2 M3 değerleri korunur");
            Assert.IsTrue(WeaponIdMigration.TryGetRuntimeId("rl_1", out ClassicWeaponId rl));
            Assert.AreEqual(9, (int)rl);
            Assert.IsFalse(WeaponIdMigration.TryGetRuntimeId("sw_arc", out _));
        }

        [Test]
        public void MenzilDususu_DogrusalPay_TamHasarSinirindanSonra()
        {
            var p = new WeaponProjectileBlock { falloffStart = 30f, falloffEnd = 60f, falloffMinShare = 0.7f };
            Assert.AreEqual(1f, WeaponDefinition.FalloffShare(p, 10f), 1e-5f);
            Assert.AreEqual(1f, WeaponDefinition.FalloffShare(p, 30f), 1e-5f);
            Assert.AreEqual(0.85f, WeaponDefinition.FalloffShare(p, 45f), 1e-5f);
            Assert.AreEqual(0.7f, WeaponDefinition.FalloffShare(p, 90f), 1e-5f);
            var none = new WeaponProjectileBlock { falloffStart = 45f, falloffEnd = 45f, falloffMinShare = 0.2f };
            Assert.AreEqual(1f, WeaponDefinition.FalloffShare(none, 200f), 1e-5f, "başlangıç = bitiş → düşüş yok (B1 A/B/C)");
        }

        // ------------------------------------------------------------------ ADR-069-1 motion (no colliders in the path)

        private static ShotRequest Shot(Vector3 origin, Vector3 direction, float range)
        {
            return new ShotRequest { Origin = origin, Direction = direction, Damage = 10f, Range = range, PierceDamageFalloff = 1f };
        }

        private static ProjectileLaunch Launch(float speed, float gravity, float range)
        {
            return new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Speed = speed, Gravity = gravity,
                MaxRange = range, MaxLifetime = 10f, FalloffStart = range, FalloffEnd = range, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
        }

        /// <summary>Far above the world (y 900) so no collider can be in the path.</summary>
        private static readonly Vector3 Sky = new Vector3(0f, 900f, 0f);

        [Test]
        public void Mermi_MenzilSonundaSoner_TamMenzilKatEder()
        {
            ProjectileSystem.Clear();
            int expired = 0;
            System.Action<int, Vector3> on = (i, pos) => expired++;
            ProjectileEvents.Expired += on;
            try
            {
                ProjectileSystem.Spawn(Shot(Sky, Vector3.forward, 20f), Launch(100f, 0f, 20f));
                Assert.AreEqual(1, ProjectileSystem.LiveCount);
                ProjectileSystem.SimulateFor(0.19f);
                Assert.AreEqual(1, ProjectileSystem.LiveCount, "19 m'de hâlâ uçuyor");
                Assert.AreEqual(19f, ProjectileSystem.Get(0).Travelled, 0.01f, "hız × süre");
                ProjectileSystem.SimulateFor(0.05f);
                Assert.AreEqual(0, ProjectileSystem.LiveCount, "20 m menzilde söndü");
                Assert.AreEqual(1, expired, "Expired olayı bir kez");
            }
            finally
            {
                ProjectileEvents.Expired -= on;
            }
        }

        [Test]
        public void Mermi_Yercekimi_YarimGTkare_Dususu()
        {
            ProjectileSystem.Clear();
            ProjectileSystem.Spawn(Shot(Sky, Vector3.forward, 500f), Launch(100f, 9.81f, 500f));
            ProjectileSystem.SimulateFor(1f);
            ProjectileState p = ProjectileSystem.Get(0);
            Assert.AreEqual(Sky.y - 0.5f * 9.81f, p.Position.y, 0.02f, "1 s'de ½ g t² düşüş (yamuk entegrasyon tam)");
            Assert.AreEqual(100f, p.Position.z, 0.01f, "yatay hız korunur");
            Assert.AreEqual(-9.81f, p.Velocity.y, 0.01f);
        }

        [Test]
        public void Mermi_Deterministik_AyniGirdi_AyniYol()
        {
            Vector3 a = RunOnce();
            Vector3 b = RunOnce();
            Assert.AreEqual(a, b, "aynı spawn + aynı test saati → aynı konum");

            Vector3 RunOnce()
            {
                ProjectileSystem.Clear();
                ProjectileSystem.Spawn(Shot(Sky, new Vector3(0.3f, 0.1f, 1f).normalized, 400f), Launch(260f, 9.81f, 400f));
                ProjectileSystem.SimulateFor(0.73f);
                return ProjectileSystem.Get(0).Position;
            }
        }

        [Test]
        public void Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim()
        {
            ProjectileSystem.Clear();
            int overflowBefore = ProjectileSystem.OverflowCount;
            for (int i = 0; i < ProjectileSystem.Capacity + 1; i++)
            {
                ProjectileSystem.Spawn(Shot(Sky + Vector3.right * i, Vector3.forward, 500f), Launch(50f, 0f, 500f));
            }

            Assert.AreEqual(ProjectileSystem.Capacity, ProjectileSystem.LiveCount, "tavan aşılmaz");
            Assert.AreEqual(overflowBefore + 1, ProjectileSystem.OverflowCount, "taşma sayıldı");
            Assert.AreEqual(Sky.x + 1f, ProjectileSystem.Get(0).Origin.x, 1e-4f, "en eski (ilk doğan) emekli oldu");
        }

        [Test]
        public void Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder()
        {
            int nested = DamageTransaction.NestedBegins;
            int id = DamageTransaction.NextId();
            Assert.AreEqual(0, DamageTransaction.Current, "NextId işlem açmaz");
            DamageTransaction.Resume(id);
            Assert.AreEqual(id, DamageTransaction.Current, "Resume aynı kimliği sürdürür");
            DamageTransaction.End();
            Assert.AreEqual(0, DamageTransaction.Current);
            Assert.AreEqual(nested, DamageTransaction.NestedBegins, "iç içe açılış yok");
        }
    }
}
