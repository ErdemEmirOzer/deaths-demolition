using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The round-end upgrade offer: three cards, buy or skip, no reroll.
    ///
    /// RULES FROM THE REQUEST, AND WHERE EACH ONE LIVES
    ///   * three cards per round end ......... EconomyConfigSection.CardsPerRound
    ///   * buy one, several, or none ......... <see cref="TryPurchase"/> may be called
    ///                                         until the gold runs out; skipping is just
    ///                                         closing the screen
    ///   * NO REROLL ......................... there is no Reroll method on this class at
    ///                                         all. EconomyConfigSection.AllowReroll is
    ///                                         read only to assert it is false, so an
    ///                                         accidental true is reported instead of
    ///                                         silently doing nothing
    ///   * unspent gold carries over ......... nothing here or in the round transition
    ///                                         touches GoldWallet.Balance
    ///   * cost starts at 15-45 and rises .... UpgradeBranchSettings.CostForTier, clamped
    ///                                         into the StartingCostMin/Max band on the
    ///                                         first tier only
    ///
    /// A plain class, not a MonoBehaviour: <see cref="RunDirector"/> owns one, and a test
    /// can build its own with a throwaway wallet.
    ///
    /// G-08 CONTRACT
    ///     shop.IsOpen                      -> draw the screen
    ///     shop.Cards                       -> three UpgradeCardOffer, already priced
    ///     shop.CanAfford(i)                -> enable/disable the buy button
    ///     shop.TryPurchase(i)              -> the entire purchase
    ///     RunDirector.Instance.CloseUpgradeScreen() -> the CONTINUE button
    /// Nothing else. The shop never draws and the screen never prices.
    /// </summary>
    public sealed class RoundUpgradeShop
    {
        private readonly List<UpgradeCardOffer> _cards = new List<UpgradeCardOffer>(4);
        private readonly List<UpgradeBranchSettings> _candidates = new List<UpgradeBranchSettings>(16);
        private readonly List<float> _weights = new List<float>(16);

        private PrototypeConfig _config;
        private GoldWallet _wallet;
        private WeaponModifierState _injectedModifiers;
        private bool _purchasing;

        // ------------------------------------------------------------------ state

        public bool IsOpen { get; private set; }

        /// <summary>The round that was just completed, i.e. the one this offer rewards.</summary>
        public int Round { get; private set; }

        /// <summary>Run the current offer belongs to. 0 while closed.</summary>
        public int RunId { get; private set; }

        /// <summary>
        /// Identity of the current screen session, raised on every <see cref="Open"/>.
        /// A purchase quoting an older session is refused: a button from the previous
        /// round's screen, or a callback that arrived after the screen closed, cannot buy
        /// into this round's offer (WP-004).
        /// </summary>
        public int SessionId { get; private set; }

        /// <summary>Why the last <see cref="TryPurchase"/> ended the way it did. F1 readout and the screen's footer.</summary>
        public UpgradePurchaseResult LastResult { get; private set; } = UpgradePurchaseResult.None;

        /// <summary>The three cards. Read-only by construction; buying goes through this class.</summary>
        public IReadOnlyList<UpgradeCardOffer> Cards => _cards;

        /// <summary>Spendable gold. Convenience so the screen does not need the wallet.</summary>
        public int Gold => _wallet != null ? _wallet.Balance : 0;

        /// <summary>Cards bought on THIS screen. Resets on every open.</summary>
        public int PurchasesThisRound { get; private set; }

        /// <summary>Gold spent on THIS screen.</summary>
        public int GoldSpentThisRound { get; private set; }

        /// <summary>
        /// Debug only. Puts branches that no live system reads back into the draw, so the
        /// card layout can be tested with the full set. It changes NOTHING today: all ten
        /// branches are backed since the four player-side ones were wired to PlayerMotor and
        /// PlayerHealth. The switch stays because the next branch added to the enum will
        /// need it. See <see cref="UpgradeCardCatalog"/>.
        /// </summary>
        public bool OfferUnbackedBranches { get; set; }

        // ------------------------------------------------------------------ events

        /// <summary>The offer was rebuilt or a card changed. G-08 redraws.</summary>
        public event Action OfferChanged;

        /// <summary>A card was bought. The director records the spend against the run stats.</summary>
        public event Action<UpgradeCardOffer> Purchased;

        /// <summary>Buy was pressed with too little gold. Argument is the card index.</summary>
        public event Action<int> PurchaseRefused;

        // ------------------------------------------------------------------ open / close

        /// <summary>Rolls a fresh offer and opens the screen. Reads tiers from the armed player's weapon.</summary>
        public void Open(int round, PrototypeConfig config, GoldWallet wallet)
        {
            Open(round, config, wallet, null, 0);
        }

        /// <summary>
        /// Rolls a fresh offer and opens the screen. <paramref name="modifiers"/> is the
        /// rank authority to read and write; null means the armed player's weapon
        /// (<see cref="WeaponRegistry.ActiveController"/>), which is what the director
        /// passes. A test hands in its own state so the whole purchase can run without a
        /// scene.
        /// </summary>
        public void Open(int round, PrototypeConfig config, GoldWallet wallet, WeaponModifierState modifiers, int runId)
        {
            _config = config;
            _wallet = wallet;
            _injectedModifiers = modifiers;

            Round = Mathf.Max(1, round);
            RunId = Mathf.Max(0, runId);
            SessionId++;
            PurchasesThisRound = 0;
            GoldSpentThisRound = 0;
            LastResult = UpgradePurchaseResult.None;
            IsOpen = true;

            BuildOffer();

            if (_config != null && _config.Economy.AllowReroll)
            {
                // Not implemented anywhere, on purpose. Saying so beats a dead button.
                Debug.LogWarning(
                    "[Run] EconomyConfigSection.AllowReroll is true, but the prototype has no reroll " +
                    "(request: no reroll). The flag does nothing; set it back to false.");
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
            RunId = 0;
            _injectedModifiers = null;
            OfferChanged?.Invoke();
        }

        // ------------------------------------------------------------------ purchase

        public bool IsPurchasable(int index)
        {
            if (!IsOpen || index < 0 || index >= _cards.Count)
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

        /// <summary>Buys one card of the CURRENT session. The F1 panel's path.</summary>
        public bool TryPurchase(int index)
        {
            return TryPurchase(index, SessionId);
        }

        /// <summary>
        /// Buys one card. The whole purchase is here, in this order, and every check
        /// happens BEFORE the gold moves so no refusal can leave a half state:
        ///
        ///   1 the shop is open and the caller's session is this session (stale screen /
        ///     late callback learns nothing)
        ///   2 the card is real, unbought and not maxed
        ///   3 there is a rank authority to apply the tier to (no armed player: refused,
        ///     nothing spent - the old refund path is gone)
        ///   4 the live tier still matches the card (a debug button moved it: refresh, refuse)
        ///   5 the branch is below its ceiling on the live state (a clamped AddTier would
        ///     silently change nothing)
        ///   6 the wallet can afford it
        ///   7 spend, exactly once, under its own transaction id
        ///   8 apply the tier, exactly once, and verify it moved
        ///   9 mark the card, re-price the rest, tell the listeners
        /// </summary>
        public bool TryPurchase(int index, int sessionId)
        {
            if (_purchasing)
            {
                // Re-entrant call from a listener of our own events. Never legitimate.
                return false;
            }

            _purchasing = true;
            try
            {
                return PurchaseCore(index, sessionId);
            }
            finally
            {
                _purchasing = false;
            }
        }

        private bool PurchaseCore(int index, int sessionId)
        {
            if (!IsOpen)
            {
                return Refuse(index, UpgradePurchaseResult.ShopClosed);
            }

            if (sessionId != SessionId)
            {
                // A stale screen learns nothing about this offer, not even a card's state.
                return Refuse(index, UpgradePurchaseResult.SessionMismatch);
            }

            if (index < 0 || index >= _cards.Count || !_cards[index].IsValid)
            {
                return Refuse(index, UpgradePurchaseResult.InvalidCard);
            }

            UpgradeCardOffer card = _cards[index];

            if (card.Purchased)
            {
                return Refuse(index, UpgradePurchaseResult.AlreadyPurchased);
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
                // tier are stale; re-read everything and let the player look again.
                RefreshCards();
                OfferChanged?.Invoke();
                return Refuse(index, UpgradePurchaseResult.StaleOffer);
            }

            if (card.IsMaxed || modifiers.IsMaxed(card.Branch, _config))
            {
                return Refuse(index, UpgradePurchaseResult.BranchMaxed);
            }

            if (_wallet == null || !_wallet.CanAfford(card.Cost))
            {
                return Refuse(index, UpgradePurchaseResult.InsufficientGold);
            }

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

            // The single line the whole card system exists to call (G-03). The weapon
            // re-reads its modifiers on the next shot; there is no apply step.
            modifiers.AddTier(card.Branch, 1, _config);

            int appliedTier = modifiers.GetTier(card.Branch);
            if (appliedTier != liveTier + 1)
            {
                // Unreachable after check 5; kept as a loud assertion so a future ceiling
                // rule cannot turn into a silent charge. The gold is NOT refunded through
                // a credit (that would count as income) - the error is the signal.
                Debug.LogError("[Run] Upgrade tier did not move after a successful spend (" + card.Branch +
                               " live " + liveTier + " -> " + appliedTier + "). Transaction " + transactionId + ".");
                RefreshCards();
                OfferChanged?.Invoke();
                return Refuse(index, UpgradePurchaseResult.TierDidNotChange);
            }

            card.Purchased = true;
            card.OwnedTier = appliedTier;
            card.TransactionId = transactionId;
            _cards[index] = card;

            PurchasesThisRound++;
            GoldSpentThisRound += card.Cost;
            LastResult = UpgradePurchaseResult.Purchased;

            // Re-price the remaining cards: the build readout is stale and a maxed branch
            // must grey out immediately.
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
                result == UpgradePurchaseResult.TierDidNotChange)
            {
                PurchaseRefused?.Invoke(index);
                RunAudioEvents.Raise(RunAudioEvent.UpgradeRefused);
            }

            return false;
        }

        // ------------------------------------------------------------------ offer

        private void BuildOffer()
        {
            _cards.Clear();
            _candidates.Clear();
            _weights.Clear();

            if (_config == null)
            {
                return;
            }

            EconomyConfigSection economy = _config.Economy;
            WeaponModifierState modifiers = ModifierState();

            for (int i = 0; i < economy.Branches.Count; i++)
            {
                UpgradeBranchSettings settings = economy.Branches[i];
                if (settings == null || settings.OfferWeight <= 0f || settings.MaxTier <= 0)
                {
                    continue;
                }

                if (!OfferUnbackedBranches && !UpgradeCardCatalog.HasLiveConsumer(settings.Branch))
                {
                    continue;
                }

                // A maxed branch stops being offered; it would be a card that cannot be
                // bought sitting in one of only three slots.
                if (modifiers != null && modifiers.GetTier(settings.Branch) >= settings.MaxTier)
                {
                    continue;
                }

                _candidates.Add(settings);
                _weights.Add(Mathf.Max(0.0001f, settings.OfferWeight));
            }

            int wanted = Mathf.Clamp(economy.CardsPerRound, 1, 8);

            // Weighted draw WITHOUT replacement: three cards must be three different
            // branches, or a round end can offer Piercing three times and the choice the
            // screen exists to present disappears.
            for (int slot = 0; slot < wanted && _candidates.Count > 0; slot++)
            {
                int pick = DrawWeightedIndex();
                if (pick < 0)
                {
                    break;
                }

                _cards.Add(BuildCard(_candidates[pick], modifiers, economy));

                _candidates.RemoveAt(pick);
                _weights.RemoveAt(pick);
            }
        }

        private int DrawWeightedIndex()
        {
            float total = 0f;
            for (int i = 0; i < _weights.Count; i++)
            {
                total += _weights[i];
            }

            if (total <= 0f)
            {
                return _weights.Count > 0 ? 0 : -1;
            }

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < _weights.Count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f)
                {
                    return i;
                }
            }

            return _weights.Count - 1;
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
                IsValid = true
            };
        }

        /// <summary>
        /// Price of the next tier. The growth curve is the config's own
        /// <see cref="UpgradeBranchSettings.CostForTier"/>; the only thing added here is the
        /// request's 15-45 band, applied to the FIRST tier only so a mis-typed BaseCost
        /// cannot put a 4-gold or a 400-gold card on the opening screen. Later tiers are
        /// deliberately left free to climb past 45 - "rises with tier" is the point.
        /// </summary>
        private static int ResolveCost(UpgradeBranchSettings settings, int ownedTier, EconomyConfigSection economy)
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

        /// <summary>Re-reads owned tiers and prices after a purchase.</summary>
        private void RefreshCards()
        {
            WeaponModifierState modifiers = ModifierState();
            EconomyConfigSection economy = _config != null ? _config.Economy : null;

            for (int i = 0; i < _cards.Count; i++)
            {
                UpgradeCardOffer card = _cards[i];
                if (!card.IsValid)
                {
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

            var sb = new System.Text.StringBuilder(96);
            sb.Append("R").Append(Round).Append(" s").Append(SessionId).Append(" | ").Append(Gold).Append("g | ");

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
                else if (!CanAfford(i))
                {
                    sb.Append(" SHORT");
                }

                sb.Append(']');
            }

            return sb.ToString();
        }
    }
}
