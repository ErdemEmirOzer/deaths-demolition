using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019 · A pooled particle burst whose sprites come from an AP-010 atlas or card
    /// (armour fragments from the 2×2 armorbreak atlas, smoke puffs from the smoke card).
    /// The ParticleSystem is configured once from the catalog row (texture-sheet grid,
    /// material); Play only sets the count, size and lifetime.
    /// </summary>
    public sealed class AtlasBurstEffect : PooledEffect
    {
        private ParticleSystem _particles;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.EmissionModule _emission;
        private bool _configured;

        public bool IsConfigured => _configured;

        // WP-073: a pooled clone does not carry the template's cached modules; the first GetComponent of a clone
        // allocates its managed wrapper, so it happens here (FeedbackPoolWarmer activates every instance at configure).
        private void Awake()
        {
            CacheModules();
        }

        /// <summary>Builds the system for <paramref name="row"/>. Called once on the pool template.</summary>
        public void Configure(VfxCatalogEntry row, bool gravity, float startSpeedMin, float startSpeedMax, float coneAngle)
        {
            _particles = GetComponent<ParticleSystem>();
            if (_particles == null)
            {
                _particles = gameObject.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = _particles.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(startSpeedMin, startSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = gravity ? 0.9f : -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.05f;

            ParticleSystem.RotationOverLifetimeModule rotation = _particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-5f, 5f);

            ParticleSystem.ColorOverLifetimeModule colour = _particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            // Atlas cells as sprites: random cell, no animation over life (the manifest: fragments 2–6 cm).
            ParticleSystem.TextureSheetAnimationModule sheet = _particles.textureSheetAnimation;
            bool atlas = row.Columns * row.Rows > 1;
            sheet.enabled = atlas;
            if (atlas)
            {
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = row.Columns;
                sheet.numTilesY = row.Rows;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                // WP-073 B2: the start frame is normalised over the sheet (0 … 1) — the frame count here clamped every particle to
                // the last cell (Wp073ImpactTests atlas render check proves the normalisation).
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.cycleCount = 1;
            }

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = row.Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.1f;
            renderer.sortMode = ParticleSystemSortMode.None;
            _configured = true;
        }

        private void CacheModules()
        {
            if (_particles == null)
            {
                _particles = GetComponent<ParticleSystem>();
            }

            if (_particles != null)
            {
                _main = _particles.main;
                _emission = _particles.emission;
                _configured = true;
            }
        }

        public void Play(int count, float sizeMin, float sizeMax, float lifetime)
        {
            CacheModules();
            if (_particles == null)
            {
                Lifetime = lifetime;
                RestartLife();
                return;
            }

            _main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            _main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime * 0.95f);
            _particles.Clear(true);
            _particles.Emit(Mathf.Clamp(count, 1, _main.maxParticles));
            Lifetime = lifetime;
            RestartLife();
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            if (_particles != null)
            {
                _particles.Clear(true);
            }
        }
    }
}
