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

            float scale = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(CameraLayers.ViewmodelFov * 0.5f * Mathf.Deg2Rad);
            return t.TransformPoint(new Vector3(local.x * scale, local.y * scale, local.z));
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
            s_firstPersonView = -2;
        }
    }
}
