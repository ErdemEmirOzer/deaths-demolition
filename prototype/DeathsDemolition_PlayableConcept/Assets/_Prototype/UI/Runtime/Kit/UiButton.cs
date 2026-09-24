using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Every interactive control in the prototype UI.
    ///
    /// It derives from <see cref="Selectable"/> rather than from Button so that keyboard
    /// and gamepad navigation, the disabled state and the "currently focused" flag come
    /// from Unity, while the visual treatment stays entirely token driven -
    /// Selectable's own ColorTint transition multiplies a literal colour into the
    /// target graphic, which would put an unauditable value on screen.
    ///
    /// STATE MATRIX (all five are implemented, none is a colour tint of another):
    ///   Normal      fill / border / label per variant
    ///   Hover       + cream wash at UiTokens.OpacityHoverWash
    ///   Focused     + focus ring and halo, drawn OUTSIDE the control rect
    ///   Pressed     darker fill token, wash removed
    ///   Disabled    SurfaceRaised fill, hairline border, TextSecondary label
    ///               (the audited "Disabled button label" pair, 4.5:1 - a disabled
    ///               control is still readable, because a locked weapon row has to
    ///               explain why it is locked)
    ///
    /// Selected is a separate, orthogonal flag: it means "this is the current choice"
    /// (nav pane, skin card, weapon row), not "this has keyboard focus".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class UiButton : Selectable, IPointerClickHandler, ISubmitHandler
    {
        private UiButtonVariant _variant;

        private GameObject _halo;
        private GameObject _ring;
        private Image _border;
        private Image _fill;
        private Image _wash;
        private Image _marker;
        private TMP_Text _label;

        private Action _onClick;

        private bool _selectedChoice;
        private bool _segment;
        private bool _configured;

        private Color _fillTarget;
        private Color _borderTarget;
        private Color _labelTarget;
        private float _washTarget;

        /// <summary>The control's text. Screens re-label buttons rather than rebuilding them.</summary>
        public TMP_Text LabelText
        {
            get { return _label; }
        }

        /// <summary>True while this control is the current choice in its group.</summary>
        public bool IsSelectedChoice
        {
            get { return _selectedChoice; }
        }

        public void Configure(
            UiButtonVariant variant,
            Image surface,
            Image halo,
            Image ring,
            Image border,
            Image fill,
            TMP_Text label,
            Action onClick)
        {
            _variant = variant;
            _halo = halo != null ? halo.gameObject : null;
            _ring = ring != null ? ring.gameObject : null;
            _border = border;
            _fill = fill;
            _label = label;
            _onClick = onClick;

            transition = Transition.None;
            targetGraphic = surface;

            // Cream wash for hover. Sits above the fill and below the label, so it
            // lightens the surface without touching the text it is measured against.
            // WP-068 5b (AD MUST-1): a Ghost's wash is a pill 8 px in (its only surface).
            _wash = UiFactory.NewImage("Wash", transform, UiColor.TextPrimary, variant == UiButtonVariant.Ghost ? UiSprites.Chip : _fill.sprite);
            UiFactory.Fill((RectTransform)_wash.transform, variant == UiButtonVariant.Ghost ? 8f : UiTokens.StrokeControl);
            _wash.canvasRenderer.SetAlpha(0f);

            // The fill and border carry their colour on the CanvasRenderer (Tint channel):
            // Graphic.color is white so the renderer colour is the absolute colour.
            _fill.color = Color.white;
            _border.color = Color.white;

            // Left-edge marker for the navigation pane's current entry.
            _marker = UiFactory.NewImage("Marker", transform, UiColor.TextPrimary, UiSprites.Chip); // WP-022B AD MUST-1: cream, not a third vermilion
            var markerRect = (RectTransform)_marker.transform;
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(UiTokens.HeadingTickWidth, UiTokens.HeadingTickHeight);
            markerRect.anchoredPosition = new Vector2((int)UiSpace.S, 0f);
            _marker.gameObject.SetActive(false);

            _label.transform.SetAsLastSibling();

            _configured = true;

            // Refresh(true), not ApplyImmediate(): the targets have to be resolved before
            // they are written, or the control spends its first frames fading in from
            // fully transparent - which reads as a rendering fault, not as a transition.
            Refresh(true);
        }

        /// <summary>
        /// AD R1-A SHOULD-1 (Nav.Segment): in a horizontal segmented row the choice is carried by the raised fill and the
        /// cream outline; the Nav tick belongs to vertical lists, where it marks a row in a column.
        /// </summary>
        public void SetSegment(bool segment)
        {
            if (_segment == segment)
            {
                return;
            }

            _segment = segment;
            Refresh(false);
        }

        /// <summary>Marks this control as the current choice in its group.</summary>
        public void SetSelectedChoice(bool value)
        {
            if (_selectedChoice == value)
            {
                return;
            }

            _selectedChoice = value;
            Refresh(false);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            Refresh(false);
        }

        public void SetOnClick(Action onClick)
        {
            _onClick = onClick;
        }

        public void SetText(string value)
        {
            if (_label != null)
            {
                _label.text = value ?? string.Empty;
            }
        }

        // ------------------------------------------------------------------ input

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (IsHoldButton)
            {
                return; // a hold control acts on a completed hold, never on a click
            }

            Activate();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (IsHoldButton)
            {
                BeginHold(true);
                return;
            }

            Activate();
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left)
            {
                UiCursor.SetPressed(true);   // V1 Press art while the control is held
            }

            if (IsHoldButton && eventData != null && eventData.button == PointerEventData.InputButton.Left)
            {
                _pointerHeld = true;
                BeginHold(false);
            }
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            _pointerHeld = false;
            UiCursor.SetPressed(false);
            base.OnPointerUp(eventData);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            _pointerHeld = false;
            base.OnPointerExit(eventData);
        }

        private void Activate()
        {
            if (!IsActive())
            {
                return;
            }

            if (!IsInteractable())
            {
                // A locked weapon row, a card the player cannot afford: the control is
                // deliberately dead, but silence reads as a dropped input rather than as a
                // refusal. This is the interface's Denied cue.
                UiSound.Play(UiSoundKind.Denied);
                return;
            }

            UiEvents.Raise(UiEvent.ButtonPress, name);
            UiSound.Play(UiSoundKind.Confirm);

            Action handler = _onClick;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke();
            }
            catch (Exception e)
            {
                // A broken screen callback must not take the whole menu down with it.
                Debug.LogException(e, this);
            }
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            // WP-068 slice 0 (D3): a hover is ONE cue - ButtonHover (UiHover). It used to
            // also play UiSound.Move (UiNavigate) in the same frame, two sounds per hover.
            UiEvents.Raise(UiEvent.ButtonHover, name);
            base.OnPointerEnter(eventData);

            if (IsInteractable())
            {
                // Pointer hover moves focus too, so the keyboard and the mouse never
                // disagree about which control is current. This select is programmatic
                // (not an AxisEventData), so OnSelect below stays silent for it.
                if (EventSystem.current != null && !EventSystem.current.alreadySelecting)
                {
                    EventSystem.current.SetSelectedGameObject(gameObject);
                }
            }
        }

        /// <summary>
        /// WP-068 slice 0 (D3): keyboard / gamepad focus travel is the Navigate cue. A
        /// navigation move arrives as an <see cref="AxisEventData"/> (Selectable.Navigate
        /// sets selectedObject with the move event); programmatic focus - screen open,
        /// focus repair after a purchase, pointer hover - passes plain BaseEventData and
        /// stays silent. Before this, pad and keyboard travel between buttons made no
        /// sound at all.
        /// </summary>
        public override void OnSelect(BaseEventData eventData)
        {
            _hasFocus = true;
            base.OnSelect(eventData);
            if (_configured)
            {
                Refresh(false);
            }

            if (_configured && eventData is AxisEventData && IsInteractable())
            {
                UiSound.Play(UiSoundKind.Move);
            }

            if (_configured)
            {
                UiFocusBrackets.Follow((RectTransform)transform); // WP-068 slice 5 (§4 #2)
            }
        }

        // ------------------------------------------------------------------ visuals

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (!_configured)
            {
                return;
            }

            Refresh(instant);
        }

        // WP-068 slice 2: no Update. State changes arrive through DoStateTransition and
        // OnSelect / OnDeselect; the fill, border and hover wash move with short Tint
        // tweens on the UiMotion core (CanvasRenderer colour over a white Graphic.color, so
        // no mesh is rebuilt while they move), and the label colour changes at once. The
        // old exponential lerp ran every frame for every button and wrote the TMP label
        // colour each time, which regenerates its mesh (interaction-designer r2).
        private bool _hasFocus;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_configured)
            {
                // A disable clears the renderer; put the resolved colours back.
                Refresh(true);
            }
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            _hasFocus = false;
            _pointerHeld = false;
            base.OnDeselect(eventData);
            if (_configured)
            {
                Refresh(false);
            }

            UiFocusBrackets.Release((RectTransform)transform);
        }

        protected override void OnDisable()
        {
            _pointerHeld = false;
            UiCursor.SetPressed(false);   // a click that closes its own screen never leaves the Press art up
            base.OnDisable();
            UiFocusBrackets.Release((RectTransform)transform);
        }

        private void Refresh(bool instant)
        {
            bool enabledControl = IsInteractable();
            bool pressed = enabledControl && currentSelectionState == SelectionState.Pressed;
            bool hovered = enabledControl && currentSelectionState == SelectionState.Highlighted;

            bool focused = enabledControl && _hasFocus;

            ResolveTargets(enabledControl, pressed, hovered);

            if (_halo != null && _halo.activeSelf != focused)
            {
                _halo.SetActive(focused);
            }

            if (_ring != null && _ring.activeSelf != focused)
            {
                _ring.SetActive(focused);
            }

            bool showMarker = _selectedChoice && _variant == UiButtonVariant.Nav && !_segment;
            if (_marker != null && _marker.gameObject.activeSelf != showMarker)
            {
                _marker.gameObject.SetActive(showMarker);
            }

            if (instant)
            {
                ApplyImmediate();
            }
            else
            {
                Animate();
            }

            RefreshV3(enabledControl, pressed, hovered || focused);
        }

        private void ResolveTargets(bool enabledControl, bool pressed, bool hovered)
        {
            UiColor fill;
            UiColor border;
            UiColor label;

            if (!enabledControl)
            {
                // The one audited disabled pair. Locked content still has to be legible.
                // A disabled Ghost stays surfaceless, its word in TextMuted (AD 5b MUST-1).
                fill = UiColor.SurfaceRaised;
                border = UiColor.BorderHairline;
                label = _variant == UiButtonVariant.Ghost ? UiColor.TextMuted : UiColor.TextSecondary;
            }
            else
            {
                switch (_variant)
                {
                    case UiButtonVariant.Primary:
                    case UiButtonVariant.Danger:
                        // Danger deliberately shares Primary's tokens. Vermilion is
                        // already the alarm colour in this palette, and inventing a
                        // second red would add an unaudited text/fill pair for the sake
                        // of one confirmation dialog. The wording carries the warning.
                        fill = pressed ? UiColor.AccentPrimaryPressed : UiColor.AccentPrimary;
                        border = UiColor.AccentPrimary;
                        label = UiColor.TextOnAccent;
                        break;

                    case UiButtonVariant.Nav:
                        // WP-022 (AD MUST-2, REQ-105 vermilion budget): the selected tab is
                        // carried by the raised fill, the cream outline and the vermilion
                        // marker tick - not by a second vermilion outline.
                        fill = _selectedChoice ? UiColor.SurfaceRaised : UiColor.SurfaceBase;
                        border = _selectedChoice ? UiColor.TextPrimary : UiColor.BorderStrong;
                        label = _selectedChoice ? UiColor.TextPrimary : UiColor.TextSecondary;
                        break;

                    case UiButtonVariant.Card:
                        // Same budget: a chosen card is raised + cream-outlined + badged.
                        fill = _selectedChoice ? UiColor.SurfaceRaised : UiColor.SurfacePanel;
                        border = _selectedChoice ? UiColor.TextPrimary : UiColor.BorderStrong;
                        label = UiColor.TextPrimary;
                        break;

                    case UiButtonVariant.Ghost:
                        // WP-068 slice 5: no fill and no border - the word in TextPrimary
                        // (TextSecondary is the disabled label of filled buttons: AD 5b MUST-1);
                        // hover / focus / press show the wash pill.
                        fill = UiColor.SurfacePanel;
                        border = UiColor.BorderHairline;
                        label = UiColor.TextPrimary;
                        break;

                    default:
                        fill = pressed ? UiColor.SurfacePanel : UiColor.SurfaceBase;
                        border = UiColor.BorderStrong;
                        label = UiColor.TextPrimary;
                        break;
                }
            }

            _fillTarget = UiTokens.Get(fill);
            _borderTarget = UiTokens.Get(border);
            _labelTarget = UiTokens.Get(label);

            _washTarget = !enabledControl
                ? 0f
                : pressed
                    ? UiTokens.OpacityPressWash
                    : hovered
                        ? UiTokens.OpacityHoverWash
                        : 0f;

            if (_variant == UiButtonVariant.Ghost)
            {
                // Never an opaque surface (a Ghost may lie over a card's content): the press is
                // the wash + the bevel; focus shows the wash too (the word has no box of its own).
                _fillTarget.a = 0f;
                _borderTarget.a = 0f;
                if (enabledControl && _hasFocus && _washTarget <= 0f)
                {
                    _washTarget = UiTokens.OpacityHoverWash;
                }
            }
        }

        private void ApplyImmediate()
        {
            UiMotion.KillTarget(_fill, false);
            UiMotion.KillTarget(_border, false);
            UiMotion.KillTarget(_wash, false);
            _fill.canvasRenderer.SetColor(_fillTarget);
            _border.canvasRenderer.SetColor(_borderTarget);
            _wash.canvasRenderer.SetAlpha(_washTarget);
            SetLabelColour();
        }

        private void Animate()
        {
            // Fast (140 ms) Settle, Decor: Reduced Motion lands the colours at once.
            UiMotion.Tint(_fill, _fillTarget, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.Tint(_border, _borderTarget, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_wash, _washTarget, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            SetLabelColour();
        }

        private void SetLabelColour()
        {
            if (_label.color != _labelTarget)
            {
                _label.color = _labelTarget; // one regen per state change, never per frame
            }
        }

    }
}
