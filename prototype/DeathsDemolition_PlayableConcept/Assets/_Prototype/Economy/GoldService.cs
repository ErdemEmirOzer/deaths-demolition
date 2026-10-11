using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Damage;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>What one round-end sweep did. Published by <see cref="GoldService.CollectOutstanding"/>.</summary>
    public readonly struct GoldAutoCollectReport
    {
        public readonly int RunId;
        public readonly int Round;

        /// <summary>Packets taken by the sweep. 0 when the floor was already clear.</summary>
        public readonly int Packets;

        /// <summary>Gold those packets carried, all of it credited before this report existed.</summary>
        public readonly int Amount;

        public readonly int BalanceBefore;
        public readonly int BalanceAfter;

        public GoldAutoCollectReport(int runId, int round, int packets, int amount, int balanceBefore, int balanceAfter)
        {
            RunId = runId;
            Round = round;
            Packets = packets;
            Amount = amount;
            BalanceBefore = balanceBefore;
            BalanceAfter = balanceAfter;
        }
    }

    /// <summary>WP-098B · A packet finished its landing hop and lies on the ground (the landing glint moment).</summary>
    public readonly struct GoldLandedFeel
    {
        public readonly Vector3 Position;
        public readonly int Amount;

        public GoldLandedFeel(Vector3 position, int amount)
        {
            Position = position;
            Amount = amount;
        }
    }

    /// <summary>WP-098B · The magnet grabbed a packet. Not every packet gets one: a packet the player stands on is taken without a pull.</summary>
    public readonly struct GoldMagnetFeel
    {
        public readonly GoldOrb Orb;
        public readonly Vector3 Position;
        public readonly int Amount;

        public GoldMagnetFeel(GoldOrb orb, Vector3 position, int amount)
        {
            Orb = orb;
            Position = position;
            Amount = amount;
        }
    }

    /// <summary>
    /// WP-098B · Raised at the moment the packet calls <c>HudGoldTarget.Arrive</c>: after its flight to the
    /// HUD gold symbol, or at once (<see cref="Flew"/> false) when Reduced Motion skips the flight.
    /// Never raised when no HUD answered at collection (today's in-place flash, no Depart/Arrive).
    /// </summary>
    public readonly struct GoldArrivedFeel
    {
        public readonly int Amount;
        public readonly GoldCollectKind Kind;
        public readonly bool Flew;

        public GoldArrivedFeel(int amount, GoldCollectKind kind, bool flew)
        {
            Amount = amount;
            Kind = kind;
            Flew = flew;
        }
    }

    /// <summary>
    /// WP-098B · The gold pickup's feel surface: presentation moments only, never the wallet's truth
    /// (credits are <see cref="GoldWallet.Credited"/> / RunEvents.GoldChanged). Struct args, no
    /// allocation per raise. Per packet, in order: Landed → MagnetStart (optional) →
    /// <c>FeelEvents.GoldCollected</c> → FlightArrived (only when a HUD took the coin).
    /// The collection moment itself is S-02's <c>FeelEvents.GoldCollected</c> (FeelGold: Amount, Balance,
    /// Position, ChainIndex in a 0.6 s window), raised from the run wallet's Credited - one per credit,
    /// so one per swept coin too. This class carries only the moments FeelEvents has no counterpart for.
    /// Listeners subscribe on enable and unsubscribe on disable; a process-long listener re-subscribes
    /// on scene change (static bus rearm lesson).
    /// </summary>
    public static class GoldFeelEvents
    {
        public static event Action<GoldLandedFeel> Landed;
        public static event Action<GoldMagnetFeel> MagnetStart;
        public static event Action<GoldArrivedFeel> FlightArrived;

        internal static void RaiseLanded(GoldLandedFeel e)
        {
            Action<GoldLandedFeel> h = Landed;
            if (h == null) return;
            try { h.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        internal static void RaiseMagnetStart(GoldMagnetFeel e)
        {
            Action<GoldMagnetFeel> h = MagnetStart;
            if (h == null) return;
            try { h.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        internal static void RaiseFlightArrived(GoldArrivedFeel e)
        {
            Action<GoldArrivedFeel> h = FlightArrived;
            if (h == null) return;
            try { h.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Landed = null;
            MagnetStart = null;
            FlightArrived = null;
        }
    }

    /// <summary>
    /// Gold: who earns it, how it reaches the player, and nothing else.
    ///
    /// THE CHAIN (WP-004), in the order it runs, every link owned by one class:
    ///
    ///   EnemyDied ─► GoldRewardLedger.TryClaim   is THIS life, in THIS run, owed a packet?
    ///            ─► GoldRewardValue.Resolve      how much (base value only in M1)
    ///            ─► one GoldOrb from the pool     the physical packet, at the death point
    ///   GoldOrb  ─► GoldPacketLifecycle          waits (never expires), magnetizes, is
    ///                                            taken at most once
    ///            ─► GoldWallet.Credit             one transaction, one id, one source
    ///   RoundCleared ─► CollectOutstanding        every packet still on the floor is taken
    ///                                            by the sweep, credited in the same frame,
    ///                                            BEFORE the round-end screen opens
    ///   Run end  ─► ResetForRunEnd               packets recycled uncredited, wallet wiped
    ///
    /// Every enemy death produces exactly one packet. The old orb ceiling that fell back
    /// to a direct credit is gone: the pool is sized for the round before it starts, and
    /// the fallback that remains is a counted safety net that is expected to read zero.
    ///
    /// GOLD AND XP NEVER TOUCH. This class raises no XP, holds no XP and has no
    /// reference to anything that awards XP (request: earning or spending gold grants no
    /// profile XP). Kill XP is counted by the run director off the same death event.
    ///
    /// The collection mode switch that lived here (DirectCredit / ContactPickup /
    /// ProximityMagnet) is retired: the approved contract is a visible packet with a
    /// magnet. <c>EconomyConfigSection.GoldCollectionMode</c> and <c>OrbLifetime</c> are
    /// no longer read (kept in the config so the shipped asset does not change).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GoldService : PrototypeConfigBehaviour
    {
        public static GoldService Instance { get; private set; }

        [Header("Packet presentation (PROTOTYPE-ONLY, all invented)")]
        [SerializeField] private GoldOrbTuning _orbTuning = new GoldOrbTuning();

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Seconds the round-end sweep's packets take to fly to the player, on UNSCALED time " +
            "so it finishes under the round-end freeze. The wallet is credited before the flight " +
            "starts; this is presentation only. INVENTED.", true)]
        [SerializeField] private float _sweepFlightDuration = 0.35f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "WP-098B: seconds a picked-up packet takes to fly to the HUD gold symbol (UNSCALED; the wallet is " +
            "credited at pickup, the flight is presentation only). Within the 0.35 s open/close budget. INVENTED.", true)]
        [SerializeField] private float _hudFlightDuration = 0.3f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "WP-098B: the HUD flight's end point sits on the camera ray through the gold symbol at this fraction " +
            "of the packet's own camera depth, so the flight rises toward the lens over the garden's canopy. INVENTED.", true)]
        [SerializeField] private float _hudFlightDepth = 0.5f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "WP-098B: scale a packet shrinks to by the end of its HUD flight (it is then nearer the lens). INVENTED.", true)]
        [SerializeField] private float _hudFlightEndScale = 0.25f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Extra packets the pool is sized for beyond the round's enemy pool, so a debug spawn " +
            "or a late corpse never pushes a kill into the counted fallback. INVENTED.", true)]
        [SerializeField] private int _packetPoolMargin = 8;

        [Header("Diagnostics")]
        [Tooltip("Draw the magnet radius around the player in the editor.")]
        [SerializeField] private bool _drawGizmos = true;

        private readonly GoldWallet _wallet = new GoldWallet();
        private readonly GoldRewardLedger _ledger = new GoldRewardLedger();
        private readonly List<GoldOrb> _liveOrbs = new List<GoldOrb>(128);
        private readonly List<GoldOrb> _warmScratch = new List<GoldOrb>(128);

        private static GoldOrb s_template;

        private PrototypePool<GoldOrb> _pool;
        private Transform _player;

        private int _runId;
        private int _round;
        private int _litOrbs;
        private int _streak;
        private float _streakTimer;
        private int _packetsSpawned;
        private int _packetsCollected;
        private int _packetsAutoCollected;
        private int _packetsLost;
        private int _fallbackCredits;
        private int _creditRefusals;
        private int _lastSweepRunId;
        private int _lastSweepRound;

        // cached from config
        private float _magnetRadius = 3.5f;
        private float _magnetSpeed = 14f;
        private float _magnetAcceleration = 38f;
        private float _contactRadius = 0.9f;
        private int _maxActiveOrbs = 60;

        private DebugPanelEntry[] _debugEntries = Array.Empty<DebugPanelEntry>();

        // ------------------------------------------------------------------ public API

        /// <summary>The run wallet. The upgrade shop spends from this instance.</summary>
        public GoldWallet Wallet => _wallet;

        /// <summary>Reward eligibility ledger. Read-only outside this class in practice; tests read its counters.</summary>
        public GoldRewardLedger Ledger => _ledger;

        /// <summary>Run this service is paying out for. 0 between runs.</summary>
        public int RunId => _runId;

        public float MagnetRadius => _magnetRadius;

        public float MagnetSpeed => _magnetSpeed;

        public float MagnetAcceleration => _magnetAcceleration;

        /// <summary>Distance at which a packet is taken. Also the magnet's capture distance.</summary>
        public float ContactRadius => _contactRadius;

        /// <summary>Seconds a swept packet flies (unscaled). Presentation only.</summary>
        public float SweepFlightDuration => Mathf.Max(0.01f, _sweepFlightDuration);

        /// <summary>WP-098B · Seconds a picked-up packet flies to the HUD gold symbol (unscaled). 0 = no flight. Presentation only.</summary>
        public float HudFlightDuration => Mathf.Clamp(_hudFlightDuration, 0f, 0.35f);

        /// <summary>WP-098B · Fraction of the packet's camera depth the HUD flight ends at.</summary>
        public float HudFlightDepth => Mathf.Clamp(_hudFlightDepth, 0.1f, 1f);

        /// <summary>WP-098B · Scale at the end of the HUD flight.</summary>
        public float HudFlightEndScale => Mathf.Clamp(_hudFlightEndScale, 0.05f, 1f);

        /// <summary>Packets currently checked out of the pool (outstanding or animating out).</summary>
        public int LiveOrbCount => _liveOrbs.Count;

        /// <summary>WP-019b · Horizontal (XZ) distance from <paramref name="point"/> to the nearest live packet (float.MaxValue when none): the blood marks keep 0.6 m clear of gold on the ground (DEC-077); the packet hovers, so height is ignored.</summary>
        public float NearestLiveOrbDistance(Vector3 point)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _liveOrbs.Count; i++)
            {
                GoldOrb orb = _liveOrbs[i];
                if (orb != null && orb.IsSpawned)
                {
                    Vector3 delta = orb.transform.position - point;
                    delta.y = 0f;
                    float d = delta.sqrMagnitude;
                    if (d < best) best = d;
                }
            }

            return best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
        }

        /// <summary>Packets on the floor or in flight to the player that still hold gold.</summary>
        public int OutstandingPacketCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _liveOrbs.Count; i++)
                {
                    GoldOrb orb = _liveOrbs[i];
                    if (orb != null && orb.IsSpawned && orb.Packet.IsOutstanding)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>Gold still lying on the floor (sum over outstanding packets).</summary>
        public int OutstandingGold
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _liveOrbs.Count; i++)
                {
                    GoldOrb orb = _liveOrbs[i];
                    if (orb != null && orb.IsSpawned && orb.Packet.IsOutstanding)
                    {
                        total += orb.Packet.Amount;
                    }
                }

                return total;
            }
        }

        public int PacketsSpawned => _packetsSpawned;
        public int PacketsCollected => _packetsCollected;
        public int PacketsAutoCollected => _packetsAutoCollected;

        /// <summary>Packets recycled while still holding gold (run reset). Diagnostics.</summary>
        public int PacketsLost => _packetsLost;

        /// <summary>
        /// Kills that got a direct credit because the pool handed back null. The pool is
        /// sized per round so this should read 0; a non-zero value is a bug report.
        /// </summary>
        public int FallbackCredits => _fallbackCredits;

        /// <summary>Pickup credits the wallet refused. Expected 0; a non-zero value is a bug report.</summary>
        public int CreditRefusals => _creditRefusals;

        /// <summary>Why the last refused pickup credit was refused. Diagnostics.</summary>
        public GoldTransactionResult LastCreditRefusal { get; private set; }

        /// <summary>What the last round-end sweep did.</summary>
        public GoldAutoCollectReport LastAutoCollect { get; private set; }

        /// <summary>Raised after every round-end sweep, even an empty one.</summary>
        public event Action<GoldAutoCollectReport> AutoCollected;

        /// <summary>Player point the magnet homes at: roughly chest height, never the feet.</summary>
        public bool TryGetPlayerPoint(out Vector3 point)
        {
            if (_player == null)
            {
                ResolvePlayer();
            }

            if (_player == null)
            {
                point = Vector3.zero;
                return false;
            }

            point = _player.position + Vector3.up * 0.9f;
            return true;
        }

        // ------------------------------------------------------------------ run boundary

        /// <summary>
        /// A run opened. Wipes everything a previous run could have left (packets, wallet,
        /// claims) and opens the wallet and the reward ledger for <paramref name="runId"/>.
        /// Called by the run director from StartRun, before the first round begins.
        /// </summary>
        public void InitializeForRun(int runId)
        {
            ClearOrbs(false);

            _runId = Mathf.Max(1, runId);
            _round = 0;
            _lastSweepRunId = 0;
            _lastSweepRound = 0;

            _wallet.InitializeForRun(_runId);
            int openingGold = DD.Prototype.Difficulty.DifficultyService.StartingGold;   // WP-102: Demolition opens with 150 (0 in Normal)
            if (openingGold > 0) _wallet.Credit(openingGold, GoldSource.RunInitialization, _wallet.NextTransactionId());
            _ledger.BeginRun(_runId, EnemyLifeIds.Last);

            ResetCounters();
        }

        /// <summary>
        /// The run ended. Packets still on the floor are destroyed uncredited (gold does
        /// not survive a run, REQ-018), the ledger closes so a late death event earns
        /// nothing, and the wallet is wiped. Called by the run director from EndRun,
        /// AFTER the summary was built.
        /// </summary>
        public void ResetForRunEnd()
        {
            ClearOrbs(false);
            _ledger.EndRun();
            _wallet.ResetForRunEnd();
            _runId = 0;
            _round = 0;
        }

        /// <summary>
        /// A round is about to start. Any packet somehow still outstanding (only a debug
        /// spawn can produce one after the sweep) is credited rather than destroyed, and
        /// the pool is sized so every enemy of the round can drop its own packet.
        /// </summary>
        public void PrepareForRound(int round, int roundPoolTotal)
        {
            _round = Mathf.Max(1, round);

            if (OutstandingPacketCount > 0)
            {
                SweepOutstanding(_round - 1, true);
            }

            EnsurePool();
            if (_pool == null)
            {
                return;
            }

            int wanted = Mathf.Max(_maxActiveOrbs, Mathf.Max(0, roundPoolTotal) + Mathf.Max(0, _packetPoolMargin));
            if (_pool.Capacity < wanted)
            {
                _pool.SetCapacity(wanted);
            }

            // Prewarm builds inactive bodies but not their visuals; a packet's first
            // OnSpawned creates a primitive and a light. Paying that here, at the round
            // boundary, keeps it out of the kill frame.
            int warm = Mathf.Min(wanted, Mathf.Max(0, roundPoolTotal) + Mathf.Max(0, _packetPoolMargin));
            WarmPackets(warm);
        }

        /// <summary>
        /// The round-end sweep (REQ-076): every packet still holding gold is taken and
        /// credited NOW, synchronously, under <see cref="GoldSource.RoundClearAutoCollect"/>,
        /// so the balance the upgrade screen opens with already contains it. The player
        /// does not walk anywhere; the packets fly to him on unscaled time as a courtesy.
        /// Idempotent per (run, round): a second call for the same round takes nothing.
        /// </summary>
        public GoldAutoCollectReport CollectOutstanding(int round)
        {
            if (_runId > 0 && _lastSweepRunId == _runId && _lastSweepRound == round)
            {
                // Already swept for this round: nothing is taken and the real report stays.
                return new GoldAutoCollectReport(_runId, round, 0, 0, _wallet.Balance, _wallet.Balance);
            }

            _lastSweepRunId = _runId;
            _lastSweepRound = round;

            GoldAutoCollectReport report = SweepOutstanding(round, false);
            LastAutoCollect = report;
            return report;
        }

        /// <summary>
        /// Returns every packet on the map to the pool. With <paramref name="credit"/> the
        /// gold is credited first (sweep); without it the gold is destroyed (run end, F1).
        /// </summary>
        public void ClearOrbs(bool credit)
        {
            if (credit)
            {
                SweepOutstanding(_round, true);
            }

            for (int i = _liveOrbs.Count - 1; i >= 0; i--)
            {
                GoldOrb orb = _liveOrbs[i];
                if (orb == null || !orb.IsSpawned)
                {
                    _liveOrbs.RemoveAt(i);
                    continue;
                }

                // Despawn → OnDespawned → NotifyOrbReleased removes it from the list.
                orb.Despawn();
            }

            _liveOrbs.Clear();
            _litOrbs = 0;
        }

        // ------------------------------------------------------------------ unity

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Gold] A second GoldService was found. Only the first one pays out.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            if (Instance == null)
            {
                Instance = this;
            }

            DamageEvents.EnemyDied += OnEnemyDied;
            PlayerRegistry.RigRegistered += OnRigRegistered;

            ResolvePlayer();
            RegisterDebugPanel();
        }

        protected override void OnDisable()
        {
            DamageEvents.EnemyDied -= OnEnemyDied;
            PlayerRegistry.RigRegistered -= OnRigRegistered;

            UnregisterDebugPanel();

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDisable();
        }

        /// <summary>
        /// Live config contract. Magnet geometry and the packet floor are re-read here, so
        /// dragging the magnet radius in the F1 panel mid-round is felt by packets that
        /// are already lying on the ground.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            EconomyConfigSection economy = config.Economy;

            // WP-008 (DEC-069): the version bonus is "family value minus Standard value",
            // floored at zero. A family typed BELOW Standard would silently pay Standard;
            // say so once rather than let the table lie.
            int standard = GoldRewardCalculator.BaseReward(config);
            for (int i = 0; i < config.Enemies.Tiers.Count; i++)
            {
                EnemyTierSettings tier = config.Enemies.Tiers[i];
                if (tier != null && tier.GoldReward < standard && !_versionWarned)
                {
                    _versionWarned = true;
                    Debug.LogWarning("[Gold] EnemyTierSettings." + tier.Tier + ".GoldReward (" + tier.GoldReward +
                                     ") is below the Standard value (" + standard + "); its version bonus clamps to 0 and it pays " + standard + ".");
                }
            }

            _magnetRadius = Mathf.Max(0f, economy.MagnetRadius);
            _magnetSpeed = Mathf.Max(0.1f, economy.MagnetSpeed);
            _magnetAcceleration = Mathf.Max(0.1f, economy.MagnetAcceleration);
            _contactRadius = Mathf.Max(0.1f, economy.ContactPickupRadius);
            _maxActiveOrbs = Mathf.Max(1, economy.MaxActiveOrbs);

            _orbTuning.Sanitize();

            if (_pool != null && _pool.Capacity < _maxActiveOrbs)
            {
                _pool.SetCapacity(_maxActiveOrbs);
            }
        }

        private void Update()
        {
            if (_streakTimer <= 0f)
            {
                return;
            }

            _streakTimer -= Time.unscaledDeltaTime;
            if (_streakTimer <= 0f)
            {
                _streak = 0;
            }
        }

        // ------------------------------------------------------------------ earning

        private GoldRewardValue _lastRewardValue;
        private bool _versionWarned;

        /// <summary>The last kill's resolved value (base / version / armour slots). Probe and F1 readout; default before any kill.</summary>
        public GoldRewardValue LastRewardValue => _lastRewardValue;

        /// <summary>
        /// The single earning path. Eligibility first (the ledger), value second (the
        /// value contract), then exactly one packet. Every refusal is counted on the
        /// ledger and produces nothing.
        /// </summary>
        /// <summary>
        /// S-02 (WP-069 × WP-077 link.gel.freeze): extra gold for one death, added to the normal kill reward before the
        /// ledger claims it (null = none). S-02's SpecialLinks.HarvestBonus answers +1 for a body that died in gel.
        /// </summary>
        public static Func<EnemyDeathInfo, int> KillBonus;

        private void OnEnemyDied(EnemyDeathInfo info)
        {
            // WP-008: the value is the enemy's SPAWN snapshot (base + version + spawn
            // armour), carried on the event. Nothing here looks at the enemy or its armour.
            GoldRewardValue value = GoldRewardValue.Resolve(in info.Reward, info.GoldReward, info.Tier, Config);
            _lastRewardValue = value;

            // WP-069 × WP-077 (S-02 link.gel.freeze "Gel Harvest"): a run-tree bonus rides on the same packet — income.
            int total = value.Total;
            Func<EnemyDeathInfo, int> bonus = KillBonus;
            if (bonus != null) total += Mathf.Max(0, bonus(info));
            total = DD.Prototype.Difficulty.DifficultyService.ScaleGold(total);   // WP-102: Hard ×1.25, Demolition ×2.5 (Normal unchanged)

            GoldRewardVerdict verdict = _ledger.TryClaim(info.LifeId, total);
            if (verdict != GoldRewardVerdict.Granted)
            {
                return;
            }

            if (!TrySpawnPacket(total, info.Position))
            {
                // Counted safety net. The pool is sized per round, so this reads 0; if it
                // ever does not, the kill still pays rather than vanishing, and the count
                // says so.
                _fallbackCredits++;
                CreditPickup(total, info.Position, _wallet.NextTransactionId());
            }
        }

        private bool TrySpawnPacket(int amount, Vector3 position)
        {
            EnsurePool();

            if (_pool == null)
            {
                return false;
            }

            GoldOrb orb = _pool.Get(position, Quaternion.identity);
            if (orb == null)
            {
                return false;
            }

            int transactionId = _wallet.NextTransactionId();
            if (!orb.Begin(transactionId, amount, _runId, _round, position, this, _orbTuning, _litOrbs < _orbTuning.LightBudget))
            {
                // A body that refused to begin a life is unusable; give it back untouched.
                orb.Despawn();
                return false;
            }

            if (orb.HasLight)
            {
                _litOrbs++;
            }

            _liveOrbs.Add(orb);
            _packetsSpawned++;

            RunAudioEvents.Raise(RunAudioEvent.GoldSpawned, position, 0.5f);
            return true;
        }

        /// <summary>
        /// Called by a packet the moment the player takes it. One credit, under the packet's own id.
        /// True when the wallet took it (the packet may then fly to the HUD; the flight never credits).
        /// </summary>
        internal bool OnPacketCollected(GoldOrb orb)
        {
            _packetsCollected++;
            return CreditPickup(orb.Packet.Amount, orb.transform.position, orb.Packet.TransactionId);
        }

        /// <summary>EditMode seam: the magnet homes at this transform (no PlayerRegistry outside a scene).</summary>
        internal void SetPlayerForTests(Transform player)
        {
            _player = player;
        }

        /// <summary>Called by a packet as it returns to the pool, taken or destroyed.</summary>
        internal void NotifyOrbReleased(GoldOrb orb, bool hadLight, bool lostGold)
        {
            _liveOrbs.Remove(orb);

            if (hadLight)
            {
                _litOrbs = Mathf.Max(0, _litOrbs - 1);
            }

            if (lostGold)
            {
                _packetsLost++;
            }
        }

        private bool CreditPickup(int amount, Vector3 position, int transactionId)
        {
            GoldTransactionResult result = _wallet.Credit(amount, GoldSource.EnemyPickup, transactionId, position);
            if (result != GoldTransactionResult.Applied)
            {
                // Unreachable in a consistent run: the packet reserved its id at spawn and
                // the ledger granted the life once. Counted so a test or the F1 panel can
                // see it rather than a silent zero.
                _creditRefusals++;
                LastCreditRefusal = result;
                return false;
            }

            _streak++;
            _streakTimer = _orbTuning.StreakWindow;

            // Short light and sound on collection (request). The light is the packet's
            // own flash; this is the sound and the HUD counter kick, which the wallet has
            // already published through RunEvents.GoldChanged.
            RunAudioEvents.Raise(RunAudioEvent.GoldCollected, position, 1f, _streak - 1);

            // WP-098B: the feel moment already fired inside _wallet.Credit (FeelEvents.GoldCollected off Credited).
            return true;
        }

        /// <summary>
        /// Takes every outstanding packet. Each packet is credited under its own reserved
        /// transaction id (so nothing can pay it twice), one sound plays for the batch,
        /// and the packets fly out on unscaled time (to the HUD gold symbol when one answers, else to the player).
        /// </summary>
        private GoldAutoCollectReport SweepOutstanding(int round, bool silent)
        {
            int before = _wallet.Balance;
            int packets = 0;
            int amount = 0;
            Vector3 lastPosition = Vector3.zero;

            for (int i = _liveOrbs.Count - 1; i >= 0; i--)
            {
                GoldOrb orb = _liveOrbs[i];
                if (orb == null || !orb.IsSpawned)
                {
                    _liveOrbs.RemoveAt(i);
                    continue;
                }

                if (!orb.Packet.IsOutstanding)
                {
                    continue;
                }

                int transactionId = orb.Packet.TransactionId;
                if (!orb.Packet.TryCollect(GoldCollectKind.RoundClearAuto, out int value))
                {
                    continue;
                }

                int credited = 0;
                if (_wallet.Credit(value, GoldSource.RoundClearAutoCollect, transactionId, orb.transform.position) == GoldTransactionResult.Applied)
                {
                    packets++;
                    amount += value;
                    credited = value;

                    // WP-098B: the Credit above raised FeelEvents.GoldCollected for this coin (one per swept coin; the
                    // whole sweep runs in one frame, so its ChainIndex climbs 0, 1, 2…); the batch audio cue below stays.
                }

                _packetsAutoCollected++;
                lastPosition = orb.transform.position;
                orb.BeginSweepFlight(credited);
            }

            // Only the round-clear path records the report (CollectOutstanding); the silent
            // sweeps (round start leftovers, F1) must not overwrite what the screen quotes.
            var report = new GoldAutoCollectReport(_runId, round, packets, amount, before, _wallet.Balance);

            if (packets > 0 && !silent)
            {
                RunAudioEvents.Raise(RunAudioEvent.GoldCollected, lastPosition, 1f, Mathf.Min(packets - 1, 7));
            }

            Action<GoldAutoCollectReport> handler = AutoCollected;
            if (handler != null)
            {
                try { handler.Invoke(report); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            return report;
        }

        // ------------------------------------------------------------------ plumbing

        private void EnsurePool()
        {
            if (_pool != null)
            {
                return;
            }

            // One template for the whole play session: PoolService keys pools by prefab,
            // so a template per service instance (per Run scene load) would leak a pool of
            // packets under the DontDestroyOnLoad root on every run.
            if (s_template == null)
            {
                s_template = GoldOrb.CreateTemplate();
            }

            _pool = PoolService.GetOrCreate(
                s_template,
                Mathf.Min(_maxActiveOrbs, 16),
                _maxActiveOrbs,
                PoolExhaustedPolicy.ReturnNull);
        }

        /// <summary>
        /// Checks out <paramref name="count"/> packets at once and returns them, so that
        /// this many bodies exist AND every one of them has built its visuals before the
        /// first kill. Taking them all before releasing any is the point: the pool hands
        /// out its free bodies first, so a partial take would never create a new one, and
        /// a prewarmed body has no visuals until its first OnSpawned. Cheap (a few dozen
        /// activations at a round boundary) and silent: no list entry, no sound.
        /// </summary>
        private void WarmPackets(int count)
        {
            if (_pool == null || count <= 0)
            {
                return;
            }

            _warmScratch.Clear();
            for (int i = 0; i < count; i++)
            {
                GoldOrb orb = _pool.Get(Vector3.down * 100f, Quaternion.identity);
                if (orb == null)
                {
                    break;
                }

                _warmScratch.Add(orb);
            }

            for (int i = 0; i < _warmScratch.Count; i++)
            {
                _warmScratch[i].Despawn();
            }

            _warmScratch.Clear();
        }

        private void ResetCounters()
        {
            _streak = 0;
            _streakTimer = 0f;
            _packetsSpawned = 0;
            _packetsCollected = 0;
            _packetsAutoCollected = 0;
            _packetsLost = 0;
            _fallbackCredits = 0;
            _creditRefusals = 0;
            LastCreditRefusal = GoldTransactionResult.Applied;
            LastAutoCollect = default;
        }

        private void OnRigRegistered(PlayerRig rig)
        {
            if (rig != null)
            {
                _player = rig.transform;
            }
        }

        private void ResolvePlayer()
        {
            PlayerRig rig = PlayerRegistry.Primary;
            _player = rig != null ? rig.transform : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            s_template = null;
        }

        // ------------------------------------------------------------------ debug panel

        private void RegisterDebugPanel()
        {
            var entries = new List<DebugPanelEntry>(10);

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Economy,
                "Wallet", DescribeWallet, -100));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Economy,
                "Packets", DescribePackets, -99));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Economy,
                "Reward ledger", DescribeLedger, -98));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Give 50 gold", () => _wallet.Credit(50), -80));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Give 250 gold", () => _wallet.Credit(250), -79));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Clear wallet", () => _wallet.DebugSet(0), -78));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Sweep all packets (credit, like a round end)", () => ClearOrbs(true), -70));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Destroy all packets (no credit)", () => ClearOrbs(false), -69));

            entries.Add(DebugPanelRegistry.AddSlider(DebugPanelTabs.Economy,
                "Magnet radius (m)",
                () => _magnetRadius,
                v =>
                {
                    if (Config == null)
                    {
                        return;
                    }

                    Config.Economy.MagnetRadius = v;
                    Config.NotifyChanged();
                },
                0f, 12f, false, -60));

            _debugEntries = entries.ToArray();
        }

        private void UnregisterDebugPanel()
        {
            for (int i = 0; i < _debugEntries.Length; i++)
            {
                DebugPanelRegistry.Remove(_debugEntries[i]);
            }

            _debugEntries = Array.Empty<DebugPanelEntry>();
        }

        private string DescribeWallet()
        {
            return string.Format("run {0} | {1} gold | earned {2} (pickup {3}, sweep {4}) | spent {5} | tx {6}",
                _wallet.RunId, _wallet.Balance, _wallet.TotalEarned, _wallet.EarnedByPickup,
                _wallet.EarnedByAutoCollect, _wallet.TotalSpent, _wallet.AppliedTransactions);
        }

        private string DescribePackets()
        {
            return string.Format(
                "live {0} (outstanding {1} = {2}g, lit {3}) | spawned {4} taken {5} swept {6} lost {7} fallback {8} | pool {9}/{10} | magnet {11:0.0}m @ {12:0}m/s | last sweep R{13}: {14} pk {15}g",
                _liveOrbs.Count,
                OutstandingPacketCount,
                OutstandingGold,
                _litOrbs,
                _packetsSpawned,
                _packetsCollected,
                _packetsAutoCollected,
                _packetsLost,
                _fallbackCredits,
                _pool != null ? _pool.CountTotal : 0,
                _pool != null ? _pool.Capacity : 0,
                _magnetRadius,
                _magnetSpeed,
                LastAutoCollect.Round,
                LastAutoCollect.Packets,
                LastAutoCollect.Amount);
        }

        private string DescribeLedger()
        {
            return string.Format("run {0} live={1} watermark {2} | granted {3} | refused: notLive {4} stale {5} dup {6} invalid {7} noValue {8}",
                _ledger.RunId, _ledger.IsRunLive, _ledger.LifeWatermark, _ledger.Granted,
                _ledger.RejectedRunNotLive, _ledger.RejectedStale, _ledger.RejectedDuplicate,
                _ledger.RejectedInvalid, _ledger.RejectedNoValue);
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos || !TryGetPlayerPoint(out Vector3 point))
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(point, _magnetRadius);

            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(point, _contactRadius);
        }
    }
}
