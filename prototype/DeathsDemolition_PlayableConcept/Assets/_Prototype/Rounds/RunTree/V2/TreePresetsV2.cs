using System;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B1 · dev-only late-round builds (KARAR-WP104-11.10: approved; S-10 FINAL scenarios, S-11 WP-110 capture, the perf gate).
    /// <c>-ddTreePreset &lt;name&gt;</c> grants the preset's nodes free at run start; <c>-ddStartRound N</c> is read here and handed to
    /// the round flow (the jump itself lands with B5 / S-01). Editor, development and release-probe builds only: the player path never sees it,
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

        /// <summary><c>-ddStartRound N</c> (dev only), 0 when absent.</summary>
        public static int StartRoundFromCommandLine()
        {
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
