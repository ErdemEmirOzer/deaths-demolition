using UnityEngine;
using UnityEngine.AI;

namespace DD.Prototype.Navigation
{
    /// <summary>Result of a walkable-path query.</summary>
    public enum NavPathStatus
    {
        /// <summary>A full path exists from A to B. The only status REQ-068 accepts.</summary>
        Complete = 0,

        /// <summary>The NavMesh could only produce a partial path. Explicitly NOT reachable.</summary>
        Partial = 1,

        /// <summary>No path, or an endpoint could not be projected onto the NavMesh.</summary>
        Invalid = 2,

        /// <summary>
        /// There is no NavMesh in the scene at all yet (the map arrives with G-06).
        /// The distance returned is a straight line and must be treated as an estimate.
        /// </summary>
        Degraded = 3
    }

    /// <summary>
    /// The NavMesh half of the S4 split: paths and WALKABLE path distance, nothing else.
    /// No separation, no pushing, no avoidance - that all lives in <see cref="CrowdSolver"/>.
    ///
    /// ALLOCATION
    /// ----------
    /// One <see cref="NavMeshPath"/> and one corner buffer are allocated per instance and
    /// reused forever. Corners are read with GetCornersNonAlloc, because NavMeshPath.corners
    /// allocates a fresh array on every single access - that property is banned here.
    ///
    /// GRACEFUL DEGRADATION (no map yet)
    /// ---------------------------------
    /// Every query first tries to project its endpoints onto the NavMesh. If projection
    /// fails everywhere, the service flips to <see cref="IsDegraded"/>, logs ONE warning
    /// (never an error - acceptance criterion 27), returns straight-line distances and,
    /// while degraded, answers <see cref="IsReachable"/> with
    /// <see cref="AssumeReachableWhenDegraded"/> so the spawn director keeps running
    /// before the map exists. It re-probes periodically and recovers by itself the moment
    /// G-06 bakes a surface.
    /// </summary>
    public sealed class NavPathService
    {
        /// <summary>Corner buffer size. A 90x90 m blockout never comes close to this.</summary>
        public const int MaxCorners = 128;

        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly Vector3[] _corners = new Vector3[MaxCorners];

        private bool _degraded;
        private bool _probedOnce;
        private float _lastProbeTime = -999f;
        private int _budgetFrame = -1;
        private int _budgetUsed;

        // WP-030: query timing (Stopwatch around NavMesh.CalculatePath; per frame and running average over frames that queried).
        private readonly System.Diagnostics.Stopwatch _queryWatch = new System.Diagnostics.Stopwatch();
        private int _timingFrame = -1;
        private double _frameQueryMs;
        private double _avgQueryMs;
        private int _avgFrames;

        /// <summary>Milliseconds spent in NavMesh.CalculatePath during the current frame.</summary>
        public float FrameQueryMilliseconds => (float)_frameQueryMs;

        /// <summary>Running average of per-frame query milliseconds over frames that issued at least one query.</summary>
        public float AverageQueryMilliseconds => (float)_avgQueryMs;

        public void ResetTiming() { _avgQueryMs = 0; _avgFrames = 0; _frameQueryMs = 0; }

        private void AccountQueryTime()
        {
            int frame = Time.frameCount;
            if (frame != _timingFrame)
            {
                if (_timingFrame >= 0 && _frameQueryMs > 0)
                {
                    _avgFrames++;
                    _avgQueryMs += (_frameQueryMs - _avgQueryMs) / _avgFrames;
                }

                _timingFrame = frame;
                _frameQueryMs = 0;
            }

            _frameQueryMs += _queryWatch.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------------ tunables
        // PROTOTYPE-ONLY, all invented: the request says nothing about query geometry.

        /// <summary>How far a query point may be from the NavMesh and still be projected.</summary>
        public float SampleRadius = 2.5f;

        /// <summary>Radius used by the "is there a NavMesh at all" probe.</summary>
        public float ProbeRadius = 40f;

        /// <summary>Seconds between two probes while degraded.</summary>
        public float ProbeInterval = 2f;

        /// <summary>Area mask for every query.</summary>
        public int AreaMask = NavMesh.AllAreas;

        /// <summary>
        /// While no NavMesh exists, treat everything as reachable so the prototype still
        /// spawns and plays. Turn off to hard-fail instead.
        /// </summary>
        public bool AssumeReachableWhenDegraded = true;

        /// <summary>
        /// Frame budget for the callers that opt into it (path followers, spawn distance
        /// cache). Direct CrowdService.PathDistance calls are never budget-gated.
        /// </summary>
        public int MaxPathQueriesPerFrame = 6;

        // ------------------------------------------------------------------ diagnostics

        /// <summary>True while no NavMesh could be found anywhere.</summary>
        public bool IsDegraded => _degraded;

        /// <summary>True when the last full query completed. Cheap surface for the F1 panel.</summary>
        public bool IsNavMeshAvailable => !_degraded;

        /// <summary>Path queries served since the last reset. Debug readout only.</summary>
        public int QueriesThisFrame => _budgetUsed;

        public int TotalQueries { get; private set; }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Walkable path length between two points, NOT the straight line.
        /// Returns the straight-line distance when the NavMesh cannot answer; use
        /// <see cref="TryPathDistance"/> when the caller must know which happened.
        /// </summary>
        public float PathDistance(Vector3 a, Vector3 b)
        {
            TryPathDistance(a, b, out float distance, out _);
            return distance;
        }

        /// <summary>
        /// Walkable path length plus the status behind it.
        /// A partial path returns the length that WAS found, with
        /// <see cref="NavPathStatus.Partial"/>; callers that need REQ-068 semantics must
        /// reject anything other than <see cref="NavPathStatus.Complete"/>.
        /// </summary>
        public bool TryPathDistance(Vector3 a, Vector3 b, out float distance, out NavPathStatus status)
        {
            float straight = FlatDistance(a, b);

            if (!TryProject(a, out Vector3 from) || !TryProject(b, out Vector3 to))
            {
                MarkDegradedIfNoMesh(a);
                distance = straight;
                status = _degraded ? NavPathStatus.Degraded : NavPathStatus.Invalid;
                return false;
            }

            TotalQueries++;

            if (!NavMesh.CalculatePath(from, to, AreaMask, _path))
            {
                distance = straight;
                status = NavPathStatus.Invalid;
                return false;
            }

            int count = _path.GetCornersNonAlloc(_corners);
            if (count < 2)
            {
                // Same poly, or a path that produced no usable corners.
                distance = straight;
                status = _path.status == NavMeshPathStatus.PathComplete
                    ? NavPathStatus.Complete
                    : NavPathStatus.Invalid;
                return status == NavPathStatus.Complete;
            }

            float length = 0f;
            for (int i = 1; i < count; i++)
            {
                length += FlatDistance(_corners[i - 1], _corners[i]);
            }

            distance = length;

            switch (_path.status)
            {
                case NavMeshPathStatus.PathComplete:
                    status = NavPathStatus.Complete;
                    return true;

                case NavMeshPathStatus.PathPartial:
                    status = NavPathStatus.Partial;
                    return false;

                default:
                    status = NavPathStatus.Invalid;
                    return false;
            }
        }

        /// <summary>
        /// REQ-068: a valid, COMPLETE walkable path is required. A partial path is
        /// explicitly not reachable - that is the difference between "the zombie will
        /// arrive" and "the zombie will walk into a wall and stand there".
        /// </summary>
        public bool IsReachable(Vector3 from, Vector3 to)
        {
            if (TryPathDistance(from, to, out _, out NavPathStatus status))
            {
                return status == NavPathStatus.Complete;
            }

            return status == NavPathStatus.Degraded && AssumeReachableWhenDegraded;
        }

        /// <summary>
        /// Fills a caller-owned corner buffer with the path. Returns the corner count,
        /// 0 when there is no usable path. Allocation free.
        /// </summary>
        public int CalculateCorners(Vector3 from, Vector3 to, Vector3[] buffer, out NavPathStatus status)
        {
            status = NavPathStatus.Invalid;

            if (buffer == null || buffer.Length == 0)
            {
                return 0;
            }

            if (!TryProject(from, out Vector3 a) || !TryProject(to, out Vector3 b))
            {
                MarkDegradedIfNoMesh(from);
                status = _degraded ? NavPathStatus.Degraded : NavPathStatus.Invalid;
                return 0;
            }

            TotalQueries++;

            _queryWatch.Restart();
            bool ok = NavMesh.CalculatePath(a, b, AreaMask, _path);
            _queryWatch.Stop();
            AccountQueryTime();
            if (!ok)
            {
                return 0;
            }

            switch (_path.status)
            {
                case NavMeshPathStatus.PathComplete:
                    status = NavPathStatus.Complete;
                    break;
                case NavMeshPathStatus.PathPartial:
                    status = NavPathStatus.Partial;
                    break;
                default:
                    status = NavPathStatus.Invalid;
                    return 0;
            }

            return _path.GetCornersNonAlloc(buffer);
        }

        /// <summary>Projects a point onto the NavMesh. False when nothing is close enough.</summary>
        public bool TryProject(Vector3 point, out Vector3 result)
        {
            return TryProject(point, SampleRadius, out result);
        }

        public bool TryProject(Vector3 point, float radius, out Vector3 result)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, radius, AreaMask))
            {
                result = hit.position;

                if (_degraded)
                {
                    // The map showed up. Recover quietly and allow a future warning again.
                    _degraded = false;
                    NavigationDiagnostics.Forget(DegradedKey);
                    Debug.Log("[Navigation] NavMesh detected. Path queries are live again.");
                }

                return true;
            }

            result = point;
            return false;
        }

        /// <summary>
        /// Per-frame budget gate for opt-in callers. Returns false once the frame's quota
        /// is spent, which is the signal to try again next frame rather than to give up.
        /// </summary>
        public bool TryTakeQueryBudget()
        {
            int frame = Time.frameCount;
            if (frame != _budgetFrame)
            {
                _budgetFrame = frame;
                _budgetUsed = 0;
            }

            if (_budgetUsed >= Mathf.Max(1, MaxPathQueriesPerFrame))
            {
                return false;
            }

            _budgetUsed++;
            return true;
        }

        /// <summary>Straight-line distance on the XZ plane. Height is ignored on purpose.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ internals

        private const string DegradedKey = "nav.degraded";

        private void MarkDegradedIfNoMesh(Vector3 near)
        {
            if (_degraded)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (_probedOnce && now - _lastProbeTime < ProbeInterval)
            {
                return;
            }

            _probedOnce = true;
            _lastProbeTime = now;

            if (NavMesh.SamplePosition(near, out _, ProbeRadius, AreaMask))
            {
                // A NavMesh exists, this single query simply asked about a spot that is
                // off the walkable surface. Not a degraded state.
                return;
            }

            _degraded = true;
            NavigationDiagnostics.WarnOnce(
                DegradedKey,
                "[Navigation] No NavMesh found within " + ProbeRadius.ToString("0") + " m. " +
                "Path distance falls back to straight-line estimates and reachability is assumed. " +
                "This is expected until the map (task G-06) bakes a NavMeshSurface; it is a warning, not an error.");
        }
    }
}
