using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
#endif
using DD.Prototype.Damage;
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
        /// Stations reported but not asserted. Empty since WP-076 B9 (C-00 30.09): C1 left after two guarded r3 PlayMode runs
        /// (7 / 12 reached, 0 stalls, 0 recoveries; every unreached body still walking, no queue / wait — the 25 s window, not a stop),
        /// on top of S-05's pond-guard fix 7bcb94fd. Waterfall is strict: every observed stall fails, and a real enemy starts at the
        /// necessary near corner with the normal crowd solve.
        /// </summary>
        private static readonly HashSet<string> KnownOpen = new HashSet<string>();
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
#if UNITY_EDITOR
            C1ContactObserver.ResetHook();
#endif
            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null) PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>Station-loop results shared by the acceptance test and the C1 diagnostic (WP-076 B9 M-22).</summary>
        private sealed class RoundResult
        {
            public readonly StringBuilder Rapor = new StringBuilder("[DD-OBS] wp076.v6ai\n");
            public readonly List<string> NoReach = new List<string>();
            public int SumRec, SumRem, SumOow, WaterfallStalls;
            public bool NearCornerReached;
        }

        private RunDirector _run;
        private EnemySpawnDirector _director;
        private PlayerRig _rig;

        private IEnumerator Kur()
        {
            LogAssert.ignoreFailingMessages = true;
            RunFlowClock.ClearHolds();
            SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;
            yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                                     && RunDirector.Instance.Phase == RunPhase.Fighting, 40f, "Run sahnesi / Fighting");
            _run = RunDirector.Instance;
            _director = EnemySpawnDirector.Instance;
            _rig = PlayerRegistry.Primary;

            _director.ForceActiveTarget(0, false);
            _run.BeginRound(1);
            yield return null;
            Assert.IsTrue(SpawnWorldProfile.IsV6, "varsayılan dünya V6 değil");
            _rig.Health.DiagnosisInvulnerable = true;
        }

        /// <summary>
        /// The acceptance test: no environment input (WP-076 B9 M-22 — the C1 observation lives in its own Explicit Diagnostics method).
        /// </summary>
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator V6_OdalarKopruSelale_TakilmaSifir()
        {
            yield return Kur();
            var stations = new List<(string name, Vector3 at)>(Stations);
            GameObject falls = GameObject.Find("Waterfall");
            Assert.IsNotNull(falls, "strict Waterfall istasyonu yok");
            stations.Add(("Waterfall", falls.transform.position));

            var r = new RoundResult();
            yield return Istasyonlar(stations, null, r);

            StringBuilder rapor = r.Rapor;
            Assert.AreEqual(0, r.SumRem, "takılma: watchdog gövde kaldırdı\n" + rapor);
            Assert.AreEqual(0, r.SumOow, "dünya dışı kaldırma\n" + rapor);
            // stuck RECOVERIES (repath + side step, the body stays) are reported, not asserted: 26.09 two runs 0 and 3 outside KnownOpen
            // (R3 / R4 / C8 once each, bodies far from the station — K1 kairō (−30.9, −5.8), north of the chūmon (3.0, 23.5)).
            rapor.AppendLine("  stuckRecover outside known = " + r.SumRec);
            Assert.IsEmpty(r.NoReach, "oyuncuya ulaşılamayan istasyon\n" + rapor);
            Assert.AreEqual(0, r.WaterfallStalls, "strict Waterfall: gözlenen takılma\n" + rapor);
            Assert.IsTrue(r.NearCornerReached, "Waterfall yakın-köşe gövdesi oyuncuya ulaşmadı\n" + rapor);
        }

#if UNITY_EDITOR
        /// <summary>
        /// WP-076 B9 (M-22 / Y6, C-00 30.09): the C1 contact observation of the unchanged KnownOpen station — a diagnostic, never an
        /// acceptance gate. Explicit + Diagnostics (not in full runs); output: DD_WP076_C1_CONTACT_OUT or the temporary cache.
        /// </summary>
        [UnityTest]
        [Explicit("Diagnostics: C1 contact observation (WP-076 B9 M-22)")]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator C1_TemasGozlemi_TakilanGovde()
        {
            string output = Environment.GetEnvironmentVariable("DD_WP076_C1_CONTACT_OUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.temporaryCachePath, "wp076-c1-contact.json");
            yield return TemasGozlemi(6, output, false);
        }

        /// <summary>
        /// WP-076 B9 M-32 (C-00 30.09, with S-05): the same contact observation at R4_TeaHouse, whose north door step (0.45 m plinth vs
        /// controller stepOffset 0.35 / NavMesh climb 0.30) is the suspected stall; the spawn-seed sequence is fixed (Y4 hook) so repeated
        /// runs compare. Explicit + Diagnostics; output: DD_WP076_R4_CONTACT_OUT or the temporary cache.
        /// </summary>
        [UnityTest]
        [Explicit("Diagnostics: R4 tea-house contact observation (WP-076 B9 M-32)")]
        [Category("Diagnostics")]
        [Timeout(600000)]
        public IEnumerator R4_TemasGozlemi_TakilanGovde()
        {
            string output = Environment.GetEnvironmentVariable("DD_WP076_R4_CONTACT_OUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.temporaryCachePath, "wp076-r4-contact.json");
            yield return TemasGozlemi(3, output, true);
        }

        private IEnumerator TemasGozlemi(int stationIndex, string output, bool fixedSeed)
        {
            yield return Kur();
            if (fixedSeed) EnemyFactory.ResetSeedForTests(1u);
            var proof = new C1ContactObserver.Proof();
            var r = new RoundResult();
            yield return Istasyonlar(new List<(string name, Vector3 at)> { Stations[stationIndex] }, proof, r);
            C1ContactObserver.ResetHook();
            proof.outcome = proof.move == null ? "UNKNOWN: no stalled-body normal Move captured"
                : proof.contacts.Count == 0 ? "UNKNOWN: selected Move had no actual collider contact"
                : "CONTACT OBSERVED: collider binding and causal interpretation require review";
            proof.sceneInputs = C1ContactObserver.CurrentScenes();
            File.WriteAllText(output, JsonUtility.ToJson(proof, true));
            Debug.Log("[DD-OBS] wp076.contact " + Stations[stationIndex].name + " " + proof.outcome + " → " + output);
            Debug.Log(r.Rapor.ToString());
        }
#endif

        /// <param name="c1Diag">a C1ContactObserver.Proof for the C1 diagnostic, null for the acceptance test</param>
        private IEnumerator Istasyonlar(List<(string name, Vector3 at)> stations, object c1Diag, RoundResult r)
        {
            RunDirector run = _run;
            EnemySpawnDirector director = _director;
            PlayerRig rig = _rig;
#if UNITY_EDITOR
            var c1 = c1Diag as C1ContactObserver.Proof;
#endif
            StringBuilder rapor = r.Rapor;
            List<string> noReach = r.NoReach;
            var oowAt = new List<string>();
            Action<DD.Prototype.Core.Config.EnemyTier, Vector3, ZombieEnemy.OutOfWorldReason, ZombieBodyState> onOow =
                (t, p, why, s) => oowAt.Add(why + "@(" + p.x.ToString("0.0") + "," + p.y.ToString("0.0") + "," + p.z.ToString("0.0") + ") " + t + " " + s);
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

#if UNITY_EDITOR
                    if (c1 != null) { c1.stationNavPosition = hit.position; c1.stationNavResolved = true; }
#endif
                    director.ForceActiveTarget(0, false);
                    director.ClearAllEnemies();
                    rig.Teleport(hit.position + Vector3.up * 0.05f, Quaternion.identity);
                    yield return null;
                    yield return null;
                    director.RefreshPointsNow();

                    int rec = ZombieEnemy.StuckRecoveries, rem = ZombieEnemy.StuckRemovals, oow = ZombieEnemy.OutOfWorldRemovals;
                    int cap0 = director.InnerQuotaRelaxes, noPoint0 = director.NoPointTicks, massN0 = director.InnerMassSamples;   // WP-076 B9 Y5 / M-26
                    double mass0 = director.InnerMassSum;
                    int holds0 = NavPathFollower.CornerHoldsStarted, cornerTo0 = NavPathFollower.CornerHoldTimeouts;   // WP-076 B9 M-23
                    var anchors = new Dictionary<ZombieEnemy, (Vector3 anchor, float since, bool reached, bool reported)>();
                    int spawned = 0, reached = 0;
                    Action<EnemySpawnContext> onSpawn = c => spawned++;
                    var stalls = new StringBuilder();
                    int stallCount = 0;
                    ZombieEnemy nearCornerBody = null;
                    if (st.name == "Waterfall")
                    {
                        // WP-076 B9 M-24 (C-00 30.09): the near-corner start is DERIVED from the current NavMesh (was S-05's fixed point
                        // (2.8, 0.3, −19) on geometry 37badf64), and its precondition is asserted, so a geometry change cannot quietly
                        // empty the strict Waterfall case. Exercise the actual CharacterController and crowd together; no sweep.
                        Assert.IsTrue(TryNecessaryCornerStart(hit.position, out Vector3 ncStart, out Vector3 ncCorner, out Vector3 ncNext),
                            "ön koşul: C8 kuzeyinden istasyona giden yolda 'gerekli' köşe bulunamadı (geometri değişti mi?)");
                        Vector3 ncFlat = ncCorner - ncStart; ncFlat.y = 0f;
                        Assert.LessOrEqual(ncFlat.magnitude, 0.65f, "ön koşul: başlangıç köşenin 0,65 m varış yarıçapı içinde");
                        Assert.IsTrue(NavMesh.Raycast(ncStart, ncNext, out NavMeshHit _, CrowdService.Paths.AreaMask),
                            "ön koşul: başlangıçtan sonraki köşeye NavMesh çizgisi kapalı (köşe gerekli)");
                        rapor.AppendLine("  waterfall nearCorner derived start=" + ncStart.ToString("F2") + " corner=" + ncCorner.ToString("F2") + " next=" + ncNext.ToString("F2"));
                        nearCornerBody = EnemyFactory.Spawn(DD.Prototype.Core.Config.EnemyTier.Standard, 1, ncStart, Quaternion.identity);
                        Assert.IsNotNull(nearCornerBody, "Waterfall yakın-köşe gövdesi doğmadı");
                        CharacterController controller = nearCornerBody.GetComponent<CharacterController>();
                        Assert.IsNotNull(controller);
                        Assert.IsTrue(controller.enabled);
                        Assert.IsNotNull(nearCornerBody.PathFollower);
                        Assert.Greater(CrowdService.Solver.MemberCount, 0, "gerçek crowd etkin olmalı");
                        Assert.AreEqual(0.65f, nearCornerBody.PathFollower.CornerArriveRadius, "normal varış yarıçapı korunmalı");
                        rapor.AppendLine("  waterfall nearCorner start=" + nearCornerBody.transform.position + " controllerRadius=" + controller.radius);
                    }
                    director.EnemySpawned += onSpawn;
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
                                        stallCount++;
#if UNITY_EDITOR
                                        if (c1 != null && !c1.selected)
                                            C1ContactObserver.Select(z, c1, a.anchor, a.since, d);
#endif
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
                    rapor.AppendLine(string.Format("  {0,-12} at=({1:0.0},{2:0.0},{3:0.0}) spawned={4} reached={5} stuckRecover={6} stuckRemove={7} oow={8} innerQuotaRelax={10} noPointTicks={11} innerShare24={12:0.00} innerMass={13} cornerHold={14} cornerTimeout={15} stalls: {9}",
                        st.name, hit.position.x, hit.position.y, hit.position.z, spawned, reached, dRec, dRem, dOow, stalls.Length > 0 ? stalls.ToString() : "-",
                        director.InnerQuotaRelaxes - cap0, director.NoPointTicks - noPoint0, SpawnWorldProfile.InnerShare,
                        director.InnerMassSamples > massN0 ? ((director.InnerMassSum - mass0) / (director.InnerMassSamples - massN0)).ToString("0.00") : "-",
                        NavPathFollower.CornerHoldsStarted - holds0, NavPathFollower.CornerHoldTimeouts - cornerTo0));
                    if (st.name == "Waterfall")
                    {
                        r.WaterfallStalls = stallCount;
                        r.NearCornerReached = anchors.TryGetValue(nearCornerBody, out var nearCornerAnchor) && nearCornerAnchor.reached;
                        rapor.AppendLine("  strict Waterfall stalls=" + r.WaterfallStalls + " nearCornerReached=" + r.NearCornerReached);
                    }
#if UNITY_EDITOR
                    if (c1 != null)
                    {
                        c1.spawned = spawned; c1.reached = reached; c1.stalls = stallCount;
                        c1.recoveries = dRec; c1.removals = dRem; c1.outOfWorld = dOow;
                    }
#endif
                    // WP-076 B9 (S-05 30.09, C1): every body that never reached the player — final (x, z), speed, remaining corners / path
                    {
                        var miss = new StringBuilder();
                        int missCount = 0;
                        foreach (var kv in anchors)
                        {
                            ZombieEnemy z = kv.Key;
                            if (kv.Value.reached || z == null || !z.IsAlive) continue;
                            Vector3 zp = z.transform.position;
                            CharacterController cc = z.GetComponent<CharacterController>();
                            float left = 0f;
                            if (NavMesh.SamplePosition(zp, out NavMeshHit zh, 1f, NavMesh.AllAreas))
                            {
                                var lp = new NavMeshPath();
                                if (NavMesh.CalculatePath(zh.position, hit.position, NavMesh.AllAreas, lp))
                                    for (int ci = 1; ci < lp.corners.Length; ci++) left += Vector3.Distance(lp.corners[ci - 1], lp.corners[ci]);
                            }
                            if (missCount++ < 12)
                            {
                                // S-05 30.09 (C1): is a stopped body a chokepoint queue / crowd wait or a physical stop? queue = the follower's
                                // chokepoint volume (e.g. C1_SteppingCauseway capacity 1), wait = the WP-050 idle_queue state
                                NavPathFollower f = z.PathFollower;
                                string queue = f != null && f.IsQueued && f.QueueVolume != null ? f.QueueVolume.id + ":" + f.QueuedSeconds.ToString("0.0") + "s" : "-";
                                miss.Append(string.Format("({0:0.0},{1:0.0} v={2:0.00} corners={3} path={4:0.0}m queue={5} wait={6}) ", zp.x, zp.z,
                                    cc != null ? new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude : -1f,
                                    f != null ? f.RemainingCorners : -1, left, queue, z.IsWaiting ? 1 : 0));
                            }
                        }
                        if (missCount > 0) rapor.AppendLine("    ulaşamayan " + missCount + ": " + miss);
                    }
                    bool known = KnownOpen.Contains(st.name);
                    if (known) rapor.AppendLine("    ^ KNOWN OPEN (not asserted)");
                    else { r.SumRec += dRec; r.SumRem += dRem; r.SumOow += dOow; if (reached == 0) noReach.Add(st.name); }
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

        }

#if UNITY_EDITOR
        /// <summary>Observer plumbing proof; not a C1 cause test. WP-076 B9 (Y6): Explicit + Diagnostics instead of an env-gated Ignore.</summary>
        [UnityTest]
        [Explicit("Diagnostics: C1 observer hook smoke (WP-076 B9 Y6)")]
        [Category("Diagnostics")]
        [Timeout(120000)]
        public IEnumerator C1_Observer_ActualMoveContact_HookSmoke()
        {
            string output = Environment.GetEnvironmentVariable("DD_WP076_C1_HOOK_OUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(Application.temporaryCachePath, "wp076-c1-hook.json");

            Assert.AreEqual(TestProfileIsolation.TestProfilePath, DD.Prototype.Profile.ProfileStore.Path);
            Assert.AreNotEqual(TestProfileIsolation.RealProfilePath, DD.Prototype.Profile.ProfileStore.Path);
            Assert.AreEqual(TestProfileIsolation.TestTelemetryDirectory, DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Directory);
            Assert.AreEqual(DD.Prototype.UI.UiDataPaths.ProcessLocalRoot, DD.Prototype.UI.UiDataPaths.Root);
            Assert.AreEqual(Path.Combine(DD.Prototype.UI.UiDataPaths.ProcessLocalRoot, DD.Prototype.UI.UiSettings.FileName), DD.Prototype.UI.UiSettings.FilePath);
            Assert.AreEqual(Path.Combine(DD.Prototype.UI.UiDataPaths.ProcessLocalRoot, DD.Prototype.UI.UiLoadTiming.FileName), DD.Prototype.UI.UiLoadTiming.FilePath);
            Assert.AreEqual(Path.Combine(DD.Prototype.UI.UiDataPaths.ProcessLocalRoot, DD.Prototype.UI.UiHintState.FileName), DD.Prototype.UI.UiHintState.FilePath);

            var proof = new C1ContactObserver.Proof { hookSmoke = true, target = 1, stationSeconds = 0f, stallSeconds = 0f,
                selection = "HOOK SMOKE: one factory body at a current authored gate, not a stalled C1 body",
                outcome = "UNKNOWN: hook smoke not completed" };
            EnemySpawnDirector director = null;
            PlayerRig rig = null;
            ZombieEnemy body = null;
            bool directorWasEnabled = false;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                RunFlowClock.ClearHolds();
                SceneManager.LoadScene(DD.Prototype.Core.Boot.PrototypeScenes.Run, LoadSceneMode.Single);
                yield return null;
                yield return Bekle(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && PlayerRegistry.Primary != null
                    && RunDirector.Instance.Phase == RunPhase.Fighting, 40f, "hook smoke Run/Fighting");
                director = EnemySpawnDirector.Instance; rig = PlayerRegistry.Primary;
                directorWasEnabled = director.enabled;
                RunDirector.Instance.BeginRound(1);
                // ForceActiveTarget(0) restores normal demand; disable only this test's director to isolate one factory body.
                director.ForceActiveTarget(0, false);
                director.enabled = false;
                director.ClearAllEnemies();
                rig.Health.DiagnosisInvulnerable = true;
                Assert.IsTrue(SpawnWorldProfile.IsV6);
                Assert.IsTrue(NavMesh.SamplePosition(Stations[6].at + Vector3.up * 0.5f, out NavMeshHit station, 3f, NavMesh.AllAreas));
                proof.stationNavResolved = true; proof.stationNavPosition = station.position;
                rig.Teleport(station.position + Vector3.up * 0.05f, Quaternion.identity);
                yield return null;
                yield return null;
                director.RefreshPointsNow();
                SpawnPointRuntime chosen = null;
                float nearest = float.PositiveInfinity;
                foreach (SpawnPointRuntime point in SpawnPointRegistry.Points)
                {
                    if (!point.Enabled || !point.HasMarker || point.PathStatus != NavPathStatus.Complete) continue;
                    Vector3 delta = point.NavPosition - rig.transform.position; delta.y = 0f;
                    float distance = delta.magnitude;
                    if (distance <= 15f) continue; // fixture-only separation from attacks, not a spawn rule
                    if (distance < nearest || (distance == nearest && string.CompareOrdinal(point.Id, chosen.Id) < 0))
                    { chosen = point; nearest = distance; }
                }
                Assert.IsNotNull(chosen, "No enabled marker-bound complete-path gate for hook smoke");
                proof.spawnPointId = chosen.Id; proof.spawnPosition = chosen.NavPosition;
                body = EnemyFactory.Spawn(DD.Prototype.Core.Config.EnemyTier.Standard, 1, chosen.NavPosition, chosen.SpawnRotation());
                Assert.IsNotNull(body);
                proof.spawned = 1;
                CharacterController controller = body.GetComponent<CharacterController>();
                Assert.IsNotNull(controller); Assert.IsTrue(controller.enabled);
                yield return Bekle(() => body != null && body.IsAlive && !body.IsRising && controller.isGrounded
                    && body.PathFollower != null && body.PathFollower.HasCompletePath, 8f, "body grounded with current complete path");
                Assert.AreEqual(1, ZombieEnemy.Live.Count, "Hook fixture must contain only its one factory body");
                C1ContactObserver.Select(body, proof, body.transform.position, Time.time, nearest);
                yield return Bekle(() => proof.hookPhases.Count == 3, 3f, "selected body actual normal Move phases");

                CollectionAssert.AreEqual(new[] { 0, 1, 2 }, proof.hookPhases);
                Assert.IsNotNull(proof.move);
                ZombieEnemy.DiagnosticMove move = proof.move.observation;
                Assert.Greater(move.callId, 0); Assert.Greater(move.dt, 0f);
                Assert.AreEqual(body.LifeId, move.lifeId);
                Assert.AreEqual(move.frame, proof.move.afterMoveFrame); Assert.AreEqual(move.frame, proof.move.afterClampFrame);
                foreach (int frame in proof.hookFrames) Assert.AreEqual(move.frame, frame);
                Assert.AreEqual(move.requestedDelta, move.finalVelocity * move.dt);
                Assert.Less(move.requestedDelta.y, 0f);
                Assert.IsTrue(move.groundedBefore); Assert.IsTrue(move.groundedAfterMove);
                Assert.IsTrue((move.flags & CollisionFlags.Below) != 0);
                Assert.IsTrue(proof.move.path.privateFieldsResolved); Assert.IsTrue(proof.move.path.complete);
                Assert.AreEqual(body.GetInstanceID(), proof.move.enemyInstanceId);
                Assert.AreEqual(controller.GetInstanceID(), proof.move.controllerInstanceId);
                Assert.IsTrue(proof.sameCallContacts);
                Assert.Greater(proof.contacts.Count, 0, "No real OnControllerColliderHit callback in selected Move");
                bool upwardWorldContact = false;
                foreach (C1ContactObserver.Contact contact in proof.contacts)
                {
                    Assert.AreEqual(move.callId, contact.callId); Assert.AreEqual(move.frame, contact.frame);
                    Assert.AreEqual(move.time, contact.time);
                    if (contact.normal.y > 0f && contact.runtimeCollider != null && contact.collider.enabled && !contact.collider.trigger
                        && contact.collider.layer == DD.Prototype.Core.PrototypeLayers.Environment
                        && contact.runtimeCollider.GetComponentInParent<ZombieEnemy>() == null) upwardWorldContact = true;
                }
                Assert.IsTrue(upwardWorldContact, "No actual upward world-ground hit callback");
                proof.outcome = "HOOK PROOF PASS / C1 CAUSE UNKNOWN: contact from a non-stalled diagnostic body";
            }
            finally
            {
                C1ContactObserver.ResetHook();
                if (body != null) body.Despawn();
                if (director != null) { director.ClearAllEnemies(); director.ForceActiveTarget(0, false); director.enabled = directorWasEnabled; }
                if (rig != null && rig.Health != null) rig.Health.DiagnosisInvulnerable = false;
                proof.sceneInputs = C1ContactObserver.CurrentScenes();
                File.WriteAllText(output, JsonUtility.ToJson(proof, true));
                Debug.Log("[DD-OBS] wp076.c1-hook " + proof.outcome);
            }
        }
#endif

        /// <summary>
        /// WP-076 B9 M-24: a start 0.5 m before a NECESSARY corner (the NavMesh line from there to the next corner is blocked, the
        /// NavPathFollower hold case) on a path from the C8 bridge's north landing (candidates on a 1 m grid, x 2.8 ± 3, z −19…−13) to
        /// <paramref name="goal"/>. The first hit in grid order is used; false when the current NavMesh has none.
        /// </summary>
        private static bool TryNecessaryCornerStart(Vector3 goal, out Vector3 start, out Vector3 corner, out Vector3 next)
        {
            start = corner = next = Vector3.zero;
            int mask = CrowdService.Paths.AreaMask;
            var path = new NavMeshPath();
            for (float dz = 0f; dz <= 6f; dz += 1f)
            for (float dx = -3f; dx <= 3f; dx += 1f)
            {
                if (!NavMesh.SamplePosition(new Vector3(2.8f + dx, 0.5f, -19f + dz), out NavMeshHit from, 1f, mask)) continue;
                if (!NavMesh.CalculatePath(from.position, goal, mask, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] c = path.corners;
                for (int i = 1; i + 1 < c.Length; i++)
                {
                    Vector3 approach = c[i] - c[i - 1]; approach.y = 0f;
                    if (approach.magnitude < 0.6f) continue;
                    Vector3 before = c[i] - approach.normalized * 0.5f;
                    if (!NavMesh.SamplePosition(before, out NavMeshHit b, 0.3f, mask)) continue;
                    Vector3 flat = c[i] - b.position; flat.y = 0f;
                    if (flat.magnitude > 0.65f) continue;
                    if (!NavMesh.Raycast(b.position, c[i + 1], out NavMeshHit _, mask)) continue;   // skippable: not necessary
                    start = b.position; corner = c[i]; next = c[i + 1];
                    return true;
                }
            }
            return false;
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

#if UNITY_EDITOR
    // Added only to the first actually stalled C1 body by the opt-in fixture.
    public sealed class C1ContactObserver : MonoBehaviour
    {
        [Serializable] public sealed class Source
        {
            public string path, guid, sha256, globalId, status;
            public long fileId;
        }
        [Serializable] public sealed class TransformState
        {
            public string name, hierarchy;
            public int instanceId;
            public Vector3 localPosition, localScale, worldPosition, worldScale;
            public Quaternion localRotation, worldRotation;
            public float[] localToWorld;
        }
        [Serializable] public sealed class ColliderState
        {
            public int instanceId, layer;
            public string type, hierarchy, scenePath;
            public bool enabled, trigger;
            public Vector3 boundsCenter, boundsSize, boxCenter, boxSize;
            public Vector3[] boxWorldCorners;
            public float boxWorldTop;
            public TransformState[] transforms;
            public Source sceneObject, source, originalSource, mesh;
        }
        [Serializable] public sealed class Contact
        {
            public long callId;
            public int frame;
            public float time, moveLength;
            public Vector3 point, normal, hitMoveDirection;
            public ColliderState collider;
            [NonSerialized] public Collider runtimeCollider;
        }
        [Serializable] public sealed class PathState
        {
            public bool privateFieldsResolved, complete;
            public string status;
            public int cornerIndex, remaining;
            public float arriveRadius;
            public Vector3[] corners;
            public Vector3 lastGoal;
        }
        [Serializable] public sealed class Move
        {
            public ZombieEnemy.DiagnosticMove observation;
            public int enemyInstanceId, controllerInstanceId, afterMoveFrame, afterClampFrame;
            public float afterMoveTime, afterClampTime, radius, height, skinWidth, stepOffset, slopeLimit, minMoveDistance;
            public Vector3 controllerCenter, achievedMoveDelta, achievedAfterClampDelta;
            public bool detectCollisions;
            public string tier, unityVersion;
            public TransformState[] enemyTransforms;
            public PathState path;
            public Source enemyScript, enemyComponent, enemyPrefabSource, controllerPrefabSource;
        }
        [Serializable] public sealed class Proof
        {
            public string outcome, selection = "first body satisfying unchanged fixture stall predicate; next normal walking Move only";
            public Vector3 station = new Vector3(-8f, 0.25f, -33f), stationNavPosition, stallAnchor, selectionPosition;
            public float stationSeconds = 25f, stallSeconds = 6f, stallMove = 0.75f, anchorSince, selectedTime, selectedDistance;
            public int target = 12, spawned, reached, stalls, recoveries, removals, outOfWorld, selectedFrame, selectedLifeId;
            public bool selected, sameCallContacts, stationNavResolved, hookSmoke;
            public string spawnPointId;
            public Vector3 spawnPosition;
            public List<int> hookPhases = new List<int>(), hookFrames = new List<int>();
            public Move move;
            public List<Contact> contacts = new List<Contact>();
            public List<ColliderState> nearbyContextOnly = new List<ColliderState>();
            public Source[] sceneInputs;
        }

        private Proof _proof;
        private ZombieEnemy _body;
        private bool _insideMove, _captured;
        private long _callId;

        public static void Select(ZombieEnemy body, Proof proof, Vector3 anchor, float since, float distance)
        {
            proof.selected = true; proof.selectedFrame = Time.frameCount; proof.selectedTime = Time.time;
            proof.selectedLifeId = body.LifeId; proof.selectionPosition = body.transform.position;
            proof.stallAnchor = anchor; proof.anchorSince = since; proof.selectedDistance = distance;
            var observer = body.gameObject.AddComponent<C1ContactObserver>();
            observer._body = body; observer._proof = proof;
            ZombieEnemy.DiagnosticMoveBody = body;
            ZombieEnemy.DiagnosticMoveObserved = observer.Observe;
        }

        public static void ResetHook()
        {
            ZombieEnemy.DiagnosticMoveBody = null;
            ZombieEnemy.DiagnosticMoveObserved = null;
            foreach (C1ContactObserver observer in UnityEngine.Object.FindObjectsOfType<C1ContactObserver>())
                Destroy(observer);
        }

        private void Observe(ZombieEnemy.DiagnosticMove value)
        {
            if (_captured || _body.LifeId != _proof.selectedLifeId) return;
            _proof.hookPhases.Add(value.phase); _proof.hookFrames.Add(Time.frameCount);
            if (value.phase == 0)
            {
                _insideMove = true; _callId = value.callId;
                CharacterController cc = _body.GetComponent<CharacterController>();
                _proof.move = new Move
                {
                    observation = value, enemyInstanceId = _body.GetInstanceID(), controllerInstanceId = cc.GetInstanceID(),
                    tier = _body.Tier.ToString(), unityVersion = Application.unityVersion,
                    radius = cc.radius, height = cc.height, skinWidth = cc.skinWidth, stepOffset = cc.stepOffset,
                    slopeLimit = cc.slopeLimit, minMoveDistance = cc.minMoveDistance, controllerCenter = cc.center,
                    detectCollisions = cc.detectCollisions, enemyTransforms = Hierarchy(_body.transform),
                    path = ReadPath(_body.PathFollower), enemyScript = AssetSource(MonoScript.FromMonoBehaviour(_body)),
                    enemyComponent = AssetSource(_body), enemyPrefabSource = AssetSource(PrefabUtility.GetCorrespondingObjectFromSource(_body)),
                    controllerPrefabSource = AssetSource(PrefabUtility.GetCorrespondingObjectFromSource(cc))
                };
            }
            else if (value.callId == _callId && value.phase == 1)
            {
                _insideMove = false;
                _proof.move.afterMoveFrame = Time.frameCount; _proof.move.afterMoveTime = Time.time;
            }
            else if (value.callId == _callId && value.phase == 2)
            {
                _proof.move.observation = value;
                _proof.move.afterClampFrame = Time.frameCount; _proof.move.afterClampTime = Time.time;
                _proof.move.achievedMoveDelta = value.afterMove - value.before;
                _proof.move.achievedAfterClampDelta = value.afterClamp - value.before;
                _proof.sameCallContacts = true;
                var actual = new HashSet<int>();
                foreach (Contact contact in _proof.contacts)
                {
                    _proof.sameCallContacts &= contact.callId == value.callId && contact.frame == value.frame;
                    ResolveSources(contact.collider, contact.runtimeCollider);
                    actual.Add(contact.collider.instanceId);
                }
                // Context only: proximity never substitutes for an actual hit callback.
                if (!_proof.hookSmoke) foreach (Collider collider in UnityEngine.Object.FindObjectsOfType<Collider>())
                {
                    if (!collider.enabled || collider.GetComponentInParent<ZombieEnemy>() != null || actual.Contains(collider.GetInstanceID())) continue;
                    foreach (Contact contact in _proof.contacts)
                        if ((collider.bounds.ClosestPoint(contact.point) - contact.point).sqrMagnitude <= 4f)
                        {
                            ColliderState state = Snapshot(collider); ResolveSources(state, collider);
                            _proof.nearbyContextOnly.Add(state); break;
                        }
                }
                _captured = true;
                ZombieEnemy.DiagnosticMoveBody = null;
                ZombieEnemy.DiagnosticMoveObserved = null;
                enabled = false;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!_insideMove || _captured || hit.controller.gameObject != gameObject) return;
            _proof.contacts.Add(new Contact
            {
                callId = _callId, frame = Time.frameCount, time = Time.time, point = hit.point,
                normal = hit.normal, hitMoveDirection = hit.moveDirection, moveLength = hit.moveLength,
                collider = Snapshot(hit.collider), runtimeCollider = hit.collider
            });
        }

        private static PathState ReadPath(NavPathFollower follower)
        {
            if (follower == null) return new PathState { status = "UNKNOWN: no follower" };
            var state = new PathState { status = follower.Status.ToString(), complete = follower.HasCompletePath,
                remaining = follower.RemainingCorners, arriveRadius = follower.CornerArriveRadius };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(NavPathFollower);
            FieldInfo corners = type.GetField("_corners", flags), count = type.GetField("_cornerCount", flags),
                index = type.GetField("_cornerIndex", flags), goal = type.GetField("_lastGoal", flags);
            if (corners == null || count == null || index == null || goal == null) return state;
            int n = (int)count.GetValue(follower);
            state.corners = new Vector3[n];
            Array.Copy((Vector3[])corners.GetValue(follower), state.corners, n);
            state.cornerIndex = (int)index.GetValue(follower); state.lastGoal = (Vector3)goal.GetValue(follower);
            state.privateFieldsResolved = true;
            return state;
        }

        private static string PathOf(Transform transform)
        {
            string result = transform.name + "[" + transform.GetSiblingIndex() + "]";
            for (Transform p = transform.parent; p != null; p = p.parent)
                result = p.name + "[" + p.GetSiblingIndex() + "]/" + result;
            return result;
        }

        private static TransformState[] Hierarchy(Transform transform)
        {
            var result = new List<TransformState>();
            for (Transform p = transform; p != null; p = p.parent)
            {
                var matrix = new float[16];
                for (int i = 0; i < 16; i++) matrix[i] = p.localToWorldMatrix[i];
                result.Add(new TransformState { name = p.name, hierarchy = PathOf(p), instanceId = p.GetInstanceID(),
                    localPosition = p.localPosition, localRotation = p.localRotation, localScale = p.localScale,
                    worldPosition = p.position, worldRotation = p.rotation, worldScale = p.lossyScale, localToWorld = matrix });
            }
            return result.ToArray();
        }

        private static ColliderState Snapshot(Collider collider)
        {
            var state = new ColliderState { instanceId = collider.GetInstanceID(), type = collider.GetType().FullName,
                layer = collider.gameObject.layer, hierarchy = PathOf(collider.transform), scenePath = collider.gameObject.scene.path,
                enabled = collider.enabled, trigger = collider.isTrigger, boundsCenter = collider.bounds.center,
                boundsSize = collider.bounds.size, transforms = Hierarchy(collider.transform) };
            if (collider is BoxCollider box)
            {
                state.boxCenter = box.center; state.boxSize = box.size; state.boxWorldCorners = new Vector3[8];
                state.boxWorldTop = float.NegativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    Vector3 corner = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, sign));
                    state.boxWorldCorners[i] = corner; state.boxWorldTop = Mathf.Max(state.boxWorldTop, corner.y);
                }
            }
            return state;
        }

        private static void ResolveSources(ColliderState state, Collider collider)
        {
            state.sceneObject = AssetSource(collider);
            state.source = AssetSource(PrefabUtility.GetCorrespondingObjectFromSource(collider));
            state.originalSource = AssetSource(PrefabUtility.GetCorrespondingObjectFromOriginalSource(collider));
            if (collider is MeshCollider meshCollider) state.mesh = AssetSource(meshCollider.sharedMesh);
        }

        private static Source AssetSource(UnityEngine.Object source)
        {
            if (source == null) return new Source { status = "UNKNOWN: no corresponding source" };
            string path = AssetDatabase.GetAssetPath(source);
            bool resolved = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long fileId);
            return new Source { path = path, guid = guid, fileId = fileId,
                globalId = GlobalObjectId.GetGlobalObjectIdSlow(source).ToString(), sha256 = HashFile(path),
                status = resolved && !string.IsNullOrEmpty(path) ? "asset GUID/fileID resolved" : "runtime/scene identity; prefab source not established" };
        }

        private static string HashFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        public static Source[] CurrentScenes()
        {
            var result = new List<Source>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                result.Add(new Source { path = scene.path, guid = AssetDatabase.AssetPathToGUID(scene.path),
                    sha256 = HashFile(scene.path), status = scene.isLoaded ? "loaded scene" : "not loaded" });
            }
            return result.ToArray();
        }
    }
#endif
}
