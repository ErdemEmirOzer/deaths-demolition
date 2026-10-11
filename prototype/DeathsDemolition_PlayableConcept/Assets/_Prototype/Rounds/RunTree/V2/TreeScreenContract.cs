namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B0 · one node as the S-09 tree screen reads it (AD §5: horizontal emaki, 7 branch rows × tiers I–V + synergy column,
    /// 400 px detail panel). Refreshed in place by B1, no allocation; strings and arrays are shared catalog data (read-only).
    /// </summary>
    public struct TreeNodeView
    {
        /// <summary>Catalog id, e.g. "sh_double" (design §2).</summary>
        public string NodeId;
        /// <summary>Localisation stem: "&lt;stem&gt;.name" / ".desc" in the R4 string pack (EN + TR).</summary>
        public string StringKey;
        /// <summary>AP-042 icon key, "Tree.&lt;NodeId&gt;".</summary>
        public string IconKey;
        public TreeBranch Branch;
        /// <summary>1–5 (I–V): the column.</summary>
        public int Tier;
        /// <summary>Slot inside the tier cell (0-based, top to bottom) when a branch has several nodes of one tier.</summary>
        public int Slot;
        /// <summary>Element sub-line (Element branch only; None elsewhere).</summary>
        public TreeElement Element;
        /// <summary>Indices into <see cref="TreeScreenSnapshot.Nodes"/>.</summary>
        public int[] ParentIndices;
        /// <summary>True when ANY listed parent suffices (Elemental Fusion: any two element II; Critical Mass: nuke OR hole).</summary>
        public bool ParentsAny;
        /// <summary>Fork id: 0 none · 1 F1 element cap · 2 F2 chaos core · 10–16 a branch capstone and its awakening alternative.</summary>
        public int ForkGroup;
        public TreeNodeState State;
        /// <summary>Round the node's tier opens in the current mode (fog line).</summary>
        public int RoundGate;
        /// <summary>Catalog price.</summary>
        public int Cost;
        /// <summary>Price now (recommendation strip −10 %, mode, awakening). Equal to <see cref="Cost"/> when none applies.</summary>
        public int Price;
        /// <summary>Gold short when Unaffordable, else 0.</summary>
        public int Shortfall;
        /// <summary>On this round's recommendation strip (3 nodes, −10 %).</summary>
        public bool Recommended;
        /// <summary>The player's Target (old pin): this node or a node on its path.</summary>
        public bool IsTarget;
        public bool OnTargetPath;
        /// <summary>Became buyable since the previous refresh (glint once).</summary>
        public bool NewlyAvailable;
        /// <summary>The capstone slot is held by an awakening alternative (meta, Ash).</summary>
        public bool IsAwakeningVariant;
        /// <summary>Synergies this node takes part in (badge preview in the detail panel).</summary>
        public TreeSynergy Synergies;
        /// <summary>Detail panel placeholders in display units, parallel arrays (shared, read-only).</summary>
        public string[] ArgNames;
        public float[] ArgValues;
        /// <summary>WP-104 B5a (S-09): AP-042 v002 file of the icon (atis_III_triple_shot.png); <see cref="IconKey"/> stays "Tree.&lt;NodeId&gt;".</summary>
        public string IconFile;
        /// <summary>Excluded: index of the owned node that closed it ("CLOSED BY …"), −1 otherwise.</summary>
        public int ClosedByIndex;
    }

    /// <summary>One synergy on the synergy column / detail panel.</summary>
    public struct TreeSynergyView
    {
        public TreeSynergy Synergy;
        public string StringKey;
        /// <summary>AP-042 badge key ("Tree.Synergy.&lt;name&gt;"): two-colour ensō + two emblems + centre hanko (AD X2).</summary>
        public string BadgeKey;
        /// <summary>The two branches whose colours the badge carries.</summary>
        public TreeBranch BranchA;
        public TreeBranch BranchB;
        /// <summary>Indices into <see cref="TreeScreenSnapshot.Nodes"/> of the two required nodes.</summary>
        public int NodeA;
        public int NodeB;
        /// <summary>Barrage also needs rl_1 in hand.</summary>
        public bool NeedsRocketInHand;
        public bool Active;
        /// <summary>Hidden until one of its nodes is owned (unless the Synergy Codex awakening is owned).</summary>
        public bool Revealed;
        /// <summary>WP-104 B5a (S-09): placeholders in display units ({every} {parts} …), shared, read-only.</summary>
        public string[] ArgNames;
        public float[] ArgValues;
        /// <summary>AP-042 v002 badge file (sinerji_barrage.png); <see cref="BadgeKey"/> stays "Tree.Synergy.&lt;name&gt;".</summary>
        public string BadgeFile;
    }

    /// <summary>
    /// WP-104 → S-09 data surface of the round-end tree (decision 1: free tree + recommendation strip + Target, one key to
    /// continue). The screen owns one instance; B1's shop fills it on open and after every command. Arrays are allocated once.
    /// </summary>
    public sealed class TreeScreenSnapshot
    {
        public TreeNodeView[] Nodes = new TreeNodeView[0];
        public TreeSynergyView[] Synergies = new TreeSynergyView[0];
        public int Gold;
        public int Round;
        /// <summary><see cref="TreeMode"/>.</summary>
        public int Mode;
        /// <summary>Highest tier whose gate is open this round (columns beyond are under the fog).</summary>
        public int OpenTier;
        /// <summary>Round the next tier opens (0 when all open).</summary>
        public int NextTierRound;
        public int PowerScore;
        public int AuraLevel;
        /// <summary>Index of the Target node, −1 when none.</summary>
        public int TargetIndex = -1;
        /// <summary>Gold still needed to reach the Target along its path (the "remaining gold" bar).</summary>
        public int TargetGoldRemaining;
        public int RerollCost;
        public bool RerollAvailable;
        public int HearthCost;
        public int HearthStokesThisRound;
        public int HearthStokesTotal;
        public bool HearthAvailable;
        /// <summary>Elements picked so far (F1) and the cap (2, 3 with Third Affinity).</summary>
        public TreeElement ElementsPicked;
        public int ElementCap = 2;
        /// <summary>The weapon in hand, for previews ("on your rl_1 this becomes …").</summary>
        public TreeWeaponProfile Weapon;
        /// <summary>WP-104 B5a (S-09): Hearth caps ("HEARTH n/20", "THIS ROUND n/3").</summary>
        public int HearthPerRound;
        public int HearthMax;
    }

    /// <summary>Commands the tree screen sends (B1's round-end shop implements it). Every call refreshes the snapshot.</summary>
    public interface ITreeShopCommands
    {
        TreeScreenSnapshot Snapshot { get; }
        bool TryBuy(int nodeIndex);
        /// <summary>−1 clears the Target.</summary>
        void SetTarget(int nodeIndex);
        bool TryReroll();
        bool TryStokeHearth();
        /// <summary>Closes the tree's own round-end state (decision 1: one key). It does NOT end the round-end phase: the screen
        /// still calls RunDirector.CloseUpgradeScreen() as today (WP-104 B5a answer to S-09).</summary>
        void Continue();
    }
}
