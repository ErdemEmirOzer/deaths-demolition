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
        private int _mask;

        public bool IsResting => _rested;

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
            _mask = (1 << 0) | LayerBit("Environment");
        }

        private static int LayerBit(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? 1 << layer : 0;
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
            if (Physics.RaycastNonAlloc(transform.position, Vector3.down, _hits, FloorRayMetres, _mask, QueryTriggerInteraction.Ignore) > 0)
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
