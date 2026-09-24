using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 6 · UiScrollView: a vertical scroller for a pane whose content can
    /// outgrow its card. Two things push content past the card: TextScale 1.25 and a short
    /// Mac window. Content is laid out top-down with a <see cref="UiStack"/> on
    /// <see cref="Content"/>, then <see cref="SetContentHeight"/> is called once. Wheel,
    /// trackpad and drag scroll it; keyboard / gamepad focus pulls the focused row into view
    /// (Move 140, Decor); a 4 px rail on the right shows the extent and only appears when
    /// something is hidden.
    ///
    /// USAGE: a pane of settings or rows that must stay reachable at 1.25 / Mac heights.
    /// DON'T: nest scrollers, or scroll a screen whose layout is fixed by design (HUD, Results).
    /// Do not hide a primary action below the fold — the first focus stays at the top.
    /// ACCESSIBILITY: focus is never lost below the fold — every Selectable under
    /// <see cref="Content"/> (after <see cref="TrackSelectables"/>) scrolls itself into view
    /// with a margin when selected. The rail's grip is a pointer target (16 px hit) and stays
    /// out of the focus chain, so the pad never lands on it. Reduced Motion lands the
    /// scroll at once.
    /// </summary>
    public sealed class UiScrollView : MonoBehaviour
    {
        public const float RailWidth = 4f;
        public const float RailHit = 16f;

        /// <summary>Space kept free on the right of the content for the rail - always, so text measured at build keeps its width when the rail appears.</summary>
        public const float Gutter = RailHit + 8f;

        /// <summary>Space kept above / below a row that focus scrolls into view.</summary>
        public const float RevealMargin = 16f;

        /// <summary>When no more than this is left beyond a revealed row (a section head above, a caption below), the scroll goes to that end.</summary>
        public const float RevealTail = 96f;

        public const float WheelStep = 40f;

        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private Scrollbar _bar;

        /// <summary>Lay rows out here (a UiStack on it), then call <see cref="SetContentHeight"/>.</summary>
        public RectTransform Content
        {
            get { return _content; }
        }

        public RectTransform Viewport
        {
            get { return _viewport; }
        }

        /// <summary>True when the content is taller than the viewport.</summary>
        public bool Scrolls
        {
            get { return _content.rect.height > _viewport.rect.height + 0.5f; }
        }

        /// <summary>How far the content is scrolled down, in reference px (0 = top).</summary>
        public float Offset
        {
            get { return _content.anchoredPosition.y; }
        }

        public bool RailVisible
        {
            get { return _bar != null && _bar.gameObject.activeSelf; }
        }

        /// <summary>A scroller filling <paramref name="parent"/>.</summary>
        public static UiScrollView Create(string name, RectTransform parent)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            UiFactory.Fill(root);
            var view = root.gameObject.AddComponent<UiScrollView>();
            view.Build(root);
            return view;
        }

        private void Build(RectTransform root)
        {
            _viewport = UiFactory.NewRect("Viewport", root);
            UiFactory.Fill(_viewport);
            UiFactory.ClipContent(_viewport);
            // A transparent raycast surface: the wheel over a gap between rows still scrolls.
            Image catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = UiTokens.Get(UiColor.Transparent);
            catcher.raycastTarget = true;

            _content = UiFactory.NewRect("Content", _viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(-Gutter, 0f);
            _content.anchoredPosition = new Vector2(-Gutter * 0.5f, 0f);

            _bar = BuildRail(root);

            _scroll = root.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = false;
            _scroll.scrollSensitivity = WheelStep;
            _scroll.verticalScrollbar = _bar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        /// <summary>The 4 px rail + grip; a Scrollbar for pointer drag, kept out of navigation.</summary>
        private static Scrollbar BuildRail(RectTransform root)
        {
            RectTransform railRoot = UiFactory.NewRect("Rail", root);
            railRoot.anchorMin = new Vector2(1f, 0f);
            railRoot.anchorMax = new Vector2(1f, 1f);
            railRoot.pivot = new Vector2(1f, 0.5f);
            railRoot.sizeDelta = new Vector2(RailHit, 0f);
            railRoot.anchoredPosition = Vector2.zero;

            Image track = UiFactory.NewImage("Track", railRoot, UiColor.SurfaceInset, UiSprites.Chip);
            RectTransform trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0.5f, 0f);
            trackRect.anchorMax = new Vector2(0.5f, 1f);
            trackRect.sizeDelta = new Vector2(RailWidth, 0f);
            track.raycastTarget = false;

            RectTransform slideArea = UiFactory.NewRect("SlideArea", railRoot);
            UiFactory.Fill(slideArea);

            // The grip: a transparent 16 px hit rect the Scrollbar resizes, a 4 px pill inside it.
            Image hit = UiFactory.NewImage("Grip", slideArea, UiColor.Transparent, UiSprites.Solid);
            hit.raycastTarget = true;
            RectTransform gripRect = (RectTransform)hit.transform;
            Image pill = UiFactory.NewImage("Pill", gripRect, UiColor.BorderStrong, UiSprites.Chip);
            RectTransform pillRect = (RectTransform)pill.transform;
            pillRect.anchorMin = new Vector2(0.5f, 0f);
            pillRect.anchorMax = new Vector2(0.5f, 1f);
            pillRect.sizeDelta = new Vector2(RailWidth, 0f);
            pill.raycastTarget = false;

            var bar = railRoot.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = gripRect;
            bar.targetGraphic = hit;
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.transition = Selectable.Transition.None;
            var navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            bar.navigation = navigation;
            return bar;
        }

        /// <summary>Set once after the rows are laid out (and again if they change).</summary>
        public void SetContentHeight(float height)
        {
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, Mathf.Ceil(height));
            ScrollTo(Mathf.Min(Offset, MaxOffset), false);
        }

        private float MaxOffset
        {
            get { return Mathf.Max(0f, _content.rect.height - _viewport.rect.height); }
        }

        /// <summary>Every Selectable under <see cref="Content"/> scrolls itself into view when it gets focus.</summary>
        public void TrackSelectables()
        {
            foreach (Selectable selectable in _content.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable.GetComponent<RevealOnSelect>() == null)
                {
                    selectable.gameObject.AddComponent<RevealOnSelect>().Owner = this;
                }
            }
        }

        /// <summary>Scroll the least distance that shows <paramref name="target"/> with <see cref="RevealMargin"/> around it.</summary>
        public void Reveal(RectTransform target, bool animate = true)
        {
            if (target == null || !target.IsChildOf(_content))
            {
                return;
            }

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float top = -_content.InverseTransformPoint(corners[1]).y;      // distance below the content top
            float bottom = -_content.InverseTransformPoint(corners[0]).y;
            float offset = Offset;
            float height = _viewport.rect.height;
            float next = offset;
            if (top - RevealMargin < offset)
            {
                next = top <= RevealTail ? 0f : top - RevealMargin;
            }
            else if (bottom + RevealMargin > offset + height)
            {
                next = _content.rect.height - bottom <= RevealTail ? MaxOffset : bottom + RevealMargin - height;
            }

            if (Mathf.Approximately(next, offset))
            {
                return;
            }

            // The focus brackets measured the row where it was (its own OnSelect ran first):
            // re-aim them at where it is going, so both arrive together.
            if (UiFocusBrackets.Target == target)
            {
                Vector2 now = _content.anchoredPosition;
                _content.anchoredPosition = new Vector2(now.x, Mathf.Clamp(next, 0f, MaxOffset));
                UiFocusBrackets.Follow(target);
                _content.anchoredPosition = now;
            }

            ScrollTo(next, animate);
        }

        /// <summary>Scroll to <paramref name="offset"/> (clamped). Animated scrolls are Decor (Reduced Motion lands them).</summary>
        public void ScrollTo(float offset, bool animate)
        {
            _scroll.StopMovement();
            var to = new Vector2(_content.anchoredPosition.x, Mathf.Clamp(offset, 0f, MaxOffset));
            if (animate)
            {
                UiMotion.Move(_content, to, UiDuration.Fast, UiEase.Move, UiMotionClass.Decor);
            }
            else
            {
                UiMotion.KillTarget(_content, false);
                _content.anchoredPosition = to;
            }
        }

        /// <summary>Forwards a Selectable's focus to its scroller (added by <see cref="TrackSelectables"/>).</summary>
        private sealed class RevealOnSelect : MonoBehaviour, ISelectHandler
        {
            public UiScrollView Owner;

            public void OnSelect(BaseEventData eventData)
            {
                if (Owner != null)
                {
                    Owner.Reveal((RectTransform)transform);
                }
            }
        }
    }
}
