using UnityEngine;

namespace DD.Prototype.CameraSystem
{
    /// <summary>
    /// Builds the camera rig from code instead of a prefab.
    ///
    /// Reason: four tasks are authoring this project in parallel and a hand-edited
    /// prefab or scene file is the one artefact they cannot merge. A factory is
    /// mergeable, reviewable and cannot carry a broken serialized reference.
    ///
    /// Anyone building the Run scene calls:
    ///
    ///     PrototypeCameraRig rig = CameraRigFactory.EnsureRig(playerFollowTarget);
    ///
    /// It is idempotent: calling it again returns the existing rig and just retargets it.
    /// </summary>
    public static class CameraRigFactory
    {
        public const string RigName = "[Prototype Camera Rig]";
        public const string CameraName = "Camera";

        /// <summary>
        /// Returns the live rig, creating it if needed, and points it at
        /// <paramref name="followTarget"/>.
        /// </summary>
        public static PrototypeCameraRig EnsureRig(Transform followTarget)
        {
            PrototypeCameraRig rig = PrototypeCameraRig.Active;

            if (rig == null)
            {
                rig = Object.FindObjectOfType<PrototypeCameraRig>();
            }

            if (rig == null)
            {
                rig = Create();
            }

            if (followTarget != null)
            {
                rig.FollowTarget = followTarget;
            }

            return rig;
        }

        /// <summary>Creates a new rig. Prefer <see cref="EnsureRig"/>.</summary>
        public static PrototypeCameraRig Create()
        {
            // Built inactive so every component is wired before OnEnable and the first
            // ApplyConfig runs. Waking a half-wired rig would place the camera at the
            // origin for one frame.
            var rigGo = new GameObject(RigName);
            rigGo.SetActive(false);

            var cameraGo = new GameObject(CameraName);
            cameraGo.transform.SetParent(rigGo.transform, false);

            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.depth = 0f;

            // WP-013: render the Global Volume's grade on the gameplay camera (post-processing
            // was never switched on before; the atmosphere owns that decision).
            DD.Prototype.World.WorldAtmosphere.ConfigureCamera(camera);

            if (Object.FindObjectOfType<AudioListener>() == null)
            {
                cameraGo.AddComponent<AudioListener>();
            }

            PrototypeCameraRig rig = rigGo.AddComponent<PrototypeCameraRig>();
            rigGo.AddComponent<CameraOcclusionFader>();
            rigGo.AddComponent<TreeCanopyReveal>();   // WP-109: top-down crowns thin over the hero (DD_TreeReveal.hlsl)

            rigGo.SetActive(true);

            // MainCamera tag so anything falling back to Camera.main still works.
            TrySetMainCameraTag(cameraGo);

            return rig;
        }

        private static void TrySetMainCameraTag(GameObject go)
        {
            try
            {
                go.tag = "MainCamera";
            }
            catch (UnityException)
            {
                // Tag missing in a stripped project; harmless, nothing relies on it.
            }
        }
    }
}
