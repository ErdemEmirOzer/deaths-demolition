using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// The gameplay camera, resolved once and re-resolved only when it goes away.
    ///
    /// Billboarding a damage number and projecting a hit marker both need the camera
    /// every frame; a crowd of thirty effects doing <c>Camera.main</c> per frame is a
    /// per-frame lookup the request explicitly bans.
    /// </summary>
    public static class FeedbackCameraRef
    {
        private static Camera s_camera;

        /// <summary>May be null before the run scene has loaded. Callers must handle that.</summary>
        public static Camera Current
        {
            get
            {
                if (s_camera == null)
                {
                    s_camera = Camera.main;
                }

                return s_camera;
            }
        }

        /// <summary>Let the camera task hand the reference over explicitly instead of tagging MainCamera.</summary>
        public static void Set(Camera camera)
        {
            s_camera = camera;
        }

        /// <summary>Forward vector of the camera, or world forward when there is no camera.</summary>
        public static Vector3 Forward
        {
            get
            {
                Camera c = Current;
                return c != null ? c.transform.forward : Vector3.forward;
            }
        }

        /// <summary>
        /// Viewer position, for feedback that needs to know which way "away from the
        /// player's eye" is - an explosion pushing the camera back, for instance.
        /// False when no camera exists yet, which is the caller's cue to fall back to a
        /// non-directional response rather than to guess.
        /// </summary>
        public static bool TryGetPosition(out Vector3 position)
        {
            Camera c = Current;
            if (c == null)
            {
                position = Vector3.zero;
                return false;
            }

            position = c.transform.position;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_camera = null;
        }
    }
}
