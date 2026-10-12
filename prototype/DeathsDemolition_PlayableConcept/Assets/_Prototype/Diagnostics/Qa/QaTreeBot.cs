#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-104 B5b (S-04, C-00 12.10): the QA bot's purchases on the feature-based run tree (RunTreeV2) for the combined
    /// WP-102 + B5 bot series. <c>-ddQaBotTreeArch &lt;Shot|Element|Barrage|Tank|Hoarder&gt;</c> arms it (with <c>-ddTreeV2</c>, dev
    /// builds); the archetype lists are the B5 simulator's (K/wp-104/b5/tree_model_v2.py priority_list), so the in-game series
    /// checks the model. Each round end: follow the archetype line while the next node is buyable, save for an unaffordable one,
    /// skip what the round gate or the forks close; when the line is done, fill by (tier, cost); Hearth is the late sink.
    /// Only live nodes are bought (TreeLiveV2) — the bot never sells itself an effect no system reads. Writes tree-bot.json.
    /// </summary>
    public static class QaTreeBot
    {
        public const string ArchFlag = "-ddQaBotTreeArch";

        private static readonly string[][] s_elements =
        {
            new[] { "el_fire", "el_fire2", "el_fire3" }, new[] { "el_ice", "el_ice2", "el_ice3" },
            new[] { "el_shock", "el_shock2", "el_shock3" }, new[] { "el_venom", "el_venom2", "el_venom3" }
        };

        private static string s_arch;
        private static List<string> s_line;
        private static readonly StringBuilder s_rounds = new StringBuilder(2048);
        private static int s_bought, s_spent, s_hearth;

        /// <summary>
        /// The archetype from the command line. Without the flag: "Shot" when the shipped config runs the feature tree
        /// (WP-104 opening: a bot plays the tree like a player), else empty — the bot buys v1 cards as before.
        /// </summary>
        public static string Arch => s_arch ?? (s_arch = QaArgs.Arg(ArchFlag) ?? (RoundUpgradeShop.TreeV2Active(DD.Prototype.Core.Config.PrototypeConfig.Active) ? "Shot" : string.Empty));

        public static bool Active => !string.IsNullOrEmpty(Arch);

        /// <summary>One round end. Returns true when the V2 tree took the purchases (the caller skips the v1 cards).</summary>
        public static bool BuyRound(RoundUpgradeShop shop, int round, int seed, Action<string> log)
        {
            if (!Active || shop == null || !shop.TreeV2.IsBuilt || !shop.TreeV2.IsOpen) return false;
            RunTreeV2 tree = shop.TreeV2;
            if (s_line == null) s_line = BuildLine(Arch, seed);

            int boughtNow = 0;
            for (int guard = 0; guard < 64; guard++)
            {
                int pick = NextFromLine(tree, out bool save);
                if (pick < 0 && !save) pick = CheapestFill(tree);
                if (pick < 0) break;
                int price = tree.PriceOf(pick);
                if (!tree.TryBuy(pick)) break;
                boughtNow++;
                s_bought++;
                s_spent += price;
            }

            // Hearth: the late gold sink once the line is spent or blocked (GD §4).
            for (int k = 0; k < 3 && tree.HearthAvailable && tree.Snapshot.Gold >= tree.HearthCost * 2; k++)
            {
                int cost = tree.HearthCost;
                if (!tree.TryStokeHearth()) break;
                s_hearth++;
                s_spent += cost;
            }

            TreeStats s = tree.Stats;
            string line = string.Format(CultureInfo.InvariantCulture,
                "{{\"round\":{0},\"bought\":{1},\"nodes\":{2},\"power\":{3},\"aura\":{4},\"synergies\":{5},\"tier3\":{6},\"gold\":{7},\"hearth\":{8},\"weaponPower\":{9:0.0}}}",
                round, boughtNow, tree.NodesOwned, s.PowerScore, s.AuraLevel, tree.SynergyCount, tree.CountOwnedTierAtLeast(3), tree.Snapshot.Gold,
                tree.HearthStokesTotal, s.WeaponPower);
            if (s_rounds.Length > 0) s_rounds.Append(",\n  ");
            s_rounds.Append(line);
            log?.Invoke("[TreeBot] arch=" + Arch + " " + line);
            return true;
        }

        /// <summary>Run end: tree-bot.json next to bot-summary.json (the series scripts read it with the death round).</summary>
        public static void WriteReport(string outDir, int deathRound, int mode)
        {
            if (!Active || string.IsNullOrEmpty(outDir)) return;
            try
            {
                Directory.CreateDirectory(outDir);
                string json = "{\n \"arch\": \"" + Arch + "\",\n \"mode\": " + mode + ",\n \"deathRound\": " + deathRound + ",\n \"bought\": " + s_bought +
                              ",\n \"spent\": " + s_spent + ",\n \"hearth\": " + s_hearth + ",\n \"rounds\": [\n  " + s_rounds + "\n ]\n}\n";
                File.WriteAllText(Path.Combine(outDir, "tree-bot.json"), json);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Tests / a new run.</summary>
        public static void Reset(string arch = null)
        {
            s_arch = arch;
            s_line = null;
            s_rounds.Clear();
            s_bought = s_spent = s_hearth = 0;
        }

        // ------------------------------------------------------------------ policy

        /// <summary>The first node of the line still to buy: Available → its index; Unaffordable → −1 with save (wait for it).</summary>
        private static int NextFromLine(RunTreeV2 tree, out bool save)
        {
            save = false;
            for (int i = 0; i < s_line.Count; i++)
            {
                int idx = TreeCatalogV2.IndexOf(s_line[i]);
                if (idx < 0) continue;
                switch (tree.StateOf(idx))
                {
                    case TreeNodeState.Available:
                        return idx;
                    case TreeNodeState.Unaffordable:
                        save = true;
                        return -1;
                    default:
                        continue;   // owned, fogged, prerequisite, closed, awakening-locked or not live yet: next on the line
                }
            }

            return -1;
        }

        private static int CheapestFill(RunTreeV2 tree)
        {
            TreeNodeDefV2[] nodes = TreeCatalogV2.Document.nodes;
            int best = -1, bestTier = int.MaxValue, bestCost = int.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (tree.StateOf(i) != TreeNodeState.Available) continue;
                int cost = tree.PriceOf(i);
                if (nodes[i].tier < bestTier || (nodes[i].tier == bestTier && cost < bestCost))
                {
                    best = i;
                    bestTier = nodes[i].tier;
                    bestCost = cost;
                }
            }

            return best;
        }

        /// <summary>The B5 simulator's archetype lines (wall-buy steps dropped: the series drives the weapon with -ddQaBotWeapon).</summary>
        public static List<string> BuildLine(string arch, int seed)
        {
            var rng = new System.Random(seed * 31 + 104);
            int a = rng.Next(4), b = (a + 1 + rng.Next(3)) % 4;
            string[] A = s_elements[a], B = s_elements[b];
            switch (arch)
            {
                case "Shot":
                    return new List<string> { "sh_heavy", "sp_rate1", "sh_double", "mg_ext", "mg_reload1", "sv_hp1", "sp_rate2", "sh_bore",
                        "sh_ricochet", "sh_triple", "sp_fullauto", "mg_double", "sh_pierce", "sv_hp2", "sp_rate3", "sh_explosive", "sh_fan", "sh_cap",
                        "sh_storm", "mg_reload2", "kx_crit", "sh_rail", "kx_crit2", "sp_twin", "mg_drum", "sv_shield", "sp_cap", "sv_hp3",
                        "mg_autoload", "mg_reload3", "mg_endless" };
                case "Element":
                {
                    var l = new List<string> { A[0], B[0], "sh_heavy", A[1], B[1], "sv_hp1", "sp_rate1", A[2], B[2], "el_fusion", "el_cap", "kx_crit" };
                    bool ice = a == 1 || b == 1, shock = a == 2 || b == 2;
                    if (ice) l.AddRange(new[] { "kx_chain", "kx_hole" });
                    else if (shock) l.AddRange(new[] { "kx_chain", "kx_crit2", "kx_nuke" });
                    else l.AddRange(new[] { "kx_crit2", "kx_nuke" });
                    l.AddRange(new[] { "sv_hp2", "kx_mass", "sh_double", "sp_rate2", "sv_shield", "sh_triple", "kx_cap", "sv_hp3" });
                    return l;
                }
                case "Barrage":
                    return new List<string> { "sp_rate1", "ec_dealer", "sh_heavy", "sp_rate2", "sv_hp1", "sh_double", "sp_fullauto", "mg_reload1",
                        "sh_triple", "sh_bore", "sh_explosive", "sp_rate3", "sh_fan", "sp_twin", "sp_cap", "mg_ext", "mg_double", "sv_hp2", "kx_crit",
                        "kx_crit2", "sh_cap", "mg_reload2", "mg_drum" };
                case "Tank":
                    return new List<string> { "sv_hp1", "ec_gold1", "sv_hp2", "sv_shield", "sv_leech", "ec_gold2", "sh_heavy", "sv_hp3", "sv_dodge",
                        "sv_bulwark", "sv_cap", "ec_interest", "sp_move", "sp_dash", "ec_gold3", "sh_double", "sp_rate1", "sp_rate2", "sh_triple" };
                case "Hoarder":
                    return new List<string> { "ec_gold1", "ec_interest", "ec_gold2", "sh_heavy", "sp_rate1", "ec_gold3", "sh_double", "sv_hp1",
                        "ec_midas", "sp_rate2", "sh_triple", "kx_crit", "sv_hp2", "sp_fullauto", "sp_rate3", "sh_fan", "sh_cap" };
                default:
                    return new List<string>();
            }
        }
    }
}
#endif
