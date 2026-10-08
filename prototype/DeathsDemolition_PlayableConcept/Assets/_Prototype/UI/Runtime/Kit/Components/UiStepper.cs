using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #4 UiStepper (direction §4): "‹ value ›" for a small set of
    /// discrete values - Quality, Display, Palette, Aim Assist. The row is one focusable
    /// <see cref="UiButton"/>; Left / Right (keys, d-pad, stick) step the value while it has
    /// focus, Up / Down keep navigating; the chevrons are 44 px pointer targets.
    ///
    /// USAGE: 2–6 named, discrete choices where a slider would pretend to be continuous.
    /// DON'T: use it for two-state ON/OFF (<see cref="UiSwitch"/>) or for long lists.
    /// Categories (≤ 3 unordered values) wrap; ordered values stop at the ends, and the
    /// end chevron dims.
    /// ACCESSIBILITY: the value is a word, never a colour; ← / → keep working with the
    /// pointer anywhere; the value change is a Fast crossfade on the UiMotion core
    /// (Decor - Reduced Motion shows the new word at once).
    ///
    /// WP-098A P0b (C-00 07.10) · C16 value cycle, Brush style (<see cref="UiControlStyle.Brush"/>, KILAVUZ §4.1 / §4.3,
    /// KIT-API §4 "Ayar satırları") - the kit's dropdown for 2–6 values: a frameless <see cref="UiButtonVariant.Brush"/>
    /// row, the MenuBody label in the label column and "◂ VALUE ▸" across the value column - the O1 Menu.CycleLeft /
    /// Menu.CycleRight glyphs at 24 (AP-034 v002.1 I6, drawn for C16: two geometries, never a mirror - a mirror would turn the
    /// light; UiSprites.Chevron when the atlas has none) in TextSecondary, TextPrimary under the pointer,
    /// the value in MenuBody cream. A step slides the arrow 4 px its way and back (Fast, Decor); an end arrow sits at
    /// <see cref="UiTokens.OpacityDisabled"/> and a push past it plays Denied + <see cref="UiPresets.DeniedNudge"/> on that
    /// arrow (never on the word). Reduced Motion: the word cuts, nothing slides or shakes. V3 (the default) is unchanged.
    /// </summary>
    public sealed class UiStepper : MonoBehaviour, IMoveHandler
    {
        /// <summary>The chevron hit box (44 px rule); the drawn glyph is <see cref="ChevronGlyph"/>.</summary>
        public const float ChevronSize = 44f;
        public const float ChevronGlyph = 14f;

        /// <summary>WP-098A · the C16 arrows' O1 glyphs (AP-034 v002.1 I6; the pager's Menu.Previous / Menu.Next stay the pager's).</summary>
        public const string CycleLeftIcon = "Menu.CycleLeft";

        public const string CycleRightIcon = "Menu.CycleRight";

        /// <summary>The value box is as wide as the longest value (measured at build, at the current TextScale), at least this - so stacked steppers with short values share one left-chevron column (AD 5a SHOULD-2).</summary>
        public const float MinValueWidth = 160f;

        private float _valueWidth = MinValueWidth;

        private UiButton _button;
        private TMP_Text _value;
        private Image _previous;
        private Image _next;
        private string[] _values;
        private int _index;
        private bool _wrap;
        private Action<int> _changed;

        // WP-098A P0b · Brush style only.
        private UiControlStyle _style;
        private int _hoverDirection;   // the arrow under the pointer (−1 / +1), 0 = none

        /// <summary>WP-098A: the dialect this row is drawn in.</summary>
        public UiControlStyle Style
        {
            get { return _style; }
        }

        /// <summary>WP-098A Brush: the arrow glyphs (tests: end dimming, hover tint).</summary>
        public Image PreviousArrow
        {
            get { return _previous; }
        }

        public Image NextArrow
        {
            get { return _next; }
        }

        public UiButton Button
        {
            get { return _button; }
        }

        public int Index
        {
            get { return _index; }
        }

        public string ValueText
        {
            get { return _value != null ? _value.text : string.Empty; }
        }

        /// <summary>Measured value box width (tests: the longest value fits).</summary>
        public float ValueWidth
        {
            get { return _valueWidth; }
        }

        /// <summary>The row label (tests: T3 truncation at 1.25).</summary>
        public TMP_Text Label
        {
            get { return _button != null ? _button.LabelText : null; }
        }

        /// <summary>The value label (tests: T3 truncation at 1.25).</summary>
        public TMP_Text ValueLabel
        {
            get { return _value; }
        }

        /// <summary>
        /// A stepper row. WP-098A: <paramref name="style"/> Brush draws the C16 value cycle (<see cref="UiBrushKit.Cycle"/>
        /// is its entry point; the row is <see cref="UiBrushTokens.ListWidth"/> wide until the caller sizes it); V3 is
        /// today's row.
        /// </summary>
        public static UiStepper Create(string name, Transform parent, string label, string[] values, int index, bool wrap, Action<int> changed,
            UiControlStyle style = UiControlStyle.V3)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("UiStepper needs at least one value.", nameof(values));
            }

            UiButton row = UiFactory.Button(name, parent, label, style == UiControlStyle.Brush ? UiButtonVariant.Brush : UiButtonVariant.Secondary, null);
            var host = row.gameObject.AddComponent<UiStepper>();
            host._style = style;
            host.Build(row, values, index, wrap, changed);
            return host;
        }

        private void Build(UiButton row, string[] values, int index, bool wrap, Action<int> changed)
        {
            _button = row;
            _values = values;
            _index = Mathf.Clamp(index, 0, values.Length - 1);
            _wrap = wrap;
            _changed = changed;
            if (_style == UiControlStyle.Brush)
            {
                BuildBrush(row, values);
                return;
            }

            _value = UiFactory.Label("Value", row.transform, string.Empty, UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            _value.enableWordWrapping = false;
            float widest = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                widest = Mathf.Max(widest, _value.GetPreferredValues(values[i]).x);
            }

            _valueWidth = Mathf.Max(MinValueWidth, Mathf.Ceil(widest) + (int)UiSpace.M);

            TMP_Text label = row.LabelText;
            label.alignment = TextAlignmentOptions.Left;
            label.fontSize = UiTokens.FontSize(UiType.Body);
            label.enableWordWrapping = false;
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2((int)UiSpace.XL - 4f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-(_valueWidth + ChevronSize * 2f + (int)UiSpace.S + (int)UiSpace.M), labelRect.offsetMax.y);

            _next = Chevron("Next", -(int)UiSpace.S, 0f, +1);
            RectTransform valueRect = _value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(_valueWidth, 0f);
            valueRect.anchoredPosition = new Vector2(-(int)UiSpace.S - ChevronSize, 0f);
            _previous = Chevron("Previous", -(int)UiSpace.S - ChevronSize - _valueWidth, 180f, -1);

            // Submit on the row steps forward (a pad player without ← / → still gets there).
            row.SetOnClick(() => Step(+1));
            Apply(false);
        }

        /// <summary>
        /// WP-098A P0b · the C16 row: label column | "◂ VALUE ▸" across the value column (the previous arrow at its left
        /// edge, the next arrow at the row's right edge, the value centred between). The value box is at least the column
        /// less the two arrows and grows - into the label column - to the widest value measured in MenuBody at the current
        /// text scale (U2: measured, never truncated).
        /// </summary>
        private void BuildBrush(UiButton row, string[] values)
        {
            var rowRect = (RectTransform)row.transform;
            rowRect.sizeDelta = new Vector2(UiBrushTokens.ListWidth, UiSlider.BrushRowHeight());
            float hit = UiTokens.TargetPreferred;

            _value = UiFactory.Label("Value", row.transform, string.Empty, UiType.MenuBody, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            _value.enableWordWrapping = false;
            float widest = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                widest = Mathf.Max(widest, _value.GetPreferredValues(values[i]).x);
            }

            _valueWidth = Mathf.Max(UiBrushTokens.ValueColumn - 2f * hit, Mathf.Ceil(widest) + (int)UiSpace.M);
            float column = _valueWidth + 2f * hit;

            TMP_Text label = row.LabelText;
            UiBrushKit.RestyleText(label, UiType.MenuBody);
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2(0f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-(column + UiBrushKit.ValueColumnGap), labelRect.offsetMax.y);

            _next = BrushArrow("Next", -hit * 0.5f, CycleRightIcon, 0f, +1, hit);
            RectTransform valueRect = _value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(_valueWidth, 0f);
            valueRect.anchoredPosition = new Vector2(-hit, 0f);
            _previous = BrushArrow("Previous", -(column - hit * 0.5f), CycleLeftIcon, 180f, -1, hit);

            row.SetOnClick(() => Step(+1));   // Submit on the row goes to the next value (KILAVUZ §4.3 C16)
            Apply(false);
        }

        /// <summary>A <paramref name="hit"/> px transparent hit box centred at <paramref name="x"/> (from the row's right edge) with the O1 glyph at 24.</summary>
        private Image BrushArrow(string name, float x, string iconKey, float fallbackRotation, int direction, float hit)
        {
            Image box = UiFactory.NewImage(name, _button.transform, UiColor.Transparent, UiSprites.Solid);
            box.raycastTarget = true;
            var rect = (RectTransform)box.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(hit, hit);
            rect.anchoredPosition = new Vector2(x, 0f);
            box.gameObject.AddComponent<UiStepperArrow>().Configure(this, direction);

            float size = (int)UiIconSize.S;
            Image glyph;
            if (UiArt.HasIcon(iconKey))
            {
                glyph = UiArt.IconImage("Glyph", rect, iconKey, UiColor.TextSecondary, size);
            }
            else
            {
                // No O1 glyph in this build: the procedural chevron (points right; the centre pivot turns it in place).
                glyph = UiFactory.NewImage("Glyph", rect, UiColor.TextSecondary, UiSprites.Chevron);
                glyph.preserveAspect = true;
                ((RectTransform)glyph.transform).sizeDelta = new Vector2(size, size);
                glyph.transform.localRotation = Quaternion.Euler(0f, 0f, fallbackRotation);
            }

            return glyph;
        }

        /// <summary>WP-098A Brush · the pointer is over (or left) an arrow: that arrow reads TextPrimary (KILAVUZ §4.1 C16 hover). V3: nothing.</summary>
        internal void SetArrowHover(int direction, bool over)
        {
            if (_style != UiControlStyle.Brush)
            {
                return;
            }

            int next = over ? direction : (_hoverDirection == direction ? 0 : _hoverDirection);
            if (next != _hoverDirection)
            {
                _hoverDirection = next;
                ApplyArrows();
            }
        }

        /// <summary>
        /// WP-098A · enable / disable the row and its parts together (Brush: label, value and arrows go TextSecondary).
        /// V3 rows keep their own disabled look through the UiButton.
        /// </summary>
        public void SetInteractable(bool value)
        {
            _button.SetInteractable(value);
            if (_style == UiControlStyle.Brush)
            {
                Apply(false);
            }
        }

        /// <summary>A 44 px transparent hit box with a 14 px chevron glyph at its centre; returns the glyph.</summary>
        private Image Chevron(string name, float x, float rotation, int direction)
        {
            Image hit = UiFactory.NewImage(name, _button.transform, UiColor.Transparent, UiSprites.Solid);
            hit.raycastTarget = true;
            var rect = (RectTransform)hit.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ChevronSize, ChevronSize);
            rect.anchoredPosition = new Vector2(x - ChevronSize * 0.5f, 0f);
            hit.gameObject.AddComponent<UiStepperArrow>().Configure(this, direction);

            Image glyph = UiFactory.NewImage("Glyph", rect, UiColor.TextSecondary, UiSprites.Chevron);
            glyph.preserveAspect = true;
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.sizeDelta = new Vector2(ChevronGlyph, ChevronGlyph);
            glyphRect.localRotation = Quaternion.Euler(0f, 0f, rotation); // UiSprites.Chevron points right; centre pivot turns in place
            return glyph;
        }

        /// <summary>← / → while the row has focus.</summary>
        public void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left)
            {
                Step(-1);
                eventData.Use();
            }
            else if (eventData.moveDir == MoveDirection.Right)
            {
                Step(+1);
                eventData.Use();
            }
        }

        public void Step(int direction)
        {
            int count = _values.Length;
            int next = _index + direction;
            if (_wrap)
            {
                next = ((next % count) + count) % count;
            }
            else if (next < 0 || next >= count)
            {
                UiSound.Play(UiSoundKind.Denied);
                if (_style == UiControlStyle.Brush)
                {
                    // The arrow, never the word; a slide still running lands first, so the shake starts from rest.
                    var end = (RectTransform)(direction < 0 ? _previous : _next).transform;
                    UiMotion.KillTarget(end, true);
                    UiPresets.DeniedNudge(end);
                }

                return;
            }

            if (next == _index)
            {
                return;
            }

            _index = next;
            Apply(true);
            if (_style == UiControlStyle.Brush)
            {
                // The arrow slides 4 px its way and settles back (Decor: under Reduced Motion it lands at rest at once).
                var arrow = (RectTransform)(direction < 0 ? _previous : _next).transform;
                UiMotion.MoveSeconds(arrow, new Vector2(direction * (int)UiSpace.XS, 0f), Vector2.zero,
                    UiMotionTokens.Seconds(UiDuration.Fast), UiEase.Settle, UiMotionClass.Decor);
            }

            UiEvents.Raise(UiEvent.SliderChanged, name);
            if (_changed != null)
            {
                _changed(_index);
            }
        }

        /// <summary>Set without notifying.</summary>
        public void Set(int index, bool animate)
        {
            _index = Mathf.Clamp(index, 0, _values.Length - 1);
            Apply(animate);
        }

        private void Apply(bool animate)
        {
            _value.text = _values[_index];
            if (animate)
            {
                _value.canvasRenderer.SetAlpha(0f);
                UiMotion.TintAlpha(_value, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            }

            if (_style == UiControlStyle.Brush)
            {
                Color value = UiTokens.Get(_button.IsInteractable() ? UiColor.TextPrimary : UiColor.TextSecondary);
                if (_value.color != value) _value.color = value;
                ApplyArrows();
                return;
            }

            bool atStart = !_wrap && _index == 0;
            bool atEnd = !_wrap && _index == _values.Length - 1;
            _previous.color = WithAlpha(UiTokens.Get(UiColor.TextSecondary), atStart ? 0.35f : 1f);
            _next.color = WithAlpha(UiTokens.Get(UiColor.TextSecondary), atEnd ? 0.35f : 1f);
        }

        /// <summary>WP-098A Brush · arrow tints: TextSecondary, TextPrimary under the pointer, an end arrow at OpacityDisabled.</summary>
        private void ApplyArrows()
        {
            bool usable = _button.IsInteractable();
            bool atStart = !_wrap && _index == 0;
            bool atEnd = !_wrap && _index == _values.Length - 1;
            _previous.color = WithAlpha(UiTokens.Get(usable && _hoverDirection < 0 ? UiColor.TextPrimary : UiColor.TextSecondary), atStart ? UiTokens.OpacityDisabled : 1f);
            _next.color = WithAlpha(UiTokens.Get(usable && _hoverDirection > 0 ? UiColor.TextPrimary : UiColor.TextSecondary), atEnd ? UiTokens.OpacityDisabled : 1f);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }

    /// <summary>
    /// Pointer target of a stepper chevron (keyboard / pad use ← / → on the row). WP-098A: it also tells a Brush row which
    /// arrow the pointer is over (enter / exit reach every ancestor anyway, so the row's own hover is unchanged).
    /// </summary>
    public sealed class UiStepperArrow : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private UiStepper _owner;
        private int _direction;

        public void Configure(UiStepper owner, int direction)
        {
            _owner = owner;
            _direction = direction;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.Step(_direction);
                eventData.Use();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.SetArrowHover(_direction, true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.SetArrowHover(_direction, false);
            }
        }
    }
}
