using DD.Prototype.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art
{
    /// <summary>
    /// WP-075 · DEC-102 · Realistic sun shafts: a raymarched volumetric light (Hidden/DD/VolumetricLight) on the High and Cinematic
    /// renderers — URP's main-light shadow map × the cloud cookie through <see cref="VolumetricLight"/>'s density field, so the shafts
    /// fall where the trees, bamboo, eaves and gates open to the sun and go out under a cloud. Every camera mode (the phase keeps an
    /// isotropic share for the top-down camera). QUALITY-TIERS-V2: Cinematic ½ resolution × 32 steps, High ¼ × 12, Performance off
    /// (the card shafts stand in there). One low-resolution march (RGBA16F: in-scatter, transmittance) and one blended full-screen
    /// composite before the transparents; only while a v6 world's field is live, never for the planar camera.
    /// </summary>
    public sealed class VolumetricLightFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "Hidden/DD/VolumetricLight";
        public const float PhaseG = 0.62f, IsotropicShare = 0.35f, DepthWeight = 0.6f;

        public struct Rules
        {
            public bool On;
            public int Downsample, Steps;
            public float MaxDistance;
        }

        /// <summary>QUALITY-TIERS-V2 (DEC-102 row) for a tier.</summary>
        public static Rules RulesFor(QualityTier tier)
        {
            return tier switch
            {
                QualityTier.Sinematik => new Rules { On = true, Downsample = 2, Steps = 32, MaxDistance = 70f },
                QualityTier.Yuksek => new Rules { On = true, Downsample = 4, Steps = 12, MaxDistance = 50f },
                _ => new Rules { On = false },
            };
        }

        [SerializeField] private Shader _shader;
        private Material _material;
        private MarchPass _pass;

        public Shader Shader { get => _shader; set => _shader = value; }

        public override void Create()
        {
            _pass = new MarchPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // The world's base cameras only: not the planar reflection, not an overlay (the FP viewmodel — its base already carries the
            // shafts; composited again it hazes the whole screen twice), not a single-layer probe (PlanarReflection.RendersWorld).
            if (!VolumetricLight.Active || renderingData.cameraData.camera.name == PlanarReflection.CameraName || !PlanarReflection.RendersWorld(renderingData.cameraData.camera)) return;
            Rules rules = RulesFor(QualityTiers.Current);
            if (!rules.On) return;
            if (_material == null)
            {
                if (_shader == null) _shader = Shader.Find(ShaderName);
                if (_shader == null) return;
                _material = CoreUtils.CreateEngineMaterial(_shader);
            }

            _pass.Prepare(_material, rules);
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

        private sealed class MarchPass : ScriptableRenderPass
        {
            private const int MarchPassIndex = 0, CompositePassIndex = 1;
            private static readonly int MarchId = Shader.PropertyToID("_VL_March");
            private static readonly int SunDirId = Shader.PropertyToID("_VL_SunDir");
            private static readonly int AmbientId = Shader.PropertyToID("_VL_Ambient");
            private static readonly int LowTexelId = Shader.PropertyToID("_VL_LowTexel");
            private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly ProfilingSampler Sampler = new ProfilingSampler("DD Volumetric Light");

            private Material _material;
            private Rules _rules;
            private RTHandle _color, _low;

            public MarchPass()
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void SetTarget(RTHandle color) => _color = color;

            public void Prepare(Material material, Rules rules)
            {
                _material = material;
                _rules = rules;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _color == null) return;
                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, Sampler))
                {
                    RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                    desc.width = Mathf.Max(1, desc.width / _rules.Downsample);
                    desc.height = Mathf.Max(1, desc.height / _rules.Downsample);
                    desc.depthBufferBits = 0;
                    desc.msaaSamples = 1;
                    desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    RenderingUtils.ReAllocateIfNeeded(ref _low, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_DD_VolumetricLight");

                    Vector3 sun = VolumetricLight.SunDirection;
                    Color ambient = RenderSettings.ambientSkyColor.linear * (VolumetricLight.Current != null ? VolumetricLight.Current.AmbientScatter : 0f);
                    _material.SetVector(MarchId, new Vector4(_rules.Steps, _rules.MaxDistance, PhaseG, IsotropicShare));
                    _material.SetVector(SunDirId, new Vector4(sun.x, sun.y, sun.z, 0f));
                    _material.SetVector(AmbientId, new Vector4(ambient.r, ambient.g, ambient.b, 0f));
                    _material.SetVector(LowTexelId, new Vector4(1f / desc.width, 1f / desc.height, DepthWeight, 0f));
                    _material.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));

                    CoreUtils.SetRenderTarget(cmd, _low, ClearFlag.None, Color.clear);
                    cmd.DrawProcedural(Matrix4x4.identity, _material, MarchPassIndex, MeshTopology.Triangles, 3, 1);
                    CoreUtils.SetRenderTarget(cmd, _color);
                    Blitter.BlitTexture(cmd, _low, new Vector4(1f, 1f, 0f, 0f), _material, CompositePassIndex);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                _low?.Release();
            }
        }
    }
}
