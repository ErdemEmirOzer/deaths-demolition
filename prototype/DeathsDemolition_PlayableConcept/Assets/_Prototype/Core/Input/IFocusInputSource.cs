namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// WP-069 B2 · the Focus / ADS control (right mouse button, gamepad LT): top-down "focus"
    /// narrows the cone and slows the step, third / first person aim down the sights. On its own
    /// interface (the <see cref="IExtendedInputSource"/> pattern) so every existing test double
    /// keeps compiling; consumers read it through <see cref="FocusInputExtensions.FocusHeld"/>.
    /// </summary>
    public interface IFocusInputSource
    {
        /// <summary>Focus / ADS is held.</summary>
        bool FocusHeld { get; }
    }

    public static class FocusInputExtensions
    {
        /// <summary>False for a source without the Focus control.</summary>
        public static bool FocusHeld(this IInputSource source)
        {
            return source is IFocusInputSource f && f.FocusHeld;
        }
    }
}
