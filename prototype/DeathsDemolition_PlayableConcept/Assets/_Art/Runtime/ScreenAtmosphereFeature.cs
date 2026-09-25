using System.Collections.Generic;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core;
using DD.Prototype.World;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art
{
    /// <summary>
    /// WP-075 C4 B + C5 §5.2 · The TP / FP screen-space atmosphere of look v3, one URP renderer feature on the High and Cinematic
    /// renderers (QUALITY-TIERS-V2 r2 §4.2; Performance: off; top-down: off). It works only while a v3 world is loaded
    /// (<see cref="WorldActive"/>, set by <see cref="LookV3Post"/> on the World v5 dress root), so World_AP005 is unchanged.
    /// <list type="bullet">
    /// <item>Radial sun shafts (Ek A §3 B): a half-resolution sky mask from the scene depth (roofs, trees, bamboo and lattices
    /// occlude by depth), blurred toward the sun's screen position — 48 samples / peak 0.40 Cinematic, 20 / 0.22 High; density
    /// 0.90, weight 0.45, decay 0.94; tone #FFE7C4. Active while the sun is inside the frustum + 25 % margin, faded in / out over
    /// 0.5 s; × the cloud cookie at the camera (a cloud over the sun puts them out); FP: ≤ 0.25 inside the 6° reticle cone.
    /// The strength is published as <c>_DD_ShaftDir.w</c> (CloudShadowDriver.ShaftStrength) for the particles.</item>
    /// <item>Height fog (Ek A §5.2): base y −0.2 m, 0.004 / m halving every 5 m, integrated along the view ray, capped at 12 %;
    /// #D3D9DF, 20 % toward #E9E2D5 facing the sun.</item>
    /// </list>
    /// Both land in one full-screen draw into the camera colour before the transparents (fog as alpha, shafts additive: Blend One
    /// OneMinusSrcAlpha), so no colour copy is made.
    /// </summary>
    public sealed class ScreenAtmosphereFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "Hidden/DD/ScreenAtmosphere";
        public const float Density = 0.90f, Weight = 0.45f, Decay = 0.94f, FrustumMargin = 0.25f, FadeSeconds = 0.5f;
        public const float FogBaseY = -0.2f, FogDensity = 0.004f, FogHalfHeight = 5f, FogCap = 0.12f, SunInscatter = 0.20f;
        public static readonly Color ShaftTone = new Color(1f, 0xE7 / 255f, 0xC4 / 255f, 1f);
        public static readonly Color FogColor = new Color(0xD3 / 255f, 0xD9 / 255f, 0xDF / 255f, 1f);
        public static readonly Color FogSunColor = new Color(0xE9 / 255f, 0xE2 / 255f, 0xD5 / 255f, 1f);

        /// <summary>True while a look v3 world is loaded (LookV3Post).</summary>
        public static bool WorldActive;

        public struct Rules
        {
            public bool HeightFog;
            public int ShaftSamples;
            public float ShaftPeak;
        }

        /// <summary>QUALITY-TIERS-V2 r2 §4.2 for a tier and camera mode: top-down and Performance get neither.</summary>
        public static Rules RulesFor(QualityTier tier, CameraMode mode)
        {
            bool on = mode != CameraMode.TopDown && tier != QualityTier.Performans;
            return new Rules
            {
                HeightFog = on,
                ShaftSamples = on ? QualityTiers.Pick(tier, 0, 20, 48) : 0,
                ShaftPeak = on ? QualityTiers.Pick(tier, 0f, 0.22f, 0.40f) : 0f,
            };
        }

        /// <summary>The composite pass's height fog (Hidden/DD/ScreenAtmosphere, same formula): opacity at <paramref name="distance"/>
        /// along the unit <paramref name="direction"/> from a camera at <paramref name="cameraY"/>.</summary>
        public static float HeightFogOpacity(float cameraY, Vector3 direction, float distance)
        {
            float k = Mathf.Log(2f) / FogHalfHeight, a0 = FogDensity * Mathf.Exp(-k * (cameraY - FogBaseY)), kv = k * direction.y;
            float optical = Mathf.Abs(kv * distance) > 1e-3f ? a0 * (1f - Mathf.Exp(-kv * distance)) / kv : a0 * distance;
            return Mathf.Min(1f - Mathf.Exp(-optical), FogCap);
        }

        [SerializeField] private Shader _shader;
        private Material _material;
        private AtmospherePass _pass;

        public Shader Shader { get => _shader; set => _shader = value; }

        public override void Create()
        {
            _pass = new AtmospherePass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!WorldActive || renderingData.cameraData.cameraType != CameraType.Game || renderingData.cameraData.camera.name == PlanarReflection.CameraName) return;
            Rules rules = RulesFor(QualityTiers.Current, CameraModeService.Current);
            if (!rules.HeightFog && rules.ShaftSamples == 0) return;
            if (_material == null)
            {
                if (_shader == null) _shader = Shader.Find(ShaderName);
                if (_shader == null) return;
                _material = CoreUtils.CreateEngineMaterial(_shader);
            }

            _pass.Prepare(_material, rules, renderingData.cameraData.camera);
            renderer.EnqueuePass(_pass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            _pass?.SetTarget(renderer.cameraColorTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private sealed class AtmospherePass : ScriptableRenderPass
        {
            private const int MaskPass = 0, BlurPass = 1, CompositePass = 2;
            private static readonly int SunScreenId = Shader.PropertyToID("_DD_SunScreen");
            private static readonly int ShaftParamsId = Shader.PropertyToID("_DD_ShaftParams");
            private static readonly int ShaftToneId = Shader.PropertyToID("_DD_ShaftTone");
            private static readonly int FogParamsId = Shader.PropertyToID("_DD_HeightFogParams");
            private static readonly int FogColorId = Shader.PropertyToID("_DD_HeightFogColor");
            private static readonly int FogSunColorId = Shader.PropertyToID("_DD_HeightFogSunColor");
            private static readonly int SunDirId = Shader.PropertyToID("_DD_SunDirWS");
            private static readonly ProfilingSampler Sampler = new ProfilingSampler("DD Screen Atmosphere");

            private Material _material;
            private Rules _rules;
            private RTHandle _color, _mask, _blur;
            private float _visibility;
            private readonly Dictionary<int, (float visibility, float time)> _fade = new Dictionary<int, (float, float)>();   // per camera
            private Vector4 _sunScreen;
            private Vector3 _sunDir;

            public AtmospherePass()
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void SetTarget(RTHandle color) => _color = color;

            public void Prepare(Material material, Rules rules, Camera camera)
            {
                _material = material;
                _rules = rules;
                CloudShadowDriver cloud = CloudShadowDriver.Active;
                Light sun = cloud != null ? cloud.Sun : RenderSettings.sun;
                _sunDir = sun != null ? -sun.transform.forward : new Vector3(-0.66f, 0.54f, -0.51f);

                // Frustum + 25 % margin, faded over 0.5 s (unscaled: captures freeze time).
                Vector3 vp = camera.WorldToViewportPoint(camera.transform.position + _sunDir * 1000f);
                bool inside = rules.ShaftSamples > 0 && vp.z > 0f && vp.x > -FrustumMargin && vp.x < 1f + FrustumMargin && vp.y > -FrustumMargin && vp.y < 1f + FrustumMargin;
                int id = camera.GetInstanceID();
                float now = Time.unscaledTime, dt = 1f;   // a new camera starts at its target
                _visibility = 0f;
                if (_fade.TryGetValue(id, out (float visibility, float time) state))
                {
                    _visibility = state.visibility;
                    dt = Mathf.Clamp(now - state.time, 0f, 1f);
                }

                _visibility = Mathf.MoveTowards(_visibility, inside ? 1f : 0f, dt / FadeSeconds);
                if (_fade.Count > 16) _fade.Clear();
                _fade[id] = (_visibility, now);
                _sunScreen = new Vector4(vp.x, vp.y, camera.aspect, CameraModeService.Current == CameraMode.FirstPerson ? 1f : 0f);
                if (cloud != null && camera == Camera.main) cloud.ShaftStrength = _visibility;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _color == null) return;
                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, Sampler))
                {
                    RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                    desc.width = Mathf.Max(1, desc.width / 2);
                    desc.height = Mathf.Max(1, desc.height / 2);
                    desc.depthBufferBits = 0;
                    desc.msaaSamples = 1;
                    desc.graphicsFormat = GraphicsFormat.R8_UNorm;
                    RenderingUtils.ReAllocateIfNeeded(ref _mask, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_DD_ShaftMask");
                    RenderingUtils.ReAllocateIfNeeded(ref _blur, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_DD_ShaftBlur");

                    float strength = _rules.ShaftPeak * _visibility;
                    _material.SetVector(SunScreenId, _sunScreen);
                    _material.SetVector(ShaftParamsId, new Vector4(_rules.ShaftSamples, Density, Weight, Decay));
                    _material.SetColor(ShaftToneId, ShaftTone.linear * strength);
                    _material.SetVector(FogParamsId, new Vector4(FogBaseY, _rules.HeightFog ? FogDensity : 0f, Mathf.Log(2f) / FogHalfHeight, FogCap));
                    _material.SetColor(FogColorId, FogColor.linear);
                    _material.SetColor(FogSunColorId, FogSunColor.linear);
                    _material.SetVector(SunDirId, new Vector4(_sunDir.x, _sunDir.y, _sunDir.z, SunInscatter));

                    if (strength > 1e-4f)
                    {
                        Blitter.BlitCameraTexture(cmd, _blur, _mask, _material, MaskPass);
                        Blitter.BlitCameraTexture(cmd, _mask, _blur, _material, BlurPass);
                    }
                    else
                    {
                        CoreUtils.SetRenderTarget(cmd, _blur, ClearFlag.Color, Color.clear);
                    }

                    CoreUtils.SetRenderTarget(cmd, _color);
                    Blitter.BlitTexture(cmd, _blur, new Vector4(1f, 1f, 0f, 0f), _material, CompositePass);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                _mask?.Release();
                _blur?.Release();
            }
        }
    }
}
