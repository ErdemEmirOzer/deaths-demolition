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
        /// the legacy button by name and never throws when the axis is missing.
        /// </summary>
        public static bool CancelPressed()
        {
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

        private const string SubmitButton = "Submit";

        /// <summary>0 = not probed, 1 = the "Submit" button exists, -1 = missing (Return / Space only).</summary>
        private static int s_submitButtonState;

        /// <summary>
        /// WP-068 slice 5: true while Submit is held (Return / Space / gamepad south) - the
        /// keyboard and pad half of a hold control (<see cref="UiButton.ConfigureHold"/>).
        /// Same probe-once rule as <see cref="CancelPressed"/>.
        /// </summary>
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
        /// </summary>
        public static void ReclaimFocus(Selectable fallback)
        {
            if (fallback == null || !FocusLost())
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
                Focus(fallback);
            }
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
    }
}
