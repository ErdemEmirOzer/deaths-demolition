using System;
using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 · B2 · III A2.3 · procedural lean over the locomotion pose: the spine tips forward under acceleration (back when
    /// braking) and rolls into turns (lateral acceleration). Applied in LateUpdate after the Animator and before the look-at /
    /// secondary chains, smoothed, only while the owner's gate is open (alive, near). Zero allocation.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    public sealed class LocomotionLean : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Degrees of lean per m/s² of acceleration, and the caps (forward / back / side). Invented.", true)]
        public const float DegreesPerAccel = 1.6f, MaxForward = 8f, MaxBack = 6f, MaxSide = 9f;
        [PrototypeOnly(PendingDecisions.None, "Lean response time constant, seconds. Invented.", true)]
        public const float Smoothing = 0.15f;

        /// <summary>Global switch (tests / A-B, and the Performans tier for far bodies).</summary>
        public static bool Enabled = true;

        private Transform _spine;
        private Transform _body;
        private Func<bool> _gate;
        private Vector3 _velocity, _lastVelocity;
        private Vector3 _accel;
        private bool _hasLast;
        private float _pitch, _roll;

        public float Pitch => _pitch;
        public float Roll => _roll;

        public void Configure(Transform spine, Transform body, Func<bool> gate)
        {
            _spine = spine;
            _body = body;
            _gate = gate;
            ResetLean();
        }

        /// <summary>The body's world velocity this frame (the owner's locomotion tick).</summary>
        public void Feed(Vector3 worldVelocity) => _velocity = worldVelocity;

        public void ResetLean()
        {
            _pitch = _roll = 0f;
            _accel = Vector3.zero;
            _hasLast = false;
        }

        private void LateUpdate()
        {
            if (_spine == null || _body == null) return;
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;
            bool on = Enabled && (_gate == null || _gate());
            if (_hasLast && on)
            {
                Vector3 a = (_velocity - _lastVelocity) / dt;
                float k = 1f - Mathf.Exp(-dt / Smoothing);
                _accel = Vector3.Lerp(_accel, a, k);
            }

            _lastVelocity = _velocity;
            _hasLast = true;
            Vector3 local = _body.InverseTransformDirection(_accel);
            float targetPitch = on ? Mathf.Clamp(local.z * DegreesPerAccel, -MaxBack, MaxForward) : 0f;
            float targetRoll = on ? Mathf.Clamp(-local.x * DegreesPerAccel, -MaxSide, MaxSide) : 0f;   // roll into the turn (lateral accel points inward)
            float s = 1f - Mathf.Exp(-dt / Smoothing);
            _pitch = Mathf.Lerp(_pitch, targetPitch, s);
            _roll = Mathf.Lerp(_roll, targetRoll, s);
            if (Mathf.Abs(_pitch) < 0.01f && Mathf.Abs(_roll) < 0.01f) return;
            _spine.rotation = Quaternion.AngleAxis(_pitch, _body.right) * Quaternion.AngleAxis(_roll, _body.forward) * _spine.rotation;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics() => Enabled = true;
    }
}
