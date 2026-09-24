using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>Modal sizes (direction §4 #11).</summary>
    public enum UiModalSize
    {
        S = 0, // 560 × 264
        M = 1, // 640 × 340
        L = 2  // 680 × 520
    }

    /// <summary>
    /// WP-068 slice 5 · component #11 UiModal (direction §4): scrim (0.72) + an opaque
    /// framed card (Elev2) with a title, a body that sizes itself, and one or two actions.
    ///
    /// USAGE: a decision that must be answered before anything else (END RUN, RESET ALL
    /// BINDINGS, a load error, the title picker). DON'T: open one for information the
    /// screen can state in place (<see cref="UiNotices"/>), or stack two.
    /// Focus is TRAPPED inside (explicit navigation between the actions only), the default
    /// focus is the SAFE action, and Cancel (B / Esc) is the secondary action - the owning
    /// screen, which already polls Cancel, calls <see cref="HandleCancel"/> (no per-frame
    /// loop in the kit). Closing restores focus to where it was.
    /// ACCESSIBILITY: text only ever sits on the opaque card (never on the scrim or a blur);
    /// the destructive action is a Danger button with its word; EnterModal is Decor
    /// (Reduced Motion shows it at rest).
    /// </summary>
    public sealed class UiModal : MonoBehaviour
    {
        private CanvasGroup _scrim;
        private CanvasGroup _card;
        private RectTransform _cardRect;
        private UiButton _primary;
        private UiButton _secondary;
        private Action _onSecondary;
        private GameObject _returnFocus;

        public UiButton Primary
        {
            get { return _primary; }
        }

        public UiButton Secondary
        {
            get { return _secondary; }
        }

        public bool IsOpen
        {
            get { return gameObject.activeSelf; }
        }

        public static Vector2 SizeOf(UiModalSize size)
        {
            switch (size)
            {
                case UiModalSize.S: return new Vector2(560f, 264f);
                case UiModalSize.L: return new Vector2(680f, 520f);
                default: return new Vector2(640f, 340f);
            }
        }

        /// <summary>
        /// Build a closed modal under a full-screen parent. <paramref name="primaryVariant"/> is
        /// Danger for destructive confirmations. <paramref name="safeIsDefault"/> puts the first
        /// focus on the secondary (safe) action, which is the rule for destructive ones.
        /// </summary>
        public static UiModal Create(string name, RectTransform parent, UiModalSize size, string title, string body,
            string primaryLabel, UiButtonVariant primaryVariant, Action onPrimary,
            string secondaryLabel, Action onSecondary, bool safeIsDefault = true)
        {
            return Create(name, parent, size, title, body, primaryLabel, primaryVariant, onPrimary, secondaryLabel, onSecondary, safeIsDefault, false);
        }

        /// <summary>
        /// The Confirm preset (E25 END RUN, RESET ALL BINDINGS): size S, height from its content
        /// (heading + body + 24 + actions, never below 264) so a two-line question leaves no void
        /// (AD 5a SHOULD-3). Destructive by default: Danger action, safe focus.
        /// </summary>
        public static UiModal Confirm(string name, RectTransform parent, string title, string body,
            string confirmLabel, Action onConfirm, string cancelLabel, Action onCancel, bool destructive = true)
        {
            return Create(name, parent, UiModalSize.S, title, body, confirmLabel, destructive ? UiButtonVariant.Danger : UiButtonVariant.Primary,
                onConfirm, cancelLabel, onCancel, true, true);
        }

        /// <summary>As above; <paramref name="fitHeight"/> sizes the card from its content (never below the size's own height).</summary>
        public static UiModal Create(string name, RectTransform parent, UiModalSize size, string title, string body,
            string primaryLabel, UiButtonVariant primaryVariant, Action onPrimary,
            string secondaryLabel, Action onSecondary, bool safeIsDefault, bool fitHeight)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            UiFactory.Fill(root);
            var modal = root.gameObject.AddComponent<UiModal>();
            modal.Build(root, size, title, body, primaryLabel, primaryVariant, onPrimary, secondaryLabel, onSecondary, safeIsDefault);
            if (fitHeight)
            {
                modal.FitHeight();
            }

            root.gameObject.SetActive(false);
            return modal;
        }

        /// <summary>Card height from the content (XL + heading + M + body + L + actions + XL), never below 264 (the S height) - the width stays the size's.</summary>
        private void FitHeight()
        {
            float content = (int)UiSpace.XL * 2f + UiFactory.LineHeight(UiType.H2) + (int)UiSpace.M + _bodyHeight + (int)UiSpace.L + UiSize.Target;
            Vector2 size = _cardRect.sizeDelta;
            _cardRect.sizeDelta = new Vector2(size.x, Mathf.Max(SizeOf(UiModalSize.S).y, Mathf.Ceil(content)));
        }

        /// <summary>Current card size (tests).</summary>
        public Vector2 CardSize
        {
            get { return _cardRect.sizeDelta; }
        }

        private bool _safeIsDefault;
        private float _bodyHeight;

        private void Build(RectTransform root, UiModalSize size, string title, string body,
            string primaryLabel, UiButtonVariant primaryVariant, Action onPrimary,
            string secondaryLabel, Action onSecondary, bool safeIsDefault)
        {
            _onSecondary = onSecondary;
            _safeIsDefault = safeIsDefault;

            Image scrim = UiFactory.NewImage("Scrim", root, UiColor.ScrimOverlay, UiSprites.Solid);
            UiFactory.Fill((RectTransform)scrim.transform);
            Color scrimColour = scrim.color;
            scrimColour.a = UiTokens.OpacityScrimUnderDialog;
            scrim.color = scrimColour;
            scrim.raycastTarget = true; // nothing underneath takes a click while open
            _scrim = UiFactory.Group(scrim.gameObject);

            _cardRect = UiFactory.FramedCard("Card", root, UiColor.BorderStrong, UiColor.SurfacePanel, UiFrame.Modal);   // A4 Elev2 double line
            Vector2 cardSize = SizeOf(size);
            _cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            _cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRect.pivot = new Vector2(0.5f, 0.5f);
            _cardRect.sizeDelta = cardSize;
            _card = UiFactory.Group(_cardRect.gameObject);

            RectTransform content = UiFactory.CardContent(_cardRect, UiSpace.XL);
            var stack = new UiStack(content);
            stack.Add(UiFactory.Heading(title, content), UiFactory.LineHeight(UiType.H2));
            stack.Gap(UiSpace.M);
            TMP_Text bodyText = UiFactory.Label("Body", content, body, UiType.Body, UiColor.TextSecondary, TextAnchor.UpperLeft);
            float bodyWidth = cardSize.x - (int)UiSpace.XL * 2f;
            float bodyHeight = Mathf.Max(UiFactory.LineHeight(UiType.Body), Mathf.Ceil(bodyText.GetPreferredValues(body, bodyWidth, 0f).y));
            _bodyHeight = bodyHeight;
            stack.Add(bodyText.rectTransform, bodyHeight);

            // Actions: bottom row, secondary on the left, primary on the right.
            float buttonWidth = (bodyWidth - (int)UiSpace.M) * 0.5f;
            if (!string.IsNullOrEmpty(secondaryLabel))
            {
                _secondary = UiFactory.Button("Secondary", content, secondaryLabel, UiButtonVariant.Secondary, () => Act(onSecondary));
                PlaceAction((RectTransform)_secondary.transform, 0f, buttonWidth);
            }

            _primary = UiFactory.Button("Primary", content, primaryLabel, primaryVariant, () => Act(onPrimary));
            PlaceAction((RectTransform)_primary.transform, _secondary != null ? buttonWidth + (int)UiSpace.M : 0f,
                _secondary != null ? buttonWidth : bodyWidth);

            // Focus trap: the actions only lead to each other.
            if (_secondary != null)
            {
                UiInput.LinkHorizontal(_secondary, _primary, _primary);
                UiInput.LinkHorizontal(_primary, _secondary, _secondary);
            }
            else
            {
                UiInput.LinkHorizontal(_primary, _primary, _primary);
            }
        }

        private static void PlaceAction(RectTransform rect, float x, float width)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(width, UiSize.Target);
            rect.anchoredPosition = new Vector2(x, 0f);
        }

        private void Act(Action action)
        {
            Close();
            if (action != null)
            {
                action();
            }
        }

        /// <summary>Open: remember focus, EnterModal (scrim Fast, card 0.96 → 1 Base), focus the default action.</summary>
        public void Open()
        {
            _returnFocus = UnityEngine.EventSystems.EventSystem.current != null
                ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject
                : null;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            UiPresets.EnterModal(_scrim, _card, _cardRect);
            UiEvents.Raise(UiEvent.DialogOpened, name);
            UiInput.Focus(_safeIsDefault && _secondary != null ? (Selectable)_secondary : _primary);
        }

        /// <summary>Close and return focus to where it was.</summary>
        public void Close()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            gameObject.SetActive(false);
            UiEvents.Raise(UiEvent.DialogClosed, name);
            if (_returnFocus != null && _returnFocus.activeInHierarchy)
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(_returnFocus);
            }
        }

        /// <summary>Cancel (B / Esc) = the secondary action. Returns true when the modal took it.</summary>
        public bool HandleCancel()
        {
            if (!gameObject.activeSelf)
            {
                return false;
            }

            Act(_onSecondary);
            UiSound.Play(UiSoundKind.Back);
            return true;
        }
    }
}
