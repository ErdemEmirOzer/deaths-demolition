using DD.Prototype.CameraSystem;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The pause screen and its quit-to-menu confirmation.
    ///
    /// IT DOES NOT TOUCH Time.timeScale
    /// --------------------------------
    /// It registers a named hold on <see cref="RunFlowClock"/> instead. Three systems in
    /// this prototype want the world stopped - the round-end upgrade screen, this
    /// screen, and the results hand-off - and they overlap. Pausing during the upgrade
    /// screen and then resuming must NOT start the enemies moving under an open shop; a
    /// single boolean cannot express that, and writing the time scale directly is not a
    /// visible bug until the day those two overlap, which is the first thing a player
    /// will do. The clock restarts only when the LAST hold is released.
    ///
    /// THE CONFIRMATION IS NOT DECORATION
    /// Leaving to the main menu destroys the run: the round, the gold and every upgrade
    /// bought with it. The dialog says exactly that, in those words, and it also says
    /// what is NOT lost - profile progress already earned - because a warning that only
    /// lists the downside makes players hesitate over a safe action.
    ///
    /// WHEN IT MAY OPEN (WP-005)
    /// Only while a run is live and the navigation base is the HUD: never over Results,
    /// never in a menu, and not during the death beat - a pause taken after the player
    /// is dead would hold the clock the Results hand-off is about to release, and a
    /// "PAUSED" panel over a corpse reads as a hang. The screen reports itself to
    /// <see cref="UiNavigation"/> as the <see cref="UiContext.Paused"/> overlay, which
    /// is how the upgrade screen knows to drop interactivity without searching the
    /// scene for this component every frame.
    ///
    /// FIVE STATES. Empty and loading do not exist here: the screen has no data source.
    /// Normal is the pause panel; the confirmation is the dense state; the error state
    /// is a missing camera rig, which greys out the shake slider rather than throwing.
    /// </summary>
    public sealed class PauseScreen : UiScreen
    {
        private const float DialogWidth = 640f;
        private const float DialogHeight = 340f;
        private const float ShakeMax = 2f;
        private const string FrozenOwner = "pause";

        protected override int SortingOrder
        {
            get { return UiTokens.SortingPause; }
        }

        protected override bool WantsUiCamera
        {
            get { return false; }
        }

        protected override bool WantsBackdrop
        {
            get { return false; }
        }

        protected override bool WantsPageGrid
        {
            get { return false; }
        }

        private GameObject _pauseRoot;
        private CanvasGroup _pauseGroup;
        private GameObject _confirmRoot;

        private UiButton _resumeButton;
        private UiButton _cancelButton;

        private Slider _shakeSlider;
        private TMP_Text _shakeValue;

        // M4-R1 N2 (WP-070 §5.1): the segmented CAMERA selector and the chosen mode's one-line description.
        // R1 content pack (planning/content/r1-strings.json): camera.mode.* and camera.mode.*.desc. The first-person line was
        // rewritten in the pack for the rear-edge arc (AD R1-A SHOULD-2): threats behind are marked, not unseen.
        public static readonly string[] CameraModeNames = { "TOP-DOWN", "THIRD-PERSON", "FIRST-PERSON" };   // fallbacks (WP-068 TR)
        private static readonly string[] CameraModeKeys = { "camera.mode.top_down", "camera.mode.third_person", "camera.mode.first_person" };
        private static readonly string[] CameraModeCaptionKeys = { "camera.mode.top_down.desc", "camera.mode.third_person.desc", "camera.mode.first_person.desc" };

        /// <summary>WP-068 TR: a camera mode's name in the chosen language.</summary>
        public static string CameraModeName(int index)
        {
            return UiStrings.Get(CameraModeKeys[index], CameraModeNames[index]);
        }

        /// <summary>WP-068 TR: a camera mode's one-line caption in the chosen language.</summary>
        public static string CameraModeCaption(int index)
        {
            return UiStrings.Get(CameraModeCaptionKeys[index], CameraModeCaptions[index]);
        }

        public static readonly string[] CameraModeCaptions =
        {
            "The whole courtyard in view. Aim with the cursor.",
            "Over the shoulder. Aim from the centre of the screen.",
            "Eye level. The sharpest aim; threats behind you show at the edge.",
        };

        private readonly UiButton[] _cameraButtons = new UiButton[3];
        private TMP_Text _cameraCaption;
        private TMP_Text _cameraLabel;
        private RectTransform _cameraRow;

        // R1-C: the measured stack (AD R1-A SHOULD-3) and the run-so-far strip (direction E25).
        private const int RunCells = 5;
        private RectTransform _panel;
        private RectTransform _divider;
        private TMP_Text _title;
        private TMP_Text _caption;
        private TMP_Text _runLabel;
        private RectTransform _runRow;
        private readonly TMP_Text[] _runValues = new TMP_Text[RunCells];
        private readonly TMP_Text[] _runWords = new TMP_Text[RunCells];
        private TMP_Text _runBuild;
        private TMP_Text _shakeLabel;
        private TMP_Text _shakeCaption;
        private bool _blurReleased;

        private bool _paused;
        private bool _confirming;

        /// <summary>Whatever held focus when the pause opened - a shop card, CONTINUE - to hand it back on close.</summary>
        private GameObject _focusBefore;

        public bool IsPaused
        {
            get { return _paused; }
        }

        /// <summary>The CAMERA segments (tests, captures).</summary>
        public UiButton CameraButton(CameraMode mode)
        {
            return _cameraButtons[(int)mode];
        }

        public string CameraCaption
        {
            get { return _cameraCaption != null ? _cameraCaption.text : string.Empty; }
        }

        /// <summary>R1-C: the card, the run-so-far strip and the stack's last line above the foot (tests, captures).</summary>
        public RectTransform PanelRect
        {
            get { return _panel; }
        }

        public string RunValue(int cell)
        {
            return _runValues[cell].text;
        }

        public string RunBuild
        {
            get { return _runBuild.text; }
        }

        public RectTransform StackEnd
        {
            get { return _shakeCaption.rectTransform; }
        }

        public RectTransform ResumeRect
        {
            get { return (RectTransform)_resumeButton.transform; }
        }

        protected override void Build()
        {
            BuildPausePanel();
            BuildConfirmDialog();

            _pauseRoot.SetActive(false);
            _confirmRoot.SetActive(false);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            // A hold that outlived its screen would freeze the next scene.
            if (_paused)
            {
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                UiFrozenFrame.Release(FrozenOwner);
                UiNavigation.ExitOverlay(UiContext.Paused);
            }
        }

        private void Update()
        {
            if (!InputSourceRegistry.Current.PausePressed)
            {
                return;
            }

            if (_confirming)
            {
                CloseConfirm();
                return;
            }

            if (!_paused && !CanOpen())
            {
                return;
            }

            SetPaused(!_paused);
        }

        /// <summary>
        /// A pause needs a live run underneath it and the HUD as the navigation base.
        /// The run check also refuses the death beat (RunPhase.Dead) and a Run scene
        /// opened with no director.
        /// </summary>
        public static bool CanOpen()
        {
            RunDirector director = RunDirector.Instance;
            return director != null && director.IsRunLive && UiNavigation.MayLayer(UiContext.Paused);
        }

        // ================================================================== state

        public void SetPaused(bool value)
        {
            if (_paused == value)
            {
                return;
            }

            if (value && !CanOpen())
            {
                return;
            }

            _paused = value;
            _pauseRoot.SetActive(value);

            if (value)
            {
                _focusBefore = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                UiFrozenFrame.Hold(FrozenOwner);   // WP-068 slice 4: frozen-frame blur under the modal (C8)
                UiNavigation.EnterLayer(UiContext.Paused);

                RefreshShake();
                RefreshCamera();
                RefreshRun();
                LayoutPanel();   // TextScale may have changed in Settings since the last pause
                _blurReleased = false;
                UiSound.Play(UiSoundKind.Confirm);
                Focus(_resumeButton);
            }
            else
            {
                CloseConfirm();
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                UiFrozenFrame.Release(FrozenOwner);   // idempotent: a camera choice may have released it already
                UiNavigation.ExitOverlay(UiContext.Paused);
                UiSound.Play(UiSoundKind.Back);
                RestoreFocus();
            }
        }

        /// <summary>
        /// Hands focus back to the control that had it before the pause, when it is
        /// still on screen and usable - a keyboard player on the upgrade screen must not
        /// come back from a pause with no focus ring anywhere.
        /// </summary>
        private void RestoreFocus()
        {
            GameObject target = _focusBefore;
            _focusBefore = null;

            if (target == null || !target.activeInHierarchy || EventSystem.current == null)
            {
                return;
            }

            // No IsInteractable() check: the upgrade screen re-enables its CanvasGroup on
            // its next Update, one frame after this, and the selection survives that.
            EventSystem.current.SetSelectedGameObject(target);
        }

        private void Resume()
        {
            SetPaused(false);
        }

        /// <summary>
        /// WP-070 §5.1: the choice is saved and requested at once; the blend runs on unscaled time behind the pause, so the
        /// frozen-frame blur lets go (the scrim stays) and the player watches the view change.
        /// </summary>
        public void ChooseCamera(CameraMode mode)
        {
            UiCameraSettingsBridge.RequestMode(mode);
            if (!_blurReleased)
            {
                _blurReleased = true;
                UiFrozenFrame.Release(FrozenOwner);
            }

            RefreshCamera();
        }

        /// <summary>The camera caption for segment <paramref name="index"/>, or for the current mode when it is below 0.</summary>
        private void ShowCameraCaption(int index)
        {
            if (_cameraCaption == null)
            {
                return;
            }

            int mode = index >= 0 ? index : (int)CameraModeService.Target;
            _cameraCaption.text = CameraModeCaption(Mathf.Clamp(mode, 0, CameraModeCaptions.Length - 1));
        }

        private void RefreshCamera()
        {
            int current = (int)CameraModeService.Target;
            for (int i = 0; i < _cameraButtons.Length; i++)
            {
                if (_cameraButtons[i] != null)
                {
                    _cameraButtons[i].SetSelectedChoice(i == current);
                }
            }

            if (_cameraCaption != null)
            {
                _cameraCaption.text = CameraModeCaption(Mathf.Clamp(current, 0, CameraModeCaptions.Length - 1));
            }
        }

        private void OpenConfirm()
        {
            UiEvents.Raise(UiEvent.DialogOpened, "AbortRun");
            _confirming = true;
            _confirmRoot.SetActive(true);

            // Modal: the panel underneath stops taking focus and clicks, so an arrow key
            // pressed on the dialog cannot walk the focus onto RESUME behind the scrim.
            _pauseGroup.interactable = false;
            _pauseGroup.blocksRaycasts = false;

            Focus(_cancelButton);
        }

        private void CloseConfirm()
        {
            UiEvents.Raise(UiEvent.DialogClosed, "AbortRun");
            if (!_confirming)
            {
                return;
            }

            _confirming = false;
            _confirmRoot.SetActive(false);
            _pauseGroup.interactable = true;
            _pauseGroup.blocksRaycasts = true;
            UiSound.Play(UiSoundKind.Back);
            Focus(_resumeButton);
        }

        private void ConfirmQuit()
        {
            _confirming = false;
            _paused = false;
            UiFrozenFrame.Release(FrozenOwner);   // WP-068 hotfix: END RUN left the blur's hold (and its overlay) on the next scene
            UiNavigation.ExitOverlay(UiContext.Paused);

            // ReleaseSimulation inside the flow drops every hold, so the main menu can
            // never open at timeScale 0.
            PrototypeUiFlow.AbortRunToMainMenu();
        }

        private static void Focus(Selectable selectable)
        {
            if (selectable != null && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }

        // ================================================================= building

        private void BuildPausePanel()
        {
            RectTransform root = UiFactory.NewRect("PauseRoot", ScreenRect);
            UiFactory.Fill(root);
            _pauseRoot = root.gameObject;
            _pauseGroup = UiFactory.Group(_pauseRoot);

            Image scrim = UiFactory.NewImage("Scrim", root, UiColor.ScrimOverlay);
            UiFactory.Fill((RectTransform)scrim.transform);
            scrim.raycastTarget = true;

            _panel = UiFactory.FramedCard("Panel", root, UiColor.BorderStrong, UiColor.SurfacePanel, UiFrame.Card);
            _panel.anchorMin = new Vector2(0.5f, 0.5f);
            _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = UiModal.SizeOf(UiModalSize.XL);
            _panel.anchoredPosition = Vector2.zero;

            RectTransform host = UiFactory.CardContent(_panel, UiSpace.XXXL);

            _title = UiFactory.Label("Title", host, UiStrings.Get("pause.title", "PAUSED"), UiType.H1, UiColor.TextPrimary);
            _caption = UiFactory.Label(
                "Caption", host,
                UiStrings.Get("pause.caption", "The whole simulation is stopped: enemies, projectiles, timers and the round clock."),
                UiType.Body, UiColor.TextSecondary);

            BuildRunStrip(host);

            _divider = (RectTransform)UiFactory.Divider("D1", host).transform;

            BuildCameraRow(host);

            _shakeLabel = UiFactory.Label(
                "ShakeLabel", host, UiStrings.Get("pause.shake", "CAMERA SHAKE INTENSITY"), UiType.Label, UiColor.TextMuted);
            _shakeValue = UiFactory.Numeric(
                "ShakeValue", host, "1.00", UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.MiddleRight);
            _shakeSlider = UiFactory.NewSlider(
                "ShakeSlider", host, 0f, ShakeMax, PrototypeCameraRig.GetShakeIntensity());
            _shakeSlider.onValueChanged.AddListener(OnShakeChanged);
            _shakeCaption = UiFactory.Label(
                "ShakeCaption", host,
                UiStrings.Get("pause.shake.caption", "0 disables shake completely; 1 is the default. Shake never moves your aim at any setting."),
                UiType.Caption, UiColor.TextMuted);

            // Actions are docked to the foot of the card, not to the cursor, so the
            // panel keeps the same shape whatever the caption above wraps to.
            _resumeButton = UiFactory.Button("Resume", host, UiStrings.Get("pause.resume", "RESUME"), UiButtonVariant.Primary, Resume);
            UiInput.Prompt(_resumeButton, UiPromptAction.Pause, true); // the same key that opened it closes it
            RectTransform resumeRect = (RectTransform)_resumeButton.transform;
            resumeRect.anchorMin = new Vector2(0f, 0f);
            resumeRect.anchorMax = new Vector2(0.5f, 0f);
            resumeRect.pivot = new Vector2(0.5f, 0f);
            resumeRect.offsetMin = new Vector2(0f, 0f);
            resumeRect.offsetMax = new Vector2(-(int)UiSpace.S, UiTokens.TargetPreferred);

            UiButton quit = UiFactory.Button(
                "MainMenu", host, UiStrings.Get("common.main_menu", "MAIN MENU"), UiButtonVariant.Secondary, OpenConfirm);
            RectTransform quitRect = (RectTransform)quit.transform;
            quitRect.anchorMin = new Vector2(0.5f, 0f);
            quitRect.anchorMax = new Vector2(1f, 0f);
            quitRect.pivot = new Vector2(0.5f, 0f);
            quitRect.offsetMin = new Vector2((int)UiSpace.S, 0f);
            quitRect.offsetMax = new Vector2(0f, UiTokens.TargetPreferred);

            LayoutPanel();
        }

        /// <summary>
        /// Direction E25 · "run so far" (GD P15): round, time, kills, gold and upgrades bought, then the build line - read
        /// only, from the producers the HUD and Results already quote (RunEvents.Current, the wallet, RunBuildState), so the
        /// three screens can never disagree. Five value-over-label cells in one row.
        /// </summary>
        private void BuildRunStrip(RectTransform host)
        {
            _runLabel = UiFactory.Label("RunLabel", host, UiStrings.Get("pause.run.title", "RUN SO FAR"), UiType.Label, UiColor.TextMuted);
            _runRow = UiFactory.NewRect("RunSoFar", host);
            string[] keys = { "pause.run.round", "pause.run.time", "pause.run.kills", "pause.run.gold", "pause.run.upgrades" };
            string[] words = { "ROUND", "TIME", "KILLS", "GOLD", "UPGRADES" };
            for (int i = 0; i < RunCells; i++)
            {
                RectTransform cell = UiFactory.NewRect("Run." + words[i], _runRow);
                cell.anchorMin = new Vector2(i / (float)RunCells, 0f);
                cell.anchorMax = new Vector2((i + 1) / (float)RunCells, 1f);
                cell.offsetMin = Vector2.zero;
                cell.offsetMax = Vector2.zero;
                _runValues[i] = UiFactory.Numeric("Value", cell, "0", UiType.NumericM, i == 3 ? UiColor.StateGold : UiColor.TextPrimary, TextAnchor.UpperLeft, false);
                _runWords[i] = UiFactory.Label("Word", cell, UiStrings.Get(keys[i], words[i]), UiType.Caption, UiColor.TextMuted, TextAnchor.UpperLeft);
                _runWords[i].enableWordWrapping = false;
            }

            _runBuild = UiFactory.Label("RunBuild", host, string.Empty, UiType.Caption, UiColor.TextSecondary);
        }

        /// <summary>
        /// AD R1-A SHOULD-3: the stack is measured top-down from its texts (their wrapped heights at the card's width, the
        /// longest camera caption reserved), so the slack collects in ONE band above RESUME / MAIN MENU instead of three.
        /// The card is the kit's XL modal; when TextScale needs more it grows, up to the screen less a Section margin.
        /// </summary>
        private void LayoutPanel()
        {
            Vector2 xl = UiModal.SizeOf(UiModalSize.XL);
            float inner = xl.x - 2f * (int)UiSpace.XXXL;
            float y = 0f;
            y = Place(_title.rectTransform, y, Wrapped(_title, inner));
            y += (int)UiSpace.S;
            y = Place(_caption.rectTransform, y, Wrapped(_caption, inner));
            y += (int)UiSpace.XL;

            y = Place(_runLabel.rectTransform, y, Line(_runLabel));
            y += (int)UiSpace.S;
            float value = Line(_runValues[0]);
            float word = Line(_runWords[0]);
            for (int i = 0; i < RunCells; i++)
            {
                Place(_runValues[i].rectTransform, 0f, value);
                Place(_runWords[i].rectTransform, value + 2f, word);
            }

            y = Place(_runRow, y, value + 2f + word);
            y += (int)UiSpace.XS;
            y = Place(_runBuild.rectTransform, y, Line(_runBuild));
            y += (int)UiSpace.XL;

            y = Place(_divider, y, UiTokens.StrokeHairline);
            y += (int)UiSpace.XL;

            y = Place(_cameraLabel.rectTransform, y, Line(_cameraLabel));
            y += (int)UiSpace.S;
            y = Place(_cameraRow, y, UiTokens.TargetPreferred);
            y += (int)UiSpace.S;
            float captionH = 0f;
            string current = _cameraCaption.text;
            for (int i = 0; i < CameraModeCaptions.Length; i++)
            {
                captionH = Mathf.Max(captionH, Mathf.Ceil(_cameraCaption.GetPreferredValues(CameraModeCaption(i), inner, 0f).y));
            }

            _cameraCaption.text = current;
            y = Place(_cameraCaption.rectTransform, y, captionH);
            y += (int)UiSpace.XL;

            float shakeLine = Line(_shakeLabel);
            Place(_shakeLabel.rectTransform, y, shakeLine);
            RectTransform shakeValueRect = _shakeValue.rectTransform;
            shakeValueRect.anchorMin = new Vector2(1f, 1f);
            shakeValueRect.anchorMax = new Vector2(1f, 1f);
            shakeValueRect.pivot = new Vector2(1f, 1f);
            shakeValueRect.sizeDelta = new Vector2(120f, shakeLine);
            shakeValueRect.anchoredPosition = new Vector2(0f, -y);
            y += shakeLine + (int)UiSpace.S;
            y = Place((RectTransform)_shakeSlider.transform, y, UiTokens.TargetCompact);
            y += (int)UiSpace.S;
            y = Place(_shakeCaption.rectTransform, y, Wrapped(_shakeCaption, inner));

            // One slack band, above the foot: at least XL between the last caption and the buttons.
            float needed = y + (int)UiSpace.XL + UiTokens.TargetPreferred + 2f * (int)UiSpace.XXXL;
            float room = ScreenRect.rect.height - 2f * (int)UiSpace.Section;
            float height = Mathf.Max(xl.y, needed);
            if (room > 0f)
            {
                height = Mathf.Min(height, Mathf.Max(xl.y, room));
            }

            _panel.sizeDelta = new Vector2(xl.x, height);
        }

        /// <summary>The read-only strip, from the live producers; written on open, never per frame.</summary>
        private void RefreshRun()
        {
            RunDirector director = RunDirector.Instance;
            RunHudState state = RunEvents.Current;
            int seconds = Mathf.Max(0, Mathf.FloorToInt(state.RunTime));
            _runValues[0].text = (director != null ? director.Round : state.Round).ToString();
            _runValues[1].text = (seconds / 60).ToString() + ":" + (seconds % 60).ToString("00");
            _runValues[2].text = state.RunKills.ToString();
            _runValues[3].text = (director != null && director.Wallet != null ? director.Wallet.Balance : state.Gold).ToString();
            _runValues[4].text = (director != null ? director.Build.Snapshot.PurchaseCount : 0).ToString();

            string build = director != null ? director.Build.DescribeBuild() : null;
            string weapon = DD.Prototype.Weapons.WeaponEvents.Classic.DisplayName;
            string upgrades = string.IsNullOrEmpty(build) || build == "Stock" ? UiStrings.Get("pause.run.build_none", "no upgrades") : build;
            _runBuild.text = string.IsNullOrEmpty(weapon) ? upgrades : UiStrings.Upper(weapon) + "  ·  " + upgrades;
        }

        private static float Place(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -(top + height));
            rect.offsetMax = new Vector2(0f, -top);
            return top + height;
        }

        private static float Line(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag").y);
        }

        private static float Wrapped(TMP_Text text, float width)
        {
            return Mathf.Max(Line(text), Mathf.Ceil(text.GetPreferredValues(text.text, width, 0f).y));
        }

        /// <summary>M4-R1 N2: CAMERA - three segments side by side (Left / Right walk them), the current one Selected, and a caption.</summary>
        private void BuildCameraRow(RectTransform host)
        {
            _cameraLabel = UiFactory.Label("CameraLabel", host, UiStrings.Get("camera.title", "CAMERA"), UiType.Label, UiColor.TextMuted);
            _cameraRow = UiFactory.NewRect("CameraModes", host);
            float gap = (int)UiSpace.S;
            for (int i = 0; i < _cameraButtons.Length; i++)
            {
                var mode = (CameraMode)i;
                // Nav: the kit's "current choice" look - raised fill, cream outline and tick; the other segments read quieter.
                UiButton button = UiFactory.Button("Camera." + mode, _cameraRow, CameraModeName(i), UiButtonVariant.Nav, () => ChooseCamera(mode));
                button.LabelText.alignment = TextAlignmentOptions.Center;
                button.SetSegment(true);   // AD R1-A SHOULD-1: no tick in a segmented row
                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(i / 3f, 0f);
                rect.anchorMax = new Vector2((i + 1) / 3f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = new Vector2(i == 0 ? 0f : gap * 0.5f, 0f);
                rect.offsetMax = new Vector2(i == 2 ? 0f : -gap * 0.5f, 0f);
                _cameraButtons[i] = button;
                // Slice 9 · E3 T2: the caption describes the focused segment; leaving the row gives it back to the current mode.
                int index = i;
                button.gameObject.AddComponent<UiFocusCallback>().Configure(() => ShowCameraCaption(index), () => ShowCameraCaption(-1));
            }

            _cameraCaption = UiFactory.Label("CameraCaption", host, CameraModeCaption(0), UiType.Caption, UiColor.TextMuted);
        }

        private void BuildConfirmDialog()
        {
            RectTransform root = UiFactory.NewRect("ConfirmRoot", ScreenRect);
            UiFactory.Fill(root);
            _confirmRoot = root.gameObject;

            // Its own canvas layer so it is unambiguously above the pause panel and
            // takes every click, including the ones aimed at the panel behind it.
            Canvas layer = root.gameObject.AddComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = UiTokens.SortingDialog;
            root.gameObject.AddComponent<GraphicRaycaster>();

            Image scrim = UiFactory.NewImage("Scrim", root, UiColor.SurfaceInset);
            UiFactory.Fill((RectTransform)scrim.transform);
            SetAlpha(scrim, UiTokens.OpacityScrimUnderDialog);
            scrim.raycastTarget = true;

            RectTransform card = UiFactory.FramedCard("Dialog", root, UiColor.AccentPrimary, UiColor.SurfacePanel, UiFrame.Card);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(DialogWidth, DialogHeight);
            card.anchoredPosition = Vector2.zero;

            RectTransform host = UiFactory.CardContent(card, UiSpace.XXXL);
            var stack = new UiStack(host);

            TMP_Text title = UiFactory.Label("Title", host, UiStrings.Get("pause.confirm.title", "END THIS RUN?"), UiType.H2, UiColor.TextPrimary);
            stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H2));

            stack.Gap(UiSpace.M);

            TMP_Text body = UiFactory.Label(
                "Body", host,
                UiStrings.Get("pause.confirm.body", "Leaving now ends the run. The current round, all unspent gold and every weapon upgrade " +
                "you bought are lost. Profile level, statistics and achievements you have already earned " +
                "are kept."),
                UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)body.transform, UiFactory.LineHeight(UiType.Body) * 4f);

            _cancelButton = UiFactory.Button(
                "Cancel", host, UiStrings.Get("pause.confirm.keep", "KEEP PLAYING"), UiButtonVariant.Secondary, CloseConfirm);
            RectTransform cancelRect = (RectTransform)_cancelButton.transform;
            cancelRect.anchorMin = new Vector2(0f, 0f);
            cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.offsetMin = new Vector2(0f, 0f);
            cancelRect.offsetMax = new Vector2(-(int)UiSpace.S, UiTokens.TargetPreferred);

            UiButton confirm = UiFactory.Button(
                "Confirm", host, UiStrings.Get("pause.confirm.end", "END RUN"), UiButtonVariant.Danger, ConfirmQuit);
            RectTransform confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = new Vector2(0.5f, 0f);
            confirmRect.anchorMax = new Vector2(1f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.offsetMin = new Vector2((int)UiSpace.S, 0f);
            confirmRect.offsetMax = new Vector2(0f, UiTokens.TargetPreferred);
        }

        // ==================================================================== shake

        private void OnShakeChanged(float value)
        {
            PrototypeCameraRig.SetShakeIntensity(value);
            _shakeValue.text = value.ToString("0.00");
        }

        /// <summary>
        /// Re-reads the live value each time the screen opens, because the F1 panel
        /// exposes the same setting and the two must never disagree.
        /// </summary>
        private void RefreshShake()
        {
            float current = PrototypeCameraRig.GetShakeIntensity();

            _shakeSlider.SetValueWithoutNotify(Mathf.Clamp(current, 0f, ShakeMax));
            _shakeValue.text = _shakeSlider.value.ToString("0.00");

            // Error state: no camera rig and no config to write to. The control says so
            // rather than pretending to work.
            bool available = PrototypeCameraRig.Active != null ||
                             DD.Prototype.Core.Config.PrototypeConfig.Active != null;

            _shakeSlider.interactable = available;
            if (!available)
            {
                _shakeValue.text = "—";
            }
        }
    }
}
