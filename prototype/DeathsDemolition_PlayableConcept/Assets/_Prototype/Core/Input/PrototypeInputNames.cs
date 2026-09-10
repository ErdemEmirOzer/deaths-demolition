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

        public const string SchemeKeyboardMouse = "KeyboardMouse";
        public const string SchemeGamepad = "Gamepad";
    }
}
