using UnityEngine;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Enemies
{
    /// <summary>What a single hit is worth in reaction terms. Value type, computed per hit.</summary>
    public struct HitReaction
    {
        /// <summary>Additive upper-body flinch, in DamageForce units. Never stops the legs.</summary>
        public float FlinchIntensity;

        /// <summary>True when this hit earned a real, locomotion-suppressing stagger.</summary>
        public bool Stagger;

        public float StaggerDuration;
        public float StaggerIntensity;

        /// <summary>World-space push applied over the stagger. Zero for anything but heavy hits.</summary>
        public Vector3 KnockbackVelocity;

        /// <summary>WP-076: the ≥ KnockbackImpactThreshold gate was earned (susceptibility above the floor); the body applies the class profile.</summary>
        public bool KnockbackEarned;

        /// <summary>Seconds of hit-stop this event suggests. Zero for ordinary bullets.</summary>
        public float SuggestedHitStop;
    }

    /// <summary>
    /// Turns "what just hit me" into "how hard do I react" - REQ-121 through REQ-129.
    ///
    /// The seven inputs the request lists are all here:
    ///   body region        - head hits flinch harder, leg hits rock the hips
    ///   incoming direction - carried straight through to the animator
    ///   weapon power       - DamageHit.ImpactForce, in DamageForce units
    ///   armour state       - armoured hits deflect (less flinch), breaks are an event
    ///   enemy version      - EnemyTierSettings.StaggerSusceptibility
    ///   current action     - a winding-up enemy is easier to interrupt than a walking one
    ///   repeat hits        - an accumulator with a short half-life
    ///
    /// THE ACCUMULATOR IS THE WHOLE DESIGN
    /// A single rifle bullet is 1.0 impact and the stagger threshold is 2.4, so automatic
    /// fire produces flinches and never a stagger (REQ-123). A shotgun lands 8 pellets at
    /// 0.85 inside one frame, sums to 6.8 and staggers hard. The accumulator decays with a
    /// ~0.12 s half-life, so the two shells of a double-tap do not stack into a permanent
    /// hold - and on top of that a cooldown forbids a second stagger for
    /// FeedbackConfigSection.StaggerCooldown seconds, which is the REQ-124 stun-lock lock.
    ///
    /// Every threshold below is PROTOTYPE-ONLY and invented.
    /// </summary>
    public sealed class EnemyHitReactionState
    {
        // ---------------------------------------------------------------- invented tuning

        /// <summary>Accumulated impact needed for a stagger, before susceptibility.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "Stagger gate in DamageForce units. 1 rifle bullet = 1.0 never staggers; 8 pellets " +
            "= 6.8 always does. REQ-123 asks for that split but names no number.", true)]
        public const float StaggerImpactThreshold = 2.4f;

        /// <summary>Accumulated impact needed to actually shove the body backwards.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "Knockback gate in DamageForce units. The request says a shotgun or blast 'can " +
            "create stagger or knockback' and names no number.", true)]
        public const float KnockbackImpactThreshold = 4.5f;

        /// <summary>Half-life of the impact accumulator, in seconds.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "How fast repeat hits stop adding up. Short enough that a double-tap cannot hold " +
            "an enemy down. Invented.", true)]
        private const float ImpactHalfLife = 0.12f;

        /// <summary>Half-life of the repeat-hit counter, in seconds.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "How fast the diminishing-returns counter for flinch decays. Invented.", true)]
        private const float RepeatHalfLife = 0.35f;

        /// <summary>Flinch falls off as hits pile up, so an SMG does not vibrate the torso.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "Strength of the diminishing returns on rapid repeat flinches. Invented.", true)]
        private const float RepeatFlinchFalloff = 0.22f;

        /// <summary>Single-hit impact above which a hit counts as "heavy" for hit-stop.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "Single-hit impact that earns hit-stop. REQ-127 says 'only strong impacts' and " +
            "names no number.", true)]
        private const float HeavyImpactThreshold = 2.5f;

        /// <summary>Metres per second of shove at exactly KnockbackImpactThreshold.</summary>
        [PrototypeOnly(PendingDecisions.None, "Knockback speed per excess impact unit. Invented.", true)]
        private const float KnockbackSpeedPerUnit = 0.9f;

        [PrototypeOnly(PendingDecisions.None,
            "Ceiling on knockback speed so nothing is launched off the ring route. Invented.", true)]
        private const float MaxKnockbackSpeed = 4.5f;

        // ------------------------------------------------------- flinch shaping (invented)

        [PrototypeOnly(PendingDecisions.None,
            "Flinch multiplier when the plate ate the hit. This is what makes 'armour hit' read " +
            "differently from 'flesh hit' before any sound plays (REQ-125).", true)]
        private const float ArmoredFlinchScale = 0.45f;

        [PrototypeOnly(PendingDecisions.None, "Flinch multiplier for a head hit.", true)]
        private const float HeadFlinchScale = 1.35f;

        [PrototypeOnly(PendingDecisions.None, "Flinch multiplier for a lower-body hit.", true)]
        private const float LowerBodyFlinchScale = 0.8f;

        [PrototypeOnly(PendingDecisions.None,
            "Flinch left to a tier with StaggerSusceptibility 0 - a heavy body still shows " +
            "something. Invented.", true)]
        private const float HeavyBodyFlinchFloor = 0.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Minimum flinch an armour break produces, whatever the bullet was. REQ-125 puts " +
            "armour break above armour hit; the number is invented.", true)]
        private const float ArmorBreakFlinchFloor = 1.15f;

        // ------------------------------------------------------ stagger shaping (invented)

        [PrototypeOnly(PendingDecisions.None,
            "Stagger score bonus when the enemy is caught mid-wind-up. This is what makes " +
            "shotgunning a winding-up Guard an answer rather than a coincidence.", true)]
        private const float TelegraphStaggerBonus = 1.35f;

        [PrototypeOnly(PendingDecisions.None,
            "Stagger score bonus when the enemy is running - it has momentum to lose.", true)]
        private const float RunningStaggerBonus = 1.15f;

        [PrototypeOnly(PendingDecisions.None,
            "An armour break is forced just over the stagger gate, so breaking a plate always " +
            "staggers. Invented.", true)]
        private const float ArmorBreakStaggerFloor = 1.05f;

        [PrototypeOnly(PendingDecisions.None, "Lower bound of stagger intensity. Invented.", true)]
        private const float MinStaggerIntensity = 0.6f;

        [PrototypeOnly(PendingDecisions.None, "Upper bound of stagger intensity. Invented.", true)]
        private const float MaxStaggerIntensity = 2.4f;

        [PrototypeOnly(PendingDecisions.None,
            "Seconds of stagger at exactly the threshold, before the per-overshoot term.", true)]
        private const float StaggerDurationBase = 0.16f;

        [PrototypeOnly(PendingDecisions.None,
            "Extra stagger seconds per unit of overshoot past the threshold.", true)]
        private const float StaggerDurationPerOvershoot = 0.14f;

        [PrototypeOnly(PendingDecisions.None,
            "Below this StaggerSusceptibility a tier is never physically shoved - the " +
            "Executioner keeps its footing. Invented.", true)]
        private const float KnockbackSusceptibilityFloor = 0.2f;

        // --------------------------------------- fallbacks used before ApplyConfig arrives
        // Each one mirrors the FeedbackConfigSection default EXACTLY. They only ever apply
        // when no PrototypeConfig is supplied (headless test, scene opened without Boot);
        // a silent disagreement here would move the feel without moving the config.

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for FeedbackConfigSection.StaggerCooldown (1.1 s). REQ-124 stun-lock lock.", true)]
        private const float DefaultStaggerCooldown = 1.1f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for FeedbackConfigSection.MaxStaggerDuration (0.45 s).", true)]
        private const float DefaultMaxStaggerDuration = 0.45f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for FeedbackConfigSection.HitStopArmorBreak (0.045 s).", true)]
        private const float DefaultHitStopArmorBreak = 0.045f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for FeedbackConfigSection.HitStopHeavyImpact (0.035 s).", true)]
        private const float DefaultHitStopHeavyImpact = 0.035f;

        [PrototypeOnly(PendingDecisions.None,
            "Fallback for FeedbackConfigSection.HitStopHeavyDeath (0.07 s).", true)]
        private const float DefaultHitStopHeavyDeath = 0.07f;

        // ------------------------------------------------------------------------ state

        private float _impactAccumulator;
        private float _repeatCounter;

        /// <summary>
        /// WP-076 B3: the shell's total, NOT spent by the stagger. Pellets are evaluated one by one, so the stagger fires
        /// on the 3rd (2.55) and the ≥ 4.5 shove gate was never reached by a shotgun; this charge lets the rest of the
        /// same shell earn it (one shove per shell).
        /// </summary>
        private float _shoveCharge;

        [PrototypeOnly(PendingDecisions.None, "WP-076: seconds after a stagger in which the rest of the same volley can still earn the shove.", true)]
        private const float ShoveWindowSeconds = 0.1f;

        /// <summary>The volley's accumulated impact (decays with the same half-life; the death impulse reads it for a shell).</summary>
        public float ShoveCharge => _shoveCharge;
        private float _staggerCooldownRemaining;

        // ----------------------------------------------------------- cached config values

        private float _susceptibility = 1f;
        private float _staggerCooldown = DefaultStaggerCooldown;
        private float _maxStaggerDuration = DefaultMaxStaggerDuration;
        private float _hitStopArmorBreak = DefaultHitStopArmorBreak;
        private float _hitStopHeavyImpact = DefaultHitStopHeavyImpact;
        private float _hitStopHeavyDeath = DefaultHitStopHeavyDeath;

        /// <summary>Current accumulated impact. Exposed for the debug readout.</summary>
        public float ImpactCharge => _impactAccumulator;

        public bool StaggerOnCooldown => _staggerCooldownRemaining > 0f;

        /// <summary>DEC-112: a stagger that did not come through <see cref="Evaluate"/> (the tree stagger) still starts the REQ-124 lock.</summary>
        public void NoteExternalStagger() => _staggerCooldownRemaining = _staggerCooldown;

        public void ApplyConfig(PrototypeConfig config, EnemyTier tier)
        {
            if (config == null)
            {
                return;
            }

            EnemyTierSettings settings = config.Enemies.Find(tier);
            _susceptibility = settings != null ? Mathf.Clamp01(settings.StaggerSusceptibility) : 1f;

            FeedbackConfigSection feedback = config.Feedback;
            _staggerCooldown = Mathf.Max(0f, feedback.StaggerCooldown);
            _maxStaggerDuration = Mathf.Max(0.05f, feedback.MaxStaggerDuration);
            _hitStopArmorBreak = Mathf.Max(0f, feedback.HitStopArmorBreak);
            _hitStopHeavyImpact = Mathf.Max(0f, feedback.HitStopHeavyImpact);
            _hitStopHeavyDeath = Mathf.Max(0f, feedback.HitStopHeavyDeath);
        }

        public void ResetForSpawn()
        {
            _impactAccumulator = 0f;
            _repeatCounter = 0f;
            _staggerCooldownRemaining = 0f;
            _shoveCharge = 0f;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            _impactAccumulator *= Mathf.Pow(0.5f, dt / ImpactHalfLife);
            _shoveCharge *= Mathf.Pow(0.5f, dt / ImpactHalfLife);
            if (_shoveCharge < 0.001f) _shoveCharge = 0f;
            _repeatCounter *= Mathf.Pow(0.5f, dt / RepeatHalfLife);

            if (_impactAccumulator < 0.001f)
            {
                _impactAccumulator = 0f;
            }

            if (_staggerCooldownRemaining > 0f)
            {
                _staggerCooldownRemaining -= dt;
            }
        }

        /// <summary>
        /// Evaluate one landed hit.
        /// </summary>
        /// <param name="isTelegraphing">True while the enemy is in an attack wind-up.</param>
        /// <param name="isRunning">True while the enemy is in its run pose.</param>
        public HitReaction Evaluate(
            in DamageHit hit,
            in DamageResult result,
            bool isTelegraphing,
            bool isRunning,
            bool wasKilled,
            bool isHeavyTier)
        {
            var reaction = new HitReaction();

            float impact = Mathf.Max(0f, hit.ImpactForce);
            _impactAccumulator += impact;
            _shoveCharge += impact;
            _repeatCounter += 1f;

            // ---------------------------------------------------------------- flinch
            float flinch = impact;

            // Armour deflects: the plate takes the energy, the body barely moves. This is
            // what makes "armour hit" feel different from "flesh hit" before any sound
            // plays (REQ-125).
            if (result.WasArmored)
            {
                flinch *= ArmoredFlinchScale;
            }

            // Region weighting.
            switch (result.Region)
            {
                case BodyRegion.Head: flinch *= HeadFlinchScale; break;
                case BodyRegion.LowerBody: flinch *= LowerBodyFlinchScale; break;
            }

            // Diminishing returns on rapid repeats.
            flinch *= 1f / (1f + _repeatCounter * RepeatFlinchFalloff);

            // Heavier enemies absorb more without visibly moving.
            flinch *= Mathf.Lerp(HeavyBodyFlinchFloor, 1f, _susceptibility);

            // An armour break always reads loudly, whatever the bullet was.
            if (result.ArmorBroke)
            {
                flinch = Mathf.Max(flinch, ArmorBreakFlinchFloor);
            }

            reaction.FlinchIntensity = flinch;

            // --------------------------------------------------------------- stagger
            if (!wasKilled && _staggerCooldownRemaining <= 0f)
            {
                float score = _impactAccumulator * _susceptibility;

                // Context: an enemy caught mid-wind-up is easier to break, and one at a
                // run has momentum to lose. Walking is the baseline.
                if (isTelegraphing) score *= TelegraphStaggerBonus;
                else if (isRunning) score *= RunningStaggerBonus;

                // Breaking a plate is a stagger-worthy event on its own.
                if (result.ArmorBroke)
                {
                    score = Mathf.Max(score, StaggerImpactThreshold * ArmorBreakStaggerFloor);
                }

                if (score >= StaggerImpactThreshold)
                {
                    float over = score / StaggerImpactThreshold;
                    reaction.Stagger = true;
                    reaction.StaggerIntensity = Mathf.Clamp(over, MinStaggerIntensity, MaxStaggerIntensity);
                    reaction.StaggerDuration = Mathf.Min(_maxStaggerDuration,
                        StaggerDurationBase + StaggerDurationPerOvershoot * over);
                    _staggerCooldownRemaining = _staggerCooldown;

                    // The accumulator is spent by the stagger, so the next shell has to
                    // earn its own stagger instead of riding this one.
                    _impactAccumulator = 0f;

                    // ------------------------------------------------------ knockback
                    if (_susceptibility > KnockbackSusceptibilityFloor
                        && over * StaggerImpactThreshold >= KnockbackImpactThreshold)
                    {
                        float excess = over * StaggerImpactThreshold - KnockbackImpactThreshold;
                        float speed = Mathf.Min(MaxKnockbackSpeed,
                            (1f + excess) * KnockbackSpeedPerUnit * _susceptibility);

                        Vector3 push = hit.Direction;
                        push.y = 0f;
                        if (push.sqrMagnitude > 0.0001f)
                        {
                            reaction.KnockbackVelocity = push.normalized * speed;
                        }
                    }
                }
            }

            // ------------------------------------------------ WP-076 B3 volley shove gate
            // The shove belongs to the stagger that just started (this hit or ≤ ShoveWindowSeconds ago) and to the
            // whole volley's weight: 8 × 0.85 = 6.8 ≥ 4.5 for a Standard, 6.8 × 0.45 = 3.06 for an Elite (never).
            if (!wasKilled && _susceptibility > KnockbackSusceptibilityFloor
                && _staggerCooldownRemaining > _staggerCooldown - ShoveWindowSeconds
                && _shoveCharge * _susceptibility >= KnockbackImpactThreshold)
            {
                reaction.KnockbackEarned = true;
                _shoveCharge = 0f;
            }

            // -------------------------------------------------------------- hit stop
            // REQ-127: never on an ordinary automatic bullet. Only these three cases.
            if (wasKilled && isHeavyTier)
            {
                reaction.SuggestedHitStop = _hitStopHeavyDeath;
            }
            else if (result.ArmorBroke)
            {
                reaction.SuggestedHitStop = _hitStopArmorBreak;
            }
            else if (impact >= HeavyImpactThreshold)
            {
                reaction.SuggestedHitStop = _hitStopHeavyImpact;
            }

            return reaction;
        }
    }
}
