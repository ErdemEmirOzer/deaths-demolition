using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8c · the Special's world marker (direction §5 E14 / E21), in two forms on one root. The HUD projects,
    /// clamps and places it every frame; nothing here runs on its own.
    /// In view: the AP-010 beacon diamond over a halo disc, 56 px at 10 m shrinking to 40 px at 40 m (E14), so distance
    /// reads as size.
    /// At the edge (behind the camera or outside the view; E21 EdgeMarker):
    /// - a 44 px ink disc carries the 28 px Marker.Edge glyph, the same silhouette as S-07's beacon (G1 SDF);
    /// - a chevron on the disc's rim points the way;
    /// - a distance caption sits under it.
    /// Urgency lerps gold → ember on the pickup's own curve, and the tint never takes the beacon's tone (§9 #A).
    /// G-21: the blink is the snapshot's ground phase, one clock for the beacon and both forms. The marker is bound to the
    /// projection with no Follow (§5 contract 5).
    /// Writes happen only on a change: the form swaps on a flip, the caption on a new metre, the size on a new whole pixel.
    /// </summary>
    public sealed class UiWorldMarker
    {
        public const float NearSize = 56f;
        public const float FarSize = 40f;
        public const float NearMetres = 10f;
        public const float FarMetres = 40f;
        public const float EdgeDisc = 44f;
        public const float EdgeGlyph = 28f;
        private const float PointerSize = 18f;

        private readonly RectTransform _root;
        private readonly RectTransform _inView;
        private readonly Image _halo;
        private readonly Image _ring;
        private readonly Image _core;
        private readonly RectTransform _edge;
        private readonly Image _disc;
        private readonly Image _glyph;
        private readonly RectTransform _pointer;
        private readonly Image _pointerImage;
        private readonly TMP_Text _distance;
        private readonly RectTransform _distanceRect;
        private int _form = -1;          // 0 in view, 1 at the edge
        private int _metres = int.MinValue;
        private float _size = -1f;
        private float _pointerAngle = float.NaN;

        public RectTransform Root
        {
            get { return _root; }
        }

        /// <summary>True while the edge form is shown (test readout).</summary>
        public bool AtEdge
        {
            get { return _form == 1; }
        }

        /// <summary>The in-view diamond's size in px (test readout).</summary>
        public float InViewSize
        {
            get { return _size; }
        }

        public TMP_Text Distance
        {
            get { return _distance; }
        }

        public UiWorldMarker(RectTransform layer)
        {
            _root = UiFactory.NewRect("SpecialWeaponMarker", layer);
            _root.sizeDelta = new Vector2(NearSize, NearSize);

            // ---- in view: halo, AP-010 diamond, core (the procedural ring / disc when the art is absent)
            _inView = UiFactory.NewRect("InView", _root);
            _inView.sizeDelta = new Vector2(NearSize, NearSize);
            _halo = UiFactory.NewImage("Halo", _inView, UiColor.SurfaceInset, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_halo.transform);
            _halo.color = Color.white;
            bool art = UiTheme.Active != null && UiTheme.Active.HudMarker != null;
            _ring = UiFactory.NewImage("Ring", _inView, UiColor.StateGold, art ? UiArt.HudMarker() : UiSprites.Ring);
            UiFactory.Fill((RectTransform)_ring.transform);
            _ring.preserveAspect = true;
            _ring.color = Color.white;
            _core = UiFactory.NewImage("Core", _inView, UiColor.StateGold, UiSprites.Disc);
            ((RectTransform)_core.transform).anchorMin = new Vector2(0.5f, 0.5f);
            ((RectTransform)_core.transform).anchorMax = new Vector2(0.5f, 0.5f);
            _core.color = Color.white;

            // ---- at the edge: 44 disc + 28 Marker.Edge + the chevron on the rim
            _edge = UiFactory.NewRect("Edge", _root);
            _edge.sizeDelta = new Vector2(EdgeDisc, EdgeDisc);
            _disc = UiFactory.NewImage("Disc", _edge, UiColor.HudInk, UiSprites.Disc);
            UiFactory.Fill((RectTransform)_disc.transform);
            Sprite edgeGlyph = UiArt.Icon("Marker.Edge");
            _glyph = UiFactory.NewImage("Glyph", _edge, UiColor.StateGold, edgeGlyph != null ? edgeGlyph : UiSprites.Disc);
            _glyph.preserveAspect = true;
            ((RectTransform)_glyph.transform).sizeDelta = new Vector2(EdgeGlyph, EdgeGlyph);
            _glyph.color = Color.white;
            _pointer = UiFactory.NewRect("Pointer", _edge);
            _pointer.sizeDelta = Vector2.zero;
            _pointerImage = UiFactory.NewImage("Chevron", _pointer, UiColor.StateGold, UiSprites.Chevron);
            var chevron = (RectTransform)_pointerImage.transform;
            chevron.sizeDelta = new Vector2(PointerSize, PointerSize);
            chevron.anchoredPosition = new Vector2(EdgeDisc * 0.5f + PointerSize * 0.5f, 0f);   // on the rim, facing +x
            _pointerImage.color = Color.white;

            // ---- the distance caption, under whichever form shows
            _distance = UiFactory.Label("Distance", _root, string.Empty, UiType.Caption, UiColor.StateGold, TextAnchor.UpperCenter);
            _distance.fontStyle = FontStyles.Bold;
            _distance.color = Color.white;
            UiHudInk.Apply(_distance);   // 8-R: over the world with no box - the caption carries its own ink
            _distanceRect = (RectTransform)_distance.transform;
            _distanceRect.pivot = new Vector2(0.5f, 1f);
            _distanceRect.sizeDelta = new Vector2(160f, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Caption)));

            SetForm(0);
            _root.gameObject.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (_root.gameObject.activeSelf != visible)
            {
                _root.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// One frame. <paramref name="local"/>: canvas px (already clamped to the inset edge);
        /// <paramref name="atEdge"/>: behind or outside the view; <paramref name="pointerDegrees"/>: the way to the drop
        /// (0 = right, counter-clockwise); <paramref name="blink"/>: the G-21 ground intensity 0..1;
        /// <paramref name="urgency01"/>: 0 gold → 1 ember.
        /// </summary>
        public void Place(Vector2 local, bool atEdge, float pointerDegrees, float metres, float blink, float urgency01)
        {
            SetForm(atEdge ? 1 : 0);
            _root.anchoredPosition = local;

            Color tone = Color.Lerp(UiTokens.Get(UiColor.StateGold), UiTokens.Get(UiColor.StateDanger), Mathf.Clamp01(urgency01));
            if (atEdge)
            {
                if (float.IsNaN(_pointerAngle) || Mathf.Abs(Mathf.DeltaAngle(pointerDegrees, _pointerAngle)) > 0.5f)
                {
                    _pointerAngle = pointerDegrees;
                    _pointer.localRotation = Quaternion.Euler(0f, 0f, pointerDegrees);
                }

                Blink(_glyph, tone, blink, 1f);
                _pointerImage.canvasRenderer.SetColor(tone);   // the way never blinks out
            }
            else
            {
                // E14: 56 px at 10 m → 40 px at 40 m, whole pixels (no rebuild for sub-pixel distance drift).
                float t = Mathf.InverseLerp(NearMetres, FarMetres, metres);
                float size = Mathf.Round(Mathf.Lerp(NearSize, FarSize, t));
                if (size != _size)
                {
                    _size = size;
                    _inView.sizeDelta = new Vector2(size, size);
                    float core = Mathf.Round(size * 0.36f);
                    ((RectTransform)_core.transform).sizeDelta = new Vector2(core, core);
                    PlaceCaption(size);
                }

                Blink(_core, tone, blink, 1f);
                Blink(_ring, tone, blink, 0.85f);
                // The halo backs the diamond at the dark end of the cycle; it breathes a little with the beat.
                Color halo = UiTokens.Get(UiColor.SurfaceInset);
                halo.a = Mathf.Lerp(0.45f, 0.75f, blink);
                _halo.canvasRenderer.SetColor(halo);
            }

            float scale = Mathf.Lerp(0.86f, 1.14f, blink);
            _root.localScale = new Vector3(scale, scale, 1f);

            // The distance label answers "how far" - the only number the ground phase keeps (G-21).
            int m = Mathf.RoundToInt(metres);
            if (m != _metres)
            {
                _metres = m;
                _distance.SetText("{0} m", m);
            }

            _distance.canvasRenderer.SetColor(tone);
        }

        private void SetForm(int form)
        {
            if (form == _form)
            {
                return;
            }

            _form = form;
            _inView.gameObject.SetActive(form == 0);
            _edge.gameObject.SetActive(form == 1);
            PlaceCaption(form == 1 ? EdgeDisc : (_size > 0f ? _size : NearSize));
        }

        private void PlaceCaption(float formSize)
        {
            _distanceRect.anchoredPosition = new Vector2(0f, -(formSize * 0.5f + (int)UiSpace.XS));
        }

        private static void Blink(Image image, Color tone, float blink, float weight)
        {
            Color c = tone;
            c.a = Mathf.Lerp(1f - weight, 1f, blink);
            image.canvasRenderer.SetColor(c);
        }
    }
}
