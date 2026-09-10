using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// Drop this on one GameObject in the run scene and the whole weapon and hit-feel
    /// stack comes up.
    ///
    /// It exists because four tasks are assembling one scene in parallel: whoever builds
    /// the run scene should not have to know that the feedback director, the hit-marker
    /// canvas, the debug hooks and the Special Weapon director are four separate objects.
    /// One component, no fields, done.
    ///
    /// THE PLAYER IS FOUND, NOT ASSIGNED. The player is spawned at runtime, so an
    /// inspector reference to it cannot exist. The installer subscribes to
    /// <see cref="PlayerRegistry.RigRegistered"/> and arms whatever rig appears, which
    /// also makes it correct across a respawn and correct when the player is created
    /// after this component. <see cref="PlayerAimPivot"/> remains as a manual override
    /// for a test scene with no PlayerRig in it.
    ///
    /// Everything it creates is idempotent: running it twice changes nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponSystemInstaller : MonoBehaviour
    {
        [Tooltip("Optional override. Leave empty to arm whatever PlayerRig registers itself.")]
        [SerializeField] private Transform _playerAimPivot;

        [Tooltip("Arm any PlayerRig that appears, including one spawned after this component.")]
        [SerializeField] private bool _autoBindPlayerRig = true;

        [Tooltip("Create the Special Weapon director. Turn off for a scene that has its own.")]
        [SerializeField] private bool _createSpecialWeaponDirector = true;

        [Tooltip("Create the hit-feedback director up front so its pools are warm before the first shot.")]
        [SerializeField] private bool _warmFeedback = true;

        /// <summary>Assign the player at runtime, e.g. right after the player is spawned.</summary>
        public Transform PlayerAimPivot
        {
            get => _playerAimPivot;
            set
            {
                _playerAimPivot = value;
                Install();
            }
        }

        private void Awake()
        {
            Install();
        }

        private void OnEnable()
        {
            if (!_autoBindPlayerRig)
            {
                return;
            }

            PlayerRegistry.RigRegistered += OnRigRegistered;

            // A rig that registered before this component woke up is not going to raise
            // the event again, so it is picked up directly.
            if (PlayerRegistry.Primary != null)
            {
                OnRigRegistered(PlayerRegistry.Primary);
            }
        }

        private void OnDisable()
        {
            PlayerRegistry.RigRegistered -= OnRigRegistered;
        }

        private static void OnRigRegistered(PlayerRig rig)
        {
            // Attach does the whole job: binding, controller, aim source, weapon mount.
            PlayerWeaponBinding.Attach(rig);
        }

        private void Install()
        {
            if (_warmFeedback)
            {
                _ = HitFeedbackDirector.Instance;
            }

            WeaponDebugHooks.EnsureExists();

            if (_createSpecialWeaponDirector && FindObjectOfType<SpecialWeaponDirector>() == null)
            {
                var go = new GameObject("[Special Weapon Director]");
                go.transform.SetParent(transform, false);
                go.AddComponent<SpecialWeaponDirector>();
            }

            if (_playerAimPivot == null)
            {
                return;
            }

            if (_playerAimPivot.GetComponent<WeaponController>() == null)
            {
                _playerAimPivot.gameObject.AddComponent<WeaponController>();
            }
        }
    }
}
