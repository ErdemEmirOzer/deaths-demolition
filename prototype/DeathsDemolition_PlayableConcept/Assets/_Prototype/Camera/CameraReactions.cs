using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-070 · Camera reaction hooks for game feel (M4-R1 N18). The rig drains them each
    /// LateUpdate. Channel split (direction doc D-20):
    ///
    ///   * <see cref="AddRecoil"/>   — joins the VIEW (TP/FP): the aim ray follows it and it
    ///                                 returns at the recoil return rate. Source = S-02 weapon
    ///                                 pattern data; return values = S-07. Ignored in TopDown.
    ///   * <see cref="AddKick"/>     — positional kick + roll on the shake node (never aim;
    ///                                 FP translation dropped, D-15).
    ///   * <see cref="SetLocomotionBob"/> — FP walk/run bob input (speed 0..1, grounded);
    ///                                 scaled by CameraModeSettings.HeadBobScale.
    ///   * <see cref="FovPunch"/>    — short FOV change that eases back.
    ///
    /// Values are accumulated here and drained by the rig; magnitudes are per-mode scaled
    /// by the rig from the FeelRuntime camera-mode profile (values owned by S-07).
    /// </summary>
    public static class CameraReactions
    {
        private static Vector2 s_recoil;        // x = pitch up (deg), y = yaw right (deg)
        private static Vector3 s_kickOffset;
        private static float s_kickRoll;
        private static float s_bobSpeed01;
        private static bool s_bobGrounded = true;
        private static int s_bobFrame = -1;
        private static float s_fovPunchDegrees;
        private static float s_fovPunchSeconds;

        /// <summary>Adds a view-angle recoil impulse (pitch up positive, yaw right positive).</summary>
        public static void AddRecoil(float pitchDegrees, float yawDegrees)
        {
            s_recoil.x += pitchDegrees;
            s_recoil.y += yawDegrees;
        }

        /// <summary>Adds a positional kick (camera-local metres) and roll (degrees).</summary>
        public static void AddKick(Vector3 localOffset, float rollDegrees)
        {
            s_kickOffset += localOffset;
            s_kickRoll += rollDegrees;
        }

        /// <summary>Locomotion input for the FP head bob; call every frame from the motor/feel layer.</summary>
        public static void SetLocomotionBob(float speed01, bool grounded)
        {
            s_bobSpeed01 = Mathf.Clamp01(speed01);
            s_bobGrounded = grounded;
            s_bobFrame = Time.frameCount;
        }

        /// <summary>Temporarily widens (positive) or narrows the world FOV, easing back over <paramref name="seconds"/>.</summary>
        public static void FovPunch(float deltaDegrees, float seconds)
        {
            if (Mathf.Abs(deltaDegrees) > Mathf.Abs(s_fovPunchDegrees))
            {
                s_fovPunchDegrees = deltaDegrees;
                s_fovPunchSeconds = Mathf.Max(0.01f, seconds);
            }
        }

        /// <summary>Rig side: takes the accumulated impulses (and clears them).</summary>
        public static void Drain(out Vector2 recoil, out Vector3 kickOffset, out float kickRoll,
            out float fovPunchDegrees, out float fovPunchSeconds)
        {
            recoil = s_recoil;
            kickOffset = s_kickOffset;
            kickRoll = s_kickRoll;
            fovPunchDegrees = s_fovPunchDegrees;
            fovPunchSeconds = s_fovPunchSeconds;
            s_recoil = Vector2.zero;
            s_kickOffset = Vector3.zero;
            s_kickRoll = 0f;
            s_fovPunchDegrees = 0f;
            s_fovPunchSeconds = 0f;
        }

        /// <summary>Rig side: locomotion bob input; false when nobody fed it this frame or the last one.</summary>
        public static bool ReadBob(out float speed01, out bool grounded)
        {
            speed01 = s_bobSpeed01;
            grounded = s_bobGrounded;
            return s_bobFrame >= Time.frameCount - 1 && s_bobFrame >= 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_recoil = Vector2.zero;
            s_kickOffset = Vector3.zero;
            s_kickRoll = 0f;
            s_bobSpeed01 = 0f;
            s_bobGrounded = true;
            s_bobFrame = -1;
            s_fovPunchDegrees = 0f;
            s_fovPunchSeconds = 0f;
        }
    }

    /// <summary>WP-070 · Shake-free aim ray (see <c>PrototypeCameraRig.TryGetAimRay</c>).</summary>
    public struct CameraAimRay
    {
        /// <summary>Ray origin: the stable camera position (TP/FP) or the cursor ray origin (TopDown).</summary>
        public Vector3 Origin;

        /// <summary>Normalised ray direction.</summary>
        public Vector3 Direction;

        /// <summary>First hit on world/enemy colliders, or the point at max range / aim plane when nothing was hit.</summary>
        public Vector3 HitPoint;

        /// <summary>True when a collider was hit (TP/FP) or the aim plane was reached (TopDown).</summary>
        public bool HasHit;

        /// <summary>Distance from <see cref="Origin"/> to <see cref="HitPoint"/>.</summary>
        public float Distance;
    }
}
