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
    /// <summary>
    /// Gold: who earns it, how it reaches the player, and nothing else.
    ///
    /// THE COLLECTION METHOD IS AN OPEN PRODUCT DECISION. That is why this is a mode
    /// switch and not an implementation. The three modes differ only in what happens
    /// between "an enemy died" and "the wallet went up":
    ///
    ///   DirectCredit    - the wallet goes up on the death event. No object, no travel,
    ///                     no reason to walk into the swarm. Fastest to read, weakest
    ///                     pull on the player's route.
    ///   ContactPickup   - an orb drops and stays. The player must physically stand on
    ///                     it. Strongest route pressure: gold is a reason to step where
    ///                     the zombies just were.
    ///   ProximityMagnet - an orb drops and jumps to the player from ~3.5 m. The route
    ///                     pressure survives (you still have to go near it) but the
    ///                     fiddly last metre does not.
    ///
    /// The default is ProximityMagnet purely so the prototype is easy to observe. That
    /// is stated in PROTOTYPE-ONLY.md and is NOT a product decision.
    ///
    /// GOLD AND XP NEVER TOUCH. This class raises no XP, holds no XP and has no
    /// reference to anything that awards XP (request: earning or spending gold grants no
    /// profile XP). Kill XP is counted by the run director off the same death event.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GoldService : PrototypeConfigBehaviour
    {
        public static GoldService Instance { get; private set; }

        [Header("Orb presentation (PROTOTYPE-ONLY, all invented)")]
        [SerializeField] private GoldOrbTuning _orbTuning = new GoldOrbTuning();

        [Header("Diagnostics")]
        [Tooltip("Draw the magnet radius around the player in the editor.")]
        [SerializeField] private bool _drawGizmos = true;

        private readonly GoldWallet _wallet = new GoldWallet();
        private readonly List<GoldOrb> _liveOrbs = new List<GoldOrb>(64);

        private PrototypePool<GoldOrb> _pool;
        private Transform _player;

        private int _litOrbs;
        private int _streak;
        private float _streakTimer;
        private int _directCredits;
        private int _orbsSpawned;
        private int _orbsCollected;
        private int _orbsExpired;

        // cached from config
        private PrototypeGoldCollectionMode _mode = PrototypeGoldCollectionMode.ProximityMagnet;
        private float _magnetRadius = 3.5f;
        private float _magnetSpeed = 14f;
        private float _magnetAcceleration = 38f;
        private float _contactRadius = 0.9f;
        private float _orbLifetime = 22f;
        private int _maxActiveOrbs = 60;

        private DebugPanelEntry[] _debugEntries = Array.Empty<DebugPanelEntry>();

        // ------------------------------------------------------------------ public API

        /// <summary>The run wallet. The upgrade shop spends from this instance.</summary>
        public GoldWallet Wallet => _wallet;

        /// <summary>Active collection mode, straight from config. F1 switches it live.</summary>
        public PrototypeGoldCollectionMode Mode => _mode;

        public float MagnetRadius => _magnetRadius;

        public float MagnetSpeed => _magnetSpeed;

        public float MagnetAcceleration => _magnetAcceleration;

        /// <summary>Distance at which an orb is taken. Also the magnet's capture distance.</summary>
        public float ContactRadius => _contactRadius;

        public float OrbLifetime => _orbLifetime;

        public int LiveOrbCount => _liveOrbs.Count;

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

        /// <summary>Switch collection mode. Writes it back to config so the change is live and visible.</summary>
        public void SetMode(PrototypeGoldCollectionMode mode)
        {
            if (Config == null)
            {
                _mode = mode;
                return;
            }

            if (Config.Economy.GoldCollectionMode == mode)
            {
                return;
            }

            Config.Economy.GoldCollectionMode = mode;
            Config.NotifyChanged();
        }

        /// <summary>Return every orb on the map. Called on run end and by the F1 panel.</summary>
        public void ClearOrbs(bool credit)
        {
            for (int i = _liveOrbs.Count - 1; i >= 0; i--)
            {
                GoldOrb orb = _liveOrbs[i];
                if (orb == null || !orb.IsSpawned)
                {
                    continue;
                }

                if (credit)
                {
                    _wallet.Credit(orb.Amount, orb.transform.position);
                }

                orb.Despawn();
            }

            _liveOrbs.Clear();
            _litOrbs = 0;
        }

        /// <summary>Wipes the run wallet and the map. Gold does not survive a run (REQ-018).</summary>
        public void ResetForNewRun()
        {
            ClearOrbs(false);
            _wallet.ResetForNewRun();

            _streak = 0;
            _streakTimer = 0f;
            _directCredits = 0;
            _orbsSpawned = 0;
            _orbsCollected = 0;
            _orbsExpired = 0;
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
        /// Live config contract. Mode, magnet geometry and the orb ceiling are all
        /// re-read here, so dragging the magnet radius in the F1 panel mid-round is felt
        /// by orbs that are already lying on the ground.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            EconomyConfigSection economy = config.Economy;

            _mode = economy.GoldCollectionMode;
            _magnetRadius = Mathf.Max(0f, economy.MagnetRadius);
            _magnetSpeed = Mathf.Max(0.1f, economy.MagnetSpeed);
            _magnetAcceleration = Mathf.Max(0.1f, economy.MagnetAcceleration);
            _contactRadius = Mathf.Max(0.1f, economy.ContactPickupRadius);
            _orbLifetime = Mathf.Max(0.5f, economy.OrbLifetime);
            _maxActiveOrbs = Mathf.Max(1, economy.MaxActiveOrbs);

            _orbTuning.Sanitize();

            if (_pool != null && _pool.Capacity < _maxActiveOrbs)
            {
                _pool.SetCapacity(_maxActiveOrbs);
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_streakTimer > 0f)
            {
                _streakTimer -= dt;
                if (_streakTimer <= 0f)
                {
                    _streak = 0;
                }
            }

            PruneOrbs();
        }

        // ------------------------------------------------------------------ earning

        /// <summary>
        /// The single earning path. Every mode branches from here.
        ///
        /// The reward comes off the death event, which already carries the value read
        /// from PrototypeConfig.GoldReward(tier) by the enemy. The config method is only
        /// called as a fallback, so the reward is never derived twice and never derived
        /// here.
        /// </summary>
        private void OnEnemyDied(EnemyDeathInfo info)
        {
            int reward = info.GoldReward;
            if (reward <= 0 && Config != null)
            {
                reward = Config.GoldReward(info.Tier);
            }

            if (reward <= 0)
            {
                return;
            }

            if (_mode == PrototypeGoldCollectionMode.DirectCredit)
            {
                _directCredits++;
                CreditAndAnnounce(reward, info.Position);
                return;
            }

            if (!TrySpawnOrb(reward, info.Position))
            {
                // The orb ceiling is a presentation limit, not an economy one: a kill
                // that cannot get a visible orb still pays. Losing gold because sixty
                // orbs were already on the floor would be an invisible economy bug.
                CreditAndAnnounce(reward, info.Position);
            }
        }

        private bool TrySpawnOrb(int amount, Vector3 position)
        {
            EnsurePool();

            if (_pool == null)
            {
                return false;
            }

            GoldOrb orb = _pool.Get(position, Quaternion.identity);
            if (orb == null)
            {
                // ReturnNull policy: the ceiling is reached. Not an error.
                return false;
            }

            bool withLight = _litOrbs < _orbTuning.LightBudget;
            if (withLight)
            {
                _litOrbs++;
            }

            orb.Begin(amount, position, this, _orbTuning, withLight);

            _liveOrbs.Add(orb);
            _orbsSpawned++;

            RunAudioEvents.Raise(RunAudioEvent.GoldSpawned, position, 0.5f);
            return true;
        }

        /// <summary>Called by an orb the moment it is taken.</summary>
        internal void OnOrbCollected(int amount, Vector3 position)
        {
            _orbsCollected++;
            CreditAndAnnounce(amount, position);
        }

        /// <summary>Called by an orb as it returns to the pool, collected or expired.</summary>
        internal void NotifyOrbReleased(GoldOrb orb, bool hadLight)
        {
            _liveOrbs.Remove(orb);

            if (hadLight)
            {
                _litOrbs = Mathf.Max(0, _litOrbs - 1);
            }
        }

        private void CreditAndAnnounce(int amount, Vector3 position)
        {
            _wallet.Credit(amount, position);

            _streak++;
            _streakTimer = _orbTuning.StreakWindow;

            // Short light and sound on collection (request). The light is the orb's own
            // flash; this is the sound and the HUD counter kick, which the wallet has
            // already published through RunEvents.GoldChanged.
            RunAudioEvents.Raise(RunAudioEvent.GoldCollected, position, 1f, _streak - 1);
        }

        // ------------------------------------------------------------------ plumbing

        private void EnsurePool()
        {
            if (_pool != null)
            {
                return;
            }

            GoldOrb template = GoldOrb.CreateTemplate();

            _pool = PoolService.GetOrCreate(
                template,
                Mathf.Min(_maxActiveOrbs, 16),
                _maxActiveOrbs,
                PoolExhaustedPolicy.ReturnNull);
        }

        private void PruneOrbs()
        {
            for (int i = _liveOrbs.Count - 1; i >= 0; i--)
            {
                GoldOrb orb = _liveOrbs[i];
                if (orb == null || !orb.IsSpawned)
                {
                    _liveOrbs.RemoveAt(i);
                }
            }
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

        // ------------------------------------------------------------------ debug panel

        private void RegisterDebugPanel()
        {
            var entries = new List<DebugPanelEntry>(12);

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Economy,
                "Wallet", DescribeWallet, -100));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Economy,
                "Collection", DescribeCollection, -99));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Mode: DirectCredit",
                () => SetMode(PrototypeGoldCollectionMode.DirectCredit), -90));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Mode: ContactPickup",
                () => SetMode(PrototypeGoldCollectionMode.ContactPickup), -89));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Mode: ProximityMagnet (default)",
                () => SetMode(PrototypeGoldCollectionMode.ProximityMagnet), -88));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Give 50 gold", () => _wallet.Credit(50), -80));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Give 250 gold", () => _wallet.Credit(250), -79));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Clear wallet", () => _wallet.DebugSet(0), -78));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Collect all orbs on the map", () => ClearOrbs(true), -70));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Economy,
                "Destroy all orbs (no credit)", () => ClearOrbs(false), -69));

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
            return string.Format("{0} gold | earned {1} | spent {2}",
                _wallet.Balance, _wallet.TotalEarned, _wallet.TotalSpent);
        }

        private string DescribeCollection()
        {
            return string.Format(
                "{0} | orbs {1}/{2} live (lit {3}) | spawned {4} taken {5} expired {6} | direct {7} | magnet {8:0.0}m @ {9:0}m/s",
                _mode,
                _liveOrbs.Count,
                _maxActiveOrbs,
                _litOrbs,
                _orbsSpawned,
                _orbsCollected,
                _orbsExpired,
                _directCredits,
                _magnetRadius,
                _magnetSpeed);
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
