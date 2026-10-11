using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Profile;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The round-end upgrade SESSION (WP-004 shop, completed by WP-008): three cards, at
    /// most one purchase, at most one paid reroll before it, or continue with nothing.
    ///
    /// RULES, AND WHERE EACH ONE LIVES
    ///   * exactly three distinct valid cards ... UpgradeOfferPolicy.TryBuildOffer (DEC-047)
    ///   * price never hides a card ............ eligibility ignores the wallet (decision 9);
    ///                                           the screen disables the button instead
    ///   * one purchase per round .............. PurchaseEntitlement (DEC-048 / REQ-078);
    ///                                           a purchase LOCKS the offer
    ///   * one paid reroll, before a purchase .. RerollEntitlement + TryReroll (DEC-050 /
    ///                                           REQ-079); one or two cards change
    ///   * rights survive reopen / pause /
    ///     reconnect / replayed commands ....... the offer is keyed to (run, round) and
    ///                                           kept across Close/Open of the same round;
    ///                                           a command quotes session + revision
    ///   * no valid offer -> no charge, no
    ///     fake card, safe error, CONTINUE ..... Status = ConfigurationError (decision 17)
    ///   * money moves last and once ........... every check precedes the spend; there is
    ///                                           no refund path (decision 18)
    ///   * unspent gold carries over ........... nothing here touches the balance except
    ///                                           the two spends
    ///
    /// A plain class, not a MonoBehaviour: <see cref="RunDirector"/> owns one, and a test
    /// builds its own with a throwaway wallet, an injected rank authority, an injected
    /// unlock predicate and a seeded <see cref="IUpgradeOfferRandom"/>.
    ///
    /// G-08 CONTRACT
    ///     shop.IsOpen / shop.Status        -> draw the screen, or the error notice
    ///     shop.Cards                       -> three UpgradeCardOffer, already priced
    ///     shop.CanAfford(i)                -> enable/disable the buy button
    ///     shop.TryPurchase(i, s, r)        -> the entire purchase
    ///     shop.RerollAvailability          -> enable/disable/label the reroll button
    ///     shop.TryReroll(s, r)             -> the entire reroll
    ///     RunDirector.Instance.CloseUpgradeScreen() -> the CONTINUE button
    /// Nothing else. The shop never draws and the screen never prices.
    /// </summary>
    public sealed class RoundUpgradeShop
    {
        private readonly List<UpgradeCardOffer> _cards = new List<UpgradeCardOffer>(UpgradeOfferPolicy.CardsPerOffer);
        private readonly List<UpgradeBranchSettings> _candidates = new List<UpgradeBranchSettings>(16);
        private readonly List<float> _weights = new List<float>(16);
        private readonly List<UpgradeBranchSettings> _picks = new List<UpgradeBranchSettings>(UpgradeOfferPolicy.CardsPerOffer);
        private readonly List<UpgradeBranchSettings> _current = new List<UpgradeBranchSettings>(UpgradeOfferPolicy.CardsPerOffer);
        private readonly List<int> _replacedSlots = new List<int>(UpgradeOfferPolicy.CardsPerOffer);

        // WP-077 K10: node cards, the last purchase (undo), the reroll's kept nodes.
        private readonly List<int> _nodePicks = new List<int>(UpgradeOfferPolicy.CardsPerOffer + 1);
        private readonly List<int> _nodeExclude = new List<int>(UpgradeOfferPolicy.CardsPerOffer * 2);
        private readonly List<int> _nodeSlots = new List<int>(UpgradeOfferPolicy.CardsPerOffer);
        private LastPurchase _last;
        private bool _undoClosed;
        private int _pinAllowanceRunId = -1, _pinAllowanceRound = -1;
        /// <summary>DEC-110: the target this round's assignment went to; pinning it again (after a clear) costs nothing.</summary>
        private int _pinAssignedIndex = -1;
        /// <summary>Successful pin assignments in this (run, round); clearing never refunds the one assignment.</summary>
        public int PinChangesThisRound { get; private set; }

        private struct LastPurchase
        {
            public bool Valid;
            public bool Hearth;
            public bool Tier;
            public bool WasPinned;
            public int Session;
            public int CardIndex;
            public int Node;
            public UpgradeBranch Branch;
            public int TransactionId;
            public int Cost;
        }

        private PrototypeConfig _config;
        private GoldWallet _wallet;
        private WeaponModifierState _injectedModifiers;
        private Func<UpgradeBranch, bool> _unlocked;
        private IUpgradeOfferRandom _random;
        private bool _purchasing;
        private bool _rerolling;

        // The (run, round) the current offer was built for. Reopening the same pair
        // restores the offer with its entitlements instead of rolling a free new one.
        private bool _offerBuilt;
        private int _offerRunId;
        private int _offerRound;

        // ------------------------------------------------------------------ state

        public bool IsOpen { get; private set; }

        /// <summary>The round that was just completed, i.e. the one this offer rewards.</summary>
        public int Round { get; private set; }

        /// <summary>Run the current offer belongs to. 0 while closed.</summary>
        public int RunId { get; private set; }

        /// <summary>
        /// Identity of the current OFFER. Raised when an offer is built for a new (run,
        /// round); kept when the same round's screen is reopened. A purchase quoting an
        /// older session is refused: a button from the previous round's screen, or a
        /// callback that arrived after the screen closed, cannot buy into this offer.
        /// </summary>
        public int SessionId { get; private set; }

        /// <summary>
        /// WP-008: revision of the current offer's CARDS. Rises on every reroll and every
        /// purchase. A command quoting an older revision was decided on cards that are no
        /// longer on the table and is refused.
        /// </summary>
        public int Revision { get; private set; }

        /// <summary>WP-008: ready, or the safe error state with no cards.</summary>
        public UpgradeOfferStatus Status { get; private set; } = UpgradeOfferStatus.Closed;

        /// <summary>WP-008: why <see cref="Status"/> is ConfigurationError. None when Ready.</summary>
        public UpgradeOfferFault Fault { get; private set; } = UpgradeOfferFault.None;

        /// <summary>WP-008 (DEC-048): the purchase right of this round. Used once <see cref="PurchasesThisRound"/> reaches <see cref="PurchaseLimit"/>.</summary>
        public UpgradeEntitlement PurchaseEntitlement { get; private set; } = UpgradeEntitlement.Available;

        /// <summary>WP-049C (DEC-087): cards the player may buy on THIS offer (config, clamped by <see cref="UpgradeOfferPolicy.PurchaseLimit"/>). Shipped 2; 1 was DEC-071.</summary>
        public int PurchaseLimit { get; private set; } = UpgradeOfferPolicy.MaxPurchasesPerRound;

        /// <summary>DEC-107: this offer carries the late-round bonus (a third card in R10–R13); the screen names it.</summary>
        public bool LateRoundBonus { get; private set; }

        /// <summary>WP-008 (DEC-050): the one paid reroll of this round. Locked once a purchase is made.</summary>
        public UpgradeEntitlement RerollEntitlement { get; private set; } = UpgradeEntitlement.Available;

        /// <summary>True once this round's purchase limit is reached: no further purchase, no reroll. CONTINUE only.</summary>
        public bool IsLocked => PurchaseEntitlement != UpgradeEntitlement.Available;

        /// <summary>Why the last <see cref="TryPurchase(int,int,int)"/> ended the way it did. F1 readout and the screen's footer.</summary>
        public UpgradePurchaseResult LastResult { get; private set; } = UpgradePurchaseResult.None;

        /// <summary>Why the last <see cref="TryReroll(int,int)"/> ended the way it did.</summary>
        public UpgradeRerollResult LastRerollResult { get; private set; } = UpgradeRerollResult.None;

        /// <summary>The three cards. Read-only by construction; buying goes through this class. Empty in the error state.</summary>
        public IReadOnlyList<UpgradeCardOffer> Cards => _cards;

        /// <summary>Spendable gold. Convenience so the screen does not need the wallet.</summary>
        public int Gold => _wallet != null ? _wallet.Balance : 0;

        /// <summary>Cards bought on THIS offer. 0..<see cref="PurchaseLimit"/>.</summary>
        public int PurchasesThisRound { get; private set; }

        /// <summary>Rerolls used on THIS offer. 0 or 1.</summary>
        public int RerollsThisRound { get; private set; }

        /// <summary>Gold spent on THIS offer: the purchase plus the reroll.</summary>
        public int GoldSpentThisRound { get; private set; }

        /// <summary>Price of the reroll, from the config. 0 without a config.</summary>
        public int RerollCost => _config == null ? 0
            : Meta.RerollCostOverride >= 0 ? Mathf.Min(Mathf.Max(0, _config.Economy.RerollCost), Meta.RerollCostOverride)
            : Mathf.Max(0, _config.Economy.RerollCost);

        /// <summary>WP-077 B2b: the meta PATH's run modifiers (RunDirector sets it at run start from the profile). None in tests.</summary>
        public MetaRunModifiers Meta { get; set; } = MetaRunModifiers.None;

        /// <summary>Slots the last reroll replaced (indices into <see cref="Cards"/>). Test and probe readout.</summary>
        public IReadOnlyList<int> LastRerollReplacedSlots => _replacedSlots;

        /// <summary>
        /// Debug only. Puts branches that no live system reads back into the draw, so the
        /// card layout can be tested with the full set. It changes NOTHING today: all ten
        /// branches are backed. See <see cref="UpgradeCardCatalog"/>.
        /// </summary>
        public bool OfferUnbackedBranches { get; set; }

        /// <summary>
        /// Randomness of the draw. Production uses <see cref="UnityUpgradeOfferRandom"/>;
        /// a test injects a <see cref="SeededUpgradeOfferRandom"/> so an offer and a reroll
        /// replay exactly.
        /// </summary>
        public IUpgradeOfferRandom Random
        {
            get { return _random ?? UnityUpgradeOfferRandom.Shared; }
            set { _random = value; }
        }

        /// <summary>
        /// "Is this content unlocked?" (decision 9 / DEC-049). Null means the profile rule:
        /// <see cref="ProfileService.IsBranchUnlocked"/> over UpgradeBranchSettings.UnlockProfileLevel.
        /// A test injects its own so no profile file is read.
        /// </summary>
        /// <summary>
        /// WP-077 (DEC-095): the run's in-run tree — built on the first offer of a run for the equipped
        /// weapon; Hearth, Purse, the pin (Nişan) and the S-09 snapshot read it. Tier ownership stays the build.
        /// </summary>
        public RunTreeState Tree { get; } = new RunTreeState();

        /// <summary>
        /// R1-B RT7 (C-00 30.09): builds this run's tree at the run start (RunDirector.StartRun), so nothing reads the previous
        /// run's tree before the first shop, and a Special picked up before the first shop keeps naming the links (the first
        /// Open no longer rebuilds a tree whose run id already matches).
        /// </summary>
        public void BeginRunTree(int runId, PrototypeConfig config)
        {
            Tree.Begin(Mathf.Max(0, runId), ShopWeapon, config, Meta);
            BeginTreeV2(runId, config);
        }

        /// <summary>WP-104 B1: the feature-based run tree (built only while <see cref="TreeV2Active"/>; the WP-077 tree above stays the shipped one until B2).</summary>
        public RunTreeV2 TreeV2 { get; } = new RunTreeV2();

        /// <summary>WP-104: the awakening unlocks (Ash) for the run; null = read from the profile when the V2 tree begins.</summary>
        public TreeAwakening? AwakeningOverride { get; set; }

        /// <summary>Tests: force the V2 tree on/off (null = config / dev flag).</summary>
        public static bool? TreeV2TestOverride { get; set; }

        /// <summary>WP-104: the V2 tree runs this run — config RunTreeV2, the dev flag -ddTreeV2, or a -ddTreePreset (dev builds only).</summary>
        public static bool TreeV2Active(PrototypeConfig config)
        {
            if (TreeV2TestOverride.HasValue) return TreeV2TestOverride.Value;
            if (config != null && config.Economy != null && config.Economy.RunTreeV2) return true;
            if (!TreePresetsV2.DevAllowed) return false;
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-ddTreeV2") >= 0 || TreePresetsV2.TryFromCommandLine(out _);
        }

        private void BeginTreeV2(int runId, PrototypeConfig config)
        {
            // WP-102: the run's mode is fixed by DifficultyService.BeginRun when the run scene loads (before StartRun); TreeMode = DifficultyMode.
            int mode = (int)DD.Prototype.Difficulty.DifficultyService.RunMode;
            TreeRuntime.Reset();
            TreeRuntime.SetMode(mode);
            TreeEconomyV2.ResetCarry();
            if (!TreeV2Active(config)) return;
            GoldWallet wallet = GoldService.Instance != null ? GoldService.Instance.Wallet : null;
            TreeAwakening awakening = AwakeningOverride ?? TreeAwakening.FromProfile(ProfileService.Current);
            TreeV2.Begin(Mathf.Max(0, runId), mode, wallet, RunTreeV2.ProfileOf(ShopWeapon), awakening);
            if (TreePresetsV2.TryFromCommandLine(out TreePresetsV2.Preset preset)) TreePresetsV2.Apply(TreeV2, preset);
        }

        /// <summary>WP-077: the equipped weapon the tree is built for. Null = WeaponRegistry.SelectedWeapon.</summary>
        public Func<ClassicWeaponId> WeaponProvider { get; set; }

        /// <summary>The shop's weapon: <see cref="WeaponProvider"/>, else the global selection. The tree and the branch gate both read it.</summary>
        private ClassicWeaponId ShopWeapon => WeaponProvider != null ? WeaponProvider() : WeaponRegistry.SelectedWeapon;

        /// <summary>WP-077: Purse bonus paid when THIS offer opened (0 without a Purse). Round-end readout.</summary>
        public int PurseBonusThisRound { get; private set; }

        public Func<UpgradeBranch, bool> UnlockedPredicate
        {
            get { return _unlocked; }
            set { _unlocked = value; }
        }

        // ------------------------------------------------------------------ events

        /// <summary>The offer was built, rerolled, locked or a card changed. G-08 redraws.</summary>
        public event Action OfferChanged;

        /// <summary>A card was bought. The director records the spend against the run stats.</summary>
        public event Action<UpgradeCardOffer> Purchased;

        /// <summary>WP-077 K10: the last card purchase was undone and refunded (the card as it was bought).</summary>
        public event Action<UpgradeCardOffer> PurchaseUndone;

        /// <summary>WP-077 K10: forces node cards on/off (tests, the F1 panel); null = the config's RunTreeNodeCards.</summary>
        public bool? NodeCardsOverride { get; set; }

        /// <summary>WP-077 K10: the offer sells in-run tree node cards instead of the classic branch cards.</summary>
        public bool NodeCardsActive => NodeCardsOverride ?? NodeCardsTestDefault ?? (_config != null && _config.Economy != null && _config.Economy.RunTreeNodeCards);

        /// <summary>
        /// Tests: a default for every shop (a PlayMode slice's director is rebuilt on each scene load). The classic-card
        /// slices pin it to false — they test the card row's contract (DEC-048 / DEC-050, the safe error state), which
        /// the tree screen replaces when RunTreeNodeCards ships. Null = the config. Reset per domain.
        /// </summary>
        public static bool? NodeCardsTestDefault { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetNodeCardsTestDefault() => NodeCardsTestDefault = null;

        /// <summary>WP-077 K10 "undo last purchase": this offer's last card / Hearth buy can be taken back in full (same screen, before CONTINUE).</summary>
        public bool UndoAvailable => IsOpen && Status == UpgradeOfferStatus.Ready && _last.Valid && _last.Session == SessionId && !_undoClosed;

        /// <summary>Gold the undo would give back (0 when none).</summary>
        public int UndoRefund => UndoAvailable ? _last.Cost : 0;

        /// <summary>The screen closes the undo window in the countdown's last 3 s (direction §2.5).</summary>
        public void CloseUndo()
        {
            if (_undoClosed)
            {
                return;
            }

            _undoClosed = true;
            OfferChanged?.Invoke();
        }

        /// <summary>Buy was pressed and refused for a reason the player caused. Argument is the card index.</summary>
        public event Action<int> PurchaseRefused;

        /// <summary>The offer was rerolled. Argument is the gold paid. The director records it against the run stats.</summary>
        public event Action<int> Rerolled;

        /// <summary>Reroll was pressed and refused for a reason the player caused.</summary>
        public event Action<UpgradeRerollResult> RerollRefused;

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens the screen for a round. Reads tiers from the armed player's weapon.</summary>
        public void Open(int round, PrototypeConfig config, GoldWallet wallet)
        {
            Open(round, config, wallet, null, 0);
        }

        /// <summary>
        /// Opens the screen for a round. The FIRST open of a (run, round) builds the offer;
        /// a later open of the same pair (the screen reconnecting, an F1 reopen) restores
        /// that offer with its purchase and reroll rights exactly as they were - never a
        /// free new offer (decision 12). <paramref name="modifiers"/> is the rank
        /// authority; null means the armed player's weapon, which is what the director
        /// passes. A run id of 0 (legacy callers) never retains an offer.
        /// </summary>
        public void Open(int round, PrototypeConfig config, GoldWallet wallet, WeaponModifierState modifiers, int runId)
        {
            _config = config;
            _wallet = wallet;
            _injectedModifiers = modifiers;

            int wantedRound = Mathf.Max(1, round);
            int wantedRun = Mathf.Max(0, runId);

            bool restore = _offerBuilt && wantedRun > 0 && _offerRunId == wantedRun && _offerRound == wantedRound;

            Round = wantedRound;
            RunId = wantedRun;
            if (_pinAllowanceRunId != wantedRun || _pinAllowanceRound != wantedRound)
            {
                _pinAllowanceRunId = wantedRun;
                _pinAllowanceRound = wantedRound;
                PinChangesThisRound = 0;
                _pinAssignedIndex = -1;
            }
            IsOpen = true;
            PurchaseLimit = UpgradeOfferPolicy.PurchaseLimit(config, wantedRound);
            LateRoundBonus = UpgradeOfferPolicy.IsLateRoundBonus(config, wantedRound);
            LastResult = UpgradePurchaseResult.None;
            LastRerollResult = UpgradeRerollResult.None;

            if (!restore)
            {
                SessionId++;
                Revision = 1;
                PurchasesThisRound = 0;
                RerollsThisRound = 0;
                GoldSpentThisRound = 0;
                PurchaseEntitlement = UpgradeEntitlement.Available;
                RerollEntitlement = UpgradeEntitlement.Available;
                _replacedSlots.Clear();
                _last = default;
                _undoClosed = false;

                // WP-077: a new run builds its tree; every new round-end pays the Purse and opens Hearth.
                if (!Tree.IsBuilt || Tree.RunId != wantedRun)
                {
                    Tree.Begin(wantedRun, ShopWeapon, config, Meta);
                }

                Tree.TierEligibility = TierEligibility;
                PurseBonusThisRound = Tree.OnRoundEnd(wallet);
                if (TreeV2.IsBuilt && TreeV2.RunId == wantedRun) TreeV2.Open(wantedRound);   // WP-104 B1: interest, strip, Hearth count
                DropClosedPin();

                BuildOffer();

                _offerBuilt = true;
                _offerRunId = wantedRun;
                _offerRound = wantedRound;
            }
            else
            {
                // Same round, same run: restore the status the offer was built with; the
                // cards may have been re-priced by an outside rank change while closed,
                // so re-read them. Rights and revision are untouched.
                Status = Fault == UpgradeOfferFault.None ? UpgradeOfferStatus.Ready : UpgradeOfferStatus.ConfigurationError;
                RefreshCards();
            }

            OfferChanged?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            Status = UpgradeOfferStatus.Closed;
            RunId = 0;
            _injectedModifiers = null;
            OfferChanged?.Invoke();
        }

        /// <summary>
        /// Forgets the retained offer so the next <see cref="Open"/> builds a fresh one
        /// even for the same (run, round). The director calls it at run boundaries; a
        /// test uses it between scenarios. Never called by the screen.
        /// </summary>
        public void DiscardOffer()
        {
            Close();
            _offerBuilt = false;
            _offerRunId = 0;
            _offerRound = 0;
            _cards.Clear();
            Fault = UpgradeOfferFault.None;
        }

        // ------------------------------------------------------------------ queries

        public bool IsPurchasable(int index)
        {
            if (!IsOpen || Status != UpgradeOfferStatus.Ready || IsLocked || index < 0 || index >= _cards.Count)
            {
                return false;
            }

            UpgradeCardOffer card = _cards[index];
            return card.IsValid && !card.Purchased && !card.IsMaxed;
        }

        public bool CanAfford(int index)
        {
            return IsPurchasable(index) && _wallet != null && _wallet.CanAfford(_cards[index].Cost);
        }

        /// <summary>The screen's reroll button state, computed from the same checks <see cref="TryReroll(int,int)"/> makes.</summary>
        public RerollAvailability RerollAvailability
        {
            get
            {
                if (!IsOpen || Status != UpgradeOfferStatus.Ready || RerollCost <= 0)
                {
                    return RerollAvailability.Unavailable;
                }

                // WP-049C: with a purchase limit above 1 the FIRST purchase locks the reroll (DEC-050)
                // while the purchase right is still Available, so the reroll entitlement is read first.
                if (RerollEntitlement == UpgradeEntitlement.Locked || PurchaseEntitlement != UpgradeEntitlement.Available)
                {
                    return RerollAvailability.LockedByPurchase;
                }

                if (RerollEntitlement != UpgradeEntitlement.Available)
                {
                    return RerollAvailability.Used;
                }

                // Same order as TryReroll (price before the pool), so the label and the
                // refusal reason never disagree. The pool count overwrites the scratch
                // lists; never read it between a reroll's compute and its commit.
                if (_wallet == null || !_wallet.CanAfford(RerollCost))
                {
                    return RerollAvailability.TooExpensive;
                }

                if (CountReplacementCandidates() == 0)
                {
                    return RerollAvailability.NoAlternatives;
                }

                return RerollAvailability.Available;
            }
        }

        public bool CanReroll => RerollAvailability == RerollAvailability.Available;

        /// <summary>Why a branch is or is not in the draw right now. F1 readout and tests.</summary>
        public UpgradeEligibility Eligibility(UpgradeBranch branch)
        {
            if (_config == null)
            {
                return UpgradeEligibility.InvalidSettings;
            }

            UpgradeBranchSettings settings = _config.Economy.Find(branch);
            WeaponModifierState modifiers = ModifierState();
            int owned = modifiers != null ? modifiers.GetTier(branch) : 0;
            return UpgradeOfferPolicy.Classify(settings, owned, HasLiveConsumer(branch), IsUnlocked(branch));
        }

        // ------------------------------------------------------------------ purchase

        /// <summary>Buys one card of the CURRENT session and revision. The F1 panel's path.</summary>
        public bool TryPurchase(int index)
        {
            return TryPurchase(index, SessionId, Revision);
        }

        /// <summary>Buys one card of the given session at the CURRENT revision. WP-004 callers and tests that do not track revisions.</summary>
        public bool TryPurchase(int index, int sessionId)
        {
            return TryPurchase(index, sessionId, Revision);
        }

        /// <summary>
        /// Buys one card. The whole purchase is here, in this order, and every check
        /// happens BEFORE the gold moves so no refusal can leave a half state:
        ///
        ///   1 session: the shop is open, the caller's session AND revision are the current
        ///     ones, the offer is not in its error state
        ///   2 entitlement: this round's one purchase is still available (DEC-048)
        ///   3 card: real, unbought, a rank authority exists, the live tier still matches
        ///     the card, the branch is below its ceiling
        ///   4 balance: the wallet can afford it
        ///   5 spend, exactly once, under its own transaction id
        ///   6 apply the tier, exactly once, and verify it moved
        ///   7 lock: mark the card, consume the entitlement, close the reroll, raise the
        ///     revision, tell the listeners
        /// </summary>
        public bool TryPurchase(int index, int sessionId, int revision) => TryPurchase(index, sessionId, revision, false);

        /// <summary>
        /// As above; <paramref name="alternative"/> takes a fork card's partner (the pair is one card, §2.5).
        /// </summary>
        public bool TryPurchase(int index, int sessionId, int revision, bool alternative)
        {
            if (_purchasing || _rerolling)
            {
                // Re-entrant call from a listener of our own events. Never legitimate.
                return false;
            }

            _purchasing = true;
            try
            {
                return PurchaseCore(index, sessionId, revision, alternative);
            }
            finally
            {
                _purchasing = false;
            }
        }

        private bool PurchaseCore(int index, int sessionId, int revision, bool alternative)
        {
            // ---- 1 session ---------------------------------------------------------
            if (!IsOpen)
            {
                return Refuse(index, UpgradePurchaseResult.ShopClosed);
            }

            if (sessionId != SessionId)
            {
                // A stale screen learns nothing about this offer, not even a card's state.
                return Refuse(index, UpgradePurchaseResult.SessionMismatch);
            }

            if (revision != Revision)
            {
                return Refuse(index, UpgradePurchaseResult.StaleRevision);
            }

            if (Status != UpgradeOfferStatus.Ready)
            {
                return Refuse(index, UpgradePurchaseResult.OfferUnavailable);
            }

            // ---- 2 entitlement -----------------------------------------------------
            if (PurchaseEntitlement != UpgradeEntitlement.Available || PurchasesThisRound >= PurchaseLimit)
            {
                return Refuse(index, UpgradePurchaseResult.PurchaseLimitReached);
            }

            // ---- 3 card ------------------------------------------------------------
            if (index < 0 || index >= _cards.Count || !_cards[index].IsValid)
            {
                return Refuse(index, UpgradePurchaseResult.InvalidCard);
            }

            UpgradeCardOffer card = _cards[index];

            if (card.Purchased)
            {
                return Refuse(index, UpgradePurchaseResult.AlreadyPurchased);
            }

            if (card.IsNode && card.NodeKind != RunTreeNodeKind.Tier)
            {
                return PurchaseNode(index, card, alternative);
            }

            WeaponModifierState modifiers = ModifierState();
            if (modifiers == null)
            {
                return Refuse(index, UpgradePurchaseResult.NoWeapon);
            }

            int liveTier = modifiers.GetTier(card.Branch);
            if (liveTier != card.OwnedTier)
            {
                // Something outside this screen moved the rank (F1). The card's price and
                // tier are stale; re-read everything, raise the revision (the cards changed)
                // and let the player look again.
                RefreshCards();
                Revision++;
                OfferChanged?.Invoke();
                return Refuse(index, UpgradePurchaseResult.StaleOffer);
            }

            if (card.IsMaxed || modifiers.IsMaxed(card.Branch, _config))
            {
                return Refuse(index, UpgradePurchaseResult.BranchMaxed);
            }

            // ---- 4 balance ---------------------------------------------------------
            if (_wallet == null || !_wallet.CanAfford(card.Cost))
            {
                return Refuse(index, UpgradePurchaseResult.InsufficientGold);
            }

            // ---- 5 spend -----------------------------------------------------------
            int transactionId = _wallet.NextTransactionId();
            GoldTransactionResult spend = _wallet.TrySpend(card.Cost, GoldSource.UpgradePurchase, transactionId);
            if (spend != GoldTransactionResult.Applied)
            {
                // CanAfford passed a moment ago, so only a closed wallet or a duplicate id
                // can land here; say which rather than blaming the balance.
                return Refuse(index, spend == GoldTransactionResult.InsufficientBalance
                    ? UpgradePurchaseResult.InsufficientGold
                    : UpgradePurchaseResult.WalletRefused);
            }

            // ---- 6 apply -----------------------------------------------------------
            // The single line the whole card system exists to call (G-03). The weapon
            // re-reads its modifiers on the next shot; there is no apply step.
            modifiers.AddTier(card.Branch, 1, _config);

            int appliedTier = modifiers.GetTier(card.Branch);
            if (appliedTier != liveTier + 1)
            {
                // Unreachable after step 3; kept as a loud assertion so a future ceiling
                // rule cannot turn into a silent charge. The gold is NOT refunded through
                // a credit (that would count as income) - the error is the signal.
                Debug.LogError("[Run] Upgrade tier did not move after a successful spend (" + card.Branch +
                               " live " + liveTier + " -> " + appliedTier + "). Transaction " + transactionId + ".");
                RefreshCards();
                Revision++;
                OfferChanged?.Invoke();
                return Refuse(index, UpgradePurchaseResult.TierDidNotChange);
            }

            // ---- 7 lock ------------------------------------------------------------
            card.Purchased = true;
            card.OwnedTier = appliedTier;
            card.TransactionId = transactionId;
            if (card.IsNode) card.BoughtNodeIndex = card.NodeIndex;
            _cards[index] = card;
            _last = new LastPurchase
            {
                Valid = true, Tier = true, Session = SessionId, CardIndex = index, Node = card.IsNode ? card.NodeIndex : -1,
                Branch = card.Branch, TransactionId = transactionId, Cost = card.Cost,
                WasPinned = card.IsNode && Tree.PinnedIndex == card.NodeIndex
            };

            return CommitPurchase(card);
        }

        /// <summary>
        /// WP-077 K10: a non-tier node card (keystone, fork, seal, capstone, link, Purse). The same session,
        /// entitlement and card checks ran above; the node must still be offerable (a partner bought this
        /// round closes a fork), then the gold moves once and the tree records the node.
        /// </summary>
        private bool PurchaseNode(int index, UpgradeCardOffer card, bool alternative)
        {
            WeaponModifierState modifiers = ModifierState();
            int target = alternative && card.AltNodeIndex >= 0 ? card.AltNodeIndex : card.NodeIndex;
            if (!Tree.IsBuilt || !Tree.IsOfferable(target, modifiers))
            {
                RefreshCards();
                Revision++;
                OfferChanged?.Invoke();
                return Refuse(index, UpgradePurchaseResult.StaleOffer);
            }

            int cost = Tree.Cost(target, modifiers);
            if (_wallet == null || !_wallet.CanAfford(cost))
            {
                return Refuse(index, UpgradePurchaseResult.InsufficientGold);
            }

            int transactionId = _wallet.NextTransactionId();
            GoldTransactionResult spend = _wallet.TrySpend(cost, GoldSource.UpgradePurchase, transactionId);
            if (spend != GoldTransactionResult.Applied)
            {
                return Refuse(index, spend == GoldTransactionResult.InsufficientBalance
                    ? UpgradePurchaseResult.InsufficientGold
                    : UpgradePurchaseResult.WalletRefused);
            }

            bool wasPinned = Tree.PinnedIndex == target;
            Tree.MarkOwned(target);

            card.Purchased = true;
            card.Cost = cost;
            card.OwnedTier = 1;
            card.TransactionId = transactionId;
            card.BoughtNodeIndex = target;
            _cards[index] = card;
            _last = new LastPurchase
            {
                Valid = true, Session = SessionId, CardIndex = index, Node = target, Branch = card.Branch,
                TransactionId = transactionId, Cost = cost, WasPinned = wasPinned
            };

            return CommitPurchase(card);
        }

        private bool CommitPurchase(UpgradeCardOffer card)
        {
            if (card.IsNode && card.BoughtNodeIndex >= 0 && Tree.IsBuilt)
            {
                RunTreeNode n = Tree.Graph[card.BoughtNodeIndex];
                RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.NodeBought, n.Id, Round, card.Cost, n.Lane, n.Kind));
            }

            PurchasesThisRound++;
            GoldSpentThisRound += card.Cost;
            if (PurchasesThisRound >= PurchaseLimit)
            {
                PurchaseEntitlement = UpgradeEntitlement.Used;
            }

            // DEC-050 is untouched by the limit: the reroll closes on the FIRST purchase.
            if (RerollEntitlement == UpgradeEntitlement.Available)
            {
                RerollEntitlement = UpgradeEntitlement.Locked;
            }

            Revision++;
            LastResult = UpgradePurchaseResult.Purchased;

            // Re-price the remaining cards: the build readout is stale and a maxed branch
            // must grey out immediately (they are locked anyway).
            RefreshCards();

            Purchased?.Invoke(card);
            RunAudioEvents.Raise(RunAudioEvent.UpgradePurchased);
            OfferChanged?.Invoke();
            return true;
        }

        private bool Refuse(int index, UpgradePurchaseResult result)
        {
            LastResult = result;

            // Only a refusal the player caused by pressing a live button is worth a sound
            // and a footer line; a stale or closed session is silently dropped input.
            if (result == UpgradePurchaseResult.InsufficientGold ||
                result == UpgradePurchaseResult.WalletRefused ||
                result == UpgradePurchaseResult.NoWeapon ||
                result == UpgradePurchaseResult.BranchMaxed ||
                result == UpgradePurchaseResult.StaleOffer ||
                result == UpgradePurchaseResult.TierDidNotChange ||
                result == UpgradePurchaseResult.PurchaseLimitReached)
            {
                PurchaseRefused?.Invoke(index);
                RunAudioEvents.Raise(RunAudioEvent.UpgradeRefused);
            }

            return false;
        }

        // ------------------------------------------------------------------ reroll

        /// <summary>Rerolls the CURRENT session and revision. The F1 panel's path.</summary>
        public bool TryReroll()
        {
            return TryReroll(SessionId, Revision);
        }

        /// <summary>
        /// The one paid reroll of this round (DEC-050 / REQ-079). Every check precedes the
        /// spend, and the new offer is computed and validated BEFORE the gold moves, so a
        /// reroll that cannot be produced costs nothing and keeps the right:
        ///
        ///   1 session: open, current session and revision, offer ready
        ///   2 entitlement: no purchase yet (a purchase locks the offer), reroll unused
        ///   3 price: a positive configured cost, and the wallet can afford it
        ///   4 compute: one or two replacements from the eligible branches not on the offer;
        ///     validated as three distinct cards with one or two slots changed
        ///   5 spend, exactly once, under its own transaction id
        ///   6 commit: swap the cards, consume the right, raise the revision, tell the listeners
        /// </summary>
        public bool TryReroll(int sessionId, int revision)
        {
            if (_purchasing || _rerolling)
            {
                return false;
            }

            _rerolling = true;
            try
            {
                return RerollCore(sessionId, revision);
            }
            finally
            {
                _rerolling = false;
            }
        }

        private bool RerollCore(int sessionId, int revision)
        {
            // ---- 1 session ---------------------------------------------------------
            if (!IsOpen)
            {
                return RefuseReroll(UpgradeRerollResult.ShopClosed);
            }

            if (sessionId != SessionId)
            {
                return RefuseReroll(UpgradeRerollResult.SessionMismatch);
            }

            if (revision != Revision)
            {
                return RefuseReroll(UpgradeRerollResult.StaleRevision);
            }

            if (Status != UpgradeOfferStatus.Ready || _config == null)
            {
                return RefuseReroll(UpgradeRerollResult.OfferUnavailable);
            }

            // ---- 2 entitlement -----------------------------------------------------
            if (RerollEntitlement == UpgradeEntitlement.Locked || PurchaseEntitlement != UpgradeEntitlement.Available)
            {
                return RefuseReroll(UpgradeRerollResult.OfferLocked);
            }

            if (RerollEntitlement != UpgradeEntitlement.Available)
            {
                return RefuseReroll(UpgradeRerollResult.RerollUsed);
            }

            // ---- 3 price -----------------------------------------------------------
            int cost = RerollCost;
            if (cost <= 0)
            {
                Debug.LogError("[Run] Upgrade offer configuration error: EconomyConfigSection.RerollCost must be positive (is " +
                               cost + "). The reroll is refused; nothing was charged.");
                return RefuseReroll(UpgradeRerollResult.InvalidCost);
            }

            if (_wallet == null || !_wallet.CanAfford(cost))
            {
                return RefuseReroll(UpgradeRerollResult.InsufficientGold);
            }

            if (_cards.Count > 0 && _cards[0].IsNode)
            {
                return RerollNodes(cost);
            }

            // ---- 4 compute ---------------------------------------------------------
            _current.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                _current.Add(_config.Economy.Find(_cards[i].Branch));
            }

            CollectReplacementCandidates();

            UpgradeOfferFault fault = UpgradeOfferPolicy.TryReroll(_current, _candidates, _weights, Random, _picks, _replacedSlots, PinnedTierSlot());
            if (fault == UpgradeOfferFault.NoReplacementAvailable)
            {
                Debug.LogWarning("[Run] Upgrade offer: no eligible branch outside the current offer to reroll in (round " + Round +
                                 "). The reroll is refused; nothing was charged and the reroll right is kept.");
                return RefuseReroll(UpgradeRerollResult.NoReplacementAvailable);
            }

            if (fault != UpgradeOfferFault.None)
            {
                Debug.LogError("[Run] Upgrade offer configuration error on reroll: " + fault +
                               " (round " + Round + "). The reroll is refused; nothing was charged and the reroll right is kept.");
                return RefuseReroll(UpgradeRerollResult.InvalidResult);
            }

            // ---- 5 spend -----------------------------------------------------------
            int transactionId = _wallet.NextTransactionId();
            GoldTransactionResult spend = _wallet.TrySpend(cost, GoldSource.OfferReroll, transactionId);
            if (spend != GoldTransactionResult.Applied)
            {
                _replacedSlots.Clear();
                return RefuseReroll(spend == GoldTransactionResult.InsufficientBalance
                    ? UpgradeRerollResult.InsufficientGold
                    : UpgradeRerollResult.WalletRefused);
            }

            // ---- 6 commit ----------------------------------------------------------
            WeaponModifierState modifiers = ModifierState();
            EconomyConfigSection economy = _config.Economy;
            for (int i = 0; i < _replacedSlots.Count; i++)
            {
                int slot = _replacedSlots[i];
                _cards[slot] = BuildCard(_picks[slot], modifiers, economy);
            }

            RerollsThisRound++;
            GoldSpentThisRound += cost;
            RerollEntitlement = UpgradeEntitlement.Used;
            Revision++;
            LastRerollResult = UpgradeRerollResult.Rerolled;

            RefreshCards();

            Rerolled?.Invoke(cost);
            RunAudioEvents.Raise(RunAudioEvent.UpgradePurchased);
            OfferChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// WP-077 K10 node-offer reroll: every card except the Nişan's step is redrawn from the frontier left
        /// once the current cards (and their fork partners) are taken out. Same guarantees as the classic
        /// reroll: computed before the spend, at least one card changes, nothing charged otherwise.
        /// </summary>
        private bool RerollNodes(int cost)
        {
            WeaponModifierState modifiers = ModifierState();
            int pinStep = Tree.PinnedOfferable(modifiers);
            _nodeExclude.Clear();
            _nodeSlots.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                UpgradeCardOffer c = _cards[i];
                _nodeExclude.Add(c.NodeIndex);
                if (c.AltNodeIndex >= 0) _nodeExclude.Add(c.AltNodeIndex);
                if (c.NodeIndex != pinStep && c.AltNodeIndex != pinStep) _nodeSlots.Add(i);
            }

            Tree.DrawFrontier(Random, modifiers, _nodeSlots.Count, _nodePicks, _nodeExclude);
            if (_nodePicks.Count == 0)
            {
                Debug.LogWarning("[Run] Upgrade offer: no tree node outside the current offer to reroll in (round " + Round +
                                 "). The reroll is refused; nothing was charged and the reroll right is kept.");
                return RefuseReroll(UpgradeRerollResult.NoReplacementAvailable);
            }

            int transactionId = _wallet.NextTransactionId();
            GoldTransactionResult spend = _wallet.TrySpend(cost, GoldSource.OfferReroll, transactionId);
            if (spend != GoldTransactionResult.Applied)
            {
                return RefuseReroll(spend == GoldTransactionResult.InsufficientBalance
                    ? UpgradeRerollResult.InsufficientGold
                    : UpgradeRerollResult.WalletRefused);
            }

            EconomyConfigSection economy = _config.Economy;
            _replacedSlots.Clear();
            for (int k = 0; k < _nodePicks.Count && k < _nodeSlots.Count; k++)
            {
                _cards[_nodeSlots[k]] = BuildNodeCard(_nodePicks[k], modifiers, economy);
                _replacedSlots.Add(_nodeSlots[k]);
            }

            RaiseOfferDrawn(true);
            RerollsThisRound++;
            GoldSpentThisRound += cost;
            RerollEntitlement = UpgradeEntitlement.Used;
            Revision++;
            LastRerollResult = UpgradeRerollResult.Rerolled;
            RefreshCards();
            Rerolled?.Invoke(cost);
            RunAudioEvents.Raise(RunAudioEvent.UpgradePurchased);
            OfferChanged?.Invoke();
            return true;
        }

        private bool RefuseReroll(UpgradeRerollResult result)
        {
            LastRerollResult = result;

            if (result == UpgradeRerollResult.InsufficientGold ||
                result == UpgradeRerollResult.OfferLocked ||
                result == UpgradeRerollResult.RerollUsed ||
                result == UpgradeRerollResult.NoReplacementAvailable ||
                result == UpgradeRerollResult.InvalidResult ||
                result == UpgradeRerollResult.InvalidCost ||
                result == UpgradeRerollResult.WalletRefused)
            {
                RerollRefused?.Invoke(result);
                RunAudioEvents.Raise(RunAudioEvent.UpgradeRefused);
            }

            return false;
        }

        // ------------------------------------------------------------------ offer

        private void BuildOffer()
        {
            _cards.Clear();
            _replacedSlots.Clear();
            Fault = UpgradeOfferFault.None;

            if (_config == null)
            {
                Fail(UpgradeOfferFault.NoConfig);
                return;
            }

            EconomyConfigSection economy = _config.Economy;
            if (economy == null || economy.Branches == null || economy.Branches.Count == 0)
            {
                Fail(UpgradeOfferFault.NoUpgradeTable);
                return;
            }

            WeaponModifierState modifiers = ModifierState();
            if (NodeCardsActive && Tree.IsBuilt)
            {
                BuildNodeOffer(modifiers, economy);
                return;
            }

            CollectCandidates(modifiers, null);

            // A table that COULD offer three cards but whose remaining branches are all at
            // their ceiling on this build is the run outgrowing the table, not a broken
            // table. Same safe state, softer report.
            bool exhausted = _candidates.Count < UpgradeOfferPolicy.CardsPerOffer &&
                             _candidates.Count + CountMaxed(modifiers) >= UpgradeOfferPolicy.CardsPerOffer;

            UpgradeOfferFault fault = UpgradeOfferPolicy.TryBuildOffer(_candidates, _weights, Random, _picks);
            if (fault != UpgradeOfferFault.None)
            {
                Fail(fault == UpgradeOfferFault.NotEnoughEligibleBranches && exhausted ? UpgradeOfferFault.PoolExhausted : fault);
                return;
            }

            ApplyPinGuarantee(modifiers, economy);

            for (int i = 0; i < _picks.Count; i++)
            {
                _cards.Add(BuildCard(_picks[i], modifiers, economy));
            }

            Status = UpgradeOfferStatus.Ready;
        }

        /// <summary>
        /// WP-077 K10 node offer (K1 hybrid): up to three frontier nodes by catalog weight with the luck guard
        /// and the Nişan's step (RunTreeState.DrawFrontier), a fork pair as one card. An empty frontier is
        /// PoolExhausted only when the Hearth is full too (§2.5); otherwise the screen offers the Hearth alone.
        /// </summary>
        private void BuildNodeOffer(WeaponModifierState modifiers, EconomyConfigSection economy)
        {
            Tree.TierEligibility = TierEligibility;
            Tree.DrawFrontier(Random, modifiers, UpgradeOfferPolicy.CardsPerOffer, _nodePicks);
            RaiseOfferDrawn(false);
            Tree.RecordOffer(_nodePicks, modifiers);
            if (_nodePicks.Count == 0)
            {
                int h = Tree.Graph.HearthIndex;
                if (h < 0 || Tree.HearthStacks >= Tree.Graph[h].MaxTier)
                {
                    Fail(UpgradeOfferFault.PoolExhausted);
                    return;
                }
            }

            for (int k = 0; k < _nodePicks.Count; k++)
            {
                _cards.Add(BuildNodeCard(_nodePicks[k], modifiers, economy));
            }

            Status = UpgradeOfferStatus.Ready;
        }

        private UpgradeCardOffer BuildNodeCard(int i, WeaponModifierState modifiers, EconomyConfigSection economy)
        {
            RunTreeNode n = Tree.Graph[i];
            UpgradeCardOffer card;
            UpgradeBranchSettings settings = n.Kind == RunTreeNodeKind.Tier && economy != null ? economy.Find(n.Branch) : null;
            if (settings != null)
            {
                card = BuildCard(settings, modifiers, economy);
            }
            else
            {
                card = new UpgradeCardOffer
                {
                    Branch = n.HasBranch ? n.Branch : default,
                    DisplayName = n.DisplayName,
                    Description = n.Description,
                    Kind = n.Lane == RunTreeLane.Weapon ? UpgradeCardKind.WeaponCore : UpgradeCardKind.Utility,
                    OwnedTier = 0,
                    NextTier = 1,
                    MaxTier = 1,
                    Cost = Tree.Cost(i, modifiers),
                    IsValid = true
                };
            }

            int partner = Tree.ForkPartner(i);
            card.IsNode = true;
            card.NodeIndex = i;
            card.NodeId = n.Id;
            card.NodeKind = n.Kind;
            card.StringKey = Tree.StringKeyOf(i);
            card.AltNodeIndex = partner >= 0 && Tree.IsOfferable(partner, modifiers) ? partner : -1;
            card.BoughtNodeIndex = -1;
            return card;
        }

        /// <summary>
        /// Decision 17: the safe error state. No card is invented, nothing is charged, the
        /// screen shows a finished sentence and CONTINUE works. Reported as an error so a
        /// misconfigured table is never silent.
        /// </summary>
        private void Fail(UpgradeOfferFault fault)
        {
            _cards.Clear();
            Fault = fault;
            Status = UpgradeOfferStatus.ConfigurationError;

            if (fault == UpgradeOfferFault.PoolExhausted)
            {
                Debug.LogWarning("[Run] Upgrade offer: every remaining branch is at its ceiling (run " + RunId + ", round " + Round +
                                 "). No card is offered this round; nothing was charged; CONTINUE proceeds normally.");
                return;
            }

            Debug.LogError("[Run] Upgrade offer configuration error: " + fault + " (run " + RunId + ", round " + Round +
                           "). No card is offered this round; nothing was charged; CONTINUE proceeds normally.");
        }

        /// <summary>Branches that would be eligible were they not at their ceiling on the live build.</summary>
        private int CountMaxed(WeaponModifierState modifiers)
        {
            int maxed = 0;
            List<UpgradeBranchSettings> branches = _config.Economy.Branches;
            for (int i = 0; i < branches.Count; i++)
            {
                UpgradeBranchSettings settings = branches[i];
                if (settings == null)
                {
                    continue;
                }

                int owned = modifiers != null ? modifiers.GetTier(settings.Branch) : 0;
                if (UpgradeOfferPolicy.Classify(settings, owned, HasLiveConsumer(settings.Branch), IsUnlocked(settings.Branch)) ==
                    UpgradeEligibility.Maxed)
                {
                    maxed++;
                }
            }

            return maxed;
        }

        /// <summary>
        /// Fills the scratch candidate/weight lists with every eligible branch, skipping
        /// the ones in <paramref name="exclude"/> (the current offer, for a reroll).
        /// </summary>
        private void CollectCandidates(WeaponModifierState modifiers, List<UpgradeBranchSettings> exclude)
        {
            _candidates.Clear();
            _weights.Clear();

            List<UpgradeBranchSettings> branches = _config.Economy.Branches;
            for (int i = 0; i < branches.Count; i++)
            {
                UpgradeBranchSettings settings = branches[i];
                if (settings == null)
                {
                    continue;
                }

                if (exclude != null && UpgradeOfferPolicy.IndexOfBranch(exclude, settings.Branch) >= 0)
                {
                    continue;
                }

                int owned = modifiers != null ? modifiers.GetTier(settings.Branch) : 0;
                if (UpgradeOfferPolicy.Classify(settings, owned, HasLiveConsumer(settings.Branch), IsUnlocked(settings.Branch)) !=
                    UpgradeEligibility.Eligible)
                {
                    continue;
                }

                _candidates.Add(settings);
                _weights.Add(Mathf.Max(0.0001f, settings.OfferWeight));
            }
        }

        private void CollectReplacementCandidates()
        {
            CollectCandidates(ModifierState(), _current);
        }

        private int CountReplacementCandidates()
        {
            if (_config == null)
            {
                return 0;
            }

            if (_cards.Count > 0 && _cards[0].IsNode)
            {
                // K10: frontier nodes not already on the offer (no draw, so the random stream is untouched).
                WeaponModifierState m = ModifierState();
                int n = 0;
                for (int i = 0; Tree.IsBuilt && i < Tree.Graph.Count; i++)
                {
                    if (Tree.IsOfferable(i, m) && !IsNodeOnOffer(i)) n++;
                }

                return n;
            }

            _current.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                _current.Add(_config.Economy.Find(_cards[i].Branch));
            }

            CollectReplacementCandidates();
            return _candidates.Count;
        }

        /// <summary>WP-077 K10: a node is on the current offer (as a card or as a fork card's partner).</summary>
        public bool IsNodeOnOffer(int nodeIndex)
        {
            for (int c = 0; c < _cards.Count; c++)
            {
                if (_cards[c].IsValid && !_cards[c].Purchased && _cards[c].IsNode && (_cards[c].NodeIndex == nodeIndex || _cards[c].AltNodeIndex == nodeIndex)) return true;
            }

            return false;
        }

        /// <summary>WP-077: the live build the tree reads (tier ownership, Hearth). Null without a weapon.</summary>
        internal WeaponModifierState BuildForTree => ModifierState();

        /// <summary>
        /// WP-077 Hearth ("Ocak", K3): buy one stack directly — outside the card limit (DEC-087), ≤ 3 per
        /// round. Same session/revision checks as a card; raises OfferChanged so the screen redraws.
        /// </summary>
        public RunTreeCommandResult TryStokeHearth(int sessionId, int revision)
        {
            if (!IsOpen || sessionId != SessionId || revision != Revision || Status != UpgradeOfferStatus.Ready)
            {
                return RunTreeCommandResult.NotAvailable;
            }

            int before = _wallet != null ? _wallet.Balance : 0;
            RunTreeCommandResult result = Tree.TryStokeHearth(_wallet, ModifierState(), out int transactionId);
            if (result != RunTreeCommandResult.Done)
            {
                return result;
            }

            int cost = before - _wallet.Balance;
            GoldSpentThisRound += cost;
            _last = new LastPurchase { Valid = true, Hearth = true, Session = SessionId, CardIndex = -1, Node = Tree.Graph.HearthIndex, TransactionId = transactionId, Cost = cost };
            RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.HearthStoked, Tree.Graph[Tree.Graph.HearthIndex].Id, Round, cost,
                RunTreeLane.Weapon, RunTreeNodeKind.Hearth, Tree.HearthStacks));
            Revision++;
            RefreshCards();
            RunAudioEvents.Raise(RunAudioEvent.UpgradePurchased);
            OfferChanged?.Invoke();
            return result;
        }

        /// <summary>
        /// WP-077 K10 "undo last purchase" (direction §2.5): the offer's LAST card or Hearth buy comes back in
        /// full — the wallet refund is a spend un-happening, not income — while the screen is open, before
        /// CONTINUE and outside the countdown's last 3 s (<see cref="CloseUndo"/>). A card's pick right comes
        /// back; the reroll right closed by the first purchase does not (DEC-050). One step only.
        /// </summary>
        public RunTreeCommandResult TryUndoLast(int sessionId, int revision)
        {
            if (!IsOpen || sessionId != SessionId || revision != Revision || Status != UpgradeOfferStatus.Ready)
            {
                return RunTreeCommandResult.NotAvailable;
            }

            if (!_last.Valid || _last.Session != SessionId)
            {
                return RunTreeCommandResult.NothingToUndo;
            }

            if (_undoClosed)
            {
                return RunTreeCommandResult.UndoClosed;
            }

            // M-37 (C-00 30.09): a Hearth stoke can only be undone onto a build — check before any gold moves.
            if (_last.Hearth && ModifierState() == null)
            {
                return RunTreeCommandResult.NotAvailable;
            }

            if (_wallet == null || _wallet.TryRefund(_last.TransactionId) != GoldTransactionResult.Applied)
            {
                _last = default;
                return RunTreeCommandResult.WalletRefused;
            }

            WeaponModifierState modifiers = ModifierState();
            LastPurchase last = _last;
            _last = default;
            UpgradeCardOffer undone = default;
            if (last.Hearth)
            {
                Tree.UnstokeHearth(modifiers);
            }
            else
            {
                if (last.Tier)
                {
                    if (modifiers != null) modifiers.SetTier(last.Branch, Math.Max(0, modifiers.GetTier(last.Branch) - 1), _config);
                }
                else
                {
                    Tree.Unmark(last.Node);
                }

                // M-38 (C-00 30.09): the bought node was the pin (its purchase cleared it); give it back only if no new pin
                // was set since — an undo never overwrites the player's newer choice nor spends an assignment.
                if (last.WasPinned && last.Node >= 0 && Tree.PinnedIndex < 0) Tree.SetPin(last.Node);

                undone = _cards[last.CardIndex];
                undone.Cost = last.Cost;
                UpgradeCardOffer card = undone;
                card.Purchased = false;
                card.TransactionId = 0;
                card.BoughtNodeIndex = -1;
                _cards[last.CardIndex] = card;
                PurchasesThisRound = Math.Max(0, PurchasesThisRound - 1);
                PurchaseEntitlement = UpgradeEntitlement.Available;
            }

            GoldSpentThisRound = Math.Max(0, GoldSpentThisRound - last.Cost);
            if (last.Node >= 0 && last.Node < Tree.Graph.Count)
                RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.Undo, Tree.Graph[last.Node].Id, Round, last.Cost, Tree.Graph[last.Node].Lane, Tree.Graph[last.Node].Kind));
            Revision++;
            RefreshCards();
            if (!last.Hearth) PurchaseUndone?.Invoke(undone);
            OfferChanged?.Invoke();
            return RunTreeCommandResult.Done;
        }

        /// <summary>WP-077 K10: gold a Reforge of this owned fork would cost now, else −1 (used / too early / not a fork).</summary>
        public int ReforgeCost(int nodeIndex)
        {
            if (!IsOpen || !Tree.IsBuilt || !Tree.CanReforge(nodeIndex, Round)) return -1;
            return Tree.Cost(Tree.ForkPartner(nodeIndex), ModifierState());
        }

        /// <summary>
        /// WP-077 K10 Reforge ("Yeniden Dövme", direction §2.5 / §6): swap an owned fork for its partner —
        /// once per run, from round 5, at the partner's price, no refund. Not undoable; it closes the undo.
        /// </summary>
        public RunTreeCommandResult TryReforge(string forkNodeId, int sessionId, int revision)
        {
            if (!IsOpen || sessionId != SessionId || revision != Revision || Status != UpgradeOfferStatus.Ready)
            {
                return RunTreeCommandResult.NotAvailable;
            }

            if (!Tree.IsBuilt || _wallet == null || !_wallet.IsOpen)
            {
                return RunTreeCommandResult.NoRun;
            }

            int i = string.IsNullOrEmpty(forkNodeId) ? -1 : Tree.Graph.IndexOf(forkNodeId);
            if (i < 0)
            {
                return RunTreeCommandResult.UnknownNode;
            }

            if (Tree.ReforgeUsed)
            {
                return RunTreeCommandResult.AlreadyUsed;
            }

            if (Round < RunTreeState.ReforgeFromRound)
            {
                return RunTreeCommandResult.TooEarly;
            }

            if (!Tree.CanReforge(i, Round))
            {
                return RunTreeCommandResult.NotAvailable;
            }

            int cost = Tree.Cost(Tree.ForkPartner(i), ModifierState());
            if (!_wallet.CanAfford(cost))
            {
                return RunTreeCommandResult.InsufficientGold;
            }

            if (_wallet.TrySpend(cost, GoldSource.TreeReforge, _wallet.NextTransactionId()) != GoldTransactionResult.Applied)
            {
                return RunTreeCommandResult.WalletRefused;
            }

            int partner = Tree.ForkPartner(i);
            Tree.Reforge(i, Round);
            GoldSpentThisRound += cost;
            RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.Reforged, Tree.Graph[partner].Id, Round, cost, Tree.Graph[partner].Lane, RunTreeNodeKind.Fork));
            _last = default;
            Revision++;
            RefreshCards();
            RunAudioEvents.Raise(RunAudioEvent.UpgradePurchased);
            OfferChanged?.Invoke();
            return RunTreeCommandResult.Done;
        }

        /// <summary>Whether SetPin would accept this action, without changing state. Null/empty means clear, including a pin whose target is now invalid.</summary>
        public bool CanSetPin(string nodeId)
        {
            if (!IsOpen || !Tree.IsBuilt || Tree.RunId != RunId)
            {
                return false;
            }

            int i = string.IsNullOrEmpty(nodeId) ? -1 : Tree.Graph.IndexOf(nodeId);
            if (!string.IsNullOrEmpty(nodeId) && i < 0)
            {
                return false;
            }

            if (i >= 0 && !Tree.CanPin(i, ModifierState())) return false;
            return i == Tree.PinnedIndex || i < 0 || i == _pinAssignedIndex || PinChangesThisRound < PinsPerRound;
        }

        /// <summary>WP-077 B6 telemetry: the node offer just drawn (a new offer or a reroll) — ids, frontier size, doubled cards.</summary>
        private void RaiseOfferDrawn(bool reroll)
        {
            if (!Tree.IsBuilt || RunTreeEvents.IsSilent) return;
            var ids = new System.Text.StringBuilder(64);
            for (int k = 0; k < _nodePicks.Count; k++)
            {
                if (k > 0) ids.Append(',');
                ids.Append(Tree.Graph[_nodePicks[k]].Id);
            }

            RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.OfferDrawn, ids.ToString(), Round, 0, RunTreeLane.Weapon, RunTreeNodeKind.Tier,
                Tree.LastFrontierSize, Tree.PityCount(_nodePicks), reroll));
        }

        /// <summary>WP-077 Nişan (K2, M-36): <see cref="PinsPerRound"/> target assignments per round (run-tree.json pin.perRound).</summary>
        public static int PinsPerRound => Math.Max(1, RunTreeCatalog.Document.pin != null ? RunTreeCatalog.Document.pin.perRound : 1);

        /// <summary>
        /// M-35 (C-00 30.09): a pin whose target has closed (bought, excluded, maxed, locked, unreachable) is cleared at the
        /// shop's command points — a refresh, a new offer, a pin command — without spending or refunding an assignment.
        /// Returns true when it cleared one.
        /// </summary>
        private bool DropClosedPin()
        {
            if (!Tree.IsBuilt || Tree.PinnedIndex < 0 || Tree.CanPin(Tree.PinnedIndex, ModifierState())) return false;
            Tree.SetPin(-1);
            return true;
        }

        /// <summary>WP-077 Nişan (K2): one target assignment per round; null/empty clears without refunding it. Same target is a no-op.</summary>
        public bool SetPin(string nodeId)
        {
            bool dropped = IsOpen && DropClosedPin();
            if (dropped && !CanSetPin(nodeId))
            {
                OfferChanged?.Invoke();   // the closed pin is gone even though this command is refused
                return false;
            }

            if (!CanSetPin(nodeId)) return false;
            int i = string.IsNullOrEmpty(nodeId) ? -1 : Tree.Graph.IndexOf(nodeId);
            if (i == Tree.PinnedIndex)
            {
                if (dropped) OfferChanged?.Invoke();   // S-09 30.09: the command cleared a closed pin — the state changed
                return true;
            }

            Tree.SetPin(i);
            if (i >= 0 && i == _pinAssignedIndex)
            {
                // DEC-110 (user 30.09): back to the target this round's assignment already went to — free.
                RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.PinSet, Tree.Graph[i].Id, Round, 0, Tree.Graph[i].Lane, Tree.Graph[i].Kind));
            }
            else if (i >= 0)
            {
                PinChangesThisRound++;
                _pinAssignedIndex = i;
                RunTreeEvents.Raise(new RunTreeEvent(RunTreeEventKind.PinSet, Tree.Graph[i].Id, Round, 0, Tree.Graph[i].Lane, Tree.Graph[i].Kind));
            }

            OfferChanged?.Invoke();
            return true;
        }

        /// <summary>WP-077: the shop's eligibility rule for a tier node of the tree (price excluded, DEC-049).</summary>
        private UpgradeEligibility TierEligibility(UpgradeBranch branch, int ownedTier)
        {
            UpgradeBranchSettings settings = _config != null && _config.Economy != null ? _config.Economy.Find(branch) : null;
            return UpgradeOfferPolicy.Classify(settings, ownedTier, HasLiveConsumer(branch), IsUnlocked(branch));
        }

        /// <summary>
        /// WP-077 Nişan (pin): the first drawable node on the pinned node's path comes with this offer.
        /// Today the offer carries branch-tier cards only, so the guarantee holds for a tier step (the
        /// last drawn card gives way); node cards join with the S-09 tree screen (WP-068 R1).
        /// </summary>
        private void ApplyPinGuarantee(WeaponModifierState modifiers, EconomyConfigSection economy)
        {
            if (!Tree.IsBuilt || Tree.PinnedIndex < 0 || _picks.Count == 0)
            {
                return;
            }

            int step = Tree.PinnedOfferable(modifiers);
            if (step < 0 || Tree.Graph[step].Kind != RunTreeNodeKind.Tier)
            {
                return;
            }

            UpgradeBranch branch = Tree.Graph[step].Branch;
            if (UpgradeOfferPolicy.IndexOfBranch(_picks, branch) >= 0)
            {
                return;
            }

            UpgradeBranchSettings settings = economy.Find(branch);
            if (settings != null)
            {
                _picks[_picks.Count - 1] = settings;
            }
        }

        /// <summary>
        /// RT5: the pinned step this shop's offer really carries — any drawable step on the node-card path; on the three-card
        /// path only a tier step (its cards are branch tiers, <see cref="ApplyPinGuarantee"/>). -1 when none.
        /// </summary>
        public int PinGuaranteedStep(WeaponModifierState build)
        {
            if (!Tree.IsBuilt) return -1;
            int step = Tree.PinnedOfferable(build);
            if (step < 0 || NodeCardsActive) return step;
            return Tree.Graph[step].Kind == RunTreeNodeKind.Tier ? step : -1;
        }

        /// <summary>RT4: the three-card slot holding the pinned tier step's card (the reroll keeps it), else -1.</summary>
        private int PinnedTierSlot()
        {
            int step = PinGuaranteedStep(ModifierState());
            if (step < 0) return -1;
            UpgradeBranch branch = Tree.Graph[step].Branch;
            for (int i = 0; i < _cards.Count; i++)
            {
                if (!_cards[i].IsNode && _cards[i].Branch == branch) return i;
            }

            return -1;
        }

        private bool HasLiveConsumer(UpgradeBranch branch)
        {
            return OfferUnbackedBranches || UpgradeCardCatalog.HasLiveConsumer(branch, ShopWeapon);   // SHOULD: the shop's weapon, not the global one
        }

        private bool IsUnlocked(UpgradeBranch branch)
        {
            return _unlocked != null ? _unlocked(branch) : ProfileService.IsBranchUnlocked(branch);
        }

        private UpgradeCardOffer BuildCard(UpgradeBranchSettings settings, WeaponModifierState modifiers,
            EconomyConfigSection economy)
        {
            int owned = modifiers != null ? modifiers.GetTier(settings.Branch) : 0;

            return new UpgradeCardOffer
            {
                Branch = settings.Branch,
                DisplayName = string.IsNullOrEmpty(settings.DisplayName)
                    ? settings.Branch.ToString()
                    : settings.DisplayName,
                Description = UpgradeCardCatalog.Describe(settings.Branch, owned),
                Kind = UpgradeCardCatalog.KindOf(settings.Branch),
                OwnedTier = owned,
                NextTier = owned + 1,
                MaxTier = Mathf.Max(1, settings.MaxTier),
                Cost = ResolveCost(settings, owned, economy),
                Purchased = false,
                IsValid = true,
                NodeIndex = -1,
                AltNodeIndex = -1,
                BoughtNodeIndex = -1
            };
        }

        /// <summary>
        /// Price of the next tier. The growth curve is the config's own
        /// <see cref="UpgradeBranchSettings.CostForTier"/>; the only thing added here is the
        /// request's 15-45 band, applied to the FIRST tier only so a mis-typed BaseCost
        /// cannot put a 4-gold or a 400-gold card on the opening screen. Later tiers are
        /// deliberately left free to climb past 45 - "rises with tier" is the point.
        /// </summary>
        internal static int ResolveCost(UpgradeBranchSettings settings, int ownedTier, EconomyConfigSection economy)
        {
            int cost = settings.CostForTier(ownedTier);

            if (ownedTier == 0 && economy != null)
            {
                int min = Mathf.Max(1, Mathf.Min(economy.StartingCostMin, economy.StartingCostMax));
                int max = Mathf.Max(min, Mathf.Max(economy.StartingCostMin, economy.StartingCostMax));
                cost = Mathf.Clamp(cost, min, max);
            }

            return Mathf.Max(1, cost);
        }

        /// <summary>Re-reads owned tiers and prices after a purchase or a reroll.</summary>
        private void RefreshCards()
        {
            DropClosedPin();
            WeaponModifierState modifiers = ModifierState();
            EconomyConfigSection economy = _config != null ? _config.Economy : null;

            for (int i = 0; i < _cards.Count; i++)
            {
                UpgradeCardOffer card = _cards[i];
                if (!card.IsValid)
                {
                    continue;
                }

                if (card.IsNode && card.NodeKind != RunTreeNodeKind.Tier)
                {
                    // K10: a node card re-prices from the tree (capstone discount, Hearth-free); its text is fixed.
                    if (!card.Purchased && Tree.IsBuilt)
                    {
                        card.Cost = Tree.Cost(card.NodeIndex, modifiers);
                        card.AltNodeIndex = Tree.ForkPartner(card.NodeIndex) >= 0 && Tree.IsOfferable(Tree.ForkPartner(card.NodeIndex), modifiers) ? Tree.ForkPartner(card.NodeIndex) : -1;
                    }

                    _cards[i] = card;
                    continue;
                }

                UpgradeBranchSettings settings = economy != null ? economy.Find(card.Branch) : null;
                if (settings == null)
                {
                    continue;
                }

                int owned = modifiers != null ? modifiers.GetTier(card.Branch) : card.OwnedTier;

                card.OwnedTier = owned;
                card.MaxTier = Mathf.Max(1, settings.MaxTier);

                if (!card.Purchased)
                {
                    card.NextTier = owned + 1;
                    card.Cost = ResolveCost(settings, owned, economy);
                    card.Description = UpgradeCardCatalog.Describe(card.Branch, owned);
                }

                _cards[i] = card;
            }
        }

        private WeaponModifierState ModifierState()
        {
            if (_injectedModifiers != null)
            {
                return _injectedModifiers;
            }

            WeaponController controller = WeaponRegistry.ActiveController;
            return controller != null ? controller.Modifiers : null;
        }

        // ------------------------------------------------------------------ debug

        /// <summary>One-line F1 readout of the current offer.</summary>
        public string Describe()
        {
            if (!IsOpen)
            {
                return "closed";
            }

            var sb = new System.Text.StringBuilder(128);
            sb.Append("R").Append(Round).Append(" s").Append(SessionId).Append(" r").Append(Revision)
              .Append(" | ").Append(Gold).Append("g | ");

            if (Status == UpgradeOfferStatus.ConfigurationError)
            {
                sb.Append("CONFIG ERROR ").Append(Fault);
                return sb.ToString();
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append("  ");
                }

                UpgradeCardOffer c = _cards[i];
                sb.Append('[').Append(i + 1).Append(": ").Append(c.TitleWithTier)
                  .Append(' ').Append(c.Cost).Append('g');

                if (c.Purchased)
                {
                    sb.Append(" BOUGHT");
                }
                else if (IsLocked)
                {
                    sb.Append(" LOCKED");
                }
                else if (!CanAfford(i))
                {
                    sb.Append(" SHORT");
                }

                sb.Append(']');
            }

            sb.Append(" | reroll ").Append(RerollCost).Append("g ").Append(RerollAvailability);
            return sb.ToString();
        }
    }
}
