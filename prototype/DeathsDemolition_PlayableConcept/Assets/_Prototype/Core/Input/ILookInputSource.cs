using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// WP-070: look input for the third- and first-person camera modes, on its own
    /// interface (the <see cref="IExtendedInputSource"/> pattern) so every existing
    /// <see cref="IInputSource"/> test double keeps compiling. Consumers read it through
    /// <see cref="LookInputExtensions"/>, which answers "no look" for a source that does
    /// not implement it. Raw values only: sensitivity, invert-Y and dead zone curves are
    /// applied by the camera (CameraModeSettings), never here.
    /// </summary>
    public interface ILookInputSource : IInputSource
    {
        /// <summary>Mouse movement this frame in screen pixels (x right, y up). Zero on gamepad frames.</summary>
        Vector2 LookDelta { get; }

        /// <summary>Right-stick look vector, magnitude clamped to 1, dead zone already applied.</summary>
        Vector2 LookStick { get; }

        /// <summary>
        /// Third-person shoulder swap went down this frame (keyboard V, mouse middle button,
        /// gamepad right-stick press; not remappable, like Look — Q is Special).
        /// </summary>
        bool ShoulderSwapPressed { get; }
    }

    /// <summary>Null-safe readers for <see cref="ILookInputSource"/>.</summary>
    public static class LookInputExtensions
    {
        public static Vector2 ReadLookDelta(this IInputSource source)
        {
            return source is ILookInputSource l ? l.LookDelta : Vector2.zero;
        }

        public static bool ReadShoulderSwapPressed(this IInputSource source)
        {
            return source is ILookInputSource l && l.ShoulderSwapPressed;
        }

        /// <summary>Falls back to <see cref="IInputSource.AimStick"/> (same physical stick) for a source without look support.</summary>
        public static Vector2 ReadLookStick(this IInputSource source)
        {
            if (source is ILookInputSource l)
            {
                return l.LookStick;
            }

            return source != null ? source.AimStick : Vector2.zero;
        }
    }
}
