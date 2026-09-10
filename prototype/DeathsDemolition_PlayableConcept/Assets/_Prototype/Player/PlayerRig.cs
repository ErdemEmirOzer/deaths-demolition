using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// The integration surface of the player. Every other task talks to the player
    /// through this component and never by walking the hierarchy.
    ///
    ///     PlayerRig rig = PlayerRegistry.Primary;
    ///     rig.Health.ApplyDamage(...);                  // enemies
    ///     weapon.transform.SetParent(rig.WeaponMount);  // weapons
    ///     rig.Animation.PlayFireRecoil(0.6f, dir);      // weapons
    ///     rig.Aim.AimWorldPoint;                        // weapons, HUD crosshair
    ///     rig.CameraFollowTarget;                       // camera
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRig : MonoBehaviour
    {
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private PlayerAim _aim;
        [SerializeField] private PlayerHealth _health;
        [SerializeField] private PlayerProceduralAnimator _animation;

        [Tooltip("Where a weapon model is parented.")]
        [SerializeField] private Transform _weaponMount;

        [Tooltip("Muzzle position and forward. Weapons may replace this with their own.")]
        [SerializeField] private Transform _muzzleAnchor;

        [Tooltip("What the camera follows. Sits at the character's feet.")]
        [SerializeField] private Transform _cameraFollowTarget;

        [SerializeField] private PlayerSkin _skin = PlayerSkins.Default;

        public PlayerMotor Motor
        {
            get { return _motor; }
        }

        public PlayerAim Aim
        {
            get { return _aim; }
        }

        public PlayerHealth Health
        {
            get { return _health; }
        }

        public PlayerProceduralAnimator Animation
        {
            get { return _animation; }
        }

        public Transform WeaponMount
        {
            get { return _weaponMount; }
        }

        public Transform MuzzleAnchor
        {
            get { return _muzzleAnchor; }
        }

        public Transform CameraFollowTarget
        {
            get { return _cameraFollowTarget != null ? _cameraFollowTarget : transform; }
        }

        public PlayerSkin Skin
        {
            get { return _skin; }
        }

        /// <summary>Called by the factory once the hierarchy exists.</summary>
        public void Bind(
            PlayerMotor motor,
            PlayerAim aim,
            PlayerHealth health,
            PlayerProceduralAnimator animation,
            Transform weaponMount,
            Transform muzzleAnchor,
            Transform cameraFollowTarget)
        {
            _motor = motor;
            _aim = aim;
            _health = health;
            _animation = animation;
            _weaponMount = weaponMount;
            _muzzleAnchor = muzzleAnchor;
            _cameraFollowTarget = cameraFollowTarget;
        }

        private void OnEnable()
        {
            PlayerRegistry.Register(this);

            if (_health != null)
            {
                _health.Died += OnDied;
            }
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.Died -= OnDied;
            }

            PlayerRegistry.Unregister(this);
        }

        /// <summary>
        /// Stops movement and facing without disabling the components, so their state
        /// stays readable. Only death and scene transitions should use this: REQ-045
        /// forbids taking control away because of damage.
        /// </summary>
        public void SetControlEnabled(bool value)
        {
            if (_motor != null)
            {
                _motor.ControlEnabled = value;
                if (!value)
                {
                    _motor.ClearVelocity();
                }
            }

            if (_aim != null)
            {
                _aim.ControlEnabled = value;
            }
        }

        /// <summary>Moves the player, clearing velocity. Used at run start and by the debug panel.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            transform.rotation = rotation;

            if (_motor != null)
            {
                _motor.Teleport(position);
            }
            else
            {
                transform.position = position;
            }
        }

        /// <summary>Recolours the character. Cosmetic only, safe to call at any time.</summary>
        public void ApplySkin(PlayerSkin skin)
        {
            _skin = skin;
            PlayerRigFactory.PaintRig(gameObject, skin);
        }

        private void OnDied(PlayerDeathInfo info)
        {
            SetControlEnabled(false);

            if (_animation != null)
            {
                _animation.PlayDeath();
            }
        }
    }

    /// <summary>
    /// Where systems find the player. This is a lookup of active rigs, not a gameplay
    /// singleton: it holds no state, supports more than one rig, and no system is
    /// required to use it - enemies that already have a collider reference should call
    /// GetComponentInParent instead.
    /// </summary>
    public static class PlayerRegistry
    {
        private static readonly List<PlayerRig> s_rigs = new List<PlayerRig>(2);

        public static IReadOnlyList<PlayerRig> All
        {
            get { return s_rigs; }
        }

        /// <summary>The first live rig, or null. Convenience for single-player code paths.</summary>
        public static PlayerRig Primary
        {
            get { return s_rigs.Count > 0 ? s_rigs[0] : null; }
        }

        /// <summary>Raised when a rig appears or disappears, so late systems can bind.</summary>
        public static event System.Action<PlayerRig> RigRegistered;

        public static event System.Action<PlayerRig> RigUnregistered;

        public static void Register(PlayerRig rig)
        {
            if (rig == null || s_rigs.Contains(rig))
            {
                return;
            }

            s_rigs.Add(rig);

            System.Action<PlayerRig> handler = RigRegistered;
            if (handler != null)
            {
                handler.Invoke(rig);
            }
        }

        public static void Unregister(PlayerRig rig)
        {
            if (rig == null || !s_rigs.Remove(rig))
            {
                return;
            }

            System.Action<PlayerRig> handler = RigUnregistered;
            if (handler != null)
            {
                handler.Invoke(rig);
            }
        }

        /// <summary>Nearest live rig to a world position. False when none exist.</summary>
        public static bool TryGetNearest(Vector3 position, out PlayerRig nearest)
        {
            nearest = null;
            float best = float.MaxValue;

            for (int i = 0; i < s_rigs.Count; i++)
            {
                PlayerRig rig = s_rigs[i];
                if (rig == null)
                {
                    continue;
                }

                float distance = (rig.transform.position - position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = rig;
                }
            }

            return nearest != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_rigs.Clear();
            RigRegistered = null;
            RigUnregistered = null;
        }
    }
}
