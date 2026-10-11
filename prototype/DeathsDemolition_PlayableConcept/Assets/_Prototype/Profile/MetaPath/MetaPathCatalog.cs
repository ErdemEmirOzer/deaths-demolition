using System;
using System.Collections.Generic;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Profile
{
    /// <summary>WP-077 (DEC-095 layer 2): the three meta PATH pages (LB/RB on the S-09 pane).</summary>
    public enum MetaPage
    {
        Weapons = 0,
        Special = 1,
        Passives = 2
    }

    public enum MetaNodeKind
    {
        Weapon = 0,
        SpecialLean = 1,
        Passive = 2
    }

    /// <summary>Why a meta node is LOCKED (the detail panel turns it into a sentence).</summary>
    public enum MetaLockReason
    {
        None = 0,
        /// <summary>Profile level below the node's gate ("Reach level N").</summary>
        LevelGate = 1,
        /// <summary>No live system reads the node's effect yet; it cannot be bought.</summary>
        EffectPending = 2,
        /// <summary>A weapon that opens on the level ladder (smg_1 L3, sg_1 L5; DEC-085) — no Ash, not bought.</summary>
        LadderWeapon = 3,
        /// <summary>A weapon this build does not carry (roster disabled / not in the arsenal).</summary>
        NotInBuild = 4,
        /// <summary>WP-107 (R4): bought at its wall during a run, never a starting weapon (rl_1).</summary>
        WallBuyOnly = 5
    }

    [Serializable]
    public sealed class MetaPathCatalogNode
    {
        public string id = string.Empty;
        public string page = string.Empty;
        public int row;
        public int column;
        public string kind = string.Empty;
        /// <summary>≤ 12 characters (S-09 T3).</summary>
        public string shortName = string.Empty;
        public string displayName = string.Empty;
        public string description = string.Empty;
        public int levelGate = 1;
        public int ashCost;
        public string effectId = string.Empty;
        public float effectValue;
        public string[] parents = new string[0];
        public int exclusionGroup;
        public string weaponKey = string.Empty;
        /// <summary>R1 content pack key stem (planning/content/r1-strings.json): "&lt;key&gt;.name.en|tr", "&lt;key&gt;.desc.en|tr".</summary>
        public string stringKey = string.Empty;
    }

    [Serializable]
    public sealed class MetaPathCatalogDocument
    {
        public string schema = string.Empty;
        public MetaPathCatalogNode[] nodes = new MetaPathCatalogNode[0];
    }

    /// <summary>One meta node, resolved (parents as indices, page/kind as enums). Immutable.</summary>
    public sealed class MetaPathNode
    {
        public string Id;
        public MetaPage Page;
        public int Row;
        public int Column;
        public MetaNodeKind Kind;
        public string ShortName;
        public string DisplayName;
        public string Description;
        public int LevelGate;
        public int AshCost;
        public string EffectId;
        public float EffectValue;
        public int[] Parents = new int[0];
        public int ExclusionGroup;
        /// <summary>Weapon nodes: the WP-069 key ("ar_2" …).</summary>
        public string WeaponKey;
        /// <summary>R1 content pack key stem (name / desc in en / tr).</summary>
        public string StringKey;
    }

    /// <summary>
    /// WP-077 B2b: the meta PATH catalog — <c>Resources/ProgressionTree/meta-tree.json</c> (dd-meta-tree-v1):
    /// WEAPONS (10, WP-069 order), SPECIAL (10 leanings), PASSIVES (18: economy / body ≤ 8 % / quality of life).
    /// Council proposal numbers (PrototypeOnly) until C-00 records DEC-098 after the B3 simulation.
    /// </summary>
    public static class MetaPathCatalog
    {
        public const string ResourcePath = "ProgressionTree/meta-tree";
        public const string Schema = "dd-meta-tree-v1";
        public const int PageColumns = 6;
        public const int PageRows = 3;

        private static List<MetaPathNode> s_nodes;
        private static Dictionary<string, int> s_index;

        public static IReadOnlyList<MetaPathNode> Nodes
        {
            get
            {
                EnsureLoaded();
                return s_nodes;
            }
        }

        public static int IndexOf(string id)
        {
            EnsureLoaded();
            return id != null && s_index.TryGetValue(id, out int i) ? i : -1;
        }

        public static MetaPathNode Find(string id)
        {
            int i = IndexOf(id);
            return i >= 0 ? s_nodes[i] : null;
        }

        /// <summary>Human-readable problems of a document; empty when sound (tests, F1).</summary>
        public static List<string> Validate(MetaPathCatalogDocument doc)
        {
            var errors = new List<string>();
            if (doc == null || doc.nodes == null)
            {
                errors.Add("no document");
                return errors;
            }

            if (doc.schema != Schema) errors.Add("schema '" + doc.schema + "' != " + Schema);
            var ids = new HashSet<string>();
            var cells = new HashSet<string>();
            foreach (MetaPathCatalogNode n in doc.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id)) { errors.Add("node without id"); continue; }
                if (!ids.Add(n.id)) errors.Add("duplicate id " + n.id);
                if (!Enum.TryParse(n.page, true, out MetaPage _)) errors.Add(n.id + ": page '" + n.page + "'");
                if (!Enum.TryParse(n.kind, true, out MetaNodeKind kind)) errors.Add(n.id + ": kind '" + n.kind + "'");
                if (n.row < 0 || n.row >= PageRows || n.column < 0 || n.column >= PageColumns) errors.Add(n.id + ": outside the 6 × 3 page grid");
                if (!cells.Add(n.page + ":" + n.row + ":" + n.column)) errors.Add(n.id + ": cell taken");
                if (string.IsNullOrEmpty(n.shortName) || n.shortName.Length > RunTreeCatalog.ShortNameMax) errors.Add(n.id + ": shortName empty or > 12");
                if (n.ashCost < 0 || n.levelGate < 1) errors.Add(n.id + ": cost ≥ 0, gate ≥ 1");
                if (kind == MetaNodeKind.Weapon && !WeaponIdMigration.TryGetRuntimeId(n.weaponKey, out _)) errors.Add(n.id + ": weapon key '" + n.weaponKey + "'");
            }

            foreach (MetaPathCatalogNode n in doc.nodes)
            {
                if (n == null || n.parents == null) continue;
                foreach (string p in n.parents) if (!ids.Contains(p)) errors.Add(n.id + ": unknown parent " + p);
            }

            return errors;
        }

        private static void EnsureLoaded()
        {
            if (s_nodes != null)
            {
                return;
            }

            MetaPathCatalogDocument doc = null;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset != null) doc = JsonUtility.FromJson<MetaPathCatalogDocument>(asset.text);
            else Debug.LogWarning("[MetaPath] No catalog at Resources/" + ResourcePath + "; the PATH is empty.");
            Build(doc);
        }

        private static void Build(MetaPathCatalogDocument doc)
        {
            s_nodes = new List<MetaPathNode>();
            s_index = new Dictionary<string, int>();
            if (doc == null || doc.nodes == null) return;

            foreach (MetaPathCatalogNode c in doc.nodes)
            {
                if (c == null || string.IsNullOrEmpty(c.id) || s_index.ContainsKey(c.id)) continue;
                Enum.TryParse(c.page, true, out MetaPage page);
                Enum.TryParse(c.kind, true, out MetaNodeKind kind);
                s_index[c.id] = s_nodes.Count;
                s_nodes.Add(new MetaPathNode
                {
                    Id = c.id, Page = page, Row = c.row, Column = c.column, Kind = kind,
                    ShortName = c.shortName, DisplayName = c.displayName, Description = c.description,
                    LevelGate = Math.Max(1, c.levelGate), AshCost = Math.Max(0, c.ashCost),
                    EffectId = c.effectId ?? string.Empty, EffectValue = c.effectValue,
                    ExclusionGroup = c.exclusionGroup, WeaponKey = c.weaponKey ?? string.Empty, StringKey = c.stringKey ?? string.Empty
                });
            }

            for (int i = 0; i < doc.nodes.Length; i++)
            {
                MetaPathCatalogNode c = doc.nodes[i];
                if (c == null || c.parents == null || !s_index.TryGetValue(c.id, out int self)) continue;
                var parents = new List<int>();
                foreach (string p in c.parents) if (s_index.TryGetValue(p, out int pi)) parents.Add(pi);
                s_nodes[self].Parents = parents.ToArray();
            }
        }

        /// <summary>Tests only: replace the catalog (null = reload from Resources on next use).</summary>
        public static class TestSeam
        {
            public static void Override(MetaPathCatalogDocument doc)
            {
                if (doc == null) { s_nodes = null; s_index = null; }
                else Build(doc);
            }
        }
    }

    /// <summary>
    /// WP-077: which meta effects a live system reads. Weapon unlocks are always live (the entitlement
    /// list is the gate); others go live with their hook — the owner adds the id here in that commit.
    /// A node whose effect is not live is LOCKED (<see cref="MetaLockReason.EffectPending"/>) and cannot be bought.
    /// </summary>
    public static class MetaPathEffects
    {
        public const string RerollCost = "meta.eco.reroll";
        public const string HearthSlope = "meta.eco.hearthslope";
        public const string PurseAtStart = "meta.eco.pursestart";
        public const string CapstoneDiscount = "meta.eco.capdiscount";

        /// <summary>DEC-113 pa_qol_6 Round Report — data in RunStatsTracker.LastRound; S-09 adds it here with the UI.</summary>
        public const string RoundReport = "meta.qol.roundreport";

        private static readonly HashSet<string> s_live = new HashSet<string>
        {
            RerollCost, HearthSlope, PurseAtStart, CapstoneDiscount,
            RoundReport   // DEC-113: S-09's round-end line (UpgradeScreen)
            // + an effect id per hook as it lands (Special leanings: WP-069 B4; body row: player systems; QoL: S-09)
        };

        /// <summary>Tests / F1: every effect counts as live.</summary>
        public static bool TreatAllAsLive { get; set; }

        public static bool IsLive(string effectId)
        {
            if (TreatAllAsLive) return true;
            if (string.IsNullOrEmpty(effectId)) return false;
            return effectId.StartsWith("weapon.", StringComparison.Ordinal) || s_live.Contains(effectId);
        }
    }
}
