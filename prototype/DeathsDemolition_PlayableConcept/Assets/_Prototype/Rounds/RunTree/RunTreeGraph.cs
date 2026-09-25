using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 (direction §2.2): one run's tree for the equipped weapon — a weapon lane (its three
    /// core branches, their keystones, the fork pair, Hearth, the Special links, the WP-069 weapon
    /// seal and the capstone) and the shared body lane (the four player branches, their keystones,
    /// the body fork pair, Purse and Second Breath). Built once per run; immutable.
    /// <code>
    /// weapon  row0  tier(core0) ─ keystone0 ─ forkX
    ///         row1  tier(core1) ─ keystone1 ─ forkY ── CAPSTONE
    ///         row2  tier(core2) ─ keystone2 ─ Hearth
    ///         row3  link I      ─ link II   ─ seal
    /// body    row0  Move ─ Evade ─ Ash Step
    ///         row1  Health ─ Thick Skin ─ Iron Knee ── SECOND BREATH
    ///         row2  RegenDelay ─ Breath ─ Purse
    ///         row3  RegenRate ─ Blood Step
    /// </code>
    /// </summary>
    public sealed class RunTreeGraph
    {
        public const int LaneRows = 4;
        public const int LaneColumns = 4;
        public const int WeaponForkGroup = 1;
        public const int BodyForkGroup = 2;

        private readonly List<RunTreeNode> _nodes = new List<RunTreeNode>(32);
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(32);

        public ClassicWeaponId Weapon { get; private set; }
        public string WeaponSlot { get; private set; }
        public IReadOnlyList<RunTreeNode> Nodes => _nodes;
        public int Count => _nodes.Count;

        public int HearthIndex { get; private set; } = -1;
        public int PurseIndex { get; private set; } = -1;
        public int WeaponCapstoneIndex { get; private set; } = -1;
        public int BodyCapstoneIndex { get; private set; } = -1;
        public int WeaponSealIndex { get; private set; } = -1;
        public int LinkIIndex { get; private set; } = -1;
        public int LinkIIIndex { get; private set; } = -1;

        public int IndexOf(string id)
        {
            return id != null && _index.TryGetValue(id, out int i) ? i : -1;
        }

        public RunTreeNode this[int i] => _nodes[i];

        /// <summary>Tier node id of a branch ("tier_FireRate").</summary>
        public static string TierId(UpgradeBranch branch) => "tier_" + branch;

        public static RunTreeGraph Build(ClassicWeaponId weapon, PrototypeConfig config)
        {
            var g = new RunTreeGraph { Weapon = weapon, WeaponSlot = RunTreeCatalog.SlotFor(weapon) };
            RunTreeCatalogDocument doc = RunTreeCatalog.Document;

            // ---- weapon lane
            UpgradeBranch[] core = CoreBranchesOf(weapon);
            var keystones = new int[3];
            for (int r = 0; r < 3; r++)
            {
                int tier = g.AddTier(RunTreeLane.Weapon, r, core[r], config);
                keystones[r] = g.AddKeystone(RunTreeLane.Weapon, r, core[r], tier);
            }

            RunTreeCatalogWeapon w = RunTreeCatalog.FindWeapon(g.WeaponSlot);
            if (w != null)
            {
                g.Add(w.forkX, RunTreeLane.Weapon, 0, 2, RunTreeNodeKind.Fork, WeaponForkGroup);
                g.Add(w.forkY, RunTreeLane.Weapon, 1, 2, RunTreeNodeKind.Fork, WeaponForkGroup);
                g.WeaponSealIndex = g.Add(w.seal, RunTreeLane.Weapon, 3, 2, RunTreeNodeKind.WeaponSeal, 0);
                g.WeaponCapstoneIndex = g.Add(w.capstone, RunTreeLane.Weapon, 1, 3, RunTreeNodeKind.Capstone, 0);
            }

            if (doc.hearth != null && !string.IsNullOrEmpty(doc.hearth.id))
            {
                g.HearthIndex = g.Add(doc.hearth, RunTreeLane.Weapon, 2, 2, RunTreeNodeKind.Hearth, 0);
                RunTreeNode h = g._nodes[g.HearthIndex];
                h.CostStep = Math.Max(0, doc.hearth.costStep);
                h.MaxTier = Math.Max(1, doc.hearth.maxStacks);
                h.PerRoundLimit = Math.Max(1, doc.hearth.perRound);
            }

            // Special links: generic slots; the Special picked up first this run names them (RunTreeState).
            g.LinkIIndex = g.AddLinkSlot(1, 0, -1);
            g.LinkIIIndex = g.AddLinkSlot(2, 1, g.LinkIIndex);

            // ---- body lane
            UpgradeBranch[] body = WeaponBranchContract.SharedPlayerBranches;
            for (int r = 0; r < body.Length && r < LaneRows; r++)
            {
                int tier = g.AddTier(RunTreeLane.Body, r, body[r], config);
                g.AddKeystone(RunTreeLane.Body, r, body[r], tier);
            }

            if (doc.bodyForks != null && doc.bodyForks.Length == 2)
            {
                g.Add(doc.bodyForks[0], RunTreeLane.Body, 0, 2, RunTreeNodeKind.Fork, BodyForkGroup);
                g.Add(doc.bodyForks[1], RunTreeLane.Body, 1, 2, RunTreeNodeKind.Fork, BodyForkGroup);
            }

            g.PurseIndex = g.Add(doc.purse, RunTreeLane.Body, 2, 2, RunTreeNodeKind.Purse, 0);
            g.BodyCapstoneIndex = g.Add(doc.bodyCapstone, RunTreeLane.Body, 1, 3, RunTreeNodeKind.Capstone, 0);
            for (int i = 0; i < g._nodes.Count; i++) g._nodes[i].StringKey = StringKeyFor(g._nodes[i], g.WeaponSlot);
            return g;
        }

        /// <summary>R1 content pack key stem of a node (planning/content/R1-CONTENT-PACK.md §2).</summary>
        public static string StringKeyFor(RunTreeNode n, string weaponSlot)
        {
            switch (n.Kind)
            {
                case RunTreeNodeKind.Tier:
                    if (n.Lane == RunTreeLane.Body) return "tree.run.body." + BodyKey(n.Branch);
                    if (n.Branch == UpgradeBranch.Explosion && weaponSlot == "rl_1") return "tree.run.branch.blast";   // WP-069 §6 Blast semantics
                    return "tree.run.branch." + Snake(n.Branch);
                case RunTreeNodeKind.BranchKeystone:
                    if (n.Lane == RunTreeLane.Body) return "tree.run.body.key." + BodyKeystoneKey(n.Branch);
                    return "tree.run.key." + Snake(n.Branch);
                case RunTreeNodeKind.Fork:
                    if (n.Lane == RunTreeLane.Body) return n.Id == "fork_body_x" ? "tree.run.body.fork.ash_step" : "tree.run.body.fork.iron_knee";
                    return "tree.run.fork." + weaponSlot + (n.Id.EndsWith("_x", StringComparison.Ordinal) ? ".x" : ".y");
                case RunTreeNodeKind.WeaponSeal: return "tree.run.seal." + weaponSlot;
                case RunTreeNodeKind.Capstone: return n.Lane == RunTreeLane.Body ? "tree.run.body.cap.second_wind" : "tree.run.cap." + weaponSlot;
                case RunTreeNodeKind.Hearth: return "tree.run.hearth";
                case RunTreeNodeKind.Purse: return "tree.run.body.purse";
                default: return string.Empty;   // links: RunTreeState.StringKeyOf (per Special)
            }
        }

        private static string Snake(UpgradeBranch b)
        {
            string s = b.ToString();
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(s[i]));
            }

            return sb.ToString();
        }

        private static string BodyKey(UpgradeBranch b)
        {
            switch (b)
            {
                case UpgradeBranch.MovementSpeed: return "move";
                case UpgradeBranch.MaximumHealth: return "max_health";
                case UpgradeBranch.RegenerationDelay: return "regen_delay";
                default: return "regen_rate";
            }
        }

        private static string BodyKeystoneKey(UpgradeBranch b)
        {
            switch (b)
            {
                case UpgradeBranch.MovementSpeed: return "escape";
                case UpgradeBranch.MaximumHealth: return "thick_skin";
                case UpgradeBranch.RegenerationDelay: return "breath";
                default: return "blood_step";
            }
        }

        /// <summary>The weapon's three core branches: the roster entry (WP-034, WP-069 v3 coreBranches), else the pre-roster trio.</summary>
        public static UpgradeBranch[] CoreBranchesOf(ClassicWeaponId weapon)
        {
            ClassicWeaponDefinition def = WeaponRoster.Find(weapon);
            if (def != null && def.CoreBranches != null && def.CoreBranches.Length == WeaponBranchContract.CoreBranchesPerWeapon)
            {
                return def.CoreBranches;
            }

            return new[] { UpgradeBranch.Piercing, UpgradeBranch.FireRate, UpgradeBranch.ReloadSpeed };
        }

        /// <summary>
        /// The ≤ 12-character seal label of a tier node: designed, unique, whole words (a cut "Movement Spe" or two
        /// "Regeneration" labels read as a bug). rl_1's Explosion tier is its Blast (WP-069 §6, pack A6).
        /// </summary>
        public static string TierShortName(UpgradeBranch branch, string weaponSlot, string fallback)
        {
            switch (branch)
            {
                case UpgradeBranch.Piercing: return "Piercing";
                case UpgradeBranch.Ricochet: return "Ricochet";
                case UpgradeBranch.Explosion: return weaponSlot == "rl_1" ? "Blast" : "Explosion";
                case UpgradeBranch.Chain: return "Chain";
                case UpgradeBranch.FireRate: return "Fire Rate";
                case UpgradeBranch.ReloadSpeed: return "Reload Speed";
                case UpgradeBranch.MovementSpeed: return "Move Speed";
                case UpgradeBranch.MaximumHealth: return "Max Health";
                case UpgradeBranch.RegenerationDelay: return "Regen Delay";
                case UpgradeBranch.RegenerationRate: return "Regen Rate";
                case UpgradeBranch.ArmorBreak: return "Armour Break";
                case UpgradeBranch.Impact: return "Impact";
                case UpgradeBranch.Cluster: return "Cluster";
            }

            string name = fallback ?? branch.ToString();
            return name.Length <= RunTreeCatalog.ShortNameMax ? name : name.Substring(0, RunTreeCatalog.ShortNameMax);
        }

        private int AddTier(RunTreeLane lane, int row, UpgradeBranch branch, PrototypeConfig config)
        {
            UpgradeBranchSettings s = config != null && config.Economy != null ? config.Economy.Find(branch) : null;
            string name = s != null && !string.IsNullOrEmpty(s.DisplayName) ? s.DisplayName : branch.ToString();
            var node = new RunTreeNode
            {
                Id = TierId(branch),
                ShortName = TierShortName(branch, WeaponSlot, name),
                DisplayName = name,
                Description = string.Empty,   // tier cards describe themselves from the live tuning (UpgradeCardCatalog.Describe)
                EffectId = "tier." + branch,
                Lane = lane,
                Row = row,
                Column = 0,
                Kind = RunTreeNodeKind.Tier,
                Branch = branch,
                HasBranch = true,
                MaxTier = s != null ? Math.Max(1, s.MaxTier) : 1,
                OfferWeight = s != null ? s.OfferWeight : 1f
            };
            return Push(node);
        }

        private int AddKeystone(RunTreeLane lane, int row, UpgradeBranch branch, int tierIndex)
        {
            RunTreeCatalogNode k = RunTreeCatalog.FindKeystone(branch);
            if (k == null)
            {
                return -1;
            }

            int i = Add(k, lane, row, 1, RunTreeNodeKind.BranchKeystone, 0);
            _nodes[i].Branch = branch;
            _nodes[i].HasBranch = true;
            _nodes[i].Parents = new[] { tierIndex };
            return i;
        }

        private int AddLinkSlot(int level, int column, int parent)
        {
            var node = new RunTreeNode
            {
                Id = "link_" + level,
                ShortName = level == 1 ? "Link I" : "Link II",
                DisplayName = level == 1 ? "Special Link I" : "Special Link II",
                Description = "Links the Special you pick up this run to your classic weapon.",
                EffectId = string.Empty,
                Lane = RunTreeLane.Weapon,
                Row = 3,
                Column = column,
                Kind = RunTreeNodeKind.SpecialLink,
                LinkLevel = level,
                Parents = parent >= 0 ? new[] { parent } : new int[0]
            };
            return Push(node);
        }

        private int Add(RunTreeCatalogNode c, RunTreeLane lane, int row, int column, RunTreeNodeKind kind, int exclusionGroup)
        {
            if (c == null || string.IsNullOrEmpty(c.id))
            {
                return -1;
            }

            var node = new RunTreeNode
            {
                Id = c.id,
                ShortName = c.shortName,
                DisplayName = c.displayName,
                Description = c.description,
                EffectId = c.effectId,
                Lane = lane,
                Row = row,
                Column = column,
                Kind = kind,
                ExclusionGroup = exclusionGroup,
                FlatCost = c.cost,
                OfferWeight = c.offerWeight,
                EffectValue = c.effectValue
            };
            return Push(node);
        }

        private int Push(RunTreeNode node)
        {
            if (_index.ContainsKey(node.Id))
            {
                return _index[node.Id];
            }

            _index[node.Id] = _nodes.Count;
            _nodes.Add(node);
            return _nodes.Count - 1;
        }
    }
}
