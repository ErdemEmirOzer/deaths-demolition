using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>Real shop/panel Move events in an owned reference fixture; no full UpgradeScreen, Continue or PauseScreen proof.</summary>
    public sealed class RunTreeNavigationRegressionTests
    {
        private const int RunId = 179;
        private readonly List<GameObject> _owned = new List<GameObject>();
        private PrototypeConfig _config;
        private GoldWallet _wallet;
        private WeaponModifierState _build;
        private RoundUpgradeShop _shop;
        private UiRunTreePanel _panel;
        private UiButton _footer;
        private GameObject _panelRoot;
        private CanvasGroup _group;
        private EventSystem _eventSystem;
        private GameObject _previousFocus;
        private ClassicWeaponId _previousWeapon;
        private string _settingsPath, _profileHash;
        private int _footerCalls;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-navigation-r3-" + Guid.NewGuid().ToString("N") + ".json");
            _footerCalls = 0;
            UiSettings.SetPathOverride(_settingsPath);
            UiSettings.EnsureLoaded();
            UiSettings.UiScale = 1.2f;
            UiSettings.TextScale = 1.25f;
            UiSettings.ReducedMotion = true;
            ProfileService.EnsureLoaded();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path);
            _profileHash = ProfileHash();
            _config = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(_config, "Use the shipped config without changing prices, limits or effects.");
            _wallet = new GoldWallet();
            _wallet.InitializeForRun(RunId);
            _wallet.Credit(50000, GoldSource.EnemyPickup, _wallet.NextTransactionId());
            _build = new WeaponModifierState();
            // The tree is built for the shop's weapon, but its tier nodes ask UpgradeCardCatalog.WeaponBranchGate,
            // which reads the global WeaponRegistry.SelectedWeapon. An earlier suite that equips another weapon
            // (RocketSliceTests: rl_1) would lock Piercing / FireRate as NotOnThisWeapon here, so the fixture
            // carries the same AR in both places and gives the selection back afterwards.
            _previousWeapon = WeaponRegistry.SelectedWeapon;
            if (_previousWeapon != ClassicWeaponId.AssaultRifle) Debug.Log("[RunTreeFixture] selected weapon was " + _previousWeapon + "; pinned to AssaultRifle for the AR shop");
            WeaponRegistry.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            _shop = new RoundUpgradeShop
            {
                UnlockedPredicate = _ => true,
                WeaponProvider = () => ClassicWeaponId.AssaultRifle,
                Random = new SeededUpgradeOfferRandom(77),
                NodeCardsOverride = true
            };
            bool hadEventSystem = Object.FindObjectOfType<EventSystem>() != null;
            _eventSystem = UiFactory.EnsureEventSystem();
            _previousFocus = _eventSystem.currentSelectedGameObject;
            if (!hadEventSystem) _owned.Add(_eventSystem.gameObject);
            if (Object.FindObjectsOfType<Camera>().Length == 0) _owned.Add(UiFactory.EnsureUiCamera().gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_shop != null) { _shop.OfferChanged -= Refresh; _shop.Close(); }
            WeaponRegistry.SelectedWeapon = _previousWeapon;
            if (_eventSystem != null) _eventSystem.SetSelectedGameObject(_previousFocus != null && _previousFocus.activeInHierarchy ? _previousFocus : null);
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            UiSettings.SetPathOverride(null);
            UiSettings.EnsureLoaded();
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            Assert.AreEqual(_profileHash, ProfileHash(), "Navigation must not write the assembly-isolated profile.");
        }

        private IEnumerator OpenPanel(string language, int round = 1)
        {
            UiSettings.Language = language;
            _shop.Open(round, _config, _wallet, _build, RunId);
            Assert.AreEqual(UpgradeOfferStatus.Ready, _shop.Status);
            // Existing MarkOwned and build tier are prerequisite fixture data, not purchase proof.
            _build.SetTier(UpgradeBranch.Piercing, 2);
            Assert.IsTrue(_shop.Tree.MarkOwned(Index("ks_piercing")));
            Canvas canvas = UiFactory.CreateCanvas("NavigationR3.OwnedCanvas", UiLayer.Screen);
            _owned.Add(canvas.gameObject);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1f;
            RectTransform host = UiFactory.NewRect("Host", canvas.transform);
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
            host.sizeDelta = new Vector2(1408f, 600f); // Fixed reference host; no Mac GameView or full-scene claim.
            Canvas.ForceUpdateCanvases();
            _panel = UiRunTreePanel.Create(host);
            _panelRoot = host.Find("RunTree").gameObject;
            _footer = UiFactory.Button("OwnedFooter", canvas.transform, UiStrings.Get("shop.continue", "CONTINUE"), UiButtonVariant.Secondary, () => _footerCalls++);
            _panel.ExitDown = _footer;
            _shop.OfferChanged += Refresh;
            Refresh();
            _panel.FocusNode(_panel.Snapshot.HearthIndex);
            yield return null;
        }

        private int Index(string id)
        {
            int index = _shop.Tree.Graph.IndexOf(id);
            Assert.GreaterOrEqual(index, 0, id);
            return index;
        }
        private void Refresh()
        {
            if (_panel == null) return;
            _panel.SessionId = _shop.SessionId;
            _panel.Revision = _shop.Revision;
            _panel.Refresh(_shop);
        }

        [UnityTest] public IEnumerator HearthLiveBuyDown_EN() { yield return HearthLiveBuyDown(UiStrings.English); }
        [UnityTest] public IEnumerator HearthLiveBuyDown_TR() { yield return HearthLiveBuyDown(UiStrings.Turkish); }
        [UnityTest] public IEnumerator TerminalOwnedRight_EN() { yield return TerminalOwnedRight(UiStrings.English); }
        [UnityTest] public IEnumerator TerminalOwnedRight_TR() { yield return TerminalOwnedRight(UiStrings.Turkish); }
        [UnityTest] public IEnumerator OwnedForkReforge_EN() { yield return OwnedForkReforge(UiStrings.English); }
        [UnityTest] public IEnumerator OwnedForkReforge_TR() { yield return OwnedForkReforge(UiStrings.Turkish); }
        [UnityTest] public IEnumerator TemporaryGroupAndReactivation_EN() { yield return TemporaryGroupAndReactivation(UiStrings.English); }
        [UnityTest] public IEnumerator TemporaryGroupAndReactivation_TR() { yield return TemporaryGroupAndReactivation(UiStrings.Turkish); }
        [UnityTest] public IEnumerator BrushControls_NoFillNoPlate_EN() { yield return BrushControls(UiStrings.English); }
        [UnityTest] public IEnumerator BrushControls_NoFillNoPlate_TR() { yield return BrushControls(UiStrings.Turkish); }

        private IEnumerator HearthLiveBuyDown(string language)
        {
            yield return OpenPanel(language);
            Assert.IsTrue(_panel.BuyLive);
            Assert.IsFalse(_panel.PinButton.IsInteractable());
            CommandState before = Capture();
            Submit(_panel.ButtonOf(_panel.Snapshot.HearthIndex));
            Selected(_panel.BuyButton);
            Move(MoveDirection.Down, _footer);
            Assert.IsTrue(_footer.IsInteractable());
            Submit(_footer);
            Assert.AreEqual(1, _footerCalls, "Only the owned fixture callback is invoked, not real Continue.");
            Unchanged(before);
        }

        private IEnumerator TerminalOwnedRight(string language)
        {
            yield return OpenPanel(language);
            int cap = Index("cap_ar_1");
            Assert.IsTrue(_shop.Tree.MarkOwned(cap)); // Owned-node arrangement, not a capstone purchase.
            Refresh();
            Assert.IsTrue(RunTreeEffects.IsLive(_shop.Tree.Graph[cap].EffectId));
            Assert.AreEqual(RunTreeNodeStatus.Purchased, _panel.Snapshot.Nodes[cap].Status);
            _panel.FocusNode(cap);
            UiButton capSeal = _panel.ButtonOf(cap);
            Assert.IsNotNull(capSeal);
            string capName = _panel.DetailName.text;
            CommandState before = Capture();
            Move(MoveDirection.Right, _footer);
            _panel.ExitDown = null;
            Refresh();
            _panel.FocusNode(cap);
            Move(MoveDirection.Right, capSeal);
            _panel.ExitDown = _footer;
            Refresh();
            _panel.FocusNode(_panel.Snapshot.HearthIndex);
            Move(MoveDirection.Right, capSeal); // Preserve the shipped nearest-node edge, not a direct Hearth-to-Buy route.
            Assert.AreEqual(cap, _panel.FocusIndex, "Actual ISelectHandler updates the detailed node after Move.");
            Assert.AreEqual(capName, _panel.DetailName.text);
            Unchanged(before);
        }

        private IEnumerator OwnedForkReforge(string language)
        {
            yield return OpenPanel(language, RunTreeState.ReforgeFromRound);
            int fork = Index("fork_ar_1_x");
            Assert.IsTrue(_shop.Tree.MarkOwned(fork)); // Owned fork fixture data; normal live effects and round eligibility remain authoritative.
            Refresh();
            Assert.AreEqual(RunTreeNodeStatus.Purchased, _panel.Snapshot.Nodes[fork].Status);
            Assert.IsTrue(RunTreeEffects.IsLive(_shop.Tree.Graph[fork].EffectId));
            Assert.Greater(_panel.Snapshot.Nodes[fork].ReforgeCost, 0);
            Assert.AreEqual(_shop.ReforgeCost(fork), _panel.Snapshot.Nodes[fork].ReforgeCost);
            _panel.FocusNode(fork);
            Assert.IsFalse(_panel.PinButton.IsInteractable());
            Assert.IsTrue(_panel.ReforgeButton.IsInteractable());
            CommandState before = Capture();
            UiInput.Focus(_panel.ReforgeButton);
            Selected(_panel.ReforgeButton);
            Move(MoveDirection.Left, _panel.ButtonOf(fork));
            Unchanged(before);
            Assert.IsTrue(_shop.Tree.IsOwned(fork));
            _wallet.DebugSet(0); // Gold fixture arrangement, not a transaction or purchase.
            Refresh();
            Assert.IsTrue(_panel.ReforgeButton.gameObject.activeInHierarchy);
            Assert.IsFalse(_panel.ReforgeButton.IsInteractable());
            Assert.IsTrue(_panel.BuyButton.gameObject.activeInHierarchy);
            Assert.IsFalse(_panel.BuyButton.IsInteractable());
            before = Capture();
            // Explicit retained-selection fixture: a previously selected control can become disabled.
            // This is not a claim that normal seal Submit selects disabled Buy.
            _eventSystem.SetSelectedGameObject(_panel.BuyButton.gameObject);
            Selected(_panel.BuyButton);
            Move(MoveDirection.Down, _footer);
            Assert.IsTrue(_footer.IsInteractable());
            Unchanged(before);
            Assert.IsTrue(_shop.Tree.IsOwned(fork));
        }

        private IEnumerator TemporaryGroupAndReactivation(string language)
        {
            yield return OpenPanel(language);
            Submit(_panel.ButtonOf(_panel.Snapshot.HearthIndex));
            Selected(_panel.BuyButton);
            CommandState before = Capture();
            _group.interactable = false;
            yield return null;
            Assert.IsFalse(_panel.BuyButton.IsInteractable());
            Assert.IsTrue(_panel.BuyButton.interactable, "The button's own eligibility survives the parent block.");
            Refresh(); // Build the real graph while the parent temporarily blocks input.
            Submit(_panel.BuyButton);
            Unchanged(before);
            _group.interactable = true;
            yield return null; // Resume without a second Refresh.
            Assert.IsTrue(_panel.BuyLive);
            Move(MoveDirection.Down, _footer);
            Unchanged(before);

            _panelRoot.SetActive(false);
            Refresh();
            yield return null;
            _panelRoot.SetActive(true);
            Refresh();
            yield return null;
            _panel.FocusNode(_panel.Snapshot.HearthIndex);
            Submit(_panel.ButtonOf(_panel.Snapshot.HearthIndex));
            Selected(_panel.BuyButton);
            Move(MoveDirection.Down, _footer);
            Unchanged(before);
        }

        /// <summary>
        /// WP-098B M3 (AD MUST M3; KILAVUZ §1 vermilion / gold reserves, C7, C11): BUY is the kit's unfilled C11 - no fill, no
        /// frame, no vermilion; live, it carries the Submit glyph. A focused seal draws no wash plate and no gold focus ring:
        /// its focus is the cream brackets alone. A dead BUY is the kit's disabled C11 - TextSecondary word, the hairline,
        /// no glyph - and its reason is a tooltip. Nothing in the panel is filled vermilion.
        /// </summary>
        private IEnumerator BrushControls(string language)
        {
            yield return OpenPanel(language);
            UiButton buy = _panel.BuyButton;
            Assert.IsTrue(_panel.BuyLive, "fixture: the Hearth can be stoked");
            Assert.AreEqual(UiButtonVariant.Brush, buy.Variant, "BUY = the kit's C11");
            Assert.IsFalse(buy.DrawsFocusRing, "BUY draws no focus ring of its own");
            NoSurface(buy, "live BUY");
            Assert.IsTrue(buy.GetComponentInChildren<UiPrompt>(true).gameObject.activeSelf, "live BUY carries the Submit glyph");

            UiButton seal = _panel.ButtonOf(_panel.Snapshot.HearthIndex);
            Selected(seal);
            yield return null;
            Assert.AreEqual(UiButtonVariant.Brush, seal.Variant, "a seal is a frameless control");
            Assert.IsFalse(seal.DrawsFocusRing, "no gold focus ring on a seal");
            NoSurface(seal, "focused seal");
            Assert.AreSame(seal.transform, UiFocusBrackets.Target, "focus = the kit's cream brackets");
            NoVermilionFill();

            _wallet.DebugSet(0); // Gold fixture arrangement, not a transaction or purchase.
            Refresh();
            _panel.FocusNode(_panel.Snapshot.HearthIndex);
            yield return null;
            Assert.IsFalse(_panel.BuyLive, "no gold: BUY cannot buy");
            Assert.IsTrue(buy.gameObject.activeInHierarchy, "a dead BUY stays on screen (kit C11 disabled)");
            Assert.IsTrue(buy.DisabledRuleVisible, "disabled = the kit's hairline");
            Assert.AreEqual(UiTokens.Get(UiColor.TextSecondary), buy.LabelText.color, "disabled word in TextSecondary");
            Assert.IsFalse(buy.GetComponentInChildren<UiPrompt>(true).gameObject.activeSelf, "no Submit glyph on a dead BUY");
            NoSurface(buy, "dead BUY");
            bool tooltip = false;
            foreach (MonoBehaviour c in buy.GetComponents<MonoBehaviour>()) tooltip |= c != null && c.GetType().Name == "TooltipSource";
            Assert.IsTrue(tooltip, "the reason is read in BUY's tooltip (kit C27)");
            NoVermilionFill();
        }

        /// <summary>No fill / border / wash drawn and no v3 focus ring or halo built on the control (its own children).</summary>
        private static void NoSurface(UiButton button, string what)
        {
            Assert.IsNull(button.transform.Find("FocusRing"), what + ": no v3 focus ring");
            Assert.IsNull(button.transform.Find("FocusHalo"), what + ": no v3 focus halo");
            foreach (string part in new[] { "Fill", "Border", "Wash" })
            {
                Transform t = button.transform.Find(part);
                Image image = t != null ? t.GetComponent<Image>() : null;
                bool drawn = image != null && image.enabled && image.gameObject.activeInHierarchy
                             && image.color.a * image.canvasRenderer.GetColor().a * image.canvasRenderer.GetAlpha() > 0.01f;
                Assert.IsFalse(drawn, what + ": its " + part + " is drawn");
            }
        }

        /// <summary>No active image in the panel draws AccentPrimary (the kit keeps vermilion for the C8 alone).</summary>
        private void NoVermilionFill()
        {
            Color accent = UiTokens.Get(UiColor.AccentPrimary);
            foreach (Image image in _panelRoot.GetComponentsInChildren<Image>(false))
            {
                if (!image.enabled) continue;
                Color c = image.color * image.canvasRenderer.GetColor();
                bool vermilion = Mathf.Abs(c.r - accent.r) < 0.03f && Mathf.Abs(c.g - accent.g) < 0.03f && Mathf.Abs(c.b - accent.b) < 0.03f;
                Assert.IsFalse(vermilion && c.a * image.canvasRenderer.GetAlpha() > 0.05f, "vermilion fill on " + image.name + " (" + image.transform.parent.name + ")");
            }
        }

        private void Move(MoveDirection direction, Selectable expected)
        {
            GameObject source = _eventSystem.currentSelectedGameObject;
            Assert.IsNotNull(source, "Deliver Move to the actually selected control.");
            Vector2 vector = direction == MoveDirection.Right ? Vector2.right : direction == MoveDirection.Left ? Vector2.left : Vector2.down;
            var data = new AxisEventData(_eventSystem) { moveDir = direction, moveVector = vector };
            Assert.IsTrue(ExecuteEvents.Execute(source, data, ExecuteEvents.moveHandler), "The real Move handler received the axis event.");
            Selected(expected);
            Assert.IsTrue(expected.IsInteractable(), "Move must land on a usable control, not an active disabled button.");
        }
        private void Selected(Selectable expected)
        {
            Assert.IsNotNull(expected);
            Assert.IsTrue(expected.gameObject.activeInHierarchy);
            Assert.AreSame(expected.gameObject, _eventSystem.currentSelectedGameObject);
        }
        private void Submit(UiButton button)
        {
            Assert.IsNotNull(button);
            Assert.IsTrue(ExecuteEvents.Execute(button.gameObject, new BaseEventData(_eventSystem), ExecuteEvents.submitHandler));
        }
        private struct CommandState
        {
            public int Gold, Revision, Session, Stacks, Count, Transactions, Pinned;
            public bool ReforgeUsed;
        }
        private CommandState Capture() => new CommandState
        {
            Gold = _wallet.Balance, Revision = _shop.Revision, Session = _shop.SessionId,
            Stacks = _shop.Tree.HearthStacks, Count = _shop.Tree.HearthThisRound,
            Transactions = _wallet.AppliedTransactions, Pinned = _shop.Tree.PinnedIndex,
            ReforgeUsed = _shop.Tree.ReforgeUsed
        };
        private void Unchanged(CommandState before)
        {
            Assert.AreEqual(before.Gold, _wallet.Balance);
            Assert.AreEqual(before.Revision, _shop.Revision);
            Assert.AreEqual(before.Session, _shop.SessionId);
            Assert.AreEqual(before.Stacks, _shop.Tree.HearthStacks);
            Assert.AreEqual(before.Count, _shop.Tree.HearthThisRound);
            Assert.AreEqual(before.Transactions, _wallet.AppliedTransactions);
            Assert.AreEqual(before.Pinned, _shop.Tree.PinnedIndex);
            Assert.AreEqual(before.ReforgeUsed, _shop.Tree.ReforgeUsed);
        }
        private static string ProfileHash()
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(TestProfileIsolation.TestProfilePath)));
        }
    }
}
