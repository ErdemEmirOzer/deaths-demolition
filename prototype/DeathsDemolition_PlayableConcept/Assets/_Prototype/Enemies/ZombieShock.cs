using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 B4 · Electrocuted (agreed with S-02, WP-069 DIRECTION §7a): the chain's shock holds the body for the seconds
    /// S-02 passes (data: `shockSeconds` 1.2; the Ultra 2.5) — no walk, no swing, grab / slot released; hitboxes stay on.
    /// A repeat of the same transaction is ignored; a new shot extends the hold (never stacks). S-02 lands the lethal chain
    /// damage at the END of the tremble, so death, score and gold count then (Die() as always) and the fall starts at
    /// once with State = Electrocuted (S-06 death_arc_electro). A lethal chain hit that arrives while the tremble still
    /// has time left holds the fall for that remainder (the reward is paid when the damage lands). The shock never
    /// feeds the REQ-124 stagger lock.
    /// </summary>
    public sealed partial class ZombieEnemy : IChainTarget
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076: seconds after the tremble in which a lethal hit still dies Electrocuted (S-02 lands it at the tremble's end); cap on a held fall.", true)]
        public const float ShockDeathGraceSeconds = 0.15f, ShockDeathHoldMaxSeconds = 2.5f;

        private float _shockUntil;
        private int _lastShockTransaction;
        private float _deathHoldRemaining;
        private bool _deathHeld;

        public bool IsElectrocuted => _alive && Time.time < _shockUntil;
        /// <summary>Seconds the lethal-shock fall is still held (0 = none / already handed to the driver).</summary>
        public float ShockDeathHoldRemaining => _deathHeld ? _deathHoldRemaining : 0f;
        public static int ShocksApplied { get; private set; }

        bool IChainTarget.CanChain => _alive && !_dying && !IsRising && _gel == GelState.None;

        /// <summary>S-02's chain calls this for every body in the chain with the weapon's shock seconds.</summary>
        public bool ApplyShock(int transactionId, float seconds)
        {
            if (!_alive || _dying || seconds <= 0f) return false;
            if (transactionId != 0 && transactionId == _lastShockTransaction) return false;
            _lastShockTransaction = transactionId;
            Vector3 direction = -transform.forward;
            _shockUntil = Mathf.Max(_shockUntil, Time.time + seconds);
            if (_attack.IsAttacking || _attack.IsGrabbing) _attack.Cancel(GrabEndReason.Staggered);
            ShocksApplied++;
            RaiseBeat(EnemyCombatEventKind.SpecialShock, transform.position + Vector3.up * ChainTargetResolver.ChestHeight, direction, ArmorSlot.None, _animator.Driven, seconds);
            return true;
        }

        /// <summary>Die(): a chain kill / a kill while (or just after) shocked dies Electrocuted; returns true when the fall is held for the tremble's remainder.</summary>
        private bool BeginShockDeathHold(in DamageHit hit)
        {
            bool shocked = hit.Source == DamageSourceKind.Chain || Time.time < _shockUntil + ShockDeathGraceSeconds;
            if (!shocked) return false;
            _bodyStateOverride = ZombieBodyState.Electrocuted;
            float remaining = Mathf.Min(ShockDeathHoldMaxSeconds, _shockUntil - Time.time);
            if (remaining <= 0f) return false;   // the tremble is over: the fall starts now, State = Electrocuted
            _deathHeld = true;
            _deathHoldRemaining = remaining;
            return true;
        }

        /// <summary>TickDying: count the held fall down, then hand the stored impulse to the driver. True while holding.</summary>
        private bool TickShockDeathHold(float dt)
        {
            if (!_deathHeld) return false;
            _deathHoldRemaining -= dt;
            _animator.Tick(dt);
            if (_deathHoldRemaining > 0f) return true;
            _deathHeld = false;
            SetSpecialVisual(ZombieSpecialVisual.None);
            _animator.SetDeathImpulse(LastDeathImpulse);
            _animator.BeginDeath(LastDeathImpulse.Direction, LastDeathImpulse.Force, LastDeathImpulse.Region, _tier == EnemyTier.Elite || _tier == EnemyTier.Ultra, transform, ref _random);
            return false;
        }

        private void ResetShock()
        {
            _shockUntil = 0f; _lastShockTransaction = 0; _deathHeld = false; _deathHoldRemaining = 0f;
        }

        public static void ResetShockStatics() => ShocksApplied = 0;
    }
}
