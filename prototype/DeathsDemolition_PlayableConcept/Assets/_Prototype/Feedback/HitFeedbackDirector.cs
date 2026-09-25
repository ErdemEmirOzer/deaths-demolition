using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.UI;
using UnityEngine;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Feedback
{
    /// <summary>Everything the director needs to dress one landed hit on an enemy.</summary>
    public struct EnemyHitFeedbackInfo
    {
        /// <summary>Where the hit landed, in world space.</summary>
        public Vector3 Point;

        /// <summary>Travel direction of the shot. Feeds the directional reaction of REQ-122.</summary>
        public Vector3 Direction;

        /// <summary>Surface normal at the hit, or -Direction when the trace did not supply one.</summary>
        public Vector3 Normal;

        /// <summary>What the damage system decided. The single source of truth for intensity.</summary>
        public DamageResult Result;

        /// <summary>How heavy the firing weapon is. Gates hit-stop.</summary>
        public WeaponPower Power;

        /// <summary>Stable id of the target, used to merge repeat feedback on one enemy.</summary>
        public int TargetId;

        /// <summary>Optional. Used only for the death-weight fallback heuristic.</summary>
        public Transform TargetRoot;

        /// <summary>How much death spectacle the target earns.</summary>
        public FeedbackWeight Weight;

        /// <summary>Physical impulse behind the hit. Scales the burst, not the damage.</summary>
        public float ImpactForce;

        /// <summary>True when this came from an explosion rather than a projectile.</summary>
        public bool FromExplosion;

        /// <summary>One trigger/blast/collapse presentation group. Zero is resolved to an isolated event.</summary>
        public int PresentationGroupId;

        /// <summary>Snapshot latched by the producer. Zero lets the director use the current revision.</summary>
        public ulong SnapshotRevision;
    }

    /// <summary>
    /// Steps 5 to 12 of the twelve-step hit chain live here. Steps 1 to 4 (recoil,
    /// muzzle flash, mechanical movement, tracer) belong to the weapon, which calls
    /// <see cref="ReportMuzzleFlash"/> and <see cref="ReportTracer"/> for the parts that
    /// need a pooled visual.
    ///
    /// One object owns the whole chain on purpose. The rules the request cares about -
    /// the intensity hierarchy, the hit-stop discipline, the crowd ceiling, the camera
    /// limit - are all statements about how the *combination* behaves. Spread across
    /// eight scripts they become eight independent opinions and the crowd stops being
    /// readable. Here they are one function.
    ///
    /// Nothing in this class knows what an enemy is. It reads a
    /// <see cref="DamageResult"/> and dresses it.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class HitFeedbackDirector : PrototypeConfigBehaviour
    {
        // ------------------------------------------------------------------ PROTOTYPE-ONLY palette

        [Header("PROTOTYPE-ONLY palette - readable against moss / stone / dark wood")]
        [PrototypeOnly(PendingDecisions.None, "Blood colour. Request asks for mid-level stylised gore.", true)]
        [SerializeField] private Color _bloodColor = new Color(0.62f, 0.06f, 0.08f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Armour spark colour: oxidised metal, not neon.", true)]
        [SerializeField] private Color _armorColor = new Color(0.78f, 0.80f, 0.86f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Armour-break shard colour: darker plate.", true)]
        [SerializeField] private Color _armorBreakColor = new Color(0.55f, 0.58f, 0.64f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Generic surface impact dust.", true)]
        [SerializeField] private Color _impactColor = new Color(0.86f, 0.84f, 0.74f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Explosion core colour. Kept warm, never neon.", true)]
        [SerializeField] private Color _explosionColor = new Color(1f, 0.62f, 0.24f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Chain / ricochet link colour. Must read at a glance in a crowd.", true)]
        [SerializeField] private Color _linkColor = new Color(0.72f, 0.88f, 1f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Tracer colour.", true)]
        [SerializeField] private Color _tracerColor = new Color(1f, 0.92f, 0.62f, 1f);

        [PrototypeOnly(PendingDecisions.None, "Muzzle flash colour.", true)]
        [SerializeField] private Color _muzzleColor = new Color(1f, 0.86f, 0.5f, 1f);

        // ------------------------------------------------------------------ PROTOTYPE-ONLY budget extras

        [Header("PROTOTYPE-ONLY ceilings the central config does not carry")]
        [PrototypeOnly(PendingDecisions.None, "Max tracers alive at once.", true)]
        [SerializeField] private int _maxTracers = 48;

        [PrototypeOnly(PendingDecisions.None, "Max muzzle flashes alive at once.", true)]
        [SerializeField] private int _maxMuzzleFlashes = 6;

        [PrototypeOnly(PendingDecisions.None, "Max chain / ricochet links alive at once.", true)]
        [SerializeField] private int _maxLinks = 20;

        [PrototypeOnly(PendingDecisions.None, "Max explosion bursts alive at once.", true)]
        [SerializeField] private int _maxExplosions = 8;

        [PrototypeOnly(PendingDecisions.SpecialWeapon,
            "Special Weapon feedback budget (its own family; WP-073 B6 draws the Raiju / Namekuji VFX in their own systems).", true)]
        [SerializeField] private int _maxSpecialEffects = 24;

        [PrototypeOnly(PendingDecisions.None,
            "Metres within which a second effect of the same family is merged away.", true)]
        [SerializeField] private float _effectMergeDistance = 0.55f;

        [PrototypeOnly(PendingDecisions.None, "Seconds the merge distance applies for.", true)]
        [SerializeField] private float _effectMergeWindow = 0.08f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds within which further damage on one target folds into the same number.", true)]
        [SerializeField] private float _damageNumberMergeWindow = 0.35f;

        // ------------------------------------------------------------------ PROTOTYPE-ONLY camera response

        [Header("PROTOTYPE-ONLY camera response - clamped again by CameraShakeBridge")]
        [PrototypeOnly(PendingDecisions.None, "Shake for an armour break, in metres.", true)]
        [SerializeField] private float _shakeArmorBreak = 0.075f;

        [PrototypeOnly(PendingDecisions.None, "Shake for an Elite / Ultra death, in metres.", true)]
        [SerializeField] private float _shakeHeavyDeath = 0.095f;

        [PrototypeOnly(PendingDecisions.None, "Shake for an explosion, in metres.", true)]
        [SerializeField] private float _shakeExplosion = 0.12f;

        /// <summary>WP-019: explosion shake length, from GameFeelSettings (≤ 0.2 s).</summary>
        private float _shakeExplosionSeconds = 0.2f;

        [Header("Presentation toggles")]
        [SerializeField] private bool _showDamageNumbers = true;
        [SerializeField] private bool _showHitMarkers = true;

        // ------------------------------------------------------------------ state

        private static HitFeedbackDirector s_instance;

        private readonly FeedbackBudget _budget = new FeedbackBudget();

        /// <summary>WP-019: the AP-010 layer. Dresses AFTER the budget check; absent catalog → WP-002 bursts.</summary>
        private readonly VfxDressing _vfx = new VfxDressing();

        private PrototypePool<BurstEffect> _impactPool;
        private PrototypePool<BurstEffect> _bloodPool;
        private PrototypePool<BurstEffect> _armorHitPool;
        private PrototypePool<BurstEffect> _armorBreakPool;
        private PrototypePool<BurstEffect> _deathPool;
        private PrototypePool<BurstEffect> _explosionPool;
        private PrototypePool<TracerEffect> _tracerPool;
        private PrototypePool<MuzzleFlashEffect> _muzzlePool;
        private PrototypePool<LinkEffect> _linkPool;

        private HitMarkerLayer _hitMarkers;

        private float _hitStopArmorBreak = 0.045f;
        private float _hitStopHeavyImpact = 0.035f;
        private float _hitStopHeavyDeath = 0.07f;
        private float _hitStopMinInterval = 0.25f;

        /// <summary>
        /// Service locator for a stateless presentation service, not a gameplay
        /// singleton: it holds no run state and any caller may ignore it and hold its
        /// own reference instead.
        /// </summary>
        public static HitFeedbackDirector Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindObjectOfType<HitFeedbackDirector>();
                }

                if (s_instance == null)
                {
                    var go = new GameObject("[Hit Feedback]");
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<HitFeedbackDirector>();
                }

                return s_instance;
            }
        }

        /// <summary>
        /// Raised for every landed enemy hit, after the intensity is resolved.
        /// This is the hook for the animator (hit reaction state) and for telemetry.
        /// It fires even when the visual was dropped by the crowd budget, so an
        /// animation never disappears because the screen was busy.
        /// </summary>
        public static event Action<EnemyHitFeedbackInfo, HitFeedbackTier> EnemyHitDressed;

        /// <summary>Budget diagnostics for the F1 panel.</summary>
        public FeedbackBudget Budget => _budget;

        /// <summary>WP-019 · AP-010 dressing layer (test / probe readout).</summary>
        public VfxDressing Vfx => _vfx;

        /// <summary>Whether floating damage numbers are drawn. Live-toggleable.</summary>
        public bool ShowDamageNumbers
        {
            get => _showDamageNumbers;
            set => _showDamageNumbers = value;
        }

        /// <summary>Whether hit confirmation is drawn. Live-toggleable.</summary>
        public bool ShowHitMarkers
        {
            get => _showHitMarkers;
            set
            {
                _showHitMarkers = value;
                if (_hitMarkers != null)
                {
                    _hitMarkers.Enabled = value;
                }
            }
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            FeelRuntime.Changed += OnFeelChanged;
            OnFeelChanged(FeelRuntime.Current);

            if (_showHitMarkers)
            {
                _hitMarkers = HitMarkerLayer.EnsureExists();
                _hitMarkers.Enabled = true;
            }

            // WP-019: the player-side layer (vignette + shake) and the armour plate hook (WP-017).
            DamageVignetteLayer.EnsureExists();
            DD.Prototype.Characters.ArmorVisualBinder.AnyBroken += OnArmorPlateBroken;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        protected override void OnDisable()
        {
            FeelRuntime.Changed -= OnFeelChanged;
            DD.Prototype.Characters.ArmorVisualBinder.AnyBroken -= OnArmorPlateBroken;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            FeelEventRouter.Clear();
            base.OnDisable();
        }

        /// <summary>WP-069 readout: pooled effects the last scene change put back (0 when every pool was whole).</summary>
        public int LastSceneRefill { get; private set; }

        /// <summary>
        /// WP-069 (S-02, CI cb78bfb4): every feedback pool back to capacity on a scene change. An effect parented to the
        /// weapon muzzle dies with the rig, so its pool came back short and grew again inside a hit (the RegionalArmor hit
        /// window after the CI sequence: 42 / 48 flipbooks, +4 Instantiate). FeedbackPoolWarmer.Refill wakes only the new ones.
        /// </summary>
        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            LastSceneRefill = _vfx.Refill() + FeedbackPoolWarmer.Refill(_impactPool) + FeedbackPoolWarmer.Refill(_bloodPool) +
                              FeedbackPoolWarmer.Refill(_armorHitPool) + FeedbackPoolWarmer.Refill(_armorBreakPool) + FeedbackPoolWarmer.Refill(_deathPool) +
                              FeedbackPoolWarmer.Refill(_explosionPool) + FeedbackPoolWarmer.Refill(_tracerPool) + FeedbackPoolWarmer.Refill(_muzzlePool) +
                              FeedbackPoolWarmer.Refill(_linkPool) + FeedbackPoolWarmer.Refill(_shockRingPool) + FeedbackPoolWarmer.Refill(_sealGlyphPool);
            if (LastSceneRefill > 0) Debug.Log("[Feedback] scene change: " + LastSceneRefill + " pooled effects refilled (destroyed behind their pools' backs)");
        }

        /// <summary>WP-017 plate hook: the fragments burst at the plate's anchor (the hit path draws the sheet + splinter).</summary>
        private void OnArmorPlateBroken(DD.Prototype.Characters.ArmorVisualBinder.BrokenEvent e)
        {
            if (e.Anchor == null)
            {
                return;
            }

            _vfx.PlayArmorFragments(e.Anchor.position, Vector3.up);
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void Update()
        {
            _vfx.Tick(Time.time);
        }

        private void LateUpdate()
        {
            FeelEventRouter.AdvanceLateFrame(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Every pool ceiling, hit-stop length and merge window is re-derived here, so a
        /// live edit in the F1 panel changes crowd behaviour on the next shot.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            FeelSnapshot feel = FeelRuntime.Current;
            OnFeelChanged(feel);
            _vfx.Configure(FeedbackEffectFactory.TemplateRoot);
            CameraFeelDriver.Ensure();   // WP-073 B4: the camera juice (per-shot roll / FOV / kick, kill beat, damage nudge)

            int impacts = Capacity(feel, FeedbackCategory.Impact, 40);
            int armorHits = Capacity(feel, FeedbackCategory.ArmorHit, 40);
            int blood = Capacity(feel, FeedbackCategory.Blood, 24);
            int armorBreaks = Capacity(feel, FeedbackCategory.ArmorBreak, 12);
            int deaths = Capacity(feel, FeedbackCategory.Death, 12);
            int numbers = Capacity(feel, FeedbackCategory.DamageNumber, 30);
            _maxExplosions = Capacity(feel, FeedbackCategory.Explosion, 8);
            _maxTracers = Capacity(feel, FeedbackCategory.Tracer, 48);
            _maxMuzzleFlashes = Capacity(feel, FeedbackCategory.MuzzleFlash, 6);
            _maxLinks = Capacity(feel, FeedbackCategory.Link, 20);
            _maxSpecialEffects = Capacity(feel, FeedbackCategory.SpecialWeapon, 24);
            _budget.ConfigurePolicy(feel.Budgets.perFrameFraction, feel.Budgets.minStartsPerFrame,
                feel.Budgets.importantStartMultiplier);

            // Armour *hits* share the impact ceiling: they are the same kind of visual
            // noise and the request budgets them together.
            _impactPool = Pool(FeedbackEffectFactory.ImpactTemplate, impacts);
            _armorHitPool = Pool(FeedbackEffectFactory.ArmorHitTemplate, armorHits);
            _bloodPool = Pool(FeedbackEffectFactory.BloodTemplate, blood);
            _armorBreakPool = Pool(FeedbackEffectFactory.ArmorBreakTemplate, armorBreaks);
            _deathPool = Pool(FeedbackEffectFactory.DeathTemplate, deaths);
            _explosionPool = Pool(FeedbackEffectFactory.ExplosionTemplate, Mathf.Max(2, _maxExplosions));

            _tracerPool = Pool(FeedbackEffectFactory.TracerTemplate, Mathf.Max(4, _maxTracers));
            _muzzlePool = Pool(FeedbackEffectFactory.MuzzleTemplate, Mathf.Max(2, _maxMuzzleFlashes));
            _linkPool = Pool(FeedbackEffectFactory.LinkTemplate, Mathf.Max(2, _maxLinks));

            int specials = Mathf.Max(4, _maxSpecialEffects);

            // WP-068 E18: the numbers are an overlay layer (UiWorldNumbers, its own pool of 48), not a world pool.
            if (Application.isPlaying)
            {
                _ = UiWorldNumbers.Instance;
            }

            _budget.Configure(FeedbackCategory.Impact, impacts, _effectMergeDistance, _effectMergeWindow);
            _budget.Configure(FeedbackCategory.ArmorHit, armorHits, _effectMergeDistance, _effectMergeWindow);
            _budget.Configure(FeedbackCategory.Blood, blood, _effectMergeDistance, _effectMergeWindow);
            _budget.Configure(FeedbackCategory.ArmorBreak, armorBreaks, 0f, 0f);
            _budget.Configure(FeedbackCategory.Death, deaths, 0f, 0f);
            _budget.Configure(FeedbackCategory.Explosion, _maxExplosions, 0f, 0f);
            _budget.Configure(FeedbackCategory.DamageNumber, numbers, _effectMergeDistance, _effectMergeWindow);
            _budget.Configure(FeedbackCategory.Tracer, _maxTracers, 0f, 0f);
            _budget.Configure(FeedbackCategory.MuzzleFlash, _maxMuzzleFlashes, 0f, 0f);
            _budget.Configure(FeedbackCategory.Link, _maxLinks, 0f, 0f);

            // No proximity merge: a Special Weapon's whole read is that MANY things happen
            // at once in one place. Merging is exactly the wrong instinct here, and it is
            // affordable precisely because this family cannot borrow from any other.
            _budget.Configure(FeedbackCategory.SpecialWeapon, specials, 0f, 0f);
        }

        private void OnFeelChanged(FeelSnapshot snapshot)
        {
            ImpactFeel armorBreak = snapshot.Impact(ImpactFeelId.ArmorBreak);
            ImpactFeel heavyDeath = snapshot.Impact(ImpactFeelId.HeavyDeath);
            ImpactFeel explosion = snapshot.Impact(ImpactFeelId.Explosion);
            _hitStopArmorBreak = armorBreak.hitStopSeconds;
            _hitStopHeavyImpact = 0f;
            _hitStopHeavyDeath = heavyDeath.hitStopSeconds;
            _hitStopMinInterval = snapshot.HitStop.minIntervalSeconds;
            _shakeArmorBreak = armorBreak.shakeAmplitudeMeters;
            _shakeHeavyDeath = heavyDeath.shakeAmplitudeMeters;
            _shakeExplosion = explosion.shakeAmplitudeMeters;
            _shakeExplosionSeconds = explosion.shakeSeconds;
            _effectMergeDistance = snapshot.Budgets.mergeDistanceMeters;
            _effectMergeWindow = snapshot.Budgets.mergeWindowSeconds;
            _damageNumberMergeWindow = snapshot.DamageNumbers.mergeWindowSeconds;
            _showDamageNumbers = snapshot.DamageNumbers.enabled;
            ConfigureBudget(snapshot);
        }

        private void ConfigureBudget(FeelSnapshot snapshot)
        {
            BudgetFeel b = snapshot.Budgets;
            _budget.ConfigurePolicy(b.perFrameFraction, b.minStartsPerFrame, b.importantStartMultiplier);
            _budget.Configure(FeedbackCategory.Impact, Capacity(snapshot, FeedbackCategory.Impact, 40), b.mergeDistanceMeters, b.mergeWindowSeconds);
            _budget.Configure(FeedbackCategory.ArmorHit, Capacity(snapshot, FeedbackCategory.ArmorHit, 40), b.mergeDistanceMeters, b.mergeWindowSeconds);
            _budget.Configure(FeedbackCategory.Blood, Capacity(snapshot, FeedbackCategory.Blood, 24), b.mergeDistanceMeters, b.mergeWindowSeconds);
            _budget.Configure(FeedbackCategory.ArmorBreak, Capacity(snapshot, FeedbackCategory.ArmorBreak, 12), 0f, 0f);
            _budget.Configure(FeedbackCategory.Death, Capacity(snapshot, FeedbackCategory.Death, 12), 0f, 0f);
            _budget.Configure(FeedbackCategory.DamageNumber, Capacity(snapshot, FeedbackCategory.DamageNumber, 30), b.mergeDistanceMeters, b.mergeWindowSeconds);
            _budget.Configure(FeedbackCategory.Tracer, Capacity(snapshot, FeedbackCategory.Tracer, 48), 0f, 0f);
            _budget.Configure(FeedbackCategory.MuzzleFlash, Capacity(snapshot, FeedbackCategory.MuzzleFlash, 6), 0f, 0f);
            _budget.Configure(FeedbackCategory.Link, Capacity(snapshot, FeedbackCategory.Link, 20), 0f, 0f);
            _budget.Configure(FeedbackCategory.Explosion, Capacity(snapshot, FeedbackCategory.Explosion, 8), 0f, 0f);
            _budget.Configure(FeedbackCategory.SpecialWeapon, Capacity(snapshot, FeedbackCategory.SpecialWeapon, 24), 0f, 0f);
        }

        private static int Capacity(FeelSnapshot snapshot, FeedbackCategory category, int fallback)
        {
            return snapshot.Budgets.CapacityFor(category.ToString(), fallback);
        }

        private static PrototypePool<T> Pool<T>(T template, int capacity) where T : PooledBehaviour
        {
            if (template == null)
            {
                return null;
            }

            // RecycleOldest everywhere: when the ceiling is reached the newest event
            // must still be visible, and the thing that disappears is the oldest one -
            // which is the one the player has already read.
            // WP-047 (ADR-037-1): prewarmed to capacity — a pool that grows on demand allocates ≈ 12 objects per Instantiate
            // in the hit frame (S-02 WP-053: the damage-number pool read as "1,75 allocations per number").
            return FeedbackPoolWarmer.Warm(PoolService.GetOrCreate(
                template,
                capacity,
                capacity,
                PoolExhaustedPolicy.RecycleOldest));
        }

        // ------------------------------------------------------------------ step 2: muzzle flash

        /// <summary>Step 2. Parented to the muzzle so it rides the recoil.</summary>
        public void ReportMuzzleFlash(Transform muzzle, float scale, float lightIntensity)
        {
            if (_muzzlePool == null || muzzle == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.MuzzleFlash, muzzle.position))
            {
                return;
            }

            // WP-019: the AP-010 muzzle sheet replaces the procedural card; the light stays with the legacy
            // effect only when the sheet is absent.
            if (_vfx.PlayMuzzle(muzzle, scale))
            {
                return;
            }

            MuzzleFlashEffect flash = _muzzlePool.Get(muzzle.position, muzzle.rotation, muzzle);
            if (flash != null)
            {
                flash.Play(_muzzleColor, scale, lightIntensity, 0.055f);
            }
        }

        // ------------------------------------------------------------------ step 4: tracer

        /// <summary>Step 4. One per pellet is fine; the budget will thin a shotgun blast.</summary>
        public void ReportTracer(Vector3 from, Vector3 to, float width, float lifetime)
        {
            if (_tracerPool == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.Tracer, from))
            {
                return;
            }

            TracerEffect tracer = _tracerPool.Get(from, Quaternion.identity);
            if (tracer != null)
            {
                tracer.Play(from, to, _tracerColor, width, lifetime, 4f);
            }
        }

        // ------------------------------------------------------------------ step 5: surface impact

        /// <summary>Step 5. Surface-aware world impact, for shots that miss or overpenetrate.</summary>
        public void ReportSurfaceImpact(Vector3 point, Vector3 normal, SurfaceKind surface, float scale)
        {
            // Audio owns its own budget. A missing/exhausted VFX pool must not suppress
            // the surface event before it reaches the audio voice policy.
            AudioEvents.Raise(AudioEvents.ImpactEventFor(surface), point, scale);

            if (_impactPool == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.Impact, point))
            {
                return;
            }

            if (!_vfx.PlaySurfaceImpact(surface, point, SafeNormal(normal), scale))
            {
                BurstEffect burst = _impactPool.Get(point, Quaternion.LookRotation(SafeNormal(normal)));
                if (burst != null)
                {
                    burst.Play(ColorForSurface(surface), scale, ParticlesForSurface(surface), 0.4f);
                }
            }
        }

        // ------------------------------------------------------------------ steps 6-12

        /// <summary>
        /// The core of the chain. Resolves the REQ-125 intensity, then plays exactly the
        /// layers that intensity deserves and no more.
        /// </summary>
        public void ReportEnemyHit(in EnemyHitFeedbackInfo info)
        {
            HitFeedbackTier tier = ResolveTier(info);
            int groupId = info.PresentationGroupId;
            if (groupId == 0) groupId = FeelEventRouter.NextGroupId();

            // Step 6/7 handshake: the animator and the enemy's own reaction hang off this
            // event. It is raised before any budget decision so a reaction is never lost
            // to visual noise.
            EnemyHitDressed?.Invoke(info, tier);

            Vector3 normal = SafeNormal(info.Normal.sqrMagnitude > 0.0001f ? info.Normal : -info.Direction);
            float force = Mathf.Max(0.35f, info.ImpactForce);

            HitStopService.BeginSpawnGroup(groupId);
            try
            {
                switch (tier)
                {
                    case HitFeedbackTier.ArmorHit: PlayArmorHit(info, normal, force); break;
                    case HitFeedbackTier.ArmorBreak: PlayArmorBreak(info, normal, force); break;
                    case HitFeedbackTier.CriticalHit: PlayFlesh(info, normal, force * 1.4f, true); break;
                    case HitFeedbackTier.Death:
                    case HitFeedbackTier.HeavyDeath: PlayDeath(info, normal, force, tier); break;
                    default: PlayFlesh(info, normal, force, false); break;
                }
                if (info.Result.ArmorBroke && info.Result.Killed) PlayArmorBreak(info, normal, force);
            }
            finally { HitStopService.EndSpawnGroup(); }

            FeelEventRouter.Enqueue(ImpactId(tier), groupId, info.Point, info.Direction);
            ShowDamageNumber(info, tier);

            if (_showHitMarkers && _hitMarkers != null)
            {
                _hitMarkers.Show(info.Point, tier);
            }

            // Step 12. One target cue per hit; AudioEvents itself merges the shotgun case.
            AudioEvents.Raise(
                AudioEvents.TargetEventFor(tier),
                info.Point,
                Mathf.Clamp01(0.5f + 0.5f * ((int)tier / 5f)),
                (int)tier * 10);
        }

        /// <summary>Explosion branch. Radius is visual only; damage was already applied.</summary>
        public void ReportExplosion(Vector3 origin, float radius, int presentationGroupId = 0)
        {
            if (_explosionPool != null && _budget.TryConsume(FeedbackCategory.Explosion, origin, true))
            {
                if (!_vfx.PlayExplosion(origin, radius))
                {
                    BurstEffect burst = _explosionPool.Get(origin, Quaternion.identity);
                    if (burst != null)
                    {
                        burst.Play(_explosionColor, Mathf.Max(0.5f, radius * 0.6f), 26, 0.55f);
                    }
                }
            }

            // Pushed away from the blast, using the camera's own stable position so the
            // direction is right whether the blast is in front of the player or behind.
            Vector3 away = Vector3.zero;
            if (FeedbackCameraRef.TryGetPosition(out Vector3 viewer))
            {
                away = viewer - origin;
                away.y = 0f;
            }

            FeelEventRouter.Enqueue(ImpactFeelId.Explosion,
                presentationGroupId != 0 ? presentationGroupId : FeelEventRouter.NextGroupId(), origin, away);
            // WP-019: no hit-stop on an explosion (only armour break and death, ADR-019-2).
            AudioEvents.RaiseCritical(CombatAudioEvent.Explosion, origin, 1f, 70);
        }

        /// <summary>Chain / ricochet connection. The "readable thin link" of the request.</summary>
        public void ReportLink(Vector3 from, Vector3 to, bool isRicochet)
        {
            // Keep the event surface independent from the visual link budget.
            AudioEvents.Raise(
                isRicochet ? CombatAudioEvent.Ricochet : CombatAudioEvent.ChainArc,
                to, 0.8f, 25);

            if (_linkPool == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.Link, from))
            {
                return;
            }

            LinkEffect link = _linkPool.Get(from, Quaternion.identity);
            if (link != null)
            {
                link.Play(from, to, _linkColor, isRicochet ? 0.03f : 0.022f, 0.18f, 0.16f);
            }

            if (isRicochet)
            {
                _vfx.PlaySpark(from, to - from);   // AP-010B wave 2: sparks at the bounce, streak along the new leg
            }
        }

        // ------------------------------------------------------------------ layers

        private void PlayFlesh(in EnemyHitFeedbackInfo info, Vector3 normal, float force, bool critical)
        {
            if (_bloodPool == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.Blood, info.Point, critical))
            {
                return;
            }

            if (_vfx.PlayFleshHit(info.Point, normal, info.Direction, critical, info.Result.Killed))
            {
                return;
            }

            BurstEffect burst = _bloodPool.Get(info.Point, Quaternion.LookRotation(normal));
            if (burst != null)
            {
                burst.Play(_bloodColor, force * (critical ? 1.5f : 1f), critical ? 14 : 8, critical ? 0.55f : 0.42f);
            }
        }

        private void PlayArmorHit(in EnemyHitFeedbackInfo info, Vector3 normal, float force)
        {
            if (_armorHitPool == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.ArmorHit, info.Point))
            {
                return;
            }

            if (_vfx.PlayBodyImpact(true, info.Point, normal, 1f))
            {
                return;
            }

            BurstEffect burst = _armorHitPool.Get(info.Point, Quaternion.LookRotation(normal));
            if (burst != null)
            {
                burst.Play(_armorColor, force, 7, 0.3f);
            }
        }

        private void PlayArmorBreak(in EnemyHitFeedbackInfo info, Vector3 normal, float force)
        {
            if (_armorBreakPool == null)
            {
                return;
            }

            // Important: bypasses the proximity merge. An armour break is a state change
            // the player must see even in the middle of a pack (REQ-129).
            if (!_budget.TryConsume(FeedbackCategory.ArmorBreak, info.Point, true))
            {
                return;
            }

            Vector3 ground = info.TargetRoot != null ? info.TargetRoot.position : new Vector3(info.Point.x, info.Point.y - 0.9f, info.Point.z);
            if (_vfx.PlayArmorBreak(info.Point, normal, ground, 1f, info.Direction))
            {
                return;
            }

            BurstEffect burst = _armorBreakPool.Get(info.Point, Quaternion.LookRotation(normal));
            if (burst != null)
            {
                burst.Play(_armorBreakColor, force * 1.6f, 18, 0.7f);
            }
        }

        private void PlayDeath(in EnemyHitFeedbackInfo info, Vector3 normal, float force, HitFeedbackTier tier)
        {
            bool heavy = tier == HitFeedbackTier.HeavyDeath;

            // Flesh layer first, so a kill still shows where it was hit.
            PlayFlesh(info, normal, force, false);

            // WP-073 B2 (direction §1.4): a head kill throws a red mist puff (≤ 2 at once; the gib set follows when P-06 delivers it).
            if (info.Result.Region == DD.Prototype.Damage.BodyRegion.Head && !info.FromExplosion && _vfx.Blood.IsReady)
            {
                _vfx.Blood.PlayHeadMist(info.Point);
            }

            if (_deathPool == null)
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.Death, info.Point, true))
            {
                return;
            }

            Vector3 feet = info.TargetRoot != null ? info.TargetRoot.position : new Vector3(info.Point.x, info.Point.y - 0.9f, info.Point.z);
            if (_vfx.PlayZombieDeath(feet, info.Direction, heavy, info.Point, normal, info.Weight))
            {
                return;
            }

            BurstEffect burst = _deathPool.Get(info.Point, Quaternion.LookRotation(normal));
            if (burst != null)
            {
                burst.Play(_bloodColor, force * (heavy ? 2.2f : 1.4f), heavy ? 26 : 14, heavy ? 0.9f : 0.65f);
            }
        }

        // ------------------------------------------------------------------ steps 11 and hit stop

        private static ImpactFeelId ImpactId(HitFeedbackTier tier)
        {
            switch (tier)
            {
                case HitFeedbackTier.ArmorHit: return ImpactFeelId.ArmorHit;
                case HitFeedbackTier.ArmorBreak: return ImpactFeelId.ArmorBreak;
                case HitFeedbackTier.CriticalHit: return ImpactFeelId.CriticalHit;
                case HitFeedbackTier.Death: return ImpactFeelId.Death;
                case HitFeedbackTier.HeavyDeath: return ImpactFeelId.HeavyDeath;
                default: return ImpactFeelId.FleshHit;
            }
        }

        // ------------------------------------------------------------------ damage numbers

        private void ShowDamageNumber(in EnemyHitFeedbackInfo info, HitFeedbackTier tier)
        {
            if (!_showDamageNumbers)
            {
                return;
            }

            float amount = info.Result.HealthDamage + info.Result.ArmorDamage;
            if (amount <= 0.01f)
            {
                return;
            }

            UiWorldNumbers numbers = UiWorldNumbers.Instance;
            DamageNumberFeel feel = FeelRuntime.Current.DamageNumbers;
            UiWorldNumberStyle style = NumberStyle(tier, info.Result.WasArmored, info.Weight);
            Vector3 point = info.Point + Vector3.up * feel.anchorOffsetYMeters;

            // Merge into the live number on the same target rather than stacking a new one on top of it (the shotgun case).
            if (numbers.TryMerge(info.TargetId, amount, style, point, _damageNumberMergeWindow, feel.mergeAnchorBlend))
            {
                return;
            }

            if (!_budget.TryConsume(FeedbackCategory.DamageNumber, info.Point, tier >= HitFeedbackTier.Death))
            {
                return;
            }

            numbers.Spawn(info.TargetId, amount, style, point);
        }

        /// <summary>
        /// WP-068 E18 · the GD hierarchy (direction §5; tiers are shapes, not colours — no amber): flesh M 22 cream, armour
        /// held S 18 grey, armour broken M 22 cream, crit L 26 cream + outlined starburst, kill bold, Elite / Ultra kill XL 30
        /// bold. Grey and cream are tokens, so the colour-blind palette follows. E18b: armour broken carries the plates
        /// glyph, an Elite / Ultra kill its tier head (outer-only, from the hit's weight).
        /// </summary>
        public static UiWorldNumberStyle NumberStyle(HitFeedbackTier tier, bool armored, FeedbackWeight weight = FeedbackWeight.Elite)
        {
            switch (tier)
            {
                case HitFeedbackTier.ArmorHit:
                    return new UiWorldNumberStyle(UiWorldNumberSize.S, UiColor.TextSecondary);
                case HitFeedbackTier.CriticalHit:
                    return new UiWorldNumberStyle(UiWorldNumberSize.L, UiColor.TextPrimary, false, true);
                case HitFeedbackTier.Death:
                    return new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary, true);
                case HitFeedbackTier.HeavyDeath:
                    return new UiWorldNumberStyle(UiWorldNumberSize.XL, UiColor.TextPrimary, true, false,
                        weight >= FeedbackWeight.Ultra ? UiWorldNumberGlyph.Ultra : UiWorldNumberGlyph.Elite);
                case HitFeedbackTier.ArmorBreak:
                    return new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary, false, false, UiWorldNumberGlyph.ArmorBreak);
                default:
                    return armored
                        ? new UiWorldNumberStyle(UiWorldNumberSize.S, UiColor.TextSecondary)
                        : new UiWorldNumberStyle(UiWorldNumberSize.M, UiColor.TextPrimary);
            }
        }

        // ------------------------------------------------------------------ resolution

        /// <summary>
        /// REQ-125 in one place. Every applicable tier is considered and the loudest
        /// wins, which is what makes the hierarchy monotonic: a headshot that also
        /// breaks a helmet and kills reports as a death, never as a flesh hit.
        /// </summary>
        public static HitFeedbackTier ResolveTier(in EnemyHitFeedbackInfo info)
        {
            DamageResult r = info.Result;

            HitFeedbackTier tier = HitFeedbackTier.FleshHit;

            if (r.WasArmored)
            {
                tier = Louder(tier, HitFeedbackTier.ArmorHit);
            }

            if (r.ArmorBroke)
            {
                tier = Louder(tier, HitFeedbackTier.ArmorBreak);
            }

            // A head hit only counts as critical when it actually reached flesh. A helmet
            // taking a bullet is an armour hit, not a critical - that distinction is the
            // whole point of the regional armour model. A blast never is one (REQ-089):
            // its "region" is only the loudest share, and the damage model applies no
            // regional multiplier to it - so the feedback must not dress it as a critical
            // either (WP-006; same rule as DamageRegions.IsCritical).
            if (!info.FromExplosion && r.Region == BodyRegion.Head && !r.WasArmored && r.HealthDamage > 0f)
            {
                tier = Louder(tier, HitFeedbackTier.CriticalHit);
            }

            if (r.Killed)
            {
                tier = Louder(tier, info.Weight >= FeedbackWeight.Elite
                    ? HitFeedbackTier.HeavyDeath
                    : HitFeedbackTier.Death);
            }

            return tier;
        }

        private static HitFeedbackTier Louder(HitFeedbackTier a, HitFeedbackTier b)
        {
            return (int)b > (int)a ? b : a;
        }

        private Color ColorForSurface(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Wood: return new Color(0.45f, 0.31f, 0.19f, 1f);
                case SurfaceKind.Foliage: return new Color(0.35f, 0.52f, 0.26f, 1f);
                case SurfaceKind.Water: return new Color(0.62f, 0.78f, 0.86f, 1f);
                case SurfaceKind.Metal: return new Color(0.9f, 0.86f, 0.7f, 1f);
                case SurfaceKind.Dirt: return new Color(0.42f, 0.35f, 0.26f, 1f);
                case SurfaceKind.Paper: return new Color(0.92f, 0.9f, 0.84f, 1f);
                case SurfaceKind.Flesh: return _bloodColor;
                case SurfaceKind.Armor: return _armorColor;
                default: return _impactColor;
            }
        }

        private static int ParticlesForSurface(SurfaceKind surface)
        {
            switch (surface)
            {
                case SurfaceKind.Foliage: return 10;
                case SurfaceKind.Water: return 12;
                case SurfaceKind.Metal: return 9;
                default: return 6;
            }
        }

        private static Vector3 SafeNormal(Vector3 n)
        {
            return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.up;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            EnemyHitDressed = null;
        }
    }
}
