using System;
using System.Text;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>The tree screen's shortcuts (UiInput.Tree.cs); tests and captures press them through <see cref="UiTreeScreenV2.HandleKey"/>.</summary>
    public enum TreeV2Key
    {
        PreviousBranch = 0,
        NextBranch = 1,
        Synergies = 2,
        Continue = 3,
        Target = 4
    }

    /// <summary>
    /// WP-104 B4 (S-09; this copy: after S-04 B5a - the Hearth's round / run caps) · the feature-based round-end tree (KARAR-WP104-11.10 decision 1: free tree + recommendation strip +
    /// Target, one key to continue). Drawn by the round-end screen (<see cref="UpgradeScreen"/>) instead of the WP-077 panel when
    /// <see cref="UiTreeScreenRoute"/> names a provider; the layout is AD-VISUAL §5 / AP-042 v002's emaki:
    /// <code>
    ///  ┌ header 96: title ·························· [koban GOLD · ASH · target line · SYMBOLS] ┐
    ///  │ I     II     III     IV     V   (fog: UNLOCKS AT ROUND n)  SYNERGIES  │ detail card     │
    ///  │ ATIŞ   ◇◇  ◇◇◇  ◇◇◇◇  ◇    ◇                               ◎          │  preview        │
    ///  │ HIZ    …                                                    ◎          │  name · tier    │
    ///  │ …  (7 rows: Shot · Speed · Magazine · Element · Chaos · Survival · Economy) │ state · words │
    ///  │                                                                        │ price/BUY/TARGET │
    ///  └                                                                        │ strip · REROLL  │
    ///                                                                           │ HEARTH CONTINUE │
    /// </code>
    /// IT OWNS NO RULE: every fact is the snapshot's (<see cref="TreeScreenSnapshot"/>), every action a command
    /// (<see cref="ITreeShopCommands"/>): BUY = TryBuy, TARGET = SetTarget, REROLL = TryReroll, HEARTH = TryStokeHearth, CONTINUE =
    /// Continue. Words: <see cref="TreeV2Text"/>; geometry: <see cref="TreeV2Layout"/>; pad grid: <see cref="TreeV2Nav"/>; art:
    /// <see cref="TreeArt"/> (procedural shapes until AP-042 is imported).
    /// INPUT: pad / keyboard walk the grid (explicit navigation from the layout - every node, fogged ones too, takes the focus:
    /// they are information); A / Enter on a node buys it when it is buyable; LB / RB (Q / E) jump branches and each branch
    /// remembers its node; Y (Tab) the synergy column; X (C) continues from anywhere; T or right-click sets the Target. The mouse:
    /// hover focuses (UiButton) and so fills the detail, a click selects, BUY is its own control. Focus is never lost
    /// (<see cref="HandleKeys"/> reclaims it). The host screen polls <see cref="HandleKeys"/> - this panel has no per-frame loop
    /// (T6) and allocates nothing outside a command, a focus change or a refresh.
    /// ORNAMENTS (UiOrnamentCensusTests, Shop group): no vermilion, no brush trace; the one ceremony is the hanko stamp on a
    /// purchase (a completed synergy stamps its badge instead - one per screen, AD §5.3). Reduced Motion lands every reveal.
    /// </summary>
    public sealed class UiTreeScreenV2 : MonoBehaviour
    {
        public const string RootName = "TreeV2";
        /// <summary>The <see cref="UiEvent.TreeOpened"/> detail.</summary>
        public const string RootEventDetail = "UpgradeTreeV2";
        /// <summary>The open's row stagger (AD §5.3: Row stagger 40 ms after a 120 ms beat, the whole open ≤ 600 ms).</summary>
        public const float OpenBeat = 0.12f;
        public const float RowStagger = 0.04f;
        private const float PlateAlpha = TreeArt.PaperPlateAlpha;
        private const float ActionGap = 8f;
        private const float CardPad = 20f;
        /// <summary>fix1: the card's spacing when its words would not fit otherwise (top pad, gap between texts). Words never shrink.</summary>
        private const float CardPadTight = 8f;
        private const float CardGapTight = 4f;
        private const float HeaderPlateHeight = 72f;
        private const float LockSize = 24f;
        private const float SealSize = 30f;
        private const float ChipHeight = 22f;
        private const float PreviewMin = 72f;
        private const float PreviewMax = 208f;

        private sealed class NodeView
        {
            public int Index;
            public UiButton Button;
            public RectTransform Rect;
            public Image Carrier;
            public Image Wash;
            public Image Frame;
            public Image Icon;
            public Image Edge;
            public Image Seal;
            public Image StampSeal;
            public Image SynergyMark;
            public Image TargetRing;
            public Image NewDot;
            public Image Cross;
            public RectTransform Chip;
            public TMP_Text ChipText;
            public Image Lock;
        }

        private sealed class SynergyView
        {
            public int Index;
            public UiButton Button;
            public Image Badge;
            public Image Ring;
            public Image BadgeSeal;
        }

        private RectTransform _root;
        private CanvasGroup _group;
        private RectTransform _block;
        private Image _paperBase;
        private RawImage _parchment;
        private Image _headerBand;
        private TMP_Text _title;
        private RectTransform _headerPlate;
        private Image _goldIcon;
        private TMP_Text _gold;
        private TMP_Text _ash;
        private TMP_Text _targetLine;
        private UiIconButton _symbolsIcon;
        private UiButton _symbols;
        private readonly TMP_Text[] _tierLabels = new TMP_Text[TreeV2Layout.Tiers];
        private readonly RectTransform[] _rows = new RectTransform[TreeV2Rows.Count];
        private readonly CanvasGroup[] _rowGroups = new CanvasGroup[TreeV2Rows.Count];
        private readonly Image[] _rowWash = new Image[TreeV2Rows.Count];
        private readonly Image[] _rowPlate = new Image[TreeV2Rows.Count];
        private readonly Image[] _rowEmblem = new Image[TreeV2Rows.Count];
        private readonly TMP_Text[] _rowLabel = new TMP_Text[TreeV2Rows.Count];
        private UiTreeEdges _edges;
        private Image _fog;
        private Image _fogPlate;
        private TMP_Text _fogLabel;
        private RectTransform _overlay;
        private TMP_Text _synergyHeader;
        private NodeView[] _nodes = new NodeView[0];
        private SynergyView[] _synergies = new SynergyView[0];

        private RectTransform _detail;
        private Image _card;
        private Image _previewIcon;
        private TMP_Text _dName;
        private TMP_Text _dKind;
        private TMP_Text _dState;
        private TMP_Text _dDesc;
        private TMP_Text _dExtra;
        private RectTransform _pricePlate;
        private TMP_Text _dPrice;
        private TMP_Text _dDiscount;
        private UiButton _buy;
        private UiButton _target;
        private RectTransform _actions;
        private TMP_Text _stripCaption;
        private TMP_Text _note;
        private UiButton _reroll;
        private UiButton _hearth;
        private TMP_Text _rerollCost;
        private TMP_Text _hearthCost;
        private UiButton _continue;
        private UiPrompt _continuePrompt;
        private UiPrompt _buyPrompt;

        private RectTransform _legend;
        private TMP_Text _legendText;

        private ITreeShopCommands _commands;
        private readonly TreeV2Layout _layout = new TreeV2Layout();
        private readonly TreeV2Nav _nav = new TreeV2Nav();
        private readonly int[] _remembered = { -1, -1, -1, -1, -1, -1, -1 };
        private Vector2 _laidOutFor = new Vector2(-1f, -1f);
        private int _laidOutNodes = -1;
        private int _laidOutSynergies = -1;
        private string _laidOutLanguage;
        private float _laidOutText = -1f;
        private int _focus = -1;           // nav target: node 0..N-1, synergy N..N+S-1
        private int _detailTarget = -1;
        private Selectable _lastTreeFocus;  // the last node or badge: the actions' Left
        private int _lastNode = -1;          // the last NODE: where Y returns from the synergy column (fix1)
        private UiTweenHandle _close;
        private bool _open;
        private TreeSynergy _activeBefore;

        /// <summary>The player continued: the host closes the round-end flow (the provider's Continue has been called).</summary>
        public event Action Continued;

        public ITreeShopCommands Commands => _commands;
        public TreeScreenSnapshot Snapshot => _commands != null ? _commands.Snapshot : null;
        public TreeV2Layout Layout => _layout;
        public TreeV2Nav Nav => _nav;
        public bool IsOpen => _open;
        public bool Closing => UiMotion.IsAlive(_close);
        public int FocusIndex => _focus;
        public int DetailTarget => _detailTarget;
        public RectTransform Root => _root;
        public RectTransform DetailRect => _detail;
        public RectTransform ActionsRect => _actions;
        public TMP_Text DetailName => _dName;
        public TMP_Text DetailState => _dState;
        public TMP_Text DetailDescription => _dDesc;
        public TMP_Text FogLabel => _fogLabel;
        internal TMP_Text GoldText => _gold;
        internal TMP_Text AshText => _ash;
        internal TMP_Text StripCaption => _stripCaption;
        public UiButton BuyButton => _buy;
        public UiButton TargetButton => _target;
        public UiButton RerollButton => _reroll;
        public UiButton HearthButton => _hearth;
        public UiButton ContinueButton => _continue;
        public UiButton SymbolsButton => _symbols;
        public UiTreeEdges Edges => _edges;
        public bool LegendShown => _legend != null && _legend.gameObject.activeSelf;

        public UiButton NodeButton(int index)
        {
            return index >= 0 && index < _nodes.Length && _nodes[index] != null ? _nodes[index].Button : null;
        }

        /// <summary>Tests (AD M2): node <paramref name="index"/>'s purchase stamp.</summary>
        internal Image NodeStamp(int index)
        {
            return index >= 0 && index < _nodes.Length && _nodes[index] != null ? _nodes[index].StampSeal : null;
        }

        public UiButton SynergyButton(int index)
        {
            return index >= 0 && index < _synergies.Length && _synergies[index] != null ? _synergies[index].Button : null;
        }

        public TMP_Text RowLabel(int row) => _rowLabel[Mathf.Clamp(row, 0, TreeV2Rows.Count - 1)];

        /// <summary>The control the screen's focus belongs on now: the focused node / badge, else the first useful one.</summary>
        public Selectable FocusTarget()
        {
            Selectable at = TargetSelectable(_focus);
            if (at != null && at.gameObject.activeInHierarchy)
            {
                return at;
            }

            return _continue;
        }

        // ================================================================== build

        public static UiTreeScreenV2 Create(RectTransform host)
        {
            RectTransform root = UiFactory.NewRect(RootName, host);
            UiFactory.Fill(root);
            var panel = root.gameObject.AddComponent<UiTreeScreenV2>();
            panel.Build(root);
            root.gameObject.SetActive(false);
            return panel;
        }

        private void Build(RectTransform root)
        {
            _root = root;
            _group = UiFactory.Group(root.gameObject);

            // The paper: the AP-042 parchment cover-fit, over the plain cream paper a build without the art keeps.
            _paperBase = UiFactory.NewImage("Paper", root, TreeArt.Paper);
            UiFactory.Fill(_paperBase.rectTransform);
            Texture2D parchment = TreeArt.ParchmentTexture();
            if (parchment != null)
            {
                var art = new GameObject("Parchment", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(UiBackdropArt));
                var artRect = (RectTransform)art.transform;
                artRect.SetParent(root, false);
                UiFactory.Fill(artRect);
                _parchment = art.GetComponent<RawImage>();
                _parchment.texture = parchment;
                _parchment.raycastTarget = false;
                art.GetComponent<UiBackdropArt>().Configure(1f);
            }

            _block = UiFactory.NewRect("Block", root);
            TopLeft(_block);

            _edges = NewEdges(_block);
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                BuildRow(r);
            }

            Sprite fog = TreeArt.Fog();
            _fog = UiFactory.NewImage("Fog", _block, TreeArt.InkText, fog != null ? fog : UiSprites.FadeRight);
            TopLeft(_fog.rectTransform);
            SetAlpha(_fog, TreeArt.FogAlpha);
            _overlay = UiFactory.NewRect("Overlay", _block);
            TopLeft(_overlay);
            _fogPlate = UiFactory.NewImage("FogPlate", _block, TreeArt.Paper, UiSprites.Chip);
            TopLeft(_fogPlate.rectTransform);
            SetAlpha(_fogPlate, PlateAlpha);
            _fogLabel = UiFactory.Label("FogLabel", _fogPlate.transform, string.Empty, UiType.Label, TreeArt.InkText, TextAnchor.MiddleCenter);
            UiFactory.Fill(_fogLabel.rectTransform, (int)UiSpace.XS);
            _fogLabel.enableWordWrapping = false;

            BuildHeader();
            _fogPlate.transform.SetAsLastSibling();   // AD M3: the fog line stands in the tier band, over the header's paper
            _synergyHeader = UiFactory.Label("SynergyHeader", _block, string.Empty, UiType.Label, TreeArt.InkText, TextAnchor.MiddleCenter);
            TopLeft(_synergyHeader.rectTransform);
            _synergyHeader.enableWordWrapping = false;
            BuildDetail();
            BuildActions();
            BuildLegend();
        }

        private void BuildHeader()
        {
            _headerBand = UiFactory.NewImage("HeaderBand", _block, TreeArt.Paper);
            TopLeft(_headerBand.rectTransform);
            SetAlpha(_headerBand, PlateAlpha);

            _title = UiFactory.Label("Title", _block, string.Empty, UiType.Display, TreeArt.InkText, TextAnchor.MiddleLeft);
            TopLeft(_title.rectTransform);
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;

            for (int t = 0; t < TreeV2Layout.Tiers; t++)
            {
                _tierLabels[t] = UiFactory.Label("Tier" + (t + 1), _block, RomanNumerals.Cached(t + 1), UiType.Label, TreeArt.InkSecondary, TextAnchor.MiddleCenter);
                TopLeft(_tierLabels[t].rectTransform);
                _tierLabels[t].enableWordWrapping = false;
            }

            // The money and the legend on an ink plate: gold reads gold only on ink (on the parchment it would not hold 4.5:1).
            Image plate = UiFactory.NewImage("HeaderPlate", _block, TreeArt.InkPlate, UiSprites.Card);
            _headerPlate = plate.rectTransform;
            TopLeft(_headerPlate);
            _goldIcon = UiArt.IconImage("Coin", _headerPlate, "Hud.Gold", UiColor.StateGold, 28f);
            _gold = UiFactory.Numeric("Gold", _headerPlate, string.Empty, UiType.NumericM, UiColor.StateGold, TextAnchor.MiddleLeft, false);
            _ash = UiFactory.Label("Ash", _headerPlate, string.Empty, UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            _ash.enableWordWrapping = false;
            _targetLine = UiFactory.Label("TargetLine", _headerPlate, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            _targetLine.enableWordWrapping = false;
            // SYMBOLS (the legend, AD §5.1) is the kit's C10: a 72 px glyph control whose word is its tooltip and reader name - the
            // money keeps the plate's width on the Mac at UI 1.2.
            _symbolsIcon = UiBrushKit.IconButton(_headerPlate, "Symbols", "Notice.Info", "tree.v2.screen.symbols", "SYMBOLS", ToggleLegend);
            _symbols = _symbolsIcon.Button;
        }

        private void BuildRow(int r)
        {
            RectTransform row = UiFactory.NewRect("Row." + TreeV2Rows.BranchAt(r), _block);
            TopLeft(row);
            _rows[r] = row;
            _rowGroups[r] = UiFactory.Group(row.gameObject);
            _rowWash[r] = UiFactory.NewImage("Wash", row, TreeArt.InkText);
            TopLeft(_rowWash[r].rectTransform);
            _rowPlate[r] = UiFactory.NewImage("LabelPlate", row, TreeArt.Paper, UiSprites.Chip);
            TopLeft(_rowPlate[r].rectTransform);
            SetAlpha(_rowPlate[r], PlateAlpha);
            _rowEmblem[r] = UiFactory.NewImage("Emblem", row, UiColor.TextPrimary);
            TopLeft(_rowEmblem[r].rectTransform);
            _rowEmblem[r].preserveAspect = true;
            _rowLabel[r] = UiFactory.Label("Label", row, string.Empty, UiType.Label, TreeArt.InkText, TextAnchor.MiddleLeft);
            TopLeft(_rowLabel[r].rectTransform);
            _rowLabel[r].enableWordWrapping = true;
            _rowLabel[r].overflowMode = TextOverflowModes.Ellipsis;
        }

        private static UiTreeEdges NewEdges(RectTransform parent)
        {
            var go = new GameObject("Edges", typeof(RectTransform), typeof(CanvasRenderer), typeof(UiTreeEdges));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            TopLeft(rect);
            rect.sizeDelta = Vector2.one;
            UiTreeEdges edges = go.GetComponent<UiTreeEdges>();
            edges.raycastTarget = false;
            return edges;
        }

        private void BuildDetail()
        {
            _detail = UiFactory.NewRect("Detail", _block);
            TopLeft(_detail);
            _card = UiFactory.NewImage("Card", _detail, TreeArt.Paper, UiSprites.Card);
            UiFactory.Fill(_card.rectTransform);

            _previewIcon = UiFactory.NewImage("Preview", _detail, UiColor.TextPrimary);
            _previewIcon.preserveAspect = true;
            TopLeft(_previewIcon.rectTransform);

            _dName = DetailLabel("Name", UiType.H2, TreeArt.InkText);
            _dKind = DetailLabel("Kind", UiType.Label, TreeArt.InkSecondary);
            _dState = DetailLabel("State", UiType.Label, TreeArt.InkText);
            _dDesc = DetailLabel("Description", UiType.Body, TreeArt.InkSecondary);
            _dExtra = DetailLabel("Extra", UiType.Caption, TreeArt.InkSecondary);

            // Price + BUY + TARGET on an ink plate (the mock's black price field): gold and the kit's cream words hold there.
            Image plate = UiFactory.NewImage("PricePlate", _detail, TreeArt.InkPlate, UiSprites.Card);
            _pricePlate = plate.rectTransform;
            TopLeft(_pricePlate);
            _dPrice = UiFactory.Numeric("Price", _pricePlate, string.Empty, UiType.NumericM, UiColor.StateGold, TextAnchor.MiddleLeft, false);
            TopLeft(_dPrice.rectTransform);
            _dDiscount = UiFactory.Label("Discount", _pricePlate, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            TopLeft(_dDiscount.rectTransform);
            _dDiscount.enableWordWrapping = false;
            _buy = UiBrushKit.Button(_pricePlate, "Buy", "tree.action.buy", "BUY", null, OnBuyPressed);
            _buyPrompt = UiRunTreePanel.SubmitGlyphBeside(_buy);
            _target = UiBrushKit.Button(_pricePlate, "Target", "tree.v2.screen.target", "SET TARGET", null, OnTargetPressed);
        }

        private TMP_Text DetailLabel(string name, UiType type, UiColor color)
        {
            TMP_Text text = UiFactory.Label(name, _detail, string.Empty, type, color, TextAnchor.UpperLeft);
            TopLeft(text.rectTransform);
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private void BuildActions()
        {
            Image plate = UiFactory.NewImage("Actions", _block, TreeArt.InkPlate, UiSprites.Card);
            _actions = plate.rectTransform;
            TopLeft(_actions);
            _stripCaption = UiFactory.Label("StripCaption", _actions, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.UpperLeft);
            TopLeft(_stripCaption.rectTransform);
            _stripCaption.enableWordWrapping = true;
            _note = UiFactory.Label("Note", _actions, string.Empty, UiType.Caption, UiColor.StateWarning, TextAnchor.UpperLeft);
            TopLeft(_note.rectTransform);
            _note.enableWordWrapping = true;
            _reroll = UiBrushKit.Button(_actions, "Reroll", "shop.reroll", "REROLL", null, OnRerollPressed);
            _hearth = UiBrushKit.Button(_actions, "Hearth", "tree.action.stoke", "STOKE THE HEARTH", null, OnHearthPressed);
            _rerollCost = CostLabel("RerollCost");
            _hearthCost = CostLabel("HearthCost");
            _continue = UiBrushKit.Button(_actions, "Continue", "shop.continue", "CONTINUE", UpgradeScreen.ContinueGlyph, Continue);
            _continuePrompt = UiInputGlyphs.Build("Prompt", _continue.transform, UiPromptAction.Continue, UiColor.TextPrimary, UiColor.SurfaceInset);
            if (_continuePrompt != null)
            {
                var rect = (RectTransform)_continuePrompt.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(-(int)UiSpace.S, 0f);
            }
        }

        private TMP_Text CostLabel(string name)
        {
            TMP_Text cost = UiFactory.Label(name, _actions, string.Empty, UiType.MenuSmall, UiColor.StateGold, TextAnchor.MiddleLeft);
            TopLeft(cost.rectTransform);
            cost.enableWordWrapping = false;
            return cost;
        }

        private void BuildLegend()
        {
            Image panel = UiFactory.NewImage("Legend", _block, TreeArt.InkPlate, UiSprites.Card);
            _legend = panel.rectTransform;
            TopLeft(_legend);
            _legendText = UiFactory.Label("Words", _legend, string.Empty, UiType.Caption, UiColor.TextPrimary, TextAnchor.UpperLeft);
            UiFactory.Fill(_legendText.rectTransform, (int)UiSpace.M);
            _legendText.enableWordWrapping = true;
            _legend.gameObject.SetActive(false);
        }

        private NodeView BuildNode(int index)
        {
            var v = new NodeView { Index = index };
            int captured = index;
            v.Button = UiFactory.Button("Node." + index, _rows[0], string.Empty, UiButtonVariant.Brush, () => OnNodeActivated(captured));
            v.Rect = (RectTransform)v.Button.transform;
            v.Button.LabelText.gameObject.SetActive(false);
            v.Button.ConfirmCue = false;   // the outcome voices itself (purchase / Denied), never a Confirm for a refusal
            v.Button.gameObject.AddComponent<UiTreeV2NodeInput>().Configure(this, captured, false);

            v.Carrier = Child(v.Rect, "Carrier", TreeArt.Paper, TreeArt.Frame(TreeFrameKind.Carrier));
            v.Wash = Child(v.Rect, "Wash", UiColor.TextPrimary, null);
            v.Frame = Child(v.Rect, "Frame", TreeArt.InkText, TreeArt.Frame(TreeFrameKind.Available));
            v.Icon = Child(v.Rect, "Icon", UiColor.TextPrimary, null);
            v.Icon.preserveAspect = true;
            v.Edge = Child(v.Rect, "TierFive", UiColor.TextPrimary, TreeArt.Frame(TreeFrameKind.TierFiveEdge));
            v.Seal = Child(v.Rect, "Seal", UiColor.TextPrimary, TreeArt.Frame(TreeFrameKind.SealPurchased));
            v.StampSeal = Child(v.Rect, "StampSeal", UiColor.TextPrimary, TreeArt.Frame(TreeFrameKind.StampPurchased));
            v.StampSeal.enabled = false;
            v.SynergyMark = Child(v.Rect, "SynergyMark", UiColor.TextPrimary, TreeArt.Frame(TreeFrameKind.SealSynergy));
            v.TargetRing = Child(v.Rect, "TargetRing", TreeArt.InkText, UiSprites.RingThin);
            v.NewDot = Child(v.Rect, "New", UiColor.AccentSakura, UiSprites.Disc);
            v.Cross = Child(v.Rect, "Cross", TreeArt.InkText, UiSprites.Solid);
            v.Cross.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            Image chip = UiFactory.NewImage("Chip", v.Rect, TreeArt.InkPlate, UiSprites.Chip);
            v.Chip = chip.rectTransform;
            v.ChipText = UiFactory.Label("Words", v.Chip, string.Empty, UiType.Caption, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            UiFactory.Fill(v.ChipText.rectTransform);
            v.ChipText.enableWordWrapping = false;
            Sprite locked = TreeArt.Frame(TreeFrameKind.SealLocked);
            v.Lock = UiFactory.NewImage("Lock." + index, _overlay, TreeArt.InkText, locked != null ? locked : UiArt.Icon("State.Locked"));
            v.Lock.preserveAspect = true;
            TopLeft(v.Lock.rectTransform);
            return v;
        }

        private SynergyView BuildSynergy(int index)
        {
            var v = new SynergyView { Index = index };
            int captured = index;
            v.Button = UiFactory.Button("Synergy." + index, _block, string.Empty, UiButtonVariant.Brush, () => OnSynergyActivated(captured));
            v.Button.LabelText.gameObject.SetActive(false);
            v.Button.ConfirmCue = false;
            v.Button.gameObject.AddComponent<UiTreeV2NodeInput>().Configure(this, captured, true);
            TopLeft((RectTransform)v.Button.transform);
            v.Ring = Child((RectTransform)v.Button.transform, "Ring", TreeArt.InkText, UiSprites.RingThin);
            v.Badge = Child((RectTransform)v.Button.transform, "Badge", UiColor.TextPrimary, null);
            v.Badge.preserveAspect = true;
            v.BadgeSeal = Child((RectTransform)v.Button.transform, "BadgeSeal", UiColor.TextPrimary, TreeArt.Frame(TreeFrameKind.SealSynergy));
            v.BadgeSeal.enabled = false;
            return v;
        }

        private static Image Child(RectTransform parent, string name, UiColor color, Sprite sprite)
        {
            Image image = UiFactory.NewImage(name, parent, color, sprite);
            image.raycastTarget = false;
            return image;
        }

        // ================================================================== open / close

        /// <summary>Binds the provider, lays the screen out for this canvas, refreshes and plays the open (≤ 600 ms, input at once).</summary>
        public void Open(ITreeShopCommands commands)
        {
            _commands = commands;
            _open = true;
            UiMotion.Kill(_close, false);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _group.interactable = true;
            _group.blocksRaycasts = true;
            _legend.gameObject.SetActive(false);
            _note.text = string.Empty;
            for (int r = 0; r < _remembered.Length; r++)
            {
                _remembered[r] = -1;
            }

            _focus = -1;
            _lastNode = -1;
            _detailTarget = -1;
            _laidOutFor = new Vector2(-1f, -1f);
            Refresh();
            _activeBefore = ActiveSynergies(Snapshot);
            UiEvents.Raise(UiEvent.TreeOpened, RootEventDetail);   // S-08 voices the open (AD §5.3 paper); once per open

            // The open (AD §5.3): the paper at once, the rows ink in 40 ms apart; Decor - Reduced Motion lands it.
            UiMotion.KillTarget(_group, false);
            _group.alpha = 0f;
            UiMotion.Alpha(_group, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, GetInstanceID());
            for (int r = 0; r < _rowGroups.Length; r++)
            {
                UiMotion.KillTarget(_rowGroups[r], false);
                _rowGroups[r].alpha = 0f;
                UiMotion.Alpha(_rowGroups[r], 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, OpenBeat + r * RowStagger, UiClock.Unscaled, GetInstanceID());
            }

            FocusFirstUseful();
        }

        /// <summary>Input off at once, a Fast fade (Decor); the host turns its canvas off when <see cref="Closing"/> ends.</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _close = UiMotion.Alpha(_group, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, GetInstanceID());
        }

        /// <summary>After the close fade (or at once): the panel leaves the hierarchy's draw.</summary>
        public void Hide()
        {
            _open = false;
            UiMotion.Kill(_close, true);
            _root.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            UiMotion.KillOwner(GetInstanceID(), true);
        }

        // ================================================================== refresh

        /// <summary>Redraws everything from the provider's snapshot (after every command, on open, on a language or scale change).</summary>
        public void Refresh()
        {
            TreeScreenSnapshot s = Snapshot;
            if (s == null)
            {
                return;
            }

            EnsureLayout(s);
            _title.text = UiStrings.Get("tree.v2.screen.title", "UPGRADE TREE");
            _gold.text = TreeV2Text.Gold(s.Gold);
            ProfileData profile = ProfileService.Current;
            _ash.text = TreeV2Text.Ash(profile != null ? profile.AshBalance : 0);
            _targetLine.text = TreeV2Text.TargetLine(s);
            _synergyHeader.text = UiStrings.Get("tree.v2.screen.synergies", "SYNERGIES");
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                _rowLabel[r].text = TreeV2Text.Branch(TreeV2Rows.BranchAt(r));
            }

            RefreshFog(s);
            TreeSynergy active = ActiveSynergies(s);
            for (int i = 0; i < _nodes.Length; i++)
            {
                ApplyNode(_nodes[i], s.Nodes[i], active);
            }

            for (int k = 0; k < _synergies.Length; k++)
            {
                ApplySynergy(_synergies[k], s.Synergies[k]);
            }

            DrawEdges(s);
            RefreshActions(s);
            ShowDetail(_detailTarget >= 0 ? _detailTarget : _focus);
            LinkNavigation();
        }

        private void EnsureLayout(TreeScreenSnapshot s)
        {
            Vector2 size = _root.rect.size;
            bool language = !string.Equals(_laidOutLanguage, UiStrings.Language, StringComparison.Ordinal);
            if (size == _laidOutFor && s.Nodes.Length == _laidOutNodes && s.Synergies.Length == _laidOutSynergies && !language &&
                Mathf.Approximately(_laidOutText, UiTokens.TextScale))
            {
                return;
            }

            _laidOutFor = size;
            _laidOutNodes = s.Nodes.Length;
            _laidOutSynergies = s.Synergies.Length;
            _laidOutLanguage = UiStrings.Language;
            _laidOutText = UiTokens.TextScale;

            if (_nodes.Length != s.Nodes.Length)
            {
                for (int i = 0; i < _nodes.Length; i++)
                {
                    if (_nodes[i] != null)
                    {
                        Destroy(_nodes[i].Button.gameObject);
                        Destroy(_nodes[i].Lock.gameObject);
                    }
                }

                _nodes = new NodeView[s.Nodes.Length];
                for (int i = 0; i < _nodes.Length; i++)
                {
                    _nodes[i] = BuildNode(i);
                }
            }

            if (_synergies.Length != s.Synergies.Length)
            {
                for (int k = 0; k < _synergies.Length; k++)
                {
                    if (_synergies[k] != null)
                    {
                        Destroy(_synergies[k].Button.gameObject);
                    }
                }

                _synergies = new SynergyView[s.Synergies.Length];
                for (int k = 0; k < _synergies.Length; k++)
                {
                    _synergies[k] = BuildSynergy(k);
                }
            }

            _layout.Compute(size, s, MeasureLabels());
            _nav.Build(_layout);
            Place(s);
        }

        /// <summary>The branch labels wrap to two lines: the widest WORD of any branch name decides the column (U2: measured).</summary>
        private float MeasureLabels()
        {
            float widest = 0f;
            TMP_Text probe = _rowLabel[0];
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                string words = TreeV2Text.Branch(TreeV2Rows.BranchAt(r));
                int start = 0;
                for (int i = 0; i <= words.Length; i++)
                {
                    if (i < words.Length && words[i] != ' ')
                    {
                        continue;
                    }

                    if (i > start)
                    {
                        widest = Mathf.Max(widest, probe.GetPreferredValues(words.Substring(start, i - start)).x);
                    }

                    start = i + 1;
                }
            }

            return Mathf.Ceil(widest) + 2f;
        }

        private void Place(TreeScreenSnapshot s)
        {
            TreeV2Layout l = _layout;
            Set(_block, l.BlockX, 0f, l.BlockWidth, l.Canvas.y);
            float scale = l.Scale;

            // Header: the band, the title, the tier numerals over their columns, the money plate at the right.
            Set(_headerBand.rectTransform, 0f, 0f, l.BlockWidth, TreeV2Layout.HeaderHeight);
            float plateWidth = Mathf.Min(l.DetailW + l.SynergyWidth + TreeV2Layout.ColumnGap, l.BlockWidth * 0.5f);
            float plateX = l.BlockWidth - TreeV2Layout.Margin - plateWidth;
            Set(_headerPlate, plateX, (TreeV2Layout.HeaderHeight - TreeV2Layout.TierBand - HeaderPlateHeight) * 0.5f + 4f, plateWidth, HeaderPlateHeight);
            LayoutHeaderPlate(plateWidth);
            Set(_title.rectTransform, TreeV2Layout.Margin, 0f, Mathf.Max(1f, plateX - TreeV2Layout.Margin * 2f), TreeV2Layout.HeaderHeight - TreeV2Layout.TierBand);
            for (int t = 0; t < TreeV2Layout.Tiers; t++)
            {
                Set(_tierLabels[t].rectTransform, l.TierX(t + 1), TreeV2Layout.HeaderHeight - TreeV2Layout.TierBand, l.TierWidth, TreeV2Layout.TierBand);
            }

            // Rows: the branch wash across the tree, the label plate (emblem + two-line name), the nodes.
            float lineHeight = UiFactory.LineHeight(UiType.Label);
            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                float y = l.RowY(r), h = l.RowHeight(r);
                Set(_rows[r], 0f, 0f, l.BlockWidth, l.Canvas.y);
                Set(_rowWash[r].rectTransform, TreeV2Layout.Margin, y + 2f, l.SynergyX - TreeV2Layout.Margin - TreeV2Layout.ColumnGap, h - 4f);
                Color pigment = TreeArt.Pigment(TreeV2Rows.BranchAt(r));
                _rowWash[r].color = WithAlpha(pigment, TreeArt.RowWashAlpha);
                float labelHeight = Mathf.Min(h - 8f, lineHeight * 2f + 8f);
                float plateY = y + (h - Mathf.Max(labelHeight, TreeV2Layout.EmblemSize)) * 0.5f;
                Set(_rowPlate[r].rectTransform, TreeV2Layout.Margin - 8f, plateY - 4f, l.LabelColumn - TreeV2Layout.Margin, Mathf.Max(labelHeight, TreeV2Layout.EmblemSize) + 8f);
                Set(_rowEmblem[r].rectTransform, TreeV2Layout.Margin, y + (h - TreeV2Layout.EmblemSize) * 0.5f, TreeV2Layout.EmblemSize, TreeV2Layout.EmblemSize);
                Sprite emblem = TreeArt.BranchEmblem(TreeV2Rows.BranchAt(r), TreeV2Layout.EmblemSize * HudArt.CanvasScale);
                _rowEmblem[r].sprite = emblem;
                _rowEmblem[r].enabled = emblem != null;
                float labelX = TreeV2Layout.Margin + TreeV2Layout.EmblemSize + TreeV2Layout.LabelGap;
                Set(_rowLabel[r].rectTransform, labelX, y + (h - labelHeight) * 0.5f, l.LabelWidth, labelHeight);
            }

            for (int i = 0; i < _nodes.Length; i++)
            {
                PlaceNode(_nodes[i], i);
            }

            Set(_synergyHeader.rectTransform, l.SynergyX, l.TreeTop, l.SynergyWidth, TreeV2Layout.SynergyHeader * scale);
            for (int k = 0; k < _synergies.Length; k++)
            {
                Vector2 c = l.SynergyCentre(k);
                float hit = Mathf.Max(48f, l.SynergyBadgeSize + 8f);
                Set((RectTransform)_synergies[k].Button.transform, c.x - hit * 0.5f, c.y - hit * 0.5f, hit, hit);
                Centre(_synergies[k].Badge.rectTransform, l.SynergyBadgeSize);
                Centre(_synergies[k].Ring.rectTransform, l.SynergyBadgeSize + 6f);
                Centre(_synergies[k].BadgeSeal.rectTransform, l.SynergyBadgeSize * 0.5f);
            }

            PlaceDetailColumn();
        }

        /// <summary>
        /// The detail column: the actions block docked at the bottom (its height measured from the strip caption's words), the card
        /// from the tree's top down to it. Re-run when the caption changes (a language, a new strip discount).
        /// </summary>
        private void PlaceDetailColumn()
        {
            TreeV2Layout l = _layout;
            float actionsHeight = ActionsHeight(l.DetailW);
            float bottom = l.Canvas.y - TreeV2Layout.Margin;
            Set(_actions, l.DetailX, bottom - actionsHeight, l.DetailW, actionsHeight);
            float cardTop = l.TreeTop + 8f;
            float cardHeight = Mathf.Max(1f, bottom - actionsHeight - ActionGap - cardTop);   // fix1: one gap, as between the controls
            Set(_detail, l.DetailX, cardTop, l.DetailW, cardHeight);
            Set(_legend, l.DetailX, cardTop, l.DetailW, cardHeight);
            LayoutActions(l.DetailW, actionsHeight);
        }

        private void PlaceNode(NodeView v, int index)
        {
            TreeV2Layout l = _layout;
            int row = l.RowOfNode(index);
            v.Rect.SetParent(_rows[Mathf.Clamp(row, 0, TreeV2Rows.Count - 1)], false);
            Rect hit = l.HitRect(index);
            Set(v.Rect, hit.x, hit.y, hit.width, hit.height);
            float frame = l.FrameSize, icon = l.IconSize;
            Centre(v.Carrier.rectTransform, frame);
            Centre(v.Wash.rectTransform, frame - 8f * l.Scale);
            Centre(v.Frame.rectTransform, frame);
            v.Frame.type = v.Frame.sprite != null && v.Frame.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            v.Frame.pixelsPerUnitMultiplier = TreeArt.FramePixelsPerUnitMultiplier / Mathf.Max(0.1f, l.Scale);
            Centre(v.Icon.rectTransform, icon);
            Centre(v.Edge.rectTransform, frame);
            Centre(v.StampSeal.rectTransform, frame);
            Centre(v.TargetRing.rectTransform, frame + 14f * l.Scale);
            Corner(v.Seal.rectTransform, new Vector2(1f, 1f), SealSize * l.Scale, 2f);
            Corner(v.SynergyMark.rectTransform, new Vector2(0f, 1f), 22f * l.Scale, 2f);
            Corner(v.NewDot.rectTransform, new Vector2(0f, 0f), 10f, 6f);
            RectTransform cross = v.Cross.rectTransform;
            cross.anchorMin = new Vector2(0.5f, 0.5f);
            cross.anchorMax = new Vector2(0.5f, 0.5f);
            cross.sizeDelta = new Vector2(frame * 1.1f, 3f);
            cross.anchoredPosition = Vector2.zero;
            v.Chip.anchorMin = new Vector2(0.5f, 0f);
            v.Chip.anchorMax = new Vector2(0.5f, 0f);
            v.Chip.pivot = new Vector2(0.5f, 0.5f);
            v.Chip.sizeDelta = new Vector2(Mathf.Max(48f, frame * 0.7f), UiFactory.ScaledBand(ChipHeight));
            v.Chip.anchoredPosition = new Vector2(0f, (hit.height - frame) * 0.5f);
            // The lock rides above the fog (AP-042: "draw_after_fog"), at the frame's lower-right corner.
            Vector2 c = l.Centre(index);
            float lockSize = LockSize * Mathf.Max(l.Scale, 0.75f);
            Set(v.Lock.rectTransform, c.x + frame * 0.5f - lockSize * 0.8f, c.y + frame * 0.5f - lockSize * 0.8f, lockSize, lockSize);
        }

        // ================================================================== nodes

        private void ApplyNode(NodeView v, in TreeNodeView n, TreeSynergy active)
        {
            Color pigment = TreeArt.Pigment(n.Branch, n.Element);
            bool purchased = n.State == TreeNodeState.Purchased;
            bool locked = IsLocked(n.State);
            float physical = _layout.IconSize * HudArt.CanvasScale;

            Sprite frame = TreeArt.Frame(purchased ? TreeFrameKind.Purchased : locked ? TreeFrameKind.Locked : TreeFrameKind.Available);
            v.Frame.sprite = frame != null ? frame : UiSprites.Card;
            v.Frame.type = frame != null && frame.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            v.Frame.fillCenter = frame == null ? false : v.Frame.fillCenter;
            v.Frame.color = locked ? WithAlpha(UiTokens.Get(TreeArt.InkText), 0.55f)
                : n.State == TreeNodeState.Unaffordable ? WithAlpha(pigment, 0.55f) : pigment;

            v.Carrier.enabled = !locked && v.Carrier.sprite != null;
            Sprite tint = TreeArt.Frame(TreeFrameKind.Carrier);
            v.Wash.sprite = tint != null ? tint : UiSprites.Card;
            v.Wash.enabled = purchased;
            v.Wash.color = WithAlpha(pigment, 0.3f);

            string id = TreeArt.NodeIdOf(n.IconKey, n.NodeId);
            Sprite icon = TreeArt.NodeIcon(id, physical);
            v.Icon.sprite = icon != null ? icon : UiSprites.Disc;
            v.Icon.color = icon == null ? WithAlpha(pigment, locked ? TreeArt.LockedIconAlpha : 0.8f)
                : locked ? WithAlpha(UiTokens.Get(UiColor.TextMuted), TreeArt.LockedIconAlpha) : Color.white;
            v.Icon.rectTransform.localScale = icon == null ? Vector3.one * 0.5f : Vector3.one;

            v.Edge.enabled = n.Tier >= TreeV2Layout.Tiers && v.Edge.sprite != null;
            v.Seal.enabled = purchased && v.Seal.sprite != null;
            v.Seal.color = pigment;
            v.StampSeal.color = pigment;
            v.StampSeal.enabled = false;   // AD M2: the stamp is the purchase's moment; the corner seal + wash are the lasting look
            v.SynergyMark.enabled = (n.Synergies & active) != 0 && v.SynergyMark.sprite != null;
            v.TargetRing.enabled = n.IsTarget || n.OnTargetPath;
            v.TargetRing.color = WithAlpha(UiTokens.Get(TreeArt.InkText), n.IsTarget ? 1f : 0.45f);
            v.NewDot.enabled = n.NewlyAvailable;
            v.Cross.enabled = n.State == TreeNodeState.Excluded;
            string discount = n.Recommended && !purchased ? TreeV2Text.Discount(n) : string.Empty;
            v.Chip.gameObject.SetActive(n.Recommended && !purchased);
            v.ChipText.text = discount.Length > 0 ? discount : UiStrings.Get("tree.v2.screen.recommended", "RECOMMENDED");
            v.Lock.enabled = n.State == TreeNodeState.Fogged || n.State == TreeNodeState.PrereqMissing || n.State == TreeNodeState.AwakeningLocked
                             || (int)n.State > (int)TreeNodeState.AwakeningLocked;
        }

        private static bool IsLocked(TreeNodeState state)
        {
            return state != TreeNodeState.Available && state != TreeNodeState.Unaffordable && state != TreeNodeState.Purchased;
        }

        private void ApplySynergy(SynergyView v, in TreeSynergyView s)
        {
            Sprite badge = TreeArt.SynergyBadge(s.Synergy, _layout.SynergyBadgeSize * HudArt.CanvasScale);
            v.Badge.sprite = badge != null ? badge : UiSprites.RingThin;
            Color a = TreeArt.Pigment(s.BranchA);
            v.Badge.color = badge == null ? a : Color.white;
            float alpha = s.Active ? 1f : s.Revealed ? 0.7f : 0.3f;
            SetAlpha(v.Badge, alpha * v.Badge.color.a);
            v.Ring.enabled = s.Active;
        }

        private static TreeSynergy ActiveSynergies(TreeScreenSnapshot s)
        {
            TreeSynergy mask = TreeSynergy.None;
            if (s == null || s.Synergies == null)
            {
                return mask;
            }

            for (int k = 0; k < s.Synergies.Length; k++)
            {
                if (s.Synergies[k].Active)
                {
                    mask |= s.Synergies[k].Synergy;
                }
            }

            return mask;
        }

        private void RefreshFog(TreeScreenSnapshot s)
        {
            bool fogged = s.OpenTier < TreeV2Layout.Tiers;
            _fog.gameObject.SetActive(fogged);
            _fogPlate.gameObject.SetActive(fogged && s.NextTierRound > 0);
            for (int t = 0; t < _tierLabels.Length; t++)
            {
                _tierLabels[t].gameObject.SetActive(true);   // AD M3: the fog line below takes back the numerals it carries or covers
            }

            if (!fogged)
            {
                return;
            }

            float x = _layout.FogX(s.OpenTier);
            Set(_fog.rectTransform, x, _layout.TreeTop, _layout.FogRight - x, _layout.TreeBottom - _layout.TreeTop);
            if (s.NextTierRound > 0)
            {
                // AD M3 (12.10): the line stood at TreeTop + 4, over the first row's upper nodes (22 px of two icons). It now takes
                // the first fogged column's place in the tier band - "II · UNLOCKS AT ROUND 3" - so it meets no node; the numerals
                // it carries or covers step back.
                // Only V fogged leaves one column: the line then reaches back over IV's band from the tree's end.
                _fogLabel.text = RomanNumerals.Cached(s.OpenTier + 1) + " · " + TreeV2Text.Fog(s.NextTierRound);
                float width = Mathf.Max(48f, Mathf.Ceil(_fogLabel.GetPreferredValues(_fogLabel.text).x) + (int)UiSpace.L);
                float left = Mathf.Max(_layout.TierX(1), Mathf.Min(x + 4f, _layout.FogRight - 4f - width));
                width = Mathf.Min(width, _layout.FogRight - 4f - left);
                Set(_fogPlate.rectTransform, left, TreeV2Layout.HeaderHeight - TreeV2Layout.TierBand, width, TreeV2Layout.TierBand);
                float right = left + width;
                for (int t = 0; t < _tierLabels.Length; t++)
                {
                    float numeral = _layout.TierX(t + 1) + _layout.TierWidth * 0.5f;
                    _tierLabels[t].gameObject.SetActive(numeral + FogNumeralClear < left || numeral - FogNumeralClear > right);
                }
            }
        }

        /// <summary>AD M3: half a numeral's width - a tier numeral whose centre is within it of the fog line steps back.</summary>
        private const float FogNumeralClear = 16f;

        private void DrawEdges(TreeScreenSnapshot s)
        {
            _edges.Clear();
            Color ink = WithAlpha(UiTokens.Get(TreeArt.InkText), 0.35f);
            float half = _layout.FrameSize * 0.5f;
            float scale = _layout.Scale;
            for (int i = 0; i < s.Nodes.Length; i++)
            {
                int[] parents = s.Nodes[i].ParentIndices;
                if (parents == null)
                {
                    continue;
                }

                TreeNodeView child = s.Nodes[i];
                Color pigment = TreeArt.Pigment(child.Branch, child.Element);
                for (int p = 0; p < parents.Length; p++)
                {
                    int k = parents[p];
                    if (k < 0 || k >= s.Nodes.Length)
                    {
                        continue;
                    }

                    TreeNodeView parent = s.Nodes[k];
                    Vector2 a = _layout.Centre(k), b = _layout.Centre(i);
                    if (b.x - a.x < half * 2f)
                    {
                        continue;   // same column: no edge to draw
                    }

                    bool owned = parent.State == TreeNodeState.Purchased;
                    bool path = (child.OnTargetPath || child.IsTarget) && (owned || parent.OnTargetPath);
                    bool open = owned && !IsLocked(child.State);
                    float width = path ? 5f * scale : owned && child.State == TreeNodeState.Purchased ? 3f * scale : 2f * scale;
                    Color colour = path || open || child.State == TreeNodeState.Purchased ? WithAlpha(pigment, open && !path ? 0.75f : 1f) : ink;
                    _edges.Add(new Vector2(a.x + half, -a.y), new Vector2(b.x - half, -b.y), width, !owned && !path, colour);
                }
            }

            _edges.Commit();
        }

        // ================================================================== detail

        private void ShowDetail(int target)
        {
            TreeScreenSnapshot s = Snapshot;
            if (s == null)
            {
                return;
            }

            if (target < 0 || target >= _nav.Count)
            {
                target = FirstUsefulNode(s);
            }

            _detailTarget = target;
            if (target < 0)
            {
                return;
            }

            float previewPhysical = PreviewMax * HudArt.CanvasScale;
            if (target >= s.Nodes.Length)
            {
                TreeSynergyView syn = s.Synergies[target - s.Nodes.Length];
                bool known = syn.Revealed || syn.Active;
                Sprite badge = TreeArt.SynergyBadge(syn.Synergy, previewPhysical);
                SetPreview(badge, TreeArt.Pigment(syn.BranchA), !known);
                _dName.text = known ? TreeV2Text.SynergyName(syn) : TreeV2Text.Missing;
                _dKind.text = UiStrings.Get("tree.v2.screen.synergies", "SYNERGIES");
                _dState.text = syn.Active ? UiStrings.Get("tree.v2.screen.synergy_active", "ACTIVE") : known ? TreeV2Text.SynergyRequires(s, syn) : string.Empty;
                _dDesc.text = known ? TreeV2Text.SynergyDescription(syn) : UiStrings.Get("tree.v2.screen.synergy_hidden", "Own one of its nodes to reveal it.");
                _dExtra.text = string.Empty;
                _pricePlate.gameObject.SetActive(false);
                LayoutDetail();
                return;
            }

            TreeNodeView n = s.Nodes[target];
            string id = TreeArt.NodeIdOf(n.IconKey, n.NodeId);
            SetPreview(TreeArt.NodeIcon(id, previewPhysical), TreeArt.Pigment(n.Branch, n.Element), IsLocked(n.State));
            _dName.text = TreeV2Text.Name(n);
            _dKind.text = TreeV2Text.Tier(n.Tier) + " · " + TreeV2Text.Branch(n.Branch);
            _dState.text = TreeV2Text.State(s, target);
            _dDesc.text = TreeV2Text.Description(n);
            _dExtra.text = ExtraLines(s, target);

            bool purchased = n.State == TreeNodeState.Purchased;
            // AD M1 (12.10): an owned node's plate stood empty - a 376 x 190 black box under its words. OWNED is the card's state
            // line; the card ends at the description (LayoutDetail gives the closed plate no height).
            _pricePlate.gameObject.SetActive(!purchased);
            _dPrice.text = purchased ? string.Empty : TreeV2Text.Gold(n.Price);
            _dDiscount.text = purchased ? string.Empty : TreeV2Text.Discount(n);
            _buy.gameObject.SetActive(!purchased);
            _buy.SetInteractable(n.State == TreeNodeState.Available);
            if (_buyPrompt != null)
            {
                _buyPrompt.gameObject.SetActive(n.State == TreeNodeState.Available);
            }

            _target.SetText(n.IsTarget ? UiStrings.Get("tree.v2.screen.target_clear", "CLEAR TARGET") : UiStrings.Get("tree.v2.screen.target", "SET TARGET"));
            _target.gameObject.SetActive(!purchased);
            _target.SetInteractable(!purchased);
            LayoutDetail();
        }

        private string ExtraLines(TreeScreenSnapshot s, int index)
        {
            TreeNodeView n = s.Nodes[index];
            var sb = new StringBuilder(96);
            if (n.IsTarget)
            {
                sb.Append(UiStrings.Get("tree.v2.screen.target_badge", "TARGET"));
            }
            else if (n.OnTargetPath)
            {
                sb.Append(UiStrings.Get("tree.v2.screen.target_path", "ON TARGET PATH"));
            }

            if (n.IsAwakeningVariant)
            {
                Line(sb).Append(UiStrings.Get("awakening.title", "AWAKENING"));
            }

            if (n.Branch == TreeBranch.Element && n.Tier == 1)
            {
                Line(sb).Append(UiStrings.Format("tree.v2.screen.elements", "value", UiStrings.Number(CountBits((int)s.ElementsPicked)), "max",
                    UiStrings.Number(Mathf.Max(1, s.ElementCap)), "ELEMENTS {value}/{max}"));
                Line(sb).Append(UiStrings.Format("tree.v2.screen.fork_elements", "max", UiStrings.Number(Mathf.Max(1, s.ElementCap)),
                    "Pick up to {max} elements. The others close for this run."));
            }

            for (int k = 0; k < s.Synergies.Length; k++)
            {
                TreeSynergyView syn = s.Synergies[k];
                if ((n.Synergies & syn.Synergy) == 0 || !(syn.Revealed || syn.Active))
                {
                    continue;
                }

                Line(sb).Append(TreeV2Text.SynergyName(syn));
                if (syn.Active)
                {
                    sb.Append(" · ").Append(UiStrings.Get("tree.v2.screen.synergy_active", "ACTIVE"));
                }
            }

            return sb.ToString();
        }

        private static StringBuilder Line(StringBuilder sb)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            return sb;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            for (; value != 0; value &= value - 1)
            {
                count++;
            }

            return count;
        }

        private void SetPreview(Sprite sprite, Color pigment, bool dim)
        {
            _previewIcon.sprite = sprite != null ? sprite : UiSprites.Disc;
            _previewIcon.color = sprite == null ? WithAlpha(pigment, 0.6f) : dim ? WithAlpha(UiTokens.Get(UiColor.TextMuted), TreeArt.LockedIconAlpha) : Color.white;
        }

        /// <summary>
        /// The card's stack, measured at the live text scale (U2): name (≤ 2 lines), kind, state, description, extra lines and the
        /// price plate; the static preview takes what is left (72–208), and drops out before any word is cut.
        /// </summary>
        private void LayoutDetail()
        {
            float width = Mathf.Max(1f, _detail.rect.width - CardPad * 2f);
            float height = _detail.rect.height;
            float name = Measure(_dName, width, 2);
            float kind = Measure(_dKind, width, 1);
            float state = string.IsNullOrEmpty(_dState.text) ? 0f : Measure(_dState, width, 2);
            float desc = string.IsNullOrEmpty(_dDesc.text) ? 0f : Measure(_dDesc, width, 99);
            float extra = string.IsNullOrEmpty(_dExtra.text) ? 0f : Measure(_dExtra, width, 99);
            float plate = _pricePlate.gameObject.activeSelf ? PricePlateHeight(width) : 0f;
            float texts = name + kind + state + desc + extra;
            int bands = (name > 0f ? 1 : 0) + (kind > 0f ? 1 : 0) + (state > 0f ? 1 : 0) + (desc > 0f ? 1 : 0) + (extra > 0f ? 1 : 0);

            // fix1 (U2 at TextScale 1.25 × Mac UI 1.2): when the words and the price plate do not fit the card with its usual air,
            // the air gives way (top pad 20 → 8, gaps 8 → 4) before anything else; no word is ever made smaller or cut.
            float top = CardPad, gap = (int)UiSpace.S;
            if (top + texts + bands * gap + plate > height)
            {
                top = CardPadTight;
                gap = CardGapTight;
            }

            float words = top + texts + bands * gap + plate + CardPad;
            float preview = Mathf.Clamp(height - words - (int)UiSpace.M, 0f, PreviewMax);
            if (preview < PreviewMin)
            {
                preview = 0f;
            }

            _previewIcon.enabled = preview > 0f;
            float y = top;
            if (preview > 0f)
            {
                Set(_previewIcon.rectTransform, (_detail.rect.width - preview) * 0.5f, y, preview, preview);
                y += preview + (int)UiSpace.M;
            }

            y = Stack(_dName, y, width, name, gap);
            y = Stack(_dKind, y, width, kind, gap);
            y = Stack(_dState, y, width, state, gap);
            y = Stack(_dDesc, y, width, desc, gap);
            y = Stack(_dExtra, y, width, extra, gap);
            if (plate > 0f)
            {
                Set(_pricePlate, CardPad - 8f, Mathf.Max(y, height - CardPad - plate), width + 16f, plate);
                LayoutPricePlate(width + 16f, plate);
            }
        }

        private float Stack(TMP_Text text, float y, float width, float height, float gap)
        {
            text.gameObject.SetActive(height > 0f);
            if (height <= 0f)
            {
                return y;
            }

            Set(text.rectTransform, CardPad, y, width, height);
            return y + height + gap;
        }

        /// <summary>A text's band: its words measured at <paramref name="width"/>, at most <paramref name="lines"/> lines (U2).</summary>
        private static float Measure(TMP_Text text, float width, int lines)
        {
            if (string.IsNullOrEmpty(text.text))
            {
                return 0f;
            }

            float line = Mathf.Ceil(text.GetPreferredValues("Ag").y);
            float h = Mathf.Ceil(text.GetPreferredValues(text.text, width, 0f).y);
            return Mathf.Min(Mathf.Max(line, h), line * lines) + 2f;
        }

        /// <summary>
        /// fix2 · the price plate: the price and its discount on a line of their own, then BUY (its Submit glyph outside its left
        /// edge) and TARGET below, right-aligned. The glyph and the price never share a row, so they cannot meet in any language,
        /// text scale or price length (the 12.10 capture: ENTER drawn over "45 GOLD").
        /// </summary>
        private float PricePlateHeight(float width)
        {
            float control = UiBrushTokens.ControlHeight;
            return (int)UiSpace.M * 2f + PriceBand() + (int)UiSpace.XS + control * 2f + (int)UiSpace.S;
        }

        /// <summary>The price line's height: the price (Numeric) or the discount (Caption, × TextScale), whichever is taller.</summary>
        private float PriceBand()
        {
            return Mathf.Ceil(Mathf.Max(_dPrice.GetPreferredValues("0").y, _dDiscount.GetPreferredValues("0%").y)) + 2f;
        }

        private void LayoutPricePlate(float width, float height)
        {
            UiBrushKit.FitWidth(_buy);
            UiBrushKit.FitWidth(_target);
            float pad = (int)UiSpace.M;
            float inner = Mathf.Max(1f, width - pad * 2f);
            float control = UiBrushTokens.ControlHeight;
            float band = PriceBand();
            float priceWidth = string.IsNullOrEmpty(_dPrice.text) ? 0f : Mathf.Min(inner, Mathf.Ceil(_dPrice.GetPreferredValues(_dPrice.text).x) + 2f);
            Set(_dPrice.rectTransform, pad, pad, Mathf.Max(1f, priceWidth), band);
            float discountX = pad + priceWidth + (priceWidth > 0f ? (int)UiSpace.S : 0f);
            Set(_dDiscount.rectTransform, discountX, pad, Mathf.Max(1f, pad + inner - discountX), band);

            var buy = (RectTransform)_buy.transform;
            var target = (RectTransform)_target.transform;
            float y = pad + band + (int)UiSpace.XS;
            float w = Mathf.Min(buy.sizeDelta.x, inner - UiRunTreePanel.SubmitGlyphRoom());
            Set(buy, pad + inner - w, y, w, control);
            y += control + (int)UiSpace.S;
            float tw = Mathf.Min(target.sizeDelta.x, inner);
            Set(target, pad + inner - tw, y, tw, control);
        }

        /// <summary>
        /// Tests (TextScale 1.25 / TR overflow): the detail card's words set directly - the longest strings of the R4 pack before
        /// B1 brings them - and laid out as a node's would be. True when no word is cut and every line stays inside the card.
        /// </summary>
        internal bool ShowTextForTests(string name, string kind, string state, string description, string extra)
        {
            _dName.text = name ?? string.Empty;
            _dKind.text = kind ?? string.Empty;
            _dState.text = state ?? string.Empty;
            _dDesc.text = description ?? string.Empty;
            _dExtra.text = extra ?? string.Empty;
            _pricePlate.gameObject.SetActive(true);
            LayoutDetail();
            Canvas.ForceUpdateCanvases();
            float height = _detail.rect.height;
            TMP_Text[] texts = { _dName, _dKind, _dState, _dDesc, _dExtra };
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text t = texts[i];
                if (!t.gameObject.activeSelf || string.IsNullOrEmpty(t.text))
                {
                    continue;
                }

                t.ForceMeshUpdate();
                RectTransform r = t.rectTransform;
                float top = -r.anchoredPosition.y;
                if (t.isTextTruncated || top + r.rect.height > height + 0.5f || t.preferredHeight > r.rect.height + 1f)
                {
                    return false;
                }
            }

            // The price plate (BUY, TARGET) stays inside the card too.
            return -_pricePlate.anchoredPosition.y + _pricePlate.rect.height <= height + 0.5f;
        }

        /// <summary>Tests (fix2): a price and discount set directly - the longest a node can show - and the plate laid out again.</summary>
        internal void ShowPriceForTests(string price, string discount)
        {
            _dPrice.text = price ?? string.Empty;
            _dDiscount.text = discount ?? string.Empty;
            _pricePlate.gameObject.SetActive(true);
            _buy.gameObject.SetActive(true);
            _target.gameObject.SetActive(true);
            if (_buyPrompt != null)
            {
                _buyPrompt.gameObject.SetActive(true);
            }

            LayoutDetail();
            Canvas.ForceUpdateCanvases();
        }

        internal RectTransform PricePlateRect => _pricePlate;
        internal TMP_Text PriceText => _dPrice;
        internal TMP_Text DiscountText => _dDiscount;
        /// <summary>BUY's Submit glyph (A / ENTER) beside it; null when the device has none.</summary>
        internal RectTransform BuyGlyphRect => _buyPrompt != null ? (RectTransform)_buyPrompt.transform : null;

        // ================================================================== header plate / actions

        private void LayoutHeaderPlate(float width)
        {
            float pad = (int)UiSpace.M;
            float h = HeaderPlateHeight;
            var symbols = (RectTransform)_symbols.transform;
            Set(symbols, width - pad - symbols.sizeDelta.x, (h - UiBrushTokens.ControlHeight) * 0.5f, symbols.sizeDelta.x, UiBrushTokens.ControlHeight);
            float x = pad;
            Set(_goldIcon.rectTransform, x, (h * 0.5f - 28f) * 0.5f + 4f, 28f, 28f);
            x += 28f + (int)UiSpace.S;
            float room = Mathf.Max(1f, width - pad - symbols.sizeDelta.x - (int)UiSpace.M - x);
            Set(_gold.rectTransform, x, 4f, room * 0.55f, h * 0.5f);
            Set(_ash.rectTransform, x + room * 0.55f + (int)UiSpace.S, 4f, Mathf.Max(1f, room * 0.45f - (int)UiSpace.S), h * 0.5f);
            Set(_targetLine.rectTransform, pad, h * 0.5f + 2f, Mathf.Max(1f, width - pad * 3f - symbols.sizeDelta.x), h * 0.5f - 6f);
        }

        /// <summary>The caption's band, its words measured at the block's inner width (U2: one line or two, never cut).</summary>
        private float CaptionBand(float inner)
        {
            float line = UiFactory.LineHeight(UiType.Caption);
            if (string.IsNullOrEmpty(_stripCaption.text) || inner <= 1f)
            {
                return line;
            }

            return Mathf.Max(line, Mathf.Ceil(_stripCaption.GetPreferredValues(_stripCaption.text, inner, 0f).y) + 2f);
        }

        /// <summary>
        /// fix1: the strip caption and a refusal sentence share one band at the block's top - the refusal (two lines) stands in
        /// for the caption while it is shown - so the block no longer keeps an empty two-line band below CONTINUE.
        /// </summary>
        private float MessageBand(float inner)
        {
            return Mathf.Max(CaptionBand(inner), UiFactory.LineHeight(UiType.Caption) * 2f);   // a refusal sentence: two lines
        }

        private float ActionsHeight(float width)
        {
            float inner = width - (int)UiSpace.M * 2f;
            float control = UiBrushTokens.ControlHeight;
            return (int)UiSpace.M * 2f + MessageBand(inner) + control * 3f + ActionGap * 3f;
        }

        private void LayoutActions(float width, float height)
        {
            float pad = (int)UiSpace.M;
            float inner = width - pad * 2f;
            float band = MessageBand(inner);
            float control = UiBrushTokens.ControlHeight;
            Set(_stripCaption.rectTransform, pad, pad, inner, band);
            Set(_note.rectTransform, pad, pad, inner, band);
            ShowMessage();
            float y = pad + band + ActionGap;
            FitAction(_reroll, _rerollCost, inner, y, 0f);
            y += control + ActionGap;
            FitAction(_hearth, _hearthCost, inner, y, 0f);
            y += control + ActionGap;
            FitAction(_continue, null, inner, y, UiInputGlyphs.ReservedWidth(UiPromptAction.Continue) + (int)UiSpace.S);
        }

        /// <summary>The band shows the refusal while there is one, otherwise the strip caption (fix1).</summary>
        private void ShowMessage()
        {
            bool refusal = !string.IsNullOrEmpty(_note.text);
            _note.enabled = refusal;
            _stripCaption.enabled = !refusal;
        }

        /// <summary>A C11 at its measured width (U2), from the plate's left (after <paramref name="glyphRoom"/>), its price after it.</summary>
        private void FitAction(UiButton button, TMP_Text cost, float inner, float y, float glyphRoom)
        {
            float pad = (int)UiSpace.M;
            float w = Mathf.Min(UiBrushKit.FitWidth(button), inner - glyphRoom);
            var rect = (RectTransform)button.transform;
            Set(rect, pad + glyphRoom, y, w, UiBrushTokens.ControlHeight);
            if (cost != null)
            {
                float x = pad + glyphRoom + w + (int)UiSpace.S;
                Set(cost.rectTransform, x, y, Mathf.Max(0f, pad + inner - x), UiBrushTokens.ControlHeight);
            }
        }

        /// <summary>
        /// U2: the action's full words ("REROLL · 10 GOLD") when they fit the plate as drawn, otherwise the verb on the control
        /// and the price beside it ("YENİDEN ÇEK" + "10 ALTIN") - never a cut word at TextScale 1.25 on the narrow panel.
        /// </summary>
        private void SetActionWords(UiButton button, TMP_Text cost, string full, string word, string price, UiColor priceColour)
        {
            float inner = _layout.DetailW - (int)UiSpace.M * 2f;
            button.SetText(full);
            if (UiBrushKit.FitWidth(button) <= inner)
            {
                cost.text = string.Empty;
                return;
            }

            button.SetText(word);
            cost.text = price;
            cost.color = UiTokens.Get(priceColour);
        }

        private void RefreshActions(TreeScreenSnapshot s)
        {
            int percent = StripPercent(s);
            string strip = UiStrings.Get("tree.v2.screen.recommended", "RECOMMENDED");
            _stripCaption.text = percent > 0
                ? strip + " · " + UiStrings.Format("tree.v2.screen.recommended_caption", "value", UiStrings.Number(percent), "{value}% off this round")
                : strip;

            bool canReroll = s.RerollAvailable || s.Gold >= s.RerollCost;
            SetActionWords(_reroll, _rerollCost,
                canReroll
                    ? UiStrings.Format("tree.v2.screen.reroll", "value", UiStrings.Number(s.RerollCost), "REROLL · {value} GOLD")
                    : UiStrings.Format("shop.reroll.need", "value", UiStrings.Number(s.RerollCost), "REROLL · NEED {value} GOLD"),
                UiStrings.Get("shop.reroll", "REROLL"), TreeV2Text.Gold(s.RerollCost), canReroll ? UiColor.StateGold : UiColor.TextSecondary);
            _reroll.SetInteractable(s.RerollAvailable);

            bool stoke = s.HearthAvailable;
            string stokeWord = UiStrings.Get("tree.action.stoke", "STOKE THE HEARTH");
            string state = TreeV2Text.HearthState(s);   // B5a: the price, THIS ROUND n/m at the round's cap, MAX at the run's
            SetActionWords(_hearth, _hearthCost,
                stoke ? UiStrings.Format("tree.v2.screen.hearth", "value", UiStrings.Number(s.HearthCost), "STOKE THE HEARTH · {value} GOLD") : stokeWord + " · " + state,
                stokeWord, state, stoke && s.Gold >= s.HearthCost ? UiColor.StateGold : UiColor.TextSecondary);
            _hearth.SetInteractable(stoke && s.Gold >= s.HearthCost);
            _continue.SetText(UiStrings.Get("shop.continue", "CONTINUE"));
            PlaceDetailColumn();
        }

        /// <summary>
        /// The strip's percent for the caption: the one percent every discounted strip node shares, else 0 (the caption then reads
        /// RECOMMENDED alone). S-04 B5a caps the strip discount in gold (25), so an expensive node's own percent is lower; each
        /// node's chip and price plate show its own true discount.
        /// </summary>
        internal static int StripPercent(TreeScreenSnapshot s)
        {
            int shared = 0;
            for (int i = 0; i < s.Nodes.Length; i++)
            {
                ref readonly TreeNodeView n = ref s.Nodes[i];
                if (!n.Recommended || n.State == TreeNodeState.Purchased || n.Cost <= 0 || n.Price >= n.Cost)
                {
                    continue;
                }

                int percent = Mathf.RoundToInt((1f - n.Price / (float)n.Cost) * 100f);
                if (shared == 0)
                {
                    shared = percent;
                }
                else if (percent != shared)
                {
                    return 0;
                }
            }

            return shared;
        }

        private void ToggleLegend()
        {
            bool show = !_legend.gameObject.activeSelf;
            _legend.gameObject.SetActive(show);
            if (show)
            {
                var sb = new StringBuilder(256);
                sb.Append(UiStrings.Get("tree.v2.state.Available", "AVAILABLE")).Append('\n');
                sb.Append(UiStrings.Format("tree.v2.state.Unaffordable", "value", "N", "{value} GOLD SHORT")).Append('\n');
                sb.Append(UiStrings.Get("tree.v2.state.Purchased", "OWNED")).Append('\n');
                sb.Append(UiStrings.Format("tree.v2.state.Fogged", "round", "N", "UNLOCKS AT ROUND {round}")).Append('\n');
                sb.Append(UiStrings.Format("tree.v2.state.Excluded", "node", "…", "CLOSED BY {node}")).Append('\n');
                sb.Append(UiStrings.Get("tree.v2.state.AwakeningLocked", "UNLOCKS IN AWAKENING")).Append('\n');
                sb.Append(UiStrings.Get("tree.v2.screen.recommended", "RECOMMENDED")).Append('\n');
                sb.Append(UiStrings.Get("tree.v2.screen.target_badge", "TARGET"));
                _legendText.text = sb.ToString();
            }
        }

        // ================================================================== commands

        private void OnNodeActivated(int index)
        {
            NodeView v = index >= 0 && index < _nodes.Length ? _nodes[index] : null;
            if (v == null)
            {
                return;
            }

            if (v.Button.LastActivationFromPointer)
            {
                // AD §5.5: a click selects; BUY is its own control.
                FocusNode(index);
                return;
            }

            // A / Enter on a node: buy it when it is buyable (AD §5.5 "A alır"); otherwise the refusal says why.
            Buy(index);
        }

        private void OnSynergyActivated(int index)
        {
            FocusSynergy(index);
        }

        private void OnBuyPressed()
        {
            if (_detailTarget >= 0 && Snapshot != null && _detailTarget < Snapshot.Nodes.Length)
            {
                Buy(_detailTarget);
            }
        }

        private void OnTargetPressed()
        {
            if (_detailTarget >= 0 && Snapshot != null && _detailTarget < Snapshot.Nodes.Length)
            {
                ToggleTarget(_detailTarget);
            }
        }

        /// <summary>TryBuy on node <paramref name="index"/>; a purchase stamps its hanko (or the synergy it completes); a refusal says why.</summary>
        public bool Buy(int index)
        {
            TreeScreenSnapshot s = Snapshot;
            if (_commands == null || s == null || index < 0 || index >= s.Nodes.Length)
            {
                return false;
            }

            TreeNodeState before = s.Nodes[index].State;
            TreeSynergy activeBefore = ActiveSynergies(s);
            bool bought = _commands.TryBuy(index);
            if (!bought)
            {
                UiSound.Play(UiSoundKind.Denied);
                _note.text = before == TreeNodeState.Unaffordable ? UiStrings.Get("tree.refuse.gold", "Not enough gold for that yet. Nothing was taken.") : string.Empty;
                Refresh();
                return false;
            }

            // The hanko's and a synergy's sound are TreeEvents' (NodePurchased / SynergyUnlocked, raised by the provider; S-08):
            // the screen raises no UiEvent for a purchase, so nothing is voiced twice.
            _note.text = string.Empty;
            Refresh();
            TreeSynergy completed = ActiveSynergies(Snapshot) & ~activeBefore;
            Stamp(index, completed);
            return true;
        }

        /// <summary>One ceremony per screen (AD §5.3): a completed synergy stamps its badge, otherwise the node takes the hanko.</summary>
        private void Stamp(int index, TreeSynergy completed)
        {
            if (completed != TreeSynergy.None)
            {
                for (int k = 0; k < _synergies.Length; k++)
                {
                    if ((Snapshot.Synergies[k].Synergy & completed) != 0 && _synergies[k].BadgeSeal.sprite != null)
                    {
                        Image badgeSeal = _synergies[k].BadgeSeal;
                        badgeSeal.enabled = true;
                        UiPresets.StampIn(badgeSeal.rectTransform, 0f, GetInstanceID());
                        return;
                    }
                }
            }

            NodeView v = _nodes[index];
            if (v.StampSeal.sprite == null)
            {
                return;
            }

            v.StampSeal.enabled = true;
            UiPresets.StampIn(v.StampSeal.rectTransform, 0f, GetInstanceID());
            // AD M2 (12.10): the stamp stayed over the icon until the screen closed, and a node bought in an earlier round had none
            // - one state, two looks. It lifts within 200 ms of its contact (held StampHold, then Fast); the next refresh puts it
            // away, so what stays is the purchased look every owned node shares (pigment wash + corner seal).
            UiMotion.TintAlpha(v.StampSeal, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor,
                UiPresets.StampContactSeconds + StampHold, UiClock.Unscaled, GetInstanceID());
        }

        /// <summary>AD M2: the stamp's hold after its contact before it lifts (hold + Fast 140 ms ≤ the AD's 200 ms).</summary>
        internal const float StampHold = 0.05f;

        /// <summary>SetTarget on node <paramref name="index"/>, or −1 when it already is the Target (clears it).</summary>
        public void ToggleTarget(int index)
        {
            TreeScreenSnapshot s = Snapshot;
            if (_commands == null || s == null || index < 0 || index >= s.Nodes.Length || s.Nodes[index].State == TreeNodeState.Purchased)
            {
                return;
            }

            _commands.SetTarget(s.TargetIndex == index ? -1 : index);
            UiEvents.Raise(UiEvent.ChoiceSelected, "TreeTarget");
            Refresh();
        }

        private void OnRerollPressed()
        {
            if (_commands == null)
            {
                return;
            }

            bool rerolled = _commands.TryReroll();
            if (rerolled)
            {
                UiEvents.Raise(UiEvent.TreeRerolled, "TreeStrip");   // not UpgradeRerolled (the old offer's cue): one sound
            }
            else
            {
                UiSound.Play(UiSoundKind.Denied);
            }

            _note.text = rerolled ? string.Empty : UiStrings.Get("shop.refuse.reroll_gold", "Not enough gold to reroll. Nothing was spent.");
            Refresh();
        }

        private void OnHearthPressed()
        {
            if (_commands == null)
            {
                return;
            }

            bool stoked = _commands.TryStokeHearth();
            if (stoked)
            {
                UiEvents.Raise(UiEvent.UpgradePurchased, "Hearth");   // the Hearth has no TreeEvents event: the shop's purchase cue
            }
            else
            {
                UiSound.Play(UiSoundKind.Denied);
            }

            Refresh();
        }

        /// <summary>CONTINUE (one key, decision 1): the provider's Continue, then the host closes the round-end flow.</summary>
        public void Continue()
        {
            if (_commands == null || !_open)
            {
                return;
            }

            _commands.Continue();
            Continued?.Invoke();
        }

        // ================================================================== input

        /// <summary>Polled by the host screen while the tree is up and no pause is over it: shortcuts, then the focus guarantee.</summary>
        public void HandleKeys()
        {
            if (!_open)
            {
                return;
            }

            int step = UiInput.BranchStepPressed();
            if (step != 0)
            {
                HandleKey(step < 0 ? TreeV2Key.PreviousBranch : TreeV2Key.NextBranch);
            }
            else if (UiInput.SynergyViewPressed())
            {
                HandleKey(TreeV2Key.Synergies);
            }
            else if (UiInput.ContinuePressed())
            {
                HandleKey(TreeV2Key.Continue);
            }
            else if (UiInput.TargetPressed())
            {
                HandleKey(TreeV2Key.Target);
            }

            UiInput.ReclaimFocus(FocusTarget(), _root);
        }

        public void HandleKey(TreeV2Key key)
        {
            TreeScreenSnapshot s = Snapshot;
            if (s == null)
            {
                return;
            }

            switch (key)
            {
                case TreeV2Key.PreviousBranch:
                case TreeV2Key.NextBranch:
                {
                    int row = _focus >= 0 && _focus < s.Nodes.Length ? _layout.RowOfNode(_focus) : 0;
                    int target = TreeV2Nav.BranchJump(_layout, s, row, key == TreeV2Key.PreviousBranch ? -1 : 1, _remembered);
                    if (target >= 0)
                    {
                        FocusNode(target);
                        UiEvents.Raise(UiEvent.ChoiceSelected, "TreeBranch");
                    }

                    break;
                }

                case TreeV2Key.Synergies:
                    if (_nav.IsSynergy(_focus))
                    {
                        // fix1: back to the last NODE. _lastTreeFocus follows the badges too (the actions' Left), so it named the
                        // badge itself and the second Y kept the focus in the column.
                        int back = _lastNode >= 0 && _lastNode < s.Nodes.Length ? _lastNode : FirstUsefulNode(s);
                        FocusNode(back);
                    }
                    else if (_synergies.Length > 0)
                    {
                        FocusSynergy(0);
                    }

                    break;
                case TreeV2Key.Continue:
                    if (_open)
                    {
                        UiSound.Play(UiSoundKind.Confirm);   // the key's confirm (the CONTINUE button voices its own press)
                    }

                    Continue();
                    break;
                case TreeV2Key.Target:
                    if (_focus >= 0 && _focus < s.Nodes.Length)
                    {
                        ToggleTarget(_focus);
                    }

                    break;
            }
        }

        /// <summary>Gives node <paramref name="index"/> the focus and the detail panel (tests, captures, the branch jump, a click).</summary>
        public void FocusNode(int index)
        {
            Selectable at = TargetSelectable(index);
            if (at == null)
            {
                return;
            }

            OnTargetSelected(index);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != at.gameObject)
            {
                UiInput.Focus(at);
            }
        }

        public void FocusSynergy(int index)
        {
            if (index < 0 || index >= _synergies.Length)
            {
                return;
            }

            FocusNode(_nodes.Length + index);
        }

        /// <summary>A node or badge took the focus (pad, keyboard, hover or code): the detail follows, the branch remembers it.</summary>
        internal void OnTargetSelected(int target)
        {
            if (target < 0 || target >= _nav.Count)
            {
                return;
            }

            _focus = target;
            _lastTreeFocus = TargetSelectable(target);
            if (target < _nodes.Length)
            {
                _lastNode = target;
                int row = _layout.RowOfNode(target);
                if (row >= 0)
                {
                    _remembered[row] = target;
                }
            }

            if (_legend.gameObject.activeSelf)
            {
                _legend.gameObject.SetActive(false);
            }

            ShowDetail(target);
            LinkDetailNavigation();
        }

        /// <summary>Right-click on a node: the Target (AD §5.5; the old pin's mouse gesture).</summary>
        internal void OnTargetContext(int target)
        {
            if (target >= 0 && target < _nodes.Length)
            {
                ToggleTarget(target);
            }
        }

        private Selectable TargetSelectable(int target)
        {
            if (target < 0)
            {
                return null;
            }

            if (target < _nodes.Length)
            {
                return _nodes[target] != null ? _nodes[target].Button : null;
            }

            int k = target - _nodes.Length;
            return k < _synergies.Length && _synergies[k] != null ? _synergies[k].Button : null;
        }

        private int FirstUsefulNode(TreeScreenSnapshot s)
        {
            int target = s.TargetIndex;
            if (target >= 0 && target < s.Nodes.Length && s.Nodes[target].State == TreeNodeState.Available)
            {
                return target;
            }

            for (int r = 0; r < TreeV2Rows.Count; r++)
            {
                for (int i = 0; i < s.Nodes.Length; i++)
                {
                    if (_layout.RowOfNode(i) == r && s.Nodes[i].Recommended && s.Nodes[i].State == TreeNodeState.Available)
                    {
                        return i;
                    }
                }
            }

            int first = TreeV2Nav.FirstOfRow(_layout, s, 0, null);
            return first >= 0 ? first : (s.Nodes.Length > 0 ? 0 : -1);
        }

        /// <summary>Opening focus: the Target when buyable, else a recommended buyable node, else a buyable one, else CONTINUE.</summary>
        public void FocusFirstUseful()
        {
            TreeScreenSnapshot s = Snapshot;
            if (s == null)
            {
                UiInput.Focus(_continue);
                return;
            }

            int node = FirstUsefulNode(s);
            bool anyBuyable = false;
            for (int i = 0; i < s.Nodes.Length && !anyBuyable; i++)
            {
                anyBuyable = s.Nodes[i].State == TreeNodeState.Available;
            }

            if (node >= 0 && anyBuyable)
            {
                FocusNode(node);
            }
            else
            {
                if (node >= 0)
                {
                    OnTargetSelected(node);
                }

                UiInput.Focus(_continue);
            }
        }

        /// <summary>The grid's explicit links (the layout's nearest neighbours) and the detail column's chain.</summary>
        private void LinkNavigation()
        {
            Selectable firstAction = FirstLiveAction();
            for (int t = 0; t < _nav.Count; t++)
            {
                Selectable at = TargetSelectable(t);
                if (at == null)
                {
                    continue;
                }

                UnityEngine.UI.Navigation nav = at.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                nav.selectOnUp = TargetSelectable(_nav.Next(t, TreeV2Nav.Up));
                nav.selectOnDown = TargetSelectable(_nav.Next(t, TreeV2Nav.Down));
                nav.selectOnLeft = TargetSelectable(_nav.Next(t, TreeV2Nav.Left));
                Selectable right = TargetSelectable(_nav.Next(t, TreeV2Nav.Right));
                nav.selectOnRight = right != null ? right : firstAction;
                at.navigation = nav;
            }

            LinkDetailNavigation();
        }

        private Selectable FirstLiveAction()
        {
            Selectable[] chain = ActionChain();
            for (int i = 0; i < chain.Length; i++)
            {
                if (chain[i] != null)
                {
                    return chain[i];
                }
            }

            return _continue;
        }

        private Selectable[] ActionChain()
        {
            return new Selectable[]
            {
                Live(_buy), Live(_target), Live(_reroll), Live(_hearth), Live(_continue)
            };
        }

        private static Selectable Live(UiButton button)
        {
            return button != null && button.gameObject.activeInHierarchy && button.IsInteractable() ? button : null;
        }

        /// <summary>BUY → TARGET → REROLL → HEARTH → CONTINUE (live ones), SYMBOLS above them; Left goes back into the tree.</summary>
        private void LinkDetailNavigation()
        {
            Selectable[] chain = ActionChain();
            UiInput.ChainVertical(false, chain);
            Selectable first = null;
            for (int i = 0; i < chain.Length; i++)
            {
                if (chain[i] == null)
                {
                    continue;
                }

                if (first == null)
                {
                    first = chain[i];
                }

                UnityEngine.UI.Navigation nav = chain[i].navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                nav.selectOnLeft = _lastTreeFocus;
                if (chain[i] == first)
                {
                    nav.selectOnUp = _symbols;
                }

                chain[i].navigation = nav;
            }

            UnityEngine.UI.Navigation symbols = _symbols.navigation;
            symbols.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            symbols.selectOnDown = first;
            symbols.selectOnLeft = _lastTreeFocus;
            symbols.selectOnUp = null;
            symbols.selectOnRight = null;
            _symbols.navigation = symbols;
        }

        // ================================================================== helpers

        private static void TopLeft(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
        }

        /// <summary>Block space (top-left origin, y down) to a top-left anchored rect.</summary>
        private static void Set(RectTransform rect, float x, float y, float width, float height)
        {
            TopLeft(rect);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static void Centre(RectTransform rect, float size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = Vector2.zero;
        }

        private static void Corner(RectTransform rect, Vector2 corner, float size, float inset)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(corner.x > 0.5f ? -inset : inset, corner.y > 0.5f ? -inset : inset);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }

    /// <summary>A node's / badge's own input: the focus it takes drives the detail; a right-click sets the Target (AD §5.5).</summary>
    public sealed class UiTreeV2NodeInput : MonoBehaviour, ISelectHandler, IPointerClickHandler
    {
        private UiTreeScreenV2 _panel;
        private int _index;
        private bool _synergy;

        public void Configure(UiTreeScreenV2 panel, int index, bool synergy)
        {
            _panel = panel;
            _index = index;
            _synergy = synergy;
        }

        private int Target => _synergy && _panel != null ? _panel.Nav.NodeCount + _index : _index;

        public void OnSelect(BaseEventData eventData)
        {
            if (_panel != null)
            {
                _panel.OnTargetSelected(Target);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right && _panel != null && !_synergy)
            {
                _panel.OnTargetContext(_index);
            }
        }
    }
}
