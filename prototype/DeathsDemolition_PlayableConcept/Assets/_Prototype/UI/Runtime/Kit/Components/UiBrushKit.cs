using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>WP-098A · what <see cref="UiBrushKit.Title"/> built: the optional glyph, the Display title, the C5 stroke on its rule, the rule, and the band's height.</summary>
    public struct UiBrushTitle
    {
        public TMP_Text Text;
        public Image Icon;
        public UiBrushMark Stroke;
        public Image Rule;
        public float Height;
    }

    /// <summary>WP-098A · one C12 tab: its object name ("Tab.*", kept for the PM tests), its O1 glyph, reader name and short word.</summary>
    public struct UiBrushTab
    {
        public string ObjectName, IconKey, NameKey, NameFallback, ShortKey, ShortFallback;
    }

    /// <summary>WP-098A · C31 copy and glyphs. CancelKey null = a single-action notice (the load error); Destructive only words it - never colours it.</summary>
    public struct UiBrushConfirm
    {
        public string TitleKey, TitleFallback, BodyKey, BodyFallback;
        public string ConfirmKey, ConfirmFallback, ConfirmIcon, CancelKey, CancelFallback, CancelIcon;
        public bool Destructive;
    }

    /// <summary>
    /// WP-098A (KIT-API §2, KIT-KILAVUZU §4.5) · the brush kit's ONE entry point for screens: C1 / C2 / C37 pane, C5 title,
    /// C6 divider and hairline, C8 selection, C10 / C11 buttons, C17 segments, C31 confirm (this file, P0a) and C12 tabs,
    /// C13–C16 setting rows, C18 list rows, C21 counter, C27 tooltip, C35 scroll (<c>UiBrushKit.Controls.cs</c>, P0b).
    /// Build-time only: every control it returns is GC-free per frame (no Update; motion on UiMotion / UiFx).
    ///
    /// USAGE: a brush window = Pane → (EdgeTrace) → Title → rows → Open (KIT-KILAVUZU §5). Names always come from the
    /// caller (PM tests find "PaneCard", "Tab.*", "Setting.*" …); every label is a UiStrings key with an English fallback.
    /// DON'T: draw a brush trace in a screen file (UiInkChannel / InkMask / BrushRibbon - UiOrnamentCensusTests catches it),
    /// write an AP-028 sRGB alpha, put text on a Pane / Wallpaper scrim without the C2 wash, use UiFactory.Heading /
    /// HeadingTick in a brush window, or spend more than the window's trace budget (≤ 6).
    /// ACCESSIBILITY: text stands on combined ink ≥ <see cref="UiBrushTokens.TextGroundMin"/> (TextSecondary ≥ 4.5:1 on a
    /// white world in linear light); focus = the cream brackets on frame 1; a choice is shape + luminance, never hue alone;
    /// every control ≥ 48 px with a reader name; Reduced Motion lands every reveal at its end state.
    /// </summary>
    public static partial class UiBrushKit
    {
        /// <summary>C5 glyph, C10 / C11 glyph: the O1 sizes on the 8 px grid.</summary>
        public const float TitleGlyph = 48f;
        public const float ControlGlyph = 40f;

        // ============================================================ C1 / C2 / C37 · panel frame (P0a)

        /// <summary>
        /// A frameless pane: C1 on <paramref name="screenRoot"/> (full screen, just under the pane's top-most ancestor there),
        /// the pane rect (named <paramref name="name"/>) filling <paramref name="host"/>, optional C2 under its clipped Content;
        /// closed until <see cref="UiBrushPane.Open"/>. A Pane / Wallpaper scrim without the wash logs a warning once.
        /// </summary>
        public static UiBrushPane Pane(RectTransform screenRoot, RectTransform host, string name, UiBrushScrim scrim, UiBrushWash wash = UiBrushWash.Column)
        {
            return UiBrushPane.Build(screenRoot, host, name, scrim, wash);
        }

        /// <summary>A second C2 under another text column of the same pane (≤ 2 per window); inks in with the pane. Lay the column out first.</summary>
        public static Image Wash(UiBrushPane pane, RectTransform column)
        {
            return pane != null ? pane.AddWash(pane.Root, column) : null;
        }

        /// <summary>
        /// C37 · the pane's one frame edge: the C6 dry stroke turned upright (−90°, so it inks top → bottom),
        /// <see cref="UiBrushTokens.EdgeWidth"/> wide, <see cref="UiBrushTokens.EdgeOffset"/> left of the content column, as tall
        /// as the pane (re-fitted on every Open). ≤ 1 per window.
        /// </summary>
        public static UiBrushMark EdgeTrace(UiBrushPane pane)
        {
            if (pane == null)
            {
                return null;
            }

            UiBrushMark edge = UiBrushMark.Create(UiBrushMarkKind.Edge, pane.Root, "EdgeTrace");
            RectTransform rect = edge.Rect;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, -90f);
            rect.anchoredPosition = new Vector2(-(UiBrushTokens.EdgeOffset + UiBrushTokens.EdgeWidth * 0.5f), 0f);
            if (pane.Content != null)
            {
                rect.SetSiblingIndex(pane.Content.GetSiblingIndex());   // outside the content's clip
            }

            pane.SetEdge(edge);
            return edge;
        }

        // ============================================================ C5 / C6 / C8 · heading, rules, selection (P0a)

        /// <summary>
        /// C5 · optional 48 px glyph + Display title + the 1 px header rule (16 under the title band, the parent's width) with
        /// the short title brush on it (title width + 48, 160–600, from the title's x), at the top of <paramref name="parent"/>
        /// in a block named "Title"; the stroke registers with the parent pane (it inks in on Open). Advance your layout by
        /// <see cref="UiBrushTitle.Height"/>.
        /// </summary>
        public static UiBrushTitle Title(RectTransform parent, string titleKey, string titleFallback, string iconKey = null)
        {
            return TitleText(parent, UiStrings.Get(titleKey, titleFallback), iconKey);
        }

        /// <summary>As <see cref="Title"/>, with the words already resolved (UiModal's brush confirm).</summary>
        internal static UiBrushTitle TitleText(RectTransform parent, string text, string iconKey)
        {
            var title = new UiBrushTitle();
            float line = UiFactory.LineHeight(UiType.Display);
            bool glyph = !string.IsNullOrEmpty(iconKey) && UiArt.HasIcon(iconKey);
            float band = Mathf.Max(line, glyph ? TitleGlyph : 0f);
            title.Height = band + UiBrushTokens.TitleRuleGap + UiBrushTokens.TitleStrokeHeight;

            RectTransform block = UiFactory.NewRect("Title", parent);
            block.anchorMin = new Vector2(0f, 1f);
            block.anchorMax = new Vector2(1f, 1f);
            block.pivot = new Vector2(0.5f, 1f);
            block.sizeDelta = new Vector2(0f, title.Height);
            block.anchoredPosition = Vector2.zero;

            float x = 0f;
            if (glyph)
            {
                title.Icon = UiArt.IconImage("Icon", block, iconKey, UiColor.TextPrimary, TitleGlyph);
                RectTransform icon = title.Icon.rectTransform;
                icon.anchorMin = new Vector2(0f, 1f);
                icon.anchorMax = new Vector2(0f, 1f);
                icon.pivot = new Vector2(0f, 0.5f);
                icon.anchoredPosition = new Vector2(0f, -band * 0.5f);
                x = TitleGlyph + (int)UiSpace.L;
            }

            title.Text = UiFactory.Label("Text", block, text, UiType.Display, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            title.Text.enableWordWrapping = false;
            RectTransform words = title.Text.rectTransform;
            words.anchorMin = new Vector2(0f, 1f);
            words.anchorMax = new Vector2(1f, 1f);
            words.pivot = new Vector2(0f, 1f);
            words.offsetMin = new Vector2(x, -band);
            words.offsetMax = new Vector2(0f, 0f);

            // The header rule: 1 px, the line tone, across the parent (decoration outside the trace budget).
            float ruleY = band + UiBrushTokens.TitleRuleGap;
            title.Rule = UiFactory.NewImage("Rule", block, UiColor.TextMuted, UiSprites.Solid);
            title.Rule.color = UiBrushTokens.LineColour();
            RectTransform rule = title.Rule.rectTransform;
            rule.anchorMin = new Vector2(0f, 1f);
            rule.anchorMax = new Vector2(1f, 1f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.offsetMin = new Vector2(0f, -(ruleY + UiTokens.StrokeHairline));
            rule.offsetMax = new Vector2(0f, -ruleY);

            // The short dry stroke on it: as long as the title + 48 (160–600), from the title's x - never wider than the parent.
            float measured = string.IsNullOrEmpty(text) ? 0f : title.Text.GetPreferredValues(text).x;
            float length = Mathf.Clamp(measured + (int)UiSpace.XXXL, UiBrushTokens.TitleStrokeMin, UiBrushTokens.TitleStrokeMax);
            float room = parent != null ? parent.rect.width - x : 0f;
            if (room > 1f)
            {
                length = Mathf.Min(length, room);
            }

            title.Stroke = UiBrushMark.Create(UiBrushMarkKind.TitleStroke, block, "TitleStroke");
            RectTransform stroke = title.Stroke.Rect;
            stroke.anchorMin = new Vector2(0f, 1f);
            stroke.anchorMax = new Vector2(0f, 1f);
            stroke.pivot = new Vector2(0f, 0.5f);
            stroke.sizeDelta = new Vector2(length, UiBrushTokens.TitleStrokeHeight);
            stroke.anchoredPosition = new Vector2(x, -(ruleY + UiTokens.StrokeHairline * 0.5f));

            TrackInPane(parent, title.Stroke);
            return title;
        }

        /// <summary>C6 · a dry-brush section divider <paramref name="width"/> long (≤ 2 per window), top-left at the parent's top-left; place it with UiFactory.Place on <see cref="UiBrushMark.Rect"/>.</summary>
        public static UiBrushMark Divider(RectTransform parent, float width)
        {
            UiBrushMark divider = UiBrushMark.Create(UiBrushMarkKind.Divider, parent, "Divider");
            divider.Rect.sizeDelta = new Vector2(Mathf.Max(1f, width), UiBrushTokens.EdgeWidth);
            TrackInPane(parent, divider);
            return divider;
        }

        /// <summary>C6 · the plain 1 px row rule in the line colour (no brush, no budget), top-left at the parent's top-left.</summary>
        public static Image Hairline(RectTransform parent, float width)
        {
            Image line = UiFactory.NewImage("Hairline", parent, UiColor.TextMuted, UiSprites.Solid);
            line.color = UiBrushTokens.LineColour();
            RectTransform rect = line.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width), UiTokens.StrokeHairline);
            rect.anchoredPosition = Vector2.zero;
            return line;
        }

        /// <summary>C8 · a free selection mark for a custom group (the menu rail); hidden until <see cref="UiBrushMark.Under"/> (counts as one selection).</summary>
        public static UiBrushMark SelectionMark(RectTransform parent)
        {
            UiBrushMark mark = UiBrushMark.Create(UiBrushMarkKind.Selection, parent, "SelectionMark");
            mark.Hide(false);
            return mark;
        }

        private static void TrackInPane(RectTransform parent, UiBrushMark mark)
        {
            UiBrushPane pane = parent != null ? parent.GetComponentInParent<UiBrushPane>(true) : null;
            if (pane != null)
            {
                pane.Track(mark);
            }
        }

        // ============================================================ C10 / C11 / C17 · buttons (P0a)

        /// <summary>C11 · an unfilled labelled button: 40 px glyph + MenuBody word, 64 high, as wide as its word (≥ 232).</summary>
        public static UiButton Button(RectTransform parent, string name, string labelKey, string labelFallback, string iconKey, Action onClick)
        {
            return BrushButton(parent, name, UiStrings.Get(labelKey, labelFallback), iconKey, onClick);
        }

        /// <summary>C11 with the word already resolved (UiModal's brush actions).</summary>
        internal static UiButton BrushButton(Transform parent, string name, string label, string iconKey, Action onClick)
        {
            UiButton button = UiFactory.Button(name, parent, label, UiButtonVariant.Brush, onClick);
            SetLeadingGlyph(button, iconKey);
            FitWidth(button);
            return button;
        }

        /// <summary>A C11's 40 px leading glyph (once; a missing key leaves the word alone): tinted with the word by the state code.</summary>
        internal static void SetLeadingGlyph(UiButton button, string iconKey)
        {
            if (button == null || button.BrushGlyph != null || string.IsNullOrEmpty(iconKey) || !UiArt.HasIcon(iconKey))
            {
                return;
            }

            Image glyph = UiArt.IconImage("Glyph", button.transform, iconKey, UiColor.TextPrimary, ControlGlyph);
            RectTransform rect = glyph.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2((int)UiSpace.L, 0f);

            TMP_Text label = button.LabelText;
            if (label != null)
            {
                RectTransform words = label.rectTransform;
                words.offsetMin = new Vector2((int)UiSpace.L + ControlGlyph + (int)UiSpace.M, words.offsetMin.y);
                label.alignment = TextAlignmentOptions.Left;
                label.transform.SetAsLastSibling();
            }

            button.BrushGlyph = glyph;
        }

        /// <summary>A C11's width from its word as drawn (U2: measured, never counted): lead + word + 16, on the 8 px grid, ≥ 232.</summary>
        internal static float FitWidth(UiButton button)
        {
            var rect = (RectTransform)button.transform;
            TMP_Text label = button.LabelText;
            float word = label != null && !string.IsNullOrEmpty(label.text) ? label.GetPreferredValues(label.text).x : 0f;
            float lead = button.BrushGlyph != null ? (int)UiSpace.L + ControlGlyph + (int)UiSpace.M : (int)UiSpace.L;
            float width = Mathf.Max(UiBrushTokens.LabelButtonMin, UiIconTokens.Snap(lead + word + (int)UiSpace.L));
            rect.sizeDelta = new Vector2(width, UiBrushTokens.ControlHeight);
            return width;
        }

        /// <summary>C10 · an unfilled 72×64 icon button (40 px O1 glyph, lit); its word is the tooltip and the reader name.</summary>
        public static UiIconButton IconButton(RectTransform parent, string name, string iconKey, string nameKey, string nameFallback, Action onClick)
        {
            UiButton button = UiFactory.Button(UiStrings.Get(nameKey, nameFallback), parent, string.Empty, UiButtonVariant.Brush, onClick);
            UiIconButton icon = button.gameObject.AddComponent<UiIconButton>();
            icon.Bind(button, iconKey, nameKey, nameFallback, null, null, UiIconLabel.Tooltip, UiIconSize.L, false, UiControlStyle.Brush);
            if (!string.IsNullOrEmpty(name))
            {
                icon.PinObjectName(name);
            }

            ((RectTransform)button.transform).sizeDelta = new Vector2(UiBrushTokens.IconButtonWidth, UiBrushTokens.ControlHeight);
            return icon;
        }

        /// <summary>C10 · the footer Back / close ("Back", Menu.Back) with the pad's Cancel glyph beside it; Cancel does the same (the screen calls <paramref name="onBack"/>).</summary>
        public static UiIconButton Back(RectTransform parent, Action onBack)
        {
            UiIconButton back = IconButton(parent, "Back", "Menu.Back", "selection.back", "BACK", onBack);
            UiPrompt prompt = UiInputGlyphs.Build("Prompt", back.transform, UiPromptAction.Cancel, UiColor.TextPrimary, UiColor.SurfaceInset);
            if (prompt != null)
            {
                var rect = (RectTransform)prompt.transform;
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2((int)UiSpace.S, 0f);
            }

            return back;
        }

        /// <summary>C17 · labelled segments (304 wide, 16 apart) with one C8 under the choice; the row's footprint includes the C8 under it.</summary>
        public static UiBrushChoiceGroup Segments(RectTransform parent, string name, string[] objectNames, string[] labelKeys,
            string[] labelFallbacks, int selected, Action<int> onSelect, float segmentWidth = UiBrushTokens.SegmentWidth)
        {
            int count = objectNames != null ? objectNames.Length : 0;
            RectTransform root = UiFactory.NewRect(name, parent);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            float gap = UiBrushTokens.TabStep - UiBrushTokens.TabWidth;
            root.sizeDelta = new Vector2(Mathf.Max(0f, count * segmentWidth + (count - 1) * gap),
                UiBrushTokens.ControlHeight + UiBrushTokens.SelectionGap + UiBrushTokens.SelectionCore + UiBrushTokens.SelectionBed);

            var items = new UiButton[count];
            for (int i = 0; i < count; i++)
            {
                string key = labelKeys != null && i < labelKeys.Length ? labelKeys[i] : null;
                string fallback = labelFallbacks != null && i < labelFallbacks.Length ? labelFallbacks[i] : objectNames[i];
                UiButton item = UiFactory.Button(objectNames[i], root, UiStrings.Get(key, fallback), UiButtonVariant.Brush, null);
                var rect = (RectTransform)item.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(segmentWidth, UiBrushTokens.ControlHeight);
                rect.anchoredPosition = new Vector2(i * (segmentWidth + gap), 0f);
                items[i] = item;
            }

            return UiBrushChoiceGroup.Build(root, items, null, selected, onSelect);
        }

        // ============================================================ C31 · confirm (P0a)

        /// <summary>
        /// C31 · frameless confirm on <paramref name="screenRoot"/>: the Confirm ink (0.985), title + rule + stroke, MenuBody body
        /// (1088 measure), two C11 - the safe action first and focused, Cancel = the safe action, focus back to the opener on
        /// close. <see cref="UiBrushConfirm.CancelKey"/> null = one action (a notice).
        /// </summary>
        public static UiModal Confirm(string name, RectTransform screenRoot, UiBrushConfirm spec, Action onConfirm, Action onCancel)
        {
            string cancel = spec.CancelKey == null ? null : UiStrings.Get(spec.CancelKey, spec.CancelFallback);
            UiModal modal = UiModal.Confirm(name, screenRoot, UiStrings.Get(spec.TitleKey, spec.TitleFallback), UiStrings.Get(spec.BodyKey, spec.BodyFallback),
                UiStrings.Get(spec.ConfirmKey, spec.ConfirmFallback), onConfirm, cancel, onCancel, spec.Destructive, UiControlStyle.Brush);
            modal.SetActionIcons(spec.ConfirmIcon, spec.CancelIcon);
            return modal;
        }
    }
}
