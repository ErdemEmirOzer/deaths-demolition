using System;
using System.Collections;
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
    /// Sumi900 scrim 0.55 → 0.80, a tip strip bottom-left (U9, <see cref="UiLoadingTips"/>),
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

        /// <summary>CURTAIN depth: the ring alone, Ø 160, once a curtain-only load passes 1.2 s (§15.3).</summary>
        public const float CurtainRingSize = 160f;
        public const float GhostAlpha = 0.12f;

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
        private TMP_Text _eyebrow;
        private TMP_Text _destination;
        private TMP_Text _subline;
        private float _shownFill = -1f;
        private bool _cursorWasVisible = true;
        private RawImage _plateFar;
        private RawImage _plateMid;
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

        /// <summary>The tip on the strip (empty when none is shown).</summary>
        public string TipText
        {
            get { return _tipStrip != null && _tipStrip.gameObject.activeSelf ? _tip.text : string.Empty; }
        }

        public bool PlateVisible
        {
            get { return _plateFar != null && _plateFar.gameObject.activeSelf && _plateFar.texture != null; }
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

            // The ensō device (10b-2, §15.3): the dry ghost circle, the stroke drawn at real
            // progress with its wet edge (a darker fill running WetBand ahead of the cream one,
            // so the band is the stroke's own shape), and the V2 seal inside at rest ink.
            _ringGroup = UiFactory.NewRect("RingGroup", groupRect);
            Place(_ringGroup, RingSize);
            Image ghost = UiFactory.NewImage("RingGhost", _ringGroup, UiColor.TextPrimary, UiSprites.EnsoBrush);
            UiFactory.Fill((RectTransform)ghost.transform);
            ghost.color = WithAlpha(UiTokens.Get(UiColor.TextPrimary), GhostAlpha);
            _ringWet = BrushFill("RingWet", UiColor.TextSecondary);
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

            _eyebrow = UiFactory.Label("Eyebrow", groupRect, "LOADING", UiType.Label, UiColor.TextSecondary, TextAnchor.UpperCenter);
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
            float textHeight = Mathf.Max(UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Body)),
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

            float screenAspect = _root.rect.width / Mathf.Max(1f, _root.rect.height);
            float textureAspect = (float)texture.width / Mathf.Max(1, texture.height);
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

            image.gameObject.SetActive(true);
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
                _cursorWasVisible = Cursor.visible;
            }

            s_covering = true;
            _canvas.enabled = true;
            _group.gameObject.SetActive(false);
            Cursor.visible = false;
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            if (instant)
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
            ShowGroup(destination, subline, null, false);
        }

        /// <summary>
        /// SCREEN depth with its plate and tip: far plate always, the mid plate too on the way into
        /// the run (the lanterns lit: where you are going); <paramref name="tip"/> null = no strip
        /// (Boot, Results, the Pause abort). Reduced Motion shows the static RM line instead.
        /// </summary>
        public void ShowGroup(string destination, string subline, string tip, bool midPlate)
        {
            _eyebrow.text = "LOADING";
            _eyebrowState = "LOADING";
            _percent = -1;
            _destination.text = destination ?? string.Empty;
            _subline.text = subline ?? string.Empty;
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

            SetTip(UiMotion.ReducedMotion ? UiLoadingTips.ReducedMotionLine : tip);
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }

            UiPresets.PaneIn(_group, null, 0f);
        }

        /// <summary>
        /// CURTAIN depth past 1.2 s (§15.3): the same ring, Ø 160, alone over the closed doors -
        /// no plate, no labels, no tip. The seal and the states are the ring's own.
        /// </summary>
        public void ShowRingOnly()
        {
            _eyebrowState = "LOADING";
            ResetRing();
            SetRingOnly(true);
            _group.gameObject.SetActive(true);
            _plateFar.gameObject.SetActive(false);
            _plateMid.gameObject.SetActive(false);
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

        /// <summary>The wet edge's fill (tests: it runs WetBand ahead of the stroke).</summary>
        public float WetFill
        {
            get { return _ringWet != null ? _ringWet.fillAmount : 0f; }
        }

        private void SetRingOnly(bool ringOnly)
        {
            _ringOnly = ringOnly;
            _scrim.gameObject.SetActive(!ringOnly);
            _scrimFoot.gameObject.SetActive(!ringOnly);
            _eyebrow.gameObject.SetActive(!ringOnly);
            _destination.gameObject.SetActive(!ringOnly);
            _subline.gameObject.SetActive(!ringOnly);
            float scale = ringOnly ? CurtainRingSize / RingSize : 1f;
            _ringGroup.localScale = new Vector3(scale, scale, 1f);
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
                _stall = UiNotices.Notice("Stalled", _group.transform, UiNoticeKind.Info, UiLoadingTips.StalledLine, width);
                _stall.anchorMin = new Vector2(0f, 0f);
                _stall.anchorMax = new Vector2(0f, 0f);
                _stall.pivot = new Vector2(0f, 1f);
                _stall.anchoredPosition = new Vector2(TipMargin, TipMargin - (int)UiSpace.S);
            }

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
            string body = "Deaths Demolition could not open " + SentenceFor(scene) + ". "
                + (stalled ? "Loading stopped making progress." : "The scene is missing from this build.")
                + " Your profile was not changed.";
            Action quit = Application.Quit;
            // M width, height from the content (no void under a two-line body).
            // The strip and the stall notice would repeat what the modal says (AD 10b-1 SHOULD-2).
            _tipStrip.gameObject.SetActive(false);
            if (_stall != null)
            {
                _stall.gameObject.SetActive(false);
            }

            _error = onPrimary != null
                ? UiModal.Create("LoadError", _root, UiModalSize.M, "THE NEXT SCREEN DID NOT LOAD", body, primaryLabel, UiButtonVariant.Primary, onPrimary, "QUIT", quit, false, true)
                : UiModal.Create("LoadError", _root, UiModalSize.M, "THE NEXT SCREEN DID NOT LOAD", body, "QUIT", UiButtonVariant.Secondary, quit, null, null, false, true);
            _error.Open();
            Cursor.visible = true;
        }

        public void HideError()
        {
            if (_error != null)
            {
                Destroy(_error.gameObject);
                _error = null;
            }
        }

        /// <summary>The destination in sentence case for the error body.</summary>
        public static string SentenceFor(string scene)
        {
            if (scene == PrototypeScenes.MainMenu) return "the main menu";
            if (scene == PrototypeScenes.Selection) return "the selection screen";
            if (scene == PrototypeScenes.Run) return "the run";
            if (scene == PrototypeScenes.Results) return "the results screen";
            return "the next screen";
        }

        /// <summary>Honest progress: <paramref name="loadProgress"/> is AsyncOperation.progress (0..0.9). Written only on a visible change (Δ ≥ 1/256).</summary>
        public void SetProgress(float loadProgress)
        {
            float fill = Mathf.Clamp(loadProgress, 0f, LoadShare);
            if (fill < _shownFill || Mathf.Abs(fill - _shownFill) < 1f / 256f)
            {
                return; // never backwards, never a meaningless write
            }

            _shownFill = fill;
            _ringWet.fillAmount = fill;
            _ring.fillAmount = Mathf.Max(0f, fill - WetBand);

            if (UiMotion.ReducedMotion)
            {
                // RM: the eyebrow carries the number, in steps of 10 (0-90 from the load).
                int percent = Mathf.FloorToInt(fill / LoadShare * 9f) * 10;
                if (percent != _percent)
                {
                    _percent = percent;
                    _eyebrow.SetText("LOADING · {0}%", percent);
                }
            }
        }

        /// <summary>Activation: ENTERING, the seal step (Stamp), then the last 36° close as the scene activates.</summary>
        public void Activate()
        {
            if (!GroupVisible)
            {
                return;
            }

            _eyebrow.text = UiMotion.ReducedMotion ? "ENTERING · 100%" : "ENTERING";
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
            if (instant)
            {
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
            Cursor.visible = _cursorWasVisible;
            Action handlers = Revealed;
            if (handlers != null)
            {
                handlers();
            }
        }

        /// <summary>The destination label for a scene (UXW §2: real names only).</summary>
        public static string DestinationFor(string scene)
        {
            if (scene == PrototypeScenes.MainMenu) return "MAIN MENU";
            if (scene == PrototypeScenes.Selection) return "BEFORE THE RUN";
            if (scene == PrototypeScenes.Run) return "ROUND 1";
            if (scene == PrototypeScenes.Results) return "RUN RESULTS";
            return string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_covering = false;
            Revealed = null;
        }
    }
}
