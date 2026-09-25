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

        // AD R1-A MUST-1: the soft arc draws its stroke between 0.75 and 0.98 of the half-size, ±30° about "up". The stroke's
        // midpoint - not the sprite's pivot - sits on the inset ellipse, and avoidance works on the stroke's own box.
        // WP-068 12b (AD R1-A SHOULD-6, the shoulder half): the edge arc is UiSprites.ArcShouldered60 - the same band and span
        // (so the box below holds), but the stroke keeps ≥ 0.5 of its peak out to 80 % of the chord instead of the ≈ 80 px core
        // ArcSoft60's quadratic taper left of the 200 px mark. The E17 damage wedge (HudPlayerArcs) keeps ArcSoft60.
        public const float StrokeMidRadius = Size * 0.5f * 0.984f * 0.875f;   // ≈ 86 px
        private const float StrokeInner = Size * 0.5f * 0.984f * 0.75f;
        private const float StrokeOuter = Size * 0.5f * 0.984f;
        public const float Clearance = 12f;
        private const float EdgeEpsilon = 0.01f;

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
            _arc = UiFactory.NewImage("RearEdge", layer, UiColor.StateDanger, UiSprites.ArcShouldered60);
            _arc.raycastTarget = false;
            _arc.rectTransform.sizeDelta = new Vector2(Size, Size);
            _arc.rectTransform.anchoredPosition = OffScreen;
            _arc.canvasRenderer.SetAlpha(0f);
        }

        public bool Shown
        {
            get { return _shown; }
        }

        /// <summary>The arc's image (tests: its sprite).</summary>
        public Image Arc
        {
            get { return _arc; }
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
        /// if its stroke would enter an avoided rect + <see cref="Clearance"/>, it moves away vertically (x unchanged - the
        /// bearing stays honest); the whole sprite stays <see cref="Clearance"/> inside the screen.
        /// WP-068 12b (AD R1-A SHOULD-10): in the lower half the move was always a lift; now, when the true point (the stroke's
        /// midpoint on the ellipse) lies below the rect's centre, the stroke resolves downward past the rect's bottom edge -
        /// but only where the sprite still fits between that edge and the screen's edge − 12 (with the FP cluster at the bottom
        /// left, x 32–432 × y 820–960, there is seldom room, and then the lift stands). If the downward pass leaves the stroke
        /// in any rect (one lower down lifts it back), the lift-only result is used, so the MUST-1 guarantees hold.
        /// </summary>
        public static Vector2 Place(float bearing, Vector2 canvasSize, Rect[] avoid, int count)
        {
            Vector2 centre = Resolve(bearing, canvasSize, avoid, count, true);
            return IsClear(centre, bearing, avoid, count) ? centre : Resolve(bearing, canvasSize, avoid, count, false);
        }

        /// <summary>As <see cref="Place(float, Vector2, Rect[], int)"/>, with the SHOULD-10 downward resolve on or off (tests compare the two).</summary>
        public static Vector2 Resolve(float bearing, Vector2 canvasSize, Rect[] avoid, int count, bool resolveDownward)
        {
            float r = bearing * Mathf.Deg2Rad;
            var u = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            Vector2 truePoint = EdgePoint(bearing, canvasSize);
            Vector2 centre = truePoint - u * StrokeMidRadius;
            float lowest = -canvasSize.y * 0.5f + Clearance + Size * 0.5f;   // the lowest centre the clamp below keeps
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

                    float down = clear.yMin - box.yMax;
                    float move = box.center.y < 0f ? clear.yMax - box.yMin : down;
                    if (resolveDownward && move > 0f && truePoint.y < clear.center.y && centre.y + down >= lowest)
                    {
                        move = down - EdgeEpsilon;   // a hair past the edge: landing exactly on it reads as an overlap next pass
                    }

                    centre.y += move;
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

        /// <summary>True when the stroke at <paramref name="centre"/> stays clear of every avoided rect + <see cref="Clearance"/>.</summary>
        private static bool IsClear(Vector2 centre, float bearing, Rect[] avoid, int count)
        {
            Rect box = StrokeBox(centre, bearing);
            for (int i = 0; i < count; i++)
            {
                if (box.Overlaps(Rect.MinMaxRect(avoid[i].xMin - Clearance, avoid[i].yMin - Clearance, avoid[i].xMax + Clearance, avoid[i].yMax + Clearance)))
                {
                    return false;
                }
            }

            return true;
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
                // Recomputed while shown: the avoided rects move (the Special block, the banner).
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
