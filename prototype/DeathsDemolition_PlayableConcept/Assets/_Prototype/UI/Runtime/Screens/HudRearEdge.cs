using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// M4-R1 N2 · WP-070 §7.1: in third and first person the camera no longer shows the ring around the figure, so the
    /// BEHIND YOU threat (S-03 RearDirection) becomes an arc at the screen's edge, turned to the attackers relative to where
    /// the camera looks: straight behind is the bottom edge, behind-left the lower left. Only while that direction is outside
    /// the view (horizontal half FOV + 10°); first person at full strength (the rear is blind), third person fainter.
    /// Top-down keeps HudPlayerArcs. The arc fades in and out (Fast); placement changes only past 0.5° (no per-frame rebuild).
    /// </summary>
    public sealed class HudRearEdge
    {
        public const float Size = 200f;
        public const float EdgeInset = 96f;
        public const float FirstPersonAlpha = 0.6f;
        public const float ThirdPersonAlpha = 0.45f;   // AD R1-A SHOULD-6: 0.36 was a whisper on slate
        public const float OutOfViewMarginDegrees = 10f;

        // AD R1-A MUST-1: ArcSoft60 draws its stroke between 0.75 and 0.98 of the half-size, ±30° about "up". The stroke's
        // midpoint - not the sprite's pivot - sits on the inset ellipse, and avoidance works on the stroke's own box.
        public const float StrokeMidRadius = Size * 0.5f * 0.984f * 0.875f;   // ≈ 86 px
        private const float StrokeInner = Size * 0.5f * 0.984f * 0.75f;
        private const float StrokeOuter = Size * 0.5f * 0.984f;
        public const float Clearance = 12f;

        /// <summary>The prompt / countdown slot at the bottom centre (AD R1-A MUST-1b), reserved whether or not it shows.</summary>
        public static readonly Vector2 PromptSlot = new Vector2(380f, 56f);

        private static readonly Vector2 OffScreen = new Vector2(-10000f, -10000f);

        private readonly Image _arc;
        private bool _shown;
        private float _alpha = -1f;
        private float _angle = float.NaN;
        private Vector2 _at = new Vector2(float.NaN, float.NaN);

        public HudRearEdge(RectTransform layer)
        {
            _arc = UiFactory.NewImage("RearEdge", layer, UiColor.StateDanger, UiSprites.ArcSoft60);
            _arc.raycastTarget = false;
            _arc.rectTransform.sizeDelta = new Vector2(Size, Size);
            _arc.rectTransform.anchoredPosition = OffScreen;
            _arc.canvasRenderer.SetAlpha(0f);
        }

        public bool Shown
        {
            get { return _shown; }
        }

        /// <summary>The arc's placement in canvas px (tests: on screen, not parked).</summary>
        public Vector2 Position
        {
            get { return _arc.rectTransform.anchoredPosition; }
        }

        /// <summary>The attackers' bearing relative to the camera's heading, degrees (+ right; 180 = behind). NaN when hidden.</summary>
        public float Angle
        {
            get { return _shown ? _angle : float.NaN; }
        }

        /// <summary>The signed bearing of a world direction relative to the camera's flat heading; NaN when either is vertical.</summary>
        public static float BearingFrom(Vector3 cameraForward, Vector3 towardsWorld)
        {
            Vector3 f = Vector3.ProjectOnPlane(cameraForward, Vector3.up);
            Vector3 d = Vector3.ProjectOnPlane(towardsWorld, Vector3.up);
            if (f.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f)
            {
                return float.NaN;
            }

            return Vector3.SignedAngle(f, d, Vector3.up);
        }

        /// <summary>Where an arc at a bearing sits on the canvas: on the ellipse inset from the edges, 0° at the top.</summary>
        public static Vector2 EdgePoint(float bearing, Vector2 canvasSize)
        {
            float r = bearing * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r) * (canvasSize.x * 0.5f - EdgeInset), Mathf.Cos(r) * (canvasSize.y * 0.5f - EdgeInset));
        }

        /// <summary>The visible stroke's box for a sprite centred at <paramref name="centre"/> and turned to <paramref name="bearing"/>.</summary>
        public static Rect StrokeBox(Vector2 centre, float bearing)
        {
            float r = bearing * Mathf.Deg2Rad;
            var u = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            float w = 2f * StrokeOuter * 0.5f;                 // chord at the outer radius: 2 R sin 30°
            float h = StrokeOuter - StrokeInner * 0.866f;      // outer apex down to the inner ends (cos 30°)
            float c = Mathf.Abs(Mathf.Cos(r)), sn = Mathf.Abs(Mathf.Sin(r));
            Vector2 size = new Vector2(w * c + h * sn, w * sn + h * c);
            Vector2 mid = centre + u * ((StrokeOuter + StrokeInner * 0.866f) * 0.5f);
            return new Rect(mid - size * 0.5f, size);
        }

        /// <summary>
        /// AD R1-A MUST-1: where the sprite goes for a bearing. The stroke's midpoint on the ellipse inset <see cref="EdgeInset"/>;
        /// if its stroke would enter an avoided rect + <see cref="Clearance"/>, it lifts away from the nearer horizontal edge
        /// (x unchanged - the bearing stays honest); the whole sprite stays <see cref="Clearance"/> inside the screen.
        /// </summary>
        public static Vector2 Place(float bearing, Vector2 canvasSize, Rect[] avoid, int count)
        {
            float r = bearing * Mathf.Deg2Rad;
            var u = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            Vector2 centre = EdgePoint(bearing, canvasSize) - u * StrokeMidRadius;
            for (int pass = 0; pass < 8; pass++)
            {
                bool moved = false;
                Rect box = StrokeBox(centre, bearing);
                for (int i = 0; i < count; i++)
                {
                    Rect clear = Rect.MinMaxRect(avoid[i].xMin - Clearance, avoid[i].yMin - Clearance, avoid[i].xMax + Clearance, avoid[i].yMax + Clearance);
                    if (!box.Overlaps(clear))
                    {
                        continue;
                    }

                    centre.y += box.center.y < 0f ? clear.yMax - box.yMin : clear.yMin - box.yMax;
                    box = StrokeBox(centre, bearing);
                    moved = true;
                }

                if (!moved)
                {
                    break;
                }
            }

            float hx = canvasSize.x * 0.5f - Clearance - Size * 0.5f, hy = canvasSize.y * 0.5f - Clearance - Size * 0.5f;
            return new Vector2(Mathf.Clamp(centre.x, -hx, hx), Mathf.Clamp(centre.y, -hy, hy));
        }

        /// <param name="on">The BEHIND YOU threat is live (look modes only; the caller keeps top-down on the figure's ring).</param>
        /// <param name="avoid">The HUD's cluster, chip-row, Special, banner and prompt rects in canvas px (centre origin).</param>
        public void Tick(bool on, Vector3 towardsWorld, Camera camera, bool firstPerson, Vector2 canvasSize, Rect[] avoid, int count)
        {
            float bearing = float.NaN;
            if (on && camera != null)
            {
                bearing = BearingFrom(camera.transform.forward, towardsWorld);
                float halfView = Camera.VerticalToHorizontalFieldOfView(camera.fieldOfView, camera.aspect) * 0.5f + OutOfViewMarginDegrees;
                on = !float.IsNaN(bearing) && Mathf.Abs(bearing) > halfView;
            }
            else
            {
                on = false;
            }

            if (on)
            {
                // Recomputed while shown: the avoided rects move (the Special block, the banner, FP's lifted weapon cluster).
                Vector2 at = Place(bearing, canvasSize, avoid, count);
                RectTransform rect = _arc.rectTransform;
                if (float.IsNaN(_angle) || Mathf.Abs(Mathf.DeltaAngle(bearing, _angle)) > 0.5f)
                {
                    _angle = bearing;
                    rect.localRotation = Quaternion.Euler(0f, 0f, -bearing);   // the sprite's arc faces up at 0°
                }

                if (float.IsNaN(_at.x) || (at - _at).sqrMagnitude > 0.25f)   // NaN compares false: the first show must place
                {
                    _at = at;
                    rect.anchoredPosition = at;
                }
            }

            float alpha = on ? (firstPerson ? FirstPersonAlpha : ThirdPersonAlpha) : 0f;
            if (on == _shown && Mathf.Approximately(alpha, _alpha))
            {
                return;
            }

            _shown = on;
            _alpha = alpha;
            UiMotion.TintAlpha(_arc, alpha, UiDuration.Fast, on ? UiEase.Settle : UiEase.Exit, UiMotionClass.Decor);
        }
    }
}
