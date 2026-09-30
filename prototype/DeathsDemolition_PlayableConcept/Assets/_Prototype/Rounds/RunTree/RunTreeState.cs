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
        private readonly List<int> _frontier = new List<int>(32);

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
        private int[] _unseen = new int[0];

        /// <summary>WP-077 B6 telemetry: the drawable cards (a fork pair counts once) of the last DrawFrontier.</summary>
        public int LastFrontierSize { get; private set; }

        /// <summary>WP-077 B6 telemetry: offered keystones / seals / capstones the luck guard (§2.5) doubled.</summary>
        public int PityCount(List<int> offered)
        {
            int n = 0;
            if (!IsBuilt || offered == null) return 0;
            for (int k = 0; k < offered.Count; k++)
            {
                int i = offered[k];
                if (i >= 0 && i < _unseen.Length && _unseen[i] >= PityRounds) n++;
            }

            return n;
        }

        /// <summary>§2.5 luck guard: a keystone / seal / capstone left out of this many offers in a row draws at double weight.</summary>
        public const int PityRounds = 3;

        /// <summary>K10 Reforge ("Yeniden Dövme"): the earliest round and the one use per run.</summary>
        public const int ReforgeFromRound = 5;

        /// <summary>K10b: the owned nodes' effects in runtime units, rebuilt on every ownership change (hooks read it).</summary>
        public RunTreeEffectValues Values { get; } = new RunTreeEffectValues();

        private string[] _link1Names = Array.Empty<string>(), _link2Names = Array.Empty<string>();
        private float[] _link1Values = Array.Empty<float>(), _link2Values = Array.Empty<float>();

        /// <summary>K10: true once the run's one Reforge is spent.</summary>
        public bool ReforgeUsed { get; private set; }
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
            _unseen = new int[Graph.Count];
            ReforgeUsed = false;
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

            _link1Names = _link2Names = Array.Empty<string>();
            _link1Values = _link2Values = Array.Empty<float>();
            RecomputeValues();
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

        /// <summary>Gold price of the node's next step (tier price with the DEC-070 opening clamp; Hearth 50 + 30·n; a Special link the linked Special's catalog price).</summary>
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
                case RunTreeNodeKind.SpecialLink:
                {
                    // WP-077 B5: the link slots are generic (no catalog price of their own); the Special picked up first names them.
                    RunTreeCatalogNode c = LinkCatalogOf(n);
                    return Math.Max(1, c != null ? c.cost : n.FlatCost);
                }
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
                    if (Meta.ArmorBreakKeystoneOpen && n.Branch == UpgradeBranch.ArmorBreak && n.Lane == RunTreeLane.Weapon) return true;
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

        /// <summary>R1 content pack key stem of a node; Special links resolve through the linked Special (Raiju / Namekuji), else empty.</summary>
        public string StringKeyOf(int i)
        {
            RunTreeNode n = Graph[i];
            if (n.Kind != RunTreeNodeKind.SpecialLink) return n.StringKey;
            if (LinkedSpecial == "sw_arc") return "tree.run.link.raiju." + n.LinkLevel;
            if (LinkedSpecial == "sw_gel") return "tree.run.link.namekuji." + n.LinkLevel;
            return string.Empty;
        }

        /// <summary>
        /// WP-077: true when the run owns a node whose effect is <paramref name="effectId"/> (Special links resolve
        /// through the linked Special). The gameplay hooks' question ("is Conductor on?"); no allocation.
        /// </summary>
        public bool OwnsEffect(string effectId)
        {
            if (!IsBuilt || string.IsNullOrEmpty(effectId)) return false;
            for (int i = 0; i < Graph.Count; i++)
            {
                if (_owned[i] && string.Equals(EffectOf(i), effectId, StringComparison.Ordinal)) return true;
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
            if (Graph[i].Kind == RunTreeNodeKind.Capstone && !Meta.CapstoneMasteryMet && i != PinnedIndex) return false;   // K6 (DEC-098): before M1 only the pin brings it
            RunTreeNodeStatus s = Status(i, build, int.MaxValue, out _, out _, out _);
            return s == RunTreeNodeStatus.Available;
        }

        // ------------------------------------------------------------------ pin (Nişan)

        /// <summary>
        /// M-35: a negative index clears the pin; a target is taken only when it is structurally valid (in range, never Hearth),
        /// otherwise the call is ignored and the current pin stays. The reachability rule is <see cref="CanPin"/> (the shop's gate).
        /// </summary>
        public void SetPin(int i)
        {
            if (i < 0 || !IsBuilt) { PinnedIndex = -1; return; }
            if (i < Graph.Count && Graph[i].Kind != RunTreeNodeKind.Hearth) PinnedIndex = i;
        }

        /// <summary>A pin may plan future prerequisites, but never a closed or unbound destination. Gold is not a gate.</summary>
        public bool CanPin(int i, WeaponModifierState build)
        {
            if (!IsBuilt || i < 0 || i >= Graph.Count || Graph[i].Kind == RunTreeNodeKind.Hearth) return false;
            RunTreeNodeStatus s = Status(i, build, int.MaxValue, out _, out _, out _);
            if (s != RunTreeNodeStatus.Available && s != RunTreeNodeStatus.Unaffordable && s != RunTreeNodeStatus.PrereqMissing) return false;
            return CanReach(i, build, 0);
        }

        private bool CanReach(int i, WeaponModifierState build, int depth)
        {
            if (i < 0 || i >= Graph.Count || depth > 8) return false;
            RunTreeNode n = Graph[i];
            if (n.Kind != RunTreeNodeKind.Tier && _owned[i]) return true;
            RunTreeNodeStatus s = Status(i, build, int.MaxValue, out _, out _, out _);
            if (s == RunTreeNodeStatus.Available || s == RunTreeNodeStatus.Unaffordable) return true;
            if (s != RunTreeNodeStatus.PrereqMissing) return false;

            switch (n.Kind)
            {
                case RunTreeNodeKind.BranchKeystone:
                    return n.Parents.Length > 0 && Graph[n.Parents[0]].MaxTier >= 2 && CanReach(n.Parents[0], build, depth + 1);
                case RunTreeNodeKind.Fork:
                case RunTreeNodeKind.WeaponSeal:
                    return ReachableCount(n.Lane, RunTreeNodeKind.BranchKeystone, build, depth + 1) >= 1;
                case RunTreeNodeKind.Capstone:
                    return (n.Lane != RunTreeLane.Weapon || (Graph.WeaponSealIndex >= 0 && CanReach(Graph.WeaponSealIndex, build, depth + 1)))
                        && ReachableCount(n.Lane, RunTreeNodeKind.BranchKeystone, build, depth + 1) >= 2
                        && ReachableCount(n.Lane, RunTreeNodeKind.Fork, build, depth + 1) >= 1;
                case RunTreeNodeKind.SpecialLink:
                    return n.LinkLevel >= 2 && Graph.LinkIIndex >= 0 && CanReach(Graph.LinkIIndex, build, depth + 1);
                default:
                    return false;
            }
        }

        private int ReachableCount(RunTreeLane lane, RunTreeNodeKind kind, WeaponModifierState build, int depth)
        {
            int count = 0;
            for (int j = 0; j < Graph.Count; j++)
                if (Graph[j].Lane == lane && Graph[j].Kind == kind && CanReach(j, build, depth)) count++;
            return count;
        }

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
            if (!CanPin(PinnedIndex, build)) return -1;
            PathTo(PinnedIndex, build, _scratch);
            for (int k = 0; k < _scratch.Count; k++)
            {
                if (IsOfferable(_scratch[k], build)) return _scratch[k];
            }

            return -1;
        }

        /// <summary>
        /// WP-077 B4 (full_lane): lanes whose capstone is owned and that hold a tier at its maximum.
        /// </summary>
        public int FullLaneCount(WeaponModifierState build)
        {
            if (!IsBuilt) return 0;
            int lanes = 0;
            for (int lane = 0; lane < 2; lane++)
            {
                int cap = lane == 0 ? Graph.WeaponCapstoneIndex : Graph.BodyCapstoneIndex;
                if (cap < 0 || !_owned[cap]) continue;
                for (int i = 0; i < Graph.Count; i++)
                {
                    RunTreeNode n = Graph[i];
                    if (n.Kind == RunTreeNodeKind.Tier && (int)n.Lane == lane && OwnedTier(i, build) >= n.MaxTier)
                    {
                        lanes++;
                        break;
                    }
                }
            }

            return lanes;
        }

        /// <summary>
        /// K1 hybrid offer on the tree (WP-077 §2.5): up to <paramref name="count"/> distinct offerable nodes,
        /// drawn without replacement by catalog offer weight; when the draw missed the Nişan's next step, that
        /// step takes the last slot. Deterministic for a seeded random. The K10 node cards and the B3 simulator
        /// (EconomySimulator.TreeModel) share this draw.
        /// </summary>
        public int DrawFrontier(IUpgradeOfferRandom random, WeaponModifierState build, int count, List<int> into) =>
            DrawFrontier(random, build, count, into, null);

        /// <summary>As above, leaving out <paramref name="exclude"/> (the cards a reroll keeps).</summary>
        public int DrawFrontier(IUpgradeOfferRandom random, WeaponModifierState build, int count, List<int> into, List<int> exclude)
        {
            into.Clear();
            if (!IsBuilt || count <= 0) return 0;
            _frontier.Clear();
            float total = 0f;
            for (int i = 0; i < Graph.Count; i++)
            {
                if (!IsOfferable(i, build) || (exclude != null && exclude.Contains(i))) continue;
                int partner = ForkPartner(i);
                if (partner >= 0 && partner < i && IsOfferable(partner, build)) continue;   // §2.5: a fork pair is one card (its lower index)
                _frontier.Add(i);
                total += OfferWeightOf(i);
            }

            LastFrontierSize = _frontier.Count;
            while (into.Count < count && _frontier.Count > 0)
            {
                float r = (random != null ? random.NextValue() : 0f) * total;
                int k = 0;
                for (; k < _frontier.Count - 1; k++)
                {
                    r -= OfferWeightOf(_frontier[k]);
                    if (r < 0f) break;
                }

                into.Add(_frontier[k]);
                total -= OfferWeightOf(_frontier[k]);
                _frontier.RemoveAt(k);
            }

            int pin = PinnedOfferable(build);
            if (pin >= 0 && !ContainsCard(into, pin) && (exclude == null || !ContainsCard(exclude, pin)))
            {
                int card = ForkPartner(pin) >= 0 && ForkPartner(pin) < pin && IsOfferable(ForkPartner(pin), build) ? ForkPartner(pin) : pin;
                if (into.Count < count) into.Add(card);
                else into[into.Count - 1] = card;
            }

            return into.Count;
        }

        /// <summary>The other fork of a node's pair (same lane, same exclusion group), else -1.</summary>
        public int ForkPartner(int i)
        {
            if (!IsBuilt || i < 0 || i >= Graph.Count) return -1;
            RunTreeNode n = Graph[i];
            if (n.Kind != RunTreeNodeKind.Fork || n.ExclusionGroup == 0) return -1;
            for (int j = 0; j < Graph.Count; j++)
            {
                if (j != i && Graph[j].Kind == RunTreeNodeKind.Fork && Graph[j].ExclusionGroup == n.ExclusionGroup) return j;
            }

            return -1;
        }

        private bool ContainsCard(List<int> cards, int node)
        {
            if (cards.Contains(node)) return true;
            int partner = ForkPartner(node);
            return partner >= 0 && cards.Contains(partner);
        }

        /// <summary>
        /// §2.5 luck guard bookkeeping, once per built offer (not per reroll): every offerable keystone / seal /
        /// capstone that the offer left out counts one more round unseen; one that was shown resets.
        /// </summary>
        public void RecordOffer(List<int> shown, WeaponModifierState build)
        {
            if (!IsBuilt || shown == null) return;
            for (int i = 0; i < Graph.Count; i++)
            {
                RunTreeNodeKind k = Graph[i].Kind;
                if (k != RunTreeNodeKind.BranchKeystone && k != RunTreeNodeKind.WeaponSeal && k != RunTreeNodeKind.Capstone) continue;
                if (shown.Contains(i)) _unseen[i] = 0;
                else if (IsOfferable(i, build)) _unseen[i]++;
            }
        }

        /// <summary>Offers in a row a drawable keystone / seal / capstone has been left out of (luck guard).</summary>
        public int UnseenRounds(int i) => i >= 0 && i < _unseen.Length ? _unseen[i] : 0;

        private float OfferWeightOf(int i)
        {
            RunTreeNode n = Graph[i];
            RunTreeCatalogNode link = n.Kind == RunTreeNodeKind.SpecialLink ? LinkCatalogOf(n) : null;
            float w = Math.Max(0.01f, link != null ? link.offerWeight : n.OfferWeight);
            return _unseen.Length > i && _unseen[i] >= PityRounds ? w * 2f : w;
        }

        // ------------------------------------------------------------------ K10 undo / Reforge

        /// <summary>K10 undo of a non-tier card purchase (the shop refunds the gold). Tier nodes go through the build.</summary>
        public bool Unmark(int i)
        {
            if (!IsBuilt || i < 0 || i >= Graph.Count) return false;
            RunTreeNodeKind k = Graph[i].Kind;
            if (k == RunTreeNodeKind.Tier || k == RunTreeNodeKind.Hearth || !_owned[i]) return false;
            _owned[i] = false;
            RecomputeValues();
            return true;
        }

        /// <summary>K10 undo of the last Hearth stoke (the shop refunds the gold).</summary>
        public bool UnstokeHearth(WeaponModifierState build)
        {
            if (!IsBuilt || build == null || HearthStacks <= 0 || HearthThisRound <= 0) return false;   // M-37: nothing changes without a build
            HearthStacks--;
            HearthThisRound--;
            if (Graph.HearthIndex >= 0) build.SetHearth(HearthStacks, Graph[Graph.HearthIndex].EffectValue);
            return true;
        }

        /// <summary>
        /// K10 Reforge: the owned fork <paramref name="i"/> can be swapped for its partner — once per run, from
        /// round <see cref="ReforgeFromRound"/>, the partner's effect live. Price: the partner's (no refund).
        /// </summary>
        public bool CanReforge(int i, int round)
        {
            int partner = ForkPartner(i);
            return partner >= 0 && !ReforgeUsed && round >= ReforgeFromRound && _owned[i] && !_owned[partner] &&
                   RunTreeEffects.IsLive(EffectOf(partner));
        }

        /// <summary>K10 Reforge bookkeeping after the shop took the partner's price: the pair swaps owner.</summary>
        public bool Reforge(int i, int round)
        {
            if (!CanReforge(i, round)) return false;
            int partner = ForkPartner(i);
            _owned[i] = false;
            _owned[partner] = true;
            ReforgeUsed = true;
            RecomputeValues();
            return true;
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
            RecomputeValues();
            return true;
        }

        /// <summary>
        /// WP-077 Hearth ("Ocak", K3): a direct buy outside the card limit — prerequisite a keystone in the
        /// weapon lane, ≤ perRound this round, ≤ maxStacks in the run, price 50 + 30·n. Takes the gold once.
        /// </summary>
        public RunTreeCommandResult TryStokeHearth(GoldWallet wallet, WeaponModifierState build) => TryStokeHearth(wallet, build, out _);

        /// <summary>As above; <paramref name="transactionId"/> is the wallet spend (K10 undo refunds it).</summary>
        public RunTreeCommandResult TryStokeHearth(GoldWallet wallet, WeaponModifierState build, out int transactionId)
        {
            transactionId = 0;
            if (!IsBuilt || Graph.HearthIndex < 0) return RunTreeCommandResult.UnknownNode;
            if (wallet == null || !wallet.IsOpen) return RunTreeCommandResult.NoRun;
            if (build == null) return RunTreeCommandResult.NotAvailable;
            int i = Graph.HearthIndex;
            RunTreeNode n = Graph[i];
            if (HearthStacks >= n.MaxTier || HearthThisRound >= n.PerRoundLimit) return RunTreeCommandResult.LimitReached;
            RunTreeNodeStatus s = Status(i, build, wallet.Balance, out _, out _, out _);
            if (s == RunTreeNodeStatus.Unaffordable) return RunTreeCommandResult.InsufficientGold;
            if (s != RunTreeNodeStatus.Available) return RunTreeCommandResult.NotAvailable;

            int cost = Cost(i, build);
            int tx = wallet.NextTransactionId();
            if (wallet.TrySpend(cost, GoldSource.TreeHearth, tx) != GoldTransactionResult.Applied)
            {
                return RunTreeCommandResult.WalletRefused;
            }

            transactionId = tx;

            HearthStacks++;
            HearthThisRound++;
            build.SetHearth(HearthStacks, n.EffectValue);
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
                RunTreeArgs.Split(RunTreeCatalog.FindLink(specialKey, 1)?.args, out _link1Names, out _link1Values);
                RunTreeArgs.Split(RunTreeCatalog.FindLink(specialKey, 2)?.args, out _link2Names, out _link2Values);
            }
        }

        /// <summary>K10b: a node's arg names / values (display units); links resolve through the linked Special.</summary>
        public string[] ArgNamesOf(int i)
        {
            RunTreeNode n = Graph[i];
            if (n.Kind != RunTreeNodeKind.SpecialLink) return n.ArgNames;
            return n.LinkLevel >= 2 ? _link2Names : _link1Names;
        }

        public float[] ArgValuesOf(int i)
        {
            RunTreeNode n = Graph[i];
            if (n.Kind != RunTreeNodeKind.SpecialLink) return n.ArgValues;
            return n.LinkLevel >= 2 ? _link2Values : _link1Values;
        }

        /// <summary>Rebuilds <see cref="Values"/> from the owned nodes (called on every ownership change).</summary>
        private void RecomputeValues()
        {
            Values.Reset(Graph != null ? Graph.WeaponSlot : string.Empty);
            if (!IsBuilt) return;
            for (int i = 0; i < Graph.Count; i++)
            {
                if (_owned[i]) Values.Apply(Graph[i]);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The catalog entry of a link slot for the linked Special (price, offer weight), else null.</summary>
        private RunTreeCatalogNode LinkCatalogOf(RunTreeNode n) =>
            string.IsNullOrEmpty(LinkedSpecial) ? null : RunTreeCatalog.FindLink(LinkedSpecial, n.LinkLevel);

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
                    if (have < 2 && Graph[tier].MaxTier < 2) continue;
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
