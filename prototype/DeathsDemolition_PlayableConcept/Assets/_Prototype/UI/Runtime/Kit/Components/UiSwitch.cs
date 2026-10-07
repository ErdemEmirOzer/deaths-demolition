using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #3 UiSwitch (direction §4): a settings row whose value is
    /// ON / OFF - the label on the left, a 44×24 track with a 20 px knob and the word on the
    /// right. The row is a <see cref="UiButton"/> (focus ring, hover, press, Submit toggles).
    ///
    /// USAGE: two-state preferences (Reduce Flashes, Reduced Motion, Rumble, Telemetry, Hints).
    /// DON'T: use it for more than two values (that is <see cref="UiStepper"/>) or for an
    /// action (that is a button); don't colour-code the state without the word.
    /// ACCESSIBILITY: the state is the knob POSITION + the word ON/OFF, never colour alone
    /// (colour-blind palette); the whole row is the 44+ px target; the knob moves on the
    /// UiMotion core (Move 140, Decor - Reduced Motion lands it) and the word changes at once;
    /// the track keeps a 1 px edge in both states.
    ///
    /// WP-098A P0b (C-00 07.10) · C15 Brush style (<see cref="UiControlStyle.Brush"/>, KILAVUZ §4.1, KIT-API §4 "Ayar
    /// satırları"): a frameless <see cref="UiButtonVariant.Brush"/> row - the MenuBody label in the label column, then a
    /// 40×8 brush pill and the word in the value column. ON: the pill filled cream, the word TextPrimary; OFF: the pill a
    /// hollow TextSecondary outline, the word TextSecondary - the state is the pill's SHAPE (filled / hollow) and the word,
    /// never its colour. The two pills cross-fade over Fast (Decor - Reduced Motion cuts); no knob, no track. Disabled
    /// (<see cref="SetInteractable"/>): everything TextSecondary. V3 (the default) is unchanged.
    /// </summary>
    public sealed class UiSwitch : MonoBehaviour
    {
        public const float TrackWidth = 44f;
        public const float TrackHeight = 24f;
        public const float KnobSize = 20f;
        public const float WordWidth = 44f;

        private UiButton _button;
        private Image _track;
        private RectTransform _knob;
        private TMP_Text _word;
        private bool _value;
        private Action<bool> _changed;

        // WP-098A P0b · Brush style only.
        private UiControlStyle _style;
        private Image _pillOn;
        private Image _pillOff;
        private bool _built;

        /// <summary>WP-098A: the dialect this row is drawn in.</summary>
        public UiControlStyle Style
        {
            get { return _style; }
        }

        /// <summary>WP-098A Brush: the filled pill's alpha (1 = ON shows; tests read the cross-fade's end state).</summary>
        public float PillOnAlpha
        {
            get { return _pillOn != null ? _pillOn.canvasRenderer.GetAlpha() : 0f; }
        }

        /// <summary>WP-098A Brush: the hollow pill's alpha (1 = OFF shows).</summary>
        public float PillOffAlpha
        {
            get { return _pillOff != null ? _pillOff.canvasRenderer.GetAlpha() : 0f; }
        }

        /// <summary>WP-098A: the row label and the word (tests: T3 / U2 at TextScale 1.25).</summary>
        public TMP_Text Label
        {
            get { return _button != null ? _button.LabelText : null; }
        }

        public TMP_Text Word
        {
            get { return _word; }
        }

        public UiButton Button
        {
            get { return _button; }
        }

        public bool Value
        {
            get { return _value; }
        }

        public string WordText
        {
            get { return _word != null ? _word.text : string.Empty; }
        }

        /// <summary>Knob x offset inside the track for the current value (test readout).</summary>
        public float KnobX
        {
            get { return _knob != null ? _knob.anchoredPosition.x : 0f; }
        }

        /// <summary>
        /// A switch row. WP-098A: <paramref name="style"/> Brush draws the C15 row (<see cref="UiBrushKit.Toggle"/> is its
        /// entry point; the row is <see cref="UiBrushTokens.ListWidth"/> wide until the caller sizes it); V3 is today's row.
        /// </summary>
        public static UiSwitch Create(string name, Transform parent, string label, bool value, Action<bool> changed,
            UiControlStyle style = UiControlStyle.V3)
        {
            UiButton row = UiFactory.Button(name, parent, label, style == UiControlStyle.Brush ? UiButtonVariant.Brush : UiButtonVariant.Secondary, null);
            var host = row.gameObject.AddComponent<UiSwitch>();
            host._style = style;
            host.Build(row, value, changed);
            return host;
        }

        private void Build(UiButton row, bool value, Action<bool> changed)
        {
            _button = row;
            _changed = changed;
            _value = value;
            if (_style == UiControlStyle.Brush)
            {
                BuildBrush(row);
                _built = true;
                Apply(false);
                return;
            }

            TMP_Text label = row.LabelText;
            label.alignment = TextAlignmentOptions.Left;
            label.fontSize = UiTokens.FontSize(UiType.Body);
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2((int)UiSpace.XL - 4f, labelRect.offsetMin.y);

            _track = UiFactory.NewImage("Track", row.transform, UiColor.SurfaceInset, UiSprites.Chip);
            var trackRect = (RectTransform)_track.transform;
            trackRect.anchorMin = new Vector2(1f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(TrackWidth, TrackHeight);

            // The track's own edge (1 px BorderStrong rim): the OFF track is an inset well on a
            // dark row, and a well without an edge disappears (kit sheet k1).
            Image rim = UiFactory.NewImage("Rim", trackRect, UiColor.BorderStrong, UiSprites.ChipRim);
            UiFactory.Fill((RectTransform)rim.transform);

            // Knob = 2 px ink edge + cream core: a cream disc alone on the ON track is 1.82:1
            // (Jade) / 2.17:1 (CB Indigo) - T1; the ink edge is 7.6 / 6.4:1 against either.
            Image knob = UiFactory.NewImage("Knob", trackRect, UiColor.SurfacePanel, UiSprites.Disc);
            _knob = (RectTransform)knob.transform;
            Image core = UiFactory.NewImage("Core", _knob, UiColor.TextPrimary, UiSprites.Disc);
            UiFactory.Fill((RectTransform)core.transform, 2f);
            _knob.anchorMin = new Vector2(0.5f, 0.5f);
            _knob.anchorMax = new Vector2(0.5f, 0.5f);
            _knob.pivot = new Vector2(0.5f, 0.5f);
            _knob.sizeDelta = new Vector2(KnobSize, KnobSize);

            _word = UiFactory.Label("Word", row.transform, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            _word.fontStyle = FontStyles.Bold;
            RectTransform wordRect = _word.rectTransform;
            wordRect.anchorMin = new Vector2(1f, 0f);
            wordRect.anchorMax = new Vector2(1f, 1f);
            wordRect.pivot = new Vector2(1f, 0.5f);
            // AD d109 SHOULD-7: the word is the language's (common.on / common.off - TR "AÇIK" / "KAPALI"), so its box is
            // the wider of the two words, never less than the English 44 px; the track and the label follow it.
            float wordWidth = Mathf.Max(WordWidth, Mathf.Ceil(Mathf.Max(_word.GetPreferredValues(OnWord()).x, _word.GetPreferredValues(OffWord()).x)));
            wordRect.sizeDelta = new Vector2(wordWidth, 0f);
            wordRect.anchoredPosition = new Vector2(-(int)UiSpace.L, 0f);
            trackRect.anchoredPosition = new Vector2(-(int)UiSpace.L - wordWidth - (int)UiSpace.S, 0f);
            labelRect.offsetMax = new Vector2(-(TrackWidth + wordWidth + (int)UiSpace.L + (int)UiSpace.S), labelRect.offsetMax.y);

            _word.transform.SetAsLastSibling();
            row.SetOnClick(Toggle);
            Apply(false);
        }

        /// <summary>
        /// WP-098A P0b · the C15 row: label column | pill + word (value column, from its left edge). The word box is the
        /// wider of the language's two words, measured in MenuBody at the current text scale.
        /// </summary>
        private void BuildBrush(UiButton row)
        {
            var rowRect = (RectTransform)row.transform;
            rowRect.sizeDelta = new Vector2(UiBrushTokens.ListWidth, UiSlider.BrushRowHeight());
            float valueColumn = UiBrushTokens.ValueColumn;

            TMP_Text label = row.LabelText;
            UiBrushKit.RestyleText(label, UiType.MenuBody);
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2(0f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-(valueColumn + UiBrushKit.ValueColumnGap), labelRect.offsetMax.y);

            // The pill: two images on one spot - the hollow outline (OFF) under the filled pill (ON); one shows at a time.
            Vector2 pillSize = new Vector2(UiBrushTokens.TogglePillWidth, UiBrushTokens.TogglePillHeight);
            _pillOff = Pill("PillOff", row.transform, UiColor.TextSecondary, UiSprites.ChipRim, pillSize, valueColumn);
            _pillOn = Pill("PillOn", row.transform, UiColor.TextPrimary, UiBrushArt.Get(UiBrushSprite.TogglePill), pillSize, valueColumn);

            _word = UiFactory.Label("Word", row.transform, string.Empty, UiType.MenuBody, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            _word.enableWordWrapping = false;
            RectTransform wordRect = _word.rectTransform;
            wordRect.anchorMin = new Vector2(1f, 0f);
            wordRect.anchorMax = new Vector2(1f, 1f);
            wordRect.pivot = new Vector2(0f, 0.5f);
            float wordWidth = Mathf.Ceil(Mathf.Max(_word.GetPreferredValues(OnWord()).x, _word.GetPreferredValues(OffWord()).x));
            wordRect.sizeDelta = new Vector2(wordWidth, 0f);
            wordRect.anchoredPosition = new Vector2(-valueColumn + pillSize.x + (int)UiSpace.M, 0f);

            _word.transform.SetAsLastSibling();
            row.SetOnClick(Toggle);
        }

        private static Image Pill(string name, Transform row, UiColor colour, Sprite sprite, Vector2 size, float valueColumn)
        {
            Image pill = UiFactory.NewImage(name, row, colour, sprite);
            var rect = (RectTransform)pill.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(-valueColumn, 0f);
            return pill;
        }

        /// <summary>
        /// WP-098A · enable / disable the row and its parts together (Brush: label, pill and word go TextSecondary - KILAVUZ
        /// §4.1 C15). V3 rows keep their own disabled look through the UiButton.
        /// </summary>
        public void SetInteractable(bool value)
        {
            _button.SetInteractable(value);
            if (_style == UiControlStyle.Brush)
            {
                Apply(false);
            }
        }

        private void OnEnable()
        {
            // A disable clears the CanvasRenderer alpha the pills cross-fade on: put the state back (Brush only).
            if (_built && _style == UiControlStyle.Brush)
            {
                Apply(false);
            }
        }

        private static string OnWord() => UiStrings.Get("common.on", "ON");

        private static string OffWord() => UiStrings.Get("common.off", "OFF");

        private float KnobTarget(bool on)
        {
            float travel = (TrackWidth - KnobSize) * 0.5f - 2f;
            return on ? travel : -travel;
        }

        /// <summary>Flip the value (Submit / click), animate, notify.</summary>
        public void Toggle()
        {
            Set(!_value, true);
            UiEvents.Raise(UiEvent.ToggleChanged, name);
            if (_changed != null)
            {
                _changed(_value);
            }
        }

        /// <summary>Set without notifying (a screen reflecting a value it read).</summary>
        public void Set(bool value, bool animate)
        {
            _value = value;
            Apply(animate);
        }

        private void Apply(bool animate)
        {
            if (_style == UiControlStyle.Brush)
            {
                ApplyBrush(animate);
                return;
            }

            _word.text = _value ? OnWord() : OffWord();
            _word.color = UiTokens.Get(_value ? UiColor.TextPrimary : UiColor.TextSecondary);
            _track.color = UiTokens.Get(_value ? UiColor.StateSuccess : UiColor.SurfaceInset);
            var to = new Vector2(KnobTarget(_value), 0f);
            if (animate)
            {
                UiMotion.Move(_knob, to, UiDuration.Fast, UiEase.Move, UiMotionClass.Decor);
            }
            else
            {
                UiMotion.KillTarget(_knob, false);
                _knob.anchoredPosition = to;
            }
        }

        /// <summary>WP-098A Brush · the word at once, the pills cross-fade (Fast, Settle, Decor); a colour is written only when it changes.</summary>
        private void ApplyBrush(bool animate)
        {
            bool usable = _button.IsInteractable();
            _word.text = _value ? OnWord() : OffWord();
            Color word = UiTokens.Get(_value && usable ? UiColor.TextPrimary : UiColor.TextSecondary);
            if (_word.color != word) _word.color = word;
            Color filled = UiTokens.Get(usable ? UiColor.TextPrimary : UiColor.TextSecondary);
            if (_pillOn.color != filled) _pillOn.color = filled;

            float on = _value ? 1f : 0f;
            if (animate)
            {
                UiMotion.TintAlpha(_pillOn, on, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                UiMotion.TintAlpha(_pillOff, 1f - on, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            }
            else
            {
                UiMotion.KillTarget(_pillOn, false);
                UiMotion.KillTarget(_pillOff, false);
                _pillOn.canvasRenderer.SetAlpha(on);
                _pillOff.canvasRenderer.SetAlpha(1f - on);
            }
        }
    }
}
