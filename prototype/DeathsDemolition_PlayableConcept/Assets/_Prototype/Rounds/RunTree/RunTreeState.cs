using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 (DEC-095, direction §2): one run's in-run tree — which non-tier nodes are owned,
    /// Hearth stacks, the Special that names the links, the pin (Nişan) — and the rules that turn
    /// that into the seven S-09 node states. Tier ownership is NOT duplicated here: the live build
    /// (<see cref="WeaponModifierState"/>) stays the one source for branch tiers.
    /// Gold and this state never reach the profile (REQ-018); a new run starts empty.
    /// </summary>
    public sealed class RunTreeState
    {
        /// <summary>Tier eligibility of a branch on this weapon/profile (the shop's Classify). Null = every branch eligible.</summary>
        public Func<UpgradeBranch, int, UpgradeEligibility> TierEligibility;

        private bool[] _owned = new bool[0];
        private readonly List<int> _scratch = new List<int>(8);

        public RunTreeGraph Graph { get; private set; }
        public int RunId { get; private set; }
        public int HearthStacks { get; private set; }
        public int HearthThisRound { get; private set; }
        /// <summary>WP-069 key of the Special picked up first this run ("sw_arc" / "sw_gel"); empty before any pickup.</summary>
        public string LinkedSpecial { get; private set; } = string.Empty;
        /// <summary>Index of the pinned node (Nişan), -1 when none. Lives for the run.</summary>
        public int PinnedIndex { get; private set; } = -1;
        public int PurseBonusPaid { get; private set; }

        /// <summary>WP-077 B2b: the meta PATH's run modifiers this run started with.</summary>
        public MetaRunModifiers Meta { get; private set; } = MetaRunModifiers.None;

        private int _earnedMark;
        private PrototypeConfig _config;

        public bool IsBuilt => Graph != null;

        /// <summary>Builds the tree for the weapon and clears the run's ownership. Called by the shop on a new run id.</summary>
        public void Begin(int runId, ClassicWeaponId weapon, PrototypeConfig config) => Begin(runId, weapon, config, MetaRunModifiers.None);

        public void Begin(int runId, ClassicWeaponId weapon, PrototypeConfig config, MetaRunModifiers meta)
        {
            RunId = runId;
            Meta = meta;
            _config = config;
            Graph = RunTreeGraph.Build(weapon, config);
            _owned = new bool[Graph.Count];
            HearthStacks = 0;
            HearthThisRound = 0;
            LinkedSpecial = string.Empty;
            PinnedIndex = -1;
            PurseBonusPaid = 0;
            _earnedMark = 0;
            if (meta.PurseAtStart && Graph.PurseIndex >= 0 && RunTreeEffects.IsLive(Graph[Graph.PurseIndex].EffectId))
            {
                _owned[Graph.PurseIndex] = true;   // meta "Purse at Start"
            }
        }

        // ------------------------------------------------------------------ queries

        public bool IsOwned(int i) => i >= 0 && i < _owned.Length && _owned[i];

        /// <summary>Owned non-tier nodes of a kind across both lanes (run summary, telemetry).</summary>
        public int OwnedCount(RunTreeNodeKind kind)
        {
            if (!IsBuilt) return 0;
            if (kind == RunTreeNodeKind.Hearth) return HearthStacks;
            int c = 0;
            for (int j = 0; j < Graph.Count; j++)
            {
                if (_owned[j] && Graph[j].Kind == kind) c++;
            }

            return c;
        }

        public int OwnedTier(int i, WeaponModifierState build)
        {
            if (!IsBuilt || i < 0 || i >= Graph.Count) return 0;
            RunTreeNode n = Graph[i];
            switch (n.Kind)
            {
                case RunTreeNodeKind.Tier: return build != null ? build.GetTier(n.Branch) : 0;
                case RunTreeNodeKind.Hearth: return HearthStacks;
                default: return _owned[i] ? 1 : 0;
            }
        }

        /// <summary>Gold price of the node's next step (tier price with the DEC-070 opening clamp; Hearth 50 + 30·n).</summary>
        public int Cost(int i, WeaponModifierState build)
        {
            RunTreeNode n = Graph[i];
            switch (n.Kind)
            {
                case RunTreeNodeKind.Tier:
                {
                    EconomyConfigSection economy = _config != null ? _config.Economy : null;
                    UpgradeBranchSettings s = economy != null ? economy.Find(n.Branch) : null;
                    return s != null ? RoundUpgradeShop.ResolveCost(s, OwnedTier(i, build), economy) : 0;
                }
                case RunTreeNodeKind.Hearth:
                    return Math.Max(1, n.FlatCost + (Meta.HearthCostStepOverride >= 0 ? Meta.HearthCostStepOverride : n.CostStep) * HearthStacks);
                case RunTreeNodeKind.Capstone:
                    return Math.Max(1, Mathf.RoundToInt(n.FlatCost * (1f - Mathf.Clamp01(Meta.CapstoneDiscount))));
                default:
                    return Math.Max(1, n.FlatCost);
            }
        }

        /// <summary>Index of the owned node that closed this one (same exclusion group), else -1.</summary>
        public int ExcludedBy(int i)
        {
            RunTreeNode n = Graph[i];
            if (n.ExclusionGroup == 0) return -1;
            for (int j = 0; j < Graph.Count; j++)
            {
                if (j != i && _owned[j] && Graph[j].ExclusionGroup == n.ExclusionGroup) return j;
            }

            return -1;
        }

        /// <summary>
        /// The prerequisite rule of §2.1. <paramref name="missing"/> names a node whose purchase would
        /// move this one closer (the "NEEDS …" line), or -1.
        /// </summary>
        public bool PrereqMet(int i, WeaponModifierState build, out int missing)
        {
            missing = -1;
            RunTreeNode n = Graph[i];
            switch (n.Kind)
            {
                case RunTreeNodeKind.Tier:
                case RunTreeNodeKind.Purse:
                    return true;

                case RunTreeNodeKind.BranchKeystone:
                {
                    int tier = n.Parents.Length > 0 ? n.Parents[0] : -1;
                    if (tier >= 0 && OwnedTier(tier, build) >= 2) return true;
                    missing = tier;
                    return false;
                }

                case RunTreeNodeKind.Fork:
                case RunTreeNodeKind.WeaponSeal:
                case RunTreeNodeKind.Hearth:
                    if (CountOwned(n.Lane, RunTreeNodeKind.BranchKeystone) >= 1) return true;
                    missing = CheapestUnowned(n.Lane, RunTreeNodeKind.BranchKeystone, build);
                    return false;

                case RunTreeNodeKind.Capstone:
                {
                    if (n.Lane == RunTreeLane.Weapon && Graph.WeaponSealIndex >= 0 && !_owned[Graph.WeaponSealIndex])
                    {
                        missing = Graph.WeaponSealIndex;
                        return false;
                    }

                    if (CountOwned(n.Lane, RunTreeNodeKind.BranchKeystone) < 2)
                    {
                        missing = CheapestUnowned(n.Lane, RunTreeNodeKind.BranchKeystone, build);
                        return false;
                    }

                    if (CountOwned(n.Lane, RunTreeNodeKind.Fork) < 1)
                    {
                        missing = CheapestUnowned(n.Lane, RunTreeNodeKind.Fork, build);
                        return false;
                    }

                    return true;
                }

                case RunTreeNodeKind.SpecialLink:
                    if (string.IsNullOrEmpty(LinkedSpecial)) return false;
                    if (n.LinkLevel >= 2 && Graph.LinkIIndex >= 0 && !_owned[Graph.LinkIIndex])
                    {
                        missing = Graph.LinkIIndex;
                        return false;
                    }

                    return true;
            }

            return false;
        }

        /// <summary>The node's effect id; Special links resolve through the linked Special (catalog), else empty.</summary>
        public string EffectOf(int i)
        {
            RunTreeNode n = Graph[i];
            if (n.Kind != RunTreeNodeKind.SpecialLink) return n.EffectId;
            RunTreeCatalogNode c = string.IsNullOrEmpty(LinkedSpecial) ? null : RunTreeCatalog.FindLink(LinkedSpecial, n.LinkLevel);
            return c != null ? c.effectId : string.Empty;
        }

        /// <summary>The seven-state status (S-09) with its lock reason and the NEEDS / CLOSED BY partners.</summary>
        public RunTreeNodeStatus Status(int i, WeaponModifierState build, int gold, out RunTreeLockReason reason, out int missing, out int closedBy)
        {
            reason = RunTreeLockReason.None;
            missing = -1;
            closedBy = -1;
            RunTreeNode n = Graph[i];

            if (n.Kind == RunTreeNodeKind.Tier)
            {
                int owned = OwnedTier(i, build);
                UpgradeEligibility e = TierEligibility != null ? TierEligibility(n.Branch, owned) : (owned >= n.MaxTier ? UpgradeEligibility.Maxed : UpgradeEligibility.Eligible);
                switch (e)
                {
                    case UpgradeEligibility.Maxed: return RunTreeNodeStatus.MaxTier;
                    case UpgradeEligibility.Eligible: return Cost(i, build) <= gold ? RunTreeNodeStatus.Available : RunTreeNodeStatus.Unaffordable;
                    case UpgradeEligibility.Locked:
                        reason = RunTreeLockReason.ProfileLocked;
                        return RunTreeNodeStatus.Locked;
                    case UpgradeEligibility.NoLiveConsumer:
                        reason = RunTreeLockReason.NotOnThisWeapon;
                        return RunTreeNodeStatus.Locked;
                    default:
                        reason = RunTreeLockReason.EffectPending;
                        return RunTreeNodeStatus.Locked;
                }
            }

            if (n.Kind == RunTreeNodeKind.SpecialLink && string.IsNullOrEmpty(LinkedSpecial))
            {
                reason = RunTreeLockReason.NoSpecialYet;
                return RunTreeNodeStatus.Locked;
            }

            if (!RunTreeEffects.IsLive(EffectOf(i)))
            {
                reason = RunTreeLockReason.EffectPending;
                return RunTreeNodeStatus.Locked;
            }

            if (n.Kind == RunTreeNodeKind.Hearth)
            {
                if (HearthStacks >= n.MaxTier) return RunTreeNodeStatus.MaxTier;
            }
            else if (_owned[i])
            {
                return RunTreeNodeStatus.Purchased;
            }

            closedBy = ExcludedBy(i);
            if (closedBy >= 0) return RunTreeNodeStatus.Excluded;
            if (!PrereqMet(i, build, out missing)) return RunTreeNodeStatus.PrereqMissing;
            return Cost(i, build) <= gold ? RunTreeNodeStatus.Available : RunTreeNodeStatus.Unaffordable;
        }

        /// <summary>
        /// In the frontier and drawable as a card: prerequisites met, not maxed/owned/excluded, effect live,
        /// not Hearth (bought directly). Price is not a criterion (an unaffordable card shows disabled, DEC-049).
        /// </summary>
        public bool IsOfferable(int i, WeaponModifierState build)
        {
            if (Graph[i].Kind == RunTreeNodeKind.Hearth) return false;
            if (Graph[i].Kind == RunTreeNodeKind.Capstone && !Meta.CapstoneMasteryMet) return false;   // K6: only the pin brings it before M3
            RunTreeNodeStatus s = Status(i, build, int.MaxValue, out _, out _, out _);
            return s == RunTreeNodeStatus.Available;
        }

        // ------------------------------------------------------------------ pin (Nişan)

        public void SetPin(int i) => PinnedIndex = i >= 0 && IsBuilt && i < Graph.Count ? i : -1;

        /// <summary>
        /// The nodes still to buy on the way to <paramref name="target"/>, in buying order, ending with the
        /// target (owned steps skipped). Fills <paramref name="into"/>; returns its count. Cheapest partners
        /// are chosen where the rule allows a choice (any keystone, any fork).
        /// </summary>
        public int PathTo(int target, WeaponModifierState build, List<int> into)
        {
            into.Clear();
            if (!IsBuilt || target < 0 || target >= Graph.Count) return 0;
            AppendPath(target, build, into, 0);
            return into.Count;
        }

        /// <summary>The first node on the pin's path that is drawable now (the one the Nişan guarantees), else -1.</summary>
        public int PinnedOfferable(WeaponModifierState build)
        {
            if (PinnedIndex < 0) return -1;
            PathTo(PinnedIndex, build, _scratch);
            for (int k = 0; k < _scratch.Count; k++)
            {
                if (IsOfferable(_scratch[k], build)) return _scratch[k];
            }

            return -1;
        }

        private void AppendPath(int i, WeaponModifierState build, List<int> into, int depth)
        {
            if (depth > 8 || into.Contains(i)) return;
            RunTreeNode n = Graph[i];
            bool done = n.Kind == RunTreeNodeKind.Tier ? false : n.Kind == RunTreeNodeKind.Hearth ? HearthStacks >= n.MaxTier : _owned[i];
            if (done) return;

            switch (n.Kind)
            {
                case RunTreeNodeKind.BranchKeystone:
                    if (n.Parents.Length > 0 && OwnedTier(n.Parents[0], build) < 2) AppendPath(n.Parents[0], build, into, depth + 1);
                    break;
                case RunTreeNodeKind.Fork:
                case RunTreeNodeKind.WeaponSeal:
                case RunTreeNodeKind.Hearth:
                    if (CountOwned(n.Lane, RunTreeNodeKind.BranchKeystone) < 1)
                    {
                        int k = CheapestUnowned(n.Lane, RunTreeNodeKind.BranchKeystone, build);
                        if (k >= 0) AppendPath(k, build, into, depth + 1);
                    }
                    break;
                case RunTreeNodeKind.Capstone:
                    if (n.Lane == RunTreeLane.Weapon && Graph.WeaponSealIndex >= 0 && !_owned[Graph.WeaponSealIndex]) AppendPath(Graph.WeaponSealIndex, build, into, depth + 1);
                    for (int need = 2 - CountOwned(n.Lane, RunTreeNodeKind.BranchKeystone) - CountIn(into, n.Lane, RunTreeNodeKind.BranchKeystone); need > 0; need--)
                    {
                        int k = CheapestUnowned(n.Lane, RunTreeNodeKind.BranchKeystone, build, into);
                        if (k < 0) break;
                        AppendPath(k, build, into, depth + 1);
                    }
                    if (CountOwned(n.Lane, RunTreeNodeKind.Fork) + CountIn(into, n.Lane, RunTreeNodeKind.Fork) < 1)
                    {
                        int f = CheapestUnowned(n.Lane, RunTreeNodeKind.Fork, build, into);
                        if (f >= 0) AppendPath(f, build, into, depth + 1);
                    }
                    break;
                case RunTreeNodeKind.SpecialLink:
                    if (n.LinkLevel >= 2 && Graph.LinkIIndex >= 0 && !_owned[Graph.LinkIIndex]) AppendPath(Graph.LinkIIndex, build, into, depth + 1);
                    break;
            }

            if (!into.Contains(i)) into.Add(i);
        }

        // ------------------------------------------------------------------ commands (the shop / screen call these)

        /// <summary>Records a card purchase of a non-tier node (the shop already took the gold). Tier nodes go through the build.</summary>
        public bool MarkOwned(int i)
        {
            if (!IsBuilt || i < 0 || i >= Graph.Count) return false;
            RunTreeNode n = Graph[i];
            if (n.Kind == RunTreeNodeKind.Tier || n.Kind == RunTreeNodeKind.Hearth || _owned[i]) return false;
            _owned[i] = true;
            if (PinnedIndex == i) PinnedIndex = -1;
            return true;
        }

        /// <summary>
        /// WP-077 Hearth ("Ocak", K3): a direct buy outside the card limit — prerequisite a keystone in the
        /// weapon lane, ≤ perRound this round, ≤ maxStacks in the run, price 50 + 30·n. Takes the gold once.
        /// </summary>
        public RunTreeCommandResult TryStokeHearth(GoldWallet wallet, WeaponModifierState build)
        {
            if (!IsBuilt || Graph.HearthIndex < 0) return RunTreeCommandResult.UnknownNode;
            if (wallet == null || !wallet.IsOpen) return RunTreeCommandResult.NoRun;
            int i = Graph.HearthIndex;
            RunTreeNode n = Graph[i];
            if (HearthStacks >= n.MaxTier || HearthThisRound >= n.PerRoundLimit) return RunTreeCommandResult.LimitReached;
            RunTreeNodeStatus s = Status(i, build, wallet.Balance, out _, out _, out _);
            if (s == RunTreeNodeStatus.Unaffordable) return RunTreeCommandResult.InsufficientGold;
            if (s != RunTreeNodeStatus.Available) return RunTreeCommandResult.NotAvailable;

            int cost = Cost(i, build);
            if (wallet.TrySpend(cost, GoldSource.TreeHearth, wallet.NextTransactionId()) != GoldTransactionResult.Applied)
            {
                return RunTreeCommandResult.WalletRefused;
            }

            HearthStacks++;
            HearthThisRound++;
            if (build != null) build.SetHearth(HearthStacks, n.EffectValue);
            if (PinnedIndex == i && HearthStacks >= n.MaxTier) PinnedIndex = -1;
            return RunTreeCommandResult.Done;
        }

        /// <summary>
        /// Round-end bookkeeping, called when the round-end offer opens: pays the Purse bonus on the gold
        /// picked up since the last round end and opens the next round's Hearth allowance.
        /// </summary>
        public int OnRoundEnd(GoldWallet wallet)
        {
            HearthThisRound = 0;
            if (!IsBuilt || wallet == null || !wallet.IsOpen) return 0;

            int earned = wallet.TotalEarned - _earnedMark;
            int bonus = 0;
            if (Graph.PurseIndex >= 0 && _owned[Graph.PurseIndex] && earned > 0)
            {
                float share = Graph[Graph.PurseIndex].EffectValue;
                bonus = Mathf.FloorToInt(earned * share);
                if (bonus > 0 && wallet.Credit(bonus, GoldSource.PurseBonus, wallet.NextTransactionId()) == GoldTransactionResult.Applied)
                {
                    PurseBonusPaid += bonus;
                }
                else
                {
                    bonus = 0;
                }
            }

            _earnedMark = wallet.TotalEarned;
            return bonus;
        }

        /// <summary>The first Special picked up in the run names the two link slots (WP-069 keys sw_arc / sw_gel).</summary>
        public void OnSpecialPickedUp(string specialKey)
        {
            if (string.IsNullOrEmpty(LinkedSpecial) && !string.IsNullOrEmpty(specialKey))
            {
                LinkedSpecial = specialKey;
            }
        }

        // ------------------------------------------------------------------ helpers

        private int CountOwned(RunTreeLane lane, RunTreeNodeKind kind)
        {
            int c = 0;
            for (int j = 0; j < Graph.Count; j++)
            {
                if (_owned[j] && Graph[j].Lane == lane && Graph[j].Kind == kind) c++;
            }

            return c;
        }

        private int CountIn(List<int> list, RunTreeLane lane, RunTreeNodeKind kind)
        {
            int c = 0;
            for (int k = 0; k < list.Count; k++)
            {
                RunTreeNode n = Graph[list[k]];
                if (n.Lane == lane && n.Kind == kind) c++;
            }

            return c;
        }

        private int CheapestUnowned(RunTreeLane lane, RunTreeNodeKind kind, WeaponModifierState build, List<int> skip = null)
        {
            int best = -1, bestCost = int.MaxValue;
            for (int j = 0; j < Graph.Count; j++)
            {
                RunTreeNode n = Graph[j];
                if (n.Lane != lane || n.Kind != kind || _owned[j] || (skip != null && skip.Contains(j)) || ExcludedBy(j) >= 0) continue;
                if (!RunTreeEffects.IsLive(EffectOf(j))) continue;
                int cost = Cost(j, build);
                if (n.Kind == RunTreeNodeKind.BranchKeystone && n.Parents.Length > 0)
                {
                    // Include what is still to pay on the tier the keystone needs.
                    int tier = n.Parents[0];
                    int have = OwnedTier(tier, build);
                    if (have < 2 && TierEligibility != null && TierEligibility(Graph[tier].Branch, have) != UpgradeEligibility.Eligible) continue;
                    cost += have < 2 ? (2 - have) * Cost(tier, build) : 0;
                }

                if (cost < bestCost)
                {
                    best = j;
                    bestCost = cost;
                }
            }

            return best;
        }
    }
}
