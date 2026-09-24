using DD.Prototype.Rounds;

namespace DD.Prototype.Profile
{
    /// <summary>Result of a meta PATH command (UI → S-04). The PATH pane maps each to a notice.</summary>
    public enum MetaCommandResult
    {
        Done = 0,
        UnknownNode = 1,
        NotAvailable = 2,
        InsufficientAsh = 3,
        AlreadyOwned = 4,
        NothingToRefund = 5,
        SaveLocked = 6,
        SaveFailed = 7
    }

    public static partial class ProfileService
    {
        /// <summary>
        /// WP-077 B2b: buy a meta PATH node with Ash. Atomic like a run transaction (WP-063): a candidate
        /// clone is changed, validated and saved; only a successful save replaces the live profile, so a
        /// failed write leaves RAM = disk. Buying an owned node is <see cref="MetaCommandResult.AlreadyOwned"/>
        /// (idempotent). A weapon node also grants the weapon (UnlockedWeapons, WP-069 key).
        /// The screen confirms with HoldConfirm 500 ms (S-09) before calling this.
        /// </summary>
        public static MetaCommandResult TryBuyMetaNode(string nodeId)
        {
            EnsureLoaded();
            int i = MetaPathCatalog.IndexOf(nodeId);
            if (i < 0) return MetaCommandResult.UnknownNode;
            MetaPathNode n = MetaPathCatalog.Nodes[i];
            if (MetaPathRules.IsOwned(s_data, n)) return MetaCommandResult.AlreadyOwned;

            RunTreeNodeStatus status = MetaPathRules.Status(s_data, i, out _, out _, out _);
            if (status == RunTreeNodeStatus.Unaffordable) return MetaCommandResult.InsufficientAsh;
            if (status != RunTreeNodeStatus.Available) return MetaCommandResult.NotAvailable;
            if (ProfileStore.SaveLocked) return MetaCommandResult.SaveLocked;

            ProfileData candidate = s_data.Clone();
            candidate.Grant(candidate.MetaNodes, n.Id);
            candidate.AshSpentTotal = ProfileProgression.SaturatingAdd(candidate.AshSpentTotal, n.AshCost);
            candidate.AshBalance = candidate.AshEarnedTotal - candidate.AshSpentTotal;
            if (n.Kind == MetaNodeKind.Weapon) candidate.Grant(candidate.UnlockedWeapons, n.WeaponKey);
            if (candidate.MetaPinnedNode == n.Id) candidate.MetaPinnedNode = string.Empty;
            return CommitMeta(candidate);
        }

        /// <summary>
        /// WP-077 B2b respec (§6, K10): every Special leaning and passive is returned; weapons stay (earned
        /// content never re-locks). The first respec refunds 100 %, later ones 75 % (25 % Ash sink).
        /// </summary>
        public static MetaCommandResult TryMetaRespec(out int refunded)
        {
            EnsureLoaded();
            refunded = 0;
            bool any = false;
            foreach (string id in s_data.MetaNodes)
            {
                MetaPathNode n = MetaPathCatalog.Find(id);
                if (n != null && n.Kind != MetaNodeKind.Weapon) { any = true; break; }
            }

            if (!any) return MetaCommandResult.NothingToRefund;
            if (ProfileStore.SaveLocked) return MetaCommandResult.SaveLocked;

            int refund = MetaPathRules.RespecRefund(s_data);
            ProfileData candidate = s_data.Clone();
            candidate.MetaNodes.RemoveAll(id =>
            {
                MetaPathNode n = MetaPathCatalog.Find(id);
                return n != null && n.Kind != MetaNodeKind.Weapon;
            });
            candidate.AshSpentTotal = System.Math.Max(0, candidate.AshSpentTotal - refund);
            candidate.AshBalance = candidate.AshEarnedTotal - candidate.AshSpentTotal;
            candidate.MetaRespecCount = ProfileProgression.SaturatingAdd(candidate.MetaRespecCount, 1);
            MetaCommandResult r = CommitMeta(candidate);
            if (r == MetaCommandResult.Done) refunded = refund;
            return r;
        }

        /// <summary>WP-077 B2b: pins a meta node for the NEXT strip (null/empty clears). A preference write, same atomic path.</summary>
        public static MetaCommandResult SetMetaPin(string nodeId)
        {
            EnsureLoaded();
            string value = nodeId ?? string.Empty;
            if (value.Length > 0 && MetaPathCatalog.IndexOf(value) < 0) return MetaCommandResult.UnknownNode;
            if (s_data.MetaPinnedNode == value) return MetaCommandResult.Done;
            if (ProfileStore.SaveLocked) return MetaCommandResult.SaveLocked;
            ProfileData candidate = s_data.Clone();
            candidate.MetaPinnedNode = value;
            return CommitMeta(candidate);
        }

        /// <summary>WP-077 B2b: the PATH pane was seen with this many owned nodes (clears the menu "new" chip).</summary>
        public static void MarkMetaSeen()
        {
            EnsureLoaded();
            if (s_data.MetaSeen == s_data.MetaNodes.Count || ProfileStore.SaveLocked) return;
            ProfileData candidate = s_data.Clone();
            candidate.MetaSeen = candidate.MetaNodes.Count;
            CommitMeta(candidate);
        }

        private static MetaCommandResult CommitMeta(ProfileData candidate)
        {
            candidate.Sanitize();
            if (!candidate.IsValid()) return MetaCommandResult.NotAvailable;
            if (!ProfileStore.Save(candidate).Success) return MetaCommandResult.SaveFailed;
            s_data = candidate;
            RaiseChanged();
            return MetaCommandResult.Done;
        }
    }
}
