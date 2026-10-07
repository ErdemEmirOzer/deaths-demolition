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
    ///
    /// WP-098A P0b (C-00 07.10) · C27 Brush style, the default from WP-098A (<see cref="Style"/>; KILAVUZ §4.1 C27, §8 S4):
    /// no box and no edge - an ink cloud (AP-034's TooltipWash, else UiSprites.SoftShadow) in SurfaceInset at
    /// <see cref="UiBrushTokens.TooltipWash"/> reaching <see cref="WashOverhang"/> past the text box, the word in MenuSmall
    /// cream, wrapping at <see cref="UiBrushTokens.TooltipMaxWidth"/> (a lock reason may take two lines: <see cref="Show(RectTransform, string, float)"/>),
    /// <see cref="UiBrushTokens.TooltipGap"/> from the control. The settle delay and the placement rules are the same in
    /// both styles. V3 keeps the raised plate (a screen or a capture that needs it sets <see cref="Style"/>).
    /// </summary>
    public sealed class UiTooltip : MonoBehaviour
    {
        private static UiTooltip s_instance;
        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>The tooltip's dialect: Brush (ink cloud, MenuSmall, ≤ 384) from WP-098A; V3 keeps the raised plate.</summary>
        public static UiControlStyle Style = UiControlStyle.Brush;

        /// <summary>WP-098A Brush: how far the ink cloud reaches past the text box (its soft edge; the box itself is <see cref="Bounds"/>).</summary>
        public const float WashOverhang = (int)UiSpace.L;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _edge;
        private Image _plate;
        private TMP_Text _text;
        private RectTransform _target;
        private UiTweenHandle _show;
        private bool _styled;
        private UiControlStyle _drawn;

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

        /// <summary>The style the tooltip is drawn in now (tests: the default and a switch back to V3).</summary>
        public static UiControlStyle DrawnStyle
        {
            get { return s_instance != null && s_instance._styled ? s_instance._drawn : Style; }
        }

        /// <summary>Show <paramref name="text"/> for <paramref name="target"/> after the settle delay, wrapping at the style's widest line.</summary>
        public static void Show(RectTransform target, string text)
        {
            Show(target, text, Style == UiControlStyle.Brush ? UiBrushTokens.TooltipMaxWidth : UiIconTokens.TooltipMaxWidth);
        }

        /// <summary>As <see cref="Show(RectTransform, string)"/>, wrapping at <paramref name="maxWidth"/> (a lock reason may take two lines).</summary>
        public static void Show(RectTransform target, string text, float maxWidth)
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

            s_instance.Present(root, target, text, maxWidth);
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
            tip._edge = edge;
            tip._plate = UiFactory.NewImage("Plate", rect, UiColor.SurfaceRaised, UiSprites.Control);
            UiFactory.Fill((RectTransform)tip._plate.transform, UiTokens.StrokeControl);

            tip._text = UiFactory.Label("Word", rect, string.Empty, UiType.Label, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            tip._text.raycastTarget = false;
            tip._text.enableWordWrapping = true;
            go.SetActive(false);
            return tip;
        }

        /// <summary>
        /// WP-098A · dress the one tooltip for <see cref="Style"/> when it changed since the last show (a build-time cost:
        /// sprite, colour, face). V3 is exactly what <see cref="Build"/> made.
        /// </summary>
        private void ApplyStyle()
        {
            UiControlStyle style = Style;
            if (_styled && _drawn == style)
            {
                return;
            }

            _styled = true;
            _drawn = style;
            bool brush = style == UiControlStyle.Brush;
            _edge.enabled = !brush;
            Sprite plate = brush ? UiBrushArt.Get(UiBrushSprite.TooltipWash) : UiSprites.Control;
            _plate.sprite = plate;
            if (plate != null && plate.border != Vector4.zero)
            {
                _plate.type = Image.Type.Sliced;
                _plate.pixelsPerUnitMultiplier = 1f;
            }

            Color fill = UiTokens.Get(brush ? UiColor.SurfaceInset : UiColor.SurfaceRaised);
            if (brush)
            {
                fill.a = UiBrushTokens.TooltipWash;
            }

            _plate.color = fill;
            UiFactory.Fill((RectTransform)_plate.transform, brush ? -WashOverhang : UiTokens.StrokeControl);
            UiBrushKit.RestyleText(_text, brush ? UiType.MenuSmall : UiType.Label);
        }

        private void Present(RectTransform root, RectTransform target, string text, float maxWidth)
        {
            _target = target;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            ApplyStyle();
            bool brush = _drawn == UiControlStyle.Brush;
            UiType type = brush ? UiType.MenuSmall : UiType.Label;
            if (brush)
            {
                _text.fontSize = UiTokens.FontSize(type);   // MenuSmall follows the text scale at every show (a 1.25 switch mid-session)
            }

            _text.text = text;

            float padX = brush ? UiBrushTokens.TooltipPadX : (int)UiIconTokens.TooltipPadX;
            float padY = brush ? UiBrushTokens.TooltipPadY : (int)UiIconTokens.TooltipPadY;
            float textWidth = Mathf.Min(maxWidth - 2f * padX, _text.GetPreferredValues(text).x);
            float textHeight = Mathf.Max(UiFactory.LineHeight(type), _text.GetPreferredValues(text, textWidth, 0f).y);
            Vector2 size = new Vector2(Mathf.Ceil(textWidth + 2f * padX), Mathf.Ceil(textHeight + 2f * padY));
            _rect.sizeDelta = size;
            var textRect = (RectTransform)_text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(padX, padY);
            textRect.offsetMax = new Vector2(-padX, -padY);

            _rect.anchoredPosition = Place(root, target, size, brush ? UiBrushTokens.TooltipGap : (int)UiIconTokens.TooltipGap);

            UiMotion.Kill(_show, false);
            _group.alpha = 0f;
            _show = UiMotion.Alpha(_group, 1f, UiDuration.Instant, UiEase.Linear, UiMotionClass.Data, UiIconTokens.TooltipDelaySeconds);
        }

        /// <summary>Centre of the tooltip in the root's space: under the target, else above; clamped inside the safe inset.</summary>
        private static Vector2 Place(RectTransform root, RectTransform target, Vector2 size, float gap)
        {
            target.GetWorldCorners(s_corners);
            Vector2 min = root.InverseTransformPoint(s_corners[0]);
            Vector2 max = root.InverseTransformPoint(s_corners[2]);
            Rect area = root.rect;
            float inset = (int)UiTokens.SafeInset;

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
