using System;
using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Navigation;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// The dynamic spawn system: round pool flow, nine-input spawn point weighting,
    /// controlled tier introduction and the hard technical safety ceiling.
    ///
    /// REQUIREMENTS IT IMPLEMENTS
    /// --------------------------
    ///   REQ-012  a round completes when the pool is empty AND no enemy is alive (corpses excluded);
    ///   REQ-019  the pool flows continuously and stops when it is spent;
    ///   REQ-065  enemies only ever leave from designer-placed points;
    ///   REQ-066  point density is dynamic; a pointless point drops to zero weight;
    ///   REQ-067  the nine approved positional inputs (see <see cref="SpawnWeightCalculator"/>);
    ///   REQ-068  a point without a COMPLETE walkable path is not used;
    ///   REQ-069  no spawn next to the player, on screen up close, or behind the camera;
    ///   REQ-070  cooldown, recency, per-point occupancy and local density spread the load;
    ///   REQ-071  Runner R3 / Guard R5 / Executioner R8 debut guaranteed, then weighted;
    ///   REQ-072  keeps the round's target active count and NEVER exceeds the ceiling of 40.
    ///
    /// HOW IT IS SPLIT (WP-003)
    /// ------------------------
    ///   <see cref="RoundPlan"/>          the three numbers a round opens with, resolved once;
    ///   <see cref="RoundLedger"/>        state machine + counters + the completion rule, pure;
    ///   <see cref="SpawnRefillClock"/>   the refill metronome, pure, pause-safe;
    ///   <see cref="SpawnPointSelector"/> weighted choice among registered points, pure;
    ///   this class                       the scene: pool, points, NavMesh cache, camera flags,
    ///                                    the enemy hand-off and the lifecycle sink.
    /// Everything with a rule in it lives in the pure parts and is proven in EditMode.
    ///
    /// ARITHMETIC OWNERSHIP
    /// --------------------
    /// Pool size, target active count, ceiling, health, damage and tier weights all come
    /// from PrototypeConfig's own methods - TotalEnemyPool(round), TargetActiveEnemies(round),
    /// AbsoluteSafetyCeiling, EnemyHealth(...), EnemyAttackDamage(...), TierSpawnWeight(...).
    /// None of it is re-derived here, so the F1 panel reshapes the curve live.
    ///
    /// POOL OWNERSHIP
    /// --------------
    /// This director owns the enemy pool. Nothing else may call
    /// PoolService.GetOrCreate on the enemy prefab, or the second caller gets a typed-pool
    /// clash. Policy is ReturnNull: when the ceiling is reached, Get returns null and the
    /// tick simply stops spawning - the ceiling can never be crossed by construction. The
    /// ledger's own occupancy test stops a spawn one step earlier, so a full map does not
    /// even reach the pool.
    ///
    /// LIFECYCLE CONTRACT
    /// ------------------
    /// The enemy reports its death and its despawn through <see cref="IEnemyLifecycleSink"/>
    /// with the ticket it was given (see <see cref="ISpawnedEnemy"/>). No frame polls the
    /// enemies for their state; housekeeping only reconciles a body that vanished without
    /// reporting (scene teardown), and counts every such case.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnDirector : PrototypeConfigBehaviour, IEnemyLifecycleSink
    {
        private struct ActiveEnemy
        {
            public PooledBehaviour Behaviour;
            public int Ticket;
            public int PointIndex;
        }

        [Header("Wiring")]
        [Tooltip("Enemy prefab (task G-04). It must derive from PooledBehaviour and should " +
                 "implement ISpawnedEnemy and ICrowdMember. Empty is tolerated: the director " +
                 "runs, warns once and spawns nothing.")]
        [SerializeField] private PooledBehaviour _enemyPrefab = null;

        [Tooltip("Optional. Empty uses the authored 12-point DefaultSpawnPointLayout.")]
        [SerializeField] private SpawnPointLayoutAsset _layout = null;

        [Header("Flow")]
        [Tooltip("Start round 1 by itself when no round director has taken over.")]
        [SerializeField] private bool _autoStartFirstRound = true;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds the director waits for a round director before starting round 1 itself.", true)]
        [SerializeField] private float _autoStartDelay = 0.5f;

        [Header("Weighting (PROTOTYPE-ONLY)")]
        [SerializeField] private SpawnWeightTuning _weightTuning = new SpawnWeightTuning();

        [Header("Diagnostics")]
        [SerializeField] private bool _drawGizmos = true;

        // ------------------------------------------------------------------ runtime state

        private readonly List<ActiveEnemy> _active = new List<ActiveEnemy>(64);
        private readonly RoundLedger _ledger = new RoundLedger(64);
        private readonly SpawnRefillClock _refillClock = new SpawnRefillClock();
        private readonly SpawnPointSelector _selector = new SpawnPointSelector();
        private readonly SpawnTierSelector _tierSelector = new SpawnTierSelector();

        /// <summary>WP-010: THE tier authority of the run (readouts, probe and tests read it; nothing else decides a tier).</summary>
        public SpawnTierSelector TierSelector => _tierSelector;

        // The exact delegates this director bound on the bridge, so OnDisable unbinds only its own.
        private Func<int> _bridgeRound;
        private Func<PrototypeConfig, EnemyTier> _bridgeDraw;
        private Func<string> _bridgeDescribe;
        private readonly Func<float> _random01 = () => UnityEngine.Random.value;

        private string[] _debugRows = new string[16];

        private PrototypePool<PooledBehaviour> _pool;
        private Camera _camera;
        private float _cameraSearchTimer;

        private float _autoStartTimer;
        private int _pathCursor;
        private float _debugRefreshTimer;
        private int _debugRoundTarget = 3;

        private bool _everStarted;
        private int _round = 1;
        private int _cooldownRelaxCount;
        private int _pathRelaxCount;
        private int _noPointTicks;

        // cached from config
        private SpawnConfigSection _spawnConfig;
        private float _refillInterval = 0.45f;
        private int _maxSpawnsPerTick = 2;

        // debug overrides
        private int _forcedTargetActive;
        private bool _spawningEnabled = true;

        private DebugPanelEntry[] _debugEntries = Array.Empty<DebugPanelEntry>();

        // ------------------------------------------------------------------ public API

        public static EnemySpawnDirector Instance { get; private set; }

        /// <summary>Fired after an enemy left the pool, was configured and reported alive.</summary>
        public event Action<EnemySpawnContext> EnemySpawned;

        /// <summary>Fired when a spawned enemy returned to the pool (death linger over, or cleanup).</summary>
        public event Action EnemyRetired;

        /// <summary>Fired once per round when the last enemy of the pool has been released.</summary>
        public event Action<int> RoundPoolExhausted;

        /// <summary>Fired exactly once per round when the pool is empty AND no enemy is alive.</summary>
        public event Action<int> RoundCleared;

        /// <summary>Fired on every round state change. The open lifecycle signal for later systems.</summary>
        public event Action<RoundState> RoundStateChanged;

        public int Round => _round;

        /// <summary>Where the round is. See <see cref="RoundState"/>.</summary>
        public RoundState State => _ledger.State;

        /// <summary>The round's population, as one value.</summary>
        public RoundPopulationSnapshot Population => _ledger.Snapshot();

        /// <summary>Enemies of the pool not released yet.</summary>
        public int RemainingInPool => _ledger.Unspawned;

        /// <summary>Living enemies spawned by this director. Corpses are not counted (REQ-012).</summary>
        public int ActiveCount => _ledger.Alive;

        /// <summary>Bodies this director has checked out of the pool, corpses included. The ceiling applies to this.</summary>
        public int OccupiedCount => _ledger.Occupied;

        /// <summary>Enemies released this round.</summary>
        public int SpawnedThisRound => _ledger.SpawnedTotal;

        /// <summary>Deaths registered this round.</summary>
        public int KilledThisRound => _ledger.Killed;

        public int TargetActive => _ledger.TargetActive;
        public int SafetyCeiling => _ledger.Ceiling;

        /// <summary>True from BeginRound until the round completes or the run ends.</summary>
        public bool IsRoundActive => _ledger.IsRoundLive;

        public bool IsForcedTarget => _forcedTargetActive > 0;

        /// <summary>Total pool of the round, straight from the round plan.</summary>
        public int RoundPoolTotal => _ledger.PoolTotal;

        private int _spawnsThisRun, _onScreenSpawnsThisRun;

        /// <summary>WP-030: share of this run's spawns whose point was off camera at spawn time (rule: ≥ 0.6, technical default).</summary>
        public float OffScreenSpawnShare => _spawnsThisRun > 0 ? 1f - (float)_onScreenSpawnsThisRun / _spawnsThisRun : 1f;
        public int SpawnsThisRun => _spawnsThisRun;
        public int OnScreenSpawnsThisRun => _onScreenSpawnsThisRun;

        // ---------------------------------------------------------------- WP-040 · DEC-081 rise visibility
        [PrototypeOnly(PendingDecisions.None,
            "WP-040 (DEC-081): viewport margin for the RISE decision, wider than the spawn-weight band (0.05) so a body " +
            "just outside the frame edge counts as visible and does not rise into a pan (hysteresis band, technical default).", true)]
        public const float RiseViewMargin = 0.12f;

        private int _offScreenSpawnsJudged, _riseEligibleSpawnsThisRun;

        /// <summary>WP-040: spawns this run whose fresh visibility check said OffScreen (rise-eligible under DEC-081).</summary>
        public int RiseEligibleSpawnsThisRun => _riseEligibleSpawnsThisRun;
        /// <summary>WP-040: spawns this run that had a camera to judge by (Unknown excluded).</summary>
        public int VisibilityJudgedSpawnsThisRun => _offScreenSpawnsJudged;

        /// <summary>
        /// WP-040 (DEC-081): the visibility class of a spawn position RIGHT NOW (not the point's cached
        /// flag): behind the camera or outside the margin band = OffScreen; inside = OnScreen; no camera = Unknown.
        /// </summary>
        public SpawnVisibility JudgeVisibility(Vector3 worldPosition)
        {
            Camera cam = ResolveCamera();
            if (cam == null) return SpawnVisibility.Unknown;

            Vector3 viewport = cam.WorldToViewportPoint(worldPosition + Vector3.up * 0.9f);   // chest height: the part of the body a rise would show
            if (viewport.z <= 0f) return SpawnVisibility.OffScreen;
            bool inside = viewport.x >= -RiseViewMargin && viewport.x <= 1f + RiseViewMargin &&
                          viewport.y >= -RiseViewMargin && viewport.y <= 1f + RiseViewMargin;
            return inside ? SpawnVisibility.OnScreen : SpawnVisibility.OffScreen;
        }

        /// <summary>Refill attempts that found no eligible point. Debug / test readout.</summary>
        public int NoPointTicks => _noPointTicks;

        private int _pathPendingTicks;
        /// <summary>WP-050: refill ticks skipped because some point had never been path-queried against a player yet (pending ≠ unreachable). Debug / test readout.</summary>
        public int PathPendingTicks => _pathPendingTicks;
        /// <summary>WP-050: times the reachability gate was relaxed with every path FRESH (a real bake / layout problem, not a start-of-round race).</summary>
        public int PathRelaxCount => _pathRelaxCount;

        /// <summary>WP-050: true when every registered point has a path result computed against a player this round.</summary>
        public bool AllPathsQueried
        {
            get
            {
                IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
                for (int i = 0; i < points.Count; i++) if (points[i].PathCachedAt < 0f) return false;
                return true;
            }
        }

        /// <summary>Bounded scatter around a point, metres (SpawnWeightTuning). Test readout for the REQ-065 check.</summary>
        public float SpawnScatterRadius => _weightTuning.SpawnScatterRadius;

        /// <summary>Share of the round pool spent before an owed debut is released (SpawnWeightTuning). Test/probe readout for the REQ-071 pacing check.</summary>
        public float GuaranteedIntroPoolFraction => _weightTuning.GuaranteedIntroPoolFraction;

        /// <summary>
        /// Population of the last round that completed through the ledger, captured at
        /// the moment <see cref="RoundCleared"/> was published. The round-end hand-off
        /// for the systems that follow (WP-004): round number plus the basic combat
        /// numbers of that round, nothing about gold or upgrades.
        /// </summary>
        public RoundPopulationSnapshot LastCompletedRound { get; private set; }

        /// <summary>
        /// The pool template this director spawns from.
        ///
        /// WHY THIS IS SETTABLE AT RUNTIME AT ALL
        /// The enemy template is not a .prefab asset: G-04 assembles it in code
        /// (EnemyFactory.TemplatePrefab) precisely so four parallel tasks never fight over
        /// a prefab file. A thing that does not exist on disk cannot be dragged into the
        /// serialized slot above, so the slot stayed empty and this director spawned
        /// nothing while reporting no error at all - the "wiring gap" EnemyFactory's own
        /// remarks warn about. The run installer closes it here.
        ///
        /// Refused once the pool exists. PoolService keys pools by prefab instance, so a
        /// swap mid-run would strand every enemy already checked out of the old one.
        /// </summary>
        public PooledBehaviour EnemyPrefab
        {
            get => _enemyPrefab;
            set
            {
                if (ReferenceEquals(_enemyPrefab, value))
                {
                    return;
                }

                if (_pool != null)
                {
                    NavigationDiagnostics.WarnOnce(
                        "spawn.prefab.late",
                        "[Spawn] The enemy prefab cannot be replaced after the pool has been " +
                        "created; the existing pool keeps serving the old template.");
                    return;
                }

                _enemyPrefab = value;
            }
        }

        /// <summary>
        /// Puts a spawn director in the scene if there is not one. The run installer's
        /// hook; mirrors RunDirector.EnsureExists so the two are created the same way.
        /// </summary>
        public static EnemySpawnDirector EnsureExists(bool replaceExisting = false)
        {
            if (Instance != null && !replaceExisting)
            {
                return Instance;
            }

            // Run -> Run can install the new world after the old RunDirector was disabled
            // but before this director receives OnDisable. The composition root identifies
            // that orphan and requests takeover; the old OnDisable cannot clear the new slot.
            Instance = null;

            var go = new GameObject("EnemySpawnDirector");
            return go.AddComponent<EnemySpawnDirector>();
        }

        /// <summary>
        /// Starts a round: clears the previous round's bodies, resets point state, opens
        /// the ledger from the round plan and arms the refill. Safe to call at any time;
        /// it also acts as "skip to round N".
        /// </summary>
        public void BeginRound(int round)
        {
            _round = Mathf.Max(1, round);
            _everStarted = true;

            // Bodies of the previous round go back first, so their despawn reports land
            // on the OLD ledger (or nowhere), never on the round about to open.
            ClearAllEnemies();

            SpawnPointRegistry.EnsureBuilt(_layout);
            SpawnPointRegistry.ResetRuntimeState();
            SpawnWorldProfile.Refresh();   // WP-076 B6: v6 values only while a v6 world scene is loaded (C-00 A)
            _v6ConfigSource = null;

            RoundPlan plan = RoundPlan.Resolve(HasConfig ? Config : null, _round);
            _ledger.BeginRound(plan.Round, plan.PoolTotal, plan.TargetActive, plan.Ceiling);

            _refillClock.Reset();
            _pathCursor = 0;
            _cooldownRelaxCount = 0;
            _pathRelaxCount = 0;
            _noPointTicks = 0;
            _pathPendingTicks = 0;

            _tierSelector.BeginRound(_round, Config, plan.PoolTotal, _weightTuning.GuaranteedIntroPoolFraction);

            EnsurePool();
            if (_pool != null)
            {
                _pool.Prewarm(Mathf.Min(plan.Ceiling, EffectiveTarget() + 4));
            }

            RefreshAllPoints(force: true);

            _ledger.Activate();
        }

        /// <summary>
        /// The run ended under this round (death, quit, debug). Spawning stops, the round
        /// can never complete, and the bodies on the map are left where they are for the
        /// results screen. Idempotent.
        /// </summary>
        public void StopRound()
        {
            _ledger.Abort();
            _tierSelector.Clear();   // WP-010: an owed debut / forced count never survives the round it belonged to
        }

        /// <summary>Returns every body this director owns to the pool. Used by the F1 panel and by BeginRound.</summary>
        public void ClearAllEnemies()
        {
            // Despawn → OnDespawned → NotifyDespawned → RegisterDespawn(ticket) removes the
            // entry re-entrantly, so the loop always works on the LAST entry and never
            // assumes an index survives the call. A body that does not report (destroyed,
            // foreign prefab) is reconciled and removed here instead.
            int guard = _active.Count + 1;
            while (_active.Count > 0 && guard-- > 0)
            {
                ActiveEnemy entry = _active[_active.Count - 1];

                if (entry.Behaviour != null && entry.Behaviour.IsSpawned)
                {
                    entry.Behaviour.Despawn();
                }

                if (_active.Count > 0 && _active[_active.Count - 1].Ticket == entry.Ticket)
                {
                    _active.RemoveAt(_active.Count - 1);
                    _ledger.Reconcile(entry.Ticket);
                }
            }

            _active.Clear();

            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            for (int i = 0; i < points.Count; i++)
            {
                points[i].AliveFromHere = 0;
            }
        }

        /// <summary>
        /// Debug only (F1 "Complete this round"): drains the pool and clears the map, then
        /// lets the ledger complete the round exactly as it would in play, so the same
        /// RoundPoolExhausted → RoundCleared pair reaches the run director. Returns false
        /// when no round is live.
        /// </summary>
        public bool DebugCompleteRound()
        {
            if (!_ledger.IsRoundLive)
            {
                return false;
            }

            _forcedTargetActive = 0;
            _ledger.DebugIgnorePool = false;
            _ledger.DebugDrainPool();

            ClearAllEnemies();

            _ledger.Evaluate();
            return _ledger.TryComplete();
        }

        /// <summary>
        /// Debug only: pin the active target (0 = back to the round's own target). Values
        /// above the safety ceiling are clamped to it, so "force 40" really means 40.
        /// </summary>
        public void ForceActiveTarget(int target, bool ignorePoolLimit)
        {
            _forcedTargetActive = Mathf.Clamp(target, 0, _ledger.Ceiling);
            _ledger.DebugIgnorePool = ignorePoolLimit && _forcedTargetActive > 0;

            RoundPlan plan = RoundPlan.Resolve(HasConfig ? Config : null, _round);
            _ledger.Retarget(_forcedTargetActive > 0 ? _forcedTargetActive : plan.TargetActive, _ledger.Ceiling);
        }

        // ------------------------------------------------------------------ lifecycle sink

        void IEnemyLifecycleSink.NotifyDied(int ticket)
        {
            // Idempotent by construction: a second report for the same ticket is refused
            // by the ledger. The state transition it may enable is evaluated in Update,
            // never here - this is called from inside the enemy's damage path.
            if (!_ledger.RegisterDeath(ticket))
            {
                return;
            }

            // Weighting input 7 counts LIVING enemies that came from a point; a corpse
            // no longer presses on its gate.
            DecrementPoint(PointIndexOf(ticket));
        }

        void IEnemyLifecycleSink.NotifyDespawned(int ticket)
        {
            // A body removed without dying (round cleanup) still owes its point the
            // decrement a death would have made.
            bool wasAlive = _ledger.IsAlive(ticket);

            if (!_ledger.RegisterDespawn(ticket))
            {
                // A ticket from a previous round (cleanup order) or a repeat. The entry, if
                // any, is dropped without touching the counters.
                RemoveActiveEntry(ticket, decrementPoint: false);
                return;
            }

            RemoveActiveEntry(ticket, decrementPoint: wasAlive);
            RaiseRetired();
        }

        // ------------------------------------------------------------------ unity

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                NavigationDiagnostics.WarnOnce(
                    "spawn.duplicate",
                    "[Spawn] A second EnemySpawnDirector was found. Only the first one drives the round.");
                enabled = false;
                return;
            }

            Instance = this;
            SpawnPointRegistry.EnsureBuilt(_layout);

            _ledger.StateChanged += OnLedgerStateChanged;
            _ledger.PoolDepleted += OnLedgerPoolDepleted;
            _ledger.RoundCompleted += OnLedgerRoundCompleted;
        }

        private void OnDestroy()
        {
            _ledger.StateChanged -= OnLedgerStateChanged;
            _ledger.PoolDepleted -= OnLedgerPoolDepleted;
            _ledger.RoundCompleted -= OnLedgerRoundCompleted;
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (Instance == null)
            {
                Instance = this;
            }

            // The spawn layer reads the player position and heading out of the crowd
            // runtime, so it must exist even in a scene that has no enemies yet.
            NavigationSystemBehaviour.EnsureExists();

            // WP-010: the enemy task's F1 tab reads the tier authority through this bridge
            // (Enemies/ keeps its isolation boundary; the authority stays here).
            if (Instance == this)
            {
                _bridgeRound = () => _round;
                _bridgeDraw = config => _tierSelector.DrawWeighted(config);
                _bridgeDescribe = () => _tierSelector.Describe();
                EnemyTierAuthorityBridge.Bind(_bridgeRound, _bridgeDraw, _bridgeDescribe);
            }

            RegisterDebugPanel();
        }

        protected override void OnDisable()
        {
            UnregisterDebugPanel();

            if (Instance == this)
            {
                Instance = null;
            }

            EnemyTierAuthorityBridge.UnbindIf(_bridgeRound, _bridgeDraw, _bridgeDescribe);

            base.OnDisable();
        }

        /// <summary>
        /// Live config contract. The refill cadence, the target and the ceiling are
        /// re-read here, so changing them in the F1 panel mid-round is felt on the very
        /// next refill tick. The round's pool total is fixed at BeginRound (the ledger
        /// owns it); a new pool curve applies from the next round.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            _spawnConfig = config.Spawn;
            _refillInterval = Mathf.Max(0.05f, config.Rounds.SpawnRefillInterval);
            _maxSpawnsPerTick = Mathf.Max(1, config.Rounds.MaxSpawnsPerRefillTick);

            _weightTuning.Sanitize();

            RoundPlan plan = RoundPlan.Resolve(config, _round);
            _ledger.Retarget(_forcedTargetActive > 0 ? _forcedTargetActive : plan.TargetActive, plan.Ceiling);

            if (_pool != null && _pool.Capacity < plan.Ceiling)
            {
                _pool.SetCapacity(plan.Ceiling);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Pause freezes the spawn flow with everything else (REQ-006): nothing below
            // this line advances on a frozen clock, including the refill metronome.
            if (dt <= 0f)
            {
                return;
            }

            // Runs in every state: a body that vanished after the run ended (results
            // scene, teardown) must still leave the roster, or the next BeginRound's
            // cleanup pass would try to despawn a destroyed object.
            Housekeeping();

            if (!_ledger.IsRoundLive)
            {
                if (_autoStartFirstRound && !_everStarted)
                {
                    _autoStartTimer += dt;
                    if (_autoStartTimer >= _autoStartDelay)
                    {
                        BeginRound(1);
                    }
                }

                return;
            }

            if (_ledger.State == RoundState.Active && _refillClock.Advance(dt, _refillInterval))
            {
                RefillTick();
            }

            _ledger.Evaluate();
            _ledger.TryComplete();
        }

        // ------------------------------------------------------------------ flow

        /// <summary>
        /// Safety net, not the contract: a body destroyed behind the pool's back (scene
        /// teardown) never reports its despawn. Each such case is reconciled and counted.
        /// </summary>
        private void Housekeeping()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveEnemy entry = _active[i];
                if (entry.Behaviour != null && entry.Behaviour.IsSpawned)
                {
                    continue;
                }

                _active.RemoveAt(i);

                if (_ledger.IsAlive(entry.Ticket))
                {
                    DecrementPoint(entry.PointIndex);
                }

                if (_ledger.Reconcile(entry.Ticket))
                {
                    RaiseRetired();
                }
            }
        }

        private void RefillTick()
        {
            if (!_spawningEnabled)
            {
                return;
            }

            RefreshStalePoints(_weightTuning.PathQueriesPerTick);

            int spawned = 0;

            // Debug only: the forced-40 measurement would take 9 s to fill at the normal
            // 2 per tick, which is most of a measurement window spent ramping.
            int perTick = _forcedTargetActive > 0 ? Mathf.Max(_maxSpawnsPerTick, 4) : _maxSpawnsPerTick;

            while (spawned < perTick && _ledger.CanSpawn(out _))
            {
                if (!TrySpawnOne())
                {
                    break;
                }

                spawned++;
            }
        }

        private int EffectiveTarget()
        {
            int target = _forcedTargetActive > 0 ? _forcedTargetActive : _ledger.TargetActive;
            return Mathf.Clamp(target, 0, _ledger.Ceiling);
        }

        private void OnLedgerStateChanged(RoundState state)
        {
            RoundStateChanged?.Invoke(state);
        }

        private void OnLedgerPoolDepleted(int round)
        {
            RoundPoolExhausted?.Invoke(round);
        }

        private void OnLedgerRoundCompleted(int round)
        {
            LastCompletedRound = _ledger.Snapshot();
            RoundCleared?.Invoke(round);
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>
        /// One registration → activation attempt. Every failure path restores the ledger:
        /// a full pool is "not now", a dead-on-arrival enemy is an activation failure that
        /// is counted, reported and rolled back, never a silent loss from the pool.
        /// </summary>
        private bool TrySpawnOne()
        {
            if (_pool == null)
            {
                EnsurePool();
                if (_pool == null)
                {
                    NavigationDiagnostics.WarnThrottled(
                        "spawn.noprefab", 30f,
                        "[Spawn] No enemy prefab assigned on the EnemySpawnDirector yet, so nothing " +
                        "can spawn. This is expected until the enemy task (G-04) delivers its prefab.");
                    return false;
                }
            }

            // WP-050 (S-10 finding "[Spawn] No spawn point had a complete NavMesh path"): a point that was never
            // path-queried against a player is PENDING, not unreachable. At round start BeginRound's forced
            // refresh runs before the player state may exist (NavigationSystemBehaviour.Update publishes it),
            // which left every point Invalid with a fresh cache stamp and let the ladder relax reachability
            // on the first tick. Query the pending ones now; if there is still no player, wait a tick.
            if (!AllPathsQueried)
            {
                RefreshPendingPoints();
                if (!AllPathsQueried)
                {
                    _pathPendingTicks++;
                    return false;
                }
            }

            SpawnPointRuntime point = SelectPoint();
            if (point == null)
            {
                _noPointTicks++;
                NavigationDiagnostics.WarnThrottled(
                    "spawn.nopoint", 5f,
                    "[Spawn] No spawn point passed the weighting this tick (all too close, on screen, " +
                    "cooling down or unreachable). Skipping this refill.");
                return false;
            }

            Vector3 position = ScatterAround(point);
            Quaternion rotation = point.SpawnRotation();

            PooledBehaviour instance = _pool.Get(position, rotation);
            if (instance == null)
            {
                // ReturnNull policy: the pool is full (debug bodies share it). Not an
                // error, just a full map, and nothing was reserved yet.
                return false;
            }

            if (!_ledger.TryReserveSpawn(out int ticket))
            {
                // CanSpawn was true a moment ago; a re-entrant report may have changed it.
                instance.Despawn();
                return false;
            }

            // WP-010: the ledger already counts this reservation (SpawnedTotal includes it,
            // Unspawned excludes it); the policy wants "released before" and "slots left
            // including this one". A pick whose activation fails below is handed back so an
            // owed debut is never lost to a rollback (ADR-010-2).
            TierPick pick = _tierSelector.Next(Config, _ledger.SpawnedTotal - 1, _ledger.Unspawned + 1);
            EnemyTier tier = pick.Tier;
            bool guaranteed = pick.Guaranteed;

            // WP-076 B6 §5.1 tier path gate: an Elite / Ultra only from ≤ 55 m of path, a Special ≤ 75 m. The point was drawn
            // before the tier (the ledger order stays as it was), so a heavy pick on a far point moves the body to the best
            // point that passes the gate; none → the drawn point (a guaranteed debut is never lost to the gate).
            float gate = SpawnModeTable.TierPathMax(tier);
            if (gate > 0f && point.PathDistance > gate)
            {
                SpawnPointRuntime nearer = SelectPoint(gate);
                if (nearer != null && nearer != point)
                {
                    point = nearer;
                    position = ScatterAround(point);
                    rotation = point.SpawnRotation();
                    CharacterController cc = instance.GetComponent<CharacterController>();
                    bool ccOn = cc != null && cc.enabled;
                    if (ccOn) cc.enabled = false;
                    instance.transform.SetPositionAndRotation(position, rotation);
                    if (ccOn) cc.enabled = true;
                    TierGateReroutes++;
                }
                else TierGateFallbacks++;
            }

            EnemySpawnContext context = BuildContext(tier, point, position, rotation, guaranteed, ticket);

            var spawned = instance as ISpawnedEnemy;
            if (spawned == null)
            {
                NavigationDiagnostics.WarnOnce(
                    "spawn.contract",
                    "[Spawn] The enemy prefab does not implement ISpawnedEnemy, so tier, health, " +
                    "damage and the lifecycle ticket could not be handed over. The body was " +
                    "returned and the spawn rolled back. See ISpawnedEnemy for the contract.");
                instance.Despawn();
                _ledger.RollbackSpawn(ticket);
                _tierSelector.Return(in pick);
                return false;
            }

            spawned.OnSpawnedFromPoint(in context);

            if (!spawned.IsAlive)
            {
                NavigationDiagnostics.WarnThrottled(
                    "spawn.activation", 5f,
                    "[Spawn] An enemy came back from activation not alive (initialise failed). " +
                    "The body was returned and the pool stock restored; see Population.ActivationFailures.");

                // Roll back FIRST: the enemy already holds the sink, so Despawn() reports
                // the ticket, and the ledger must not mistake that for a normal despawn.
                // (The ledger also treats a despawn of an unconfirmed ticket as a rollback,
                // so either order restores the stock; this order keeps the readout exact.)
                _ledger.RollbackSpawn(ticket);
                instance.Despawn();
                _tierSelector.Return(in pick);
                return false;
            }

            _ledger.ConfirmActivation(ticket);
            _active.Add(new ActiveEnemy { Behaviour = instance, Ticket = ticket, PointIndex = point.Index });

            point.AliveFromHere++;
            point.SpawnedThisRound++;
            point.LastUsedTime = Time.time;
            RearSpawnShare.Record(point.IsBehindCamera);   // WP-076 §5.1
            if (SpawnWorldProfile.IsV6) SpawnWorldProfile.RecordSpawn(point.Definition != null ? point.Definition.Kind : SpawnPointKind.Edge);   // WP-076 B6
            SpawnRoomBalance.Record(point);   // WP-076 B6c: door-side memory while the player is in a room
            // WP-030 spawn visibility readout (DEC-023 counting is unchanged: the body is active from activation, rise included).
            _spawnsThisRun++;
            if (point.IsOnScreen) _onScreenSpawnsThisRun++;
            // WP-040 (DEC-081) rise readout: counted on the confirmed activation only.
            if (context.Visibility != SpawnVisibility.Unknown) _offScreenSpawnsJudged++;
            if (context.Visibility == SpawnVisibility.OffScreen) _riseEligibleSpawnsThisRun++;

            Action<EnemySpawnContext> spawnedHandler = EnemySpawned;
            if (spawnedHandler != null)
            {
                try
                {
                    spawnedHandler.Invoke(context);
                }
                catch (Exception e)
                {
                    // A broken listener must not undo a spawn that already happened.
                    Debug.LogException(e, this);
                }
            }

            return true;
        }

        /// <summary>
        /// EnemyRetired is raised from inside the pool's return path; a throwing listener
        /// would leave the body half-returned, so it is isolated here.
        /// </summary>
        private void RaiseRetired()
        {
            Action handler = EnemyRetired;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private EnemySpawnContext BuildContext(EnemyTier tier, SpawnPointRuntime point,
            Vector3 position, Quaternion rotation, bool guaranteed, int ticket)
        {
            float health = 0f;
            float damage = 0f;
            float speed = 0f;

            if (HasConfig)
            {
                health = Config.EnemyHealth(tier, _round);
                damage = Config.EnemyAttackDamage(tier, _round);

                EnemyTierSettings settings = Config.Enemies.Find(tier);
                speed = settings != null ? settings.MoveSpeed : 0f;
            }

            SpawnVisibility visibility = JudgeVisibility(position);   // WP-040 (DEC-081): fresh, at the final position

            return new EnemySpawnContext(tier, _round, point.Index, point.Id, point.Region,
                position, rotation, health, damage, speed, guaranteed, ticket, this, visibility);
        }

        /// <summary>
        /// The only place a spawn position is computed: the chosen point's NavMesh
        /// position plus a bounded scatter (SpawnScatterRadius, so two enemies never spawn
        /// inside each other), re-projected onto the NavMesh. It is never a coordinate the
        /// designer did not author a point for (REQ-065).
        /// </summary>
        private Vector3 ScatterAround(SpawnPointRuntime point)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * _weightTuning.SpawnScatterRadius;
            Vector3 candidate = point.NavPosition + new Vector3(offset.x, 0f, offset.y);

            if (CrowdService.TryProjectToNavMesh(candidate, 2f, out Vector3 onMesh))
            {
                return onMesh;
            }

            // The scattered candidate is off the mesh: fall back to the point itself, not
            // to an unprojected coordinate.
            return point.NavPosition;
        }

        private int PointIndexOf(int ticket)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Ticket == ticket)
                {
                    return _active[i].PointIndex;
                }
            }

            return -1;
        }

        private void RemoveActiveEntry(int ticket, bool decrementPoint)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].Ticket != ticket)
                {
                    continue;
                }

                int pointIndex = _active[i].PointIndex;
                _active.RemoveAt(i);

                if (decrementPoint)
                {
                    DecrementPoint(pointIndex);
                }

                return;
            }
        }

        private static void DecrementPoint(int pointIndex)
        {
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            if (pointIndex >= 0 && pointIndex < points.Count)
            {
                points[pointIndex].AliveFromHere = Mathf.Max(0, points[pointIndex].AliveFromHere - 1);
            }
        }

        // ------------------------------------------------------------------ selection

        // WP-076 B6 world profile: a per-round copy of the config with the v6 distances (World_AP005 uses _spawnConfig as is)
        private SpawnConfigSection _v6Config, _v6ConfigSource;
        private SpawnConfigSection EffectiveSpawnConfig
        {
            get
            {
                if (!SpawnWorldProfile.IsV6 || _spawnConfig == null) return _spawnConfig;
                if (_v6Config == null || _v6ConfigSource != _spawnConfig)
                {
                    _v6Config = JsonUtility.FromJson<SpawnConfigSection>(JsonUtility.ToJson(_spawnConfig));   // once per round / config change
                    SpawnWorldProfile.ApplyV6(_v6Config);
                    _v6ConfigSource = _spawnConfig;
                }
                return _v6Config;
            }
        }

        /// <summary>WP-076 B6 readouts: heavy / Special spawns moved to a nearer point by the tier path gate, and those that found none.</summary>
        public int TierGateReroutes { get; private set; }
        public int TierGateFallbacks { get; private set; }
        /// <summary>WP-076 B9: picks that needed the inner-cap safety valve (<see cref="SpawnRelaxation.InnerCap"/>).</summary>
        public int InnerCapFallbacks { get; private set; }

        private SpawnPointRuntime SelectPoint(float maxPathDistance = 0f)
        {
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;

            if (points.Count == 0 || _spawnConfig == null)
            {
                return null;
            }

            CrowdPlayerState player = CrowdService.PlayerState;
            if (!player.HasPlayer)
            {
                NavigationDiagnostics.WarnThrottled(
                    "spawn.noplayer", 10f,
                    "[Spawn] No player registered with CrowdService, so spawn distance and heading " +
                    "cannot be evaluated. Call CrowdService.SetPlayer from the player controller " +
                    "or tag the player GameObject 'Player'.");
                return null;
            }

            var inputs = new SpawnWeightInputs
            {
                PlayerPosition = player.Position,
                PlayerRegion = RingRegionMap.Classify(player.Position),
                PlayerVelocity = player.Velocity,
                Now = Time.time,
                NavDegraded = !CrowdService.IsNavMeshAvailable,
                ThreatLayer = ThreatLevelFeed.Layer,   // WP-054: threat-aware visibility weight
                CameraMode = SpawnModeTable.Mode,       // WP-076 B6: TP / FP rows
                MaxPathDistance = maxPathDistance,      // WP-076 B6: tier path gate
                InnerShare = SpawnWorldProfile.IsV6 ? SpawnWorldProfile.SteeringShare : -1f,
                InnerQuotaFull = SpawnWorldProfile.IsV6 && !SpawnWorldProfile.CanRecordInner   // WP-076: steered on the prior-smoothed share
            };

            // WP-076 §5.1: rear-hemisphere share cap (FP 35 % / TP 45 % of the last 8 spawns)
            {
                bool otherReady = false;
                for (int i = 0; i < points.Count && !otherReady; i++)
                    otherReady = !points[i].IsBehindCamera && points[i].PathStatus == NavPathStatus.Complete;
                inputs.RearSpawnPenalty = RearSpawnShare.Penalty(RearSpawnShare.CurrentMode, otherReady);
            }

            UpdatePointDensity(points);
            SpawnRoomBalance.Prepare(points, player.Position, SpawnWorldProfile.IsV6);   // WP-076 B6c §5.3 (v6 rooms only; factors 1 otherwise)

            // Strict first, then drop one gate at a time. Anything past the first pass is
            // rare and is reported, because it means the map or the tuning is squeezing the
            // director into a corner.
            SpawnPointRuntime chosen = _selector.SelectWithLadder(points, in inputs, EffectiveSpawnConfig, _weightTuning,
                _random01, out SpawnRelaxation used);

            if (chosen == null)
            {
                return null;
            }

            if ((used & SpawnRelaxation.InnerCap) != 0) InnerCapFallbacks++;   // WP-076 B9 safety valve (C-00 30.09)

            if ((used & SpawnRelaxation.Cooldown) != 0)
            {
                // Expected, not exceptional: 12 points with a 4 s cooldown serve 3 spawns/s,
                // while a full refill tick wants up to 4.4/s. When the demand outruns the
                // points, the cooldown becomes a preference instead of a gate and the COOLEST
                // point wins through the recency factor. Counted, not logged.
                _cooldownRelaxCount++;
            }

            if ((used & SpawnRelaxation.Reachability) != 0)
            {
                NavigationDiagnostics.WarnThrottled(
                    "spawn.relax.path", 15f,
                    "[Spawn] No spawn point had a complete NavMesh path to the player (REQ-068) although every point was queried this round. " +
                    "The reachability gate was relaxed so the round can continue - check the NavMesh bake / the player's position on the mesh.");
                _pathRelaxCount++;
            }

            return chosen;
        }

        private void UpdatePointDensity(IReadOnlyList<SpawnPointRuntime> points)
        {
            float radius = _spawnConfig != null ? _spawnConfig.LocalDensityRadius : 9f;

            for (int i = 0; i < points.Count; i++)
            {
                points[i].LocalDensity = CrowdService.CountMembersWithin(points[i].WorldPosition, radius);
            }
        }

        // ------------------------------------------------------------------ path cache

        private void RefreshAllPoints(bool force)
        {
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            for (int i = 0; i < points.Count; i++)
            {
                RefreshPoint(points[i], force);
            }
        }

        /// <summary>
        /// Test / probe hook: re-query every point's path against the player right now,
        /// so a teleported player is reflected in the weights without waiting for the
        /// cache interval.
        /// </summary>
        public void RefreshPointsNow()
        {
            RefreshAllPoints(force: true);
        }

        /// <summary>
        /// Re-queries a few points per refill tick instead of all twelve every frame:
        /// NavMesh.CalculatePath is the single most expensive call in this system.
        /// </summary>
        /// <summary>WP-050: query every point that has no path result yet (round start, or a player that registered late). Bounded by the point count, once per round in practice.</summary>
        private void RefreshPendingPoints()
        {
            if (!CrowdService.PlayerState.HasPlayer) return;
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].PathCachedAt < 0f) RefreshPoint(points[i], force: true);
            }
        }

        private void RefreshStalePoints(int budget)
        {
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            int count = points.Count;
            if (count == 0)
            {
                return;
            }

            float now = Time.time;
            int refreshed = 0;

            for (int step = 0; step < count && refreshed < budget; step++)
            {
                SpawnPointRuntime point = points[_pathCursor];
                _pathCursor = (_pathCursor + 1) % count;

                if (now - point.PathCachedAt < _weightTuning.PathCacheInterval)
                {
                    continue;
                }

                RefreshPoint(point, force: false);
                refreshed++;
            }
        }

        private void RefreshPoint(SpawnPointRuntime point, bool force)
        {
            CrowdPlayerState player = CrowdService.PlayerState;
            if (!player.HasPlayer && !force)
            {
                return;
            }

            if (CrowdService.TryProjectToNavMesh(point.WorldPosition, 3f, out Vector3 onMesh))
            {
                point.NavPosition = onMesh;
            }
            else
            {
                point.NavPosition = point.WorldPosition;
            }

            if (player.HasPlayer)
            {
                CrowdService.TryPathDistance(point.NavPosition, player.Position,
                    out float distance, out NavPathStatus status);

                point.PathDistance = distance;
                point.PathStatus = status;
                point.StraightDistance = NavPathService.FlatDistance(point.NavPosition, player.Position);
                point.PathCachedAt = Time.time;   // WP-050: a cache stamp means "queried against a player"; no player = still pending
            }

            UpdateScreenFlags(point);
        }

        private void UpdateScreenFlags(SpawnPointRuntime point)
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                point.IsOnScreen = false;
                point.IsBehindCamera = false;
                return;
            }

            Vector3 viewport = cam.WorldToViewportPoint(point.NavPosition);
            bool behind = viewport.z <= 0f;

            point.IsBehindCamera = behind;
            if (SpawnModeTable.Mode != 0 && DD.Prototype.CameraSystem.PrototypeCameraRig.Active != null)
            {
                // WP-076 B6 §5.1 TP / FP: on screen = inside the view frustum AND not behind a wall (one Linecast per point
                // per path refresh — the PathCacheInterval cadence). A mid gate behind a wall 20 m away is off screen.
                Vector3 chest = point.NavPosition + Vector3.up * SpawnModeTable.ChestHeight;
                point.IsOnScreen = !behind && DD.Prototype.CameraSystem.CameraView.IsInView(chest, 0f)
                                   && !Physics.Linecast(DD.Prototype.CameraSystem.CameraView.Position, chest, 1 << PrototypeLayers.Environment, QueryTriggerInteraction.Ignore);
                return;
            }
            point.IsOnScreen = !behind &&
                               viewport.x >= -0.05f && viewport.x <= 1.05f &&
                               viewport.y >= -0.05f && viewport.y <= 1.05f;
        }

        private Camera ResolveCamera()
        {
            if (_camera != null)
            {
                return _camera;
            }

            _cameraSearchTimer -= Time.deltaTime;
            if (_cameraSearchTimer > 0f)
            {
                return null;
            }

            _cameraSearchTimer = 1f;
            _camera = Camera.main;
            return _camera;
        }

        private void EnsurePool()
        {
            if (_pool != null || _enemyPrefab == null)
            {
                return;
            }

            int ceiling = _ledger.Ceiling;
            _pool = PoolService.GetOrCreate(
                _enemyPrefab,
                Mathf.Min(ceiling, 12),
                ceiling,
                PoolExhaustedPolicy.ReturnNull);
        }

        // ------------------------------------------------------------------ debug panel

        private void RegisterDebugPanel()
        {
            var entries = new List<DebugPanelEntry>(16);

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Force Spawn to 40 (perf measurement)",
                () =>
                {
                    if (!_ledger.IsRoundLive)
                    {
                        BeginRound(_round);
                    }

                    ForceActiveTarget(_ledger.Ceiling, true);
                }, -100));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Release forced target",
                () => ForceActiveTarget(0, false), -99));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Clear All Enemies",
                ClearAllEnemies, -98));

            entries.Add(DebugPanelRegistry.AddToggle(DebugPanelTabs.Spawn,
                "Spawning enabled",
                () => _spawningEnabled,
                v => _spawningEnabled = v, -97));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Skip to Round 3 (Runner debut)",
                () => BeginRound(3), -90));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Skip to Round 5 (Guard debut)",
                () => BeginRound(5), -89));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Skip to Round 8 (Executioner debut)",
                () => BeginRound(8), -88));

            entries.Add(DebugPanelRegistry.AddSlider(DebugPanelTabs.Spawn,
                "Skip target round",
                () => _debugRoundTarget,
                v => _debugRoundTarget = Mathf.Max(1, Mathf.RoundToInt(v)),
                1f, 25f, true, -87));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Spawn,
                "Skip to Round N",
                () => BeginRound(_debugRoundTarget), -86));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Spawn,
                "Round flow", DescribeFlow, -80));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Spawn,
                "Tier debut owed", () => _tierSelector.DescribePending(), -79));

            entries.Add(DebugPanelRegistry.AddSection(DebugPanelTabs.Spawn,
                "Spawn point weights (live)", DrawWeightTable, -70));

            _debugEntries = entries.ToArray();
        }

        private void UnregisterDebugPanel()
        {
            for (int i = 0; i < _debugEntries.Length; i++)
            {
                DebugPanelRegistry.Remove(_debugEntries[i]);
            }

            _debugEntries = Array.Empty<DebugPanelEntry>();
        }

        /// <summary>One-line round readout. Only evaluated while the F1 panel draws it (or by a probe).</summary>
        public string DescribeFlow()
        {
            RoundPopulationSnapshot p = _ledger.Snapshot();
            return string.Format(
                "R{0} {1} | pool {2}/{3} left | alive {4}/{5} bodies {6}/{7}{8} | spawned {9} killed {10} | fail {11} rec {12} | relax cd:{13} path:{14} nopoint:{15}",
                p.Round,
                p.State,
                p.Unspawned,
                p.PoolTotal,
                p.Alive,
                EffectiveTarget(),
                p.Occupied,
                p.Ceiling,
                _forcedTargetActive > 0 ? " FORCED" : "",
                p.SpawnedTotal,
                p.Killed,
                p.ActivationFailures,
                p.Reconciled,
                _cooldownRelaxCount,
                _pathRelaxCount,
                _noPointTicks);
        }

        /// <summary>
        /// Live readout of the nine weighting inputs per point. Rows are rebuilt at 4 Hz
        /// and reused, so opening the panel does not allocate a string per point per frame.
        /// </summary>
        private void DrawWeightTable()
        {
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            int count = points.Count;

            if (count == 0)
            {
                GUILayout.Label("No spawn points built yet.");
                return;
            }

            if (_debugRows.Length < count)
            {
                _debugRows = new string[Mathf.NextPowerOfTwo(count)];
            }

            if (Event.current == null || Event.current.type == EventType.Layout)
            {
                _debugRefreshTimer -= Time.unscaledDeltaTime;
                if (_debugRefreshTimer <= 0f)
                {
                    _debugRefreshTimer = 0.25f;
                    RebuildDebugRows(points, count);
                }
            }

            GUILayout.Label(string.Format("{0} points, {1} bound by map markers, NavMesh {2}",
                count, SpawnPointRegistry.MarkerBoundCount,
                CrowdService.IsNavMeshAvailable ? "OK" : "DEGRADED"));

            for (int i = 0; i < count; i++)
            {
                GUILayout.Label(_debugRows[i] ?? string.Empty);
            }
        }

        private void RebuildDebugRows(IReadOnlyList<SpawnPointRuntime> points, int count)
        {
            for (int i = 0; i < count; i++)
            {
                SpawnPointRuntime p = points[i];
                _debugRows[i] = string.Format(
                    "{0,-22} {1} w={2,6:0.000} path={3,5:0.0}m {4,-8} alive={5} near={6} cd={7,4:0.0}s {8}",
                    p.Id,
                    RingRegionMap.ToShortLabel(p.Region),
                    p.LastWeight,
                    p.PathDistance,
                    p.PathStatus,
                    p.AliveFromHere,
                    p.LocalDensity,
                    Mathf.Max(0f, (_spawnConfig != null ? _spawnConfig.PointCooldown : 0f) -
                                  (Time.time - p.LastUsedTime)),
                    p.LastWeight > 0f ? "" : "[" + p.LastRejectReason + "]");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        // ------------------------------------------------------------------ gizmos

        private void OnDrawGizmos()
        {
            if (!_drawGizmos)
            {
                return;
            }

            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;

            if (points.Count == 0)
            {
                // Editor preview of the authored layout before play mode.
                List<SpawnPointDefinition> preview = _layout != null && _layout.Count > 0
                    ? null
                    : DefaultSpawnPointLayout.Create();

                if (preview != null)
                {
                    Gizmos.color = new Color(0.9f, 0.6f, 0.3f, 0.6f);
                    for (int i = 0; i < preview.Count; i++)
                    {
                        Gizmos.DrawWireSphere(preview[i].Position, 1.2f);
                    }
                }

                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                SpawnPointRuntime p = points[i];
                float weight = Mathf.Clamp01(p.LastWeight * 0.5f);

                Gizmos.color = p.LastWeight > 0f
                    ? new Color(0.2f + weight, 0.9f, 0.3f, 0.8f)
                    : new Color(0.8f, 0.25f, 0.25f, 0.5f);

                Gizmos.DrawWireSphere(p.WorldPosition, 1.2f + weight * 1.5f);
                Gizmos.DrawLine(p.WorldPosition, p.WorldPosition + Vector3.up * 3f);
            }
        }
    }
}
