using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-104 B4 (S-09) · the feature-based tree screen with the FAKE provider (UiTreeCaptureShop; S-04 B1's RunTreeV2 is the
    /// game's): the open raises TreeOpened once; the pad walks the whole grid through explicit links, LB / RB jump branches and
    /// each branch remembers its node, Y goes to the synergies and back; A on a node is TryBuy and the focus stays; TARGET is
    /// SetTarget; X is Continue; REROLL raises TreeRerolled once and never the old UpgradeRerolled; at TextScale 1.25 (EN / TR,
    /// 1080p and the Mac at UI 1.2) the detail card cuts no word and the branch labels hold. One real-run test: with no
    /// provider the round end is the shipped tree (B1 off: the game is unchanged), with one it is this screen, and its
    /// CONTINUE ends the round-end phase; with S-04 B1's RunTreeV2 on (main 17f29c5d) the round end shows the live tree, a live
    /// node (Coin Sense) is bought through it and CONTINUE closes both the tree and the phase. The one-time Ash refund notice
    /// shows on the main menu once, and reading it acknowledges it.
    /// </summary>
    public sealed class TreeScreenV2PlayTests
    {
        private readonly List<UiEvent> _events = new List<UiEvent>();
        private readonly List<string> _errors = new List<string>();
        private GameObject _canvas;
        private UiTreeScreenV2 _panel;
        private UiTreeCaptureShop _shop;
        private int _continued;

        private const string LongEn = "Every 7 s, a hit opens a 3 m black hole for 2.5 s. It drags enemies in for 60% of your weapon's DPS per second and 150% when it collapses.";
        private const string LongTr = "7 saniyede bir isabet, 2,5 sn süren 3 m kara delik açar. Düşmanları içine çeker, saniyede silah DPS'inin %60 kadarı, çökünce %150 vurur.";

        [SetUp]
        public void SetUp()
        {
            _events.Clear();
            _errors.Clear();
            _continued = 0;
            UiEvents.Raised += OnRaised;
            Application.logMessageReceived += Collect;
            UiTreeScreenRoute.Override(null);
        }

        [TearDown]
        public void TearDown()
        {
            UiEvents.Raised -= OnRaised;
            Application.logMessageReceived -= Collect;
            UiTreeScreenRoute.Override(null);
            RoundUpgradeShop.TreeV2TestOverride = null;
            TreeLiveV2.TreatAllAsLive = false;
            UiAshRefundNotice.PendingSource = null;
            UiAshRefundNotice.AcknowledgeAction = null;
            UiAshRefundNotice.SuppressOverride = null;
            UiAshRefundNotice.ResetForTests();
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiSettings.Language = UiStrings.English;
            LogAssert.ignoreFailingMessages = false;
            if (_canvas != null)
            {
                Object.Destroy(_canvas);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            Assert.AreEqual(0, _errors.Count, "UI errors while the tree was up:\n" + string.Join("\n", _errors));
        }

        private void OnRaised(UiEvent e, string detail)
        {
            _events.Add(e);
        }

        private void Collect(string message, string trace, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) &&
                (message.Contains("[UI]") || message.Contains("NullReferenceException") || message.Contains("IndexOutOfRangeException") ||
                 message.Contains("ArgumentException") || message.Contains("MissingReferenceException")))
            {
                _errors.Add(type + ": " + message);
            }
        }

        private int Count(UiEvent e)
        {
            int n = 0;
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i] == e) n++;
            }

            return n;
        }

        private static UiTreeCaptureShop Staged()
        {
            var shop = new UiTreeCaptureShop(1350, 6);
            foreach (string id in new[] { "sh_heavy", "sh_bore", "sh_double", "sh_ricochet", "mg_ext", "sp_rate1", "sp_rate2", "el_fire", "kx_crit", "sv_hp1", "ec_gold1" })
            {
                shop.Grant(id);
            }

            return shop;
        }

        private IEnumerator Open(Vector2 size)
        {
            Canvas canvas = UiFactory.CreateCanvas("TreeV2Test.Canvas", UiTokens.SortingScreen);
            _canvas = canvas.gameObject;
            UiFactory.EnsureEventSystem();
            RectTransform host = UiFactory.NewRect("Host", canvas.transform);
            host.sizeDelta = size;
            yield return null;
            _panel = UiTreeScreenV2.Create(host);
            _panel.Continued += () => _continued++;
            _shop = Staged();
            _panel.Open(_shop);
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        private static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

        private static void Submit(Selectable control)
        {
            ExecuteEvents.Execute(control.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        private int NodeOf(GameObject go)
        {
            for (int i = 0; i < _shop.Snapshot.Nodes.Length; i++)
            {
                if (_panel.NodeButton(i) != null && _panel.NodeButton(i).gameObject == go) return i;
            }

            return -1;
        }

        [UnityTest]
        public IEnumerator Acilis_TekTreeOpened_OdakDugumde_DetayDolu()
        {
            yield return Open(new Vector2(1920f, 1080f));
            Assert.AreEqual(1, Count(UiEvent.TreeOpened), "TreeOpened once per open");
            Assert.IsNotNull(Selected, "a control holds the focus on frame 1");
            int node = NodeOf(Selected);
            Assert.GreaterOrEqual(node, 0, "the opening focus is a node (there are buyable ones): " + Selected.name);
            Assert.AreEqual(TreeNodeState.Available, _shop.Snapshot.Nodes[node].State, "the first useful node is buyable");
            Assert.AreEqual(node, _panel.DetailTarget, "the detail names the focused node");
            Assert.IsFalse(string.IsNullOrEmpty(_panel.DetailState.text), "its state in words");
            Assert.IsTrue(_panel.FogLabel.gameObject.activeInHierarchy, "round 6: the fog line over IV–V");
            StringAssert.Contains("10", _panel.FogLabel.text);
        }

        [UnityTest]
        public IEnumerator Gezinme_TumIzgaraErisilir_DalAtlamaHatirlar_SinerjiGidipGelir()
        {
            yield return Open(new Vector2(1920f, 1080f));
            TreeScreenSnapshot s = _shop.Snapshot;
            var reached = new HashSet<Selectable>();
            var queue = new Queue<Selectable>();
            Selectable start = _panel.NodeButton(0);
            reached.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                UnityEngine.UI.Navigation nav = queue.Dequeue().navigation;
                foreach (Selectable next in new[] { nav.selectOnUp, nav.selectOnDown, nav.selectOnLeft, nav.selectOnRight })
                {
                    if (next != null && reached.Add(next)) queue.Enqueue(next);
                }
            }

            for (int i = 0; i < s.Nodes.Length; i++)
            {
                Assert.IsTrue(reached.Contains(_panel.NodeButton(i)), "A16: " + s.Nodes[i].NodeId + " reachable with the pad");
            }

            for (int k = 0; k < s.Synergies.Length; k++)
            {
                Assert.IsTrue(reached.Contains(_panel.SynergyButton(k)), "synergy badge " + k + " reachable");
            }

            Assert.IsTrue(reached.Contains(_panel.ContinueButton), "CONTINUE reachable from the grid");

            int first = NodeOf(Selected);
            int row = _panel.Layout.RowOfNode(first);
            _panel.HandleKey(TreeV2Key.NextBranch);
            yield return null;
            int moved = NodeOf(Selected);
            Assert.AreEqual((row + 1) % TreeV2Rows.Count, _panel.Layout.RowOfNode(moved), "RB: the next branch");
            _panel.HandleKey(TreeV2Key.PreviousBranch);
            yield return null;
            Assert.AreEqual(first, NodeOf(Selected), "LB back: the branch remembered its node");

            _panel.HandleKey(TreeV2Key.Synergies);
            yield return null;
            Assert.AreSame(_panel.SynergyButton(0).gameObject, Selected, "Y: the synergy column");
            Assert.IsFalse(string.IsNullOrEmpty(_panel.DetailName.text), "the badge fills the detail");
            _panel.HandleKey(TreeV2Key.Synergies);
            yield return null;
            Assert.AreEqual(first, NodeOf(Selected), "Y again: back to the tree");
        }

        [UnityTest]
        public IEnumerator Komutlar_ANodeTryBuy_OdakKalir_Hedef_XDevam()
        {
            yield return Open(new Vector2(1920f, 1080f));
            int node = NodeOf(Selected);
            int gold = _shop.Gold;
            int price = _shop.Snapshot.Nodes[node].Price;
            Submit(_panel.NodeButton(node));
            yield return null;
            Assert.AreEqual(1, _shop.BuyCalls, "A on a node = TryBuy");
            Assert.AreEqual(node, _shop.LastBuyIndex);
            Assert.AreEqual(TreeNodeState.Purchased, _shop.Snapshot.Nodes[node].State);
            Assert.AreEqual(gold - price, _shop.Snapshot.Gold);
            Assert.AreSame(_panel.NodeButton(node).gameObject, Selected, "the focus stays on the bought node");
            Assert.AreEqual(0, Count(UiEvent.UpgradePurchased), "the purchase is voiced by TreeEvents (S-08), not a UiEvent");

            // TARGET on a fogged node deep in a branch: SetTarget, its path marked.
            int deep = _shop.IndexOf("sh_fan");
            _panel.FocusNode(deep);
            yield return null;
            Assert.IsTrue(_panel.TargetButton.IsInteractable());
            Submit(_panel.TargetButton);
            yield return null;
            Assert.AreEqual(1, _shop.TargetCalls);
            Assert.AreEqual(deep, _shop.LastTargetIndex);
            Assert.IsTrue(_shop.Snapshot.Nodes[deep].IsTarget);
            _panel.HandleKey(TreeV2Key.Target);
            yield return null;
            Assert.AreEqual(-1, _shop.LastTargetIndex, "T on the Target clears it");

            // A refused buy (fogged) says why and changes nothing.
            int buys = _shop.BuyCalls;
            Assert.IsFalse(_panel.Buy(deep));
            Assert.AreEqual(buys + 1, _shop.BuyCalls);
            Assert.AreEqual(TreeNodeState.Fogged, _shop.Snapshot.Nodes[deep].State);

            _panel.HandleKey(TreeV2Key.Continue);
            yield return null;
            Assert.AreEqual(1, _shop.ContinueCalls, "X / C: Continue - one key");
            Assert.AreEqual(1, _continued, "the host is told to close the round end");
        }

        [UnityTest]
        public IEnumerator Reroll_TekTreeRerolled_UpgradeRerolledYok()
        {
            yield return Open(new Vector2(1920f, 1080f));
            _events.Clear();
            Assert.IsTrue(_panel.RerollButton.IsInteractable());
            Submit(_panel.RerollButton);
            yield return null;
            Assert.AreEqual(1, _shop.RerollCalls);
            Assert.AreEqual(1, Count(UiEvent.TreeRerolled), "one TreeRerolled");
            Assert.AreEqual(0, Count(UiEvent.UpgradeRerolled), "never the old offer's UpgradeRerolled (a double cue)");
            Assert.AreEqual(0, Count(UiEvent.TreeOpened), "no second open");
        }

        [UnityTest]
        public IEnumerator MetinOlcegi125_EN_TR_1080p_VeMac12_KesilmeYok([Values("en", "tr")] string language, [Values(0, 1)] int canvas)
        {
            UiSettings.TextScale = 1.25f;
            UiSettings.Language = language;
            Vector2 size = canvas == 0 ? new Vector2(1920f, 1080f) : new Vector2(1600f, 1039f);
            yield return Open(size);
            bool tr = language == "tr";
            bool fits = _panel.ShowTextForTests(
                tr ? "Zincirleme Patlama" : "Explosive Rounds",
                tr ? "KADEME III · HAYATTA KALMA" : "TIER III · SURVIVAL",
                tr ? "ÖNCE ZİNCİRLEME PATLAMA" : "NEEDS EXPLOSIVE ROUNDS",
                tr ? LongTr : LongEn,
                tr ? "HEDEF\nUYANIŞ\nBilardo Fırtınası · ETKİN" : "TARGET\nAWAKENING\nPinball Storm · ACTIVE");
            Assert.IsTrue(fits, language + " " + size + ": the detail card cuts no word and keeps BUY / TARGET inside");
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                TMP_Text label = _panel.RowLabel(r);
                label.ForceMeshUpdate();
                Assert.IsFalse(label.isTextTruncated, language + ": branch label '" + label.text + "' cut");
            }

            Assert.IsTrue(_panel.Layout.Fits, size + ": the tree fits (scale " + _panel.Layout.Scale + ")");
            foreach (TMP_Text one in new[] { _panel.GoldText, _panel.AshText, _panel.FogLabel })
            {
                one.ForceMeshUpdate();
                Assert.LessOrEqual(one.preferredWidth, one.rectTransform.rect.width + 1f, language + ": '" + one.text + "' wider than its box");
            }

            _panel.StripCaption.ForceMeshUpdate();
            Assert.IsFalse(_panel.StripCaption.isTextTruncated, language + ": the strip caption is cut");
            foreach (UiButton action in new[] { _panel.RerollButton, _panel.HearthButton, _panel.ContinueButton })
            {
                action.LabelText.ForceMeshUpdate();
                Assert.IsFalse(action.LabelText.isTextTruncated, language + ": '" + action.LabelText.text + "' cut");
            }
            RectTransform root = _panel.Root;
            var a = new Vector3[4];
            var b = new Vector3[4];
            root.GetWorldCorners(b);
            for (int i = 0; i < _shop.Snapshot.Nodes.Length; i++)
            {
                ((RectTransform)_panel.NodeButton(i).transform).GetWorldCorners(a);
                Assert.IsTrue(a[0].x >= b[0].x - 1f && a[2].x <= b[2].x + 1f && a[0].y >= b[0].y - 1f && a[2].y <= b[2].y + 1f,
                    _shop.Snapshot.Nodes[i].NodeId + " inside the screen at " + size);
            }

            _panel.ActionsRect.GetWorldCorners(a);
            _panel.DetailRect.GetWorldCorners(b);
            Assert.LessOrEqual(b[0].y + 0.5f, b[1].y);
            Assert.LessOrEqual(a[1].y, b[0].y + 0.5f, "the actions block sits below the detail card");
        }

        // ------------------------------------------------------------------ fix2: the price plate and the Awakening tiles

        private static Rect World(RectTransform rect)
        {
            var c = new Vector3[4];
            rect.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        /// <summary>Two rects share more than half a pixel.</summary>
        private static bool Meet(Rect a, Rect b)
        {
            return a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;
        }

        private static bool Inside(Rect inner, Rect outer)
        {
            return inner.xMin >= outer.xMin - 0.5f && inner.xMax <= outer.xMax + 0.5f && inner.yMin >= outer.yMin - 0.5f && inner.yMax <= outer.yMax + 0.5f;
        }

        private static void AssertWords(TMP_Text text, string when)
        {
            text.ForceMeshUpdate();
            Assert.IsFalse(text.isTextTruncated, when + ": '" + text.text + "' cut");
            Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width + 1f, when + ": '" + text.text + "' wider than its box");
        }

        private void AssertPlateClear(string when)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform glyphRect = _panel.BuyGlyphRect;
            Assert.IsNotNull(glyphRect, when + ": BUY's Submit glyph");
            Assert.IsTrue(glyphRect.gameObject.activeInHierarchy, when + ": the glyph shows on a buyable node");
            AssertWords(_panel.PriceText, when + " price");
            if (!string.IsNullOrEmpty(_panel.DiscountText.text))
            {
                AssertWords(_panel.DiscountText, when + " discount");
            }

            Rect plate = World(_panel.PricePlateRect);
            Rect glyph = World(glyphRect);
            Rect buy = World((RectTransform)_panel.BuyButton.transform);
            Rect target = World((RectTransform)_panel.TargetButton.transform);
            Rect price = World(_panel.PriceText.rectTransform);
            Rect discount = World(_panel.DiscountText.rectTransform);
            Assert.IsFalse(Meet(glyph, price), when + ": the ENTER / A glyph over the price");
            Assert.IsFalse(Meet(glyph, discount), when + ": the glyph over the discount");
            Assert.IsFalse(Meet(glyph, buy), when + ": the glyph over BUY");
            Assert.IsFalse(Meet(glyph, target), when + ": the glyph over TARGET");
            Assert.IsFalse(Meet(buy, price) || Meet(buy, discount), when + ": BUY over the price line");
            Assert.IsFalse(Meet(target, price) || Meet(target, discount) || Meet(target, buy), when + ": TARGET over the price line or BUY");
            Assert.IsTrue(Inside(glyph, plate) && Inside(buy, plate) && Inside(target, plate) && Inside(price, plate), when + ": the plate holds price, glyph, BUY and TARGET");
            Assert.IsTrue(Inside(plate, World(_panel.DetailRect)), when + ": the plate stays inside the detail card");
        }

        /// <summary>
        /// fix2 (12.10 capture): BUY's ENTER glyph was drawn over "45 GOLD". The price line now stands alone above BUY and TARGET:
        /// no overlap of glyph, price, discount, BUY and TARGET, in EN / TR, text 1.0 / 1.25, 1080p and Mac UI 1.2 - as opened and
        /// with the longest price a node can show.
        /// </summary>
        [UnityTest]
        public IEnumerator FiyatPlakasi_GirisGlifiFiyataBinmez_HerDilVeOlcekte([Values("en", "tr")] string language, [Values(1f, 1.25f)] float scale, [Values(0, 1)] int canvas)
        {
            UiSettings.TextScale = scale;
            UiSettings.Language = language;
            yield return Open(canvas == 0 ? new Vector2(1920f, 1080f) : new Vector2(1600f, 1039f));
            string when = language + " " + scale + " " + (canvas == 0 ? "1080p" : "Mac 1.2");
            int node = NodeOf(Selected);
            Assert.GreaterOrEqual(node, 0, when + ": the opening focus is a node");
            Assert.AreEqual(TreeNodeState.Available, _shop.Snapshot.Nodes[node].State, when + ": a buyable node - BUY and its glyph show");
            AssertPlateClear(when + " as opened");

            _panel.ShowPriceForTests(TreeV2Text.Gold(13500), TreeV2Text.Discount(new TreeNodeView { Cost = 13500, Price = 10125 }));
            yield return null;
            AssertPlateClear(when + " longest price");
        }

        /// <summary>Unlocked, buyable, Ash short and locked nodes with the B1a catalog's words (no profile read).</summary>
        private sealed class MixedAwakening : IAwakeningSource
        {
            public bool CanUnlock => true;

            public void Read(AwakeningPageModel into)
            {
                into.ResetToLayout();
                into.AshBalance = 120;
                into.Pending = false;
                into.CanUnlock = true;
                for (int i = 0; i < into.Entries.Count; i++)
                {
                    AwakeningEntry e = into.Entries[i];
                    foreach (MetaPathNode n in MetaPathCatalog.Nodes)
                    {
                        if (n != null && n.Id == e.Id)
                        {
                            e.StringKey = n.StringKey;
                        }
                    }

                    e.Cost = i == 1 ? 100 : 350;
                    e.State = i == 0 ? AwakeningEntryState.Unlocked
                        : i == 1 ? AwakeningEntryState.Available
                        : i % 3 == 0 ? AwakeningEntryState.Locked
                        : AwakeningEntryState.Short;
                    into.Entries[i] = e;
                }
            }

            public bool TryUnlock(string id, out string refusal)
            {
                refusal = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// fix2 (12.10 capture: "300 KÜL EKSİ" cut on the tile): a tile's state line is short and measured - the price (UNLOCKED
        /// once open) with its lock / open glyph, the words alone or the glyph alone when the pair does not fit - and is never cut;
        /// the glyph never meets the words; the names keep their two lines. EN / TR, text 1.0 / 1.25, 1080p and Mac UI 1.2.
        /// </summary>
        [UnityTest]
        public IEnumerator Uyanis_KaroDurumu_KesilmezGlifYaziyaBinmez([Values("en", "tr")] string language, [Values(1f, 1.25f)] float scale, [Values(0, 1)] int canvas)
        {
            UiSettings.TextScale = scale;
            UiSettings.Language = language;
            Canvas root = UiFactory.CreateCanvas("AwakeningTest.Canvas", UiTokens.SortingScreen);
            _canvas = root.gameObject;
            UiFactory.EnsureEventSystem();
            RectTransform host = UiFactory.NewRect("Host", root.transform);
            host.sizeDelta = canvas == 0 ? new Vector2(1920f, 1080f) : new Vector2(1600f, 1039f);
            yield return null;
            UiAwakeningPage page = UiAwakeningPage.Create(host);
            page.Open(new MixedAwakening(), null);
            for (int f = 0; f < 3; f++)
            {
                yield return null;
            }

            Canvas.ForceUpdateCanvases();
            string when = language + " " + scale + " " + (canvas == 0 ? "1080p" : "Mac 1.2");
            for (int i = 0; i < page.Model.Entries.Count; i++)
            {
                AwakeningEntry e = page.Model.Entries[i];
                string id = when + " " + e.Id;
                TMP_Text state = page.CellState(i);
                Image glyph = page.CellGlyph(i);
                Rect cell = World((RectTransform)page.CellButton(i).transform);
                bool word = state.gameObject.activeInHierarchy && !string.IsNullOrEmpty(state.text);
                bool mark = glyph != null && glyph.enabled && glyph.sprite != null;
                Assert.IsTrue(word || mark, id + ": the tile says its state");
                if (word)
                {
                    AssertWords(state, id + " state");
                    Assert.IsTrue(Inside(World(state.rectTransform), cell), id + ": the state line inside its tile");
                    if (e.State == AwakeningEntryState.Short)
                    {
                        Assert.AreEqual(TreeV2Text.Ash(e.Cost), state.text, id + ": the tile shows the price; the shortfall sentence is the detail's");
                    }
                }

                if (mark)
                {
                    Assert.IsTrue(Inside(World(glyph.rectTransform), cell), id + ": the glyph inside its tile");
                    if (word)
                    {
                        Assert.IsFalse(Meet(World(glyph.rectTransform), World(state.rectTransform)), id + ": the glyph over the words");
                    }
                }

                TMP_Text name = page.CellName(i);
                name.ForceMeshUpdate();
                Assert.IsFalse(name.isTextTruncated, id + ": the name '" + name.text + "' cut");
            }

            page.Close();
        }

        /// <summary>
        /// The selection point in the real round-end flow: B1 absent (no provider) → the shipped page / WP-077 panel, the game
        /// unchanged; a provider (here the fake, through the route's override) → the V2 tree; its X ends the round-end phase.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gecis_SaglayiciYokkenEskiPanel_VarkenYeniEkran_DevamRounduKapatir()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            RunDirector run = RunDirector.Instance;

            Assert.IsNull(UiTreeScreenRoute.Resolve(), "B1 absent: no provider");
            Assert.IsTrue(EnemySpawnDirector.Instance.DebugCompleteRound());
            yield return LifecycleTestKit.Bekle(() => run.Phase == RunPhase.Upgrading, 30f, "round 1's round end did not open");
            yield return null;
            yield return null;
            UpgradeScreen screen = Object.FindObjectOfType<UpgradeScreen>();
            Assert.IsFalse(screen.TreeV2Active, "no provider: the shipped round-end page");
            Assert.IsTrue(screen.Pane.IsOpen, "the shipped window is open");
            Assert.IsTrue(screen.TreeV2 == null || !screen.TreeV2.IsOpen);
            run.CloseUpgradeScreen();

            var fake = Staged();
            UiTreeScreenRoute.Override(fake);
            yield return LifecycleTestKit.Bekle(() => run.Phase == RunPhase.Fighting, 40f, "round 2 did not start");
            Assert.IsTrue(EnemySpawnDirector.Instance.DebugCompleteRound());
            yield return LifecycleTestKit.Bekle(() => run.Phase == RunPhase.Upgrading, 30f, "round 2's round end did not open");
            yield return null;
            yield return null;
            Assert.IsTrue(screen.TreeV2Active, "a provider: the feature-based tree");
            Assert.IsTrue(screen.TreeV2.IsOpen);
            Assert.IsFalse(screen.Pane.IsOpen, "the shipped page stays closed under it");
            screen.TreeV2.HandleKey(TreeV2Key.Continue);
            yield return LifecycleTestKit.Bekle(() => run.Phase != RunPhase.Upgrading, 10f, "CONTINUE did not end the round end");
            Assert.AreEqual(1, fake.ContinueCalls, "the provider's Continue ran once");
            UiTreeScreenRoute.Override(null);
        }

        /// <summary>S-04 B1 (17f29c5d) on: the round end's tree is the run's RunTreeV2 through the route, live data and commands.</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator B1Saglayici_CanliAgac_CoinSenseAlinir_DevamFaziVeAgaciKapatir()
        {
            LogAssert.ignoreFailingMessages = true;
            RoundUpgradeShop.TreeV2TestOverride = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            RunDirector run = RunDirector.Instance;
            Assert.IsTrue(run.Shop.TreeV2.IsBuilt, "RunTreeV2 is built for this run (TreeV2Active)");
            Assert.IsTrue(EnemySpawnDirector.Instance.DebugCompleteRound());
            yield return LifecycleTestKit.Bekle(() => run.Phase == RunPhase.Upgrading, 30f, "the round end did not open");
            yield return null;
            yield return null;
            UpgradeScreen screen = Object.FindObjectOfType<UpgradeScreen>();
            Assert.AreSame(run.Shop.TreeV2, UiTreeScreenRoute.Resolve(), "the route names the run's own tree");
            Assert.IsTrue(screen.TreeV2Active, "the round end shows the feature-based tree");
            TreeScreenSnapshot s = run.Shop.TreeV2.Snapshot;
            Assert.AreEqual(70, s.Nodes.Length, "63 nodes + 7 awakening variants");
            Assert.AreEqual(10, s.Synergies.Length);

            run.Wallet.DebugSet(1000);
            run.Shop.TreeV2.Refresh();
            screen.TreeV2.Refresh();
            int coin = -1;
            for (int i = 0; i < s.Nodes.Length; i++)
            {
                if (s.Nodes[i].NodeId == "ec_gold1") coin = i;
            }

            Assert.GreaterOrEqual(coin, 0);
            Assert.AreEqual(TreeNodeState.Available, s.Nodes[coin].State, "Coin Sense I is live and buyable in B1");
            int gold = run.Wallet.Balance;
            Assert.IsTrue(screen.TreeV2.Buy(coin), "TryBuy through the screen");
            Assert.AreEqual(TreeNodeState.Purchased, s.Nodes[coin].State);
            Assert.Less(run.Wallet.Balance, gold, "the run's wallet paid");
            Assert.IsTrue(run.Shop.TreeV2.Owns("ec_gold1"));

            screen.TreeV2.HandleKey(TreeV2Key.Continue);
            yield return LifecycleTestKit.Bekle(() => run.Phase != RunPhase.Upgrading, 10f, "CONTINUE did not end the round end");
            Assert.IsFalse(run.Shop.TreeV2.IsOpen, "the provider's Continue closed its round");
        }

        /// <summary>The one-time "Ash refunded: +X" notice: on the menu once, read = acknowledged, never again this session.</summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator IadeBildirimi_MenudeBirKez_OkununcaTemizlenir()
        {
            LogAssert.ignoreFailingMessages = true;
            int pending = 420;
            int acks = 0;
            UiAshRefundNotice.ResetForTests();
            UiAshRefundNotice.SuppressOverride = false;
            UiAshRefundNotice.PendingSource = () => pending;
            UiAshRefundNotice.AcknowledgeAction = () =>
            {
                acks++;
                pending = 0;
                return MetaCommandResult.Done;
            };

            yield return LifecycleTestKit.BootTanMainMenuye();
            yield return LifecycleTestKit.Bekle(() => UiAshRefundNotice.Current != null, 20f, "the refund notice did not show on the menu");
            UiModal notice = UiAshRefundNotice.Current;
            string words = string.Empty;
            foreach (TMP_Text t in notice.GetComponentsInChildren<TMP_Text>(true))
            {
                words += t.text + " ";
            }

            StringAssert.Contains("420", words, "the refund amount is in the notice");
            Assert.AreSame(notice.Primary.gameObject, Selected, "the notice holds the focus");
            Submit(notice.Primary);
            yield return null;
            Assert.AreEqual(1, acks, "reading it acknowledges it once");
            Assert.IsNull(UiAshRefundNotice.Current, "the notice closed");

            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.GoToMainMenu();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.MainMenu);
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until)
            {
                Assert.IsNull(UiAshRefundNotice.Current, "never twice");
                yield return null;
            }

            Assert.AreEqual(1, acks);
        }
    }
}
