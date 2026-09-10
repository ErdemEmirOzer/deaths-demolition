using System;
using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>Which device is currently driving the aim direction.</summary>
    public enum AimSourceKind
    {
        /// <summary>Mouse cursor. Aim is resolved by projecting the screen point onto the aim plane.</summary>
        Pointer = 0,

        /// <summary>Right stick. Aim is a direction vector, not a world point.</summary>
        Stick = 1
    }

    /// <summary>
    /// The only input surface production code is allowed to touch.
    ///
    /// Nothing outside Assets/_Prototype/Input may reference UnityEngine.InputSystem.
    /// The concrete implementation lives there (G-02); everything else - player,
    /// weapons, camera, pause, debug panel - talks to this interface through
    /// <see cref="InputSourceRegistry.Current"/> or an injected reference.
    ///
    /// Semantics:
    ///   * Movement and aim are fully independent. Reading one never affects the other.
    ///   * "Pressed" / "Released" are edge flags valid for the frame they were raised in.
    ///   * "Held" is a level flag.
    ///   * Values stay valid while the game is paused (Time.timeScale == 0), because
    ///     pause menus and the debug panel still need input.
    /// </summary>
    public interface IInputSource
    {
        /// <summary>False while input is suspended, e.g. during a scene transition.</summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Raw movement axis, magnitude clamped to 1. Camera-relative resolution is the
        /// consumer's job, not this interface's.
        /// </summary>
        Vector2 Move { get; }

        /// <summary>Which device supplied the most recent aim intent.</summary>
        AimSourceKind AimKind { get; }

        /// <summary>Pointer position in screen pixels. Meaningful when AimKind == Pointer.</summary>
        Vector2 AimScreenPosition { get; }

        /// <summary>Right-stick vector, magnitude clamped to 1. Meaningful when AimKind == Stick.</summary>
        Vector2 AimStick { get; }

        /// <summary>Fire is being held. Hold-to-fire weapons read this.</summary>
        bool FireHeld { get; }

        /// <summary>Fire went down this frame.</summary>
        bool FirePressed { get; }

        /// <summary>Fire came up this frame.</summary>
        bool FireReleased { get; }

        /// <summary>Reload was pressed this frame.</summary>
        bool ReloadPressed { get; }

        /// <summary>Pause was pressed this frame.</summary>
        bool PausePressed { get; }

        /// <summary>Debug panel toggle (F1) was pressed this frame.</summary>
        bool DebugPanelPressed { get; }

        /// <summary>Fires when the player switches between mouse and gamepad aiming.</summary>
        event Action<AimSourceKind> AimKindChanged;

        /// <summary>Start listening. Safe to call when already enabled.</summary>
        void Enable();

        /// <summary>Stop listening and clear all held state. Safe to call when already disabled.</summary>
        void Disable();
    }
}
