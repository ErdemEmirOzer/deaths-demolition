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
    ///
    /// WP-098A P0b (C-00 07.10) · C35 Brush style (<see cref="UiControlStyle.Brush"/>, KILAVUZ §4.1, KIT-API §4 "Scroll"):
    /// the frameless pane's scroller - the edge ramps fade into the ink ground (SurfaceInset, <see cref="BrushEdgeColor"/>),
    /// the rail is a 1 px line in the line colour (only while something is hidden, as before), the grip a 4 px cream
    /// brush (AP-034's ScrollThumb, else Chip) never shorter than <see cref="UiBrushTokens.ScrollGripMin"/>: the slide area
    /// is inset by half that at each end and the grip overhangs its handle by the same, so it reaches both ends of the
    /// rail without an Update. Its 16 px hit and the focus behaviour are V3's. V3 (the default) is unchanged.
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
        public const float ClipSoftness = 16f;

        /// <summary>The ground the scroller sits on (the pane card's fill); the edge ramps fade into it.</summary>
        public const UiColor EdgeColor = UiColor.SurfacePanel;

        /// <summary>WP-098A: a Brush scroller's ground - the frameless pane's ink (C1 + C2), not a card fill.</summary>
        public const UiColor BrushEdgeColor = UiColor.SurfaceInset;

        private UiControlStyle _style;

        /// <summary>WP-098A: the dialect this scroller is drawn in.</summary>
        public UiControlStyle Style
        {
            get { return _style; }
        }

        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private Scrollbar _bar;
        private Image _fadeTop;
        private Image _fadeBottom;

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

        /// <summary>A scroller filling <paramref name="parent"/>. WP-098A: <paramref name="style"/> Brush draws C35 (<see cref="UiBrushKit.Scroll"/>).</summary>
        public static UiScrollView Create(string name, RectTransform parent, UiControlStyle style = UiControlStyle.V3)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            UiFactory.Fill(root);
            var view = root.gameObject.AddComponent<UiScrollView>();
            view._style = style;
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

            // A row cut by the edge reads as scrolling, not as a clipped label (AD 6ab SHOULD-2,
            // revised: RectMask2D softness does not reach TMP 3.0.6 text, a ground-coloured ramp
            // over the content does). Each ramp shows only while there is content beyond its edge.
            UiColor ground = _style == UiControlStyle.Brush ? BrushEdgeColor : EdgeColor;
            _fadeTop = EdgeFade("FadeTop", root, UiSprites.FadeDown, 1f, ground);
            _fadeBottom = EdgeFade("FadeBottom", root, UiSprites.FadeUp, 0f, ground);

            _bar = BuildRail(root, _style);

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
            _scroll.onValueChanged.AddListener(_ => UpdateFades());
        }

        /// <summary>A <see cref="ClipSoftness"/> px ramp in the pane's ground colour on one edge of the viewport, drawn over the content.</summary>
        private static Image EdgeFade(string name, RectTransform root, Sprite ramp, float edge, UiColor ground)
        {
            Image fade = UiFactory.NewImage(name, root, ground, ramp);
            var rect = (RectTransform)fade.transform;
            rect.anchorMin = new Vector2(0f, edge);
            rect.anchorMax = new Vector2(1f, edge);
            rect.pivot = new Vector2(0.5f, edge);
            rect.sizeDelta = new Vector2(-Gutter, ClipSoftness);
            rect.anchoredPosition = new Vector2(-Gutter * 0.5f, 0f);
            fade.raycastTarget = false;
            fade.enabled = false;
            return fade;
        }

        /// <summary>Top ramp while scrolled down, bottom ramp while content remains below.</summary>
        private void UpdateFades()
        {
            float offset = Offset;
            _fadeTop.enabled = offset > 0.5f;
            _fadeBottom.enabled = offset < MaxOffset - 0.5f;
        }

        /// <summary>The 4 px rail + grip; a Scrollbar for pointer drag, kept out of navigation. WP-098A Brush: 1 px line rail, 4 px cream grip ≥ 40.</summary>
        private static Scrollbar BuildRail(RectTransform root, UiControlStyle style)
        {
            bool brush = style == UiControlStyle.Brush;
            RectTransform railRoot = UiFactory.NewRect("Rail", root);
            railRoot.anchorMin = new Vector2(1f, 0f);
            railRoot.anchorMax = new Vector2(1f, 1f);
            railRoot.pivot = new Vector2(1f, 0.5f);
            railRoot.sizeDelta = new Vector2(RailHit, 0f);
            railRoot.anchoredPosition = Vector2.zero;

            Image track = UiFactory.NewImage("Track", railRoot, UiColor.SurfaceInset, brush ? UiSprites.Solid : UiSprites.Chip);
            RectTransform trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0.5f, 0f);
            trackRect.anchorMax = new Vector2(0.5f, 1f);
            trackRect.sizeDelta = new Vector2(brush ? UiTokens.StrokeHairline : RailWidth, 0f);
            track.raycastTarget = false;
            if (brush)
            {
                track.color = UiBrushTokens.LineColour();
            }

            // Brush: the handle travels a slide area inset by half the grip floor at each end, and the drawn grip overhangs
            // the handle by that half at each end - so the grip is never under ScrollGripMin and still meets both rail ends.
            float overhang = brush ? UiBrushTokens.ScrollGripMin * 0.5f : 0f;
            RectTransform slideArea = UiFactory.NewRect("SlideArea", railRoot);
            UiFactory.Fill(slideArea);
            slideArea.offsetMin = new Vector2(0f, overhang);
            slideArea.offsetMax = new Vector2(0f, -overhang);

            // The grip: a transparent 16 px hit rect the Scrollbar resizes, a 4 px pill inside it.
            Image hit = UiFactory.NewImage("Grip", slideArea, UiColor.Transparent, UiSprites.Solid);
            hit.raycastTarget = true;
            RectTransform gripRect = (RectTransform)hit.transform;
            Image pill = brush
                ? UiFactory.NewImage("Pill", gripRect, UiColor.TextPrimary, UiBrushArt.Get(UiBrushSprite.ScrollThumb))   // cream (G15), never gold
                : UiFactory.NewImage("Pill", gripRect, UiColor.BorderStrong, UiSprites.Chip);
            RectTransform pillRect = (RectTransform)pill.transform;
            pillRect.anchorMin = new Vector2(0.5f, 0f);
            pillRect.anchorMax = new Vector2(0.5f, 1f);
            pillRect.sizeDelta = new Vector2(brush ? UiBrushTokens.ScrollGrip : RailWidth, 2f * overhang);
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
            UpdateFades();
        }

        /// <summary>Tests: which edge ramps show.</summary>
        public bool FadeTopVisible
        {
            get { return _fadeTop.enabled; }
        }

        public bool FadeBottomVisible
        {
            get { return _fadeBottom.enabled; }
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
                UpdateFades();   // an animated scroll is caught by ScrollRect.onValueChanged (LateUpdate)
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
