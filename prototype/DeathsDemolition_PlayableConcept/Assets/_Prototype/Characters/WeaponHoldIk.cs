using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-083 B2c (C-00 05.10, R2 N2-05) · the weapon-first third-person hold. The weapon's grip point is a pivot that rides the upper
    /// chest and looks along the aim. The right hand comes to the weapon: a two-bone solve puts the hand where its
    /// <c>socket_weapon_r</c> coincides with the pivot in position and rotation, with the elbow out and down. The left hand then
    /// follows WeaponGripIk onto the weapon's support point (grip_l / the AP-022 hands_in_weapon_frame point), as before. The clip's
    /// hand no longer decides where the weapon is. That moves the R1 defect "weapon on the shoulder" (TP grip wrong at bone level)
    /// out of the hold.
    /// <para>
    /// Runs in LateUpdate (order 890). That is after the Animator, the IK pass (aim layer spine, feet, look-at) and LocomotionLean
    /// (−10), so it solves on the final torso. It is before WeaponMountAlignment (900), which then lets the mount ride the socket, and
    /// before WeaponGripIk (910), so the left hand solves on the placed weapon. The owner's gate fades it out over
    /// <see cref="BlendSeconds"/> for reloads / switches / lowered / hurt, where the clip moves the weapon.
    /// </para>
    /// <para>
    /// Why not Animation Rigging (K2): on this pipeline's Humanoid bodies a rig works only when built after the Animator's first
    /// evaluation with its weights fixed at 1. A weight changed after the build never reaches the stream (WP-083 evidence
    /// b2c4…b2c12), so it cannot fade. C-00 05.10 decision (A).
    /// </para>
    /// <para>
    /// Off by default (<see cref="Enabled"/>): the hold offsets are starting values to be tuned with Hero v2 (AP-024 / AP-026) and
    /// the AD. Zero allocation per frame.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(890)]
    [DisallowMultipleComponent]
    public sealed class WeaponHoldIk : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "WP-083 B2c: the weapon-first hold — off until H1/H2 measure it on the player path and Hero v2 lands (tests / A-B switch it on).", true)]
        public static bool Enabled = false;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2c: hold weight blend time (s). Invented.", true)]
        public const float BlendSeconds = 0.12f;

        /// <summary>
        /// The hip hold: the grip point (the right hand's socket_weapon_r) relative to the upper chest, in the aim's yaw frame
        /// (x right, y up, z forward; m). Starting value (tests / A-B may set it); per-class values come with Hero v2 and the AD.
        /// </summary>
        public static Vector3 HipHold = DefaultHipHold;

        // chest-high ready hold: of the measured candidates the one with the most reach margin for the support hand (left shoulder →
        // a handguard 18 cm ahead of the grip 37.5 cm of a 54.5 cm arm on the current player; K/wp-083/evidence b2c14)
        public static readonly Vector3 DefaultHipHold = new Vector3(0.10f, -0.12f, 0.22f);

        /// <summary>The right elbow's pole relative to the upper arm, in the aim's yaw frame (out, down, back; m).</summary>
        public static readonly Vector3 RightElbowPole = new Vector3(0.30f, -0.30f, -0.15f);

        private Animator _animator;
        private Transform _socket, _chest, _upper, _lower, _hand;
        private Func<Vector3> _aimDirection;
        private Func<bool> _gate;
        private Quaternion _socketInHand = Quaternion.identity;
        private Vector3 _socketOffset;   // socket position in the hand's rotation frame, world length
        private float _weight;
        private bool _ready;

        /// <summary>The hold carries weight this frame (WeaponMountAlignment lets the mount ride the socket).</summary>
        public bool Active => _ready && _weight > 0f;
        public bool Ready => _ready;
        public float Weight => _weight;
        /// <summary>The last solve's grip pivot (world) — where socket_weapon_r is held.</summary>
        public Vector3 PivotPosition { get; private set; }
        public Quaternion PivotRotation { get; private set; } = Quaternion.identity;
        /// <summary>socket_weapon_r ↔ pivot after the last solve at full weight (m; NaN otherwise; tests / H1).</summary>
        public float LastError { get; private set; } = float.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Enabled = false;
            HipHold = DefaultHipHold;
        }

        /// <summary>
        /// Owner binding (PlayerSkinnedVisual.Attach): the animator, the right hand's weapon socket, the aim and the gate (rest pose,
        /// alive). The socket's pose in the hand is read once (a child of the hand: pose-independent).
        /// </summary>
        public void Configure(Animator animator, Transform weaponSocket, Func<Vector3> aimDirection, Func<bool> gate)
        {
            _animator = animator;
            _socket = weaponSocket;
            _aimDirection = aimDirection;
            _gate = gate;
            _weight = 0f;
            _chest = HumanoidBones.Bone(animator, HumanBodyBones.UpperChest);
            if (_chest == null) _chest = HumanoidBones.Bone(animator, HumanBodyBones.Chest);
            _upper = HumanoidBones.Bone(animator, HumanBodyBones.RightUpperArm);
            _lower = HumanoidBones.Bone(animator, HumanBodyBones.RightLowerArm);
            _hand = HumanoidBones.Bone(animator, HumanBodyBones.RightHand);
            _ready = _socket != null && _chest != null && _upper != null && _lower != null && _hand != null;
            if (!_ready) return;
            _socketInHand = Quaternion.Inverse(_hand.rotation) * _socket.rotation;
            _socketOffset = Quaternion.Inverse(_hand.rotation) * (_socket.position - _hand.position);
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            bool on = Enabled && (_gate == null || _gate());
            _weight = Mathf.MoveTowards(_weight, on ? 1f : 0f, dt / BlendSeconds);
            if (_weight <= 0f) { LastError = float.NaN; return; }

            Transform body = _animator.transform;
            Vector3 aim = _aimDirection != null ? _aimDirection() : Vector3.zero;
            if (aim.sqrMagnitude < 1e-6f) aim = body.forward;
            aim.Normalize();
            Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up);
            Quaternion yaw = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat, Vector3.up) : Quaternion.LookRotation(Vector3.ProjectOnPlane(body.forward, Vector3.up), Vector3.up);

            // the grip pivot and the hand pose that puts socket_weapon_r on it
            Vector3 pivot = _chest.position + yaw * HipHold;
            Quaternion pivotRot = Quaternion.LookRotation(aim, Vector3.up);
            Quaternion handRot = pivotRot * Quaternion.Inverse(_socketInHand);
            Vector3 handPos = pivot - handRot * _socketOffset;
            PivotPosition = pivot;
            PivotRotation = pivotRot;

            Quaternion animatedHand = _hand.rotation;
            WeaponGripIk.Solve(_upper, _lower, _hand, Vector3.Lerp(_hand.position, handPos, _weight), _upper.position + yaw * RightElbowPole);
            _hand.rotation = Quaternion.Slerp(animatedHand, handRot, _weight);
            LastError = _weight >= 1f ? Vector3.Distance(_socket.position, pivot) : float.NaN;
        }
    }
}
