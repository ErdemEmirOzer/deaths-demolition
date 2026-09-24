using System;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>WP-076 B5: the gel state machine of one body.</summary>
    public enum GelState
    {
        None = 0,
        Sliding = 1,
        Falling = 2,
        Downed = 3,
        GettingUp = 4
    }

    /// <summary>
    /// WP-076 B5 · the gel Special's AI side, agreed with S-02 (WP-069 DIRECTION §7b): a body that steps into a puddle
    /// (<see cref="GelPuddleRegistry.TryFind"/>, every 2nd frame) at ≥ <see cref="SlideEnterSpeed"/> loses traction and
    /// slides — deterministic, no avoidance (DEC-096): direction and speed kept, <see cref="SlideDeceleration"/> only,
    /// Elite / Ultra ×<see cref="HeavyGelSpeedScale"/>, no steering, attack cancelled + slot released. After
    /// <see cref="GelPuddleRegistry.SlipFallSeconds"/> the fall lands: sw_fall beat and S-02's outcome through
    /// <see cref="SlipOutcomeHandler"/> (the slip death / the Executioner's 20 % — S-02's damage, reward attributed to
    /// the gel). A survivor lies Downed, then GettingUp; S-02 may make it immune (<see cref="SetGelImmunity"/>). A direct
    /// hit's knockdown (<see cref="ApplyKnockdown"/>) runs the same Falling → Downed → GettingUp. A shock holds the slide
    /// still. The body neither walks nor swings in any gel state; hitboxes stay on.
    /// </summary>
    public sealed partial class ZombieEnemy
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §4.2: slide entry speed (m/s), slide deceleration (m/s²), Elite/Ultra speed scale in gel, knockdown fall (s), get-up seconds (light / heavy), a slip survivor's downed seconds by tier.", true)]
        public const float SlideEnterSpeed = 0.6f, SlideDeceleration = 0.15f, HeavyGelSpeedScale = 0.6f, KnockdownFallSeconds = 0.4f,
                           GetUpSecondsLight = 0.9f, GetUpSecondsHeavy = 1.2f;
        public static readonly float[] SlipDownedSeconds = { 1.6f, 1.2f, 2.2f, 2.8f };

        /// <summary>S-02 sets this to its SpecialGelEffects.ApplySlipOutcome (body, puddle id); null = the body survives and lies Downed.</summary>
        public static Action<ZombieEnemy, int> SlipOutcomeHandler;

        /// <summary>
        /// S-02 (WP-069 × WP-077 link.gel.slickfloor): the scale on every damage this body takes right now — ApplyHit and
        /// ApplyExplosion multiply by it (null = 1). S-02's SpecialLinks.SlickScale answers 1.15 for a body in gel.
        /// </summary>
        public static Func<ZombieEnemy, float> IncomingDamageScale;

        public static int SlidesStarted { get; private set; }
        public static int SlipFalls { get; private set; }
        public static int Knockdowns { get; private set; }

        private GelState _gel;
        private float _gelTimer, _downedSeconds, _gelImmuneUntil, _pendingGelImmunity;
        private Vector3 _slideVelocity;
        private int _slidePuddle, _lastKnockdownTransaction;

        public GelState Gel => _gel;
        public bool IsGelImmune => Time.time < _gelImmuneUntil;

        /// <summary>
        /// S-02 (§7b): the Executioner is immune to slipping for <paramref name="seconds"/> once it is back on its feet —
        /// called during the fall (the outcome), the immunity starts when GettingUp ends; called on a standing body, now.
        /// </summary>
        public void SetGelImmunity(float seconds)
        {
            if (!_alive) return;
            seconds = Mathf.Max(0f, seconds);
            if (_gel != GelState.None) _pendingGelImmunity = Mathf.Max(_pendingGelImmunity, seconds);
            else _gelImmuneUntil = Mathf.Max(_gelImmuneUntil, Time.time + seconds);
        }

        /// <summary>S-02's direct gel hit topples the body (same pattern as ApplyShock: the same transaction once, a new hit extends).</summary>
        public bool ApplyKnockdown(int transactionId, float seconds)
        {
            if (!_alive || _dying || seconds <= 0f) return false;
            if (transactionId != 0 && transactionId == _lastKnockdownTransaction) return false;
            _lastKnockdownTransaction = transactionId;
            Knockdowns++;
            if (_gel == GelState.Downed || _gel == GelState.Falling)
            {
                _downedSeconds = Mathf.Max(_downedSeconds, seconds - KnockdownFallSeconds);
                if (_gel == GelState.Downed) _gelTimer = Mathf.Min(_gelTimer, 0f);
                return true;
            }
            EnterFalling(Mathf.Max(0f, seconds - KnockdownFallSeconds), false);
            return true;
        }

        /// <summary>Movement this frame from the gel state (the slide); <paramref name="holds"/> = no walk / no swing.</summary>
        private Vector3 TickGel(float dt, out bool holds)
        {
            holds = _gel != GelState.None;
            switch (_gel)
            {
                case GelState.None:
                    if (!IsGelImmune && !IsElectrocuted && ((Time.frameCount + _lifeId) & 1) == 0 && _currentSpeed >= SlideEnterSpeed
                        && GelPuddleRegistry.TryFind(transform.position, out int puddle))
                        BeginSlide(puddle);
                    holds = _gel != GelState.None;
                    return Vector3.zero;

                case GelState.Sliding:
                    _gelTimer += dt;
                    if (_gelTimer >= GelPuddleRegistry.SlipFallSeconds) { LandSlipFall(); return Vector3.zero; }
                    if (IsElectrocuted) return Vector3.zero;   // the shock holds the slide still
                    float speed = _slideVelocity.magnitude;
                    if (speed > 1e-4f) _slideVelocity *= Mathf.Max(0f, speed - SlideDeceleration * dt) / speed;
                    return _slideVelocity;

                case GelState.Falling:
                    _gelTimer += dt;
                    if (_gelTimer >= KnockdownFallSeconds) { _gel = GelState.Downed; _gelTimer = 0f; }
                    return Vector3.zero;

                case GelState.Downed:
                    _gelTimer += dt;
                    if (_gelTimer >= _downedSeconds) { _gel = GelState.GettingUp; _gelTimer = 0f; }
                    return Vector3.zero;

                case GelState.GettingUp:
                    _gelTimer += dt;
                    if (_gelTimer >= (_tier == EnemyTier.Elite || _tier == EnemyTier.Ultra ? GetUpSecondsHeavy : GetUpSecondsLight))
                    {
                        _gel = GelState.None; _gelTimer = 0f;
                        if (_pendingGelImmunity > 0f) { _gelImmuneUntil = Time.time + _pendingGelImmunity; _pendingGelImmunity = 0f; }
                        SetSpecialVisual(ZombieSpecialVisual.None);
                    }
                    return Vector3.zero;
            }
            return Vector3.zero;
        }

        private void BeginSlide(int puddle)
        {
            Vector3 v = _plannedVelocity; v.y = 0f;
            if (v.sqrMagnitude < SlideEnterSpeed * SlideEnterSpeed) v = FlatForward(transform) * Mathf.Max(SlideEnterSpeed, _currentSpeed);
            if (_tier == EnemyTier.Elite || _tier == EnemyTier.Ultra) v *= HeavyGelSpeedScale;
            _slideVelocity = v;
            _slidePuddle = puddle;
            _gel = GelState.Sliding; _gelTimer = 0f;
            if (_attack.IsAttacking || _attack.IsGrabbing) _attack.Cancel(GrabEndReason.Staggered);
            SlidesStarted++;
            if (!IsElectrocuted) SetSpecialVisual(ZombieSpecialVisual.Slipping);   // S-06 WP-071 B3
            RaiseBeat(EnemyCombatEventKind.SpecialSlide, transform.position, v.sqrMagnitude > 1e-6f ? v.normalized : FlatForward(transform), ArmorSlot.None, _animator.Driven, GelPuddleRegistry.SlipFallSeconds);
        }

        private void LandSlipFall()
        {
            SlipFalls++;
            int puddle = _slidePuddle;
            EnterFalling(SlipDownedSeconds[Mathf.Clamp((int)_tier, 0, SlipDownedSeconds.Length - 1)], true);
            try { SlipOutcomeHandler?.Invoke(this, puddle); }   // S-02: the slip damage (death / 20 %), reward to the gel, immunity
            catch (Exception e) { Debug.LogException(e); }
        }

        private void EnterFalling(float downedSeconds, bool fromSlip)
        {
            _gel = GelState.Falling; _gelTimer = 0f;
            _downedSeconds = downedSeconds;
            _slideVelocity = Vector3.zero;
            if (_attack.IsAttacking || _attack.IsGrabbing) _attack.Cancel(GrabEndReason.Staggered);
            if (!IsElectrocuted) SetSpecialVisual(ZombieSpecialVisual.Slipping);   // falling / downed: Slipping until S-06's Downed visual
            RaiseBeat(EnemyCombatEventKind.SpecialFall, transform.position, FlatForward(transform), ArmorSlot.None, _animator.Driven, KnockdownFallSeconds + downedSeconds);
        }

        private void ResetGel()
        {
            _gel = GelState.None; _gelTimer = 0f; _downedSeconds = 0f; _gelImmuneUntil = 0f; _pendingGelImmunity = 0f;
            _slideVelocity = Vector3.zero; _slidePuddle = 0; _lastKnockdownTransaction = 0;
        }

        public static void ResetGelStatics() { SlidesStarted = 0; SlipFalls = 0; Knockdowns = 0; }
    }
}
