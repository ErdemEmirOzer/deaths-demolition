using System.Collections.Generic;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Keyboard / gamepad plumbing for the menus (WP-022): explicit navigation chains,
    /// focus guarantees and the Cancel button.
    ///
    /// The EventSystem runs the legacy StandaloneInputModule (see UiFactory), whose
    /// axes come from the Input Manager: "Horizontal"/"Vertical" already cover the
    /// arrow keys, WASD and the left stick / d-pad, "Submit" is Return/Space and the
    /// south face button, "Cancel" is Escape and the east face button. Nothing here
    /// references UnityEngine.InputSystem, which Core/Input keeps out of this module.
    ///
    /// GAMEPAD CAVEAT: written against the Input Manager defaults; there is no gamepad on
    /// the build machine, so the stick paths are verified only through the EventSystem's
    /// own axis/submit events in PlayMode (MetaUiSliceTests.Gamepad_*).
    /// </summary>
    public static class UiInput
    {
        private const string CancelButton = "Cancel";

        /// <summary>
        /// True on the frame the Cancel action was pressed (Escape / gamepad east). Reads
        /// the legacy button by name and never throws when the axis is missing. Never true
        /// while the load error stands (R1-E B2): it has no safe "back", so the screen under
        /// it must not take one.
        /// R4 WP-105 (KOD NICE-8): never true while the curtain covers either (from the moment it starts closing until it is
        /// half open again): the screen under it is leaving or not shown yet, so a "back" there would play its sound, start a
        /// load that is ignored, or open a dialog under the cover. The curtain's own read is <see cref="CurtainCancelPressed"/>.
        /// </summary>
        public static bool CancelPressed()
        {
            if (UiCurtain.IsCovering)
            {
                return false;
            }

            return CurtainCancelPressed();
        }

        /// <summary>
        /// R4 WP-105 (NICE-8) · Cancel as the curtain itself reads it (the boot seal's skip): pressed this frame even while the
        /// curtain covers. Still never true under the load error (R1-E B2). Screens read <see cref="CancelPressed"/>.
        /// </summary>
        public static bool CurtainCancelPressed()
        {
            if (UiCurtain.LoadErrorOpen)
            {
                return false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Time.frameCount == s_simulatedCancelFrame)
            {
                return true;
            }
#endif

            // WP-022K (S-10 finding, MainMenu ~2 allocations/frame from the UI): probing the
            // legacy button by name throws when the Input Manager has no "Cancel" entry, and
            // a thrown-and-caught exception every frame IS the allocation. Probe once, then
            // read the remembered path with no exception on the hot path.
            if (s_cancelButtonState == 0)
            {
                try
                {
                    Input.GetButtonDown(CancelButton);
                    s_cancelButtonState = 1;
                }
                catch (System.ArgumentException)
                {
                    s_cancelButtonState = -1;
                }
            }

            if (s_cancelButtonState > 0)
            {
                return Input.GetButtonDown(CancelButton);
            }

            // Input Manager without a "Cancel" entry: Escape alone.
            return Input.GetKeyDown(KeyCode.Escape);
        }

        /// <summary>0 = not probed, 1 = the "Cancel" button exists, -1 = missing (Escape only).</summary>
        private static int s_cancelButtonState;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static int s_simulatedCancelFrame = -1;

        /// <summary>
        /// Dev / test seam (R1-E B4): every <see cref="CancelPressed"/> reader sees one press on the NEXT frame, as the
        /// legacy button would - a test cannot press Escape or the pad's east button through the Input Manager.
        /// </summary>
        public static void SimulateCancelNextFrame()
        {
            s_simulatedCancelFrame = Time.frameCount + 1;
        }
#endif

        private const string SubmitButton = "Submit";

        /// <summary>0 = not probed, 1 = the "Submit" button exists, -1 = missing (Return / Space only).</summary>
        private static int s_submitButtonState;

        /// <summary>
        /// WP-068 slice 5: true while Submit is held (Return / Space / gamepad south) - the
        /// keyboard and pad half of a hold control (<see cref="UiButton.ConfigureHold"/>).
        /// Same probe-once rule as <see cref="CancelPressed"/>.
        /// </summary>
        /// <summary>
        /// WP-068 R1-D: true on the frame Submit was pressed (Return / Space / gamepad south) - the Results choreography's
        /// skip. Same probe-once rule as <see cref="CancelPressed"/>.
        /// R4 WP-105 (NICE-8): never true while the curtain covers (as <see cref="CancelPressed"/>). The held state
        /// (<see cref="SubmitHeld"/>, the hold controls and the boot seal's skip) is not filtered.
        /// </summary>
        public static bool SubmitPressed()
        {
            SubmitHeld();   // probes the button once
            if (UiCurtain.IsCovering)
            {
                return false;
            }

            if (s_submitButtonState > 0)
            {
                return Input.GetButtonDown(SubmitButton);
            }

            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
        }

        public static bool SubmitHeld()
        {
            if (s_submitButtonState == 0)
            {
                try
                {
                    Input.GetButton(SubmitButton);
                    s_submitButtonState = 1;
                }
                catch (System.ArgumentException)
                {
                    s_submitButtonState = -1;
                }
            }

            if (s_submitButtonState > 0)
            {
                return Input.GetButton(SubmitButton);
            }

            return Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Space);
        }

        /// <summary>Puts EventSystem focus on a control, if both exist and the control accepts it.</summary>
        public static void Focus(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null || !selectable.IsInteractable() ||
                !selectable.gameObject.activeInHierarchy)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }

        /// <summary>
        /// True when nothing is focused, or the focused object is gone/disabled - the
        /// state a mouse click on empty space leaves behind, in which a gamepad's next
        /// press would go nowhere.
        /// </summary>
        public static bool FocusLost()
        {
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                return false;
            }

            GameObject current = system.currentSelectedGameObject;
            if (current == null || !current.activeInHierarchy)
            {
                return true;
            }

            var selectable = current.GetComponent<Selectable>();
            return selectable != null && !selectable.IsInteractable();
        }

        /// <summary>
        /// Re-focuses <paramref name="fallback"/> when focus was lost and a navigation
        /// input arrives (any axis, Submit or Cancel): a mouse user who clicked empty
        /// space and then picked up the pad gets a visible cursor on the first press
        /// instead of a dead first press. Call once per frame from a screen's Update.
        /// R4 WP-105 (KOD SHOULD-13, NICE-8): where the focus may go is <see cref="ReclaimTarget"/> - while a modal stands, only
        /// into the modal (a screen under it reclaims for the modal, never behind it; a selection left behind the modal
        /// counts as lost), and nothing under a closing or closed curtain except the curtain's own load error.
        /// </summary>
        public static void ReclaimFocus(Selectable fallback)
        {
            Selectable target = ReclaimTarget(fallback);
            if (target == null)
            {
                return;
            }

            UiModal modal = TopModal;
            if (!FocusLost() && (modal == null || HeldUnder(modal.transform)))
            {
                return;
            }

            bool moved;
            try
            {
                moved = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.5f ||
                        Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.5f ||
                        Input.GetButtonDown("Submit");
            }
            catch (System.ArgumentException)
            {
                moved = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
                        Input.GetKeyDown(KeyCode.Return);
            }

            if (moved)
            {
                Focus(target);
            }
        }

        // ============================================== R4 WP-105 · focus ownership under a modal and the curtain

        private static readonly List<UiModal> s_modals = new List<UiModal>(4);

        /// <summary>
        /// R4 WP-105 (KOD SHOULD-13) · <see cref="UiModal.Open"/> registers its modal: from now on the modal owns the focus
        /// (<see cref="ReclaimTarget"/>). The last one opened is on top. Build or press time, never per frame.
        /// </summary>
        public static void ModalOpened(UiModal modal)
        {
            if (modal == null)
            {
                return;
            }

            s_modals.Remove(modal);
            s_modals.Add(modal);
        }

        /// <summary>R4 WP-105 · <see cref="UiModal.Close"/> (and the load error's HideError) gives the focus back to the screens.</summary>
        public static void ModalClosed(UiModal modal)
        {
            s_modals.Remove(modal);
        }

        /// <summary>
        /// R4 WP-105 · the modal on top: the last one opened that is still open and on screen (a destroyed one is dropped, a
        /// hidden one skipped); null when none stands. A walk over at most a few entries, no allocation.
        /// </summary>
        public static UiModal TopModal
        {
            get
            {
                for (int i = s_modals.Count - 1; i >= 0; i--)
                {
                    UiModal modal = s_modals[i];
                    if (modal == null)
                    {
                        s_modals.RemoveAt(i);
                        continue;
                    }

                    if (modal.gameObject.activeInHierarchy)
                    {
                        return modal;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// R4 WP-105 (KOD SHOULD-13, NICE-8) · where a reclaim asked for <paramref name="fallback"/> may put the focus:
        ///   * the curtain covers (closing, closed or opening) and no load error stands: nowhere (null) - the screen under it
        ///     is leaving or not revealed yet, and the pad's next Submit would press a button nobody sees;
        ///   * a modal stands: the fallback when it is inside the modal, otherwise the modal's own safe action (the screen
        ///     under it asks, the modal answers - focus never goes behind a dialog);
        ///   * otherwise the fallback.
        /// </summary>
        public static Selectable ReclaimTarget(Selectable fallback)
        {
            if (UiCurtain.IsCovering && !UiCurtain.LoadErrorOpen)
            {
                return null;
            }

            UiModal modal = TopModal;
            if (modal != null)
            {
                return fallback != null && fallback.transform.IsChildOf(modal.transform) ? fallback : modal.DefaultFocus;
            }

            return fallback;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetModals()
        {
            s_modals.Clear();
        }

        /// <summary>
        /// R4 WP-105 (MUST-1) · as <see cref="ReclaimFocus(Selectable)"/>, but a selection still active under
        /// <paramref name="keepUnder"/> is held, not lost - even when it is not interactable (a locked row the pad walked onto on
        /// purpose: its explicit navigation leads there and its reason shows on focus).
        /// </summary>
        public static void ReclaimFocus(Selectable fallback, Transform keepUnder)
        {
            if (HeldUnder(keepUnder))
            {
                return;
            }

            ReclaimFocus(fallback);
        }

        /// <summary>The current selection is active under <paramref name="root"/> (interactable or not): focus held there, not lost.</summary>
        public static bool HeldUnder(Transform root)
        {
            EventSystem system = EventSystem.current;
            GameObject current = system != null ? system.currentSelectedGameObject : null;
            return root != null && current != null && current.activeInHierarchy && current.transform.IsChildOf(root);
        }

        /// <summary>
        /// Links controls top-to-bottom with explicit Up/Down targets, wrapping at both
        /// ends, and leaves Left/Right on automatic. Null entries are skipped.
        /// </summary>
        public static void ChainVertical(params Selectable[] controls)
        {
            ChainVertical(true, controls);
        }

        /// <summary>
        /// Vertical chain with a wrap choice. A chain whose ends are PLAY and QUIT must NOT
        /// wrap: one stray Up on the first frame would park focus on the destructive button.
        /// </summary>
        public static void ChainVertical(bool wrap, params Selectable[] controls)
        {
            int count = 0;
            var live = new Selectable[controls.Length];
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] != null)
                {
                    live[count++] = controls[i];
                }
            }

            for (int i = 0; i < count; i++)
            {
                UnityEngine.UI.Navigation navigation = live[i].navigation;
                navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                navigation.selectOnUp = i > 0 ? live[i - 1] : wrap ? live[count - 1] : null;
                navigation.selectOnDown = i < count - 1 ? live[i + 1] : wrap ? live[0] : null;
                live[i].navigation = navigation;
            }
        }

        /// <summary>Links controls left-to-right with explicit Left/Right targets, wrapping.</summary>
        public static void ChainHorizontal(params Selectable[] controls)
        {
            int count = 0;
            var live = new Selectable[controls.Length];
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] != null)
                {
                    live[count++] = controls[i];
                }
            }

            for (int i = 0; i < count; i++)
            {
                UnityEngine.UI.Navigation navigation = live[i].navigation;
                navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
                navigation.selectOnLeft = live[(i - 1 + count) % count];
                navigation.selectOnRight = live[(i + 1) % count];
                live[i].navigation = navigation;
            }
        }

        /// <summary>Sets one control's Up/Down explicitly without touching Left/Right.</summary>
        public static void LinkVertical(Selectable control, Selectable up, Selectable down)
        {
            if (control == null)
            {
                return;
            }

            UnityEngine.UI.Navigation navigation = control.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            if (up != null)
            {
                navigation.selectOnUp = up;
            }

            if (down != null)
            {
                navigation.selectOnDown = down;
            }

            control.navigation = navigation;
        }

        /// <summary>Sets one control's Left/Right explicitly without touching Up/Down.</summary>
        public static void LinkHorizontal(Selectable control, Selectable left, Selectable right)
        {
            if (control == null)
            {
                return;
            }

            UnityEngine.UI.Navigation navigation = control.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            if (left != null)
            {
                navigation.selectOnLeft = left;
            }

            if (right != null)
            {
                navigation.selectOnRight = right;
            }

            control.navigation = navigation;
        }

        /// <summary>
        /// An AP-012 glyph at the leading edge of a button, with the label shifted right
        /// of it. Without the atlas the button is left exactly as it was.
        /// </summary>
        public static Image LeadingIcon(UiButton button, string iconKey, UiColor color, float size)
        {
            if (button == null || !UiArt.HasIcon(iconKey))
            {
                return null;
            }

            Image icon = UiArt.IconImage("Icon", button.transform, iconKey, color, size);
            var rect = (RectTransform)icon.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2((int)UiSpace.L, 0f);

            RectTransform label = (RectTransform)button.LabelText.transform;
            label.offsetMin = new Vector2(label.offsetMin.x + size + (int)UiSpace.M, label.offsetMin.y);
            return icon;
        }

        /// <summary>
        /// WP-022F · an input prompt glyph at the button's trailing edge ("BACK  Ⓑ"): the
        /// action's glyph on the active device, redrawn when the device flips. Ink on the
        /// button's own text colour family: Primary buttons get ink-on-cream, the others
        /// cream-on-inset, so the glyph reads on either fill.
        /// </summary>
        public static UiPrompt Prompt(UiButton button, UiPromptAction action, bool onPrimary = false, float size = UiInputGlyphs.DefaultSize)
        {
            if (button == null)
            {
                return null;
            }

            UiPrompt prompt = UiInputGlyphs.Build(
                "Prompt", button.transform, action,
                onPrimary ? UiColor.SurfaceBase : UiColor.TextPrimary,
                onPrimary ? UiColor.TextPrimary : UiColor.SurfaceInset, size);
            var rect = (RectTransform)prompt.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-(int)UiSpace.L, 0f);

            // The label gives up the widest glyph the action can draw (A vs ENTER), so it
            // does not shift when the device flips (AD v005 MUST-4).
            RectTransform label = (RectTransform)button.LabelText.transform;
            prompt.AttachLabel(label, label.offsetMax.x); // WP-022J: re-inset on every rebind, not only at build
            return prompt;
        }

        // ============================================== R4 WP-106 · the pad's view controls (Prepare Run's weapon stage)

        /// <summary>
        /// R4 WP-106 · the right stick as a 3D view's orbit (Prepare Run's weapon stage, <see cref="PrepareRunStage"/>): Core/Input's
        /// look stick - dead zone applied, magnitude at most 1 (the aim stick for a source without look). The pad's left stick and
        /// d-pad stay the menus' navigation, so this stick moves no focus. Zero while the curtain covers, with no real input source
        /// or a suspended one (a rebind listening). Read through Core/Input only, no Input System type here (ADR-024-2 / WP-059).
        /// </summary>
        public static Vector2 ViewStick()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (s_viewSimulated)
            {
                return s_simulatedStick;
            }
#endif

            IInputSource source = ViewSource();
            return source != null ? Vector2.ClampMagnitude(source.ReadLookStick(), 1f) : Vector2.zero;
        }

        /// <summary>
        /// R4 WP-106 · the triggers as a 3D view's zoom: +1 in (RT, the Fire action), −1 out (LT, the Focus action), 0 for both or
        /// neither. Only while the pad is the active scheme: on the mouse the same two actions are its buttons, which drag the view.
        /// A player's Fire / Focus remap carries over (the actions are read, never the buttons). Zero under the same conditions as
        /// <see cref="ViewStick"/>.
        /// </summary>
        public static float ViewZoomAxis()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (s_viewSimulated)
            {
                return s_simulatedZoom;
            }
#endif

            IInputSource source = ViewSource();
            if (source == null || source.ActiveScheme() != InputSchemeKind.Gamepad)
            {
                return 0f;
            }

            float axis = 0f;
            if (source.FireHeld)
            {
                axis += 1f;
            }

            if (source.FocusHeld())
            {
                axis -= 1f;
            }

            return axis;
        }

        /// <summary>The live device source the view controls read, or null (curtain covering, no real source, input suspended).</summary>
        private static IInputSource ViewSource()
        {
            if (UiCurtain.IsCovering || !InputSourceRegistry.HasRealSource)
            {
                return null;
            }

            IInputSource source = InputSourceRegistry.Current;
            return source.IsEnabled ? source : null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool s_viewSimulated;
        private static Vector2 s_simulatedStick;
        private static float s_simulatedZoom;

        /// <summary>
        /// Dev / test seam (R4 WP-106): <see cref="ViewStick"/> and <see cref="ViewZoomAxis"/> answer these values until
        /// <see cref="ClearSimulatedViewInput"/> - a test has no pad (the <see cref="SimulateCancelNextFrame"/> pattern).
        /// </summary>
        public static void SimulateViewInput(Vector2 stick, float zoom)
        {
            s_viewSimulated = true;
            s_simulatedStick = Vector2.ClampMagnitude(stick, 1f);
            s_simulatedZoom = Mathf.Clamp(zoom, -1f, 1f);
        }

        /// <summary>Dev / test seam (R4 WP-106): the view controls read the device again.</summary>
        public static void ClearSimulatedViewInput()
        {
            s_viewSimulated = false;
            s_simulatedStick = Vector2.zero;
            s_simulatedZoom = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetViewInput()
        {
            ClearSimulatedViewInput();
        }
#endif
    }

    /// <summary>
    /// Slice 9 · E3 T2 (the shared focus-caption hook): calls back when its control gains or loses focus - keyboard, pad,
    /// or a pointer hover (a UiButton moves focus on hover). Event-driven: a screen needs no per-frame focus poll.
    /// USAGE: a caption that describes the focused choice and falls back to the current one when focus leaves.
    /// Focus plumbing (no visual), so it lives with UiInput, not with the kit's drawn components.
    /// </summary>
    public sealed class UiFocusCallback : MonoBehaviour, UnityEngine.EventSystems.ISelectHandler, UnityEngine.EventSystems.IDeselectHandler
    {
        private System.Action _focus;
        private System.Action _blur;

        public void Configure(System.Action focus, System.Action blur)
        {
            _focus = focus;
            _blur = blur;
        }

        public void OnSelect(UnityEngine.EventSystems.BaseEventData eventData)
        {
            _focus?.Invoke();
        }

        public void OnDeselect(UnityEngine.EventSystems.BaseEventData eventData)
        {
            _blur?.Invoke();
        }
    }
}
