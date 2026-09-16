namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// Stable string keys into Assets/_Prototype/Input/DD_Controls.inputactions.
    ///
    /// The .inputactions asset deliberately does NOT generate a C# wrapper class:
    /// generated wrappers are regenerated on every asset edit and would leak Input
    /// System types into whatever assembly they land in. The adapter looks actions up
    /// by these constants instead.
    /// </summary>
    public static class PrototypeInputNames
    {
        public const string MapGameplay = "Gameplay";
        public const string MapUI = "UI";

        public const string ActionMove = "Move";
        public const string ActionAim = "Aim";
        public const string ActionAimStick = "AimStick";
        public const string ActionFire = "Fire";
        public const string ActionReload = "Reload";
        public const string ActionPause = "Pause";
        public const string ActionDebugPanel = "DebugPanel";

        // WP-024: gamepad parity. Special = use / fire the Special Weapon path on a
        // dedicated control (today the Special Weapon fires with Fire; the action is the
        // remappable slot the design may bind later). Interact = context action.
        public const string ActionSpecial = "Special";
        public const string ActionInteract = "Interact";

        // WP-024: UI map actions consumed by the Input System UI module (S-09 screens).
        public const string UiNavigate = "Navigate";
        public const string UiSubmit = "Submit";
        public const string UiCancel = "Cancel";
        public const string UiPoint = "Point";
        public const string UiClick = "Click";
        public const string UiScrollWheel = "ScrollWheel";

        public const string SchemeKeyboardMouse = "KeyboardMouse";
        public const string SchemeGamepad = "Gamepad";
    }
}
