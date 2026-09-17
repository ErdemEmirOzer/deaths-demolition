#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-026 · Automatic run bot (development Player / Editor only).
    ///
    /// <c>-ddQaBot &lt;outDir&gt;</c> arms it; <c>-ddQaBotRounds N</c> (default 8), <c>-ddQaBotSeed S</c>
    /// (default 26), <c>-ddQaBotRoundTimeout s</c> (default 240), <c>-ddProfilePath</c> isolates the
    /// profile. The bot boots the real menu → selection → run flow, then drives the player through
    /// the project's own Input System actions with a virtual gamepad (<see cref="QaVirtualGamepad"/>):
    /// nearest-zombie aim, burst fire with line of sight, a fighting distance, a retreat window
    /// after damage, NavMesh patrol between spawn points otherwise. The upgrade screen between
    /// rounds is skipped through <see cref="RunDirector.CloseUpgradeScreen"/> (the Skip button's
    /// own call). It changes no gameplay code and observes only public state.
    ///
    /// Determinism: <c>UnityEngine.Random.InitState(seed)</c> at run start seeds every global draw
    /// (spawn weights, look seeds, spread); the bot's own choices use a seeded System.Random.
    /// Frame timing is not seeded, so two runs agree on the spawn *sequence hash* far more often
    /// than on the kill timeline; both hashes are reported and the gate is "N rounds completed".
    /// </summary>
    public static class QaRunBot
    {
        public const string OutFlag = "-ddQaBot";
        public const string RoundsFlag = "-ddQaBotRounds";
        public const string SeedFlag = "-ddQaBotSeed";
        public const string RoundTimeoutFlag = "-ddQaBotRoundTimeout";
        public const string ProfileFlag = "-ddProfilePath";
        /// <summary>WP-052: start the run at this round (the Executioner rounds without playing 1–7 first).</summary>
        public const string StartRoundFlag = "-ddQaBotStartRound";
        /// <summary>WP-052: write tier readability frames into this directory (first Special / Elite / Ultra spawn per round, the Ultra overhead).</summary>
        public const string FramesFlag = "-ddQaBotFrames";
        /// <summary>WP-052: evidence runs only — the player cannot die (PlayerHealth.DiagnosisInvulnerable), so the late rounds are reached; the summary is marked and never a QA pass.</summary>
        public const string InvulnerableFlag = "-ddQaBotInvulnerable";
        /// <summary>WP-044 K1: <c>-ddQaAllocScan N</c> — at round N (spawner on, crowd live) disable every Behaviour type in turn and sample 3 frames of GC (AllocationProbeTests pattern); writes alloc-scan.txt. The player is invulnerable during the scan; the run is a measurement, not a QA pass.</summary>
        public const string AllocScanFlag = "-ddQaAllocScan";
        /// <summary>WP-055: evidence only — the bot grants itself the Special Weapons in turn (Kagura → Ofuda → Tide) whenever a body is near, writes one frame per sw_* beat × tier; summary marked, never a QA pass.</summary>
        public const string SpecialFlag = "-ddQaBotSpecial";
        /// <summary>WP-059: evidence runs only — this many seconds after run-ready the bot takes a lethal hit from the nearest zombie's side, so the death beat (hurt → collapse → overlay → Results) is captured as deathframe-* PNGs (with -ddQaBotFrames); the summary is marked and never a QA pass.</summary>
        public const string DieAfterFlag = "-ddQaBotDieAfter";
        public const int DefaultRounds = 8;
        public const int DefaultSeed = 26;
        public const int GateRoundsMin = 4;              // C-00 closure decision: bot gate = round >= 4 + no death before it
        public const float DefaultRoundTimeout = 420f;   // WP-044: round 8 (pool ~80, tier bodies) needs > 300 s at bot pace

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(QaArgs.Arg(OutFlag)))
            {
                return;
            }

            string profilePath = QaArgs.Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[QaRunBot] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = QaArgs.Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            Driver d = Arm(outDir, QaArgs.ArgInt(RoundsFlag, DefaultRounds, 1, 50), QaArgs.ArgInt(SeedFlag, DefaultSeed, 0, int.MaxValue),
                QaArgs.ArgFloat(RoundTimeoutFlag, DefaultRoundTimeout, 30f, 3600f), true);
            d.ConfigureTierEvidence(QaArgs.ArgInt(StartRoundFlag, 1, 1, 50), QaArgs.Arg(FramesFlag), QaArgs.Arg(InvulnerableFlag) != null);   // WP-052
            d.ConfigureSpecialEvidence(QaArgs.Arg(SpecialFlag) != null);   // WP-055
            d.ConfigureDeathEvidence(QaArgs.ArgFloat(DieAfterFlag, -1f, -1f, 3600f));   // WP-059
        }

        /// <summary>Also used by the PlayMode slice test: boot=false means the caller already put the run into Fighting.</summary>
        public static Driver Arm(string outDir, int rounds, int seed, float roundTimeout, bool bootFlow)
        {
            var carrier = new GameObject("QaRunBot");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            Driver d = carrier.AddComponent<Driver>();
            d.Configure(outDir, rounds, seed, roundTimeout, bootFlow);
            Debug.Log("[QaRunBot] armed rounds=" + rounds + " seed=" + seed + " roundTimeout=" + QaArgs.F(roundTimeout, 0) + "s out=" + outDir);
            return d;
        }

        [Serializable]
        public sealed class RoundRecord
        {
            public int round;
            public int poolTotal;
            public int kills;
            public int damageEvents;
            public float seconds;
            public bool completed;
            public string outcome;
        }

        [Serializable]
        public sealed class Summary
        {
            public string schema = "dd-qa-bot-v1";
            public int seed;
            public int roundsRequested;
            public int roundsCompleted;
            public int deaths;
            public int totalKills;
            public int totalDamageEvents;
            public float totalSeconds;
            public int spawnCount;
            public string spawnSequenceHash;
            public string outcomeHash;
            public int stuckRecoveries;
            public int relocations;         // blocked fire line → walked back to the open arena centre
            public int wallEscapes;         // frames spent backing off a blocker within 2 m of the muzzle
            public int reloads;
            public string allocMethod;         // WP-044: positive control result for both readings
            public bool allocMethodValid;      // both readings saw the 64 KB bait
            public float allocRecorderAvgB, allocHeapDeltaAvgB;
            public long allocRecorderMaxB, allocHeapDeltaMaxB;
            public int allocFrames, allocFramesOver4K;
            public int edgeBias;               // frames whose move was leaned away from the NavMesh edge
            public bool gatePass;              // bot.rounds gate (round >= 4 + no death before), separate from the full-run pass
            public int upgradesBought;      // WP-044: cheapest affordable card per upgrade screen (DEC-048 one per round)
            public int goldSpent;
            public float meanFrameMs;
            public float meanAimErrorDeg;     // angle between the rig's aim direction and the bot's target direction while a target was aimed
            public string aimKind;            // PlayerAim.AimKind seen most (Stick expected)
            public int fireFrames;
            public string swBeats;            // WP-055: sw_push/sw_web/sw_pull beats seen (kind=count), "" when none
            public int swActivations;         // WP-055: Special Weapons the evidence bot granted itself (0 on a QA run)
            public string fireLineHits;       // what a ray along the rig's aim from the muzzle hits while firing (layer:name = frames), top 6
            public bool pass;
            public string note;
            public List<RoundRecord> rounds = new List<RoundRecord>();
        }

        public sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeoutSeconds = 120f;
            private const float PathRefreshSeconds = 0.4f;
            private const float WaypointReachRadius = 1.6f;
            private const float StuckSeconds = 3f;
            private const float StuckDistance = 0.4f;
            private const float UpgradeSkipDelay = 0.6f;

            private string _outDir;
            private int _rounds;
            private int _seed;
            private float _roundTimeout;
            private bool _bootFlow;

            private QaVirtualGamepad _pad;
            private QaBotBrain _brain;
            private System.Random _random;
            private readonly Summary _summary = new Summary();
            private readonly StringBuilder _events = new StringBuilder();
            private readonly List<int> _spawnTiers = new List<int>(512);
            private readonly List<Vector3> _waypoints = new List<Vector3>();
            private NavMeshPath _path;   // created in Start: NavMeshPath may not be constructed in a MonoBehaviour field initialiser
            private readonly List<float> _frameMs = new List<float>(65536);

            private RoundRecord _current;
            private float _roundStart;
            private float _runStart = -1f;
            private float _lastDamageTime = -999f;
            private float _lastPathTime = -999f;
            private int _pathCorner;
            private Vector3 _goal;
            private bool _hasGoal;
            private bool _hunting;
            private bool _relocating;
            private float _relocateSince;
            private Collider _closeBlocker;
            private bool _lastMuzzleClear, _lastEyeClear;
            private float _lastTrace = -999f;
            private float _blockedSince = -1f;
            private Vector3 _arenaCentre;
            private Vector3 _stuckAnchor;
            private float _stuckTimer;
            private float _upgradeSeen = -1f;
            private bool _running;
            private bool _finished;
            private int _deathRound, _timeoutRound;   // round of the death / timeout (0 = none)
            private bool _externalQuit;
            private bool _died;
            private string _fail;
            private float _reloadCooldown;
            private double _aimErrorSum;
            private int _aimErrorSamples;
            private int _aimKindStick, _aimKindPointer;
            private readonly Dictionary<string, int> _fireHits = new Dictionary<string, int>();

            // WP-044 allocation arbiter (C-00 / S-07 WP-047 finding: GC.GetAllocatedBytesForCurrentThread is always 0 on Unity Mono and
            // ProfilerRecorder "GC Allocated In Frame" read 0 in batchmode): in the dev Player two readings per frame — the recorder and
            // the managed-heap delta (GC.GetTotalMemory(false), rising steps only) — each validated by a 64 KB bait allocation first.
            private ProfilerRecorder _gcRecorder;
            private long _lastHeap = -1;
            private double _allocRecorderSum, _allocHeapSum;
            private long _allocRecorderMax, _allocHeapMax;
            private int _allocFrames, _allocFramesOver4K;
            private byte[] _bait;
            private int _targetFrameRateBefore = int.MinValue;
            private int _baitPhase;            // 0 = not yet, 1 = bait allocated this frame, 2 = read next frame, 3 = done
            private long _baitRecorder = -1, _baitHeap = -1, _heapBeforeBait;

            public bool Finished => _finished;
            public Summary Result => _summary;

            public void Configure(string outDir, int rounds, int seed, float roundTimeout, bool bootFlow)
            {
                _outDir = outDir;
                _rounds = rounds;
                _seed = seed;
                _roundTimeout = roundTimeout;
                _bootFlow = bootFlow;
                _summary.seed = seed;
                _summary.roundsRequested = rounds;
            }

            // ------------------------------------------------------------ WP-052 · tier readability evidence
            private int _startRound = 1;
            private string _framesDir;
            private readonly HashSet<string> _framesTaken = new HashSet<string>();
            private struct PendingFrame { public string Name; public float Due; public EnemyTier Tier; public float Deadline; }
            private bool _invulnerable;
            private readonly List<PendingFrame> _pendingFrames = new List<PendingFrame>();
            /// <summary>WP-052 readout: frames written (tests).</summary>
            public int FramesWritten { get; private set; }
            // WP-059 · death evidence: seconds after run-ready at which the bot takes a lethal hit (< 0 = never); frames queued from the death.
            private float _dieAfterSeconds = -1f;
            private bool _deathEvidence;
            public const float DeathFrameImpactSeconds = 0.12f, DeathFrameCollapseSeconds = 0.9f, DeathFrameOverlaySeconds = 2.0f;
            public const float DeathFramesQuitTimeoutSeconds = 4f;

            /// <summary>WP-059: die this many unscaled seconds after run-ready (&lt; 0 = never); marks the run as evidence (never a QA pass).</summary>
            public void ConfigureDeathEvidence(float dieAfterSeconds)
            {
                _dieAfterSeconds = dieAfterSeconds;
                _deathEvidence = dieAfterSeconds >= 0f;
            }

            /// <summary>WP-052: start round (1 = the normal run) and the frames directory ("" = no frames).</summary>
            public void ConfigureTierEvidence(int startRound, string framesDir, bool invulnerable = false)
            {
                _startRound = Mathf.Max(1, startRound);
                _framesDir = framesDir;
                _invulnerable = invulnerable;
                if (!string.IsNullOrEmpty(_framesDir)) Directory.CreateDirectory(_framesDir);
            }

            private const float TierFrameNearMetres = 10f, TierFrameDeadlineSeconds = 25f;

            private void QueueFrame(string name, float delaySeconds, EnemyTier tier = EnemyTier.Standard)
            {
                if (string.IsNullOrEmpty(_framesDir) || !_framesTaken.Add(name)) return;
                _pendingFrames.Add(new PendingFrame { Name = name, Due = Time.unscaledTime + delaySeconds, Tier = tier, Deadline = Time.unscaledTime + TierFrameDeadlineSeconds });
            }

            private static bool TierNearPlayer(EnemyTier tier, float metres)
            {
                Transform p = PlayerRegistry.Primary != null ? PlayerRegistry.Primary.transform : null;
                if (p == null) return false;
                foreach (ZombieEnemy z in UnityEngine.Object.FindObjectsOfType<ZombieEnemy>())
                {
                    if (z.IsAlive && z.Tier == tier && (z.transform.position - p.position).sqrMagnitude <= metres * metres) return true;
                }
                return false;
            }

            /// <summary>
            /// One frame per tick at most (ScreenCapture writes at end of frame). A tier's first spawn is captured once a
            /// body of that tier is within 10 m of the player (readable in frame), or at the 25 s deadline; the Ultra
            /// overhead is captured on the beat (Tier = Standard marks "no proximity wait").
            /// </summary>
            private void FlushPendingFrame()
            {
                for (int i = 0; i < _pendingFrames.Count; i++)
                {
                    PendingFrame f = _pendingFrames[i];
                    if (Time.unscaledTime < f.Due) continue;
                    bool proximity = f.Tier == EnemyTier.Standard || TierNearPlayer(f.Tier, TierFrameNearMetres) || Time.unscaledTime >= f.Deadline;
                    if (!proximity) continue;
                    _pendingFrames.RemoveAt(i);
                    ScreenCapture.CaptureScreenshot(Path.Combine(_framesDir, f.Name + ".png"));
                    FramesWritten++;
                    Log("frame " + f.Name + (Time.unscaledTime >= f.Deadline ? " (deadline)" : " (near)"));
                    return;
                }
            }

            private void OnCombatBeat(EnemyCombatEventInfo info)
            {
                if (info.Kind == EnemyCombatEventKind.AttackCommit && info.Tier == EnemyTier.Ultra && info.Style == ZombieAttackStyle.Overhead)
                {
                    QueueFrame("tierframe-r" + (RunDirector.Instance != null ? RunDirector.Instance.Round : 0) + "-ultra-overhead", 0f);
                }

                // WP-055: one frame per sw_* row × tier (the beat happens at the player's feet — no proximity wait)
                if (info.Kind == EnemyCombatEventKind.SpecialPush || info.Kind == EnemyCombatEventKind.SpecialWeb || info.Kind == EnemyCombatEventKind.SpecialPull)
                {
                    _swBeats[(int)info.Kind - (int)EnemyCombatEventKind.SpecialPush]++;
                    QueueFrame("swframe-r" + (RunDirector.Instance != null ? RunDirector.Instance.Round : 0) + "-" + info.RowName + "-" + info.Tier.ToString().ToLowerInvariant(), 0.05f);
                }
            }

            // ------------------------------------------------------------ WP-055 · Special Weapon reaction evidence
            private bool _specialEvidence;
            private int _specialCycle;
            private float _nextSpecialAt;
            private readonly int[] _swBeats = new int[3];
            private const float SpecialNearMetres = 12f, SpecialGapSeconds = 4f;

            /// <summary>WP-055: evidence-only Special Weapon cycling (summary marked, never a QA pass).</summary>
            public void ConfigureSpecialEvidence(bool enabled) => _specialEvidence = enabled;

            private void TickSpecialEvidence(PlayerRig rig)
            {
                if (!_specialEvidence || WeaponRegistry.ActiveController == null || Time.unscaledTime < _nextSpecialAt) return;
                WeaponController weapons = WeaponRegistry.ActiveController;
                if (weapons.Special.IsActive) { _nextSpecialAt = Time.unscaledTime + SpecialGapSeconds; return; }
                bool near = false;
                foreach (ZombieEnemy z in UnityEngine.Object.FindObjectsOfType<ZombieEnemy>())
                {
                    if (z.IsAlive && (z.transform.position - rig.transform.position).sqrMagnitude <= SpecialNearMetres * SpecialNearMetres) { near = true; break; }
                }
                if (!near) { _nextSpecialAt = Time.unscaledTime + 0.5f; return; }
                var id = (SpecialWeaponId)(_specialCycle++ % 3);
                weapons.ActivateSpecialWeapon(id);
                _summary.swActivations++;
                _nextSpecialAt = Time.unscaledTime + SpecialGapSeconds;
                Log("special-evidence " + id + " granted (WP-055; not a QA pass)");
            }

            private IEnumerator Start()
            {
                _path = new NavMeshPath();
                Directory.CreateDirectory(_outDir);
                UnityEngine.Random.InitState(_seed);
                _random = new System.Random(_seed);
                _brain = new QaBotBrain(_seed);

                if (_bootFlow)
                {
                    yield return WaitForScene(PrototypeScenes.MainMenu, SceneTimeoutSeconds);
                    if (!string.IsNullOrEmpty(_fail)) { Finish(); yield break; }
                    yield return null;
                    PrototypeUiFlow.GoToSelection();
                    yield return WaitForScene(PrototypeScenes.Selection, SceneTimeoutSeconds);
                    if (!string.IsNullOrEmpty(_fail)) { Finish(); yield break; }
                    PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                    UnityEngine.Random.InitState(_seed);   // the run's first draw happens after StartRun
                    PrototypeUiFlow.StartRun();
                    yield return WaitForScene(PrototypeScenes.Run, SceneTimeoutSeconds);
                    if (!string.IsNullOrEmpty(_fail)) { Finish(); yield break; }
                }

                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting", SceneTimeoutSeconds);
                if (!string.IsNullOrEmpty(_fail)) { Finish(); yield break; }

                // WP-052: jump to the evidence rounds (the run is already Fighting; BeginRound re-opens the ledger at the target round)
                if (_startRound > 1 && RunDirector.Instance != null && RunDirector.Instance.Round < _startRound)
                {
                    RunDirector.Instance.BeginRound(_startRound);
                    yield return null;
                    Log("start-round " + RunDirector.Instance.Round + " (WP-052 tier evidence)");
                }

                if (_invulnerable && PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
                {
                    PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;   // WP-052 evidence runs: reach the late rounds; never a QA pass
                    Log("invulnerable (WP-052 evidence run — summary marked, not a QA pass)");
                }

                Subscribe();
                _pad = new QaVirtualGamepad();
                _pad.Neutral();
                BuildWaypoints();
                _runStart = Time.unscaledTime;
                BeginRoundRecord(RunDirector.Instance.Round);
                Log("run-ready round=" + RunDirector.Instance.Round + " waypoints=" + _waypoints.Count + " pad=" + _pad.IsAlive);
                _running = true;
            }

            private void Subscribe()
            {
                Application.quitting += OnApplicationQuitting;
                RunEvents.RoundStarted += OnRoundStarted;
                RunEvents.RoundCompleted += OnRoundCompleted;
                RunEvents.RunEnded += OnRunEnded;
                PlayerHealth.AnyDamaged += OnDamaged;
                PlayerHealth.AnyPlayerDied += OnDied;
                if (EnemySpawnDirector.Instance != null)
                {
                    EnemySpawnDirector.Instance.EnemySpawned += OnEnemySpawned;
                }
                EnemyCombatEvents.Raised += OnCombatBeat;   // WP-052
            }

            private void Unsubscribe()
            {
                EnemyCombatEvents.Raised -= OnCombatBeat;   // WP-052
                Application.quitting -= OnApplicationQuitting;
                RunEvents.RoundStarted -= OnRoundStarted;
                RunEvents.RoundCompleted -= OnRoundCompleted;
                RunEvents.RunEnded -= OnRunEnded;
                PlayerHealth.AnyDamaged -= OnDamaged;
                PlayerHealth.AnyPlayerDied -= OnDied;
                if (EnemySpawnDirector.Instance != null)
                {
                    EnemySpawnDirector.Instance.EnemySpawned -= OnEnemySpawned;
                }
            }

            /// <summary>
            /// The Player is going down without the driver asking for it (SIGTERM → Unity quits with exit 0,
            /// or a game-side quit). Leave the partial evidence behind so the run is not a silent hole.
            /// </summary>
            private void OnApplicationQuitting()
            {
                if (_finished)
                {
                    return;
                }

                _externalQuit = true;
                _fail = "player quit externally (Application.quitting) at " + QaArgs.F(_runStart >= 0f ? Time.unscaledTime - _runStart : 0f, 1) +
                        " s, phase=" + (RunDirector.Instance != null ? RunDirector.Instance.Phase.ToString() : "n/a") +
                        ", round=" + (_current != null ? _current.round : 0) + " kills=" + _summary.totalKills;
                Log("external-quit " + _fail);
                Finish();
            }

            private void OnEnemySpawned(EnemySpawnContext ctx)
            {
                _spawnTiers.Add((int)ctx.Tier);
                _summary.spawnCount++;
                if (ctx.Tier != EnemyTier.Standard) QueueFrame("tierframe-r" + ctx.Round + "-" + ctx.Tier.ToString().ToLowerInvariant(), 1f, ctx.Tier);   // WP-052: captured when that tier is near the player
            }

            private void OnRoundStarted(int round)
            {
                if (_current == null || _current.round != round)
                {
                    BeginRoundRecord(round);
                }

                Log("round-start " + round + " pool=" + (RunDirector.Instance != null ? RunDirector.Instance.RoundPoolTotal : 0));
                int scanRound = QaArgs.ArgInt(AllocScanFlag, 0, 0, 99);
                if (scanRound > 0 && round == scanRound && !_allocScanDone)
                {
                    _allocScanDone = true;
                    StartCoroutine(AllocScan(round));
                }
            }

            private bool _allocScanDone;
            private bool _allocScanning;

            /// <summary>
            /// WP-044 K1 attribution with the spawner on: 8 s into the round (crowd around the player) freeze the bot's input, make the
            /// player invulnerable, take a 3-frame baseline, then disable every Behaviour type in the scene one at a time (zombies,
            /// player rig, scene objects; Camera/QA drivers excluded), settle 1 frame, sample 3 frames, restore — the types whose
            /// absence saves bytes/count are the allocators. Same recorders and thresholds as AllocationProbeTests (WP-053C).
            /// </summary>
            private IEnumerator AllocScan(int round)
            {
                yield return new WaitForSecondsRealtime(8f);
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig == null || rig.Health == null) yield break;
                _allocScanning = true;
                _invulnerable = true;   // measurement run: never a QA pass
                rig.Health.DiagnosisInvulnerable = true;
                if (_pad != null) { _pad.Neutral(); _pad.Flush(); }
                // -ddQaAllocScanCrowd N: match the perf probe's forced crowd (PilotPerfProbe force-40) so the reading is comparable
                int crowd = QaArgs.ArgInt("-ddQaAllocScanCrowd", 0, 0, 200);
                if (crowd > 0 && EnemySpawnDirector.Instance != null)
                {
                    EnemySpawnDirector.Instance.ForceActiveTarget(crowd, true);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 40f)
                    {
                        int a = 0; IReadOnlyList<ZombieEnemy> lv = ZombieEnemy.Live;
                        for (int i = 0; i < lv.Count; i++) if (lv[i] != null && lv[i].IsAlive) a++;
                        if (a >= crowd) break;
                        yield return null;
                    }
                    yield return new WaitForSecondsRealtime(3f);   // let them close in and start attacking (the probe's steady state)
                }
                bool profilerWas = UnityEngine.Profiling.Profiler.enabled;
                UnityEngine.Profiling.Profiler.enabled = true;
                var sb = new StringBuilder();
                var rows = new List<KeyValuePair<string, long[]>>();
                using (var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                using (var count = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count"))
                {
                    yield return null; yield return null;
                    long zb = 0, zc = 0;
                    yield return SampleAlloc(bytes, count, 6, (b, c) => { zb = b; zc = c; });
                    int alive = 0; IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                    for (int i = 0; i < live.Count; i++) if (live[i] != null && live[i].IsAlive) alive++;
                    sb.Append("alloc-scan round=").Append(round).Append(" alive=").Append(alive).Append(" baseline bytes=").Append(zb).Append(" count=").Append(zc).Append(" (6 frames avg)\n");
                    Log("alloc-scan baseline round=" + round + " alive=" + alive + " bytes=" + zb + " count=" + zc);

                    var byType = new Dictionary<Type, List<Behaviour>>();
                    foreach (Behaviour b in UnityEngine.Object.FindObjectsOfType<Behaviour>())
                    {
                        if (b == null) continue;
                        Type t = b.GetType();
                        if (t == typeof(Camera) || t.Namespace == null || !t.Namespace.StartsWith("DD.")) continue;   // game code only
                        if (t.Namespace.StartsWith("DD.Prototype.Qa")) continue;
                        if (!byType.TryGetValue(t, out List<Behaviour> list)) byType[t] = list = new List<Behaviour>();
                        list.Add(b);
                    }

                    sb.Append("behaviour types=").Append(byType.Count).Append("\n");
                    foreach (KeyValuePair<Type, List<Behaviour>> kv in byType)
                    {
                        var wasEnabled = new bool[kv.Value.Count];
                        for (int i = 0; i < kv.Value.Count; i++) { wasEnabled[i] = kv.Value[i] != null && kv.Value[i].enabled; if (kv.Value[i] != null) kv.Value[i].enabled = false; }
                        yield return null;
                        long tb = 0, tc = 0;
                        yield return SampleAlloc(bytes, count, 3, (b, c) => { tb = b; tc = c; });
                        for (int i = 0; i < kv.Value.Count; i++) if (kv.Value[i] != null) kv.Value[i].enabled = wasEnabled[i];
                        yield return null;
                        rows.Add(new KeyValuePair<string, long[]>(kv.Key.FullName + " x" + kv.Value.Count, new[] { tb, tc, zb - tb, zc - tc }));
                    }
                }

                UnityEngine.Profiling.Profiler.enabled = profilerWas;
                rows.Sort((a, b) => b.Value[2].CompareTo(a.Value[2]));
                for (int i = 0; i < rows.Count; i++)
                {
                    long[] v = rows[i].Value;
                    if (i < 25 || Math.Abs(v[2]) > 2048 || Math.Abs(v[3]) > 50)
                        sb.Append("without ").Append(rows[i].Key).Append(" bytes=").Append(v[0]).Append(" count=").Append(v[1]).Append(" savedBytes=").Append(v[2]).Append(" savedCount=").Append(v[3]).Append('\n');
                }

                try { Directory.CreateDirectory(_outDir); File.WriteAllText(Path.Combine(_outDir, "alloc-scan.txt"), sb.ToString()); } catch (Exception) { }
                Log("alloc-scan done types=" + rows.Count + " top=" + (rows.Count > 0 ? rows[0].Key + " savedBytes=" + rows[0].Value[2] : "n/a"));
                _allocScanning = false;
            }

            private static IEnumerator SampleAlloc(ProfilerRecorder bytes, ProfilerRecorder count, int frames, Action<long, long> result)
            {
                long b = 0, c = 0;
                for (int i = 0; i < frames; i++)
                {
                    yield return null;
                    b += bytes.LastValue;
                    c += count.LastValue;
                }
                result(b / frames, c / frames);
            }

            private void OnRoundCompleted(int round)
            {
                if (_current != null && _current.round == round)
                {
                    CloseRoundRecord(true, "completed");
                    _summary.roundsCompleted++;
                }

                Log("round-complete " + round + " kills=" + (_current != null ? _current.kills : 0));
                if (_summary.roundsCompleted >= _rounds)
                {
                    _running = false;
                    Finish();
                }
            }

            private void OnRunEnded(RunSummary s)
            {
                Log("run-ended reason=" + s.EndReason + " round=" + s.ReachedRound + " kills=" + s.TotalKills);
                if (s.EndReason == RunEndReason.PlayerDeath && !_died)
                {
                    _died = true;   // AnyPlayerDied can arrive after RunEnded; count the death from the summary
                    if (_deathRound <= 0) _deathRound = _current != null ? _current.round : s.ReachedRound;
                    _summary.deaths++;
                }

                if (!_finished)
                {
                    if (_current != null && !_current.completed)
                    {
                        CloseRoundRecord(false, "run-ended:" + s.EndReason);
                    }

                    _running = false;
                    Finish();
                }
            }

            private void OnDamaged(PlayerDamageInfo info)
            {
                _lastDamageTime = Time.unscaledTime;
                if (_current != null) _current.damageEvents++;
                _summary.totalDamageEvents++;
                if (_summary.totalDamageEvents <= 5 || info.HealthAfter <= 0f)
                {
                    // WP-044 / WP-047E finding (instant PlayerDeath at run start): where, how much, from which direction
                    PlayerRig rig = PlayerRegistry.Primary;
                    Log("damage #" + _summary.totalDamageEvents + " t+" + QaArgs.F(_runStart >= 0f ? Time.unscaledTime - _runStart : -1f, 2) + "s amount=" + QaArgs.F(info.AppliedAmount, 1) +
                        " hp " + QaArgs.F(info.HealthBefore, 1) + "→" + QaArgs.F(info.HealthAfter, 1) + "/" + QaArgs.F(info.MaxHealth, 0) + " at=" + V(info.WorldPoint) + " dir=" + V(info.HitDirection) +
                        " player=" + (rig != null ? V(rig.transform.position) : "n/a") + " onNavMesh=" + (rig != null && NavMesh.SamplePosition(rig.transform.position, out NavMeshHit nh, 0.5f, NavMesh.AllAreas) ? "1" : "0"));
                }
            }

            private void OnDied(PlayerDeathInfo info)
            {
                _died = true;
                _summary.deaths++;
                int round = _current != null ? _current.round : 0;
                if (_deathRound <= 0) _deathRound = round;
                Log("player-died round=" + round + " t+" + QaArgs.F(_runStart >= 0f ? Time.unscaledTime - _runStart : -1f, 2) + "s killedBy=" + info.KilledBy + " pos=" + V(info.Position) + " overkill=" + QaArgs.F(info.Overkill, 1) + " kills=" + (_current != null ? _current.kills : 0));
                // WP-059: the death beat on three frames — the hit, the slowed collapse, the overlay before Results
                // (unscaled delays; Standard tier = no proximity wait). Flushed after Finish, QuitSoon waits for them.
                QueueFrame("deathframe-r" + round + "-impact", DeathFrameImpactSeconds);
                QueueFrame("deathframe-r" + round + "-collapse", DeathFrameCollapseSeconds);
                QueueFrame("deathframe-r" + round + "-overlay", DeathFrameOverlaySeconds);
            }

            /// <summary>WP-059 evidence: a lethal hit from the nearest zombie's side (or from behind when none is near) — the real damage path, so the hurt / death chain is what the player would see.</summary>
            private void DieNow(PlayerRig rig)
            {
                _dieAfterSeconds = -1f;
                if (rig == null || rig.Health == null || rig.Health.IsDead) return;
                rig.Health.DiagnosisInvulnerable = false;
                Vector3 from = rig.transform.position - rig.transform.forward * 1.2f;
                float best = float.MaxValue;
                foreach (ZombieEnemy z in UnityEngine.Object.FindObjectsOfType<ZombieEnemy>())
                {
                    if (!z.IsAlive) continue;
                    float d = (z.transform.position - rig.transform.position).sqrMagnitude;
                    if (d < best) { best = d; from = z.transform.position; }
                }
                Log("die-after (WP-059 death evidence) from=" + QaArgs.F(from.x, 1) + "," + QaArgs.F(from.z, 1));
                rig.Health.ApplyDamage(rig.Health.Max * 4f, from, "QaRunBot.DieAfter");
            }

            private void BeginRoundRecord(int round)
            {
                _current = new RoundRecord { round = round, poolTotal = RunDirector.Instance != null ? RunDirector.Instance.RoundPoolTotal : 0 };
                _roundStart = Time.unscaledTime;
                _summary.rounds.Add(_current);
                PickNewGoal();
            }

            private void CloseRoundRecord(bool completed, string outcome)
            {
                if (_current == null) return;
                _current.completed = completed;
                _current.outcome = outcome;
                _current.seconds = Time.unscaledTime - _roundStart;
                _current.kills = RunDirector.Instance != null ? RunDirector.Instance.RoundKills : _current.kills;
                _summary.totalKills += _current.kills;
            }

            private void Update()
            {
                if (_finished)
                {
                    FlushPendingFrame();   // WP-059: the death frames land after the run ended
                    return;
                }

                if (!_running)
                {
                    return;
                }

                _frameMs.Add(Time.unscaledDeltaTime * 1000f);
                FlushPendingFrame();   // WP-052
                SampleAllocation();    // WP-044 allocation arbiter
                if (_allocScanning) return;   // the scan owns the frame: no bot input, no steering
                RunDirector run = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                if (run == null || rig == null)
                {
                    return;
                }
                TickSpecialEvidence(rig);   // WP-055

                if (_dieAfterSeconds >= 0f && !_died && Time.unscaledTime - _runStart >= _dieAfterSeconds)
                {
                    DieNow(rig);   // WP-059
                    if (_finished) return;   // the death ended the run inside ApplyDamage (RunEnded → Finish → pad disposed)
                }

                if (_current != null && Time.unscaledTime - _roundStart > _roundTimeout)
                {
                    CloseRoundRecord(false, "timeout");
                    _timeoutRound = _current.round;
                    _fail = "round " + _current.round + " timed out after " + QaArgs.F(_roundTimeout, 0) + " s (kills " + run.RoundKills + "/" + run.RoundPoolTotal + ")";
                    _running = false;
                    Finish();
                    return;
                }

                switch (run.Phase)
                {
                    case RunPhase.Upgrading:
                    case RunPhase.RoundComplete:
                        if (_upgradeSeen < 0f) _upgradeSeen = Time.unscaledTime;
                        _pad.Set(Vector2.zero, Vector2.zero, false, false, false);
                        _pad.Flush();
                        if (run.Phase == RunPhase.Upgrading && Time.unscaledTime - _upgradeSeen > UpgradeSkipDelay)
                        {
                            // WP-044: a player buys between rounds — the bot takes the cheapest affordable card once the offer is
                            // ready (shop.TryPurchase is the card button's own call; DEC-048 one purchase per round), then CONTINUE
                            RoundUpgradeShop shop = run.Shop;
                            if (shop != null && shop.Status == UpgradeOfferStatus.Ready && !shop.IsLocked)
                            {
                                // the dearest affordable card (cost is the shop's own value ladder; run3 left 230 gold unspent with the cheapest rule)
                                int pick = -1, pickCost = -1;
                                for (int c = 0; c < shop.Cards.Count; c++)
                                {
                                    if (!shop.CanAfford(c)) continue;
                                    int cost = shop.Cards[c].Cost;
                                    if (cost > pickCost) { pickCost = cost; pick = c; }
                                }

                                if (pick >= 0 && shop.TryPurchase(pick))
                                {
                                    _summary.upgradesBought++;
                                    _summary.goldSpent += pickCost;
                                    Log("upgrade-bought round=" + run.Round + " card=" + shop.Cards[pick].DisplayName + " cost=" + pickCost + " goldLeft=" + shop.Gold);
                                }
                                else
                                {
                                    Log("upgrade-none round=" + run.Round + " gold=" + shop.Gold + " affordable=" + (pick >= 0));
                                }
                            }
                            else if (shop != null && shop.Status != UpgradeOfferStatus.Ready)
                            {
                                if (Time.unscaledTime - _upgradeSeen < UpgradeSkipDelay + 3f) return;   // offer still building; give it a moment
                                Log("upgrade-offer-not-ready status=" + shop.Status + " fault=" + shop.Fault);
                            }

                            run.CloseUpgradeScreen();   // the CONTINUE button's call
                            _upgradeSeen = -1f;
                            Log("upgrade-closed");
                        }

                        return;

                    case RunPhase.Countdown:
                    case RunPhase.None:
                    case RunPhase.Dead:
                        _upgradeSeen = -1f;
                        _pad.Set(Vector2.zero, Vector2.zero, false, false, false);
                        _pad.Flush();
                        return;
                }

                _upgradeSeen = -1f;
                if (_current != null)
                {
                    _current.kills = run.RoundKills;
                }

                Tick(rig);
            }

            private void Tick(PlayerRig rig)
            {
                Vector3 pos = rig.transform.position;
                Vector3 fwd, right;
                CameraBasis(out fwd, out right);

                Vector3 crowdDir;
                ZombieEnemy target = Nearest(pos, out int nearby, out crowdDir);
                var o = new QaBotObservation
                {
                    PlayerPosition = pos,
                    CameraForward = fwd,
                    CameraRight = right,
                    HealthRatio = rig.Health != null ? rig.Health.Normalized : 1f,
                    SecondsSinceDamage = Time.unscaledTime - _lastDamageTime,
                    NearbyEnemies = nearby,
                    CrowdDirection = crowdDir,
                    Time = Time.unscaledTime
                };

                if (target != null)
                {
                    o.HasTarget = true;
                    o.TargetPosition = target.transform.position;
                    Vector3 d = o.TargetPosition - pos;
                    d.y = 0f;
                    o.TargetDistance = d.magnitude;
                    // the fire line: from the muzzle (≈ 1.0 m) toward the target's chest — a planter or curb edge that the
                    // 1.2 m eye line clears can still stop the bullet, so the bot checks the line the shot will actually take
                    Transform muzzle = rig.MuzzleAnchor != null ? rig.MuzzleAnchor : rig.transform;
                    bool muzzleClear = HasLineOfSight(muzzle.position, o.TargetPosition + Vector3.up * 1.0f, o.TargetDistance);
                    bool eyeClear = HasLineOfSight(pos + Vector3.up * 1.2f, o.TargetPosition + Vector3.up * 1.2f, o.TargetDistance);
                    _closeBlocker = CloseBlocker(muzzle.position, o.TargetPosition + Vector3.up * 1.0f, 2.0f);
                    o.TargetLineOfSight = muzzleClear || (eyeClear && _closeBlocker == null);   // a wall at the muzzle eats every shot; farther out the runtime's hitscan is the judge
                    _lastMuzzleClear = muzzleClear; _lastEyeClear = eyeClear;
                    if (!muzzleClear && o.TargetDistance <= QaBotBrain.FireRange)
                    {
                        if (_blockedSince < 0f) _blockedSince = Time.unscaledTime;
                        if (Time.unscaledTime - _blockedSince > 2.5f && !_relocating)
                        {
                            _relocating = true;   // walls/planters keep eating the shots: walk to the open centre of the arena
                            _relocateSince = Time.unscaledTime;
                            _goal = _arenaCentre;
                            _hasGoal = true;
                            _hunting = false;
                            _lastPathTime = -999f;
                            _summary.relocations++;
                        }
                    }
                    else
                    {
                        _blockedSince = -1f;
                        // the line is clear again (or the centre is reached, or 8 s passed): relocation is over — run2 showed a
                        // relocation that never ended, which demoted every target and put the bot into a reload loop
                        if (_relocating && (muzzleClear || (pos - _arenaCentre).sqrMagnitude < 9f || Time.unscaledTime - _relocateSince > 8f)) _relocating = false;
                    }
                }

                if (_relocating && (target == null || Time.unscaledTime - _relocateSince > 8f)) _relocating = false;

                WeaponController wc = WeaponRegistry.ActiveController;
                ClassicWeaponRuntime classic = wc != null ? wc.Classic : null;
                if (classic != null)
                {
                    o.WeaponCanFire = classic.HasAmmo && !classic.IsReloading;   // hold the trigger through the shot cooldown (automatic weapon)
                    o.WeaponNeedsReload = classic.NeedsReload && !classic.IsReloading;
                    o.WeaponEmpty = !classic.HasAmmo && !classic.IsReloading;
                }
                else
                {
                    o.WeaponCanFire = true;
                }
                // WP-055 evidence mode: a Special in hand has no magazine — the trigger must not wait for a classic reload
                // (the classic cannot reload while suspended; back-to-back grants would otherwise starve every shot)
                if (_specialEvidence && wc != null && wc.Special.IsActive) o.WeaponCanFire = true;

                // hunt: a target beyond the engage radius becomes the navigation goal (the last body of a round is
                // usually far away); otherwise patrol between waypoints
                if (target != null && o.TargetDistance > QaBotBrain.EngageRadius)
                {
                    if (!_hunting || (o.TargetPosition - _goal).sqrMagnitude > 4f)
                    {
                        _goal = o.TargetPosition;
                        _hasGoal = true;
                        _hunting = true;
                        _lastPathTime = -999f;
                    }
                }
                else if (_hunting)
                {
                    _hunting = false;
                    PickNewGoal();
                }

                UpdateGoal(pos);
                Vector3 wpDir;
                if (NextWaypointDirection(pos, out wpDir))
                {
                    o.HasWaypoint = true;
                    o.WaypointDirection = wpDir;
                }

                if (_relocating) { o.HasTarget = o.HasTarget && o.TargetDistance < QaBotBrain.PreferredDistanceMin; }   // keep only an emergency retreat; otherwise walk the path
                QaBotCommand cmd = _brain.Decide(o);
                if (cmd.Move.sqrMagnitude > 0.01f)
                {
                    Vector3 wantMove = QaBotBrain.StickToWorld(cmd.Move, fwd, right);
                    Vector3 avoid = nearby >= 3 ? crowdDir : Vector3.zero;   // run3: a wall-avoiding rotation walked the bot into a 10-body crowd
                    NavMeshHit edge;
                    if (NavMesh.FindClosestEdge(pos, out edge, NavMesh.AllAreas) && edge.distance < 2.5f)
                    {
                        Vector3 inward = _arenaCentre - pos; inward.y = 0f;
                        if (inward.sqrMagnitude > 1f) wantMove = (wantMove.normalized + inward.normalized).normalized;   // never kite into the boundary
                        _summary.edgeBias++;
                    }
                    Vector3 steered = SteerOnNavMesh(pos, wantMove, avoid);
                    if (_closeBlocker != null && cmd.Mode != QaBotMode.Patrol)
                    {
                        // hugging a wall with the target beyond it: back off from the blocker before anything else
                        Vector3 away = pos - _closeBlocker.ClosestPoint(pos); away.y = 0f;
                        if (away.sqrMagnitude > 1e-4f) steered = SteerOnNavMesh(pos, (away.normalized * 0.7f + steered * 0.3f).normalized, avoid);
                        _summary.wallEscapes++;
                    }
                    cmd.Move = QaBotBrain.WorldToStick(steered, fwd, right) * Mathf.Clamp01(cmd.Move.magnitude);
                }
                if (Time.unscaledTime - _lastTrace >= 1f)
                {
                    _lastTrace = Time.unscaledTime;
                    Log("t pos=" + V(pos) + " mode=" + cmd.Mode + " tgt=" + (target != null ? QaArgs.F(o.TargetDistance, 1) : "-") + " los=" + (o.TargetLineOfSight ? 1 : 0) +
                        " mz=" + (_lastMuzzleClear ? 1 : 0) + " eye=" + (_lastEyeClear ? 1 : 0) + " blk=" + (_closeBlocker != null ? _closeBlocker.name + "@" + V(_closeBlocker.bounds.center) + "/" + V(_closeBlocker.bounds.size) : "-") +
                        " fire=" + (cmd.Fire ? 1 : 0) + " hp=" + QaArgs.F(rig.Health != null ? rig.Health.Normalized : -1f, 2) + " near=" + nearby + " kills=" + _summary.totalKills + "+" + (_current != null ? _current.kills : 0));
                }
                if (_relocating && target != null && cmd.Mode == QaBotMode.Patrol)
                {
                    Vector3 aimTo = target.transform.position - pos; aimTo.y = 0f;
                    cmd.Aim = QaBotBrain.WorldToStick(aimTo, fwd, right);
                    cmd.Fire = o.TargetLineOfSight && o.TargetDistance <= QaBotBrain.FireRange && o.WeaponCanFire;
                }
                bool reload = cmd.Reload && _reloadCooldown <= 0f;
                if (reload)
                {
                    _reloadCooldown = 1.5f;
                    _summary.reloads++;
                }

                _reloadCooldown -= Time.unscaledDeltaTime;
                if (o.HasTarget && rig.Aim != null)
                {
                    Vector3 aimed = rig.Aim.AimDirection; aimed.y = 0f;
                    Vector3 want = o.TargetPosition - pos; want.y = 0f;
                    if (aimed.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f)
                    {
                        _aimErrorSum += Vector3.Angle(aimed, want);
                        _aimErrorSamples++;
                    }

                    if (rig.Aim.AimKind == AimSourceKind.Stick) _aimKindStick++; else _aimKindPointer++;
                }

                if (cmd.Fire)
                {
                    _summary.fireFrames++;
                    if (rig.Aim != null && (_summary.fireFrames & 3) == 0) SampleFireLine(rig);
                }
                _pad.Set(cmd.Move, cmd.Aim, cmd.Fire, reload, false);
                _pad.Flush();

                // stuck detection (any mode): re-pick the goal when the player barely moved for a while
                if ((pos - _stuckAnchor).sqrMagnitude > StuckDistance * StuckDistance)
                {
                    _stuckAnchor = pos;
                    _stuckTimer = 0f;
                }
                else
                {
                    _stuckTimer += Time.unscaledDeltaTime;
                    if (_stuckTimer > StuckSeconds && cmd.Mode == QaBotMode.Patrol)
                    {
                        _stuckTimer = 0f;
                        _summary.stuckRecoveries++;
                        PickNewGoal();
                    }
                    else if (_stuckTimer > 1.5f && cmd.Mode != QaBotMode.Patrol && !_relocating)
                    {
                        // run4: pinned against the world edge by a crowd (position frozen 15 s, hp → 0) — break out along the
                        // NavMesh toward the open centre; the relocation path keeps firing at anything in range
                        _stuckTimer = 0f;
                        _summary.stuckRecoveries++;
                        _relocating = true;
                        _relocateSince = Time.unscaledTime;
                        _goal = _arenaCentre;
                        _hasGoal = true;
                        _hunting = false;
                        _lastPathTime = -999f;
                        Log("breakout pos=" + V(pos) + " near=" + nearby);
                    }
                }
            }

            private void SampleAllocation()
            {
                if (!_gcRecorder.Valid)
                {
                    // S-02 WP-052 finding: with Application.targetFrameRate == 60 the recorder's LastValue can stay 0 (Editor runner);
                    // the gate measurement runs uncapped and the original cap is restored in Finish
                    _targetFrameRateBefore = Application.targetFrameRate;
                    Application.targetFrameRate = -1;
                    try { _gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"); } catch (Exception) { }
                    if (!_gcRecorder.Valid) return;
                }

                long recorder = _gcRecorder.LastValue;            // bytes allocated during the previous frame
                long heap = GC.GetTotalMemory(false);
                long heapDelta = _lastHeap >= 0 ? heap - _lastHeap : 0;   // a drop = collection; only rising steps count as allocation
                if (heapDelta < 0) heapDelta = 0;

                switch (_baitPhase)
                {
                    case 0:
                        _heapBeforeBait = heap;
                        _bait = new byte[65536];
                        _bait[0] = 1;
                        _baitPhase = 1;
                        _lastHeap = heap;
                        return;                                   // this frame is the bait frame: not counted
                    case 1:
                        _baitRecorder = recorder;                 // the recorder reports the bait frame now
                        _baitHeap = heap - _heapBeforeBait;
                        _baitPhase = 2;
                        _lastHeap = heap;
                        _summary.allocMethodValid = _baitRecorder >= 65536 && _baitHeap >= 65536;
                        _summary.allocMethod = "positive control 64 KB: recorder " + _baitRecorder + " B, heap-delta " + _baitHeap + " B → " + (_summary.allocMethodValid ? "both readings valid" : "READING INVALID (see WP-047)");
                        Log("alloc-method " + _summary.allocMethod);
                        return;
                }

                _lastHeap = heap;
                _allocFrames++;
                _allocRecorderSum += recorder;
                _allocHeapSum += heapDelta;
                if (recorder > _allocRecorderMax) _allocRecorderMax = recorder;
                if (heapDelta > _allocHeapMax) _allocHeapMax = heapDelta;
                if (recorder > 4096) _allocFramesOver4K++;
            }

            private void SampleFireLine(PlayerRig rig)
            {
                Transform muzzle = rig.MuzzleAnchor != null ? rig.MuzzleAnchor : rig.transform;
                Vector3 origin = muzzle.position;
                Vector3 dir = rig.Aim.AimDirection;
                RaycastHit hit;
                string key;
                if (Physics.Raycast(origin, dir, out hit, QaBotBrain.FireRange, PrototypeLayers.BulletHitMask, QueryTriggerInteraction.Ignore))
                {
                    float d = hit.distance;
                    string bucket = d < 1.5f ? "<1.5m" : d < 4f ? "<4m" : d < 10f ? "<10m" : ">=10m";
                    key = LayerMask.LayerToName(hit.collider.gameObject.layer) + ":" + hit.collider.name + "@" + bucket + " dy" + QaArgs.F(hit.point.y - origin.y, 1) + " oy" + QaArgs.F(origin.y - rig.transform.position.y, 2);
                }
                else
                {
                    key = "miss";
                }

                int n;
                _fireHits.TryGetValue(key, out n);
                _fireHits[key] = n + 1;
            }

            private static void CameraBasis(out Vector3 forward, out Vector3 right)
            {
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                if (rig != null)
                {
                    forward = rig.PlanarForward;
                    right = rig.PlanarRight;
                    return;
                }

                forward = Vector3.forward;
                right = Vector3.right;
            }

            private static ZombieEnemy Nearest(Vector3 from, out int nearby, out Vector3 crowdDirection)
            {
                nearby = 0;
                crowdDirection = Vector3.zero;
                Vector3 centroid = Vector3.zero;
                ZombieEnemy best = null;
                float bestSq = float.MaxValue;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive || z.IsDying)
                    {
                        continue;
                    }

                    Vector3 d = z.transform.position - from;
                    d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq <= QaBotBrain.CrowdRadius * QaBotBrain.CrowdRadius)
                    {
                        nearby++;
                        centroid += d;
                    }

                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = z;
                    }
                }

                if (nearby > 0)
                {
                    centroid /= nearby;
                    crowdDirection = centroid.sqrMagnitude > 1e-4f ? centroid.normalized : Vector3.zero;
                }

                return best;
            }

            /// <summary>The environment collider that sits within <paramref name="within"/> metres on the shot line, if any.</summary>
            private static Collider CloseBlocker(Vector3 from, Vector3 to, float within)
            {
                Vector3 d = to - from;
                float len = d.magnitude;
                if (len < 0.05f) return null;
                RaycastHit hit;
                float reach = Mathf.Min(within, Mathf.Max(0.1f, len - 0.45f));
                return Physics.Raycast(from, d / len, out hit, reach, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore) ? hit.collider : null;
            }

            /// <summary>
            /// Keeps a commanded move on walkable ground: the first rotation of the wanted direction whose 1.6 m probe
            /// stays on the NavMesh wins, so the bot slides along walls and planters instead of grinding into them.
            /// </summary>
            private static readonly float[] SteerAngles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 150f, -150f, 180f };
            private static Vector3 SteerOnNavMesh(Vector3 pos, Vector3 want, Vector3 avoidDirection)
            {
                want.y = 0f;
                if (want.sqrMagnitude < 1e-4f) return want;
                want.Normalize();
                avoidDirection.y = 0f;
                bool avoiding = avoidDirection.sqrMagnitude > 1e-4f;
                if (avoiding) avoidDirection.Normalize();
                NavMeshHit hit;
                Vector3 fallback = Vector3.zero;
                for (int i = 0; i < SteerAngles.Length; i++)
                {
                    Vector3 d = Quaternion.Euler(0f, SteerAngles[i], 0f) * want;
                    if (NavMesh.Raycast(pos, pos + d * 1.6f, out hit, NavMesh.AllAreas)) continue;
                    if (avoiding && Vector3.Dot(d, avoidDirection) > 0.5f) { if (fallback == Vector3.zero) fallback = d; continue; }   // open, but into the crowd
                    return d;
                }
                return fallback != Vector3.zero ? fallback : want;
            }

            private static string V(Vector3 v) { return QaArgs.F(v.x, 1) + "," + QaArgs.F(v.y, 1) + "," + QaArgs.F(v.z, 1); }

            private static bool HasLineOfSight(Vector3 from, Vector3 to, float planarDistance)
            {
                Vector3 d = to - from;
                float len = d.magnitude;
                if (len < 0.01f)
                {
                    return true;
                }

                // stop a body radius short of the target so its own capsule never counts as a blocker
                float reach = Mathf.Max(0.1f, len - 0.45f);
                return !Physics.Raycast(from, d / len, reach, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore);
            }

            // ------------------------------------------------------------------ patrol

            private void BuildWaypoints()
            {
                _waypoints.Clear();
                IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 p = points[i].NavPosition;
                    if (p == Vector3.zero) p = points[i].WorldPosition;
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(p, out hit, 3f, NavMesh.AllAreas))
                    {
                        _waypoints.Add(hit.position);
                    }
                }

                _arenaCentre = Vector3.zero;
                for (int i = 0; i < _waypoints.Count; i++) _arenaCentre += _waypoints[i];
                if (_waypoints.Count > 0) _arenaCentre /= _waypoints.Count;
                NavMeshHit centreHit;
                if (NavMesh.SamplePosition(_arenaCentre, out centreHit, 6f, NavMesh.AllAreas)) _arenaCentre = centreHit.position;

                // sparse worlds: sample around the player so patrol still has somewhere to go
                PlayerRig rig = PlayerRegistry.Primary;
                Vector3 origin = rig != null ? rig.transform.position : Vector3.zero;
                for (int i = 0; _waypoints.Count < 12 && i < 64; i++)
                {
                    float ang = (float)_random.NextDouble() * Mathf.PI * 2f;
                    float rad = 6f + (float)_random.NextDouble() * 18f;
                    Vector3 c = origin + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                    NavMeshHit hit;
                    if (NavMesh.SamplePosition(c, out hit, 4f, NavMesh.AllAreas))
                    {
                        _waypoints.Add(hit.position);
                    }
                }
            }

            private void PickNewGoal()
            {
                _hasGoal = false;
                if (_waypoints.Count == 0)
                {
                    return;
                }

                PlayerRig rig = PlayerRegistry.Primary;
                Vector3 pos = rig != null ? rig.transform.position : Vector3.zero;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 c = _waypoints[_random.Next(_waypoints.Count)];
                    if ((c - pos).sqrMagnitude > 16f)
                    {
                        _goal = c;
                        _hasGoal = true;
                        _lastPathTime = -999f;
                        return;
                    }
                }

                _goal = _waypoints[_random.Next(_waypoints.Count)];
                _hasGoal = true;
                _lastPathTime = -999f;
            }

            private void UpdateGoal(Vector3 pos)
            {
                if (!_hasGoal)
                {
                    PickNewGoal();
                    return;
                }

                Vector3 d = _goal - pos;
                d.y = 0f;
                if (d.magnitude < WaypointReachRadius)
                {
                    PickNewGoal();
                }
            }

            private bool NextWaypointDirection(Vector3 pos, out Vector3 dir)
            {
                dir = Vector3.zero;
                if (!_hasGoal)
                {
                    return false;
                }

                if (Time.unscaledTime - _lastPathTime > PathRefreshSeconds)
                {
                    _lastPathTime = Time.unscaledTime;
                    NavMeshHit start;
                    Vector3 from = NavMesh.SamplePosition(pos, out start, 2f, NavMesh.AllAreas) ? start.position : pos;
                    if (!NavMesh.CalculatePath(from, _goal, NavMesh.AllAreas, _path) || _path.status == NavMeshPathStatus.PathInvalid)
                    {
                        PickNewGoal();
                        return false;
                    }

                    _pathCorner = 1;
                }

                Vector3[] corners = _path.corners;
                if (corners == null || corners.Length == 0)
                {
                    return false;
                }

                while (_pathCorner < corners.Length - 1)
                {
                    Vector3 c = corners[_pathCorner] - pos;
                    c.y = 0f;
                    if (c.magnitude > 0.8f) break;
                    _pathCorner++;
                }

                int idx = Mathf.Clamp(_pathCorner, 0, corners.Length - 1);
                Vector3 to = corners[idx] - pos;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-4f)
                {
                    return false;
                }

                dir = to.normalized;
                return true;
            }

            // ------------------------------------------------------------------ finish

            private void Finish()
            {
                if (_finished)
                {
                    return;
                }

                _finished = true;
                _running = false;
                Unsubscribe();
                if (_pad != null)
                {
                    _pad.Neutral();
                    _pad.Dispose();
                    _pad = null;
                }

                _summary.totalSeconds = _runStart >= 0f ? Time.unscaledTime - _runStart : 0f;
                _summary.spawnSequenceHash = Fnv(_spawnTiers);
                _summary.outcomeHash = Fnv(OutcomeTokens());
                _summary.meanFrameMs = MeanFrame();
                _summary.allocFrames = _allocFrames;
                _summary.allocFramesOver4K = _allocFramesOver4K;
                _summary.allocRecorderAvgB = _allocFrames > 0 ? (float)(_allocRecorderSum / _allocFrames) : -1f;
                _summary.allocHeapDeltaAvgB = _allocFrames > 0 ? (float)(_allocHeapSum / _allocFrames) : -1f;
                _summary.allocRecorderMaxB = _allocRecorderMax;
                _summary.allocHeapDeltaMaxB = _allocHeapMax;
                if (_gcRecorder.Valid) _gcRecorder.Dispose();
                if (_targetFrameRateBefore != int.MinValue) Application.targetFrameRate = _targetFrameRateBefore;
                if (string.IsNullOrEmpty(_summary.allocMethod)) _summary.allocMethod = "no reading (recorder unavailable or run too short)";
                _summary.meanAimErrorDeg = _aimErrorSamples > 0 ? (float)(_aimErrorSum / _aimErrorSamples) : -1f;
                _summary.aimKind = "stick " + _aimKindStick + " / pointer " + _aimKindPointer;
                var top = new List<KeyValuePair<string, int>>(_fireHits);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 6; i++) sb.Append(i > 0 ? " | " : "").Append(top[i].Key).Append('=').Append(top[i].Value);
                _summary.fireLineHits = sb.ToString();
                // C-00 decision 2026-09-17 (M3 closure, user informed): the gate is "round >= 4 completed with no death before it"
                // — the bot's skill past round 4 is not the product; later rounds still run for perf/readability coverage
                // and are reported. The full requested count remains the reported target.
                int gateRounds = Mathf.Min(GateRoundsMin, _rounds);
                bool gateOk = _summary.roundsCompleted >= gateRounds && (_deathRound <= 0 || _deathRound > gateRounds) && (_timeoutRound <= 0 || _timeoutRound > gateRounds);
                _summary.gatePass = gateOk;
                _summary.pass = gateOk && !_invulnerable && !_specialEvidence && !_deathEvidence;   // WP-052/055/059: an evidence run is never a QA pass
                _summary.note = string.IsNullOrEmpty(_fail) ? (_died ? "player died" : "ok") : _fail;
                if (_invulnerable) _summary.note += " [invulnerable evidence run: WP-052 tier frames; not a QA pass]";   // (WP-055: the marks used to be overwritten by the line above)
                if (_specialEvidence) _summary.note += " [special evidence run: WP-055 sw_* frames; not a QA pass]";
                if (_deathEvidence) _summary.note += " [death evidence run: WP-059 deathframe-*; not a QA pass]";
                _summary.swBeats = (_swBeats[0] + _swBeats[1] + _swBeats[2]) > 0 ? "sw_push=" + _swBeats[0] + " sw_web=" + _swBeats[1] + " sw_pull=" + _swBeats[2] : "";

                try
                {
                    Directory.CreateDirectory(_outDir);
                    File.WriteAllText(Path.Combine(_outDir, "bot-summary.json"), JsonUtility.ToJson(_summary, true));
                    File.WriteAllText(Path.Combine(_outDir, "events.txt"), _events.ToString());
                    File.WriteAllText(Path.Combine(_outDir, "summary.txt"), SummaryLines());
                    QaReport report = QaReport.Create("QaRunBot", _seed);
                    report.Add("bot.alloc.method", _summary.allocMethodValid, _summary.allocMethod, "both readings see the 64 KB bait",
                        "single arbiter for the closure: dev Player (not batchmode), targetFrameRate -1 during the reading (WP-052), ProfilerRecorder 'GC Allocated In Frame' + managed heap delta (GC.GetTotalMemory rising steps), positive control first (C-00 / WP-047)", "bot-summary.json");
                    report.Add("bot.alloc.perFrame", true, "recorder avg " + QaArgs.F(_summary.allocRecorderAvgB, 0) + " B / max " + _summary.allocRecorderMaxB + " B; heap-delta avg " + QaArgs.F(_summary.allocHeapDeltaAvgB, 0) + " B / max " + _summary.allocHeapDeltaMaxB + " B; frames > 4 KB " + _summary.allocFramesOver4K + "/" + _summary.allocFrames,
                        "report only (0-allocation rule is the hit-path test; whole-run figure here)", "bot run, all phases incl. upgrade screens", "bot-summary.json");
                    report.Add("bot.rounds", _summary.gatePass, _summary.roundsCompleted + "/" + _rounds + (_deathRound > 0 ? " (died r" + _deathRound + ")" : "") + (_timeoutRound > 0 ? " (timeout r" + _timeoutRound + ")" : ""), ">= " + GateRoundsMin + " rounds, no death/timeout before (C-00 2026-09-17); " + _rounds + " requested",
                        _summary.note + "; kills " + _summary.totalKills + ", deaths " + _summary.deaths + ", damage " + _summary.totalDamageEvents +
                        ", stuck " + _summary.stuckRecoveries + ", relocations " + _summary.relocations + ", wallEscapes " + _summary.wallEscapes + ", upgrades " + _summary.upgradesBought + " (" + _summary.goldSpent + " gold), reloads " + _summary.reloads, "bot-summary.json");
                    report.Add("bot.determinism", true, "spawn " + _summary.spawnSequenceHash + " outcome " + _summary.outcomeHash, "report only",
                        "compare across runs with the same seed; frame timing is not seeded", "bot-summary.json");
                    report.summary = "bot mean frame " + QaArgs.F(_summary.meanFrameMs) + " ms over " + _frameMs.Count + " frames";
                    report.Write(_outDir);
                }
                catch (Exception e)
                {
                    Debug.LogError("[QaRunBot] write failed: " + e);
                }

                Debug.Log("[QaRunBot] " + (_summary.pass ? "PASS" : "FAIL") + " rounds=" + _summary.roundsCompleted + "/" + _rounds +
                          " kills=" + _summary.totalKills + " deaths=" + _summary.deaths + " spawnHash=" + _summary.spawnSequenceHash + " aimErr=" + QaArgs.F(_summary.meanAimErrorDeg, 1) + "° aimKind=" + _summary.aimKind + " fireFrames=" + _summary.fireFrames + " fireLine[" + _summary.fireLineHits + "]" + (_summary.swActivations > 0 ? " special=" + _summary.swActivations + " " + _summary.swBeats : "") + " note=" + _summary.note);

                if (_bootFlow && !_externalQuit)
                {
                    StartCoroutine(QuitSoon());
                }
            }


            private float MeanFrame()
            {
                if (_frameMs.Count == 0) return 0f;
                double sum = 0d;
                for (int i = 0; i < _frameMs.Count; i++) sum += _frameMs[i];
                return (float)(sum / _frameMs.Count);
            }

            private List<int> OutcomeTokens()
            {
                var tokens = new List<int>(_summary.rounds.Count * 3);
                for (int i = 0; i < _summary.rounds.Count; i++)
                {
                    RoundRecord r = _summary.rounds[i];
                    tokens.Add(r.round);
                    tokens.Add(r.poolTotal);
                    tokens.Add(r.kills);
                    tokens.Add(r.completed ? 1 : 0);
                }

                return tokens;
            }

            private string SummaryLines()
            {
                var sb = new StringBuilder();
                sb.Append("QaRunBot ").Append(_summary.pass ? "PASS" : "FAIL").Append(" seed=").Append(_seed)
                  .Append(" rounds=").Append(_summary.roundsCompleted).Append('/').Append(_rounds)
                  .Append(" kills=").Append(_summary.totalKills).Append(" deaths=").Append(_summary.deaths)
                  .Append(" spawns=").Append(_summary.spawnCount).Append(" spawnHash=").Append(_summary.spawnSequenceHash)
                  .Append(" outcomeHash=").Append(_summary.outcomeHash).Append(" note=").Append(_summary.note).Append('\n');
                for (int i = 0; i < _summary.rounds.Count; i++)
                {
                    RoundRecord r = _summary.rounds[i];
                    sb.Append(r.completed ? "PASS " : "FAIL ").Append("round-").Append(r.round.ToString("D2"))
                      .Append(" | pool ").Append(r.poolTotal).Append(" kills ").Append(r.kills).Append(" damage ").Append(r.damageEvents)
                      .Append(" seconds ").Append(QaArgs.F(r.seconds, 1)).Append(" | ").Append(r.outcome).Append('\n');
                }

                return sb.ToString();
            }

            private IEnumerator QuitSoon()
            {
                yield return new WaitForSecondsRealtime(0.5f);
                // WP-059: let the queued death frames land (bounded), they are written at end of frame.
                float deadline = Time.unscaledTime + DeathFramesQuitTimeoutSeconds;
                while (_pendingFrames.Count > 0 && Time.unscaledTime < deadline)
                {
                    yield return null;
                }

                yield return null;
                Application.Quit(_summary.pass ? 0 : 3);
            }

            private void OnDestroy()
            {
                if (!_finished)
                {
                    Unsubscribe();
                    _pad?.Dispose();
                }
            }

            private void Log(string line)
            {
                _events.Append(Time.unscaledTime.ToString("F2", CultureInfo.InvariantCulture)).Append(' ').Append(line).Append('\n');
                Debug.Log("[QaRunBot] " + line);
            }

            private IEnumerator WaitForScene(string name, float timeout)
            {
                yield return WaitUntil(() => SceneManager.GetActiveScene().name == name, "scene '" + name + "' did not load", timeout);
            }

            private IEnumerator WaitUntil(Func<bool> condition, string what, float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - t0 > timeout)
                    {
                        _fail = what + " within " + QaArgs.F(timeout, 0) + " s";
                        Debug.LogError("[QaRunBot] " + _fail);
                        yield break;
                    }

                    yield return null;
                }
            }

            public static string Fnv(List<int> values)
            {
                const uint offset = 2166136261u;
                const uint prime = 16777619u;
                uint h = offset;
                for (int i = 0; i < values.Count; i++)
                {
                    unchecked
                    {
                        h ^= (uint)values[i];
                        h *= prime;
                    }
                }

                return h.ToString("x8");
            }
        }
    }
}
#endif
