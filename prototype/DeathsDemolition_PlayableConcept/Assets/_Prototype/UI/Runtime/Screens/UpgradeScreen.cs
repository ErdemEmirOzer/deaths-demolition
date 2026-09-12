using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The round-end upgrade screen. Cards for the round just cleared, then continue.
    ///
    /// IT OWNS NO RULE OF ITS OWN
    /// --------------------------
    /// Every fact on this screen comes from <see cref="RoundUpgradeShop"/> and is drawn
    /// verbatim:
    ///
    ///     shop.IsOpen / RunDirector.Phase  -> whether the screen is up
    ///     shop.Cards[i]                    -> title, tier, MaxTier, cost, purchased
    ///     shop.Cards[i].Description        -> UpgradeCardCatalog.Describe(), computed
    ///                                         from the LIVE tuning, so the card text
    ///                                         cannot describe an effect the player does
    ///                                         not get. This screen never writes its own
    ///                                         sentence about what an upgrade does.
    ///     shop.Gold / shop.CanAfford(i)    -> the balance and the enabled state
    ///     shop.TryPurchase(i)              -> the whole purchase
    ///     RunDirector.CloseUpgradeScreen() -> the CONTINUE button
    ///
    /// There is no price arithmetic here, no timer here, and no countdown clock here -
    /// the 3 s pre-round countdown is <see cref="RunDirector"/>'s, and this screen only
    /// PRINTS <c>RunEvents.Current.CountdownRemaining</c> after it has closed. The
    /// screen's copy is deliberately neutral about how many cards a round allows and
    /// whether an offer can be re-rolled: both are open product decisions (DEC-048,
    /// DEC-050) and the screen must not announce either as a rule.
    ///
    /// WHICH ROUND IT BELONGS TO (WP-005)
    /// The panel opens only for the director's current <c>LastRoundEnd</c> snapshot:
    /// same run id, same round, valid. Until that snapshot exists the phase may already
    /// say Upgrading for a frame, and the screen shows its loading notice rather than a
    /// balance that is about to change. Every purchase quotes the shop session captured
    /// at open, so a stale button cannot buy into the next round's offer. The screen
    /// reports itself to <see cref="UiNavigation"/> as the RoundUpgrade layer and reads
    /// the pause overlay from there - no scene search per frame.
    ///
    /// OPENING AND CLOSING
    /// -------------------
    /// Driven entirely by <see cref="RunPhase"/>. The screen has no timer of its own:
    ///
    ///     RunPhase.Upgrading -> panel canvas on   (the world is already frozen by the
    ///                                              director's RoundEndHold)
    ///     RunPhase.Countdown -> panel off, the small "NEXT ROUND IN n" strip on
    ///     anything else      -> both off
    ///
    /// A FULL SCREEN, NOT A TRANSLUCENT OVERLAY
    /// ----------------------------------------
    /// This was measured, not chosen. Over a scrim (ScrimOverlay, alpha 0.86) laid on a
    /// worst-case bright frozen frame, the backdrop luminance rises to about that of
    /// SurfaceRaised - and there the primary accent fill measures 2.48:1 and a hairline
    /// border 1.33:1 against it, i.e. the screen's own edges would disappear on a light
    /// map and hold on a dark one. A UI whose contrast depends on where the camera was
    /// pointing cannot be audited at all. So the screen draws the standard opaque
    /// backdrop, exactly like Main Menu / Selection / Results, and every pair it puts on
    /// screen is one <see cref="UiContrast"/> already measures.
    ///
    /// The countdown strip is the one thing that DOES sit over live gameplay, and it uses
    /// the HUD's near-opaque HudInk backing for that reason - the same treatment, and the
    /// same audited pairs, as every HUD panel.
    ///
    /// FIVE STATES
    ///   normal    three cards, at least one affordable
    ///   empty     no cards - every branch is at MaxTier. A finished sentence and a
    ///             working CONTINUE, never a blank rectangle
    ///   loading   phase says Upgrading but the offer is not built yet (one frame, or a
    ///             scene opened without PrototypeConfig). Named, not blank
    ///   error     a purchase the shop refused - stated in the footer, gold untouched
    ///   dense     0 gold (every card disabled, CONTINUE still works and takes the
    ///             initial focus), over-long card text, five-figure gold, and more cards
    ///             than three if EconomyConfigSection.CardsPerRound is raised
    /// </summary>
    public sealed class UpgradeScreen : UiScreen
    {
        // ---------------------------------------------------------------- geometry
        // PROTOTYPE-ONLY layout constants. Art direction, not product decisions; every
        // one of them is a multiple of the 8pt grid, and every GAP between them is a
        // UiSpace step - there is no free-typed offset anywhere in this file.
        //
        // THE VERTICAL BUDGET, which is what stops this screen from ever needing to
        // scroll. Content is 1920x1080 minus the page grid (96 sides, 64 top/bottom), so
        // 1728 x 952 at the reference resolution:
        //
        //   header (eyebrow 24 + 4 + H1 52 + 8 + body 32 + 16 + rule 1) ....... 137
        //   + XXL ............................................................  32
        //   gold strip .......................................................  128
        //   + XL .............................................................  24
        //   cards ............................................................  440
        //                                                                       ---
        //                                                                       761
        //   footer, docked to the bottom of the same 952 ......................  48
        //
        // 143 px of slack, and the canvas scaler matches on HEIGHT, so that margin is
        // identical at every aspect ratio. Width is absorbed by fractional anchors.
        //
        // The card's own 440 breaks down as:
        //   XL 24 pad + eyebrow 24 + XS 4 + title 36 + S 8 + tier 24 + M 12
        //   + ladder 20 + L 16 + rule 1 + L 16 + describe 4x32=128 ............ 289
        //   shelf gap M 12 + shelf 96 + shelf inset M 12 ...................... 120
        //   (the body rect is 296, so 7 px of slack absorbs font rounding)

        /// <summary>PROTOTYPE-ONLY: card height. Fits the tallest Describe() at four lines.</summary>
        private const float CardHeight = 440f;

        /// <summary>PROTOTYPE-ONLY: the recessed action shelf at the foot of every card.</summary>
        private const float ShelfHeight = 96f;

        /// <summary>PROTOTYPE-ONLY: gold banner under the header.</summary>
        private const float GoldStripHeight = 128f;

        /// <summary>PROTOTYPE-ONLY: tier ladder well. Pips are inset 4 px inside it.</summary>
        private const float PipWellHeight = 20f;

        /// <summary>Upper bound on the tier ladder. EconomyConfigSection clamps MaxTier well below this.</summary>
        private const int MaxPips = 8;

        /// <summary>PROTOTYPE-ONLY: state pill on the action shelf. Wide enough for "TOO EXPENSIVE".</summary>
        private const float StateChipWidth = 208f;

        private const float StateChipHeight = 30f;

        /// <summary>PROTOTYPE-ONLY: "PURCHASED" badge in the card's top-right corner.</summary>
        private const float BadgeWidth = 168f;

        private const float BadgeHeight = 30f;

        /// <summary>PROTOTYPE-ONLY: coin glyph beside a gold number.</summary>
        private const float CoinSize = 24f;

        private const float HeroCoinSize = 32f;

        /// <summary>PROTOTYPE-ONLY: hero gold value box. Numeric shrinks to fit inside it.</summary>
        private const float HeroValueHeight = 48f;

        /// <summary>PROTOTYPE-ONLY: CONTINUE, same width as the Results screen's.</summary>
        private const float ContinueWidth = 320f;

        /// <summary>PROTOTYPE-ONLY: the countdown strip that survives the screen's close.</summary>
        private const float CountdownWidth = 420f;

        private const float CountdownHeight = 72f;

        /// <summary>Lines reserved for Describe(). The box truncates; it never grows.</summary>
        private const int DescribeLines = 4;

        /// <summary>Hard clip on the card sentence before it is even laid out.</summary>
        private const int DescribeMaxCharacters = 200;

        /// <summary>Empty-state panel.</summary>
        private const float EmptyPanelWidth = 760f;

        private const float EmptyPanelHeight = 260f;

        // ----------------------------------------------------------------- runtime

        private CanvasGroup _panelGroup;

        private Canvas _countdownCanvas;
        private Text _countdownValue;

        private CardSlot[] _slots = new CardSlot[0];
        private RectTransform _cardsHost;
        private float _cardsTop;

        private GameObject _emptyPanel;
        private Text _emptyTitle;
        private Text _emptyBody;

        private Text _goldValue;
        private Text _goldSpent;
        private Text _goldNote;

        private UiButton _continueButton;
        private Text _footerNote;

        private bool _panelVisible;
        private bool _dirty = true;
        private int _sessionId;
        private RoundUpgradeShop _boundShop;
        private GoldWallet _boundWallet;
        private string _notice;
        private float _fade;

        protected override bool WantsUiCamera
        {
            // The run scene has a gameplay camera. Creating a second one would make the
            // upgrade screen the thing that clears the frame.
            get { return false; }
        }

        // ==================================================================== build

        protected override void Build()
        {
            _panelGroup = UiFactory.Group(Canvas.gameObject);

            // Header. Round number comes from the shop when it is open, so the eyebrow is
            // re-written on every open rather than baked in here.
            _cardsTop = BuildHeader(
                "ROUND COMPLETE",
                "SPEND YOUR GOLD",
                "Strengthen your weapon for the rounds ahead. Unspent gold carries into the next round.");

            _cardsTop += (int)UiSpace.XXL;

            BuildGoldStrip(_cardsTop);
            _cardsTop += GoldStripHeight + (int)UiSpace.XL;

            _cardsHost = Content;

            BuildEmptyPanel();
            BuildFooter();
            BuildCountdownStrip();

            Canvas.gameObject.SetActive(false);
        }

        // This screen holds no RunFlowClock hold: the round-end freeze is RunDirector's
        // RoundEndHold, taken and released by the director. The only subscriptions are to
        // the director's own shop and wallet INSTANCES (never to the static RunEvents),
        // and they are dropped here so nothing outlives the scene.
        private void OnDisable()
        {
            Unbind();

            if (_panelVisible)
            {
                _panelVisible = false;
                UiNavigation.ExitLayer(UiContext.RoundUpgrade);
            }
        }

        /// <summary>
        /// Points the screen at the live shop and wallet, and re-points it if the director
        /// is swapped. The screen redraws only when one of them says something changed -
        /// no per-frame polling of the wallet (WP-004).
        /// </summary>
        private void Bind()
        {
            RunDirector director = RunDirector.Instance;
            RoundUpgradeShop shop = director != null ? director.Shop : null;
            GoldWallet wallet = director != null ? director.Wallet : null;

            if (!ReferenceEquals(shop, _boundShop))
            {
                if (_boundShop != null)
                {
                    _boundShop.OfferChanged -= MarkDirty;
                }

                _boundShop = shop;
                if (_boundShop != null)
                {
                    _boundShop.OfferChanged += MarkDirty;
                }

                _dirty = true;
            }

            if (!ReferenceEquals(wallet, _boundWallet))
            {
                if (_boundWallet != null)
                {
                    _boundWallet.Changed -= OnWalletChanged;
                }

                _boundWallet = wallet;
                if (_boundWallet != null)
                {
                    _boundWallet.Changed += OnWalletChanged;
                }

                _dirty = true;
            }
        }

        private void Unbind()
        {
            if (_boundShop != null)
            {
                _boundShop.OfferChanged -= MarkDirty;
                _boundShop = null;
            }

            if (_boundWallet != null)
            {
                _boundWallet.Changed -= OnWalletChanged;
                _boundWallet = null;
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void OnWalletChanged(int balance)
        {
            _dirty = true;
        }

        // ============================================================== gold banner

        private void BuildGoldStrip(float top)
        {
            RectTransform card = UiFactory.Card("GoldStrip", Content, UiColor.BorderHairline, UiColor.SurfacePanel);
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = new Vector2(0f, -(top + GoldStripHeight));
            card.offsetMax = new Vector2(0f, -top);

            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XL));

            // ---- available gold, the hero number of this screen ------------------
            RectTransform available = Zone(host, 0f, 0.34f);
            var availableStack = new UiStack(available);

            Text label = UiFactory.Label(
                "AvailableLabel", available, "GOLD AVAILABLE", UiType.Label, UiColor.TextMuted);
            availableStack.Add((RectTransform)label.transform, UiFactory.LineHeight(UiType.Label));

            availableStack.Gap(UiSpace.XS);

            RectTransform valueRow = UiFactory.NewRect("ValueRow", available);
            availableStack.Add(valueRow, HeroValueHeight);

            Image coin = UiFactory.NewImage("Coin", valueRow, UiColor.StateGold, UiSprites.Disc);
            var coinRect = (RectTransform)coin.transform;
            coinRect.anchorMin = new Vector2(0f, 0.5f);
            coinRect.anchorMax = new Vector2(0f, 0.5f);
            coinRect.pivot = new Vector2(0f, 0.5f);
            coinRect.sizeDelta = new Vector2(HeroCoinSize, HeroCoinSize);
            coinRect.anchoredPosition = Vector2.zero;

            _goldValue = UiFactory.Numeric(
                "AvailableValue", valueRow, "0", UiType.NumericXL, UiColor.StateGold);
            RectTransform goldRect = UiFactory.Fill((RectTransform)_goldValue.transform);
            goldRect.offsetMin = new Vector2(HeroCoinSize + (int)UiSpace.M, 0f);

            // ---- spent this round ------------------------------------------------
            RectTransform spent = Zone(host, 0.36f, 0.62f);
            var spentStack = new UiStack(spent);

            Text spentLabel = UiFactory.Label(
                "SpentLabel", spent, "SPENT THIS ROUND", UiType.Label, UiColor.TextMuted);
            spentStack.Add((RectTransform)spentLabel.transform, UiFactory.LineHeight(UiType.Label));

            spentStack.Gap(UiSpace.XS);

            _goldSpent = UiFactory.Numeric("SpentValue", spent, "0", UiType.NumericM, UiColor.TextPrimary);
            spentStack.Add((RectTransform)_goldSpent.transform, HeroValueHeight);

            // ---- advisory line ---------------------------------------------------
            // Carries the 0-gold case. It is a finished sentence, not an error: a player
            // who cannot afford anything has still done nothing wrong.
            RectTransform note = Zone(host, 0.64f, 1f);
            _goldNote = UiFactory.Label(
                "Note", note,
                "Choose what to strengthen, then continue.",
                UiType.Caption, UiColor.TextMuted, TextAnchor.LowerLeft);
            UiFactory.Fill((RectTransform)_goldNote.transform);
        }

        /// <summary>A fractional-width zone of a row. Same responsive rule as UiScreen.Column.</summary>
        private static RectTransform Zone(RectTransform host, float from, float to)
        {
            RectTransform zone = UiFactory.NewRect("Zone", host);
            zone.anchorMin = new Vector2(from, 0f);
            zone.anchorMax = new Vector2(to, 1f);
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;
            return zone;
        }

        // ==================================================================== cards

        /// <summary>
        /// One card. Built once and then re-labelled, never rebuilt: destroying and
        /// recreating the cards after every purchase would throw away the focused control
        /// and leave a keyboard player with no focus at all.
        /// </summary>
        private sealed class CardSlot
        {
            public UiButton Button;
            public Text Eyebrow;
            public Text Title;
            public Text Tier;
            public Text Describe;
            public Text Price;

            /// <summary>The pill on the shelf: BUY / TOO EXPENSIVE / PURCHASED / MAX TIER.</summary>
            public Text StateLabel;

            /// <summary>The pill's fill. Recoloured with the label so the pair stays audited.</summary>
            public Image StateFill;

            /// <summary>The one-sentence explanation under the pill.</summary>
            public Text Sentence;

            public Image[] Pips;
            public GameObject Badge;
            public Image Coin;

            /// <summary>Icon well in the body's top-left. Sprite when art registered one, monogram otherwise.</summary>
            public Image IconWell;
            public Image Icon;
            public Text Monogram;
        }

        private void EnsureSlots(int count)
        {
            if (_slots.Length >= count)
            {
                return;
            }

            var grown = new CardSlot[count];
            for (int i = 0; i < _slots.Length; i++)
            {
                grown[i] = _slots[i];
            }

            for (int i = _slots.Length; i < count; i++)
            {
                grown[i] = BuildSlot(i);
            }

            _slots = grown;
        }

        private CardSlot BuildSlot(int index)
        {
            int captured = index;

            var slot = new CardSlot();

            // The WHOLE card is the control. UiButtonVariant.Card is the module's
            // "selectable tile" - the same thing the selection screen's weapon rows are -
            // so the focus ring wraps the card, the hit target is 560x440 rather than a
            // small button, and there is no nested interactive to navigate into.
            slot.Button = UiFactory.Button(
                "Card." + captured, _cardsHost, string.Empty, UiButtonVariant.Card,
                () => OnCardActivated(captured));

            // NOT clipped at the root: UiFactory.Button draws the focus halo and ring
            // OUTSIDE the control rect, and a RectMask2D here would erase both of them -
            // silently costing the screen its focus indicator (O4).
            RectTransform root = (RectTransform)slot.Button.transform;

            // ---- action shelf, docked to the foot --------------------------------
            // A recessed well, and not decoration: AccentPrimary measures 2.99:1 on
            // SurfacePanel - under the 3:1 floor for a non-text boundary - and 3.53:1 on
            // SurfaceInset. The BUY pill therefore lives on an inset shelf, which is also
            // where the price and the state sentence belong.
            Image shelf = UiFactory.NewImage("Shelf", root, UiColor.SurfaceInset, UiSprites.Control);
            var shelfRect = (RectTransform)shelf.transform;
            shelfRect.anchorMin = new Vector2(0f, 0f);
            shelfRect.anchorMax = new Vector2(1f, 0f);
            shelfRect.pivot = new Vector2(0.5f, 0f);
            shelfRect.offsetMin = new Vector2((int)UiSpace.M, (int)UiSpace.M);
            shelfRect.offsetMax = new Vector2(-(int)UiSpace.M, (int)UiSpace.M + ShelfHeight);

            RectTransform shelfHost = UiFactory.NewRect("ShelfContent", shelfRect);
            UiFactory.Fill(shelfHost, (int)UiSpace.L);
            UiFactory.ClipContent(shelfHost);

            var shelfStack = new UiStack(shelfHost);

            RectTransform priceRow = UiFactory.NewRect("PriceRow", shelfHost);
            shelfStack.Add(priceRow, StateChipHeight + 2f);

            slot.Coin = UiFactory.NewImage("Coin", priceRow, UiColor.StateGold, UiSprites.Disc);
            var coinRect = (RectTransform)slot.Coin.transform;
            coinRect.anchorMin = new Vector2(0f, 0.5f);
            coinRect.anchorMax = new Vector2(0f, 0.5f);
            coinRect.pivot = new Vector2(0f, 0.5f);
            coinRect.sizeDelta = new Vector2(CoinSize, CoinSize);
            coinRect.anchoredPosition = Vector2.zero;

            slot.Price = UiFactory.Numeric("Price", priceRow, "0", UiType.NumericM, UiColor.StateGold);
            RectTransform priceRect = UiFactory.Fill((RectTransform)slot.Price.transform);
            priceRect.offsetMin = new Vector2(CoinSize + (int)UiSpace.M, 0f);
            priceRect.offsetMax = new Vector2(-(StateChipWidth + (int)UiSpace.M), 0f);

            slot.StateLabel = UiFactory.Chip(
                "State", priceRow, "BUY", UiColor.AccentPrimary, UiColor.TextOnAccent,
                StateChipWidth, StateChipHeight);
            var stateRect = (RectTransform)slot.StateLabel.transform.parent;
            stateRect.anchorMin = new Vector2(1f, 0.5f);
            stateRect.anchorMax = new Vector2(1f, 0.5f);
            stateRect.pivot = new Vector2(1f, 0.5f);
            stateRect.anchoredPosition = Vector2.zero;
            slot.StateFill = stateRect.GetComponent<Image>();

            shelfStack.Gap(UiSpace.S);

            slot.Sentence = UiFactory.Label(
                "Sentence", shelfHost, string.Empty, UiType.Caption, UiColor.TextSecondary);
            shelfStack.Add((RectTransform)slot.Sentence.transform, UiFactory.LineHeight(UiType.Caption));

            // ---- body -------------------------------------------------------------
            RectTransform body = UiFactory.NewRect("Body", root);
            UiFactory.Fill(body);
            body.offsetMin = new Vector2((int)UiSpace.XL, (int)UiSpace.M + ShelfHeight + (int)UiSpace.M);
            body.offsetMax = new Vector2(-(int)UiSpace.XL, -(int)UiSpace.XL);
            UiFactory.ClipContent(body);

            var bodyStack = new UiStack(body);

            // ---- icon well ---------------------------------------------------
            // Top-left of the body, spanning the eyebrow + title band. A recessed
            // chip that holds either art's sprite or the branch monogram; the same
            // size in both cases, so the text beside it never moves (WP-005).
            BuildIconWell(slot, body);

            slot.Eyebrow = UiFactory.Label(
                "Kind", body, string.Empty, UiType.Label, UiColor.TextSecondary);
            RectTransform eyebrowRect = bodyStack.Add(
                (RectTransform)slot.Eyebrow.transform, UiFactory.LineHeight(UiType.Label));
            eyebrowRect.offsetMin = new Vector2(IconWellSize + (int)UiSpace.M, eyebrowRect.offsetMin.y);

            bodyStack.Gap(UiSpace.XS);

            // The button's own label is the card title. Re-parenting it into the body
            // stack keeps the vertical rhythm in one place and keeps the title's colour
            // driven by UiButton's state machine (TextPrimary enabled, TextSecondary
            // disabled - both audited on SurfacePanel and on SurfaceRaised).
            slot.Title = slot.Button.LabelText;
            slot.Title.alignment = TextAnchor.LowerLeft;
            slot.Title.fontSize = UiTokens.FontSize(UiType.H2);
            RectTransform titleRect = bodyStack.Add(
                (RectTransform)slot.Title.transform, UiFactory.LineHeight(UiType.H2));
            titleRect.offsetMin = new Vector2(IconWellSize + (int)UiSpace.M, titleRect.offsetMin.y);

            bodyStack.Gap(UiSpace.S);

            slot.Tier = UiFactory.Label("Tier", body, string.Empty, UiType.Label, UiColor.TextSecondary);
            bodyStack.Add((RectTransform)slot.Tier.transform, UiFactory.LineHeight(UiType.Label));

            bodyStack.Gap(UiSpace.M);

            slot.Pips = BuildPipLadder(body, ref bodyStack);

            bodyStack.Gap(UiSpace.L);
            bodyStack.Add((RectTransform)UiFactory.Divider("D", body).transform, UiTokens.StrokeHairline);
            bodyStack.Gap(UiSpace.L);

            slot.Describe = UiFactory.Label(
                "Describe", body, string.Empty, UiType.Body, UiColor.TextSecondary);
            bodyStack.Add(
                (RectTransform)slot.Describe.transform,
                UiFactory.LineHeight(UiType.Body) * DescribeLines);

            // ---- purchased badge ---------------------------------------------------
            Text badge = UiFactory.Chip(
                "Badge", root, "PURCHASED", UiColor.StateSuccess, UiColor.SurfaceInset,
                BadgeWidth, BadgeHeight);
            var badgeRect = (RectTransform)badge.transform.parent;
            badgeRect.anchorMin = new Vector2(1f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.anchoredPosition = new Vector2(-(int)UiSpace.XL, -(int)UiSpace.XL);
            slot.Badge = badgeRect.gameObject;
            slot.Badge.SetActive(false);

            slot.Button.gameObject.SetActive(false);
            return slot;
        }

        /// <summary>PROTOTYPE-ONLY: the icon well. Spans the eyebrow and title band (24 + 4 + 36 = 64).</summary>
        private const float IconWellSize = 64f;

        /// <summary>Icon drawn inside the well, inset so the chip's corner stays visible.</summary>
        private const float IconSize = 40f;

        private static void BuildIconWell(CardSlot slot, RectTransform body)
        {
            slot.IconWell = UiFactory.NewImage("IconWell", body, UiColor.SurfaceInset, UiSprites.Chip);
            var wellRect = (RectTransform)slot.IconWell.transform;
            wellRect.anchorMin = new Vector2(0f, 1f);
            wellRect.anchorMax = new Vector2(0f, 1f);
            wellRect.pivot = new Vector2(0f, 1f);
            wellRect.sizeDelta = new Vector2(IconWellSize, IconWellSize);
            wellRect.anchoredPosition = Vector2.zero;

            slot.Icon = UiFactory.NewImage("Icon", wellRect, UiColor.TextPrimary);
            var iconRect = (RectTransform)slot.Icon.transform;
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            slot.Icon.preserveAspect = true;
            slot.Icon.gameObject.SetActive(false);

            // The fallback: the branch's build code, in the label weight, on the well.
            // TextSecondary on SurfaceInset is an audited pair ("chip text in a well").
            slot.Monogram = UiFactory.Label(
                "Monogram", wellRect, string.Empty, UiType.H3, UiColor.TextSecondary, TextAnchor.MiddleCenter);
            UiFactory.Fill((RectTransform)slot.Monogram.transform);
        }

        /// <summary>Sprite when one is registered for the branch, otherwise the monogram.</summary>
        private static void ApplyIcon(CardSlot slot, UpgradeBranch branch)
        {
            Sprite sprite;
            bool hasSprite = UpgradeIcons.TryGet(branch, out sprite);

            if (hasSprite)
            {
                slot.Icon.sprite = sprite;
            }

            if (slot.Icon.gameObject.activeSelf != hasSprite)
            {
                slot.Icon.gameObject.SetActive(hasSprite);
            }

            if (slot.Monogram.gameObject.activeSelf == hasSprite)
            {
                slot.Monogram.gameObject.SetActive(!hasSprite);
            }

            if (!hasSprite)
            {
                slot.Monogram.text = UpgradeIcons.Monogram(branch);
            }
        }

        /// <summary>
        /// The tier ladder: MaxTier pips in a recessed well. Owned tiers are filled in
        /// AccentPrimary (3.53:1 on SurfaceInset), the tier this card sells is marked in
        /// TextSecondary, the rest are a hairline. Buying the card turns one more pip
        /// accent-coloured, which is the screen's instant confirmation that the gold did
        /// something.
        /// </summary>
        private static Image[] BuildPipLadder(RectTransform body, ref UiStack stack)
        {
            Image well = UiFactory.NewImage("TierLadder", body, UiColor.SurfaceInset, UiSprites.Chip);
            RectTransform wellRect = stack.Add((RectTransform)well.transform, PipWellHeight);

            var pips = new Image[MaxPips];
            for (int i = 0; i < MaxPips; i++)
            {
                pips[i] = UiFactory.NewImage("Pip." + i, wellRect, UiColor.BorderHairline, UiSprites.Chip);
                pips[i].gameObject.SetActive(false);
            }

            return pips;
        }

        // =================================================================== chrome

        private void BuildEmptyPanel()
        {
            RectTransform card = UiFactory.Card("EmptyCard", Content, UiColor.BorderHairline, UiColor.SurfacePanel);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(EmptyPanelWidth, EmptyPanelHeight);
            card.anchoredPosition = Vector2.zero;

            RectTransform host = UiFactory.ClipContent(UiFactory.CardContent(card, UiSpace.XXL));
            var stack = new UiStack(host);

            _emptyTitle = UiFactory.Label(
                "Title", host, string.Empty, UiType.H3, UiColor.TextPrimary);
            stack.Add((RectTransform)_emptyTitle.transform, UiFactory.LineHeight(UiType.H3));

            stack.Gap(UiSpace.M);

            _emptyBody = UiFactory.Label(
                "Body", host, string.Empty, UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)_emptyBody.transform, UiFactory.LineHeight(UiType.Body) * 3f);

            _emptyPanel = card.gameObject;
            _emptyPanel.SetActive(false);
        }

        private void BuildFooter()
        {
            RectTransform footer = UiFactory.NewRect("Footer", Content);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, UiTokens.TargetPreferred);
            footer.anchoredPosition = Vector2.zero;

            _footerNote = UiFactory.Label(
                "FooterNote", footer, string.Empty, UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleLeft);
            RectTransform noteRect = UiFactory.Fill((RectTransform)_footerNote.transform);
            noteRect.offsetMax = new Vector2(-(ContinueWidth + (int)UiSpace.XL), 0f);

            _continueButton = UiFactory.Button(
                "Continue", footer, "CONTINUE", UiButtonVariant.Primary, CloseScreen);
            RectTransform rect = (RectTransform)_continueButton.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(ContinueWidth, UiTokens.TargetPreferred);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// The pre-round countdown, printed after this screen has closed.
        ///
        /// The NUMBER IS NOT THIS SCREEN'S: it reads RunEvents.Current.CountdownRemaining,
        /// which RunDirector ticks. A second timer here would drift away from the round it
        /// is describing the moment either side changed.
        ///
        /// It is a second, tiny canvas rather than part of the panel because it is the one
        /// thing here that sits over LIVE gameplay - the world is running again during the
        /// countdown so the player can reposition - so it uses the HUD's near-opaque
        /// HudInk backing and the HUD's already-audited text pairs.
        /// </summary>
        private void BuildCountdownStrip()
        {
            _countdownCanvas = UiFactory.CreateCanvas(
                GetType().Name + ".Countdown", UiTokens.SortingScreen, transform);

            var canvasRect = (RectTransform)_countdownCanvas.transform;

            RectTransform card = UiFactory.Card(
                "Countdown", canvasRect, UiColor.BorderHairline, UiColor.HudInk);
            card.anchorMin = new Vector2(0.5f, 0f);
            card.anchorMax = new Vector2(0.5f, 0f);
            card.pivot = new Vector2(0.5f, 0f);
            card.sizeDelta = new Vector2(CountdownWidth, CountdownHeight);
            card.anchoredPosition = new Vector2(0f, (int)UiSpace.Section);

            RectTransform host = UiFactory.CardContent(card, UiSpace.L);

            Text label = UiFactory.Label(
                "Label", host, "NEXT ROUND IN", UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMax = new Vector2(-100f, 0f);

            _countdownValue = UiFactory.Numeric(
                "Value", host, "3", UiType.NumericL, UiColor.StateGold, TextAnchor.MiddleRight);
            UiFactory.Fill((RectTransform)_countdownValue.transform);

            _countdownCanvas.gameObject.SetActive(false);
        }

        // ==================================================================== state

        private static RoundUpgradeShop Shop
        {
            get { return RunDirector.Instance != null ? RunDirector.Instance.Shop : null; }
        }

        private void Update()
        {
            Bind();

            RunDirector director = RunDirector.Instance;
            RunPhase phase = director != null ? director.Phase : RunPhase.None;

            // Open only for the snapshot of THIS round in THIS run. The director writes
            // LastRoundEnd in the same call that sets the phase, so in normal play the two
            // agree on the first frame; the check is what refuses a stale snapshot after
            // an F1 skip or a director swap.
            bool contextValid = director != null &&
                                director.LastRoundEnd.IsValid &&
                                director.LastRoundEnd.RunId == director.RunId &&
                                director.LastRoundEnd.Round == director.Round;

            SetPanelVisible(phase == RunPhase.Upgrading && contextValid);
            UpdateCountdown(phase);

            if (!_panelVisible)
            {
                return;
            }

            TickFade();
            GateForPause();

            // Redraw only when the shop or the wallet said something changed. The F1
            // panel's own buy / give-gold buttons drive the same instances, so they land
            // here too, without this screen ever polling a balance.
            if (_dirty)
            {
                _dirty = false;
                Refresh();
            }
        }

        private void SetPanelVisible(bool visible)
        {
            if (_panelVisible == visible)
            {
                return;
            }

            _panelVisible = visible;
            Canvas.gameObject.SetActive(visible);

            if (!visible)
            {
                UiNavigation.ExitLayer(UiContext.RoundUpgrade);
                return;
            }

            UiNavigation.EnterLayer(UiContext.RoundUpgrade);

            _notice = null;
            _fade = 0f;
            _panelGroup.alpha = 0f;

            // The session this screen belongs to. Every purchase from here quotes it, so a
            // button that somehow fires after the next round's screen opened is refused.
            RoundUpgradeShop shop = Shop;
            _sessionId = shop != null ? shop.SessionId : 0;

            Refresh();
            _dirty = false;

            FocusFirstUsefulControl();
        }

        /// <summary>
        /// Unscaled fade-in. The round-end freeze runs at timeScale 0, so a scaled fade
        /// would never finish and the screen would open invisible.
        /// </summary>
        private void TickFade()
        {
            if (_fade >= 1f)
            {
                return;
            }

            _fade = Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / UiTokens.MotionScreen);
            _panelGroup.alpha = _fade;
        }

        /// <summary>
        /// The pause screen can be opened on top of this one - it sorts above at
        /// SortingPause. Dropping interactivity here keeps arrow-key focus from walking
        /// into cards the player can no longer see.
        ///
        /// Reads the pause state from <see cref="UiNavigation"/> - a field read - instead
        /// of finding the PauseScreen in the scene (WP-004 hand-off: that search ran
        /// every Upgrading frame and was the source of the idle-frame garbage).
        /// </summary>
        private void GateForPause()
        {
            bool blocked = UiNavigation.IsPaused;

            if (_panelGroup.interactable == !blocked)
            {
                return;
            }

            _panelGroup.interactable = !blocked;
            _panelGroup.blocksRaycasts = !blocked;

            if (!blocked)
            {
                // Back from a pause. The pause screen hands focus back to the card that
                // had it; if nothing usable got it, the screen picks its own again.
                EnsureFocus();
            }
        }

        private void EnsureFocus()
        {
            EventSystem system = EventSystem.current;
            GameObject current = system != null ? system.currentSelectedGameObject : null;

            if (current != null && current.activeInHierarchy && current.transform.IsChildOf(Canvas.transform))
            {
                return;
            }

            FocusFirstUsefulControl();
        }

        private void UpdateCountdown(RunPhase phase)
        {
            bool show = phase == RunPhase.Countdown;

            if (_countdownCanvas.gameObject.activeSelf != show)
            {
                _countdownCanvas.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            int seconds = Mathf.Max(0, Mathf.CeilToInt(RunEvents.Current.CountdownRemaining));
            _countdownValue.text = seconds.ToString();
        }

        // ================================================================== refresh

        private void Refresh()
        {
            RoundUpgradeShop shop = Shop;

            // LOADING. The phase says Upgrading but the offer is not built - one frame in
            // normal play, or a scene opened with no PrototypeConfig. It is named rather
            // than left as an empty screen.
            if (shop == null || !shop.IsOpen)
            {
                ShowNotice(
                    "Preparing the offer",
                    "The round-end offer is still being built. It will appear in a moment; " +
                    "continuing now simply starts the next round.");
                RefreshFooter(0, 0);
                return;
            }

            int count = shop.Cards.Count;

            // EMPTY. Every branch is at its maximum tier, or the economy section offers
            // none. A finished sentence, and CONTINUE still works.
            if (count <= 0)
            {
                ShowNotice(
                    PrototypeConfig.Active != null
                        ? "Every branch is at maximum tier"
                        : "No upgrade table is loaded",
                    PrototypeConfig.Active != null
                        ? "Every upgrade branch is already at its maximum tier, so this round has no card " +
                          "to offer. Your gold carries into the next round."
                        : "PrototypeConfig is not loaded, so there is no upgrade table to draw cards from. " +
                          "The run continues normally.");
                RefreshFooter(shop.Gold, shop.PurchasesThisRound);
                return;
            }

            _emptyPanel.SetActive(false);

            EnsureSlots(count);

            int affordable = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (i >= count)
                {
                    _slots[i].Button.gameObject.SetActive(false);
                    continue;
                }

                UpgradeCardOffer card = shop.Cards[i];
                bool canAfford = shop.CanAfford(i);
                if (canAfford)
                {
                    affordable++;
                }

                PlaceCard((RectTransform)_slots[i].Button.transform, i, count);
                ApplyCard(_slots[i], card, canAfford, shop.Gold);
            }

            RefreshGoldStrip(shop, affordable, count);
            RefreshFooter(shop.Gold, shop.PurchasesThisRound);
        }

        /// <summary>
        /// Cards on fractional anchors, gapped by exactly one UiSpace.XL between
        /// neighbours (UiSpace.M on each inner edge). One, two or three cards all fill the
        /// row without a second layout.
        /// </summary>
        private void PlaceCard(RectTransform rect, int index, int count)
        {
            float from = index / (float)count;
            float to = (index + 1) / (float)count;

            rect.gameObject.SetActive(true);
            rect.anchorMin = new Vector2(from, 1f);
            rect.anchorMax = new Vector2(to, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(
                index == 0 ? 0f : (int)UiSpace.M,
                -(_cardsTop + CardHeight));
            rect.offsetMax = new Vector2(
                index == count - 1 ? 0f : -(int)UiSpace.M,
                -_cardsTop);
        }

        private void ApplyCard(CardSlot slot, UpgradeCardOffer card, bool canAfford, int gold)
        {
            if (!card.IsValid)
            {
                slot.Button.gameObject.SetActive(false);
                return;
            }

            slot.Eyebrow.text = card.Kind == UpgradeCardKind.WeaponCore ? "WEAPON CORE" : "UTILITY";
            ApplyIcon(slot, card.Branch);
            slot.Button.SetText(UiFactory.Clip(card.TitleWithTier, 22));
            slot.Tier.text = DescribeTierStep(card);

            // Verbatim from UpgradeCardCatalog.Describe(), clipped only so a surprise
            // 400-character line truncates instead of reflowing the card.
            slot.Describe.text = UiFactory.Clip(card.Description, DescribeMaxCharacters);

            ApplyPips(slot, card);

            slot.Price.text = card.Cost.ToString();

            bool purchased = card.Purchased;
            bool maxed = card.IsMaxed && !purchased;

            slot.Badge.SetActive(purchased);
            slot.Button.SetInteractable(canAfford);

            if (purchased)
            {
                SetChip(slot, "PURCHASED", UiColor.StateSuccess, UiColor.SurfaceInset);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = "Bought this round. It is already applied to your weapon.";
                slot.Sentence.color = UiTokens.Get(UiColor.StateSuccess);
                return;
            }

            if (maxed)
            {
                SetChip(slot, "MAX TIER", UiColor.SurfaceRaised, UiColor.TextSecondary);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = "This branch is already at its maximum tier.";
                slot.Sentence.color = UiTokens.Get(UiColor.TextSecondary);
                return;
            }

            if (!canAfford)
            {
                SetChip(slot, "TOO EXPENSIVE", UiColor.SurfaceRaised, UiColor.TextSecondary);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = "You need " + Mathf.Max(0, card.Cost - gold) + " more gold for this one.";
                slot.Sentence.color = UiTokens.Get(UiColor.StateDanger);
                return;
            }

            SetChip(slot, "BUY", UiColor.AccentPrimary, UiColor.TextOnAccent);
            slot.Price.color = UiTokens.Get(UiColor.StateGold);
            slot.Coin.color = UiTokens.Get(UiColor.StateGold);
            slot.Sentence.text = "Leaves you " + Mathf.Max(0, gold - card.Cost) + " gold.";
            slot.Sentence.color = UiTokens.Get(UiColor.TextSecondary);
        }

        private static void SetChip(CardSlot slot, string text, UiColor fill, UiColor textColor)
        {
            slot.StateLabel.text = text;
            slot.StateLabel.color = UiTokens.Get(textColor);
            slot.StateFill.color = UiTokens.Get(fill);
        }

        private static void ApplyPips(CardSlot slot, UpgradeCardOffer card)
        {
            int max = Mathf.Clamp(card.MaxTier, 1, MaxPips);

            for (int i = 0; i < slot.Pips.Length; i++)
            {
                Image pip = slot.Pips[i];
                bool used = i < max;

                if (pip.gameObject.activeSelf != used)
                {
                    pip.gameObject.SetActive(used);
                }

                if (!used)
                {
                    continue;
                }

                var rect = (RectTransform)pip.transform;
                rect.anchorMin = new Vector2(i / (float)max, 0f);
                rect.anchorMax = new Vector2((i + 1) / (float)max, 1f);
                rect.offsetMin = new Vector2((int)UiSpace.XS, (int)UiSpace.XS);
                rect.offsetMax = new Vector2(-(int)UiSpace.XS, -(int)UiSpace.XS);

                int tier = i + 1;
                UiColor token = tier <= card.OwnedTier
                    ? UiColor.AccentPrimary
                    : tier == card.NextTier && !card.Purchased
                        ? UiColor.TextSecondary
                        : UiColor.BorderHairline;

                pip.color = UiTokens.Get(token);
            }
        }

        private static string DescribeTierStep(UpgradeCardOffer card)
        {
            string owned = card.OwnedTier <= 0
                ? "NOT OWNED"
                : "TIER " + UpgradeCardCatalog.Roman(card.OwnedTier);

            return owned +
                   "  →  TIER " + UpgradeCardCatalog.Roman(card.NextTier) +
                   "  ·  MAX " + UpgradeCardCatalog.Roman(card.MaxTier);
        }

        private void RefreshGoldStrip(RoundUpgradeShop shop, int affordable, int count)
        {
            _goldValue.text = shop.Gold.ToString();
            _goldSpent.text = shop.GoldSpentThisRound.ToString();

            if (affordable <= 0 && count > 0)
            {
                // The 0-gold opening, and every later round where the prices have outrun
                // the wallet. Stated as a fact with a way forward, not as a failure.
                _goldNote.text = shop.Gold <= 0
                    ? "You collected no gold this round, so nothing here can be bought yet. A new offer " +
                      "appears at the end of the next round."
                    : "None of these is affordable yet. Unspent gold carries into the next round.";
                _goldNote.color = UiTokens.Get(UiColor.StateWarning);
                return;
            }

            _goldNote.text = "Choose what to strengthen, then continue.";
            _goldNote.color = UiTokens.Get(UiColor.TextMuted);
        }

        private void RefreshFooter(int gold, int purchases)
        {
            // One fixed label. A button whose caption changes under the focus ring reads
            // as a different control to a keyboard or gamepad player.
            _continueButton.SetText("CONTINUE");

            if (!string.IsNullOrEmpty(_notice))
            {
                // ERROR state. Stated in the footer, never thrown, and it says explicitly
                // that the gold was not taken.
                _footerNote.text = _notice;
                _footerNote.color = UiTokens.Get(UiColor.StateWarning);
                return;
            }

            _footerNote.text = purchases > 0
                ? "Applied immediately. The next shot already uses it. " + gold + " gold carries into the next round."
                : "Nothing bought yet. All " + gold + " gold carries into the next round.";
            _footerNote.color = UiTokens.Get(UiColor.TextMuted);
        }

        private void ShowNotice(string title, string body)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Button.gameObject.SetActive(false);
            }

            _emptyTitle.text = title;
            _emptyBody.text = body;
            _emptyPanel.SetActive(true);

            _goldValue.text = Shop != null ? Shop.Gold.ToString() : "0";
            _goldSpent.text = Shop != null ? Shop.GoldSpentThisRound.ToString() : "0";
            _goldNote.text = "Nothing to spend it on this round. It carries over.";
            _goldNote.color = UiTokens.Get(UiColor.TextMuted);
        }

        // ================================================================== actions

        private void OnCardActivated(int index)
        {
            RoundUpgradeShop shop = Shop;
            if (shop == null)
            {
                return;
            }

            if (shop.TryPurchase(index, _sessionId))
            {
                _notice = null;
            }
            else
            {
                _notice = DescribeRefusal(shop.LastResult);
            }

            // Synchronous redraw so the focus move below sees the card's new state.
            _dirty = false;
            Refresh();
            MoveFocusOffDisabled(index);
        }

        /// <summary>
        /// The footer line for a refused purchase. Every refusal leaves the gold where it
        /// was, and every line says so; a silent no-op would read as a dropped input.
        /// </summary>
        private static string DescribeRefusal(UpgradePurchaseResult result)
        {
            switch (result)
            {
                case UpgradePurchaseResult.InsufficientGold:
                    return "Not enough gold for that upgrade. Nothing was spent.";
                case UpgradePurchaseResult.BranchMaxed:
                    return "That upgrade is already at its maximum tier. Nothing was spent.";
                case UpgradePurchaseResult.NoWeapon:
                    return "That upgrade could not be applied to your weapon, so no gold was spent.";
                case UpgradePurchaseResult.StaleOffer:
                    return "The offer changed. Prices and tiers were refreshed; nothing was spent.";
                case UpgradePurchaseResult.AlreadyPurchased:
                    return "You already own that card. Nothing was spent.";
                case UpgradePurchaseResult.ShopClosed:
                case UpgradePurchaseResult.SessionMismatch:
                case UpgradePurchaseResult.WalletRefused:
                    return "This offer is no longer open. Nothing was spent.";
                default:
                    return "That purchase could not be completed. Nothing was spent.";
            }
        }

        private void CloseScreen()
        {
            if (RunDirector.Instance == null)
            {
                return;
            }

            RunDirector.Instance.CloseUpgradeScreen();
        }

        /// <summary>
        /// Focus goes to the first card the player can actually buy, and to CONTINUE when
        /// none of them is affordable. Opening with focus on a dead control is the exact
        /// state that reads as "the screen is not responding".
        /// </summary>
        private void FocusFirstUsefulControl()
        {
            RoundUpgradeShop shop = Shop;

            if (shop != null)
            {
                for (int i = 0; i < _slots.Length && i < shop.Cards.Count; i++)
                {
                    if (_slots[i].Button.gameObject.activeSelf && shop.CanAfford(i))
                    {
                        Focus(_slots[i].Button);
                        return;
                    }
                }
            }

            Focus(_continueButton);
        }

        /// <summary>
        /// After a purchase the card that was just bought is disabled. The EventSystem
        /// keeps a disabled object selected, so focus has to be moved by hand or a
        /// keyboard player is stranded.
        /// </summary>
        private void MoveFocusOffDisabled(int index)
        {
            if (index >= 0 && index < _slots.Length && _slots[index].Button.IsInteractable())
            {
                return;
            }

            FocusFirstUsefulControl();
        }

        private static void Focus(Selectable selectable)
        {
            if (selectable != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }
    }
}
