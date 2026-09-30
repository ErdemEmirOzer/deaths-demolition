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

        /// <summary>
        /// M-66: a tier's live next-tier detail in the language on screen - the pack's <c>shop.detail.&lt;Branch&gt;</c> filled
        /// with <see cref="UpgradeCardCatalog.TryFacts"/>' numbers, the same ones the English catalog line uses (one source,
        /// one path for EN and TR; numbers in the language's form, units from stat.fmt.*). Null when the branch has no
        /// computed line (ArmorBreak, Impact, Cluster): the caller then uses the node's own pack line.
        /// </summary>
        public static string TierDescription(UpgradeBranch branch, int ownedTier, string stringKey)
        {
            int next = ownedTier + 1;
            if (stringKey == "tree.run.branch.blast")
            {
                return Fill("tree.run.branch.blast.desc", "Blast radius +{radius} m, blast damage +{value}%.",
                    "radius", Number(RocketImpacts.BlastOuterPerTier * next), "value", Number(RocketImpacts.BlastDamagePerTier * next * 100f));
            }

            if (!UpgradeCardCatalog.TryFacts(branch, ownedTier, out TierFacts facts))
            {
                return null;
            }

            string line = UiStrings.Get("shop.detail." + branch, null);
            if (string.IsNullOrEmpty(line))
            {
                return UpgradeCardCatalog.Describe(branch, ownedTier);
            }

            for (int i = 0; i < facts.Count; i++)
            {
                float v = facts.Values[i];
                string number = Number(v, facts.Formats[i]);
                string text;
                switch (facts.Names[i])
                {
                    case "health":
                        text = v > 0f ? ", " + UiStrings.Format("stat.fmt.hp", "value", number, "{value} HP") : string.Empty;
                        break;
                    case "seconds":
                        text = v > 0f ? ", " + UiStrings.Format("stat.fmt.seconds", "value", number, "{value} s") : string.Empty;
                        break;
                    case "rate":
                        text = v > 0f ? ", " + UiStrings.Format("stat.fmt.hp_per_second", "value", number, "{value} HP/s") : string.Empty;
                        break;
                    default:
                        text = number;
                        break;
                }

                line = line.Replace("{" + facts.Names[i] + "}", text);
            }

            return line;
        }
    }
}
