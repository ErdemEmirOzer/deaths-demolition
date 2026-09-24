using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #18 UiPips (direction §4): a row of cream pips - the upgrade
    /// tier ladder, the two pick slots (DEC-087). Filled = solid, empty = outline.
    /// USAGE: small counts with a fixed ceiling (≤ 10) where the ceiling itself matters.
    /// DON'T: use gold (reserve) or colour to tell filled from empty.
    /// ACCESSIBILITY: filled vs empty is SHAPE (solid vs ring), readable in both palettes; a
    /// newly filled pip stamps (Decor - Reduced Motion shows it filled at once).
    /// </summary>
    public sealed class UiPips : MonoBehaviour
    {
        public const float PipWidth = 8f;
        public const float PipHeight = 12f;

        private Image[] _pips;
        private int _filled;

        public int Filled
        {
            get { return _filled; }
        }

        public int Count
        {
            get { return _pips != null ? _pips.Length : 0; }
        }

        public static UiPips Create(string name, Transform parent, int count, int filled, float width = PipWidth, float height = PipHeight)
        {
            RectTransform row = UiFactory.NewRect(name, parent);
            row.sizeDelta = new Vector2(count * width + (count - 1) * (int)UiSpace.XS, height);
            var pips = row.gameObject.AddComponent<UiPips>();
            pips._pips = new Image[count];
            for (int i = 0; i < count; i++)
            {
                Image pip = UiFactory.NewImage("Pip" + i, row, UiColor.TextPrimary, UiSprites.Chip);
                var rect = (RectTransform)pip.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = new Vector2(i * (width + (int)UiSpace.XS), 0f);
                pips._pips[i] = pip;
            }

            pips.Set(filled, false);
            return pips;
        }

        public void Set(int filled, bool animate)
        {
            filled = Mathf.Clamp(filled, 0, _pips.Length);
            for (int i = 0; i < _pips.Length; i++)
            {
                bool on = i < filled;
                _pips[i].sprite = on ? UiSprites.Chip : UiSprites.RingThin;
                _pips[i].color = UiTokens.Get(on ? UiColor.TextPrimary : UiColor.TextMuted);
                if (animate && on && i >= _filled)
                {
                    UiPresets.StampIn((RectTransform)_pips[i].transform);
                }
            }

            _filled = filled;
        }
    }

    /// <summary>Counter formats (direction §4 #19).</summary>
    public enum UiCounterStyle
    {
        Plain = 0,
        Currency = 1,
        Fraction = 2,
        Timer = 3
    }

    /// <summary>
    /// WP-068 slice 5 · component #19 UiCounter (direction §4): a number that rolls to its new
    /// value (CounterRoll, 600 ms Settle, 0 allocations - TMP SetText with an int).
    /// Plain / Currency (a new delta mid-roll retargets the same roll from the digit on screen) / Fraction (value + " / max"
    /// in a second label - no rich text) / Timer (m:ss, set each second, never rolled).
    /// USAGE: HUD gold, Results / Statistics hero numbers, shop prices.
    /// DON'T: roll a Data readout that must read exact every frame (ammo in combat - set it).
    /// ACCESSIBILITY: Delta class - Reduced Motion shows the final value at once; the number is
    /// the Numeric face with equal digit advances (no jitter while rolling).
    /// </summary>
    public sealed class UiCounter : MonoBehaviour
    {
        private TMP_Text _value;
        private TMP_Text _max;
        private UiCounterStyle _style;
        private int _shown;
        private int _target;

        public int Target
        {
            get { return _target; }
        }

        public TMP_Text ValueText
        {
            get { return _value; }
        }

        public static UiCounter Create(string name, Transform parent, UiCounterStyle style, int value, UiType type = UiType.NumericL, UiColor colour = UiColor.TextPrimary, int max = 0)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            var counter = root.gameObject.AddComponent<UiCounter>();
            counter._style = style;
            counter._value = UiFactory.Numeric("Value", root, string.Empty, type, colour, TextAnchor.MiddleLeft, false);
            UiFactory.Fill(counter._value.rectTransform);
            if (style == UiCounterStyle.Fraction)
            {
                // "24 / 30" reads as one unit: the value sits right-aligned in a box as wide as
                // the maximum (digits never shift the slash), the maximum follows it.
                counter._value.alignment = TextAlignmentOptions.Right;
                counter._max = UiFactory.Label("Max", root, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.MiddleLeft);
                RectTransform maxRect = UiFactory.Fill(counter._max.rectTransform);
                maxRect.offsetMax = Vector2.zero;
                counter.SetMax(max);
            }

            counter.SetNow(value);
            return counter;
        }

        /// <summary>Show a value at once (build, Timer ticks, Reduced Motion).</summary>
        public void SetNow(int value)
        {
            _target = value;
            _shown = value;
            UiMotion.KillTarget(_value, false);
            Write(value);
        }

        /// <summary>Roll to a new value; a new value mid-roll retargets the same roll from the digit on screen.</summary>
        public void RollTo(int value)
        {
            if (value == _target)
            {
                return;
            }

            _target = value;
            if (_style == UiCounterStyle.Timer)
            {
                SetNow(value);
                return;
            }

            UiPresets.CounterRoll(_value, _shown, _target);
            _shown = _target;
        }

        public void SetMax(int max)
        {
            if (_max == null)
            {
                return;
            }

            _max.SetText("/ {0}", max);
            float box = Mathf.Ceil(_value.GetPreferredValues(max.ToString()).x); // one string per SetMax (magazine change), never per value
            RectTransform valueRect = _value.rectTransform;
            valueRect.anchorMax = new Vector2(0f, 1f);
            valueRect.offsetMin = new Vector2(0f, valueRect.offsetMin.y);
            valueRect.offsetMax = new Vector2(box, valueRect.offsetMax.y);
            RectTransform maxRect = _max.rectTransform;
            maxRect.offsetMin = new Vector2(box + (int)UiSpace.S, maxRect.offsetMin.y);
        }

        private void Write(int value)
        {
            if (_style == UiCounterStyle.Timer)
            {
                int minutes = value / 60;
                int seconds = value % 60;
                _value.SetText("{0}:{1}{2}", minutes, seconds / 10, seconds % 10);
            }
            else
            {
                _value.SetText("{0}", value);
            }
        }
    }

    /// <summary>
    /// WP-068 slice 5 · component #17 UiRing (direction §4): a radial Data ring (Image.Filled
    /// Radial360 from 12 o'clock, clockwise) over a faint untinted cream ghost ring (0.12 α -
    /// AD 5a SHOULD-5: a ghost in the fill colour read as a second gold band) - reload 64, Special 132,
    /// achievement progress, the loading ensō. The value is written as-is (Data: never eased,
    /// Reduced Motion never changes it), and only on a visible change (Δ ≥ 1/256).
    /// USAGE: a proportion that fills over time. DON'T: animate it for decoration.
    /// ACCESSIBILITY: the fill is shape (arc length); pair it with a word or number.
    /// </summary>
    public sealed class UiRing : MonoBehaviour
    {
        private Image _fill;
        private Image _ghost;
        private float _shown = -1f;

        public float Value
        {
            get { return _fill != null ? _fill.fillAmount : 0f; }
        }

        public static UiRing Create(string name, Transform parent, float size, UiColor colour, float ghostAlpha = 0.12f, Sprite ringSprite = null)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            root.sizeDelta = new Vector2(size, size);
            // WP-068 A4: the authored progress rings (reload 64 stroke 5, Special 132 stroke 8) when the atlas has them;
            // a caller that needs another stroke (the HUD's Ø 100 hairline, AD 8a SHOULD-1) passes its own ring.
            Sprite art = ringSprite != null ? ringSprite : UiArt.Kit(size <= 96f ? "Ring.Progress64" : "Ring.Progress132");
            Image ghost = UiFactory.NewImage("Ghost", root, UiColor.TextPrimary, art != null ? art : UiSprites.RingThin);
            UiFactory.Fill((RectTransform)ghost.transform);
            Color g = ghost.color;
            g.a = ghostAlpha;
            ghost.color = g;

            var ring = root.gameObject.AddComponent<UiRing>();
            ring._ghost = ghost;
            ring._fill = UiFactory.NewImage("Fill", root, colour, art != null ? art : UiSprites.Ring);
            UiFactory.Fill((RectTransform)ring._fill.transform);
            ring._fill.type = Image.Type.Filled;
            ring._fill.fillMethod = Image.FillMethod.Radial360;
            ring._fill.fillOrigin = (int)Image.Origin360.Top;
            ring._fill.fillClockwise = true;
            ring._fill.fillAmount = 0f;
            return ring;
        }

        /// <summary>WP-068 8-R: recolour the track (the HUD ring's LOW state, AD SHOULD-1).</summary>
        public void SetGhost(UiColor colour, float alpha)
        {
            if (_ghost == null)
            {
                return;
            }

            Color c = UiTokens.Get(colour);
            c.a = alpha;
            if (_ghost.color != c)
            {
                _ghost.color = c;
            }
        }

        /// <summary>Swap the fill colour (the HUD's LOW reload ring turns StateWarning, E11).</summary>
        public void SetColour(UiColor colour)
        {
            Color c = UiTokens.Get(colour);
            if (_fill != null && _fill.color != c)
            {
                _fill.color = c;
            }
        }

        public void Set(float value01)
        {
            float v = Mathf.Clamp01(value01);
            if (Mathf.Abs(v - _shown) < 1f / 256f && _shown >= 0f)
            {
                return;
            }

            _shown = v;
            _fill.fillAmount = v;
        }
    }

    /// <summary>
    /// WP-068 slice 5 · component #20 HeroTile (direction §4): 414 × 120 (334 in the 1408 page's
    /// three columns) - one NumericXL counter over a Label caption.
    /// USAGE: the three or four numbers a Statistics / Results screen is about.
    /// DON'T: put more than one number in a tile.
    /// ACCESSIBILITY: the caption names the number (never an icon alone).
    /// </summary>
    public static class UiHeroTiles
    {
        public const float Width = 414f;
        public const float Height = 120f;

        public static UiCounter Create(string name, Transform parent, string caption, int value)
        {
            RectTransform tile = UiFactory.FramedCard(name, parent, UiColor.BorderHairline, UiColor.SurfacePanel, UiFrame.Card);
            tile.sizeDelta = new Vector2(Width, Height);
            RectTransform content = UiFactory.CardContent(tile, UiSpace.L);
            UiCounter counter = UiCounter.Create("Counter", content, UiCounterStyle.Plain, value, UiType.NumericXL);
            var counterRect = (RectTransform)counter.transform;
            counterRect.anchorMin = new Vector2(0f, 1f);
            counterRect.anchorMax = new Vector2(1f, 1f);
            counterRect.pivot = new Vector2(0.5f, 1f);
            counterRect.sizeDelta = new Vector2(0f, UiFactory.LineHeight(UiType.NumericXL));
            counterRect.anchoredPosition = Vector2.zero;

            TMP_Text label = UiFactory.Label("Caption", content, caption, UiType.Label, UiColor.TextSecondary, TextAnchor.LowerLeft);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.sizeDelta = new Vector2(0f, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Label)));
            labelRect.anchoredPosition = Vector2.zero;
            return counter;
        }
    }
}
