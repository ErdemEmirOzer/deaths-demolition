using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    [Serializable]
    public sealed class TreeArgV2
    {
        public string name;
        public float value;
        /// <summary>Display unit (K/wp-104/strings/NOTES.md §1): bonus (v−1)×100 · cut (1−v)×100 · pct v×100 · mult · count · sec · m · deg.</summary>
        public string unit;

        /// <summary>The number the pack text shows for this arg (sign and % live in the text).</summary>
        public float DisplayValue
        {
            get
            {
                switch (unit)
                {
                    case "bonus": return Round1((value - 1f) * 100f);
                    case "cut": return Round1((1f - value) * 100f);
                    case "pct": return Round1(value * 100f);
                    default: return value;
                }
            }
        }

        private static float Round1(float v) => Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? Mathf.Round(v) : Mathf.Round(v * 10f) / 10f;
    }

    [Serializable]
    public sealed class TreeNodeDefV2
    {
        public string id;
        /// <summary><see cref="TreeBranch"/> name.</summary>
        public string branch;
        /// <summary>1–5 (I–V).</summary>
        public int tier;
        public int slot;
        /// <summary><see cref="TreeElement"/> name ("None" outside the Element branch).</summary>
        public string element;
        public int cost;
        public string[] parents;
        /// <summary>Parents needed (0 = all of them; Elemental Fusion 2 of four, Critical Mass 1 of two).</summary>
        public int parentsMin;
        /// <summary>1 = F1 element cap, 2 = F2 chaos core, ≥ 10 = a capstone and its awakening alternative.</summary>
        public int fork;
        /// <summary>Non-empty: an awakening variant, buyable only when the profile owns this meta node (Ash).</summary>
        public string awakening;
        public string stringKey;
        /// <summary>AP-042 icon file (C-00 11.10): "&lt;branch-tr&gt;_&lt;tier-roman&gt;_&lt;english_name&gt;.png", e.g. atis_III_triple_shot.png; filled to v002.</summary>
        public string icon;
        public TreeArgV2[] args;

        [NonSerialized] public TreeBranch Branch;
        [NonSerialized] public TreeElement Element;
        [NonSerialized] public int[] ParentIndices;
        /// <summary>Placeholder names and display values for the detail panel (built once on load; shared, read-only).</summary>
        [NonSerialized] public string[] DisplayNames;
        [NonSerialized] public float[] DisplayValues;

        public float Arg(string name, float fallback)
        {
            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (args[i] != null && string.Equals(args[i].name, name, StringComparison.Ordinal)) return args[i].value;
            }

            return fallback;
        }

        public bool IsAwakening => !string.IsNullOrEmpty(awakening);
    }

    [Serializable]
    public sealed class TreeSynergyDefV2
    {
        /// <summary><see cref="TreeSynergy"/> name.</summary>
        public string id;
        public string a;
        /// <summary>Empty for Barrage (its second condition is rl_1 in hand).</summary>
        public string b;
        public bool needsRocket;
        /// <summary>AP-042 badge file, e.g. sinerji_barrage.png.</summary>
        public string icon;
        public TreeArgV2[] args;

        [NonSerialized] public TreeSynergy Synergy;
        [NonSerialized] public int NodeA = -1;
        [NonSerialized] public int NodeB = -1;
        [NonSerialized] public string[] DisplayNames;
        [NonSerialized] public float[] DisplayValues;

        public float Arg(string name, float fallback)
        {
            for (int i = 0; args != null && i < args.Length; i++)
            {
                if (args[i] != null && string.Equals(args[i].name, name, StringComparison.Ordinal)) return args[i].value;
            }

            return fallback;
        }
    }

    [Serializable]
    public sealed class TreeModeDefV2
    {
        public string name;
        /// <summary>Round each tier I–V opens in.</summary>
        public int[] tierGates;
        /// <summary>Compound Interest cap in this mode (the mode's gold scale and starting gold are WP-102's DifficultyService).</summary>
        public int interestCap;
    }

    [Serializable]
    public sealed class TreeStripDefV2
    {
        public int count = 3;
        public float discount = 0.10f;
        /// <summary>B5a: the strip discount never exceeds this many gold (a reroll must not pay for itself on tier IV–V).</summary>
        public int discountCap = 25;
        public int rerollCost = 10;
        /// <summary>B5a: each further reroll in the same round costs this much more (10, 20, 30 …).</summary>
        public int rerollStep = 10;
    }

    [Serializable]
    public sealed class TreeHearthDefV2
    {
        public int baseCost = 50;
        public int costStep = 30;
        public int perRound = 3;
        public int max = 20;
        public float value = 0.02f;
    }

    /// <summary>Full-Auto Conversion per weapon class (design §2.3): rpm ×, damage ×, magazine (0 = unchanged), blast radius ×, reload s (0 = unchanged).</summary>
    [Serializable]
    public sealed class TreeFullAutoDefV2
    {
        public string key;
        public float rpmScale = 1f;
        public float damageScale = 1f;
        public int magazine;
        public float radiusScale = 1f;
        public float reload;
    }

    [Serializable]
    public sealed class TreeCatalogDocumentV2
    {
        public string schema;
        public string note;
        public TreeModeDefV2[] modes;
        public TreeStripDefV2 strip;
        public TreeHearthDefV2 hearth;
        public int elementCap = 2;
        public float goldScaleMax = 1.6f;
        public int[] auraThresholds;
        public TreeFullAutoDefV2[] fullAuto;
        public TreeFullAutoDefV2 fullAutoDefault;
        public TreeNodeDefV2[] nodes;
        public TreeSynergyDefV2[] synergies;
    }

    /// <summary>
    /// WP-104 B1 · the feature-based run tree catalog, <c>Resources/ProgressionTree/run-tree-v2.json</c> (dd-run-tree-v2),
    /// generated from the approved design by K/wp-104/b1/gen_tree_v2.py. Loaded once; indices resolved on load.
    /// </summary>
    public static class TreeCatalogV2
    {
        public const string ResourcePath = "ProgressionTree/run-tree-v2";
        public const string Schema = "dd-run-tree-v2";
        public const int Tiers = 5;

        private static TreeCatalogDocumentV2 s_document;
        private static Dictionary<string, int> s_index;

        public static TreeCatalogDocumentV2 Document
        {
            get
            {
                if (s_document == null) Install(Load());
                return s_document;
            }
        }

        /// <summary>Tests: install a document (null reloads the shipped one on next read).</summary>
        public static void SetForTests(TreeCatalogDocumentV2 doc)
        {
            s_document = null;
            s_index = null;
            if (doc != null) Install(doc);
        }

        public static int IndexOf(string id)
        {
            TreeCatalogDocumentV2 doc = Document;
            return id != null && s_index != null && s_index.TryGetValue(id, out int i) ? i : -1;
        }

        public static TreeNodeDefV2 Find(string id)
        {
            int i = IndexOf(id);
            return i >= 0 ? s_document.nodes[i] : null;
        }

        public static TreeModeDefV2 Mode(int mode)
        {
            TreeModeDefV2[] modes = Document.modes;
            if (modes == null || modes.Length == 0) return null;
            return modes[Mathf.Clamp(mode, 0, modes.Length - 1)];
        }

        /// <summary>Round tier <paramref name="tier"/> (1–5) opens in, for a mode.</summary>
        public static int TierGate(int mode, int tier)
        {
            TreeModeDefV2 m = Mode(mode);
            if (m == null || m.tierGates == null || m.tierGates.Length == 0) return 1;
            return m.tierGates[Mathf.Clamp(tier, 1, m.tierGates.Length) - 1];
        }

        /// <summary>Highest tier open in a round (0 = none).</summary>
        public static int OpenTier(int mode, int round)
        {
            int open = 0;
            for (int t = 1; t <= Tiers; t++)
            {
                if (round >= TierGate(mode, t)) open = t;
            }

            return open;
        }

        public static TreeFullAutoDefV2 FullAutoFor(string weaponKey)
        {
            TreeCatalogDocumentV2 doc = Document;
            for (int i = 0; doc.fullAuto != null && i < doc.fullAuto.Length; i++)
            {
                if (doc.fullAuto[i] != null && string.Equals(doc.fullAuto[i].key, weaponKey, StringComparison.Ordinal)) return doc.fullAuto[i];
            }

            return doc.fullAutoDefault ?? new TreeFullAutoDefV2();
        }

        private static TreeCatalogDocumentV2 Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError("[TreeV2] missing Resources/" + ResourcePath + ".json");
                return new TreeCatalogDocumentV2 { schema = Schema, nodes = new TreeNodeDefV2[0], synergies = new TreeSynergyDefV2[0] };
            }

            return JsonUtility.FromJson<TreeCatalogDocumentV2>(asset.text);
        }

        private static void Install(TreeCatalogDocumentV2 doc)
        {
            doc.nodes = doc.nodes ?? new TreeNodeDefV2[0];
            doc.synergies = doc.synergies ?? new TreeSynergyDefV2[0];
            var index = new Dictionary<string, int>(doc.nodes.Length);
            for (int i = 0; i < doc.nodes.Length; i++)
            {
                TreeNodeDefV2 n = doc.nodes[i];
                if (n == null || string.IsNullOrEmpty(n.id) || index.ContainsKey(n.id)) continue;
                index[n.id] = i;
                Enum.TryParse(n.branch, out n.Branch);
                n.Element = TreeElement.None;
                if (!string.IsNullOrEmpty(n.element)) Enum.TryParse(n.element, out n.Element);
                BuildDisplay(n.args, out n.DisplayNames, out n.DisplayValues);
            }

            for (int i = 0; i < doc.nodes.Length; i++)
            {
                TreeNodeDefV2 n = doc.nodes[i];
                if (n == null) continue;
                int count = n.parents != null ? n.parents.Length : 0;
                n.ParentIndices = new int[count];
                for (int p = 0; p < count; p++) n.ParentIndices[p] = index.TryGetValue(n.parents[p] ?? string.Empty, out int pi) ? pi : -1;
            }

            for (int i = 0; i < doc.synergies.Length; i++)
            {
                TreeSynergyDefV2 s = doc.synergies[i];
                if (s == null) continue;
                Enum.TryParse(s.id, out s.Synergy);
                s.NodeA = !string.IsNullOrEmpty(s.a) && index.TryGetValue(s.a, out int a) ? a : -1;
                s.NodeB = !string.IsNullOrEmpty(s.b) && index.TryGetValue(s.b, out int b) ? b : -1;
                BuildDisplay(s.args, out s.DisplayNames, out s.DisplayValues);
            }

            s_document = doc;
            s_index = index;
        }

        private static void BuildDisplay(TreeArgV2[] args, out string[] names, out float[] values)
        {
            int count = args != null ? args.Length : 0;
            names = new string[count];
            values = new float[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = args[i] != null ? args[i].name : string.Empty;
                values[i] = args[i] != null ? args[i].DisplayValue : 0f;
            }
        }

        /// <summary>Human-readable problems; empty when sound (tests, F1).</summary>
        public static List<string> Validate(TreeCatalogDocumentV2 doc)
        {
            var errors = new List<string>();
            if (doc == null)
            {
                errors.Add("no document");
                return errors;
            }

            if (doc.schema != Schema) errors.Add("schema '" + doc.schema + "' != " + Schema);
            if (doc.modes == null || doc.modes.Length != TreeMode.Count) errors.Add("modes != " + TreeMode.Count);
            for (int m = 0; doc.modes != null && m < doc.modes.Length; m++)
            {
                int[] g = doc.modes[m] != null ? doc.modes[m].tierGates : null;
                if (g == null || g.Length != Tiers) { errors.Add("mode " + m + ": tierGates != " + Tiers); continue; }
                for (int t = 1; t < g.Length; t++)
                {
                    if (g[t] < g[t - 1]) errors.Add("mode " + m + ": tier gates not monotone");
                }

                if (g[0] != 1) errors.Add("mode " + m + ": tier I must open in round 1");
            }

            var ids = new HashSet<string>();
            foreach (TreeNodeDefV2 n in doc.nodes ?? new TreeNodeDefV2[0])
            {
                if (n == null || string.IsNullOrEmpty(n.id)) { errors.Add("missing node"); continue; }
                if (!ids.Add(n.id)) errors.Add("duplicate id " + n.id);
                if (!Enum.TryParse(n.branch, out TreeBranch _)) errors.Add(n.id + ": unknown branch " + n.branch);
                if (n.tier < 1 || n.tier > Tiers) errors.Add(n.id + ": tier " + n.tier);
                if (n.cost <= 0) errors.Add(n.id + ": cost must be positive");
                if (string.IsNullOrEmpty(n.stringKey)) errors.Add(n.id + ": stringKey empty");
            }

            foreach (TreeNodeDefV2 n in doc.nodes ?? new TreeNodeDefV2[0])
            {
                if (n == null || n.parents == null) continue;
                foreach (string p in n.parents)
                {
                    if (!ids.Contains(p)) { errors.Add(n.id + ": unknown parent " + p); continue; }
                    TreeNodeDefV2 parent = Array.Find(doc.nodes, x => x != null && x.id == p);
                    if (parent != null && parent.tier >= n.tier) errors.Add(n.id + ": parent " + p + " is not a lower tier");
                }

                if (n.parentsMin < 0 || n.parentsMin > n.parents.Length) errors.Add(n.id + ": parentsMin out of range");
            }

            foreach (TreeSynergyDefV2 s in doc.synergies ?? new TreeSynergyDefV2[0])
            {
                if (s == null || !Enum.TryParse(s.id, out TreeSynergy syn) || syn == TreeSynergy.None) { errors.Add("unknown synergy " + (s != null ? s.id : "null")); continue; }
                if (!ids.Contains(s.a)) errors.Add(s.id + ": unknown node a " + s.a);
                if (!s.needsRocket && !ids.Contains(s.b)) errors.Add(s.id + ": unknown node b " + s.b);
            }

            return errors;
        }
    }
}
