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
    ///
    /// 12b · D1 (AD 8a carried MUST, measured with the pinned linear composite in
    /// UiTranslucentTokensTests): the left third keeps SurfaceBase 0.55 (AD §14 MUST-5: fixed; S-05
    /// clamped the plate for it, TextSecondary ≥ 4.6), and the text on it is TextSecondary or
    /// brighter. The header block over the sky is <see cref="HeaderScrimAlpha"/> and spans from x 0
    /// (AD 6ab MUST-2), and the text-safe scrim widens to the nav column where an ultrawide page puts
    /// it past the third (<see cref="CoverColumn"/>).
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

        /// <summary>
        /// 12b · D1 (AD 8a carried MUST, a T10 pair under its floor is fixed by raising α): the header block over the
        /// sky. In linear light (the GPU's blend) 0.55 left the subtitle at 2.0:1 and the Display title at 3.6:1 over
        /// the pale sky at 21:9 / Mac × 1.25 (the sRGB estimate had passed them); the subtitle needs ≥ 0.88 at the
        /// Mac crop, 0.90 keeps it ≥ 4.9:1 at every §12 matrix point with the lantern at its breath peak.
        /// </summary>
        public const float HeaderScrimAlpha = 0.90f;

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
        private RectTransform _thirdScrim;
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
            _thirdScrim = scrimRect;
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
        /// 12b · D1: where the page puts text past the left third (the nav column on an ultrawide page at UI 1.0 or
        /// 0.9: its right edge 865 / 1007 px against a third of 853 / 948), the text-safe scrim widens to
        /// <paramref name="right"/> px (root-local, from the left; the caller adds the parallax margin) and its fade
        /// starts there, so no word sits on the fade. Nothing changes where the third already holds the column. Call
        /// it before <see cref="CoverHeader"/>. The plate is clamped for T10 only to a third of its width
        /// (menu_plates.json text_safe; S-05), so beyond it the scrim alone does not carry TextSecondary.
        /// </summary>
        public void CoverColumn(float right)
        {
            Rect r = _root.rect;
            if (!_built || _thirdScrim == null || r.width <= 1f)
            {
                return;
            }

            float edge = Mathf.Clamp01(right / r.width);
            if (edge <= _thirdScrim.anchorMax.x)
            {
                return;
            }

            _thirdScrim.anchorMax = new Vector2(edge, _thirdScrim.anchorMax.y);
            if (_thirdFade != null)
            {
                _thirdFade.anchorMin = new Vector2(edge, _thirdFade.anchorMin.y);
                _thirdFade.anchorMax = new Vector2(edge + ScrimFade, _thirdFade.anchorMax.y);
            }
        }

        /// <summary>The text-safe scrim's right edge (root-local px from the left): a third, or the nav column (tests).</summary>
        public float TextSafeRight
        {
            get { return _thirdScrim != null && _root != null ? _thirdScrim.anchorMax.x * _root.rect.width : 0f; }
        }

        /// <summary>
        /// AD 6ab MUST-2: the header's text-safe block. SurfaceBase <see cref="HeaderScrimAlpha"/> from x 0 to
        /// <paramref name="right"/> px (at least the text-safe edge) and from the top down to <paramref name="bottom"/>
        /// px (root-local, measured from the top-left). It fades out over 8 % of the width to the right and 8 % of the
        /// height below, so the Display title and subtitle read at 21:9 and on a Mac, where the crop puts pale sky
        /// behind them. It never doubles the left-third scrim: that scrim and its fade start under the block, and
        /// below the block over the third the fade only closes the step from the block to the third's 0.55.
        /// 12b · D1: from x 0 (the AD's own wording) because at the Mac's width crop the subtitle's left half lies
        /// on plate texels past S-05's clamp (0.55 gave it 2.1:1 there).
        /// </summary>
        public void CoverHeader(float right, float bottom)
        {
            Rect r = _root.rect;
            if (!_built || bottom <= 0f || r.width <= 1f)
            {
                return;
            }

            float safe = TextSafeRight;
            right = Mathf.Max(right, safe);

            Image block = UiFactory.NewImage("HeaderScrim", _root, UiColor.SurfaceBase);
            TopLeft((RectTransform)block.transform, 0f, 0f, right, bottom);
            SetAlpha(block, HeaderScrimAlpha);

            Image fadeRight = UiFactory.NewImage("HeaderFadeRight", _root, UiColor.SurfaceBase, UiSprites.FadeLeft);
            TopLeft((RectTransform)fadeRight.transform, right, 0f, r.width * ScrimFade, bottom);
            SetAlpha(fadeRight, HeaderScrimAlpha);

            // Below the block over the text-safe column the third's own scrim continues, so this fade only adds
            // the difference: SurfaceBase at (H − T) / (1 − T) over T composites to H at its top edge.
            Image fadeDownSafe = UiFactory.NewImage("HeaderFadeDownSafe", _root, UiColor.SurfaceBase, UiSprites.FadeDown);
            TopLeft((RectTransform)fadeDownSafe.transform, 0f, bottom, safe, r.height * ScrimFade);
            SetAlpha(fadeDownSafe, (HeaderScrimAlpha - ScrimAlpha) / (1f - ScrimAlpha));

            if (right > safe)
            {
                Image fadeDown = UiFactory.NewImage("HeaderFadeDown", _root, UiColor.SurfaceBase, UiSprites.FadeDown);
                TopLeft((RectTransform)fadeDown.transform, safe, bottom, right - safe, r.height * ScrimFade);
                SetAlpha(fadeDown, HeaderScrimAlpha);
            }

            if (_thirdScrim != null)
            {
                _thirdScrim.offsetMax = new Vector2(_thirdScrim.offsetMax.x, -bottom);
            }

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
                // The plates cover-fit; the world scrim is a ramp across its own rect (cover-fit cropped it to a sliver of
                // its middle on a resize - a flat panel with a hard edge at 4:3 and on the Mac, bfinal2).
                if (image != _worldScrim)
                {
                    CoverFit(image);
                }
            }
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime, Time.unscaledTime, LookOverride ?? LookTarget());
            if (_worldFadeAt >= 0f)
            {
                StepWorldFade(Time.unscaledTime);
            }
        }

        /// <summary>WP-081: the bands that step aside for the live world (the text-safe scrim and its fade stay).</summary>
        public static readonly string[] WorldBands = { "Far", "Mid", "Lanterns", "Near", "Rain" };

        private readonly System.Collections.Generic.List<CanvasGroup> _worldFade = new System.Collections.Generic.List<CanvasGroup>();
        private float _worldFadeAt = -1f;
        private float _worldFadeSeconds;

        /// <summary>
        /// WP-081 (MenuLook): the text-safe scrim and its fade at <paramref name="alpha"/> (C-00 decision b: <see cref="ScrimAlpha"/>;
        /// a menu look over the live world may differ - C-00 05.10 options).
        /// </summary>
        public void SetTextSafeAlpha(float alpha)
        {
            foreach (RectTransform part in new[] { _thirdScrim, _thirdFade })
            {
                Graphic graphic = part != null ? part.GetComponent<Graphic>() : null;
                if (graphic != null)
                {
                    SetAlpha(graphic, alpha);
                }
            }
        }

        private RawImage _worldScrim;
        private float _worldEdge, _worldColumn, _worldEnd, _worldColumnAt;

        /// <summary>
        /// WP-081 (C-00 05.10, decision B): over the live world the spine's scrim is one wide, soft profile - <paramref name="edge"/>
        /// at the screen's edge, smoothstep down to <paramref name="column"/> at the column's edge (the third, or wider where
        /// <see cref="CoverColumn"/> took it), then smoothstep to 0 at <paramref name="end"/> of the width. Both halves meet with no
        /// slope step, so no vertical line reads where the scrim ends (the short fade of the third read as one over V6's daylight).
        /// Replaces the third's solid scrim and its fade; one 512×1 alpha ramp, built once.
        /// </summary>
        public void SetWorldScrim(float edge, float column, float end, float minFall = 0f)
        {
            if (!_built || _thirdScrim == null)
            {
                return;
            }

            _worldEdge = edge;
            _worldColumn = column;
            float columnAt = Mathf.Clamp(_thirdScrim.anchorMax.x, 0.05f, 0.9f);
            _worldEnd = Mathf.Clamp(Mathf.Max(end, columnAt + minFall), 0.1f, 1f);
            _worldColumnAt = Mathf.Clamp(columnAt, 0.05f, _worldEnd - 0.05f);
            _thirdScrim.gameObject.SetActive(false);
            if (_thirdFade != null)
            {
                _thirdFade.gameObject.SetActive(false);
            }

            if (_worldScrim == null)
            {
                var go = new GameObject("WorldScrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.transform.SetParent(_root, false);
                _worldScrim = go.GetComponent<RawImage>();
                _worldScrim.raycastTarget = false;
                _worldScrim.transform.SetSiblingIndex(_thirdScrim.GetSiblingIndex());
            }

            const int Width = 512;
            var ramp = new Texture2D(Width, 1, TextureFormat.RGBA32, false, true) { name = "WorldScrimRamp", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[Width];
            for (int i = 0; i < Width; i++)
            {
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * WorldScrimAlphaAt((i + 0.5f) / Width * _worldEnd)));
            }

            ramp.SetPixels32(pixels);
            ramp.Apply(false, true);
            if (_worldScrim.texture != null)
            {
                Destroy(_worldScrim.texture);
            }

            _worldScrim.texture = ramp;
            _worldScrim.uvRect = new Rect(0f, 0f, 1f, 1f);
            _worldScrim.color = UiTokens.Get(UiColor.SurfaceBase);
            RectTransform rect = UiFactory.Fill(_worldScrim.rectTransform);
            rect.anchorMax = new Vector2(_worldEnd, 1f);
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[UI] World scrim: edge {0:0.00} → column {1:0.00} at {2:0.000} W → 0 at {3:0.000} W (root {4:0}×{5:0}, third anchorMax {6:0.000}).",
                edge, column, _worldColumnAt, _worldEnd, _root.rect.width, _root.rect.height, _thirdScrim.anchorMax.x));
        }

        /// <summary>The world scrim's alpha at <paramref name="x"/> (fraction of the width); 0 before <see cref="SetWorldScrim"/>.</summary>
        public float WorldScrimAlphaAt(float x)
        {
            if (_worldEnd <= 0f)
            {
                return 0f;
            }

            if (x <= _worldColumnAt)
            {
                return Mathf.Lerp(_worldEdge, _worldColumn, Smooth(x / _worldColumnAt));
            }

            return _worldColumn * (1f - Smooth((x - _worldColumnAt) / (_worldEnd - _worldColumnAt)));
        }

        /// <summary>The world scrim's column edge (fraction of the width) - where the profile changes from 'over the words' to 'into the world'.</summary>
        public float WorldScrimColumn => _worldColumnAt;

        public bool HasWorldScrim => _worldScrim != null && _worldScrim.gameObject.activeSelf;

        private static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>True once the live world shows under the menu (MenuCinematic): the plates have stepped aside.</summary>
        public bool WorldShown { get; private set; }

        /// <summary>
        /// WP-081: the main menu's real-time world is up - the plate bands and the rain fade out over
        /// <paramref name="seconds"/> (0 = at once: behind the curtain, Reduced Motion) and then stop drawing. The
        /// text-safe scrim (α 0.55, C-00 05.10 decision b) and its fade stay over the world.
        /// </summary>
        public void ShowWorld(float seconds, float now)
        {
            if (!_built || WorldShown)
            {
                return;
            }

            WorldShown = true;
            foreach (string band in WorldBands)
            {
                Transform child = _root.Find(band);
                if (child == null || !child.gameObject.activeSelf)
                {
                    continue;
                }

                CanvasGroup group = child.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = child.gameObject.AddComponent<CanvasGroup>();
                }

                group.blocksRaycasts = false;
                group.interactable = false;
                _worldFade.Add(group);
            }

            _worldFadeSeconds = seconds;
            _worldFadeAt = now;
            StepWorldFade(now);
        }

        /// <summary>One step of the plates' fade toward the world (smoothstep); at the end the bands go inactive. Tests drive it.</summary>
        public void StepWorldFade(float now)
        {
            float u = _worldFadeSeconds > 0f ? Mathf.Clamp01((now - _worldFadeAt) / _worldFadeSeconds) : 1f;
            float alpha = 1f - u * u * (3f - 2f * u);
            for (int i = 0; i < _worldFade.Count; i++)
            {
                if (_worldFade[i] == null)
                {
                    continue;
                }

                _worldFade[i].alpha = alpha;
                if (u >= 1f)
                {
                    _worldFade[i].gameObject.SetActive(false);
                }
            }

            if (u >= 1f)
            {
                _worldFadeAt = -1f;
                _worldFade.Clear();
            }
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
                Shader shader = DD.Prototype.Rendering.PrototypeShaders.IsReady ? DD.Prototype.Rendering.PrototypeShaders.UiMaskTint : null;
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
