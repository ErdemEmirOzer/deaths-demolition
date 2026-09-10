namespace DD.Prototype.Core.Boot
{
    /// <summary>
    /// Scene names used by the meta flow:
    /// Boot -> Main Menu -> Skin and Weapon Selection -> Run -> Run Results -> Main Menu.
    ///
    /// Boot is the only scene this task owns; the rest are created by later tasks and
    /// must use exactly these names.
    /// </summary>
    public static class PrototypeScenes
    {
        public const string Boot = "Boot";
        public const string MainMenu = "MainMenu";
        public const string Selection = "Selection";
        public const string Run = "Run";
        public const string Results = "Results";
    }
}
