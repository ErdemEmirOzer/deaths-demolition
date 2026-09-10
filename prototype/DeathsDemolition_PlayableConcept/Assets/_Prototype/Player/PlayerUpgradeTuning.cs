using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// How an upgrade tier turns into a PLAYER-side number.
    ///
    /// The split is the same one <see cref="DD.Prototype.Weapons.WeaponUpgradeTuning"/>
    /// draws: the central <see cref="PrototypeConfig"/> owns the economy of a branch -
    /// cost, max tier, offer weight - because that is the shop's business, and what a tier
    /// actually *does* belongs to the system that reads it. Four branches are read here:
    ///
    ///     MovementSpeed     -> PlayerMotor, top speed
    ///     MaximumHealth     -> PlayerHealth, maximum
    ///     RegenerationDelay -> PlayerHealth, seconds of quiet before regen starts
    ///     RegenerationRate  -> PlayerHealth, HP per second once it does
    ///
    /// EVERY VALUE IS A MULTIPLIER, NEVER AN ABSOLUTE. A tier must not know what the base
    /// speed or the base health is: dragging PlayerConfigSection.MoveSpeed in the F1 panel
    /// has to keep a 3-tier build 3 tiers better, not silently richer or poorer.
    ///
    /// It is a ScriptableObject with a code default, so it works with or without an
    /// authored asset: drop a <c>PlayerUpgradeTuning</c> into any Resources folder under
    /// the name in <see cref="ResourcesPath"/> and it takes over; do nothing and the
    /// defaults below are used. No scene reference, no missing reference.
    ///
    /// WHERE THE DEFAULT MAGNITUDES COME FROM
    /// --------------------------------------
    /// The request names the six utility cards but gives no numbers. Each default below is
    /// the economy table's own <c>UpgradeBranchSettings.EffectPerTier</c> re-expressed as a
    /// fraction of the shipped base value, so the card magnitudes agree with the numbers a
    /// designer already wrote next to the prices instead of contradicting them:
    ///
    ///     MovementSpeed      0.06 of base            -> 6 % per tier
    ///     MaximumHealth      15 HP of 100 base HP    -> 15 % per tier
    ///     RegenerationDelay  0.5 s of 4.5 s base     -> 11 % off per tier
    ///     RegenerationRate   3 HP/s of 12 HP/s base  -> 25 % per tier
    ///
    /// They are still INVENTED as multipliers and every one of them is marked as such.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerUpgradeTuning", menuName = "Deaths Demolition/Player Upgrade Tuning", order = 11)]
    public sealed class PlayerUpgradeTuning : ScriptableObject
    {
        public const string ResourcesPath = "PlayerUpgradeTuning";

        private static PlayerUpgradeTuning s_active;

        public static PlayerUpgradeTuning Active
        {
            get
            {
                if (s_active != null)
                {
                    return s_active;
                }

                s_active = Resources.Load<PlayerUpgradeTuning>(ResourcesPath);

                if (s_active == null)
                {
                    s_active = CreateInstance<PlayerUpgradeTuning>();
                    s_active.name = "PlayerUpgradeTuning (code defaults)";
                    s_active.hideFlags = HideFlags.HideAndDontSave;
                }

                return s_active;
            }
        }

        // ------------------------------------------------------------------ Movement Speed

        [Header("Movement Speed - PlayerMotor top speed")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Top speed added per tier, as a fraction of PlayerConfigSection.MoveSpeed. " +
            "Matches the economy table's EffectPerTier of 0.06 for this branch.", true)]
        [Range(0f, 0.25f)]
        public float MoveSpeedBonusPerTier = 0.06f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Ceiling on the total movement multiplier. Stops a re-tuned max tier from " +
            "outrunning the camera frame and the enemy chase speed.", true)]
        public float MaximumMoveSpeedMultiplier = 1.4f;

        // ------------------------------------------------------------------ Maximum Health

        [Header("Maximum Health - PlayerHealth ceiling")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Maximum health added per tier, as a fraction of PlayerConfigSection.MaxHealth. " +
            "15 % is the economy table's 15 HP against the shipped 100 HP base.", true)]
        [Range(0f, 0.5f)]
        public float MaxHealthBonusPerTier = 0.15f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Ceiling on the total maximum-health multiplier.", true)]
        public float MaximumHealthMultiplier = 2.5f;

        // ------------------------------------------------------------------ Regeneration Delay

        [Header("Regeneration Delay - seconds of quiet before regen starts")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Delay removed per tier, as a fraction of PlayerConfigSection.HealthRegenDelay. " +
            "11 % is the economy table's 0.5 s against the request's 4.5 s.", true)]
        [Range(0f, 0.5f)]
        public float RegenDelayReductionPerTier = 0.11f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Floor on the delay after all tiers, as a fraction of base. A build must never " +
            "reach instant regeneration, which would erase the cost of being hit.", true)]
        [Range(0.1f, 1f)]
        public float MinimumRegenDelayFraction = 0.45f;

        // ------------------------------------------------------------------ Regeneration Rate

        [Header("Regeneration Rate - HP per second once regen starts")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Regeneration speed added per tier, as a fraction of PlayerConfigSection.HealthRegenRate. " +
            "25 % is the economy table's 3 HP/s against the request's 12 HP/s.", true)]
        [Range(0f, 0.75f)]
        public float RegenRateBonusPerTier = 0.25f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Ceiling on the total regeneration-rate multiplier.", true)]
        public float MaximumRegenRateMultiplier = 3f;

        // ------------------------------------------------------------------ resolved effects
        // Tier -> multiplier. Nothing outside this class does the arithmetic, so the card
        // text and the live player can never disagree: both call these.

        /// <summary>Multiplier on PlayerConfigSection.MoveSpeed. 1 with no tiers.</summary>
        public float MoveSpeedMultiplier(int tier)
        {
            float value = 1f + MoveSpeedBonusPerTier * Mathf.Max(0, tier);
            return Mathf.Min(value, Mathf.Max(1f, MaximumMoveSpeedMultiplier));
        }

        /// <summary>Multiplier on PlayerConfigSection.MaxHealth. 1 with no tiers.</summary>
        public float MaxHealthMultiplier(int tier)
        {
            float value = 1f + MaxHealthBonusPerTier * Mathf.Max(0, tier);
            return Mathf.Min(value, Mathf.Max(1f, MaximumHealthMultiplier));
        }

        /// <summary>Multiplier on PlayerConfigSection.HealthRegenDelay. Below 1 once bought.</summary>
        public float RegenDelayMultiplier(int tier)
        {
            float value = 1f - RegenDelayReductionPerTier * Mathf.Max(0, tier);
            return Mathf.Max(Mathf.Clamp01(MinimumRegenDelayFraction), value);
        }

        /// <summary>Multiplier on PlayerConfigSection.HealthRegenRate. 1 with no tiers.</summary>
        public float RegenRateMultiplier(int tier)
        {
            float value = 1f + RegenRateBonusPerTier * Mathf.Max(0, tier);
            return Mathf.Min(value, Mathf.Max(1f, MaximumRegenRateMultiplier));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
        }
    }
}
