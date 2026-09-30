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
        private readonly System.Collections.Generic.List<ZombieEnemy> _donuk = new System.Collections.Generic.List<ZombieEnemy>(4);

        [UnityTearDown]
        public IEnumerator Bitir()
        {
            ProjectileSystem.Clear();
            // Frozen bodies go back to the pool alive (WP-062J lesson): Update and the Animator on again.
            foreach (ZombieEnemy z in _donuk)
            {
                if (z == null) continue;
                z.enabled = true;
                foreach (Animator a in z.GetComponentsInChildren<Animator>(true)) a.enabled = true;
            }

            _donuk.Clear();
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

        // ------------------------------------------------------------------ C-00 01.10: no round through a visible body

        private static readonly RaycastHit[] s_dikis = new RaycastHit[32];

        /// <summary>2 = a hitbox of <paramref name="z"/> on the line, 1 = only its body capsule, 0 = neither (as the b5k measurement).</summary>
        private static int CizgiSinifi(ZombieEnemy z, Vector3 from, Vector3 dir, float len)
        {
            int n = Physics.RaycastNonAlloc(from, dir, s_dikis, len, ~0, QueryTriggerInteraction.Collide);
            bool hitbox = false, kapsul = false;
            for (int i = 0; i < n; i++)
            {
                Collider col = s_dikis[i].collider;
                if (col == null || col.GetComponentInParent<ZombieEnemy>() != z) continue;
                if (col.gameObject.layer == PrototypeLayers.EnemyHitbox) hitbox = true;
                else if (col.gameObject.layer == PrototypeLayers.Enemy) kapsul = true;
            }

            return hitbox ? 2 : kapsul ? 1 : 0;
        }

        private void Dondur(ZombieEnemy z)
        {
            z.enabled = false;
            foreach (Animator a in z.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            _donuk.Add(z);
            Physics.SyncTransforms();
        }

        /// <summary>Three flat top-down rounds (centre, ±0.1 m) at the shot height toward <paramref name="z"/>; each must damage it.</summary>
        private IEnumerator UcAtis(ZombieEnemy z, string poz, System.Collections.Generic.List<string> yanlis, System.Collections.Generic.List<string> rapor)
        {
            Vector3 player = _s.Rig.transform.position;
            Vector3 from = player + Vector3.up * _s.Controller.ShotRayHeight;
            Vector3 flat = z.transform.position - player; flat.y = 0f;
            Vector3 dir = flat.normalized, right = Vector3.Cross(Vector3.up, dir);
            ProjectileLaunch launch = ProjectileLaunch.From(Arsenal.Get(ClassicWeaponId.AssaultRifle), ClassicWeaponId.AssaultRifle, 30f);
            using (var sayac = new SpecialOlaySayaci())
            {
                foreach (float o in new[] { -0.1f, 0f, 0.1f })
                {
                    int sinif = CizgiSinifi(z, from + right * o, dir, flat.magnitude + 2f);
                    sayac.DamagedByLife.TryGetValue(z.LifeId, out int once);
                    ProjectileSystem.Spawn(Atis(from + right * o, from + right * o + dir, 1f, 30f), launch);
                    yield return SpecialWeaponSahne.Bekle(() => ProjectileSystem.LiveCount == 0, 2f, "mermi inmedi");
                    sayac.DamagedByLife.TryGetValue(z.LifeId, out int sonra);
                    string satir = poz + " " + o.ToString("+0.0;-0.0;0") + " m: line " + (sinif == 2 ? "hitbox" : sinif == 1 ? "capsule-only" : "none") + " → " + (sonra > once ? "hit" : "MISS");
                    rapor.Add(satir);
                    if (sonra <= once) yanlis.Add(satir);
                }
            }
        }

        /// <summary>
        /// C-00 01.10 (S-02 neck-seam measurement b5k: the top-down line at root + ShotRayHeight met only the body capsule of 6.2 %
        /// of attack frames — Special 12.9 %, peaks 26–51 %): a round through a living body's capsule that meets none of its
        /// hitboxes counts as UpperBody, so no top-down round passes through a visible zombie. Deterministic frozen poses: a
        /// Special's attack frame where the centre line is capsule-only (searched, then Update + Animator off), a still Standard
        /// and a staggering Standard; three flat rounds each (centre, ±0.1 m) must all damage the body. ShotRayHeight unchanged.
        /// </summary>
        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Tepeden_DuzHat_GorunurGovdedenGecmez_SaldiriBeklemeSendeleme()
        {
            yield return Kur();
            Vector3 c = _s.Rig.transform.position;
            Vector3 fwd = _s.Rig.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var yanlis = new System.Collections.Generic.List<string>();
            var rapor = new System.Collections.Generic.List<string>();

            // 1) a Special attacking the (invulnerable) player: wait for a frame whose centre line is capsule-only, then freeze it.
            Vector3 sp = c + fwd * 2.2f; sp.y = c.y;
            ZombieEnemy saldiran = EnemyFactory.Spawn(EnemyTier.Special, 6, sp, Quaternion.LookRotation(-fwd, Vector3.up), SpawnVisibility.Unknown);
            Assert.IsNotNull(saldiran, "Special doğmadı");
            saldiran.CompleteRise();
            saldiran.FitArmorComposition(false, false, false);
            bool bulundu = false;
            float t0 = Time.realtimeSinceStartup;
            while (!bulundu && Time.realtimeSinceStartup - t0 < 30f)
            {
                yield return null;
                Physics.SyncTransforms();
                if (!saldiran.IsAlive || saldiran.AttackPhase == ZombieAttackPhase.None) continue;
                Vector3 p0 = _s.Rig.transform.position + Vector3.up * _s.Controller.ShotRayHeight;
                Vector3 f = saldiran.transform.position - _s.Rig.transform.position; f.y = 0f;
                if (f.sqrMagnitude > 1e-4f && CizgiSinifi(saldiran, p0, f.normalized, f.magnitude + 2f) == 1) bulundu = true;
            }

            Assert.IsTrue(bulundu, "30 s içinde merkez çizginin yalnız kapsüle çarptığı saldırı karesi yok (b5k: Special saldırı karelerinin %12,9'u)");
            Dondur(saldiran);
            yield return UcAtis(saldiran, "Special attack " + saldiran.AttackPhase, yanlis, rapor);

            // 2) a still Standard (idle), off to the left
            ZombieEnemy duran = _s.Dogur(_s.Zemin(-4f, 1f));
            duran.CompleteRise();
            yield return null;
            Dondur(duran);
            yield return UcAtis(duran, "Standard idle", yanlis, rapor);

            // 3) a staggering Standard, off to the right
            ZombieEnemy sendeleyen = _s.Dogur(_s.Zemin(4f, 1f));
            sendeleyen.CompleteRise();
            yield return null;
            Vector3 geri = sendeleyen.transform.position - _s.Rig.transform.position; geri.y = 0f;
            sendeleyen.Animator.BeginStagger(geri.normalized, 1f, 0.6f, sendeleyen.transform);
            yield return new WaitForSeconds(0.25f);
            Dondur(sendeleyen);
            yield return UcAtis(sendeleyen, "Standard stagger", yanlis, rapor);

            Debug.Log("[DD-TEST] wp069.dikis.fix " + string.Join(" | ", rapor));
            Assert.IsEmpty(yanlis, "görünür gövdeden geçen mermi: " + string.Join(" | ", yanlis));
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
