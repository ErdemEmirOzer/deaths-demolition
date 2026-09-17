using System.Collections.Generic;
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
    /// the far side of a 90 m map (the far corner is ~90 m from the camera; WP-015 lightened
    /// the ramp to Presentation A's mist density, see <see cref="FogEnd"/>). Exponential
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

        /// <summary>
        /// WP-015 (DEC-076 final palette = Presentation A + DEC-068 "hafif sisli"): the WP-013
        /// art review found the 52→138 m ramp heavy at the ~90 m overview (44 % fog at 90 m,
        /// 100 % at the map's far edge). Presentation A's mist is 0.003/m finite; a linear ramp
        /// from 52 m that matches that density at 90 m (24 %) ends at ~210 m (54 % at 138 m,
        /// where the old ramp was opaque). The start is unchanged: REQ-104's combat range
        /// (≤ 45 m from the camera) still receives no fog at all. WP-013 values are kept
        /// below for the A/B captures.
        /// </summary>
        public const float FogEnd = 210f;

        /// <summary>
        /// Presentation A mist colour, linear (0.63, 0.68, 0.73) → sRGB (0.815, 0.843, 0.870)
        /// ≈ #D0D7DE (WP-011 style-guide draft candidate, decided by DEC-076). Script colours
        /// are sRGB here; the shared sky horizon follows this value (ArtSkyBuilder).
        /// </summary>
        public static readonly Color FogColor = new Color(0.815f, 0.843f, 0.870f);

        /// <summary>WP-013 fog end, reference for the WP-015 A/B captures only.</summary>
        public const float FogEndWp013 = 138f;

        /// <summary>WP-013 fog colour (Presentation A background × 0.65), reference for the WP-015 A/B captures only.</summary>
        public static readonly Color FogColorWp013 = new Color(0.694f, 0.714f, 0.706f);

        /// <summary>
        /// Resources name of the shared overcast sky material
        /// (Assets/_Art/Resources/DD_Sky_Overcast_v1.mat, written by the art pipeline). Kept
        /// as a string on purpose: DD.Prototype must not reference DD.Art.
        /// </summary>
        public const string SharedSkyResourceName = "DD_Sky_Overcast_v1";

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
            /// <summary>WP-013: the grade below is rendered on the gameplay camera (see ConfigureCamera).</summary>
            public const bool Enabled = true;

            /// <summary>Neutral, NOT ACES. ACES desaturates and darkens exactly the loud,
            /// saturated colours that REQ-105 needs to stay loud.</summary>
            public const int TonemappingMode = 1;

            public const float Contrast = 8f;

            // WP-013: the grade is rendered for the first time (renderer PostProcessData was
            // unassigned before). Measured on the gameplay pose: saturation −6 and white
            // balance −8 together took 27 % off the gold band's separation from the earth
            // (REQ-105 measurement, AtmosphereVfxTests); halved to the values below the
            // separation stays within 10 % of the ungraded frame while the overcast cool
            // cast (DEC-068) remains. Still PROTOTYPE-ONLY; the palette decision is open.
            public const float Saturation = -2f;
            public const float PostExposure = 0f;

            public const float WhiteBalanceTemperature = -3f;
            public const float WhiteBalanceTint = 1f;

            public const float VignetteIntensity = 0.20f;
            public const float VignetteSmoothness = 0.45f;

            /// <summary>Above pure white. The world tops out under 1.0, so only genuinely
            /// emissive gameplay objects - gold, the Special Weapon beacon, warnings -
            /// can ever bloom. That is REQ-105 enforced by a threshold.</summary>
            public const float BloomThreshold = 1.25f;

            public const float BloomIntensity = 0.5f;
            public const float BloomScatter = 0.62f;
            /// <summary>WP-038J: 6 → 4 mip iterations (setPass 16 → 10 for the chain); the frame diff against the WP-029 references is below the JND.</summary>
            public const int BloomMaxIterations = 4;
            /// <summary>WP-038J: bilinear upsampling (URP default) — bicubic bought nothing the reference frames show.</summary>
            public const bool BloomHighQualityFiltering = false;

            public static readonly Color ColorFilter = new Color(1f, 0.99f, 0.96f);
        }

        /// <summary>
        /// WP-013 · ADR-013-1 wind contract. PROTOTYPE-ONLY numbers; the schema (what each
        /// global means) is the contract, the values are a starting point for the art
        /// director. Consumers: DD_Foliage_Wind (vertex sway/flutter), DD_Water_v1
        /// (ripple direction), the drift particles (WindZone force).
        /// </summary>
        public static class Wind
        {
            /// <summary>Compass direction the wind blows TOWARDS, degrees from +X towards +Z (west → east-ish, over the garden).</summary>
            public const float DirectionDegrees = 24f;

            /// <summary>Wind strength 0..1 with no gust. A breeze, not a storm (REQ-104: leaves must not read as motion noise).</summary>
            public const float BaseStrength = 0.35f;

            /// <summary>Gust amplitude added on top of the base by slow Perlin noise.</summary>
            public const float GustStrength = 0.4f;

            public const float GustPeriodSeconds = 9f;

            /// <summary>How far the direction wanders with the gusts.</summary>
            public const float DirectionWanderDegrees = 22f;

            /// <summary>Tip displacement in metres at strength 1 for a vertex with sway 1 (a 6 m crown leans ~0.28 m).</summary>
            public const float SwayAmplitude = 0.28f;

            public const float SwayFrequency = 0.45f;

            /// <summary>Leaf-edge flutter along the normal, metres, at strength 1.</summary>
            public const float FlutterAmplitude = 0.035f;

            public const float FlutterFrequency = 2.6f;

            /// <summary>WindZone.windMain at strength 1 (particle External Forces multiplier 1).</summary>
            public const float ParticleForce = 0.9f;
        }

        /// <summary>Resources names of the WP-013 drift prefabs (Assets/_Art/VFX/Resources). Loaded by name: DD.Prototype must not reference DD.Art.</summary>
        public const string PollenResourceName = "DD_Pollen";
        public const string PetalsResourceName = "DD_SakuraPetals";

        public enum DriftKind
        {
            Pollen = 0,
            Petals = 1
        }

        /// <summary>One placed drift field. Densities are 0..1 of the prefab's own budget.</summary>
        public struct DriftRegion
        {
            public DriftKind Kind;
            public AtmosphereDrift.Region Region;
            public Vector3 Centre;
            public Vector3 Box;
            public float Density;

            public DriftRegion(DriftKind kind, AtmosphereDrift.Region region, Vector3 centre, Vector3 box, float density)
            {
                Kind = kind;
                Region = region;
                Centre = centre;
                Box = box;
                Density = density;
            }
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
            EnsureWind(root);
            BuildDrift(WorldPiece.Group(root, "Atmosphere"), DefaultRunRegions(), palette);
        }

        /// <summary>
        /// WP-014 · The same lighting, environment, volume, wind and drift as
        /// <see cref="Build"/>, for a world that brings its own regions (the AP-005 scene
        /// derives them from placement.json) and no procedural palette.
        /// </summary>
        public static void BuildFor(Transform root, VolumeProfile profile, DriftRegion[] regions)
        {
            ApplyLighting(root);
            ApplyEnvironment();
            EnsureVolume(root, profile);
            EnsureWind(root);
            BuildDrift(WorldPiece.Group(root, "Atmosphere"), regions ?? DefaultRunRegions(), null);
        }

        /// <summary>
        /// The Run map's region table (PROTOTYPE-ONLY; WP-014 replaces it with region data):
        /// pollen everywhere, a thin petal drift everywhere, a dense one over the east garden.
        /// </summary>
        public static DriftRegion[] DefaultRunRegions()
        {
            return new[]
            {
                new DriftRegion(DriftKind.Pollen, AtmosphereDrift.Region.Global, new Vector3(0f, 3.2f, 0f), new Vector3(86f, 5f, 86f), 1f),
                new DriftRegion(DriftKind.Petals, AtmosphereDrift.Region.Global, new Vector3(0f, 12f, 0f), new Vector3(86f, 1f, 86f), 0.45f),
                new DriftRegion(DriftKind.Petals, AtmosphereDrift.Region.EastGarden, new Vector3(31f, 10f, 0f), new Vector3(26f, 1f, 52f), 1f)
            };
        }

        /// <summary>
        /// Region table for an authored slice: pollen over the whole footprint, petals only
        /// where <paramref name="sakura"/> (the crowns' bounds) is, none when there are no crowns.
        /// </summary>
        public static DriftRegion[] RegionsFor(Bounds footprint, Bounds? sakura)
        {
            var list = new List<DriftRegion>(2)
            {
                new DriftRegion(DriftKind.Pollen, AtmosphereDrift.Region.Global,
                    new Vector3(footprint.center.x, footprint.min.y + 3.2f, footprint.center.z),
                    new Vector3(footprint.size.x, 5f, footprint.size.z), 1f)
            };

            if (sakura.HasValue)
            {
                Bounds b = sakura.Value;
                list.Add(new DriftRegion(DriftKind.Petals, AtmosphereDrift.Region.EastGarden,
                    new Vector3(b.center.x, b.max.y + 2f, b.center.z),
                    new Vector3(b.size.x + 8f, 1f, b.size.z + 8f), 1f));
            }

            return list.ToArray();
        }

        // ------------------------------------------------------------------ wind

        /// <summary>The one wind driver of the scene (created under <paramref name="root"/> when missing).</summary>
        public static WindDriver EnsureWind(Transform root)
        {
            WindDriver existing = Object.FindObjectOfType<WindDriver>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("Wind (ADR-013-1)");
            go.transform.SetParent(root, false);
            return go.AddComponent<WindDriver>();
        }

        // ------------------------------------------------------------------ camera

        /// <summary>
        /// WP-013: the gameplay camera renders the Global Volume's post-processing (it never
        /// did before: URP's additional camera data defaults to off, which is why the
        /// authored grade was "effectively off" in WP-011/012). MSAA stays the anti-aliasing;
        /// no FXAA/SMAA on top.
        /// </summary>
        public static void ConfigureCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = PostFx.Enabled;
            data.antialiasing = AntialiasingMode.None;
        }

        // ------------------------------------------------------------------ lighting

        /// <summary>WP-029: name of the Mixed-mode sun the world scene serialises for the lightmap bake (ArtLightingBake).</summary>
        public const string BakedSunName = "Sun (baked, WP-029)";

        /// <summary>
        /// WP-029 · A world scene that carries its own baked sun (Mixed: direct realtime with
        /// shadows, indirect in the lightmaps and probes) is the one light; a second runtime sun
        /// would double the direct term and shade the lightmapped world twice. Returns it, or null.
        /// </summary>
        public static Light FindBakedSun()
        {
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l != null && l.type == LightType.Directional && l.name == BakedSunName) return l;
            }

            return null;
        }

        /// <summary>The sun parameters every path uses (runtime sun and the baked sun the bake builder writes).</summary>
        public static void ConfigureSun(Light light)
        {
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
        }

        private static void ApplyLighting(Transform root)
        {
            Light baked = FindBakedSun();
            if (baked != null)
            {
                baked.enabled = true;
                RenderSettings.sun = baked;
                return;
            }

            var go = new GameObject("Sun (overcast spring afternoon)");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(SunEuler.x, SunEuler.y, SunEuler.z);

            Light light = go.AddComponent<Light>();
            ConfigureSun(light);
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

            // WP-012 (DEC-068, ADR-011-4 closure): the shared overcast sky authored by the art
            // pipeline (a latitude-longitude gradient: no sun disk, no horizon glow) is the
            // sky of every scene. Loaded by name so this assembly stays independent of
            // DD.Art; the procedural sky below is only the fallback when the asset is absent.
            var shared = Resources.Load<Material>(SharedSkyResourceName);
            if (shared != null)
            {
                RenderSettings.skybox = shared;
                return;
            }

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
            // WP-038J (M3 K1 setPass): the bloom chain was 16 of the world side's 68 setPass calls (6 mip iterations, bicubic
            // upsampling). Four iterations keep every radius the 1080p frame resolves at scatter 0.62 (the last two mips are
            // 34 px and 17 px wide — a screen-sized haze the overcast frame has no source for at threshold 1.25); bilinear
            // upsampling is the URP default. Both measured against the WP-029 reference frames (planning/04 §WP-038J).
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = PostFx.BloomHighQualityFiltering;
            bloom.maxIterations.overrideState = true;
            bloom.maxIterations.value = PostFx.BloomMaxIterations;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet(out T existing) ? existing : profile.Add<T>();
        }

        // ------------------------------------------------------------------ particles

        /// <summary>
        /// Places the drift fields. The WP-013 prefabs (Resources, authored by the art
        /// pipeline) are the source; when a prefab is absent — a fresh checkout before the
        /// pipeline ran — the field is built in code exactly as WP-002 built it, so the
        /// map is never bare. Returns the number of fields placed.
        /// </summary>
        public static int BuildDrift(Transform group, IList<DriftRegion> regions, WorldPalette palette)
        {
            if (regions == null || regions.Count == 0)
            {
                return 0;
            }

            var pollenPrefab = Resources.Load<GameObject>(PollenResourceName);
            var petalsPrefab = Resources.Load<GameObject>(PetalsResourceName);

            Material petalMat = null, petalDeepMat = null, pollenMat = null;
            int placed = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                DriftRegion r = regions[i];
                GameObject prefab = r.Kind == DriftKind.Pollen ? pollenPrefab : petalsPrefab;
                if (prefab != null)
                {
                    GameObject go = Object.Instantiate(prefab, r.Centre, Quaternion.identity, group);
                    go.name = prefab.name + "_" + r.Region;
                    AtmosphereDrift drift = go.GetComponent<AtmosphereDrift>();
                    if (drift != null)
                    {
                        drift.Configure(r.Region, r.Density, r.Box);
                    }

                    placed++;
                    continue;
                }

                // Legacy in-code field (WP-002 numbers), scaled by the region density.
                if (r.Kind == DriftKind.Pollen)
                {
                    pollenMat = pollenMat ?? ParticleMaterial("World/PollenParticle", new Color(0.949f, 0.925f, 0.804f, 0.55f));
                    MakeDrift(group, "Pollen_" + r.Region, pollenMat, r.Centre, r.Box,
                        20f * r.Density, 12f, Mathf.Max(1, Mathf.RoundToInt(PollenMax * r.Density)), 0.03f, 0.06f, 0.004f);
                }
                else if (r.Region == AtmosphereDrift.Region.EastGarden)
                {
                    petalDeepMat = petalDeepMat ?? ParticleMaterial("World/PetalParticleDeep", new Color(0.741f, 0.596f, 0.757f, 0.8f));
                    MakeDrift(group, "SakuraPetals_" + r.Region, petalDeepMat, r.Centre, r.Box,
                        15f * r.Density, 8.5f, Mathf.Max(1, Mathf.RoundToInt(PetalsGardenMax * r.Density)), 0.08f, PetalSizeMax, 0.06f);
                }
                else
                {
                    petalMat = petalMat ?? ParticleMaterial("World/PetalParticle", new Color(0.925f, 0.702f, 0.780f, 0.85f));
                    MakeDrift(group, "SakuraPetals_" + r.Region, petalMat, r.Centre, r.Box,
                        11f / 0.45f * r.Density, 9.5f, Mathf.Max(1, Mathf.RoundToInt(PetalsGlobalMax / 0.45f * r.Density)), 0.09f, PetalSizeMax, 0.055f);
                }

                placed++;
            }

            return placed;
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
