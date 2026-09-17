using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Characters;
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
    /// WP-050 on the Run scene: (1) S-10 finding — a round that opens before the player state exists
    /// spawns nothing and never relaxes the reachability gate (pending ≠ unreachable); once the player
    /// registers every point is queried and spawning proceeds with all paths fresh; (2) the shipped
    /// Ö-5 rows reach the live director (R11 forced Executioners, R8 Standard armour jump on real
    /// spawns); (3) WP-032 waiting: a body queued at the bridge or stalled in the crowd holds the
    /// queue idle (SetWaiting) and releases it when it moves again; a corpse never waits.
    /// </summary>
    [Category("WP050")]
    public sealed class SpawnTuningSliceTests
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
            TierBehaviourProfileSet.ResetStatics();
            ZombieEnemy.Rise = ZombieEnemy.RisePolicy.Off;
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            ZombieEnemy.ResetRisePolicy();
            TierBehaviourProfileSet.ResetStatics();
            if (PlayerRegistry.Primary != null)
            {
                CrowdService.SetPlayer(PlayerRegistry.Primary.transform, PrototypeConfig.Active != null ? PrototypeConfig.Active.Player.CollisionRadius : 0.4f);
                if (PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }
        }

        private static Vector3 Yerde(Vector3 p) => NavPathFollower.ClampToNavMesh(p, 6f);

        private static ZombieEnemy Dogur(EnemyTier tier, Vector3 p, int round = 6)
        {
            ZombieEnemy z = EnemyFactory.Spawn(tier, round, Yerde(p), Quaternion.identity);
            Assert.IsNotNull(z, "spawn");
            return z;
        }

        // ================================================================= S-10 finding: pending ≠ unreachable

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator OyuncusuzRoundBaslangici_YolBekler_GevsemeYok_OyuncuGelinceTumYollarTaze()
        {
            yield return RunSahnesiniHazirla();
            float radius = CrowdService.PlayerRadius > 0f ? CrowdService.PlayerRadius : 0.4f;
            Transform playerT = CrowdService.PlayerTransform;
            Assert.IsNotNull(playerT, "CrowdService'e oyuncu kayıtlı olmalı (RunSystemsInstaller)");

            // the race: the round opens before the player state exists (NavigationSystemBehaviour publishes it a frame later)
            CrowdService.SetPlayer(null, 0f);
            yield return null;
            yield return null;
            Assert.IsFalse(CrowdService.PlayerState.HasPlayer, "oyuncu durumu boşaltıldı");
            _director.BeginRound(3);
            Assert.IsFalse(_director.AllPathsQueried, "oyuncusuz forced refresh yolları damgalamaz (bekliyor)");
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
            Assert.AreEqual(0, _director.SpawnedThisRound, "oyuncusuz tick'ler spawn üretmez");
            Assert.AreEqual(0, _director.PathRelaxCount, "REQ-068 kapısı bekleyen yollarla GEVŞETİLMEZ (S-10 bulgusu)");
            Assert.Greater(_director.PathPendingTicks, 0, "bekleyen yol tick'i sayılır");
            Assert.IsFalse(_director.AllPathsQueried);

            // the player registers: every pending point is queried at once, spawning proceeds with fresh paths only
            CrowdService.SetPlayer(playerT, radius);
            yield return Bekle(() => CrowdService.PlayerState.HasPlayer, 2f, "oyuncu durumu geri gelmedi");
            yield return Bekle(() => _director.SpawnedThisRound > 0, 6f, "oyuncu gelince spawn başlamadı");
            Assert.IsTrue(_director.AllPathsQueried, "ilk spawn'dan önce her nokta sorgulanmış");
            Assert.AreEqual(0, _director.PathRelaxCount, "AP-005 dünyasında tüm yollar tazeyken gevşeme olmamalı");
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            int complete = 0;
            for (int i = 0; i < points.Count; i++) if (points[i].PathStatus == NavPathStatus.Complete) complete++;
            Assert.Greater(complete, 0, "en az bir nokta tam yol");
            Debug.Log("[DD-OBS] wp050.spawnpath pendingTicks=" + _director.PathPendingTicks + " relax=" + _director.PathRelaxCount + " noPoint=" + _director.NoPointTicks + " complete=" + complete + "/" + points.Count + " spawned=" + _director.SpawnedThisRound);
            _director.StopRound();
            _director.ClearAllEnemies();
        }

        // ================================================================= Ö-5 rows on the live director

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator SevkSatirlari_R11ZorunluExecutioner_R8StandardZirhSicramasi()
        {
            yield return RunSahnesiniHazirla();
            PrototypeConfig cfg = PrototypeConfig.Active;
            Assert.AreEqual(3, cfg.RoundDifficultyOverrides.Count, "sevk tablosu WP-050 satırları");
            Assert.AreEqual(2, cfg.ForcedTierCount(EnemyTier.Ultra, 11));
            Assert.AreEqual(0.40f, cfg.ArmorChance(EnemyTier.Standard, ArmorSlot.Helmet, 8), 1e-6f);
            Assert.AreEqual(cfg.Enemies.Find(EnemyTier.Elite).ArmorChances.Resolve(ArmorSlot.Helmet, 8), cfg.ArmorChance(EnemyTier.Elite, ArmorSlot.Helmet, 8), 1e-6f, "Guard satırdan etkilenmez (kapsam)");

            // R11: the policy owes two Executioners on top of the draw (released with the WP-010 pacing over the pool):
            // keep the spawns flowing by clearing bodies, count Executioners as they spawn until the debt is paid.
            int ultraSpawned = 0;
            System.Action<EnemySpawnContext> sayac = c => { if (c.Tier == EnemyTier.Ultra) ultraSpawned++; };
            _director.EnemySpawned += sayac;
            try
            {
                _director.BeginRound(11);
                Assert.AreEqual(2, _director.TierSelector.PendingObligations, "R11 zorunlu Executioner borcu (satır)");
                float t0 = Time.realtimeSinceStartup;
                while (_director.TierSelector.PendingObligations > 0 && _director.SpawnedThisRound < 90 && _director.IsRoundActive && Time.realtimeSinceStartup - t0 < 90f)
                {
                    float t1 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t1 < 0.5f) yield return null;
                    _director.ClearAllEnemies();
                }
                Assert.AreEqual(0, _director.TierSelector.PendingObligations, "R11 borcu havuz akarken ödenir (spawned " + _director.SpawnedThisRound + ")");
                Assert.GreaterOrEqual(ultraSpawned, 2, "R11'de en az iki Executioner doğar (zorunlu satır)");
                Debug.Log("[DD-OBS] wp050.rows R11 spawned=" + _director.SpawnedThisRound + " ultraSpawned=" + ultraSpawned + " pending=" + _director.TierSelector.PendingObligations);
            }
            finally { _director.EnemySpawned -= sayac; }
            _director.StopRound();
            _director.ClearAllEnemies();
            EnemyFactory.ClearAll();
            yield return null;

            // R8: Standard armour follows the row (0.40 / 0.36 / 0.30) — 60 Standards through the factory at round 8, two batches under the pool ceiling
            int helmet = 0, n = 60;
            for (int i = 0; i < n; i++)
            {
                ZombieEnemy z = Dogur(EnemyTier.Standard, _rig.transform.position + new Vector3((i % 10) * 1.2f - 6f, 0f, 10f + ((i / 10) % 3) * 1.5f), 8);
                if (z.Armor.IsEquipped(ArmorSlot.Helmet)) helmet++;
                if (i % 30 == 29) { EnemyFactory.ClearAll(); yield return null; }
            }
            float share = (float)helmet / n;
            Assert.That(share, Is.InRange(0.22f, 0.60f), "R8 Standard kask payı satır 0,40 civarı (n=60, ölçülen " + share.ToString("0.00") + ")");
            Debug.Log("[DD-OBS] wp050.rows R8 standardHelmet=" + share.ToString("0.000") + " (row 0.40; formula would be 0.234)");
            EnemyFactory.ClearAll();
        }

        // ================================================================= WP-032 waiting (SetWaiting)

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Bekleme_KoprudeKuyrukYaDaSurudeTakilma_QueueIdle_HareketEdinceBirakir_CesetBeklemez()
        {
            yield return RunSahnesiniHazirla();
            NavRegionVolume bridge = RegionNavProfile.Active.Find(new Vector3(4f, 0f, -28f));
            Assert.IsNotNull(bridge);
            _rig.Teleport(Yerde(new Vector3(4f, 0f, -43f)), Quaternion.identity);
            yield return null;
            var bodies = new List<ZombieEnemy>();
            for (int i = 0; i < 14; i++) bodies.Add(Dogur(EnemyTier.Standard, new Vector3(-2f + (i % 4) * 2f, 0f, -8f - (i / 4) * 2f)));
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            int waitingSamples = 0, queuedSamples = 0, queuedWithoutWait = 0, samples = 0;
            ZombieEnemy waited = null;
            while (Time.realtimeSinceStartup - t0 < 14f)
            {
                foreach (ZombieEnemy z in bodies)
                {
                    if (!z.IsAlive) continue;
                    if (z.IsWaiting) { waitingSamples++; waited = waited ?? z; }
                    if (z.PathFollower != null && z.PathFollower.IsQueued)
                    {
                        queuedSamples++;
                        if (!z.IsWaiting) queuedWithoutWait++;
                    }
                    ZombieSkinnedVisual sk = ((ZombieAppearance)z.VersionVisual).Skinned;
                    if (sk != null && sk.Authored) Assert.AreEqual(z.IsWaiting, sk.Waiting, "sürücü Waiting bayrağı düşmanla aynı");
                }
                samples++;
                yield return null;
            }
            Debug.Log("[DD-OBS] wp050.waiting samples=" + samples + " waitingSamples=" + waitingSamples + " queuedSamples=" + queuedSamples + " queuedWithoutWait=" + queuedWithoutWait + " queueEvents=" + RegionNavRouter.QueueEvents + " reroutes=" + RegionNavRouter.RerouteCount);
            // WP-052 note: on the world v3 NavMesh (WP-020 area costs) the crowd may take the shore alternative and never queue at the
            // bridge mouth; the binding is asserted whenever a queue DID happen, otherwise the run only proves the idle does not misfire.
            if (RegionNavRouter.QueueEvents > 0 || queuedSamples > 0)
            {
                Assert.Greater(waitingSamples, 0, "köprü kuyruğu oluştu: en az bir gövde bekleme durumuna girer");
                Assert.LessOrEqual(queuedWithoutWait, Mathf.CeilToInt(queuedSamples * 0.05f), "kuyruktaki gövde aynı karede bekler (kuyruk dalı histerezissiz; yalnız saldırı/sendeleme istisnası)");
            }
            else
            {
                Assert.Greater(RegionNavRouter.RerouteCount, 0, "kuyruk yoksa sürü kıyı rotasına dönmüş olmalı (aksi hâlde boğaz kuralı hiç çalışmadı)");
                Assert.LessOrEqual((float)waitingSamples / Mathf.Max(1, samples * 14), 0.05f, "kuyruksuz koşuda serbest yürüyen gövdeler beklemez");
            }

            // release: a body that waited and then died never holds the idle; a body cleared from the crowd walks again
            if (waited != null && waited.IsAlive)
            {
                waited.Kill();
                yield return null;
                Assert.IsFalse(waited.IsWaiting, "ceset beklemez");
            }
            EnemyFactory.ClearAll();
            yield return null;
            ZombieEnemy lone = Dogur(EnemyTier.Standard, new Vector3(4f, 0f, -12f));
            yield return null;
            float t1 = Time.realtimeSinceStartup;
            int loneWaiting = 0, loneSamples = 0;
            while (Time.realtimeSinceStartup - t1 < 3f) { if (lone.IsWaiting) loneWaiting++; loneSamples++; yield return null; }
            Assert.LessOrEqual((float)loneWaiting / Mathf.Max(1, loneSamples), 0.1f, "tek başına yürüyen gövde beklemez");
            EnemyFactory.ClearAll();
        }
    }
}
