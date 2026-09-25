using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 8c · E13 the banner v3 (round / phase / Special news) in the top third, once per event.
    /// The envelope is <see cref="Seconds"/> (2.4 s):
    /// - The words wipe in left → right inside a RectMask2D whose width runs 0 → full over 220 ms Settle (the Size
    ///   channel), so the text is readable by 220 ms.
    /// - A sumi brush (AP-012 v004 Ink.BrushUnderline, InkReveal) inks in behind them over 400 ms.
    /// - It holds to 2.12 s, then goes out over 280 ms Exit (α and y −8).
    /// Banner 48 on the Display face, with a 1.5 px InkStroke outside the letters.
    /// Reduced motion: every step is Decor, so it appears and disappears statically.
    /// A new banner restarts the envelope; a retitle (the Special's name arriving) swaps the words in place, without a
    /// second wipe. Per frame there is one time compare (<see cref="Tick"/>).
    /// </summary>
    public sealed class HudBanner
    {
        public const float Seconds = 2.4f;
        public const float Width = 900f;
        public const float ExitDrop = 8f;
        private const float BrushPadding = 56f;
        private const float BrushHeight = 84f;

        /// <summary>TMP outline / dilate for 1.5 px of ink outside a 48 px letter (Display atlas 64 pt, gradient 9).</summary>
        private const float StrokeWidth = 0.22f;

        private static Material s_strokeMaterial;

        private readonly RectTransform _root;
        private readonly RectTransform _clip;
        private readonly Image _brush;
        private readonly UiFx _brushFx;
        private readonly TMP_Text _text;
        private readonly CanvasGroup _group;
        private readonly Vector2 _home;
        private readonly float _height;
        private float _exitAt = -1f;
        private float _endsAt = -1f;
        private float _width = Width;   // the words' width this banner (≥ Width): a long line never clips at the mask

        public TMP_Text Text
        {
            get { return _text; }
        }

        /// <summary>The banner's root (WP-068 12b, AD R1-B SHOULD-4: the HUD sorts it above the world-bound stamps).</summary>
        public RectTransform Root
        {
            get { return _root; }
        }

        public RectTransform Clip
        {
            get { return _clip; }
        }

        public Image Brush
        {
            get { return _brush; }
        }

        public CanvasGroup Group
        {
            get { return _group; }
        }

        /// <summary>A banner lives from <see cref="Show"/> until its envelope ends.</summary>
        public bool Live(float now)
        {
            return _endsAt > now;
        }

        public HudBanner(RectTransform parent, float topOffset)
        {
            _height = UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Banner));
            _root = UiFactory.NewRect("BannerRoot", parent);
            _root.anchorMin = new Vector2(0.5f, 1f);
            _root.anchorMax = new Vector2(0.5f, 1f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.sizeDelta = new Vector2(Width, Mathf.Max(_height, BrushHeight));
            _home = new Vector2(0f, -topOffset);
            _root.anchoredPosition = _home;
            _group = UiFactory.Group(_root.gameObject);
            _group.alpha = 0f;

            // The brush first (under the words). AD 8c MUST-1: one stroke behind the whole line - a loaded head 56 px before
            // the first letter, a HudInk body under every word, the dry tail only past the last one (UiSprites.BrushRibbon,
            // 9-sliced so the body stretches).
            _brush = UiFactory.NewImage("Brush", _root, UiColor.HudInk, UiSprites.BrushRibbon);
            _brush.type = Image.Type.Sliced;
            _brush.preserveAspect = false;
            var brushRect = (RectTransform)_brush.transform;
            brushRect.anchorMin = new Vector2(0.5f, 0.5f);
            brushRect.anchorMax = new Vector2(0.5f, 0.5f);
            brushRect.pivot = new Vector2(0.5f, 0.5f);
            brushRect.sizeDelta = new Vector2(Width * 0.5f, BrushHeight);
            _brushFx = _brush.gameObject.AddComponent<UiFx>();
            // Wipe mode: the ribbon's own alpha is the shape, the underline's time map orders the ink left → right.
            _brushFx.Configure(UiFxMode.InkReveal, UiDuration.Emphasis, new Vector3(0f, 1f, 0f), UiInkChannel.Underline);

            // The wipe: a left-pivoted mask that grows; the words inside are pinned to its left edge at full width, so
            // they stay put while more of them is uncovered.
            _clip = UiFactory.NewRect("Clip", _root);
            _clip.anchorMin = new Vector2(0f, 0.5f);
            _clip.anchorMax = new Vector2(0f, 0.5f);
            _clip.pivot = new Vector2(0f, 0.5f);
            _clip.sizeDelta = new Vector2(Width, _height);
            _clip.anchoredPosition = Vector2.zero;
            UiFactory.ClipContent(_clip);

            // Named "Banner": the WP-022J tests find the banner text by name.
            _text = UiFactory.Label("Banner", _clip, string.Empty, UiType.Banner, UiColor.TextPrimary, TextAnchor.MiddleCenter);
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            var textRect = (RectTransform)_text.transform;
            textRect.anchorMin = new Vector2(0f, 0.5f);
            textRect.anchorMax = new Vector2(0f, 0.5f);
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.sizeDelta = new Vector2(Width, _height);
            textRect.anchoredPosition = Vector2.zero;
            Material stroke = StrokeMaterial(_text);
            if (stroke != null)
            {
                _text.fontSharedMaterial = stroke;
            }
        }

        /// <summary>A new banner: the envelope restarts from the wipe.</summary>
        public void Show(string message, float now)
        {
            UiMotion.KillTarget(_group, false);
            UiMotion.KillTarget(_root, false);
            UiMotion.KillTarget(_clip, false);
            _text.text = message;
            Fit();
            _group.alpha = 1f;
            _root.anchoredPosition = _home;
            _clip.sizeDelta = new Vector2(0f, _height);
            UiMotion.Resize(_clip, new Vector2(_width, _height), UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            if (_brush.enabled)
            {
                _brushFx.Play();
            }

            _endsAt = now + Seconds;
            _exitAt = _endsAt - UiMotionTokens.Seconds(UiDuration.Screen);
        }

        /// <summary>The words change in place (no second wipe); the brush follows their width.</summary>
        public void Retitle(string message)
        {
            _text.text = message;
            float before = _width;
            Fit();
            if (!Mathf.Approximately(before, _width))
            {
                // A longer name: the wipe (running or done) now opens to the new width.
                UiMotion.Resize(_clip, new Vector2(_width, _height), UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            }
        }

        /// <summary>One frame: starts the exit at 2.12 s. Returns the seconds the banner has left (0 when gone).</summary>
        public float Tick(float now)
        {
            if (_exitAt > 0f && now >= _exitAt)
            {
                _exitAt = -1f;
                UiMotion.Alpha(_group, 0f, UiDuration.Screen, UiEase.Exit, UiMotionClass.Decor);
                UiMotion.Move(_root, _home + new Vector2(0f, -ExitDrop), UiDuration.Screen, UiEase.Exit, UiMotionClass.Decor);
            }

            return Mathf.Max(0f, _endsAt - now);
        }

        /// <summary>The banner is as wide as its words (never under <see cref="Width"/>): root, words and the wipe's end all follow.</summary>
        private void Fit()
        {
            float words = Mathf.Ceil(_text.GetPreferredValues(_text.text).x);
            _width = Mathf.Max(Width, words + 48f);
            _root.sizeDelta = new Vector2(_width, _root.sizeDelta.y);
            _text.rectTransform.sizeDelta = new Vector2(_width, _height);
            // Head from 56 px before the first letter; the tail (UiSprites.BrushRibbonTail) starts at the last letter.
            var brushRect = (RectTransform)_brush.transform;
            brushRect.sizeDelta = new Vector2(words + BrushPadding + UiSprites.BrushRibbonTail, BrushHeight);
            brushRect.anchoredPosition = new Vector2((UiSprites.BrushRibbonTail - BrushPadding) * 0.5f, 0f);
        }

        /// <summary>The Display face with the house ink edge outside the letters (built once per process).</summary>
        private static Material StrokeMaterial(TMP_Text text)
        {
            if (s_strokeMaterial != null)
            {
                return s_strokeMaterial;
            }

            Material face = text.fontSharedMaterial;
            if (face == null || !face.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                return null;
            }

            Color ink = UiTokens.Get(UiColor.InkStroke);
            s_strokeMaterial = new Material(face) { name = "DD_Banner (InkStroke)", hideFlags = HideFlags.DontSave };
            s_strokeMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            // TMP centres the outline on the edge; the face dilates by the same amount so the ink sits outside it.
            s_strokeMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, StrokeWidth);
            s_strokeMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, StrokeWidth);
            s_strokeMaterial.SetColor(ShaderUtilities.ID_OutlineColor, ink);
            return s_strokeMaterial;
        }
    }
}
