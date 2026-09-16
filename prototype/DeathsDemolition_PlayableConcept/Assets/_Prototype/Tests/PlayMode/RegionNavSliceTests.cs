using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-030 · AI and spawn on the real world (Run scene, AP-005 world default): bridge
    /// chokepoint queue / reroute with real bodies, fit-scaled separation radius, the spawn
    /// rise handshake, the spawn visibility share, and the 40-zombie × 5-region stress run
    /// (stuck ratio, nav + crowd cost, allocation).
    /// </summary>
    [Category("WP030")]
    public sealed class RegionNavSliceTests
    {
        private const float KurulumZamanAsimi = 30f;
        private EnemySpawnDirector _director;
        private RunDirector _run;
        private PlayerRig _rig;

        private static IEnumerator Bekle(System.Func<bool> kosul, float saniye, string mesaj)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!kosul() && Time.realtimeSinceStartup - t0 < saniye) yield return null;
            Assert.IsTrue(kosul(), mesaj);
        }

        private IEnumerator RunSahnesiniHazirla()
        {
            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => SceneManager.GetActiveScene().name == PrototypeScenes.Run, KurulumZamanAsimi, "Run sahnesi etkin olmadı.");
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null, KurulumZamanAsimi, "Sahne kurulmadı.");
            yield return Bekle(() => RunDirector.Instance.Phase == RunPhase.Fighting, KurulumZamanAsimi, "Koşu Fighting fazına girmedi.");
            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;
            _rig.Health.DiagnosisInvulnerable = true;
            RegionNavProfile.ResetStatics();
            RegionNavRouter.ResetStatics();
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            RegionNavProfile.ResetStatics();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
        }

        private static Vector3 Yerde(Vector3 p)
        {
            Vector3 c = NavPathFollower.ClampToNavMesh(p, 6f);
            return c;
        }

        private ZombieEnemy Dogur(EnemyTier tier, Vector3 p)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, 6, Yerde(p), Quaternion.identity);
            Assert.IsNotNull(z, "spawn");
            return z;
        }

        // ------------------------------------------------------------------ bridge chokepoint with real bodies

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kopru_Bogaz_KuyrukVeyaKiyiRotasi_TakilmaYok_KapasiteAsimiSinirli()
        {
            yield return RunSahnesiniHazirla();
            RegionNavProfile profile = RegionNavProfile.Active;
            NavRegionVolume bridge = profile.Find(new Vector3(4f, 0f, -28f));
            Assert.IsNotNull(bridge);
            _rig.Teleport(Yerde(new Vector3(4f, 0f, -43f)), Quaternion.identity);
            yield return null;
            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 12; i++) bodies.Add(Dogur(EnemyTier.Standard, new Vector3(-2f + (i % 4) * 2f, 0f, -8f - (i / 4) * 2f)));
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            int samples = 0, overCapacity = 0, maxInside = 0, queuedSamples = 0, reroutedSamples = 0;
            float startMean = OrtalamaMesafe(bodies);
            while (Time.realtimeSinceStartup - t0 < 12f)
            {
                int inside = RegionNavRouter.Occupancy(bridge);
                maxInside = Mathf.Max(maxInside, inside);
                if (inside > bridge.capacity + 2) overCapacity++;
                foreach (ZombieEnemy z in bodies) { if (z.PathFollower != null && z.PathFollower.IsQueued) queuedSamples++; if (z.PathFollower != null && z.PathFollower.IsRerouted) reroutedSamples++; }
                samples++;
                yield return null;
            }

            float endMean = OrtalamaMesafe(bodies);
            Debug.Log("[DD-TEST] wp030.bridge samples=" + samples + " maxInside=" + maxInside + " overCap(+2)=" + overCapacity + " queuedSamples=" + queuedSamples + " reroutedSamples=" + reroutedSamples +
                      " queueEvents=" + RegionNavRouter.QueueEvents + " reroutes=" + RegionNavRouter.RerouteCount + " meanDist " + startMean.ToString("0.0") + "→" + endMean.ToString("0.0"));
            Assert.Greater(samples, 200);
            Assert.Less(endMean, startMean - 8f, "sürü oyuncuya yaklaşmalı (DEC-032 tehdit korunur)");
            Assert.IsTrue(RegionNavRouter.QueueEvents > 0 || RegionNavRouter.RerouteCount > 0, "12 gövde köprü boğazında ya kuyruğa girer ya kıyı rotasına döner");
            Assert.LessOrEqual((float)overCapacity / samples, 0.25f, "köprü içinde kapasite+2 üstü kare payı");
            EnemyFactory.ClearAll();
        }

        private static float OrtalamaMesafe(List<ZombieEnemy> bodies)
        {
            Transform p = CrowdService.PlayerTransform;
            float sum = 0f; int n = 0;
            foreach (ZombieEnemy z in bodies) { if (z != null && z.IsAlive) { sum += Vector3.Distance(z.transform.position, p.position); n++; } }
            return n > 0 ? sum / n : 0f;
        }

        // ------------------------------------------------------------------ fit-scaled separation radius + rise handshake

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator AyrismaYaricapi_FitOlcekli_KapsulAyni_RiseElSikisma()
        {
            yield return RunSahnesiniHazirla();
            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 12; i++) bodies.Add(Dogur(EnemyTier.Standard, new Vector3(-6f + i, 0f, 12f)));
            yield return null;
            RegionNavProfile profile = RegionNavProfile.Active;
            int skinned = 0;
            foreach (ZombieEnemy z in bodies)
            {
                var look = (ZombieAppearance)z.VersionVisual;
                float capsule = z.GetComponent<CharacterController>().radius;
                if (look.IsSkinned)
                {
                    skinned++;
                    float expected = capsule * profile.FitRadiusScale(look.Look.fit);
                    Assert.AreEqual(expected, ((ICrowdMember)z).Radius, 1e-4f, "fit " + look.Look.fit + " ayrışma yarıçapı");
                }

                Assert.AreEqual(PrototypeConfig.Active.Enemies.Find(EnemyTier.Standard).BodyRadius, capsule, 1e-4f, "kapsül yarıçapı config (fit'e göre ölçeklenmez)");
            }

            Debug.Log("[DD-TEST] wp030.fit skinned=" + skinned + "/12");

            // rise: no movement, no attack, crowd routes around, ledger unaffected (EnemyFactory spawn has no ledger); timer ends it
            ZombieEnemy r = bodies[0];
            _rig.Teleport(Yerde(r.transform.position + new Vector3(0f, 0f, -6f)), Quaternion.identity);
            yield return null;
            Vector3 before = r.transform.position;
            r.BeginRise(0.8f);
            Assert.IsTrue(r.IsRising);
            Assert.AreEqual(before, ((ICrowdMember)r).SteeringTarget, "yükselen gövde yerinde durur (kalabalık etrafından akar)");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
            Vector3 mid = r.transform.position; mid.y = before.y;
            Assert.Less(Vector3.Distance(before, mid), 0.15f, "rise sırasında yürümez");
            Assert.IsTrue(r.IsRising);
            yield return Bekle(() => !r.IsRising, 3f, "rise zamanlayıcısı bitmedi");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
            Vector3 after = r.transform.position; after.y = before.y;
            Assert.Greater(Vector3.Distance(before, after), 0.5f, "rise bitince yürür");
            r.BeginRise(5f); r.CompleteRise(); Assert.IsFalse(r.IsRising, "CompleteRise anında bitirir");
            EnemyFactory.ClearAll();
        }

        // ------------------------------------------------------------------ spawn visibility share (director round)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SpawnGorunurluk_RoundBoyunca_KameraDisiPayi_En_Az_060()
        {
            yield return RunSahnesiniHazirla();
            _rig.Teleport(Yerde(new Vector3(0f, 0f, -3f)), Quaternion.Euler(0f, 0f, 0f));
            yield return null;
            RunFlowClock.ClearHolds();
            _director.ForceActiveTarget(0, false);
            _run.BeginRound(3);
            yield return null;
            Time.captureDeltaTime = 0.45f;
            float t0 = Time.realtimeSinceStartup;
            while (_director.RemainingInPool > 0 && Time.realtimeSinceStartup - t0 < 60f)
            {
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = live.Count - 1; i >= 0; i--) if (live[i] != null && live[i].IsAlive) live[i].Kill();
                yield return null;
            }

            Time.captureDeltaTime = 0f;
            Assert.AreEqual(0, _director.RemainingInPool, "havuz tüketilemedi");
            Debug.Log("[DD-TEST] wp030.visibility spawns=" + _director.SpawnsThisRun + " onScreen=" + _director.OnScreenSpawnsThisRun + " offShare=" + _director.OffScreenSpawnShare.ToString("0.000"));
            Assert.GreaterOrEqual(_director.SpawnsThisRun, 29);
            Assert.GreaterOrEqual(_director.OffScreenSpawnShare, 0.6f, "kamera dışı spawn payı (teknik varsayılan ≥ %60)");
            _director.StopRound();
        }

        // ------------------------------------------------------------------ 40 zombies × 5 regions: stuck ratio, nav + crowd cost, allocation

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Stres_40Zombi_5Bolge_TakilmaOrani_PathingMaliyeti()
        {
            yield return RunSahnesiniHazirla();
            Vector3[] bolgeler = { new Vector3(0f, 0f, -3f), new Vector3(0f, 0f, 24f), new Vector3(26f, 0f, 0f), new Vector3(4f, 0f, -43f), new Vector3(-33f, 0f, 0f) };
            string[] adlar = { "Center", "North", "East", "South", "West" };
            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 40; i++) bodies.Add(Dogur((EnemyTier)(i % 4 == 3 ? 2 : 0), new Vector3(-12f + (i % 10) * 2.5f, 0f, 8f + (i / 10) * 2.5f)));
            yield return null;
            Assert.AreEqual(40, ZombieEnemy.Live.Count);
            var gcAlloc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            var lastPos = new Vector3[40]; var stillSince = new float[40];
            for (int i = 0; i < 40; i++) { lastPos[i] = bodies[i].transform.position; stillSince[i] = Time.realtimeSinceStartup; }
            int frames = 0, stuckSamples = 0, memberSamples = 0; double costSum = 0, crowdSum = 0, navSum = 0; long gcSum = 0; int gcFrames = 0; int stuckFramesMax = 0;
            var rapor = new System.Text.StringBuilder();
            NavPathService paths = CrowdService.Paths; paths.ResetTiming(); CrowdService.Solver.ResetPeaks();
            for (int b = 0; b < bolgeler.Length; b++)
            {
                _rig.Teleport(Yerde(bolgeler[b]), Quaternion.identity);
                yield return null;
                float t0 = Time.realtimeSinceStartup; int regionFrames = 0, regionStuck = 0; double regionCost = 0;
                while (Time.realtimeSinceStartup - t0 < 7f)
                {
                    float now = Time.realtimeSinceStartup;
                    float cost = CrowdService.Solver.LastSolveMilliseconds + paths.FrameQueryMilliseconds;
                    costSum += cost; crowdSum += CrowdService.Solver.LastSolveMilliseconds; navSum += paths.FrameQueryMilliseconds; regionCost += cost;
                    if (gcAlloc.Valid) { gcSum += gcAlloc.LastValue; gcFrames++; }
                    int stuckNow = 0;
                    for (int i = 0; i < 40; i++)
                    {
                        ZombieEnemy z = bodies[i]; if (z == null || !z.IsAlive) continue;
                        Vector3 p = z.transform.position;
                        if ((p - lastPos[i]).sqrMagnitude > 0.05f * 0.05f) { lastPos[i] = p; stillSince[i] = now; }
                        float dist = Vector3.Distance(p, _rig.transform.position);
                        bool nearPlayer = dist < 3.5f;   // arrived / attacking: standing still is correct
                        bool stuck = !nearPlayer && !z.IsRising && now - stillSince[i] >= 3f;
                        memberSamples++;
                        if (stuck) { stuckSamples++; stuckNow++; }
                    }

                    stuckFramesMax = Mathf.Max(stuckFramesMax, stuckNow);
                    frames++; regionFrames++;
                    yield return null;
                }

                rapor.Append(adlar[b]).Append(": frames=").Append(regionFrames).Append(" avgCost=").Append((regionCost / Mathf.Max(1, regionFrames)).ToString("0.000")).Append(" ms\n");
            }

            gcAlloc.Dispose();
            float stuckRatio = memberSamples > 0 ? (float)stuckSamples / memberSamples : 0f;
            double avgCost = costSum / Mathf.Max(1, frames), avgGc = gcFrames > 0 ? (double)gcSum / gcFrames : -1;
            Debug.Log("[DD-TEST] wp030.stress frames=" + frames + " avgNav+Crowd=" + avgCost.ToString("0.000") + " ms (crowd " + (crowdSum / Mathf.Max(1, frames)).ToString("0.000") + ", nav " + (navSum / Mathf.Max(1, frames)).ToString("0.000") +
                      ") peakCrowd=" + CrowdService.Solver.PeakSolveMilliseconds.ToString("0.00") + " avgNavPerQueryFrame=" + paths.AverageQueryMilliseconds.ToString("0.000") + " queries=" + paths.TotalQueries +
                      " stuckRatio=" + stuckRatio.ToString("0.0000") + " maxStuckInFrame=" + stuckFramesMax + " gcAlloc/frame=" + avgGc.ToString("0") + " B (" + (gcAlloc.Valid ? "recorder" : "no recorder") + ") reroutes=" + RegionNavRouter.RerouteCount + " queueEvents=" + RegionNavRouter.QueueEvents + "\n" + rapor);
            Assert.Greater(frames, 300);
            Assert.LessOrEqual(stuckRatio, 0.02f, "≥ 3 s hareketsiz zombi payı ≤ %2");
            Assert.LessOrEqual(avgCost, 1.5, "NavMesh sorgu + crowd çözüm ortalaması ≤ 1,5 ms @ 40");
            EnemyFactory.ClearAll();
        }
    }
}
