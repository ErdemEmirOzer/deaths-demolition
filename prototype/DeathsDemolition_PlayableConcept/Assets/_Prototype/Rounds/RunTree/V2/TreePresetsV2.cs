using System;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B1 · dev-only late-round builds (KARAR-WP104-11.10: approved; S-10 FINAL scenarios, S-11 WP-110 capture, the perf gate).
    /// <c>-ddTreePreset &lt;name&gt;</c> grants the preset's nodes free at run start; <c>-ddStartRound N</c> is read here and handed to
    /// RunDirector (C-00 12.10: one hook, StartRun(RunStartRound)). Editor, development and release-probe builds only: the player path never sees it,
    /// and evidence of player behaviour is always taken without these flags.
    /// </summary>
    public static class TreePresetsV2
    {
        public struct Preset
        {
            public string Name;
            /// <summary>The weapon the preset is meant for (the capture equips it; "" = any).</summary>
            public string WeaponKey;
            public int Round;
            public string[] Nodes;
        }

        public static readonly Preset[] All =
        {
            new Preset
            {
                Name = "barrage_r15", WeaponKey = "rl_1", Round = 15,
                Nodes = new[] { "sp_rate1", "sp_rate2", "sp_rate3", "sp_fullauto", "sp_twin", "sh_heavy", "sh_double", "sh_triple", "sh_fan",
                                "mg_ext", "mg_double", "mg_reload1", "mg_reload2", "sh_bore", "sh_explosive", "sv_hp1", "sv_hp2", "ec_gold1" }
            },
            new Preset
            {
                Name = "element_r15", WeaponKey = "ar_1", Round = 15,
                Nodes = new[] { "el_fire", "el_fire2", "el_fire3", "el_ice", "el_ice2", "el_ice3", "el_fusion", "kx_chain", "kx_hole",
                                "sp_rate1", "sp_rate2", "mg_ext", "mg_reload1", "sv_hp1", "sv_hp2", "sv_shield", "ec_gold1" }
            },
            new Preset
            {
                Name = "shot_r10", WeaponKey = "sg_1", Round = 10,
                Nodes = new[] { "sh_heavy", "sh_bore", "sh_double", "sh_ricochet", "sh_triple", "sp_rate1", "sp_rate2", "sp_fullauto",
                                "mg_ext", "mg_reload1", "sv_hp1", "ec_gold1" }
            },
            new Preset
            {
                Name = "demolition_r4", WeaponKey = "ar_1", Round = 4,
                Nodes = new[] { "sh_heavy", "sh_double", "sh_bore", "sp_rate1", "sp_rate2", "sp_fullauto", "el_fire", "el_fire2", "mg_ext", "sv_hp1" }
            },
            new Preset
            {
                // S-06 B4 TD evidence: every weapon attachment on ar_1 at once — E2-L drum (mg_drum, tier IV opens R10), E3 twin
                // barrel + E5 speed ring (sp_twin), E4 fire capsule glowing at tier III.
                Name = "attach_r12", WeaponKey = "ar_1", Round = 12,
                Nodes = new[] { "mg_ext", "mg_double", "mg_reload1", "mg_reload2", "mg_reload3", "mg_drum", "sp_rate1", "sp_rate2", "sp_rate3",
                                "sp_twin", "el_fire", "el_fire2", "el_fire3" }
            },
            new Preset
            {
                // C-00 12.10 balance check: the in-game Hard Shot bot's tree around R14 (its line's first 20; 23 nodes at R15 in the
                // s3 series) so the R14-R22 window plays in one 25-minute cell (-ddDifficulty hard -ddStartRound 14).
                Name = "hardshot_r14", WeaponKey = "ar_1", Round = 14,
                Nodes = new[] { "sh_heavy", "sp_rate1", "sh_double", "mg_ext", "mg_reload1", "sv_hp1", "sp_rate2", "sh_bore", "sh_ricochet",
                                "sh_triple", "sp_fullauto", "mg_double", "sh_pierce", "sv_hp2", "sp_rate3", "sh_explosive", "sh_fan", "mg_reload2",
                                "kx_crit", "sv_shield" }
            },
            new Preset
            {
                // Design §8: the worst build for the perf gate (rl_1 auto + triple + explosive + chain + nuke, 40 zombies).
                Name = "perf_worst", WeaponKey = "rl_1", Round = 15,
                Nodes = new[] { "sp_rate1", "sp_rate2", "sp_rate3", "sp_fullauto", "sp_twin", "sh_heavy", "sh_double", "sh_triple", "sh_fan",
                                "sh_bore", "sh_explosive", "kx_crit", "kx_crit2", "kx_chain", "kx_nuke", "kx_mass", "el_shock", "el_shock2" }
            }
        };

        /// <summary>Editor, development builds and the release-probe build (tools/build.sh release-probe, DD_PERF_PROBE: the S-10 R4 FINAL
        /// tree perf cell runs -ddTreePreset perf_worst -ddStartRound 15 there). The player's release build never reads the flags.</summary>
        public static bool DevAllowed
        {
            get
            {
#if DD_PERF_PROBE
                return true;
#else
                return Application.isEditor || Debug.isDebugBuild;
#endif
            }
        }

        /// <summary>The preset named on the command line, if any (dev only).</summary>
        public static bool TryFromCommandLine(out Preset preset)
        {
            preset = default;
            if (!DevAllowed) return false;
            string name = Arg("-ddTreePreset");
            return name != null && TryGet(name, out preset);
        }

        /// <summary>
        /// The round a run starts in: <c>-ddStartRound N</c> on dev / release-probe builds, else 1. RunDirector's one hook
        /// (<c>StartRun(TreePresetsV2.RunStartRound)</c>); the round's pool, health and spawn mix start from N.
        /// </summary>
        public static int RunStartRound => Math.Max(1, StartRoundFromCommandLine());

        /// <summary>
        /// The gold a run started at <paramref name="startRound"/> brings: the Normal kill gold banked before that round
        /// (run-tree-v2 devCumulativeGold) less what the preset's free nodes cost, never below 0. The caller applies the mode scale.
        /// </summary>
        public static int StartGold(int startRound, int presetCost)
        {
            if (startRound <= 1) return 0;
            int[] cum = TreeCatalogV2.Document.devCumulativeGold;
            if (cum == null || cum.Length == 0) return 0;
            int banked = cum[Math.Min(cum.Length, startRound - 1) - 1];
            return Math.Max(0, banked - Math.Max(0, presetCost));
        }

        /// <summary>Catalog price of the preset's nodes (what <see cref="Apply"/> grants free).</summary>
        public static int CostOf(in Preset preset)
        {
            int sum = 0;
            for (int i = 0; preset.Nodes != null && i < preset.Nodes.Length; i++)
            {
                TreeNodeDefV2 n = TreeCatalogV2.Find(preset.Nodes[i]);
                if (n != null) sum += n.cost;
            }

            return sum;
        }

        /// <summary>Tests: the start round instead of the command line (null = command line).</summary>
        public static int? TestStartRound { get; set; }

        /// <summary><c>-ddStartRound N</c> (dev only), 0 when absent.</summary>
        public static int StartRoundFromCommandLine()
        {
            if (TestStartRound.HasValue) return TestStartRound.Value;
            if (!DevAllowed) return 0;
            string v = Arg("-ddStartRound");
            return v != null && int.TryParse(v, out int r) && r > 1 ? r : 0;
        }

        public static bool TryGet(string name, out Preset preset)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    preset = All[i];
                    return true;
                }
            }

            preset = default;
            return false;
        }

        /// <summary>Grants the preset's nodes free (gates and gold ignored); returns how many were newly owned.</summary>
        public static int Apply(RunTreeV2 tree, in Preset preset)
        {
            if (tree == null || !tree.IsBuilt || preset.Nodes == null) return 0;
            int granted = 0;
            for (int i = 0; i < preset.Nodes.Length; i++)
            {
                if (tree.GrantFree(preset.Nodes[i])) granted++;
            }

            Debug.Log("[TreeV2] preset " + preset.Name + ": " + granted + "/" + preset.Nodes.Length + " nodes (dev only)");
            return granted;
        }

        private static string Arg(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal)) return args[i + 1];
            }

            return null;
        }
    }
}
