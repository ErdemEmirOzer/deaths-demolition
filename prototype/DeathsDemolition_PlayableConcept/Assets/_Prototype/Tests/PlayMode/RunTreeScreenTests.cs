using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-068 R1-C · the in-run tree on the round-end shop (N14) with S-04's node cards (WP-077 K10a). The tree takes the
    /// card row's place: every node the offer sells carries its OFFER mark, BUY buys it through its card, UNDO LAST in
    /// the footer refunds it in full, the Hearth is stoked directly, a pin draws its path, the lanes switch, and the
    /// grid and the detail card fit their host at 1080p and at the Mac setting (UI 1.2 × text 1.25).
    /// </summary>
    public sealed class RunTreeScreenTests
    {
        private static readonly Vector3[] s_a = new Vector3[4];
        private static readonly Vector3[] s_b = new Vector3[4];
        private readonly List<string> _errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _errors.Clear();
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            Application.logMessageReceived += Collect;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Collect;
            LogAssert.ignoreFailingMessages = false;
            UiSettings.TextScale = 1f;
            UiSettings.UiScale = 1f;
            UiSettings.Language = UiStrings.English;
            RunFlowClock.ClearHolds();
            RunTreeEffects.TreatAllAsLive = false;
            if (RunDirector.Instance != null && RunDirector.Instance.Shop != null)
            {
                RunDirector.Instance.Shop.NodeCardsOverride = null;
            }

            if (EnemySpawnDirector.Instance != null)
            {
                Spawning(true);
            }

            if (PlayerRegistry.Primary != null && PlayerRegistry.Primary.Health != null)
            {
                PlayerRegistry.Primary.Health.DiagnosisInvulnerable = false;
            }

            Assert.AreEqual(0, _errors.Count, "UI / run errors while the tree was open:\n" + string.Join("\n", _errors));
        }

        private void Collect(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (message.Contains("[UI]") || message.Contains("[Run]") || message.Contains("[Gold]") ||
                message.Contains("NullReferenceException") || message.Contains("MissingReferenceException") ||
                message.Contains("IndexOutOfRangeException") || message.Contains("ArgumentException"))
            {
                _errors.Add(type + ": " + message);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Agac_TeklifIsaretli_Al_GeriAl_Ocak_Pin_Serit()
        {
            yield return OpenTree();
            RunDirector run = RunDirector.Instance;
            RoundUpgradeShop shop = run.Shop;
            UpgradeScreen screen = Object.FindObjectOfType<UpgradeScreen>();
            UiRunTreePanel tree = screen.GetComponentInChildren<UiRunTreePanel>(false);
            Assert.IsNotNull(tree, "the tree replaces the card row while node cards are on");
            foreach (UiButton b in screen.GetComponentsInChildren<UiButton>(false))
            {
                Assert.IsFalse(b.gameObject.name.StartsWith("Card."), "no classic card is shown in tree mode: " + b.gameObject.name);
            }

            RunTreeSnapshot snap = tree.Snapshot;
            Assert.Greater(snap.Count, 0, "the snapshot has the run's tree");
            Assert.Greater(shop.Cards.Count, 0, "the offer has node cards");
            Assert.IsNotNull(tree.NodeButton(0, 0) ?? tree.NodeButton(1, 0) ?? tree.NodeButton(2, 0) ?? tree.NodeButton(3, 0), "tier 1 of the weapon lane is drawn");
            Assert.Greater(tree.EdgeCount, 0, "the weapon lane draws its edges");
            Assert.GreaterOrEqual(tree.GateX, 0f, "the keystone gate is drawn before the forks / seal / Hearth");
            Assert.IsTrue(tree.GateLabel.gameObject.activeSelf, "the gate carries its word (AD R1-C MUST-3b)");
            StringAssert.Contains("KEYSTONE", tree.GateLabel.text);

            // Every node on screen carries a word (PD §5.1), and every node the offer sells its OFFER mark.
            for (int r = 0; r < UiRunTreePanel.Rows; r++)
            {
                for (int t = 0; t < UiRunTreePanel.Tiers; t++)
                {
                    UiButton node = tree.NodeButton(r, t);
                    if (node != null)
                    {
                        TMP_Text state = node.transform.Find("State").GetComponent<TMP_Text>();
                        Assert.IsFalse(string.IsNullOrEmpty(state.text), "node " + r + "," + t + " has a state word");
                    }
                }
            }

            int bought = -1;
            for (int c = 0; c < shop.Cards.Count; c++)
            {
                UpgradeCardOffer card = shop.Cards[c];
                if (!card.IsNode || card.NodeIndex < 0)
                {
                    continue;
                }

                tree.FocusNode(card.NodeIndex);
                Assert.IsTrue(tree.OfferShownOn(card.NodeIndex), snap.Nodes[card.NodeIndex].NodeId + " is marked OFFER");
                if (bought < 0 && snap.Nodes[card.NodeIndex].Status == RunTreeNodeStatus.Available)
                {
                    bought = card.NodeIndex;
                }
            }

            // BUY through the card; the seal reads OWNED; UNDO LAST in the footer gives the gold back in full.
            Assert.GreaterOrEqual(bought, 0, "some offered node is affordable with 5000 gold");
            tree.FocusNode(bought);
            Assert.AreEqual(tree.ButtonOf(bought).gameObject, EventSystem.current.currentSelectedGameObject, "focus = the detail's subject (MUST-4)");
            Assert.IsTrue(tree.BuyButton.IsInteractable(), "BUY is live on an offered, affordable node");
            UiButton next = Named(screen, "Continue");
            Assert.AreEqual(UiButtonVariant.Secondary, next.Variant, "one Primary: BUY is live, CONTINUE steps down (MUST-1)");
            int gold = shop.Gold;
            tree.OnBuy();
            yield return null;
            Assert.Less(shop.Gold, gold, "BUY spent gold");
            RunTreeNodeStatus after = tree.Snapshot.Nodes[bought].Status;
            Assert.IsTrue(after == RunTreeNodeStatus.Purchased || after == RunTreeNodeStatus.MaxTier || tree.Snapshot.Nodes[bought].OwnedTier > 0,
                "the bought node is owned (" + after + ")");
            Assert.IsFalse(tree.OfferShownOn(bought), "a bought card's node no longer reads OFFER");
            UiButton undo = Named(screen, "Undo");
            Assert.IsTrue(undo.gameObject.activeInHierarchy, "UNDO LAST shows after a purchase");
            StringAssert.Contains("+", undo.LabelText.text, "UNDO names its refund");
            Click(undo);
            yield return null;
            Assert.AreEqual(gold, shop.Gold, "UNDO refunds in full");
            int locked = -1;
            for (int i = 0; i < tree.Snapshot.Count && locked < 0; i++)
            {
                if (tree.Snapshot.Nodes[i].Lane == tree.Lane && tree.Snapshot.Nodes[i].Status == RunTreeNodeStatus.Locked) locked = i;
            }

            if (locked >= 0)
            {
                tree.FocusNode(locked);
                Assert.AreEqual(UiButtonVariant.Primary, next.Variant, "nothing buyable detailed: CONTINUE is the Primary");
            }
            Assert.IsFalse(undo.gameObject.activeInHierarchy, "one step of undo");

            // The Hearth is stoked directly, outside the cards, once a weapon-lane keystone opens it (WP-077 K3);
            // until then STOKE is dead and the seal says what it needs.
            int hearth = tree.Snapshot.HearthIndex;
            if (hearth >= 0)
            {
                tree.FocusNode(hearth);
                int stacks = tree.Snapshot.HearthStacks;
                StringAssert.Contains("HEARTH", tree.BuyButton.LabelText.text.ToUpperInvariant(), "BUY reads STOKE THE HEARTH");
                RunTreeNodeStatus status = tree.Snapshot.Nodes[hearth].Status;
                if (status == RunTreeNodeStatus.Available)
                {
                    tree.OnBuy();
                    yield return null;
                    Assert.AreEqual(stacks + 1, tree.Snapshot.HearthStacks, "the Hearth took one stoke");
                }
                else
                {
                    Assert.IsFalse(tree.BuyButton.IsInteractable(), "STOKE waits for the Hearth's prerequisite (" + status + ")");
                    StringAssert.Contains(status == RunTreeNodeStatus.PrereqMissing ? "NEEDS" : string.Empty,
                        tree.StateTextOf(hearth).text.ToUpperInvariant(), "the Hearth's seal names its prerequisite");
                }
            }

            // A pin on a node the offer does not sell: PINNED, and its path drawn.
            int pin = -1;
            for (int i = 0; i < tree.Snapshot.Count; i++)
            {
                RunTreeNodeView n = tree.Snapshot.Nodes[i];
                if (n.Lane == RunTreeLane.Weapon && n.Status == RunTreeNodeStatus.PrereqMissing && n.Kind != RunTreeNodeKind.Hearth)
                {
                    pin = i;
                    break;
                }
            }

            if (pin >= 0)
            {
                tree.FocusNode(pin);
                Assert.IsTrue(tree.PinButton.IsInteractable(), "PIN is live on a node still out of reach");
                tree.OnPin();
                yield return null;
                Assert.AreEqual(pin, tree.Snapshot.PinnedIndex, "the pin is set");
                Assert.Greater(tree.Snapshot.PinPathCount, 0, "the pin has a path");
                StringAssert.Contains("UNPIN", tree.PinButton.LabelText.text.ToUpperInvariant());
            }

            // Lanes.
            tree.SetLane(RunTreeLane.Body, false);
            Assert.AreEqual(RunTreeLane.Body, tree.Lane);
            bool any = false;
            for (int r = 0; r < UiRunTreePanel.Rows && !any; r++)
            {
                for (int t = 0; t < UiRunTreePanel.Tiers && !any; t++)
                {
                    any = tree.NodeButton(r, t) != null;
                }
            }

            Assert.IsTrue(any, "the body lane draws its nodes");
            Debug.Log("[DD-TEST] r1c.tree 1080 compact=" + tree.Compact + " seal=" + tree.SealPx + " pitch=" + tree.RowPitchPx +
                      " tree=" + tree.TreeRect.rect.size + " detail=" + tree.DetailRect.rect.size);
            Assert.IsFalse(tree.Compact, "1080p keeps the seal over its name (PD §5)");
            Assert.AreEqual(UiRunTreePanel.SealSize, tree.SealPx, "1080p seal 64");
            AssertFits(screen, tree, "1080p");
            run.CloseUpgradeScreen();
            yield return null;
        }

        /// <summary>The Mac setting: the rows compact (the seal beside its words) and the grid, the detail card and BUY stay inside their hosts.</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Kompakt_UiOlcek12_Metin125_AgacSigar()
        {
            yield return OpenTree(1.2f, 1.25f);
            UpgradeScreen screen = Object.FindObjectOfType<UpgradeScreen>();
            UiRunTreePanel tree = screen.GetComponentInChildren<UiRunTreePanel>(false);
            Assert.IsNotNull(tree);
            Debug.Log("[DD-TEST] r1c.tree mac compact=" + tree.Compact + " seal=" + tree.SealPx + " pitch=" + tree.RowPitchPx +
                      " tree=" + tree.TreeRect.rect.size + " detail=" + tree.DetailRect.rect.size);
            Assert.GreaterOrEqual(tree.SealPx, 40f, "the seal stays a seal");
            Assert.IsFalse(tree.Compact, "the Mac host has the room for the stacked row");
            AssertFits(screen, tree, "mac");
            tree.SetLane(RunTreeLane.Body, false);
            AssertFits(screen, tree, "mac body");

            // The Mac aspect (3024 × 1964) leaves the host ~510 px: the rows go compact and the words beside the seal
            // wrap to two lines rather than cut a name ("Regeneration Delay" / "Regeneration Rate" must both print).
            var host = (RectTransform)tree.transform.parent;
            host.offsetMin += new Vector2(0f, 160f);
            RunTreeEffects.TreatAllAsLive = true;   // keystones / forks read NEEDS <name> (their effects are pending in this build)
            tree.Refresh(RunDirector.Instance.Shop);
            Assert.IsTrue(tree.Compact, "a ~510 px host compacts the rows");
            foreach (RunTreeLane lane in new[] { RunTreeLane.Body, RunTreeLane.Weapon })
            {
                tree.SetLane(lane, false);
                var needs = new Dictionary<string, int>();
                for (int i = 0; i < tree.Snapshot.Count; i++)
                {
                    TMP_Text word = tree.StateTextOf(i);
                    RunTreeNodeView n = tree.Snapshot.Nodes[i];
                    if (word != null && n.Status == RunTreeNodeStatus.PrereqMissing && n.MissingParentIndex >= 0 && word.text.StartsWith("NEEDS"))
                    {
                        // Two different prerequisites never print the same word (AD R1-C MUST-2: "NEEDS REGENERATION" ×2).
                        string key = word.text.Replace("\n", " ");
                        if (needs.TryGetValue(key, out int other) && other != n.MissingParentIndex && tree.Snapshot.Nodes[other].Kind == RunTreeNodeKind.Tier)
                        {
                            Assert.Fail("compact " + lane + ": '" + key + "' names two prerequisites");
                        }

                        needs[key] = n.MissingParentIndex;
                    }
                }

                for (int i = 0; i < tree.Snapshot.Count; i++)
                {
                    TMP_Text name = tree.NameTextOf(i);
                    if (name == null) continue;
                    name.ForceMeshUpdate();
                    Assert.IsFalse(name.isTextTruncated, "compact " + lane + ": " + name.text + " prints whole");
                    TMP_Text state = tree.StateTextOf(i);
                    state.ForceMeshUpdate();
                    Assert.IsFalse(state.isTextTruncated, "compact " + lane + ": " + state.text + " prints whole");
                }

                AssertFits(screen, tree, "mac compact " + lane);
            }

            host.offsetMin -= new Vector2(0f, 160f);
            RunDirector.Instance.CloseUpgradeScreen();
            yield return null;
        }

        /// <summary>
        /// WP-081 (R1 AD SHOULD-3 / SHOULD-6): stacked rows in Turkish at the Mac setting (UI 1.2 × text 1.25). Every name and
        /// every NEEDS line prints whole in both lanes - the column takes the host's room, names are never shortened - and
        /// every OFFER tag sits inside its node, UiSpace.S under its top edge, where a focused card's brackets above end.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Yigin_TR_UiOlcek12_Metin125_OnceSatiriButun_TeklifAralikli()
        {
            UiSettings.Language = UiStrings.Turkish;
            yield return OpenTree(1.2f, 1.25f);
            UpgradeScreen screen = Object.FindObjectOfType<UpgradeScreen>();
            UiRunTreePanel tree = screen.GetComponentInChildren<UiRunTreePanel>(false);
            Assert.IsNotNull(tree);
            Assert.IsFalse(tree.Compact, "stacked rows at the Mac setting");
            RunTreeEffects.TreatAllAsLive = true;   // keystones / forks read ÖNCE <name> (their effects are pending in this build)
            tree.Refresh(RunDirector.Instance.Shop);
            int offers = 0;
            int needs = 0;
            foreach (RunTreeLane lane in new[] { RunTreeLane.Body, RunTreeLane.Weapon })
            {
                tree.SetLane(lane, false);
                yield return null;
                for (int i = 0; i < tree.Snapshot.Count; i++)
                {
                    TMP_Text name = tree.NameTextOf(i);
                    if (name == null || !name.gameObject.activeInHierarchy) continue;
                    name.ForceMeshUpdate();
                    Assert.IsFalse(name.isTextTruncated, "tr " + lane + ": " + name.text + " prints whole");
                    TMP_Text state = tree.StateTextOf(i);
                    state.ForceMeshUpdate();
                    Assert.IsFalse(state.isTextTruncated, "tr " + lane + ": " + state.text + " prints whole");
                    if (tree.Snapshot.Nodes[i].Status == RunTreeNodeStatus.PrereqMissing)
                    {
                        needs++;
                    }

                    if (tree.OfferShownOn(i))
                    {
                        offers++;
                        RectTransform tag = tree.OfferTagOf(i, out RectTransform node);
                        tag.GetWorldCorners(s_a);
                        float top = node.InverseTransformPoint(s_a[1]).y;
                        Assert.LessOrEqual(top, node.rect.yMax - (int)UiSpace.S + 0.5f,
                            "tr " + lane + " node " + i + ": the OFFER tag keeps UiSpace.S under its node's top edge");
                    }
                }

                AssertFits(screen, tree, "tr mac " + lane);
            }

            Debug.Log("[DD-TEST] wp081.tree tr mac pitch=" + tree.RowPitchPx + " tree=" + tree.TreeRect.rect.size + " offers=" + offers + " needs=" + needs);
            Assert.Greater(offers, 0, "the offer marks some nodes");
            Assert.Greater(needs, 0, "some nodes print a NEEDS line");
            RunDirector.Instance.CloseUpgradeScreen();
            yield return null;
        }

        // ================================================================== helpers

        private static IEnumerator OpenTree(float uiScale = 1f, float textScale = 1f)
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LifecycleTestKit.BootTanMainMenuye();
            UiSettings.UiScale = uiScale;
            UiSettings.TextScale = textScale;
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            PrototypeUiFlow.GoToSelection();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return LifecycleTestKit.SahneyiBekle(PrototypeScenes.Run);
            yield return LifecycleTestKit.KosuHazirOlsun();
            yield return LifecycleTestKit.Bekle(() => EnemySpawnDirector.Instance != null && RunDirector.Instance != null && RunDirector.Instance.Wallet != null, 40f, "run not ready");
            PlayerRegistry.Primary.Health.DiagnosisInvulnerable = true;
            Spawning(false);
            RunDirector.Instance.Shop.NodeCardsOverride = true;
            RunDirector.Instance.Wallet.DebugSet(5000);
            Assert.IsTrue(PressDebug("Complete this round"), "F1 'Complete this round' is missing");
            yield return LifecycleTestKit.Bekle(() => RunDirector.Instance.Phase == RunPhase.Upgrading, 25f, "the shop did not open");
            for (int i = 0; i < 4; i++)
            {
                yield return null;
            }
        }

        private static void AssertFits(UpgradeScreen screen, UiRunTreePanel tree, string when)
        {
            RectTransform treeRect = tree.TreeRect;
            for (int r = 0; r < UiRunTreePanel.Rows; r++)
            {
                for (int t = 0; t < UiRunTreePanel.Tiers; t++)
                {
                    UiButton node = tree.NodeButton(r, t);
                    if (node != null)
                    {
                        Inside((RectTransform)node.transform, treeRect, when + " node " + r + "," + t);
                    }
                }
            }

            Inside((RectTransform)tree.BuyButton.transform, tree.DetailRect, when + " BUY");
            Inside((RectTransform)tree.PinButton.transform, tree.DetailRect, when + " PIN");
            Inside((RectTransform)tree.DetailName.transform, tree.DetailRect, when + " name");
            Assert.IsFalse(tree.DetailName.isTextTruncated, when + ": the node name is whole (" + tree.DetailName.text + ")");

            // The tree's host ends above the footer (CONTINUE).
            UiButton next = Named(screen, "Continue");
            ((RectTransform)next.transform).GetWorldCorners(s_a);
            treeRect.GetWorldCorners(s_b);
            Assert.GreaterOrEqual(s_b[0].y + 0.5f, s_a[1].y, when + ": the tree clears the footer");
        }

        private static void Inside(RectTransform child, RectTransform host, string what)
        {
            child.GetWorldCorners(s_a);
            host.GetWorldCorners(s_b);
            const float slack = 1f;
            Assert.IsTrue(s_a[0].x >= s_b[0].x - slack && s_a[0].y >= s_b[0].y - slack &&
                          s_a[2].x <= s_b[2].x + slack && s_a[2].y <= s_b[2].y + slack,
                what + " stays inside its host: " + s_a[0] + ".." + s_a[2] + " in " + s_b[0] + ".." + s_b[2]);
        }

        private static UiButton Named(Component root, string name)
        {
            foreach (UiButton b in root.GetComponentsInChildren<UiButton>(true))
            {
                if (b.gameObject.name == name) return b;
            }

            Assert.Fail("no button '" + name + "'");
            return null;
        }

        private static void Click(UiButton button)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        private static bool PressDebug(string label)
        {
            IReadOnlyList<DebugPanelEntry> entries = DebugPanelRegistry.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is DebugPanelButton button && button.Label != null && button.Label.Contains(label) && button.OnPressed != null)
                {
                    button.OnPressed.Invoke();
                    return true;
                }
            }

            return false;
        }

        private static void Spawning(bool on)
        {
            FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f);
            f.SetValue(EnemySpawnDirector.Instance, on);
        }
    }
}
