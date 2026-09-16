using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-018 · ADR-018-2 · The player's <c>aim_layer</c>: on the same GameObject as the
    /// Animator, it turns Spine / Chest / UpperChest toward the aim direction (REQ-038) in the
    /// layer's IK pass, on top of whatever the base clip plays. The body root already turns
    /// toward the mouse (PlayerAim, code-driven — untouched), so the yaw here is only the
    /// residual facing error during the turn plus the aim pitch; the base clips keep the
    /// arms locked on the weapon and the torso quiet (±4°, AP-009) so nothing fights.
    /// Zero allocation; nothing runs on the rig-test controller (no IK pass there).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAimLayer : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Largest torso yaw toward the aim, degrees, split over three spine bones. Invented.", true)]
        private const float MaxYawDegrees = 35f;

        [PrototypeOnly(PendingDecisions.None, "Largest torso pitch toward the aim, degrees. Invented.", true)]
        private const float MaxPitchDegrees = 20f;

        [PrototypeOnly(PendingDecisions.None, "Aim follow response (1/s). Invented.", true)]
        private const float Response = 14f;

        private Animator _animator;
        private Func<Vector3> _aimDirection;
        private bool _active;
        private float _yaw;
        private float _pitch;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public bool Active => _active;

        public void Bind(Animator animator, Func<Vector3> aimDirection, bool active)
        {
            _animator = animator;
            _aimDirection = aimDirection;
            _active = active && animator != null && animator.isHuman;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (!_active || _animator == null || _aimDirection == null) return;
            Vector3 aim = _aimDirection();
            if (aim.sqrMagnitude < 1e-6f) return;

            Vector3 local = transform.InverseTransformDirection(aim.normalized);
            float targetYaw = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(0.05f, local.z)) * Mathf.Rad2Deg, -MaxYawDegrees, MaxYawDegrees);
            float targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -MaxPitchDegrees, MaxPitchDegrees);
            float k = 1f - Mathf.Exp(-Response * Time.deltaTime);
            _yaw = Mathf.Lerp(_yaw, targetYaw, k);
            _pitch = Mathf.Lerp(_pitch, targetPitch, k);

            Quaternion third = Quaternion.Euler(_pitch / 3f, _yaw / 3f, 0f);
            _animator.SetBoneLocalRotation(HumanBodyBones.Spine, third);
            _animator.SetBoneLocalRotation(HumanBodyBones.Chest, third);
            _animator.SetBoneLocalRotation(HumanBodyBones.UpperChest, third);
        }
    }
}
