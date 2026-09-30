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
    /// <summary>Owned reference panel and real event handlers; no native keyboard polling, full screen or hardware claim.</summary>
    public sealed class RunTreePinActionRegressionTests
    {
        private const int RunId = 181;
        private readonly List<GameObject> _owned = new List<GameObject>();
        private readonly List<string> _choices = new List<string>();
        private readonly List<string> _refusals = new List<string>();
        private PrototypeConfig _config;
        private GoldWallet _wallet;
        private WeaponModifierState _build;
        private RoundUpgradeShop _shop;
        private UiRunTreePanel _panel;
        private UiButton _footer;
        private CanvasGroup _group;
        private EventSystem _events;
        private GameObject _previousFocus;
        private ClassicWeaponId _previousWeapon;
        private string _settingsPath, _profileHash;
        private int _offerEvents, _changed;

        [SetUp]
        public void SetUp()
        {
            _settingsPath = Path.Combine(Path.GetTempPath(), "dd-pin-action-" + Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(_settingsPath);
            UiSettings.EnsureLoaded();
            UiSettings.UiScale = 1.2f;
            UiSettings.TextScale = 1.25f;
            UiSettings.ReducedMotion = true;
            ProfileService.EnsureLoaded();
            Assert.AreEqual(TestProfileIsolation.TestProfilePath, ProfileStore.Path);
            _profileHash = ProfileHash();
            PrototypeConfig shipped = Resources.Load<PrototypeConfig>(PrototypeConfig.ResourcesPath);
            Assert.IsNotNull(shipped);
            _config = Object.Instantiate(shipped); // Own config: the shipped resource remains immutable.
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
            _events = UiFactory.EnsureEventSystem();
            _previousFocus = _events.currentSelectedGameObject;
            if (!hadEventSystem) _owned.Add(_events.gameObject);
            if (Object.FindObjectsOfType<Camera>().Length == 0) _owned.Add(UiFactory.EnsureUiCamera().gameObject);
            _choices.Clear();
            _refusals.Clear();
            _offerEvents = _changed = 0;
            UiEvents.Raised += Choice;
        }

        [TearDown]
        public void TearDown()
        {
            UiEvents.Raised -= Choice;
            if (_shop != null) { _shop.OfferChanged -= Refresh; _shop.OfferChanged -= OfferChanged; _shop.Close(); }
            WeaponRegistry.SelectedWeapon = _previousWeapon;
            if (_events != null) _events.SetSelectedGameObject(_previousFocus != null && _previousFocus.activeInHierarchy ? _previousFocus : null);
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            if (_config != null) Object.DestroyImmediate(_config);
            UiSettings.SetPathOverride(null);
            UiSettings.EnsureLoaded();
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            Assert.AreEqual(_profileHash, ProfileHash(), "PIN actions must not write the isolated profile.");
        }

        private IEnumerator OpenPanel(string language)
        {
            UiSettings.Language = language;
            Open(1);
            Canvas canvas = UiFactory.CreateCanvas("PinAction.OwnedCanvas", UiLayer.Screen);
            _owned.Add(canvas.gameObject);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1f;
            RectTransform host = UiFactory.NewRect("Host", canvas.transform);
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 0.5f);
            host.sizeDelta = new Vector2(1408f, 600f);
            Canvas.ForceUpdateCanvases();
            _panel = UiRunTreePanel.Create(host);
            _footer = UiFactory.Button("OwnedFooter", canvas.transform, UiStrings.Get("shop.continue", "CONTINUE"), UiButtonVariant.Secondary, () => { });
            _panel.ExitDown = _footer;
            _panel.Changed = () => _changed++;
            _panel.Refused = message => _refusals.Add(message);
            _shop.OfferChanged += Refresh;
            _shop.OfferChanged += OfferChanged;
            Refresh();
            yield return null;
        }

        private void Open(int round)
        {
            _shop.Open(round, _config, _wallet, _build, RunId);
            Assert.AreEqual(UpgradeOfferStatus.Ready, _shop.Status);
            Refresh();
        }
        private void Refresh()
        {
            if (_panel == null) return;
            _panel.SessionId = _shop.SessionId;
            _panel.Revision = _shop.Revision;
            _panel.Refresh(_shop);
        }
        private void OfferChanged() => _offerEvents++;
        private void Choice(UiEvent evt, string detail)
        {
            if (evt == UiEvent.ChoiceSelected && (detail == "TreePin" || detail == "TreeUnpin")) _choices.Add(detail);
        }
        private int Index(string id)
        {
            int index = _shop.Tree.Graph.IndexOf(id);
            Assert.GreaterOrEqual(index, 0, id);
            return index;
        }
        private UiButton Focus(string id)
        {
            int index = Index(id);
            _panel.FocusNode(index);
            UiButton seal = _panel.ButtonOf(index);
            Assert.IsNotNull(seal, id);
            Assert.AreSame(seal.gameObject, _events.currentSelectedGameObject);
            return seal;
        }
        private void PinControl(bool allowed, bool unpin)
        {
            Assert.IsTrue(_panel.PinButton.gameObject.activeInHierarchy);
            Assert.AreEqual(allowed, _panel.PinButton.interactable, "Local eligibility is the intended command query.");
            string text = UiSettings.Language == UiStrings.Turkish
                ? (unpin ? "BIRAK" : "İŞARETLE") : (unpin ? "UNPIN" : "PIN");
            Assert.AreEqual(text, _panel.PinButton.LabelText.text);
        }
        /// <summary>M-34: UNPIN is the visible control's completed hold (the kit's test hooks drive the hold without a frame loop).</summary>
        private void HoldUnpin()
        {
            UiButton pin = _panel.PinButton;
            Assert.IsTrue(pin.IsHoldButton, "UNPIN is a hold, a single press never spends it (M-34).");
            pin.BeginHoldForTests(0f);
            Assert.AreEqual(UiHoldState.Completed, pin.TickHoldForTests(UiMotionTokens.HoldConfirm + 0.01f, true));
        }
        /// <summary>M-34: a refused PIN says why - the round's pin is used.</summary>
        private void RefusedAsUsed(int count)
        {
            Assert.AreEqual(count, _refusals.Count, "each refused PIN reports once");
            Assert.AreEqual(UiStrings.Get("tree.pin.used"), _refusals[_refusals.Count - 1]);
        }
        private void Submit(UiButton button)
        {
            Assert.IsTrue(ExecuteEvents.Execute(button.gameObject, new BaseEventData(_events), ExecuteEvents.submitHandler));
        }
        private void RightClick(UiButton seal)
        {
            Assert.IsNotNull(seal.GetComponent<UiTreeNodeInput>());
            Assert.IsTrue(ExecuteEvents.Execute(seal.gameObject,
                new PointerEventData(_events) { button = PointerEventData.InputButton.Right }, ExecuteEvents.pointerClickHandler));
        }
        private void MoveRight(Selectable target)
        {
            GameObject selected = _events.currentSelectedGameObject;
            Assert.IsNotNull(selected);
            Assert.IsTrue(ExecuteEvents.Execute(selected,
                new AxisEventData(_events) { moveDir = MoveDirection.Right, moveVector = Vector2.right }, ExecuteEvents.moveHandler));
            Assert.AreSame(target.gameObject, _events.currentSelectedGameObject);
            Assert.IsTrue(target.IsActive());
            Assert.IsTrue(target.IsInteractable());
        }

        [UnityTest] public IEnumerator QuotaAndReopen_EN() { yield return QuotaAndReopen(UiStrings.English); }
        [UnityTest] public IEnumerator QuotaAndReopen_TR() { yield return QuotaAndReopen(UiStrings.Turkish); }
        [UnityTest] public IEnumerator InvalidPinAndStaleSnapshot_EN() { yield return InvalidPinAndStaleSnapshot(UiStrings.English); }
        [UnityTest] public IEnumerator InvalidPinAndStaleSnapshot_TR() { yield return InvalidPinAndStaleSnapshot(UiStrings.Turkish); }
        [UnityTest] public IEnumerator GoldPrerequisiteAndPurchaseQuota_EN() { yield return GoldPrerequisiteAndPurchaseQuota(UiStrings.English); }
        [UnityTest] public IEnumerator GoldPrerequisiteAndPurchaseQuota_TR() { yield return GoldPrerequisiteAndPurchaseQuota(UiStrings.Turkish); }
        [UnityTest] public IEnumerator RejectedStatusesAndPartialTier_EN() { yield return RejectedStatusesAndPartialTier(UiStrings.English); }
        [UnityTest] public IEnumerator RejectedStatusesAndPartialTier_TR() { yield return RejectedStatusesAndPartialTier(UiStrings.Turkish); }
        [UnityTest] public IEnumerator ParentBlockAndStaleDisabledControl_EN() { yield return ParentBlockAndStaleDisabledControl(UiStrings.English); }
        [UnityTest] public IEnumerator ParentBlockAndStaleDisabledControl_TR() { yield return ParentBlockAndStaleDisabledControl(UiStrings.Turkish); }

        private IEnumerator QuotaAndReopen(string language)
        {
            yield return OpenPanel(language);
            Focus("ks_reload");
            Assert.IsTrue(_shop.CanSetPin("ks_reload"));
            PinControl(true, false);
            State before = Capture();
            _panel.HandleKey(KeyCode.P); // Actual shared dispatch handler, not native Input.GetKeyDown injection.
            Accepted(before, Index("ks_reload"), 1, "TreePin");

            UiButton cap = Focus("cap_ar_1");
            Assert.IsFalse(_shop.CanSetPin("cap_ar_1"));
            PinControl(false, false);
            Assert.IsFalse(_panel.BuyLive);
            before = Capture();
            MoveRight(_footer); // Terminal capstone skips the quota-disabled PIN.
            Submit(_panel.PinButton); // Delivered to the disabled handler: Denied, and M-34's reason.
            RightClick(cap);
            _panel.HandleKey(KeyCode.P);
            Unchanged(before);
            RefusedAsUsed(3); // M-34: button, right-click and P each report the used pin - none is swallowed silently

            Focus("ks_reload");
            PinControl(true, true);
            before = Capture();
            RightClick(_panel.ButtonOf(Index("ks_reload")));
            _panel.HandleKey(KeyCode.P);
            Unchanged(before); // M-34: right-click and P never UNPIN
            HoldUnpin();
            Accepted(before, -1, 1, "TreeUnpin");
            Focus("cap_ar_1");
            PinControl(false, false);
            _shop.Close();
            before = Capture();
            _panel.HandleKey(KeyCode.P);
            Unchanged(before);
            Open(1);
            Focus("cap_ar_1");
            Assert.AreEqual(1, _shop.PinChangesThisRound);
            PinControl(false, false);
            before = Capture();
            _panel.HandleKey(KeyCode.P);
            Unchanged(before);
            RefusedAsUsed(4);
            _shop.DiscardOffer();
            Open(1);
            Focus("cap_ar_1");
            Assert.AreEqual(1, _shop.PinChangesThisRound);
            PinControl(false, false);
            Open(2);
            Focus("cap_ar_1");
            Assert.AreEqual(0, _shop.PinChangesThisRound);
            PinControl(true, false);
            before = Capture();
            MoveRight(_panel.PinButton);
            Submit(_panel.PinButton);
            Accepted(before, Index("cap_ar_1"), 1, "TreePin");
        }

        private IEnumerator InvalidPinAndStaleSnapshot(string language)
        {
            yield return OpenPanel(language);
            Focus("fork_ar_1_y");
            Assert.IsTrue(_shop.SetPin("fork_ar_1_y")); // Real assignment consumes its normal allowance.
            Assert.IsTrue(_shop.Tree.MarkOwned(Index("fork_ar_1_x"))); // Disclosed exclusion fixture, not purchase proof.
            Refresh();
            Assert.AreEqual(RunTreeNodeStatus.Excluded, _panel.Snapshot.Nodes[Index("fork_ar_1_y")].Status);
            // M-35 (S-04): a pin whose target has closed is no pin. The snapshot hides it and the control offers PIN, passive:
            // the round's assignment is spent, not refunded - never an UNPIN for a marker the tree doesn't draw.
            Assert.IsFalse(_panel.Snapshot.Nodes[Index("fork_ar_1_y")].IsPinned);
            Focus("fork_ar_1_y");
            Assert.IsFalse(_shop.CanSetPin("fork_ar_1_y"));
            Assert.AreEqual(1, _shop.PinChangesThisRound);
            PinControl(false, false);
            State before = Capture();
            RightClick(_panel.ButtonOf(Index("fork_ar_1_y")));
            _panel.HandleKey(KeyCode.P);
            Unchanged(before); // The UI sends no command; the shop drops the closed pin at its own command point.
            RefusedAsUsed(2);  // M-34: right-click and P each say why

            Open(2);
            Assert.AreEqual(-1, _shop.Tree.PinnedIndex, "M-35: the new round's offer drops the closed pin.");
            Assert.AreEqual(0, _shop.PinChangesThisRound);
            Focus("cap_ar_1");
            Assert.IsFalse(_panel.Snapshot.Nodes[Index("cap_ar_1")].IsPinned);
            _shop.OfferChanged -= Refresh;
            try
            {
                Assert.IsTrue(_shop.SetPin("cap_ar_1"));
                Assert.IsFalse(_panel.Snapshot.Nodes[Index("cap_ar_1")].IsPinned, "Owned callback is withheld to arrange a genuinely stale snapshot.");
            }
            finally { _shop.OfferChanged += Refresh; }
            before = Capture();
            _panel.HandleKey(KeyCode.P);
            Unchanged(before); // M-34: P never UNPINs - even though the live pin (not the stale snapshot) says UNPIN
            PinControl(true, true);
            HoldUnpin();
            Accepted(before, -1, 1, "TreeUnpin"); // A snapshot-based command would incorrectly send same-ID and leave the pin.
            PinControl(false, false);
        }

        private IEnumerator GoldPrerequisiteAndPurchaseQuota(string language)
        {
            _config.Economy.PurchasesPerRound = 1; // Owned fixture limit proves purchase quota is independent; no resource edit.
            MetaRunModifiers meta = MetaRunModifiers.None;
            meta.CapstoneMasteryMet = false;
            _shop.Meta = meta;
            yield return OpenPanel(language);
            Assert.IsTrue(_wallet.TrySpend(_wallet.Balance));
            Refresh();
            UiButton tier = Focus("tier_MovementSpeed");
            Assert.AreEqual(RunTreeNodeStatus.Unaffordable, _panel.Snapshot.Nodes[Index("tier_MovementSpeed")].Status);
            Assert.IsTrue(_shop.CanSetPin("tier_MovementSpeed"));
            Assert.IsFalse(_panel.BuyLive);
            PinControl(true, false);
            State before = Capture();
            RightClick(tier);
            Accepted(before, Index("tier_MovementSpeed"), 1, "TreePin");

            Open(2);
            Focus("cap_ar_1");
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, _panel.Snapshot.Nodes[Index("cap_ar_1")].Status);
            Assert.IsTrue(_shop.CanSetPin("cap_ar_1"));
            Assert.IsFalse(_panel.BuyLive);
            PinControl(true, false);
            before = Capture();
            MoveRight(_panel.PinButton);
            Submit(_panel.PinButton);
            Accepted(before, Index("cap_ar_1"), 1, "TreePin");

            _wallet.Credit(50000, GoldSource.EnemyPickup, _wallet.NextTransactionId()); // Owned fixture funds for a real card purchase.
            Open(3);
            Assert.IsTrue(_shop.TryPurchase(0, _shop.SessionId, _shop.Revision));
            Assert.IsTrue(_shop.IsLocked);
            Assert.AreEqual(0, _shop.PinChangesThisRound);
            Focus("cap_ar_1");
            Assert.IsTrue(_shop.CanSetPin(null)); // Existing pin is clearable even with purchase quota used.
            PinControl(true, true);
            before = Capture();
            HoldUnpin();
            Accepted(before, -1, 0, "TreeUnpin");
            Focus("cap_ar_1");
            Assert.IsTrue(_shop.CanSetPin("cap_ar_1"));
            PinControl(true, false);
            before = Capture();
            _panel.HandleKey(KeyCode.P);
            Accepted(before, Index("cap_ar_1"), 1, "TreePin");
        }

        private IEnumerator RejectedStatusesAndPartialTier(string language)
        {
            yield return OpenPanel(language);
            Func<UpgradeBranch, int, UpgradeEligibility> eligibility = _shop.Tree.TierEligibility;
            foreach (UpgradeEligibility reason in new[] { UpgradeEligibility.Locked, UpgradeEligibility.NoLiveConsumer })
            {
                _shop.Tree.TierEligibility = (branch, owned) => branch == UpgradeBranch.FireRate ? reason : eligibility(branch, owned);
                Refresh();
                Denied("tier_FireRate");
            }
            _shop.Tree.TierEligibility = eligibility;
            _build.SetTier(UpgradeBranch.FireRate, _shop.Tree.Graph[Index("tier_FireRate")].MaxTier, _config);
            Assert.AreEqual(_shop.Tree.Graph[Index("tier_FireRate")].MaxTier, _shop.Tree.OwnedTier(Index("tier_FireRate"), _build));
            Refresh();
            Denied("tier_FireRate");
            _build.SetTier(UpgradeBranch.FireRate, 0, _config);

            string effect = _shop.Tree.Graph[Index("ks_reload")].EffectId;
            _shop.Tree.Graph[Index("ks_reload")].EffectId = "fixture.unwired";
            Refresh();
            Assert.AreEqual(RunTreeNodeStatus.Locked, _panel.Snapshot.Nodes[Index("ks_reload")].Status);
            Denied("ks_reload");
            _shop.Tree.Graph[Index("ks_reload")].EffectId = effect;

            int seal = _shop.Tree.Graph.WeaponSealIndex;
            effect = _shop.Tree.Graph[seal].EffectId;
            _shop.Tree.Graph[seal].EffectId = "fixture.unwired";
            Refresh();
            Assert.AreEqual(RunTreeNodeStatus.PrereqMissing, _panel.Snapshot.Nodes[Index("cap_ar_1")].Status);
            Denied("cap_ar_1"); // Destination is live but the future path is unreachable.
            _shop.Tree.Graph[seal].EffectId = effect;
            Refresh();
            Denied(_shop.Tree.Graph[_shop.Tree.Graph.LinkIIndex].Id);
            Denied(_shop.Tree.Graph[_shop.Tree.Graph.HearthIndex].Id);
            Assert.IsTrue(_shop.Tree.MarkOwned(_shop.Tree.Graph.PurseIndex)); // Owned-node fixture, not purchase proof.
            Refresh();
            Denied(_shop.Tree.Graph[_shop.Tree.Graph.PurseIndex].Id);
            Assert.IsTrue(_shop.Tree.MarkOwned(Index("fork_ar_1_x")));
            Refresh();
            Denied("fork_ar_1_y");

            _build.SetTier(UpgradeBranch.MovementSpeed, 1, _config);
            Assert.Greater(_shop.Tree.Graph[Index("tier_MovementSpeed")].MaxTier, 1);
            Assert.AreEqual(1, _shop.Tree.OwnedTier(Index("tier_MovementSpeed"), _build));
            Refresh();
            Focus("tier_MovementSpeed");
            PinControl(true, false);
            State before = Capture();
            _panel.HandleKey(KeyCode.P);
            Accepted(before, Index("tier_MovementSpeed"), 1, "TreePin");
        }

        private IEnumerator ParentBlockAndStaleDisabledControl(string language)
        {
            yield return OpenPanel(language);
            Assert.IsTrue(_shop.SetPin("ks_reload"));
            Focus("cap_ar_1");
            PinControl(false, false);
            _shop.Tree.SetPin(Index("cap_ar_1")); // Disclosed legacy primitive fixture; no event/redraw, allowance stays consumed.
            Assert.IsFalse(_panel.Snapshot.Nodes[Index("cap_ar_1")].IsPinned);
            _group.interactable = false;
            yield return null;
            State before = Capture();
            _panel.HandleKey(KeyCode.P);
            PinControl(true, true); // Fresh command recovers stale local disabled state without removing parent block.
            Assert.IsFalse(_panel.PinButton.IsInteractable());
            Assert.AreSame(_panel.PinButton, _panel.ButtonOf(Index("cap_ar_1")).navigation.selectOnRight);
            Submit(_panel.PinButton);
            RightClick(_panel.ButtonOf(Index("cap_ar_1")));
            Unchanged(before);
            _group.interactable = true;
            yield return null; // No second Refresh: the local navigation and eligibility survive the block.
            MoveRight(_panel.PinButton);
            before = Capture();
            HoldUnpin();
            Accepted(before, -1, 1, "TreeUnpin");
        }

        private void Denied(string id)
        {
            UiButton seal = Focus(id);
            Assert.IsFalse(_shop.CanSetPin(id), id);
            PinControl(false, false);
            State before = Capture();
            Submit(_panel.PinButton);
            RightClick(seal);
            _panel.HandleKey(KeyCode.P);
            Unchanged(before);
        }
        private struct State
        {
            public int Gold, Spent, Transactions, Revision, Session, Pin, Quota, Offers, Changes, Choices, Hearth, HearthCount, Purchases, Rerolls;
            public bool Reforge;
        }
        private State Capture() => new State
        {
            Gold = _wallet.Balance, Spent = _wallet.TotalSpent, Transactions = _wallet.AppliedTransactions,
            Revision = _shop.Revision, Session = _shop.SessionId, Pin = _shop.Tree.PinnedIndex, Quota = _shop.PinChangesThisRound,
            Offers = _offerEvents, Changes = _changed, Choices = _choices.Count,
            Hearth = _shop.Tree.HearthStacks, HearthCount = _shop.Tree.HearthThisRound,
            Purchases = _shop.PurchasesThisRound, Rerolls = _shop.RerollsThisRound, Reforge = _shop.Tree.ReforgeUsed
        };
        private void EconomicStateUnchanged(State before)
        {
            Assert.AreEqual(before.Gold, _wallet.Balance);
            Assert.AreEqual(before.Spent, _wallet.TotalSpent);
            Assert.AreEqual(before.Transactions, _wallet.AppliedTransactions);
            Assert.AreEqual(before.Revision, _shop.Revision);
            Assert.AreEqual(before.Session, _shop.SessionId);
            Assert.AreEqual(before.Hearth, _shop.Tree.HearthStacks);
            Assert.AreEqual(before.HearthCount, _shop.Tree.HearthThisRound);
            Assert.AreEqual(before.Purchases, _shop.PurchasesThisRound);
            Assert.AreEqual(before.Rerolls, _shop.RerollsThisRound);
            Assert.AreEqual(before.Reforge, _shop.Tree.ReforgeUsed);
        }
        private void Unchanged(State before)
        {
            EconomicStateUnchanged(before);
            Assert.AreEqual(before.Pin, _shop.Tree.PinnedIndex);
            Assert.AreEqual(before.Quota, _shop.PinChangesThisRound);
            Assert.AreEqual(before.Offers, _offerEvents);
            Assert.AreEqual(before.Changes, _changed);
            Assert.AreEqual(before.Choices, _choices.Count);
        }
        private void Accepted(State before, int pin, int quota, string choice)
        {
            EconomicStateUnchanged(before);
            Assert.AreEqual(pin, _shop.Tree.PinnedIndex);
            Assert.AreEqual(quota, _shop.PinChangesThisRound);
            Assert.AreEqual(before.Offers + 1, _offerEvents);
            Assert.AreEqual(before.Changes + 1, _changed);
            Assert.AreEqual(before.Choices + 1, _choices.Count);
            Assert.AreEqual(choice, _choices[_choices.Count - 1]);
        }
        private static string ProfileHash()
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(TestProfileIsolation.TestProfilePath)));
        }
    }
}
