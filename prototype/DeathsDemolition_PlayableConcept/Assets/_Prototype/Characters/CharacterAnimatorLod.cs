using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-018 · ADR-018-2 · Crowd animation budget. Two rules on one crowd body:
    ///   * off screen → the Animator's own <c>CullUpdateTransforms</c> (set by the visual): the
    ///     state machine and its events keep running (hit windows, death_end), no bone write;
    ///   * far (LOD ≥ <see cref="FarLodIndex"/> by the LODGroup's own screen-height rule) → the
    ///     Animator is stepped by hand at <see cref="FarRateHz"/> instead of every frame. Events
    ///     still fire on the manual step; parameters written between steps accumulate as usual.
    /// Nothing here is a gameplay value; a body's clock never stops (the step carries the
    /// elapsed time), so a far attack window opens at the same second as a near one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimatorLod : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Animator step rate for far crowd bodies, Hz. Invented.", true)]
        public const float FarRateHz = 15f;

        [PrototypeOnly(PendingDecisions.None, "LOD index from which a body is 'far' (0 = LOD0). Invented.", true)]
        public const int FarLodIndex = 2;

        [SerializeField] private Animator _animator;
        [SerializeField] private LODGroup _lodGroup;

        private float _accumulated;
        private bool _far;
        private LOD[] _lods;   // cached: LODGroup.GetLODs allocates
        private static int s_farCount;
        private static Camera s_camera;
        private static int s_cameraFrame = -1;

        /// <summary>Bodies currently on the reduced step (probe / tests).</summary>
        public static int FarCount => s_farCount;
        public bool Far => _far;
        /// <summary>Tests: force the far path regardless of distance (-1 = automatic).</summary>
        public static int ForceFar = -1;

        public void Configure(Animator animator, LODGroup lodGroup)
        {
            _animator = animator;
            _lodGroup = lodGroup;
        }

        private void OnEnable()
        {
            _accumulated = 0f;
            _far = false;
            if (_lods == null && _lodGroup != null) _lods = _lodGroup.GetLODs();
        }

        private void OnDisable()
        {
            if (_far) s_farCount--;
            _far = false;
            Suspended = false;
            if (_animator != null && !_animator.enabled) _animator.enabled = true;
        }

        /// <summary>WP-071: a ragdoll owns the bones — the LOD neither toggles nor steps the Animator until <see cref="Resume"/>.</summary>
        public bool Suspended { get; private set; }

        public void Suspend()
        {
            if (_far) s_farCount--;
            _far = false;
            _accumulated = 0f;
            Suspended = true;
        }

        public void Resume() => Suspended = false;

        private void Update()
        {
            if (_animator == null || Suspended) return;
            bool far = ForceFar >= 0 ? ForceFar == 1 : IsFar();
            if (far != _far)
            {
                _far = far;
                s_farCount += far ? 1 : -1;
                if (!far)
                {
                    // Back to every-frame stepping: give the Animator the time it has not seen yet.
                    if (_accumulated > 0f) _animator.Update(_accumulated);
                    _accumulated = 0f;
                    _animator.enabled = true;
                }
                else
                {
                    _animator.enabled = false;
                }
            }

            if (!_far) return;

            _accumulated += Time.deltaTime;
            if (_accumulated >= 1f / FarRateHz)
            {
                _animator.Update(_accumulated);
                _accumulated = 0f;
            }
        }

        /// <summary>LODGroup's screen-relative height ≥ the Far LOD's threshold? (same formula as the group, incl. lodBias).</summary>
        private bool IsFar()
        {
            if (_lodGroup == null) return false;
            Camera cam = MainCamera();
            if (cam == null) return false;
            LOD[] lods = _lods;
            if (lods == null || lods.Length <= FarLodIndex) return false;

            Vector3 centre = _lodGroup.transform.TransformPoint(_lodGroup.localReferencePoint);
            float size = _lodGroup.size * MaxScale(_lodGroup.transform.lossyScale);
            float relativeHeight;
            if (cam.orthographic)
            {
                relativeHeight = size * 0.5f / Mathf.Max(0.01f, cam.orthographicSize);
            }
            else
            {
                float distance = Vector3.Distance(cam.transform.position, centre);
                float halfAngle = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                relativeHeight = size * 0.5f / Mathf.Max(0.01f, distance * halfAngle);
            }

            relativeHeight *= QualitySettings.lodBias;
            // LOD i is shown while relativeHeight > lods[i].screenRelativeTransitionHeight; far = below the LOD(Far-1) threshold.
            return relativeHeight <= lods[FarLodIndex - 1].screenRelativeTransitionHeight;
        }

        private static float MaxScale(Vector3 s)
        {
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        private static Camera MainCamera()
        {
            if (s_cameraFrame != Time.frameCount)
            {
                s_cameraFrame = Time.frameCount;
                if (s_camera == null || !s_camera.isActiveAndEnabled) s_camera = Camera.main;
            }

            return s_camera;
        }

        public static void ResetStatics()
        {
            s_farCount = 0;
            s_camera = null;
            s_cameraFrame = -1;
            ForceFar = -1;
        }
    }
}
