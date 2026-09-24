using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>Notice severity (direction §4 #10).</summary>
    public enum UiNoticeKind
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// WP-068 slice 5 · component #10 UiNotice and #12 UiEmptyState (direction §4).
    ///
    /// UiNotice - a 4 px bar + optional glyph + one or two lines of Body text on the inset
    /// surface, sized from the text itself (auto height, so TextScale 1.25 never clips).
    /// USAGE: a fact the player should read before acting (read-only profile, a binding
    /// conflict, an offer error). DON'T: stack more than one per pane, or use it for
    /// success confirmation (that is the control's own state).
    /// ACCESSIBILITY: severity is the word in the text and the bar, not the bar colour
    /// alone; Warning uses StateWarning (not the currency gold - U4), Error StateDanger.
    ///
    /// UiEmptyState - 48 px glyph + Body sentence + optional action button, centred.
    /// USAGE: a pane with nothing to show yet (no runs, no achievements) - it states the fact
    /// and the way forward. DON'T: show a wall of zeros instead.
    /// ACCESSIBILITY: the action is a normal focusable UiButton; the sentence is TextSecondary
    /// on the pane surface (audited pair).
    /// </summary>
    public static class UiNotices
    {
        public const float BarWidth = 4f;
        public const float GlyphSize = 20f;

        /// <summary>The glyph well starts here (AD 5a SHOULD-1): x +12.</summary>
        public const float GlyphX = 12f;

        /// <summary>Text always starts here, glyph or not (AD 5a SHOULD-1: the well is reserved, so the A3 glyphs never reflow the text).</summary>
        public const float TextX = 40f;

        /// <summary>Build a notice under <paramref name="parent"/> with the given width; its height is measured from the text (≥ one line).</summary>
        public static RectTransform Notice(string name, Transform parent, UiNoticeKind kind, string text, float width, string glyphKey = null)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            Image background = UiFactory.NewImage("Surface", root, UiColor.SurfaceInset, UiSprites.Card);
            UiFactory.Fill((RectTransform)background.transform);

            Image bar = UiFactory.NewImage("Bar", root, BarColour(kind), UiSprites.Solid);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(BarWidth, 0f);
            barRect.anchoredPosition = Vector2.zero;

            float left = TextX;
            if (!string.IsNullOrEmpty(glyphKey) && UiArt.HasIcon(glyphKey))
            {
                Image glyph = UiArt.IconImage("Glyph", root, glyphKey, BarColour(kind), GlyphSize);
                var glyphRect = (RectTransform)glyph.transform;
                glyphRect.anchorMin = new Vector2(0f, 1f);
                glyphRect.anchorMax = new Vector2(0f, 1f);
                glyphRect.pivot = new Vector2(0f, 1f);
                glyphRect.anchoredPosition = new Vector2(GlyphX, -(int)UiSpace.M);
            }

            TMP_Text body = UiFactory.Label("Text", root, text, UiType.Body, UiColor.TextPrimary, TextAnchor.UpperLeft);
            RectTransform bodyRect = UiFactory.Fill(body.rectTransform);
            bodyRect.offsetMin = new Vector2(left, (int)UiSpace.M);
            bodyRect.offsetMax = new Vector2(-(int)UiSpace.M, -(int)UiSpace.M);

            float textWidth = Mathf.Max(1f, width - left - (int)UiSpace.M);
            float textHeight = Mathf.Max(UiFactory.LineHeight(UiType.Body), Mathf.Ceil(body.GetPreferredValues(text, textWidth, 0f).y));
            root.sizeDelta = new Vector2(width, textHeight + (int)UiSpace.M * 2f);
            return root;
        }

        private static UiColor BarColour(UiNoticeKind kind)
        {
            switch (kind)
            {
                case UiNoticeKind.Warning: return UiColor.StateWarning;
                case UiNoticeKind.Error: return UiColor.StateDanger;
                default: return UiColor.TextSecondary;
            }
        }

        public const float EmptyGlyphSize = 48f;

        /// <summary>Glyph + sentence + optional action, centred in <paramref name="parent"/>. Returns the action button (or null).</summary>
        public static UiButton EmptyState(string name, Transform parent, string glyphKey, string sentence, string actionLabel = null, System.Action action = null)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            UiFactory.Fill(root);

            float y = 48f;
            if (!string.IsNullOrEmpty(glyphKey) && UiArt.HasIcon(glyphKey))
            {
                Image glyph = UiArt.IconImage("Glyph", root, glyphKey, UiColor.TextMuted, EmptyGlyphSize);
                ((RectTransform)glyph.transform).anchoredPosition = new Vector2(0f, y + EmptyGlyphSize * 0.5f);
            }

            // The sentence measures to the pane it sits in (never wider than the reading
            // measure), so TextScale 1.25 wraps inside the pane instead of spilling out (k1).
            float paneWidth = parent is RectTransform ? ((RectTransform)parent).rect.width : 0f;
            float width = paneWidth > 1f ? Mathf.Min(UiTokens.MaxMeasure, paneWidth - (int)UiSpace.L * 2f) : UiTokens.MaxMeasure;
            TMP_Text text = UiFactory.Label("Sentence", root, sentence, UiType.Body, UiColor.TextSecondary, TextAnchor.UpperCenter);
            float textHeight = Mathf.Max(UiFactory.LineHeight(UiType.Body), Mathf.Ceil(text.GetPreferredValues(sentence, width, 0f).y));
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.sizeDelta = new Vector2(width, textHeight);
            textRect.anchoredPosition = new Vector2(0f, y - (int)UiSpace.L);

            if (string.IsNullOrEmpty(actionLabel) || action == null)
            {
                return null;
            }

            UiButton button = UiFactory.Button("Action", root, actionLabel, UiButtonVariant.Secondary, action);
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.sizeDelta = new Vector2(Mathf.Min(280f, width), UiSize.Target);
            buttonRect.anchoredPosition = new Vector2(0f, y - (int)UiSpace.L - textHeight - (int)UiSpace.L);
            return button;
        }
    }
}
