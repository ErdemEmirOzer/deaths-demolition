using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The single adapter between the weapon stack and the player stack.
    ///
    /// WHY THIS FILE EXISTS
    /// The player task (G-02) publishes exactly four things a weapon needs, all of them
    /// on <see cref="PlayerRig"/>:
    ///
    ///     rig.Aim.AimWorldPoint                      where the cursor actually is
    ///     rig.MuzzleAnchor                           where a shot leaves the character
    ///     rig.WeaponMount                            where a weapon model belongs
    ///     rig.Animation.PlayFireRecoil / PlayReload  what the arms do about it
    ///
    /// Consuming those from inside <see cref="WeaponController"/> would put a hard
    /// dependency on the player implementation in the middle of the firing path. Instead
    /// the controller talks to <see cref="IAimSource"/> and <see cref="IWeaponHost"/>,
    /// and this one class - the only file in the folder that names a player type - fills
    /// them in.
    ///
    /// AIM: THE POINT, NOT THE FACING
    /// The request's turn rate is finite, so during a turn the character's forward and
    /// the cursor disagree by up to a few degrees. Firing along transform.forward would
    /// send the shot where the character has got to so far; firing at
    /// <see cref="PlayerAim.AimWorldPoint"/> sends it where the player is pointing. The
    /// second is what a top-down shooter is expected to do, and it is why the aim point
    /// is preferred over the direction here.
    ///
    /// The point is resolved against the MUZZLE, not the character root, so the shot
    /// converges on the cursor rather than running parallel to a line through it.
    ///
    /// SELF-INSTALLING
    /// <see cref="WeaponSystemInstaller"/> adds this to the rig, and it finds its own rig
    /// if it was dropped on one by hand. Nothing has to be wired in the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponBinding : MonoBehaviour, IAimSource, IWeaponHost
    {
        /// <summary>
        /// PROTOTYPE-ONLY: below this distance between muzzle and aim point the point is
        /// ignored and the character's facing is used instead. Without it, a cursor
        /// resting on the player's own feet would make the aim direction degenerate and
        /// the weapon would spray at random.
        /// </summary>
        public const float MinAimPointDistance = 0.75f;

        [Tooltip("Leave empty to use the rig on this object or its parents.")]
        [SerializeField] private PlayerRig _rig;

        private WeaponController _controller;

        /// <summary>The rig this binding drives. Null until a rig exists.</summary>
        public PlayerRig Rig => _rig;

        private void Awake()
        {
            if (_rig == null)
            {
                _rig = GetComponentInParent<PlayerRig>();
            }
        }

        private void OnEnable()
        {
            if (_rig == null)
            {
                _rig = GetComponentInParent<PlayerRig>();
            }

            EnsureController();
        }

        /// <summary>
        /// Attach the binding and a weapon to a rig. Idempotent, so the installer, the
        /// player spawner and a hand-placed component can all call it.
        /// </summary>
        public static PlayerWeaponBinding Attach(PlayerRig rig)
        {
            if (rig == null)
            {
                return null;
            }

            PlayerWeaponBinding binding = rig.GetComponentInChildren<PlayerWeaponBinding>(true);
            if (binding == null)
            {
                binding = rig.gameObject.AddComponent<PlayerWeaponBinding>();
            }

            binding._rig = rig;
            binding.EnsureController();
            return binding;
        }

        private void EnsureController()
        {
            if (_controller != null || _rig == null)
            {
                return;
            }

            _controller = _rig.GetComponentInChildren<WeaponController>(true);
            if (_controller == null)
            {
                _controller = _rig.gameObject.AddComponent<WeaponController>();
            }

            _controller.BindHost(this);
        }

        // ------------------------------------------------------------------ IAimSource

        /// <summary>
        /// Muzzle -> cursor, flattened by the controller. Falls back to the aim direction
        /// and then to the character's facing, so this never returns a zero vector.
        /// </summary>
        public Vector3 AimDirection
        {
            get
            {
                PlayerAim aim = _rig != null ? _rig.Aim : null;
                if (aim == null)
                {
                    return transform.forward;
                }

                if (aim.HasAimPoint)
                {
                    Vector3 toPoint = aim.AimWorldPoint - MuzzlePosition;
                    toPoint.y = 0f;

                    if (toPoint.sqrMagnitude >= MinAimPointDistance * MinAimPointDistance)
                    {
                        return toPoint;
                    }
                }

                Vector3 direction = aim.AimDirection;
                return direction.sqrMagnitude > 1e-6f ? direction : transform.forward;
            }
        }

        /// <summary>
        /// Where a shot actually leaves the character.
        ///
        /// The weapon's own rig muzzle wins over the rig's generic anchor when a weapon
        /// is equipped, because that is the transform <see cref="HitscanEngine"/> is
        /// given as the ray origin. Resolving the aim point against any other point would
        /// make the tracer converge somewhere the cursor is not, by the offset between
        /// the two - small, constant, and exactly the kind of thing that reads as "the
        /// gun is slightly off" without anyone being able to say why.
        /// </summary>
        public Vector3 MuzzlePosition
        {
            get
            {
                if (_controller != null)
                {
                    Transform muzzle = _controller.Muzzle;
                    if (muzzle != null)
                    {
                        return muzzle.position;
                    }
                }

                Transform anchor = _rig != null ? _rig.MuzzleAnchor : null;
                return anchor != null ? anchor.position : transform.position;
            }
        }

        /// <summary>
        /// A dead player does not fire. Damage explicitly does NOT block firing - REQ-045
        /// forbids taking control away because of damage - so health above zero is the
        /// only gate, alongside the rig's own control flag.
        ///
        /// A frozen simulation (pause menu, round-end upgrade screen, any
        /// <see cref="RunFlowClock"/> hold) does not fire either. Hitscan resolves
        /// instantly and ignores Time.timeScale, so without this gate one shot per freeze
        /// slipped through while the world stood still (WP-000 B1; PauseFireTests).
        /// </summary>
        public bool CanFire
        {
            get
            {
                if (RunFlowClock.IsFrozen)
                {
                    return false;
                }

                if (_rig == null)
                {
                    return true;
                }

                if (_rig.Health != null && _rig.Health.IsDead)
                {
                    return false;
                }

                return _rig.Animation == null || !_rig.Animation.IsDead;
            }
        }

        // ------------------------------------------------------------------ IWeaponHost

        public Transform WeaponMount => _rig != null ? _rig.WeaponMount : null;

        public void OnWeaponFired(float strength01, Vector3 worldDirection)
        {
            PlayerProceduralAnimator animation = _rig != null ? _rig.Animation : null;
            if (animation == null)
            {
                return;
            }

            animation.PlayFireRecoil(strength01, worldDirection);
        }

        public void OnReloadStarted(float duration)
        {
            PlayerProceduralAnimator animation = _rig != null ? _rig.Animation : null;
            if (animation == null)
            {
                return;
            }

            animation.PlayReload(duration);
        }

        public void OnReloadFinished(bool completed)
        {
            PlayerProceduralAnimator animation = _rig != null ? _rig.Animation : null;
            if (animation == null)
            {
                return;
            }

            // The animator drives its own pose out over the duration it was given, so a
            // completed reload needs no call. An interrupted one does, or the arms stay
            // down while the rotary cannon is already firing.
            if (!completed)
            {
                animation.CancelReload();
            }
        }
    }
}
