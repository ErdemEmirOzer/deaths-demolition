using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 K10b · U11 (AD R1-C SHOULD-4): a tier node's before → after row — the stat label (an R1 content pack
    /// key), its value at a tier in DISPLAY units and the unit ("%", "s", "HP", "HP/s" or "" for a count). Computed
    /// from the same live tuning the card text uses (UpgradeCardCatalog.Describe), so the row cannot disagree with it.
    /// rl_1's Explosion tier (the Blast) has no single number and reports no row (empty label).
    /// </summary>
    public static class RunTreeTierStats
    {
        public static bool TryGet(UpgradeBranch branch, int tier, string weaponSlot, out string label, out float value, out string unit)
        {
            WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
            PlayerUpgradeTuning p = PlayerUpgradeTuning.Active;
            PrototypeConfig config = PrototypeConfig.Active;
            tier = Mathf.Max(0, tier);
            label = string.Empty;
            value = 0f;
            unit = string.Empty;

            switch (branch)
            {
                case UpgradeBranch.Piercing:
                    label = "stat.penetration"; value = tier * Mathf.Max(0, t.PierceTargetsPerTier); return true;
                case UpgradeBranch.Ricochet:
                    label = "tree.run.branch.ricochet.name"; unit = "%";
                    value = tier > 0 ? Mathf.Clamp01(t.RicochetBaseChance + t.RicochetChancePerTier * (tier - 1)) * 100f : 0f;
                    return true;
                case UpgradeBranch.Explosion:
                    if (weaponSlot == "rl_1") return false;
                    label = "tree.run.branch.explosion.name"; unit = "%";
                    value = tier > 0 ? Mathf.Clamp01(t.ExplosionBaseChance + t.ExplosionChancePerTier * (tier - 1)) * 100f : 0f;
                    return true;
                case UpgradeBranch.Chain:
                    label = "stat.chain"; value = tier * Mathf.Max(0, t.ChainTargetsPerTier); return true;
                case UpgradeBranch.FireRate:
                    label = "stat.fire_rate"; unit = "%";
                    value = (Mathf.Min(1f + t.FireRateBonusPerTier * tier, Mathf.Max(1f, t.MaximumFireRateMultiplier)) - 1f) * 100f;
                    return true;
                case UpgradeBranch.ReloadSpeed:
                    label = "stat.reload"; unit = "%";
                    value = -(1f - Mathf.Max(t.MinimumReloadFraction, 1f - t.ReloadSpeedBonusPerTier * tier)) * 100f;
                    return true;
                case UpgradeBranch.ArmorBreak:
                    label = "stat.armour"; unit = "%"; value = t.ArmorBreakBonusPerTier * tier * 100f; return true;
                case UpgradeBranch.Impact:
                    label = "stat.stagger"; value = t.ImpactStaggerPerTier * tier; return true;
                case UpgradeBranch.Cluster:
                    label = "tree.run.branch.cluster.name"; value = tier > 0 ? tier + 1 : 0; return true;
                case UpgradeBranch.MovementSpeed:
                    label = "tree.run.body.move.name"; unit = "%"; value = (p.MoveSpeedMultiplier(tier) - 1f) * 100f; return true;
                case UpgradeBranch.MaximumHealth:
                    label = "tree.run.body.max_health.name"; unit = "HP";
                    value = (config != null ? config.Player.MaxHealth : 0f) * p.MaxHealthMultiplier(tier);
                    return true;
                case UpgradeBranch.RegenerationDelay:
                    label = "tree.run.body.regen_delay.name"; unit = "s";
                    value = (config != null ? config.Player.HealthRegenDelay : 0f) * p.RegenDelayMultiplier(tier);
                    return true;
                case UpgradeBranch.RegenerationRate:
                    label = "tree.run.body.regen_rate.name"; unit = "HP/s";
                    value = (config != null ? config.Player.HealthRegenRate : 0f) * p.RegenRateMultiplier(tier);
                    return true;
            }

            return false;
        }
    }
}
