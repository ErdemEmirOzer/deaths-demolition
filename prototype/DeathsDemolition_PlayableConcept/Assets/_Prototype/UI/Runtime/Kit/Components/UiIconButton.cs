using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>The glyph inside an icon control (WP-081 B2). 24 is the legibility floor (R2 U1); every size sits on the 8 px grid.</summary>
    public enum UiIconSize
    {
        S = 24,
        M = 32,
        L = 40
    }

    /// <summary>
    /// How an icon control shows its word: on lift (hover / focus) as a tooltip, always under the glyph (a tab), or beside it
    /// where its screen sets the word (a menu rail: the focused item's word stands next to it - no tooltip).
    /// </summary>
    public enum UiIconLabel
    {
        Tooltip = 0,
        Below = 1,
        Beside = 2
    }

    /// <summary>
    /// WP-081 B2 · the icon control's numbers. One grid (8 px), one hit floor (44, <see cref="UiTokens.TargetCompact"/>),
    /// one glyph floor (24). A glyph never fills its hit: the square is the glyph plus a grid unit of air on each side,
    /// rounded up to the grid, never under 44.
    /// </summary>
    public static class UiIconTokens
    {
        public const int Grid = (int)UiSpace.S;
        public const int GlyphFloor = (int)UiIconSize.S;
        public const int HitFloor = UiTokens.TargetCompact;

        /// <summary>A tab's narrowest width (12 grid units): short words keep a steady rhythm along the row.</summary>
        public const int TabMinWidth = 96;

        /// <summary>Glyph to word in a tab, and the word's air at the tab's foot.</summary>
        public const UiSpace TabLabelGap = UiSpace.XS;
        public const UiSpace TabPadding = UiSpace.M;

        /// <summary>The tab's chosen bar under its word: a shape, so the choice never rests on colour alone.</summary>
        public const float TabBarHeight = 2f;
        public const float TabBarWidth = 24f;

        /// <summary>Tooltip: its gap to the control, its padding, its widest line, and the hover / focus settle before it shows.</summary>
        public const UiSpace TooltipGap = UiSpace.S;
        public const UiSpace TooltipPadX = UiSpace.M;
        public const UiSpace TooltipPadY = UiSpace.XS;
        public const float TooltipMaxWidth = 320f;
        public const float TooltipDelaySeconds = 0.3f;

        /// <summary>The square hit of an icon button for a glyph size.</summary>
        public static float Hit(UiIconSize size)
        {
            return Mathf.Max(HitFloor, Snap((int)size + 2 * Grid));
        }

        /// <summary>Up to the next grid line.</summary>
        public static float Snap(float value)
        {
            return Mathf.Ceil(value / Grid) * Grid;
        }
    }

    /// <summary>
    /// WP-081 B2 (C-00 05.10, R2 U1 "less text, more icons") · the icon control of menu v4: a glyph from the G1 SDF atlas,
    /// a short word, and a reader name.
    ///  · <see cref="AccessibleName"/>: what the control does, in the language on screen (UiStrings) - the object's name,
    ///    the events' name, and <see cref="Narration"/> ("SETTINGS, SELECTED") for a reader or narration layer.
    ///  · <see cref="ShortLabel"/>: the word the player sees - under the glyph in a tab, in the tooltip of a button
    ///    (<see cref="UiTooltip"/>, on hover and on keyboard / gamepad focus alike: hover moves focus, UiButton).
    ///  · Focus, press, hold and the disabled pair are UiButton's own; a tab is a Nav segment (raised fill + cream outline)
    ///    plus a bar under its word.
    /// The glyph is tinted by token: TextPrimary when it is the choice or a plain button, TextSecondary for the other tabs.
    /// WP-081 ikon v2 B5b: an O1 glyph (<see cref="UiArt.IsO1Glyph"/>) draws through the icon material (DD/UI/Fx Icon) -
    /// light only ever adds to that token (UiIconMaterialTests T1), and a disabled control shows the flat token.
    ///
    /// WP-098A P0b (C-00 07.10) · C12 Brush tab (<see cref="UiControlStyle.Brush"/>, KILAVUZ §4.1 C12, KIT-API §4 "Choice
    /// group"): a frameless <see cref="UiButtonVariant.Brush"/> control - no Nav segment fill, no ChosenBar; a 40 px glyph,
    /// TextSecondary until it is the choice (TextPrimary), the choice's C8 drawn by its <see cref="UiBrushChoiceGroup"/>.
    /// <see cref="UiBrushTokens.TabWidth"/>×<see cref="UiBrushTokens.ControlHeight"/> with the word as the tooltip; with
    /// the word under the glyph (G2 interim, S-09 07.10 §8 S1: <c>showWords</c>) as wide and as tall as the word needs.
    ///
    /// USAGE: <see cref="UiIconControls.Button"/>, <see cref="UiIconControls.Tab"/>, <see cref="UiIconControls.Tabs"/>.
    /// DON'T: ship an icon without a string key (no literal names), or a glyph under 24 px.
    /// ACCESSIBILITY: a name in every language, a word on focus, a 44 px hit; the choice is shape + luminance, not hue.
    /// </summary>
    public sealed class UiIconButton : MonoBehaviour
    {
        private static readonly HashSet<string> s_missingIcons = new HashSet<string>();

        private UiButton _button;
        private Image _glyph;
        private UiFx _material;   // the glyph's Icon driver (an O1 key), else null: the glyph stays SdfGlyph
        private Image _bar;
        private string _iconKey;
        private string _nameKey;
        private string _nameFallback;
        private string _shortKey;
        private string _shortFallback;
        private UiIconLabel _labelMode;
        private UiIconSize _size;
        private bool _tab;
        private string _objectName;
        private UiControlStyle _style;

        /// <summary>WP-098A: the dialect this control is drawn in.</summary>
        public UiControlStyle Style
        {
            get { return _style; }
        }

        public UiButton Button
        {
            get { return _button; }
        }

        public Image Glyph
        {
            get { return _glyph; }
        }

        /// <summary>WP-081 ikon v2 B5b · the glyph's DD/UI/Fx Icon driver when its key is O1 (B5c writes its channels), else null.</summary>
        public UiFx IconMaterial
        {
            get { return _material; }
        }

        public string IconKey
        {
            get { return _iconKey; }
        }

        public string NameKey
        {
            get { return _nameKey; }
        }

        public string ShortKey
        {
            get { return _shortKey; }
        }

        public UiIconLabel LabelMode
        {
            get { return _labelMode; }
        }

        public UiIconSize Size
        {
            get { return _size; }
        }

        public bool IsTab
        {
            get { return _tab; }
        }

        /// <summary>The glyph resolved in the G1 atlas (false: the key is missing - see <see cref="MissingIconKeys"/>).</summary>
        public bool HasGlyph
        {
            get { return _glyph != null && _glyph.sprite != null && _glyph.enabled; }
        }

        /// <summary>The reader name in the language on screen.</summary>
        public string AccessibleName
        {
            get { return UiStrings.Get(_nameKey, _nameFallback); }
        }

        /// <summary>The visible word: the short key when there is one, else the reader name.</summary>
        public string ShortLabel
        {
            get { return string.IsNullOrEmpty(_shortKey) ? AccessibleName : UiStrings.Get(_shortKey, _shortFallback); }
        }

        /// <summary>
        /// Name and state in the language on screen - one line for a reader or narration layer (and the tests). The role
        /// is <see cref="IsTab"/>: a reader speaks its own word for it.
        /// </summary>
        public string Narration
        {
            get
            {
                string text = AccessibleName;
                if (_tab && _button != null && _button.IsSelectedChoice)
                {
                    text += ", " + UiStrings.Get("selection.selected", "SELECTED");
                }

                if (_button != null && !_button.IsInteractable())
                {
                    text += ", " + UiStrings.Get("selection.unavailable", "UNAVAILABLE");
                }

                return text;
            }
        }

        /// <summary>The chosen bar is on screen (tabs).</summary>
        public bool BarVisible
        {
            get { return _bar != null && _bar.gameObject.activeSelf; }
        }

        /// <summary>Icon keys asked for that the G1 atlas does not have (tests: an empty set on every screen).</summary>
        public static IReadOnlyCollection<string> MissingIconKeys
        {
            get { return s_missingIcons; }
        }

        internal void Bind(UiButton button, string iconKey, string nameKey, string nameFallback, string shortKey, string shortFallback, UiIconLabel labelMode, UiIconSize size, bool tab,
            UiControlStyle style = UiControlStyle.V3)
        {
            _style = style;
            _button = button;
            _iconKey = iconKey;
            _nameKey = nameKey;
            _nameFallback = nameFallback;
            _shortKey = shortKey;
            _shortFallback = shortFallback;
            _labelMode = labelMode;
            _size = (int)size < UiIconTokens.GlyphFloor ? UiIconSize.S : size;
            _tab = tab;

            _glyph = UiArt.IconImage("Glyph", button.transform, iconKey, UiColor.TextPrimary, (int)_size);
            if (!UiArt.HasIcon(iconKey))
            {
                s_missingIcons.Add(iconKey ?? string.Empty);
            }

            // WP-081 ikon v2 B5b: an O1 glyph takes the icon material; any other key keeps SdfGlyph (C-00 06.10).
            _material = UiArt.ApplyIconMaterial(_glyph, iconKey, true);

            if (tab && style == UiControlStyle.V3)   // WP-098A: a Brush tab's choice is the group's one C8, not a bar
            {
                _bar = UiFactory.NewImage("ChosenBar", button.transform, UiColor.TextPrimary, UiSprites.Solid);
                var barRect = (RectTransform)_bar.transform;
                barRect.anchorMin = new Vector2(0.5f, 0f);
                barRect.anchorMax = new Vector2(0.5f, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                barRect.sizeDelta = new Vector2(UiIconTokens.TabBarWidth, UiIconTokens.TabBarHeight);
                barRect.anchoredPosition = new Vector2(0f, UiTokens.StrokeControl + 2f);
                _bar.gameObject.SetActive(false);
            }

            if (labelMode == UiIconLabel.Tooltip)
            {
                button.LiftChanged += OnLift;
            }

            Layout();
            RefreshState();
        }

        /// <summary>Re-read the words (a language change rebuilds screens; a pane that stays can call this).</summary>
        public void RefreshText()
        {
            gameObject.name = _objectName ?? AccessibleName;
            if (_labelMode == UiIconLabel.Below && _button.LabelText != null)
            {
                _button.SetText(ShortLabel);
            }

            Layout();
        }

        /// <summary>
        /// Keep a fixed object name (and so the events' name) instead of the reader name - a screen whose tests and cues know
        /// its controls by id ("Tab.Profile", "Play"). The reader name still comes from the table.
        /// </summary>
        public void PinObjectName(string objectName)
        {
            _objectName = objectName;
            gameObject.name = objectName;
        }

        /// <summary>Make this tab the choice (or not): fill, outline, bar and glyph tint together.</summary>
        public void SetSelected(bool selected)
        {
            _button.SetSelectedChoice(selected);
            RefreshState();
        }

        public void SetInteractable(bool value)
        {
            _button.SetInteractable(value);
            RefreshState();
        }

        private void RefreshState()
        {
            bool chosen = _button.IsSelectedChoice;
            bool usable = _button.IsInteractable();
            if (_bar != null)
            {
                _bar.gameObject.SetActive(_tab && chosen);
            }

            if (_glyph != null)
            {
                UiColor tint = !usable || (_tab && !chosen) ? UiColor.TextSecondary : UiColor.TextPrimary;
                _glyph.color = UiTokens.Get(tint);
            }

            if (_material != null)
            {
                _material.SetIconLit(usable);   // disabled: the light goes out, the flat token stays (AP-028 render(): disabled = flat)
            }
        }

        private void Layout()
        {
            var rect = (RectTransform)_button.transform;
            var glyphRect = (RectTransform)_glyph.transform;
            if (_style == UiControlStyle.Brush && _tab)
            {
                LayoutBrushTab(rect, glyphRect);
            }
            else if (_labelMode == UiIconLabel.Below)
            {
                TMP_Text label = _button.LabelText;
                label.fontSize = UiTokens.FontSize(UiType.Label);   // the tab's word: Label 15 (bold, upper case), not the button's Body
                label.text = ShortLabel;
                float line = UiFactory.LineHeight(UiType.Label);
                float pad = (int)UiIconTokens.TabPadding;
                float width = Mathf.Max(UiIconTokens.TabMinWidth, UiIconTokens.Snap(label.GetPreferredValues(ShortLabel).x + 2f * pad));
                float height = UiIconTokens.Snap(pad + (int)_size + (int)UiIconTokens.TabLabelGap + line + pad);
                rect.sizeDelta = new Vector2(width, height);

                glyphRect.anchorMin = new Vector2(0.5f, 1f);
                glyphRect.anchorMax = new Vector2(0.5f, 1f);
                glyphRect.pivot = new Vector2(0.5f, 1f);
                glyphRect.anchoredPosition = new Vector2(0f, -pad);

                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.offsetMin = new Vector2(pad * 0.5f, pad);
                labelRect.offsetMax = new Vector2(-pad * 0.5f, pad + line);
                label.alignment = TextAlignmentOptions.Center;
            }
            else
            {
                float hit = UiIconTokens.Hit(_size);
                // WP-098A (C10): a Brush icon button keeps the kit's 72×64 through every relayout (RefreshText), not the V3 hit square.
                rect.sizeDelta = _style == UiControlStyle.Brush ? new Vector2(UiBrushTokens.IconButtonWidth, UiBrushTokens.ControlHeight) : new Vector2(hit, hit);
                glyphRect.anchorMin = new Vector2(0.5f, 0.5f);
                glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
                glyphRect.pivot = new Vector2(0.5f, 0.5f);
                glyphRect.anchoredPosition = Vector2.zero;
            }

            _glyph.transform.SetAsLastSibling();
        }

        /// <summary>
        /// WP-098A P0b · a C12 tab: 72×64 with the glyph centred (the word is the tooltip), or - with the word under the glyph
        /// (G2 interim) - at least 72 wide, as wide as the word plus a grid unit each side, as tall as glyph + word measured
        /// at the current text scale (U2: from the text, never from a character count).
        /// </summary>
        private void LayoutBrushTab(RectTransform rect, RectTransform glyphRect)
        {
            if (_labelMode != UiIconLabel.Below)
            {
                rect.sizeDelta = new Vector2(UiBrushTokens.TabWidth, UiBrushTokens.ControlHeight);
                glyphRect.anchorMin = new Vector2(0.5f, 0.5f);
                glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
                glyphRect.pivot = new Vector2(0.5f, 0.5f);
                glyphRect.anchoredPosition = Vector2.zero;
                _glyph.transform.SetAsLastSibling();
                return;
            }

            TMP_Text label = _button.LabelText;
            UiBrushKit.RestyleText(label, UiType.Label);   // the tab's word: Label 15 (KILAVUZ §4.1 C12)
            label.enableWordWrapping = false;
            label.text = ShortLabel;
            float line = UiFactory.LineHeight(UiType.Label);
            float pad = (int)UiSpace.S;
            float width = Mathf.Max(UiBrushTokens.TabWidth, UiIconTokens.Snap(label.GetPreferredValues(ShortLabel).x + 2f * pad));
            float height = Mathf.Max(UiBrushTokens.ControlHeight, UiIconTokens.Snap(pad + (int)_size + (int)UiIconTokens.TabLabelGap + line + pad));
            rect.sizeDelta = new Vector2(width, height);

            glyphRect.anchorMin = new Vector2(0.5f, 1f);
            glyphRect.anchorMax = new Vector2(0.5f, 1f);
            glyphRect.pivot = new Vector2(0.5f, 1f);
            glyphRect.anchoredPosition = new Vector2(0f, -pad);

            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.offsetMin = new Vector2(0f, pad);
            labelRect.offsetMax = new Vector2(0f, pad + line);
            label.alignment = TextAlignmentOptions.Center;
            _glyph.transform.SetAsLastSibling();
        }

        private void OnLift(bool lifted)
        {
            if (lifted)
            {
                UiTooltip.Show((RectTransform)transform, ShortLabel);
            }
            else
            {
                UiTooltip.Hide((RectTransform)transform);
            }
        }

        private void OnDisable()
        {
            UiTooltip.Hide((RectTransform)transform);
        }

        /// <summary>Tests: forget the missing keys recorded so far.</summary>
        public static void ResetMissingForTests()
        {
            s_missingIcons.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_missingIcons.Clear();
        }
    }

    /// <summary>WP-081 B2 · the icon control factories (menu v4).</summary>
    public static class UiIconControls
    {
        /// <summary>
        /// An icon button: the glyph in its square hit (<see cref="UiIconTokens.Hit"/>), the word in a tooltip on hover and
        /// focus. <paramref name="nameKey"/> is required: the reader name, the object's name and the tooltip's word
        /// (unless <paramref name="shortKey"/> gives a shorter one).
        /// </summary>
        public static UiIconButton Button(Transform parent, string iconKey, string nameKey, string nameFallback, Action onClick,
            UiIconSize size = UiIconSize.M, string shortKey = null, string shortFallback = null, UiIconLabel label = UiIconLabel.Tooltip,
            UiButtonVariant variant = UiButtonVariant.Icon)
        {
            // Icon: the glyph in a square plate. Ghost (a menu rail, O1): the bare glyph, the plate only as the hover wash.
            UiButton button = UiFactory.Button(UiStrings.Get(nameKey, nameFallback), parent, string.Empty, variant, onClick);
            UiIconButton icon = button.gameObject.AddComponent<UiIconButton>();
            icon.Bind(button, iconKey, nameKey, nameFallback, shortKey, shortFallback, label == UiIconLabel.Below ? UiIconLabel.Tooltip : label, size, false);
            return icon;
        }

        /// <summary>
        /// A tab: glyph over its short word, a Nav segment; <see cref="UiIconButton.SetSelected"/> marks the choice.
        /// WP-098A: <paramref name="style"/> Brush is the C12 tab (frameless, 40 px glyph, the word under it - the G2 interim;
        /// <see cref="UiBrushKit.Tabs"/> builds the strip and its one C8, and drops the word with <c>showWords: false</c>).
        /// </summary>
        public static UiIconButton Tab(Transform parent, string iconKey, string nameKey, string nameFallback, Action onClick,
            string shortKey = null, string shortFallback = null, UiControlStyle style = UiControlStyle.V3)
        {
            return Tab(parent, iconKey, nameKey, nameFallback, onClick, shortKey, shortFallback, style, UiIconLabel.Below);
        }

        /// <summary>WP-098A: a tab whose word is <paramref name="label"/> (Below, or Tooltip once the glyph speaks for itself - G2).</summary>
        internal static UiIconButton Tab(Transform parent, string iconKey, string nameKey, string nameFallback, Action onClick,
            string shortKey, string shortFallback, UiControlStyle style, UiIconLabel label)
        {
            bool brush = style == UiControlStyle.Brush;
            UiButton button = UiFactory.Button(UiStrings.Get(nameKey, nameFallback), parent, string.Empty, brush ? UiButtonVariant.Brush : UiButtonVariant.Nav, onClick);
            if (!brush)
            {
                button.SetSegment(true);
            }

            UiIconButton icon = button.gameObject.AddComponent<UiIconButton>();
            icon.Bind(button, iconKey, nameKey, nameFallback, shortKey, shortFallback, brush ? label : UiIconLabel.Below,
                brush ? UiIconSize.L : UiIconSize.M, true, style);
            return icon;
        }

        /// <summary>
        /// Lay tabs in a row from <paramref name="start"/> (the row's left edge, its centre line), a grid unit apart, and
        /// wire left / right navigation between them explicitly; the screen wires up / down. Returns the row's width.
        /// </summary>
        public static float Tabs(IList<UiIconButton> tabs, Vector2 start)
        {
            return Tabs(tabs, start, (int)UiTokens.TargetGap);
        }

        /// <summary>WP-098A: as <see cref="Tabs(IList{UiIconButton}, Vector2)"/>, with an explicit gap between tabs (Brush: TabStep − TabWidth = 16).</summary>
        public static float Tabs(IList<UiIconButton> tabs, Vector2 start, float gap)
        {
            float x = start.x;
            for (int i = 0; i < tabs.Count; i++)
            {
                var rect = (RectTransform)tabs[i].transform;
                float width = rect.sizeDelta.x;
                rect.anchoredPosition = new Vector2(x + width * 0.5f, start.y);
                x += width + gap;

                UnityEngine.UI.Navigation nav = tabs[i].Button.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                nav.selectOnLeft = i > 0 ? tabs[i - 1].Button : null;
                nav.selectOnRight = i < tabs.Count - 1 ? tabs[i + 1].Button : null;
                tabs[i].Button.navigation = nav;
            }

            return tabs.Count > 0 ? x - gap - start.x : 0f;
        }
    }
}
