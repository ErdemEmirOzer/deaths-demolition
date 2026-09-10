using System;
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
    public sealed class UiButton : Selectable, IPointerClickHandler, ISubmitHandler
    {
        private UiButtonVariant _variant;

        private GameObject _halo;
        private GameObject _ring;
        private Image _border;
        private Image _fill;
        private Image _wash;
        private Image _marker;
        private Text _label;

        private Action _onClick;

        private bool _selectedChoice;
        private bool _configured;

        private Color _fillTarget;
        private Color _borderTarget;
        private Color _labelTarget;
        private float _washTarget;

        /// <summary>The control's text. Screens re-label buttons rather than rebuilding them.</summary>
        public Text LabelText
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
            Text label,
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
            _wash = UiFactory.NewImage("Wash", transform, UiColor.TextPrimary, _fill.sprite);
            UiFactory.Fill((RectTransform)_wash.transform, UiTokens.StrokeControl);
            SetAlpha(_wash, 0f);

            // Left-edge marker for the navigation pane's current entry.
            _marker = UiFactory.NewImage("Marker", transform, UiColor.AccentPrimary, UiSprites.Chip);
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

            Activate();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            Activate();
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
            base.OnPointerEnter(eventData);

            if (IsInteractable())
            {
                UiSound.Play(UiSoundKind.Move);

                // Pointer hover moves focus too, so the keyboard and the mouse never
                // disagree about which control is current.
                if (EventSystem.current != null && !EventSystem.current.alreadySelecting)
                {
                    EventSystem.current.SetSelectedGameObject(gameObject);
                }
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

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            Refresh(false);

            // Unscaled: the pause screen and the round-end freeze both run at timeScale 0.
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.0001f, UiTokens.MotionFast));

            _fill.color = Color.Lerp(_fill.color, _fillTarget, t);
            _border.color = Color.Lerp(_border.color, _borderTarget, t);
            _label.color = Color.Lerp(_label.color, _labelTarget, t);

            Color wash = _wash.color;
            wash.a = Mathf.Lerp(wash.a, _washTarget, t);
            _wash.color = wash;
        }

        private void Refresh(bool instant)
        {
            bool enabledControl = IsInteractable();
            bool pressed = enabledControl && currentSelectionState == SelectionState.Pressed;
            bool hovered = enabledControl && currentSelectionState == SelectionState.Highlighted;

            bool focused = enabledControl
                           && EventSystem.current != null
                           && EventSystem.current.currentSelectedGameObject == gameObject;

            ResolveTargets(enabledControl, pressed, hovered);

            if (_halo != null && _halo.activeSelf != focused)
            {
                _halo.SetActive(focused);
            }

            if (_ring != null && _ring.activeSelf != focused)
            {
                _ring.SetActive(focused);
            }

            bool showMarker = _selectedChoice && _variant == UiButtonVariant.Nav;
            if (_marker != null && _marker.gameObject.activeSelf != showMarker)
            {
                _marker.gameObject.SetActive(showMarker);
            }

            if (instant)
            {
                ApplyImmediate();
            }
        }

        private void ResolveTargets(bool enabledControl, bool pressed, bool hovered)
        {
            UiColor fill;
            UiColor border;
            UiColor label;

            if (!enabledControl)
            {
                // The one audited disabled pair. Locked content still has to be legible.
                fill = UiColor.SurfaceRaised;
                border = UiColor.BorderHairline;
                label = UiColor.TextSecondary;
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
                        fill = _selectedChoice ? UiColor.SurfaceRaised : UiColor.SurfaceBase;
                        border = _selectedChoice ? UiColor.AccentPrimary : UiColor.BorderStrong;
                        label = _selectedChoice ? UiColor.TextPrimary : UiColor.TextSecondary;
                        break;

                    case UiButtonVariant.Card:
                        fill = _selectedChoice ? UiColor.SurfaceRaised : UiColor.SurfacePanel;
                        border = _selectedChoice ? UiColor.AccentPrimary : UiColor.BorderStrong;
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
        }

        private void ApplyImmediate()
        {
            _fill.color = _fillTarget;
            _border.color = _borderTarget;
            _label.color = _labelTarget;
            SetAlpha(_wash, _washTarget);
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
