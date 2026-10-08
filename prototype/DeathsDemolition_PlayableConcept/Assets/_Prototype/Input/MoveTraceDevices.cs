using UnityEngine;
using UnityEngine.InputSystem;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// WP-087 R3 · the raw devices the move trace records (Diagnostics/MoveTraceRecorder, <c>-ddMoveTrace</c>). It lives in Input/,
    /// the only folder that reads the Input System (ADR-024-2 / WP-059 parity — InputParityContractTests.InputSystem_YalnizInputKlasorunde).
    /// Besides W / A / S / D it reads the arrow keys and the gamepad's left stick (gt SHOULD-3, 07.10: a drifting stick or an arrow key
    /// must be told apart from "the character moves on its own").
    /// </summary>
    public static class MoveTraceDevices
    {
        /// <summary>
        /// <paramref name="keys"/>: bits 0–3 W A S D, bits 4–7 Up Left Down Right; the keyboard's and the gamepad's last Input System
        /// update time (−1 without the device); the left stick as the device reports it (no deadzone).
        /// </summary>
        public static void Read(out byte keys, out double keyboardLast, out Vector2 stick, out double gamepadLast)
        {
            keys = 0;
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed) keys |= 1;
                if (kb.aKey.isPressed) keys |= 2;
                if (kb.sKey.isPressed) keys |= 4;
                if (kb.dKey.isPressed) keys |= 8;
                if (kb.upArrowKey.isPressed) keys |= 16;
                if (kb.leftArrowKey.isPressed) keys |= 32;
                if (kb.downArrowKey.isPressed) keys |= 64;
                if (kb.rightArrowKey.isPressed) keys |= 128;
            }

            keyboardLast = kb != null ? kb.lastUpdateTime : -1.0;
            Gamepad pad = Gamepad.current;
            stick = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            gamepadLast = pad != null ? pad.lastUpdateTime : -1.0;
        }
    }
}
