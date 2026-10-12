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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>RT9: real shop commands and panel input. No Run scene, PauseScreen or full-screen acceptance.</summary>
    public sealed class HearthLimitScreenTests
    {
        private const int RunId = 173;
        private readonly List<GameObject> _owned = new List<GameObject>();
        private PrototypeConfig _config;
        private GoldWallet _wallet;
        private WeaponModifierState _build;
        private RoundUpgradeShop _shop;
        private UiRunTreePanel _panel;
        private string _settingsPath;
        private string _profileHash;
        private bool _pauseHeld;
        private EventSystem _eventSystem;
        private GameObject _previousFocus;

        [SetUp]
        public void SetUp()
        {
            RoundUpgradeShop.TreeV2TestOverride = false;   // WP-104: these slices pin the v1 card shop (retired with the S-09 V2 screen)
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-rt9-ui-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiSettings.EnsureLoaded();
            UiSettings.UiScale = 1.2f;
            UiSettings.TextScale = 1.25f;
            UiSettings.ReducedMotion = true;
            ProfileService.EnsureLoaded();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path);
            _profileHash = ProfileHash();
            _pauseHeld = RunFlowClock.HasHold(RunFlowClock.PauseHold);
            _config = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(_config, "Use the shipped config without changing cost/cap/limit settings.");
            _wallet = new GoldWallet();
            _wallet.InitializeForRun(RunId);
            _wallet.Credit(50000, GoldSource.EnemyPickup, _wallet.NextTransactionId());
            _build = new WeaponModifierState();
            _shop = new RoundUpgradeShop
            {
                UnlockedPredicate = _ => true,
                WeaponProvider = () => ClassicWeaponId.AssaultRifle,
                Random = new SeededUpgradeOfferRandom(77),
                NodeCardsOverride = true
            };
            bool eventSystemExisted = Object.FindObjectOfType<EventSystem>() != null;
            _eventSystem = UiFactory.EnsureEventSystem();
            _previousFocus = _eventSystem.currentSelectedGameObject;
            if (!eventSystemExisted) _owned.Add(_eventSystem.gameObject);
            if (Object.FindObjectsOfType<Camera>().Length == 0) _owned.Add(UiFactory.EnsureUiCamera().gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            RoundUpgradeShop.TreeV2TestOverride = null;
            if (_shop != null) { _shop.OfferChanged -= Refresh; _shop.Close(); }
            if (_eventSystem != null) _eventSystem.SetSelectedGameObject(_previousFocus != null && _previousFocus.activeInHierarchy ? _previousFocus : null);
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            if (!_pauseHeld) RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            UiSettings.SetPathOverride(null);
            UiSettings.EnsureLoaded();
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            Assert.AreEqual(_profileHash, ProfileHash(), "The standalone shop/panel must not write the isolated profile.");
        }

        private IEnumerator OpenPanel(string language, bool ownKeystone = true)
        {
            UiSettings.Language = language;
            _shop.Open(1, _config, _wallet, _build, RunId);
            Assert.AreEqual(UpgradeOfferStatus.Ready, _shop.Status);
            if (ownKeystone) OwnPrerequisite();
            Canvas canvas = UiFactory.CreateCanvas("RT9.OwnedCanvas", UiLayer.Screen);
            _owned.Add(canvas.gameObject);
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1f;
            RectTransform host = UiFactory.NewRect("Host", canvas.transform);
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
            host.sizeDelta = new Vector2(1408f, 600f); // DIRECTION minimum reference-page width, no GameView mutation.
            Canvas.ForceUpdateCanvases();
            _panel = UiRunTreePanel.Create(host);
            _shop.OfferChanged += Refresh;
            Refresh();
            _panel.FocusNode(_panel.Snapshot.HearthIndex);
            yield return null;
        }

        private void OwnPrerequisite()
        {
            // Existing MarkOwned is prerequisite test-data arrangement, not purchase evidence.
            _build.SetTier(UpgradeBranch.Piercing, 2);
            int keystone = _shop.Tree.Graph.IndexOf("ks_piercing");
            Assert.GreaterOrEqual(keystone, 0);
            Assert.IsTrue(_shop.Tree.MarkOwned(keystone));
        }

        private void Refresh()
        {
            if (_panel == null) return;
            _panel.SessionId = _shop.SessionId;
            _panel.Revision = _shop.Revision;
            _panel.Refresh(_shop);
        }

        [UnityTest]
        public IEnumerator HearthLimit_EN_ThirdBuy_DisabledInput_Undo_Reopen_NextRound() { yield return ProveLimit(UiStrings.English); }

        [UnityTest]
        public IEnumerator HearthLimit_TR_ThirdBuy_DisabledInput_Undo_Reopen_NextRound() { yield return ProveLimit(UiStrings.Turkish); }

        private IEnumerator ProveLimit(string language)
        {
            yield return OpenPanel(language);
            Assert.AreEqual(3, _panel.Snapshot.HearthPerRound, "Verify the shipped allowance; do not replace it in the test.");
            for (int i = 0; i < _panel.Snapshot.HearthPerRound; i++)
            {
                Assert.IsTrue(_panel.BuyLive);
                int gold = _wallet.Balance, revision = _shop.Revision;
                int cost = _panel.Snapshot.Nodes[_panel.Snapshot.HearthIndex].Cost;
                Submit(_panel.BuyButton);
                Assert.AreEqual(gold - cost, _wallet.Balance, "Real UI Submit spends the existing command's quoted price.");
                Assert.AreEqual(revision + 1, _shop.Revision);
                Assert.AreEqual(i + 1, _panel.Snapshot.HearthThisRound);
            }
            AssertLimitRendering();
            int balance = _wallet.Balance, stacks = _shop.Tree.HearthStacks, rev = _shop.Revision, tx = _wallet.AppliedTransactions;
            Submit(_panel.BuyButton);
            Assert.IsTrue(ExecuteEvents.Execute(_panel.BuyButton.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler), "The disabled button received its actual pointer handler.");
            AssertUnchanged(balance, stacks, rev, tx);
            Assert.AreEqual(RunTreeCommandResult.LimitReached, _shop.TryStokeHearth(_shop.SessionId, _shop.Revision));
            AssertUnchanged(balance, stacks, rev, tx);

            // Accepted navigation + existing pause hold and same-run/round restore contract;
            // this does not call/fake PauseScreen or claim hardware/frozen-frame coverage.
            var navigation = new UiNavigationModel();
            Assert.AreEqual(UiNavigationResult.Changed, navigation.EnterBase(UiContext.GameplayHud));
            Assert.AreEqual(UiNavigationResult.Changed, navigation.EnterLayer(UiContext.RoundUpgrade));
            Assert.AreEqual(UiNavigationResult.Changed, navigation.EnterLayer(UiContext.Paused));
            RunFlowClock.AddHold(RunFlowClock.PauseHold);
            Assert.IsTrue(navigation.IsPaused && navigation.IsShopOpen && RunFlowClock.HasHold(RunFlowClock.PauseHold));
            int session = _shop.SessionId;
            _shop.Close();
            _shop.Open(1, _config, _wallet, _build, RunId);
            Assert.AreEqual(session, _shop.SessionId);
            AssertUnchanged(balance, stacks, rev, tx);
            Assert.AreEqual(3, _panel.Snapshot.HearthThisRound);
            Assert.IsFalse(_panel.BuyLive);
            Assert.AreEqual(UiNavigationResult.Changed, navigation.ExitOverlay(UiContext.Paused));
            if (!_pauseHeld) RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
            navigation.Clear();

            // The count remains visible if the next stoke is unaffordable as well as round-limited.
            _wallet.DebugSet(0); // wallet test-data arrangement, not a payment claim.
            Refresh();
            Assert.AreEqual(RunTreeNodeStatus.Unaffordable, _panel.Snapshot.Nodes[_panel.Snapshot.HearthIndex].Status);
            Assert.AreEqual(RoundSentence(), Note().text);
            AssertFits(Note());
            _wallet.DebugSet(balance);
            Refresh();
            int refund = _shop.UndoRefund;
            Assert.AreEqual(RunTreeCommandResult.Done, _shop.TryUndoLast(_shop.SessionId, _shop.Revision));
            Assert.AreEqual(balance + refund, _wallet.Balance);
            Assert.AreEqual(2, _panel.Snapshot.HearthThisRound);
            Assert.IsTrue(_panel.BuyLive, "Legal undo restores exactly one allowance.");
            Submit(_panel.BuyButton);
            AssertLimitRendering();
            _shop.Close();
            _shop.Open(2, _config, _wallet, _build, RunId);
            Assert.Greater(_shop.SessionId, session);
            Assert.AreEqual(0, _panel.Snapshot.HearthThisRound, "A real new shop round opens fresh allowance.");
            Assert.IsTrue(_panel.BuyLive);
            Assert.AreEqual(RoundSentence(), Note().text);
            AssertFits(Note());
            Debug.Log("[RT9-UI] " + language + " realThirdBuy=1 disabledInput=1 directLimitReached=1 undo3to2=1 sameRoundRestore=1 navigationPause=1 nextRound=1 profile=" + _profileHash);
        }

        [UnityTest]
        public IEnumerator CardPicksExhausted_HearthStillBuyable()
        {
            yield return OpenPanel(UiStrings.English);
            for (int i = 0; i < _shop.PurchaseLimit; i++)
            {
                int slot = -1;
                for (int c = 0; c < _shop.Cards.Count && slot < 0; c++) if (_shop.CanAfford(c)) slot = c;
                Assert.GreaterOrEqual(slot, 0);
                Assert.IsTrue(_shop.TryPurchase(slot, _shop.SessionId, _shop.Revision), "Consume the real card entitlement.");
            }
            Assert.IsTrue(_shop.IsLocked);
            _panel.FocusNode(_panel.Snapshot.HearthIndex);
            Assert.IsTrue(_panel.BuyLive, "Card lock must not lock the independent Hearth allowance.");
            Submit(_panel.BuyButton);
            Assert.AreEqual(1, _panel.Snapshot.HearthThisRound);
            Assert.AreEqual(_shop.PurchaseLimit, _shop.PurchasesThisRound);
        }

        private void AssertLimitRendering()
        {
            Assert.AreEqual(RunTreeNodeStatus.Available, _panel.Snapshot.Nodes[_panel.Snapshot.HearthIndex].Status, "Keep the authoritative seven-status contract.");
            Assert.IsFalse(_panel.BuyLive);
            Assert.IsFalse(_panel.BuyButton.GetComponentInChildren<UiPrompt>(true).gameObject.activeSelf);
            UiButton seal = _panel.ButtonOf(_panel.Snapshot.HearthIndex);
            Submit(seal);
            Assert.AreSame(seal.gameObject, EventSystem.current.currentSelectedGameObject, "Exhausted Hearth seal Submit must keep focus on the seal, not route into disabled Buy.");
            Assert.AreNotSame(_panel.BuyButton.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.AreEqual(RoundSentence(), _panel.DetailState.text);
            Assert.AreEqual(string.Empty, Note().text, "No duplicate counter or refusal message after a successful purchase.");
            Canvas.ForceUpdateCanvases();
            AssertFits(_panel.DetailState);
            // M-63: the tree's node row (full or compact) says the round is spent - never "AVAILABLE · price" - at UI 1.2 × text 1.25.
            TMP_Text word = _panel.StateTextOf(_panel.Snapshot.HearthIndex);
            Assert.IsNotNull(word, "The Hearth seal has its state word on the tree.");
            string spent = UiStrings.Get("tree.state.hearth_round").Replace("{value}", "3").Replace("{max}", "3");
            StringAssert.Contains(spent, word.text.Replace("\n", " "), "Node word: " + word.text);
            StringAssert.DoesNotContain(UiStrings.Get("tree.state.available"), word.text);
            AssertFits(word);
        }

        private TMP_Text Note()
        {
            Transform note = _panel.DetailState.transform.parent.Find("Note");
            Assert.IsNotNull(note, "The live detail state's Content parent also owns Note.");
            return note.GetComponent<TMP_Text>();
        }
        private string RoundSentence() => UiStrings.Format("tree.hearth.round", "value", _panel.Snapshot.HearthThisRound.ToString(), "max", _panel.Snapshot.HearthPerRound.ToString(), "");
        private static void Submit(UiButton button)
        {
            Assert.IsTrue(ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler), "The button received its actual Submit handler.");
        }
        private void AssertUnchanged(int gold, int stacks, int revision, int transactions)
        {
            Assert.AreEqual(gold, _wallet.Balance);
            Assert.AreEqual(stacks, _shop.Tree.HearthStacks);
            Assert.AreEqual(_panel.Snapshot.HearthPerRound, _shop.Tree.HearthThisRound);
            Assert.AreEqual(revision, _shop.Revision);
            Assert.AreEqual(transactions, _wallet.AppliedTransactions, "Refused input/command creates no gold transaction.");
        }
        private static void AssertFits(TMP_Text text)
        {
            text.ForceMeshUpdate();
            Assert.IsFalse(text.isTextTruncated, text.text);
            Assert.LessOrEqual(text.textBounds.size.x, text.rectTransform.rect.width + 1f);
            Assert.LessOrEqual(text.textBounds.size.y, text.rectTransform.rect.height + 1f);
            Debug.Log("[RT9-UI] text=" + text.name + " rectRefPx=" + text.rectTransform.rect.size + " meshRefPx=" + text.textBounds.size + " truncated=" + text.isTextTruncated);
        }
        private static string ProfileHash()
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(TestProfileIsolation.TestProfilePath)));
        }
    }
}
