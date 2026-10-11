using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>WP-107 (R4): what a wall station sells the player standing at it.</summary>
    public enum WallBuyAction
    {
        /// <summary>A different weapon: paid in full, it replaces the classic weapon in hand.</summary>
        Buy = 0,

        /// <summary>The weapon already in hand: an ammo refill at <see cref="WallBuyCatalog.RefillShare"/> of the price.</summary>
        Refill = 1
    }

    /// <summary>WP-107 (R4): why a wall buy cannot go through right now (<see cref="None"/> = it can).</summary>
    public enum WallBuyBlock
    {
        None = 0,
        NotEnoughGold = 1,

        /// <summary>The weapon in hand already has a full magazine and this round's reserve - a refill would add nothing.</summary>
        AmmoFull = 2,

        /// <summary>A Special is in hand (it parks the classic weapon until it ends).</summary>
        SpecialActive = 3,

        /// <summary>No run, no wallet, or the simulation is frozen (round-end beat, upgrade screen).</summary>
        Unavailable = 4
    }

    /// <summary>WP-107 (R4): how a press at a station ended.</summary>
    public enum WallBuyResult
    {
        Bought = 0,
        Refilled = 1,
        Denied = 2
    }

    /// <summary>WP-107 (R4): the priced offer of one station for the player's current state.</summary>
    public readonly struct WallBuyQuote
    {
        public readonly WallBuyAction Action;
        public readonly int Cost;
        public readonly WallBuyBlock Block;

        /// <summary>WP-107B: the price before the run tree's wall buy scale (Arms Dealer); equal to <see cref="Cost"/> without one.</summary>
        public readonly int BaseCost;

        public WallBuyQuote(WallBuyAction action, int cost, WallBuyBlock block, int baseCost = -1)
        {
            Action = action;
            Cost = cost;
            Block = block;
            BaseCost = baseCost < 0 ? cost : baseCost;
        }

        public bool CanBuy => Block == WallBuyBlock.None;
    }

    /// <summary>
    /// WP-107 (R4): the interaction prompt of the station the player stands at (S-09's HUD draws it; S-08 may ping on
    /// <see cref="Visible"/>). <see cref="Visible"/> false = no station in reach.
    /// </summary>
    public readonly struct WallBuyPrompt
    {
        public readonly bool Visible;
        public readonly string StationId;
        public readonly ClassicWeaponId Weapon;
        public readonly string WeaponKey;
        public readonly WallBuyAction Action;
        public readonly int Cost;
        public readonly WallBuyBlock Block;
        public readonly Vector3 StationWorld;

        /// <summary>WP-107B: the price before the run tree's discount (the HUD's discount seal shows when <see cref="Discounted"/>).</summary>
        public readonly int BaseCost;

        public WallBuyPrompt(string stationId, ClassicWeaponId weapon, string weaponKey, WallBuyQuote quote, Vector3 stationWorld)
        {
            Visible = true;
            StationId = stationId;
            Weapon = weapon;
            WeaponKey = weaponKey;
            Action = quote.Action;
            Cost = quote.Cost;
            Block = quote.Block;
            StationWorld = stationWorld;
            BaseCost = quote.BaseCost;
        }

        /// <summary>WP-107B: the run tree lowered the price (Arms Dealer).</summary>
        public bool Discounted => Cost < BaseCost;

        /// <summary>The player has the gold (or nothing else stands in the way but other blocks).</summary>
        public bool Affordable => Block != WallBuyBlock.NotEnoughGold;

        public bool SameAs(in WallBuyPrompt other)
        {
            return Visible == other.Visible && StationId == other.StationId && Weapon == other.Weapon && Action == other.Action
                   && Cost == other.Cost && Block == other.Block && BaseCost == other.BaseCost;
        }
    }

    /// <summary>WP-107 (R4): one resolved press at a station (S-08's sounds, the HUD's beat).</summary>
    public readonly struct WallBuyOutcome
    {
        public readonly WallBuyResult Result;
        public readonly WallBuyBlock Block;
        public readonly string StationId;
        public readonly ClassicWeaponId Weapon;
        public readonly int Cost;
        public readonly Vector3 StationWorld;

        public WallBuyOutcome(WallBuyResult result, WallBuyBlock block, string stationId, ClassicWeaponId weapon, int cost, Vector3 stationWorld)
        {
            Result = result;
            Block = block;
            StationId = stationId;
            Weapon = weapon;
            Cost = cost;
            StationWorld = stationWorld;
        }
    }
}
