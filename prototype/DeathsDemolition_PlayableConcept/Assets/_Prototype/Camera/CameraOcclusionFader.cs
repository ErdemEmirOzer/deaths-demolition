using System.Collections.Generic;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// REQ-035. Anything on the CameraOccluder layer that gets between the camera and
    /// the player fades out smoothly and fades back in once it is clear. The camera
    /// itself never moves closer, which is the explicit requirement: "kamera aniden
    /// oyuncuya yaklasmamali".
    ///
    /// The probe is a sphere cast, not a line, so a thin sliver of wall clipping the
    /// player's shoulder still counts. It runs on a fixed interval rather than every
    /// frame: at 20 Hz the fade is already smooth (the fade itself is time based, not
    /// probe based) and the crowd frames stay cheap.
    ///
    /// Only colliders on the CameraOccluder layer are considered, so the world author
    /// decides what may disappear. Ground, small props and enemies never fade.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeCameraRig))]
    public sealed class CameraOcclusionFader : PrototypeConfigBehaviour
    {
        [PrototypeOnly(PendingDecisions.None,
            "Radius of the occlusion probe in metres. Roughly the player's silhouette width.", true)]
        [SerializeField] private float _probeRadius = 0.65f;

        [PrototypeOnly(PendingDecisions.None,
            "Height above the follow target the probe aims at, so a wall covering the head also fades.", true)]
        [SerializeField] private float _probeHeight = 1.1f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds between probes. The fade itself is time based, so this only affects reaction latency.", true)]
        [SerializeField] private float _probeInterval = 0.05f;

        [PrototypeOnly(PendingDecisions.None, "Maximum occluders handled in one probe.", true)]
        [SerializeField] private int _maxOccluders = 12;

        private float _targetAlpha = 0.22f;
        private float _fadeTime = 0.15f;

        private PrototypeCameraRig _rig;
        private RaycastHit[] _hits;
        private readonly List<OccluderFadeState> _touched = new List<OccluderFadeState>(12);
        private float _nextProbeTime;
        private int _layerMask;
        private DebugPanelReadout _readout;

        protected override void OnEnable()
        {
            base.OnEnable();
            FeelRuntime.Changed += ApplyFeel;
            ApplyFeel(FeelRuntime.Current);

            _rig = GetComponent<PrototypeCameraRig>();
            _hits = new RaycastHit[Mathf.Max(1, _maxOccluders)];
            int occluderLayer = PrototypeLayers.CameraOccluder;
            _layerMask = occluderLayer >= 0 ? 1 << occluderLayer : 0;
            if (_layerMask == 0)
            {
                Debug.LogWarning(
                    "[Camera] The CameraOccluder layer does not exist, so nothing will ever fade.", this);
            }

            _nextProbeTime = 0f;

            _readout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Occluders faded", DescribeOccluders, -46);
        }

        protected override void OnDisable()
        {
            FeelRuntime.Changed -= ApplyFeel;
            DebugPanelRegistry.Remove(_readout);
            _readout = null;

            _touched.Clear();
            base.OnDisable();
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            ApplyFeel(FeelRuntime.Current);
        }

        private void ApplyFeel(FeelSnapshot snapshot)
        {
            CameraFeel cam = snapshot.Camera;
            _targetAlpha = Mathf.Clamp01(cam.occlusionTargetAlpha);
            _fadeTime = Mathf.Max(0.01f, cam.occlusionFadeSeconds);
        }

        private void LateUpdate()
        {
            if (_rig == null || !HasConfig)
            {
                return;
            }

            Transform target = _rig.FollowTarget;
            if (target == null)
            {
                return;
            }

            // Unscaled: an occluder that is in the way when the game pauses should not
            // stay solid on the pause screen.
            if (Time.unscaledTime < _nextProbeTime)
            {
                MarkTouched();
                return;
            }

            _nextProbeTime = Time.unscaledTime + Mathf.Max(0f, _probeInterval);

            Vector3 from = _rig.StablePosition;
            Vector3 to = target.position + Vector3.up * _probeHeight;
            Vector3 delta = to - from;
            float distance = delta.magnitude;

            _touched.Clear();

            if (distance <= 0.01f)
            {
                return;
            }

            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(
                from, _probeRadius, direction, _hits, distance, _layerMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                OccluderFadeState state = Resolve(collider);
                if (state == null || _touched.Contains(state))
                {
                    continue;
                }

                _touched.Add(state);
            }

            MarkTouched();
        }

        private void MarkTouched()
        {
            for (int i = 0; i < _touched.Count; i++)
            {
                OccluderFadeState state = _touched[i];
                if (state != null)
                {
                    state.MarkOccluding(_targetAlpha, _fadeTime);
                }
            }
        }

        /// <summary>
        /// Finds, or creates once, the fade component for a hit collider. The component
        /// goes on the transform that owns the renderers, so a wall built from several
        /// child pieces fades as one object.
        /// </summary>
        private static OccluderFadeState Resolve(Collider collider)
        {
            OccluderFadeState existing = collider.GetComponentInParent<OccluderFadeState>();
            if (existing != null)
            {
                return existing;
            }

            Transform owner = collider.transform;
            if (owner.GetComponentInChildren<Renderer>(true) == null)
            {
                Renderer parentRenderer = collider.GetComponentInParent<Renderer>();
                if (parentRenderer == null)
                {
                    return null;
                }

                owner = parentRenderer.transform;
            }

            return owner.gameObject.AddComponent<OccluderFadeState>();
        }

        private string DescribeOccluders()
        {
            return _touched.Count + " occluding, target alpha " + _targetAlpha.ToString("0.00");
        }
    }
}
