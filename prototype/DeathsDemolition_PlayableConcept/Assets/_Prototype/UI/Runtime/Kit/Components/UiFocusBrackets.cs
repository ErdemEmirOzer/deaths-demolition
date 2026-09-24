using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #2 FocusRing - the travelling half (direction §4): one set
    /// of focus brackets (four 2 px corner arms, <see cref="UiSprites.FocusBrackets"/>) that
    /// moves from the control that lost focus to the one that gained it (Move 140 for the
    /// position and the size together). The per-control ring and halo stay where they are
    /// and appear on frame 1 - the brackets are the motion that says WHERE focus went, the
    /// ring is the proof that it arrived.
    ///
    /// Driven by the controls' own select / deselect (UiButton calls <see cref="Follow"/> and
    /// <see cref="Release"/>): no per-frame polling. The brackets live under the focused
    /// control's nearest canvas, drawn last; changing canvas (a modal, the pause overlay)
    /// is a cut, not a flight across layers.
    ///
    /// USAGE: automatic for every UiButton (rows, switches, steppers, cards, modal actions).
    /// DON'T: parent anything to the brackets, or call Follow for a control that is not
    /// focused (the brackets mean "this has focus").
    /// ACCESSIBILITY: the ring is the guarantee (3 px Focus400 + 3 px halo, frame 1); the
    /// brackets are Decor - Reduced Motion lands them at the destination with no flight.
    /// </summary>
    public sealed class UiFocusBrackets : MonoBehaviour
    {
        /// <summary>Distance from the control's edge to the brackets: ring 3 + halo 3 + 2 px air.</summary>
        public const float Outset = UiTokens.StrokeFocusRing + UiTokens.StrokeFocusHalo + 2f;

        private static UiFocusBrackets s_instance;
        private static readonly Vector3[] s_corners = new Vector3[4];

        private RectTransform _rect;
        private Image _image;
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
            if (cut)
            {
                UiMotion.KillTarget(brackets._rect, false);
                brackets._rect.anchoredPosition = centre;
                brackets._rect.sizeDelta = size;
                brackets._image.canvasRenderer.SetAlpha(0f);
                UiMotion.TintAlpha(brackets._image, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
                return;
            }

            UiPresets.FocusTravel(brackets._rect, centre);
            UiMotion.Resize(brackets._rect, size, UiDuration.Fast, UiEase.Move, UiMotionClass.Decor);
            UiMotion.TintAlpha(brackets._image, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
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
            Image image = UiFactory.NewImage("FocusBrackets", space, UiColor.BorderFocus, UiSprites.FocusBrackets);
            image.raycastTarget = false;
            var brackets = image.gameObject.AddComponent<UiFocusBrackets>();
            brackets._image = image;
            brackets._rect = (RectTransform)image.transform;
            brackets._rect.anchorMin = new Vector2(0.5f, 0.5f);
            brackets._rect.anchorMax = new Vector2(0.5f, 0.5f);
            brackets._rect.pivot = new Vector2(0.5f, 0.5f);
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
