using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Shared skeleton for every full-screen surface: canvas, event system, backdrop,
    /// page grid and initial focus.
    ///
    /// THE PAGE GRID
    /// -------------
    /// One rule, applied by all four screens: a centred content rect inset by
    /// <see cref="UiSpace.Page"/> from the sides and <see cref="UiSpace.Section"/> from
    /// the top and bottom, with columns placed on FRACTIONAL anchors rather than pixel
    /// widths. That is the whole responsive strategy - a narrower window makes every
    /// column narrower in proportion, and no layout ever needs a breakpoint, because
    /// none of these screens has a second arrangement to switch to.
    ///
    /// The canvas scaler matches on height, so the vertical rhythm - which is the axis
    /// the results screen must not overflow on - is identical at every aspect ratio.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UiScreen : MonoBehaviour
    {
        protected Canvas Canvas { get; private set; }

        /// <summary>
        /// Full-bleed rect. Backdrops and scrims go here.
        /// Named ScreenRect and not Screen so it cannot shadow <see cref="UnityEngine.Screen"/>
        /// inside a subclass - the HUD needs Screen.width for the off-screen marker.
        /// </summary>
        protected RectTransform ScreenRect { get; private set; }

        /// <summary>The page grid: everything the player reads lives inside this.</summary>
        protected RectTransform Content { get; private set; }

        private Selectable _initialFocus;

        protected virtual int SortingOrder
        {
            get { return UiTokens.SortingScreen; }
        }

        /// <summary>Menu screens clear the frame themselves; the HUD sits over gameplay.</summary>
        protected virtual bool WantsUiCamera
        {
            get { return true; }
        }

        protected virtual bool WantsBackdrop
        {
            get { return true; }
        }

        /// <summary>
        /// WP-068 R1-C: an artwork wallpaper for the backdrop (a Resources path to a Texture2D), or null for the plain
        /// base. The art replaces the sky veil; the ground veil, petals and foot rule stay over it. Its luminance ceiling
        /// is the art's contract (see UpgradeWallpaperTests), so every text pair measured on SurfaceBase still holds.
        /// </summary>
        protected virtual string BackdropArt
        {
            get { return null; }
        }

        /// <summary>The share of the art's width shown, anchored to its bottom-right corner (1 = the whole image, cover-fit).</summary>
        protected virtual float BackdropArtWidth
        {
            get { return 1f; }
        }

        protected virtual bool WantsPageGrid
        {
            get { return true; }
        }

        /// <summary>
        /// The navigation base this screen owns, or None for a layer screen (pause, the
        /// round-end shop). Declared here so a screen serialised into a scene file - whose
        /// Awake runs before the installer's sceneLoaded hook - names its base itself, and
        /// a hand-made test scene needs no installer at all. Idempotent with the installer.
        /// </summary>
        protected virtual UiContext BaseContext
        {
            get { return UiContext.None; }
        }

        private void Awake()
        {
            if (BaseContext != UiContext.None)
            {
                UiNavigation.EnterBase(BaseContext);
            }

            // A screen that owns the frame draws through its camera; the HUD, which sits
            // over gameplay someone else is rendering, stays an overlay.
            Camera uiCamera = WantsUiCamera ? UiFactory.EnsureUiCamera() : null;

            UiFactory.EnsureEventSystem();

            Canvas = UiFactory.CreateCanvas(
                GetType().Name + ".Canvas", SortingOrder, transform, uiCamera);
            ScreenRect = (RectTransform)Canvas.transform;

            if (WantsBackdrop)
            {
                BuildBackdrop();
            }

            if (WantsPageGrid)
            {
                // WP-022C (AD SHOULD-2, 21:9): the page is centred and capped at
                // UiTokens.ContentMaxWidth, so an ultrawide frame gains margin, not
                // stretched cards; the menu plate stays visible at both sides.
                RectTransform pageHost = UiFactory.NewRect("PageHost", ScreenRect);
                UiFactory.Fill(pageHost);
                pageHost.offsetMin = new Vector2((int)UiSpace.Page, (int)UiSpace.Section);
                pageHost.offsetMax = new Vector2(-(int)UiSpace.Page, -(int)UiSpace.Section);
                pageHost.gameObject.AddComponent<UiMaxWidth>().Configure(UiTokens.ContentMaxWidth);

                Content = UiFactory.NewRect("Page", pageHost);
                UiFactory.Fill(Content);
            }
            else
            {
                Content = ScreenRect;
            }

            Build();

            // WP-022B: entrance fade on the whole canvas (skipped by reduced motion); the
            // screen-entered event is what the audio owner keys transitions off.
            CanvasGroup fade = UiFactory.Group(Canvas.gameObject);
            Canvas.gameObject.AddComponent<UiScreenFade>().Configure(fade);
            UiEvents.Raise(UiEvent.ScreenEntered, GetType().Name);
        }

        /// <summary>Virtual for the same reason as Start: a derived private OnDestroy would hide it.</summary>
        protected virtual void OnDestroy()
        {
            // WP-068 slice 2: presets a screen starts pass its instance id as owner.
            UiMotion.KillOwner(GetInstanceID(), false);
            UiEvents.Raise(UiEvent.ScreenLeft, GetType().Name);
        }

        /// <summary>
        /// Virtual, and it must stay virtual: a derived screen that declared its own
        /// private Start would HIDE this one, and Unity would then dispatch the message
        /// to the derived method only - silently losing the initial focus, which is the
        /// kind of bug that only shows up for keyboard players.
        /// </summary>
        protected virtual void Start()
        {
            // Deferred to Start so the EventSystem created in Awake has run its own.
            if (_initialFocus != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(_initialFocus.gameObject);
            }
        }

        /// <summary>Builds the screen. Called once, from Awake, after the grid exists.</summary>
        protected abstract void Build();

        /// <summary>
        /// Marks the control that should hold focus when the screen opens. Without this
        /// a keyboard or gamepad player would have to press a direction key before any
        /// focus indicator appeared, which reads as "the UI is not responding".
        /// </summary>
        protected void SetInitialFocus(Selectable selectable)
        {
            _initialFocus = selectable;
        }

        // ================================================================== backdrop

        /// <summary>
        /// Base fill, two very low veils for depth, six drifting petals and a vermilion
        /// hairline at the foot.
        ///
        /// The controlled part of the "controlled Japanese detail" is the count and the
        /// opacity: nothing in the backdrop exceeds a tenth of full alpha, everything is
        /// behind every panel, and no text is ever set directly on a petal.
        /// </summary>
        private void BuildBackdrop()
        {
            RectTransform backdrop = UiFactory.NewRect("Backdrop", ScreenRect);
            UiFactory.Fill(backdrop);

            Image baseFill = UiFactory.NewImage("Base", backdrop, UiColor.SurfaceBase);
            UiFactory.Fill((RectTransform)baseFill.transform);

            Texture2D art = string.IsNullOrEmpty(BackdropArt) ? null : Resources.Load<Texture2D>(BackdropArt);
            if (art != null)
            {
                var artObject = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(UiBackdropArt));
                var artRect = (RectTransform)artObject.transform;
                artRect.SetParent(backdrop, false);
                UiFactory.Fill(artRect);
                RawImage image = artObject.GetComponent<RawImage>();
                image.texture = art;
                image.raycastTarget = false;
                artObject.GetComponent<UiBackdropArt>().Configure(BackdropArtWidth);
            }
            else
            {
                Image sky = UiFactory.NewImage("SkyVeil", backdrop, UiColor.AccentWisteria, UiSprites.FadeDown);
                RectTransform skyRect = UiFactory.Fill((RectTransform)sky.transform);
                skyRect.anchorMin = new Vector2(0f, 0.45f);
                skyRect.anchorMax = new Vector2(1f, 1f);
                skyRect.offsetMin = Vector2.zero;
                skyRect.offsetMax = Vector2.zero;
                SetAlpha(sky, 0.07f);
            }

            Image ground = UiFactory.NewImage("GroundVeil", backdrop, UiColor.SurfaceInset, UiSprites.FadeUp);
            RectTransform groundRect = UiFactory.Fill((RectTransform)ground.transform);
            groundRect.anchorMin = new Vector2(0f, 0f);
            groundRect.anchorMax = new Vector2(1f, 0.4f);
            groundRect.offsetMin = Vector2.zero;
            groundRect.offsetMax = Vector2.zero;
            SetAlpha(ground, 0.55f);

            BuildPetals(backdrop);

            // Cream hairline (WP-022): the foot rule is decoration, not a third vermilion.
            Image rule = UiFactory.NewImage("FootRule", backdrop, UiColor.TextSecondary);
            RectTransform ruleRect = (RectTransform)rule.transform;
            ruleRect.anchorMin = new Vector2(0f, 0f);
            ruleRect.anchorMax = new Vector2(1f, 0f);
            ruleRect.pivot = new Vector2(0.5f, 0f);
            ruleRect.sizeDelta = new Vector2(0f, UiTokens.StrokeControl);
            ruleRect.anchoredPosition = Vector2.zero;
            SetAlpha(rule, 0.35f);
        }

        /// <summary>PROTOTYPE-ONLY: six petals is the entire atmosphere budget of a menu.</summary>
        private static void BuildPetals(RectTransform parent)
        {
            const int petalCount = 6;
            const float span = 900f;
            const float top = 620f;

            for (int i = 0; i < petalCount; i++)
            {
                Image petal = UiFactory.NewImage("Petal", parent, UiColor.AccentSakura, UiSprites.Petal);
                var rect = (RectTransform)petal.transform;

                float size = Random.Range(40f, 88f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(Random.Range(-span, span), Random.Range(-top, top));
                rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                SetAlpha(petal, Random.Range(0.05f, 0.10f));

                petal.gameObject.AddComponent<UiDrift>().Configure(
                    rect,
                    new Vector2(Random.Range(-9f, -3f), Random.Range(-16f, -7f)),
                    Random.Range(-11f, 11f),
                    span,
                    top);
            }
        }

        protected static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }

        // ================================================================== chrome

        /// <summary>
        /// Screen header: eyebrow, title, and a rule. Returns the height it consumed, so
        /// the caller's <see cref="UiStack"/> stays the single source of vertical rhythm.
        /// </summary>
        protected float BuildHeader(
            string eyebrow,
            string title,
            string subtitle = null,
            UiType titleType = UiType.H1)
        {
            var stack = new UiStack(Content);

            if (!string.IsNullOrEmpty(eyebrow))
            {
                // TextSecondary, not TextAccent (WP-022, AD MUST-2): vermilion is spent on
                // the primary action and the heading tick only (REQ-105 budget).
                TMP_Text eyebrowText = UiFactory.Label(
                    "Eyebrow", Content, eyebrow, UiType.Label, UiColor.TextSecondary, TextAnchor.LowerLeft);
                stack.Add((RectTransform)eyebrowText.transform, UiFactory.LineHeight(UiType.Label));
                stack.Gap(UiSpace.XS);
            }

            TMP_Text titleText = UiFactory.Label(
                "Title", Content, title, titleType, UiColor.TextPrimary, TextAnchor.LowerLeft);
            stack.Add((RectTransform)titleText.transform, UiFactory.LineHeight(titleType));

            if (!string.IsNullOrEmpty(subtitle))
            {
                stack.Gap(UiSpace.S);
                TMP_Text subtitleText = UiFactory.Label(
                    "Subtitle", Content, subtitle, UiType.Body, UiColor.TextSecondary, TextAnchor.UpperLeft);
                stack.Add((RectTransform)subtitleText.transform, UiFactory.LineHeight(UiType.Body));
            }

            stack.Gap(UiSpace.L);

            Image rule = UiFactory.Divider("HeaderRule", Content);
            stack.Add((RectTransform)rule.transform, UiTokens.StrokeHairline);

            // AP-012 ornament (WP-022B): the lozenge divider sits on the hairline at the
            // left edge, the one decorative mark a screen header carries.
            Sprite ornament = UiArt.Divider();
            if (ornament != null)
            {
                Image mark = UiFactory.NewImage("HeaderOrnament", Content, UiColor.TextSecondary, ornament);
                mark.preserveAspect = true;
                var markRect = (RectTransform)mark.transform;
                markRect.anchorMin = new Vector2(0f, 1f);
                markRect.anchorMax = new Vector2(0f, 1f);
                markRect.pivot = new Vector2(0f, 0.5f);
                markRect.sizeDelta = new Vector2(160f, 24f);
                markRect.anchoredPosition = new Vector2(0f, -stack.Cursor + UiTokens.StrokeHairline * 0.5f);
            }

            return stack.Cursor;
        }

        /// <summary>
        /// A column of the page grid, positioned on fractional anchors between
        /// <paramref name="from"/> and <paramref name="to"/> of the content width.
        /// </summary>
        protected RectTransform Column(string name, float from, float to, float top, float bottom = 0f)
        {
            RectTransform column = UiFactory.NewRect(name, Content);
            column.anchorMin = new Vector2(from, 0f);
            column.anchorMax = new Vector2(to, 1f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.offsetMin = new Vector2(0f, bottom);
            column.offsetMax = new Vector2(0f, -top);
            return column;
        }
    }

    /// <summary>
    /// The backdrop art's cover-fit: the image fills the screen at any aspect without stretching, anchored to its
    /// bottom-right corner (the ground and the art's subject keep their place), showing <c>width</c> of the image's
    /// width where the screen's aspect allows. Re-fits on a resize; no per-frame work.
    /// </summary>
    public sealed class UiBackdropArt : UIBehaviour
    {
        private float _width = 1f;

        public void Configure(float width)
        {
            _width = Mathf.Clamp(width, 0.1f, 1f);
            Fit();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            Fit();
        }

        private void Fit()
        {
            var image = GetComponent<RawImage>();
            var rect = (RectTransform)transform;
            if (image == null || image.texture == null || rect.rect.height <= 0f)
            {
                return;
            }

            float texAspect = image.texture.width / (float)image.texture.height;
            float screenAspect = rect.rect.width / rect.rect.height;
            float w = _width;
            float h = w * texAspect / screenAspect;
            if (h > 1f)
            {
                h = 1f;
                w = screenAspect / texAspect;
            }

            image.uvRect = new Rect(1f - w, 0f, w, h);
        }
    }
}
