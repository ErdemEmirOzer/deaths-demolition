using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// Drives the crowd solver and owns its live tuning.
    ///
    /// It self-hosts: <see cref="CrowdService.Register"/> calls
    /// <see cref="EnsureExists"/>, so the crowd works even if no scene ever places this
    /// component. If the map scene (task G-06) wants the tuning visible in the Inspector
    /// it can drop the component into the scene instead - the first instance wins and any
    /// later duplicate destroys itself.
    ///
    /// Execution order is set to -200 in the .meta file so the correction handed to
    /// ApplySteering is computed from this frame's positions, before the enemies move.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NavigationSystemBehaviour : PrototypeConfigBehaviour
    {
        private const string RuntimeObjectName = "[Prototype Crowd]";

        /// <summary>PROTOTYPE-ONLY, invented: seconds between "Player" tag fallback searches.</summary>

        /// <summary>PROTOTYPE-ONLY, invented: smoothing on the measured player velocity.</summary>
        private const float PlayerVelocitySmoothing = 0.6f;

        [Header("Crowd layer (PROTOTYPE-ONLY, every value invented)")]
        [SerializeField] private CrowdTuning _tuning = new CrowdTuning();

        [Header("Diagnostics")]
        [Tooltip("Turn the whole crowd layer off to A/B its cost and its effect. Debug only.")]
        [SerializeField] private bool _crowdEnabled = true;

        [Tooltip("Draw member radii and correction vectors in the Scene view.")]
        [SerializeField] private bool _drawGizmos = false;

        private static NavigationSystemBehaviour s_instance;

        private float _solveAccumulator;
        private float _configPlayerRadius = 0.45f;
        private Vector3 _playerPrevPosition;
        private Vector3 _playerVelocity;
        private bool _playerPrimed;

        private DebugPanelReadout _crowdReadout;
        private DebugPanelReadout _navReadout;
        private DebugPanelToggle _crowdToggle;
        private DebugPanelButton _resetPeaks;

        public static NavigationSystemBehaviour Instance => s_instance;

        /// <summary>Creates the runtime host if it does not exist yet. Cheap and idempotent.</summary>
        public static void EnsureExists()
        {
            if (s_instance != null || !Application.isPlaying)
            {
                return;
            }

            var go = new GameObject(RuntimeObjectName);
            go.AddComponent<NavigationSystemBehaviour>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(this);
                return;
            }

            s_instance = this;
            CrowdService.Solver.Tuning = _tuning;
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (s_instance == null)
            {
                s_instance = this;
            }

            CrowdService.Solver.Tuning = _tuning;

            _crowdReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Performance, "Crowd", DescribeCrowd, -90);

            _navReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Performance, "NavMesh", DescribeNav, -89);

            _crowdToggle = DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Performance, "Crowd layer enabled",
                () => _crowdEnabled, v => _crowdEnabled = v, -88);

            _resetPeaks = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Performance, "Reset crowd peak",
                () => CrowdService.Solver.ResetPeaks(), -87);
        }

        protected override void OnDisable()
        {
            DebugPanelRegistry.Remove(_crowdReadout);
            DebugPanelRegistry.Remove(_navReadout);
            DebugPanelRegistry.Remove(_crowdToggle);
            DebugPanelRegistry.Remove(_resetPeaks);

            if (s_instance == this)
            {
                s_instance = null;
            }

            base.OnDisable();
        }

        /// <summary>
        /// Live config contract: the player capsule radius the crowd braces against comes
        /// from PrototypeConfig, so editing it in the F1 panel mid-run changes how thick
        /// the wall of zombies stands around the player immediately.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            _configPlayerRadius = Mathf.Max(0.05f, config.Player.CollisionRadius);
            _tuning.Sanitize();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Pause freezes the whole simulation: no tick, no ApplySteering, nothing moves.
            if (dt <= 0f)
            {
                return;
            }

            UpdatePlayerState(dt);

            if (!_crowdEnabled)
            {
                return;
            }

            float interval = _tuning.SolveInterval;
            if (interval > 0f)
            {
                _solveAccumulator += dt;
                if (_solveAccumulator < interval)
                {
                    return;
                }

                dt = _solveAccumulator;
                _solveAccumulator = 0f;
            }

            CrowdPlayerState state = CrowdService.PlayerState;
            CrowdService.Solver.Tick(dt, state);
        }

        private void UpdatePlayerState(float dt)
        {
            Transform player = CrowdService.PlayerTransform;

            if (player == null)
            {
                // No tag discovery: nothing in this project carries the "Player" tag, so the
                // old fallback could only ever return null while making the handoff look
                // defended. CrowdService.SetPlayer is mandatory; RunPlayerBinding performs it.
            }

            if (player == null)
            {
                _playerPrimed = false;
                CrowdService.SetPlayerState(default);
                return;
            }

            Vector3 position = player.position;
            position.y = 0f;

            if (_playerPrimed)
            {
                Vector3 raw = (position - _playerPrevPosition) / dt;
                raw.y = 0f;
                _playerVelocity = Vector3.Lerp(raw, _playerVelocity, PlayerVelocitySmoothing);
            }
            else
            {
                _playerVelocity = Vector3.zero;
                _playerPrimed = true;
            }

            _playerPrevPosition = position;

            CrowdService.SetPlayerState(new CrowdPlayerState
            {
                HasPlayer = true,
                Position = position,
                Radius = CrowdService.PlayerRadius > 0f ? CrowdService.PlayerRadius : _configPlayerRadius,
                Velocity = _playerVelocity
            });
        }

        private static string DescribeCrowd()
        {
            CrowdSolver solver = CrowdService.Solver;
            return string.Format(
                "{0} members | solve {1:0.000} ms (avg {2:0.000}, peak {3:0.000}) | {4} pair tests, {5} contacts",
                solver.MemberCount,
                solver.LastSolveMilliseconds,
                solver.AverageSolveMilliseconds,
                solver.PeakSolveMilliseconds,
                solver.LastPairTests,
                solver.LastContactCount);
        }

        private static string DescribeNav()
        {
            NavPathService paths = CrowdService.Paths;
            string state = paths.IsNavMeshAvailable ? "OK" : "DEGRADED (no NavMesh, straight-line estimates)";
            return string.Format("{0} | {1} queries total, {2} this frame",
                state, paths.TotalQueries, paths.QueriesThisFrame);
        }

        private void OnDrawGizmos()
        {
            if (!_drawGizmos || !Application.isPlaying)
            {
                return;
            }

            CrowdSolver solver = CrowdService.Solver;
            int count = solver.MemberCount;

            for (int i = 0; i < count; i++)
            {
                if (!solver.TryGetDebugMember(i, out Vector3 position, out float radius, out Vector3 correction))
                {
                    continue;
                }

                Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.35f);
                Gizmos.DrawWireSphere(position, radius);

                Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
                Gizmos.DrawLine(position, position + correction * 0.4f);
            }

            if (CrowdService.HasPlayer)
            {
                CrowdPlayerState state = CrowdService.PlayerState;
                Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.8f);
                Gizmos.DrawWireSphere(state.Position, state.Radius);
            }
        }
    }
}
