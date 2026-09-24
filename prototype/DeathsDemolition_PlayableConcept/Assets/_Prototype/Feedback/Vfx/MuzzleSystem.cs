using DD.Prototype.CameraSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-073 B1 · The muzzle's light and smoke per weapon class (direction §1.1): a pooled point light per shot
    /// (colour / intensity / range / hold frames + fade from the profile; Cinematic casts soft shadows, High none,
    /// Performance lights every second shot; eight / four / two alive by tier), the sustained-fire model for auto
    /// weapons ≥ 600 RPM (one light stays lit and flickers 0.6–1.0 at 10 Hz while the trigger is held instead of a
    /// strobe) and ONE shared world-space smoke emitter (not an emitter per muzzle) — a soft radial puff — 0.3 m ahead of the
    /// muzzle and grows over its life. Reduce flashes: the light holds a steady 0.8 and never flickers. In first person
    /// the light sits at the viewmodel muzzle's world position (CameraView.ViewmodelToWorld). 0 B per shot.
    /// </summary>
    public sealed class MuzzleSystem : MonoBehaviour
    {
        private static readonly Unity.Profiling.ProfilerMarker s_marker = new Unity.Profiling.ProfilerMarker("WP073.MuzzleSystem");

        public const int LightCapacity = 8;
        private const float SustainGap = 0.2f;   // a shot within this of the last keeps the sustained light lit

        private struct MuzzleLight
        {
            public Light Light;
            public float Start;
            public float Hold;
            public float Fade;
            public float Intensity;
            public bool Sustained;
            public float LastShot;
            public Transform Muzzle;
            public bool FirstPerson;
            /// <summary>WP-073 B2: a light at a fixed world point (impact), not following a muzzle.</summary>
            public bool Fixed;
        }

        private static MuzzleSystem s_instance;
        private readonly MuzzleLight[] _lights = new MuzzleLight[LightCapacity];
        private ParticleSystem _smoke;
        private int _shot;

        public static MuzzleSystem Instance => s_instance;

        /// <summary>Lights lit right now (tests / probes).</summary>
        public int LitCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _lights.Length; i++)
                {
                    if (_lights[i].Light != null && _lights[i].Light.enabled) n++;
                }

                return n;
            }
        }

        public int SmokeAlive => _smoke != null ? _smoke.particleCount : 0;
        public int LightsStarted { get; private set; }

        public static MuzzleSystem Ensure()
        {
            if (s_instance == null)
            {
                var go = new GameObject("[MuzzleSystem]");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<MuzzleSystem>();
                s_instance.Build();
            }

            return s_instance;
        }

        private Texture2D _puffTexture;
        private Material _puffMaterial;

        // AD B1 MUST 2: a soft radial puff (edge α = 0) — the AP-010 card sheet drew hard-edged rectangles near the FP eye.
        private static Texture2D BuildPuff(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_MuzzlePuff", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float noise = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.35f + 0.65f;
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a) * noise;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (_puffMaterial != null) Destroy(_puffMaterial);
            if (_puffTexture != null) Destroy(_puffTexture);
        }

        private void Build()
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject("MuzzleLight" + i);
                go.transform.SetParent(transform, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                l.enabled = false;
                _lights[i].Light = l;
            }

            _puffTexture = BuildPuff(64);
            _puffMaterial = new Material(FeedbackMaterials.ParticleAlpha()) { name = "DD_MuzzlePuff (runtime)", hideFlags = HideFlags.DontSave };
            _puffMaterial.SetTexture("_BaseMap", _puffTexture);
            _puffMaterial.mainTexture = _puffTexture;

            var smokeGo = new GameObject("MuzzleSmoke");
            smokeGo.transform.SetParent(transform, false);
            _smoke = smokeGo.AddComponent<ParticleSystem>();
            _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _smoke.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 96;
            main.startSpeed = 0f;
            main.gravityModifier = -0.02f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            ParticleSystem.EmissionModule emission = _smoke.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _smoke.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = _smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 2.1f));
            ParticleSystem.ColorOverLifetimeModule colour = _smoke.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystem.RotationOverLifetimeModule rotation = _smoke.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _puffMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 0.5f;
            _smoke.Play();
        }

        /// <summary>One shot's light and smoke.</summary>
        public void Play(Transform muzzle, VfxWeaponProfile p, bool firstPerson)
        {
            if (muzzle == null || p == null)
            {
                return;
            }

            _shot++;
            float now = Time.time;
            VfxTierLevel tier = VfxTier.Current;
            bool reduceFlashes = GameFeelSettings.FlashScale < 0.99f;

            // ---- light
            bool lightThisShot = tier != VfxTierLevel.Performance || (_shot & 1) == 0 || p.Sustained;
            if (lightThisShot)
            {
                int slot = -1;
                if (p.Sustained)
                {
                    for (int i = 0; i < _lights.Length; i++)
                    {
                        if (_lights[i].Light.enabled && _lights[i].Sustained && _lights[i].Muzzle == muzzle && now - _lights[i].LastShot <= SustainGap)
                        {
                            slot = i;
                            break;
                        }
                    }
                }

                if (slot < 0) slot = FreeLight(tier, now);
                if (slot >= 0)
                {
                    ref MuzzleLight m = ref _lights[slot];
                    m.Fixed = false;
                    bool fresh = !m.Light.enabled || m.Muzzle != muzzle || !m.Sustained;
                    m.Muzzle = muzzle;
                    m.FirstPerson = firstPerson;
                    m.Sustained = p.Sustained;
                    m.LastShot = now;
                    m.Hold = p.LightHoldSeconds;
                    m.Fade = p.LightFadeSeconds;
                    // AD B1 SHOULD 11: top-down pools × 0.8 (the character's silhouette first).
                    bool topDown = CameraModeService.Current == CameraMode.TopDown;
                    m.Intensity = p.LightIntensity * (reduceFlashes ? 0.8f : 1f) * (topDown ? p.LightTopDownScale : 1f) * (firstPerson ? p.LightFirstPersonScale : 1f);
                    if (fresh)
                    {
                        m.Start = now;
                        LightsStarted++;
                    }

                    Light l = m.Light;
                    l.color = p.LightColor;
                    l.range = p.LightRange;
                    l.shadows = tier == VfxTierLevel.Cinematic ? LightShadows.Soft : LightShadows.None;
                    l.intensity = m.Intensity;
                    l.transform.position = MuzzlePoint(muzzle, firstPerson);
                    l.enabled = true;
                }
            }

            // ---- smoke: one shared world emitter, 0.3 m ahead of the muzzle (never inside the viewmodel)
            if (_smoke != null && p.SmokeSeconds > 0f)
            {
                float life = p.SmokeSeconds * VfxTier.Pick(0.3f, 0.625f, 1f);
                Vector3 fwd = muzzle.forward;
                Vector3 at = MuzzlePoint(muzzle, firstPerson) + fwd * 0.3f;
                if (firstPerson && FeedbackCameraRef.TryGetPosition(out Vector3 eye))
                {
                    // Never inside the eye's near space (AD B1 MUST 2): at least 0.6 m out along the barrel.
                    float d = Vector3.Distance(eye, at);
                    if (d < 0.6f) at += fwd * (0.6f - d);
                }

                var emit = new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Vector3.up * 0.4f + fwd * 0.3f,
                    startSize = p.SmokeStart,
                    startLifetime = life,
                    startColor = new Color(0.78f, 0.77f, 0.75f, p.SmokeAlpha)
                };
                _smoke.Emit(emit, 1);
            }
        }

        /// <summary>
        /// WP-073 B2: a short light at a world point (direction §1.4 impact light #FFE0B0 / 3 / 2 m / 2 frames) from the same
        /// pool, so impacts and muzzles share the tier's light cap (8 / 4 / 2). Reduce flashes: ×0.8, never above the muzzle's.
        /// </summary>
        public bool FlashAt(Vector3 point, Color colour, float intensity, float range, float holdSeconds, float fadeSeconds)
        {
            float now = Time.time;
            int slot = FreeLight(VfxTier.Current, now);
            if (slot < 0) return false;
            ref MuzzleLight m = ref _lights[slot];
            m.Muzzle = null;
            m.Fixed = true;
            m.FirstPerson = false;
            m.Sustained = false;
            m.Start = now;
            m.LastShot = now;
            m.Hold = holdSeconds;
            m.Fade = fadeSeconds;
            m.Intensity = intensity * (GameFeelSettings.FlashScale < 0.99f ? 0.8f : 1f);
            Light l = m.Light;
            l.color = colour;
            l.range = range;
            l.shadows = LightShadows.None;
            l.intensity = m.Intensity;
            l.transform.position = point;
            l.enabled = true;
            ImpactLights++;
            return true;
        }

        /// <summary>
        /// WP-073 B2: one soft puff from the shared world smoke emitter (the radial puff grows ×2.1 over its life) — the head-kill
        /// red mist. The AP-010 card sheet drew a hard-edged square there (as it did at the FP eye in B1, AD MUST 2).
        /// </summary>
        public bool PuffAt(Vector3 point, float size, float seconds, Color colour, Vector3 drift)
        {
            if (_smoke == null) return false;
            var emit = new ParticleSystem.EmitParams { position = point, velocity = drift, startSize = size, startLifetime = seconds, startColor = colour };
            _smoke.Emit(emit, 1);
            return true;
        }

        /// <summary>WP-073 B2: impact lights started (tests / probes).</summary>
        public int ImpactLights { get; private set; }

        private static Vector3 MuzzlePoint(Transform muzzle, bool firstPerson)
        {
            return firstPerson ? CameraView.ViewmodelToWorld(muzzle.position) : muzzle.position;
        }

        // A dark light, or the oldest when the tier's cap (8 / 4 / 2) is reached.
        private int FreeLight(VfxTierLevel tier, float now)
        {
            int cap = tier == VfxTierLevel.Cinematic ? 8 : tier == VfxTierLevel.High ? 4 : 2;
            int lit = 0, oldest = -1, free = -1;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (!_lights[i].Light.enabled)
                {
                    if (free < 0) free = i;
                    continue;
                }

                lit++;
                if (oldest < 0 || _lights[i].Start < _lights[oldest].Start) oldest = i;
            }

            if (lit >= cap)
            {
                if (oldest >= 0) _lights[oldest].Light.enabled = false;
                return oldest;
            }

            return free;
        }

        private void LateUpdate()
        {
            using var marker = s_marker.Auto();   // WP-073 §5: per-tier ms, read by ProfilerRecorder (S-10 / tier-ms.md)
            float now = Time.time;
            bool reduceFlashes = GameFeelSettings.FlashScale < 0.99f;
            for (int i = 0; i < _lights.Length; i++)
            {
                ref MuzzleLight m = ref _lights[i];
                Light l = m.Light;
                if (l == null || !l.enabled) continue;
                if (m.Muzzle == null && !m.Fixed)
                {
                    l.enabled = false;
                    continue;
                }

                if (!m.Fixed) l.transform.position = MuzzlePoint(m.Muzzle, m.FirstPerson);
                float since = now - m.LastShot;
                if (m.Sustained && since <= SustainGap)
                {
                    // Held trigger: a lit, living muzzle — flicker 0.6–1.0 at 10 Hz, steady 0.8 under reduce flashes.
                    float flicker = reduceFlashes ? 1f : 0.8f + 0.2f * Mathf.Sin(now * Mathf.PI * 20f);
                    l.intensity = m.Intensity * flicker;
                    continue;
                }

                float t = m.Sustained ? since - SustainGap : now - m.Start - m.Hold;
                if (t <= 0f)
                {
                    l.intensity = m.Intensity;
                    continue;
                }

                float k = 1f - t / Mathf.Max(0.01f, m.Fade);
                if (k <= 0f)
                {
                    l.enabled = false;
                    continue;
                }

                l.intensity = m.Intensity * k * k;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
