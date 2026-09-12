using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// What one kill is worth, as three named slots:
    ///
    ///     Total = Base + VariantContribution + ArmorContribution
    ///
    /// The long-term value contract (WP-004) lets an enemy's version and its armour state
    /// add to the base value. In M1 only the Standard, unarmoured zombie exists, so both
    /// contributions are ZERO and only <see cref="Base"/> - the prototype's existing
    /// PrototypeOnly value - is used. Nothing here invents a value for Special / Elite /
    /// Ultra or for armour; the slots are the extension point, not a decision.
    ///
    /// Slots are ASSIGNED, never accumulated: <see cref="WithVariant"/> replaces the
    /// slot, so the same contribution can never be added twice, and every slot is
    /// clamped at zero so a negative value can never be produced.
    /// </summary>
    public readonly struct GoldRewardValue
    {
        /// <summary>The enemy's base value (PrototypeConfig.GoldReward(tier)).</summary>
        public readonly int Base;

        /// <summary>Contribution of the enemy version. 0 in M1.</summary>
        public readonly int VariantContribution;

        /// <summary>Contribution of the enemy's armour state. 0 in M1.</summary>
        public readonly int ArmorContribution;

        public GoldRewardValue(int baseValue, int variantContribution = 0, int armorContribution = 0)
        {
            Base = Mathf.Max(0, baseValue);
            VariantContribution = Mathf.Max(0, variantContribution);
            ArmorContribution = Mathf.Max(0, armorContribution);
        }

        /// <summary>The gold the packet carries. Never negative.</summary>
        public int Total => Base + VariantContribution + ArmorContribution;

        /// <summary>True when the kill is worth anything at all.</summary>
        public bool HasValue => Total > 0;

        /// <summary>Same value with the version slot REPLACED (not added to).</summary>
        public GoldRewardValue WithVariant(int contribution) => new GoldRewardValue(Base, contribution, ArmorContribution);

        /// <summary>Same value with the armour slot REPLACED (not added to).</summary>
        public GoldRewardValue WithArmor(int contribution) => new GoldRewardValue(Base, VariantContribution, contribution);

        /// <summary>
        /// The M1 resolution: the base value the death event already carries (read once by
        /// the enemy from PrototypeConfig.GoldReward(tier)), or the config as a fallback
        /// for a producer that left it at zero. Variant and armour slots stay 0 - there is
        /// no data to fill them from in M1 and no approved value to fill them with.
        /// </summary>
        public static GoldRewardValue Resolve(int eventGoldReward, EnemyTier tier, PrototypeConfig config)
        {
            int baseValue = eventGoldReward;
            if (baseValue <= 0 && config != null)
            {
                baseValue = config.GoldReward(tier);
            }

            return new GoldRewardValue(baseValue);
        }
    }
}
