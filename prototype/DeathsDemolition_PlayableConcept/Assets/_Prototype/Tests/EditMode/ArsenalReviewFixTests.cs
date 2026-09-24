using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-069 · the review R1-A findings on the weapon / projectile side, without a scene: the look-mode parallax
    /// guard (#1), the muzzle-path check (#2), projectile event keys and Cleared (#5), the touched set (#6),
    /// re-entrant spawns (#13), the look-frame cone (#16), config clones (#19); plus the S-10 WP-078 hooks.
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class ArsenalReviewFixTests
    {
        /// <summary>Far above the world so no scene collider is in any path.</summary>
        private static readonly Vector3 Sky = new Vector3(0f, 900f, 0f);

        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            ProjectileSystem.Clear();
            foreach (GameObject go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
        }

        // ------------------------------------------------------------------ #1 parallax

        [Test]
        public void Paralaks_NoktaNamluArkasinda_YaDa12DereceUstu_KameraIsininaDuser()
        {
            Vector3 ray = Vector3.forward;
            Vector3 fallback = Vector3.right;
            Assert.AreEqual(ray, PlayerWeaponBinding.ResolveLookAim(new Vector3(0f, 0f, -0.3f), ray, fallback), "nokta namlunun arkasında → kamera ışını");
            Vector3 ok = Quaternion.AngleAxis(8f, Vector3.up) * Vector3.forward * 10f;
            Assert.AreEqual(ok, PlayerWeaponBinding.ResolveLookAim(ok, ray, fallback), "8° → namlu → nokta");
            Vector3 wide = Quaternion.AngleAxis(20f, Vector3.up) * Vector3.forward * 0.5f;
            Assert.AreEqual(ray, PlayerWeaponBinding.ResolveLookAim(wide, ray, fallback), "20° > 12° → kamera ışını");
            Assert.AreEqual(ray, PlayerWeaponBinding.ResolveLookAim(Vector3.zero, ray, fallback), "sıfır → kamera ışını");
            Assert.AreEqual(fallback, PlayerWeaponBinding.ResolveLookAim(Vector3.zero, Vector3.zero, fallback), "ikisi de yok → yön");
        }

        // ------------------------------------------------------------------ #2 muzzle inside a collider

        private GameObject Wall(Vector3 centre, Vector3 size)
        {
            var go = new GameObject("TestWall");
            go.layer = PrototypeLayers.Environment;
            go.transform.position = centre;
            go.AddComponent<BoxCollider>().size = size;
            _made.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        [Test]
        public void NamluYolu_DuvarIcindekiNamlu_VeAradakiDuvar_Yakalanir()
        {
            Vector3 interior = Sky;
            Vector3 muzzle = Sky + Vector3.forward * 0.8f;
            Assert.IsFalse(WeaponController.MuzzlePathBlocked(interior, muzzle, out _), "boş yol");

            GameObject wall = Wall(Sky + Vector3.forward * 1.0f, new Vector3(2f, 2f, 0.8f));   // z 0.6–1.4: the muzzle is inside it
            Assert.IsTrue(WeaponController.MuzzlePathBlocked(interior, muzzle, out Vector3 dir), "namlu duvarın içinde → içeriden gelen ışın duvarı görür");
            Assert.Greater(Vector3.Dot(dir, Vector3.forward), 0.99f);

            Object.DestroyImmediate(wall);
            Wall(Sky + Vector3.forward * 0.4f, new Vector3(2f, 2f, 0.1f));
            Assert.IsTrue(WeaponController.MuzzlePathBlocked(interior, muzzle, out _), "oyuncu ile namlu arasındaki duvar");
        }

        // ------------------------------------------------------------------ #5 / #13 / #6 projectile system

        private static ShotRequest Shot(Vector3 origin, float range)
        {
            return new ShotRequest { Origin = origin, Direction = Vector3.forward, Damage = 10f, Range = range, PierceDamageFalloff = 1f };
        }

        private static ProjectileLaunch Launch(float speed, float range)
        {
            return new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Speed = speed, MaxRange = range,
                MaxLifetime = 10f, FalloffStart = range, FalloffEnd = range, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
        }

        [Test]
        public void MermiOlaylari_OrtakKimlik_ClearHerMermiyiCleared_IleKapatir()
        {
            ProjectileSystem.Clear();
            var spawned = new List<int>();
            var ended = new List<int>();
            System.Action<ProjectileSpawnInfo> onSpawn = i => spawned.Add(i.ProjectileId);
            System.Action<ProjectileExpireInfo> onEnd = i => { if (i.Reason == ProjectileEndReason.Cleared) ended.Add(i.ProjectileId); };
            ProjectileEvents.Spawned += onSpawn;
            ProjectileEvents.Expired += onEnd;
            try
            {
                ProjectileSystem.Spawn(Shot(Sky, 500f), Launch(50f, 500f));
                ProjectileSystem.Spawn(Shot(Sky + Vector3.right, 500f), Launch(50f, 500f));
                Assert.AreEqual(2, spawned.Count);
                Assert.AreNotEqual(spawned[0], spawned[1], "kimlik benzersiz");
                ProjectileSystem.Clear();
                CollectionAssert.AreEquivalent(spawned, ended, "Clear her mermiyi aynı kimlikle kapatır");
                Assert.AreEqual(0, ProjectileSystem.LiveCount);
            }
            finally
            {
                ProjectileEvents.Spawned -= onSpawn;
                ProjectileEvents.Expired -= onEnd;
            }
        }

        [Test]
        public void YenidenGiris_AdimIcindekiSpawn_AdimSonunaErtelenir()
        {
            ProjectileSystem.Clear();
            int returned = 0, pending = 0;
            System.Action<ProjectileExpireInfo> onEnd = i =>
            {
                if (i.Reason != ProjectileEndReason.Range) return;
                returned = ProjectileSystem.Spawn(Shot(Sky + Vector3.up, 500f), Launch(50f, 500f));
                pending = ProjectileSystem.PendingSpawnCount;
            };
            ProjectileEvents.Expired += onEnd;
            try
            {
                ProjectileSystem.Spawn(Shot(Sky, 0.5f), Launch(100f, 0.5f));
                ProjectileSystem.Step(0.02f);
                Assert.AreEqual(-1, returned, "adım içinde spawn kuyruğa girer");
                Assert.AreEqual(1, pending);
                Assert.AreEqual(1, ProjectileSystem.LiveCount, "adım bitince uygulandı");
                Assert.AreEqual(0, ProjectileSystem.PendingSpawnCount);
                Assert.AreEqual(Sky.y + 1f, ProjectileSystem.Get(0).Origin.y, 1e-4f, "kuyruktaki mermi");
            }
            finally
            {
                ProjectileEvents.Expired -= onEnd;
            }
        }

        [Test]
        public void DokunulanKume_SekizdenFazlaGovdeyiUnutmaz()
        {
            var p = new ProjectileState { Slot = 7 };
            for (int i = 1; i <= 60; i++) p.AddTouched(1000 + i);
            Assert.AreEqual(60, p.TouchedCount, "patlamanın dokunduğu 60 gövde hatırlanır (eski tavan 8)");
            Assert.IsTrue(p.HasTouched(1001) && p.HasTouched(1060));
            p.AddTouched(1030);
            Assert.AreEqual(60, p.TouchedCount, "tekrar eklenmez");
            Assert.AreEqual(192, ProjectileState.TouchedCapacity);
        }

        // ------------------------------------------------------------------ #16 look-frame cone

        [Test]
        public void BakisModu_Koni_EgikNisandaTamGenislik_Yuvarlak()
        {
            Random.InitState(5);
            Vector3 aim = Quaternion.AngleAxis(-35f, Vector3.right) * Vector3.forward;   // 35° up
            const float cone = 4f;
            float widest = 0f;
            for (int i = 0; i < 3000; i++)
            {
                Vector3 d = SpreadUtility.Apply(aim, cone, true);
                float a = Vector3.Angle(aim, d);
                Assert.LessOrEqual(a, cone + 1e-3f, "yuvarlak koni yarım açının içinde");
                widest = Mathf.Max(widest, a);
            }

            Assert.Greater(widest, 0.95f * cone, "koni kenarına ulaşır (eğik nişanda daralmaz)");

            float ringWidest = 0f;
            for (int k = 1; k < 8; k++)
            {
                ringWidest = Mathf.Max(ringWidest, Vector3.Angle(aim, SpreadUtility.ApplyRing(aim, cone, k, 8, 1f, 0f, 0.1f, true)));
            }

            Assert.AreEqual(cone, ringWidest, 0.05f, "bakış modunda halka daire (dikey 0,35 basıklığı yok)");
        }

        // ------------------------------------------------------------------ #19 clone

        [Test]
        public void KonfigKlonu_CanliSilahDuzenlemesiniTasir()
        {
            PrototypeConfig original = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            original.Weapons.RebuildClassicView();
            ClassicWeaponSettings smg = original.Weapons.Find(ClassicWeaponId.AutoSmg);
            float before = smg.Damage;
            PrototypeConfig clone = null;
            try
            {
                smg.Damage = before + 3f;
                clone = PrototypeConfig.Clone(original);
                Assert.AreEqual(before + 3f, clone.Weapons.Find(ClassicWeaponId.AutoSmg).Damage, 1e-5f, "Clone canlı düzenlemeyi taşır");
                Assert.AreNotSame(smg, clone.Weapons.Find(ClassicWeaponId.AutoSmg), "kopya, paylaşım değil");
                clone.Weapons.Find(ClassicWeaponId.AutoSmg).Damage = 99f;
                Assert.AreEqual(before + 3f, smg.Damage, 1e-5f, "klon yazması orijinale sızmaz");
            }
            finally
            {
                smg.Damage = before;
                if (clone != null) Object.DestroyImmediate(clone);
            }
        }

        // ------------------------------------------------------------------ S-10 WP-078 hooks

        [Test]
        public void S10_TurRaporu_SayaclarVeKatalogHizi_SilahAyariGuvenli()
        {
            ProjectileSystem.Clear();
            WeaponDefinition ar1 = Arsenal.Get(ClassicWeaponId.AssaultRifle);
            Assert.IsNotNull(ar1);
            ProjectileLaunch launch = ProjectileLaunch.From(ar1, ClassicWeaponId.AssaultRifle, 60f);
            ProjectileSystem.Spawn(Shot(Sky, 60f), launch);
            ProjectileSystem.Spawn(Shot(Sky + Vector3.right, 60f), launch);
            Assert.GreaterOrEqual(ProjectileSystem.PeakLiveCount, 2);
            Assert.AreEqual(ar1.projectile.speed, ProjectileSystem.LastLaunchSpeed(ClassicWeaponId.AssaultRifle), 0.01f * ar1.projectile.speed, "±%1");
            string line = ProjectileSystem.RoundReport(3);
            StringAssert.StartsWith("[Projectile] round=3 active=2 peak=", line);
            StringAssert.Contains(" overflow=", line);
            StringAssert.Contains("ar_1=260.0/260.0", line);

            ClassicWeaponId before = WeaponRegistry.SelectedWeapon;
            try
            {
                Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.Rl1), "WP-069 B3: rl_1 etkin");
                Assert.IsFalse(WeaponQaHooks.TrySetClassicWeapon((ClassicWeaponId)42));
                Assert.IsTrue(WeaponQaHooks.TrySetClassicWeapon(ClassicWeaponId.Sr1));
                Assert.AreEqual(ClassicWeaponId.Sr1, WeaponRegistry.SelectedWeapon);
            }
            finally
            {
                WeaponRegistry.SelectedWeapon = before;
            }
        }
    }
}
