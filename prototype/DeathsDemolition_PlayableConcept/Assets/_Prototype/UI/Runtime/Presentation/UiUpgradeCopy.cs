using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>UI wording over the shop's live counts and tuning; never changes the shop or its data.</summary>
    public static class UiUpgradeCopy
    {
        private static string Number(int value) => UiStrings.Number(value);

        // M-65: the language's number form (TR "1,34"), never the raw invariant digits.
        private static string Number(float value, string format = "0.#") => UiStrings.Number(value, format);

        private static string CountWord(int count)
        {
            switch (count)
            {
                case 1: return UiStrings.Get("shop.count.one", "one");
                case 2: return UiStrings.Get("shop.count.two", "two");
                case 3: return UiStrings.Get("shop.count.three", "three");
                default: return count.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string Intro(int limit) => limit > 1
            ? UiStrings.Format("shop.intro.many", "count", CountWord(limit), "Buy up to {count} upgrades this round, or reroll the offer once before the first. Unspent gold carries into the next round.")
            : UiStrings.Get("shop.intro.one", "Buy one upgrade this round, or reroll the offer once first. Unspent gold carries into the next round.");

        public static string Choose(int limit) => UiStrings.Format("shop.note.choose_many", "count", CountWord(limit),
            "Choose up to {count} upgrades, or reroll once first, then continue.");

        public static string PurchaseLimitReached(int limit) => UiStrings.Format("shop.refuse.limit", "count", CountWord(limit),
            "All {count} picks for this round are used. The offer is locked; nothing was spent.");

        public static string Footer(int limit, int gold, int purchases, bool locked)
        {
            if (purchases <= 0)
            {
                return UiStrings.Format("shop.footer.none", "gold", gold.ToString(CultureInfo.InvariantCulture),
                    "Nothing bought yet. All {gold} gold carries into the next round.");
            }

            if (limit > 1 && !locked)
            {
                return Fill("shop.footer.remaining", "Applied immediately. {used} of {max} picks used, {left} left. {gold} gold carries into the next round.",
                    "used", Number(purchases), "max", Number(limit), "left", Number(Mathf.Max(0, limit - purchases)), "gold", Number(gold));
            }

            return UiStrings.Format(limit > 1 ? "shop.footer.locked_many" : "shop.footer.locked_one", "gold", Number(gold),
                limit > 1 ? "Applied immediately. All picks used, so the offer is now locked. {gold} gold carries into the next round."
                    : "Applied immediately. One upgrade per round, so the offer is now locked. {gold} gold carries into the next round.");
        }

        private static string Fill(string key, string fallback, params string[] args)
        {
            string line = UiStrings.Get(key, fallback);
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                line = line.Replace("{" + args[i] + "}", args[i + 1]);
            }

            return line;
        }

        /// <summary>The catalog's live next-tier detail in Turkish. English keeps its original catalog wording.</summary>
        public static string TierDescription(UpgradeBranch branch, int ownedTier, string stringKey)
        {
            if (UiStrings.Language != UiStrings.Turkish)
            {
                return UpgradeCardCatalog.Describe(branch, ownedTier);
            }

            WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
            PlayerUpgradeTuning p = PlayerUpgradeTuning.Active;
            PrototypeConfig config = PrototypeConfig.Active;
            int next = ownedTier + 1;
            if (stringKey == "tree.run.branch.blast")
            {
                return Fill("tree.run.branch.blast.desc", "Blast radius +{radius} m, blast damage +{value}%.",
                    "radius", Number(RocketImpacts.BlastOuterPerTier * next), "value", Number(RocketImpacts.BlastDamagePerTier * next * 100f));
            }

            string key = "shop.detail." + branch;
            switch (branch)
            {
                case UpgradeBranch.Piercing:
                    return Fill(key, "", "count", Number(Mathf.Max(0, t.PierceTargetsPerTier)),
                        "total", Number(next * Mathf.Max(0, t.PierceTargetsPerTier)), "damage", Number(t.PierceDamageFalloff * 100f, "0"));
                case UpgradeBranch.Ricochet:
                    return Fill(key, "", "chance", Number(Mathf.Clamp01(t.RicochetBaseChance + t.RicochetChancePerTier * (next - 1)) * 100f, "0"),
                        "radius", Number(t.RicochetRadius), "count", Number(1 + (next - 1) / Mathf.Max(1, t.RicochetTiersPerExtraBounce)), "damage", Number(t.RicochetDamageScale * 100f, "0"));
                case UpgradeBranch.Explosion:
                    return Fill(key, "", "chance", Number(Mathf.Clamp01(t.ExplosionBaseChance + t.ExplosionChancePerTier * (next - 1)) * 100f, "0"),
                        "radius", Number(t.ExplosionBaseRadius + t.ExplosionRadiusPerTier * (next - 1)), "scale", Number(t.ExplosionBaseDamageScale + t.ExplosionDamageScalePerTier * (next - 1), "0.##"));
                case UpgradeBranch.Chain:
                    return Fill(key, "", "count", Number(Mathf.Max(0, t.ChainTargetsPerTier)), "radius", Number(t.ChainRadius),
                        "damage", Number(t.ChainDamageScale * 100f, "0"), "falloff", Number((1f - t.ChainDamageFalloff) * 100f, "0"));
                case UpgradeBranch.FireRate:
                    return Fill(key, "", "value", Number(t.FireRateBonusPerTier * 100f, "0"),
                        "total", Number(Mathf.Min(1f + t.FireRateBonusPerTier * next, Mathf.Max(1f, t.MaximumFireRateMultiplier)), "0.00"), "max", Number(t.MaximumFireRateMultiplier, "0.00"));
                case UpgradeBranch.ReloadSpeed:
                    return Fill(key, "", "value", Number(t.ReloadSpeedBonusPerTier * 100f, "0"),
                        "total", Number(Mathf.Max(t.MinimumReloadFraction, 1f - t.ReloadSpeedBonusPerTier * next), "0.00"), "min", Number(t.MinimumReloadFraction, "0.00"));
                case UpgradeBranch.MovementSpeed:
                    return Fill(key, "", "value", Number(p.MoveSpeedBonusPerTier * 100f, "0"),
                        "total", Number(p.MoveSpeedMultiplier(next), "0.00"), "max", Number(p.MaximumMoveSpeedMultiplier, "0.00"));
                case UpgradeBranch.MaximumHealth:
                    float health = config != null ? config.Player.MaxHealth * p.MaxHealthMultiplier(next) : 0f;
                    return Fill(key, "", "value", Number(p.MaxHealthBonusPerTier * 100f, "0"), "total", Number(p.MaxHealthMultiplier(next), "0.00"),
                        "health", health > 0f ? ", " + UiStrings.Format("stat.fmt.hp", "value", Number(health, "0"), "{value} HP") : string.Empty);
                case UpgradeBranch.RegenerationDelay:
                    float delay = config != null ? config.Player.HealthRegenDelay * p.RegenDelayMultiplier(next) : 0f;
                    return Fill(key, "", "value", Number(p.RegenDelayReductionPerTier * 100f, "0"), "total", Number(p.RegenDelayMultiplier(next), "0.00"),
                        "seconds", delay > 0f ? ", " + UiStrings.Format("stat.fmt.seconds", "value", Number(delay, "0.0"), "{value} s") : string.Empty);
                case UpgradeBranch.RegenerationRate:
                    float rate = config != null ? config.Player.HealthRegenRate * p.RegenRateMultiplier(next) : 0f;
                    return Fill(key, "", "value", Number(p.RegenRateBonusPerTier * 100f, "0"), "total", Number(p.RegenRateMultiplier(next), "0.00"),
                        "rate", rate > 0f ? ", " + UiStrings.Format("stat.fmt.hp_per_second", "value", Number(rate), "{value} HP/s") : string.Empty);
                default:
                    return UiStrings.Get("tree.lock.pending", "Not in this build yet.");
            }
        }
    }
}
