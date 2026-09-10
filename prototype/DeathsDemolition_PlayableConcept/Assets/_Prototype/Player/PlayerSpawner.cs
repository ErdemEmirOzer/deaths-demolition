using DD.Prototype.CameraSystem;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Drop this on an empty GameObject in the Run scene and the player appears at its
    /// transform when play starts, with the camera rig already following.
    ///
    /// The run flow may also skip the component entirely and call
    /// <see cref="Spawn(Vector3, Quaternion, PlayerSkin)"/> directly, which is the
    /// route to use when the skin comes from the selection screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [Tooltip("Spawn as soon as this component starts. Turn off if the run flow spawns the player itself.")]
        [SerializeField] private bool _spawnOnStart = true;

        [Tooltip("Skin used when nothing else selected one.")]
        [SerializeField] private PlayerSkin _skin = PlayerSkins.Default;

        [Tooltip("Face this direction on spawn, in degrees. 0 looks along the camera's forward.")]
        [SerializeField] private float _facingDegrees = 0f;

        private PlayerRig _spawned;

        /// <summary>The rig this spawner created, or null.</summary>
        public PlayerRig Spawned
        {
            get { return _spawned; }
        }

        private void Start()
        {
            if (_spawnOnStart && _spawned == null)
            {
                _spawned = Spawn(transform.position, Quaternion.Euler(0f, _facingDegrees, 0f), _skin);
            }
        }

        /// <summary>
        /// Creates the player, or moves the existing one. Safe to call more than once:
        /// a run restart re-uses the rig rather than leaving a corpse in the scene.
        /// </summary>
        public static PlayerRig Spawn(Vector3 position, Quaternion rotation, PlayerSkin skin)
        {
            PlayerRig existing = PlayerRegistry.Primary;
            if (existing != null)
            {
                existing.Teleport(position, rotation);
                existing.ApplySkin(skin);

                if (existing.Health != null)
                {
                    existing.Health.ResetForNewRun();
                }

                existing.SetControlEnabled(true);

                PrototypeCameraRig rig = CameraRigFactory.EnsureRig(existing.CameraFollowTarget);
                if (rig != null && existing.Aim != null)
                {
                    rig.SetLookAheadProvider(existing.Aim);
                }

                return existing;
            }

            return PlayerRigFactory.Create(position, rotation, skin);
        }

        /// <summary>Spawns with the default skin at the given point.</summary>
        public static PlayerRig Spawn(Vector3 position)
        {
            return Spawn(position, Quaternion.identity, PlayerSkins.Default);
        }
    }
}
