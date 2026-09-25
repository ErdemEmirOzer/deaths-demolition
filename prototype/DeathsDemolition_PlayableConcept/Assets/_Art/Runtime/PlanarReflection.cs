using DD.Prototype.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art
{
    /// <summary>
    /// WP-074 B8 · Planar reflection over a horizontal plane (QUALITY-TIERS-V2 r2 §4.1 "Planar"): before each game camera renders,
    /// a mirrored camera renders the world under the plane into an HDR target. It uses the camera's own projection with an oblique
    /// near plane at the water line, with no shadows or post. The target is published as <see cref="TextureName"/> and
    /// <see cref="EnabledName"/>. DD/GroundTiles blends it into wet slate and puddles by Fresnel, which is what makes the court read
    /// wet from the steep gameplay cameras. Tier: Performance off · High 256 px tall without characters · Cinematic 1024 px with.
    /// The owner is the S-05 quality module. It re-reads the tier on <see cref="QualityTiers.Changed"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlanarReflection : MonoBehaviour
    {
        public const string TextureName = "_DD_PlanarReflection", EnabledName = "_DD_PlanarReflectionOn";
        private static readonly int s_texture = Shader.PropertyToID(TextureName), s_enabled = Shader.PropertyToID(EnabledName);

        [SerializeField] private float _planeHeight = 0.012f;
        [SerializeField] private float _clipOffset = 0.03f;
        [SerializeField] private float _farClip = 180f;

        private Camera _reflection;
        private RenderTexture _target;
        private static bool s_rendering;

        public float PlaneHeight { get => _planeHeight; set => _planeHeight = value; }

        /// <summary>Contract r2: the target's height in px per tier (0 = off).</summary>
        /// <summary>The mirrored camera's name (the screen atmosphere skips it).</summary>
        public const string CameraName = "DD Planar Reflection Camera";

        public static int HeightOf(QualityTier tier) => QualityTiers.Pick(tier, 0, 256, 1024);

        /// <summary>Contract r2: characters (player, enemies, ragdolls) reflect only at Cinematic.</summary>
        public static bool CharactersIn(QualityTier tier) => tier == QualityTier.Sinematik;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            QualityTiers.Changed += OnTier;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            QualityTiers.Changed -= OnTier;
            Shader.SetGlobalFloat(s_enabled, 0f);
            Release();
        }

        private void OnTier(QualityTier tier) => Release();

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (s_rendering || cam == _reflection || cam.cameraType != CameraType.Game) return;
            QualityTier tier = QualityTiers.Current;
            int height = HeightOf(tier);
            Shader.SetGlobalFloat(s_enabled, 0f);   // never sampled while the reflection itself renders
            if (height <= 0) return;

            EnsureTarget(cam, height);
            EnsureCamera();
            Setup(cam, tier);
            s_rendering = true;
            bool invert = GL.invertCulling;
            GL.invertCulling = !invert;
            try
            {
#pragma warning disable CS0618   // URP 14: RenderSingleCamera is the nested-render entry (RenderRequest arrives with later versions)
                UniversalRenderPipeline.RenderSingleCamera(context, _reflection);
#pragma warning restore CS0618
            }
            finally
            {
                GL.invertCulling = invert;
                s_rendering = false;
            }

            Shader.SetGlobalTexture(s_texture, _target);
            Shader.SetGlobalFloat(s_enabled, 1f);
        }

        private void Setup(Camera cam, QualityTier tier)
        {
            _reflection.CopyFrom(cam);
            _reflection.targetTexture = _target;
            _reflection.enabled = false;
            _reflection.farClipPlane = Mathf.Min(cam.farClipPlane, _farClip);
            int mask = cam.cullingMask & ~(1 << 5);   // no UI
            if (!CharactersIn(tier)) mask &= ~((1 << PrototypeLayers.Player) | (1 << PrototypeLayers.Enemy) | (1 << PrototypeLayers.Ragdoll));
            _reflection.cullingMask = mask;

            Vector4 plane = new Vector4(0f, 1f, 0f, -_planeHeight);
            Matrix4x4 mirror = Reflection(plane);
            Transform t = cam.transform;
            _reflection.transform.SetPositionAndRotation(mirror.MultiplyPoint(t.position),
                Quaternion.LookRotation(mirror.MultiplyVector(t.forward), mirror.MultiplyVector(t.up)));
            _reflection.worldToCameraMatrix = cam.worldToCameraMatrix * mirror;
            Vector4 clip = CameraSpacePlane(_reflection.worldToCameraMatrix, new Vector3(0f, _planeHeight, 0f), Vector3.up, _clipOffset);
            _reflection.projectionMatrix = cam.CalculateObliqueMatrix(clip);
        }

        /// <summary>The reflection about the plane (a, b, c, d) with a·x + b·y + c·z + d = 0 (unit normal).</summary>
        public static Matrix4x4 Reflection(Vector4 p)
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y;      m.m02 = -2f * p.x * p.z;      m.m03 = -2f * p.w * p.x;
            m.m10 = -2f * p.y * p.x;      m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z;      m.m13 = -2f * p.w * p.y;
            m.m20 = -2f * p.z * p.x;      m.m21 = -2f * p.z * p.y;      m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
            return m;
        }

        /// <summary>The plane in camera space (for the oblique near plane), pushed <paramref name="offset"/> along its normal.</summary>
        public static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 point, Vector3 normal, float offset)
        {
            Vector3 cpos = worldToCamera.MultiplyPoint(point + normal * offset);
            Vector3 cnormal = worldToCamera.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }

        private void EnsureTarget(Camera cam, int height)
        {
            int width = Mathf.Max(16, Mathf.RoundToInt(height * Mathf.Max(0.1f, cam.aspect)));
            if (_target != null && _target.width == width && _target.height == height) return;
            Release();
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR) { name = "DD_PlanarReflection", hideFlags = HideFlags.HideAndDontSave };
            _target.Create();
        }

        private void EnsureCamera()
        {
            if (_reflection != null) return;
            var go = new GameObject(CameraName) { hideFlags = HideFlags.HideAndDontSave };
            _reflection = go.AddComponent<Camera>();
            _reflection.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }

        private void Release()
        {
            if (_target != null)
            {
                if (_reflection != null) _reflection.targetTexture = null;
                _target.Release();
                Destroy(_target);
                _target = null;
            }
        }

        private void OnDestroy()
        {
            Release();
            if (_reflection != null) Destroy(_reflection.gameObject);
        }
    }
}
