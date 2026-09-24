using System;
using System.Collections;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-069 B1 · ADR-069-1 in the Run scene: a projectile lands AFTER its flight (distance / speed),
    /// a 600 m/s round cannot tunnel through a 5 cm wall even in one long step, the classic weapon
    /// fires projectiles through the real controller path and they hurt, and stepping / impacting
    /// projectiles allocate nothing per frame (WP-059D referee form: amplified frame vs control
    /// frame, min over pairs, 64 KB positive control).
    /// </summary>
    [TestFixture]
    [Category("WP069")]
    public sealed class ProjectileSliceTests
    {
        private SpecialWeaponSahne _s;
        private GameObject _wall;

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            ProjectileSystem.Clear();
            if (_wall != null) UnityEngine.Object.Destroy(_wall);
            if (_s != null)
            {
                yield return _s.Bitir();
                _s = null;
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator Kur()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
            LogAssert.ignoreFailingMessages = true;
        }

        private static ShotRequest Atis(Vector3 from, Vector3 to, float damage, float range)
        {
            return new ShotRequest
            {
                Origin = from, Direction = (to - from).normalized, Damage = damage, Range = range,
                ImpactForce = DamageForce.RifleBullet, Source = DamageSourceKind.Bullet, Power = WeaponPower.Medium,
                PierceDamageFalloff = 1f
            };
        }

        /// <summary>Aim plane height of the cursor projection (PlayerConfig.AimPlaneHeight, as CoreCombatSliceTests).</summary>
        private const float NisanDuzlemi = 1.15f;

        private static Vector2 Ekran(Vector3 world)
        {
            DD.Prototype.CameraSystem.PrototypeCameraRig rig = DD.Prototype.CameraSystem.PrototypeCameraRig.Active;
            Camera cam = rig != null ? rig.Camera : Camera.main;
            Vector3 p = cam.WorldToScreenPoint(world);
            return new Vector2(p.x, p.y);
        }

        private static ProjectileLaunch Firlat(float speed, float range)
        {
            return new ProjectileLaunch
            {
                WeaponId = ClassicWeaponId.AssaultRifle, Kind = ProjectileKind.Bullet, Caliber = WeaponCaliber.Medium,
                Speed = speed, Gravity = 0f, MaxRange = range, MaxLifetime = 5f,
                FalloffStart = range, FalloffEnd = range, FalloffMinShare = 1f, PenetrationDamageKeep = 1f
            };
        }

        // ------------------------------------------------------------------ flight time

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Mermi_UcusSuresindenSonraHasarVerir_TekIsabet()
        {
            yield return Kur();
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 16f));
            yield return new WaitForSeconds(1.5f);
            Collider torso = z.HitboxFor(BodyRegion.UpperBody);
            Assert.IsNotNull(torso);
            Vector3 target = torso.bounds.center;
            Vector3 from = new Vector3(target.x, target.y, target.z - 16f);

            int impacts = 0;
            float impactAt = -1f;
            Action<ProjectileImpactInfo> on = info => { if (info.Flesh) { impacts++; if (impactAt < 0f) impactAt = Time.time; } };
            ProjectileEvents.Impact += on;
            var sayac = new SpecialOlaySayaci();
            try
            {
                float spawnedAt = Time.time;
                ProjectileSystem.Spawn(Atis(from, target, 7f, 40f), Firlat(80f, 40f));   // 16 m at 80 m/s = 0.20 s
                yield return SpecialWeaponSahne.Bekle(() => ProjectileSystem.LiveCount == 0, 3f, "mermi hedefe ulaşmadı");
                Assert.AreEqual(1, impacts, "tek gövde isabeti");
                // Health or armour: the spawn may roll a torso plate, which takes the hit instead of health.
                Assert.AreEqual(1, sayac.Damaged.Count, "tek hasar olayı");
                Assert.Greater(sayac.Damaged[0].Result.HealthDamage + sayac.Damaged[0].Result.ArmorDamage, 0f, "isabet hasar vermedi");
                float flight = impactAt - spawnedAt;
                Debug.Log("[DD-TEST] wp069.flight 16 m @ 80 m/s = " + flight.ToString("F3") + " s");
                Assert.That(flight, Is.InRange(0.15f, 0.40f), "uçuş süresi mesafe/hız ile uyumsuz (0,20 s beklenir)");
            }
            finally
            {
                ProjectileEvents.Impact -= on;
                sayac.Dispose();
            }
        }

        // ------------------------------------------------------------------ tunnelling

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HizliMermi_InceDuvardan_Gecemez_TekUzunAdimdaBile()
        {
            yield return Kur();
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 14f));
            yield return new WaitForSeconds(1.5f);
            Vector3 target = z.HitboxFor(BodyRegion.UpperBody).bounds.center;
            Vector3 from = new Vector3(target.x, target.y, target.z - 14f);

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "WP069-InceDuvar";
            _wall.layer = PrototypeLayers.Environment;
            _wall.transform.position = new Vector3(from.x, from.y, from.z + 8f);
            _wall.transform.localScale = new Vector3(3f, 3f, 0.05f);
            Physics.SyncTransforms();

            float hp0 = z.Health;
            bool stoppedAtWall = false;
            Action<ProjectileImpactInfo> on = info => { if (!info.Flesh && info.Stopped && Mathf.Abs(info.Position.z - _wall.transform.position.z) < 0.1f) stoppedAtWall = true; };
            ProjectileEvents.Impact += on;
            try
            {
                ProjectileSystem.Spawn(Atis(from, target, 50f, 40f), Firlat(600f, 40f));
                ProjectileSystem.Step(1f / 20f);   // 30 m in ONE sweep, the wall is 8 m away
                Assert.IsTrue(stoppedAtWall, "600 m/s mermi 5 cm duvarı tek adımda geçti (tünelleme)");
                Assert.AreEqual(0, ProjectileSystem.LiveCount);
                Assert.AreEqual(hp0, z.Health, 1e-4f, "duvarın arkasındaki gövde vuruldu");
            }
            finally
            {
                ProjectileEvents.Impact -= on;
            }
        }

        // ------------------------------------------------------------------ the real controller path

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator KlasikSilah_Projectile_Atar_VeHedefiVurur()
        {
            yield return Kur();
            _s.Controller.EquipClassic(ClassicWeaponId.AssaultRifle, _s.Config);
            Assert.IsNotNull(_s.Controller.Classic.Definition, "A tüfek v3 tanımına bağlı değil (Arsenal)");
            ZombieEnemy z = _s.Dogur(_s.Zemin(0f, 7f));
            yield return new WaitForSeconds(1.5f);

            float hp0 = z.Health;
            int spawned0 = ProjectileSystem.SpawnedTotal;
            int started = 0, stopped = 0;
            Action<ClassicWeaponId> onStart = id => started++;
            Action<ClassicWeaponId> onStop = id => stopped++;
            WeaponEvents.FireStarted += onStart;
            WeaponEvents.FireStopped += onStop;
            try
            {
                // The real aim path (cursor → aim plane → facing → muzzle), re-aimed every frame at the body on the
                // aim plane (CoreCombatSliceTests' method: the zombie walks and flanks, a fixed cursor would miss).
                for (int f = 0; f < 30; f++) { _s.Girdi.Imlec = Ekran(z.transform.position + Vector3.up * NisanDuzlemi); yield return null; }
                _s.Girdi.AtesBasili = true;
                _s.Girdi.AtesBasti = true;
                float until = Time.time + 3f;
                while (ProjectileSystem.SpawnedTotal < spawned0 + 3 && Time.time < until)
                {
                    _s.Girdi.Imlec = Ekran(z.transform.position + Vector3.up * NisanDuzlemi);
                    yield return null;
                }

                Assert.GreaterOrEqual(ProjectileSystem.SpawnedTotal, spawned0 + 3, "tetik projectile üretmedi");
                _s.Girdi.AtesBasili = false;
                _s.Girdi.AtesBasti = false;
                yield return SpecialWeaponSahne.Bekle(() => ProjectileSystem.LiveCount == 0, 3f, "mermiler sönmedi");
                yield return null;
                Assert.Less(z.Health, hp0, "klasik silahın mermileri hedefe hasar vermedi");
                Assert.GreaterOrEqual(started, 1, "FireStarted yayınlanmadı");
                Assert.GreaterOrEqual(stopped, 1, "FireStopped yayınlanmadı");
                Debug.Log("[DD-TEST] wp069.controller shots=" + (ProjectileSystem.SpawnedTotal - spawned0) + " hp " + hp0 + " → " + z.Health);
            }
            finally
            {
                WeaponEvents.FireStarted -= onStart;
                WeaponEvents.FireStopped -= onStop;
            }
        }

        // ------------------------------------------------------------------ allocation (WP-059D referee form)

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Mermiler_UcarkenVeCarparken_KareBasinaAyirmaYok()
        {
            const int Amplify = 40, Pairs = 3, Slack = 12;
            yield return Kur();
            int fpsWas = Application.targetFrameRate;
            Application.targetFrameRate = -1;   // WP-059: a capped rate freezes the recorder's LastValue
            bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.layer = PrototypeLayers.Environment;
            Vector3 basePos = _s.Rig.transform.position + new Vector3(0f, 60f, 0f);   // above the world: only our wall is there
            _wall.transform.position = basePos + new Vector3(0f, 0f, 6f);
            _wall.transform.localScale = new Vector3(20f, 20f, 0.2f);
            Physics.SyncTransforms();

            // Warm-up: one full volley so pools / first impacts size themselves.
            for (int k = 0; k < Amplify; k++) ProjectileSystem.Spawn(Atis(basePos + Vector3.right * (k * 0.2f - 4f), basePos + new Vector3(k * 0.2f - 4f, 0f, 6f), 1f, 30f), Firlat(90f, 30f));
            yield return new WaitForSeconds(0.6f);

            using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
            using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                yield return null;
                yield return null;
                long controlMin = long.MaxValue, flyMin = long.MaxValue, hitMin = long.MaxValue;
                var pairs = new System.Text.StringBuilder();
                for (int pair = 0; pair < Pairs; pair++)
                {
                    yield return new WaitForSeconds(0.3f);
                    yield return null;
                    long control = count.LastValue;   // no projectiles alive

                    // 40 projectiles at 90 m/s towards the wall 6 m ahead: they fly ~5 frames, then impact.
                    for (int k = 0; k < Amplify; k++) ProjectileSystem.Spawn(Atis(basePos + Vector3.right * (k * 0.2f - 4f), basePos + new Vector3(k * 0.2f - 4f, 0f, 6f), 1f, 30f), Firlat(90f, 30f));
                    yield return null;                // spawn frame (+ first steps)
                    yield return null;
                    long fly = count.LastValue;       // a frame of 40 stepping + drawing
                    long hit = long.MaxValue;
                    while (ProjectileSystem.LiveCount > 0)
                    {
                        yield return null;
                        hit = Math.Min(hit, count.LastValue);   // frames containing impacts
                    }

                    yield return null;
                    hit = Math.Min(hit, count.LastValue);
                    controlMin = Math.Min(controlMin, control);
                    flyMin = Math.Min(flyMin, fly);
                    hitMin = Math.Min(hitMin, hit);
                    pairs.Append(' ').Append(control).Append('/').Append(fly).Append('/').Append(hit);
                }

                var keep = new object[64];
                for (int i = 0; i < keep.Length; i++) keep[i] = new byte[1024];
                yield return null;
                long positive = count.LastValue, positiveBytes = bytes.LastValue;
                GC.KeepAlive(keep);

                Debug.Log("[DD-TEST] wp069.alloc projectiles/volley=" + Amplify + " control/fly/impact per pair:" + pairs + " positive=" + positive + " / " + positiveBytes + " B");
                Assert.GreaterOrEqual(positive, 64, "pozitif kontrol: sayaç 64 × 1 KB ayırmayı görmedi");
                Assert.LessOrEqual(flyMin - controlMin, Slack, "uçan 40 mermi karede ayırıyor: +" + (flyMin - controlMin));
                Assert.LessOrEqual(hitMin - controlMin, Slack, "çarpan mermiler karede ayırıyor: +" + (hitMin - controlMin));
            }

            UnityEngine.Profiling.Profiler.enabled = profilerWas;
            Application.targetFrameRate = fpsWas;
        }
    }
}
