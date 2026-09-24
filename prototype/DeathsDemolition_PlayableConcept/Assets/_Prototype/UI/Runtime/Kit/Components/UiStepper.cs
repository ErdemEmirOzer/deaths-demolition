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
    /// </summary>
    public sealed class UiStepper : MonoBehaviour, IMoveHandler
    {
        /// <summary>The chevron hit box (44 px rule); the drawn glyph is <see cref="ChevronGlyph"/>.</summary>
        public const float ChevronSize = 44f;
        public const float ChevronGlyph = 14f;

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

        public static UiStepper Create(string name, Transform parent, string label, string[] values, int index, bool wrap, Action<int> changed)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("UiStepper needs at least one value.", nameof(values));
            }

            UiButton row = UiFactory.Button(name, parent, label, UiButtonVariant.Secondary, null);
            var host = row.gameObject.AddComponent<UiStepper>();
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
                return;
            }

            if (next == _index)
            {
                return;
            }

            _index = next;
            Apply(true);
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

            bool atStart = !_wrap && _index == 0;
            bool atEnd = !_wrap && _index == _values.Length - 1;
            _previous.color = WithAlpha(UiTokens.Get(UiColor.TextSecondary), atStart ? 0.35f : 1f);
            _next.color = WithAlpha(UiTokens.Get(UiColor.TextSecondary), atEnd ? 0.35f : 1f);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }

    /// <summary>Pointer target of a stepper chevron (keyboard / pad use ← / → on the row).</summary>
    public sealed class UiStepperArrow : MonoBehaviour, IPointerClickHandler
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
    }
}
