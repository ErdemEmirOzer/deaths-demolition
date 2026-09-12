using System;
using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// One zombie. The whole family - Standard, Runner, Guard, Executioner - is this one
    /// component configured differently (REQ-060, REQ-062, DEC-009).
    ///
    /// WHY ONE COMPONENT AND ONE POOL
    /// The tier is data, not a subclass. That means a single pool holds every enemy on the
    /// map, and the absolute technical safety ceiling of 40 is a property of that one pool
    /// rather than a sum over four pools that could quietly reach 160. The pool policy is
    /// ReturnNull, so exceeding the ceiling is a refused spawn, never a silent grow.
    ///
    /// WHY THE SUB-SYSTEMS ARE PLAIN CLASSES
    /// Animator, appearance, attack, hit reaction and the damage body are all plain C#
    /// objects owned here, not MonoBehaviours. One Update call per enemy instead of six,
    /// and no GetComponent anywhere in the hit path.
    ///
    /// CONFIG BINDING - A DOCUMENTED DEVIATION
    /// The wave contract asks every system to derive from PrototypeConfigBehaviour and read
    /// config only inside ApplyConfig. Pooling requires deriving from PooledBehaviour, and
    /// C# has single inheritance, so this class cannot do both. It follows the letter of
    /// the rule the other way round: ALL config reads live in ApplyConfig(PrototypeConfig),
    /// which is driven by EnemySystemHost - itself a PrototypeConfigBehaviour - and backed
    /// by a ConfigRevisionWatcher, the alternative that PrototypeConfigBehaviour's own
    /// documentation offers for exactly this case. No config value is read anywhere else.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed partial class ZombieEnemy : PooledBehaviour, IDamageReceiver, IExplosionReceiver
    {
        [SerializeField] private ZombieRig _rig = new ZombieRig();

        // ------------------------------------------------------------------ sub-systems
        private RegionalDamageBody _body;
        private ZombieAnimator _animator;
        private ZombieAppearance _appearance;
        private EnemyAttackState _attack;
        private EnemyHitReactionState _reaction;
        private CharacterController _controller;

        // ------------------------------------------------------------------- run state
        private EnemyRandom _random;
        private ConfigRevisionWatcher _configWatcher;
        private bool _initialised;

        private EnemyTier _tier = EnemyTier.Standard;
        private int _spawnRound = 1;
        private bool _alive;
        private bool _dying;
        private float _despawnCountdown;

        private Vector3 _knockbackVelocity;
        private float _verticalVelocity;
        private Vector3 _plannedVelocity;
        private float _currentSpeed;

        // --------------------------------------- fallbacks used before ApplyConfig arrives
        // Each mirrors the EnemyTierSettings default for Standard EXACTLY. They only apply
        // when no PrototypeConfig is supplied; a silent disagreement here would move the
        // fight without moving the config.

        [PrototypeOnly(PendingDecisions.CombatScaling,
            "Fallback for EnemyTierSettings.MoveSpeed. Request value: Standard 2.7 m/s.")]
        private const float DefaultMoveSpeed = 2.7f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.BodyRadius. Drives soft separation and the " +
            "CharacterController width.", true)]
        private const float DefaultBodyRadius = 0.45f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for EnemyTierSettings.SeparationMass. 1 = the Standard baseline.", true)]
        private const float DefaultSeparationMass = 1f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback turn rate before a tier is resolved, degrees per second.", true)]
        private const float DefaultTurnSpeedDegrees = 320f;

        // -------------------------------------------------- invented locomotion / physics

        [PrototypeOnly(PendingDecisions.None,
            "Turn rate floor, degrees per second. Turn rate is not in the request at all; it " +
            "is tied to move speed so a Runner can track a strafing player while an " +
            "Executioner has to commit to a heading.", true)]
        private const float TurnSpeedBaseDegrees = 120f;

        [PrototypeOnly(PendingDecisions.None,
            "Degrees per second of turn rate added per m/s of move speed. Invented.", true)]
        private const float TurnSpeedPerMoveSpeed = 62f;

        [PrototypeOnly(PendingDecisions.None,
            "Fraction of attack range at which an enemy stops pushing forward. Without this " +
            "the swarm compresses into a solid wall around the player.", true)]
        private const float AttackRangeApproachStop = 0.85f;

        [PrototypeOnly(PendingDecisions.None,
            "Speed an enemy keeps once inside AttackRangeApproachStop - the swing closes the " +
            "last gap instead of the walk.", true)]
        private const float ClosedInSpeedScale = 0.15f;

        // PROTOTYPE-ONLY / INVENTED. Not in the request; needed so a corpse leaves the
        // playfield without popping and so knockback bleeds off smoothly.

        [PrototypeOnly(PendingDecisions.None,
            "Seconds a corpse stays after its death reaction finishes, before it despawns. " +
            "Directly controls how cluttered a busy round looks.", true)]
        private const float CorpseLingerSeconds = 0.55f;

        [PrototypeOnly(PendingDecisions.None,
            "How fast a knockback shove bleeds off, per second. Invented.", true)]
        private const float KnockbackDamping = 7.5f;

        [PrototypeOnly(PendingDecisions.None,
            "Gameplay gravity for enemies, m/s^2. Deliberately heavier than real gravity so " +
            "bodies settle fast. Invented.", true)]
        private const float Gravity = -22f;

        // -------------------------------------------------- feedback intensity (REQ-125)
        // The magnitudes of the flesh < armour < break < critical < death hierarchy live in
        // HitFeedbackDirector (the single, budgeted owner of hit dressing since WP-002).
        // This class only reports what happened; it no longer spawns bursts of its own.

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for FeedbackConfigSection.HitStopArmorBreak (0.045 s), used when no " +
            "PrototypeConfig is active.", true)]
        private const float FallbackHitStopArmorBreak = 0.045f;

        // ------------------------------------------------------ cached from ApplyConfig
        private float _moveSpeed = DefaultMoveSpeed;
        private float _bodyRadius = DefaultBodyRadius;
        private float _separationMass = DefaultSeparationMass;
        private float _turnSpeedDegrees = DefaultTurnSpeedDegrees;

        // ------------------------------------------------------------------- accessors

        public EnemyTier Tier => _tier;
        public int SpawnRound => _spawnRound;
        public bool IsAlive => _alive;
        public bool IsDying => _dying;
        public float Health => _body != null ? _body.Health : 0f;
        public float MaxHealth => _body != null ? _body.MaxHealth : 0f;
        public float HealthRatio => _body != null ? _body.HealthRatio : 0f;
        public ArmorSet Armor => _body != null ? _body.Armor : null;
        public float BodyRadius => _bodyRadius;
        public ZombiePose Pose => _animator != null ? _animator.Pose : ZombiePose.Idle;
        public ZombieAttackPhase AttackPhase => _attack != null ? _attack.Phase : ZombieAttackPhase.None;

        /// <summary>Raised when this instance is returned to the pool. Spawners use it to keep counts.</summary>
        public event Action<ZombieEnemy> Released;

        // ------------------------------------------------------------------ live roster
        // Maintained by the instance itself, NOT by whoever spawned it. The spawn director
        // (G-06) owns its own pool and never tells this task about a spawn, so a roster
        // kept by EnemyFactory alone would silently miss every enemy in a real run. Debug
        // readouts, "clear all" and the config fan-out all read this list.

        private static readonly List<ZombieEnemy> s_live = new List<ZombieEnemy>(48);

        /// <summary>Every checked-out zombie, whoever spawned it. Includes corpses mid-reaction.</summary>
        public static IReadOnlyList<ZombieEnemy> Live => s_live;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLiveRoster()
        {
            s_live.Clear();
        }

        // ==================================================================== lifecycle

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            _body = new RegionalDamageBody();
            _animator = new ZombieAnimator(_rig);
            _appearance = new ZombieAppearance(_rig);
            _attack = new EnemyAttackState(_rig);
            _reaction = new EnemyHitReactionState();

            // The rig was assembled on the pool template, so this clone already owns a full
            // hierarchy; capture ITS transforms as the rest pose, not the template's.
            _rig.CaptureRestPose();

            // Region colliders live as long as the instance, so bind them once, here.
            // This is what makes a bullet hit a single dictionary lookup.
            RegisterHitboxes();

            _initialised = true;
        }

        private void OnDestroy()
        {
            UnregisterHitboxes();
        }

        /// <summary>
        /// Assemble the procedural body. Called ONCE, by EnemyFactory, on the inactive pool
        /// template - never on a clone. Instantiate then remaps every rig reference into
        /// the copy, so 40 zombies cost one build.
        /// </summary>
        internal void BuildProceduralBody()
        {
            ZombieBodyBuilder.Build(gameObject, _rig);
        }

        private void RegisterHitboxes()
        {
            HitboxRegistry.Register(_rig.HeadHitbox, this, this, BodyRegion.Head, transform);
            HitboxRegistry.Register(_rig.UpperHitbox, this, this, BodyRegion.UpperBody, transform);
            HitboxRegistry.Register(_rig.LowerHitbox, this, this, BodyRegion.LowerBody, transform);
        }

        private void UnregisterHitboxes()
        {
            HitboxRegistry.Unregister(_rig.HeadHitbox);
            HitboxRegistry.Unregister(_rig.UpperHitbox);
            HitboxRegistry.Unregister(_rig.LowerHitbox);
        }

        /// <summary>
        /// Physical presence of the three region hitboxes. Off while the body is a corpse
        /// so a bullet neither stops at it nor reports a hit on it; on again for a spawn.
        /// The registry bindings are untouched - they live as long as the instance.
        /// </summary>
        private void SetHitboxesEnabled(bool value)
        {
            if (_rig == null)
            {
                return;
            }

            if (_rig.HeadHitbox != null) _rig.HeadHitbox.enabled = value;
            if (_rig.UpperHitbox != null) _rig.UpperHitbox.enabled = value;
            if (_rig.LowerHitbox != null) _rig.LowerHitbox.enabled = value;
        }

        /// <summary>
        /// Configure a fresh spawn. Called by EnemyFactory immediately after the pool hands
        /// the instance over, before the first Update.
        /// </summary>
        public void Initialise(EnemyTier tier, int round, uint seed, PrototypeConfig config)
        {
            _tier = tier;
            _spawnRound = Mathf.Max(1, round);
            _random = new EnemyRandom(seed);

            // Order matters: the silhouette sets Visual.localScale, and ApplyConfig sizes
            // the CharacterController from it.
            _appearance.ApplyVariation(_tier, ref _random);
            ApplyConfig(config);
            _animator.ResetForSpawn(_tier, _appearance.BaseLeanDegrees, ref _random);

            RollArmor(config);

            _alive = true;
            _dying = false;
            _despawnCountdown = 0f;

            DamageTargetRegistry.Register(transform, this, this, _bodyRadius, ZombieBodyBuilder.StandHeight * 0.5f);
        }

        protected override void OnSpawned()
        {
            _rig.ResetToRestPose();

            _knockbackVelocity = Vector3.zero;
            _plannedVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _currentSpeed = 0f;
            _alive = false;   // Initialise flips this on
            _dying = false;

            if (_controller != null)
            {
                _controller.enabled = true;
                _controller.detectCollisions = true;
            }

            // A pooled body comes back from its last death with the region hitboxes off
            // (see Die); a fresh spawn must be shootable again.
            SetHitboxesEnabled(true);

            _body?.Reset(1f);
            _attack?.ResetForSpawn();
            _reaction?.ResetForSpawn();
            _appearance?.HideAllArmor();

            ResetSpawnBinding();
            RegisterCrowdMembership();

            if (!s_live.Contains(this))
            {
                s_live.Add(this);
            }
        }

        protected override void OnDespawned()
        {
            UnregisterCrowdMembership();
            DamageTargetRegistry.Unregister(transform);
            s_live.Remove(this);

            _alive = false;
            _dying = false;

            Action<ZombieEnemy> handler = Released;
            if (handler != null)
            {
                try { handler.Invoke(this); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        // ====================================================================== config

        /// <summary>
        /// THE ONLY PLACE THIS CLASS READS PrototypeConfig.
        ///
        /// Every derived value is recomputed here, so a live F1 edit to move speed, attack
        /// timing, health scaling or the stagger budget lands on enemies already walking
        /// the map. Health is rescaled by ratio rather than refilled, so buffing an enemy
        /// mid-run does not heal it.
        ///
        /// Scaling arithmetic is NOT re-derived locally: EnemyHealth, ArmorChance and
        /// TierSpawnWeight are asked of the config, because those methods consult the
        /// DEC-021 override table first. That table is empty by design and must stay so.
        /// </summary>
        public void ApplyConfig(PrototypeConfig config)
        {
            if (config == null)
            {
                return;
            }

            EnemyTierSettings settings = config.Enemies.Find(_tier);
            if (settings != null)
            {
                _moveSpeed = Mathf.Max(0.1f, settings.MoveSpeed);
                _bodyRadius = Mathf.Max(0.1f, settings.BodyRadius);
                _separationMass = Mathf.Max(0.1f, settings.SeparationMass);

                // PROTOTYPE-ONLY / INVENTED: turn rate is not in the request. Tied to move
                // speed so a Runner can actually track a strafing player while an
                // Executioner has to commit to a heading.
                _turnSpeedDegrees = TurnSpeedBaseDegrees + settings.MoveSpeed * TurnSpeedPerMoveSpeed;
            }

            if (_controller != null)
            {
                float scale = _rig != null && _rig.Visual != null ? _rig.Visual.localScale.y : 1f;
                _controller.radius = _bodyRadius;
                _controller.height = Mathf.Max(_bodyRadius * 2.1f, ZombieBodyBuilder.StandHeight * scale);
                _controller.center = new Vector3(0f, _controller.height * 0.5f, 0f);
                _controller.stepOffset = 0.35f;
                _controller.slopeLimit = 55f;
                _controller.skinWidth = 0.03f;
            }

            // config.EnemyHealth already folds in the round health multiplier.
            float maxHealth = config.EnemyHealth(_tier, _spawnRound);
            if (_body != null)
            {
                if (_body.MaxHealth <= 0f)
                {
                    _body.Reset(maxHealth);
                }
                else
                {
                    _body.RescaleMaxHealth(maxHealth);
                }
            }

            _attack?.ApplyConfig(config, _tier, _spawnRound);
            _reaction?.ApplyConfig(config, _tier);
        }

        /// <summary>
        /// Fit armour for this spawn - REQ-081. Zero, one, two or three pieces, each rolled
        /// independently against the tier's per-round odds.
        /// </summary>
        private void RollArmor(PrototypeConfig config)
        {
            if (config == null || _body == null)
            {
                return;
            }

            ArmorConfigSection armorConfig = config.Enemies.Armor;
            _appearance.HideAllArmor();

            TryFit(config, armorConfig, ArmorSlot.Helmet);
            TryFit(config, armorConfig, ArmorSlot.Body);
            TryFit(config, armorConfig, ArmorSlot.Leg);
        }

        private void TryFit(PrototypeConfig config, ArmorConfigSection armorConfig, ArmorSlot slot)
        {
            float chance = config.ArmorChance(_tier, slot, _spawnRound);
            if (!_random.Chance(chance))
            {
                return;
            }

            float durability = armorConfig.DurabilityFor(slot);
            if (durability <= 0f)
            {
                return;
            }

            _body.Armor.Equip(slot, durability);
            _appearance.EquipArmor(slot);
        }

        // ======================================================================== tick

        private void Update()
        {
            if (!_initialised)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return; // paused: the whole simulation freezes with timeScale, by design
            }

            // Backstop for a live config edit that reaches an enemy the host missed.
            PrototypeConfig config = PrototypeConfig.Active;
            if (_configWatcher.Consume(config))
            {
                ApplyConfig(config);
            }

            _reaction.Tick(dt);

            if (_dying)
            {
                TickDying(dt);
                return;
            }

            if (!_alive)
            {
                return;
            }

            TickCombat(dt);
            _animator.Tick(dt);
        }

        private void TickCombat(float dt)
        {
            Transform target = ResolveTarget();
            Vector3 selfPosition = transform.position;

            Vector3 toTarget = Vector3.zero;
            float distance = float.MaxValue;

            if (target != null)
            {
                toTarget = target.position - selfPosition;
                toTarget.y = 0f;
                distance = toTarget.magnitude;
            }

            // ------------------------------------------------------------------ attack
            bool staggering = _animator.IsStaggering;

            if (!staggering && target != null && _attack.CanStartAttack(distance))
            {
                _attack.Begin();
            }

            _attack.Tick(dt, this, _tier, _animator);

            // ---------------------------------------------------------------- steering
            // OWN locomotion first: follow the NavMesh path towards the target. The crowd
            // layer only ever contributes a CORRECTION on top of this, so a lone zombie on
            // an empty map still walks.
            Vector3 ownVelocity = Vector3.zero;

            bool canWalk = !staggering && _attack.Phase != ZombieAttackPhase.Recovery;
            if (canWalk && target != null && TickNavigation(selfPosition, target.position, out Vector3 heading))
            {
                float speed = _moveSpeed;

                // Stop pushing into the player once inside attack range; the swing closes
                // the last gap. Without this the swarm compresses into a solid wall.
                if (distance < _attack.Range * AttackRangeApproachStop)
                {
                    speed *= ClosedInSpeedScale;
                }

                ownVelocity = heading * speed;
            }

            // The Runner's lunge adds committed forward speed during its active window.
            float impulse = _attack.ForwardImpulse;
            if (impulse > 0f)
            {
                ownVelocity += transform.forward * impulse;
            }

            _plannedVelocity = ownVelocity;

            // ICrowdMember contract: final = own locomotion + correction.
            Vector3 correction = ConsumeCrowdCorrection();
            Vector3 velocity = ownVelocity + correction;

            // ----------------------------------------------------------------- physics
            velocity += _knockbackVelocity;
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Mathf.Clamp01(KnockbackDamping * dt));

            _verticalVelocity = _controller.isGrounded ? -2f : _verticalVelocity + Gravity * dt;

            Vector3 motion = velocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * dt);

            // A strong push can walk the body off the mesh; put it back.
            ClampToWalkable(correction);

            // ------------------------------------------------------------------ facing
            // Faces its OWN intent, not the crowd correction: a zombie being shouldered
            // aside should keep looking where it is going instead of pirouetting.
            Vector3 facing = ownVelocity;
            facing.y = 0f;

            // While winding up or striking, face the target rather than the movement, so
            // the telegraph always points at the player it threatens.
            if (_attack.IsAttacking && target != null && toTarget.sqrMagnitude > 0.0001f)
            {
                facing = toTarget;
            }

            if (facing.sqrMagnitude > 0.0004f)
            {
                Quaternion want = Quaternion.LookRotation(facing.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, _turnSpeedDegrees * dt);
            }

            // ---------------------------------------------------------------- animation
            Vector3 planar = velocity;
            planar.y = 0f;
            _currentSpeed = planar.magnitude;
            _animator.SetLocomotion(_moveSpeed > 0f ? _currentSpeed / _moveSpeed : 0f, _currentSpeed);
        }

        private void TickDying(float dt)
        {
            // Corpses still slide under knockback, but they no longer steer, attack,
            // register as damage targets or block anything: the controller is off (see
            // Die), so the slide is a plain transform move on the ground plane.
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Mathf.Clamp01(KnockbackDamping * dt));

            if (_controller != null && _controller.enabled)
            {
                _verticalVelocity = _controller.isGrounded ? -2f : _verticalVelocity + Gravity * dt;
                Vector3 motion = _knockbackVelocity;
                motion.y = _verticalVelocity;
                _controller.Move(motion * dt);
            }
            else
            {
                Vector3 slide = _knockbackVelocity;
                slide.y = 0f;
                transform.position += slide * dt;
            }

            _animator.Tick(dt);

            if (_animator.DeathFinished)
            {
                _despawnCountdown -= dt;
                if (_despawnCountdown <= 0f)
                {
                    Despawn();
                }
            }
        }

        // ==================================================================== damage in

        /// <summary>
        /// REQ-082, REQ-083, REQ-084. Region resolution already happened: the collider that
        /// was hit told DamageQuery which region this is.
        /// </summary>
        public DamageResult ApplyHit(in DamageHit hit)
        {
            if (!_alive || _body == null)
            {
                return default;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            DamageResult result = _body.ApplyDirectHit(in hit, config != null ? config.Enemies.Armor : null);
            ProcessResult(in hit, in result, hit.Point, hit.Direction);
            return result;
        }

        /// <summary>
        /// REQ-085..REQ-090. The split lives in RegionalDamageBody; this method only turns
        /// the outcome into feedback.
        /// </summary>
        public DamageResult ApplyExplosion(float totalDamage, Vector3 origin)
        {
            if (!_alive || _body == null)
            {
                return default;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            DamageResult result = _body.ApplyExplosion(
                totalDamage,
                config != null ? config.Enemies.Armor : null,
                config != null ? config.Enemies.Explosion : null);

            Vector3 centre = transform.position + Vector3.up * (ZombieBodyBuilder.StandHeight * 0.5f);
            Vector3 direction = centre - origin;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

            var syntheticHit = new DamageHit
            {
                Amount = totalDamage,
                Region = result.Region,
                Point = centre,
                Direction = direction,
                ImpactForce = DamageForce.ExplosionCore,
                Source = DamageSourceKind.Explosion
            };

            ProcessResult(in syntheticHit, in result, centre, direction);
            return result;
        }

        /// <summary>
        /// Everything that happens because of a damage result: armour visuals, break
        /// events, hit reaction, effects, gameplay events, death.
        /// </summary>
        private void ProcessResult(in DamageHit hit, in DamageResult result, Vector3 point, Vector3 direction)
        {
            bool heavyTier = _tier == EnemyTier.Elite || _tier == EnemyTier.Ultra;

            // -------------------------------------------------------- armour readability
            RefreshArmorVisual(ArmorSlot.Helmet);
            RefreshArmorVisual(ArmorSlot.Body);
            RefreshArmorVisual(ArmorSlot.Leg);

            for (int i = 0; i < _body.LastBreakCount; i++)
            {
                ArmorSlot slot = _body.LastBreak(i);
                _appearance.BreakArmor(slot);

                DamageEvents.RaiseArmorBroken(new ArmorBreakInfo(
                    transform, _tier, slot, DamageRegions.RegionFor(slot), point, direction, hit.Source,
                    _body.Armor.ProtectingCount,
                    PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Feedback.HitStopArmorBreak
                        : FallbackHitStopArmorBreak));
            }

            // ------------------------------------------------------------ hit reaction
            HitReaction reaction = _reaction.Evaluate(
                in hit, in result,
                _attack.IsTelegraphing,
                _animator.Pose == ZombiePose.Run,
                result.Killed,
                heavyTier);

            if (!result.Killed)
            {
                _animator.AddFlinch(direction, reaction.FlinchIntensity, result.Region, transform);

                if (reaction.Stagger)
                {
                    _animator.BeginStagger(direction, reaction.StaggerIntensity, reaction.StaggerDuration, transform);
                    _attack.Cancel();
                }

                if (reaction.KnockbackVelocity != Vector3.zero)
                {
                    _knockbackVelocity += reaction.KnockbackVelocity;
                }
            }

            // ------------------------------------------------------------------ effects
            // WP-002: no enemy-side burst here. Every hit that reaches this method through
            // HitscanEngine is dressed once by HitFeedbackDirector.ReportEnemyHit (blood,
            // armour spark, armour shards, death), which is the only layer that applies the
            // REQ-128 readability budget. A second, unbudgeted EnemyEffects burst on the same
            // point doubled the visual noise of every hit and every death in a crowd.

            // ------------------------------------------------------------------- events
            DamageEvents.RaiseEnemyDamaged(new EnemyDamageInfo(
                transform, _tier, in result, point, direction, hit.Source, hit.ImpactForce,
                _body.HealthRatio, reaction.Stagger));

            if (result.Killed)
            {
                Die(in hit, in result, point, direction, heavyTier, reaction.SuggestedHitStop);
            }
        }

        private void RefreshArmorVisual(ArmorSlot slot)
        {
            if (!_body.Armor.IsEquipped(slot) || _body.Armor.IsBroken(slot))
            {
                return;
            }

            _appearance.RefreshArmorDamage(slot, _body.Armor.Ratio(slot));
        }

        // ======================================================================= death

        private void Die(in DamageHit hit, in DamageResult result, Vector3 point, Vector3 direction,
            bool heavyTier, float suggestedHitStop)
        {
            if (!_alive)
            {
                return;
            }

            _alive = false;
            _dying = true;
            _despawnCountdown = CorpseLingerSeconds;

            // Out of the target registry immediately: a corpse must not eat a chain arc, a
            // ricochet or a share of the next explosion.
            DamageTargetRegistry.Unregister(transform);
            UnregisterCrowdMembership();
            _attack.Cancel();

            if (_controller != null)
            {
                // Stop blocking the player the instant it dies (DEC-032 applies to the
                // living swarm, not to corpses on the floor). detectCollisions alone is not
                // enough: a CharacterController sweep (the player's Move) still collides
                // with a controller that has detectCollisions off - measured in WP-002,
                // five corpses let the player advance 0.02 m in 1.5 s. Disabling the
                // component removes the capsule from every query; TickDying moves the
                // corpse by transform while it is off.
                _controller.detectCollisions = false;
                _controller.enabled = false;
            }

            // Same rule for bullets (WP-002): a corpse must not swallow a direct shot either.
            // ApplyHit already refuses damage once _alive is false, but the region hitboxes
            // stayed physically present, so a hitscan ray stopped at the corpse, spent its
            // pierce budget on it and dressed a zero-result hit as a flesh hit. Turning the
            // colliders off makes the ray pass through and removes the fake feedback.
            SetHitboxesEnabled(false);

            _animator.BeginDeath(direction, hit.ImpactForce, result.Region, heavyTier, transform, ref _random);

            PrototypeConfig config = PrototypeConfig.Active;
            int gold = config != null ? config.GoldReward(_tier) : 0;
            int xp = config != null ? config.KillXp(_tier) : 0;

            float hitStop = suggestedHitStop;
            if (heavyTier && config != null)
            {
                hitStop = Mathf.Max(hitStop, config.Feedback.HitStopHeavyDeath);
            }

            DamageEvents.RaiseEnemyDied(new EnemyDeathInfo(
                transform, _tier, transform.position, direction, result.Region, hit.Source,
                DamageRegions.IsCritical(in result, hit.Source), heavyTier, _spawnRound,
                gold, xp, _body.LastOverkill, hitStop));
        }

        /// <summary>
        /// Debug / round-cleanup kill. Goes through the same death path so gold, XP and
        /// kill counters behave exactly as they do in play.
        /// </summary>
        public void Kill(DamageSourceKind source = DamageSourceKind.Bullet)
        {
            if (!_alive || _body == null)
            {
                return;
            }

            DamageResult result = _body.Execute(BodyRegion.UpperBody);
            var syntheticHit = new DamageHit
            {
                Amount = result.HealthDamage,
                Region = BodyRegion.UpperBody,
                Point = transform.position + Vector3.up,
                Direction = transform.forward,
                ImpactForce = 1f,
                Source = source
            };

            ProcessResult(in syntheticHit, in result, syntheticHit.Point, syntheticHit.Direction);
        }

        /// <summary>Debug: shatter every remaining plate, raising one event per piece.</summary>
        public int BreakAllArmor()
        {
            if (_body == null || !_alive)
            {
                return 0;
            }

            var broken = new ArmorSlot[3];
            int count = _body.Armor.BreakAll(broken);

            Vector3 point = transform.position + Vector3.up * 1.1f;
            Vector3 direction = transform.forward;

            for (int i = 0; i < count; i++)
            {
                _appearance.BreakArmor(broken[i]);

                DamageEvents.RaiseArmorBroken(new ArmorBreakInfo(
                    transform, _tier, broken[i], DamageRegions.RegionFor(broken[i]), point, direction,
                    DamageSourceKind.Bullet, _body.Armor.ProtectingCount,
                    PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Feedback.HitStopArmorBreak
                        : FallbackHitStopArmorBreak));
            }

            return count;
        }

        /// <summary>Round cleanup: leave without dying, so no gold and no kill is credited.</summary>
        public void Remove()
        {
            _alive = false;
            _dying = false;
            Despawn();
        }

        /// <summary>One-line armour state, for the F1 readout.</summary>
        public string DescribeArmor()
        {
            if (_body == null)
            {
                return "-";
            }

            ArmorSet armor = _body.Armor;
            return string.Format("{0} hp {1:0}/{2:0}  H {3}  B {4}  L {5}",
                _tier, _body.Health, _body.MaxHealth,
                Describe(armor, ArmorSlot.Helmet),
                Describe(armor, ArmorSlot.Body),
                Describe(armor, ArmorSlot.Leg));
        }

        private static string Describe(ArmorSet armor, ArmorSlot slot)
        {
            if (!armor.IsEquipped(slot)) return "--";
            if (armor.IsBroken(slot)) return "XX";
            return Mathf.RoundToInt(armor.Current(slot)).ToString();
        }
    }
}
