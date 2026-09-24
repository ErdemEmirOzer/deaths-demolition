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

        public static UiSwitch Create(string name, Transform parent, string label, bool value, Action<bool> changed)
        {
            UiButton row = UiFactory.Button(name, parent, label, UiButtonVariant.Secondary, null);
            var host = row.gameObject.AddComponent<UiSwitch>();
            host.Build(row, value, changed);
            return host;
        }

        private void Build(UiButton row, bool value, Action<bool> changed)
        {
            _button = row;
            _changed = changed;
            _value = value;

            TMP_Text label = row.LabelText;
            label.alignment = TextAlignmentOptions.Left;
            label.fontSize = UiTokens.FontSize(UiType.Body);
            RectTransform labelRect = label.rectTransform;
            labelRect.offsetMin = new Vector2((int)UiSpace.XL - 4f, labelRect.offsetMin.y);
            labelRect.offsetMax = new Vector2(-(TrackWidth + WordWidth + (int)UiSpace.L + (int)UiSpace.S), labelRect.offsetMax.y);

            _track = UiFactory.NewImage("Track", row.transform, UiColor.SurfaceInset, UiSprites.Chip);
            var trackRect = (RectTransform)_track.transform;
            trackRect.anchorMin = new Vector2(1f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(TrackWidth, TrackHeight);
            trackRect.anchoredPosition = new Vector2(-(int)UiSpace.L - WordWidth - (int)UiSpace.S, 0f);

            // The track's own edge (1 px BorderStrong rim): the OFF track is an inset well on a
            // dark row, and a well without an edge disappears (kit sheet k1).
            Image rim = UiFactory.NewImage("Rim", trackRect, UiColor.BorderStrong, UiSprites.ChipRim);
            UiFactory.Fill((RectTransform)rim.transform);

            Image knob = UiFactory.NewImage("Knob", trackRect, UiColor.TextPrimary, UiSprites.Disc);
            _knob = (RectTransform)knob.transform;
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
            wordRect.sizeDelta = new Vector2(WordWidth, 0f);
            wordRect.anchoredPosition = new Vector2(-(int)UiSpace.L, 0f);

            _word.transform.SetAsLastSibling();
            row.SetOnClick(Toggle);
            Apply(false);
        }

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
            _word.text = _value ? "ON" : "OFF";
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
    }
}
