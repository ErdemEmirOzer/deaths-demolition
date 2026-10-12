using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-104 (S-09) · the Awakening ("Uyanış") page (KARAR-WP104-11.10 decision 2; GD-TREE §3): Ash opens variety on the run
    /// tree, never power - seven alternative capstones, two fork widenings, two aids (WP-112: Second Thoughts left the release),
    /// on a 4 × 3 grid of rows 4 · 3 · 4 (the row of three centred) with the AP-042 Awakening icons. A brush window (UiBrushKit: Window scrim, one column wash, the C5 title with the PATH glyph,
    /// C11 actions, the C10 Back): grid on the left, the focused node's words, price and UNLOCK on the right.
    /// DATA AND COMMAND: <see cref="IAwakeningSource"/> - the game's is <see cref="ProfileAwakeningSource"/>: S-04's B1a meta
    /// catalog (the Awakening page, 11 nodes) and the profile; UNLOCK is S-04's <c>ProfileService.TryBuyMetaNode</c> behind a
    /// 500 ms hold (S-04 confirmed 12.10; <see cref="UiAwakeningRoute.PurchaseEnabled"/>). A build whose catalog has no Awakening
    /// node (before B1a) shows the pending line - "Not in this build yet. Your Ash is kept." - and no UNLOCK. A node the string
    /// pack does not name is drawn with its icon and an em dash, never its id.
    /// OPENED from the Results screen's AWAKENING control while <see cref="UiAwakeningRoute.LinkEnabled"/> (off until B1, C-00
    /// 11.10) and by the capture agent; one page at a time (<see cref="UiAwakeningRoute"/>). Cancel / Back closes it and the focus
    /// returns to the opener. No per-frame loop: the host's Update forwards <see cref="HandleKeys"/> (T6).
    /// </summary>
    public sealed class UiAwakeningPage : MonoBehaviour
    {
        public const string PaneName = "AwakeningPane";
        private const float CellGap = 16f;
        private const float IconMax = 96f;
        /// <summary>fix2: below this the tile's bands take the words' own height instead of the line tokens (Mac 1.2 × text 1.25).</summary>
        private const float IconMin = 48f;
        private const float DetailShare = 0.38f;
        private const float PageMaxWidth = 1408f;

        private sealed class Cell
        {
            public UiButton Button;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text State;
            public Image Glyph;   // fix2: the lock / unlocked mark beside the tile's price
        }

        private RectTransform _screenRoot;
        private RectTransform _host;
        private UiBrushPane _pane;
        private UiBrushTitle _title;
        private TMP_Text _balance;
        private TMP_Text _pending;
        private RectTransform _grid;
        private readonly Cell[] _cells = new Cell[AwakeningPageModel.LayoutIds.Length];
        private RectTransform _detail;
        private Image _dIcon;
        private TMP_Text _dName;
        private TMP_Text _dAlt;
        private TMP_Text _dDesc;
        private TMP_Text _dState;
        private TMP_Text _note;
        private UiButton _unlock;
        private UiIconButton _back;

        private readonly AwakeningPageModel _model = new AwakeningPageModel();
        private IAwakeningSource _source;
        private Action _onClosed;
        private Selectable _opener;
        private int _focus = -1;
        private bool _open;
        private readonly List<GameObject> _hostHidden = new List<GameObject>(16);
        private float _cellW;      // fix2: the tile geometry the state line is measured against
        private float _stateY;
        private float _stateH;

        public bool IsOpen => _open;
        public AwakeningPageModel Model => _model;
        public UiBrushPane Pane => _pane;
        public UiButton UnlockButton => _unlock;
        public TMP_Text PendingLine => _pending;
        public TMP_Text DetailName => _dName;
        public TMP_Text DetailDescription => _dDesc;
        public int FocusIndex => _focus;

        public UiButton CellButton(int index)
        {
            return index >= 0 && index < _cells.Length && _cells[index] != null ? _cells[index].Button : null;
        }

        public TMP_Text CellName(int index)
        {
            return index >= 0 && index < _cells.Length && _cells[index] != null ? _cells[index].Name : null;
        }

        /// <summary>Tests (fix2): the tile's short state line and its glyph.</summary>
        public TMP_Text CellState(int index)
        {
            return index >= 0 && index < _cells.Length && _cells[index] != null ? _cells[index].State : null;
        }

        public Image CellGlyph(int index)
        {
            return index >= 0 && index < _cells.Length && _cells[index] != null ? _cells[index].Glyph : null;
        }

        // ================================================================== build

        public static UiAwakeningPage Create(RectTransform screenRoot)
        {
            RectTransform holder = UiFactory.NewRect("AwakeningPage", screenRoot);
            UiFactory.Fill(holder);
            var page = holder.gameObject.AddComponent<UiAwakeningPage>();
            page.Build(screenRoot, holder);
            return page;
        }

        private void Build(RectTransform screenRoot, RectTransform holder)
        {
            _screenRoot = screenRoot;
            _host = UiFactory.NewRect("Host", holder);
            UiFactory.Fill(_host);
            float side = Mathf.Max((int)UiSpace.XL, (screenRoot.rect.width - PageMaxWidth) * 0.5f);
            _host.offsetMin = new Vector2(side, (int)UiSpace.Section);
            _host.offsetMax = new Vector2(-side, -(int)UiSpace.Section);

            _pane = UiBrushKit.Pane(screenRoot, _host, PaneName, UiBrushScrim.Window, UiBrushWash.Column);
            RectTransform page = _pane.Content;
            _title = UiBrushKit.Title(page, "awakening.title", "AWAKENING", "Menu.Path");

            _balance = UiFactory.Label("Balance", page, string.Empty, UiType.MenuBody, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            _balance.enableWordWrapping = false;
            _pending = UiFactory.Label("Pending", page, string.Empty, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.UpperLeft);
            _pending.enableWordWrapping = true;

            _grid = UiFactory.NewRect("Grid", page);
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = BuildCell(i);
            }

            _detail = UiFactory.NewRect("Detail", page);
            _dIcon = UiFactory.NewImage("Icon", _detail, UiColor.TextPrimary);
            _dIcon.preserveAspect = true;
            _dName = DetailText("Name", UiType.H2, UiColor.TextPrimary);
            _dAlt = DetailText("Alternative", UiType.MenuSmall, UiColor.TextSecondary);
            _dDesc = DetailText("Description", UiType.MenuSmall, UiColor.TextPrimary);
            _dState = DetailText("State", UiType.MenuBody, UiColor.TextPrimary);
            _note = DetailText("Note", UiType.MenuSmall, UiColor.TextSecondary);
            _unlock = UiBrushKit.Button(_detail, "Unlock", "awakening.title", "AWAKENING", null, OnUnlock);
            _unlock.ConfigureHold(UiMotionTokens.HoldConfirm);   // ProfileService.TryBuyMetaNode: "the screen confirms with HoldConfirm 500 ms"
            _back = UiBrushKit.Back(page, Close);
            _pane.Root.gameObject.SetActive(false);
        }

        private Cell BuildCell(int index)
        {
            var c = new Cell();
            int captured = index;
            c.Button = UiFactory.Button("Awakening." + AwakeningPageModel.LayoutIds[index], _grid, string.Empty, UiButtonVariant.Brush, () => OnCellActivated(captured));
            c.Button.LabelText.gameObject.SetActive(false);
            c.Button.ConfirmCue = false;
            c.Button.gameObject.AddComponent<UiAwakeningCellInput>().Configure(this, captured);
            c.Icon = UiFactory.NewImage("Icon", c.Button.transform, UiColor.TextPrimary);
            c.Icon.preserveAspect = true;
            c.Icon.raycastTarget = false;
            c.Name = UiFactory.Label("Name", c.Button.transform, string.Empty, UiType.MenuSmall, UiColor.TextPrimary, TextAnchor.UpperCenter);
            c.Name.enableWordWrapping = true;
            c.Name.overflowMode = TextOverflowModes.Ellipsis;
            c.State = UiFactory.Label("State", c.Button.transform, string.Empty, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.UpperCenter);
            c.State.enableWordWrapping = false;
            c.Glyph = UiArt.IconImage("StateGlyph", c.Button.transform, AwakeningPageModel.LockGlyph, UiColor.TextSecondary, GlyphSize());
            c.Glyph.raycastTarget = false;
            return c;
        }

        private TMP_Text DetailText(string name, UiType type, UiColor color)
        {
            TMP_Text text = UiFactory.Label(name, _detail, string.Empty, type, color, TextAnchor.UpperLeft);
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        // ================================================================== open / close

        public void Open(IAwakeningSource source, Action onClosed)
        {
            _source = source;
            _onClosed = onClosed;
            EventSystem system = EventSystem.current;
            _opener = system != null && system.currentSelectedGameObject != null ? system.currentSelectedGameObject.GetComponent<Selectable>() : null;
            transform.SetAsLastSibling();
            HideHost();
            _open = true;
            _note.text = string.Empty;
            Refresh();
            int first = 0;
            for (int i = 0; i < _model.Entries.Count; i++)
            {
                if (_model.Entries[i].State == AwakeningEntryState.Available)
                {
                    first = i;
                    break;
                }
            }

            _pane.Open(_cells[Mathf.Clamp(first, 0, _cells.Length - 1)].Button);
            ShowDetail(first);
        }

        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _pane.HandleCancel();
            RestoreHost();
            Action closed = _onClosed;
            _onClosed = null;
            if (_opener != null && _opener.gameObject.activeInHierarchy)
            {
                UiInput.Focus(_opener);
            }

            closed?.Invoke();
        }

        /// <summary>
        /// AD M4 (12.10): the host screen's own layers - the menu's logo, rail words and level bar, Results' lines - read through
        /// the Window scrim (linear blending) right under the page's title. While the page is up they step out; the world behind
        /// and the page's own scrim stay. Close gives back exactly what Open took.
        /// </summary>
        private void HideHost()
        {
            if (_screenRoot == null)
            {
                return;
            }

            string scrim = PaneName + ".Scrim";
            for (int i = 0; i < _screenRoot.childCount; i++)
            {
                Transform child = _screenRoot.GetChild(i);
                if (child == transform || child.name == scrim || !child.gameObject.activeSelf || child.GetComponent<UiAwakeningPage>() != null)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
                _hostHidden.Add(child.gameObject);
            }
        }

        private void RestoreHost()
        {
            for (int i = 0; i < _hostHidden.Count; i++)
            {
                if (_hostHidden[i] != null)
                {
                    _hostHidden[i].SetActive(true);
                }
            }

            _hostHidden.Clear();
        }

        /// <summary>Tests (AD M4): the host layers the open page holds hidden.</summary>
        internal int HostHiddenCount => _hostHidden.Count;

        /// <summary>Forwarded by the host's Update while open: Cancel closes, the focus is kept inside the page.</summary>
        public void HandleKeys()
        {
            if (!_open)
            {
                return;
            }

            if (UiInput.CancelPressed())
            {
                Close();
                return;
            }

            UiInput.ReclaimFocus(CellButton(Mathf.Max(0, _focus)), _pane.Root);
        }

        // ================================================================== refresh

        public void Refresh()
        {
            if (_source != null)
            {
                _source.Read(_model);
            }
            else
            {
                _model.ResetToLayout();
            }

            _balance.text = TreeV2Text.Ash(_model.AshBalance);
            _pending.text = _model.Pending || !_model.CanUnlock ? UiStrings.Get("awakening.pending", "Not in this build yet. Your Ash is kept.") : string.Empty;
            if (_model.AshBalance <= 0 && !_model.Pending)
            {
                _pending.text = UiStrings.Get("awakening.empty", "Ash is earned at the end of each run.");
            }

            Layout();
            for (int i = 0; i < _cells.Length; i++)
            {
                ApplyCell(i);
            }

            LinkNavigation();
            if (_focus >= 0)
            {
                ShowDetail(_focus);
            }
        }

        private void Layout()
        {
            RectTransform page = _pane.Content;
            float width = page.rect.width > 1f ? page.rect.width : _host.rect.width;
            float height = page.rect.height > 1f ? page.rect.height : _host.rect.height;
            float y = _title.Height + (int)UiSpace.L;
            float line = UiFactory.LineHeight(UiType.MenuBody);
            Set(_balance.rectTransform, 0f, y, width, line);
            y += line + (int)UiSpace.S;
            float pendingHeight = Mathf.Ceil(_pending.GetPreferredValues(string.IsNullOrEmpty(_pending.text) ? "Ag" : _pending.text, width, 0f).y) + 2f;
            Set(_pending.rectTransform, 0f, y, width, pendingHeight);
            y += pendingHeight + (int)UiSpace.L;

            float footer = UiBrushTokens.ControlHeight + (int)UiSpace.L;
            float gridWidth = Mathf.Floor(width * (1f - DetailShare)) - CellGap;
            float gridHeight = Mathf.Max(1f, height - y - footer);
            Set(_grid, 0f, y, gridWidth, gridHeight);
            float cellW = (gridWidth - CellGap * (AwakeningPageModel.Columns - 1)) / AwakeningPageModel.Columns;
            float cellH = (gridHeight - CellGap * (AwakeningPageModel.Rows - 1)) / AwakeningPageModel.Rows;
            float small = UiFactory.LineHeight(UiType.MenuSmall);
            float nameH = small * 2f;
            float stateH = small;
            if (cellH - nameH - stateH - 8f < IconMin)
            {
                // fix2: a short tile (Mac UI 1.2 × text 1.25): the name's two lines and the state's one take the words' own
                // height (the line token carries the paragraph spacing), so the icon keeps a size and nothing leaves the tile.
                TMP_Text probe = _cells[0].Name;
                nameH = Mathf.Ceil(probe.GetPreferredValues("Ag\nAg").y) + 2f;
                stateH = Mathf.Ceil(probe.GetPreferredValues("Ag").y) + 2f;
            }

            float icon = Mathf.Clamp(Mathf.Min(cellW, cellH - nameH - stateH - 8f), 24f, IconMax);
            for (int i = 0; i < _model.Entries.Count && i < _cells.Length; i++)
            {
                AwakeningEntry e = _model.Entries[i];
                Cell c = _cells[i];
                Set((RectTransform)c.Button.transform, _model.VisualColumn(i) * (cellW + CellGap), e.Row * (cellH + CellGap), cellW, cellH);
                Set(c.Icon.rectTransform, (cellW - icon) * 0.5f, 4f, icon, icon);
                Set(c.Name.rectTransform, 2f, icon + 8f, cellW - 4f, nameH);
                Set(c.State.rectTransform, 2f, icon + 8f + nameH, cellW - 4f, stateH);
            }

            _cellW = cellW;
            _stateY = icon + 8f + nameH;
            _stateH = stateH;

            Set(_detail, gridWidth + CellGap * 2f, y, Mathf.Max(1f, width - gridWidth - CellGap * 2f), gridHeight);
            var back = (RectTransform)_back.transform;
            Set(back, 0f, height - UiBrushTokens.ControlHeight, back.sizeDelta.x, UiBrushTokens.ControlHeight);
        }

        private void ApplyCell(int i)
        {
            Cell c = _cells[i];
            bool present = i < _model.Entries.Count;
            c.Button.gameObject.SetActive(present);
            if (!present)
            {
                return;
            }

            AwakeningEntry e = _model.Entries[i];
            Sprite icon = TreeArt.NodeIcon(e.Id, c.Icon.rectTransform.rect.width * HudArt.CanvasScale);
            c.Icon.sprite = icon != null ? icon : UiSprites.Disc;
            c.Icon.color = icon == null ? WithAlpha(UiTokens.Get(UiColor.TextMuted), 0.6f)
                : e.State == AwakeningEntryState.Unlocked || e.State == AwakeningEntryState.Available ? Color.white : WithAlpha(Color.white, 0.6f);
            c.Name.text = AwakeningPageModel.Name(e);
            FitTileState(c, e);
        }

        /// <summary>The tile glyph's size: about a MenuSmall cap, so it scales with the text.</summary>
        private static float GlyphSize()
        {
            return Mathf.Round(UiTokens.FontSize(UiType.MenuSmall) * 0.9f);
        }

        /// <summary>
        /// fix2 (12.10 capture: the Turkish "300 ASH SHORT" cut on a 129 px tile) · the tile's state is a short line measured against the
        /// tile at the live text scale, never cut: price (or UNLOCKED) with its glyph; the words alone when the pair does not fit;
        /// the glyph alone when even the words do not. The full sentence ("300 ASH SHORT") stands in the detail.
        /// </summary>
        private void FitTileState(Cell c, in AwakeningEntry e)
        {
            string word = AwakeningPageModel.TileLine(e);
            string key = AwakeningPageModel.TileGlyph(e);
            Sprite glyph = key != null ? UiArt.Icon(key) : null;
            float room = Mathf.Max(1f, _cellW - 4f);
            float size = GlyphSize();
            float gap = (int)UiSpace.XS;
            float textW = string.IsNullOrEmpty(word) ? 0f : Mathf.Ceil(c.State.GetPreferredValues(word).x) + 2f;
            bool showGlyph = glyph != null;
            bool showWord = textW > 0f;
            if (showGlyph && showWord && textW + gap + size > room)
            {
                if (textW <= room)
                {
                    showGlyph = false;   // the words carry it; the dimmed icon and the detail say the rest
                }
                else
                {
                    showWord = false;    // UNLOCKED wider than the tile at 1.25: the mark alone
                }
            }

            float total = (showGlyph ? size : 0f) + (showGlyph && showWord ? gap : 0f) + (showWord ? textW : 0f);
            float x = Mathf.Max(2f, (_cellW - total) * 0.5f);
            c.Glyph.enabled = showGlyph;
            if (showGlyph)
            {
                c.Glyph.sprite = glyph;
                Set(c.Glyph.rectTransform, x, _stateY + Mathf.Max(0f, (_stateH - size) * 0.5f), size, size);
                x += size + gap;
            }

            c.State.text = showWord ? word : string.Empty;
            c.State.gameObject.SetActive(showWord);
            if (showWord)
            {
                Set(c.State.rectTransform, x, _stateY, Mathf.Min(textW, room), _stateH);
            }
        }

        private void ShowDetail(int index)
        {
            if (index < 0 || index >= _model.Entries.Count)
            {
                return;
            }

            _focus = index;
            AwakeningEntry e = _model.Entries[index];
            float width = Mathf.Max(1f, _detail.rect.width);
            float icon = Mathf.Min(160f, width * 0.45f);
            Sprite sprite = TreeArt.NodeIcon(e.Id, icon * HudArt.CanvasScale);
            _dIcon.sprite = sprite != null ? sprite : UiSprites.Disc;
            _dIcon.color = sprite == null ? WithAlpha(UiTokens.Get(UiColor.TextMuted), 0.6f) : Color.white;
            _dName.text = AwakeningPageModel.Name(e);
            string alt = AwakeningPageModel.AlternativeName(e.Id);
            _dAlt.text = alt == null ? string.Empty : UiStrings.Format("awakening.alt_of", "node", UiStrings.Upper(alt), "ALTERNATIVE TO {node}");
            string desc = AwakeningPageModel.Description(e);
            if (alt != null && !string.IsNullOrEmpty(desc))
            {
                desc += "\n" + UiStrings.Format("awakening.alt_note", "node", alt, "In a run, take either this or {node}.");
            }

            _dDesc.text = desc;
            _dState.text = AwakeningPageModel.StateLine(e, _model.AshBalance);
            bool live = _model.CanUnlock && e.State == AwakeningEntryState.Available;
            _unlock.gameObject.SetActive(_model.CanUnlock && e.State != AwakeningEntryState.Unlocked && e.State != AwakeningEntryState.Pending);
            _unlock.SetText(UiStrings.Format("awakening.unlock", "value", UiStrings.Number(e.Cost), "UNLOCK · {value} ASH"));
            _unlock.SetInteractable(live);

            float y = 0f;
            Set(_dIcon.rectTransform, 0f, y, icon, icon);
            y += icon + (int)UiSpace.M;
            y = Stack(_dName, y, width, 2);
            y = Stack(_dAlt, y, width, 2);
            y = Stack(_dDesc, y, width, 99);
            y = Stack(_dState, y, width, 1);
            if (_unlock.gameObject.activeSelf)
            {
                float w = Mathf.Min(UiBrushKit.FitWidth(_unlock), width);
                Set((RectTransform)_unlock.transform, 0f, y, w, UiBrushTokens.ControlHeight);
                y += UiBrushTokens.ControlHeight + (int)UiSpace.S;
            }

            Stack(_note, y, width, 3);
            LinkNavigation();
        }

        private static float Stack(TMP_Text text, float y, float width, int lines)
        {
            if (string.IsNullOrEmpty(text.text))
            {
                text.gameObject.SetActive(false);
                return y;
            }

            text.gameObject.SetActive(true);
            float line = Mathf.Ceil(text.GetPreferredValues("Ag").y);
            float h = Mathf.Min(Mathf.Max(line, Mathf.Ceil(text.GetPreferredValues(text.text, width, 0f).y)), line * lines) + 2f;
            Set(text.rectTransform, 0f, y, width, h);
            return y + h + (int)UiSpace.S;
        }

        // ================================================================== commands / input

        internal void OnCellSelected(int index)
        {
            ShowDetail(index);
        }

        private void OnCellActivated(int index)
        {
            ShowDetail(index);
            if (_unlock.gameObject.activeSelf && _unlock.IsInteractable())
            {
                UiInput.Focus(_unlock);
            }
        }

        private void OnUnlock()
        {
            if (_source == null || _focus < 0 || _focus >= _model.Entries.Count)
            {
                return;
            }

            string id = _model.Entries[_focus].Id;
            if (_source.TryUnlock(id, out string refusal))
            {
                _note.text = string.Empty;
                UiEvents.Raise(UiEvent.UnlockRevealed, id);
            }
            else
            {
                UiSound.Play(UiSoundKind.Denied);
                _note.text = refusal;
            }

            Refresh();
            UiInput.Focus(CellButton(_focus));
        }

        /// <summary>The grid by rows and columns (explicit, from the catalog's places), the detail's UNLOCK to the right, Back below.</summary>
        private void LinkNavigation()
        {
            int count = Mathf.Min(_model.Entries.Count, _cells.Length);
            for (int i = 0; i < count; i++)
            {
                UnityEngine.UI.Navigation nav = _cells[i].Button.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                nav.selectOnLeft = Nearest(i, 0, -1, count);
                Selectable right = Nearest(i, 0, 1, count);
                nav.selectOnRight = right != null ? right : (_unlock.gameObject.activeSelf && _unlock.IsInteractable() ? _unlock : null);
                nav.selectOnUp = Nearest(i, -1, 0, count);
                Selectable down = Nearest(i, 1, 0, count);
                nav.selectOnDown = down != null ? down : _back.Button;
                _cells[i].Button.navigation = nav;
            }

            Selectable focusCell = CellButton(Mathf.Max(0, _focus));
            UiInput.LinkHorizontal(_unlock, focusCell, null);
            UnityEngine.UI.Navigation backNav = _back.Button.navigation;
            backNav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            backNav.selectOnUp = focusCell;
            _back.Button.navigation = backNav;
        }

        /// <summary>
        /// The nearest live cell from cell <paramref name="from"/> stepping (dRow, dColumn): same row / column first, then the
        /// closest. Columns are the drawn ones (WP-112: the centred row of three sits half a cell in).
        /// </summary>
        private Selectable Nearest(int from, int dRow, int dColumn, int count)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            int row = _model.Entries[from].Row;
            float column = _model.VisualColumn(from);
            for (int j = 0; j < count; j++)
            {
                AwakeningEntry o = _model.Entries[j];
                int dr = o.Row - row;
                float dc = _model.VisualColumn(j) - column;
                if (dRow != 0 && Math.Sign(dr) != dRow) continue;
                if (dColumn != 0 && (dr != 0 || Math.Sign(dc) != dColumn)) continue;
                float score = dRow != 0 ? Mathf.Abs(dr) * 10f + Mathf.Abs(dc) : Mathf.Abs(dc);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = j;
                }
            }

            return best >= 0 ? _cells[best].Button : null;
        }

        private static void Set(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }

    /// <summary>An Awakening cell's focus drives the page's detail (pad, keyboard, hover).</summary>
    public sealed class UiAwakeningCellInput : MonoBehaviour, ISelectHandler
    {
        private UiAwakeningPage _page;
        private int _index;

        public void Configure(UiAwakeningPage page, int index)
        {
            _page = page;
            _index = index;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (_page != null)
            {
                _page.OnCellSelected(_index);
            }
        }
    }
}
