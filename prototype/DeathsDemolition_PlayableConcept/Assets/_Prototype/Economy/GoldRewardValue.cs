using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// What one kill is worth, as three named slots:
    ///
    ///     Total = Base + VariantContribution + ArmorContribution
    ///
    /// WP-004 defined the three slots; WP-008 fills them (DEC-043 / REQ-074): the version
    /// bonus and the SPAWN armour bonus are computed once per enemy life by
    /// <see cref="GoldRewardCalculator"/> and carried in an <see cref="EnemyRewardSnapshot"/>.
    /// This struct is the value contract only; it computes nothing from config itself.
    ///
    /// Slots are ASSIGNED, never accumulated: <see cref="WithVariant"/> replaces the
    /// slot, so the same contribution can never be added twice, and every slot is
    /// clamped at zero so a negative value can never be produced.
    /// </summary>
    public readonly struct GoldRewardValue
    {
        /// <summary>The enemy's base value (PrototypeConfig.GoldReward(tier)).</summary>
        public readonly int Base;

        /// <summary>Contribution of the enemy version (Standard / Special / Elite / Ultra). 0 for Standard.</summary>
        public readonly int VariantContribution;

        /// <summary>Contribution of the armour the enemy wore AT SPAWN. 0 for a bare enemy.</summary>
        public readonly int ArmorContribution;

        public GoldRewardValue(int baseValue, int variantContribution = 0, int armorContribution = 0)
        {
            Base = Mathf.Max(0, baseValue);
            VariantContribution = Mathf.Max(0, variantContribution);
            ArmorContribution = Mathf.Max(0, armorContribution);
        }

        /// <summary>The gold the packet carries. Never negative; saturates instead of wrapping.</summary>
        public int Total => (int)System.Math.Min((long)Base + VariantContribution + ArmorContribution, int.MaxValue);

        /// <summary>True when the kill is worth anything at all.</summary>
        public bool HasValue => Total > 0;

        /// <summary>Same value with the version slot REPLACED (not added to).</summary>
        public GoldRewardValue WithVariant(int contribution) => new GoldRewardValue(Base, contribution, ArmorContribution);

        /// <summary>Same value with the armour slot REPLACED (not added to).</summary>
        public GoldRewardValue WithArmor(int contribution) => new GoldRewardValue(Base, VariantContribution, contribution);

        /// <summary>
        /// Resolution for a death event (WP-008). A producer that took a spawn snapshot
        /// (every <c>ZombieEnemy</c>) is paid EXACTLY that snapshot - nothing is re-read
        /// from the live enemy or the config at death. A foreign producer without a
        /// snapshot falls back to the WP-004 rule: the event's flat gold value, or the
        /// config's unarmoured value for the version, base slot only.
        /// </summary>
        public static GoldRewardValue Resolve(in EnemyRewardSnapshot snapshot, int eventGoldReward, EnemyTier tier, PrototypeConfig config)
        {
            if (snapshot.IsValid)
            {
                return snapshot.Value;
            }

            return Resolve(eventGoldReward, tier, config);
        }

        /// <summary>
        /// The WP-004 fallback: the flat value the death event carries, or the config's
        /// unarmoured value for the version when the producer left it at zero. Base slot
        /// only; no version or armour data exists without a snapshot.
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
