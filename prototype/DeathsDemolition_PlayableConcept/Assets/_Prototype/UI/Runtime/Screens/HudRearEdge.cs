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
        public const float ThirdPersonAlpha = 0.36f;
        public const float OutOfViewMarginDegrees = 10f;

        private static readonly Vector2 OffScreen = new Vector2(-10000f, -10000f);

        private readonly Image _arc;
        private bool _shown;
        private float _alpha = -1f;
        private float _angle = float.NaN;
        private Vector2 _canvas;

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

        /// <param name="on">The BEHIND YOU threat is live (look modes only; the caller keeps top-down on the figure's ring).</param>
        public void Tick(bool on, Vector3 towardsWorld, Camera camera, bool firstPerson, Vector2 canvasSize)
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

            if (on && (float.IsNaN(_angle) || Mathf.Abs(Mathf.DeltaAngle(bearing, _angle)) > 0.5f || canvasSize != _canvas))
            {
                _angle = bearing;
                _canvas = canvasSize;
                RectTransform rect = _arc.rectTransform;
                rect.anchoredPosition = EdgePoint(bearing, canvasSize);
                rect.localRotation = Quaternion.Euler(0f, 0f, -bearing);   // the sprite's arc faces up at 0°
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
