using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 8-R concept B · the ensō: health as an ink circle painted on the slate around the ronin.
    /// - A 300° arc at r 100, open at the top, squashed to the ground plane (y × 0.87, the camera's 60° pitch).
    /// - Outside the 8b arcs (rear hairline r 76, damage wedge r 48–64), so separation stays by radius and weight.
    /// - Layers, back to front: a contact shadow, a 1 px HudInk rim, a cream ghost track at 0.25, the lost-value ghost,
    ///   the fill (BarHealth; Coral400 while low).
    /// - Nine hairline notches every 10 % plus a stronger one at the 30 % low mark.
    /// - Low: the cracked heart sits in the gap, upright.
    /// Only the shape lives at the figure; the number lives on the self-line under it (numbers never sit on a fixation
    /// point, GD 8-R). It follows the player's screen point in whole pixels and writes only on a change.
    /// </summary>
    public sealed class HudEnso
    {
        public const float Radius = 100f;
        public const float SpanDegrees = 300f;
        public const float GroundSquash = 0.87f;
        private const float GapDegrees = (360f - SpanDegrees) * 0.5f;
        private const float ShadowAlpha = 0.35f;
        private const float TrackAlpha = 0.25f;
        private const float LowGlyphSize = 22f;

        private readonly RectTransform _root;
        private readonly Image _fill;
        private readonly Image _ghost;
        private readonly Image _lowGlyph;
        private float _value = -1f;
        private bool _low;
        private Vector2 _shownAt = new Vector2(float.NaN, float.NaN);

        public RectTransform Root
        {
            get { return _root; }
        }

        /// <summary>The fill's shown share of the arc, 0..1 (test readout).</summary>
        public float Value
        {
            get { return _value; }
        }

        public HudEnso(RectTransform layer)
        {
            _root = UiFactory.NewRect("Enso", layer);
            _root.sizeDelta = Vector2.zero;

            // The arc planes: rotated so the 300° opens at the top, squashed onto the ground.
            RectTransform arc = UiFactory.NewRect("Arc", _root);
            arc.sizeDelta = Vector2.zero;
            arc.localRotation = Quaternion.Euler(0f, 0f, 180f);
            arc.localScale = new Vector3(1f, GroundSquash, 1f);

            Image shadow = Layer("Shadow", arc, UiColor.HudInk, UiSprites.EnsoArc300, 202f);
            shadow.rectTransform.anchoredPosition = new Vector2(-2f, 3f);   // down-right on screen (the arc plane is turned 180°)
            Fade(shadow, ShadowAlpha);
            Layer("Rim", arc, UiColor.HudInk, UiSprites.EnsoRim300, 204f);
            Fade(Layer("Track", arc, UiColor.TextPrimary, UiSprites.EnsoArc300, 202f), TrackAlpha);
            _ghost = Filled(Layer("Ghost", arc, UiColor.TextSecondary, UiSprites.EnsoArc300, 202f));
            Fade(_ghost, UiBar.GhostAlpha);
            _fill = Filled(Layer("Fill", arc, UiColor.BarHealth, UiSprites.EnsoArc300, 202f));

            // Notches on the ground ellipse, radial, upright in screen space.
            for (int i = 1; i < 10; i++)
            {
                bool strong = i == Mathf.RoundToInt(UiTokens.LowHealthFraction * 10f);
                Image notch = UiFactory.NewImage("Notch" + i, _root, strong ? UiColor.TextSecondary : UiColor.HudInk, UiSprites.Solid);
                float degrees = GapDegrees + SpanDegrees * i / 10f;   // clockwise from 12 o'clock
                float radians = degrees * Mathf.Deg2Rad;
                var rect = notch.rectTransform;
                rect.sizeDelta = new Vector2(strong ? 2f : 1f, 10f);
                rect.anchoredPosition = new Vector2(Mathf.Sin(radians) * (Radius - 3f), Mathf.Cos(radians) * (Radius - 3f) * GroundSquash);
                rect.localRotation = Quaternion.Euler(0f, 0f, -degrees);
            }

            _lowGlyph = UiArt.IconImage("Low", _root, "Hud.LowHealth", UiColor.BarHealthLow, LowGlyphSize);
            if (UiArt.IsGlyph(_lowGlyph.sprite))
            {
                UiArt.ApplyGlyph(_lowGlyph, new Vector3(0.25f, 0f, 0f));
            }

            _lowGlyph.rectTransform.anchoredPosition = new Vector2(0f, (Radius + 4f) * GroundSquash);
            _lowGlyph.gameObject.SetActive(false);
        }

        private static Image Layer(string name, RectTransform parent, UiColor colour, Sprite sprite, float diameter)
        {
            Image image = UiFactory.NewImage(name, parent, colour, sprite);
            image.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            image.raycastTarget = false;
            return image;
        }

        private static Image Filled(Image image)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Bottom;   // the gap, once the plane is turned 180°
            image.fillClockwise = true;
            image.fillAmount = ArcFill(1f);
            return image;
        }

        private static Image Fade(Image image, float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
            return image;
        }

        /// <summary>A health share → the radial fill that starts at the middle of the gap (the first 30° draw nothing).</summary>
        public static float ArcFill(float value01)
        {
            return (GapDegrees + SpanDegrees * Mathf.Clamp01(value01)) / 360f;
        }

        /// <summary>The value (Data: Base Settle, the same as the bar it replaces), its lost-value ghost and the low state.</summary>
        public void Set(float value01, bool low)
        {
            float value = Mathf.Clamp01(value01);
            if (Mathf.Abs(value - _value) >= 1f / 512f)
            {
                float previous = _value < 0f ? value : _value;
                _value = value;
                if (value < previous)
                {
                    UiPresets.BarGhost(_ghost, Mathf.Max(ArcFill(previous), _ghost.fillAmount), ArcFill(value));
                }
                else
                {
                    UiMotion.KillTarget(_ghost, false);
                    _ghost.fillAmount = ArcFill(value);
                }

                UiMotion.Fill(_fill, ArcFill(value), UiDuration.Base, UiEase.Settle, UiMotionClass.Data);
            }

            if (low != _low)
            {
                _low = low;
                _fill.color = UiTokens.Get(low ? UiColor.BarHealthLow : UiColor.BarHealth);
                _lowGlyph.gameObject.SetActive(low);
                if (low)
                {
                    UiPresets.StampIn(_lowGlyph.rectTransform);
                }
            }
        }

        /// <summary>Follow the player's screen point (canvas px), whole pixels only.</summary>
        public void Place(Vector2 local)
        {
            local = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
            if (local != _shownAt)
            {
                _shownAt = local;
                _root.anchoredPosition = local;
            }
        }
    }
}
