using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-104 B4 (S-09) · the tree screen's shortcuts (AD-VISUAL §5.5), read here like every other UI key (the UI reads input
    /// only through <see cref="UiInput"/>; no Input System type - ADR-024-2). The legacy Input Manager has no named axis for the
    /// shoulder and face buttons, so they are its joystick buttons in the XInput order (4 LB, 5 RB, 3 Y, 2 X) - the same
    /// gamepad caveat as Submit / Cancel above: verified through the screen's own handlers in PlayMode, not on a pad.
    ///   Q / LB, E / RB   previous / next branch (its remembered or first open node)
    ///   Tab / Y          the synergy column (and back)
    ///   C / X            CONTINUE - one key (KARAR-WP104-11.10 decision 1), drawn as <see cref="UiPromptAction.Continue"/>
    ///   T                set / clear the Target on the focused node (pad: the detail panel's TARGET; mouse: right-click)
    /// Every read is false / 0 while the curtain covers (as <see cref="CancelPressed"/>). Polled by the round-end screen's Update
    /// while the tree is up; nothing here allocates.
    /// </summary>
    public static partial class UiInput
    {
        /// <summary>−1 previous branch (Q / LB), +1 next (E / RB), 0 none this frame.</summary>
        public static int BranchStepPressed()
        {
            if (UiCurtain.IsCovering)
            {
                return 0;
            }

            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.JoystickButton4))
            {
                return -1;
            }

            return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.JoystickButton5) ? 1 : 0;
        }

        /// <summary>Tab / Y: the synergy column.</summary>
        public static bool SynergyViewPressed()
        {
            return !UiCurtain.IsCovering && (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton3));
        }

        /// <summary>C / X: continue to the next round.</summary>
        public static bool ContinuePressed()
        {
            return !UiCurtain.IsCovering && (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.JoystickButton2));
        }

        /// <summary>T: the Target on the focused node.</summary>
        public static bool TargetPressed()
        {
            return !UiCurtain.IsCovering && Input.GetKeyDown(KeyCode.T);
        }
    }
}
