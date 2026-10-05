using DD.Prototype.Core;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-047E · one ejected casing: a MeshRenderer flung from the weapon's Ejection socket (+X, up), ballistic without physics
    /// (one ground ray at spawn gives the floor), tumbling, resting for the tail of its life. Pooled (12), no allocation per shot.
    /// </summary>
    public sealed class CasingEffect : PooledEffect
    {
        public const float LifeSeconds = 1.6f;
        public const float Gravity = 9.8f;
        public const float FloorRayMetres = 3f;

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Vector3 _velocity;
        private Vector3 _spin;
        private float _floorY;
        private bool _rested;
        private readonly RaycastHit[] _hits = new RaycastHit[1];

        public bool IsResting => _rested;

        private static readonly RaycastHit[] s_predictHits = new RaycastHit[1];

        /// <summary>
        /// The floor ray's layers (Default | Environment; no Environment layer = Default only). B-03 B2 (C-00 05.10): no name lookup
        /// per casing — PredictLanding runs for every visible casing (WeaponFoleyAudio, a62d83db); PrototypeLayers resolves the
        /// layer once and again on play.
        /// </summary>
        public static int FloorMask
        {
            get
            {
                int environment = PrototypeLayers.Environment;
                return (1 << 0) | (environment >= 0 ? 1 << environment : 0);
            }
        }

        /// <summary>
        /// WP-087 (C-00 05.10) · where a casing launched at <paramref name="from"/> with <paramref name="velocity"/> first touches down
        /// and where it lies — this effect's own rules in closed form: the spawn's floor ray (Default | Environment, 3 m; no hit = 3 m
        /// down), gravity, a bounce (× 0.35 along, × 0.22 up) while it lands faster than 0.6 m/s, else it rests. The visible casing
        /// steps the same arc a frame at a time, so it differs by at most a frame's travel. Pair with CasingEvents.Launched.
        /// </summary>
        public static void PredictLanding(Vector3 from, Vector3 velocity, out Vector3 touchdown, out float touchdownSeconds, out Vector3 rest, out float restSeconds)
        {
            int mask = FloorMask;
            float floor = from.y - FloorRayMetres;
            if (Physics.RaycastNonAlloc(from, Vector3.down, s_predictHits, FloorRayMetres, mask, QueryTriggerInteraction.Ignore) > 0) floor = s_predictHits[0].point.y;
            floor += 0.006f;
            Vector3 p = from, v = velocity;
            float t = 0f;
            touchdown = rest = from;
            touchdownSeconds = restSeconds = 0f;
            for (int bounce = 0; bounce < 8; bounce++)
            {
                float h = Mathf.Max(0f, p.y - floor);
                float tau = (v.y + Mathf.Sqrt(Mathf.Max(0f, v.y * v.y + 2f * Gravity * h))) / Gravity;
                p += new Vector3(v.x, 0f, v.z) * tau;
                p.y = floor;
                t += tau;
                float vy = v.y - Gravity * tau;
                if (bounce == 0) { touchdown = p; touchdownSeconds = t; }
                if (vy >= -0.6f) break;
                v = new Vector3(v.x * 0.35f, -vy * 0.22f, v.z * 0.35f);
            }

            rest = p;
            restSeconds = t;
        }

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        public void Play(Mesh mesh, Material material, Vector3 velocity, float seed, float life = LifeSeconds)
        {
            if (_filter == null) Awake();
            _filter.sharedMesh = mesh;
            _renderer.sharedMaterial = material;
            _velocity = velocity;
            // Tumble mostly around the case's own axis, seeded per shot so no two look alike.
            _spin = new Vector3(420f + seed * 300f, 90f * (seed - 0.5f), 760f * (0.5f + seed));
            _rested = false;
            _floorY = transform.position.y - FloorRayMetres;
            if (Physics.RaycastNonAlloc(transform.position, Vector3.down, _hits, FloorRayMetres, FloorMask, QueryTriggerInteraction.Ignore) > 0)
            {
                _floorY = _hits[0].point.y;
            }

            Lifetime = Mathf.Max(0.5f, life);
            RestartLife();
        }

        protected override void Tick(float t)
        {
            if (_rested) return;
            float dt = Time.deltaTime;
            _velocity.y -= Gravity * dt;
            Vector3 p = transform.position + _velocity * dt;
            if (p.y <= _floorY + 0.006f)
            {
                p.y = _floorY + 0.006f;
                if (_velocity.y < -0.6f)
                {
                    // One small bounce, then it lies down.
                    _velocity = new Vector3(_velocity.x * 0.35f, -_velocity.y * 0.22f, _velocity.z * 0.35f);
                    _spin *= 0.4f;
                }
                else
                {
                    _rested = true;
                    transform.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
                }
            }

            transform.position = p;
            if (!_rested) transform.Rotate(_spin * dt, Space.Self);
        }
    }
}
