using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Characters;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Pseudo animation for the player, driven entirely by code. There is no animation
    /// asset and no Animator component: every pose is written straight onto the
    /// transforms of the primitive rig that PlayerRigFactory builds.
    ///
    /// STATES REQUIRED BY THE BRIEF, AND HOW EACH IS PRODUCED
    ///   idle           torso breathing plus a slow weapon sway
    ///   run            leg and arm swing, hip bob, body lean towards travel direction
    ///   aim rotation   the character root is turned by PlayerAim; this class adds the
    ///                  shoulder counter-twist so the weapon leads the turn
    ///   fire recoil    spring-damped kick, driven by the weapon task through
    ///                  PlayFireRecoil
    ///   reload         a timed two-phase arm movement, driven through PlayReload
    ///   damage         a short flinch plus a colour flash - NEVER a control lock
    ///                  (REQ-045), this class cannot touch input or movement
    ///   death          a one-way collapse
    ///
    /// LEGIBILITY OVER FIDELITY. Every motion is separable: the run cycle keeps playing
    /// through a recoil, a reload and a flinch, because in this game the player is
    /// always doing several of those at once.
    ///
    /// WHAT THE WEAPON TASK CALLS
    ///     animator.PlayFireRecoil(0.6f, muzzleForward);
    ///     animator.PlayReload(1.5f);
    ///     animator.CancelReload();
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProceduralAnimator : PrototypeConfigBehaviour
    {
        [Header("Rig")]
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private Transform _hips;
        [SerializeField] private Transform _torso;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _armLeft;
        [SerializeField] private Transform _armRight;
        [SerializeField] private Transform _legLeft;
        [SerializeField] private Transform _legRight;
        [SerializeField] private Transform _weaponMount;

        [Header("Locomotion")]
        [PrototypeOnly(PendingDecisions.None, "Leg swing at full speed, degrees.", true)]
        [SerializeField] private float _legSwingDegrees = 38f;

        [PrototypeOnly(PendingDecisions.None, "Arm swing at full speed, degrees. Lower than the legs; this arm holds a weapon.", true)]
        [SerializeField] private float _armSwingDegrees = 16f;

        [PrototypeOnly(PendingDecisions.None, "Step cycles per second at full speed.", true)]
        [SerializeField] private float _stepFrequency = 1.65f;

        [PrototypeOnly(PendingDecisions.None, "Hip rise per step, metres.", true)]
        [SerializeField] private float _hipBob = 0.055f;

        [PrototypeOnly(PendingDecisions.None, "Body lean into the travel direction at full speed, degrees.", true)]
        [SerializeField] private float _leanDegrees = 9f;

        [PrototypeOnly(PendingDecisions.None, "How fast the lean catches up, per second.", true)]
        [SerializeField] private float _leanResponse = 8f;

        [Header("Idle")]
        [PrototypeOnly(PendingDecisions.None, "Idle breathing amplitude, metres.", true)]
        [SerializeField] private float _breathAmplitude = 0.018f;

        [PrototypeOnly(PendingDecisions.None, "Idle breathing cycles per second.", true)]
        [SerializeField] private float _breathFrequency = 0.5f;

        [Header("Recoil")]
        [PrototypeOnly(PendingDecisions.None, "Weapon travel at recoil strength 1, metres.", true)]
        [SerializeField] private float _recoilKickDistance = 0.13f;

        [PrototypeOnly(PendingDecisions.None, "Torso pitch at recoil strength 1, degrees.", true)]
        [SerializeField] private float _recoilPitchDegrees = 7f;

        [PrototypeOnly(PendingDecisions.None, "Recoil spring stiffness. Higher snaps back faster.", true)]
        [SerializeField] private float _recoilStiffness = 190f;

        [PrototypeOnly(PendingDecisions.None, "Recoil spring damping. Near critical, so it does not wobble.", true)]
        [SerializeField] private float _recoilDamping = 26f;

        [Header("Reload")]
        [PrototypeOnly(PendingDecisions.None, "How far the weapon arm drops during a reload, degrees.", true)]
        [SerializeField] private float _reloadArmDropDegrees = 46f;

        [Header("Damage feedback")]
        [PrototypeOnly(PendingDecisions.None, "Flinch decay per second. Short by design: control is never interrupted.", true)]
        [SerializeField] private float _flinchDecay = 5.5f;

        [PrototypeOnly(PendingDecisions.None, "Torso displacement at flinch strength 1, metres.", true)]
        [SerializeField] private float _flinchDisplacement = 0.11f;

        [PrototypeOnly(PendingDecisions.None, "Colour the body flashes towards when hit.", true)]
        [SerializeField] private Color _damageFlashColor = new Color(1f, 0.32f, 0.28f, 1f);

        [Header("Death")]
        [PrototypeOnly(PendingDecisions.None, "Seconds the collapse takes.", true)]
        [SerializeField] private float _deathDuration = 0.75f;

        // ---------------------------------------------------------------- rest pose

        private Vector3 _hipsRest;
        private Vector3 _torsoRestPosition;
        private Quaternion _torsoRest;
        private Quaternion _headRest;
        private Quaternion _armLeftRest;
        private Quaternion _armRightRest;
        private Quaternion _legLeftRest;
        private Quaternion _legRightRest;
        private Vector3 _weaponRest;
        private Quaternion _visualRest;
        private bool _restCaptured;

        // -------------------------------------------------------------------- state

        private PlayerMotor _motor;

        private float _stepPhase;
        private float _breathPhase;
        private Vector2 _lean;
        private float _recoil;
        private float _recoilVelocity;
        private Vector3 _recoilDirectionLocal = Vector3.back;
        private float _reloadTimer;
        private float _reloadDuration;
        private float _flinch;
        private Vector3 _flinchDirectionLocal;
        private float _deathTimer;
        private bool _dying;

        private readonly List<Renderer> _bodyRenderers = new List<Renderer>(8);
        private readonly List<Color> _bodyBaseColors = new List<Color>(8);
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private bool _flashApplied;

        // Overwritten by ApplyConfig before the first frame; 1 only avoids a divide by zero.
        private float _maxSpeed = 1f;

        /// <summary>Where a weapon model should be parented.</summary>
        public Transform WeaponMount
        {
            get { return _weaponMount; }
        }

        /// <summary>True while the collapse is playing or finished.</summary>
        public bool IsDead
        {
            get { return _dying; }
        }

        /// <summary>0..1 reload progress; 0 when not reloading.</summary>
        public float ReloadProgress
        {
            get { return _reloadDuration > 0f ? Mathf.Clamp01(_reloadTimer / _reloadDuration) : 0f; }
        }

        public bool IsReloading
        {
            get { return _reloadDuration > 0f; }
        }

        /// <summary>Called by the factory to hand over the primitive rig.</summary>
        public void Bind(
            Transform visualRoot, Transform hips, Transform torso, Transform head,
            Transform armLeft, Transform armRight, Transform legLeft, Transform legRight,
            Transform weaponMount)
        {
            _visualRoot = visualRoot;
            _hips = hips;
            _torso = torso;
            _head = head;
            _armLeft = armLeft;
            _armRight = armRight;
            _legLeft = legLeft;
            _legRight = legRight;
            _weaponMount = weaponMount;
            _restCaptured = false;
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _propertyBlock = new MaterialPropertyBlock();
            CaptureRest();
            CollectRenderers();
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            _maxSpeed = Mathf.Max(0.01f, config.Player.MoveSpeed);
        }

        /// <summary>
        /// The speed the lean is measured against. The motor's own top speed is preferred
        /// because it already includes the run's MovementSpeed upgrade tier: normalising
        /// against the raw config value would peg an upgraded player's lean at full every
        /// time they simply walk. The cached config value stays as the motorless fallback.
        /// </summary>
        private float NormalisationSpeed
        {
            get { return _motor != null && _motor.MaxSpeed > 0.01f ? _motor.MaxSpeed : _maxSpeed; }
        }

        private void CaptureRest()
        {
            if (_restCaptured)
            {
                return;
            }

            _restCaptured = true;

            if (_hips != null) _hipsRest = _hips.localPosition;
            if (_torso != null)
            {
                _torsoRest = _torso.localRotation;
                _torsoRestPosition = _torso.localPosition;
            }

            if (_head != null) _headRest = _head.localRotation;
            if (_armLeft != null) _armLeftRest = _armLeft.localRotation;
            if (_armRight != null) _armRightRest = _armRight.localRotation;
            if (_legLeft != null) _legLeftRest = _legLeft.localRotation;
            if (_legRight != null) _legRightRest = _legRight.localRotation;
            if (_weaponMount != null) _weaponRest = _weaponMount.localPosition;
            if (_visualRoot != null) _visualRest = _visualRoot.localRotation;
        }

        private void CollectRenderers()
        {
            _bodyRenderers.Clear();
            _bodyBaseColors.Clear();

            if (_visualRoot == null)
            {
                return;
            }

            var renderers = _visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r is ParticleSystemRenderer)
                {
                    continue;
                }

                Material mat = r.sharedMaterial;
                Color color = Color.white;
                if (mat != null)
                {
                    if (mat.HasProperty(BaseColorId))
                    {
                        color = mat.GetColor(BaseColorId);
                    }
                    else if (mat.HasProperty(ColorId))
                    {
                        color = mat.GetColor(ColorId);
                    }
                }

                _bodyRenderers.Add(r);
                _bodyBaseColors.Add(color);
            }
        }

        // ------------------------------------------------------------- public hooks

        /// <summary>
        /// Weapon fired. <paramref name="strength01"/> scales the whole reaction, so a
        /// shotgun passes ~1 and an SMG ~0.25. <paramref name="worldDirection"/> is the
        /// direction the shot travelled; the kick is applied opposite to it.
        /// Additive: firing again mid-recoil stacks, which is what makes sustained fire
        /// climb.
        /// </summary>
        public void PlayFireRecoil(float strength01, Vector3 worldDirection)
        {
            if (_dying)
            {
                return;
            }

            if (_skinned != null)
            {
                // WP-018: the additive fire_recoil clip + WeaponVisualRig's own kick; no spring on the hidden rig.
                _skinned.PlayFire();
                return;
            }

            float strength = Mathf.Clamp(strength01, 0f, 3f);
            _recoilVelocity += strength * 9f;
            _recoil = Mathf.Min(_recoil + strength * 0.25f, 2.5f);

            if (worldDirection.sqrMagnitude > 1e-6f)
            {
                Vector3 local = transform.InverseTransformDirection(worldDirection.normalized);
                local.y *= 0.4f;
                _recoilDirectionLocal = -local.normalized;
            }
            else
            {
                _recoilDirectionLocal = Vector3.back;
            }
        }

        /// <summary>Convenience overload: kick straight back along the character's facing.</summary>
        public void PlayFireRecoil(float strength01)
        {
            PlayFireRecoil(strength01, transform.forward);
        }

        /// <summary>Starts the reload pose for the given number of seconds.</summary>
        public void PlayReload(float duration)
        {
            if (_dying || duration <= 0f || _skinned != null)
            {
                // WP-018: no reload clip in AP-009; WeaponVisualRig tips the weapon (PlayReloadMotion) on its own.
                return;
            }

            _reloadDuration = duration;
            _reloadTimer = 0f;
        }

        /// <summary>Aborts the reload pose, e.g. when a Special Weapon is picked up.</summary>
        public void CancelReload()
        {
            _reloadDuration = 0f;
            _reloadTimer = 0f;
        }

        /// <summary>
        /// Damage feedback. Visual only: it cannot and must not stop movement, aim or
        /// firing (REQ-045).
        /// </summary>
        public void PlayDamageFlinch(float strength01, Vector3 fromDirection)
        {
            if (_dying)
            {
                return;
            }

            _flinch = Mathf.Clamp01(Mathf.Max(_flinch, strength01));

            if (fromDirection.sqrMagnitude > 1e-6f)
            {
                Vector3 local = transform.InverseTransformDirection(fromDirection.normalized);
                local.y = 0f;
                _flinchDirectionLocal = local.sqrMagnitude > 1e-6f ? local.normalized : Vector3.back;
            }
            else
            {
                _flinchDirectionLocal = Vector3.back;
            }

            if (_skinned != null)
            {
                // WP-059 (AP-009C v003 hurt clips): a blow that travels BACK through the body
                // came from the front — the gut hit with a step (hurt_B, base layer) when it
                // is worth at least half a full reaction and the player is not running;
                // everything else (side, behind, light, on the move) is the upper-body
                // hurt_A over uninterrupted locomotion. Never a control lock (REQ-045).
                bool frontal = _flinchDirectionLocal.z < -0.2f && Mathf.Abs(_flinchDirectionLocal.z) >= Mathf.Abs(_flinchDirectionLocal.x);
                bool moving = _motor != null && _motor.PlanarVelocity.sqrMagnitude > HurtStepMaxSpeed * HurtStepMaxSpeed;
                LastHurtUpper = !frontal || moving || strength01 < HurtStepMinStrength;
                HurtCount++;
                _skinned.PlayHurt(LastHurtUpper);
            }
        }

        /// <summary>WP-059: below this reaction strength (0..1) a frontal hit stays an upper-body hurt.</summary>
        public const float HurtStepMinStrength = 0.5f;

        /// <summary>WP-059: above this planar speed (m/s) a frontal hit stays an upper-body hurt (the gut step would slide).</summary>
        public const float HurtStepMaxSpeed = 1.5f;

        /// <summary>True when the last hurt went to the upper-body clip (hurt_A), false for the full-body gut hit (hurt_B). Tests / F1.</summary>
        public bool LastHurtUpper { get; private set; } = true;

        /// <summary>Hurt clips requested since spawn (tests).</summary>
        public int HurtCount { get; private set; }

        /// <summary>One-way collapse. There is no revive, so there is no way back out.</summary>
        public void PlayDeath()
        {
            if (_dying)
            {
                return;
            }

            _dying = true;
            _deathTimer = 0f;
            CancelReload();

            if (_skinned != null)
            {
                // WP-018: death_fwd / death_back from where the last hit pushed the body; a coin for a side hit.
                _skinned.PlayDeath(_flinchDirectionLocal, (Time.frameCount & 1) == 0);
            }
        }

        /// <summary>WP-018: the AP-009 skinned body. When set, this class writes nothing to the block rig and forwards its hooks.</summary>
        public PlayerSkinnedVisual Skinned
        {
            get { return _skinned; }
            set { _skinned = value; }
        }

        private PlayerSkinnedVisual _skinned;

        // ------------------------------------------------------------------- update

        private void LateUpdate()
        {
            CaptureRest();

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            if (_skinned != null)
            {
                // WP-018: the Animator owns the pose; only the death clock survives here (IsDead readout).
                if (_dying) _deathTimer += dt;
                return;
            }

            if (_dying)
            {
                UpdateDeath(dt);
                return;
            }

            float speed01 = _motor != null ? _motor.Speed01 : 0f;

            UpdateLocomotion(dt, speed01);
            UpdateRecoil(dt);
            UpdateReload(dt);
            UpdateFlinch(dt);
        }

        private void UpdateLocomotion(float dt, float speed01)
        {
            _breathPhase += dt * _breathFrequency * Mathf.PI * 2f;
            _stepPhase += dt * _stepFrequency * Mathf.PI * 2f * Mathf.Max(speed01, 0.001f);

            if (_stepPhase > Mathf.PI * 4f)
            {
                _stepPhase -= Mathf.PI * 4f;
            }

            float swing = Mathf.Sin(_stepPhase);

            if (_legLeft != null)
            {
                _legLeft.localRotation = _legLeftRest * Quaternion.Euler(swing * _legSwingDegrees * speed01, 0f, 0f);
            }

            if (_legRight != null)
            {
                _legRight.localRotation = _legRightRest * Quaternion.Euler(-swing * _legSwingDegrees * speed01, 0f, 0f);
            }

            if (_armLeft != null)
            {
                _armLeft.localRotation = _armLeftRest * Quaternion.Euler(-swing * _armSwingDegrees * speed01, 0f, 0f);
            }

            // Lean towards the direction of travel, expressed in the character's own
            // axes so strafing reads as a side lean rather than a forward one.
            Vector3 localVelocity = Vector3.zero;
            if (_motor != null)
            {
                localVelocity = transform.InverseTransformDirection(_motor.PlanarVelocity) / NormalisationSpeed;
            }

            Vector2 targetLean = new Vector2(
                Mathf.Clamp(localVelocity.x, -1f, 1f),
                Mathf.Clamp(localVelocity.z, -1f, 1f));

            _lean = Vector2.Lerp(_lean, targetLean, 1f - Mathf.Exp(-_leanResponse * dt));

            if (_hips != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(_stepPhase)) * _hipBob * speed01;
                float breath = Mathf.Sin(_breathPhase) * _breathAmplitude * (1f - speed01);
                _hips.localPosition = _hipsRest + new Vector3(0f, bob + breath, 0f);
            }

            if (_torso != null)
            {
                float pitch = _lean.y * _leanDegrees - _recoil * _recoilPitchDegrees;
                float roll = -_lean.x * _leanDegrees;
                float yaw = Mathf.Sin(_stepPhase) * 3f * speed01;

                Vector3 flinchOffset = _flinchDirectionLocal * (_flinch * _flinchDisplacement);

                _torso.localRotation = _torsoRest * Quaternion.Euler(pitch, yaw, roll);
                _torso.localPosition = _torsoRestPosition + flinchOffset;
            }

            if (_head != null)
            {
                // The head stays level: it is the clearest read of where the player is.
                _head.localRotation = _headRest * Quaternion.Euler(-_lean.y * _leanDegrees * 0.5f, 0f, 0f);
            }
        }

        private void UpdateRecoil(float dt)
        {
            // Critically damped spring back to zero. Explicit integration with a fixed
            // stiffness / damping pair gives the same settle time at any frame rate.
            float acceleration = -_recoilStiffness * _recoil - _recoilDamping * _recoilVelocity;
            _recoilVelocity += acceleration * dt;
            _recoil += _recoilVelocity * dt;

            if (Mathf.Abs(_recoil) < 0.0005f && Mathf.Abs(_recoilVelocity) < 0.0005f)
            {
                _recoil = 0f;
                _recoilVelocity = 0f;
            }

            if (_weaponMount == null)
            {
                return;
            }

            Vector3 kick = _recoilDirectionLocal * (_recoil * _recoilKickDistance);
            _weaponMount.localPosition = _weaponRest + kick;
        }

        private void UpdateReload(float dt)
        {
            if (_reloadDuration <= 0f)
            {
                if (_armRight != null)
                {
                    _armRight.localRotation = Quaternion.Slerp(
                        _armRight.localRotation, _armRightRest, 1f - Mathf.Exp(-14f * dt));
                }

                return;
            }

            _reloadTimer += dt;

            float u = Mathf.Clamp01(_reloadTimer / _reloadDuration);

            // Down fast, hold, back up: the shape reads as "magazine out, magazine in"
            // even on a capsule arm.
            float drop = u < 0.25f
                ? Mathf.SmoothStep(0f, 1f, u / 0.25f)
                : u < 0.75f
                    ? 1f
                    : Mathf.SmoothStep(1f, 0f, (u - 0.75f) / 0.25f);

            if (_armRight != null)
            {
                _armRight.localRotation = _armRightRest * Quaternion.Euler(drop * _reloadArmDropDegrees, 0f, 0f);
            }

            if (_reloadTimer >= _reloadDuration)
            {
                CancelReload();
            }
        }

        private void UpdateFlinch(float dt)
        {
            if (_flinch <= 0f)
            {
                if (_flashApplied)
                {
                    WriteBodyColor(0f);
                    _flashApplied = false;
                }

                return;
            }

            _flinch = Mathf.Max(0f, _flinch - _flinchDecay * dt);
            WriteBodyColor(_flinch);
            _flashApplied = true;
        }

        private void WriteBodyColor(float flash)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            for (int i = 0; i < _bodyRenderers.Count; i++)
            {
                Renderer r = _bodyRenderers[i];
                if (r == null)
                {
                    continue;
                }

                Color color = Color.Lerp(_bodyBaseColors[i], _damageFlashColor, Mathf.Clamp01(flash));

                r.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                _propertyBlock.SetColor(ColorId, color);
                r.SetPropertyBlock(_propertyBlock);
            }
        }

        private void UpdateDeath(float dt)
        {
            if (_visualRoot == null)
            {
                return;
            }

            _deathTimer += dt;
            float u = Mathf.Clamp01(_deathTimer / Mathf.Max(0.01f, _deathDuration));
            float eased = Mathf.SmoothStep(0f, 1f, u);

            _visualRoot.localRotation = _visualRest * Quaternion.Euler(eased * 88f, 0f, eased * 14f);
            _visualRoot.localPosition = new Vector3(0f, -eased * 0.25f, 0f);

            if (_weaponMount != null)
            {
                _weaponMount.localPosition = _weaponRest;
            }
        }
    }
}
