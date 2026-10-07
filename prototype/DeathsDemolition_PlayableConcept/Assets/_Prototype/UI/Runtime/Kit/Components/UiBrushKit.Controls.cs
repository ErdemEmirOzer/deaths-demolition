using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A P0b (C-00 07.10, KIT-API §2 "C12 · tabs" … "C35", KIT-KILAVUZU §4) · the brush kit's control entry points: the
    /// C12 tab strip, the C13–C16 setting rows (slider, toggle, value cycle), the C18 list row, the C21 big counter, the C27
    /// tooltip and the C35 scroller. Each wraps the v3 component in its Brush style (<see cref="UiControlStyle.Brush"/>), so a
    /// screen that asks for nothing new keeps today's look. Build-time only: names, strings and closures are made here once;
    /// every control returned is GC-free per frame and has no Update.
    ///
    /// Rows share one geometry: <see cref="UiBrushTokens.ListWidth"/> wide by default, the label column on the left, the
    /// value column (<see cref="UiBrushTokens.ValueColumn"/>) on the right, <see cref="ValueColumnGap"/> between them; a row
    /// is <see cref="UiBrushTokens.RowHeight"/> high - measured from MenuBody, so it grows at TextScale 1.25 (T3). The rule
    /// between rows is the screen's (<see cref="Hairline"/>, C6): a row draws no line of its own.
    ///
    /// USAGE: <c>UiBrushKit.Slider(pane.Content, "Setting.CAMERA SHAKE", "pause.shake", "CAMERA SHAKE INTENSITY", 0f, 2f, v,
    /// 0.1f, FormatShake, SetShake)</c>; lay rows with UiStack / UiFactory.Place at <see cref="UiBrushTokens.RowStep"/>.
    /// Tabs: <c>UiBrushKit.Tabs(pane.Content, "Tabs", tabs, current, OpenTab, showWords: true)</c> (G2 interim, S-09 §8 S1).
    /// DON'T: invent object names here (they come from the caller: "Tab.*", "Setting.*"); colour a choice gold (StateGold is
    /// money only - the Currency counter); put a sentence in a tooltip; use Cycle for more than six values (P2: the washed
    /// dropdown); draw a brush trace in a screen file.
    /// ACCESSIBILITY: every row is one ≥ 48 px focus target carrying C7 (the cream brackets); text stands on the window ink
    /// (≥ 0.945 combined, KILAVUZ §4.4) in TextPrimary / TextSecondary - never TextMuted; no state rests on colour alone (the
    /// pill's shape and word, the C8 under a tab, the check glyph of a list choice); a disabled row stays readable and says
    /// why in its tooltip (<see cref="Tooltip"/> opens on pointer and pad focus alike, disabled or not).
    /// </summary>
    public static partial class UiBrushKit
    {
        /// <summary>WP-098A P0b · the air between a row's label column and its value column (ListWidth − LabelColumn − ValueColumn = 48).</summary>
        internal const int ValueColumnGap = UiBrushTokens.ListWidth - UiBrushTokens.LabelColumn - UiBrushTokens.ValueColumn;

        // C12 · tabs ---------------------------------------------------------------------------------------------- P0b

        /// <summary>
        /// C12 · icon tab strip (72×64, step 88) with one C8 under the chosen tab; ←/→ wired, Submit selects (no select-on-focus).
        /// The strip's root is <paramref name="name"/>, as wide as its tabs, its left edge at its own x (pivot 0.5 - place it
        /// with UiFactory.Place); each tab keeps <see cref="UiBrushTab.ObjectName"/>. <paramref name="showWords"/>: the word under
        /// each glyph (G2 interim until the Menu.Display glyph lands); false: the word is the tooltip.
        /// </summary>
        public static UiBrushChoiceGroup Tabs(RectTransform parent, string name, UiBrushTab[] tabs, int selected, Action<int> onSelect, bool showWords = false)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            int count = tabs != null ? tabs.Length : 0;
            var items = new UiButton[count];
            var icons = new UiIconButton[count];
            float height = 0f;
            for (int i = 0; i < count; i++)
            {
                UiBrushTab spec = tabs[i];
                UiIconButton tab = UiIconControls.Tab(root, spec.IconKey, spec.NameKey, spec.NameFallback, null, spec.ShortKey, spec.ShortFallback,
                    UiControlStyle.Brush, showWords ? UiIconLabel.Below : UiIconLabel.Tooltip);
                if (!string.IsNullOrEmpty(spec.ObjectName))
                {
                    tab.PinObjectName(spec.ObjectName);
                }

                var rect = (RectTransform)tab.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);   // laid from the strip's left edge (UiIconControls.Tabs)
                rect.anchorMax = new Vector2(0f, 0.5f);
                height = Mathf.Max(height, rect.sizeDelta.y);
                icons[i] = tab;
                items[i] = tab.Button;
            }

            float width = UiIconControls.Tabs(icons, Vector2.zero, UiBrushTokens.TabStep - UiBrushTokens.TabWidth);
            root.sizeDelta = new Vector2(width, height);
            return UiBrushChoiceGroup.Build(root, items, icons, selected, onSelect);
        }

        // C13–C16 · setting rows ---------------------------------------------------------------------------------- P0b

        /// <summary>C14 · slider row (label | 1 px rail · 3 px cream fill · 12 px head | value): UiSlider in Brush style.</summary>
        public static UiSlider Slider(RectTransform parent, string name, string labelKey, string labelFallback, float min, float max,
            float value, float step, Func<float, string> format, Action<float> changed, float width = UiBrushTokens.ListWidth)
        {
            return UiSlider.Create(name, parent, UiStrings.Get(labelKey, labelFallback), min, max, value, step, format, changed,
                width, LabelColumn(width), UiControlStyle.Brush);
        }

        /// <summary>C15 · ON/OFF row; a 40×8 brush pill (filled ON / hollow OFF) and the word carry the state: UiSwitch in Brush style.</summary>
        public static UiSwitch Toggle(RectTransform parent, string name, string labelKey, string labelFallback, bool value,
            Action<bool> changed, float width = UiBrushTokens.ListWidth)
        {
            UiSwitch toggle = UiSwitch.Create(name, parent, UiStrings.Get(labelKey, labelFallback), value, changed, UiControlStyle.Brush);
            SetRowWidth(toggle.transform, width);
            return toggle;
        }

        /// <summary>C16 · "◂ value ▸" for 2–6 values (the kit's dropdown): UiStepper in Brush style.</summary>
        public static UiStepper Cycle(RectTransform parent, string name, string labelKey, string labelFallback, string[] values, int index,
            bool wrap, Action<int> changed, float width = UiBrushTokens.ListWidth)
        {
            UiStepper cycle = UiStepper.Create(name, parent, UiStrings.Get(labelKey, labelFallback), values, index, wrap, changed, UiControlStyle.Brush);
            SetRowWidth(cycle.transform, width);
            return cycle;
        }

        // C18 / C21 / C27 / C35 ------------------------------------------------------------------------------------- P0b

        /// <summary>
        /// C18 · unframed list row: label, trailing 24/32 status glyph and small text; the choice is the check glyph.
        /// <paramref name="compact"/>: the C18 metrics (hit 48, 24 px glyph) - false: a C13-height row (hit 56, 32 px glyph).
        /// </summary>
        public static UiButton ListRow(RectTransform parent, string name, string label, string statusIconKey, Action onClick, bool compact = true)
        {
            UiButton row = UiFactory.Button(name, parent, label, UiButtonVariant.Brush, onClick);
            var rect = (RectTransform)row.transform;
            rect.sizeDelta = new Vector2(UiBrushTokens.ListWidth,
                UiBrushTokens.RowHeight(UiType.MenuBody, compact ? UiBrushTokens.RowHitCompact : UiBrushTokens.RowHit));

            TMP_Text text = row.LabelText;
            RestyleText(text, UiType.MenuBody);
            text.alignment = TextAlignmentOptions.Left;
            text.enableWordWrapping = false;
            RectTransform labelRect = text.rectTransform;
            labelRect.offsetMin = new Vector2(0f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(0f, labelRect.offsetMax.y);

            float glyph = (int)(compact ? UiIconSize.S : UiIconSize.M);
            Image status = UiFactory.NewImage("Status", row.transform, UiColor.TextPrimary, null);
            status.preserveAspect = true;
            status.enabled = false;
            var statusRect = (RectTransform)status.transform;
            statusRect.anchorMin = new Vector2(1f, 0.5f);
            statusRect.anchorMax = new Vector2(1f, 0.5f);
            statusRect.pivot = new Vector2(1f, 0.5f);
            statusRect.sizeDelta = new Vector2(glyph, glyph);
            statusRect.anchoredPosition = Vector2.zero;

            TMP_Text trailing = UiFactory.Label("Trailing", row.transform, string.Empty, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleRight);
            trailing.enableWordWrapping = false;
            RectTransform trailingRect = trailing.rectTransform;
            trailingRect.anchorMin = new Vector2(1f, 0f);
            trailingRect.anchorMax = new Vector2(1f, 1f);
            trailingRect.pivot = new Vector2(1f, 0.5f);

            RowParts parts = row.gameObject.AddComponent<RowParts>();
            parts.Status = status;
            parts.Trailing = trailing;
            parts.Glyph = glyph;
            text.transform.SetAsLastSibling();
            SetRowStatus(row, statusIconKey, null);
            return row;
        }

        /// <summary>
        /// C18 · swap a row's status glyph and trailing text (event-time only; null / empty hides either). The glyph follows the
        /// row's state as it is now - TextPrimary, or TextSecondary while disabled: call it after SetInteractable.
        /// </summary>
        public static void SetRowStatus(UiButton row, string statusIconKey, string trailing)
        {
            RowParts parts = row != null ? row.GetComponent<RowParts>() : null;
            if (parts == null)
            {
                return;   // not a ListRow
            }

            Sprite sprite = string.IsNullOrEmpty(statusIconKey) ? null : UiArt.Icon(statusIconKey);
            if (sprite != null)
            {
                UiArt.SetGlyph(parts.Status, sprite);
            }

            parts.Status.enabled = sprite != null;
            Color tint = UiTokens.Get(row.IsInteractable() ? UiColor.TextPrimary : UiColor.TextSecondary);
            if (parts.Status.color != tint) parts.Status.color = tint;

            string words = trailing ?? string.Empty;
            parts.Trailing.text = words;
            float gap = (int)UiSpace.M;
            float glyphSpace = sprite != null ? parts.Glyph + gap : 0f;
            float wordsWidth = words.Length > 0 ? Mathf.Ceil(parts.Trailing.GetPreferredValues(words).x) : 0f;
            RectTransform trailingRect = parts.Trailing.rectTransform;
            trailingRect.sizeDelta = new Vector2(wordsWidth, 0f);
            trailingRect.anchoredPosition = new Vector2(-glyphSpace, 0f);

            // The label runs up to the trailing block (measured, not guessed: U2).
            float reserved = glyphSpace + wordsWidth;
            RectTransform labelRect = row.LabelText.rectTransform;
            labelRect.offsetMax = new Vector2(reserved > 0f ? -(reserved + (int)UiSpace.L) : 0f, labelRect.offsetMax.y);
        }

        /// <summary>
        /// C21 · caption + hero number + hairline; Currency draws in StateGold (the gold reserve lives here). RollTo counts up.
        /// The counter's root is <paramref name="name"/>, <see cref="UiBrushTokens.ValueColumn"/> wide until the caller sizes
        /// it (caption, number and line stretch with it); <paramref name="hero"/> false: a NumericL number.
        /// </summary>
        public static UiCounter BigCounter(RectTransform parent, string name, string captionKey, string captionFallback,
            UiCounterStyle style, int value, bool hero = true, int max = 0)
        {
            UiType numberType = hero ? UiType.NumericHero : UiType.NumericL;
            UiColor ink = style == UiCounterStyle.Currency ? UiColor.StateGold : UiColor.TextPrimary;   // G15: gold is money only
            UiCounter counter = UiCounter.Create(name, parent, style, value, numberType, ink, max);
            var root = (RectTransform)counter.transform;

            float captionBand = UiFactory.LineHeight(UiType.MenuSmall);
            float numberBand = UiFactory.LineHeight(numberType);
            float numberTop = captionBand + (int)UiSpace.XS;
            float ruleTop = numberTop + numberBand + (int)UiSpace.S;
            root.sizeDelta = new Vector2(UiBrushTokens.ValueColumn, ruleTop + UiTokens.StrokeHairline);

            TMP_Text caption = UiFactory.Label("Caption", root, UiStrings.Get(captionKey, captionFallback), UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            caption.enableWordWrapping = false;
            Band(caption.rectTransform, 0f, captionBand, true);
            Band(counter.ValueText.rectTransform, numberTop, numberBand, false);

            Transform fraction = root.Find("Max");   // Fraction: "/ max" beside the number, in the small face
            if (fraction != null)
            {
                TMP_Text maxText = fraction.GetComponent<TMP_Text>();
                if (maxText != null)
                {
                    RestyleText(maxText, UiType.MenuSmall);
                }

                Band((RectTransform)fraction, numberTop, numberBand, false);
            }

            Image rule = UiFactory.NewImage("Rule", root, UiColor.TextMuted, UiSprites.Solid);
            rule.color = UiBrushTokens.LineColour();
            Band((RectTransform)rule.transform, ruleTop, UiTokens.StrokeHairline, true);
            return counter;
        }

        /// <summary>
        /// C27 · the brush tooltip for <paramref name="target"/> while it is lifted (hover or focus); text resolved on show.
        /// It opens on a disabled control too - the lock reason is read this way (KILAVUZ §4.3 C27). Calling it again swaps
        /// the text (a reason that changed).
        /// </summary>
        public static void Tooltip(UiButton target, string textKey, string textFallback)
        {
            if (target == null)
            {
                return;
            }

            TooltipSource source = target.GetComponent<TooltipSource>();
            if (source == null)
            {
                source = target.gameObject.AddComponent<TooltipSource>();
            }

            source.Bind(textKey, textFallback);
        }

        /// <summary>C35 · vertical scroller in Brush style (hairline rail, cream grip, ink edge fades).</summary>
        public static UiScrollView Scroll(RectTransform parent, string name)
        {
            return UiScrollView.Create(name, parent, UiControlStyle.Brush);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>The label column of a <paramref name="width"/> row: what the value column and the gap leave (600 at ListWidth).</summary>
        internal static float LabelColumn(float width)
        {
            return Mathf.Max(0f, width - UiBrushTokens.ValueColumn - ValueColumnGap);
        }

        /// <summary>
        /// Dress a TMP label in <paramref name="type"/>'s face, size, weight and line air - what UiFactory.Label does at build,
        /// for a label the kit restyles (a row's button label, the tooltip word). The colour stays the caller's.
        /// </summary>
        internal static void RestyleText(TMP_Text text, UiType type)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset face = UiFonts.For(type);
            if (face != null && text.font != face)
            {
                text.font = face;
            }

            text.fontSize = UiTokens.FontSize(type);
            text.fontStyle = UiTokens.Weight(type) == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            text.lineSpacing = (UiTokens.LineSpacing(type) - 1f) * 100f;
        }

        private static void SetRowWidth(Transform row, float width)
        {
            var rect = (RectTransform)row;
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }

        /// <summary>A horizontal band <paramref name="height"/> high, <paramref name="top"/> below the parent's top; stretchX false keeps the rect's own x.</summary>
        private static void Band(RectTransform rect, float top, float height, bool stretchX)
        {
            rect.anchorMin = new Vector2(stretchX ? 0f : rect.anchorMin.x, 1f);
            rect.anchorMax = new Vector2(stretchX ? 1f : rect.anchorMax.x, 1f);
            rect.offsetMin = new Vector2(stretchX ? 0f : rect.offsetMin.x, -(top + height));
            rect.offsetMax = new Vector2(stretchX ? 0f : rect.offsetMax.x, -top);
        }

        /// <summary>C18 · the parts <see cref="SetRowStatus"/> swaps (found once per event, never per frame).</summary>
        private sealed class RowParts : MonoBehaviour
        {
            public Image Status;
            public TMP_Text Trailing;
            public float Glyph;
        }

        /// <summary>
        /// C27 · lifts the tooltip with its control: pointer over OR focus (as UiButton's lift), read from the uGUI events
        /// themselves so a DISABLED control shows its reason too (UiButton lifts only while interactable). No Update.
        /// </summary>
        private sealed class TooltipSource : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
        {
            private string _key;
            private string _fallback;
            private bool _hovered;
            private bool _focused;

            public void Bind(string key, string fallback)
            {
                _key = key;
                _fallback = fallback;
                if (UiTooltip.Target == transform)
                {
                    UiTooltip.Show((RectTransform)transform, UiStrings.Get(_key, _fallback));   // the reason changed while it was up
                }
            }

            public void OnSelect(BaseEventData eventData)
            {
                Set(_hovered, true);
            }

            public void OnDeselect(BaseEventData eventData)
            {
                Set(_hovered, false);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                Set(true, _focused);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                Set(false, _focused);
            }

            private void OnDisable()
            {
                _hovered = false;
                _focused = false;
                UiTooltip.Hide((RectTransform)transform);
            }

            private void Set(bool hovered, bool focused)
            {
                bool was = _hovered || _focused;
                _hovered = hovered;
                _focused = focused;
                bool lifted = hovered || focused;
                if (lifted == was)
                {
                    return;
                }

                if (lifted)
                {
                    UiTooltip.Show((RectTransform)transform, UiStrings.Get(_key, _fallback));
                }
                else
                {
                    UiTooltip.Hide((RectTransform)transform);
                }
            }
        }
    }
}
