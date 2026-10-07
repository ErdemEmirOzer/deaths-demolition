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
    /// - Glyphs: once all five letters have a sprite (<see cref="SetGlyphAlternates"/> / <see cref="SetGlyphSprites"/>; the HUD
    ///   looks up <see cref="GlyphKeys"/> - AP-032 v001's brush cells, HudArt), each letter is one Image placed on AP-032's
    ///   metrics (roman/ap032_roman_metrics.json, 08.10): the cell is 1.6 em square, its left edge at pen − originX, the
    ///   baseline 0.3 em over its bottom (so the cap sits centred in the box); the pen moves by the letter's advance plus the
    ///   pair's kerning. The alternates (I×3 V×2 X×3 L×2 C×2) are picked as the metrics say: (round + position) mod n, so III
    ///   and XXX never repeat a cell side by side and a round always draws the same.
    /// - Text (no AP-032 set, or a numeral longer than <see cref="Capacity"/>): the HUD's display-face TMP line, the same
    ///   letters in the same box, sized to the line's preferred width. On the glyph path the (disabled) line still carries
    ///   the numeral's text, so a reader of the HUD reads the round either way.
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
        /// The theme keys the AP-032 brush letters are looked up under (I V X L C): UiTheme resolves them to AP-032 v001's
        /// cells (HudArt.Icon; the HUD itself asks HudArt for every alternate).
        /// </summary>
        public static readonly string[] GlyphKeys = { "Roman.I", "Roman.V", "Roman.X", "Roman.L", "Roman.C" };

        /// <summary>Each letter's advance in em (cap heights) - AP-032 v001 metrics (`glyphs.&lt;G&gt;.advance_em`).</summary>
        public static readonly float[] Advance = { 0.3417f, 0.7513f, 0.78f, 0.6447f, 0.7364f };

        /// <summary>Each letter's pen-to-cell offset in em (`originX_em`): the cell's left edge is pen − originX.</summary>
        public static readonly float[] OriginX = { 0.6312f, 0.4066f, 0.3941f, 0.4378f, 0.4174f };

        /// <summary>Each letter's left side bearing in em (`lsb_em`): where its ink starts past the pen.</summary>
        public static readonly float[] LeftBearing = { 0.05f, 0.0121f, 0.0121f, 0.0528f, 0.0295f };

        /// <summary>
        /// Pair kerning in em, [left × 5 + right] in I V X L C order - AP-032 v001's `kerning_table_em` (all 25 pairs, P-07's
        /// optical pass held to its core-gap constraint ≥ 0.07 em: LV takes the foot's room, XX / CV / CX open up).
        /// </summary>
        public static readonly float[] Kerning =
        {
            //  I        V        X        L        C        (right)
            0f,      0.01f,   0.01f,   0f,      -0.005f,   // I
            -0.005f, 0.02f,   0.03f,   -0.01f,  -0.025f,   // V
            0.01f,   0.05f,   0.065f,  0.04f,   -0.03f,    // X
            0.01f,   -0.13f,  0.025f,  0.015f,  -0.03f,    // L
            0.01f,   0.065f,  0.065f,  0.005f,  -0.07f     // C
        };

        private const float TextPad = 4f;
        private static readonly Vector3 GlyphInk = new Vector3(0.25f, 0f, 0f);   // the HUD's SDF ink edge (RunHudScreen.Ink)

        private readonly RectTransform _box;
        private readonly TMP_Text _text;
        private float _height;
        private readonly UiColor _tint;
        private readonly Sprite[][] _alternates = new Sprite[5][];
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

        /// <summary>True when <paramref name="value"/> would draw as glyphs (the set is in and it fits <see cref="Capacity"/>).</summary>
        public bool DrawsGlyphsFor(int value)
        {
            int length = RomanNumerals.Length(value);
            return _complete && length > 0 && length <= Capacity;
        }

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

        /// <summary>One sprite per letter in I V X L C order (no alternates; null where a letter is missing).</summary>
        public void SetGlyphSprites(Sprite[] sprites)
        {
            var alternates = new Sprite[5][];
            for (int i = 0; i < 5; i++)
            {
                Sprite sprite = sprites != null && i < sprites.Length ? sprites[i] : null;
                alternates[i] = sprite != null ? new[] { sprite } : null;
            }

            SetGlyphAlternates(alternates);
        }

        /// <summary>
        /// The AP-032 letters in I V X L C order, each with its alternates (null / empty where a letter is missing). The glyph
        /// path turns on only when all five have one; the Images are built on the first complete set. The shown value is
        /// laid out again.
        /// </summary>
        public void SetGlyphAlternates(Sprite[][] alternates)
        {
            bool complete = alternates != null && alternates.Length >= 5;
            for (int i = 0; i < 5; i++)
            {
                Sprite[] set = alternates != null && i < alternates.Length ? alternates[i] : null;
                _alternates[i] = set != null && set.Length > 0 && set[0] != null ? set : null;
                complete &= _alternates[i] != null;
            }

            _complete = complete;
            if (_complete && _glyphs == null)
            {
                _glyphs = new Image[Capacity];
                for (int i = 0; i < Capacity; i++)
                {
                    Image glyph = UiFactory.NewImage("Glyph." + i, _box, _tint);
                    glyph.preserveAspect = false;   // the cell is square and placed by the metrics
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
                _text.text = RomanNumerals.Cached(value);   // the line stays the numeral's text (readers, the fallback)
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

        /// <summary>The first letter's left side bearing in em (0 on the text path) - the smear's layout starts the ink there.</summary>
        public float FirstBearingEm
        {
            get
            {
                if (!_drawsGlyphs || _length == 0)
                {
                    return 0f;
                }

                int kind = KindOf(_letters[0]);
                return kind >= 0 ? LeftBearing[kind] : 0f;
            }
        }

        /// <summary>The letters' height now (the plate fit may shrink it for a long numeral).</summary>
        public float Height => _height;

        /// <summary>
        /// WP-094 (08.10, the plate fit): a new cap height for the glyph path; the shown value is laid again at it. The text path
        /// keeps its type size (it has no plate).
        /// </summary>
        public void SetHeight(float height)
        {
            if (Mathf.Approximately(height, _height))
            {
                return;
            }

            _height = height;
            int value = _value;
            _value = int.MinValue;
            if (value != int.MinValue)
            {
                Set(value);
            }
        }

        private static readonly char[] s_measure = new char[Capacity];

        /// <summary>The numeral's width in em on the glyph path (advances + kerning); 0 for ≤ 0 or past <see cref="Capacity"/>.</summary>
        public static float EmWidth(int round, out float firstBearingEm)
        {
            firstBearingEm = 0f;
            int length = RomanNumerals.Length(round);
            if (length <= 0 || length > Capacity)
            {
                return 0f;
            }

            int n = RomanNumerals.Write(round, s_measure, 0);
            firstBearingEm = LeftBearing[KindOf(s_measure[0])];
            return MeasureGlyphs(s_measure, n, 1f);
        }

        /// <summary>The alternate a letter at <paramref name="position"/> of round <paramref name="round"/> takes: (round + position) mod n.</summary>
        public static int AlternateFor(int round, int position, int count)
        {
            return count <= 1 ? 0 : (int)((uint)(round + position) % (uint)count);
        }

        private void LayGlyphs()
        {
            float h = _height;
            float cell = HudArt.RomanCellEm * h;
            float x = 0f;
            int previous = -1;
            for (int i = 0; i < _length; i++)
            {
                int kind = KindOf(_letters[i]);
                if (previous >= 0)
                {
                    x += Kerning[previous * 5 + kind] * h;
                }

                Sprite[] set = _alternates[kind];
                Sprite sprite = set[AlternateFor(_value, i, set.Length)];
                Image glyph = _glyphs[i];
                glyph.sprite = sprite;
                if (UiArt.IsGlyph(sprite))
                {
                    UiArt.ApplyGlyph(glyph, GlyphInk);
                }

                glyph.enabled = true;
                glyph.canvasRenderer.SetAlpha(1f);
                RectTransform rect = glyph.rectTransform;
                rect.sizeDelta = new Vector2(cell, cell);
                // The cell's left edge at pen − originX; its centre is the cap's centre (baseline 0.3 em over the bottom).
                rect.anchoredPosition = new Vector2(x - OriginX[kind] * h + cell * 0.5f, 0f);
                rect.localScale = Vector3.one;
                x += Advance[kind] * h;
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
