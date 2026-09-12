#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Navigation;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-003 · test-only round/spawn probe for the real Player.
    ///
    /// Drives one real run through the public flow API and records what the manual
    /// checklist asks for, in the Player rather than the Editor: round 1 start and the
    /// centre start, continuous arrival, spawn positions against the authored points,
    /// the target and the ceiling under forced load, death → refill, pause freeze, pool
    /// exhaustion and the single completion on the last living enemy, spawning stopping
    /// on player death with no late completion, and a second run opening clean with one
    /// director. Nothing here changes product behaviour: it only calls the same public
    /// methods the screens and the F1 panel call and reads public state.
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddRoundProbe</c> a development Player
    /// allocates nothing either.
    ///
    /// Command line:
    ///   -ddRoundProbe &lt;outDir&gt;     enable; write stage JSON files into outDir, then quit
    ///   -ddProfilePath &lt;file&gt;      redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class RoundProbe
    {
        private const string OutFlag = "-ddRoundProbe";
        private const string ProfileFlag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(Arg(OutFlag)))
            {
                return;
            }

            string profilePath = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[RoundProbe] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            var carrier = new GameObject("RoundProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [Serializable]
        public sealed class Stage
        {
            public string stage;
            public string utc;
            public string scene;
            public bool pass;
            public string note;

            public int runRound;
            public string runPhase;
            public string roundState;
            public int poolTotal;
            public int unspawned;
            public int spawnedTotal;
            public int alive;
            public int occupied;
            public int killed;
            public int targetActive;
            public int ceiling;
            public int activationFailures;
            public int reconciled;
            public int liveBodies;
            public int directorCount;
            public int runDirectorCount;
            public int markerBoundPoints;
            public int registeredPoints;

            public float playerX;
            public float playerZ;
            public string playerRegion;
            public float playerHealth;

            public float fillSeconds;
            public int maxAlive;
            public int maxOccupied;
            public int maxLiveBodies;
            public int spawnEvents;
            public float maxSpawnOffsetFromPoint;
            public string pointsUsed;
            public int roundClearedEvents;
            public int poolExhaustedEvents;
            public int runRoundCompletedEvents;
            public float replenishSeconds;
            public int pauseFrames;
            public bool pauseCountersFrozen;
            public int corpsesAtCompletion;
            public string flow;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 30f;
            private string _outDir;
            private readonly List<Stage> _stages = new List<Stage>();

            private int _roundCleared;
            private int _poolExhausted;
            private int _runRoundCompleted;
            private int _spawnEvents;
            private float _maxSpawnOffset;
            private readonly HashSet<string> _pointsUsed = new HashSet<string>();
            private EnemySpawnDirector _bound;

            internal void Configure(string outDir)
            {
                _outDir = outDir;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);
                RunEvents.RoundCompleted += r => _runRoundCompleted++;

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;

                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");

                Bind(EnemySpawnDirector.Instance);
                PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = true;   // the probe player stands still; keep the measurements alive

                // ---------------------------------------------------------------- M1/M2/M17
                Stage s1 = Capture("01-round1-start");
                Vector3 pos = rig.transform.position;
                Vector3 d = pos - WorldLayout.PlayerStart; d.y = 0f;
                s1.pass = s1.runRound == 1 && s1.poolTotal > 0 && d.magnitude < 2.5f && s1.playerRegion == "Center" && s1.directorCount == 1;
                s1.note = "round 1, centre start (" + d.magnitude.ToString("F2") + " m from PlayerStart), one director";
                Write(s1);

                // ---------------------------------------------------------------- M3/M4/M7
                ResetCounters();
                float t0 = Time.realtimeSinceStartup;
                int maxAlive = 0, maxOcc = 0, maxLive = 0;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                while (Time.realtimeSinceStartup - t0 < 15f && dir.ActiveCount < dir.TargetActive)
                {
                    maxAlive = Mathf.Max(maxAlive, dir.ActiveCount);
                    maxOcc = Mathf.Max(maxOcc, dir.OccupiedCount);
                    maxLive = Mathf.Max(maxLive, ZombieEnemy.Live.Count);
                    yield return null;
                }

                Stage s2 = Capture("02-fill-to-target");
                s2.fillSeconds = Time.realtimeSinceStartup - t0;
                s2.maxAlive = Mathf.Max(maxAlive, dir.ActiveCount);
                s2.maxOccupied = Mathf.Max(maxOcc, dir.OccupiedCount);
                s2.maxLiveBodies = Mathf.Max(maxLive, ZombieEnemy.Live.Count);
                s2.spawnEvents = _spawnEvents;
                s2.maxSpawnOffsetFromPoint = _maxSpawnOffset;
                s2.pointsUsed = string.Join(",", _pointsUsed);
                s2.pass = dir.ActiveCount == dir.TargetActive && _maxSpawnOffset <= dir.SpawnScatterRadius + 2f && s2.maxOccupied <= s2.ceiling;
                s2.note = "continuous arrival to target; every spawn within scatter+projection of an authored point";
                Write(s2);

                // ---------------------------------------------------------------- M8
                dir.ForceActiveTarget(dir.SafetyCeiling, true);
                t0 = Time.realtimeSinceStartup;
                maxOcc = 0; maxLive = 0;
                while (Time.realtimeSinceStartup - t0 < 6f)
                {
                    maxOcc = Mathf.Max(maxOcc, dir.OccupiedCount);
                    maxLive = Mathf.Max(maxLive, ZombieEnemy.Live.Count);
                    yield return null;
                }

                dir.ForceActiveTarget(0, false);
                Stage s3 = Capture("03-ceiling-forced");
                s3.maxOccupied = maxOcc;
                s3.maxLiveBodies = maxLive;
                s3.pass = maxOcc <= s3.ceiling && maxLive <= s3.ceiling && maxOcc >= Mathf.RoundToInt(s3.ceiling * 0.75f);
                s3.note = "forced target = ceiling for 6 s; bodies never above ceiling";
                Write(s3);

                // ---------------------------------------------------------------- M9 (death → refill) fresh round
                RunDirector.Instance.BeginRound(1);
                yield return null;
                ResetCounters();
                yield return WaitUntil(() => dir.ActiveCount >= 3, "3 alive");
                int aliveBefore = dir.ActiveCount, occBefore = dir.OccupiedCount, liveBefore = ZombieEnemy.Live.Count, spawnedBefore = dir.SpawnedThisRound;
                ZombieEnemy victim = FirstLivingTicketed();
                victim.Kill();
                bool deathBook = dir.ActiveCount == aliveBefore - 1 && dir.OccupiedCount == occBefore && ZombieEnemy.Live.Count == liveBefore;
                t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => dir.SpawnedThisRound > spawnedBefore || Time.realtimeSinceStartup - t0 > 5f, "replenish");
                Stage s4 = Capture("04-death-refill");
                s4.replenishSeconds = Time.realtimeSinceStartup - t0;
                s4.pass = deathBook && dir.SpawnedThisRound > spawnedBefore;
                s4.note = "death drops alive at once (corpse still a body), pool refills within " + s4.replenishSeconds.ToString("F2") + " s";
                Write(s4);

                // ---------------------------------------------------------------- M11 pause
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                RoundPopulationSnapshot frozen = dir.Population;
                bool frozenOk = true;
                int pauseFrames = 0;
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 1.5f)
                {
                    RoundPopulationSnapshot p = dir.Population;
                    frozenOk &= p.SpawnedTotal == frozen.SpawnedTotal && p.Alive == frozen.Alive && p.Occupied == frozen.Occupied &&
                                p.Unspawned == frozen.Unspawned && Time.deltaTime <= 0f;
                    pauseFrames++;
                    yield return null;
                }

                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return null;
                Stage s5 = Capture("05-pause");
                s5.pauseFrames = pauseFrames;
                s5.pauseCountersFrozen = frozenOk;
                s5.pass = frozenOk && !RunFlowClock.IsFrozen && dir.Population.SpawnedTotal - frozen.SpawnedTotal <= 2;
                s5.note = "PauseHold 1.5 s: no spawn, counters frozen, no burst on resume";
                Write(s5);

                // ---------------------------------------------------------------- M9/M10 drain to completion
                ResetCounters();
                t0 = Time.realtimeSinceStartup;
                int spawnedAtExhaust = -1;
                bool noSpawnAfterExhaust = true;
                int corpses = 0;
                while (Time.realtimeSinceStartup - t0 < 90f && _roundCleared == 0)
                {
                    RoundPopulationSnapshot p = dir.Population;
                    if (p.Unspawned == 0)
                    {
                        if (spawnedAtExhaust < 0) spawnedAtExhaust = p.SpawnedTotal;
                        noSpawnAfterExhaust &= p.SpawnedTotal == spawnedAtExhaust;
                    }

                    KillAllLiving();
                    yield return null;
                }

                corpses = ZombieEnemy.Live.Count;
                for (int i = 0; i < 5; i++) yield return null;
                Stage s6 = Capture("06-drain-complete");
                s6.corpsesAtCompletion = corpses;
                s6.pass = _roundCleared == 1 && _poolExhausted == 1 && _runRoundCompleted == 1 && noSpawnAfterExhaust &&
                          s6.roundState == "Completed" && s6.runPhase == "RoundComplete" && corpses > 0;
                s6.note = "no spawn after pool exhausted; last living death completes the round once; corpses present at completion";
                Write(s6);

                // ---------------------------------------------------------------- M12/M13 player death, no late completion
                RunFlowClock.ClearHolds();
                RunDirector.Instance.CloseUpgradeScreen();
                yield return WaitUntil(() => RunDirector.Instance.Phase == RunPhase.Fighting && RunDirector.Instance.Round == 2, "round 2");
                yield return WaitUntil(() => dir.ActiveCount >= 2, "2 alive in round 2");
                ResetCounters();
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp-003-probe");
                yield return WaitUntil(() => RunDirector.Instance.Phase == RunPhase.Dead, "Dead");

                // The Run scene hands off to Results 1.6 s after death (PrototypeUiFlow), so the
                // window is kept inside that: 0.8 s of no-spawn, then late kills, then capture
                // through the retained director reference (the scene may already be gone).
                int spawnedAtDeath = dir.SpawnedThisRound;
                RunDirector deadRun = RunDirector.Instance;
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 0.8f) yield return null;
                bool spawnFrozen = dir.SpawnedThisRound == spawnedAtDeath;
                KillAllLiving();
                for (int i = 0; i < 5; i++) yield return null;
                Stage s7 = Capture("07-player-death", dir, deadRun);
                s7.pass = s7.roundState == "Aborted" && spawnFrozen && _roundCleared == 0 && _runRoundCompleted == 0 &&
                          dir.SpawnedThisRound == spawnedAtDeath;
                s7.note = "player death aborts the round: no spawn for 0.8 s, late kills produce no completion";
                Write(s7);

                // ---------------------------------------------------------------- M13/M17 second run
                yield return WaitForScene(PrototypeScenes.Results);
                yield return null;
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                             EnemySpawnDirector.Instance != dir && RunDirector.Instance.Phase == RunPhase.Fighting, "second run Fighting");
                Bind(EnemySpawnDirector.Instance);
                ResetCounters();
                for (int i = 0; i < 30; i++) yield return null;

                int stale = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null && (live[i].SpawnTicket <= 0 || live[i].IsDying)) stale++;
                }

                Stage s8 = Capture("08-second-run");
                s8.pass = s8.runRound == 1 && s8.killed == 0 && s8.directorCount == 1 && s8.runDirectorCount == 1 &&
                          s8.alive == s8.occupied && stale == 0 && _runRoundCompleted == 0;
                s8.note = "second run: round 1, zero kills, one director, no stale bodies, no leaked completion (stale=" + stale + ")";
                Write(s8);

                // summary
                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "RoundProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[RoundProbe] DONE stages=" + _stages.Count + " pass=" + passCount);

                yield return new WaitForSecondsRealtime(0.5f);
                PrototypeUiFlow.Quit();
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " | " + _stages[i].note + " | " + _stages[i].flow;
                }
            }

            private void Bind(EnemySpawnDirector director)
            {
                if (_bound != null)
                {
                    _bound.RoundCleared -= OnCleared;
                    _bound.RoundPoolExhausted -= OnExhausted;
                    _bound.EnemySpawned -= OnSpawned;
                }

                _bound = director;
                if (_bound == null)
                {
                    return;
                }

                _bound.RoundCleared += OnCleared;
                _bound.RoundPoolExhausted += OnExhausted;
                _bound.EnemySpawned += OnSpawned;
            }

            private void OnCleared(int r) => _roundCleared++;
            private void OnExhausted(int r) => _poolExhausted++;

            private void OnSpawned(EnemySpawnContext c)
            {
                _spawnEvents++;
                _pointsUsed.Add(c.PointId);
                IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
                if (c.PointIndex >= 0 && c.PointIndex < points.Count)
                {
                    Vector3 d = c.Position - points[c.PointIndex].NavPosition;
                    d.y = 0f;
                    _maxSpawnOffset = Mathf.Max(_maxSpawnOffset, d.magnitude);
                }
                else
                {
                    _maxSpawnOffset = float.MaxValue;   // unregistered point: fails the check
                }
            }

            private void ResetCounters()
            {
                _roundCleared = 0;
                _poolExhausted = 0;
                _runRoundCompleted = 0;
                _spawnEvents = 0;
                _maxSpawnOffset = 0f;
                _pointsUsed.Clear();
            }

            private static ZombieEnemy FirstLivingTicketed()
            {
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null && live[i].IsAlive && live[i].SpawnTicket > 0) return live[i];
                }

                return null;
            }

            private static void KillAllLiving()
            {
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (live[i] != null && live[i].IsAlive) live[i].Kill();
                }
            }

            private Stage Capture(string stage)
            {
                return Capture(stage, EnemySpawnDirector.Instance, RunDirector.Instance);
            }

            private Stage Capture(string stage, EnemySpawnDirector d, RunDirector r)
            {
                PlayerRig rig = PlayerRegistry.Primary;

                // Managed reference checks on purpose: a director whose scene has already
                // been unloaded still holds its ledger, and that is exactly what stage 07 reads.
                bool hasDirector = !ReferenceEquals(d, null);
                bool hasRun = !ReferenceEquals(r, null);
                RoundPopulationSnapshot p = hasDirector ? d.Population : default;
                Vector3 pos = rig != null ? rig.transform.position : Vector3.zero;

                var s = new Stage
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    runRound = hasRun ? r.Round : 0,
                    runPhase = hasRun ? r.Phase.ToString() : "none",
                    roundState = p.State.ToString(),
                    poolTotal = p.PoolTotal,
                    unspawned = p.Unspawned,
                    spawnedTotal = p.SpawnedTotal,
                    alive = p.Alive,
                    occupied = p.Occupied,
                    killed = p.Killed,
                    targetActive = p.TargetActive,
                    ceiling = p.Ceiling,
                    activationFailures = p.ActivationFailures,
                    reconciled = p.Reconciled,
                    liveBodies = ZombieEnemy.Live.Count,
                    directorCount = UnityEngine.Object.FindObjectsOfType<EnemySpawnDirector>().Length,
                    runDirectorCount = UnityEngine.Object.FindObjectsOfType<RunDirector>().Length,
                    markerBoundPoints = SpawnPointRegistry.MarkerBoundCount,
                    registeredPoints = SpawnPointRegistry.Count,
                    playerX = pos.x,
                    playerZ = pos.z,
                    playerRegion = RingRegionMap.Classify(pos).ToString(),
                    playerHealth = rig != null && rig.Health != null ? rig.Health.Current : 0f,
                    roundClearedEvents = _roundCleared,
                    poolExhaustedEvents = _poolExhausted,
                    runRoundCompletedEvents = _runRoundCompleted,
                    spawnEvents = _spawnEvents,
                    flow = hasDirector ? d.DescribeFlow() : "no director"
                };
                return s;
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[RoundProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.flow);
            }

            private static IEnumerator WaitForScene(string name)
            {
                yield return WaitUntil(
                    () => SceneManager.GetActiveScene().name == name && SceneManager.GetActiveScene().isLoaded,
                    "scene '" + name + "' not active");
            }

            private static IEnumerator WaitUntil(Func<bool> condition, string what)
            {
                float start = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - start > SceneTimeout)
                    {
                        Debug.LogError("[RoundProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        Application.Quit(3);
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
