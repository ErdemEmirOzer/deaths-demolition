using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8b · E15 the honest reticle (S-02 behaviour, S-09 look). Four cream ticks with an ink edge (G1 SDF,
    /// outline) sit at the weapon's REAL spread projected at the aim distance - ClassicWeaponHudState.CurrentSpreadDegrees,
    /// followed with a 0.05 s half-life, no canned kick. Pellet weapons add the pellet ring (PelletRingDegrees). Past
    /// <see cref="SaturationRadius"/> the ticks turn into arcs (the cone saturated). Reload: a hairline arc fills with
    /// ReloadProgress01 and the ticks drop to 0.4 so the eye stays on the aim. Empty: the ticks split 6 px outward. A live
    /// Special tints it Gold400. Reduced motion: no follow, the spread quantised to three steps. Never flashes (hits are
    /// HitMarkerLayer's). Writes only on a visible change; lives on the HUD's reticle canvas.
    /// </summary>
    public sealed class HudReticle
    {
        // M4-R1 N21 (user 24.09 "the cursor is too big"; ui-ux-designer ruling): 3 × 10 → 2 × 7, the saturated arc 22 → 16,
        // in reference px at UI scale 1. The UI scale setting no longer fattens the crosshair (base / uiScale), and a
        // tick never drops under 2 × 6 physical px. The spread radius is gameplay data (U12) and is not scaled.
        public const float TickWidth = 2f;
        public const float TickLength = 7f;
        public const float ArcWidth = 16f;
        public const float MinTickWidthPx = 2f;
        public const float MinTickLengthPx = 6f;
        public const float MinRadius = 6f;
        public const float SaturationRadius = 48f;
        public const float EmptySplit = 6f;
        public const float FollowHalfLife = 0.05f;
        public const float ReloadGap = 6f;
        public const float ReloadTickAlpha = 0.4f;

        /// <summary>SDF outline width (distance units) - the ink edge of the ticks.</summary>
        private static readonly Vector3 InkEdge = new Vector3(0.3f, 0f, 0f);

        private readonly RectTransform _root;
        private readonly Image[] _ticks = new Image[4];
        private readonly Image _pellet;
        private readonly Image _reload;
        private Sprite _barSprite;
        private Sprite _arcSprite;
        private float _radius = -1f;
        private float _shownRadius = -1f;
        private float _shownPellet = -1f;
        private bool _saturated;
        private int _state = -1;       // 0 ready, 1 reloading, 2 empty
        private bool _gold;
        private float _shownReload = -1f;
        private float _lastTime = -1f;
        private Canvas _canvas;
        private float _geomUi = -1f;
        private float _geomScale = -1f;
        private Vector2 _tick = new Vector2(TickWidth, TickLength);
        private Vector2 _arc = new Vector2(ArcWidth, TickLength * 0.8f);

        public RectTransform Root
        {
            get { return _root; }
        }

        public float Radius
        {
            get { return _shownRadius; }
        }

        /// <summary>The tick's size now, in canvas units (tests, captures).</summary>
        public Vector2 TickSize
        {
            get { return _tick; }
        }

        /// <summary>
        /// N21: the tick (and the saturated arc) in canvas units for a UI scale and the canvas's physical px per unit:
        /// the base divided by the UI scale, floored at <see cref="MinTickWidthPx"/> × <see cref="MinTickLengthPx"/> physical px.
        /// </summary>
        public static void TickGeometry(float uiScale, float pxPerUnit, out Vector2 tick, out Vector2 arc)
        {
            float ui = Mathf.Max(0.01f, uiScale);
            float px = Mathf.Max(0.01f, pxPerUnit);
            tick = new Vector2(Mathf.Max(TickWidth / ui, MinTickWidthPx / px), Mathf.Max(TickLength / ui, MinTickLengthPx / px));
            arc = new Vector2(Mathf.Max(ArcWidth / ui, MinTickWidthPx / px), tick.y * 0.8f);
        }

        public HudReticle(RectTransform layer)
        {
            _root = UiFactory.NewRect("Reticle", layer);
            _root.sizeDelta = Vector2.zero;

            _barSprite = UiArt.Icon("Pip.Full");
            _arcSprite = UiArt.Icon("Reticle.SpreadArc");

            _pellet = UiFactory.NewImage("PelletRing", _root, UiColor.TextPrimary, UiSprites.RingHairline);
            Color pellet = _pellet.color;
            pellet.a = 0.45f;
            _pellet.color = pellet;
            _pellet.enabled = false;

            _reload = UiFactory.NewImage("ReloadArc", _root, UiColor.BarReload, UiSprites.RingHairline);
            _reload.type = Image.Type.Filled;
            _reload.fillMethod = Image.FillMethod.Radial360;
            _reload.fillOrigin = (int)Image.Origin360.Top;
            _reload.fillClockwise = true;
            _reload.fillAmount = 0f;
            _reload.enabled = false;

            for (int i = 0; i < _ticks.Length; i++)
            {
                Image tick = UiFactory.NewImage("Tick." + i, _root, UiColor.TextPrimary, _barSprite != null ? _barSprite : UiSprites.Solid);
                tick.preserveAspect = false;
                if (UiArt.IsGlyph(tick.sprite))
                {
                    UiArt.ApplyGlyph(tick, InkEdge);
                }

                var rect = (RectTransform)tick.transform;
                rect.sizeDelta = new Vector2(TickWidth, TickLength);
                rect.localRotation = Quaternion.Euler(0f, 0f, -90f * i);   // N, E, S, W
                _ticks[i] = tick;
            }
        }

        /// <summary>
        /// One frame. <paramref name="aimWorld"/> / <paramref name="originWorld"/>: the aim point and where the shot leaves;
        /// <paramref name="canvasSize"/>: the HUD canvas in reference px.
        /// </summary>
        public void Tick(Camera camera, Vector3 aimWorld, Vector3 originWorld, Vector2 canvasSize, in ClassicWeaponHudState weapon, bool specialActive)
        {
            // Direction §2.5 Follow: a value-coupled element eases towards the value with a half-life (unscaled clock).
            float now = Time.unscaledTime;
            float dt = _lastTime < 0f ? 0f : Mathf.Min(0.1f, now - _lastTime);
            _lastTime = now;
            float target = Project(camera, aimWorld, originWorld, canvasSize, weapon.CurrentSpreadDegrees);
            float pellet = weapon.PelletRingDegrees > 0f ? Project(camera, aimWorld, originWorld, canvasSize, weapon.PelletRingDegrees) : 0f;
            bool reduced = UiMotion.ReducedMotion;
            if (reduced)
            {
                target = Quantise(target);
                _radius = target;
            }
            else if (_radius < 0f)
            {
                _radius = target;
            }
            else
            {
                _radius += (target - _radius) * (1f - Mathf.Pow(2f, -dt / FollowHalfLife));
            }

            // N21: the tick geometry follows the UI scale and the resolution (both change only from the settings / a resize).
            if (_canvas == null)
            {
                _canvas = _root.GetComponentInParent<Canvas>();
            }

            float pxPerUnit = _canvas != null ? _canvas.rootCanvas.scaleFactor : 1f;
            float uiScale = UiSettings.UiScale;
            bool regeometry = !Mathf.Approximately(uiScale, _geomUi) || !Mathf.Approximately(pxPerUnit, _geomScale);
            if (regeometry)
            {
                _geomUi = uiScale;
                _geomScale = pxPerUnit;
                TickGeometry(uiScale, pxPerUnit, out _tick, out _arc);
            }

            int state = weapon.IsReloading ? 1 : weapon.MagazineSize > 0 && weapon.AmmoInMagazine <= 0 ? 2 : 0;
            bool saturated = _radius > SaturationRadius;
            float radius = Mathf.Clamp(_radius, MinRadius, SaturationRadius) + (state == 2 ? EmptySplit : 0f);
            radius = Mathf.Round(radius * 2f) * 0.5f;   // half-pixel steps: no rebuild for sub-pixel drift

            if (saturated != _saturated || _state < 0 || regeometry)
            {
                _saturated = saturated;
                Sprite sprite = saturated && _arcSprite != null ? _arcSprite : _barSprite;
                for (int i = 0; i < _ticks.Length; i++)
                {
                    if (sprite != null)
                    {
                        UiArt.SetGlyph(_ticks[i], sprite);
                        UiArt.ApplyGlyph(_ticks[i], InkEdge);
                    }

                    ((RectTransform)_ticks[i].transform).sizeDelta = saturated ? _arc : _tick;
                }

                _shownRadius = -1f; // re-place
            }

            if (!Mathf.Approximately(radius, _shownRadius))
            {
                _shownRadius = radius;
                float centre = radius + _tick.y * 0.5f;
                for (int i = 0; i < _ticks.Length; i++)
                {
                    float angle = i * Mathf.PI * 0.5f;   // N, E, S, W
                    ((RectTransform)_ticks[i].transform).anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * centre;
                }

                float ring = (radius + _tick.y + ReloadGap) * 2f;
                ((RectTransform)_reload.transform).sizeDelta = new Vector2(ring, ring);
            }

            if (!Mathf.Approximately(pellet, _shownPellet))
            {
                _shownPellet = pellet;
                _pellet.enabled = pellet > MinRadius;
                float d = Mathf.Round(pellet * 2f);
                ((RectTransform)_pellet.transform).sizeDelta = new Vector2(d, d);
            }

            if (state != _state || specialActive != _gold)
            {
                _state = state;
                _gold = specialActive;
                Color tint = UiTokens.Get(specialActive ? UiColor.StateGold : UiColor.TextPrimary);
                tint.a = state == 1 ? ReloadTickAlpha : 1f;
                for (int i = 0; i < _ticks.Length; i++)
                {
                    _ticks[i].color = tint;
                }

                _reload.enabled = state == 1;
            }

            if (state == 1 && Mathf.Abs(weapon.ReloadProgress01 - _shownReload) >= 1f / 256f)
            {
                _shownReload = weapon.ReloadProgress01;
                _reload.fillAmount = _shownReload; // Data: the real progress, never eased
            }
        }

        /// <summary>The spread half-angle at the aim distance, in canvas px (0 when anything is missing).</summary>
        private static float Project(Camera camera, Vector3 aimWorld, Vector3 originWorld, Vector2 canvasSize, float degrees)
        {
            if (camera == null || degrees <= 0f)
            {
                return 0f;
            }

            float distance = Vector3.Distance(originWorld, aimWorld);
            float worldRadius = Mathf.Tan(degrees * Mathf.Deg2Rad) * distance;
            Vector3 a = camera.WorldToViewportPoint(aimWorld);
            Vector3 b = camera.WorldToViewportPoint(aimWorld + camera.transform.right * worldRadius);
            if (a.z < 0f || b.z < 0f)
            {
                return 0f;
            }

            return Mathf.Abs(b.x - a.x) * canvasSize.x;
        }

        /// <summary>Reduced motion: three steps between the resting cone and saturation.</summary>
        private static float Quantise(float radius)
        {
            float mid = (MinRadius + SaturationRadius) * 0.5f;
            if (radius < (MinRadius + mid) * 0.5f)
            {
                return MinRadius;
            }

            return radius < (mid + SaturationRadius) * 0.5f ? mid : SaturationRadius;
        }
    }
}
