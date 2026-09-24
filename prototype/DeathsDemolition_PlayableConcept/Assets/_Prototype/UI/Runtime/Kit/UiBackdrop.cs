using DD.Prototype.Core.InputAbstraction;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 6 · the main menu's Look B backdrop (direction E2, §6.1). S-05's engine
    /// captures (<c>MenuPlates/menu_plates.json</c>) are stacked far → mid → lantern glow → near
    /// and cover-fit to the screen. Each band drifts 4 / 8 / 12 px toward the pointer or the
    /// right stick, followed over 0.20 s. The lantern mask breathes: 2.4 s, Amber300, peak 0.35,
    /// never below 0.55 of it. The left third keeps the SurfaceBase 0.55 scrim of the
    /// text-safe rectangle (T10), softened into the plate over a short fade so there is no
    /// hard seam behind the pane. Rain (α ≤ 0.06) falls on the Fx Particles loop in front of
    /// the near band.
    ///
    /// Reduced Motion: the bands stay centred, the lanterns hold at their peak and the rain is
    /// frozen — nothing drifts or breathes.
    /// </summary>
    public sealed class UiBackdrop : MonoBehaviour
    {
        public const float FarParallax = 4f;
        public const float MidParallax = 8f;
        public const float NearParallax = 12f;
        public const float FollowSeconds = 0.20f;
        public const float BreathPeriod = 2.4f;
        public const float LanternPeak = 0.35f;
        public const float BreathFloor = 0.55f;
        public const float ScrimAlpha = 0.55f;
        public const float TextSafeFraction = 1f / 3f;

        /// <summary>Soft edge of the text-safe scrim into the plate, as a fraction of the screen width.</summary>
        public const float ScrimFade = 0.08f;

        public const float RainAlpha = 0.06f;
        public const int RainDrops = 48;

        public const string MaskShader = "DD/UI/MaskTint";

        private static Texture2D s_streak;

        /// <summary>Capture seam (<c>-ddUiLook x,y</c>): a fixed look target instead of the pointer / stick; null = live.</summary>
        public static Vector2? LookOverride;
        private static Material s_mask;

        private RectTransform _root;
        private RectTransform _far;
        private RectTransform _mid;
        private RectTransform _near;
        private RawImage _lantern;
        private RectTransform _thirdFade;
        private Vector2 _look;
        private bool _built;

        /// <summary>True when the plates loaded (a build without MenuPlates keeps the old veil backdrop).</summary>
        public bool HasPlates
        {
            get { return _far != null; }
        }

        /// <summary>Current parallax of the near band (tests: RM keeps it at zero).</summary>
        public Vector2 NearOffset
        {
            get { return _near != null ? _near.anchoredPosition : Vector2.zero; }
        }

        public float LanternAlpha
        {
            get { return _lantern != null ? _lantern.color.a : 0f; }
        }

        /// <summary>Builds the stack into <paramref name="parent"/> (first sibling = furthest back); null when the plates are missing.</summary>
        public static UiBackdrop Create(RectTransform parent)
        {
            Texture2D far = Resources.Load<Texture2D>("MenuPlates/plate_far");
            if (far == null)
            {
                return null;
            }

            RectTransform root = UiFactory.NewRect("PlateStack", parent);
            UiFactory.Fill(root);
            var backdrop = root.gameObject.AddComponent<UiBackdrop>();
            backdrop.Build(root, far);
            return backdrop;
        }

        private void Build(RectTransform root, Texture2D far)
        {
            _root = root;
            _far = Band("Far", far, FarParallax).rectTransform;
            RawImage mid = Band("Mid", Resources.Load<Texture2D>("MenuPlates/plate_mid"), MidParallax);
            _mid = mid != null ? mid.rectTransform : null;

            // The lantern glow: the baked mask (occluded by everything in front of it) tinted
            // Amber300 and laid over the mid band; the near band then draws over it.
            // The mask is one BC4 channel: UI-Default would draw it as a red rectangle, so it goes
            // through DD/UI/MaskTint (red = coverage of the graphic's colour) or not at all.
            _lantern = Band("Lanterns", Resources.Load<Texture2D>("MenuPlates/plate_lantern_mask"), MidParallax);
            Material mask = MaskMaterial();
            if (_lantern != null && mask == null)
            {
                _lantern.gameObject.SetActive(false);
                _lantern = null;
            }
            else if (_lantern != null)
            {
                _lantern.material = mask;
                Color glow = UiTokens.Get(UiColor.LanternGlow); // decorative Amber300 (menu_plates.json tint), never a state token
                glow.a = LanternPeak;
                _lantern.color = glow;
            }

            RawImage near = Band("Near", Resources.Load<Texture2D>("MenuPlates/plate_near"), NearParallax);
            _near = near != null ? near.rectTransform : null;

            // Rain: thin streaks falling through the whole frame, barely there (≤ 0.06).
            RectTransform rainRect = UiFactory.NewRect("Rain", root);
            UiFactory.Fill(rainRect);
            var rain = rainRect.gameObject.AddComponent<UiFxParticles>();
            Color drop = UiTokens.Get(UiColor.TextPrimary);
            drop.a = RainAlpha;
            rain.color = drop;
            rain.Configure(RainDrops, 28f, 900f, 0f, 1.6f, 1.6f, Streak());

            // Text-safe scrim (T10): the left third at 0.55, then a short fade into the plate.
            Image scrim = UiFactory.NewImage("TextSafeScrim", root, UiColor.SurfaceBase);
            RectTransform scrimRect = UiFactory.Fill((RectTransform)scrim.transform);
            scrimRect.anchorMax = new Vector2(TextSafeFraction, 1f);
            SetAlpha(scrim, ScrimAlpha);
            // FadeLeft = opaque at its left edge, i.e. against the scrim (FadeRight ran the
            // other way and drew a seam at the fade's far edge - s6d frames).
            Image fade = UiFactory.NewImage("TextSafeFade", root, UiColor.SurfaceBase, UiSprites.FadeLeft);
            _thirdFade = UiFactory.Fill((RectTransform)fade.transform);
            _thirdFade.anchorMin = new Vector2(TextSafeFraction, 0f);
            _thirdFade.anchorMax = new Vector2(TextSafeFraction + ScrimFade, 1f);
            SetAlpha(fade, ScrimAlpha);

            _built = true;
            Place(Vector2.zero);
        }

        /// <summary>
        /// AD 6ab MUST-2: the header's text-safe block. SurfaceBase 0.55 from the left third's
        /// edge to <paramref name="right"/> px and from the top down to <paramref name="bottom"/>
        /// px (root-local, measured from the top-left). It fades out over 8 % of the width to
        /// the right and 8 % of the height below, so the Display title and subtitle read at
        /// 21:9 and on a Mac, where the crop puts pale sky behind them. It joins the left-third
        /// scrim without doubling it: that scrim's own fade starts under the block.
        /// </summary>
        public void CoverHeader(float right, float bottom)
        {
            Rect r = _root.rect;
            float third = r.width * TextSafeFraction;
            if (!_built || right <= third || bottom <= 0f)
            {
                return;
            }

            Image block = UiFactory.NewImage("HeaderScrim", _root, UiColor.SurfaceBase);
            TopLeft((RectTransform)block.transform, third, 0f, right - third, bottom);
            SetAlpha(block, ScrimAlpha);

            Image fadeRight = UiFactory.NewImage("HeaderFadeRight", _root, UiColor.SurfaceBase, UiSprites.FadeLeft);
            TopLeft((RectTransform)fadeRight.transform, right, 0f, r.width * ScrimFade, bottom);
            SetAlpha(fadeRight, ScrimAlpha);

            Image fadeDown = UiFactory.NewImage("HeaderFadeDown", _root, UiColor.SurfaceBase, UiSprites.FadeDown);
            TopLeft((RectTransform)fadeDown.transform, third, bottom, right - third, r.height * ScrimFade);
            SetAlpha(fadeDown, ScrimAlpha);

            if (_thirdFade != null)
            {
                _thirdFade.offsetMax = new Vector2(_thirdFade.offsetMax.x, -bottom);
            }
        }

        /// <summary>The header block's right edge (root-local px), or 0 when there is none (tests).</summary>
        public float HeaderRight
        {
            get
            {
                Transform block = _root != null ? _root.Find("HeaderScrim") : null;
                return block != null ? ((RectTransform)block).anchoredPosition.x + ((RectTransform)block).sizeDelta.x : 0f;
            }
        }

        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>A cover-fit plate band, oversized by its parallax on every side so a drift never shows an edge.</summary>
        private RawImage Band(string name, Texture2D texture, float parallax)
        {
            if (texture == null)
            {
                return null;
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            UiFactory.Fill(rect, -parallax);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            CoverFit(image);
            return image;
        }

        private void CoverFit(RawImage image)
        {
            Rect r = _root.rect;
            float screenAspect = r.width > 1f && r.height > 1f ? r.width / r.height : 16f / 9f;
            float textureAspect = (float)image.texture.width / Mathf.Max(1, image.texture.height);
            if (screenAspect >= textureAspect)
            {
                float h = textureAspect / screenAspect;
                image.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
            else
            {
                float w = screenAspect / textureAspect;
                image.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_built)
            {
                return;
            }

            foreach (RawImage image in _root.GetComponentsInChildren<RawImage>())
            {
                CoverFit(image);
            }
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime, Time.unscaledTime, LookOverride ?? LookTarget());
        }

        /// <summary>One frame: follow <paramref name="look"/> (−1..1) and breathe. Tests drive it directly.</summary>
        public void Tick(float deltaSeconds, float now, Vector2 look)
        {
            if (!_built)
            {
                return;
            }

            if (UiMotion.ReducedMotion)
            {
                _look = Vector2.zero;
                Place(_look);
                SetLantern(1f);
                return;
            }

            // Exponential follow: 0.20 s closes ~63 % of the gap, independent of the frame rate.
            float dt = Mathf.Clamp(deltaSeconds, 0f, 0.1f);
            float k = 1f - Mathf.Exp(-dt / FollowSeconds);
            _look += (look - _look) * k;
            Place(_look);

            float breath = 0.5f + 0.5f * Mathf.Sin(now * (2f * Mathf.PI / BreathPeriod));
            SetLantern(BreathFloor + (1f - BreathFloor) * breath);
        }

        /// <summary>The shared DD/UI/MaskTint material (one-channel masks and tiles: red = coverage of the graphic colour).</summary>
        internal static Material MaskMaterial()
        {
            if (s_mask == null)
            {
                Shader shader = Shader.Find(MaskShader);
                if (shader != null && shader.isSupported)
                {
                    s_mask = new Material(shader) { name = "UiLanternMask", hideFlags = HideFlags.DontSave };
                }
            }

            return s_mask;
        }

        /// <summary>A 32² white streak (one soft column, fading upward) for the rain particles; built once.</summary>
        private static Texture2D Streak()
        {
            if (s_streak != null)
            {
                return s_streak;
            }

            const int size = 32;
            s_streak = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "UiRainStreak", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                byte fade = (byte)(255 * y / (size - 1));
                for (int x = 0; x < size; x++)
                {
                    byte a = x == size / 2 ? fade : x == size / 2 - 1 ? (byte)(fade / 3) : (byte)0;
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            s_streak.SetPixels32(pixels);
            s_streak.Apply(false, true);
            return s_streak;
        }

        /// <summary>−1..1 on both axes: the right stick when it is the aim source, else the pointer against the screen centre.</summary>
        private static Vector2 LookTarget()
        {
            IInputSource source = InputSourceRegistry.Current;
            if (InputSourceRegistry.HasRealSource && source.AimKind == AimSourceKind.Stick)
            {
                return Vector2.ClampMagnitude(source.AimStick, 1f);
            }

            Vector2 pointer = InputSourceRegistry.HasRealSource ? source.AimScreenPosition : (Vector2)Input.mousePosition;
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return Vector2.zero;
            }

            var centred = new Vector2(pointer.x / Screen.width * 2f - 1f, pointer.y / Screen.height * 2f - 1f);
            return new Vector2(Mathf.Clamp(centred.x, -1f, 1f), Mathf.Clamp(centred.y, -1f, 1f));
        }

        /// <summary>Bands move against the look (the far band least), as a camera turning toward the pointer would show them.</summary>
        private void Place(Vector2 look)
        {
            SetBand(_far, -look * FarParallax);
            SetBand(_mid, -look * MidParallax);
            SetBand(_lantern != null ? _lantern.rectTransform : null, -look * MidParallax);
            SetBand(_near, -look * NearParallax);
        }

        private static void SetBand(RectTransform band, Vector2 offset)
        {
            if (band != null)
            {
                band.anchoredPosition = offset;
            }
        }

        private void SetLantern(float amount)
        {
            if (_lantern == null)
            {
                return;
            }

            Color c = _lantern.color;
            c.a = LanternPeak * amount;
            _lantern.color = c;
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
