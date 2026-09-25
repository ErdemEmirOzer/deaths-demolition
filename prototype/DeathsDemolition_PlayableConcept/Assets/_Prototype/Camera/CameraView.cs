using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// WP-070 · Read-only view queries for systems that care what the player can see.
    /// All values come from the rig's SHAKE-FREE pose. Before a rig exists every query
    /// answers "visible / not rear-blind" so headless tests behave like before.
    /// </summary>
    public static class CameraView
    {
        /// <summary>Shake-free camera position (Vector3.zero without a rig).</summary>
        public static Vector3 Position
        {
            get
            {
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                return rig != null ? rig.StablePosition : Vector3.zero;
            }
        }

        /// <summary>Shake-free camera forward (world +Z without a rig).</summary>
        public static Vector3 Forward
        {
            get
            {
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                return rig != null ? rig.StableRotation * Vector3.forward : Vector3.forward;
            }
        }

        /// <summary>
        /// True in first person: the player cannot see behind, so rear-threat warnings
        /// (S-03 fairness, S-08 audio cue, S-09 HUD arc) should be active.
        /// </summary>
        public static bool IsRearBlind
        {
            get { return CameraModeService.Current == CameraMode.FirstPerson; }
        }

        /// <summary>
        /// WP-070 (S-06 animation): view pitch for aim offsets, degrees, POSITIVE = UP, clamped
        /// to ±60 (the animation range). 0 in top-down (the body never pitches there).
        /// </summary>
        public static float AimPitchUpDegrees
        {
            get
            {
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                if (rig == null || !CameraModeService.IsLookMode)
                {
                    return 0f;
                }

                return Mathf.Clamp(-rig.ViewPitch, -60f, 60f);
            }
        }

        /// <summary>
        /// WP-070 (S-06): signed yaw of the camera relative to <paramref name="body"/>'s facing,
        /// degrees (−180…180, positive = camera turned right of the body). 0 in top-down.
        /// </summary>
        public static float AimYawDegrees(Transform body)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            if (rig == null || body == null || !CameraModeService.IsLookMode)
            {
                return 0f;
            }

            Vector3 f = body.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(f.normalized, rig.PlanarForward, Vector3.up);
        }

        /// <summary>
        /// FP viewmodel anchor (weapon/hands root on the <see cref="CameraLayers.FirstPersonView"/>
        /// layer); null outside first person. S-08 places first-person weapon audio here.
        /// </summary>
        public static Transform ViewmodelAnchor
        {
            get
            {
                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                return rig != null && CameraModeService.Current == CameraMode.FirstPerson ? rig.ViewmodelAnchor : null;
            }
        }

        /// <summary>
        /// WP-070 (S-07 VFX): converts a point on the FP viewmodel (drawn by the
        /// <see cref="CameraLayers.FirstPersonViewName"/> pass with <see cref="CameraLayers.ViewmodelFov"/>)
        /// into the world point that appears at the SAME screen position and depth under the
        /// world camera's FOV. Use it to start the world tracer / place the world muzzle
        /// light where the viewmodel muzzle is seen. Identity outside first person.
        /// </summary>
        public static Vector3 ViewmodelToWorld(Vector3 viewmodelWorldPosition)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Camera cam = rig != null ? rig.Camera : null;
            if (cam == null || CameraModeService.Current != CameraMode.FirstPerson)
            {
                return viewmodelWorldPosition;
            }

            Transform t = cam.transform;
            Vector3 local = t.InverseTransformPoint(viewmodelWorldPosition);
            if (local.z <= 1e-4f)
            {
                return viewmodelWorldPosition;
            }

            // WP-070V: the viewmodel pass also applies CameraLayers' screen transform (NDC' = s·NDC + t), so the
            // world point keeps the depth and lands where the viewmodel point is drawn.
            CameraLayers.GetViewmodelScreenTransform(out float s, out Vector2 ndc);
            float tanWorld = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float scale = s * tanWorld / Mathf.Tan(CameraLayers.ViewmodelFov * 0.5f * Mathf.Deg2Rad);
            return t.TransformPoint(new Vector3(local.x * scale + ndc.x * local.z * tanWorld * cam.aspect,
                                                local.y * scale + ndc.y * local.z * tanWorld, local.z));
        }

        /// <summary>
        /// Whether <paramref name="worldPosition"/> lies inside the camera's view frustum
        /// widened by <paramref name="marginDegrees"/> on every side. Top-down always
        /// answers by frustum too, so "off screen" means the same thing in every mode.
        /// </summary>
        public static bool IsInView(Vector3 worldPosition, float marginDegrees = 0f)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;
            Camera cam = rig != null ? rig.Camera : null;
            if (cam == null)
            {
                return true;
            }

            Vector3 local = Quaternion.Inverse(rig.StableRotation) * (worldPosition - rig.StablePosition);
            if (local.z <= 0f)
            {
                return false;
            }

            float halfV = cam.fieldOfView * 0.5f + marginDegrees;
            float halfH = Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg + marginDegrees;
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
            return Mathf.Abs(yaw) <= halfH && Mathf.Abs(pitch) <= halfV;
        }
    }

    /// <summary>WP-070 · Layer names the camera modes rely on (TagManager).</summary>
    public static class CameraLayers
    {
        /// <summary>FP viewmodel (hands + weapon), drawn by a URP RenderObjects pass with its own FOV.</summary>
        public const string FirstPersonViewName = "FirstPersonView";

        /// <summary>
        /// Phase-2 override of the viewmodel vertical FOV (0 = locked to the world FOV). Direction
        /// doc D-5: Phase 1 keeps the viewmodel on the world FOV because the hands are part of the
        /// body mesh (a different FOV would detach the wrist); S-06's FP arm mesh unlocks it.
        /// </summary>
        public static float ViewmodelFovOverride { get; set; }

        /// <summary>Viewmodel vertical FOV in effect: the override, or the world camera's FOV (Phase 1).</summary>
        public static float ViewmodelFov
        {
            get
            {
                if (ViewmodelFovOverride > 0f)
                {
                    return ViewmodelFovOverride;
                }

                PrototypeCameraRig rig = PrototypeCameraRig.Active;
                return rig != null && rig.Camera != null ? rig.Camera.fieldOfView : 60f;
            }
        }

        /// <summary>
        /// WP-070V (S-09 AD R1-A MUST-2): the FP viewmodel's resting silhouette covered the HUD weapon cluster
        /// (bottom-right 420×150 + 24 px at 1080). Until the FP arm rest pose (S-06 B4b) the viewmodel pass is
        /// drawn with a screen-space transform: scale about <see cref="ViewmodelScreenPivot"/> (screen fraction,
        /// 0,0 = bottom-left), then shift by <see cref="ViewmodelScreenShift"/> (screen fraction). The world is
        /// not affected; <see cref="CameraView.ViewmodelToWorld"/> applies the same transform.
        /// WP-070I (C-00 25.09): IDENTITY by default. The FP HUD weapon cluster moved to the bottom-left (S-09 R1-C) and
        /// S-02's weapon-head fpView pose alone places the viewmodel; the old 0.75 / −12 % default pushed the weapon
        /// towards the new cluster. The knobs stay for tests and tuning.
        /// </summary>
        public const float DefaultViewmodelScreenScale = 1f;
        public static readonly Vector2 DefaultViewmodelScreenPivot = new Vector2(0f, 0f);
        public static readonly Vector2 DefaultViewmodelScreenShift = Vector2.zero;

        public static float ViewmodelScreenScale { get; set; } = DefaultViewmodelScreenScale;
        public static Vector2 ViewmodelScreenPivot { get; set; } = DefaultViewmodelScreenPivot;
        public static Vector2 ViewmodelScreenShift { get; set; } = DefaultViewmodelScreenShift;

        /// <summary>The transform in clip space: NDC' = <paramref name="scale"/> · NDC + <paramref name="ndcOffset"/>.</summary>
        public static void GetViewmodelScreenTransform(out float scale, out Vector2 ndcOffset)
        {
            scale = ViewmodelScreenScale > 0f ? ViewmodelScreenScale : 1f;
            Vector2 pivot = ViewmodelScreenPivot * 2f - Vector2.one;
            ndcOffset = pivot * (1f - scale) + ViewmodelScreenShift * 2f;
        }

        private static int s_firstPersonView = -2;

        /// <summary>Layer index, or -1 when the layer is not defined in TagManager.</summary>
        public static int FirstPersonView
        {
            get
            {
                if (s_firstPersonView == -2)
                {
                    s_firstPersonView = LayerMask.NameToLayer(FirstPersonViewName);
                }

                return s_firstPersonView;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ViewmodelFovOverride = 0f;
            ViewmodelScreenScale = DefaultViewmodelScreenScale;
            ViewmodelScreenPivot = DefaultViewmodelScreenPivot;
            ViewmodelScreenShift = DefaultViewmodelScreenShift;
            s_firstPersonView = -2;
        }
    }
}
