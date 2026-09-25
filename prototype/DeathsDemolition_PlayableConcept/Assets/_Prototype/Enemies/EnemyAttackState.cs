using UnityEngine;
using DD.Prototype.Characters;
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
    /// WP-018 (ADR-018-1): with an AP-009 clip driving the attack the Windup→Active and
    /// Active→Recovery edges come from the clip's own <c>hit_window_start</c> /
    /// <c>hit_window_end</c> AnimationEvents (<see cref="IAttackWindowSink"/>), not from the
    /// timers — the hitbox is live exactly when the strike is drawn. The clip is rate-scaled so
    /// its window start lands on TelegraphTime, so the numbers below stay the source of truth;
    /// the timers remain as the safety net (event never arrives) and on the block rig.
    ///
    /// A stagger during Windup cancels the whole attack, which is what makes shotgunning a
    /// winding-up Guard feel like an answer rather than a coincidence.
    /// </summary>
    public sealed class EnemyAttackState : IAttackWindowSink
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

        // ------------------------------------------------ WP-076 · the current swing (style-scaled from the tier base)
        private EnemyTier _tier = EnemyTier.Standard;
        private float _bodyRadius = 0.45f, _moveSpeed = 1f;
        private float _swingTelegraph = DefaultTelegraph, _swingDamage = DefaultDamage, _swingCooldown = DefaultCooldown;
        private bool _slotHeld, _swipeMirror, _committed;
        private int _lastStyle = -1, _secondLastStyle = -1;
        private bool _lastSwipeMirrored;
        private float _grabRemaining = -1f;
        private Collider _grabCollider;
        private ZombieEnemy _grabOwner;

        // ---------------------------------------------------- WP-018 animation window
        [PrototypeOnly(PendingDecisions.None,
            "Seconds past the tier telegraph after which a clip-driven Windup opens the window " +
            "by timer because hit_window_start never arrived (clip missing, Animator off). Safety net.", true)]
        private const float WindowStartGraceSeconds = 0.5f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds past the tier active time after which a clip-driven Active closes by timer " +
            "because hit_window_end never arrived. Safety net.", true)]
        private const float WindowEndGraceSeconds = 0.5f;

        private bool _animationDriven;
        private static int s_windowFallbackCount;
        private static int s_windowEventCount;

        /// <summary>True while an AP-009 clip owns the hit window (DEC-029 through AnimationEvents).</summary>
        public bool AnimationDriven => _animationDriven;

        /// <summary>Windows opened/closed by the timer safety net instead of the clip (tests / probe; expected 0).</summary>
        public static int WindowFallbackCount => s_windowFallbackCount;

        /// <summary>hit_window_* events consumed (tests / probe).</summary>
        public static int WindowEventCount => s_windowEventCount;

        /// <summary>Time.frameCount of the last frame the active window connected (tests; -1 = never).</summary>
        public int LastConnectFrame { get; private set; } = -1;

        /// <summary>WP-032 probe readouts: overlap tests run / overlaps that found a collider (all enemies, since ResetStatics).</summary>
        public static int OverlapTests { get; private set; }
        public static int OverlapHits { get; private set; }


        /// <summary>Frames on which the overlap test ran this swing (tests).</summary>
        public int ActiveFramesThisSwing { get; private set; }

        public static void ResetStatics()
        {
            AttackSlotBoard.ResetStatics();   // WP-076
            GrabState.ResetStatics();
            s_windowFallbackCount = 0;
            s_windowEventCount = 0;
            OverlapTests = 0;
            OverlapHits = 0;
        }

        /// <summary>WP-018: hand the window to the clip (true) or back to the timers (false, block rig).</summary>
        public void SetAnimationDriven(bool driven)
        {
            _animationDriven = driven;
        }

        void IAttackWindowSink.OnHitWindowStart()
        {
            s_windowEventCount++;

            if (!_animationDriven || _phase != ZombieAttackPhase.Windup)
            {
                return;
            }

            _phase = ZombieAttackPhase.Active;
            _phaseTime = 0f;
            if (_slotHeld && !_committed) { AttackSlotBoard.Commit(_style, Time.time); _committed = true; }   // WP-076
        }

        void IAttackWindowSink.OnHitWindowEnd()
        {
            s_windowEventCount++;
            if (!_animationDriven || _phase != ZombieAttackPhase.Active)
            {
                return;
            }

            _phase = ZombieAttackPhase.Recovery;
            _phaseTime = 0f;
        }

        public EnemyAttackState(ZombieRig rig)
        {
            _rig = rig;
        }

        public ZombieAttackPhase Phase => _phase;
        public ZombieAttackStyle Style => _style;
        /// <summary>WP-076: the current Swipe uses the left hand (swipe_L); S-06 plays the mirror.</summary>
        public bool SwipeMirror => _swipeMirror;
        /// <summary>WP-076: this body holds the player (bite_grab, §1.4).</summary>
        public bool IsGrabbing => _grabRemaining >= 0f;
        /// <summary>WP-076: this swing's telegraph / damage (the tier values are <see cref="TelegraphSeconds"/> / <see cref="Damage"/>).</summary>
        public float SwingTelegraphSeconds => _swingTelegraph;
        public float SwingDamage => _swingDamage;
        public int LastStyleIndex => _lastStyle;
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

        // WP-076 · AP-022 clip events (S-06 WP-071 d9bc62ff): attack_step moves the body by the style's root step and
        // replaces the timer dash for that swing; grab_end ends the hold (second bite share). Timers stay the fallback.
        [PrototypeOnly(PendingDecisions.None, "WP-076 §1 root step per style on attack_step (m): Swipe, Lunge, Sweep, Overhead, BiteGrab; the Standard's lunge ×0.8.", true)]
        public static readonly float[] StepMetres = { 0.35f, 1.4f, 0.25f, 0.5f, 0.25f };
        public const float StandardLungeStepScale = 0.8f;
        private bool _clipStepThisSwing;

        /// <summary>The clip's attack_step for the current swing: the metres the body steps (0 = no swing / already stepped).</summary>
        public float TakeClipStep(EnemyTier tier)
        {
            if (_phase == ZombieAttackPhase.None || _clipStepThisSwing) return 0f;
            _clipStepThisSwing = true;
            int i = Mathf.Clamp((int)_style, 0, StepMetres.Length - 1);
            float m = StepMetres[i];
            if (_style == ZombieAttackStyle.Lunge && tier == EnemyTier.Standard) m *= StandardLungeStepScale;
            return m;
        }

        /// <summary>The clip's grab_end: the hold ends now (the timer is the fallback); the next Tick pays the second share.</summary>
        public void OnClipGrabEnd()
        {
            if (_grabRemaining > 0f) _grabRemaining = 0.0001f;
        }

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

        /// <summary>
        /// WP-040: the version's cooldown floor (Special B: the ≥ 1.9 s Lunge band) on top of the
        /// DEC-021 cooldown. Like the timing scales it is re-applied after every ApplyConfig
        /// (which resets to the table value), and it can only lengthen.
        /// </summary>
        public void ApplyVersionCooldownFloor(float floorSeconds)
        {
            _cooldown = Mathf.Max(_cooldown, Mathf.Max(0f, floorSeconds));
        }

        public float TelegraphSeconds => _telegraph;
        public float ActiveSeconds => _active;
        public float RecoverySeconds => _recovery;
        public float CooldownSeconds => _cooldown;
        /// <summary>WP-040: true once this swing's active window connected (the beat layer tells a hit from a miss with it).</summary>
        public bool ConnectedThisSwing => _connectedThisSwing;

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
            _tier = tier;
            _bodyRadius = settings.BodyRadius;
            _moveSpeed = settings.MoveSpeed;

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
            ReleaseSwing(GrabEndReason.Killed);
            _phase = ZombieAttackPhase.None;
            _phaseTime = 0f;
            _cooldownRemaining = 0f;
            _connectedThisSwing = false;
            ForwardImpulse = 0f;
            _lastStyle = -1; _secondLastStyle = -1; _lastSwipeMirrored = false;
        }

        /// <summary>Cancel mid-attack. Called by a stagger and by death.</summary>
        public void Cancel() => Cancel(GrabEndReason.Staggered);

        /// <summary>WP-076: cancel with the reason a held grab ends for (death = Killed).</summary>
        public void Cancel(GrabEndReason reason)
        {
            if (_phase == ZombieAttackPhase.None)
            {
                ReleaseSwing(reason);
                return;
            }

            ReleaseSwing(reason);

            _phase = ZombieAttackPhase.None;
            _phaseTime = 0f;
            _connectedThisSwing = false;
            ForwardImpulse = 0f;

            // A cancelled swing still costs the cooldown, otherwise staggering an enemy
            // would let it re-swing instantly the moment it recovers.
            _cooldownRemaining = Mathf.Max(_cooldownRemaining, _swingCooldown * CancelledSwingCooldownShare);
        }

        /// <summary>WP-076: gives back the slot and ends a held grab (no second bite share). Idempotent.</summary>
        private void ReleaseSwing(GrabEndReason reason)
        {
            if (_slotHeld)
            {
                AttackSlotBoard.Release(_style, _phase == ZombieAttackPhase.Windup);
                _slotHeld = false;
            }

            if (_grabRemaining >= 0f)
            {
                _grabRemaining = -1f;
                if (_grabOwner != null) GrabState.End(_grabOwner.LifeId, reason);
                _grabCollider = null; _grabOwner = null;
            }
        }

        /// <summary>WP-076 §1.4: a single hit ≥ <see cref="GrabState.EscapeImpulse"/> on the holding body frees the player — the second bite share is lost, the swing ends.</summary>
        public bool TryEscapeGrab(float impulse)
        {
            if (_grabRemaining < 0f || impulse < GrabState.EscapeImpulse) return false;
            ReleaseSwing(GrabEndReason.Escaped);
            _phase = ZombieAttackPhase.None;
            _phaseTime = 0f;
            ForwardImpulse = 0f;
            _cooldownRemaining = _swingCooldown;
            return true;
        }

        public bool CanStartAttack(float distanceToTarget)
        {
            return _phase == ZombieAttackPhase.None
                   && _cooldownRemaining <= 0f
                   && distanceToTarget <= _range;
        }

        /// <summary>WP-040 surface: the tier's signature style (used when the v3 selector is off and by old callers).</summary>
        public void Begin() => Begin(AttackStyleProfile.Signature(_tier), false, false);

        /// <summary>
        /// WP-076 §1.3: choose this swing (tier / distance / angle / variety / crowd slots) and start it. False = nothing
        /// may swing now (cooldown, busy, out of reach or every slot taken) — the body keeps pressing.
        /// </summary>
        public bool TrySelectAndBegin(ref AttackContext context, ref EnemyRandom random)
        {
            if (_phase != ZombieAttackPhase.None || _cooldownRemaining > 0f) return false;
            context.Tier = _tier;
            context.AttackRange = _range;
            context.LastStyle = _lastStyle;
            context.SecondLastStyle = _secondLastStyle;
            context.LastSwipeMirrored = _lastSwipeMirrored;
            if (!AttackSelector.Select(in context, ref random, out ZombieAttackStyle style, out bool mirror)) return false;
            Begin(style, mirror, AttackSelector.Enabled);
            return true;
        }

        private void Begin(ZombieAttackStyle style, bool mirror, bool useSlots)
        {
            _style = style;
            _swipeMirror = style == ZombieAttackStyle.Swipe && mirror;
            _swingTelegraph = Mathf.Max(0.02f, AttackStyleProfile.Scale(AttackStyleProfile.TelegraphMul, _tier, style, _telegraph));
            _swingDamage = AttackStyleProfile.Scale(AttackStyleProfile.DamageMul, _tier, style, _damage);
            _swingCooldown = Mathf.Max(0f, AttackStyleProfile.Scale(AttackStyleProfile.CooldownMul, _tier, style, _cooldown));
            _hitRadius = _bodyRadius * AttackStyleProfile.HitRadiusFactor[(int)style];
            _lungeSpeed = style == ZombieAttackStyle.Lunge
                ? _moveSpeed * LungeSpeedFactor * (_tier == EnemyTier.Standard ? AttackStyleProfile.StandardLungeReach : 1f)
                : 0f;
            _secondLastStyle = _lastStyle;
            _lastStyle = (int)style;
            if (style == ZombieAttackStyle.Swipe) _lastSwipeMirrored = _swipeMirror;
            _committed = false;
            if (useSlots) { AttackSlotBoard.Acquire(style, Time.time); _slotHeld = true; }
            _phase = ZombieAttackPhase.Windup;
            _phaseTime = 0f;
            _connectedThisSwing = false;
            ForwardImpulse = 0f;
            ActiveFramesThisSwing = 0;
            _clipStepThisSwing = false;
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

            // WP-076 §1.4: the grab window (clip grab_start → grab_end; timer until S-06's events land) — the second bite share at its end
            if (_grabRemaining >= 0f)
            {
                _grabRemaining -= dt;
                if (_grabRemaining < 0f && _grabOwner != null && _grabCollider != null)
                {
                    ZombieEnemy holder = _grabOwner;
                    Collider held = _grabCollider;
                    _grabCollider = null; _grabOwner = null;
                    Vector3 dir = held.transform.position - holder.transform.position; dir.y = 0f;
                    ApplyToPlayer(held, _swingDamage * GrabState.BiteShareOnEnd, holder, tier, held.ClosestPoint(holder.transform.position),
                        dir.sqrMagnitude > 1e-4f ? dir.normalized : holder.transform.forward);
                    GrabState.End(holder.LifeId, GrabEndReason.Timer);
                }
            }

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
                    if (_animationDriven)
                    {
                        // The clip opens the window (OnHitWindowStart). Timer = safety net only.
                        if (_phaseTime >= _swingTelegraph + WindowStartGraceSeconds)
                        {
                            s_windowFallbackCount++;
                            _phaseTime = 0f;
                            _phase = ZombieAttackPhase.Active;
                        }
                    }
                    else if (_phaseTime >= _swingTelegraph)
                    {
                        _phaseTime -= _swingTelegraph;
                        _phase = ZombieAttackPhase.Active;
                    }

                    if (_phase == ZombieAttackPhase.Active && _slotHeld && !_committed) { AttackSlotBoard.Commit(_style, Time.time); _committed = true; }   // WP-076: the style's cooldown starts at commit

                    break;

                case ZombieAttackPhase.Active:
                    // The lunge carries the body forward for exactly as long as the hitbox
                    // is live, so the visible dash and the damage window are the same thing.
                    ForwardImpulse = _clipStepThisSwing ? 0f : _lungeSpeed;   // WP-076: a clip step replaces the timer dash
                    ActiveFramesThisSwing++;

                    if (!_connectedThisSwing)
                    {
                        connected = TryConnect(owner, tier);
                        if (connected)
                        {
                            LastConnectFrame = Time.frameCount;
                        }
                    }

                    if (_animationDriven)
                    {
                        // The clip closes the window (OnHitWindowEnd). Timer = safety net only.
                        if (_phaseTime >= _active + WindowEndGraceSeconds)
                        {
                            s_windowFallbackCount++;
                            _phaseTime = 0f;
                            _phase = ZombieAttackPhase.Recovery;
                        }
                    }
                    else if (_phaseTime >= _active)
                    {
                        _phaseTime -= _active;
                        _phase = ZombieAttackPhase.Recovery;
                    }

                    break;

                case ZombieAttackPhase.Recovery:
                    if (_phaseTime >= _recovery)
                    {
                        if (_slotHeld) { AttackSlotBoard.Release(_style, false); _slotHeld = false; }   // WP-076 (attack_recover once S-06's event lands)
                        _phase = ZombieAttackPhase.None;
                        _phaseTime = 0f;
                        _cooldownRemaining = _swingCooldown;
                    }

                    break;
            }

            animator?.SetAttack(_style, _phase, PhaseProgress, _swingTelegraph);
            return connected;
        }

        private float CurrentPhaseDuration()
        {
            switch (_phase)
            {
                case ZombieAttackPhase.Windup: return _swingTelegraph;
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
            OverlapTests++;
            if (count > 0) OverlapHits++;

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

                // WP-076 §1.4: the bite pays 60 % here and 40 % when the grab ends un-escaped; the grab starts on this connect
                bool bite = _style == ZombieAttackStyle.BiteGrab;
                ApplyToPlayer(hit, bite ? _swingDamage * GrabState.BiteShareOnHit : _swingDamage, owner, tier, point, direction);
                if (bite && GrabState.TryBegin(owner.LifeId, owner.transform, tier, GrabState.SecondsFor(tier)))
                {
                    _grabRemaining = Mathf.Min(GrabState.SecondsFor(tier), GrabState.GrabMaxSeconds);
                    _grabCollider = hit;
                    _grabOwner = owner;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// The handover of one amount of damage to the player collider — wiring A (registry receiver) then B (the
        /// PlayerHealth bridge: nothing about PlayerHealth crosses into this file; no cooldown / i-frame / throttle —
        /// REQ-044 wants two same-frame swings to both land), then the event for C; see <see cref="TryConnect"/>.
        /// WP-076: shared by the hit and the grab's second bite share.
        /// </summary>
        private void ApplyToPlayer(Collider hit, float amount, ZombieEnemy owner, EnemyTier tier, Vector3 point, Vector3 direction)
        {
            bool applied = false;
            if (HitboxRegistry.TryResolve(hit, out IDamageReceiver receiver, out _))
            {
                var damageHit = new DamageHit
                {
                    Amount = amount,
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
                applied = EnemyPlayerDamageBridge.TryApply(hit, amount, owner.transform.position, SourceId(tier, _style), out bool resolvedPlayer);
                if (resolvedPlayer && !applied)
                {
                    // dead (REQ-048) or diagnosis-invulnerable: the swing is spent, nothing to publish
                    return;
                }
            }

            DamageEvents.RaisePlayerAttacked(new EnemyAttackLandedInfo(owner.transform, tier, amount, point, direction, applied));
        }

        /// <summary>
        /// Kill-attribution string handed to PlayerHealth, e.g. "Zombie.Ultra.Overhead".
        /// A fixed table rather than string concatenation, because the run-results screen
        /// reads this on death and the hit path must not allocate while an Ultra is
        /// swinging into a crowd. WP-076: any tier may use several styles — tier × style, 20 entries.
        /// </summary>
        private static string SourceId(EnemyTier tier, ZombieAttackStyle style) => s_sourceIds[Mathf.Clamp((int)tier, 0, 3), Mathf.Clamp((int)style, 0, AttackStyleProfile.StyleCount - 1)];

        // WP-076: tier × style (20 fixed strings — no concatenation on the hit path)
        private static readonly string[,] s_sourceIds =
        {
            { "Zombie.Standard.Swipe", "Zombie.Standard.Lunge", "Zombie.Standard.Sweep", "Zombie.Standard.Overhead", "Zombie.Standard.BiteGrab" },
            { "Zombie.Special.Swipe", "Zombie.Special.Lunge", "Zombie.Special.Sweep", "Zombie.Special.Overhead", "Zombie.Special.BiteGrab" },
            { "Zombie.Elite.Swipe", "Zombie.Elite.Lunge", "Zombie.Elite.Sweep", "Zombie.Elite.Overhead", "Zombie.Elite.BiteGrab" },
            { "Zombie.Ultra.Swipe", "Zombie.Ultra.Lunge", "Zombie.Ultra.Sweep", "Zombie.Ultra.Overhead", "Zombie.Ultra.BiteGrab" },
        };

        /// <summary>Debug gizmo data: where the active window will test, and how wide.</summary>
        public void GetHitVolume(out Vector3 centre, out float radius)
        {
            centre = _rig != null && _rig.AttackSocket != null ? _rig.AttackSocket.position : Vector3.zero;
            radius = _hitRadius;
        }
    }
}
