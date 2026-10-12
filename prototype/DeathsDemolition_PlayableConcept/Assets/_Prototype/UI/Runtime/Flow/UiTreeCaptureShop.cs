#if DEVELOPMENT_BUILD || UNITY_EDITOR
// WP-104 B4 (S-09): FAKE DATA for captures and tests - development builds and the Editor only, never in a release player.
using System;
using System.Collections.Generic;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-104 B4 (S-09) · FAKE DATA - a stand-in <see cref="ITreeShopCommands"/> for the tree screen's captures
    /// (<c>-ddUiCapture tree</c>, S-10's request) and its PlayMode tests, used only while S-04 B1's provider is absent
    /// (<see cref="UiTreeScreenRoute.Override"/>). It is NOT the game's tree: the rules here are a minimal reading of the design
    /// (round gates I R1 · II R3 · III R6 · IV R10 · V R14, parents, F1 two elements, F2 one core, a capstone or its Awakening
    /// alternative, strip −10 % with a 10-gold reroll, Hearth 50 + 30 n, three a round) so every state reaches a frame; the node
    /// table mirrors the B1 catalog's ids / tiers / slots (run-tree-v2.json, 11.10 draft) and its prices, and only a handful of
    /// nodes carry placeholder arguments. B1's <c>RunTreeV2</c> is the provider the game uses. Records every command (tests).
    /// This copy (after S-04 B5a) also fills B5a's fields: the closer index, synergy arguments, the Hearth's caps.
    /// </summary>
    public sealed class UiTreeCaptureShop : ITreeShopCommands
    {
        /// <summary>id | branch | tier | slot | element | parents | parentsMin | fork | cost | awakening (B1 catalog shape).</summary>
        private static readonly string[] s_nodes =
        {
            "sh_heavy|Shot|1|0|None||0|0|45|0", "sh_bore|Shot|1|1|None||0|0|40|0", "sh_double|Shot|2|0|None|sh_heavy|0|0|160|0",
            "sh_pierce|Shot|2|1|None|sh_heavy|0|0|150|0", "sh_ricochet|Shot|2|2|None|sh_bore|0|0|140|0",
            "sh_triple|Shot|3|0|None|sh_double|0|0|500|0", "sh_rail|Shot|3|1|None|sh_pierce|0|0|450|0",
            "sh_storm|Shot|3|2|None|sh_ricochet|0|0|420|0", "sh_explosive|Shot|3|3|None|sh_bore|0|0|520|0",
            "sh_fan|Shot|4|0|None|sh_triple|0|0|1100|0", "sh_cap|Shot|5|0|None|sh_fan|0|10|2300|0", "aw_alt_sh|Shot|5|1|None|sh_fan|0|10|2300|1",
            "mg_ext|Magazine|1|0|None||0|0|40|0", "mg_reload1|Magazine|1|1|None||0|0|40|0", "mg_double|Magazine|2|0|None|mg_ext|0|0|160|0",
            "mg_reload2|Magazine|2|1|None|mg_reload1|0|0|130|0", "mg_reload3|Magazine|3|0|None|mg_reload2|0|0|380|0",
            "mg_autoload|Magazine|3|1|None|mg_reload2|0|0|420|0", "mg_drum|Magazine|4|0|None|mg_double|0|0|950|0",
            "mg_endless|Magazine|4|1|None|mg_autoload|0|0|1050|0", "mg_cap|Magazine|5|0|None|mg_endless|0|11|2100|0",
            "aw_alt_mg|Magazine|5|1|None|mg_endless|0|11|2100|1",
            "sp_rate1|Speed|1|0|None||0|0|45|0", "sp_move|Speed|1|1|None||0|0|35|0", "sp_rate2|Speed|2|0|None|sp_rate1|0|0|150|0",
            "sp_dash|Speed|2|1|None|sp_move|0|0|130|0", "sp_rate3|Speed|3|0|None|sp_rate2|0|0|450|0",
            "sp_fullauto|Speed|3|1|None|sp_rate2|0|0|650|0", "sp_twin|Speed|4|0|None|sp_rate3|0|0|1100|0",
            "sp_cap|Speed|5|0|None|sp_twin|0|12|2200|0", "aw_alt_sp|Speed|5|1|None|sp_twin|0|12|2200|1",
            "el_fire|Element|1|0|Fire||0|1|50|0", "el_ice|Element|1|1|Ice||0|1|50|0", "el_shock|Element|1|2|Shock||0|1|50|0",
            "el_venom|Element|1|3|Venom||0|1|50|0", "el_fire2|Element|2|0|Fire|el_fire|0|0|170|0", "el_ice2|Element|2|1|Ice|el_ice|0|0|170|0",
            "el_shock2|Element|2|2|Shock|el_shock|0|0|170|0", "el_venom2|Element|2|3|Venom|el_venom|0|0|170|0",
            "el_fire3|Element|3|0|Fire|el_fire2|0|0|480|0", "el_ice3|Element|3|1|Ice|el_ice2|0|0|480|0",
            "el_shock3|Element|3|2|Shock|el_shock2|0|0|480|0", "el_venom3|Element|3|3|Venom|el_venom2|0|0|480|0",
            "el_fusion|Element|4|0|None|el_fire2,el_ice2,el_shock2,el_venom2|2|0|1150|0", "el_cap|Element|5|0|None|el_fusion|0|13|2300|0",
            "aw_alt_el|Element|5|1|None|el_fusion|0|13|2300|1",
            "kx_crit|Chaos|1|0|None||0|0|50|0", "kx_crit2|Chaos|2|0|None|kx_crit|0|0|170|0", "kx_chain|Chaos|2|1|None||0|0|180|0",
            "kx_nuke|Chaos|3|0|None|kx_crit2|0|2|600|0", "kx_hole|Chaos|3|1|None|kx_chain|0|2|600|0",
            "kx_mass|Chaos|4|0|None|kx_nuke,kx_hole|1|0|1100|0", "kx_cap|Chaos|5|0|None|kx_mass|0|14|2300|0",
            "aw_alt_kx|Chaos|5|1|None|kx_mass|0|14|2300|1",
            "sv_hp1|Survival|1|0|None||0|0|40|0", "sv_hp2|Survival|2|0|None|sv_hp1|0|0|130|0", "sv_shield|Survival|2|1|None|sv_hp1|0|0|170|0",
            "sv_leech|Survival|2|2|None|sv_hp1|0|0|160|0", "sv_hp3|Survival|3|0|None|sv_hp2|0|0|400|0",
            "sv_dodge|Survival|3|1|None|sv_leech|0|0|420|0", "sv_bulwark|Survival|4|0|None|sv_shield,sv_hp3|0|0|950|0",
            "sv_cap|Survival|5|0|None|sv_bulwark|0|15|2000|0", "aw_alt_sv|Survival|5|1|None|sv_bulwark|0|15|2000|1",
            "ec_gold1|Economy|1|0|None||0|0|40|0", "ec_dealer|Economy|1|1|None||0|0|45|0", "ec_gold2|Economy|2|0|None|ec_gold1|0|0|150|0",
            "ec_interest|Economy|2|1|None|ec_gold1|0|0|180|0", "ec_gold3|Economy|3|0|None|ec_gold2|0|0|450|0",
            "ec_midas|Economy|4|0|None|ec_gold3|0|16|1000|0", "aw_alt_ec|Economy|4|1|None|ec_gold3|0|16|1000|1"
        };

        /// <summary>name | node a | node b | needs the rocket launcher in hand.</summary>
        private static readonly string[] s_synergies =
        {
            "Barrage|sp_fullauto||1", "FrozenGravity|el_ice2|kx_hole|0", "ThunderCascade|el_shock|kx_chain|0",
            "PinballStorm|sh_double|sh_ricochet|0", "UnbrokenLine|mg_endless|sp_fullauto|0", "WildfireRounds|el_fire2|sh_explosive|0",
            "ToxicRail|el_venom|sh_pierce|0", "CriticalMeltdown|kx_crit2|kx_nuke|0", "JudgmentLine|sh_rail|sh_explosive|0",
            "GoldenGun|ec_midas|kx_crit|0"
        };

        /// <summary>Placeholder arguments in display units for the nodes a capture details (fake: design mid-values).</summary>
        private static readonly string[] s_args =
        {
            "sh_heavy|dmg=15", "sh_bore|scale=50,dmg=5", "sh_double|extra=1,arc=6,extraDmg=60", "sh_triple|extra=2,arc=12,extraDmg=55",
            "sh_fan|extra=4,arc=40,extraDmg=45", "mg_ext|mag=50", "mg_reload1|reload=20", "sp_rate1|rpm=12", "sp_twin|rpm=35,freeEvery=2",
            "sp_fullauto|dpsTarget=50,autoRpm=30", "el_fire|dot=15,seconds=3", "el_fire2|dot=25,ground=2,groundSeconds=3",
            "el_fire3|dot=35,spread=1.5,vuln=15", "kx_crit|chance=10,mult=2.5,pity=8", "kx_nuke|k=10,radius=5,share=300,stun=1",
            "sv_hp1|hp=15", "ec_gold1|gold=10"
        };

        private static readonly int[] s_gates = { 1, 3, 6, 10, 14 };

        private readonly TreeScreenSnapshot _snapshot = new TreeScreenSnapshot();
        private readonly string[] _ids;
        private readonly int[] _cost;
        private readonly int[][] _parents;
        private readonly bool[] _parentsAny;
        private readonly int[] _fork;
        private readonly bool[] _awakening;
        private readonly bool[] _owned;
        private readonly bool[] _availableLast;
        private readonly List<int> _strip = new List<int>(3);
        private readonly int[] _synA;
        private readonly int[] _synB;
        private readonly bool[] _synRocket;
        private int _seed = 104;

        public int Gold;
        public int Round = 6;
        /// <summary>Awakening alternatives the fake profile owns (none: they read AWAKENING LOCKED).</summary>
        public bool AwakeningOwned;

        public int BuyCalls { get; private set; }
        public int TargetCalls { get; private set; }
        public int RerollCalls { get; private set; }
        public int HearthCalls { get; private set; }
        public int ContinueCalls { get; private set; }
        public int LastBuyIndex { get; private set; } = -1;
        public int LastTargetIndex { get; private set; } = -2;

        public TreeScreenSnapshot Snapshot => _snapshot;
        public int NodeCount => _ids.Length;

        public UiTreeCaptureShop(int gold = 1200, int round = 6)
        {
            Gold = gold;
            Round = round;
            int n = s_nodes.Length;
            _ids = new string[n];
            _cost = new int[n];
            _parents = new int[n][];
            _parentsAny = new bool[n];
            _fork = new int[n];
            _awakening = new bool[n];
            _owned = new bool[n];
            _availableLast = new bool[n];
            _snapshot.Nodes = new TreeNodeView[n];
            var parentIds = new string[n];
            for (int i = 0; i < n; i++)
            {
                string[] f = s_nodes[i].Split('|');
                _ids[i] = f[0];
                ref TreeNodeView v = ref _snapshot.Nodes[i];
                v.NodeId = f[0];
                v.StringKey = "tree.v2." + f[0];
                v.IconKey = TreeArt.KeyPrefix + f[0];
                v.Branch = (TreeBranch)Enum.Parse(typeof(TreeBranch), f[1]);
                v.Tier = int.Parse(f[2]);
                v.Slot = int.Parse(f[3]);
                v.Element = f[4] == "None" ? TreeElement.None : (TreeElement)Enum.Parse(typeof(TreeElement), f[4]);
                parentIds[i] = f[5];
                _parentsAny[i] = int.Parse(f[6]) > 0;
                _fork[i] = int.Parse(f[7]);
                _cost[i] = int.Parse(f[8]);
                _awakening[i] = f[9] == "1";
                v.RoundGate = s_gates[v.Tier - 1];
                v.Cost = _cost[i];
                v.ParentsAny = _parentsAny[i];
                v.ForkGroup = _fork[i];
                v.IsAwakeningVariant = _awakening[i];
            }

            for (int i = 0; i < n; i++)
            {
                string[] p = parentIds[i].Length == 0 ? new string[0] : parentIds[i].Split(',');
                _parents[i] = new int[p.Length];
                for (int k = 0; k < p.Length; k++)
                {
                    _parents[i][k] = IndexOf(p[k]);
                }

                _snapshot.Nodes[i].ParentIndices = _parents[i];
            }

            for (int a = 0; a < s_args.Length; a++)
            {
                string[] f = s_args[a].Split('|');
                int i = IndexOf(f[0]);
                string[] pairs = f[1].Split(',');
                var names = new string[pairs.Length];
                var values = new float[pairs.Length];
                for (int k = 0; k < pairs.Length; k++)
                {
                    string[] kv = pairs[k].Split('=');
                    names[k] = kv[0];
                    values[k] = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                }

                _snapshot.Nodes[i].ArgNames = names;
                _snapshot.Nodes[i].ArgValues = values;
            }

            int s = s_synergies.Length;
            _snapshot.Synergies = new TreeSynergyView[s];
            _synA = new int[s];
            _synB = new int[s];
            _synRocket = new bool[s];
            for (int k = 0; k < s; k++)
            {
                string[] f = s_synergies[k].Split('|');
                _synA[k] = IndexOf(f[1]);
                _synB[k] = f[2].Length > 0 ? IndexOf(f[2]) : -1;
                _synRocket[k] = f[3] == "1";
                ref TreeSynergyView v = ref _snapshot.Synergies[k];
                v.Synergy = (TreeSynergy)Enum.Parse(typeof(TreeSynergy), f[0]);
                v.StringKey = "tree.v2.synergy." + f[0];
                v.BadgeKey = TreeArt.SynergyKeyPrefix + f[0];
                v.NodeA = _synA[k];
                v.NodeB = _synB[k];
                v.BranchA = _synA[k] >= 0 ? _snapshot.Nodes[_synA[k]].Branch : TreeBranch.Speed;
                v.BranchB = _synB[k] >= 0 ? _snapshot.Nodes[_synB[k]].Branch : TreeBranch.Shot;
                v.NeedsRocketInHand = _synRocket[k];
                if (f[0] == "PinballStorm")
                {
                    v.ArgNames = new[] { "seek" };
                    v.ArgValues = new[] { 20f };
                }
                for (int side = 0; side < 2; side++)
                {
                    int node = side == 0 ? _synA[k] : _synB[k];
                    if (node >= 0)
                    {
                        _snapshot.Nodes[node].Synergies |= v.Synergy;
                    }
                }
            }

            _snapshot.TargetIndex = -1;
            _snapshot.RerollCost = 10;
            _snapshot.ElementCap = 2;
            DrawStrip();
            Refresh();
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _ids.Length; i++)
            {
                if (_ids[i] == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Capture staging: owns <paramref name="id"/> without paying (a played run's tree).</summary>
        public void Grant(string id)
        {
            int i = IndexOf(id);
            if (i >= 0)
            {
                _owned[i] = true;
                Refresh();
            }
        }

        public int OpenTier
        {
            get
            {
                int open = 0;
                for (int t = 0; t < s_gates.Length; t++)
                {
                    if (Round >= s_gates[t])
                    {
                        open = t + 1;
                    }
                }

                return open;
            }
        }

        public TreeNodeState StateOf(int i)
        {
            if (_owned[i]) return TreeNodeState.Purchased;
            if (_awakening[i] && !AwakeningOwned) return TreeNodeState.AwakeningLocked;
            if (_snapshot.Nodes[i].Tier > OpenTier) return TreeNodeState.Fogged;
            if (Excluded(i)) return TreeNodeState.Excluded;
            if (!ParentsMet(i)) return TreeNodeState.PrereqMissing;
            return Price(i) > Gold ? TreeNodeState.Unaffordable : TreeNodeState.Available;
        }

        private bool ParentsMet(int i)
        {
            int[] p = _parents[i];
            if (p.Length == 0) return true;
            int owned = 0;
            for (int k = 0; k < p.Length; k++) if (p[k] >= 0 && _owned[p[k]]) owned++;
            return _parentsAny[i] ? owned >= 2 || (owned >= 1 && p.Length <= 2) : owned == p.Length;
        }

        private bool Excluded(int i)
        {
            TreeNodeView n = _snapshot.Nodes[i];
            if (n.Branch == TreeBranch.Element && n.Element != TreeElement.None)
            {
                TreeElement picked = PickedElements();
                return (picked & n.Element) == 0 && CountElements(picked) >= _snapshot.ElementCap;
            }

            if (_fork[i] == 0) return false;
            for (int k = 0; k < _ids.Length; k++)
            {
                if (k != i && _owned[k] && _fork[k] == _fork[i]) return true;
            }

            return false;
        }

        /// <summary>B5a's ClosedByIndex: F1 - the last element line taken (its tier-I node); a fork - the owned node of its group.</summary>
        private int CloserOf(int i)
        {
            TreeNodeView n = _snapshot.Nodes[i];
            int last = -1;
            for (int k = 0; k < _ids.Length; k++)
            {
                if (k == i || !_owned[k]) continue;
                TreeNodeView o = _snapshot.Nodes[k];
                bool element = n.Branch == TreeBranch.Element && n.Element != TreeElement.None && o.Branch == TreeBranch.Element &&
                               o.Element != TreeElement.None && o.Tier == 1;
                bool fork = _fork[i] != 0 && _fork[k] == _fork[i];
                if (element || fork) last = k;
            }

            return last;
        }

        private TreeElement PickedElements()
        {
            TreeElement picked = TreeElement.None;
            for (int k = 0; k < _ids.Length; k++)
            {
                if (_owned[k] && _snapshot.Nodes[k].Branch == TreeBranch.Element) picked |= _snapshot.Nodes[k].Element;
            }

            return picked;
        }

        private static int CountElements(TreeElement e)
        {
            int c = 0;
            for (int v = (int)e; v != 0; v &= v - 1) c++;
            return c;
        }

        private int Price(int i)
        {
            return _strip.Contains(i) ? Mathf.RoundToInt(_cost[i] * 0.9f) : _cost[i];
        }

        private void DrawStrip()
        {
            _strip.Clear();
            var candidates = new List<int>(16);
            for (int i = 0; i < _ids.Length; i++)
            {
                TreeNodeState st = StateOf(i);
                if (st == TreeNodeState.Available || st == TreeNodeState.Unaffordable) candidates.Add(i);
            }

            var rng = new System.Random(_seed++);
            while (_strip.Count < 3 && candidates.Count > 0)
            {
                int pick = rng.Next(candidates.Count);
                _strip.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
        }

        public bool TryBuy(int nodeIndex)
        {
            BuyCalls++;
            LastBuyIndex = nodeIndex;
            if (nodeIndex < 0 || nodeIndex >= _ids.Length || StateOf(nodeIndex) != TreeNodeState.Available) return false;
            Gold -= Price(nodeIndex);
            _owned[nodeIndex] = true;
            _strip.Remove(nodeIndex);
            if (_snapshot.TargetIndex == nodeIndex) _snapshot.TargetIndex = -1;
            Refresh();
            return true;
        }

        public void SetTarget(int nodeIndex)
        {
            TargetCalls++;
            LastTargetIndex = nodeIndex;
            _snapshot.TargetIndex = nodeIndex >= 0 && nodeIndex < _ids.Length && !_owned[nodeIndex] ? nodeIndex : -1;
            Refresh();
        }

        public bool TryReroll()
        {
            RerollCalls++;
            if (Gold < _snapshot.RerollCost) return false;
            Gold -= _snapshot.RerollCost;
            DrawStrip();
            Refresh();
            return true;
        }

        private int _stokesThisRound;
        private int _stokesTotal;

        public bool TryStokeHearth()
        {
            HearthCalls++;
            int cost = 50 + 30 * _stokesTotal;
            if (_stokesThisRound >= 3 || _stokesTotal >= 20 || Gold < cost) return false;
            Gold -= cost;
            _stokesThisRound++;
            _stokesTotal++;
            Refresh();
            return true;
        }

        public void Continue()
        {
            ContinueCalls++;
        }

        private readonly List<int> _path = new List<int>(16);

        private void BuildPath(int i, int depth)
        {
            if (i < 0 || depth > 8 || _owned[i] || _path.Contains(i)) return;
            int[] p = _parents[i];
            if (_parentsAny[i])
            {
                int need = p.Length > 2 ? 2 : 1;
                for (int k = 0; k < p.Length && need > 0; k++)
                {
                    if (p[k] >= 0 && _owned[p[k]]) need--;
                }

                for (int k = 0; k < p.Length && need > 0; k++)
                {
                    if (p[k] >= 0 && !_owned[p[k]])
                    {
                        BuildPath(p[k], depth + 1);
                        need--;
                    }
                }
            }
            else
            {
                for (int k = 0; k < p.Length; k++) BuildPath(p[k], depth + 1);
            }

            _path.Add(i);
        }

        /// <summary>Fills the snapshot in place (the B1 refresh's shape).</summary>
        public void Refresh()
        {
            _path.Clear();
            BuildPath(_snapshot.TargetIndex, 0);
            int remaining = 0;
            for (int k = 0; k < _path.Count; k++) remaining += Price(_path[k]);
            for (int i = 0; i < _ids.Length; i++)
            {
                ref TreeNodeView v = ref _snapshot.Nodes[i];
                v.State = StateOf(i);
                v.Price = Price(i);
                v.Shortfall = v.State == TreeNodeState.Unaffordable ? Mathf.Max(0, v.Price - Gold) : 0;
                v.Recommended = _strip.Contains(i);
                v.IsTarget = i == _snapshot.TargetIndex;
                v.OnTargetPath = _path.Contains(i);
                bool buyable = v.State == TreeNodeState.Available || v.State == TreeNodeState.Unaffordable;
                v.NewlyAvailable = buyable && !_availableLast[i];
                v.ClosedByIndex = v.State == TreeNodeState.Excluded ? CloserOf(i) : -1;
                _availableLast[i] = buyable;
            }

            for (int k = 0; k < _snapshot.Synergies.Length; k++)
            {
                ref TreeSynergyView v = ref _snapshot.Synergies[k];
                bool a = _synA[k] >= 0 && _owned[_synA[k]];
                bool b = _synB[k] < 0 || _owned[_synB[k]];
                v.Active = a && b && !_synRocket[k];
                v.Revealed = (_synA[k] >= 0 && _owned[_synA[k]]) || (_synB[k] >= 0 && _owned[_synB[k]]);
            }

            int open = OpenTier;
            _snapshot.Gold = Gold;
            _snapshot.Round = Round;
            _snapshot.Mode = TreeMode.Normal;
            _snapshot.OpenTier = open;
            _snapshot.NextTierRound = open < s_gates.Length ? s_gates[open] : 0;
            _snapshot.TargetGoldRemaining = remaining;
            _snapshot.RerollAvailable = Gold >= _snapshot.RerollCost;
            _snapshot.HearthCost = 50 + 30 * _stokesTotal;
            _snapshot.HearthStokesThisRound = _stokesThisRound;
            _snapshot.HearthStokesTotal = _stokesTotal;
            _snapshot.HearthAvailable = _stokesThisRound < 3 && _stokesTotal < 20;
            _snapshot.HearthPerRound = 3;
            _snapshot.HearthMax = 20;
            _snapshot.ElementsPicked = PickedElements();
        }
    }
}
#endif
