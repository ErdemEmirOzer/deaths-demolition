using System;
using System.Collections;
using System.Collections.Generic;
using DD.Prototype.Core.Boot;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 10 · the loading curtain (UI v3 direction §15). One overlay canvas on the
    /// flow runner (UiLayer.Curtain 400, survives scene loads) with two depths:
    ///   CURTAIN - two lacquer doors meet in the middle and part again (fast loads);
    ///   SCREEN  - the doors plus the loading group: the ensō progress ring (honest 0 → 0.9,
    ///             Data class, never eased), LOADING / ENTERING and the destination label.
    /// <see cref="PrototypeUiFlow"/> drives it around SceneManager.LoadSceneAsync; screens
    /// that open behind it start their intro on <see cref="Revealed"/> instead of Start().
    /// Slice 10a: the flow core with plain doors and ring. Slice 10b-1: the SCREEN group
    /// stands on the Look B plates (far; far + mid on the way into the run) under a
    /// Sumi900 scrim 0.55 → 0.80 (R1-D: on the loading key art, the plates its fallback), a tip strip bottom-left (U9, <see cref="UiLoadingTips"/>),
    /// the 10 s stall notice, the Reduced Motion percent line, the load error modal and the
    /// S-08 cues (CurtainClosed from the flow, CurtainOpened here). The InkReveal ring stroke,
    /// the seal device, the ink variant and the boot hand-off are 10b-2.
    /// </summary>
    public sealed class UiCurtain : MonoBehaviour
    {
        /// <summary>Ring diameter and its centre offset above the screen centre (direction §15.3).</summary>
        public const float RingSize = 240f;
        public const float RingLift = 40f;

        /// <summary>0.9 of the fill is the load; the last 36° close on activation.</summary>
        public const float LoadShare = 0.9f;

        /// <summary>The seal device inside the ring (V2, AP-012 v004) and its resting ink alpha (direction §15.3).</summary>
        public const float SealSize = 96f;
        public const float SealRestAlpha = 0.35f;

        /// <summary>The wet edge at the stroke's tip, as a share of the full circle (§15.3: "0,04 genişlik").</summary>
        public const float WetBand = 0.04f;

        /// <summary>
        /// AD 10b-2 MUST-1: the wet tip is the last WetBand of the drawn stroke, cream at 0.6 (≈ TextSecondary
        /// luminance) - never the next WetBand. Its front trembles BACK from the truth by up to WetTremble
        /// (≈ 3 px of arc, stepped at WetTrembleHz); nothing is drawn past the progress. Reduced motion: still.
        /// </summary>
        public const float WetAlpha = 0.6f;
        public const float WetTremble = 0.004f;
        public const float WetTrembleHz = 12f;

        /// <summary>CURTAIN depth: the ring alone, Ø 160, once a curtain-only load passes 1.2 s (§15.3).</summary>
        public const float CurtainRingSize = 160f;

        /// <summary>The dry ghost circle (the idle track). 12b (AD key art SHOULD-2): 0.12 → 0.18, so the track still reads as a
        /// circle where it crosses pale plaster on the key art; the dry stroke (1.0) and the wet tip (0.6) stay well above it.</summary>
        public const float GhostAlpha = 0.18f;

        /// <summary>Boot seal variant (E1, §15.3): the logo seal's size, the ink blot behind it, the wordmark's tracking.</summary>
        public const float BootSealSize = 240f;
        public const float BootBlotSize = 720f;
        public const float BootBlotSeconds = 0.5f;
        public const float BootStampAt = 0.55f;
        public const float BootWordmarkAt = 0.70f;
        public const float WordmarkTracking = 8f;   // +0.08 em (TMP characterSpacing is em/100)
        private const int BootOwner = 0x10B2;

        /// <summary>Ink variant (E22 / §15.1): the bleed from the corners (t 0.6 → 2.0 of the death beat), its recede, the corner blot size.</summary>
        public const float InkBleedSeconds = 1.4f;
        public const float InkRecedeSeconds = 0.6f;
        public const float InkBlotSize = 2600f;

        /// <summary>The doors' kumiko lattice (A6): one 256² tile per KumikoTilePixels on screen, cream at KumikoAlpha (≤ 0.08).</summary>
        public const float KumikoTilePixels = 256f;   // AD G1 SHOULD-2: 192 → 256, α 0.06 → 0.045 (slightly busy)
        public const float KumikoAlpha = 0.045f;

        private static UiCurtain s_instance;
        private static bool s_covering;

        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _leftDoor;
        private RectTransform _rightDoor;
        private CanvasGroup _group;
        private Image _ring;
        private Image _ringWet;
        private Image _seal;
        private RectTransform _ringGroup;
        private Image _scrim;
        private Image _scrimFoot;
        private bool _ringOnly;
        private Image _ringGhost;
        private RectTransform _bootLayer;
        private Image _blot;
        private UiFx _blotFx;
        private TMP_Text _kicker;
        private RectTransform _wordmarkClip;
        private TMP_Text _wordmark;
        private float _wordmarkWidth;
        private bool _boot;
        private bool _bootHandedOff;
        private float _bootStart;
        private UiTimeline _bootTimeline;
        private static bool s_bootSkip;
        private RectTransform _inkLayer;
        private UiFx[] _inkFx;
        private Image _inkField;
        private bool _ink;
        private bool _inkReceding;
        private TMP_Text _eyebrow;
        private TMP_Text _destination;
        private TMP_Text _subline;
        private float _shownFill = -1f;
        private bool _cursorWasVisible = true;
        private RawImage _plateFar;
        private RawImage _plateMid;

        /// <summary>
        /// WP-068 R1-D (N20): the loading screen's key art - the rainy castle courtyard, the ronin, the horde at the gate;
        /// on the way to the Results the ronin on the wall as the horde pours in (Higgsfield). Full and rich except under
        /// the words: darkened there so the ring, the labels and the tip strip keep their contrast (LoadingKeyArtTests).
        /// Over the plates, under the ring, labels and tip strip.
        /// </summary>
        private RawImage _keyArt;
        private UiTweenHandle _keyArtPush;
        private ResourceRequest _keyArtLoad;
        private string _keyArtLoadPath;
        private ResourceRequest _resultsKeyArtLoad;

        /// <summary>The slow push-in over a load (AE-class motion, Decor: Reduced Motion holds it still).</summary>
        public const float KeyArtPushScale = 1.05f;
        public const float KeyArtPushSeconds = 9f;
        private RectTransform _tipStrip;
        private TMP_Text _tip;
        private RectTransform _stall;
        private UiModal _error;
        private int _percent = -1;
        private string _eyebrowState = "LOADING"; // TMP's .text is not updated by SetText (the RM percent line)

        public const float TipMargin = 96f;
        public const float TipMaxWidth = UiTokens.MaxMeasure;
        public const float ScrimTop = 0.55f;
        public const float ScrimBottom = 0.80f;

        /// <summary>Raised when the doors are half open over the new scene (screens start their intro here).</summary>
        public static event Action Revealed;

        /// <summary>True from the moment the doors start closing until they are half open again.</summary>
        public static bool IsCovering
        {
            get { return s_covering; }
        }

        /// <summary>Yield until the curtain no longer covers the screen (immediately when it never did).</summary>
        public static IEnumerator WaitRevealed()
        {
            while (s_covering)
            {
                yield return null;
            }
        }

        public static UiCurtain Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var host = new GameObject("UiCurtain") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(host);
                    s_instance = host.AddComponent<UiCurtain>();
                    s_instance.Build();
                }

                return s_instance;
            }
        }

        /// <summary>The group (ring + labels) is visible: SCREEN depth.</summary>
        public bool GroupVisible
        {
            get { return _group != null && _group.gameObject.activeSelf; }
        }

        /// <summary>The fill the ring currently shows (0..1).</summary>
        public float RingFill
        {
            get { return _ring != null ? _ring.fillAmount : 0f; }
        }

        /// <summary>The loading state word: LOADING or ENTERING (also under Reduced Motion, where the label shows the percent).</summary>
        public string EyebrowText
        {
            get { return _eyebrow != null ? _eyebrowState : string.Empty; }
        }

        /// <summary>The destination label (tests).</summary>
        public string DestinationText
        {
            get { return _destination != null ? _destination.text : string.Empty; }
        }

        /// <summary>The tip on the strip (empty when none is shown).</summary>
        public string TipText
        {
            get { return _tipStrip != null && _tipStrip.gameObject.activeSelf ? _tip.text : string.Empty; }
        }

        public bool PlateVisible
        {
            get { return _plateFar != null && _plateFar.gameObject.activeSelf && _plateFar.texture != null; }
        }

        /// <summary>The destination's key art is on screen (tests, captures): in the hierarchy, so a hidden group's stale art does not count.</summary>
        public bool KeyArtVisible
        {
            get { return _keyArt != null && _keyArt.gameObject.activeInHierarchy && _keyArt.texture != null; }
        }

        /// <summary>The name of the art on screen (tests, captures); null when none shows.</summary>
        public string KeyArtName
        {
            get { return KeyArtVisible ? _keyArt.texture.name : null; }
        }

        /// <summary>The loading key art (Resources paths): the courtyard opens the rotation, the wall is the way to the Results.</summary>
        public const string LoadingKeyArt = "UI/Wallpapers/loading_keyart";
        public const string ResultsKeyArt = "UI/Wallpapers/loading_keyart_results";

        /// <summary>
        /// WP-081 B1 (C-00 05.10): the arts the loading screens rotate through, never the same one twice in a row
        /// (<see cref="KeyArtRotation"/>). Until AP-028's paintings land, 02-05 are placeholder copies of the courtyard.
        /// The Results keep their own art, outside the rotation.
        /// </summary>
        private static readonly string[] s_loadingKeyArts =
        {
            LoadingKeyArt,
            "UI/Wallpapers/loading_keyart_02",
            "UI/Wallpapers/loading_keyart_03",
            "UI/Wallpapers/loading_keyart_04",
            "UI/Wallpapers/loading_keyart_05"
        };

        private static KeyArtRotation s_rotation;

        public static IReadOnlyList<string> LoadingKeyArts
        {
            get { return s_loadingKeyArts; }
        }

        private static KeyArtRotation Rotation
        {
            get { return s_rotation ?? (s_rotation = new KeyArtRotation(s_loadingKeyArts.Length, Environment.TickCount)); }
        }

        /// <summary>The art the next loading screen shows (warmed ahead).</summary>
        public static string UpcomingKeyArt
        {
            get { return s_loadingKeyArts[Rotation.Upcoming]; }
        }

        /// <summary>The rotation art the last loading screen showed; null before the first.</summary>
        public static string LastLoadingKeyArt
        {
            get { return Rotation.Last < 0 ? null : s_loadingKeyArts[Rotation.Last]; }
        }

        /// <summary>
        /// The key art for a destination: a painted scene for every load (C-00 25.09, the user: no runtime frame of an
        /// empty world behind the ring) - the next one in the rotation, the Results their own. Asking does not use up a
        /// turn; showing it does. The Look B plates stay only as the fallback when the art cannot load.
        /// </summary>
        public static string KeyArtFor(string scene)
        {
            return scene == PrototypeScenes.Results ? ResultsKeyArt : UpcomingKeyArt;
        }

        public bool MidPlateVisible
        {
            get { return _plateMid != null && _plateMid.gameObject.activeSelf && _plateMid.texture != null; }
        }

        public bool StallVisible
        {
            get { return _stall != null && _stall.gameObject.activeSelf; }
        }

        public bool ErrorOpen
        {
            get { return _error != null && _error.IsOpen; }
        }

        /// <summary>R1-E B2: the load error stands - the screen under it must not act on Cancel (UiInput reads this).</summary>
        public static bool LoadErrorOpen
        {
            get { return s_instance != null && s_instance.ErrorOpen; }
        }

        /// <summary>The group's render state in one line (captures: why a frame shows what it shows).</summary>
        public string DebugState
        {
            get
            {
                return "group=" + (_group != null && _group.gameObject.activeInHierarchy) + " a=" + (_group != null ? _group.alpha.ToString("F2") : "-")
                       + " art=" + KeyArtVisible + " ring=" + DeviceVisible + " ringOnly=" + _ringOnly + " ink=" + _ink
                       + " canvas=" + (_canvas != null && _canvas.enabled) + " inkIdx=" + (_inkLayer != null ? _inkLayer.GetSiblingIndex() : -1)
                       + " groupIdx=" + (_group != null ? _group.transform.GetSiblingIndex() : -1) + " eyebrow=" + _eyebrowState;
            }
        }

        /// <summary>The key art's current push-in scale (captures: the t 0 / t 4 s evidence).</summary>
        public float KeyArtScale
        {
            get { return _keyArt != null ? _keyArt.rectTransform.localScale.x : 1f; }
        }

        /// <summary>The ring and its label stack are on screen (tests: hidden under the error modal).</summary>
        public bool DeviceVisible
        {
            get { return _ringGroup != null && _ringGroup.gameObject.activeInHierarchy; }
        }

        /// <summary>The stall notice's rect (tests: its slot).</summary>
        public RectTransform StallRect
        {
            get { return _stall; }
        }

        public string SublineText
        {
            get { return _subline != null ? _subline.text : string.Empty; }
        }

        // ================================================================== build

        private void Build()
        {
            _canvas = UiFactory.CreateCanvas("UiCurtain.Canvas", UiLayer.Curtain, transform);
            _root = (RectTransform)_canvas.transform;

            _leftDoor = Door("DoorLeft", 0f, 0.5f);
            _rightDoor = Door("DoorRight", 0.5f, 1f);
            BuildInkLayer();

            var groupRect = UiFactory.NewRect("Group", _root);
            UiFactory.Fill(groupRect);
            _group = UiFactory.Group(groupRect.gameObject);

            // Look B plates (slice 6 captures) under a Sumi900 scrim: 0.55 at the top, 0.80 at the
            // bottom (the bottom third is text-safe for the strip, T10 at the darkest end).
            _plateFar = Plate("PlateFar", groupRect);
            _plateMid = Plate("PlateMid", groupRect);
            _scrim = UiFactory.NewImage("Scrim", groupRect, UiColor.SurfaceInset, UiSprites.Solid);
            UiFactory.Fill((RectTransform)_scrim.transform);
            _scrim.color = WithAlpha(UiTokens.Get(UiColor.SurfaceInset), ScrimTop);
            _scrimFoot = UiFactory.NewImage("ScrimFoot", groupRect, UiColor.SurfaceInset, UiSprites.FadeUp);
            UiFactory.Fill((RectTransform)_scrimFoot.transform);
            _scrimFoot.color = WithAlpha(UiTokens.Get(UiColor.SurfaceInset), 1f - (1f - ScrimBottom) / (1f - ScrimTop));
            _keyArt = Plate("KeyArt", groupRect);   // above the scrims: the art is text-safe where the words sit
            // Warm the art now (behind the boot seal): a synchronous load on the first SCREEN would hitch the group's
            // first frame and eat its minimum display.
            WarmUpcomingKeyArt();
            _resultsKeyArtLoad = Resources.LoadAsync<Texture2D>(ResultsKeyArt);

            // The ensō device (10b-2, §15.3): the dry ghost circle, the stroke drawn at real
            // progress whose last WetBand is still wet (cream 0.6 under the dry cream, so the band
            // is the stroke's own shape and ends at the progress), and the V2 seal inside at rest ink.
            BuildBootLayer(groupRect);

            _ringGroup = UiFactory.NewRect("RingGroup", groupRect);
            Place(_ringGroup, RingSize);
            _ringGhost = UiFactory.NewImage("RingGhost", _ringGroup, UiColor.TextPrimary, UiSprites.EnsoBrush);
            UiFactory.Fill((RectTransform)_ringGhost.transform);
            _ringGhost.color = WithAlpha(UiTokens.Get(UiColor.TextPrimary), GhostAlpha);
            _ringWet = BrushFill("RingWet", UiColor.TextPrimary);
            _ringWet.color = WithAlpha(UiTokens.Get(UiColor.TextPrimary), WetAlpha);
            _ring = BrushFill("Ring", UiColor.TextPrimary);
            Sprite seal = Resources.Load<Sprite>("UI/Glyphs/Seal.Mark");
            _seal = UiFactory.NewImage("Seal", _ringGroup, UiColor.TextSecondary, seal);
            var sealRect = (RectTransform)_seal.transform;
            sealRect.anchorMin = new Vector2(0.5f, 0.5f);
            sealRect.anchorMax = new Vector2(0.5f, 0.5f);
            sealRect.pivot = new Vector2(0.5f, 0.5f);
            sealRect.sizeDelta = new Vector2(SealSize, SealSize);
            sealRect.anchoredPosition = Vector2.zero;
            _seal.preserveAspect = true;
            _seal.enabled = seal != null;
            RestSeal();

            _eyebrow = UiFactory.Label("Eyebrow", groupRect, UiStrings.Get("loading.word", "LOADING"), UiType.Label, UiColor.TextSecondary, TextAnchor.UpperCenter);
            PlaceText(_eyebrow, RingLift - RingSize * 0.5f - (int)UiSpace.XL, UiFactory.LineHeight(UiType.Label));

            _destination = UiFactory.Label("Destination", groupRect, string.Empty, UiType.H2, UiColor.TextPrimary, TextAnchor.UpperCenter);
            PlaceText(_destination, RingLift - RingSize * 0.5f - (int)UiSpace.XL - UiFactory.LineHeight(UiType.Label) - (int)UiSpace.S, UiFactory.LineHeight(UiType.H2));

            _subline = UiFactory.Label("Subline", groupRect, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.UpperCenter);
            PlaceText(_subline, RingLift - RingSize * 0.5f - (int)UiSpace.XL - UiFactory.LineHeight(UiType.Label) - (int)UiSpace.S - UiFactory.LineHeight(UiType.H2), UiFactory.LineHeight(UiType.Label));

            BuildTipStrip(groupRect);

            _group.gameObject.SetActive(false);
            SetDoorsOpen(true);
            _canvas.enabled = false;
        }

        /// <summary>
        /// The ink variant's layer (E22): four A8 blots centred on the screen's corners seep in
        /// (InkReveal, Blot channel - "köşelerden mürekkep sızması"), and an ink field closes
        /// whatever they leave. Above the doors, under the group (the Ø 160 ring stands in the ink).
        /// </summary>
        private void BuildInkLayer()
        {
            _inkLayer = UiFactory.NewRect("Ink", _root);
            UiFactory.Fill(_inkLayer);
            Sprite blot = UiFxGlobals.InkMask(UiInkChannel.Blot);
            var corners = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _inkFx = new UiFx[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            {
                Image image = UiFactory.NewImage("Blot" + i, _inkLayer, UiColor.InkStroke, blot);
                var rect = (RectTransform)image.transform;
                rect.anchorMin = corners[i];
                rect.anchorMax = corners[i];
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(InkBlotSize, InkBlotSize);
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.Euler(0f, 0f, 90f * i);   // four different edges of the same blot
                image.enabled = blot != null;
                _inkFx[i] = image.gameObject.AddComponent<UiFx>();
                _inkFx[i].Configure(UiFxMode.InkReveal, InkBleedSeconds, Vector3.zero, UiInkChannel.Blot);
                _inkFx[i].Hold();
            }

            _inkField = UiFactory.NewImage("Field", _inkLayer, UiColor.SurfaceInset, UiSprites.Solid);
            UiFactory.Fill((RectTransform)_inkField.transform);
            _inkField.canvasRenderer.SetAlpha(0f);
            _inkLayer.gameObject.SetActive(false);
        }

        /// <summary>
        /// E22 t 0.6 of the death beat: the ink bleeds in from the corners over 1.4 s and the
        /// field closes it - this IS the curtain closing for the Results load (no doors).
        /// Reduced Motion: the dim at once, static ink (§15.5).
        /// </summary>
        public void BeginInk()
        {
            if (!s_covering)
            {
                _cursorWasVisible = UiCursor.ContextVisible;
            }

            s_covering = true;
            _ink = true;
            _inkReceding = false;
            _canvas.enabled = true;
            SetDoorsOpen(true);
            _group.gameObject.SetActive(false);
            _inkLayer.gameObject.SetActive(true);
            for (int i = 0; i < _inkFx.Length; i++)
            {
                _inkFx[i].Configure(UiFxMode.InkReveal, InkBleedSeconds, Vector3.zero, UiInkChannel.Blot);
                _inkFx[i].Play();
            }

            UiMotion.KillTarget(_inkField, false);
            _inkField.canvasRenderer.SetAlpha(0f);
            UiMotion.TintAlpha(_inkField, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, InkBleedSeconds - UiMotionTokens.Seconds(UiDuration.Base));
        }

        /// <summary>True while the doors are shut (tests: the ink variant never closes them).</summary>
        public bool DoorsShut
        {
            get { return Mathf.Abs(_leftDoor.anchoredPosition.x) < 1f; }
        }

        /// <summary>True while the ink variant covers the screen (the Results load runs under it).</summary>
        public bool InkCovering
        {
            get { return _ink; }
        }

        /// <summary>
        /// AD 10b-2 MUST-2: true while a screen opens under the ink. Its static content must be on screen before the
        /// recede - the ink's erase IS the reveal - so screen fades and staggers land at rest; only the intro beats
        /// wait for <see cref="Revealed"/>.
        /// </summary>
        public static bool InkCovers
        {
            get { return s_instance != null && s_instance._ink; }
        }

        /// <summary>True once the ink has started to recede (tests: the screen under it was drawn before this).</summary>
        public bool InkReceding
        {
            get { return _inkReceding; }
        }

        /// <summary>A load under the ink (Run → Results): no doors - what Close does besides them.</summary>
        public void CoverWithInk()
        {
            UiCursor.SetContextVisible(false);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        /// <summary>
        /// Any other load while the ink is down (WP-061J: the abort during the death beat): the
        /// doors cut shut under the ink, then the ink goes - the frozen frame never shows between.
        /// </summary>
        private void EndInk()
        {
            if (!_ink)
            {
                return;
            }

            _ink = false;
            UiMotion.KillTarget(_leftDoor, false);
            UiMotion.KillTarget(_rightDoor, false);
            SetDoorsOpen(false);
            UiMotion.KillTarget(_inkField, false);
            _inkLayer.gameObject.SetActive(false);
        }

        /// <summary>The Results screen opens from the same ink (E22): the blots recede (erase mode) and the field lifts.</summary>
        private IEnumerator RecedeInk()
        {
            _inkReceding = true;
            RaiseOpened();
            for (int i = 0; i < _inkFx.Length; i++)
            {
                _inkFx[i].Configure(UiFxMode.InkReveal, InkRecedeSeconds, new Vector3(0f, 1f, 0f), UiInkChannel.Blot);
                _inkFx[i].Play();
            }

            UiMotion.TintAlpha(_inkField, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
            if (!UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(InkRecedeSeconds * 0.5f);
            }

            FinishReveal();
            if (!UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(InkRecedeSeconds * 0.5f);
            }

            _ink = false;
            _inkLayer.gameObject.SetActive(false);
            _canvas.enabled = false;
        }

        /// <summary>
        /// The boot seal variant's own layer (E1): an ink field, the A8 blot behind the seal, and
        /// the kicker + Display wordmark under it (the wordmark behind a clip that wipes open).
        /// </summary>
        private void BuildBootLayer(RectTransform groupRect)
        {
            _bootLayer = UiFactory.NewRect("Boot", groupRect);
            UiFactory.Fill(_bootLayer);
            Image field = UiFactory.NewImage("Field", _bootLayer, UiColor.SurfaceBase, UiSprites.Solid);
            UiFactory.Fill((RectTransform)field.transform);

            _blot = UiFactory.NewImage("Blot", _bootLayer, UiColor.InkStroke, UiFxGlobals.InkMask(UiInkChannel.Blot));
            Place((RectTransform)_blot.transform, BootBlotSize);
            _blot.enabled = _blot.sprite != null;   // the ink kit degrades: no mask, no blot (never a solid square)
            _blotFx = _blot.gameObject.AddComponent<UiFx>();
            _blotFx.Configure(UiFxMode.InkReveal, BootBlotSeconds, Vector3.zero, UiInkChannel.Blot);
            _blotFx.Hold();

            float top = RingLift - BootSealSize * 0.5f - (int)UiSpace.XL;
            _kicker = UiFactory.Label("Kicker", _bootLayer, UiStrings.Get("menu.eyebrow", "PLAYABLE CONCEPT · PROTOTYPE"), UiType.Label, UiColor.TextSecondary, TextAnchor.UpperCenter);
            PlaceText(_kicker, top, UiFactory.LineHeight(UiType.Label));
            top -= UiFactory.LineHeight(UiType.Label) + (int)UiSpace.S;

            float height = UiFactory.LineHeight(UiType.Display);
            _wordmarkClip = UiFactory.ClipContent(UiFactory.NewRect("WordmarkClip", _bootLayer));
            _wordmark = UiFactory.Label("Wordmark", _wordmarkClip, "DEATHS DEMOLITION", UiType.Display, UiColor.TextPrimary, TextAnchor.UpperCenter);
            _wordmark.characterSpacing = WordmarkTracking;
            _wordmarkWidth = Mathf.Ceil(_wordmark.GetPreferredValues(_wordmark.text).x) + (int)UiSpace.L;
            var clip = _wordmarkClip;
            clip.anchorMin = new Vector2(0.5f, 0.5f);
            clip.anchorMax = new Vector2(0.5f, 0.5f);
            clip.pivot = new Vector2(0f, 1f);
            clip.anchoredPosition = new Vector2(-_wordmarkWidth * 0.5f, top);
            clip.sizeDelta = new Vector2(_wordmarkWidth, height);
            var wordRect = _wordmark.rectTransform;
            wordRect.anchorMin = new Vector2(0f, 1f);
            wordRect.anchorMax = new Vector2(0f, 1f);
            wordRect.pivot = new Vector2(0f, 1f);
            wordRect.sizeDelta = new Vector2(_wordmarkWidth, height);
            wordRect.anchoredPosition = Vector2.zero;
            _bootLayer.gameObject.SetActive(false);
        }

        /// <summary>
        /// E1 / §15.3 (U7): the boot seal IS the first loading screen - no doors. t0 the ink
        /// blot (InkReveal 500), t 0.55 the vermilion seal Stamps (contact t 0.64 = the
        /// BootLogo bell), t 0.70 the wordmark wipes open under the kicker; held to t 1.6 by
        /// the flow. Reduced Motion: the static composite at once (§15.5).
        /// </summary>
        public void BeginBoot()
        {
            _cursorWasVisible = UiCursor.ContextVisible;
            s_covering = true;
            s_bootSkip = false;
            _canvas.enabled = true;
            UiCursor.SetContextVisible(false);
            SetDoorsOpen(true);
            _boot = true;
            _bootHandedOff = false;
            _bootStart = UiFxGlobals.Now;

            ResetRing();
            SetRingOnly(false);
            _eyebrow.gameObject.SetActive(false);
            _destination.gameObject.SetActive(false);
            _subline.gameObject.SetActive(false);
            _scrim.gameObject.SetActive(false);
            _scrimFoot.gameObject.SetActive(false);
            _plateFar.gameObject.SetActive(false);
            _plateMid.gameObject.SetActive(false);
            HideKeyArt();
            SetTip(null);
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }

            UiMotion.KillTarget(_group, false);
            _group.alpha = 1f;
            _group.gameObject.SetActive(true);
            _bootLayer.gameObject.SetActive(true);
            _ringGhost.canvasRenderer.SetAlpha(0f);
            _ringWet.canvasRenderer.SetAlpha(0f);
            _ring.canvasRenderer.SetAlpha(0f);

            _seal.rectTransform.sizeDelta = new Vector2(BootSealSize, BootSealSize);
            _seal.canvasRenderer.SetColor(WithAlpha(UiTokens.Get(UiColor.AccentPrimary), 0f));   // the logo seal: the only vermilion (§1)
            _kicker.canvasRenderer.SetAlpha(0f);
            _wordmark.canvasRenderer.SetAlpha(1f);
            _wordmarkClip.sizeDelta = new Vector2(0f, _wordmarkClip.sizeDelta.y);
            _blotFx.Configure(UiFxMode.InkReveal, BootBlotSeconds, Vector3.zero, UiInkChannel.Blot);
            _blotFx.Play();

            _bootTimeline = UiMotion.Timeline(BootOwner);
            _bootTimeline.At(BootStampAt);
            _bootTimeline.Add(UiMotion.TintAlpha(_seal, 1f, UiDuration.Instant, UiEase.Settle, UiMotionClass.Decor, _bootTimeline.Cursor));
            _bootTimeline.Add(UiPresets.StampIn(_seal.rectTransform, _bootTimeline.Cursor));
            _bootTimeline.Cue(UiEvent.BootLogo, BootStampAt + UiPresets.StampContactSeconds);
            _bootTimeline.At(BootWordmarkAt);
            _bootTimeline.Add(UiMotion.TintAlpha(_kicker, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor, _bootTimeline.Cursor));
            _bootTimeline.Add(UiMotion.Resize(_wordmarkClip, new Vector2(_wordmarkWidth, _wordmarkClip.sizeDelta.y), UiDuration.Screen, UiEase.Brush, UiMotionClass.Decor, _bootTimeline.Cursor));
            if (UiMotion.ReducedMotion)
            {
                CompleteBoot();
            }
        }

        /// <summary>A boot skip (Submit / Cancel / click, silent - no prompt, §15.5) or a test's request.</summary>
        public bool BootSkipRequested
        {
            get { return s_bootSkip || UiInput.CancelPressed() || UiInput.SubmitHeld() || Input.GetMouseButtonDown(0); }
        }

        public void RequestBootSkip()
        {
            s_bootSkip = true;
        }

        /// <summary>True while the boot seal variant is on screen (tests).</summary>
        public bool BootVisible
        {
            get { return _boot && GroupVisible; }
        }

        /// <summary>The boot choreography lands at t = 1 (the hold's end or a skip): cues collapse, the blot is whole.</summary>
        public void CompleteBoot()
        {
            if (_bootTimeline.IsValid && _bootTimeline.IsAlive)
            {
                _bootTimeline.Complete();
            }

            if (UiFxGlobals.Now - _bootStart < BootBlotSeconds)
            {
                _blotFx.Configure(UiFxMode.InkReveal, 0.01f, Vector3.zero, UiInkChannel.Blot);
                _blotFx.Play();
            }
        }

        /// <summary>
        /// The scene was not ready at t 1.6: the seal turns ink and settles into the ring's
        /// centre, the wordmark leaves (Exit 140), LOADING enters, and the ensō appears around the
        /// seal at the real progress (§15.3 el verme).
        /// </summary>
        public void BootHandOff()
        {
            _bootHandedOff = true;
            // AD 10b-2 SHOULD-6: the shrink is a move (Screen 280, Settle) with the vermilion → ink tint alongside -
            // no Stamp here, the stamp is the activation's mark.
            UiMotion.Tint(_seal, WithAlpha(UiTokens.Get(UiColor.TextSecondary), SealRestAlpha), UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.Resize(_seal.rectTransform, new Vector2(SealSize, SealSize), UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_kicker, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
            UiMotion.TintAlpha(_wordmark, 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
            _eyebrow.text = UiStrings.Get("loading.word", "LOADING");
            _eyebrowState = "LOADING";
            _eyebrow.gameObject.SetActive(true);
            _eyebrow.canvasRenderer.SetAlpha(0f);
            UiMotion.TintAlpha(_eyebrow, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_ringGhost, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_ringWet, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_ring, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor);
        }

        /// <summary>Back to the loading device after a boot (the next load builds on the plain layout).</summary>
        private void EndBootMode()
        {
            if (!_boot)
            {
                return;
            }

            _boot = false;
            _bootHandedOff = false;
            UiMotion.KillOwner(BootOwner, true);
            _bootLayer.gameObject.SetActive(false);
            _seal.rectTransform.sizeDelta = new Vector2(SealSize, SealSize);
            _ringGhost.canvasRenderer.SetAlpha(1f);
            _ringWet.canvasRenderer.SetAlpha(1f);
            _ring.canvasRenderer.SetAlpha(1f);
            _eyebrow.canvasRenderer.SetAlpha(1f);
        }

        /// <summary>A Radial360 fill of the ensō brush from 12 o'clock, clockwise (the Radial360 order is the brush's direction).</summary>
        private Image BrushFill(string name, UiColor color)
        {
            Image fill = UiFactory.NewImage(name, _ringGroup, color, UiSprites.EnsoBrush);
            UiFactory.Fill((RectTransform)fill.transform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            return fill;
        }

        /// <summary>
        /// The seal at rest: ink (TextSecondary) at 0.35, scale 1. The colour lives on the
        /// CanvasRenderer over a white Graphic - the channel UiMotion.Tint drives, so the stamp's
        /// ink → TextPrimary is an absolute colour, not a product of the two.
        /// </summary>
        private void RestSeal()
        {
            UiMotion.KillTarget(_seal, false);
            UiMotion.KillTarget(_seal.rectTransform, false);
            _seal.color = Color.white;
            _seal.canvasRenderer.SetColor(WithAlpha(UiTokens.Get(UiColor.TextSecondary), SealRestAlpha));
            _seal.rectTransform.localScale = Vector3.one;
        }

        private void ResetRing()
        {
            UiMotion.KillTarget(_ring, false);
            UiMotion.KillTarget(_ringWet, false);
            _ring.fillAmount = 0f;
            _ringWet.fillAmount = 0f;
            _shownFill = 0f;
            RestSeal();
        }

        private static RawImage Plate(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            UiFactory.Fill(rect);
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = false;
            go.SetActive(false);
            return image;
        }

        /// <summary>Tip strip (direction §15.3): bottom-left, 96 px in, ≤ 720 wide, SurfaceBase 0.72 with a hairline top edge; height from the text.</summary>
        private void BuildTipStrip(RectTransform parent)
        {
            Image strip = UiFactory.NewImage("TipStrip", parent, UiColor.SurfaceBase, UiSprites.Solid);
            strip.color = WithAlpha(UiTokens.Get(UiColor.SurfaceBase), 0.72f);
            _tipStrip = (RectTransform)strip.transform;
            _tipStrip.anchorMin = new Vector2(0f, 0f);
            _tipStrip.anchorMax = new Vector2(0f, 0f);
            _tipStrip.pivot = new Vector2(0f, 0f);
            _tipStrip.anchoredPosition = new Vector2(TipMargin, TipMargin);

            Image edge = UiFactory.NewImage("Edge", _tipStrip, UiColor.BorderHairline, UiSprites.Solid);
            var edgeRect = (RectTransform)edge.transform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 1f);

            _tip = UiFactory.Label("Tip", _tipStrip, string.Empty, UiType.Body, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform tipRect = UiFactory.Fill(_tip.rectTransform);
            tipRect.offsetMin = new Vector2((int)UiSpace.L, (int)UiSpace.M);
            tipRect.offsetMax = new Vector2(-(int)UiSpace.L, -(int)UiSpace.M);
            _tipStrip.gameObject.SetActive(false);
        }

        /// <summary>Show a tip on the strip (null / empty hides it); its height follows the text (1.25 → two lines, grows upward).</summary>
        private void SetTip(string tip)
        {
            bool show = !string.IsNullOrEmpty(tip);
            _tipStrip.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            float width = Mathf.Min(TipMaxWidth, _root.rect.width - TipMargin * 2f);
            _tip.text = tip;
            float textHeight = Mathf.Max(UiFactory.LineHeight(UiType.Body),
                Mathf.Ceil(_tip.GetPreferredValues(tip, width - (int)UiSpace.L * 2f, 0f).y));
            _tipStrip.sizeDelta = new Vector2(width, textHeight + (int)UiSpace.M * 2f);
        }

        /// <summary>Cover-fit a plate to the canvas (crop, never letterbox).</summary>
        private void Cover(RawImage image, string resource)
        {
            if (image.texture == null)
            {
                image.texture = Resources.Load<Texture2D>(resource);
            }

            Texture texture = image.texture;
            if (texture == null)
            {
                image.gameObject.SetActive(false);
                return;
            }

            CoverTexture(image, texture);
            image.gameObject.SetActive(true);
        }

        /// <summary>Cover-fit: the texture fills the screen at any aspect without stretching (centre crop).</summary>
        private void CoverTexture(RawImage image, Texture texture)
        {
            image.uvRect = CoverUv(_root.rect.width / Mathf.Max(1f, _root.rect.height), (float)texture.width / Mathf.Max(1, texture.height));
        }

        /// <summary>The part of a texture a cover-fit shows (centre crop), as a uvRect (LoadingKeyArtTests maps the words through it).</summary>
        public static Rect CoverUv(float screenAspect, float textureAspect)
        {
            if (screenAspect >= textureAspect)
            {
                float h = textureAspect / screenAspect;
                return new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }

            float w = screenAspect / textureAspect;
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }

        private RectTransform Door(string name, float anchorMinX, float anchorMaxX)
        {
            // Lacquer panel: SurfaceInset (Sumi900), never white (Reduce Flashes: nothing here flashes).
            Image door = UiFactory.NewImage(name, _root, UiColor.SurfaceInset, UiSprites.Solid);
            door.raycastTarget = true; // while closed, nothing underneath takes a click
            var rect = (RectTransform)door.transform;
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // A6 kumiko (asanoha) lattice on the lacquer, cream at KumikoAlpha (≤ 0.08; AD 10b-2 SHOULD-5, AD #12).
            UiPatternTile.Create("Kumiko", rect, UiTheme.KumikoTileResource, KumikoTilePixels, UiColor.TextPrimary, KumikoAlpha);
            return rect;
        }

        private void Place(RectTransform rect, float size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(0f, RingLift);
        }

        private void PlaceText(TMP_Text text, float top, float height)
        {
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(UiGrid.BindingPageWidth, height);
            rect.anchoredPosition = new Vector2(0f, top);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        // ================================================================== states

        private float HalfWidth
        {
            get { return Mathf.Max(960f, _root.rect.width * 0.5f); }
        }

        private void SetDoorsOpen(bool open)
        {
            float away = open ? HalfWidth + (int)UiSpace.S : 0f;
            _leftDoor.anchoredPosition = new Vector2(-away, 0f);
            _rightDoor.anchoredPosition = new Vector2(away, 0f);
        }

        /// <summary>Doors close (Move 280; RM or <paramref name="instant"/>: cut). Hides the pointer and clears focus while covered.</summary>
        public void Close(bool instant)
        {
            // A load that supersedes a reveal closes doors that are still shut: keep the cursor state from the first close.
            if (!s_covering)
            {
                _cursorWasVisible = UiCursor.ContextVisible;
            }

            s_covering = true;
            EndBootMode();
            bool inkWasDown = _ink;
            EndInk();
            _canvas.enabled = true;
            _group.gameObject.SetActive(false);
            UiCursor.SetContextVisible(false);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            if (instant || inkWasDown)
            {
                UiMotion.KillTarget(_leftDoor, false);
                UiMotion.KillTarget(_rightDoor, false);
                SetDoorsOpen(false);
                return;
            }

            UiMotion.Move(_leftDoor, Vector2.zero, UiDuration.Screen, UiEase.Move, UiMotionClass.Decor);
            UiMotion.Move(_rightDoor, Vector2.zero, UiDuration.Screen, UiEase.Move, UiMotionClass.Decor);
        }

        /// <summary>SCREEN depth: the loading group enters (PaneIn, Base 220) with the destination labels.</summary>
        public void ShowGroup(string destination, string subline)
        {
            ShowGroup(destination, subline, null, false, UpcomingKeyArt);
        }

        /// <summary>
        /// SCREEN depth with its key art and tip (the plates - far always, mid too on the way into the
        /// run - only when the art cannot load); <paramref name="tip"/> null = no strip
        /// (Boot, Results, the Pause abort). Reduced Motion shows the static RM line instead.
        /// </summary>
        public void ShowGroup(string destination, string subline, string tip, bool midPlate, string keyArt = LoadingKeyArt)
        {
            _eyebrow.text = UiStrings.Get("loading.word", "LOADING");
            _eyebrowState = "LOADING";
            _percent = -1;
            _destination.text = destination ?? string.Empty;
            _subline.text = subline ?? string.Empty;
            EndBootMode();
            ResetRing();
            SetRingOnly(false);
            _group.gameObject.SetActive(true);
            Cover(_plateFar, "MenuPlates/plate_far");
            if (midPlate)
            {
                Cover(_plateMid, "MenuPlates/plate_mid");
            }
            else
            {
                _plateMid.gameObject.SetActive(false);
            }

            ShowKeyArt(keyArt);
            SetTip(UiMotion.ReducedMotion ? UiStrings.Get("loading.reduced_motion", UiLoadingTips.ReducedMotionLine) : tip);
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }

            UiPresets.PaneIn(_group, null, 0f);
        }

        /// <summary>
        /// E22 slow Results load (C-00 25.09, decision A; AD R1-D SHOULD-6): once the load under the ink runs past the ring
        /// threshold, the Results key art opens INSIDE the ink - the group fades in over the ink field (PaneIn, Base) with the
        /// full ring Ø 240 and its labels, no tip - instead of the Ø 160 ring alone. The group leaves (Exit, Fast) before the
        /// ink recedes into Results (<see cref="Open"/>). A fast load never gets here: the ink opens straight into Results.
        /// </summary>
        public void ShowInkGroup(string destination, string keyArt)
        {
            ShowGroup(destination, string.Empty, null, false, keyArt);
        }

        /// <summary>The key art (or none): cover-fit, over the plates, pushing in slowly while the load runs.</summary>
        private void ShowKeyArt(string resource)
        {
            UiMotion.Kill(_keyArtPush, false);
            ResourceRequest warmed = resource == _keyArtLoadPath ? _keyArtLoad : resource == ResultsKeyArt ? _resultsKeyArtLoad : null;
            Texture2D art = null;
            if (!string.IsNullOrEmpty(resource))
            {
                art = warmed != null ? warmed.asset as Texture2D : null;
                if (art == null)
                {
                    art = Resources.Load<Texture2D>(resource);   // off the warm path (a test's pick), or a warm the scene change let go
                }
            }

            int turn = Array.IndexOf(s_loadingKeyArts, resource);
            if (turn >= 0)
            {
                Rotation.Shown(turn);
                WarmUpcomingKeyArt();
            }

            if (art == null)
            {
                _keyArt.gameObject.SetActive(false);
                return;
            }

            _keyArt.texture = art;
            CoverTexture(_keyArt, art);
            _keyArt.gameObject.SetActive(true);
            _plateFar.gameObject.SetActive(false);   // hidden under the opaque art: no fill spent on them
            _plateMid.gameObject.SetActive(false);
            var rect = (RectTransform)_keyArt.transform;
            rect.localScale = Vector3.one;
            _keyArtPush = UiMotion.ScaleSeconds(rect, 1f, KeyArtPushScale, KeyArtPushSeconds, UiEase.Linear, UiMotionClass.Decor);
        }

        /// <summary>Warm the rotation's next art: at Build behind the boot seal, then behind each loading screen that used its turn.</summary>
        private void WarmUpcomingKeyArt()
        {
            string next = UpcomingKeyArt;
            if (next != _keyArtLoadPath)
            {
                _keyArtLoadPath = next;
                _keyArtLoad = Resources.LoadAsync<Texture2D>(next);
            }
        }

        private void HideKeyArt()
        {
            UiMotion.Kill(_keyArtPush, false);
            if (_keyArt != null)
            {
                _keyArt.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// CURTAIN depth past 1.2 s (§15.3): the same ring, Ø 160, alone over the closed doors -
        /// no plate, no labels, no tip. The seal and the states are the ring's own.
        /// </summary>
        public void ShowRingOnly(string destination = null)
        {
            _eyebrowState = "LOADING";
            EndBootMode();
            ResetRing();
            SetRingOnly(true);
            if (destination != null)
            {
                // The ink variant keeps its two labels under the small ring (§15.1: LOADING / RUN RESULTS).
                _eyebrow.text = UiStrings.Get("loading.word", "LOADING");
                _eyebrow.gameObject.SetActive(true);
                _destination.text = destination;
                _destination.gameObject.SetActive(true);
            }

            _group.gameObject.SetActive(true);
            _plateFar.gameObject.SetActive(false);
            _plateMid.gameObject.SetActive(false);
            HideKeyArt();
            SetTip(null);
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }

            UiPresets.PaneIn(_group, null, 0f);
        }

        /// <summary>True while the CURTAIN-depth ring is shown (tests).</summary>
        public bool RingOnlyVisible
        {
            get { return _ringOnly && GroupVisible; }
        }

        /// <summary>The seal device's current colour (tests: rest ink 0.35, stamped TextPrimary).</summary>
        public Color SealColor
        {
            get { return _seal != null ? _seal.color * _seal.canvasRenderer.GetColor() : Color.clear; }
        }

        /// <summary>The wet tip's fill (tests: it ends at or before <see cref="ShownProgress"/>, WetBand past the dry stroke).</summary>
        public float WetFill
        {
            get { return _ringWet != null ? _ringWet.fillAmount : 0f; }
        }

        /// <summary>The wet tip's colour (tests: cream at <see cref="WetAlpha"/>).</summary>
        public Color WetColor
        {
            get { return _ringWet != null ? _ringWet.color * _ringWet.canvasRenderer.GetColor() : Color.clear; }
        }

        /// <summary>The last load progress the device drew (0..LoadShare).</summary>
        public float ShownProgress
        {
            get { return Mathf.Max(0f, _shownFill); }
        }

        private void SetRingOnly(bool ringOnly)
        {
            _ringOnly = ringOnly;
            _ringGroup.gameObject.SetActive(true);
            _scrim.gameObject.SetActive(!ringOnly);
            _scrimFoot.gameObject.SetActive(!ringOnly);
            _eyebrow.gameObject.SetActive(!ringOnly);
            _destination.gameObject.SetActive(!ringOnly);
            _subline.gameObject.SetActive(!ringOnly);
            float scale = ringOnly ? CurtainRingSize / RingSize : 1f;
            _ringGroup.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>M-70: progress came back before the error - the stall notice goes (the watchdog re-arms).</summary>
        public void HideStalled()
        {
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }
        }

        /// <summary>No progress for 10 s: an Info notice under the tip (direction §15.2).</summary>
        public void ShowStalled()
        {
            if (!GroupVisible)
            {
                return;
            }

            if (_stall == null)
            {
                float width = Mathf.Min(TipMaxWidth, _root.rect.width - TipMargin * 2f);
                _stall = UiNotices.Notice("Stalled", _group.transform, UiNoticeKind.Info, UiStrings.Get("loading.stalled", UiLoadingTips.StalledLine), width);
                _stall.anchorMin = new Vector2(0f, 0f);
                _stall.anchorMax = new Vector2(0f, 0f);
            }

            // Under the tip when there is one; in the tip's own slot (bottom edge 96 in) when there is none
            // (12b, AD R1-D SHOULD-7a: it hung at the screen's foot on the way to the Results).
            bool underTip = _tipStrip.gameObject.activeSelf;
            _stall.pivot = new Vector2(0f, underTip ? 1f : 0f);
            _stall.anchoredPosition = new Vector2(TipMargin, underTip ? TipMargin - (int)UiSpace.S : TipMargin);
            _stall.gameObject.SetActive(true);
        }

        /// <summary>
        /// The load error (UXW §2.2): an opaque M modal on the curtain - THE NEXT SCREEN DID NOT
        /// LOAD, why, and what now. <paramref name="onPrimary"/> null = QUIT only (a stalled load
        /// cannot be cancelled). Cancel does nothing: there is no safe "back". Nothing writes the profile.
        /// </summary>
        public void ShowError(string scene, bool stalled, string primaryLabel, Action onPrimary)
        {
            HideError();
            _canvas.enabled = true;
            string body = UiStrings.Format("loading.error.body", "value", SentenceFor(scene), "reason",
                stalled
                    ? UiStrings.Get("loading.error.stalled", "Loading stopped making progress.")
                    : UiStrings.Get("loading.error.missing", "The scene is missing from this build."),
                "Deaths Demolition could not open {value}. {reason} Your profile was not changed.");
            Action quit = Application.Quit;
            // M width, height from the content (no void under a two-line body).
            // The strip and the stall notice would repeat what the modal says (AD 10b-1 SHOULD-2).
            _tipStrip.gameObject.SetActive(false);
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }

            // The ring and its labels step out under the modal (12b, AD R1-D SHOULD-7b): the arc peeked over the modal's top
            // edge and the destination sat on its bottom border. HideError puts back what was shown.
            HideDevice();
            _error = onPrimary != null
                ? UiModal.Create("LoadError", _root, UiModalSize.M, UiStrings.Get("loading.error.title", "THE NEXT SCREEN DID NOT LOAD"), body, primaryLabel, UiButtonVariant.Primary, onPrimary, UiStrings.Get("menu.quit", "QUIT"), quit, false, true)
                : UiModal.Create("LoadError", _root, UiModalSize.M, UiStrings.Get("loading.error.title", "THE NEXT SCREEN DID NOT LOAD"), body, UiStrings.Get("menu.quit", "QUIT"), UiButtonVariant.Secondary, quit, null, null, false, true);
            _error.Open();
            UiCursor.SetContextVisible(true);
        }

        public void HideError()
        {
            if (_error != null)
            {
                Destroy(_error.gameObject);
                _error = null;
                RestoreDevice();
            }
        }

        private readonly bool[] _deviceShown = new bool[4];

        private void HideDevice()
        {
            GameObject[] parts = { _ringGroup.gameObject, _eyebrow.gameObject, _destination.gameObject, _subline.gameObject };
            for (int i = 0; i < parts.Length; i++)
            {
                _deviceShown[i] = parts[i].activeSelf;
                parts[i].SetActive(false);
            }
        }

        private void RestoreDevice()
        {
            GameObject[] parts = { _ringGroup.gameObject, _eyebrow.gameObject, _destination.gameObject, _subline.gameObject };
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].SetActive(_deviceShown[i]);
            }
        }

        /// <summary>The destination in sentence case for the error body.</summary>
        public static string SentenceFor(string scene)
        {
            if (scene == PrototypeScenes.MainMenu) return UiStrings.Get("loading.dest.main_menu", "the main menu");
            if (scene == PrototypeScenes.Selection) return UiStrings.Get("loading.dest.selection", "the selection screen");
            if (scene == PrototypeScenes.Run) return UiStrings.Get("loading.dest.run", "the run");
            if (scene == PrototypeScenes.Results) return UiStrings.Get("loading.dest.results", "the results screen");
            return UiStrings.Get("loading.dest.next", "the next screen");
        }

        /// <summary>
        /// Honest progress: <paramref name="loadProgress"/> is AsyncOperation.progress (0..0.9). The dry stroke is
        /// written only on a visible change (Δ ≥ 1/256); the wet tip ends at the progress and trembles back from it.
        /// </summary>
        public void SetProgress(float loadProgress)
        {
            float fill = Mathf.Clamp(loadProgress, 0f, LoadShare);
            bool moved = fill > _shownFill && fill - _shownFill >= 1f / 256f;   // never backwards, never a meaningless write
            if (moved)
            {
                _shownFill = fill;
                _ring.fillAmount = Mathf.Max(0f, fill - WetBand);
            }

            // AD 10b-2 MUST-1: the wet tip is the stroke's last WetBand - its front is the progress, trembling
            // back from it a step at a time; no pixel past the truth. Reduced motion: still.
            float front = Mathf.Max(0f, _shownFill);
            if (!UiMotion.ReducedMotion && front > WetBand)
            {
                front -= WetTremble * Hash01(Mathf.Floor(Time.unscaledTime * WetTrembleHz));
            }

            if (Mathf.Abs(front - _ringWet.fillAmount) >= 1f / 1024f)
            {
                _ringWet.fillAmount = front;
            }

            if (moved && UiMotion.ReducedMotion)
            {
                // RM: the eyebrow carries the number, in steps of 10 (0-90 from the load).
                int percent = Mathf.FloorToInt(fill / LoadShare * 9f) * 10;
                if (percent != _percent)
                {
                    _percent = percent;
                    _eyebrow.SetText(UiStrings.Get("loading.percent", "LOADING · {0}%"), percent);
                }
            }
        }

        private static float Hash01(float x)
        {
            float s = Mathf.Sin(x * 12.9898f + 3.1f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        /// <summary>Activation: ENTERING, the seal step (Stamp), then the last 36° close as the scene activates.</summary>
        public void Activate()
        {
            if (!GroupVisible)
            {
                return;
            }

            if (_boot && !_bootHandedOff)
            {
                // Ready by t 1.6: the stamp at the hold's end IS the activation - nothing extra shows.
                UiPresets.StampIn(_seal.rectTransform);
                return;
            }

            _eyebrow.text = UiMotion.ReducedMotion ? UiStrings.Get("loading.entering.full", "ENTERING · 100%") : UiStrings.Get("loading.entering", "ENTERING");
            _eyebrowState = "ENTERING";
            // The ring never scales: the seal marks the step (Stamp 1.12 → 1.00, ink → TextPrimary,
            // §15.3), and the mouth closes as the scene activates; the wet edge dries into the stroke.
            UiMotion.Fill(_ring, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Data);
            UiMotion.Fill(_ringWet, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Data);
            if (_seal.enabled)
            {
                UiPresets.StampIn(_seal.rectTransform);
                UiMotion.Tint(_seal, UiTokens.Get(UiColor.TextPrimary), UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);   // ink 0.35 → TextPrimary 1 (renderer colour)
            }
        }

        /// <summary>Group out (Exit 140), then the doors part (Settle 280); <see cref="Revealed"/> fires half way. Returns the seconds it takes.</summary>
        public float Open(bool instant)
        {
            if (_ink && !instant)
            {
                float groupOutSeconds = 0f;
                if (GroupVisible)
                {
                    UiPresets.PaneOut(_group);
                    groupOutSeconds = UiMotionTokens.Seconds(UiDuration.Fast);
                }

                StartCoroutine(RecedeAfter(groupOutSeconds));
                return groupOutSeconds + InkRecedeSeconds;
            }

            if (instant)
            {
                _ink = false;
                _inkLayer.gameObject.SetActive(false);
                _group.gameObject.SetActive(false);
                SetDoorsOpen(true);
                RaiseOpened();
                FinishReveal();
                _canvas.enabled = false;
                return 0f;
            }

            float groupOut = 0f;
            if (GroupVisible)
            {
                UiPresets.PaneOut(_group);
                groupOut = UiMotionTokens.Seconds(UiDuration.Fast);
            }

            StartCoroutine(OpenDoors(groupOut));
            return groupOut + UiMotionTokens.Seconds(UiDuration.Screen);
        }

        private IEnumerator RecedeAfter(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            _group.gameObject.SetActive(false);
            yield return RecedeInk();
        }

        /// <summary>The S-08 open cue fires on the first frame the doors part.</summary>
        private static void RaiseOpened()
        {
            UiEvents.Raise(UiEvent.CurtainOpened);
        }

        private IEnumerator OpenDoors(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            _group.gameObject.SetActive(false);
            RaiseOpened();
            float away = HalfWidth + (int)UiSpace.S;
            UiMotion.Move(_leftDoor, new Vector2(-away, 0f), UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.Move(_rightDoor, new Vector2(away, 0f), UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            if (!UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(UiMotionTokens.Seconds(UiDuration.Screen) * 0.5f);
            }

            FinishReveal();
            if (!UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(UiMotionTokens.Seconds(UiDuration.Screen) * 0.5f);
            }

            _canvas.enabled = false;
        }

        private void FinishReveal()
        {
            s_covering = false;
            UiCursor.SetContextVisible(_cursorWasVisible);
            Action handlers = Revealed;
            if (handlers != null)
            {
                handlers();
            }
        }

        /// <summary>The destination label for a scene (UXW §2: real names only).</summary>
        public static string DestinationFor(string scene)
        {
            if (scene == PrototypeScenes.MainMenu) return UiStrings.Get("common.main_menu", "MAIN MENU");
            if (scene == PrototypeScenes.Selection) return UiStrings.Get("selection.eyebrow", "BEFORE THE RUN");
            if (scene == PrototypeScenes.Run) return UiStrings.Format("hud.banner.round", "value", "1", "ROUND {value}");
            if (scene == PrototypeScenes.Results) return UiStrings.Get("results.title", "RUN RESULTS");
            return string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_covering = false;
            s_rotation = null;
            Revealed = null;
        }
    }
}
