using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// PROTOTYPE-ONLY upgrade card definition. The branch list is prototype content,
    /// not an approved upgrade roster.
    /// </summary>
    [Serializable]
    public sealed class UpgradeBranchSettings
    {
        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Identifier of a placeholder upgrade branch, not a tunable value.")]
        public UpgradeBranch Branch = UpgradeBranch.Piercing;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Placeholder card title shown on the round-end screen.")]
        public string DisplayName = "Piercing";

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Cost of tier 1. Request: start the range at 15-45 gold.")]
        public int BaseCost = 20;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Multiplier applied per already-owned tier. Request: cost rises with tier. DEC-085 (user, 2026-09-17, WP-041/045 simulation): 2.0 per tier for every branch — one card per round (DEC-071) absorbed ~12% of income at 1.5–1.6.")]
        public float CostGrowthPerTier = 2f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Highest tier obtainable in one run.", true)]
        public int MaxTier = 4;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Effect magnitude granted per tier. Meaning depends on the branch.", true)]
        public float EffectPerTier = 1f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Draw weight in the three-card offer.", true)]
        public float OfferWeight = 1f;

        [PrototypeOnly(PendingDecisions.ProfileXp, "Profile level required before this branch can be offered.")]
        public int UnlockProfileLevel = 1;

        public int CostForTier(int ownedTier)
        {
            int steps = Mathf.Max(0, ownedTier);
            return Mathf.Max(1, Mathf.RoundToInt(BaseCost * Mathf.Pow(CostGrowthPerTier, steps)));
        }
    }

    /// <summary>
    /// PROTOTYPE-ONLY gold and round-end shop economy.
    /// The collection method itself is explicitly undecided for the product.
    /// </summary>
    [Serializable]
    public sealed class EconomyConfigSection
    {
        [Header("Collection - product decision NOT made")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Default is ProximityMagnet purely so the prototype is easier to observe. NOT a product decision.")]
        public PrototypeGoldCollectionMode GoldCollectionMode = PrototypeGoldCollectionMode.ProximityMagnet;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Request value: magnet engages at ~3.5 m")]
        public float MagnetRadius = 3.5f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Speed the orb travels toward the player once attracted.", true)]
        public float MagnetSpeed = 14f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Acceleration applied while the orb homes in.", true)]
        public float MagnetAcceleration = 38f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Seconds an uncollected orb stays on the ground.", true)]
        public float OrbLifetime = 22f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Contact radius for ContactPickup mode.", true)]
        public float ContactPickupRadius = 0.9f;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Ceiling on simultaneously visible gold orbs; excess is direct-credited.", true)]
        public int MaxActiveOrbs = 60;

        [Header("Round-end shop")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Price of the one paid reroll per round-end screen (DEC-050 / REQ-079). The RULE (one reroll, before a purchase, " +
            "one or two cards replaced) is product; this NUMBER is a placeholder.")]
        public int RerollCost = 10;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Request: unspent gold carries into the next round.")]
        public bool CarryUnspentGold = true;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Upgrade cards the player may buy on one round-end offer. DEC-087 (user, 2026-09-17, Ö-6a): 2 — DEC-071's one purchase per round is superseded (WP-049 simulation: spend share 20% → 38–44%, R15 risk 8.8 → 6.1). The reroll still closes on the first purchase (DEC-050); the shop clamps to 1..CardsPerOffer.")]
        public int PurchasesPerRound = 2;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "WP-077 K1 (user, 2026-09-24): Hybrid = the offer draws three cards from the in-run tree frontier + the pin (Nişan); reroll kept. 1 = FreeTree (named alternative, not playable yet; behaves as Hybrid).")]
        public int RunTreeOfferModel = 0;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Lower bound of the request's 15-45 starting cost band; used as a sanity clamp.")]
        public int StartingCostMin = 15;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Upper bound of the request's 15-45 starting cost band; used as a sanity clamp.")]
        public int StartingCostMax = 45;

        [Header("Gold reward - armour bonus (DEC-043 / REQ-074)")]
        [PrototypeOnly(PendingDecisions.UpgradesAndGold,
            "Gold added for a HELMET the enemy was wearing when it SPAWNED. The rule (base + version + spawn armour, " +
            "deterministic, never reduced by breaking the plate) is product; the number is a placeholder.")]
        public int ArmorGoldBonusHelmet = 1;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Gold added for BODY ARMOUR present at spawn. Placeholder number, product rule.")]
        public int ArmorGoldBonusBody = 2;

        [PrototypeOnly(PendingDecisions.UpgradesAndGold, "Gold added for LEG ARMOUR present at spawn. Placeholder number, product rule.")]
        public int ArmorGoldBonusLeg = 1;

        /// <summary>Spawn-armour gold bonus for one slot. 0 for <see cref="ArmorSlot.None"/> and for a negative config value.</summary>
        public int ArmorGoldBonusFor(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return Mathf.Max(0, ArmorGoldBonusHelmet);
                case ArmorSlot.Body: return Mathf.Max(0, ArmorGoldBonusBody);
                case ArmorSlot.Leg: return Mathf.Max(0, ArmorGoldBonusLeg);
                default: return 0;
            }
        }

        public List<UpgradeBranchSettings> Branches = new List<UpgradeBranchSettings>
        {
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.Piercing, DisplayName = "Piercing",
                BaseCost = 30, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 1f,
                OfferWeight = 1f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.Ricochet, DisplayName = "Ricochet",
                BaseCost = 30, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 1f,
                OfferWeight = 1f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.Explosion, DisplayName = "Explosion",
                BaseCost = 45, CostGrowthPerTier = 2f, MaxTier = 3, EffectPerTier = 0.18f,
                OfferWeight = 0.8f, UnlockProfileLevel = 2
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.Chain, DisplayName = "Chain",
                BaseCost = 40, CostGrowthPerTier = 2f, MaxTier = 3, EffectPerTier = 1f,
                OfferWeight = 0.8f, UnlockProfileLevel = 3
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.FireRate, DisplayName = "Fire Rate",
                BaseCost = 18, CostGrowthPerTier = 2f, MaxTier = 5, EffectPerTier = 0.08f,
                OfferWeight = 1.2f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.ReloadSpeed, DisplayName = "Reload Speed",
                BaseCost = 15, CostGrowthPerTier = 2f, MaxTier = 5, EffectPerTier = 0.10f,
                OfferWeight = 1.2f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.MovementSpeed, DisplayName = "Movement Speed",
                BaseCost = 22, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 0.06f,
                OfferWeight = 1f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.MaximumHealth, DisplayName = "Maximum Health",
                BaseCost = 25, CostGrowthPerTier = 2f, MaxTier = 5, EffectPerTier = 15f,
                OfferWeight = 1f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.RegenerationDelay, DisplayName = "Regeneration Delay",
                BaseCost = 18, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 0.5f,
                OfferWeight = 0.9f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.RegenerationRate, DisplayName = "Regeneration Rate",
                BaseCost = 18, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 3f,
                OfferWeight = 0.9f, UnlockProfileLevel = 1
            },
            // WP-077 / WP-069 §6 (council proposal): armour multiplier +25 %/tier, stagger +0.15 s/tier,
            // +1 bomblet per tier (2/3/4). Opening price inside the DEC-070 band; growth DEC-085.
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.ArmorBreak, DisplayName = "Armor Break",
                BaseCost = 30, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 0.25f,
                OfferWeight = 1f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.Impact, DisplayName = "Impact",
                BaseCost = 25, CostGrowthPerTier = 2f, MaxTier = 4, EffectPerTier = 0.15f,
                OfferWeight = 1f, UnlockProfileLevel = 1
            },
            new UpgradeBranchSettings
            {
                Branch = UpgradeBranch.Cluster, DisplayName = "Cluster",
                BaseCost = 45, CostGrowthPerTier = 2f, MaxTier = 3, EffectPerTier = 1f,
                OfferWeight = 0.8f, UnlockProfileLevel = 1
            }
        };

        public UpgradeBranchSettings Find(UpgradeBranch branch)
        {
            for (int i = 0; i < Branches.Count; i++)
            {
                if (Branches[i] != null && Branches[i].Branch == branch)
                {
                    return Branches[i];
                }
            }

            return null;
        }
    }
}
