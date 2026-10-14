using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Rounds;
using TMPro;
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
    /// PRINTS <c>RunEvents.Current.CountdownRemaining</c> after it has closed. Since
    /// WP-008 the screen states the two product rules it enforces (DEC-048: one purchase
    /// per round, the offer locks; DEC-050: one paid reroll before the purchase) and
    /// draws the reroll control in the gold strip; it still decides nothing itself.
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
    /// WP-098B (brush kit KILAVUZ §3 "Raund sonu / yükseltme"; AP-028 §3.8): the screen is the kit's window - the ink-wash
    /// wallpaper under the kit's Wallpaper scrim (C1, linear 0.71), two C2 washes (the tree column and the detail column,
    /// abutting, so every word stands on scrim ⊕ wash ≥ 0.945), the kit's C5 title (Menu.Upgrade glyph + ROUND COMPLETE,
    /// its rule and title brush). <see cref="UiBrushPane"/> opens it (input at once, C1 Fast, washes and the title brush ink
    /// in, the content rises) and closes it (input off at once, a Fast fade, the canvas off when the kit's close cue has
    /// run) - no hand-rolled fade. The gold strip is open words on the wash (no card); REROLL is the kit's C11 in the footer
    /// (64 high, so its hit is ≥ 48), and the pad walks the tree in the order nodes → BUY / PIN → REROLL → CONTINUE (G6).
    /// The tree (UpgradeTree.cs: nodes, seals, detail) is unchanged. Trace budget: title brush 1 + washes 2 = 3.
    ///
    /// FIVE STATES
    ///   normal    three cards, at least one affordable
    ///   locked    one card PURCHASED, the other two OFFER LOCKED, reroll closed, focus on
    ///             CONTINUE (WP-008, DEC-048)
    ///   error     the shop could not build three valid cards (RoundUpgradeShop.Status ==
    ///             ConfigurationError): a finished sentence saying no gold was taken, and
    ///             a working CONTINUE, never a blank rectangle (WP-008, decision 17)
    ///   loading   phase says Upgrading but the offer is not built yet (one frame, or a
    ///             scene opened without PrototypeConfig). Named, not blank
    ///   error     a purchase the shop refused - stated in the footer, gold untouched
    ///   dense     0 gold (every card disabled, reroll disabled, CONTINUE still works and
    ///             takes the initial focus), over-long card text, five-figure gold
    /// </summary>
    public sealed class UpgradeScreen : UiScreen
    {
        private const string FrozenOwner = "upgrade";

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

        /// <summary>PROTOTYPE-ONLY: gold banner under the header (open words on the wash since WP-098B; the height is kept so
        /// the tree's host keeps its room - RunTreeScreenTests' stacked / compact thresholds).</summary>
        private const float GoldStripHeight = 128f;

        /// <summary>
        /// WP-098B: where the two C2 washes meet - the middle of the gutter between the tree (8 columns, 0.657 of the host)
        /// and the detail card (from 0.672), so the cores abut and no word stands on the bare Wallpaper scrim.
        /// </summary>
        private const float WashSplit = 0.6645f;

        /// <summary>The window's kit pane (its root under the page grid; its C1 on the screen root).</summary>
        public const string PaneName = "UpgradePane";

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

        /// <summary>PROTOTYPE-ONLY: the countdown strip that survives the screen's close.</summary>
        private const float CountdownWidth = 380f; // AD v005 MUST-3 (360 + the frame's notches)

        private const float CountdownHeight = 56f;

        /// <summary>Lines reserved for Describe(). The box truncates; it never grows.</summary>
        private const int DescribeLines = 4;

        /// <summary>Hard clip on the card sentence before it is even laid out.</summary>
        private const int DescribeMaxCharacters = 200;

        /// <summary>Empty-state panel.</summary>
        private const float EmptyPanelWidth = 760f;

        private const float EmptyPanelHeight = 260f;

        // ----------------------------------------------------------------- runtime

        private CanvasGroup _panelGroup;
        private UiBrushPane _pane;
        private RectTransform _page;          // the pane's clipped content: every block of the window lives here
        private CanvasGroup _ground;          // the wallpaper (outside the pane): it fades out with the window's close
        private bool _closing;                // the pane is fading out; the canvas goes off when its root has
        private UiBrushTitle _title;

        private Canvas _countdownCanvas;
        private TMP_Text _countdownValue;

        private CardSlot[] _slots = new CardSlot[0];
        private RectTransform _cardsHost;
        private float _cardsTop;

        /// <summary>WP-068 R1-C: the in-run tree, shown instead of the card row while the shop draws node cards (WP-077 K10a).</summary>
        private UiRunTreePanel _tree;

        /// <summary>
        /// WP-104 B4 (S-09): the feature-based tree screen, built on its first open, drawn over the whole canvas instead of this
        /// window's page while <see cref="UiTreeScreenRoute"/> names a provider (KARAR-WP104-11.10 decision 1). Until S-04 B1 no
        /// provider exists and the shipped page / WP-077 panel above is what opens.
        /// </summary>
        private UiTreeScreenV2 _treeV2;

        /// <summary>The provider this open's V2 tree is bound to (null = the shipped page).</summary>
        private ITreeShopCommands _treeV2Shop;
        private UiButton _undoButton;
        private UiPrompt _continuePrompt;

        private GameObject _emptyPanel;
        private TMP_Text _emptyTitle;
        private TMP_Text _emptyBody;

        private TMP_Text _goldValue;
        private TMP_Text _goldSpent;
        private TMP_Text _goldNote;

        private UiButton _continueButton;
        private TMP_Text _footerNote;
        private TMP_Text _intro;
        private TMP_Text _roundReport;   // DEC-113

        private TMP_Text _rerollLabel;
        private UiButton _rerollButton;

        private bool _panelVisible;
        private bool _dirty = true;
        private int _sessionId;
        private int _revision;

        /// <summary>Frame of the last successful reroll: no purchase is taken in that same frame (the new cards are unseen).</summary>
        private int _rerollFrame = -1;
        private RoundUpgradeShop _boundShop;
        private GoldWallet _boundWallet;
        private string _notice;

        // WP-098B: the DEC-113 line's count-up (see StartTally).
        private readonly RoundTally _tally = new RoundTally();
        private Image _tallyClock;     // disabled; its fill is the count's progress, driven by UiMotion
        private UiTimeline _tallyLine;
        private bool _tallyRolling;
        private int _tallySession = -1;
        private string _roundReportFinal;

        /// <summary>
        /// WP-068 R1-C (user request 25.09): the round-end screen stands on an ink-wash wallpaper - a faded black silhouette of
        /// the ronin before the ruined keep, the horde on the horizon (external render, luminance-clamped: see
        /// UpgradeWallpaperTests). 80 % of its width is shown, so the ronin stands in the gap between the tree and the
        /// detail card and the keep's roofs rise over the header. WP-098B: drawn by <see cref="BuildWallpaper"/> (no v2
        /// veils, petals or foot rule); the kit's Wallpaper scrim lies over it.
        /// </summary>
        public const string UpgradeWallpaper = "UI/Wallpapers/upgrade_wallpaper";

        /// <summary>The share of the wallpaper's width shown, anchored to its bottom-right corner (UiBackdropArt).</summary>
        public const float WallpaperWidth = 0.8f;

        /// <summary>WP-104 B4: the feature-based tree (null until its first open; tests, captures).</summary>
        public UiTreeScreenV2 TreeV2
        {
            get { return _treeV2; }
        }

        /// <summary>WP-104 B4: this round-end open shows the feature-based tree (a provider was resolved at the open).</summary>
        public bool TreeV2Active
        {
            get { return _treeV2Shop != null; }
        }

        /// <summary>The kit's carrier: C1 Wallpaper scrim, two washes, the clipped content (tests).</summary>
        public UiBrushPane Pane
        {
            get { return _pane; }
        }

        /// <summary>WP-098B: no v2 backdrop - the wallpaper is <see cref="BuildWallpaper"/>'s, the scrim the kit's.</summary>
        protected override bool WantsBackdrop
        {
            get { return false; }
        }

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
            BuildWallpaper();

            // KILAVUZ §3: the Wallpaper scrim takes no text by itself - the two column washes (BuildWashes) carry the words,
            // so the pane is built without its own whole-pane wash.
            _pane = UiBrushKit.Pane(ScreenRect, Content, PaneName, UiBrushScrim.Wallpaper, UiBrushWash.None);
            _page = _pane.Content;

            // Header: the kit's title (Menu.Upgrade glyph + ROUND COMPLETE, its rule and brush), then the DEC-107 intro line
            // (re-written on every open). U1 (fewer letters): "SPEND YOUR GOLD" left with the v2 header - the gold strip says it.
            _title = UiBrushKit.Title(_page, "shop.eyebrow", "ROUND COMPLETE", "Menu.Upgrade");
            _cardsTop = _title.Height + (int)UiSpace.S;
            _intro = UiFactory.Label("Intro", _page, IntroSentence(), UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.UpperLeft);
            _intro.enableWordWrapping = false;
            _intro.overflowMode = TextOverflowModes.Ellipsis;
            float introLine = UiFactory.LineHeight(UiType.MenuSmall);
            TopStrip(_intro.rectTransform, _cardsTop, introLine);
            _cardsTop += introLine;
            BuildRoundReport();

            // The gold strip starts no higher than the v2 header ended: the brush header is 9 px shorter at every TextScale,
            // and the tree's host must keep the height its stacked / compact switch was set against (Mac 1.2 × 1.25: 671 px,
            // ~510 on the Mac aspect, compacts; 1080p: 923, stacked - RunTreeScreenTests). The difference is air above the strip.
            _cardsTop = Mathf.Max(_cardsTop + (int)UiSpace.XL, V2HeaderBand());

            BuildGoldStrip(_cardsTop);
            _cardsTop += GoldStripHeight + (int)UiSpace.XL;

            _cardsHost = _page;

            BuildEmptyPanel();
            BuildFooter();
            BuildTree();
            BuildWashes();
            BuildCountdownStrip();

            Canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// The ground under the window: a base fill (a build without the art keeps a plain ink ground) and the wallpaper,
        /// cover-fit at <see cref="WallpaperWidth"/>. Static; its CanvasGroup fades it with the window's close.
        /// </summary>
        private void BuildWallpaper()
        {
            RectTransform ground = UiFactory.NewRect("Backdrop", ScreenRect);
            UiFactory.Fill(ground);
            ground.SetAsFirstSibling();
            _ground = UiFactory.Group(ground.gameObject);
            _ground.interactable = false;
            _ground.blocksRaycasts = false;

            Image baseFill = UiFactory.NewImage("Base", ground, UiColor.SurfaceBase);
            UiFactory.Fill((RectTransform)baseFill.transform);

            Texture2D art = Resources.Load<Texture2D>(UpgradeWallpaper);
            if (art == null)
            {
                return;
            }

            var artObject = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(UiBackdropArt));
            var artRect = (RectTransform)artObject.transform;
            artRect.SetParent(ground, false);
            UiFactory.Fill(artRect);
            RawImage image = artObject.GetComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
            artObject.GetComponent<UiBackdropArt>().Configure(WallpaperWidth);
        }

        /// <summary>
        /// KILAVUZ §3 (C2 × 2): one wash under the tree column, one under the detail column, each the page's full height (the
        /// header, the gold strip and the footer stand in them too) and abutting at <see cref="WashSplit"/>. The kit lays a
        /// wash from its column's rect once, at build, so the columns are laid out first.
        /// </summary>
        private void BuildWashes()
        {
            RectTransform tree = WashColumn("TreeColumn", 0f, WashSplit);
            RectTransform detail = WashColumn("DetailColumn", WashSplit, 1f);
            UiBrushKit.Wash(_pane, tree);
            UiBrushKit.Wash(_pane, detail);
        }

        /// <summary>
        /// The v2 header's band (UiScreen.BuildHeader: eyebrow Label + XS + H1 title + S + Body subtitle + L + the hairline
        /// rule, then XXL to the gold strip), from the same line heights at the live TextScale. The tree host's room - and so
        /// UiRunTreePanel's stacked / compact switch - was measured against it; Build keeps the gold strip no higher.
        /// </summary>
        private static float V2HeaderBand()
        {
            return UiFactory.LineHeight(UiType.Label) + (int)UiSpace.XS + UiFactory.LineHeight(UiType.H1) + (int)UiSpace.S
                   + UiFactory.LineHeight(UiType.Body) + (int)UiSpace.L + UiTokens.StrokeHairline + (int)UiSpace.XXL;
        }

        private RectTransform WashColumn(string name, float from, float to)
        {
            RectTransform column = UiFactory.NewRect(name, _page);
            column.anchorMin = new Vector2(from, 0f);
            column.anchorMax = new Vector2(to, 1f);
            column.offsetMin = Vector2.zero;
            column.offsetMax = Vector2.zero;
            return column;
        }

        /// <summary>A full-width strip <paramref name="height"/> tall, <paramref name="top"/> below its parent's top.</summary>
        private static void TopStrip(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
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
                StopTally();
                UiFrozenFrame.Release(FrozenOwner);
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

        /// <summary>
        /// The gold strip. WP-098B: open words on the washes - no card, no frame (KILAVUZ §1 "iz, yüzey değil"); TextSecondary
        /// captions (TextMuted is no text colour on a brush window). Four zones, none across the washes' seam: GOLD AVAILABLE
        /// (the hero number, the economy's gold), SPENT THIS ROUND and the reroll's price / state line on the tree column; the
        /// advisory sentence on the detail column. The REROLL control itself is the footer's (G6).
        /// </summary>
        private void BuildGoldStrip(float top)
        {
            RectTransform host = UiFactory.NewRect("GoldStrip", _page);
            host.anchorMin = new Vector2(0f, 1f);
            host.anchorMax = new Vector2(1f, 1f);
            host.pivot = new Vector2(0.5f, 1f);
            host.offsetMin = new Vector2(0f, -(top + GoldStripHeight));
            host.offsetMax = new Vector2(0f, -top);

            // ---- available gold, the hero number of this screen ------------------
            RectTransform available = Zone(host, 0f, 0.26f);
            var availableStack = new UiStack(available);

            TMP_Text label = UiFactory.Label(
                "AvailableLabel", available, UiStrings.Get("shop.gold_available", "GOLD AVAILABLE"), UiType.Label, UiColor.TextSecondary);
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
            RectTransform spent = Zone(host, 0.28f, 0.46f);
            var spentStack = new UiStack(spent);

            TMP_Text spentLabel = UiFactory.Label(
                "SpentLabel", spent, UiStrings.Get("shop.spent", "SPENT THIS ROUND"), UiType.Label, UiColor.TextSecondary);
            spentStack.Add((RectTransform)spentLabel.transform, UiFactory.LineHeight(UiType.Label));

            spentStack.Gap(UiSpace.XS);

            _goldSpent = UiFactory.Numeric("SpentValue", spent, "0", UiType.NumericM, UiColor.TextPrimary);
            spentStack.Add((RectTransform)_goldSpent.transform, HeroValueHeight);

            // ---- the reroll's price and state (WP-008, DEC-050) ------------------
            // The one paid reroll of the round. The control's caption is fixed ("REROLL", in the footer); the price and the
            // state (used / locked / too expensive) are this line - beside the gold they are paid from - and the control's
            // tooltip, so the control never changes its name under the focus brackets.
            RectTransform reroll = Zone(host, 0.48f, WashSplit - 0.01f);
            _rerollLabel = UiFactory.Label(
                "RerollLabel", reroll, UiStrings.Get("shop.reroll", "REROLL"), UiType.Label, UiColor.TextSecondary, TextAnchor.UpperLeft);
            TopStrip(_rerollLabel.rectTransform, 0f, UiFactory.LineHeight(UiType.Label) * 2f);

            // ---- advisory line ---------------------------------------------------
            // Carries the 0-gold case. It is a finished sentence, not an error: a player
            // who cannot afford anything has still done nothing wrong.
            RectTransform note = Zone(host, WashSplit + 0.01f, 1f);
            _goldNote = UiFactory.Label(
                "Note", note,
                UiStrings.Get("shop.note.choose", "Choose what to strengthen, then continue."),
                UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
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
            public TMP_Text Eyebrow;
            public TMP_Text Title;
            public TMP_Text Tier;
            public TMP_Text Describe;
            public TMP_Text Price;

            /// <summary>The pill on the shelf: BUY / TOO EXPENSIVE / PURCHASED / OFFER LOCKED / MAX TIER.</summary>
            public TMP_Text StateLabel;

            /// <summary>The pill's fill. Recoloured with the label so the pair stays audited.</summary>
            public Image StateFill;

            /// <summary>The one-sentence explanation under the pill.</summary>
            public TMP_Text Sentence;

            public Image[] Pips;
            public GameObject Badge;
            public Image Coin;

            /// <summary>Icon well in the body's top-left. Sprite when art registered one, monogram otherwise.</summary>
            public Image IconWell;
            public Image Icon;
            public TMP_Text Monogram;
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
                "State", priceRow, UiStrings.Get("tree.action.buy", "BUY"), UiColor.AccentPrimary, UiColor.TextOnAccent,
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
            slot.Title.alignment = TextAlignmentOptions.BottomLeft;
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
            TMP_Text badge = UiFactory.Chip(
                "Badge", root, UiStrings.Get("shop.purchased", "PURCHASED"), UiColor.StateSuccess, UiColor.SurfaceInset,
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
                UiArt.SetGlyph(slot.Icon, sprite);   // WP-068 G1: SDF draw path
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
        /// cream (TextPrimary; 12b: AccentPrimary is the one Primary's alone, §2.1), the tier this
        /// card sells is marked in TextSecondary, the rest are a hairline. Buying the card turns
        /// one more pip cream, which is the screen's instant confirmation that the gold did
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
            RectTransform card = UiFactory.FramedCard("EmptyCard", _page, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
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

        /// <summary>
        /// The footer row: the note, then (right to left) CONTINUE, UNDO LAST (tree mode, after a purchase) and REROLL.
        /// WP-098B (G6): REROLL is the kit's C11 here - 64 high (its hit ≥ 48), as wide as its word, focus = the brackets -
        /// so the pad's order reads nodes → BUY / PIN → REROLL → CONTINUE down the screen. WP-098B M3 (KILAVUZ §1 vermilion
        /// reserve, §4.1 C11 "no difference between primary and secondary"): CONTINUE is a C11 too - glyph + MenuBody, no
        /// fill, no frame; its Submit glyph stands outside its left edge and shows while BUY is not the live action
        /// (SyncPrimary). UNDO keeps its variant. The row is the kit's control height; the
        /// tree's host ends S above it (the room it had over the 48 px row + XL).
        /// </summary>
        private void BuildFooter()
        {
            RectTransform footer = UiFactory.NewRect("Footer", _page);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, UiBrushTokens.ControlHeight);
            footer.anchoredPosition = Vector2.zero;

            _footerNote = UiFactory.Label(
                "FooterNote", footer, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Fill((RectTransform)_footerNote.transform);

            _continueButton = UiBrushKit.Button(footer, "Continue", "shop.continue", "CONTINUE", ContinueGlyph, CloseScreen);
            _continuePrompt = UiRunTreePanel.SubmitGlyphBeside(_continueButton);
            RectTransform rect = (RectTransform)_continueButton.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;   // the kit's measured width and 64 height

            // WP-068 R1-C (WP-077 §2.5): UNDO LAST beside CONTINUE - it is about the offer, not a node. Full refund
            // until the player continues; shown only while the shop offers it.
            _undoButton = UiFactory.Button(
                "Undo", footer, UiStrings.Get("tree.action.undo", "UNDO LAST"), UiButtonVariant.Secondary, OnUndoActivated);
            RectTransform undo = (RectTransform)_undoButton.transform;
            undo.anchorMin = new Vector2(1f, 0.5f);
            undo.anchorMax = new Vector2(1f, 0.5f);
            undo.pivot = new Vector2(1f, 0.5f);
            undo.sizeDelta = new Vector2(UndoWidth, UiTokens.TargetPreferred);
            _undoButton.gameObject.SetActive(false);   // placed by LayoutFooter, left of CONTINUE and its glyph

            _rerollButton = UiBrushKit.Button(footer, "Reroll", "shop.reroll", "REROLL", null, OnRerollActivated);
            RectTransform reroll = (RectTransform)_rerollButton.transform;
            reroll.anchorMin = new Vector2(1f, 0.5f);
            reroll.anchorMax = new Vector2(1f, 0.5f);
            reroll.pivot = new Vector2(1f, 0.5f);
            LayoutFooter(false);
        }

        private const float UndoWidth = 260f;

        /// <summary>WP-098B M3: CONTINUE's leading glyph (C11 = glyph + word).</summary>
        internal const string ContinueGlyph = "Menu.Next";

        /// <summary>
        /// WP-068 R1-C: the tree takes the card row's place (and its height, down to the footer). Built once, shown
        /// only while <see cref="RoundUpgradeShop.NodeCardsActive"/>.
        /// </summary>
        private void BuildTree()
        {
            RectTransform host = UiFactory.NewRect("TreeHost", _page);
            host.anchorMin = new Vector2(0f, 0f);
            host.anchorMax = new Vector2(1f, 1f);
            host.offsetMin = new Vector2(0f, UiBrushTokens.ControlHeight + (int)UiSpace.S);
            host.offsetMax = new Vector2(0f, -_cardsTop);
            _tree = UiRunTreePanel.Create(host);
            _tree.Refused = OnTreeRefused;
            _tree.Changed = OnTreeChanged;
            _tree.DescribePurchase = DescribeRefusal;
            _tree.DetailShown = OnTreeDetailShown;
            host.gameObject.SetActive(false);
        }

        /// <summary>
        /// One main action per screen (AD R1-C MUST-1), carried by the Submit glyph, not by a fill (WP-098B M3, KILAVUZ §1:
        /// vermilion is the C8's alone, C11 has no primary / secondary look): while the tree details a node BUY can buy, the
        /// glyph is BUY's and CONTINUE shows none; otherwise CONTINUE carries it. The default focus does the rest.
        /// </summary>
        private void SyncPrimary()
        {
            bool buy = TreeMode && _tree.BuyLive;
            if (_continuePrompt != null && _continuePrompt.gameObject.activeSelf == buy)
            {
                _continuePrompt.gameObject.SetActive(!buy);
            }
        }

        /// <summary>The detail card follows the focused node: the one Primary, and the footer row's way back up (G6).</summary>
        private void OnTreeDetailShown()
        {
            SyncPrimary();
            if (TreeMode)
            {
                LinkTreeNavigation();
            }
        }

        private bool TreeMode
        {
            get { return _tree != null && _tree.transform.parent.gameObject.activeSelf; }
        }

        private void SetTreeMode(bool on)
        {
            GameObject host = _tree.transform.parent.gameObject;
            if (host.activeSelf != on)
            {
                host.SetActive(on);
            }
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
        private RectTransform _countdownCard;

        private void BuildCountdownStrip()
        {
            _countdownCanvas = UiFactory.CreateCanvas(
                GetType().Name + ".Countdown", UiTokens.SortingScreen, transform);

            var canvasRect = (RectTransform)_countdownCanvas.transform;

            // AD v005 MUST-3: the same HUD panel frame as the round block (9-slice, gold
            // hairline, corner notches), so the strip reads as HUD, not as a stray card.
            RectTransform card = UiFactory.FramedCard(
                "Countdown", canvasRect, UiColor.BorderHairline, UiColor.HudInk, UiFrame.HudPanel, UiColor.TextSecondary);
            card.anchorMin = new Vector2(0.5f, 0f);
            card.anchorMax = new Vector2(0.5f, 0f);
            card.pivot = new Vector2(0.5f, 0f);
            card.sizeDelta = new Vector2(CountdownWidth, CountdownHeight);
            card.anchoredPosition = new Vector2(0f, (int)UiSpace.Section);
            _countdownCard = card;

            // L padding: the HudPanel frame's corner notches take ~16 px on each side, and
            // the H2 numeral must not touch the frame (bot-run3 upgrade frame).
            RectTransform host = UiFactory.CardContent(card, UiSpace.L);

            TMP_Text label = UiFactory.Label(
                "Label", host, UiStrings.Get("shop.next_round_in", "NEXT ROUND IN"), UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            RectTransform labelRect = UiFactory.Fill((RectTransform)label.transform);
            labelRect.offsetMax = new Vector2(-100f, 0f);

            // Cream, not gold: gold stays with the gold chip and the ammo / reload bar.
            _countdownValue = UiFactory.Numeric(
                "Value", host, "3", UiType.H2, UiColor.TextPrimary, TextAnchor.MiddleRight);
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

            if (_closing && (_treeV2Shop != null ? !_treeV2.Closing : !_pane.Root.gameObject.activeSelf))
            {
                // The kit's close cue has run (the content, the washes and C1 faded and went off) - or the V2 tree's fade: the
                // canvas follows.
                _closing = false;
                if (_treeV2Shop != null)
                {
                    _treeV2.Hide();
                    _treeV2Shop = null;
                }

                Canvas.gameObject.SetActive(false);
            }

            if (!_panelVisible)
            {
                return;
            }

            if (_tallyRolling)
            {
                TickTally();
            }

            GateForPause();
            if (_treeV2Shop != null)
            {
                // WP-104 B4: the V2 tree owns the window; this screen keeps the phase, the freeze, the pause gate and the countdown.
                if (!UiNavigation.IsPaused)
                {
                    _treeV2.HandleKeys();
                }

                if (_dirty)
                {
                    _dirty = false;
                    _treeV2.Refresh();
                }

                return;
            }

            if (TreeMode && !UiNavigation.IsPaused)
            {
                _tree.HandleKeys();
            }

            // Redraw only when the shop or the wallet said something changed. The F1
            // panel's own buy / give-gold buttons drive the same instances, so they land
            // here too, without this screen ever polling a balance.
            if (_dirty)
            {
                _dirty = false;
                Refresh();
                EnsureFocus();
            }
        }

        private void SetPanelVisible(bool visible)
        {
            if (_panelVisible == visible)
            {
                return;
            }

            _panelVisible = visible;

            if (!visible)
            {
                StopTally();
                UiFrozenFrame.Release(FrozenOwner);
                UiNavigation.ExitLayer(UiContext.RoundUpgrade);
                if (_treeV2Shop != null)
                {
                    // WP-104 B4: the V2 tree fades (input off at once); Update turns the canvas off when its fade has run.
                    _treeV2.Close();
                    UiMotion.Alpha(_ground, 0f, UiBrushTokens.Close, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, GetInstanceID());
                    _closing = true;
                    return;
                }

                // WP-098B: the kit closes the window - input off at once, the content, washes and C1 fade (Fast, Exit, on
                // the unscaled clock) and its cue takes the pane off; the wallpaper fades with them, and Update turns the
                // canvas off once the pane has gone (Reduced Motion: in the same call / the next frame).
                _pane.Close();
                UiMotion.Alpha(_ground, 0f, UiBrushTokens.Close, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, GetInstanceID());
                _closing = true;
                return;
            }

            _closing = false;
            Canvas.gameObject.SetActive(true);
            UiMotion.KillTarget(_ground, false);
            _ground.alpha = 1f;

            UiFrozenFrame.Hold(FrozenOwner);   // WP-068 slice 4: frozen-frame blur under the shop (C8, E24)
            UiNavigation.EnterLayer(UiContext.RoundUpgrade);

            // WP-104 B4 (S-09): THE tree choice of the round end - UiTreeScreenRoute's provider (S-04 B1, or a capture / test
            // override) opens the feature-based tree over this window; none keeps the shipped page below (B1 not in: the game
            // is unchanged).
            _treeV2Shop = UiTreeScreenRoute.Resolve();
            if (_treeV2Shop != null)
            {
                if (_treeV2 == null)
                {
                    _treeV2 = UiTreeScreenV2.Create(ScreenRect);
                    _treeV2.Continued += OnTreeV2Continued;
                }

                _notice = null;
                _treeV2.Open(_treeV2Shop);
                _dirty = false;
                return;
            }

            _notice = null;
            // WP-098B: the kit opens the window (KILAVUZ §4.2) - the input on this frame (CONTINUE holds the focus until
            // FocusFirstUsefulControl below picks the useful one), C1 in 140 ms, the washes and the title brush ink in over
            // 280 ms, the content rises; all on the unscaled clock (the round-end freeze runs at timeScale 0) and Decor, so
            // Reduced Motion lands it at once. No hand-rolled fade.
            _pane.Open(_continueButton);
            _tree.BeginOpening();

            // The session this screen belongs to. Every purchase from here quotes it, so a
            // button that somehow fires after the next round's screen opened is refused.
            RoundUpgradeShop shop = Shop;
            _sessionId = shop != null ? shop.SessionId : 0;
            _revision = shop != null ? shop.Revision : 0;

            Refresh();
            _dirty = false;

            FocusFirstUsefulControl();
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
                // Still ours - but a control that went dead under the ring (a card after the
                // round's purchase, the reroll after its use) must not keep a hidden focus.
                Selectable selectable = current.GetComponent<Selectable>();
                if (selectable == null || selectable.IsInteractable())
                {
                    return;
                }
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

            // WP-068 slice 0 (D8): above the HUD's bottom-centre block when one is shown.
            float y = Mathf.Max((int)UiSpace.Section, RunHudScreen.BottomCentreTop + (int)UiSpace.S);
            if (!Mathf.Approximately(_countdownCard.anchoredPosition.y, y))
            {
                _countdownCard.anchoredPosition = new Vector2(0f, y);
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
                    UiStrings.Get("shop.preparing.title", "Preparing the offer"),
                    UiStrings.Get("shop.preparing.body", "The round-end offer is still being built. It will appear in a moment; " +
                    "continuing now simply starts the next round."));
                RefreshReroll(null);
                RefreshFooter(0, 0);
                return;
            }

            // Every command from this screen quotes the cards it was drawn with (WP-008).
            _sessionId = shop.SessionId;
            _revision = shop.Revision;
            RefreshRoundReport();
            if (_intro != null)
            {
                // DEC-107: the intro follows this offer's pick limit and names the late-round bonus (built once, the screen
                // would keep the first round's count).
                _intro.text = UiUpgradeCopy.Intro(PrototypeConfig.Active, shop.Round, shop.PurchaseLimit, shop.LateRoundBonus);
            }

            // CONFIGURATION ERROR (decision 17). Three valid cards could not be produced.
            // Nothing was charged, no card is invented, CONTINUE works. Stated plainly.
            if (shop.Status == UpgradeOfferStatus.ConfigurationError)
            {
                ShowNotice(
                    shop.Fault == UpgradeOfferFault.PoolExhausted
                        ? UiStrings.Get("shop.fault.exhausted.title", "Nothing left to upgrade")
                        : UiStrings.Get("shop.fault.invalid.title", "No valid upgrade offer this round"),
                    UiStrings.Format("shop.fault.body_suffix", "value", DescribeFault(shop.Fault),
                        "{value} No gold was taken and nothing is for sale this round. Continue when ready."));
                RefreshReroll(shop);
                RefreshFooter(shop.Gold, shop.PurchasesThisRound);
                return;
            }

            int count = shop.Cards.Count;

            // WP-068 R1-C: node cards on (WP-077 K10a) - the tree is the offer. It stays up with no card left too:
            // the Hearth is bought outside the cards and the pin still plans the next offer.
            if (shop.NodeCardsActive)
            {
                RefreshTree(shop, count);
                return;
            }

            SetTreeMode(false);
            _undoButton.gameObject.SetActive(false);
            SetFooterNoteRoom(false);
            SyncPrimary();

            // EMPTY. Every branch is at its maximum tier, or the economy section offers
            // none. A finished sentence, and CONTINUE still works.
            if (count <= 0)
            {
                ShowNotice(
                    PrototypeConfig.Active != null
                        ? UiStrings.Get("shop.maxed.title", "Every branch is at maximum tier")
                        : UiStrings.Get("shop.no_table.title", "No upgrade table is loaded"),
                    PrototypeConfig.Active != null
                        ? UiStrings.Get("shop.maxed.body", "Every upgrade branch is already at its maximum tier, so this round has no card " +
                          "to offer. Your gold carries into the next round.")
                        : UiStrings.Get("shop.no_table.body", "PrototypeConfig is not loaded, so there is no upgrade table to draw cards from. " +
                          "The run continues normally."));
                RefreshReroll(shop);
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
                ApplyCard(_slots[i], card, canAfford, shop.Gold, shop.IsLocked);
            }

            RefreshGoldStrip(shop, affordable, count);
            RefreshReroll(shop);
            RefreshFooter(shop.Gold, shop.PurchasesThisRound);
            LinkNavigation(count);
        }

        private void RefreshTree(RoundUpgradeShop shop, int count)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Button.gameObject.SetActive(false);
            }

            _emptyPanel.SetActive(false);
            SetTreeMode(true);

            int affordable = 0;
            for (int i = 0; i < count; i++)
            {
                if (shop.CanAfford(i))
                {
                    affordable++;
                }
            }

            _tree.SessionId = _sessionId;
            _tree.Revision = _revision;
            RefreshGoldStrip(shop, affordable, count);
            RefreshReroll(shop);
            bool undo = shop.UndoAvailable;
            _undoButton.gameObject.SetActive(undo);
            if (undo)
            {
                _undoButton.SetText(UiStrings.Get("tree.action.undo", "UNDO LAST") + " · +" + shop.UndoRefund);
            }

            RefreshFooter(shop.Gold, shop.PurchasesThisRound);
            SetFooterNoteRoom(undo);
            // G6 (KILAVUZ §8): nodes → BUY / PIN → REROLL → CONTINUE. Down from the detail's buttons (and from a lane's last
            // node) is REROLL while it can be pressed, else UNDO / CONTINUE; nothing sits above the lane tabs any more.
            Selectable footer = undo ? (Selectable)_undoButton : _continueButton;
            _tree.ExitDown = RerollLive ? _rerollButton : footer;
            _tree.ExitUp = null;
            _treeWasLocked = shop.IsLocked;
            _tree.Refresh(shop);
            LinkTreeNavigation();
            SyncPrimary();
        }

        private bool _treeWasLocked;

        /// <summary>The footer note gives UNDO its room while it shows; REROLL stands left of UNDO (or of CONTINUE).</summary>
        private void SetFooterNoteRoom(bool undo)
        {
            LayoutFooter(undo);
        }

        /// <summary>
        /// Right to left: CONTINUE (the kit's width) and its Submit glyph's room (kept while the glyph is BUY's, so nothing
        /// shifts), UNDO (while shown), REROLL (the kit's width); the note takes what is left.
        /// </summary>
        private void LayoutFooter(bool undo)
        {
            float right = ((RectTransform)_continueButton.transform).sizeDelta.x + UiRunTreePanel.SubmitGlyphRoom() + (int)UiSpace.M;
            ((RectTransform)_undoButton.transform).anchoredPosition = new Vector2(-right, 0f);
            right += undo ? UndoWidth + (int)UiSpace.M : 0f;
            var reroll = (RectTransform)_rerollButton.transform;
            reroll.anchoredPosition = new Vector2(-right, 0f);
            right += reroll.sizeDelta.x + (int)UiSpace.XL;
            var noteRect = (RectTransform)_footerNote.transform;
            noteRect.offsetMax = new Vector2(-right, 0f);
        }

        /// <summary>
        /// Pad map in tree mode (G6: nodes → BUY / PIN → REROLL → CONTINUE): the tree links its own nodes, tabs and detail
        /// buttons, their Down leading to REROLL; REROLL, UNDO and CONTINUE are one footer row (← / →), and Up from it goes
        /// back to the detail's live button (BUY, else PIN), else to the detailed node.
        /// </summary>
        private void LinkTreeNavigation()
        {
            Selectable node = _tree.FocusTarget();
            Selectable detail = Live(_tree.BuyButton) ?? Live(_tree.PinButton) ?? node;
            bool undo = _undoButton.gameObject.activeSelf;
            Selectable reroll = RerollLive ? _rerollButton : null;
            Selectable afterReroll = undo ? (Selectable)_undoButton : _continueButton;
            if (_rerollButton != null)
            {
                Nav(_rerollButton, detail, null, null, afterReroll);
            }

            if (undo)
            {
                Nav(_undoButton, detail, null, reroll, _continueButton);
            }

            Nav(_continueButton, detail, null, undo ? _undoButton : reroll, null);
        }

        /// <summary>REROLL can be pressed now (the shop says so).</summary>
        private bool RerollLive
        {
            get { return _rerollButton != null && _rerollButton.IsInteractable(); }
        }

        /// <summary>A control the pad may land on: shown and pressable, else null.</summary>
        private static Selectable Live(UiButton button)
        {
            return button != null && button.gameObject.activeInHierarchy && button.IsInteractable() ? button : null;
        }

        private static void Nav(Selectable control, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            control.navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right
            };
        }

        private void OnTreeRefused(string sentence)
        {
            _notice = sentence;
            RoundUpgradeShop shop = Shop;
            RefreshFooter(shop != null ? shop.Gold : 0, shop != null ? shop.PurchasesThisRound : 0);
        }

        private void OnTreeChanged()
        {
            _notice = null;
            RoundUpgradeShop shop = Shop;
            if (shop != null && shop.IsLocked && !_treeWasLocked)
            {
                UiEvents.Raise(UiEvent.UpgradeOfferLocked);
            }

            // Synchronous, like a card purchase: the next input meets the new state.
            _dirty = false;
            Refresh();
            EnsureFocus();
        }

        private void OnUndoActivated()
        {
            RoundUpgradeShop shop = Shop;
            if (shop == null)
            {
                return;
            }

            RunTreeCommandResult result = shop.TryUndoLast(_sessionId, _revision);
            if (result != RunTreeCommandResult.Done)
            {
                UiSound.Play(UiSoundKind.Denied);
                OnTreeRefused(UiRunTreePanel.Refusal(result));
                return;
            }

            UiEvents.Raise(UiEvent.ChoiceSelected, "TreeUndo");
            _notice = null;
            _dirty = false;
            Refresh();
            Focus(_tree.FocusTarget() ?? _continueButton);
        }

        /// <summary>
        /// Explicit gamepad map (WP-022, closes WP-008 note N3): cards form a horizontal
        /// chain; Down from any card goes to REROLL when it is pressable, else CONTINUE;
        /// Up from REROLL/CONTINUE returns to the first affordable card; REROLL and
        /// CONTINUE are Left/Right neighbours. Disabled controls are never targets.
        /// </summary>
        private void LinkNavigation(int count)
        {
            var cards = new System.Collections.Generic.List<Selectable>(count);
            for (int i = 0; i < _slots.Length && i < count; i++)
            {
                if (_slots[i].Button.gameObject.activeSelf && _slots[i].Button.IsInteractable())
                {
                    cards.Add(_slots[i].Button);
                }
            }

            Selectable below = _rerollButton != null && _rerollButton.IsInteractable() ? _rerollButton : _continueButton;
            Selectable above = cards.Count > 0 ? cards[0] : below;

            if (cards.Count > 0)
            {
                UiInput.ChainHorizontal(cards.ToArray());
                for (int i = 0; i < cards.Count; i++)
                {
                    UiInput.LinkVertical(cards[i], below, below);
                }
            }

            if (_rerollButton != null)
            {
                UiInput.LinkVertical(_rerollButton, above, above);
                UiInput.LinkHorizontal(_rerollButton, _continueButton, _continueButton);
            }

            if (_continueButton != null)
            {
                UiInput.LinkVertical(_continueButton, above, above);
                Selectable side = _rerollButton != null && _rerollButton.IsInteractable() ? _rerollButton : _continueButton;
                UiInput.LinkHorizontal(_continueButton, side, side);
            }
        }

        /// <summary>The reroll block: price and state in the label, pressable only when the shop says so.</summary>
        private void RefreshReroll(RoundUpgradeShop shop)
        {
            RerollAvailability state = shop != null ? shop.RerollAvailability : RerollAvailability.Unavailable;
            int cost = shop != null ? shop.RerollCost : 0;

            _rerollButton.SetInteractable(state == RerollAvailability.Available);

            // One pattern, at most 24 characters so the label never truncates at 4:3.
            switch (state)
            {
                case RerollAvailability.Available:
                    _rerollLabel.text = UiStrings.Format("shop.reroll.cost", "value", cost.ToString(), "REROLL · {value} GOLD · ONCE");
                    _rerollLabel.color = UiTokens.Get(UiColor.TextPrimary);
                    break;
                case RerollAvailability.Used:
                    _rerollLabel.text = UiStrings.Get("shop.reroll.used", "REROLL · USED THIS ROUND");
                    _rerollLabel.color = UiTokens.Get(UiColor.TextSecondary);
                    break;
                case RerollAvailability.LockedByPurchase:
                    _rerollLabel.text = UiStrings.Get("shop.reroll.locked", "REROLL · LOCKED");
                    _rerollLabel.color = UiTokens.Get(UiColor.TextSecondary);
                    break;
                case RerollAvailability.NoAlternatives:
                    _rerollLabel.text = UiStrings.Get("shop.reroll.nothing", "REROLL · NOTHING TO SWAP");
                    _rerollLabel.color = UiTokens.Get(UiColor.TextSecondary);
                    break;
                case RerollAvailability.TooExpensive:
                    _rerollLabel.text = UiStrings.Format("shop.reroll.need", "value", cost.ToString(), "REROLL · NEED {value} GOLD");
                    _rerollLabel.color = UiTokens.Get(UiColor.TextSecondary);
                    break;
                default:
                    _rerollLabel.text = UiStrings.Get("shop.reroll.unavailable", "REROLL · UNAVAILABLE");
                    _rerollLabel.color = UiTokens.Get(UiColor.TextSecondary);
                    break;
            }

            // WP-098B (C27): the same words on the control while it is lifted (hover or pad focus - a disabled REROLL too),
            // so the price / reason is read where the focus is. Event-time only (a redraw), never per frame.
            UiBrushKit.Tooltip(_rerollButton, null, _rerollLabel.text);
        }

        /// <summary>Player-facing sentence for an offer fault. English, finished, no jargon.</summary>
        private static string DescribeFault(UpgradeOfferFault fault)
        {
            switch (fault)
            {
                case UpgradeOfferFault.NotEnoughEligibleBranches:
                    return UiStrings.Get("shop.fault.pool", "Not enough upgrades are available for your weapon and build right now.");
                case UpgradeOfferFault.PoolExhausted:
                    return UiStrings.Get("shop.fault.maxed", "Every upgrade you can still take is already at its maximum tier.");
                case UpgradeOfferFault.NoUpgradeTable:
                case UpgradeOfferFault.NoConfig:
                    return UiStrings.Get("shop.fault.no_table", "The upgrade table is not loaded.");
                case UpgradeOfferFault.DuplicateBranch:
                    return UiStrings.Get("shop.fault.duplicate", "The upgrade table lists the same upgrade twice.");
                default:
                    return UiStrings.Get("shop.fault.other", "The upgrade offer could not be built.");
            }
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

        private void ApplyCard(CardSlot slot, UpgradeCardOffer card, bool canAfford, int gold, bool offerLocked)
        {
            if (!card.IsValid)
            {
                slot.Button.gameObject.SetActive(false);
                return;
            }

            slot.Eyebrow.text = card.Kind == UpgradeCardKind.WeaponCore ? UiStrings.Get("shop.card.weapon_core", "WEAPON CORE") : UiStrings.Get("shop.card.utility", "UTILITY");
            ApplyIcon(slot, card.Branch);
            slot.Button.SetText(UiFactory.Clip(card.TitleWithTier, 22));
            slot.Tier.text = DescribeTierStep(card);

            // The catalog's live numbers (M-66: TryFacts, the source of UpgradeCardCatalog.Describe) in the language on
            // screen; clipped only so a surprise 400-character line truncates instead of reflowing the card.
            string describe = UiUpgradeCopy.TierDescription(card.Branch, card.OwnedTier, null);
            slot.Describe.text = UiFactory.Clip(string.IsNullOrEmpty(describe) ? card.Description : describe, DescribeMaxCharacters);

            ApplyPips(slot, card);

            slot.Price.text = card.Cost.ToString();

            bool purchased = card.Purchased;
            bool maxed = card.IsMaxed && !purchased;

            slot.Badge.SetActive(purchased);
            slot.Button.SetInteractable(canAfford);

            if (purchased)
            {
                SetChip(slot, UiStrings.Get("shop.purchased", "PURCHASED"), UiColor.StateSuccess, UiColor.SurfaceInset);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = UiStrings.Get("shop.state.purchased", "Bought this round. It is already applied to your weapon.");
                slot.Sentence.color = UiTokens.Get(UiColor.StateSuccess);
                return;
            }

            if (offerLocked)
            {
                // WP-008 (DEC-048): the round's one purchase was made on another card.
                SetChip(slot, UiStrings.Get("shop.state.locked", "OFFER LOCKED"), UiColor.SurfaceRaised, UiColor.TextSecondary);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = PicksPerRound > 1
                    ? UiStrings.Get("shop.state.picks_used", "All picks for this round are used. Continue to the next round.")
                    : UiStrings.Get("shop.state.one_per_round", "One upgrade per round. Continue to the next round.");
                slot.Sentence.color = UiTokens.Get(UiColor.TextSecondary);
                return;
            }

            if (maxed)
            {
                SetChip(slot, UiStrings.Get("shop.state.max", "MAX TIER"), UiColor.SurfaceRaised, UiColor.TextSecondary);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = UiStrings.Get("shop.state.max.body", "This branch is already at its maximum tier.");
                slot.Sentence.color = UiTokens.Get(UiColor.TextSecondary);
                return;
            }

            if (!canAfford)
            {
                SetChip(slot, UiStrings.Get("shop.state.expensive", "TOO EXPENSIVE"), UiColor.SurfaceRaised, UiColor.TextSecondary);
                slot.Price.color = UiTokens.Get(UiColor.TextMuted);
                slot.Coin.color = UiTokens.Get(UiColor.TextMuted);
                slot.Sentence.text = UiStrings.Format("shop.state.need_gold", "value", Mathf.Max(0, card.Cost - gold).ToString(), "You need {value} more gold for this one.");
                slot.Sentence.color = UiTokens.Get(UiColor.StateDanger);
                return;
            }

            SetChip(slot, UiStrings.Get("tree.action.buy", "BUY"), UiColor.AccentPrimary, UiColor.TextOnAccent);
            slot.Price.color = UiTokens.Get(UiColor.StateGold);
            slot.Coin.color = UiTokens.Get(UiColor.StateGold);
            slot.Sentence.text = UiStrings.Format("shop.state.leaves", "value", Mathf.Max(0, gold - card.Cost).ToString(), "Leaves you {value} gold.");
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
                    ? UiColor.TextPrimary
                    : tier == card.NextTier && !card.Purchased
                        ? UiColor.TextSecondary
                        : UiColor.BorderHairline;

                pip.color = UiTokens.Get(token);
            }
        }

        private static string DescribeTierStep(UpgradeCardOffer card)
        {
            string owned = card.OwnedTier <= 0
                ? UiStrings.Get("shop.tier.not_owned", "NOT OWNED")
                : "TIER " + UpgradeCardCatalog.Roman(card.OwnedTier);

            return owned +
                   "  →  TIER " + UpgradeCardCatalog.Roman(card.NextTier) +
                   "  ·  MAX " + UpgradeCardCatalog.Roman(card.MaxTier);
        }

        private void RefreshGoldStrip(RoundUpgradeShop shop, int affordable, int count)
        {
            _goldValue.text = shop.Gold.ToString();
            _goldSpent.text = shop.GoldSpentThisRound.ToString();

            if (shop.IsLocked)
            {
                // The round's purchase is made: every remaining card is locked, not
                // "unaffordable" - say the true thing (probe screenshot 06 caught this).
                _goldNote.text = shop.PurchasesThisRound > 1 ? UiStrings.Get("shop.note.applied_many", "Upgrades applied. Continue when ready.") : UiStrings.Get("shop.note.applied_one", "Upgrade applied. Continue when ready.");
                _goldNote.color = UiTokens.Get(UiColor.TextSecondary);
                return;
            }

            if (affordable <= 0 && count > 0)
            {
                // The 0-gold opening, and every later round where the prices have outrun
                // the wallet. Stated as a fact with a way forward, not as a failure.
                _goldNote.text = shop.Gold <= 0
                    ? UiStrings.Get("shop.note.no_gold", "No gold collected this round, so nothing can be bought yet. A new offer appears next round.")
                    : UiStrings.Get("shop.note.unaffordable", "None of these is affordable yet. Unspent gold carries into the next round.");
                _goldNote.color = UiTokens.Get(UiColor.StateWarning);
                return;
            }

            _goldNote.text = PicksPerRound > 1
                ? (shop.PurchasesThisRound > 0
                    ? UiStrings.Get("shop.note.another", "Choose another upgrade, or continue.")
                    : UiUpgradeCopy.Choose(PicksPerRound))
                : UiStrings.Get("shop.note.choose_one", "Choose one upgrade, or reroll once, then continue.");
            _goldNote.color = UiTokens.Get(UiColor.TextSecondary);
        }

        /// <summary>
        /// WP-022L (Ö-6a, DEC-087): picks per round-end screen. Read from the open shop (the
        /// config limit it was opened with), not cached, so the S-04 value is the one every
        /// sentence on this screen follows. WP-049C2: MaxPurchasesPerRound is the no-config
        /// fallback (1); the shipped value lives in EconomyConfigSection.PurchasesPerRound (2).
        /// </summary>
        private static int PicksPerRound => Shop != null ? Shop.PurchaseLimit : UpgradeOfferPolicy.PurchaseLimit(PrototypeConfig.Active);

        /// <summary>
        /// DEC-113: the Round Report line - one line on the ROUND COMPLETE row, right-aligned, so the header keeps its
        /// height. WP-098B: the row is the kit title's band - the line starts after the title's glyph and words (+ XL) and
        /// never wraps (information only, no focus stop); MenuSmall, TextSecondary, on the washes.
        /// </summary>
        private void BuildRoundReport()
        {
            if (_title.Text == null)
            {
                return;
            }

            _roundReport = UiFactory.Label("RoundReport", _page, string.Empty, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleRight);
            _roundReport.enableWordWrapping = false;
            _roundReport.overflowMode = TextOverflowModes.Ellipsis;
            float band = _title.Height - UiBrushTokens.TitleRuleGap - UiBrushTokens.TitleStrokeHeight;
            float lead = _title.Icon != null ? _title.Icon.rectTransform.sizeDelta.x + (int)UiSpace.L : 0f;
            float words = Mathf.Ceil(_title.Text.GetPreferredValues(_title.Text.text).x);
            RectTransform to = _roundReport.rectTransform;
            to.anchorMin = new Vector2(0f, 1f);
            to.anchorMax = new Vector2(1f, 1f);
            // WP-098B: pivot at the right middle (set before the offsets, so the rect is unchanged) - the landing punch grows
            // the right-aligned line away from its anchored end.
            to.pivot = new Vector2(1f, 0.5f);
            to.offsetMin = new Vector2(lead + words + (int)UiSpace.XL, -band);
            to.offsetMax = new Vector2(0f, 0f);
            _roundReport.gameObject.SetActive(false);

            _tallyClock = UiFactory.NewImage("ReportTally", _page, UiColor.TextSecondary);
            _tallyClock.enabled = false;
        }

        private void RefreshRoundReport()
        {
            if (_roundReport == null)
            {
                return;
            }

            RunDirector run = RunDirector.Instance;
            RoundReport report = run != null ? run.LastRoundReport : default;
            string line = RoundReportLine(report,
                CaptureShowRoundReport || DD.Prototype.Profile.MetaPathRules.Owns(DD.Prototype.Profile.ProfileService.Current, DD.Prototype.Profile.MetaPathEffects.RoundReport, out _));
            _roundReport.gameObject.SetActive(line != null);
            if (line == null)
            {
                StopTally();
                return;
            }

            _roundReportFinal = line;
            if (_tallySession != _sessionId)
            {
                // The first draw of this round's offer: the line counts up. A redraw of the same offer (a purchase, the
                // wallet, a return from pause) leaves a running count alone and never restarts it.
                _tallySession = _sessionId;
                StartTally(report);
                return;
            }

            if (!_tallyRolling)
            {
                _roundReport.text = line;
            }
        }

        // ======================================================== DEC-113 count-up

        /// <summary>WP-098B: the count's start after the panel opens (it rolls while the screen settles in).</summary>
        public const float TallyDelaySeconds = 0.12f;

        /// <summary>WP-098B: one roll for the whole line (Reveal 600 ms, Settle): every number lands together at 0.72 s.</summary>
        public const UiDuration TallyDuration = UiDuration.Reveal;

        /// <summary>
        /// WP-098B: the landing punch of a whole line of words - a third of the hero numbers' TextPop (a 1.06 punch moved
        /// the line's far end ~50 px), around the right baseline.
        /// </summary>
        public const float TallyPunchScale = 1.02f;

        public const float TallyPunchSeconds = 0.12f;

        /// <summary>
        /// WP-098B (DEC-113 data, S-09's line unchanged): the Round Report line counts its numbers 0 → value - kills by type,
        /// damage taken, gold - as one roll on the motion core (Delta), then lands with a short punch and a tok. The Elite
        /// kill time is a time, not a tally: it prints as it is from the first frame (its "—" when no Elite was timed).
        /// The frames in between are written into a char buffer (TMP SetCharArray), so the roll allocates nothing per
        /// frame; the end is <see cref="RoundReportLine"/>'s own string. Reduced Motion: the line at once.
        /// </summary>
        private void StartTally(RoundReport report)
        {
            StopTally();
            if (UiMotion.ReducedMotion || _tallyClock == null)
            {
                _roundReport.text = _roundReportFinal;
                return;
            }

            _tally.Prepare(report);
            _tally.Write(0f);
            _roundReport.SetCharArray(_tally.Buffer, 0, _tally.Length);

            int owner = GetInstanceID();
            float land = TallyDelaySeconds + UiMotionTokens.Seconds(TallyDuration);
            _tallyClock.fillAmount = 0f;
            _tallyLine = UiMotion.Timeline(owner);
            _tallyLine.Add(UiMotion.Fill(_tallyClock, 1f, TallyDuration, UiEase.Settle, UiMotionClass.Delta, TallyDelaySeconds, UiClock.Unscaled, owner));
            _tallyLine.Add(UiMotion.ScaleSeconds(_roundReport.rectTransform, TallyPunchScale, 1f, TallyPunchSeconds, UiEase.Settle, UiMotionClass.Decor, land, owner));
            _tallyLine.Cue(UiEvent.CounterRolling, TallyDelaySeconds).Cue(UiEvent.CounterSettled, land);
            _tallyRolling = true;
        }

        /// <summary>The line at the count's progress; the end writes the exact line and stops.</summary>
        private void TickTally()
        {
            float k = _tallyClock.fillAmount;
            if (k >= 1f)
            {
                _tallyRolling = false;
                _roundReport.text = _roundReportFinal;
                return;
            }

            if (_tally.Write(k))
            {
                _roundReport.SetCharArray(_tally.Buffer, 0, _tally.Length);
            }
        }

        /// <summary>Lands a running count (the panel closed or the line went away): the exact line, the settle cue (the roll voice stops).</summary>
        private void StopTally()
        {
            if (_tallyLine.IsAlive)
            {
                _tallyLine.Complete();
            }

            if (_tallyRolling)
            {
                _tallyRolling = false;
                if (_roundReport != null && _roundReportFinal != null)
                {
                    _roundReport.text = _roundReportFinal;
                }
            }
        }

        /// <summary>
        /// WP-098B: the Round Report line written at a count's progress into a fixed char buffer - the same parts, words
        /// and separator as <see cref="UiUpgradeCopy.RoundReport"/> (UiLanguageTests pins the two equal at k = 1, EN and TR),
        /// each counted number at round(k × value), the Elite kill time part as printed. Built once per round
        /// (<see cref="Prepare"/>); <see cref="Write"/> allocates nothing.
        /// </summary>
        internal sealed class RoundTally
        {
            public const int Capacity = 256;
            private const string Separator = "  ·  ";
            private const string Slot = "{value}";
            private const int MaxCounted = 6;

            private readonly string[] _before = new string[MaxCounted];
            private readonly string[] _after = new string[MaxCounted];
            private readonly int[] _target = new int[MaxCounted];
            private readonly int[] _shown = new int[MaxCounted];
            private int _count;
            private string _tail = string.Empty;

            public readonly char[] Buffer = new char[Capacity];

            public int Length { get; private set; }

            /// <summary>The parts of this round's line in <see cref="UiUpgradeCopy.RoundReport"/>'s order (types with no kill are left out, as there).</summary>
            public void Prepare(RoundReport report)
            {
                _count = 0;
                Add("shop.report.standard", "STANDARD {value}", Mathf.Max(0, report.KillsStandard));
                if (report.KillsSpecial > 0) Add("shop.report.special", "SPECIAL {value}", report.KillsSpecial);
                if (report.KillsElite > 0) Add("shop.report.elite", "ELITE {value}", report.KillsElite);
                if (report.KillsUltra > 0) Add("shop.report.ultra", "ULTRA {value}", report.KillsUltra);
                Add("shop.report.damage", "DAMAGE TAKEN {value}", Mathf.Max(0, Mathf.RoundToInt(report.DamageTaken)));
                Add("shop.report.gold", "GOLD +{value}", Mathf.Max(0, report.GoldIncome));
                string ttk = report.EliteTtkSamples > 0
                    ? UiStrings.Format("stat.fmt.seconds", "value", UiStrings.Number(Mathf.Max(0f, report.EliteTtkAverage), "0.0"), "{value} s")
                    : UiStrings.Get("shop.report.none", "—");
                _tail = UiStrings.Format("shop.report.elite_ttk", "value", ttk, "ELITE KILL TIME {value}");
                for (int i = 0; i < MaxCounted; i++)
                {
                    _shown[i] = int.MinValue;
                }
            }

            private void Add(string key, string fallback, int value)
            {
                string template = UiStrings.Get(key, fallback);
                int at = template.IndexOf(Slot, System.StringComparison.Ordinal);
                _before[_count] = at >= 0 ? template.Substring(0, at) : template;
                _after[_count] = at >= 0 ? template.Substring(at + Slot.Length) : string.Empty;
                _target[_count] = value;
                _count++;
            }

            /// <summary>Writes the line at progress <paramref name="k"/> (0..1); false when no number changed since the last write.</summary>
            public bool Write(float k)
            {
                bool changed = false;
                for (int i = 0; i < _count; i++)
                {
                    int value = k >= 1f ? _target[i] : Mathf.RoundToInt(_target[i] * Mathf.Clamp01(k));
                    if (value != _shown[i])
                    {
                        _shown[i] = value;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return false;
                }

                int n = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (i > 0) n = Put(Separator, n);
                    n = Put(_before[i], n);
                    n = PutNumber(_shown[i], n);
                    n = Put(_after[i], n);
                }

                n = Put(Separator, n);
                n = Put(_tail, n);
                Length = n;
                return true;
            }

            private int Put(string text, int n)
            {
                for (int i = 0; i < text.Length && n < Capacity; i++)
                {
                    Buffer[n++] = text[i];
                }

                return n;
            }

            /// <summary>A non-negative int in invariant digits (UiStrings.Number's form), no string.</summary>
            private int PutNumber(int value, int n)
            {
                int digits = 1;
                for (int v = value; v >= 10; v /= 10)
                {
                    digits++;
                }

                if (n + digits > Capacity)
                {
                    return n;
                }

                for (int i = digits - 1; i >= 0; i--)
                {
                    Buffer[n + i] = (char)('0' + value % 10);
                    value /= 10;
                }

                return n + digits;
            }
        }

        /// <summary>DEC-113: the line for a frozen round report, or null when the node isn't owned or no round has closed yet.</summary>
        public static string RoundReportLine(RoundReport report, bool owned)
        {
            return owned && report.IsValid
                ? UiUpgradeCopy.RoundReport(report.KillsStandard, report.KillsSpecial, report.KillsElite, report.KillsUltra,
                    report.DamageTaken, report.GoldIncome, report.EliteTtkAverage, report.EliteTtkSamples)
                : null;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>Dev capture only (-ddUiRoundReport 1): the line shows without owning the node, for the AD frames (H12).</summary>
        public static bool CaptureShowRoundReport { get; set; }
#else
        private const bool CaptureShowRoundReport = false;
#endif

        private static string IntroSentence()
        {
            return UiUpgradeCopy.Intro(PicksPerRound);
        }

        private void RefreshFooter(int gold, int purchases)
        {
            // One fixed label. A button whose caption changes under the focus ring reads
            // as a different control to a keyboard or gamepad player. WP-098B M3: the C11 is as wide as its word, so a
            // language switch re-measures it and re-lays the footer (never on an unchanged word).
            string word = UiStrings.Get("shop.continue", "CONTINUE");
            if (_continueButton.LabelText.text != word)
            {
                _continueButton.SetText(word);
                UiBrushKit.FitWidth(_continueButton);
                LayoutFooter(_undoButton.gameObject.activeSelf);
            }

            if (!string.IsNullOrEmpty(_notice))
            {
                // ERROR state. Stated in the footer, never thrown, and it says explicitly
                // that the gold was not taken.
                _footerNote.text = _notice;
                _footerNote.color = UiTokens.Get(UiColor.StateWarning);
                return;
            }

            // WP-022H (Ö-6a readiness): the wording follows UpgradeOfferPolicy.MaxPurchasesPerRound
            // and the shop's own lock, so a second pick (when S-04 raises the policy) is
            // announced here without a UI change; with the policy at 1 the text is unchanged.
            int max = PicksPerRound;
            RoundUpgradeShop shop = Shop;
            bool locked = shop == null || shop.IsLocked;
            _footerNote.text = UiUpgradeCopy.Footer(max, gold, purchases, locked);

            _footerNote.color = UiTokens.Get(UiColor.TextSecondary);
        }

        private void ShowNotice(string title, string body)
        {
            SetTreeMode(false);
            _undoButton.gameObject.SetActive(false);
            SetFooterNoteRoom(false);
            SyncPrimary();
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Button.gameObject.SetActive(false);
            }

            _emptyTitle.text = title;
            _emptyBody.text = body;
            _emptyPanel.SetActive(true);

            _goldValue.text = Shop != null ? Shop.Gold.ToString() : "0";
            _goldSpent.text = Shop != null ? Shop.GoldSpentThisRound.ToString() : "0";
            _goldNote.text = UiStrings.Get("shop.note.nothing", "Nothing to spend it on this round. It carries over.");
            _goldNote.color = UiTokens.Get(UiColor.TextSecondary);
        }

        // ================================================================== actions

        private void OnCardActivated(int index)
        {
            RoundUpgradeShop shop = Shop;
            if (shop == null)
            {
                return;
            }

            if (Time.frameCount == _rerollFrame)
            {
                // The offer was rerolled THIS frame: the player has not seen the new cards,
                // so a queued or double input must not buy one. Dropped, nothing spent; the
                // next frame's redraw makes the cards live again.
                UiSound.Play(UiSoundKind.Denied);
                return;
            }

            if (shop.TryPurchase(index, _sessionId, _revision))
            {
                _notice = null;
                UiEvents.Raise(UiEvent.UpgradePurchased);
                if (shop.IsLocked)
                {
                    UiEvents.Raise(UiEvent.UpgradeOfferLocked);
                }
            }
            else
            {
                _notice = DescribeRefusal(shop.LastResult);
            }

            // Synchronous redraw so the focus move below sees the card's new state, and so
            // a second click in the same frame meets a disabled button.
            _dirty = false;
            Refresh();
            MoveFocusOffDisabled(index);
        }

        /// <summary>The reroll button (WP-008). The shop does the whole reroll; this only redraws and reports.</summary>
        private void OnRerollActivated()
        {
            RoundUpgradeShop shop = Shop;
            if (shop == null)
            {
                return;
            }

            bool rerolled = shop.TryReroll(_sessionId, _revision);
            _notice = rerolled ? null : DescribeRerollRefusal(shop.LastRerollResult);
            if (rerolled)
            {
                UiEvents.Raise(UiEvent.UpgradeRerolled);
            }

            _dirty = false;
            Refresh();

            if (rerolled)
            {
                // The new cards have not been SEEN yet: a second input queued in this same
                // frame (a card click, a held submit) must not buy off cards the player never
                // looked at. OnCardActivated drops input for the rest of this frame.
                _rerollFrame = Time.frameCount;
            }

            if (!_rerollButton.IsInteractable())
            {
                FocusFirstUsefulControl();
            }
        }

        /// <summary>The footer line for a refused reroll. Every line says the gold and the reroll were kept.</summary>
        private static string DescribeRerollRefusal(UpgradeRerollResult result)
        {
            switch (result)
            {
                case UpgradeRerollResult.InsufficientGold:
                    return UiStrings.Get("shop.refuse.reroll_gold", "Not enough gold to reroll. Nothing was spent.");
                case UpgradeRerollResult.RerollUsed:
                    return UiStrings.Get("shop.refuse.reroll_used", "You already rerolled this round. Nothing was spent.");
                case UpgradeRerollResult.OfferLocked:
                    return UiStrings.Get("shop.refuse.reroll_locked", "The offer is locked after a purchase. Nothing was spent.");
                case UpgradeRerollResult.NoReplacementAvailable:
                    return UiStrings.Get("shop.refuse.reroll_nothing", "There is no other upgrade to swap in. Nothing was spent and your reroll is kept.");
                case UpgradeRerollResult.InvalidResult:
                case UpgradeRerollResult.InvalidCost:
                    return UiStrings.Get("shop.refuse.reroll_failed", "The reroll could not be built. Nothing was spent and your reroll is kept.");
                case UpgradeRerollResult.ShopClosed:
                case UpgradeRerollResult.SessionMismatch:
                case UpgradeRerollResult.StaleRevision:
                case UpgradeRerollResult.OfferUnavailable:
                case UpgradeRerollResult.WalletRefused:
                    return UiStrings.Get("shop.refuse.closed", "This offer is no longer open. Nothing was spent.");
                default:
                    return UiStrings.Get("shop.refuse.reroll_other", "That reroll could not be completed. Nothing was spent.");
            }
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
                    return UiStrings.Get("shop.refuse.gold", "Not enough gold for that upgrade. Nothing was spent.");
                case UpgradePurchaseResult.BranchMaxed:
                    return UiStrings.Get("shop.refuse.max", "That upgrade is already at its maximum tier. Nothing was spent.");
                case UpgradePurchaseResult.NoWeapon:
                    return UiStrings.Get("shop.refuse.apply", "That upgrade could not be applied to your weapon, so no gold was spent.");
                case UpgradePurchaseResult.StaleOffer:
                    return UiStrings.Get("shop.refuse.changed", "The offer changed. Prices and tiers were refreshed; nothing was spent.");
                case UpgradePurchaseResult.AlreadyPurchased:
                    return UiStrings.Get("shop.refuse.owned", "You already own that card. Nothing was spent.");
                case UpgradePurchaseResult.PurchaseLimitReached:
                    return PicksPerRound > 1
                        ? UiUpgradeCopy.PurchaseLimitReached(PicksPerRound)
                        : UiStrings.Get("shop.refuse.one_per_round", "One upgrade per round. The offer is locked; nothing was spent.");
                case UpgradePurchaseResult.OfferUnavailable:
                    return UiStrings.Get("shop.refuse.no_offer", "There is no valid offer this round. Nothing was spent.");
                case UpgradePurchaseResult.ShopClosed:
                case UpgradePurchaseResult.SessionMismatch:
                case UpgradePurchaseResult.StaleRevision:
                case UpgradePurchaseResult.WalletRefused:
                    return UiStrings.Get("shop.refuse.closed", "This offer is no longer open. Nothing was spent.");
                default:
                    return UiStrings.Get("shop.refuse.other", "That purchase could not be completed. Nothing was spent.");
            }
        }

        /// <summary>
        /// WP-104 B4: the V2 tree's CONTINUE ran the provider's Continue. The round-end flow is the director's: when the provider
        /// did not already end the phase (B1's RunTreeV2 raises ContinueRequested and leaves the flow to its host), this screen
        /// closes it exactly as its own CONTINUE does.
        /// </summary>
        private void OnTreeV2Continued()
        {
            RunDirector director = RunDirector.Instance;
            if (director != null && director.Phase == RunPhase.Upgrading)
            {
                CloseScreen();
            }
        }

        private void CloseScreen()
        {
            if (RunDirector.Instance == null)
            {
                return;
            }

            // WP-077 §2.5: undo lasts until CONTINUE.
            if (Shop != null)
            {
                Shop.CloseUndo();
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
            if (_treeV2Shop != null)
            {
                _treeV2.FocusFirstUseful();
                return;
            }

            RoundUpgradeShop shop = Shop;

            if (TreeMode && _tree.FocusTarget() != null)
            {
                Focus(_tree.FocusTarget());
                return;
            }

            if (shop != null && !shop.IsLocked)
            {
                for (int i = 0; i < _slots.Length && i < shop.Cards.Count; i++)
                {
                    if (_slots[i].Button.gameObject.activeSelf && shop.CanAfford(i))
                    {
                        Focus(_slots[i].Button);
                        return;
                    }
                }

                if (_rerollButton != null && _rerollButton.IsInteractable())
                {
                    Focus(_rerollButton);
                    return;
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
