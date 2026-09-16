using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-032 · ADR-032-1 · Head / upper-body tracking through the built-in Humanoid LookAt
    /// (no Animation Rigging). Sits next to the Animator; the owner binds a target provider:
    /// the zombie looks at the threat (player head), the player at its aim point — the SAME
    /// aim direction <see cref="PlayerAimLayer"/> uses, so the two never disagree (S-02 aim
    /// data is the single source). Weight fades out beyond <see cref="MaxAngleDegrees"/>,
    /// while dying / rising, and on far bodies (CharacterAnimatorLod). Zero allocation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterLookAt : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Angle from the body's forward beyond which the look fades to 0 (a zombie does not track a player behind it). Invented.", true)]
        public const float MaxAngleDegrees = 80f;

        [PrototypeOnly(PendingDecisions.None, "Angle band over which the weight fades to 0 before MaxAngleDegrees. Invented.", true)]
        public const float FadeDegrees = 25f;

        [PrototypeOnly(PendingDecisions.None, "Weight follow response (1/s). Invented.", true)]
        public const float Response = 8f;

        [SerializeField] private Animator _animator;
        [SerializeField, Range(0f, 1f)] private float _headWeight = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _bodyWeight = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _eyesWeight = 0f;
        [SerializeField, Range(0f, 1f)] private float _clampWeight = 0.5f;

        private Func<Vector3> _target;      // world point (Vector3.zero = none)
        private Func<bool> _enabled;        // owner gate (alive, not rising, near)
        private float _weight;
        private Vector3 _point;
        private bool _hasPoint;
        private static int s_activeCount;

        /// <summary>Bodies with a non-zero look weight this frame (probe / tests).</summary>
        public static int ActiveCount => s_activeCount;
        public float Weight => _weight;
        public Vector3 Point => _point;
        public bool Active => _hasPoint && _weight > 0.001f;

        public void Configure(Animator animator, float headWeight, float bodyWeight)
        {
            _animator = animator;
            _headWeight = headWeight;
            _bodyWeight = bodyWeight;
        }

        /// <summary>Owner binding: where to look (world point) and whether tracking is allowed right now.</summary>
        public void Bind(Func<Vector3> target, Func<bool> enabledGate)
        {
            _target = target;
            _enabled = enabledGate;
            _weight = 0f;
            _hasPoint = false;
        }

        public void ResetWeight()
        {
            _weight = 0f;
            _hasPoint = false;
        }

        private void OnDisable()
        {
            _weight = 0f;
            _hasPoint = false;
        }

        private void LateUpdate()
        {
            // Count once per frame from LateUpdate (OnAnimatorIK runs per IK-pass layer).
            if (Time.frameCount != s_frame)
            {
                s_frame = Time.frameCount;
                s_activeCount = 0;
            }

            if (Active) s_activeCount++;
        }

        private static int s_frame = -1;

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || !_animator.isHuman) return;
            if (_ikFrame == Time.frameCount) return;   // one evaluation per frame (the callback fires per IK-pass layer)
            _ikFrame = Time.frameCount;

            bool allowed = _target != null && (_enabled == null || _enabled());
            Vector3 point = allowed ? _target() : Vector3.zero;
            float target = 0f;
            if (allowed && point != Vector3.zero)
            {
                Vector3 to = point - transform.position;
                to.y = 0f;
                float angle = to.sqrMagnitude > 1e-6f ? Vector3.Angle(transform.forward, to) : 0f;
                target = angle <= MaxAngleDegrees - FadeDegrees ? 1f : angle >= MaxAngleDegrees ? 0f : (MaxAngleDegrees - angle) / FadeDegrees;
                _point = point;
                _hasPoint = true;
            }

            float k = 1f - Mathf.Exp(-Response * Time.deltaTime);
            _weight = Mathf.Lerp(_weight, target, k);
            if (_weight < 0.001f)
            {
                _weight = 0f;
                _hasPoint = false;
                _animator.SetLookAtWeight(0f);
                return;
            }

            _animator.SetLookAtWeight(_weight, _bodyWeight, _headWeight, _eyesWeight, _clampWeight);
            _animator.SetLookAtPosition(_point);
        }

        private int _ikFrame = -1;
    }
}
