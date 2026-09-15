using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// WP-013 · ADR-013-1: the one wind in the world. Everything that moves with the wind
    /// (the foliage shader, the pond ripples, the drifting pollen and petals) reads the
    /// same two shader globals, written here and nowhere else:
    ///
    ///   _DD_WindVector  (dir.x, dir.z, strength 0..1, wind time)
    ///   _DD_WindParams  (sway amplitude m, sway Hz, flutter amplitude m, flutter Hz)
    ///
    /// The numbers live in <see cref="WorldAtmosphere.Wind"/> so the atmosphere is one
    /// file to read. The wind clock is its own accumulator of scaled deltaTime: pausing
    /// the game (timeScale 0) freezes the leaves, which is what a pause should look like,
    /// and the Editor preview of a scene without a driver sees the calm default below.
    ///
    /// Particles take the same wind through a directional <see cref="WindZone"/> (their
    /// External Forces module), so a gust that bends the bamboo also carries the petals.
    /// </summary>
    public static class WorldWind
    {
        public static readonly int WindVectorId = Shader.PropertyToID("_DD_WindVector");
        public static readonly int WindParamsId = Shader.PropertyToID("_DD_WindParams");

        /// <summary>Direction (unit, xz), strength and time of the last <see cref="Apply"/>.</summary>
        public static Vector3 Direction { get; private set; } = new Vector3(1f, 0f, 0f);
        public static float Strength { get; private set; }
        public static float Time { get; private set; }

        /// <summary>Writes the globals. Direction is normalised on the horizontal plane; strength clamped to 0..1.</summary>
        public static void Apply(Vector3 direction, float strength, float time)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f)
            {
                direction = Vector3.right;
            }

            direction.Normalize();
            strength = Mathf.Clamp01(strength);

            Direction = direction;
            Strength = strength;
            Time = time;

            Shader.SetGlobalVector(WindVectorId, new Vector4(direction.x, direction.z, strength, time));
            Shader.SetGlobalVector(WindParamsId, new Vector4(
                WorldAtmosphere.Wind.SwayAmplitude,
                WorldAtmosphere.Wind.SwayFrequency,
                WorldAtmosphere.Wind.FlutterAmplitude,
                WorldAtmosphere.Wind.FlutterFrequency));
        }

        /// <summary>Calm: no displacement anywhere. Used when nothing drives the wind (tests, editor preview).</summary>
        public static void ApplyCalm()
        {
            Apply(Vector3.right, 0f, 0f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Globals persist across scene loads; a scene without a driver must not
            // inherit the previous scene's gust.
            ApplyCalm();
        }
    }

    /// <summary>
    /// Drives <see cref="WorldWind"/> every frame: a base breeze plus slow Perlin gusts,
    /// and the matching <see cref="WindZone"/> for the particle systems. One per scene,
    /// created by <see cref="WorldAtmosphere.EnsureWind"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindDriver : MonoBehaviour
    {
        [SerializeField] private float _directionDegrees = WorldAtmosphere.Wind.DirectionDegrees;
        [SerializeField] private float _baseStrength = WorldAtmosphere.Wind.BaseStrength;
        [SerializeField] private float _gustStrength = WorldAtmosphere.Wind.GustStrength;
        [SerializeField] private float _gustPeriodSeconds = WorldAtmosphere.Wind.GustPeriodSeconds;

        [Tooltip("0 freezes every wind consumer (A/B measurements, screenshots).")]
        [SerializeField] private float _enabledScale = 1f;

        private WindZone _zone;
        private float _time;
        private float _seed;

        /// <summary>The scale applied on top of the authored strength; 0 = calm.</summary>
        public float EnabledScale
        {
            get => _enabledScale;
            set => _enabledScale = Mathf.Clamp(value, 0f, 2f);
        }

        public float DirectionDegrees
        {
            get => _directionDegrees;
            set => _directionDegrees = value;
        }

        /// <summary>The strength written on the last Update (after gusts and the enabled scale).</summary>
        public float CurrentStrength { get; private set; }

        private void Awake()
        {
            _zone = GetComponent<WindZone>();
            if (_zone == null)
            {
                _zone = gameObject.AddComponent<WindZone>();
            }

            _zone.mode = WindZoneMode.Directional;
            _zone.windTurbulence = 0.35f;
            _zone.windPulseMagnitude = 0.25f;
            _zone.windPulseFrequency = 0.12f;
            _seed = Random.Range(0f, 100f);
            Step(0f);
        }

        private void OnEnable()
        {
            Step(0f);
        }

        private void OnDisable()
        {
            WorldWind.ApplyCalm();
        }

        private void Update()
        {
            Step(UnityEngine.Time.deltaTime);
        }

        private void Step(float dt)
        {
            _time += dt;

            float period = Mathf.Max(0.5f, _gustPeriodSeconds);
            float gust = Mathf.PerlinNoise(_seed + _time / period, 0.37f);            // 0..1, slow
            float gust2 = Mathf.PerlinNoise(0.71f, _seed + _time / (period * 0.31f)); // faster ripple on top
            float strength = (_baseStrength + _gustStrength * (0.7f * gust + 0.3f * gust2)) * _enabledScale;

            // The direction wanders a little with the gusts so leaves do not all lean the same way forever.
            float wander = (gust - 0.5f) * WorldAtmosphere.Wind.DirectionWanderDegrees;
            float radians = (_directionDegrees + wander) * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));

            CurrentStrength = Mathf.Clamp01(strength);
            WorldWind.Apply(direction, CurrentStrength, _time);

            if (_zone != null)
            {
                _zone.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                _zone.windMain = CurrentStrength * WorldAtmosphere.Wind.ParticleForce;
            }
        }
    }
}
