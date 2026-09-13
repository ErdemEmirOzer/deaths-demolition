using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// Which armour pieces an enemy was wearing WHEN IT SPAWNED. Three bits, never
    /// updated: what breaks during the fight is the armour system's business
    /// (<c>ArmorSet</c>), not the reward's (DEC-043 / REQ-074, WP-008).
    /// </summary>
    public readonly struct SpawnArmorComposition
    {
        public readonly bool Helmet;
        public readonly bool Body;
        public readonly bool Leg;

        public SpawnArmorComposition(bool helmet, bool body, bool leg)
        {
            Helmet = helmet;
            Body = body;
            Leg = leg;
        }

        public static SpawnArmorComposition None => default;

        public int PieceCount => (Helmet ? 1 : 0) + (Body ? 1 : 0) + (Leg ? 1 : 0);

        public bool Has(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return Helmet;
                case ArmorSlot.Body: return Body;
                case ArmorSlot.Leg: return Leg;
                default: return false;
            }
        }

        /// <summary>"H+B+L" style readout for probes and the F1 panel. Allocates; not for a hot path.</summary>
        public string Describe()
        {
            if (PieceCount == 0)
            {
                return "none";
            }

            var sb = new System.Text.StringBuilder(8);
            if (Helmet) sb.Append('H');
            if (Body) { if (sb.Length > 0) sb.Append('+'); sb.Append('B'); }
            if (Leg) { if (sb.Length > 0) sb.Append('+'); sb.Append('L'); }
            return sb.ToString();
        }
    }

    /// <summary>
    /// The immutable reward record of ONE enemy life, taken at spawn (WP-008).
    ///
    ///     Total = Base + VersionBonus + ArmorBonus
    ///
    /// Everything the death event needs to pay the kill is decided here, once, from the
    /// enemy's version and the armour it was fitted with at spawn. Nothing reads a live
    /// armour component at death, so a plate that was damaged or broken in the fight
    /// changes nothing (product decision 6), and a second death callback, a despawn or a
    /// replayed event cannot recompute a different number. <see cref="IsValid"/> is false
    /// only for the default value - a producer that never took a snapshot.
    /// </summary>
    public readonly struct EnemyRewardSnapshot
    {
        public readonly EnemyTier Tier;
        public readonly SpawnArmorComposition Armor;
        public readonly GoldRewardValue Value;

        /// <summary>True once a snapshot was actually computed. The default value is not a reward.</summary>
        public readonly bool IsValid;

        public EnemyRewardSnapshot(EnemyTier tier, SpawnArmorComposition armor, GoldRewardValue value)
        {
            Tier = tier;
            Armor = armor;
            Value = value;
            IsValid = true;
        }

        public int Base => Value.Base;
        public int VersionBonus => Value.VariantContribution;
        public int ArmorBonus => Value.ArmorContribution;
        public int Total => Value.Total;
    }

    /// <summary>
    /// The one place the gold value of a kill is computed (WP-008, DEC-043 / REQ-074).
    /// Pure: config in, numbers out; no Unity object, no state, no allocation.
    ///
    /// CONTRACT (approved product decisions 3-6):
    ///   * Base Reward      = the Standard version's <c>EnemyTierSettings.GoldReward</c>.
    ///                        Every enemy earns at least this.
    ///   * Version Bonus    = this version's GoldReward minus the Standard value, floored
    ///                        at zero. Standard therefore contributes 0; the request's
    ///                        2 / 5 / 10 / 25 ladder is honoured as 2+0 / 2+3 / 2+8 / 2+23
    ///                        without inventing a second table. "Version" is the enemy's
    ///                        Standard/Special/Elite/Ultra family - NOT the Special Weapon.
    ///   * Spawn Armor Bonus= sum of <c>EconomyConfigSection.ArmorGoldBonus*</c> over the
    ///                        pieces present AT SPAWN (helmet / body / legs). Damage to or
    ///                        loss of a plate during the fight does not reduce it.
    ///   * No randomness anywhere.
    ///
    /// The numbers themselves are PrototypeOnly placeholders in the central config; the
    /// formula is the product rule.
    /// </summary>
    public static class GoldRewardCalculator
    {
        /// <summary>The base reward every enemy shares: the Standard version's gold value. 0 without a config.</summary>
        public static int BaseReward(PrototypeConfig config)
        {
            return config != null ? Mathf.Max(0, config.GoldReward(EnemyTier.Standard)) : 0;
        }

        /// <summary>This version's bonus over the base. 0 for Standard, for a missing config, and for a version worth less than Standard.</summary>
        public static int VersionBonus(EnemyTier tier, PrototypeConfig config)
        {
            if (config == null)
            {
                return 0;
            }

            return Mathf.Max(0, config.GoldReward(tier) - BaseReward(config));
        }

        /// <summary>Spawn-armour bonus for a composition: the per-slot config values of the pieces present, summed.</summary>
        public static int ArmorBonus(SpawnArmorComposition armor, PrototypeConfig config)
        {
            if (config == null || armor.PieceCount == 0)
            {
                return 0;
            }

            EconomyConfigSection economy = config.Economy;
            int bonus = 0;
            if (armor.Helmet) bonus += economy.ArmorGoldBonusFor(ArmorSlot.Helmet);
            if (armor.Body) bonus += economy.ArmorGoldBonusFor(ArmorSlot.Body);
            if (armor.Leg) bonus += economy.ArmorGoldBonusFor(ArmorSlot.Leg);
            return bonus;
        }

        /// <summary>The three slots, resolved. Deterministic for the same inputs.</summary>
        public static GoldRewardValue Compute(EnemyTier tier, SpawnArmorComposition armor, PrototypeConfig config)
        {
            return new GoldRewardValue(BaseReward(config), VersionBonus(tier, config), ArmorBonus(armor, config));
        }

        /// <summary>The spawn-time record an enemy keeps for its whole life.</summary>
        public static EnemyRewardSnapshot Snapshot(EnemyTier tier, SpawnArmorComposition armor, PrototypeConfig config)
        {
            return new EnemyRewardSnapshot(tier, armor, Compute(tier, armor, config));
        }
    }
}
