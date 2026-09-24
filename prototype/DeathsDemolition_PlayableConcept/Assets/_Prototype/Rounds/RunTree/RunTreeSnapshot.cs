using System.Collections.Generic;
using DD.Prototype.Weapons;

namespace DD.Prototype.Rounds
{
    /// <summary>One node as the S-09 tree screen reads it (WP-077 direction §7.3). Refreshed in place, no allocation.</summary>
    public struct RunTreeNodeView
    {
        public string NodeId;
        /// <summary>≤ 12 characters, the label under the seal.</summary>
        public string ShortName;
        /// <summary>Full name, detail panel only.</summary>
        public string DisplayName;
        /// <summary>Detail panel sentence. Empty for tier nodes: those describe themselves from the live tuning (UpgradeCardCatalog.Describe).</summary>
        public string Description;
        public RunTreeLane Lane;
        public int Row;
        public int Column;
        public RunTreeNodeKind Kind;
        /// <summary>Valid for tier and keystone nodes.</summary>
        public DD.Prototype.Core.Config.UpgradeBranch Branch;
        public bool HasBranch;
        /// <summary>Indices into <see cref="RunTreeSnapshot.Nodes"/> (the graph's edges; shared, never mutated).</summary>
        public int[] ParentIndices;
        public int ExclusionGroup;
        public int OwnedTier;
        public int MaxTier;
        public RunTreeNodeStatus Status;
        public RunTreeLockReason LockReason;
        /// <summary>Price of the next step (tier price, Hearth 50 + 30·n).</summary>
        public int Cost;
        /// <summary>Gold short when Unaffordable ("N SHORT"), else 0.</summary>
        public int Shortfall;
        /// <summary>"NEEDS …": a node whose purchase moves this one closer, -1 when none.</summary>
        public int MissingParentIndex;
        /// <summary>"CLOSED BY …": the owned fork partner, -1 when none.</summary>
        public int ClosedByIndex;
        /// <summary>Became Available/Unaffordable since the previous refresh of this run (glint once).</summary>
        public bool NewlyUnlocked;
        public bool IsPinned;
        /// <summary>On the current offer's cards. False for a drawable node = "not in this offer" overlay (K9).</summary>
        public bool InOffer;
    }

    /// <summary>
    /// WP-077 → S-09 data surface for the in-run tree (direction §7.3; S-09 fields + PD additions).
    /// The screen owns one instance and calls <see cref="Refresh"/> when the shop raises OfferChanged
    /// (and on open). Every array is allocated once; a refresh writes in place (0 B per frame).
    /// Commands go through the shop: TryPurchase(card), TryStokeHearth(), SetPin(nodeId), TryReroll.
    /// </summary>
    public sealed class RunTreeSnapshot
    {
        public const int Capacity = 32;
        public const int PathCapacity = 16;

        public readonly RunTreeNodeView[] Nodes = new RunTreeNodeView[Capacity];
        public int Count { get; private set; }

        public int Gold { get; private set; }
        public int PicksUsed { get; private set; }
        public int PicksMax { get; private set; }
        public int HearthStacks { get; private set; }
        public int HearthThisRound { get; private set; }
        public int HearthPerRound { get; private set; }
        public int HearthIndex { get; private set; } = -1;
        public int PinnedIndex { get; private set; } = -1;
        public int PurseBonusThisRound { get; private set; }

        /// <summary>The pin's path (steps still to buy, ending with the pinned node).</summary>
        public readonly int[] PinPath = new int[PathCapacity];
        public int PinPathCount { get; private set; }
        public int PinPathTotalCost { get; private set; }
        /// <summary>The pinned step that is drawable now (the Nişan guarantee), -1 when none.</summary>
        public int PinReachableIndex { get; private set; } = -1;

        /// <summary>K10 is open (C-00): undo-last and fork reforge are not offered yet.</summary>
        public bool UndoAvailable => false;
        public bool ReforgeAvailable => false;

        public int RunId { get; private set; }

        private readonly RunTreeNodeStatus[] _previous = new RunTreeNodeStatus[Capacity];
        private readonly List<int> _path = new List<int>(PathCapacity);
        private int _previousRun = -1;

        public int IndexOf(string nodeId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Nodes[i].NodeId == nodeId) return i;
            }

            return -1;
        }

        /// <summary>The steps still to buy on the way to a node (focus path), written into <paramref name="into"/>; returns the count.</summary>
        public int FocusPath(RoundUpgradeShop shop, int nodeIndex, int[] into)
        {
            if (shop == null || into == null || !shop.Tree.IsBuilt) return 0;
            int n = shop.Tree.PathTo(nodeIndex, ShopBuild(shop), _path);
            int c = 0;
            for (int k = 0; k < n && c < into.Length; k++) into[c++] = _path[k];
            return c;
        }

        public void Refresh(RoundUpgradeShop shop)
        {
            Count = 0;
            if (shop == null || !shop.Tree.IsBuilt)
            {
                return;
            }

            RunTreeState tree = shop.Tree;
            WeaponModifierState build = ShopBuild(shop);
            bool newRun = tree.RunId != _previousRun;

            Gold = shop.Gold;
            PicksUsed = shop.PurchasesThisRound;
            PicksMax = shop.PurchaseLimit;
            HearthStacks = tree.HearthStacks;
            HearthThisRound = tree.HearthThisRound;
            HearthIndex = tree.Graph.HearthIndex;
            HearthPerRound = HearthIndex >= 0 ? tree.Graph[HearthIndex].PerRoundLimit : 0;
            PinnedIndex = tree.PinnedIndex;
            PurseBonusThisRound = shop.PurseBonusThisRound;
            RunId = tree.RunId;

            int count = tree.Graph.Count < Capacity ? tree.Graph.Count : Capacity;
            for (int i = 0; i < count; i++)
            {
                RunTreeNode node = tree.Graph[i];
                RunTreeNodeStatus status = tree.Status(i, build, Gold, out RunTreeLockReason reason, out int missing, out int closedBy);
                int cost = tree.Cost(i, build);

                ref RunTreeNodeView v = ref Nodes[i];
                v.NodeId = node.Id;
                v.ShortName = node.ShortName;
                v.DisplayName = node.DisplayName;
                v.Description = node.Description;
                v.Lane = node.Lane;
                v.Row = node.Row;
                v.Column = node.Column;
                v.Kind = node.Kind;
                v.Branch = node.Branch;
                v.HasBranch = node.HasBranch;
                v.ParentIndices = node.Parents;
                v.ExclusionGroup = node.ExclusionGroup;
                v.OwnedTier = tree.OwnedTier(i, build);
                v.MaxTier = node.MaxTier;
                v.Status = status;
                v.LockReason = reason;
                v.Cost = cost;
                v.Shortfall = status == RunTreeNodeStatus.Unaffordable ? cost - Gold : 0;
                v.MissingParentIndex = missing;
                v.ClosedByIndex = closedBy;
                bool drawable = status == RunTreeNodeStatus.Available || status == RunTreeNodeStatus.Unaffordable;
                v.NewlyUnlocked = !newRun && drawable &&
                                  (_previous[i] == RunTreeNodeStatus.Locked || _previous[i] == RunTreeNodeStatus.PrereqMissing);
                v.IsPinned = i == tree.PinnedIndex;
                v.InOffer = node.Kind == RunTreeNodeKind.Tier && InOffer(shop, node.Branch);
                _previous[i] = status;
            }

            Count = count;
            _previousRun = tree.RunId;

            PinPathCount = 0;
            PinPathTotalCost = 0;
            PinReachableIndex = -1;
            if (tree.PinnedIndex >= 0)
            {
                int n = tree.PathTo(tree.PinnedIndex, build, _path);
                for (int k = 0; k < n && PinPathCount < PathCapacity; k++)
                {
                    PinPath[PinPathCount++] = _path[k];
                    PinPathTotalCost += tree.Cost(_path[k], build);
                }

                PinReachableIndex = tree.PinnedOfferable(build);
            }
        }

        private static bool InOffer(RoundUpgradeShop shop, DD.Prototype.Core.Config.UpgradeBranch branch)
        {
            IReadOnlyList<UpgradeCardOffer> cards = shop.Cards;
            for (int c = 0; c < cards.Count; c++)
            {
                if (cards[c].IsValid && cards[c].Branch == branch) return true;
            }

            return false;
        }

        private static WeaponModifierState ShopBuild(RoundUpgradeShop shop) => shop.BuildForTree;
    }
}
