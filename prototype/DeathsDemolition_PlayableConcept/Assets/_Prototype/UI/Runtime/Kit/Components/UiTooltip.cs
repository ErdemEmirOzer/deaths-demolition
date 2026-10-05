using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-081 B2 · the word of an icon control (menu v4): one tooltip, because focus is one. It follows the control that
    /// lifted (hover or keyboard / gamepad focus - hover moves focus, so the two never disagree), shows after
    /// <see cref="UiIconTokens.TooltipDelaySeconds"/> so a pointer passing over draws nothing, and goes the moment the
    /// control drops. Under the control, a grid unit away; above it when the foot of the screen has no room; never past
    /// the safe inset. The delay is Data-class motion: Reduced Motion keeps it (a tooltip that flashes under a moving
    /// pointer is worse than one that waits).
    ///
    /// USAGE: automatic for <see cref="UiIconButton"/> in Tooltip mode. DON'T: put a sentence in it - one or two words;
    /// the long text belongs to the pane the control opens.
    /// ACCESSIBILITY: TextPrimary on SurfaceRaised (UiTokenContrast pair); wraps at <see cref="UiIconTokens.TooltipMaxWidth"/>
    /// instead of clipping (U2); never takes a raycast.
    /// </summary>
    public sealed class UiTooltip : MonoBehaviour
    {
        private static UiTooltip s_instance;
        private static readonly Vector3[] s_corners = new Vector3[4];

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _plate;
        private TMP_Text _text;
        private RectTransform _target;
        private UiTweenHandle _show;

        /// <summary>The control the tooltip belongs to; null when none.</summary>
        public static RectTransform Target
        {
            get { return s_instance != null ? s_instance._target : null; }
        }

        /// <summary>The tooltip is up and fully shown.</summary>
        public static bool Visible
        {
            get { return s_instance != null && s_instance._target != null && s_instance.gameObject.activeSelf && s_instance._group.alpha >= 0.99f; }
        }

        public static string Text
        {
            get { return s_instance != null && s_instance._target != null ? s_instance._text.text : null; }
        }

        /// <summary>The tooltip's rect in its root's local space (tests: placement and clamping).</summary>
        public static Rect Bounds
        {
            get
            {
                var root = s_instance != null ? s_instance.transform.parent as RectTransform : null;
                if (root == null)
                {
                    return default(Rect);
                }

                Vector2 size = s_instance._rect.sizeDelta;
                Vector2 centre = s_instance._rect.anchoredPosition + root.rect.center;
                return new Rect(centre - size * 0.5f, size);
            }
        }

        /// <summary>Show <paramref name="text"/> for <paramref name="target"/> after the settle delay.</summary>
        public static void Show(RectTransform target, string text)
        {
            if (target == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            Canvas canvas = target.GetComponentInParent<Canvas>();
            var root = canvas != null ? (RectTransform)canvas.rootCanvas.transform : target.root as RectTransform;
            if (root == null)
            {
                return;
            }

            if (s_instance == null)
            {
                s_instance = Build(root);
            }
            else if (s_instance.transform.parent != root)
            {
                s_instance.transform.SetParent(root, false);   // another canvas (a modal, the pause overlay): a cut
            }

            s_instance.Present(root, target, text);
        }

        /// <summary>Drop the tooltip if it belongs to <paramref name="target"/>.</summary>
        public static void Hide(RectTransform target)
        {
            if (s_instance != null && s_instance._target != null && s_instance._target == target)
            {
                s_instance.Dismiss();
            }
        }

        private static UiTooltip Build(RectTransform root)
        {
            var go = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            UiTooltip tip = go.AddComponent<UiTooltip>();
            tip._rect = rect;
            tip._group = go.GetComponent<CanvasGroup>();
            tip._group.blocksRaycasts = false;
            tip._group.interactable = false;
            tip._group.alpha = 0f;

            Image edge = UiFactory.NewImage("Edge", rect, UiColor.BorderStrong, UiSprites.Control);
            UiFactory.Fill((RectTransform)edge.transform);
            tip._plate = UiFactory.NewImage("Plate", rect, UiColor.SurfaceRaised, UiSprites.Control);
            UiFactory.Fill((RectTransform)tip._plate.transform, UiTokens.StrokeControl);

            tip._text = UiFactory.Label("Word", rect, string.Empty, UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            tip._text.raycastTarget = false;
            tip._text.enableWordWrapping = true;
            go.SetActive(false);
            return tip;
        }

        private void Present(RectTransform root, RectTransform target, string text)
        {
            _target = target;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _text.text = text;

            float padX = (int)UiIconTokens.TooltipPadX;
            float padY = (int)UiIconTokens.TooltipPadY;
            float textWidth = Mathf.Min(UiIconTokens.TooltipMaxWidth - 2f * padX, _text.GetPreferredValues(text).x);
            float textHeight = Mathf.Max(UiFactory.LineHeight(UiType.Label), _text.GetPreferredValues(text, textWidth, 0f).y);
            Vector2 size = new Vector2(Mathf.Ceil(textWidth + 2f * padX), Mathf.Ceil(textHeight + 2f * padY));
            _rect.sizeDelta = size;
            var textRect = (RectTransform)_text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(padX, padY);
            textRect.offsetMax = new Vector2(-padX, -padY);

            _rect.anchoredPosition = Place(root, target, size);

            UiMotion.Kill(_show, false);
            _group.alpha = 0f;
            _show = UiMotion.Alpha(_group, 1f, UiDuration.Instant, UiEase.Linear, UiMotionClass.Data, UiIconTokens.TooltipDelaySeconds);
        }

        /// <summary>Centre of the tooltip in the root's space: under the target, else above; clamped inside the safe inset.</summary>
        private static Vector2 Place(RectTransform root, RectTransform target, Vector2 size)
        {
            target.GetWorldCorners(s_corners);
            Vector2 min = root.InverseTransformPoint(s_corners[0]);
            Vector2 max = root.InverseTransformPoint(s_corners[2]);
            Rect area = root.rect;
            float inset = (int)UiTokens.SafeInset;
            float gap = (int)UiIconTokens.TooltipGap;

            float y = min.y - gap - size.y * 0.5f;
            if (y - size.y * 0.5f < area.yMin + inset)
            {
                y = max.y + gap + size.y * 0.5f;
            }

            float x = (min.x + max.x) * 0.5f;
            float half = size.x * 0.5f;
            x = Mathf.Clamp(x, area.xMin + inset + half, Mathf.Max(area.xMin + inset + half, area.xMax - inset - half));
            return new Vector2(x, y) - area.center;   // anchored to the root's centre
        }

        private void Dismiss()
        {
            UiMotion.Kill(_show, false);
            _group.alpha = 0f;
            _target = null;
            gameObject.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
