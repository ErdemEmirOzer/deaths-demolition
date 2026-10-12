using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B1 · the feature-based run tree of one run: what is owned, the round-end rules (fog by round gate per mode, parents,
    /// F1 element cap, F2 chaos core, capstone ↔ awakening alternative, effect-live gate), the free-tree commands of
    /// <see cref="ITreeShopCommands"/> (KARAR-WP104-11.10 decision 1: buy anything on the frontier, recommendation strip −10 %
    /// with a 10-gold reroll, Target with its path, Hearth, one-key Continue), the interest and the mode's starting gold, and the
    /// resolved <see cref="TreeStats"/> it publishes to <see cref="TreeRuntime"/>. Never per frame: every method runs on a
    /// round-end screen command, a purchase, an equip or the run boundary.
    /// </summary>
    public sealed class RunTreeV2 : ITreeShopCommands
    {
        private bool[] _owned = new bool[0];
        private int[] _order = new int[0];
        private bool[] _availableLast = new bool[0];
        private readonly List<int> _strip = new List<int>(4);
        private readonly List<int> _pool = new List<int>(64);
        private readonly List<int> _path = new List<int>(16);
        private readonly TreeScreenSnapshot _snapshot = new TreeScreenSnapshot();
        private GoldWallet _wallet;
        private System.Random _rng;

        public bool IsBuilt { get; private set; }
        public bool IsOpen { get; private set; }
        public int RunId { get; private set; }
        public int Round { get; private set; }
        public int Mode { get; private set; }
        public TreeWeaponProfile Weapon { get; private set; }
        public TreeAwakening Awakening { get; private set; }
        public int HearthStokesTotal { get; private set; }
        public int HearthStokesThisRound { get; private set; }
        public int RerollsThisRound { get; private set; }
        public int TargetIndex { get; private set; } = -1;
        public int InterestPaidThisRound { get; private set; }
        public int NodesOwned { get; private set; }
        public TreeStats Stats { get; private set; } = TreeStats.Neutral;

        public TreeScreenSnapshot Snapshot => _snapshot;

        /// <summary>Raised after every command and on open (the screen refreshes).</summary>
        public event Action Changed;
        /// <summary>The player pressed Continue: the flow closes the screen and starts the next round.</summary>
        public event Action ContinueRequested;

        // ------------------------------------------------------------------ run boundary

        /// <summary>A new run: empty tree, the Head Start awakening's gold (the mode's starting gold is WP-102's), stats published.</summary>
        public void Begin(int runId, int mode, GoldWallet wallet, in TreeWeaponProfile weapon, in TreeAwakening awakening)
        {
            TreeCatalogDocumentV2 doc = TreeCatalogV2.Document;
            int count = doc.nodes.Length;
            _owned = new bool[count];
            _order = new int[count];
            _availableLast = new bool[count];
            _wallet = wallet;
            RunId = runId;
            Mode = mode < 0 || mode >= TreeMode.Count ? TreeMode.Normal : mode;
            Weapon = weapon;
            Awakening = awakening;
            Round = 0;
            HearthStokesTotal = 0;
            HearthStokesThisRound = 0;
            RerollsThisRound = 0;
            TargetIndex = -1;
            NodesOwned = 0;
            IsOpen = false;
            IsBuilt = true;
            _rng = new System.Random(unchecked(runId * 7919 + 104));
            _strip.Clear();

            TreeRuntime.SetMode(Mode);
            if (awakening.HeadStart && Mode != TreeMode.Demolition && wallet != null && wallet.IsOpen)
            {
                wallet.Credit(HeadStartGold, GoldSource.RunInitialization, wallet.NextTransactionId());
            }

            Republish();
        }

        /// <summary>The run ended: neutral stats, closed.</summary>
        public void End()
        {
            IsOpen = false;
            IsBuilt = false;
            TreeRuntime.Reset();
        }

        /// <summary>Awakening "Head Start" (GD-TREE §3): R1 +40 gold in Normal/Hard.</summary>
        public const int HeadStartGold = 40;

        /// <summary>The weapon in hand changed (S-02 calls on equip; wall buy included): stats re-resolve for it.</summary>
        public void OnEquip(in TreeWeaponProfile weapon)
        {
            Weapon = weapon;
            if (IsBuilt) Republish();
        }

        // ------------------------------------------------------------------ round-end screen

        /// <summary>The round-end tree opens for <paramref name="round"/>: interest, new strip, Hearth per-round count.</summary>
        public void Open(int round)
        {
            if (!IsBuilt) return;
            bool newRound = round != Round;
            Round = Mathf.Max(1, round);
            IsOpen = true;
            if (newRound)
            {
                HearthStokesThisRound = 0;
                RerollsThisRound = 0;
                InterestPaidThisRound = PayInterest();
                DrawStrip();
            }

            Refresh();
        }

        public void Continue()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ContinueRequested?.Invoke();
            Changed?.Invoke();
        }

        public bool TryBuy(int nodeIndex)
        {
            if (!IsOpen || !InRange(nodeIndex)) return false;
            TreeNodeState state = StateOf(nodeIndex);
            if (state != TreeNodeState.Available) return false;
            int price = PriceOf(nodeIndex);
            if (_wallet == null || _wallet.TrySpend(price, GoldSource.UpgradePurchase, _wallet.NextTransactionId()) != GoldTransactionResult.Applied) return false;
            Grant(nodeIndex);
            _strip.Remove(nodeIndex);
            if (TargetIndex == nodeIndex) TargetIndex = -1;
            TreeNodeDefV2 n = TreeCatalogV2.Document.nodes[nodeIndex];
            TreeEvents.RaiseNodePurchased(n.id, n.Branch, n.tier);
            Refresh();
            return true;
        }

        public void SetTarget(int nodeIndex)
        {
            TargetIndex = InRange(nodeIndex) && !_owned[nodeIndex] ? nodeIndex : -1;
            Refresh();
        }

        public bool TryReroll()
        {
            if (!IsOpen || _wallet == null) return false;
            int cost = RerollCost;
            if (_wallet.TrySpend(cost, GoldSource.OfferReroll, _wallet.NextTransactionId()) != GoldTransactionResult.Applied) return false;
            RerollsThisRound++;
            DrawStrip();
            Refresh();
            return true;
        }

        public bool TryStokeHearth()
        {
            if (!IsOpen || !HearthAvailable || _wallet == null) return false;
            if (_wallet.TrySpend(HearthCost, GoldSource.TreeHearth, _wallet.NextTransactionId()) != GoldTransactionResult.Applied) return false;
            HearthStokesTotal++;
            HearthStokesThisRound++;
            Republish();
            Refresh();
            return true;
        }

        public int RerollCost
        {
            get
            {
                TreeStripDefV2 strip = TreeCatalogV2.Document.strip;
                int baseCost = strip != null ? strip.rerollCost : 10;
                int step = strip != null ? Mathf.Max(0, strip.rerollStep) : 0;
                return Mathf.Max(1, baseCost + step * RerollsThisRound);
            }
        }

        public int HearthCost
        {
            get
            {
                TreeHearthDefV2 h = TreeCatalogV2.Document.hearth ?? new TreeHearthDefV2();
                return h.baseCost + h.costStep * HearthStokesTotal;
            }
        }

        public bool HearthAvailable
        {
            get
            {
                TreeHearthDefV2 h = TreeCatalogV2.Document.hearth ?? new TreeHearthDefV2();
                return HearthStokesThisRound < h.perRound && HearthStokesTotal < h.max;
            }
        }

        // ------------------------------------------------------------------ rules

        public bool Owns(int nodeIndex) => InRange(nodeIndex) && _owned[nodeIndex];

        /// <summary>Owned nodes of tier ≥ <paramref name="tier"/> (run summary: TreeTier3Owned).</summary>
        public int CountOwnedTierAtLeast(int tier)
        {
            TreeNodeDefV2[] nodes = TreeCatalogV2.Document.nodes;
            int count = 0;
            for (int i = 0; i < _owned.Length && i < nodes.Length; i++) if (_owned[i] && nodes[i].tier >= tier) count++;
            return count;
        }

        /// <summary>Active synergies (run summary: SynergiesOwned).</summary>
        public int SynergyCount
        {
            get
            {
                int bits = (int)Stats.Synergies, count = 0;
                while (bits != 0) { count += bits & 1; bits >>= 1; }
                return count;
            }
        }

        public bool Owns(string nodeId) => Owns(TreeCatalogV2.IndexOf(nodeId));

        public int ElementCap => Awakening.ThirdAffinity ? 3 : Mathf.Max(1, TreeCatalogV2.Document.elementCap);

        public TreeNodeState StateOf(int i)
        {
            TreeNodeDefV2 n = TreeCatalogV2.Document.nodes[i];
            if (_owned[i]) return TreeNodeState.Purchased;
            if (n.IsAwakening && (Awakening.AltCapstones & (1 << (int)n.Branch)) == 0) return TreeNodeState.AwakeningLocked;
            if (IsExcluded(i, n)) return TreeNodeState.Excluded;
            if (n.tier > TreeCatalogV2.OpenTier(Mode, Round)) return TreeNodeState.Fogged;
            if (!ParentsMet(n)) return TreeNodeState.PrereqMissing;
            if (!TreeLiveV2.IsLive(n.id)) return TreeNodeState.EffectPending;
            return _wallet != null && PriceOf(i) <= _wallet.Balance ? TreeNodeState.Available : TreeNodeState.Unaffordable;
        }

        public int PriceOf(int i)
        {
            int cost = TreeCatalogV2.Document.nodes[i].cost;
            if (!_strip.Contains(i)) return cost;
            TreeStripDefV2 strip = TreeCatalogV2.Document.strip;
            float discount = strip != null ? Mathf.Clamp01(strip.discount) : 0f;
            int off = Mathf.RoundToInt(cost * discount);
            if (strip != null && strip.discountCap > 0) off = Mathf.Min(off, strip.discountCap);
            return Mathf.Max(1, cost - off);
        }

        private bool IsExcluded(int i, TreeNodeDefV2 n)
        {
            TreeNodeDefV2[] nodes = TreeCatalogV2.Document.nodes;
            if (n.fork == 1 && n.tier == 1)
            {
                // F1: at most ElementCap element lines (tier I of another element closes when the cap is reached).
                int picked = 0;
                for (int k = 0; k < nodes.Length; k++)
                {
                    if (_owned[k] && nodes[k].fork == 1 && nodes[k].tier == 1) picked++;
                }

                return picked >= ElementCap;
            }

            if (n.fork == 2 && Awakening.TwinCores) return false;
            if (n.fork >= 2)
            {
                for (int k = 0; k < nodes.Length; k++)
                {
                    if (k != i && _owned[k] && nodes[k].fork == n.fork) return true;
                }
            }

            return false;
        }

        /// <summary>The owned node that closes <paramref name="i"/>: its fork partner, or for F1 the last element line bought.</summary>
        private int ClosedBy(int i, TreeNodeDefV2 n)
        {
            TreeNodeDefV2[] nodes = TreeCatalogV2.Document.nodes;
            int best = -1, bestOrder = -1;
            for (int k = 0; k < nodes.Length; k++)
            {
                if (k == i || !_owned[k] || nodes[k].fork != n.fork) continue;
                if (n.fork == 1 && nodes[k].tier != 1) continue;
                if (_order[k] > bestOrder) { bestOrder = _order[k]; best = k; }
            }

            return best;
        }

        private bool ParentsMet(TreeNodeDefV2 n)
        {
            int[] parents = n.ParentIndices;
            if (parents == null || parents.Length == 0) return true;
            int have = 0;
            for (int p = 0; p < parents.Length; p++)
            {
                if (parents[p] >= 0 && _owned[parents[p]]) have++;
            }

            return n.parentsMin > 0 ? have >= n.parentsMin : have == parents.Length;
        }

        private bool InRange(int i) => i >= 0 && i < _owned.Length;

        private void Grant(int i)
        {
            if (_owned[i]) return;
            TreeSynergy before = Stats.Synergies;
            int auraBefore = Stats.AuraLevel;
            _owned[i] = true;
            NodesOwned++;
            _order[i] = NodesOwned;
            Republish();
            TreeSynergy gained = Stats.Synergies & ~before;
            for (int bit = 0; bit < 16; bit++)
            {
                var s = (TreeSynergy)(1 << bit);
                if ((gained & s) != 0) TreeEvents.RaiseSynergyUnlocked(s);
            }

            if (Stats.AuraLevel != auraBefore) TreeEvents.RaiseAuraChanged(Stats.AuraLevel);
        }

        /// <summary>Dev presets and tests: own a node without gold or gates (no event to the shop's gold log).</summary>
        public bool GrantFree(string nodeId)
        {
            int i = TreeCatalogV2.IndexOf(nodeId);
            if (!InRange(i) || _owned[i]) return false;
            Grant(i);
            return true;
        }

        private void Republish()
        {
            var input = new TreeResolver.Input { Owned = _owned, Order = _order, Weapon = Weapon, Mode = Mode, HearthStokes = HearthStokesTotal, Awakening = Awakening };
            Stats = TreeResolver.Resolve(input);
            TreeRuntime.Publish(Stats);
        }

        /// <summary>ec_interest: a share of the gold carried into the round end, capped per mode (Normal 150, Demolition 400).</summary>
        private int PayInterest()
        {
            if (_wallet == null || !_wallet.IsOpen || Stats.InterestRate <= 0f) return 0;
            int amount = Mathf.Min(Mathf.FloorToInt(_wallet.Balance * Stats.InterestRate), Mathf.Max(0, Stats.InterestCap));
            if (amount <= 0) return 0;
            return _wallet.Credit(amount, GoldSource.PurseBonus, _wallet.NextTransactionId()) == GoldTransactionResult.Applied ? amount : 0;
        }

        /// <summary>The recommendation strip: up to N frontier nodes (gate open, parents met, live, not owned / closed), seeded per run.</summary>
        private void DrawStrip()
        {
            _strip.Clear();
            _pool.Clear();
            TreeNodeDefV2[] nodes = TreeCatalogV2.Document.nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                TreeNodeState s = StateOf(i);
                if (s == TreeNodeState.Available || s == TreeNodeState.Unaffordable) _pool.Add(i);
            }

            TreeStripDefV2 strip = TreeCatalogV2.Document.strip;
            int want = Mathf.Min(strip != null ? strip.count : 3, _pool.Count);
            for (int k = 0; k < want; k++)
            {
                int pick = _rng.Next(k, _pool.Count);
                (_pool[k], _pool[pick]) = (_pool[pick], _pool[k]);
                _strip.Add(_pool[k]);
            }
        }

        /// <summary>Unowned nodes on the cheapest path to the Target (Target included), parents before children.</summary>
        private void BuildTargetPath(int target)
        {
            _path.Clear();
            if (!InRange(target) || _owned[target]) return;
            AddPath(target, 0);
        }

        private void AddPath(int i, int depth)
        {
            if (depth > 8 || _owned[i] || _path.Contains(i)) return;
            TreeNodeDefV2 n = TreeCatalogV2.Document.nodes[i];
            int[] parents = n.ParentIndices;
            if (parents != null && parents.Length > 0)
            {
                if (n.parentsMin > 0)
                {
                    // Any-of parents: count owned ones, then add the cheapest missing ones.
                    int have = 0;
                    for (int p = 0; p < parents.Length; p++) if (parents[p] >= 0 && _owned[parents[p]]) have++;
                    while (have < n.parentsMin)
                    {
                        int best = -1, bestCost = int.MaxValue;
                        for (int p = 0; p < parents.Length; p++)
                        {
                            int pi = parents[p];
                            if (pi < 0 || _owned[pi] || _path.Contains(pi)) continue;
                            int c = ChainCost(pi, 0);
                            if (c < bestCost) { bestCost = c; best = pi; }
                        }

                        if (best < 0) break;
                        AddPath(best, depth + 1);
                        have++;
                    }
                }
                else
                {
                    for (int p = 0; p < parents.Length; p++) if (parents[p] >= 0) AddPath(parents[p], depth + 1);
                }
            }

            _path.Add(i);
        }

        private int ChainCost(int i, int depth)
        {
            if (depth > 8 || _owned[i]) return 0;
            TreeNodeDefV2 n = TreeCatalogV2.Document.nodes[i];
            int sum = n.cost;
            int[] parents = n.ParentIndices;
            if (parents != null && n.parentsMin == 0)
            {
                for (int p = 0; p < parents.Length; p++) if (parents[p] >= 0) sum += ChainCost(parents[p], depth + 1);
            }

            return sum;
        }

        // ------------------------------------------------------------------ snapshot

        /// <summary>Fills <see cref="Snapshot"/> in place and raises <see cref="Changed"/>.</summary>
        public void Refresh()
        {
            TreeCatalogDocumentV2 doc = TreeCatalogV2.Document;
            int count = doc.nodes.Length;
            if (_snapshot.Nodes.Length != count) _snapshot.Nodes = new TreeNodeView[count];
            if (_snapshot.Synergies.Length != doc.synergies.Length) _snapshot.Synergies = new TreeSynergyView[doc.synergies.Length];

            BuildTargetPath(TargetIndex);
            int gold = _wallet != null ? _wallet.Balance : 0;
            int targetRemaining = 0;
            for (int k = 0; k < _path.Count; k++) targetRemaining += PriceOf(_path[k]);

            for (int i = 0; i < count; i++)
            {
                TreeNodeDefV2 n = doc.nodes[i];
                TreeNodeState state = IsBuilt ? StateOf(i) : TreeNodeState.Fogged;
                int price = PriceOf(i);
                bool buyable = state == TreeNodeState.Available || state == TreeNodeState.Unaffordable;
                TreeSynergy syn = TreeSynergy.None;
                for (int s = 0; s < doc.synergies.Length; s++)
                {
                    TreeSynergyDefV2 d = doc.synergies[s];
                    if (d != null && (d.NodeA == i || d.NodeB == i)) syn |= d.Synergy;
                }

                ref TreeNodeView v = ref _snapshot.Nodes[i];
                v.NodeId = n.id;
                v.StringKey = n.stringKey;
                v.IconKey = "Tree." + n.id;
                v.IconFile = n.icon;
                v.ClosedByIndex = state == TreeNodeState.Excluded ? ClosedBy(i, n) : -1;
                v.Branch = n.Branch;
                v.Tier = n.tier;
                v.Slot = n.slot;
                v.Element = n.Element;
                v.ParentIndices = n.ParentIndices;
                v.ParentsAny = n.parentsMin > 0;
                v.ForkGroup = n.fork;
                v.State = state;
                v.RoundGate = TreeCatalogV2.TierGate(Mode, n.tier);
                v.Cost = n.cost;
                v.Price = price;
                v.Shortfall = state == TreeNodeState.Unaffordable ? Mathf.Max(0, price - gold) : 0;
                v.Recommended = _strip.Contains(i);
                v.IsTarget = i == TargetIndex;
                v.OnTargetPath = _path.Contains(i);
                v.NewlyAvailable = buyable && !_availableLast[i];
                v.IsAwakeningVariant = n.IsAwakening;
                v.Synergies = syn;
                v.ArgNames = n.DisplayNames;
                v.ArgValues = n.DisplayValues;
                _availableLast[i] = buyable;
            }

            for (int s = 0; s < doc.synergies.Length; s++)
            {
                TreeSynergyDefV2 d = doc.synergies[s];
                ref TreeSynergyView v = ref _snapshot.Synergies[s];
                v.Synergy = d.Synergy;
                v.StringKey = "tree.v2.synergy." + d.id;
                v.BadgeKey = "Tree.Synergy." + d.id;
                v.BadgeFile = d.icon;
                v.ArgNames = d.DisplayNames;
                v.ArgValues = d.DisplayValues;
                v.NodeA = d.NodeA;
                v.NodeB = d.NodeB;
                v.BranchA = d.NodeA >= 0 ? doc.nodes[d.NodeA].Branch : TreeBranch.Speed;
                v.BranchB = d.NodeB >= 0 ? doc.nodes[d.NodeB].Branch : TreeBranch.Shot;
                v.NeedsRocketInHand = d.needsRocket;
                v.Active = Stats.Has(d.Synergy);
                v.Revealed = Awakening.SynergyCodex || Owns(d.NodeA) || Owns(d.NodeB);
            }

            int open = TreeCatalogV2.OpenTier(Mode, Round);
            _snapshot.Gold = gold;
            _snapshot.Round = Round;
            _snapshot.Mode = Mode;
            _snapshot.OpenTier = open;
            _snapshot.NextTierRound = open < TreeCatalogV2.Tiers ? TreeCatalogV2.TierGate(Mode, open + 1) : 0;
            _snapshot.PowerScore = Stats.PowerScore;
            _snapshot.AuraLevel = Stats.AuraLevel;
            _snapshot.TargetIndex = TargetIndex;
            _snapshot.TargetGoldRemaining = targetRemaining;
            _snapshot.RerollCost = RerollCost;
            _snapshot.RerollAvailable = IsOpen && _wallet != null && _wallet.Balance >= RerollCost;
            _snapshot.HearthCost = HearthCost;
            _snapshot.HearthStokesThisRound = HearthStokesThisRound;
            _snapshot.HearthStokesTotal = HearthStokesTotal;
            _snapshot.HearthAvailable = IsOpen && HearthAvailable;
            _snapshot.ElementsPicked = Stats.Elements;
            _snapshot.ElementCap = ElementCap;
            _snapshot.Weapon = Weapon;
            TreeHearthDefV2 hearthDef = doc.hearth ?? new TreeHearthDefV2();
            _snapshot.HearthPerRound = hearthDef.perRound;
            _snapshot.HearthMax = hearthDef.max;
            Changed?.Invoke();
        }

        /// <summary>The strip's node indices (tests, F1).</summary>
        public IReadOnlyList<int> Strip => _strip;

        // ------------------------------------------------------------------ weapon profile

        /// <summary>The tree's view of an Arsenal weapon (B2: S-02 calls <see cref="OnEquip"/> with it on every equip / wall buy).</summary>
        public static TreeWeaponProfile ProfileOf(ClassicWeaponId id)
        {
            WeaponDefinition def = Arsenal.Get(id);
            if (def == null) return new TreeWeaponProfile { Id = id, Key = RunTreeCatalog.SlotFor(id), FireMode = WeaponFireMode.Auto, BaseRpm = 600f, BaseDamage = 10f, Pellets = 1, Magazine = 30 };
            return new TreeWeaponProfile
            {
                Id = id,
                Key = def.weaponId,
                FireMode = def.fire.fireMode,
                BaseRpm = def.fire.rpm,
                BaseDamage = def.fire.damage,
                Pellets = Mathf.Max(1, def.fire.pelletCount),
                Magazine = def.ammo.magazine,
                Projectile = def.projectile.kind
            };
        }
    }

    /// <summary>
    /// WP-104 · which V2 nodes have a live consumer (the WP-077 rule): a node whose effect no system reads yet is shown
    /// <see cref="TreeNodeState.EffectPending"/> and is never sold. The owner of a behaviour (S-02 fire path, S-03 receivers,
    /// S-04 systems) adds the node ids to this list in the commit that installs the hook.
    /// </summary>
    public static class TreeLiveV2
    {
        private static readonly HashSet<string> s_live = new HashSet<string>
        {
            // B1 (S-04): economy read by GoldRewardCalculator (gold ×) and the round-end interest.
            "ec_gold1", "ec_gold2", "ec_gold3", "ec_interest",
            // B2 (S-02 fire path, TreeHooks table): Shot / Magazine / Speed / crit, PlayerHealth + PlayerMotor, WP-107 wall buy, Midas.
            "sh_heavy", "sh_bore", "sh_double", "sh_triple", "sh_fan", "sh_pierce", "sh_ricochet",
            "mg_ext", "mg_double", "mg_reload1", "mg_reload2", "mg_autoload", "mg_drum", "mg_endless", "mg_cap",
            "sp_rate1", "sp_rate2", "sp_rate3", "sp_move", "sp_dash", "sp_fullauto", "sp_twin", "sp_cap",
            "kx_crit",
            "sv_hp1", "sv_hp2", "sv_hp3", "sv_shield", "sv_leech", "sv_dodge",
            "ec_dealer", "ec_midas",
            // B2 × B3 (S-02 HitscanEngine: TreeZombieBinding.OnWeaponHit + the shock chain, TreeBlastQueue explosive round / crit ring;
            // S-04 systems and S-03 receivers already on main): elements, the crit ring, Mini Nuke, Black Hole, Mass Collapse.
            "el_fire", "el_fire2", "el_fire3", "el_ice", "el_ice2", "el_ice3", "el_shock", "el_shock2", "el_shock3",
            "el_venom", "el_venom2", "el_venom3", "el_fusion",
            "sh_explosive", "kx_crit2", "kx_nuke", "kx_hole", "kx_mass",
            // S-04 guard waves (TreeHooks → TreeSystemsRunner.GuardWave → S-03 ApplyStun / ApplyPull) and Quick Hands III
            // (reload ×0.45 through ReloadTimeScale; the game has no reload movement penalty, so ReloadMoveFree adds nothing).
            "sv_bulwark", "sv_cap", "mg_reload3",
            // B2b (S-02): Rail Line's pierce stagger (S-03 ApplyTreeStagger), Bouncing Storm's bounce seek, Seeker Swarm's homing shots;
            // and S-04's B3 systems already wired on main: Chain Reaction (TreeZombieBinding → OnBodyDied), Element Nova and Doomsday
            // Bell (the driver's player anchor every frame).
            "sh_rail", "sh_storm", "sh_cap", "kx_chain", "el_cap", "kx_cap",
            // WP-104 B6 (S-04): the seven Awakening alternative capstones - Ring of Ruin (PlanVolley), Ammo Fountain (OnKill +
            // AutoReloadTick + ShotDamageScale), Blink Fire (OnKill / dodge + RateScale), Elemental Avatar (runner + OnWeaponHit),
            // Event Horizon (TreeGravityWells.TickHorizon), Phoenix (IncomingDamage + PhoenixBlast), Golden Rain (Tick / OnGoldSpent).
            "aw_alt_sh", "aw_alt_mg", "aw_alt_sp", "aw_alt_el", "aw_alt_kx", "aw_alt_sv", "aw_alt_ec"
        };

        /// <summary>Dev presets / F1 / tests: every node sells (the effect may still be a no-op until its hook lands).</summary>
        public static bool TreatAllAsLive { get; set; }

        public static bool IsLive(string nodeId) => TreatAllAsLive || (nodeId != null && s_live.Contains(nodeId));

        public static int LiveCount => s_live.Count;
    }
}
