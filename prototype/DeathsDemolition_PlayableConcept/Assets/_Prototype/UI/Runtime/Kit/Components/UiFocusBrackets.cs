using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #2 FocusRing - the travelling half (direction §4): one set
    /// of focus brackets that moves from the control that lost focus to the one that gained it
    /// (Move 140 for the position and the size together). The v3 controls' own ring and halo
    /// stay where they are and appear on frame 1.
    /// WP-098A (KIT-KILAVUZU §1 C7, S-09 S4 - global): the brackets are cream arms
    /// (<see cref="UiBrushTokens.BracketArm"/> 10 × <see cref="UiBrushTokens.BracketStroke"/> 3, TextPrimary,
    /// <see cref="UiSprites.FocusBracketsInk"/>) on a 2 px ink halo (BorderFocusHalo, <see cref="UiSprites.FocusBracketsHalo"/>,
    /// drawn in a rect 2 px larger so it rims the arms on every side), <see cref="Outset"/> (8) off the control - and they
    /// show fully opaque on the cut (the first frame): for a Brush control, which has no ring, they ARE the proof of focus.
    /// The root carries no image of its own: halo, then arms, as children.
    ///
    /// Driven by the controls' own select / deselect (UiButton calls <see cref="Follow"/> and
    /// <see cref="Release"/>): no per-frame polling. The brackets live under the focused
    /// control's nearest canvas, drawn last; changing canvas (a modal, the pause overlay)
    /// is a cut, not a flight across layers.
    ///
    /// USAGE: automatic for every UiButton (rows, switches, steppers, cards, modal actions).
    /// DON'T: parent anything to the brackets, or call Follow for a control that is not
    /// focused (the brackets mean "this has focus").
    /// ACCESSIBILITY: cream on its ink halo is 16.40:1 on any world; on the cut (first focus, a
    /// new canvas) the arms are at α 1 in that frame - only the flight is Decor (Reduced Motion
    /// lands them at the destination); v3 controls keep their ring as well.
    /// </summary>
    public sealed class UiFocusBrackets : MonoBehaviour
    {
        /// <summary>Distance from the control's edge to the brackets: ring 3 + halo 3 + 2 px air.</summary>
        public const float Outset = UiTokens.StrokeFocusRing + UiTokens.StrokeFocusHalo + 2f;

        private static UiFocusBrackets s_instance;
        private static readonly Vector3[] s_corners = new Vector3[4];

        private RectTransform _rect;
        private Image _halo;
        private Image _arms;
        private RectTransform _target;
        private int _generation;

        /// <summary>The control the brackets are on (or travelling to); null when hidden.</summary>
        public static RectTransform Target
        {
            get { return s_instance != null && s_instance.gameObject.activeSelf ? s_instance._target : null; }
        }

        /// <summary>The brackets' rect (tests and captures).</summary>
        public static RectTransform Rect
        {
            get { return s_instance != null ? s_instance._rect : null; }
        }

        /// <summary>WP-098A · the cream arms (their ink halo sits under them); tests read their alpha on the cut frame (= 1).</summary>
        public static Image Arms
        {
            get { return s_instance != null ? s_instance._arms : null; }
        }

        /// <summary>WP-098A · the arms' 2 px ink halo.</summary>
        public static Image Halo
        {
            get { return s_instance != null ? s_instance._halo : null; }
        }

        /// <summary>Bring the brackets to <paramref name="target"/>: a flight from the previous control on the same canvas, a cut otherwise.</summary>
        public static void Follow(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            Canvas canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var space = (RectTransform)canvas.transform;
            UiFocusBrackets brackets = s_instance;
            if (brackets == null)
            {
                brackets = Create(space);
            }

            bool cut = brackets._target == null || !brackets.gameObject.activeSelf || brackets._rect.parent != space;
            if (brackets._rect.parent != space)
            {
                brackets._rect.SetParent(space, false);
            }

            brackets._rect.SetAsLastSibling();
            brackets._target = target;
            brackets._generation++;
            brackets.gameObject.SetActive(true);

            Vector2 centre, size;
            Measure(space, target, out centre, out size);

            // WP-098A: opaque at once on the cut and during a flight - no fade-in (KILAVUZ §1: the brackets show on frame 1).
            brackets.Opaque();
            if (cut)
            {
                UiMotion.KillTarget(brackets._rect, false);
                brackets._rect.anchoredPosition = centre;
                brackets._rect.sizeDelta = size;
                return;
            }

            UiPresets.FocusTravel(brackets._rect, centre);
            UiMotion.Resize(brackets._rect, size, UiDuration.Fast, UiEase.Move, UiMotionClass.Decor);
        }

        private void Opaque()
        {
            UiMotion.KillTarget(_halo, false);
            UiMotion.KillTarget(_arms, false);
            _halo.canvasRenderer.SetAlpha(1f);
            _arms.canvasRenderer.SetAlpha(1f);
        }

        /// <summary>
        /// <paramref name="target"/> lost focus. The brackets wait one frame: if another
        /// control claims them in that frame (normal navigation) they fly there; otherwise
        /// (focus cleared, screen closed) they go.
        /// </summary>
        public static void Release(RectTransform target)
        {
            UiFocusBrackets brackets = s_instance;
            if (brackets == null || brackets._target != target)
            {
                return;
            }

            if (!brackets.isActiveAndEnabled)
            {
                brackets.Hide();
                return;
            }

            brackets.StartCoroutine(brackets.HideIfUnclaimed(brackets._generation));
        }

        /// <summary>Hide at once (tests, a screen that clears focus on purpose).</summary>
        public static void HideNow()
        {
            if (s_instance != null)
            {
                s_instance.Hide();
            }
        }

        private IEnumerator HideIfUnclaimed(int generation)
        {
            yield return null;
            if (generation == _generation)
            {
                Hide();
            }
        }

        private void Hide()
        {
            _target = null;
            UiMotion.KillTarget(_rect, false);
            gameObject.SetActive(false);
        }

        private static UiFocusBrackets Create(RectTransform space)
        {
            RectTransform root = UiFactory.NewRect("FocusBrackets", space);
            var brackets = root.gameObject.AddComponent<UiFocusBrackets>();
            brackets._rect = root;

            // The halo first (under), 2 px larger than the arms' rect, so its sprite rims the arms inside, outside and past the tips.
            brackets._halo = UiFactory.NewImage("Halo", root, UiColor.BorderFocusHalo, UiSprites.FocusBracketsHalo);
            UiFactory.Outset(brackets._halo.rectTransform, UiBrushTokens.BracketHalo);
            brackets._arms = UiFactory.NewImage("Arms", root, UiColor.TextPrimary, UiSprites.FocusBracketsInk);
            UiFactory.Fill(brackets._arms.rectTransform);
            brackets._halo.raycastTarget = false;
            brackets._arms.raycastTarget = false;
            s_instance = brackets;
            return brackets;
        }

        /// <summary>The target's rect in <paramref name="space"/>, as an anchored centre (anchors at the space's centre) and an outset size.</summary>
        private static void Measure(RectTransform space, RectTransform target, out Vector2 centre, out Vector2 size)
        {
            target.GetWorldCorners(s_corners);
            Vector2 min = space.InverseTransformPoint(s_corners[0]);
            Vector2 max = space.InverseTransformPoint(s_corners[2]);
            centre = (min + max) * 0.5f - space.rect.center;
            size = new Vector2(Mathf.Abs(max.x - min.x) + Outset * 2f, Mathf.Abs(max.y - min.y) + Outset * 2f);
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
    }
}
