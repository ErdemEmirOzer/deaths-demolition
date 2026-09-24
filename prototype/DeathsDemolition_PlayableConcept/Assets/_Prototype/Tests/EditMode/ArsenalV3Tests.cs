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
    /// WP-069 B1/B2 · WeaponDefinition v3 (A4) and the projectile core (ADR-069-1) without a scene:
    /// the ten-weapon catalog is valid, the per-config copy carries each definition's numbers
    /// (B2: the council's, no longer M3's), clones stay clone-scoped, the saved-profile table maps
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
        public void Katalog_OnTanim_Gecerli_KimliklerBenzersiz()
        {
            ArsenalCatalog catalog = Arsenal.Catalog;
            Assert.IsNotNull(catalog, "Resources/Arsenal/ArsenalCatalog yok (ArsenalAssetBuilder.Build).");
            var faults = new List<string>();
            Assert.IsTrue(catalog.Validate(faults), string.Join("\n", faults));
            Assert.AreEqual(10, catalog.weapons.Count, "WP-069 B2: on klasik tanım (Special'lar B4)");
            Assert.AreSame(Arsenal.Get("ar_1"), Arsenal.Get(ClassicWeaponId.AssaultRifle));
            Assert.AreSame(Arsenal.Get("smg_1"), Arsenal.Get(ClassicWeaponId.AutoSmg));
            Assert.AreSame(Arsenal.Get("sg_1"), Arsenal.Get(ClassicWeaponId.CombatShotgun));
            Assert.AreSame(Arsenal.Get("sr_1"), Arsenal.Get(ClassicWeaponId.Sr1));
            Assert.AreSame(Arsenal.Get("sg_1"), Arsenal.Get("CombatShotgun"), "eski ad (legacyNames) aynı tanıma çıkar");
            Assert.AreEqual("AR1", Arsenal.Get("ar_1").AudioId, "audioId = weaponId büyük harf, rakamdan önceki _ düşer (§0 uzlaştırma)");
            Assert.AreEqual("RL1", WeaponDefinition.DeriveAudioId("rl_1"));
            Assert.AreEqual("SW_ARC", WeaponDefinition.DeriveAudioId("sw_arc"));
            Assert.AreSame(Arsenal.Get("ar_1").AudioId, Arsenal.Get("ar_1").AudioId, "önbellekli (ateş yolunda ayırma yok)");
            Assert.AreEqual("sg_1", Arsenal.Get("sg_1").AnimSet);
            Assert.IsTrue(Arsenal.Get("ar_1").presentation.fireLoop, "600 RPM auto → döngülü ateş (A4: Auto ∧ rpm ≥ 600)");
            Assert.IsTrue(Arsenal.Get("lmg_1").presentation.fireLoop, "720 RPM auto");
            Assert.IsFalse(Arsenal.Get("smg_2").presentation.fireLoop, "540 RPM → tek tek");
            Assert.IsFalse(Arsenal.Get("sg_1").presentation.fireLoop, "single");
            Assert.IsFalse(Arsenal.Get("rl_1").enabled, "rl_1 B3'e dek kapalı");

            var triples = new HashSet<string>();
            foreach (WeaponDefinition d in catalog.weapons)
            {
                var core = new List<UpgradeBranch>(d.upgrade.coreBranches);
                core.Sort();
                Assert.IsTrue(triples.Add(string.Join("+", core)), d.weaponId + ": çekirdek üçlüsü başka silahla aynı (WP-034)");
                Assert.That(Arsenal.LegacyPoseIndex(d.id), Is.InRange(0, 2), d.weaponId + ": M3 anim poz indeksi (WP-071'e dek sınıf kardeşi)");
            }
        }

        [Test]
        public void KonfigKopyasi_TanimSayilariniTasir_KonseySayilari()
        {
            PrototypeConfig config = Config;
            Assert.IsNotNull(config);
            config.Weapons.RebuildClassicView();
            Assert.AreEqual(9, config.Weapons.ClassicWeapons.Count, "9 etkin klasik (rl_1 B3)");
            Assert.IsNull(config.Weapons.FindExact(ClassicWeaponId.Rl1), "kapalı tanım kopyaya girmez");
            foreach (WeaponDefinition d in Arsenal.All)
            {
                if (!d.enabled || d.slot != WeaponSlotKind.Classic) continue;
                ClassicWeaponSettings a = config.Weapons.Find(d.id);
                string tag = d.weaponId + ": ";
                Assert.IsNotNull(a, tag + "kopya yok");
                Assert.AreSame(d, a.Definition, tag + "kopya tanımına bağlı değil");
                Assert.AreEqual(d.displayName, a.DisplayName, tag + "ad");
                Assert.AreEqual(d.fire.damage, a.Damage, 1e-5f, tag + "hasar");
                Assert.AreEqual(d.ShotsPerSecond, a.FireRate, 1e-4f, tag + "atış hızı");
                Assert.AreEqual(d.ammo.magazine, a.MagazineSize, tag + "şarjör");
                Assert.AreEqual(Mathf.Max(1, d.fire.pelletCount), a.PelletCount, tag + "pellet");
                Assert.AreEqual(d.accuracy.topDown.hip, a.SpreadDegrees, 1e-5f, tag + "tepeden hip = F1 kopyası");
                Assert.AreEqual(d.projectile.impulse, a.StaggerImpulse, 1e-5f, tag + "itki");
                Assert.AreEqual(d.projectile.maxRange, a.Range, 1e-5f, tag + "menzil");
                int ladder = d.unlock.kind == WeaponUnlockV3.Default ? 1
                    : d.unlock.kind == WeaponUnlockV3.Meta ? ClassicWeaponSettings.NotOnProfileLadder : d.unlock.profileLevel;
                Assert.AreEqual(ladder, a.UnlockProfileLevel, tag + "kilit seviyesi (DEC-095: meta ağacı silahı profil merdiveninde değil)");
                Assert.Greater(a.RecoilRecoverySeconds, 0f, tag + "his satırı (sınıf kardeşi) bağlı");
            }

            // Council anchors (DIRECTION §0/§1) — the three M3 slots now carry v3 numbers.
            ClassicWeaponSettings ar1 = config.Weapons.Find(ClassicWeaponId.AssaultRifle);
            Assert.AreEqual(12f, ar1.Damage, 1e-5f); Assert.AreEqual(10f, ar1.FireRate, 1e-4f); Assert.AreEqual(30, ar1.MagazineSize);
            ClassicWeaponSettings smg1 = config.Weapons.Find(ClassicWeaponId.AutoSmg);
            Assert.AreEqual(8f, smg1.Damage, 1e-5f); Assert.AreEqual(15f, smg1.FireRate, 1e-4f); Assert.AreEqual(42, smg1.MagazineSize);
            ClassicWeaponSettings sg1 = config.Weapons.Find(ClassicWeaponId.CombatShotgun);
            Assert.AreEqual(9f, sg1.Damage, 1e-5f); Assert.AreEqual(1.2f, sg1.FireRate, 1e-4f); Assert.AreEqual(8, sg1.PelletCount);
            Assert.AreEqual(0.75f + 0.55f * 7, sg1.ReloadTime, 1e-4f, "tek-fişek: M3 alanı = boştan tam şarjör");
            Assert.AreEqual(55f, config.Weapons.Find(ClassicWeaponId.Sr1).Damage, 1e-5f);
            Assert.AreEqual(100, config.Weapons.Find(ClassicWeaponId.Lmg1).MagazineSize);
            Assert.AreEqual(3, smg1.UnlockProfileLevel, "DEC-085 Ö-3: B seviye 3");
            Assert.AreEqual(5, sg1.UnlockProfileLevel, "DEC-085 Ö-3: C seviye 5");
            Assert.AreEqual(WeaponUnlockV3.Meta, Arsenal.Get("ar_2").unlock.kind, "yeni silahlar meta ağacıyla (DEC-095)");
            Assert.AreEqual(10, Arsenal.Get("rl_1").unlock.metaCost, "meta bandı §6 tablosu");
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
            System.Action<ProjectileExpireInfo> on = info => { if (info.Reason == ProjectileEndReason.Range) expired++; };
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
