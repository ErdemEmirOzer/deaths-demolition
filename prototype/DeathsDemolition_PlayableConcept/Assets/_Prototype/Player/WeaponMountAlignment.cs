using DD.Prototype.Characters;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// WP-059 · the weapon frame on the live rig. AP-009 authors every aim / handling clip with
    /// <c>socket_weapon_r</c> pointing +Z forward and level (P-06 solves the hand rotation for it in
    /// Blender), but the socket is a plain child of RightHand and Humanoid retargeting carries only
    /// two of the hand's three rotations (the roll is folded into the forearm, "Right Hand In-Out"
    /// reads 0 in every pose) — so in Unity the socket ends up 45–55° off in the A / B aims (the
    /// rifle stood on end behind an open hand; measured in WeaponMountDiagnosticTests, the clip
    /// itself re-imported into Blender is correct). The socket's POSITION is right (mid-palm, 5 cm
    /// from the wrist); only its rotation is not reproducible through the humanoid pipeline.
    ///
    /// This component sits on the weapon mount (a child of the socket) and, after the Animator and
    /// the WP-032 LateUpdate writers, sets the mount's world rotation to the authored intent: the
    /// character's facing, level, up = world up — TIMES the socket's animated deviation from its
    /// rest aim pose (WP-059C): the reference is the socket's rotation in character space captured
    /// while the override layer rests in Aim_&lt;w&gt; / SwHold_&lt;n&gt; and the additive layer is
    /// empty; every frame the mount gets base × (socket now × reference⁻¹), so the fire clip's kick
    /// (muzzle rise), the reload's hand travel and the hurt clips still move the weapon, while the
    /// humanoid-lost roll of the rest pose stays corrected. Without a reference yet (the first
    /// frames) the base alone is used. The models are authored in that frame (ADR-042-1), so
    /// muzzle, ejection and grip markers land where the contract says. Off while dead (the collapse
    /// keeps the weapon in the falling hand) and switchable for tests / A-B.
    /// </summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class WeaponMountAlignment : MonoBehaviour
    {
        /// <summary>Global switch (tests / A-B): false leaves the mount to the socket bone.</summary>
        public static bool Enabled = true;

        private PlayerRig _rig;
        private PlayerHealth _health;
        private PlayerSkinnedVisual _skinned;
        private int _overrideLayer = -1;
        private int _additiveLayer = -1;
        // Reference socket rotation (character space) per rest pose: index 0..2 = Aim_<weapon>, 3..4 = SwHold_1/2.
        private readonly Quaternion[] _reference = new Quaternion[6];
        private readonly bool[] _hasReference = new bool[6];

        // WP-069 · the v3 aim pose (S-06 B4b 6ee44282: CharacterAnimatorContract.WeaponPoseV3 = 4 / AimV3State = "Aim_V3"),
        // mirrored here until B4b lands on main — then these read the contract's names.
        private const int WeaponPoseV3 = 4;
        private const string AimV3State = "Aim_V3";

        /// <summary>WP-069 (C-00 grip decision B): the hand's mid-grip socket in the weapon frame (the held weapon's definition, set by WeaponController).</summary>
        public Vector3 GripOffset { get; set; } = DD.Prototype.Core.Weapons.WeaponGrip.DefaultOffset;

        /// <summary>True on the frames the mount was written (tests).</summary>
        public bool AlignedThisFrame { get; private set; }

        /// <summary>True when the last write included the socket's deviation from a captured rest pose (tests).</summary>
        public bool DeltaApplied { get; private set; }

        /// <summary>The last frame's deviation from the rest pose in degrees (0 at rest; the kick / reload / hurt travel). Tests / F1.</summary>
        public float DeltaDegrees { get; private set; }

        /// <summary>
        /// WP-069 B5 (C-00 28.09 / 30.09, release TP frame: the rifle stood on end in the Player only) · <c>-ddMountDiag</c>: the
        /// mount logs its binding and, every 60 frames for 10 s, its pose / rest / deviation. Development builds and the Editor
        /// only (H12); off without the flag, false in a release build.
        /// </summary>
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public static bool Diagnostics => s_diag ?? (s_diag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ddMountDiag") >= 0).Value;

        private static bool? s_diag;
        private int _diagFrames;
        private readonly int[] _refFrame = new int[6];
        private readonly string[] _refClip = new string[6];
#else
        public static bool Diagnostics => false;
#endif

        /// <summary>Attach to <paramref name="mount"/> for <paramref name="rig"/> (idempotent).</summary>
        public static WeaponMountAlignment Attach(Transform mount, PlayerRig rig)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Diagnostics) Debug.Log("[DD-MOUNT] attach mount=" + (mount != null ? mount.name : "null") + " parent=" + (mount != null && mount.parent != null ? mount.parent.name : "null") +
                                       " rig=" + (rig != null) + " skinned=" + (rig != null && rig.Animation != null && rig.Animation.Skinned != null));
#endif
            if (mount == null || rig == null)
            {
                return null;
            }

            WeaponMountAlignment a = mount.GetComponent<WeaponMountAlignment>();
            if (a == null) a = mount.gameObject.AddComponent<WeaponMountAlignment>();
            a._rig = rig;
            a._health = rig.Health;
            a._skinned = rig.Animation != null ? rig.Animation.Skinned : null;
            a._overrideLayer = -1;
            a._additiveLayer = -1;
            for (int i = 0; i < a._hasReference.Length; i++) a._hasReference[i] = false;
            return a;
        }

        /// <summary>Tests: forget the captured rest poses (re-captured on the next resting frame).</summary>
        public void ResetReferences()
        {
            for (int i = 0; i < _hasReference.Length; i++) _hasReference[i] = false;
        }

        private int PoseIndex()
        {
            if (_skinned == null) return -1;
            if (_skinned.SpecialHold > 0) return 2 + Mathf.Clamp(_skinned.SpecialHold, 1, 2);
            if (_skinned.Weapon == WeaponPoseV3) return 5;
            return _skinned.Weapon <= 2 ? _skinned.Weapon : -1;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static string DiagClip(Animator animator, int layer)
        {
            AnimatorClipInfo[] info = animator.GetCurrentAnimatorClipInfo(layer);
            return info.Length > 0 && info[0].clip != null ? info[0].clip.name : "-";
        }
#endif

        /// <summary>The override layer rests in the pose's own state (no transition) and the additive layer is empty: the socket is at its authored rest.</summary>
        private bool AtRest(Animator animator, int pose)
        {
            if (animator == null || pose < 0) return false;
            if (_overrideLayer < 0) _overrideLayer = animator.GetLayerIndex(CharacterAnimatorContract.OverrideUpperLayer);
            if (_additiveLayer < 0) _additiveLayer = animator.GetLayerIndex(CharacterAnimatorContract.AdditiveLayer);
            if (_overrideLayer < 0) return false;
            if (animator.IsInTransition(_overrideLayer)) return false;
            string state = pose == 5 ? AimV3State : pose >= 3
                ? CharacterAnimatorContract.SwHoldState + "_" + (pose - 2)
                : CharacterAnimatorContract.AimState + "_" + pose;
            if (!animator.GetCurrentAnimatorStateInfo(_overrideLayer).IsName(state)) return false;
            if (_additiveLayer >= 0)
            {
                if (animator.IsInTransition(_additiveLayer)) return false;
                if (!animator.GetCurrentAnimatorStateInfo(_additiveLayer).IsName(CharacterAnimatorContract.EmptyState)) return false;
            }

            return true;
        }

        private void LateUpdate()
        {
            AlignedThisFrame = false;
            if (!Enabled || _rig == null)
            {
                return;
            }

            if (_health != null && _health.IsDead)
            {
                return;
            }

            Vector3 forward = _rig.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Quaternion baseRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

            // The socket's deviation from its rest pose, in character space (the character turns; the deviation must not).
            Quaternion delta = Quaternion.identity;
            DeltaApplied = false;
            Transform socket = transform.parent;
            Animator animator = _skinned != null ? _skinned.Animator : null;
            int pose = PoseIndex();
            if (socket != null && animator != null && pose >= 0)
            {
                Quaternion socketLocal = Quaternion.Inverse(_rig.transform.rotation) * socket.rotation;
                if (AtRest(animator, pose))
                {
                    _reference[pose] = socketLocal;
                    _hasReference[pose] = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if (Diagnostics)
                    {
                        _refFrame[pose] = Time.frameCount;
                        _refClip[pose] = DiagClip(animator, _overrideLayer);
                    }
#endif
                }

                if (_hasReference[pose])
                {
                    delta = socketLocal * Quaternion.Inverse(_reference[pose]);
                    DeltaApplied = true;
                }
            }

            DeltaDegrees = Quaternion.Angle(Quaternion.identity, delta);
            transform.rotation = baseRotation * delta;
            if (socket != null) transform.position = socket.position - transform.rotation * GripOffset;   // the grip point on the hand
            AlignedThisFrame = true;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Diagnostics && _diagFrames++ < 600 && _diagFrames % 60 == 1)
            {
                string path = "";
                for (Transform p = transform.parent; p != null && path.Length < 200; p = p.parent) path = p.name + "/" + path;
                string layer = "n/a";
                if (animator != null && _overrideLayer >= 0)
                {
                    AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(_overrideLayer);
                    layer = "w " + animator.GetLayerWeight(_overrideLayer).ToString("0.00") + " clip " + DiagClip(animator, _overrideLayer) + " t " + cur.normalizedTime.ToString("0.00") +
                            (animator.IsInTransition(_overrideLayer) ? " (→ next)" : "");
                }

                Transform hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
                string handText = hand != null
                    ? "hand " + _rig.transform.InverseTransformPoint(hand.position).ToString("F2") + " / " + (Quaternion.Inverse(_rig.transform.rotation) * hand.rotation).eulerAngles.ToString("F0") +
                      " mount→hand " + Vector3.Distance(transform.position, hand.position).ToString("F2")
                    : "hand n/a";
                Debug.Log("[DD-MOUNT] frame " + Time.frameCount + " path=" + path + " pose=" + pose + " atRest=" + (animator != null && AtRest(animator, pose)) +
                          " hasRef=" + (pose >= 0 && _hasReference[pose]) + (pose >= 0 ? " refFrame=" + _refFrame[pose] + " refClip=" + _refClip[pose] : "") +
                          " delta=" + DeltaDegrees.ToString("0.0") + " weapon=" + (_skinned != null ? _skinned.Weapon : -9) + " swHold=" + (_skinned != null ? _skinned.SpecialHold : -9) +
                          " override: " + layer + " " + handText);
            }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Enabled = true;
        }
    }
}
