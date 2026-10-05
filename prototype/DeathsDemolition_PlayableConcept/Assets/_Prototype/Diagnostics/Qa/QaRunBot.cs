#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    /// than on the kill timeline; both hashes are reported and the gate is exactly "N rounds completed".
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
        /// <summary>WP-064 / DEC-091: <c>-ddQaBotStability</c> — the player cannot die; the run is the soak STABILITY gate (all rounds, no timeout, logs/memory checked by tools/qa/soak_gate.py --stability), reported as <c>runMode=stability</c> with its own <c>stabilityPass</c>; <c>pass</c> (the real-bot result) stays false.</summary>
        public const string StabilityFlag = "-ddQaBotStability";
        /// <summary>WP-078 A3 (arsenal): round N plays classic weapon (N−1) mod 10 through WeaponQaHooks — the seven meta-tree
        /// weapons are not unlocked on a sandbox profile, so this is the only way a bot run fires all ten.</summary>
        public const string WeaponCycleFlag = "-ddQaBotWeaponCycle";
        /// <summary>WP-078 D3c (C-00 30.09 R1–R3): a separate route run — spawns held, the bot walks the fixed V6 evidence points
        /// (courtyard, horizon, corridor = R6 chūmon, kairo = K1 north end) by gamepad, captures one PNG per point into the given
        /// directory, records them in the summary and quits. Never a gameplay run; the A2 gameplay runs are unchanged.</summary>
        public const string RouteTourFlag = "-ddQaBotRouteTour";
        /// <summary>WP-044 K1: <c>-ddQaAllocScan N</c> — at round N (spawner on, crowd live) disable every Behaviour type in turn and sample 3 frames of GC (AllocationProbeTests pattern); writes alloc-scan.txt. The player is invulnerable during the scan; the run is a measurement, not a QA pass.</summary>
        public const string AllocScanFlag = "-ddQaAllocScan";
        /// <summary>WP-055: evidence only — the bot grants itself the Special Weapons in turn (Kagura → Ofuda → Tide) whenever a body is near, writes one frame per sw_* beat × tier; summary marked, never a QA pass.</summary>
        public const string SpecialFlag = "-ddQaBotSpecial";
        /// <summary>WP-059: evidence runs only — this many seconds after run-ready the bot takes a lethal hit from the nearest zombie's side, so the death beat (hurt → collapse → overlay → Results) is captured as deathframe-* PNGs (with -ddQaBotFrames); the summary is marked and never a QA pass.</summary>
        public const string DieAfterFlag = "-ddQaBotDieAfter";
        public const int DefaultRounds = 8;
        public const int DefaultSeed = 26;
        public const float DefaultRoundTimeout = 420f;   // WP-044: round 8 (pool ~80, tier bodies) needs > 300 s at bot pace

        public static bool ExactRunPassed(int completed, int requested, int deathRound, int timeoutRound)
        {
            return requested > 0 && completed == requested && deathRound <= 0 && timeoutRound <= 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            string outDir = QaArgs.Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            // WP-078 (C-00 §4.14 a, 25.09): a bot run never touches the player's own UI files — ui-settings / load-timing /
            // ui-hints go to <out>/ui-data (C1b bot-tp2 had rewritten the real load-timing.json). The override also drops
            // anything read before it, so every run starts from default settings.
            string uiRoot = Path.Combine(outDir, "ui-data");
            Directory.CreateDirectory(uiRoot);
            UiDataPaths.SetRootOverride(uiRoot);
            Debug.Log("[QaRunBot] ui data root override -> " + uiRoot);

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
            if (QaArgs.Arg(StabilityFlag) != null) d.ConfigureStability();   // WP-064 / DEC-091
            if (QaArgs.Arg(WeaponCycleFlag) != null) d.ConfigureWeaponCycle();   // WP-078 A3
            if (!string.IsNullOrEmpty(QaArgs.Arg(RouteTourFlag))) d.ConfigureRouteTour(QaArgs.Arg(RouteTourFlag));   // WP-078 D3c
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

        /// <summary>WP-078 D3c: one evidence frame of the route run (key courtyard / horizon / corridor / kairo).</summary>
        [Serializable]
        public sealed class RouteFrame
        {
            public string key;
            public string status;          // captured | blocked | notApplicable
            public string reason;          // why blocked / not applicable
            public string path;            // relative to the run's out dir: frames/route-<key>.png
            public string mode;            // CameraModeService.Current
            public float x, z, yaw, pitch; // stand point, requested view (rig: pitch + looks down)
            public float seconds;          // unscaled seconds after run-ready
            public bool roofAbove;         // corridor / kairo: an upward RaycastAll hit a CameraOccluderKind.Roof collider
            public long drawCalls = -1, batches = -1;   // E-14: Render recorders at the capture frame (-1 = unavailable)
        }

        [Serializable]
        public sealed class RoundRecord
        {
            public int round;
            public string weapon;             // WP-078 A3 arsenal: classic weapon the round was played with (-ddQaBotWeaponCycle), "" otherwise
            public int poolTotal;
            public int kills;
            public int damageEvents;
            public float seconds;
            public bool completed;
            public bool inputLost;            // R2 (C-00 01.10): the bot's pad was disabled or the pointer took the scheme in this round
            public string outcome;
            // WP-064 stability gate (DEC-091): memory at the end of the round, MB (-1 = unavailable)
            public float memMonoUsedMB, memMonoHeapMB, memTotalAllocatedMB, memTotalReservedMB, memRssMB;
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
            public int kiteFrames;             // WP-064B: frames steered by the threat-field escape planner
            public int edgeGuards;             // WP-064B: moves turned inward because the next metre left the NavMesh
            public int offMeshRecoveries;      // WP-064B: frames spent walking back onto the NavMesh
            public int kiteBlocked;            // WP-064B: planner directions closed because the body did not move
            public int vantageMoves;           // WP-064B: walks to a shooting angle on a hidden, stationary target
            public int pickupDashes;           // WP-064B: frames spent dashing to a Special Weapon pickup within 8 m
            public int islandEscapes;          // WP-064B: path refreshes with no NavMesh path (island) → straight walk to the goal
            public int orbitFrames;            // WP-064B: planner steps that followed the plaza loop around the centre
            public int loopFrames;             // WP-064B v3: planner steps following the training loop
            public int unstickSweeps;          // WP-064B: direction sweeps after two stall recoveries at the same spot
            public int safeSpotFrames;         // WP-064B: planner steps heading for the chosen safe open spot
            public int loopReversals;          // WP-064B v3: loop sense flips (crowd ahead 2.5× behind, 3 votes)
            public int telegraphDodges;        // WP-064B: telegraph starts within 5 m that raised a body's weight
            public int specialPickupsSought;   // WP-064B: Special Weapon pickups the bot walked to
            public int specialActiveFrames;    // WP-064B: frames with a Special Weapon in hand
            public bool gatePass;              // bot.rounds gate (round >= 4 + no death before), separate from the full-run pass
            public int upgradesBought;      // WP-044: cheapest affordable card per upgrade screen (DEC-048 one per round)
            public int goldSpent;
            public float meanFrameMs;
            public float meanAimErrorDeg;     // angle between the rig's aim direction and the bot's target direction while a target was aimed
            public string aimKind;            // PlayerAim.AimKind seen most (Stick expected)
            public int padDisabledFrames;     // R2 (C-00 01.10): frames the virtual pad was not enabled (its events dropped)
            public int pointerAfterPadFrames; // frames the pointer held the scheme once the pad had it (or 2 s in without it)
            public int unfocusedFrames;       // frames the Player window had no focus (report only: input ignores focus)
            public bool inputMeasurable = true;   // false: the bot's input did not reach the game — not a gameplay result, rerun
            public string cameraMode;         // WP-070: TopDown / ThirdPerson / FirstPerson (-ddCameraMode)
            public string qualityTier;        // WP-078 A5: QualityTiers.Current the run played at (Performans / Yuksek / Sinematik)
            public float worldHalfExtent;     // WP-078 A5: playable ring inner face the bot used (WorldBounds, V6 55; else the ±45 ring)
            public int fireFrames;
            public int lookFireGated;         // WP-078: TP/FP frames the trigger was held back until the centre ray was on the chest
            public string swBeats;           // WP-055: sw_push/sw_web/sw_pull beats seen (kind=count), "" when none
            public int swActivations;         // WP-055: Special Weapons the evidence bot granted itself (0 on a QA run)
            public string fireLineHits;       // what a ray along the rig's aim from the muzzle hits while firing (layer:name = frames), top 6
            // WP-064: structured terminal-death evidence. These fields preserve the
            // product/balance diagnosis even when RunEnded is dispatched before AnyPlayerDied.
            public int deathRound;
            public float deathTimeSeconds;
            public string deathKilledBy;
            public float deathPositionX, deathPositionY, deathPositionZ;
            public float deathOverkill;
            public string fatalSource;
            public float fatalAppliedAmount, fatalHealthBefore, fatalHealthAfter;
            public bool fatalOnNavMesh;
            public int fatalNearbyEnemies;
            public bool fatalRelocating, fatalForcedRelocation;
            public float fatalStuckSeconds;
            public int fatalRoundPool, fatalRoundKills, fatalSpawnCount;
            public int fatalStandard, fatalSpecial, fatalElite, fatalUltra;
            public bool artifactWriteFailed;
            public string runMode = "normal";   // normal | stability (DEC-091) | evidence (WP-052/055/059 frames)
            public bool stabilityPass;          // stability mode only: all requested rounds, no timeout, invulnerable by design
            public bool pass;
            public string note;
            public List<RoundRecord> rounds = new List<RoundRecord>();
            public List<RouteFrame> routeFrames = new List<RouteFrame>();   // WP-078 D3c route run only
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
            private bool _forcedRelocation;
            private float _relocateSince;
            private Collider _closeBlocker;
            private bool _lastMuzzleClear, _lastEyeClear;
            private float _lastTrace = -999f;
            // WP-064B escape planner state
            private const int PlannerDirections = 16;
            /// <summary>Playable ring inner face: WP-074 B1 <see cref="DD.Art.WorldBounds"/> (V6 ±55) when the world has one, else the
            /// WP-038M ±45 m ring (AP-005).</summary>
            private static float RingMargin
            {
                get { DD.Art.WorldBounds b = DD.Art.WorldBounds.Active; return b != null && b.HalfExtent > 1f ? b.HalfExtent : 45f; }
            }
            // Environment + CameraOccluder + Ignore Raycast (ring, pond rims). A property: PrototypeLayers calls NameToLayer,
            // which Unity forbids in a MonoBehaviour's (static) field initializer — soak 8 died on that before round 1.
            private static int BodyBlockMask => PrototypeLayers.AimBlockMask | (1 << 2);
            private const int MaxThreats = 64;
            private readonly Vector3[] _threatPos = new Vector3[MaxThreats];
            private readonly float[] _threatWeight = new float[MaxThreats];
            private readonly Transform[] _telegraphBody = new Transform[16];
            private readonly float[] _telegraphTime = new float[16];
            private int _telegraphNext;
            private Vector3 _planDir;
            private int _planIndex = -1;
            private readonly float[] _dirClosedUntil = new float[PlannerDirections];
            private readonly float[] _dirScore = new float[PlannerDirections];
            private float _planSwitchTime;
            private float _kiteStillSince = -1f;
            private Vector3 _kiteAnchor;
            private float _nextPlanTime;
            private float _nextPickupScan;
            private SpecialWeaponPickup _pickup;
            private float _noLosSince = -1f;
            private bool _fellLogged;
            private Vector3 _lastStallPos, _unstickAnchor;
            private int _sameSpotStalls;
            private float _unstickUntil, _unstickStart, _unstickPhase;
            private Vector3 _lastOnMesh;
            private bool _hasLastOnMesh;
            private float _lastReachCheck = -999f;
            private NavMeshPath _reachPath;   // created lazily (never in a field initializer)
            private Vector3 _lastVantage = new Vector3(9999f, 0f, 9999f);
            private float _vantageUntil;
            private float _lastKillTime;
            private int _orbitSign;
            private float _orbitKeepUntil;
            private const float OrbitRadius = 16f;
            private int _islandFails;
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
            private bool _finishQueued;
            private bool _artifactWriteFailed;
            private string _fail;
            private float _reloadCooldown;
            private double _aimErrorSum;
            private int _aimErrorSamples;
            private int _aimKindStick, _aimKindPointer;
            private int _padDisabledFrames, _pointerAfterPad, _unfocusedFrames;
            private bool _padSeen;
            private const float PadGraceSeconds = 2f;   // healthy runs show 2–3 pointer frames before the pad's first event lands
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
            public bool Running => _running;
            public bool ArtifactWriteFailed => _artifactWriteFailed;
            public int ExitCode => _artifactWriteFailed ? 4 : ((_stability ? _summary.stabilityPass : _summary.pass) ? 0 : 3);
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

            public void ConfigureStability() { _stability = true; _invulnerable = true; }
            public void ConfigureWeaponCycle() { _weaponCycle = true; }
            public void ConfigureRouteTour(string framesDir) { _routeDir = framesDir; }
            private string _routeDir;
            private int _routeStep = -1;
            private float _routeStepStart, _routeSettleSince = -1f, _routeCaptureAt = -1f, _routeTimeout, _routeTraceAt;
            private Vector3 _routeTracePos, _routeLastSteer, _routeLastGoal;   // the route-stall trace: where the last trace stood, what steering asked
            private bool _routeArrived;
            private float _routeYawError, _routePitchError;
            private string _routeCapturePath;
            private NavMeshPath _routePath;
            private Vector3 _routeTarget;
            private int _routeCorner;
            private ProfilerRecorder _drawCalls, _batches;
            private bool _weaponCycle;
            private bool _stability;

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
                var id = (SpecialWeaponId)(_specialCycle++ % SpecialWeaponConfigSection.All.Length);   // WP-069 B4: two Specials (S-02)
                weapons.ActivateSpecialWeapon(id);
                _summary.swActivations++;
                _nextSpecialAt = Time.unscaledTime + SpecialGapSeconds;
                Log("special-evidence " + id + " granted (WP-055; not a QA pass)");
            }

            private IEnumerator Start()
            {
                _path = new NavMeshPath();
                try
                {
                    Directory.CreateDirectory(_outDir);
                }
                catch (Exception e)
                {
                    _artifactWriteFailed = true;
                    _fail = "artifact directory unavailable: " + e.GetType().Name;
                    Debug.LogError("[QaRunBot] " + _fail + ": " + e.Message);
                    Finish();
                    yield break;
                }
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
                BuildLoop();
                BuildSafeSpots();
                _runStart = Time.unscaledTime;
                BeginRoundRecord(RunDirector.Instance.Round);
                Log("run-ready round=" + RunDirector.Instance.Round + " waypoints=" + _waypoints.Count + " pad=" + _pad.IsAlive +
                    " padEnabled=" + _pad.PadEnabled + " nativeSuspended=" + _pad.SuspendedNative + " background=" + UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior);
                _running = true;
            }

            private void Subscribe()
            {
                Application.quitting += OnApplicationQuitting;
                RunEvents.RoundStarted += OnRoundStarted;
                EnemyCombatEvents.Raised += OnEnemyCombatEvent;
                RunEvents.RoundCompleted += OnRoundCompleted;
                RunEvents.RunEnded += OnRunEnded;
                PlayerHealth.AnyDamaged += OnDamaged;
                PlayerHealth.AnyPlayerDied += OnDied;
                if (EnemySpawnDirector.Instance != null)
                {
                    EnemySpawnDirector.Instance.EnemySpawned += OnEnemySpawned;
                    // WP-076 (R1 28.09, kill 61 > spawn 59): the bot arms after the round is Fighting — its first refill tick has
                    // already spawned; count those bodies (tiers from the live list, in spawn order) so spawns match the pool.
                    int already = EnemySpawnDirector.Instance.Population.SpawnedTotal;
                    IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                    for (int i = 0; i < live.Count && i < already; i++) _spawnTiers.Add((int)live[i].Tier);
                    _summary.spawnCount += already;
                    if (already > 0) Log("spawns-before-arm " + already);
                }
                EnemyCombatEvents.Raised += OnCombatBeat;   // WP-052
            }

            private void Unsubscribe()
            {
                EnemyCombatEvents.Raised -= OnCombatBeat;   // WP-052
                Application.quitting -= OnApplicationQuitting;
                RunEvents.RoundStarted -= OnRoundStarted;
                EnemyCombatEvents.Raised -= OnEnemyCombatEvent;
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
                    StartCoroutine(CloseRunThenFinish());
                }
            }

            /// <summary>
            /// WP-076 (R1 28.09: no [Progress] line in bot runs): the bot used to stop after its last round with the run still open, so
            /// the profile commit (ProfileRunCommitter on RunEnded, which logs [Progress]) never ran. Against a redirected profile only
            /// (-ddProfilePath sandbox, test isolation) the run is closed the way F1 "End run" closes it — one frame after the
            /// RoundCompleted dispatch; the real profile is never written by the bot.
            /// </summary>
            private IEnumerator CloseRunThenFinish()
            {
                yield return null;
                RunDirector run = RunDirector.Instance;
                if (!_finished && run != null && run.Phase != RunPhase.Dead && DD.Prototype.Profile.ProfileStore.HasPathOverride)
                {
                    Log("run-close EndRun(Debug) → profile commit (sandbox " + DD.Prototype.Profile.ProfileStore.Path + ")");
                    run.EndRun(RunEndReason.Debug, "qa-bot");
                }
                if (!_finished) Finish();
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
                    if (s.EndReason == RunEndReason.PlayerDeath)
                    {
                        // RunDirector observes PlayerHealth before this QA listener on some
                        // builds. Defer persistence one frame so OnDied can record killer,
                        // position and overkill instead of writing a truncated death.
                        if (!_finishQueued)
                        {
                            _finishQueued = true;
                            StartCoroutine(FinishAfterDeathDispatch());
                        }
                    }
                    else
                    {
                        Finish();
                    }
                }
            }

            private IEnumerator FinishAfterDeathDispatch()
            {
                yield return null;
                if (!_finished) Finish();
            }

            private void OnDamaged(PlayerDamageInfo info)
            {
                _lastDamageTime = Time.unscaledTime;
                PlayerRig rig = PlayerRegistry.Primary;
                if (_current != null) _current.damageEvents++;
                _summary.totalDamageEvents++;
                if (_summary.totalDamageEvents <= 5 || info.HealthAfter <= 0f)
                {
                    // WP-044 / WP-047E finding (instant PlayerDeath at run start): where, how much, from which direction
                    Log("damage #" + _summary.totalDamageEvents + " t+" + QaArgs.F(_runStart >= 0f ? Time.unscaledTime - _runStart : -1f, 2) + "s source=" + (info.SourceId ?? "<null>") + " amount=" + QaArgs.F(info.AppliedAmount, 1) +
                        " hp " + QaArgs.F(info.HealthBefore, 1) + "→" + QaArgs.F(info.HealthAfter, 1) + "/" + QaArgs.F(info.MaxHealth, 0) + " at=" + V(info.WorldPoint) + " dir=" + V(info.HitDirection) +
                        " player=" + (rig != null ? V(rig.transform.position) : "n/a") + " onNavMesh=" + (rig != null && NavMesh.SamplePosition(rig.transform.position, out NavMeshHit nh, 0.5f, NavMesh.AllAreas) ? "1" : "0"));
                }
                if (info.HealthAfter <= 0f)
                {
                    CaptureFatalContext(info, rig);
                }
            }

            private void OnDied(PlayerDeathInfo info)
            {
                if (!_died) _summary.deaths++;
                _died = true;
                int round = _current != null ? _current.round : 0;
                if (_deathRound <= 0) _deathRound = round;
                _summary.deathRound = round;
                _summary.deathTimeSeconds = _runStart >= 0f ? Time.unscaledTime - _runStart : -1f;
                _summary.deathKilledBy = info.KilledBy;
                _summary.deathPositionX = info.Position.x;
                _summary.deathPositionY = info.Position.y;
                _summary.deathPositionZ = info.Position.z;
                _summary.deathOverkill = info.Overkill;
                Log("player-died round=" + round + " t+" + QaArgs.F(_runStart >= 0f ? Time.unscaledTime - _runStart : -1f, 2) + "s killedBy=" + info.KilledBy + " pos=" + V(info.Position) + " overkill=" + QaArgs.F(info.Overkill, 1) + " kills=" + (_current != null ? _current.kills : 0));
                // WP-059: the death beat on three frames — the hit, the slowed collapse, the overlay before Results
                // (unscaled delays; Standard tier = no proximity wait). Flushed after Finish, QuitSoon waits for them.
                QueueFrame("deathframe-r" + round + "-impact", DeathFrameImpactSeconds);
                QueueFrame("deathframe-r" + round + "-collapse", DeathFrameCollapseSeconds);
                QueueFrame("deathframe-r" + round + "-overlay", DeathFrameOverlaySeconds);
            }

            private void CaptureFatalContext(PlayerDamageInfo info, PlayerRig rig)
            {
                _summary.fatalSource = info.SourceId;
                _summary.fatalAppliedAmount = info.AppliedAmount;
                _summary.fatalHealthBefore = info.HealthBefore;
                _summary.fatalHealthAfter = info.HealthAfter;
                _summary.fatalRelocating = _relocating;
                _summary.fatalForcedRelocation = _forcedRelocation;
                _summary.fatalStuckSeconds = _stuckTimer;
                _summary.fatalRoundPool = RunDirector.Instance != null ? RunDirector.Instance.RoundPoolTotal : 0;
                _summary.fatalRoundKills = RunDirector.Instance != null ? RunDirector.Instance.RoundKills : 0;
                _summary.fatalSpawnCount = _summary.spawnCount;
                _summary.fatalOnNavMesh = rig != null && NavMesh.SamplePosition(rig.transform.position, out NavMeshHit _, 0.5f, NavMesh.AllAreas);

                Vector3 player = rig != null ? rig.transform.position : info.WorldPoint;
                int nearby = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive) continue;
                    if ((z.transform.position - player).sqrMagnitude <= 25f) nearby++;
                    switch (z.Tier)
                    {
                        case EnemyTier.Standard: _summary.fatalStandard++; break;
                        case EnemyTier.Special: _summary.fatalSpecial++; break;
                        case EnemyTier.Elite: _summary.fatalElite++; break;
                        case EnemyTier.Ultra: _summary.fatalUltra++; break;
                    }
                }
                _summary.fatalNearbyEnemies = nearby;
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
                _lastKillTime = Time.unscaledTime;   // kill-progress timer starts with the round
                _current = new RoundRecord { round = round, poolTotal = RunDirector.Instance != null ? RunDirector.Instance.RoundPoolTotal : 0 };
                _roundStart = Time.unscaledTime;
                _summary.rounds.Add(_current);
                if (_weaponCycle) _current.weapon = CycleWeapon(round);
                PickNewGoal();
            }

            /// <summary>WP-078: equip classic weapon (round−1) mod 10, skipping disabled ones (rl_1 until enabled); "" if none.</summary>
            private string CycleWeapon(int round)
            {
                const int Count = 10;
                for (int step = 0; step < Count; step++)
                {
                    var id = (ClassicWeaponId)(((round - 1) + step) % Count);
                    if (WeaponQaHooks.TrySetClassicWeapon(id))
                    {
                        Log("weapon round=" + round + " id=" + id + (step > 0 ? " (skipped " + step + " disabled)" : ""));
                        return id.ToString();
                    }
                }

                Log("weapon round=" + round + " none enabled");
                return "";
            }

            private void CloseRoundRecord(bool completed, string outcome)
            {
                if (_current == null) return;
                _current.completed = completed;
                _current.outcome = outcome;
                _current.seconds = Time.unscaledTime - _roundStart;
                _current.kills = RunDirector.Instance != null ? RunDirector.Instance.RoundKills : _current.kills;
                _summary.totalKills += _current.kills;
                SampleMemory(_current);
            }

            private static void SampleMemory(RoundRecord r)
            {
                const float MB = 1f / (1024f * 1024f);
                r.memMonoUsedMB = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() * MB;
                r.memMonoHeapMB = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() * MB;
                r.memTotalAllocatedMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() * MB;
                r.memTotalReservedMB = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() * MB;
                long rss = 0;
                try { rss = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64; } catch (Exception) { rss = 0; }
                r.memRssMB = rss > 0 ? rss * MB : -1f;
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
                if (_routeDir != null)
                {
                    TickRouteTour(rig);         // WP-078 D3c: the route run owns every frame; no fighting, no rounds
                    return;
                }

                TickInputHealth(rig);   // R2 (C-00 01.10)

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
                                // WP-064B: a simple player policy — damage and survival first, speed only up to two tiers —
                                // and as many cards as the round allows (DEC-087: PurchaseLimit, shipped 2)
                                int bought = 0;
                                for (int guard = 0; guard < 4 && !shop.IsLocked && shop.PurchasesThisRound < shop.PurchaseLimit; guard++)
                                {
                                    int pick = -1, pickRank = int.MaxValue, pickCost = 0;
                                    for (int c = 0; c < shop.Cards.Count; c++)
                                    {
                                        if (!shop.CanAfford(c)) continue;
                                        UpgradeCardOffer card = shop.Cards[c];
                                        int rank = UpgradeRank(card.Branch, card.OwnedTier);
                                        if (rank < pickRank || (rank == pickRank && card.Cost > pickCost)) { pickRank = rank; pick = c; pickCost = card.Cost; }
                                    }

                                    if (pick < 0) break;
                                    string name = shop.Cards[pick].DisplayName;
                                    if (!shop.TryPurchase(pick)) break;
                                    bought++;
                                    _summary.upgradesBought++;
                                    _summary.goldSpent += pickCost;
                                    Log("upgrade-bought round=" + run.Round + " card=" + name + " cost=" + pickCost + " goldLeft=" + shop.Gold + " (" + shop.PurchasesThisRound + "/" + shop.PurchaseLimit + ")");
                                }

                                if (bought == 0)
                                {
                                    Log("upgrade-none round=" + run.Round + " gold=" + shop.Gold);
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
                    if (run.RoundKills != _current.kills) _lastKillTime = Time.unscaledTime;
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
                // WP-064B soak 2: the nearest body was often still RISING under the ground plate (DEC-081) — the fire line hit
                // the ground collider, the bot held fire and chased it for minutes. Target = the nearest non-rising body with
                // a clear muzzle line inside fire range, else the nearest non-rising body.
                ZombieEnemy nearestAny = target;
                target = SelectTarget(pos, rig, target);
                float nearestAnyDist = 999f;
                if (nearestAny != null) { Vector3 nd = nearestAny.transform.position - pos; nd.y = 0f; nearestAnyDist = nd.magnitude; }
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
                    if (o.TargetLineOfSight) { _noLosSince = -1f; _vantageUntil = 0f; } else if (_noLosSince < 0f) _noLosSince = Time.unscaledTime;
                    if (!muzzleClear && o.TargetDistance <= QaBotBrain.FireRange)
                    {
                        if (_blockedSince < 0f) _blockedSince = Time.unscaledTime;
                        if (Time.unscaledTime - _blockedSince > 2.5f && !_relocating)
                        {
                            _relocating = true;   // walls/planters keep eating the shots: walk to the open centre of the arena
                            _forcedRelocation = false;
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
                        if (_relocating && ((!_forcedRelocation && muzzleClear) || (pos - _goal).sqrMagnitude < 9f || Time.unscaledTime - _relocateSince > 8f))
                        {
                            _relocating = false;
                            _forcedRelocation = false;
                        }
                    }
                }

                if (_relocating && (target == null || Time.unscaledTime - _relocateSince > 8f))
                {
                    _relocating = false;
                    _forcedRelocation = false;
                }

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
                o.WeaponAmmoRatio = classic != null && classic.MagazineSize > 0 ? (float)classic.Ammo / classic.MagazineSize : 1f;
                // WP-055 evidence mode / WP-064B: a Special in hand has no magazine — the trigger must not wait for a classic
                // reload (the classic cannot reload while suspended; back-to-back grants would otherwise starve every shot)
                if (wc != null && wc.Special.IsActive)
                {
                    o.WeaponCanFire = true;
                    o.WeaponEmpty = false;
                    o.WeaponNeedsReload = false;
                    o.WeaponAmmoRatio = 1f;
                    _summary.specialActiveFrames++;
                }
                o.NearestThreatDistance = nearestAnyDist;   // rising bodies count: they will swing soon

                // WP-064B: a Special Weapon pickup on the ground is worth a detour when nothing is close
                if (Time.unscaledTime >= _nextPickupScan)
                {
                    _nextPickupScan = Time.unscaledTime + 0.5f;
                    _pickup = NearestPickup(pos, 35f);
                }
                if (_pickup != null && !_pickup.isActiveAndEnabled) _pickup = null;

                bool lowHealth = o.HealthRatio < 0.5f;
                bool threatened = nearestAnyDist < (lowHealth ? QaBotBrain.ThreatRadiusLowHealth : QaBotBrain.ThreatRadius) || nearby >= 3;
                if (_pickup != null && !threatened && !_relocating)
                {
                    Vector3 pg = _pickup.transform.position;
                    if (!_hunting || (pg - _goal).sqrMagnitude > 1f)
                    {
                        _goal = pg;
                        _hasGoal = true;
                        _hunting = true;
                        _lastPathTime = -999f;
                        _summary.specialPickupsSought++;
                        Log("special-pickup goal=" + V(pg) + " weapon=" + _pickup.WeaponId);
                    }
                }
                // hunt: a target beyond the engage radius — or one hidden behind a wall for over 2 s (WP-064B soak 1:
                // the bot pushed into a wall toward a body 11 m away for 300 s) — becomes the NavMesh goal (the last
                // body of a round is usually far away); otherwise patrol between waypoints
                else if (target != null && (o.TargetDistance > QaBotBrain.EngageRadius || (!o.TargetLineOfSight && !threatened && _noLosSince >= 0f && Time.unscaledTime - _noLosSince > 2f)
                                            || (!threatened && Time.unscaledTime - _lastKillTime > 15f)))   // stab26: a flickering line held the bot 17 m off the last bodies for 120 s
                {
                    if (Time.unscaledTime >= _vantageUntil && (!_hunting || (o.TargetPosition - _goal).sqrMagnitude > 4f))
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

                QaBotCommand cmd = _brain.Decide(o);
                // WP-064B: under threat the escape planner owns movement (aim / fire / reload stay with the brain). A forced
                // stall recovery keeps its first 1.25 s side-step; after that the planner decides even while relocating.
                bool plannerOwns = threatened && !(_forcedRelocation && Time.unscaledTime - _relocateSince < 1.25f);
                if (plannerOwns)
                {
                    Vector3 esc = PlanEscape(pos, lowHealth, _pickup != null ? _pickup.transform.position - pos : Vector3.zero);
                    if (esc.sqrMagnitude < 1e-4f) { esc = _arenaCentre - pos; esc.y = 0f; esc = esc.sqrMagnitude > 1e-4f ? esc.normalized : Vector3.zero; }
                    // the chosen direction does not move the body (an invisible fence / pinned by bodies): close it
                    // net progress, not instantaneous speed: soak 22 dithered ±1 m between two directions for 8 s
                    if ((pos - _kiteAnchor).sqrMagnitude > 1.0f) { _kiteAnchor = pos; _kiteStillSince = Time.unscaledTime; }
                    else if (_kiteStillSince >= 0f && Time.unscaledTime - _kiteStillSince > 1.2f && _planIndex >= 0)
                    {
                        // soak 22: the body stood still 8 s at a low obstacle the capsule test misses (a curb above the 0.35 m
                        // step); close the chosen direction and its two neighbours for 3 s, and remember several at once
                        for (int n = -1; n <= 1; n++) _dirClosedUntil[(_planIndex + n + PlannerDirections) % PlannerDirections] = Time.unscaledTime + 3f;
                        _nextPlanTime = 0f; _kiteStillSince = Time.unscaledTime;
                        _summary.kiteBlocked++;
                    }
                    if (esc.sqrMagnitude > 1e-4f)
                    {
                        cmd.Move = QaBotBrain.WorldToStick(esc, fwd, right);
                        cmd.Mode = QaBotMode.Kite;
                        _summary.kiteFrames++;
                    }
                }
                // A stall recovery must actually follow the centre path. A close target keeps the
                // brain in Retreat/Engage, whose movement ignored the relocation waypoint and
                // left the bot pinned; preserve its aim/fire decision but replace that move.
                // WP-064B: a Special Weapon pickup within 8 m is grabbed unless a body is at arm's length — soak 3 turned back
                // from a Tide Jewel 5 m away four times because the crowd kept handing movement to the planner
                if (_pickup != null && nearestAnyDist > 2.5f)
                {
                    Vector3 toPick = _pickup.transform.position - pos; toPick.y = 0f;
                    if (toPick.sqrMagnitude < 64f && toPick.sqrMagnitude > 0.04f)
                    {
                        cmd.Move = QaBotBrain.WorldToStick(toPick.normalized, fwd, right);
                        plannerOwns = true;   // direct: the last metres, no steering rotations
                        _summary.pickupDashes++;
                    }
                }
                // WP-064B soak 1/5: the player body slipped off the NavMesh behind the north wall line (z ≈ 43–44); every
                // NavMesh query then failed and the bot stood still for minutes. Off the mesh → walk straight back onto it.
                NavMeshHit onMeshHit;
                bool offMesh = true;
                // the NavMesh is eroded by the agent radius: a body hugging a wall is routinely 0.3–0.6 m off it — soak 18 walked
                // "back onto the mesh" straight into the crowd. Off-mesh = more than 1.2 m from any mesh point.
                if (NavMesh.SamplePosition(pos, out onMeshHit, 2.5f, NavMesh.AllAreas)) { Vector3 dv = onMeshHit.position - pos; dv.y = 0f; offMesh = dv.sqrMagnitude > 1.44f; }
                if (!offMesh) { Vector3 dv2 = onMeshHit.position - pos; dv2.y = 0f; if (dv2.sqrMagnitude < 0.09f) { _lastOnMesh = onMeshHit.position; _hasLastOnMesh = true; } }
                if (offMesh && (_hasLastOnMesh || NavMesh.SamplePosition(pos, out onMeshHit, 8f, NavMesh.AllAreas)))
                {
                    // the way back is the way in: the last on-mesh point (the nearest mesh point can sit across a wall)
                    Vector3 back = (_hasLastOnMesh ? _lastOnMesh : onMeshHit.position) - pos; back.y = 0f;
                    if (back.sqrMagnitude > 1e-4f)
                    {
                        cmd.Move = QaBotBrain.WorldToStick(back.normalized, fwd, right);
                        plannerOwns = true;   // bypass steering / guards: they all assume a position on the mesh
                        _summary.offMeshRecoveries++;
                    }
                }
                if (target != null && _hunting && (!o.TargetLineOfSight || Time.unscaledTime - _lastKillTime > 15f) && Time.unscaledTime - _lastReachCheck > 10f)
                {
                    _lastReachCheck = Time.unscaledTime;
                    if (_reachPath == null) _reachPath = new NavMeshPath();
                    NavMeshHit rs, rt;
                    Vector3 rFrom = NavMesh.SamplePosition(pos, out rs, 2f, NavMesh.AllAreas) ? rs.position : pos;
                    Vector3 rTo = NavMesh.SamplePosition(o.TargetPosition, out rt, 2f, NavMesh.AllAreas) ? rt.position : o.TargetPosition;
                    bool ok = NavMesh.CalculatePath(rFrom, rTo, NavMesh.AllAreas, _reachPath);
                    Log("reach target=" + V(o.TargetPosition) + " status=" + (ok ? _reachPath.status.ToString() : "failed") + " corners=" + _reachPath.corners.Length +
                        " targetWaiting=" + target.IsWaiting + " targetOnMesh=" + (NavMesh.SamplePosition(o.TargetPosition, out NavMeshHit th, 0.6f, NavMesh.AllAreas) ? "1" : "0"));
                    // WP-064B soak 9–11: a body that stays hidden for 20 s is shot from another angle — sample NavMesh points on
                    // rings around it, keep the ones with a clear line at chest height and a complete path, walk to the nearest
                    if (((_noLosSince >= 0f && Time.unscaledTime - _noLosSince > 20f) || Time.unscaledTime - _lastKillTime > 25f) && FindVantage(pos, o.TargetPosition, out Vector3 vantage))
                    {
                        _goal = vantage; _hasGoal = true; _hunting = true; _lastPathTime = -999f; _vantageUntil = Time.unscaledTime + 15f;
                        _summary.vantageMoves++;
                        Log("vantage goal=" + V(vantage) + " for target=" + V(o.TargetPosition));
                    }
                }
                if (_hunting && !plannerOwns && !_relocating && o.HasWaypoint && cmd.Mode != QaBotMode.Patrol)
                {
                    cmd.Move = QaBotBrain.WorldToStick(o.WaypointDirection, fwd, right);   // path to the hidden / far target
                }
                if (_relocating && !plannerOwns)
                {
                    Vector3 recoveryDirection = o.HasWaypoint ? o.WaypointDirection : _goal - pos;
                    recoveryDirection.y = 0f;
                    if (_forcedRelocation && Time.unscaledTime - _relocateSince < 1.25f)
                    {
                        // A direct centre path can keep pushing into the same physical prop or
                        // boundary that caused the measured stall. One deterministic side-step
                        // clears that contact before following the unchanged centre path.
                        float turn = (_summary.stuckRecoveries & 1) == 0 ? 90f : -90f;
                        recoveryDirection = Quaternion.Euler(0f, turn, 0f) * recoveryDirection;
                    }
                    if (recoveryDirection.sqrMagnitude > 1e-4f)
                    {
                        cmd.Move = QaBotBrain.WorldToStick(recoveryDirection.normalized, fwd, right);
                    }
                }
                if (cmd.Move.sqrMagnitude > 0.01f && !plannerOwns)
                {
                    Vector3 wantMove = QaBotBrain.StickToWorld(cmd.Move, fwd, right);
                    Vector3 avoid = nearby >= 3 ? crowdDir : Vector3.zero;   // run3: a wall-avoiding rotation walked the bot into a 10-body crowd
                    NavMeshHit edge;
                    if (NavMesh.FindClosestEdge(pos, out edge, NavMesh.AllAreas) && edge.distance < 3.5f)
                    {
                        Vector3 inward = _arenaCentre - pos; inward.y = 0f;
                        if (inward.sqrMagnitude > 1f)
                        {
                            // At the map rim, crowd avoidance must not veto the only safe exit.
                            // The seed-61 soak died while repeatedly choosing "away from crowd"
                            // into the west boundary. Commit normal movement toward the arena,
                            // keeping a small part of the tactical input for obstacle steering.
                            wantMove = (inward.normalized * 0.85f + wantMove.normalized * 0.15f).normalized;
                            avoid = Vector3.zero;
                        }
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
                    Log("t pos=" + V(pos) + " time=" + QaArgs.F(Time.unscaledTime, 1) + " mode=" + cmd.Mode + " tgt=" + (target != null ? QaArgs.F(o.TargetDistance, 1) + "@" + V(o.TargetPosition) + (target.IsWaiting ? "/waiting" : "") + (target.IsRising ? "/rising" : "") : "-") + " los=" + (o.TargetLineOfSight ? 1 : 0) +
                        " mz=" + (_lastMuzzleClear ? 1 : 0) + " eye=" + (_lastEyeClear ? 1 : 0) + " blk=" + (_closeBlocker != null ? _closeBlocker.name + "@" + V(_closeBlocker.bounds.center) + "/" + V(_closeBlocker.bounds.size) : "-") +
                        " fire=" + (cmd.Fire ? 1 : 0) + " hp=" + QaArgs.F(rig.Health != null ? rig.Health.Normalized : -1f, 2) + " near=" + nearby + " kills=" + _summary.totalKills + "+" + (_current != null ? _current.kills : 0) +
                        (classic != null ? " ammo=" + classic.Ammo + "/" + classic.MagazineSize + "+" + (classic.ReserveInfinite ? "inf" : classic.ReserveAmmo.ToString()) + (classic.IsReloading ? "/reloading" : "") : ""));   // WP-078 C1: TP/FP rounds stalled with the trigger idle
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
                    // WP-078: in TP/FP the shot leaves down the camera's centre ray — measure from the camera to the target's
                    // chest (the point LookStickToward aims at); from the player's position the shoulder offset inflated it
                    float aimError;
                    if (CameraModeService.IsLookMode ? TryLookAimError(o, out aimError) : TryPlanarAimError(rig, o, pos, out aimError))
                    {
                        _aimErrorSum += aimError;
                        _aimErrorSamples++;
                    }

                    if (rig.Aim.AimKind == AimSourceKind.Stick) _aimKindStick++; else _aimKindPointer++;
                }

                // WP-078 C1: in TP/FP the shot leaves down the screen-centre ray; with the trigger held while the view is
                // still swinging the bot emptied the WP-069 reserve into walls (TP 390 shots / 11 kills) and the round
                // timed out. Hold the classic trigger until the centre ray points at the target's chest.
                if (cmd.Fire && CameraModeService.IsLookMode && o.HasTarget && !(wc != null && wc.Special.IsActive) && !LookRayOnTarget(o))
                {
                    cmd.Fire = false;
                    _summary.lookFireGated++;
                }

                if (cmd.Fire)
                {
                    _summary.fireFrames++;
                    if (rig.Aim != null && (_summary.fireFrames & 3) == 0) SampleFireLine(rig);
                }
                // WP-064B soak 25: round 12 froze 230 s on the raised south crossing (4.0, 0.3, -31.3) — the path is complete
                // but the body is wedged; repeated recoveries re-chose the same blocked direction. Sweep directions instead.
                if (_unstickUntil > Time.unscaledTime)
                {
                    int slot = (int)((Time.unscaledTime - _unstickStart) / 0.7f) % 8;
                    Vector3 sweep = Quaternion.Euler(0f, slot * 45f + _unstickPhase, 0f) * Vector3.forward;
                    cmd.Move = QaBotBrain.WorldToStick(sweep, fwd, right);
                    plannerOwns = true;
                    if ((pos - _unstickAnchor).sqrMagnitude > 2.25f) { _unstickUntil = 0f; Log("unstuck pos=" + V(pos) + " dir=" + (slot * 45f + _unstickPhase)); }
                }
                if (cmd.Move.sqrMagnitude > 0.01f && !plannerOwns)   // the planner already demanded 1.2 m of NavMesh ahead
                {
                    Vector3 step = QaBotBrain.StickToWorld(cmd.Move, fwd, right);
                    NavMeshHit edgeHit;
                    Vector3 ahead = pos + step * 1.0f;
                    bool onMesh = NavMesh.SamplePosition(ahead, out edgeHit, 1.0f, NavMesh.AllAreas);
                    if (onMesh) { Vector3 off = edgeHit.position - ahead; off.y = 0f; onMesh = off.sqrMagnitude < 0.36f; }   // planar, so steps/ramps don't trip it
                    if (!onMesh)
                    {
                        // off the walkable area one metre ahead: the world has no wall there (south plate edge) — turn inward
                        Vector3 inward = _arenaCentre - pos; inward.y = 0f;
                        cmd.Move = inward.sqrMagnitude > 1e-4f ? QaBotBrain.WorldToStick(inward.normalized, fwd, right) : Vector2.zero;
                        _summary.edgeGuards++;
                    }
                }
                if (pos.y < -5f && !_fellLogged)
                {
                    _fellLogged = true;
                    Log("fell-out-of-world pos=" + V(pos) + " (world boundary missing — game defect, not a bot pass)");
                }
                if (CameraModeService.IsLookMode)
                {
                    // WP-070 / WP-078 A-9: in TP/FP the right stick turns the view and the shot goes
                    // down the screen-centre ray, so the bot steers the camera through the SAME input
                    // path a player uses: the virtual gamepad's right stick, sized from the wanted
                    // yaw/pitch rate through the rig's stick curve and the adapter's dead zone.
                    cmd.Aim = LookStickToward(o, cmd.Aim, fwd, right);
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
                        BeginStallRecovery(pos, nearby, "patrol");
                    }
                    else if (_stuckTimer > 1.5f && cmd.Mode != QaBotMode.Patrol && !_relocating)
                    {
                        // run4: pinned against the world edge by a crowd (position frozen 15 s, hp → 0) — break out along the
                        // NavMesh toward the open centre; the relocation path keeps firing at anything in range
                        _stuckTimer = 0f;
                        _summary.stuckRecoveries++;
                        BeginStallRecovery(pos, nearby, "combat");
                    }
                }
            }

            /// <summary>WP-064B upgrade priority (lower = earlier): fire rate, piercing, health pool, regeneration, reload,
            /// movement (two tiers), then the rest. A simple human-like build, not an optimiser.</summary>
            private static int UpgradeRank(UpgradeBranch branch, int ownedTier)
            {
                switch (branch)
                {
                    case UpgradeBranch.MovementSpeed: return ownedTier < 3 ? 0 : 8;   // design review: speed margin is the budget
                    case UpgradeBranch.Piercing: return 1;                               // lines up the trailing train
                    case UpgradeBranch.FireRate: return 2;
                    case UpgradeBranch.MaximumHealth: return 3;
                    case UpgradeBranch.RegenerationRate: return 4;
                    case UpgradeBranch.RegenerationDelay: return 5;
                    case UpgradeBranch.ReloadSpeed: return 6;
                    case UpgradeBranch.Explosion: return 6;
                    case UpgradeBranch.Chain: return 7;
                    default: return 9;
                }
            }

            private void OnEnemyCombatEvent(EnemyCombatEventInfo info)
            {
                if (info.Kind != EnemyCombatEventKind.TelegraphStart || info.Body == null) return;
                _telegraphBody[_telegraphNext] = info.Body;
                _telegraphTime[_telegraphNext] = Time.unscaledTime;
                _telegraphNext = (_telegraphNext + 1) % _telegraphBody.Length;
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && (info.Body.position - rig.transform.position).sqrMagnitude < 25f)
                {
                    _summary.telegraphDodges++;
                    _nextPlanTime = 0f;   // re-plan this frame: a swing is coming
                }
            }

            private bool IsTelegraphing(Transform body)
            {
                for (int i = 0; i < _telegraphBody.Length; i++)
                {
                    if (_telegraphBody[i] == body && Time.unscaledTime - _telegraphTime[i] < 1.2f) return true;
                }
                return false;
            }

            /// <summary>
            /// WP-064B threat-field escape: 16 planar directions, each scored by <see cref="QaBotBrain.ScoreDirection"/> from
            /// the live crowd (telegraphing bodies ×3, Elite/Ultra ×1.5) and two NavMesh reads (free length, openness at the
            /// probe). Re-planned at 10 Hz or on a telegraph; the result is the move of the frame.
            /// </summary>
            private Vector3 PlanEscape(Vector3 pos, bool lowHealth, Vector3 attraction)
            {
                if (Time.unscaledTime < _nextPlanTime && _planDir.sqrMagnitude > 1e-4f) return _planDir;
                _nextPlanTime = Time.unscaledTime + 0.1f;

                int count = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count && count < MaxThreats; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive) continue;
                    Vector3 zp = z.transform.position;
                    Vector3 d = zp - pos; d.y = 0f;
                    if (d.sqrMagnitude > 18f * 18f) continue;
                    float w = z.Tier == EnemyTier.Ultra ? 3f : z.Tier == EnemyTier.Elite ? 1.5f : 1f;   // soak 26: one Ultra overhead = 44 HP; its TTC radius is the swing (weight ≥ 3 → 4.1 m)
                    if (IsTelegraphing(z.transform)) w *= 3f;
                    if (lowHealth) w *= 1.5f;
                    _threatPos[count] = zp;
                    _threatWeight[count] = w;
                    count++;
                }

                if (attraction.sqrMagnitude > 1e-4f) { attraction.y = 0f; attraction = attraction.sqrMagnitude > 1e-4f ? attraction.normalized : Vector3.zero; }
                // WP-064B soak 16/17: against a big crowd a greedy escape ends in a pocket or at the ring. A human "trains" the
                // horde around the open plaza: run a loop of radius ~16 m about the centre, tangent chosen away from the crowd.
                if (count >= 3 && attraction.sqrMagnitude < 1e-4f)
                {
                    // WP-064B v3 horde training: follow the loop inside the gate ring so the crowd strings out behind
                    // soak 24: a 14 m loop (the only closed ring the ponds/walls allow) lets the crowd cut the chord — the loop
                    // is used only when a ring of >= 22 m exists; otherwise the global safe-spot choice (soak 19/22: 8/12)
                    Vector3 tangent = _loopRadius >= 22f ? LoopTangent(pos, count) : Vector3.zero;
                    if (tangent.sqrMagnitude > 1e-4f) { attraction = tangent * 2.5f; _summary.loopFrames++; }
                    else
                    {
                        Vector3 toSafe = SafeSpotDirection(pos, count);
                        if (toSafe.sqrMagnitude > 1e-4f) { attraction = toSafe * 2.2f; _summary.safeSpotFrames++; }
                    }
                }
                if (count >= 6 && attraction.sqrMagnitude < 1e-4f)
                {
                    Vector3 c = _arenaCentre; Vector3 rel = pos - c; rel.y = 0f;
                    float rad = rel.magnitude;
                    if (rad > 1f)
                    {
                        Vector3 radial = rel / rad;
                        Vector3 tangent = Vector3.Cross(Vector3.up, radial);          // counter-clockwise
                        Vector3 crowd = Vector3.zero;
                        for (int i = 0; i < count; i++) { Vector3 d = _threatPos[i] - pos; d.y = 0f; crowd += d; }
                        if (Vector3.Dot(tangent, crowd) > 0f) tangent = -tangent;       // run the loop away from the pack
                        if (_orbitSign != 0 && Vector3.Dot(tangent, Vector3.Cross(Vector3.up, radial)) * _orbitSign < 0f && Time.unscaledTime < _orbitKeepUntil)
                            tangent = -tangent;                                          // keep the direction ~3 s (no dithering)
                        else { _orbitSign = Vector3.Dot(tangent, Vector3.Cross(Vector3.up, radial)) > 0f ? 1 : -1; _orbitKeepUntil = Time.unscaledTime + 3f; }
                        float radialCorrection = Mathf.Clamp((OrbitRadius - rad) / 8f, -1f, 1f);   // drift back onto the loop
                        attraction = (tangent + radial * radialCorrection).normalized * 1.8f;
                        _summary.orbitFrames++;
                    }
                }
                // WP-064B soak 19: the round-9 escape ran south into the gate the rest of the round was spawning from. While
                // bodies are still to come, spawn points within 20 m weigh as half a zombie each (a player avoids live gates).
                RunDirector rd = RunDirector.Instance;
                int pending = rd != null ? rd.RoundPoolTotal - rd.RoundKills - count : 0;
                if (pending > 0)
                {
                    IReadOnlyList<SpawnPointRuntime> sp = SpawnPointRegistry.Points;
                    for (int i = 0; i < sp.Count && count < MaxThreats; i++)
                    {
                        Vector3 sPos = sp[i].NavPosition == Vector3.zero ? sp[i].WorldPosition : sp[i].NavPosition;
                        Vector3 sd = sPos - pos; sd.y = 0f;
                        if (sd.sqrMagnitude > 20f * 20f) continue;
                        _threatPos[count] = sPos; _threatWeight[count] = 0.5f; count++;
                    }
                }
                NavMeshHit snap;
                if (NavMesh.SamplePosition(pos, out snap, 1.0f, NavMesh.AllAreas)) pos = snap.position;   // raycasts start on the mesh
                float best = -1e5f;
                Vector3 bestDir = Vector3.zero;
                int bestIndex = -1;
                NavMeshHit hit;
                for (int k = 0; k < PlannerDirections; k++)
                {
                    float ang = k * (360f / PlannerDirections);
                    Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                    float clear = NavMesh.Raycast(pos, pos + dir * 6f, out hit, NavMesh.AllAreas) ? hit.distance : 6f;
                    // WP-064B soak 7: the WP-038M ±45 m ring and the pond rims are physics-only walls (Ignore Raycast layer,
                    // outside the NavMesh bake) — the bot kited into the ring and was pinned. Body-height sphere cast.
                    RaycastHit phys;
                    if (Physics.SphereCast(pos + Vector3.up * 0.6f, 0.35f, dir, out phys, clear, BodyBlockMask, QueryTriggerInteraction.Ignore))
                        clear = Mathf.Min(clear, phys.distance);
                    // a cast that starts inside a collider (the body already touching a rim fence) reports nothing — soak 10
                    // froze against one for 8 s; test the first stride as a volume instead
                    if (Physics.CheckCapsule(pos + dir * 0.6f + Vector3.up * 0.7f, pos + dir * 0.6f + Vector3.up * 1.3f, 0.3f, BodyBlockMask, QueryTriggerInteraction.Ignore))
                        clear = Mathf.Min(clear, 0.5f);
                    // a direction that just failed to move the body is closed for 1.5 s
                    if (Time.unscaledTime < _dirClosedUntil[k]) clear = Mathf.Min(clear, 0.5f);
                    float openness = 0f;
                    if (clear >= 1.2f)
                    {
                        Vector3 probe = pos + dir * Mathf.Min(clear, 3.5f);
                        openness = NavMesh.FindClosestEdge(probe, out hit, NavMesh.AllAreas) ? hit.distance : 6f;
                        openness = Mathf.Min(openness, RingMargin - Mathf.Max(Mathf.Abs(probe.x), Mathf.Abs(probe.z)));
                        // WP-064B soak 16: a pocket (the strip under the north wall) looks open at 3.5 m but has one way out,
                        // which the crowd then closes. Count the long exits from the probe; with a crowd around, few exits = trap.
                        if (count >= 4)
                        {
                            int exits = 0;
                            for (int e = 0; e < 8; e++)
                            {
                                Vector3 ed = Quaternion.Euler(0f, e * 45f, 0f) * Vector3.forward;
                                if (!NavMesh.Raycast(probe, probe + ed * 7f, out hit, NavMesh.AllAreas) && Mathf.Max(Mathf.Abs(probe.x + ed.x * 7f), Mathf.Abs(probe.z + ed.z * 7f)) < RingMargin) exits++;
                            }
                            openness = Mathf.Min(openness, 6f) - (exits <= 2 ? 4f : exits <= 4 ? 1.5f : 0f);
                            // soak 19/20 died twice on the raised south crossing (y ≈ 0.3): bridges and decks are chokepoints
                            if (NavMesh.SamplePosition(probe, out hit, 1.0f, NavMesh.AllAreas) && hit.position.y > 0.2f) openness -= 3f;
                        }
                    }
                    float score = QaBotBrain.ScoreDirection(pos, dir, clear, openness, _threatPos, _threatWeight, count, _planDir, attraction, Vector3.zero, RingMargin);
                    _dirScore[k] = score;
                    if (score > best) { best = score; bestDir = dir; bestIndex = k; }
                }

                // latch (design review): keep the current direction for 0.6 s unless the new best is clearly better —
                // soak 22 dithered between two near-equal directions for 8 s
                if (_planIndex >= 0 && bestIndex >= 0 && bestIndex != _planIndex && Time.unscaledTime - _planSwitchTime < 0.6f
                    && _dirScore[_planIndex] > best - 1.0f && _dirScore[_planIndex] > -1e4f)
                {
                    bestIndex = _planIndex;
                    bestDir = Quaternion.Euler(0f, _planIndex * (360f / PlannerDirections), 0f) * Vector3.forward;
                }
                else if (bestIndex != _planIndex) _planSwitchTime = Time.unscaledTime;
                _planDir = bestDir;   // zero when every direction is walled: the caller walks back toward the mesh / centre
                _planIndex = bestIndex;
                return bestDir;
            }

            private static SpecialWeaponPickup NearestPickup(Vector3 from, float radius)
            {
                SpecialWeaponPickup best = null;
                float bestSq = radius * radius;
                SpecialWeaponPickup[] all = UnityEngine.Object.FindObjectsOfType<SpecialWeaponPickup>();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || !all[i].isActiveAndEnabled) continue;
                    Vector3 d = all[i].transform.position - from; d.y = 0f;
                    if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; best = all[i]; }
                }
                return best;
            }

            private void BeginStallRecovery(Vector3 pos, int nearby, string mode)
            {
                if ((pos - _lastStallPos).sqrMagnitude < 1f) _sameSpotStalls++; else _sameSpotStalls = 0;
                _lastStallPos = pos;
                if (_sameSpotStalls >= 2 && _unstickUntil <= Time.unscaledTime)
                {
                    _unstickStart = Time.unscaledTime; _unstickUntil = Time.unscaledTime + 5.6f; _unstickAnchor = pos;
                    _unstickPhase = (_unstickPhase + 22.5f) % 45f;
                    _summary.unstickSweeps++;
                    Log("unstick-sweep pos=" + V(pos) + " time=" + QaArgs.F(Time.unscaledTime, 1) + " (" + _sameSpotStalls + " recoveries at the same spot)");
                }
                _relocating = true;
                // A measured movement stall needs a committed path; otherwise a clear muzzle
                // line cancels the recovery on its next frame before any movement is applied.
                _forcedRelocation = true;
                _relocateSince = Time.unscaledTime;
                _goal = _arenaCentre;
                _hasGoal = true;
                _hunting = false;
                _lastPathTime = -999f;
                Log("stall-recovery mode=" + mode + " pos=" + V(pos) + " time=" + QaArgs.F(Time.unscaledTime, 1) + " near=" + nearby + " goal=" + V(_goal));   // WP-078 K6: time for tools/qa/stuck_clusters.py
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

            /// <summary>WP-078: seconds the bot wants to close its view error in (then capped by the stick speed).</summary>
            private const float LookCloseSeconds = 0.12f;

            /// <summary>
            /// WP-070 / WP-078 A-9: the right-stick deflection that turns the TP/FP view toward the
            /// target's chest (seen from the camera, the centre ray's origin), else along the brain's
            /// aim direction. Inverse of the rig's stick model: rate = |s|^exp · speed (pitch × share,
            /// invert-Y honoured), with the adapter's radial dead zone / saturation undone.
            /// </summary>
            // ---------------------------------------------------------------- WP-078 D3c route run

            private struct RoutePoint
            {
                public string Key; public float X, Z, Yaw, PitchTp, PitchFp; public bool Roof;
                public RoutePoint(string key, float x, float z, float yaw, float pitchTp, float pitchFp, bool roof)
                { Key = key; X = x; Z = z; Yaw = yaw; PitchTp = pitchTp; PitchFp = pitchFp; Roof = roof; }
            }

            /// <summary>C-00 30.09 R3 (V6): courtyard (0,−3) 0°, horizon (0,−3) 180°, corridor R6 chūmon (0,19), kairo K1 north end
            /// on the corridor's centre line (−33,15) 180° (the first pick −31 sat 0.6 m from the east wall and was never reached).
            /// Rig pitch + looks down; corridor/kairo look up under the roof. Each point is snapped to the NavMesh (≤ 2 m) at step start.</summary>
            private static readonly RoutePoint[] RoutePoints =
            {
                new RoutePoint("courtyard", 0f, -3f, 0f, 8f, 5f, false),
                new RoutePoint("horizon", 0f, -3f, 180f, -3f, -3f, false),
                new RoutePoint("corridor", 0f, 19f, 180f, -18f, -25f, true),
                new RoutePoint("kairo", -33f, 15f, 180f, -12f, -15f, true),   // K1 centre line x −33 (walls −35.6 / −30.4; GreyboxV6Builder K1)
            };

            /// <summary>The flag's folder name, always under the run's out dir (e.g. -ddQaBotRouteTour &lt;out&gt;/frames → "frames").</summary>
            private string RouteFolder => Path.GetFileName(_routeDir.TrimEnd('/', '\\'));

            private const float RouteStartDelay = 3f;     // the ROUND 1 banner (2.4 s) is gone
            // WP-078 prova 30.09: the step budget follows the NavMesh path — kairo (~55 m of path from the corridor) took 31 s in
            // top-down and ran out of a flat 45 s in TP/FP. Budget = 20 s + path / 1.2 m/s, clamped to [45, 120] s.
            private const float RouteStepTimeoutMin = 45f, RouteStepTimeoutMax = 120f, RouteWalkSpeed = 1.2f, RouteTracePeriod = 2f;
            private const float RouteArrive = 0.8f;

            private void TickRouteTour(PlayerRig rig)
            {
                float now = Time.unscaledTime;
                if (_routeStep < 0)
                {
                    HoldSpawns();
                    if (now - _runStart < RouteStartDelay) { _pad.Set(Vector2.zero, Vector2.zero, false, false, false); _pad.Flush(); return; }
                    Directory.CreateDirectory(Path.Combine(_outDir, RouteFolder));   // frames live inside the run's out dir (bound by the runner)
                    try { _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"); _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count"); } catch (Exception) { }
                    _routePath = new NavMeshPath();
                    BeginRouteStep(0);
                }

                if (_routeStep >= RoutePoints.Length)
                {
                    return;
                }

                RoutePoint point = RoutePoints[_routeStep];
                bool topDown = CameraModeService.Current == CameraMode.TopDown;
                if (topDown && point.Key == "horizon")
                {
                    RecordRoute(point, "notApplicable", "topdown-fixed-camera: yaw fixed north, the top ray is ~28° below the horizon (E-3 by FP)", null, false);
                    BeginRouteStep(_routeStep + 1);
                    return;
                }

                // a capture is in flight: wait for the PNG on disk (ScreenCapture writes at the end of a frame)
                if (_routeCaptureAt >= 0f)
                {
                    _pad.Set(Vector2.zero, Vector2.zero, false, false, false); _pad.Flush();
                    string full = Path.Combine(_outDir, _routeCapturePath);
                    if (File.Exists(full) && new FileInfo(full).Length > 0)
                    {
                        RecordRoute(point, "captured", "", _routeCapturePath, RoofAbove(rig));
                        _routeCaptureAt = -1f;
                        BeginRouteStep(_routeStep + 1);
                    }
                    else if (now - _routeCaptureAt > 3f)
                    {
                        RecordRoute(point, "blocked", "frame not on disk 3 s after the capture", null, false);
                        _routeCaptureAt = -1f;
                        BeginRouteStep(_routeStep + 1);
                    }
                    return;
                }

                if (now - _routeStepStart > _routeTimeout)
                {
                    // WP-078 prova: say which half failed — walking (distance left) or the view (yaw/pitch error)
                    Vector3 at = rig.transform.position;
                    float left = new Vector2(_routeTarget.x - at.x, _routeTarget.z - at.z).magnitude;
                    RecordRoute(point, "blocked", (_routeArrived ? "view not settled" : "point not reached") + " in " + QaArgs.F(_routeTimeout, 0)
                        + " s (pos " + V(at) + ", " + QaArgs.F(left, 1) + " m left, yawErr " + QaArgs.F(_routeYawError, 1)
                        + "°, pitchErr " + QaArgs.F(_routePitchError, 1) + "°)", null, false);
                    BeginRouteStep(_routeStep + 1);
                    return;
                }

                Vector3 pos = rig.transform.position;
                Vector3 fwd, right;
                CameraBasis(out fwd, out right);
                Vector3 target = new Vector3(_routeTarget.x, pos.y, _routeTarget.z);
                Vector2 move = Vector2.zero, look = Vector2.zero;
                Vector3 flat = target - pos; flat.y = 0f;
                bool arrived = flat.magnitude <= RouteArrive;
                _routeArrived = arrived;
                if (now >= _routeTraceAt)
                {
                    _routeTraceAt = now + RouteTracePeriod;
                    Log("route-walk key=" + point.Key + " pos=" + V(pos) + " left=" + QaArgs.F(flat.magnitude, 1) + " time=" + QaArgs.F(now - _runStart, 1));
                    Vector3 since = pos - _routeTracePos; since.y = 0f;
                    if (!arrived && _routeTraceAt - RouteTracePeriod > _routeStepStart + RouteTracePeriod && since.magnitude < 0.1f) LogRouteStall(point.Key, rig, pos);
                    _routeTracePos = pos;
                }
                if (!arrived)
                {
                    float toCorner;
                    Vector3 onMesh;
                    Vector3 dir = RouteDirection(pos, target, out toCorner, out onMesh);
                    // The wall probe starts on the mesh and reaches no further than the corner being walked to, which RouteDirection
                    // keeps in straight sight: at the NW kairō door (corners 0.3–1 m apart, the player r 0.45 inside the agent r 0.5
                    // margin) a fixed 1.6 m probe left the mesh past the turn, every rotation failed, and the bot circled or stood
                    // (FINAL 23093940 A2 BLOCKED at −25, 28.7 in all three modes).
                    _routeLastSteer = SteerOnNavMesh(onMesh, dir, Vector3.zero, Mathf.Clamp(toCorner, 0.3f, SteerProbe));
                    // Standing on the mesh boundary every probe grazes the edge and reports a hit (route-stall trace: steer 0, input
                    // 0, touching only the K3 post and the floor). The corner is the next point of a path planned from here, on the
                    // mesh, so walk straight at it instead of standing; the fight planner keeps its stop (WP-064B soak 3).
                    if (_routeLastSteer == Vector3.zero) _routeLastSteer = dir;
                    _routeLastGoal = pos + dir * toCorner;
                    move = QaBotBrain.WorldToStick(_routeLastSteer, fwd, right) * (flat.magnitude < 2f ? 0.5f : 1f);
                    _routeSettleSince = -1f;
                }

                bool aligned = true;
                PrototypeCameraRig cam = PrototypeCameraRig.Active;
                if (!topDown && cam != null)
                {
                    float pitch = CameraModeService.Current == CameraMode.FirstPerson ? point.PitchFp : point.PitchTp;
                    Vector3 viewDir = Quaternion.Euler(pitch, point.Yaw, 0f) * Vector3.forward;
                    var o = new QaBotObservation { HasTarget = true, TargetPosition = cam.StablePosition + viewDir * 20f - Vector3.up * 1.0f };
                    look = LookStickToward(o, Vector2.zero, fwd, right);
                    _routeYawError = Mathf.Abs(Mathf.DeltaAngle(cam.ViewYaw, point.Yaw));
                    _routePitchError = Mathf.Abs(cam.ViewPitch - pitch);
                    aligned = _routeYawError <= 2f && _routePitchError <= 2f;
                }

                _pad.Set(arrived ? Vector2.zero : move, aligned ? Vector2.zero : look, false, false, false);
                _pad.Flush();
                if (!arrived || !aligned)
                {
                    _routeSettleSince = -1f;
                    return;
                }

                if (_routeSettleSince < 0f) _routeSettleSince = now;
                if (now - _routeSettleSince < (topDown && point.Roof ? 1.0f : 0.5f)) return;   // TP smoothing / top-down roof fade

                _routeCapturePath = RouteFolder + "/route-" + point.Key + ".png";
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, _routeCapturePath));
                _routeCaptureAt = now;
                Log("route-capture key=" + point.Key + " pos=" + V(pos) + " mode=" + CameraModeService.Current);
            }

            private void BeginRouteStep(int step)
            {
                _routeStep = step;
                _routeStepStart = Time.unscaledTime;
                _routeSettleSince = -1f;
                _routeCorner = -1;
                if (step < RoutePoints.Length)
                {
                    // the stand point on the NavMesh (≤ 2 m); a point with no NavMesh near it is recorded as blocked by the timeout
                    RoutePoint p = RoutePoints[step];
                    Vector3 raw = new Vector3(p.X, 0f, p.Z);
                    NavMeshHit hit;
                    _routeTarget = NavMesh.SamplePosition(raw + Vector3.up * 1f, out hit, 2f, NavMesh.AllAreas) ? hit.position : raw;
                    // step budget from the NavMesh path length (no path: straight line × 1.6 for the detours around walls)
                    PlayerRig rig = PlayerRegistry.Primary;
                    Vector3 from = rig != null ? rig.transform.position : _routeTarget;
                    float length = 1.6f * Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(_routeTarget.x, 0f, _routeTarget.z));
                    var path = new NavMeshPath();
                    if (rig != null && NavMesh.CalculatePath(from, _routeTarget, NavMesh.AllAreas, path) && path.corners.Length > 1)
                    {
                        length = 0f;
                        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    }
                    _routeTimeout = Mathf.Clamp(20f + length / RouteWalkSpeed, RouteStepTimeoutMin, RouteStepTimeoutMax);
                    _routeArrived = false; _routeYawError = _routePitchError = 0f; _routeTraceAt = 0f;
                    Log("route-step key=" + p.Key + " target=" + V(_routeTarget) + (hit.hit ? "" : " (no NavMesh within 2 m)")
                        + " path=" + QaArgs.F(length, 1) + "m budget=" + QaArgs.F(_routeTimeout, 0) + "s");
                }
                if (step >= RoutePoints.Length)
                {
                    Log("route-tour done frames=" + _summary.routeFrames.Count);
                    _running = false;
                    Finish();
                }
            }

            private void RecordRoute(RoutePoint point, string status, string reason, string path, bool roofAbove)
            {
                var f = new RouteFrame
                {
                    key = point.Key, status = status, reason = reason, path = path ?? "", mode = CameraModeService.Current.ToString(),
                    x = point.X, z = point.Z, yaw = point.Yaw,
                    pitch = CameraModeService.Current == CameraMode.FirstPerson ? point.PitchFp : point.PitchTp,
                    seconds = Time.unscaledTime - _runStart, roofAbove = roofAbove,
                    drawCalls = _drawCalls.Valid ? _drawCalls.LastValue : -1, batches = _batches.Valid ? _batches.LastValue : -1
                };
                _summary.routeFrames.Add(f);
                Log("[QaFrame] " + point.Key + " status=" + status + " drawCalls=" + f.drawCalls + " batches=" + f.batches + " roofAbove=" + (roofAbove ? 1 : 0) + (reason.Length > 0 ? " reason=" + reason : ""));
            }

            /// <summary>WP-078 R3: any collider straight above the head marked CameraOccluderKind.Roof (RaycastAll — the dressed kit
            /// has unmarked slabs and lintels under the roof occluder).</summary>
            private static bool RoofAbove(PlayerRig rig)
            {
                Vector3 head = rig.transform.position + Vector3.up * 1.7f;
                RaycastHit[] hits = Physics.RaycastAll(head, Vector3.up, 15f, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hits.Length; i++)
                {
                    if (CameraOccluderClass.Of(hits[i].collider) == CameraOccluderKind.Roof) return true;
                }

                return false;
            }

            /// <summary>
            /// R2 (C-00 01.10, FINAL dab326ab A2): did the bot's input reach the game this frame? A disabled pad, or the pointer
            /// holding the scheme after the pad had it (or 2 s into the run without it), marks the round's input lost; the run is
            /// then unmeasurable — the player stood still with a full magazine because nothing arrived, not because of the game.
            /// </summary>
            private void TickInputHealth(PlayerRig rig)
            {
                bool lost = false;
                if (!Application.isFocused) _unfocusedFrames++;
                if (_pad != null && !_pad.PadEnabled) { _padDisabledFrames++; lost = true; }
                if (rig.Aim != null)
                {
                    if (rig.Aim.AimKind == AimSourceKind.Stick) _padSeen = true;
                    else if (_padSeen || Time.unscaledTime - _runStart > PadGraceSeconds) { _pointerAfterPad++; lost = true; }
                }

                if (lost && _current != null && !_current.inputLost)
                {
                    _current.inputLost = true;
                    Log("input-lost round=" + _current.round + " padEnabled=" + (_pad != null && _pad.PadEnabled) + " aimKind=" + (rig.Aim != null ? rig.Aim.AimKind.ToString() : "-") +
                        " focused=" + Application.isFocused + " time=" + QaArgs.F(Time.unscaledTime - _runStart, 1));
                }
            }

            /// <summary>Straight NavMesh path to the route point, followed corner by corner (no goal logic of the fight planner).</summary>
            private Vector3 RouteDirection(Vector3 pos, Vector3 target, out float toCorner, out Vector3 onMesh)
            {
                NavMeshHit a, b;
                onMesh = NavMesh.SamplePosition(pos, out a, 2f, NavMesh.AllAreas) ? a.position : pos;
                // Replan from where the body is when the corner being walked to is out of straight sight on the mesh (the body
                // drifted past a tight turn): a fresh path's first corner is always in sight.
                if (_routeCorner < 0 || _routeCorner >= _routePath.corners.Length
                    || NavMesh.Raycast(onMesh, _routePath.corners[_routeCorner], out b, NavMesh.AllAreas))
                {
                    Vector3 to = NavMesh.SamplePosition(target, out b, 2f, NavMesh.AllAreas) ? b.position : target;
                    _routeCorner = NavMesh.CalculatePath(onMesh, to, NavMesh.AllAreas, _routePath) && _routePath.corners.Length > 1 ? 1 : int.MaxValue;
                }

                Vector3 goal = _routeCorner < _routePath.corners.Length ? _routePath.corners[_routeCorner] : target;
                Vector3 d = goal - pos; d.y = 0f;
                if (d.magnitude < 0.6f && _routeCorner < _routePath.corners.Length - 1)
                {
                    _routeCorner++;
                    goal = _routePath.corners[_routeCorner];
                    d = goal - pos; d.y = 0f;
                }

                toCorner = d.magnitude;
                return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
            }

            /// <summary>
            /// Dev-only (H12) route-stall trace: the body moved under 0.1 m in a trace period while walking — log what steering
            /// asked, what the motor was commanded and every solid collider the controller capsule touches, so a BLOCKED point
            /// names its blocker instead of leaving it to guesswork.
            /// </summary>
            private void LogRouteStall(string key, PlayerRig rig, Vector3 pos)
            {
                var cc = rig.Motor != null ? rig.Motor.GetComponent<CharacterController>() : null;
                var sb = new System.Text.StringBuilder("route-stall key=" + key + " pos=" + V(pos) + " goal=" + V(_routeLastGoal) + " steer=" + V(_routeLastSteer));
                if (rig.Motor != null) sb.Append(" input=" + QaArgs.F(rig.Motor.RawInput.x, 2) + "," + QaArgs.F(rig.Motor.RawInput.y, 2) + " commanded=" + V(rig.Motor.CommandedPlanarVelocity));
                if (cc != null)
                {
                    sb.Append(" cc r=" + QaArgs.F(cc.radius, 2) + " h=" + QaArgs.F(cc.height, 2) + " c=" + V(cc.center) + " skin=" + QaArgs.F(cc.skinWidth, 2) + " grounded=" + cc.isGrounded);
                    Vector3 c = cc.transform.TransformPoint(cc.center);
                    float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
                    foreach (Collider hit in Physics.OverlapCapsule(c + Vector3.down * half, c + Vector3.up * half, cc.radius + cc.skinWidth + 0.05f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit == cc || hit.transform.IsChildOf(rig.transform)) continue;
                        var path = new System.Text.StringBuilder(hit.name);
                        for (Transform t = hit.transform.parent; t != null && path.Length < 160; t = t.parent) path.Insert(0, t.name + "/");
                        sb.Append(" | touch " + hit.GetType().Name + " layer=" + LayerMask.LayerToName(hit.gameObject.layer) + " " + path + " min=" + V(hit.bounds.min) + " max=" + V(hit.bounds.max));
                    }
                }

                Log(sb.ToString());
            }

            /// <summary>Dev-only spawn hold for the route run (VfxLauncherCaptureAgent / SpecialWeaponProbe precedent, H12).</summary>
            private static void HoldSpawns()
            {
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                if (dir == null) return;
                FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && (bool)f.GetValue(dir)) { f.SetValue(dir, false); dir.ClearAllEnemies(); }
                else if (f != null && dir.ActiveCount > 0) dir.ClearAllEnemies();
            }

            /// <summary>WP-064: top-down aim error — the rig's planar aim direction against the planar direction to the target.</summary>
            private static bool TryPlanarAimError(PlayerRig rig, QaBotObservation o, Vector3 pos, out float degrees)
            {
                Vector3 aimed = rig.Aim.AimDirection; aimed.y = 0f;
                Vector3 want = o.TargetPosition - pos; want.y = 0f;
                degrees = aimed.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f ? Vector3.Angle(aimed, want) : 0f;
                return aimed.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f;
            }

            /// <summary>WP-078: TP/FP aim error — the camera's centre ray against the ray origin → target chest (1 m up).</summary>
            private static bool TryLookAimError(QaBotObservation o, out float degrees)
            {
                degrees = 0f;
                PrototypeCameraRig cam = PrototypeCameraRig.Active;
                CameraAimRay ray;
                if (cam == null || !cam.TryGetAimRay(Vector2.zero, 0f, out ray) || ray.Direction.sqrMagnitude < 1e-6f)
                {
                    return false;
                }

                Vector3 toChest = o.TargetPosition + Vector3.up * 1.0f - ray.Origin;
                if (toChest.sqrMagnitude < 1e-6f)
                {
                    return false;
                }

                degrees = Vector3.Angle(ray.Direction, toChest);
                return true;
            }

            /// <summary>
            /// WP-078: true when the TP/FP screen-centre ray (from the stable camera pose) passes within the target's chest
            /// radius — the angle to the chest point (1 m up, as <see cref="LookStickToward"/> aims) is under
            /// max(1.5°, atan(0.35 m / distance)).
            /// </summary>
            private static bool LookRayOnTarget(QaBotObservation o)
            {
                PrototypeCameraRig cam = PrototypeCameraRig.Active;
                CameraAimRay ray;
                if (cam == null || !cam.TryGetAimRay(Vector2.zero, 0f, out ray) || ray.Direction.sqrMagnitude < 1e-6f)
                {
                    return true;   // no ray to judge by: do not starve the trigger
                }

                Vector3 toChest = o.TargetPosition + Vector3.up * 1.0f - ray.Origin;
                float distance = toChest.magnitude;
                if (distance < 1e-3f)
                {
                    return true;
                }

                float gate = Mathf.Max(1.5f, Mathf.Atan2(0.35f, distance) * Mathf.Rad2Deg);
                return Vector3.Angle(ray.Direction, toChest) <= gate;
            }

            private static Vector2 LookStickToward(QaBotObservation o, Vector2 aimStick, Vector3 fwd, Vector3 right)
            {
                PrototypeCameraRig cam = PrototypeCameraRig.Active;
                float dt = Time.deltaTime;
                if (cam == null || dt <= 0f)
                {
                    return Vector2.zero;
                }

                Vector3 dir;
                if (o.HasTarget)
                {
                    dir = o.TargetPosition + Vector3.up * 1.0f - cam.StablePosition;
                }
                else if (aimStick.sqrMagnitude > 0.01f)
                {
                    dir = QaBotBrain.StickToWorld(aimStick, fwd, right);
                }
                else
                {
                    return Vector2.zero;
                }

                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude < 1e-6f)
                {
                    return Vector2.zero;
                }

                float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                float pitch = o.HasTarget ? -Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg : 0f;   // + looks down
                CameraModeSettings settings = CameraModeService.Settings;
                float speed = Mathf.Max(1f, settings.StickLookDegreesPerSecond);
                float share = Mathf.Max(0.05f, cam.StickPitchShare);
                float wy = Mathf.Clamp(Mathf.DeltaAngle(cam.ViewYaw, yaw) / LookCloseSeconds, -speed, speed);
                float wp = Mathf.Clamp((pitch - cam.ViewPitch) / LookCloseSeconds, -speed * share, speed * share);

                // stick x → yaw right; stick y up → look up (pitch decreases), flipped by invert-Y
                Vector2 v = new Vector2(wy / speed, -wp / (speed * share) * (settings.InvertY ? -1f : 1f));
                float m = v.magnitude;
                if (m < 1e-3f)
                {
                    return Vector2.zero;
                }

                float effective = Mathf.Pow(Mathf.Min(1f, m), 1f / Mathf.Max(0.1f, cam.StickResponseExponent));
                float dz = InputPreferencesStore.Current.StickDeadzone;
                float raw = dz + effective * (DD.Prototype.Inputs.PrototypeInputSource.StickSaturation - dz);
                return v / m * Mathf.Min(1f, raw);
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

            private readonly ZombieEnemy[] _candidates = new ZombieEnemy[8];
            private readonly float[] _candidateSq = new float[8];

            private ZombieEnemy SelectTarget(Vector3 from, PlayerRig rig, ZombieEnemy nearest)
            {
                int n = 0;
                ZombieEnemy fallback = null;
                float fallbackSq = float.MaxValue;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive || z.IsDying || z.IsRising) continue;
                    Vector3 d = z.transform.position - from; d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq < fallbackSq) { fallbackSq = sq; fallback = z; }
                    if (sq > QaBotBrain.FireRange * QaBotBrain.FireRange) continue;
                    // keep the 8 nearest (insertion into a fixed array, no allocation)
                    int at = n < _candidates.Length ? n++ : _candidates.Length;
                    if (at == _candidates.Length) { if (sq >= _candidateSq[at - 1]) continue; at--; }
                    while (at > 0 && _candidateSq[at - 1] > sq) { _candidates[at] = _candidates[at - 1]; _candidateSq[at] = _candidateSq[at - 1]; at--; }
                    _candidates[at] = z; _candidateSq[at] = sq;
                }

                // design review target priority (lower rank first, nearest within a rank since the list is sorted):
                // 0 a wind-up at arm's length (< 2.5 m), 1 a telegraphing Special/Elite/Ultra within 6 m ahead,
                // 2 a blocker within 10 m and ±40° of the escape direction, 3 the rest
                Transform muzzle = rig.MuzzleAnchor != null ? rig.MuzzleAnchor : rig.transform;
                ZombieEnemy pickBest = null; int pickRank = int.MaxValue;
                Vector3 moveDir = _planDir; moveDir.y = 0f;
                for (int i = 0; i < n; i++)
                {
                    ZombieEnemy z = _candidates[i];
                    Vector3 tp = z.transform.position;
                    float dist = Mathf.Sqrt(_candidateSq[i]);
                    bool tele = IsTelegraphing(z.transform);
                    Vector3 dz = tp - from; dz.y = 0f;
                    float cos = moveDir.sqrMagnitude > 1e-4f && dist > 1e-3f ? Vector3.Dot(dz / dist, moveDir.normalized) : 0f;
                    int rank = tele && dist < 2.5f ? 0
                             : tele && z.Tier != EnemyTier.Standard && dist < 6f && cos > 0f ? 1
                             : dist < 10f && cos > 0.766f ? 2
                             : 3;
                    if (rank >= pickRank) continue;
                    if (!HasLineOfSight(muzzle.position, tp + Vector3.up * 1.0f, dist)) continue;
                    pickRank = rank; pickBest = z;
                    if (rank == 0) break;
                }
                if (pickBest != null) { ClearCandidates(n); return pickBest; }
                ClearCandidates(n);
                return fallback != null ? fallback : nearest;
            }

            private void ClearCandidates(int n) { for (int i = 0; i < n; i++) _candidates[i] = null; }

            // ---- WP-064B v3 training loop (design review 24.09, game-test-engineer): a fixed NavMesh ring inside the spawn
            // gates. Running it keeps the whole horde on one side; fresh spawns ahead are handled by target priority.
            private readonly List<Vector3> _loop = new List<Vector3>(32);
            private int _loopSense = 1;              // +1 counter-clockwise, -1 clockwise
            private int _flipVotes;
            private float _nextLoopEval, _reverseCostUntil;

            private void BuildLoop()
            {
                _loop.Clear();
                // radius from the real gates: stay >= 8 m inside the nearest one, 14..26 m
                float gate = float.MaxValue;
                IReadOnlyList<SpawnPointRuntime> sp = SpawnPointRegistry.Points;
                for (int i = 0; i < sp.Count; i++)
                {
                    Vector3 g = sp[i].NavPosition == Vector3.zero ? sp[i].WorldPosition : sp[i].NavPosition;
                    Vector3 d = g - _arenaCentre; d.y = 0f;
                    gate = Mathf.Min(gate, d.magnitude);
                }
                float[] radii = { Mathf.Clamp(gate - 8f, 14f, 26f), 20f, 16f, 14f };
                NavMeshHit h;
                for (int ri = 0; ri < radii.Length && _loop.Count == 0; ri++)
                {
                    var ring = new List<Vector3>(24);
                    bool broken = false;
                    for (int k = 0; k < 24 && !broken; k++)
                    {
                        float a = k * Mathf.PI * 2f / 24f;
                        Vector3 c = _arenaCentre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radii[ri];
                        // the nearest point with >= 3 m clearance, not raised, within 4 m of the ideal ring
                        Vector3 bestP = Vector3.zero; bool found = false;
                        for (int t = 0; t < 9 && !found; t++)
                        {
                            float off = (t % 2 == 1 ? 1 : -1) * ((t + 1) / 2) * 1f;
                            Vector3 q = _arenaCentre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radii[ri] + off);
                            if (!NavMesh.SamplePosition(q, out h, 2f, NavMesh.AllAreas)) continue;
                            Vector3 p = h.position;
                            if (p.y > 0.2f) continue;
                            if (NavMesh.FindClosestEdge(p, out h, NavMesh.AllAreas) && h.distance < 3f) continue;
                            bestP = p; found = true;
                        }
                        if (!found) { broken = true; break; }
                        ring.Add(bestP);
                    }
                    if (broken) continue;
                    // consecutive waypoints must connect without detours
                    if (_safePath == null) _safePath = new NavMeshPath();
                    for (int k = 0; k < ring.Count && !broken; k++)
                    {
                        Vector3 a = ring[k], b = ring[(k + 1) % ring.Count];
                        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, _safePath) || _safePath.status != NavMeshPathStatus.PathComplete) { broken = true; break; }
                        float len = 0f; Vector3[] cs = _safePath.corners;
                        for (int j = 1; j < cs.Length; j++) len += Vector3.Distance(cs[j - 1], cs[j]);
                        if (len > 2.2f * Vector3.Distance(a, b) + 1f) broken = true;
                    }
                    if (!broken) { _loop.AddRange(ring); _loopRadius = radii[ri]; }
                }
                Log("training-loop radius=" + QaArgs.F(_loopRadius, 1) + " points=" + _loop.Count + " nearestGate=" + QaArgs.F(gate, 1));
            }

            private float _loopRadius;
            private NavMeshPath _safePath;

            // ---- WP-064B safe-spot planner: a global "where is it safe to be in 3 s" choice feeding the local planner
            private readonly List<Vector3> _spots = new List<Vector3>(256);
            private Vector3 _safeSpot;
            private bool _hasSafeSpot;
            private float _nextSafeSpotTime;
            private NavMeshPath _safeSpotPath;

            private void BuildSafeSpots()
            {
                _spots.Clear();
                NavMeshHit h;
                for (float x = -42f; x <= 42f; x += 5f)
                for (float z = -42f; z <= 42f; z += 5f)
                {
                    if (!NavMesh.SamplePosition(new Vector3(x, 0f, z), out h, 2f, NavMesh.AllAreas)) continue;
                    Vector3 p = h.position;
                    if (p.y > 0.2f) continue;   // no bridges / decks
                    int exits = 0;
                    for (int e = 0; e < 8; e++)
                    {
                        Vector3 ed = Quaternion.Euler(0f, e * 45f, 0f) * Vector3.forward;
                        if (!NavMesh.Raycast(p, p + ed * 7f, out h, NavMesh.AllAreas) && Mathf.Max(Mathf.Abs(p.x + ed.x * 7f), Mathf.Abs(p.z + ed.z * 7f)) < RingMargin) exits++;
                    }
                    if (exits >= 6) _spots.Add(p);
                }
                Log("safe-spots " + _spots.Count);
            }

            /// <summary>
            /// Score every open spot by the arrival-time margin (nearest zombie reach time minus the player's, zombies ≤ 4.6 m/s,
            /// player ≥ 6.2 m/s), minus bodies near the straight run there and the ring edge; head along the NavMesh path to the
            /// best one. Re-chosen every 0.5 s.
            /// </summary>
            private Vector3 SafeSpotDirection(Vector3 pos, int zombieCount)
            {
                if (_spots.Count == 0) return Vector3.zero;
                if (Time.unscaledTime >= _nextSafeSpotTime)
                {
                    _nextSafeSpotTime = Time.unscaledTime + 0.5f;
                    float best = float.NegativeInfinity; Vector3 bestSpot = Vector3.zero;
                    for (int k = 0; k < _spots.Count; k++)
                    {
                        Vector3 c = _spots[k];
                        Vector3 dc = c - pos; dc.y = 0f;
                        float dp = dc.magnitude;
                        if (dp < 4f || dp > 30f) continue;
                        float tPlayer = dp / 6.2f;
                        float tZombie = float.MaxValue, laneRisk = 0f;
                        Vector3 dirC = dc / dp;
                        for (int i = 0; i < zombieCount; i++)
                        {
                            Vector3 zc = c - _threatPos[i]; zc.y = 0f;
                            tZombie = Mathf.Min(tZombie, zc.magnitude / 4.6f);
                            Vector3 zp = _threatPos[i] - pos; zp.y = 0f;
                            float along = Vector3.Dot(zp, dirC);
                            if (along > 0f && along < dp)
                            {
                                float lat = Mathf.Sqrt(Mathf.Max(0f, zp.sqrMagnitude - along * along));
                                if (lat < 2.5f) laneRisk += (2.5f - lat);
                            }
                        }
                        float edge = Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.z)) / RingMargin;
                        float score = (tZombie - tPlayer) - 0.6f * laneRisk - 3f * Mathf.Clamp01((edge - 0.7f) / 0.25f) - 0.02f * dp;
                        if (_hasSafeSpot && (c - _safeSpot).sqrMagnitude < 1f) score += 0.4f;   // hysteresis
                        if (score > best) { best = score; bestSpot = c; }
                    }
                    _hasSafeSpot = best > float.NegativeInfinity;
                    _safeSpot = bestSpot;
                    if (_hasSafeSpot)
                    {
                        if (_safeSpotPath == null) _safeSpotPath = new NavMeshPath();
                        NavMeshHit sh;
                        Vector3 from = NavMesh.SamplePosition(pos, out sh, 1.5f, NavMesh.AllAreas) ? sh.position : pos;
                        if (!NavMesh.CalculatePath(from, _safeSpot, NavMesh.AllAreas, _safeSpotPath) || _safeSpotPath.status == NavMeshPathStatus.PathInvalid) _hasSafeSpot = false;
                    }
                }
                if (!_hasSafeSpot || _safeSpotPath == null) return Vector3.zero;
                Vector3[] corners = _safeSpotPath.corners;
                for (int i = 1; i < corners.Length; i++)
                {
                    Vector3 d = corners[i] - pos; d.y = 0f;
                    if (d.sqrMagnitude > 1.5f * 1.5f) return d.normalized;
                }
                Vector3 last = _safeSpot - pos; last.y = 0f;
                return last.sqrMagnitude > 1e-3f ? last.normalized : Vector3.zero;
            }

            /// <summary>Direction along the loop in the current sense (joining it first when off it); the sense flips only
            /// after three 0.5 s evaluations with the crowd ahead 2.5× the crowd behind.</summary>
            private Vector3 LoopTangent(Vector3 pos, int zombieCount)
            {
                if (_loop.Count < 3) return Vector3.zero;
                int nearest = 0; float bestSq = float.MaxValue;
                for (int i = 0; i < _loop.Count; i++) { Vector3 d = _loop[i] - pos; d.y = 0f; if (d.sqrMagnitude < bestSq) { bestSq = d.sqrMagnitude; nearest = i; } }
                if (bestSq > 36f)
                {
                    Vector3 join = _loop[nearest] - pos; join.y = 0f;   // off the loop: head for it first
                    return join.normalized;
                }
                int next = (nearest + _loopSense + _loop.Count) % _loop.Count;
                Vector3 tan = _loop[next] - pos; tan.y = 0f;
                if (tan.sqrMagnitude < 4f) { next = (next + _loopSense + _loop.Count) % _loop.Count; tan = _loop[next] - pos; tan.y = 0f; }
                tan = tan.normalized;

                if (Time.unscaledTime >= _nextLoopEval)
                {
                    _nextLoopEval = Time.unscaledTime + 0.5f;
                    float ahead = 0f, behind = 0f;
                    for (int i = 0; i < zombieCount; i++)
                    {
                        Vector3 d = _threatPos[i] - pos; d.y = 0f;
                        float dist = d.magnitude;
                        if (dist > 14f || dist < 1e-3f) continue;
                        float cos = Vector3.Dot(d / dist, tan);
                        if (cos > 0.5f) ahead += 1f / (dist * dist);
                        else if (cos < -0.5f) behind += 1f / (dist * dist);
                    }
                    _flipVotes = ahead > 2.5f * behind + 0.01f ? _flipVotes + 1 : 0;
                    if (_flipVotes >= 3)
                    {
                        _loopSense = -_loopSense; _flipVotes = 0; _reverseCostUntil = Time.unscaledTime + 2f;
                        _summary.loopReversals++;
                        next = (nearest + _loopSense + _loop.Count) % _loop.Count;
                        tan = _loop[next] - pos; tan.y = 0f; tan = tan.sqrMagnitude > 1e-4f ? tan.normalized : Vector3.zero;
                    }
                }
                return tan;
            }

            private bool FindVantage(Vector3 from, Vector3 target, out Vector3 vantage)
            {
                vantage = Vector3.zero;
                float bestCost = float.MaxValue;
                if (_reachPath == null) _reachPath = new NavMeshPath();
                float[] radii = { 6f, 10f, 14f };
                for (int r = 0; r < radii.Length; r++)
                {
                    for (int k = 0; k < 16; k++)
                    {
                        float a = k * Mathf.PI / 8f;
                        Vector3 c = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radii[r];
                        if (Mathf.Abs(c.x) > RingMargin - 2f || Mathf.Abs(c.z) > RingMargin - 2f) continue;
                        if (!NavMesh.SamplePosition(c, out NavMeshHit h, 1.5f, NavMesh.AllAreas)) continue;
                        Vector3 p = h.position;
                        if (!HasLineOfSight(p + Vector3.up * 1.1f, target + Vector3.up * 1.0f, radii[r])) continue;   // the muzzle line, not the eye
                        if (!NavMesh.CalculatePath(from, p, NavMesh.AllAreas, _reachPath) || _reachPath.status != NavMeshPathStatus.PathComplete) continue;
                        Vector3 d = p - from; d.y = 0f;
                        float cost = d.magnitude + ((p - _lastVantage).sqrMagnitude < 4f ? 50f : 0f);   // don't retry the same failed spot
                        if (cost < bestCost) { bestCost = cost; vantage = p; }
                    }
                }
                if (bestCost < float.MaxValue) { _lastVantage = vantage; return true; }
                return false;
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
            private const float SteerProbe = 1.6f;
            private static Vector3 SteerOnNavMesh(Vector3 pos, Vector3 want, Vector3 avoidDirection, float probe = SteerProbe)
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
                    if (NavMesh.Raycast(pos, pos + d * probe, out hit, NavMesh.AllAreas)) continue;
                    if (avoiding && Vector3.Dot(d, avoidDirection) > 0.5f) { if (fallback == Vector3.zero) fallback = d; continue; }   // open, but into the crowd
                    return d;
                }
                // WP-064B soak 3: at an edge corner every rotation was blocked and the raw wanted direction walked the player
                // off the south ground plate (z = -80, no boundary collider) — the fallback is now a stop, never the edge
                return fallback != Vector3.zero ? fallback : Vector3.zero;
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
                        // WP-064B: spawn points may sit beyond the WP-038M ring — patrol stays 3 m inside it
                        Vector3 w = hit.position;
                        float lim = RingMargin - 3f;
                        if (Mathf.Abs(w.x) > lim || Mathf.Abs(w.z) > lim)
                        {
                            Vector3 inside = new Vector3(Mathf.Clamp(w.x, -lim, lim), w.y, Mathf.Clamp(w.z, -lim, lim));
                            if (!NavMesh.SamplePosition(inside, out hit, 4f, NavMesh.AllAreas)) continue;
                            w = hit.position;
                        }
                        _waypoints.Add(w);
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
                    if (NavMesh.SamplePosition(c, out hit, 4f, NavMesh.AllAreas) && Mathf.Abs(hit.position.x) < RingMargin - 3f && Mathf.Abs(hit.position.z) < RingMargin - 3f)
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
                        // WP-064B soak 14: the body stood on a NavMesh island (a patch the player can step onto but agents
                        // cannot leave — every path failed, 300 s frozen). No path → walk straight at the goal; the stall
                        // detector rotates it if a wall is in the way.
                        _islandFails++;
                        if (_islandFails >= 2)
                        {
                            Vector3 straight = _goal - pos; straight.y = 0f;
                            if (straight.sqrMagnitude > 1e-4f) { dir = straight.normalized; _summary.islandEscapes++; return true; }
                        }
                        PickNewGoal();
                        return false;
                    }
                    _islandFails = 0;

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
                _summary.padDisabledFrames = _padDisabledFrames;
                _summary.pointerAfterPadFrames = _pointerAfterPad;
                _summary.unfocusedFrames = _unfocusedFrames;
                _summary.inputMeasurable = _routeDir != null || (_padDisabledFrames == 0 && _pointerAfterPad == 0);
                _summary.cameraMode = CameraModeService.Current.ToString();
                _summary.qualityTier = QualityTiers.Current.ToString();
                _summary.worldHalfExtent = RingMargin;
                var top = new List<KeyValuePair<string, int>>(_fireHits);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 6; i++) sb.Append(i > 0 ? " | " : "").Append(top[i].Key).Append('=').Append(top[i].Value);
                _summary.fireLineHits = sb.ToString();
                // WP-064 closure is exact: asking for N rounds means N ordered rounds must
                // complete without death or timeout. The former four-round legacy minimum
                // allowed a truncated 12-round soak to exit successfully.
                bool gateOk = ExactRunPassed(_summary.roundsCompleted, _rounds, _deathRound, _timeoutRound);
                _summary.gatePass = gateOk;
                _summary.pass = gateOk && !_invulnerable && !_specialEvidence && !_deathEvidence;   // WP-052/055/059: an evidence run is never a QA pass
                _summary.runMode = _stability ? "stability" : (_invulnerable || _specialEvidence || _deathEvidence ? "evidence" : "normal");
                if (_routeDir != null)
                {
                    // WP-078 D3c: a route run passes when every point was captured or is not applicable; it is not a gameplay pass
                    bool all = _summary.routeFrames.Count == RoutePoints.Length;
                    foreach (RouteFrame f in _summary.routeFrames) all &= f.status == "captured" || f.status == "notApplicable";
                    _summary.runMode = "route";
                    _summary.gatePass = all;
                    _summary.pass = all && string.IsNullOrEmpty(_fail);
                    if (_drawCalls.Valid) _drawCalls.Dispose();
                    if (_batches.Valid) _batches.Dispose();
                }
                if (!_summary.inputMeasurable)
                {
                    // R2 (C-00 01.10): the input never reached the game for part of the run — not a gameplay pass or fail, a rerun
                    _summary.pass = false;
                    _summary.runMode = "unmeasurable";
                }

                _summary.stabilityPass = _stability && !_specialEvidence && !_deathEvidence && string.IsNullOrEmpty(_fail) && _timeoutRound <= 0
                                         && ExactRunPassed(_summary.roundsCompleted, _rounds, _deathRound, _timeoutRound);
                _summary.note = string.IsNullOrEmpty(_fail) ? (_died ? "player died" : "ok") : _fail;
                if (_stability) _summary.note += " [stability run (DEC-091): invulnerable by design; stabilityPass=" + _summary.stabilityPass + "]";
                else if (_invulnerable) _summary.note += " [invulnerable evidence run: WP-052 tier frames; not a QA pass]";   // (WP-055: the marks used to be overwritten by the line above)
                if (_specialEvidence) _summary.note += " [special evidence run: WP-055 sw_* frames; not a QA pass]";
                if (_deathEvidence) _summary.note += " [death evidence run: WP-059 deathframe-*; not a QA pass]";
                if (!_summary.inputMeasurable) _summary.note += " [input lost: pad disabled " + _padDisabledFrames + " / pointer after pad " + _pointerAfterPad + " / unfocused " + _unfocusedFrames + " frames — unmeasurable, rerun]";
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
                    report.Add("bot.rounds", _summary.gatePass, _summary.roundsCompleted + "/" + _rounds + (_deathRound > 0 ? " (died r" + _deathRound + ")" : "") + (_timeoutRound > 0 ? " (timeout r" + _timeoutRound + ")" : ""), "exactly " + _rounds + " requested rounds, no death/timeout",
                        _summary.note + "; kills " + _summary.totalKills + ", deaths " + _summary.deaths + ", damage " + _summary.totalDamageEvents +
                        ", stuck " + _summary.stuckRecoveries + ", relocations " + _summary.relocations + ", wallEscapes " + _summary.wallEscapes + ", upgrades " + _summary.upgradesBought + " (" + _summary.goldSpent + " gold), kite " + _summary.kiteFrames + ", telegraphs " + _summary.telegraphDodges + ", specialPickups " + _summary.specialPickupsSought + ", reloads " + _summary.reloads, "bot-summary.json");
                    report.Add("bot.input", _summary.inputMeasurable, _summary.aimKind + "; pad disabled " + _padDisabledFrames + ", pointer after pad " + _pointerAfterPad + ", unfocused " + _unfocusedFrames + " frames",
                        "the bot's pad holds the scheme for the whole run", "R2 (C-00 01.10): input ignores focus, native keyboard / pointer suspended; false = unmeasurable (rerun), not a gameplay result", "bot-summary.json");
                    report.Add("bot.determinism", true, "spawn " + _summary.spawnSequenceHash + " outcome " + _summary.outcomeHash, "report only",
                        "compare across runs with the same seed; frame timing is not seeded", "bot-summary.json");
                    report.summary = "bot mean frame " + QaArgs.F(_summary.meanFrameMs) + " ms over " + _frameMs.Count + " frames";
                    report.Write(_outDir);
                }
                catch (Exception e)
                {
                    _artifactWriteFailed = true;
                    _summary.artifactWriteFailed = true;
                    _summary.pass = false;
                    _summary.note = (_summary.note ?? "") + " [artifact write failed: " + e.GetType().Name + "]";
                    Debug.LogError("[QaRunBot] write failed: " + e);
                }

                Debug.Log("[QaRunBot] " + (_stability ? (_summary.stabilityPass ? "STABILITY PASS" : "STABILITY FAIL") : (_summary.pass ? "PASS" : "FAIL")) + " rounds=" + _summary.roundsCompleted + "/" + _rounds +
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
                sb.Append("QaRunBot ").Append(_stability ? (_summary.stabilityPass ? "STABILITY PASS" : "STABILITY FAIL") : (_summary.pass ? "PASS" : "FAIL")).Append(" seed=").Append(_seed)
                  .Append(" rounds=").Append(_summary.roundsCompleted).Append('/').Append(_rounds)
                  .Append(" kills=").Append(_summary.totalKills).Append(" deaths=").Append(_summary.deaths)
                  .Append(" spawns=").Append(_summary.spawnCount).Append(" spawnHash=").Append(_summary.spawnSequenceHash)
                  .Append(" outcomeHash=").Append(_summary.outcomeHash).Append(" note=").Append(_summary.note).Append('\n');
                for (int i = 0; i < _summary.rounds.Count; i++)
                {
                    RoundRecord r = _summary.rounds[i];
                    sb.Append(r.inputLost ? "UNMEASURED " : (r.completed ? "PASS " : "FAIL ")).Append("round-").Append(r.round.ToString("D2"))
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
                Application.Quit(ExitCode);
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
