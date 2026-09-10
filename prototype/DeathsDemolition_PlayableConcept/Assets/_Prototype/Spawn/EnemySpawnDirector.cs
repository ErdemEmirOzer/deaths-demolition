using System;
using System.Collections.Generic;
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
    ///   REQ-065  enemies only ever leave from designer-placed points;
    ///   REQ-066  point density is dynamic; a pointless point drops to zero weight;
    ///   REQ-067  the nine approved positional inputs (see <see cref="SpawnWeightCalculator"/>);
    ///   REQ-068  a point without a COMPLETE walkable path is not used;
    ///   REQ-069  no spawn next to the player, on screen up close, or behind the camera;
    ///   REQ-070  cooldown, recency, per-point occupancy and local density spread the load;
    ///   REQ-071  Runner R3 / Guard R5 / Executioner R8 debut guaranteed, then weighted;
    ///   REQ-072  keeps the round's target active count and NEVER exceeds the ceiling of 40.
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
    /// tick simply stops spawning - the 40 ceiling can never be crossed by construction.
    ///
    /// DEATH DETECTION
    /// ---------------
    /// No event contract with the enemy: an enemy that dies calls Despawn(), the pooled
    /// instance flips IsSpawned to false, and housekeeping notices on the next tick. That
    /// is what keeps this task and the enemy task free of a hard type dependency.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnDirector : PrototypeConfigBehaviour
    {
        private struct ActiveEnemy
        {
            public PooledBehaviour Behaviour;
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
        private readonly SpawnTierSelector _tierSelector = new SpawnTierSelector();
        private float[] _pointWeights = new float[16];
        private string[] _debugRows = new string[16];

        private PrototypePool<PooledBehaviour> _pool;
        private Camera _camera;
        private float _cameraSearchTimer;

        private float _refillTimer;
        private float _autoStartTimer;
        private int _pathCursor;
        private float _debugRefreshTimer;
        private int _debugRoundTarget = 3;

        private bool _roundActive;
        private bool _everStarted;
        private bool _poolExhaustedAnnounced;

        private int _round = 1;
        private int _remainingInPool;
        private int _spawnedThisRound;
        private int _cooldownRelaxCount;
        private int _pathRelaxCount;

        // cached from config
        private SpawnConfigSection _spawnConfig;
        private float _refillInterval = 0.45f;
        private int _maxSpawnsPerTick = 2;
        private int _ceiling = 40;
        private int _targetActive = 9;

        // debug overrides
        private int _forcedTargetActive;
        private bool _debugIgnorePoolLimit;
        private bool _spawningEnabled = true;

        private DebugPanelEntry[] _debugEntries = Array.Empty<DebugPanelEntry>();

        // ------------------------------------------------------------------ public API

        public static EnemySpawnDirector Instance { get; private set; }

        /// <summary>Fired after an enemy left the pool and was configured.</summary>
        public event Action<EnemySpawnContext> EnemySpawned;

        /// <summary>Fired when a spawned enemy returned to the pool (death or cleanup).</summary>
        public event Action EnemyRetired;

        /// <summary>Fired once per round when the last enemy of the pool has been released.</summary>
        public event Action<int> RoundPoolExhausted;

        /// <summary>Fired when the pool is empty AND the map is clear. The round is over.</summary>
        public event Action<int> RoundCleared;

        public int Round => _round;
        public int RemainingInPool => _remainingInPool;
        public int ActiveCount => _active.Count;
        public int TargetActive => EffectiveTarget();
        public int SafetyCeiling => _ceiling;
        public bool IsRoundActive => _roundActive;
        public bool IsForcedTarget => _forcedTargetActive > 0;

        /// <summary>Total pool of the round, straight from config.TotalEnemyPool(round).</summary>
        public int RoundPoolTotal { get; private set; }

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
        public static EnemySpawnDirector EnsureExists()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("EnemySpawnDirector");
            return go.AddComponent<EnemySpawnDirector>();
        }

        /// <summary>
        /// Starts a round: resets point state, sizes the pool from config and begins the
        /// uninterrupted refill. Safe to call at any time; it also acts as "skip to round N".
        /// </summary>
        public void BeginRound(int round)
        {
            _round = Mathf.Max(1, round);
            _everStarted = true;
            _roundActive = true;
            _poolExhaustedAnnounced = false;

            ClearAllEnemies();
            SpawnPointRegistry.EnsureBuilt(_layout);
            SpawnPointRegistry.ResetRuntimeState();

            if (HasConfig)
            {
                RoundPoolTotal = Config.TotalEnemyPool(_round);
                _targetActive = Config.TargetActiveEnemies(_round);
            }

            _remainingInPool = RoundPoolTotal;
            _spawnedThisRound = 0;
            _refillTimer = 0f;
            _pathCursor = 0;
            _cooldownRelaxCount = 0;
            _pathRelaxCount = 0;

            _tierSelector.BeginRound(_round, Config, RoundPoolTotal, _weightTuning.GuaranteedIntroPoolFraction);

            EnsurePool();
            if (_pool != null)
            {
                _pool.Prewarm(Mathf.Min(_ceiling, EffectiveTarget() + 4));
            }

            RefreshAllPoints(force: true);
        }

        /// <summary>Stops spawning without touching the enemies already on the map.</summary>
        public void StopRound()
        {
            _roundActive = false;
        }

        /// <summary>Returns every living enemy to the pool. Used by the F1 panel and by BeginRound.</summary>
        public void ClearAllEnemies()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                PooledBehaviour behaviour = _active[i].Behaviour;
                if (behaviour != null && behaviour.IsSpawned)
                {
                    behaviour.Despawn();
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
        /// Debug only: pin the active target (0 = back to the round's own target). Values
        /// above the safety ceiling are clamped to it, so "force 40" really means 40.
        /// </summary>
        public void ForceActiveTarget(int target, bool ignorePoolLimit)
        {
            _forcedTargetActive = Mathf.Clamp(target, 0, _ceiling);
            _debugIgnorePoolLimit = ignorePoolLimit && _forcedTargetActive > 0;
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

            RegisterDebugPanel();
        }

        protected override void OnDisable()
        {
            UnregisterDebugPanel();

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDisable();
        }

        /// <summary>
        /// Live config contract. Everything derived from the round curve is recomputed
        /// here, so raising the pool or the target in the F1 panel mid-round is felt on the
        /// very next refill tick.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            _spawnConfig = config.Spawn;
            _refillInterval = Mathf.Max(0.05f, config.Rounds.SpawnRefillInterval);
            _maxSpawnsPerTick = Mathf.Max(1, config.Rounds.MaxSpawnsPerRefillTick);
            _ceiling = Mathf.Max(1, config.AbsoluteSafetyCeiling);
            _targetActive = config.TargetActiveEnemies(_round);
            RoundPoolTotal = config.TotalEnemyPool(_round);

            _weightTuning.Sanitize();

            if (_pool != null && _pool.Capacity < _ceiling)
            {
                _pool.SetCapacity(_ceiling);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Pause freezes the spawn flow with everything else.
            if (dt <= 0f)
            {
                return;
            }

            if (!_roundActive)
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

            Housekeeping();

            _refillTimer -= dt;
            if (_refillTimer <= 0f)
            {
                _refillTimer = _refillInterval;
                RefillTick();
            }

            EvaluateRoundCompletion();
        }

        // ------------------------------------------------------------------ flow

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

                IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
                if (entry.PointIndex >= 0 && entry.PointIndex < points.Count)
                {
                    points[entry.PointIndex].AliveFromHere =
                        Mathf.Max(0, points[entry.PointIndex].AliveFromHere - 1);
                }

                EnemyRetired?.Invoke();
            }
        }

        private void RefillTick()
        {
            if (!_spawningEnabled)
            {
                return;
            }

            RefreshStalePoints(_weightTuning.PathQueriesPerTick);

            int target = EffectiveTarget();
            int spawned = 0;

            // Debug only: the forced-40 measurement would take 9 s to fill at the normal
            // 2 per tick, which is most of a measurement window spent ramping.
            int perTick = _forcedTargetActive > 0 ? Mathf.Max(_maxSpawnsPerTick, 4) : _maxSpawnsPerTick;

            while (spawned < perTick &&
                   _active.Count < target &&
                   _active.Count < _ceiling &&
                   (_remainingInPool > 0 || _debugIgnorePoolLimit))
            {
                if (!TrySpawnOne())
                {
                    break;
                }

                spawned++;
            }
        }

        private void EvaluateRoundCompletion()
        {
            if (_remainingInPool > 0 || _debugIgnorePoolLimit)
            {
                return;
            }

            if (!_poolExhaustedAnnounced)
            {
                _poolExhaustedAnnounced = true;
                RoundPoolExhausted?.Invoke(_round);
            }

            if (_active.Count == 0)
            {
                _roundActive = false;
                RoundCleared?.Invoke(_round);
            }
        }

        private int EffectiveTarget()
        {
            int target = _forcedTargetActive > 0 ? _forcedTargetActive : _targetActive;
            return Mathf.Clamp(target, 0, _ceiling);
        }

        // ------------------------------------------------------------------ spawning

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

            SpawnPointRuntime point = SelectPoint();
            if (point == null)
            {
                NavigationDiagnostics.WarnThrottled(
                    "spawn.nopoint", 5f,
                    "[Spawn] No spawn point passed the weighting this tick (all too close, on screen, " +
                    "cooling down or unreachable). Skipping this refill.");
                return false;
            }

            EnemyTier tier = _tierSelector.Next(_round, Config, _spawnedThisRound, _remainingInPool,
                out bool guaranteed);

            Vector3 position = ScatterAround(point);
            Quaternion rotation = point.SpawnRotation();

            PooledBehaviour instance = _pool.Get(position, rotation);
            if (instance == null)
            {
                // ReturnNull policy: the ceiling is reached. Not an error, just a full map.
                return false;
            }

            EnemySpawnContext context = BuildContext(tier, point, position, rotation, guaranteed);

            if (instance is ISpawnedEnemy spawned)
            {
                spawned.OnSpawnedFromPoint(context);
            }
            else
            {
                NavigationDiagnostics.WarnOnce(
                    "spawn.contract",
                    "[Spawn] The enemy prefab does not implement ISpawnedEnemy, so tier, health and " +
                    "damage could not be handed over. See ISpawnedEnemy for the contract.");
            }

            _active.Add(new ActiveEnemy { Behaviour = instance, PointIndex = point.Index });

            point.AliveFromHere++;
            point.SpawnedThisRound++;
            point.LastUsedTime = Time.time;

            _spawnedThisRound++;
            if (!_debugIgnorePoolLimit)
            {
                _remainingInPool = Mathf.Max(0, _remainingInPool - 1);
            }

            EnemySpawned?.Invoke(context);
            return true;
        }

        private EnemySpawnContext BuildContext(EnemyTier tier, SpawnPointRuntime point,
            Vector3 position, Quaternion rotation, bool guaranteed)
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

            return new EnemySpawnContext(tier, _round, point.Index, point.Id, point.Region,
                position, rotation, health, damage, speed, guaranteed);
        }

        private Vector3 ScatterAround(SpawnPointRuntime point)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * _weightTuning.SpawnScatterRadius;
            Vector3 candidate = point.NavPosition + new Vector3(offset.x, 0f, offset.y);

            if (CrowdService.TryProjectToNavMesh(candidate, 2f, out Vector3 onMesh))
            {
                return onMesh;
            }

            return candidate;
        }

        // ------------------------------------------------------------------ selection

        private SpawnPointRuntime SelectPoint()
        {
            IReadOnlyList<SpawnPointRuntime> points = SpawnPointRegistry.Points;
            int count = points.Count;

            if (count == 0 || _spawnConfig == null)
            {
                return null;
            }

            if (_pointWeights.Length < count)
            {
                _pointWeights = new float[Mathf.NextPowerOfTwo(count)];
                _debugRows = new string[_pointWeights.Length];
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
                NavDegraded = !CrowdService.IsNavMeshAvailable
            };

            UpdatePointDensity(points);

            // Strict first, then drop one gate at a time. Anything past the first pass is
            // rare and is reported, because it means the map or the tuning is squeezing the
            // director into a corner.
            SpawnPointRuntime chosen = TrySelect(points, count, inputs, SpawnRelaxation.None);
            if (chosen != null)
            {
                return chosen;
            }

            chosen = TrySelect(points, count, inputs, SpawnRelaxation.Screen);
            if (chosen != null)
            {
                return chosen;
            }

            // Expected, not exceptional: 12 points with a 4 s cooldown serve 3 spawns/s,
            // while a full refill tick wants up to 4.4/s. When the demand outruns the
            // points, the cooldown becomes a preference instead of a gate and the COOLEST
            // point wins through the recency factor. Counted, not logged.
            chosen = TrySelect(points, count, inputs, SpawnRelaxation.Screen | SpawnRelaxation.Cooldown);
            if (chosen != null)
            {
                _cooldownRelaxCount++;
                return chosen;
            }

            chosen = TrySelect(points, count, inputs,
                SpawnRelaxation.Screen | SpawnRelaxation.Cooldown | SpawnRelaxation.Reachability);
            if (chosen != null)
            {
                NavigationDiagnostics.WarnThrottled(
                    "spawn.relax.path", 15f,
                    "[Spawn] No spawn point had a complete NavMesh path to the player (REQ-068). " +
                    "The reachability gate was relaxed so the round can continue - check the NavMesh bake.");
                _pathRelaxCount++;
            }

            return chosen;
        }

        private SpawnPointRuntime TrySelect(IReadOnlyList<SpawnPointRuntime> points, int count,
            in SpawnWeightInputs inputs, SpawnRelaxation relaxation)
        {
            float total = 0f;

            for (int i = 0; i < count; i++)
            {
                float weight = SpawnWeightCalculator.Evaluate(
                    points[i], inputs, _spawnConfig, _weightTuning, relaxation, out _);

                _pointWeights[i] = weight;
                total += weight;
            }

            if (total <= 0f)
            {
                return null;
            }

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < count; i++)
            {
                roll -= _pointWeights[i];
                if (roll <= 0f)
                {
                    return points[i];
                }
            }

            return points[count - 1];
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
        /// Re-queries a few points per refill tick instead of all twelve every frame:
        /// NavMesh.CalculatePath is the single most expensive call in this system.
        /// </summary>
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
            }

            UpdateScreenFlags(point);
            point.PathCachedAt = Time.time;
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

            _pool = PoolService.GetOrCreate(
                _enemyPrefab,
                Mathf.Min(_ceiling, 12),
                _ceiling,
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
                    if (!_roundActive)
                    {
                        BeginRound(_round);
                    }

                    ForceActiveTarget(_ceiling, true);
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

        private string DescribeFlow()
        {
            return string.Format(
                "R{0} | pool {1}/{2} left | active {3}/{4} (ceiling {5}){6} | spawned {7} | relax cd:{8} path:{9}",
                _round,
                _remainingInPool,
                RoundPoolTotal,
                _active.Count,
                EffectiveTarget(),
                _ceiling,
                _forcedTargetActive > 0 ? " FORCED" : "",
                _spawnedThisRound,
                _cooldownRelaxCount,
                _pathRelaxCount);
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
