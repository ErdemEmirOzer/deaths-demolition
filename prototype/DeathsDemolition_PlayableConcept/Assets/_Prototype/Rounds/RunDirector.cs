using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Spawn;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The run: rounds, phases, kill and XP counting, the round-end freeze, the upgrade
    /// screen, the countdown and the run summary.
    ///
    /// THE LOOP, IN THE ORDER IT HAPPENS
    /// ---------------------------------
    ///   Fighting       EnemySpawnDirector feeds the round's pool at its own pace.
    ///   |              This director only counts kills and publishes the HUD snapshot.
    ///   | pool empty   -> RoundPoolExhausted. Still Fighting; the stragglers are alive.
    ///   | map clear    -> RoundCleared.
    ///   RoundComplete  Round XP is banked, the clock is FROZEN (RunFlowClock hold), the
    ///   |              "ROUND COMPLETE" beat plays for RoundCompleteBeat seconds.
    ///   Upgrading      Three cards. Still frozen. G-08 draws; this class owns the state.
    ///   | continue     -> the hold is released and the world moves again.
    ///   Countdown      3 s (RoundConfigSection.RoundStartCountdown), simulation RUNNING,
    ///   |              so the player can reposition before the next pool starts.
    ///   Fighting       next round.
    ///
    /// Player death cuts across all of it and is the only ending (REQ-048).
    ///
    /// WHY THE FREEZE IS A HOLD AND NOT Time.timeScale = 0
    /// --------------------------------------------------
    /// The pause menu and the results screen also want the world still, and they can
    /// overlap a round end. See <see cref="RunFlowClock"/>. This class takes exactly one
    /// hold, <see cref="RunFlowClock.RoundEndHold"/>, and is the only thing allowed to
    /// take it.
    ///
    /// ARITHMETIC OWNERSHIP
    /// --------------------
    /// Pool size, round XP, kill XP and gold rewards are read from PrototypeConfig and
    /// from the death event, never re-derived here:
    ///     PrototypeConfig.TotalEnemyPool(round)
    ///     PrototypeConfig.RoundCompletionXp(round)
    ///     EnemyDeathInfo.KillXp   (already PrototypeConfig.KillXp(tier))
    ///     EnemyDeathInfo.GoldReward -> consumed by GoldService, never by this class
    ///
    /// DEC-021
    /// -------
    /// The authored round-override table stays EMPTY (user decision t-0002). This class
    /// never writes to PrototypeConfig.RoundDifficultyOverrides and never branches on it;
    /// every round is the formula.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-31000)]
    public sealed partial class RunDirector : PrototypeConfigBehaviour
    {
        public static RunDirector Instance { get; private set; }

        [Header("Flow")]
        [Tooltip("Start round 1 by itself when the scene loads. Turn off for a scene driven by a menu.")]
        [SerializeField] private bool _autoStartRun = true;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds after scene load before the run starts itself. Long enough for the player " +
            "rig and the spawn director to register.", true)]
        [SerializeField] private float _autoStartDelay = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds the frozen 'ROUND COMPLETE' beat holds before the upgrade screen opens. " +
            "INVENTED: the request asks for the beat but states no duration.", true)]
        [SerializeField] private float _roundCompleteBeat = 1.1f;

        [Tooltip("Open the upgrade screen at all. Off runs straight from RoundComplete to the countdown.")]
        [SerializeField] private bool _showUpgradeScreen = true;

        // ------------------------------------------------------------------ runtime

        private readonly RunStatsTracker _stats = new RunStatsTracker();
        private readonly RoundUpgradeShop _shop = new RoundUpgradeShop();
        private readonly RunBuildState _build = new RunBuildState();

        private RunPhase _phase = RunPhase.None;
        private int _round = 1;
        private int _poolTotal;
        private int _killsThisRound;
        private bool _poolExhausted;

        private float _phaseTimer;
        private float _countdownRemaining;
        private int _lastCountdownTick;
        private float _autoStartTimer;
        private bool _runEverStarted;
        private bool _boundToSpawnDirector;

        private EnemySpawnDirector _spawn;
        private GoldWallet _boundWallet;
        private WeaponModifierState _boundModifiers;
        private int _runId;
        private string _completionId;
        private GoldAutoCollectReport _roundClearSweep;

        // ------------------------------------------------------------------ public API

        /// <summary>Current phase. The screen-driving value.</summary>
        public RunPhase Phase => _phase;

        /// <summary>1-based round number.</summary>
        public int Round => _round;

        /// <summary>The round-end shop. G-08 draws from this while <see cref="Phase"/> is Upgrading.</summary>
        public RoundUpgradeShop Shop => _shop;

        /// <summary>The run's counters. Read-only in practice; only this class writes.</summary>
        public RunStatsTracker Stats => _stats;

        /// <summary>
        /// The run's build: weapon, purchases, cached description (WP-004). Ranks stay in
        /// the weapon's <see cref="WeaponModifierState"/>; this records what was bought.
        /// </summary>
        public RunBuildState Build => _build;

        /// <summary>
        /// Identity of the current run: a play-session-wide serial that rises by one on
        /// every <see cref="StartRun"/>, across scene reloads. The wallet, the reward
        /// ledger, the build and the shop session all carry it, so nothing from a previous
        /// run can be mistaken for this one. 0 before the first run.
        /// </summary>
        public int RunId => _runId;

        /// <summary>
        /// WP-009H: the completion capability carried by this run's summary, issued by
        /// <see cref="CompletionAuthority"/> at <see cref="StartRun"/>; null before the first
        /// run or when none could be issued. Opaque here. See <see cref="RunSummary.CompletionId"/>.
        /// </summary>
        public string CompletionId => _completionId;

        /// <summary>What the last round-end screen opened with (balance after the sweep, build). Default before any.</summary>
        public RoundEndEconomySnapshot LastRoundEnd { get; private set; }

        /// <summary>Run wallet, or null before the gold service exists.</summary>
        public GoldWallet Wallet => GoldService.Instance != null ? GoldService.Instance.Wallet : null;

        public bool IsRunLive => _phase != RunPhase.None && _phase != RunPhase.Dead;

        /// <summary>Kills scored inside the current round.</summary>
        public int RoundKills => _killsThisRound;

        /// <summary>Total pool of the current round, from PrototypeConfig.TotalEnemyPool(round).</summary>
        public int RoundPoolTotal => _poolTotal;

        /// <summary>
        /// Kills this round over the round's pool, 0..1.
        ///
        /// THIS IS THE NUMBER THE SPECIAL WEAPON DIRECTOR SCHEDULES AGAINST. It is kill
        /// progress and not elapsed time on purpose: a round ends when the pool is emptied,
        /// so a clock would drift out of step with the fight it is supposed to describe -
        /// a slow player would see the drop at "70%" with most of the pool still to come.
        /// </summary>
        public float RoundProgress01
        {
            get
            {
                if (_poolTotal <= 0)
                {
                    return 0f;
                }

                return Mathf.Clamp01(_killsThisRound / (float)_poolTotal);
            }
        }

        /// <summary>Puts a RunDirector in the scene if there is not one. Boot / G-08 hook.</summary>
        public static RunDirector EnsureExists()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject("RunDirector");
            return go.AddComponent<RunDirector>();
        }

        // ------------------------------------------------------------------ run lifecycle

        /// <summary>
        /// Starts a fresh run. Wipes gold, the weapon build, the counters and the player's
        /// health, in that order, then begins <paramref name="startRound"/>.
        /// </summary>
        public void StartRun(int startRound = 1)
        {
            // The same-scene restart path is also a real new-run boundary. Commit N fields
            // before any run-local state, pool policy or weapon build reads the snapshot.
            FeelRuntime.ApplyPendingNextRun();
            _runEverStarted = true;

            RunFlowClock.ClearHolds();
            RunEvents.ClearState();

            // The special-weapon HUD snapshot is static and outlives the scene. A pickup
            // still lying on the ground when the previous run ended would otherwise show
            // up as "on the field" in this run (WP-001 A2).
            WeaponEvents.PublishSpecial(default, true);

            _stats.Reset();
            _runId = ++s_runSerial;

            // WP-009H: the completion identity is ISSUED by the profile's identity
            // authority (this profile's lineage, the next ordinal, a fresh nonce), not
            // minted here. Null when no identity can exist (exhausted ordinal / no
            // lineage): the run still plays, and its summary is refused by the profile.
            _completionId = CompletionAuthority.IssueForNewRun();

            // WP-008: an offer retained from the previous run's last round must never be
            // restored into this run (the shop keys it on the run id, this is the belt).
            _shop.DiscardOffer();

            // WP-007 (AP-10): the Special Weapon process is bounded by the run. A same-scene
            // restart must not inherit a scheduled drop, a pickup or an active runtime.
            SpecialWeaponRoundBridge.NotifyRunStarted(_runId);
            LastRoundEnd = default;
            _roundClearSweep = default;

            // REQ-018: gold lives inside one run only. The service wipes packets, claims and
            // the wallet, then opens them for THIS run id, so a run can never inherit the
            // previous one's balance through a half-torn-down service.
            if (GoldService.Instance != null)
            {
                GoldService.Instance.InitializeForRun(_runId);
            }

            WeaponController controller = WeaponRegistry.ActiveController;
            if (controller != null)
            {
                controller.Modifiers.ResetForNewRun();
            }

            BindBuild();
            _build.BeginRun(_runId, WeaponRegistry.SelectedWeapon);
            // WP-077 B2b: the meta PATH's passives for this run (read once; the run never writes the profile).
            _shop.Meta = MetaPathRules.RunModifiers(ProfileService.Current, ProfileProgression.NameOf(WeaponRegistry.SelectedWeapon));

            PlayerRig rig = PlayerRegistry.Primary;
            if (rig != null && rig.Health != null)
            {
                rig.Health.ResetForNewRun();
            }

            ReportUnbackedBranchesOnce();

            RunEvents.PublishRunStarted(Mathf.Max(1, startRound));
            BeginRound(Mathf.Max(1, startRound));
        }

        /// <summary>
        /// Starts a round. Also the "skip to round N" path, which is why it resets the
        /// per-round counters rather than assuming it follows a completed round.
        /// </summary>
        public void BeginRound(int round)
        {
            _round = Mathf.Max(1, round);
            _killsThisRound = 0;
            _poolExhausted = false;
            _countdownRemaining = 0f;
            _phaseTimer = 0f;

            _poolTotal = HasConfig ? Config.TotalEnemyPool(_round) : 0;

            _stats.BeginRound();
            _shop.Close();

            // The round-end hold must be gone before the pool starts moving, or the next
            // round opens on a frozen world.
            RunFlowClock.RemoveHold(RunFlowClock.RoundEndHold);

            EnemySpawnDirector spawn = ResolveSpawnDirector();
            if (spawn != null)
            {
                spawn.BeginRound(_round);
                _poolTotal = spawn.RoundPoolTotal > 0 ? spawn.RoundPoolTotal : _poolTotal;
            }

            // Every enemy of this round gets its own packet: size the packet pool for the
            // round before the first one can die (WP-004).
            if (GoldService.Instance != null)
            {
                GoldService.Instance.PrepareForRound(_round, _poolTotal);
            }

            SetPhase(RunPhase.Fighting);

            RunEvents.PublishRoundStarted(_round);
            SpecialWeaponRoundBridge.NotifyRoundStarted(_round);
            RunAudioEvents.Raise(RunAudioEvent.RoundStart);

            PublishState();
        }

        /// <summary>
        /// Ends the run. Builds the summary FIRST, publishes it, and only then wipes the
        /// wallet - a summary that read the balance after the reset would report 0 gold for
        /// every run.
        /// </summary>
        public void EndRun(RunEndReason reason, string killedBy = null)
        {
            if (_phase == RunPhase.Dead || _phase == RunPhase.None)
            {
                return;
            }

            EnemySpawnDirector spawn = ResolveSpawnDirector();
            if (spawn != null)
            {
                spawn.StopRound();
            }

            // WP-008: the run is over; its offer and rights die with it (REQ-018).
            _shop.DiscardOffer();

            // The world must not stay frozen behind a results screen this class does not
            // own. G-08 takes RunFlowClock.ResultsHold if it wants the world still.
            RunFlowClock.RemoveHold(RunFlowClock.RoundEndHold);

            RunSummary summary = BuildSummary(reason, killedBy);

            SetPhase(RunPhase.Dead);
            PublishState();

            // WP-007 (AP-10): cancel the Special Weapon process (scheduled / on the ground /
            // in hand) the moment the run ends, before the results hand-off, not at scene load.
            SpecialWeaponRoundBridge.NotifyRunEnded();

            RunAudioEvents.Raise(RunAudioEvent.RunEnded);
            RunEvents.PublishRunEnded(summary);

            // Now the run's gold may go. Nothing carries out of a run: packets on the floor
            // are destroyed uncredited, the reward ledger closes (a late death event earns
            // nothing), the wallet is wiped. The build record closes with it; the weapon's
            // tiers are wiped by the next StartRun.
            if (GoldService.Instance != null)
            {
                GoldService.Instance.ResetForRunEnd();
            }

            _build.EndRun();
        }

        /// <summary>The upgrade screen's CONTINUE / SKIP button. Starts the countdown.</summary>
        public void CloseUpgradeScreen()
        {
            if (_phase != RunPhase.Upgrading && _phase != RunPhase.RoundComplete)
            {
                return;
            }

            if (_shop.IsOpen && _shop.PurchasesThisRound == 0)
            {
                RunAudioEvents.Raise(RunAudioEvent.UpgradeSkipped);
            }

            _shop.Close();

            // The world moves again here, not at the end of the countdown: the request's
            // 3 s is a chance to reposition, which is only worth having if you can walk.
            RunFlowClock.RemoveHold(RunFlowClock.RoundEndHold);

            float countdown = HasConfig ? Config.Rounds.RoundStartCountdown : 3f;
            _countdownRemaining = Mathf.Max(0f, countdown);
            _lastCountdownTick = int.MaxValue;

            SetPhase(RunPhase.Countdown);

            if (_countdownRemaining <= 0f)
            {
                BeginRound(_round + 1);
            }
        }

        /// <summary>Opens the upgrade screen for the round just completed. Idempotent.</summary>
        public void OpenUpgradeScreen()
        {
            if (_phase != RunPhase.RoundComplete && _phase != RunPhase.Fighting)
            {
                return;
            }

            if (!RunFlowClock.HasHold(RunFlowClock.RoundEndHold))
            {
                RunFlowClock.AddHold(RunFlowClock.RoundEndHold);
            }

            // Belt and braces: the sweep already ran from OnRoundCleared. If the screen is
            // being forced open from the F1 panel mid-round, whatever is on the floor is
            // taken now so the screen never opens on a balance that is about to change.
            GoldService gold = GoldService.Instance;
            GoldAutoCollectReport sweep = gold != null ? gold.CollectOutstanding(_round) : default;
            if (sweep.Packets == 0 && _roundClearSweep.RunId == _runId && _roundClearSweep.Round == _round)
            {
                // The normal case: OnRoundCleared already swept this round; quote that report.
                sweep = _roundClearSweep;
            }

            GoldWallet wallet = Wallet;
            int balance = wallet != null ? wallet.Balance : 0;

            _shop.Open(_round, Config, wallet, null, _runId);

            LastRoundEnd = new RoundEndEconomySnapshot(
                _runId, _round,
                sweep.Round == _round ? sweep.BalanceBefore : balance,
                sweep.Round == _round ? sweep.Packets : 0,
                sweep.Round == _round ? sweep.Amount : 0,
                balance,
                _shop.SessionId,
                _build.DescribeBuild());

            SetPhase(RunPhase.Upgrading);
        }

        // ------------------------------------------------------------------ unity

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Run] A second RunDirector was found. Only the first one drives the run.", this);
                enabled = false;
                return;
            }

            // On scene entry this runs after FeelRuntimeDriver (-32000) and before ordinary
            // pool/setup behaviours, so staged N fields size the new run from its first Awake.
            FeelRuntime.ApplyPendingNextRun();
            Instance = this;

            // WP-005: the HUD snapshots are static and outlive the scene. Between this
            // scene loading and StartRun (the auto-start delay) a HUD would otherwise show
            // the previous run's round, gold, weapon and Special Weapon marker. StartRun
            // clears them again; this is the same clear, one scene earlier.
            RunEvents.ClearState();
            WeaponEvents.PublishClassic(default, false);
            WeaponEvents.PublishSpecial(default, false);

            // Gold is meaningless without a run and a run always wants a wallet, so the
            // director brings its own service rather than depending on scene authoring.
            if (GoldService.Instance == null && GetComponent<GoldService>() == null)
            {
                gameObject.AddComponent<GoldService>();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (Instance == null)
            {
                Instance = this;
            }

            DamageEvents.EnemyDied += OnEnemyDied;
            DamageEvents.ArmorBroken += OnArmorBroken;
            PlayerHealth.AnyPlayerDied += OnPlayerDied;

            _shop.Purchased += OnCardPurchased;
            _shop.PurchaseUndone += OnCardPurchaseUndone;
            _shop.Rerolled += OnOfferRerolled;

            BindSpawnDirector();
            BindSpecialWeaponBridge();
            RegisterDebugPanel();
        }

        protected override void OnDisable()
        {
            DamageEvents.EnemyDied -= OnEnemyDied;
            DamageEvents.ArmorBroken -= OnArmorBroken;
            PlayerHealth.AnyPlayerDied -= OnPlayerDied;

            _shop.Purchased -= OnCardPurchased;
            _shop.PurchaseUndone -= OnCardPurchaseUndone;
            _shop.Rerolled -= OnOfferRerolled;

            UnbindSpawnDirector();
            UnbindSpecialWeaponBridge();
            UnbindWallet();
            UnbindBuild();
            UnregisterDebugPanel();

            RunFlowClock.RemoveHold(RunFlowClock.RoundEndHold);

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDisable();
        }

        /// <summary>
        /// Live config contract. The pool total is recomputed so dragging the pool curve in
        /// the F1 panel mid-round moves the HUD's "enemies remaining" and the Special
        /// Weapon's schedule together, instead of leaving them describing different rounds.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            if (_phase == RunPhase.None)
            {
                _poolTotal = config.TotalEnemyPool(_round);
                return;
            }

            EnemySpawnDirector spawn = ResolveSpawnDirector();
            _poolTotal = spawn != null && spawn.RoundPoolTotal > 0
                ? spawn.RoundPoolTotal
                : config.TotalEnemyPool(_round);
        }

        private void Update()
        {
            BindSpawnDirector();
            BindWallet();
            BindBuild();

            if (!_runEverStarted)
            {
                TickAutoStart();
                return;
            }

            switch (_phase)
            {
                case RunPhase.Fighting:
                    // Scaled: a paused or frozen run must not accumulate run time.
                    _stats.Tick(Time.deltaTime);
                    break;

                case RunPhase.RoundComplete:
                    TickRoundCompleteBeat();
                    break;

                case RunPhase.Countdown:
                    _stats.Tick(Time.deltaTime);
                    TickCountdown();
                    break;
            }

            PublishState();
        }

        private void TickAutoStart()
        {
            if (!_autoStartRun)
            {
                return;
            }

            // Unscaled: the run must be able to start even if something else left the clock
            // frozen from a previous scene.
            _autoStartTimer += Time.unscaledDeltaTime;
            if (_autoStartTimer >= _autoStartDelay)
            {
                StartRun(1);
            }
        }

        private void TickRoundCompleteBeat()
        {
            // Unscaled, because this phase is the freeze. Scaled time is zero here.
            _phaseTimer += Time.unscaledDeltaTime;

            if (_phaseTimer < Mathf.Max(0f, _roundCompleteBeat))
            {
                return;
            }

            if (_showUpgradeScreen)
            {
                OpenUpgradeScreen();
            }
            else
            {
                CloseUpgradeScreen();
            }
        }

        private void TickCountdown()
        {
            _countdownRemaining -= Time.deltaTime;

            int second = Mathf.CeilToInt(Mathf.Max(0f, _countdownRemaining));
            if (second < _lastCountdownTick && second > 0)
            {
                _lastCountdownTick = second;
                RunEvents.PublishCountdownTick(second);
                RunAudioEvents.Raise(RunAudioEvent.CountdownTick, default, 1f, 3 - second);
            }

            if (_countdownRemaining <= 0f)
            {
                BeginRound(_round + 1);
            }
        }

        // ------------------------------------------------------------------ round end

        private void OnRoundPoolExhausted(int round)
        {
            if (_poolExhausted)
            {
                return;
            }

            _poolExhausted = true;

            RunEvents.PublishRoundPoolExhausted(round);
            RunAudioEvents.Raise(RunAudioEvent.RoundPoolExhausted);
        }

        private void OnRoundCleared(int round)
        {
            if (_phase != RunPhase.Fighting)
            {
                return;
            }

            // An unfinished round grants no round XP; this is the only path that banks it.
            int roundXp = HasConfig ? Config.RoundCompletionXp(round) : 0;
            _stats.RecordRoundCompleted(roundXp);

            _phaseTimer = 0f;
            SetPhase(RunPhase.RoundComplete);

            // The world stops here and stays stopped through the upgrade screen. One hold
            // covers both phases, so a pause taken during the beat cannot un-freeze the
            // shop when it is released.
            RunFlowClock.AddHold(RunFlowClock.RoundEndHold);

            // REQ-076 (WP-004): every packet still on the floor - the last kill's included,
            // it was dropped the frame before this event - is taken and credited NOW, in
            // this frame, before anything downstream can read the balance. The director
            // calls it directly rather than letting the service subscribe, so the order
            // is fixed and not a matter of subscription luck.
            _roundClearSweep = GoldService.Instance != null
                ? GoldService.Instance.CollectOutstanding(round)
                : default;

            RunEvents.PublishRoundCompleted(round);
            SpecialWeaponRoundBridge.NotifyRoundEnded(round);
            RunAudioEvents.Raise(RunAudioEvent.RoundComplete);

            PublishState();
        }

        // ------------------------------------------------------------------ counting

        private void OnEnemyDied(EnemyDeathInfo info)
        {
            if (!IsRunLive)
            {
                return;
            }

            // The event already carries PrototypeConfig.KillXp(tier). The config call is a
            // fallback for a producer that left it at zero, so the number is never derived
            // twice and never derived differently.
            int killXp = info.KillXp;
            if (killXp <= 0 && HasConfig)
            {
                killXp = Config.KillXp(info.Tier);
            }

            _stats.RecordKill(info.Tier, killXp);
            _killsThisRound++;

            // Gold is NOT handled here. GoldService listens to the same event and owns the
            // payout, which is what keeps "gold grants no XP" true by construction: the two
            // numbers never meet in one method.
        }

        private void OnArmorBroken(ArmorBreakInfo info)
        {
            if (IsRunLive)
            {
                _stats.RecordArmorBreak();
            }
        }

        private void OnCardPurchased(UpgradeCardOffer card)
        {
            _stats.RecordGoldSpent(card.Cost);
            _build.RecordPurchase(card.Branch, card.Cost, _round);
        }

        /// <summary>WP-077 K10: the last card purchase was undone and refunded.</summary>
        private void OnCardPurchaseUndone(UpgradeCardOffer card)
        {
            _stats.RecordPurchaseUndone(card.Cost);
            _build.RecordPurchaseUndone(card.Cost);
        }

        /// <summary>WP-008: the paid reroll is run spending too (Results "Gold Spent" = purchases + rerolls).</summary>
        private void OnOfferRerolled(int cost)
        {
            _stats.RecordRerollSpent(cost);
        }

        private void OnPlayerDied(PlayerDeathInfo info)
        {
            EndRun(RunEndReason.PlayerDeath, info.KilledBy);
        }

        // ------------------------------------------------------------------ publishing

        private void SetPhase(RunPhase phase)
        {
            if (_phase == phase)
            {
                return;
            }

            _phase = phase;
            RunEvents.PublishPhaseChanged(phase);
        }

        /// <summary>
        /// Refreshes the HUD snapshot. Polled by G-08 rather than pushed, so printing a
        /// number costs no event.
        /// </summary>
        private void PublishState()
        {
            EnemySpawnDirector spawn = ResolveSpawnDirector();

            var state = new RunHudState
            {
                Round = _round,
                PoolRemaining = spawn != null ? spawn.RemainingInPool : 0,
                AliveEnemies = spawn != null ? spawn.ActiveCount : 0,
                Gold = Wallet != null ? Wallet.Balance : 0,
                RunKills = _stats.TotalKills,
                StandardKills = _stats.KillsFor(EnemyTier.Standard),
                SpecialKills = _stats.KillsFor(EnemyTier.Special),
                EliteKills = _stats.KillsFor(EnemyTier.Elite),
                UltraKills = _stats.KillsFor(EnemyTier.Ultra),
                Phase = _phase,
                CountdownRemaining = _phase == RunPhase.Countdown ? Mathf.Max(0f, _countdownRemaining) : 0f,
                PoolTotal = _poolTotal,
                RoundProgress01 = RoundProgress01,
                RoundKills = _killsThisRound,
                PoolExhausted = _poolExhausted,
                SimulationFrozen = RunFlowClock.IsFrozen,
                RunTime = _stats.RunTime
            };

            RunEvents.PublishState(state);
        }

        private RunSummary BuildSummary(RunEndReason reason, string killedBy)
        {
            WeaponController controller = WeaponRegistry.ActiveController;
            GoldWallet wallet = Wallet;

            return new RunSummary
            {
                ReachedRound = _round,
                CompletedRounds = _stats.CompletedRounds,

                TotalKills = _stats.TotalKills,
                StandardKills = _stats.KillsFor(EnemyTier.Standard),
                SpecialKills = _stats.KillsFor(EnemyTier.Special),
                EliteKills = _stats.KillsFor(EnemyTier.Elite),
                UltraKills = _stats.KillsFor(EnemyTier.Ultra),

                CompletedRoundXp = _stats.CompletedRoundXp,
                KillXp = _stats.KillXp,
                TotalXpEarned = _stats.TotalXp,

                ArmorPiecesBroken = _stats.ArmorPiecesBroken,
                HighestTierKilled = _stats.HighestTierKilled,

                CompletionId = _completionId,
                Weapon = WeaponRegistry.SelectedWeapon,
                BuildDescription = _build.HasState ? _build.DescribeBuild()
                    : controller != null ? controller.Modifiers.DescribeBuild() : "Stock",

                // The wallet is the ledger, so the summary quotes it rather than the
                // tracker's mirror of it.
                GoldEarned = wallet != null ? wallet.TotalEarned : _stats.GoldEarned,
                GoldSpent = wallet != null ? wallet.TotalSpent : _stats.GoldSpent,
                GoldSpentOnUpgrades = wallet != null ? wallet.SpentOnUpgrades : _stats.GoldSpentOnUpgrades,
                GoldSpentOnRerolls = wallet != null ? wallet.SpentOnRerolls : _stats.GoldSpentOnRerolls,
                GoldUnspent = wallet != null ? wallet.Balance : 0,
                UpgradesPurchased = _stats.UpgradesPurchased,
                RerollsUsed = _stats.RerollsUsed,
                TreeKeystones = _shop.Tree.OwnedCount(RunTreeNodeKind.BranchKeystone) + _shop.Tree.OwnedCount(RunTreeNodeKind.WeaponSeal),
                TreeCapstones = _shop.Tree.OwnedCount(RunTreeNodeKind.Capstone),
                TreeHearthStacks = _shop.Tree.HearthStacks,
                TreeLinkII = _shop.Tree.IsBuilt && _shop.Tree.Graph.LinkIIIndex >= 0 && _shop.Tree.IsOwned(_shop.Tree.Graph.LinkIIIndex) ? 1 : 0,
                TreeWeaponCapstone = _shop.Tree.IsBuilt && _shop.Tree.IsOwned(_shop.Tree.Graph.WeaponCapstoneIndex) ? 1 : 0,
                TreeFullLanes = _shop.Tree.FullLaneCount(_shop.BuildForTree),

                RunDuration = _stats.RunTime,
                EndReason = reason,
                KilledBy = killedBy
            };
        }

        // ------------------------------------------------------------------ wiring

        private EnemySpawnDirector ResolveSpawnDirector()
        {
            if (_spawn == null)
            {
                _spawn = EnemySpawnDirector.Instance;
            }

            return _spawn;
        }

        /// <summary>
        /// Binds to the spawn director's two round events, and RE-binds if the live
        /// instance is swapped. Polled from Update rather than bound once in OnEnable
        /// because the spawn director may be created after this one; missing that would
        /// mean a round that never ends, silently.
        /// </summary>
        private void BindSpawnDirector()
        {
            EnemySpawnDirector current = EnemySpawnDirector.Instance;

            if (_boundToSpawnDirector && ReferenceEquals(current, _spawn))
            {
                return;
            }

            UnbindSpawnDirector();

            _spawn = current;
            if (_spawn == null)
            {
                return;
            }

            _spawn.RoundPoolExhausted += OnRoundPoolExhausted;
            _spawn.RoundCleared += OnRoundCleared;
            _boundToSpawnDirector = true;
        }

        private void UnbindSpawnDirector()
        {
            if (!_boundToSpawnDirector || _spawn == null)
            {
                _boundToSpawnDirector = false;
                return;
            }

            _spawn.RoundPoolExhausted -= OnRoundPoolExhausted;
            _spawn.RoundCleared -= OnRoundCleared;
            _boundToSpawnDirector = false;
        }

        /// <summary>
        /// Mirrors gold income into the run stats. Without it the tracker's GoldEarned
        /// would stay at zero while the wallet climbed, and the summary's fallback would
        /// report a run that earned nothing. The wallet instance outlives every
        /// ResetForNewRun, so this binds once.
        /// </summary>
        private void BindWallet()
        {
            GoldWallet wallet = Wallet;
            if (wallet == null || ReferenceEquals(wallet, _boundWallet))
            {
                return;
            }

            if (_boundWallet != null)
            {
                _boundWallet.Credited -= OnGoldCredited;
            }

            _boundWallet = wallet;
            _boundWallet.Credited += OnGoldCredited;
        }

        private void UnbindWallet()
        {
            if (_boundWallet == null)
            {
                return;
            }

            _boundWallet.Credited -= OnGoldCredited;
            _boundWallet = null;
        }

        private void OnGoldCredited(int amount, int balance, Vector3 position)
        {
            _stats.RecordGoldEarned(amount);
        }

        /// <summary>
        /// Points the build record at the armed player's modifier state, and RE-points it
        /// if the controller is swapped. Polled like the spawn director because the
        /// weapon system may arm the rig after this director woke up.
        /// </summary>
        private void BindBuild()
        {
            WeaponController controller = WeaponRegistry.ActiveController;
            WeaponModifierState modifiers = controller != null ? controller.Modifiers : null;

            if (ReferenceEquals(modifiers, _boundModifiers))
            {
                return;
            }

            _boundModifiers = modifiers;
            _build.Attach(modifiers);
        }

        private void UnbindBuild()
        {
            _boundModifiers = null;
            _build.Detach();
        }

        /// <summary>
        /// The three lines <see cref="SpecialWeaponRoundBridge"/> asks for.
        ///
        /// WITHOUT THIS THE SPECIAL WEAPON NEVER APPEARS BY ITSELF and nothing complains:
        /// the director checks IsBound, finds it false, and quietly returns every frame,
        /// leaving only the F1 button working. That is the exact failure this method exists
        /// to prevent, so it is called from OnEnable and never conditionally.
        /// </summary>
        private void BindSpecialWeaponBridge()
        {
            SpecialWeaponRoundBridge.CurrentRoundProvider = () => _round;
            SpecialWeaponRoundBridge.RoundProgressProvider = () => RoundProgress01;
        }

        private void UnbindSpecialWeaponBridge()
        {
            SpecialWeaponRoundBridge.CurrentRoundProvider = null;
            SpecialWeaponRoundBridge.RoundProgressProvider = null;
        }

        /// <summary>
        /// Announces branches kept out of the card pool. Silent while there are none, which
        /// is the state since the four player-side branches became live: a log line saying
        /// "held back: none" is noise, and acceptance criterion 27 wants a quiet console.
        /// </summary>
        private void ReportUnbackedBranchesOnce()
        {
            if (s_unbackedReported || !UpgradeCardCatalog.HasUnbackedBranches)
            {
                return;
            }

            s_unbackedReported = true;

            Debug.Log(
                "[Run] Upgrade branches held back from the card pool because no live system reads " +
                "them yet: " + UpgradeCardCatalog.DescribeUnbacked() +
                ". Enable 'Offer unbacked branches' on the F1 Rounds tab to see the held-back cards.");
        }

        private static bool s_unbackedReported;
        private static int s_runSerial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            s_unbackedReported = false;
            s_runSerial = 0;
        }
    }
}
