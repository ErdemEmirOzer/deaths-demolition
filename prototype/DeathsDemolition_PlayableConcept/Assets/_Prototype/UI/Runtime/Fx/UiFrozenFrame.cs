using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 4 · C8 / §6.4 frozen-frame blur. While any owner holds it (Pause, the round-end shop) the world is
    /// frozen by RunFlowClock, so ONE capture of the game camera is enough: on the first hold a render pass
    /// (<see cref="UiFrozenFramePass"/>, enqueued at runtime on the game camera's URP renderer — the renderer asset is not
    /// modified) blurs the post-processed frame with a ¼-resolution dual Kawase + ink wash into a persistent RT
    /// (0.5–1.5 MB, no disk). A RawImage on its own overlay canvas (layer 90, under the HUD) shows it until the last
    /// owner releases. Cost: ≈ one pass on the capture frame, 0 per frame afterwards. Text never sits on the blur —
    /// the modal above it stays opaque and the scrim stays.
    /// </summary>
    public static class UiFrozenFrame
    {
        public const int Layer = UiLayer.FrozenFrame;   // 90 (§2.6)
        public const int Downscale = 4;               // ¼ resolution

        private static readonly HashSet<string> s_owners = new HashSet<string>();
        private static UiFrozenFramePass s_pass;
        private static Material s_material;
        private static RenderTexture s_target;
        private static bool s_hooked;
        private static bool s_pending;
        private static int s_capturedFrame = -1;
        private static UiFrozenFrameLayer s_layer;

        /// <summary>Captures since start-up (tests: one per hold, 0 per frame afterwards).</summary>
        public static int CaptureCount { get; private set; }
        public static bool IsHeld => s_owners.Count > 0;
        /// <summary>A capture exists for the current hold and the layer shows it.</summary>
        public static bool IsShowing => s_layer != null && s_layer.Showing;
        public static RenderTexture Target => s_target;

        /// <summary>Take the frozen frame for <paramref name="owner"/> (idempotent per owner). The first owner triggers the capture.</summary>
        public static void Hold(string owner)
        {
            if (string.IsNullOrEmpty(owner) || !Application.isPlaying || !s_owners.Add(owner))
            {
                return;
            }

            UiFxGlobals.EnsureInstalled();
            EnsureLayer();
            if (s_owners.Count == 1)
            {
                s_pending = true;
                s_capturedFrame = -1;
                Hook(true);
            }
        }

        public static void Release(string owner)
        {
            if (string.IsNullOrEmpty(owner) || !s_owners.Remove(owner) || s_owners.Count > 0)
            {
                return;
            }

            s_pending = false;
            Hook(false);
            if (s_layer != null)
            {
                s_layer.Show(false);
            }
        }

        /// <summary>Drop every owner (scene teardown, tests).</summary>
        public static void ReleaseAll()
        {
            s_owners.Clear();
            s_pending = false;
            Hook(false);
            if (s_layer != null)
            {
                s_layer.Show(false);
            }
        }

        internal static bool CaptureReady => s_capturedFrame >= 0 && s_owners.Count > 0;
        internal static int CapturedFrame => s_capturedFrame;

        private static void Hook(bool on)
        {
            if (on == s_hooked)
            {
                return;
            }

            s_hooked = on;
            if (on)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            }
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!s_pending || camera.cameraType != CameraType.Game || camera != Camera.main)
            {
                return;
            }

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null || !EnsureResources(camera))
            {
                return;
            }

            data.scriptableRenderer.EnqueuePass(s_pass);
        }

        private static bool EnsureResources(Camera camera)
        {
            if (s_material == null)
            {
                Shader shader = Shader.Find("Hidden/DD/UI/FrozenFrame");
                if (shader == null)
                {
                    return false;
                }

                s_material = new Material(shader) { name = "DD_UiFrozenFrame", hideFlags = HideFlags.DontSave };
                s_material.SetFloat("_DD_FrozenOffset", 1f);
                s_material.SetFloat("_DD_FrozenWash", 0.55f);

            }

            int w = Mathf.Max(1, camera.pixelWidth / Downscale), h = Mathf.Max(1, camera.pixelHeight / Downscale);
            if (s_target == null || s_target.width != w || s_target.height != h)
            {
                if (s_target != null)
                {
                    s_target.Release();
                    Object.Destroy(s_target);
                }

                s_target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "DD_UiFrozenFrame",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                s_target.Create();
            }

            if (s_pass == null)
            {
                s_pass = new UiFrozenFramePass();
            }

            s_pass.Setup(s_material, s_target);
            return true;
        }

        internal static void OnCaptured()
        {
            s_pending = false;
            s_capturedFrame = Time.frameCount;
            CaptureCount++;
            Hook(false);
        }

        private static void EnsureLayer()
        {
            if (s_layer != null)
            {
                return;
            }

            var go = new GameObject("[UiFrozenFrame]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Layer;
            canvas.enabled = false;

            var imageGo = new GameObject("Frame", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(go.transform, false);
            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = imageGo.GetComponent<RawImage>();
            image.raycastTarget = false;

            s_layer = go.AddComponent<UiFrozenFrameLayer>();
            s_layer.Bind(canvas, image);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_owners.Clear();
            s_pending = false;
            s_capturedFrame = -1;
            if (s_hooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            }

            s_hooked = false;
            s_layer = null;
            CaptureCount = 0;
        }
    }

    /// <summary>
    /// The capture pass (AfterRenderingPostProcessing: the graded frame, no UI — overlay canvases draw after the
    /// pipeline). Source → ½ → ¼ → ⅛ (Kawase down) → ¼ (up) → ink wash into the persistent ¼ target.
    /// </summary>
    internal sealed class UiFrozenFramePass : ScriptableRenderPass
    {
        private static readonly int HalfId = Shader.PropertyToID("_DD_FrozenHalf");
        private static readonly int QuarterId = Shader.PropertyToID("_DD_FrozenQuarter");
        private static readonly int EighthId = Shader.PropertyToID("_DD_FrozenEighth");
        private static readonly int UpId = Shader.PropertyToID("_DD_FrozenUp");
        private static readonly ProfilingSampler Sampler = new ProfilingSampler("DD UiFrozenFrame");
        private static readonly int FlipId = Shader.PropertyToID("_DD_FrozenFlip");
        private static bool _logged;

        /// <summary>1 = the wash pass flips v. Measured: a screen camera on Metal needs the flip, a render-texture camera does not.</summary>
        internal static float FlipFor(bool projectionFlipped, bool toScreen)
        {
            return toScreen && SystemInfo.graphicsUVStartsAtTop ? 1f : 0f;
        }

        private Material _material;
        private RenderTexture _target;

        public UiFrozenFramePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public void Setup(Material material, RenderTexture target)
        {
            _material = material;
            _target = target;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _target == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, Sampler))
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                int w = desc.width, h = desc.height;

                cmd.GetTemporaryRT(HalfId, Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), 0, FilterMode.Bilinear, desc.colorFormat);
                cmd.GetTemporaryRT(QuarterId, Mathf.Max(1, w / 4), Mathf.Max(1, h / 4), 0, FilterMode.Bilinear, desc.colorFormat);
                cmd.GetTemporaryRT(EighthId, Mathf.Max(1, w / 8), Mathf.Max(1, h / 8), 0, FilterMode.Bilinear, desc.colorFormat);
                cmd.GetTemporaryRT(UpId, Mathf.Max(1, w / 4), Mathf.Max(1, h / 4), 0, FilterMode.Bilinear, desc.colorFormat);

                // Orientation: the colour target is stored flipped or not depending on where the camera renders (an
                // intermediate for the back buffer vs a render texture). The wash pass un-flips so the RawImage (bottom-up)
                // shows the frame upright; locked by UiFxSliceTests (RT camera) and the Player capture (screen camera).
                bool flipped = renderingData.cameraData.IsCameraProjectionMatrixFlipped();
                bool toScreen = renderingData.cameraData.targetTexture == null;
                float flip = FlipFor(flipped, toScreen);
                _material.SetFloat(FlipId, flip);
                if (!_logged)
                {
                    _logged = true;
                    Debug.Log("[UiFrozenFrame] capture projectionFlipped=" + flipped + " toScreen=" + toScreen + " uvStartsAtTop=" + SystemInfo.graphicsUVStartsAtTop + " flip=" + flip);
                }

                RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                cmd.Blit(source, HalfId, _material, 0);
                cmd.Blit(HalfId, QuarterId, _material, 0);
                cmd.Blit(QuarterId, EighthId, _material, 0);
                cmd.Blit(EighthId, UpId, _material, 1);
                cmd.Blit(UpId, _target, _material, 2);

                cmd.ReleaseTemporaryRT(HalfId);
                cmd.ReleaseTemporaryRT(QuarterId);
                cmd.ReleaseTemporaryRT(EighthId);
                cmd.ReleaseTemporaryRT(UpId);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            UiFrozenFrame.OnCaptured();
        }
    }
}
