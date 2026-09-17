using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-019b · Blood droplets: a pooled ParticleSystem in mesh render mode with GPU
    /// instancing on the shared quad, so every droplet of a burst is one instanced draw
    /// (transform + colour from the particle instance buffer, <c>DD/VFX Flipbook</c>
    /// procedural instancing). Simulation stays on the CPU (Shuriken; VFX Graph would be a
    /// package addition, which WP-019 may not make). Droplets fall under 1.5 g and die on
    /// world contact (art-director brief: no bounce), directed by the hit normal and the
    /// shot direction. One material for every droplet system: the importer's synthesized
    /// <c>blood_droplet</c> row (tint baked into the material, no property block, so
    /// instancing is never broken).
    /// </summary>
    public sealed class BloodDropletEffect : PooledEffect
    {
        public const int MaxParticles = 48;

        private ParticleSystem _particles;
        private ParticleSystem.MainModule _main;
        private ParticleSystem.ShapeModule _shape;
        private bool _configured;

        public bool IsConfigured => _configured;
        public bool GpuInstanced { get; private set; }

        /// <summary>Builds the system from the droplet row. Called once on the pool template; instances clone it.</summary>
        public void Configure(VfxCatalogEntry row)
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
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 0) });

            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.03f;

            // Stretch a little along the velocity: a droplet, not a disc.
            ParticleSystem.SizeOverLifetimeModule size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 0.9f), new Keyframe(1f, 0.3f)));

            ParticleSystem.ColorOverLifetimeModule colour = _particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            // Die on world contact, no bounce (brief 1).
            ParticleSystem.CollisionModule collision = _particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.collidesWith = PrototypeCollisionMask();
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.maxCollisionShapes = 64;

            // AP-010B v003 droplet sheet (4×4, 16 static sprites): a random cell per particle; the material's grid is 1×1
            // so the shader passes the particle UVs through (the v002 flesh decal fallback is a single cell).
            ParticleSystem.TextureSheetAnimationModule sheet = _particles.textureSheetAnimation;
            bool atlas = row.Columns * row.Rows > 1;
            sheet.enabled = atlas;
            if (atlas)
            {
                sheet.mode = ParticleSystemAnimationMode.Grid;
                sheet.numTilesX = row.Columns;
                sheet.numTilesY = row.Rows;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, row.Columns * row.Rows);
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                sheet.cycleCount = 1;
            }

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = VfxLibrary.Quad;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = row.Material;
            renderer.enableGPUInstancing = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;
            GpuInstanced = renderer.enableGPUInstancing && renderer.renderMode == ParticleSystemRenderMode.Mesh;
            _configured = true;
        }

        private static LayerMask PrototypeCollisionMask()
        {
            int environment = LayerMask.NameToLayer("Environment");
            int mask = 1 << 0;   // Default (the stand-in ground)
            if (environment >= 0) mask |= 1 << environment;
            return mask;
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
                _shape = _particles.shape;
                _configured = true;
                var renderer = GetComponent<ParticleSystemRenderer>();
                GpuInstanced = renderer != null && renderer.enableGPUInstancing && renderer.renderMode == ParticleSystemRenderMode.Mesh;
            }
        }

        /// <summary>Emits <paramref name="count"/> droplets along this transform's forward (set by the caller from normal + shot direction).</summary>
        public void Play(int count, float sizeMin, float sizeMax, float speedMin, float speedMax, float lifetime)
        {
            CacheModules();
            if (_particles == null)
            {
                Lifetime = lifetime;
                RestartLife();
                return;
            }

            _main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            _main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            _main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.75f, lifetime);
            _particles.Clear(true);
            _particles.Play(true);
            _particles.Emit(Mathf.Clamp(count, 1, MaxParticles));
            Lifetime = lifetime + 0.05f;
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
