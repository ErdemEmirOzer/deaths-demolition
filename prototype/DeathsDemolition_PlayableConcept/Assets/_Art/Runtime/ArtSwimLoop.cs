using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-038L · AP-017 koi shoal swim loop (FBX custom props <c>AP017_swim = loop</c>, radius 0.8–1.6 m, 0.15 m/s): the shoal
    /// card group circles under the pond surface. The placement point lies on the circle; the radius, the start angle and the
    /// turning direction come from a hash of that point (deterministic, no RNG, no allocation). The card sheet's head is local
    /// −X (AP-017 koi atlas: head left, dorsal up), so −X is kept on the direction of travel. Runs in Play only — the scene
    /// file keeps the placement pose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArtSwimLoop : MonoBehaviour
    {
        [SerializeField] private float _radiusMin = 0.8f;
        [SerializeField] private float _radiusMax = 1.6f;
        [SerializeField] private float _speed = 0.15f;

        private Vector3 _centre;
        private float _radius;
        private float _angle;
        private float _direction = 1f;
        private bool _started;

        public float RadiusMin => _radiusMin;
        public float RadiusMax => _radiusMax;
        public float Speed => _speed;
        /// <summary>The radius this instance swims at (valid after the first enable).</summary>
        public float Radius => _radius;

        public void Configure(float radiusMin, float radiusMax, float speed)
        {
            _radiusMin = Mathf.Max(0.05f, Mathf.Min(radiusMin, radiusMax));
            _radiusMax = Mathf.Max(_radiusMin, Mathf.Max(radiusMin, radiusMax));
            _speed = Mathf.Max(0f, speed);
        }

        private void OnEnable()
        {
            if (_started) return;
            Vector3 p = transform.position;
            uint h = Hash(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.z * 100f));
            _radius = Mathf.Lerp(_radiusMin, _radiusMax, (h & 0xFFFF) / 65535f);
            _angle = ((h >> 16) & 0x7FFF) / 32767f * 2f * Mathf.PI;
            _direction = (h & 0x80000000u) != 0 ? 1f : -1f;
            _centre = p - new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle)) * _radius;
            _started = true;
            Apply();
        }

        private void Update()
        {
            if (_radius <= 0f) return;
            _angle += _direction * _speed / _radius * Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            float c = Mathf.Cos(_angle), s = Mathf.Sin(_angle);
            transform.position = _centre + new Vector3(c, 0f, s) * _radius;
            var travel = new Vector3(-s, 0f, c) * _direction;
            // local −X on the travel direction: right = −travel, forward = right × up (Unity: right = up × forward)
            Vector3 forward = Vector3.Cross(-travel, Vector3.up);
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private static uint Hash(int x, int z)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return h;
            }
        }
    }
}
