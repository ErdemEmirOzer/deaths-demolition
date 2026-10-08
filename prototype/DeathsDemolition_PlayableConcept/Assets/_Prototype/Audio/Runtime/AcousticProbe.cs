using DD.Prototype.Core;
using Unity.Profiling;
using UnityEngine;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// WP-097 K1 (03 §1, §4.3) · what the walls around the player look like to the ear, top-down only. Eight world-fixed horizontal
    /// rays (45° apart) out to 40 m plus one roof ray (8 m up); in the open (no roof) eight more far rays out to 120 m feed the echo
    /// delay. The slapback (<see cref="WeaponReflections.SelectTaps"/>) and the enclosure guess (<see cref="TryGuess"/>) share it.
    ///
    /// CACHED: <see cref="Refresh"/> casts only when the origin moved ≥ 1 m or 0.5 s passed (or forced) — at most once per call,
    /// ≤ 17 casts. <c>Physics.Raycast(…, out RaycastHit, …)</c> gives the NEAREST hit and allocates nothing;
    /// <c>RaycastNonAlloc</c> is not used on purpose (its results are unordered and a one-slot buffer does not guarantee the nearest).
    /// Mask: <see cref="PrototypeLayers.AimBlockMask"/> (Environment | CameraOccluder — V6 walls and roofs).
    ///
    /// A REFLECTOR is a hit 2.5–40 m away whose face looks back at the ray (normal · −dir ≥ 0.5) on a collider at least 1.5 m wide
    /// and 2 m tall (bounds) — tree trunks and lanterns do not count. Any hit counts for the enclosure guess.
    ///
    /// ENCLOSURE GUESS (until S-05's JSON / an <see cref="AcousticVolume"/> covers a building): roof hit AND ≥ 6 of 8 rays hit
    /// within 10 m → enclosed; then the narrowest opposite-ray pair &lt; 3.5 m → Corridor; else a floor area of 20–90 m²
    /// (<see cref="EstimateArea"/>) under a roof ≤ 4 m above the ear → Room (C-00 K3); else Hall. Otherwise Open. A new guess must
    /// come twice in a row (two refreshes) before it replaces the current one (hysteresis).
    ///
    /// Owned by <see cref="PrototypeAudioDirector"/>; no state outside its fields; allocation-free after construction.
    /// </summary>
    public sealed class AcousticProbe
    {
        public const int RayCount = 8;
        public const float MinSlapMeters = 2.5f;
        public const float MaxSlapMeters = 40f;
        public const float RoofMeters = 8f;
        public const float FarMeters = 120f;
        public const float RefreshMeters = 1f;
        public const float RefreshSeconds = 0.5f;
        public const float MinFacingDot = 0.5f;
        public const float MinReflectorWidth = 1.5f;
        public const float MinReflectorHeight = 2f;
        public const float EnclosedMeters = 10f;
        public const int EnclosedRays = 6;
        public const float CorridorWidthMeters = 3.5f;

        /// <summary>WP-097 K1 (C-00 K3): Room = enclosed, 20–90 m² of floor, roof ray ≤ 4 m.</summary>
        public const float RoomMinArea = 20f;
        public const float RoomMaxArea = 90f;
        public const float RoomMaxCeilingMeters = 4f;
        public const int HysteresisRefreshes = 2;

        /// <summary>The guess answers only for points this close to the probe origin (m) …</summary>
        public const float AnswerRadius = 2f;

        /// <summary>… and only this long after the last refresh (s): a stale probe (camera mode left top-down) says nothing.</summary>
        public const float StaleSeconds = 2f;

        private static readonly Vector3[] s_dirs = BuildDirections();
        private static readonly ProfilerMarker s_marker = new ProfilerMarker("DD.Audio.AcousticProbe");

        private readonly float[] _dist = new float[RayCount];
        private readonly Vector3[] _normal = new Vector3[RayCount];
        private readonly bool[] _valid = new bool[RayCount];
        private readonly float[] _far = new float[RayCount];
        private readonly int _mask;
        private readonly bool _aimMask;
        private bool _maskResolved;
        private int _resolvedMask;

        private Vector3 _origin;
        private float _time;
        private bool _hasData;
        private bool _roof;
        private float _roofMeters = float.PositiveInfinity;
        private float _openness = 1f;
        private float _farMedian = -1f;

        private AcousticSpace _guess = AcousticSpace.Open;
        private AcousticSpace _pending = AcousticSpace.Open;
        private int _pendingCount;

        /// <summary>Game probe: the aim-block mask (resolved on the first refresh).</summary>
        public AcousticProbe() : this(0)
        {
            _aimMask = true;
        }

        /// <summary>Tests: an explicit layer mask.</summary>
        public AcousticProbe(int layerMask)
        {
            _mask = layerMask;
            for (int i = 0; i < RayCount; i++)
            {
                _dist[i] = float.PositiveInfinity;
                _far[i] = -1f;
            }
        }

        /// <summary>World-fixed horizontal ray direction <paramref name="ray"/> (0 = +X, then 45° steps towards +Z).</summary>
        public static Vector3 Direction(int ray) => s_dirs[((ray % RayCount) + RayCount) % RayCount];

        /// <summary>Per ray: metres to the nearest hit (+∞ = none within 40 m). Read-only by contract.</summary>
        public float[] Distances => _dist;

        /// <summary>Per ray: the hit is a reflector (see the class summary). Read-only by contract.</summary>
        public bool[] Valid => _valid;

        /// <summary>Per ray: the hit's surface normal (zero when none).</summary>
        public Vector3 Normal(int ray) => _normal[((ray % RayCount) + RayCount) % RayCount];

        /// <summary>Reflectors seen at the last refresh.</summary>
        public int ValidCount { get; private set; }

        /// <summary>0 … 1: share of the eight rays with nothing within 10 m (1 = open court).</summary>
        public float Openness => _openness;

        /// <summary>Median far-ray distance beyond 40 m (m), −1 when none (or under a roof).</summary>
        public float FarMedian => _farMedian;

        /// <summary>The roof ray hit something within 8 m at the last refresh.</summary>
        public bool Roof => _roof;

        /// <summary>Metres from the probe origin up to the roof (+∞ = none within 8 m).</summary>
        public float RoofDistance => _roofMeters;

        /// <summary>The committed enclosure guess (after hysteresis).</summary>
        public AcousticSpace Guess => _guess;

        public bool HasData => _hasData;
        public Vector3 Origin => _origin;
        public float RefreshedAt => _time;

        /// <summary>Near + roof casts since construction (9 per refresh). Test / F1 counter.</summary>
        public int Casts { get; private set; }

        /// <summary>Far casts since construction (open air only).</summary>
        public int FarCasts { get; private set; }

        /// <summary>Refreshes that cast.</summary>
        public int Refreshes { get; private set; }

        /// <summary>
        /// Re-casts when <paramref name="force"/>, on the first call, after ≥ 1 m of movement or after 0.5 s; otherwise keeps the
        /// cache. Returns true when it cast. <paramref name="now"/> is any monotonic clock (the director passes unscaled time).
        /// </summary>
        public bool Refresh(Vector3 origin, float now, bool force)
        {
            if (!force && _hasData && (origin - _origin).sqrMagnitude < RefreshMeters * RefreshMeters && now - _time < RefreshSeconds)
            {
                return false;
            }

            using (s_marker.Auto())
            {
                Cast(origin);
            }

            _origin = origin;
            _time = now;
            _hasData = true;
            Refreshes++;
            UpdateGuess();
            return true;
        }

        /// <summary>
        /// The enclosure guess for a point next to the probe (≤ <see cref="AnswerRadius"/> from the origin, refreshed within
        /// <see cref="StaleSeconds"/>). True only for an enclosed guess (Corridor / Hall): Open is the resolver's own default.
        /// </summary>
        public bool TryGuess(Vector3 point, float now, out AcousticSpace space)
        {
            space = AcousticSpace.Open;
            if (!_hasData || _guess == AcousticSpace.Open || now - _time > StaleSeconds || (point - _origin).sqrMagnitude > AnswerRadius * AnswerRadius)
            {
                return false;
            }

            space = _guess;
            return true;
        }

        /// <summary>Forget everything (scene change, tests).</summary>
        public void Clear()
        {
            for (int i = 0; i < RayCount; i++)
            {
                _dist[i] = float.PositiveInfinity;
                _normal[i] = Vector3.zero;
                _valid[i] = false;
                _far[i] = -1f;
            }

            _hasData = false;
            _roof = false;
            _roofMeters = float.PositiveInfinity;
            _openness = 1f;
            _farMedian = -1f;
            _guess = AcousticSpace.Open;
            _pending = AcousticSpace.Open;
            _pendingCount = 0;
            ValidCount = 0;
        }

        /// <summary>
        /// A hit that returns a slap: 2.5–40 m away, its face towards the ray (normal · −dir ≥ 0.5), on a collider ≥ 1.5 m wide
        /// (larger horizontal bounds side) and ≥ 2 m tall. Pure (tests).
        /// </summary>
        public static bool IsReflector(float meters, Vector3 dir, Vector3 normal, Bounds bounds)
        {
            if (meters < MinSlapMeters || meters > MaxSlapMeters)
            {
                return false;
            }

            if (Vector3.Dot(normal, -dir) < MinFacingDot)
            {
                return false;
            }

            Vector3 size = bounds.size;
            return Mathf.Max(size.x, size.z) >= MinReflectorWidth && size.y >= MinReflectorHeight;
        }

        /// <summary>
        /// The raw enclosure guess from one refresh (pure, tests): a roof (<paramref name="roofMeters"/> ≤ 8, +∞ = none) and ≥ 6
        /// of 8 hits within 10 m → enclosed; the narrowest opposite pair (ray i + ray i+4) &lt; 3.5 m → Corridor; else 20–90 m²
        /// (<see cref="EstimateArea"/>) with the roof ≤ 4 m → Room; else Hall. Otherwise Open.
        /// </summary>
        public static AcousticSpace RawGuess(float roofMeters, float[] dist)
        {
            if (!(roofMeters <= RoofMeters) || dist == null || dist.Length < RayCount)
            {
                return AcousticSpace.Open;
            }

            int near = 0;
            for (int i = 0; i < RayCount; i++)
            {
                if (dist[i] <= EnclosedMeters) near++;
            }

            if (near < EnclosedRays)
            {
                return AcousticSpace.Open;
            }

            float narrowest = float.PositiveInfinity;
            for (int i = 0; i < RayCount / 2; i++)
            {
                float w = dist[i] + dist[i + RayCount / 2];
                if (w < narrowest) narrowest = w;
            }

            if (narrowest < CorridorWidthMeters)
            {
                return AcousticSpace.Corridor;
            }

            float area = EstimateArea(dist);
            return area >= RoomMinArea && area <= RoomMaxArea && roofMeters <= RoomMaxCeilingMeters ? AcousticSpace.Room : AcousticSpace.Hall;
        }

        /// <summary>
        /// Floor area (m²) of the octagon the eight hits span around the origin: ½·Σ dᵢ·dᵢ₊₁·sin 45°, every distance capped at
        /// 10 m (a door / a miss counts as the 10 m enclosure limit). An 8 × 8 m room from its centre → 64 m².
        /// </summary>
        public static float EstimateArea(float[] dist)
        {
            if (dist == null || dist.Length < RayCount)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < RayCount; i++)
            {
                float a = Mathf.Min(dist[i], EnclosedMeters);
                float b = Mathf.Min(dist[(i + 1) % RayCount], EnclosedMeters);
                sum += a * b;
            }

            return 0.5f * sum * Mathf.Sin(2f * Mathf.PI / RayCount);
        }

        private void Cast(Vector3 origin)
        {
            int mask = Mask();
            int valid = 0;
            int near = 0;
            for (int i = 0; i < RayCount; i++)
            {
                Vector3 dir = s_dirs[i];
                Casts++;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, MaxSlapMeters, mask, QueryTriggerInteraction.Ignore))
                {
                    _dist[i] = hit.distance;
                    _normal[i] = hit.normal;
                    Collider c = hit.collider;
                    _valid[i] = c != null && IsReflector(hit.distance, dir, hit.normal, c.bounds);
                    if (_valid[i]) valid++;
                    if (hit.distance <= EnclosedMeters) near++;
                }
                else
                {
                    _dist[i] = float.PositiveInfinity;
                    _normal[i] = Vector3.zero;
                    _valid[i] = false;
                }
            }

            Casts++;
            _roof = Physics.Raycast(origin, Vector3.up, out RaycastHit roofHit, RoofMeters, mask, QueryTriggerInteraction.Ignore);
            _roofMeters = _roof ? roofHit.distance : float.PositiveInfinity;
            ValidCount = valid;
            _openness = 1f - (float)near / RayCount;

            // Far geometry for the echo delay — open air only (under a roof the room's own tail is the space).
            int farCount = 0;
            for (int i = 0; i < RayCount; i++)
            {
                _far[i] = -1f;
                if (_roof || _dist[i] <= MaxSlapMeters)
                {
                    continue;   // a wall inside 40 m blocks this direction
                }

                FarCasts++;
                if (Physics.Raycast(origin, s_dirs[i], out RaycastHit far, FarMeters, mask, QueryTriggerInteraction.Ignore) && far.distance > MaxSlapMeters)
                {
                    _far[farCount++] = far.distance;
                }
            }

            _farMedian = farCount > 0 ? MedianInPlace(_far, farCount) : -1f;
        }

        private void UpdateGuess()
        {
            AcousticSpace raw = RawGuess(_roofMeters, _dist);
            if (raw == _guess)
            {
                _pendingCount = 0;
                return;
            }

            if (raw == _pending && _pendingCount > 0)
            {
                _pendingCount++;
            }
            else
            {
                _pending = raw;
                _pendingCount = 1;
            }

            if (_pendingCount >= HysteresisRefreshes)
            {
                _guess = raw;
                _pendingCount = 0;
            }
        }

        private int Mask()
        {
            if (!_aimMask)
            {
                return _mask;
            }

            if (!_maskResolved)
            {
                _resolvedMask = PrototypeLayers.AimBlockMask;
                _maskResolved = true;
            }

            return _resolvedMask;
        }

        /// <summary>Insertion sort of the first <paramref name="n"/> values, then the median (allocation-free; n ≤ 8).</summary>
        private static float MedianInPlace(float[] v, int n)
        {
            for (int i = 1; i < n; i++)
            {
                float x = v[i];
                int j = i - 1;
                while (j >= 0 && v[j] > x)
                {
                    v[j + 1] = v[j];
                    j--;
                }

                v[j + 1] = x;
            }

            return (n & 1) == 1 ? v[n / 2] : 0.5f * (v[n / 2 - 1] + v[n / 2]);
        }

        private static Vector3[] BuildDirections()
        {
            var dirs = new Vector3[RayCount];
            for (int i = 0; i < RayCount; i++)
            {
                float a = i * (2f * Mathf.PI / RayCount);
                dirs[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }

            return dirs;
        }
    }
}
