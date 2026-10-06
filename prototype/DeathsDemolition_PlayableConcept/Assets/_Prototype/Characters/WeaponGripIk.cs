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

        /// <summary>The set's left-hand point in the weapon frame and whether it is in use (tests).</summary>
        public Vector3 HandInWeapon => _handInWeapon;
        public bool HasHandInWeapon => _hasHandInWeapon;

        /// <summary>The model root the weapon frame is read from (AP021_&lt;id&gt;; tests).</summary>
        public Transform WeaponFrame => _weaponFrame;
        private float _weight, _searchClock;
        private bool _eventReleased;

        public float Weight => _weight;
        /// <summary>The owner's gate as last read (tests).</summary>
        public bool GateOpen { get; private set; }
        public Transform Grip => _grip;
        /// <summary>WP-083: the left upper arm's roll about its own axis against the clip's pose after the last solve (°; tests, ≤ 15).</summary>
        public float UpperArmRollDegrees { get; private set; }

        /// <summary>
        /// WP-083 (C-00 05.10, v4 frames: the upper arm rolled out and back, the shoulder cloth behind the body): the two-bone solve
        /// without roll. The analytic solve swings the chain along the shortest path, and a large swing also turns the upper arm about
        /// its own axis. This removes that twist (the upper arm keeps the clip's roll; the elbow does not move, the twist axis runs
        /// through it), then aims the forearm from the elbow at the target along the shortest path. The hand keeps its rotation.
        /// </summary>
        public static void SolveNoRoll(Transform a, Transform b, Transform c, Vector3 t)
        {
            Quaternion upperAnimated = a.rotation, handRot = c.rotation;
            Vector3 axis = b.position - a.position;   // the clip's bone direction: q = swing · twist about it, swing carries it onto the new one
            Solve(a, b, c, t);
            if (axis.sqrMagnitude > 1e-8f)
            {
                Quaternion delta = a.rotation * Quaternion.Inverse(upperAnimated);
                a.rotation = Swing(delta, axis.normalized) * upperAnimated;
            }

            Vector3 have = c.position - b.position, want = t - b.position;
            if (have.sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f) b.rotation = Quaternion.FromToRotation(have, want) * b.rotation;
            c.rotation = handRot;
        }

        /// <summary>The swing part of <paramref name="q"/> about the unit <paramref name="axis"/> (swing-twist: q = swing · twist).</summary>
        public static Quaternion Swing(Quaternion q, Vector3 axis)
        {
            Vector3 v = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Dot(v, axis) * axis;
            var twist = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (m < 1e-6f) return q;
            twist = new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m);
            return q * Quaternion.Inverse(twist);
        }

        /// <summary>
        /// WP-083 (C-00 05.10): how far a solve turned an upper arm about its own axis (shoulder → elbow) against the clip's pose — the
        /// twist part of the change (swing-twist), in degrees. A solve should swing the arm, not roll it.
        /// </summary>
        public static float UpperArmRoll(Quaternion animated, Transform upper, Transform lower)
        {
            // the twist about the clip's own bone direction (the bone's local direction carried by the animated rotation)
            Vector3 local = Quaternion.Inverse(upper.rotation) * (lower.position - upper.position);
            if (local.sqrMagnitude < 1e-8f) return 0f;
            return Mathf.Abs(TwistCorrector.TwistAngle(upper.rotation * Quaternion.Inverse(animated), (animated * local).normalized));
        }

        /// <summary>Distance hand → grip after the last solve (m; tests).</summary>
        public float LastError { get; private set; }

        /// <summary>
        /// WP-083 B2f (AD 06.10 MUST-2; measured b2f3 / b2f4: the support fingers 10–15 cm off the handguard on ar_1 / smg_1 / C / ar_2 /
        /// sg_2, the palm turned up to 136° from the barrel axis — the solve put the WRIST on the goal and kept the clip's hand turn,
        /// while the goal, grip_l / hands_in_weapon_frame, is where the PALM meets the handguard). Contact placement (C-00 06.10): the
        /// hand's turn R is built first — the palm toward the barrel axis at the goal, the fingers across the handguard's underside
        /// (tangent, wrapping); the wrist goal is the contact minus R · the palm's offset; the two-bone solve takes the wrist there and
        /// the hand takes R (the clip's turn only blends in with the weight); then the index → little fingers curl about their joints
        /// until the tips reach the handguard (a cylinder about the axis through the contact; bisected, never past the surface).
        /// <see cref="LastError"/> is then the palm ↔ goal (H1's support-hand point). Off for a vertical foregrip (rl_1) and a Special
        /// (<see cref="SetGrasp"/>). Zero allocation.
        /// </summary>
        public static bool GraspEnabled = true;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2f: the support hand's grasp — the finger joints' full curl (proximal / intermediate / distal, °), the wrap of the contact about the axis from the authored goal toward the left side (°) and the hand's largest turn from the clip's (°). Council 06.10 (K/wp-083/KARAR-B2f-SOLEL-06.10.md); measured by R2HeroOlcumTests H1 / H1b.", true)]
        public const float CurlProximal = 85f, CurlIntermediate = 100f, CurlDistal = 75f;

        /// <summary>The grasp for the held weapon (PlayerSkinnedVisual.EquipWeapon: off on a vertical foregrip).</summary>
        public void SetGrasp(bool on) => _graspOn = on;

        /// <summary>The palm's angle to the barrel axis and the curl share after the last grasp (°, 0–1; NaN / 0 when it did not run; tests).</summary>
        public float LastPalmToAxisDegrees { get; private set; } = float.NaN;
        public float LastCurl { get; private set; }

        private bool _graspOn = true;
        private Transform _thumb;
        private readonly Transform[] _fingers = new Transform[12];   // index, middle, ring, little × proximal, intermediate, distal
        private readonly Quaternion[] _fingerLocal = new Quaternion[12];
        private readonly Vector3[] _fingerAxis = new Vector3[12];   // the flexion axis in each joint's own frame
        private Transform _middleProximal;
        private bool _geometry;
        private Vector3 _palmNormalLocal, _fingerLocalDir, _palmLocal;   // the hand's own frame: palm normal, wrist → middle knuckle, palm point

        public void Configure(Animator animator, Transform searchRoot, Func<bool> gate)
        {
            _animator = animator;
            _searchRoot = searchRoot;
            _gate = gate;
            _upper = HumanoidBones.Bone(animator, HumanBodyBones.LeftUpperArm);
            _lower = HumanoidBones.Bone(animator, HumanBodyBones.LeftLowerArm);
            _hand = HumanoidBones.Bone(animator, HumanBodyBones.LeftHand);
            _grip = null;
            _geometry = false;
            _theta = float.NaN;
            _poseHandler = animator != null && animator.avatar != null && animator.avatar.isHuman ? new HumanPoseHandler(animator.avatar, animator.transform) : null;
            _pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "Left Forearm Twist In-Out") _mTwist = i;
                else if (names[i] == "Left Hand Down-Up") _mDownUp = i;
                else if (names[i] == "Left Hand In-Out") _mInOut = i;
            }
            _thumb = HumanoidBones.Bone(animator, HumanBodyBones.LeftThumbIntermediate);
            _middleProximal = HumanoidBones.Bone(animator, HumanBodyBones.LeftMiddleProximal);
            for (int f = 0; f < 4; f++)
            for (int j = 0; j < 3; j++)
                _fingers[f * 3 + j] = HumanoidBones.Bone(animator, (HumanBodyBones)((int)HumanBodyBones.LeftIndexProximal + f * 3 + j));
            _weight = 0f;
            HandGeometry();   // the hand's frame in the bind pose (Configure runs at Attach, before the Animator's first pass)
        }

        /// <summary>
        /// WP-083 B2c: the left hand's goal on the held weapon (the set's hands_in_weapon_frame point, else the model's grip marker)
        /// without solving — WeaponHoldIk sizes the hold to it. False while no grip is found.
        /// </summary>
        public bool TryGetSupportGoal(out Vector3 goal)
        {
            goal = default;
            if (_grip == null || !_grip.gameObject.activeInHierarchy) return false;
            goal = _hasHandInWeapon && _weaponFrame != null ? _weaponFrame.TransformPoint(_handInWeapon) : _grip.position;
            return true;
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
                // WP-083 (C-00 05.10): an authored model's body (its rest pose is the rig's own, identity) carries the shot's kick —
                // the support goal rides it, so the left hand stays on the handguard through the recoil
                _weaponFrame = rig != null ? (rig.HasAuthoredModel && rig.Body != null ? rig.Body : rig.transform) : FindFrame(_searchRoot);
                _theta = float.NaN;
            }

            GateOpen = _gate == null || _gate();
            bool on = Enabled && _grip != null && _grip.gameObject.activeInHierarchy && !_eventReleased && GateOpen;
            _weight = Mathf.MoveTowards(_weight, on ? 1f : 0f, dt / BlendSeconds);
            if (_weight <= 0f || _grip == null) { LastError = float.NaN; return; }
            Vector3 goal = _hasHandInWeapon && _weaponFrame != null ? _weaponFrame.TransformPoint(_handInWeapon) : _grip.position;
            LastPalmToAxisDegrees = float.NaN;
            LastCurl = 0f;
            WristClamped = false;
            // the grasp's readouts are this frame's only (a weapon without the grasp reads zero, not the last grasped weapon's)
            LastWristShare = 0f;
            LastWristDegrees = 0f;
            LastMusclesWanted = Vector3.zero;
            if (!(GraspEnabled && _graspOn && _element.Valid && _weaponFrame != null && HandGeometry()))
            {
                Vector3 target = Vector3.Lerp(_hand.position, goal, _weight);
                Quaternion animatedUpper = _upper.rotation;
                SolveNoRoll(_upper, _lower, _hand, target);
                UpperArmRollDegrees = UpperArmRoll(animatedUpper, _upper, _lower);
                LastError = Vector3.Distance(_hand.position, goal);
                LastContact = goal;
                LastWristGoal = goal;
                return;
            }

            Grasp(goal, dt);
        }

        // ------------------------------------------------------------------ WP-083 B2f · the support hand's contact grasp
        // Council K/wp-083/KARAR-B2f-SOLEL-06.10.md (character-animator lead), C-00 06.10: the palm meets the grasped element at K, its
        // normal toward the element's axis, the fingers across it; the wrist goal follows from the palm (W = K − R · p); two solves a
        // frame (the forearm of the first sets the fingers' direction of the second); the wrist kept inside the anatomical gate; the
        // fingers curled onto the element. Zero allocation: the hand's frame is read once, the element is the weapon's row.

        /// <summary>The grasped element in the weapon frame (S-06 row per classic weapon): its axis (+Z handguard, +Y vertical foregrip),
        /// the section centre at the support station (the other two coordinates; the station itself comes from the goal) and radius.</summary>
        public readonly struct GraspElement
        {
            public readonly bool Valid, Vertical;
            public readonly Vector2 Centre;   // handguard: (x, y) in the weapon frame; vertical: (x, z)
            public readonly float Radius;      // the contact's: the section's surface along d(θ0) from the centre
            public readonly float CurlRadius;  // the fingers': half the section's narrower side (an oval / a flat grip wraps closer)

            public GraspElement(bool vertical, Vector2 centre, float radius, float curlRadius)
            {
                Valid = true;
                Vertical = vertical;
                Centre = centre;
                Radius = radius;
                CurlRadius = curlRadius;
            }
        }

        private GraspElement _element;

        /// <summary>The held weapon's grasped element (PlayerSkinnedVisual.EquipWeapon; a weapon without a row: no grasp).</summary>
        public void SetGraspElement(GraspElement element)
        {
            _element = element;
            _theta = float.NaN;
            ThetaSwitchCount = 0;   // a weapon's own count (a weapon without the grasp reads 0, not the last grasped weapon's)
        }

        /// <summary>
        /// Whether the contact grasp runs for a classic weapon (b2f15, 06.10): not on the vertical foregrips of lmg_2 / rl_1, whose
        /// authored (P-06, AD-approved) grip wraps the grip already — fingertips 0.1 / 1.8 cm on the clip against 4.4 / 4.4 cm under the
        /// curl; not on smg_1 (B; AD B2f MUST-1, gt GT-1): its clip holds the forearm so far from the handguard's wrap that the twist
        /// muscle clamps every frame (60/60, wanted 1.95) and the wrap target hunts (24–29 changes in 4 s) — the hand stays the B2e one
        /// until the hero clip's hold reaches it. Their rows stay for the section gate and the measures.
        /// </summary>
        public static bool GraspOf(DD.Prototype.Core.Config.ClassicWeaponId id) =>
            id != DD.Prototype.Core.Config.ClassicWeaponId.Lmg2 && id != DD.Prototype.Core.Config.ClassicWeaponId.Rl1 &&
            id != DD.Prototype.Core.Config.ClassicWeaponId.AutoSmg;

        /// <summary>
        /// The grasped element of each classic weapon's support hand, in its weapon frame (S-06 rows; the LOD0 section at the support
        /// station, measured by R2HeroOlcumTests.KavramaSatiri_LOD0Kesitiyle_TumSilahlar, ≤ 0.3 cm). A weapon without a row: no grasp.
        /// </summary>
        public static GraspElement ElementOf(DD.Prototype.Core.Config.ClassicWeaponId id)
        {
            // measured b2f14 (K/wp-083/evidence/t-b2f14-elem — seeded at the row; the radius is the section's surface along the contact direction d(θ0), the curl radius half its narrower side; section maps b2f7-sectionmap.txt): the handguard ring about the goal on
            // A / B / C / ar_2 / sg_2 (their goal is the ring's centre); sr_1's handguard tube and lmg_1's shroud above an off-mesh
            // goal (grasped from below); smg_2's magazine and lmg_2's / rl_1's vertical foregrips beside theirs (held from the left)
            switch (id)
            {
                case ClassicWeaponId.AssaultRifle: return new GraspElement(false, new Vector2(0.0000f, 0.0547f), 0.0359f, 0.0310f);
                case ClassicWeaponId.AutoSmg: return new GraspElement(false, new Vector2(-0.0001f, 0.0507f), 0.0385f, 0.0326f);
                case ClassicWeaponId.CombatShotgun: return new GraspElement(false, new Vector2(0.0003f, 0.0520f), 0.0403f, 0.0319f);
                case ClassicWeaponId.Ar2: return new GraspElement(false, new Vector2(-0.0016f, 0.0835f), 0.0335f, 0.0330f);
                case ClassicWeaponId.Sg2: return new GraspElement(false, new Vector2(0.0004f, 0.0498f), 0.0254f, 0.0254f);
                case ClassicWeaponId.Sr1: return new GraspElement(false, new Vector2(0.0000f, 0.0523f), 0.0284f, 0.0210f);
                case ClassicWeaponId.Lmg1: return new GraspElement(false, new Vector2(0.0000f, 0.0277f), 0.0193f, 0.0178f);
                case ClassicWeaponId.Smg2: return new GraspElement(true, new Vector2(0.0002f, 0.2345f), 0.0180f, 0.0178f);
                case ClassicWeaponId.Lmg2: return new GraspElement(true, new Vector2(0.0000f, 0.3707f), 0.0245f, 0.0245f);
                case ClassicWeaponId.Rl1: return new GraspElement(true, new Vector2(-0.0001f, 0.2045f), 0.0231f, 0.0229f);
                default: return default;
            }
        }

        /// <summary>The left hand's palm normal in its own frame (wrist, index and little knuckles, on the thumb's side), read once in the
        /// bind pose — the hand's anatomy, not a solve (the R2 hand measures use it; NaN-free only after the first grasp).</summary>
        public bool TryGetPalmFrame(out Vector3 palmNormalLocal, out Vector3 palmPointLocal)
        {
            palmNormalLocal = _palmNormalLocal;
            palmPointLocal = _palmLocal;
            return _geometry;
        }

        /// <summary>The contact target K of the last solve (world; the goal when the grasp did not run) — H1's support-hand target.</summary>
        public Vector3 LastContact { get; private set; }
        /// <summary>The wrist goal of the last solve (world; W = K − R · p after the wrist gate; the goal when the grasp did not run) — H1c's reach.</summary>
        public Vector3 LastWristGoal { get; private set; }
        /// <summary>The contact's wrap about the element on the last solve (°; θ).</summary>
        public float LastWrapDegrees => _theta;
        /// <summary>True when the last solve clamped the hand's turn at the wrist gate.</summary>
        public bool WristClamped { get; private set; }
        /// <summary>The wrist's largest share of its gate on the last solve (1 = on the limit; swing ellipse or twist).</summary>
        public float LastWristShare { get; private set; }
        /// <summary>The wrist's largest angle on the last solve — flexion / extension, deviation or twist from its rest (°; H1b report).</summary>
        public float LastWristDegrees { get; private set; }
        private float _wristDeg;
        private Vector3 _wristSplit;

        /// <summary>The wrist in the avatar's own humanoid muscle space (Left Forearm Twist In-Out, Left Hand Down-Up, Left Hand In-Out;
        /// −1…1 = the avatar's limits) of the wanted turn before the gate on the last solve (diagnostics).</summary>
        public Vector3 LastMusclesWanted { get; private set; }
        private HumanPoseHandler _poseHandler;
        private HumanPose _pose;
        private int _mTwist = -1, _mDownUp = -1, _mInOut = -1;

        // the three left-wrist muscles of the hand turned to r (the body as it stands; the hand put back)
        private Vector3 WristMuscles(Quaternion r)
        {
            if (_poseHandler == null || _mTwist < 0) return new Vector3(float.NaN, float.NaN, float.NaN);
            Quaternion keep = _hand.rotation;
            _hand.rotation = r;
            _poseHandler.GetHumanPose(ref _pose);
            _hand.rotation = keep;
            return new Vector3(_pose.muscles[_mTwist], _pose.muscles[_mDownUp], _pose.muscles[_mInOut]);
        }

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2f: the fingers' largest turn in the palm's plane from the forearm toward the wrap tangent (°; council 06.10 ulnar limit). The wrist gate itself is the avatar's humanoid muscle space (MuscleShare ≤ 1; b2f10).", true)]
        public const float WristUlnar = 30f;
        [PrototypeOnly(PendingDecisions.None, "WP-083 B2f: the contact's wrap about the element — handguard from below toward the left, vertical foregrip from the left toward the back (°); its search range and step when the wrist gate is exceeded; its largest change per second.", true)]
        public const float WrapHandguard = 25f, WrapVertical = 10f, WrapSearchMax = 90f, WrapSearchStep = 10f, WrapHysteresis = 5f, WrapSpeed = 180f, WrapSmoothSeconds = 0.15f, CurlClearance = 0.007f;

        private float _theta = float.NaN, _thetaVelocity, _thetaTarget = float.NaN;

        /// <summary>How many times the contact's wrap target moved since the grasp began (co-sign K1: 0 standing, ≤ 1 a walk cycle; tests).</summary>
        public int ThetaSwitchCount { get; private set; }
        private Vector3 _flexLocal;   // s = f × u in the hand's frame: + flexes the fingers toward the palm

        private void Grasp(Vector3 goal, float dt)
        {
            GraspElement e = _element;
            Vector3 axis = e.Vertical ? _weaponFrame.up : _weaponFrame.forward;
            // the station: the goal's projection on the element's axis through the section centre
            Vector3 centreLocal = e.Vertical ? new Vector3(e.Centre.x, 0f, e.Centre.y) : new Vector3(e.Centre.x, e.Centre.y, 0f);
            Vector3 goalLocal = _weaponFrame.InverseTransformPoint(goal);
            if (e.Vertical) centreLocal.y = goalLocal.y; else centreLocal.z = goalLocal.z;
            Vector3 c = _weaponFrame.TransformPoint(centreLocal);
            float theta0 = e.Vertical ? WrapVertical : WrapHandguard;
            if (float.IsNaN(_theta)) { _theta = _thetaTarget = theta0; _thetaVelocity = 0f; ThetaSwitchCount = 0; }

            // 1 · the first solve: the wrist to the contact (θ as last frame), the forearm it gives
            Quaternion animatedUpper = _upper.rotation, animatedHand = _hand.rotation;
            Vector3 k = Contact(c, axis, e, _theta, out Vector3 d);
            SolveNoRoll(_upper, _lower, _hand, Vector3.Lerp(_hand.position, k, _weight));
            // 2 · the hand's turn R at θ, the wrist goal; the gate picks θ (search) or clamps R
            float want = theta0;
            Quaternion r = HandTurn(axis, d, out float share);
            if (share > 1f)
            {
                float best = share;
                for (float t = 0f; t <= WrapSearchMax + 0.01f; t += WrapSearchStep)
                {
                    Contact(c, axis, e, t, out Vector3 dt2);
                    HandTurn(axis, dt2, out float s2);
                    if (s2 < best - 1e-3f) { best = s2; want = t; }
                    if (s2 <= 1f) { want = t; break; }
                }
            }

            // the wrap moves to a new target only past the hysteresis, eased (co-sign K1: SmoothDamp ~0.15 s, 180°/s the ceiling)
            if (Mathf.Abs(want - _thetaTarget) > WrapHysteresis || (want == theta0 && _thetaTarget != theta0))
            {
                _thetaTarget = want;
                ThetaSwitchCount++;
            }

            _theta = dt > 0f ? Mathf.SmoothDamp(_theta, _thetaTarget, ref _thetaVelocity, WrapSmoothSeconds, WrapSpeed, dt) : _theta;
            k = Contact(c, axis, e, _theta, out d);
            r = HandTurn(axis, d, out share);
            LastMusclesWanted = _wristSplit;
            if (share > 1f) { r = ClampToGate(r, animatedHand); WristClamped = true; }
            Vector3 w = k - r * _palmLocal;
            LastWristGoal = w;
            // 3 · the second solve: the wrist to W; R again with the new forearm
            SolveNoRoll(_upper, _lower, _hand, Vector3.Lerp(_hand.position, w, _weight));
            UpperArmRollDegrees = UpperArmRoll(animatedUpper, _upper, _lower);
            // R stays the one W was built from (the palm lands on K); the gate is read again on the new forearm for the report only
            share = MuscleShare(r);
            LastWristShare = share;
            LastWristDegrees = _wristDeg;
            _hand.rotation = Quaternion.Slerp(animatedHand, r, _weight);

            Vector3 palm = _hand.position + _hand.rotation * _palmLocal, n = _hand.rotation * _palmNormalLocal;
            LastContact = k;
            LastError = Vector3.Distance(palm, k);
            Vector3 foot = c + axis * Vector3.Dot(palm - c, axis);
            LastPalmToAxisDegrees = (foot - palm).sqrMagnitude > 1e-8f ? Vector3.Angle(n, foot - palm) : 0f;
            Curl(c, axis, e.CurlRadius + CurlClearance, _weight);
        }

        // K = c + r · d(θ): d the outward radial direction at the contact — on a handguard θ from straight below toward the left side
        // (−X), on a vertical foregrip from the left (−X) toward the back (−Z)
        private Vector3 Contact(Vector3 c, Vector3 axis, GraspElement e, float theta, out Vector3 d)
        {
            Vector3 start = e.Vertical ? -_weaponFrame.right : -_weaponFrame.up, toward = e.Vertical ? -_weaponFrame.forward : -_weaponFrame.right;
            float rad = theta * Mathf.Deg2Rad;
            d = (start * Mathf.Cos(rad) + toward * Mathf.Sin(rad)).normalized;
            d = (d - axis * Vector3.Dot(d, axis)).normalized;
            return c + d * e.Radius;
        }

        // R: the palm normal onto −d (whole); the fingers in the palm's plane, from the forearm's projection toward the wrap tangent
        // t = a × d by at most the ulnar limit (analytic). share: the wrist's largest share of its gate (> 1 outside)
        private Quaternion HandTurn(Vector3 axis, Vector3 d, out float share)
        {
            Vector3 nStar = -d, t = Vector3.Cross(axis, d).normalized;
            Vector3 forearm = _hand.position - _lower.position;
            Vector3 fp = forearm - nStar * Vector3.Dot(forearm, nStar);
            if (fp.sqrMagnitude < 1e-8f) fp = t;
            fp.Normalize();
            float toT = Vector3.SignedAngle(fp, t, nStar);
            Vector3 fStar = Quaternion.AngleAxis(Mathf.Clamp(toT, -WristUlnar, WristUlnar), nStar) * fp;
            Quaternion r = Quaternion.LookRotation(fStar, nStar) * Quaternion.Inverse(Quaternion.LookRotation(_fingerLocalDir, _palmNormalLocal));
            share = MuscleShare(r);
            return r;
        }

        // the wrist against its gate in the avatar's own humanoid muscle space (Left Forearm Twist In-Out, Left Hand Down-Up, Left
        // Hand In-Out: −1…1 between the avatar's limits, 0 its anatomical neutral) — b2f9 / b2f10 (06.10): the bind pose is not this
        // rig's neutral (the clip's AD-approved grasps read 50° / 50° / 115° against it), so a table of angles against the bind clamped
        // every frame; against the muscles 9 of 10 wanted turns sit inside. share = the largest |muscle| (> 1 outside); the angle (°)
        // through the avatar's default muscle ranges
        private float MuscleShare(Quaternion r)
        {
            Vector3 m = WristMuscles(r);
            if (float.IsNaN(m.x)) { _wristDeg = 0f; return 0f; }
            _wristDeg = Mathf.Max(MuscleDegrees(_mTwist, m.x), Mathf.Max(MuscleDegrees(_mDownUp, m.y), MuscleDegrees(_mInOut, m.z)));
            _wristSplit = m;
            return Mathf.Max(Mathf.Abs(m.x), Mathf.Max(Mathf.Abs(m.y), Mathf.Abs(m.z)));
        }

        private static float MuscleDegrees(int muscle, float value) =>
            Mathf.Abs(value) * (value >= 0f ? HumanTrait.GetMuscleDefaultMax(muscle) : -HumanTrait.GetMuscleDefaultMin(muscle));

        // R pulled toward the clip's hand until it sits on the gate (bisected; the clip's pose is inside the avatar's muscle space by
        // construction): contact first — the wrist goal follows
        private Quaternion ClampToGate(Quaternion r, Quaternion clip)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 8; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (MuscleShare(Quaternion.Slerp(clip, r, mid)) <= 1f) lo = mid; else hi = mid;
            }

            return Quaternion.Slerp(clip, r, lo);
        }

        // the palm normal from the hand's geometry (wrist, index and little knuckles), on the thumb's side (palmar)
        private bool PalmNormal(out Vector3 n)
        {
            n = Vector3.zero;
            Transform ip = _fingers[0], lp = _fingers[9];
            if (ip == null || lp == null || _thumb == null) return false;
            n = Vector3.Cross(ip.position - _hand.position, lp.position - _hand.position);
            if (n.sqrMagnitude < 1e-10f) return false;
            n.Normalize();
            if (Vector3.Dot(_thumb.position - _hand.position, n) < 0f) n = -n;
            return true;
        }

        // the hand's own frame, read once (humanoid bones only rotate: the knuckles and the thumb's base are fixed in it): u the palm
        // normal, f the wrist → middle knuckle direction (⊥ u), p the palm point (55 % along it, 1.2 cm out of the palm), s = f × u
        private bool HandGeometry()
        {
            if (_geometry) return true;
            if (_middleProximal == null || !PalmNormal(out Vector3 n)) return false;
            Quaternion inv = Quaternion.Inverse(_hand.rotation);
            _palmNormalLocal = inv * n;
            Vector3 f = inv * (_middleProximal.position - _hand.position);
            _fingerLocalDir = (f - _palmNormalLocal * Vector3.Dot(f, _palmNormalLocal)).normalized;
            _palmLocal = inv * (Vector3.Lerp(_hand.position, _middleProximal.position, 0.55f) - _hand.position) + _palmNormalLocal * 0.012f;
            _flexLocal = Vector3.Cross(_fingerLocalDir, _palmNormalLocal).normalized;
            _geometry = _fingerLocalDir.sqrMagnitude > 0.5f;
            return _geometry;
        }

        // the index → little fingers curl about the hand's own s axis until their tips reach the element (a cylinder r + clearance)
        private void Curl(Vector3 c, Vector3 axis, float radius, float weight)
        {
            for (int i = 0; i < 12; i++)
            {
                Transform t = _fingers[i];
                if (t == null) return;
                _fingerLocal[i] = t.localRotation;
            }

            Vector3 s = _hand.rotation * _flexLocal;
            for (int f = 0; f < 4; f++)
            {
                int b = f * 3;
                for (int j = 0; j < 3; j++) _fingerAxis[b + j] = Quaternion.Inverse(_fingers[b + j].rotation) * s;
                float lo = 0f, hi = 1f;
                if (TipToAxis(b, 0f, c, axis) <= radius) hi = 0f;
                else if (TipToAxis(b, 1f, c, axis) > radius) lo = 1f;
                else
                    for (int k = 0; k < 8; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (TipToAxis(b, mid, c, axis) > radius) lo = mid; else hi = mid;
                    }

                float cu = (lo >= 1f ? 1f : hi) * weight;
                CurlFinger(b, cu);
                if (f == 0) LastCurl = cu;
            }
        }

        private void CurlFinger(int b, float c)
        {
            _fingers[b].localRotation = _fingerLocal[b] * Quaternion.AngleAxis(c * CurlProximal, _fingerAxis[b]);
            _fingers[b + 1].localRotation = _fingerLocal[b + 1] * Quaternion.AngleAxis(c * CurlIntermediate, _fingerAxis[b + 1]);
            _fingers[b + 2].localRotation = _fingerLocal[b + 2] * Quaternion.AngleAxis(c * CurlDistal, _fingerAxis[b + 2]);
        }

        // the finger's tip (the distal bone carried one more distal length) against the element's axis, at curl c
        private float TipToAxis(int b, float c, Vector3 centre, Vector3 axis)
        {
            CurlFinger(b, c);
            Vector3 d = _fingers[b + 2].position, tip = d + (d - _fingers[b + 1].position);
            Vector3 foot = centre + axis * Vector3.Dot(tip - centre, axis);
            return Vector3.Distance(tip, foot);
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
        public static void ResetStatics()
        {
            Enabled = true;
            GraspEnabled = true;
        }
    }
}
