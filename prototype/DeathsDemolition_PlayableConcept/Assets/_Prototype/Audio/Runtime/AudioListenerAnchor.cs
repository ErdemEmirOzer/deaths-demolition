using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Audio
{
    /// <summary>
    /// ADR-021-2 · The player-anchored listener.
    ///
    /// The gameplay camera sits ~20 m above and behind the player; a listener on it hears
    /// everything from twenty metres away and pans by where things are relative to the
    /// CAMERA. This anchor owns a second <see cref="AudioListener"/> that, whenever a
    /// player rig exists, sits at the player's head with the camera's yaw - so distance
    /// is distance from the player and left/right is left/right on screen - and hands
    /// back to the scene's own listener (Boot / menu camera) when there is no player.
    ///
    /// Unity allows one active listener, so the swap is done by enabling/disabling
    /// components, never by adding or destroying them: the camera factory's listener stays
    /// where the camera task put it. Rolloff ranges in <see cref="AudioVoicePool"/> are the
    /// WP-002 ones (8-60 m linear); with the listener at the player the 8 m plateau is now
    /// "the player's own space" literally, which is what it was written to mean.
    /// </summary>
    public sealed class AudioListenerAnchor
    {
        /// <summary>Head height above the rig root. PROTOTYPE-ONLY / INVENTED.</summary>
        private const float HeadHeight = 1.4f;

        private readonly AudioListener _own;
        private readonly Transform _transform;
        private AudioListener[] _others = System.Array.Empty<AudioListener>();
        private bool _scanDirty = true;

        /// <summary>True while our listener is the active one (a player exists).</summary>
        public bool Anchored { get; private set; }

        public Transform Transform => _transform;

        public AudioListenerAnchor(Transform root)
        {
            var go = new GameObject("Listener (player-anchored)");
            go.transform.SetParent(root, false);
            _transform = go.transform;
            _own = go.AddComponent<AudioListener>();
            _own.enabled = false;
        }

        public void Subscribe()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        public void Unsubscribe()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _scanDirty = true;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _scanDirty = true;
        }

        /// <summary>Call from LateUpdate: after the player moved and the camera followed.</summary>
        public void LateTick()
        {
            if (_scanDirty)
            {
                Rescan();
            }

            PlayerRig rig = PlayerRegistry.Primary;
            bool anchor = rig != null && rig.isActiveAndEnabled;

            if (anchor != Anchored)
            {
                Anchored = anchor;
                Apply();
            }

            if (!anchor)
            {
                // No player: the scene's own listener is active. A scene without one
                // (the camera factories skip theirs when this one is found) is covered
                // by ours riding the current camera.
                if (_others.Length == 0)
                {
                    Camera cam = FeedbackCameraRef.Current;
                    if (cam != null)
                    {
                        _transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
                    }
                }

                return;
            }

            Vector3 forward = FeedbackCameraRef.Forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            _transform.SetPositionAndRotation(
                rig.transform.position + Vector3.up * HeadHeight,
                Quaternion.LookRotation(forward.normalized, Vector3.up));
        }

        private void Rescan()
        {
            _scanDirty = false;
            AudioListener[] all = Object.FindObjectsOfType<AudioListener>(true);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != _own) n++;
            }

            var others = new AudioListener[n];
            n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != _own) others[n++] = all[i];
            }

            _others = others;
            Apply();
        }

        /// <summary>Exactly one listener enabled: ours when anchored or when the scene has none, else the scene's.</summary>
        private void Apply()
        {
            bool own = Anchored || _others.Length == 0;
            for (int i = 0; i < _others.Length; i++)
            {
                if (_others[i] != null)
                {
                    _others[i].enabled = !Anchored;
                }
            }

            _own.enabled = own;
        }

        public string Describe()
        {
            return Anchored ? "listener: player-anchored" : (_others.Length == 0 ? "listener: own, riding camera" : "listener: scene camera (" + _others.Length + ")");
        }
    }
}
