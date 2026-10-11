using System;
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
    ///
    /// WP-098B (AP-028 pause / pause-confirm; brush kit KILAVUZ §3 + §5): the panel is a frameless
    /// <see cref="UiBrushPane"/> on the Window scrim - its left edge trace, the Menu.Pause title with its brush,
    /// RESUME, the CAMERA segments (all three modes visible, C-00 07.10; the one C8 under the current mode), the
    /// shake slider and END RUN in the left column, the run so far in the right. Trace budget: title 1 + C8 1 +
    /// edge 1 = 3. END THIS RUN? is the kit's Confirm (Confirm scrim; the destructive action says itself in a word
    /// and an icon, never a colour; RESUME focused). Cancel (pad B / Esc) closes the confirmation, else
    /// resumes; Esc is both Pause and Cancel - one press is one step back. The chain is explicit, RESUME focused
    /// first; the kit opens the panel inside 0.35 s (Reduced Motion: at once).
    ///
    /// WP-105 (V-42, NICE-4): one action, one name. The foot button ends the run, so it says so - END RUN with the
    /// Menu.EndRun glyph, the confirmation's own words (it used to read MAIN MENU and hide the loss). The confirmation's
    /// safe action is RESUME (pause.resume), the panel's first button: it resumes play, as the word says (it read KEEP
    /// PLAYING and only went back to the panel). Cancel stays one step back - the confirmation only.
    /// </summary>
    public sealed class PauseScreen : UiScreen
    {
        /// <summary>
        /// WP-098B · K4 (C-00 07.10, WP-095 feel contract §6): the player's shake scale is 0-1, default 1 (above 1 already
        /// hit the 0.22 m aggregate cap). S-02 owns the range but main has no camera-side constant yet
        /// (PrototypeCameraRig.SetShakeIntensity and the F1 slider still clamp 0-2), so this is its one place until then.
        /// </summary>
        public const float ShakeMax = 1f;
        private const float ShakeStep = 0.1f;
        private const string FrozenOwner = "pause";

        /// <summary>The END THIS RUN? root's name since WP-005 (tests and captures find it by it).</summary>
        public const string ConfirmName = "ConfirmRoot";

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

        private UiBrushPane _pane;
        private CanvasGroup _contentGroup;
        private UiBrushTitle _title;
        private UiModal _confirm;

        private UiButton _resumeButton;
        private UiButton _endRunButton;

        // WP-105 (§C2, C-00 11.10 decision): SETTINGS beside RESUME opens a settings page inside the pause - audio, text
        // scale, flashes, motion; the kit's rows bound to UiSettings (no key binding: that stays in the main menu).
        private UiButton _settingsButton;
        private RectTransform _settingsPage;
        private UiIconButton _settingsBack;
        private UiScrollView _settingsScroll;
        private UiSlider _masterVolume;
        private UiSlider _musicVolume;
        private UiSlider _effectsVolume;
        private UiSlider _uiVolume;
        private UiSlider _textScale;
        private UiSwitch _reduceFlashes;
        private UiSwitch _reducedMotion;
        private bool _settingsOpen;
        private float _leftWidth;   // the left column's width and the rows' top, from the last LayoutPanel
        private float _rowsTop;
        private static readonly float[] TextScaleStops = { 1f, 1.1f, 1.25f };   // the menu's E8 stops: 100 / 110 / 125

        private UiSlider _shakeSlider;
        private bool _shakeAvailable = true;

        // M4-R1 N2 (WP-070 §5.1): the segmented CAMERA selector and the chosen mode's one-line description.
        // R1 content pack (planning/content/r1-strings.json): camera.mode.* and camera.mode.*.desc. The first-person line was
        // rewritten in the pack for the rear-edge arc (AD R1-A SHOULD-2): threats behind are marked, not unseen.
        public static readonly string[] CameraModeNames = { "TOP-DOWN", "THIRD-PERSON", "FIRST-PERSON" };   // fallbacks (WP-068 TR)
        private static readonly string[] CameraModeKeys = { "camera.mode.top_down", "camera.mode.third_person", "camera.mode.first_person" };
        private static readonly string[] CameraModeCaptionKeys = { "camera.mode.top_down.desc", "camera.mode.third_person.desc", "camera.mode.first_person.desc" };
        private static readonly string[] CameraObjectNames = { "Camera." + CameraMode.TopDown, "Camera." + CameraMode.ThirdPerson, "Camera." + CameraMode.FirstPerson };

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

        private UiBrushChoiceGroup _camera;
        private TMP_Text _cameraLabel;
        private TMP_Text _cameraCaption;

        // R1-C / direction E25: the run so far (GD P15), now AP-028's right column - label left, value right, one row each.
        private const int RunCells = 5;
        private static readonly string[] RunKeys = { "pause.run.round", "pause.run.time", "pause.run.kills", "pause.run.gold", "pause.run.upgrades" };
        private static readonly string[] RunWords = { "ROUND", "TIME", "KILLS", "GOLD", "UPGRADES" };
        private readonly TMP_Text[] _runValues = new TMP_Text[RunCells];
        private readonly TMP_Text[] _runWords = new TMP_Text[RunCells];
        private TMP_Text _runBuild;
        private bool _blurReleased;

        private bool _paused;

        /// <summary>Whatever held focus when the pause opened - a shop card, CONTINUE - to hand it back on close.</summary>
        private GameObject _focusBefore;

        // WP-098B · AD M1: the C7 brackets ride the content's PaneIn rise (see RideBrackets). The content's rest is read
        // before the open; the ride ends when the content is back at it, or - a backstop - after the rise's own time.
        private static readonly float BracketRideCap =
            UiBrushTokens.ContentDelay + UiMotionTokens.Seconds(UiDuration.Base) + UiMotionTokens.Seconds(UiDuration.Fast);
        private bool _bracketsRide;
        private Vector2 _contentRest;
        private float _rideUntil;

        public bool IsPaused
        {
            get { return _paused; }
        }

        /// <summary>
        /// WP-105 (V-41, for the HUD - section D): true while a pause is up. It is the navigation model's Paused overlay
        /// (entered on open, left on close and on END RUN), so a screen can read it - and follow it through
        /// <see cref="UiNavigation.Changed"/> - without finding this one. No state of its own.
        /// </summary>
        public static bool IsOpen
        {
            get { return UiNavigation.IsPaused; }
        }

        /// <summary>The CAMERA segments (tests, captures).</summary>
        public UiButton CameraButton(CameraMode mode)
        {
            return _camera[(int)mode];
        }

        /// <summary>The CAMERA segment row: one C8 under the current mode (tests).</summary>
        public UiBrushChoiceGroup CameraSegments
        {
            get { return _camera; }
        }

        public string CameraCaption
        {
            get { return _cameraCaption != null ? _cameraCaption.text : string.Empty; }
        }

        /// <summary>WP-098B: the frameless pane - its scrim level, its traces, its open state (tests).</summary>
        public UiBrushPane Pane
        {
            get { return _pane; }
        }

        /// <summary>The pane's rect: every word of the panel sits inside it (tests, captures).</summary>
        public RectTransform PanelRect
        {
            get { return _pane.Root; }
        }

        public string RunValue(int cell)
        {
            return _runValues[cell].text;
        }

        public string RunBuild
        {
            get { return _runBuild.text; }
        }

        /// <summary>The left column's last row above the foot (the shake slider): the slack band is under it.</summary>
        public RectTransform StackEnd
        {
            get { return (RectTransform)_shakeSlider.transform; }
        }

        public RectTransform ResumeRect
        {
            get { return (RectTransform)_resumeButton.transform; }
        }

        /// <summary>WP-098B: RESUME, the shake slider, END RUN and the END THIS RUN? confirmation (tests, captures).</summary>
        public UiButton ResumeButton
        {
            get { return _resumeButton; }
        }

        public UiSlider ShakeSlider
        {
            get { return _shakeSlider; }
        }

        /// <summary>WP-105 (V-42): the foot button - END RUN, it opens END THIS RUN? (object "EndRun").</summary>
        public UiButton EndRunButton
        {
            get { return _endRunButton; }
        }

        /// <summary>The same END RUN button under its pre-WP-105 name (the capture agents and older suites ask for it).</summary>
        public UiButton MainMenuButton
        {
            get { return _endRunButton; }
        }

        /// <summary>WP-105 (§C2): SETTINGS - the C11 beside RESUME (object "Settings").</summary>
        public UiButton SettingsButton
        {
            get { return _settingsButton; }
        }

        /// <summary>WP-105 (§C2): the settings page (null until first opened) and whether it is the page on show (tests).</summary>
        public RectTransform SettingsPage
        {
            get { return _settingsPage; }
        }

        public bool IsSettingsOpen
        {
            get { return _settingsOpen; }
        }

        /// <summary>WP-105 (§C2): the page's C10 Back (null until first opened).</summary>
        public UiButton SettingsBackButton
        {
            get { return _settingsBack != null ? _settingsBack.Button : null; }
        }

        public UiModal Confirmation
        {
            get { return _confirm; }
        }

        public bool IsConfirming
        {
            get { return _confirm != null && _confirm.IsOpen; }
        }

        /// <summary>The panel is on screen - true through the exit fade, false once it has landed and before the first open (tests).</summary>
        public bool IsShowing
        {
            get { return _pane != null && _pane.Root.gameObject.activeSelf; }
        }

        /// <summary>The pane's content group: the kit's PaneIn fades it in (tests).</summary>
        public CanvasGroup PanelGroup
        {
            get { return _contentGroup; }
        }

        // ============================================================ END THIS RUN? (shared with the QUIT confirm)

        /// <summary>
        /// WP-098B (AP-028 pause-confirm, C31): END THIS RUN? - the loss and what is kept. WP-105 (NICE-4): the safe action
        /// is RESUME (pause.resume) - the panel's own word for going back to play, not a second name (KEEP PLAYING).
        /// </summary>
        public static UiBrushConfirm EndRunSpec
        {
            get
            {
                return new UiBrushConfirm
                {
                    TitleKey = "pause.confirm.title",
                    TitleFallback = "END THIS RUN?",
                    BodyKey = "pause.confirm.body",
                    BodyFallback = "Leaving now ends the run. The current round, all unspent gold and every weapon upgrade " +
                                   "you bought are lost. Profile level, statistics and achievements you have already earned " +
                                   "are kept.",
                    ConfirmKey = "pause.confirm.end",
                    ConfirmFallback = "END RUN",
                    ConfirmIcon = "Menu.EndRun",
                    CancelKey = "pause.resume",
                    CancelFallback = "RESUME",
                    CancelIcon = "Menu.Resume",
                    Destructive = true,
                };
            }
        }

        /// <summary>
        /// The kit's Confirm for END THIS RUN? on <paramref name="screenRoot"/>, closed: <paramref name="onEnd"/> is END RUN,
        /// <paramref name="onKeep"/> RESUME (and the modal's own Cancel). Public so the EditMode suite builds the very dialog
        /// the screen does.
        /// </summary>
        public static UiModal CreateEndRunConfirm(RectTransform screenRoot, Action onEnd, Action onKeep)
        {
            UiModal confirm = UiBrushKit.Confirm(ConfirmName, screenRoot, EndRunSpec, onEnd, onKeep);
            KeepConfirmNames(confirm);
            RaiseToDialogLayer(confirm);
            return confirm;
        }

        /// <summary>The action names the PlayMode suites and captures have found since WP-005: the safe "Cancel", the confirming "Confirm".</summary>
        public static void KeepConfirmNames(UiModal confirm)
        {
            if (confirm.Secondary != null)
            {
                confirm.Secondary.gameObject.name = "Cancel";
            }

            if (confirm.Primary != null)
            {
                confirm.Primary.gameObject.name = "Confirm";
            }
        }

        /// <summary>
        /// A confirmation is its own canvas layer at the dialog order: unambiguously above the screen it covers (a pane
        /// or a sub-canvas of that screen cannot draw over it), and its scrim takes every click aimed past it.
        ///
        /// WP-098B: Unity drops a Canvas.overrideSorting set on a canvas that is not active in the hierarchy (by design),
        /// and the kit builds a confirmation closed (UiModal.Confirm turns its root off) - so the layer used to be asked for
        /// and silently never taken. The root is switched on for the two writes and back off in the same call: nothing is
        /// drawn or raycast in between. Call it with the confirmation under the screen's canvas (a root canvas has no
        /// sorting to override).
        /// </summary>
        public static void RaiseToDialogLayer(UiModal confirm)
        {
            GameObject root = confirm.gameObject;
            bool wasActive = root.activeSelf;
            if (!wasActive)
            {
                root.SetActive(true);
            }

            Canvas layer = root.GetComponent<Canvas>();
            if (layer == null)
            {
                layer = root.AddComponent<Canvas>();
                root.AddComponent<GraphicRaycaster>();
            }

            layer.overrideSorting = true;
            layer.sortingOrder = UiTokens.SortingDialog;

            if (!wasActive)
            {
                root.SetActive(false);
            }
        }

        protected override void Build()
        {
            BuildPausePanel();
            _confirm = CreateEndRunConfirm(ScreenRect, ConfirmQuit, ResumeFromConfirm);

            // WP-098B (T3, UiTextScaleTests at 1.25): the screen is built closed. The kit keeps a pane's root active while the
            // caller builds (TMP measures only active text) and turns it off only from a Close - which a pane that has never
            // been opened never gets. So the closed pause sat under the whole run with its words active, unplaced (the rows
            // are laid out on open) and zero-sized: every one of them "truncated", and all of it drawn at alpha 0 each frame.
            // Building is over: the root goes off here - the end state the kit's own Close leaves (root and C1 off) - and
            // Open turns it back on. WP-005 did the same with its _pauseRoot.
            _pane.Root.gameObject.SetActive(false);
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
            // WP-098B: Cancel (pad B / Esc) is read only while the screen is up. Esc is ALSO the Pause key, so the two are
            // one press: one step back (the confirmation, else the pause), never a close-and-reopen in the same frame.
            if (_bracketsRide)
            {
                RideBrackets();
            }

            bool pausePressed = InputSourceRegistry.Current.PausePressed;
            bool cancelPressed = _paused && UiInput.CancelPressed();
            if (!pausePressed && !cancelPressed)
            {
                if (_paused)
                {
                    UiInput.ReclaimFocus(IsConfirming ? _confirm.Secondary : _settingsOpen ? (Selectable)_masterVolume : _resumeButton);
                }

                return;
            }

            if (IsConfirming)
            {
                // WP-105: one step back - the confirmation only, with the Back cue. Not the modal's HandleCancel: that is its
                // safe action, which now resumes play (RESUME), and a Cancel must never take two steps.
                CloseConfirm();
                UiSound.Play(UiSoundKind.Back);
                return;
            }

            if (_paused && _settingsOpen)
            {
                CloseSettings();   // WP-105 (§C2): one step back - the settings page, to the pause's own page
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

            if (value)
            {
                _focusBefore = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                UiFrozenFrame.Hold(FrozenOwner);   // WP-068 slice 4: frozen-frame blur under the modal (C8)
                UiNavigation.EnterLayer(UiContext.Paused);

                // The kit pane: input at once, scrim / title brush / edge / C8 ink in, content PaneIn (Reduced Motion: at
                // rest). Its PaneOpened is the open sound. The rows are written and laid out in the same frame, before
                // anything is drawn - measured live, so a TextScale changed in Settings is honoured.
                //
                // WP-098B · AD M1: RESUME is focused AFTER the layout, still in this frame. The C7 brackets measure their
                // control once, on its select (no polling); the pane used to be opened with RESUME's focus before
                // LayoutPanel had placed it, so on the first pause the brackets were measured at RESUME's build place - the
                // content's centre (x 840-1080, y 512-585 at 1920×1080, under the shake value) - and stayed there.
                _contentRest = _pane.Content.anchoredPosition;
                _pane.Open(null);
                ShowSettingsPage(false);   // WP-105: every pause opens on its own page (a settings page left open stays behind)
                RefreshShake();
                RefreshRun();
                LayoutPanel();
                RefreshCamera();
                LinkNavigation();
                FocusResume();
                _bracketsRide = true;
                _rideUntil = Time.unscaledTime + BracketRideCap;
                _blurReleased = false;
            }
            else
            {
                CloseConfirm();
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                UiFrozenFrame.Release(FrozenOwner);   // idempotent: a camera choice may have released it already
                UiNavigation.ExitOverlay(UiContext.Paused);
                UiSound.Play(UiSoundKind.Back);
                _bracketsRide = false;
                _pane.Close();   // input off at once; the screen under it is live while the panel fades
                RestoreFocus();
            }
        }

        /// <summary>
        /// WP-098B: the explicit pad / keyboard chain (no automatic navigation), top to bottom as drawn: RESUME ↓ the
        /// current CAMERA segment (Left / Right walk the three, the kit's wiring) ↓ the shake slider (Left / Right move its
        /// value) ↓ END RUN; Up walks back; no wrap from the foot to the top, so a stray Up on RESUME never lands on END
        /// RUN. A greyed slider (no camera rig) is stepped over. Re-linked on every open: the slider's state and the
        /// current camera mode can change between pauses.
        /// </summary>
        private void LinkNavigation()
        {
            Selectable shake = _shakeSlider.interactable ? _shakeSlider : null;
            Selectable camera = _camera[Mathf.Clamp(_camera.Selected, 0, _camera.Count - 1)];
            UiInput.ChainVertical(false, _resumeButton, camera, shake, _endRunButton);
            _camera.LinkVertical(_resumeButton, shake != null ? shake : _endRunButton);
            // WP-105 (§C2): SETTINGS sits beside RESUME - RESUME → SETTINGS, SETTINGS ← RESUME, its ↓ the camera row like RESUME's.
            UiInput.LinkHorizontal(_resumeButton, null, _settingsButton);
            UiInput.LinkHorizontal(_settingsButton, _resumeButton, null);
            UiInput.LinkVertical(_settingsButton, null, camera);
        }

        /// <summary>
        /// WP-098B · AD M1: RESUME takes focus on every open, with its brackets. A close hands focus back only when there
        /// was a control to hand it to; otherwise the EventSystem still names RESUME (inactive under the closed pane), and
        /// SetSelectedGameObject ignores a select of the object it already holds - no OnSelect, so the reopened pause had
        /// RESUME "focused" with no C7 on it. The stale selection is cleared first, so RESUME's select always runs.
        /// </summary>
        private void FocusResume()
        {
            EventSystem events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == _resumeButton.gameObject)
            {
                events.SetSelectedGameObject(null);
            }

            UiInput.Focus(_resumeButton);
        }

        /// <summary>
        /// WP-098B · AD M1: the kit's PaneIn starts the content 8 px low and lets it rise; the brackets, measured once on the
        /// select, would stay 8 px under the focused row. For the rise only (≈ 0.3 s; Reduced Motion: one frame) the brackets
        /// are re-cut onto whatever pane control holds focus - RESUME, or the row a quick ↓ reached - so they rise with it
        /// and land 8 px out on every side (KILAVUZ §1 C7). On the slider that is its row root (C14 "C7 satırda"): the
        /// UiSlider sits on the row, label to value. Kit API only (HideNow + Follow = a cut); nothing runs once landed.
        /// </summary>
        private void RideBrackets()
        {
            EventSystem events = EventSystem.current;
            if (!_paused || IsConfirming || events == null)
            {
                _bracketsRide = false;
                return;
            }

            bool landed = (_pane.Content.anchoredPosition - _contentRest).sqrMagnitude < 0.01f || Time.unscaledTime >= _rideUntil;
            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(_pane.Content))
            {
                UiFocusBrackets.HideNow();
                UiFocusBrackets.Follow((RectTransform)selected.transform);
            }

            if (landed)
            {
                _bracketsRide = false;
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

        /// <summary>A segment was chosen (the kit's onSelect; its C8 is already under it).</summary>
        private void OnCameraSegment(int index)
        {
            ChooseCamera((CameraMode)index);
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

        /// <summary>
        /// The current mode is the group's choice (its C8 moves only when the mode really changed, so an open's ink-in
        /// is not cut) and every segment's selected-choice flag, which the camera suites read.
        /// </summary>
        private void RefreshCamera()
        {
            int current = Mathf.Clamp((int)CameraModeService.Target, 0, _camera.Count - 1);
            if (_camera.Selected != current)
            {
                _camera.Select(current, false);
            }

            for (int i = 0; i < _camera.Count; i++)
            {
                _camera[i].SetSelectedChoice(i == current);
            }

            _cameraCaption.text = CameraModeCaption(current);
        }

        private void OpenConfirm()
        {
            if (!_paused || IsConfirming)
            {
                return;
            }

            // Modal: the panel underneath stops taking focus and clicks, so an arrow key
            // pressed on the dialog cannot walk the focus onto RESUME behind the scrim.
            SetContentInput(false);

            // DialogOpened, RESUME focused (the safe action), focus handed back to END RUN on close.
            _confirm.Open();
        }

        /// <summary>
        /// Closes the confirmation if - and only if - it is open. WP-098B: DialogClosed (the UI sound hook) used to be raised
        /// before this check, so every unpause sent a close for a dialog that was never open.
        /// </summary>
        private void CloseConfirm()
        {
            if (!IsConfirming)
            {
                return;
            }

            _confirm.Close();
            OnConfirmClosed();
        }

        /// <summary>Cancel / the Pause key: the confirmation is gone, the panel takes input again.</summary>
        private void OnConfirmClosed()
        {
            if (_paused && _pane.IsOpen)
            {
                SetContentInput(true);
            }
        }

        /// <summary>
        /// WP-105 (NICE-4): the confirmation's RESUME (the modal has closed itself) - back to play, exactly as the panel's
        /// RESUME: the hold, the blur and the overlay go, the Back cue plays, focus returns to what it was before the pause.
        /// </summary>
        private void ResumeFromConfirm()
        {
            OnConfirmClosed();
            SetPaused(false);
        }

        private void SetContentInput(bool value)
        {
            if (_contentGroup != null)
            {
                _contentGroup.interactable = value;
                _contentGroup.blocksRaycasts = value;
            }
        }

        private void ConfirmQuit()
        {
            _paused = false;
            UiFrozenFrame.Release(FrozenOwner);   // WP-068 hotfix: END RUN left the blur's hold (and its overlay) on the next scene
            UiNavigation.ExitOverlay(UiContext.Paused);

            // ReleaseSimulation inside the flow drops every hold, so the main menu can
            // never open at timeScale 0.
            PrototypeUiFlow.AbortRunToMainMenu();
        }

        // ================================================================= building

        /// <summary>
        /// KILAVUZ §3 / §5: Window scrim (text sits on it directly, no wash), the left edge trace, the Menu.Pause title with
        /// its brush; RESUME, CAMERA (C17 segments), CAMERA SHAKE (C14) and END RUN on the left, the run so far on the
        /// right. The pane fills the screen less a Page margin at the sides and a Section margin above and below; the rows
        /// are placed in <see cref="LayoutPanel"/>.
        /// </summary>
        private void BuildPausePanel()
        {
            _pane = UiBrushKit.Pane(ScreenRect, Content, "PausePanel", UiBrushScrim.Window, UiBrushWash.None);
            RectTransform root = _pane.Root;
            UiFactory.Fill(root);
            root.offsetMin = new Vector2((int)UiSpace.Page, (int)UiSpace.Section);
            root.offsetMax = new Vector2(-(int)UiSpace.Page, -(int)UiSpace.Section);
            _contentGroup = _pane.Content.GetComponent<CanvasGroup>();

            RectTransform host = _pane.Content;
            UiBrushKit.EdgeTrace(_pane);
            _title = UiBrushKit.Title(host, "pause.title", "PAUSED", "Menu.Pause");

            _resumeButton = UiBrushKit.Button(host, "Resume", "pause.resume", "RESUME", "Menu.Resume", Resume);
            // WP-105 (§C2, V-40): the settings of a run - sound, text, flashes, motion - without ending it; the menu's word.
            _settingsButton = UiBrushKit.Button(host, "Settings", "menu.tab.settings", "SETTINGS", "Menu.Settings", OpenSettings);

            _cameraLabel = UiFactory.Label("CameraLabel", host, UiStrings.Get("camera.title", "CAMERA"), UiType.H1, UiColor.TextPrimary);
            _cameraLabel.enableWordWrapping = false;
            int current = Mathf.Clamp((int)CameraModeService.Target, 0, CameraModeKeys.Length - 1);
            _camera = UiBrushKit.Segments(host, "CameraModes", CameraObjectNames, CameraModeKeys, CameraModeNames, current, OnCameraSegment);
            for (int i = 0; i < _camera.Count; i++)
            {
                // Slice 9 · E3 T2: the caption describes the focused segment; leaving the row gives it back to the current mode.
                int index = i;
                _camera[i].gameObject.AddComponent<UiFocusCallback>().Configure(() => ShowCameraCaption(index), () => ShowCameraCaption(-1));
            }

            _cameraCaption = UiFactory.Label("CameraCaption", host, CameraModeCaption(current), UiType.MenuSmall, UiColor.TextSecondary);

            _shakeSlider = UiBrushKit.Slider(
                host, "Setting.CAMERA SHAKE", "pause.shake", "CAMERA SHAKE INTENSITY", 0f, ShakeMax,
                Mathf.Clamp(PrototypeCameraRig.GetShakeIntensity(), 0f, ShakeMax), ShakeStep, FormatShake, OnShakeChanged,
                UiBrushTokens.SegmentWidth * CameraModeKeys.Length);

            // WP-105 (V-42): the button ends the run, so it is called that - the confirmation's own word and glyph (it read
            // MAIN MENU with the home glyph and hid the loss until the dialog).
            _endRunButton = UiBrushKit.Button(host, "EndRun", "pause.confirm.end", "END RUN", "Menu.EndRun", OpenConfirm);

            BuildRunColumn(host);
        }

        /// <summary>
        /// Direction E25 · "run so far" (GD P15), AP-028's right column: round, time, kills, gold and upgrades bought - a
        /// MenuSmall word on the left, its number on the right - then the weapon and build line. Read only, from the
        /// producers the HUD and Results already quote (RunEvents.Current, the wallet, RunBuildState), so the three
        /// screens can never disagree. Gold is the one StateGold number (the gold reserve: money only).
        /// </summary>
        private void BuildRunColumn(RectTransform host)
        {
            for (int i = 0; i < RunCells; i++)
            {
                _runWords[i] = UiFactory.Label("Run." + RunWords[i], host, UiStrings.Get(RunKeys[i], RunWords[i]), UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleLeft);
                _runWords[i].enableWordWrapping = false;
                _runValues[i] = UiFactory.Numeric("Run." + RunWords[i] + ".Value", host, "0", UiType.NumericL, i == 3 ? UiColor.StateGold : UiColor.TextPrimary, TextAnchor.MiddleRight, false);
            }

            _runBuild = UiFactory.Label("RunBuild", host, string.Empty, UiType.MenuBody, UiColor.TextPrimary);
        }

        /// <summary>
        /// WP-098B: the two columns, measured live (open time, never per frame). Left: the title's band, RESUME, CAMERA and
        /// its segments, the caption (the longest of the three reserved, so the slider does not jump as focus walks the
        /// segments), the slider, and END RUN docked to the foot - the slack collects in ONE band above it (AD R1-A
        /// SHOULD-3). Right: the run so far from RESUME's top. Kit controls keep the size the kit gave them; only their
        /// place is set here.
        /// </summary>
        private void LayoutPanel()
        {
            RectTransform content = _pane.Content;
            float width = content.rect.width;
            float height = content.rect.height;
            RectTransform cameraRow = (RectTransform)_camera.transform;
            float left = Mathf.Max(cameraRow.rect.width, Mathf.Min(UiBrushTokens.ListWidth, width * 0.6f));

            float top = _title.Height + (int)UiSpace.XXL;
            var resume = (RectTransform)_resumeButton.transform;
            float y = PlaceAt(resume, 0f, top) + (int)UiSpace.XXL;
            PlaceAt((RectTransform)_settingsButton.transform, resume.rect.width + (int)UiSpace.XL, top);   // WP-105: beside RESUME
            _leftWidth = left;
            _rowsTop = top;
            y = Place(_cameraLabel.rectTransform, 0f, y, left, Line(_cameraLabel)) + (int)UiSpace.S;
            y = PlaceAt(cameraRow, 0f, y) + (int)UiSpace.S;

            float captionH = 0f;
            string shown = _cameraCaption.text;
            for (int i = 0; i < CameraModeCaptions.Length; i++)
            {
                captionH = Mathf.Max(captionH, Mathf.Ceil(_cameraCaption.GetPreferredValues(CameraModeCaption(i), left, 0f).y));
            }

            _cameraCaption.text = shown;
            y = Place(_cameraCaption.rectTransform, 0f, y, left, Mathf.Max(Line(_cameraCaption), captionH)) + (int)UiSpace.XL;
            y = PlaceAt((RectTransform)_shakeSlider.transform, 0f, y);

            var menu = (RectTransform)_endRunButton.transform;
            float foot = Mathf.Max(y + (int)UiSpace.XL, height - menu.rect.height);
            PlaceAt(menu, 0f, foot);

            float x = left + (int)UiSpace.Section;
            float column = Mathf.Max(0f, width - x);
            float row = UiBrushTokens.RowHeight(UiType.NumericL, UiBrushTokens.RowStep);
            for (int i = 0; i < RunCells; i++)
            {
                float rowTop = top + i * row;
                Place(_runWords[i].rectTransform, x, rowTop, column, row);
                Place(_runValues[i].rectTransform, x, rowTop, column, row);
            }

            float buildTop = top + RunCells * row + (int)UiSpace.S;
            Place(_runBuild.rectTransform, x, buildTop, column, Wrapped(_runBuild, column));
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

        /// <summary>A text block at (x, top) from the content's top-left; returns its bottom.</summary>
        private static float Place(RectTransform rect, float x, float top, float width, float height)
        {
            UiFactory.Place(rect, x, top, width, height);
            return top + height;
        }

        /// <summary>A kit control moved to (x, top), keeping the size the kit measured for it; returns its bottom.</summary>
        private static float PlaceAt(RectTransform rect, float x, float top)
        {
            Vector2 size = rect.rect.size;
            UiFactory.Place(rect, x, top, size.x, size.y);
            return top + size.y;
        }

        private static float Line(TMP_Text text)
        {
            return Mathf.Ceil(text.GetPreferredValues("Ag").y);
        }

        private static float Wrapped(TMP_Text text, float width)
        {
            return Mathf.Max(Line(text), Mathf.Ceil(text.GetPreferredValues(text.text, width, 0f).y));
        }

        // ========================================================== settings page (WP-105 §C2)

        /// <summary>
        /// WP-105 (§C2, V-40, KOD NICE-10; C-00 11.10 decision): SETTINGS opens the run's settings inside the pause - the
        /// page takes the left column's place (the run so far stays on the right), the pause's title above it. Built on the
        /// first open, its values re-read on every open. Focus goes to its first row; Cancel / B, the Pause key and the C10
        /// Back go back to the pause's own page, focus on SETTINGS.
        /// </summary>
        private void OpenSettings()
        {
            if (!_paused || IsConfirming || _settingsOpen)
            {
                return;
            }

            if (_settingsPage == null)
            {
                BuildSettingsPage();
            }

            PlaceSettingsPage();
            RefreshSettings();
            ShowSettingsPage(true);
            UiInput.Focus(_masterVolume);
        }

        /// <summary>Back to the pause's own page with the Back cue, focus on SETTINGS (Cancel, the Pause key, the C10).</summary>
        private void CloseSettings()
        {
            if (!_settingsOpen)
            {
                return;
            }

            ShowSettingsPage(false);
            UiSound.Play(UiSoundKind.Back);
            UiInput.Focus(_settingsButton);
        }

        /// <summary>One page at a time: the settings page, or the left column's own controls (RESUME … END RUN).</summary>
        private void ShowSettingsPage(bool open)
        {
            _settingsOpen = open && _settingsPage != null;
            if (_settingsPage != null)
            {
                _settingsPage.gameObject.SetActive(_settingsOpen);
            }

            bool main = !_settingsOpen;
            _resumeButton.gameObject.SetActive(main);
            _settingsButton.gameObject.SetActive(main);
            _cameraLabel.gameObject.SetActive(main);
            _camera.gameObject.SetActive(main);
            _cameraCaption.gameObject.SetActive(main);
            _shakeSlider.gameObject.SetActive(main);
            _endRunButton.gameObject.SetActive(main);
        }

        /// <summary>The page on the left column, from RESUME's top to the pane's foot (measured on every open).</summary>
        private void PlaceSettingsPage()
        {
            float height = Mathf.Max(1f, _pane.Content.rect.height - _rowsTop);
            Place(_settingsPage, 0f, _rowsTop, Mathf.Max(1f, _leftWidth), height);
        }

        /// <summary>
        /// The page: its word (H1, like CAMERA) with the kit's C10 Back and the pad's Cancel glyph at its right, then a C35
        /// scroller (the rows outgrow a Mac window at text 1.25) with two section heads and the rows - AUDIO: MASTER, MUSIC,
        /// EFFECTS, UI SOUNDS; ACCESSIBILITY: TEXT SCALE (+ its note), REDUCE FLASHES, REDUCED MOTION. The rows are the kit's
        /// C14 / C15 under the main menu's own keys and object names, bound to UiSettings like the menu's (volumes on 5 %
        /// steps, text scale on the menu's three stops). One explicit vertical chain, Back first, no wrap; Left / Right on a
        /// slider move its value (the kit's). Camera shake stays on the pause's page, where it has always been.
        /// </summary>
        private void BuildSettingsPage()
        {
            _settingsPage = UiFactory.NewRect("SettingsPage", _pane.Content);
            PlaceSettingsPage();
            float width = _settingsPage.rect.width;

            UiIconButton back = UiBrushKit.Back(_settingsPage, CloseSettings);
            back.Button.ConfirmCue = false;   // WP-105 (SHOULD-8): its one voice is CloseSettings' Back cue
            var backRect = (RectTransform)back.transform;
            float prompt = UiInputGlyphs.ReservedWidth(UiPromptAction.Cancel) + (int)UiSpace.S;   // its Cancel glyph, outside its right edge
            backRect.anchorMin = new Vector2(1f, 1f);
            backRect.anchorMax = new Vector2(1f, 1f);
            backRect.pivot = new Vector2(1f, 1f);
            backRect.anchoredPosition = new Vector2(-prompt, 0f);

            string word = UiStrings.Get("menu.tab.settings", "SETTINGS");
            TMP_Text heading = UiFactory.Label("Heading", _settingsPage, word, UiType.H1, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            heading.enableWordWrapping = false;
            float headBand = Mathf.Max(Line(heading), UiBrushTokens.ControlHeight);
            Place(heading.rectTransform, 0f, 0f, Mathf.Max(1f, width - backRect.sizeDelta.x - prompt - (int)UiSpace.XL), headBand);

            _settingsScroll = UiBrushKit.Scroll(_settingsPage, "SettingsScroll");
            var scrollRect = (RectTransform)_settingsScroll.transform;
            scrollRect.offsetMax = new Vector2(0f, -(headBand + (int)UiSpace.L));
            RectTransform content = _settingsScroll.Content;
            float rows = Mathf.Max(1f, width - UiScrollView.Gutter);
            var stack = new UiStack(content);

            SettingsSection(ref stack, content, "settings.section.audio", "AUDIO", false);
            _masterVolume = SettingsRow(ref stack, UiBrushKit.Slider(content, "Setting.MASTER VOLUME", "settings.row.master_volume", "MASTER VOLUME",
                0f, 1f, UiSettings.MasterVolume, 0f, Percent, v => UiSettings.MasterVolume = Mathf.Round(v * 20f) / 20f, rows));
            _musicVolume = SettingsRow(ref stack, UiBrushKit.Slider(content, "Setting.MUSIC VOLUME", "settings.row.music_volume", "MUSIC",
                0f, 1f, UiSettings.MusicVolume, 0f, Percent, v => UiSettings.MusicVolume = Mathf.Round(v * 20f) / 20f, rows));
            _effectsVolume = SettingsRow(ref stack, UiBrushKit.Slider(content, "Setting.EFFECTS VOLUME", "settings.row.effects_volume", "EFFECTS",
                0f, 1f, UiSettings.EffectsVolume, 0f, Percent, v => UiSettings.EffectsVolume = Mathf.Round(v * 20f) / 20f, rows));
            _uiVolume = SettingsRow(ref stack, UiBrushKit.Slider(content, "Setting.UI SOUNDS", "settings.row.ui_sounds", "UI SOUNDS",
                0f, 1f, UiSettings.UiVolume, 0f, Percent, v => UiSettings.UiVolume = Mathf.Round(v * 20f) / 20f, rows));

            SettingsSection(ref stack, content, "menu.tab.accessibility", "ACCESSIBILITY", true);
            _textScale = SettingsRow(ref stack, UiBrushKit.Slider(content, "Setting.TEXT SCALE", "access.row.text_scale", "TEXT SCALE",
                UiSettings.TextScaleMin, UiSettings.TextScaleMax, UiSettings.TextScale, 0f, Percent, v => UiSettings.TextScale = v, rows));
            _textScale.SetStops(TextScaleStops);
            string note = UiStrings.Get("access.note.text_scale", "Body, label and caption text. It applies to the next screen you open.");
            TMP_Text noteText = UiFactory.Label("TextNote", content, note, UiType.MenuSmall, UiColor.TextSecondary);
            stack.Add(noteText.rectTransform, Mathf.Max(Line(noteText), Mathf.Ceil(noteText.GetPreferredValues(note, rows, 0f).y)));
            stack.Gap(UiSpace.S);
            _reduceFlashes = UiBrushKit.Toggle(content, "ReduceFlashes", "access.row.reduce_flashes", "REDUCE FLASHES", UiSettings.ReduceFlashes,
                on => UiSettings.ReduceFlashes = on, rows);
            SettingsRow(ref stack, _reduceFlashes.transform);
            _reducedMotion = UiBrushKit.Toggle(content, "ReducedMotion", "access.row.reduced_motion", "REDUCED MOTION", UiSettings.ReducedMotion,
                on => UiSettings.ReducedMotion = on, rows);
            SettingsRow(ref stack, _reducedMotion.transform);

            _settingsScroll.SetContentHeight(stack.Cursor);
            _settingsScroll.TrackSelectables();
            UiInput.ChainVertical(false, back.Button, _masterVolume, _musicVolume, _effectsVolume, _uiVolume, _textScale,
                _reduceFlashes.Button, _reducedMotion.Button);
            _settingsBack = back;
        }

        /// <summary>A C14 / C15 row on the page's stack: the kit's row height (MenuBody's line box at the text scale), its step's air under it.</summary>
        private static T SettingsRow<T>(ref UiStack stack, T row) where T : Component
        {
            SettingsRow(ref stack, row.transform);
            return row;
        }

        private static void SettingsRow(ref UiStack stack, Transform row)
        {
            stack.Add((RectTransform)row, UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHit));
            stack.Cursor += UiBrushTokens.RowStep - UiBrushTokens.RowHit;
        }

        /// <summary>A section head as the menu draws it: the word (MenuSmall, TextSecondary), then the plain C6 rule to the edge (no brush, no budget).</summary>
        private static void SettingsSection(ref UiStack stack, RectTransform content, string key, string fallback, bool gapBefore)
        {
            if (gapBefore)
            {
                stack.Gap(UiSpace.S);
            }

            string word = UiStrings.Get(key, fallback);
            RectTransform head = UiFactory.NewRect("Section." + fallback, content);
            TMP_Text label = UiFactory.Label("Label", head, word, UiType.MenuSmall, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            label.enableWordWrapping = false;
            UiFactory.Fill(label.rectTransform);
            stack.Add(head, Line(label));
            float x = Mathf.Ceil(label.GetPreferredValues(word).x) + (int)UiSpace.M;
            RectTransform rule = UiBrushKit.Hairline(head, 1f).rectTransform;
            rule.anchorMin = new Vector2(0f, 0.5f);
            rule.anchorMax = new Vector2(1f, 0.5f);
            rule.pivot = new Vector2(0f, 0.5f);
            rule.offsetMin = new Vector2(x, -0.5f);
            rule.offsetMax = new Vector2(0f, 0.5f);
            stack.Gap(UiSpace.XS);
        }

        /// <summary>The rows show what UiSettings holds now (the F1 panel or another screen may have moved a value); no notify.</summary>
        private void RefreshSettings()
        {
            _masterVolume.SetValue(UiSettings.MasterVolume, false);
            _musicVolume.SetValue(UiSettings.MusicVolume, false);
            _effectsVolume.SetValue(UiSettings.EffectsVolume, false);
            _uiVolume.SetValue(UiSettings.UiVolume, false);
            _textScale.SetValue(UiSettings.TextScale, false);
            _reduceFlashes.Set(UiSettings.ReduceFlashes, false);
            _reducedMotion.Set(UiSettings.ReducedMotion, false);
        }

        /// <summary>A 0-1 value as the kit's sliders write it: EN "70%", TR "%70" (UiStrings.Percent).</summary>
        private static string Percent(float value)
        {
            return UiStrings.Percent(Mathf.RoundToInt(value * 100f));
        }

        // ============================================================ focus loss (WP-105 §C5)

        /// <summary>
        /// WP-105 (§C5, KOD SHOULD-9; C-00 11.10 decision 5): the window losing focus (alt-tab, a notification) pauses a live
        /// run - with runInBackground on, the horde kept coming behind the player's back. Focus coming back resumes nothing:
        /// the player says RESUME. Off in automation (<see cref="FocusPauseAllowed"/>).
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus || _paused || !FocusPauseEnabled || !CanOpen())
            {
                return;
            }

            SetPaused(true);
        }

        private static int s_focusPause;   // 0 = not read yet, 1 = on, -1 = off (the command line does not change in a session)

        /// <summary>Tests: force the focus-loss pause on or off; null = the rule (<see cref="FocusPauseAllowed"/>).</summary>
        internal static bool? FocusPauseOverride;

        /// <summary>Whether a focus loss pauses this session's runs (read once).</summary>
        public static bool FocusPauseEnabled
        {
            get
            {
                if (FocusPauseOverride.HasValue)
                {
                    return FocusPauseOverride.Value;
                }

                if (s_focusPause == 0)
                {
                    string[] args;
                    try
                    {
                        args = Environment.GetCommandLineArgs();
                    }
                    catch (Exception)
                    {
                        args = null;
                    }

                    s_focusPause = FocusPauseAllowed(Application.isBatchMode, args) ? 1 : -1;
                }

                return s_focusPause > 0;
            }
        }

        /// <summary>
        /// Automation flags that run the game unfocused: -ddNoFocusPause (the explicit switch), the UI capture agents
        /// (-ddUiCapture…, -ddUiFxCapture…, -ddUiKit…, -ddUiPerf, -ddCapture), the QA bot and its runs (QaRunBot's -ddQaBot,
        /// every -ddQa…), the TopDown capture (-ddTd…), the pilot / perf / VFX / showcase / feel-cost runs. Any -dd…Probe flag
        /// counts too. A player's own launch (and a guarded playtest's -ddTelemetryDir / -ddProfilePath) keeps the pause.
        /// </summary>
        private static readonly string[] FocusPauseOffFlags =
        {
            "-ddNoFocusPause", "-ddUiCapture", "-ddUiFxCapture", "-ddUiKit", "-ddUiPerf", "-ddCapture", "-ddQa", "-ddTd",
            "-ddPilot", "-ddVfx", "-ddWeaponShowcase", "-ddFeelCost", "-ddCinePerf"
        };

        /// <summary>
        /// WP-105 (§C5): the rule - off in batch mode, under the test runner (-runTests) and with any automation flag
        /// (<see cref="FocusPauseOffFlags"/> as prefixes, or a -dd…Probe flag); on otherwise. Pure, for the EditMode suite.
        /// </summary>
        public static bool FocusPauseAllowed(bool batchMode, string[] args)
        {
            if (batchMode)
            {
                return false;
            }

            if (args == null)
            {
                return true;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                if (string.Equals(arg, "-runTests", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!arg.StartsWith("-dd", StringComparison.Ordinal))
                {
                    continue;
                }

                if (arg.IndexOf("Probe", StringComparison.Ordinal) >= 0)
                {
                    return false;
                }

                for (int f = 0; f < FocusPauseOffFlags.Length; f++)
                {
                    if (arg.StartsWith(FocusPauseOffFlags[f], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFocusPause()
        {
            s_focusPause = 0;
            FocusPauseOverride = null;
        }

        // ==================================================================== shake

        private void OnShakeChanged(float value)
        {
            PrototypeCameraRig.SetShakeIntensity(value);
        }

        /// <summary>
        /// The slider's value text, or "—" when there is no rig to write to (the error state says so). WP-105 (NICE-3, V-04):
        /// a percentage like every other slider of the kit (UiSlider's own default, HEAD BOB "100%") in the language's
        /// order - EN "70%", TR "%70" - never the invariant "0.70" (TR writes its decimals with a comma). The scale is the
        /// shake multiplier: 1 = 100%.
        /// </summary>
        private string FormatShake(float value)
        {
            return _shakeAvailable ? UiStrings.Percent(Mathf.RoundToInt(value * 100f)) : "—";
        }

        /// <summary>
        /// Re-reads the live value each time the screen opens, because the F1 panel
        /// exposes the same setting and the two must never disagree.
        /// </summary>
        private void RefreshShake()
        {
            // Error state: no camera rig and no config to write to. The control says so
            // rather than pretending to work.
            _shakeAvailable = PrototypeCameraRig.Active != null ||
                              DD.Prototype.Core.Config.PrototypeConfig.Active != null;
            _shakeSlider.interactable = _shakeAvailable;

            // K4: a live value above the range (an old 0-2 setting, the F1 panel) is written back clipped, so the
            // camera shakes exactly as much as the slider says.
            float live = PrototypeCameraRig.GetShakeIntensity();
            float clipped = Mathf.Clamp(live, 0f, ShakeMax);
            if (_shakeAvailable && (live < 0f || live > ShakeMax))
            {
                PrototypeCameraRig.SetShakeIntensity(clipped);
            }

            _shakeSlider.SetValue(clipped, false);
        }
    }
}
