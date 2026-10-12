using System.Collections.Generic;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B4 (S-09) · the feature-based tree screen's pure half: the snapshot → cell placement (Branch row, Tier column,
    /// Slot in the cell), the fog line, the recommendation strip's price, the Target path, the price / percent formats, the pad
    /// grid (every node reachable, no dead end - AD A16), the branch jump, the tree art's pigments (Speed is not gold, AD §1.6),
    /// the ink-on-paper contrast, the route (no provider = the shipped tree) and the Awakening page's model. The data is the
    /// capture's FAKE provider (UiTreeCaptureShop: the B1 catalog's ids / tiers / slots), never the player's profile.
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class TreeScreenV2Tests
    {
        private string _language;

        [SetUp]
        public void SetUp()
        {
            _language = UiStrings.Language;
            UiStrings.Language = UiStrings.English;
            UiTreeScreenRoute.Override(null);
            UiTreeScreenRoute.Provider = null;
        }

        [TearDown]
        public void TearDown()
        {
            UiStrings.Language = _language;
            UiTreeScreenRoute.Override(null);
            UiTreeScreenRoute.Provider = null;
        }

        private static TreeV2Layout Lay(UiTreeCaptureShop shop, float w, float h, float label = 128f)
        {
            var layout = new TreeV2Layout();
            layout.Compute(new Vector2(w, h), shop.Snapshot, label);
            return layout;
        }

        // ------------------------------------------------------------------ layout

        [Test]
        public void Satirlar_ADSirasi_AtisHizSarjorElementKaosHayattaKalmaEkonomi()
        {
            TreeBranch[] expected = { TreeBranch.Shot, TreeBranch.Speed, TreeBranch.Magazine, TreeBranch.Element, TreeBranch.Chaos, TreeBranch.Survival, TreeBranch.Economy };
            for (int r = 0; r < expected.Length; r++)
            {
                Assert.AreEqual(expected[r], TreeV2Rows.BranchAt(r), "row " + r);
                Assert.AreEqual(r, TreeV2Rows.RowOf(expected[r]));
            }
        }

        [Test]
        public void Yerlesim_1080p_AP042Olculeri()
        {
            var shop = new UiTreeCaptureShop();
            TreeV2Layout l = Lay(shop, 1920f, 1080f);
            Assert.IsTrue(l.Fits);
            Assert.AreEqual(1f, l.Scale, 1e-4f, "1080p draws at the reference size");
            Assert.AreEqual(224f, l.TierX(1), 0.5f, "tier I starts at 224 (AP-042 tier_columns)");
            Assert.AreEqual(208f, l.TierWidth, 0.5f);
            Assert.AreEqual(1280f, l.SynergyX, 0.5f, "the synergy column at 1280");
            Assert.AreEqual(400f, l.DetailW, 0.5f, "the 400 px detail panel");
            Assert.AreEqual(96f, l.TreeTop, 0.5f, "header 96");
            Assert.AreEqual(10, l.TotalUnits, "three double rows (Shot, Element, Survival) + four single");
            int[] units = { 2, 1, 1, 2, 1, 2, 1 };
            for (int r = 0; r < units.Length; r++)
            {
                Assert.AreEqual(units[r], l.RowUnits(r), "row " + r + " units");
            }

            Assert.LessOrEqual(l.TreeBottom, 1080f - TreeV2Layout.Margin + 0.5f, "960 px of rows end above the margin");
            Assert.AreEqual(64f, l.IconSize, 0.01f, "64 px icons (AP-042's floor)");
        }

        [Test]
        public void Yerlesim_DalSatiri_KademeSutunu_HucreIciSlotOkumaSirasinda()
        {
            var shop = new UiTreeCaptureShop();
            TreeV2Layout l = Lay(shop, 1920f, 1080f);
            TreeScreenSnapshot s = shop.Snapshot;
            for (int i = 0; i < s.Nodes.Length; i++)
            {
                Vector2 c = l.Centre(i);
                int tier = s.Nodes[i].Tier;
                Assert.AreEqual(TreeV2Rows.RowOf(s.Nodes[i].Branch), l.RowOfNode(i), s.Nodes[i].NodeId + " row = its branch");
                Assert.GreaterOrEqual(c.x, l.TierX(tier), s.Nodes[i].NodeId + " in its tier column");
                Assert.Less(c.x, l.TierX(tier) + l.TierWidth, s.Nodes[i].NodeId + " in its tier column");
                Assert.GreaterOrEqual(c.y, l.RowY(l.RowOfNode(i)), s.Nodes[i].NodeId + " in its row");
                Assert.Less(c.y, l.RowY(l.RowOfNode(i)) + l.RowHeight(l.RowOfNode(i)), s.Nodes[i].NodeId + " in its row");
            }

            // The Element row's four lines: fire / ice over shock / venom (slots 0 1 / 2 3, AP-042's emaki).
            Vector2 fire = l.Centre(shop.IndexOf("el_fire")), ice = l.Centre(shop.IndexOf("el_ice"));
            Vector2 shock = l.Centre(shop.IndexOf("el_shock")), venom = l.Centre(shop.IndexOf("el_venom"));
            Assert.Less(fire.x, ice.x);
            Assert.AreEqual(fire.y, ice.y, 0.5f);
            Assert.AreEqual(fire.x, shock.x, 0.5f);
            Assert.Greater(shock.y, fire.y);
            Assert.AreEqual(shock.y, venom.y, 0.5f);
            // A single node centres in its cell; three take two over one.
            Vector2 fan = l.Centre(shop.IndexOf("sh_fan"));
            Assert.AreEqual(l.TierX(4) + l.TierWidth * 0.5f, fan.x, 0.5f, "a lone node is centred");
            Vector2 d = l.Centre(shop.IndexOf("sh_double")), p = l.Centre(shop.IndexOf("sh_pierce")), r2 = l.Centre(shop.IndexOf("sh_ricochet"));
            Assert.AreEqual(d.y, p.y, 0.5f);
            Assert.AreEqual(d.x, r2.x, 0.5f);
            Assert.Greater(r2.y, d.y);
        }

        [TestCase(1920f, 1080f, "1080p")]
        [TestCase(1920f, 1247f, "Mac 16:10")]
        [TestCase(1600f, 1039f, "Mac UI 1.2")]
        [TestCase(1600f, 900f, "1080p UI 1.2")]
        [TestCase(1920f, 1440f, "4:3")]
        [TestCase(2520f, 1080f, "21:9")]
        public void Yerlesim_HerCanvasta_SigarCakismaz(float w, float h, string when)
        {
            var shop = new UiTreeCaptureShop();
            TreeV2Layout l = Lay(shop, w, h, 128f);
            Assert.IsTrue(l.Fits, when + ": fits (scale " + l.Scale + ")");
            Assert.LessOrEqual(l.BlockWidth, TreeV2Layout.BlockMaxWidth + 0.01f, when + ": the block is at most 1920 wide (21:9 centres it)");
            Assert.LessOrEqual(l.SynergyX + l.SynergyWidth, l.DetailX + 0.5f, when + ": the synergy column ends before the detail panel");
            Assert.LessOrEqual(l.DetailX + l.DetailW, l.BlockWidth - TreeV2Layout.Margin + 0.5f, when + ": the detail panel inside the margin");
            Assert.LessOrEqual(l.TreeBottom, h - TreeV2Layout.Margin + 0.5f, when + ": the rows end above the margin");
            int n = shop.Snapshot.Nodes.Length;
            for (int i = 0; i < n; i++)
            {
                Rect a = l.HitRect(i);
                Assert.GreaterOrEqual(a.xMin, l.LabelColumn - 0.5f, when + ": " + shop.Snapshot.Nodes[i].NodeId + " right of the labels");
                Assert.LessOrEqual(a.xMax, l.SynergyX + 0.5f, when + ": " + shop.Snapshot.Nodes[i].NodeId + " left of the synergies");
                Assert.GreaterOrEqual(a.yMin, l.TreeTop - 0.5f, when + ": " + shop.Snapshot.Nodes[i].NodeId + " under the header");
                for (int j = i + 1; j < n; j++)
                {
                    Rect b = l.HitRect(j);
                    bool overlap = a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;
                    Assert.IsFalse(overlap, when + ": hit boxes of " + shop.Snapshot.Nodes[i].NodeId + " and " + shop.Snapshot.Nodes[j].NodeId + " overlap");
                }
            }

            Assert.GreaterOrEqual(l.HitSize, 48f, when + ": every node keeps a 48 px target");
        }

        [Test]
        public void Sis_AcikKademeninSagindanBaslar_HepsiAcikkenYok()
        {
            var shop = new UiTreeCaptureShop(1200, 6);
            TreeV2Layout l = Lay(shop, 1920f, 1080f);
            Assert.AreEqual(3, shop.Snapshot.OpenTier, "round 6: I–III open");
            Assert.AreEqual(10, shop.Snapshot.NextTierRound, "IV opens at R10 (Normal)");
            Assert.AreEqual(l.TierX(4), l.FogX(shop.Snapshot.OpenTier), 0.5f, "the fog starts at tier IV's column");
            Assert.AreEqual(l.FogRight, l.FogX(5), 0.5f, "every tier open: no fog");
            Assert.AreEqual(TreeNodeState.Fogged, shop.Snapshot.Nodes[shop.IndexOf("sh_fan")].State);
            StringAssert.Contains("10", TreeV2Text.Fog(shop.Snapshot.NextTierRound));
            StringAssert.DoesNotContain("{", TreeV2Text.Fog(10));
        }

        // ------------------------------------------------------------------ strip, target, price formats

        [Test]
        public void OneriSeridi_UcDugum_YuzdeOnIndirim_Yeniden_On_Altin()
        {
            var shop = new UiTreeCaptureShop(1200, 6);
            int strip = 0;
            foreach (TreeNodeView n in shop.Snapshot.Nodes)
            {
                if (!n.Recommended)
                {
                    continue;
                }

                strip++;
                Assert.AreEqual(Mathf.RoundToInt(n.Cost * 0.9f), n.Price, n.NodeId + ": −10 % on the strip");
                // The node's own word is its real cut after the rounding (45 → 40 reads −11 %); the strip's caption carries
                // the common 10 % (OneriSeridi_AltinTavani_*).
                int real = Mathf.RoundToInt((1f - n.Price / (float)n.Cost) * 100f);
                Assert.AreEqual(UiStrings.Percent("−" + UiStrings.Number(real)), TreeV2Text.Discount(n), n.NodeId);
                Assert.That(real, Is.InRange(9, 11), n.NodeId + ": the 10 % rule, within the rounding");
            }

            Assert.AreEqual(3, strip, "three recommended nodes");
            int gold = shop.Gold;
            Assert.IsTrue(shop.TryReroll());
            Assert.AreEqual(gold - 10, shop.Gold, "reroll 10 gold");
        }

        [Test]
        public void OneriSeridi_AltinTavani_BaslikYalnizOrtakYuzdeyiSoyler()
        {
            // S-04 B5a caps the strip discount at 25 gold: 120 → 108 (−10 %), 400 → 375 (−6 %). The caption must not say 10 %.
            var s = new TreeScreenSnapshot
            {
                Nodes = new[]
                {
                    new TreeNodeView { NodeId = "a", Recommended = true, Cost = 120, Price = 108, State = TreeNodeState.Available },
                    new TreeNodeView { NodeId = "b", Recommended = true, Cost = 400, Price = 375, State = TreeNodeState.Unaffordable },
                    new TreeNodeView { NodeId = "c", Recommended = false, Cost = 300, Price = 300, State = TreeNodeState.Available },
                },
            };
            Assert.AreEqual(0, UiTreeScreenV2.StripPercent(s), "different percents: the caption reads RECOMMENDED alone");
            Assert.AreEqual("−10%", TreeV2Text.Discount(s.Nodes[0]));
            Assert.AreEqual("−6%", TreeV2Text.Discount(s.Nodes[1]), "each chip shows its own true discount");

            s.Nodes[1].Cost = 200;
            s.Nodes[1].Price = 180;
            Assert.AreEqual(10, UiTreeScreenV2.StripPercent(s), "one shared percent: the caption says it");
            s.Nodes[1].State = TreeNodeState.Purchased;
            s.Nodes[1].Price = 150;
            Assert.AreEqual(10, UiTreeScreenV2.StripPercent(s), "a bought strip node does not count");
        }

        [Test]
        public void Hedef_YoluIsaretlenir_KalanAltinYolunToplami()
        {
            var shop = new UiTreeCaptureShop(100, 14);
            int fan = shop.IndexOf("sh_fan");
            shop.SetTarget(fan);
            TreeScreenSnapshot s = shop.Snapshot;
            Assert.IsTrue(s.Nodes[fan].IsTarget);
            string[] path = { "sh_heavy", "sh_double", "sh_triple", "sh_fan" };
            int sum = 0;
            foreach (string id in path)
            {
                TreeNodeView n = s.Nodes[shop.IndexOf(id)];
                Assert.IsTrue(n.OnTargetPath, id + " on the Target's path");
                sum += n.Price;
            }

            Assert.AreEqual(sum, s.TargetGoldRemaining, "the remaining-gold bar is the path's price");
            Assert.IsFalse(s.Nodes[shop.IndexOf("sh_bore")].OnTargetPath, "a node off the path is not marked");
            StringAssert.Contains(UiStrings.Number(sum - s.Gold), TreeV2Text.TargetLine(s));
            shop.SetTarget(-1);
            Assert.AreEqual(-1, shop.Snapshot.TargetIndex);
            Assert.AreEqual(string.Empty, TreeV2Text.TargetLine(shop.Snapshot));
        }

        [Test]
        public void Bicim_Fiyat_Yuzde_Ondalik_DileGore()
        {
            var node = new TreeNodeView { Cost = 500, Price = 450 };
            UiStrings.Language = UiStrings.English;
            Assert.AreEqual("−10%", TreeV2Text.Discount(node));
            Assert.AreEqual("+15% / 1.5 m", TreeV2Text.FillArgs("+{dmg}% / {r} m", new[] { "dmg", "r" }, new[] { 15f, 1.5f }));
            StringAssert.Contains("450", TreeV2Text.Gold(450));
            UiStrings.Language = UiStrings.Turkish;
            Assert.AreEqual("−%10", TreeV2Text.Discount(node), "TR: the sign stays in front, % before the number");
            Assert.AreEqual("+%15 / 1,5 m", TreeV2Text.FillArgs("+%{dmg} / {r} m", new[] { "dmg", "r" }, new[] { 15f, 1.5f }), "TR: decimal comma");
            Assert.AreEqual(string.Empty, TreeV2Text.Discount(new TreeNodeView { Cost = 500, Price = 500 }), "no discount, no chip");
        }

        [Test]
        public void Metin_EksikAnahtar_KimlikDegilTire_EksikArgumanTire()
        {
            var node = new TreeNodeView { NodeId = "zz_secret_node", StringKey = "tree.v2.zz_secret_node" };
            Assert.AreEqual(TreeV2Text.Missing, TreeV2Text.Name(node), "a node the pack does not name prints an em dash");
            StringAssert.DoesNotContain("zz_secret_node", TreeV2Text.Name(node));
            Assert.AreEqual("+—%", TreeV2Text.FillArgs("+{dmg}%", null, null), "an unfilled placeholder never shows its brace");
        }

        [Test]
        public void Durum_KelimeyleSoylenir_KapiRoundu_EksikAltin()
        {
            var shop = new UiTreeCaptureShop(30, 6);
            TreeScreenSnapshot s = shop.Snapshot;
            StringAssert.Contains("10", TreeV2Text.State(s, shop.IndexOf("sh_fan")), "Fogged: the round its tier opens");
            int heavy = shop.IndexOf("sh_heavy");
            Assert.AreEqual(TreeNodeState.Unaffordable, s.Nodes[heavy].State);
            StringAssert.Contains(UiStrings.Number(s.Nodes[heavy].Shortfall), TreeV2Text.State(s, heavy), "the gold short");
            foreach (TreeNodeView n in s.Nodes)
            {
                Assert.IsFalse(string.IsNullOrEmpty(TreeV2Text.State(s, System.Array.IndexOf(s.Nodes, n))), n.NodeId + ": a state word");
            }
        }

        // ------------------------------------------------------------------ fake provider commands

        [Test]
        public void SahteSaglayici_TryBuy_YalnizAlinabilir_AltinDuser_DurumAlindi()
        {
            var shop = new UiTreeCaptureShop(1200, 6);
            int heavy = shop.IndexOf("sh_heavy");
            int fan = shop.IndexOf("sh_fan");
            int price = shop.Snapshot.Nodes[heavy].Price;
            Assert.IsFalse(shop.TryBuy(fan), "a fogged node is not sold");
            Assert.IsTrue(shop.TryBuy(heavy));
            Assert.AreEqual(1200 - price, shop.Snapshot.Gold);
            Assert.AreEqual(TreeNodeState.Purchased, shop.Snapshot.Nodes[heavy].State);
            Assert.AreEqual(TreeNodeState.Available, shop.Snapshot.Nodes[shop.IndexOf("sh_double")].State, "its child opens");
            Assert.IsFalse(shop.TryBuy(heavy), "owned: not sold twice");
            Assert.AreEqual(3, shop.BuyCalls);
        }

        // ------------------------------------------------------------------ pad grid

        [Test]
        public void Gezinme_HerHedefErisilir_OluUcYok_SatirIciSagSol()
        {
            var shop = new UiTreeCaptureShop(1200, 6);
            TreeV2Layout l = Lay(shop, 1920f, 1080f);
            var nav = new TreeV2Nav();
            nav.Build(l);
            Assert.AreEqual(shop.Snapshot.Nodes.Length + shop.Snapshot.Synergies.Length, nav.Count);
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int next = nav.Next(at, d);
                    if (next >= 0 && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            Assert.AreEqual(nav.Count, seen.Count, "A16: every node and badge is reached from the first node");
            for (int t = 0; t < nav.Count; t++)
            {
                bool any = false;
                for (int d = 0; d < 4; d++)
                {
                    any |= nav.Next(t, d) >= 0;
                }

                Assert.IsTrue(any, "dead end at " + t);
                int right = nav.Next(t, TreeV2Nav.Right);
                if (t < nav.NodeCount && right >= 0 && right < nav.NodeCount)
                {
                    Assert.AreEqual(l.RowOfNode(t), l.RowOfNode(right), "right walks the tiers along the branch");
                    Assert.Greater(l.Centre(right).x, l.Centre(t).x);
                }
            }

            // Up from the second row's node reaches the first row; the last tier's right reaches the synergy column.
            int sp = shop.IndexOf("sp_rate1");
            Assert.AreEqual(0, l.RowOfNode(nav.Next(sp, TreeV2Nav.Up)), "up = the branch above");
            int cap = shop.IndexOf("sh_cap");
            Assert.IsTrue(nav.IsSynergy(nav.Next(cap, TreeV2Nav.Right)) || nav.Next(cap, TreeV2Nav.Right) > cap, "past tier V: the synergy column");
        }

        [Test]
        public void DalAtlama_SonrakiDal_HatirlananDugum_Sarar()
        {
            var shop = new UiTreeCaptureShop(1200, 6);
            TreeV2Layout l = Lay(shop, 1920f, 1080f);
            var remembered = new[] { -1, -1, -1, -1, -1, -1, -1 };
            int next = TreeV2Nav.BranchJump(l, shop.Snapshot, 0, 1, remembered);
            Assert.AreEqual(1, l.RowOfNode(next), "RB: the Speed row");
            Assert.AreEqual(TreeNodeState.Available, shop.Snapshot.Nodes[next].State, "its first buyable node");
            remembered[1] = shop.IndexOf("sp_move");
            Assert.AreEqual(shop.IndexOf("sp_move"), TreeV2Nav.BranchJump(l, shop.Snapshot, 0, 1, remembered), "the row remembers its node");
            Assert.AreEqual(TreeV2Rows.Count - 1, l.RowOfNode(TreeV2Nav.BranchJump(l, shop.Snapshot, 0, -1, remembered)), "LB from the top wraps to Economy");
        }

        // ------------------------------------------------------------------ art, route

        [Test]
        public void Pigment_HizAltinDegil_EkonomiAltin_ElementlerAyrisir()
        {
            Assert.IsFalse(TreeArt.IsGoldReserve(TreeArt.Pigment(TreeBranch.Speed)), "AD §1.6: Speed is hagane silver, not gold");
            Assert.IsFalse(TreeArt.IsGoldReserve(TreeArt.Pigment(TreeBranch.Element, TreeElement.Shock)), "electricity is Raiju blue");
            Assert.IsTrue(TreeArt.IsGoldReserve(TreeArt.Pigment(TreeBranch.Economy)), "gold is Economy's alone");
            foreach (TreeBranch b in System.Enum.GetValues(typeof(TreeBranch)))
            {
                if (b != TreeBranch.Economy)
                {
                    Assert.IsFalse(TreeArt.IsGoldReserve(TreeArt.Pigment(b)), b + " pigment in the gold reserve");
                }
            }

            Assert.AreNotEqual(TreeArt.Pigment(TreeBranch.Shot), TreeArt.Pigment(TreeBranch.Element, TreeElement.Fire), "fire is not the Shot vermilion");
        }

        [Test]
        public void Murekkep_KagitPlakadaOkunur_EnKoyuParsomenUstunde()
        {
            // The paper plate (α 0.9) over the parchment's darkest corner, linear light (the project's blending).
            Color paper = UiTokens.Get(TreeArt.Paper).linear;
            float plate = TreeArt.PaperPlateAlpha;
            float ground = (0.2126f * paper.r + 0.7152f * paper.g + 0.0722f * paper.b) * plate + TreeArt.ParchmentDarkestLinear * (1f - plate);
            foreach (UiColor ink in new[] { TreeArt.InkText, TreeArt.InkSecondary })
            {
                Color c = UiTokens.Get(ink).linear;
                float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                float ratio = (ground + 0.05f) / (l + 0.05f);
                Assert.GreaterOrEqual(ratio, 4.5f, ink + " on the paper plate: " + ratio.ToString("0.00") + ":1");
            }
        }

        [Test]
        public void Rota_SaglayiciYoksaEskiAgac_OverrideKazanir()
        {
            Assert.IsNull(UiTreeScreenRoute.Resolve(), "no provider (B1 absent): the round end opens the shipped tree");
            var run = new UiTreeCaptureShop();
            UiTreeScreenRoute.Provider = () => run;
            Assert.AreSame(run, UiTreeScreenRoute.Resolve(), "the run's provider");
            var fake = new UiTreeCaptureShop();
            UiTreeScreenRoute.Override(fake);
            Assert.AreSame(fake, UiTreeScreenRoute.Resolve(), "a capture / test override wins");
            UiTreeScreenRoute.Override(null);
            Assert.AreSame(run, UiTreeScreenRoute.Resolve());
        }

        [Test]
        public void TemaAnahtari_TreeOnEki_YalnizAgacSanati()
        {
            Assert.IsNull(TreeArt.Icon("Hud.Gold"), "TreeArt answers only Tree.* keys");
            Assert.AreEqual("sh_heavy", TreeArt.NodeIdOf("Tree.sh_heavy", "sh_heavy"));
            Assert.AreEqual("sh_heavy", TreeArt.NodeIdOf("atis_I_heavy_rounds.png", "sh_heavy"), "B1's file-name key falls back to the node id");
            Assert.AreEqual("sh_heavy", TreeArt.NodeIdOf(null, "sh_heavy"));
        }

        // ------------------------------------------------------------------ Awakening model

        private sealed class FakeAwakening : IAwakeningSource
        {
            public bool CanUnlock => true;
            public int Unlocks;

            public void Read(AwakeningPageModel into)
            {
                into.ResetToLayout();
                into.AshBalance = 320;
                into.Pending = false;
                into.CanUnlock = true;
                for (int i = 0; i < into.Entries.Count; i++)
                {
                    AwakeningEntry e = into.Entries[i];
                    e.Cost = 300;
                    e.StringKey = "tree.meta.awakening.zz_" + i;
                    e.State = i == 0 ? AwakeningEntryState.Unlocked : AwakeningEntryState.Available;
                    into.Entries[i] = e;
                }
            }

            public bool TryUnlock(string id, out string refusal)
            {
                Unlocks++;
                refusal = string.Empty;
                return true;
            }
        }

        [Test]
        public void Uyanis_OnIkiDugum_IzgaradaTekYer_AdYoksaTire()
        {
            var model = new AwakeningPageModel();
            model.ResetToLayout();
            Assert.AreEqual(11, model.Entries.Count, "seven alternative capstones, two forks, two aids (WP-112: aw_rethink retired)");
            Assert.IsTrue(model.Pending, "before B1: the page reads pending");
            var places = new HashSet<int>();
            foreach (AwakeningEntry e in model.Entries)
            {
                Assert.Less(e.Row, AwakeningPageModel.Rows);
                Assert.Less(e.Column, AwakeningPageModel.Columns);
                Assert.IsTrue(places.Add(e.Row * 10 + e.Column), e.Id + " shares a grid place");
                Assert.AreEqual(TreeV2Text.Missing, AwakeningPageModel.Name(e), e.Id + ": no words → an em dash, never the id");
                Assert.AreEqual(TreeV2Text.Missing, AwakeningPageModel.StateLine(e, 0));
            }

            int alternatives = 0;
            foreach (string id in AwakeningPageModel.LayoutIds)
            {
                if (AwakeningPageModel.AlternativeOf(id) != null)
                {
                    alternatives++;
                }
            }

            Assert.AreEqual(7, alternatives);
            Assert.AreEqual("ec_midas", AwakeningPageModel.AlternativeOf("aw_alt_ec"), "Golden Rain stands in for Economy IV");
        }

        [Test]
        public void Uyanis_Kaynak_DurumSatiri_KulEksik_Acildi()
        {
            var model = new AwakeningPageModel();
            var source = new FakeAwakening();
            source.Read(model);
            Assert.IsFalse(model.Pending);
            StringAssert.Contains("300", AwakeningPageModel.StateLine(model.Entries[1], model.AshBalance), "the price in Ash");
            AwakeningEntry shortOne = model.Entries[2];
            shortOne.State = AwakeningEntryState.Short;
            shortOne.Cost = 400;
            StringAssert.Contains("80", AwakeningPageModel.StateLine(shortOne, model.AshBalance), "400 − 320 Ash short");
            Assert.IsTrue(source.TryUnlock(model.Entries[1].Id, out _));
            Assert.IsFalse(new ProfileAwakeningSource(false).CanUnlock, "read-only until S-04 confirms the command");
            Assert.IsFalse(new ProfileAwakeningSource(false).TryUnlock("aw_third", out string refusal));
            Assert.IsFalse(string.IsNullOrEmpty(refusal), "a refusal says why");
        }
        // ------------------------------------------------------------------ after S-04 B5a

        [Test]
        public void B5a_KapatanDugum_IndekstenOkunur_ElementCatali()
        {
            var shop = new UiTreeCaptureShop(1200, 6);
            shop.Grant("el_fire");
            shop.Grant("el_ice");
            TreeScreenSnapshot s = shop.Snapshot;
            int shock = shop.IndexOf("el_shock");
            Assert.AreEqual(TreeNodeState.Excluded, s.Nodes[shock].State, "F1: a third element closes");
            Assert.AreEqual(shop.IndexOf("el_ice"), s.Nodes[shock].ClosedByIndex, "B5a: the last element line taken closes it");
            Assert.AreEqual(TreeV2Text.Name(s.Nodes[shop.IndexOf("el_ice")]), TreeV2Text.ClosedByName(s, shock));
            Assert.AreEqual(-1, s.Nodes[shop.IndexOf("sh_heavy")].ClosedByIndex, "not excluded: −1");
        }

        [Test]
        public void B5a_Ocak_FiyatRoundTavaniKosuTavani()
        {
            var shop = new UiTreeCaptureShop(5000, 6);
            Assert.AreEqual(3, shop.Snapshot.HearthPerRound);
            Assert.AreEqual(20, shop.Snapshot.HearthMax);
            StringAssert.Contains("50", TreeV2Text.HearthState(shop.Snapshot), "stokable: its price");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(shop.TryStokeHearth());
            }

            Assert.IsFalse(shop.Snapshot.HearthAvailable);
            StringAssert.Contains("3/3", TreeV2Text.HearthState(shop.Snapshot), "the round's cap: THIS ROUND 3/3");
        }

        // ------------------------------------------------------------------ fix2: the Awakening tile line

        [Test]
        public void Uyanis_KaroSatiri_FiyatVeGlif_EksikCumlesiAyrintida()
        {
            var e = new AwakeningEntry { Id = "aw_alt_sh", Cost = 300, State = AwakeningEntryState.Short };
            Assert.AreEqual(TreeV2Text.Ash(300), AwakeningPageModel.TileLine(e), "Ash short: the tile shows the price");
            Assert.AreEqual(AwakeningPageModel.LockGlyph, AwakeningPageModel.TileGlyph(e), "… with the lock");
            StringAssert.Contains("300", AwakeningPageModel.StateLine(e, 0), "the detail keeps the full sentence");
            Assert.AreNotEqual(AwakeningPageModel.TileLine(e), AwakeningPageModel.StateLine(e, 0), "the shortfall sentence is not the tile's");

            e.State = AwakeningEntryState.Available;
            Assert.AreEqual(TreeV2Text.Ash(300), AwakeningPageModel.TileLine(e));
            Assert.IsNull(AwakeningPageModel.TileGlyph(e), "buyable now: no glyph");

            e.State = AwakeningEntryState.Locked;
            Assert.AreEqual(AwakeningPageModel.LockGlyph, AwakeningPageModel.TileGlyph(e));

            e.State = AwakeningEntryState.Unlocked;
            Assert.AreEqual(AwakeningPageModel.UnlockedGlyph, AwakeningPageModel.TileGlyph(e));
            Assert.AreEqual(UiStrings.Get("awakening.state.unlocked", "UNLOCKED"), AwakeningPageModel.TileLine(e));

            e.State = AwakeningEntryState.Pending;
            Assert.AreEqual(TreeV2Text.Missing, AwakeningPageModel.TileLine(e));
            Assert.IsNull(AwakeningPageModel.TileGlyph(e));
        }
    }
}
