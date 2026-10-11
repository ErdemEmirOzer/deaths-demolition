using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Difficulty;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>WP-104 B3: what one <see cref="ZombieEnemy.ApplyIce"/> call did (S-04 raises the Freeze proc on Frozen).</summary>
    public enum IceResult
    {
        None = 0,
        Slowed = 1,
        Frozen = 2,
        /// <summary>The Ultra at the cap: it never freezes, it slows by FreezeUltraSlow for FreezeSeconds.</summary>
        UltraSlowed = 3
    }

    /// <summary>WP-104 B3: who stunned the body (readout / telemetry; every kind follows the same tier rule).</summary>
    public enum TreeStunKind
    {
        Nuke = 0,
        Bell = 1,
        Bulwark = 2
    }

    /// <summary>
    /// WP-104 B3 · the run tree's status receivers on the body (S-03 side of the B0 contract, S-04 12.10: push model). S-04's
    /// TreeStatusSystem / TreeGravityWells / blast queue decide WHEN; these calls decide what the body does:
    ///
    ///   ApplyIce        ice stacks slow the walk (IceSlowPerStack each, IceSeconds, refreshed per hit); at IceMaxStacks a freeze
    ///                   (FreezeSeconds, Elite × FreezeEliteShare) clears the stacks (S1) — no walk, no swing, the Animator held
    ///                   (Netted visual); the Ultra slows by FreezeUltraSlow instead. FreezeCooldown counts from the hold's end.
    ///   ApplyStun       nuke / bell / bulwark: no walk, no swing (Dazed visual), Elite / Ultra × StunHeavyShare, extends, never
    ///                   stacks (the ApplyShock rule). Never feeds the REQ-124 stagger lock.
    ///   ApplyPull       the black hole drags the body toward its centre (knockback lane, own walk keeps going, the swing is not
    ///                   cancelled — S4); Special / Elite·Ultra feel a share, ramped over 1 m past the core; a pulled body
    ///                   takes half the crowd correction. Kept alive by the wells' 10 Hz refresh.
    ///   ApplyStatusDamage  a DoT tick through the explosion lane with the ground-fire force (no stagger, no shove), past the
    ///                   regional armour (S5).
    ///   SetStatusTint   S-04's burn / chill / venom colour; StatusTintChanged fires on a change only (S-07 binds ZombieSkinFx).
    ///
    /// All state is plain fields of this life: a body that no call reached reads exactly the shipped values (factors 1f, zero
    /// pull, no hold). Death latches <see cref="FrozenAtDeath"/> (Shatter) and reports <see cref="TreeStatusEnded"/> once per life.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        /// <summary>S-04 releases the body's status slot here — once per life, at death or when the pool takes a living body.</summary>
        public static Action<ZombieEnemy> TreeStatusEnded;

        /// <summary>S-07: the body's status tint changed (element, 0..1). None / 0 at death and on the way back to the pool.</summary>
        public static event Action<ZombieEnemy, TreeElement, float> StatusTintChanged;

        public static int FreezesApplied { get; private set; }
        public static int UltraSlowsApplied { get; private set; }
        public static int StunsApplied { get; private set; }
        public static int PullsApplied { get; private set; }
        public static int StatusDamageApplied { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTreeStatusStatics()
        {
            FreezesApplied = 0; UltraSlowsApplied = 0; StunsApplied = 0; PullsApplied = 0; StatusDamageApplied = 0;
        }

        private int _iceStacks;
        private float _iceUntil, _iceSlowPerStack;
        private float _freezeUntil, _freezeReadyAt;
        private float _ultraSlowUntil, _ultraSlowFactor = 1f;
        private float _stunUntil;
        private TreeStunKind _lastStunKind;
        private Vector3 _pullCentre, _treePullVelocity;
        private float _pullSpeed, _pullUntil;
        private TreeElement _tintElement, _lastStatusElement;
        private float _tintIntensity;
        private bool _frozenAtDeath;
        private int _treeStatusEndedLife = -1;

        // ------------------------------------------------------------------ readouts

        public bool IsFrozen => _alive && Time.time < _freezeUntil;
        public bool IsStunned => _alive && Time.time < _stunUntil;
        public bool IsPulled => _alive && Time.time < _pullUntil;
        /// <summary>Frozen or stunned: the body neither walks nor swings (TickCombat's hold, the watchdogs' exemption).</summary>
        public bool TreeHolds => _alive && (Time.time < _freezeUntil || Time.time < _stunUntil);
        public int IceStacks => _alive && Time.time < _iceUntil ? _iceStacks : 0;
        public TreeStunKind LastStunKind => _lastStunKind;
        /// <summary>The pull velocity added this frame (zero when not pulled).</summary>
        public Vector3 TreePullVelocity => _treePullVelocity;
        /// <summary>Latched in Die: the body was frozen when it died (S-04's Shatter / Frozen Gravity read it on the kill).</summary>
        public bool FrozenAtDeath => _frozenAtDeath;
        public TreeElement StatusTintElement => _tintElement;
        public float StatusTintIntensity => _tintIntensity;
        /// <summary>The element of the last status tick that landed (death dressing / telemetry).</summary>
        public TreeElement LastStatusElement => _lastStatusElement;

        /// <summary>The tree's walking factor this frame: the ice stacks, or the Ultra's freeze slow if stronger. Exactly 1f untouched.</summary>
        public float TreeSlowFactor
        {
            get
            {
                if (!_alive) return 1f;
                float now = Time.time;
                float f = now < _iceUntil ? TreeStatusMath.IceSpeedFactor(_iceStacks, _iceSlowPerStack) : 1f;
                if (now < _ultraSlowUntil && _ultraSlowFactor < f) f = _ultraSlowFactor;
                return f;
            }
        }

        /// <summary>
        /// The damage factor of the body's statuses on every incoming hit (S5: they multiply): Deep Freeze's FreezeVulnerability
        /// while frozen × S-04's fire / venom factor (<see cref="TreeZombieBinding.StatusDamageScale"/>, S03-RECEIVER §4.1).
        /// Exactly 1f without the tree. S-04 is only asked when the build has a fire / venom vulnerability at all — its lookup
        /// binds an unknown body (a status slot and the driver), which a Normal run must never do; with both vulnerabilities at 1
        /// its answer is 1 anyway, so the guard changes no number.
        /// </summary>
        public float TreeVulnerabilityScale
        {
            get
            {
                if (!_alive) return 1f;
                ref readonly TreeStats s = ref TreeRuntime.Current;
                float scale = 1f;
                if (Time.time < _freezeUntil && s.FreezeVulnerability > 0f) scale = s.FreezeVulnerability;
                if (s.FireVulnerability != 1f || s.VenomVulnerability != 1f) scale *= TreeZombieBinding.StatusDamageScale(this);
                return scale;
            }
        }

        // ------------------------------------------------------------------ receivers

        /// <summary>
        /// <paramref name="stacks"/> ice stacks from one hit (S-04 passes IceStacksPerHit). Refreshes the stacks' time; at the cap,
        /// with the freeze ready, freezes (Elite shorter) or — the Ultra — slows. A frozen body takes no stacks.
        /// </summary>
        public IceResult ApplyIce(int stacks) => ApplyIce(stacks, false);

        /// <summary>
        /// <see cref="ApplyIce(int)"/>; <paramref name="ignoreCooldown"/> skips the body's FreezeCooldown (S03-RECEIVER §4.4,
        /// S-04 12.10: only the Frozen Gravity synergy — "the pulled crowd freezes"). The tier rules stay: Elite × FreezeEliteShare,
        /// the Ultra slows instead of freezing.
        /// </summary>
        public IceResult ApplyIce(int stacks, bool ignoreCooldown)
        {
            if (!_alive || _dying || stacks <= 0) return IceResult.None;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            if (s.IceMaxStacks <= 0 || s.IceSlowPerStack <= 0f) return IceResult.None;

            using (TreeMarkers.ZombieStatus.Auto())
            {
                float now = Time.time;
                if (now < _freezeUntil) return IceResult.None;

                int current = now < _iceUntil ? _iceStacks : 0;
                _iceStacks = TreeStatusMath.AddStacks(current, stacks, s.IceMaxStacks);
                _iceSlowPerStack = s.IceSlowPerStack;   // the hit's weapon decides the slow; a later weapon swap does not rewrite it
                _iceUntil = now + Mathf.Max(0f, s.IceSeconds);
                if (_iceStacks < s.IceMaxStacks || s.FreezeSeconds <= 0f || !TreeStatusMath.FreezeReady(now, _freezeReadyAt, ignoreCooldown))
                    return IceResult.Slowed;

                if (_tier == EnemyTier.Ultra)
                {
                    _iceStacks = 0; _iceUntil = 0f;   // S1: the cap is spent
                    _ultraSlowFactor = TreeStatusMath.UltraSlowFactor(s.FreezeUltraSlow);
                    _ultraSlowUntil = now + s.FreezeSeconds;
                    _freezeReadyAt = TreeStatusMath.NextFreezeReady(_ultraSlowUntil, s.FreezeCooldown);
                    UltraSlowsApplied++;
                    return IceResult.UltraSlowed;
                }

                float share = TreeStatusMath.FreezeShare(_tier, s.FreezeEliteShare);
                if (share <= 0f) return IceResult.Slowed;   // an immune tier keeps its stacks' slow

                _iceStacks = 0; _iceUntil = 0f;   // S1: the freeze clears the stacks
                _freezeUntil = now + s.FreezeSeconds * share;
                _freezeReadyAt = TreeStatusMath.NextFreezeReady(_freezeUntil, s.FreezeCooldown);
                CancelSwingForTreeHold();
                FreezesApplied++;
                return IceResult.Frozen;
            }
        }

        /// <summary>A stun of <paramref name="seconds"/> (Elite / Ultra × StunHeavyShare). Extends a stun in progress, never stacks.</summary>
        public bool ApplyStun(float seconds, TreeStunKind kind)
        {
            if (!_alive || _dying || seconds <= 0f) return false;
            float share = TreeStatusMath.StunShare(_tier, TreeRuntime.Current.StunHeavyShare);
            if (share <= 0f) return false;

            using (TreeMarkers.ZombieStatus.Auto())
            {
                _stunUntil = TreeStatusMath.Extend(_stunUntil, Time.time, seconds * share);
                _lastStunKind = kind;
                CancelSwingForTreeHold();
                StunsApplied++;
                return true;
            }
        }

        /// <summary>
        /// The black hole at <paramref name="centre"/> pulls this body at <paramref name="speed"/> m/s (S-04 passes HolePullSpeed;
        /// the tier share is applied here) for <paramref name="holdSeconds"/> — the wells refresh it every tick while inside.
        /// </summary>
        public void ApplyPull(Vector3 centre, float speed, float holdSeconds)
        {
            if (!_alive || _dying || speed <= 0f || holdSeconds <= 0f) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float share = TreeStatusMath.PullShare(_tier, s.HolePullSpecialShare, s.HolePullHeavyShare);
            if (share <= 0f) return;

            using (TreeMarkers.ZombieStatus.Auto())
            {
                _pullCentre = centre;
                _pullSpeed = speed * share;
                _pullUntil = Time.time + holdSeconds;
                PullsApplied++;
            }
        }

        /// <summary>
        /// One status damage tick (burn / venom / cold). Uses the caller's transaction when one is open (one call per body per
        /// transaction — the WP-006 guard refuses a second), else its own. Through the explosion lane with the ground-fire force:
        /// the tick never staggers or shoves, passes regional armour (S5; no plate is touched); a kill pays through the normal
        /// Die. The caller's weapon attribution is kept.
        /// </summary>
        public DamageResult ApplyStatusDamage(float amount, TreeElement element)
        {
            if (!_alive || _dying || amount <= 0f) return default;

            using (TreeMarkers.ZombieStatus.Auto())
            {
                bool ownTransaction = DamageTransaction.Current == 0;
                if (ownTransaction) DamageTransaction.Begin();
                bool fromWeapon = DamageHitContext.FromClassicWeapon;
                var weapon = DamageHitContext.Weapon;
                DamageHitContext.Set(1f, 1f, fromWeapon, weapon, null);
                DamageHitContext.MarkGroundFire();
                // S5 (C-00 / S-04 12.10): a status tick passes regional armour. RegionalDamageBody's blast split honours IgnoreArmor
                // only together with GroundFire (S03-RECEIVER §4.3 (a)); this call owns Set … Clear, so the flag never outlives it.
                DamageHitContext.MarkIgnoreArmor();
                DamageResult result;
                try
                {
                    result = ApplyExplosion(amount, transform.position);
                }
                finally
                {
                    DamageHitContext.Clear();
                    if (ownTransaction) DamageTransaction.End();
                }

                if (!DamageResults.IsEmpty(in result))
                {
                    _lastStatusElement = element;
                    StatusDamageApplied++;
                }

                return result;
            }
        }

        /// <summary>S-04's status colour for this body (None / 0 clears). Raises <see cref="StatusTintChanged"/> on a change only.</summary>
        public void SetStatusTint(TreeElement element, float intensity01)
        {
            if (!_alive) return;
            float k = element == TreeElement.None ? 0f : Mathf.Clamp01(intensity01);
            if (k <= 0f) element = TreeElement.None;
            if (element == _tintElement && (k == _tintIntensity || (k > 0f && Mathf.Abs(k - _tintIntensity) < TreeStatusMath.TintEventStep))) return;
            _tintElement = element;
            _tintIntensity = k;
            StatusTintChanged?.Invoke(this, element, k);
        }

        // ------------------------------------------------------------------ body side (TickCombat / lifecycle)

        /// <summary>
        /// ApplyHit, before the plate is asked (S03-RECEIVER §4.2, GD el_venom3 Corrosion): a weapon hit on a corroded body passes
        /// regional armour. Only inside a classic weapon's own DamageHitContext (Set … Clear by the weapon), so the mark is
        /// consumed by this hit and cleared by its owner — a context-free ApplyHit (tests, debug) never leaves it set. S-04 is
        /// only asked when the build corrodes at all (its lookup binds an unknown body).
        /// </summary>
        private void MarkCorrosionIgnoreArmor()
        {
            if (!DamageHitContext.FromClassicWeapon || TreeRuntime.Current.VenomArmorIgnoreStacks <= 0) return;
            if (TreeZombieBinding.IgnoresArmor(this)) DamageHitContext.MarkIgnoreArmor();
        }

        private void CancelSwingForTreeHold()
        {
            if (_attack != null && (_attack.IsAttacking || _attack.IsGrabbing)) _attack.Cancel(GrabEndReason.Staggered);
        }

        /// <summary>TickCombat: the pull for this frame (knockback lane). Zero, and no work, for a body never pulled.</summary>
        private Vector3 TickTreePull(float dt)
        {
            if (_pullUntil <= 0f) return Vector3.zero;
            if (Time.time >= _pullUntil)
            {
                _pullUntil = 0f;
                _treePullVelocity = Vector3.zero;
                return Vector3.zero;
            }

            Vector3 toCentre = _pullCentre - transform.position;
            toCentre.y = 0f;
            _treePullVelocity = TreeStatusMath.PullVelocity(toCentre, _pullSpeed, _bodyRadius + TreeStatusMath.PullCoreMetres, dt);
            return _treePullVelocity;
        }

        /// <summary>
        /// WP-102 × B3: a frozen / stunned body of a pursuit role is not "making no progress" — its PursuitStallSeconds clock
        /// waits out the hold; a pulled body is displaced, so its progress window restarts from where the pull leaves it.
        /// </summary>
        private void PauseTreePursuitClock(bool held, float dt)
        {
            if (_aiRole == ZombieAiRole.Chase) return;
            if (held)
            {
                if (_pursuitProgressAt >= 0f) _pursuitProgressAt += dt;
            }
            else if (IsPulled)
            {
                _pursuitProgressAt = -1f;
            }
        }

        /// <summary>OnSpawned: a new life starts with no status (silent — the previous life already reported its end).</summary>
        private void ResetTreeStatus()
        {
            _iceStacks = 0; _iceUntil = 0f; _iceSlowPerStack = 0f;
            _freezeUntil = 0f; _freezeReadyAt = 0f;
            _ultraSlowUntil = 0f; _ultraSlowFactor = 1f;
            _stunUntil = 0f; _lastStunKind = TreeStunKind.Nuke;
            _pullCentre = Vector3.zero; _treePullVelocity = Vector3.zero; _pullSpeed = 0f; _pullUntil = 0f;
            _tintElement = TreeElement.None; _tintIntensity = 0f; _lastStatusElement = TreeElement.None;
            _frozenAtDeath = false;
            _treeStatusEndedLife = -1;
        }

        /// <summary>
        /// Die, right after <c>_dying = true</c>: latch the frozen state so every death listener (EnemyDied, S-04's OnBodyDied)
        /// reads it. The statuses end at the close of Die (<see cref="EndTreeStatus"/>), after the corpse left the target registry,
        /// the crowd and the hitboxes — S-04's chain / Shatter blasts then never find this body.
        /// </summary>
        private void LatchTreeStatusAtDeath()
        {
            _frozenAtDeath = Time.time < _freezeUntil;
        }

        /// <summary>
        /// The end of Die (IsDying true → S-04 counts a death) / OnDespawned of a living body (IsDying false → a plain release):
        /// drop every status, clear the tint (event) and report the end once per life.
        /// </summary>
        private void EndTreeStatus()
        {
            bool tinted = _tintElement != TreeElement.None;
            _iceStacks = 0; _iceUntil = 0f;
            _freezeUntil = 0f; _ultraSlowUntil = 0f; _ultraSlowFactor = 1f;
            _stunUntil = 0f;
            _treePullVelocity = Vector3.zero; _pullSpeed = 0f; _pullUntil = 0f;
            _tintElement = TreeElement.None; _tintIntensity = 0f;
            if (tinted) StatusTintChanged?.Invoke(this, TreeElement.None, 0f);
            if (_treeStatusEndedLife == _lifeId) return;
            _treeStatusEndedLife = _lifeId;
            TreeStatusEnded?.Invoke(this);
        }
    }
}
