using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-094 HUD v4 (R3, C-00 07.10; the user: "round sayısı ... sağ üstte ... fırça darbesi tasarım diliyle romen
    /// rakamlarıyla ... high polished") · the round as a Roman numeral (<see cref="RomanNumerals"/>: I V X L C only), laid
    /// out letter by letter so the AP-032 brush set drops in without code.
    ///
    /// TWO DRAW PATHS, ONE BOX
    /// - Glyphs: once all five letters have a sprite (<see cref="SetGlyphSprites"/>; the HUD looks up <see cref="GlyphKeys"/>
    ///   in the theme), each letter is one Image placed on the metrics table - an advance per letter and a kerning per pair,
    ///   both in glyph heights, so the set is spaced by numbers the art can tune. The cell keeps the sprite's aspect, centred
    ///   on its advance.
    /// - Text (today, and for a numeral longer than <see cref="Capacity"/>): the HUD's display-face TMP line, the same
    ///   letters in the same box, sized to the line's preferred width.
    /// The box grows LEFTWARD from its right edge (the caller pivots it at x 1 and anchors it to the right), so the top-right
    /// corner holds whatever the round's length. Layout runs only when the value changes; per frame nothing runs.
    ///
    /// THE REVEAL (<see cref="PlayReveal"/>, the round change's hook): glyphs stamp in one after another
    /// (<see cref="RevealStagger"/> apart, α and the stamp scale, Decor - static under reduced motion); the text path stamps
    /// the line as the old seal digit did. It returns the contact time - when the last letter lands - for the seal's hit.
    /// </summary>
    public sealed class HudRoundNumeral
    {
        /// <summary>Letters drawn as glyph Images; a longer numeral (round 1 600 and up) falls back to the text line.</summary>
        public const int Capacity = 16;

        /// <summary>The five letters, in the order every table here uses.</summary>
        public const string Letters = "IVXLC";

        /// <summary>Seconds between two letters' stamps in the glyph reveal.</summary>
        public const float RevealStagger = 0.04f;

        /// <summary>
        /// The theme keys the AP-032 brush letters are looked up under (I V X L C). Not in the G1 atlas yet: until all five
        /// resolve, the numeral is the text line. S-09 confirms the names with P-07 before the set lands.
        /// </summary>
        public static readonly string[] GlyphKeys = { "Roman.I", "Roman.V", "Roman.X", "Roman.L", "Roman.C" };

        /// <summary>Each letter's advance in glyph heights (brush capitals: the I narrow, V and X wide). AP-032 retunes it.</summary>
        public static readonly float[] Advance = { 0.40f, 0.76f, 0.76f, 0.62f, 0.70f };

        /// <summary>
        /// Pair kerning in glyph heights, [left × 5 + right] in I V X L C order; negative draws the pair together (the
        /// diagonals of V and X tuck under their neighbours, L's open foot takes the next letter in).
        /// </summary>
        public static readonly float[] Kerning =
        {
            //  I       V       X       L       C       (right)
            0f,     -0.04f, -0.04f, 0f,     0f,      // I
            -0.04f, 0f,     -0.06f, 0f,     0f,      // V
            -0.04f, -0.06f, -0.02f, -0.02f, -0.02f,  // X
            -0.06f, -0.10f, -0.08f, 0f,     -0.02f,  // L
            0f,     -0.02f, -0.04f, 0f,     0f       // C
        };

        private const float TextPad = 4f;
        private static readonly Vector3 GlyphInk = new Vector3(0.25f, 0f, 0f);   // the HUD's SDF ink edge (RunHudScreen.Ink)

        private readonly RectTransform _box;
        private readonly TMP_Text _text;
        private readonly float _height;
        private readonly UiColor _tint;
        private readonly Sprite[] _sprites = new Sprite[5];
        private readonly char[] _letters = new char[Capacity];
        private Image[] _glyphs;
        private bool _complete;
        private int _value = int.MinValue;
        private int _length;
        private bool _drawsGlyphs;

        /// <param name="box">The numeral's box: pivot x 1, anchored right by the caller; its width follows the numeral.</param>
        /// <param name="fallback">The display-face line inside the box (filled, right-aligned, in the HUD ink).</param>
        /// <param name="glyphHeight">The letters' height on the glyph path (layout px).</param>
        /// <param name="tint">The glyphs' tint (the text line keeps its own colour).</param>
        public HudRoundNumeral(RectTransform box, TMP_Text fallback, float glyphHeight, UiColor tint)
        {
            _box = box;
            _text = fallback;
            _height = glyphHeight;
            _tint = tint;
        }

        /// <summary>The numeral's box (its width is the numeral's).</summary>
        public RectTransform Box => _box;

        /// <summary>The display-face line (the text path; tests read its text).</summary>
        public TMP_Text Text => _text;

        /// <summary>The value shown (int.MinValue before the first <see cref="Set"/>).</summary>
        public int Value => _value;

        /// <summary>The numeral's width in layout px.</summary>
        public float Width => _box.sizeDelta.x;

        /// <summary>True when the letters are glyph Images (the AP-032 set is in and the numeral fits <see cref="Capacity"/>).</summary>
        public bool DrawsGlyphs => _drawsGlyphs;

        /// <summary>Glyph Images showing (0 on the text path).</summary>
        public int GlyphCount => _drawsGlyphs ? _length : 0;

        /// <summary>A letter's index in <see cref="Letters"/> (I 0 … C 4), −1 for anything else.</summary>
        public static int KindOf(char letter)
        {
            switch (letter)
            {
                case 'I': return 0;
                case 'V': return 1;
                case 'X': return 2;
                case 'L': return 3;
                case 'C': return 4;
                default: return -1;
            }
        }

        /// <summary>The glyph path's width for <paramref name="length"/> letters at <paramref name="height"/>: advances plus pair kerning.</summary>
        public static float MeasureGlyphs(char[] letters, int length, float height)
        {
            float x = 0f;
            int previous = -1;
            for (int i = 0; i < length; i++)
            {
                int kind = KindOf(letters[i]);
                if (kind < 0)
                {
                    continue;
                }

                if (previous >= 0)
                {
                    x += Kerning[previous * 5 + kind] * height;
                }

                x += Advance[kind] * height;
                previous = kind;
            }

            return x;
        }

        /// <summary>
        /// The AP-032 letters in I V X L C order (null where a letter is missing). The glyph path turns on only when all five
        /// are present; the Images are built on the first complete set. The shown value is laid out again.
        /// </summary>
        public void SetGlyphSprites(Sprite[] sprites)
        {
            bool complete = sprites != null && sprites.Length >= 5;
            for (int i = 0; i < 5; i++)
            {
                _sprites[i] = sprites != null && i < sprites.Length ? sprites[i] : null;
                complete &= _sprites[i] != null;
            }

            _complete = complete;
            if (_complete && _glyphs == null)
            {
                _glyphs = new Image[Capacity];
                for (int i = 0; i < Capacity; i++)
                {
                    Image glyph = UiFactory.NewImage("Glyph." + i, _box, _tint);
                    glyph.preserveAspect = true;
                    RectTransform rect = glyph.rectTransform;
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(0f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    glyph.enabled = false;
                    _glyphs[i] = glyph;
                }
            }

            int value = _value;
            _value = int.MinValue;
            if (value != int.MinValue)
            {
                Set(value);
            }
        }

        /// <summary>Shows <paramref name="value"/> (≤ 0: the HUD placeholder); returns the numeral's width. A repeat value does nothing.</summary>
        public float Set(int value)
        {
            if (value == _value)
            {
                return Width;
            }

            _value = value;
            int length = RomanNumerals.Length(value);
            _drawsGlyphs = _complete && length > 0 && length <= Capacity;
            if (_drawsGlyphs)
            {
                _length = RomanNumerals.Write(value, _letters, 0);
                LayGlyphs();
                _text.enabled = false;
                return Width;
            }

            _length = 0;
            HideGlyphs(0);
            _text.enabled = true;
            _text.text = value > 0 ? RomanNumerals.Cached(value) : RunHudViewModel.Placeholder;
            float width = Mathf.Ceil(_text.GetPreferredValues(_text.text).x) + TextPad;
            _box.sizeDelta = new Vector2(width, _box.sizeDelta.y);
            return width;
        }

        /// <summary>
        /// The round change's reveal: the glyphs stamp in left to right, or the text line stamps (Decor - reduced motion shows
        /// it at rest). Returns the seconds until the last letter lands (the seal's contact frame).
        /// </summary>
        public float PlayReveal()
        {
            if (!_drawsGlyphs)
            {
                UiPresets.StampIn(_box);
                return UiPresets.StampContactSeconds;
            }

            for (int i = 0; i < _length; i++)
            {
                Image glyph = _glyphs[i];
                float delay = i * RevealStagger;
                glyph.canvasRenderer.SetAlpha(UiMotion.ReducedMotion ? 1f : 0f);
                UiMotion.TintAlpha(glyph, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor, delay);
                UiMotion.ScaleSeconds(glyph.rectTransform, UiMotionTokens.StampPeakScale, 1f, UiMotionTokens.StampInSeconds, UiEase.Exit, UiMotionClass.Decor, delay);
            }

            return Mathf.Max(0, _length - 1) * RevealStagger + UiMotionTokens.StampInSeconds;
        }

        private void LayGlyphs()
        {
            float h = _height;
            float x = 0f;
            int previous = -1;
            for (int i = 0; i < _length; i++)
            {
                int kind = KindOf(_letters[i]);
                if (previous >= 0)
                {
                    x += Kerning[previous * 5 + kind] * h;
                }

                Sprite sprite = _sprites[kind];
                Image glyph = _glyphs[i];
                glyph.sprite = sprite;
                if (UiArt.IsGlyph(sprite))
                {
                    UiArt.ApplyGlyph(glyph, GlyphInk);
                }

                glyph.enabled = true;
                glyph.canvasRenderer.SetAlpha(1f);
                float advance = Advance[kind] * h;
                float cell = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height * h : advance;
                RectTransform rect = glyph.rectTransform;
                rect.sizeDelta = new Vector2(cell, h);
                rect.anchoredPosition = new Vector2(x + advance * 0.5f, 0f);
                rect.localScale = Vector3.one;
                x += advance;
                previous = kind;
            }

            HideGlyphs(_length);
            _box.sizeDelta = new Vector2(Mathf.Ceil(x), _box.sizeDelta.y);
        }

        private void HideGlyphs(int from)
        {
            if (_glyphs == null)
            {
                return;
            }

            for (int i = from; i < Capacity; i++)
            {
                if (_glyphs[i].enabled)
                {
                    _glyphs[i].enabled = false;
                }
            }
        }
    }
}
