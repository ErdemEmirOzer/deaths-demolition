using System;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU M8 / M9 / M10, the ikon v2 council's kit gaps) · UiButton's motion half:
    ///  · <see cref="PressPulse"/>: Submit on keyboard / pad shows the press the pointer shows - the pressed state for Instant 80
    ///    (V3: the darker fill + bevel; Brush: the 0.98 depth), released by a silent timeline cue (no Update, no coroutine).
    ///    Not motion: it holds under Reduced Motion too (the RM equivalent of the icon's press is this wash + bevel).
    ///  · <see cref="PressChanged"/> (pointer down or a pulse), <see cref="ChoiceChanged"/>, <see cref="InteractableChanged"/>:
    ///    the state the icon tracks follow (UiIconButton), raised at event time only.
    ///  · <see cref="LastActivationFromPointer"/> / <see cref="LastPointerPosition"/>: where a confirm came from - the splash
    ///    lands at the pointer, or at the control's centre for a pad / keyboard confirm.
    ///  · Brush controls (M8): on lift the word and its glyph rise <see cref="UiMenuMotionTokens.HoverLiftPx"/> (a whole pixel, no
    ///    scale) and an ink wave ripples behind (<see cref="UiInkWave"/>); an icon control's glyph has its own pose (UiIconMotion).
    ///
    /// USAGE: nothing to call for the press and the lift - Submit pulses itself, Brush controls lift themselves; listen to the
    /// events for a control that moves something of its own (UiIconButton).
    /// DON'T: assign the events with <c>=</c> (add with <c>+=</c>: the icon and the screen both listen), or scale a text control.
    /// ACCESSIBILITY: keyboard / pad presses show the same press as the pointer (WCAG 2.1.1 parity); the lift and the wave are
    /// Decor and absent under Reduced Motion, the press look is not motion and stays.
    /// </summary>
    public sealed partial class UiButton : IUiMotionListener
    {
        private const int PulseDone = 0x5055;

        private bool _pulse;
        private bool _pressShown;
        private bool _faceCaptured;
        private Vector2 _faceRest;
        private Vector2 _glyphRest;
        private UiInkWave _wave;

        /// <summary>True when the pressed look shows (pointer held or a Submit pulse) - raised on each change.</summary>
        public Action<bool> PressChanged;

        /// <summary>The choice flag changed (<see cref="SetSelectedChoice"/>).</summary>
        public Action<bool> ChoiceChanged;

        /// <summary>The control became usable / unusable through <see cref="SetInteractable"/>.</summary>
        public Action<bool> InteractableChanged;

        /// <summary>The last activation was a pointer click (else Submit on keyboard / pad).</summary>
        public bool LastActivationFromPointer { get; private set; }

        /// <summary>The pointer's screen position at the last click (px).</summary>
        public Vector2 LastPointerPosition { get; private set; }

        /// <summary>An icon control drives its glyph's own pose (UiIconMotion): the Brush face lift leaves it alone.</summary>
        public bool IconDriven { get; internal set; }

        /// <summary>Hovered or focused now (the icon's composite rest reads it).</summary>
        public bool IsLifted
        {
            get { return _lifted; }
        }

        /// <summary>The Submit pulse is holding the pressed look (tests).</summary>
        public bool Pulsing
        {
            get { return _pulse; }
        }

        /// <summary>
        /// M9 · the pressed look for Instant 80 ms from a Submit (or a test); the release is a silent timeline cue. With no free
        /// timeline slot the press shows for this frame only.
        /// </summary>
        public void PressPulse()
        {
            if (!_configured)
            {
                return;
            }

            _pulse = true;
            Refresh(false);
            UiTimeline timeline = UiMotion.Timeline(GetInstanceID(), this);
            if (!timeline.IsValid)
            {
                EndPulse();
                return;
            }

            timeline.Callback(UiMotionTokens.Seconds(UiDuration.Instant), PulseDone);
        }

        void IUiMotionListener.OnCue(int cueId)
        {
            if (cueId == PulseDone && this != null)
            {
                EndPulse();
            }
        }

        private void EndPulse()
        {
            if (!_pulse)
            {
                return;
            }

            _pulse = false;
            if (_configured && this != null)
            {
                Refresh(false);
            }
        }

        /// <summary>M8 · a Brush control's face (word + glyph) rises 2 px on lift and settles back; the ink wave behind it. Not under RM.</summary>
        private void BrushLift(bool lifted)
        {
            if (IconDriven || _label == null)
            {
                return;
            }

            if (!_faceCaptured)
            {
                _faceRest = _label.rectTransform.anchoredPosition;
                _glyphRest = _brushGlyph != null ? _brushGlyph.rectTransform.anchoredPosition : Vector2.zero;
                _faceCaptured = true;
            }

            bool up = lifted && !UiMotion.ReducedMotion;
            UiPresets.HoverLift(_label.rectTransform, _faceRest, up);
            if (_brushGlyph != null)
            {
                UiPresets.HoverLift(_brushGlyph.rectTransform, _glyphRest, up);
            }

            if (_wave == null && up)
            {
                _wave = UiInkWave.Attach((RectTransform)transform);   // once per control, on its first lift
            }

            UiPresets.InkWave(_wave, lifted);
        }

        protected override void OnDestroy()
        {
            UiMotion.KillOwner(GetInstanceID(), false);   // a pulse's release cue goes with the control
            base.OnDestroy();
        }
    }
}
