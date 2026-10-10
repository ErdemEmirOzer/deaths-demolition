using System;
using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Navigation;
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
    public sealed partial class ZombieEnemy : PooledBehaviour, IDamageReceiver, IExplosionReceiver, DD.Prototype.Feedback.IFeedbackWeightSource
    {
        [SerializeField] private ZombieRig _rig = new ZombieRig();

        // WP-016: the AP-007 skinned body, instantiated once on the pool template under
        // rig.Visual (clones inherit the reference like the rig); null = M1 block body.
        [SerializeField] private ZombieSkinnedVisual _skinned;

        /// <summary>WP-040 · DEC-081 (user decision 2026-09-17): when a spawn plays the AP-009 rise clip.</summary>
        public enum RisePolicy
        {
            /// <summary>Never (WP-003 behaviour: the body walks at once).</summary>
            Off = 0,
            /// <summary>Every spawn (the WP-018 probe / test switch).</summary>
            Always = 1,
            /// <summary>HYBRID (DEC-081, shipped): only a spawn the director judged OffScreen rises; an on-screen or unjudged spawn walks at once.</summary>
            OffScreenOnly = 2,
        }

        /// <summary>
        /// WP-040 · DEC-081: the rise policy. Hybrid by default — the rise reads as "the dead get up"
        /// only where the camera cannot see the pop-in it hides (off-screen spawns, WP-030 visibility
        /// signal); an on-screen spawn skips the 1.57 s stand-still so time-to-engage in view is unchanged.
        /// Probe / test switch: <see cref="RiseOnSpawn"/> (true = Always, false = Off).
        /// </summary>
        public static RisePolicy Rise = RisePolicy.OffScreenOnly;

        /// <summary>
        /// WP-018: play the AP-009 rise clip on spawn (the body neither walks nor attacks for its
        /// 1.57 s; DEC-023 counts it as active). WP-040: a VIEW on <see cref="Rise"/> — true = Always,
        /// false = Off; the shipped default is the DEC-081 hybrid (probe / test switch: -ddRise on).
        /// </summary>
        public static bool RiseOnSpawn
        {
            get => Rise == RisePolicy.Always;
            set => Rise = value ? RisePolicy.Always : RisePolicy.Off;
        }

        /// <summary>Reset the policy to the DEC-081 default (tests).</summary>
        public static void ResetRisePolicy() => Rise = RisePolicy.OffScreenOnly;

        /// <summary>WP-040: the visibility class of this life's spawn (director-judged; Unknown for a debug spawn).</summary>
        public SpawnVisibility SpawnVisibilityClass { get; private set; }
        /// <summary>WP-040: true when the policy × visibility asked this life's spawn to rise (the clip plays on a driven body; a block body records the decision only).</summary>
        public bool RoseOnSpawn { get; private set; }

        /// <summary>WP-040 · DEC-081: does a spawn of <paramref name="visibility"/> rise under <paramref name="policy"/>? Pure.</summary>
        public static bool ShouldRise(RisePolicy policy, SpawnVisibility visibility)
        {
            switch (policy)
            {
                case RisePolicy.Always: return true;
                case RisePolicy.OffScreenOnly: return visibility == SpawnVisibility.OffScreen;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ sub-systems
        private RegionalDamageBody _body;
        private ZombieAnimator _animator;
        private ZombieAppearance _appearance;
        private EnemyAttackState _attack;
        private EnemyHitReactionState _reaction;
        private CharacterController _controller;
#if UNITY_EDITOR
        // Opt-in, single-body test observation. No observer component exists in normal play.
        [Serializable]
        public struct DiagnosticMove
        {
            public long callId;
            public int frame, phase, lifeId;
            public float time, dt;
            public Vector3 ownVelocity, crowdVelocity, finalVelocity, requestedDelta;
            public Vector3 before, afterMove, afterClamp, steeringCorner;
            public bool hasSteeringCorner, groundedBefore, groundedAfterMove, groundedAfterClamp;
            public CollisionFlags flags;
        }
        public static ZombieEnemy DiagnosticMoveBody;
        public static Action<DiagnosticMove> DiagnosticMoveObserved;
        private long _diagnosticMoveCall;
#endif


        // ------------------------------------------------------------------- run state
        private EnemyRandom _random;
        private ConfigRevisionWatcher _configWatcher;
        private bool _initialised;

        private EnemyTier _tier = EnemyTier.Standard;
        private EnemyRewardSnapshot _reward;
        private EnemyStatSnapshot _stats;
        private TierBehaviourProfile _versionProfile;   // WP-028: the B/B/B layer of this life (null in the pool)
        private TierSilhouetteSlot _silhouette;          // WP-028: placeholder piece object, pooled with the body
        private int _spawnRound = 1;
        private int _lifeId;
        private bool _alive;
        private bool _dying;
        private float _despawnCountdown;

        // WP-006 event contract: the transaction this life last accepted damage in. A
        // second application inside the same transaction is refused (see ApplyHit).
        private int _lastTransactionId;
        private DamageEventMeta _lastMeta;

        private static int s_duplicateApplicationCount;

        /// <summary>Applications refused because the same transaction already reached this enemy. Test/probe readout.</summary>
        public static int DuplicateApplicationCount => s_duplicateApplicationCount;

        /// <summary>WP-006 meta of the most recent accepted damage call (default before the first). Test/probe readout.</summary>
        public DamageEventMeta LastDamageMeta => _lastMeta;

        private Vector3 _knockbackVelocity;
        private float _verticalVelocity;
        private Vector3 _plannedVelocity;
        private float _riseRemaining;   // WP-030: spawn rise handshake (WP-018 animator drives it; DEC-023 counts the body active meanwhile)
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

        // WP-054: the corpse linger moved to CrowdFeelSettings.CorpseLinger (per tier, jitter, ceiling rule); readout below.
        /// <summary>WP-054: the linger this life's corpse was given at death (tests).</summary>
        public float LastCorpseLinger { get; private set; }

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

        /// <summary>
        /// WP-018 · G-04 opt-in: the feedback weight is the version, verbatim. The bounds-height
        /// fallback read the union of every renderer under the root; with the AP-009 clips the
        /// skinned bounds turn with the hips and a Standard shamble measured ≥ 2.05 m → an Elite
        /// "heavy death" on a Standard (measured in CoreCombatSliceTests). The tier is the truth.
        /// </summary>
        public DD.Prototype.Feedback.FeedbackWeight Weight
        {
            get
            {
                switch (_tier)
                {
                    case EnemyTier.Special: return DD.Prototype.Feedback.FeedbackWeight.Special;
                    case EnemyTier.Elite: return DD.Prototype.Feedback.FeedbackWeight.Elite;
                    case EnemyTier.Ultra: return DD.Prototype.Feedback.FeedbackWeight.Ultra;
                    default: return DD.Prototype.Feedback.FeedbackWeight.Standard;
                }
            }
        }
        public int SpawnRound => _spawnRound;

        /// <summary>WP-004: identity of this body's current life (one per pool checkout). 0 before the first spawn.</summary>
        public int LifeId => _lifeId;

        /// <summary>
        /// WP-008: the reward record taken when this life spawned (version + armour
        /// composition at spawn + resolved gold slots). Immutable for the life; the death
        /// event carries it verbatim. Default (not valid) before the first spawn.
        /// </summary>
        public EnemyRewardSnapshot Reward => _reward;

        /// <summary>
        /// WP-010: the stat profile this life was priced with at spawn (tier settings × the
        /// SPAWN round's multipliers). Frozen for the life; a round boundary never changes
        /// it. Re-resolved only by a live config edit, with the same spawn round.
        /// </summary>
        public EnemyStatSnapshot Stats => _stats;

        /// <summary>WP-010: the version-look contract this body's appearance implements (readout for tests/probe; no scene search).</summary>
        public IEnemyVersionVisual VersionVisual => _appearance;

        /// <summary>WP-028: the version layer applied to this life (attack timer scales, armour tendency, fit, silhouette, attack clip placeholder). Read by the animator for playback rates.</summary>
        public TierBehaviourProfile VersionProfile => _versionProfile;

        /// <summary>WP-028: the silhouette piece slot (null when the version has none or the body was never shown one).</summary>
        public TierSilhouetteSlot Silhouette => _silhouette;

        public bool IsAlive => _alive;

        /// <summary>WP-030 · Spawn rise handshake: while rising the body neither walks nor attacks, the crowd routes around it, and the round ledger already counts it active (DEC-023 - activation, not first step). Two sources: the WP-018 AP-009 rise clip (<c>ZombieSkinnedVisual.IsRising</c>, <c>RiseOnSpawn</c>) and this timer (<c>BeginRise</c>, for bodies without a driven clip / tests); <c>CompleteRise</c> or the timer ends the latter.</summary>
        public bool IsRising => _riseRemaining > 0f || (_skinned != null && _skinned.IsRising);   // timer handshake OR the WP-018 AP-009 rise clip (RiseOnSpawn)
        public float RiseRemaining => Mathf.Max(0f, _riseRemaining);

        public void BeginRise(float seconds)
        {
            if (!_alive || _dying) return;
            _riseRemaining = Mathf.Max(0f, seconds);
        }

        public void CompleteRise() => _riseRemaining = 0f;
        public bool IsDying => _dying;
        public float Health => _body != null ? _body.Health : 0f;
        public float MaxHealth => _body != null ? _body.MaxHealth : 0f;
        public float HealthRatio => _body != null ? _body.HealthRatio : 0f;
        public ArmorSet Armor => _body != null ? _body.Armor : null;
        public float BodyRadius => _bodyRadius;
        public ZombiePose Pose => _animator != null ? _animator.Pose : ZombiePose.Idle;
        public ZombieAttackPhase AttackPhase => _attack != null ? _attack.Phase : ZombieAttackPhase.None;
        /// <summary>WP-018: the hit window is opened / closed by the AP-009 clip events (DEC-029 through ADR-018-1).</summary>
        public bool AttackAnimationDriven => _attack != null && _attack.AnimationDriven;
        /// <summary>WP-052: the animator (named reaction readouts, tests).</summary>
        public ZombieAnimator Animator => _animator;
        /// <summary>WP-040: the live cooldown after the version floor (readout / tests).</summary>
        public float AttackCooldownSeconds => _attack != null ? _attack.CooldownSeconds : 0f;
        /// <summary>WP-018: Time.frameCount of the last connecting swing (tests; -1 = never).</summary>
        public int LastAttackConnectFrame => _attack != null ? _attack.LastConnectFrame : -1;

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

        /// <summary>WP-088: drop Unity-null roster entries (a destroyed body whose OnDestroy did not run); returns how many.</summary>
        internal static int PruneDestroyedFromRoster()
        {
            int removed = 0;
            for (int i = s_live.Count - 1; i >= 0; i--)
            {
                if (s_live[i] == null)
                {
                    s_live.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLiveRoster()
        {
            s_live.Clear();
            s_duplicateApplicationCount = 0;
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

            // WP-016: bind the skinned body of THIS clone (bridge rest = block rest + bind pose,
            // captured before the Animator's first update) and hand it to the appearance.
            if (_skinned != null)
            {
                _skinned.Attach(_rig, _animator);
                // WP-018 (ADR-018-1 / DEC-029): the AP-009 clip's hit window drives the attack state.
                _skinned.WindowSink = _attack;
                _attack.SetAnimationDriven(_skinned.Authored);
                CharacterAssetSet set = CharacterAssetSet.Load();
                // WP-017: with the AP-008 library the plate placeholders are never shown; the
                // appearance drives real armour meshes through ArmorVisualBinder.
                _appearance.SetSkinned(_skinned, set != null ? set.ZombieLibrary : null, set != null ? set.ZombieArmor : null);
            }

            // Region colliders live as long as the instance, so bind them once, here.
            // This is what makes a bullet hit a single dictionary lookup.
            RegisterHitboxes();

            _initialised = true;
        }

        private void OnDestroy()
        {
            UnregisterHitboxes();
            UnbindClipEvents();   // WP-076

            // WP-088: a body destroyed while checked out (a Destroy outside the pool's release) never runs OnDespawned. It
            // leaves the roster, the corpse list, the crowd and the damage targets here, or the dead entry counts against the
            // ceiling (SpawnedCount) and every "clear all" for the rest of the session.
            if (s_live.Remove(this))
            {
                UnregisterCorpse();
                UnregisterCrowdMembership();
                DamageTargetRegistry.Unregister(transform);
            }
        }

        /// <summary>
        /// Assemble the procedural body. Called ONCE, by EnemyFactory, on the inactive pool
        /// template - never on a clone. Instantiate then remaps every rig reference into
        /// the copy, so 40 zombies cost one build.
        /// </summary>
        internal void BuildProceduralBody()
        {
            ZombieBodyBuilder.Build(gameObject, _rig);

            // WP-016: the AP-007 skinned body on top of the block rig (which stays as the
            // hidden driver skeleton). Template only; the pool's clones inherit hierarchy,
            // re-homed hitboxes/armour roots and the serialized reference.
            CharacterAssetSet set = CharacterAssetSet.Load();
            if (CharacterAssetSet.UseSkinnedZombie() && _rig.Visual != null)
            {
                GameObject visual = Instantiate(set.ZombiePrefab, _rig.Visual, false);
                visual.name = ZombieSkinnedVisual.PrefabName;
                _skinned = visual.GetComponent<ZombieSkinnedVisual>();
                if (_skinned == null)
                {
                    Debug.LogError("[Enemies] " + ZombieSkinnedVisual.PrefabName + " prefab has no ZombieSkinnedVisual; keeping the block body.");
                    Destroy(visual);
                }
                else
                {
                    SetLayerRecursively(visual, PrototypeLayers.Enemy);
                    ZombieSkinnedVisual.HideBlockRenderers(_rig);
                    _skinned.RehomeGameplayNodes(_rig);
                }
            }
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>WP-016: the skinned visual (null with the M1 block body).</summary>
        public ZombieSkinnedVisual Skinned => _skinned;

        /// <summary>WP-016: the M1 rig (the hidden driver skeleton when skinned) — tests/probe.</summary>
        public ZombieRig Rig => _rig;

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
        /// <summary>WP-040: the debug / test spawn route with an explicit visibility class (the director route sets it through the spawn binding).</summary>
        public void Initialise(EnemyTier tier, int round, uint seed, PrototypeConfig config, SpawnVisibility visibility)
        {
            SpawnVisibilityClass = visibility;
            Initialise(tier, round, seed, config);
        }

        public void Initialise(EnemyTier tier, int round, uint seed, PrototypeConfig config)
        {
            _tier = tier;
            _spawnRound = Mathf.Max(1, round);
            _random = new EnemyRandom(seed);
            _appearance.LookSeed = seed;   // WP-016: the look is a pure function of the spawn seed
            _versionProfile = TierBehaviourProfileSet.Active.For(_tier);   // WP-028: resolved once per life, before the look (fit rule) and the roll (armour tendency)

            // Order matters: the silhouette sets Visual.localScale, and ApplyConfig sizes
            // the CharacterController from it. WP-010: the version look goes through the
            // IEnemyVersionVisual contract (same placeholder behaviour as before).
            _appearance.ApplyVersion(_tier, ref _random);
            ApplyConfig(config);
            _animator.ResetForSpawn(_tier, _appearance.BaseLeanDegrees, ref _random);

            RollArmor(config);

            ApplyNavProfileForLife(seed);   // WP-030: fit-scaled separation radius + route roll (after the look, before the first tick)
            {
                CrowdFeelSettings feel = CrowdFeelSettings.Active;   // WP-054: per-life queue variety
                _waitStallSeconds = _random.Range(feel.StallSecondsMin, feel.StallSecondsMax);
                _shufflePeriod = _random.Range(feel.ShufflePeriodMin, feel.ShufflePeriodMax);
                _shufflePhase = _random.Range(0f, 2f * Mathf.PI);
            }

            // WP-028: the version's silhouette piece under its anchor (placeholder until AP-014).
            _silhouette = EnemyVersionApplier.ApplySilhouette(_silhouette, _appearance.Anchors, _tier, _versionProfile, _rig != null ? _rig.Visual : transform, _appearance);   // WP-036: real AP-014 parts when the library is built

            // WP-018: the driven body needs the version timings (attack clip rate, gait rate) and the fit.
            // The telegraph is the attack state's own (WP-028 version scale applied), so the clip's
            // hit_window_start lands on the timer this life actually runs with.
            _animator.SetTiming(_attack != null ? _attack.TelegraphSeconds : _stats.TelegraphTime, _moveSpeed,
                _versionProfile != null ? _versionProfile.TelegraphScale : 1f);   // WP-032: wind-up emphasis from the version
            bool rise = ShouldRise(Rise, SpawnVisibilityClass);   // WP-040 (DEC-081): hybrid — only an off-screen spawn rises
            RoseOnSpawn = rise;
            _animator.ResetDriven(_tier, _skinned != null && _skinned.Look.IsValid ? _skinned.Look.fit : "A", rise, ref _random);
            // WP-096 P2: the driven body picked this spawn's move speed jitter from its spawn ordinal — walk at it (block body: 1)
            ApplySpeedJitter(_skinned != null && _animator.Driven ? _skinned.SpeedJitter : 1f);

            // WP-008 (DEC-043): the reward is decided HERE, once, from the version and the
            // plates just fitted. Die() reads this record and nothing else, so what the
            // fight does to the armour cannot move the number.
            TakeRewardSnapshot(config);

            _alive = true;
            _dying = false;
            _despawnCountdown = 0f;
            _riseRemaining = 0f;   // WP-030: the animator (WP-018) calls BeginRise when a rise clip plays; without one the body walks at once (WP-003 behaviour)

            DamageTargetRegistry.Register(transform, this, this, _bodyRadius, ZombieBodyBuilder.StandHeight * 0.5f);

            // WP-040 (ADR-040-1) spawn beats: rise (the clip plays) or land (up and walking at once).
            _lastAttackPhase = ZombieAttackPhase.None;
            _wasRising = IsRising;
            _landRaised = false;
            if (_wasRising)
            {
                RaiseBeat(EnemyCombatEventKind.SpawnRise, transform.position, transform.forward, ArmorSlot.None, _skinned != null && _skinned.IsRising, RiseClipSeconds);
            }
            else
            {
                RaiseLand(false);
            }
        }

        // ------------------------------------------------------------ WP-040 · ADR-040-1 beat layer
        [PrototypeOnly(PendingDecisions.None, "AP-009 rise clip length (README: 1.57 s) reported on the spawn_rise beat; the clip's rise_end is the real end.", true)]
        private const float RiseClipSeconds = 1.57f;

        private ZombieAttackPhase _lastAttackPhase;
        private bool _wasRising, _landRaised;

        private void RaiseBeat(EnemyCombatEventKind kind, Vector3 position, Vector3 direction, ArmorSlot region, bool animationDriven, float seconds)
        {
            EnemyCombatEvents.Raise(new EnemyCombatEventInfo(kind, transform, _tier, _lifeId, position, direction, region, animationDriven, seconds,
                _attack != null ? _attack.Style : ZombieAttackStyle.Swipe));   // WP-052: the style rides on every beat (attack beats read it)
        }

        private void RaiseLand(bool fromClip)
        {
            if (_landRaised) return;
            _landRaised = true;
            RaiseBeat(EnemyCombatEventKind.SpawnLand, transform.position, transform.forward, ArmorSlot.None, fromClip, 0f);
        }

        /// <summary>The swing's origin for the attack beats: the attack socket when the rig has one, else chest height.</summary>
        private Vector3 SwingOrigin => _rig != null && _rig.AttackSocket != null ? _rig.AttackSocket.position : transform.position + Vector3.up * 1.1f;

        /// <summary>
        /// Phase edges of the attack machine → beats. Runs right after the attack tick every frame;
        /// the edges are the machine's own transitions, so a clip-driven window (hit_window_start /
        /// hit_window_end) and the timer fallback raise the same beats (AnimationDriven tells them apart).
        /// </summary>
        private void RaiseAttackBeats(bool connected)
        {
            ZombieAttackPhase phase = _attack.Phase;
            ZombieAttackPhase last = _lastAttackPhase;
            _lastAttackPhase = phase;
            if (phase == last)
            {
                if (connected) RaiseBeat(EnemyCombatEventKind.AttackHit, SwingOrigin, transform.forward, ArmorSlot.None, _attack.AnimationDriven, 0f);
                return;
            }

            bool driven = _attack.AnimationDriven;
            Vector3 origin = SwingOrigin;
            if (phase == ZombieAttackPhase.Windup)
            {
                RaiseBeat(EnemyCombatEventKind.TelegraphStart, origin, transform.forward, ArmorSlot.None, driven, _attack.TelegraphSeconds);
            }
            else if (phase == ZombieAttackPhase.Active && last == ZombieAttackPhase.Windup)
            {
                RaiseBeat(EnemyCombatEventKind.AttackCommit, origin, transform.forward, ArmorSlot.None, driven, _attack.ActiveSeconds);
                if (connected) RaiseBeat(EnemyCombatEventKind.AttackHit, origin, transform.forward, ArmorSlot.None, driven, 0f);
            }
            else if (phase == ZombieAttackPhase.Recovery && last == ZombieAttackPhase.Active)
            {
                if (connected) RaiseBeat(EnemyCombatEventKind.AttackHit, origin, transform.forward, ArmorSlot.None, driven, 0f);
                else if (!_attack.ConnectedThisSwing) RaiseBeat(EnemyCombatEventKind.AttackMiss, origin, transform.forward, ArmorSlot.None, driven, _attack.RecoverySeconds);
            }
            // Active → None (stagger / death cancel) raises nothing here: the stagger / death beat covers it.
        }

        protected override void OnSpawned()
        {
            // WP-004: a new life. The gold reward is granted against this id at most once,
            // so a body recycled by the pool can never be paid for a previous life.
            _lifeId = EnemyLifeIds.Next();

            _rig.ResetToRestPose();

            _knockbackVelocity = Vector3.zero;
            _plannedVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _currentSpeed = 0f;
            ResetLocomotionV4();       // WP-096: speed jitter, ramp, heading rate and measured ground speed start fresh
            ResetSpecialReactions();   // WP-055: no net / transaction memory crosses a life
            _hasTargetSample = false; _targetSpeed = 0f;   // WP-076
            ResetHitResponse();        // WP-076 B3
            ResetShock();              // WP-076 B4
            ResetGel();                // WP-076 B5
            ResetRear();               // WP-076 B7
            ResetClipStep();           // WP-076 clip events
            ResetOutOfWorld();         // WP-061K
            ResetWaiting();   // WP-050
            _noticed = false;   // WP-052
            _alive = false;   // Initialise flips this on
            _dying = false;
            _lastTransactionId = 0;
            _lastMeta = default;
            _reward = default;   // WP-008: a recycled body never carries a previous life's reward
            _stats = default;    // WP-010: nor a previous life's tier profile

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
            _attack?.Cancel(GrabEndReason.Killed);   // WP-076: a body leaving without dying (pool clear, WP-061K/L) gives back its slot / grab
            UnregisterCorpse();   // WP-076: off the corpse ceiling list (buried, evicted or cleared)
            _bodyStateOverride = ZombieBodyState.Standing;
            ResetShock();   // WP-076 B4: a held shock death never resumes on a pooled body
            ResetGel();     // WP-076 B5
            UnregisterCrowdMembership();
            DamageTargetRegistry.Unregister(transform);
            s_live.Remove(this);

            _alive = false;
            _dying = false;

            // WP-006: strip the armour on the way OUT as well as on the way in (OnSpawned →
            // Reset), so a pooled body never carries a previous life's plates, broken or
            // not, for even one frame after it is handed out again.
            _body?.Armor.Clear();
            _lastTransactionId = 0;
            _lastMeta = default;
            _stats = default;   // WP-010: a body in the pool carries no tier profile (readout truth, ADR-010-4)
            _appearance?.ResetVersion();
            _versionProfile = null;   // WP-028
            _silhouette?.Hide();

            // WP-003 lifecycle contract: the body is going back to the pool. Reported
            // before Released so the round ledger is consistent when listeners read it.
            NotifySpawnLifecycleDespawned();

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
        /// DEC-021 override table first. That table ships empty in this package; an authored
        /// row takes precedence there (WP-010) and nowhere else.
        /// </summary>
        public void ApplyConfig(PrototypeConfig config)
        {
            if (config == null)
            {
                return;
            }

            // WP-010 (ADR-010-4): one resolution of the tier profile at the SPAWN round; the
            // cached fields below read from it, so what this body runs with is the record
            // it exposes (Stats) and nothing else.
            _stats = EnemyStatSnapshot.Resolve(config, _tier, _spawnRound, _versionProfile);

            EnemyTierSettings settings = config.Enemies.Find(_tier);
            if (settings != null)
            {
                _baseMoveSpeed = _stats.MoveSpeed;
                _moveSpeed = _baseMoveSpeed * _speedJitter;   // WP-096 P2: the life's jitter survives a live config edit
                _bodyRadius = _stats.BodyRadius;
                _separationMass = _stats.SeparationMass;

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

            // The snapshot's MaxHealth is config.EnemyHealth(tier, spawn round): the round
            // health multiplier of the spawn round, already folded in.
            float maxHealth = _stats.MaxHealth;
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
            if (_versionProfile != null)
            {
                _attack?.ApplyVersionTiming(_versionProfile.TelegraphScale, _versionProfile.RecoveryScale);   // WP-028
                _attack?.ApplyVersionCooldownFloor(_versionProfile.CooldownFloor);                            // WP-040: the Lunge band floor
            }
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
            // WP-028: the DEC-021 chance through the version's armour tendency (floor / multiplier); the table value is read, never written.
            float chance = EnemyVersionApplier.ArmourChance(config, _versionProfile, _tier, slot, _spawnRound);
            if (!_random.Chance(chance))
            {
                return;
            }

            Fit(armorConfig, slot);
        }

        private bool Fit(ArmorConfigSection armorConfig, ArmorSlot slot)
        {
            float durability = armorConfig != null ? armorConfig.DurabilityFor(slot) : 0f;
            if (durability <= 0f)
            {
                return false;
            }

            _body.Armor.Equip(slot, durability);
            _appearance.Apply(slot, ArmorVisualState.Present, 1f);
            return true;
        }

        /// <summary>
        /// WP-006 test / probe harness: replace whatever the spawn roll fitted with an
        /// exact 0/1/2/3 composition, at full durability from the same PrototypeOnly
        /// config the roll uses. Only on a living enemy; never called by production spawn
        /// (RollArmor is untouched), so it cannot change the shipped odds. Returns the
        /// number of pieces fitted.
        /// </summary>
        public int FitArmorComposition(bool helmet, bool body, bool leg)
        {
            if (!_alive || _body == null)
            {
                return 0;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            ArmorConfigSection armorConfig = config != null ? config.Enemies.Armor : null;

            _body.Armor.Clear();
            _appearance.HideAllArmor();

            int fitted = 0;
            if (helmet && Fit(armorConfig, ArmorSlot.Helmet)) fitted++;
            if (body && Fit(armorConfig, ArmorSlot.Body)) fitted++;
            if (leg && Fit(armorConfig, ArmorSlot.Leg)) fitted++;

            // WP-008: this harness REPLACES the spawn composition, so the spawn-time reward
            // record is retaken from the new plates. It is the only path that retakes it,
            // and it is not a production path; combat damage never reaches here.
            TakeRewardSnapshot(config);
            return fitted;
        }

        /// <summary>
        /// WP-008: freeze the reward for this life from the version and the plates currently
        /// fitted (called right after the spawn roll). Deterministic; no randomness.
        /// </summary>
        private void TakeRewardSnapshot(PrototypeConfig config)
        {
            if (config == null)
            {
                // No table to price from: no snapshot, so Die() keeps the WP-004 fallback
                // (a zero-valued "valid" snapshot would pay nothing and hide the fault).
                _reward = default;
                return;
            }

            ArmorSet armor = _body != null ? _body.Armor : null;
            var composition = armor != null
                ? new SpawnArmorComposition(armor.IsEquipped(ArmorSlot.Helmet), armor.IsEquipped(ArmorSlot.Body), armor.IsEquipped(ArmorSlot.Leg))
                : SpawnArmorComposition.None;

            _reward = GoldRewardCalculator.Snapshot(_tier, composition, config);
        }

        /// <summary>Region hitbox collider (test/probe readout: aim points for the real weapon path). Null when the rig is incomplete.</summary>
        public Collider HitboxFor(BodyRegion region)
        {
            if (_rig == null)
            {
                return null;
            }

            switch (region)
            {
                case BodyRegion.Head: return _rig.HeadHitbox;
                case BodyRegion.LowerBody: return _rig.LowerHitbox;
                default: return _rig.UpperHitbox;
            }
        }

        /// <summary>The visual state last applied to a slot (IArmorVisual contract). Test/probe readout.</summary>
        public ArmorVisualState ArmorVisualStateOf(ArmorSlot slot)
        {
            return _appearance != null ? _appearance.StateOf(slot) : ArmorVisualState.Absent;
        }

        /// <summary>Attachment anchors for the three armour pieces (REQ-115).</summary>
        public ArmorAnchors ArmorAnchors => _appearance != null ? _appearance.Anchors : default;

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

            if (TickOutOfWorld() || TickStuck())
            {
                return;   // WP-061K / WP-061L: left the world or stuck → despawned, no reward
            }

            TickCombat(dt);
            TickSpecialVisual();   // WP-055 / WP-032E: Netted / Dazed visual follows the reaction timers
            _animator.Tick(dt);
        }

        // WP-076: player speed estimate for the attack selector
        private Vector3 _lastTargetSample;
        private bool _hasTargetSample;
        private float _targetSpeed;

        private static Vector3 FlatForward(Transform t)
        {
            Vector3 f = t.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
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

                // WP-076: the player's ground speed (smoothed), read by the bite_grab gate (standing still)
                Vector3 tp = target.position; tp.y = 0f;
                if (_hasTargetSample && dt > 1e-4f) _targetSpeed = Mathf.Lerp(_targetSpeed, (tp - _lastTargetSample).magnitude / dt, Mathf.Clamp01(8f * dt));
                _lastTargetSample = tp; _hasTargetSample = true;
            }

            // ------------------------------------------------------------------ rise (WP-030)
            // WP-040: the rise → walk edge (rise_end on the clip, or the timer) is the spawn_land beat.
            bool risingNow = IsRising;
            if (_wasRising && !risingNow) RaiseLand(_skinned != null);
            _wasRising = risingNow;

            if (_riseRemaining > 0f)
            {
                _riseRemaining -= dt;
                _plannedVelocity = Vector3.zero;
                ConsumeCrowdCorrection();
                _verticalVelocity = _controller.isGrounded ? -2f : _verticalVelocity + Gravity * dt;
                _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * dt);
                return;
            }

            // ------------------------------------------------------------------ WP-052 · Ultra notice
            // Once per life, the first time the Executioner has the player inside its notice range: the
            // "noticed you" moment (readout now; an upper-body clip is an AP-009 reserve — no clip in v003/v004,
            // so the moment is carried by the WP-022B ULTRA NEARBY chip and the WP-048 aggro voice).
            if (_tier == EnemyTier.Ultra && !_noticed && target != null && distance <= UltraNoticeRange)
            {
                _noticed = true;
            }

            // ------------------------------------------------------------------ attack
            // WP-018: a body still rising from its spawn (AP-009 rise clip) neither walks nor swings.
            // WP-040: the armour-break heavy stagger holds the body until the clip's stagger_end (timer cap inside).
            bool staggering = _animator.IsStaggering || (_skinned != null && (_skinned.IsRising || _skinned.IsHeavyStaggering));
            // WP-055: a rooted body (Ofuda net, WP-032E Netted = the clip is frozen) neither walks nor swings; a slowed one does both.
            if (IsRooted && _attack.IsAttacking) _attack.Cancel();
            staggering |= IsRooted;
            // WP-076 B4: Electrocuted holds the body the same way (no walk, no swing; hitboxes stay on).
            if (IsElectrocuted && _attack.IsAttacking) _attack.Cancel(GrabEndReason.Staggered);
            staggering |= IsElectrocuted;
            // WP-076 B5: gel (slide / fall / downed / getting up) holds walking and swinging; the slide moves the body below.
            Vector3 gelVelocity = TickGel(dt, out bool gelHolds);
            if (gelHolds && _attack.IsAttacking) _attack.Cancel(GrabEndReason.Staggered);
            staggering |= gelHolds;

            bool rearLeadOk = TickRear(target, distance);   // WP-076 B7 §6: rear_threat beat + the fairness lead before a swing from behind
            if (!staggering && target != null && rearLeadOk)
            {
                // WP-076 §1.3: the selector picks this swing (tier / distance / angle / variety / crowd slots)
                var ctx = new AttackContext
                {
                    Distance = distance,
                    AngleToPlayer = Vector3.SignedAngle(FlatForward(transform), toTarget, Vector3.up),
                    PlayerFacingAway = Vector3.Angle(FlatForward(target), -toTarget),
                    PlayerSpeed = _targetSpeed,
                    Now = Time.time,
                };
                _attack.TrySelectAndBegin(ref ctx, ref _random);
            }

            BindClipEvents();   // WP-076: AP-022 attack_step / grab_end (lazy; the driven visual finds its events after spawn)
            bool connected = _attack.Tick(dt, this, _tier, _animator);
            if (connected) NoteRearHit();   // WP-076 B8 telemetry
            RaiseAttackBeats(connected);   // WP-040 (ADR-040-1)

            // ---------------------------------------------------------------- steering
            // OWN locomotion first: follow the NavMesh path towards the target. The crowd
            // layer only ever contributes a CORRECTION on top of this, so a lone zombie on
            // an empty map still walks.
            // WP-096 P6 / P7: the path heading and the asked speed are the INTENT (commanded); the own velocity is that intent
            // after the tier acceleration and, with VelocityFollowsFacing, along the body's facing.
            Vector3 walkHeading = Vector3.zero;
            float walkSpeed = 0f;

            bool canWalk = !staggering && _attack.Phase != ZombieAttackPhase.Recovery;
            if (canWalk && target != null && TickNavigation(selfPosition, CrowdFeelGoal(target.position), out Vector3 heading))   // WP-054: flank goal while far
            {
                float speed = _moveSpeed * _navSpeedFactor;   // WP-030: chokepoint queue slows the approach, never stops the threat
                speed *= WallFrictionFactor();                // WP-054: sliding along world geometry keeps 80 % (hysteresis)
                speed *= SpecialSlowFactor;                   // WP-055: Ofuda net (root / slow by tier) and the Ultra's Tide drag
                speed *= SuppressionFactor;                   // WP-076 B3: LMG suppression ×0.70
                if (_attack.IsGrabbing) speed = 0f;           // WP-076 §1.4: the holding body stands; the crowd flows around it

                // Stop pushing into the player once inside attack range; the swing closes
                // the last gap. Without this the swarm compresses into a solid wall.
                if (distance < _attack.Range * AttackRangeApproachStop)
                {
                    speed *= ClosedInSpeedScale;
                }

                walkHeading = heading;
                walkSpeed = speed;
            }

            Vector3 ownVelocity = SteerOwnVelocity(canWalk, IsRooted || IsElectrocuted || gelHolds, walkHeading, walkSpeed, distance, dt);
            Vector3 commanded = walkHeading * walkSpeed;
            float transitionYawStep = 0f;
            Vector3 transitionVelocity = Vector3.zero;
            // WP-096 R3 (REQ-046, S-02 10.10 root cause): a body engaging its target (UpdateWaiting's "engaging") plays no p3 start /
            // stop / turn — the start_walk after a swing's Recovery drove the own velocity to the clip's ~0 opening for ~0.3 s, the
            // crowd layer pushed the front line out and aside, and the live wall let the player through (1.24 m, R2 0.60 m). Close
            // pressure stays on the WP-003 path, as v4's facing rule already leaves it (FacingRuleMinDistance); the stumble_recover
            // reaction still plays — its clip and its attach-out crossfade back to the gait (gt S1: the clip name alone let the attach-out
            // be cut the frame the clip ended).
            bool stumbleRecover = _skinned != null && _skinned.StumbleRecoverActive;
            bool engagingTarget = distance <= _attack.Range * WaitEngageRangeFactor;
            bool transitionMotor = _skinned != null && _skinned.StepTransition(walkHeading, walkSpeed, _groundForward,
                (canWalk || stumbleRecover) && !_attack.IsAttacking && !IsRooted && !IsElectrocuted && !gelHolds && (!engagingTarget || stumbleRecover),
                dt, out transitionVelocity, out transitionYawStep);
            if (transitionMotor) { ownVelocity = transitionVelocity; _steerSpeed = transitionVelocity.magnitude; }

            // The Runner's lunge adds committed forward speed during its active window.
            float impulse = _attack.ForwardImpulse;
            if (impulse > 0f)
            {
                Vector3 lunge = transform.forward * (impulse * SpecialSlowFactor);   // WP-055: a netted Runner's lunge is held by the same net
                ownVelocity += lunge;
                commanded += lunge;
            }

            Vector3 shuffle = QueueShuffle(dt);   // WP-054: a queued light body sways a little so the queue reads alive
            ownVelocity += shuffle;
            commanded += shuffle;
            _commandedVelocity = commanded;
            _plannedVelocity = ownVelocity;

            // ICrowdMember contract: final = own locomotion + correction.
            Vector3 correction = ConsumeCrowdCorrection();
            Vector3 velocity = ownVelocity + correction;

            // ----------------------------------------------------------------- physics
            velocity += _knockbackVelocity;
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Mathf.Clamp01(KnockbackDamping * dt));
            velocity += TickHitResponse(dt);   // WP-076 B3: class knockback (ease-out) + LMG sustained push
            velocity += gelVelocity;           // WP-076 B5: the gel slide keeps its momentum (no steering)
            velocity += TickClipStep(dt);      // WP-076: the clip's attack_step root step (clips play in place)

            _verticalVelocity = _controller.isGrounded ? -2f : _verticalVelocity + Gravity * dt;

            Vector3 motion = velocity;
            motion.y = _verticalVelocity;
#if UNITY_EDITOR
            bool observeMove = DiagnosticMoveBody == this && DiagnosticMoveObserved != null;
            DiagnosticMove observation = default;
            if (observeMove)
            {
                observation = new DiagnosticMove
                {
                    callId = ++_diagnosticMoveCall, frame = Time.frameCount, time = Time.time, dt = dt,
                    lifeId = _lifeId, ownVelocity = ownVelocity, crowdVelocity = correction,
                    finalVelocity = motion, requestedDelta = motion * dt, before = transform.position,
                    steeringCorner = _steeringCorner, hasSteeringCorner = _hasSteeringCorner,
                    groundedBefore = _controller.isGrounded
                };
                DiagnosticMoveObserved(observation);
            }
#endif
            Vector3 beforeMove = transform.position;   // WP-096 P1: the real ground motion is measured from here
            CollisionFlags moveFlags = _controller.Move(motion * dt);
            NoteWallContact(moveFlags);   // WP-055E (M3 K1): side-contact flag instead of the allocating OnControllerColliderHit message
#if UNITY_EDITOR
            if (observeMove)
            {
                observation.phase = 1;
                observation.afterMove = transform.position;
                observation.groundedAfterMove = _controller.isGrounded;
                observation.flags = moveFlags;
                DiagnosticMoveObserved(observation);
            }
#endif

            // A strong push can walk the body off the mesh; put it back.
            ClampToWalkable(correction);
#if UNITY_EDITOR
            if (observeMove)
            {
                observation.phase = 2;
                observation.afterClamp = transform.position;
                observation.groundedAfterClamp = _controller.isGrounded;
                DiagnosticMoveObserved(observation);
            }
#endif

            // ------------------------------------------------------------------ facing
            // Faces its OWN intent, not the crowd correction: a zombie being shouldered
            // aside should keep looking where it is going instead of pirouetting.
            // WP-096: the intent before acceleration (the path heading) — with VelocityFollowsFacing the own velocity follows the
            // facing, so facing the own velocity would chase its own tail.
            Vector3 facing = commanded;
            facing.y = 0f;

            // While winding up or striking, face the target rather than the movement, so
            // the telegraph always points at the player it threatens.
            if (_attack.IsAttacking && target != null && toTarget.sqrMagnitude > 0.0001f)
            {
                facing = toTarget;
            }

            if (transitionMotor)
            {
                transform.rotation = Quaternion.AngleAxis(transitionYawStep, Vector3.up) * transform.rotation;
                _yawRate = dt > 0f ? transitionYawStep / dt : 0f;
            }
            else if (facing.sqrMagnitude > 0.0004f)
            {
                TurnTowards(facing, dt);   // WP-096 P7: angular velocity capped at the tier turn rate, angular acceleration limited
            }
            else
            {
                _yawRate = 0f;
            }

            // ---------------------------------------------------------------- animation
            // _currentSpeed stays the REQUESTED planar speed (own + correction + pushes): the pose / stagger bonus, the gel slide
            // entry, the queue stall test and the death impulse read it (gameplay, unchanged). WP-096 P1: the driven gait reads
            // the REAL forward ground speed instead (MeasureGroundMotion) — pushes never reach the cadence.
            Vector3 planar = velocity;
            planar.y = 0f;
            _currentSpeed = planar.magnitude;
            _animator.SetLocomotion(_moveSpeed > 0f ? _currentSpeed / _moveSpeed : 0f, _currentSpeed);
            MeasureGroundMotion(beforeMove, ownVelocity, dt);
            if (_skinned != null)
            {
                _skinned.SetSpeed(_groundForward, _groundLateral, dt);
            }

            UpdateWaiting(commanded, distance, dt);   // WP-050: WP-032 queue / idle-in-crowd wait state (the intent, as before WP-096)
        }

        // ------------------------------------------------------------ WP-050 · waiting (WP-032 SetWaiting)
        [PrototypeOnly(PendingDecisions.None, "WP-050: seconds a body must want to walk (own velocity ≥ WaitWantsSpeedShare × speed) while going nowhere (< WaitStalledSpeed) before the queue idle plays; cleared after WaitClearSeconds of real movement. Hysteresis so the idle does not flicker in a jostling crowd. WP-054: the stall threshold is rolled per life (CrowdFeelSettings band).", true)]
        private const float WaitClearSeconds = 0.25f, WaitStalledSpeed = 0.15f, WaitWantsSpeedShare = 0.4f;
        private float _waitStallSeconds = 0.6f;
        private float _shufflePeriod = 1.8f, _shufflePhase;
        /// <summary>WP-054 readouts.</summary>
        public float WaitStallSeconds => _waitStallSeconds;
        public Vector3 LastShuffle { get; private set; }

        /// <summary>WP-054: lateral sway while queued (light tiers only), bounded by amplitude; zero otherwise.</summary>
        private Vector3 QueueShuffle(float dt)
        {
            LastShuffle = Vector3.zero;
            if (!_waiting || _pathFollower == null || !_pathFollower.IsQueued) return Vector3.zero;
            CrowdFeelSettings feel = CrowdFeelSettings.Active;
            if (feel.IsHeavy(_tier)) return Vector3.zero;
            float w = 2f * Mathf.PI / Mathf.Max(0.2f, _shufflePeriod);
            // velocity of a sine displacement with amplitude A: A·w·cos(wt + φ); A = ShuffleSpeed / w capped at ShuffleAmplitude
            float amplitude = Mathf.Min(feel.ShuffleAmplitude, feel.ShuffleSpeed / w);
            float v = amplitude * w * Mathf.Cos(Time.time * w + _shufflePhase);
            Vector3 side = Vector3.Cross(Vector3.up, transform.forward);
            LastShuffle = side * v;
            return LastShuffle;
        }
        [PrototypeOnly(PendingDecisions.None, "WP-050: a body within this × AttackRange of its target is engaging, not queued — it holds the threat pose (DEC-032), never the queue idle.", true)]
        private const float WaitEngageRangeFactor = 1.6f;

        private float _stallSeconds, _moveSeconds;
        private bool _waiting;

        [PrototypeOnly(PendingDecisions.None, "WP-052: distance at which the Executioner 'notices' the player once per life (upper-body clip when AP-009 ships it).", true)]
        private const float UltraNoticeRange = 12f;
        private bool _noticed;
        /// <summary>WP-052 readout: the Ultra notice fired this life.</summary>
        public bool Noticed => _noticed;

        /// <summary>WP-050: the body is holding the WP-032 queue idle (chokepoint queue or stalled in the crowd). Readout / tests.</summary>
        public bool IsWaiting => _waiting;

        /// <summary>
        /// WP-050: the WP-030 chokepoint queue (follower.IsQueued) and a body stalled behind others
        /// (wants to walk, does not move) both read as "waiting" for the driven body: WP-032's
        /// SetWaiting → the v002 idle_queue Wait state. Attack / stagger / rise never wait.
        /// </summary>
        private void UpdateWaiting(Vector3 ownVelocity, float distanceToTarget, float dt)
        {
            bool queued = _pathFollower != null && _pathFollower.IsQueued;
            bool wants = ownVelocity.sqrMagnitude >= (_moveSpeed * WaitWantsSpeedShare) * (_moveSpeed * WaitWantsSpeedShare);
            bool engaging = distanceToTarget <= _attack.Range * WaitEngageRangeFactor;   // blocked by the player at the front line ≠ queued behind others
            // WP-096 (P12, S-03 08.10): "going nowhere" is read from the REAL ground motion (P1's measurement) as well as the requested
            // speed. _currentSpeed is the requested planar speed (own + crowd correction + pushes): a body jammed in the back row keeps
            // asking ≥ 0.15 m/s while the CharacterController moves it nowhere, so it never counted as stalled — never waiting, and the
            // stuck watchdog (TickStuck: _waiting exempts) could remove it. Since v4 the own walk ramps and slows on heading error, which
            // makes the requested speed in a jam noisier still. min(requested, real): every body that was stalled before still is.
            float movingSpeed = Mathf.Min(_currentSpeed, RealGroundSpeed);
            bool stalled = wants && !engaging && movingSpeed < WaitStalledSpeed;
            bool busy = _attack.IsAttacking || _animator.IsStaggering || IsRising || engaging;

            if (stalled && !busy) { _stallSeconds += dt; _moveSeconds = 0f; }
            else if (movingSpeed >= WaitStalledSpeed || busy) { _moveSeconds += dt; if (_moveSeconds >= WaitClearSeconds) _stallSeconds = 0f; }

            bool waiting = !busy && (queued || _stallSeconds >= _waitStallSeconds);
            if (waiting != _waiting)
            {
                _waiting = waiting;
                _animator.SetWaiting(waiting);
            }
        }

        private void ResetWaiting()
        {
            _stallSeconds = 0f;
            _moveSeconds = 0f;
            if (_waiting) { _waiting = false; _animator?.SetWaiting(false); }
        }

        private void TickDying(float dt)
        {
            // WP-076 §2.3 (S-06 A3.7): the driven body's corpse belongs to the animation driver (clip root / ragdoll) —
            // S-03 writes no transform after death, only the despawn countdown and the floor guard below. The block
            // rig keeps its procedural fall and the knockback slide (plain transform move; the controller is off).
            if (TickCorpseFloor()) return;
            if (TickShockDeathHold(dt)) return;   // WP-076 B4: a lethal shock convulses before the fall
            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, Mathf.Clamp01(KnockbackDamping * dt));

            if (_animator.Driven)
            {
                // driven: no slide
            }
            else if (_controller != null && _controller.enabled)
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
            if (_skinned != null)
            {
                _skinned.SetSpeed(0f, dt);
            }

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

            if (RefuseDuplicateTransaction())
            {
                return default;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            SnapshotBefore(out float healthBefore, out float helmetBefore, out float bodyBefore, out float legBefore);

            // WP-069 × WP-077 (S-02 link.gel.slickfloor): a body lying / sliding in gel takes more from every source.
            DamageHit scaled = hit;
            Func<ZombieEnemy, float> incoming = IncomingDamageScale;
            if (incoming != null) scaled.Amount *= incoming(this);
            scaled.Amount *= DD.Prototype.Rounds.RunTreeHooks.StaggeredScale(this);   // WP-077 ks.impact.stagger

            DamageResult result = _body.ApplyDirectHit(in scaled, config != null ? config.Enemies.Armor : null);

            BuildMeta(DamageKind.Direct, DamageRegions.SlotFor(scaled.Region),
                healthBefore, helmetBefore, bodyBefore, legBefore);
            ProcessResult(in scaled, in result, scaled.Point, scaled.Direction);
            return result;
        }

        /// <summary>
        /// WP-006 event contract: the same weapon-side transaction (one shot / one blast /
        /// one external hit) reaches one enemy at most once. HitscanEngine's per-shot
        /// target list already guarantees this on its side; this is the receiver's own
        /// guard, so a duplicate can never break, reward or dress twice. Outside a
        /// transaction (id 0: debug Kill, tests calling ApplyHit directly) nothing is refused.
        /// </summary>
        private bool RefuseDuplicateTransaction()
        {
            int tx = DamageTransaction.Current;
            if (tx == 0)
            {
                return false;
            }

            if (tx == _lastTransactionId)
            {
                s_duplicateApplicationCount++;
                return true;
            }

            _lastTransactionId = tx;
            return false;
        }

        private void SnapshotBefore(out float health, out float helmet, out float body, out float leg)
        {
            health = _body.Health;
            helmet = _body.Armor.Current(ArmorSlot.Helmet);
            body = _body.Armor.Current(ArmorSlot.Body);
            leg = _body.Armor.Current(ArmorSlot.Leg);
        }

        private void BuildMeta(DamageKind kind, ArmorSlot slot,
            float healthBefore, float helmetBefore, float bodyBefore, float legBefore)
        {
            ArmorSet armor = _body.Armor;
            _lastMeta = new DamageEventMeta(
                _lifeId, DamageTransaction.Current, DamageEventIds.Next(), kind, slot,
                healthBefore, _body.Health,
                helmetBefore, armor.Current(ArmorSlot.Helmet),
                bodyBefore, armor.Current(ArmorSlot.Body),
                legBefore, armor.Current(ArmorSlot.Leg),
                _body.LastHealthDamageIn(BodyRegion.Head),
                _body.LastHealthDamageIn(BodyRegion.UpperBody),
                _body.LastHealthDamageIn(BodyRegion.LowerBody),
                _body.LastArmorDamageIn(BodyRegion.Head),
                _body.LastArmorDamageIn(BodyRegion.UpperBody),
                _body.LastArmorDamageIn(BodyRegion.LowerBody));
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

            if (RefuseDuplicateTransaction())
            {
                return default;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            SnapshotBefore(out float healthBefore, out float helmetBefore, out float bodyBefore, out float legBefore);

            Func<ZombieEnemy, float> incoming = IncomingDamageScale;
            if (incoming != null) totalDamage *= incoming(this);   // WP-069 × WP-077 link.gel.slickfloor
            totalDamage *= DD.Prototype.Rounds.RunTreeHooks.StaggeredScale(this);   // WP-077 ks.impact.stagger

            DamageResult result = _body.ApplyExplosion(
                totalDamage,
                config != null ? config.Enemies.Armor : null,
                config != null ? config.Enemies.Explosion : null);

            BuildMeta(DamageKind.Explosion, ArmorSlot.None, healthBefore, helmetBefore, bodyBefore, legBefore);

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
                ImpactForce = DamageHitContext.GroundFire ? DamageForce.Chain : DamageForce.ExplosionCore,   // WP-077: a burning-ground tick never staggers
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

            // WP-076 §1.4: one strong hit (≥ 0.85, e.g. a single pellet) on the holding body frees the player; the
            // REQ-124 stagger gate is independent (the escape does not spend it)
            if (!result.Killed && _attack.IsGrabbing) _attack.TryEscapeGrab(hit.HitWeight);   // review R1-A #10: ungoverned

            // -------------------------------------------------------- armour readability
            RefreshArmorVisual(ArmorSlot.Helmet);
            RefreshArmorVisual(ArmorSlot.Body);
            RefreshArmorVisual(ArmorSlot.Leg);

            for (int i = 0; i < _body.LastBreakCount; i++)
            {
                ArmorSlot slot = _body.LastBreak(i);
                _appearance.Apply(slot, ArmorVisualState.Broken, 0f);

                DamageEvents.RaiseArmorBroken(new ArmorBreakInfo(
                    transform, _tier, slot, DamageRegions.RegionFor(slot), point, direction, hit.Source,
                    _body.Armor.ProtectingCount,
                    PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Feedback.HitStopArmorBreak
                        : FallbackHitStopArmorBreak,
                    in _lastMeta));
                RaiseBeat(EnemyCombatEventKind.ArmorBreak, point, direction, slot, _skinned != null, 0f);   // WP-040
                // WP-040: the break's heavy stagger (ArmorVisualBinder → hit_heavy_stagger clip) is a beat of its own.
                if (_skinned != null && _appearance.HasArmor && !result.Killed) RaiseBeat(EnemyCombatEventKind.Stagger, point, direction, ArmorSlot.None, true, ZombieSkinnedVisual.HeavyStaggerCapSeconds);
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
                // WP-076 B3 §3.1: the weapon class picks the knockback profile, LMG suppression and the sniper flinch floor.
                bool classKnown = WeaponClassifier.TryClassify(hit.Source, hit.HitWeight, _lastMeta.Kind == DamageKind.Debug, out DD.Prototype.Core.Weapons.WeaponClass weaponClass);
                float flinch = ApplyWeaponResponse(in hit, in reaction, direction, classKnown, weaponClass);

                // WP-032C: the driven body picks Hit_<dir>_<tier> from the local hit itself (AddFlinch passes it).
                _animator.AddFlinch(direction, flinch, result.Region, transform);

                if (reaction.Stagger)
                {
                    reaction.StaggerDuration = DD.Prototype.Rounds.RunTreeHooks.TreeStaggerSeconds(reaction.StaggerDuration);   // WP-077 seal / fork.smg_2
                    _animator.BeginStagger(direction, reaction.StaggerIntensity, reaction.StaggerDuration, transform);
                    // WP-052 · Special B: the REQ-124 stagger reads as the Runner's stumble (v003 hit_special_stumble, base layer;
                    // S-06's PlayStumble hook — the block rig ignores it, the stagger timer is unchanged).
                    if (_tier == EnemyTier.Special)
                    {
                        _animator.PlayStumble();
                        BeginStumbleBrake();   // WP-096 P6: the stumble keeps the Runner's momentum instead of a one-frame stop
                    }
                    _attack.Cancel();
                    RaiseBeat(EnemyCombatEventKind.Stagger, point, direction, ArmorSlot.None, _animator.Driven, reaction.StaggerDuration);   // WP-040
                    DD.Prototype.Rounds.RunTreeHooks.AfterTreeStagger(this);   // WP-077 seal.smg_2: a staggered Runner falls
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
                _body.HealthRatio, reaction.Stagger, in _lastMeta));

            if (result.Killed)
            {
                Die(in hit, in result, point, direction, heavyTier, reaction.SuggestedHitStop);
            }
        }

        private void RefreshArmorVisual(ArmorSlot slot)
        {
            ArmorSet armor = _body.Armor;
            if (!armor.IsEquipped(slot) || armor.IsBroken(slot))
            {
                // Broken is applied by the break loop (once, with its event); Absent never
                // needs refreshing.
                return;
            }

            float ratio = armor.Ratio(slot);
            _appearance.Apply(slot, ratio >= 1f ? ArmorVisualState.Present : ArmorVisualState.Damaged, ratio);
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
            // WP-054: corpse linger by tier with jitter; near the body ceiling (DEC-023: corpses count) it shortens.
            {
                CrowdFeelSettings feel = CrowdFeelSettings.Active;
                bool nearCeiling = EnemyFactory.Capacity > 0 && EnemyFactory.SpawnedCount >= EnemyFactory.Capacity - feel.CorpseCapMargin;
                _despawnCountdown = feel.CorpseLingerFor(_tier, _random.Range(0f, 1f), nearCeiling, QualityTiers.Current);   // WP-076: v2, × quality tier
                LastCorpseLinger = _despawnCountdown;
            }

            bool shockHeld = BeginShockDeathHold(in hit);   // WP-076 B4: State = Electrocuted; a mid-tremble kill holds the fall for the remainder
            if (shockHeld) SetSpecialVisual(ZombieSpecialVisual.Electrocuted);   // S-06 hit_electro_loop holds while the fall waits

            // WP-076 §2 / DEC-096: the killing push for the driver (S-06 picks the family). Read BEFORE the controller goes off.
            // a pellet carries the shell's weight (§3.1: all pellets inside 4 m → Force ≥ 3.0)
            float deathForce = hit.Source == DamageSourceKind.Pellet ? Mathf.Max(hit.ImpactForce, _reaction.ShoveCharge) : hit.ImpactForce;
            LastDeathImpulse = DeathImpulse.Build(direction, transform.forward, deathForce, hit.Source, _lastMeta.Kind == DamageKind.Debug,
                result.Region, hit.Point, _currentSpeed, BodyState, hit.HitWeight);

            // WP-003 lifecycle contract: from here on this body is a corpse. The round
            // ledger stops counting it as alive NOW, not when the corpse despawns (REQ-012).
            NotifySpawnLifecycleDied();

            // Out of the target registry immediately: a corpse must not eat a chain arc, a
            // ricochet or a share of the next explosion.
            DamageTargetRegistry.Unregister(transform);
            UnregisterCrowdMembership();
            _attack.Cancel(GrabEndReason.Killed);   // WP-076: a held grab ends as Killed (no second bite share)
            ResetWaiting();   // WP-050: a corpse never holds the queue idle

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

            // WP-032C: the driven body picks the tier death (Special A/B, Ultra, side_R) from the tier and the push itself.
            if (!shockHeld)
            {
                _animator.SetDeathImpulse(LastDeathImpulse);
                _animator.BeginDeath(LastDeathImpulse.Direction, LastDeathImpulse.Force, result.Region, heavyTier, transform, ref _random);
            }
            RegisterCorpse();   // WP-076: the corpse ceiling (oldest buried first) — after BeginDeath so an evicted body is never this one

            PrototypeConfig config = PrototypeConfig.Active;

            // WP-008: the gold is the SPAWN snapshot's total, never a fresh read of the
            // config or of the (possibly broken) armour. A life that somehow has no
            // snapshot falls back to the WP-004 flat value.
            int gold = _reward.IsValid ? _reward.Total : (config != null ? config.GoldReward(_tier) : 0);
            int xp = config != null ? config.KillXp(_tier) : 0;

            float hitStop = suggestedHitStop;
            if (heavyTier && config != null)
            {
                hitStop = Mathf.Max(hitStop, config.Feedback.HitStopHeavyDeath);
            }

            DamageEvents.RaiseEnemyDied(new EnemyDeathInfo(
                transform, _tier, transform.position, direction, result.Region, hit.Source,
                DamageRegions.IsCritical(in result, hit.Source), heavyTier, _spawnRound,
                gold, xp, _body.LastOverkill, hitStop, _lifeId, in _lastMeta, in _reward));
            RaiseBeat(EnemyCombatEventKind.Death, transform.position, direction, ArmorSlot.None, _skinned != null, 0f);   // WP-040
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

            SnapshotBefore(out float healthBefore, out float helmetBefore, out float bodyBefore, out float legBefore);
            DamageResult result = _body.Execute(BodyRegion.UpperBody);
            BuildMeta(DamageKind.Debug, ArmorSlot.None, healthBefore, helmetBefore, bodyBefore, legBefore);
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

        private static readonly ArmorSlot[] s_breakScratch = new ArmorSlot[3];

        /// <summary>Debug: shatter every remaining plate, raising one event per piece.</summary>
        public int BreakAllArmor()
        {
            if (_body == null || !_alive)
            {
                return 0;
            }

            ArmorSlot[] broken = s_breakScratch;
            int count = _body.Armor.BreakAll(broken);

            Vector3 point = transform.position + Vector3.up * 1.1f;
            Vector3 direction = transform.forward;

            for (int i = 0; i < count; i++)
            {
                _appearance.Apply(broken[i], ArmorVisualState.Broken, 0f);

                DamageEvents.RaiseArmorBroken(new ArmorBreakInfo(
                    transform, _tier, broken[i], DamageRegions.RegionFor(broken[i]), point, direction,
                    DamageSourceKind.Bullet, _body.Armor.ProtectingCount,
                    PrototypeConfig.Active != null
                        ? PrototypeConfig.Active.Feedback.HitStopArmorBreak
                        : FallbackHitStopArmorBreak));
                RaiseBeat(EnemyCombatEventKind.ArmorBreak, point, direction, broken[i], _skinned != null, 0f);   // WP-040
                if (_skinned != null && _appearance.HasArmor) RaiseBeat(EnemyCombatEventKind.Stagger, point, direction, ArmorSlot.None, true, ZombieSkinnedVisual.HeavyStaggerCapSeconds);   // WP-040: the binder plays the heavy stagger here too
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
