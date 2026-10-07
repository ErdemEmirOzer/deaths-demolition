using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>WP-098A · which structural brush trace a mark is (each has its own per-window budget, KIT-KILAVUZU §1).</summary>
    public enum UiBrushMarkKind
    {
        /// <summary>C5 · the short dry stroke on the title rule (160–600 px, 12 high).</summary>
        TitleStroke = 0,
        /// <summary>C8 · the selection trace: a 3 px vermilion core on an opaque ink bed, 16 px in, 8 px under its item.</summary>
        Selection = 1,
        /// <summary>C6 · a dry section divider (≤ 2 per window).</summary>
        Divider = 2,
        /// <summary>C37 · the pane's one frame edge: the divider stroke turned upright, 24 px left of the content column.</summary>
        Edge = 3
    }

    /// <summary>
    /// WP-098A (KIT-API §2, §4 "C8"; C-00 07.10 "iz, yüzey değil") · one brush trace: an Image (Selection: an ink bed + a
    /// vermilion core) inked in by the DD/UI/Fx InkReveal. Title, divider and edge wipe along their stroke (left → right; the
    /// edge, turned −90°, top → bottom); the C8 inks through its mask's own time map. No Update: a Draw / Under is one vertex
    /// rebuild and the GPU runs the reveal from <c>_DD_UiTime</c>. Under Reduced Motion the shader shows the mark drawn at once
    /// (Decor), so <see cref="Drawn"/> is true as soon as it is visible.
    ///
    /// USAGE: through <see cref="UiBrushKit"/> only - Title / Divider / EdgeTrace (tracked by their pane, inked in on Open) and
    /// the one C8 of a <see cref="UiBrushChoiceGroup"/> or <see cref="UiBrushKit.SelectionMark"/> (placed by <see cref="Under"/>).
    /// DON'T: build one per item (a group has ONE C8 that moves), tint it in a screen, or spend more than the window budget
    /// (title 1, C8 2, divider 2, edge 1; ≤ 6 traces with the washes - UiOrnamentCensusTests counts the call sites).
    /// ACCESSIBILITY: the C8 core is vermilion only on its opaque ink bed (3.53:1; bedless it is 1.99:1) and never the only
    /// sign of a choice - the chosen item's word / glyph turns TextPrimary too. Title / divider / edge strokes are decoration
    /// in the line tone (3.70:1 on ink): shape, never state. The reveal is Decor (drawn at once under Reduced Motion).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiBrushMark : MonoBehaviour
    {
        /// <summary>Wipe: the sprite's alpha is the shape, the time map orders the ink along the rect (KILAVUZ §4.2 "silme").</summary>
        private static readonly Vector3 Wipe = new Vector3(0f, 1f, 0f);

        /// <summary>Mark: the AP-012 time map's own coverage clips (the C8 fallback IS that mask, so the stroke inks as authored).</summary>
        private static readonly Vector3 Stroke = Vector3.zero;

        private static readonly Vector3[] s_corners = new Vector3[4];

        private UiBrushMarkKind _kind;
        private RectTransform _rect;
        private Image _ink;     // the trace; Selection: the vermilion "Core"
        private Image _bed;     // Selection only: the opaque ink "Bed" under the core
        private UiFx _inkFx;
        private UiFx _bedFx;
        private UiBrushSprite _sprite;
        private UiDuration _duration = UiBrushTokens.Open;
        private bool _hidden;

        public UiBrushMarkKind Kind
        {
            get { return _kind; }
        }

        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>The trace image (Selection: the vermilion core) - tests and captures.</summary>
        public Image Ink
        {
            get { return _ink; }
        }

        /// <summary>Selection: the opaque ink bed under the core; null for the other kinds.</summary>
        public Image Bed
        {
            get { return _bed; }
        }

        /// <summary>False after <see cref="Hide"/> (and while a closed pane holds it), until <see cref="Draw"/> / <see cref="Under"/>.</summary>
        public bool Visible
        {
            get { return !_hidden; }
        }

        /// <summary>The ink has landed (its reveal is over, or Reduced Motion).</summary>
        public bool Drawn
        {
            get
            {
                if (_hidden || _inkFx == null)
                {
                    return false;
                }

                bool still = UiFxGlobals.MotionOverride.HasValue ? !UiFxGlobals.MotionOverride.Value : UiMotion.ReducedMotion;
                return still || UiFxGlobals.Now >= _inkFx.StartTime + _inkFx.Duration;
            }
        }

        /// <summary>Kit-only factory (<see cref="UiBrushKit"/>): the mark under <paramref name="parent"/>, unplaced, drawn (its end state).</summary>
        internal static UiBrushMark Create(UiBrushMarkKind kind, RectTransform parent, string name)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            var mark = root.gameObject.AddComponent<UiBrushMark>();
            mark.Build(kind, root);
            return mark;
        }

        private void Build(UiBrushMarkKind kind, RectTransform root)
        {
            _kind = kind;
            _rect = root;
            switch (kind)
            {
                case UiBrushMarkKind.Selection:
                    _sprite = UiBrushSprite.SelectShort;
                    root.sizeDelta = new Vector2(UiBrushTokens.TabWidth - UiBrushTokens.SelectionInset * 2f, UiBrushTokens.SelectionCore);

                    // The bed: the same mask in ink, the core's thickness again on every side (3 × thick), opaque.
                    _bed = UiFactory.NewImage("Bed", root, UiColor.SurfaceInset, UiBrushArt.Get(_sprite));
                    UiFactory.Outset(_bed.rectTransform, UiBrushTokens.SelectionBed);
                    Color bed = _bed.color;
                    bed.a = UiBrushTokens.BedAlpha;
                    _bed.color = bed;
                    _bedFx = Reveal(_bed, _sprite, UiBrushTokens.Reselect);

                    _ink = UiFactory.NewImage("Core", root, UiColor.AccentPrimary, UiBrushArt.Get(_sprite));
                    UiFactory.Fill(_ink.rectTransform);
                    _inkFx = Reveal(_ink, _sprite, UiBrushTokens.Reselect);
                    _duration = UiBrushTokens.Reselect;
                    break;

                case UiBrushMarkKind.TitleStroke:
                    _sprite = UiBrushSprite.TitleStroke;
                    root.sizeDelta = new Vector2(UiBrushTokens.TitleStrokeMin, UiBrushTokens.TitleStrokeHeight);
                    _ink = LineImage("Stroke", root, _sprite);
                    _inkFx = Reveal(_ink, _sprite, UiBrushTokens.Open);
                    break;

                default:
                    // Divider and Edge share the C6 mask at EdgeWidth; the edge is the divider turned upright (UiBrushKit.EdgeTrace).
                    _sprite = UiBrushSprite.Divider;
                    root.sizeDelta = new Vector2(UiBrushTokens.TitleStrokeMin, UiBrushTokens.EdgeWidth);
                    _ink = LineImage(kind == UiBrushMarkKind.Edge ? "Edge" : "Divider", root, _sprite);
                    _inkFx = Reveal(_ink, _sprite, UiBrushTokens.Open);
                    break;
            }
        }

        private static Image LineImage(string name, RectTransform root, UiBrushSprite sprite)
        {
            Image image = UiFactory.NewImage(name, root, UiColor.TextMuted, UiBrushArt.Get(sprite));
            image.color = UiBrushTokens.LineColour();   // P0 line tone (TextMuted × 0.51); P1 → UiColor.InkLine
            UiFactory.Fill(image.rectTransform);
            return image;
        }

        private static UiFx Reveal(Image image, UiBrushSprite sprite, UiDuration duration)
        {
            image.preserveAspect = false;
            var fx = image.gameObject.AddComponent<UiFx>();
            fx.Configure(UiFxMode.InkReveal, duration, ParamsOf(sprite), UiBrushArt.RevealChannel(sprite));
            return fx;
        }

        /// <summary>
        /// The C8 inks through the AP-012 underline time map in mark mode while its sprite IS that mask (the fallback); an
        /// AP-034 mask has its own shape, so it wipes instead (its own time sheet is P1, S-09). Every other trace wipes.
        /// </summary>
        private static Vector3 ParamsOf(UiBrushSprite sprite)
        {
            bool selection = sprite == UiBrushSprite.SelectShort || sprite == UiBrushSprite.SelectLong;
            return selection && !UiBrushArt.IsAuthored(sprite) ? Stroke : Wipe;
        }

        /// <summary>Ink the mark in along its stroke, <paramref name="delay"/> s from now (Decor: under RM it shows drawn at once).</summary>
        public void Draw(UiDuration duration, float delay = 0f)
        {
            Show();
            SetDuration(duration);
            Play(delay);
        }

        /// <summary>Show nothing until <see cref="Draw"/> (a pane about to open): the ink waits at its start, the image stays on.</summary>
        public void Hold()
        {
            Show();
            if (_inkFx != null) _inkFx.Hold();
            if (_bedFx != null) _bedFx.Hold();
        }

        /// <summary>
        /// Selection: sit under <paramref name="target"/> (16 px in from each end, 8 px below its bottom edge) and redraw over
        /// Reselect (400), or land drawn (a cut) when <paramref name="animate"/> is false or under Reduced Motion. The target's
        /// corners go through a static buffer into this mark's parent space: no allocation.
        /// </summary>
        public void Under(RectTransform target, bool animate)
        {
            if (target == null || _rect == null)
            {
                return;
            }

            var parent = _rect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            target.GetWorldCorners(s_corners);
            Vector2 min = parent.InverseTransformPoint(s_corners[0]);
            Vector2 max = parent.InverseTransformPoint(s_corners[2]);
            float left = Mathf.Min(min.x, max.x), right = Mathf.Max(min.x, max.x), bottom = Mathf.Min(min.y, max.y);
            float width = Mathf.Max(1f, right - left - UiBrushTokens.SelectionInset * 2f);

            // Anchored to the parent's top-left, like the items it marks: a later move of the parent keeps it under them.
            Rect space = parent.rect;
            _rect.anchoredPosition = new Vector2(left + UiBrushTokens.SelectionInset - space.xMin, bottom - UiBrushTokens.SelectionGap - space.yMax);
            _rect.sizeDelta = new Vector2(width, _kind == UiBrushMarkKind.Selection ? UiBrushTokens.SelectionCore : _rect.sizeDelta.y);

            if (_kind == UiBrushMarkKind.Selection)
            {
                // KIT-API §4: ≤ 64 wide is the short mark (rail, tab), longer the long one (segment, 200–504 px).
                SetSprite(width <= 64f ? UiBrushSprite.SelectShort : UiBrushSprite.SelectLong);
            }

            Show();
            if (animate && !UiMotion.ReducedMotion)
            {
                SetDuration(UiBrushTokens.Reselect);
                Play(0f);
            }
            else
            {
                Land();
            }
        }

        /// <summary>Fade out (Fast, Exit; Decor) and stay hidden until <see cref="Under"/> / <see cref="Draw"/>; at once when <paramref name="animate"/> is false.</summary>
        public void Hide(bool animate)
        {
            _hidden = true;
            if (animate)
            {
                FadeOut(_ink);
                FadeOut(_bed);
                return;
            }

            SetAlpha(_ink, 0f);
            SetAlpha(_bed, 0f);
        }

        private static void FadeOut(Image image)
        {
            if (image != null)
            {
                UiMotion.TintAlpha(image, 0f, UiBrushTokens.Close, UiEase.Exit, UiMotionClass.Decor);
            }
        }

        private void Show()
        {
            _hidden = false;
            SetAlpha(_ink, 1f);
            SetAlpha(_bed, 1f);
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null)
            {
                return;
            }

            UiMotion.KillTarget(image, false);
            image.canvasRenderer.SetAlpha(alpha);
        }

        private void SetDuration(UiDuration duration)
        {
            if (_duration == duration)
            {
                return;
            }

            _duration = duration;
            Vector3 parameters = ParamsOf(_sprite);
            UiInkChannel channel = UiBrushArt.RevealChannel(_sprite);
            if (_inkFx != null) _inkFx.Configure(UiFxMode.InkReveal, duration, parameters, channel);
            if (_bedFx != null) _bedFx.Configure(UiFxMode.InkReveal, duration, parameters, channel);
        }

        private void SetSprite(UiBrushSprite id)
        {
            if (_sprite == id)
            {
                return;
            }

            _sprite = id;
            Sprite sprite = UiBrushArt.Get(id);
            if (_ink != null) _ink.sprite = sprite;
            if (_bed != null) _bed.sprite = sprite;
            Vector3 parameters = ParamsOf(id);
            UiInkChannel channel = UiBrushArt.RevealChannel(id);
            if (_inkFx != null) _inkFx.Configure(UiFxMode.InkReveal, _duration, parameters, channel);
            if (_bedFx != null) _bedFx.Configure(UiFxMode.InkReveal, _duration, parameters, channel);
        }

        private void Play(float delay)
        {
            if (_inkFx != null) _inkFx.Play(delay);
            if (_bedFx != null) _bedFx.Play(delay);
        }

        /// <summary>
        /// Drawn now: the reveal started more than one duration ago (UiFx.Play with a negative delay - one vertex rebuild, no
        /// front band); the extra second keeps <see cref="Drawn"/> true against float rounding of "now − d + d".
        /// </summary>
        private void Land()
        {
            if (_inkFx != null) _inkFx.Play(-(_inkFx.Duration + 1f));
            if (_bedFx != null) _bedFx.Play(-(_bedFx.Duration + 1f));
        }
    }
}
