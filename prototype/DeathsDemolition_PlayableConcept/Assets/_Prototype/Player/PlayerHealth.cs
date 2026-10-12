using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// Player health, regeneration and death. REQ-041, REQ-044, REQ-045, REQ-048.
    ///
    /// RULES THIS CLASS ENFORCES, AND WHERE THEY COME FROM
    ///   * No invulnerability window of any kind. There is no i-frame timer, no per
    ///     attacker cooldown and no minimum interval between hits, so two attacks
    ///     landing on the same frame both apply in full.            (REQ-044)
    ///   * Taking damage never touches movement, aim or firing. This class only writes
    ///     a number and raises events; nothing here can stall input. (REQ-045)
    ///   * One life, no revive. Health reaching zero ends the run.   (REQ-048)
    ///   * Regeneration starts after HealthRegenDelay seconds without damage, restores
    ///     HealthRegenRate per second up to HealthRegenCeilingFraction of maximum, and
    ///     any new damage resets the wait.                           (REQ-041)
    ///
    /// All four numbers live in PrototypeConfig.Player and are re-read in ApplyConfig,
    /// so editing them in the F1 panel mid-run takes effect on the next frame.
    ///
    /// THE THREE HEALTH UPGRADES
    ///   MaximumHealth     multiplies the ceiling
    ///   RegenerationDelay multiplies the wait, downwards
    ///   RegenerationRate  multiplies the HP per second
    ///
    /// All three are read through <see cref="PlayerUpgradeBinding"/> inside ApplyConfig, so
    /// a card bought at a round end lands on the next frame without any apply step, and a
    /// live config edit and a purchased tier compose instead of overwriting each other.
    ///
    /// WHAT A MAXIMUM-HEALTH CARD DOES TO THE HEALTH BAR
    /// A tier change PRESERVES THE CURRENT FRACTION: at 40/100, buying +15 % leaves 46/115.
    /// It is not a heal and it is not a punishment, and it is the only sane answer for a
    /// card bought at a round end - scaling absolutely would make the purchase read as a
    /// loss of health on the bar, and topping up would make it a stealth full-heal.
    /// A live config edit is a different event and keeps its old rule: absolute health is
    /// held, and a player who was at full stays at full.
    ///
    /// HOW ATTACKERS REACH IT
    ///     var health = hitCollider.GetComponentInParent&lt;PlayerHealth&gt;();
    ///     health.ApplyDamage(new PlayerDamageRequest(damage, point, transform.position, "Zombie.Standard.Swipe"));
    ///
    /// The component sits on the same GameObject as the player's collider, so
    /// GetComponentInParent from any attack trigger finds it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHealth : PrototypeConfigBehaviour
    {
        // ------------------------------------------------------------ cached config

        private readonly PlayerUpgradeBinding _upgrades = new PlayerUpgradeBinding();

        private float _maxHealth;
        private float _regenDelay;
        private float _regenRate;
        private float _regenCeilingFraction;

        /// <summary>MaximumHealth tier the cached ceiling was computed from. -1 before the first apply.</summary>
        private int _appliedMaxHealthTier = -1;

        /// <summary>WP-104 B2: the run tree's max-health scale the cached ceiling was computed from (a change keeps the fraction, like a tier).</summary>
        private float _appliedTreeHealthScale = 1f;

        /// <summary>WP-104 B2 sv_shield / sv_dodge / sv_cap: the tree's shield, ghost and Undying Oath state (TreeHooks owns the rules).</summary>
        private DD.Prototype.Rounds.TreeShieldState _treeShield;

        // ------------------------------------------------------------------- state

        private float _current;
        private float _timeSinceDamage = float.MaxValue;
        private bool _dead;
        private bool _initialised;

        /// <summary>
        /// Diagnosis aid exposed on the F1 panel ONLY. This is not a gameplay rule and
        /// must never be set by gameplay code: REQ-044 forbids damage immunity.
        /// </summary>
        public bool DiagnosisInvulnerable { get; set; }

        // ------------------------------------------------------------------ queries

        public float Current
        {
            get { return _current; }
        }

        public float Max
        {
            get { return _maxHealth; }
        }

        public float Normalized
        {
            get { return _maxHealth > 0f ? Mathf.Clamp01(_current / _maxHealth) : 0f; }
        }

        public bool IsDead
        {
            get { return _dead; }
        }

        /// <summary>Seconds since the last damage. Large when the player has never been hit.</summary>
        public float TimeSinceDamage
        {
            get { return _timeSinceDamage; }
        }

        /// <summary>Seconds before regeneration begins, 0 once it has.</summary>
        public float SecondsUntilRegen
        {
            get { return Mathf.Max(0f, _regenDelay - _timeSinceDamage); }
        }

        public bool IsRegenerating
        {
            get { return !_dead && _timeSinceDamage >= _regenDelay && _current < RegenCeiling; }
        }

        /// <summary>Seconds of quiet before regeneration starts, RegenerationDelay tiers included.</summary>
        public float RegenDelaySeconds
        {
            get { return _regenDelay; }
        }

        /// <summary>HP per second once regeneration starts, RegenerationRate tiers included.</summary>
        public float RegenRatePerSecond
        {
            get { return _regenRate; }
        }

        /// <summary>Player-side half of the run build, e.g. "HP2 / RR1". F1 readout.</summary>
        public string UpgradeBuildDescription
        {
            get { return _upgrades.DescribeBuild(); }
        }

        /// <summary>WP-104 B2 (S-09 HUD): the tree shield's points and its pool for the current maximum (0 without a shield node).</summary>
        public float TreeShield => _treeShield.Shield;

        public float TreeShieldMax => DD.Prototype.Rounds.TreeHooks.ShieldMax(_maxHealth);

        private float RegenCeiling
        {
            get { return _maxHealth * Mathf.Clamp01(_regenCeilingFraction); }
        }

        public PlayerHealthSnapshot Snapshot
        {
            get
            {
                return new PlayerHealthSnapshot(
                    this, _current, _maxHealth, IsRegenerating, SecondsUntilRegen, _dead);
            }
        }

        // ------------------------------------------------------------------- events

        /// <summary>The player took a hit. Raised after health has been written.</summary>
        public event Action<PlayerDamageInfo> Damaged;

        /// <summary>Health changed for any reason: damage, healing, regeneration, reset.</summary>
        public event Action<PlayerHealthSnapshot> HealthChanged;

        /// <summary>Health reached zero. The run is over; there is no revive.</summary>
        public event Action<PlayerDeathInfo> Died;

        /// <summary>Static mirror of <see cref="Damaged"/>, for systems that spawn after the player.</summary>
        public static event Action<PlayerDamageInfo> AnyDamaged;

        /// <summary>Static mirror of <see cref="HealthChanged"/>. This is the HUD's simplest hook.</summary>
        public static event Action<PlayerHealthSnapshot> AnyHealthChanged;

        /// <summary>
        /// Static mirror of <see cref="Died"/>. The run flow binds to this:
        ///
        ///     PlayerHealth.AnyPlayerDied += info =&gt; EndRun(info);
        /// </summary>
        public static event Action<PlayerDeathInfo> AnyPlayerDied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AnyDamaged = null;
            AnyHealthChanged = null;
            AnyPlayerDied = null;
        }

        // ------------------------------------------------------------------- config

        /// <summary>
        /// Bound BEFORE base.OnEnable, because base.OnEnable runs the first ApplyConfig and
        /// that one already has to see the run's owned tiers.
        /// </summary>
        protected override void OnEnable()
        {
            _upgrades.Changed += OnUpgradesChanged;
            _upgrades.Bind();

            base.OnEnable();

            // WP-104 B2: the tree's stats (O1), the kill heal (O4, sv_leech) and the shield for this run.
            DD.Prototype.Rounds.TreeEvents.StatsChanged -= OnTreeStatsChanged;
            DD.Prototype.Rounds.TreeEvents.StatsChanged += OnTreeStatsChanged;
            DD.Prototype.Damage.DamageEvents.EnemyDied -= OnTreeKill;
            DD.Prototype.Damage.DamageEvents.EnemyDied += OnTreeKill;
            DD.Prototype.Rounds.RunEvents.RunStarted -= OnTreeRunStarted;
            DD.Prototype.Rounds.RunEvents.RunStarted += OnTreeRunStarted;
            ResetTreeShield(0);
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            _upgrades.Unbind();
            _upgrades.Changed -= OnUpgradesChanged;
            DD.Prototype.Rounds.TreeEvents.StatsChanged -= OnTreeStatsChanged;
            DD.Prototype.Damage.DamageEvents.EnemyDied -= OnTreeKill;
            DD.Prototype.Rounds.RunEvents.RunStarted -= OnTreeRunStarted;
        }

        /// <summary>WP-104 B2 O1: a tree purchase / re-resolve — recompute like a card (the fraction is kept).</summary>
        private void OnTreeStatsChanged()
        {
            if (HasConfig)
            {
                ApplyConfig(Config);
            }
        }

        /// <summary>WP-104 B2 O4 sv_leech: a kill heals a share of the maximum (Standard / Elite / Ultra, capped per second by TreeHooks).</summary>
        private void OnTreeKill(DD.Prototype.Damage.EnemyDeathInfo info)
        {
            float share = DD.Prototype.Rounds.TreeHooks.OnKillHeal(info.Tier, Time.time, info.Position);
            if (share > 0f) Heal(share * _maxHealth);
        }

        private void OnTreeRunStarted(int startRound)
        {
            DD.Prototype.Rounds.RunDirector director = DD.Prototype.Rounds.RunDirector.Instance;
            ResetTreeShield(director != null ? director.RunId : 0);
        }

        /// <summary>WP-104 B2 O2: a fresh shield state per run (its dodge roll seeded by the run), filled to the pool.</summary>
        private void ResetTreeShield(int runId)
        {
            _treeShield = DD.Prototype.Rounds.TreeShieldState.Create(DD.Prototype.Rounds.TreeHooks.SeedFor(runId, 3u));
            DD.Prototype.Rounds.TreeHooks.ShieldFill(_maxHealth, ref _treeShield);
        }

        /// <summary>A health card was bought, or the build was wiped. Recompute, do not cache.</summary>
        private void OnUpgradesChanged()
        {
            if (HasConfig)
            {
                ApplyConfig(Config);
            }
        }

        protected override void ApplyConfig(PrototypeConfig config)
        {
            PlayerConfigSection player = config.Player;

            float previousMax = _maxHealth;
            bool wasFull = _initialised && previousMax > 0f && _current >= previousMax - 0.0001f;
            float previousFraction = _initialised && previousMax > 0f ? Mathf.Clamp01(_current / previousMax) : 1f;

            // A tier change and a config edit both land here, and they want different
            // answers for the current health, so the two are told apart by the tier itself
            // rather than by who called.
            int maxHealthTier = _upgrades.Tier(UpgradeBranch.MaximumHealth);
            float treeHealthScale = DD.Prototype.Rounds.TreeHooks.MaxHealthScale;   // WP-104 B2 sv_hp1-3
            bool tierChanged = _initialised && (maxHealthTier != _appliedMaxHealthTier || !Mathf.Approximately(treeHealthScale, _appliedTreeHealthScale));
            _appliedMaxHealthTier = maxHealthTier;
            _appliedTreeHealthScale = treeHealthScale;

            _maxHealth = Mathf.Max(1f, player.MaxHealth * _upgrades.MaxHealthMultiplier * treeHealthScale);
            _regenDelay = Mathf.Max(0f, player.HealthRegenDelay * _upgrades.RegenDelayMultiplier * DD.Prototype.Rounds.TreeHooks.RegenDelayScale);
            _regenRate = Mathf.Max(0f, player.HealthRegenRate * _upgrades.RegenRateMultiplier);
            _regenCeilingFraction = Mathf.Clamp01(player.HealthRegenCeilingFraction);

            if (!_initialised)
            {
                _initialised = true;
                _current = _maxHealth;
                RaiseHealthChanged();
                return;
            }

            // Bought a Maximum Health tier: keep the fraction, so the bar does not move and
            // the purchase is neither a heal nor a loss. A dead player has fraction 0 and
            // stays dead; there is no revive (REQ-048).
            //
            // Live edit of max health: a player who was at full stays at full, anyone
            // else keeps their absolute health clamped to the new ceiling.
            float target = tierChanged
                ? Mathf.Clamp(previousFraction * _maxHealth, 0f, _maxHealth)
                : (wasFull ? _maxHealth : Mathf.Min(_current, _maxHealth));

            if (!Mathf.Approximately(target, _current))
            {
                _current = target;
                RaiseHealthChanged();
            }
        }

        // ------------------------------------------------------------------- update

        private void Update()
        {
            if (_dead || !_initialised)
            {
                return;
            }

            // Scaled time: pause freezes the regeneration wait along with everything else.
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            DD.Prototype.Rounds.TreeHooks.ShieldTick(dt, Time.time, _maxHealth, ref _treeShield, transform.position);   // WP-104 B2 O3: the shield refills after its delay (ShieldUp at the player)

            if (_timeSinceDamage < float.MaxValue * 0.5f)
            {
                _timeSinceDamage += dt;
            }

            if (_timeSinceDamage < _regenDelay || _regenRate <= 0f)
            {
                return;
            }

            float ceiling = RegenCeiling;
            if (_current >= ceiling)
            {
                return;
            }

            _current = Mathf.Min(ceiling, _current + _regenRate * dt);
            RaiseHealthChanged();
        }

        private PlayerMotor _motor;

        /// <summary>
        /// WP-077 body lane (ks.body.bloodstep / ks.body.breath): a kill heals <paramref name="heal"/> (up to the
        /// maximum) and cuts the regeneration wait by <paramref name="cutWaitSeconds"/>. Nothing while dead.
        /// </summary>
        public void ApplyTreeRecovery(float heal, float cutWaitSeconds)
        {
            if (_dead || !_initialised) return;
            if (cutWaitSeconds > 0f && _timeSinceDamage < float.MaxValue * 0.5f) _timeSinceDamage += cutWaitSeconds;
            if (heal > 0f && _current < _maxHealth)
            {
                _current = Mathf.Min(_maxHealth, _current + heal);
                RaiseHealthChanged();
            }
        }

        // ------------------------------------------------------------------ damage

        /// <summary>Convenience overload for attackers that only know an amount and a position.</summary>
        public bool ApplyDamage(float amount, Vector3 sourcePosition, string sourceId = null)
        {
            return ApplyDamage(new PlayerDamageRequest(amount, transform.position, sourcePosition, sourceId));
        }

        /// <summary>
        /// Applies one hit. Returns true when health actually changed.
        ///
        /// Deliberately has no cooldown of any kind: call it as many times per frame as
        /// there are attacks landing, and every call applies (REQ-044).
        /// </summary>
        public bool ApplyDamage(in PlayerDamageRequest request)
        {
            if (_dead || !_initialised || request.Amount <= 0f)
            {
                return false;
            }

            if (DiagnosisInvulnerable)
            {
                // Diagnosis only. Still resets the regen wait so the readout stays honest.
                _timeSinceDamage = 0f;
                return false;
            }

            // WP-077 body lane: Second Wind's grace window, then Thick Skin / Ash Step / Iron Knee on the amount.
            if (DD.Prototype.Rounds.RunTreeHooks.PlayerInvulnerable)
            {
                _timeSinceDamage = 0f;
                return false;
            }

            if (_motor == null) _motor = GetComponentInParent<PlayerMotor>();
            float amount = DD.Prototype.Rounds.RunTreeHooks.PlayerDamage(request.Amount, _current >= _maxHealth, _motor != null && _motor.Speed01 > 0.1f);

            float before = _current;
            // WP-104 B2 O2: invulnerable → ghost → dodge (≤ 30 %) → shield → Undying Oath. Neutral: the amount, unchanged.
            DD.Prototype.Rounds.TreeHitResult hit = DD.Prototype.Rounds.TreeHooks.IncomingDamage(amount, Time.time, _current, _maxHealth, ref _treeShield, transform.position);
            if (hit.Outcome != DD.Prototype.Rounds.TreeHitOutcome.None && hit.Damage <= 0f && hit.HealthFloor <= 0f) { _timeSinceDamage = 0f; return false; }   // the tree took it all
            float applied = Mathf.Min(before, hit.Damage);
            _current = Mathf.Max(0f, before - hit.Damage);
            if (hit.HealthFloor > _current) { _current = hit.HealthFloor; applied = Mathf.Max(0f, before - _current); }   // Undying Oath (HealthFloor > 0 only then)
            if (_current <= 0f && DD.Prototype.Rounds.RunTreeHooks.TrySecondWind(out float kept))
            {
                _current = Mathf.Min(_maxHealth, kept);   // WP-077 cap.body.secondbreath: once per run
                applied = Mathf.Max(0f, before - _current);
            }

            _timeSinceDamage = 0f;

            Vector3 point = request.WorldPoint == Vector3.zero ? transform.position : request.WorldPoint;

            Vector3 hitDirection = Vector3.zero;
            if (request.SourcePosition != Vector3.zero)
            {
                Vector3 delta = transform.position - request.SourcePosition;
                delta.y = 0f;
                if (delta.sqrMagnitude > 1e-6f)
                {
                    hitDirection = delta.normalized;
                }
            }

            bool fatal = _current <= 0f;

            var info = new PlayerDamageInfo(
                this, request.Amount, applied, before, _current, _maxHealth, point, hitDirection,
                request.SourceId, fatal);

            RaiseDamaged(info);
            RaiseHealthChanged();

            if (fatal)
            {
                Die(hitDirection, request.Amount - applied, request.SourceId);
            }

            return true;
        }

        /// <summary>Restores health. Returns how much was actually restored.</summary>
        public float Heal(float amount)
        {
            if (_dead || !_initialised || amount <= 0f)
            {
                return 0f;
            }

            float before = _current;
            _current = Mathf.Min(_maxHealth, _current + amount);

            float healed = _current - before;
            if (healed > 0f)
            {
                RaiseHealthChanged();
            }

            return healed;
        }

        /// <summary>Full heal. The F1 panel's "Heal Player" button.</summary>
        public void HealToFull()
        {
            if (_dead || !_initialised)
            {
                return;
            }

            if (Mathf.Approximately(_current, _maxHealth))
            {
                return;
            }

            _current = _maxHealth;
            RaiseHealthChanged();
        }

        /// <summary>
        /// Puts the player back to a fresh run state. Called by the run flow when a new
        /// run starts; it does NOT revive mid-run, since revive does not exist.
        /// </summary>
        public void ResetForNewRun()
        {
            _dead = false;
            _current = _maxHealth;
            _timeSinceDamage = float.MaxValue;
            DiagnosisInvulnerable = false;
            RaiseHealthChanged();
        }

        private void Die(Vector3 hitDirection, float overkill, string killedBy)
        {
            if (_dead)
            {
                return;
            }

            _dead = true;
            _current = 0f;

            // The snapshot raised by ApplyDamage a moment ago still said IsDead == false, so
            // without this the HUD never receives a "dead" health state (WP-002).
            RaiseHealthChanged();

            var info = new PlayerDeathInfo(
                this, transform.position, hitDirection, Mathf.Max(0f, overkill), killedBy, Time.time);

            Action<PlayerDeathInfo> handler = Died;
            if (handler != null)
            {
                try
                {
                    handler.Invoke(info);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }

            Action<PlayerDeathInfo> anyHandler = AnyPlayerDied;
            if (anyHandler != null)
            {
                try
                {
                    anyHandler.Invoke(info);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        private void RaiseDamaged(in PlayerDamageInfo info)
        {
            Action<PlayerDamageInfo> handler = Damaged;
            if (handler != null)
            {
                try
                {
                    handler.Invoke(info);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }

            Action<PlayerDamageInfo> anyHandler = AnyDamaged;
            if (anyHandler != null)
            {
                try
                {
                    anyHandler.Invoke(info);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        private void RaiseHealthChanged()
        {
            PlayerHealthSnapshot snapshot = Snapshot;

            Action<PlayerHealthSnapshot> handler = HealthChanged;
            if (handler != null)
            {
                try
                {
                    handler.Invoke(snapshot);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }

            Action<PlayerHealthSnapshot> anyHandler = AnyHealthChanged;
            if (anyHandler != null)
            {
                try
                {
                    anyHandler.Invoke(snapshot);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }
    }
}
