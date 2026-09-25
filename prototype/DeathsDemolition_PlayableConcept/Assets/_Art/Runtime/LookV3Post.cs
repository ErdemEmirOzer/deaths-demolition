using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art
{
    /// <summary>
    /// WP-075 C7 · The cinematic post of look v3 by tier and camera mode (WP-075 direction Ek A §7; QUALITY-TIERS-V2 r2). It works
    /// on the Global Volume's instanced profile (never the asset):
    /// <list type="bullet">
    /// <item>Bloom: threshold 1.10, intensity 0.55, scatter 0.68, tint #FFF1DC, 4 iterations, high-quality filtering off (every tier).</item>
    /// <item>Film grain: Thin1, response 0.85 at 0.18 Cinematic / 0.10 High / off in Performance.</item>
    /// <item>Vignette: 0.24 / 0.40 top-down and 0.22 / 0.42 TP / FP, not rounded.</item>
    /// <item>Depth of field: never top-down. Cinematic TP uses Gaussian far 70 → 220 m, radius 1.2. Cinematic FP uses Bokeh f/8,
    /// 28 mm, focus on the aim ray (0.25 s smoothing).</item>
    /// <item>Motion blur: camera-only, 0.12 / clamp 0.03, in Cinematic TP / FP only.</item>
    /// </list>
    /// The World v5 dress root carries it (v6 layout; DD_Look_v5 holds the Cinematic top-down baseline for the editor); it
    /// re-applies on <see cref="QualityTiers.Changed"/> and <see cref="CameraModeService.Changed"/>.
    /// </summary>
    public sealed class LookV3Post : MonoBehaviour
    {
        public enum DepthMode { Off, GaussianFar, BokehAim }

        public struct Rules
        {
            public float Grain, VignetteIntensity, VignetteSmoothness, MotionBlur;
            public DepthMode Depth;
        }

        public const float BloomThreshold = 1.10f, BloomIntensity = 0.55f, BloomScatter = 0.68f;
        public static readonly Color BloomTint = new Color(1f, 0xF1 / 255f, 0xDC / 255f, 1f);
        public const float GrainResponse = 0.85f, GaussianStart = 70f, GaussianEnd = 220f, GaussianRadius = 1.2f;
        public const float BokehAperture = 8f, BokehFocalLength = 28f, FocusSmoothing = 0.25f, MotionClamp = 0.03f;
        public const int BloomIterations = 4;

        /// <summary>The §7 table for a tier and a camera mode.</summary>
        public static Rules RulesFor(QualityTier tier, CameraMode mode)
        {
            bool cinematic = tier == QualityTier.Sinematik, look = mode != CameraMode.TopDown;
            return new Rules
            {
                Grain = QualityTiers.Pick(tier, 0f, 0.10f, 0.18f),
                VignetteIntensity = look ? 0.22f : 0.24f,
                VignetteSmoothness = look ? 0.42f : 0.40f,
                Depth = !cinematic || !look ? DepthMode.Off : mode == CameraMode.FirstPerson ? DepthMode.BokehAim : DepthMode.GaussianFar,
                MotionBlur = cinematic && look ? 0.12f : 0f,
            };
        }

        private VolumeProfile _profile;
        private float _focus = 10f;

        /// <summary>Evidence-only A/B switches for the perf rows (WP-075 perf: planar and bake on their own rows): <c>-ddPlanar off</c>
        /// disables the planar reflection, <c>-ddBake off</c> drops the lightmaps and the baked reflection probes at load (light probes
        /// stay), <c>-ddScreenAtmos off</c> keeps the TP / FP screen atmosphere off, <c>-ddVolumetric off</c> the DEC-102 volumetric light.
        /// Editor PlayMode captures (DEC-102 A/B frames) name them in <see cref="EvidenceOffVariable"/> instead, e.g. "ddVolumetric".
        /// Nothing changes without a flag.</summary>
        public const string PlanarFlag = "-ddPlanar", BakeFlag = "-ddBake", ScreenAtmosFlag = "-ddScreenAtmos", VolumetricFlag = "-ddVolumetric";
        public const string EvidenceOffVariable = "DD_EVIDENCE_OFF";

        private static bool Off(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == flag) return string.Equals(args[i + 1], "off", System.StringComparison.OrdinalIgnoreCase);
            string env = System.Environment.GetEnvironmentVariable(EvidenceOffVariable);
            if (string.IsNullOrEmpty(env)) return false;
            foreach (string name in env.Split(','))
                if (string.Equals(name.Trim(), flag.TrimStart('-'), System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // DEC-102: the raymarched volumetric light (VolumetricLight on this root) replaces the C4 B radial shafts and the §5.2 height fog.
        private void OnEnable() => ScreenAtmosphereFeature.WorldActive = false;

        private void OnDisable() => ScreenAtmosphereFeature.WorldActive = false;

        private void Start()
        {
            // The world's Global Volume (WorldAtmosphere.EnsureVolume, built in the bootstrap's Awake); not the death volume (priority 50).
            Volume volume = null;
            foreach (Volume v in transform.root.GetComponentsInChildren<Volume>(true))
                if (v.isGlobal && v.name == "Global Volume" && (volume == null || v.priority < volume.priority)) volume = v;
            ApplyEvidenceFlags();
            if (volume == null) return;
            _profile = volume.profile;   // instanced copy: the asset stays untouched
            QualityTiers.Changed += OnTier;
            CameraModeService.Changed += OnMode;
            Apply();
        }

        private static void ApplyEvidenceFlags()
        {
            bool planar = Off(PlanarFlag), bake = Off(BakeFlag), screen = Off(ScreenAtmosFlag), volumetric = Off(VolumetricFlag);
            if (!planar && !bake && !screen && !volumetric) return;
            if (volumetric)
                foreach (VolumetricLight v in FindObjectsOfType<VolumetricLight>()) v.enabled = false;
            if (planar)
                foreach (PlanarReflection p in FindObjectsOfType<PlanarReflection>()) p.enabled = false;
            if (bake)
            {
                LightmapSettings.lightmaps = System.Array.Empty<LightmapData>();
                foreach (ReflectionProbe probe in FindObjectsOfType<ReflectionProbe>())
                    if (probe.mode == ReflectionProbeMode.Baked) probe.enabled = false;
            }

            Debug.Log("[LookV3] evidence planar=" + (planar ? "off" : "on") + " bake=" + (bake ? "off" : "on") + " screenAtmos=" + (screen ? "off" : "on") + " volumetric=" + (volumetric ? "off" : "on"));
        }

        private void OnDestroy()
        {
            QualityTiers.Changed -= OnTier;
            CameraModeService.Changed -= OnMode;
        }

        private void OnTier(QualityTier tier) => Apply();
        private void OnMode(CameraModeChange change) => Apply();

        private void Apply()
        {
            if (_profile != null) Write(_profile, RulesFor(QualityTiers.Current, CameraModeService.Current), _focus);
        }

        /// <summary>Writes the rules into a profile (the runtime instance, or DD_Look_v5's baseline from the editor).</summary>
        public static void Write(VolumeProfile profile, Rules r, float focus)
        {
            Bloom bloom = Get<Bloom>(profile);
            bloom.active = true;
            Set(bloom.threshold, BloomThreshold); Set(bloom.intensity, BloomIntensity); Set(bloom.scatter, BloomScatter);
            bloom.tint.overrideState = true; bloom.tint.value = BloomTint;
            bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = false;
            bloom.maxIterations.overrideState = true; bloom.maxIterations.value = BloomIterations;

            FilmGrain grain = Get<FilmGrain>(profile);
            grain.active = r.Grain > 0f;
            grain.type.overrideState = true; grain.type.value = FilmGrainLookup.Thin1;
            Set(grain.intensity, r.Grain); Set(grain.response, GrainResponse);

            Vignette vignette = Get<Vignette>(profile);
            vignette.active = true;
            Set(vignette.intensity, r.VignetteIntensity); Set(vignette.smoothness, r.VignetteSmoothness);
            vignette.rounded.overrideState = true; vignette.rounded.value = false;

            DepthOfField dof = Get<DepthOfField>(profile);
            dof.active = r.Depth != DepthMode.Off;
            dof.mode.overrideState = true;
            dof.mode.value = r.Depth == DepthMode.GaussianFar ? DepthOfFieldMode.Gaussian : r.Depth == DepthMode.BokehAim ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off;
            Set(dof.gaussianStart, GaussianStart); Set(dof.gaussianEnd, GaussianEnd); Set(dof.gaussianMaxRadius, GaussianRadius);
            Set(dof.aperture, BokehAperture); Set(dof.focalLength, BokehFocalLength); Set(dof.focusDistance, focus);

            MotionBlur blur = Get<MotionBlur>(profile);
            blur.active = r.MotionBlur > 0f;
            blur.mode.overrideState = true; blur.mode.value = MotionBlurMode.CameraOnly;
            Set(blur.intensity, r.MotionBlur); Set(blur.clamp, MotionClamp);
        }

        private void LateUpdate()
        {
            // FP Cinematic: focus follows the aim ray (screen centre), smoothed.
            if (_profile == null || RulesFor(QualityTiers.Current, CameraModeService.Current).Depth != DepthMode.BokehAim) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            int mask = ~((1 << PrototypeLayers.Player) | (1 << 2));
            float target = Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore) ? Mathf.Max(0.5f, hit.distance) : 200f;
            _focus = Mathf.Lerp(_focus, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / FocusSmoothing));
            if (_profile.TryGet(out DepthOfField dof)) Set(dof.focusDistance, _focus);
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component)) component = profile.Add<T>();
            return component;
        }

        private static void Set(VolumeParameter<float> p, float value)
        {
            p.overrideState = true;
            p.value = value;
        }
    }
}
