using UnityEngine;

namespace DD.Prototype.World
{
    /// <summary>
    /// WP-013 · One drifting particle field (pollen, or sakura petals) as a prefab
    /// component. The prefab carries the look (material, sizes, lifetime, curves); this
    /// component carries the PLACEMENT the atmosphere decides at build time — the box the
    /// field covers and how dense it is there — and the hard budget the field may never
    /// exceed whatever a region asks for.
    ///
    /// REGION DENSITY. WP-014 will bring real region data; until then the atmosphere passes
    /// a region tag and a density 0..1 per instance (<see cref="Configure"/>): petals only
    /// in the east garden / courtyard rows, pollen everywhere, thinner over water.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class AtmosphereDrift : MonoBehaviour
    {
        public enum Region
        {
            Global = 0,
            EastGarden = 1,
            Courtyard = 2
        }

        [Header("Budget (hard ceilings, never raised by a region)")]
        [SerializeField] private int _maxParticles = 240;
        [SerializeField] private float _emissionAtDensityOne = 20f;

        [Header("Placement (written by WorldAtmosphere)")]
        [SerializeField] private Region _region = Region.Global;
        [SerializeField, Range(0f, 1f)] private float _density = 1f;
        [SerializeField] private Vector3 _box = new Vector3(86f, 5f, 86f);

        private ParticleSystem _system;

        public int MaxParticles => _maxParticles;
        public float EmissionAtDensityOne => _emissionAtDensityOne;
        public Region RegionTag => _region;
        public float Density => _density;
        public Vector3 Box => _box;
        public ParticleSystem System => _system != null ? _system : (_system = GetComponent<ParticleSystem>());

        /// <summary>Places the field: box centre = this transform, size and density as given. Idempotent.</summary>
        public void Configure(Region region, float density, Vector3 box)
        {
            _region = region;
            _density = Mathf.Clamp01(density);
            _box = box;
            Apply();
        }

        /// <summary>0 stops emission (existing particles fade out on their own lifetime); 1 restores the authored density.</summary>
        public void SetEnabledScale(float scale)
        {
            ParticleSystem ps = System;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = _emissionAtDensityOne * _density * Mathf.Clamp01(scale);
        }

        private void Awake()
        {
            Apply();
        }

        private void Apply()
        {
            ParticleSystem ps = System;
            bool wasPlaying = ps.isPlaying;
            if (wasPlaying)
            {
                // duration/maxParticles cannot be written on a playing system.
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            ParticleSystem.MainModule main = ps.main;
            // The ceiling is the prefab's; a denser region only raises the rate, and the
            // rate × lifetime product can never exceed the ceiling anyway.
            main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(_maxParticles * Mathf.Max(_density, 0.05f)));

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = _box;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = _emissionAtDensityOne * _density;

            if (wasPlaying || (Application.isPlaying && main.playOnAwake))
            {
                ps.Play(true);
            }
        }
    }
}
