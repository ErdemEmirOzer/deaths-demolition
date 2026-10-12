using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107 (R4, user 11.10 note 8: "every weapon but the Specials can be bought with gold at certain points of the map,
    /// CoD Zombies wall buy; each swap is paid again; the rocket launcher is very expensive and hard to reach"). Pure rules,
    /// no scene: what a station offers and whether it can go through.
    /// - A different weapon: <see cref="WallBuyAction.Buy"/> at the full price.
    /// - The weapon in hand: <see cref="WallBuyAction.Refill"/> at the refill price; refused when the ammo is already full
    ///   (no gold taken for nothing).
    /// - A Special in hand blocks both (it parks the classic weapon); so does a frozen or missing run.
    /// - Not enough gold blocks last, so the prompt names the price the player is short of.
    /// </summary>
    public static class WallBuyRules
    {
        /// <summary>A refill costs <paramref name="share"/> of the price, rounded up, at least 1 (0 when the weapon is not sold).</summary>
        public static int RefillCost(int price, float share)
        {
            if (price <= 0) return 0;
            return Mathf.Max(1, Mathf.CeilToInt(price * Mathf.Clamp01(share) - 0.0001f));
        }

        /// <param name="price">The weapon's RAW catalog price (WallBuyCatalog.PriceOf). WP-104 B2 ec_dealer: the run tree's discount is
        /// applied here, once — the wall price scaled, the refill half of the raw price scaled (never half of the discounted one).
        /// <see cref="WallBuyQuote.BaseCost"/> keeps the raw value for the HUD's discount seal. Neutral tree: exactly the M4 prices.</param>
        public static WallBuyQuote Quote(ClassicWeaponId target, int price, float refillShare, bool available, bool specialActive,
            bool hasClassic, ClassicWeaponId inHand, bool ammoFull, int balance)
        {
            bool refill = hasClassic && inHand == target;
            WallBuyAction action = refill ? WallBuyAction.Refill : WallBuyAction.Buy;
            int listed = Mathf.Max(0, price);
            int cost = refill ? DD.Prototype.Rounds.TreeHooks.AmmoPrice(listed, refillShare) : DD.Prototype.Rounds.TreeHooks.WallBuyPrice(listed);   // WP-104 B2 ec_dealer
            int baseCost = refill ? RefillCost(listed, refillShare) : listed;
            if (!available || price <= 0) return new WallBuyQuote(action, cost, WallBuyBlock.Unavailable, baseCost);
            if (specialActive) return new WallBuyQuote(action, cost, WallBuyBlock.SpecialActive, baseCost);
            if (refill && ammoFull) return new WallBuyQuote(action, cost, WallBuyBlock.AmmoFull, baseCost);
            if (balance < cost) return new WallBuyQuote(action, cost, WallBuyBlock.NotEnoughGold, baseCost);
            return new WallBuyQuote(action, cost, WallBuyBlock.None, baseCost);
        }

        /// <summary>The run phases a station works in: the simulation runs (fighting, the countdown before a round).</summary>
        public static bool PhaseAllows(DD.Prototype.Rounds.RunPhase phase)
        {
            return phase == DD.Prototype.Rounds.RunPhase.Fighting || phase == DD.Prototype.Rounds.RunPhase.Countdown;
        }
    }
}
