using System;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-032 · ADR-032-1 · Foot placement on the real ground through the built-in Humanoid
    /// IK goals (no Animation Rigging): each foot's animated position is dropped onto the
    /// ground hit below it (steps, thresholds, the bridge) and the pelvis is lowered by the
    /// deeper foot so no leg over-stretches. Budgeted: only near bodies (not
    /// <see cref="CharacterAnimatorLod.Far"/>) and at most <see cref="MaxBodiesPerFrame"/>
    /// per frame (two raycasts each), off while dying / rising (the clip owns the floor
    /// contact there). Zero allocation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootIkAdapter : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Bodies that get foot IK in one frame (nearest LOD0/1 first by arrival). Cost cap ≤ 0.6 ms @ 40. Invented.", true)]
        public const int MaxBodiesPerFrame = 12;

        [PrototypeOnly(PendingDecisions.None, "Raycast starts this far above the animated ankle and reaches this far below (steps / thresholds). Invented.", true)]
        public const float RayUp = 0.5f, RayDown = 0.6f;

        [PrototypeOnly(PendingDecisions.None, "Largest pelvis drop / foot lift the adapter applies, metres. Invented.", true)]
        public const float MaxCorrection = 0.35f;

        [PrototypeOnly(PendingDecisions.None, "Position / rotation IK weight response (1/s). Invented.", true)]
        public const float Response = 12f;

        [SerializeField] private Animator _animator;
        [SerializeField, Range(0f, 1f)] private float _rotationWeight = 0.6f;

        private Func<bool> _enabled;
        private Func<bool> _near;
        private float _weight;
        private float _pelvisOffset;
        private float _leftLift, _rightLift;
        private int _ikFrame = -1;
        private bool _servedThisFrame;
        private static int s_frame = -1;
        private static int s_served;
        private static int s_raycasts;

        /// <summary>Bodies served this frame / raycasts this frame (probe / tests).</summary>
        public static int ServedThisFrame => s_served;
        public static int RaycastsThisFrame => s_raycasts;
        public float Weight => _weight;
        public float PelvisOffset => _pelvisOffset;
        public float LeftLift => _leftLift;
        public float RightLift => _rightLift;
        public bool Served => _servedThisFrame;
        /// <summary>Tests: force the adapter on/off regardless of the near gate (-1 = automatic).</summary>
        public static int Force = -1;

        public void Configure(Animator animator)
        {
            _animator = animator;
        }

        /// <summary>Owner binding: allowed (alive, not dying/rising) and near (LOD0/1).</summary>
        public void Bind(Func<bool> enabledGate, Func<bool> nearGate)
        {
            _enabled = enabledGate;
            _near = nearGate;
            _weight = 0f;
            _pelvisOffset = 0f;
        }

        public static void ResetStatics()
        {
            s_frame = -1;
            s_served = 0;
            s_raycasts = 0;
            Force = -1;
        }

        private void OnDisable()
        {
            _weight = 0f;
            _pelvisOffset = 0f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (_animator == null || !_animator.isHuman) return;
            if (_ikFrame == Time.frameCount) return;
            _ikFrame = Time.frameCount;
            if (s_frame != Time.frameCount)
            {
                s_frame = Time.frameCount;
                s_served = 0;
                s_raycasts = 0;
            }

            bool allowed = Force == 1 || (Force != 0 && (_enabled == null || _enabled()) && (_near == null || _near()));
            bool budget = allowed && s_served < MaxBodiesPerFrame;
            _servedThisFrame = budget;
            float k = 1f - Mathf.Exp(-Response * Time.deltaTime);
            _weight = Mathf.Lerp(_weight, budget ? 1f : 0f, k);
            if (_weight < 0.001f)
            {
                _weight = 0f;
                _pelvisOffset = 0f;
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
                _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
                return;
            }

            if (!budget)
            {
                // Fading out: keep the last targets, just lower the weights.
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, _weight);
                _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, _weight);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, _weight * _rotationWeight);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, _weight * _rotationWeight);
                return;
            }

            s_served++;
            float footHeight = _animator.leftFeetBottomHeight;
            Solve(AvatarIKGoal.LeftFoot, footHeight, out float leftDelta);
            Solve(AvatarIKGoal.RightFoot, footHeight, out float rightDelta);
            _leftLift = leftDelta;
            _rightLift = rightDelta;

            // Pelvis follows the lower foot (a negative delta = the ground is below the animated foot).
            // WP-032D (WP-053 bisect): the three-argument Mathf.Min is the params overload — one float[] per served body per frame.
            float lowest = Mathf.Min(Mathf.Min(leftDelta, rightDelta), 0f);
            _pelvisOffset = Mathf.Lerp(_pelvisOffset, Mathf.Max(-MaxCorrection, lowest), k);
            if (Mathf.Abs(_pelvisOffset) > 1e-4f)
            {
                Vector3 body = _animator.bodyPosition;
                body.y += _pelvisOffset * _weight;
                _animator.bodyPosition = body;
            }
        }

        private void Solve(AvatarIKGoal goal, float footHeight, out float delta)
        {
            Vector3 animated = _animator.GetIKPosition(goal);
            Vector3 origin = animated + Vector3.up * RayUp;
            delta = 0f;
            s_raycasts++;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, RayUp + RayDown, PrototypeLayers.AimBlockMask, QueryTriggerInteraction.Ignore))
            {
                float targetY = hit.point.y + footHeight;
                delta = Mathf.Clamp(targetY - animated.y, -MaxCorrection, MaxCorrection);
                Vector3 target = animated;
                target.y = animated.y + delta;
                _animator.SetIKPositionWeight(goal, _weight);
                _animator.SetIKPosition(goal, target);
                Quaternion animatedRot = _animator.GetIKRotation(goal);
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, hit.normal);
                _animator.SetIKRotationWeight(goal, _weight * _rotationWeight);
                _animator.SetIKRotation(goal, tilt * animatedRot);
            }
            else
            {
                _animator.SetIKPositionWeight(goal, 0f);
                _animator.SetIKRotationWeight(goal, 0f);
            }
        }
    }
}
