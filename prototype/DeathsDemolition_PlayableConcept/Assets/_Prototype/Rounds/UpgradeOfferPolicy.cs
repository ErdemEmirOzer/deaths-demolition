using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>Randomness for the offer draw. Injected so a test can replay a fixed sequence.</summary>
    public interface IUpgradeOfferRandom
    {
        /// <summary>Uniform value in [0, 1).</summary>
        float NextValue();
    }

    /// <summary>Production randomness: UnityEngine.Random.</summary>
    public sealed class UnityUpgradeOfferRandom : IUpgradeOfferRandom
    {
        public static readonly UnityUpgradeOfferRandom Shared = new UnityUpgradeOfferRandom();

        public float NextValue()
        {
            // Random.value is inclusive of 1; the policy treats 1 as "last bucket".
            return UnityEngine.Random.value;
        }
    }

    /// <summary>Deterministic randomness for tests and probes (System.Random with a seed).</summary>
    public sealed class SeededUpgradeOfferRandom : IUpgradeOfferRandom
    {
        private readonly System.Random _random;

        public int Seed { get; }

        public SeededUpgradeOfferRandom(int seed)
        {
            Seed = seed;
            _random = new System.Random(seed);
        }

        public float NextValue()
        {
            return (float)_random.NextDouble();
        }
    }

    /// <summary>Why a branch is, or is not, a candidate for the round-end offer (DEC-047 / DEC-049).</summary>
    public enum UpgradeEligibility
    {
        Eligible = 0,

        /// <summary>Null entry, or a MaxTier of zero, in the economy table.</summary>
        InvalidSettings = 1,

        /// <summary>OfferWeight at or below zero: the table says "never draw this".</summary>
        ZeroWeight = 2,

        /// <summary>No live system reads the branch (UpgradeCardCatalog). Never sold for real gold.</summary>
        NoLiveConsumer = 3,

        /// <summary>Already at its ceiling on the live build.</summary>
        Maxed = 4,

        /// <summary>The content is not unlocked for this profile (UpgradeBranchSettings.UnlockProfileLevel).</summary>
        Locked = 5
    }

    /// <summary>What went wrong when an offer or a reroll could not be produced. <see cref="None"/> is the only success.</summary>
    public enum UpgradeOfferFault
    {
        None = 0,

        /// <summary>No PrototypeConfig at all.</summary>
        NoConfig = 1,

        /// <summary>The economy section lists no branch.</summary>
        NoUpgradeTable = 2,

        /// <summary>Fewer eligible branches than cards in an offer (three). Every card must be distinct and valid.</summary>
        NotEnoughEligibleBranches = 3,

        /// <summary>The economy table lists the same branch twice; a distinct offer cannot be guaranteed.</summary>
        DuplicateBranch = 4,

        /// <summary>A reroll found no eligible branch outside the current offer to swap in.</summary>
        NoReplacementAvailable = 5,

        /// <summary>The produced offer failed its own validation (assertion branch; nothing is committed).</summary>
        InvalidResult = 6,

        /// <summary>
        /// Fewer eligible branches than cards because the build has MAXED the rest - the
        /// table is fine, the run has outgrown it. The same safe state as any other fault
        /// (no cards, nothing charged, CONTINUE works), reported as a warning, not an error.
        /// </summary>
        PoolExhausted = 7
    }

    /// <summary>
    /// The pure half of the round-end offer (WP-008): which cards to show and which to
    /// swap on a reroll. Lists in, lists out, injected randomness, no Unity object, no
    /// LINQ, no allocation beyond the caller's lists. The shop owns the state
    /// (entitlements, revision, wallet); this class owns the rules:
    ///
    ///   * an offer is EXACTLY <see cref="CardsPerOffer"/> distinct eligible branches
    ///     (DEC-047, product decisions 8/10); fewer eligible branches is a fault, never a
    ///     padded or duplicated offer (decision 17);
    ///   * a reroll replaces ONE OR TWO of the three cards, never zero, never all three
    ///     (decision 16), with branches not already on the offer, so the result is again
    ///     three distinct eligible cards (decision 15);
    ///   * draws are weighted by OfferWeight, without replacement; there is no retry loop,
    ///     so a small pool cannot spin (decision C).
    /// </summary>
    public static class UpgradeOfferPolicy
    {
        /// <summary>DEC-047 / decision 8: exactly three cards per round-end offer.</summary>
        public const int CardsPerOffer = 3;

        /// <summary>DEC-048 / REQ-078: at most one purchase per round-end screen.</summary>
        public const int MaxPurchasesPerRound = 1;

        /// <summary>DEC-050 / REQ-079: at most one paid reroll per round-end screen, before the purchase.</summary>
        public const int MaxRerollsPerRound = 1;

        /// <summary>Decision 16: a reroll changes at least this many cards...</summary>
        public const int MinRerollReplacements = 1;

        /// <summary>...and at most this many, so at least one card survives.</summary>
        public const int MaxRerollReplacements = CardsPerOffer - 1;

        // ------------------------------------------------------------------ eligibility

        /// <summary>
        /// Decision 9 / DEC-049. Price is deliberately NOT a criterion: an unaffordable
        /// card is shown with its buy button disabled.
        /// </summary>
        public static UpgradeEligibility Classify(UpgradeBranchSettings settings, int ownedTier, bool hasLiveConsumer, bool unlocked)
        {
            if (settings == null || settings.MaxTier <= 0)
            {
                return UpgradeEligibility.InvalidSettings;
            }

            if (settings.OfferWeight <= 0f)
            {
                return UpgradeEligibility.ZeroWeight;
            }

            if (!hasLiveConsumer)
            {
                return UpgradeEligibility.NoLiveConsumer;
            }

            if (!unlocked)
            {
                return UpgradeEligibility.Locked;
            }

            if (ownedTier >= settings.MaxTier)
            {
                return UpgradeEligibility.Maxed;
            }

            return UpgradeEligibility.Eligible;
        }

        // ------------------------------------------------------------------ draws

        /// <summary>
        /// Weighted index over <paramref name="weights"/>. -1 for an empty list. A roll at
        /// the very top lands on the last bucket, never past it.
        /// </summary>
        public static int DrawWeightedIndex(List<float> weights, IUpgradeOfferRandom random)
        {
            if (weights == null || weights.Count == 0)
            {
                return -1;
            }

            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                total += Mathf.Max(0f, weights[i]);
            }

            if (float.IsNaN(total) || total <= 0f)
            {
                return 0;
            }

            float roll = Mathf.Clamp01(random != null ? random.NextValue() : 0f) * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= Mathf.Max(0f, weights[i]);
                if (roll < 0f)
                {
                    return i;
                }
            }

            return weights.Count - 1;
        }

        /// <summary>
        /// Draws <see cref="CardsPerOffer"/> distinct branches from the eligible
        /// candidates, weighted, without replacement. <paramref name="candidates"/> and
        /// <paramref name="weights"/> are CONSUMED (entries removed) so the caller's scratch
        /// lists need no copy. Returns <see cref="UpgradeOfferFault.None"/> with exactly
        /// three picks, or a fault with <paramref name="picks"/> cleared.
        /// </summary>
        public static UpgradeOfferFault TryBuildOffer(List<UpgradeBranchSettings> candidates, List<float> weights,
            IUpgradeOfferRandom random, List<UpgradeBranchSettings> picks)
        {
            picks.Clear();

            if (candidates == null || weights == null || candidates.Count != weights.Count)
            {
                return UpgradeOfferFault.InvalidResult;
            }

            if (HasDuplicateBranch(candidates))
            {
                return UpgradeOfferFault.DuplicateBranch;
            }

            if (candidates.Count < CardsPerOffer)
            {
                return UpgradeOfferFault.NotEnoughEligibleBranches;
            }

            for (int slot = 0; slot < CardsPerOffer; slot++)
            {
                int pick = DrawWeightedIndex(weights, random);
                if (pick < 0)
                {
                    picks.Clear();
                    return UpgradeOfferFault.InvalidResult;
                }

                picks.Add(candidates[pick]);
                candidates.RemoveAt(pick);
                weights.RemoveAt(pick);
            }

            return Validate(picks) ? UpgradeOfferFault.None : UpgradeOfferFault.InvalidResult;
        }

        /// <summary>
        /// Computes a reroll of <paramref name="current"/> (three distinct branches) using
        /// <paramref name="replacements"/> - the eligible branches NOT on the current offer
        /// - and their weights (both CONSUMED). One or two slots are replaced: two when at
        /// least two replacements exist and the coin says so, otherwise one. The kept
        /// cards stay in their slots; <paramref name="result"/> receives the new three in
        /// slot order and <paramref name="replacedSlots"/> the indices that changed.
        /// Nothing is drawn when the pool is empty (fault, no randomness consumed).
        /// </summary>
        public static UpgradeOfferFault TryReroll(List<UpgradeBranchSettings> current,
            List<UpgradeBranchSettings> replacements, List<float> weights, IUpgradeOfferRandom random,
            List<UpgradeBranchSettings> result, List<int> replacedSlots)
        {
            result.Clear();
            replacedSlots.Clear();

            if (current == null || current.Count != CardsPerOffer || !Validate(current) ||
                replacements == null || weights == null || replacements.Count != weights.Count)
            {
                return UpgradeOfferFault.InvalidResult;
            }

            for (int i = 0; i < replacements.Count; i++)
            {
                if (replacements[i] == null || IndexOfBranch(current, replacements[i].Branch) >= 0)
                {
                    // A replacement already on the offer would make a duplicate. The caller
                    // filtered; this is the assertion.
                    return UpgradeOfferFault.InvalidResult;
                }
            }

            if (HasDuplicateBranch(replacements))
            {
                return UpgradeOfferFault.DuplicateBranch;
            }

            if (replacements.Count == 0)
            {
                return UpgradeOfferFault.NoReplacementAvailable;
            }

            // How many cards change: one or two (decision 16). Two needs two distinct
            // replacements; with exactly one available the answer is one, no coin.
            int count = MinRerollReplacements;
            if (replacements.Count >= MaxRerollReplacements)
            {
                float coin = random != null ? random.NextValue() : 0f;
                count = coin < 0.5f ? MinRerollReplacements : MaxRerollReplacements;
            }

            // Which slots change: distinct, uniform.
            int first = Mathf.Clamp((int)(Mathf.Clamp01(random != null ? random.NextValue() : 0f) * CardsPerOffer), 0, CardsPerOffer - 1);
            replacedSlots.Add(first);
            if (count == 2)
            {
                int second = Mathf.Clamp((int)(Mathf.Clamp01(random != null ? random.NextValue() : 0f) * (CardsPerOffer - 1)), 0, CardsPerOffer - 2);
                if (second >= first)
                {
                    second++;
                }

                replacedSlots.Add(second);
            }

            for (int i = 0; i < current.Count; i++)
            {
                result.Add(current[i]);
            }

            for (int i = 0; i < replacedSlots.Count; i++)
            {
                int pick = DrawWeightedIndex(weights, random);
                if (pick < 0)
                {
                    result.Clear();
                    replacedSlots.Clear();
                    return UpgradeOfferFault.InvalidResult;
                }

                result[replacedSlots[i]] = replacements[pick];
                replacements.RemoveAt(pick);
                weights.RemoveAt(pick);
            }

            int changed = CountChanged(current, result);
            if (!Validate(result) || changed < MinRerollReplacements || changed > MaxRerollReplacements)
            {
                result.Clear();
                replacedSlots.Clear();
                return UpgradeOfferFault.InvalidResult;
            }

            return UpgradeOfferFault.None;
        }

        // ------------------------------------------------------------------ validation

        /// <summary>Exactly three entries, none null, all branches distinct.</summary>
        public static bool Validate(List<UpgradeBranchSettings> offer)
        {
            if (offer == null || offer.Count != CardsPerOffer)
            {
                return false;
            }

            for (int i = 0; i < offer.Count; i++)
            {
                if (offer[i] == null)
                {
                    return false;
                }

                for (int j = i + 1; j < offer.Count; j++)
                {
                    if (offer[j] != null && offer[j].Branch == offer[i].Branch)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Slots whose branch differs between two offers of equal length.</summary>
        public static int CountChanged(List<UpgradeBranchSettings> before, List<UpgradeBranchSettings> after)
        {
            if (before == null || after == null || before.Count != after.Count)
            {
                return -1;
            }

            int changed = 0;
            for (int i = 0; i < before.Count; i++)
            {
                bool same = before[i] != null && after[i] != null && before[i].Branch == after[i].Branch;
                if (!same)
                {
                    changed++;
                }
            }

            return changed;
        }

        public static bool HasDuplicateBranch(List<UpgradeBranchSettings> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                {
                    continue;
                }

                for (int j = i + 1; j < list.Count; j++)
                {
                    if (list[j] != null && list[j].Branch == list[i].Branch)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static int IndexOfBranch(List<UpgradeBranchSettings> list, UpgradeBranch branch)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].Branch == branch)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
