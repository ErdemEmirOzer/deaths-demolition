using System;
using System.Globalization;
using DD.Prototype.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 R1-C · the in-run tree on the round-end shop (M4-R1 N14, WP-077 §2 / §2.5, PD §5, S09-CONSTRAINTS).
    /// The hybrid offer (WP-077 K1) is unchanged: three frontier nodes are for sale, the Hearth is bought directly,
    /// and the pin guarantees a path. This screen is where the player sees all of it at once:
    /// - Eight columns of tree, four of detail. Two lanes (WEAPON / BODY) behind tabs (Q / E). Each lane is a 4 × 4
    ///   grid of seals with a one-line name and a state word; no scrolling.
    /// - The seven states are shape + glyph + word (PD §5.1): LOCKED, NEEDS x, AVAILABLE, N GOLD SHORT, OWNED,
    ///   CLOSED BY x, MAX. Overlays: OFFER (the gold ring and chip: the nodes this offer sells), the pin, NEW.
    /// - The edges are one Graphic (UiTreeEdges): hairline, owned Rim 2, dashed while a prerequisite is missing,
    ///   4 px along the pinned path.
    /// - The detail card names the focused node: kind, name, pips, what it does, the state sentence, the price. BUY is
    ///   its one Primary (STOKE on the Hearth), with PIN / UNPIN and REFORGE on an owned fork. UNDO LAST lives in the
    ///   screen's footer, beside CONTINUE, because it is about the offer rather than the node.
    /// The layout is measured from the host and the text (TextScale, UI scale): 112 px rows and 64 px seals where they
    /// fit (1080p), a compact row with the seal beside its words where they do not (Mac 1.2 × 1.25). Every fact comes
    /// from S-04's RunTreeSnapshot and every action is a RoundUpgradeShop command quoting the offer's session and
    /// revision; the screen decides nothing.
    /// </summary>
    public sealed class UiRunTreePanel : MonoBehaviour
    {
        public const float SealSize = 64f;
        public const float RowPitch = 112f;
        public const float TierPitch = 214f;
        public const float NodeWidth = 196f;
        public const int Tiers = 4;
        public const int Rows = 4;
        public const float OpeningBeatSeconds = 0.12f;

        private const float TabHeight = 44f;
        private const float TabWidth = 168f;
        private const float CompactSealMax = 48f;

        /// <summary>12b (AD R1-C SHOULD-1): the lock on a LOCKED seal, and the band a NEEDS glyph keeps of its outline (SDF units, 0.5 = edge).</summary>
        private const float LockSize = 18f;
        private const float HollowGlyphBand = 0.2f;   // ≈ 2.4 px of the 64 px cell (spread 6) - the ink outline is 0.25
        private const float MinSeal = 40f;
        private const float EdgeGap = 24f;
        private const float BuyWidth = 204f;
        private const float BuyChrome = 132f;   // the prompt keycap, its gap and the button's side padding

        private sealed class NodeView
        {
            public UiButton Button;
            public RectTransform Rect;
            public RectTransform Seal;
            public Image Fill;
            public Image Ring;
            public Image Glyph;
            public Image Cross;
            public Image Lock;
            public Image Mark;
            public GameObject Pin;
            public GameObject Offer;
            public GameObject New;
            public TMP_Text Count;
            public CanvasGroup Group;
            public TMP_Text Name;
            public TMP_Text State;
            public int Index = -1;
        }

        private readonly NodeView[] _views = new NodeView[Tiers * Rows];
        private readonly RunTreeSnapshot _snap = new RunTreeSnapshot();
        private readonly int[] _cell = new int[Tiers * Rows];
        private readonly bool[] _onPath = new bool[RunTreeSnapshot.Capacity];
        private RoundUpgradeShop _shop;
        private RunTreeLane _lane = RunTreeLane.Weapon;

        private RectTransform _tree;
        private RectTransform _detail;
        private UiButton _tabWeapon;
        private UiButton _tabBody;
        private TMP_Text _laneKeys;
        private TMP_Text _hearthCount;
        private RectTransform _grid;
        // One edge Graphic per tier (12b, AD R1-C re-check PD §5.4): an edge inks in with the tier it leads into, so the
        // Opening Beat never draws a join into an empty slot. Same material, no texture: the tiers still batch as one.
        private readonly UiTreeEdges[] _edgeTiers = new UiTreeEdges[Tiers];
        private readonly CanvasGroup[] _edgeGroups = new CanvasGroup[Tiers];

        private RectTransform _dContent;
        private RectTransform _dHead;
        private Image _dGlyph;
        private TMP_Text _dKind;
        private UiPips _dPips;
        private int _dPipsCount = -1;
        private TMP_Text _dName;
        private TMP_Text _dState;
        private TMP_Text _dDesc;
        private TMP_Text _dNote;
        private RectTransform _dStatRow;   // 12b U11 (AD R1-C SHOULD-4): a tier's before → after row
        private TMP_Text _dStatLabel;
        private TMP_Text _dStatValue;
        private RectTransform _dBuyRow;
        private RectTransform _dActions;
        private TMP_Text _dPrice;
        private UiButton _buy;
        private UiPrompt _buyPrompt;
        private UiButton _pin;
        private UiButton _reforge;

        private float _pitchX = TierPitch;
        private float _pitchY = RowPitch;
        private float _seal = SealSize;
        private float _headSize = SealSize;
        private float _boxH;
        private float _nameFontSize = -1f;
        private float _nameLine;
        private float _stateLine;
        private float _innerWidth;
        private bool _compact;
        private float _nodeWidth = NodeWidth;
        private int _focus = -1;
        private bool _opening;

        /// <summary>Set by the screen from the open offer: every command quotes them (WP-008).</summary>
        public int SessionId { get; set; }
        public int Revision { get; set; }

        /// <summary>The control below the tree (the screen's UNDO / CONTINUE) for pad navigation.</summary>
        public Selectable ExitDown { get; set; }

        /// <summary>The control above the lane tabs (the gold strip's REROLL when it is pressable).</summary>
        public Selectable ExitUp { get; set; }

        /// <summary>A command was refused: the screen shows the sentence in its footer.</summary>
        public Action<string> Refused;

        /// <summary>A command changed the offer: the screen redraws synchronously.</summary>
        public Action Changed;

        /// <summary>The detail card changed its subject or its BUY: the screen hands the Primary role (one per screen).</summary>
        public Action DetailShown;

        /// <summary>BUY is the screen's live Primary (a buyable node is detailed).</summary>
        public bool BuyLive => _buy != null && _buy.gameObject.activeSelf && _buy.IsInteractable();

        /// <summary>The screen's sentence for a refused card purchase.</summary>
        public Func<UpgradePurchaseResult, string> DescribePurchase;

        public RunTreeSnapshot Snapshot => _snap;
        public int FocusIndex => _focus;
        public RunTreeLane Lane => _lane;
        public bool Compact => _compact;
        public float SealPx => _seal;
        public float RowPitchPx => _pitchY;
        public UiButton BuyButton => _buy;
        public UiButton PinButton => _pin;
        public UiButton ReforgeButton => _reforge;
        public TMP_Text DetailName => _dName;
        public TMP_Text DetailState => _dState;
        public TMP_Text DetailDescription => _dDesc;
        /// <summary>Edges drawn across the tiers (tests).</summary>
        public int EdgeCount
        {
            get
            {
                int count = 0;
                for (int t = 0; t < Tiers; t++)
                {
                    count += _edgeTiers[t] != null ? _edgeTiers[t].Count : 0;
                }

                return count;
            }
        }
        public RectTransform DetailRect => _detail;
        public RectTransform TreeRect => _tree;

        /// <summary>The seal button at (row, tier) of the lane on screen, null where the lane has no node.</summary>
        public UiButton NodeButton(int row, int tier)
        {
            NodeView v = _views[row * Tiers + tier];
            return v != null && v.Index >= 0 ? v.Button : null;
        }

        /// <summary>The seal button of a snapshot node when its lane is on screen.</summary>
        public UiButton ButtonOf(int index)
        {
            NodeView v = ViewOf(index);
            return v != null ? v.Button : null;
        }

        public TMP_Text StateTextOf(int index)
        {
            NodeView v = ViewOf(index);
            return v != null ? v.State : null;
        }

        public TMP_Text NameTextOf(int index)
        {
            NodeView v = ViewOf(index);
            return v != null ? v.Name : null;
        }

        /// <summary>
        /// The offer sells this node now: an unbought card names it (a fork pair is one card; a tier node is sold by its
        /// branch card). The snapshot's InOffer stays true after a tier's card is bought, so the screen asks the cards.
        /// </summary>
        public bool Offered(int index)
        {
            return _shop != null && index >= 0 && index < _snap.Count && _snap.Nodes[index].Kind != RunTreeNodeKind.Hearth &&
                   _snap.Nodes[index].Status != RunTreeNodeStatus.MaxTier && !IsOwned(_snap.Nodes[index]) &&
                   CardOf(index, _snap.Nodes[index], out _) >= 0;
        }

        private int IndexOfView(RunTreeNodeView n)
        {
            return _snap.IndexOf(n.NodeId);
        }

        public bool OfferShownOn(int index)
        {
            NodeView v = ViewOf(index);
            return v != null && v.Offer.activeSelf;
        }

        /// <summary>The control the screen focuses: the detailed node's seal.</summary>
        public Selectable FocusTarget()
        {
            NodeView v = ViewOf(_focus);
            return v != null ? v.Button : null;
        }

        // ================================================================== build

        public static UiRunTreePanel Create(RectTransform host)
        {
            RectTransform root = UiFactory.NewRect("RunTree", host);
            UiFactory.Fill(root);
            var panel = root.gameObject.AddComponent<UiRunTreePanel>();
            panel.Build(root);
            return panel;
        }

        private void Build(RectTransform root)
        {
            // Eight columns of tree, four of detail (S09-CONSTRAINTS; 0.657 / 0.672 = the 12-column grid's 8 + gutter).
            _tree = UiFactory.NewRect("Tree", root);
            _tree.anchorMin = new Vector2(0f, 0f);
            _tree.anchorMax = new Vector2(0.657f, 1f);
            _tree.offsetMin = Vector2.zero;
            _tree.offsetMax = Vector2.zero;

            _tabWeapon = UiFactory.Button("Lane.Weapon", _tree, UiStrings.Get("tree.lane.weapon", "WEAPON"), UiButtonVariant.Nav, () => SetLane(RunTreeLane.Weapon, true));
            _tabBody = UiFactory.Button("Lane.Body", _tree, UiStrings.Get("tree.lane.body", "BODY"), UiButtonVariant.Nav, () => SetLane(RunTreeLane.Body, true));
            _tabWeapon.SetSegment(true);
            _tabBody.SetSegment(true);
            PlaceTopLeft((RectTransform)_tabWeapon.transform, 0f, 0f, TabWidth, TabHeight);
            PlaceTopLeft((RectTransform)_tabBody.transform, TabWidth + (int)UiSpace.S, 0f, TabWidth, TabHeight);
            _laneKeys = UiFactory.Label("LaneKeys", _tree, "Q / E", UiType.Caption, UiColor.TextMuted, TextAnchor.MiddleLeft);
            PlaceTopLeft((RectTransform)_laneKeys.transform, TabWidth * 2f + (int)UiSpace.S + (int)UiSpace.L, 0f, 60f, TabHeight);   // clear of the gate's word
            _hearthCount = UiFactory.Label("HearthCount", _tree, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleRight);
            PlaceAnchored((RectTransform)_hearthCount.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(260f, TabHeight));
            _hearthCount.enableWordWrapping = false;

            _grid = UiFactory.NewRect("Grid", _tree);
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = new Vector2(0f, 1f);
            _grid.pivot = new Vector2(0f, 1f);

            for (int t = 0; t < Tiers; t++)
            {
                var edgesGo = new GameObject("Edges" + t, typeof(RectTransform), typeof(CanvasRenderer), typeof(UiTreeEdges), typeof(CanvasGroup));
                var edgesRect = (RectTransform)edgesGo.transform;
                edgesRect.SetParent(_grid, false);
                edgesRect.anchorMin = new Vector2(0f, 1f);
                edgesRect.anchorMax = new Vector2(0f, 1f);
                edgesRect.pivot = new Vector2(0f, 1f);
                edgesRect.anchoredPosition = Vector2.zero;
                _edgeTiers[t] = edgesGo.GetComponent<UiTreeEdges>();
                _edgeTiers[t].raycastTarget = false;
                _edgeGroups[t] = edgesGo.GetComponent<CanvasGroup>();
                _edgeGroups[t].interactable = false;
                _edgeGroups[t].blocksRaycasts = false;
            }
            _gateLabel = UiFactory.Label("GateLabel", _grid, string.Empty, UiType.Caption, UiColor.TextMuted, TextAnchor.LowerLeft);
            _gateLabel.enableWordWrapping = false;
            var gateRect = (RectTransform)_gateLabel.transform;
            gateRect.anchorMin = new Vector2(0f, 1f);
            gateRect.anchorMax = new Vector2(0f, 1f);
            gateRect.pivot = new Vector2(0f, 1f);
            _gateLabel.gameObject.SetActive(false);

            for (int s = 0; s < _views.Length; s++)
            {
                _views[s] = BuildNode(s);
            }

            BuildDetail(root);
        }

        private NodeView BuildNode(int slot)
        {
            var v = new NodeView();
            v.Button = UiFactory.Button("Node." + slot / Tiers + "." + slot % Tiers, _grid, string.Empty, UiButtonVariant.Ghost, () => OnNodeActivated(slot));
            v.Rect = (RectTransform)v.Button.transform;
            v.Button.LabelText.gameObject.SetActive(false);
            v.Button.gameObject.AddComponent<UiTreeNodeInput>().Configure(this, slot);

            v.Seal = UiFactory.NewRect("Seal", v.Rect);
            v.Mark = UiFactory.NewImage("Mark", v.Seal, UiColor.TextPrimary, UiSprites.RingHairline);
            UiFactory.Fill((RectTransform)v.Mark.transform, -6f);
            v.Fill = UiFactory.NewImage("Fill", v.Seal, UiColor.SurfaceInset, UiSprites.Disc);
            UiFactory.Fill((RectTransform)v.Fill.transform);
            v.Ring = UiFactory.NewImage("Ring", v.Seal, UiColor.TextPrimary, UiSprites.Ring);
            UiFactory.Fill((RectTransform)v.Ring.transform);
            v.Glyph = UiFactory.NewImage("Glyph", v.Seal, UiColor.TextPrimary, UiSprites.Disc);
            v.Glyph.preserveAspect = true;
            var glyphRect = (RectTransform)v.Glyph.transform;
            glyphRect.anchorMin = new Vector2(0.2f, 0.2f);
            glyphRect.anchorMax = new Vector2(0.8f, 0.8f);
            glyphRect.offsetMin = Vector2.zero;
            glyphRect.offsetMax = Vector2.zero;
            v.Cross = UiFactory.NewImage("Cross", v.Seal, UiColor.TextMuted, UiSprites.Solid);
            var crossRect = (RectTransform)v.Cross.transform;
            crossRect.anchorMin = new Vector2(-0.1f, 0.5f);
            crossRect.anchorMax = new Vector2(1.1f, 0.5f);
            crossRect.sizeDelta = new Vector2(0f, 3f);
            crossRect.localRotation = Quaternion.Euler(0f, 0f, 35f);

            // Overlays, one corner each (AD R1-C SHOULD-5 / SHOULD-2): OFFER upper-left, the pin upper-right outside the ring,
            // NEW (sakura) lower-left, a tier's owned count ("2/4") lower-right. Compact rows move OFFER and the count into the
            // state word (their words sit right of the seal) and the pin to the upper-left.
            Sprite mark = UiArt.Icon("Seal.Mark");
            Image pin = UiFactory.NewImage("Pin", v.Seal, UiColor.TextPrimary, mark != null ? mark : UiSprites.Disc);
            v.Pin = pin.gameObject;
            v.Count = UiChips.Create("Count", v.Seal, UiChipKind.Neutral, "0/5", UiChipSize.Row);
            Image dot = UiFactory.NewImage("New", v.Seal, UiColor.AccentSakura, UiSprites.Disc);   // the kit's NEW colour (UiChips)
            PlaceAnchored((RectTransform)dot.transform, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(6f, 6f), new Vector2(10f, 10f));
            v.New = dot.gameObject;
            // 12b (AD R1-C SHOULD-1): LOCKED carries the kit's lock in the lower-left corner (NEW's corner - a locked node is
            // never new), so the state has a shape of its own beside the hairline ring and the node keeps its glyph.
            Sprite locked = UiArt.Icon("Shop.Locked");
            v.Lock = UiFactory.NewImage("Lock", v.Seal, UiColor.TextMuted, locked != null ? locked : UiSprites.Disc);
            v.Lock.raycastTarget = false;
            PlaceAnchored(v.Lock.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(4f, 4f), new Vector2(LockSize, LockSize));
            if (locked != null && UiArt.IsGlyph(locked))
            {
                UiArt.ApplyGlyph(v.Lock);
            }

            v.Lock.gameObject.SetActive(false);
            TMP_Text offer = UiChips.Create("Offer", v.Seal, UiChipKind.Value, UiStrings.Get("tree.flag.offer", "OFFER"), UiChipSize.Row);
            v.Offer = offer.transform.parent.gameObject;
            v.Group = v.Button.gameObject.AddComponent<CanvasGroup>();

            v.Name = UiFactory.Label("Name", v.Rect, string.Empty, UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            v.Name.enableWordWrapping = false;
            v.Name.overflowMode = TextOverflowModes.Ellipsis;
            v.State = UiFactory.Label("State", v.Rect, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleCenter);
            v.State.enableWordWrapping = false;
            v.State.overflowMode = TextOverflowModes.Ellipsis;
            return v;
        }

        private void BuildDetail(RectTransform root)
        {
            _detail = UiFactory.NewRect("Detail", root);
            _detail.anchorMin = new Vector2(0.672f, 0f);
            _detail.anchorMax = new Vector2(1f, 1f);
            _detail.offsetMin = Vector2.zero;
            _detail.offsetMax = Vector2.zero;
            UiCard card = UiCard.Create("DetailCard", _detail, Vector2.zero, UiElevation.Elev1, UiColor.SurfacePanel, UiSpace.XL);
            UiFactory.Fill((RectTransform)card.transform);
            _dContent = card.Content;

            _dHead = UiFactory.NewRect("Head", _dContent);
            _dGlyph = UiFactory.NewImage("Glyph", _dHead, UiColor.TextPrimary, UiSprites.Disc);
            _dGlyph.preserveAspect = true;
            _dKind = UiFactory.Label("Kind", _dHead, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            _dKind.enableWordWrapping = false;
            _dKind.overflowMode = TextOverflowModes.Ellipsis;

            _dName = UiFactory.Label("Name", _dContent, string.Empty, UiType.H2, UiColor.TextPrimary);
            _dName.enableWordWrapping = false;
            _dName.overflowMode = TextOverflowModes.Ellipsis;
            _dState = UiFactory.Label("State", _dContent, string.Empty, UiType.Label, UiColor.TextPrimary);
            _dState.enableWordWrapping = false;
            _dState.overflowMode = TextOverflowModes.Ellipsis;
            _dDesc = UiFactory.Label("Description", _dContent, string.Empty, UiType.Body, UiColor.TextSecondary);
            _dDesc.overflowMode = TextOverflowModes.Ellipsis;
            _dNote = UiFactory.Label("Note", _dContent, string.Empty, UiType.Caption, UiColor.TextMuted);
            _dNote.overflowMode = TextOverflowModes.Ellipsis;
            _dStatRow = UiFactory.NewRect("StatRow", _dContent);
            _dStatLabel = UiFactory.Label("StatLabel", _dStatRow, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Fill(_dStatLabel.rectTransform);
            _dStatValue = UiFactory.Label("StatValue", _dStatRow, string.Empty, UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.MiddleRight);
            UiFactory.Fill(_dStatValue.rectTransform);
            _dStatRow.gameObject.SetActive(false);

            _dBuyRow = UiFactory.NewRect("BuyRow", _dContent);
            _dPrice = UiFactory.Numeric("Price", _dBuyRow, string.Empty, UiType.NumericM, UiColor.StateGold, TextAnchor.MiddleLeft, false);
            RectTransform priceRect = UiFactory.Fill((RectTransform)_dPrice.transform);
            priceRect.offsetMax = new Vector2(-212f, 0f);
            _buy = UiFactory.Button("Buy", _dBuyRow, "BUY", UiButtonVariant.Primary, OnBuy);
            _buyPrompt = UiInput.Prompt(_buy, UiPromptAction.Submit, true);
            PlaceAnchored((RectTransform)_buy.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(204f, UiTokens.TargetPreferred));

            _dActions = UiFactory.NewRect("Actions", _dContent);
            _pin = UiFactory.Button("Pin", _dActions, UiStrings.Get("tree.action.pin", "PIN"), UiButtonVariant.Secondary, OnPin);
            PlaceAnchored((RectTransform)_pin.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(150f, UiTokens.TargetPreferred));
            _reforge = UiFactory.Button("Reforge", _dActions, UiStrings.Get("tree.action.reforge", "REFORGE"), UiButtonVariant.Secondary, OnReforge);
            PlaceAnchored((RectTransform)_reforge.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(204f, UiTokens.TargetPreferred));
        }

        // ================================================================== layout

        /// <summary>
        /// Measured from the host rect and the live text metrics, so TextScale and the UI scale grow the rows into the
        /// room the host has instead of past it (the R1-B Selection lesson).
        /// </summary>
        private void Layout()
        {
            if (_nameFontSize < 0f)
            {
                _nameFontSize = _views[0].Name.fontSize;
            }

            float w = _tree.rect.width;
            float h = _tree.rect.height;
            _views[0].Name.fontSize = _nameFontSize;
            float name = LineOf(_views[0].Name);
            float state = LineOf(_views[0].State);
            float gridTop = TabHeight + (int)UiSpace.M;
            _pitchX = Mathf.Min(TierPitch, Mathf.Floor(w / Tiers));

            // PD §5's 112 px row is the 1080p / TextScale 1 value of "a 64 px seal over two lines": the row grows with
            // the text while the host has the room, so a larger TextScale keeps the seal stacked instead of shrinking it.
            float stackNeed = 8f + 4f + SealSize + 4f + name + state + 4f;
            _pitchY = Mathf.Min(Mathf.Floor((h - gridTop) / Rows), Mathf.Max(RowPitch, Mathf.Ceil(stackNeed)));

            // Stacked (the seal over its one-line name) while a 48+ seal still fits over both lines; the compact row
            // puts the seal beside them.
            float stackedSeal = _pitchY - 8f - 4f - name - state - 4f;
            _compact = stackedSeal < 48f;
            if (_compact)
            {
                _pitchX = Mathf.Floor(w / Tiers);   // the words sit beside the seal: give them the host's width
            }

            _seal = _compact
                ? Mathf.Clamp(_pitchY - 12f, MinSeal, CompactSealMax)
                : Mathf.Min(SealSize, stackedSeal);
            _nodeWidth = _compact ? _pitchX - EdgeGap : Mathf.Min(NodeWidth, _pitchX - (int)UiSpace.M);

            _grid.sizeDelta = new Vector2(_pitchX * Tiers, _pitchY * Rows);
            _grid.anchoredPosition = new Vector2(0f, -gridTop);
            for (int t = 0; t < Tiers; t++)
            {
                ((RectTransform)_edgeTiers[t].transform).sizeDelta = _grid.sizeDelta;
            }

            float boxH = _pitchY - (_compact ? 4f : 8f);
            _boxH = boxH;
            if (_compact)
            {
                name = state;   // the compact name takes the state word's size (set below)
            }

            _nameLine = name;
            _stateLine = state;
            for (int s = 0; s < _views.Length; s++)
            {
                NodeView v = _views[s];
                int row = s / Tiers;
                int tier = s % Tiers;
                v.Rect.anchorMin = new Vector2(0f, 1f);
                v.Rect.anchorMax = new Vector2(0f, 1f);
                v.Rect.sizeDelta = new Vector2(_nodeWidth, boxH);
                v.Name.enableWordWrapping = _compact;
                v.State.enableWordWrapping = _compact;
                // Compact rows (AD R1-C MUST-2): the name steps down to the state word's size so two lines of each fit
                // beside the seal; the word keeps its two lines first.
                v.Name.fontSize = _compact ? v.State.fontSize : _nameFontSize;
                if (_compact)
                {
                    v.Rect.pivot = new Vector2(0f, 1f);
                    v.Rect.anchoredPosition = new Vector2(tier * _pitchX, -row * _pitchY);
                    PlaceAnchored(v.Seal, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(_seal, _seal));
                    float textLeft = _seal + (int)UiSpace.S;
                    float top = Mathf.Max(0f, (boxH - name - state) * 0.5f);
                    Band(v.Name, textLeft, top, name, TextAlignmentOptions.MidlineLeft);
                    Band(v.State, textLeft, top + name, state, TextAlignmentOptions.MidlineLeft);
                }
                else
                {
                    v.Rect.pivot = new Vector2(0.5f, 1f);
                    v.Rect.anchoredPosition = new Vector2(tier * _pitchX + _pitchX * 0.5f, -row * _pitchY);
                    PlaceAnchored(v.Seal, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(_seal, _seal));
                    Band(v.Name, 0f, 4f + _seal + 4f, name, TextAlignmentOptions.Midline);
                    Band(v.State, 0f, 4f + _seal + 4f + name, state, TextAlignmentOptions.Midline);
                }
            }

            LayoutDetail();
        }

        private void LayoutDetail()
        {
            float h = _dContent.rect.height;
            float kind = LineOf(_dKind);
            float nameLine = LineOf(_dName);
            float stateLine = LineOf(_dState);
            float body = LineOf(_dDesc);
            float button = UiTokens.TargetPreferred;

            // The foot: BUY over the node actions, anchored to the card's bottom; the description takes what is left.
            float foot = button * 2f + (int)UiSpace.S;
            float head = h - foot >= SealSize + nameLine + stateLine + body * 3f + (int)UiSpace.M * 3f ? SealSize : 48f;
            _headSize = head;

            float y = 0f;
            Dock(_dHead, y, head);
            PlaceAnchored((RectTransform)_dGlyph.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(head, head));
            var kindRect = (RectTransform)_dKind.transform;
            kindRect.anchorMin = new Vector2(0f, 1f);
            kindRect.anchorMax = new Vector2(1f, 1f);
            kindRect.pivot = new Vector2(0f, 1f);
            kindRect.offsetMin = new Vector2(head + (int)UiSpace.L, -(head * 0.5f));
            kindRect.offsetMax = new Vector2(0f, -(head * 0.5f - kind));
            y += head + (int)UiSpace.M;
            Dock((RectTransform)_dName.transform, y, nameLine);
            y += nameLine + (int)UiSpace.XS;
            Dock((RectTransform)_dState.transform, y, stateLine);
            y += stateLine + (int)UiSpace.M;
            // The rule note (two Caption lines) sits over the foot; the description takes the band between.
            float note = TwoLines(_dNote);
            float noteBottom = foot + (int)UiSpace.L;
            DockBottom((RectTransform)_dNote.transform, noteBottom, note);
            // 12b U11: the before → after row over the rule note (the band PD §5 reserves); the description keeps the rest.
            float stat = LineOf(_dStatValue);
            float statBottom = noteBottom + note + (int)UiSpace.S;
            DockBottom(_dStatRow, statBottom, stat);
            float descH = Mathf.Max(body, h - statBottom - stat - (int)UiSpace.S - y);
            Dock((RectTransform)_dDesc.transform, y, descH);

            DockBottom(_dActions, 0f, button);
            DockBottom(_dBuyRow, button + (int)UiSpace.S, button);
            _innerWidth = _dContent.rect.width;
            if (_dPips != null)
            {
                ((RectTransform)_dPips.transform).anchoredPosition = new Vector2(head + (int)UiSpace.L, -(int)UiSpace.XS);
            }
        }

        // ================================================================== refresh

        /// <summary>The screen opened: the next Refresh stamps the tree in tier by tier (Opening Beat, ≤ 600 ms).</summary>
        public void BeginOpening()
        {
            _opening = true;
            _focus = -1;
        }

        /// <summary>Redraw from the shop (on open and on OfferChanged); never per frame.</summary>
        public void Refresh(RoundUpgradeShop shop)
        {
            _shop = shop;
            _snap.Refresh(shop);
            Layout();
            _tabWeapon.SetSelectedChoice(_lane == RunTreeLane.Weapon);
            _tabBody.SetSelectedChoice(_lane == RunTreeLane.Body);
            RefreshHearthCount();

            for (int i = 0; i < _cell.Length; i++)
            {
                _cell[i] = -1;
            }

            for (int i = 0; i < _snap.Count; i++)
            {
                RunTreeNodeView n = _snap.Nodes[i];
                if (n.Lane == _lane && n.Row >= 0 && n.Row < Rows && n.Column >= 0 && n.Column < Tiers)
                {
                    _cell[n.Row * Tiers + n.Column] = i;
                }
            }

            Array.Clear(_onPath, 0, _onPath.Length);
            for (int k = 0; k < _snap.PinPathCount; k++)
            {
                int p = _snap.PinPath[k];
                if (p >= 0 && p < _onPath.Length)
                {
                    _onPath[p] = true;
                }
            }

            if (_focus < 0 || _focus >= _snap.Count || _snap.Nodes[_focus].Lane != _lane)
            {
                _focus = FirstUseful();
            }

            for (int s = 0; s < _views.Length; s++)
            {
                ApplyNode(_views[s], _cell[s]);
            }

            DrawEdges();
            ShowDetail(_focus);
            LinkNavigation();

            // A redraw reuses the seal buttons (a lane switch, a purchase): a focus left on a seal that now shows another
            // node moves to the detailed one, so the ring and the card never name two nodes (AD R1-C MUST-4).
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            for (int s = 0; s < _views.Length && selected != null; s++)
            {
                if (_views[s].Button.gameObject == selected && _views[s].Index != _focus)
                {
                    Selectable target = FocusTarget();
                    if (target != null)
                    {
                        UiInput.Focus(target);
                    }

                    break;
                }
            }

            if (_opening)
            {
                _opening = false;
                for (int s = 0; s < _views.Length; s++)
                {
                    if (_views[s].Index >= 0)
                    {
                        // PD §5.4 Opening Beat: each tier inks in 120 ms after the one before (≤ 600 ms), the seal stamping.
                        float delay = (s % Tiers) * OpeningBeatSeconds;
                        _views[s].Group.alpha = UiMotion.ReducedMotion ? 1f : 0f;
                        UiMotion.Alpha(_views[s].Group, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, delay);
                        UiPresets.StampIn(_views[s].Seal, delay);
                    }
                }

                for (int t = 0; t < Tiers; t++)
                {
                    // The joins into tier t ink in with tier t's seals (12b, PD §5.4).
                    _edgeGroups[t].alpha = UiMotion.ReducedMotion ? 1f : 0f;
                    UiMotion.Alpha(_edgeGroups[t], 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, t * OpeningBeatSeconds);
                }
            }
        }

        private void RefreshHearthCount()
        {
            int hearth = _snap.HearthIndex;
            bool show = hearth >= 0 && hearth < _snap.Count;
            _hearthCount.gameObject.SetActive(show);
            if (show)
            {
                _hearthCount.text = UiStrings.Get("tree.hearth.count", "HEARTH {value} / {max}")
                    .Replace("{value}", _snap.HearthStacks.ToString(CultureInfo.InvariantCulture))
                    .Replace("{max}", _snap.Nodes[hearth].MaxTier.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void ApplyNode(NodeView v, int index)
        {
            v.Index = index;
            v.Button.gameObject.SetActive(index >= 0);
            if (index < 0)
            {
                return;
            }

            RunTreeNodeView n = _snap.Nodes[index];
            bool owned = IsOwned(n) || (n.Kind == RunTreeNodeKind.Tier && n.OwnedTier > 0);
            bool dim = n.Status == RunTreeNodeStatus.Locked || n.Status == RunTreeNodeStatus.Excluded;
            bool waiting = n.Status == RunTreeNodeStatus.PrereqMissing;
            bool forSale = Offered(index);
            SetGlyph(v.Glyph, n, waiting && !owned);

            // PD §5.1: shape + glyph + word, never colour alone. Owned = a cream seal with an ink glyph; open = an
            // inset seal ringed in cream; waiting = a muted ring; locked / closed = no fill, dim, the closed one struck.
            v.Fill.enabled = owned || n.Status == RunTreeNodeStatus.Available || n.Status == RunTreeNodeStatus.Unaffordable;
            v.Fill.color = UiTokens.Get(owned ? UiColor.TextPrimary : UiColor.SurfaceInset);
            v.Glyph.color = WithAlpha(UiTokens.Get(owned ? UiColor.SurfaceBase : UiColor.TextPrimary), dim ? 0.35f : waiting ? 0.6f : 1f);
            // The heavy ring is the offer's alone (gold); every other seal wears the thin one, the capstone / seal the ensō.
            // 12b (AD R1-C SHOULD-1): the three unbought states read by shape - AVAILABLE the solid ring, NEEDS a dotted
            // hairline (and a hollow glyph), LOCKED the plain hairline (and the lock).
            bool special = n.Kind == RunTreeNodeKind.Capstone || n.Kind == RunTreeNodeKind.WeaponSeal;
            v.Ring.sprite = forSale ? UiSprites.Ring
                : waiting && !owned ? UiSprites.RingDotted
                : n.Status == RunTreeNodeStatus.Locked ? UiSprites.RingHairline
                : special ? UiSprites.RingEnso : UiSprites.RingThin;
            v.Lock.gameObject.SetActive(n.Status == RunTreeNodeStatus.Locked);
            // MAX wears the ginpaku ring - the silver-leaf colour (12b, AD R1-C MUST-5 residual). No Leaf glint: the Opening
            // Beat's seal stamp is the shop's one ceremony kind (§14 MUST-4, UiOrnamentCensusTests).
            bool max = n.Kind == RunTreeNodeKind.Tier && n.MaxTier > 0 && n.OwnedTier >= n.MaxTier;
            UiColor ring = forSale ? UiColor.StateGold : max ? UiColor.SilverLeaf : owned ? UiColor.TextSecondary : dim || waiting ? UiColor.TextMuted : UiColor.TextPrimary;
            v.Ring.color = WithAlpha(UiTokens.Get(ring), dim ? 0.6f : 1f);
            v.Cross.gameObject.SetActive(n.Status == RunTreeNodeStatus.Excluded);
            v.New.SetActive(n.NewlyUnlocked);
            v.Pin.SetActive(n.IsPinned);
            float pinSize = Mathf.Round(20f * Mathf.Max(1f, UiSettings.TextScale * 0.96f));
            PlaceAnchored((RectTransform)v.Pin.transform, _compact ? new Vector2(0f, 1f) : new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                _compact ? new Vector2(-2f, 2f) : new Vector2(2f, 2f), new Vector2(pinSize, pinSize));
            v.Offer.SetActive(forSale && !_compact);
            var offerRect = (RectTransform)v.Offer.transform;
            PlaceAnchored(offerRect, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(6f, -2f), offerRect.sizeDelta);
            bool count = n.Kind == RunTreeNodeKind.Tier && n.OwnedTier > 0 && n.MaxTier > 1;
            var countRect = (RectTransform)v.Count.transform.parent;
            countRect.gameObject.SetActive(count && !_compact);
            PlaceAnchored(countRect, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-4f, 4f), countRect.sizeDelta);
            if (count)
            {
                v.Count.text = n.OwnedTier + "/" + n.MaxTier;
            }

            v.Mark.enabled = index == _focus;
            v.Name.color = UiTokens.Get(dim ? UiColor.TextMuted : UiColor.TextPrimary);

            // PD §3.5: the pin path's steps are numbered before the label (1, 2 … the pinned node last).
            int step = PathStep(index);
            string prefix = step > 0 ? step.ToString(CultureInfo.InvariantCulture) + " · " : string.Empty;
            v.Name.text = _compact ? prefix + FullName(n) : Fit(v.Name, prefix, n, true);

            _measureState = v.State;
            string word = StateWord(n);
            if (_compact)
            {
                // Compact rows carry OFFER and the owned count in the word (no room for chips beside the words).
                string counted = count ? n.OwnedTier + "/" + n.MaxTier + " · " : string.Empty;
                word = counted + word;
                if (!forSale && n.Status == RunTreeNodeStatus.Available && v.State.GetPreferredValues(word).x > v.State.rectTransform.rect.width)
                {
                    // AD 12b MUST-3: a word that would wrap yields its state word ("2/4 · 120") - the price never sits alone on a
                    // line (the solid ring already says AVAILABLE).
                    word = counted + n.Cost.ToString(CultureInfo.InvariantCulture);
                }
                if (forSale)
                {
                    // An offered node that can be bought reads "OFFER · 15" (or "OFFER · 2/4 · 15"): the price never sits
                    // alone on a line below "OFFER · AVAILABLE ·" (12b, AD R1-C SHOULD-8 fallback; the static atlas has no
                    // no-break space).
                    word = UiStrings.Get("tree.flag.offer", "OFFER") + " · "
                        + (n.Status == RunTreeNodeStatus.Available ? counted + n.Cost.ToString(CultureInfo.InvariantCulture) : word);
                }
            }

            v.State.text = word;
            _measureState = null;
            if (_compact)
            {
                FitCompact(v, n, prefix);
            }

            v.State.color = UiTokens.Get(n.Status == RunTreeNodeStatus.Unaffordable ? UiColor.StateWarning
                : _compact && forSale ? UiColor.StateGold : dim ? UiColor.TextMuted : UiColor.TextSecondary);
        }

        private static void SetGlyph(Image image, RunTreeNodeView n, bool hollow)
        {
            Sprite glyph = GlyphOf(n);
            UiArt.SetGlyph(image, glyph != null ? glyph : UiSprites.Disc);
            if (UiArt.IsGlyph(image.sprite))
            {
                // NEEDS draws the glyph outline-only (DD/UI/Fx SdfGlyph, a negative width = a hollow band; 12b AD R1-C SHOULD-1).
                UiArt.ApplyGlyph(image, new Vector3(hollow ? -HollowGlyphBand : 0.25f, 0f, 0f));
            }
        }

        /// <summary>
        /// The snapshot's icon key, then the nearest glyph G1 ships: the upgrade's stand-in, the weapon's roster
        /// glyph, the Hud set for the Hearth (the seal), the Purse (gold), the Special link and the body lane (health).
        /// </summary>
        public static Sprite GlyphOf(RunTreeNodeView n)
        {
            Sprite own = UiArt.Icon(n.IconKey);
            if (own != null)
            {
                return own;
            }

            if (n.HasBranch)
            {
                Sprite upgrade = UiArt.UpgradeIcon(n.Branch);
                if (upgrade != null)
                {
                    return upgrade;
                }
            }

            switch (n.Kind)
            {
                case RunTreeNodeKind.Hearth: return UiArt.Icon("Seal.Mark");
                case RunTreeNodeKind.Purse: return UiArt.Icon("Hud.Gold");
                case RunTreeNodeKind.SpecialLink: return UiArt.Icon("Hud.Special");
            }

            if (!string.IsNullOrEmpty(n.IconKey) && n.IconKey.StartsWith("Weapon.", StringComparison.Ordinal))
            {
                Sprite roster = UiArt.RosterGlyph(n.IconKey.Substring("Weapon.".Length));
                if (roster != null)
                {
                    return roster;
                }
            }

            return UiArt.Icon(n.Lane == RunTreeLane.Body ? "Hud.Health" : "Weapon.AssaultRifle");
        }

        private string StateWord(RunTreeNodeView n)
        {
            string word;
            switch (n.Status)
            {
                case RunTreeNodeStatus.Locked:
                    word = UiStrings.Get("tree.state.locked", "LOCKED");
                    break;
                case RunTreeNodeStatus.PrereqMissing:
                    // Forks, the weapon seal and the Hearth open on ANY keystone of the lane (WP-077 §2.2): the word says
                    // so rather than naming the cheapest one (AD R1-C MUST-3b).
                    word = n.MissingParentIndex >= 0 && !Gated(n)
                        ? Needs("tree.state.prereq_missing", "NEEDS {node}", n.MissingParentIndex)
                        : UiStrings.Get("tree.state.prereq_keystone", "NEEDS A KEYSTONE");
                    break;
                case RunTreeNodeStatus.Available:
                    word = UiStrings.Get("tree.state.available", "AVAILABLE") + " · " + n.Cost.ToString(CultureInfo.InvariantCulture);
                    break;
                case RunTreeNodeStatus.Unaffordable:
                    word = UiStrings.Get("tree.state.unaffordable", "{value} GOLD SHORT").Replace("{value}", n.Shortfall.ToString(CultureInfo.InvariantCulture));
                    break;
                case RunTreeNodeStatus.Purchased:
                    word = UiStrings.Get("tree.state.purchased", "OWNED") + (n.MaxTier > 1 ? " · " + n.OwnedTier + "/" + n.MaxTier : string.Empty);
                    break;
                case RunTreeNodeStatus.Excluded:
                    word = Needs("tree.state.excluded", "CLOSED BY {node}", n.ClosedByIndex);
                    break;
                case RunTreeNodeStatus.MaxTier:
                    word = UiStrings.Get("tree.state.max_tier", "MAX");
                    break;
                default:
                    word = string.Empty;
                    break;
            }

            return n.NewlyUnlocked ? UiStrings.Get("tree.flag.new", "NEW") + " · " + word : word;
        }

        /// <summary>
        /// The node's full name where the measured line holds it, else its short name (S-04's ShortName clips a tier's name
        /// at 12 characters: "Movement Spe", and both regeneration branches read "Regeneration").
        /// </summary>
        private static string Fit(TMP_Text line, string template, RunTreeNodeView n, bool prefix = false)
        {
            string full = string.IsNullOrEmpty(n.DisplayName) ? n.ShortName : n.DisplayName;
            if (prefix)
            {
                string whole = template + full;
                if (string.IsNullOrEmpty(n.ShortName) || line == null || line.rectTransform.rect.width <= 0f ||
                    line.GetPreferredValues(whole).x <= line.rectTransform.rect.width)
                {
                    return whole;
                }

                return template + n.ShortName;
            }

            // A tier's ShortName is its name cut at 12 characters ("Regeneration" for both regeneration branches): a word that
            // names a tier keeps the full name and takes another line instead (AD R1-C MUST-2 re-check).
            if (string.IsNullOrEmpty(n.ShortName) || line == null || n.Kind == RunTreeNodeKind.Tier)
            {
                return template.Length > 0 ? template.Replace("{node}", full.ToUpperInvariant()) : full;
            }

            string candidate = template.Length > 0 ? template.Replace("{node}", full.ToUpperInvariant()) : full;
            float width = line.rectTransform.rect.width;
            bool fits = line.enableWordWrapping
                ? line.GetPreferredValues(candidate, width, 0f).y <= TwoLines(line)   // compact: two lines beside the seal
                : line.GetPreferredValues(candidate).x <= width;
            if (width <= 0f || fits)
            {
                return candidate;
            }

            return template.Length > 0 ? template.Replace("{node}", n.ShortName.ToUpperInvariant()) : n.ShortName;
        }

        private TMP_Text _measureState;

        private static string FullName(RunTreeNodeView n)
        {
            return string.IsNullOrEmpty(n.DisplayName) ? n.ShortName : n.DisplayName;
        }

        /// <summary>
        /// Compact rows (the Mac aspect) have the height the words beside a 48 px seal lack in width: the name and the state
        /// word take up to two lines each, the pair centred on the seal, so full names ("Regeneration Delay") always print.
        /// </summary>
        private void FitCompact(NodeView v, RunTreeNodeView n, string prefix)
        {
            float width = v.Name.rectTransform.rect.width;
            float name = Mathf.Min(TwoLines(v.Name), Mathf.Ceil(v.Name.GetPreferredValues(v.Name.text, width, 0f).y));
            float state = Mathf.Min(TwoLines(v.State), Mathf.Ceil(v.State.GetPreferredValues(v.State.text, width, 0f).y));
            if (name + state > _boxH && n.Kind != RunTreeNodeKind.Tier && !string.IsNullOrEmpty(n.ShortName))
            {
                // The word keeps its two lines; the name yields first - to the designer's short name ("Full Mag"), never a
                // clipped one (a tier's ShortName is its name cut at 12 characters, so tiers keep their full name).
                v.Name.text = prefix + n.ShortName;
                name = Mathf.Min(TwoLines(v.Name), Mathf.Ceil(v.Name.GetPreferredValues(v.Name.text, width, 0f).y));
            }

            if (name + state > _boxH)
            {
                name = Mathf.Max(_nameLine, _boxH - state);
            }

            // A long NEEDS word ("NEEDS REGENERATION DELAY") takes a third line while the name holds one.
            float whole = Mathf.Ceil(v.State.GetPreferredValues(v.State.text, width, 0f).y);
            if (whole > state && name <= _nameLine + 1f && _nameLine + whole <= _boxH)
            {
                state = whole;
            }

            name = Mathf.Max(name, _nameLine);
            state = Mathf.Max(state, _stateLine);
            float left = _seal + (int)UiSpace.S;
            float top = Mathf.Max(0f, (_boxH - name - state) * 0.5f);
            Band(v.Name, left, top, name, TextAlignmentOptions.MidlineLeft);
            Band(v.State, left, top + name, state, TextAlignmentOptions.MidlineLeft);
        }

        private static float TwoLines(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag\nAg").y);
        }

        /// <summary>NEEDS / CLOSED BY with the partner's name fitted to the node's state line.</summary>
        private string Needs(string key, string fallback, int partner)
        {
            if (partner < 0 || partner >= _snap.Count)
            {
                return UiStrings.Get(key, fallback).Replace("{node}", "?");
            }

            return Fit(_measureState, UiStrings.Get(key, fallback), _snap.Nodes[partner]);
        }

        private void DrawEdges()
        {
            for (int t = 0; t < Tiers; t++)
            {
                _edgeTiers[t].Clear();
            }

            Color hair = WithAlpha(UiTokens.Get(UiColor.TextMuted), 0.7f);
            Color owned = UiTokens.Get(UiColor.TextSecondary);
            Color pinned = UiTokens.Get(UiColor.TextPrimary);
            for (int s = 0; s < _cell.Length; s++)
            {
                int child = _cell[s];
                if (child < 0 || _snap.Nodes[child].ParentIndices == null)
                {
                    continue;
                }

                RunTreeNodeView c = _snap.Nodes[child];
                for (int k = 0; k < c.ParentIndices.Length; k++)
                {
                    int parent = c.ParentIndices[k];
                    if (parent < 0 || parent >= _snap.Count || _snap.Nodes[parent].Lane != _lane)
                    {
                        continue;
                    }

                    RunTreeNodeView p = _snap.Nodes[parent];
                    Link(p, c, _onPath[child] && (_onPath[parent] || HasOwned(p)), hair, owned, pinned);
                }
            }

            // The rules the parents do not carry (RunTreeState.PrereqMet): the capstone waits on the lane's weapon seal, and
            // forks, the seal and the Hearth open on ANY keystone of the lane - a gate line in the gutter before them,
            // dashed while it is shut, solid once a keystone is owned.
            int seal = -1;
            int gateTier = Tiers;
            bool keystoneOwned = false;
            for (int s = 0; s < _cell.Length; s++)
            {
                int i = _cell[s];
                if (i < 0)
                {
                    continue;
                }

                RunTreeNodeView n = _snap.Nodes[i];
                if (n.Kind == RunTreeNodeKind.WeaponSeal)
                {
                    seal = i;
                }

                if (n.Kind == RunTreeNodeKind.BranchKeystone && IsOwned(n))
                {
                    keystoneOwned = true;
                }

                if (n.Kind == RunTreeNodeKind.Fork || n.Kind == RunTreeNodeKind.WeaponSeal || n.Kind == RunTreeNodeKind.Hearth)
                {
                    gateTier = Mathf.Min(gateTier, n.Column);
                }
            }

            for (int s = 0; s < _cell.Length && seal >= 0; s++)
            {
                int cap = _cell[s];
                if (cap < 0 || _snap.Nodes[cap].Kind != RunTreeNodeKind.Capstone)
                {
                    continue;
                }

                RunTreeNodeView p = _snap.Nodes[seal];
                RunTreeNodeView c = _snap.Nodes[cap];
                Link(p, c, _onPath[cap] && (_onPath[seal] || HasOwned(p)), hair, owned, pinned);
            }

            _gateLabel.gameObject.SetActive(false);
            _gateX = -1f;
            if (gateTier > 0 && gateTier < Tiers)
            {
                // The gate runs only beside the rows it guards (Purse, a tier-less row, is not behind it), with its word
                // at its head: ANY KEYSTONE while shut, KEYSTONE OWNED once open (AD R1-C MUST-3b).
                float x = _compact ? gateTier * _pitchX - EdgeGap * 0.5f : gateTier * _pitchX;
                _gateX = x;
                int firstRow = Rows;
                for (int s = 0; s < _cell.Length; s++)
                {
                    int i = _cell[s];
                    if (i < 0 || !Gated(_snap.Nodes[i]) || _snap.Nodes[i].Column < gateTier)
                    {
                        continue;
                    }

                    int row = _snap.Nodes[i].Row;
                    firstRow = Mathf.Min(firstRow, row);
                    EdgesInto(gateTier).Add(new Vector2(x, -(row * _pitchY + 2f)), new Vector2(x, -(row * _pitchY + _pitchY - 10f)), keystoneOwned ? 2f : 1f,
                        !keystoneOwned, keystoneOwned ? owned : hair);
                }

                if (firstRow < Rows)
                {
                    _gateLabel.gameObject.SetActive(true);
                    _gateLabel.text = keystoneOwned ? UiStrings.Get("tree.gate.open", "KEYSTONE OWNED") : UiStrings.Get("tree.gate.shut", "ANY KEYSTONE");
                    _gateLabel.color = UiTokens.Get(keystoneOwned ? UiColor.TextSecondary : UiColor.TextMuted);
                    var label = (RectTransform)_gateLabel.transform;
                    float line = LineOf(_gateLabel);
                    label.sizeDelta = new Vector2(_pitchX * 1.5f, line);
                    label.anchoredPosition = new Vector2(x + 6f, -(firstRow * _pitchY) + line + 2f);
                }
            }

            for (int t = 0; t < Tiers; t++)
            {
                _edgeTiers[t].Commit();
            }
        }

        /// <summary>The edge Graphic of the tier an edge leads into.</summary>
        private UiTreeEdges EdgesInto(int tier)
        {
            return _edgeTiers[Mathf.Clamp(tier, 0, Tiers - 1)];
        }

        private float _gateX = -1f;
        private TMP_Text _gateLabel;

        public TMP_Text GateLabel => _gateLabel;

        /// <summary>
        /// One edge's look (AD R1-C MUST-3a): dashed exactly while the child still NEEDS its prerequisite, 2 px once it is
        /// met (the child opened and the parent carries a purchase), a hairline otherwise, and 4 px cream on the pin path.
        /// </summary>
        private void Link(RunTreeNodeView p, RunTreeNodeView c, bool path, Color hair, Color owned, Color pinned)
        {
            bool missing = c.Status == RunTreeNodeStatus.PrereqMissing;
            bool met = !missing && c.Status != RunTreeNodeStatus.Locked && HasOwned(p);
            AddEdge(p, c, path ? 4f : met ? 2f : 1f, missing && !path, path ? pinned : met ? owned : hair);
        }

        private static bool HasOwned(RunTreeNodeView n)
        {
            return IsOwned(n) || (n.Kind == RunTreeNodeKind.Tier && n.OwnedTier > 0);
        }

        /// <summary>The keystone gate's x in the grid (-1 when the lane draws none) - tests.</summary>
        public float GateX => _gateX;

        /// <summary>
        /// Stacked rows join seal to seal in a straight line. Compact rows put words beside the seal, so a join across rows
        /// runs as an elbow through the gutter before the child's tier instead of cutting over the words.
        /// </summary>
        private void AddEdge(RunTreeNodeView p, RunTreeNodeView c, float width, bool dashed, Color colour)
        {
            Vector2 a = EdgeOut(p.Row, p.Column);
            Vector2 b = EdgeIn(c.Row, c.Column);
            UiTreeEdges edges = EdgesInto(c.Column);
            if (!_compact || p.Row == c.Row)
            {
                edges.Add(a, b, width, dashed, colour);
                return;
            }

            float gutter = c.Column * _pitchX - EdgeGap * 0.5f;
            var g1 = new Vector2(gutter, a.y);
            var g2 = new Vector2(gutter, b.y);
            edges.Add(a, g1, width, dashed, colour);
            edges.Add(g1 + new Vector2(0f, a.y > b.y ? width * 0.5f : -width * 0.5f), g2 + new Vector2(0f, a.y > b.y ? -width * 0.5f : width * 0.5f), width, dashed, colour);
            edges.Add(g2, b, width, dashed, colour);
        }

        private Vector2 SealCentre(int row, int tier)
        {
            return _compact
                ? new Vector2(tier * _pitchX + _seal * 0.5f, -(row * _pitchY + _boxH * 0.5f))
                : new Vector2(tier * _pitchX + _pitchX * 0.5f, -(row * _pitchY + 4f + _seal * 0.5f));
        }

        private Vector2 EdgeOut(int row, int tier)
        {
            Vector2 c = SealCentre(row, tier);
            return _compact ? new Vector2(tier * _pitchX + _nodeWidth + 2f, c.y) : c + new Vector2(_seal * 0.5f + 6f, 0f);
        }

        private Vector2 EdgeIn(int row, int tier)
        {
            return SealCentre(row, tier) - new Vector2(_seal * 0.5f + 6f, 0f);
        }

        private static bool Gated(RunTreeNodeView n)
        {
            return n.Kind == RunTreeNodeKind.Fork || n.Kind == RunTreeNodeKind.WeaponSeal || n.Kind == RunTreeNodeKind.Hearth;
        }

        /// <summary>1-based step of a node on the pin path (0 when it is not on it).</summary>
        private int PathStep(int index)
        {
            for (int k = 0; k < _snap.PinPathCount; k++)
            {
                if (_snap.PinPath[k] == index)
                {
                    return k + 1;
                }
            }

            return 0;
        }

        private static bool IsOwned(RunTreeNodeView n)
        {
            return n.Status == RunTreeNodeStatus.Purchased || n.Status == RunTreeNodeStatus.MaxTier;
        }

        private int FirstUseful()
        {
            int first = -1;
            int pinned = -1;
            for (int s = 0; s < _cell.Length; s++)
            {
                int i = _cell[s];
                if (i < 0)
                {
                    continue;
                }

                RunTreeNodeView n = _snap.Nodes[i];
                if (Offered(i) && n.Status == RunTreeNodeStatus.Available)
                {
                    return i;
                }

                if (n.IsPinned && pinned < 0)
                {
                    pinned = i;
                }

                if (first < 0)
                {
                    first = i;
                }
            }

            return pinned >= 0 ? pinned : first;
        }

        // ================================================================== navigation

        /// <summary>
        /// Edge-first pad map (PD §5.5): Right follows an edge to a child, Left back to a parent, Up / Down stay in the
        /// tier. Off the grid: Up to the lane tab (and on to REROLL), Right to BUY, Down to the screen's UNDO / CONTINUE.
        /// </summary>
        private void LinkNavigation()
        {
            Selectable detail = _buy.gameObject.activeSelf && _buy.IsInteractable() ? _buy : _pin;
            for (int s = 0; s < _views.Length; s++)
            {
                NodeView v = _views[s];
                if (v.Index < 0)
                {
                    continue;
                }

                int row = s / Tiers;
                int tier = s % Tiers;
                Selectable right = ChildOf(v.Index) ?? Nearest(row, tier + 1, +1) ?? detail;
                Selectable left = ParentOf(v.Index) ?? Nearest(row, tier - 1, -1);
                Selectable up = InTier(tier, row - 1, -1) ?? (_lane == RunTreeLane.Weapon ? _tabWeapon : _tabBody);
                Selectable down = InTier(tier, row + 1, +1) ?? ExitDown;
                Explicit(v.Button, up, down, left, right);
            }

            Selectable first = FirstButton();
            Explicit(_tabWeapon, ExitUp, first, null, _tabBody);
            Explicit(_tabBody, ExitUp, first, _tabWeapon, null);
            Selectable node = FocusTarget() ?? first;
            Selectable buy = _buy.IsInteractable() ? _buy : null;
            Explicit(_buy, null, _pin, node, null);
            Explicit(_pin, buy, ExitDown, node, _reforge.gameObject.activeSelf ? _reforge : null);
            Explicit(_reforge, buy, ExitDown, _pin, null);
        }

        private Selectable ChildOf(int index)
        {
            int best = -1;
            int bestRow = int.MaxValue;
            int row = _snap.Nodes[index].Row;
            for (int s = 0; s < _cell.Length; s++)
            {
                int c = _cell[s];
                if (c < 0 || _snap.Nodes[c].ParentIndices == null || Array.IndexOf(_snap.Nodes[c].ParentIndices, index) < 0)
                {
                    continue;
                }

                int d = Math.Abs(_snap.Nodes[c].Row - row);
                if (d < bestRow)
                {
                    bestRow = d;
                    best = c;
                }
            }

            return best >= 0 ? ButtonOf(best) : null;
        }

        private Selectable ParentOf(int index)
        {
            int[] parents = _snap.Nodes[index].ParentIndices;
            if (parents == null)
            {
                return null;
            }

            for (int k = 0; k < parents.Length; k++)
            {
                UiButton b = parents[k] >= 0 && parents[k] < _snap.Count ? ButtonOf(parents[k]) : null;
                if (b != null)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>The nearest node by row in a tier, walking further tiers in <paramref name="step"/>'s direction.</summary>
        private Selectable Nearest(int row, int tier, int step)
        {
            for (int t = tier; t >= 0 && t < Tiers; t += step)
            {
                for (int d = 0; d < Rows; d++)
                {
                    Selectable a = At(row - d, t) ?? At(row + d, t);
                    if (a != null)
                    {
                        return a;
                    }
                }
            }

            return null;
        }

        private Selectable InTier(int tier, int row, int step)
        {
            for (int r = row; r >= 0 && r < Rows; r += step)
            {
                Selectable a = At(r, tier);
                if (a != null)
                {
                    return a;
                }
            }

            return null;
        }

        private Selectable At(int row, int tier)
        {
            return row >= 0 && row < Rows && tier >= 0 && tier < Tiers ? NodeButton(row, tier) : null;
        }

        private Selectable FirstButton()
        {
            for (int t = 0; t < Tiers; t++)
            {
                Selectable a = InTier(t, 0, +1);
                if (a != null)
                {
                    return a;
                }
            }

            return null;
        }

        private static void Explicit(Selectable control, Selectable up, Selectable down, Selectable left, Selectable right)
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

        // ================================================================== detail

        private void ShowDetail(int index)
        {
            bool has = index >= 0 && index < _snap.Count;
            _buy.gameObject.SetActive(has);
            _pin.gameObject.SetActive(has);
            _dGlyph.enabled = has;
            for (int s = 0; s < _views.Length; s++)
            {
                _views[s].Mark.enabled = has && _views[s].Index >= 0 && _views[s].Index == index;
            }

            if (!has)
            {
                _dKind.text = string.Empty;
                _dName.text = string.Empty;
                _dDesc.text = string.Empty;
                _dNote.text = string.Empty;
                _dState.text = string.Empty;
                _dPrice.text = string.Empty;
                _reforge.gameObject.SetActive(false);
                SetPips(0, 0);
                DetailShown?.Invoke();
                return;
            }

            RunTreeNodeView n = _snap.Nodes[index];
            SetGlyph(_dGlyph, n, false);
            _dGlyph.color = UiTokens.Get(UiColor.TextPrimary);
            bool hearth = n.Kind == RunTreeNodeKind.Hearth;
            _dKind.text = UiStrings.Get("tree.kind." + n.Kind, n.Kind.ToString().ToUpperInvariant()) + "  ·  " +
                          UiStrings.Get(n.Lane == RunTreeLane.Weapon ? "tree.lane.weapon" : "tree.lane.body", n.Lane.ToString().ToUpperInvariant());
            SetPips(!hearth && n.MaxTier > 1 && n.MaxTier <= 6 ? n.MaxTier : 0, n.OwnedTier);
            if (_dPips != null)
            {
                _dPips.SetGhost(n.Kind == RunTreeNodeKind.Tier && n.OwnedTier < n.MaxTier ? n.OwnedTier : -1);   // the tier a buy adds
            }

            _dName.text = n.DisplayName;
            _dState.text = StateSentence(n);
            _dState.color = UiTokens.Get(n.Status == RunTreeNodeStatus.Unaffordable ? UiColor.StateWarning
                : Offered(index) ? UiColor.StateGold : UiColor.TextPrimary);
            _dDesc.text = DescriptionOf(n);
            _dNote.text = NoteOf(n);
            ShowStat(n);

            bool buyable = n.Status == RunTreeNodeStatus.Available && (hearth || (Offered(index) && !_shop.IsLocked));
            bool priced = n.Status != RunTreeNodeStatus.MaxTier && n.Status != RunTreeNodeStatus.Excluded && !(IsOwned(n) && n.MaxTier <= 1);
            _dPrice.text = priced ? n.Cost.ToString(CultureInfo.InvariantCulture) + " GOLD" : string.Empty;
            _buy.SetText(hearth ? UiStrings.Get("tree.action.stoke", "STOKE THE HEARTH") : "BUY");
            FitBuy();
            _buy.SetInteractable(buyable);
            if (_buyPrompt != null)
            {
                _buyPrompt.gameObject.SetActive(buyable);   // the key chip follows the Primary (AD R1-C MUST-1)
            }
            _pin.SetText(n.IsPinned ? UiStrings.Get("tree.action.unpin", "UNPIN") : UiStrings.Get("tree.action.pin", "PIN"));
            _pin.SetInteractable(n.IsPinned || (!IsOwned(n) && n.Status != RunTreeNodeStatus.Excluded && !hearth));
            bool reforge = n.Kind == RunTreeNodeKind.Fork && IsOwned(n) && n.ReforgeCost >= 0;
            _reforge.gameObject.SetActive(reforge);
            _reforge.SetInteractable(reforge && _snap.Gold >= n.ReforgeCost);
            if (reforge)
            {
                _reforge.SetText(UiStrings.Get("tree.action.reforge", "REFORGE") + " · " + n.ReforgeCost.ToString(CultureInfo.InvariantCulture));
            }

            DetailShown?.Invoke();
        }

        /// <summary>BUY holds its word and its prompt: STOKE THE HEARTH widens it, up to 62 % of the card.</summary>
        private void FitBuy()
        {
            float word = Mathf.Ceil(_buy.LabelText.GetPreferredValues(_buy.LabelText.text).x);
            if (_innerWidth > 0f && word + BuyChrome > Mathf.Floor(_innerWidth * 0.62f) && _buy.LabelText.text != "BUY")
            {
                // The narrow card (Mac 1.2 × 1.25): the short verb; the name and kind above already say HEARTH.
                _buy.SetText(UiStrings.Get("tree.action.stoke_short", "STOKE"));
                word = Mathf.Ceil(_buy.LabelText.GetPreferredValues(_buy.LabelText.text).x);
            }

            float width = Mathf.Max(BuyWidth, word + BuyChrome);
            if (_innerWidth > 0f)
            {
                width = Mathf.Min(width, Mathf.Floor(_innerWidth * 0.62f));
            }

            ((RectTransform)_buy.transform).sizeDelta = new Vector2(width, UiTokens.TargetPreferred);
            ((RectTransform)_dPrice.transform).offsetMax = new Vector2(-(width + (int)UiSpace.S), 0f);
        }

        /// <summary>
        /// 12b U11 (AD R1-C SHOULD-4; S-04 cb66c375): a tier's stat now → at the next tier, in display units ("RELOAD
        /// −10 % → −20 %", "REGEN DELAY 4.5 s → 4 s"), "… · MAX" at the top; no row for nodes without a single number.
        /// </summary>
        private void ShowStat(RunTreeNodeView n)
        {
            bool show = !string.IsNullOrEmpty(n.Stat) && !float.IsNaN(n.StatNow);
            _dStatRow.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            _dStatLabel.text = UiStrings.Get(n.Stat, n.Stat).ToUpperInvariant();
            _dStatValue.text = float.IsNaN(n.StatNext)
                ? StatText(n.StatNow, n.StatUnit) + " · " + UiStrings.Get("tree.state.max_tier", "MAX")
                : StatText(n.StatNow, n.StatUnit) + "  →  " + StatText(n.StatNext, n.StatUnit);
        }

        /// <summary>A stat in display units: percentages signed ("+20 %", "−10 %"), seconds / HP / HP/s after the number, counts bare.</summary>
        public static string StatText(float value, string unit)
        {
            string number = Mathf.Abs(value - Mathf.Round(value)) < 0.001f
                ? Mathf.Abs(Mathf.RoundToInt(value)).ToString(CultureInfo.InvariantCulture)
                : Mathf.Abs(value).ToString("0.#", CultureInfo.InvariantCulture);
            string sign = value < 0f ? "−" : unit == "%" && value > 0f ? "+" : string.Empty;
            return string.IsNullOrEmpty(unit) ? sign + number : sign + number + " " + unit;
        }

        private void SetPips(int count, int filled)
        {
            if (count != _dPipsCount)
            {
                if (_dPips != null)
                {
                    Destroy(_dPips.gameObject);
                    _dPips = null;
                }

                _dPipsCount = count;
                if (count > 0)
                {
                    _dPips = UiPips.Create("Pips", _dHead, count, 0);
                    PlaceAnchored((RectTransform)_dPips.transform, new Vector2(0f, 0.5f), new Vector2(0f, 1f),
                        new Vector2(_headSize + (int)UiSpace.L, -(int)UiSpace.XS), ((RectTransform)_dPips.transform).sizeDelta);
                }
            }

            if (_dPips != null)
            {
                _dPips.Set(Mathf.Clamp(filled, 0, count), false);
            }
        }

        /// <summary>
        /// WP-077 K10b (S-04): the pack's reviewed line (<c>&lt;StringKey&gt;.desc</c>) with the node's live args in display
        /// units ({value} {count} {seconds} {radius} {range} {max} {base}) - one source for the card and the hooks. The
        /// catalog line when the pack has none or a placeholder would stay open; tiers describe themselves from the live tuning.
        /// </summary>
        public static string DescriptionOf(RunTreeNodeView n)
        {
            if (!string.IsNullOrEmpty(n.StringKey))
            {
                string line = UiStrings.Get(n.StringKey + ".desc", null);
                if (!string.IsNullOrEmpty(line))
                {
                    line = FillArgs(line, n.ArgNames, n.ArgValues);
                    if (line.IndexOf('{') < 0)
                    {
                        return line;
                    }
                }
            }

            return !string.IsNullOrEmpty(n.Description) ? n.Description
                : n.HasBranch ? UpgradeCardCatalog.Describe(n.Branch, n.OwnedTier) : string.Empty;
        }

        /// <summary>Each "{name}" → its value: whole numbers bare ("20"), others to one decimal ("2.5"), invariant culture.</summary>
        public static string FillArgs(string line, string[] names, float[] values)
        {
            if (names == null || values == null)
            {
                return line;
            }

            for (int i = 0; i < names.Length && i < values.Length; i++)
            {
                float v = values[i];
                string text = Mathf.Abs(v - Mathf.Round(v)) < 0.001f
                    ? Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture)
                    : v.ToString("0.#", CultureInfo.InvariantCulture);
                line = line.Replace("{" + names[i] + "}", text);
            }

            return line;
        }

        /// <summary>The node's rule in one muted line: the Hearth's allowance, a fork's exclusion or reforge, the pin's path.</summary>
        private string NoteOf(RunTreeNodeView n)
        {
            if (n.Kind == RunTreeNodeKind.Hearth)
            {
                return UiStrings.Get("tree.hearth.round", "{value} of {max} stokes this round")
                    .Replace("{value}", _snap.HearthThisRound.ToString(CultureInfo.InvariantCulture))
                    .Replace("{max}", _snap.HearthPerRound.ToString(CultureInfo.InvariantCulture));
            }

            if (n.Kind == RunTreeNodeKind.Fork)
            {
                return IsOwned(n) && n.ReforgeCost >= 0
                    ? UiStrings.Get("tree.reforge.caption", "Swap this fork's side once per run. The new side is paid in full.")
                    : UiStrings.Get("tree.fork.closed", "Taking one side closes the other for this run.");
            }

            if (n.IsPinned && _snap.PinPathCount > 0)
            {
                return UiStrings.Get("tree.path_total", "Path total {value} gold").Replace("{value}", _snap.PinPathTotalCost.ToString(CultureInfo.InvariantCulture));
            }

            return !IsOwned(n) && n.Status != RunTreeNodeStatus.Excluded
                ? UiStrings.Get("tree.pin.caption", "A pinned node comes in the next offer once it is reachable.")
                : string.Empty;
        }

        private string StateSentence(RunTreeNodeView n)
        {
            switch (n.Status)
            {
                case RunTreeNodeStatus.Locked:
                    switch (n.LockReason)
                    {
                        case RunTreeLockReason.NotOnThisWeapon: return UiStrings.Get("tree.lock.weapon", "Not a branch of the weapon you carry.");
                        case RunTreeLockReason.NoSpecialYet: return UiStrings.Get("tree.lock.special", "Opens when you pick up a Special this run.");
                        case RunTreeLockReason.EffectPending: return UiStrings.Get("tree.lock.pending", "Not in this build yet.");
                        default: return UiStrings.Get("tree.lock.profile", "Earn it on the PATH first.");
                    }

                case RunTreeNodeStatus.Available:
                    if (n.Kind == RunTreeNodeKind.Hearth || Offered(IndexOfView(n)))
                    {
                        return _shop != null && _shop.IsLocked && n.Kind != RunTreeNodeKind.Hearth
                            ? UiStrings.Get("tree.picks_used", "This round's picks are used.")
                            : UiStrings.Get("tree.state.available", "AVAILABLE");
                    }

                    return UiStrings.Get("tree.flag.not_offered", "NOT IN THIS OFFER");
                default:
                    return StateWord(n);
            }
        }

        // ================================================================== input

        /// <summary>A seal took the focus (pad, keyboard, pointer): the detail card follows it.</summary>
        public void OnNodeSelected(int slot)
        {
            int index = _views[slot].Index;
            if (index >= 0 && index != _focus)
            {
                _focus = index;
                ShowDetail(_focus);
                LinkNavigation();
            }
        }

        /// <summary>
        /// Keyboard (PD §5.5): Q / E switch lanes, P pins the detailed node; right-click pins with the mouse. Polled by
        /// the shop screen's own Update while the tree is up and no pause is over it (T6: no per-frame loop here).
        /// </summary>
        public void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                SetLane(RunTreeLane.Weapon, true);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                SetLane(RunTreeLane.Body, true);
            }
            else if (Input.GetKeyDown(KeyCode.P))
            {
                OnPin();
            }
        }

        /// <summary>Shows a lane (the tabs, Q / E, tests).</summary>
        public void SetLane(RunTreeLane lane, bool focusNode)
        {
            if (lane == _lane || _shop == null)
            {
                return;
            }

            _lane = lane;
            _focus = -1;
            Refresh(_shop);
            UiEvents.Raise(UiEvent.ChoiceSelected, "TreeLane");
            Selectable target = FocusTarget();
            if (focusNode && target != null)
            {
                UiInput.Focus(target);
            }
        }

        /// <summary>
        /// Details a node and gives its seal the focus (tests, captures, the seal's click, right-click pin), switching lanes
        /// when it lives in the other one: the focus ring and the detail card always name the same node (AD R1-C MUST-4).
        /// </summary>
        public void FocusNode(int index)
        {
            if (index < 0 || index >= _snap.Count)
            {
                return;
            }

            _focus = index;
            if (_snap.Nodes[index].Lane != _lane)
            {
                _lane = _snap.Nodes[index].Lane;
                Refresh(_shop);
            }
            else
            {
                ShowDetail(index);
                LinkNavigation();
            }

            UiButton seal = ButtonOf(index);
            if (seal != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != seal.gameObject)
            {
                UiInput.Focus(seal);
            }
        }

        private void OnNodeActivated(int slot)
        {
            int index = _views[slot].Index;
            if (index < 0)
            {
                return;
            }

            FocusNode(index);
            if (_buy.IsInteractable())
            {
                UiInput.Focus(_buy);
            }
        }

        /// <summary>Right-click pins (PD §5.5).</summary>
        public void PinSlot(int slot)
        {
            if (_views[slot].Index >= 0)
            {
                FocusNode(_views[slot].Index);
                OnPin();
            }
        }

        /// <summary>BUY: the Hearth directly, a node through its card (a fork pair is one card), a tier through its branch card.</summary>
        public void OnBuy()
        {
            if (_shop == null || _focus < 0 || _focus >= _snap.Count)
            {
                return;
            }

            RunTreeNodeView n = _snap.Nodes[_focus];
            NodeView view = ViewOf(_focus);
            if (n.Kind == RunTreeNodeKind.Hearth)
            {
                Report(_shop.TryStokeHearth(SessionId, Revision), view, true);
                return;
            }

            int card = CardOf(_focus, n, out bool alternative);
            if (card < 0)
            {
                Report(RunTreeCommandResult.NotAvailable, view, true);
                return;
            }

            if (_shop.TryPurchase(card, SessionId, Revision, alternative))
            {
                Report(RunTreeCommandResult.Done, view, true);
                return;
            }

            // A card refusal keeps the shop's own sentence (UpgradeScreen.DescribeRefusal): one wording per cause.
            UiSound.Play(UiSoundKind.Denied);
            if (view != null)
            {
                UiPresets.DeniedNudge(view.Seal);
            }

            Refused?.Invoke(DescribePurchase != null ? DescribePurchase(_shop.LastResult) : Refusal(RunTreeCommandResult.NotAvailable));
        }

        private int CardOf(int index, RunTreeNodeView n, out bool alternative)
        {
            alternative = false;
            for (int i = 0; i < _shop.Cards.Count; i++)
            {
                UpgradeCardOffer card = _shop.Cards[i];
                if (card.IsValid && !card.Purchased && card.IsNode && (card.NodeIndex == index || card.AltNodeIndex == index))
                {
                    alternative = card.AltNodeIndex == index;
                    return i;
                }
            }

            if (n.Kind == RunTreeNodeKind.Tier && n.HasBranch)
            {
                for (int i = 0; i < _shop.Cards.Count; i++)
                {
                    UpgradeCardOffer card = _shop.Cards[i];
                    if (card.IsValid && !card.Purchased && (!card.IsNode || card.NodeKind == RunTreeNodeKind.Tier) && card.Branch == n.Branch)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        public void OnPin()
        {
            if (_shop == null || _focus < 0 || _focus >= _snap.Count || !_pin.IsInteractable())
            {
                return;
            }

            RunTreeNodeView n = _snap.Nodes[_focus];
            if (!_shop.SetPin(n.IsPinned ? null : n.NodeId))
            {
                Report(RunTreeCommandResult.NotAvailable, ViewOf(_focus), false);
                return;
            }

            UiEvents.Raise(UiEvent.ChoiceSelected, n.IsPinned ? "TreeUnpin" : "TreePin");
            Changed?.Invoke();
        }

        private void OnReforge()
        {
            if (_shop == null || _focus < 0 || _focus >= _snap.Count)
            {
                return;
            }

            Report(_shop.TryReforge(_snap.Nodes[_focus].NodeId, SessionId, Revision), ViewOf(_focus), true);
        }

        private void Report(RunTreeCommandResult result, NodeView view, bool purchase)
        {
            if (result == RunTreeCommandResult.Done)
            {
                if (purchase)
                {
                    UiEvents.Raise(UiEvent.UpgradePurchased);
                }

                Changed?.Invoke();
                if (view != null && view.Index >= 0)
                {
                    UiPresets.StampIn(view.Seal);   // PD §5.4: the seal stamps
                }

                return;
            }

            UiSound.Play(UiSoundKind.Denied);
            if (view != null)
            {
                UiPresets.DeniedNudge(view.Seal);
            }

            Refused?.Invoke(Refusal(result));
        }

        public static string Refusal(RunTreeCommandResult result)
        {
            switch (result)
            {
                case RunTreeCommandResult.InsufficientGold: return UiStrings.Get("tree.refuse.gold", "Not enough gold for that yet. Nothing was taken.");
                case RunTreeCommandResult.LimitReached: return UiStrings.Get("tree.refuse.limit", "No more of that this round. Nothing was taken.");
                case RunTreeCommandResult.NothingToUndo: return UiStrings.Get("tree.refuse.nothing_to_undo", "There is nothing to undo.");
                case RunTreeCommandResult.UndoClosed: return UiStrings.Get("tree.refuse.undo_closed", "Undo has closed for this offer.");
                case RunTreeCommandResult.AlreadyUsed: return UiStrings.Get("tree.refuse.reforge_used", "The run's one Reforge is spent.");
                case RunTreeCommandResult.TooEarly: return UiStrings.Get("tree.refuse.reforge_early", "Reforge opens from round 5.");
                case RunTreeCommandResult.NoRun: return UiStrings.Get("tree.refuse.no_run", "No run is in progress.");
                default: return UiStrings.Get("tree.refuse.not_offered", "That is not for sale in this offer. Nothing was taken.");
            }
        }

        private NodeView ViewOf(int index)
        {
            if (index < 0)
            {
                return null;
            }

            for (int s = 0; s < _views.Length; s++)
            {
                if (_views[s] != null && _views[s].Index == index)
                {
                    return _views[s];
                }
            }

            return null;
        }

        // ================================================================== helpers

        private static float LineOf(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag").y);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        private static void PlaceAnchored(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 at, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = at;
        }

        private static void PlaceTopLeft(RectTransform rect, float left, float top, float width, float height)
        {
            PlaceAnchored(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, -top), new Vector2(width, height));
        }

        private static void Band(TMP_Text text, float left, float top, float height, TextAlignmentOptions alignment)
        {
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
            text.alignment = alignment;
        }

        private static void Dock(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
        }

        private static void DockBottom(RectTransform rect, float bottom, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(0f, bottom);
            rect.offsetMax = new Vector2(0f, bottom + height);
        }
    }

    /// <summary>A seal's own input: the focus it takes drives the detail card, and a right-click pins it (PD §5.5).</summary>
    public sealed class UiTreeNodeInput : MonoBehaviour, IPointerClickHandler, ISelectHandler
    {
        private UiRunTreePanel _panel;
        private int _slot;

        public void Configure(UiRunTreePanel panel, int slot)
        {
            _panel = panel;
            _slot = slot;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (_panel != null)
            {
                _panel.OnNodeSelected(_slot);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && _panel != null)
            {
                _panel.PinSlot(_slot);
            }
        }
    }
}
