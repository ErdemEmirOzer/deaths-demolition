using System;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10) · the logo's textures (Resources, warmed ahead) and the DD/UI/Fx LogoInk time map global.
    /// Logo class (≤ 2048, UiArtThemeTests): the wordmark, its time map, its halo. UI 1024 class: the seal and its fx.
    /// <see cref="Preload"/> starts asynchronous loads at the curtain's Build (behind the boot seal), so the first loading
    /// screen never loads them on its first frame (C-00 08.10: nothing on first use); a missing texture degrades (the time
    /// map to a 1x1 Alpha8 0 - every texel inked at once, S3-03), never throws.
    /// </summary>
    public static class UiLogoAssets
    {
        public const string WordmarkResource = "UI/Logo/ap035_logo_wordmark_2048x768";
        public const string TimeMapResource = "UI/Logo/ap035_logo_wordmark_timemap_2048x768";
        public const string HaloResource = "UI/Logo/ap035_logo_wordmark_halo_1024x384";
        public const string SealResource = "UI/LogoMark/ap035_logo_seal_768";
        public const string GlowResource = "UI/LogoMark/ap035_logo_seal_glow";
        public const string BlotResource = "UI/LogoMark/ap035_logo_seal_blot";
        public const string RingResource = "UI/LogoMark/ap035_logo_impact_ring";
        public const string BackingResource = "UI/LogoMark/ap035_logo_seal_backing_256";

        private static readonly string[] SpriteResources = { WordmarkResource, HaloResource, SealResource, GlowResource, BlotResource, RingResource, BackingResource };
        private static readonly int LogoMapId = Shader.PropertyToID("_DD_LogoMap");

        private static ResourceRequest[] s_spriteLoads;
        private static ResourceRequest s_mapLoad;
        private static Texture2D s_map;
        private static Texture2D s_fallback;
        private static readonly Sprite[] s_sprites = new Sprite[7];

        /// <summary>Start the loads (idempotent). The curtain calls it at Build.</summary>
        public static void Preload()
        {
            if (s_spriteLoads != null)
            {
                return;
            }

            s_spriteLoads = new ResourceRequest[SpriteResources.Length];
            for (int i = 0; i < SpriteResources.Length; i++)
            {
                s_spriteLoads[i] = Resources.LoadAsync<Sprite>(SpriteResources[i]);
            }

            s_mapLoad = Resources.LoadAsync<Texture2D>(TimeMapResource);
        }

        /// <summary>True once every load has finished (tests, the warm draw).</summary>
        public static bool Ready
        {
            get
            {
                if (s_spriteLoads == null || !s_mapLoad.isDone)
                {
                    return false;
                }

                for (int i = 0; i < s_spriteLoads.Length; i++)
                {
                    if (!s_spriteLoads[i].isDone)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public static Sprite Wordmark => SpriteAt(0);
        public static Sprite Halo => SpriteAt(1);
        public static Sprite Seal => SpriteAt(2);
        public static Sprite Glow => SpriteAt(3);
        public static Sprite Blot => SpriteAt(4);
        public static Sprite Ring => SpriteAt(5);
        public static Sprite Backing => SpriteAt(6);

        /// <summary>The time map (null when missing).</summary>
        public static Texture2D TimeMap
        {
            get
            {
                if (s_map == null)
                {
                    Preload();
                    s_map = s_mapLoad.asset as Texture2D;   // a load still in flight completes here (once)
                }

                return s_map;
            }
        }

        private static Sprite SpriteAt(int i)
        {
            if (s_sprites[i] == null)
            {
                Preload();
                s_sprites[i] = s_spriteLoads[i].asset as Sprite;
            }

            return s_sprites[i];
        }

        /// <summary>Bind the time map global (or the all-inked fallback). Cheap; called on every play.</summary>
        public static void InstallGlobal()
        {
            Texture2D map = TimeMap;
            if (map == null && s_fallback == null)
            {
                s_fallback = new Texture2D(1, 1, TextureFormat.Alpha8, false) { name = "DD_LogoMap_fallback", hideFlags = HideFlags.DontSave };
                s_fallback.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                s_fallback.Apply(false, true);
            }

            Shader.SetGlobalTexture(LogoMapId, map != null ? map : (Texture)s_fallback);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_spriteLoads = null;
            s_mapLoad = null;
            s_map = null;
            Array.Clear(s_sprites, 0, s_sprites.Length);
        }
    }

    /// <summary>
    /// AP-035 R3 (S-11 O2, C-00 08.10, K1-K12) · the AP-035 logo, natively in uGUI (no video): the wordmark inks in through
    /// its A8 time map (DD/UI/Fx LogoInk, GPU-timed), the seal drops and squashes at the spec's per-frame samples, the impact
    /// ring, ink blot and crack glow, the camera shake, then the loop (ring breath, crack glow, ash, sparks) and the outro.
    /// Laid out in the spec's own pixels: the composition box is the canonical frame's [480..1440] x [78..998] (960 x 920),
    /// scaled as a whole (<see cref="Create"/> width / 960) - the seal 570² at [675, 78], the wordmark 960 x 360 at [480, 638].
    /// Reads no input (S-09 08.10, binding): a skip is <see cref="Skip"/>, called by whoever owns the input.
    /// Time: <see cref="UiFxGlobals.Now"/> only (unscaled, the tests' clock) through <see cref="UiLogoTimeline"/> - absolute,
    /// so a long frame jumps the pose. Per frame: transform / alpha writes only, no allocation (GC 0 B per frame).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiLogoAnimator : MonoBehaviour
    {
        /// <summary>The composition box in spec px (canonical frame [480..1440] x [78..998]).</summary>
        public const float CompositionWidth = 960f;
        public const float CompositionHeight = 920f;
        public const float CompositionLeft = 480f;
        public const float CompositionTop = 78f;

        /// <summary>The wordmark rect in spec px (canonical frame), the seal layer's centre x.</summary>
        public static readonly Rect WordmarkRect = new Rect(480f, 638f, 960f, 360f);
        public const float SealCentreX = 960f;

        /// <summary>Bake placement (logo-intake.json placement, spec px; centre offsets y up): the sprites around the seal layer's centre.</summary>
        public const float SealSpriteSize = 481.494f;
        public static readonly Vector2 GlowSize = new Vector2(574.738f, 574.738f);
        public static readonly Vector2 GlowOffset = new Vector2(-1.349f, 0f);
        public static readonly Vector2 BlotSize = new Vector2(574.438f, 574.438f);
        public static readonly Vector2 BlotOffset = new Vector2(-44.349f, 5.993f);
        /// <summary>The ring sprite is padded 1 texel each side (492 for the 490 art): drawn 492/490 wider than the spec's art box.</summary>
        public const float RingSpriteWidthPerArt = 492f / 490f;

        /// <summary>Readability over the key arts (LoadingKeyArtTests): the halo under the wordmark and the paper behind the seal, Sumi900.</summary>
        public const float HaloAlpha = 0.85f;
        public const float BackingAlpha = 0.85f;

        /// <summary>Loop particles (motion-spec ash: 10, 65 px up per loop; sparks: 5).</summary>
        public const int AshCount = 10;
        public const int SparkCount = 5;
        public const float AshDrift = 65f;

        private static readonly Color Ember = new Color(0.93f, 0.48f, 0.24f, 1f);
        private static readonly Color AshTint = new Color(0.86f, 0.83f, 0.78f, 0.36f);

        private readonly UiLogoTimeline _timeline = new UiLogoTimeline();
        private RectTransform _root;
        private CanvasGroup _group;
        private RectTransform _shake;
        private Image _halo;
        private Image _wordmark;
        private UiFx _haloFx;
        private UiFx _wordFx;
        private RectTransform _sealLayer;
        private Image _backing;
        private Image _blot;
        private Image _seal;
        private Image _glow;
        private Image _ring;
        private UiFxParticles _ash;
        private UiFxParticles _sparks;
        private bool _assetsBound;
        private Action _onDone;
        private bool _cueRaised;
        private bool _eraseSet;
        private float _lastAlpha = -1f;

        /// <summary>Raised once when a menu intro's logo lands (the impact): the caller rings the BootLogo bell (K12).</summary>
        public event Action Impact;

        /// <summary>
        /// Build the logo under <paramref name="parent"/>, <paramref name="widthPx"/> wide (the composition's 960 spec px).
        /// The root is anchored at the parent's centre, pivot bottom-centre; the caller places it. Hidden until played.
        /// </summary>
        public static UiLogoAnimator Create(RectTransform parent, float widthPx)
        {
            UiLogoAssets.Preload();
            RectTransform root = UiFactory.NewRect("AP035Logo", parent);
            root.pivot = new Vector2(0.5f, 0f);
            root.sizeDelta = new Vector2(CompositionWidth, CompositionHeight);
            float scale = widthPx / CompositionWidth;
            root.localScale = new Vector3(scale, scale, 1f);
            var animator = root.gameObject.AddComponent<UiLogoAnimator>();
            animator.Build(root);
            root.gameObject.SetActive(false);
            return animator;
        }

        public UiLogoPhase Phase
        {
            get
            {
                _timeline.Tick(UiFxGlobals.Now);
                return _timeline.Phase;
            }
        }

        /// <summary>The timeline (tests, captures).</summary>
        public UiLogoTimeline Timeline => _timeline;

        public RectTransform Root => _root;

        /// <summary>True while anything of the logo is on screen.</summary>
        public bool Visible => _root != null && _root.gameObject.activeInHierarchy && _timeline.Visible(UiFxGlobals.Now);

        /// <summary>A menu intro (or its fade) is running.</summary>
        public bool IsPlaying => _timeline.CurrentMode == UiLogoTimeline.Mode.Menu && _timeline.Visible(UiFxGlobals.Now);

        /// <summary>K7: the menu intro, start to gone (≤ 2.0 s).</summary>
        public float MenuIntroDuration => UiLogoTimeline.MenuIntroDuration;

        /// <summary>The whole logo's opacity this frame (tests: the menu title rule).</summary>
        public float Alpha => _group != null && _root.gameObject.activeInHierarchy ? _group.alpha : 0f;

        /// <summary>K3: the load may activate now (true when the logo is not in its intro).</summary>
        public bool CanLeave => _timeline.CanLeave(UiFxGlobals.Now);

        /// <summary>The seal's centre, the wordmark rect (tests / captures: where the logo is on the canvas).</summary>
        public RectTransform WordmarkRectTransform => _wordmark != null ? _wordmark.rectTransform : null;
        public RectTransform SealRectTransform => _seal != null ? _seal.rectTransform : null;

        // ================================================================ build

        private void Build(RectTransform root)
        {
            _root = root;
            _group = UiFactory.Group(root.gameObject);
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _shake = UiFactory.NewRect("Composition", root);
            UiFactory.Fill(_shake);

            _halo = Art("Halo", _shake, null);
            PlaceTopLeft(_halo.rectTransform, WordmarkRect.x - CompositionLeft, WordmarkRect.y - CompositionTop, WordmarkRect.width, WordmarkRect.height);
            _halo.color = WithAlpha(UiTokens.Get(UiColor.SurfaceInset), HaloAlpha);
            _haloFx = _halo.gameObject.AddComponent<UiFx>();

            _ring = Art("ImpactRing", _shake, null);
            PlaceTopLeft(_ring.rectTransform, SealCentreX - CompositionLeft, UiLogoTimeline.SealRestTop + UiLogoTimeline.SealLayerSize * 0.5f - CompositionTop, 0f, 0f);
            _ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            _sealLayer = UiFactory.NewRect("SealLayer", _shake);
            _sealLayer.anchorMin = _sealLayer.anchorMax = new Vector2(0f, 1f);
            _sealLayer.pivot = new Vector2(0.5f, 0f);
            _sealLayer.sizeDelta = new Vector2(UiLogoTimeline.SealLayerSize, UiLogoTimeline.SealLayerSize);
            _backing = Art("Backing", _sealLayer, null);
            Centre(_backing.rectTransform, Vector2.zero, new Vector2(SealSpriteSize, SealSpriteSize));
            _backing.color = WithAlpha(UiTokens.Get(UiColor.SurfaceInset), BackingAlpha);
            _blot = Art("Blot", _sealLayer, null);
            Centre(_blot.rectTransform, BlotOffset, BlotSize);
            _seal = Art("Seal", _sealLayer, null);
            Centre(_seal.rectTransform, Vector2.zero, new Vector2(SealSpriteSize, SealSpriteSize));
            _glow = Art("Glow", _sealLayer, null);
            Centre(_glow.rectTransform, GlowOffset, GlowSize);

            _wordmark = Art("Wordmark", _shake, null);
            PlaceTopLeft(_wordmark.rectTransform, WordmarkRect.x - CompositionLeft, WordmarkRect.y - CompositionTop, WordmarkRect.width, WordmarkRect.height);
            _wordFx = _wordmark.gameObject.AddComponent<UiFx>();

            // Loop particles: emitters on a line through the seal's middle, drifting up (negative speed: the Particles mode's
            // loop direction is down) - ash cream, sparks ember.
            _ash = Particles("Ash", AshCount, 7f, -AshDrift / UiLogoTimeline.LoopSeconds, AshTint);
            _sparks = Particles("Sparks", SparkCount, 5f, -1.6f * AshDrift / UiLogoTimeline.LoopSeconds, Ember);
        }

        private Image Art(string name, Transform parent, Sprite sprite)
        {
            Image image = UiFactory.NewImage(name, parent, UiColor.TextPrimary, sprite);
            image.color = Color.white;   // the art's own colours (the halo / backing re-tint below)
            image.raycastTarget = false;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            return image;
        }

        private UiFxParticles Particles(string name, int count, float size, float speed, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UiFxParticles));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_shake, false);
            PlaceTopLeft(rect, SealCentreX - CompositionLeft - 260f, UiLogoTimeline.SealRestTop + 330f - CompositionTop, 520f, 8f);
            rect.pivot = new Vector2(0f, 1f);
            var p = go.GetComponent<UiFxParticles>();
            p.color = tint;
            p.raycastTarget = false;
            p.Configure(count, size, speed, 0f, UiLogoTimeline.LoopSeconds, UiLogoTimeline.LoopSeconds);
            return p;
        }

        private static void PlaceTopLeft(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static void Centre(RectTransform rect, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>Sprites in place (the loads started at Preload; a load still in flight completes here, once).</summary>
        private void BindAssets()
        {
            UiLogoAssets.InstallGlobal();
            if (_assetsBound)
            {
                return;
            }

            _assetsBound = true;
            Bind(_wordmark, UiLogoAssets.Wordmark);
            Bind(_halo, UiLogoAssets.Halo);
            Bind(_seal, UiLogoAssets.Seal);
            Bind(_glow, UiLogoAssets.Glow);
            Bind(_blot, UiLogoAssets.Blot);
            Bind(_ring, UiLogoAssets.Ring);
            Bind(_backing, UiLogoAssets.Backing);
        }

        private static void Bind(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;   // a missing piece is left out, never a white square
        }

        // ================================================================ play

        /// <summary>
        /// K2: the loading logo from now - intro at <paramref name="speed"/> (reduced motion: the canonical frame eased in
        /// over 0.5 s). The caller tells it when the load is ready (<see cref="NotifyReady"/>) and opens with
        /// <see cref="BeginOutro"/>.
        /// </summary>
        public void PlayLoading(float speed = UiLogoTimeline.IntroSpeed)
        {
            _onDone = null;
            Begin();
            _timeline.BeginLoading(UiFxGlobals.Now, UiMotion.ReducedMotion, speed);
            StartWordmark();
            Apply(UiFxGlobals.Now);
        }

        /// <summary>
        /// K7: the menu intro - the intro x 1.33 to the canonical frame, then a 0.3 s fade (≤ 2.0 s; once per session is the
        /// caller's rule). Reduced motion: nothing shows, <paramref name="onDone"/> at once. Reads no input: call
        /// <see cref="Skip"/> from the menu's own submit / cancel / pointer.
        /// </summary>
        public void PlayMenuIntro(Action onDone = null)
        {
            _onDone = onDone;
            if (UiMotion.ReducedMotion)
            {
                HideNow();
                Done();
                return;
            }

            Begin();
            _timeline.BeginMenu(UiFxGlobals.Now, false);
            StartWordmark();
            Apply(UiFxGlobals.Now);
        }

        private void Begin()
        {
            BindAssets();
            _cueRaised = false;
            _eraseSet = false;
            _lastAlpha = -1f;
            _root.gameObject.SetActive(true);
            enabled = true;
        }

        private void StartWordmark()
        {
            float seconds = UiLogoTimeline.RevealEndSeconds / Mathf.Max(0.1f, _timeline.Speed);
            _wordFx.Configure(UiFxMode.LogoInk, seconds, Vector3.zero);
            _haloFx.Configure(UiFxMode.LogoInk, seconds, Vector3.zero);
            _wordFx.Play();
            _haloFx.Play();
        }

        /// <summary>The load is ready (loaded, its minimum display shown): from here the logo holds it ≤ 0.5 s.</summary>
        public void NotifyReady()
        {
            _timeline.NotifyReady(UiFxGlobals.Now);
        }

        /// <summary>The curtain opens: the outro (reduced motion: a short fade). Never delays the reveal.</summary>
        public void BeginOutro()
        {
            _timeline.BeginOutro(UiFxGlobals.Now);
        }

        /// <summary>The end of the intro at once: the canonical pose fades out over 0.14 s, then onDone (once). Safe to call twice.</summary>
        public void Skip()
        {
            _timeline.Skip(UiFxGlobals.Now);
            Apply(UiFxGlobals.Now);
        }

        /// <summary>Gone at once (a new load, an instant open, the error modal).</summary>
        public void HideNow()
        {
            _timeline.Hide();
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }
        }

        private void Done()
        {
            Action done = _onDone;
            _onDone = null;
            done?.Invoke();
        }

        private void Update()
        {
            Apply(UiFxGlobals.Now);
        }

        /// <summary>Pose everything for <paramref name="now"/> (absolute clock; no state carried between frames but the timeline's).</summary>
        private void Apply(float now)
        {
            _timeline.Tick(now);
            UiLogoPhase phase = _timeline.Phase;
            if (phase == UiLogoPhase.Hidden)
            {
                _root.gameObject.SetActive(false);
                Done();
                return;
            }

            float alpha = _timeline.Alpha(now);
            if (Mathf.Abs(alpha - _lastAlpha) > 1e-4f)
            {
                _group.alpha = alpha;
                _lastAlpha = alpha;
            }

            float spec = _timeline.SpecTime(now);

            // the seal: drop, squash (bottom-centre pivot, spec y down -> anchored y up)
            bool sealOn = UiLogoTimeline.SealPose(spec, out float bottom, out float sx, out float sy, out float glow);
            _sealLayer.anchoredPosition = new Vector2(SealCentreX - CompositionLeft, -(bottom - CompositionTop));
            _sealLayer.localScale = new Vector3(sx, sy, 1f);
            SetAlpha(_backing, sealOn ? 1f : 0f);
            SetAlpha(_seal, sealOn ? 1f : 0f);

            // impact: ring, blot, crack glow, shake
            float ringAlpha = UiLogoTimeline.RingAlpha(spec, out Vector2 ringSize);
            float blotAlpha = UiLogoTimeline.BlotAlpha(spec, out float blotScale);
            Vector2 shake = UiLogoTimeline.Shake(spec);
            float loopGlow = 0f;
            float sparkAlpha = 0f;
            bool particles = false;
            if (phase == UiLogoPhase.Loop)
            {
                float envelope = UiLogoTimeline.LoopEnvelope(_timeline.LoopTime(now));
                ringAlpha = UiLogoTimeline.RingBreathPeakAlpha * envelope;
                ringSize = UiLogoTimeline.RingSizeEnd;
                loopGlow = UiLogoTimeline.LoopGlowPeakAlpha * envelope;
                sparkAlpha = envelope;
                particles = true;
            }
            else if (phase == UiLogoPhase.Outro)
            {
                OutroPose(now);
            }

            _ring.rectTransform.sizeDelta = new Vector2(ringSize.x * RingSpriteWidthPerArt, ringSize.y);
            SetAlpha(_ring, ringAlpha);
            _blot.rectTransform.localScale = new Vector3(blotScale, blotScale, 1f);
            SetAlpha(_blot, blotAlpha);
            SetAlpha(_glow, Mathf.Max(glow, loopGlow));
            _shake.anchoredPosition = new Vector2(shake.x, -shake.y);
            SetAlpha(_ash, particles ? 1f : 0f);
            SetAlpha(_sparks, particles ? sparkAlpha : 0f);

            if (!_cueRaised && _timeline.CurrentMode == UiLogoTimeline.Mode.Menu && spec >= UiLogoTimeline.ImpactSeconds)
            {
                _cueRaised = true;
                Impact?.Invoke();
            }
        }

        /// <summary>v001 outro: hold (frames 1..8), the wordmark wiped left -> right (8..14), the seal under ink (9..12), the rest fades (10..16).</summary>
        private void OutroPose(float now)
        {
            if (!_eraseSet)
            {
                _eraseSet = true;
                float eraseAt = _timeline.PhaseStart + UiLogoTimeline.OutroHoldSeconds;
                var erase = new Vector3(eraseAt, UiLogoTimeline.OutroEraseSeconds, 0f);
                _wordFx.SetParams(erase);
                _haloFx.SetParams(erase);
            }

            float t = _timeline.PhaseSeconds(now);
            float ink = UiLogoTimeline.Smooth01((t - UiLogoTimeline.OutroSealInkFrom) / UiLogoTimeline.OutroSealInkSeconds);
            SetAlpha(_seal, 1f - ink);
            SetAlpha(_backing, 1f);
        }

        private static void SetAlpha(Graphic g, float alpha)
        {
            CanvasRenderer r = g.canvasRenderer;
            if (Mathf.Abs(r.GetAlpha() - alpha) > 1e-4f)
            {
                r.SetAlpha(alpha);
            }
        }

        /// <summary>
        /// GPU warm (C-00 08.10): draw the whole logo once, invisibly (alpha 1/255), so its textures and the DD/UI/Fx batch
        /// are resident before the first loading screen. The curtain calls it behind the boot seal; ends itself next frame.
        /// </summary>
        public void WarmDraw()
        {
            if (_timeline.Phase != UiLogoPhase.Hidden)
            {
                return;
            }

            BindAssets();
            _root.gameObject.SetActive(true);
            _group.alpha = 1f / 255f;
            _lastAlpha = -1f;
            enabled = false;   // no Update: the root goes again in EndWarmDraw
        }

        /// <summary>Ends <see cref="WarmDraw"/> (no-op when a play took over).</summary>
        public void EndWarmDraw()
        {
            enabled = true;
            if (_timeline.Phase == UiLogoPhase.Hidden)
            {
                _root.gameObject.SetActive(false);
            }
        }
    }
}
