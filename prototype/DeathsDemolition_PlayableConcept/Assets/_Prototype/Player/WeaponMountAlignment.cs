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
    /// character's facing, level, up = world up. The models are authored in that frame (ADR-042-1),
    /// so muzzle, ejection and grip markers land where the contract says. Off while dead (the
    /// collapse keeps the weapon in the falling hand) and switchable for tests / A-B.
    /// </summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class WeaponMountAlignment : MonoBehaviour
    {
        /// <summary>Global switch (tests / A-B): false leaves the mount to the socket bone.</summary>
        public static bool Enabled = true;

        private PlayerRig _rig;
        private PlayerHealth _health;

        /// <summary>True on the frames the mount was written (tests).</summary>
        public bool AlignedThisFrame { get; private set; }

        /// <summary>Attach to <paramref name="mount"/> for <paramref name="rig"/> (idempotent).</summary>
        public static WeaponMountAlignment Attach(Transform mount, PlayerRig rig)
        {
            if (mount == null || rig == null)
            {
                return null;
            }

            WeaponMountAlignment a = mount.GetComponent<WeaponMountAlignment>() ?? mount.gameObject.AddComponent<WeaponMountAlignment>();
            a._rig = rig;
            a._health = rig.Health;
            return a;
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

            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            AlignedThisFrame = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Enabled = true;
        }
    }
}
