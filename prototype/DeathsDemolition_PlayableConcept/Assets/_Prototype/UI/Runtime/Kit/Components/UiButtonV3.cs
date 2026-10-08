using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>Button heights (direction §4 #1): L 56 (a screen's one primary CTA), M 48 (default), S 40 (dense rows, the 1408 binding page). Every one keeps the 44 px hit rule except S, which pads its hit rect.</summary>
    public enum UiButtonSize
    {
        L = 56,
        M = 48,
        S = 40
    }

    /// <summary>Hold state after a tick.</summary>
    public enum UiHoldState
    {
        Idle = 0,
        Running = 1,
        Completed = 2,
        Cancelled = 3
    }

    /// <summary>
    /// The hold rule as a value (testable without a frame loop): begin at a time, tick with
    /// "still held?", complete at the duration, cancel on an early release.
    /// </summary>
    public struct UiHoldTimer
    {
        public float Seconds;
        public float StartedAt;
        public UiHoldState State;

        public void Begin(float now, float seconds)
        {
            Seconds = Mathf.Max(0.01f, seconds);
            StartedAt = now;
            State = UiHoldState.Running;
        }

        /// <summary>0..1 of the hold so far.</summary>
        public float Progress(float now)
        {
            return State == UiHoldState.Running ? Mathf.Clamp01((now - StartedAt) / Seconds) : State == UiHoldState.Completed ? 1f : 0f;
        }

        public UiHoldState Tick(float now, bool held)
        {
            if (State != UiHoldState.Running)
            {
                return State;
            }

            if (now - StartedAt >= Seconds)
            {
                State = UiHoldState.Completed;
            }
            else if (!held)
            {
                State = UiHoldState.Cancelled;
            }

            return State;
        }
    }

    /// <summary>
    /// WP-068 slice 5 · component #1 UiButton v3 (direction §4) - the additions on top of the
    /// v2 state matrix in UiButton.cs:
    ///  · pressed = the darker fill token + a 2 px inner bevel at the top edge (no scale - AD);
    ///  · disabled keeps the audited disabled pair and can carry a REASON caption
    ///    (<see cref="SetDisabledReason"/>: "LOCKED - REACH ROUND 8", "NOT ENOUGH GOLD");
    ///  · Ghost (word only) and Icon (44² glyph) variants; L / M / S heights;
    ///  · Hold (<see cref="ConfigureHold"/>): the action runs when a press is HELD for the
    ///    duration (RESET 1 s, REROLL 0.5 s - U5); a Linear fill shows the hold, an early
    ///    release drains it (Back cue) and does nothing.
    ///
    /// USAGE: every clickable control. Hold for destructive or costly actions that must
    /// not happen by a stray press.
    /// DON'T: put Danger outside a modal; use Hold for a frequent action; scale a button on
    /// press (text jitters - the bevel is the press).
    /// ACCESSIBILITY: 44 px hit area (S pads its hit rect); focus ring on frame 1 + the
    /// travelling brackets; a disabled control still reads (4.5:1) and says why; the hold
    /// fill is Data (Reduced Motion never shortens or skips a hold) and works with Submit
    /// held on keyboard / pad as well as the mouse.
    /// </summary>
    public sealed partial class UiButton
    {
        public const float BevelHeight = 2f;
        public const float HoldEdgeWidth = 2f;

        /// <summary>Inset of the bevel, the hold fill and its edge: just inside the AP-012 button frame (~5 px).</summary>
        public const float FrameInset = UiTokens.StrokeControl + 3f;

        private Image _bevel;
        private TMP_Text _reason;
        private string _reasonText;
        private Image _holdFill;
        private RectTransform _holdEdge;
        private float _holdSeconds;
        private UiHoldTimer _hold;
        private bool _pointerHeld;
        private Coroutine _holdRoutine;
        private bool _lifted;

        /// <summary>Called with true when the control becomes hovered or focused, false when it stops (a <see cref="UiCard"/> lifts with it).</summary>
        public Action<bool> LiftChanged;

        /// <summary>True when this control acts on a completed hold (<see cref="ConfigureHold"/>).</summary>
        public bool IsHoldButton
        {
            get { return _holdSeconds > 0f; }
        }

        /// <summary>Hold readout for tests and captures.</summary>
        public UiHoldState HoldState
        {
            get { return _hold.State; }
        }

        public Image HoldFill
        {
            get { return _holdFill; }
        }

        public bool BevelVisible
        {
            get { return _bevel != null && _bevel.gameObject.activeSelf; }
        }

        public string DisabledReasonShown
        {
            get { return _reason != null && _reason.gameObject.activeSelf ? _reason.text : string.Empty; }
        }

        /// <summary>Make this a hold control: the click handler runs after <paramref name="seconds"/> of continuous press.</summary>
        public void ConfigureHold(float seconds)
        {
            _holdSeconds = Mathf.Max(0.1f, seconds);
            if (_holdFill == null)
            {
                // HoldFill token (Cream100 0.25, AD 5b MUST-2), Solid and inset past the frame (a
                // Filled image cannot 9-slice); a 2 px InkStroke leading edge so the progress edge
                // reads even where the sweep is faint. The label never changes colour.
                _holdFill = UiFactory.NewImage("HoldFill", transform, UiColor.HoldFill, UiSprites.Solid);
                UiFactory.Fill((RectTransform)_holdFill.transform, FrameInset);
                _holdFill.type = Image.Type.Filled;
                _holdFill.fillMethod = Image.FillMethod.Horizontal;
                _holdFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                _holdFill.fillAmount = 0f;
                Image edge = UiFactory.NewImage("HoldEdge", transform, UiColor.InkStroke, UiSprites.Solid);
                _holdEdge = (RectTransform)edge.transform;
                _holdEdge.anchorMin = new Vector2(0f, 0f);
                _holdEdge.anchorMax = new Vector2(0f, 1f);
                _holdEdge.pivot = new Vector2(1f, 0.5f);
                _holdEdge.sizeDelta = new Vector2(HoldEdgeWidth, -FrameInset * 2f);
                _holdEdge.gameObject.SetActive(false);
                if (_label != null)
                {
                    _label.transform.SetAsLastSibling();
                }
            }
        }

        /// <summary>The reason shown while the control is disabled (a Caption at the right edge); null/empty = none.</summary>
        public void SetDisabledReason(string reason)
        {
            _reasonText = reason;
            if (!string.IsNullOrEmpty(reason) && _reason == null)
            {
                _reason = UiFactory.Label("Reason", transform, string.Empty, UiType.Caption, UiColor.TextSecondary, TextAnchor.MiddleRight);
                RectTransform rect = UiFactory.Fill(_reason.rectTransform);
                rect.offsetMin = new Vector2((int)UiSpace.L, 0f);
                rect.offsetMax = new Vector2(-(int)UiSpace.L, 0f);
            }

            if (_reason != null)
            {
                _reason.text = reason ?? string.Empty;
            }

            if (_configured)
            {
                RefreshV3(IsInteractable(), false, _lifted);
            }
        }

        /// <summary>
        /// Back to a plain press - for a control whose action changes with its state (M-34: the tree's PIN is a press,
        /// the same control's UNPIN a hold). A running hold stops without acting.
        /// </summary>
        public void ClearHold()
        {
            if (_holdSeconds <= 0f)
            {
                return;
            }

            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
                _holdRoutine = null;
            }

            _hold.State = UiHoldState.Idle;
            _holdSeconds = 0f;
            if (_holdFill != null)
            {
                UiMotion.KillTarget(_holdFill, false);
                _holdFill.fillAmount = 0f;
            }

            if (_holdEdge != null)
            {
                _holdEdge.gameObject.SetActive(false);
            }
        }

        /// <summary>Show a hold at <paramref name="progress"/> without running one (kit sheet, captures).</summary>
        public void SetHoldPreview(float progress)
        {
            if (_holdFill == null)
            {
                return;
            }

            _holdFill.fillAmount = Mathf.Clamp01(progress);
            PlaceHoldEdge();
        }

        /// <summary>The leading edge sits where the sweep ends (hidden at 0 and 1).</summary>
        private void PlaceHoldEdge()
        {
            if (_holdEdge == null)
            {
                return;
            }

            float fill = _holdFill.fillAmount;
            bool show = fill > 0.001f && fill < 0.999f;
            if (_holdEdge.gameObject.activeSelf != show)
            {
                _holdEdge.gameObject.SetActive(show);
            }

            if (show)
            {
                float width = ((RectTransform)transform).rect.width - FrameInset * 2f;
                _holdEdge.anchoredPosition = new Vector2(FrameInset + width * fill, 0f);
            }
        }

        /// <summary>Start a hold (pointer down, or Submit on keyboard / pad).</summary>
        private void BeginHold(bool fromSubmit)
        {
            if (!IsActive() || _hold.State == UiHoldState.Running)
            {
                return;
            }

            if (!IsInteractable())
            {
                UiSound.Play(UiSoundKind.Denied);
                return;
            }

            _hold.Begin(Time.unscaledTime, _holdSeconds);
            if (_holdFill != null)
            {
                UiMotion.FillSeconds(_holdFill, 0f, 1f, _holdSeconds, UiEase.Linear, UiMotionClass.Data, 0f);
            }

            if (_holdRoutine != null)
            {
                StopCoroutine(_holdRoutine);
            }

            _holdRoutine = StartCoroutine(HoldLoop(fromSubmit));
        }

        private IEnumerator HoldLoop(bool fromSubmit)
        {
            while (true)
            {
                bool held = IsInteractable() && (fromSubmit ? UiInput.SubmitHeld() : _pointerHeld);
                UiHoldState state = _hold.Tick(Time.unscaledTime, held);
                PlaceHoldEdge();
                if (state == UiHoldState.Completed)
                {
                    _holdRoutine = null;
                    CompleteHold();
                    yield break;
                }

                if (state == UiHoldState.Cancelled)
                {
                    _holdRoutine = null;
                    CancelHold();
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>Tests: drive the hold without a frame loop (<paramref name="held"/> = press still down).</summary>
        public UiHoldState TickHoldForTests(float now, bool held)
        {
            UiHoldState state = _hold.Tick(now, held);
            if (state == UiHoldState.Completed)
            {
                CompleteHold();
            }
            else if (state == UiHoldState.Cancelled)
            {
                CancelHold();
            }

            return state;
        }

        /// <summary>Tests: begin a hold at an explicit time.</summary>
        public void BeginHoldForTests(float now)
        {
            _hold.Begin(now, _holdSeconds);
        }

        private void CompleteHold()
        {
            _hold.State = UiHoldState.Idle;
            if (_holdFill != null)
            {
                UiMotion.KillTarget(_holdFill, false);
                _holdFill.fillAmount = 0f;
                PlaceHoldEdge();
            }

            Activate();
        }

        private void CancelHold()
        {
            _hold.State = UiHoldState.Idle;
            if (_holdFill != null)
            {
                UiPresets.HoldCancel(_holdFill);
                if (_holdEdge != null)
                {
                    _holdEdge.gameObject.SetActive(false); // the sweep drains; the edge goes at once
                }
            }

            UiSound.Play(UiSoundKind.Back);
        }

        /// <summary>Called at the end of every Refresh: the bevel, the reason caption, the lift callback.</summary>
        private void RefreshV3(bool enabledControl, bool pressed, bool lifted)
        {
            if (_lifted != lifted)
            {
                _lifted = lifted;
                if (_variant == UiButtonVariant.Brush)
                {
                    BrushLift(lifted);   // WP-098A M8: the face's 2 px and the ink wave behind it (UiButtonMotion.cs)
                }

                if (LiftChanged != null)
                {
                    LiftChanged(lifted);
                }
            }

            if (_pressShown != pressed)
            {
                _pressShown = pressed;
                PressChanged?.Invoke(pressed);   // WP-098A M9: pointer down or a Submit pulse - the icon's press track
            }

            // WP-098A: a Brush control's press is its depth (UiButtonBrush.cs), never the bevel.
            bool bevel = pressed && _variant != UiButtonVariant.Brush;
            if (bevel && _bevel == null)
            {
                _bevel = UiFactory.NewImage("Bevel", transform, UiColor.ShadowKey, UiSprites.Solid);
                var rect = (RectTransform)_bevel.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                // Just inside the frame: the AP-012 button frame is ~5 px (FrameInset), the same
                // inset as the hold fill - at StrokeControl the bevel darkened the frame (b2).
                float inset = FrameInset;
                rect.sizeDelta = new Vector2(-inset * 2f, BevelHeight);
                rect.anchoredPosition = new Vector2(0f, -inset);
                Color c = _bevel.color;
                c.a = 0.6f;
                _bevel.color = c;
                if (_label != null)
                {
                    _bevel.transform.SetSiblingIndex(_label.transform.GetSiblingIndex());
                }
            }

            if (_bevel != null && _bevel.gameObject.activeSelf != bevel)
            {
                _bevel.gameObject.SetActive(bevel); // PressDepth: Instant, no scale
            }

            if (_reason != null)
            {
                bool showReason = !enabledControl && !string.IsNullOrEmpty(_reasonText);
                if (_reason.gameObject.activeSelf != showReason)
                {
                    _reason.gameObject.SetActive(showReason);
                }
            }
        }
    }

    /// <summary>WP-068 slice 5: button sizes and the Icon button (component #1).</summary>
    public static class UiButtons
    {
        public const float IconSize = 44f;
        public const float IconGlyph = 24f; // v007 floor for chip-sized glyphs (AD 5b SHOULD-1)

        public static float Height(UiButtonSize size)
        {
            return (int)size;
        }

        /// <summary>A 44² glyph button. <paramref name="accessibleName"/> names it (object name, events); no visible word.</summary>
        public static UiButton Icon(string accessibleName, Transform parent, string iconKey, Action onClick)
        {
            UiButton button = UiFactory.Button(accessibleName, parent, string.Empty, UiButtonVariant.Icon, onClick);
            ((RectTransform)button.transform).sizeDelta = new Vector2(IconSize, IconSize);
            if (UiArt.HasIcon(iconKey))
            {
                Image glyph = UiArt.IconImage("Glyph", button.transform, iconKey, UiColor.TextPrimary, IconGlyph);
                glyph.transform.SetAsLastSibling();
            }

            return button;
        }
    }
}
