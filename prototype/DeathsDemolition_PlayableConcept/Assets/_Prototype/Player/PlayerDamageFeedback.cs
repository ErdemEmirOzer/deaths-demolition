using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Turns a hit on the player into feedback. REQ-045: "screen edge, a short sound
    /// and a small camera response - do not interrupt control".
    ///
    /// This component owns the two halves that live on the player itself:
    ///   * the procedural flinch, which is pose only, and
    ///   * a small directional camera bump, deliberately the gentlest of the four shake
    ///     profiles, because it plays while the player is being surrounded and must not
    ///     make the fight unreadable.
    ///
    /// The other two halves belong to other tasks and hang off the same events:
    ///   * the screen-edge vignette  -> HUD task, PlayerHealth.AnyDamaged
    ///   * the damage sound          -> audio task, PlayerHealth.AnyDamaged
    ///
    /// Nothing in this class can touch input, movement or firing. That is the whole
    /// point: there is no code path from taking damage to losing control.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerDamageFeedback : PrototypeConfigBehaviour
    {
        [PrototypeOnly(PendingDecisions.None,
            "Camera bump amplitude in metres for a hit worth a quarter of max health.", true)]
        [SerializeField] private float _bumpAmplitude = 0.09f;

        [PrototypeOnly(PendingDecisions.None, "Camera bump duration in seconds.", true)]
        [SerializeField] private float _bumpDuration = 0.25f;

        [PrototypeOnly(PendingDecisions.None,
            "Damage as a fraction of max health that counts as a full-strength reaction.", true)]
        [SerializeField] private float _fullReactionDamageFraction = 0.25f;

        [PrototypeOnly(PendingDecisions.None, "Camera shake played once on death.", true)]
        [SerializeField] private float _deathShakeAmplitude = 0.18f;

        private PlayerHealth _health;
        private PlayerProceduralAnimator _animator;

        private float _vignetteTime = 0.35f;
        private float _flash;

        /// <summary>
        /// 0..1 damage flash strength, decaying over PrototypeConfig.Feedback.DamageVignetteTime.
        /// The HUD may read this instead of running its own timer.
        /// </summary>
        public float FlashStrength01
        {
            get { return _flash; }
        }

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _animator = GetComponent<PlayerProceduralAnimator>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.Died += OnDied;
            }
        }

        protected override void OnDisable()
        {
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }

            base.OnDisable();
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            _vignetteTime = Mathf.Max(0.01f, config.Feedback.DamageVignetteTime);
        }

        private void Update()
        {
            if (_flash <= 0f)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            _flash = Mathf.Max(0f, _flash - dt / _vignetteTime);
        }

        private void OnDamaged(PlayerDamageInfo info)
        {
            float fraction = info.MaxHealth > 0f ? info.AppliedAmount / info.MaxHealth : 0f;
            float strength = Mathf.Clamp01(fraction / Mathf.Max(0.001f, _fullReactionDamageFraction));

            _flash = Mathf.Max(_flash, Mathf.Max(strength, 0.35f));

            if (_animator != null)
            {
                _animator.PlayDamageFlinch(strength, info.HitDirection);
            }

            // Bump is pushed along the direction the blow travelled, so being hit from
            // behind reads differently from being hit head on.
            CameraShakeService.Shake(
                _bumpAmplitude * Mathf.Max(0.35f, strength),
                _bumpDuration,
                ShakeKind.Bump,
                info.HitDirection);
        }

        private void OnDied(PlayerDeathInfo info)
        {
            _flash = 1f;
            CameraShakeService.Shake(_deathShakeAmplitude, 0.5f, ShakeKind.Rumble, info.KillingHitDirection);
        }
    }
}
