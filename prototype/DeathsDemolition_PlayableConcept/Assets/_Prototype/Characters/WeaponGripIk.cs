using System;
using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 · B4 · III A6.3 · the left hand on the weapon: after the Animator AND after <c>WeaponMountAlignment</c> (order 900)
    /// has placed the weapon, a two-bone solve (upper arm → forearm → hand) puts the left hand on the model's <c>grip_l</c>
    /// socket (AP-021; the AP-013 models' <c>marker_foregrip</c> until those land) — no frame of lag. The owner's gate closes it
    /// during reloads / switches / lowered / pickup / hurt (the clip's hand leaves the weapon), and the clip events
    /// <c>lh_release</c> / <c>lh_grab</c> (AP-022) close / open it exactly. Blended over <see cref="BlendSeconds"/>. Zero allocation.
    /// </summary>
    [DefaultExecutionOrder(910)]
    [DisallowMultipleComponent]
    public sealed class WeaponGripIk : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "Left-hand grip IK blend time, seconds; target re-search interval when the weapon model changes. Invented / III A6.3.", true)]
        public const float BlendSeconds = 0.10f, SearchInterval = 0.25f;

        public static readonly string[] GripNames = { "grip_l", "marker_foregrip" };

        /// <summary>Global switch (tests / A-B).</summary>
        public static bool Enabled = true;

        private Animator _animator;
        private Transform _searchRoot;
        private Func<bool> _gate;
        private Transform _upper, _lower, _hand, _grip, _weaponFrame;
        private bool _hasHandInWeapon;
        private Vector3 _handInWeapon;

        /// <summary>
        /// WP-071 B4b: the v3 set's left-hand point in the weapon frame (AP-022 hands_in_weapon_frame — P-06 authors the palm on the
        /// handguard, not on the model's grip_l, and ar_2's grip_l is out of the class stance's reach). Off = the model's grip marker.
        /// </summary>
        public void SetHandInWeapon(bool has, Vector3 pointInWeaponFrame)
        {
            _hasHandInWeapon = has;
            _handInWeapon = pointInWeaponFrame;
        }

        /// <summary>The model root the weapon frame is read from (AP021_&lt;id&gt;; tests).</summary>
        public Transform WeaponFrame => _weaponFrame;
        private float _weight, _searchClock;
        private bool _eventReleased;

        public float Weight => _weight;
        /// <summary>The owner's gate as last read (tests).</summary>
        public bool GateOpen { get; private set; }
        public Transform Grip => _grip;
        /// <summary>Distance hand → grip after the last solve (m; tests).</summary>
        public float LastError { get; private set; }

        public void Configure(Animator animator, Transform searchRoot, Func<bool> gate)
        {
            _animator = animator;
            _searchRoot = searchRoot;
            _gate = gate;
            _upper = HumanoidBones.Bone(animator, HumanBodyBones.LeftUpperArm);
            _lower = HumanoidBones.Bone(animator, HumanBodyBones.LeftLowerArm);
            _hand = HumanoidBones.Bone(animator, HumanBodyBones.LeftHand);
            _grip = null;
            _weight = 0f;
        }

        /// <summary>lh_release (true) / lh_grab (false) from the clip.</summary>
        public void SetReleased(bool released) => _eventReleased = released;

        private void LateUpdate()
        {
            if (_upper == null || _lower == null || _hand == null) return;
            float dt = Time.deltaTime;
            _searchClock -= dt;
            if ((_grip == null || !_grip.gameObject.activeInHierarchy) && _searchClock <= 0f)
            {
                _searchClock = SearchInterval;
                _grip = FindGrip(_searchRoot);
                // the weapon rig's own frame (S-02 contract: +Z barrel, +Y up, +X right) — the AP021_ model node under it carries the FBX
                // axis conversion (270°, 0, 0) and is NOT the frame hands_in_weapon_frame is expressed in
                DD.Prototype.Weapons.WeaponVisualRig rig = _searchRoot != null ? _searchRoot.GetComponentInChildren<DD.Prototype.Weapons.WeaponVisualRig>() : null;
                _weaponFrame = rig != null ? rig.transform : FindFrame(_searchRoot);
            }

            GateOpen = _gate == null || _gate();
            bool on = Enabled && _grip != null && _grip.gameObject.activeInHierarchy && !_eventReleased && GateOpen;
            _weight = Mathf.MoveTowards(_weight, on ? 1f : 0f, dt / BlendSeconds);
            if (_weight <= 0f || _grip == null) { LastError = float.NaN; return; }
            Vector3 goal = _hasHandInWeapon && _weaponFrame != null ? _weaponFrame.TransformPoint(_handInWeapon) : _grip.position;
            Vector3 target = Vector3.Lerp(_hand.position, goal, _weight);
            Solve(_upper, _lower, _hand, target);
            LastError = Vector3.Distance(_hand.position, goal);
        }

        /// <summary>Analytic two-bone IK in world space (the bend plane is the current one: the elbow keeps its side).</summary>
        public static void Solve(Transform a, Transform b, Transform c, Vector3 t)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lcb = (pc - pb).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((t - pa).magnitude, 1e-3f, lab + lcb - 1e-3f);
            Vector3 ac = (pc - pa).normalized, ab = (pb - pa).normalized, ba = (pa - pb).normalized, bc = (pc - pb).normalized, at = (t - pa).normalized;
            float acAb0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac, ab), -1f, 1f));
            float baBc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ba, bc), -1f, 1f));
            float acAt0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac, at), -1f, 1f));
            float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            Vector3 axis0 = Vector3.Cross(ac, ab);
            if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(ac, a.up);
            axis0.Normalize();
            Vector3 axis1 = Vector3.Cross(ac, at);
            Quaternion r0 = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis0);
            Quaternion r1 = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, axis0);
            Quaternion r2 = axis1.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(acAt0 * Mathf.Rad2Deg, axis1.normalized) : Quaternion.identity;
            Quaternion handRot = c.rotation;
            b.rotation = r1 * b.rotation;          // the elbow first (child), then the shoulder carries it
            a.rotation = r2 * r0 * a.rotation;
            c.rotation = handRot;                   // the hand keeps its animated orientation
        }

        // The AP-021 model root (AP021_<id>, S-02 mounts it so gripOffset lands on the socket): the weapon frame of hands_in_weapon_frame.
        private static Transform FindFrame(Transform t)
        {
            if (t == null) return null;
            if (t.name.StartsWith("AP021_", StringComparison.Ordinal) && t.gameObject.activeInHierarchy) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindFrame(t.GetChild(i));
                if (r != null) return r;
            }

            return null;
        }

        /// <summary>
        /// WP-071 B4b (C-00 25.09 / 26.09): the same solve, then the chain turns about the shoulder → target axis so the elbow points at
        /// <paramref name="pole"/> (FP left arm: down and slightly back, off screen — P-06's measurement). The hand keeps its rotation.
        /// </summary>
        public static void Solve(Transform a, Transform b, Transform c, Vector3 t, Vector3 pole)
        {
            Solve(a, b, c, t);
            Vector3 axis = c.position - a.position;
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            Vector3 elbow = Vector3.ProjectOnPlane(b.position - a.position, axis), want = Vector3.ProjectOnPlane(pole - a.position, axis);
            if (elbow.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            Quaternion handRot = c.rotation;
            a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(elbow, want, axis), axis) * a.rotation;
            c.rotation = handRot;
        }

        private static Transform FindGrip(Transform root)
        {
            if (root == null) return null;
            for (int n = 0; n < GripNames.Length; n++)
            {
                Transform t = FindDeep(root, GripNames[n]);
                if (t != null) return t;
            }

            return null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name && t.gameObject.activeInHierarchy) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics() => Enabled = true;
    }
}
