using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// WP-024: the controls added for full gamepad parity, kept on a second interface so
    /// the many <see cref="IInputSource"/> test doubles written since WP-002 keep
    /// compiling. Production consumers read them through
    /// <see cref="InputSourceExtensions"/>, which returns "not pressed" for a source
    /// that does not implement this interface.
    /// </summary>
    public interface IExtendedInputSource : IInputSource
    {
        /// <summary>Special action is being held (dedicated control; the Special Weapon itself still fires with Fire).</summary>
        bool SpecialHeld { get; }

        /// <summary>Special action went down this frame.</summary>
        bool SpecialPressed { get; }

        /// <summary>Interact went down this frame.</summary>
        bool InteractPressed { get; }

        /// <summary>
        /// Which control scheme the most recent actuation came from. Unlike
        /// <see cref="IInputSource.AimKind"/> this also flips on button presses, so a UI
        /// can show the right glyphs.
        /// </summary>
        InputSchemeKind ActiveScheme { get; }

        /// <summary>Stick deadzone currently applied by the adapter (radial, 0..1). Readout for the F1 panel and tests.</summary>
        float StickDeadzone { get; }
    }

    /// <summary>The two control schemes of DD_Controls.</summary>
    public enum InputSchemeKind
    {
        KeyboardMouse = 0,
        Gamepad = 1
    }

    /// <summary>Null-safe readers for the WP-024 controls on any <see cref="IInputSource"/>.</summary>
    public static class InputSourceExtensions
    {
        public static bool SpecialHeld(this IInputSource source)
        {
            return source is IExtendedInputSource e && e.SpecialHeld;
        }

        public static bool SpecialPressed(this IInputSource source)
        {
            return source is IExtendedInputSource e && e.SpecialPressed;
        }

        public static bool InteractPressed(this IInputSource source)
        {
            return source is IExtendedInputSource e && e.InteractPressed;
        }

        /// <summary>Falls back to the aim kind for a source without scheme tracking.</summary>
        public static InputSchemeKind ActiveScheme(this IInputSource source)
        {
            if (source is IExtendedInputSource e)
            {
                return e.ActiveScheme;
            }

            return source != null && source.AimKind == AimSourceKind.Stick ? InputSchemeKind.Gamepad : InputSchemeKind.KeyboardMouse;
        }
    }
}
