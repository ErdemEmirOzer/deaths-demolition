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
        private const float PanelWidth = 680f;
        private const float PanelHeight = 520f;
        private const float DialogWidth = 640f;
        private const float DialogHeight = 340f;
        private const float ShakeMax = 2f;

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

        private bool _paused;
        private bool _confirming;

        /// <summary>Whatever held focus when the pause opened - a shop card, CONTINUE - to hand it back on close.</summary>
        private GameObject _focusBefore;

        public bool IsPaused
        {
            get { return _paused; }
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
                UiNavigation.EnterLayer(UiContext.Paused);

                RefreshShake();
                UiSound.Play(UiSoundKind.Confirm);
                Focus(_resumeButton);
            }
            else
            {
                CloseConfirm();
                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
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

            RectTransform card = UiFactory.FramedCard("Panel", root, UiColor.BorderStrong, UiColor.SurfacePanel, UiFrame.Card);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            card.anchoredPosition = Vector2.zero;

            RectTransform host = UiFactory.CardContent(card, UiSpace.XXXL);
            var stack = new UiStack(host);

            TMP_Text title = UiFactory.Label("Title", host, "PAUSED", UiType.H1, UiColor.TextPrimary);
            stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H1));

            stack.Gap(UiSpace.S);

            TMP_Text caption = UiFactory.Label(
                "Caption", host,
                "The whole simulation is stopped: enemies, projectiles, timers and the round clock.",
                UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)caption.transform, UiFactory.LineHeight(UiType.Body) * 2f);

            stack.Gap(UiSpace.XL);
            stack.Add((RectTransform)UiFactory.Divider("D1", host).transform, UiTokens.StrokeHairline);
            stack.Gap(UiSpace.XL);

            TMP_Text shakeLabel = UiFactory.Label(
                "ShakeLabel", host, "CAMERA SHAKE INTENSITY", UiType.Label, UiColor.TextMuted);
            RectTransform shakeLabelRect = stack.Add(
                (RectTransform)shakeLabel.transform, UiFactory.LineHeight(UiType.Label));

            _shakeValue = UiFactory.Numeric(
                "ShakeValue", host, "1.00", UiType.BodyStrong, UiColor.TextPrimary, TextAnchor.MiddleRight);
            RectTransform shakeValueRect = (RectTransform)_shakeValue.transform;
            shakeValueRect.anchorMin = new Vector2(1f, 1f);
            shakeValueRect.anchorMax = new Vector2(1f, 1f);
            shakeValueRect.pivot = new Vector2(1f, 1f);
            shakeValueRect.sizeDelta = new Vector2(120f, 22f);
            shakeValueRect.anchoredPosition = shakeLabelRect.anchoredPosition;

            stack.Gap(UiSpace.S);

            _shakeSlider = UiFactory.NewSlider(
                "ShakeSlider", host, 0f, ShakeMax, PrototypeCameraRig.GetShakeIntensity());
            RectTransform sliderRect = stack.Add(
                (RectTransform)_shakeSlider.transform, UiTokens.TargetCompact);
            sliderRect.sizeDelta = new Vector2(0f, UiTokens.TargetCompact);

            _shakeSlider.onValueChanged.AddListener(OnShakeChanged);

            stack.Gap(UiSpace.S);

            TMP_Text shakeCaption = UiFactory.Label(
                "ShakeCaption", host,
                "0 disables shake completely; 1 is the default. Shake never moves your aim at any setting.",
                UiType.Caption, UiColor.TextMuted);
            stack.Add((RectTransform)shakeCaption.transform, UiFactory.LineHeight(UiType.Caption) * 2f);

            // Actions are docked to the foot of the card, not to the cursor, so the
            // panel keeps the same shape whatever the caption above wraps to.
            _resumeButton = UiFactory.Button("Resume", host, "RESUME", UiButtonVariant.Primary, Resume);
            UiInput.Prompt(_resumeButton, UiPromptAction.Pause, true); // the same key that opened it closes it
            RectTransform resumeRect = (RectTransform)_resumeButton.transform;
            resumeRect.anchorMin = new Vector2(0f, 0f);
            resumeRect.anchorMax = new Vector2(0.5f, 0f);
            resumeRect.pivot = new Vector2(0.5f, 0f);
            resumeRect.offsetMin = new Vector2(0f, 0f);
            resumeRect.offsetMax = new Vector2(-(int)UiSpace.S, UiTokens.TargetPreferred);

            UiButton quit = UiFactory.Button(
                "MainMenu", host, "MAIN MENU", UiButtonVariant.Secondary, OpenConfirm);
            RectTransform quitRect = (RectTransform)quit.transform;
            quitRect.anchorMin = new Vector2(0.5f, 0f);
            quitRect.anchorMax = new Vector2(1f, 0f);
            quitRect.pivot = new Vector2(0.5f, 0f);
            quitRect.offsetMin = new Vector2((int)UiSpace.S, 0f);
            quitRect.offsetMax = new Vector2(0f, UiTokens.TargetPreferred);
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

            TMP_Text title = UiFactory.Label("Title", host, "END THIS RUN?", UiType.H2, UiColor.TextPrimary);
            stack.Add((RectTransform)title.transform, UiFactory.LineHeight(UiType.H2));

            stack.Gap(UiSpace.M);

            TMP_Text body = UiFactory.Label(
                "Body", host,
                "Leaving now ends the run. The current round, all unspent gold and every weapon upgrade " +
                "you bought are lost. Profile level, statistics and achievements you have already earned " +
                "are kept.",
                UiType.Body, UiColor.TextSecondary);
            stack.Add((RectTransform)body.transform, UiFactory.LineHeight(UiType.Body) * 4f);

            _cancelButton = UiFactory.Button(
                "Cancel", host, "KEEP PLAYING", UiButtonVariant.Secondary, CloseConfirm);
            RectTransform cancelRect = (RectTransform)_cancelButton.transform;
            cancelRect.anchorMin = new Vector2(0f, 0f);
            cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0f);
            cancelRect.offsetMin = new Vector2(0f, 0f);
            cancelRect.offsetMax = new Vector2(-(int)UiSpace.S, UiTokens.TargetPreferred);

            UiButton confirm = UiFactory.Button(
                "Confirm", host, "END RUN", UiButtonVariant.Danger, ConfirmQuit);
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
