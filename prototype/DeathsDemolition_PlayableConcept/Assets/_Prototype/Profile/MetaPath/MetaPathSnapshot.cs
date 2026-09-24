using DD.Prototype.Rounds;

namespace DD.Prototype.Profile
{
    /// <summary>One meta PATH node as the S-09 PATH pane reads it (WP-077 §7.3). Refreshed in place.</summary>
    public struct MetaPathNodeView
    {
        public string NodeId;
        public string ShortName;
        public string DisplayName;
        public string Description;
        public MetaPage Page;
        public int Row;
        public int Column;
        public MetaNodeKind Kind;
        /// <summary>Weapon nodes: WP-069 key (icon, Selection).</summary>
        public string WeaponKey;
        public int LevelGate;
        public int AshCost;
        /// <summary>Indices into <see cref="MetaPathSnapshot.Nodes"/> (shared, never mutated).</summary>
        public int[] ParentIndices;
        public int ExclusionGroup;
        public RunTreeNodeStatus Status;
        public MetaLockReason LockReason;
        /// <summary>Ash short when Unaffordable ("N SHORT").</summary>
        public int Shortfall;
        public int MissingParentIndex;
        public int ClosedByIndex;
        public bool IsPinned;
    }

    /// <summary>
    /// WP-077 B2b → S-09: the meta PATH surface (MainMenu PROGRESS › PATH, Results PATH block, Selection
    /// lock line). One instance; <see cref="Refresh"/> on open and on ProfileService.Changed. Arrays
    /// allocated once. Commands: ProfileService.TryBuyMetaNode (after HoldConfirm 500), TryMetaRespec,
    /// SetMetaPin, MarkMetaSeen.
    /// </summary>
    public sealed class MetaPathSnapshot
    {
        public const int Capacity = 64;

        public readonly MetaPathNodeView[] Nodes = new MetaPathNodeView[Capacity];
        public int Count { get; private set; }

        public int AshBalance { get; private set; }
        public int ProfileLevel { get; private set; }
        /// <summary>The NEXT strip: the pinned node, else the cheapest not-owned node whose gate is met (auto). −1 when the PATH is complete.</summary>
        public int NextIndex { get; private set; } = -1;
        public bool NextIsAuto { get; private set; }
        /// <summary>Rough runs to afford NEXT at the last run's Ash (0 = affordable now, −1 = unknown).</summary>
        public int RunsToNext { get; private set; } = -1;
        /// <summary>Owned / total, for "Path k/n".</summary>
        public int Owned { get; private set; }
        public int Total { get; private set; }
        /// <summary>Menu "new" chip: owned nodes the PATH pane has not been seen with.</summary>
        public bool HasNew { get; private set; }
        public int RespecRefund { get; private set; }
        public bool RespecFree { get; private set; }
        /// <summary>Ash the last applied run added (Results "+N").</summary>
        public int LastRunAsh { get; private set; }

        public int IndexOf(string nodeId)
        {
            for (int i = 0; i < Count; i++) if (Nodes[i].NodeId == nodeId) return i;
            return -1;
        }

        public void Refresh()
        {
            ProfileData p = ProfileService.Current;
            var nodes = MetaPathCatalog.Nodes;
            int count = nodes.Count < Capacity ? nodes.Count : Capacity;
            AshBalance = p.AshBalance;
            ProfileLevel = p.ProfileLevel;
            Owned = 0;
            Total = count;
            int pinned = MetaPathCatalog.IndexOf(p.MetaPinnedNode);
            int auto = -1, autoCost = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                MetaPathNode n = nodes[i];
                RunTreeNodeStatus s = MetaPathRules.Status(p, i, out MetaLockReason reason, out int missing, out int closedBy);
                ref MetaPathNodeView v = ref Nodes[i];
                v.NodeId = n.Id; v.ShortName = n.ShortName; v.DisplayName = n.DisplayName; v.Description = n.Description;
                v.Page = n.Page; v.Row = n.Row; v.Column = n.Column; v.Kind = n.Kind; v.WeaponKey = n.WeaponKey;
                v.LevelGate = n.LevelGate; v.AshCost = n.AshCost; v.ParentIndices = n.Parents; v.ExclusionGroup = n.ExclusionGroup;
                v.Status = s; v.LockReason = reason; v.MissingParentIndex = missing; v.ClosedByIndex = closedBy;
                v.Shortfall = s == RunTreeNodeStatus.Unaffordable ? n.AshCost - p.AshBalance : 0;
                v.IsPinned = i == pinned;
                if (s == RunTreeNodeStatus.Purchased) Owned++;
                if ((s == RunTreeNodeStatus.Available || s == RunTreeNodeStatus.Unaffordable) && n.AshCost < autoCost)
                {
                    auto = i;
                    autoCost = n.AshCost;
                }
            }

            Count = count;
            NextIsAuto = pinned < 0 || Nodes[pinned].Status == RunTreeNodeStatus.Purchased;
            NextIndex = NextIsAuto ? auto : pinned;
            RunRewardReport report = ProfileService.LastReport;
            LastRunAsh = report != null && report.Applied ? report.AshEarned : 0;
            if (NextIndex < 0) RunsToNext = -1;
            else
            {
                int need = Nodes[NextIndex].AshCost - p.AshBalance;
                RunsToNext = need <= 0 ? 0 : LastRunAsh > 0 ? (need + LastRunAsh - 1) / LastRunAsh : -1;
            }

            HasNew = p.MetaNodes.Count > p.MetaSeen;
            RespecRefund = MetaPathRules.RespecRefund(p);
            RespecFree = p.MetaRespecCount == 0;
        }

    }
}
