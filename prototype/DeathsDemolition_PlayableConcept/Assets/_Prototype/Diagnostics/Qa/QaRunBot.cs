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
        public const int DefaultRounds = 8;
        public const int DefaultSeed = 26;
        public const float DefaultRoundTimeout = 300f;

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
            public float meanFrameMs;
            public float meanAimErrorDeg;     // angle between the rig's aim direction and the bot's target direction while a target was aimed
            public string aimKind;            // PlayerAim.AimKind seen most (Stick expected)
            public int fireFrames;
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
            private bool _externalQuit;
            private bool _died;
            private string _fail;
            private float _reloadCooldown;
            private double _aimErrorSum;
            private int _aimErrorSamples;
            private int _aimKindStick, _aimKindPointer;
            private readonly Dictionary<string, int> _fireHits = new Dictionary<string, int>();

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
            }

            private void OnDied(PlayerDeathInfo info)
            {
                _died = true;
                _summary.deaths++;
                Log("player-died round=" + (_current != null ? _current.round : 0));
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
                if (!_running || _finished)
                {
                    return;
                }

                _frameMs.Add(Time.unscaledDeltaTime * 1000f);
                FlushPendingFrame();   // WP-052
                RunDirector run = RunDirector.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                if (run == null || rig == null)
                {
                    return;
                }

                if (_current != null && Time.unscaledTime - _roundStart > _roundTimeout)
                {
                    CloseRoundRecord(false, "timeout");
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
                            run.CloseUpgradeScreen();   // the Skip button's call; the bot buys nothing
                            _upgradeSeen = -1f;
                            Log("upgrade-skipped");
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
                        if (_relocating && (pos - _arenaCentre).sqrMagnitude < 9f) _relocating = false;
                    }
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
                    Vector3 steered = SteerOnNavMesh(pos, wantMove);
                    if (_closeBlocker != null && cmd.Mode != QaBotMode.Patrol)
                    {
                        // hugging a wall with the target beyond it: back off from the blocker before anything else
                        Vector3 away = pos - _closeBlocker.ClosestPoint(pos); away.y = 0f;
                        if (away.sqrMagnitude > 1e-4f) steered = SteerOnNavMesh(pos, (away.normalized * 0.7f + steered * 0.3f).normalized);
                        _summary.wallEscapes++;
                    }
                    cmd.Move = QaBotBrain.WorldToStick(steered, fwd, right) * Mathf.Clamp01(cmd.Move.magnitude);
                }
                if (Time.unscaledTime - _lastTrace >= 1f)
                {
                    _lastTrace = Time.unscaledTime;
                    Log("t pos=" + V(pos) + " mode=" + cmd.Mode + " tgt=" + (target != null ? QaArgs.F(o.TargetDistance, 1) : "-") + " los=" + (o.TargetLineOfSight ? 1 : 0) +
                        " mz=" + (_lastMuzzleClear ? 1 : 0) + " eye=" + (_lastEyeClear ? 1 : 0) + " blk=" + (_closeBlocker != null ? _closeBlocker.name + "@" + V(_closeBlocker.bounds.center) + "/" + V(_closeBlocker.bounds.size) : "-") +
                        " fire=" + (cmd.Fire ? 1 : 0) + " hp=" + QaArgs.F(rig.Health != null ? rig.Health.Normalized : -1f, 2) + " near=" + nearby + " kills=" + _summary.totalKills);
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
                }
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
            private static Vector3 SteerOnNavMesh(Vector3 pos, Vector3 want)
            {
                want.y = 0f;
                if (want.sqrMagnitude < 1e-4f) return want;
                want.Normalize();
                NavMeshHit hit;
                for (int i = 0; i < SteerAngles.Length; i++)
                {
                    Vector3 d = Quaternion.Euler(0f, SteerAngles[i], 0f) * want;
                    if (!NavMesh.Raycast(pos, pos + d * 1.6f, out hit, NavMesh.AllAreas)) return d;
                }
                return want;
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
                _summary.meanAimErrorDeg = _aimErrorSamples > 0 ? (float)(_aimErrorSum / _aimErrorSamples) : -1f;
                _summary.aimKind = "stick " + _aimKindStick + " / pointer " + _aimKindPointer;
                var top = new List<KeyValuePair<string, int>>(_fireHits);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 6; i++) sb.Append(i > 0 ? " | " : "").Append(top[i].Key).Append('=').Append(top[i].Value);
                _summary.fireLineHits = sb.ToString();
                _summary.pass = _summary.roundsCompleted >= _rounds && string.IsNullOrEmpty(_fail) && !_invulnerable;   // WP-052: an invulnerable evidence run is never a QA pass
                if (_invulnerable) _summary.note = (_summary.note ?? "") + " [invulnerable evidence run: WP-052 tier frames; not a QA pass]";
                _summary.note = string.IsNullOrEmpty(_fail) ? (_died ? "player died" : "ok") : _fail;

                try
                {
                    Directory.CreateDirectory(_outDir);
                    File.WriteAllText(Path.Combine(_outDir, "bot-summary.json"), JsonUtility.ToJson(_summary, true));
                    File.WriteAllText(Path.Combine(_outDir, "events.txt"), _events.ToString());
                    File.WriteAllText(Path.Combine(_outDir, "summary.txt"), SummaryLines());
                    QaReport report = QaReport.Create("QaRunBot", _seed);
                    report.Add("bot.rounds", _summary.roundsCompleted >= _rounds, _summary.roundsCompleted + "/" + _rounds, ">= " + _rounds,
                        _summary.note + "; kills " + _summary.totalKills + ", deaths " + _summary.deaths + ", damage " + _summary.totalDamageEvents +
                        ", stuck " + _summary.stuckRecoveries + ", relocations " + _summary.relocations + ", wallEscapes " + _summary.wallEscapes + ", reloads " + _summary.reloads, "bot-summary.json");
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
                          " kills=" + _summary.totalKills + " deaths=" + _summary.deaths + " spawnHash=" + _summary.spawnSequenceHash + " aimErr=" + QaArgs.F(_summary.meanAimErrorDeg, 1) + "° aimKind=" + _summary.aimKind + " fireFrames=" + _summary.fireFrames + " fireLine[" + _summary.fireLineHits + "] note=" + _summary.note);

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
