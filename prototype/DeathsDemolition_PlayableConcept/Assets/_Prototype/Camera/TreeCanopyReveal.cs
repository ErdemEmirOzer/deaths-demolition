using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-109 (R4, user note 10: "ağaçların da altına girince … karakteri görebileceğimiz şekilde ağaçlar da hafif
    /// saydamlaştırılsın yapılardaki aynı mantık ile") · the top-down tree-crown reveal. The roofs fade whole through
    /// <see cref="CameraOcclusionFader"/>; the crowns are mostly static-batched library prefabs on shared cutout materials, so
    /// DD/Foliage Wind decides per pixel (DD_TreeReveal.hlsl) with the roofs' rule — only the crown pixels standing between the
    /// camera and the hero are screen-door thinned, a crown that hides nothing stays as authored, and its shadow never changes.
    /// This component only feeds the shader: the focus (the follow target at <see cref="FocusHeight"/>) and a strength eased
    /// in over <see cref="FadeSeconds"/> in top-down and out in the other modes. Only the game camera reveals: every other camera
    /// (the wet-slate planar reflection draws the crowns from a mirrored eye; previews, captures) renders the crowns as authored —
    /// the global is written per camera in RenderPipelineManager.beginCameraRendering, and put back when a camera nested inside
    /// the game camera's render (PlanarReflection.RenderSingleCamera runs from the game camera's begin) ends. A few global writes
    /// per frame, 0 B.
    /// </summary>
    [DefaultExecutionOrder(110)]   // after PrototypeCameraRig (100) has placed the camera
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeCameraRig))]
    public sealed class TreeCanopyReveal : MonoBehaviour
    {
        public static readonly int RevealId = Shader.PropertyToID("_DD_TreeReveal");
        public static readonly int ShapeId = Shader.PropertyToID("_DD_TreeRevealShape");

        /// <summary>The focus over the follow target's root, as CameraOcclusionFader's probe (chest height).</summary>
        public const float FocusHeight = 1.1f;
        /// <summary>Radius of the revealed disc at the focus (m): the hero's silhouette with a weapon and a step of room.</summary>
        public const float Radius = 2.4f;
        /// <summary>The disc's soft rim (m): the thinning ramps from none at <see cref="Radius"/> to full this far inside it.</summary>
        public const float SoftRim = 1.2f;
        /// <summary>Share of the crown's pixels dropped at the disc's core — "hafif": the crown still reads, the hero shows through.</summary>
        public const float DroppedShare = .75f;   // r1 .65: the hero read dim under a dense momiji (wp109-r1 frames)
        /// <summary>Only foliage this far over the focus thins (fully 0.5 m higher): the canopy, never the shrub at the hero's knee.</summary>
        public const float MinHeightOverFocus = .9f;
        /// <summary>Strength ease in and out (s), the roofs' occlusion fade time.</summary>
        public const float FadeSeconds = .15f;

        private PrototypeCameraRig _rig;
        private float _strength;
        private Vector4 _live;
        private bool _gameRendering;

        /// <summary>The strength fed to the shader last frame (tests).</summary>
        public float Strength => _strength;

        private void OnEnable()
        {
            _rig = GetComponent<PrototypeCameraRig>();
            Shader.SetGlobalVector(ShapeId, new Vector4(Radius, SoftRim, DroppedShare, MinHeightOverFocus));
            Shader.SetGlobalVector(RevealId, Vector4.zero);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        // The strength is kept across a disable (a same-frame toggle draws the authored crowns once and comes back at full strength).
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            _gameRendering = false;
            Shader.SetGlobalVector(RevealId, Vector4.zero);
        }

        private bool IsGame(Camera camera) => camera != null && _rig != null && camera == _rig.Camera;

        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool game = IsGame(camera);
            if (game) _gameRendering = true;
            Shader.SetGlobalVector(RevealId, game ? _live : Vector4.zero);
        }

        // A nested camera (the planar reflection) ends inside the game camera's render: the game camera's crowns reveal again.
        private void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (IsGame(camera)) { _gameRendering = false; return; }
            if (_gameRendering) Shader.SetGlobalVector(RevealId, _live);
        }

        private void LateUpdate()
        {
            Transform target = _rig != null ? _rig.FollowTarget : null;
            bool want = target != null && CameraModeService.Current == CameraMode.TopDown;
            // Unscaled, as the roofs: a crown over the hero when the game pauses stays thinned on the pause screen.
            _strength = Mathf.MoveTowards(_strength, want ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            Vector3 focus = target != null ? target.position + Vector3.up * FocusHeight : Vector3.zero;
            _live = new Vector4(focus.x, focus.y, focus.z, target != null ? _strength : 0f);
            Shader.SetGlobalVector(RevealId, _live);
        }
    }
}
