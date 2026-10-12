using System;
using System.Collections.Generic;
using System.Linq;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B1 · özellik bazlı ağaç: katalog (63 + 7 Uyanış + 10 sinerji, tasarım bütçesi), mod başına sis kapıları, ebeveyn /
    /// "en az N ebeveyn", F1 element sınırı, F2 çekirdek, capstone ↔ Uyanış alternatifi, etki-canlı kapısı, serbest satın alma,
    /// öneri şeridi (−%10, reroll), Hedef yolu, Hearth, faiz, mod başlangıç altını, çözümleyici (taramalı sınıf tablosu, Barrage,
    /// rpm tavanı → hasar), altın ölçeği (mod × ağaç, kesir taşıma), dev ön ayarları.
    /// </summary>
    [TestFixture]
    public sealed class TreeV2Tests
    {
        private GoldWallet _wallet;
        private RunTreeV2 _tree;

        private static TreeWeaponProfile Rl => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.Rl1, Key = "rl_1", FireMode = WeaponFireMode.Single, BaseRpm = 20f, BaseDamage = 110f, Pellets = 1, Magazine = 1,
            Projectile = ProjectileKind.Rocket
        };

        private static TreeWeaponProfile Ar => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.AssaultRifle, Key = "ar_1", FireMode = WeaponFireMode.Auto, BaseRpm = 600f, BaseDamage = 12f, Pellets = 1, Magazine = 30
        };

        private static TreeWeaponProfile Smg => new TreeWeaponProfile
        {
            Id = ClassicWeaponId.AutoSmg, Key = "smg_1", FireMode = WeaponFireMode.Auto, BaseRpm = 900f, BaseDamage = 8f, Pellets = 1, Magazine = 42
        };

        [SetUp]
        public void SetUp()
        {
            TreeCatalogV2.SetForTests(null);
            TreeLiveV2.TreatAllAsLive = true;
            TreeRuntime.Reset();
            TreeEconomyV2.ResetCarry();
            _wallet = new GoldWallet();
            _wallet.InitializeForRun(1);
            _tree = new RunTreeV2();
        }

        [TearDown]
        public void TearDown()
        {
            TreeLiveV2.TreatAllAsLive = false;
            TreeRuntime.Reset();
            TreeEvents.ResetForTests();
            TreeEconomyV2.ResetCarry();
        }

        private void Begin(int mode = TreeMode.Normal, TreeWeaponProfile? weapon = null, TreeAwakening? awakening = null)
        {
            _tree.Begin(1, mode, _wallet, weapon ?? Ar, awakening ?? TreeAwakening.None);
        }

        private int Idx(string id) => TreeCatalogV2.IndexOf(id);

        private void Give(int gold) => _wallet.Credit(gold, GoldSource.EnemyPickup, _wallet.NextTransactionId());

        // ------------------------------------------------------------------ catalog

        [Test]
        public void Catalog_MatchesApprovedDesign()
        {
            TreeCatalogDocumentV2 doc = TreeCatalogV2.Document;
            CollectionAssert.IsEmpty(TreeCatalogV2.Validate(doc));
            TreeNodeDefV2[] baseNodes = doc.nodes.Where(n => !n.IsAwakening).ToArray();
            Assert.AreEqual(63, baseNodes.Length, "design §2: 63 nodes");
            Assert.AreEqual(7, doc.nodes.Count(n => n.IsAwakening), "one awakening alternative per branch");
            Assert.AreEqual(10, doc.synergies.Length);
            Assert.AreEqual(33242, baseNodes.Sum(n => n.cost), "GD-TREE §2 prices with the B5a ladder (III ×1,25, IV ×1,15, V ×0,75)");
            var perBranch = new Dictionary<TreeBranch, int> { [TreeBranch.Shot] = 11, [TreeBranch.Magazine] = 9, [TreeBranch.Speed] = 8, [TreeBranch.Element] = 14,
                [TreeBranch.Chaos] = 7, [TreeBranch.Survival] = 8, [TreeBranch.Economy] = 6 };
            foreach (var kv in perBranch) Assert.AreEqual(kv.Value, baseNodes.Count(n => n.Branch == kv.Key), kv.Key.ToString());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, TreeCatalogV2.Mode(TreeMode.Demolition).tierGates);
            Assert.AreEqual(400, TreeCatalogV2.Mode(TreeMode.Demolition).interestCap);
        }

        [Test]
        public void Fog_FollowsModeGates()
        {
            Assert.AreEqual(1, TreeCatalogV2.OpenTier(TreeMode.Normal, 1));
            Assert.AreEqual(2, TreeCatalogV2.OpenTier(TreeMode.Normal, 3));
            Assert.AreEqual(4, TreeCatalogV2.OpenTier(TreeMode.Normal, 10));
            Assert.AreEqual(5, TreeCatalogV2.OpenTier(TreeMode.Normal, 14));
            Assert.AreEqual(3, TreeCatalogV2.OpenTier(TreeMode.Hard, 5));
            Assert.AreEqual(5, TreeCatalogV2.OpenTier(TreeMode.Demolition, 5));

            Begin();
            Give(5000);
            _tree.Open(1);
            Assert.AreEqual(TreeNodeState.Available, _tree.StateOf(Idx("sh_heavy")));
            Assert.AreEqual(TreeNodeState.Fogged, _tree.StateOf(Idx("sh_double")));
            _tree.TryBuy(Idx("sh_heavy"));
            _tree.Open(3);
            Assert.AreEqual(TreeNodeState.Available, _tree.StateOf(Idx("sh_double")));
            Assert.AreEqual(TreeNodeState.Fogged, _tree.StateOf(Idx("sh_triple")));
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void Purchase_SpendsGold_ParentsAndAffordability()
        {
            Begin();
            _tree.Open(14);
            Assert.AreEqual(TreeNodeState.Unaffordable, _tree.StateOf(Idx("sh_heavy")));
            Give(200);
            _tree.Refresh();
            Assert.AreEqual(TreeNodeState.PrereqMissing, _tree.StateOf(Idx("sh_double")));
            int price = _tree.PriceOf(Idx("sh_heavy"));
            Assert.IsTrue(_tree.TryBuy(Idx("sh_heavy")));
            Assert.AreEqual(200 - price, _wallet.Balance);
            Assert.AreEqual(TreeNodeState.Purchased, _tree.StateOf(Idx("sh_heavy")));
            Assert.IsFalse(_tree.TryBuy(Idx("sh_heavy")), "owned");
            Assert.AreNotEqual(TreeNodeState.PrereqMissing, _tree.StateOf(Idx("sh_double")));
            Assert.AreEqual(1.15f, TreeRuntime.Current.DamageScale, 1e-4f, "published");
        }

        [Test]
        public void EffectPending_NotSoldUntilLive()
        {
            TreeLiveV2.TreatAllAsLive = false;
            Begin();
            Give(500);
            _tree.Open(1);
            Assert.AreEqual(TreeNodeState.EffectPending, _tree.StateOf(Idx("sh_heavy")));
            Assert.IsFalse(_tree.TryBuy(Idx("sh_heavy")));
            Assert.AreEqual(TreeNodeState.Available, _tree.StateOf(Idx("ec_gold1")), "B1 wires Coin Sense");
        }

        [Test]
        public void F1_ElementCap_ThirdAffinity()
        {
            Begin();
            Give(2000);
            _tree.Open(1);
            Assert.IsTrue(_tree.TryBuy(Idx("el_fire")));
            Assert.IsTrue(_tree.TryBuy(Idx("el_ice")));
            Assert.AreEqual(TreeNodeState.Excluded, _tree.StateOf(Idx("el_shock")));
            Assert.AreEqual(TreeNodeState.Excluded, _tree.StateOf(Idx("el_venom")));

            var aw = TreeAwakening.None;
            aw.ThirdAffinity = true;
            _tree = new RunTreeV2();
            Begin(awakening: aw);
            _tree.Open(1);
            _tree.TryBuy(Idx("el_fire"));
            _tree.TryBuy(Idx("el_ice"));
            Assert.IsTrue(_tree.TryBuy(Idx("el_shock")), "three with Third Affinity");
            Assert.AreEqual(TreeNodeState.Excluded, _tree.StateOf(Idx("el_venom")));
        }

        [Test]
        public void F2_ChaosCore_TwinCores_AndAnyParent()
        {
            Begin();
            Give(10000);
            _tree.Open(6);
            foreach (string id in new[] { "kx_crit", "kx_crit2", "kx_chain", "kx_nuke" }) Assert.IsTrue(_tree.TryBuy(Idx(id)), id);
            Assert.AreEqual(TreeNodeState.Excluded, _tree.StateOf(Idx("kx_hole")));
            _tree.Open(10);
            Assert.AreEqual(TreeNodeState.Available, _tree.StateOf(Idx("kx_mass")), "Critical Mass: nuke OR hole");

            var aw = TreeAwakening.None;
            aw.TwinCores = true;
            _tree = new RunTreeV2();
            Begin(awakening: aw);
            _tree.Open(6);
            foreach (string id in new[] { "kx_crit", "kx_crit2", "kx_chain", "kx_nuke" }) _tree.TryBuy(Idx(id));
            Assert.IsTrue(_tree.TryBuy(Idx("kx_hole")), "Twin Cores");
            Assert.AreEqual(TreeChaosCore.Nuke | TreeChaosCore.Hole, TreeRuntime.Current.Core);
        }

        [Test]
        public void Fusion_NeedsTwoElementLines()
        {
            Begin();
            Give(10000);
            _tree.Open(10);
            foreach (string id in new[] { "el_fire", "el_fire2" }) _tree.TryBuy(Idx(id));
            Assert.AreEqual(TreeNodeState.PrereqMissing, _tree.StateOf(Idx("el_fusion")));
            foreach (string id in new[] { "el_ice", "el_ice2" }) _tree.TryBuy(Idx(id));
            Assert.AreEqual(TreeNodeState.Available, _tree.StateOf(Idx("el_fusion")));
        }

        [Test]
        public void Capstone_AwakeningAlternative()
        {
            Begin();
            Give(20000);
            _tree.Open(14);
            Assert.AreEqual(TreeNodeState.AwakeningLocked, _tree.StateOf(Idx("aw_alt_sh")));

            var aw = TreeAwakening.None;
            aw.AltCapstones = (byte)(1 << (int)TreeBranch.Shot);
            _tree = new RunTreeV2();
            Begin(awakening: aw);
            _tree.Open(14);
            foreach (string id in new[] { "sh_heavy", "sh_double", "sh_triple", "sh_fan" }) Assert.IsTrue(_tree.TryBuy(Idx(id)), id);
            Assert.AreEqual(TreeNodeState.Available, _tree.StateOf(Idx("aw_alt_sh")));
            Assert.IsTrue(_tree.TryBuy(Idx("sh_cap")));
            Assert.AreEqual(TreeNodeState.Excluded, _tree.StateOf(Idx("aw_alt_sh")), "capstone and its alternative exclude each other");
        }

        // ------------------------------------------------------------------ resolver

        [Test]
        public void Resolver_FullAutoRocket_AndBarrage()
        {
            Begin(weapon: Rl);
            foreach (string id in new[] { "sp_rate1", "sp_rate2", "sp_fullauto" }) _tree.GrantFree(id);
            ref readonly TreeStats s = ref TreeRuntime.Current;
            Assert.IsTrue(s.FullAuto);
            Assert.AreEqual(10f, s.FullAutoRpmScale, 1e-4f, "rl_1 20 → 200 rpm");
            Assert.AreEqual(0.40f, s.FullAutoDamageScale, 1e-4f);
            Assert.AreEqual(12, s.FullAutoMagazine);
            Assert.IsTrue(s.Has(TreeSynergy.Barrage));
            Assert.AreEqual(1f, s.OwnBlastRadiusScale, 1e-4f, "Barrage lifts the ×0,85 radius penalty");

            _tree.OnEquip(Ar);
            Assert.IsFalse(TreeRuntime.Current.Has(TreeSynergy.Barrage), "Barrage needs rl_1 in hand");
            Assert.AreEqual(1.15f, TreeRuntime.Current.FullAutoRpmScale, 1e-4f, "automatic weapons: rpm ×1,15 (B5a)");
            Assert.AreEqual(0, TreeRuntime.Current.FullAutoMagazine);
        }

        [Test]
        public void Resolver_RpmCapMovesPowerToDamage()
        {
            Begin(weapon: Smg);
            foreach (string id in new[] { "sp_rate1", "sp_rate2", "sp_rate3", "sp_twin" }) _tree.GrantFree(id);
            ref readonly TreeStats s = ref TreeRuntime.Current;
            float rpm = 900f * s.FireRateScale * s.FullAutoRpmScale;
            Assert.LessOrEqual(rpm, TreeGuardrails.TriggerRpmMax + 0.01f);
            float raw = 900f * 1.30f * 1.15f;   // B5a: Trigger III ×1,3, Twin ×1,15
            Assert.AreEqual(raw / 60f * 8f, rpm / 60f * 8f * s.DamageScale, 0.01f, "K6: DPS kept");
        }

        [Test]
        public void Resolver_ShotAndSlowWeaponStacks()
        {
            Begin(weapon: Rl);
            foreach (string id in new[] { "sh_heavy", "sh_double", "sh_triple", "sh_fan", "el_ice", "sh_bore", "sh_explosive" }) _tree.GrantFree(id);
            ref readonly TreeStats s = ref TreeRuntime.Current;
            Assert.AreEqual(4, s.ExtraShots, "Dragon Fan: 5 shots");
            Assert.AreEqual(0.35f, s.ExtraShotDamage, 1e-4f, "Dragon Fan B5a");
            Assert.AreEqual(3, s.IceStacksPerHit, "slow weapon: 3 stacks per hit");
            Assert.AreEqual(0f, s.ExplosiveRadius, "rl_1 already explodes");
            Assert.AreEqual(1.3f, s.OwnBlastRadiusScale, 1e-4f);
            Assert.Greater(s.WeaponPower, Rl.BaseDps);
            Assert.AreEqual(1 + 2 + 3 + 4 + 1 + 1 + 3, s.PowerScore);
        }

        // ------------------------------------------------------------------ shop commands

        [Test]
        public void Strip_DiscountAndReroll()
        {
            Begin();
            Give(1000);
            _tree.Open(3);
            Assert.AreEqual(3, _tree.Strip.Count);
            int i = _tree.Strip[0];
            int cost = TreeCatalogV2.Document.nodes[i].cost;
            Assert.AreEqual(cost - UnityEngine.Mathf.Min(UnityEngine.Mathf.RoundToInt(cost * 0.1f), 25), _tree.PriceOf(i), "−10 %, capped at 25 gold (B5a)");
            Assert.IsTrue(_tree.Snapshot.Nodes[i].Recommended);
            int before = _wallet.Balance;
            Assert.IsTrue(_tree.TryReroll());
            Assert.AreEqual(before - 10, _wallet.Balance);
            Assert.AreEqual(3, _tree.Strip.Count);
            Assert.AreEqual(20, _tree.RerollCost, "B5a: +10 per reroll in the round");
            Assert.IsTrue(_tree.TryReroll());
            Assert.AreEqual(before - 30, _wallet.Balance);
            _tree.Open(4);
            Assert.AreEqual(10, _tree.RerollCost, "a new round resets the reroll price");
        }

        [Test]
        public void Target_PathAndRemainingGold()
        {
            Begin();
            _tree.Open(6);
            _tree.SetTarget(Idx("sh_triple"));
            TreeScreenSnapshot snap = _tree.Snapshot;
            Assert.IsTrue(snap.Nodes[Idx("sh_triple")].IsTarget);
            Assert.IsTrue(snap.Nodes[Idx("sh_heavy")].OnTargetPath);
            Assert.IsTrue(snap.Nodes[Idx("sh_double")].OnTargetPath);
            Assert.IsFalse(snap.Nodes[Idx("sh_bore")].OnTargetPath);
            int expected = _tree.PriceOf(Idx("sh_heavy")) + _tree.PriceOf(Idx("sh_double")) + _tree.PriceOf(Idx("sh_triple"));
            Assert.AreEqual(expected, snap.TargetGoldRemaining);
        }

        [Test]
        public void Hearth_PriceStepAndPerRoundCap()
        {
            Begin();
            Give(1000);
            _tree.Open(1);
            Assert.AreEqual(50, _tree.HearthCost);
            Assert.IsTrue(_tree.TryStokeHearth());
            Assert.AreEqual(80, _tree.HearthCost);
            Assert.IsTrue(_tree.TryStokeHearth());
            Assert.IsTrue(_tree.TryStokeHearth());
            Assert.IsFalse(_tree.TryStokeHearth(), "3 per round");
            Assert.AreEqual(1.06f, TreeRuntime.Current.HearthDamageScale, 1e-4f);
            _tree.Open(2);
            Assert.IsTrue(_tree.HearthAvailable);
        }

        [Test]
        public void Interest_PaidAtNextRoundEnd_Capped()
        {
            Begin();
            Give(3000);
            _tree.Open(3);
            Assert.IsTrue(_tree.TryBuy(Idx("ec_gold1")));
            Assert.IsTrue(_tree.TryBuy(Idx("ec_interest")));
            int balance = _wallet.Balance;
            _tree.Open(4);
            Assert.AreEqual(UnityEngine.Mathf.Min(UnityEngine.Mathf.FloorToInt(balance * 0.08f), 150), _tree.InterestPaidThisRound);
        }

        [Test]
        public void StartGold_OnlyHeadStart_ModeGoldIsWp102()
        {
            Begin(TreeMode.Demolition);
            Assert.AreEqual(0, _wallet.Balance, "the mode's 150 is WP-102's DifficultyService, not the tree's");
            Assert.AreEqual(2, TreeEconomyV2.ScaleKillGold(2), "no Coin Sense: unchanged in every mode");

            var aw = TreeAwakening.None;
            aw.HeadStart = true;
            var wallet = new GoldWallet();
            wallet.InitializeForRun(2);
            new RunTreeV2().Begin(2, TreeMode.Normal, wallet, Ar, aw);
            Assert.AreEqual(RunTreeV2.HeadStartGold, wallet.Balance, "Head Start (Normal / Hard)");
            var wallet2 = new GoldWallet();
            wallet2.InitializeForRun(3);
            new RunTreeV2().Begin(3, TreeMode.Demolition, wallet2, Ar, aw);
            Assert.AreEqual(0, wallet2.Balance, "Head Start does not apply in Demolition");
        }

        [Test]
        public void GoldScale_CarryIsDeterministic()
        {
            Begin();
            _tree.GrantFree("ec_gold1");
            int sum = 0;
            for (int k = 0; k < 10; k++) sum += TreeEconomyV2.ScaleKillGold(2);
            Assert.AreEqual(22, sum, "10 × 2 × 1,1 with the fractional carry");
        }

        [Test]
        public void Continue_RaisesAndCloses()
        {
            Begin();
            _tree.Open(1);
            int raised = 0;
            _tree.ContinueRequested += () => raised++;
            _tree.Continue();
            Assert.AreEqual(1, raised);
            Assert.IsFalse(_tree.IsOpen);
            Assert.IsFalse(_tree.TryBuy(Idx("sh_heavy")), "closed");
        }

        [Test]
        public void Synergy_UnlockEventOnPurchase()
        {
            var seen = new List<TreeSynergy>();
            TreeEvents.SynergyUnlocked += s => seen.Add(s);
            Begin();
            Give(5000);
            _tree.Open(3);
            foreach (string id in new[] { "sh_heavy", "sh_bore", "sh_double", "sh_ricochet" }) Assert.IsTrue(_tree.TryBuy(Idx(id)), id);
            CollectionAssert.AreEqual(new[] { TreeSynergy.PinballStorm }, seen);
            Assert.IsTrue(_tree.Snapshot.Synergies.First(v => v.Synergy == TreeSynergy.PinballStorm).Active);
        }

        [Test]
        public void DominantElement_HighestTier_TieGoesToLater()
        {
            Begin();
            Assert.AreEqual(TreeElement.None, TreeRuntime.Current.DominantElement);
            _tree.GrantFree("el_fire");
            Assert.AreEqual(TreeElement.Fire, TreeRuntime.Current.DominantElement);
            _tree.GrantFree("el_ice");
            Assert.AreEqual(TreeElement.Ice, TreeRuntime.Current.DominantElement, "tie at tier I: the later purchase");
            _tree.GrantFree("el_fire2");
            Assert.AreEqual(TreeElement.Fire, TreeRuntime.Current.DominantElement, "fire reaches tier II");
            _tree.GrantFree("el_ice2");
            Assert.AreEqual(TreeElement.Ice, TreeRuntime.Current.DominantElement);
        }

        [Test]
        public void Snapshot_DisplayArgs_IconsAndClosedBy()
        {
            Begin();
            Give(5000);
            _tree.Open(3);
            TreeNodeView heavy = _tree.Snapshot.Nodes[Idx("sh_heavy")];
            Assert.AreEqual("Tree.sh_heavy", heavy.IconKey);
            Assert.AreEqual("atis_I_heavy_rounds.png", heavy.IconFile);
            int dmg = System.Array.IndexOf(heavy.ArgNames, "dmg");
            Assert.AreEqual(15f, heavy.ArgValues[dmg], 1e-3f, "bonus: (1,15 − 1) × 100");
            TreeNodeView reload = _tree.Snapshot.Nodes[Idx("mg_reload1")];
            Assert.AreEqual(20f, reload.ArgValues[System.Array.IndexOf(reload.ArgNames, "reload")], 1e-3f, "cut: (1 − 0,80) × 100");
            TreeNodeView dbl = _tree.Snapshot.Nodes[Idx("sh_double")];
            Assert.AreEqual(50f, dbl.ArgValues[System.Array.IndexOf(dbl.ArgNames, "extraDmg")], 1e-3f, "pct (B5a 0,5)");
            TreeSynergyView barrage = _tree.Snapshot.Synergies.First(v => v.Synergy == TreeSynergy.Barrage);
            Assert.AreEqual("Tree.Synergy.Barrage", barrage.BadgeKey);
            Assert.AreEqual("sinerji_barrage.png", barrage.BadgeFile);
            Assert.AreEqual(4f, barrage.ArgValues[System.Array.IndexOf(barrage.ArgNames, "every")], 1e-3f);
            Assert.AreEqual(3, _tree.Snapshot.HearthPerRound);
            Assert.AreEqual(20, _tree.Snapshot.HearthMax);

            _tree.TryBuy(Idx("el_fire"));
            _tree.TryBuy(Idx("el_ice"));
            _tree.Refresh();
            TreeNodeView shock = _tree.Snapshot.Nodes[Idx("el_shock")];
            Assert.AreEqual(TreeNodeState.Excluded, shock.State);
            Assert.AreEqual(Idx("el_ice"), shock.ClosedByIndex, "closed by the last element line bought");
            Assert.AreEqual(-1, heavy.ClosedByIndex);
        }

        [Test]
        public void StartRound_GoldBankedLessPresetCost()
        {
            Assert.AreEqual(0, TreePresetsV2.StartGold(1, 0), "R1: nothing banked");
            Assert.AreEqual(471, TreePresetsV2.StartGold(6, 0), "R1–R5");
            Assert.AreEqual(6506, TreePresetsV2.StartGold(16, 0), "R1–R15");
            Assert.IsTrue(TreePresetsV2.TryGet("barrage_r15", out TreePresetsV2.Preset barrage));
            int cost = TreePresetsV2.CostOf(barrage);
            Assert.Greater(cost, 0);
            Assert.AreEqual(Math.Max(0, 6506 - cost), TreePresetsV2.StartGold(16, cost), "the preset's free nodes come out of the bank");
            TreePresetsV2.TestStartRound = 12;
            try { Assert.AreEqual(12, TreePresetsV2.RunStartRound); }
            finally { TreePresetsV2.TestStartRound = null; }
            Assert.AreEqual(1, TreePresetsV2.RunStartRound, "no flag: round 1");
        }

        [Test]
        public void SpecialResonance_StormChainAndDeepFreeze()
        {
            Begin();
            Assert.AreEqual(1f, TreeHooks.RaijuDurationScale);
            Assert.AreEqual(0, TreeHooks.RaijuExtraJumps);
            Assert.IsFalse(TreeHooks.NamekujiFreezes);
            _tree.GrantFree("el_shock");
            Assert.AreEqual(1f, TreeHooks.RaijuDurationScale, "Arc Rounds alone: no resonance");
            _tree.GrantFree("el_shock2");
            Assert.AreEqual(1.3f, TreeHooks.RaijuDurationScale, 1e-4f, "Storm Chain: Raiju ×1,3");
            Assert.AreEqual(2, TreeHooks.RaijuExtraJumps, "Storm Chain: +2 jumps");
            _tree.GrantFree("el_ice");
            Assert.IsFalse(TreeHooks.NamekujiFreezes);
            _tree.GrantFree("el_ice2");
            Assert.IsTrue(TreeHooks.NamekujiFreezes, "Deep Freeze: the gel freezes");
        }

        [Test]
        public void Presets_ResolveToCatalogNodes()
        {
            foreach (TreePresetsV2.Preset p in TreePresetsV2.All)
            {
                foreach (string id in p.Nodes) Assert.GreaterOrEqual(Idx(id), 0, p.Name + ": " + id);
            }

            Begin(weapon: Rl);
            Assert.IsTrue(TreePresetsV2.TryGet("barrage_r15", out TreePresetsV2.Preset barrage));
            Assert.AreEqual(barrage.Nodes.Length, TreePresetsV2.Apply(_tree, barrage));
            Assert.IsTrue(TreeRuntime.Current.Has(TreeSynergy.Barrage));
        }
    }
}
