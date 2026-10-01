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

        [PrototypeOnly(PendingDecisions.None, "WP-071 foot lock (event-driven: the clip's plant events): unused height slack, blend time, release at this fraction of the leg reach. Invented / III A2.4.", true)]
        public const float LockHeight = 0.025f, LockBlendSeconds = 0.06f, LockMaxStretch = 1.0f;   // B2: 0.96 released every walking stance (the clip's own stance leg is ~97 % extended) — release at full reach
        [PrototypeOnly(PendingDecisions.None, "WP-071 foot lock bodies per frame (Yüksek: player + ≤ 8 zombies; quality tier sets it). Invented.", true)]
        public static int MaxLockBodiesPerFrame = 9;
        [PrototypeOnly(PendingDecisions.None, "WP-071 (C-00 01.10, 4a): a plant within this time of the last accepted one is the same footfall — every clip of a locomotion blend fires its own foot_l / foot_r 1–2 frames apart (diagonals: 3 per step, each re-picking the held foot). Invented.", true)]
        public static float PlantDebounceSeconds = 0.1f;
        [PrototypeOnly(PendingDecisions.None, "WP-071 (C-00 01.10, 4a r25): the stretch release is relative — the reach at the lock frame plus this share of the leg (the IK-pass hip vs the IK target reads ≈ 10 % over the bone geometry, so the absolute 1.0 released every lock one frame later). Invented.", true)]
        public const float LockStretchSlack = 0.05f;

        [SerializeField] private Animator _animator;
        [SerializeField, Range(0f, 1f)] private float _rotationWeight = 0.6f;

        private Func<bool> _enabled;
        private Func<bool> _near;
        private Func<bool> _lockGate;
        // WP-071 foot lock state per foot (0 left, 1 right)
        private readonly bool[] _locked = new bool[2];
        private readonly float[] _lockWeight = new float[2];
        private readonly Vector3[] _lockPos = new Vector3[2];
        private readonly float[] _lockReach = new float[2];
        private int _plantPending;
        private float _lastPlantTime = -1f;
        /// <summary>WP-071 diagnostics: locked feet released by the leg stretch / the gate closing / a lift event / the other foot's plant.</summary>
        public int ReleasedByStretch { get; private set; }
        public int ReleasedByGate { get; private set; }
        public int ReleasedByLift { get; private set; }
        public int ReleasedByPlant { get; private set; }
        /// <summary>WP-071 diagnostics: plant events accepted / dropped as the same footfall.</summary>
        public int PlantsAccepted { get; private set; }
        public int PlantsDebounced { get; private set; }
        /// <summary>WP-071 diagnostics: the leg length the stretch release uses, and hip→held point / leg length at the last stretch release.</summary>
        public float LegLength => _legLength;
        public float LastReleaseRatio { get; private set; }
        private readonly bool[] _releasePending = new bool[2];
        private readonly float[] _gapThisFrame = new float[2];
        private readonly Vector3[] _targetThisFrame = new Vector3[2];
        private float _legLength;
        private bool _lockThisFrame;
        private static int s_locked;
        /// <summary>WP-071: feet currently held by the lock (tests / probes).</summary>
        public int LockedFeet => (_locked[0] ? 1 : 0) + (_locked[1] ? 1 : 0);
        public bool IsLocked(int foot) => _locked[foot];
        /// <summary>WP-071 lock readouts of the last solve (tests): gate open, left-foot height over its ground contact, moving back in body space.</summary>
        public bool LockGateOpen => _lockThisFrame;
        public float LastHeightGap { get; private set; }

        public static int LockBodiesThisFrame => s_locked;
        private float _weight;
        private float _pelvisOffset;
        private float _leftLift, _rightLift;
        private int _ikFrame = -1;
        private int _lastServedFrame = -2;
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
        /// <summary>Tests only: every served body allocates once (proves the crowd allocation gate catches a per-body allocation).</summary>
        public static bool ProbeAllocPerServe;
        private static byte[] s_probeSink;

        public void Configure(Animator animator)
        {
            _animator = animator;
        }

        /// <summary>Owner binding: allowed (alive, not dying/rising) and near (LOD0/1).</summary>
        public void Bind(Func<bool> enabledGate, Func<bool> nearGate) => Bind(enabledGate, nearGate, null);

        /// <summary>WP-071: + the foot-lock gate (owner: cadence rate ≤ <see cref="CadenceBand.LockMaxRate"/>, moving, near).</summary>
        public void Bind(Func<bool> enabledGate, Func<bool> nearGate, Func<bool> lockGate)
        {
            _enabled = enabledGate;
            _near = nearGate;
            _lockGate = lockGate;
            ClearLock();
            _weight = 0f;
            _pelvisOffset = 0f;
        }

        public static void ResetStatics()
        {
            s_frame = -1;
            s_served = 0;
            s_raycasts = 0;
            s_locked = 0;
            Force = -1;
            ProbeAllocPerServe = false;
            MaxLockBodiesPerFrame = 9;
            PlantDebounceSeconds = 0.1f;
        }

        private void ClearLock()
        {
            for (int i = 0; i < 2; i++) { _locked[i] = false; _lockWeight[i] = 0f; _releasePending[i] = false; }
            _plantPending = 0;
        }

        private void OnDisable()
        {
            _weight = 0f;
            _pelvisOffset = 0f;
            ClearLock();
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
                s_locked = 0;
            }

            bool allowed = Force == 1 || (Force != 0 && (_enabled == null || _enabled()) && (_near == null || _near()));
            bool budget = allowed && s_served < MaxBodiesPerFrame;
            _servedThisFrame = budget;
            // R1-B AI3 (C-00 30.09): the foot lock lives only while the IK is served every frame — a gap (budget, LOD / manual
            // step, rise, disabled Animator) would otherwise blend the foot back toward a stale world point (a pop up to ~0.4 m).
            if (!budget || _lastServedFrame != Time.frameCount - 1) ClearLock();
            if (budget) _lastServedFrame = Time.frameCount;
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
            if (ProbeAllocPerServe) s_probeSink = new byte[16];   // tests: the positive control for the crowd allocation gate
            _lockThisFrame = _lockGate != null && _lockGate() && s_locked < MaxLockBodiesPerFrame;
            if (_lockThisFrame) s_locked++;
            if (_legLength <= 0f)
            {
                Transform up = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), lo = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), ft = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                if (up != null && lo != null && ft != null) _legLength = Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, ft.position);
            }

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

        /// <summary>
        /// WP-071 · III A2.4 foot lock: in stance (the animated foot at the ground and moving back in body space) the foot is
        /// held at its landing point; it releases on lift-off, when the leg would stretch past <see cref="LockMaxStretch"/>
        /// (the slide wins), or when the gate closes — blended over <see cref="LockBlendSeconds"/>.
        /// </summary>
        private Vector3 Lock(int foot, Vector3 animated, Vector3 target, float groundFootY)
        {
            float gap = animated.y - groundFootY;
            if (foot == 0) LastHeightGap = gap;
            // event-driven stance: a plant event locks the LOWER foot at its landing point and frees the other (the stance
            // window "own contact → the other foot's contact", as the AP-009 slip gates define it); foot_*_off frees one foot.
            if (_plantPending == 1 && foot == 1)
            {
                // both feet solved this frame: decide which one landed
                int lower = _gapThisFrame[0] <= gap ? 0 : 1;
                if (_lockThisFrame)
                {
                    _locked[lower] = true;
                    _lockPos[lower] = lower == 1 ? target : _targetThisFrame[0];
                    Transform lockHip = _animator.GetBoneTransform(lower == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                    _lockReach[lower] = lockHip != null ? Vector3.Distance(lockHip.position, _lockPos[lower]) : 0f;   // same metric as the release
                }

                if (_locked[1 - lower]) ReleasedByPlant++;
                _locked[1 - lower] = false;
                _plantPending = 0;
            }

            if (foot == 0) { _gapThisFrame[0] = gap; _targetThisFrame[0] = target; }
            if (_releasePending[foot]) { if (_locked[foot]) ReleasedByLift++; _locked[foot] = false; _releasePending[foot] = false; }
            if (!_lockThisFrame) { if (_locked[foot]) ReleasedByGate++; _locked[foot] = false; }
            if (_locked[foot] && _legLength > 0f)
            {
                Transform hip = _animator.GetBoneTransform(foot == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                float reach = hip != null ? Vector3.Distance(hip.position, _lockPos[foot]) : 0f;
                // r25: relative to the reach at the lock frame (same metric) — the body passes over the held foot and lets it go past its landing reach
                float limit = Mathf.Max(_legLength * LockMaxStretch, _lockReach[foot] + _legLength * LockStretchSlack);
                if (hip != null && reach > limit) { _locked[foot] = false; ReleasedByStretch++; LastReleaseRatio = reach / _legLength; }   // the slide wins
            }

            float step = Time.deltaTime / LockBlendSeconds;
            _lockWeight[foot] = Mathf.MoveTowards(_lockWeight[foot], _locked[foot] ? 1f : 0f, step);
            if (_lockWeight[foot] <= 0f) return target;
            Vector3 held = _lockPos[foot];
            held.y = target.y;
            return Vector3.Lerp(target, held, _lockWeight[foot]);
        }

        /// <summary>WP-071: the clip's foot_l / foot_r contact (owner forwards the event; the lower foot is taken, names aside).</summary>
        public void NotifyPlant()
        {
            // WP-071 (C-00 01.10, 4a): one footfall, one plant — the blend's other clips fire the same contact 1–2 frames later
            if (_lastPlantTime >= 0f && Time.time - _lastPlantTime < PlantDebounceSeconds) { PlantsDebounced++; return; }
            _lastPlantTime = Time.time;
            PlantsAccepted++;
            _plantPending = 1;
        }

        /// <summary>WP-071: foot_l_off / foot_r_off (AP-022) — free that foot now (0 left, 1 right).</summary>
        public void NotifyLift(int foot) => _releasePending[foot] = true;

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
                target = Lock(goal == AvatarIKGoal.LeftFoot ? 0 : 1, animated, target, targetY);
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
