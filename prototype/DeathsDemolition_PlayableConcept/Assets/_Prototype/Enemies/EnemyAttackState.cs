using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// The attack state machine - REQ-043 / DEC-029.
    ///
    /// THE RULE THIS FILE EXISTS TO ENFORCE
    /// Touching a zombie does nothing. Damage happens if and only if the ACTIVE window of a
    /// visible attack animation overlaps the player. There is no OnCollisionStay damage,
    /// no proximity tick, no contact damage anywhere in this codebase - grep for
    /// TryConnect and you will find the only place damage can leave an enemy.
    ///
    /// The three phases are driven by the same normalised clock as the pose, so what the
    /// player sees and what the game measures cannot drift apart:
    ///
    ///   Windup    TelegraphTime        pose winds up, hitbox OFF   - readable warning
    ///   Active    AttackActiveTime     pose strikes,  hitbox ON    - one overlap test
    ///   Recovery  AttackRecoveryTime   pose settles,  hitbox OFF   - punish window
    ///
    /// A stagger during Windup cancels the whole attack, which is what makes shotgunning a
    /// winding-up Guard feel like an answer rather than a coincidence.
    /// </summary>
    public sealed class EnemyAttackState
    {
        // Shared across all enemies: the overlap test is synchronous and never re-entrant.
        private static readonly Collider[] s_overlapBuffer = new Collider[8];

        // ------------------------------------- fallbacks used before ApplyConfig arrives
        // Each one mirrors the EnemyTierSettings default for Standard EXACTLY. They only
        // apply when no PrototypeConfig is supplied (headless test, scene opened without
        // Boot); a silent disagreement here would move the fight without moving the config.

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.TelegraphTime. Request value: Standard telegraph 0.55 s.")]
        private const float DefaultTelegraph = 0.55f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.AttackActiveTime. Seconds the hitbox is live.", true)]
        private const float DefaultActive = 0.12f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.AttackRecoveryTime - the punish window.", true)]
        private const float DefaultRecovery = 0.45f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.AttackRange. Distance at which a swing is committed.", true)]
        private const float DefaultRange = 1.7f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.AttackCooldown.", true)]
        private const float DefaultCooldown = 1.5f;

        [PrototypeOnly(PendingDecisions.CombatScaling,
            "Fallback for EnemyTierSettings.AttackDamage. Request value: Standard attack damage 10.")]
        private const float DefaultDamage = 10f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback overlap radius of the attack hitbox before a tier is resolved.", true)]
        private const float DefaultHitRadius = 0.75f;

        // ------------------------------------------------- attack shape (all invented)
        // The request gives no attack shapes or hit radii. Radius is expressed as a
        // multiple of the tier's BodyRadius so a bigger silhouette really does reach
        // further - that is the readability contract the player is judging.

        [PrototypeOnly(PendingDecisions.None, "Runner lunge hit radius, x EnemyTierSettings.BodyRadius.", true)]
        private const float LungeHitRadiusFactor = 1.9f;

        [PrototypeOnly(PendingDecisions.CombatScaling,
            "Runner lunge speed during the active window, x EnemyTierSettings.MoveSpeed - which " +
            "is itself a CombatScaling value. The request says 'physical lunge attack' and " +
            "names no speed.", true)]
        private const float LungeSpeedFactor = 1.85f;

        [PrototypeOnly(PendingDecisions.None, "Guard sweep hit radius, x BodyRadius. Request: 'wide'.", true)]
        private const float SweepHitRadiusFactor = 2.1f;

        [PrototypeOnly(PendingDecisions.None, "Executioner overhead hit radius, x BodyRadius.", true)]
        private const float OverheadHitRadiusFactor = 1.8f;

        [PrototypeOnly(PendingDecisions.None, "Standard swipe hit radius, x BodyRadius.", true)]
        private const float SwipeHitRadiusFactor = 1.7f;

        [PrototypeOnly(PendingDecisions.None,
            "Share of the cooldown a CANCELLED swing still costs. Without it, staggering an " +
            "enemy would let it re-swing the instant it recovers.", true)]
        private const float CancelledSwingCooldownShare = 0.5f;

        private readonly ZombieRig _rig;

        private ZombieAttackPhase _phase = ZombieAttackPhase.None;
        private float _phaseTime;
        private float _cooldownRemaining;
        private bool _connectedThisSwing;

        // Cached per-tier data, refreshed by ApplyConfig.
        private ZombieAttackStyle _style = ZombieAttackStyle.Swipe;
        private float _versionTelegraphScale = 1f, _versionRecoveryScale = 1f;   // WP-028
        private float _telegraph = DefaultTelegraph;
        private float _active = DefaultActive;
        private float _recovery = DefaultRecovery;
        private float _range = DefaultRange;
        private float _cooldown = DefaultCooldown;
        private float _damage = DefaultDamage;
        private float _hitRadius = DefaultHitRadius;
        private float _lungeSpeed;

        public EnemyAttackState(ZombieRig rig)
        {
            _rig = rig;
        }

        public ZombieAttackPhase Phase => _phase;
        public ZombieAttackStyle Style => _style;
        public bool IsAttacking => _phase != ZombieAttackPhase.None;

        /// <summary>Windup only. The window where a stagger is worth spending a shell on.</summary>
        public bool IsTelegraphing => _phase == ZombieAttackPhase.Windup;

        /// <summary>Distance at which the enemy commits to a swing.</summary>
        public float Range => _range;

        /// <summary>Scaled damage of one connecting hit, for the debug readout.</summary>
        public float Damage => _damage;

        /// <summary>
        /// Forward speed the body should carry this frame because of the attack itself.
        /// Only the Runner's lunge produces a non-zero value. Read by ZombieEnemy so the
        /// movement stays in one place.
        /// </summary>
        public float ForwardImpulse { get; private set; }

        /// <summary>Normalised progress through the current phase, for the pose.</summary>
        public float PhaseProgress
        {
            get
            {
                float duration = CurrentPhaseDuration();
                return duration <= 0f ? 1f : Mathf.Clamp01(_phaseTime / duration);
            }
        }

        // ==================================================================== config

        /// <summary>
        /// Re-read everything from the live config. Called from ZombieEnemy.ApplyConfig, so
        /// an F1 panel edit to a telegraph time lands on enemies already on the map.
        /// </summary>
        /// <summary>
        /// WP-028 (ADR-028-1): the version's wind-up / recovery emphasis on top of the tier timer
        /// (Special B: shorter telegraph, longer recovery). Called right after <see cref="ApplyConfig"/>
        /// on every config read; the active window (DEC-029 hitbox) is never scaled. Scales of 1
        /// leave the WP-002 timers exactly as they were.
        /// </summary>
        public void ApplyVersionTiming(float telegraphScale, float recoveryScale)
        {
            float tScale = Mathf.Max(0.1f, telegraphScale), rScale = Mathf.Max(0.1f, recoveryScale);
            // undo a previous scale (ApplyConfig resets to 1) then apply the new one, so repeated calls do not compound
            _telegraph = Mathf.Max(0.02f, _telegraph / _versionTelegraphScale * tScale);
            _recovery = Mathf.Max(0.02f, _recovery / _versionRecoveryScale * rScale);
            _versionTelegraphScale = tScale;
            _versionRecoveryScale = rScale;
        }

        public float TelegraphSeconds => _telegraph;
        public float ActiveSeconds => _active;
        public float RecoverySeconds => _recovery;

        public void ApplyConfig(PrototypeConfig config, EnemyTier tier, int round)
        {
            if (config == null)
            {
                return;
            }

            EnemyTierSettings settings = config.Enemies.Find(tier);
            if (settings == null)
            {
                return;
            }

            _telegraph = Mathf.Max(0.02f, settings.TelegraphTime);
            _active = Mathf.Max(0.02f, settings.AttackActiveTime);
            _recovery = Mathf.Max(0.02f, settings.AttackRecoveryTime);
            _versionTelegraphScale = 1f;
            _versionRecoveryScale = 1f;
            _range = Mathf.Max(0.2f, settings.AttackRange);
            _cooldown = Mathf.Max(0f, settings.AttackCooldown);

            // Scaled by the round damage multiplier through the config's own method.
            _damage = config.EnemyAttackDamage(tier, round);

            // PROTOTYPE-ONLY / INVENTED: see the *HitRadiusFactor constants above.
            switch (tier)
            {
                case EnemyTier.Special:
                    _style = ZombieAttackStyle.Lunge;
                    _hitRadius = settings.BodyRadius * LungeHitRadiusFactor;
                    _lungeSpeed = settings.MoveSpeed * LungeSpeedFactor;
                    break;
                case EnemyTier.Elite:
                    _style = ZombieAttackStyle.Sweep;
                    _hitRadius = settings.BodyRadius * SweepHitRadiusFactor;
                    _lungeSpeed = 0f;
                    break;
                case EnemyTier.Ultra:
                    _style = ZombieAttackStyle.Overhead;
                    _hitRadius = settings.BodyRadius * OverheadHitRadiusFactor;
                    _lungeSpeed = 0f;
                    break;
                default:
                    _style = ZombieAttackStyle.Swipe;
                    _hitRadius = settings.BodyRadius * SwipeHitRadiusFactor;
                    _lungeSpeed = 0f;
                    break;
            }
        }

        // ================================================================= lifecycle

        public void ResetForSpawn()
        {
            _phase = ZombieAttackPhase.None;
            _phaseTime = 0f;
            _cooldownRemaining = 0f;
            _connectedThisSwing = false;
            ForwardImpulse = 0f;
        }

        /// <summary>Cancel mid-attack. Called by a stagger and by death.</summary>
        public void Cancel()
        {
            if (_phase == ZombieAttackPhase.None)
            {
                return;
            }

            _phase = ZombieAttackPhase.None;
            _phaseTime = 0f;
            _connectedThisSwing = false;
            ForwardImpulse = 0f;

            // A cancelled swing still costs the cooldown, otherwise staggering an enemy
            // would let it re-swing instantly the moment it recovers.
            _cooldownRemaining = Mathf.Max(_cooldownRemaining, _cooldown * CancelledSwingCooldownShare);
        }

        public bool CanStartAttack(float distanceToTarget)
        {
            return _phase == ZombieAttackPhase.None
                   && _cooldownRemaining <= 0f
                   && distanceToTarget <= _range;
        }

        public void Begin()
        {
            _phase = ZombieAttackPhase.Windup;
            _phaseTime = 0f;
            _connectedThisSwing = false;
            ForwardImpulse = 0f;
        }

        // ====================================================================== tick

        /// <summary>
        /// Advance the machine. Returns true on the frame the active window connected.
        /// </summary>
        public bool Tick(float dt, ZombieEnemy owner, EnemyTier tier, ZombieAnimator animator)
        {
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= dt;
            }

            ForwardImpulse = 0f;

            if (_phase == ZombieAttackPhase.None)
            {
                animator?.ClearAttack();
                return false;
            }

            _phaseTime += dt;
            bool connected = false;

            switch (_phase)
            {
                case ZombieAttackPhase.Windup:
                    if (_phaseTime >= _telegraph)
                    {
                        _phaseTime -= _telegraph;
                        _phase = ZombieAttackPhase.Active;
                    }

                    break;

                case ZombieAttackPhase.Active:
                    // The lunge carries the body forward for exactly as long as the hitbox
                    // is live, so the visible dash and the damage window are the same thing.
                    ForwardImpulse = _lungeSpeed;

                    if (!_connectedThisSwing)
                    {
                        connected = TryConnect(owner, tier);
                    }

                    if (_phaseTime >= _active)
                    {
                        _phaseTime -= _active;
                        _phase = ZombieAttackPhase.Recovery;
                    }

                    break;

                case ZombieAttackPhase.Recovery:
                    if (_phaseTime >= _recovery)
                    {
                        _phase = ZombieAttackPhase.None;
                        _phaseTime = 0f;
                        _cooldownRemaining = _cooldown;
                    }

                    break;
            }

            animator?.SetAttack(_style, _phase, PhaseProgress);
            return connected;
        }

        private float CurrentPhaseDuration()
        {
            switch (_phase)
            {
                case ZombieAttackPhase.Windup: return _telegraph;
                case ZombieAttackPhase.Active: return _active;
                case ZombieAttackPhase.Recovery: return _recovery;
                default: return 0f;
            }
        }

        // =================================================================== the hit

        /// <summary>
        /// The ONLY place an enemy can damage the player.
        ///
        /// An explicit overlap test rather than trigger callbacks: OnTriggerEnter would miss
        /// a player who was already inside the volume when the collider switched on, and it
        /// would run at physics rate instead of at the animation's active window. Here the
        /// test happens on the exact frames the strike is live, and only then.
        ///
        /// PLAYER CONTRACT - three wirings, tried in this order, FIRST ONE WINS:
        ///   A. The player registers its own collider with
        ///        HitboxRegistry.Register(collider, playerReceiver, null, BodyRegion.UpperBody, playerRoot)
        ///      and this method applies the damage directly through IDamageReceiver.
        ///   B. EnemyPlayerDamageBridge resolves PlayerHealth from the collider with
        ///      GetComponentInParent and calls ApplyDamage. This is the wiring the shipped
        ///      player rig actually uses, and it needs nothing from the player module.
        ///   C. A listener on DamageEvents.PlayerAttacked applies info.Damage itself when
        ///      info.DamageApplied is false.
        /// Doing more than one would double-apply, so the event reports which path was
        /// taken and C only ever fires when both A and B found nobody.
        ///
        /// WHY B EXISTS AND WHAT ITS ABSENCE COST (G-15)
        /// -------------------------------------------------------------------------------
        /// A and C were both written and neither was ever executed. The player rig is one
        /// CharacterController on layer Player and it registers with nothing, so
        /// HitboxRegistry.TryResolve returned false on every single hit; DamageEvents
        /// .PlayerAttacked had zero subscribers in the whole project. EnemyPlayerDamageBridge
        /// - the file whose own header calls itself "the ONLY file in Enemies/ that touches
        /// DD.Prototype.PlayerSystem" - had zero callers.
        ///
        /// Measured on 2026-08-18, player standing still inside 6 Standard zombies for 12 s
        /// at a fixed 1/60 step:
        ///     nearest planar distance   0.929 m   (AttackRange 1.70 - in range)
        ///     windup / active / recovery frames   960 / 240 / 672   (state machine running)
        ///     active frames whose overlap found the player   240 / 240   (hitbox connecting)
        ///     DamageEvents.PlayerAttacked raised   30 times, carrying 300 damage
        ///     of those, DamageApplied == true      0
        ///     PlayerHealth.Damaged raised          0
        ///     player health                        100.0 -> 100.0
        /// Three hundred points of damage were computed, published and dropped. The break
        /// was never in the attack, the hitbox or the layer mask - it was the last handover.
        /// EnemyDamagesPlayerTests is the sentry for that handover.
        /// </summary>
        private bool TryConnect(ZombieEnemy owner, EnemyTier tier)
        {
            if (_rig == null || _rig.AttackSocket == null || owner == null)
            {
                return false;
            }

            Vector3 origin = _rig.AttackSocket.position;

            int count = Physics.OverlapSphereNonAlloc(
                origin, _hitRadius, s_overlapBuffer,
                PrototypeLayers.EnemyAttackMask,
                QueryTriggerInteraction.Collide);

            if (count <= 0)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                Collider hit = s_overlapBuffer[i];
                if (hit == null)
                {
                    continue;
                }

                _connectedThisSwing = true;

                Vector3 point = hit.ClosestPoint(origin);
                Vector3 direction = point - owner.transform.position;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : owner.transform.forward;

                bool applied = false;
                if (HitboxRegistry.TryResolve(hit, out IDamageReceiver receiver, out _))
                {
                    var damageHit = new DamageHit
                    {
                        Amount = _damage,
                        Region = BodyRegion.UpperBody,
                        Point = point,
                        Direction = direction,
                        ImpactForce = 1f,
                        Source = DamageSourceKind.Bullet
                    };

                    receiver.ApplyHit(in damageHit);
                    applied = true;
                }
                else
                {
                    // Wiring B. Nothing about PlayerHealth crosses into this file: the
                    // bridge owns that reference so Enemies/ keeps exactly one edge into
                    // the player module. No cooldown, no i-frame, no per-source throttle
                    // is added here or there - REQ-044 wants two same-frame swings to both
                    // land, and the only gate in this path is _connectedThisSwing, which
                    // is per swing of THIS enemy, never per player and never per second.
                    applied = EnemyPlayerDamageBridge.TryApply(
                        hit, _damage, owner.transform.position, SourceId(tier), out bool resolvedPlayer);

                    if (resolvedPlayer && !applied)
                    {
                        // The swing connected with a player who refused the hit: dead (the run
                        // is over, REQ-048) or under the diagnosis flag. The swing is still
                        // spent, but there is no damage to publish - a listener must not be
                        // told "applied" for a corpse, nor invited to apply it itself.
                        return true;
                    }
                }

                DamageEvents.RaisePlayerAttacked(new EnemyAttackLandedInfo(
                    owner.transform, tier, _damage, point, direction, applied));

                return true;
            }

            return false;
        }

        /// <summary>
        /// Kill-attribution string handed to PlayerHealth, e.g. "Zombie.Ultra.Overhead".
        /// A fixed table rather than string concatenation, because the run-results screen
        /// reads this on death and the hit path must not allocate while an Ultra is
        /// swinging into a crowd. Tier maps to exactly one style, so four entries cover it.
        /// </summary>
        private static string SourceId(EnemyTier tier)
        {
            switch (tier)
            {
                case EnemyTier.Special: return "Zombie.Special.Lunge";
                case EnemyTier.Elite: return "Zombie.Elite.Sweep";
                case EnemyTier.Ultra: return "Zombie.Ultra.Overhead";
                default: return "Zombie.Standard.Swipe";
            }
        }

        /// <summary>Debug gizmo data: where the active window will test, and how wide.</summary>
        public void GetHitVolume(out Vector3 centre, out float radius)
        {
            centre = _rig != null && _rig.AttackSocket != null ? _rig.AttackSocket.position : Vector3.zero;
            radius = _hitRadius;
        }
    }
}
