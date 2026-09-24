using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-077 (DEC-095): the in-run tree catalog — keystone / fork / seal / capstone / link / Hearth /
    /// Purse definitions from <c>Resources/ProgressionTree/run-tree.json</c> (dd-run-tree-v1). Tier nodes
    /// are not in it: they are the existing <see cref="UpgradeBranchSettings"/> (EconomyConfigSection).
    /// Numbers are the WP-077 council proposal (PrototypeOnly) until C-00 records them as a DEC.
    /// </summary>
    public static class RunTreeCatalog
    {
        public const string ResourcePath = "ProgressionTree/run-tree";
        public const string Schema = "dd-run-tree-v1";
        public const int ShortNameMax = 12;

        /// <summary>Runtime weapon id (int) → WP-069 slot key. Matches S-02's WeaponIdMigration.TryGetRuntimeId (0/1/2 are the M3 A/B/C).</summary>
        private static readonly string[] s_slots = { "ar_1", "smg_1", "sg_1", "ar_2", "smg_2", "sg_2", "lmg_1", "lmg_2", "sr_1", "rl_1" };

        private static RunTreeCatalogDocument s_document;

        public static RunTreeCatalogDocument Document
        {
            get
            {
                if (s_document == null)
                {
                    s_document = Load();
                }

                return s_document;
            }
        }

        /// <summary>The WP-069 slot key of a runtime weapon id, or null for an id outside the arsenal.</summary>
        public static string SlotFor(ClassicWeaponId id)
        {
            int i = (int)id;
            return i >= 0 && i < s_slots.Length ? s_slots[i] : null;
        }

        public static RunTreeCatalogWeapon FindWeapon(string slot)
        {
            RunTreeCatalogWeapon[] weapons = Document.weapons;
            for (int i = 0; weapons != null && i < weapons.Length; i++)
            {
                if (weapons[i] != null && weapons[i].slot == slot)
                {
                    return weapons[i];
                }
            }

            return null;
        }

        public static RunTreeCatalogNode FindKeystone(UpgradeBranch branch)
        {
            string name = branch.ToString();
            RunTreeCatalogNode[] keystones = Document.branchKeystones;
            for (int i = 0; keystones != null && i < keystones.Length; i++)
            {
                if (keystones[i] != null && keystones[i].branch == name)
                {
                    return keystones[i];
                }
            }

            return null;
        }

        public static RunTreeCatalogNode FindLink(string special, int level)
        {
            RunTreeCatalogNode[] links = Document.specialLinks;
            for (int i = 0; links != null && i < links.Length; i++)
            {
                if (links[i] != null && links[i].special == special && links[i].level == level)
                {
                    return links[i];
                }
            }

            return null;
        }

        /// <summary>Human-readable problems of a document; empty when it is sound. Used by the tests and the F1 readout.</summary>
        public static List<string> Validate(RunTreeCatalogDocument doc)
        {
            var errors = new List<string>();
            if (doc == null)
            {
                errors.Add("no catalog document");
                return errors;
            }

            if (doc.schema != Schema)
            {
                errors.Add("schema '" + doc.schema + "' != " + Schema);
            }

            var ids = new HashSet<string>();
            void Check(RunTreeCatalogNode n, string where, bool needsCost)
            {
                if (n == null || string.IsNullOrEmpty(n.id))
                {
                    errors.Add(where + ": missing node");
                    return;
                }

                if (!ids.Add(n.id)) errors.Add(where + ": duplicate id " + n.id);
                if (string.IsNullOrEmpty(n.shortName) || n.shortName.Length > ShortNameMax) errors.Add(n.id + ": shortName empty or > " + ShortNameMax);
                if (string.IsNullOrEmpty(n.displayName)) errors.Add(n.id + ": displayName empty");
                if (string.IsNullOrEmpty(n.effectId)) errors.Add(n.id + ": effectId empty");
                if (needsCost && n.cost <= 0) errors.Add(n.id + ": cost must be positive");
            }

            foreach (RunTreeCatalogNode k in doc.branchKeystones ?? new RunTreeCatalogNode[0])
            {
                Check(k, "keystone", true);
                if (k != null && !System.Enum.TryParse(k.branch, out UpgradeBranch _)) errors.Add(k.id + ": unknown branch '" + k.branch + "'");
            }

            foreach (UpgradeBranch b in (UpgradeBranch[])System.Enum.GetValues(typeof(UpgradeBranch)))
            {
                bool found = false;
                foreach (RunTreeCatalogNode k in doc.branchKeystones ?? new RunTreeCatalogNode[0]) found |= k != null && k.branch == b.ToString();
                if (!found) errors.Add("no keystone for branch " + b);
            }

            if ((doc.bodyForks ?? new RunTreeCatalogNode[0]).Length != 2) errors.Add("bodyForks must be a pair");
            foreach (RunTreeCatalogNode f in doc.bodyForks ?? new RunTreeCatalogNode[0]) Check(f, "bodyFork", true);
            Check(doc.bodyCapstone, "bodyCapstone", true);
            Check(doc.purse, "purse", true);
            Check(doc.hearth, "hearth", true);
            if (doc.hearth != null && (doc.hearth.costStep < 0 || doc.hearth.maxStacks <= 0 || doc.hearth.perRound <= 0)) errors.Add("hearth: costStep ≥ 0, maxStacks > 0, perRound > 0");
            foreach (RunTreeCatalogNode l in doc.specialLinks ?? new RunTreeCatalogNode[0])
            {
                Check(l, "link", true);
                if (l != null && (l.level < 1 || l.level > 2)) errors.Add(l.id + ": link level must be 1 or 2");
            }

            foreach (string slot in s_slots)
            {
                RunTreeCatalogWeapon w = null;
                foreach (RunTreeCatalogWeapon x in doc.weapons ?? new RunTreeCatalogWeapon[0]) if (x != null && x.slot == slot) w = x;
                if (w == null)
                {
                    errors.Add("no weapon entry for " + slot);
                    continue;
                }

                Check(w.seal, slot + ".seal", true);
                Check(w.forkX, slot + ".forkX", true);
                Check(w.forkY, slot + ".forkY", true);
                Check(w.capstone, slot + ".capstone", true);
            }

            return errors;
        }

        private static RunTreeCatalogDocument Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[RunTree] No catalog at Resources/" + ResourcePath + "; the tree offers branch tiers only.");
                return new RunTreeCatalogDocument { schema = Schema };
            }

            RunTreeCatalogDocument doc = JsonUtility.FromJson<RunTreeCatalogDocument>(asset.text);
            return doc ?? new RunTreeCatalogDocument { schema = Schema };
        }

        /// <summary>Tests only: replace the catalog (null = reload from Resources on next use).</summary>
        public static class TestSeam
        {
            public static void Override(RunTreeCatalogDocument document) => s_document = document;
        }
    }

    /// <summary>
    /// WP-077: which node effects a live system reads. A node whose effect is not live is shown
    /// LOCKED (<see cref="RunTreeLockReason.EffectPending"/>) and is never offered or sold — the same
    /// rule as UpgradeCardCatalog's unbacked branches. The owner of a behaviour (WP-069 S-02 weapon
    /// hooks, player systems) adds its effect id to the live list below in the commit that installs the hook.
    /// </summary>
    public static class RunTreeEffects
    {
        public const string HearthDamage = "hearth.damage";
        public const string PurseGold = "purse.gold";

        private static readonly HashSet<string> s_live = new HashSet<string>
        {
            HearthDamage, PurseGold
            // + an effect id per hook as it lands (WP-069 B2/B3 weapon hooks, player systems)
        };

        /// <summary>F1 / tests: treat every effect as live (shows the full tree). Never on in a shipped build by default.</summary>
        public static bool TreatAllAsLive { get; set; }

        public static bool IsLive(string effectId)
        {
            return TreatAllAsLive || (!string.IsNullOrEmpty(effectId) && s_live.Contains(effectId));
        }

    }
}
