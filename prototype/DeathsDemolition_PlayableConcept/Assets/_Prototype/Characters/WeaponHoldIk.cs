using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-083 B2c (C-00 05.10, R2 N2-05) · the weapon-first third-person hold. The weapon's grip point is a pivot that rides the upper
    /// chest and faces where the body faces, pitched by the aim (WeaponMountAlignment's rest frame). The right hand comes to the weapon: a two-bone solve puts the hand where its
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
    /// On by default (C-00 05.10, the user's N2-05 "weapons on the shoulder"; <see cref="Enabled"/> for tests / A-B), third-person
    /// views only — in first person this body is not seen. The hold offsets are starting values to be tuned with Hero v2 (AP-024 /
    /// AP-026). Zero allocation per frame.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(890)]
    [DisallowMultipleComponent]
    public sealed class WeaponHoldIk : MonoBehaviour
    {
        [PrototypeOnly(PendingDecisions.None, "WP-083 B2c/B2d: the weapon-first hold — ON (C-00 05.10: the structural fix of the user's N2-05 \"weapons on the shoulder\"); tests / A-B switch it off.", true)]
        public static bool Enabled = true;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2c: hold weight blend time (s). Invented.", true)]
        public const float BlendSeconds = 0.12f;

        /// <summary>
        /// The hip hold: the grip point (the right hand's socket_weapon_r) relative to the upper chest, in the body's yaw frame
        /// (x right, y up, z forward; m). Starting value (tests / A-B may set it); per-class values come with Hero v2 and the AD.
        /// </summary>
        public static Vector3 HipHold = DefaultHipHold;

        // chest-high ready hold: of the measured candidates the one with the most reach margin for the support hand (left shoulder →
        // a handguard 18 cm ahead of the grip 37.5 cm of a 54.5 cm arm on the current player; K/wp-083/evidence b2c14)
        public static readonly Vector3 DefaultHipHold = new Vector3(0.10f, -0.12f, 0.22f);

        /// <summary>
        /// The per-weapon part of the hold (WP-083 B2 "silah başına tutamak / destek tablosu"): the support point's forward reach
        /// ahead of the grip (m) the hip hold is sized for. A weapon whose support point (WeaponGripIk's goal) lies farther ahead is
        /// pulled back along the aim by the difference — a long gun goes to the shoulder — so the support hand stays at the same
        /// reach on every weapon. The pull is capped at <see cref="MaxPullBack"/> and eased over <see cref="PullBackSeconds"/>
        /// (a weapon switch).
        /// </summary>
        public static readonly float ReferenceSupportForward = 0.12f;

        /// <summary>
        /// Where a fully pulled-back (long) weapon's grip moves, in the body's yaw frame (right, up; m) — the stock goes to the right
        /// shoulder pocket instead of through the sternum. Scaled by the pull-back share.
        /// </summary>
        public static readonly Vector3 ShoulderShift = new Vector3(0.06f, 0.08f, 0f);

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2: the hold's largest pull-back along the aim (m) and its easing time (s). Invented.", true)]
        public const float MaxPullBack = 0.35f, PullBackSeconds = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2: how far the stock (attach_stock) stays from the right shoulder joint when the hold pulls a long weapon back (m) — the joint plus the shoulder's flesh. Invented.", true)]
        public static float StockClearance = DefaultStockClearance;
        public const float DefaultStockClearance = 0.11f;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2: how far the stock (attach_stock) stays from the upper chest bone when the hold pulls a long weapon back (m) — the chest's flesh, breathing included. Measured.", false)]
        public static float ChestClearance = DefaultChestClearance;
        public const float DefaultChestClearance = 0.17f;

        /// <summary>
        /// WP-083 B2e (C-00 05.10: rl_1 on the shoulder — the tube on the right shoulder beside the head, the sight at the eye, not
        /// over the head): the shouldered row. The tube's axis (attach_stock → Muzzle) passes <see cref="ShoulderRest"/> from the right
        /// shoulder joint (body yaw frame: x out, y up; the shoulder's flesh plus the tube's radius) and the grip's foot on that axis
        /// lies <see cref="ShoulderGripAhead"/> ahead of it; no pull-back, no stock clearance (the tube rests on the shoulder).
        /// </summary>
        [PrototypeOnly(PendingDecisions.None, "WP-083 B2e: the shouldered tube's axis from the right shoulder joint (x out, y up; m) and the grip's distance ahead of it along the axis (m). Measured (b2k sweep).", true)]
        public static Vector3 ShoulderRest = DefaultShoulderRest;
        public static float ShoulderGripAhead = DefaultShoulderGripAhead;
        // b2n (05.10): tube ↔ head 3.5 cm, tube ↔ shoulder 0.6 cm, sight 6.4 cm under the eye, both hands 0 (K/wp-083/evidence b2n-r2.log)
        public static readonly Vector3 DefaultShoulderRest = new Vector3(0.005f, 0.05f, 0f);
        public const float DefaultShoulderGripAhead = 0.15f;

        [PrototypeOnly(PendingDecisions.None, "WP-083 B2: the hold's largest pitch with the aim (°), up or down. Invented.", true)]
        public const float MaxPitchDegrees = 40f;

        [PrototypeOnly(PendingDecisions.None, "WP-087 B: the stock's distance past StockClearance over which a pulled-back weapon's kick centre moves from the grip to the stock (m) — a stock in the shoulder pocket lifts the muzzle about itself. Council 06.10 (character-animator), not measured.", true)]
        public const float StockShoulderBlend = 0.05f;


        private Animator _animator;
        private Transform _socket, _chest, _upper, _lower, _hand, _stock, _muzzle;
        private float _stockSearch;
        private bool _hadStock;
        private DD.Prototype.PlayerSystem.WeaponMountAlignment _mount;
        private float _mountSearch;

        // where the stock would sit for a pull (the pivot formula below): clear of the shoulder joint and of the upper chest
        private bool StockClear(Vector3 shoulder, Quaternion yaw, Vector3 aim, Vector3 stockOffset, float pull)
        {
            Vector3 pivot = _chest.position + yaw * (HipHold + _weaponOffset + ShoulderShift * (pull / MaxPullBack)) - aim * pull;
            Vector3 stock = pivot + stockOffset;
            return Vector3.Distance(stock, shoulder) >= StockClearance && Vector3.Distance(stock, _chest.position) >= ChestClearance;
        }

        // WP-087 B: the arms' share of the held weapon's kick (KickSource), after the row's pivot and the clip's HoldDelta. The muzzle
        // rises about a centre fixed on the shot's first frame, in the pivot's frame (it rides the hold, not the walk's stock blend —
        // a centre re-picked every frame shook the pivot by ±1 cm): the grip at the hip; on the shouldered row the tube's shoulder
        // contact, with no push (the shoulder takes it); on a row pulled to the shoulder the centre moves toward the stock and the
        // push goes to the shoulder by the same share. A kick that would bring the stock nearer the shoulder or chest than the hold
        // without it (and inside the clearances) is scaled down, bisected — never a worse hold than no kick.
        private void ApplyKick(ref Vector3 pivot, ref Quaternion pivotRot, Transform stock, Vector3? support, Quaternion yaw)
        {
            LastKickOffset = Vector3.zero;
            LastKickPivotMove = Vector3.zero;
            LastKickDegrees = 0f;
            KickScale = 1f;
            KickPushShare = 1f;
            IHeldWeaponKick src = KickSource;
            // S-02 review MUST (06.10): the source alone decides — ArmsCarryKick is written at 895, after this solve (890), so reading
            // it here gives last frame's share and drops the kick on the hold's first / last frame; C's controller nulls the source
            // whenever the arms do not carry (a Special, the hold off, FP)
            bool carried = src != null;
            Vector3 push = carried ? src.KickLocalOffset : Vector3.zero;
            Quaternion rise = carried ? src.KickLocalRotation : Quaternion.identity;
            if (push.sqrMagnitude < 1e-10f && Quaternion.Angle(Quaternion.identity, rise) < 0.01f)
            {
                _kicking = false;
                LastSupportShortfall = Shortfall(pivot, pivotRot, support);
                return;
            }

            bool hasStock = stock != null && !_shouldered;
            Vector3 stockInSocket = hasStock ? Quaternion.Inverse(_socket.rotation) * (stock.position - _socket.position) : Vector3.zero;
            if (!_kicking)
            {
                Vector3 centre = pivot;
                float share = 0f;
                if (_shouldered) { centre = _upper.position + yaw * ShoulderRest; share = 1f; }
                else if (hasStock)
                {
                    Vector3 stockWorld = pivot + pivotRot * stockInSocket;
                    share = 1f - Mathf.Clamp01((Vector3.Distance(stockWorld, _upper.position) - StockClearance) / StockShoulderBlend);
                    centre = Vector3.Lerp(pivot, stockWorld, share);
                }

                _kickCentreInPivot = Quaternion.Inverse(pivotRot) * (centre - pivot);
                _kickShoulderShare = share;
                _kicking = true;
            }

            Vector3 c = pivot + pivotRot * _kickCentreInPivot;
            push *= 1f - _kickShoulderShare;
            float s = 1f;
            if (hasStock)
            {
                Vector3 rest = pivot + pivotRot * stockInSocket;
                float shoulderMin = Mathf.Min(StockClearance, Vector3.Distance(rest, _upper.position)), chestMin = Mathf.Min(ChestClearance, Vector3.Distance(rest, _chest.position));
                if (!KickStockClear(pivot, pivotRot, c, push, rise, stockInSocket, 1f, shoulderMin, chestMin))
                {
                    float lo = 0f, hi = 1f;
                    for (int i = 0; i < 8; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (KickStockClear(pivot, pivotRot, c, push, rise, stockInSocket, mid, shoulderMin, chestMin)) lo = mid; else hi = mid;
                    }

                    s = lo;
                }
            }

            KickPose(pivot, pivotRot, c, push, rise, s, out Vector3 kicked, out Quaternion kickedRot);
            KickScale = s;
            KickPushShare = (1f - _kickShoulderShare) * s;
            LastKickOffset = push * s;
            LastKickPivotMove = Quaternion.Inverse(pivotRot) * (kicked - pivot);
            LastKickDegrees = -Mathf.DeltaAngle(0f, (Quaternion.Inverse(pivotRot) * kickedRot).eulerAngles.x);
            pivot = kicked;
            pivotRot = kickedRot;
            LastSupportShortfall = Shortfall(pivot, pivotRot, support);
        }

        private static void KickPose(Vector3 pivot, Quaternion pivotRot, Vector3 centre, Vector3 push, Quaternion rise, float s, out Vector3 kicked, out Quaternion kickedRot)
        {
            Quaternion r = Quaternion.Slerp(Quaternion.identity, rise, s);
            kicked = centre + pivotRot * r * Quaternion.Inverse(pivotRot) * (pivot - centre) + pivotRot * (push * s);
            kickedRot = pivotRot * r;
        }

        private bool KickStockClear(Vector3 pivot, Quaternion pivotRot, Vector3 centre, Vector3 push, Quaternion rise, Vector3 stockInSocket, float s, float shoulderMin, float chestMin)
        {
            KickPose(pivot, pivotRot, centre, push, rise, s, out Vector3 p, out Quaternion r);
            Vector3 stock = p + r * stockInSocket;
            return Vector3.Distance(stock, _upper.position) >= shoulderMin - 1e-4f && Vector3.Distance(stock, _chest.position) >= chestMin - 1e-4f;
        }

        // the support point (WeaponGripIk's goal, read before the solve in the socket frame the weapon rides) against the left arm
        private float Shortfall(Vector3 pivot, Quaternion pivotRot, Vector3? support)
        {
            if (!support.HasValue || _lUpper == null || _leftArmLength <= 0f) return float.NaN;
            Vector3 inSocket = Quaternion.Inverse(_socket.rotation) * (support.Value - _socket.position);
            return Vector3.Distance(pivot + pivotRot * inSocket, _lUpper.position) - _leftArmLength;
        }

        // the held weapon's stock marker (attach_stock, AP-021 / ADR-042-1) under the socket; searched again while missing. Only an
        // active marker counts: a hidden weapon under the same socket (the classic rig while a Special is out) is never the held one
        private Transform FindStock(float dt)
        {
            if (_stock != null && _stock.gameObject.activeInHierarchy && _stock.IsChildOf(_socket)) return _stock;
            // the held marker went away (a weapon switch): look again on this frame, then every 0.25 s while none is found (gt 06.10
            // R-1: a shouldered tube must not spend a search interval on the hip row)
            if (_hadStock) { _hadStock = false; _stockSearch = 0f; }
            _stockSearch -= dt;
            if (_stockSearch > 0f) return null;
            _stockSearch = 0.25f;
            _stock = FindActive(_socket, "attach_stock");
            _muzzle = _stock != null ? FindActive(_socket, "Muzzle") : null;
            _hadStock = _stock != null;
            return _stock;
        }

        private static Transform FindActive(Transform t, string name)
        {
            if (!t.gameObject.activeInHierarchy) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindActive(t.GetChild(i), name);
                if (r != null) return r;
            }

            return null;
        }

        // the held weapon's mount (its alignment keeps the clip's rest reference); searched again while missing
        private DD.Prototype.PlayerSystem.WeaponMountAlignment FindMount(float dt)
        {
            if (_mount != null && _mount.transform.parent == _socket) return _mount;
            _mountSearch -= dt;
            if (_mountSearch > 0f) return null;
            _mountSearch = 0.25f;
            _mount = _socket.GetComponentInChildren<DD.Prototype.PlayerSystem.WeaponMountAlignment>();
            return _mount;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }

            return null;
        }
        private Func<Vector3> _aimDirection;
        private Func<bool> _gate;
        private Func<Vector3?> _support;
        private Vector3 _weaponOffset;

        /// <summary>
        /// WP-083 B2 (C-00 05.10): the per-weapon hold data — an offset added to the hip hold in the body's yaw frame (m), for the
        /// weapons whose measure needs it (all others 0). CombatShotgun (sg_1): +6 cm out, so its stock end clears the right armpit
        /// (WeaponModelSliceTests KundakOmuz ≤ 2 cm: 2.5 → 1.5 cm after a shot, K/wp-083/evidence stk5).
        /// </summary>
        public static Vector3 WeaponOffset(ClassicWeaponId id) => id == ClassicWeaponId.CombatShotgun ? new Vector3(0.06f, 0f, 0f) : Vector3.zero;

        /// <summary>
        /// WP-083 B2e (C-00 05.10): the weapons held on the shoulder rather than at the hip. The rocket launcher (rl_1): at the hip its
        /// tube's stock end stays 8–9 cm inside the body even at no pull-back and the support hand falls 6.4 cm short; its own clip
        /// carries the tube over the head (K/wp-083/evidence b2h, v9 frames). It rests on the right shoulder (<see cref="ShoulderRest"/>).
        /// </summary>
        public static bool Shouldered(ClassicWeaponId id) => id == ClassicWeaponId.Rl1;

        /// <summary>The held classic weapon (PlayerSkinnedVisual.EquipWeapon) — its row of the per-weapon hold data.</summary>
        public void SetWeapon(ClassicWeaponId id)
        {
            _weaponOffset = WeaponOffset(id);
            _shouldered = Shouldered(id);
            // the new weapon's markers are searched on the next frame, not after the last search's 0.25 s wait (gt 06.10 R-1: rl_1's
            // tube could start one search late at the hip)
            _stock = null;
            _muzzle = null;
            _hadStock = false;
            _stockSearch = 0f;
        }

        private bool _shouldered;
        private Transform _facing;   // the PlayerRig root (WeaponMountAlignment's facing), else the animator
        private float _pullBack;
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
        /// <summary>The right upper arm's roll about its own axis against the clip's pose after the last solve (°; tests, ≤ 15).</summary>
        public float UpperArmRollDegrees { get; private set; }
        /// <summary>The current pull-back along the aim for the held weapon's support reach (m; tests).</summary>
        public float PullBack => _pullBack;
        /// <summary>socket_weapon_r ↔ pivot after the last solve at full weight (m; NaN otherwise; tests / H1).</summary>
        public float LastError { get; private set; } = float.NaN;

        /// <summary>
        /// WP-087 B (C-00 06.10, council K/wp-083/KARAR-HELDKICK-API-06.10.KONSOLIDE.md): the held weapon's kick the arms carry at the
        /// pivot. S-02's rig publishes it; WeaponController hands over the active weapon's source (never searched in the hierarchy,
        /// both rigs sit under one mount). Null, or a zero share: no kick on the hold — the rig's body keeps all of it.
        /// </summary>
        public IHeldWeaponKick KickSource { get; set; }
        /// <summary>
        /// The push applied on the last solve, in the pivot's frame (m): the source's KickLocalOffset × <see cref="KickPushShare"/>
        /// (tests, WP-087's "the kick reached the hold" gate and its conservation check against the rig's arms channel).
        /// </summary>
        public Vector3 LastKickOffset { get; private set; }
        /// <summary>The share of the source's push the arms applied on the last solve: 1 at the hip, 0 on the shouldered row (the
        /// shoulder takes it), (1 − stock-on-shoulder share) × <see cref="KickScale"/> on a row pulled to the shoulder.</summary>
        public float KickPushShare { get; private set; } = 1f;
        /// <summary>The pivot's whole move from the kick on the last solve, in the pivot's frame before it (m; the rise about a centre off the grip moves it too).</summary>
        public Vector3 LastKickPivotMove { get; private set; }
        /// <summary>The muzzle rise applied on the last solve about the pivot's +X, signed (°; + muzzle up, − down).</summary>
        public float LastKickDegrees { get; private set; }
        /// <summary>The share of the published kick kept after the stock guard on the last solve (1 = all; the WP-087 feel gate reports it).</summary>
        public float KickScale { get; private set; } = 1f;
        /// <summary>The support point after the kick against the left arm's reach (m; &gt; 0 = beyond it; NaN without a support point).</summary>
        public float LastSupportShortfall { get; private set; } = float.NaN;
        private bool _kicking;
        private Vector3 _kickCentreInPivot;
        private float _kickShoulderShare;
        private Transform _lUpper;
        private float _leftArmLength;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Enabled = true;
            HipHold = DefaultHipHold;
            StockClearance = DefaultStockClearance;
            ChestClearance = DefaultChestClearance;
            ShoulderRest = DefaultShoulderRest;
            ShoulderGripAhead = DefaultShoulderGripAhead;
        }

        /// <summary>
        /// Owner binding (PlayerSkinnedVisual.Attach): the animator, the right hand's weapon socket, the aim and the gate (rest pose,
        /// alive). The socket's pose in the hand is read once (a child of the hand: pose-independent).
        /// </summary>
        public void Configure(Animator animator, Transform weaponSocket, Func<Vector3> aimDirection, Func<bool> gate, Func<Vector3?> supportWorld = null)
        {
            _support = supportWorld;
            _pullBack = 0f;
            DD.Prototype.PlayerSystem.PlayerRig rig = animator != null ? animator.GetComponentInParent<DD.Prototype.PlayerSystem.PlayerRig>() : null;
            _facing = rig != null ? rig.transform : animator != null ? animator.transform : null;
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
            _lUpper = HumanoidBones.Bone(animator, HumanBodyBones.LeftUpperArm);
            Transform lLower = HumanoidBones.Bone(animator, HumanBodyBones.LeftLowerArm), lHand = HumanoidBones.Bone(animator, HumanBodyBones.LeftHand);
            _leftArmLength = _lUpper != null && lLower != null && lHand != null
                ? Vector3.Distance(_lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position) : 0f;
            _kicking = false;
            if (!_ready) return;
            _socketInHand = Quaternion.Inverse(_hand.rotation) * _socket.rotation;
            _socketOffset = Quaternion.Inverse(_hand.rotation) * (_socket.position - _hand.position);
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            // third-person only: in first person this body is not seen (the FP arms / view weapon have their own path)
            bool on = Enabled && (_gate == null || _gate()) && DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson;
            _weight = Mathf.MoveTowards(_weight, on ? 1f : 0f, dt / BlendSeconds);
            if (_weight <= 0f) { LastError = float.NaN; _kicking = false; return; }

            Transform body = _animator.transform;
            // the weapon frame: the player's facing (the PlayerRig root's yaw, WeaponMountAlignment's rest frame — the body turns to the
            // aim; the visual may lag it) pitched by the
            // aim's elevation (the torso's aim layer pitches the shoulders the same way)
            Vector3 facing = Vector3.ProjectOnPlane((_facing != null ? _facing : body).forward, Vector3.up);
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            Quaternion yaw = Quaternion.LookRotation(facing.normalized, Vector3.up);
            Vector3 aimDir = _aimDirection != null ? _aimDirection() : Vector3.zero;
            float pitch = aimDir.sqrMagnitude > 1e-6f ? Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aimDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -MaxPitchDegrees, MaxPitchDegrees) : 0f;
            Vector3 aim = yaw * Quaternion.Euler(-pitch, 0f, 0f) * Vector3.forward;

            // the per-weapon pull-back: the support point's reach ahead of the grip, read in the socket frame (the weapon rides it)
            Vector3? support = _support != null ? _support() : null;
            float wantPull = 0f;
            if (support.HasValue && !_shouldered) wantPull = Mathf.Clamp((Quaternion.Inverse(_socket.rotation) * (support.Value - _socket.position)).z - ReferenceSupportForward, 0f, MaxPullBack);
            // ... but never so far that the stock goes into the shoulder: the weapon's attach_stock stays StockClearance from the right
            // shoulder joint (WeaponModelSliceTests KundakOmuz: ≤ 2 cm into the body). A stock that passes beside the shoulder keeps the
            // full pull; one that would reach it is pulled less (the largest pull that keeps the clearance, bisected)
            Transform stock = FindStock(dt);
            if (stock != null && wantPull > 0f)
            {
                Vector3 stockInSocket = Quaternion.Inverse(_socket.rotation) * (stock.position - _socket.position);
                Vector3 stockOffset = Quaternion.LookRotation(aim, Vector3.up) * stockInSocket;
                Vector3 shoulder = _upper.position;
                if (!StockClear(shoulder, yaw, aim, stockOffset, wantPull))
                {
                    float lo = 0f, hi = wantPull;
                    for (int i = 0; i < 10; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (StockClear(shoulder, yaw, aim, stockOffset, mid)) lo = mid; else hi = mid;
                    }

                    wantPull = lo;
                }
            }

            _pullBack = Mathf.MoveTowards(_pullBack, wantPull, dt * MaxPullBack / PullBackSeconds);

            // the grip pivot and the hand pose that puts socket_weapon_r on it
            Vector3 pivot = _chest.position + yaw * (HipHold + _weaponOffset + ShoulderShift * (_pullBack / MaxPullBack)) - aim * _pullBack;
            Quaternion pivotRot = Quaternion.LookRotation(aim, Vector3.up);
            // the shouldered row: the tube's axis (stock → muzzle, in the socket frame — the socket takes the pivot's frame) passes
            // ShoulderRest from the right shoulder joint, the grip's foot on it ShoulderGripAhead ahead
            if (_shouldered && stock != null && _muzzle != null)
            {
                Quaternion toSocket = Quaternion.Inverse(_socket.rotation);
                Vector3 s0 = toSocket * (stock.position - _socket.position), m0 = toSocket * (_muzzle.position - _socket.position);
                Vector3 d = m0 - s0;
                if (d.sqrMagnitude > 1e-6f)
                {
                    d.Normalize();
                    Vector3 foot = s0 + d * Vector3.Dot(-s0, d);   // the grip's foot on the axis, socket frame
                    Vector3 rest = _upper.position + yaw * ShoulderRest;
                    pivot = rest + pivotRot * (d * ShoulderGripAhead - foot);
                }
            }

            // the clip's own deviation from its rest pose (the fire clip's kick, the hurt flinch) rides on the hold frame: the mount's
            // alignment measures it on the animated socket (before this solve overwrites it), in the character's frame
            DD.Prototype.PlayerSystem.WeaponMountAlignment mount = FindMount(dt);
            if (mount != null)
            {
                Quaternion character = _facing != null ? _facing.rotation : body.rotation;
                Quaternion kick = mount.HoldDelta(_hand.rotation * _socketInHand, pitch);
                pivotRot = character * kick * Quaternion.Inverse(character) * pivotRot;
            }

            ApplyKick(ref pivot, ref pivotRot, stock, support, yaw);

            Quaternion handRot = pivotRot * Quaternion.Inverse(_socketInHand);
            Vector3 handPos = pivot - handRot * _socketOffset;
            PivotPosition = pivot;
            PivotRotation = pivotRot;

            Quaternion animatedHand = _hand.rotation, animatedUpper = _upper.rotation;
            // the clip's own elbow plane (no pole): a pole turned the upper arm about its axis — out and back, the shoulder cloth behind
            // the body (C-00 05.10 v4 frames)
            WeaponGripIk.SolveNoRoll(_upper, _lower, _hand, Vector3.Lerp(_hand.position, handPos, _weight));
            _hand.rotation = Quaternion.Slerp(animatedHand, handRot, _weight);
            UpperArmRollDegrees = WeaponGripIk.UpperArmRoll(animatedUpper, _upper, _lower);
            LastError = _weight >= 1f ? Vector3.Distance(_socket.position, pivot) : float.NaN;
        }
    }
}
