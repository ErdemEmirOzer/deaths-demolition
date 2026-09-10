using DD.Prototype.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Prototype.World
{
    /// <summary>
    /// Light, fog, sky, post-processing and the drifting petals.
    ///
    /// THE ONE HARD PROBLEM: REQ-103 WANTS FOG, REQ-104 FORBIDS IT
    /// -----------------------------------------------------------
    /// "Soft but tense, light distant fog, drifting petals and pollen" and "fog, particles
    /// and plants must not obscure enemies or the aim direction" are the same paragraph.
    /// They are only compatible if the fog is placed OUTSIDE combat range, and that range
    /// is knowable here, because the camera is fixed:
    ///
    ///     camera offset from the player   (0, 15, -13)  ->  19.8 m to the player
    ///     furthest engaged enemy           ~25 m from the player
    ///     therefore furthest thing that matters  ~45 m from the camera
    ///     URP shadow distance                     45 m   (already set to exactly this)
    ///
    /// So LINEAR fog starting at <see cref="FogStart"/> = 52 m is mathematically incapable
    /// of tinting a single pixel of anything the player is fighting, while still veiling
    /// the far side of a 90 m map (the far corner is ~90 m from the camera). Exponential
    /// fog cannot do this - it has density everywhere, including on the enemy two metres
    /// away - which is why the mode is Linear and is not a matter of taste.
    ///
    /// The particles are solved by count and size rather than by cleverness: about 440
    /// alive over 7 700 m2, none larger than 18 cm. At the fixed 20 m camera distance an
    /// 18 cm billboard covers roughly five pixels at 1080p. There is no arrangement of
    /// them that hides a zombie.
    ///
    /// FOG IS A LIGHTING SETTING, NOT A VOLUME OVERRIDE
    /// ------------------------------------------------
    /// URP reads fog from <see cref="RenderSettings"/> (the Lighting window), not from a
    /// Volume component - there is no fog override in URP 14. Setting it here is the only
    /// place it can be set.
    /// </summary>
    public static class WorldAtmosphere
    {
        // ------------------------------------------------------------------ numbers
        // PROTOTYPE-ONLY, all invented. The request describes the mood in words only.

        /// <summary>Metres from the CAMERA at which fog starts. See the class summary.</summary>
        public const float FogStart = 52f;

        public const float FogEnd = 138f;

        public static readonly Color FogColor = new Color(0.694f, 0.714f, 0.706f);

        /// <summary>Overcast key: low contrast, slightly warm, from the south-west.</summary>
        public static readonly Vector3 SunEuler = new Vector3(42f, 25f, 0f);

        public const float SunIntensity = 1.15f;
        public const float SunShadowStrength = 0.5f;

        public static readonly Color SunColor = new Color(1.0f, 0.971f, 0.925f);

        public static readonly Color AmbientSky = new Color(0.616f, 0.655f, 0.702f);
        public static readonly Color AmbientEquator = new Color(0.447f, 0.470f, 0.420f);
        public static readonly Color AmbientGround = new Color(0.216f, 0.231f, 0.192f);

        /// <summary>Post-processing. Kept in code so the .asset and the runtime fallback agree.</summary>
        public static class PostFx
        {
            /// <summary>Neutral, NOT ACES. ACES desaturates and darkens exactly the loud,
            /// saturated colours that REQ-105 needs to stay loud.</summary>
            public const int TonemappingMode = 1;

            public const float Contrast = 8f;
            public const float Saturation = -6f;
            public const float PostExposure = 0f;

            public const float WhiteBalanceTemperature = -8f;
            public const float WhiteBalanceTint = 3f;

            public const float VignetteIntensity = 0.20f;
            public const float VignetteSmoothness = 0.45f;

            /// <summary>Above pure white. The world tops out under 1.0, so only genuinely
            /// emissive gameplay objects - gold, the Special Weapon beacon, warnings -
            /// can ever bloom. That is REQ-105 enforced by a threshold.</summary>
            public const float BloomThreshold = 1.25f;

            public const float BloomIntensity = 0.5f;
            public const float BloomScatter = 0.62f;

            public static readonly Color ColorFilter = new Color(1f, 0.99f, 0.96f);
        }

        // Particle budget. The comment above is only true if these stay small.
        private const int PetalsGlobalMax = 110;
        private const int PetalsGardenMax = 130;
        private const int PollenMax = 240;
        private const float PetalSizeMax = 0.18f;

        public static void Build(Transform root, WorldPalette palette, VolumeProfile profile)
        {
            ApplyLighting(root);
            ApplyEnvironment();
            EnsureVolume(root, profile);
            BuildParticles(WorldPiece.Group(root, "Atmosphere"), palette);
        }

        // ------------------------------------------------------------------ lighting

        private static void ApplyLighting(Transform root)
        {
            var go = new GameObject("Sun (overcast spring afternoon)");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(SunEuler.x, SunEuler.y, SunEuler.z);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = SunColor;
            light.intensity = SunIntensity;

            // Soft shadows at half strength: an overcast sky has no hard sun, and a weak
            // shadow still grounds every object without eating the enemy silhouettes.
            light.shadows = LightShadows.Soft;
            light.shadowStrength = SunShadowStrength;
            light.shadowBias = 0.06f;
            light.shadowNormalBias = 0.5f;
            light.shadowNearPlane = 0.2f;

            RenderSettings.sun = light;
        }

        /// <summary>
        /// Fog, ambient and sky - the parts of the atmosphere that live in the scene's
        /// lighting settings rather than on a GameObject. Public because the editor tool
        /// calls it before saving Run.unity, so the settings are stored IN the scene file
        /// and the map looks right the moment it is opened, not only after Awake.
        /// </summary>
        public static void ApplyEnvironment()
        {
            ApplyFog();

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.55f;

            Shader skyShader = PrototypeShaders.Skybox;
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "World/OvercastSky" };
                sky.SetFloat("_SunDisk", 0f);              // no disk: it is overcast
                sky.SetFloat("_AtmosphereThickness", 1.55f);
                sky.SetColor("_SkyTint", new Color(0.678f, 0.702f, 0.722f));
                sky.SetColor("_GroundColor", new Color(0.498f, 0.490f, 0.451f));
                sky.SetFloat("_Exposure", 1.05f);
                RenderSettings.skybox = sky;
            }
        }

        private static void ApplyFog()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
        }

        // ------------------------------------------------------------------ volume

        /// <summary>
        /// Global Volume. Uses the authored profile when there is one (the editor path
        /// assigns Assets/Settings/DD_GlobalVolumeProfile.asset) and otherwise builds an
        /// identical one in memory, so a runtime-only build looks the same as the editor.
        /// </summary>
        public static Volume EnsureVolume(Transform root, VolumeProfile profile)
        {
            var go = new GameObject("Global Volume");
            go.transform.SetParent(root, false);

            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;

            if (profile != null)
            {
                volume.sharedProfile = profile;
                return volume;
            }

            VolumeProfile runtime = ScriptableObject.CreateInstance<VolumeProfile>();
            runtime.name = "DD_GlobalVolumeProfile (runtime)";
            ApplyPostFx(runtime);
            volume.sharedProfile = runtime;
            return volume;
        }

        /// <summary>Writes <see cref="PostFx"/> into a profile. Shared by both paths.</summary>
        public static void ApplyPostFx(VolumeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            var tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = (TonemappingMode)PostFx.TonemappingMode;

            var color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.overrideState = true;
            color.postExposure.value = PostFx.PostExposure;
            color.contrast.overrideState = true;
            color.contrast.value = PostFx.Contrast;
            color.saturation.overrideState = true;
            color.saturation.value = PostFx.Saturation;
            color.colorFilter.overrideState = true;
            color.colorFilter.value = PostFx.ColorFilter;

            var white = GetOrAdd<WhiteBalance>(profile);
            white.temperature.overrideState = true;
            white.temperature.value = PostFx.WhiteBalanceTemperature;
            white.tint.overrideState = true;
            white.tint.value = PostFx.WhiteBalanceTint;

            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = PostFx.VignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = PostFx.VignetteSmoothness;

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = PostFx.BloomThreshold;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = PostFx.BloomIntensity;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = PostFx.BloomScatter;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = true;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet(out T existing) ? existing : profile.Add<T>();
        }

        // ------------------------------------------------------------------ particles

        private static void BuildParticles(Transform group, WorldPalette palette)
        {
            Material petalMat = ParticleMaterial("World/PetalParticle", new Color(0.925f, 0.702f, 0.780f, 0.85f));
            Material petalDeepMat = ParticleMaterial("World/PetalParticleDeep", new Color(0.741f, 0.596f, 0.757f, 0.8f));
            Material pollenMat = ParticleMaterial("World/PollenParticle", new Color(0.949f, 0.925f, 0.804f, 0.55f));

            // Thin drift over the whole map: enough to feel alive anywhere.
            MakeDrift(group, "SakuraPetals_Global", petalMat,
                new Vector3(0f, 12f, 0f), new Vector3(86f, 1f, 86f),
                11f, 9.5f, PetalsGlobalMax, 0.09f, PetalSizeMax, 0.055f);

            // Denser over the east garden, because that is where the trees are.
            MakeDrift(group, "SakuraPetals_EastGarden", petalDeepMat,
                new Vector3(31f, 10f, 0f), new Vector3(26f, 1f, 52f),
                15f, 8.5f, PetalsGardenMax, 0.08f, PetalSizeMax, 0.06f);

            // Pollen: near-invisible individually, reads as haze in motion.
            MakeDrift(group, "Pollen", pollenMat,
                new Vector3(0f, 3.2f, 0f), new Vector3(86f, 5f, 86f),
                20f, 12f, PollenMax, 0.03f, 0.06f, 0.004f);
        }

        private static void MakeDrift(Transform parent, string name, Material material,
            Vector3 centre, Vector3 boxSize, float rate, float lifetime, int maxParticles,
            float sizeMin, float sizeMax, float gravity)
        {
            var go = new GameObject(name);

            // INACTIVE UNTIL FULLY CONFIGURED - this is not tidiness, it is the fix for
            // seven asserts per build. AddComponent<ParticleSystem> on a LIVE GameObject
            // starts the system immediately (playOnAwake defaults to true), and the very
            // next line, MainModule.duration, then throws
            //     "Setting the duration while system is still playing is not supported."
            // An inactive object has no playing system to complain, and the whole module
            // set below is written before the first Update ever sees it. This is the same
            // idiom FeedbackEffectFactory.NewChild and EnemyEffects.CreateTemplate already
            // use; this one call site was the odd one out.
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;

            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = Color.white;

            // Prewarm, or the map is bare for the first ten seconds of every run.
            main.prewarm = true;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.55f);

            // ALL THREE AXES MUST BE IN THE SAME MinMaxCurve MODE. x and z above are
            // TwoConstants; y left at its default is Constant, and Unity answers with
            //     "Particle Velocity curves must all be in the same mode"
            // once per system PER FRAME - 8322 console errors in a single run, which on
            // its own fails acceptance criterion 27.
            //
            // The range is symmetric about zero on purpose: the old Constant y was 0, so
            // the mean drift is unchanged and the descent still comes from
            // main.gravityModifier. The +-0.07 only adds the waver a falling petal has.
            velocity.y = new ParticleSystem.MinMaxCurve(-0.07f, 0.07f);

            velocity.z = new ParticleSystem.MinMaxCurve(-0.45f, 0.45f);

            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.6f, 1.6f);

            // Fade in and out, so nothing ever pops into existence in front of the player.
            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.16f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.alignment = ParticleSystemRenderSpace.View;

            // Hard ceiling on apparent size. Even if something later scales the system,
            // a petal can never grow into a screen-filling blob and break REQ-104.
            renderer.maxParticleSize = 0.02f;

            // Every module is written. NOW the system may come to life: playOnAwake and
            // prewarm do their work on activation, so the map is not bare on frame one.
            go.SetActive(true);
        }

        private static Material ParticleMaterial(string name, Color color)
        {
            Shader shader = PrototypeShaders.ParticlesUnlit;

            var mat = new Material(shader) { name = name };

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }

            DD.Prototype.CameraSystem.OccluderFadeState.MakeTransparent(mat);

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            return mat;
        }
    }
}
