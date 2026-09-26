using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DD.Prototype.Damage;
using DD.Prototype.Enemies;
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
    /// WP-076 (C-00 26.09) · the AI on the default V6 world, one round under pressure: the player (invulnerable, standing) at the
    /// rooms (R1 hall on its plinth, R2 pavilion, R3 kura, R4 tea house, the chūmon), the C8 bridge, the causeway and the waterfall
    /// shore; at each, the director fills to <see cref="Target"/> from the gates for <see cref="StationSeconds"/>. ASSERTED: the
    /// game's own nets stay silent (outside <see cref="KnownOpen"/>) — stuck removals and out-of-world removals 0 (recoveries reported) — and bodies reach the player at
    /// every station. REPORTED ([DD-OBS] wp076.v6ai): per station the spawns, the bodies that reached ≤ 3.5 m, and every body that
    /// stood ≥ 6 s more than 4 m away without moving 0.75 m (where, how far) — the map's trouble spots.
    /// </summary>
    [TestFixture]
    [Category("WP076")]
    public sealed class V6AiRoundTests
    {
        private const int Target = 12;

        /// <summary>
        /// KNOWN OPEN (26.09 measurement, reported to C-00 / S-05): the C1 causeway and the bridge ends by the waterfall — bodies stop at
        /// the dress slab joints (causeway z −23.2 between slab_bridge #3/#2; plank ends z −18.9 / −38.6) while the NavMesh says walkable
        /// (stuck recoveries 2 + 2, 0 reached on the causeway). Reported every run; removed from this set when the geometry is fixed.
        /// </summary>
        private static readonly HashSet<string> KnownOpen = new HashSet<string> { "C1_Causeway", "Waterfall" };
        private const float StationSeconds = 25f, ReachMetres = 3.5f, StallSeconds = 6f, StallMove = 0.75f;

        private static readonly (string name, Vector3 at)[] Stations =
        {
            ("R1_Hall", new Vector3(0f, 1.2f, 44f)), ("R2_Pavilion", new Vector3(35f, 0.25f, 28f)), ("R3_Kura", new Vector3(-39f, 0.3f, 40f)),
            ("R4_TeaHouse", new Vector3(-32f, 0.45f, -36f)), ("R6_Chumon", new Vector3(0f, 0f, 20.5f)), ("C8_Bridge", new Vector3(4f, 0.25f, -28.75f)),
            ("C1_Causeway", new Vector3(-8f, 0.25f, -33f)),
        };

        [TearDown]
        public void TearDown()
        {
            RunFlowClock.ClearHolds();
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator V6_OdalarKopruSelale_TakilmaSifir()
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

            var stations = new List<(string name, Vector3 at)>(Stations);
            GameObject falls = GameObject.Find("Waterfall");
            if (falls != null) stations.Add(("Waterfall", falls.transform.position));
            else Debug.LogWarning("[DD-OBS] wp076.v6ai Waterfall nesnesi yok");

            var rapor = new StringBuilder("[DD-OBS] wp076.v6ai\n");
            int sumRec = 0, sumRem = 0, sumOow = 0;
            var noReach = new List<string>();
            var oowAt = new List<string>();
            Action<DD.Prototype.Core.Config.EnemyTier, Vector3, ZombieEnemy.OutOfWorldReason, ZombieBodyState> onOow =
                (t, p, r, s) => oowAt.Add(r + "@(" + p.x.ToString("0.0") + "," + p.y.ToString("0.0") + "," + p.z.ToString("0.0") + ") " + t + " " + s);
            ZombieEnemy.OutOfWorldRemoved += onOow;
            try
            {
                foreach ((string name, Vector3 at) st in stations)
                {
                    float radius = st.name == "Waterfall" ? 8f : 3f;
                    if (!NavMesh.SamplePosition(st.at + Vector3.up * 0.5f, out NavMeshHit hit, radius, NavMesh.AllAreas))
                    {
                        rapor.AppendLine("  " + st.name + " NavMesh yok " + st.at);
                        noReach.Add(st.name + " (NavMesh yok)");
                        continue;
                    }

                    director.ForceActiveTarget(0, false);
                    director.ClearAllEnemies();
                    rig.Teleport(hit.position + Vector3.up * 0.05f, Quaternion.identity);
                    yield return null;
                    yield return null;
                    director.RefreshPointsNow();

                    int rec = ZombieEnemy.StuckRecoveries, rem = ZombieEnemy.StuckRemovals, oow = ZombieEnemy.OutOfWorldRemovals;
                    var anchors = new Dictionary<ZombieEnemy, (Vector3 anchor, float since, bool reached, bool reported)>();
                    int spawned = 0, reached = 0;
                    Action<EnemySpawnContext> onSpawn = c => spawned++;
                    director.EnemySpawned += onSpawn;
                    var stalls = new StringBuilder();
                    director.ForceActiveTarget(Target, true);
                    float t0 = Time.time, nextCheck = 0f;
                    try
                    {
                        while (Time.time - t0 < StationSeconds)
                        {
                            if (Time.time >= nextCheck)
                            {
                                nextCheck = Time.time + 0.5f;
                                Vector3 pp = rig.transform.position;
                                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                                for (int i = 0; i < live.Count; i++)
                                {
                                    ZombieEnemy z = live[i];
                                    if (z == null || !z.IsAlive) continue;
                                    Vector3 zp = z.transform.position;
                                    float d = Vector3.Distance(new Vector3(zp.x, 0f, zp.z), new Vector3(pp.x, 0f, pp.z));
                                    if (!anchors.TryGetValue(z, out var a)) { anchors[z] = (zp, Time.time, d <= ReachMetres, false); if (d <= ReachMetres) reached++; continue; }
                                    if (!a.reached && d <= ReachMetres) { a.reached = true; reached++; }
                                    if (Vector3.Distance(zp, a.anchor) > StallMove) { a.anchor = zp; a.since = Time.time; }
                                    else if (!a.reported && d > 4f && Time.time - a.since >= StallSeconds)
                                    {
                                        a.reported = true;
                                        stalls.Append(string.Format("({0:0.0},{1:0.0},{2:0.0} d={3:0.0} {4}) ", zp.x, zp.y, zp.z, d, z.Tier));
                                    }
                                    anchors[z] = a;
                                }
                            }
                            yield return null;
                        }
                    }
                    finally
                    {
                        director.EnemySpawned -= onSpawn;
                        director.ForceActiveTarget(0, false);
                    }

                    int dRec = ZombieEnemy.StuckRecoveries - rec, dRem = ZombieEnemy.StuckRemovals - rem, dOow = ZombieEnemy.OutOfWorldRemovals - oow;
                    rapor.AppendLine(string.Format("  {0,-12} at=({1:0.0},{2:0.0},{3:0.0}) spawned={4} reached={5} stuckRecover={6} stuckRemove={7} oow={8} stalls: {9}",
                        st.name, hit.position.x, hit.position.y, hit.position.z, spawned, reached, dRec, dRem, dOow, stalls.Length > 0 ? stalls.ToString() : "-"));
                    bool known = KnownOpen.Contains(st.name);
                    if (known) rapor.AppendLine("    ^ KNOWN OPEN (not asserted)");
                    else { sumRec += dRec; sumRem += dRem; sumOow += dOow; if (reached == 0) noReach.Add(st.name); }
                    director.ClearAllEnemies();
                    yield return null;
                }
            }
            finally
            {
                ZombieEnemy.OutOfWorldRemoved -= onOow;
                director.ForceActiveTarget(0, false);
                rig.Health.DiagnosisInvulnerable = false;
                rapor.AppendLine("  oow: " + (oowAt.Count > 0 ? string.Join("; ", oowAt) : "-"));
                Debug.Log(rapor.ToString());
            }

            Assert.AreEqual(0, sumRem, "takılma: watchdog gövde kaldırdı\n" + rapor);
            Assert.AreEqual(0, sumOow, "dünya dışı kaldırma\n" + rapor);
            // stuck RECOVERIES (repath + side step, the body stays) are reported, not asserted: 26.09 two runs 0 and 3 outside KnownOpen
            // (R3 / R4 / C8 once each, bodies far from the station — K1 kairō (−30.9, −5.8), north of the chūmon (3.0, 23.5)).
            rapor.AppendLine("  stuckRecover outside known = " + sumRec);
            Assert.IsEmpty(noReach, "oyuncuya ulaşılamayan istasyon\n" + rapor);
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
