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

        // ------------------------------------------------------------------ state

        public bool IsOpen { get; private set; }

        /// <summary>The round that was just completed, i.e. the one this offer rewards.</summary>
        public int Round { get; private set; }

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

        /// <summary>Rolls a fresh offer and opens the screen.</summary>
        public void Open(int round, PrototypeConfig config, GoldWallet wallet)
        {
            _config = config;
            _wallet = wallet;

            Round = Mathf.Max(1, round);
            PurchasesThisRound = 0;
            GoldSpentThisRound = 0;
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

        /// <summary>
        /// Buys one card. The whole purchase is here: take the gold, add the tier, tell the
        /// listeners. There is no apply step - the weapon re-reads its modifiers on the
        /// very next shot.
        /// </summary>
        public bool TryPurchase(int index)
        {
            if (!IsPurchasable(index))
            {
                return false;
            }

            UpgradeCardOffer card = _cards[index];

            if (_wallet == null || !_wallet.TrySpend(card.Cost))
            {
                PurchaseRefused?.Invoke(index);
                RunAudioEvents.Raise(RunAudioEvent.UpgradeRefused);
                return false;
            }

            WeaponController controller = WeaponRegistry.ActiveController;
            if (controller != null)
            {
                // The single line the whole card system exists to call (G-03).
                controller.Modifiers.AddTier(card.Branch, 1, _config);
            }
            else
            {
                // Refund rather than charge for nothing. Only reachable if the screen is
                // opened outside a run, which the director does not do.
                _wallet.Credit(card.Cost);
                PurchaseRefused?.Invoke(index);
                Debug.LogWarning("[Run] Upgrade bought with no armed player. Refunded; nothing was applied.");
                return false;
            }

            card.Purchased = true;
            card.OwnedTier = controller.Modifiers.GetTier(card.Branch);
            _cards[index] = card;

            PurchasesThisRound++;
            GoldSpentThisRound += card.Cost;

            // Re-price the remaining cards: buying Piercing I makes the OTHER Piercing card
            // (there is never one, branches are drawn without replacement) and the build
            // readout stale, and a maxed branch must grey out immediately.
            RefreshCards();

            Purchased?.Invoke(card);
            RunAudioEvents.Raise(RunAudioEvent.UpgradePurchased);
            OfferChanged?.Invoke();
            return true;
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

        private static WeaponModifierState ModifierState()
        {
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
            sb.Append("R").Append(Round).Append(" | ").Append(Gold).Append("g | ");

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
