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
        L = 2, // 680 × 520

        /// <summary>WP-068 R1-C (AD R1-A SHOULD-3): the Pause panel - three CAMERA segments hold "THIRD PERSON" at TextScale 1.25.</summary>
        XL = 3 // 760 × 680
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
    /// WP-098A (KIT-API §3, §4 "Confirm"; KIT-KILAVUZU C31): <see cref="UiControlStyle.Brush"/> - no card, no shadow, no frame:
    /// the Confirm ink (<see cref="UiBrushTokens.ScrimConfirm"/> 0.985, TextPrimary 13.3:1 on a white world in linear light)
    /// carries the words; a transparent "Card" rect <see cref="UiBrushTokens.ConfirmMeasure"/> wide holds the C5 title, a
    /// MenuBody TextPrimary body measured to its height, and two C11 actions - the safe one first (left), 64 apart. The
    /// destructive action is said with its word and glyph (<see cref="SetActionIcons"/>), never a colour. Focus trap, safe
    /// default, Cancel = safe, focus return and EnterModal are the same as v3.
    /// </summary>
    public sealed class UiModal : MonoBehaviour
    {
        private CanvasGroup _scrim;
        private CanvasGroup _card;
        private RectTransform _cardRect;
        private RectTransform _shadowRect;
        private UiButton _primary;
        private UiButton _secondary;
        private Action _onSecondary;
        private GameObject _returnFocus;
        private UiControlStyle _style;
        private UiBrushTitle _brushTitle;
        private Image _scrimImage;

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

        /// <summary>WP-098A · the dialect the modal is drawn in.</summary>
        public UiControlStyle Style
        {
            get { return _style; }
        }

        /// <summary>WP-098A · the control <see cref="Open"/> focuses: the safe (secondary) action when the modal has one and safe is the default.</summary>
        public Selectable DefaultFocus
        {
            get { return _safeIsDefault && _secondary != null ? (Selectable)_secondary : _primary; }
        }

        /// <summary>WP-098A · the scrim image (v3: ScrimOverlay at the dialog α; Brush: the Confirm ink) - tests read its α.</summary>
        public Image ScrimImage
        {
            get { return _scrimImage; }
        }

        public static Vector2 SizeOf(UiModalSize size)
        {
            switch (size)
            {
                case UiModalSize.S: return new Vector2(560f, 264f);
                case UiModalSize.L: return new Vector2(680f, 520f);
                case UiModalSize.XL: return new Vector2(760f, 680f);
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
            string confirmLabel, Action onConfirm, string cancelLabel, Action onCancel, bool destructive = true,
            UiControlStyle style = UiControlStyle.V3)
        {
            if (style == UiControlStyle.Brush)
            {
                // WP-098A: destructive is worded (the caller's copy and glyph), never coloured - both actions are C11.
                RectTransform root = UiFactory.NewRect(name, parent);
                UiFactory.Fill(root);
                var modal = root.gameObject.AddComponent<UiModal>();
                modal.BuildBrush(root, title, body, confirmLabel, onConfirm, cancelLabel, onCancel);
                root.gameObject.SetActive(false);
                return modal;
            }

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
            FitShadow();
        }

        /// <summary>The card's shadow: the card's size plus the Elev2 reach on every side.</summary>
        private void FitShadow()
        {
            if (_shadowRect == null)
            {
                return;   // WP-098A: a Brush confirm has no card, so no shadow
            }

            float reach = UiElevationSpec.ShadowRadius(UiElevation.Elev2);
            _shadowRect.sizeDelta = _cardRect.sizeDelta + new Vector2(reach * 2f, reach * 2f);
        }

        /// <summary>The shadow under the card (tests: it frames the card on every side).</summary>
        public RectTransform ShadowRect
        {
            get { return _shadowRect; }
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
            _scrimImage = scrim;

            // WP-081 (R1 AD SHOULD-9): the kit's one soft shadow at Elev2 under the card. The scrim alone left about 12 % of
            // a cream row legible at the edge of a small dialog ("ARAYÜZ ÖLÇEĞİ" cut by its top edge); now the rows behind
            // stop at a falloff. It is the scrim's child, so it draws between the scrim and the card and fades with the scrim.
            float reach = UiElevationSpec.ShadowRadius(UiElevation.Elev2);
            Image shadow = UiFactory.NewImage("CardShadow", scrim.transform, UiColor.ShadowKey, UiSprites.SoftShadow);
            shadow.raycastTarget = false;
            shadow.type = Image.Type.Sliced;
            shadow.pixelsPerUnitMultiplier = UiSprites.SoftShadowReach / Mathf.Max(1f, reach);
            Color shade = shadow.color;
            shade.a = UiElevationSpec.ShadowAlpha(UiElevation.Elev2);
            shadow.color = shade;
            _shadowRect = shadow.rectTransform;
            _shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            _shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            _shadowRect.pivot = new Vector2(0.5f, 0.5f);
            _shadowRect.anchoredPosition = new Vector2(0f, -reach * 0.25f);

            _cardRect = UiFactory.FramedCard("Card", root, UiColor.BorderStrong, UiColor.SurfacePanel, UiFrame.Modal);   // A4 Elev2 double line
            Vector2 cardSize = SizeOf(size);
            _cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            _cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRect.pivot = new Vector2(0.5f, 0.5f);
            _cardRect.sizeDelta = cardSize;
            FitShadow();
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

        /// <summary>WP-098A · the Brush build: the Confirm ink, a transparent measure, the C5 title, the MenuBody body, two C11.</summary>
        private void BuildBrush(RectTransform root, string title, string body, string primaryLabel, Action onPrimary,
            string secondaryLabel, Action onSecondary)
        {
            _style = UiControlStyle.Brush;
            _onSecondary = onSecondary;
            _safeIsDefault = true;

            _scrimImage = UiFactory.NewImage("Scrim", root, UiColor.SurfaceInset, UiSprites.Solid);
            UiFactory.Fill(_scrimImage.rectTransform);
            Color ink = _scrimImage.color;
            ink.a = UiBrushTokens.ScrimConfirm;
            _scrimImage.color = ink;
            _scrimImage.raycastTarget = true; // nothing underneath takes a click while open
            _scrim = UiFactory.Group(_scrimImage.gameObject);

            _cardRect = UiFactory.NewRect("Card", root);   // no surface: the ink carries the words
            _cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            _cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRect.pivot = new Vector2(0.5f, 0.5f);
            float measure = UiBrushTokens.ConfirmMeasure;
            _cardRect.sizeDelta = new Vector2(measure, UiBrushTokens.ControlHeight);
            _card = UiFactory.Group(_cardRect.gameObject);

            _brushTitle = UiBrushKit.TitleText(_cardRect, title, null);

            // The body ("Card/Body"): measured to its height; none at all when there is nothing to say (QUIT asks in its title).
            _bodyHeight = 0f;
            if (!string.IsNullOrEmpty(body))
            {
                TMP_Text bodyText = UiFactory.Label("Body", _cardRect, body, UiType.MenuBody, UiColor.TextPrimary, TextAnchor.UpperLeft);
                _bodyHeight = Mathf.Max(UiFactory.LineHeight(UiType.MenuBody), Mathf.Ceil(bodyText.GetPreferredValues(body, measure, 0f).y));
                RectTransform bodyRect = bodyText.rectTransform;
                bodyRect.anchorMin = new Vector2(0f, 1f);
                bodyRect.anchorMax = new Vector2(1f, 1f);
                bodyRect.pivot = new Vector2(0.5f, 1f);
                bodyRect.sizeDelta = new Vector2(0f, _bodyHeight);
                bodyRect.anchoredPosition = new Vector2(0f, -(_brushTitle.Height + (int)UiSpace.XL));
            }

            if (!string.IsNullOrEmpty(secondaryLabel))
            {
                _secondary = UiBrushKit.BrushButton(_cardRect, "Secondary", secondaryLabel, null, () => Act(onSecondary));
            }

            _primary = UiBrushKit.BrushButton(_cardRect, "Primary", primaryLabel, null, () => Act(onPrimary));
            LayoutBrushActions();

            // The measure's height from its content: title band + XL + (body + XL) + the actions.
            float height = _brushTitle.Height + (int)UiSpace.XL + (_bodyHeight > 0f ? _bodyHeight + (int)UiSpace.XL : 0f) + UiBrushTokens.ControlHeight;
            _cardRect.sizeDelta = new Vector2(measure, Mathf.Ceil(height));

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

        /// <summary>WP-098A · Brush actions on the measure's foot: the safe one first from the left, <see cref="UiBrushTokens.ConfirmActionGap"/> apart, each as wide as its word.</summary>
        private void LayoutBrushActions()
        {
            float x = 0f;
            if (_secondary != null)
            {
                float width = ((RectTransform)_secondary.transform).sizeDelta.x;
                PlaceAction((RectTransform)_secondary.transform, x, width, UiBrushTokens.ControlHeight);
                x += width + UiBrushTokens.ConfirmActionGap;
            }

            if (_primary != null)
            {
                PlaceAction((RectTransform)_primary.transform, x, ((RectTransform)_primary.transform).sizeDelta.x, UiBrushTokens.ControlHeight);
            }
        }

        /// <summary>
        /// WP-098A (KIT-API §3) · leading 40 px glyphs on the two actions (Brush: Menu.EndRun / Menu.Resume …); null keeps a
        /// word-only action. Brush re-measures the actions and re-lays them; v3 puts the glyph at the button's leading edge.
        /// Build time, once per action.
        /// </summary>
        public void SetActionIcons(string primaryIconKey, string secondaryIconKey)
        {
            if (_style == UiControlStyle.Brush)
            {
                if (_primary != null && !string.IsNullOrEmpty(primaryIconKey))
                {
                    UiBrushKit.SetLeadingGlyph(_primary, primaryIconKey);
                    UiBrushKit.FitWidth(_primary);
                }

                if (_secondary != null && !string.IsNullOrEmpty(secondaryIconKey))
                {
                    UiBrushKit.SetLeadingGlyph(_secondary, secondaryIconKey);
                    UiBrushKit.FitWidth(_secondary);
                }

                LayoutBrushActions();
                return;
            }

            V3Icon(_primary, primaryIconKey);
            V3Icon(_secondary, secondaryIconKey);
        }

        private static void V3Icon(UiButton button, string iconKey)
        {
            if (button == null || string.IsNullOrEmpty(iconKey) || button.transform.Find("Icon") != null)
            {
                return;
            }

            bool onAccent = button.Variant == UiButtonVariant.Primary || button.Variant == UiButtonVariant.Danger;
            UiInput.LeadingIcon(button, iconKey, onAccent ? UiColor.TextOnAccent : UiColor.TextPrimary, UiBrushKit.ControlGlyph);
        }

        private static void PlaceAction(RectTransform rect, float x, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, 0f);
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
            if (_brushTitle.Stroke != null)
            {
                _brushTitle.Stroke.Draw(UiBrushTokens.Open);   // WP-098A: the C5 stroke inks in with the confirm (Decor)
            }

            UiEvents.Raise(UiEvent.DialogOpened, name);
            UiInput.Focus(DefaultFocus);
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
