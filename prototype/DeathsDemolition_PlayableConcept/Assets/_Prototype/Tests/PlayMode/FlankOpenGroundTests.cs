using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-076 B9 (M-25, C-00 30.09; AI-SPAWN-06/07 on 12c7215c) · the WP-054 flank share on V6 open ground. The player
    /// (invulnerable, standing) on the central plaza near (0, 0, 0); the director fills to <see cref="Target"/> for
    /// <see cref="StationSeconds"/>. Every 0.5 s, among live bodies with a per-life flank sign ≠ 0 that are farther than
    /// CrowdFeelSettings.FlankStraightRadius, the share with FlankActive true is sampled.
    /// REPORTED ([DD-OBS] wp076.flank): bodies seen, flank-rolled bodies (observed vs expected from FlankShareOf per tier),
    /// far flank-rolled samples and the active share, the plaza's distance to the nearest NavMesh edge.
    /// ASSERTED (B9 30.09, after the first run: overall 0.44): among far flank samples whose line to the player is open
    /// (ZombieEnemy.FlankLineOpen, the WP-061M rule), the share whose flank the edge check accepts ≥ <see cref="MinAcceptedGivenOpen"/>
    /// — PROVISIONAL; catches the 12c7215c regression (flanks rejected on flat ground). The overall share is a report line.
    /// </summary>
    [TestFixture]
    [Category("WP076")]
    public sealed class FlankOpenGroundTests
    {
        private const int Target = 12;
        private const float StationSeconds = 20f, SampleSeconds = 0.5f;
        /// <summary>PROVISIONAL (WP-076 B9 M-25): pending the wp076.flank measurement.</summary>
        private const float ProvisionalMinActiveShare = 0.5f;   // report only (B9 30.09: overall share)
        /// <summary>PROVISIONAL (B9 M-25): share of far flank samples with an open line whose flank the edge check accepts.</summary>
        private const float MinAcceptedGivenOpen = 0.9f;
        private const int MinFarSamples = 10;

        [TearDown]
        public void TearDown()
        {
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kanat_V6AcikAlan_PayOlculur()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                                     && RunDirector.Instance.Phase == RunPhase.Fighting, 40f, "Run sahnesi / Fighting");
            RunDirector run = RunDirector.Instance;
            EnemySpawnDirector director = EnemySpawnDirector.Instance;
            PlayerRig rig = PlayerRegistry.Primary;

            director.ForceActiveTarget(0, false);
            run.BeginRound(1);
            yield return null;
            Assert.IsTrue(SpawnWorldProfile.IsV6, "varsayılan dünya V6 değil");
            rig.Health.DiagnosisInvulnerable = true;

            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(0f, 0.5f, 0f), out NavMeshHit plaza, 4f, NavMesh.AllAreas), "merkez meydanda NavMesh yok");
            float edgeMetres = NavMesh.FindClosestEdge(plaza.position, out NavMeshHit edge, NavMesh.AllAreas) ? edge.distance : -1f;

            director.ClearAllEnemies();
            rig.Teleport(plaza.position + Vector3.up * 0.05f, Quaternion.identity);
            yield return null;
            yield return null;
            director.RefreshPointsNow();

            CrowdFeelSettings feel = CrowdFeelSettings.Active;
            float straight = feel.FlankStraightRadius;
            var seen = new HashSet<ZombieEnemy>();
            int rolled = 0, farSamples = 0, activeSamples = 0;
            int lineOpenSamples = 0, acceptedSamples = 0;   // M-25: edge acceptance given an open line (the regression 12c7215c introduced)
            float expectedRolled = 0f;

            director.ForceActiveTarget(Target, true);
            float t0 = Time.time, nextSample = 0f;
            try
            {
                while (Time.time - t0 < StationSeconds)
                {
                    if (Time.time >= nextSample)
                    {
                        nextSample = Time.time + SampleSeconds;
                        Vector3 pp = rig.transform.position;
                        IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                        for (int i = 0; i < live.Count; i++)
                        {
                            ZombieEnemy z = live[i];
                            if (z == null || !z.IsAlive) continue;
                            if (seen.Add(z))
                            {
                                expectedRolled += feel.FlankShareOf(z.Tier);
                                if (z.FlankSign != 0) rolled++;
                            }
                            if (z.FlankSign == 0) continue;
                            Vector3 zp = z.transform.position;
                            float dx = zp.x - pp.x, dz = zp.z - pp.z;
                            if (dx * dx + dz * dz <= straight * straight) continue;
                            farSamples++;
                            if (z.FlankActive) activeSamples++;
                            if (z.FlankLineOpen) { lineOpenSamples++; if (z.FlankActive) acceptedSamples++; }
                        }
                    }
                    yield return null;
                }
            }
            finally
            {
                director.ForceActiveTarget(0, false);
            }
            director.ClearAllEnemies();

            int bodies = seen.Count;
            float activeShare = farSamples > 0 ? (float)activeSamples / farSamples : 0f;
            float lineOpenShare = farSamples > 0 ? (float)lineOpenSamples / farSamples : 0f;
            float acceptedShare = lineOpenSamples > 0 ? (float)acceptedSamples / lineOpenSamples : 0f;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[DD-OBS] wp076.flank.m25 lineOpenShare={0:0.00} ({1}/{2}) acceptedGivenOpen={3:0.00} ({4}/{1}) threshold {5:0.00} PROVISIONAL",
                lineOpenShare, lineOpenSamples, farSamples, acceptedShare, acceptedSamples, MinAcceptedGivenOpen));
            float rolledShare = bodies > 0 ? (float)rolled / bodies : 0f;
            float expectedShare = bodies > 0 ? expectedRolled / bodies : 0f;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[DD-OBS] wp076.flank at=({0:0.0},{1:0.0},{2:0.0}) edge={3:0.00}m bodies={4} flankRolled={5} rolledShare={6:0.00} expectedRolledShare={7:0.00} farSamples={8} activeSamples={9} activeShare={10:0.00} projectMax={11:0.00}m straight={12:0.0}m (threshold {13:0.00} PROVISIONAL)",
                plaza.position.x, plaza.position.y, plaza.position.z, edgeMetres, bodies, rolled, rolledShare, expectedShare,
                farSamples, activeSamples, activeShare, feel.FlankProjectMaxMetres, straight, ProvisionalMinActiveShare));

            Assert.Greater(bodies, 0, "meydanda hiç gövde doğmadı");
            Assert.GreaterOrEqual(farSamples, MinFarSamples, "ölçüm için yeterli uzak kanat örneği yok (rolled=" + rolled + ")");
            // WP-076 B9 (M-25): the overall active share mixes the WP-061M line-of-sight rule with the new edge check (first run
            // 30.09: 0.44 overall). The regression M-25 targets is the edge check rejecting flanks on open ground, so the assert is
            // on the accepted share GIVEN an open line; the overall share stays a report line.
            Assert.GreaterOrEqual(lineOpenSamples, MinFarSamples, "açık hat örneği yetersiz");
            Assert.GreaterOrEqual(acceptedShare, MinAcceptedGivenOpen,
                "açık hatta kanat kabul payı düşük: " + acceptedSamples + "/" + lineOpenSamples + " (geçici eşik, ölçüm bekleniyor)");
        }

        private static IEnumerator Bekle(Func<bool> kosul, float saniye, string aciklama)
        {
            float t = Time.realtimeSinceStartup;
            while (!kosul())
            {
                if (Time.realtimeSinceStartup - t > saniye) Assert.Fail("ZAMAN AŞIMI (" + saniye + " sn): " + aciklama);
                yield return null;
            }
        }
    }
}
