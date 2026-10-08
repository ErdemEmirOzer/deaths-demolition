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
    /// WP-098A (S-09 07.10 §8 S1, G2 closed 08.10 with AP-034 v002.1's Menu.Display): <see cref="BelowChosen"/> - the word
    /// under the chosen tab only; every tab keeps the word's room (the strip never shifts when the choice moves) and the
    /// others say it as the tooltip on lift.
    /// </summary>
    public enum UiIconLabel
    {
        Tooltip = 0,
        Below = 1,
        Beside = 2,
        BelowChosen = 3
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
    /// lit, then inked within the AD's ceiling - never under token × (1 − (0.06 + 0.06 s)), WP-098A row d (UiIconMaterialTests
    /// T1) - and a disabled control shows the flat token.
    ///
    /// WP-098A P0b (C-00 07.10) · C12 Brush tab (<see cref="UiControlStyle.Brush"/>, KILAVUZ §4.1 C12, KIT-API §4 "Choice
    /// group"): a frameless <see cref="UiButtonVariant.Brush"/> control - no Nav segment fill, no ChosenBar; a 40 px glyph,
    /// TextSecondary until it is the choice (TextPrimary), the choice's C8 drawn by its <see cref="UiBrushChoiceGroup"/>.
    /// <see cref="UiBrushTokens.TabWidth"/>×<see cref="UiBrushTokens.ControlHeight"/> with the word as the tooltip; with
    /// the word under the glyph (<c>showWords</c>: since G2 closed, S-09 §8 S1, only under the chosen tab -
    /// <see cref="UiIconLabel.BelowChosen"/>) as wide and as tall as the word needs.
    ///
    /// WP-098A (C-00 08.10; DEC-114; HAREKET-MENU §3, M7 / M9 / M18) · the icon states move: lift, press (pointer or a Submit
    /// pulse), choose, disable, settle and the one idle sheen pass run as tracks on <see cref="UiIconMotion"/>, driven by the
    /// button's state events (no Update); the glyph's scale and offset ride its RectTransform about its rest, the light (sheen,
    /// glow, stroke front, the lob's lift) its Icon material. <see cref="PlayNotify"/> is the notification pass. Bind-frame
    /// changes land at once; Reduced Motion lands every track (the kit's brackets and wash carry the state).
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

        // WP-098A · the motion state (UiIconMotion): the pose last written, the glyph's laid-out rest, the bind time.
        private UiIconPose _shown = UiIconPose.Rest;
        private Vector2 _glyphRest;
        private float _boundAt = float.NaN;

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

        /// <summary>WP-098A · the word stands under the glyph right now (Below always; BelowChosen while this tab is the choice).</summary>
        public bool WordVisible
        {
            get { return WordsBelow && _button != null && _button.LabelText != null && _button.LabelText.enabled; }
        }

        /// <summary>The layout keeps a word under the glyph (Below, or BelowChosen whether or not it shows).</summary>
        private bool WordsBelow
        {
            get { return _labelMode == UiIconLabel.Below || _labelMode == UiIconLabel.BelowChosen; }
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

            if (labelMode == UiIconLabel.Tooltip || labelMode == UiIconLabel.BelowChosen)
            {
                button.LiftChanged += OnLift;
            }

            // WP-098A: the state tracks (UiIconMotion). Handlers are bound once here; the events fire at state changes only.
            _boundAt = UiMotion.Now(UiClock.Unscaled);
            button.IconDriven = true;
            button.LiftChanged += OnLiftMotion;
            button.PressChanged += OnPressMotion;
            button.ChoiceChanged += OnChoiceMotion;
            button.InteractableChanged += OnInteractableMotion;

            Layout();
            RefreshState();
            ApplyPose(RestPose(), true);
        }

        // ================================================================== WP-098A · motion (UiIconMotion)

        /// <summary>The pose on screen now (the tracks start from it).</summary>
        public UiIconPose ShownPose
        {
            get { return _shown; }
        }

        /// <summary>Hovered or focused (the idle candidate's condition).</summary>
        public bool Lifted
        {
            get { return _button != null && _button.IsLifted; }
        }

        /// <summary>A Submit-pressed choice waits for the press's contact (UiIconMotion).</summary>
        internal bool PendingChoose { get; set; }

        /// <summary>True when <paramref name="now"/> is this control's bind time: its changes then land at once (no track on screen open).</summary>
        internal bool BoundAt(float now)
        {
            return now == _boundAt;
        }

        /// <summary>
        /// The composite rest (HAREKET-MENU §3, council §1.3): Reduced Motion or disabled → Rest; lifted → Lifted (whether or not
        /// chosen); chosen → Chosen; else Rest.
        /// </summary>
        public UiIconPose RestPose()
        {
            if (UiMotion.ReducedMotion || _button == null || !_button.IsInteractable())
            {
                return UiIconPose.Rest;
            }

            if (_button.IsLifted)
            {
                return UiIconPose.Lifted;
            }

            return _button.IsSelectedChoice ? UiIconPose.Chosen : UiIconPose.Rest;
        }

        /// <summary>
        /// Write <paramref name="pose"/>: the glyph's scale and offset about its laid-out rest (AP-028 px24 units, + down: Unity's y
        /// is − that × the glyph's px24 size), the Icon channels with the lit flag as the state set it. A step below 1/512 is
        /// skipped mid-track; <paramref name="exact"/> (a track's end, a landing) always writes the pose itself, so every rest is
        /// exact - never left a sub-threshold step short of its target (WP-098A r3-w4).
        /// </summary>
        internal void ApplyPose(UiIconPose pose, bool exact = false)
        {
            if (_glyph == null)
            {
                return;
            }

            if (exact ? pose.Same(_shown) : pose.Near(_shown, 1f / 512f))
            {
                return;
            }

            _shown = pose;
            var rect = (RectTransform)_glyph.transform;
            float px24 = (int)_size * (64f / 52f) / 24f;
            rect.localScale = new Vector3(pose.Scale, pose.Scale, 1f);
            rect.anchoredPosition = _glyphRest + new Vector2(0f, -pose.OffsetY * px24);
            if (_material != null)
            {
                _material.SetIcon(new Vector3(pose.Sheen, pose.Glow, 0f), new Vector3(pose.Draw, _material.Parameters.y, 1f), pose.Lift);
            }
        }

        /// <summary>A notification's pass (DEC-114 / §3 notify, 600 ms): the sheen once and a 1.05 nudge.</summary>
        public void PlayNotify()
        {
            UiIconMotion.Play(this, UiIconTrack.Notify);
        }

        private void OnLiftMotion(bool lifted)
        {
            if (lifted)
            {
                UiIconMotion.Focused(this);
                UiIconMotion.Play(this, UiIconTrack.Lift);
            }
            else
            {
                UiIconMotion.Unfocused(this);
                UiIconMotion.Play(this, UiIconTrack.Settle);
            }
        }

        private void OnPressMotion(bool pressed)
        {
            if (pressed)
            {
                UiIconMotion.Play(this, UiIconTrack.Press);
            }
        }

        private void OnChoiceMotion(bool chosen)
        {
            UiIconMotion.Play(this, chosen ? UiIconTrack.Choose : UiIconTrack.Settle);
        }

        private void OnInteractableMotion(bool usable)
        {
            UiIconMotion.Play(this, usable ? UiIconTrack.Settle : UiIconTrack.Disable);
        }

        /// <summary>Re-read the words (a language change rebuilds screens; a pane that stays can call this).</summary>
        public void RefreshText()
        {
            gameObject.name = _objectName ?? AccessibleName;
            if (WordsBelow && _button.LabelText != null)
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

            if (_labelMode == UiIconLabel.BelowChosen && _button.LabelText != null)
            {
                // G2 (S-09 §8 S1): the chosen tab says its word under the glyph; the others keep the room and use the tooltip.
                bool word = _tab && chosen;
                _button.LabelText.enabled = word;
                if (word)
                {
                    UiTooltip.Hide((RectTransform)transform);
                }
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
            else if (WordsBelow)
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
            KeepGlyphRest();
        }

        /// <summary>WP-098A: a relayout moves the glyph's rest; the pose on screen keeps its offset about the new one.</summary>
        private void KeepGlyphRest()
        {
            var glyphRect = (RectTransform)_glyph.transform;
            _glyphRest = glyphRect.anchoredPosition;
            if (_shown.OffsetY != 0f)
            {
                glyphRect.anchoredPosition = _glyphRest + new Vector2(0f, -_shown.OffsetY * ((int)_size * (64f / 52f) / 24f));
            }
        }

        /// <summary>
        /// WP-098A P0b · a C12 tab: 72×64 with the glyph centred (the word is the tooltip), or - with the word under the glyph
        /// (Below; BelowChosen keeps the room on every tab, the word shows under the chosen one) - at least 72 wide, as wide as the word plus a grid unit each side, as tall as glyph + word measured
        /// at the current text scale (U2: from the text, never from a character count).
        /// </summary>
        private void LayoutBrushTab(RectTransform rect, RectTransform glyphRect)
        {
            if (!WordsBelow)
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
            if (lifted && !WordVisible)   // BelowChosen: the chosen tab's word is already under it
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
            UiIconMotion.Forget(this);   // WP-098A: no run, no idle candidate on a hidden control
        }

        private void OnEnable()
        {
            if (_glyph != null)
            {
                ApplyPose(RestPose(), true);   // WP-098A: a control disabled mid-track comes back at its rest
            }
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
            bool below = label == UiIconLabel.Below || label == UiIconLabel.BelowChosen;   // a button has no word under it
            icon.Bind(button, iconKey, nameKey, nameFallback, shortKey, shortFallback, below ? UiIconLabel.Tooltip : label, size, false);
            return icon;
        }

        /// <summary>
        /// A tab: glyph over its short word, a Nav segment; <see cref="UiIconButton.SetSelected"/> marks the choice.
        /// WP-098A: <paramref name="style"/> Brush is the C12 tab (frameless, 40 px glyph, the word under it;
        /// <see cref="UiBrushKit.Tabs"/> builds the strip and its one C8, and drops the word with <c>showWords: false</c>).
        /// </summary>
        public static UiIconButton Tab(Transform parent, string iconKey, string nameKey, string nameFallback, Action onClick,
            string shortKey = null, string shortFallback = null, UiControlStyle style = UiControlStyle.V3)
        {
            return Tab(parent, iconKey, nameKey, nameFallback, onClick, shortKey, shortFallback, style, UiIconLabel.Below);
        }

        /// <summary>WP-098A: a Brush tab whose word is <paramref name="label"/> (Below; BelowChosen - G2 closed: the chosen tab only; Tooltip).</summary>
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
