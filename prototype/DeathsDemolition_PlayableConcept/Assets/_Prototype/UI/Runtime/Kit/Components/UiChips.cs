using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>Chip kinds (direction §4 #9).</summary>
    public enum UiChipKind
    {
        /// <summary>Informational: inset fill, secondary text.</summary>
        Neutral = 0,

        /// <summary>A state: 0.6 α outline + glyph + word, coloured by <see cref="UiChipState"/>.</summary>
        State = 1,

        /// <summary>NEW: ink on sakura (AD v004 MUST-1).</summary>
        New = 2,

        /// <summary>The current choice: the shipped SELECTED badge - cream fill, ink word, no rim (AD v003 MUST-3; slice 5a AD MUST-3).</summary>
        Selected = 3,

        /// <summary>A value (LEVEL 3, x8): raised fill, primary text.</summary>
        Value = 4
    }

    /// <summary>
    /// What a State chip says (slice 5a AD MUST-2). Danger = a threat to the player (SURROUNDED,
    /// BEHIND YOU) in StateDanger; Warning = the player must act (ELITE / ULTRA NEAR, LOW, EMPTY,
    /// PRESS A KEY) in StateWarning; Status = a condition of an enemy or object (NET, DAZED,
    /// PULLED) in cream - the warning amber never carries an enemy status.
    /// </summary>
    public enum UiChipState
    {
        Status = 0,
        Warning = 1,
        Danger = 2
    }

    /// <summary>Chip heights (direction §4 #9, AD v004 SHOULD-5: two heights) - floors; the real height is metric × TextScale.</summary>
    public enum UiChipSize
    {
        Row = 0,  // 22
        Card = 1, // 28
        Hud = 2   // 40
    }

    /// <summary>
    /// WP-068 slice 5 · component #9 UiChip v3 (direction §4): height from the type metric at
    /// the current TextScale, width from the text itself (the fixed 208 px threat chip that
    /// clipped "SURROUNDE" is the lesson), a minimum width kept.
    /// USAGE: a short state or value next to what it describes.
    /// DON'T: build a State chip without a word - the constructor rejects it (colour alone
    /// never carries state); don't put two chips' worth of words in one.
    /// ACCESSIBILITY: word (+ glyph for State) always; the outline is 0.6 α of the state colour
    /// so the chip separates from bright ground without relying on hue; the three State kinds
    /// differ by word and glyph first, colour second.
    /// </summary>
    public static class UiChips
    {
        public const float MinWidth = 56f;
        public const float Padding = 12f;
        public const float GlyphGap = 6f;

        public static float Height(UiChipSize size)
        {
            switch (size)
            {
                case UiChipSize.Card: return UiFactory.ScaledBand(UiSize.CardChip);
                case UiChipSize.Hud: return UiFactory.ScaledBand(UiSize.HudChip);
                default: return UiFactory.ScaledBand(UiSize.RowChip);
            }
        }

        /// <summary>The colour of a State chip's rim, word and glyph.</summary>
        public static UiColor ColourOf(UiChipState state)
        {
            switch (state)
            {
                case UiChipState.Danger: return UiColor.StateDanger;
                case UiChipState.Warning: return UiColor.StateWarning;
                default: return UiColor.TextPrimary;
            }
        }

        /// <summary>Build a chip; returns its label (the chip is the label's parent). <paramref name="state"/> is used by State chips.</summary>
        public static TMP_Text Create(string name, Transform parent, UiChipKind kind, string word, UiChipSize size = UiChipSize.Card,
            UiChipState state = UiChipState.Status, string glyphKey = null)
        {
            UiColor stateColour = ColourOf(state);
            if (string.IsNullOrEmpty(word))
            {
                throw new ArgumentException("A chip carries a word (state is never colour alone).", nameof(word));
            }

            if (kind == UiChipKind.State && string.IsNullOrEmpty(glyphKey))
            {
                throw new ArgumentException("A State chip carries a glyph as well as its word.", nameof(glyphKey));
            }

            UiColor fill, text;
            switch (kind)
            {
                case UiChipKind.New:
                    fill = UiColor.AccentSakura;
                    text = UiColor.SurfaceBase;
                    break;
                case UiChipKind.Value:
                    fill = UiColor.SurfaceRaised;
                    text = UiColor.TextPrimary;
                    break;
                case UiChipKind.Selected:
                    fill = UiColor.TextPrimary;
                    text = UiColor.SurfaceBase;
                    break;
                case UiChipKind.State:
                    fill = UiColor.SurfaceInset;
                    text = stateColour;
                    break;
                default:
                    fill = UiColor.SurfaceInset;
                    text = UiColor.TextSecondary;
                    break;
            }

            float height = Height(size);
            TMP_Text label = UiFactory.Chip(name, parent, word, fill, text, MinWidth, height);
            label.fontStyle = FontStyles.Bold;
            if (size == UiChipSize.Hud)
            {
                label.fontSize = UiTokens.FontSize(UiType.Body);
            }

            var chip = (RectTransform)label.transform.parent;
            float glyph = 0f;
            if (!string.IsNullOrEmpty(glyphKey) && UiArt.HasIcon(glyphKey))
            {
                glyph = Mathf.Round(height * 0.6f);
                Image icon = UiArt.IconImage("Glyph", chip, glyphKey, text, glyph);
                var iconRect = (RectTransform)icon.transform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(Padding, 0f);
            }

            float left = glyph > 0f ? Padding + glyph + GlyphGap : Padding;
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2(left, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-Padding, labelRect.offsetMax.y);
            label.alignment = glyph > 0f ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;

            // Width from the text in the chip's own style (measured once, at build).
            float width = Mathf.Max(MinWidth, Mathf.Ceil(label.GetPreferredValues(word).x) + left + Padding);
            chip.sizeDelta = new Vector2(width, height);

            if (kind == UiChipKind.State)
            {
                // 1 px rim at 0.6 α in the state colour, drawn inside the chip (uGUI Outline is banned in v3).
                Image rim = UiFactory.NewImage("Rim", chip, stateColour, UiSprites.ChipRim);
                UiFactory.Fill((RectTransform)rim.transform);
                Color c = rim.color;
                c.a = 0.6f;
                rim.color = c;
                rim.transform.SetSiblingIndex(0);
            }

            return label;
        }
    }
}
